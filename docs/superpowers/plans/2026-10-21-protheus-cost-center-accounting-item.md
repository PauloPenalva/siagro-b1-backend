# Centro de Custo + Item Contábil + rateio CTJ (modelo Protheus) — Rework Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Substituir as "5 dimensões genéricas" já implementadas na branch
`feature/transport-managerial-accounting` pela estrutura do SIGACTB:

- Centro de Custo (CTT) e Item Contábil (CTD), com hierarquia sintético/analítico, bloqueio e
  vigência;
- obrigatoriedade **por conta contábil** (CT1_ACCUST/CT1_CCOBRG e equivalentes do Item);
- rateio pré-configurado CTJ, com linhas de CC + Item + %.

**Architecture:** Este plano **retrabalha** a branch. Nada dela foi mergeado nem aplicado a outro
banco além das cópias `_DIMS`. Por isso, as duas migrations da primeira versão são **apagadas e
regeradas** em vez de compensadas. O motor existente continua a base e é renomeado:

| Hoje | Passa a ser |
|---|---|
| `DimensionCodeValidator` | `AccountingAssignmentValidator` |
| `DimensionDefaultsResolver` | `AccountingAssignmentDefaultsResolver` |
| `DimensionRequirementGuard` | `AccountingAssignmentGuard` |

A interface `IDimensionFields` vira `IAccountingAssignment`, com campos fixos.

**Tech Stack:** .NET 10, EF Core, OData v4, xUnit; OpenUI5 + TS, QUnit.

**Spec:** `docs/superpowers/specs/2026-10-21-transport-and-managerial-accounting-design.md`, seção A
("Entidades contábeis no modelo do Protheus").

## Global Constraints

- Apenas STANDALONE. Os serviços são registrados em `AddApplicationServices` (os dois modos) e
  não fazem nada quando `!ErpMode.IsStandalone(configuration)`. Os menus novos têm
  `StandaloneOnly = true`.
- Identificadores em inglês; telas e mensagens em pt-BR (`DefaultException`).
- Classe de Valor e entidades 05–09 **não existem**. Não criar colunas para elas.
- Códigos de CC e Item: `VARCHAR(20)`, gravados com trim e em maiúsculas. A comparação com o
  cadastro é case-insensitive, porque podem existir códigos legados em minúsculas.
- `CostCenterClass`/`AccountingItemClass` = enum `AccountingEntityClass { Synthetic = 1, Analytic = 2 }`.
  Os centros existentes migram como `Analytic`, sem pai.
- Só código **analítico, ativo e vigente na data do documento** é aceito como **valor novo**.
  Um valor inalterado de documento antigo continua aceito, porque o validador recebe `previous`.
- `DistributionRuleCode` e o par (`CostCenterCode`, `AccountingItemCode`) são exclusivos no mesmo
  registro: "Informe o rateio ou o centro de custo/item contábil, não os dois."
- A obrigatoriedade é por conta, nas flags de `LedgerAccount`. A guarda roda só na confirmação
  feita pelo usuário (parâmetro `enforceAccountingAssignment`) e na criação de título manual,
  adiantamento e lançamento na conta. Fluxos automáticos não são bloqueados.
- `COMPANY_SETTINGS`, `ACCOUNTING_DIMENSIONS`, `Dim1Code`…`Dim5Code`, `DimensionNumber` e
  `DimensionRole` **somem**.
- `SalesInvoiceItem`: a coluna que no main se chama `CostCenterCode` **continua** `CostCenterCode`.
  Como a migration é regerada a partir do main, não há renomeação.
- Toda migration leva o trailer `DB:`. Novo arquivo recebe `git add`. Commits no branch atual,
  sem push nem merge.

## Review Focus

1. **Documento antigo com CC que depois foi bloqueado ou virou sintético** continua editável. Só
   um valor novo é recusado. (Task 3)
2. **Rateio na linha com conta que exige CC**: passa se todas as linhas do rateio têm CC e falha se
   alguma linha não tem. (Task 3)
3. **Hierarquia**: pai analítico, ciclo, ou excluir/tornar analítico um CC que tem filhos são
   recusados. (Task 2)
4. **Vigência**: CC com `ValidTo` antes da data do documento é recusado como valor novo. (Task 3)
5. **Trocar o rateio por CC/Item na mesma linha**: o modo anterior é limpo, e nunca os dois ficam
   gravados juntos. (Tasks 3 e 7)

---

### Task 1: Esquema — trocar as dimensões genéricas por CC + Item Contábil

**Files:**
- Delete: `SiagroB1.Migrations/AppContext/20261021165655_AddAccountingDimensions*.cs` and `SiagroB1.Migrations/CommonContext/20261021181502_AddAccountingDimensionMenus*.cs`. Restore both ModelSnapshots to their `main` versions (`git checkout main -- <snapshot>`), then re-apply the model.
- Delete: `Entities/AccountingDimension.cs`, `Enums/DimensionRole.cs`, `Entities/CompanySettings.cs`.
- Rename: `Interfaces/IDimensionFields.cs` → `IAccountingAssignment.cs`, with `string? CostCenterCode; string? AccountingItemCode; string? DistributionRuleCode; string? LedgerAccountCode`. Drop `GetDim`/`SetDim` and add `AccountingAssignmentSnapshot` (Copy/From/FromOriginal), replacing `DimensionSnapshot`.
- Create: `Entities/AccountingItem.cs` (`ACCOUNTING_ITEMS`), `Enums/AccountingEntityClass.cs`.
- Modify:
  - `Entities/CostCenter.cs`: remove `DimensionNumber`; add `Class`, `ParentCode` (VARCHAR 20), `ValidFrom`, `ValidTo` (date, null); `Code` becomes VARCHAR(20).
  - `LedgerAccount.cs`: add 4 bools — `AcceptsCostCenter` and `AcceptsAccountingItem` (default true), `RequiresCostCenter` and `RequiresAccountingItem` (default false).
  - `DistributionRule.cs`: remove `DimensionNumber`.
  - `DistributionRuleLine.cs`: `CostCenterCode` becomes nullable and gains `AccountingItemCode` (nullable).
  - The 7 document entities implement `IAccountingAssignment`. `SalesInvoiceItem` keeps its existing `CostCenterCode` and `LedgerAccountCode`.
  - `BusinessPartner.cs`: `DefaultDimensionCode` is replaced by `DefaultCostCenterCode` and `DefaultAccountingItemCode`.
  - `AppDbContext.cs`.
- Create migrations:
  - `AddCostCenterAccountingItems` (App): one migration from `main`, with defaults: `Class = 2`, flags `1/1/0/0`.
  - `AddAccountingModuleMenus` (Common): new **root module "Contabilidade"** (user request 21/10), following the pattern of `20260907224917_AddFinancialMenus.cs` (root = `ParentKey null`).
    - Key `accounting`, icon `sap-icon://accounting-document-verification`, Order 8, i.e. right after Financeiro (7). Shift the other roots with a relative `UPDATE MENU_ITEMS SET [Order]=[Order]+1 WHERE ParentKey IS NULL AND [Order] >= 8 AND [Key] <> 'accounting'`.
    - The root is **not** `StandaloneOnly`, because Plano de Contas and Centro de Custo still show read-only from SAP in SAPB1.
    - Children, in order:
      1. `ledgerAccounts` "Plano de Contas": **moved** from `registers` with `UPDATE ... SET ParentKey='accounting', [Order]=1`.
      2. `costCenters` "Centro de Custo": **moved**, Order 2.
      3. `accountingItems` "Item Contábil": new, `StandaloneOnly`.
      4. `distributionRules` "Rateios Pré-configurados": new, `StandaloneOnly`.
    - Do not reorder the remaining `registers` children; gaps are fine.
    - `ROLE_MENUS`: insert `accounting` for **every role that already has `ledgerAccounts` or `costCenters`**, via an `INSERT ... SELECT DISTINCT NEWID(), RoleCode, 'accounting' FROM ROLE_MENUS WHERE MenuItemKey IN (...) AND NOT EXISTS (...)`. Insert the two new children for ADMIN.
    - `Down` reverses: move the two items back to `registers` with their original orders (read them from earlier migrations: `ledgerAccounts` 12, `costCenters` 11), delete the new rows, and shift the roots back.
    - Remove the first version's `AddAccountingDimensionMenus`; it is deleted together with the App migration.
- Test: rewrite `Dimensions/DimensionSchemaTests.cs` → `Accounting/AccountingSchemaTests.cs`.

- [ ] **Step 1:** Write `AccountingSchemaTests`:
  - `Persists_assignment_on_all_documents`: the 4 fields on each of the 7 entities round-trip.
  - `Rule_line_accepts_cost_center_and_item`.
  - `Ledger_account_flags_default_accept_true_require_false`.
- [ ] **Step 2:** Apply the entity changes and delete the obsolete files. The build will fail across the services; that is expected. Fix compile errors in services and tests only by deleting code that belongs to removed concepts: `AccountingDimensionService`/Controller, `CompanySettingsService`/Controller and their tests, and `DimensionCodeQueryService` (replaced in Task 2). Leave a `// TODO Task N` stub in services that Tasks 2–5 rewrite, so the solution compiles.
- [ ] **Step 3:** Regenerate both migrations and check them by hand: no drop of `SALES_INVOICES_ITEMS.CostCenterCode`; `Down` is symmetric; the menu shift is relative. Run `dotnet ef migrations has-pending-model-changes` for both contexts and expect no changes.
- [ ] **Step 4:** Run `dotnet build SiagroB1.sln` and `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Accounting"`. Expected: PASS.
- [ ] **Step 5:** Commit `refactor(master-data): centro de custo e item contábil no lugar das dimensões genéricas`, with the trailers `DB: AddCostCenterAccountingItems` and `DB: AddAccountingModuleMenus`.

### Task 2: Cadastros CTT, CTD e flags da conta

**Files:**
- Modify: `CostCenterService.cs`, `Services/SAP/CostCenterService.cs` (project `Class = Analytic`), `CostCenterModel.cs`; `LedgerAccountService.cs`, `LedgerAccountModel.cs`, `Services/SAP/LedgerAccountService.cs` (flags default).
- Create: `Services/Accounting/AccountingItemService.cs` + `IAccountingItemService` + `AccountingItemModel` + `AccountingItemsController` (OData `AccountingItems`; GET/POST/PATCH/DELETE); `Services/Accounting/AccountingEntityRules.cs` (hierarchy rules shared by CC and Item).
- Test: `Accounting/CostCenterHierarchyTests.cs`, `AccountingItemServiceTests.cs`, `LedgerAccountFlagsTests.cs`.

**Interfaces:**
- `AccountingEntityRules.ValidateAsync<T>(IQueryable<T> set, string code, AccountingEntityClass cls, string? parentCode, Func<T,string?> parentOf, Func<T,AccountingEntityClass> classOf)`. The messages:
  - "O superior {pai} não existe."
  - "O superior precisa ser sintético."
  - "O código não pode ser superior dele mesmo." (also used for a cycle anywhere up the chain)
  - "Não é possível tornar analítico: {code} tem subordinados."
  - delete with children: "Não é possível excluir: {code} tem subordinados."
- `ValidTo` before `ValidFrom` → "A vigência final deve ser posterior à inicial."
- Code normalization: Trim + upper, VARCHAR(20).
- Flags: a `Requires*` set to true forces `Accepts*` true; `Accepts*` false with `Requires*` true → "A conta não pode exigir e não aceitar centro de custo." (same for item).

- [ ] **Step 1:** Tests for each message above, plus create/update/delete round-trip of AccountingItem, plus normalization `" cam01 "` → `"CAM01"`.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement. Register the services in `AddApplicationServices` (AccountingItem) and keep CostCenter/LedgerAccount in their existing mode-specific places.
- [ ] **Step 4:** Run `dotnet test --filter "FullyQualifiedName~Accounting|FullyQualifiedName~CostCenter|FullyQualifiedName~LedgerAccount"`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(master-data): centro de custo e item contábil com hierarquia, bloqueio e vigência`.

### Task 3: Rateio CTJ, validador, preenchimento padrão e guarda pela conta

**Files:**
- Modify/rename in `Services/Dimensions/` → `Services/Accounting/`:
  - `DistributionRuleService.cs`;
  - `DistributionRuleExpander.cs`;
  - `DimensionCodeValidator.cs` → `AccountingAssignmentValidator.cs`;
  - `DimensionDefaultsResolver.cs` → `AccountingAssignmentDefaultsResolver.cs`;
  - `DimensionRequirementGuard.cs` → `AccountingAssignmentGuard.cs`.
  Update `DistributionRulesController`/`DistributionRuleVersionsActionsController`. The SaveVersion action takes three parallel arrays: `LineCostCenterCodes`, `LineAccountingItemCodes`, `LinePercentages`.
- Test: port and adapt every `Dimensions/*Tests.cs` into `Accounting/` (rules, expander, validator, resolver, guard). Delete the dimension-specific cases.

**Interfaces:**
- `DistributionRuleExpander.ExpandAsync(string ruleCode, DateTime documentDate) : Task<IReadOnlyList<(string? CostCenterCode, string? AccountingItemCode, decimal Percentage)>>`. Versioning rules are unchanged: the new version must start after today, current versions are immutable, and the last version cannot be deleted.
- Rule lines:
  - at least one of CC or Item per line;
  - both must be analytic and active;
  - the pair must be unique: "O par centro de custo/item contábil se repete na versão.";
  - the sum must be 100.
- `AccountingAssignmentValidator.NormalizeAndValidateAsync(IAccountingAssignment target, IAccountingAssignment? previous, DateTime documentDate)`. For each changed field:
  - CC/Item must exist, be analytic, active and in force at `documentDate`: "O centro de custo {c} não existe, é sintético, está bloqueado ou fora da vigência." (and the same for "O item contábil {c} …");
  - the rule must exist and be active;
  - the ledger account must be active with `AllowsPosting`;
  - the CC/Item vs rule exclusivity check.
- `AccountingAssignmentDefaultsResolver.ApplyAsync(IAccountingAssignment target, IAccountingAssignment? header, string? partnerCode, int? usageCode, DateTime documentDate)` fills empty fields: header → partner (`DefaultCostCenterCode`, `DefaultAccountingItemCode`) → usage (CC, Item, Rule, Account). Rules:
  - never fill CC/Item when the target already has a rule, and vice versa;
  - skip invalid candidates silently.
- `AccountingAssignmentGuard.EnsureAsync(IEnumerable<IAccountingAssignment> rows, Func<int,string>? rowLabel, DateTime documentDate)`. For each row with `LedgerAccountCode`, load the account flags, then:
  - Requires CC: satisfied by `CostCenterCode`, or by a rule whose every line (in the version in force) has CC.
  - Accepts CC = false: fail if `CostCenterCode` is set or any rule line has CC.
  - The same checks apply to Item.
  - Messages: "A conta {conta} exige centro de custo (linha {n})." / "A conta {conta} não aceita centro de custo (linha {n})." / same with "item contábil". Without `rowLabel`, drop " (linha …)".
  - Rows without an account are skipped. In SAPB1 the guard does nothing.

- [ ] **Step 1:** Port the existing tests to the new API, then add:
  - Review Focus 1 (old blocked/synthetic value unchanged → accepted);
  - Review Focus 2 (rule satisfies or fails "exige CC");
  - Review Focus 4 (`ValidTo` before the document date → rejected);
  - Review Focus 5 (exclusivity);
  - `Account_not_accepting_item_rejects_item`;
  - `Row_without_account_is_skipped`.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement. Delete `CompanySettings` usage from the guard.
- [ ] **Step 4:** Run `dotnet test --filter "FullyQualifiedName~Accounting"`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(financial): rateio pré-configurado e amarração pela conta contábil`.

### Task 4: Ligar nos documentos, natureza e parceiro

**Files:**
- Modify the services wired in the first version to use the new API and the `documentDate` of each document:
  - SalesInvoices: Create, Update, ItemsCreate, ItemsUpdate;
  - PurchaseInvoices: Create, Update (including SyncItems), ItemsCreate, ItemsUpdate;
  - Financials: ManualCreate/ManualUpdate (+ `ManualFinancialDocumentChanges`: CC, Item, Rule, Account), FinancialAdvancesCreate (both routes), FinancialAccountEntriesCreate;
  - `UsageService`/`UsageModel` and `UsageTaxationMapper`;
  - `BusinessPartnerService`/`BusinessPartnerModel`;
  - the Confirm services (`enforceDimensionRequirements` → `enforceAccountingAssignment`; the controllers pass true);
  - the OData action params of `FinancialAdvancesCreate`/`FinancialAccountEntriesCreate`: replace Dim1..5 with `CostCenterCode`, `AccountingItemCode`, `DistributionRuleCode`.
- Test: port `InvoiceDimensionsTests`, `FinancialDimensionsTests`, `UsagePartnerDimensionDefaultsTests` and the guard placement tests into `Accounting/`.

- [ ] **Step 1:** Port the tests. Keep the guard placement tests: user confirm blocks; system flows (billing, return, refusal, NF-e result handlers) do not.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the full suite `dotnet test SiagroB1.Application.Tests`. Expected: 0 failed. `grep -rn "Dim[1-5]Code\|DimensionNumber\|DimensionRole\|CompanySettings" --include=*.cs SiagroB1.*` excluding the main-era migrations must be empty.
- [ ] **Step 5:** Commit `feat(invoice): centro de custo, item contábil e rateio nos documentos`.

### Task 5: Frontend — remover dimensões, cadastros novos

**Files (frontend worktree `siagro-b1-frontend-transport`):**
- Delete:
  - `view/accountingDimensions/*` + controller;
  - `view/companySettings/*` + controller;
  - `helpers/DimensionHelpers.ts` + its QUnit;
  - the `dims` model loading in `SessionService`/`Component`;
  - `DimensionCodesSelectDialog`;
  - the manifest routes `accountingDimensions` and `companySettings`.
- Modify `view/costCenters/*`: columns and fields Classe (Sintético/Analítico), Superior (value help over synthetic centers), Vigência de/até, and a hierarchical display in the list (indent by level or a "Superior" column). Remove the Dimensão column and Select and the `onDimensionChange` handler.
- Create `view/accountingItems/{Main,Add,Edit}` (same layout as costCenters), routes `accountingItems`/`accounting-items`.
- Modify the ledger account form: 4 checkboxes "Aceita centro de custo", "Exige centro de custo", "Aceita item contábil", "Exige item contábil".
- Modify `view/distributionRules/*`: header without Dimensão; lines with Centro de Custo + Item Contábil value helps (analytic and active only) + %; the save action sends the 3 parallel arrays.

- [ ] **Step 1:** Implement. Add QUnit for any pure helper (hierarchy indent, rule line validation).
- [ ] **Step 2:** Run `yarn ts-typecheck`, `yarn lint` and the unit tests. Expected: clean and PASS.
- [ ] **Step 3:** Commit `refactor(master-data): telas de centro de custo, item contábil e rateio no modelo Protheus`.

### Task 6: Frontend — campos nos documentos

**Files:**
- Replace `DimensionFields.fragment.xml`/`DimensionsForm.fragment.xml` with `AccountingAssignmentFields.fragment.xml`: Centro de Custo, Item Contábil, Rateio, Conta Contábil. Value helps cover CC/Item (analytic and active), rules (active) and accounts (active with `AllowsPosting`). Choosing a rule clears CC/Item and vice versa. Use the same host files as the first version:
  - sales/purchase header and item dialog;
  - manual title, title detail;
  - account entry and advance dialogs;
  - usage "Contabilização padrão".
- Partner: two fields, "Centro de custo padrão" and "Item contábil padrão".
- Every `create()`/initial payload lists the 4 fields (null). The account entry and advance actions send the new param names.
- Visibility: shown only in STANDALONE. Use the existing session ERP flag (the one `loadDimensions` used via `isSapMode()`) exposed as e.g. `session>/standalone`.

- [ ] **Step 1:** Implement. `grep -rn "Dim[1-5]Code\|dims>" webapp` must be empty.
- [ ] **Step 2:** Run the gates. Expected: clean and PASS.
- [ ] **Step 3:** Commit `feat(invoice): centro de custo, item contábil e rateio nos documentos`.

### Task 7: E2E na cópia + revisão final

- [ ] **Step 1 (controller):** Drop and recreate `CEAGUI_SIAGRO_DEV_DIMS`/`_COMMON_DEV_DIMS` from the current `CEAGUI_SIAGRO_DEV`/`_COMMON_DEV` (remove company 0002 in the copy). Apply the two new migrations to the copies only.
- [ ] **Step 2 (controller, browser from the home):**
  - CC: sintético OPER → analíticos OPER01/OFIC01.
  - Item contábil CAM01/CAM02.
  - Conta that requires CC and accepts Item.
  - Rateio DIESEL 60/40 with OPER01+CAM01 / OPER01+CAM02.
  - Manual title with that account and no CC → error; with the rule → saves.
  - Purchase invoice line with CC + Item → confirm.
  - Rule ↔ CC exclusivity in the UI.
  - Usage default fills a new line.
  - Account entry with item.
  Fix loop through the frontend/backend implementers.
- [ ] **Step 3:** Final whole-branch review (most capable model), ONE fix wave, scoped re-review.
- [ ] **Step 4:** Stop the stack by PID. Update the memory `transport-managerial-accounting-design.md`.
