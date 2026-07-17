# Contract Persisted AllocatedVolume Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `PurchaseContract.AvaiableVolume` robust to any read path (including OData `$select`) by persisting `AllocatedVolume` as a mapped column and deriving the balance as scalar arithmetic, eliminating the runtime navigation dependency.

**Architecture:** Persist `AllocatedVolume` (signed Σ of allocation volumes) on `PurchaseContract`; `AvaiableVolume` becomes `TotalVolume − AllocatedVolume`. Recalculate `AllocatedVolume` from a DB `SUM` only in the two allocation services (the cancel-guard invariant guarantees no other event changes the sum). Add a `rowversion` concurrency token, symmetric with the storage-transaction side already implemented.

**Tech Stack:** .NET 10, EF Core 10 (SqlServer), xUnit + EF InMemory, migrations in `SiagroB1.Migrations`.

## Global Constraints

- Target framework `net10.0`; nullable enabled; `LangVersion` 14.
- Contract-side sum is **signed** (`Sum(x => x.Volume)`) — `PurchaseReturn` allocations are negative and return balance. Do NOT use `decimal.Abs` on the contract side (that is the storage-transaction side's rule).
- Recalculation derives from a DB `SUM` in a single place per service — never `+=`/`-=`.
- Migrations are applied via `SiagroB1.Web` with `ASPNETCORE_ENVIRONMENT=Migration`, not `dotnet ef database update`. New migrations are scaffolded with `dotnet ef migrations add ... --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext`.
- Tests live in `SiagroB1.Application.Tests`; use `TestDb.CreateUnitOfWork()` (EF InMemory) and `TestLogger<T>`/`NullLogger<T>`.
- Run tests with `dotnet test SiagroB1.Application.Tests --nologo`. Build with `dotnet build SiagroB1.sln`.
- Commit only at the end of each task.

## File Structure

- `SiagroB1.Domain/Entities/PurchaseContract.cs` — add `AllocatedVolume` (mapped) + `RowVersion` (`[Timestamp]`); simplify `AvaiableVolume` to `TotalVolume − AllocatedVolume`.
- `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationCreateService.cs` — set `purchaseContract.AllocatedVolume` from signed DB sum in both overloads.
- `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationDeleteService.cs` — load the contract, set `AllocatedVolume` from signed DB sum of remaining allocations.
- `SiagroB1.Migrations/AppContext/<timestamp>_AddPurchaseContractAllocatedVolume.cs` — AddColumn(s) + backfill SQL (scaffolded, then hand-edited).
- `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractAvaiableVolumeTests.cs` — new: entity robustness test.
- `SiagroB1.Application.Tests/Infra/PurchaseContractConcurrencyTests.cs` — new: rowversion mapping guard.
- `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsTotalsServiceTests.cs` — update existing test to seed `AllocatedVolume`.
- `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsAllocationCreateServiceTests.cs` — add contract-balance assertions.
- `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsAllocationDeleteServiceTests.cs` — seed contract + assert contract balance.

---

### Task 1: Persist `AllocatedVolume` + `RowVersion`; simplify `AvaiableVolume`

**Files:**
- Modify: `SiagroB1.Domain/Entities/PurchaseContract.cs`
- Create: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractAvaiableVolumeTests.cs`
- Create: `SiagroB1.Application.Tests/Infra/PurchaseContractConcurrencyTests.cs`
- Modify: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsTotalsServiceTests.cs`

**Interfaces:**
- Produces: `PurchaseContract.AllocatedVolume` (`decimal`, mapped column), `PurchaseContract.RowVersion` (`byte[]?`, `[Timestamp]`), and `PurchaseContract.AvaiableVolume` (`decimal`, `[NotMapped]`) now equal to `Round(TotalVolume − AllocatedVolume, 2)` with no navigation access.

- [ ] **Step 1: Write the failing entity robustness test**

Create `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractAvaiableVolumeTests.cs`:

```csharp
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractAvaiableVolumeTests
{
    [Fact]
    public void AvaiableVolume_DerivesFromAllocatedVolume_WithoutLoadingAllocations()
    {
        // Prova que a dependência de navegação sumiu: sem nenhuma alocação
        // carregada, o saldo vem de TotalVolume − AllocatedVolume.
        var contract = new PurchaseContract
        {
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 5000m,
            AllocatedVolume = 1200m,
        };

        Assert.Empty(contract.Allocations);
        Assert.Equal(3800m, contract.AvaiableVolume);
    }

    [Fact]
    public void AvaiableVolume_NegativeAllocated_IncreasesAvailable()
    {
        // Devolução (Volume negativo) reduz o alocado e aumenta o disponível.
        var contract = new PurchaseContract
        {
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 5000m,
            AllocatedVolume = -100m,
        };

        Assert.Equal(5100m, contract.AvaiableVolume);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractAvaiableVolumeTests --nologo`
Expected: FAIL — compile error `'PurchaseContract' does not contain a definition for 'AllocatedVolume'`.

- [ ] **Step 3: Modify the entity**

In `SiagroB1.Domain/Entities/PurchaseContract.cs`:

Add the using at the top (after the existing `using System.ComponentModel.DataAnnotations.Schema;`):

```csharp
using System.ComponentModel.DataAnnotations;
```

Add the mapped column + concurrency token (place near the other scalar columns, e.g. right after the `FunruralType` property, before `AddAttachment`):

```csharp
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal AllocatedVolume { get; set; }

    /// <summary>
    /// Token de concorrência otimista (SQL Server rowversion). Protege
    /// <see cref="AllocatedVolume"/> contra alocações concorrentes ao mesmo contrato.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
```

Replace the current computed `AvaiableVolume` (the `[NotMapped] public decimal AvaiableVolume => ...` block that sums `Allocations` filtered by `StorageTransaction`) with:

```csharp
    /// <summary>
    /// Saldo alocável do contrato, derivado de <see cref="AllocatedVolume"/>
    /// (persistido, recalculado nos serviços de alocação). Não depende de
    /// nenhuma navegação em runtime — funciona sob $select do OData.
    /// </summary>
    [NotMapped]
    public decimal AvaiableVolume =>
        decimal.Round(TotalVolume - AllocatedVolume, 2, MidpointRounding.ToEven);
```

- [ ] **Step 4: Run the robustness test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractAvaiableVolumeTests --nologo`
Expected: PASS (2 tests).

- [ ] **Step 5: Write the failing rowversion mapping guard test**

Create `SiagroB1.Application.Tests/Infra/PurchaseContractConcurrencyTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class PurchaseContractConcurrencyTests
{
    // InMemory não enforça rowversion; este teste garante o mapeamento do token
    // de concorrência otimista (o EF então emite WHERE RowVersion=@orig no SQL Server).
    [Fact]
    public void RowVersion_IsMappedAsConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;

        using var context = new AppDbContext(options);

        var prop = context.Model
            .FindEntityType(typeof(PurchaseContract))!
            .FindProperty(nameof(PurchaseContract.RowVersion));

        Assert.NotNull(prop);
        Assert.True(prop!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, prop.ValueGenerated);
    }
}
```

- [ ] **Step 6: Run the guard test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractConcurrencyTests --nologo`
Expected: PASS (the entity already has `[Timestamp] RowVersion` from Step 3).

- [ ] **Step 7: Update the existing Totals test to seed AllocatedVolume**

In `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsTotalsServiceTests.cs`, replace the whole `GetTotals_SubtractsAllocationsFromTotalVolume` test body so it seeds the persisted `AllocatedVolume` (no allocation/ST rows needed — proves the balance comes from the column, simulating OData projection dropping navigations):

```csharp
    [Fact]
    public async Task GetTotals_SubtractsAllocatedVolumeFromTotalVolume()
    {
        var pc = NewContract(totalVolume: 5000m);
        pc.AllocatedVolume = 5000m; // saldo persistido, sem navegação carregada
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        var totals = await new PurchaseContractsTotalsService(_db.Context).GetTotals(pc.Key);

        Assert.Equal(5000m, totals.TotalVolume);
        Assert.Equal(0m, totals.AvaiableVolume);
    }
```

Remove the now-unused `NewConfirmedPurchase()` helper and any `StorageTransaction`/`PurchaseContractAllocation` seeding in that file if they are no longer referenced (keep `NewContract`).

- [ ] **Step 8: Run the full test suite**

Run: `dotnet test SiagroB1.Application.Tests --nologo`
Expected: PASS, no failures. (Existing ST-side allocation tests are unaffected; the create tests seed a contract via `NewContract` which now has `AllocatedVolume = 0` by default.)

- [ ] **Step 9: Build the solution**

Run: `dotnet build SiagroB1.sln --nologo`
Expected: `Compilação com êxito. 0 Erro(s)`.

- [ ] **Step 10: Commit**

```bash
git add SiagroB1.Domain/Entities/PurchaseContract.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractAvaiableVolumeTests.cs SiagroB1.Application.Tests/Infra/PurchaseContractConcurrencyTests.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsTotalsServiceTests.cs
git commit -m "feat: persist PurchaseContract.AllocatedVolume, derive AvaiableVolume from it"
```

---

### Task 2: Recalculate `AllocatedVolume` on allocation create

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationCreateService.cs`
- Modify: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsAllocationCreateServiceTests.cs`

**Interfaces:**
- Consumes: `PurchaseContract.AllocatedVolume` (from Task 1).
- Produces: after `ExecuteAsync(purchaseContractKey, storageTransactionKey, volume, ...)`, the contract's `AllocatedVolume` equals the signed DB sum of its allocations (including the new one).

- [ ] **Step 1: Write the failing test**

Add to `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsAllocationCreateServiceTests.cs` (the file already has `NewPurchase`, `NewContract`, `CreateService`, `ReloadStAsync` helpers). Add a contract reload helper and two tests:

```csharp
    private async Task<PurchaseContract> ReloadContractAsync(Guid key) =>
        await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task ExecuteAsync_SetsContractAllocatedVolume_FromSignedSum()
    {
        var st = NewPurchase(netWeight: 1000m, available: 1000m);
        var pc = NewContract(totalVolume: 5000m);
        _db.Context.StorageTransactions.Add(st);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        await CreateService().ExecuteAsync(pc.Key, st.Key, 300m, "tester");

        var contract = await ReloadContractAsync(pc.Key);
        Assert.Equal(300m, contract.AllocatedVolume);
        Assert.Equal(4700m, contract.AvaiableVolume); // 5000 − 300
    }

    [Fact]
    public async Task ExecuteAsync_TwoAllocations_AccumulateContractAllocatedVolume()
    {
        var st = NewPurchase(netWeight: 1000m, available: 1000m);
        var pc = NewContract(totalVolume: 5000m);
        _db.Context.StorageTransactions.Add(st);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        await CreateService().ExecuteAsync(pc.Key, st.Key, 300m, "tester");
        await CreateService().ExecuteAsync(pc.Key, st.Key, 200m, "tester");

        var contract = await ReloadContractAsync(pc.Key);
        Assert.Equal(500m, contract.AllocatedVolume);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractsAllocationCreateServiceTests --nologo`
Expected: FAIL — the new tests expect `AllocatedVolume == 300`/`500` but the service does not set it yet, so it stays `0`.

- [ ] **Step 3: Set the contract's AllocatedVolume in the key-based overload**

In `PurchaseContractsAllocationCreateService.cs`, in the FIRST `ExecuteAsync(Guid purchaseContractKey, Guid storageTransactionKey, decimal volume, string userName, CommitMode commitMode)` overload, locate the block after `AddAsync(alloc)` that recalculates the storage transaction. Immediately after `storageTransaction.RecalculateAvailableVolume(existingAllocated + decimal.Abs(volume));`, add:

```csharp
            // Saldo do CONTRATO usa Volume com sinal (devolução negativa devolve saldo).
            var contractAllocated = await unitOfWork.Context.PurchaseContractsAllocations
                .Where(x => x.PurchaseContractKey == purchaseContractKey)
                .SumAsync(x => x.Volume);

            if (purchaseContract != null)
                purchaseContract.AllocatedVolume = contractAllocated + volume;
```

(Here `purchaseContract` is the tracked entity already loaded near the top of this overload via `purchaseContractsGetService.GetByIdAsync(purchaseContractKey)`; `volume` is the signed value after the transaction-type switch.)

- [ ] **Step 4: Set the contract's AllocatedVolume in the entity-based overload**

In the SECOND `ExecuteAsync(Guid purchaseContractKey, StorageTransaction storageTransaction, decimal volume, string userName, CommitMode commitMode)` overload, after its `storageTransaction.RecalculateAvailableVolume(existingAllocated + decimal.Abs(volume));` line, add the identical contract recompute:

```csharp
            var contractAllocated = await unitOfWork.Context.PurchaseContractsAllocations
                .Where(x => x.PurchaseContractKey == purchaseContractKey)
                .SumAsync(x => x.Volume);

            if (purchaseContract != null)
                purchaseContract.AllocatedVolume = contractAllocated + volume;
```

(`purchaseContract` in this overload is loaded at the top via `purchaseContractsGetService.GetByIdAsync(purchaseContractKey)`.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractsAllocationCreateServiceTests --nologo`
Expected: PASS (all create tests, including the two new ones).

- [ ] **Step 6: Run the full suite and build**

Run: `dotnet test SiagroB1.Application.Tests --nologo`
Expected: PASS.
Run: `dotnet build SiagroB1.sln --nologo`
Expected: `0 Erro(s)`.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationCreateService.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsAllocationCreateServiceTests.cs
git commit -m "feat: recalc contract AllocatedVolume on allocation create"
```

---

### Task 3: Recalculate `AllocatedVolume` on allocation delete

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationDeleteService.cs`
- Modify: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsAllocationDeleteServiceTests.cs`

**Interfaces:**
- Consumes: `PurchaseContract.AllocatedVolume` (Task 1).
- Produces: after `ExecuteAsync(key, userName, ...)`, the deleted allocation's contract has `AllocatedVolume` equal to the signed DB sum of its remaining allocations.

- [ ] **Step 1: Write the failing test**

In `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsAllocationDeleteServiceTests.cs`, the existing helpers seed a `StorageTransaction` and allocations but NOT a `PurchaseContract`, and allocations get a random `PurchaseContractKey` via `NewAllocation`. Add a helper to seed a contract and wire allocations to it, plus a test. Add near the other helpers:

```csharp
    private async Task<PurchaseContract> SeedContractAsync(decimal totalVolume)
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-DEL",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = totalVolume,
            AllocatedVolume = totalVolume, // será recalculado no delete
        };
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();
        return pc;
    }
```

Add the test (uses the existing `NewStorageTransaction`, `NewAllocation`, `SeedAsync`, `CreateService`; note `NewAllocation` sets a random `PurchaseContractKey`, so set it explicitly here):

```csharp
    [Fact]
    public async Task ExecuteAsync_RecalculatesContractAllocatedVolume_FromRemainingSigned()
    {
        var pc = await SeedContractAsync(totalVolume: 1000m);
        var st = NewStorageTransaction(netWeight: 1000m, availableVolume: 650m);

        var toDelete = NewAllocation(st, volume: 100m);
        toDelete.PurchaseContractKey = pc.Key;
        var keep = NewAllocation(st, volume: 250m);
        keep.PurchaseContractKey = pc.Key;
        await SeedAsync(st, toDelete, keep);

        await CreateService().ExecuteAsync(toDelete.Key, "tester");

        var contract = await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == pc.Key);
        Assert.Equal(250m, contract.AllocatedVolume); // Σ(250) restante, com sinal
        Assert.Equal(750m, contract.AvaiableVolume);  // 1000 − 250
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractsAllocationDeleteServiceTests --nologo`
Expected: FAIL — `contract.AllocatedVolume` stays `1000` (seeded) because the delete service does not recalc it yet; test expects `250`.

- [ ] **Step 3: Recalc the contract in the delete service**

In `PurchaseContractsAllocationDeleteService.cs`, inside `ExecuteAsync`, after the line `storageTransaction.RecalculateAvailableVolume(remainingAllocated);` and before the `if (commitMode == CommitMode.Auto)` block, add:

```csharp
        // Recalcula o saldo alocado do CONTRATO (Volume com sinal, exclui a removida).
        var contract = await db.Context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == alloc.PurchaseContractKey);

        if (contract != null)
        {
            var contractRemaining = await db.Context.PurchaseContractsAllocations
                .Where(x => x.PurchaseContractKey == alloc.PurchaseContractKey && x.Key != alloc.Key)
                .SumAsync(x => x.Volume);

            contract.AllocatedVolume = contractRemaining;
        }
```

(`db.Context` and `SumAsync`/`FirstOrDefaultAsync` are already available — the file imports `Microsoft.EntityFrameworkCore`.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractsAllocationDeleteServiceTests --nologo`
Expected: PASS (all delete tests, including the new one; existing delete tests that don't seed a contract still pass because `contract` is null → recalc skipped).

- [ ] **Step 5: Run the full suite and build**

Run: `dotnet test SiagroB1.Application.Tests --nologo`
Expected: PASS.
Run: `dotnet build SiagroB1.sln --nologo`
Expected: `0 Erro(s)`.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationDeleteService.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsAllocationDeleteServiceTests.cs
git commit -m "feat: recalc contract AllocatedVolume on allocation delete"
```

---

### Task 4: Migration with backfill for `AllocatedVolume` + `RowVersion`

**Files:**
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddPurchaseContractAllocatedVolume.cs` (scaffolded, then hand-edited)
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddPurchaseContractAllocatedVolume.Designer.cs` (scaffolded)
- Modify: `SiagroB1.Migrations/AppContext/AppDbContextModelSnapshot.cs` (updated by scaffold)

**Interfaces:**
- Consumes: entity changes from Task 1.
- Produces: a migration that adds the two columns and backfills `AllocatedVolume` from existing allocations.

- [ ] **Step 1: Scaffold the migration**

Run:
```bash
dotnet ef migrations add AddPurchaseContractAllocatedVolume --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```
Expected: `Done. To undo this action, use 'ef migrations remove'`.

- [ ] **Step 2: Verify the scaffold only touches the two new columns**

Open the generated `<timestamp>_AddPurchaseContractAllocatedVolume.cs`. Confirm `Up()` contains exactly:
- `AddColumn<decimal>(name: "AllocatedVolume", table: "PURCHASE_CONTRACTS", type: "DECIMAL(18,3)", nullable: false, defaultValue: 0m)`
- `AddColumn<byte[]>(name: "RowVersion", table: "PURCHASE_CONTRACTS", type: "rowversion", rowVersion: true, nullable: true)`

If any OTHER table/column appears, STOP — there is unexpected model drift; do not proceed, report it.

- [ ] **Step 3: Add the backfill SQL to Up()**

Edit the generated `<timestamp>_AddPurchaseContractAllocatedVolume.cs`. At the END of the `Up(MigrationBuilder migrationBuilder)` method (after the two `AddColumn` calls), append:

```csharp
            // Backfill: soma (com sinal) das alocações existentes por contrato.
            migrationBuilder.Sql(@"
                UPDATE PC
                SET PC.AllocatedVolume = ISNULL((
                    SELECT SUM(a.Volume)
                    FROM PURCHASE_CONTRACTS_ALLOCATIONS a
                    WHERE a.PurchaseContractKey = PC.[Key]
                ), 0)
                FROM PURCHASE_CONTRACTS PC;");
```

Leave `Down()` as scaffolded (it drops the two columns).

- [ ] **Step 4: Verify no pending model changes**

Run:
```bash
dotnet ef migrations has-pending-model-changes --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```
Expected: `No changes have been made to the model since the last migration.`

- [ ] **Step 5: Build the solution**

Run: `dotnet build SiagroB1.sln --nologo`
Expected: `Compilação com êxito. 0 Erro(s)`.

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test SiagroB1.Application.Tests --nologo`
Expected: PASS (all tests from Tasks 1-3).

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Migrations/AppContext/
git commit -m "feat: migration adds PurchaseContract AllocatedVolume + RowVersion with backfill"
```

---

## Self-Review

**Spec coverage:**
- §1 Modelo de dados (AllocatedVolume + RowVersion + AvaiableVolume arithmetic) → Task 1. ✓
- §2 Recálculo nos 2 serviços de alocação (signed sum) → Tasks 2, 3. ✓
- §3 Concorrência (rowversion) → Task 1 (entity) + Task 4 (column) + guard test Task 1. ✓
- §4 Migration + backfill → Task 4. ✓
- §5 Impacto leitura (AvaiableVolume sem navegação) → Task 1 robustness test + Totals test. ✓
- Testes §5 items 1-5 → Task 1 (robustness, rowversion, Totals), Task 2 (create signed sum, second allocation), Task 3 (delete signed remaining incl. negative). ✓
- Fora de escopo (SalesContract, revert ThenInclude) → not touched. ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code; commands have expected output. ✓

**Type consistency:** `AllocatedVolume` (`decimal`), `RowVersion` (`byte[]?`), `AvaiableVolume` (`decimal`) used consistently. Contract sum is signed `Sum(x => x.Volume)` in Tasks 2/3 and backfill (Task 4); ST-side abs untouched. `purchaseContract` tracked entity reused in both create overloads; `contract` loaded fresh in delete. ✓
