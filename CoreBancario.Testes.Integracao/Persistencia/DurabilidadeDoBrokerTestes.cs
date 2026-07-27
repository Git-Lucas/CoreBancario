using CoreBancario.Aplicacao.Transferencias;
using CoreBancario.Infraestrutura.Mensageria;
using CoreBancario.Testes.Integracao.Infraestrutura;
using RabbitMQ.Client;

namespace CoreBancario.Testes.Integracao.Persistencia;

/// <summary>Durabilidade sob restart do broker com mensagem pendente.</summary>
[Collection(nameof(TransferenciaColecaoDeTestes))]
public class DurabilidadeDoBrokerTestes(PostgreSqlFixture postgres, RabbitMqFixture rabbit) : IAsyncLifetime
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
    public async Task ReiniciarBrokerComMensagemPendente_NaoPerdeATransferencia()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var conexaoPublicacao = await rabbit.CriarConexaoAsync())
        {
            var ambiente = new AmbienteDeTransferencia(postgres, conexaoPublicacao);
            var comando = new ComandoSolicitarTransferencia(Guid.NewGuid(), Guid.NewGuid(), 77.77m);
            var aceita = Assert.IsType<ResultadoSolicitacaoTransferencia.Aceita>(
                await ambiente.NovoCasoDeSolicitacao().ExecutarAsync(comando, ct));

            // Mensagem publicada e confirmada, mas nunca consumida — fica pendente na fila
            // durável quando o broker cai.
            await rabbit.PararAsync(ct);
            await rabbit.IniciarAsync(ct);

            await using var novaConexao = await rabbit.CriarConexaoAsync();
            await using var canal = await novaConexao.CreateChannelAsync(cancellationToken: ct);

            var mensagem = await EsperaDeMensageria.ReceberComEsperaAsync(
                canal, TopologiaDeMensageria.FilaPrincipal, ct, tentativasMaximas: 100);
            var solicitacao = AmbienteDeTransferencia.Desserializar(mensagem.Body);
            Assert.Equal(aceita.LiquidacaoId, solicitacao.LiquidacaoId);

            var resultadoLiquidacao = await new AmbienteDeTransferencia(postgres, novaConexao).LiquidarAsync(solicitacao, ct);
            Assert.Equal(ResultadoLiquidacao.Liquidada, resultadoLiquidacao);
            await canal.BasicAckAsync(mensagem.DeliveryTag, multiple: false, ct);
        }
    }
}
