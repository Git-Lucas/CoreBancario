namespace CoreBancario.Dominio.Identidades;

/// <summary>
/// Extração de instante e fronteira de período sobre identidades Guid versão 7 (RFC 9562).
/// Opera sobre os bytes big-endian, que são a representação canônica comparada pelo PostgreSQL.
/// </summary>
public static class Guid7
{
    public static DateTimeOffset ExtrairInstante(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);

        var milissegundos = ((long)bytes[0] << 40)
            | ((long)bytes[1] << 32)
            | ((long)bytes[2] << 24)
            | ((long)bytes[3] << 16)
            | ((long)bytes[4] << 8)
            | bytes[5];

        return DateTimeOffset.FromUnixTimeMilliseconds(milissegundos);
    }

    /// <summary>
    /// Identidade sintética com o timestamp de <paramref name="instante"/> e os demais 80 bits
    /// zerados — nunca persistida, usada apenas como fronteira de comparação no keyset.
    /// Satisfaz Piso(t) &lt;= id &lt; Piso(t+1ms) para qualquer id v7 real gerado no instante t.
    /// </summary>
    public static Guid Piso(DateTimeOffset instante)
    {
        var milissegundos = instante.ToUnixTimeMilliseconds();

        Span<byte> bytes = stackalloc byte[16];
        bytes[0] = (byte)(milissegundos >> 40);
        bytes[1] = (byte)(milissegundos >> 32);
        bytes[2] = (byte)(milissegundos >> 24);
        bytes[3] = (byte)(milissegundos >> 16);
        bytes[4] = (byte)(milissegundos >> 8);
        bytes[5] = (byte)milissegundos;

        return new Guid(bytes, bigEndian: true);
    }
}
