using CoreBancario.Aplicacao.Extrato;
using CoreBancario.Dominio.Identidades;
using Microsoft.EntityFrameworkCore;

namespace CoreBancario.Infraestrutura.Persistencia;

public sealed class ConsultaDeExtratoRepositorio(CoreBancarioDbContext contexto) : IConsultaDeExtratoRepositorio
{
    private const int TamanhoDaPagina = 50;

    public async Task<ResultadoConsultaExtrato> ConsultarAsync(
        Guid contaId,
        DateTimeOffset de,
        DateTimeOffset ate,
        LancamentoId? cursor,
        CancellationToken cancellationToken)
    {
        // Piso sempre derivado do parâmetro de início da requisição, nunca do cursor. Teto
        // unificado: primeira página usa o teto do período; demais usam o cursor recebido — a
        // mesma variável, então a consulta abaixo não precisa se ramificar. Um cursor adulterado
        // nunca empurra o teto além do período: o valor efetivo fica sempre limitado ao teto
        // calculado a partir do fim solicitado.
        var piso = new LancamentoId(Guid7.Piso(de));
        var tetoDoPeriodo = new LancamentoId(Guid7.Piso(ate));
        var teto = cursor is { } cursorValor && cursorValor < tetoDoPeriodo ? cursorValor : tetoDoPeriodo;
        var conta = new ContaId(contaId);

        var linhas = await contexto.Lancamentos
            .AsNoTracking()
            .Where(l => l.ContaId == conta)
            .Where(l => l.Id >= piso && l.Id < teto)
            .OrderByDescending(l => l.Id)
            .Take(TamanhoDaPagina + 1)
            .Select(l => new LinhaExtratoBruta(l.Id, l.Valor.Valor, l.ContraparteNome))
            .ToListAsync(cancellationToken);

        LancamentoId? proximoCursor = null;
        if (linhas.Count > TamanhoDaPagina)
        {
            linhas.RemoveAt(TamanhoDaPagina);
            proximoCursor = linhas[^1].Id;
        }

        return new ResultadoConsultaExtrato(linhas, proximoCursor);
    }
}
