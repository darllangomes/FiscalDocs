# Decisões de arquitetura (ADRs)

Cada registro explica uma decisão: o contexto, o que foi escolhido, o que foi descartado e o custo que aceitamos. Todos estão **aceitos**.

| # | Decisão | Tema |
|---|---|---|
| [0001](0001-postgresql-como-banco.md) | PostgreSQL como banco de dados | Persistência |
| [0002](0002-clean-architecture-com-api-e-worker.md) | Clean Architecture com Api e Worker separados | Estrutura |
| [0003](0003-nfe-e-cte-com-nfse-como-extensao.md) | NFe e CTe implementados, NFSe como extensão | Escopo |
| [0004](0004-cnpj-e-chave-de-acesso-alfanumericos.md) | CNPJ e chave de acesso alfanuméricos | Domínio |
| [0005](0005-idempotencia-da-ingestao.md) | Idempotência da ingestão pelo índice único | Ingestão |
| [0006](0006-outbox-transacional-e-consumidor-idempotente.md) | Outbox transacional e consumidor idempotente | Mensageria |
| [0007](0007-retry-com-ttl-dlq-e-recuperacao-do-rabbitmq.md) | Retry com TTL, DLQ e recuperação do RabbitMQ | Mensageria |
| [0008](0008-api-independente-do-broker.md) | API independente do broker, sondas de saúde e migração na subida | Operação |
| [0009](0009-concorrencia-otimista-patch-e-soft-delete.md) | Concorrência otimista, PATCH restrito e soft delete | API |
| [0010](0010-paginacao-por-offset.md) | Paginação por offset | API |
| [0011](0011-seguranca-e-dados-sensiveis.md) | Segurança e dados sensíveis | Segurança |

Para mudar uma decisão, escreva um novo registro que substitua o anterior e troque o status do antigo para "substituída por ADR NNNN".
