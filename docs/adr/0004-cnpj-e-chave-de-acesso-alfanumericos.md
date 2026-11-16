# ADR 0004 — CNPJ e chave de acesso alfanuméricos como requisito de primeira classe

**Status:** aceita

## Contexto

Pela Nota Técnica Conjunta 2025.001, a partir de julho de 2026 a Receita Federal passa a emitir CNPJs com letras: as 12 primeiras posições são alfanuméricas e os 2 dígitos verificadores continuam numéricos. A chave de acesso muda junto, porque carrega o CNPJ do emitente (posições 7 a 20). Um modelo que trate o CNPJ como número falha justamente nos documentos mais novos. Os CNPJs numéricos antigos continuam válidos.

## Decisão

- CNPJ e chave de acesso são **strings** normalizadas (maiúsculas, sem máscara), guardadas em `varchar(14)` e `varchar(44)`.
- O objeto de valor `Cnpj` aceita máscara (`.`, `/`, `-`) e minúsculas na entrada, valida o formato `[0-9A-Z]{12}[0-9]{2}` e os dois dígitos verificadores por módulo 11: pesos de 2 a 9 da direita para a esquerda, com o valor de cada caractere igual ao código ASCII menos 48. A regra reproduz o exemplo oficial (`12ABC34501DE` → DV `35`) e serve também para os CNPJs numéricos.
- `AccessKey` valida as 44 posições (as 14 do CNPJ aceitam letras), o dígito verificador e o modelo. `FiscalDocument.Create` exige que o modelo combine com o tipo (55 para NFe, 57 para CTe) e que o CNPJ da chave seja o do emitente.
- O filtro `issuerDocument` da listagem aceita CNPJ ou CPF (`TaxId.TryParse`), e os índices operam sobre a forma normalizada.
- Uma verificação no CI (job `security`) barra, em `src/**/*.cs`, `\d{14}`, `\d{44}`, `long.Parse` e `ulong.Parse`, para que ninguém reintroduza o tratamento numérico.
- As amostras em `samples/` cobrem NFe e CTe com CNPJ numérico e alfanumérico.

## Alternativas descartadas

- **Tipo numérico para o CNPJ.** Não representa letras.
- **Validar o XML contra o XSD.** Acopla a API ao pacote de schemas de cada Nota Técnica, que muda com o tempo. A API armazena documentos, não os homologa.
- **Rejeitar CNPJ alfanumérico por data de implantação.** Quem vigia a data é a SEFAZ na autorização, não a nossa API de armazenamento.

## Consequências

- **Emitente pessoa física (CPF).** O emitente é um `TaxId`: CNPJ (14 posições) ou CPF (11), e o tamanho distingue os dois. O parser lê `<CNPJ>` ou `<CPF>` e valida cada um pela regra do próprio elemento, então um CPF dentro de `<CNPJ>` é rejeitado. Na chave de acesso o CPF ocupa as 14 posições do emitente com `000` à esquerda. Como um CNPJ também pode começar com `000` (`00000000000191` e o CPF `00000000191` são ambos válidos e geram o mesmo campo), a chave **não infere o tipo**: o elemento do XML decide, e `AccessKey.IsIssuedBy(TaxId)` só confirma que o número é o mesmo. O CPF repetido (`111.111.111-11`) é rejeitado. A coluna é `IssuerDocument` (antes `IssuerCnpj`), a API devolve o CPF mascarado e o evento leva o documento completo (ver [ADR 0011](0011-seguranca-e-dados-sensiveis.md)). O destinatário pode ser CPF, CNPJ ou estrangeiro.
- **CNPJ com todos os dígitos iguais** (como `00000000000000`) não é rejeitado hoje. É uma decisão em aberto.
- As datas de implantação variam por tipo de documento e por fonte. A versão da Nota Técnica adotada deve ser revista quando os schemas oficiais mudarem.
