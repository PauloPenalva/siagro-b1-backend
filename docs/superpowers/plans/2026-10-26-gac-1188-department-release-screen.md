# GAC-1188 (parte 2) — Tela de liberação por departamento, motivo e histórico

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Each department release requires a free-text "Motivo" and keeps a full history (Sim/Não, motivo, user, date/time). Department releases move to a dedicated screen where each user sees/edits only the departments their permissions allow; the contract dialog becomes read-only for releases.

**Spec (user decisions, 26/10/2026):** full history (every mark/unmark is a row); Motivo ALWAYS required (mark and unmark); separate screen gated by department permission (Jurídico user only sees Lib. Jurídico, etc.; admin sees all); one contract per action (no batch); contract dialog: department releases read-only + history, Financeiro status and antecipação stay editable there.

Builds on branch `feature/gac-1188-payment-release` (both repos). Existing: `PurchaseContractsSetDepartmentReleaseService.ExecuteAsync(Guid key, PaymentReleaseDepartment department, bool released, string userName)`, entity `PurchaseContractPaymentRelease`, permissions `PAYMENT_RELEASE_*` (`PermissionCodes.ForDepartment`), action `PurchaseContractsSetDepartmentRelease {Key, Department, Released}`, screen `paymentReleases`, dialog `PurchaseContractPaymentReleaseDialog.fragment.xml`, frontend `SessionService.hasPermission(code)` (admin bypass).

## Global Constraints
- Identifiers English; user-facing pt-BR. Never push/merge; check branch before commits; `git add` new files; commit convention of each repo + `Refs: GAC-1188` + `DB:` trailer when a migration is included; trailer `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Department labels exact: Lib. Estoque, Lib. Jurídico, Lib. Fiscal, Lib. Royalties, Lib. Financeiro, Lib. Loja, Lib. Diretoria.
- Motivo: required, trimmed, max 500 chars; error "Informe o motivo da liberação." when empty.
- Do NOT apply migrations to any database (user will decide).

---

### Task A: Backend — motivo, histórico, papéis

**Produces:**
- Entity `PurchaseContractPaymentReleaseHistory { Guid Id; Guid PurchaseContractKey; PaymentReleaseDepartment Department; bool Released; string Reason; DateTime ChangedAt; string ChangedBy; }` → table `PURCHASE_CONTRACT_PAYMENT_RELEASE_HISTORY`, index (PurchaseContractKey, Department, ChangedAt), cascade from contract; nav `PurchaseContract.PaymentReleaseHistory`. Delete service removes them (same pattern as PaymentReleases).
- `PurchaseContractPaymentRelease` gains `string? Reason` (reason of the current state).
- Service signature becomes `ExecuteAsync(Guid key, PaymentReleaseDepartment department, bool released, string? reason, string userName)`: reason required (after permission check, before no-op); no-op (unchanged) writes nothing; otherwise updates the current row (Released, ReleasedAt/By — on unmark keep ReleasedAt/By null as today — and Reason) and appends a history row (ChangedAt = DateTime.Now, ChangedBy = user). Change-log value includes the motivo: "Lib. Fiscal: Sim (motivo: <texto>)".
- Action `PurchaseContractsSetDepartmentRelease` gains `Reason: string` (nullable in EDM; controller passes through; service validates). `PaymentReleaseHistory` expandable on PurchaseContracts.
- CommonContext migration `AddPaymentReleaseDepartmentRoles` (idempotent SQL like `20261026155720_SeedPaymentReleasePermissions`): 7 roles `PAYMENT_RELEASE_STOCK` … `PAYMENT_RELEASE_BOARD` with pt-BR names "Liberação Estoque", "Liberação Jurídico", "Liberação Fiscal", "Liberação Royalties", "Liberação Financeiro", "Liberação Loja", "Liberação Diretoria"; each linked (ROLE_PERMISSIONS) to its own permission; ROLE_MENUS for each role to the new menu item `departmentReleases` and to the parent `purchases` if parent menus need explicit grant (check how ROLE_MENUS handles parents). Menu item `departmentReleases`, Title "Liberação por Departamento", parent `purchases`, icon copied from siblings, next Order; ADMIN also gets it. Check the ROLES table columns in existing seeds before writing SQL.
- AppContext migration `AddPaymentReleaseHistory`.

- [ ] Tests (TDD) in `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractDepartmentReleaseTests.cs`: update existing calls to pass a reason; add `Release_WithoutReason_Throws_NothingSaved` (null, "", "   "), `Release_ReasonTooLong_Throws`, `Release_AppendsHistory_WithReasonUserAndDate`, `ReleaseThenUnrelease_KeepsTwoHistoryRows`, `Unchanged_IsNoOp_NoHistory`, `Release_SetsCurrentReason`, delete test covers history rows.
- [ ] Implement; `dotnet build SiagroB1.sln`; `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContract"` green; generate migrations (ASPNETCORE_ENVIRONMENT=Development, never db-migration profile, no `database update`). Commit.

### Task B: Frontend — tela "Liberação por Departamento" + diálogo só leitura

- New route/view `departmentReleases` (pattern `department-releases`), mirroring `view/paymentReleases/Main.view.xml` + controller (filters, sort, Excel optional — not required).
- Columns: Contrato, Emissão, Fornecedor, Status Financeiro, then ONLY the department columns the user may see: `SessionService.hasPermission(PAYMENT_RELEASE_<DEPT>)` (admin sees all). Hide columns via a view-model flag per department. Badge Sim/Não ("—" Loja non-barter) as in the existing list.
- Single selection. Toolbar buttons "Liberar" and "Retirar liberação" open a dialog: Select "Departamento" (only permitted departments; Loja only if contract IsBarter; preselect when only one), TextArea "Motivo" (required, maxLength 500, value state error when empty), Confirmar/Cancelar. Calls `PurchaseContractsSetDepartmentRelease {Key, Department, Released, Reason}`; pt-BR server errors via MessageBox; refresh list.
- Row detail: button "Histórico" (or row press) opens a dialog listing `PaymentReleaseHistory` for that contract, filtered to permitted departments, sorted ChangedAt desc: Departamento, Liberado (Sim/Não), Motivo, Usuário, Data/Hora.
- Contract dialog (`PurchaseContractPaymentReleaseDialog.fragment.xml` + `PurchaseContractsBaseController.ts`): department Switches become read-only display (Sim/Não + who/when + current Motivo); add a history section/table (all departments). Financeiro status Select and antecipação checkbox stay editable. Remove the now-unused department-change handler.
- Formatter for history rows reused where possible.
- [ ] Implement; `yarn ts-typecheck` + `yarn lint` clean. Commit.
