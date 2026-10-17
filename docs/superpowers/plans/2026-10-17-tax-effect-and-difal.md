# Efeito dos tributos e DIFAL: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** a tabela "Tributos" passa a mostrar o efeito de cada tributo, incluindo as retenções, e o Documento de
Entrada calcula DIFAL e FCP nas compras interestaduais de uso e consumo e de ativo.

**Architecture:** o cálculo do DIFAL é uma função pura no projeto Fiscal (`DifalCalculator`). A aplicação a chama
por linha nos mesmos cinco pontos que já recalculam as retenções. As alíquotas internas vêm de dois cadastros
novos, por UF e por produto × UF. A coluna "Efeito" é calculada no cliente por uma função pura com QUnit, a
partir do CST, da destinação da natureza, do regime da filial e das retenções do cabeçalho.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData v4, xUnit; OpenUI5 TypeScript, QUnit.

**Spec:** `docs/superpowers/specs/2026-10-17-tax-effect-and-difal-design.md` (inclui "Decisões minhas" 1–9).

## Global Constraints

- Só STANDALONE. Em SAPB1 nada muda.
- Identificadores em inglês, texto ao usuário em pt-BR.
- Mesma branch `feature/other-purchases`, nos worktrees `siagro-b1-backend-other-purchases` e
  `siagro-b1-frontend-other-purchases`.
- Commit no formato `tipo(escopo): descrição` (escopos `invoice`, `master-data`, `financial`), com o trailer `DB:`
  quando houver migration. Nunca push.
- Dinheiro com `decimal.Round(x, 2, MidpointRounding.ToEven)`. Alíquotas em percentual: 18 = 18 %.
- DIFAL e FCP não entram em `NetPayable`, `GrandTotal` nem no XML da NF-e, e não geram título.
- Textos exatos dos efeitos: "Crédito na apuração", "Sem crédito", "Crédito CIAP (1/48)", "Diferido",
  "Débito na apuração", "A recolher em guia", "Retido na fonte".

## Review Focus

1. **Linha com produto sem alíquota própria em UF sem cadastro.** A gravação passa com DIFAL 0 e a confirmação
   recusa com a mensagem da spec. Teste na Task 3.
2. **Operação interna** (UF do emitente igual à da filial) **ou emitente "EX".** DIFAL 0 e nenhum campo de DIFAL
   preenchido. Teste na Task 3.
3. **Trocar a natureza da linha de uso e consumo para revenda.** O DIFAL anterior é zerado na gravação seguinte.
   Teste na Task 3.
4. **Interestadual ≥ interna** (por exemplo, a importada a 4 % comparada com a interna de 4 %). DIFAL 0, nunca
   negativo. Teste na Task 1.
5. **Tabela Tributos de um documento sem nenhum CST** (entrada digitada em filial sem motor), mas com retenções. As
   linhas de retenção e de DIFAL aparecem mesmo assim. Teste na Task 5.

---

### Task 1: DifalCalculator puro (Fiscal)

**Files:** Create `SiagroB1.Fiscal/Taxes/DifalCalculator.cs`. Test
`SiagroB1.Fiscal.Tests/Taxes/DifalCalculatorTests.cs`.

**Interfaces:**
- Produces `enum DifalBaseRule { Single = 0, Double = 1 }`, em `SiagroB1.Domain/Enums/DifalBaseRule.cs`.
- Produces `record DifalInput(decimal OperationValue, decimal InterstateRate, decimal OriginIcmsValue, decimal InternalRate, decimal FcpRate, DifalBaseRule BaseRule)`.
- Produces `record DifalResult(decimal Base, decimal InternalRate, decimal InterstateRate, decimal Value, decimal FcpRate, decimal FcpValue)` e `static DifalResult.None`.
- Produces `static DifalResult DifalCalculator.Calculate(DifalInput input)`.
  - `OriginIcmsValue <= 0` → usa `OperationValue × InterstateRate / 100`.
  - `InterstateRate >= InternalRate` → `Value = 0`. O FCP continua sendo calculado sobre a base.

- [ ] **Step 1: testes**
  - `Single_base`: V 1.000, interestadual 12, interna 18, FCP 0 → base 1.000, DIFAL 60,00.
  - `Double_base`: V 1.000, interestadual 12, ICMS de origem 120, interna 18 →
    base = 880 ÷ 0,82 = 1.073,17; DIFAL = 1.073,17 × 18 % − 120 = 73,17.
  - `Double_base_with_fcp`: mesmo caso com FCP 2 → base = 880 ÷ 0,80 = 1.100,00; DIFAL = 198,00 − 120 = 78,00;
    FCP = 22,00.
  - `Origin_icms_missing_uses_interstate_rate`: ICMS de origem 0 dá o mesmo resultado de `Double_base`.
  - `Interstate_not_below_internal_gives_zero`: 18 contra 18 → DIFAL 0.
- [ ] **Step 2:** `dotnet test SiagroB1.Fiscal.Tests --filter FullyQualifiedName~DifalCalculatorTests` → FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** rodar → PASS.
- [ ] **Step 5: commit** `feat(invoice): cálculo puro do DIFAL e do FCP`.

### Task 2: cadastros e campos novos (backend)

**Files:**
- Create: entidades `IcmsStateSetting` (tabela `ICMS_STATE_SETTINGS`) e `IcmsItemStateRate` (tabela
  `ICMS_ITEM_STATE_RATES`), com `DbSet`s.
- Create: enums `PurchasePurpose { ResaleOrInput = 0, UseAndConsumption = 1, FixedAsset = 2 }` e
  `PisCofinsRegime { Cumulative = 0, NonCumulative = 1 }`.
- Modify:
  - `UsageEffect` e `UsageModel`, com `PurchasePurpose`. Ajustar os dois `UsageService` e o `UsageEffectWriter`.
  - `Branch`, com `PisCofinsRegime` (padrão `Cumulative`) e o mapeamento nos serviços e no OData da filial.
  - `PurchaseInvoiceItem`, com `DifalBase`, `DifalInternalRate`, `DifalInterstateRate`, `DifalValue`, `FcpRate`
    e `FcpValue`, usando `DECIMAL(18,2)`/`(7,4) DEFAULT 0`.
- Create: serviços e controllers OData CRUD `IcmsStateSettings` (chave `StateCode`, sigla) e
  `IcmsItemStateRates` (chave composta `ItemCode`+`StateCode`). Se a chave composta não tiver precedente no
  repositório, usar `Guid Key` mais um índice único em `(ItemCode, StateCode)`. Siga o padrão de
  `WithholdingTaxSettings` e `PaymentConditions`.
- Create: migration `AddDifalAndTaxEffect`, para o esquema, e migration `SeedIcmsStateSettingsSp`, só SQL e
  idempotente: SP com 18 % de alíquota interna, FCP 0 e base dupla (1). O `Down` fica vazio.
- Test: `SiagroB1.Application.Tests/Taxes/IcmsStateSettingsTests.cs`.

**Interfaces:**
- Produces os campos e as entidades acima. O serviço que resolve alíquotas é:
  `IcmsInternalRateResolver.ResolveAsync(AppDbContext ctx, string itemCode, string stateCode): Task<(decimal InternalRate, decimal FcpRate, DifalBaseRule BaseRule)?>`.
  A alíquota do produto prevalece. Um `FcpRate` nulo no produto herda o da UF. Sem cadastro de UF, devolve `null`,
  e a regra da base vem sempre da UF.

- [ ] **Step 1: testes**
  - `Item_rate_overrides_state_rate`
  - `Item_null_fcp_inherits_state_fcp`
  - `No_state_setting_returns_null`
  - `State_setting_validation`: alíquota de 0 a 100, UF de 2 letras e mensagens em pt-BR.
  - `Usage_purchase_purpose_roundtrip`
  - `Branch_pis_cofins_regime_roundtrip`
- [ ] **Step 2:** rodar → FAIL.
- [ ] **Step 3:** implementar. Gerar as migrations com `dotnet ef migrations add` e revisar o SQL.
- [ ] **Step 4:** rodar → PASS, depois `--filter "FullyQualifiedName~Usage|FullyQualifiedName~Branch"`.
- [ ] **Step 5: commit** `feat(master-data): ICMS por UF, alíquota por produto, destinação e regime de PIS/COFINS`,
  com os trailers `DB: AddDifalAndTaxEffect` e `DB: SeedIcmsStateSettingsSp`.

### Task 3: DIFAL na linha do Documento de Entrada

**Files:**
- Modify: `PurchaseInvoicesTaxApplyService`, com o método novo `ApplyDifalAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines): Task`.
- Modify: os cinco serviços que já chamam `ApplyWithholdingsAsync` (Create, Update, ItemsCreate, ItemsUpdate e
  ItemsDelete), que passam a chamar `ApplyDifalAsync` logo depois, com o mesmo conjunto de linhas.
- Modify: `PurchaseInvoicesConfirmService`, que recusa na confirmação.
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceDifalTests.cs`.

**Interfaces:**
- Consumes: `DifalCalculator` (Task 1), `IcmsInternalRateResolver` e `PurchasePurpose` (Task 2),
  `InterstateIcmsRate.Resolve(originState, destinationState, goodsOrigin)` (já existe).
- O DIFAL incide quando todas estas condições valem:
  - a filial é STANDALONE;
  - o documento é `InvoiceType == Normal`, `IssuerType == ThirdParty` e `!IsNfeReturn`;
  - a UF do emitente vem do endereço de faturamento, como o motor de tributos já resolve;
  - a UF do emitente não é "EX" e é diferente de `Branch.StateCode`;
  - a natureza da linha tem `PurchasePurpose` igual a `UseAndConsumption` ou `FixedAsset`.
- Fora dessas condições, os seis campos são zerados.
- Alíquota interestadual, pela decisão 4 da spec:
  - com o motor ativo (`IsActiveForAsync`), vale o `IcmsRate` da linha depois do `ApplyAsync`;
  - sem motor, vale o `IcmsRate` maior que 0, ou senão `Resolve` com a origem da mercadoria do produto (0 se não
    houver).
- ICMS de origem: `IcmsValue`.
- Sem alíquota pelo resolver: a gravação passa com zeros. A confirmação recusa com
  `"Cadastre a alíquota interna de ICMS da UF {UF} em ICMS por UF."`, verificando de novo na confirmação.

- [ ] **Step 1: testes**
  - `Interstate_use_and_consumption_computes_double_base_difal`: emitente do PR, filial de SP, V 1.000,
    interestadual 12 → DIFAL 73,17 gravado na linha.
  - `Resale_purpose_has_no_difal`
  - `Same_state_has_no_difal`
  - `Foreign_supplier_has_no_difal`
  - `Changing_line_usage_to_resale_clears_difal` (Review Focus 3), pela atualização de item.
  - `Missing_state_rate_saves_with_zero_and_confirm_refuses` (Review Focus 1).
  - `Item_rate_overrides_state_rate_on_line`
  - `Difal_does_not_change_net_payable_or_grand_total`
  - `Sapb1_mode_never_computes_difal`
- [ ] **Step 2:** rodar → FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** rodar → PASS, e depois a suíte completa `dotnet test SiagroB1.Application.Tests`.
- [ ] **Step 5: commit** `feat(invoice): DIFAL e FCP na linha do documento de entrada`.

### Task 4: telas de cadastro (frontend + menu)

**Files (frontend):**
- Create: tela "ICMS por UF" (`webapp/controller/icmsStateSettings/Main.controller.ts` e a view). É uma lista
  editável com UF (Select de siglas), Alíquota interna %, FCP % e Regra da base (Select "Base única" / "Base
  dupla"), mais a rota no `manifest.json`. Siga o padrão de `withholdingTaxSettings`.
- Modify: tela do Complemento do Produto (`webapp/view/produtos/Complement.view.xml` e o controller), com a seção
  "Alíquota interna por UF": tabela editável com UF, Alíquota interna % e FCP %.
- Modify: `webapp/view/usages/fragments/Form.fragment.xml`, com o campo "Destinação" (Select "Revenda / insumo",
  "Uso e consumo" e "Ativo imobilizado"), visível só para natureza de entrada.
- Modify: tela da filial (`webapp/controller/branchs`), com o campo "Regime de PIS/COFINS" (Select "Cumulativo
  (Lucro Presumido)" / "Não cumulativo (Lucro Real)").
- Backend: migration de menu `AddIcmsStateSettingsMenu` em Cadastros, com o título "ICMS por UF" e
  `StandaloneOnly = 1`, no padrão de `AddWithholdingTaxSettingsMenu`.

- [ ] **Step 1:** implementar. Decimais editáveis usam `sap.ui.model.odata.type.Double`. O Select ligado a OData
  carrega os itens antes do `selectedKey`.
- [ ] **Step 2:** `yarn ts-typecheck` e `yarn lint` passam, e o `dotnet build SiagroB1.sln` também.
- [ ] **Step 3: commits.** Frontend: `feat(master-data): telas de ICMS por UF, destinação e regime de PIS/COFINS`.
  Backend: `feat(master-data): menu do ICMS por UF`, com o trailer `DB: AddIcmsStateSettingsMenu`.

### Task 5: coluna "Efeito" na tabela Tributos (frontend)

**Files:**
- Modify: `webapp/helpers/InvoiceTaxTotalsHelpers.ts`. `TaxTotalRow` ganha `Effect: string`, e entram as linhas de
  DIFAL e FCP do DIFAL, além das seis retenções do cabeçalho.
- Modify: `webapp/fragments/InvoiceTaxTotals.fragment.xml`, com a coluna "Efeito" entre Tributo e Base. A largura
  da tabela vai para cerca de 52rem.
- Modify: os dois `BaseController.refreshDocumentTotal()`, de entrada e de saída, que passam o contexto do efeito.
  `TAX_TOTALS_SELECT` e `PURCHASE_ITEM_SELECT` ganham os campos de DIFAL.
- Modify: o diálogo "Tributos do item" da entrada (`ItemFiscalDialog.fragment.xml`), com DIFAL base, alíquota
  interna, valor, FCP % e FCP valor, todos somente leitura.
- Test: `webapp/test/unit/helpers/InvoiceTaxTotalsHelpers.qunit.ts`.

**Interfaces:**
- Produces:
  ```
  buildTaxTotals(lines: TaxLine[], ctx: {
    direction: 'Incoming' | 'Outgoing';
    isReturn: boolean;
    branchSimples: boolean;
    pisCofinsRegime: 'Cumulative' | 'NonCumulative';
    purposeOf: (line: TaxLine) => 'ResaleOrInput' | 'UseAndConsumption' | 'FixedAsset';
    withholdings?: { base: number; iss: number; irrf: number; inss: number; pis: number; cofins: number; csll: number };
  }): TaxTotalRow[]
  ```
- As linhas que partilham tributo e efeito são somadas. Linhas de ICMS com efeitos diferentes, como uso e consumo
  e revenda no mesmo documento, viram linhas separadas.
- A destinação da natureza vem do mapa de naturezas que a tela já carrega (`/odata/Usages`, `$select` explícito),
  acrescentando `PurchasePurpose`. O regime da filial e o CRT vêm da leitura da filial que a tela já faz para o modo
  NF-e. Se não fizer, buscar `/Branchs('{code}')?$select=TaxRegime,PisCofinsRegime`.

As regras do efeito estão na tabela §3 da spec e nas decisões 3 e 7.

- [ ] **Step 1: testes QUnit**
  - `ICMS resale credit`
  - `ICMS use-and-consumption no credit`
  - `ICMS fixed asset CIAP`
  - `Simples branch ICMS no credit and DIFAL "A recolher em guia"`
  - `PIS CST 50 non-cumulative credit`
  - `PIS CST 50 cumulative no credit`
  - `PIS CST 70 no credit`
  - `Deferred ICMS`
  - `DIFAL and FCP debit`
  - `Withholding rows "Retido na fonte" with WithholdingBase`
  - `Outgoing sale debit`
  - `Outgoing return credit`
  - `Rows without CST still show withholdings and DIFAL` (Review Focus 5)
  - `Zero rows hidden`
- [ ] **Step 2:** `yarn test` só no arquivo, se o runner permitir. Senão, confirmar o vermelho pela checagem de
  tipos.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** `yarn ts-typecheck`, `yarn lint` e o QUnit do arquivo passam.
- [ ] **Step 5: commit** `feat(invoice): coluna Efeito e retenções na tabela de tributos`.

### Task 6: verificação no navegador (controlador)

- [ ] Aplicar as migrations novas nos bancos locais de dev: `CEAGUI_SIAGRO_DEV`, `CEAGUI_SIAGRO_COMMON_DEV` (menu) e
  `LMCEREAIS_SIAGRO_DEV`.
- [ ] Subir a stack `ceagui`.
- [ ] Pelo menu, cadastrar e conferir SP 18 % com base dupla.
- [ ] Marcar a natureza 15 como "Uso e consumo".
- [ ] Lançar uma NF de fornecedor de outra UF, com ICMS destacado de 12 %, sobre R$ 1.000. Conferir o DIFAL de
  73,17 no diálogo do item e a tabela Tributos com os efeitos, as retenções e o DIFAL.
- [ ] Conferir o documento DE000045 (operação interna): sem DIFAL e com a tabela mostrando as retenções.
- [ ] Mandar os prints ao usuário e derrubar a stack pelo PID.
