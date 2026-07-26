namespace CoreBancario.Dominio.Identidades;

public readonly record struct LancamentoId(Guid Valor) : IComparable<LancamentoId>
{
    public static LancamentoId Nova() => new(Guid.CreateVersion7());

    public DateTimeOffset Instante => Guid7.ExtrairInstante(Valor);

    /// <summary>Fronteira de período: ver <see cref="Guid7.Piso"/>.</summary>
    public static LancamentoId Piso(DateTimeOffset instante) => new(Guid7.Piso(instante));

    // Ordem total sobre os bytes big-endian do Guid v7 — a mesma ordem que o PostgreSQL usa em
    // ORDER BY (uuid_cmp = memcmp), necessária para o keyset da consulta de extrato.
    public int CompareTo(LancamentoId outro) => Valor.CompareTo(outro.Valor);

    public static bool operator <(LancamentoId esquerda, LancamentoId direita) => esquerda.CompareTo(direita) < 0;

    public static bool operator <=(LancamentoId esquerda, LancamentoId direita) => esquerda.CompareTo(direita) <= 0;

    public static bool operator >(LancamentoId esquerda, LancamentoId direita) => esquerda.CompareTo(direita) > 0;

    public static bool operator >=(LancamentoId esquerda, LancamentoId direita) => esquerda.CompareTo(direita) >= 0;

    public override string ToString() => Valor.ToString();
}
