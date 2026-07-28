using CoreBancario.Dominio.Ledger;
using Microsoft.Extensions.Logging;
using TransferenciaAgregado = CoreBancario.Dominio.Ledger.Transferencia;

namespace CoreBancario.Aplicacao.Transferencias;

public enum ResultadoLiquidacao
{
    Liquidada,
    JaLiquidada,
}

/// <summary>
/// Caso de uso consumido pelo Worker ao processar uma mensagem: resolve os nomes de titular a
/// partir do ledger, constrói a <see cref="TransferenciaAgregado"/> e registra o par de
/// lançamentos. Reentrega da mensagem é absorvida como sucesso, não como erro — a violação do
/// índice único de idempotência significa "já liquidada", e tratá-la como falha mandaria uma
/// transferência corretamente liquidada para o fluxo de retry/DLQ.
/// </summary>
public sealed class LiquidarTransferencia(
    IResolucaoDeContraparteRepositorio resolucao,
    IRegistroDeLiquidacaoRepositorio registro,
    ILogger<LiquidarTransferencia> log)
{
    public async Task<ResultadoLiquidacao> ExecutarAsync(
        SolicitacaoDeTransferencia solicitacao, CancellationToken cancellationToken)
    {
        log.LogInformation("Consumindo mensagem da transferência {LiquidacaoId}.", solicitacao.LiquidacaoId);

        var nomes = await resolucao.ResolverAsync(
            solicitacao.ContaOrigem, solicitacao.ContaDestino, cancellationToken);

        var nomeOrigem = ResolverNome(nomes, solicitacao.ContaOrigem);
        var nomeDestino = ResolverNome(nomes, solicitacao.ContaDestino);

        var transferencia = TransferenciaAgregado.Solicitar(
            solicitacao.ContaOrigem,
            solicitacao.ContaDestino,
            valorDebito: new Dinheiro(-solicitacao.Valor.Valor, solicitacao.Valor.Moeda),
            valorCredito: solicitacao.Valor);

        var liquidacao = transferencia.Liquidar(solicitacao.LiquidacaoId, nomeOrigem, nomeDestino);

        var resultadoRegistro = await registro.RegistrarAsync(liquidacao, cancellationToken);

        if (resultadoRegistro == ResultadoRegistro.JaRegistrada)
        {
            log.LogInformation(
                "Reentrega absorvida: transferência {LiquidacaoId} já estava liquidada.",
                solicitacao.LiquidacaoId);
            return ResultadoLiquidacao.JaLiquidada;
        }

        log.LogInformation(
            "Transferência {LiquidacaoId} liquidada: débito {DebitoId}, crédito {CreditoId}.",
            solicitacao.LiquidacaoId, liquidacao.Debito.Id, liquidacao.Credito.Id);
        return ResultadoLiquidacao.Liquidada;
    }

    private static string ResolverNome(IReadOnlyDictionary<Dominio.Identidades.ContaId, string> nomes, Dominio.Identidades.ContaId contaId) =>
        nomes.TryGetValue(contaId, out var nome) ? nome : GeradorDeNomeDeTitular.Gerar(contaId);
}
