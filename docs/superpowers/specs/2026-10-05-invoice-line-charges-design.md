# Frete, seguro, desconto e outras despesas na linha dos documentos de entrada e saída — design

Data: 05/10/2026. Branch `feature/invoice-line-charges` nos dois repos, criado de `main` (que já tem a emissão
STANDALONE, entrada própria/terceiro, devoluções, o quadro "Tributos" e o valor declarado do terceiro).

## 1. O pedido

"No documento de entrada e saída, são os campos valor do frete, valor do seguro, desconto e outras despesas no item
de linha do documento, e mostrar o totalizador desses campos + total dos itens no detalhe. O total geral do documento
é total dos itens + (frete + seguro + despesas) − desconto."

Hoje nenhum dos quatro valores existe: todo total é Σ(quantidade × preço), o XML da NF-e grava `vFrete`, `vSeg`,
`vDesc` e `vOutro` sempre 0 e `vNF` = Σ produtos.

Sucesso: o operador informa os quatro valores em cada linha; o Detail mostra os totais e o total geral; na filial
que emite pelo Siagro, os tributos e a NF-e saem coerentes com esses valores (a SEFAZ confere os totais).

## 2. Decisões (do usuário, em 05/10)

| # | Decisão | Motivo |
|---|---|---|
| D1 | Quatro valores **por linha**: frete, seguro, desconto, outras despesas. | Pedido. Espelha o `det/prod` da NF-e. |
| D2 | **Total geral** = itens + frete + seguro + outras despesas − desconto (linha e documento). | Pedido. |
| D3 | **Entram na base dos tributos**: base da linha = total geral da linha (antes da redução de base do ICMS), para ICMS, PIS/COFINS e IBS/CBS. | "Como manda a legislação" (LC 87/96 art. 13; LC 214) para valores cobrados na própria nota; desconto incondicional fora da base. |
| D4 | **Devolução proporcional**: na devolução ("Devolver", venda e compra) cada valor vem da linha de origem × (quantidade devolvida ÷ quantidade original), em centavos; editável enquanto Pendente. | Espelha a nota de origem, como a tributação. |

Decisões do desenho (aprovado em 05/10):

| # | Decisão |
|---|---|
| R1 | Os campos valem em **todas as filiais** (padrão 0 — nada muda onde não são preenchidos). O efeito nos tributos e na NF-e só existe onde o motor calcula (filial com a regra ativa). |
| R2 | Validação: nenhum valor negativo; o desconto não passa de itens + frete + seguro + outras despesas da linha. |
| R3 | Entrada de terceiro: importar o XML lê os quatro valores de cada `det`; o **valor declarado** (`TotalDocumentValue`) passa a ser o **total geral** — no terceiro Normal da filial ativa (regra de 05/10) e na emissão própria. |
| R4 | Telas: quatro colunas na grade de itens; coluna "Total" = total geral da linha; seção **"Totais"** no Detail; a barra da grade e as listas mostram o total geral. |

## 3. Escopo

Dentro: `SalesInvoiceItem` e `PurchaseInvoiceItem` (dados, gravação, travas, cópias), motor de tributos, montagem da
NF-e (itens, totais, pagamento/fatura), leitura do XML do fornecedor, devoluções, valor declarado, telas (grade, Detail,
listas).

Fora: rateio de um frete do cabeçalho entre as linhas (o operador informa por linha); frete de transporte da carga
(`FreightPrice`/`FreightCostStandard`, outra coisa); IPI e ICMS-ST (não tratados pelo Siagro); relatórios FastReport
próprios (o DANFE vem do XML e já mostra os valores).

## 4. Modelo de dados

### 4.1 Linhas — colunas novas (as duas tabelas)

`SALES_INVOICES_ITEMS` e `PURCHASE_INVOICES_ITEMS`:

| Coluna | Tipo |
|---|---|
| `FreightValue` | `DECIMAL(18,2) NOT NULL DEFAULT 0` |
| `InsuranceValue` | `DECIMAL(18,2) NOT NULL DEFAULT 0` |
| `DiscountValue` | `DECIMAL(18,2) NOT NULL DEFAULT 0` |
| `OtherExpensesValue` | `DECIMAL(18,2) NOT NULL DEFAULT 0` |

Migration única `AddInvoiceLineCharges` (`AppDbContext`); linhas existentes ficam com 0.

### 4.2 Calculados (não gravados)

- Linha: `Total` continua = quantidade × preço (é o `vProd`); novo `[NotMapped] GrandTotal` = `Total + FreightValue +
  InsuranceValue + OtherExpensesValue − DiscountValue`.
- Cabeçalho (`SalesInvoice`, `PurchaseInvoice`): `TotalInvoiceItems` continua = Σ `Total`; novos `[NotMapped]`
  `TotalFreight`, `TotalInsurance`, `TotalOtherExpenses`, `TotalDiscount` e `GrandTotal` (Σ `GrandTotal` das linhas).
  Expostos no EDM com `AddProperty`, como o `TotalInvoiceItems`.

### 4.3 Travas e cópias

- As travas de NF-e emitida/em processamento (`SalesInvoiceNfeLock`, `SalesInvoiceNfeReturnLock`,
  `PurchaseInvoiceNfeLock`) passam a proteger os quatro campos como protegem quantidade e preço.
- Todo serviço que copia linha campo a campo (alteração do documento, alteração de linha, cópia de documento) passa a
  copiar os quatro campos.

## 5. Validação (R2)

Na gravação da linha (inclusão/alteração de documento e de linha, nas duas entidades): valor negativo recusa
"Item {código}: frete, seguro, desconto e outras despesas não podem ser negativos."; desconto acima de itens + frete +
seguro + outras despesas recusa "Item {código}: o desconto passa do valor da linha.".

## 6. Tributos (D3)

O valor passado ao motor como base (`TaxLineRequest.Amount`) deixa de ser `item.Total` e passa a ser
`item.GrandTotal`, nos dois serviços de aplicação (`SalesInvoicesTaxApplyService`, `PurchaseInvoicesTaxApplyService`).
O motor (`TaxCalculator`) não muda: a redução de base do ICMS, a exclusão do ICMS da base do PIS/COFINS e as deduções
da base do IBS/CBS continuam aplicadas sobre esse valor. A conferência da devolução (`NfeReturnConference`) compara
CST/alíquotas/reduções e não muda.

## 7. NF-e

- `INfeTaxedLine` e `NfeItem` ganham os quatro valores; o montador (`NfeIssueInputAssembler`) os leva da linha.
- `det/prod`: `vFrete`, `vSeg`, `vDesc`, `vOutro` da linha (omitidos quando 0, como o leiaute permite).
- `ICMSTot`: `vFrete`, `vSeg`, `vDesc`, `vOutro` = somas; `vNF` = `vProd` + `vFrete` + `vSeg` + `vOutro` − `vDesc`.
- Pagamento: o total que o montador passa ao cálculo das parcelas, a guarda "pagamento = total" do construtor do XML,
  `cobr/fat` (`vOrig`, `vLiq`) e `detPag/vPag` usam o total geral.
- Valor declarado da emissão própria: `PurchaseInvoicesNfeIssueService` grava o total geral.

## 8. Entrada de terceiro (R3)

- `SupplierNfeXmlReader` lê `vFrete`, `vSeg`, `vDesc`, `vOutro` de cada `det/prod` (ausente = 0); o rascunho da
  importação (`PurchaseInvoiceDraftItemDto`) e o rascunho no frontend levam os quatro valores para a linha.
- `PurchaseInvoiceDeclaredTotal` passa a somar o `GrandTotal` das linhas.

## 9. Devoluções (D4)

`SalesInvoiceReturnFactory` e `PurchaseInvoicesNfeReturnCreateService`: cada um dos quatro valores = valor da linha
de origem × quantidade devolvida ÷ quantidade da linha de origem, arredondado em 2 casas (`MidpointRounding.AwayFromZero`);
devolvendo a quantidade inteira, os valores vêm exatamente iguais. Ficam editáveis enquanto a devolução está Pendente.

## 10. Telas (frontend)

- **Grade de itens** (entrada e saída): colunas "Frete", "Seguro", "Desconto", "Outras despesas" (tipo `Double`, 2
  casas — campo de escrita, nunca `Decimal`), editáveis quando a linha é editável; coluna "Total" mostra o total geral
  da linha.
- **Payloads/linhas novas**: os quatro campos existem desde o `create()` (valor 0), no rascunho, no "Incluir Item" e
  nas linhas da saída criadas pela tela de faturamento.
- **Seção "Totais"** no Detail (as duas telas), tabela como a de Tributos: Total dos itens, Frete, Seguro, Outras
  despesas, (−) Desconto, **Total geral**. Calculada no cliente junto do "Total dos itens", com os valores vindos das
  linhas.
- A barra da grade mostra o total geral ("Total do Documento" na saída, "Total dos itens" vira "Total geral" na entrada);
  as listas (Main) mostram `GrandTotal`.

## 11. Testes e verificação

Backend: `GrandTotal` de linha e documento; validações (negativo, desconto acima da linha); base de cada tributo com
os quatro valores (ICMS com redução, PIS/COFINS, IBS/CBS); XML (`det/prod` e `ICMSTot` com os quatro, `vNF`, `vPag`,
`fat`); leitura do XML do fornecedor; devolução proporcional (parcial e total) nas duas; valor declarado (terceiro e
emissão própria); travas; EDM das propriedades novas; filial sem a regra só guarda os valores.

Frontend: QUnit da soma dos totais (linhas com e sem os quatro valores, valores em string).

Navegador (CEAGUI, homologação): venda com frete e desconto emitida e autorizada (SEFAZ confere `vNF`); entrada
importada de um XML fictício com frete; devolução parcial com valores proporcionais.

## 12. Riscos

- **Rejeição de totais na SEFAZ** (ex.: 531/532/610 — soma de itens ≠ totais): coberta pelos testes do XML e pela
  emissão real em homologação.
- **Arredondamento** da devolução proporcional: a soma das devoluções parciais pode diferir de 1 centavo do original;
  aceito (a última devolução não é ajustada).
- **Documentos já emitidos** não mudam (valores 0, XML guardado).
