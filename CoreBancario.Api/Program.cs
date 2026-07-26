using System.Text.Json.Serialization;
using CoreBancario.Api.Endpoints;
using CoreBancario.Aplicacao.Extrato;
using CoreBancario.Infraestrutura.Persistencia;
using Microsoft.EntityFrameworkCore;

var construtor = WebApplication.CreateBuilder(args);

construtor.Services.ConfigureHttpJsonOptions(opcoes =>
    opcoes.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

construtor.Services.AddDbContext<CoreBancarioDbContext>(opcoes =>
    opcoes.UseNpgsql(construtor.Configuration.GetConnectionString("CoreBancario")));

construtor.Services.AddScoped<IConsultaDeExtratoRepositorio, ConsultaDeExtratoRepositorio>();
construtor.Services.AddScoped<ConsultaDeExtrato>();

var aplicacao = construtor.Build();

await aplicacao.AplicarMigrationsAsync();

aplicacao.MapEndpointsSistema();
aplicacao.MapEndpointsExtrato();

await aplicacao.RunAsync();
