using CoreBancario.Testes.Integracao.Infraestrutura;
using Npgsql;
using NpgsqlTypes;

namespace CoreBancario.Testes.Integracao.Persistencia;

/// <summary>
/// Testes de plano de execução e custo de acesso sobre a massa de 1,2M (D15 em design.md).
/// Pulam com motivo explícito quando o banco de desenvolvimento semeado não está disponível —
/// não é o Testcontainers narrow que sustenta esse volume.
/// </summary>
[Collection(nameof(BancoSemeadoColecaoDeTestes))]
public class PlanoDeExecucaoExtratoTestes(BancoSemeadoFixture fixture)
{
    private static readonly Guid PisoMinimo = Guid.Empty;
    private static readonly Guid TetoMaximo = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private const string SqlKeyset =
        """
        SELECT l.id, l.valor, l.contraparte_nome
        FROM lancamentos AS l
        WHERE l.conta_id = @conta AND l.id >= @piso AND l.id < @teto
        ORDER BY l.id DESC
        LIMIT 51
        """;

    private const string SqlOffset =
        """
        SELECT l.id, l.valor, l.contraparte_nome
        FROM lancamentos AS l
        WHERE l.conta_id = @conta
        ORDER BY l.id DESC
        OFFSET @deslocamento LIMIT 50
        """;

    [Fact]
    public async Task ConsultaDeExtrato_EhIndexOnlyScanComHeapFetchesZero()
    {
        if (!fixture.Disponivel)
        {
            Assert.Skip(MotivoDoSkip);
        }

        var ct = TestContext.Current.CancellationToken;
        await using var conexao = await fixture.AbrirConexaoAsync();

        var resultado = await ExplainAnalyzeApoio.ExecutarAsync(
            conexao, SqlKeyset, ParametrosKeyset(fixture.ContaMonstro, PisoMinimo, TetoMaximo), ct);

        Assert.Equal("Index Only Scan", resultado.TipoDeNo);
        Assert.Equal(0, resultado.HeapFetches);
    }

    [Fact]
    public async Task ConsultaDeExtrato_SemIndiceDeCobertura_PlanoMuda()
    {
        if (!fixture.Disponivel)
        {
            Assert.Skip(MotivoDoSkip);
        }

        var ct = TestContext.Current.CancellationToken;
        await using var conexao = await fixture.AbrirConexaoAsync();
        await using var transacao = await conexao.BeginTransactionAsync(ct);

        await using (var drop = new NpgsqlCommand("DROP INDEX ix_lancamentos_extrato", conexao, transacao))
        {
            await drop.ExecuteNonQueryAsync(ct);
        }

        var resultado = await ExplainAnalyzeApoio.ExecutarAsync(
            conexao, SqlKeyset, ParametrosKeyset(fixture.ContaMonstro, PisoMinimo, TetoMaximo), ct, transacao);

        Assert.NotEqual("Index Only Scan", resultado.TipoDeNo);

        await transacao.RollbackAsync(ct);
    }

    [Fact]
    public async Task ConsultaDeExtrato_FiltroDePeriodo_ApareceComoIndexCondSemFilterResidual()
    {
        if (!fixture.Disponivel)
        {
            Assert.Skip(MotivoDoSkip);
        }

        var ct = TestContext.Current.CancellationToken;
        await using var conexao = await fixture.AbrirConexaoAsync();

        var piso = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddMonths(-6));
        var teto = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddMonths(-3));

        var resultado = await ExplainAnalyzeApoio.ExecutarAsync(
            conexao, SqlKeyset, ParametrosKeyset(fixture.ContaMonstro, piso, teto), ct);

        Assert.NotNull(resultado.IndexCond);
        Assert.Contains("id", resultado.IndexCond, StringComparison.Ordinal);
        Assert.Null(resultado.Filter);
    }

    [Fact]
    public async Task PaginaProfunda_BuffersRespeitamOLimiarCalibradoEmRelacaoAPagina1()
    {
        if (!fixture.Disponivel)
        {
            Assert.Skip(MotivoDoSkip);
        }

        var ct = TestContext.Current.CancellationToken;
        await using var conexao = await fixture.AbrirConexaoAsync();

        var pagina1 = await ExplainAnalyzeApoio.ExecutarAsync(
            conexao, SqlKeyset, ParametrosKeyset(fixture.ContaMonstro, PisoMinimo, TetoMaximo), ct);

        var cursorPagina5000 = await ObterCursorAsync(conexao, fixture.ContaMonstro, pagina: 5000, ct);
        var pagina5000 = await ExplainAnalyzeApoio.ExecutarAsync(
            conexao, SqlKeyset, ParametrosKeyset(fixture.ContaMonstro, PisoMinimo, cursorPagina5000), ct);

        // Limiar absoluto, não percentual (D10/PRD-1 calibrados): os valores de keyset são
        // pequenos demais (~5 buffers) para um percentual não virar teste instável.
        Assert.True(
            pagina5000.Buffers <= pagina1.Buffers + 10,
            $"página 5000 leu {pagina5000.Buffers} buffers, página 1 leu {pagina1.Buffers}");
    }

    [Fact]
    public async Task VarianteComOffset_LeOrdensDeMagnitudeMaisBuffersNaMesmaPagina()
    {
        if (!fixture.Disponivel)
        {
            Assert.Skip(MotivoDoSkip);
        }

        var ct = TestContext.Current.CancellationToken;
        await using var conexao = await fixture.AbrirConexaoAsync();

        var cursorPagina5000 = await ObterCursorAsync(conexao, fixture.ContaMonstro, pagina: 5000, ct);
        var keyset = await ExplainAnalyzeApoio.ExecutarAsync(
            conexao, SqlKeyset, ParametrosKeyset(fixture.ContaMonstro, PisoMinimo, cursorPagina5000), ct);

        var offset = await ExplainAnalyzeApoio.ExecutarAsync(
            conexao, SqlOffset, ParametrosOffset(fixture.ContaMonstro, 249950), ct);

        Assert.True(
            offset.Buffers >= keyset.Buffers * 100,
            $"OFFSET leu {offset.Buffers} buffers, keyset leu {keyset.Buffers} (esperado >= 100x)");
    }

    private const string MotivoDoSkip =
        "Banco de desenvolvimento semeado (1,2M lançamentos) indisponível — rode o comando de seed contra a instância de dev antes destes testes.";

    private static async Task<Guid> ObterCursorAsync(NpgsqlConnection conexao, Guid contaId, int pagina, CancellationToken ct)
    {
        var deslocamento = ((pagina - 1) * 50) - 1;
        await using var comando = new NpgsqlCommand(
            "SELECT id FROM lancamentos WHERE conta_id = @conta ORDER BY id DESC OFFSET @deslocamento LIMIT 1", conexao);
        comando.Parameters.Add(new NpgsqlParameter("conta", NpgsqlDbType.Uuid) { Value = contaId });
        comando.Parameters.Add(new NpgsqlParameter("deslocamento", NpgsqlDbType.Integer) { Value = deslocamento });
        return (Guid)(await comando.ExecuteScalarAsync(ct))!;
    }

    private static List<NpgsqlParameter> ParametrosKeyset(Guid conta, Guid piso, Guid teto) =>
    [
        new NpgsqlParameter("conta", NpgsqlDbType.Uuid) { Value = conta },
        new NpgsqlParameter("piso", NpgsqlDbType.Uuid) { Value = piso },
        new NpgsqlParameter("teto", NpgsqlDbType.Uuid) { Value = teto },
    ];

    private static List<NpgsqlParameter> ParametrosOffset(Guid conta, int deslocamento) =>
    [
        new NpgsqlParameter("conta", NpgsqlDbType.Uuid) { Value = conta },
        new NpgsqlParameter("deslocamento", NpgsqlDbType.Integer) { Value = deslocamento },
    ];
}
