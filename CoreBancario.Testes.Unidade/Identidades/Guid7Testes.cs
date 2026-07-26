using CoreBancario.Dominio.Identidades;

namespace CoreBancario.Testes.Unidade.Identidades;

public class Guid7Testes
{
    [Fact]
    public void ExtrairInstante_DevolveOMilissegundoCodificadoNoId()
    {
        var instante = DateTimeOffset.Parse("2026-03-15T10:30:00.123Z", System.Globalization.CultureInfo.InvariantCulture);
        var id = LancamentoId.Piso(instante).Valor;

        var extraido = Guid7.ExtrairInstante(id);

        Assert.Equal(instante, extraido);
    }

    [Fact]
    public void Piso_ZeraOsOitentaBitsMenosSignificativos()
    {
        var instante = DateTimeOffset.UtcNow;

        var piso = Guid7.Piso(instante);

        Span<byte> bytes = stackalloc byte[16];
        piso.TryWriteBytes(bytes, bigEndian: true, out _);

        for (var i = 6; i < 16; i++)
        {
            Assert.Equal(0, bytes[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(999)]
    public void Piso_SatisfazAPropriedadeDeFronteiraParaIdsReaisDoMesmoInstante(int deslocamentoMs)
    {
        var instante = DateTimeOffset.Parse("2026-06-01T00:00:00.000Z", System.Globalization.CultureInfo.InvariantCulture).AddMilliseconds(deslocamentoMs);
        var id = Guid.CreateVersion7(instante);

        var piso = Guid7.Piso(instante);
        var pisoProximo = Guid7.Piso(instante.AddMilliseconds(1));

        Assert.True(piso.CompareTo(id) <= 0);
        Assert.True(id.CompareTo(pisoProximo) < 0);
    }
}
