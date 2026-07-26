using System.Text.Json;
using Npgsql;

namespace CoreBancario.Testes.Integracao.Infraestrutura;

/// <summary>
/// Resultado de EXPLAIN (ANALYZE, BUFFERS): buffers totais são lidos do nó raiz, que no
/// PostgreSQL já é cumulativo (inclui os filhos) — somar por nó dobraria a contagem. Tipo de
/// nó, Heap Fetches, Index Cond e Filter vêm do nó de acesso (o primeiro "*Scan*" encontrado).
/// </summary>
public sealed record ResultadoExplain(string TipoDeNo, int? HeapFetches, int Buffers, string? IndexCond, string? Filter);

public static class ExplainAnalyzeApoio
{
    public static async Task<ResultadoExplain> ExecutarAsync(
        NpgsqlConnection conexao,
        string sql,
        IEnumerable<NpgsqlParameter>? parametros,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transacao = null)
    {
        await using var comando = new NpgsqlCommand($"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) {sql}", conexao, transacao);
        if (parametros is not null)
        {
            foreach (var parametro in parametros)
            {
                comando.Parameters.Add(parametro);
            }
        }

        var resultado = await comando.ExecuteScalarAsync(cancellationToken);
        var json = (string)resultado!;

        using var documento = JsonDocument.Parse(json);
        var raiz = documento.RootElement[0].GetProperty("Plan");
        var noDeAcesso = EncontrarNoDeAcesso(raiz);

        var buffers = LerInt(raiz, "Shared Hit Blocks") + LerInt(raiz, "Shared Read Blocks");

        return new ResultadoExplain(
            noDeAcesso.GetProperty("Node Type").GetString()!,
            noDeAcesso.TryGetProperty("Heap Fetches", out var hf) ? hf.GetInt32() : null,
            buffers,
            noDeAcesso.TryGetProperty("Index Cond", out var ic) ? ic.GetString() : null,
            noDeAcesso.TryGetProperty("Filter", out var f) ? f.GetString() : null);
    }

    private static JsonElement EncontrarNoDeAcesso(JsonElement no)
    {
        var tipo = no.GetProperty("Node Type").GetString()!;
        if (tipo.Contains("Scan", StringComparison.Ordinal))
        {
            return no;
        }

        if (no.TryGetProperty("Plans", out var filhos) && filhos.GetArrayLength() > 0)
        {
            return EncontrarNoDeAcesso(filhos[0]);
        }

        return no;
    }

    private static int LerInt(JsonElement no, string propriedade) =>
        no.TryGetProperty(propriedade, out var valor) ? valor.GetInt32() : 0;
}
