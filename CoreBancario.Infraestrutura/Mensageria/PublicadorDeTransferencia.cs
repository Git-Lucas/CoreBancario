using System.Text.Json;
using CoreBancario.Aplicacao.Transferencias;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace CoreBancario.Infraestrutura.Mensageria;

/// <summary>
/// D11 em design.md: o `202` é uma promessa irrevogável, então nunca é emitido antes do
/// publisher confirm. Um canal novo por publicação evita ter que sincronizar o uso concorrente
/// de um único `IChannel` entre requisições — otimização de vazão é não-objetivo explícito.
/// </summary>
public sealed class PublicadorDeTransferencia(IConnection conexao, ILogger<PublicadorDeTransferencia> log)
    : IPublicadorDeTransferencia
{
    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan TimeoutDaPublicacao = TimeSpan.FromSeconds(5);

    public async Task<ResultadoPublicacao> PublicarAsync(
        SolicitacaoDeTransferencia solicitacao, CancellationToken cancellationToken)
    {
        var mensagem = new MensagemTransferencia(
            solicitacao.LiquidacaoId.Valor,
            solicitacao.ContaOrigem.Valor,
            solicitacao.ContaDestino.Valor,
            solicitacao.Valor.Valor,
            solicitacao.Valor.Moeda.ToString());

        var corpo = JsonSerializer.SerializeToUtf8Bytes(mensagem, OpcoesJson);

        var propriedades = new BasicProperties
        {
            Persistent = true,
            MessageId = solicitacao.LiquidacaoId.Valor.ToString(),
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeoutDaPublicacao);

        try
        {
            await using var canal = await conexao.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                cts.Token);

            await canal.BasicPublishAsync(
                TopologiaDeMensageria.Exchange,
                TopologiaDeMensageria.RoutingKey,
                mandatory: true,
                basicProperties: propriedades,
                body: corpo,
                cancellationToken: cts.Token);

            return ResultadoPublicacao.Sucesso();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogError(
                ex, "Timeout aguardando o publisher confirm da transferência {LiquidacaoId}.", solicitacao.LiquidacaoId);
            return ResultadoPublicacao.Falha("Timeout aguardando confirmação do broker.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao publicar a transferência {LiquidacaoId}.", solicitacao.LiquidacaoId);
            return ResultadoPublicacao.Falha(ex.Message);
        }
    }
}
