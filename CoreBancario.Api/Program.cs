using System.Text.Json.Serialization;
using CoreBancario.Api.Endpoints;
using CoreBancario.Aplicacao.Extrato;
using CoreBancario.Aplicacao.Transferencias;
using CoreBancario.Infraestrutura.Mensageria;
using CoreBancario.Infraestrutura.Persistencia;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

var construtor = WebApplication.CreateBuilder(args);

// 8.1 em tasks.md: formatador JSON, para que o liquidacao_id (aberto em escopo — 8.2) seja campo
// consultável no log, não texto interpolado.
construtor.Logging.ClearProviders();
construtor.Logging.AddJsonConsole(opcoes => opcoes.IncludeScopes = true);

construtor.Services.ConfigureHttpJsonOptions(opcoes =>
    opcoes.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

construtor.Services.AddDbContext<CoreBancarioDbContext>(opcoes =>
    opcoes.UseNpgsql(construtor.Configuration.GetConnectionString("CoreBancario")));

construtor.Services.AddScoped<IConsultaDeExtratoRepositorio, ConsultaDeExtratoRepositorio>();
construtor.Services.AddScoped<ConsultaDeExtrato>();

var conexaoRabbitMq = await new ConnectionFactory
{
    Uri = new Uri(construtor.Configuration.GetConnectionString("RabbitMQ")
        ?? throw new InvalidOperationException("ConnectionStrings:RabbitMQ não configurada.")),
}.CreateConnectionAsync();

construtor.Services.AddSingleton(conexaoRabbitMq);
construtor.Services.AddSingleton<IPublicadorDeTransferencia, PublicadorDeTransferencia>();
construtor.Services.AddScoped<SolicitarTransferencia>();

var aplicacao = construtor.Build();

await aplicacao.AplicarMigrationsAsync();

// Idempotente (D12 em design.md), na inicialização dos dois processos — API e Worker.
await TopologiaDeMensageria.DeclararAsync(conexaoRabbitMq);

aplicacao.MapEndpointsSistema();
aplicacao.MapEndpointsExtrato();
aplicacao.MapEndpointsTransferencia();

await aplicacao.RunAsync();
