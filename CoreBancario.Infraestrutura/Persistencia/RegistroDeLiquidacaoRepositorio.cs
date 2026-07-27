using CoreBancario.Aplicacao.Transferencias;
using CoreBancario.Dominio.Ledger;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CoreBancario.Infraestrutura.Persistencia;

/// <summary>
/// D6 em design.md: a violação do índice único de idempotência (`ux_lancamentos_idempotencia`)
/// é traduzida em <see cref="ResultadoRegistro.JaRegistrada"/>, nunca propagada como falha — é
/// o próprio mecanismo de idempotência da liquidação, não um caso de erro do consumidor.
/// </summary>
public sealed class RegistroDeLiquidacaoRepositorio(CoreBancarioDbContext contexto) : IRegistroDeLiquidacaoRepositorio
{
    private const string CodigoViolacaoDeUnicidade = "23505";

    public async Task<ResultadoRegistro> RegistrarAsync(Liquidacao liquidacao, CancellationToken cancellationToken)
    {
        contexto.Lancamentos.AddRange(liquidacao.Debito, liquidacao.Credito);

        try
        {
            // Débito e crédito no mesmo SaveChangesAsync: uma transação implícita só, então a
            // reentrega nunca encontra um par pela metade — ou os dois já existem, ou nenhum.
            await contexto.SaveChangesAsync(cancellationToken);
            return ResultadoRegistro.Registrada;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: CodigoViolacaoDeUnicidade })
        {
            contexto.ChangeTracker.Clear();
            return ResultadoRegistro.JaRegistrada;
        }
    }
}
