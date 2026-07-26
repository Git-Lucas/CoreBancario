# CoreBancario

## Configuração

A conexão com o PostgreSQL é lida de `ConnectionStrings:CoreBancario` (`appsettings.json`), com o valor de desenvolvimento apontando para `localhost:5432`. Para sobrescrever — por exemplo em outro ambiente — defina a variável de ambiente `ConnectionStrings__CoreBancario`.

## Como rodar localmente

1. **Subir o PostgreSQL 18** (requisito de versão — ver `docs/prd/PRD-1-ledger-e-extrato.md`):

   ```
   docker compose up -d
   ```

2. **Iniciar a API** — aplica as migrations pendentes automaticamente (tolerando o banco ainda subindo) e passa a responder em `/sistema/saude`:

   ```
   dotnet run --project CoreBancario.Api
   ```

3. **Semear a massa de dados** (600.000 liquidações → 1.200.000 lançamentos, distribuição enviesada — ver PRD-1 C1.9). Repetível: cada execução esvazia e recarrega a tabela. Leva cerca de 25 a 40 segundos:

   ```
   dotnet run --project CoreBancario.Worker -- --seed
   ```

4. **Consultar o extrato**:

   ```
   curl "http://localhost:5046/contas/{contaId}/extrato?de=2024-01-01T00:00:00Z&ate=2026-12-31T00:00:00Z"
   ```

## Testes

- `dotnet test --project CoreBancario.Testes.Unidade` — testes unitários de domínio, sem I/O.
- `dotnet test --project CoreBancario.Testes.Integracao` — testes de integração narrow contra PostgreSQL real via Testcontainers (não precisa de nada rodando antes), mais os testes de plano de execução e custo de acesso (`EXPLAIN ANALYZE`), que exigem o banco de desenvolvimento **semeado** dos passos acima — se não estiver disponível, esses testes pulam com um motivo explícito em vez de falhar.