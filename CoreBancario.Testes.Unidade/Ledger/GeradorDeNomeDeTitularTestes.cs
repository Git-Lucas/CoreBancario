using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;

namespace CoreBancario.Testes.Unidade.Ledger;

public class GeradorDeNomeDeTitularTestes
{
    [Fact]
    public void Gerar_MesmoContaId_ProduzSempreOMesmoNome()
    {
        var contaId = ContaId.Nova();

        var primeiraChamada = GeradorDeNomeDeTitular.Gerar(contaId);
        var segundaChamada = GeradorDeNomeDeTitular.Gerar(contaId);

        Assert.Equal(primeiraChamada, segundaChamada);
    }

    [Fact]
    public void Gerar_ContaIdsDistintos_RaramenteColidem()
    {
        var nomes = Enumerable.Range(0, 200)
            .Select(_ => GeradorDeNomeDeTitular.Gerar(ContaId.Nova()))
            .ToList();

        var distintos = nomes.Distinct().Count();

        // Limiar com folga sobre a expectativa teórica (~94% para 200 amostras no espaço de
        // combinações do gerador), tolerando a variação natural do acaso do teste sem ficar frágil.
        Assert.True(
            distintos >= nomes.Count * 0.85,
            $"esperado ao menos 85% de nomes distintos entre 200 contas, obtidos {distintos}");
    }
}
