using CoreBancario.Dominio.Identidades;

namespace CoreBancario.Dominio.Ledger;

/// <summary>
/// Comando de transferência validado: existe para impedir, no domínio, a construção
/// de uma transferência desbalanceada ou multi-moeda, antes de o Worker conhecer o
/// <see cref="LiquidacaoId"/> e os nomes de titular necessários para produzir a <see cref="Ledger.Liquidacao"/>.
/// </summary>
public sealed class Transferencia
{
    public ContaId ContaOrigem { get; }
    public ContaId ContaDestino { get; }
    public Dinheiro Valor { get; }

    private Transferencia(ContaId contaOrigem, ContaId contaDestino, Dinheiro valor)
    {
        ContaOrigem = contaOrigem;
        ContaDestino = contaDestino;
        Valor = valor;
    }

    public static Transferencia Solicitar(
        ContaId contaOrigem,
        ContaId contaDestino,
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
                "Transferência desbalanceada: débito e crédito devem ter o mesmo valor absoluto.");
        }

        var magnitude = Math.Abs(valorDebito.Valor);
        return new Transferencia(contaOrigem, contaDestino, new Dinheiro(magnitude, valorDebito.Moeda));
    }

    /// <summary>
    /// Produz o par de lançamentos via a <see cref="Ledger.Liquidacao"/> já existente, uma vez
    /// conhecidos o identificador da liquidação e os nomes resolvidos.
    /// </summary>
    public Liquidacao Liquidar(LiquidacaoId id, string nomeContaOrigem, string nomeContaDestino) =>
        Liquidacao.Registrar(
            id,
            contaDebito: ContaOrigem,
            nomeContaDebito: nomeContaOrigem,
            contaCredito: ContaDestino,
            nomeContaCredito: nomeContaDestino,
            valorDebito: new Dinheiro(-Valor.Valor, Valor.Moeda),
            valorCredito: Valor);
}
