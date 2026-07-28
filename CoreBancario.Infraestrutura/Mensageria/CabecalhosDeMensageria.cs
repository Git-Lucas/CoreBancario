using System.Text;

namespace CoreBancario.Infraestrutura.Mensageria;

/// <summary>
/// Leitura dos cabeçalhos nativos de reentrega/descarte do RabbitMQ:
/// `x-delivery-count` (inteiro) e `x-death` (array de tabelas, com `reason` como long-string).
/// Tolerante a ausência — a fila de descartes não pode lançar por cabeçalho inesperado.
/// </summary>
public static class CabecalhosDeMensageria
{
    public static int LerTentativas(IDictionary<string, object?>? cabecalhos)
    {
        if (cabecalhos is null || !cabecalhos.TryGetValue("x-delivery-count", out var valor))
        {
            return 0;
        }

        return ParaInt(valor);
    }

    public static string LerMotivoDoDescarte(IDictionary<string, object?>? cabecalhos)
    {
        if (cabecalhos is null
            || !cabecalhos.TryGetValue("x-death", out var xDeath)
            || xDeath is not IList<object?> mortes
            || mortes.Count == 0
            || mortes[0] is not IDictionary<string, object?> primeiraMorte
            || !primeiraMorte.TryGetValue("reason", out var razao))
        {
            return "desconhecido";
        }

        return razao switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string texto => texto,
            _ => razao?.ToString() ?? "desconhecido",
        };
    }

    private static int ParaInt(object? valor) => valor switch
    {
        null => 0,
        int i => i,
        long l => (int)l,
        short s => s,
        _ => Convert.ToInt32(valor, System.Globalization.CultureInfo.InvariantCulture),
    };
}
