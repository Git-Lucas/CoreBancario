using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;

namespace CoreBancario.Testes.Integracao.Infraestrutura;

internal static class ConstrutorDeTeste
{
    public static Lancamento NovoLancamento(
        ContaId? contaId = null,
        LiquidacaoId? liquidacaoId = null,
        Dinheiro? valor = null,
        ContaId? contraparteId = null,
        string contraparteNome = "Fulano de Tal") =>
        new(
            LancamentoId.Nova(),
            contaId ?? ContaId.Nova(),
            liquidacaoId ?? LiquidacaoId.Nova(),
            valor ?? new Dinheiro(100.00m, Moeda.BRL),
            contraparteId ?? ContaId.Nova(),
            contraparteNome);
}
