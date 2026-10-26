# GAC-1188 Liberação de Pagamento de Contrato — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Per purchase contract, record the Financeiro's payment-release status, each department's release (Sim/Não), and the "Antecipação Liberada pela Diretoria" flag, with a list screen showing them as badges.

**Architecture:** Same shape as the signature status (`PurchaseContractsSetSignatureStatusService` + OData action + change log): fact-type actions valid in any contract status, bypassing the draft-only PATCH. Department releases live in a child table. Authorization uses permission codes via `IUserPermissions.HasAsync` (admin passes automatically) — the pattern of `SalesContractFiscalComplementService`.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData v8, xUnit; OpenUI5 + TypeScript.

**Spec:** `C:\Users\Penalva\.claude\plans\chamado-gac-1188-libera-o-de-humming-gizmo.md`

## Global Constraints
- Code identifiers English; everything the user reads in pt-BR.
- Branch `feature/gac-1188-payment-release` in **both** repos; check branch before each commit; never push, never merge.
- `git add` every new file right after creating it.
- Commit message convention: per each repo's `CLAUDE.md`.
- Status labels (exact): Pendente, Em análise, Documentação incompleta, Aprovado, Pré-aprovado.
- Department labels (exact): Lib. Estoque, Lib. Jurídico, Lib. Fiscal, Lib. Royalties, Lib. Financeiro, Lib. Loja, Lib. Diretoria.
- Flag label (exact): "Antecipação Liberada pela Diretoria".
- Spec deviation (my call): authorization by **permission code** (`HasAsync`) instead of `HasRoleAsync`, because the spec wants admin to pass and `HasRoleAsync` doesn't; also `FakeUserPermissions.HasRoleAsync` always returns false. The "Mara" department is named `Board` / "Lib. Diretoria", consistent with the user's rename.

## Review Focus
- Contract switched from "sobre rodas" to not after being `PreApproved` → status stays, but the next status change follows the new rule (no silent rewrite). Test in Task 2.
- Unmarking Barter after Loja was released → existing Loja row kept; list shows "—" since not barter. Test in Task 3 (release refused, existing row untouched).
- Setting `released=false` on a department never released → no-op, no log row. Test in Task 3.
- Deleting a purchase contract that has release rows → must not fail with FK 547. Test in Task 1.
- User without permission → readable pt-BR message, nothing saved. Tests in Tasks 2 and 3.

---

### Task 1: Domain, table and migrations

**Files:**
- Create: `SiagroB1.Domain/Enums/PaymentReleaseStatus.cs`, `SiagroB1.Domain/Enums/PaymentReleaseDepartment.cs`, `SiagroB1.Domain/Entities/PurchaseContractPaymentRelease.cs`
- Modify: `SiagroB1.Domain/Entities/PurchaseContract.cs`, `AppDbContext` config, `SiagroB1.Domain/Entities/ContractChangeLogFields.cs`, purchase-contract delete service (cascade the new rows)
- Create: AppContext migration `AddPurchaseContractPaymentRelease`; CommonContext migration `SeedPaymentReleasePermissions` (model: `20261007151804_SeedSalesContractFiscalEditPermission.cs`) + menu item (model: `20260914204644_AddPurchaseContractWashoutApprovalMenu.cs`)
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractPaymentReleaseDeleteTests.cs`

**Produces:**
- `enum PaymentReleaseStatus { Pending=0, UnderReview=1, IncompleteDocuments=2, Approved=3, PreApproved=4 }`
- `enum PaymentReleaseDepartment { Stock=0, Legal=1, Fiscal=2, Royalties=3, Financial=4, Store=5, Board=6 }`
- `PurchaseContract`: `PaymentReleaseStatus? PaymentReleaseStatus`, `bool EarlyPaymentAuthorized`, `bool IsBarter`, `bool IsOnWheels`, `ICollection<PurchaseContractPaymentRelease> PaymentReleases`
- `PurchaseContractPaymentRelease { Guid Id; Guid PurchaseContractKey; PaymentReleaseDepartment Department; bool Released; DateTime? ReleasedAt; string? ReleasedBy; }` → table `PURCHASE_CONTRACT_PAYMENT_RELEASES`, unique (PurchaseContractKey, Department), cascade from contract.
- `ContractChangeLogFields`: consts `PaymentReleaseStatus`, `DepartmentRelease`, `EarlyPaymentAuthorized`; `DescribePaymentReleaseStatus(PaymentReleaseStatus?)` (null → "Pendente") and `DescribeDepartment(PaymentReleaseDepartment)` with the exact labels above.
- `PermissionCodes`: `PaymentReleaseStock="PAYMENT_RELEASE_STOCK"`, `..._LEGAL`, `..._FISCAL`, `..._ROYALTIES`, `..._FINANCIAL`, `..._STORE`, `..._BOARD`; `static string ForDepartment(PaymentReleaseDepartment)`.
- Menu item Key `paymentReleases`, Title "Liberação de Pagamento", Icon `sap-icon://folder-blank` per memory only for top-level; use the parent of the purchase-contract menu.

- [ ] **Step 1:** Test `DeletingContract_WithPaymentReleases_RemovesThem` — seed contract + 2 release rows, run the existing delete service, assert both rows gone. Run, expect FAIL (compile).
- [ ] **Step 2:** Add enums, entity, properties, DbContext config, change-log helpers, permission codes; make delete service remove the rows.
- [ ] **Step 3:** `dotnet test --filter FullyQualifiedName~PaymentRelease` → PASS.
- [ ] **Step 4:** Generate both migrations (`ASPNETCORE_ENVIRONMENT=Development`; never the db-migration profile). Permission seed idempotent SQL, one INSERT per code with pt-BR description, linked to ADMIN.
- [ ] **Step 5:** `dotnet build SiagroB1.sln` clean; commit.

### Task 2: Financial status + early payment flag

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsSetPaymentReleaseStatusService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractPaymentReleaseStatusTests.cs` (copy seeding from `PurchaseContractSignatureStatusTests.cs`)

**Produces:** `PurchaseContractsSetPaymentReleaseStatusService(AppDbContext, PurchaseContractsChangeLogService, IUserPermissions)` with
- `Task SetStatusAsync(Guid key, PaymentReleaseStatus? status, string userName)`
- `Task SetEarlyPaymentAuthorizedAsync(Guid key, bool authorized, string userName)`

Both require `PAYMENT_RELEASE_FINANCIAL` else `DefaultException("Você não tem permissão para alterar a liberação financeira do contrato.")`; `NotFoundException` when missing; no-op if unchanged; set UpdatedAt/By; change-log row with pt-BR labels; any contract status allowed.
Rules: `PreApproved` when `!IsOnWheels` → `DefaultException("Pré-aprovado só se aplica a contrato sobre rodas.")`; `Approved` when `IsOnWheels` → `DefaultException("Contrato sobre rodas deve ser marcado como Pré-aprovado.")`.

- [ ] **Step 1:** Tests: `SetStatus_WithoutPermission_Throws_AndKeepsValue`; `SetStatus_AnyContractStatus_Saves` (Theory Finished/Canceled/Approved/Draft); `SetStatus_LogsPtBrLabels` (null→UnderReview logs "Pendente"→"Em análise"); `SetStatus_Same_IsNoOp`; `PreApproved_NotOnWheels_Throws`; `Approved_OnWheels_Throws`; `PreApproved_OnWheels_Saves`; `OnWheelsChangedAfterPreApproved_KeepsStatus_NextChangeFollowsRule`; `EarlyPayment_Toggle_Logs`.
- [ ] **Step 2:** Run, expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run, expect PASS; commit.

### Task 3: Department release

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsSetDepartmentReleaseService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractDepartmentReleaseTests.cs`

**Produces:** `PurchaseContractsSetDepartmentReleaseService(AppDbContext, PurchaseContractsChangeLogService, IUserPermissions)` with `Task ExecuteAsync(Guid key, PaymentReleaseDepartment department, bool released, string userName)`.
Requires `PermissionCodes.ForDepartment(department)` else `DefaultException($"Você não tem permissão para a {label}.")`. `Store` when `!IsBarter` → `DefaultException("Lib. Loja só se aplica a contrato com barter.")`. Upserts the row (released=true sets ReleasedAt/By; false clears them). Change log value "Lib. X: Sim/Não".

- [ ] **Step 1:** Tests: `Release_WithoutPermission_Throws`; `Release_CreatesRow_WithUserAndDate`; `Unrelease_ClearsRow`; `Unrelease_NeverReleased_IsNoOp_NoLog`; `Store_NotBarter_Throws_ExistingRowUntouched`; `Store_Barter_Saves`; `Release_FinishedContract_Saves`.
- [ ] **Step 2:** Run, expect FAIL. **Step 3:** Implement. **Step 4:** PASS; commit.

### Task 4: OData actions + exposure

**Files:**
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (next to `PurchaseContractsSetSignatureStatus`, ~line 1409), `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Create: `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsSetPaymentReleaseStatusController.cs`, `...SetEarlyPaymentAuthorizedController.cs`, `...SetDepartmentReleaseController.cs` (model: `PurchaseContractsSetSignatureStatusController.cs`)

**Produces:** actions
- `PurchaseContractsSetPaymentReleaseStatus(Key: Guid, PaymentReleaseStatus: string?)` — string parsed with `Enum.TryParse`, empty = null.
- `PurchaseContractsSetEarlyPaymentAuthorized(Key: Guid, Authorized: bool)`
- `PurchaseContractsSetDepartmentRelease(Key: Guid, Department: string, Released: bool)`
- `PaymentReleases` navigation `$expand`-able on `PurchaseContracts`; `IsBarter`/`IsOnWheels` editable through the existing PATCH (draft-only rule unchanged).

- [ ] **Step 1:** Implement; `dotnet build` clean.
- [ ] **Step 2:** Run Web locally (profile `yktb`), check `$metadata` lists the 3 actions and the nav; POST each action once via curl through the Gateway; expect 200 / pt-BR error for invalid enum. Commit.

### Task 5: Frontend — contract flags + release dialog (`siagro-b1-frontend`)

**Files:**
- Modify: purchase contract form view (checkboxes "Barter", "Sobre rodas"), `view/purchaseContracts/Detail.view.xml` (button "Liberação de Pagamento" + header ObjectStatus), `controller/purchaseContracts/PurchaseContractsBaseController.ts` (handlers next to signature ones, lines ~218-261), `model/ServerRoutes.ts`, `model/formatter.ts` (`formatPaymentReleaseStatus`, `statePaymentReleaseStatus`: Pending None, UnderReview Information, IncompleteDocuments Error, Approved/PreApproved Success; change-log field labels)
- Create: `view/purchaseContracts/fragments/PurchaseContractPaymentReleaseDialog.fragment.xml` — Select of status (only valid options per IsOnWheels), CheckBox "Antecipação Liberada pela Diretoria", one Switch per department (Loja hidden unless IsBarter) showing who/when, and the signature status shown beside Lib. Jurídico. Each change calls its action immediately and refreshes; backend errors shown via MessageBox.

- [ ] **Step 1:** Implement. **Step 2:** `yarn ts-typecheck` and `yarn lint` clean. Commit.

### Task 6: Frontend — "Liberação de Pagamento" list

**Files:**
- Create: `view/paymentReleases/Main.view.xml`, `controller/paymentReleases/Main.controller.ts`, filter bar fragment
- Modify: `manifest.json` (route/target `paymentReleases`)

Table bound to `PurchaseContracts` with `$expand=PaymentReleases`; columns Contrato, Emissão, Fornecedor, Cond. Pagto., Status Financeiro, then one Sim (Success)/Não (Error) ObjectStatus per department ("—" for Loja when not barter), icon column for the early-payment flag. Filters: MultiComboBox status (pattern of `PurchaseContractFilterbar.fragment.xml` + `anyOfFilter`), fornecedor, emissão. Excel export as in the contract list. Row press → purchase contract Detail.

- [ ] **Step 1:** Implement. **Step 2:** typecheck + lint. **Step 3:** Commit.

### Task 7: End-to-end verification
- [ ] Full `dotnet test` (report pre-existing failures separately), `dotnet build`.
- [ ] Stack local (`yktb` + `yarn start:dev`): home → menu → Liberação de Pagamento; open a contract, mark Barter/Sobre rodas, change status, release departments, toggle early payment; confirm badges, change log, Loja rule, Pré-aprovado rule; user without permission gets the message. Kill processes by PID.
- [ ] Final commits in both repos; report SHAs. No push, no merge.
