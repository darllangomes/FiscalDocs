# Testes de carga (k6)

```bash
loadtests/run.sh [ingest|queries|all] [--keep]
```

| Variável | Vale para | Padrão | O que faz |
|---|---|---|---|
| `RATES` | ingestão | `50` | Taxas em documentos por segundo, separadas por vírgula. Cada uma é um cenário, em sequência e com 15 s de pausa. |
| `DURATION` | os dois | `60s` (ingestão), `30s` (consulta) | Duração de cada taxa ou cenário, com o sufixo `s`. |
| `ITEMS` | os dois | `20` | Itens por NFe. Com 20 o XML tem cerca de 23 KB; com 990 (o máximo do leiaute), cerca de 730 KB. |
| `SEED` | consulta | `5000` | Documentos gravados antes de medir as consultas. |
| `RATE` | consulta | `30` | Requisições por segundo de cada cenário de consulta. |

O script sobe a stack num projeto próprio (`fiscaldocs-load`), com o rate limit elevado (`docker-compose.loadtest.yml`; no padrão de 60 requisições por minuto o k6 só mediria os `429`), roda o k6 num container da mesma rede e, no fim, mede quanto a outbox demora para esvaziar e confere se o total de documentos bate com o do agregado. Como os containers têm nomes fixos, ele recusa rodar com outra stack `fiscaldocs-*` no ar (`docker compose down` antes). Os resumos ficam em `loadtests/results/` (fora do Git).

- **`k6/ingest.js`**: `POST` de uma NFe nova por iteração, a uma taxa fixa (`constant-arrival-rate`), para que um servidor lento não alivie a própria carga. A chave de acesso é única e tem dígito verificador válido; sem isso o teste mediria só o caminho de duplicata. Os 4 emitentes alternam CNPJs numéricos e alfanuméricos.
- **`k6/lib/documents.js`**: monta a NFe no formato real, com itens e impostos, transporte, pagamento, assinatura e protocolo (a mesma estrutura de `samples\nfe-realista-*.xml`). A primeira versão do teste usava um XML de 2,5 KB sem itens nem assinatura, que não representa um documento real.
- **`k6/queries.js`**: grava `SEED` documentos e mede, um cenário por vez: listagem simples, filtrada (tipo, UF e período), por CNPJ, **última página** (o pior caso do `OFFSET`) e `GET` por `id`.

## Resultados

Máquina: Docker Desktop no Windows com 4 CPUs e 8 GB, rodando tudo junto (API, Worker, PostgreSQL, RabbitMQ e o próprio k6). São números de um notebook de desenvolvimento, de uma execução de 30 s por taxa, sem repetição: servem para comparar ordens de grandeza, não para prometer capacidade.

### Ingestão com NFe de 23 KB (20 itens)

Taxas em ordem crescente, a partir de uma API já aquecida. Nenhuma requisição falhou e nenhuma iteração foi descartada.

| Taxa | p50 | p95 | p99 |
|---|---|---|---|
| 10/s | 5 ms | 11 ms | 73 ms |
| 25/s | 4 ms | 7 ms | 13 ms |
| 50/s | 4 ms | 7 ms | 12 ms |
| 75/s | 4 ms | 7 ms | 13 ms |
| 100/s | 4 ms | 8 ms | 14 ms |

Ao final, 7 804 documentos gravados e 7 804 agregados, com a outbox vazia menos de 1 s depois da carga.

### Onde fica o limite

Uma execução separada, com a API recém-subida (fria), subindo a taxa até a saturação:

| Taxa | p50 | p95 | p99 | Falhas | Iterações descartadas |
|---|---|---|---|---|---|
| 100/s (primeira, a frio) | 5 ms | 108 ms | 805 ms | 0 | 0 |
| 200/s | 5 ms | 19 ms | 44 ms | 0 | 0 |
| 300/s | 7 ms | 55 ms | 103 ms | 0 | 0 |
| 400/s | 10 ms | 1 051 ms | 2 075 ms | 12 | 309 |

- **Partida a frio:** os 100/s logo depois de subir a API tiveram p95 de 108 ms, contra 8 ms com a API aquecida. Quem mede a primeira taxa de uma execução mede também o aquecimento.
- **Limite:** nesta máquina, a ingestão de documentos de 23 KB aguenta 300/s com p95 de 55 ms e satura entre 300 e 400/s. Não investiguei o gargalo, e as 12 falhas de 400/s não foram diagnosticadas (a stack já havia sido desmontada).
- **Consumidor:** com 29 683 documentos gravados, a outbox levou 22 s para esvaziar depois do fim da carga e o total agregado bateu (29 683). Acima de cerca de 300 documentos por segundo o consumidor forma fila, mas não perde nem duplica eventos.

### Ingestão com NFe de 731 KB (990 itens, o máximo do leiaute)

| Taxa | p50 | p95 | p99 |
|---|---|---|---|
| 1/s (primeira, a frio) | 47 ms | 76 ms | 387 ms |
| 2/s | 27 ms | 34 ms | 48 ms |
| 5/s | 30 ms | 65 ms | 89 ms |
| 10/s | 26 ms | 48 ms | 66 ms |
| 20/s | 26 ms | 36 ms | 57 ms |

Sem falhas nem descartes; 1 144 documentos gravados e agregados. A 20/s são cerca de 15 MB por segundo de XML. Não testei taxas maiores com esse peso.

### Consulta (5 000 documentos de 23 KB, 100 requisições por segundo por cenário, 30 s)

| Cenário | p50 | p95 | p99 | max |
|---|---|---|---|---|
| `list_default` (página 1) | 4 ms | 9 ms | 119 ms | 477 ms |
| `list_filtered` (tipo, UF e período) | 4 ms | 6 ms | 10 ms | 33 ms |
| `list_cnpj` | 4 ms | 6 ms | 8 ms | 14 ms |
| `list_deep` (última página, 250) | 8 ms | 12 ms | 21 ms | 35 ms |
| `get_by_id` | 2 ms | 3 ms | 5 ms | 31 ms |

Nenhuma requisição falhou. A listagem não lê o XML, então o tamanho do documento quase não pesa nela.

## O que isso diz sobre a paginação por keyset

Com 5 000 documentos, a última página custa cerca do dobro da primeira no p50 (8 ms contra 4 ms) e fica em 12 ms no p95. Nesse volume o `OFFSET` não é problema, então **a decisão do [ADR 0010](../docs/adr/0010-paginacao-por-offset.md) se mantém**. A medição não cobre volumes maiores: o custo cresce com o deslocamento, e vale repetir com `SEED=100000` antes de mudar de ideia.

## Histórico: a primeira medição estava errada

A primeira rodada usava um XML de 2,5 KB e concluiu que a ingestão "satura entre 50 e 100 documentos por segundo" (p95 de 929 ms a 100/s). A conclusão não se sustentou: com documentos dez vezes maiores e a API aquecida, 100/s teve p95 de 8 ms. Parte da diferença é a partida a frio, que também aparece acima (108 ms), mas o p95 de 929 ms não se reproduziu e não sei explicá-lo por completo. Por isso as taxas passaram a rodar em sequência crescente, e os resultados dizem quando a API estava fria.
