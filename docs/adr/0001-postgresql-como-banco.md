# ADR 0001 — PostgreSQL como banco de dados

**Status:** aceita

## Contexto

A API guarda documentos fiscais e precisa:

- filtrar por data de emissão, CNPJ do emitente, UF, tipo e status, com paginação;
- impedir que a mesma chave de acesso seja gravada duas vezes, mesmo com requisições simultâneas;
- detectar edições concorrentes do mesmo documento;
- guardar o XML original, que é a fonte legal do documento.

## Decisão

PostgreSQL 16, acessado por EF Core 8 com Npgsql.

- Os campos filtráveis são colunas tipadas e indexadas: `access_key` (único), `(issuer_document, issue_date)`, `(issuer_state, issue_date)` e `issue_date`. O XML bruto fica numa coluna `text` da mesma tabela, então não existe um segundo banco.
- O índice único em `access_key` é o árbitro da idempotência da ingestão ([ADR 0005](0005-idempotencia-da-ingestao.md)).
- A coluna de sistema `xmin` é o token de concorrência otimista ([ADR 0009](0009-concorrencia-otimista-patch-e-soft-delete.md)).
- A outbox usa `FOR UPDATE SKIP LOCKED`, e o consumidor usa `INSERT … ON CONFLICT DO NOTHING` para descartar mensagens repetidas ([ADR 0006](0006-outbox-transacional-e-consumidor-idempotente.md)).
- A imagem oficial `postgres:16` é leve e existe para várias arquiteturas, o que simplifica o `docker compose up` de quem avalia.

## Alternativas descartadas

- **SQL Server.** Atenderia aos requisitos, com equivalentes para cada recurso acima (`rowversion`, `READPAST`, `MERGE`). Foi preterido pelo peso da imagem para quem avalia e por não trazer ganho para este escopo.
- **MongoDB.** É natural para documento hierárquico, mas os filtros exigidos são relacionais, e paginação, unicidade e concorrência otimista dariam mais trabalho (e mais risco) do que em SQL.
- **SQL e Mongo juntos.** Dobra a infraestrutura, os testes e os pontos de falha sem ganho que o escopo pague.

## Consequências

- Os testes de integração rodam contra um Postgres real em container (Testcontainers). O provider em memória do EF não reproduz índice único, concorrência nem `xmin`.
- `xmin` e `SKIP LOCKED` prendem a persistência ao Postgres; trocar de banco exige reescrever esses pontos.
- O XML bruto convive com os metadados na mesma tabela. A listagem não o devolve (devolve só um resumo), mas, em volume muito maior, valeria mover o XML para uma tabela própria ou para armazenamento de objetos.
