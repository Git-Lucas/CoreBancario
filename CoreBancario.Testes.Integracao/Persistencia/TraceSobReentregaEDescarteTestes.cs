using System.Diagnostics;
using CoreBancario.Infraestrutura.Mensageria;
using CoreBancario.Testes.Integracao.Infraestrutura;
using CoreBancario.Worker;
using OpenTelemetry;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

namespace CoreBancario.Testes.Integracao.Persistencia;

/// <summary>
/// A sobrevivência do trace à reentrega e ao descarte é o principal ganho desta mudança — é o
/// que torna "morta na fila de descartes" legível sem endpoint de status.
/// Reproduz, contra o broker real, a sequência que <c>ConsumidorDeTransferencias</c> e
/// <c>ConsumidorDeDescartes</c> fazem a cada entrega: extrair do cabeçalho e abrir span a partir
/// do contexto extraído — nunca do contexto ambiente.
/// </summary>
[Collection(nameof(TransferenciaColecaoDeTestes))]
public sealed class TraceSobReentregaEDescarteTestes(RabbitMqFixture rabbit) : IAsyncLifetime
{
    private IConnection _conexao = null!;

    public async ValueTask InitializeAsync()
    {
        _conexao = await rabbit.CriarConexaoAsync();
        await TopologiaDeMensageria.DeclararAsync(_conexao);

        await using var canal = await _conexao.CreateChannelAsync();
        await canal.QueuePurgeAsync(TopologiaDeMensageria.FilaPrincipal);
        await canal.QueuePurgeAsync(TopologiaDeMensageria.FilaDeDescartes);
    }

    public async ValueTask DisposeAsync()
    {
        await _conexao.DisposeAsync();
    }

    [Fact]
    public async Task TentativasSucessivasEDescarte_PermanecemNoMesmoTrace_ComLiquidacaoIdDoEnvelopePreservado()
    {
        var ct = TestContext.Current.CancellationToken;

        var atividadesExportadas = new List<Activity>();
        using var provedorDeRastreamento = Sdk.CreateTracerProviderBuilder()
            .AddRabbitMQInstrumentation()
            .AddSource(InstrumentacaoDoWorker.NomeDoServico)
            .AddInMemoryExporter(atividadesExportadas)
            .Build();

        await using var canal = await _conexao.CreateChannelAsync(cancellationToken: ct);

        var liquidacaoId = Guid.CreateVersion7().ToString();
        var propriedades = new BasicProperties { Persistent = true, MessageId = liquidacaoId };
        var corpoInvalido = "corpo inválido de propósito"u8.ToArray();

        ActivityTraceId traceIdDaPublicacao;
        using (var origemDoTeste = new ActivitySource("Teste.TraceSobReentrega"))
        {
            using var listenerDoTeste = new ActivityListener
            {
                ShouldListenTo = fonte => fonte.Name == origemDoTeste.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            };
            ActivitySource.AddActivityListener(listenerDoTeste);

            using var atividadeRaiz = origemDoTeste.StartActivity("solicitar-transferencia")!;
            traceIdDaPublicacao = atividadeRaiz.TraceId;

            await canal.BasicPublishAsync(
                TopologiaDeMensageria.Exchange, TopologiaDeMensageria.RoutingKey,
                mandatory: true, basicProperties: propriedades, body: corpoInvalido, cancellationToken: ct);
        }

        // x-delivery-limit=3: quatro tentativas (a original + três reentregas) antes do
        // dead-letter, cada uma abrindo o próprio span de processamento — mesma sequência de
        // ConsumidorDeTransferencias.ProcessarAsync.
        var spansDeTentativa = new List<Activity>();
        for (var tentativa = 0; tentativa < 4; tentativa++)
        {
            var entrega = await EsperaDeMensageria.ReceberComEsperaAsync(canal, ct);
            Assert.Equal(liquidacaoId, entrega.BasicProperties.MessageId);

            var contextoExtraido = RabbitMQActivitySource.ContextExtractor(entrega.BasicProperties);
            using var spanDeProcessamento = InstrumentacaoDoWorker.ActivitySource.StartActivity(
                "ProcessarTransferencia", ActivityKind.Internal, contextoExtraido);
            spanDeProcessamento?.SetTag("liquidacao_id", entrega.BasicProperties.MessageId);
            spanDeProcessamento?.SetStatus(ActivityStatusCode.Error, "falha forçada pelo teste");
            spansDeTentativa.Add(spanDeProcessamento!);

            await canal.BasicRejectAsync(entrega.DeliveryTag, requeue: true, ct);
        }

        var mensagemDescartada = await EsperaDeMensageria.ReceberComEsperaAsync(
            canal, TopologiaDeMensageria.FilaDeDescartes, ct);
        Assert.Equal(liquidacaoId, mensagemDescartada.BasicProperties.MessageId);

        var contextoDoDescarte = RabbitMQActivitySource.ContextExtractor(mensagemDescartada.BasicProperties);
        using var spanDeDescarte = InstrumentacaoDoWorker.ActivitySource.StartActivity(
            "RegistrarDescarte", ActivityKind.Internal, contextoDoDescarte);
        spanDeDescarte?.SetTag("liquidacao_id", mensagemDescartada.BasicProperties.MessageId);
        await canal.BasicAckAsync(mensagemDescartada.DeliveryTag, multiple: false, ct);

        provedorDeRastreamento.ForceFlush();

        Assert.Equal(4, spansDeTentativa.Count);
        Assert.All(spansDeTentativa, span => Assert.Equal(traceIdDaPublicacao, span.TraceId));
        Assert.NotNull(spanDeDescarte);
        Assert.Equal(traceIdDaPublicacao, spanDeDescarte.TraceId);

        // O `liquidacao_id` do descarte veio do envelope, não do corpo (que era inválido de
        // propósito) — o trace acrescenta informação, não substitui essa correlação (D9).
        Assert.Equal(liquidacaoId, spanDeDescarte.GetTagItem("liquidacao_id"));
    }
}
