using CoreBancario.Infraestrutura.Mensageria;
using RabbitMQ.Client;

namespace CoreBancario.Testes.Integracao.Infraestrutura;

internal static class EsperaDeMensageria
{
    public static async Task<BasicGetResult> ReceberComEsperaAsync(
        IChannel canal, string fila, CancellationToken cancellationToken, int tentativasMaximas = 50)
    {
        for (var tentativa = 0; tentativa < tentativasMaximas; tentativa++)
        {
            var resultado = await canal.BasicGetAsync(fila, autoAck: false, cancellationToken);
            if (resultado is not null)
            {
                return resultado;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new InvalidOperationException($"Nenhuma mensagem disponível em '{fila}' após a espera.");
    }

    public static Task<BasicGetResult> ReceberComEsperaAsync(IChannel canal, CancellationToken cancellationToken) =>
        ReceberComEsperaAsync(canal, TopologiaDeMensageria.FilaPrincipal, cancellationToken);
}
