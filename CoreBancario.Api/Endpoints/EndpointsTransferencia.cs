using CoreBancario.Aplicacao.Transferencias;

namespace CoreBancario.Api.Endpoints;

public static class EndpointsTransferencia
{
    public static IEndpointRouteBuilder MapEndpointsTransferencia(this IEndpointRouteBuilder rotas)
    {
        // C2.2/C2.4 do PRD-2: nenhuma dependência de banco no caminho — só `SolicitarTransferencia`
        // (que só conhece `IPublicadorDeTransferencia`) é resolvida aqui.
        rotas.MapPost("/transferencias", async (
            ComandoSolicitarTransferencia comando,
            SolicitarTransferencia caso,
            CancellationToken cancellationToken) =>
        {
            var resultado = await caso.ExecutarAsync(comando, cancellationToken);

            return resultado switch
            {
                ResultadoSolicitacaoTransferencia.Aceita aceita =>
                    Results.Accepted(value: new RespostaTransferenciaAceita(aceita.LiquidacaoId.Valor)),
                ResultadoSolicitacaoTransferencia.RejeitadaNaValidacao rejeitada =>
                    Results.BadRequest(new RespostaDeErro(rejeitada.Motivo)),
                ResultadoSolicitacaoTransferencia.FalhaAoPublicar falha =>
                    Results.Json(new RespostaDeErro(falha.Motivo), statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => throw new InvalidOperationException($"Resultado de solicitação não tratado: {resultado.GetType().Name}"),
            };
        });

        return rotas;
    }
}

public sealed record RespostaTransferenciaAceita(Guid LiquidacaoId);

public sealed record RespostaDeErro(string Motivo);
