using RabbitMQ.Client;

namespace CoreBancario.Infraestrutura.Mensageria;

/// <summary>
/// Nomes e declaração da topologia (D12 em design.md). Declarada na inicialização de ambos os
/// processos, de forma idempotente — mesma escolha já feita para as migrations no PRD-1.
/// </summary>
public static class TopologiaDeMensageria
{
    public const string Exchange = "corebancario.transferencias";
    public const string RoutingKey = "transferencia.solicitada";
    public const string FilaPrincipal = "transferencias";
    public const string DeadLetterExchange = "corebancario.transferencias.dlx";
    public const string FilaDeDescartes = "transferencias.dlq";

    private const int LimiteDeEntregas = 3;

    public static async Task DeclararAsync(IConnection conexao, CancellationToken cancellationToken = default)
    {
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: cancellationToken);

        await canal.ExchangeDeclareAsync(
            Exchange, type: ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        await canal.ExchangeDeclareAsync(
            DeadLetterExchange, type: ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        await canal.QueueDeclareAsync(
            FilaDeDescartes, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: cancellationToken);

        await canal.QueueBindAsync(FilaDeDescartes, DeadLetterExchange, RoutingKey, cancellationToken: cancellationToken);

        await canal.QueueDeclareAsync(
            FilaPrincipal, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = LimiteDeEntregas,
                ["x-dead-letter-exchange"] = DeadLetterExchange,
            },
            cancellationToken: cancellationToken);

        await canal.QueueBindAsync(FilaPrincipal, Exchange, RoutingKey, cancellationToken: cancellationToken);
    }
}
