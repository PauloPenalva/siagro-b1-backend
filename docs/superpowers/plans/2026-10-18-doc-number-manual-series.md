# Numeração Automático/Manual + séries de Cliente, Fornecedor e Produto — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every numbered document/master record can be created with an "Automático" series (server generates, skipping codes already in use) or a "Manual" series (user types a free, unique code), and STANDALONE gets numbering for Cliente (C), Fornecedor (F) and Produto.

**Architecture:** One central entry point, `DocNumberSequenceService.AssignAsync(DocNumberRequest)`, resolves the series, validates it, and returns `(DocNumberKey, Code)`: typed code for manual, generated-and-skipped code for automatic. Every create service calls it instead of `GetKeyByTransactionCode` + `GetDocNumber`, passing an `ExistsAsync` lambda for its own table. A data-only migration renames default series to "Automático", adds "Manual" rows and seeds the three new types. The frontend gets one shared series-change handler that toggles the number Input between read-only and required.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory in tests), Dapper, xUnit; OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-18-doc-number-manual-series-design.md` (backend repo). Read it before any task.

## Global Constraints

- Code identifiers in English; user-facing text (messages, labels) in pt-BR.
- Two repos, two worktrees, branch `feature/doc-number-manual-series` in each:
  backend `C:\Projetos\SiagroB1\wt-backend-doc-numbers` (exists); frontend: create
  `C:\Projetos\SiagroB1\wt-frontend-doc-numbers` from `main` before Task 8
  (`git -C C:\Projetos\SiagroB1\siagro-b1-frontend worktree add -b feature/doc-number-manual-series ../wt-frontend-doc-numbers main`). Never work in the shared `siagro-b1-*` checkouts.
- Stage every new file right after creating it. Commit per task, message per the repo's `CLAUDE.md` (`tipo(escopo): ...` pt-BR; `DB: <MigrationName>` trailer when a migration is in the commit). Never push, never merge.
- New services must be registered by hand in `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`.
- Series names: exactly `"Automático"` and `"Manual"`.
- New enum values: `Customer = 16`, `Supplier = 17`, `Item = 18` in `TransactionCode`.
- Seeds: Customer prefix `C`, Supplier prefix `F`, Item prefix empty; `NumberSize = '6'`.
- No "Manual" row and no manual numbering for `FinancialDocument` (12) and `StorageTransaction` (5).
- Manual code: `Trim()`ed, prefix/suffix/padding NOT applied, must fit the column, must not exist in the target table. Automatic counter untouched by manual entries.
- **Manual series + empty typed code ⇒ fall back to the default automatic series** (decision for this plan: it is what makes the ~20 system-generated flows that copy/inherit a `DocNumberKey` safe, spec rule 4; the screens enforce "obrigatório" on the client).
- No new unique indexes; duplicate checks happen in `AssignAsync` via `ExistsAsync`.
- Backend tests: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Class>"`. Frontend: `npm run ts-typecheck` and `npm run lint` (do NOT use `yarn test` as a gate — its coverage gate never passes).
- Migrations: never use the `db-migration` launch profile (targets production); always set `ASPNETCORE_ENVIRONMENT` explicitly.

## Review Focus

1. User types a manual code that exists only with different case/spaces (`" f002448 "` vs `F002448`) → treated as the same code: trim always; comparison follows the column's SQL collation (CI) — the InMemory test pins trim only. Test in Task 1.
2. Automatic series meets a run of codes already taken (manual entries ahead of the counter) → skips all of them and returns the first free one, never throws a duplicate. Test in Task 1.
3. Contract/romaneio **copy** of a document created with a manual code → copy must get an automatic code, not "número já em uso". Test in Task 4.
4. A series marked Inativo or belonging to another document type sent by an old/open browser tab → server refuses with pt-BR message. Test in Task 1.
5. Fornecedor created with Manual `F002448` while the Customer `C002448` exists → allowed (different code); Fornecedor with Manual `C002448` → refused (CardCode is one table). Test in Task 6.

---

### Task 1: Central assignment in `DocNumberSequenceService`

**Files:**
- Modify: `SiagroB1.Domain/Enums/TransactionCode.cs` (add 16/17/18)
- Create: `SiagroB1.Application/Services/DocNumbers/DocNumberRequest.cs` (the three records below)
- Modify: `SiagroB1.Application/Services/DocNumbers/DocNumberSequenceService.cs`
- Modify: `SiagroB1.Application.Tests/Support/FakeDocNumberSequenceService.cs`
- Test: `SiagroB1.Application.Tests/DocNumbers/DocNumberAssignTests.cs` (new folder)

**Interfaces:**
- Produces:
  - `public sealed record DocNumberRequest(TransactionCode TransactionCode, Guid? DocNumberKey, string? TypedCode, int MaxLength, Func<string, Task<bool>> ExistsAsync);`
  - `public sealed record DocNumberAssignment(Guid DocNumberKey, string Code);`
  - `public sealed record DocNumberSeries(Guid Key, TransactionCode TransactionCode, string Name, bool IsManual, bool Inactive);`
  - `public virtual Task<DocNumberSeries?> GetSeriesAsync(Guid key)` (Dapper SELECT on DOC_NUMBERS)
  - `public virtual Task<DocNumberAssignment> AssignAsync(DocNumberRequest request)`
  - `public static bool AllowsManual(TransactionCode code)` → false for `FinancialDocument`, `StorageTransaction`.
  - `GetKeyByTransactionCode` now selects `TOP 1 ... WHERE TransactionCode=@tc AND [Default]=1 AND IsManual=0 AND Inactive=0 ORDER BY [Key]`.
  - Fake: `List<DocNumberRequest> Requests`; `AssignAsync` override records the request and returns `(request.DocNumberKey ?? key, string.IsNullOrWhiteSpace(TypedCode) ? next "ST-0001" code : TypedCode.Trim())`. Existing `GetDocNumber`/`GetKeyByTransactionCode` overrides stay so untouched tests keep passing.

`AssignAsync` algorithm (real implementation):
1. `key = request.DocNumberKey ?? await GetKeyByTransactionCode(tc)`; `series = await GetSeriesAsync(key) ?? throw NotFoundException`.
2. `series.TransactionCode != tc` → `ApplicationException("A série de numeração escolhida não pertence a este documento.")`.
3. `series.Inactive` → `ApplicationException($"A série de numeração \"{series.Name}\" está inativa.")`.
4. `series.IsManual`:
   - `!AllowsManual(tc)` → `ApplicationException("Este documento não aceita numeração manual.")`.
   - typed empty/whitespace → re-resolve `key = await GetKeyByTransactionCode(tc)` and continue as automatic.
   - else `code = TypedCode.Trim()`; `code.Length > MaxLength` → `ApplicationException($"O número informado passa de {MaxLength} caracteres.")`; `await ExistsAsync(code)` → `ApplicationException($"O número {code} já está em uso.")`; return `(series.Key, code)`.
5. Automatic: up to 10 000 times `code = await GetDocNumber(key)`; return the first with `!await ExistsAsync(code)`; after the limit throw `ApplicationException("Não foi possível gerar um número livre para a série \"{Name}\".")`.

- [ ] **Step 1: Write failing tests** in `DocNumberAssignTests` using a private stub subclass of `DocNumberSequenceService(null!)` that overrides `GetSeriesAsync`, `GetDocNumber` (returns `"F" + counter.ToString("000000")`) and `GetKeyByTransactionCode`. Tests:
  - `Manual_ReturnsTrimmedTypedCode_WithoutTouchingCounter` — typed `" F002448 "` → Code `"F002448"`, key = manual key, `GetDocNumber` call count 0.
  - `Manual_RejectsCodeAlreadyInUse` — `ExistsAsync` true → throws, message contains `"já está em uso"`.
  - `Manual_RejectsCodeLongerThanColumn` — MaxLength 9, typed `"DE0000001X"` → throws.
  - `Manual_EmptyTypedCode_FallsBackToDefaultAutomatic` — returns default key and generated code.
  - `Manual_NotAllowedForStorageTransaction` — tc `StorageTransaction`, manual series → throws `"não aceita numeração manual"`.
  - `Automatic_SkipsCodesAlreadyInUse` — existing `{F000001, F000002, F000003}` → returns `F000004`.
  - `Rejects_InactiveSeries` and `Rejects_SeriesOfAnotherTransactionCode`.
- [ ] **Step 2: Run** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~DocNumberAssignTests"` → FAIL (types missing).
- [ ] **Step 3: Implement** enum values, records, `GetSeriesAsync`, `AllowsManual`, `AssignAsync`, the `GetKeyByTransactionCode` filter, and the fake changes.
- [ ] **Step 4: Run** the filter above → PASS; then the whole `SiagroB1.Application.Tests` → no new failures versus `main`. (Record the baseline count before Step 3.)
- [ ] **Step 5: Commit** `feat(platform): numeração manual e salto de códigos já usados na série`.

### Task 2: Series rules in the "Numeração de Documentos" CRUD

**Files:**
- Modify: `SiagroB1.Application/Services/DocNumberService.cs`
- Test: `SiagroB1.Application.Tests/DocNumbers/DocNumberServiceTests.cs`

**Interfaces:**
- Consumes: `DocNumberSequenceService.AllowsManual` (Task 1).
- Produces: same public methods; new pt-BR `ApplicationException`s.

Rules (create and update; update must load the stored row to compare, it currently attaches blindly):
- `IsManual` cannot change on update.
- Only one `IsManual` row per `TransactionCode`; refused when `!AllowsManual(tc)`.
- `IsManual && Default` → refused ("A série Manual não pode ser a padrão.").
- `Default && Inactive` → refused ("A série padrão não pode ser inativada.").
- Saving a row with `Default = true` sets `Default = false` on the other rows of the same `TransactionCode`.
- `DeleteAsync` of a manual row → refused ("A série Manual não pode ser excluída.").

- [ ] **Step 1: Failing tests** (InMemory via `TestDb`): `SettingDefault_UnsetsOtherDefaultsOfSameType`, `ManualSeries_CannotBeDefault`, `SecondManualSeries_IsRejected`, `ManualSeries_CannotBeDeleted`, `ManualFlag_CannotChangeOnUpdate`, `DefaultSeries_CannotBeInactivated`, `ManualSeries_RejectedForFinancialDocument`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~DocNumberServiceTests"` → FAIL.
- [ ] **Step 3: Implement** the rules.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(platform): regras de série padrão, manual e inativa na numeração`.

### Task 3: Data migration `DocNumberManualSeries`

**Files:**
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_DocNumberManualSeries.cs` (+ Designer via `dotnet ef migrations add DocNumberManualSeries --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`, then fill `Up` with SQL; model snapshot must not change — the model is unchanged).

`Up` (idempotent T-SQL, `migrationBuilder.Sql`, same style as `SeedPurchaseInvoiceDocNumber`), for the existing types `1,2,4,5,7,8,9,10,11,12,13,14`:
1. Several `Default=1` in one type → keep the one with the highest `NextNumber` (tie: lowest `[Key]`), unset the others.
2. Type with rows but no default → promote the non-manual, active row with highest `NextNumber`.
3. Type with no rows at all → insert `Automático` (`Default=1`, `Prefix=''`, `NumberSize='6'`, `FirstNumber=1`, `NextNumber=1`, `LastNumber=0`). Builder call taken: create, so a Manual row never exists alone and the skip logic covers any collision.
4. Rename the default row to `Automático` unless another row of that type is already named `Automático` (then leave it and `PRINT` the type).
5. Any row already named `Manual` → set `IsManual=1, Default=0`; otherwise insert `Manual` (`IsManual=1`, `Default=0`, counters 0, `NumberSize=NULL`) for types `1,2,4,7,8,9,10,11,13,14,16,17,18`.
6. New types: insert `Automático` default for 16 (`C`), 17 (`F`), 18 (`''`) if the type has no rows, with `NextNumber` = 1 + the max numeric tail among existing codes of that shape: Customer `CardType='C' AND CardCode LIKE 'C%' AND LEN(CardCode) BETWEEN 2 AND 10 AND SUBSTRING(CardCode,2,15) NOT LIKE '%[^0-9]%'`; Supplier same with `S`/`F`; Item `ItemCode NOT LIKE '%[^0-9]%' AND LEN(ItemCode) BETWEEN 1 AND 9`. `LastNumber = NextNumber - 1`.
`Down`: no-op with a comment (data migration; renames are not reversible safely).

- [ ] **Step 1:** Generate the migration and write `Up`.
- [ ] **Step 2: Verify** `dotnet ef migrations script <previous migration> DocNumberManualSeries --context AppDbContext --idempotent --project SiagroB1.Migrations --startup-project SiagroB1.Web` produces the SQL; `git diff` shows no `AppDbContextModelSnapshot` change.
- [ ] **Step 3: Verify on the local dev database** (the one used by the local run profile — see memory `run-stack-locally`, `ASPNETCORE_ENVIRONMENT` explicit): apply, then `SELECT TransactionCode, Name, [Default], IsManual, Prefix, NextNumber FROM DOC_NUMBERS ORDER BY 1,2` shows one `Automático` default per type, `Manual` where expected, none for 5 and 12; run `database update` again → no-op.
- [ ] **Step 4: Commit** `feat(platform): séries Automático e Manual em todos os documentos` with trailer `DB: DocNumberManualSeries`.

### Task 4: Document create services use `AssignAsync`

**Files (modify; line numbers from `main` 20bdb4c):**
- `PurchaseContracts/PurchaseContractsCreateService.cs:28,49` (keep EXCEPTION_00012 for missing key), `PurchaseContracts/PurchaseContractsCopyService.cs` (clear `Code` on the clone)
- `SalesContracts/SalesContractsCreateService.cs:38`, `SalesContracts/SalesContractsCopyService.cs` (clear `Code`)
- `SalesInvoices/SalesInvoicesCreateService.cs:122,128` — `InvoiceNumber`, max 9
- `PurchaseInvoices/PurchaseInvoicesCreateService.cs:160-163` — respect `invoice.DocNumberKey` and typed `InvoiceNumber`, max 9; update the comment. Confirm `PurchaseInvoicesUpdateService` still never copies these two fields.
- `WeighingTickets/WeighingTicketsCreateService.cs:21,25` — max 15
- `StorageAddresses/StorageAddressesCreateService.cs:37,41`
- `OwnershipTransfers/OwnershipTransfersCreateService.cs:20,26` — `TransferCode`
- `WarehouseReconciliations/WarehouseReconciliationsCreateService.cs:35-36`
- `ShipmentLoads/ShipmentLoadsCreateService.cs:36-40` + `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsCreateController.cs` (accept optional `DocNumberKey` and `Code` parameters and set them on the load; register them in `ODataConfigurations` action parameters)
- `StorageInvoices/StorageInvoiceClosingService.cs:233` + `StorageInvoiceCloseRequest` (add `string? Code`) + its action parameter registration
- All under `SiagroB1.Application/Services/` unless a path says otherwise.
- Tests: one test class per touched area under the matching `SiagroB1.Application.Tests/<Area>/` folder (extend the existing test context where one exists).

**Interfaces:**
- Consumes: `AssignAsync(new DocNumberRequest(tc, entity.DocNumberKey, entity.<CodeField>, <max>, c => context.<Set>.AnyAsync(x => x.<CodeField> == c)))`; assign both `DocNumberKey` and code field from the result.
- Max lengths: 50 for `VARCHAR(50)` codes, 15 WeighingTicket, 9 `InvoiceNumber`.
- System callers of `SalesInvoicesCreateService` (billing, returns, ICMS complement, refusal, custody, remittance) and of `PurchaseInvoicesCreateService` (NF-e return, counter-note, items, custody) must reach it with `InvoiceNumber` null/empty, so they never trip "já está em uso": check each caller and clear the field where it is copied from an origin. Same for `StorageTransactionsCreateService` callers (already ignored by Task 5).
- Call `AssignAsync` **before** any `try` whose `catch` re-wraps exceptions (so the pt-BR message reaches the user), and keep it where the old call ran relative to transactions.

- [ ] **Step 1: Failing tests** (fake from Task 1), one per consumer, e.g. `PurchaseInvoicesCreate_UsesChosenSeriesAndTypedNumber`, `SalesInvoicesCreate_PassesTypedInvoiceNumber`, `ShipmentLoadsCreate_UsesChosenSeries`, each asserting `fake.Requests.Single()` has the right `TransactionCode`, key, `TypedCode`, `MaxLength`, and that `ExistsAsync(<code of a seeded row>)` is `true` and `ExistsAsync("NOPE")` is `false`. Plus `PurchaseContractsCopy_ClearsCode` (Review Focus 3): copy request has `TypedCode == null`.
- [ ] **Step 2: Run** the new tests → FAIL.
- [ ] **Step 3: Implement** the conversions.
- [ ] **Step 4: Run** new tests + full `SiagroB1.Application.Tests` → PASS / no regressions vs baseline.
- [ ] **Step 5: Commit** `feat(platform): documentos aceitam número manual na criação` (split into 2 commits if the diff is large: contracts+invoices, then the rest).

### Task 5: System flows and romaneios

**Files (modify):**
- Financial: `Financials/FinancialAdvancesCreateService.cs:87-88`, `FinancialAdvancesRelinkContractService.cs:152-153`, `FinancialDocumentsFirmSyncService.cs:110-111,195-196`, `FinancialDocumentsGenerateService.cs:142-148`, `FinancialDocumentsGenerateWashoutReceivableService.cs:59-64`, `FinancialDocumentsOtherPurchaseService.cs:142-143`, `FinancialDocumentsSalesFirmSyncService.cs:262-263` → `AssignAsync(new(TransactionCode.FinancialDocument, null, null, 50, exists))`.
- `SalesInvoices/SalesInvoicesCreateIcmsComplementService.cs:83-85` → key = origin key, typed `null` (manual origin falls back to automatic).
- `StorageAddresses/StorageAddressesTechnicalLossCalculatorService.cs:122-123`, `StorageTransactions/StorageTransactionsCreateService.cs:54,62` → `AssignAsync` with `TransactionCode.StorageTransaction`, typed `null` (romaneios never take a typed code, even if the body has one).
- Romaneio deletion: `SiagroB1.Web/Controllers/StorageTransactionsController.cs` DELETE → always respond with the pt-BR error "Romaneio não pode ser excluído. Use o cancelamento." (same error shape the controller already returns for business errors); remove `StorageTransactionsDeleteService` and its registration if nothing else uses it.
- Tests: `SiagroB1.Application.Tests/StorageTransactions/StorageTransactionsNumberingTests.cs`, plus existing financial tests must stay green.

- [ ] **Step 1: Failing tests:** `StorageTransactionsCreate_IgnoresTypedCode` (body with `Code="X"` → request `TypedCode == null`, tc `StorageTransaction`); `IcmsComplement_UsesOriginSeriesWithoutTypedCode`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.** Grep afterwards: `grep -rn "GetDocNumber(" SiagroB1.Application/Services` must show only `DocNumberSequenceService` itself.
- [ ] **Step 4: Run** full `SiagroB1.Application.Tests` → no regressions; `dotnet build SiagroB1.sln` → 0 errors.
- [ ] **Step 5: Commit** `feat(storage): romaneio só cancela e fluxos do sistema usam a série automática`.

### Task 6: Cliente, Fornecedor e Produto (STANDALONE)

**Files:**
- Modify: `BusinessPartnerModel` and `ItemModel` (add `public Guid? DocNumberKey { get; set; }`, not persisted on the entities — builder call: no column).
- Modify: `SiagroB1.Application/Services/BusinessPartnerService.cs:83-102` and `SiagroB1.Application/Services/ItemService.cs:15-30` (STANDALONE implementations only; SAP ones untouched).
- Modify: model validation so `CardCode`/`ItemCode` may arrive empty when the series is automatic (check data annotations / `required` on the models and the OData EDM).
- Tests: `SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerNumberingTests.cs`, `SiagroB1.Application.Tests/Items/ItemNumberingTests.cs`.

**Interfaces:**
- Consumes: `AssignAsync`; inject `DocNumberSequenceService` into both services (update DI and existing test constructors).
- Mapping: `CardType "C"` → `Customer`, `"S"` → `Supplier`; any other → keep today's behavior (typed code required, no series). Max 15 for `CardCode`, 50 for `ItemCode`. `ExistsAsync` checks `BusinessPartners.CardCode` (whole table, both types) / `Items.ItemCode`. The old `BP_EXISTING_CODE` check is superseded for the series path.

- [ ] **Step 1: Failing tests:** `Supplier_Automatic_GetsSupplierSeriesCode` (request tc `Supplier`, typed null); `Supplier_Manual_AcceptsF002448_WhenCustomerC002448Exists`; `Supplier_Manual_RejectsCodeOfExistingCustomer` (Review Focus 5, via `ExistsAsync("C002448") == true`); `Item_Automatic_UsesItemSeries`.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** new + full tests → PASS; `dotnet build SiagroB1.sln` → 0 errors.
- [ ] **Step 5: Commit** `feat(partner): numeração automática de cliente, fornecedor e produto`.

### Task 7: Frontend — shared series handling + "Numeração de Documentos" screen

(Frontend worktree from Global Constraints. Paths under `webapp/`.)

**Files:**
- Modify: `controller/common/CommonController.ts` (add handler), `model/` or wherever `DocNumberInfo` is typed (add `IsManual`)
- Modify: `view/docNumbers/fragments/Form.fragment.xml`, `controller/docNumbers/{Add,Edit,Main,BaseController}.controller.ts`
- Modify: `i18n` only if the screen uses it (labels are inline today).

**Interfaces:**
- Produces: `public onDocNumberSeriesChange(event: Select$ChangeEvent): void` on `CommonController`. Reads `IsManual` from the selected item's binding context; sets `ui>/docNumberManual` (boolean); when switching to automatic, sets the property named by the Select's `customData` key `codeProperty` to `null` on the view's binding context. Also `protected resetDocNumberMode(): void` (sets `ui>/docNumberManual=false`) for Add/Edit `onRouteMatched`.
- Standard Select used by every form (Tasks 8–10):
  `items="{path:'/DocNumbers', parameters:{$filter:'TransactionCode eq \'X\' and Inactive eq false', $orderby:'IsManual,Name'}}"`, `change=".onDocNumberSeriesChange"`, `<customData><core:CustomData key="codeProperty" value="Code"/></customData>`;
  Input: `editable="{= ${ui>/docNumberManual} === true }" required="{= ${ui>/docNumberManual} === true }"`.

Screen changes: add Select items `Customer` "Cliente", `Supplier` "Fornecedor", `Item` "Produto", hidden when `isSapMode()`; show the "Manual" checkbox read-only (uncomment), remove the stray `<Label text="Padrão"/>`; when the row is manual, hide Primeiro/Próximo Número, Prefixo, Sufixo, Qtd. Dígitos and lock Padrão; "Manual" cannot be created from the Add screen (checkbox not editable).

- [ ] **Step 1:** Implement handler + screen.
- [ ] **Step 2: Run** `npm run ts-typecheck` and `npm run lint` → 0 errors.
- [ ] **Step 3: Commit** (frontend) `feat(platform): série manual na tela de numeração de documentos`.

### Task 8: Frontend — existing document forms

**Files (modify the series Select + number Input in each form fragment and call `resetDocNumberMode()` in the Add controller):**
`purchaseContracts/fragments/PurchaseContractForm.fragment.xml`, `salesContracts` form, `salesInvoices` form, `storageAddresses` form, `weighingTicket` Form/FirstWeighing/SecondWeighing, `ownershipTransfers` form, `storageInvoices` closing dialog (send `Code` with the action when manual).
Romaneio forms (`storageTransactions`, `storageEntryTransaction`, `shippingTransaction`): only add `and Inactive eq false` + `IsManual eq false` to the filter; Input stays read-only. Remove the delete button/`onDelete` from `controller/storageTransactions/Main.controller.ts` and its view.
`codeProperty` per form: `Code`, except `InvoiceNumber` (salesInvoices) and `TransferCode` (ownershipTransfers).

- [ ] **Step 1:** Implement.
- [ ] **Step 2: Run** `npm run ts-typecheck` and `npm run lint` → 0 errors.
- [ ] **Step 3: Commit** (frontend) `feat(platform): número manual nas telas de documentos e romaneio sem exclusão`.

### Task 9: Frontend — new series Selects and master data forms

**Files:**
- `purchaseInvoices/fragments/Form.fragment.xml:33-37` ("Número interno" gets the Select, `codeProperty=InvoiceNumber`, create sends `DocNumberKey`), its Add/Create controller pre-selects the default (`getDocNumberInfoByTransaction("PurchaseInvoice")`).
- `shipmentLoads` create page (send `DocNumberKey`/`Code` in the create action), `warehouseReconciliations` create form.
- `parceirosNegocio/fragments/Form.fragment.xml:31-37`: Select (filter by `Customer` or `Supplier` from `CardType`; rebind the items filter and preselect the default when Tipo changes) + `CardCode` Input (read-only/required per mode; editable as today on Edit? No — read-only on Edit, as the code is immutable). Visible only when `ui>/standalone`; in SAP mode leave the screen as is.
- `produtos/fragments/Form.fragment.xml:31-37`: same with `Item`, `codeProperty=ItemCode`.
- Add controllers send `DocNumberKey` and leave the code empty for automatic.

- [ ] **Step 1:** Implement.
- [ ] **Step 2: Run** `npm run ts-typecheck` and `npm run lint` → 0 errors.
- [ ] **Step 3: Commit** (frontend) `feat(master-data): série Automático/Manual em entrada, carga, conferência, parceiro e produto`.

### Task 10: End-to-end verification

Follow memories `run-stack-locally`, `verify-via-user-path-not-just-my-layer`, `playwright-verification-pitfalls`, `subagent-must-kill-by-pid`. Start Web + Gateway from the backend worktree and the frontend dev server from the frontend worktree (STANDALONE profile).

- [ ] **Step 1:** From the home page, in Playwright: Numeração de Documentos lists `Automático`/`Manual` per type; create a Fornecedor with Manual `F002448` → saved; create one automatic → gets `F` + next number; create a Cliente automatic → `C…`; create a Produto automatic → 6 digits.
- [ ] **Step 2:** Documento de Entrada with Manual `DE000005` (a free number) → saved with that number; Manual with an existing number → pt-BR error shown; automatic → next free number.
- [ ] **Step 3:** Contrato de Compra Manual → saved; copy it → copy gets automatic code. Romaneio list has no delete; cancel works.
- [ ] **Step 4:** Stop every process you started, by PID.
- [ ] **Step 5:** Report results; no commit needed unless fixes were made (then commit them in their repo).
