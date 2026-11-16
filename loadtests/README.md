# Testes de carga (k6)

```bash
loadtests/run.sh [ingest|queries|all] [--keep]     # RATE, DURATION e SEED mudam a carga
```

O script sobe a stack num projeto próprio (`fiscaldocs-load`), com o rate limit elevado (`docker-compose.loadtest.yml`; no padrão de 60 requisições por minuto o k6 só mediria os `429`), roda o k6 num container da mesma rede e, no fim, mede quanto a outbox demora para esvaziar. Como os containers têm nomes fixos, ele recusa rodar com outra stack `fiscaldocs-*` no ar (`docker compose down` antes). Os resumos ficam em `loadtests/results/` (fora do Git).

- **`k6/ingest.js`**: `POST` de uma NFe nova por iteração, a uma taxa fixa (`constant-arrival-rate`), para que um servidor lento não alivie a própria carga. A chave de acesso é única e tem dígito verificador válido (`k6/lib/documents.js`); sem isso o teste mediria só o caminho de duplicata. Os 4 emitentes alternam CNPJs numéricos e alfanuméricos.
- **`k6/queries.js`**: grava `SEED` documentos e mede, um cenário por vez: listagem simples, filtrada (tipo, UF e período), por CNPJ, **última página** (o pior caso do `OFFSET`) e `GET` por `id`.

## Resultados

Máquina: Docker Desktop no Windows com 4 CPUs e 8 GB, rodando tudo junto (API, Worker, PostgreSQL, RabbitMQ e o próprio k6). Números de um notebook de desenvolvimento, não de um ambiente de produção.

### Ingestão

| Taxa pedida | Requisições | p50 | p95 | p99 | Falhas | Iterações descartadas | Outbox esvazia em |
|---|---|---|---|---|---|---|---|
| 50/s por 30 s | 1 501 | n/d | 7,6 ms | n/d | 0 | 0 | 1 s |
| 100/s por 30 s | 2 958 | 6 ms | 929 ms | 1 921 ms | 1 | 43 | 0 s |

A 50/s a API folga. A 100/s a mediana continua em 6 ms, mas a cauda explode e o k6 deixa de conseguir manter a taxa: o limite nesta máquina fica entre 50 e 100 documentos por segundo. O gargalo não foi investigado (a máquina divide 4 CPUs entre cinco processos). Em todas as rodadas o número de documentos gravados bateu com o do agregado, ou seja, nenhum evento se perdeu nem foi contado duas vezes.

### Consulta (5 000 documentos, 100 requisições por segundo por cenário, 30 s)

| Cenário | p50 | p95 | p99 | max |
|---|---|---|---|---|
| `list_default` (página 1) | 6 ms | 24 ms | 126 ms | 414 ms |
| `list_filtered` (tipo, UF e período) | 6 ms | 9 ms | 13 ms | 33 ms |
| `list_cnpj` | 6 ms | 8 ms | 9 ms | 20 ms |
| `list_deep` (última página, 250) | 16 ms | 45 ms | 130 ms | 226 ms |
| `get_by_id` | 2 ms | 3 ms | 5 ms | 27 ms |

Nenhuma requisição falhou.

## O que isso diz sobre a paginação por keyset

Com 5 000 documentos, a última página custa cerca de 3 vezes a primeira no p50 (16 ms contra 6 ms) e fica em 45 ms no p95. Nesse volume o `OFFSET` não é problema, então **a decisão do [ADR 0010](../docs/adr/0010-paginacao-por-offset.md) se mantém**. A medição não cobre volumes maiores: o custo cresce com o deslocamento, e vale repetir com `SEED=100000` antes de mudar de ideia.
