namespace CoreBancario.Aplicacao.Extrato;

/// <summary>Linha devolvida ao cliente. O identificador interno da contraparte e o da liquidação
/// não aparecem — o primeiro vazaria o id de outra conta, o segundo é irrelevante para quem lê o
/// extrato.</summary>
public sealed record LinhaExtrato(
    string Id,
    DateTimeOffset DataHora,
    Sentido Sentido,
    decimal Valor,
    string Contraparte,
    string Descricao);
