using System.Diagnostics;
using System.Text;
using CoreBancario.Infraestrutura.Mensageria;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CoreBancario.Worker;

/// <summary>
/// Consumidor da DLQ: sem endpoint de status, este log estruturado é a única observabilidade de
/// uma transferência morta. Drena em vez de acumular — sacrifica o re-drive automatizado (fora de
/// escopo; o corpo bruto fica no log e permite reenvio manual) em troca de nunca deixar a fila de
/// descartes crescer sem limite.
/// </summary>
public sealed class ConsumidorDeDescartes(IConnection conexao, ILogger<ConsumidorDeDescartes> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, ea) => ProcessarAsync(canal, ea, stoppingToken);

        await canal.BasicConsumeAsync(TopologiaDeMensageria.FilaDeDescartes, autoAck: false, consumidor, stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal do host.
        }
    }

    private async Task ProcessarAsync(IChannel canal, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        // liquidacao_id lido do envelope (MessageId), nunca do corpo — é exatamente aqui que um
        // corpo corrompido não pode impedir a identificação. O trace acrescenta informação, não
        // substitui essa correlação (design.md, D5/D9).
        var liquidacaoId = ea.BasicProperties.MessageId ?? "(sem MessageId)";
        using var escopoDeLog = log.BeginScope(new Dictionary<string, object> { ["LiquidacaoId"] = liquidacaoId });

        // Mesmo contexto de trace da transferência: sobrevive à reentrega e ao roteamento para a
        // DLQ, porque viaja no cabeçalho, não no corpo — o descarte cai no trace da transferência
        // em vez de virar um evento solto (design.md, D5).
        var contextoExtraido = RabbitMQActivitySource.ContextExtractor(ea.BasicProperties);
        using var atividade = InstrumentacaoDoWorker.ActivitySource.StartActivity(
            "RegistrarDescarte", ActivityKind.Internal, contextoExtraido);
        atividade?.SetTag("liquidacao_id", liquidacaoId);

        try
        {
            var tentativas = CabecalhosDeMensageria.LerTentativas(ea.BasicProperties.Headers);
            var motivo = CabecalhosDeMensageria.LerMotivoDoDescarte(ea.BasicProperties.Headers);
            var corpoBruto = Encoding.UTF8.GetString(ea.Body.Span);

            atividade?.SetTag("tentativas", tentativas);
            atividade?.SetTag("motivo", motivo);

            log.LogWarning(
                "Transferência {LiquidacaoId} descartada após {Tentativas} tentativa(s). Motivo: {Motivo}. Corpo bruto: {CorpoBruto}",
                liquidacaoId, tentativas, motivo, corpoBruto);

            InstrumentacaoDoWorker.RegistrarDesfecho(Desfechos.Descartada);
        }
        catch (Exception ex)
        {
            // A DLQ não tem DLQ própria: se este consumidor rejeitar a mensagem, ela quica
            // para sempre. Confirmar incondicionalmente — mesmo quando o próprio registro falha
            // — é o comportamento correto aqui, não um descuido.
            atividade?.SetStatus(ActivityStatusCode.Error, ex.Message);
            log.LogError(ex, "Falha ao registrar mensagem descartada; confirmando mesmo assim.");
        }
        finally
        {
            await canal.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
        }
    }
}
