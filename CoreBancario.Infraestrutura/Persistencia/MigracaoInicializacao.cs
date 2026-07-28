using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoreBancario.Infraestrutura.Persistencia;

/// <summary>
/// Anti-padrão consciente para projeto de estudo: em produção, migração no startup significa
/// réplicas migrando concorrentemente e indisponibilidade durante migração longa. O correto
/// seria um passo separado de deploy ou um Job dedicado.
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

        await EsperaComRepeticao.ExecutarAsync(
            async () =>
            {
                await contexto.Database.MigrateAsync(cancellationToken);
                return true;
            },
            "PostgreSQL",
            log,
            IntervaloEntreTentativas,
            MaximoTentativas,
            cancellationToken);
    }
}
