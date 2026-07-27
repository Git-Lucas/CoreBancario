using CoreBancario.Dominio.Identidades;

namespace CoreBancario.Aplicacao.Transferencias;

public sealed record ComandoSolicitarTransferencia(Guid ContaOrigem, Guid ContaDestino, decimal Valor);

/// <summary>Fechado por construção: os três casos abaixo são as únicas variantes possíveis.</summary>
public abstract record ResultadoSolicitacaoTransferencia
{
    private ResultadoSolicitacaoTransferencia()
    {
    }

    public sealed record Aceita(LiquidacaoId LiquidacaoId) : ResultadoSolicitacaoTransferencia;

    public sealed record RejeitadaNaValidacao(string Motivo) : ResultadoSolicitacaoTransferencia;

    public sealed record FalhaAoPublicar(string Motivo) : ResultadoSolicitacaoTransferencia;
}
