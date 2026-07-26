namespace CoreBancario.Dominio.Identidades;

public readonly record struct ContaId(Guid Valor)
{
    public static ContaId Nova() => new(Guid.CreateVersion7());

    public override string ToString() => Valor.ToString();
}
