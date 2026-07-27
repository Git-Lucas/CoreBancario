using CoreBancario.Aplicacao.Transferencias;
using CoreBancario.Dominio.Identidades;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CoreBancario.Infraestrutura.Persistencia;

/// <summary>
/// D10 em design.md: uma única consulta com DISTINCT ON resolve as duas contas de uma vez,
/// atendida pelo índice `ix_lancamentos_contraparte` sem varredura sequencial.
/// </summary>
public sealed class ResolucaoDeContraparteRepositorio(CoreBancarioDbContext contexto) : IResolucaoDeContraparteRepositorio
{
    private const string Sql =
        """
        SELECT DISTINCT ON (contraparte_id) contraparte_id, contraparte_nome
          FROM lancamentos
         WHERE contraparte_id IN (@origem, @destino);
        """;

    public async Task<IReadOnlyDictionary<ContaId, string>> ResolverAsync(
        ContaId contaOrigem, ContaId contaDestino, CancellationToken cancellationToken)
    {
        await contexto.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var conexao = (NpgsqlConnection)contexto.Database.GetDbConnection();
            await using var comando = new NpgsqlCommand(Sql, conexao);
            comando.Parameters.Add(new NpgsqlParameter("origem", NpgsqlDbType.Uuid) { Value = contaOrigem.Valor });
            comando.Parameters.Add(new NpgsqlParameter("destino", NpgsqlDbType.Uuid) { Value = contaDestino.Valor });

            var resolvidos = new Dictionary<ContaId, string>();
            await using var leitor = await comando.ExecuteReaderAsync(cancellationToken);
            while (await leitor.ReadAsync(cancellationToken))
            {
                resolvidos[new ContaId(leitor.GetGuid(0))] = leitor.GetString(1);
            }

            return resolvidos;
        }
        finally
        {
            await contexto.Database.CloseConnectionAsync();
        }
    }
}
