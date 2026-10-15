# Unidade tributável de exportação e precisão do DANFE

Data: 2026-10-14
Status: aprovado para planejamento
Modo alvo: `STANDALONE` (onde o SiagroB1 emite a NF-e). O cadastro vive nos dois modos, como todo o `ITEM_COMPLEMENTS`.

## Problema

1. **DANFE arredonda o que o XML tem.** O `DanfeReportService` instancia `ConfiguracaoDanfeNfe` com o padrão da Zeus:
   `DecimaisQuantidadeItem = 2` e `DecimaisValorUnitario = 2`. O XML aceita até 4 casas em `qCom`/`qTrib`
   (`TDec_1104v`) e até 10 em `vUnCom`/`vUnTrib` (`TDec_1110v`); o DANFE impresso diverge do XML autorizado.
2. **Grupo tributável sempre igual ao comercial.** `NfeXmlBuilder.BuildItem` copia `uCom`/`qCom`/`vUnCom` para
   `uTrib`/`qTrib`/`vUnTrib`. Na exportação direta ou indireta — inclusive remessa para formação de lote — a soja
   (NCM 12019000) deve sair com a quantidade e o valor tributados em **TON**. Outros produtos têm a mesma regra,
   possivelmente com outra unidade.

## Decisões do usuário

| # | Decisão |
|---|---|
| D1 | A unidade tributável é **configuração do produto**, não regra de negócio por NCM: unidade + fator, qualquer unidade (hoje TON; amanhã outra). |
| D2 | A configuração só se aplica quando o **CFOP do item é de exportação** (lista fixa, abaixo). Fora disso, o tributável continua igual ao comercial. |
| D3 | Quando a unidade tributável difere da comercial, o DANFE imprime **as duas** (comercial em cima, tributável embaixo). |
| D4 | Quantidade (até 4 casas) e valor unitário (até 4 casas) no DANFE com **a maior precisão que o XML tiver, dentro desses limites** (revisto em 15/10/2026: valor unitário limitado a 4 casas no DANFE — com 10 casas o número quebra no meio da coluna do layout da Zeus; o XML mantém até 10). |

## 1. Precisão do DANFE

Em `DanfeReportService.Render`, a partir do `nfeProc` já carregado, sobre **todos** os `det/prod` da nota:

- `DecimaisQuantidadeItem` = maior número de casas **significativas** (sem zeros à direita) entre `qCom` e `qTrib`,
  limitado a `[2, 4]`.
- `DecimaisValorUnitario` = idem entre `vUnCom` e `vUnTrib`, limitado a `[2, 4]` (o XML mantém até 10 casas em `vUnTrib`).
- `ImprimirUnidQtdeValor` = `Ambos` se algum item tem `uTrib ≠ uCom` ou `qTrib ≠ qCom`; senão `Comercial` (como hoje).

O cálculo fica numa classe estática pura (`DanfeItemPrecision`, em `SiagroB1.Reports/Services`), testável sem
FastReport. O `.frx` da Zeus **não** é alterado.

Consequências aceitas:
- A configuração de casas é por nota (o layout não aceita por item): um item com 4 casas faz todos os itens
  da nota saírem com 4.
- No modo `Ambos` o layout imprime as duas linhas em **todos** os itens; numa nota que mistura soja em TON com outro
  produto, o outro aparece com a mesma unidade repetida.
- Vale também para notas já autorizadas, pois o DANFE é sempre gerado do XML gravado. A CC-e (`NFeEvento.frx`)
  fica como está.

## 2. Cadastro: unidade tributável no complemento do produto

`ITEM_COMPLEMENTS` ganha duas colunas anuláveis (migration):

| Coluna | Tipo | Significado |
|---|---|---|
| `TaxableUnitOfMeasureCode` | `VARCHAR(6)` | `uTrib` na exportação (ex.: `TON`). 6 = tamanho máximo do `uTrib`. |
| `TaxableFactor` | `DECIMAL(18,6)` | Quantas unidades da **linha da NF-e** (`uCom`, a UoM física — KG) cabem em uma tributável (ex.: 1000 para KG → TON). **Não** se relaciona com `CommercialUnitOfMeasureCode`/`CommercialFactor` do mesmo complemento (SC/60), que são só de exibição. |

Regras (`ItemComplementService.SetAsync`, mensagens pt-BR):
- Os dois andam juntos: só um preenchido → `"Informe a unidade tributável e o fator juntos."`.
- Fator > 0 → senão `"O fator da unidade tributável deve ser maior que zero."`.
- Unidade com até 6 caracteres, gravada em maiúsculas e sem espaços nas pontas; `""` = não configurado.
- Na action `ItemsSetComplement`, os dois parâmetros novos são opcionais: **ausentes do corpo = inalterados**;
  presentes com `null` = limpar. O fator é `double?` no EDM (ver armadilha do `Parameter<decimal>` na action).
- `ItemComplementDto` e o `GetAsync` passam a expor os dois campos.

Tela `produtos/Complement` (frontend): nova seção "Exportação (NF-e)" com "Unidade tributável" (Input livre,
`maxLength` 6) e "Fator (KG por unidade tributável)", seguindo o padrão dos campos comerciais:
leitura crua do Input e parse pt-BR do fator, e o botão Limpar zera também esses dois.

## 3. Gatilho: CFOP de exportação

Nova classe estática em `SiagroB1.Fiscal/Nfe`: `ExportCfops.Contains(string cfop)`, verdadeira para:

| CFOP | Operação |
|---|---|
| `7xxx` | Exportação direta (qualquer CFOP iniciado por 7) |
| `5501` / `6501` | Remessa com fim específico de exportação (produção do estabelecimento) |
| `5502` / `6502` | Remessa com fim específico de exportação (mercadoria adquirida) |
| `5504` / `6504` | Remessa para formação de lote de exportação (produção do estabelecimento) |
| `5505` / `6505` | Remessa para formação de lote de exportação (mercadoria adquirida) |

A lista é regra fiscal e fica no código; só o produto é configuração do usuário (D1/D2).

## 4. Montagem da NF-e

- `NfeItem` ganha `TaxableUnitOfMeasure`, `TaxableQuantity` e `TaxableUnitPrice`, **opcionais**: nulos = iguais
  aos comerciais (as notas e testes existentes não mudam).
- `NfeIssueInputAssembler` carrega os complementos dos itens da nota (como já faz com o CEST em `ItemCests`) e,
  para cada item com CFOP de exportação **e** complemento configurado, preenche:
  - `TaxableUnitOfMeasure` = unidade do complemento;
  - `TaxableQuantity` = `round(Quantity ÷ TaxableFactor, 4)`;
  - `TaxableUnitPrice` = `TaxableQuantity == 0 ? 0 : round(Total ÷ TaxableQuantity, 10)`.
  Arredondamento `MidpointRounding.AwayFromZero`. A conta fica numa função pura (`NfeTaxableUnit.Convert`, em `SiagroB1.Fiscal/Nfe`) testada à parte.
- `NfeXmlBuilder.BuildItem`: `uTrib = Truncate(item.TaxableUnitOfMeasure ?? item.UnitOfMeasure, 6)`,
  `qTrib = item.TaxableQuantity ?? item.Quantity`, `vUnTrib = item.TaxableUnitPrice ?? item.UnitPrice`.
- `vProd`, `qCom`, `vUnCom` e os tributos não mudam; a validação 629 (`qCom × vUnCom = vProd`) continua sobre o
  grupo comercial.
- A nota de complemento de valor (quantidade 0) cai no ramo `TaxableQuantity == 0` e sai com `qTrib = vUnTrib = 0`.

Não há regra nova de prontidão: produto sem configuração em CFOP de exportação emite como hoje. Se a SEFAZ rejeitar
a unidade pelo NCM, o usuário configura o produto e retransmite.

## 5. Testes

- `ExportCfopsTests`: cada família da tabela, um 7xxx qualquer, CFOP interno (5102, 6102, 5905) falso.
- `NfeTaxableUnitTests`: 30.000,5 KG × fator 1000 → 30,0005 TON; `vUnTrib` com 10 casas; quantidade 0 → 0/0.
- Assembler: item 5504 com complemento → campos preenchidos; item 5102 com complemento → nulos; item 5504 sem
  complemento → nulos.
- `NfeXmlBuilder`: XML com `uTrib=TON`, `qTrib` e `vUnTrib` convertidos; sem os campos, igual ao comercial.
- `DanfeItemPrecisionTests`: casas mínimas 2, `30.0005` → 4, `vUnTrib` com 10 casas → valor unitário limitado a 4, 3 casas se mantêm, limite superior, escolha
  `Ambos`/`Comercial`.
- `ItemComplementServiceTests`: os dois juntos, fator ≤ 0, `""` limpa, ausente mantém; EDM do fator `double`.
- Verificação final pelo caminho do usuário: configurar SOJA (TON/1000) na tela Complemento, emitir em homologação
  uma NF-e 5504 com soja, conferir o XML autorizado e abrir o DANFE pela tela (duas linhas, casas coerentes).

## Fora do escopo

- Validar a unidade tributável contra a tabela de comércio exterior por NCM.
- NF-e de entrada e devoluções de exportação.
- Modo `SAPB1` (a NF-e não é emitida pelo SiagroB1).
- Alterar o layout `.frx` da Zeus.
