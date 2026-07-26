using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;

namespace CoreBancario.Testes.Unidade.Ledger;

public class TransferenciaTestes
{
    [Fact]
    public void Solicitar_ComValoresAbsolutosDiferentes_Lanca()
    {
        var origem = ContaId.Nova();
        var destino = ContaId.Nova();

        Assert.Throws<InvalidOperationException>(() => Transferencia.Solicitar(
            origem,
            destino,
            valorDebito: new Dinheiro(-100m, Moeda.BRL),
            valorCredito: new Dinheiro(50m, Moeda.BRL)));
    }

    [Fact]
    public void Solicitar_ComMoedasDiferentes_Lanca()
    {
        var origem = ContaId.Nova();
        var destino = ContaId.Nova();

        Assert.Throws<InvalidOperationException>(() => Transferencia.Solicitar(
            origem,
            destino,
            valorDebito: new Dinheiro(-100m, Moeda.BRL),
            valorCredito: new Dinheiro(100m, Moeda.USD)));
    }

    [Fact]
    public void Solicitar_Balanceada_ProduzTransferenciaComValorAbsoluto()
    {
        var origem = ContaId.Nova();
        var destino = ContaId.Nova();

        var transferencia = Transferencia.Solicitar(
            origem,
            destino,
            valorDebito: new Dinheiro(-100m, Moeda.BRL),
            valorCredito: new Dinheiro(100m, Moeda.BRL));

        Assert.Equal(origem, transferencia.ContaOrigem);
        Assert.Equal(destino, transferencia.ContaDestino);
        Assert.Equal(new Dinheiro(100m, Moeda.BRL), transferencia.Valor);
    }

    [Fact]
    public void Liquidar_ProduzParDeLancamentosComOsNomesInformados()
    {
        var origem = ContaId.Nova();
        var destino = ContaId.Nova();
        var id = LiquidacaoId.Nova();

        var transferencia = Transferencia.Solicitar(
            origem,
            destino,
            valorDebito: new Dinheiro(-100m, Moeda.BRL),
            valorCredito: new Dinheiro(100m, Moeda.BRL));

        var liquidacao = transferencia.Liquidar(id, "Fulano", "Beltrano");

        Assert.Equal(id, liquidacao.Id);
        Assert.Equal(origem, liquidacao.Debito.ContaId);
        Assert.Equal("Beltrano", liquidacao.Debito.ContraparteNome);
        Assert.Equal(destino, liquidacao.Credito.ContaId);
        Assert.Equal("Fulano", liquidacao.Credito.ContraparteNome);
        Assert.Equal(-100m, liquidacao.Debito.Valor.Valor);
        Assert.Equal(100m, liquidacao.Credito.Valor.Valor);
    }
}
