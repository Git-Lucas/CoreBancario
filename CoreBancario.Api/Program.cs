using CoreBancario.Api.Endpoints;

var construtor = WebApplication.CreateBuilder(args);

var aplicacao = construtor.Build();

aplicacao.MapEndpointsSistema();

await aplicacao.RunAsync();
