using System.Diagnostics;
using System.Text.Json;
using CoreBancario.Aplicacao.Transferencias;
using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;
using CoreBancario.Infraestrutura.Mensageria;
using CoreBancario.Testes.Integracao.Infraestrutura;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

namespace CoreBancario.Testes.Integracao.Persistencia;

/// <summary>
/// Teste narrow que comprova exatamente o mecanismo que sustenta o trace distribuído: a
/// instrumentação oficial do cliente injeta o contexto no cabeçalho da publicação e o
/// extrai no consumo. Não exercita o Worker nem abre span de processamento — isso é o span de
/// `RabbitMQ.Client.Publisher`/`RabbitMQ.Client.Subscriber` da própria biblioteca. Se essa camada
/// quebrar, o span de processamento (encadeado a partir do mesmo `ContextExtractor`) quebra junto.
/// </summary>
[Collection(nameof(TransferenciaColecaoDeTestes))]
public sealed class TraceDistribuidoTestes(RabbitMqFixture rabbit) : IAsyncLifetime
{
    private IConnection _conexao = null!;

    public async ValueTask InitializeAsync()
    {
        _conexao = await rabbit.CriarConexaoAsync();
        await TopologiaDeMensageria.DeclararAsync(_conexao);

        await using var canal = await _conexao.CreateChannelAsync();
        await canal.QueuePurgeAsync(TopologiaDeMensageria.FilaPrincipal);
    }

    public async ValueTask DisposeAsync()
    {
        await _conexao.DisposeAsync();
    }

    [Fact]
    public async Task ContextoDeTraceAtravessaAPublicacaoEOConsumo_ComOCorpoDaMensagemSemMetadadoDeTrace()
    {
        var ct = TestContext.Current.CancellationToken;

        var atividadesExportadas = new List<Activity>();
        using var provedorDeRastreamento = Sdk.CreateTracerProviderBuilder()
            .AddRabbitMQInstrumentation()
            .AddInMemoryExporter(atividadesExportadas)
            .Build();

        using var origemDoTeste = new ActivitySource("Teste.TraceDistribuido");
        using var listenerDoTeste = new ActivityListener
        {
            ShouldListenTo = fonte => fonte.Name == origemDoTeste.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listenerDoTeste);

        var publicador = new PublicadorDeTransferencia(_conexao, NullLogger<PublicadorDeTransferencia>.Instance);
        var solicitacao = new SolicitacaoDeTransferencia(
            LiquidacaoId.Nova(), ContaId.Nova(), ContaId.Nova(), new Dinheiro(10.00m, Moeda.BRL));

        ActivityTraceId traceIdDaPublicacao;
        using (var atividadeRaiz = origemDoTeste.StartActivity("solicitar-transferencia"))
        {
            traceIdDaPublicacao = atividadeRaiz!.TraceId;
            var resultado = await publicador.PublicarAsync(solicitacao, ct);
            Assert.True(resultado.Confirmada);
        }

        await using var canal = await _conexao.CreateChannelAsync(cancellationToken: ct);
        var entrega = await EsperaDeMensageria.ReceberComEsperaAsync(canal, ct);
        await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, ct);

        provedorDeRastreamento.ForceFlush();

        var atividadesDoTrace = atividadesExportadas.Where(a => a.TraceId == traceIdDaPublicacao).ToList();

        var atividadeDePublicacao = Assert.Single(
            atividadesDoTrace, a => a.Source.Name == RabbitMQActivitySource.PublisherSourceName);
        var atividadeDeConsumo = Assert.Single(
            atividadesDoTrace, a => a.Source.Name == RabbitMQActivitySource.SubscriberSourceName);

        Assert.Equal(traceIdDaPublicacao, atividadeDePublicacao.TraceId);
        Assert.Equal(traceIdDaPublicacao, atividadeDeConsumo.TraceId);

        using var corpo = JsonDocument.Parse(entrega.Body.ToArray());
        var camposDoCorpo = corpo.RootElement.EnumerateObject().Select(p => p.Name);
        Assert.DoesNotContain(camposDoCorpo, campo => campo.Contains("trace", StringComparison.OrdinalIgnoreCase));
    }
}
