using CoreBancario.Dominio.Identidades;

namespace CoreBancario.Dominio.Ledger;

/// <summary>
/// Partida dobrada: não é uma tabela nem uma entidade persistida — existe apenas para produzir,
/// de forma correlacionada, o par débito/crédito que compõe uma liquidação.
/// </summary>
public sealed class Liquidacao
{
    public LiquidacaoId Id { get; }
    public Lancamento Debito { get; }
    public Lancamento Credito { get; }

    private Liquidacao(LiquidacaoId id, Lancamento debito, Lancamento credito)
    {
        Id = id;
        Debito = debito;
        Credito = credito;
    }

    public static Liquidacao Registrar(
        LiquidacaoId id,
        ContaId contaDebito,
        string nomeContaDebito,
        ContaId contaCredito,
        string nomeContaCredito,
        Dinheiro valorDebito,
        Dinheiro valorCredito)
    {
        if (valorDebito.Moeda != valorCredito.Moeda)
        {
            throw new InvalidOperationException(
                $"Débito ({valorDebito.Moeda}) e crédito ({valorCredito.Moeda}) devem ser da mesma moeda.");
        }

        if (Math.Abs(valorDebito.Valor) != Math.Abs(valorCredito.Valor))
        {
            throw new InvalidOperationException(
                "Liquidação desbalanceada: débito e crédito devem ter o mesmo valor absoluto.");
        }

        var magnitude = Math.Abs(valorDebito.Valor);
        var moeda = valorDebito.Moeda;

        var debito = new Lancamento(
            LancamentoId.Nova(), contaDebito, id, new Dinheiro(-magnitude, moeda), contaCredito, nomeContaCredito);

        var credito = new Lancamento(
            LancamentoId.Nova(), contaCredito, id, new Dinheiro(magnitude, moeda), contaDebito, nomeContaDebito);

        return new Liquidacao(id, debito, credito);
    }
}
