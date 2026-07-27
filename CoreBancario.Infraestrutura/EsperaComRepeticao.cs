using Microsoft.Extensions.Logging;

namespace CoreBancario.Infraestrutura;

/// <summary>
/// Política de repetição extraída de <see cref="Persistencia.MigracaoInicializacao"/> (D1 em
/// design.md da change add-k8s-packaging): a mesma espera limitada que o banco já tinha na
/// inicialização passa a valer para toda dependência externa aberta no startup — hoje, também o
/// RabbitMQ. Escopo é a primeira conexão; reconexão em regime é responsabilidade do cliente.
/// </summary>
public static class EsperaComRepeticao
{
    public static async Task<T> ExecutarAsync<T>(
        Func<Task<T>> operacao,
        string dependencia,
        ILogger log,
        TimeSpan intervaloEntreTentativas,
        int maximoTentativas,
        CancellationToken cancellationToken = default)
    {
        Exception? ultimaFalha = null;

        for (var tentativa = 1; tentativa <= maximoTentativas; tentativa++)
        {
            try
            {
                return await operacao();
            }
            catch (Exception ex)
            {
                ultimaFalha = ex;
                log.LogWarning(
                    ex,
                    "Falha ao conectar a {Dependencia} (tentativa {Tentativa}/{Maximo}). Dependência pode ainda não estar pronta.",
                    dependencia,
                    tentativa,
                    maximoTentativas);
                await Task.Delay(intervaloEntreTentativas, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Não foi possível conectar a {dependencia} após {maximoTentativas} tentativas.", ultimaFalha);
    }
}
