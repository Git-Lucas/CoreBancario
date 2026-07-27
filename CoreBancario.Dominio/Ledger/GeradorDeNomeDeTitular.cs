using CoreBancario.Dominio.Identidades;

namespace CoreBancario.Dominio.Ledger;

/// <summary>
/// Invenção determinística de nome de titular para conta inédita no ledger (D4/D5 em design.md).
/// Função pura sobre os bytes do <see cref="ContaId"/>: mesma entrada sempre produz a mesma
/// saída, o que torna inofensiva a corrida entre duas liquidações concorrentes inventando o
/// nome da mesma conta pela primeira vez.
/// </summary>
public static class GeradorDeNomeDeTitular
{
    private static readonly string[] PrimeirosNomes =
    [
        "Alice", "Bento", "Clara", "Diego", "Elena", "Fabio", "Giulia", "Heitor", "Ines", "Joaquim",
        "Laura", "Marcos", "Nina", "Otavio", "Paula", "Quintino", "Renata", "Sergio", "Tania", "Ulisses",
        "Valeria", "Wagner", "Ximena", "Yara", "Zeno", "Aurora", "Benicio", "Camila", "Davi", "Estela",
        "Fernanda", "Gustavo", "Helena", "Icaro", "Julia", "Kaique", "Luiza", "Mateus", "Noemi", "Oscar",
    ];

    private static readonly string[] Sobrenomes =
    [
        "Andrade", "Barros", "Cunha", "Dutra", "Esteves", "Farias", "Guimaraes", "Henriques", "Iglesias", "Junqueira",
        "Klein", "Leal", "Machado", "Neves", "Ortiz", "Peixoto", "Queiroz", "Ramalho", "Siqueira", "Torres",
        "Uchoa", "Vale", "Wanderley", "Xavier", "Yamamoto", "Zimmer", "Abreu", "Bicalho", "Coutinho", "Delgado",
        "Escobar", "Falcao", "Godoi", "Homem", "Ivo", "Jardim", "Krause", "Lacerda", "Mesquita", "Novaes",
    ];

    public static string Gerar(ContaId contaId)
    {
        Span<byte> bytes = stackalloc byte[16];
        contaId.Valor.TryWriteBytes(bytes, bigEndian: true, out _);

        var indicePrimeiro = ChecksumFnv1a(bytes[..8]) % (uint)PrimeirosNomes.Length;
        var indiceSobrenome = ChecksumFnv1a(bytes[8..]) % (uint)Sobrenomes.Length;

        return $"{PrimeirosNomes[indicePrimeiro]} {Sobrenomes[indiceSobrenome]}";
    }

    // FNV-1a: hash não criptográfico, determinístico, com boa dispersão para chaves pequenas.
    private static uint ChecksumFnv1a(ReadOnlySpan<byte> bytes)
    {
        var acumulado = 2166136261u;
        foreach (var b in bytes)
        {
            acumulado ^= b;
            acumulado *= 16777619u;
        }

        return acumulado;
    }
}
