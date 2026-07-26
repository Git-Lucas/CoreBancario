using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;

namespace CoreBancario.Aplicacao.Transferencias;

/// <summary>O que o solicitante informou (D3/D12 em design.md) — a mensagem não carrega nomes.</summary>
public sealed record SolicitacaoDeTransferencia(
    LiquidacaoId LiquidacaoId,
    ContaId ContaOrigem,
    ContaId ContaDestino,
    Dinheiro Valor);

public sealed record ResultadoPublicacao(bool Confirmada, string? MotivoDaFalha)
{
    public static ResultadoPublicacao Sucesso() => new(true, null);

    public static ResultadoPublicacao Falha(string motivo) => new(false, motivo);
}

/// <summary>
/// Port (driven): publicação confirmada pelo broker (D11 em design.md) — o retorno só é
/// bem-sucedido depois do publisher confirm, nunca em fire-and-forget.
/// </summary>
public interface IPublicadorDeTransferencia
{
    Task<ResultadoPublicacao> PublicarAsync(SolicitacaoDeTransferencia solicitacao, CancellationToken cancellationToken);
}
