namespace CoreBancario.Worker;

public class Trabalhador(ILogger<Trabalhador> log) : BackgroundService
{
    // Nome do parâmetro imposto pela assinatura da classe base (BackgroundService).
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (log.IsEnabled(LogLevel.Information))
            {
                log.LogInformation("Trabalhador em execução às: {Hora}", DateTimeOffset.Now);
            }

            await Task.Delay(1000, stoppingToken);
        }
    }
}
