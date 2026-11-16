# Guia de testes manuais

Roteiro para validar a API à mão, sem escrever código. Cada passo diz o que enviar e o que esperar. Os dados são fictícios e estão em [`samples/`](../samples/README.md).

- [Antes de começar](#antes-de-começar)
- [1. Saúde e autenticação](#1-saúde-e-autenticação)
- [2. Ingestão: `POST /api/v1/documents`](#2-ingestão-post-apiv1documents)
- [3. Outros documentos e tipos](#3-outros-documentos-e-tipos)
- [4. Consulta: `GET /api/v1/documents`](#4-consulta-get-apiv1documents)
- [5. Detalhe e ETag: `GET /api/v1/documents/{id}`](#5-detalhe-e-etag-get-apiv1documentsid)
- [6. Edição: `PATCH /api/v1/documents/{id}`](#6-edição-patch-apiv1documentsid)
- [7. Download do XML: `GET /api/v1/documents/{id}/xml`](#7-download-do-xml-get-apiv1documentsidxml)
- [8. Exclusão: `DELETE /api/v1/documents/{id}`](#8-exclusão-delete-apiv1documentsid)
- [9. Mensageria: outbox, RabbitMQ e agregado](#9-mensageria-outbox-rabbitmq-e-agregado)
- [10. Limite de requisições](#10-limite-de-requisições)
- [Checklist](#checklist)

## Antes de começar

**1. Suba a API** (veja o [README](../README.md#como-rodar)). As duas formas mais curtas:

| Forma | Endereço da API | Swagger |
|---|---|---|
| `docker compose up --build -d` | `http://localhost:8080` | desligado (ambiente Production) |
| `dotnet run --project src/FiscalDocs.Api --launch-profile http` | `http://localhost:5260` | `http://localhost:5260/swagger` |

Sem Docker, só com o PostgreSQL instalado, veja [Sem Docker](../README.md#sem-docker): a API funciona inteira, e só o passo 9 (agregado) precisa do Worker e do RabbitMQ.

**2. Tenha a chave de API.** É a `API_KEY` do `.env` (Docker) ou o `Auth:ApiKey` dos user-secrets (`dotnet run`).

**3. Escolha como testar.**

- **No Swagger** (somente com `dotnet run`): abra `/swagger`, clique em **Authorize**, cole a chave e use **Try it out** em cada rota. O corpo do `POST` já vem preenchido com um documento válido, e o `PATCH` tem o campo `If-Match`.
- **No terminal**, com `curl` (Git Bash, WSL, macOS ou Linux). Defina duas variáveis e copie os comandos abaixo:

```bash
API=http://localhost:5260        # ou http://localhost:8080 com o Docker
KEY="<sua chave de API>"
```

- **No editor:** o arquivo [`docs/api.http`](api.http) tem as mesmas chamadas para a extensão REST Client (VS Code) ou para o JetBrains.

Os comandos usam `-s -o /dev/null -w "%{http_code}\n"` quando só o status importa, e `-i` quando os headers importam.
Rode os comandos da raiz do repositório, porque eles leem arquivos de `samples/`.

## 1. Saúde e autenticação

```bash
curl -i $API/health/live          # 200: o processo está de pé (não exige chave)
curl -i $API/health/ready         # 200: o banco responde (não exige chave)
curl -i $API/api/v1/documents     # 401: sem o header X-Api-Key
curl -i -H "X-Api-Key: errada" $API/api/v1/documents    # 401: chave errada
curl -i -H "X-Api-Key: $KEY" $API/api/v1/documents      # 200: lista (vazia se o banco é novo)
```

**Esperado:** as duas sondas de saúde respondem `200` sem chave. As rotas de documentos respondem `401` sem a chave ou com a chave errada, e `200` com a certa.

## 2. Ingestão: `POST /api/v1/documents`

```bash
# 2.1 Documento novo: 201 e o id no corpo
curl -i -X POST $API/api/v1/documents -H "X-Api-Key: $KEY" \
  -H "Content-Type: application/xml" --data-binary @samples/nfe-cnpj-numerico.xml
```

**Esperado:** `201 Created`, o header `Location: /api/v1/documents/<id>` e o corpo `{"id":"<guid>"}`. Guarde o id:

```bash
ID=$(curl -s -X POST $API/api/v1/documents -H "X-Api-Key: $KEY" \
  -H "Content-Type: application/xml" --data-binary @samples/nfe-cnpj-numerico.xml | sed -E 's/.*"id":"([^"]+)".*/\1/')
echo $ID
```

(Esse segundo envio já é um reenvio, então devolve `200` com o mesmo id.)

> Use sempre `--data-binary`. O `-d @arquivo` do curl remove as quebras de linha, e aí o conteúdo muda.

| Caso | Comando | Esperado |
|---|---|---|
| Reenvio idêntico | repita o 2.1 | **`200`**, o mesmo id, nada duplicado |
| Mesma chave, conteúdo diferente | `(cat samples/nfe-cnpj-numerico.xml; echo) \| curl -s -o /dev/null -w "%{http_code}\n" -X POST $API/api/v1/documents -H "X-Api-Key: $KEY" -H "Content-Type: application/xml" --data-binary @-` | **`409`**: a chave de acesso já existe com outro conteúdo |
| XML mal formado | `--data-binary @samples/invalido.xml` | **`422`** |
| XML com DTD (ataque XXE) | `--data-binary @samples/malicioso-xxe.xml` | **`422`**: a entidade externa nunca é resolvida |
| `Content-Type` errado | `-H "Content-Type: application/json"` com qualquer corpo | **`415`** |
| Corpo grande demais | `head -c 3000000 /dev/zero \| tr '\0' 'a' \| curl -s -o /dev/null -w "%{http_code}\n" -X POST $API/api/v1/documents -H "X-Api-Key: $KEY" -H "Content-Type: application/xml" --data-binary @-` | **`413`**: o limite é 2 MiB |
| Corpo vazio | `-H "Content-Type: application/xml" -d ""` | **`422`** |

O corpo dos erros é `application/problem+json`, com `title` e `detail`, sem stack trace.

## 3. Outros documentos e tipos

Envie cada um e confira o `201` (ou `200`, se já existir):

```bash
for f in nfe-cnpj-alfanumerico cte-cnpj-numerico cte-cnpj-alfanumerico nfe-realista-3itens nfe-emitente-cpf; do
  printf "%-28s" "$f"
  curl -s -o /dev/null -w "%{http_code}\n" -X POST $API/api/v1/documents -H "X-Api-Key: $KEY" \
    -H "Content-Type: application/xml" --data-binary @samples/$f.xml
done
```

| Sample | O que prova |
|---|---|
| `samples/nfe-cnpj-alfanumerico.xml` | CNPJ **alfanumérico** (`12ABC34501DE35`), a regra de julho de 2026 |
| `samples/cte-cnpj-numerico.xml` e `samples/cte-cnpj-alfanumerico.xml` | O tipo **CTe** |
| `samples/nfe-realista-3itens.xml` | NFe no leiaute real: itens, impostos, assinatura e protocolo |
| `samples/nfe-emitente-cpf.xml` | Emitente **pessoa física** (produtor rural): `<emit><CPF>`, série 920 |
| `samples/nfe-realista-990itens.xml` | O maior documento (731 KB). Aceito, abaixo do limite de 2 MiB |

## 4. Consulta: `GET /api/v1/documents`

```bash
curl -s -H "X-Api-Key: $KEY" "$API/api/v1/documents?pageSize=5"
```

**Esperado:** `200` com `{"items":[…],"page":1,"pageSize":5,"totalCount":N}`. Os itens vêm da emissão mais recente para a mais antiga e **não** trazem o XML.

| Filtro | Exemplo | Esperado |
|---|---|---|
| `type` | `?type=CTe` | só CTe |
| `status` | `?status=Received` | só os recém-recebidos |
| `issuerDocument` (CNPJ) | `?issuerDocument=12.abc.345/01de-35` | aceita máscara e minúsculas |
| `issuerDocument` (CPF) | `?issuerDocument=52998224725` ou `529.982.247-25` | o emitente pessoa física |
| `issuerState` | `?issuerState=pe` | sem diferenciar caixa |
| `issuedFrom` e `issuedTo` | `?issuedFrom=2026-08-15&issuedTo=2026-08-20` | os dois limites são inclusivos, em UTC. Num banco só com os documentos dos passos 2 e 3, voltam as notas `2002`, `3003`, `42`, `5005` e `7007` |
| combinados | `?type=NFe&issuerState=PE&issuedFrom=2026-08-15` | E entre todos |
| paginação | `?page=2&pageSize=2` | a segunda página; `totalCount` não muda |

Em um emitente pessoa física, o item traz `"issuerDocument":"***.982.247-**"`: o CPF sai **mascarado**. O CNPJ sai por inteiro.

**Entradas inválidas devolvem `400`** (`application/problem+json`, com os campos errados em `errors`):

```bash
for q in "page=0" "pageSize=101" "type=XYZ" "status=Foo" "issuedFrom=nao-e-data" "issuerState=SPX" \
         "issuerDocument=123" "issuerDocument=52998224726" "issuerCnpj=11222333000181" \
         "issuedFrom=2026-08-20&issuedTo=2026-08-10"; do
  printf "%-45s" "$q"; curl -s -o /dev/null -w "%{http_code}\n" -H "X-Api-Key: $KEY" "$API/api/v1/documents?$q"
done
```

**Esperado:** `400` em todas. Repare em duas: `issuerDocument=52998224726` é um CPF com dígito verificador errado, e `issuerCnpj` é o nome **antigo** do filtro, rejeitado de propósito para não devolver a lista inteira a quem ainda o usa.

## 5. Detalhe e ETag: `GET /api/v1/documents/{id}`

```bash
curl -i -H "X-Api-Key: $KEY" $API/api/v1/documents/$ID
```

**Esperado:** `200`, o header `ETag: "<número>"` e o documento em JSON (`accessKey`, `number`, `issuerDocument`, `issuerName`, `totalAmount`, `status: "Received"` e outros). Não há `rawXml` no corpo. O `ETag` é a versão do documento, e você vai usá-lo no passo 6.

| Caso | Esperado |
|---|---|
| id que não existe (`$API/api/v1/documents/00000000-0000-0000-0000-000000000000`) | `404` |
| id que não é um GUID (`$API/api/v1/documents/123`) | `404` |

## 6. Edição: `PATCH /api/v1/documents/{id}`

Só `status` e `notes` podem mudar, e o `If-Match` com o ETag é **obrigatório**.

```bash
# Leia o ETag atual (sem as aspas do shell, mas com as aspas do ETag)
ETAG=$(curl -si -H "X-Api-Key: $KEY" $API/api/v1/documents/$ID | grep -i '^etag:' | cut -d' ' -f2 | tr -d '\r')
echo $ETAG

# 6.1 Edição válida: 200 e um ETag novo
curl -i -X PATCH $API/api/v1/documents/$ID -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
  -H "If-Match: $ETAG" -d '{"status":"Reviewed","notes":"Conferida pelo financeiro"}'
```

**Esperado:** `200`, um `ETag` **diferente** do anterior e o documento com `status: "Reviewed"` e as notas.

| Caso | Como | Esperado |
|---|---|---|
| ETag velho | repita o 6.1 com o `$ETAG` antigo | **`412`**: alguém alterou o documento depois da sua leitura |
| Sem `If-Match` | tire o header `-H "If-Match: …"` | **`428`** |
| Status inválido | `-d '{"status":"Foo"}'` | **`400`**, a mensagem lista os valores aceitos |
| Campo não permitido | `-d '{"status":"Reviewed","issuerName":"X"}'` | **`400`**: só `status` e `notes` |
| Corpo vazio | `-d '{}'` | **`400`**: informe ao menos um campo |
| Notas longas demais | `notes` com mais de 500 caracteres | **`400`** |
| Limpar as notas | `-d '{"notes":null}'` | `200`, `notes: null` |
| `Content-Type` errado | `-H "Content-Type: text/plain"` | **`415`** |
| Id que não existe | outro GUID | **`404`** |

Releia o ETag depois de cada `200`, porque ele muda a cada alteração.

## 7. Download do XML: `GET /api/v1/documents/{id}/xml`

```bash
curl -i -H "X-Api-Key: $KEY" $API/api/v1/documents/$ID/xml
curl -OJ -H "X-Api-Key: $KEY" $API/api/v1/documents/$ID/xml     # salva como <chave>.xml
```

**Esperado:** `200`, `Content-Type: application/xml; charset=utf-8`, `Content-Disposition: attachment; filename=<chave de acesso>.xml` e `Cache-Control: no-store`. O arquivo é igual ao enviado:

```bash
cmp samples/nfe-cnpj-numerico.xml 26260811222333000181550010000010011123456782.xml && echo "iguais"
```

**Atenção, a máscara do CPF não vale aqui.** Para o documento do emitente pessoa física (`samples/nfe-emitente-cpf.xml`), o JSON mostra `***.982.247-**`, mas o XML traz `<CPF>52998224725</CPF>` por inteiro. O XML é a fonte legal e não é alterado ([ADR 0011](adr/0011-seguranca-e-dados-sensiveis.md)). Id inexistente: `404`. Sem a chave: `401`.

## 8. Exclusão: `DELETE /api/v1/documents/{id}`

A exclusão é lógica. O `If-Match` é opcional aqui.

```bash
curl -i -X DELETE $API/api/v1/documents/$ID -H "X-Api-Key: $KEY"        # 204
curl -s -o /dev/null -w "%{http_code}\n" -H "X-Api-Key: $KEY" $API/api/v1/documents/$ID        # 404
curl -s -o /dev/null -w "%{http_code}\n" -H "X-Api-Key: $KEY" $API/api/v1/documents/$ID/xml    # 404
curl -s -o /dev/null -w "%{http_code}\n" -X POST $API/api/v1/documents -H "X-Api-Key: $KEY" \
  -H "Content-Type: application/xml" --data-binary @samples/nfe-cnpj-numerico.xml               # 409
```

**Esperado:** `204`; depois `404` no detalhe e no XML, e o documento some da listagem; e **`409`** ao reenviar o mesmo XML, porque um documento apagado não volta. Com `-H 'If-Match: "0"'` (um ETag velho), o `DELETE` dá `412`.

## 9. Mensageria: outbox, RabbitMQ e agregado

Cada documento novo gera **um evento**: a API o grava na tabela `outbox_messages`, na mesma transação do documento, e o Worker o publica no RabbitMQ. O consumidor atualiza o agregado diário por emitente e UF. A API não depende do broker.

Os comandos abaixo usam o PostgreSQL do compose (`fiscaldocs-postgres`). Troque o usuário e o banco pelos do seu `.env`.

```bash
# O que já foi publicado: um evento por documento novo, com processed_at preenchido
docker exec fiscaldocs-postgres psql -U fiscaldocs -d fiscaldocs -c \
  "SELECT left(id::text,8) id, processed_at, payload->>'issuerDocument' emitente, payload->>'totalAmount' valor FROM outbox_messages ORDER BY occurred_at DESC LIMIT 5;"

# O resultado: contagem e valor por dia, emitente e UF
docker exec fiscaldocs-postgres psql -U fiscaldocs -d fiscaldocs -c \
  "SELECT * FROM daily_issuer_aggregates ORDER BY date;"
```

**Esperado:** o agregado do emitente `11222333000181` no dia 2026-08-14 mostra `document_count = 1` e `total_amount = 1500.00` depois do passo 2. Reenviar o mesmo XML **não** soma de novo.

**Ver a fila no RabbitMQ.** O painel está em `http://localhost:15672` (usuário e senha do `.env`). O Worker consome na hora, então a fila `fiscaldocs.document-ingested` costuma mostrar `Ready = 0`. Para ver uma mensagem parada:

1. Pare o Worker (feche o terminal dele ou `docker compose stop worker`).
2. Envie um documento novo (passo 2 com outro XML).
3. No painel, abra **Queues** e a fila `fiscaldocs.document-ingested`: `Ready = 1`. Em **Get messages**, use *Ack mode: Nack message requeue true* para ler sem consumir.
4. Religue o Worker: a mensagem é consumida e o agregado atualiza.

**A API não depende do broker.** Pare o RabbitMQ (`docker compose stop rabbitmq`) e envie um documento: a resposta continua `201`, e `/health/ready` continua `200`. O evento espera na outbox (`processed_at` vazio). Com `docker compose start rabbitmq`, o Worker o entrega e o agregado atualiza. O script `scripts/smoke-compose.sh` automatiza esse cenário.

## 10. Limite de requisições

O padrão é 60 requisições por 60 s, por IP.

```bash
for i in $(seq 1 70); do curl -s -o /dev/null -w "%{http_code} " -H "X-Api-Key: $KEY" $API/api/v1/documents; done; echo
```

**Esperado:** `200` nas primeiras e **`429`** nas seguintes, com o header `Retry-After`. Aguarde a janela passar para voltar ao normal. As sondas `/health/*` não entram no limite.

## Checklist

| # | Verificação | Esperado |
|---|---|---|
| 1 | `GET /health/live` e `GET /health/ready` sem chave | 200 |
| 2 | Documentos sem chave ou com chave errada | 401 |
| 3 | `POST /api/v1/documents` com um XML novo | 201 |
| 4 | O mesmo XML de novo | 200, mesmo id |
| 5 | Mesma chave de acesso, conteúdo diferente | 409 |
| 6 | XML mal formado e XML com DTD | 422 |
| 7 | `Content-Type` errado e corpo acima de 2 MiB | 415 e 413 |
| 8 | CNPJ alfanumérico, CTe e emitente CPF | 201 |
| 9 | Filtros e paginação da listagem | itens certos; `totalCount` estável |
| 10 | Filtro inválido, inclusive `issuerCnpj` | 400 |
| 11 | CPF do emitente na listagem e no detalhe | `***.982.247-**` |
| 12 | `PATCH` com o ETag certo | 200 e ETag novo |
| 13 | `PATCH` com ETag velho, e sem `If-Match` | 412 e 428 |
| 14 | `GET /api/v1/documents/{id}/xml` | 200, arquivo igual ao enviado |
| 15 | XML do emitente CPF | CPF completo (sem máscara) |
| 16 | `DELETE`, depois detalhe, XML e reenvio | 204, 404, 404, 409 |
| 17 | Evento na outbox e agregado no PostgreSQL | um por documento novo; sem soma duplicada |
| 18 | RabbitMQ parado: `POST` e `/health/ready` | 201 e 200; o evento é entregue na volta |
| 19 | 70 requisições seguidas | 429 com `Retry-After` |
