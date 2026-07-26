namespace CoreBancario.Aplicacao.Extrato;

/// <summary>Linha devolvida ao cliente. Contraparte e liquidação não aparecem (PRD-1 C1.11).</summary>
public sealed record LinhaExtrato(
    string Id,
    DateTimeOffset DataHora,
    Sentido Sentido,
    decimal Valor,
    string Contraparte,
    string Descricao);
