namespace CoreBancario.Api.Endpoints;

public static class EndpointsSistema
{
    public static IEndpointRouteBuilder MapEndpointsSistema(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/sistema");

        grupo.MapGet("/saude", () => Results.Ok());

        return rotas;
    }
}
