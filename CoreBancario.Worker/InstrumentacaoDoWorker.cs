using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CoreBancario.Worker;

/// <summary>
/// Nada aqui é automático: as fronteiras técnicas (HTTP, banco, mensageria) vêm de instrumentação
/// de biblioteca, mas "uma transferência foi liquidada" é conhecimento de domínio que nenhum
/// framework tem — por isso o Worker precisa do próprio <see cref="ActivitySource"/> e
/// <see cref="Meter"/>.
/// </summary>
public static class InstrumentacaoDoWorker
{
    public const string NomeDoServico = "CoreBancario.Worker";

    public static readonly ActivitySource ActivitySource = new(NomeDoServico);

    private static readonly Meter Meter = new(NomeDoServico);

    private static readonly Counter<long> MensagensProcessadas = Meter.CreateCounter<long>(
        "corebancario.worker.mensagens_processadas",
        description: "Mensagens de transferência processadas pelo Worker, segmentadas por desfecho.");

    private static readonly Histogram<double> DuracaoDoProcessamento = Meter.CreateHistogram<double>(
        "corebancario.worker.duracao_processamento",
        unit: "ms",
        description: "Duração do processamento de uma mensagem de transferência pelo consumidor principal.");

    public static void RegistrarDesfecho(string desfecho) =>
        MensagensProcessadas.Add(1, new KeyValuePair<string, object?>("desfecho", desfecho));

    public static void RegistrarDuracao(double milissegundos, string desfecho) =>
        DuracaoDoProcessamento.Record(milissegundos, new KeyValuePair<string, object?>("desfecho", desfecho));
}

/// <summary>
/// Rótulo de desfecho do processamento — cardinalidade fixa e pequena, ao contrário de
/// `liquidacao_id`/`conta_id`, que nunca podem virar rótulo de métrica.
/// </summary>
public static class Desfechos
{
    public const string Liquidada = "liquidada";
    public const string JaLiquidada = "ja_liquidada";
    public const string Falha = "falha";
    public const string Descartada = "descartada";
}
