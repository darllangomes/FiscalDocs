# ADR 0008 — API independente do broker, sondas de saúde e migração na subida

**Status:** aceita

## Contexto

Com a outbox ([ADR 0006](0006-outbox-transacional-e-consumidor-idempotente.md)), a API só precisa do banco para aceitar um documento. Se ela também dependesse do RabbitMQ, para publicar ou para se declarar "pronta", uma queda do broker pararia a ingestão que o desenho quer proteger. Além disso, as sondas de saúde são consumidas por orquestradores: não têm credencial, e uma sonda lenta é tão ruim quanto uma sonda falha.

## Decisão

- **A API não conhece o RabbitMQ.** `AddInfrastructure` registra banco, repositório e parsers. `AddMessaging` (provider, opções e publicador do RabbitMQ) só é chamado pelo Worker e lança na subida se faltar `RabbitMq:Uri`. Com o broker fora do ar, `POST /api/v1/documents` continua devolvendo `201`, e o evento espera na outbox. O smoke test prova esse caminho.
- **Liveness e readiness.** `GET /health/live` responde `200` sempre, sem executar verificações. `GET /health/ready` verifica só o banco, com prazo de 3 s, e responde `503` se ele cair. O corpo é `text/plain`, sem detalhes. O RabbitMQ não entra na readiness.
- **As sondas ficam fora da autenticação, do rate limit e do redirecionamento HTTPS**: chegam por HTTP simples e sem chave.
- **Prazo explícito.** O timeout padrão de um health check é de 30 s, e o `CanConnectAsync` do EF/Npgsql não respeita o cancelamento durante o handshake: um banco que aceita a conexão e não responde segurava a sonda por 15 s. Por isso a verificação usa `WaitAsync(cancellationToken)` e o check tem timeout de 3 s.
- **Migração na subida.** `Database:MigrateOnStartup` (padrão `false`) roda `MigrateAsync()` depois do `Build()`. O `docker-compose.yml` liga a opção na API.

## Alternativas descartadas

- **Readiness incluir o RabbitMQ.** Tiraria a API de circulação por um problema que não a impede de trabalhar.
- **A API publicar direto, com o broker opcional.** Reintroduz o *dual write* que a outbox resolve.
- **Job de migração separado.** É o mais correto com várias réplicas, mas é excessivo para este escopo.

## Consequências

- O EF Core 8 não tem trava de migração: duas réplicas subindo ao mesmo tempo podem migrar em corrida. Com a opção ligada, suba **uma réplica** na primeira vez, ou aplique as migrations num passo separado do deploy (por exemplo, um *migration bundle*).
- Uma queda do broker não aparece na readiness da API. Hoje ela só é visível nos logs do Worker, no painel do RabbitMQ e no crescimento da outbox. Não há sonda de saúde do Worker nem métrica de atraso da outbox.
- O smoke test ainda não confere `/health/ready` com o RabbitMQ parado.
