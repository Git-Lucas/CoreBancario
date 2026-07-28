using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;
using Microsoft.Extensions.Logging;

namespace CoreBancario.Aplicacao.Transferencias;

/// <summary>
/// Caso de uso consumido pela API. Validação estrutural — não de existência, pois não há cadastro
/// de contas — seguida da geração do `liquidacao_id` e da publicação confirmada pelo broker.
/// </summary>
public sealed class SolicitarTransferencia(
    IPublicadorDeTransferencia publicador, ILogger<SolicitarTransferencia> log)
{
    // Moeda é constante de sistema, como em ConsultaDeExtrato: nunca informada pelo solicitante.
    private static readonly Moeda MoedaDoSistema = Moeda.BRL;

    public async Task<ResultadoSolicitacaoTransferencia> ExecutarAsync(
        ComandoSolicitarTransferencia comando, CancellationToken cancellationToken)
    {
        // Validação estrutural antes de qualquer id existir: uma solicitação rejeitada aqui não
        // tem liquidacao_id para correlacionar — não há o que abrir escopo ainda.
        if (comando.ContaOrigem == Guid.Empty || comando.ContaDestino == Guid.Empty)
        {
            return Rejeitar("Identificador de conta malformado.");
        }

        if (comando.ContaOrigem == comando.ContaDestino)
        {
            return Rejeitar("Conta de origem não pode ser igual à conta de destino.");
        }

        if (comando.Valor <= 0)
        {
            return Rejeitar("Valor da transferência deve ser positivo.");
        }

        Dinheiro valor;
        try
        {
            valor = new Dinheiro(comando.Valor, MoedaDoSistema);
        }
        catch (ArgumentException ex)
        {
            return Rejeitar(ex.Message);
        }

        var liquidacaoId = LiquidacaoId.Nova();

        // Escopo de log aberto assim que o liquidacao_id existe, cobrindo o restante do
        // tratamento da solicitação (recebimento, publicação/falha) — é o identificador que
        // permite localizar a transferência inteira nos logs da API e do Worker.
        using var escopo = log.BeginScope(new Dictionary<string, object> { ["LiquidacaoId"] = liquidacaoId.Valor });

        log.LogInformation(
            "Solicitação de transferência recebida: origem {ContaOrigem}, destino {ContaDestino}, valor {Valor}.",
            comando.ContaOrigem, comando.ContaDestino, comando.Valor);

        var solicitacao = new SolicitacaoDeTransferencia(
            liquidacaoId, new ContaId(comando.ContaOrigem), new ContaId(comando.ContaDestino), valor);

        var resultado = await publicador.PublicarAsync(solicitacao, cancellationToken);

        if (!resultado.Confirmada)
        {
            log.LogError("Falha ao publicar transferência: {Motivo}", resultado.MotivoDaFalha);
            return new ResultadoSolicitacaoTransferencia.FalhaAoPublicar(
                resultado.MotivoDaFalha ?? "Falha desconhecida ao publicar.");
        }

        log.LogInformation("Transferência publicada e confirmada pelo broker.");
        return new ResultadoSolicitacaoTransferencia.Aceita(liquidacaoId);
    }

    private ResultadoSolicitacaoTransferencia.RejeitadaNaValidacao Rejeitar(string motivo)
    {
        log.LogWarning("Solicitação de transferência rejeitada na validação: {Motivo}", motivo);
        return new ResultadoSolicitacaoTransferencia.RejeitadaNaValidacao(motivo);
    }
}
