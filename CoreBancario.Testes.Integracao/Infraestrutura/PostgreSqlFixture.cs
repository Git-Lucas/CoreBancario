using CoreBancario.Infraestrutura.Persistencia;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CoreBancario.Testes.Integracao.Infraestrutura;

/// <summary>
/// Um PostgreSQL 18 descartável por execução da suíte, com o esquema criado pelas próprias
/// migrations — compartilhado entre os testes da coleção via <see cref="BancoColecaoDeTestes"/>,
/// não recriado por teste.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("corebancario")
        .WithUsername("corebancario")
        .WithPassword("corebancario")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using var contexto = CriarContexto();
        await contexto.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public CoreBancarioDbContext CriarContexto(ContadorDeComandosInterceptor? interceptor = null)
    {
        var construtor = new DbContextOptionsBuilder<CoreBancarioDbContext>()
            .UseNpgsql(ConnectionString);

        if (interceptor is not null)
        {
            construtor.AddInterceptors(interceptor);
        }

        return new CoreBancarioDbContext(construtor.Options);
    }

    public async Task TruncateAsync()
    {
        await using var conexao = new NpgsqlConnection(ConnectionString);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand("TRUNCATE lancamentos", conexao);
        await comando.ExecuteNonQueryAsync();
    }

    /// <summary>Usado pelo teste de indisponibilidade de banco.</summary>
    public Task PararAsync(CancellationToken cancellationToken = default) => _container.StopAsync(cancellationToken);

    public Task IniciarAsync(CancellationToken cancellationToken = default) => _container.StartAsync(cancellationToken);
}

[CollectionDefinition(nameof(BancoColecaoDeTestes))]
public class BancoColecaoDeTestes : ICollectionFixture<PostgreSqlFixture>;
