# ShipmentRelease Close/Reopen + Manual Recalc Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add close/reopen (reusing `ReleaseStatus.Completed`) and manual balance recalculation (single + all) for `ShipmentRelease`, block new romaneios against non-shippable releases, and refactor `HasStorageTransactions` off the navigation.

**Architecture:** Mirrors the purchase-contract close/recalc feature. New Application services (close/reopen/manual-recalc) exposed as OData actions; a shared movement-guard service called from storage-transaction create/confirm; the manual recalc reuses the internal `ShipmentReleasesRecalculateShippedService`. No migration (`Completed` already exists).

**Tech Stack:** .NET 10, EF Core 10, OData, xUnit + EF InMemory.

## Global Constraints

- Target framework `net10.0`; nullable enabled; `LangVersion` 14.
- `Completed` is the finalized/closed state (reused). Close from `Actived` **or** `Paused`; reopen from `Completed`.
- Movement guard blocks new `SalesShipment`/`SalesShipmentReturn` linked to a release in `Completed`/`Cancelled`/`Paused`. `Actived` allows shipping.
- Manual recalc: `Completed` release → error on single; excluded from all.
- No schema migration.
- DTOs in `SiagroB1.Domain/Dtos/` (namespace `SiagroB1.Domain.Dtos`).
- Services registered in the `// shipment releases` block of `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (~line 232). OData actions in `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (`using SiagroB1.Domain.Dtos;` already present).
- Controllers in `SiagroB1.Web/Actions/ShipmentReleases/`, `ODataController`, `[HttpPost("odata/<Name>")]`, `NotFoundException`/`KeyNotFoundException` → 404, other → 400.
- Tests in `SiagroB1.Application.Tests`; `TestDb.CreateUnitOfWork()` → `_db` / `_db.Context`.
- Run: `dotnet test SiagroB1.Application.Tests --nologo`; build: `dotnet build SiagroB1.sln --nologo` (file-copy locks if the app is running are not compile errors — check for `error CS`).
- Commit only at the end of each task.

## File Structure

- `SiagroB1.Domain/Dtos/ShipmentReleaseRecalcResultDto.cs`, `ShipmentReleaseRecalcAllResultDto.cs` — new DTOs.
- `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateBalanceService.cs` — manual recalc.
- `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesCloseService.cs`, `ShipmentReleasesReopenService.cs` — status transitions.
- `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleaseMovementGuardService.cs` — shared shippable-state guard.
- `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesCancelationService.cs` — refactor `HasStorageTransactions`.
- `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCreateService.cs`, `StorageTransactionsConfirmedService.cs` — call the movement guard.
- `SiagroB1.Domain/Entities/ShipmentRelease.cs` — `[Obsolete]` on `HasStorageTransactions`.
- `SiagroB1.Web/Actions/ShipmentReleases/` — 4 controllers.
- `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, `ODataConfig/ODataConfigurations.cs` — registrations.
- Tests: `ShipmentReleasesRecalculateBalanceServiceTests.cs`, `ShipmentReleasesCloseReopenServiceTests.cs`, `ShipmentReleaseMovementGuardServiceTests.cs`, `ShipmentReleasesCancelationServiceTests.cs`.

---

### Task 1: Recalc DTOs + manual recalc service + DI

**Files:**
- Create: `SiagroB1.Domain/Dtos/ShipmentReleaseRecalcResultDto.cs`, `ShipmentReleaseRecalcAllResultDto.cs`
- Create: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateBalanceService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesRecalculateBalanceServiceTests.cs`

**Interfaces:**
- Consumes: `ShipmentReleasesRecalculateShippedService.RecalculateAsync(Guid)` (existing internal recalc).
- Produces: `ShipmentReleasesRecalculateBalanceService.ExecuteAsync(Guid) : Task<ShipmentReleaseRecalcResultDto>` and `ExecuteAllAsync() : Task<ShipmentReleaseRecalcAllResultDto>`.

- [ ] **Step 1: Write the failing test**

Create `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesRecalculateBalanceServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesRecalculateBalanceServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentReleasesRecalculateBalanceService Service() =>
        new(_db.Context, new ShipmentReleasesRecalculateShippedService(_db.Context));

    private async Task<ShipmentRelease> SeedReleaseAsync(decimal released, decimal shipped, ReleaseStatus status = ReleaseStatus.Actived)
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = released,
            ShippedQuantity = shipped,
            Status = status,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    private StorageTransaction Tx(Guid releaseKey, decimal net) => new()
    {
        Key = Guid.NewGuid(), Code = "ST", CardCode = "F0001", ItemCode = "SOJA",
        UnitOfMeasureCode = "KG", WarehouseCode = "01",
        TransactionType = StorageTransactionType.SalesShipment,
        TransactionStatus = StorageTransactionsStatus.Confirmed,
        NetWeight = net, ShipmentReleaseKey = releaseKey,
    };

    [Fact]
    public async Task ExecuteAsync_CorrectsDivergentShipped_ReportsBeforeAfter()
    {
        var sr = await SeedReleaseAsync(released: 100m, shipped: 999m); // errado
        _db.Context.StorageTransactions.Add(Tx(sr.Key, 30m));
        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAsync(sr.Key);

        Assert.True(result.Changed);
        Assert.Equal(999m, result.PreviousShippedQuantity);
        Assert.Equal(30m, result.NewShippedQuantity);
        Assert.Equal(70m, result.NewAvailableQuantity); // 100 − 30
    }

    [Fact]
    public async Task ExecuteAsync_CompletedRelease_Throws()
    {
        var sr = await SeedReleaseAsync(released: 100m, shipped: 30m, status: ReleaseStatus.Completed);

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(sr.Key));
    }

    [Fact]
    public async Task ExecuteAsync_NotFound_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Service().ExecuteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ExecuteAllAsync_ExcludesCompleted_ListsChanged()
    {
        var ok = await SeedReleaseAsync(released: 100m, shipped: 0m);
        _db.Context.StorageTransactions.Add(Tx(ok.Key, 0m)); // shipped 0 → sem mudança
        var wrong = await SeedReleaseAsync(released: 100m, shipped: 0m);
        _db.Context.StorageTransactions.Add(Tx(wrong.Key, 40m)); // divergente
        var completed = await SeedReleaseAsync(released: 100m, shipped: 777m, status: ReleaseStatus.Completed);
        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAllAsync();

        Assert.Equal(2, result.Scanned);
        Assert.Equal(1, result.Changed);
        Assert.Equal(wrong.Key, result.Changes.Single().Key);
        Assert.Equal(777m, (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == completed.Key)).ShippedQuantity);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleasesRecalculateBalanceServiceTests --nologo`
Expected: FAIL — compile error `ShipmentReleasesRecalculateBalanceService` / `ShipmentReleaseRecalcResultDto` not found.

- [ ] **Step 3: Create the DTOs**

Create `SiagroB1.Domain/Dtos/ShipmentReleaseRecalcResultDto.cs`:

```csharp
namespace SiagroB1.Domain.Dtos;

public class ShipmentReleaseRecalcResultDto
{
    public Guid Key { get; set; }
    public decimal PreviousShippedQuantity { get; set; }
    public decimal NewShippedQuantity { get; set; }
    public decimal PreviousAvailableQuantity { get; set; }
    public decimal NewAvailableQuantity { get; set; }
    public bool Changed { get; set; }
}
```

Create `SiagroB1.Domain/Dtos/ShipmentReleaseRecalcAllResultDto.cs`:

```csharp
namespace SiagroB1.Domain.Dtos;

public class ShipmentReleaseRecalcAllResultDto
{
    public int Scanned { get; set; }
    public int Changed { get; set; }
    public ICollection<ShipmentReleaseRecalcResultDto> Changes { get; set; } = [];
}
```

- [ ] **Step 4: Create the manual recalc service**

Create `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateBalanceService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesRecalculateBalanceService(
    AppDbContext context,
    ShipmentReleasesRecalculateShippedService recalcShipped)
{
    public async Task<ShipmentReleaseRecalcResultDto> ExecuteAsync(Guid key)
    {
        var release = await context.ShipmentReleases
                          .FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Liberação de embarque não encontrada.");

        if (release.Status == ReleaseStatus.Completed)
            throw new ApplicationException("Liberação finalizada não participa do recálculo de saldo.");

        return await RecalculateAsync(release);
    }

    public async Task<ShipmentReleaseRecalcAllResultDto> ExecuteAllAsync()
    {
        var releases = await context.ShipmentReleases
            .Where(x => x.Status != ReleaseStatus.Completed)
            .ToListAsync();

        var changes = new List<ShipmentReleaseRecalcResultDto>();
        foreach (var release in releases)
        {
            var result = await RecalculateAsync(release);
            if (result.Changed)
                changes.Add(result);
        }

        return new ShipmentReleaseRecalcAllResultDto
        {
            Scanned = releases.Count,
            Changed = changes.Count,
            Changes = changes,
        };
    }

    private async Task<ShipmentReleaseRecalcResultDto> RecalculateAsync(ShipmentRelease release)
    {
        var previousShipped = release.ShippedQuantity;
        var previousAvailable = release.AvailableQuantity;

        // recalcShipped usa o MESMO AppDbContext (scoped) → atualiza a mesma instância rastreada.
        await recalcShipped.RecalculateAsync(release.Key);

        return new ShipmentReleaseRecalcResultDto
        {
            Key = release.Key,
            PreviousShippedQuantity = previousShipped,
            NewShippedQuantity = release.ShippedQuantity,
            PreviousAvailableQuantity = previousAvailable,
            NewAvailableQuantity = release.AvailableQuantity,
            Changed = previousShipped != release.ShippedQuantity,
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleasesRecalculateBalanceServiceTests --nologo`
Expected: PASS (4 tests).

- [ ] **Step 6: Register the service in DI**

In `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, in the `// shipment releases` block, add:

```csharp
        services.AddScoped<ShipmentReleasesRecalculateBalanceService>();
```

- [ ] **Step 7: Build and run full suite**

Run: `dotnet build SiagroB1.sln --nologo` → no `error CS`.
Run: `dotnet test SiagroB1.Application.Tests --nologo` → PASS.

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Domain/Dtos/ShipmentReleaseRecalcResultDto.cs SiagroB1.Domain/Dtos/ShipmentReleaseRecalcAllResultDto.cs SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateBalanceService.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesRecalculateBalanceServiceTests.cs
git commit -m "feat: ShipmentRelease manual balance recalculation (single + all)"
```

---

### Task 2: Recalc OData endpoints

**Files:**
- Create: `SiagroB1.Web/Actions/ShipmentReleases/ShipmentReleasesRecalculateBalanceController.cs`, `ShipmentReleasesRecalculateAllBalancesController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`

**Interfaces:**
- Consumes: `ShipmentReleasesRecalculateBalanceService` (Task 1).
- Produces: OData actions `ShipmentReleasesRecalculateBalance` (POST, param `Key`), `ShipmentReleasesRecalculateAllBalances` (POST, no param).

- [ ] **Step 1: Create the single controller**

Create `SiagroB1.Web/Actions/ShipmentReleases/ShipmentReleasesRecalculateBalanceController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentReleases;

public class ShipmentReleasesRecalculateBalanceController(
    ShipmentReleasesRecalculateBalanceService service) : ODataController
{
    [HttpPost("odata/ShipmentReleasesRecalculateBalance")]
    public async Task<IActionResult> RecalculateAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Missing required parameters");

            var key = Guid.Parse(keyObj.ToString());
            var result = await service.ExecuteAsync(key);
            return Ok(result);
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
```

- [ ] **Step 2: Create the all controller**

Create `SiagroB1.Web/Actions/ShipmentReleases/ShipmentReleasesRecalculateAllBalancesController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentReleases;

namespace SiagroB1.Web.Actions.ShipmentReleases;

public class ShipmentReleasesRecalculateAllBalancesController(
    ShipmentReleasesRecalculateBalanceService service) : ODataController
{
    [HttpPost("odata/ShipmentReleasesRecalculateAllBalances")]
    public async Task<IActionResult> RecalculateAllAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var result = await service.ExecuteAllAsync();
            return Ok(result);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

- [ ] **Step 3: Register the actions in ODataConfigurations**

In `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, near the other shipment-release actions (e.g. after the `shipmentReleasesPurchaseContracts` block), add:

```csharp
        var shipmentReleasesRecalculateBalance = modelBuilder.Action("ShipmentReleasesRecalculateBalance");
        shipmentReleasesRecalculateBalance.Parameter<Guid>("Key");
        shipmentReleasesRecalculateBalance.Returns<ShipmentReleaseRecalcResultDto>();

        var shipmentReleasesRecalculateAllBalances = modelBuilder.Action("ShipmentReleasesRecalculateAllBalances");
        shipmentReleasesRecalculateAllBalances.Returns<ShipmentReleaseRecalcAllResultDto>();
```

- [ ] **Step 4: Build the solution**

Run: `dotnet build SiagroB1.sln --nologo` → no `error CS`.

- [ ] **Step 5: Run full suite**

Run: `dotnet test SiagroB1.Application.Tests --nologo` → PASS.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Web/Actions/ShipmentReleases/ SiagroB1.Web/ODataConfig/ODataConfigurations.cs
git commit -m "feat: OData actions for ShipmentRelease balance recalculation"
```

---

### Task 3: Close/Reopen services + DI

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesCloseService.cs`, `ShipmentReleasesReopenService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesCloseReopenServiceTests.cs`

**Interfaces:**
- Produces: `ShipmentReleasesCloseService.ExecuteAsync(Guid, string) : Task`, `ShipmentReleasesReopenService.ExecuteAsync(Guid, string) : Task`.

- [ ] **Step 1: Write the failing test**

Create `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesCloseReopenServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesCloseReopenServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<ShipmentRelease> SeedAsync(ReleaseStatus status)
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = 100m,
            Status = status,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    private async Task<ShipmentRelease> ReloadAsync(Guid key) =>
        await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key);

    [Theory]
    [InlineData(ReleaseStatus.Actived)]
    [InlineData(ReleaseStatus.Paused)]
    public async Task Close_ActivedOrPaused_BecomesCompleted(ReleaseStatus from)
    {
        var sr = await SeedAsync(from);

        await new ShipmentReleasesCloseService(_db.Context).ExecuteAsync(sr.Key, "paulo.penalva");

        var reloaded = await ReloadAsync(sr.Key);
        Assert.Equal(ReleaseStatus.Completed, reloaded.Status);
        Assert.Equal("paulo.penalva", reloaded.UpdatedBy);
    }

    [Fact]
    public async Task Close_PendingRelease_Throws()
    {
        var sr = await SeedAsync(ReleaseStatus.Pending);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ShipmentReleasesCloseService(_db.Context).ExecuteAsync(sr.Key, "tester"));
    }

    [Fact]
    public async Task Reopen_Completed_BecomesActived()
    {
        var sr = await SeedAsync(ReleaseStatus.Completed);

        await new ShipmentReleasesReopenService(_db.Context).ExecuteAsync(sr.Key, "tester");

        Assert.Equal(ReleaseStatus.Actived, (await ReloadAsync(sr.Key)).Status);
    }

    [Fact]
    public async Task Reopen_NotCompleted_Throws()
    {
        var sr = await SeedAsync(ReleaseStatus.Actived);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new ShipmentReleasesReopenService(_db.Context).ExecuteAsync(sr.Key, "tester"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleasesCloseReopenServiceTests --nologo`
Expected: FAIL — compile error: `ShipmentReleasesCloseService`/`ShipmentReleasesReopenService` not found.

- [ ] **Step 3: Create the Close service**

Create `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesCloseService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesCloseService(AppDbContext context)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var release = await context.ShipmentReleases
                          .FirstOrDefaultAsync(x => x.Key == key &&
                              (x.Status == ReleaseStatus.Actived || x.Status == ReleaseStatus.Paused))
                      ?? throw new NotFoundException("Liberação não encontrada ou não está ativa/pausada.");

        release.Status = ReleaseStatus.Completed;
        release.UpdatedAt = DateTime.Now;
        release.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Create the Reopen service**

Create `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesReopenService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesReopenService(AppDbContext context)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var release = await context.ShipmentReleases
                          .FirstOrDefaultAsync(x => x.Key == key && x.Status == ReleaseStatus.Completed)
                      ?? throw new NotFoundException("Liberação não encontrada ou não está finalizada.");

        release.Status = ReleaseStatus.Actived;
        release.UpdatedAt = DateTime.Now;
        release.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleasesCloseReopenServiceTests --nologo`
Expected: PASS (5 test cases).

- [ ] **Step 6: Register both services in DI**

In the `// shipment releases` block, add:

```csharp
        services.AddScoped<ShipmentReleasesCloseService>();
        services.AddScoped<ShipmentReleasesReopenService>();
```

- [ ] **Step 7: Build and run full suite**

Run: `dotnet build SiagroB1.sln --nologo` → no `error CS`.
Run: `dotnet test SiagroB1.Application.Tests --nologo` → PASS.

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesCloseService.cs SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesReopenService.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesCloseReopenServiceTests.cs
git commit -m "feat: ShipmentRelease close/reopen services (Completed status)"
```

---

### Task 4: Close/Reopen OData endpoints

**Files:**
- Create: `SiagroB1.Web/Actions/ShipmentReleases/ShipmentReleasesCloseController.cs`, `ShipmentReleasesReopenController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`

**Interfaces:**
- Consumes: `ShipmentReleasesCloseService`, `ShipmentReleasesReopenService` (Task 3).
- Produces: OData actions `ShipmentReleasesClose`, `ShipmentReleasesReopen` (POST, param `Key`).

- [ ] **Step 1: Create the Close controller**

Create `SiagroB1.Web/Actions/ShipmentReleases/ShipmentReleasesCloseController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentReleases;

public class ShipmentReleasesCloseController(
    ShipmentReleasesCloseService service) : ODataController
{
    [HttpPost("odata/ShipmentReleasesClose")]
    public async Task<IActionResult> CloseAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Missing required parameters");

            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString());
            await service.ExecuteAsync(key, userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
```

- [ ] **Step 2: Create the Reopen controller**

Create `SiagroB1.Web/Actions/ShipmentReleases/ShipmentReleasesReopenController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentReleases;

public class ShipmentReleasesReopenController(
    ShipmentReleasesReopenService service) : ODataController
{
    [HttpPost("odata/ShipmentReleasesReopen")]
    public async Task<IActionResult> ReopenAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Missing required parameters");

            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString());
            await service.ExecuteAsync(key, userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
```

- [ ] **Step 3: Register the actions in ODataConfigurations**

In `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, near the other shipment-release actions, add:

```csharp
        var shipmentReleasesClose = modelBuilder.Action("ShipmentReleasesClose");
        shipmentReleasesClose.Parameter<Guid>("Key");
        shipmentReleasesClose.Returns<IActionResult>();

        var shipmentReleasesReopen = modelBuilder.Action("ShipmentReleasesReopen");
        shipmentReleasesReopen.Parameter<Guid>("Key");
        shipmentReleasesReopen.Returns<IActionResult>();
```

- [ ] **Step 4: Build the solution**

Run: `dotnet build SiagroB1.sln --nologo` → no `error CS`.

- [ ] **Step 5: Run full suite**

Run: `dotnet test SiagroB1.Application.Tests --nologo` → PASS.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Web/Actions/ShipmentReleases/ SiagroB1.Web/ODataConfig/ODataConfigurations.cs
git commit -m "feat: OData actions for ShipmentRelease close/reopen"
```

---

### Task 5: Movement guard (new romaneio blocked on non-shippable releases)

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleaseMovementGuardService.cs`
- Modify: `StorageTransactionsCreateService.cs`, `StorageTransactionsConfirmedService.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleaseMovementGuardServiceTests.cs`

**Interfaces:**
- Produces: `ShipmentReleaseMovementGuardService.EnsureCanShipAsync(StorageTransaction) : Task` — throws `ApplicationException` if the transaction is `SalesShipment`/`SalesShipmentReturn` linked to a release in `Completed`/`Cancelled`/`Paused`.

- [ ] **Step 1: Write the failing test**

Create `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleaseMovementGuardServiceTests.cs`:

```csharp
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleaseMovementGuardServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<Guid> SeedReleaseAsync(ReleaseStatus status)
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01", ReleasedQuantity = 100m, Status = status,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr.Key;
    }

    private static StorageTransaction SalesTx(Guid releaseKey) => new()
    {
        Key = Guid.NewGuid(), Code = "ST", CardCode = "F0001", ItemCode = "SOJA",
        UnitOfMeasureCode = "KG", WarehouseCode = "01",
        TransactionType = StorageTransactionType.SalesShipment,
        ShipmentReleaseKey = releaseKey,
    };

    [Theory]
    [InlineData(ReleaseStatus.Completed)]
    [InlineData(ReleaseStatus.Cancelled)]
    [InlineData(ReleaseStatus.Paused)]
    public async Task EnsureCanShip_NonShippableRelease_Throws(ReleaseStatus status)
    {
        var key = await SeedReleaseAsync(status);
        var service = new ShipmentReleaseMovementGuardService(_db.Context);

        await Assert.ThrowsAsync<ApplicationException>(() => service.EnsureCanShipAsync(SalesTx(key)));
    }

    [Fact]
    public async Task EnsureCanShip_ActivedRelease_DoesNotThrow()
    {
        var key = await SeedReleaseAsync(ReleaseStatus.Actived);
        var service = new ShipmentReleaseMovementGuardService(_db.Context);

        await service.EnsureCanShipAsync(SalesTx(key)); // no throw
    }

    [Fact]
    public async Task EnsureCanShip_NoReleaseKeyOrNonSalesType_DoesNotThrow()
    {
        var service = new ShipmentReleaseMovementGuardService(_db.Context);

        await service.EnsureCanShipAsync(new StorageTransaction
        {
            Key = Guid.NewGuid(), CardCode = "F", ItemCode = "S", UnitOfMeasureCode = "KG", WarehouseCode = "01",
            TransactionType = StorageTransactionType.Purchase, ShipmentReleaseKey = null,
        });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleaseMovementGuardServiceTests --nologo`
Expected: FAIL — compile error: `ShipmentReleaseMovementGuardService` not found.

- [ ] **Step 3: Create the guard service**

Create `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleaseMovementGuardService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleaseMovementGuardService(AppDbContext context)
{
    /// <summary>
    /// Rejeita romanear (SalesShipment/SalesShipmentReturn) contra uma liberação
    /// não disponível: Completed (finalizada), Cancelled ou Paused.
    /// </summary>
    public async Task EnsureCanShipAsync(StorageTransaction transaction)
    {
        if (transaction.ShipmentReleaseKey is not { } releaseKey)
            return;

        if (transaction.TransactionType is not (StorageTransactionType.SalesShipment
            or StorageTransactionType.SalesShipmentReturn))
            return;

        var status = await context.ShipmentReleases
            .Where(r => r.Key == releaseKey)
            .Select(r => (ReleaseStatus?)r.Status)
            .FirstOrDefaultAsync();

        if (status is ReleaseStatus.Completed or ReleaseStatus.Cancelled or ReleaseStatus.Paused)
            throw new ApplicationException(
                "Liberação de embarque finalizada/cancelada/pausada: não é possível romanear.");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleaseMovementGuardServiceTests --nologo`
Expected: PASS (5 test cases).

- [ ] **Step 5: Register the guard in DI**

In the `// shipment releases` block, add:

```csharp
        services.AddScoped<ShipmentReleaseMovementGuardService>();
```

- [ ] **Step 6: Wire the guard into `StorageTransactionsCreateService`**

Add `using SiagroB1.Application.Services.ShipmentReleases;` and the guard dependency to the constructor:

```csharp
public class StorageTransactionsCreateService(
    IUnitOfWork unitOfWork,
    DocNumberSequenceService numberSequenceService,
    IBusinessPartnerService  businessPartnerService,
    IItemService itemService,
    IWarehouseService warehouseService,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    ShipmentReleaseMovementGuardService movementGuard,
    ILogger<StorageTransactionsCreateService> logger)
{
```

In `ExecuteAsync`, call the guard BEFORE the `try` (so its `ApplicationException` is not swallowed by the `catch` that wraps everything in `DefaultException`). Insert right after the `entity.DocNumberKey ??= ...` line and before `try`:

```csharp
        await movementGuard.EnsureCanShipAsync(entity);
```

- [ ] **Step 7: Wire the guard into `StorageTransactionsConfirmedService`**

Add `ShipmentReleaseMovementGuardService movementGuard` to the constructor (`using` already added in the prior feature). At the START of `Processing`, before the `switch`:

```csharp
        await movementGuard.EnsureCanShipAsync(st);
```

- [ ] **Step 8: Build and run full suite**

Run: `dotnet build SiagroB1.sln --nologo` → no `error CS`.
Run: `dotnet test SiagroB1.Application.Tests --nologo` → PASS.

Note: the create/confirm wiring is a one-line call to the tested guard service; verified by build (their heavy dependencies make full unit tests disproportionate).

- [ ] **Step 9: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentReleases/ShipmentReleaseMovementGuardService.cs SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCreateService.cs SiagroB1.Application/Services/StorageTransactions/StorageTransactionsConfirmedService.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleaseMovementGuardServiceTests.cs
git commit -m "feat: block new romaneio against completed/cancelled/paused shipment release"
```

---

### Task 6: Refactor `HasStorageTransactions` off the navigation

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesCancelationService.cs`
- Modify: `SiagroB1.Domain/Entities/ShipmentRelease.cs`
- Test: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesCancelationServiceTests.cs`

**Interfaces:**
- Produces: `ShipmentReleasesCancelationService` blocks cancellation via a direct DB query (no reliance on the loaded `Transactions` navigation).

- [ ] **Step 1: Write the failing test**

Create `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesCancelationServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesCancelationServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentReleasesCancelationService Service() =>
        new(_db.Context, NullLogger<ShipmentReleasesCancelationService>.Instance);

    private async Task<ShipmentRelease> SeedReleaseAsync()
    {
        var sr = new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01", ReleasedQuantity = 100m, Status = ReleaseStatus.Actived,
        };
        _db.Context.ShipmentReleases.Add(sr);
        await _db.Context.SaveChangesAsync();
        return sr;
    }

    [Fact]
    public async Task Cancel_WithLiveTransaction_ThrowsListingCodes()
    {
        var sr = await SeedReleaseAsync();
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "ST-777", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "01",
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentReleaseKey = sr.Key,
        });
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(sr.Key));
        Assert.Contains("ST-777", ex.Message);
    }

    [Fact]
    public async Task Cancel_NoTransactions_Cancels()
    {
        var sr = await SeedReleaseAsync();

        await Service().ExecuteAsync(sr.Key);

        Assert.Equal(ReleaseStatus.Cancelled, (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == sr.Key)).Status);
    }
}
```

- [ ] **Step 2: Run test to verify it passes against current code, then confirm the refactor keeps it green**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleasesCancelationServiceTests --nologo`
Expected: PASS against the CURRENT code (it already blocks via `HasStorageTransactions` with the loaded navigation). This test pins the behavior so the refactor is safe.

- [ ] **Step 3: Refactor the cancelation service to a direct query**

In `ShipmentReleasesCancelationService.cs`, remove `.Include(x => x.Transactions)` from the release load, and replace the `if (sr.HasStorageTransactions) { ... }` block with a direct query:

```csharp
        var blockingCodes = await context.StorageTransactions
            .Where(t => t.ShipmentReleaseKey == sr.Key &&
                        t.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                        (t.TransactionType == StorageTransactionType.SalesShipment ||
                         t.TransactionType == StorageTransactionType.SalesShipmentReturn ||
                         t.TransactionType == StorageTransactionType.Purchase ||
                         t.TransactionType == StorageTransactionType.PurchaseReturn))
            .Select(t => t.Code)
            .ToListAsync();

        if (blockingCodes.Count > 0)
        {
            var msg = "Shipment Release has storage transaction(s) confirmed.\n"
                      + "Please, cancel storage transaction(s) code(s):\n"
                      + string.Join("\n", blockingCodes.Select(c => $"- {c}"));

            throw new ApplicationException(msg);
        }
```

(The release load becomes `var sr = await context.ShipmentReleases.FirstOrDefaultAsync(x => x.Key == key) ?? throw ...;`.)

- [ ] **Step 4: Mark `HasStorageTransactions` obsolete**

In `SiagroB1.Domain/Entities/ShipmentRelease.cs`, add the attribute above `HasStorageTransactions`:

```csharp
    [Obsolete("Depende da navegação Transactions carregada; prefira uma query direta em StorageTransactions. Ver ShipmentReleasesCancelationService.")]
    [NotMapped]
    public bool HasStorageTransactions => Transactions
```

(Keep the property body unchanged; it stays for backward compatibility but is no longer used in code.)

- [ ] **Step 5: Run tests to verify they still pass**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleasesCancelationServiceTests --nologo`
Expected: PASS (behavior preserved via direct query).

- [ ] **Step 6: Build and run full suite**

Run: `dotnet build SiagroB1.sln --nologo` → no `error CS` (an obsolete-usage warning would only appear if something still referenced `HasStorageTransactions`; nothing does).
Run: `dotnet test SiagroB1.Application.Tests --nologo` → PASS.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesCancelationService.cs SiagroB1.Domain/Entities/ShipmentRelease.cs SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesCancelationServiceTests.cs
git commit -m "refactor: ShipmentRelease cancel checks transactions via direct query; deprecate HasStorageTransactions"
```

---

## Self-Review

**Spec coverage:**
- Parte A close/reopen services → Task 3; endpoints → Task 4. ✓
- Parte A movement guard (create + confirm, Completed/Cancelled/Paused) → Task 5. ✓
- Parte B manual recalc (single/all, Completed→error, before/after) → Task 1; endpoints → Task 2. ✓
- Parte C HasStorageTransactions refactor + [Obsolete] → Task 6. ✓
- No migration → confirmed (Completed exists). ✓
- Tests 1-10 from spec → Task 3 (1-4), Task 1 (5-8), Task 5 (9), Task 6 (10). ✓
- Frontend out of scope (separate phase). ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code; commands have expected output. ✓

**Type consistency:** `ShipmentReleaseRecalcResultDto`/`...AllResultDto` fields consistent across service, DTO, ODataConfig `Returns<>`. Service signatures (`ExecuteAsync(Guid)`, `ExecuteAllAsync()`, `ExecuteAsync(Guid,string)`, `EnsureCanShipAsync(StorageTransaction)`) consistent between tasks, controllers, and tests. Movement guard blocks `Completed`/`Cancelled`/`Paused` identically in service (Task 5) and spec. Close accepts `Actived`/`Paused`; reopen from `Completed`; recalc excludes/rejects `Completed` — consistent throughout. ✓
