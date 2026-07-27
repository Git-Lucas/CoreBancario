namespace CoreBancario.Infraestrutura.Mensageria;

/// <summary>
/// Contrato de fio (D12/C2.12 em design.md/PRD-2): exclusivamente o que o solicitante informou.
/// Sem nomes — nome não é dado do comando, é preenchimento do ledger, resolvido no consumidor.
/// </summary>
public sealed record MensagemTransferencia(
    Guid LiquidacaoId,
    Guid ContaOrigem,
    Guid ContaDestino,
    decimal Valor,
    string Moeda);
