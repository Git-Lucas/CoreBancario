using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBancario.Infraestrutura.Persistencia;

/// <summary>
/// Anti-padrão consciente para projeto de estudo (D13 em design.md): em produção, migração no
/// startup significa réplicas migrando concorrentemente e indisponibilidade durante migração
/// longa. O correto seria um passo separado de deploy ou um Job dedicado.
/// </summary>
public static class MigracaoInicializacao
{
    private static readonly TimeSpan IntervaloEntreTentativas = TimeSpan.FromSeconds(2);
    private const int MaximoTentativas = 30;

    public static async Task AplicarMigrationsAsync(this IHost app, CancellationToken cancellationToken = default)
    {
        using var escopo = app.Services.CreateScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<CoreBancarioDbContext>();
        var log = escopo.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MigracaoInicializacao));

        Exception? ultimaFalha = null;

        for (var tentativa = 1; tentativa <= MaximoTentativas; tentativa++)
        {
            try
            {
                await contexto.Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                ultimaFalha = ex;
                log.LogWarning(
                    ex,
                    "Falha ao aplicar migrations (tentativa {Tentativa}/{Maximo}). Banco pode ainda não estar pronto.",
                    tentativa,
                    MaximoTentativas);
                await Task.Delay(IntervaloEntreTentativas, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Não foi possível aplicar as migrations após {MaximoTentativas} tentativas.", ultimaFalha);
    }
}
