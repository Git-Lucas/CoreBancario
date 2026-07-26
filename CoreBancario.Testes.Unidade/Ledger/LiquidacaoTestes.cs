using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;

namespace CoreBancario.Testes.Unidade.Ledger;

public class LiquidacaoTestes
{
    [Fact]
    public void Registrar_ProduzParQueSomaZero()
    {
        var contaA = ContaId.Nova();
        var contaB = ContaId.Nova();

        var liquidacao = Liquidacao.Registrar(
            LiquidacaoId.Nova(),
            contaDebito: contaA,
            nomeContaDebito: "Fulano",
            contaCredito: contaB,
            nomeContaCredito: "Beltrano",
            valorDebito: new Dinheiro(100m, Moeda.BRL),
            valorCredito: new Dinheiro(100m, Moeda.BRL));

        Assert.Equal(liquidacao.Id, liquidacao.Debito.LiquidacaoId);
        Assert.Equal(liquidacao.Id, liquidacao.Credito.LiquidacaoId);
        Assert.Equal(new Dinheiro(0m, Moeda.BRL), liquidacao.Debito.Valor + liquidacao.Credito.Valor);
        Assert.Equal(-100m, liquidacao.Debito.Valor.Valor);
        Assert.Equal(100m, liquidacao.Credito.Valor.Valor);
    }

    [Fact]
    public void Registrar_ComValoresAbsolutosDiferentes_Lanca()
    {
        var contaA = ContaId.Nova();
        var contaB = ContaId.Nova();

        Assert.Throws<InvalidOperationException>(() => Liquidacao.Registrar(
            LiquidacaoId.Nova(),
            contaDebito: contaA,
            nomeContaDebito: "Fulano",
            contaCredito: contaB,
            nomeContaCredito: "Beltrano",
            valorDebito: new Dinheiro(100m, Moeda.BRL),
            valorCredito: new Dinheiro(50m, Moeda.BRL)));
    }
}
