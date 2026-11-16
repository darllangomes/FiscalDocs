# ADR 0009 — Concorrência otimista, PATCH restrito e soft delete

**Status:** aceita

## Contexto

O XML é a fonte legal do documento: se a API aceitasse alterar valor ou emitente, banco e XML passariam a divergir. Mesmo assim, quem opera o sistema precisa marcar o andamento (status) e anotar observações, possivelmente ao mesmo tempo. E documento fiscal tem obrigação de guarda (em geral, cinco anos), então apagá-lo fisicamente é indefensável.

## Decisão

**`PATCH /api/v1/documents/{id}` altera apenas campos de gestão:** `status` (`Received`, `Reviewed` ou `Archived`) e `notes` (até 500 caracteres; `null` limpa a observação). O corpo precisa de ao menos um dos dois, e qualquer outro campo dá `400`.

**Concorrência otimista com ETag forte.** O ETag é o `xmin` da linha, entre aspas (por exemplo, `"1234"`), devolvido por `GET` e `PATCH`. O `PATCH` exige `If-Match`. A comparação vai junto da escrita, porque `xmin` é o token de concorrência do EF: dois `PATCH` com o mesmo ETag não passam os dois. A ordem das verificações é:

1. `404`: o documento não existe;
2. `428`: falta o `If-Match`;
3. `412`: o ETag é malformado ou está desatualizado;
4. `400`: o corpo é inválido.

**`DELETE` é soft delete** (`is_deleted = true`, resposta `204`). O `If-Match` é opcional e, se vier, segue a mesma regra do `PATCH`. Um filtro global do EF esconde os apagados, então `GET`, `PATCH` e `DELETE` de um documento apagado dão `404` (o segundo `DELETE` também). Reenviar o mesmo XML dá `409` ([ADR 0005](0005-idempotencia-da-ingestao.md)). Não há endpoint de restauração.

## Alternativas descartadas

- **`PUT` do documento inteiro, ou editar valores fiscais.** Quebraria a imutabilidade do documento.
- **Exclusão física.** Contraria a obrigação de guarda.
- **Sem controle de concorrência (o último a escrever vence).** Perderia a anotação de outro operador sem avisar.

## Consequências

- O `xmin` tem 32 bits e muda a cada escrita na linha, e o contador de transações do Postgres pode dar a volta. O ETag serve para comparar com a versão atual, não como identificador permanente.
- `If-Match` com mais de um ETag, com `*` ou com ETag fraco (`W/"…"`) resulta em `412`. É mais restrito que a RFC 9110, que aceita lista e `*`. O cliente recebe um ETag e devolve esse ETag.
- Um corpo malformado, ou com campo desconhecido, dá `400` mesmo que o `id` não exista, porque a validação do corpo roda antes da ação. O `404` só aparece quando o corpo é válido.
- Os dados só crescem, e os índices incluem os apagados (não há índice parcial por `is_deleted`).
