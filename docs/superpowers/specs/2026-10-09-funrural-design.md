# Funrural no Documento de Entrada — desenho

Data: 2026-10-09 · Repos: `siagro-b1-backend` + `siagro-b1-frontend` · Branch: `feature/funrural`

## 1. Contexto e objetivo

Na compra de produção rural a empresa compradora retém a contribuição do produtor (Funrural),
composta de três parcelas — **INSS**, **RAT/GILRAT** e **SENAR** — cada uma com sua alíquota.
Hoje o SiagroB1 não calcula nada: o operador digita à mão, em "Informações do contribuinte"
(`TaxPayerComments` → `infCpl`), um texto como "Contribuição Social denominada Funrural 1,5% = R$ 22,50"
(spec `2026-10-05-nfe-purchase-invoice-issuance-design.md`, que deixou o cálculo fora do escopo).

Objetivo: calcular o Funrural por linha no Documento de Entrada, mostrar o valor retido e o
**líquido a pagar** ao produtor, montar o texto da NF-e automaticamente e estornar o mesmo
Funrural na devolução de compra.

Alíquotas vigentes informadas pelo usuário:

| Produtor | INSS | RAT | SENAR | Total |
|---|---|---|---|---|
| Pessoa física | 1,32% | 0,11% | 0,20% | 1,63% |
| Pessoa jurídica | 1,98% (INSS + RAT combinados) | — | 0,25% | 2,23% |
| Decisão judicial | — | — | só o SENAR | 0,20% / 0,25% |

Decisões do usuário (brainstorming 09/10/2026):

- **Informar e descontar**: o documento mostra o Funrural retido e o líquido a pagar; o
  financeiro usará o líquido quando os títulos passarem a nascer da nota (Fase 2 do financeiro).
- **Saída = devolução de compra**: quem comprou com Funrural (ou só SENAR) devolve com o mesmo
  tratamento, para zerar a arrecadação. A devolução de compra é um registro de `PURCHASE_INVOICES`
  (`InvoiceType.Return` + `IsNfeReturn`), não do Documento de Saída — por isso **o Documento de
  Saída de venda não muda**.
- **A regra mora na natureza de operação**, com as **alíquotas na própria natureza** (mesmo molde
  de ICMS e PIS/COFINS). PF, PJ e decisão judicial são naturezas distintas; o operador escolhe a
  natureza da linha.
- O campo `PurchaseContract.FunruralType` (Livre/Bruto) fica **apenas informativo** — não entra
  no cálculo.

## 2. Natureza de operação

`Usage` (tabela `USAGES`) ganha três colunas anuláveis, `DECIMAL(7,4)` como as demais alíquotas:

| Coluna | Rótulo na tela |
|---|---|
| `FunruralInssRate` | INSS (%) |
| `FunruralRatRate` | RAT/GILRAT (%) |
| `FunruralSenarRate` | SENAR (%) |

- Vazio ou zero = parcela não retida. Natureza com as três vazias/zeradas = sem Funrural.
- Validação no salvar: cada alíquota entre 0 e 100 ("A alíquota de {parcela} do Funrural deve
  estar entre 0 e 100.").
- `UsageModel` (e o DTO lido por `IUsage`) carrega as três.
- Só existe no modo STANDALONE (a natureza SAPB1 `OUSG` não tem tributação — e o cálculo nem roda,
  §4).

Exemplos de cadastro: "Compra produtor rural PF" 1,32 / 0,11 / 0,20; "Compra produtor rural PJ"
1,98 / 0 / 0,25; "Compra produtor rural PF – decisão judicial" 0 / 0 / 0,20; remessa, insumo e
serviço sem alíquotas. O número do processo judicial vai no `DefaultAdditionalInfo` da natureza.

## 3. Linha e cabeçalho do Documento de Entrada

### 3.1 Fotografia na linha

`PurchaseInvoiceItem` (tabela `PURCHASE_INVOICES_ITEMS`) ganha, `decimal` não anuláveis no mesmo
padrão de `IcmsBase`/`IcmsRate`/`IcmsValue`:

| Coluna | Tipo |
|---|---|
| `FunruralBase` | `DECIMAL(18,2) DEFAULT 0` |
| `FunruralInssRate`, `FunruralRatRate`, `FunruralSenarRate` | `DECIMAL(7,4) DEFAULT 0` |
| `FunruralInssValue`, `FunruralRatValue`, `FunruralSenarValue` | `DECIMAL(18,2) DEFAULT 0` |

E calculado, `[NotMapped]`: `FunruralValue = INSS + RAT + SENAR`.

Os campos **não** entram em `INfeTaxedLine` (a linha de saída não os tem); ficam só em
`PurchaseInvoiceItem`.

### 3.2 Cálculo

Função pura nova em `SiagroB1.Fiscal/Taxes/FunruralCalculator.cs`:

```
FunruralResult Calculate(decimal amount, decimal? inssRate, decimal? ratRate, decimal? senarRate)
```

- Base = `amount` (o `GrandTotal` da linha — a mesma base do ICMS, spec 2026-10-05 D3).
- Cada parcela = `Round(base × alíquota / 100)`, 2 casas, `MidpointRounding.AwayFromZero` (mesma
  regra do `TaxCalculator`).
- Alíquota nula/zero → parcela 0 e alíquota gravada como 0.
- Todas zeradas → base, alíquotas e valores gravados como **0** (linha sem Funrural; a base também
  fica 0, para "sem Funrural" ser inequívoco).

### 3.3 Totais do cabeçalho

`PurchaseInvoice` ganha, `[NotMapped]` como os demais totais:

- `TotalFunrural` = soma de `FunruralValue` das linhas;
- `NetPayable` = `GrandTotal − TotalFunrural`.

Por serem `[NotMapped]`, somem do EDM: precisam ser readicionados com `AddProperty` em
`SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, junto de `TotalInvoiceTaxes` (o mesmo vale para
`PurchaseInvoiceItem.FunruralValue`).

**Não muda**: `vNF`, `pag`, `cobr/fat` e `TotalDocumentValue` — a retenção não é tributo da NF-e;
o total da nota continua o valor bruto da mercadoria.

## 4. Onde o cálculo roda

Em `PurchaseInvoicesTaxApplyService.ApplyAsync`, logo depois de `TaxLineCalculator.Apply`, com
todas as condições que já existem: modo STANDALONE + filial emissora (`TaxCalculationGate`),
documento Pendente, NF-e não Autorizada/Cancelada/Em processamento, e só nos tipos que o serviço
já calcula:

- **Entrada própria Normal** e **entrada de terceiro Normal** (nota do produtor): alíquotas da
  natureza da linha.
- **Devolução de compra** (`IsOwnNfeReturn`, inclusive a devolução de entrada de terceiro): §5.
- **Devolução do cliente** (terceiro + Devolução): intocada, como hoje.

Proteção dos valores: os campos do §3.1 são calculados — o PATCH/criação da linha não os grava a
partir do corpo da requisição, e documento fora de Pendente mantém o que está gravado (mesmo
tratamento dos campos de `TaxSnapshot`, aplicado aos campos novos da linha de entrada).

Documentos existentes não são recalculados nem preenchidos retroativamente: ficam com Funrural
0 até serem salvos de novo enquanto Pendentes.

## 5. Devolução de compra

A linha da devolução **copia as alíquotas da linha de origem** (`PurchaseInvoiceItemOriginKey`,
já carregada por `LoadOriginItemsAsync`) e recalcula os valores sobre o `GrandTotal` da linha
devolvida. A natureza de devolução não define o Funrural — assim quem comprou só com SENAR devolve
só com SENAR, mesmo que o cadastro da natureza mude depois.

Origem sem fotografia de Funrural (entrada anterior a esta feature) → devolução sem Funrural.

`NetPayable` na devolução = valor a receber do produtor (total − Funrural estornado).

## 6. Texto na NF-e

`NfeIssueInputAssembler.AdditionalInfo` (sobrecarga de entrada) acrescenta o texto do Funrural
**depois dos textos padrão das naturezas e antes de `TaxPayerComments`**, quando
`TotalFunrural > 0`. As alíquotas e valores são agregados por documento (somas das linhas);
se as linhas tiverem alíquotas diferentes, cada combinação distinta gera seu trecho.

- Com INSS ou RAT:
  `Contribuição Social denominada Funrural 1,63% = R$ 1.630,00 (INSS 1,32% R$ 1.320,00; RAT 0,11% R$ 110,00; SENAR 0,20% R$ 200,00)`
  (parcelas zeradas omitidas do parêntese).
- Só SENAR: `SENAR 0,20% = R$ 200,00`.
- Na devolução de compra, o mesmo texto prefixado por `Estorno - `.

Formatação pt-BR (vírgula decimal, milhar com ponto). O comentário do formulário de entrada que
orienta digitar o Funrural (`purchaseInvoices/fragments/Form.fragment.xml`) é atualizado: o texto
agora é automático.

## 7. Frontend

- **Naturezas de operação**: grupo "Funrural" no formulário com os três campos (Double, como as
  demais alíquotas — memória "Decimal editável usa Double").
- **Diálogo fiscal da linha** (`purchaseInvoices/fragments/ItemFiscalDialog`): base, alíquotas e
  valores de INSS, RAT e SENAR, somente leitura.
- **Seção "Tributos"** (`helpers/InvoiceTaxTotalsHelpers.ts`): linhas "Funrural - INSS",
  "Funrural - RAT" e "Funrural - SENAR" só no Documento de Entrada (o helper recebe a lista de
  linhas por documento; a saída não muda).
- **Seção "Totais"** (`helpers/InvoiceChargeTotalsHelpers.ts` / `InvoiceChargeTotals.fragment.xml`):
  "Funrural retido" e "Líquido a pagar" no Documento de Entrada.

## 8. Migrations

Uma migration `AddFunruralRates`: três colunas anuláveis em `USAGES` e sete `DEFAULT 0` em
`PURCHASE_INVOICES_ITEMS`, sem dados. Nenhuma migration de menu.

## 9. Testes

Backend (xUnit):

1. `SiagroB1.Fiscal.Tests` — `FunruralCalculator`: PF (1,32/0,11/0,20), PJ (1,98/0/0,25), só
   SENAR, sem Funrural (zeros), arredondamento de meio centavo.
2. `SiagroB1.Application.Tests` — `PurchaseInvoicesTaxApplyService`: entrada própria e de terceiro
   gravam o Funrural da natureza; natureza sem alíquotas grava zeros; documento não Pendente não
   recalcula.
3. Devolução: copia as alíquotas da origem mesmo com a natureza alterada depois; origem sem
   Funrural → devolução sem Funrural.
4. Totais: `TotalFunrural` e `NetPayable` do cabeçalho.
5. NF-e: texto completo, só SENAR, combinações distintas e prefixo de estorno no `infCpl`.
6. Validação de alíquota fora de 0–100 na natureza.

Frontend: QUnit dos helpers de Tributos e Totais com Funrural.

Ponta a ponta pela tela (CEAGUI dev): natureza PF → entrada própria com Funrural → conferir
Tributos/Totais e o `infCpl` do XML → devolver → conferir o estorno.

## 10. Fora do escopo

- Funrural no Documento de Saída de venda.
- Geração de títulos financeiros a partir da nota (Fase 2 do financeiro consumirá `NetPayable`).
- Apuração e recolhimento (GPS, DCTFWeb, EFD-Reinf R-2055).
- Uso de `PurchaseContract.FunruralType` ou da tabela `TAXES` no cálculo.
- Modo SAPB1.
