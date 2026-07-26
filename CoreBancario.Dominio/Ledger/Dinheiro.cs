namespace CoreBancario.Dominio.Ledger;

public readonly record struct Dinheiro
{
    public decimal Valor { get; }
    public Moeda Moeda { get; }

    public Dinheiro(decimal valor, Moeda moeda)
    {
        var casasAdmitidas = CasasDecimais(moeda);
        if (decimal.Round(valor, casasAdmitidas) != valor)
        {
            throw new ArgumentException(
                $"Valor {valor} tem mais casas decimais do que {moeda} admite ({casasAdmitidas}).",
                nameof(valor));
        }

        Valor = valor;
        Moeda = moeda;
    }

    public static Dinheiro operator +(Dinheiro esquerda, Dinheiro direita)
    {
        ExigirMesmaMoeda(esquerda, direita);
        return new Dinheiro(esquerda.Valor + direita.Valor, esquerda.Moeda);
    }

    public static Dinheiro operator -(Dinheiro esquerda, Dinheiro direita)
    {
        ExigirMesmaMoeda(esquerda, direita);
        return new Dinheiro(esquerda.Valor - direita.Valor, esquerda.Moeda);
    }

    public static Dinheiro operator -(Dinheiro dinheiro) => new(-dinheiro.Valor, dinheiro.Moeda);

    private static void ExigirMesmaMoeda(Dinheiro esquerda, Dinheiro direita)
    {
        if (esquerda.Moeda != direita.Moeda)
        {
            throw new InvalidOperationException(
                $"Não é possível operar {esquerda.Moeda} com {direita.Moeda}.");
        }
    }

    private static int CasasDecimais(Moeda moeda) => moeda switch
    {
        Moeda.BRL => 2,
        Moeda.USD => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(moeda), moeda, "Moeda desconhecida."),
    };
}
