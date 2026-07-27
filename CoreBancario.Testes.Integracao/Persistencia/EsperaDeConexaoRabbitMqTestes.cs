using CoreBancario.Infraestrutura.Mensageria;
using CoreBancario.Testes.Integracao.Infraestrutura;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.RabbitMq;

namespace CoreBancario.Testes.Integracao.Persistencia;

/// <summary>
/// Espera de inicialização aplicada à primeira conexão com o RabbitMQ: mesmo tratamento que
/// <c>MigracaoInicializacao</c> já dava ao banco, agora simétrico para o broker.
/// </summary>
[Collection(nameof(TransferenciaColecaoDeTestes))]
public class EsperaDeConexaoRabbitMqTestes(RabbitMqFixture rabbit)
{
    [Fact]
    public async Task BrokerSobeDentroDoOrcamento_ConexaoEhConcluida()
    {
        var ct = TestContext.Current.CancellationToken;

        // Container próprio, com porta fixa conhecida antes mesmo de o broker subir — reproduz o
        // processo iniciado "antes" do broker, sem depender de reiniciar o container da fixture
        // compartilhada (o encaminhamento de porta do Docker Desktop sob WSL2 é lento para
        // reconectar após um restart, o que tornaria o teste sobre isso, não sobre a espera).
        const int porta = 25772;
        await using var container = new RabbitMqBuilder("rabbitmq:4-management")
            .WithUsername("corebancario")
            .WithPassword("corebancario")
            .WithPortBinding(porta, 5672)
            .Build();

        var connectionString = $"amqp://corebancario:corebancario@localhost:{porta}";

        var tarefaDeConexao = ConexaoRabbitMqInicializacao.AbrirComEsperaAsync(
            connectionString,
            NullLogger.Instance,
            intervaloEntreTentativas: TimeSpan.FromSeconds(1),
            maximoTentativas: 60,
            cancellationToken: ct);

        await container.StartAsync(ct);

        await using var conexao = await tarefaDeConexao;
        Assert.True(conexao.IsOpen);
    }

    [Fact]
    public async Task BrokerNuncaSobe_FalhaComErroQueIdentificaACausa()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = rabbit.ConnectionString;
        await rabbit.PararAsync(ct);

        try
        {
            var excecao = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ConexaoRabbitMqInicializacao.AbrirComEsperaAsync(
                    connectionString,
                    NullLogger.Instance,
                    intervaloEntreTentativas: TimeSpan.FromMilliseconds(100),
                    maximoTentativas: 3,
                    cancellationToken: ct));

            Assert.Contains("RabbitMQ", excecao.Message);
            Assert.NotNull(excecao.InnerException);
        }
        finally
        {
            await rabbit.IniciarAsync(ct);
        }
    }
}
