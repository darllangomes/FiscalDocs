# Samples

XMLs **fictícios** para desenvolvimento e testes. Nomes, CNPJ, CPF e valores são inventados.

| Arquivo | Uso |
|---|---|
| `nfe-cnpj-numerico.xml` | NFe, emitente com CNPJ numérico (legado), destinatário CPF |
| `nfe-cnpj-alfanumerico.xml` | NFe, emitente com CNPJ alfanumérico, destinatário CNPJ numérico |
| `cte-cnpj-numerico.xml` | CTe, emitente numérico, destinatário CPF |
| `cte-cnpj-alfanumerico.xml` | CTe, emitente e remetente alfanuméricos |
| `nfe-realista-3itens.xml` | NFe no formato real (leiaute 4.00): minificada, 3 itens com ICMS, PIS e COFINS, transporte, pagamento, informações adicionais, assinatura e protocolo. Destinatário CNPJ alfanumérico. 11 KB |
| `nfe-realista-250itens.xml` | O mesmo formato com 250 itens e destinatário CPF. 195 KB, abaixo dos 500 KB que as fontes citam como limite da mensagem |
| `nfe-realista-990itens.xml` | O mesmo formato com 990 itens, o máximo do leiaute. 731 KB: passa dos 500 KB que algumas fontes citam como limite da mensagem, mas cabe em 1 MB e no limite de 2 MiB da API |
| `nfe-emitente-cpf.xml` | NFe de produtor rural pessoa física: o emitente é um CPF (`<emit><CPF>`), a chave de acesso leva o CPF com zeros à esquerda e a série é 920. Destinatário CNPJ |
| `invalido.xml` | XML mal formado (deve ser rejeitado como inválido) |
| `malicioso-xxe.xml` | Contém `DOCTYPE` com entidade externa (deve ser rejeitado, nunca resolvido) |

As duas NFes realistas são **sintéticas**: a estrutura segue o leiaute 4.00, mas a assinatura, o certificado, o protocolo e os dados são inventados (bytes aleatórios em base64, sem validade). Foram geradas por um script determinístico e não passam por uma SEFAZ.

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
