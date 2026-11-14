# Samples

XMLs **fictícios** para desenvolvimento e testes. Nomes, CNPJ, CPF e valores são inventados.

| Arquivo | Uso |
|---|---|
| `nfe-cnpj-numerico.xml` | NFe, emitente com CNPJ numérico (legado), destinatário CPF |
| `nfe-cnpj-alfanumerico.xml` | NFe, emitente com CNPJ alfanumérico, destinatário CNPJ numérico |
| `cte-cnpj-numerico.xml` | CTe, emitente numérico, destinatário CPF |
| `cte-cnpj-alfanumerico.xml` | CTe, emitente e remetente alfanuméricos |
| `invalido.xml` | XML mal formado (deve ser rejeitado como inválido) |
| `malicioso-xxe.xml` | Contém `DOCTYPE` com entidade externa (deve ser rejeitado, nunca resolvido) |

## Dígitos verificadores

CNPJ e chave de acesso usam módulo 11, pesos 2–9 da direita para a esquerda, com valor de cada caractere = código ASCII − 48
(regra do CNPJ alfanumérico, NT Conjunta 2025.001). O cálculo reproduz o exemplo oficial da Receita (`12ABC34501DE` → DV `35`).
Os testes do VO `Cnpj` e `ChaveAcesso` devem confirmar esses valores de forma independente.

| Documento | Chave de acesso |
|---|---|
| NFe numérica | `26260811222333000181550010000010011123456782` |
| NFe alfanumérica | `26260812ABC34501DE35550010000020021234567895` |
| CTe numérico | `35260844555666000181570010000030031345678900` |
| CTe alfanumérico | `3526087K9X2M4P000144570010000040041456789018` |
