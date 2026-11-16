# ADR 0007 — Retry com TTL, DLQ e recuperação de conexão do RabbitMQ

**Status:** aceita

## Contexto

O consumidor precisa sobreviver a falhas transitórias (banco indisponível por instantes, por exemplo) sem perder mensagem e sem travar a fila. A mensagem que nunca vai funcionar precisa ficar guardada para análise. E o backoff não pode depender da memória de um processo que pode cair.

## Decisão

O retry é feito pela topologia do broker, não em memória.

```text
fiscaldocs.events ──document.ingested──▶ fiscaldocs.document-ingested ──▶ consumidor
(direct)                                       ▲                              │
                                               │ TTL expira: volta pelo       │ falha na tentativa n
                                               │ exchange                     ▼
                                               └──── fiscaldocs.document-ingested.retry.n   (n = 1, 2, 3)

Tentativas esgotadas ou mensagem malformada:
  nack(requeue: false) ──▶ fiscaldocs.dlx ──▶ fiscaldocs.document-ingested.dlq
```

- `RabbitMqTopology` declara tudo. A fila principal tem `x-dead-letter-exchange` apontando para o `fiscaldocs.dlx`, então um `nack(requeue: false)` já estaciona a mensagem na DLQ.
- Existe uma fila `…retry.N` por tentativa, cada uma com `x-message-ttl` e `x-dead-letter-exchange` de volta para o exchange principal. Os atrasos vêm de `RabbitMqOptions.RetryDelays`: 5 s, 30 s e 2 min. Usar uma fila por atraso é de propósito: todas as mensagens de uma fila têm o mesmo TTL, e o RabbitMQ só expira a mensagem que está na cabeça da fila.
- Quando o processamento falha, o consumidor copia a mensagem (mesmo corpo, `MessageId` e `Type`, e o header `x-attempt` incrementado) para a `retry.(x-attempt + 1)` por um canal com *publisher confirms* e só então dá `ack` na original. Se a cópia falhar, faz `nack` com requeue.
- Quando `x-attempt` já atingiu o número de filas de retry, a mensagem vai para a DLQ. Com os atrasos padrão, são no máximo 4 entregas: a original e 3 retries.
- Uma mensagem que não deserializa vai direto para a DLQ, porque tentar de novo não a conserta. Qualquer outra exceção é tratada como transitória: o consumidor não distingue falha permanente de transitória no processamento.
- O consumidor usa `ack` manual e `prefetch` 10.

**Conexão.** `RabbitMqConnectionProvider` mantém uma única conexão, criada sob demanda, e **nunca substitui uma conexão existente**. A recuperação automática do RabbitMQ.Client 7 refaz conexão, canais e consumidores sozinha, e trocar uma conexão "fechada" durante a recuperação mataria o canal do consumidor (um bug real, encontrado pelo smoke test).

## Alternativas descartadas

- **MassTransit.** Resolveria o retry com configuração, mas esconde ack, nack e dead-letter, que eram parte do que este projeto quer demonstrar. A versão 9 também passou a ser comercial.
- **Retry em memória (RabbitMQ.Client e Polly).** O estado do backoff mora no processo: uma queda zera a contagem, e esperar com `Task.Delay` segura o canal e o `prefetch`.

## Consequências

- Mais código de infraestrutura e mais testes de integração para provar a topologia (publicador, consumidor, retry e DLQ).
- Os TTLs são argumentos da fila. Mudar `RetryDelays` com as filas já criadas dá `PRECONDITION_FAILED`, e é preciso apagar as filas de retry (`rabbitmqctl delete_queue`) antes. O mesmo vale para os argumentos da fila principal.
- Entre copiar para o retry e dar `ack`, uma queda duplica a mensagem. O consumidor idempotente ([ADR 0006](0006-outbox-transacional-e-consumidor-idempotente.md)) absorve a duplicata.
- Uma falha determinística no processamento gasta as três tentativas (cerca de 2 min 35 s) antes de chegar à DLQ.
- As mensagens da DLQ ficam guardadas para análise manual. O repositório não tem ferramenta de reprocessamento.
- Se a recuperação automática da conexão desistir, só reiniciar o Worker resolve. Não há detecção automática desse estado.
- `RetryDelays` e o timeout de conexão são valores padrão no código: só `RabbitMq:Uri` é lido da configuração.
