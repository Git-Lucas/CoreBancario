using CoreBancario.Aplicacao.Transferencias;
using Microsoft.Extensions.Logging.Abstractions;

namespace CoreBancario.Testes.Unidade.Transferencias;

public class SolicitarTransferenciaTestes
{
    [Fact]
    public async Task ExecutarAsync_ValorNaoPositivo_EhRejeitadaENadaEhPublicado()
    {
        var publicador = new PublicadorFalso();
        var caso = new SolicitarTransferencia(publicador, NullLogger<SolicitarTransferencia>.Instance);
        var comando = new ComandoSolicitarTransferencia(Guid.NewGuid(), Guid.NewGuid(), 0m);

        var resultado = await caso.ExecutarAsync(comando, TestContext.Current.CancellationToken);

        Assert.IsType<ResultadoSolicitacaoTransferencia.RejeitadaNaValidacao>(resultado);
        Assert.False(publicador.Chamado);
    }

    [Fact]
    public async Task ExecutarAsync_ValorNegativo_EhRejeitadaENadaEhPublicado()
    {
        var publicador = new PublicadorFalso();
        var caso = new SolicitarTransferencia(publicador, NullLogger<SolicitarTransferencia>.Instance);
        var comando = new ComandoSolicitarTransferencia(Guid.NewGuid(), Guid.NewGuid(), -10m);

        var resultado = await caso.ExecutarAsync(comando, TestContext.Current.CancellationToken);

        Assert.IsType<ResultadoSolicitacaoTransferencia.RejeitadaNaValidacao>(resultado);
        Assert.False(publicador.Chamado);
    }

    [Fact]
    public async Task ExecutarAsync_IdentificadorDeContaVazio_EhRejeitadaENadaEhPublicado()
    {
        var publicador = new PublicadorFalso();
        var caso = new SolicitarTransferencia(publicador, NullLogger<SolicitarTransferencia>.Instance);
        var comando = new ComandoSolicitarTransferencia(Guid.Empty, Guid.NewGuid(), 10m);

        var resultado = await caso.ExecutarAsync(comando, TestContext.Current.CancellationToken);

        Assert.IsType<ResultadoSolicitacaoTransferencia.RejeitadaNaValidacao>(resultado);
        Assert.False(publicador.Chamado);
    }

    [Fact]
    public async Task ExecutarAsync_OrigemIgualAoDestino_EhRejeitadaENadaEhPublicado()
    {
        var publicador = new PublicadorFalso();
        var caso = new SolicitarTransferencia(publicador, NullLogger<SolicitarTransferencia>.Instance);
        var contaId = Guid.NewGuid();
        var comando = new ComandoSolicitarTransferencia(contaId, contaId, 10m);

        var resultado = await caso.ExecutarAsync(comando, TestContext.Current.CancellationToken);

        Assert.IsType<ResultadoSolicitacaoTransferencia.RejeitadaNaValidacao>(resultado);
        Assert.False(publicador.Chamado);
    }

    [Fact]
    public async Task ExecutarAsync_ContaDestinoInedita_EhAceitaSemVerificacaoDeExistencia()
    {
        var publicador = new PublicadorFalso();
        var caso = new SolicitarTransferencia(publicador, NullLogger<SolicitarTransferencia>.Instance);
        var comando = new ComandoSolicitarTransferencia(Guid.NewGuid(), Guid.NewGuid(), 10m);

        var resultado = await caso.ExecutarAsync(comando, TestContext.Current.CancellationToken);

        var aceita = Assert.IsType<ResultadoSolicitacaoTransferencia.Aceita>(resultado);
        Assert.True(publicador.Chamado);
        Assert.Equal(publicador.UltimaSolicitacao!.LiquidacaoId, aceita.LiquidacaoId);
    }

    [Fact]
    public async Task ExecutarAsync_FalhaDoConfirmDoBroker_ResultaEmFalhaAoPublicar()
    {
        var publicador = new PublicadorFalso(confirmar: false);
        var caso = new SolicitarTransferencia(publicador, NullLogger<SolicitarTransferencia>.Instance);
        var comando = new ComandoSolicitarTransferencia(Guid.NewGuid(), Guid.NewGuid(), 10m);

        var resultado = await caso.ExecutarAsync(comando, TestContext.Current.CancellationToken);

        Assert.IsType<ResultadoSolicitacaoTransferencia.FalhaAoPublicar>(resultado);
    }

    private sealed class PublicadorFalso(bool confirmar = true) : IPublicadorDeTransferencia
    {
        public bool Chamado { get; private set; }

        public SolicitacaoDeTransferencia? UltimaSolicitacao { get; private set; }

        public Task<ResultadoPublicacao> PublicarAsync(
            SolicitacaoDeTransferencia solicitacao, CancellationToken cancellationToken)
        {
            Chamado = true;
            UltimaSolicitacao = solicitacao;
            return Task.FromResult(confirmar
                ? ResultadoPublicacao.Sucesso()
                : ResultadoPublicacao.Falha("broker indisponível"));
        }
    }
}
