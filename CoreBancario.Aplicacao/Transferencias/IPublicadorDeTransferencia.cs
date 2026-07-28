using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;

namespace CoreBancario.Aplicacao.Transferencias;

/// <summary>O que o solicitante informou — a mensagem não carrega nomes de titular, porque nome
/// não é dado do comando de transferência, é preenchimento do ledger feito pelo Worker ao
/// liquidar.</summary>
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
/// Port (driven): publicação confirmada pelo broker — o retorno só é bem-sucedido depois do
/// publisher confirm, nunca em fire-and-forget. Sem essa confirmação, o `202` devolvido ao
/// cliente seria uma promessa que o broker poderia ter aceitado e perdido antes do fsync.
/// </summary>
public interface IPublicadorDeTransferencia
{
    Task<ResultadoPublicacao> PublicarAsync(SolicitacaoDeTransferencia solicitacao, CancellationToken cancellationToken);
}
