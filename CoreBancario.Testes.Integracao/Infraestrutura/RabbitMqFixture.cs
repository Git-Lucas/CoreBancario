using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace CoreBancario.Testes.Integracao.Infraestrutura;

/// <summary>
/// Um RabbitMQ 4.x descartável por execução da suíte, reaproveitando o padrão de
/// <see cref="PostgreSqlFixture"/>: compartilhado entre os testes da coleção, não recriado
/// por teste — a topologia é declarada por cada teste que precisa dela.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername("corebancario")
        .WithPassword("corebancario")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public Task<IConnection> CriarConexaoAsync() =>
        new ConnectionFactory { Uri = new Uri(ConnectionString) }.CreateConnectionAsync();

    /// <summary>Usado pelo teste de durabilidade (9.4): reinicia o broker com mensagens pendentes.</summary>
    public Task PararAsync(CancellationToken cancellationToken = default) => _container.StopAsync(cancellationToken);

    public Task IniciarAsync(CancellationToken cancellationToken = default) => _container.StartAsync(cancellationToken);
}

[CollectionDefinition(nameof(TransferenciaColecaoDeTestes))]
public class TransferenciaColecaoDeTestes : ICollectionFixture<PostgreSqlFixture>, ICollectionFixture<RabbitMqFixture>;
