using System.Diagnostics.Metrics;
using CoreBancario.Worker;

namespace CoreBancario.Testes.Unidade.Worker;

/// <summary>
/// A reentrega absorvida por idempotência precisa incrementar o desfecho próprio
/// (<see cref="Desfechos.JaLiquidada"/>), não <see cref="Desfechos.Liquidada"/> — contá-la como
/// liquidação nova inflaria a taxa com trabalho que não aconteceu.
/// </summary>
public class InstrumentacaoDoWorkerTestes
{
    [Fact]
    public void RegistrarDesfecho_LiquidadaEJaLiquidada_SaoContadosSobRotulosDeDesfechoDistintos()
    {
        var desfechosRegistrados = new List<string?>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrumento, l) =>
        {
            if (instrumento.Meter.Name == InstrumentacaoDoWorker.NomeDoServico
                && instrumento.Name == "corebancario.worker.mensagens_processadas")
            {
                l.EnableMeasurementEvents(instrumento);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
            desfechosRegistrados.Add(tags.ToArray().FirstOrDefault(t => t.Key == "desfecho").Value?.ToString()));
        listener.Start();

        InstrumentacaoDoWorker.RegistrarDesfecho(Desfechos.Liquidada);
        InstrumentacaoDoWorker.RegistrarDesfecho(Desfechos.JaLiquidada);

        Assert.Contains(Desfechos.Liquidada, desfechosRegistrados);
        Assert.Contains(Desfechos.JaLiquidada, desfechosRegistrados);
    }

    [Fact]
    public void RegistrarDuracao_RegistraOValorNoHistogramaComORotuloDeDesfecho()
    {
        double? valorRegistrado = null;
        string? desfechoRegistrado = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrumento, l) =>
        {
            if (instrumento.Meter.Name == InstrumentacaoDoWorker.NomeDoServico
                && instrumento.Name == "corebancario.worker.duracao_processamento")
            {
                l.EnableMeasurementEvents(instrumento);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, valor, tags, _) =>
        {
            valorRegistrado = valor;
            desfechoRegistrado = tags.ToArray().FirstOrDefault(t => t.Key == "desfecho").Value?.ToString();
        });
        listener.Start();

        InstrumentacaoDoWorker.RegistrarDuracao(42.5, Desfechos.Falha);

        Assert.Equal(42.5, valorRegistrado);
        Assert.Equal(Desfechos.Falha, desfechoRegistrado);
    }
}
