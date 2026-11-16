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

- não é exposto pelos endpoints JSON (o nome e a UF do destinatário aparecem no detalhe, `GET /api/v1/documents/{id}`, mas não na listagem); o único endpoint que o devolve é o do XML, descrito abaixo;
- não entra no evento do RabbitMQ;
- não é registrado em log;
- **fica guardado em texto puro**, na coluna `recipient_document` e dentro do XML original, que é a fonte legal e precisa ser preservado.

O CPF do **emitente** (produtor rural pessoa física) tem outro tratamento, porque identifica o documento:

- a API o devolve **mascarado** (`***.982.247-**`, `TaxId.Masked`); o CNPJ sai por inteiro;
- a **chave de acesso o carrega por inteiro** (com `000` à esquerda) e é o identificador público do documento, então a máscara reduz a exposição acidental, mas não é sigilo;
- o banco (`IssuerDocument`), o evento e o agregado diário levam o CPF completo, porque o agregado precisa de uma chave estável por emitente;
- o filtro `issuerDocument=` recebe o CPF completo na URL, que proxies e logs de acesso podem registrar; a API não o registra (há teste);
- fica em texto puro, como o do destinatário.

**Download do XML.** `GET /api/v1/documents/{id}/xml` devolve o XML guardado, porque ele é a fonte legal do documento e precisa poder ser recuperado. Ele **não é mascarado**: o CPF do emitente e o do destinatário saem por inteiro, e a máscara vale só para os campos JSON. A mesma chave de API dá acesso, sem escopos para restringi-lo. A resposta sai como anexo `<chave>.xml`, com `Cache-Control: no-store`, e o download não registra o corpo nem o CPF (há teste). O banco guarda o texto já decodificado, então a resposta é o texto em UTF-8 sem BOM, não os bytes originais: uma declaração `encoding` diferente no prólogo não é preservada, e o `charset` do cabeçalho HTTP prevalece sobre ela.

**Segredos.** O `.env` fica fora do Git (o repositório traz só o `.env.example`), o desenvolvimento usa user-secrets, e o CI roda o `gitleaks`.

## Alternativas descartadas

- **JWT ou OAuth.** Exigiria emissor de tokens e fluxo de usuários que o enunciado não pede. A chave de API cobre "autenticação" sem overengineering.
- **Janela deslizante ou token bucket no rate limit.** Controlam melhor a rajada na virada da janela, ao custo de mais estado por cliente. A janela fixa bastou: no pior caso, um cliente consegue até 2× o limite no encontro de duas janelas.
- **Cifrar o CPF no banco (Data Protection) com um hash para busca.** Fica como evolução.

## Consequências e limitações

- **Chave única.** Não há rotação sem novo deploy, nem escopos, nem identificação de quem chamou (todo chamador é `api-client`).
- **Atrás de um proxy reverso**, o IP de origem visto pela API é o do proxy, e todos os clientes dividiriam o mesmo balde do rate limit. Em produção é preciso configurar `ForwardedHeaders` com `KnownProxies` ou `KnownNetworks`. O repositório não faz isso.
- **TLS.** O `UseHttpsRedirection` está ativo (exceto em `/health`), mas o container escuta só HTTP na porta 8080 e não há HSTS. Em produção, o TLS deve terminar num proxy reverso.
- **CPF em texto puro** (destinatário e emitente, este também no evento e no agregado). Depende da proteção do próprio banco, do disco e do broker.
- **O endpoint do XML contorna a máscara.** Quem tem a chave de API lê todos os CPFs. Restringi-lo exigiria escopos de chave, que não existem.
