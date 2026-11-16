# ADR 0003 — NFe e CTe implementados, NFSe como ponto de extensão

**Status:** aceita

## Contexto

O enunciado cita três tipos de documento fiscal. NFe e CTe têm schema nacional estável: a mesma família de estrutura (`infNFe` e `infCte`) e a chave de acesso de 44 posições. A NFSe é municipal: cada prefeitura define seu layout, e o identificador não segue a chave de 44 posições.

## Decisão

Implementar NFe e CTe de ponta a ponta e deixar a NFSe como extensão documentada.

Cada tipo é um `IDocumentParser` (`CanParse` e `Parse`). O `DocumentParserResolver` pergunta a cada parser qual aceita o XML, e `AddInfrastructure` registra `NFeParser` e `CTeParser`. O resto do sistema (handlers, persistência, evento) só conhece o resultado do parsing, `ParsedDocument`.

## Alternativas descartadas

- **Só NFe.** Menos código, mas mostraria menos do desenho de extensão.
- **Os três tipos.** Cada layout municipal é um parser novo e uma fonte de casos de borda. O tempo sairia de idempotência, resiliência e testes, que pesam mais.

## Consequências

- O recorte é explícito: um XML de outro tipo é rejeitado com `422` (`Unsupported document type`).
- Acrescentar a NFSe é mais que uma classe de parser. Também exige um novo valor em `DocumentType`, rever a regra que casa o modelo da chave com o tipo (55 para NFe e 57 para CTe, em `FiscalDocument.Create`) e decidir como guardar um identificador que não é a chave de 44 posições.
- Não há validação contra XSD ([ADR 0004](0004-cnpj-e-chave-de-acesso-alfanumericos.md)).
