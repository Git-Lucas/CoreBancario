namespace CoreBancario.Dominio.Identidades;

public readonly record struct LiquidacaoId(Guid Valor)
{
    public static LiquidacaoId Nova() => new(Guid.CreateVersion7());

    public override string ToString() => Valor.ToString();
}
