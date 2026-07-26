using CoreBancario.Aplicacao.Extrato;

namespace CoreBancario.Api.Endpoints;

public static class EndpointsExtrato
{
    public static IEndpointRouteBuilder MapEndpointsExtrato(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGet("/contas/{contaId:guid}/extrato", async (
            Guid contaId,
            DateTimeOffset de,
            DateTimeOffset ate,
            string? cursor,
            ConsultaDeExtrato consulta,
            CancellationToken cancellationToken) =>
        {
            var pagina = await consulta.ExecutarAsync(contaId, de, ate, cursor, cancellationToken);
            return Results.Ok(pagina);
        });

        return rotas;
    }
}
