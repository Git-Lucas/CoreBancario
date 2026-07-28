using System.Buffers.Text;
using CoreBancario.Dominio.Identidades;

namespace CoreBancario.Aplicacao.Extrato;

/// <summary>
/// Cursor opaco simples: Base64Url do id, sem assinatura. Um cursor assinado (HMAC) seria a
/// resposta de produção quando houver autorização por conta — hoje não há "conta autorizada" da
/// qual escapar, e o período em si é reaplicado a cada página a partir da requisição, não do
/// cursor, então adulterar o cursor não amplia o que o cliente já pode pedir.
/// </summary>
public static class CursorDeExtrato
{
    public static string Codificar(LancamentoId id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.Valor.TryWriteBytes(bytes);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>Tolerante: cursor ausente ou malformado devolve null em vez de lançar.</summary>
    public static LancamentoId? Decodificar(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        Span<byte> destino = stackalloc byte[16];
        if (!Base64Url.TryDecodeFromChars(cursor, destino, out var escritos) || escritos != 16)
        {
            return null;
        }

        return new LancamentoId(new Guid(destino));
    }
}
