using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace CoreBancario.Infraestrutura.Mensageria;

/// <summary>
/// Abertura da primeira conexão com o RabbitMQ com a mesma espera que
/// <see cref="Persistencia.MigracaoInicializacao"/> já aplica ao banco — sob Kubernetes não há
/// garantia de ordem de subida entre os workloads. A reconexão em regime é responsabilidade do
/// <see cref="ConnectionFactory.AutomaticRecoveryEnabled"/> do próprio cliente; esta espera cobre
/// só a conexão inicial.
/// </summary>
public static class ConexaoRabbitMqInicializacao
{
    private static readonly TimeSpan IntervaloEntreTentativasPadrao = TimeSpan.FromSeconds(2);
    private const int MaximoTentativasPadrao = 30;

    public static Task<IConnection> AbrirComEsperaAsync(
        string connectionString,
        ILogger log,
        TimeSpan? intervaloEntreTentativas = null,
        int? maximoTentativas = null,
        CancellationToken cancellationToken = default) =>
        EsperaComRepeticao.ExecutarAsync(
            () => new ConnectionFactory { Uri = new Uri(connectionString) }
                .CreateConnectionAsync(cancellationToken),
            "RabbitMQ",
            log,
            intervaloEntreTentativas ?? IntervaloEntreTentativasPadrao,
            maximoTentativas ?? MaximoTentativasPadrao,
            cancellationToken);
}
