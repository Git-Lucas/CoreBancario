using CoreBancario.Aplicacao.Transferencias;
using CoreBancario.Infraestrutura.Mensageria;
using CoreBancario.Infraestrutura.Persistencia;
using CoreBancario.Testes.Integracao.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace CoreBancario.Testes.Integracao.Persistencia;

/// <summary>
/// Fluxo de transferência ponta a ponta contra RabbitMQ e PostgreSQL reais via Testcontainers
/// (seção 9 em tasks.md). O laço de consumo é conduzido manualmente por `BasicGetAsync` — o
/// mesmo `ack`/`nack` que `ConsumidorDeTransferencias` faria — para controlar deliveries com
/// precisão, sem depender de tempo de espera de um `BackgroundService` em segundo plano.
/// </summary>
[Collection(nameof(TransferenciaColecaoDeTestes))]
public class FluxoDeTransferenciaTestes(PostgreSqlFixture postgres, RabbitMqFixture rabbit) : IAsyncLifetime
{
    private IConnection _conexao = null!;
    private AmbienteDeTransferencia _ambiente = null!;

    public async ValueTask InitializeAsync()
    {
        await postgres.TruncateAsync();

        _conexao = await rabbit.CriarConexaoAsync();
        await TopologiaDeMensageria.DeclararAsync(_conexao);

        await using var canal = await _conexao.CreateChannelAsync();
        await canal.QueuePurgeAsync(TopologiaDeMensageria.FilaPrincipal);
        await canal.QueuePurgeAsync(TopologiaDeMensageria.FilaDeDescartes);

        _ambiente = new AmbienteDeTransferencia(postgres, _conexao);
    }

    public async ValueTask DisposeAsync()
    {
        await _conexao.DisposeAsync();
    }

    [Fact]
    public async Task Idempotencia_ReentregaDaMesmaMensagem_ProduzUmUnicoParEASegundaEntregaEhSucesso()
    {
        var ct = TestContext.Current.CancellationToken;
        var comando = new ComandoSolicitarTransferencia(Guid.NewGuid(), Guid.NewGuid(), 100.00m);
        var aceita = Assert.IsType<ResultadoSolicitacaoTransferencia.Aceita>(
            await _ambiente.NovoCasoDeSolicitacao().ExecutarAsync(comando, ct));

        await using var canal = await _conexao.CreateChannelAsync(cancellationToken: ct);

        // 1ª entrega: liquida de verdade, mas NÃO confirma — simula falha do Worker entre a
        // persistência e o ack (D6 em design.md).
        var primeiraEntrega = await ReceberComEsperaAsync(canal, ct);
        var solicitacao = AmbienteDeTransferencia.Desserializar(primeiraEntrega.Body);
        Assert.Equal(ResultadoLiquidacao.Liquidada, await _ambiente.LiquidarAsync(solicitacao, ct));
        await canal.BasicRejectAsync(primeiraEntrega.DeliveryTag, requeue: true, ct);

        // 2ª entrega (reentrega do broker, sem qualquer ação do cliente): o índice único
        // absorve, tratado como sucesso.
        var segundaEntrega = await ReceberComEsperaAsync(canal, ct);
        Assert.True(segundaEntrega.Redelivered);
        var resultadoSegunda = await _ambiente.LiquidarAsync(AmbienteDeTransferencia.Desserializar(segundaEntrega.Body), ct);
        Assert.Equal(ResultadoLiquidacao.JaLiquidada, resultadoSegunda);
        await canal.BasicAckAsync(segundaEntrega.DeliveryTag, multiple: false, ct);

        await using var contexto = _ambiente.NovoContexto();
        var quantidade = await contexto.Lancamentos.CountAsync(l => l.LiquidacaoId == aceita.LiquidacaoId, ct);
        Assert.Equal(2, quantidade);
    }

    [Fact]
    public async Task ReentregaAbsorvida_NaoChegaAFilaDeDescartes()
    {
        var ct = TestContext.Current.CancellationToken;
        var comando = new ComandoSolicitarTransferencia(Guid.NewGuid(), Guid.NewGuid(), 55.00m);
        await _ambiente.NovoCasoDeSolicitacao().ExecutarAsync(comando, ct);

        await using var canal = await _conexao.CreateChannelAsync(cancellationToken: ct);

        var primeiraEntrega = await ReceberComEsperaAsync(canal, ct);
        var solicitacao = AmbienteDeTransferencia.Desserializar(primeiraEntrega.Body);
        await _ambiente.LiquidarAsync(solicitacao, ct);
        await canal.BasicRejectAsync(primeiraEntrega.DeliveryTag, requeue: true, ct);

        var segundaEntrega = await ReceberComEsperaAsync(canal, ct);
        await _ambiente.LiquidarAsync(AmbienteDeTransferencia.Desserializar(segundaEntrega.Body), ct);
        await canal.BasicAckAsync(segundaEntrega.DeliveryTag, multiple: false, ct);

        var descartes = await canal.QueueDeclarePassiveAsync(TopologiaDeMensageria.FilaDeDescartes, ct);
        Assert.Equal(0u, descartes.MessageCount);
    }

    [Fact]
    public async Task Descarte_MensagemQueSempreFalha_ChegaADlqApos3EntregasSemBloquearAsDemais()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var canal = await _conexao.CreateChannelAsync(cancellationToken: ct);

        var idVenenoso = await PublicarMensagemVenenosaAsync(canal, ct);

        // x-delivery-limit=3 permite 3 reentregas (1.2 em tasks.md/evidencias): 4 tentativas no
        // total antes do dead-letter, confirmadas aqui pelo mesmo MessageId em todas elas.
        for (var tentativa = 0; tentativa < 4; tentativa++)
        {
            var entrega = await ReceberComEsperaAsync(canal, ct);
            Assert.Equal(idVenenoso, entrega.BasicProperties.MessageId);
            await canal.BasicRejectAsync(entrega.DeliveryTag, requeue: true, ct);
        }

        var descartes = await AguardarContagemAsync(canal, TopologiaDeMensageria.FilaDeDescartes, esperado: 1, ct);
        Assert.Equal(1u, descartes);

        var principal = await canal.QueueDeclarePassiveAsync(TopologiaDeMensageria.FilaPrincipal, ct);
        Assert.Equal(0u, principal.MessageCount);

        // A fila principal continua processando normalmente uma transferência válida depois.
        var comandoValido = new ComandoSolicitarTransferencia(Guid.NewGuid(), Guid.NewGuid(), 10.00m);
        var aceitaValida = Assert.IsType<ResultadoSolicitacaoTransferencia.Aceita>(
            await _ambiente.NovoCasoDeSolicitacao().ExecutarAsync(comandoValido, ct));

        var entregaValida = await ReceberComEsperaAsync(canal, ct);
        var solicitacaoValida = AmbienteDeTransferencia.Desserializar(entregaValida.Body);
        Assert.Equal(aceitaValida.LiquidacaoId, solicitacaoValida.LiquidacaoId);
        Assert.Equal(ResultadoLiquidacao.Liquidada, await _ambiente.LiquidarAsync(solicitacaoValida, ct));
        await canal.BasicAckAsync(entregaValida.DeliveryTag, multiple: false, ct);
    }

    [Fact]
    public async Task RegistroDoDescarte_ContemLiquidacaoIdTentativasMotivoECorpoBruto()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var canal = await _conexao.CreateChannelAsync(cancellationToken: ct);

        var idVenenoso = await PublicarMensagemVenenosaAsync(canal, ct);

        for (var tentativa = 0; tentativa < 4; tentativa++)
        {
            var entrega = await ReceberComEsperaAsync(canal, ct);
            await canal.BasicRejectAsync(entrega.DeliveryTag, requeue: true, ct);
        }

        var mensagemDescartada = await ReceberComEsperaAsync(canal, TopologiaDeMensageria.FilaDeDescartes, ct);

        Assert.Equal(idVenenoso, mensagemDescartada.BasicProperties.MessageId);
        Assert.Equal(4, CabecalhosDeMensageria.LerTentativas(mensagemDescartada.BasicProperties.Headers));
        Assert.Equal("delivery_limit", CabecalhosDeMensageria.LerMotivoDoDescarte(mensagemDescartada.BasicProperties.Headers));
        Assert.Equal("corpo inválido de propósito", System.Text.Encoding.UTF8.GetString(mensagemDescartada.Body.Span));

        await canal.BasicAckAsync(mensagemDescartada.DeliveryTag, multiple: false, ct);
    }

    [Fact]
    public async Task InterrupcaoEntreConsumoEPersistencia_MensagemEhReprocessadaSemLancamentoParcialEComEfeitoUnico()
    {
        var ct = TestContext.Current.CancellationToken;
        var comando = new ComandoSolicitarTransferencia(Guid.NewGuid(), Guid.NewGuid(), 30.00m);
        var aceita = Assert.IsType<ResultadoSolicitacaoTransferencia.Aceita>(
            await _ambiente.NovoCasoDeSolicitacao().ExecutarAsync(comando, ct));

        await using var canal = await _conexao.CreateChannelAsync(cancellationToken: ct);

        // 1ª entrega: "recebida" pelo consumidor e imediatamente perdida antes de qualquer
        // persistência — não chama LiquidarTransferencia, só nack com requeue.
        var primeiraEntrega = await ReceberComEsperaAsync(canal, ct);
        await canal.BasicRejectAsync(primeiraEntrega.DeliveryTag, requeue: true, ct);

        await using (var contextoAntes = _ambiente.NovoContexto())
        {
            var quantidadeAntes = await contextoAntes.Lancamentos
                .CountAsync(l => l.LiquidacaoId == aceita.LiquidacaoId, ct);
            Assert.Equal(0, quantidadeAntes);
        }

        // 2ª entrega: processa de verdade.
        var segundaEntrega = await ReceberComEsperaAsync(canal, ct);
        var solicitacao = AmbienteDeTransferencia.Desserializar(segundaEntrega.Body);
        Assert.Equal(ResultadoLiquidacao.Liquidada, await _ambiente.LiquidarAsync(solicitacao, ct));
        await canal.BasicAckAsync(segundaEntrega.DeliveryTag, multiple: false, ct);

        await using var contextoDepois = _ambiente.NovoContexto();
        var quantidadeDepois = await contextoDepois.Lancamentos
            .CountAsync(l => l.LiquidacaoId == aceita.LiquidacaoId, ct);
        Assert.Equal(2, quantidadeDepois);
    }

    [Fact]
    public async Task ResolucaoDeNome_ContaSemeadaUsaNomeDoSeed_ContaIneditaGeraNomeRepetidoNaSeguinte()
    {
        var ct = TestContext.Current.CancellationToken;

        var contaConhecida = Dominio.Identidades.ContaId.Nova();
        var nomeConhecido = "Titular Semeado";
        await using (var contexto = _ambiente.NovoContexto())
        {
            contexto.Lancamentos.Add(new Dominio.Ledger.Lancamento(
                Dominio.Identidades.LancamentoId.Nova(),
                Dominio.Identidades.ContaId.Nova(),
                Dominio.Identidades.LiquidacaoId.Nova(),
                new Dominio.Ledger.Dinheiro(10m, Dominio.Ledger.Moeda.BRL),
                contaConhecida,
                nomeConhecido));
            await contexto.SaveChangesAsync(ct);
        }

        var contaInedita = Dominio.Identidades.ContaId.Nova();

        // Transferência 1: destino é a conta "semeada" — deve gravar o nome já existente.
        var comando1 = new ComandoSolicitarTransferencia(contaInedita.Valor, contaConhecida.Valor, 12.00m);
        await _ambiente.NovoCasoDeSolicitacao().ExecutarAsync(comando1, ct);

        await using var canal = await _conexao.CreateChannelAsync(cancellationToken: ct);
        var entrega1 = await ReceberComEsperaAsync(canal, ct);
        var solicitacao1 = AmbienteDeTransferencia.Desserializar(entrega1.Body);
        await _ambiente.LiquidarAsync(solicitacao1, ct);
        await canal.BasicAckAsync(entrega1.DeliveryTag, multiple: false, ct);

        string nomeGeradoParaContaInedita;
        await using (var contexto = _ambiente.NovoContexto())
        {
            var linhaDaContaConhecida = await contexto.Lancamentos.AsNoTracking()
                .Where(l => l.ContraparteId == contaConhecida)
                .OrderByDescending(l => l.Id)
                .FirstAsync(ct);
            Assert.Equal(nomeConhecido, linhaDaContaConhecida.ContraparteNome);

            var linhaDaContaInedita = await contexto.Lancamentos.AsNoTracking()
                .Where(l => l.ContraparteId == contaInedita)
                .FirstAsync(ct);
            nomeGeradoParaContaInedita = linhaDaContaInedita.ContraparteNome;
            Assert.False(string.IsNullOrWhiteSpace(nomeGeradoParaContaInedita));
        }

        // Transferência 2: a conta antes inédita agora é contraparte de novo — o nome gerado na
        // primeira vez deve se repetir, resolvido por consulta (D4 em design.md).
        var comando2 = new ComandoSolicitarTransferencia(Dominio.Identidades.ContaId.Nova().Valor, contaInedita.Valor, 8.00m);
        await _ambiente.NovoCasoDeSolicitacao().ExecutarAsync(comando2, ct);

        var entrega2 = await ReceberComEsperaAsync(canal, ct);
        var solicitacao2 = AmbienteDeTransferencia.Desserializar(entrega2.Body);
        await _ambiente.LiquidarAsync(solicitacao2, ct);
        await canal.BasicAckAsync(entrega2.DeliveryTag, multiple: false, ct);

        await using var contextoFinal = _ambiente.NovoContexto();
        var linhaRepetida = await contextoFinal.Lancamentos.AsNoTracking()
            .Where(l => l.ContraparteId == contaInedita)
            .OrderByDescending(l => l.Id)
            .FirstAsync(ct);
        Assert.Equal(nomeGeradoParaContaInedita, linhaRepetida.ContraparteNome);
    }

    private static async Task<string> PublicarMensagemVenenosaAsync(IChannel canal, CancellationToken ct)
    {
        var liquidacaoId = Guid.CreateVersion7().ToString();
        var propriedades = new BasicProperties { Persistent = true, MessageId = liquidacaoId };
        var corpoInvalido = "corpo inválido de propósito"u8.ToArray();

        await canal.BasicPublishAsync(
            TopologiaDeMensageria.Exchange, TopologiaDeMensageria.RoutingKey,
            mandatory: true, basicProperties: propriedades, body: corpoInvalido, cancellationToken: ct);

        return liquidacaoId;
    }

    private static Task<BasicGetResult> ReceberComEsperaAsync(IChannel canal, CancellationToken ct) =>
        EsperaDeMensageria.ReceberComEsperaAsync(canal, ct);

    private static Task<BasicGetResult> ReceberComEsperaAsync(IChannel canal, string fila, CancellationToken ct) =>
        EsperaDeMensageria.ReceberComEsperaAsync(canal, fila, ct);

    private static async Task<uint> AguardarContagemAsync(IChannel canal, string fila, uint esperado, CancellationToken ct)
    {
        for (var tentativa = 0; tentativa < 50; tentativa++)
        {
            var declaracao = await canal.QueueDeclarePassiveAsync(fila, ct);
            if (declaracao.MessageCount == esperado)
            {
                return declaracao.MessageCount;
            }

            await Task.Delay(100, ct);
        }

        return (await canal.QueueDeclarePassiveAsync(fila, ct)).MessageCount;
    }
}
