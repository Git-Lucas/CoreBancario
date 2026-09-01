# CoreBancario

## O que é este projeto

Projeto de estudo — uma fatia de um core bancário, construída para preparação de arguição técnica sênior em .NET. Não é (nem tenta ser) um sistema de produção: cobre em profundidade os quatro problemas escolhidos como foco — persistência de alto volume, mensageria confiável, orquestração em Kubernetes e observabilidade — e deixa outros deliberadamente de fora (cadastro de contas, saldo, autenticação, multi-moeda, entre outros) para caber no prazo sem virar superficial em tudo. Arquitetura Clean/Hexagonal/DDD sobre um único bounded context, em camadas por projeto: `CoreBancario.Dominio` (entidades e ValueObjects, sem dependência de framework), `CoreBancario.Aplicacao` (casos de uso e ports), `CoreBancario.Infraestrutura` (adapters — EF Core/Npgsql, RabbitMQ.Client) e os hosts `CoreBancario.Api`/`CoreBancario.Worker`.

### Ledger e extrato performático

**Problema:** um sistema financeiro registra movimentações como histórico imutável — partida dobrada, todo lançamento tem um débito e um crédito pareados que somam zero — e consultar o extrato de uma conta sobre milhões de linhas, sem os cuidados corretos de indexação e paginação, degrada rapidamente.

**Solução:** tabela única `lancamentos`, append-only — trigger no banco rejeita `UPDATE`/`DELETE`, e o domínio não expõe mutação. A identidade é `Guid` v7 gerada em .NET (nunca pelo banco), o que embute o timestamp no próprio identificador; isso sustenta paginação por keyset (nunca `OFFSET`) e a tradução do filtro de período em `Index Cond` de índice, não `Filter` residual. Um índice de cobertura (`INCLUDE`) sobre `(conta_id, id DESC)` viabiliza `Index Only Scan` sem tocar o heap, e a leitura é projetada direto para DTO, sem tracking. O modelo é desnormalizado — a contraparte é gravada na própria linha — porque não existe tabela de contas: decisão de escopo consciente, não descuido.

### Transferência assíncrona

**Problema:** núcleos de pagamento não liquidam de forma síncrona no request HTTP — a API registra a intenção e responde rápido enquanto a liquidação ocorre desacoplada, o que introduz os problemas reais de mensageria: entrega duplicada (at-least-once), perda em restart, mensagens venenosas.

**Solução:** a API valida estruturalmente, publica no RabbitMQ com publisher confirms — só responde `202` depois do confirm do broker — e não toca o PostgreSQL no caminho da transferência: dual write não é mitigado, é eliminado, porque existe um único write. O `liquidacaoId` (`Guid` v7) nasce na API antes da publicação e viaja como correlation id, chave de idempotência e identificador devolvido ao cliente. O Worker consome e liquida; idempotência é garantida por constraint única de banco (`liquidacao_id, conta_id`) — uma reentrega vira `ack`, nunca erro. Falhas repetidas (fila quorum, `x-delivery-limit=3`) desviam para uma DLQ, drenada por um consumidor dedicado que loga `liquidacaoId`, tentativas, motivo e corpo bruto — a única forma de observar uma transferência morta, já que não há endpoint de status.

### Empacotamento em Kubernetes

**Problema:** o sistema tem processos de naturezas distintas — serviços sem estado (API, Worker) e serviços com estado (broker, banco) — e orquestrá-los corretamente, sabendo justificar cada escolha, é fundamento cobrado de um sênior.

**Solução:** API e Worker como `Deployment`; PostgreSQL e RabbitMQ como `StatefulSet` com `PersistentVolumeClaim` (dados sobrevivem à recriação do pod); configuração não sensível em `ConfigMap`, credenciais em `Secret`; comunicação interna por `Service`/DNS do cluster; entrada externa via `Ingress`. Os manifestos são desenhados para portabilidade entre kind (desenvolvimento local) e GKE, sem nenhum campo que precise mudar entre os dois.

### Observabilidade

**Problema:** um fluxo assíncrono e distribuído é difícil de depurar — uma transferência atravessa processos e um broker, e sem correlação não dá para saber onde falhou ou o que demorou.

**Solução:** instrumentação OpenTelemetry em API e Worker, com o contexto de trace propagado através da própria mensagem no broker — uma transferência produz um único trace, com o span do Worker encadeado como filho do span da API, cobrindo recebimento, publicação, consumo e escrita no banco. Métricas RED (taxa, erro, duração) das operações principais, mais profundidade da fila. Backend gratuito in-cluster (Grafana + Prometheus + Tempo, empacotados numa única imagem), com exportação adicional — não substitutiva — para um backend OTLP externo, ativável sem reinstrumentar o código.

## Configuração

A conexão com o PostgreSQL é lida de `ConnectionStrings:CoreBancario` (`appsettings.Development.json`, carregado por `dotnet run`), com o valor de desenvolvimento apontando para `localhost:5432`. Para sobrescrever — por exemplo em outro ambiente — defina a variável de ambiente `ConnectionStrings__CoreBancario`. O `appsettings.json` base, que vai para a imagem de container publicada, não traz connection string nenhuma — em produção (e no cluster) o valor vem sempre de variável de ambiente.

A conexão com o RabbitMQ é lida de `ConnectionStrings:RabbitMQ`, com o valor de desenvolvimento apontando para `localhost:5672` (usuário e senha `corebancario`, os mesmos do `docker-compose.yml` — credenciais de desenvolvimento, não reais). Para sobrescrever, defina a variável de ambiente `ConnectionStrings__RabbitMQ`. O broker sobe junto do `docker compose up -d` (painel de administração em `http://localhost:15672`).

## Como rodar localmente

1. **Subir o PostgreSQL 18 e o RabbitMQ** (versões mínimas, já fixadas no `docker-compose.yml`):

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

4. **Semear a massa de dados** (600.000 liquidações → 1.200.000 lançamentos, distribuição enviesada). Repetível: cada execução esvazia e recarrega a tabela. Leva cerca de 25 a 40 segundos:

   ```
   dotnet run --project CoreBancario.Worker -- --seed
   ```

5. **Consultar o extrato**:

   ```
   curl "http://localhost:5046/contas/{contaId}/extrato?de=2024-01-01T00:00:00Z&ate=2026-12-31T00:00:00Z"
   ```

## Transferência assíncrona

Solicitar uma transferência: a API valida estruturalmente, publica no RabbitMQ com publisher confirms e responde antes de qualquer lançamento existir no ledger.

```
curl -i -X POST http://localhost:5046/transferencias \
  -H "content-type: application/json" \
  -d '{"contaOrigem":"<guid>","contaDestino":"<guid>","valor":100.00}'
```

- `202 Accepted` com `{"liquidacaoId": "..."}` — aceita; o Worker liquida de forma assíncrona.
- `400` — falha de validação estrutural (valor não positivo, identificador malformado, origem igual ao destino).
- `503` — falha ou expiração da confirmação do broker; nada foi publicado.

Não há endpoint de status: "morta na fila de descartes" não é observável a partir do banco (só "liquidada" seria, via `EXISTS` no ledger), e um endpoint que não distingue "ainda processando" de "falhou definitivamente" responderia a pergunta errada com confiança. A visibilidade do fluxo é o log estruturado dos dois processos e o consumidor de descartes. Para acompanhar uma transferência, use o `liquidacaoId` da resposta como filtro sobre os logs — formatados em JSON (`Scopes[].LiquidacaoId`) — da API e do Worker; ele localiza recebimento, publicação, consumo e liquidação sem nenhum outro insumo. Uma transferência que esgota as tentativas (`x-delivery-limit = 3`) aparece registrada pelo `ConsumidorDeDescartes`, com `liquidacaoId`, tentativas, motivo e corpo bruto — o único lugar em que uma transferência morta é observável.

O painel do RabbitMQ (`http://localhost:15672`, credenciais `corebancario`/`corebancario`) mostra a topologia declarada: exchange `corebancario.transferencias`, fila `transferencias` (quorum, `x-delivery-limit = 3`), dead-letter exchange `corebancario.transferencias.dlx` e fila de descartes `transferencias.dlq`.

## Subida em cluster (Kubernetes)

O `docker compose up -d` acima continua sendo o caminho de desenvolvimento sem cluster — não foi substituído. Este é o segundo caminho, exercitando os mesmos quatro componentes como workloads Kubernetes.

### Pré-requisitos

- `docker`, `kubectl` e [`kind`](https://kind.sigs.k8s.io/) instalados.
- Máquina `x86_64` — as imagens publicadas não são multi-arquitetura.

### Cluster local (kind)

1. Criar o cluster (a imagem do node fica fixada em `kind-config.yaml` — `kindest/node:v1.36`, o padrão do kind, recusa iniciar sob cgroup v1, o que Docker Desktop no WSL2 ainda usa; `v1.33.2` é a última linha testada compatível):

   ```
   kind create cluster --config kind-config.yaml --name corebancario
   ```

2. Instalar o `ingress-nginx` na variante para kind e aguardar o controlador ficar pronto **antes** de aplicar qualquer manifesto da aplicação:

   ```
   kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/main/deploy/static/provider/kind/deploy.yaml
   kubectl wait --namespace ingress-nginx --for=condition=ready pod --selector=app.kubernetes.io/component=controller --timeout=180s
   ```

3. Aplicar os manifestos — sobe `Namespace`, `ConfigMap`, `Secret`, os dois `StatefulSet` (PostgreSQL e RabbitMQ), os dois `Deployment` (API e Worker), o `Ingress` e o `Job` de semeadura, tudo com um comando, a partir de um cluster vazio:

   ```
   kubectl apply -f k8s/
   ```

4. Acompanhar até tudo ficar pronto (o `Job` de seed demora alguns minutos — gera 1.200.000 lançamentos):

   ```
   kubectl get pods -n corebancario -w
   ```

5. Usar a API pelo mesmo `Ingress` que atende `http://localhost` (portas 80/443 mapeadas do node de controle para o host via `kind-config.yaml`):

   ```
   curl http://localhost/sistema/saude
   curl -X POST http://localhost/transferencias -H "Content-Type: application/json" -d '{"ContaOrigem":"<guid>","ContaDestino":"<guid>","Valor":100.00}'
   curl "http://localhost/contas/<guid>/extrato?de=2020-01-01T00:00:00Z&ate=2030-01-01T00:00:00Z"
   ```

Derrubar o cluster local (os volumes vivem dentro do container do node — `kind delete cluster` os leva junto; é esperado, não é o que os critérios de persistência medem):

```
kind delete cluster --name corebancario
```

### Observabilidade

Manifesto separado em `k8s/opcional/`, fora do `kubectl apply -f k8s/` do passo 3 — o backend (imagem `grafana/otel-lgtm`) sozinho pede 640Mi de memória, caro demais para reservar por padrão num cluster kind rodando num host com pouca folga (ex.: WSL2 com poucos GiB). API e Worker exportam traces e métricas por OTLP independentemente de o workload estar de pé; se estiver ausente ou indisponível, os dois continuam operando normalmente (a exportação apenas falha em silêncio) — por isso subir observabilidade é opt-in, não pré-requisito:

```
kubectl apply -f k8s/opcional/
```

Alcançar o painel (a imagem baixa e instala plugins na primeira subida — a primeira vez pode levar alguns minutos; as seguintes são rápidas):

```
kubectl port-forward -n corebancario deploy/otel-lgtm 3000:3000
```

Abrir `http://localhost:3000` (autenticação anônima habilitada, sem login). O painel "CoreBancario" já vem provisionado, com as métricas RED da API e do processamento assíncrono e a profundidade da fila de transferências — nenhum indicador declara limiar numérico de latência ou erro, porque não há linha de base de produção que sustente um.

Ativar a exportação adicional para um backend externo compatível com OTLP (ex.: Dynatrace) — sem reinstrumentar nem reiniciar API e Worker:

```
kubectl edit secret corebancario-observabilidade-externa -n corebancario
```

Preencher `OTEL_EXPORTER_OTLP_ENDPOINT` (URL do backend) e `OTEL_EXPORTER_OTLP_HEADERS` (ex.: `Authorization=Api-Token <token>`) com o valor real — nunca versionado — e reiniciar só o coletor:

```
kubectl rollout restart deployment/otel-lgtm -n corebancario
```

O backend do cluster continua recebendo os mesmos sinais; a exportação externa é cumulativa, não substitutiva. Esvaziar os dois campos do `Secret` e reiniciar de novo desativa, sem erro nem sinal perdido.

### Reconstruir e publicar as imagens

Necessário só após alterar código — as imagens públicas já publicadas (`dockerlucasoliveira/corebancario-api:latest`, `dockerlucasoliveira/corebancario-worker:latest`) bastam para só subir o cluster. O contexto de build é a **raiz do repositório** (os `Dockerfile` ficam em `CoreBancario.Api/` e `CoreBancario.Worker/`, mas os `ProjectReference` e os `Directory.*.props` exigem o contexto na raiz):

```
docker build -f CoreBancario.Api/Dockerfile -t dockerlucasoliveira/corebancario-api:latest .
docker build -f CoreBancario.Worker/Dockerfile -t dockerlucasoliveira/corebancario-worker:latest .
docker push dockerlucasoliveira/corebancario-api:latest
docker push dockerlucasoliveira/corebancario-worker:latest
```

Como o `Deployment` usa `imagePullPolicy: Always` sobre a tag `latest`, um `kubectl rollout restart deployment/api deployment/worker -n corebancario` já busca a imagem nova.

## Testes

- `dotnet test --project CoreBancario.Testes.Unidade` — testes unitários de domínio, sem I/O.
- `dotnet test --project CoreBancario.Testes.Integracao` — testes de integração narrow contra PostgreSQL e RabbitMQ reais via Testcontainers (não precisa de nada rodando antes), mais os testes de plano de execução e custo de acesso (`EXPLAIN ANALYZE`), que exigem o banco de desenvolvimento **semeado** dos passos acima — se não estiver disponível, esses testes pulam com um motivo explícito em vez de falhar.