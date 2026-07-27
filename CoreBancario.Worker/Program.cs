using CoreBancario.Aplicacao.Transferencias;
using CoreBancario.Infraestrutura.Mensageria;
using CoreBancario.Infraestrutura.Persistencia;
using CoreBancario.Infraestrutura.Persistencia.Seed;
using CoreBancario.Worker;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

var executarSeed = args.Contains("--seed", StringComparer.OrdinalIgnoreCase);

var construtor = Host.CreateApplicationBuilder(args);

// 8.1 em tasks.md: formatador JSON, para que o liquidacao_id (aberto em escopo — 8.2) seja campo
// consultável no log, não texto interpolado. Fica fora do modo --seed: ali não há liquidacao_id
// para correlacionar, e o log de progresso do seed é lido por humano, não por consulta.
if (!executarSeed)
{
    construtor.Logging.ClearProviders();
    construtor.Logging.AddJsonConsole(opcoes => opcoes.IncludeScopes = true);
}

construtor.Services.AddDbContext<CoreBancarioDbContext>(opcoes =>
    opcoes.UseNpgsql(construtor.Configuration.GetConnectionString("CoreBancario")));

if (!executarSeed)
{
    // Logger de bootstrap: a conexão precisa existir antes de construtor.Build() para ser
    // registrada como singleton, e o container de DI só existe depois disso.
    using var logFactoryDeBootstrap = LoggerFactory.Create(
        opcoes => opcoes.AddJsonConsole(json => json.IncludeScopes = true));

    var conexaoRabbitMq = await ConexaoRabbitMqInicializacao.AbrirComEsperaAsync(
        construtor.Configuration.GetConnectionString("RabbitMQ")
            ?? throw new InvalidOperationException("ConnectionStrings:RabbitMQ não configurada."),
        logFactoryDeBootstrap.CreateLogger("ConexaoRabbitMq"));

    construtor.Services.AddSingleton(conexaoRabbitMq);
    construtor.Services.AddScoped<IResolucaoDeContraparteRepositorio, ResolucaoDeContraparteRepositorio>();
    construtor.Services.AddScoped<IRegistroDeLiquidacaoRepositorio, RegistroDeLiquidacaoRepositorio>();
    construtor.Services.AddScoped<LiquidarTransferencia>();
    construtor.Services.AddHostedService<ConsumidorDeTransferencias>();
    construtor.Services.AddHostedService<ConsumidorDeDescartes>();
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

// Idempotente (D12 em design.md): mesma escolha já feita para as migrations — não introduzir
// passo de deploy separado num projeto com este prazo.
await TopologiaDeMensageria.DeclararAsync(anfitriao.Services.GetRequiredService<IConnection>());

await anfitriao.RunAsync();
