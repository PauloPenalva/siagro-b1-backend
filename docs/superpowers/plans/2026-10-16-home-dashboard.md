# Home Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the empty home view into an operational dashboard fed by a single `GET /reports/Dashboard` call (STANDALONE only).

**Architecture:** A `DashboardService` in `SiagroB1.Reports` builds a `DashboardDto` from five aggregated EF queries plus the existing contract-position helpers; a thin controller exposes it. The frontend `Main` view/controller fetch it on every route match and render five KPI tiles, a stock table, the monthly position rows, a 7-day loads table and two contract queues, all with `sap.m`/`sap.f`/`sap.ui.layout` controls already used across the app.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory in tests), xUnit, OpenUI5 1.141 TypeScript, `sap.m`.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-16-home-dashboard-design.md`

## Global Constraints

- Two repos, two commits, both on branch `feature/home-dashboard` (backend from `main`, frontend from `main`). Never push.
- Code identifiers in English; everything the user reads in pt-BR, as literals in the view (no view in the app uses i18n).
- Backend commit: `feat(reports): ...`; frontend commit: `feat(platform): ...`; subject pt-BR imperative, ≤72 chars, no trailing period, body says WHY, ends with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- "Hoje" = `DateTime.Today` passed in by the controller; services/tests take `today` as parameter. Test "today" = `ContractPositionSeed.Today` = 2026-10-08.
- One aggregated query per section, grouped in SQL. No per-item/per-warehouse loops.
- In SAPB1 mode (`IConfiguration["Erp"]` ≠ `STANDALONE`, case-insensitive) the endpoint returns 404.
- JSON is camelCase (default STJ). Dates are serialized as ISO; `deliveryEndDate` is `null` when the sentinel `< 1901-01-01`.
- Stage every new file with `git add` immediately after creating it.

## Review Focus

1. Financial document in USD due today: counted but not summed (Task 2 test `Financial_OtherCurrencyCountedNotSummed`).
2. Contract with negative balance and overdue deadline: not an overdue contract, `ToDeliver = 0` (Task 3 test `Overdue_NegativeBalanceIsNotOverdue`).
3. Stock item whose contracts use a different UOM (e.g. "SC"): own row, `physical = null`, `net = null` (Task 5 test `Stock_ContractUomWithoutStockGetsOwnRowWithoutPhysical`).
4. Day with no loads in the last 7 days: row present with zero (Task 4 test `RecentLoads_EmptyDayIsZero`).
5. Frontend: endpoint 404 (SAPB1) must leave the page empty without an error box (Task 8 manual check in SAP mode is not possible locally; controller guards with `isSapMode()` before calling and swallows 404).

---

### Task 1: DTOs and empty service skeleton

**Files:**
- Create: `SiagroB1.Reports/Dtos/DashboardDto.cs`
- Create: `SiagroB1.Reports/Services/DashboardService.cs`
- Create: `SiagroB1.Reports/Controllers/DashboardController.cs`
- Test: `SiagroB1.Application.Tests/Reports/DashboardServiceTests.cs`

**Interfaces:**
- Produces (all in namespace `SiagroB1.Reports.Dtos`):

```csharp
public class DashboardDto
{
    public DateTime GeneratedAt { get; set; }
    public DashboardFinancialDto Financial { get; set; } = new();
    public DashboardOverdueContractsDto OverdueContracts { get; set; } = new();
    public DashboardLoadsInProgressDto LoadsInProgress { get; set; } = new();
    public List<DashboardDayLoadsDto> RecentLoads { get; set; } = [];
    public List<DashboardStockRowDto> Stock { get; set; } = [];
    public List<ContractMonthlyPositionRowDto> MonthlyPosition { get; set; } = [];
    public DashboardContractQueueDto SalesToDeliver { get; set; } = new();
    public DashboardContractQueueDto PurchasesToReceive { get; set; } = new();
}
public class DashboardMoneyDto { public decimal Amount { get; set; } public int Count { get; set; } }
public class DashboardFinancialDto
{
    public DashboardMoneyDto PayablesDueToday { get; set; } = new();
    public DashboardMoneyDto ReceivablesDueToday { get; set; } = new();
    public DashboardMoneyDto PayablesOverdue { get; set; } = new();
    public DashboardMoneyDto ReceivablesOverdue { get; set; } = new();
}
public class DashboardQuantityDto { public string UnitOfMeasure { get; set; } = ""; public decimal Quantity { get; set; } }
public class DashboardOverdueSideDto { public int Count { get; set; } public List<DashboardQuantityDto> Quantities { get; set; } = []; }
public class DashboardOverdueContractsDto { public DashboardOverdueSideDto Purchase { get; set; } = new(); public DashboardOverdueSideDto Sales { get; set; } = new(); }
public class DashboardLoadsInProgressDto { public int Total, Planned, Loading, InTransshipment, InTransit, RefusalPending; }  // use properties { get; set; }
public class DashboardDayLoadsDto { public DateTime Date { get; set; } public int Count { get; set; } public List<DashboardQuantityDto> Quantities { get; set; } = []; }
public class DashboardStockRowDto
{
    public string ItemCode { get; set; } = ""; public string? ItemName { get; set; } public string UnitOfMeasure { get; set; } = "";
    public decimal? Physical { get; set; } public decimal InLoad { get; set; }
    public decimal PurchaseToDeliver { get; set; } public decimal SalesToDeliver { get; set; } public decimal? Net { get; set; }
}
public class DashboardContractQueueItemDto
{
    public Guid Key { get; set; } public string Code { get; set; } = ""; public string CardCode { get; set; } = ""; public string? CardName { get; set; }
    public string ItemCode { get; set; } = ""; public string? ItemName { get; set; } public string HarvestSeasonCode { get; set; } = "";
    public decimal ToDeliver { get; set; } public string UnitOfMeasure { get; set; } = "";
    public DateTime? CashFlowDate { get; set; } public DateTime? DeliveryEndDate { get; set; } public bool IsOverdue { get; set; }
}
public class DashboardContractQueueDto { public int Total { get; set; } public List<DashboardContractQueueItemDto> Items { get; set; } = []; }
```

- `public class DashboardService(IUnitOfWork db, ContractMonthlyPositionReportService monthlyPosition)` with `public async Task<DashboardDto> BuildAsync(DateTime today)` (Scrutor scan in `AddReportServices` registers any `*Service` class in the assembly — no manual registration).
- `DashboardController`: `[ApiController] [Route("/reports/Dashboard")]`, ctor `(DashboardService service, IConfiguration configuration)`, `[HttpGet] Get()` → `NotFound()` unless `Erp` is `STANDALONE` (case-insensitive, default STANDALONE when missing), else `Ok(await service.BuildAsync(DateTime.Today))`.

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Application.Tests/Reports/DashboardServiceTests.cs
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>"Hoje" = ContractPositionSeed.Today = 08/10/2026.</summary>
public class DashboardServiceTests
{
    [Fact]
    public async Task Build_EmptyDatabase_ReturnsEmptySectionsAndSevenDays()
    {
        var db = TestDb.CreateUnitOfWork();
        var dto = await Service(db).BuildAsync(Today);
        Assert.Equal(Today, dto.GeneratedAt.Date);
        Assert.Equal(0, dto.Financial.PayablesDueToday.Count);
        Assert.Empty(dto.Stock);
        Assert.Empty(dto.MonthlyPosition);
        Assert.Equal(7, dto.RecentLoads.Count);
        Assert.Equal(0, dto.SalesToDeliver.Total);
    }

    private static DashboardService Service(IUnitOfWork db) =>
        new(db, new ContractMonthlyPositionReportService(db, new RecordingFastReportService()));

    private static Task Save(IUnitOfWork db) => db.Context.SaveChangesAsync();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~DashboardServiceTests"`
Expected: build error (DashboardService not found).

- [ ] **Step 3: Create the DTO file, the service with `BuildAsync` returning `GeneratedAt = today` plus 7 `RecentLoads` entries (today backwards, zeros) and empty sections, and the controller**

- [ ] **Step 4: Run test to verify it passes**

Run: same. Expected: PASS. Then `dotnet build SiagroB1.sln` succeeds.

- [ ] **Step 5: Stage and commit**

```bash
git add SiagroB1.Reports/Dtos/DashboardDto.cs SiagroB1.Reports/Services/DashboardService.cs SiagroB1.Reports/Controllers/DashboardController.cs SiagroB1.Application.Tests/Reports/DashboardServiceTests.cs
git commit -m "feat(reports): esqueleto do endpoint do dashboard da home"
```

---

### Task 2: Financial section

**Files:**
- Modify: `SiagroB1.Reports/Services/DashboardService.cs`
- Test: `SiagroB1.Application.Tests/Reports/DashboardServiceTests.cs`

**Interfaces:**
- Produces: `private static async Task<DashboardFinancialDto> FinancialAsync(AppDbContext context, DateTime today)`.

Rules (spec §4.1): universe `Status ∈ {Open, PartiallySettled}` and `Nature != Advance`; amount = Σ(`NetAmount − SettledAmount`) only for `Currency == Brl`; count = all currencies. "Today" = `DueDate >= today && DueDate < today.AddDays(1)`; "overdue" = `DueDate < today`. One query grouped by `Direction` and a computed bucket, or two small `GroupBy` queries — both fine; no per-document loop.

- [ ] **Step 1: Write failing tests**

Add a seed helper in the test class:

```csharp
private static FinancialDocument Fin(FinancialDirection dir, DateTime due, decimal net, decimal settled = 0m,
    FinancialDocumentStatus status = FinancialDocumentStatus.Open, FinancialDocumentNature nature = FinancialDocumentNature.Provisional,
    CurrencyType currency = CurrencyType.Brl) =>
    new() { Key = Guid.NewGuid(), CardCode = "F001", Direction = dir, DueDate = due, NetAmount = net, SettledAmount = settled, Status = status, Nature = nature, Currency = currency };
```

Tests:

```csharp
[Fact] public async Task Financial_TodayAndOverdueSplitByDirection()
// Payable due Today 1000 (settled 200) → PayablesDueToday {800, 1}; Payable due Today-1 500 → PayablesOverdue {500, 1};
// Receivable due Today 300; Receivable due Today+1 900 → not counted anywhere. Assert all four buckets.

[Fact] public async Task Financial_SettledCanceledAndAdvanceAreOut()
// Payable due Today: one Settled, one Canceled, one Nature=Advance → PayablesDueToday {0, 0}.

[Fact] public async Task Financial_OtherCurrencyCountedNotSummed()
// Payable due Today: 1000 Brl + 400 Usd → PayablesDueToday {1000, 2}.

[Fact] public async Task Financial_ProvisionalWithoutInvoiceCounts()
// Provisional Payable due Today, NetAmount 5000, ComposedAmount 0 → PayablesDueToday.Amount == 5000 (spec: compromisso do contrato).
```

- [ ] **Step 2: Run, expect FAIL** (`dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~DashboardServiceTests.Financial"`)
- [ ] **Step 3: Implement `FinancialAsync` and wire it into `BuildAsync`**
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** `feat(reports): dashboard — a pagar e a receber de hoje e vencidos`

---

### Task 3: Overdue contracts and the two contract queues

**Files:**
- Modify: `SiagroB1.Reports/Services/DashboardService.cs`
- Test: `SiagroB1.Application.Tests/Reports/DashboardServiceTests.cs`

**Interfaces:**
- Consumes: `ContractPositionData.PurchasePositionsAsync(context, ContractPositionData.PurchaseQuery(context, new ContractPositionReportRequest()))` and the `Sales*` pair; `ContractPosition.ToDeliver`, `ContractPositionText.HasDeadline`, `ContractPositionText.InSectionOrder`.
- Produces: `private static DashboardOverdueContractsDto Overdue(List<ContractPosition> positions, DateTime today)`, `private static DashboardContractQueueDto Queue(IEnumerable<ContractPosition> side, DateTime today)` (top 10, `Total` = all with `ToDeliver > 0`), and `BuildAsync` loads positions ONCE and shares them with Task 5.

Rules: overdue = `HasDeadline(DeliveryEndDate) && DeliveryEndDate.Date < today && ToDeliver > 0`; quantities grouped by `UnitOfMeasureCode.Trim().ToUpperInvariant()`. Queue order = `InSectionOrder`; `IsOverdue` same predicate; `DeliveryEndDate = null` when `!HasDeadline`; `CashFlowDate = StandardCashFlowDate`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public async Task Overdue_CountsOnlyPastDeadlineWithBalance()
// Purchase deliveryEnd Today-1 total 1000 → counted; Purchase deliveryEnd Today → not; Purchase deliveryEnd Today-1 status Finished → not;
// Sales deliveryEnd Today-5 total 300 → sales counted. Assert Purchase {1, [KG 1000]}, Sales {1, [KG 300]}.

[Fact] public async Task Overdue_NegativeBalanceIsNotOverdue()
// Purchase deliveryEnd Today-1 total 1000 with PurchaseAllocation(contract, 1200) → Purchase.Count == 0.

[Fact] public async Task Overdue_QuantitiesGroupedByUom()
// Two overdue purchases, uom "KG" and "kg " → one quantity line "KG".

[Fact] public async Task Queues_OrderedByCashFlowThenDeliveryEndNullsLast_TopTenWithTotal()
// 12 sales: cashFlow dates spread, two null cashFlow, one no-deadline (DateTime.MinValue). Assert Items.Count == 10, Total == 12,
// first item is the earliest cashFlow, last-of-all (not in top 10) are the null cashFlow ones; item without deadline has DeliveryEndDate == null.

[Fact] public async Task Queues_ExcludeZeroBalanceAndFlagOverdue()
// Sales fully allocated (SalesAllocation total) → not in queue; Sales deliveryEnd Today-1 → IsOverdue true.
```

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement `Overdue`, `Queue`, position loading in `BuildAsync`**
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** `feat(reports): dashboard — contratos vencidos e filas a entregar/receber`

---

### Task 4: Loads in progress and last 7 days

**Files:**
- Modify: `SiagroB1.Reports/Services/DashboardService.cs`
- Test: `SiagroB1.Application.Tests/Reports/DashboardServiceTests.cs`

**Interfaces:**
- Produces: `private static async Task<DashboardLoadsInProgressDto> LoadsInProgressAsync(AppDbContext context)` and `private static async Task<List<DashboardDayLoadsDto>> RecentLoadsAsync(AppDbContext context, DateTime today)`.

Rules (spec §4.1/§4.4): in progress: Planned → `Planned`; Open + PartiallyInvoiced → `Loading`; InTransshipment; Invoiced → `InTransit`; RefusalPending; `Total` = sum. Recent: `LoadDate >= today.AddDays(-6) && LoadDate < today.AddDays(1)` and `Status != Planned && Status != Cancelled`, grouped by `LoadDate.Date` and UOM (sum `TotalQuantity`); output 7 entries, today first, missing days zero with empty `Quantities`.

Seed helper: `private static ShipmentLoad Load(ShipmentLoadStatus status, DateTime date, decimal qty = 30_000m, string uom = "KG") => new() { Key = Guid.NewGuid(), Code = Guid.NewGuid().ToString("N")[..10], Status = status, LoadDate = date, TotalQuantity = qty, ItemCode = "10001", ItemName = "SOJA", UnitOfMeasureCode = uom, TruckCode = "T1", WarehouseCode = "W1" };`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public async Task LoadsInProgress_GroupsByStatus()
// one of each status 0..9 → Total 6, Planned 1, Loading 2, InTransshipment 1, InTransit 1, RefusalPending 1.

[Fact] public async Task RecentLoads_SevenDaysTodayFirst_PlannedAndCancelledOut()
// Open Today 30000; Open Today-1 x2 (20000 + 10000); Planned Today-1; Cancelled Today-2; Invoiced Today-6 5000; Open Today-7 (out).
// Assert dates == Today, Today-1 … Today-6; counts [1,2,0,0,0,0,1]; day Today-1 Quantities == [KG 30000].

[Fact] public async Task RecentLoads_EmptyDayIsZero()
// no loads → 7 rows, all Count 0 and Quantities empty.
```

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement both methods; `RecentLoadsAsync` replaces the Task 1 placeholder**
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** `feat(reports): dashboard — cargas em andamento e dos últimos 7 dias`

---

### Task 5: Stock by item

**Files:**
- Modify: `SiagroB1.Reports/Services/DashboardService.cs`
- Test: `SiagroB1.Application.Tests/Reports/DashboardServiceTests.cs`

**Interfaces:**
- Consumes: positions list from Task 3.
- Produces: `private static async Task<List<DashboardStockRowDto>> StockAsync(AppDbContext context, List<ContractPosition> positions)`.

Rules (spec §4.2): physical per `ItemCode` + normalized UOM = one `GroupBy` query over `StorageTransactions` with the same predicate as `StorageTransactionsWarehouseBalanceService.CalculateAsync` (status Confirmed/Invoiced; types Purchase, PurchaseReturn, SalesShipment, SalesShipmentReturn, TransshipmentReceipt, WarehouseLoss, WarehouseGain; `+NetWeight` for Purchase/SalesShipmentReturn/TransshipmentReceipt/WarehouseGain, `−NetWeight` otherwise), all warehouses. `InLoad` = Σ(`TotalQuantity − InvoicedQuantity`) of `ShipmentLoads` with status Open/PartiallyInvoiced grouped by item + UOM. `PurchaseToDeliver`/`SalesToDeliver` = Σ `ToDeliver` of positions by item + UOM. Rows = union of keys; `Physical = null` when no storage row; `Net = Physical + InLoad + PurchaseToDeliver − SalesToDeliver` or `null` when `Physical` is null. Drop rows where everything is zero/null. `ItemName`: first non-empty from storage, loads, or positions. Order by `ItemName`, then `ItemCode`.

Seed helper: `private static StorageTransaction St(StorageTransactionType type, decimal net, StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed, string item = "10001", string uom = "KG", string wh = "W1") => new() { Key = Guid.NewGuid(), TransactionType = type, TransactionStatus = status, NetWeight = net, ItemCode = item, ItemName = "SOJA EM GRÃOS", UnitOfMeasureCode = uom, WarehouseCode = wh, CardCode = "F001" };` (add any other `required` members the entity declares).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public async Task Stock_PhysicalSumsAllWarehousesWithSignByType()
// Purchase 1000 W1, Purchase 500 W2, SalesShipment 300, WarehouseGain 50, WarehouseLoss 20, PurchaseReturn 100 → Physical 1130.

[Fact] public async Task Stock_PendingAndCancelledTransactionsAreOut()
// Purchase 1000 Confirmed, Purchase 999 Pending, Purchase 999 Cancelled, Purchase 200 Invoiced → Physical 1200.

[Fact] public async Task Stock_NetUsesContractsAndInLoad()
// Physical 1000; Purchase contract total 800; Sales contract total 1500; Load Open TotalQuantity 400 InvoicedQuantity 100 → InLoad 300, Net 600.

[Fact] public async Task Stock_ContractUomWithoutStockGetsOwnRowWithoutPhysical()
// Storage KG 1000; Sales contract uom "SC" total 50 → two rows; "SC" row Physical null, Net null, SalesToDeliver 50.

[Fact] public async Task Stock_ItemWithoutAnythingDoesNotAppear()
// Purchase contract Finished only → Stock empty.
```

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Implement `StockAsync`**
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** `feat(reports): dashboard — estoque físico e posição por produto`

---

### Task 6: Monthly position and end-to-end service check

**Files:**
- Modify: `SiagroB1.Reports/Services/DashboardService.cs`
- Test: `SiagroB1.Application.Tests/Reports/DashboardServiceTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
[Fact] public async Task MonthlyPosition_IsTheReportRows()
// Purchase total 1000 deliveryEnd Today-1; Sales total 300 deliveryEnd 2026-12-01 → MonthlyPosition buckets ["Vencido","12/2026"], PurchaseQuantity [1000,0], SalesQuantity [0,300].
```

- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: `BuildAsync` sets `MonthlyPosition = await monthlyPosition.BuildRowsAsync(new ContractMonthlyPositionRequest(), today)`**
- [ ] **Step 4: Run full test project and build**

Run: `dotnet test SiagroB1.Application.Tests` and `dotnet build SiagroB1.sln`. Expected: all green.

- [ ] **Step 5: Smoke against SQL Server** (memory `ef-inmemory-hides-untranslatable-linq`): run Web + Gateway + Reports locally per `run-stack-locally` memory, log in, `GET http://localhost:5246/reports/Dashboard` with the session cookie → 200 JSON, no EF translation error in the Reports console. Record the response time.

- [ ] **Step 6: Commit** `feat(reports): dashboard — comprado x vendido por mês e endpoint completo`. Also commit the spec and this plan on the same branch: `git add docs/superpowers/specs/2026-10-16-home-dashboard-design.md docs/superpowers/plans/2026-10-16-home-dashboard.md && git commit -m "docs(reports): design e plano do dashboard da home"`.

---

### Task 7: Frontend — route, model and controller

**Files:**
- Modify: `siagro-b1-frontend/webapp/model/ServerRoutes.ts` (add `dashboard: '/reports/Dashboard'`)
- Create: `siagro-b1-frontend/webapp/types/Dashboard.ts` (TS interfaces mirroring Task 1 DTOs, camelCase)
- Modify: `siagro-b1-frontend/webapp/controller/Main.controller.ts`

**Interfaces:**
- Produces: `Main` controller with `onInit()` (attach `main` route `patternMatched` → `load()`), `public async load(): Promise<void>`, `onRefresh()`, navigation handlers `onOpenPayables/onOpenReceivables/onOpenLoads/onOpenPurchaseContracts/onOpenSalesContracts/onOpenMonthlyReport` (routes `accountsPayable`, `accountsReceivable`, `shipmentLoads`, `purchaseContracts`, `salesContracts`, `contractMonthlyPositionReport`), `onOpenSalesContract(ev)` / `onOpenPurchaseContract(ev)` (routes `salesContractsDetail` / `purchaseContractsDetail` with `{ id: key }`), and a `JSONModel` named `dashboard` with the DTO plus `{ loading: boolean, loadedAt: string, hidden: boolean }`.
- `load()`: if `SessionService.isSapMode()` → `hidden = true`, return. Else `new RequestModel().get<Dashboard>(ServerRoutes.dashboard)`; on 404 → `hidden = true`; other error → rethrow to the global handler. Use `SessionService` the way `BaseController.refreshStandaloneFlag()` does.

- [ ] **Step 1: Add the route entry and the types file; `git add` the new file**
- [ ] **Step 2: Implement the controller**
- [ ] **Step 3: Verify** `yarn ts-typecheck` and `yarn lint` pass (run from `siagro-b1-frontend`).
- [ ] **Step 4: Commit** `feat(platform): controller da home carrega o dashboard`

---

### Task 8: Frontend — view

**Files:**
- Modify: `siagro-b1-frontend/webapp/view/Main.view.xml`
- Modify: `siagro-b1-frontend/webapp/model/formatter.ts` (add `formatBrl(value: number | string): string` → `R$ 1.234,56` via `toLocaleString('pt-BR', {style:'currency', currency:'BRL'})`, and `formatQuantityUom(quantity, uom)` → `formatDecimal(q, 0) + " " + uom`)

Layout (spec §3), inside `f:DynamicPage` with title "Painel" and a "Atualizar" button + `loadedAt` text; whole page `visible="{= !${dashboard>/hidden}}"`, `busy="{dashboard>/loading}"`:

1. KPI row: `sap.ui.layout.Grid defaultSpan="XL2 L2 M4 S6"` with five `GenericTile` (`frameType="TwoByOne"`, `press` → handler). Each has a `TileContent` with `NumericContent` for the big number and `footer` for the small line:
   - "A pagar hoje": value `formatBrl(payablesDueToday.amount)`, footer `"{n} títulos · vencidos R$ x ({m})"`, `valueColor="Error"` when overdue amount > 0.
   - "A receber hoje": same with receivables, `valueColor="Good"`.
   - "Compras com entrega vencida": value = `purchase.count`, footer = quantities joined (`"1.240 KG"`), `valueColor="Critical"` when count > 0.
   - "Vendas com entrega vencida": same with sales.
   - "Cargas em andamento": value = `total`, footer `"{planned} planej. · {loading} carreg. · {inTransshipment} transb. · {inTransit} trânsito · {refusalPending} recusa"`.
2. Body: `sap.ui.layout.Grid defaultSpan="XL6 L6 M12 S12"` with two `VBox`:
   - Left: `Panel headerText="Estoque e posição por produto"` → `sap.m.Table items="{dashboard>/stock}"` columns Produto, UM, Físico, Em carga, Compra a receber, Venda a entregar, Líquido (`ObjectNumber state="Error"` when net < 0; blank when null). Then `Panel headerText="Comprado x vendido por mês"` with a header toolbar button "Relatório" → `onOpenMonthlyReport`; `sap.m.Table items="{dashboard>/monthlyPosition}"` columns Produto/safra (`group`), Prazo (`bucket`), Compra, Venda, Líquido, Acumulado; use `sap.m.Table` `items` sorter grouping by `group` (`Sorter("group", false, true)` in the binding) so the group header shows once.
   - Right: `Panel headerText="Cargas dos últimos 7 dias"` → table of `recentLoads`: Dia (`Hoje`/`Ontem`/`Anteontem`/`dd/MM` via a formatter `formatRelativeDay(date, index)`), Cargas, Quantidade. Then `Panel headerText="Vendas a entregar"` (subheader "por recebimento, depois por entrega") → table `salesToDeliver/items`, columns Cliente, Produto/safra, Saldo, Recebe em, Entrega até (`ObjectStatus state="Error"` when `isOverdue`); `ColumnListItem type="Navigation" press="onOpenSalesContract"`; footer toolbar `"{total} contratos"` + button "Ver todos" → `onOpenSalesContracts`. Then the same for `purchasesToReceive` with Fornecedor / Paga em / `onOpenPurchaseContract` / `onOpenPurchaseContracts`.
3. Only libraries already used by other views: `sap.m`, `sap.f`, `sap.ui.layout`.

- [ ] **Step 1: Add the formatters**
- [ ] **Step 2: Write the view**
- [ ] **Step 3: Verify** `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint` (no new errors beyond the baseline on `main`).
- [ ] **Step 4: Visual check on the local stack** (`yarn start:dev` + backend from Task 6 Step 5): open `http://localhost:8080`, log in on the CEAGUI dev database. Confirm: five tiles with real numbers, both columns populated, the two queues ordered by date, tile and row clicks navigate, "Atualizar" reloads, narrow window (≤ 1024px) stacks the columns. Take a screenshot for the user.
- [ ] **Step 5: Cross-check two numbers against their screens**: "A pagar hoje" vs Contas a Pagar filtered by status aberto and vencimento = hoje; "Comprado x vendido" first group vs the PDF report. They must match (spec anti-goal).
- [ ] **Step 6: Commit** `feat(platform): home vira painel com indicadores operacionais`

---

### Task 9: Finish

- [ ] **Step 1:** `superpowers:verification-before-completion`: rerun `dotnet test SiagroB1.Application.Tests`, `yarn ts-typecheck`, `yarn lint`; paste the summary lines.
- [ ] **Step 2:** Confirm both repos are on `feature/home-dashboard`, `git status` clean, `git log --oneline main..` lists the commits. Do not merge, do not push.
- [ ] **Step 3:** Write a memory note (`home-dashboard-feature.md`) with the decisions in spec §7 and any trap hit during Task 6 Step 5 / Task 8 Step 4.
