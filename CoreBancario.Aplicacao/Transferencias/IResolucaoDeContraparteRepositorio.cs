using CoreBancario.Dominio.Identidades;

namespace CoreBancario.Aplicacao.Transferencias;

/// <summary>
/// Port (driven): resolução do nome de titular a partir do próprio ledger — sem cadastro de
/// contas, o ledger é seu próprio cadastro de nomes (o primeiro lançamento de uma conta grava o
/// nome que a próxima resolução vai encontrar). Devolve apenas as contas encontradas — quem
/// faltar, o chamador resolve por invenção.
/// </summary>
public interface IResolucaoDeContraparteRepositorio
{
    Task<IReadOnlyDictionary<ContaId, string>> ResolverAsync(
        ContaId contaOrigem, ContaId contaDestino, CancellationToken cancellationToken);
}
