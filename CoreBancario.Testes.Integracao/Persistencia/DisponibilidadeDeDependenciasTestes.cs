using CoreBancario.Aplicacao.Transferencias;
using CoreBancario.Infraestrutura.Mensageria;
using CoreBancario.Testes.Integracao.Infraestrutura;
using Microsoft.EntityFrameworkCore;

namespace CoreBancario.Testes.Integracao.Persistencia;

/// <summary>
/// Disponibilidade cruzada das duas dependências (9.9/9.10 em tasks.md): o caminho de
/// solicitação não depende do banco (D1/D3 em design.md), e a liquidação não depende de o
/// broker estar disponível no momento em que o banco volta.
/// </summary>
[Collection(nameof(TransferenciaColecaoDeTestes))]
public class DisponibilidadeDeDependenciasTestes(PostgreSqlFixture postgres, RabbitMqFixture rabbit) : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await postgres.TruncateAsync();

        await using var conexao = await rabbit.CriarConexaoAsync();
        await TopologiaDeMensageria.DeclararAsync(conexao);

        await using var canal = await conexao.CreateChannelAsync();
        await canal.QueuePurgeAsync(TopologiaDeMensageria.FilaPrincipal);
        await canal.QueuePurgeAsync(TopologiaDeMensageria.FilaDeDescartes);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task BrokerIndisponivel_SolicitacaoValidaEhRecusadaSemAceitacao()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conexao = await rabbit.CriarConexaoAsync();
        var ambiente = new AmbienteDeTransferencia(postgres, conexao);

        try
        {
            await rabbit.PararAsync(ct);

            var comando = new ComandoSolicitarTransferencia(Guid.NewGuid(), Guid.NewGuid(), 10.00m);
            var resultado = await ambiente.NovoCasoDeSolicitacao().ExecutarAsync(comando, ct);

            Assert.IsType<ResultadoSolicitacaoTransferencia.FalhaAoPublicar>(resultado);
        }
        finally
        {
            await rabbit.IniciarAsync(ct);
        }
    }

    [Fact]
    public async Task BancoIndisponivel_ApiAceitaEATransferenciaLiquidaQuandoBancoRetorna()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conexao = await rabbit.CriarConexaoAsync();
        var ambiente = new AmbienteDeTransferencia(postgres, conexao);

        var comando = new ComandoSolicitarTransferencia(Guid.NewGuid(), Guid.NewGuid(), 15.00m);
        var aceita = Assert.IsType<ResultadoSolicitacaoTransferencia.Aceita>(
            await ambiente.NovoCasoDeSolicitacao().ExecutarAsync(comando, ct));

        try
        {
            await postgres.PararAsync(ct);

            await using var canal = await conexao.CreateChannelAsync(cancellationToken: ct);
            var primeiraEntrega = await EsperaDeMensageria.ReceberComEsperaAsync(canal, ct);
            var solicitacao = AmbienteDeTransferencia.Desserializar(primeiraEntrega.Body);

            // Banco fora do ar: a liquidação falha (exceção de conectividade), e o Worker
            // devolveria a mensagem à fila — aqui, replicado manualmente.
            await Assert.ThrowsAnyAsync<Exception>(() => ambiente.LiquidarAsync(solicitacao, ct));
            await canal.BasicRejectAsync(primeiraEntrega.DeliveryTag, requeue: true, ct);

            await postgres.IniciarAsync(ct);
            await AguardarPostgresDisponivelAsync(ambiente, ct);

            var segundaEntrega = await EsperaDeMensageria.ReceberComEsperaAsync(canal, ct);
            var resultadoLiquidacao = await ambiente.LiquidarAsync(
                AmbienteDeTransferencia.Desserializar(segundaEntrega.Body), ct);

            Assert.Equal(ResultadoLiquidacao.Liquidada, resultadoLiquidacao);
            await canal.BasicAckAsync(segundaEntrega.DeliveryTag, multiple: false, ct);

            await using var contexto = ambiente.NovoContexto();
            var quantidade = await contexto.Lancamentos.CountAsync(l => l.LiquidacaoId == aceita.LiquidacaoId, ct);
            Assert.Equal(2, quantidade);
        }
        finally
        {
            await postgres.IniciarAsync(ct);
        }
    }

    private static async Task AguardarPostgresDisponivelAsync(AmbienteDeTransferencia ambiente, CancellationToken ct)
    {
        for (var tentativa = 0; tentativa < 50; tentativa++)
        {
            try
            {
                await using var contexto = ambiente.NovoContexto();
                if (await contexto.Database.CanConnectAsync(ct))
                {
                    return;
                }
            }
            catch (Exception)
            {
                // Ainda subindo — tenta de novo.
            }

            await Task.Delay(500, ct);
        }

        throw new InvalidOperationException("PostgreSQL não voltou a responder após a espera.");
    }
}
