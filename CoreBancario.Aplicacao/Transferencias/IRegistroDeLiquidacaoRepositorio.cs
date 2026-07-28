using CoreBancario.Dominio.Ledger;

namespace CoreBancario.Aplicacao.Transferencias;

public enum ResultadoRegistro
{
    Registrada,
    JaRegistrada,
}

/// <summary>
/// Port (driven): grava o par de lançamentos da liquidação. A violação do índice único de
/// idempotência (`ux_lancamentos_idempotencia`) DEVE ser traduzida em <see cref="ResultadoRegistro.JaRegistrada"/>,
/// não em exceção — do contrário, uma reentrega legítima do broker consumiria as tentativas de
/// retry e mandaria uma transferência já liquidada corretamente para a fila de descartes.
/// </summary>
public interface IRegistroDeLiquidacaoRepositorio
{
    Task<ResultadoRegistro> RegistrarAsync(Liquidacao liquidacao, CancellationToken cancellationToken);
}
