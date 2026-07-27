namespace CoreBancario.Aplicacao.Extrato;

/// <summary>
/// Caso de uso consumido pela API. O cursor entra e sai como string opaca — quem decodifica
/// para identidade tipada, e quem deriva os campos não lidos do banco, é esta classe.
/// </summary>
public sealed class ConsultaDeExtrato(IConsultaDeExtratoRepositorio repositorio)
{
    // Moeda é constante de sistema (mono-moeda, sem conversão nem taxa): nunca lida do banco.
    private const string MoedaDoSistema = "BRL";

    public async Task<PaginaExtrato> ExecutarAsync(
        Guid contaId,
        DateTimeOffset de,
        DateTimeOffset ate,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var cursorDecodificado = CursorDeExtrato.Decodificar(cursor);

        var resultado = await repositorio.ConsultarAsync(contaId, de, ate, cursorDecodificado, cancellationToken);

        var linhas = resultado.Linhas.Select(Mapear).ToList();

        var proximoCursor = resultado.ProximoCursor is { } proximo
            ? CursorDeExtrato.Codificar(proximo)
            : null;

        return new PaginaExtrato(contaId, MoedaDoSistema, de, ate, linhas, proximoCursor);
    }

    private static LinhaExtrato Mapear(LinhaExtratoBruta linha)
    {
        var sentido = linha.Valor < 0 ? Sentido.Debito : Sentido.Credito;
        var descricao = sentido == Sentido.Debito
            ? $"Débito para {linha.ContraparteNome}"
            : $"Crédito de {linha.ContraparteNome}";

        return new LinhaExtrato(
            CursorDeExtrato.Codificar(linha.Id),
            linha.Id.Instante,
            sentido,
            linha.Valor,
            linha.ContraparteNome,
            descricao);
    }
}
