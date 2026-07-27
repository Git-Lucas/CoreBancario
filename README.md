# CoreBancario

## Configuração

A conexão com o PostgreSQL é lida de `ConnectionStrings:CoreBancario` (`appsettings.Development.json`, carregado por `dotnet run`), com o valor de desenvolvimento apontando para `localhost:5432`. Para sobrescrever — por exemplo em outro ambiente — defina a variável de ambiente `ConnectionStrings__CoreBancario`. O `appsettings.json` base, que vai para a imagem de container publicada, não traz connection string nenhuma — em produção (e no cluster) o valor vem sempre de variável de ambiente.

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

## Subida em cluster (Kubernetes)

O `docker compose up -d` acima continua sendo o caminho de desenvolvimento sem cluster — não foi substituído. Este é o segundo caminho, exercitando os mesmos quatro componentes como workloads Kubernetes (ver PRD-3 e a change `add-k8s-packaging`).

### Pré-requisitos

- `docker`, `kubectl` e [`kind`](https://kind.sigs.k8s.io/) instalados.
- Máquina `x86_64` (mesma arquitetura dos nodes do GKE — nenhum build multi-arquitetura é necessário).

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

### Reconstruir e publicar as imagens

Necessário só após alterar código — as imagens públicas já publicadas (`dockerlucasoliveira/corebancario-api:latest`, `dockerlucasoliveira/corebancario-worker:latest`) bastam para só subir o cluster. O contexto de build é a **raiz do repositório** (os `Dockerfile` ficam em `CoreBancario.Api/` e `CoreBancario.Worker/`, mas os `ProjectReference` e os `Directory.*.props` exigem o contexto na raiz):

```
docker build -f CoreBancario.Api/Dockerfile -t dockerlucasoliveira/corebancario-api:latest .
docker build -f CoreBancario.Worker/Dockerfile -t dockerlucasoliveira/corebancario-worker:latest .
docker push dockerlucasoliveira/corebancario-api:latest
docker push dockerlucasoliveira/corebancario-worker:latest
```

Como o `Deployment` usa `imagePullPolicy: Always` sobre a tag `latest`, um `kubectl rollout restart deployment/api deployment/worker -n corebancario` já busca a imagem nova.

### GKE (validação pontual)

Sessão sob crédito — o objetivo é confirmar que os **mesmos** manifestos, sem edição, sobem em um cluster gerenciado, não manter o cluster de pé.

1. Dimensionar o pool a partir da soma das reservas (`design.md`, D10: ~700m CPU / 1,5Gi, mais o `ingress-nginx` e os pods de sistema do GKE) e criar o cluster zonal.
2. Instalar o `ingress-nginx` (variante padrão, não a de kind) e aguardar o balanceador receber endereço externo.
3. `kubectl apply -f k8s/` — os mesmos manifestos do kind, sem nenhum campo alterado.
4. Repetir o fluxo ponta a ponta pelo IP externo do balanceador.
5. **Derrubar tudo ao final da sessão** — o custo não controlado é o risco real de um cluster gerenciado esquecido de pé:

   ```
   kubectl delete -f k8s/
   kubectl delete -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/main/deploy/static/provider/cloud/deploy.yaml
   gcloud container clusters delete <nome-do-cluster> --zone <zona>
   ```

   Conferir depois que não sobrou balanceador (`gcloud compute forwarding-rules list`) nem disco persistente órfão (`gcloud compute disks list`) — um `Service` `LoadBalancer` ou um `PersistentVolumeClaim` esquecido não são derrubados pela remoção do cluster e continuam sendo cobrados.

## Testes

- `dotnet test --project CoreBancario.Testes.Unidade` — testes unitários de domínio, sem I/O.
- `dotnet test --project CoreBancario.Testes.Integracao` — testes de integração narrow contra PostgreSQL e RabbitMQ reais via Testcontainers (não precisa de nada rodando antes), mais os testes de plano de execução e custo de acesso (`EXPLAIN ANALYZE`), que exigem o banco de desenvolvimento **semeado** dos passos acima — se não estiver disponível, esses testes pulam com um motivo explícito em vez de falhar.