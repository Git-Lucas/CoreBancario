namespace CoreBancario.Aplicacao.Extrato;

/// <summary>Envelope da resposta: contaId, moeda, período e cursor não se repetem por linha.</summary>
public sealed record PaginaExtrato(
    Guid ContaId,
    string Moeda,
    DateTimeOffset De,
    DateTimeOffset Ate,
    IReadOnlyList<LinhaExtrato> Lancamentos,
    string? ProximoCursor);
