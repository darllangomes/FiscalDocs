# ADR 0006 — Outbox transacional e consumidor idempotente

**Status:** aceita

## Contexto

Cada documento ingerido gera um evento no RabbitMQ. Gravar no banco e publicar no broker são duas operações em sistemas diferentes e não podem ser atômicas (*dual write*): se a API cair entre as duas, o sistema fica com um documento sem evento, ou com um evento de um documento que não existe. O broker, por sua vez, só garante entrega *at-least-once*, então o consumidor recebe mensagens repetidas.

## Decisão

**Publicação (outbox).** Na ingestão, `TryAddAsync` grava o documento e uma linha em `outbox_messages` no mesmo `SaveChanges`, ou seja, na mesma transação. A API não fala com o broker. Quem publica é o Worker:

- `OutboxRelay` (hospedado por `OutboxRelayService`) drena lotes de 50 mensagens e espera `Outbox:PollIntervalSeconds` (padrão 1 s) entre as varreduras.
- `OutboxDispatcher` lê as pendentes, das mais antigas para as mais novas, com `FOR UPDATE SKIP LOCKED` (dois relays não pegam a mesma linha). Publica com *publisher confirms*, mensagem persistente e `mandatory`, e então marca `ProcessedAt`. **Na primeira falha ele para** e deixa o resto para a próxima rodada: isso preserva a ordem e evita insistir num broker fora do ar.

**Consumo (idempotência).** `DocumentIngestedProcessor` roda numa transação. Primeiro faz `INSERT INTO processed_messages … ON CONFLICT DO NOTHING`, com o `MessageId` como chave. Se nenhuma linha entrou, a mensagem é repetida e vira no-op, mesmo quando as duas cópias chegam ao mesmo tempo. Se entrou, o mesmo commit faz o upsert atômico do agregado diário em `daily_issuer_aggregates`: a chave é o dia de emissão em UTC, o CNPJ do emitente e a UF, e o agregado guarda a contagem e o valor total.

**Evento.** `DocumentIngestedEvent` carrega `MessageId`, `DocumentId`, `Type`, `IssuerDocument`, `IssuerState`, `IssueDate`, `TotalAmount` e `OccurredAt`. Não leva XML nem dados do destinatário. `IssuerDocument` é o CNPJ do emitente ou, quando ele é pessoa física, o CPF completo (ver [ADR 0011](0011-seguranca-e-dados-sensiveis.md)). Ao renomear o campo (antes `issuerCnpj`), a migration reescreve as mensagens ainda pendentes na outbox; mensagens já publicadas e não consumidas, que estejam na fila ou na DLQ, continuam com o nome antigo e chegam ao consumidor sem `issuerDocument`: o ideal é drenar a fila antes do deploy.

## Alternativas descartadas

- **Publicar logo depois do commit.** É mais simples, mas perde o evento se o processo cair entre o commit e a publicação.
- **Confiar em "exactly-once" do broker.** Não existe. Sem a trava de idempotência, uma reentrega somaria o mesmo documento duas vezes no agregado.

## Consequências

- A garantia é *at-least-once* de ponta a ponta, e a duplicação é absorvida pelo consumidor.
- A entrega ganha uma latência de até o intervalo de varredura. Se o broker cair, a API segue aceitando documentos e os eventos esperam na outbox ([ADR 0008](0008-api-independente-do-broker.md)).
- A ordem de entrega não é garantida de ponta a ponta: uma mensagem que passa pela fila de retry chega depois das seguintes. O agregado é uma soma, então não depende da ordem.
- As linhas da outbox já processadas **não são removidas**, e a tabela só cresce. A limpeza é uma pendência.
- Ainda não há métrica de atraso da outbox nem sonda de saúde do Worker.
