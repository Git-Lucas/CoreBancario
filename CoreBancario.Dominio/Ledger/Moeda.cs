namespace CoreBancario.Dominio.Ledger;

/// <summary>
/// Sistema mono-moeda: apenas BRL circula. USD existe só para exercitar em teste a invariante
/// de Dinheiro que rejeita operar moedas diferentes (PRD-1 C1.3) — não há conversão nem taxa.
/// </summary>
public enum Moeda
{
    BRL,
    USD,
}
