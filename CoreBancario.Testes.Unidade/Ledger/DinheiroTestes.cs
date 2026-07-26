using CoreBancario.Dominio.Ledger;

namespace CoreBancario.Testes.Unidade.Ledger;

public class DinheiroTestes
{
    [Fact]
    public void Somar_MoedasDiferentes_Lanca()
    {
        var real = new Dinheiro(10m, Moeda.BRL);
        var dolar = new Dinheiro(10m, Moeda.USD);

        Assert.Throws<InvalidOperationException>(() => real + dolar);
    }

    [Fact]
    public void Somar_MesmaMoeda_RetornaSoma()
    {
        var a = new Dinheiro(10.50m, Moeda.BRL);
        var b = new Dinheiro(5.25m, Moeda.BRL);

        var resultado = a + b;

        Assert.Equal(new Dinheiro(15.75m, Moeda.BRL), resultado);
    }

    [Fact]
    public void Construir_ComMaisCasasDecimaisQueAMoedaAdmite_Lanca()
    {
        Assert.Throws<ArgumentException>(() => new Dinheiro(10.123m, Moeda.BRL));
    }

    [Fact]
    public void Construir_ComCasasDecimaisValidas_NaoLanca()
    {
        var dinheiro = new Dinheiro(10.12m, Moeda.BRL);

        Assert.Equal(10.12m, dinheiro.Valor);
    }
}
