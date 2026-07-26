using CoreBancario.Dominio.Identidades;

namespace CoreBancario.Dominio.Ledger;

/// <summary>
/// Append-only: nenhum setter, nenhum método que altere estado após a construção.
/// A imutabilidade é reforçada pelo banco por trigger — ver migration da tabela lancamentos.
/// </summary>
public sealed class Lancamento
{
    // Setters privados (não públicos) só para o EF Core materializar via reflexão — não há
    // caminho público de mutação. O construtor sem parâmetros, também privado, existe pelo
    // mesmo motivo e nunca produz uma instância válida por si só.
    public LancamentoId Id { get; private set; }
    public ContaId ContaId { get; private set; }
    public LiquidacaoId LiquidacaoId { get; private set; }
    public Dinheiro Valor { get; private set; }
    public ContaId ContraparteId { get; private set; }
    public string ContraparteNome { get; private set; } = string.Empty;

    private Lancamento()
    {
    }

    public Lancamento(
        LancamentoId id,
        ContaId contaId,
        LiquidacaoId liquidacaoId,
        Dinheiro valor,
        ContaId contraparteId,
        string contraparteNome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contraparteNome);

        Id = id;
        ContaId = contaId;
        LiquidacaoId = liquidacaoId;
        Valor = valor;
        ContraparteId = contraparteId;
        ContraparteNome = contraparteNome;
    }
}
