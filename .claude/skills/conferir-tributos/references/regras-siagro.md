# Regras de tributação do Siagro B1 (motor STANDALONE)

Referência para conferir se um valor no XML/na tela é o que o **motor do Siagro** deveria ter
produzido. Caminhos relativos a `siagro-b1-backend/`. Confirme no código antes de afirmar algo —
as linhas citadas podem ter mudado. A especificação de origem é
`docs/superpowers/specs/2026-10-01-nfe-standalone-taxation-design.md` (§6, fórmulas).

## Índice
1. Quando o motor calcula
2. De onde vêm as regras (cadastros)
3. Fórmulas por tributo
4. Funrural, retenções e DIFAL
5. Como o XML é montado
6. Onde ler os dados para auditoria
7. Testes que já cobrem o motor
8. Armadilhas

## 1. Quando o motor calcula

- Só com `Erp=STANDALONE` **e** `Branch.IssuesNfe` ligado (`SiagroB1.Application/Services/Taxes/TaxCalculationGate.cs`).
  Em modo SAPB1 (ex.: Yokotobi) ou com a chave desligada (ex.: MH Agro) os valores são os
  digitados — não há motor para comparar, só coerência interna do documento.
- Compras (`PurchaseInvoicesTaxApplyService`): própria Normal/Complemento de valor, devolução
  própria, e entrada de terceiros Normal/Complemento — **terceiros são recalculados pela
  natureza de operação; os valores do XML do fornecedor não são copiados**. Contranota não
  passa pelo motor. Nota com `NfeStatus` Autorizada/Cancelada/Processando não é recalculada.
- Vendas (`SalesInvoicesTaxApplyService`): Normal ou devolução própria, status nulo/Pendente.
- Devoluções conferem com a linha de origem (`NfeReturnConference`): CST/alíquota/redução/
  diferimento/cBenef de ICMS e todo o IBS/CBS. **PIS/COFINS não são conferidos** na devolução.

## 2. De onde vêm as regras

| Cadastro (tabela) | O que define |
|---|---|
| Natureza de operação `USAGES` (`Domain/Entities/Usage.cs`) | CFOPs dentro/fora do estado; bloco ICMS dentro do estado (CST/CSOSN, alíquota, redução, diferimento, cBenef); bloco fora do estado (sem alíquota — ela é automática); PIS/COFINS CST+alíquota; `ExcludeIcmsFromPisCofinsBase` (Tema 69); IBS/CBS CST, cClassTrib, reduções; Funrural/RAT/Senar; retenções; natureza de devolução |
| `USAGE_EFFECTS` | Finalidade da compra (revenda/insumo, uso e consumo, ativo) → DIFAL |
| `IBS_CBS_RATES` | Alíquotas por vigência (`StartDate <= data`). 2026: CBS 0,9 · IBS UF 0,1 · IBS Mun 0 |
| `ICMS_STATE_SETTINGS` / `ICMS_ITEM_STATE_RATES` | Alíquota interna e FCP da UF (item tem precedência) — usados no DIFAL |
| `ITEMS` | NCM, origem da mercadoria (obrigatórios), CEST |
| `BRANCHS` | UF, regime tributário (CSOSN só para Simples/MEI), IE (sem IE → sem DIFAL) |

Alíquota interestadual é fixa em código (`SiagroB1.Fiscal/Taxes/InterstateIcmsRate.cs`):
4% origem importada (1, 2, 3, 8); 7% de PR/SC/RS/SP/RJ/MG para N/NE/CO/ES; 12% nos demais.

Códigos suportados (`SiagroB1.Fiscal/Taxes/FiscalCodes.cs`): ICMS 00/20/51/90 tributados,
40/41/50 sem tributo; CSOSN 900 tributado, 102/103/300/400 sem; PIS/COFINS saída 01/02/04–09/49,
entrada 50–56/60–67/70–75/98/99; IBS/CBS 000/200/400/410.
**Não suportados pelo motor:** ST/MVA (10/30/60/70, CSOSN 201/202/203/500), CSOSN 101, IPI,
vICMSDeson, FCP no XML, PIS/COFINS CST 03. Se aparecerem num XML emitido pelo Siagro, é anomalia.

Validação de cadastro (`UsageTaxationValidator`): CST 20 exige redução; CST 51 exige
diferimento e diferimento só com 51; IBS/CBS 200 exige redução; cClassTrib com 6 dígitos;
PIS/COFINS 01/02 exigem alíquota.

## 3. Fórmulas (`SiagroB1.Fiscal/Taxes/TaxCalculator.cs`)

Arredondamento: `Math.Round(x, 2, MidpointRounding.AwayFromZero)` **a cada etapa**, e o valor
arredondado alimenta a etapa seguinte. Ordem: ICMS → PIS/COFINS → IBS/CBS. Cálculo **por linha**.

```
Total       = Round(Qtd × Preço, 2, ToEven)            (+ ComplementAmount na compra)
GrandTotal  = Total + Frete + Seguro + OutrasDesp − Desconto      ← "Amount" do motor
ICMS base   = Round(Amount × (1 − red/100))            (redução só em 20/51/90/900)
ICMS        = Round(base × aliq/100)
  CST 51:  vICMSOp = Round(base × aliq/100)
           vICMSDif = Round(vICMSOp × pDif/100)
           vICMS   = vICMSOp − vICMSDif
PIS/COFINS base = Round(Amount − (Tema69 ? vICMS : 0))   ← vICMS LÍQUIDO (após diferimento)
PIS/COFINS      = Round(base × aliq/100)                 (CST 04–09 → base 0 e valor 0)
IBS/CBS base    = Round(Amount − vICMS − vPIS − vCOFINS)
CBS, IBS UF, IBS Mun = Round(base × aliq/100 × (1 − red/100))
pAliqEfet (XML) = Round(aliq × (1 − red/100), 4, AwayFromZero)
CST IBS/CBS 400/410 ou natureza sem CST → zero
```

IPI e ST **nunca** entram em base. Frete/seguro/desconto/outras são arredondados a centavos
(AwayFromZero), não podem ser negativos, e desconto > Total é recusado (rejeição 483) —
`SiagroB1.Application/Services/InvoiceLineChargeRules.cs`.

UFs: venda = filial → cliente; compra = fornecedor → filial; CT-e = início/fim da prestação;
devolução própria usa as UFs da operação original e as alíquotas IBS/CBS da data do documento de origem.

## 4. Funrural, retenções e DIFAL

**Funrural** (só linhas de compra; `PurchaseInvoiceFunrural` + `SiagroB1.Fiscal/Taxes/FunruralCalculator.cs`):
base = `GrandTotal` da linha; cada parcela (INSS, RAT, Senar) = `Round(base × aliq/100, 2, AwayFromZero)`.
Alíquotas da natureza de operação (devolução usa as da linha de origem). Cabeçalho:
`TotalFunrural`, `TotalWithheld` = ISS+IRRF+INSS+PIS+COFINS+CSLL,
`NetPayable = GrandTotal − TotalFunrural − TotalWithheld`. Vai para o XML apenas como texto
em `infCpl` (`FunruralText`).

**Retenções** (cabeçalho, só "outras compras"; `PurchaseInvoiceWithholdings`): base = Σ GrandTotal,
valor = `Round(base × aliq/100, 2, ToEven)` (**ToEven aqui**). Todas as linhas com as mesmas
alíquotas. `WithholdingsManual` mantém valores digitados (só valida).

**DIFAL/FCP** (`PurchaseInvoiceDifal` + `SiagroB1.Fiscal/Taxes/DifalCalculator.cs`) — só quando:
STANDALONE, terceiros, Normal, não devolução, não CT-e, filial com IE, UF fornecedor ≠ UF filial
e ≠ EX, finalidade uso e consumo ou ativo.
```
inter       = IcmsRate da linha se > 0, senão tabela interestadual
icmsOrigem  = IcmsValue da linha se > 0, senão OperationValue × inter/100
base única  = Round(V)
base dupla  = Round((V − icmsOrigem) / (1 − (interna + fcp)/100))
DIFAL       = inter >= interna ? 0 : max(0, Round(base × interna/100 − icmsOrigem))
FCP         = Round(base × fcp/100)
```
Arredondamento **ToEven** aqui. DIFAL/FCP são informativos: não entram em GrandTotal,
NetPayable nem no XML.

## 5. Montagem do XML (`SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs`)

O builder **lê o snapshot gravado na linha** (`TaxSnapshot`) — não recalcula. Logo, divergência
entre XML e tela é bug de mapeamento; divergência entre XML e fórmula é bug do motor ou do cadastro.

- `vProd = Total`; vFrete/vSeg/vDesc/vOutro omitidos quando 0; `cBenef` da natureza; `indTot=1`.
- ICMS: 00→ICMS00; 20→ICMS20 (pRedBC); 40/41/50→ICMS40 (só orig+CST); 51→ICMS51 com
  **pRedBC sempre** (mesmo 0 — rejeição 929), vICMSOp, pDif, vICMSDif, vICMS; 90/900→ICMS90/ICMSSN900.
- PIS/COFINS: 01/02→Aliq; 04–09→NT; 49 e 50–99→Outr.
- IBSCBS: CST e cClassTrib sempre; gIBSCBS só para 000/200; gRed quando redução > 0.
- ICMSTot: vBC/vICMS somam só linhas 00/20/51/90/900; vPIS/vCOFINS excluem CST 04–09;
  `vNF = Σ GrandTotal`; vICMSDeson/vFCP/vBCST/vST/vIPI/vII fixos em 0. Pagamento deve somar vNF.
- IBSCBSTot: presente se alguma linha tiver CST IBS/CBS; soma linhas 000/200.

## 6. Onde ler os dados

| O quê | Onde |
|---|---|
| Cabeçalho | `SALES_INVOICES` / `PURCHASE_INVOICES` (`ChaveNFe`, `NfeStatus`, `BranchCode`, `CardCode`, `InvoiceDate`/`IssueDate`) |
| Linhas (snapshot) | `SALES_INVOICES_ITEMS` / `PURCHASE_INVOICES_ITEMS`: `CstIcms`, `IcmsBase/Rate/Value/BaseReduction/Deferral/OperationValue/DeferredValue/BenefitCode`, `CstPis`, `PisBase/Rate/Value`, `Cofins*`, `IbsCbsCst/ClassCode/Base`, `CbsRate/Reduction/Value`, `IbsState*/IbsMunicipal*`, `IbsRateReduction`, `Freight/Insurance/Discount/OtherExpensesValue`, `Cfop`, `Ncm`, `GoodsOrigin`, `UsageCode`; compra também `Funrural*`, `Difal*`, `Fcp*` |
| XML autorizado | `SALES_INVOICE_NFE_XMLS` / `PURCHASE_INVOICE_NFE_XMLS`, coluna `Xml`, `Kind=2` (procNFe; 1 assinado, 3 denegado, 4 cancelamento, 5 inutilização) — pegue o mais recente |
| XML via API | `GET /odata/SalesInvoicesNfeXml(Key=…)` e `GET /odata/PurchaseInvoicesNfeXml(Key=…)` (pelo Gateway, autenticado) |
| XML do fornecedor | `PURCHASE_INVOICES.XmlData` (varbinary) + `XmlFileName` |
| OData | `SalesInvoices`, `SalesInvoicesItems`, `PurchaseInvoices`, `PurchaseInvoicesItems`; regras em `Usages`, `IbsCbsRates`, `IcmsStateSettings`, `IcmsItemStateRates`, `Branchs` |
| XMLs de homologação | `../nfe-homologacao-testes/`, `../dados-ceagui-efiscloud*/emitidas-siagro-homologacao/` |

As credenciais do banco estão em `appsettings.json` — use-as só para leitura e nunca as copie
para relatórios ou respostas.

## 7. Testes existentes

`dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~TaxCalculatorTests"` — caso de
referência SP→BA, valor 60000, PIS 1,65 / COFINS 7,6 com Tema 69: vICMS 4200; base PIS 55800
(920,70); COFINS 4240,80; base IBS/CBS 50638,50; CBS 455,75; IBS UF 50,64. Também:
`DifalCalculatorTests`, `FunruralCalculatorTests`, `InterstateIcmsRateTests`,
`UsageTaxationValidatorTests`, `Nfe/NfeXmlBuilderTests`. Em `SiagroB1.Application.Tests`:
`PurchaseInvoicesTaxApplyTests`, `SalesInvoicesTaxApplyServiceTests`,
`SalesInvoicesNfeReturnTaxApplyTests`, `PurchaseInvoiceDifalTests`,
`PurchaseInvoiceWithholdingsTests`, `PurchaseInvoiceFunruralTests`, `InvoiceLineChargeRulesTests`.

Quando achar uma divergência que é bug do motor, o caminho natural é um teste novo nessas
classes reproduzindo o caso (TDD), não um ajuste manual no banco.

## 8. Armadilhas

- Arredondamento misto: AwayFromZero no motor, Funrural e encargos de linha; **ToEven** no
  `Total` da linha, no DIFAL e nas retenções. Diferença de 1 centavo pode ser só isso.
- Tema 69 subtrai o ICMS **líquido**: com CST 51 e diferimento 100%, a base de PIS/COFINS é o valor cheio.
- ICMSTot.vBC não soma linhas 40/41/50 — não é erro.
- Nota de terceiros: o Siagro recalcula pela natureza, então os valores na tela podem
  divergir do XML do fornecedor **por design**. Divergência aí é sinal de natureza de operação
  mal escolhida/cadastrada, ou de erro no XML do fornecedor — não de bug do motor.
