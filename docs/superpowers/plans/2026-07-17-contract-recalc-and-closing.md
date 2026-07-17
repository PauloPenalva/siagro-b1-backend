# Contract Recalc + Closing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a manual purchase-contract balance recalculation service (single + all, with before/after results) and a contract closing/reopening mechanism (reusing `ContractStatus.Finished`) that blocks new movement and is excluded from bulk recalculation.

**Architecture:** New Application services following the one-class-per-operation convention, exposed as OData actions (controller + `ODataConfigurations` + DI registration). Closing reuses the existing `ContractStatus.Finished` enum value (no migration). Movement guards added to allocation-create and shipment-release-create.

**Tech Stack:** .NET 10, EF Core 10, OData (`Microsoft.AspNetCore.OData`), xUnit + EF InMemory.

## Global Constraints

- Target framework `net10.0`; nullable enabled; `LangVersion` 14.
- Recalc formula is **signed** `Sum(a => a.Volume)` (matches the runtime/backfill; `PurchaseReturn` is negative).
- No schema migration — `ContractStatus.Finished` (value 2) already exists.
- DTOs go in `SiagroB1.Domain/Dtos/` (namespace `SiagroB1.Domain.Dtos`).
- Services registered in `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` under the `// purchase contracts` block (~line 156).
- OData actions registered in `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (which already has `using SiagroB1.Domain.Dtos;`).
- Controllers go in `SiagroB1.Web/Actions/PurchaseContracts/`, extend `ODataController`, use `[HttpPost("odata/<ActionName>")]`, map `NotFoundException`/`KeyNotFoundException` → 404 and other exceptions → 400.
- Tests in `SiagroB1.Application.Tests`; use `TestDb.CreateUnitOfWork()` (the `AppDbContext` is `_db.Context`).
- Run tests: `dotnet test SiagroB1.Application.Tests --nologo`. Build: `dotnet build SiagroB1.sln --nologo`.
- Business-rule exception messages in Portuguese (matches existing `PurchaseContractsCancelService`).

## File Structure

- `SiagroB1.Domain/Dtos/PurchaseContractRecalcResultDto.cs` — per-contract before/after result.
- `SiagroB1.Domain/Dtos/PurchaseContractRecalcAllResultDto.cs` — batch summary.
- `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsRecalculateBalanceService.cs` — recalc logic.
- `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCloseService.cs` — Approved→Finished.
- `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsReopenService.cs` — Finished→Approved.
- `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsRecalculateBalanceController.cs` — action (single).
- `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsRecalculateAllBalancesController.cs` — action (all).
- `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsCloseController.cs` — action.
- `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsReopenController.cs` — action.
- Modify: `PurchaseContractsAllocationCreateService.cs` (Finished guard, both overloads), `ShipmentReleasesCreateService.cs` (Finished guard), `ServiceCollectionExtensions.cs`, `ODataConfigurations.cs`.
- Tests: `PurchaseContractsRecalculateBalanceServiceTests.cs`, `PurchaseContractsCloseReopenServiceTests.cs`, `ShipmentReleasesCreateServiceTests.cs` (new); add to `PurchaseContractsAllocationCreateServiceTests.cs`.

---

### Task 1: Recalc DTOs + service + DI

**Files:**
- Create: `SiagroB1.Domain/Dtos/PurchaseContractRecalcResultDto.cs`, `SiagroB1.Domain/Dtos/PurchaseContractRecalcAllResultDto.cs`
- Create: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsRecalculateBalanceService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsRecalculateBalanceServiceTests.cs`

**Interfaces:**
- Produces: `PurchaseContractsRecalculateBalanceService.ExecuteAsync(Guid key) : Task<PurchaseContractRecalcResultDto>` and `ExecuteAllAsync() : Task<PurchaseContractRecalcAllResultDto>`.

- [ ] **Step 1: Write the failing test**

Create `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsRecalculateBalanceServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractsRecalculateBalanceServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsRecalculateBalanceService Service() => new(_db.Context);

    private static PurchaseContract NewContract(
        decimal totalVolume, decimal allocatedVolume,
        ContractStatus status = ContractStatus.Approved) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = totalVolume,
        AllocatedVolume = allocatedVolume,
        Status = status,
    };

    private PurchaseContractAllocation NewAllocation(Guid contractKey, decimal volume) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contractKey,
        StorageTransactionKey = Guid.NewGuid(),
        Volume = volume,
    };

    [Fact]
    public async Task ExecuteAsync_CorrectsDivergentAllocatedVolume_AndReportsBeforeAfter()
    {
        var pc = NewContract(totalVolume: 5000m, allocatedVolume: 999m); // valor errado
        _db.Context.PurchaseContracts.Add(pc);
        _db.Context.PurchaseContractsAllocations.AddRange(
            NewAllocation(pc.Key, 300m), NewAllocation(pc.Key, 200m));
        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAsync(pc.Key);

        Assert.True(result.Changed);
        Assert.Equal(999m, result.PreviousAllocatedVolume);
        Assert.Equal(500m, result.NewAllocatedVolume);
        Assert.Equal(4500m, result.NewAvaiableVolume); // 5000 − 500
        Assert.Equal(500m, (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == pc.Key)).AllocatedVolume);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyCorrect_ReturnsChangedFalse()
    {
        var pc = NewContract(totalVolume: 5000m, allocatedVolume: 500m);
        _db.Context.PurchaseContracts.Add(pc);
        _db.Context.PurchaseContractsAllocations.AddRange(
            NewAllocation(pc.Key, 300m), NewAllocation(pc.Key, 200m));
        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAsync(pc.Key);

        Assert.False(result.Changed);
        Assert.Equal(500m, result.NewAllocatedVolume);
    }

    [Fact]
    public async Task ExecuteAsync_FinishedContract_Throws()
    {
        var pc = NewContract(totalVolume: 5000m, allocatedVolume: 500m, status: ContractStatus.Finished);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(pc.Key));
    }

    [Fact]
    public async Task ExecuteAsync_NotFound_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Service().ExecuteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ExecuteAllAsync_RecalculatesNonFinished_ExcludesFinished_ListsChanged()
    {
        var ok = NewContract(totalVolume: 1000m, allocatedVolume: 100m); // correto
        _db.Context.PurchaseContracts.Add(ok);
        _db.Context.PurchaseContractsAllocations.Add(NewAllocation(ok.Key, 100m));

        var wrong = NewContract(totalVolume: 1000m, allocatedVolume: 0m);  // divergente
        _db.Context.PurchaseContracts.Add(wrong);
        _db.Context.PurchaseContractsAllocations.Add(NewAllocation(wrong.Key, 250m));

        var finished = NewContract(totalVolume: 1000m, allocatedVolume: 777m, status: ContractStatus.Finished);
        _db.Context.PurchaseContracts.Add(finished);
        _db.Context.PurchaseContractsAllocations.Add(NewAllocation(finished.Key, 100m));

        await _db.Context.SaveChangesAsync();

        var result = await Service().ExecuteAllAsync();

        Assert.Equal(2, result.Scanned);                 // ok + wrong (finished excluído)
        Assert.Equal(1, result.Changed);                 // só o wrong divergia
        Assert.Single(result.Changes);
        Assert.Equal(wrong.Key, result.Changes.First().Key);
        Assert.Equal(250m, result.Changes.First().NewAllocatedVolume);
        // finished intocado
        Assert.Equal(777m, (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == finished.Key)).AllocatedVolume);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractsRecalculateBalanceServiceTests --nologo`
Expected: FAIL — compile error `type or namespace 'PurchaseContractsRecalculateBalanceService' could not be found` / `PurchaseContractRecalcResultDto` not found.

- [ ] **Step 3: Create the DTOs**

Create `SiagroB1.Domain/Dtos/PurchaseContractRecalcResultDto.cs`:

```csharp
namespace SiagroB1.Domain.Dtos;

public class PurchaseContractRecalcResultDto
{
    public Guid Key { get; set; }
    public string? Code { get; set; }
    public decimal PreviousAllocatedVolume { get; set; }
    public decimal NewAllocatedVolume { get; set; }
    public decimal PreviousAvaiableVolume { get; set; }
    public decimal NewAvaiableVolume { get; set; }
    public bool Changed { get; set; }
}
```

Create `SiagroB1.Domain/Dtos/PurchaseContractRecalcAllResultDto.cs`:

```csharp
namespace SiagroB1.Domain.Dtos;

public class PurchaseContractRecalcAllResultDto
{
    public int Scanned { get; set; }
    public int Changed { get; set; }
    public ICollection<PurchaseContractRecalcResultDto> Changes { get; set; } = [];
}
```

- [ ] **Step 4: Create the service**

Create `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsRecalculateBalanceService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsRecalculateBalanceService(AppDbContext context)
{
    public async Task<PurchaseContractRecalcResultDto> ExecuteAsync(Guid key)
    {
        var contract = await context.PurchaseContracts
                           .FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Contrato não encontrado.");

        if (contract.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado não participa do recálculo de saldo.");

        var result = await RecalculateAsync(contract);
        await context.SaveChangesAsync();
        return result;
    }

    public async Task<PurchaseContractRecalcAllResultDto> ExecuteAllAsync()
    {
        var contracts = await context.PurchaseContracts
            .Where(x => x.Status != ContractStatus.Finished)
            .ToListAsync();

        var changes = new List<PurchaseContractRecalcResultDto>();
        foreach (var contract in contracts)
        {
            var result = await RecalculateAsync(contract);
            if (result.Changed)
                changes.Add(result);
        }

        await context.SaveChangesAsync();

        return new PurchaseContractRecalcAllResultDto
        {
            Scanned = contracts.Count,
            Changed = changes.Count,
            Changes = changes,
        };
    }

    private async Task<PurchaseContractRecalcResultDto> RecalculateAsync(PurchaseContract contract)
    {
        // Σ com sinal — igual ao runtime/backfill.
        var newAllocated = await context.PurchaseContractsAllocations
            .Where(a => a.PurchaseContractKey == contract.Key)
            .SumAsync(a => a.Volume);

        var previousAllocated = contract.AllocatedVolume;
        var previousAvaiable = contract.AvaiableVolume;

        contract.AllocatedVolume = newAllocated;

        return new PurchaseContractRecalcResultDto
        {
            Key = contract.Key,
            Code = contract.Code,
            PreviousAllocatedVolume = previousAllocated,
            NewAllocatedVolume = newAllocated,
            PreviousAvaiableVolume = previousAvaiable,
            NewAvaiableVolume = contract.AvaiableVolume,
            Changed = previousAllocated != newAllocated,
        };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractsRecalculateBalanceServiceTests --nologo`
Expected: PASS (5 tests).

- [ ] **Step 6: Register the service in DI**

In `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, in the `// purchase contracts` block (after `services.AddScoped<PurchaseContractsQualityParametersUpdateService>();` or any line in that block), add:

```csharp
        services.AddScoped<PurchaseContractsRecalculateBalanceService>();
```

- [ ] **Step 7: Build and run full suite**

Run: `dotnet build SiagroB1.sln --nologo` → Expected: `0 Erro(s)`.
Run: `dotnet test SiagroB1.Application.Tests --nologo` → Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Domain/Dtos/PurchaseContractRecalcResultDto.cs SiagroB1.Domain/Dtos/PurchaseContractRecalcAllResultDto.cs SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsRecalculateBalanceService.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsRecalculateBalanceServiceTests.cs
git commit -m "feat: purchase contract balance recalculation service (single + all)"
```

---

### Task 2: Recalc OData endpoints

**Files:**
- Create: `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsRecalculateBalanceController.cs`, `PurchaseContractsRecalculateAllBalancesController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`

**Interfaces:**
- Consumes: `PurchaseContractsRecalculateBalanceService` (Task 1).
- Produces: OData actions `PurchaseContractsRecalculateBalance` (POST, param `Key`) and `PurchaseContractsRecalculateAllBalances` (POST, no param).

- [ ] **Step 1: Create the single-contract controller**

Create `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsRecalculateBalanceController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsRecalculateBalanceController(
    PurchaseContractsRecalculateBalanceService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsRecalculateBalance")]
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

- [ ] **Step 2: Create the all-contracts controller**

Create `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsRecalculateAllBalancesController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsRecalculateAllBalancesController(
    PurchaseContractsRecalculateBalanceService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsRecalculateAllBalances")]
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

In `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, near the other purchase-contract actions (e.g. after the `purchaseContractsDeleteAllocation` block around line 219), add:

```csharp
        var purchaseContractsRecalculateBalance = modelBuilder.Action("PurchaseContractsRecalculateBalance");
        purchaseContractsRecalculateBalance.Parameter<Guid>("Key");
        purchaseContractsRecalculateBalance.Returns<PurchaseContractRecalcResultDto>();

        var purchaseContractsRecalculateAllBalances = modelBuilder.Action("PurchaseContractsRecalculateAllBalances");
        purchaseContractsRecalculateAllBalances.Returns<PurchaseContractRecalcAllResultDto>();
```

- [ ] **Step 4: Build the solution**

Run: `dotnet build SiagroB1.sln --nologo`
Expected: `Compilação com êxito. 0 Erro(s)`.

- [ ] **Step 5: Run full suite**

Run: `dotnet test SiagroB1.Application.Tests --nologo`
Expected: PASS (unchanged; wiring only).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsRecalculateBalanceController.cs SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsRecalculateAllBalancesController.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs
git commit -m "feat: OData actions for purchase contract balance recalculation"
```

---

### Task 3: Close/Reopen services + DI

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCloseService.cs`, `PurchaseContractsReopenService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsCloseReopenServiceTests.cs`

**Interfaces:**
- Produces: `PurchaseContractsCloseService.ExecuteAsync(Guid key, string userName) : Task` and `PurchaseContractsReopenService.ExecuteAsync(Guid key, string userName) : Task`.

- [ ] **Step 1: Write the failing test**

Create `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsCloseReopenServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractsCloseReopenServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static PurchaseContract NewContract(ContractStatus status) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = 1000m,
        Status = status,
    };

    private async Task<PurchaseContract> SeedAsync(ContractStatus status)
    {
        var pc = NewContract(status);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();
        return pc;
    }

    private async Task<PurchaseContract> ReloadAsync(Guid key) =>
        await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Close_ApprovedContract_BecomesFinished_AndRecordsUser()
    {
        var pc = await SeedAsync(ContractStatus.Approved);

        await new PurchaseContractsCloseService(_db.Context).ExecuteAsync(pc.Key, "paulo.penalva");

        var contract = await ReloadAsync(pc.Key);
        Assert.Equal(ContractStatus.Finished, contract.Status);
        Assert.Equal("paulo.penalva", contract.UpdatedBy);
    }

    [Fact]
    public async Task Close_NonApprovedContract_Throws()
    {
        var pc = await SeedAsync(ContractStatus.Draft);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new PurchaseContractsCloseService(_db.Context).ExecuteAsync(pc.Key, "tester"));
    }

    [Fact]
    public async Task Reopen_FinishedContract_BecomesApproved()
    {
        var pc = await SeedAsync(ContractStatus.Finished);

        await new PurchaseContractsReopenService(_db.Context).ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Approved, (await ReloadAsync(pc.Key)).Status);
    }

    [Fact]
    public async Task Reopen_NonFinishedContract_Throws()
    {
        var pc = await SeedAsync(ContractStatus.Approved);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new PurchaseContractsReopenService(_db.Context).ExecuteAsync(pc.Key, "tester"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractsCloseReopenServiceTests --nologo`
Expected: FAIL — compile error `PurchaseContractsCloseService`/`PurchaseContractsReopenService` not found.

- [ ] **Step 3: Create the Close service**

Create `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCloseService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsCloseService(AppDbContext context)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await context.PurchaseContracts
                           .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.Approved)
                       ?? throw new NotFoundException("Contrato não encontrado ou não está aprovado.");

        contract.Status = ContractStatus.Finished;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Create the Reopen service**

Create `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsReopenService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsReopenService(AppDbContext context)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await context.PurchaseContracts
                           .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.Finished)
                       ?? throw new NotFoundException("Contrato não encontrado ou não está encerrado.");

        contract.Status = ContractStatus.Approved;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractsCloseReopenServiceTests --nologo`
Expected: PASS (4 tests).

- [ ] **Step 6: Register both services in DI**

In `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, in the `// purchase contracts` block, add:

```csharp
        services.AddScoped<PurchaseContractsCloseService>();
        services.AddScoped<PurchaseContractsReopenService>();
```

- [ ] **Step 7: Build and run full suite**

Run: `dotnet build SiagroB1.sln --nologo` → `0 Erro(s)`.
Run: `dotnet test SiagroB1.Application.Tests --nologo` → PASS.

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCloseService.cs SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsReopenService.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsCloseReopenServiceTests.cs
git commit -m "feat: purchase contract close/reopen services (Finished status)"
```

---

### Task 4: Close/Reopen OData endpoints

**Files:**
- Create: `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsCloseController.cs`, `PurchaseContractsReopenController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`

**Interfaces:**
- Consumes: `PurchaseContractsCloseService`, `PurchaseContractsReopenService` (Task 3).
- Produces: OData actions `PurchaseContractsClose` (POST, param `Key`), `PurchaseContractsReopen` (POST, param `Key`).

- [ ] **Step 1: Create the Close controller**

Create `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsCloseController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsCloseController(
    PurchaseContractsCloseService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsClose")]
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

Create `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsReopenController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsReopenController(
    PurchaseContractsReopenService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsReopen")]
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

In `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, near the other purchase-contract actions, add:

```csharp
        var purchaseContractsClose = modelBuilder.Action("PurchaseContractsClose");
        purchaseContractsClose.Parameter<Guid>("Key");
        purchaseContractsClose.Returns<IActionResult>();

        var purchaseContractsReopen = modelBuilder.Action("PurchaseContractsReopen");
        purchaseContractsReopen.Parameter<Guid>("Key");
        purchaseContractsReopen.Returns<IActionResult>();
```

- [ ] **Step 4: Build the solution**

Run: `dotnet build SiagroB1.sln --nologo`
Expected: `Compilação com êxito. 0 Erro(s)`.

- [ ] **Step 5: Run full suite**

Run: `dotnet test SiagroB1.Application.Tests --nologo`
Expected: PASS (unchanged; wiring only).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsCloseController.cs SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsReopenController.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs
git commit -m "feat: OData actions for purchase contract close/reopen"
```

---

### Task 5: Movement guards on Finished contracts

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationCreateService.cs` (both overloads)
- Modify: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesCreateService.cs`
- Modify: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsAllocationCreateServiceTests.cs`
- Create: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesCreateServiceTests.cs`

**Interfaces:**
- Consumes: `ContractStatus.Finished`.
- Produces: allocation-create and shipment-release-create reject when the purchase contract is `Finished`.

- [ ] **Step 1: Write the failing allocation-guard test**

In `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsAllocationCreateServiceTests.cs`, add this test (the file already has `NewPurchase`, `NewContract`, `CreateService` helpers; `NewContract` builds an `Approved`-less contract — set `Status` here):

```csharp
    [Fact]
    public async Task ExecuteAsync_ContractFinished_ThrowsAndDoesNotAllocate()
    {
        var st = NewPurchase(netWeight: 1000m, available: 1000m);
        var pc = NewContract(totalVolume: 5000m);
        pc.Status = SiagroB1.Domain.Enums.ContractStatus.Finished;
        _db.Context.StorageTransactions.Add(st);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<SiagroB1.Domain.Exceptions.DefaultException>(() =>
            CreateService().ExecuteAsync(pc.Key, st.Key, 300m, "tester"));

        Assert.Equal(0, await _db.Context.PurchaseContractsAllocations.AsNoTracking().CountAsync());
    }
```

Note: the create service wraps business exceptions — the outer `ExecuteAsync` throws `ApplicationException` from the guard, but the surrounding `catch (Exception e) => throw new DefaultException(e.Message)` in the create body only wraps the allocation-building block. The guard is placed BEFORE that try, so it surfaces as `ApplicationException`. Adjust the expected type in Step 3 verification if needed (see note there).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "ExecuteAsync_ContractFinished_ThrowsAndDoesNotAllocate" --nologo`
Expected: FAIL — no exception thrown (allocation is created), so `ThrowsAsync` fails.

- [ ] **Step 3: Add the guard to both allocation-create overloads**

In `PurchaseContractsAllocationCreateService.cs`, FIRST overload `ExecuteAsync(Guid purchaseContractKey, Guid storageTransactionKey, ...)`: right after the two loads

```csharp
        var storageTransaction = await storageTransactionsGetService.GetByIdAsync(storageTransactionKey);
        var purchaseContract = await purchaseContractsGetService.GetByIdAsync(purchaseContractKey);
```

add:

```csharp
        if (purchaseContract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível alocar.");
```

In the SECOND overload `ExecuteAsync(Guid purchaseContractKey, StorageTransaction storageTransaction, ...)`, right after

```csharp
        var purchaseContract = await purchaseContractsGetService.GetByIdAsync(purchaseContractKey);
```

add the identical guard:

```csharp
        if (purchaseContract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível alocar.");
```

(`ContractStatus` is in `SiagroB1.Domain.Enums`, already imported in this file; `ApplicationException` is `System`.)

Because the guard is before the inner `try` (whose `catch` wraps in `DefaultException`), the thrown type is `ApplicationException`. Update the test from Step 1 to expect `ApplicationException` instead of `DefaultException`:

```csharp
        await Assert.ThrowsAsync<System.ApplicationException>(() =>
            CreateService().ExecuteAsync(pc.Key, st.Key, 300m, "tester"));
```

- [ ] **Step 4: Run the allocation-guard test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter "ExecuteAsync_ContractFinished_ThrowsAndDoesNotAllocate" --nologo`
Expected: PASS.

- [ ] **Step 5: Write the failing shipment-release-guard test**

Create `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesCreateServiceTests.cs`:

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

public class ShipmentReleasesCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    // Não deve ser chamado — o guard de contrato encerrado lança antes.
    private sealed class ThrowingWarehouseService : IWarehouseService
    {
        public Task<IEnumerable<WarehouseModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<WarehouseModel?> GetByIdAsync(string code) => throw new NotImplementedException();
        public Task<WarehouseModel> CreateAsync(WarehouseModel model) => throw new NotImplementedException();
        public Task<WarehouseModel?> UpdateAsync(string code, WarehouseModel model) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
        public IQueryable<WarehouseModel> QueryAll() => throw new NotImplementedException();
        public Task<Dictionary<string, WarehouseInfo>> LoadWarehousesAsync() => throw new NotImplementedException();
    }

    private ShipmentReleasesCreateService Service() => new(
        _db, new ThrowingWarehouseService(), NullLogger<ShipmentReleasesCreateService>.Instance);

    [Fact]
    public async Task ExecuteAsync_ContractFinished_Throws()
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 1000m,
            Status = ContractStatus.Finished,
        };
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        var release = new ShipmentRelease
        {
            PurchaseContractKey = pc.Key,
            DeliveryLocationCode = "01",
        };

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(release, "tester"));
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleasesCreateServiceTests --nologo`
Expected: FAIL — no guard yet; the code proceeds past the contract load into the `try` and calls `warehouseService.GetByIdAsync` → `NotImplementedException` (not `ApplicationException`), so `ThrowsAsync<ApplicationException>` fails.

- [ ] **Step 7: Add the guard to ShipmentReleasesCreateService**

In `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesCreateService.cs`, right after the contract load

```csharp
        var purchaseContract = await db.Context.PurchaseContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == entity.PurchaseContractKey) ??
                               throw new NotFoundException("Purchase contract not found.");
```

and BEFORE the `try` block, add:

```csharp
        if (purchaseContract.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível criar liberação de embarque.");
```

(`ContractStatus` is in `SiagroB1.Domain.Enums`, already imported.)

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentReleasesCreateServiceTests --nologo`
Expected: PASS.

- [ ] **Step 9: Build and run full suite**

Run: `dotnet build SiagroB1.sln --nologo` → `0 Erro(s)`.
Run: `dotnet test SiagroB1.Application.Tests --nologo` → PASS (all tasks).

- [ ] **Step 10: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationCreateService.cs SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesCreateService.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsAllocationCreateServiceTests.cs SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesCreateServiceTests.cs
git commit -m "feat: block allocation and shipment-release creation on finished contracts"
```

---

## Self-Review

**Spec coverage:**
- Parte A recalc service (single + all, before/after, skip Finished, not-found) → Task 1 (service + 5 tests). ✓
- Parte A DTOs → Task 1. ✓
- Parte A endpoints → Task 2. ✓
- Parte B reuse Finished, Close/Reopen services → Task 3 (+ 4 tests). ✓
- Parte B endpoints → Task 4. ✓
- Parte B guards (allocation, shipment release) → Task 5 (+ 2 tests). ✓
- Edit already blocked (no work) → documented, no task needed. ✓
- No migration → confirmed (enum value exists). ✓
- Tests 1-11 from spec → Task 1 (1-5), Task 3 (6-9), Task 5 (10-11). ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code; commands have expected output. ✓

**Type consistency:** `PurchaseContractRecalcResultDto`/`PurchaseContractRecalcAllResultDto` fields consistent across service, DTO, and ODataConfig `Returns<>`. Service method names (`ExecuteAsync(Guid)`, `ExecuteAllAsync()`, `ExecuteAsync(Guid,string)`) consistent between tasks, controllers, and tests. Guard uses `ContractStatus.Finished` and throws `ApplicationException` consistently; Task 5 Step 3 note corrects the expected exception type to `ApplicationException`. ✓
