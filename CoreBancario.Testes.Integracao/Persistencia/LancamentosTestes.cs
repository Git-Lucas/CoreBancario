using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;
using CoreBancario.Testes.Integracao.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoreBancario.Testes.Integracao.Persistencia;

[Collection(nameof(BancoColecaoDeTestes))]
public class LancamentosTestes(PostgreSqlFixture fixture) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(fixture.TruncateAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Update_LancamentoExistente_EhRejeitadoPeloBanco()
    {
        var ct = TestContext.Current.CancellationToken;
        var lancamento = ConstrutorDeTeste.NovoLancamento();
        await using (var contexto = fixture.CriarContexto())
        {
            contexto.Lancamentos.Add(lancamento);
            await contexto.SaveChangesAsync(ct);
        }

        await using var conexao = new NpgsqlConnection(fixture.ConnectionString);
        await conexao.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("UPDATE lancamentos SET valor = 999.00 WHERE id = @id", conexao);
        comando.Parameters.AddWithValue("id", lancamento.Id.Valor);

        var excecao = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync(ct));
        Assert.Contains("append-only", excecao.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Delete_LancamentoExistente_EhRejeitadoPeloBanco()
    {
        var ct = TestContext.Current.CancellationToken;
        var lancamento = ConstrutorDeTeste.NovoLancamento();
        await using (var contexto = fixture.CriarContexto())
        {
            contexto.Lancamentos.Add(lancamento);
            await contexto.SaveChangesAsync(ct);
        }

        await using var conexao = new NpgsqlConnection(fixture.ConnectionString);
        await conexao.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("DELETE FROM lancamentos WHERE id = @id", conexao);
        comando.Parameters.AddWithValue("id", lancamento.Id.Valor);

        var excecao = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync(ct));
        Assert.Contains("append-only", excecao.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Truncate_EhPermitidoEEsvaziaATabela()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var contexto = fixture.CriarContexto();
        contexto.Lancamentos.Add(ConstrutorDeTeste.NovoLancamento());
        await contexto.SaveChangesAsync(ct);

        await using var conexao = new NpgsqlConnection(fixture.ConnectionString);
        await conexao.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("TRUNCATE lancamentos", conexao);
        await comando.ExecuteNonQueryAsync(ct);

        var quantidade = await contexto.Lancamentos.CountAsync(ct);
        Assert.Equal(0, quantidade);
    }

    [Fact]
    public async Task Insert_SemId_Falha()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conexao = new NpgsqlConnection(fixture.ConnectionString);
        await conexao.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(
            """
            INSERT INTO lancamentos (conta_id, liquidacao_id, contraparte_id, contraparte_nome, moeda, valor)
            VALUES (@contaId, @liquidacaoId, @contraparteId, 'Fulano', 'BRL', 10.00)
            """,
            conexao);
        comando.Parameters.AddWithValue("contaId", Guid.NewGuid());
        comando.Parameters.AddWithValue("liquidacaoId", Guid.NewGuid());
        comando.Parameters.AddWithValue("contraparteId", Guid.NewGuid());

        var excecao = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync(ct));
        Assert.Equal("23502", excecao.SqlState); // not_null_violation
    }

    [Fact]
    public async Task DataCriacao_EhPreenchidaPeloBancoEIgualAoInstanteDoId()
    {
        var ct = TestContext.Current.CancellationToken;
        var lancamento = ConstrutorDeTeste.NovoLancamento();
        await using var contexto = fixture.CriarContexto();
        contexto.Lancamentos.Add(lancamento);
        await contexto.SaveChangesAsync(ct);

        await using var conexao = new NpgsqlConnection(fixture.ConnectionString);
        await conexao.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("SELECT data_criacao FROM lancamentos WHERE id = @id", conexao);
        comando.Parameters.AddWithValue("id", lancamento.Id.Valor);
        await using var leitor = await comando.ExecuteReaderAsync(ct);
        await leitor.ReadAsync(ct);
        var dataCriacao = leitor.GetFieldValue<DateTimeOffset>(0);

        Assert.Equal(lancamento.Id.Instante, dataCriacao);
    }

    [Fact]
    public async Task InserirLancamentoDeLiquidacaoJaRegistrada_ViolaIndiceUnico()
    {
        var ct = TestContext.Current.CancellationToken;
        var liquidacao = Liquidacao.Registrar(
            LiquidacaoId.Nova(),
            contaDebito: ContaId.Nova(),
            nomeContaDebito: "Fulano",
            contaCredito: ContaId.Nova(),
            nomeContaCredito: "Beltrano",
            valorDebito: new Dinheiro(100m, Moeda.BRL),
            valorCredito: new Dinheiro(100m, Moeda.BRL));

        await using (var contexto = fixture.CriarContexto())
        {
            contexto.Lancamentos.AddRange(liquidacao.Debito, liquidacao.Credito);
            await contexto.SaveChangesAsync(ct);
        }

        // Mesma (liquidacao_id, conta_id) do débito original, id de lançamento novo — simula
        // a reentrega de uma mensagem que o PRD-2 vai desduplicar por este índice.
        var debitoDuplicado = new Lancamento(
            LancamentoId.Nova(),
            liquidacao.Debito.ContaId,
            liquidacao.Id,
            liquidacao.Debito.Valor,
            liquidacao.Debito.ContraparteId,
            liquidacao.Debito.ContraparteNome);

        await using var outroContexto = fixture.CriarContexto();
        outroContexto.Lancamentos.Add(debitoDuplicado);

        var excecao = await Assert.ThrowsAsync<DbUpdateException>(() => outroContexto.SaveChangesAsync(ct));
        Assert.IsType<PostgresException>(excecao.InnerException);
        Assert.Equal("23505", ((PostgresException)excecao.InnerException).SqlState); // unique_violation
    }

    [Fact]
    public async Task Valor_SobreviveAoRoundtripSemPerdaDePrecisao()
    {
        var ct = TestContext.Current.CancellationToken;
        var valorOriginal = new Dinheiro(123456789012345.67m, Moeda.BRL);
        var lancamento = ConstrutorDeTeste.NovoLancamento(valor: valorOriginal);

        await using (var contexto = fixture.CriarContexto())
        {
            contexto.Lancamentos.Add(lancamento);
            await contexto.SaveChangesAsync(ct);
        }

        await using var outroContexto = fixture.CriarContexto();
        var recarregado = await outroContexto.Lancamentos
            .AsNoTracking()
            .FirstAsync(l => l.Id == lancamento.Id, ct);

        Assert.Equal(valorOriginal, recarregado.Valor);
    }

    [Fact]
    public async Task OrderById_CorrespondeAOrdemDeCriacaoDasIdentidadesV7()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = new List<LancamentoId>();
        await using (var contexto = fixture.CriarContexto())
        {
            for (var i = 0; i < 50; i++)
            {
                var lancamento = ConstrutorDeTeste.NovoLancamento();
                ids.Add(lancamento.Id);
                contexto.Lancamentos.Add(lancamento);

                // Um milissegundo distinto por id: evita a não-monotonicidade conhecida do
                // Guid.CreateVersion7() dentro do mesmo ms (ver design.md D1).
                await Task.Delay(1, ct);
            }

            await contexto.SaveChangesAsync(ct);
        }

        await using var conexao = new NpgsqlConnection(fixture.ConnectionString);
        await conexao.OpenAsync(ct);
        await using var comando = new NpgsqlCommand("SELECT id FROM lancamentos ORDER BY id", conexao);
        await using var leitor = await comando.ExecuteReaderAsync(ct);

        var lidos = new List<Guid>();
        while (await leitor.ReadAsync(ct))
        {
            lidos.Add(leitor.GetGuid(0));
        }

        Assert.Equal(ids.Select(id => id.Valor), lidos);
    }
}
