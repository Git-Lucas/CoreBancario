using Npgsql;

namespace CoreBancario.Testes.Integracao.Infraestrutura;

/// <summary>
/// Conecta à instância de desenvolvimento semeada com 1,2M lançamentos (D15 em design.md — o
/// container Testcontainers não semeia esse volume; os testes de plano/custo rodam contra o
/// banco de desenvolvimento). Se a conexão ou a massa esperada não estiverem disponíveis, os
/// testes que dependem desta fixture pulam com motivo explícito em vez de falhar.
/// </summary>
public sealed class BancoSemeadoFixture : IAsyncLifetime
{
    private const string VariavelDeAmbiente = "ConnectionStrings__CoreBancarioSemeada";

    private const string ConnectionStringPadrao =
        "Host=localhost;Port=5432;Database=corebancario;Username=corebancario;Password=corebancario";

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable(VariavelDeAmbiente) ?? ConnectionStringPadrao;

    public bool Disponivel { get; private set; }

    public Guid ContaMonstro { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            await using var conexao = new NpgsqlConnection(ConnectionString);
            await conexao.OpenAsync();

            await using var comando = new NpgsqlCommand(
                "SELECT conta_id FROM lancamentos GROUP BY conta_id HAVING count(*) = 250000 LIMIT 1", conexao);
            var resultado = await comando.ExecuteScalarAsync();

            if (resultado is Guid contaId)
            {
                ContaMonstro = contaId;
                Disponivel = true;
            }
        }
        catch (NpgsqlException)
        {
            Disponivel = false;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task<NpgsqlConnection> AbrirConexaoAsync()
    {
        var conexao = new NpgsqlConnection(ConnectionString);
        await conexao.OpenAsync();
        return conexao;
    }
}

[CollectionDefinition(nameof(BancoSemeadoColecaoDeTestes))]
public class BancoSemeadoColecaoDeTestes : ICollectionFixture<BancoSemeadoFixture>;
