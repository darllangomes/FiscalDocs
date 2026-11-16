# FiscalDocs

API em .NET 8 que recebe o XML de documentos fiscais (**NFe** e **CTe**), guarda no PostgreSQL e publica um evento por documento no RabbitMQ. Um Worker consome o evento de forma idempotente e resiliente (retry com TTL e DLQ) e mantém um agregado diário por emitente.

- [O que o sistema faz](#o-que-o-sistema-faz)
- [Arquitetura](#arquitetura)
- [Como rodar](#como-rodar)
- [Usando a API](#usando-a-api)
- [Mensageria](#mensageria)
- [Segurança e dados sensíveis](#segurança-e-dados-sensíveis)
- [Testes e CI](#testes-e-ci)
- [Decisões e justificativas](#decisões-e-justificativas)
- [Limitações conhecidas e próximos passos](#limitações-conhecidas-e-próximos-passos)
- [Estrutura do repositório](#estrutura-do-repositório)

Os comandos deste documento usam a sintaxe de um shell POSIX (Git Bash, WSL, macOS ou Linux).

## O que o sistema faz

- **Ingestão.** `POST /api/v1/documents` com o XML no corpo. O parser extrai a chave de acesso, o emitente, o destinatário, os valores e as datas. XML inválido, hostil (XXE) ou de outro tipo é rejeitado com `422`.
- **Idempotência.** A chave de acesso é única. Reenviar o mesmo XML devolve `200` com o mesmo `id`, sem duplicar, mesmo com requisições simultâneas. A mesma chave com conteúdo diferente devolve `409`.
- **Um evento por documento.** O evento é gravado na mesma transação do documento (outbox) e publicado pelo Worker. O consumidor atualiza o agregado diário por emitente e UF sem contar a mesma mensagem duas vezes.
- **Consulta e gestão.** Listagem paginada com filtros, detalhe com ETag, `PATCH` de status e observações com controle de concorrência e `DELETE` lógico.
- **CNPJ alfanumérico** desde o início (Nota Técnica Conjunta 2025.001), ao lado dos CNPJs numéricos.

## Arquitetura

```text
cliente ──POST /api/v1/documents──▶ Api
                                     │  uma transação
                                     ▼
                                 PostgreSQL ── fiscal_documents    (o documento)
                                     │      └─ outbox_messages     (o evento, pendente)
                                     │
                  Worker · OutboxRelay lê as pendentes e publica com confirmação
                                     │
                                     ▼
                                 RabbitMQ ── fiscaldocs.document-ingested
                                     │          ▲  o TTL expira
                                     │          └─ retry.1 · retry.2 · retry.3
                                     │       esgotou ou malformada ──▶ DLQ
                                     ▼
                  Worker · consumidor, numa transação:
                    processed_messages (descarta repetidas) + daily_issuer_aggregates
```

Clean Architecture, com a regra de dependência travada por testes (`tests/FiscalDocs.ArchitectureTests`):

```text
Domain  ←  Application  ←  Infrastructure  ←  Api
                                           ←  Worker
```

| Projeto | Responsabilidade |
|---|---|
| `FiscalDocs.Domain` | `FiscalDocument`, objetos de valor (`Cnpj`, `AccessKey`) e enums. Sem dependências. |
| `FiscalDocs.Application` | Casos de uso, portas (interfaces) e o evento `DocumentIngestedEvent`. |
| `FiscalDocs.Infrastructure` | EF Core/Npgsql, parsers de XML, RabbitMQ e outbox. |
| `FiscalDocs.Api` | Controllers, autenticação, rate limiting, health checks e tratamento de erros. A API **não conhece o RabbitMQ**. |
| `FiscalDocs.Worker` | Consumidor do RabbitMQ e relay da outbox. |

## Como rodar

### Tudo em containers

Requisito: Docker com Compose v2.

```bash
cp .env.example .env                    # troque as senhas e a API_KEY
docker compose up --build -d
curl -i http://localhost:8080/health/ready    # 200 "Healthy" quando o banco responde
```

| Variável do `.env` | Para quê |
|---|---|
| `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` | Banco de dados. |
| `RABBITMQ_DEFAULT_USER`, `RABBITMQ_DEFAULT_PASS` | Usuário do RabbitMQ (e do painel de administração). |
| `API_KEY` | Valor que os clientes enviam no header `X-Api-Key`. |
| `API_PORT` | Porta da API no host (padrão `8080`). |

| Serviço | Porta no host | Observações |
|---|---|---|
| `api` | `8080` (`API_PORT`) | Aplica as migrations ao subir. O Swagger fica desligado (ambiente Production). |
| `worker` | — | Espera a API ficar saudável e o RabbitMQ responder. |
| `postgres` | `5432` | Dados no volume `postgres_data`. |
| `rabbitmq` | `5672` e `15672` | O painel está em `http://localhost:15672`. |

Para parar: `docker compose down` mantém os dados; `docker compose down -v` apaga os volumes.

### Verificação ponta a ponta (smoke test)

```bash
bash scripts/smoke-compose.sh           # --keep deixa a stack no ar no final
```

O script lê `API_KEY` e `API_PORT` do `.env`, sobe uma stack própria (projeto `fiscaldocs-smoke`, com volumes próprios) e confere o caminho inteiro: `POST` devolve `201`, o `GET` devolve `200`, o agregado chega a `1|1500.00`, o reenvio devolve `200` sem somar de novo e, com o RabbitMQ parado, o `POST` continua devolvendo `201` e o evento pendente é entregue quando o broker volta.

Os containers têm nomes fixos (`fiscaldocs-*`), então o script se recusa a rodar se já existir outra stack do projeto no ar. Nesse caso, rode `docker compose down` antes.

### Desenvolvimento local

Requisitos: SDK do .NET 8.0.428 ou posterior (`global.json`) e Docker para o banco e o broker.

```bash
docker compose up -d postgres rabbitmq

# API
dotnet user-secrets set "ConnectionStrings:Default" "Host=127.0.0.1;Port=5432;Database=fiscaldocs;Username=fiscaldocs;Password=<POSTGRES_PASSWORD>" --project src/FiscalDocs.Api
dotnet user-secrets set "Auth:ApiKey" "<uma chave de desenvolvimento>" --project src/FiscalDocs.Api
dotnet user-secrets set "Database:MigrateOnStartup" "true" --project src/FiscalDocs.Api   # só na primeira vez
dotnet run --project src/FiscalDocs.Api --launch-profile http                              # Swagger em http://localhost:5260/swagger

# Worker (em outro terminal)
dotnet user-secrets set "ConnectionStrings:Default" "Host=127.0.0.1;Port=5432;Database=fiscaldocs;Username=fiscaldocs;Password=<POSTGRES_PASSWORD>" --project src/FiscalDocs.Worker
dotnet user-secrets set "RabbitMq:Uri" "amqp://<RABBITMQ_DEFAULT_USER>:<RABBITMQ_DEFAULT_PASS>@127.0.0.1:5672/" --project src/FiscalDocs.Worker
dotnet run --project src/FiscalDocs.Worker
```

No Windows com o Docker Desktop, use `127.0.0.1` em vez de `localhost` nas conexões. O `localhost` pode tentar o IPv6 primeiro e atrasar cada conexão nova, e aí o `/health/ready`, que abre uma conexão nova a cada chamada, passa a responder `503`.

Para criar uma migration:

```bash
dotnet tool restore
FISCALDOCS_CONNECTION="Host=127.0.0.1;Port=5432;Database=fiscaldocs;Username=fiscaldocs;Password=<POSTGRES_PASSWORD>" \
  dotnet ef migrations add NomeDaMigration \
  --project src/FiscalDocs.Infrastructure --startup-project src/FiscalDocs.Api
```

### Configuração

Cada chave também pode vir de uma variável de ambiente, com `__` no lugar de `:` (por exemplo, `Auth__ApiKey`).

| Chave | Usada por | Padrão | Descrição |
|---|---|---|---|
| `ConnectionStrings:Default` | Api e Worker | obrigatória | Conexão com o PostgreSQL. |
| `Auth:ApiKey` | Api | obrigatória | Chave esperada no header `X-Api-Key`. A API não sobe sem ela. |
| `RateLimiting:PermitLimit` e `RateLimiting:WindowSeconds` | Api | `60` e `60` | Requisições permitidas por IP em cada janela. |
| `Database:MigrateOnStartup` | Api | `false` (o compose liga) | Aplica as migrations ao subir. Use uma réplica só ([ADR 0008](docs/adr/0008-api-independente-do-broker.md)). |
| `RabbitMq:Uri` | Worker | obrigatória | `amqp://usuário:senha@host:5672/`. |
| `Outbox:PollIntervalSeconds` | Worker | `1` | Intervalo entre as varreduras da outbox. |

## Usando a API

Todas as rotas, exceto `/health/*`, exigem o header `X-Api-Key`. Os erros seguem o formato `application/problem+json` (`ProblemDetails`), sem stack trace.

| Rota | O que faz | Sucesso | Erros |
|---|---|---|---|
| `POST /api/v1/documents` | Ingere um XML (`Content-Type: application/xml` ou `text/xml`, até 2 MiB). | `201` (novo) ou `200` (reenvio idêntico) | `409` (conteúdo diferente ou documento apagado), `413`, `415`, `422` |
| `GET /api/v1/documents` | Lista paginada com filtros. | `200` | `400` |
| `GET /api/v1/documents/{id}` | Detalhe do documento, com `ETag`. | `200` | `404` |
| `PATCH /api/v1/documents/{id}` | Altera `status` e/ou `notes`. Exige `If-Match`. | `200`, com o novo `ETag` | `400`, `404`, `412`, `415`, `428` |
| `DELETE /api/v1/documents/{id}` | Apaga logicamente. O `If-Match` é opcional. | `204` | `404`, `412` |
| `GET /health/live` | O processo está de pé. | `200` | |
| `GET /health/ready` | O banco responde (prazo de 3 s). | `200` | `503` |

Filtros da listagem: `type` (`NFe` ou `CTe`), `status` (`Received`, `Reviewed` ou `Archived`), `issuerCnpj` (com ou sem máscara, em qualquer caixa), `issuerState` (duas letras), `issuedFrom` e `issuedTo` (datas `AAAA-MM-DD`, inclusivas, em UTC), `page` (a partir de 1) e `pageSize` (de 1 a 100, padrão 20).

Os exemplos usam os XMLs fictícios de `samples/`. Os valores de `id` e `ETag` mudam a cada banco.

```bash
API=http://localhost:8080
KEY=<o valor de API_KEY no .env>
```

**Enviar um documento.** Use `--data-binary`: o `-d @arquivo` remove as quebras de linha, o que muda o hash do XML e faz um reenvio ser tratado como conteúdo diferente.

```bash
curl -i -X POST "$API/api/v1/documents" \
  -H "X-Api-Key: $KEY" -H "Content-Type: application/xml" \
  --data-binary @samples/nfe-cnpj-numerico.xml
```

```http
HTTP/1.1 201 Created
Location: /api/v1/documents/87f95b11-0faa-420d-ae08-cb072e849dc4

{"id":"87f95b11-0faa-420d-ae08-cb072e849dc4"}
```

Repetir o mesmo comando devolve `200 OK` com o mesmo `id`. Enviar a mesma chave com qualquer byte diferente devolve `409`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Document conflict.",
  "status": 409,
  "detail": "A document with access key already exists with different content.",
  "traceId": "00-5d8b29c7ffef79821c85972afd8064b3-6d49630eec5a37f0-00"
}
```

**Listar com filtros.**

```bash
curl -H "X-Api-Key: $KEY" \
  "$API/api/v1/documents?type=NFe&issuerState=PE&issuedFrom=2026-08-01&issuedTo=2026-08-31&pageSize=10"
```

```json
{
  "items": [
    {
      "id": "4c0a36bb-ce3d-4fac-adcc-674239f5b775",
      "type": "NFe",
      "status": "Received",
      "accessKey": "26260812ABC34501DE35550010000020021234567895",
      "number": "2002",
      "series": "1",
      "issueDate": "2026-08-20T17:15:00+00:00",
      "issuerCnpj": "12ABC34501DE35",
      "issuerName": "EMPRESA FICTICIA ALFANUMERICA LTDA",
      "issuerState": "PE",
      "totalAmount": 8750.5,
      "updatedAt": "2026-11-16T03:08:45.600218+00:00"
    }
  ],
  "page": 1,
  "pageSize": 10,
  "totalCount": 2
}
```

(O exemplo mostra só o primeiro dos dois itens devolvidos.) Os itens vêm por data de emissão decrescente. A listagem não traz o destinatário nem o XML.

**Ler o detalhe e editar.** O `GET` devolve o `ETag` (`"748"`). O `PATCH` precisa devolvê-lo em `If-Match`.

```bash
ID=87f95b11-0faa-420d-ae08-cb072e849dc4

curl -i -H "X-Api-Key: $KEY" "$API/api/v1/documents/$ID"
# HTTP/1.1 200 OK
# ETag: "748"
# {"id":"87f9…","type":"NFe","status":"Received","accessKey":"2626…","number":"1001","series":"1",
#  "issueDate":"2026-08-14T13:30:00+00:00","issuerCnpj":"11222333000181","issuerName":"…","issuerState":"PE",
#  "recipientName":"…","recipientState":"SP","totalAmount":1500.0,"notes":null,"createdAt":"…","updatedAt":"…"}

curl -i -X PATCH "$API/api/v1/documents/$ID" \
  -H "X-Api-Key: $KEY" -H "Content-Type: application/json" -H 'If-Match: "748"' \
  -d '{"status":"Reviewed","notes":"conferido"}'
# HTTP/1.1 200 OK
# ETag: "754"        <- use este valor no próximo PATCH
```

| Situação do `PATCH` | Resposta |
|---|---|
| Documento inexistente | `404` |
| Sem `If-Match` | `428` |
| `If-Match` desatualizado ou malformado | `412` |
| Campo desconhecido, status inválido ou corpo vazio | `400` |

**Apagar.**

```bash
curl -i -X DELETE "$API/api/v1/documents/$ID" -H "X-Api-Key: $KEY" -H 'If-Match: "754"'
# HTTP/1.1 204 No Content
```

Depois disso, `GET`, `PATCH` e `DELETE` desse `id` devolvem `404`, e reenviar o mesmo XML devolve `409`.

**Swagger.** A interface fica em `/swagger` e só existe no ambiente Development (veja [Desenvolvimento local](#desenvolvimento-local)). Use o botão *Authorize* com a chave de API.

## Mensageria

| Elemento | Nome |
|---|---|
| Exchange (direct) | `fiscaldocs.events`, chave de roteamento `document.ingested` |
| Fila principal | `fiscaldocs.document-ingested` |
| Filas de retry | `fiscaldocs.document-ingested.retry.1`, `.2` e `.3` (TTL de 5 s, 30 s e 2 min) |
| Dead-letter | exchange `fiscaldocs.dlx` e fila `fiscaldocs.document-ingested.dlq` |

O evento `DocumentIngested` leva `messageId`, `documentId`, `type`, `issuerCnpj`, `issuerState`, `issueDate`, `totalAmount` e `occurredAt`. Não leva XML, CPF nem dados do destinatário.

- A entrega é *at-least-once*. O consumidor registra cada `messageId` e descarta as repetidas, então reentregas não inflam o agregado.
- Se o processamento falha, a mensagem passa pelas filas de retry (com o TTL como backoff) e, depois de três retries, vai para a DLQ para análise manual. Uma mensagem que não deserializa vai direto para a DLQ.
- Se o RabbitMQ cai, a API continua aceitando documentos e os eventos esperam na outbox. Quando o broker volta, o Worker os entrega.

Para acompanhar:

```bash
# agregado diário por emitente (contagem e valor total); use o usuário e o banco do seu .env
docker compose exec postgres psql -U fiscaldocs -d fiscaldocs -c "SELECT * FROM daily_issuer_aggregates"
```

O painel do RabbitMQ (`http://localhost:15672`) mostra as filas, as mensagens em retry e a DLQ. Detalhes em [ADR 0006](docs/adr/0006-outbox-transacional-e-consumidor-idempotente.md) e [ADR 0007](docs/adr/0007-retry-com-ttl-dlq-e-recuperacao-do-rabbitmq.md).

## Segurança e dados sensíveis

- **Autenticação** por chave de API (`X-Api-Key`), comparada em tempo constante. Todas as rotas exigem a chave, exceto as sondas de saúde.
- **Rate limiting** por IP (60 requisições por 60 s por padrão): `429` com `Retry-After`.
- **XML hostil:** DTD proibido e sem resolução de entidades externas (XXE), limite de 5 milhões de caracteres no parser e de 2 MiB no corpo.
- **Logs e erros:** nunca registram o XML, o corpo da requisição nem o CPF, e as respostas de erro não trazem stack trace. Testes de integração provam isso.
- **CPF do destinatário:** nenhum endpoint o devolve, ele não entra no evento e não vai para os logs. Mas **fica guardado em texto puro** no banco (coluna `recipient_document` e dentro do XML original). Cifrá-lo é uma evolução prevista.
- **Segredos:** o `.env` fica fora do Git, o desenvolvimento usa user-secrets e o CI roda o `gitleaks`.

Detalhes e limitações (chave única, proxy reverso, TLS) em [ADR 0011](docs/adr/0011-seguranca-e-dados-sensiveis.md).

## Testes e CI

```bash
dotnet test                                      # tudo; os testes de integração precisam do Docker
dotnet test tests/FiscalDocs.UnitTests           # sem Docker
dotnet test tests/FiscalDocs.ArchitectureTests   # sem Docker
```

| Projeto | Testes | O que cobre |
|---|---|---|
| `FiscalDocs.UnitTests` | 154 | Domínio (CNPJ e chave de acesso, numéricos e alfanuméricos), parsers de NFe e CTe, handler de ingestão e leitura segura de XML. |
| `FiscalDocs.IntegrationTests` | 242 | A API de ponta a ponta e a mensageria, com PostgreSQL e RabbitMQ reais em containers (Testcontainers): ingestão e concorrência, consulta, `PATCH` e `DELETE`, outbox, consumidor, retry e DLQ, health checks, rate limiting, Swagger e privacidade dos logs. |
| `FiscalDocs.ArchitectureTests` | 14 | Regra de dependência entre as camadas (NetArchTest). |

Os testes de integração usam containers reais porque o provider em memória do EF não reproduz índice único, concorrência nem `xmin` ([ADR 0001](docs/adr/0001-postgresql-como-banco.md)). Na primeira execução o Docker baixa as imagens `postgres:16` e `rabbitmq:3-management`.

O CI (`.github/workflows/ci.yml`) roda em todo pull request e em todo push na `main`:

- **Build and test:** verificação de formatação (`dotnet format`), build em Release e `dotnet test` com cobertura (publicada como artefato).
- **Dependency and code policy checks:** falha se houver pacote NuGet vulnerável e se aparecer, em `src`, tratamento numérico de CNPJ ou chave de acesso (`\d{14}`, `\d{44}`, `long.Parse`, `ulong.Parse`).
- **Secret scan:** `gitleaks` em todo o histórico.

O Dependabot abre pull requests semanais para os pacotes NuGet, as actions do CI e as imagens do Compose.

## Decisões e justificativas

Cada decisão tem um registro com contexto, alternativas descartadas e o custo aceito, em [`docs/adr/`](docs/adr/README.md).

| Tema | Decisão | ADR |
|---|---|---|
| Banco | PostgreSQL | [0001](docs/adr/0001-postgresql-como-banco.md) |
| Estrutura | Clean Architecture, com Api e Worker separados e a regra travada por testes | [0002](docs/adr/0002-clean-architecture-com-api-e-worker.md) |
| Escopo | NFe e CTe implementados, NFSe como extensão | [0003](docs/adr/0003-nfe-e-cte-com-nfse-como-extensao.md) |
| Domínio | CNPJ e chave de acesso alfanuméricos desde o início | [0004](docs/adr/0004-cnpj-e-chave-de-acesso-alfanumericos.md) |
| Ingestão | Índice único decide; reenvio idêntico dá `200`, conteúdo diferente dá `409` | [0005](docs/adr/0005-idempotencia-da-ingestao.md) |
| Eventos | Outbox transacional e consumidor idempotente | [0006](docs/adr/0006-outbox-transacional-e-consumidor-idempotente.md) |
| Resiliência | Retry com TTL, DLQ e recuperação de conexão | [0007](docs/adr/0007-retry-com-ttl-dlq-e-recuperacao-do-rabbitmq.md) |
| Operação | API sem dependência do broker, readiness só do banco, migração na subida | [0008](docs/adr/0008-api-independente-do-broker.md) |
| Edição | ETag a partir do `xmin`, `PATCH` só de campos de gestão, soft delete | [0009](docs/adr/0009-concorrencia-otimista-patch-e-soft-delete.md) |
| Listagem | Paginação por offset | [0010](docs/adr/0010-paginacao-por-offset.md) |
| Segurança | Chave de API, rate limit por IP, XXE e LGPD | [0011](docs/adr/0011-seguranca-e-dados-sensiveis.md) |

## Limitações conhecidas e próximos passos

Limitações, com o registro onde cada uma é explicada:

- Emitente pessoa física (CPF) é rejeitado com `422`; CNPJ com todos os dígitos iguais não é rejeitado ([ADR 0004](docs/adr/0004-cnpj-e-chave-de-acesso-alfanumericos.md)).
- O XML original é guardado, mas ainda não há endpoint para baixá-lo.
- O POST aceita só o XML no corpo (`application/xml` ou `text/xml`), sem `multipart/form-data`.
- Não há endpoint para restaurar um documento apagado, e reenviá-lo dá `409` ([ADR 0009](docs/adr/0009-concorrencia-otimista-patch-e-soft-delete.md)).
- `If-Match` aceita um único ETag forte; lista, `*` e ETag fraco dão `412` ([ADR 0009](docs/adr/0009-concorrencia-otimista-patch-e-soft-delete.md)).
- Paginação por offset, com `COUNT(*)` a cada requisição ([ADR 0010](docs/adr/0010-paginacao-por-offset.md)).
- Chave de API única, sem rotação nem escopos. Atrás de um proxy reverso é preciso configurar `ForwardedHeaders`, e o TLS deve terminar no proxy ([ADR 0011](docs/adr/0011-seguranca-e-dados-sensiveis.md)).
- O CPF do destinatário fica em texto puro no banco ([ADR 0011](docs/adr/0011-seguranca-e-dados-sensiveis.md)).
- A migração na subida não tem trava: use uma réplica da API na primeira subida ([ADR 0008](docs/adr/0008-api-independente-do-broker.md)).
- As linhas processadas da outbox não são removidas, e não há métrica de atraso da outbox nem sonda de saúde do Worker ([ADR 0006](docs/adr/0006-outbox-transacional-e-consumidor-idempotente.md)).
- A DLQ não tem ferramenta de reprocessamento. Se a recuperação automática da conexão com o RabbitMQ desistir, só reiniciar o Worker resolve ([ADR 0007](docs/adr/0007-retry-com-ttl-dlq-e-recuperacao-do-rabbitmq.md)).
- Os testes de carga ([`loadtests/`](loadtests/README.md)) mostram que, num notebook de desenvolvimento, a ingestão de NFes de 23 KB sustenta 300 documentos por segundo (p95 de 55 ms) e satura entre 300 e 400. O gargalo não foi investigado.

Próximos passos: suporte a NFSe ([ADR 0003](docs/adr/0003-nfe-e-cte-com-nfse-como-extensao.md)), repetir a carga de consulta com um volume maior para reavaliar o keyset, cifrar o CPF, limpar a outbox e expor a métrica de atraso, e uma sonda de saúde para o Worker.

## Estrutura do repositório

```text
src/
  FiscalDocs.Domain           entidade, objetos de valor e enums
  FiscalDocs.Application      casos de uso, portas e o evento
  FiscalDocs.Infrastructure   EF Core, parsers de XML, RabbitMQ e outbox
  FiscalDocs.Api              controllers, autenticação, rate limiting e health checks
  FiscalDocs.Worker           consumidor e relay da outbox
tests/
  FiscalDocs.UnitTests
  FiscalDocs.IntegrationTests
  FiscalDocs.ArchitectureTests
samples/                      XMLs fictícios (NFe e CTe, numéricos e alfanuméricos, inválido e XXE)
scripts/smoke-compose.sh      verificação ponta a ponta no Docker Compose
docs/adr/                     registros de decisão de arquitetura
docker-compose.yml            PostgreSQL, RabbitMQ, Api e Worker
.env.example                  variáveis do Compose
```
