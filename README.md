# CoreBancario

## Configuração

A conexão com o PostgreSQL é lida de `ConnectionStrings:CoreBancario` (`appsettings.json`), com o valor de desenvolvimento apontando para `localhost:5432`. Para sobrescrever — por exemplo em outro ambiente — defina a variável de ambiente `ConnectionStrings__CoreBancario`.

A conexão com o RabbitMQ é lida de `ConnectionStrings:RabbitMQ`, com o valor de desenvolvimento apontando para `localhost:5672` (usuário e senha `corebancario`, os mesmos do `docker-compose.yml` — credenciais de desenvolvimento, não reais). Para sobrescrever, defina a variável de ambiente `ConnectionStrings__RabbitMQ`. O broker sobe junto do `docker compose up -d` (painel de administração em `http://localhost:15672`).

## Como rodar localmente

1. **Subir o PostgreSQL 18 e o RabbitMQ** (requisitos de versão — ver `docs/prd/PRD-1-ledger-e-extrato.md` e `docs/prd/PRD-2-transferencia-assincrona.md`):

   ```
   docker compose up -d
   ```

2. **Iniciar a API** — aplica as migrations pendentes automaticamente (tolerando o banco ainda subindo), declara a topologia de mensageria (idempotente) e passa a responder em `/sistema/saude`:

   ```
   dotnet run --project CoreBancario.Api
   ```

3. **Iniciar o Worker** — hospeda os dois consumidores (liquidação e descartes) e passa a processar transferências publicadas pela API:

   ```
   dotnet run --project CoreBancario.Worker
   ```

4. **Semear a massa de dados** (600.000 liquidações → 1.200.000 lançamentos, distribuição enviesada — ver PRD-1 C1.9). Repetível: cada execução esvazia e recarrega a tabela. Leva cerca de 25 a 40 segundos:

   ```
   dotnet run --project CoreBancario.Worker -- --seed
   ```

5. **Consultar o extrato**:

   ```
   curl "http://localhost:5046/contas/{contaId}/extrato?de=2024-01-01T00:00:00Z&ate=2026-12-31T00:00:00Z"
   ```

## Transferência assíncrona (PRD-2)

Solicitar uma transferência: a API valida estruturalmente, publica no RabbitMQ com publisher confirms e responde antes de qualquer lançamento existir no ledger.

```
curl -i -X POST http://localhost:5046/transferencias \
  -H "content-type: application/json" \
  -d '{"contaOrigem":"<guid>","contaDestino":"<guid>","valor":100.00}'
```

- `202 Accepted` com `{"liquidacaoId": "..."}` — aceita; o Worker liquida de forma assíncrona.
- `400` — falha de validação estrutural (valor não positivo, identificador malformado, origem igual ao destino).
- `503` — falha ou expiração da confirmação do broker; nada foi publicado.

Não há endpoint de status (decisão registrada em `design.md` da change `add-async-transfer`, D2): a visibilidade do fluxo é o log estruturado dos dois processos e o consumidor de descartes. Para acompanhar uma transferência, use o `liquidacaoId` da resposta como filtro sobre os logs — formatados em JSON (`Scopes[].LiquidacaoId`) — da API e do Worker; ele localiza recebimento, publicação, consumo e liquidação sem nenhum outro insumo. Uma transferência que esgota as tentativas (`x-delivery-limit = 3`) aparece registrada pelo `ConsumidorDeDescartes`, com `liquidacaoId`, tentativas, motivo e corpo bruto — o único lugar em que uma transferência morta é observável.

O painel do RabbitMQ (`http://localhost:15672`, credenciais `corebancario`/`corebancario`) mostra a topologia declarada: exchange `corebancario.transferencias`, fila `transferencias` (quorum, `x-delivery-limit = 3`), dead-letter exchange `corebancario.transferencias.dlx` e fila de descartes `transferencias.dlq`.

## Testes

- `dotnet test --project CoreBancario.Testes.Unidade` — testes unitários de domínio, sem I/O.
- `dotnet test --project CoreBancario.Testes.Integracao` — testes de integração narrow contra PostgreSQL e RabbitMQ reais via Testcontainers (não precisa de nada rodando antes), mais os testes de plano de execução e custo de acesso (`EXPLAIN ANALYZE`), que exigem o banco de desenvolvimento **semeado** dos passos acima — se não estiver disponível, esses testes pulam com um motivo explícito em vez de falhar.