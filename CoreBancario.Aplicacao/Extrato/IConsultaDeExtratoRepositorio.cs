using CoreBancario.Dominio.Identidades;

namespace CoreBancario.Aplicacao.Extrato;

/// <summary>Projeção crua de uma linha — só o que o índice de cobertura entrega. Qualquer campo
/// além destes força o plano a visitar o heap linha a linha em vez de responder só pelo índice.</summary>
public sealed record LinhaExtratoBruta(LancamentoId Id, decimal Valor, string ContraparteNome);

public sealed record ResultadoConsultaExtrato(IReadOnlyList<LinhaExtratoBruta> Linhas, LancamentoId? ProximoCursor);

/// <summary>
/// Port (driven) implementado pela infraestrutura. Fala em datas e identidades tipadas — quem
/// traduz período em intervalo de identidade (v7Piso) é a implementação, não este contrato.
/// </summary>
public interface IConsultaDeExtratoRepositorio
{
    Task<ResultadoConsultaExtrato> ConsultarAsync(
        Guid contaId,
        DateTimeOffset de,
        DateTimeOffset ate,
        LancamentoId? cursor,
        CancellationToken cancellationToken);
}
