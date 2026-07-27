using System.Diagnostics;
using System.Text.Json;
using CoreBancario.Aplicacao.Transferencias;
using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;
using CoreBancario.Infraestrutura.Mensageria;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CoreBancario.Worker;

/// <summary>
/// Consumidor principal: `prefetch = 10`, confirmação manual após o commit da liquidação — a
/// violação do índice único de idempotência já é traduzida em sucesso dentro de
/// <see cref="LiquidarTransferencia"/>/<see cref="IRegistroDeLiquidacaoRepositorio"/>, então
/// qualquer exceção que chegue aqui é falha real e volta para a fila.
///
/// A devolução usa `basic.reject`, não `basic.nack`: a partir do RabbitMQ 4.3, reentregas via
/// `nack` deixaram de contar para `x-delivery-limit` quando o mesmo canal permanece aberto entre
/// tentativas — só `reject` (ou a conexão cair) incrementa `x-delivery-count`. Com `nack`, uma
/// mensagem venenosa reentregaria para sempre no mesmo Worker sem nunca alcançar a DLQ.
/// Verificado empiricamente contra RabbitMQ 4.3.4: um protótipo usando `nack` reentregava a
/// mesma mensagem indefinidamente ao mesmo Worker, sem nunca isolar a mensagem venenosa.
/// </summary>
public sealed class ConsumidorDeTransferencias(
    IConnection conexao, IServiceScopeFactory escopos, ILogger<ConsumidorDeTransferencias> log) : BackgroundService
{
    private const ushort Prefetch = 10;
    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: stoppingToken);
        await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: Prefetch, global: false, stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, ea) => ProcessarAsync(canal, ea, stoppingToken);

        await canal.BasicConsumeAsync(TopologiaDeMensageria.FilaPrincipal, autoAck: false, consumidor, stoppingToken);

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
        var liquidacaoId = ea.BasicProperties.MessageId ?? "(sem MessageId)";
        var tentativa = CabecalhosDeMensageria.LerTentativas(ea.BasicProperties.Headers) + 1;

        using var escopoDeLog = log.BeginScope(new Dictionary<string, object> { ["LiquidacaoId"] = liquidacaoId });
        log.LogInformation("Mensagem da transferência {LiquidacaoId} recebida (tentativa {Tentativa}).", liquidacaoId, tentativa);

        // Encadeado a partir do contexto extraído do cabeçalho, não do contexto ambiente: o
        // processamento roda dentro de um callback do consumidor assíncrono, onde o contexto
        // ambiente não é garantido. Sem este span, os spans de banco da
        // liquidação nascem soltos, e é aqui que o trace se partiria.
        var contextoExtraido = RabbitMQActivitySource.ContextExtractor(ea.BasicProperties);
        using var atividade = InstrumentacaoDoWorker.ActivitySource.StartActivity(
            "ProcessarTransferencia", ActivityKind.Internal, contextoExtraido);
        atividade?.SetTag("liquidacao_id", liquidacaoId);

        var cronometro = Stopwatch.StartNew();
        try
        {
            var mensagem = JsonSerializer.Deserialize<MensagemTransferencia>(ea.Body.Span, OpcoesJson)
                ?? throw new InvalidOperationException("Corpo da mensagem vazio ou inválido.");

            var solicitacao = new SolicitacaoDeTransferencia(
                new LiquidacaoId(mensagem.LiquidacaoId),
                new ContaId(mensagem.ContaOrigem),
                new ContaId(mensagem.ContaDestino),
                new Dinheiro(mensagem.Valor, Enum.Parse<Moeda>(mensagem.Moeda)));

            using var escopo = escopos.CreateScope();
            var casoDeUso = escopo.ServiceProvider.GetRequiredService<LiquidarTransferencia>();
            var resultado = await casoDeUso.ExecutarAsync(solicitacao, stoppingToken);

            await canal.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);

            var desfecho = resultado == ResultadoLiquidacao.Liquidada ? Desfechos.Liquidada : Desfechos.JaLiquidada;
            InstrumentacaoDoWorker.RegistrarDesfecho(desfecho);
            InstrumentacaoDoWorker.RegistrarDuracao(cronometro.Elapsed.TotalMilliseconds, desfecho);
        }
        catch (Exception ex)
        {
            atividade?.SetStatus(ActivityStatusCode.Error, ex.Message);
            log.LogError(ex, "Falha ao liquidar transferência {LiquidacaoId}.", liquidacaoId);
            await canal.BasicRejectAsync(ea.DeliveryTag, requeue: true, stoppingToken);

            InstrumentacaoDoWorker.RegistrarDesfecho(Desfechos.Falha);
            InstrumentacaoDoWorker.RegistrarDuracao(cronometro.Elapsed.TotalMilliseconds, Desfechos.Falha);
        }
    }
}
