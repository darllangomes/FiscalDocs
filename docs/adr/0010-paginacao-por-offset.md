# ADR 0010 — Paginação por offset

**Status:** aceita

## Contexto

`GET /api/v1/documents` precisa de paginação e de filtros por tipo, status, CNPJ e UF do emitente e período de emissão.

## Decisão

Paginação por offset: `page` (a partir de 1, padrão 1) e `pageSize` (de 1 a 100, padrão 20). A resposta traz `items`, `page`, `pageSize` e `totalCount`.

- **Ordenação fixa:** data de emissão decrescente e, como desempate, `Id` crescente. Sem o desempate, documentos com a mesma data poderiam repetir ou sumir entre páginas.
- **Projeção:** a listagem devolve um resumo (`DocumentSummary`), sem o XML bruto nem dados do destinatário, e lê com `AsNoTracking`.
- **Filtros:** `type`, `status`, `issuerCnpj` (aceita máscara e minúsculas), `issuerState`, `issuedFrom` e `issuedTo` (datas inclusivas, interpretadas em UTC). Um parâmetro inválido dá `400` com `ValidationProblemDetails`.
- **Índices:** `(issuer_cnpj, issue_date)`, `(issuer_state, issue_date)` e `issue_date`.

## Alternativas descartadas

- **Keyset (cursor).** É estável e rápido em páginas profundas, mas mais complexo, e não oferece um `totalCount` barato. Fica para quando houver medição que o justifique.

## Consequências

- O `OFFSET` custa proporcionalmente ao deslocamento: páginas muito profundas ficam lentas.
- O `totalCount` vem de um `COUNT(*)` a cada requisição, e o custo cresce com o conjunto filtrado.
- Inserções entre duas requisições deslocam as páginas seguintes: um item pode aparecer duas vezes ou ser pulado.
- Os testes de carga que dariam os números para migrar para keyset ainda não foram feitos. Até lá, a decisão fica como está.
