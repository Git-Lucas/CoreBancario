using CoreBancario.Testes.Integracao.Infraestrutura;
using Npgsql;
using NpgsqlTypes;

namespace CoreBancario.Testes.Integracao.Persistencia;

/// <summary>
/// Plano de execução da resolução de nome de contraparte sobre a massa de 1,2M. Pula com motivo
/// explícito quando o banco de desenvolvimento semeado não está disponível.
/// </summary>
[Collection(nameof(BancoSemeadoColecaoDeTestes))]
public class PlanoDeExecucaoContraparteTestes(BancoSemeadoFixture fixture)
{
    private const string SqlResolucao =
        """
        SELECT DISTINCT ON (contraparte_id) contraparte_id, contraparte_nome
          FROM lancamentos
         WHERE contraparte_id IN (@origem, @destino)
        """;

    private const string MotivoDoSkip =
        "Banco de desenvolvimento semeado (1,2M lançamentos) indisponível — rode o comando de seed contra a instância de dev antes destes testes.";

    [Fact]
    public async Task ResolucaoDeContraparte_UsaIndexOnlyScanSemVarredura()
    {
        if (!fixture.Disponivel)
        {
            Assert.Skip(MotivoDoSkip);
        }

        var ct = TestContext.Current.CancellationToken;
        await using var conexao = await fixture.AbrirConexaoAsync();

        var resultado = await ExplainAnalyzeApoio.ExecutarAsync(
            conexao,
            SqlResolucao,
            [
                new NpgsqlParameter("origem", NpgsqlDbType.Uuid) { Value = fixture.ContaMonstro },
                new NpgsqlParameter("destino", NpgsqlDbType.Uuid) { Value = fixture.ContaMonstro },
            ],
            ct);

        Assert.Equal("Index Only Scan", resultado.TipoDeNo);
        Assert.Equal(0, resultado.HeapFetches);
        Assert.NotEqual("Seq Scan", resultado.TipoDeNo);
    }
}
