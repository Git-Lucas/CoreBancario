using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CoreBancario.Testes.Integracao.Infraestrutura;

/// <summary>Conta comandos SQL emitidos e guarda o texto do último, para inspeção em teste.</summary>
public sealed class ContadorDeComandosInterceptor : DbCommandInterceptor
{
    private int _contagem;

    public int Contagem => _contagem;

    public string? UltimoComandoTexto { get; private set; }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Registrar(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Registrar(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Registrar(DbCommand command)
    {
        Interlocked.Increment(ref _contagem);
        UltimoComandoTexto = command.CommandText;
    }
}
