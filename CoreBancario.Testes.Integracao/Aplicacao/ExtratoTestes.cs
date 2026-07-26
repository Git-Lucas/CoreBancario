using CoreBancario.Aplicacao.Extrato;
using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;
using CoreBancario.Infraestrutura.Persistencia;
using CoreBancario.Testes.Integracao.Infraestrutura;

namespace CoreBancario.Testes.Integracao.Aplicacao;

[Collection(nameof(BancoColecaoDeTestes))]
public class ExtratoTestes(PostgreSqlFixture fixture) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(fixture.TruncateAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ConsultarPagina_EmiteExatamenteUmComandoSql()
    {
        var ct = TestContext.Current.CancellationToken;
        var contaId = ContaId.Nova();
        await SemearAsync(contaId, 10, ct);

        var interceptor = new ContadorDeComandosInterceptor();
        await using var contexto = fixture.CriarContexto(interceptor);
        var caso = new ConsultaDeExtrato(new ConsultaDeExtratoRepositorio(contexto));

        await caso.ExecutarAsync(contaId.Valor, Ha30Dias, Amanha, null, ct);

        Assert.Equal(1, interceptor.Contagem);
    }

    [Fact]
    public async Task ConsultarPagina_ColunasSelecionadasEstaoNoConjuntoCoberto()
    {
        var ct = TestContext.Current.CancellationToken;
        var contaId = ContaId.Nova();
        await SemearAsync(contaId, 5, ct);

        var interceptor = new ContadorDeComandosInterceptor();
        await using var contexto = fixture.CriarContexto(interceptor);
        var caso = new ConsultaDeExtrato(new ConsultaDeExtratoRepositorio(contexto));

        await caso.ExecutarAsync(contaId.Valor, Ha30Dias, Amanha, null, ct);

        var sql = interceptor.UltimoComandoTexto;
        Assert.NotNull(sql);
        Assert.DoesNotContain("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
        // O INCLUDE do índice cobre valor e contraparte_nome; moeda, liquidacao_id,
        // contraparte_id e data_criacao ficam fora — se aparecerem no SELECT, o índice deixou
        // de cobrir a consulta.
        Assert.DoesNotContain("moeda", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("liquidacao_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contraparte_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data_criacao", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConsultarPagina_NenhumaEntidadeFicaRastreada()
    {
        var ct = TestContext.Current.CancellationToken;
        var contaId = ContaId.Nova();
        await SemearAsync(contaId, 5, ct);

        await using var contexto = fixture.CriarContexto();
        var caso = new ConsultaDeExtrato(new ConsultaDeExtratoRepositorio(contexto));

        await caso.ExecutarAsync(contaId.Valor, Ha30Dias, Amanha, null, ct);

        Assert.Empty(contexto.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Paginacao_NavegacaoConsecutiva_NaoRepeteNemOmiteLancamentos()
    {
        var ct = TestContext.Current.CancellationToken;
        var contaId = ContaId.Nova();
        var idsCriados = await SemearAsync(contaId, 120, ct);

        await using var contexto = fixture.CriarContexto();
        var caso = new ConsultaDeExtrato(new ConsultaDeExtratoRepositorio(contexto));

        var idsLidos = new List<string>();
        string? cursor = null;
        int paginas = 0;
        do
        {
            var pagina = await caso.ExecutarAsync(contaId.Valor, Ha30Dias, Amanha, cursor, ct);
            idsLidos.AddRange(pagina.Lancamentos.Select(l => l.Id));
            cursor = pagina.ProximoCursor;
            paginas++;
        }
        while (cursor is not null && paginas < 10);

        var idsEsperados = idsCriados.Select(CursorDeExtrato.Codificar).ToList();
        Assert.Equal(idsEsperados.Count, idsLidos.Count);
        Assert.Equal(idsEsperados.ToHashSet(), idsLidos.ToHashSet());
        Assert.Equal(3, paginas); // 120 / 50 = 2 páginas cheias + 1 página final de 20
    }

    [Fact]
    public async Task Cursor_Adulterado_NaoEscapaDoPeriodoInformado()
    {
        var ct = TestContext.Current.CancellationToken;
        var contaId = ContaId.Nova();

        // Um lançamento bem antigo (fora do período que será consultado) e vários dentro do
        // período — o cursor adulterado vai apontar para depois do lançamento antigo, tentando
        // fazer o teto do keyset "vazar" para fora do período pedido.
        var antigo = await InserirLancamentoAsync(contaId, DateTimeOffset.UtcNow.AddYears(-2), ct);
        await SemearAsync(contaId, 5, ct);

        var cursorAdulterado = CursorDeExtrato.Codificar(antigo);

        await using var contexto = fixture.CriarContexto();
        var caso = new ConsultaDeExtrato(new ConsultaDeExtratoRepositorio(contexto));

        var pagina = await caso.ExecutarAsync(contaId.Valor, Ha30Dias, Amanha, cursorAdulterado, ct);

        Assert.All(pagina.Lancamentos, l => Assert.True(l.DataHora >= Ha30Dias && l.DataHora < Amanha));
    }

    [Fact]
    public async Task ContaSemLancamentos_DevolveListaVazia()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var contexto = fixture.CriarContexto();
        var caso = new ConsultaDeExtrato(new ConsultaDeExtratoRepositorio(contexto));

        var pagina = await caso.ExecutarAsync(Guid.NewGuid(), Ha30Dias, Amanha, null, ct);

        Assert.Empty(pagina.Lancamentos);
        Assert.Null(pagina.ProximoCursor);
    }

    private static DateTimeOffset Ha30Dias => DateTimeOffset.UtcNow.AddDays(-30);

    private static DateTimeOffset Amanha => DateTimeOffset.UtcNow.AddDays(1);

    private async Task<List<LancamentoId>> SemearAsync(ContaId contaId, int quantidade, CancellationToken ct)
    {
        var ids = new List<LancamentoId>();
        await using var contexto = fixture.CriarContexto();
        for (var i = 0; i < quantidade; i++)
        {
            var lancamento = ConstrutorDeTeste.NovoLancamento(contaId: contaId);
            ids.Add(lancamento.Id);
            contexto.Lancamentos.Add(lancamento);
            await Task.Delay(1, ct);
        }

        await contexto.SaveChangesAsync(ct);
        return ids;
    }

    private async Task<LancamentoId> InserirLancamentoAsync(ContaId contaId, DateTimeOffset instante, CancellationToken ct)
    {
        var id = new LancamentoId(Guid.CreateVersion7(instante));
        var lancamento = new Lancamento(
            id, contaId, LiquidacaoId.Nova(), new Dinheiro(10m, Moeda.BRL), ContaId.Nova(), "Fulano");

        await using var contexto = fixture.CriarContexto();
        contexto.Lancamentos.Add(lancamento);
        await contexto.SaveChangesAsync(ct);
        return id;
    }
}
