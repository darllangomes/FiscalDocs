# ADR 0011 — Segurança e dados sensíveis

**Status:** aceita

## Contexto

A API recebe XML de terceiros e guarda dados fiscais. O XML contém o CPF do destinatário, que é dado pessoal (LGPD). O enunciado pede autenticação, tratamento de dados sensíveis e proteção contra abuso, sem exigir um fluxo de usuários.

## Decisão

**Autenticação: uma chave de API.** O cliente envia o header `X-Api-Key`, e o valor esperado vem de `Auth:ApiKey` (user-secrets em desenvolvimento, variável de ambiente em produção). A API não sobe sem ele (`ValidateOnStart`). A comparação é feita sobre o SHA-256 dos dois valores, com `CryptographicOperations.FixedTimeEquals`, em tempo constante e com comprimentos iguais. O header precisa aparecer exatamente uma vez. A política padrão exige usuário autenticado em todas as rotas (`FallbackPolicy`), e só as sondas de saúde são anônimas. Chave ausente ou errada resulta em `401`.

**Rate limiting: janela fixa por IP.** O padrão é 60 requisições por 60 s (`RateLimiting:PermitLimit` e `RateLimiting:WindowSeconds`), sem fila. Ao estourar, a API responde `429` em `problem+json`, com `Retry-After`. `/health` não tem limite.

**XML hostil.** O XML é lido com `DtdProcessing.Prohibit`, `XmlResolver = null` e `MaxCharactersInDocument` de 5 000 000. Isso bloqueia XXE e "billion laughs": um XML com DTD é rejeitado (`422`), nunca resolvido. O corpo é limitado a 2 MiB (`413`), e só `application/xml` e `text/xml` são aceitos (`415`).

**Erros e logs.** Os erros saem como `ProblemDetails`, sem stack trace. Uma requisição rejeitada gera um único log, com o tipo da exceção e o status, nunca o corpo nem o XML. Testes de integração provam que o CPF (puro ou formatado) e o XML não aparecem nos logs de uma ingestão bem-sucedida, de um reenvio e de uma rejeição.

**Dados pessoais.** O CPF do destinatário:

- não é exposto por nenhum endpoint, e o XML bruto não tem endpoint de leitura (o nome e a UF do destinatário aparecem no detalhe, `GET /api/v1/documents/{id}`, mas não na listagem);
- não entra no evento do RabbitMQ;
- não é registrado em log;
- **fica guardado em texto puro**, na coluna `recipient_document` e dentro do XML original, que é a fonte legal e precisa ser preservado.

**Segredos.** O `.env` fica fora do Git (o repositório traz só o `.env.example`), o desenvolvimento usa user-secrets, e o CI roda o `gitleaks`.

## Alternativas descartadas

- **JWT ou OAuth.** Exigiria emissor de tokens e fluxo de usuários que o enunciado não pede. A chave de API cobre "autenticação" sem overengineering.
- **Janela deslizante ou token bucket no rate limit.** Controlam melhor a rajada na virada da janela, ao custo de mais estado por cliente. A janela fixa bastou: no pior caso, um cliente consegue até 2× o limite no encontro de duas janelas.
- **Cifrar o CPF no banco (Data Protection) com um hash para busca.** Fica como evolução.

## Consequências e limitações

- **Chave única.** Não há rotação sem novo deploy, nem escopos, nem identificação de quem chamou (todo chamador é `api-client`).
- **Atrás de um proxy reverso**, o IP de origem visto pela API é o do proxy, e todos os clientes dividiriam o mesmo balde do rate limit. Em produção é preciso configurar `ForwardedHeaders` com `KnownProxies` ou `KnownNetworks`. O repositório não faz isso.
- **TLS.** O `UseHttpsRedirection` está ativo (exceto em `/health`), mas o container escuta só HTTP na porta 8080 e não há HSTS. Em produção, o TLS deve terminar num proxy reverso.
- **CPF em texto puro.** Depende da proteção do próprio banco e do disco.
