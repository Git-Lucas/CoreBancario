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
/// não em exceção — é a tradução exigida por D6 em design.md.
/// </summary>
public interface IRegistroDeLiquidacaoRepositorio
{
    Task<ResultadoRegistro> RegistrarAsync(Liquidacao liquidacao, CancellationToken cancellationToken);
}
