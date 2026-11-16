# ADR 0005 — Idempotência da ingestão pelo índice único da chave de acesso

**Status:** aceita

## Contexto

O mesmo XML pode chegar mais de uma vez: reenvio por timeout, nova tentativa do cliente, duas requisições simultâneas. Um documento fiscal não pode ser gravado duas vezes, e o sistema precisa distinguir um reenvio legítimo de um conteúdo diferente com a mesma chave de acesso.

## Decisão

A chave de acesso é a chave natural do documento, e o índice único em `access_key` decide.

`IngestDocumentHandler` tenta inserir primeiro. Se o banco recusar por duplicidade (`TryAddAsync` devolve `false`), o handler busca o documento existente e compara o `XmlHash`: o SHA-256 do XML recebido, em hexadecimal minúsculo.

| Situação | Resposta |
|---|---|
| Chave nova | `201 Created`, com `Location` e o corpo `{ "id": "…" }` |
| Mesma chave e mesmo hash | `200 OK` com o `id` existente: o reenvio é idempotente |
| Mesma chave e hash diferente | `409 Conflict`: conteúdo divergente é sinal de erro ou adulteração, não de duplicata |
| Mesma chave de um documento apagado | `409 Conflict`: documento fiscal tem guarda obrigatória, e a chave continua ocupada |

O comportamento sob concorrência tem teste: 20 uploads simultâneos do mesmo XML produzem exatamente um `201`, dezenove `200`, um único `id` e uma única linha.

## Alternativas descartadas

- **Consultar antes de inserir (check-then-insert).** Tem condição de corrida: dois uploads simultâneos passam na consulta e os dois inserem. Só o banco é um árbitro confiável.
- **Responder `409` a qualquer reenvio.** Obrigaria o cliente a tratar como erro uma nova tentativa que deu certo.

## Consequências

- O hash vale para os bytes recebidos. O mesmo documento reserializado (espaços, quebras de linha, ordem de atributos) conta como conteúdo diferente e recebe `409`. Ao reenviar com `curl`, use `--data-binary`: `-d @arquivo` remove as quebras de linha e altera o hash.
- Depois de apagado, um documento não pode ser reenviado, e não há endpoint de restauração ([ADR 0009](0009-concorrencia-otimista-patch-e-soft-delete.md)).
- Uma duplicata não deixa linha na outbox, porque documento e evento nascem na mesma transação ([ADR 0006](0006-outbox-transacional-e-consumidor-idempotente.md)).
