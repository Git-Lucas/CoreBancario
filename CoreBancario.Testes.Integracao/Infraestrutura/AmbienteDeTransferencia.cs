using System.Text.Json;
using CoreBancario.Aplicacao.Transferencias;
using CoreBancario.Dominio.Identidades;
using CoreBancario.Dominio.Ledger;
using CoreBancario.Infraestrutura.Mensageria;
using CoreBancario.Infraestrutura.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;

namespace CoreBancario.Testes.Integracao.Infraestrutura;

/// <summary>
/// Fiação real (não mock) dos componentes do fluxo de transferência para os testes narrow da
/// seção 9 — a mesma composição de `Program.cs` (API/Worker), sem o aparato de DI, apontada
/// para os containers descartáveis da suíte. Cada operação recebe um `CoreBancarioDbContext`
/// novo, espelhando o escopo por mensagem que o Worker usa em produção.
/// </summary>
public sealed class AmbienteDeTransferencia(PostgreSqlFixture postgres, IConnection conexaoRabbitMq)
{
    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    public IConnection ConexaoRabbitMq => conexaoRabbitMq;

    public SolicitarTransferencia NovoCasoDeSolicitacao() =>
        new(
            new PublicadorDeTransferencia(conexaoRabbitMq, NullLogger<PublicadorDeTransferencia>.Instance),
            NullLogger<SolicitarTransferencia>.Instance);

    public static LiquidarTransferencia NovoCasoDeLiquidacao(CoreBancarioDbContext contexto) =>
        new(
            new ResolucaoDeContraparteRepositorio(contexto),
            new RegistroDeLiquidacaoRepositorio(contexto),
            NullLogger<LiquidarTransferencia>.Instance);

    public CoreBancarioDbContext NovoContexto() =>
        new(new DbContextOptionsBuilder<CoreBancarioDbContext>().UseNpgsql(postgres.ConnectionString).Options);

    public async Task<ResultadoLiquidacao> LiquidarAsync(SolicitacaoDeTransferencia solicitacao, CancellationToken cancellationToken)
    {
        await using var contexto = NovoContexto();
        return await NovoCasoDeLiquidacao(contexto).ExecutarAsync(solicitacao, cancellationToken);
    }

    public static SolicitacaoDeTransferencia Desserializar(ReadOnlyMemory<byte> corpo)
    {
        var mensagem = JsonSerializer.Deserialize<MensagemTransferencia>(corpo.Span, OpcoesJson)
            ?? throw new InvalidOperationException("Corpo da mensagem vazio ou inválido.");

        return new SolicitacaoDeTransferencia(
            new LiquidacaoId(mensagem.LiquidacaoId),
            new ContaId(mensagem.ContaOrigem),
            new ContaId(mensagem.ContaDestino),
            new Dinheiro(mensagem.Valor, Enum.Parse<Moeda>(mensagem.Moeda)));
    }
}
