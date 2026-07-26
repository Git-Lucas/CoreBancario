using CoreBancario.Dominio.Identidades;

namespace CoreBancario.Aplicacao.Transferencias;

/// <summary>
/// Port (driven): resolução do nome de titular a partir do próprio ledger (D4 em design.md).
/// Devolve apenas as contas encontradas — quem faltar, o chamador resolve por invenção.
/// </summary>
public interface IResolucaoDeContraparteRepositorio
{
    Task<IReadOnlyDictionary<ContaId, string>> ResolverAsync(
        ContaId contaOrigem, ContaId contaDestino, CancellationToken cancellationToken);
}
