# ADR 0002 — Clean Architecture com Api e Worker separados

**Status:** aceita

## Contexto

O sistema tem dois trabalhos diferentes: responder requisições HTTP e processar mensagens do broker. Eles falham, escalam e são implantados de forma diferente. Além disso, uma regra de dependência só é verificável por teste se existir uma fronteira clara entre as camadas.

## Decisão

```text
Domain  ←  Application  ←  Infrastructure  ←  Api
                                           ←  Worker
```

| Projeto | Responsabilidade |
|---|---|
| `FiscalDocs.Domain` | `FiscalDocument`, objetos de valor (`Cnpj`, `AccessKey`, `Mod11`) e enums. Sem dependências. |
| `FiscalDocs.Application` | Casos de uso (handlers), portas (`IFiscalDocumentRepository`, `IDocumentEventPublisher`, `IDocumentParser`, `IXmlDocumentLoader`) e o evento `DocumentIngestedEvent`. |
| `FiscalDocs.Infrastructure` | EF Core/Npgsql, parsers de XML, RabbitMQ, outbox e processador do consumidor. Implementa as portas. |
| `FiscalDocs.Api` | Controllers, autenticação, rate limiting, health checks e tratamento de erros. |
| `FiscalDocs.Worker` | Consumidor do RabbitMQ e relay da outbox. |

Api e Worker são hosts distintos, com imagem e container próprios.

A regra de dependência é verificada por `tests/FiscalDocs.ArchitectureTests` (NetArchTest.Rules, 14 testes):

- o Domain não depende de outras camadas nem de frameworks;
- a Application não depende das camadas externas nem de frameworks;
- a Infrastructure não depende dos hosts, e Api e Worker não dependem um do outro;
- controllers não usam a Infrastructure;
- handlers são `sealed` e as portas da Application são interfaces;
- quatro testes de controle positivo provam que as regras enxergam os tipos. Sem eles, um filtro errado faria as regras passarem sem verificar nada.

## Alternativas descartadas

- **Consumidor como `BackgroundService` dentro da API.** Mais simples, mas um defeito no consumidor derrubaria a ingestão, e os dois não escalariam separadamente.
- **Projeto único organizado em pastas (vertical slice).** Menos cerimônia, mas a regra de dependência passaria a depender de disciplina, não de teste.

## Consequências

- Mais projetos e mais serviços no `docker-compose.yml`.
- Alguns handlers são finos: `GetDocumentHandler` só repassa a chamada ao repositório. É o preço de manter os controllers fora da Infrastructure.
- Para conter o excesso de cerimônia: sem CQRS formal, sem MediatR nem AutoMapper (as versões recentes têm licença comercial), mapeamento manual e nada de DDD tático além de entidade e objetos de valor.
