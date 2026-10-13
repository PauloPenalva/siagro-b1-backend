# Troca nota — romaneios de quebra/sobra de descarga — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the delivery reconciliation of a sales invoice item is closed, the port result reaches
the supplier's CIF purchase contract as new storage transactions `PurchaseDischargeLoss(16)` /
`PurchaseDischargeGain(17)`. They are allocated to the contract (∓) and stay out of the warehouse
balance and out of the release's shipped quantity.

**Architecture:**
- **Planner.** `PurchaseDischargeAllocationPlanner` is pure and static. It splits the item's
  difference across the Expedições of the load.
- **Sync.** `PurchaseDischargeTransactionsSyncService` works in two steps:
  - `PrepareAsync` reads the data and runs every guard, with no writes;
  - `ApplyAsync` cancels the item's old 16/17 and creates and allocates the new ones.
- **Hook.** `SalesInvoicesItemsUpdateService` calls `PrepareAsync` before touching the item and
  `ApplyAsync` after the sales recalculations, all inside one DB transaction.
- **Guards.** Static `PurchaseDischargeGuards` blocks deleting/cancelling invoices and items, and
  reversing Expedições, that have active 16/17.
- **Read side.** A read-only list function feeds the new screen.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory in tests), ASP.NET Core OData, Dapper (the
allocations-by-contract query), xUnit.

**Spec:** `docs/superpowers/specs/2026-09-19-triangular-port-adjustment-design.md` — read it first.
Sections 1–3 are the contract of this plan.

## Global Constraints

- **Language:** code identifiers, tables and columns are English. User-facing messages are pt-BR.
  This plan follows the style of `SalesInvoicesItemsUpdateService` and uses literal pt-BR strings in
  `DefaultException`.
- **Git:** `git add` every new or changed file right after touching it. Commit only after the user
  reviews, following the convention in `CLAUDE.md`, with `DB: <MigrationName>` whenever a migration
  is included. **Never push.** Check the branch (`git branch --show-current`) before committing.
- **Only `FreightTerms.Cif` purchase contracts get discharge transactions.**
  ⚠️ `Cif = 0` is the enum default. The eligibility rule is "Expedição of a load **and** contract
  CIF", never CIF alone.
- **16/17 must never enter** `StorageTransactionsWarehouseBalanceService`,
  `ShipmentReleasesRecalculateShippedService` or any load service. So:
  - the new transactions **never** set `ShipmentLoadKey`, `ShipmentReleaseKey` or `SalesInvoiceKey`;
  - their links are two new columns, `DischargeSalesInvoiceItemKey` and
    `DischargeShippingTransactionKey`.
- **Business guards run before any write**, and before `BeginTransactionAsync`. A catch that wraps
  in `DefaultException` would otherwise swallow the message.
- **Allocation sums:** `ApplyAllocationAsync` derives `AllocatedVolume` from the **persisted**
  allocations, so call `SaveChangesAsync` after every line (memory
  "delivery-difference-single-owner-feature").
- **InMemory does not enforce FKs:** the delete/cancel guards are what the tests verify.
- **Migrations:** apply them with `$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'`, and never
  use the `db-migration` profile. To undo, run `dotnet ef migrations remove` **with** build
  (`--no-build` removes the wrong one).
- **Stop `SiagroB1.Web`** before `dotnet build`/`dotnet test` (DLL lock).
- **Single test:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Name>"`.
- **Rounding:** 3 decimals, `MidpointRounding.ToEven`, the same as the warehouse balance.

## File map

| File | Responsibility |
|---|---|
| `SiagroB1.Domain/Enums/StorageTransactionType.cs` (modify) | `PurchaseDischargeLoss = 16`, `PurchaseDischargeGain = 17` |
| `SiagroB1.Domain/Enums/TransactionCode.cs` (modify) | `PurchaseDischarge = 14` (owner of 16/17) |
| `SiagroB1.Domain/Entities/StorageTransaction.cs` (modify) | `DischargeSalesInvoiceItemKey`, `DischargeShippingTransactionKey` |
| `SiagroB1.Infra/Context/AppDbContext.cs` (modify) | Two NoAction FKs without navigation |
| `SiagroB1.Migrations/AppContext/*_AddPurchaseDischargeTransactions.cs` (generate) | Schema |
| `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationCreateService.cs` (modify) | `ExecuteDischargeAsync` |
| `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsGetAllocationsByContractService.cs` (modify) | Types 16/17 in the Entregas query |
| `SiagroB1.Domain/Dtos/PurchaseContractAllocationsByContractDto.cs` (modify) | `DischargeInvoiceNumber`, `DischargeLoadCode` |
| `SiagroB1.Application/Services/PurchaseDischarges/PurchaseDischargeAllocationPlanner.cs` (create) | Pure split rule |
| `SiagroB1.Application/Services/PurchaseDischarges/PurchaseDischargeTransactionsSyncService.cs` (create) | Prepare (guards) + Apply |
| `SiagroB1.Application/Services/PurchaseDischarges/PurchaseDischargeGuards.cs` (create) | "Has active 16/17?" queries |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsUpdateService.cs` (modify) | Hook + DB transaction |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesDeleteService.cs`, `SalesInvoicesItemsDeleteService.cs`, `SalesInvoicesCancelService.cs` (modify) | Guards |
| `SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsReverseService.cs` (modify) | Guard |
| `SiagroB1.Application/Services/PurchaseDischarges/PurchaseDischargeTransactionsListService.cs` (create) | Read side for the new screen |
| `SiagroB1.Domain/Dtos/PurchaseDischargeTransactionListDto.cs` (create) | List row |
| `SiagroB1.Web/...` controller + `ODataConfigurations.cs` + `ServiceCollectionExtensions.cs` (modify/create) | Function endpoint + DI |
| `SiagroB1.Migrations/AppContext/*_AddPurchaseDischargesMenu.cs` (generate + hand-edit) | `MENU_ITEMS` + `ROLE_MENUS` |
| `SiagroB1.Application.Tests/PurchaseDischarges/*` (create) | Tests |

---

### Task 1: Types, owner code, link columns and migration

**Files:**
- Modify: `SiagroB1.Domain/Enums/StorageTransactionType.cs`
- Modify: `SiagroB1.Domain/Enums/TransactionCode.cs`
- Modify: `SiagroB1.Domain/Entities/StorageTransaction.cs` (after `ShippingReleaseChangeKey`, ~line 172)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (next to the `ShippingReleaseChangeKey` FK, ~line 462)
- Generate: `SiagroB1.Migrations/AppContext/*_AddPurchaseDischargeTransactions.cs`
- Test: `SiagroB1.Application.Tests/PurchaseDischarges/PurchaseDischargeModelTests.cs` (create)

**Interfaces:**
- Produces:
  - `StorageTransactionType.PurchaseDischargeLoss` (16) and `.PurchaseDischargeGain` (17);
  - `TransactionCode.PurchaseDischarge` (14);
  - `StorageTransaction.DischargeSalesInvoiceItemKey : Guid?` and
    `StorageTransaction.DischargeShippingTransactionKey : Guid?`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

/// <summary>
/// Troca nota: os romaneios de quebra/sobra de descarga (16/17) acertam o contrato de compra CIF e
/// NADA mais — nem o saldo do armazém do par 8/7 (onde o grão nunca esteve), nem o embarcado da
/// liberação (ela registra o que foi embarcado). Os vínculos são colunas próprias, sem navegação.
/// </summary>
public class PurchaseDischargeModelTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public void Enum_values_are_stable()
    {
        Assert.Equal(16, (int)StorageTransactionType.PurchaseDischargeLoss);
        Assert.Equal(17, (int)StorageTransactionType.PurchaseDischargeGain);
        Assert.Equal(14, (int)TransactionCode.PurchaseDischarge);
    }

    [Theory]
    [InlineData(nameof(StorageTransaction.DischargeSalesInvoiceItemKey), typeof(SalesInvoiceItem))]
    [InlineData(nameof(StorageTransaction.DischargeShippingTransactionKey), typeof(ShippingTransaction))]
    public void Link_columns_are_NoAction_foreign_keys(string property, Type principal)
    {
        var entity = _db.Context.Model.FindEntityType(typeof(StorageTransaction))!;
        var fk = entity.GetForeignKeys().Single(f => f.Properties.Single().Name == property);

        Assert.Equal(principal, fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
        Assert.Null(fk.DependentToPrincipal);
    }

    [Theory]
    [InlineData(StorageTransactionType.PurchaseDischargeLoss)]
    [InlineData(StorageTransactionType.PurchaseDischargeGain)]
    public async Task Discharge_transactions_do_not_move_the_warehouse_balance(StorageTransactionType type)
    {
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "D1", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "PORTO", TransactionType = type,
            TransactionStatus = StorageTransactionsStatus.Confirmed, GrossWeight = 500m, NetWeight = 500m,
        });
        await _db.Context.SaveChangesAsync();

        Assert.Equal(0m, await StorageTransactionsWarehouseBalanceService.CalculateAsync(_db.Context, "PORTO", "SOJA"));
    }

    [Theory]
    [InlineData(StorageTransactionType.PurchaseDischargeLoss)]
    [InlineData(StorageTransactionType.PurchaseDischargeGain)]
    public void Discharge_transactions_do_not_consume_releases(StorageTransactionType type)
    {
        Assert.False(ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(type));
        Assert.Equal(0, ShipmentReleasesRecalculateShippedService.ShippedSign(ReleaseOrigin.Standard, type));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseDischargeModelTests"`
Expected: compile error — `PurchaseDischargeLoss`, `PurchaseDischarge` and the link properties do
not exist.

- [ ] **Step 3: Add the enum values**

In `StorageTransactionType.cs`, after `WarehouseGain = 14`:

```csharp
    // 15 reservado ao transbordo (GAC-1181, pausado).
    PurchaseDischargeLoss = 16,  // Compra - Quebra de descarga (troca nota): gerada pela Conferência de Entregas
    PurchaseDischargeGain = 17,  // Compra - Sobra de descarga (troca nota): gerada pela Conferência de Entregas
```

In `TransactionCode.cs`, after `WarehouseReconciliation = 13`:

```csharp
    PurchaseDischarge = 14,
```

- [ ] **Step 4: Add the link columns**

In `StorageTransaction.cs`, right after `public Guid? ShippingReleaseChangeKey { get; set; }`:

```csharp
    /// <summary>
    /// Troca nota: item do documento de saída cuja Conferência de Entregas gerou este romaneio de
    /// quebra/sobra de descarga (<see cref="StorageTransactionType.PurchaseDischargeLoss"/> /
    /// <see cref="StorageTransactionType.PurchaseDischargeGain"/>).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Coluna própria, e NÃO <see cref="SalesInvoiceKey"/>/<see cref="ShipmentLoadKey"/>.</b>
    /// Aquelas entram nos saldos da nota e da carga; o romaneio de descarga só acerta o contrato de
    /// compra. Sem navigation property, pelo mesmo motivo das chaves da troca de liberação.
    /// </remarks>
    public Guid? DischargeSalesInvoiceItemKey { get; set; }

    /// <summary>
    /// Troca nota: a Expedição (par Compra 8 / Saída 7 em <c>SHIPPING_TRANSACTIONS</c>) cujo
    /// contrato de compra este romaneio acerta.
    /// </summary>
    /// <remarks>
    /// ⚠️ NÃO é <see cref="ShipmentReleaseKey"/>: a quebra reabre o CONTRATO, nunca a liberação.
    /// </remarks>
    public Guid? DischargeShippingTransactionKey { get; set; }
```

- [ ] **Step 5: Configure the FKs**

In `AppDbContext.cs`, right after the `ShippingReleaseChangeKey` FK block (before the closing `}` of
`OnModelCreating`, ~line 466):

```csharp
        // Troca nota: o romaneio de quebra/sobra de descarga aponta o item conferido e a Expedição
        // acertada. Sem navegação (não entra no EDM) e NoAction, como todo o projeto — excluir nota,
        // item ou Expedição com romaneio ativo é barrado por PurchaseDischargeGuards.
        modelBuilder.Entity<StorageTransaction>()
            .HasOne<SalesInvoiceItem>().WithMany()
            .HasForeignKey(x => x.DischargeSalesInvoiceItemKey)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<StorageTransaction>()
            .HasOne<ShippingTransaction>().WithMany()
            .HasForeignKey(x => x.DischargeShippingTransactionKey)
            .OnDelete(DeleteBehavior.NoAction);
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseDischargeModelTests"`
Expected: PASS (6 tests).

The balance and release tests pass without code changes, because both services use explicit type
lists. That is the point: they guard against someone later adding a "catch-all" filter.

- [ ] **Step 7: Generate and apply the migration**

```powershell
dotnet ef migrations add AddPurchaseDischargeTransactions --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir AppContext
```

Open the generated file. It must contain **only**:
- two `AddColumn<Guid>` on `STORAGE_TRANSACTIONS`, nullable;
- two `CreateIndex`;
- two `AddForeignKey` with `onDelete: ReferentialAction.NoAction`.

Anything else means snapshot drift, and you must stop and report it.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef migrations list --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Expected: `list` shows the new migration as pending against `IDX_SIAGRO_DEV` (localhost), and
`update` applies it.

- [ ] **Step 8: Stage**

```bash
git add SiagroB1.Domain/Enums/StorageTransactionType.cs SiagroB1.Domain/Enums/TransactionCode.cs SiagroB1.Domain/Entities/StorageTransaction.cs SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Migrations/AppContext SiagroB1.Application.Tests/PurchaseDischarges/PurchaseDischargeModelTests.cs
```

---

### Task 2: Allocation of 16/17 and the Entregas query

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationCreateService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsGetAllocationsByContractService.cs`
- Modify: `SiagroB1.Domain/Dtos/PurchaseContractAllocationsByContractDto.cs`
- Test: `SiagroB1.Application.Tests/PurchaseDischarges/PurchaseContractsAllocationDischargeTests.cs` (create)

**Interfaces:**
- Consumes: Task 1 enum values.
- Produces:
  `public Task PurchaseContractsAllocationCreateService.ExecuteDischargeAsync(Guid purchaseContractKey, StorageTransaction discharge, decimal volume, string userName, CommitMode commitMode = CommitMode.Auto)`.
  - `volume` is **positive**. Loss(16) is stored negative and gain(17) positive.
  - Finished contract → `ApplicationException("Contrato encerrado: não é possível alocar.")`.
  - A gain above `AvaiableVolume` → `ApplicationException("The reported volume is greater than the available balance on the purchase contract.")`.
  - A loss has no contract ceiling.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

public class PurchaseContractsAllocationDischargeTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsAllocationCreateService Service() => new(
        _db, new StorageTransactionsGetService(_db, NullLogger<StorageTransactionsGetService>.Instance));

    private async Task<PurchaseContract> SeedContractAsync(decimal total, decimal allocated,
        ContractStatus status = ContractStatus.Approved)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-1", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", Status = status,
            TotalVolume = total, AllocatedVolume = allocated, FreightTerms = FreightTerms.Cif,
        };
        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    private static StorageTransaction NewDischarge(StorageTransactionType type, decimal qty) => new()
    {
        Key = Guid.NewGuid(), Code = "D1", CardCode = "F0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
        WarehouseCode = "PORTO", TransactionType = type, TransactionStatus = StorageTransactionsStatus.Confirmed,
        GrossWeight = qty, NetWeight = qty, AvaiableVolumeToAllocate = qty,
    };

    private async Task<decimal> AllocatedAsync(Guid key) =>
        (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key)).AllocatedVolume;

    [Fact]
    public async Task Loss_is_allocated_negative_and_reopens_the_balance()
    {
        var contract = await SeedContractAsync(total: 1000m, allocated: 1000m);
        var loss = NewDischarge(StorageTransactionType.PurchaseDischargeLoss, 30m);
        _db.Context.StorageTransactions.Add(loss);

        await Service().ExecuteDischargeAsync(contract.Key, loss, 30m, "tester");

        var alloc = await _db.Context.PurchaseContractsAllocations.SingleAsync();
        Assert.Equal(-30m, alloc.Volume);
        Assert.Equal(970m, await AllocatedAsync(contract.Key));
        Assert.Equal(0m, loss.AvaiableVolumeToAllocate);
    }

    [Fact]
    public async Task Gain_is_allocated_positive()
    {
        var contract = await SeedContractAsync(total: 1000m, allocated: 900m);
        var gain = NewDischarge(StorageTransactionType.PurchaseDischargeGain, 20m);
        _db.Context.StorageTransactions.Add(gain);

        await Service().ExecuteDischargeAsync(contract.Key, gain, 20m, "tester");

        Assert.Equal(920m, await AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Gain_above_the_contract_balance_is_refused()
    {
        var contract = await SeedContractAsync(total: 1000m, allocated: 990m);
        var gain = NewDischarge(StorageTransactionType.PurchaseDischargeGain, 20m);
        _db.Context.StorageTransactions.Add(gain);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteDischargeAsync(contract.Key, gain, 20m, "tester"));
    }

    [Fact]
    public async Task Loss_has_no_contract_ceiling()
    {
        var contract = await SeedContractAsync(total: 1000m, allocated: 1000m);
        var loss = NewDischarge(StorageTransactionType.PurchaseDischargeLoss, 50m);
        _db.Context.StorageTransactions.Add(loss);

        await Service().ExecuteDischargeAsync(contract.Key, loss, 50m, "tester");

        Assert.Equal(950m, await AllocatedAsync(contract.Key));
    }

    [Theory]
    [InlineData(StorageTransactionType.PurchaseDischargeLoss)]
    [InlineData(StorageTransactionType.PurchaseDischargeGain)]
    public async Task Finished_contract_is_refused(StorageTransactionType type)
    {
        var contract = await SeedContractAsync(total: 1000m, allocated: 500m, status: ContractStatus.Finished);
        var tx = NewDischarge(type, 10m);
        _db.Context.StorageTransactions.Add(tx);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteDischargeAsync(contract.Key, tx, 10m, "tester"));
        Assert.Contains("encerrado", ex.Message);
    }

    [Fact]
    public async Task Other_types_are_refused()
    {
        var contract = await SeedContractAsync(total: 1000m, allocated: 0m);
        var tx = NewDischarge(StorageTransactionType.Purchase, 10m);
        _db.Context.StorageTransactions.Add(tx);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteDischargeAsync(contract.Key, tx, 10m, "tester"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsAllocationDischargeTests"`
Expected: compile error — `ExecuteDischargeAsync` does not exist.

- [ ] **Step 3: Implement `ExecuteDischargeAsync`**

In `PurchaseContractsAllocationCreateService.cs`, insert before `ApplyAllocationAsync`:

```csharp
    /// <summary>
    /// Troca nota: aloca o romaneio de quebra (<see cref="StorageTransactionType.PurchaseDischargeLoss"/>,
    /// volume NEGATIVO — devolve saldo ao contrato) ou de sobra
    /// (<see cref="StorageTransactionType.PurchaseDischargeGain"/>, volume positivo) gerado pela
    /// Conferência de Entregas. <paramref name="volume"/> chega sempre positivo.
    /// </summary>
    /// <remarks>
    /// Contrato encerrado recusa os DOIS sentidos (decisão do usuário, 19/09/2026: a conferência é
    /// recusada até o contrato ser reaberto) — ao contrário de <see cref="ExecuteReversalAsync"/>.
    /// O teto do saldo do contrato só vale para a sobra; a quebra devolve saldo.
    /// </remarks>
    public async Task ExecuteDischargeAsync(
        Guid purchaseContractKey,
        StorageTransaction discharge,
        decimal volume,
        string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        if (discharge.TransactionType is not (StorageTransactionType.PurchaseDischargeLoss
                                           or StorageTransactionType.PurchaseDischargeGain))
            throw new ApplicationException("Only purchase discharge transactions are supported.");

        if (volume <= 0)
            throw new ApplicationException("Invalid purchase contract allocation volume.");

        if (volume > discharge.AvaiableVolumeToAllocate)
            throw new ApplicationException(
                "The reported volume is greater than the available balance on the delivery note.");

        var purchaseContract = await LoadContractAsync(purchaseContractKey);

        if (purchaseContract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível alocar.");

        var isGain = discharge.TransactionType == StorageTransactionType.PurchaseDischargeGain;

        if (isGain && volume > purchaseContract?.AvaiableVolume)
            throw new ApplicationException(
                "The reported volume is greater than the available balance on the purchase contract.");

        await ApplyAllocationAsync(
            purchaseContractKey, purchaseContract, discharge, isGain ? volume : -volume, userName, commitMode);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsAllocationDischargeTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Show 16/17 in the Entregas query**

This is Dapper over SQL Server with no InMemory test. You verify it in the browser in the frontend
plan.

In `PurchaseContractsGetAllocationsByContractService.cs`, replace the two `CASE` blocks and the
`Origin` `CASE`, then add the joins.

```sql
            CASE
        		 WHEN ST.TransactionType = 8 THEN 'Compra'
        		 WHEN ST.TransactionType = 9 THEN 'Dev.Compra'
        		 WHEN ST.TransactionType = 16 THEN 'Quebra Descarga'
        		 WHEN ST.TransactionType = 17 THEN 'Sobra Descarga'
        	END AS StorageTransactionTypeName,
```

```sql
        	CASE
                WHEN ST.TransactionType = 8 THEN PA.Volume 
                WHEN ST.TransactionType = 9 THEN PA.Volume * -1     
                -- Troca nota: a alocação da quebra já é negativa; exibida positiva com o tipo dizendo
                -- o sentido, como a Dev.Compra.
                WHEN ST.TransactionType = 16 THEN PA.Volume * -1
                WHEN ST.TransactionType = 17 THEN PA.Volume
            END AS Volume, 
```

```sql
            CASE
                WHEN ST.TransactionType = 16 THEN 'DischargeLoss'
                WHEN ST.TransactionType = 17 THEN 'DischargeGain'
                WHEN WRR.[Key] IS NOT NULL THEN 'WarehouseLoss'
                ELSE 'Standard'
            END AS Origin,
            WR.Code AS WarehouseReconciliationCode,
            WRRS.Description AS ReasonDescription,
            DINV.TaxDocumentNumber AS DischargeInvoiceNumber,
            DLOAD.Code AS DischargeLoadCode
```

Append these joins before the `WHERE`:

```sql
        -- Troca nota (romaneios 16/17): a nota de venda conferida e a carga que ela faturou.
        LEFT JOIN SALES_INVOICES_ITEMS DITEM
               ON DITEM.[Key] = ST.DischargeSalesInvoiceItemKey
        LEFT JOIN SALES_INVOICES DINV
               ON DINV.[Key] = DITEM.SalesInvoiceKey
        LEFT JOIN SHIPMENT_LOADS DLOAD
               ON DLOAD.[Key] = DINV.ShipmentLoadKey
```

Before using the table names, check them with
`grep -n "Table(\"SHIPMENT_LOADS\")\|Table(\"SALES_INVOICES_ITEMS\")" -r SiagroB1.Domain/Entities`.

Map the two new columns in the `foreach`:

```csharp
                ReasonDescription = allocation.ReasonDescription,
                DischargeInvoiceNumber = allocation.DischargeInvoiceNumber,
                DischargeLoadCode = allocation.DischargeLoadCode
```

In `PurchaseContractAllocationsByContractDto.cs`, update the `Origin` doc to list `DischargeLoss` /
`DischargeGain` and append:

```csharp
    /// <summary>Troca nota: número fiscal da nota de venda cuja conferência gerou o romaneio 16/17.</summary>
    [JsonPropertyName("DischargeInvoiceNumber")]
    public string? DischargeInvoiceNumber { get; set; }

    /// <summary>Troca nota: código da carga faturada por essa nota.</summary>
    [JsonPropertyName("DischargeLoadCode")]
    public string? DischargeLoadCode { get; set; }
```

- [ ] **Step 6: Build and stage**

Run: `dotnet build SiagroB1.sln` — expected: 0 errors.

```bash
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationCreateService.cs SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsGetAllocationsByContractService.cs SiagroB1.Domain/Dtos/PurchaseContractAllocationsByContractDto.cs SiagroB1.Application.Tests/PurchaseDischarges/PurchaseContractsAllocationDischargeTests.cs
```

---

### Task 3: The split rule (pure)

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseDischarges/PurchaseDischargeAllocationPlanner.cs`
- Test: `SiagroB1.Application.Tests/PurchaseDischarges/PurchaseDischargeAllocationPlannerTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record DischargeShipment(
    Guid ShippingTransactionKey, Guid PurchaseStorageTransactionKey, Guid PurchaseContractKey,
    decimal SalesNetWeight, bool IsCif);

public sealed record DischargeLine(DischargeShipment Shipment, decimal OriginVolume, decimal AdjustmentVolume);

public static IReadOnlyList<DischargeLine> PurchaseDischargeAllocationPlanner.Plan(
    decimal itemQuantity, decimal itemNetQuantity, IReadOnlyList<DischargeShipment> loadShipments);
```

`AdjustmentVolume` is signed: negative is a loss. Lines with `AdjustmentVolume == 0` are dropped.

- [ ] **Step 1: Write the failing tests**

```csharp
using SiagroB1.Application.Services.PurchaseDischarges;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

/// <summary>
/// Spec §2: a diferença do item (líquido − quantidade) é rateada pela participação de cada Expedição
/// no peso da CARGA inteira (CIF ou não); só as CIF viram linha. O resto do arredondamento vai para a
/// maior participação CIF, para a soma das linhas bater com a parte CIF da diferença.
/// </summary>
public class PurchaseDischargeAllocationPlannerTests
{
    private static DischargeShipment Ship(decimal weight, bool cif) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), weight, cif);

    [Fact]
    public void Single_cif_expedition_takes_the_whole_difference()
    {
        var lines = PurchaseDischargeAllocationPlanner.Plan(1000m, 970m, [Ship(1000m, true)]);

        var line = Assert.Single(lines);
        Assert.Equal(-30m, line.AdjustmentVolume);
        Assert.Equal(1000m, line.OriginVolume);
    }

    [Fact]
    public void Gain_is_positive()
    {
        var lines = PurchaseDischargeAllocationPlanner.Plan(1000m, 1012m, [Ship(1000m, true)]);

        Assert.Equal(12m, Assert.Single(lines).AdjustmentVolume);
    }

    [Fact]
    public void Fob_expeditions_dilute_but_get_no_line()
    {
        var cif = Ship(600m, true);
        var lines = PurchaseDischargeAllocationPlanner.Plan(1000m, 900m, [cif, Ship(400m, false)]);

        var line = Assert.Single(lines);
        Assert.Same(cif, line.Shipment);
        Assert.Equal(-60m, line.AdjustmentVolume);
        Assert.Equal(600m, line.OriginVolume);
    }

    [Fact]
    public void Rounding_remainder_goes_to_the_largest_cif_share()
    {
        var a = Ship(1m, true);
        var b = Ship(1m, true);
        var c = Ship(1m, true);

        var lines = PurchaseDischargeAllocationPlanner.Plan(100m, 99.999m, [a, b, c]);

        Assert.Equal(-0.001m, lines.Sum(l => l.AdjustmentVolume));
        Assert.Equal(100m, lines.Sum(l => l.OriginVolume));
    }

    [Theory]
    [InlineData(1000, 1000)]
    public void No_difference_no_lines(decimal quantity, decimal net)
    {
        Assert.Empty(PurchaseDischargeAllocationPlanner.Plan(quantity, net, [Ship(1000m, true)]));
    }

    [Fact]
    public void No_cif_expedition_no_lines()
    {
        Assert.Empty(PurchaseDischargeAllocationPlanner.Plan(1000m, 970m, [Ship(1000m, false)]));
    }

    [Fact]
    public void Empty_load_no_lines()
    {
        Assert.Empty(PurchaseDischargeAllocationPlanner.Plan(1000m, 970m, []));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseDischargeAllocationPlannerTests"`
Expected: compile error — the namespace and types do not exist.

- [ ] **Step 3: Implement**

```csharp
namespace SiagroB1.Application.Services.PurchaseDischarges;

/// <summary>Uma Expedição da carga, já resolvida até o contrato de compra.</summary>
public sealed record DischargeShipment(
    Guid ShippingTransactionKey,
    Guid PurchaseStorageTransactionKey,
    Guid PurchaseContractKey,
    decimal SalesNetWeight,
    bool IsCif);

/// <summary>Parte da diferença do item que cabe a uma Expedição CIF. Negativo = quebra.</summary>
public sealed record DischargeLine(DischargeShipment Shipment, decimal OriginVolume, decimal AdjustmentVolume);

/// <summary>
/// Troca nota (spec §2): rateia a diferença do item conferido entre as Expedições da carga pela
/// participação no peso da carga INTEIRA; só as de contrato CIF viram linha — a parte da FOB é da
/// Yokotobi. O resto do arredondamento vai para a maior participação CIF.
/// </summary>
public static class PurchaseDischargeAllocationPlanner
{
    public static IReadOnlyList<DischargeLine> Plan(
        decimal itemQuantity, decimal itemNetQuantity, IReadOnlyList<DischargeShipment> loadShipments)
    {
        var diff = itemNetQuantity - itemQuantity;
        var totalWeight = loadShipments.Sum(s => s.SalesNetWeight);
        var cif = loadShipments.Where(s => s.IsCif && s.SalesNetWeight > 0).ToList();

        if (diff == 0 || totalWeight <= 0 || cif.Count == 0)
            return [];

        var cifWeight = cif.Sum(s => s.SalesNetWeight);
        var cifDiff = Round(diff * cifWeight / totalWeight);
        var cifOrigin = Round(itemQuantity * cifWeight / totalWeight);

        var lines = cif
            .Select(s => new DischargeLine(
                s,
                Round(itemQuantity * s.SalesNetWeight / totalWeight),
                Round(diff * s.SalesNetWeight / totalWeight)))
            .ToList();

        var largest = lines.MaxBy(l => l.Shipment.SalesNetWeight)!;
        var index = lines.IndexOf(largest);
        lines[index] = largest with
        {
            OriginVolume = largest.OriginVolume + (cifOrigin - lines.Sum(l => l.OriginVolume)),
            AdjustmentVolume = largest.AdjustmentVolume + (cifDiff - lines.Sum(l => l.AdjustmentVolume)),
        };

        return lines.Where(l => l.AdjustmentVolume != 0).ToList();
    }

    private static decimal Round(decimal value) => decimal.Round(value, 3, MidpointRounding.ToEven);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseDischargeAllocationPlannerTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Stage**

```bash
git add SiagroB1.Application/Services/PurchaseDischarges/PurchaseDischargeAllocationPlanner.cs SiagroB1.Application.Tests/PurchaseDischarges/PurchaseDischargeAllocationPlannerTests.cs
```

---

### Task 4: Sync service (prepare with guards, apply)

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseDischarges/PurchaseDischargeTransactionsSyncService.cs`
- Create: `SiagroB1.Application.Tests/PurchaseDischarges/PurchaseDischargeTestContext.cs`
- Test: `SiagroB1.Application.Tests/PurchaseDischarges/PurchaseDischargeTransactionsSyncServiceTests.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (DI, next to line 317)

**Interfaces:**
- Consumes: `PurchaseDischargeAllocationPlanner.Plan` (Task 3), `ExecuteDischargeAsync` (Task 2),
  `StorageTransactionsCreateService.ExecuteAsync(entity, user, TransactionCode, CommitMode)`, and
  `PurchaseContractsAllocationDeleteService.ExecuteAsync(Guid allocationKey, string user, CommitMode)`.
- Produces:

```csharp
public sealed class PurchaseDischargeSyncPlan
{
    public required Guid SalesInvoiceItemKey { get; init; }
    public required IReadOnlyList<Guid> TransactionsToCancel { get; init; }
    public required IReadOnlyList<DischargeLine> NewLines { get; init; }
    public required string? InvoiceNumber { get; init; }
    public required string? LoadCode { get; init; }
    public bool IsEmpty => TransactionsToCancel.Count == 0 && NewLines.Count == 0;
}

public Task<PurchaseDischargeSyncPlan> PrepareAsync(
    Guid salesInvoiceItemKey, SalesInvoiceDeliveryStatus newStatus,
    decimal quantity, decimal deliveredQuantity, decimal quantityLoss);   // reads + guards, NO writes

public Task ApplyAsync(PurchaseDischargeSyncPlan plan, string userName);  // cancels + creates + allocates, saves per line
```

Guard messages (`DefaultException`, thrown by `PrepareAsync`):
- `$"O contrato de compra {code} está encerrado; reabra-o para conferir esta entrega."`
- `$"A sobra de descarga ({volume:N3}) excede o saldo do contrato de compra {code}."`

- [ ] **Step 1: Write the test context**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.PurchaseDischarges;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

/// <summary>
/// Monta uma carga faturada numa nota Normal, com N Expedições (par Compra 8 alocada + Saída 7 na
/// carga + linha em SHIPPING_TRANSACTIONS), cada uma no seu contrato de compra.
/// </summary>
internal sealed class PurchaseDischargeTestContext
{
    public UnitOfWork Db { get; } = TestDb.CreateUnitOfWork();
    public ShipmentLoad Load { get; private set; } = null!;
    public SalesInvoice Invoice { get; private set; } = null!;
    public SalesInvoiceItem Item { get; private set; } = null!;
    private int _seq;

    public StorageTransactionsCreateService StorageCreate() =>
        new(Db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new() { ["F0001"] = "Cerealista A", ["F0002"] = "Cerealista B" }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { ["PORTO"] = "Porto" }),
            new ShipmentReleasesRecalculateShippedService(Db.Context),
            new ShipmentReleaseMovementGuardService(Db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    public PurchaseDischargeTransactionsSyncService Sync() =>
        new(Db,
            StorageCreate(),
            new PurchaseContractsAllocationCreateService(Db,
                new StorageTransactionsGetService(Db, NullLogger<StorageTransactionsGetService>.Instance)),
            new PurchaseContractsAllocationDeleteService(Db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance));

    public async Task SeedLoadAndInvoiceAsync(decimal itemQuantity)
    {
        Load = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000001", BranchCode = "01", ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG", TruckCode = "ABC1D23",
            WarehouseCode = "PORTO", LoadType = ShipmentLoadType.Normal, Status = ShipmentLoadStatus.Invoiced,
        };
        Invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C0001", InvoiceNumber = "NF-1", ShipmentLoadKey = Load.Key,
            InvoiceStatus = InvoiceStatus.Confirmed, InvoiceType = SalesInvoiceType.Normal,
        };
        Item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = Invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            Quantity = itemQuantity,
        };
        Db.Context.ShipmentLoads.Add(Load);
        Db.Context.SalesInvoices.Add(Invoice);
        Db.Context.SalesInvoicesItems.Add(Item);
        await Db.Context.SaveChangesAsync();
    }

    public async Task<PurchaseContract> SeedContractAsync(
        FreightTerms freight = FreightTerms.Cif, decimal total = 10_000m,
        ContractStatus status = ContractStatus.Approved, string cardCode = "F0001")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = $"PC-{++_seq}", CardCode = cardCode, ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", Status = status,
            TotalVolume = total, FreightTerms = freight,
        };
        Db.Context.PurchaseContracts.Add(contract);
        await Db.Context.SaveChangesAsync();
        return contract;
    }

    /// <summary>O que a Expedição grava: 8 alocada no contrato + 7 na carga + par em SHIPPING_TRANSACTIONS.</summary>
    public async Task<ShippingTransaction> SeedExpeditionAsync(PurchaseContract contract, decimal weight)
    {
        var purchase = NewTx(StorageTransactionType.Purchase, contract.CardCode, weight);
        var sales = NewTx(StorageTransactionType.SalesShipment, contract.CardCode, weight);
        sales.ShipmentLoadKey = Load.Key;
        var shipping = new ShippingTransaction
        {
            Key = Guid.NewGuid(), PurchaseStorageTransactionKey = purchase.Key, SalesStorageTransactionKey = sales.Key,
        };

        Db.Context.StorageTransactions.AddRange(purchase, sales);
        Db.Context.ShippingTransactions.Add(shipping);
        Db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contract.Key, StorageTransactionKey = purchase.Key, Volume = weight,
        });
        var tracked = await Db.Context.PurchaseContracts.SingleAsync(x => x.Key == contract.Key);
        tracked.AllocatedVolume += weight;
        await Db.Context.SaveChangesAsync();
        return shipping;
    }

    private StorageTransaction NewTx(StorageTransactionType type, string cardCode, decimal weight) => new()
    {
        Key = Guid.NewGuid(), Code = $"SEED-{++_seq:0000}", BranchCode = "01", CardCode = cardCode,
        ItemCode = "SOJA", UnitOfMeasureCode = "KG", WarehouseCode = "PORTO", TruckCode = "ABC1D23",
        TransactionType = type, TransactionStatus = StorageTransactionsStatus.Confirmed,
        GrossWeight = weight, NetWeight = weight,
    };

    public Task<List<StorageTransaction>> DischargesAsync() =>
        Db.Context.StorageTransactions.AsNoTracking()
            .Where(x => x.TransactionType == StorageTransactionType.PurchaseDischargeLoss ||
                        x.TransactionType == StorageTransactionType.PurchaseDischargeGain)
            .ToListAsync();

    public async Task<decimal> AllocatedAsync(Guid contractKey) =>
        (await Db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contractKey)).AllocatedVolume;

    /// <summary>Atalho: Prepare + Apply como o gancho faz.</summary>
    public async Task SyncAsync(SalesInvoiceDeliveryStatus status, decimal delivered, decimal loss = 0m)
    {
        var sync = Sync();
        var plan = await sync.PrepareAsync(Item.Key!.Value, status, Item.Quantity, delivered, loss);
        await sync.ApplyAsync(plan, "tester");
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

public class PurchaseDischargeTransactionsSyncServiceTests
{
    private readonly PurchaseDischargeTestContext _ctx = new();

    [Fact]
    public async Task Closing_with_loss_creates_a_16_allocated_negative_on_the_cif_contract()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        var shipping = await _ctx.SeedExpeditionAsync(contract, 1000m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 980m, loss: 10m);

        var tx = Assert.Single(await _ctx.DischargesAsync());
        Assert.Equal(StorageTransactionType.PurchaseDischargeLoss, tx.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, tx.TransactionStatus);
        Assert.Equal(TransactionCode.PurchaseDischarge, tx.TransactionOrigin);
        Assert.Equal(30m, tx.NetWeight);
        Assert.Equal("F0001", tx.CardCode);
        Assert.Equal(_ctx.Item.Key, tx.DischargeSalesInvoiceItemKey);
        Assert.Equal(shipping.Key, tx.DischargeShippingTransactionKey);
        Assert.Null(tx.ShipmentLoadKey);
        Assert.Null(tx.ShipmentReleaseKey);
        Assert.Null(tx.SalesInvoiceKey);
        Assert.Equal(970m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Warehouse_balance_is_untouched()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);

        Assert.Equal(0m, await StorageTransactionsWarehouseBalanceService.CalculateAsync(_ctx.Db.Context, "PORTO", "SOJA"));
    }

    [Fact]
    public async Task Closing_with_gain_creates_a_17()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 1012m);

        var tx = Assert.Single(await _ctx.DischargesAsync());
        Assert.Equal(StorageTransactionType.PurchaseDischargeGain, tx.TransactionType);
        Assert.Equal(1012m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Fob_contract_gets_nothing()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync(FreightTerms.Fob);
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);

        Assert.Empty(await _ctx.DischargesAsync());
        Assert.Equal(1000m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Two_cif_and_one_fob_split_by_load_weight()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var a = await _ctx.SeedContractAsync();
        var b = await _ctx.SeedContractAsync(cardCode: "F0002");
        var fob = await _ctx.SeedContractAsync(FreightTerms.Fob);
        await _ctx.SeedExpeditionAsync(a, 500m);
        await _ctx.SeedExpeditionAsync(b, 300m);
        await _ctx.SeedExpeditionAsync(fob, 200m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 950m);

        Assert.Equal(2, (await _ctx.DischargesAsync()).Count);
        Assert.Equal(475m, await _ctx.AllocatedAsync(a.Key));   // 500 − 25
        Assert.Equal(285m, await _ctx.AllocatedAsync(b.Key));   // 300 − 15
        Assert.Equal(200m, await _ctx.AllocatedAsync(fob.Key)); // FOB: quebra é da Yokotobi
    }

    [Fact]
    public async Task Two_expeditions_on_the_same_contract_sum_right()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 600m);
        await _ctx.SeedExpeditionAsync(contract, 400m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 950m);

        Assert.Equal(950m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Reopening_cancels_and_restores()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Open, delivered: 970m);

        var tx = Assert.Single(await _ctx.DischargesAsync());
        Assert.Equal(StorageTransactionsStatus.Cancelled, tx.TransactionStatus);
        Assert.Empty(await _ctx.Db.Context.PurchaseContractsAllocations
            .Where(x => x.StorageTransactionKey == tx.Key).ToListAsync());
        Assert.Equal(1000m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Reclosing_with_another_weight_replaces()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);

        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 990m);

        var active = (await _ctx.DischargesAsync())
            .Where(x => x.TransactionStatus != StorageTransactionsStatus.Cancelled).ToList();
        Assert.Equal(10m, Assert.Single(active).NetWeight);
        Assert.Equal(990m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Finished_contract_refuses_before_any_write()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync(status: ContractStatus.Finished);
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            _ctx.Sync().PrepareAsync(_ctx.Item.Key!.Value, SalesInvoiceDeliveryStatus.Closed, 1000m, 970m, 0m));

        Assert.Contains(contract.Code!, ex.Message);
        Assert.Contains("encerrado", ex.Message);
        Assert.Empty(await _ctx.DischargesAsync());
    }

    [Fact]
    public async Task Reopening_on_a_finished_contract_is_refused_too()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);
        var tracked = await _ctx.Db.Context.PurchaseContracts.SingleAsync(x => x.Key == contract.Key);
        tracked.Status = ContractStatus.Finished;
        await _ctx.Db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            _ctx.Sync().PrepareAsync(_ctx.Item.Key!.Value, SalesInvoiceDeliveryStatus.Open, 1000m, 970m, 0m));
    }

    [Fact]
    public async Task Gain_above_contract_balance_refuses_before_any_write()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync(total: 1000m);
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            _ctx.Sync().PrepareAsync(_ctx.Item.Key!.Value, SalesInvoiceDeliveryStatus.Closed, 1000m, 1012m, 0m));

        Assert.Contains("sobra de descarga", ex.Message);
        Assert.Empty(await _ctx.DischargesAsync());
    }

    [Fact]
    public async Task Return_invoice_items_are_ignored()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        var invoice = await _ctx.Db.Context.SalesInvoices.SingleAsync();
        invoice.InvoiceType = SalesInvoiceType.Return;
        await _ctx.Db.Context.SaveChangesAsync();

        var plan = await _ctx.Sync().PrepareAsync(_ctx.Item.Key!.Value, SalesInvoiceDeliveryStatus.Closed, 1000m, 970m, 0m);

        Assert.True(plan.IsEmpty);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseDischargeTransactionsSyncServiceTests"`
Expected: compile error — `PurchaseDischargeTransactionsSyncService` does not exist.

- [ ] **Step 4: Implement**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.PurchaseDischarges;

public sealed class PurchaseDischargeSyncPlan
{
    public required Guid SalesInvoiceItemKey { get; init; }
    public required IReadOnlyList<Guid> TransactionsToCancel { get; init; }
    public required IReadOnlyList<DischargeLine> NewLines { get; init; }
    public required string? InvoiceNumber { get; init; }
    public required string? LoadCode { get; init; }
    public bool IsEmpty => TransactionsToCancel.Count == 0 && NewLines.Count == 0;
}

/// <summary>
/// Troca nota (spec §3): mantém os romaneios de quebra/sobra de descarga (16/17) de um item em
/// sincronia com a Conferência de Entregas dele. Dono único desses romaneios.
/// </summary>
/// <remarks>
/// Dois passos, porque o gancho precisa recusar ANTES de gravar o item:
/// <list type="bullet">
/// <item><see cref="PrepareAsync"/> lê e roda todas as travas, sem gravar nada;</item>
/// <item><see cref="ApplyAsync"/> cancela os romaneios ativos do item (removendo as alocações) e,
/// se a conferência ficou encerrada, cria e aloca os novos — com flush por linha, porque
/// <c>ApplyAllocationAsync</c> soma as alocações PERSISTIDAS.</item>
/// </list>
/// Nunca preenche ShipmentLoadKey/ShipmentReleaseKey/SalesInvoiceKey: os romaneios 16/17 não podem
/// entrar nos saldos da carga, da liberação nem da nota.
/// </remarks>
public class PurchaseDischargeTransactionsSyncService(
    IUnitOfWork db,
    StorageTransactionsCreateService storageCreate,
    PurchaseContractsAllocationCreateService allocationCreate,
    PurchaseContractsAllocationDeleteService allocationDelete)
{
    public async Task<PurchaseDischargeSyncPlan> PrepareAsync(
        Guid salesInvoiceItemKey,
        SalesInvoiceDeliveryStatus newStatus,
        decimal quantity,
        decimal deliveredQuantity,
        decimal quantityLoss)
    {
        var ctx = db.Context;

        var invoice = await (
                from i in ctx.SalesInvoicesItems.AsNoTracking()
                where i.Key == salesInvoiceItemKey
                join inv in ctx.SalesInvoices.AsNoTracking() on i.SalesInvoiceKey equals inv.Key
                select new { inv.InvoiceType, inv.ShipmentLoadKey, inv.InvoiceNumber })
            .SingleOrDefaultAsync();

        var active = await ctx.StorageTransactions.AsNoTracking()
            .Where(x => x.DischargeSalesInvoiceItemKey == salesInvoiceItemKey &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .Select(x => x.Key)
            .ToListAsync();

        var oldAllocations = await ctx.PurchaseContractsAllocations.AsNoTracking()
            .Where(a => active.Contains(a.StorageTransactionKey))
            .Select(a => new { a.PurchaseContractKey, a.Volume })
            .ToListAsync();

        IReadOnlyList<DischargeLine> lines = [];
        string? loadCode = null;

        if (invoice is { InvoiceType: SalesInvoiceType.Normal, ShipmentLoadKey: not null } &&
            newStatus == SalesInvoiceDeliveryStatus.Closed)
        {
            var loadKey = invoice.ShipmentLoadKey.Value;
            loadCode = await ctx.ShipmentLoads.AsNoTracking()
                .Where(x => x.Key == loadKey).Select(x => x.Code).FirstOrDefaultAsync();

            var shipments = await LoadShipmentsAsync(loadKey);
            lines = PurchaseDischargeAllocationPlanner.Plan(quantity, deliveredQuantity - quantityLoss, shipments);
        }

        // Travas: sobre TODO contrato mexido — os que perdem romaneio antigo e os que ganham novo.
        var contractKeys = oldAllocations.Select(a => a.PurchaseContractKey)
            .Concat(lines.Select(l => l.Shipment.PurchaseContractKey))
            .Distinct()
            .ToList();

        var contracts = await ctx.PurchaseContracts.AsNoTracking()
            .Where(c => contractKeys.Contains(c.Key))
            .ToDictionaryAsync(c => c.Key);

        foreach (var contract in contracts.Values)
        {
            if (contract.Status == ContractStatus.Finished)
                throw new DefaultException(
                    $"O contrato de compra {contract.Code} está encerrado; reabra-o para conferir esta entrega.");

            var gain = lines
                .Where(l => l.Shipment.PurchaseContractKey == contract.Key && l.AdjustmentVolume > 0)
                .Sum(l => l.AdjustmentVolume);

            if (gain <= 0) continue;

            // Saldo depois de cancelar os antigos (remover alocação v devolve v) e aplicar os novos.
            var released = oldAllocations.Where(a => a.PurchaseContractKey == contract.Key).Sum(a => a.Volume);
            var applied = lines.Where(l => l.Shipment.PurchaseContractKey == contract.Key).Sum(l => l.AdjustmentVolume);

            if (contract.AvaiableVolume + released - applied < 0)
                throw new DefaultException(
                    $"A sobra de descarga ({gain:N3}) excede o saldo do contrato de compra {contract.Code}.");
        }

        return new PurchaseDischargeSyncPlan
        {
            SalesInvoiceItemKey = salesInvoiceItemKey,
            TransactionsToCancel = active,
            NewLines = lines,
            InvoiceNumber = invoice?.InvoiceNumber,
            LoadCode = loadCode,
        };
    }

    public async Task ApplyAsync(PurchaseDischargeSyncPlan plan, string userName)
    {
        var ctx = db.Context;

        foreach (var key in plan.TransactionsToCancel)
        {
            var allocationKeys = await ctx.PurchaseContractsAllocations
                .Where(a => a.StorageTransactionKey == key)
                .Select(a => a.Key)
                .ToListAsync();

            foreach (var allocationKey in allocationKeys)
                await allocationDelete.ExecuteAsync(allocationKey, userName, CommitMode.Auto);

            var tx = await ctx.StorageTransactions.SingleAsync(x => x.Key == key);
            tx.TransactionStatus = StorageTransactionsStatus.Cancelled;
            tx.UpdatedAt = DateTime.Now;
            tx.UpdatedBy = userName;
            await db.SaveChangesAsync();
        }

        foreach (var line in plan.NewLines)
        {
            var purchase = await ctx.StorageTransactions.AsNoTracking()
                .SingleAsync(x => x.Key == line.Shipment.PurchaseStorageTransactionKey);

            var volume = decimal.Abs(line.AdjustmentVolume);
            var isGain = line.AdjustmentVolume > 0;

            var discharge = new StorageTransaction
            {
                TransactionType = isGain
                    ? StorageTransactionType.PurchaseDischargeGain
                    : StorageTransactionType.PurchaseDischargeLoss,
                TransactionStatus = StorageTransactionsStatus.Confirmed,
                TransactionDate = DateTime.Now.Date,
                BranchCode = purchase.BranchCode,
                CardCode = purchase.CardCode,
                ItemCode = purchase.ItemCode,
                UnitOfMeasureCode = purchase.UnitOfMeasureCode,
                WarehouseCode = purchase.WarehouseCode,
                TruckCode = purchase.TruckCode,
                ProcessingCostCode = purchase.ProcessingCostCode,
                GrossWeight = volume,
                NetWeight = volume,
                AvaiableVolumeToAllocate = volume,
                DischargeSalesInvoiceItemKey = plan.SalesInvoiceItemKey,
                DischargeShippingTransactionKey = line.Shipment.ShippingTransactionKey,
                Comments = $"{(isGain ? "Sobra" : "Quebra")} de descarga — nota {plan.InvoiceNumber}, carga {plan.LoadCode}",
            };

            await storageCreate.ExecuteAsync(discharge, userName, TransactionCode.PurchaseDischarge, CommitMode.Deferred);
            await allocationCreate.ExecuteDischargeAsync(
                line.Shipment.PurchaseContractKey, discharge, volume, userName, CommitMode.Deferred);

            // Flush por linha: a próxima pode cair no MESMO contrato, e a soma lê o banco.
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Saídas(7) ativas da carga → par em SHIPPING_TRANSACTIONS → Compra(8) → alocação → contrato.
    /// Saída sem perna de compra (ou 8 sem alocação) pesa na carga, mas nunca é CIF.
    /// </summary>
    private async Task<IReadOnlyList<DischargeShipment>> LoadShipmentsAsync(Guid loadKey)
    {
        var ctx = db.Context;

        var rows = await (
                from sales in ctx.StorageTransactions.AsNoTracking()
                where sales.ShipmentLoadKey == loadKey &&
                      sales.TransactionType == StorageTransactionType.SalesShipment &&
                      sales.TransactionStatus != StorageTransactionsStatus.Cancelled
                join sht in ctx.ShippingTransactions.AsNoTracking()
                    on sales.Key equals sht.SalesStorageTransactionKey into shts
                from sht in shts.DefaultIfEmpty()
                select new
                {
                    sales.NetWeight,
                    ShippingKey = sht == null ? (Guid?)null : sht.Key,
                    PurchaseKey = sht == null ? null : sht.PurchaseStorageTransactionKey,
                })
            .ToListAsync();

        var purchaseKeys = rows.Where(r => r.PurchaseKey != null).Select(r => r.PurchaseKey!.Value).ToList();

        var contractsByPurchase = (await (
                    from a in ctx.PurchaseContractsAllocations.AsNoTracking()
                    where purchaseKeys.Contains(a.StorageTransactionKey)
                    join c in ctx.PurchaseContracts.AsNoTracking() on a.PurchaseContractKey equals c.Key
                    select new { a.StorageTransactionKey, ContractKey = c.Key, c.FreightTerms })
                .ToListAsync())
            .GroupBy(x => x.StorageTransactionKey)
            .ToDictionary(g => g.Key, g => g.First());

        return rows.Select(r =>
        {
            var contract = r.PurchaseKey != null && contractsByPurchase.TryGetValue(r.PurchaseKey.Value, out var c)
                ? c
                : null;

            return new DischargeShipment(
                r.ShippingKey ?? Guid.Empty,
                r.PurchaseKey ?? Guid.Empty,
                contract?.ContractKey ?? Guid.Empty,
                r.NetWeight,
                IsCif: contract is { FreightTerms: FreightTerms.Cif } && r.ShippingKey != null);
        }).ToList();
    }
}
```

- [ ] **Step 5: Register in DI**

In `ServiceCollectionExtensions.cs`, next to `services.AddScoped<SalesInvoicesItemsUpdateService>();`:

```csharp
        services.AddScoped<PurchaseDischargeTransactionsSyncService>();
```

Add `using SiagroB1.Application.Services.PurchaseDischarges;` at the top.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseDischarge"`
Expected: PASS (all tests from Tasks 1–4).

- [ ] **Step 7: Stage**

```bash
git add SiagroB1.Application/Services/PurchaseDischarges SiagroB1.Application.Tests/PurchaseDischarges SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs
```

---

### Task 5: Hook into the delivery reconciliation

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsUpdateService.cs`
- Modify: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesItemsUpdateServiceTests.cs` (`Service()` factory)
- Modify: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesReturnWeightTests.cs` (its factory for the same service)
- Test: `SiagroB1.Application.Tests/PurchaseDischarges/SalesInvoicesItemsUpdateDischargeTests.cs` (create)

**Interfaces:**
- Consumes: `PurchaseDischargeTransactionsSyncService.PrepareAsync` / `ApplyAsync` (Task 4).
- Produces: the new constructor
  `SalesInvoicesItemsUpdateService(IUnitOfWork db, IItemService itemService, PurchaseDischargeTransactionsSyncService dischargeSync, ILogger<SalesInvoicesUpdateService> logger)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

/// <summary>
/// O gancho pelo caminho do usuário: PATCH da Conferência de Entregas (entidade rastreada e mutada,
/// como o controller entrega).
/// </summary>
public class SalesInvoicesItemsUpdateDischargeTests
{
    private readonly PurchaseDischargeTestContext _ctx = new();

    private SalesInvoicesItemsUpdateService Service() => new(
        _ctx.Db,
        new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
        _ctx.Sync(),
        new TestLogger<SalesInvoicesUpdateService>());

    private async Task<SalesInvoiceItem> PatchAsync(SalesInvoiceDeliveryStatus status, decimal delivered, decimal loss = 0m)
    {
        var tracked = await _ctx.Db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == _ctx.Item.Key);
        tracked.DeliveredQuantity = delivered;
        tracked.QuantityLoss = loss;
        tracked.DeliveryStatus = status;
        await Service().ExecuteAsync(_ctx.Item.Key!.Value, tracked, "tester");
        return tracked;
    }

    [Fact]
    public async Task Closing_creates_the_discharge_and_adjusts_the_purchase_contract()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        await PatchAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 980m, loss: 10m);

        Assert.Single(await _ctx.DischargesAsync());
        Assert.Equal(970m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Reopening_cancels_the_discharge()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        await PatchAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);

        await PatchAsync(SalesInvoiceDeliveryStatus.Open, delivered: 970m);

        Assert.All(await _ctx.DischargesAsync(),
            x => Assert.Equal(StorageTransactionsStatus.Cancelled, x.TransactionStatus));
        Assert.Equal(1000m, await _ctx.AllocatedAsync(contract.Key));
    }

    [Fact]
    public async Task Finished_contract_refuses_and_the_item_is_not_saved()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync(status: ContractStatus.Finished);
        await _ctx.SeedExpeditionAsync(contract, 1000m);

        await Assert.ThrowsAsync<DefaultException>(() =>
            PatchAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m));

        var stored = await _ctx.Db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(x => x.Key == _ctx.Item.Key);
        Assert.Equal(SalesInvoiceDeliveryStatus.Open, stored.DeliveryStatus);
        Assert.Empty(await _ctx.DischargesAsync());
    }
}
```

The last test reads with `AsNoTracking`. InMemory keeps the unsaved tracked mutation out of the
store, so this proves nothing was flushed.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesItemsUpdateDischargeTests"`
Expected: compile error — the constructor takes 3 arguments.

- [ ] **Step 3: Wire the hook**

In `SalesInvoicesItemsUpdateService.cs`, change the constructor:

```csharp
public class SalesInvoicesItemsUpdateService(
    IUnitOfWork db, 
    IItemService itemService,
    PurchaseDischargeTransactionsSyncService dischargeSync,
    ILogger<SalesInvoicesUpdateService> logger)
```

Add `using SiagroB1.Application.Services.PurchaseDischarges;`.

Right after the "net ≤ 0" guard and before `var logs = ...`:

```csharp
            // Troca nota: as travas do contrato de compra (encerrado, sobra acima do saldo) rodam
            // AQUI, antes de qualquer gravação — recusar depois deixaria a conferência encerrada
            // sem o acerto do fornecedor.
            var dischargePlan = deliveryChanged
                ? await dischargeSync.PrepareAsync(
                    key, entity.DeliveryStatus, entity.Quantity, entity.DeliveredQuantity, entity.QuantityLoss)
                : null;

            // Daqui em diante tudo é uma coisa só: conferência, recálculos da venda e acerto da compra.
            await db.BeginTransactionAsync();
```

At the end of the `if (deliveryChanged)` block, after the release recalculation's
`await db.SaveChangesAsync();`:

```csharp
                if (dischargePlan is { IsEmpty: false })
                    await dischargeSync.ApplyAsync(dischargePlan, userName);
```

After the `if (deliveryChanged) { ... }` block (still inside `try`):

```csharp
            await db.CommitAsync();
```

Replace the catch section with:

```csharp
        catch (DbUpdateConcurrencyException)
        {
            await db.RollbackAsync();
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new DefaultException("Error updating entity due to concurrency issues.");
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
```

`RollbackAsync` before `BeginTransactionAsync` (a guard throwing early) is a no-op. That is the
documented behavior in memory "unitofwork-rollback-silent-noop". Confirm it by reading
`SiagroB1.Infra/UnitOfWork.cs` before relying on it. If it throws instead, track a
`var began = false;` flag and roll back only when it is true.

- [ ] **Step 4: Update the two existing factories**

In `SalesInvoicesItemsUpdateServiceTests.cs` and `SalesInvoicesReturnWeightTests.cs`, the
`SalesInvoicesItemsUpdateService` factory gets the sync service as the 3rd argument, built over the
same `_db`:

```csharp
    private SalesInvoicesItemsUpdateService Service() => new(
        _db,
        new FakeItemService(new Dictionary<string, string>
        {
            ["SOJA"] = "SOJA EM GRAOS",
            ["MILHO"] = "MILHO EM GRAOS",
        }),
        new PurchaseDischargeTransactionsSyncService(
            _db,
            new StorageTransactionsCreateService(_db, new FakeDocNumberSequenceService(),
                new FakeBusinessPartnerService(new()), new FakeItemService(new()), new FakeWarehouseService(new()),
                new ShipmentReleasesRecalculateShippedService(_db.Context),
                new ShipmentReleaseMovementGuardService(_db.Context),
                NullLogger<StorageTransactionsCreateService>.Instance),
            new PurchaseContractsAllocationCreateService(_db,
                new StorageTransactionsGetService(_db, NullLogger<StorageTransactionsGetService>.Instance)),
            new PurchaseContractsAllocationDeleteService(_db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance)),
        new TestLogger<SalesInvoicesUpdateService>());
```

Keep each file's own `FakeItemService` dictionary as it is today. Add the needed `using`s. Those
tests seed items without an invoice or load, so the plan is always empty and their behavior does not
change.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoices|FullyQualifiedName~PurchaseDischarge"`
Expected: PASS, including every pre-existing `SalesInvoicesItemsUpdateServiceTests` and
`SalesInvoicesReturnWeightTests`.

- [ ] **Step 6: Stage**

```bash
git add SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsUpdateService.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesItemsUpdateServiceTests.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesReturnWeightTests.cs SiagroB1.Application.Tests/PurchaseDischarges/SalesInvoicesItemsUpdateDischargeTests.cs
```

---

### Task 6: Guards — invoice/item delete, invoice cancel, Expedição reverse

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseDischarges/PurchaseDischargeGuards.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesDeleteService.cs` (next to the GAC-1171 discharge-ticket guard)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsDeleteService.cs` (same place)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCancelService.cs` (after the "possui retorno" guard, ~line 34, before any write)
- Modify: `SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsReverseService.cs` (with the other guards, before line ~90)
- Test: `SiagroB1.Application.Tests/PurchaseDischarges/PurchaseDischargeGuardsTests.cs` (create)

**Interfaces:**
- Produces:

```csharp
public static class PurchaseDischargeGuards
{
    public const string InvoiceMessage =
        "Este documento de saída tem quebra/sobra de descarga lançada no contrato de compra. Reabra a conferência de entregas antes.";
    public const string ItemMessage =
        "Este item tem quebra/sobra de descarga lançada no contrato de compra. Reabra a conferência de entregas antes.";
    public const string ShippingMessage =
        "Esta Expedição tem quebra/sobra de descarga lançada no contrato de compra. Reabra a conferência de entregas antes.";

    public static Task<bool> HasActiveForItemAsync(AppDbContext ctx, Guid salesInvoiceItemKey);
    public static Task<bool> HasActiveForInvoiceAsync(AppDbContext ctx, Guid salesInvoiceKey);
    public static Task<bool> HasActiveForShippingAsync(AppDbContext ctx, Guid shippingTransactionKey);
}
```

- [ ] **Step 1: Write the failing tests**

Build the services under test the way their existing tests do. Read
`SalesInvoicesDeleteDischargeGuardTests.cs` for delete/item-delete, and the existing cancel and
reverse tests (`grep -rl "SalesInvoicesCancelService\|ShippingTransactionsReverseService" SiagroB1.Application.Tests`)
for the constructor arguments. Reuse `PurchaseDischargeTestContext` to seed. After `SyncAsync(...)`
the item has an active 16.

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseDischarges;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

/// <summary>
/// FKs NoAction + InMemory sem FK: é a trava, e não o banco, que estes testes verificam (mesma
/// lição do GAC-1171). Romaneio 16/17 CANCELADO não trava nada.
/// </summary>
public class PurchaseDischargeGuardsTests
{
    private readonly PurchaseDischargeTestContext _ctx = new();

    private async Task<Guid> SeedClosedAsync()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        var shipping = await _ctx.SeedExpeditionAsync(contract, 1000m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);
        return shipping.Key;
    }

    [Fact]
    public async Task Active_discharge_is_detected_by_item_invoice_and_shipping()
    {
        var shippingKey = await SeedClosedAsync();

        Assert.True(await PurchaseDischargeGuards.HasActiveForItemAsync(_ctx.Db.Context, _ctx.Item.Key!.Value));
        Assert.True(await PurchaseDischargeGuards.HasActiveForInvoiceAsync(_ctx.Db.Context, _ctx.Invoice.Key));
        Assert.True(await PurchaseDischargeGuards.HasActiveForShippingAsync(_ctx.Db.Context, shippingKey));
    }

    [Fact]
    public async Task Cancelled_discharge_does_not_block()
    {
        var shippingKey = await SeedClosedAsync();
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Open, delivered: 970m);

        Assert.False(await PurchaseDischargeGuards.HasActiveForItemAsync(_ctx.Db.Context, _ctx.Item.Key!.Value));
        Assert.False(await PurchaseDischargeGuards.HasActiveForInvoiceAsync(_ctx.Db.Context, _ctx.Invoice.Key));
        Assert.False(await PurchaseDischargeGuards.HasActiveForShippingAsync(_ctx.Db.Context, shippingKey));
    }

    [Fact]
    public async Task Invoice_delete_is_refused()
    {
        await SeedClosedAsync();
        var invoice = await _ctx.Db.Context.SalesInvoices.SingleAsync(x => x.Key == _ctx.Invoice.Key);
        invoice.InvoiceStatus = InvoiceStatus.Pending; // delete só aceita Pending
        await _ctx.Db.Context.SaveChangesAsync();

        var service = new SalesInvoicesDeleteService(
            _ctx.Db,
            new ShipmentLoadsBalanceHookService(_ctx.Db.Context, new ShipmentLoadsMovementLogService(_ctx.Db.Context)),
            NullLogger<SalesInvoicesDeleteService>.Instance);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(_ctx.Invoice.Key, "tester"));
        Assert.Equal(PurchaseDischargeGuards.InvoiceMessage, ex.Message);
        Assert.True(await _ctx.Db.Context.SalesInvoices.AnyAsync(x => x.Key == _ctx.Invoice.Key));
    }

    [Fact]
    public async Task Item_delete_is_refused()
    {
        await SeedClosedAsync();

        var service = new SalesInvoicesItemsDeleteService(_ctx.Db, NullLogger<SalesInvoicesItemsDeleteService>.Instance);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(_ctx.Item.Key!.Value));
        Assert.Equal(PurchaseDischargeGuards.ItemMessage, ex.Message);
        Assert.True(await _ctx.Db.Context.SalesInvoicesItems.AnyAsync(x => x.Key == _ctx.Item.Key));
    }

    [Fact]
    public async Task Invoice_cancel_is_refused()
    {
        await SeedClosedAsync();

        var service = new SalesInvoicesCancelService(
            _ctx.Db,
            new SalesShipmentReleasesRecalculateShippedService(_ctx.Db.Context),
            new SalesContractsAllocationDeleteForInvoiceService(_ctx.Db),
            new ShipmentLoadsBalanceHookService(_ctx.Db.Context, new ShipmentLoadsMovementLogService(_ctx.Db.Context)),
            NullLogger<SalesInvoicesCancelService>.Instance);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(_ctx.Invoice.Key, "tester"));
        Assert.Equal(PurchaseDischargeGuards.InvoiceMessage, ex.Message);
        var stored = await _ctx.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(x => x.Key == _ctx.Invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, stored.InvoiceStatus);
    }

    [Fact]
    public async Task Expedition_reverse_is_refused()
    {
        await SeedClosedAsync();
        var shipping = await _ctx.Db.Context.ShippingTransactions.AsNoTracking().SingleAsync();

        var service = new ShippingTransactionsReverseService(
            _ctx.Db,
            new StorageTransactionsGetService(_ctx.Db, NullLogger<StorageTransactionsGetService>.Instance),
            new PurchaseContractsAllocationDeleteService(_ctx.Db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance),
            new ShipmentReleasesRecalculateShippedService(_ctx.Db.Context),
            NullLogger<ShippingTransactionsReverseService>.Instance);

        // O estorno recebe a chave da SAÍDA(7), não a de SHIPPING_TRANSACTIONS.
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            service.ExecuteAsync(shipping.SalesStorageTransactionKey, "tester"));
        Assert.Equal(PurchaseDischargeGuards.ShippingMessage, ex.Message);
    }
}
```

Test file `using`s, besides the ones above: `Microsoft.Extensions.Logging.Abstractions`,
`SiagroB1.Application.Services.PurchaseContracts`, `SiagroB1.Application.Services.SalesContracts`,
`SiagroB1.Application.Services.SalesInvoices`, `SiagroB1.Application.Services.SalesShipmentReleases`,
`SiagroB1.Application.Services.ShipmentLoads`, `SiagroB1.Application.Services.ShipmentReleases`,
`SiagroB1.Application.Services.ShippingTransactions`,
`SiagroB1.Application.Services.StorageTransactions` and `SiagroB1.Domain.Exceptions`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseDischargeGuardsTests"`
Expected: compile error — `PurchaseDischargeGuards` does not exist.

- [ ] **Step 3: Implement the guards**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseDischarges;

/// <summary>
/// Troca nota: nota, item ou Expedição com romaneio de quebra/sobra de descarga ATIVO não pode ser
/// excluído/cancelado/estornado — as FKs são NoAction (o banco daria 547 → 500 mudo) e, mais
/// importante, o acerto do fornecedor ficaria órfão. Quem desfaz é reabrir a conferência.
/// </summary>
public static class PurchaseDischargeGuards
{
    public const string InvoiceMessage =
        "Este documento de saída tem quebra/sobra de descarga lançada no contrato de compra. Reabra a conferência de entregas antes.";
    public const string ItemMessage =
        "Este item tem quebra/sobra de descarga lançada no contrato de compra. Reabra a conferência de entregas antes.";
    public const string ShippingMessage =
        "Esta Expedição tem quebra/sobra de descarga lançada no contrato de compra. Reabra a conferência de entregas antes.";

    private static IQueryable<Domain.Entities.StorageTransaction> Active(AppDbContext ctx) =>
        ctx.StorageTransactions.Where(x =>
            (x.TransactionType == StorageTransactionType.PurchaseDischargeLoss ||
             x.TransactionType == StorageTransactionType.PurchaseDischargeGain) &&
            x.TransactionStatus != StorageTransactionsStatus.Cancelled);

    public static Task<bool> HasActiveForItemAsync(AppDbContext ctx, Guid salesInvoiceItemKey) =>
        Active(ctx).AnyAsync(x => x.DischargeSalesInvoiceItemKey == salesInvoiceItemKey);

    public static Task<bool> HasActiveForInvoiceAsync(AppDbContext ctx, Guid salesInvoiceKey) =>
        Active(ctx).AnyAsync(x => ctx.SalesInvoicesItems.Any(i =>
            i.Key == x.DischargeSalesInvoiceItemKey && i.SalesInvoiceKey == salesInvoiceKey));

    public static Task<bool> HasActiveForShippingAsync(AppDbContext ctx, Guid shippingTransactionKey) =>
        Active(ctx).AnyAsync(x => x.DischargeShippingTransactionKey == shippingTransactionKey);
}
```

- [ ] **Step 4: Call them**

- `SalesInvoicesDeleteService`, right after the GAC-1171 ticket guard:

```csharp
            if (await PurchaseDischargeGuards.HasActiveForInvoiceAsync(db.Context, entity.Key))
                throw new DefaultException(PurchaseDischargeGuards.InvoiceMessage);
```

- `SalesInvoicesItemsDeleteService`, right after the GAC-1171 ticket guard:

```csharp
            if (await PurchaseDischargeGuards.HasActiveForItemAsync(db.Context, key))
                throw new DefaultException(PurchaseDischargeGuards.ItemMessage);
```

- `SalesInvoicesCancelService`, after the `"Documento de saída possui retorno."` guard, before any
  write:

```csharp
        if (await PurchaseDischargeGuards.HasActiveForInvoiceAsync(db.Context, existingInvoice.Key))
            throw new DefaultException(PurchaseDischargeGuards.InvoiceMessage);
```

  Use that service's own `IUnitOfWork` field name. Check the constructor, and if it is not `db`,
  adapt.

- `ShippingTransactionsReverseService`, as the **first** guard right after `shipping` is loaded,
  before the "Sales transaction already invoiced" check. The later guards (invoiced, load) would
  otherwise answer first with a message that does not say what to undo:

```csharp
        if (await PurchaseDischargeGuards.HasActiveForShippingAsync(db.Context, shipping.Key))
            throw new DefaultException(PurchaseDischargeGuards.ShippingMessage);
```

Add `using SiagroB1.Application.Services.PurchaseDischarges;` and `using SiagroB1.Domain.Exceptions;`
where missing.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseDischargeGuardsTests|FullyQualifiedName~SalesInvoicesDelete|FullyQualifiedName~ShippingTransactions"`
Expected: PASS.

- [ ] **Step 6: Stage**

```bash
git add SiagroB1.Application/Services/PurchaseDischarges/PurchaseDischargeGuards.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesDeleteService.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsDeleteService.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCancelService.cs SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsReverseService.cs SiagroB1.Application.Tests/PurchaseDischarges/PurchaseDischargeGuardsTests.cs
```

---

### Task 7: Keep 16/17 out of the storage statement report and name them

The storage statement (`SiagroB1.Reports`) lists **every** non-cancelled transaction. A 16/17 would
show up as "Outros", with its quantity, inside the port warehouse's statement, where the grain never
was.

**Files:**
- Modify: `SiagroB1.Reports/Services/StorageStatementReportService.cs` (the base `query`, ~line 17)
- Modify: `SiagroB1.Reports/Helpers/StorageStatementReportHelper.cs` (`GetTransactionTypeName`)
- Test: add to `SiagroB1.Application.Tests/PurchaseDischarges/PurchaseDischargeModelTests.cs` only if
  the test project already references `SiagroB1.Reports`. Check with
  `grep -n "SiagroB1.Reports" SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj`. If it
  does not, this task is covered by the browser check in the frontend plan. Do not add the reference
  just for this.

- [ ] **Step 1: Exclude from the statement**

```csharp
        var query = db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            // Troca nota: quebra/sobra de descarga só acerta o contrato de compra — o grão nunca
            // esteve no armazém do extrato.
            .Where(x => x.TransactionType != StorageTransactionType.PurchaseDischargeLoss &&
                        x.TransactionType != StorageTransactionType.PurchaseDischargeGain)
            .Include(x => x.QualityInspections)
            .AsQueryable();
```

- [ ] **Step 2: Name them**

In `GetTransactionTypeName`, before `_ => "Outros"`:

```csharp
        StorageTransactionType.PurchaseDischargeLoss => "Quebra Descarga",
        StorageTransactionType.PurchaseDischargeGain => "Sobra Descarga",
```

Leave `IsEntry`/`IsExit` untouched. 16/17 are neither; the warehouse does not move.

- [ ] **Step 3: Build and stage**

Run: `dotnet build SiagroB1.sln` — expected: 0 errors.

```bash
git add SiagroB1.Reports/Services/StorageStatementReportService.cs SiagroB1.Reports/Helpers/StorageStatementReportHelper.cs
```

---

### Task 8: Read side for "Ajustes de Descarga" + menu

**Files:**
- Create: `SiagroB1.Domain/Dtos/PurchaseDischargeListDto.cs`
- Create: `SiagroB1.Application/Services/PurchaseDischarges/PurchaseDischargesListService.cs`
- Create: `SiagroB1.Web/Functions/PurchaseDischarges/PurchaseDischargesListController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (next to `WarehouseReconciliationsListReleases`, ~line 1066)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Generate: `SiagroB1.Migrations/CommonContext/*_AddPurchaseDischargesMenu.cs`
- Test: `SiagroB1.Application.Tests/PurchaseDischarges/PurchaseDischargesListServiceTests.cs`
- Test: `SiagroB1.Application.Tests/PurchaseDischarges/PurchaseDischargeListDtoSerializationTests.cs`

**Interfaces:**
- Produces:
  - HTTP `GET /odata/PurchaseDischargesList(DateFrom='yyyy-MM-dd',DateTo='yyyy-MM-dd')` → a JSON
    array of `PurchaseDischargeListDto` with **PascalCase** names, and `TransactionType`/`Status` as
    enum **names**;
  - menu key / manifest route name `purchaseDischarges`.
- DTO fields: `Key`, `Code`, `TransactionType`, `Status`, `TransactionDate`, `SupplierCode`,
  `SupplierName`, `PurchaseContractKey`, `PurchaseContractCode`, `ShipmentLoadCode`,
  `SalesInvoiceNumber`, `Volume`, `UnitOfMeasureCode`, `CreatedBy`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using SiagroB1.Application.Services.PurchaseDischarges;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

public class PurchaseDischargesListServiceTests
{
    private readonly PurchaseDischargeTestContext _ctx = new();

    [Fact]
    public async Task Lists_active_and_cancelled_with_contract_load_and_invoice()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 990m); // 1 cancelada + 1 ativa

        var today = DateTime.Now.Date;
        var rows = await new PurchaseDischargesListService(_ctx.Db).ExecuteAsync(today, today);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(contract.Code, r.PurchaseContractCode);
            Assert.Equal("CG000001", r.ShipmentLoadCode);
            Assert.Equal(StorageTransactionType.PurchaseDischargeLoss, r.TransactionType);
        });
        Assert.Single(rows, r => r.Status == StorageTransactionsStatus.Cancelled);
    }

    [Fact]
    public async Task Date_range_is_inclusive_of_the_last_day_and_excludes_other_days()
    {
        await _ctx.SeedLoadAndInvoiceAsync(1000m);
        var contract = await _ctx.SeedContractAsync();
        await _ctx.SeedExpeditionAsync(contract, 1000m);
        await _ctx.SyncAsync(SalesInvoiceDeliveryStatus.Closed, delivered: 970m);

        var today = DateTime.Now.Date;
        Assert.Single(await new PurchaseDischargesListService(_ctx.Db).ExecuteAsync(today.AddDays(-5), today));
        Assert.Empty(await new PurchaseDischargesListService(_ctx.Db).ExecuteAsync(today.AddDays(-5), today.AddDays(-1)));
    }
}

public class PurchaseDischargeListDtoSerializationTests
{
    [Fact]
    public void Serializes_pascal_case_with_enum_names()
    {
        var json = JsonSerializer.Serialize(new PurchaseDischargeListDto
        {
            TransactionType = StorageTransactionType.PurchaseDischargeGain,
            Status = StorageTransactionsStatus.Confirmed,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"TransactionType\":\"PurchaseDischargeGain\"", json);
        Assert.Contains("\"Status\":\"Confirmed\"", json);
        Assert.Contains("\"PurchaseContractCode\"", json);
    }
}
```

`JsonSerializerDefaults.Web` is camelCase, the same as the API. The test proves the attributes win
(memory "odata-api-serializes-camelcase").

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseDischargesList|FullyQualifiedName~PurchaseDischargeListDto"`
Expected: compile error.

- [ ] **Step 3: DTO**

```csharp
using System.Text.Json.Serialization;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha da tela "Ajustes de Descarga" (troca nota): um romaneio de quebra/sobra de descarga.
/// PascalCase e enum por NOME explícitos — a API serializa camelCase por padrão e o tipo TS leria
/// undefined em silêncio.
/// </summary>
public class PurchaseDischargeListDto
{
    [JsonPropertyName("Key")] public Guid Key { get; set; }
    [JsonPropertyName("Code")] public string? Code { get; set; }

    [JsonPropertyName("TransactionType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StorageTransactionType TransactionType { get; set; }

    [JsonPropertyName("Status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public StorageTransactionsStatus Status { get; set; }

    [JsonPropertyName("TransactionDate")] public DateTime? TransactionDate { get; set; }
    [JsonPropertyName("SupplierCode")] public string? SupplierCode { get; set; }
    [JsonPropertyName("SupplierName")] public string? SupplierName { get; set; }
    [JsonPropertyName("PurchaseContractKey")] public Guid? PurchaseContractKey { get; set; }
    [JsonPropertyName("PurchaseContractCode")] public string? PurchaseContractCode { get; set; }
    [JsonPropertyName("ShipmentLoadCode")] public string? ShipmentLoadCode { get; set; }
    [JsonPropertyName("SalesInvoiceNumber")] public string? SalesInvoiceNumber { get; set; }
    [JsonPropertyName("Volume")] public decimal Volume { get; set; }
    [JsonPropertyName("UnitOfMeasureCode")] public string? UnitOfMeasureCode { get; set; }
    [JsonPropertyName("CreatedBy")] public string? CreatedBy { get; set; }
}
```

- [ ] **Step 4: Service**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseDischarges;

/// <summary>
/// Romaneios de quebra/sobra de descarga no período (inclusive o último dia). O contrato vem pela
/// Expedição (SHIPPING_TRANSACTIONS → Compra 8 → alocação), e não pela alocação do próprio romaneio,
/// que some quando ele é cancelado.
/// </summary>
public class PurchaseDischargesListService(IUnitOfWork db)
{
    public async Task<List<PurchaseDischargeListDto>> ExecuteAsync(DateTime dateFrom, DateTime dateTo)
    {
        var ctx = db.Context;
        var from = dateFrom.Date;
        var until = dateTo.Date.AddDays(1);

        var query =
            from st in ctx.StorageTransactions.AsNoTracking()
            where (st.TransactionType == StorageTransactionType.PurchaseDischargeLoss ||
                   st.TransactionType == StorageTransactionType.PurchaseDischargeGain) &&
                  st.TransactionDate >= from && st.TransactionDate < until
            join sht in ctx.ShippingTransactions.AsNoTracking()
                on st.DischargeShippingTransactionKey equals (Guid?)sht.Key into shts
            from sht in shts.DefaultIfEmpty()
            join pa in ctx.PurchaseContractsAllocations.AsNoTracking()
                on (sht == null ? null : sht.PurchaseStorageTransactionKey) equals (Guid?)pa.StorageTransactionKey into pas
            from pa in pas.DefaultIfEmpty()
            join pc in ctx.PurchaseContracts.AsNoTracking()
                on (pa == null ? (Guid?)null : pa.PurchaseContractKey) equals (Guid?)pc.Key into pcs
            from pc in pcs.DefaultIfEmpty()
            join item in ctx.SalesInvoicesItems.AsNoTracking()
                on st.DischargeSalesInvoiceItemKey equals item.Key into items
            from item in items.DefaultIfEmpty()
            join inv in ctx.SalesInvoices.AsNoTracking()
                on (item == null ? null : item.SalesInvoiceKey) equals (Guid?)inv.Key into invs
            from inv in invs.DefaultIfEmpty()
            join load in ctx.ShipmentLoads.AsNoTracking()
                on (inv == null ? null : inv.ShipmentLoadKey) equals (Guid?)load.Key into loads
            from load in loads.DefaultIfEmpty()
            orderby st.TransactionDate descending, st.Code descending
            select new PurchaseDischargeListDto
            {
                Key = st.Key,
                Code = st.Code,
                TransactionType = st.TransactionType,
                Status = st.TransactionStatus,
                TransactionDate = st.TransactionDate,
                SupplierCode = st.CardCode,
                SupplierName = st.CardName,
                PurchaseContractKey = pc == null ? null : pc.Key,
                PurchaseContractCode = pc == null ? null : pc.Code,
                ShipmentLoadCode = load == null ? null : load.Code,
                SalesInvoiceNumber = inv == null ? null : (inv.TaxDocumentNumber ?? inv.InvoiceNumber),
                Volume = st.NetWeight,
                UnitOfMeasureCode = st.UnitOfMeasureCode,
                CreatedBy = st.CreatedBy,
            };

        return await query.ToListAsync();
    }
}
```

`PurchaseContractsAllocations` has one row per Compra(8) in the Expedição flow, so the join does not
duplicate rows. If the test shows duplicates, add `.GroupBy(x => x.Key).Select(g => g.First())` in
memory and report it.

- [ ] **Step 5: Controller + EDM + DI**

`SiagroB1.Web/Functions/PurchaseDischarges/PurchaseDischargesListController.cs`:

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseDischarges;

namespace SiagroB1.Web.Functions.PurchaseDischarges;

public class PurchaseDischargesListController(PurchaseDischargesListService service) : ODataController
{
    [HttpGet("odata/PurchaseDischargesList(DateFrom={dateFrom},DateTo={dateTo})")]
    public async Task<ActionResult> List([FromRoute] string? dateFrom, [FromRoute] string? dateTo)
    {
        // Parâmetro string do OData é anulável e chega com as aspas: TryParseExact estrito, sem
        // "hoje" como fallback (lição do GAC-1171).
        if (!TryParse(dateFrom, out var from) || !TryParse(dateTo, out var to))
            return BadRequest("Informe o período no formato aaaa-mm-dd.");

        return Ok(await service.ExecuteAsync(from, to));
    }

    private static bool TryParse(string? raw, out DateTime value) =>
        DateTime.TryParseExact(raw?.Trim('\''), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out value);
}
```

Copy the `using`s from `WarehouseReconciliationsListReleasesController.cs`, which is the pattern
here.

In `ODataConfigurations.cs`, next to `WarehouseReconciliationsListReleases`:

```csharp
        var purchaseDischargesList = modelBuilder.Function("PurchaseDischargesList");
        purchaseDischargesList.Parameter<string>("DateFrom");
        purchaseDischargesList.Parameter<string>("DateTo");
        purchaseDischargesList.Returns<IActionResult>();
```

In `ServiceCollectionExtensions.cs`:

```csharp
        services.AddScoped<PurchaseDischargesListService>();
```

- [ ] **Step 6: Menu migration (CommonContext)**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef migrations add AddPurchaseDischargesMenu --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o CommonContext
```

Replace the body of the generated class. Keep its name and namespace, and use a freshly generated
GUID (`[guid]::NewGuid()`) for the `ROLE_MENUS` Id:

```csharp
    /// <summary>
    /// Menu "Ajustes de Descarga" (troca nota), no grupo "Compras". Order 14 porque 1 a 13 já estão
    /// ocupados em "purchases". A Key PRECISA ser igual ao name da rota no manifest.json
    /// (App.controller navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddPurchaseDischargesMenu : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "purchaseDischarges", "Ajustes de Descarga", "sap-icon://folder-blank", true, false, 14, "purchases" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "<NEW-GUID-UPPERCASE>", "ADMIN", "purchaseDischarges" },
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "<SAME-GUID>");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "purchaseDischarges");
        }
    }
```

Replace `<NEW-GUID-UPPERCASE>`/`<SAME-GUID>` with the generated value. It is a literal and must be
the same in Up and Down.

Before choosing 14, confirm the order is still free. Run
`grep -rn "\"purchases\" *}" SiagroB1.Migrations/CommonContext | grep -o ", [0-9]*, \"purchases\"" | sort`.
If 14 is taken, use the next free one and fix the comment.

```powershell
dotnet ef database update --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseDischarge"`
Expected: PASS.

- [ ] **Step 8: Stage**

```bash
git add SiagroB1.Domain/Dtos/PurchaseDischargeListDto.cs SiagroB1.Application/Services/PurchaseDischarges/PurchaseDischargesListService.cs SiagroB1.Web/Functions/PurchaseDischarges SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Migrations/CommonContext SiagroB1.Application.Tests/PurchaseDischarges
```

---

### Task 9: Full suite, endpoint smoke test, spec status

- [ ] **Step 1: Full suite**

Stop `SiagroB1.Web` first.

Run: `dotnet test SiagroB1.Application.Tests`
Expected: everything green. Report the total count, and compare it with the count before Task 1
(`git stash` is not allowed; use the count printed by the first run of this plan).

- [ ] **Step 2: Smoke test the endpoint against localhost**

Start the stack with profile `yktb` (memory "run-stack-locally"). Log in as admin/1234 through the
Gateway and call:
- `GET /odata/PurchaseDischargesList(DateFrom='2026-01-01',DateTo='2026-12-31')` → `200` and `[]`;
- `GET /odata/PurchaseContractsGetAllocationsByContract(PurchaseContractKey=<any contract>)` →
  `200`, with the new fields `DischargeInvoiceNumber`/`DischargeLoadCode` present (null).

Stop the stack at the end.

- [ ] **Step 3: Update the spec status line**

In `docs/superpowers/specs/2026-09-19-triangular-port-adjustment-design.md` set
`status: backend implementado <data> — frontend pendente`. §1 and §3 already describe what this plan
builds: the link columns, the hook covering only Normal invoices, and the four guards. If the
implementation diverged, fix the spec text to match and say so in the report.

```bash
git add docs/superpowers/specs/2026-09-19-triangular-port-adjustment-design.md
```

- [ ] **Step 4: Draft the commit message for the user's review**

Show the branch, `git status --short` and the draft below. Commit only after the user says yes.

```
feat(purchase-contract): acertar compra CIF pela descarga na troca nota

Na troca nota o fornecedor responde pelo resultado do porto (peso e qualidade), mas a
Conferência de Entregas só ajustava o contrato de VENDA. Encerrar a conferência agora gera
romaneios de Quebra(16)/Sobra(17) de descarga, alocados ∓ no contrato de compra CIF de cada
Expedição da carga, rateados pelo peso; reabrir cancela. Contrato encerrado e sobra acima do
saldo recusam a conferência.

Atenção: 16/17 ficam FORA do saldo de armazém, do embarcado da liberação e do extrato — por
isso nunca carregam ShipmentLoadKey/ShipmentReleaseKey/SalesInvoiceKey. FreightTerms.Cif é o
valor 0 do enum: a regra é "Expedição de carga E contrato CIF", nunca CIF sozinho.

DB: AddPurchaseDischargeTransactions
DB: AddPurchaseDischargesMenu
```

## Self-review notes (for the executor)

- **`SalesInvoicesReverseConfirmService` is not hooked, on purpose.** It resets the delivery only for
  RETURN invoice items, and those never get 16/17 (`PrepareAsync` requires `SalesInvoiceType.Normal`).
  Spec §3 says so.
- **The "sobra above balance" guard is in two places.** `PrepareAsync` gives the business message
  before any write. `ExecuteDischargeAsync` is the last line of defense and keeps the English
  message, the same as its siblings.
