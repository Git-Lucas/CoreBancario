using System.Diagnostics;
using CoreBancario.Aplicacao.Transferencias;
using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;
using CoreBancario.Infraestrutura.Mensageria;
using CoreBancario.Testes.Integracao.Infraestrutura;
using CoreBancario.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

namespace CoreBancario.Testes.Integracao.Persistencia;

/// <summary>
/// Comprova o ponto que o design (D6) chama de mais provável de falhar em toda a mudança: que o
/// span de processamento aberto por <c>ConsumidorDeTransferencias</c> — a partir do contexto
/// extraído do cabeçalho, não do contexto ambiente — é o ancestral direto dos spans de banco da
/// liquidação. Reproduz a mesma sequência de <c>ProcessarAsync</c>, com <see cref="AmbienteDeTransferencia"/>
/// no lugar da liquidação real do Worker, contra Postgres e RabbitMQ via Testcontainers.
/// </summary>
[Collection(nameof(TransferenciaColecaoDeTestes))]
public sealed class SpanDeProcessamentoTestes(PostgreSqlFixture postgres, RabbitMqFixture rabbit) : IAsyncLifetime
{
    private IConnection _conexao = null!;
    private AmbienteDeTransferencia _ambiente = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.TruncateAsync();

        _conexao = await rabbit.CriarConexaoAsync();
        await TopologiaDeMensageria.DeclararAsync(_conexao);

        await using var canal = await _conexao.CreateChannelAsync();
        await canal.QueuePurgeAsync(TopologiaDeMensageria.FilaPrincipal);

        _ambiente = new AmbienteDeTransferencia(postgres, _conexao);
    }

    public async ValueTask DisposeAsync()
    {
        await _conexao.DisposeAsync();
    }

    [Fact]
    public async Task SpansDeBancoDaLiquidacao_SaoDescendentesDoSpanDeProcessamento_NoMesmoTraceDaPublicacao()
    {
        var ct = TestContext.Current.CancellationToken;

        var atividadesExportadas = new List<Activity>();
        using var provedorDeRastreamento = Sdk.CreateTracerProviderBuilder()
            .AddRabbitMQInstrumentation()
            .AddNpgsql()
            .AddSource(InstrumentacaoDoWorker.NomeDoServico)
            .AddInMemoryExporter(atividadesExportadas)
            .Build();

        var publicador = new PublicadorDeTransferencia(_conexao, NullLogger<PublicadorDeTransferencia>.Instance);
        var solicitacao = new SolicitacaoDeTransferencia(
            LiquidacaoId.Nova(), ContaId.Nova(), ContaId.Nova(), new Dinheiro(25.00m, Moeda.BRL));
        Assert.True((await publicador.PublicarAsync(solicitacao, ct)).Confirmada);

        await using var canal = await _conexao.CreateChannelAsync(cancellationToken: ct);
        var entrega = await EsperaDeMensageria.ReceberComEsperaAsync(canal, ct);

        // Mesma sequência de ConsumidorDeTransferencias.ProcessarAsync: extrai do cabeçalho,
        // nunca do contexto ambiente, e abre o span de processamento a partir dele.
        var contextoExtraido = RabbitMQActivitySource.ContextExtractor(entrega.BasicProperties);
        Activity? spanDeProcessamento;
        using (spanDeProcessamento = InstrumentacaoDoWorker.ActivitySource.StartActivity(
            "ProcessarTransferencia", ActivityKind.Internal, contextoExtraido))
        {
            Assert.NotNull(spanDeProcessamento);
            var resultado = await _ambiente.LiquidarAsync(solicitacao, ct);
            Assert.Equal(ResultadoLiquidacao.Liquidada, resultado);
        }

        await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, ct);
        provedorDeRastreamento.ForceFlush();

        var spansDeBanco = atividadesExportadas.Where(a => a.Source.Name == "Npgsql").ToList();
        Assert.NotEmpty(spansDeBanco);

        Assert.All(spansDeBanco, span =>
        {
            Assert.Equal(spanDeProcessamento.TraceId, span.TraceId);
            Assert.Equal(spanDeProcessamento.SpanId, span.ParentSpanId);
        });

        // O trace inteiro — publicação, extração e escrita no banco — é um único trace.
        var atividadeDePublicacao = Assert.Single(
            atividadesExportadas, a => a.Source.Name == RabbitMQActivitySource.PublisherSourceName);
        Assert.Equal(atividadeDePublicacao.TraceId, spanDeProcessamento.TraceId);
    }
}
