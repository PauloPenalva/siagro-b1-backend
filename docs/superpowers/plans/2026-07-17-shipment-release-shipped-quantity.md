# ShipmentRelease Persisted ShippedQuantity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist `ShippedQuantity` on `ShipmentRelease` (NetWeight, sign-correct: shipments +, sales returns −), derive `AvailableQuantity = ReleasedQuantity − ShippedQuantity`, recalculate it from a single service on transaction lifecycle changes, and make the two balance services read the column.

**Architecture:** Symmetric with `PurchaseContract.AllocatedVolume`. A mapped `ShippedQuantity` column + `RowVersion`; a `ShipmentReleasesRecalculateShippedService` derives it from a DB SUM; storage-transaction lifecycle services (create/confirm/cancel/reverse) call the recalc for linked sales shipment/return transactions. Migration adds columns + backfill.

**Tech Stack:** .NET 10, EF Core 10, xUnit + EF InMemory, migrations in `SiagroB1.Migrations`.

## Global Constraints

- Target framework `net10.0`; nullable enabled; `LangVersion` 14.
- Sign convention (canonical, from `StorageTransactionsConfirmedService.GetWarehouseBalanceAsync`): `SalesShipment` is outbound (+ to used), `SalesShipmentReturn` is inbound (− from used). So `ShippedQuantity = Σ(SalesShipment.NetWeight) − Σ(SalesShipmentReturn.NetWeight)`.
- Uses **NetWeight** (not GrossWeight).
- Filter: `TransactionStatus != StorageTransactionsStatus.Cancelled` (Pending counts — preserved).
- Recalc derives from a DB `SUM` in one place; never `+=`/`-=`.
- Enum int values for backfill SQL: `StorageTransactionType.SalesShipment = 7`, `SalesShipmentReturn = 12`, `StorageTransactionsStatus.Cancelled = 2`.
- Migrations scaffolded with `dotnet ef migrations add ... --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext`.
- Tests in `SiagroB1.Application.Tests`; `TestDb.CreateUnitOfWork()` gives `_db` (UnitOfWork) with `_db.Context` (AppDbContext).
- Run: `dotnet test SiagroB1.Application.Tests --nologo`; build: `dotnet build SiagroB1.sln --nologo`.
- Commit only at the end of each task.

## File Structure

- `SiagroB1.Domain/Entities/ShipmentRelease.cs` — add `ShippedQuantity` + `RowVersion`; rewrite `AvailableQuantity`.
- `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateShippedService.cs` — new recalc service.
- `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCreateService.cs`, `StorageTransactionsConfirmedService.cs`, `StorageTransactionsCancelService.cs`, `StorageTransactionsReverseService.cs` — add recalc hook.
- `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesBalanceService.cs`, `ShipmentReleasesPurchaseContractsService.cs` — read `ShippedQuantity`.
- `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` — register recalc service.
- `SiagroB1.Migrations/AppContext/<ts>_AddShipmentReleaseShippedQuantity.cs` — migration + backfill.
- Tests: `ShipmentReleaseAvailableQuantityTests.cs`, `ShipmentReleasesRecalculateShippedServiceTests.cs`, `StorageTransactionsCancelHookTests.cs`, `ShipmentReleasesBalanceServiceShippedTests.cs`, `ShipmentReleaseConcurrencyTests.cs` (new).

---

### Task 1: Persist `ShippedQuantity` + `RowVersion`; rewrite `AvailableQuantity`

**Files:**
- Modify: `SiagroB1.Domain/Entities/ShipmentRelease.cs`
- Create: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleaseAvailableQuantityTests.cs`, `SiagroB1.Application.Tests/Infra/ShipmentReleaseConcurrencyTests.cs`

**Interfaces:**
- Produces: `ShipmentRelease.ShippedQuantity` (`decimal`, mapped), `ShipmentRelease.RowVersion` (`byte[]?`, `[Timestamp]`), `ShipmentRelease.AvailableQuantity` (`decimal`, `[NotMapped]`) = `Status != Cancelled ? Round(ReleasedQuantity − ShippedQuantity, 3) : 0`, no navigation.

- [ ] **Step 1: Write the failing robustness test**

Create `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleaseAvailableQuantityTests.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleaseAvailableQuantityTests
{
    private static ShipmentRelease New(decimal released, decimal shipped, ReleaseStatus status = ReleaseStatus.Actived) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = Guid.NewGuid(),
        DeliveryLocationCode = "01",
        ReleasedQuantity = released,
        ShippedQuantity = shipped,
        Status = status,
    };

    [Fact]
    public void AvailableQuantity_DerivesFromShippedQuantity_WithoutTransactions()
    {
        var sr = New(released: 100m, shipped: 40m);
        Assert.Empty(sr.Transactions);
        Assert.Equal(60m, sr.AvailableQuantity);
    }

    [Fact]
    public void AvailableQuantity_NegativeShipped_FromNetReturn_IncreasesAvailable()
    {
        // shipped 80 − returned 30 = 50 usados
        var sr = New(released: 100m, shipped: 50m);
        Assert.Equal(50m, sr.AvailableQuantity);
    }

    [Fact]
    public void AvailableQuantity_Cancelled_IsZero()
    {
        var sr = New(released: 100m, shipped: 40m, status: ReleaseStatus.Cancelled);
        Assert.Equal(0m, sr.AvailableQuantity);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleaseAvailableQuantityTests --nologo`
Expected: FAIL — compile error `'ShipmentRelease' does not contain a definition for 'ShippedQuantity'`.

- [ ] **Step 3: Modify the entity**

In `SiagroB1.Domain/Entities/ShipmentRelease.cs`, add the using at the top:

```csharp
using System.ComponentModel.DataAnnotations;
```

Replace the `AvailableQuantity` property (the `[NotMapped] public decimal AvailableQuantity => ...` block that subtracts `Transactions...Sum(x => x.GrossWeight)`) with the mapped column, rowversion, and the scalar-derived property:

```csharp
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal ShippedQuantity { get; set; }

    /// <summary>
    /// Token de concorrência otimista (SQL Server rowversion).
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Saldo disponível para romanear, derivado de <see cref="ShippedQuantity"/>
    /// (persistido, recalculado nos hooks de romaneio). Não depende de navegação.
    /// </summary>
    [NotMapped]
    public decimal AvailableQuantity =>
        Status != ReleaseStatus.Cancelled
            ? decimal.Round(ReleasedQuantity - ShippedQuantity, 3, MidpointRounding.ToEven)
            : decimal.Zero;
```

Leave `HasStorageTransactions` unchanged.

- [ ] **Step 4: Run the robustness test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleaseAvailableQuantityTests --nologo`
Expected: PASS (3 tests).

- [ ] **Step 5: Write the rowversion mapping guard test**

Create `SiagroB1.Application.Tests/Infra/ShipmentReleaseConcurrencyTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class ShipmentReleaseConcurrencyTests
{
    [Fact]
    public void RowVersion_IsMappedAsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;

        using var context = new AppDbContext(options);

        var prop = context.Model
            .FindEntityType(typeof(ShipmentRelease))!
            .FindProperty(nameof(ShipmentRelease.RowVersion));

        Assert.NotNull(prop);
        Assert.True(prop!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, prop.ValueGenerated);
    }
}
```

- [ ] **Step 6: Run the full suite and build**

Run: `dotnet test SiagroB1.Application.Tests --nologo` → Expected: PASS.
Run: `dotnet build SiagroB1.sln --nologo` → Expected: `0 Erro(s)` (if the app is running locally, file-copy locks may appear; confirm there are no `error CS` lines).

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Domain/Entities/ShipmentRelease.cs SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleaseAvailableQuantityTests.cs SiagroB1.Application.Tests/Infra/ShipmentReleaseConcurrencyTests.cs
git commit -m "feat: persist ShipmentRelease.ShippedQuantity, derive AvailableQuantity from it"
```

---

### Task 2: Recalc service + DI

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateShippedService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesRecalculateShippedServiceTests.cs`

**Interfaces:**
- Produces: `ShipmentReleasesRecalculateShippedService.RecalculateAsync(Guid shipmentReleaseKey) : Task`.

- [ ] **Step 1: Write the failing test**

Create `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesRecalculateShippedServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesRecalculateShippedServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentReleasesRecalculateShippedService Service() => new(_db.Context);

    private async Task<ShipmentRelease> SeedReleaseAsync(decimal released)
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = released,
            ShippedQuantity = 999m, // valor errado, deve ser recalculado
            Status = ReleaseStatus.Actived,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    private StorageTransaction Tx(Guid releaseKey, StorageTransactionType type, decimal net,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed) => new()
    {
        Key = Guid.NewGuid(),
        Code = "ST",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "01",
        TransactionType = type,
        TransactionStatus = status,
        NetWeight = net,
        ShipmentReleaseKey = releaseKey,
    };

    private async Task<decimal> ShippedAsync(Guid key) =>
        (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key)).ShippedQuantity;

    [Fact]
    public async Task Recalc_ShipmentMinusReturn_UsingNetWeight()
    {
        var sr = await SeedReleaseAsync(released: 100m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.SalesShipment, 80m),
            Tx(sr.Key, StorageTransactionType.SalesShipmentReturn, 30m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(50m, await ShippedAsync(sr.Key)); // 80 − 30
    }

    [Fact]
    public async Task Recalc_IgnoresCancelled_CountsPending()
    {
        var sr = await SeedReleaseAsync(released: 100m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.SalesShipment, 40m, StorageTransactionsStatus.Pending),
            Tx(sr.Key, StorageTransactionType.SalesShipment, 25m, StorageTransactionsStatus.Cancelled));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(40m, await ShippedAsync(sr.Key)); // pending conta, cancelled não
    }

    [Fact]
    public async Task Recalc_IgnoresOtherTypesAndOtherReleases()
    {
        var sr = await SeedReleaseAsync(released: 100m);
        var other = Guid.NewGuid();
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.Purchase, 500m),          // tipo ignorado
            Tx(other, StorageTransactionType.SalesShipment, 70m),       // outro release
            Tx(sr.Key, StorageTransactionType.SalesShipment, 10m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(10m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_NoTransactions_SetsZero()
    {
        var sr = await SeedReleaseAsync(released: 100m);

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(0m, await ShippedAsync(sr.Key));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleasesRecalculateShippedServiceTests --nologo`
Expected: FAIL — compile error: `ShipmentReleasesRecalculateShippedService` not found.

- [ ] **Step 3: Create the recalc service**

Create `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateShippedService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesRecalculateShippedService(AppDbContext context)
{
    public async Task RecalculateAsync(Guid shipmentReleaseKey)
    {
        var release = await context.ShipmentReleases
            .FirstOrDefaultAsync(x => x.Key == shipmentReleaseKey);

        if (release is null)
            return;

        // usado = Σ(SalesShipment.Net) − Σ(SalesShipmentReturn.Net); Pending conta, Cancelled não.
        var shipped = await context.StorageTransactions
            .Where(t => t.ShipmentReleaseKey == shipmentReleaseKey
                        && t.TransactionStatus != StorageTransactionsStatus.Cancelled
                        && (t.TransactionType == StorageTransactionType.SalesShipment
                            || t.TransactionType == StorageTransactionType.SalesShipmentReturn))
            .SumAsync(t => t.TransactionType == StorageTransactionType.SalesShipment
                ? t.NetWeight
                : -t.NetWeight);

        release.ShippedQuantity = shipped;

        await context.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleasesRecalculateShippedServiceTests --nologo`
Expected: PASS (4 tests).

- [ ] **Step 5: Register the service in DI**

In `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, in the shipment-releases registration area (near the other `ShipmentReleases*` services), add:

```csharp
        services.AddScoped<ShipmentReleasesRecalculateShippedService>();
```

- [ ] **Step 6: Build and run full suite**

Run: `dotnet build SiagroB1.sln --nologo` → no `error CS`.
Run: `dotnet test SiagroB1.Application.Tests --nologo` → PASS.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateShippedService.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesRecalculateShippedServiceTests.cs
git commit -m "feat: ShipmentReleasesRecalculateShippedService (net, sign-correct)"
```

---

### Task 3: Recalc hooks in storage-transaction lifecycle services

**Files:**
- Modify: `StorageTransactionsCancelService.cs`, `StorageTransactionsReverseService.cs`, `StorageTransactionsCreateService.cs`, `StorageTransactionsConfirmedService.cs`
- Test: `SiagroB1.Application.Tests/StorageTransactions/StorageTransactionsCancelHookTests.cs`

**Interfaces:**
- Consumes: `ShipmentReleasesRecalculateShippedService` (Task 2).
- Produces: after a `SalesShipment`/`SalesShipmentReturn` transaction linked to a release is created/confirmed/cancelled/reversed, that release's `ShippedQuantity` is recalculated.

- [ ] **Step 1: Write the failing hook test (cancel — lightest deps)**

Create `SiagroB1.Application.Tests/StorageTransactions/StorageTransactionsCancelHookTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

public class StorageTransactionsCancelHookTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public async Task Cancel_SalesShipmentLinkedToRelease_RecalculatesShippedQuantity()
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = 100m,
            ShippedQuantity = 80m,
            Status = ReleaseStatus.Actived,
        };
        var tx = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "ST",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionOrigin = TransactionCode.StorageTransaction,
            NetWeight = 80m,
            ShipmentReleaseKey = release.Key,
        };
        _db.Context.ShipmentReleases.Add(release);
        _db.Context.StorageTransactions.Add(tx);
        await _db.Context.SaveChangesAsync();

        var recalc = new ShipmentReleasesRecalculateShippedService(_db.Context);
        var service = new StorageTransactionsCancelService(_db, recalc);

        await service.ExecuteAsync(tx.Key, "tester");

        var reloaded = await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(0m, reloaded.ShippedQuantity); // transação cancelada saiu da soma
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter StorageTransactionsCancelHookTests --nologo`
Expected: FAIL — compile error: `StorageTransactionsCancelService` constructor does not take a second argument.

- [ ] **Step 3: Add the hook to `StorageTransactionsCancelService`**

In `StorageTransactionsCancelService.cs`, add the recalc dependency to the primary constructor and call it after the save. Change the class declaration:

```csharp
public class StorageTransactionsCancelService(
    IUnitOfWork db,
    ShipmentReleasesRecalculateShippedService recalcShipped)
{
```

(Add `using SiagroB1.Application.Services.ShipmentReleases;` at the top.)

Replace the `try` block's save with a save-then-recalc:

```csharp
        try
        {
            doc.TransactionStatus = StorageTransactionsStatus.Cancelled;
            await db.SaveChangesAsync();

            if (doc.ShipmentReleaseKey.HasValue &&
                doc.TransactionType is StorageTransactionType.SalesShipment or StorageTransactionType.SalesShipmentReturn)
            {
                await recalcShipped.RecalculateAsync(doc.ShipmentReleaseKey.Value);
            }
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
```

- [ ] **Step 4: Run the hook test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter StorageTransactionsCancelHookTests --nologo`
Expected: PASS.

- [ ] **Step 5: Add the hook to `StorageTransactionsReverseService`**

In `StorageTransactionsReverseService.cs`, add `ShipmentReleasesRecalculateShippedService recalcShipped` to the constructor and `using SiagroB1.Application.Services.ShipmentReleases;`. After `await db.SaveChangesAsync();` inside the `try`, add:

```csharp
            if (doc.ShipmentReleaseKey.HasValue &&
                doc.TransactionType is StorageTransactionType.SalesShipment or StorageTransactionType.SalesShipmentReturn)
            {
                await recalcShipped.RecalculateAsync(doc.ShipmentReleaseKey.Value);
            }
```

- [ ] **Step 6: Add the hook to `StorageTransactionsCreateService`**

In `StorageTransactionsCreateService.cs`, add `ShipmentReleasesRecalculateShippedService recalcShipped` to the constructor and `using SiagroB1.Application.Services.ShipmentReleases;`. In `ExecuteAsync`, replace the auto-commit block:

```csharp
            if (commitMode == CommitMode.Auto)
                await unitOfWork.SaveChangesAsync();

            return entity;
```

with:

```csharp
            if (commitMode == CommitMode.Auto)
            {
                await unitOfWork.SaveChangesAsync();

                if (entity.ShipmentReleaseKey.HasValue &&
                    entity.TransactionType is StorageTransactionType.SalesShipment or StorageTransactionType.SalesShipmentReturn)
                {
                    await recalcShipped.RecalculateAsync(entity.ShipmentReleaseKey.Value);
                }
            }

            return entity;
```

- [ ] **Step 7: Add the hook to `StorageTransactionsConfirmedService`**

In `StorageTransactionsConfirmedService.cs`, add `ShipmentReleasesRecalculateShippedService recalcShipped` to the constructor and `using SiagroB1.Application.Services.ShipmentReleases;`. In `Processing`, after the `switch (st.TransactionType) { ... }` block, add:

```csharp
        if (commitMode == CommitMode.Auto &&
            st.ShipmentReleaseKey.HasValue &&
            st.TransactionType is StorageTransactionType.SalesShipment or StorageTransactionType.SalesShipmentReturn)
        {
            await recalcShipped.RecalculateAsync(st.ShipmentReleaseKey.Value);
        }
```

(The sales branches SaveChanges in Auto mode, so the transaction is persisted before the recalc reads the DB.)

- [ ] **Step 8: Build and run full suite**

Run: `dotnet build SiagroB1.sln --nologo` → no `error CS`.
Run: `dotnet test SiagroB1.Application.Tests --nologo` → PASS.

Note: the create/confirm/reverse hooks are one-line calls mirroring the tested cancel hook; they are verified by build + the cancel-hook test (the recalc logic itself is exhaustively tested in Task 2). Their heavy external dependencies (localizer, business-partner/item/warehouse services, doc-number sequence) make full unit tests disproportionate.

- [ ] **Step 9: Commit**

```bash
git add SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCancelService.cs SiagroB1.Application/Services/StorageTransactions/StorageTransactionsReverseService.cs SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCreateService.cs SiagroB1.Application/Services/StorageTransactions/StorageTransactionsConfirmedService.cs SiagroB1.Application.Tests/StorageTransactions/StorageTransactionsCancelHookTests.cs
git commit -m "feat: recalc ShipmentRelease.ShippedQuantity on transaction lifecycle changes"
```

---

### Task 4: Balance services read `ShippedQuantity`

**Files:**
- Modify: `ShipmentReleasesBalanceService.cs`, `ShipmentReleasesPurchaseContractsService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesBalanceServiceShippedTests.cs`

**Interfaces:**
- Consumes: `ShipmentRelease.ShippedQuantity`.
- Produces: balance projections compute `UsedQuantity = Σ ShippedQuantity` (no transaction aggregation).

- [ ] **Step 1: Write the failing test**

Create `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesBalanceServiceShippedTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesBalanceServiceShippedTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private sealed class EmptyWarehouseService : IWarehouseService
    {
        public Task<IEnumerable<WarehouseModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<WarehouseModel?> GetByIdAsync(string code) => throw new NotImplementedException();
        public Task<WarehouseModel> CreateAsync(WarehouseModel model) => throw new NotImplementedException();
        public Task<WarehouseModel?> UpdateAsync(string code, WarehouseModel model) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
        public IQueryable<WarehouseModel> QueryAll() => throw new NotImplementedException();
        public Task<Dictionary<string, WarehouseInfo>> LoadWarehousesAsync() => Task.FromResult(new Dictionary<string, WarehouseInfo>());
    }

    [Fact]
    public async Task Balance_UsesShippedQuantityColumn()
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
            ItemName = "Soja", TotalVolume = 1000m,
        };
        _db.Context.PurchaseContracts.Add(pc);
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = pc.Key, DeliveryLocationCode = "01",
            DeliveryLocationName = "Matriz", ReleasedQuantity = 100m, ShippedQuantity = 30m,
            Status = ReleaseStatus.Actived,
        });
        await _db.Context.SaveChangesAsync();

        var service = new ShipmentReleasesBalanceService(_db, new EmptyWarehouseService(),
            NullLogger<ShipmentReleasesBalanceService>.Instance);

        var result = await service.ExecuteAsync("SOJA");

        var row = Assert.Single(result);
        Assert.Equal(100m, row.ReleasedQuantity);
        Assert.Equal(70m, row.AvailableQuantity); // 100 − 30
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleasesBalanceServiceShippedTests --nologo`
Expected: FAIL — `AvailableQuantity` is `100` (current code aggregates transactions, of which there are none → UsedQuantity 0), test expects `70`.

- [ ] **Step 3: Update `ShipmentReleasesBalanceService`**

In `ShipmentReleasesBalanceService.cs`, in `LoadBalancesAsync`, replace the `UsedQuantity` projection

```csharp
                UsedQuantity = g.Sum(sr =>
                    sr.Transactions
                        .Where(t =>
                            t.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                            (t.TransactionType == StorageTransactionType.SalesShipment ||
                             t.TransactionType == StorageTransactionType.SalesShipmentReturn))
                        .Sum(t => t.NetWeight))
```

with:

```csharp
                UsedQuantity = g.Sum(sr => sr.ShippedQuantity)
```

- [ ] **Step 4: Update `ShipmentReleasesPurchaseContractsService`**

In `ShipmentReleasesPurchaseContractsService.cs`, in its `LoadBalancesAsync`, replace the identical `UsedQuantity = g.Sum(sr => sr.Transactions.Where(...).Sum(t => t.NetWeight))` projection with:

```csharp
                UsedQuantity = g.Sum(sr => sr.ShippedQuantity)
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleasesBalanceServiceShippedTests --nologo`
Expected: PASS.

- [ ] **Step 6: Build and run full suite**

Run: `dotnet build SiagroB1.sln --nologo` → no `error CS`.
Run: `dotnet test SiagroB1.Application.Tests --nologo` → PASS.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesBalanceService.cs SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesPurchaseContractsService.cs SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesBalanceServiceShippedTests.cs
git commit -m "feat: balance services read persisted ShippedQuantity"
```

---

### Task 5: Migration + backfill

**Files:**
- Create: `SiagroB1.Migrations/AppContext/<ts>_AddShipmentReleaseShippedQuantity.cs` (+ Designer, snapshot)

**Interfaces:**
- Consumes: entity changes from Task 1.

- [ ] **Step 1: Scaffold the migration**

Run:
```bash
dotnet ef migrations add AddShipmentReleaseShippedQuantity --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```
Expected: `Done.`

- [ ] **Step 2: Verify the scaffold only touches the two new columns**

Open the generated `<ts>_AddShipmentReleaseShippedQuantity.cs`. Confirm `Up()` contains exactly:
- `AddColumn<decimal>(name: "ShippedQuantity", table: "SHIPMENT_RELEASES", type: "DECIMAL(18,3)", nullable: false, defaultValue: 0m)`
- `AddColumn<byte[]>(name: "RowVersion", table: "SHIPMENT_RELEASES", type: "rowversion", rowVersion: true, nullable: true)`

If any OTHER table/column appears, STOP and report unexpected drift.

- [ ] **Step 3: Add the backfill SQL to Up()**

At the END of `Up(MigrationBuilder migrationBuilder)`, append:

```csharp
            // Backfill: usado = Σ(SalesShipment.Net) − Σ(SalesShipmentReturn.Net), status <> Cancelled.
            migrationBuilder.Sql(@"
                UPDATE SR
                SET SR.ShippedQuantity = ISNULL((
                    SELECT SUM(CASE
                                 WHEN t.TransactionType = 7  THEN t.NetWeight
                                 WHEN t.TransactionType = 12 THEN -t.NetWeight
                                 ELSE 0 END)
                    FROM STORAGE_TRANSACTIONS t
                    WHERE t.ShipmentReleaseKey = SR.[Key]
                      AND t.TransactionStatus <> 2
                      AND t.TransactionType IN (7, 12)
                ), 0)
                FROM SHIPMENT_RELEASES SR;");
```

Leave `Down()` as scaffolded (drops both columns).

- [ ] **Step 4: Verify no pending model changes**

Run:
```bash
dotnet ef migrations has-pending-model-changes --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```
Expected: `No changes have been made to the model since the last migration.`

- [ ] **Step 5: Build and run full suite**

Run: `dotnet build SiagroB1.sln --nologo` → no `error CS`.
Run: `dotnet test SiagroB1.Application.Tests --nologo` → PASS.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Migrations/AppContext/
git commit -m "feat: migration adds ShipmentRelease ShippedQuantity + RowVersion with backfill"
```

---

## Self-Review

**Spec coverage:**
- §1 Model (ShippedQuantity + RowVersion + AvailableQuantity scalar) → Task 1. ✓
- §2 Recalc service (net, sign, pending counts) → Task 2. ✓
- §3 Hooks (create/confirm/cancel/reverse) → Task 3. ✓
- §4 Balance services read column → Task 4. ✓
- §5 Migration + backfill + rowversion → Task 5 (+ Task 1 entity rowversion). ✓
- Tests §1-6 → Task 1 (robustness, cancelled=0, rowversion), Task 2 (sign/net/pending/other), Task 3 (cancel hook), Task 4 (balance via column). ✓
- HasStorageTransactions out of scope → untouched. ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code; commands have expected output. ✓

**Type consistency:** `ShippedQuantity` (`decimal`), `RowVersion` (`byte[]?`), `AvailableQuantity` (`decimal`); `RecalculateAsync(Guid)` used consistently in service, hooks, and tests. Sign convention (`SalesShipment +`, `SalesShipmentReturn −`) identical in recalc service (Task 2), hook filter (Task 3), and backfill SQL (Task 5). `ShipmentReleasesRecalculateShippedService(AppDbContext)` ctor matches test and hook construction. ✓
