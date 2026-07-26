using CoreBancario.Infraestrutura.Persistencia;
using CoreBancario.Infraestrutura.Persistencia.Seed;
using CoreBancario.Worker;
using Microsoft.EntityFrameworkCore;

var executarSeed = args.Contains("--seed", StringComparer.OrdinalIgnoreCase);

var construtor = Host.CreateApplicationBuilder(args);

construtor.Services.AddDbContext<CoreBancarioDbContext>(opcoes =>
    opcoes.UseNpgsql(construtor.Configuration.GetConnectionString("CoreBancario")));

if (!executarSeed)
{
    construtor.Services.AddHostedService<Trabalhador>();
}

var anfitriao = construtor.Build();

await anfitriao.AplicarMigrationsAsync();

if (executarSeed)
{
    var connectionString = construtor.Configuration.GetConnectionString("CoreBancario")
        ?? throw new InvalidOperationException("ConnectionStrings:CoreBancario não configurada.");

    var log = anfitriao.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");

    await GeradorDeMassaDeDados.ExecutarAsync(connectionString, log);
    return;
}

await anfitriao.RunAsync();
