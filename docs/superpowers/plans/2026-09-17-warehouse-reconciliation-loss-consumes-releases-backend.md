# Conferência de Saldo de Armazém — a Perda consome as liberações — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement §9 of the reconciliation spec (revision of 17/09/2026). The reconciliation's
system balance becomes the **released − shipped** balance of the shipment releases at the reference
date. The loss is distributed by the user across releases. On approval, each distribution line
consumes its release:

- a `Standard` release gets **Purchase(8) allocated to the contract + WarehouseLoss(13)**;
- a release without a purchase leg gets **WarehouseLoss(13)** only.

Gains (positive difference) are refused.

**Architecture:**
- **Balance.** A new `WarehouseReconciliationReleaseBalanceService` lists the releases with their
  balance at the reference date. Its total is the snapshot.
- **Distribution.** A child table `WAREHOUSE_RECONCILIATION_RELEASES` holds the distribution. It is
  written by a dedicated action, `WarehouseReconciliationsDistributeLoss`, which replaces the whole
  set while the reconciliation is Draft.
- **Approval** mirrors `ShippingTransactionsCreateService`. **Cancel** mirrors
  `ShippingTransactionsReverseService`.
- **Recalculation.** `ShipmentReleasesRecalculateShippedService` learns that WarehouseLoss consumes
  releases without a purchase leg.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory in tests), ASP.NET Core OData, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-14-warehouse-reconciliation-design.md` — read **§9** first;
it overrides §§2–7 where they differ.

## Global Constraints

- **Language:** code identifiers, tables and columns are English. User-facing messages are pt-BR in
  `SiagroB1.Commons/Resources/Resource.pt-br.resx`, with an English copy in `Resource.resx`. Tests
  use `FakeStringLocalizer`, so `ex.Message` equals the resource KEY.
- **Git:** `git add` every new or changed file right after touching it. **Never commit or push.**
- **Business guards run BEFORE `BeginTransactionAsync` and outside `try`.** The catch wraps
  everything in `DefaultException` and would hide the message.
- **Recalculation hooks:** they only fire in `CommitMode.Auto`. Every multi-step flow here runs in
  `CommitMode.Deferred`, so it calls `ShipmentReleasesRecalculateShippedService.RecalculateAsync`
  explicitly (see memory "Hooks de recálculo só disparam pelo caminho certo").
- **Paused releases:** they count in the balance but NEVER receive loss.
  `ShipmentReleaseMovementGuardService` refuses movement on them.
- **Migrations:** apply them only with `ASPNETCORE_ENVIRONMENT=Yokotobi-Development` (profile
  `yktb` → `IDX_SIAGRO_DEV` on localhost). Confirm the target with `migrations list` first. Never use
  the `db-migration` profile.
- **Stop `SiagroB1.Web`** before `dotnet build`/`dotnet test` (DLL lock). If you cannot, add
  `--artifacts-path <scratchpad dir>`.
- **Single test:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Name>"`.
- **Tolerance:** use `0.001m` for "sum equals difference" (same value as
  `ShipmentReleasesFromReturnService.Tolerance`).

## File map

| File | Responsibility |
|---|---|
| `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateShippedService.cs` (modify) | Loss(13) consumes releases without a purchase leg; new `ShippedSign` helper |
| `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleaseMovementGuardService.cs` (modify) | Guard also covers Loss(13) |
| `SiagroB1.Domain/Entities/WarehouseReconciliationRelease.cs` (create) | Distribution line |
| `SiagroB1.Domain/Entities/WarehouseReconciliation.cs` (modify) | `Releases` collection; doc comment |
| `SiagroB1.Infra/Context/AppDbContext.cs` (modify) | DbSet + FKs + unique index |
| `SiagroB1.Migrations/AppContext/*_AddWarehouseReconciliationReleases.cs` (generate) | Schema |
| `SiagroB1.Domain/Dtos/WarehouseReconciliationReleaseBalanceDto.cs` (create) | One release row with its balances |
| `SiagroB1.Domain/Dtos/WarehouseReconciliationBalancePreviewDto.cs` (modify) | `Releases` list |
| `SiagroB1.Domain/Dtos/WarehouseReconciliationReleaseLineDto.cs` (create) | Saved line for detail/approval screens |
| `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationReleaseBalanceService.cs` (create) | Balance per release at date |
| `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsGuardService.cs` (modify) | Snapshot from releases; distribution guard |
| `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsGetBalancePreviewService.cs` (modify) | Returns releases |
| `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsDistributeLossService.cs` (create) | Replaces the lines (Draft only) |
| `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsListReleasesService.cs` (create) | Saved lines + generated transaction codes |
| `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsSendApprovalService.cs` (modify) | Only loss; distribution must close |
| `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsApprovalService.cs` (rewrite) | Generates transactions per line |
| `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsCancelService.cs` (rewrite) | Undoes per line; legacy path |
| `SiagroB1.Web/Actions/WarehouseReconciliations/WarehouseReconciliationsDistributeLossController.cs` (create) | Action endpoint |
| `SiagroB1.Web/Functions/WarehouseReconciliations/WarehouseReconciliationsListReleasesController.cs` (create) | Function endpoint |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (modify) | EDM action + function |
| `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (modify) | DI |
| `SiagroB1.Commons/Resources/Resource.resx` + `Resource.pt-br.resx` (modify) | New messages |
| `SiagroB1.Application.Tests/WarehouseReconciliations/*` | Test context + rewritten tests |

---

### Task 1: Loss(13) consumes releases without a purchase leg

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateShippedService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleaseMovementGuardService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesRecalculateWarehouseLossTests.cs` (create)

**Interfaces:**
- Produces: `public static decimal ShipmentReleasesRecalculateShippedService.ShippedSign(ReleaseOrigin origin, StorageTransactionType type)`.
  It returns `1`, `-1` or `0`, and it is the single source of truth for what consumes a release.
  Task 3 uses it.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

/// <summary>
/// GAC-1164 §9: a Perda(13) gerada pela Conferência de Saldo consome a liberação quando a liberação
/// não tem perna de compra (transferência / devolução de venda). Na Standard quem consome é a
/// Compra(8) do par, e a Perda não pode contar de novo.
/// </summary>
public class ShipmentReleasesRecalculateWarehouseLossTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<ShipmentRelease> SeedReleaseAsync(ReleaseOrigin origin)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-1", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026", DeliveryLocationCode = "ARM",
            Status = ContractStatus.Approved, TotalVolume = 10_000m,
        };
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contract.Key, DeliveryLocationCode = "ARM",
            ReleasedQuantity = 1_000m, Status = ReleaseStatus.Actived, Origin = origin,
        };
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();
        return release;
    }

    private async Task SeedTransactionAsync(Guid releaseKey, StorageTransactionType type, decimal qty)
    {
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = Guid.NewGuid().ToString("N")[..8], CardCode = "ARM", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "ARM", TransactionType = type,
            TransactionStatus = StorageTransactionsStatus.Confirmed, GrossWeight = qty, NetWeight = qty,
            ShipmentReleaseKey = releaseKey,
        });
        await _db.Context.SaveChangesAsync();
    }

    [Theory]
    [InlineData(ReleaseOrigin.OwnershipTransfer)]
    [InlineData(ReleaseOrigin.SalesReturn)]
    public async Task Loss_consumes_a_release_without_purchase_leg(ReleaseOrigin origin)
    {
        var release = await SeedReleaseAsync(origin);
        await SeedTransactionAsync(release.Key, StorageTransactionType.SalesShipment, 300m);
        await SeedTransactionAsync(release.Key, StorageTransactionType.WarehouseLoss, 50m);

        var shipped = await new ShipmentReleasesRecalculateShippedService(_db.Context)
            .CalculateShippedAsync(release.Key, origin);

        Assert.Equal(350m, shipped);
    }

    [Fact]
    public async Task Loss_does_not_count_twice_on_a_standard_release()
    {
        var release = await SeedReleaseAsync(ReleaseOrigin.Standard);
        await SeedTransactionAsync(release.Key, StorageTransactionType.Purchase, 50m);
        await SeedTransactionAsync(release.Key, StorageTransactionType.WarehouseLoss, 50m);

        var shipped = await new ShipmentReleasesRecalculateShippedService(_db.Context)
            .CalculateShippedAsync(release.Key, ReleaseOrigin.Standard);

        Assert.Equal(50m, shipped);
    }

    [Fact]
    public void Warehouse_loss_affects_shipped_quantity()
    {
        Assert.True(ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(StorageTransactionType.WarehouseLoss));
        Assert.False(ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(StorageTransactionType.WarehouseGain));
    }

    [Theory]
    [InlineData(ReleaseOrigin.Standard, StorageTransactionType.Purchase, 1)]
    [InlineData(ReleaseOrigin.Standard, StorageTransactionType.PurchaseReturn, -1)]
    [InlineData(ReleaseOrigin.Standard, StorageTransactionType.WarehouseLoss, 0)]
    [InlineData(ReleaseOrigin.Standard, StorageTransactionType.SalesShipment, 0)]
    [InlineData(ReleaseOrigin.SalesReturn, StorageTransactionType.SalesShipment, 1)]
    [InlineData(ReleaseOrigin.SalesReturn, StorageTransactionType.WarehouseLoss, 1)]
    [InlineData(ReleaseOrigin.SalesReturn, StorageTransactionType.SalesShipmentReturn, -1)]
    [InlineData(ReleaseOrigin.OwnershipTransfer, StorageTransactionType.Purchase, 0)]
    public void Shipped_sign_matches_the_recalculation(ReleaseOrigin origin, StorageTransactionType type, int expected)
    {
        Assert.Equal(expected, ShipmentReleasesRecalculateShippedService.ShippedSign(origin, type));
    }

    [Fact]
    public async Task Movement_guard_refuses_a_loss_on_a_paused_release()
    {
        var release = await SeedReleaseAsync(ReleaseOrigin.SalesReturn);
        release.Status = ReleaseStatus.Paused;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            new ShipmentReleaseMovementGuardService(_db.Context).EnsureCanShipAsync(new StorageTransaction
            {
                CardCode = "ARM", ItemCode = "SOJA", UnitOfMeasureCode = "KG", WarehouseCode = "ARM",
                TransactionType = StorageTransactionType.WarehouseLoss, ShipmentReleaseKey = release.Key,
            }));

        Assert.Contains("pausada", ex.Message);
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentReleasesRecalculateWarehouseLossTests"`
Expected: compile error (`ShippedSign` does not exist). After a stub, the loss and guard tests fail.

- [ ] **Step 3: Implement.** In `ShipmentReleasesRecalculateShippedService`:

  - `AffectsShippedQuantity` becomes:

```csharp
    public static bool AffectsShippedQuantity(StorageTransactionType type) =>
        type is StorageTransactionType.Purchase
            or StorageTransactionType.PurchaseReturn
            or StorageTransactionType.SalesShipment
            or StorageTransactionType.SalesShipmentReturn
            or StorageTransactionType.WarehouseLoss;
```

  - Add below it:

```csharp
    /// <summary>
    /// Sinal com que um romaneio consome (+1), devolve (−1) ou não mexe (0) no saldo da liberação,
    /// conforme a origem. Espelho em memória das listas de <see cref="CalculateShippedAsync"/>, que
    /// precisam ficar inline para o EF traduzir — mudou uma, mude a outra (há teste de paridade).
    /// <para>
    /// <c>WarehouseLoss(13)</c> (GAC-1164 §9) consome só a liberação SEM perna de compra. Na
    /// Standard a Conferência gera o par Compra(8) + Perda(13), e quem consome é a Compra — contar
    /// a Perda também dobraria o volume, como a Saída(7) dobraria na Expedição.
    /// </para>
    /// </summary>
    public static decimal ShippedSign(ReleaseOrigin origin, StorageTransactionType type) =>
        ReleaseOriginRules.ShipsWithoutPurchaseLeg(origin)
            ? type switch
            {
                StorageTransactionType.SalesShipment or StorageTransactionType.WarehouseLoss => 1m,
                StorageTransactionType.SalesShipmentReturn => -1m,
                _ => 0m,
            }
            : type switch
            {
                StorageTransactionType.Purchase => 1m,
                StorageTransactionType.PurchaseReturn => -1m,
                _ => 0m,
            };
```

  - In `CalculateShippedAsync`, replace the `ShipsWithoutPurchaseLeg` branch with:

```csharp
        if (ReleaseOriginRules.ShipsWithoutPurchaseLeg(origin))
        {
            return await query
                .Where(t => t.TransactionType == StorageTransactionType.SalesShipment
                            || t.TransactionType == StorageTransactionType.WarehouseLoss
                            || t.TransactionType == StorageTransactionType.SalesShipmentReturn)
                .SumAsync(t => t.TransactionType == StorageTransactionType.SalesShipmentReturn
                    ? -t.NetWeight
                    : t.NetWeight);
        }
```

  - Add a `<item><c>WarehouseLoss(13)</c> …</item>` line to the method's XML doc, saying that it
    consumes in the two branches without a purchase leg.
  - In `ShipmentReleaseMovementGuardService.EnsureCanShipAsync`, add
    `or StorageTransactionType.WarehouseLoss` to the type list. Also extend the summary: "…e a Perda
    de armazém da Conferência de Saldo (GAC-1164)".

- [ ] **Step 4: Run the new tests and the release suites.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentRelease|FullyQualifiedName~ShippingTransactions|FullyQualifiedName~WarehouseAdjustmentTransactionGuardsTests"`
Expected: PASS. If `WarehouseAdjustmentTransactionGuardsTests` has an assertion that 13 is OUTSIDE
`AffectsShippedQuantity` or the guard, update that assertion to the new rule. Keep 14 outside.

- [ ] **Step 5: Stage.** `git add` the two services and the new test file.

---

### Task 2: `WAREHOUSE_RECONCILIATION_RELEASES` table

**Files:**
- Create: `SiagroB1.Domain/Entities/WarehouseReconciliationRelease.cs`
- Modify: `SiagroB1.Domain/Entities/WarehouseReconciliation.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSet next to line 67, config after the Attachments cascade ~line 343)
- Generate: `SiagroB1.Migrations/AppContext/<ts>_AddWarehouseReconciliationReleases.cs`
- Test: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationReleaseModelTests.cs` (create)

**Interfaces:**
- Produces: entity `WarehouseReconciliationRelease` with the fields `Key`,
  `WarehouseReconciliationKey`, `ShipmentReleaseKey`, `Quantity`,
  `PurchaseStorageTransactionKey?` and `LossStorageTransactionKey?`.
- Produces: `WarehouseReconciliation.Releases` (`ICollection<WarehouseReconciliationRelease>`) and
  `AppDbContext.WarehouseReconciliationReleases`.

- [ ] **Step 1: Write the failing model test.**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationReleaseModelTests
{
    [Fact]
    public void Distribution_line_is_mapped_with_unique_release_per_reconciliation_and_restrict_to_release()
    {
        using var db = TestDb.CreateUnitOfWork().Context;
        var entity = db.Model.FindEntityType(typeof(WarehouseReconciliationRelease))!;

        Assert.Equal("WAREHOUSE_RECONCILIATION_RELEASES", entity.GetTableName());

        var unique = entity.GetIndexes().Single(i => i.IsUnique);
        Assert.Equal(
            new[] { nameof(WarehouseReconciliationRelease.WarehouseReconciliationKey), nameof(WarehouseReconciliationRelease.ShipmentReleaseKey) },
            unique.Properties.Select(p => p.Name).ToArray());

        var toReconciliation = entity.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(WarehouseReconciliation));
        Assert.Equal(DeleteBehavior.Cascade, toReconciliation.DeleteBehavior);

        var toRelease = entity.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(ShipmentRelease));
        Assert.Equal(DeleteBehavior.Restrict, toRelease.DeleteBehavior);
    }
}
```

  Check how `TestDb.CreateUnitOfWork()` exposes the context. If `Context` is not disposable, drop
  the `using`.

- [ ] **Step 2: Run it.** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationReleaseModelTests"` → compile error.

- [ ] **Step 3: Create the entity.**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Linha de distribuição da perda da Conferência de Saldo de Armazém (GAC-1164 §9): quanto da perda
/// cai em cada liberação. A quebra é custo da empresa, mas precisa CONSUMIR a liberação — senão o
/// saldo a embarcar dos contratos fica maior que o grão que existe no armazém e sobra um resíduo
/// que nunca é embarcado.
/// </summary>
[Table("WAREHOUSE_RECONCILIATION_RELEASES")]
public class WarehouseReconciliationRelease
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid WarehouseReconciliationKey { get; set; }
    public virtual WarehouseReconciliation? WarehouseReconciliation { get; set; }

    public Guid ShipmentReleaseKey { get; set; }
    public virtual ShipmentRelease? ShipmentRelease { get; set; }

    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal Quantity { get; set; }

    /// <summary>Compra(8) gerada na aprovação; nula quando a liberação não tem perna de compra.</summary>
    public Guid? PurchaseStorageTransactionKey { get; set; }

    /// <summary>Perda(13) gerada na aprovação.</summary>
    public Guid? LossStorageTransactionKey { get; set; }
}
```

  In `WarehouseReconciliation`:
  - add `public virtual ICollection<WarehouseReconciliationRelease> Releases { get; set; } = [];`;
  - change the `<remarks>` from "Nunca toca contrato…" to: "Desde a revisão de 17/09/2026 (spec
    §9) o saldo do sistema é o saldo a embarcar das liberações e a perda é distribuída em
    <see cref="Releases"/>: a aprovação consome cada liberação (Compra+Perda na Standard, só Perda
    sem perna de compra). <see cref="StorageTransactionKey"/> só é preenchido em conferência
    anterior à revisão.";
  - change the `StorageTransactionKey` summary to "Romaneio de Perda/Sobra das conferências
    anteriores à revisão de 17/09/2026. Nas novas, as chaves ficam em <see cref="Releases"/>."

- [ ] **Step 4: Map it in `AppDbContext`.**
  - Add `public DbSet<WarehouseReconciliationRelease> WarehouseReconciliationReleases { get; set; }`
    after line 67.
  - After the Attachments cascade block, add:

```csharp
        // Distribuição da perda (GAC-1164 §9). Uma linha por liberação; apagar a conferência leva as
        // linhas, mas a liberação não pode sumir debaixo de uma distribuição (Restrict).
        modelBuilder.Entity<WarehouseReconciliation>()
            .HasMany(x => x.Releases)
            .WithOne(x => x.WarehouseReconciliation)
            .HasForeignKey(x => x.WarehouseReconciliationKey)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WarehouseReconciliationRelease>()
            .HasOne(x => x.ShipmentRelease)
            .WithMany()
            .HasForeignKey(x => x.ShipmentReleaseKey)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WarehouseReconciliationRelease>()
            .HasIndex(x => new { x.WarehouseReconciliationKey, x.ShipmentReleaseKey })
            .IsUnique();
```

  ⚠️ Memory "Restrict e NoAction não são intercambiáveis": use `Restrict` exactly as above.
  ⚠️ Memory "Tabela filha nova quebra o delete do pai": `ShipmentRelease` gains a Restrict child.
  Grep `ShipmentReleases.Remove` in `SiagroB1.Application/Services`. For each delete service found,
  open it and check whether it can delete a release that has a distribution line. The only lines
  that can exist belong to a Draft/InApproval/Approved reconciliation whose release is `Actived`.
  If such a delete service exists, add a guard before its transaction:
  `if (await db.Context.WarehouseReconciliationReleases.AnyAsync(x => x.ShipmentReleaseKey == key)) throw new ApplicationException("Liberação usada em conferência de saldo de armazém: não pode ser excluída.");`
  Report what you found in the task summary.

- [ ] **Step 5: Build and run the model tests.**

Run: `dotnet build SiagroB1.sln` → 0 errors.
Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationReleaseModelTests|FullyQualifiedName~AppDbContextModelTests"` → PASS.

- [ ] **Step 6: Generate and review the migration.**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef migrations add AddWarehouseReconciliationReleases --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o AppContext
```

  The migration must contain ONLY:
  - `CreateTable WAREHOUSE_RECONCILIATION_RELEASES` with the 2 FKs (Cascade to
    `WAREHOUSE_RECONCILIATIONS`, Restrict to `SHIPMENT_RELEASES`);
  - the unique index and the index on `ShipmentReleaseKey`.

  Anything else means the snapshot drifted. Stop and report.

- [ ] **Step 7: Apply it locally and verify.**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef migrations list --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

  Expected: `list` shows `IDX_SIAGRO_DEV` pending only the new migration, and the last command says
  "No changes".

- [ ] **Step 8: Stage** the entity, the context, the migration + designer + snapshot, and the test.

---

### Task 3: System balance from releases at the reference date

**Files:**
- Create: `SiagroB1.Domain/Dtos/WarehouseReconciliationReleaseBalanceDto.cs`
- Modify: `SiagroB1.Domain/Dtos/WarehouseReconciliationBalancePreviewDto.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationReleaseBalanceService.cs`
- Modify: `WarehouseReconciliationsGuardService.cs`, `WarehouseReconciliationsGetBalancePreviewService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (register the new service next to line 497)
- Create: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationsTestContext.Releases.cs`
- Modify: `WarehouseReconciliationsTestContext.Reconciliations.cs`, `WarehouseReconciliationsConcurrencyTests.cs`, `WarehouseReconciliationsCreateServiceTests.cs`
- Test: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationReleaseBalanceServiceTests.cs` (create)

**Interfaces:**
- Consumes: `ShipmentReleasesRecalculateShippedService.ShippedSign` (Task 1).
- Produces: `WarehouseReconciliationReleaseBalanceService.ListAsync(string warehouseCode, string itemCode, DateTime referenceDate) : Task<List<WarehouseReconciliationReleaseBalanceDto>>`.
- Produces: `WarehouseReconciliationsGuardService` constructor
  `(IUnitOfWork db, IWarehouseComplementService complements, WarehouseReconciliationReleaseBalanceService releaseBalances, IStringLocalizer<Resource> resource)`.
- Produces test helpers:
  - `SeedReleaseAsync(decimal released, DateTime releaseDate, ReleaseOrigin origin = Standard, ReleaseStatus status = Actived, ContractStatus contractStatus = Approved, string warehouse = ThirdPartyWarehouse) : Task<ShipmentRelease>`;
  - `SeedReleaseMovementAsync(ShipmentRelease release, StorageTransactionType type, decimal quantity, DateTime? date) : Task<StorageTransaction>`;
  - `ReleaseBalance() : WarehouseReconciliationReleaseBalanceService`;
  - `ReloadReleaseAsync(Guid key) : Task<ShipmentRelease>`.

- [ ] **Step 1: Create the test context helpers** in `WarehouseReconciliationsTestContext.Releases.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public const string Producer = "F0001";

    public WarehouseReconciliationReleaseBalanceService ReleaseBalance() => new(Db);

    /// <summary>
    /// Contrato aprovado do produtor com UMA liberação no armazém de terceiros. É o grão que o
    /// sistema enxerga no armazém desde a revisão de 17/09 (spec §9): liberado − embarcado.
    /// </summary>
    public async Task<ShipmentRelease> SeedReleaseAsync(
        decimal released,
        DateTime releaseDate,
        ReleaseOrigin origin = ReleaseOrigin.Standard,
        ReleaseStatus status = ReleaseStatus.Actived,
        ContractStatus contractStatus = ContractStatus.Approved,
        string warehouse = ThirdPartyWarehouse)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = $"PC-{++_seq:000}",
            CardCode = Producer,
            CardName = "Produtor Teste",
            ItemCode = Item,
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = warehouse,
            Status = contractStatus,
            TotalVolume = released * 10,
            AllocatedVolume = 0m,
        };

        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = warehouse,
            DeliveryLocationName = "Armazém Terceiro",
            ReleaseDate = releaseDate.Date,
            ReleasedQuantity = released,
            ShippedQuantity = 0m,
            Status = status,
            Origin = origin,
        };

        Db.Context.PurchaseContracts.Add(contract);
        Db.Context.ShipmentReleases.Add(release);
        await Db.Context.SaveChangesAsync();
        return release;
    }

    /// <summary>Romaneio já confirmado contra a liberação, com o recálculo do embarcado.</summary>
    public async Task<StorageTransaction> SeedReleaseMovementAsync(
        ShipmentRelease release, StorageTransactionType type, decimal quantity, DateTime? date)
    {
        var transaction = await SeedStockAsync(type, quantity, date, warehouse: release.DeliveryLocationCode);
        transaction.ShipmentReleaseKey = release.Key;
        await Db.Context.SaveChangesAsync();
        await new ShipmentReleasesRecalculateShippedService(Db.Context).RecalculateAsync(release.Key);
        return transaction;
    }

    public Task<ShipmentRelease> ReloadReleaseAsync(Guid key) =>
        Db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key);
}
```

  If `RecalculateAsync` flips a fully consumed release to `Completed`, that is expected. Read its
  body before writing assertions that depend on the status.

- [ ] **Step 2: Write the failing balance tests** in `WarehouseReconciliationReleaseBalanceServiceTests.cs`:

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationReleaseBalanceServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;
    private const string Wh = WarehouseReconciliationsTestContext.ThirdPartyWarehouse;
    private const string Item = WarehouseReconciliationsTestContext.Item;

    [Fact]
    public async Task Balance_today_is_released_minus_shipped()
    {
        var release = await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.Purchase, 5_000m, Today.AddDays(-5));

        var rows = await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today);

        var row = Assert.Single(rows);
        Assert.Equal(15_000m, row.BalanceAtReferenceDate);
        Assert.Equal(15_000m, row.CurrentBalance);
        Assert.True(row.CanReceiveLoss);
        Assert.Equal(WarehouseReconciliationsTestContext.Producer, row.CardCode);
    }

    [Fact]
    public async Task Shipments_after_the_reference_date_are_added_back()
    {
        var release = await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.Purchase, 3_000m, Today.AddDays(-20));
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.Purchase, 4_000m, Today.AddDays(-2));

        var row = Assert.Single(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today.AddDays(-10)));

        Assert.Equal(17_000m, row.BalanceAtReferenceDate);
        Assert.Equal(13_000m, row.CurrentBalance);
    }

    [Fact]
    public async Task Release_issued_after_the_reference_date_is_left_out()
    {
        await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        await _ctx.SeedReleaseAsync(9_000m, Today.AddDays(-1));

        var rows = await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today.AddDays(-5));

        Assert.Equal(20_000m, Assert.Single(rows).BalanceAtReferenceDate);
    }

    [Fact]
    public async Task Paused_counts_but_cannot_receive_loss()
    {
        await _ctx.SeedReleaseAsync(8_000m, Today.AddDays(-30), status: ReleaseStatus.Paused);

        var row = Assert.Single(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today));

        Assert.Equal(8_000m, row.BalanceAtReferenceDate);
        Assert.False(row.CanReceiveLoss);
    }

    [Fact]
    public async Task Pending_and_cancelled_releases_are_left_out()
    {
        await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30), status: ReleaseStatus.Pending);
        await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30), status: ReleaseStatus.Cancelled);

        Assert.Empty(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today));
    }

    [Fact]
    public async Task Completed_after_the_reference_date_still_counts_at_that_date()
    {
        var release = await _ctx.SeedReleaseAsync(2_000m, Today.AddDays(-30));
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.Purchase, 2_000m, Today.AddDays(-1));
        var reloaded = await _ctx.ReloadReleaseAsync(release.Key);
        if (reloaded.Status != ReleaseStatus.Completed)
        {
            // O recálculo não finaliza sozinho neste código: simula a finalização.
            var tracked = await _ctx.Db.Context.ShipmentReleases.FindAsync(release.Key);
            tracked!.Status = ReleaseStatus.Completed;
            await _ctx.Db.Context.SaveChangesAsync();
        }

        var row = Assert.Single(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today.AddDays(-5)));

        Assert.Equal(2_000m, row.BalanceAtReferenceDate);
        Assert.Equal(0m, row.CurrentBalance);
        Assert.False(row.CanReceiveLoss);
    }

    [Fact]
    public async Task Sales_return_release_is_consumed_by_sales_shipment_and_loss()
    {
        var release = await _ctx.SeedReleaseAsync(29_400m, Today.AddDays(-30), origin: ReleaseOrigin.SalesReturn);
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.WarehouseLoss, 400m, Today.AddDays(-3));

        var row = Assert.Single(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today));

        Assert.Equal(29_000m, row.BalanceAtReferenceDate);
    }

    [Fact]
    public async Task Other_warehouse_or_item_is_ignored()
    {
        await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30), warehouse: "OUTRO");

        Assert.Empty(await _ctx.ReleaseBalance().ListAsync(Wh, Item, Today));
    }
}
```

- [ ] **Step 3: Run them.** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationReleaseBalanceServiceTests"` → compile error.

- [ ] **Step 4: Create the DTO and the service.**

`SiagroB1.Domain/Dtos/WarehouseReconciliationReleaseBalanceDto.cs`:

```csharp
namespace SiagroB1.Domain.Dtos;

/// <summary>Uma liberação do armazém+produto com o saldo a embarcar na data e hoje (GAC-1164 §9).</summary>
public class WarehouseReconciliationReleaseBalanceDto
{
    public Guid ShipmentReleaseKey { get; set; }
    public DateTime ReleaseDate { get; set; }
    /// <summary>Nome do enum <c>ReleaseStatus</c> (string, para a tela não depender da serialização de enum).</summary>
    public string Status { get; set; } = "";
    /// <summary>Nome do enum <c>ReleaseOrigin</c>.</summary>
    public string Origin { get; set; } = "";
    public Guid PurchaseContractKey { get; set; }
    public string? PurchaseContractCode { get; set; }
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public decimal BalanceAtReferenceDate { get; set; }
    public decimal CurrentBalance { get; set; }
    /// <summary>Liberada e com saldo hoje. Pausada soma no saldo mas não recebe perda.</summary>
    public bool CanReceiveLoss { get; set; }
}
```

  In `WarehouseReconciliationBalancePreviewDto`, add
  `public List<WarehouseReconciliationReleaseBalanceDto> Releases { get; set; } = [];`.

`WarehouseReconciliationReleaseBalanceService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>
/// Saldo a embarcar das liberações de um armazém+produto numa data (GAC-1164 §9). É o "saldo do
/// sistema" da Conferência de Saldo de Armazém.
/// </summary>
/// <remarks>
/// <b>Por que não o saldo por romaneios</b> (<c>StorageTransactionsWarehouseBalanceService</c>): na
/// liberação Standard a Expedição só grava a Compra(8) AO EMBARCAR, em par com a Saída(7). Enquanto o
/// grão está parado no armazém de terceiros aquele saldo fica em zero — na homologação a conferência
/// apurou 0 com 196.430 kg a embarcar.
/// <para>
/// <b>Na data:</b> parte do saldo de hoje (Released − Shipped), soma de volta o que foi romaneado
/// contra a liberação DEPOIS do fim do dia de referência (mesmos tipos e sinais do recálculo, via
/// <see cref="ShipmentReleasesRecalculateShippedService.ShippedSign"/>) e descarta liberação emitida
/// depois da data. Romaneio sem data conta como anterior, igual ao saldo por romaneios.
/// </para>
/// </remarks>
public class WarehouseReconciliationReleaseBalanceService(IUnitOfWork db)
{
    public async Task<List<WarehouseReconciliationReleaseBalanceDto>> ListAsync(
        string warehouseCode, string itemCode, DateTime referenceDate)
    {
        var limit = referenceDate.Date.AddDays(1);

        var releases = await db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(r => r.DeliveryLocationCode == warehouseCode &&
                        r.PurchaseContract!.ItemCode == itemCode &&
                        (r.Status == ReleaseStatus.Actived ||
                         r.Status == ReleaseStatus.Paused ||
                         r.Status == ReleaseStatus.Completed) &&
                        r.ReleaseDate < limit)
            .Select(r => new
            {
                r.Key,
                r.ReleaseDate,
                r.Status,
                r.Origin,
                r.ReleasedQuantity,
                r.ShippedQuantity,
                r.PurchaseContractKey,
                ContractCode = r.PurchaseContract!.Code,
                r.PurchaseContract.CardCode,
                r.PurchaseContract.CardName,
            })
            .ToListAsync();

        if (releases.Count == 0)
            return [];

        var keys = releases.Select(r => r.Key).ToList();

        var movedAfter = await db.Context.StorageTransactions
            .AsNoTracking()
            .Where(t => t.ShipmentReleaseKey != null &&
                        keys.Contains(t.ShipmentReleaseKey.Value) &&
                        t.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                        t.TransactionDate != null &&
                        t.TransactionDate >= limit)
            .Select(t => new { ReleaseKey = t.ShipmentReleaseKey!.Value, t.TransactionType, t.NetWeight })
            .ToListAsync();

        return releases
            .Select(r =>
            {
                var consumedAfter = movedAfter
                    .Where(t => t.ReleaseKey == r.Key)
                    .Sum(t => ShipmentReleasesRecalculateShippedService.ShippedSign(r.Origin, t.TransactionType) * t.NetWeight);

                var current = r.ReleasedQuantity - r.ShippedQuantity;

                return new WarehouseReconciliationReleaseBalanceDto
                {
                    ShipmentReleaseKey = r.Key,
                    ReleaseDate = r.ReleaseDate,
                    Status = r.Status.ToString(),
                    Origin = r.Origin.ToString(),
                    PurchaseContractKey = r.PurchaseContractKey,
                    PurchaseContractCode = r.ContractCode,
                    CardCode = r.CardCode,
                    CardName = r.CardName,
                    BalanceAtReferenceDate = decimal.Round(current + consumedAfter, 3, MidpointRounding.ToEven),
                    CurrentBalance = decimal.Round(current, 3, MidpointRounding.ToEven),
                    CanReceiveLoss = r.Status == ReleaseStatus.Actived && current > decimal.Zero,
                };
            })
            .Where(x => x.BalanceAtReferenceDate > decimal.Zero)
            .OrderBy(x => x.ReleaseDate)
            .ThenBy(x => x.PurchaseContractCode)
            .ToList();
    }
}
```

- [ ] **Step 5: Wire the snapshot and the preview.**
  - **Guard constructor:** add `WarehouseReconciliationReleaseBalanceService releaseBalances` after
    `complements`.
  - **Guard `RefreshSnapshotAsync`:**

```csharp
    /// <summary>
    /// Recalcula o saldo do sistema na data de referência — o saldo a embarcar das liberações
    /// (spec §9.3), não mais o saldo por romaneios — e a diferença.
    /// </summary>
    public async Task RefreshSnapshotAsync(WarehouseReconciliation r)
    {
        var rows = await releaseBalances.ListAsync(r.WarehouseCode, r.ItemCode, r.ReferenceDate);
        r.SystemBalance = rows.Sum(x => x.BalanceAtReferenceDate);
        r.Difference = r.ReportedBalance - r.SystemBalance;
    }
```

  - **Guard usings:** remove `using SiagroB1.Application.Services.StorageTransactions;` if nothing
    else uses it.
  - **Preview service:** inject `WarehouseReconciliationReleaseBalanceService releaseBalances`. Load
    the list once, then set `SystemBalance = releases.Sum(x => x.BalanceAtReferenceDate)` and
    `Releases = releases`. Keep the other three fields.
  - **DI:** `services.AddScoped<WarehouseReconciliationReleaseBalanceService>();`.
  - **Tests:**
    - `WarehouseReconciliationsTestContext.Reconciliations.cs`:
      - `Guard()` → `new(Db, new WarehouseComplementService(Db), ReleaseBalance(), Resource)`;
      - `Preview()` → `new(Db, new WarehouseComplementService(Db), ReleaseBalance(), Guard())`.
    - `WarehouseReconciliationsConcurrencyTests.cs` (lines 44 and 76): pass
      `new WarehouseReconciliationReleaseBalanceService(db)` as the new third argument.

- [ ] **Step 6: Port `WarehouseReconciliationsCreateServiceTests` to releases.**
  - **Helper:** replace `SeedPurchaseAsync` with:

```csharp
    private Task SeedReleaseAsync(decimal quantity, DateTime date) =>
        _ctx.SeedReleaseAsync(quantity, date);
```

  - **Callers:** rename every call to `SeedPurchaseAsync(` into `SeedReleaseAsync(`. That covers
    lines 16, 143 and 169.
  - **Snapshot test:** replace `Snapshot_ignores_transactions_after_the_reference_date` with:

```csharp
    [Fact]
    public async Task Snapshot_is_the_release_balance_at_the_reference_date()
    {
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.Purchase, 300m, Today.AddDays(-1));
        await _ctx.SeedReleaseAsync(500m, Today.AddDays(-1));

        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));

        Assert.Equal(1_000m, (await _ctx.ReloadAsync(r.Key)).SystemBalance);
    }
```

  - **Preview test:** add to `Preview_reports_balance_ownership_open_and_last_approved`:
    `Assert.Single(preview.Releases);`.

- [ ] **Step 7: Run the balance and create suites.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationReleaseBalanceServiceTests|FullyQualifiedName~WarehouseReconciliationsCreateServiceTests|FullyQualifiedName~WarehouseReconciliationsConcurrencyTests"`
Expected: PASS. `ApprovalFlowTests` and `CancelServiceTests` still fail here, because they seed
stock by storage transactions. Tasks 5 and 6 rewrite them.

- [ ] **Step 8: Stage** every new or changed file.

---

### Task 4: Distribution — action, guard, send for approval

**Files:**
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsDistributeLossService.cs`
- Modify: `WarehouseReconciliationsGuardService.cs` (add `EnsureLossDistributionAsync`)
- Modify: `WarehouseReconciliationsSendApprovalService.cs`
- Create: `SiagroB1.Web/Actions/WarehouseReconciliations/WarehouseReconciliationsDistributeLossController.cs`
- Modify: `ODataConfigurations.cs` (after the `WarehouseReconciliationsCancel` action, ~line 955), `ServiceCollectionExtensions.cs`
- Modify: `Resource.resx`, `Resource.pt-br.resx`
- Modify: `WarehouseReconciliationEdmModelTests.cs`
- Create: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationsDistributeLossServiceTests.cs`
- Create: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationsTestContext.Distribution.cs`

**Interfaces:**
- Consumes: `WarehouseReconciliationReleaseBalanceService.ListAsync` (Task 3), entity from Task 2.
- Produces: `public record WarehouseReconciliationLossLine(Guid ShipmentReleaseKey, decimal Quantity);`
  (declared in the DistributeLoss service file).
- Produces: `WarehouseReconciliationsDistributeLossService.ExecuteAsync(Guid key, IReadOnlyList<WarehouseReconciliationLossLine> lines, string userName) : Task`.
- Produces: `WarehouseReconciliationsGuardService.EnsureLossDistributionAsync(WarehouseReconciliation r) : Task<List<WarehouseReconciliationRelease>>`.
  It returns the tracked lines and assumes the snapshot is fresh; Task 5 uses it.
- Produces: test helpers `DistributeLoss()` and `DistributeAsync(Guid key, params (ShipmentRelease release, decimal qty)[] lines)`.
- Produces: action `WarehouseReconciliationsDistributeLoss(Key: Guid, ShipmentReleaseKeys: Collection(Guid), Quantities: Collection(Double))`.

- [ ] **Step 1: Add the resource keys** to BOTH resx files, next to the other
  `WAREHOUSE_RECONCILIATION_*` keys:

| Key | pt-BR | en |
|---|---|---|
| `WAREHOUSE_RECONCILIATION_ONLY_LOSS` | A conferência de saldo aceita apenas perda: o saldo informado precisa ser menor que o saldo do sistema. | The reconciliation only accepts a loss: the reported balance must be lower than the system balance. |
| `WAREHOUSE_RECONCILIATION_DISTRIBUTION_MISMATCH` | A soma da distribuição da perda entre as liberações precisa ser igual à diferença apurada. | The loss distributed across releases must equal the difference. |
| `WAREHOUSE_RECONCILIATION_DISTRIBUTION_INVALID` | Distribuição inválida: cada liberação pode aparecer uma vez e com quantidade maior que zero. | Invalid distribution: each release may appear once, with a quantity greater than zero. |
| `WAREHOUSE_RECONCILIATION_RELEASE_NOT_ELIGIBLE` | Uma das liberações da distribuição não pertence a este armazém e produto ou não está liberada (pausada, finalizada ou cancelada). | A distributed release does not belong to this warehouse and item or is not active. |
| `WAREHOUSE_RECONCILIATION_RELEASE_INSUFFICIENT_BALANCE` | A quantidade distribuída para uma liberação é maior que o saldo a embarcar dela hoje. | The quantity distributed to a release exceeds its current balance. |
| `WAREHOUSE_RECONCILIATION_CONTRACT_FINISHED` | O contrato de compra de uma das liberações está encerrado e não pode receber a perda. | The purchase contract of a distributed release is finished and cannot receive the loss. |

- [ ] **Step 2: Create the test helpers** `WarehouseReconciliationsTestContext.Distribution.cs`:

```csharp
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public WarehouseReconciliationsDistributeLossService DistributeLoss() => new(Db, Resource);

    public Task DistributeAsync(Guid key, params (ShipmentRelease Release, decimal Quantity)[] lines) =>
        DistributeLoss().ExecuteAsync(
            key,
            lines.Select(l => new WarehouseReconciliationLossLine(l.Release.Key, l.Quantity)).ToList(),
            "tester");
}
```

- [ ] **Step 3: Write the failing tests** `WarehouseReconciliationsDistributeLossServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsDistributeLossServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;

    [Fact]
    public async Task Distribution_replaces_the_previous_lines()
    {
        var a = await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var b = await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(39_000m);

        await _ctx.DistributeAsync(r.Key, (a, 1_000m));
        await _ctx.DistributeAsync(r.Key, (a, 600m), (b, 400m));

        var lines = await _ctx.Db.Context.WarehouseReconciliationReleases.AsNoTracking()
            .Where(x => x.WarehouseReconciliationKey == r.Key).OrderBy(x => x.Quantity).ToListAsync();
        Assert.Equal(new[] { 400m, 600m }, lines.Select(x => x.Quantity).ToArray());
    }

    [Fact]
    public async Task Distribution_outside_draft_is_refused()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.InApproval, Today);
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.DistributeAsync(r.Key, (release, 10m)));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_DRAFT", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Non_positive_quantity_is_refused(int quantity)
    {
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.DistributeAsync(r.Key, (release, quantity)));

        Assert.Equal("WAREHOUSE_RECONCILIATION_DISTRIBUTION_INVALID", ex.Message);
    }

    [Fact]
    public async Task Duplicated_release_is_refused()
    {
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.DistributeAsync(r.Key, (release, 50m), (release, 50m)));

        Assert.Equal("WAREHOUSE_RECONCILIATION_DISTRIBUTION_INVALID", ex.Message);
    }

    [Fact]
    public async Task Send_refuses_a_gain()
    {
        await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(1_200m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ONLY_LOSS", ex.Message);
    }

    [Fact]
    public async Task Send_refuses_a_distribution_that_does_not_close()
    {
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (release, 60m));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_DISTRIBUTION_MISMATCH", ex.Message);
    }

    [Fact]
    public async Task Send_refuses_a_paused_release()
    {
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30), status: ReleaseStatus.Paused);
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (release, 100m));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_RELEASE_NOT_ELIGIBLE", ex.Message);
    }

    [Fact]
    public async Task Send_refuses_a_line_above_the_current_release_balance()
    {
        var a = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var b = await _ctx.SeedReleaseAsync(50m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (a, 50m), (b, 100m));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_RELEASE_INSUFFICIENT_BALANCE", ex.Message);
    }

    [Fact]
    public async Task Send_refuses_a_standard_release_of_a_finished_contract()
    {
        var release = await _ctx.SeedReleaseAsync(1_000m, Today.AddDays(-30), contractStatus: ContractStatus.Finished);
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (release, 100m));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_CONTRACT_FINISHED", ex.Message);
    }

    [Fact]
    public async Task Send_accepts_a_closed_distribution_across_two_releases()
    {
        var a = await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var b = await _ctx.SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(39_000m);
        await _ctx.DistributeAsync(r.Key, (a, 600m), (b, 400m));

        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        Assert.Equal(WarehouseReconciliationStatus.InApproval, (await _ctx.ReloadAsync(r.Key)).Status);
    }
}
```

- [ ] **Step 4: Run them.** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationsDistributeLossServiceTests"` → compile error.

- [ ] **Step 5: Implement the service.**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public record WarehouseReconciliationLossLine(Guid ShipmentReleaseKey, decimal Quantity);

/// <summary>
/// Grava quanto da perda cai em cada liberação (GAC-1164 §9.4). Substitui o conjunto inteiro e só
/// em Rascunho. Aqui valida só a forma (quantidade positiva, liberação única); as regras que dependem
/// do saldo — fechar com a diferença, liberação elegível, saldo de hoje, contrato encerrado — rodam no
/// envio e na aprovação, em <see cref="WarehouseReconciliationsGuardService.EnsureLossDistributionAsync"/>,
/// porque o saldo muda entre digitar e decidir.
/// </summary>
public class WarehouseReconciliationsDistributeLossService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, IReadOnlyList<WarehouseReconciliationLossLine> lines, string userName)
    {
        var r = await db.Context.WarehouseReconciliations
                    .Include(x => x.Releases)
                    .FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        if (r.Status != WarehouseReconciliationStatus.Draft)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_DRAFT"].Value);

        if (lines.Any(l => l.Quantity <= decimal.Zero) ||
            lines.Select(l => l.ShipmentReleaseKey).Distinct().Count() != lines.Count)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_DISTRIBUTION_INVALID"].Value);

        db.Context.WarehouseReconciliationReleases.RemoveRange(r.Releases);

        foreach (var line in lines)
        {
            db.Context.WarehouseReconciliationReleases.Add(new WarehouseReconciliationRelease
            {
                WarehouseReconciliationKey = r.Key,
                ShipmentReleaseKey = line.ShipmentReleaseKey,
                Quantity = decimal.Round(line.Quantity, 3, MidpointRounding.ToEven),
            });
        }

        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
```

  ⚠️ Remove the old lines and insert the new ones in the same `SaveChanges`. If the unique index
  makes SQL Server fail on re-adding the same release, split into two saves inside a transaction.
  InMemory will not catch this; Task 8 verifies it against the real database.

- [ ] **Step 6: Add the guard** to `WarehouseReconciliationsGuardService`:

```csharp
    private const decimal Tolerance = 0.001m;

    /// <summary>
    /// Regras da distribuição da perda (spec §9.4), sobre o snapshot JÁ recalculado. Devolve as linhas
    /// rastreadas para a aprovação gravar as chaves dos romaneios nelas.
    /// </summary>
    public async Task<List<WarehouseReconciliationRelease>> EnsureLossDistributionAsync(WarehouseReconciliation r)
    {
        if (r.Difference >= decimal.Zero)
            throw Fail("WAREHOUSE_RECONCILIATION_ONLY_LOSS");

        var lines = await db.Context.WarehouseReconciliationReleases
            .Where(x => x.WarehouseReconciliationKey == r.Key)
            .ToListAsync();

        if (Math.Abs(lines.Sum(x => x.Quantity) - Math.Abs(r.Difference)) > Tolerance)
            throw Fail("WAREHOUSE_RECONCILIATION_DISTRIBUTION_MISMATCH");

        var keys = lines.Select(x => x.ShipmentReleaseKey).ToList();
        var releases = await db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(x => keys.Contains(x.Key))
            .Select(x => new
            {
                x.Key, x.Status, x.Origin, x.DeliveryLocationCode, x.ReleasedQuantity, x.ShippedQuantity,
                x.PurchaseContract!.ItemCode, ContractStatus = x.PurchaseContract.Status,
            })
            .ToDictionaryAsync(x => x.Key);

        foreach (var line in lines)
        {
            if (!releases.TryGetValue(line.ShipmentReleaseKey, out var release) ||
                release.DeliveryLocationCode != r.WarehouseCode ||
                release.ItemCode != r.ItemCode ||
                release.Status != ReleaseStatus.Actived)
                throw Fail("WAREHOUSE_RECONCILIATION_RELEASE_NOT_ELIGIBLE");

            if (line.Quantity - (release.ReleasedQuantity - release.ShippedQuantity) > Tolerance)
                throw Fail("WAREHOUSE_RECONCILIATION_RELEASE_INSUFFICIENT_BALANCE");

            // Só a Standard aloca contrato (perna de compra); as outras origens não tocam o contrato.
            if (!ReleaseOriginRules.ShipsWithoutPurchaseLeg(release.Origin) &&
                release.ContractStatus == ContractStatus.Finished)
                throw Fail("WAREHOUSE_RECONCILIATION_CONTRACT_FINISHED");
        }

        return lines;
    }
```

- [ ] **Step 7: Update `WarehouseReconciliationsSendApprovalService`.** Replace the zero-difference
  check with:

```csharp
        if (r.Difference == decimal.Zero)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_ZERO_DIFFERENCE"].Value);

        // Revisão 17/09 (spec §9): só perda, e a distribuição entre liberações precisa fechar.
        await guard.EnsureLossDistributionAsync(r);
```

- [ ] **Step 8: Add the action.**
  - **EDM**, after the Cancel action:

```csharp
        // Distribuição da perda entre liberações (GAC-1164 §9). Duas coleções paralelas, mesmo padrão
        // PROVADO de ShipmentLoadsRefuse (Guid + double).
        var warehouseReconciliationsDistributeLoss = modelBuilder.Action("WarehouseReconciliationsDistributeLoss");
        warehouseReconciliationsDistributeLoss.Parameter<Guid>("Key");
        warehouseReconciliationsDistributeLoss.CollectionParameter<Guid>("ShipmentReleaseKeys");
        warehouseReconciliationsDistributeLoss.CollectionParameter<double>("Quantities");
        warehouseReconciliationsDistributeLoss.Returns<IActionResult>();
```

  - **Controller:** read `ShipmentLoadsRefuseController` (grep `ShipmentLoadsRefuse` under
    `SiagroB1.Web/Actions`) to see how it reads `CollectionParameter`s from
    `ODataActionParameters`, and copy that reading code exactly. Shape:

```csharp
public class WarehouseReconciliationsDistributeLossController(
    WarehouseReconciliationsDistributeLossService service) : ODataController
{
    [HttpPost("odata/WarehouseReconciliationsDistributeLoss")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
            return BadRequest("Key é obrigatório.");

        // <copiar a leitura de coleção de ShipmentLoadsRefuseController>
        var releaseKeys = /* IEnumerable<Guid> de "ShipmentReleaseKeys", vazio se ausente */;
        var quantities = /* IEnumerable<double> de "Quantities", vazio se ausente */;

        if (releaseKeys.Count != quantities.Count)
            return BadRequest("ShipmentReleaseKeys e Quantities precisam ter o mesmo tamanho.");

        var lines = releaseKeys
            .Select((k, i) => new WarehouseReconciliationLossLine(k, (decimal)quantities[i]))
            .ToList();

        try
        {
            await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!), lines, User.Identity?.Name ?? "Unknown");
            return Ok();
        }
        catch (Exception e) when (e is NotFoundException or KeyNotFoundException)
        {
            return NotFound(e.Message);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

  An empty list is valid and clears the distribution.
  - **DI:** `services.AddScoped<WarehouseReconciliationsDistributeLossService>();`.
  - **EDM test:** in `WarehouseReconciliationEdmModelTests.The_actions_declare_their_parameters`,
    add `[InlineData("WarehouseReconciliationsDistributeLoss", "Key,ShipmentReleaseKeys,Quantities")]`.

- [ ] **Step 9: Port the send tests in `WarehouseReconciliationsApprovalFlowTests`.** Only the
  `Send_*`, `Withdraw_*` and `Reject_*` tests change here; Task 5 replaces the approval tests.
  - **Helper:** replace `SeedAsync` with
    `private Task<ShipmentRelease> SeedReleaseAsync(decimal q, DateTime d) => _ctx.SeedReleaseAsync(q, d);`
    and add `using SiagroB1.Domain.Entities;`.
  - **Distribution:** every test that sends for approval must distribute first. The pattern is
    `var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30)); var r = await _ctx.CreateDraftAsync(900m); await _ctx.DistributeAsync(r.Key, (release, 100m));`.
  - **`Send_recomputes_a_stale_snapshot`** becomes:

```csharp
    [Fact]
    public async Task Send_recomputes_a_stale_snapshot()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));
        // Nova liberação emitida depois do rascunho, com data anterior à referência.
        await _ctx.SeedReleaseAsync(50m, Today.AddDays(-20));
        await _ctx.DistributeAsync(r.Key, (release, 150m));

        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(1_050m, saved.SystemBalance);
        Assert.Equal(-150m, saved.Difference);
    }
```

  - **`Send_refuses_zero_difference`:** seed a release of 1.000 and report 1.000. It still expects
    `ZERO_DIFFERENCE`, because the zero check runs before the distribution guard.

- [ ] **Step 10: Run the suites.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationsDistributeLossServiceTests|FullyQualifiedName~WarehouseReconciliationEdmModelTests|FullyQualifiedName~WarehouseReconciliationsApprovalFlowTests.Send|FullyQualifiedName~WarehouseReconciliationsApprovalFlowTests.Withdraw|FullyQualifiedName~WarehouseReconciliationsApprovalFlowTests.Reject"`
Expected: PASS.

- [ ] **Step 11: Stage** every new or changed file.

---

### Task 5: Approval consumes the releases

**Files:**
- Rewrite: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsApprovalService.cs`
- Modify: `WarehouseReconciliationsTestContext.Approval.cs`
- Modify: `WarehouseReconciliationsApprovalFlowTests.cs` (approval tests)

**Interfaces:**
- Consumes: `guard.EnsureCanPersistAsync`, `guard.RefreshSnapshotAsync`,
  `guard.EnsureLossDistributionAsync` (Task 4), and `ShippedSign` semantics (Task 1).
- Produces: `WarehouseReconciliationsApprovalService` constructor
  `(IUnitOfWork db, WarehouseReconciliationsGuardService guard, StorageTransactionsCreateService storageCreate, StorageTransactionsConfirmedService storageConfirmed, StorageTransactionsCopyService storageCopy, PurchaseContractsAllocationCreateService allocationCreate, ShipmentReleasesRecalculateShippedService recalcShipped, IStringLocalizer<Resource> resource, ILogger<WarehouseReconciliationsApprovalService> logger)`.
  `ExecuteAsync(Guid key, string? comments, string userName)` is unchanged.
- Produces test helper: `CreateApprovedAsync(decimal reportedBalance, DateTime referenceDate, params (ShipmentRelease Release, decimal Quantity)[] lines) : Task<WarehouseReconciliation>`.

- [ ] **Step 1: Update the test context** `WarehouseReconciliationsTestContext.Approval.cs`. Replace
  the file body with:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public StorageTransactionsCreateService StorageCreate() =>
        new(Db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new() { [ThirdPartyWarehouse] = "Armazém Terceiro Ltda", [Producer] = "Produtor Teste" }),
            new FakeItemService(new() { [Item] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { [ThirdPartyWarehouse] = "Armazém Terceiro" }),
            new ShipmentReleasesRecalculateShippedService(Db.Context),
            new ShipmentReleaseMovementGuardService(Db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    public WarehouseReconciliationsSendApprovalService SendApproval() => new(Db, Guard(), Resource);
    public WarehouseReconciliationsWithdrawApprovalService Withdraw() => new(Db, Resource);
    public WarehouseReconciliationsRejectService Reject() => new(Db, Resource);

    public WarehouseReconciliationsApprovalService Approval()
    {
        var recalc = new ShipmentReleasesRecalculateShippedService(Db.Context);
        var guard = new ShipmentReleaseMovementGuardService(Db.Context);
        var create = StorageCreate();

        return new(
            Db,
            Guard(),
            create,
            new StorageTransactionsConfirmedService(Db, new FakeStringLocalizer<Resource>(), recalc, guard,
                NullLogger<StorageTransactionsConfirmedService>.Instance),
            new StorageTransactionsCopyService(Db, new FakeDocNumberSequenceService(), create, new FakeStringLocalizer<Resource>()),
            new PurchaseContractsAllocationCreateService(Db,
                new StorageTransactionsGetService(Db, NullLogger<StorageTransactionsGetService>.Instance)),
            recalc,
            Resource,
            NullLogger<WarehouseReconciliationsApprovalService>.Instance);
    }

    public async Task<WarehouseReconciliation> CreateApprovedAsync(
        decimal reportedBalance, DateTime referenceDate, params (ShipmentRelease Release, decimal Quantity)[] lines)
    {
        var r = await CreateDraftAsync(reportedBalance, referenceDate);
        await DistributeAsync(r.Key, lines);
        await SendApproval().ExecuteAsync(r.Key, "tester");
        await Approval().ExecuteAsync(r.Key, "ok", "approver");
        return r;
    }

    public Task<decimal> CurrentBalanceAsync() =>
        StorageTransactionsWarehouseBalanceService.CalculateAsync(Db.Context, ThirdPartyWarehouse, Item);
}
```

  Check the real constructor parameter lists of `StorageTransactionsConfirmedService`,
  `StorageTransactionsCopyService`, `PurchaseContractsAllocationCreateService` and
  `StorageTransactionsGetService` against `ShippingTransactionsCreateServiceTests.CreateService()`.
  Mirror that file if they differ.

- [ ] **Step 2: Replace the approval tests.** In `WarehouseReconciliationsApprovalFlowTests`,
  delete these tests:
  - `Approving_a_loss_generates_a_confirmed_warehouse_loss_transaction`;
  - `Approving_a_gain_generates_a_warehouse_gain_transaction`;
  - `Approval_recomputes_the_snapshot_before_generating`;
  - `Approval_refuses_when_the_difference_became_zero`;
  - `Approval_refuses_a_loss_bigger_than_the_current_balance`.

  Keep `Approval_refuses_non_in_approval` and add:

```csharp
    /// <summary>Cenário do usuário (17/09): 2 contratos de 20.000, quebra de 1.000 → 39.000 a embarcar.</summary>
    [Fact]
    public async Task Approving_a_loss_on_standard_releases_generates_purchase_and_loss_and_consumes_each_release()
    {
        var a = await SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var b = await SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var referenceDate = Today.AddDays(-2);

        var r = await _ctx.CreateApprovedAsync(39_000m, referenceDate, (a, 600m), (b, 400m));

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Approved, saved.Status);
        Assert.Equal("approver", saved.ApprovedBy);
        Assert.Null(saved.StorageTransactionKey);

        Assert.Equal(19_400m, (await _ctx.ReloadReleaseAsync(a.Key)).AvailableQuantity);
        Assert.Equal(19_600m, (await _ctx.ReloadReleaseAsync(b.Key)).AvailableQuantity);

        var lines = await _ctx.Db.Context.WarehouseReconciliationReleases.AsNoTracking()
            .Where(x => x.WarehouseReconciliationKey == r.Key).ToListAsync();
        Assert.All(lines, l => Assert.NotNull(l.PurchaseStorageTransactionKey));
        Assert.All(lines, l => Assert.NotNull(l.LossStorageTransactionKey));

        var lineA = lines.Single(l => l.ShipmentReleaseKey == a.Key);
        var purchase = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == lineA.PurchaseStorageTransactionKey);
        var loss = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == lineA.LossStorageTransactionKey);

        Assert.Equal(StorageTransactionType.Purchase, purchase.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, purchase.TransactionStatus);
        Assert.Equal(TransactionCode.WarehouseReconciliation, purchase.TransactionOrigin);
        Assert.Equal(600m, purchase.NetWeight);
        Assert.Equal(referenceDate, purchase.TransactionDate);
        Assert.Equal(WarehouseReconciliationsTestContext.Producer, purchase.CardCode);

        Assert.Equal(StorageTransactionType.WarehouseLoss, loss.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, loss.TransactionStatus);
        Assert.Equal(TransactionCode.WarehouseReconciliation, loss.TransactionOrigin);
        Assert.Equal(600m, loss.NetWeight);
        Assert.Equal(decimal.Zero, loss.AvaiableVolumeToAllocate);
        Assert.Equal(a.Key, loss.ShipmentReleaseKey);
        Assert.Null(loss.StorageAddressCode);

        // A perda é custo da empresa: o contrato do produtor conta como entregue.
        var allocation = await _ctx.Db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(x => x.StorageTransactionKey == purchase.Key);
        Assert.Equal(a.PurchaseContractKey, allocation.PurchaseContractKey);
        Assert.Equal(600m, allocation.Volume);

        // Compra + Perda se anulam no saldo por romaneios.
        Assert.Equal(0m, await _ctx.CurrentBalanceAsync());
    }

    [Fact]
    public async Task Approving_a_loss_on_a_sales_return_release_generates_only_the_loss()
    {
        await _ctx.SeedStockAsync(StorageTransactionType.SalesShipmentReturn, 29_400m, Today.AddDays(-30));
        var release = await _ctx.SeedReleaseAsync(29_400m, Today.AddDays(-30), origin: ReleaseOrigin.SalesReturn);

        var r = await _ctx.CreateApprovedAsync(29_000m, Today, (release, 400m));

        var line = await _ctx.Db.Context.WarehouseReconciliationReleases.AsNoTracking()
            .SingleAsync(x => x.WarehouseReconciliationKey == r.Key);
        Assert.Null(line.PurchaseStorageTransactionKey);
        Assert.NotNull(line.LossStorageTransactionKey);
        Assert.Equal(29_000m, (await _ctx.ReloadReleaseAsync(release.Key)).AvailableQuantity);
        Assert.False(await _ctx.Db.Context.PurchaseContractsAllocations.AnyAsync());
        Assert.Equal(29_000m, await _ctx.CurrentBalanceAsync());
    }

    [Fact]
    public async Task Approval_refuses_when_the_balance_changed_after_sending()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));
        await _ctx.DistributeAsync(r.Key, (release, 100m));
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.PurchaseReturn, 40m, Today.AddDays(-15));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Approval().ExecuteAsync(r.Key, null, "approver"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_DISTRIBUTION_MISMATCH", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.InApproval, (await _ctx.ReloadAsync(r.Key)).Status);
        Assert.False(await _ctx.Db.Context.StorageTransactions.AnyAsync(
            x => x.TransactionOrigin == TransactionCode.WarehouseReconciliation));
    }

    [Fact]
    public async Task Approval_refuses_when_the_release_was_shipped_meanwhile()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));
        await _ctx.DistributeAsync(r.Key, (release, 100m));
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");
        // Embarque depois da data: não muda o saldo NA DATA, mas deixa só 50 hoje.
        await _ctx.SeedReleaseMovementAsync(release, StorageTransactionType.Purchase, 950m, Today.AddDays(-1));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Approval().ExecuteAsync(r.Key, null, "approver"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_RELEASE_INSUFFICIENT_BALANCE", ex.Message);
    }

    [Fact]
    public async Task Approval_refuses_a_gain()
    {
        await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.InApproval, Today, difference: 10m);
        r.ReportedBalance = 1_010m;
        await _ctx.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Approval().ExecuteAsync(r.Key, null, "approver"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ONLY_LOSS", ex.Message);
    }
```

  `SeedWithStatusAsync` adds the entity to the tracked context, so `r.ReportedBalance = …` followed
  by `SaveChangesAsync` persists. If it does not, reload it tracked first.

- [ ] **Step 3: Run the tests.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationsApprovalFlowTests"`
Expected: compile error (constructor), then failures.

- [ ] **Step 4: Rewrite the approval service.**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>
/// Aprova a conferência e CONSOME as liberações da distribuição (GAC-1164 §9.5). A quebra é custo da
/// empresa, e o grão perdido não pode continuar a embarcar.
/// </summary>
/// <remarks>
/// Por linha, espelhando <c>ShippingTransactionsCreateService</c> com a Perda no lugar da Saída:
/// <list type="bullet">
/// <item><b>Standard</b> — Compra(8) confirmada e alocada no contrato (o produtor entregou; a
/// liberação é consumida) + Perda(13) copiada dela, que dá a saída no armazém. No saldo por romaneios
/// as duas se anulam, como o par da Expedição.</item>
/// <item><b>Sem perna de compra</b> (transferência / devolução de venda) — o grão já entrou antes; só
/// a Perda(13), que consome a liberação por
/// <see cref="ShipmentReleasesRecalculateShippedService.ShippedSign"/>.</item>
/// </list>
/// Tudo em <c>CommitMode.Deferred</c> numa transação; o recálculo das liberações roda DEPOIS do
/// commit, porque a Perda nasce como cópia tipada de Compra e só é retipada em memória — recalcular
/// antes contaria a Compra duas vezes (mesmo motivo documentado na Expedição).
/// </remarks>
public class WarehouseReconciliationsApprovalService(
    IUnitOfWork db,
    WarehouseReconciliationsGuardService guard,
    StorageTransactionsCreateService storageCreate,
    StorageTransactionsConfirmedService storageConfirmed,
    StorageTransactionsCopyService storageCopy,
    PurchaseContractsAllocationCreateService allocationCreate,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    IStringLocalizer<Resource> resource,
    ILogger<WarehouseReconciliationsApprovalService> logger)
{
    public async Task ExecuteAsync(Guid key, string? comments, string userName)
    {
        var r = await db.Context.WarehouseReconciliations.FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        // Tudo abaixo roda antes da transação: o catch embrulharia a mensagem de negócio.
        if (r.Status != WarehouseReconciliationStatus.InApproval)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL"].Value);

        await guard.EnsureCanPersistAsync(r);
        await guard.RefreshSnapshotAsync(r);

        if (r.Difference == decimal.Zero)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_ZERO_DIFFERENCE"].Value);

        var lines = await guard.EnsureLossDistributionAsync(r);

        var releases = await db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(x => lines.Select(l => l.ShipmentReleaseKey).Contains(x.Key))
            .Select(x => new { x.Key, x.Origin, x.PurchaseContractKey, x.PurchaseContract!.CardCode })
            .ToDictionaryAsync(x => x.Key);

        try
        {
            await db.BeginTransactionAsync();

            foreach (var line in lines)
            {
                var release = releases[line.ShipmentReleaseKey];

                if (ReleaseOriginRules.ShipsWithoutPurchaseLeg(release.Origin))
                {
                    var loss = NewTransaction(r, line, StorageTransactionType.WarehouseLoss, r.CardCode ?? r.WarehouseCode);
                    loss.TransactionStatus = StorageTransactionsStatus.Confirmed;
                    await storageCreate.ExecuteAsync(loss, userName, TransactionCode.WarehouseReconciliation, CommitMode.Deferred);
                    line.LossStorageTransactionKey = loss.Key;
                }
                else
                {
                    // Perna comercial: o produtor da liberação, como na Expedição.
                    var purchase = NewTransaction(r, line, StorageTransactionType.Purchase, release.CardCode);
                    await storageCreate.ExecuteAsync(purchase, userName, TransactionCode.WarehouseReconciliation, CommitMode.Deferred);
                    await storageConfirmed.ExecuteAsync(purchase, userName, CommitMode.Deferred, true);
                    await allocationCreate.ExecuteAsync(
                        release.PurchaseContractKey, purchase, purchase.NetWeight, userName, CommitMode.Deferred);

                    var loss = await storageCopy.ExecuteAsync(purchase, userName, CommitMode.Deferred);
                    loss.TransactionType = StorageTransactionType.WarehouseLoss;
                    loss.TransactionStatus = StorageTransactionsStatus.Confirmed;
                    // A cópia nasce com a origem de romaneio avulso; a Conferência é a dona, e é isso
                    // que faz Cancelar/Estornar da tela de Romaneios recusarem mexer nela.
                    loss.TransactionOrigin = TransactionCode.WarehouseReconciliation;
                    loss.NetWeight = purchase.NetWeight;
                    loss.GrossWeight = purchase.NetWeight;
                    loss.AvaiableVolumeToAllocate = decimal.Zero;
                    loss.StorageAddressCode = null;

                    line.PurchaseStorageTransactionKey = purchase.Key;
                    line.LossStorageTransactionKey = loss.Key;
                }
            }

            r.Status = WarehouseReconciliationStatus.Approved;
            r.ApprovalComments = comments;
            r.ApprovedAt = DateTime.Now;
            r.ApprovedBy = userName;
            r.UpdatedAt = DateTime.Now;
            r.UpdatedBy = userName;

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, e.Message);
            throw new DefaultException($"Erro ao aprovar a conferência de saldo: {e.Message}");
        }

        foreach (var releaseKey in releases.Keys)
            await recalcShipped.RecalculateAsync(releaseKey);
    }

    private static StorageTransaction NewTransaction(
        WarehouseReconciliation r, WarehouseReconciliationRelease line, StorageTransactionType type, string cardCode) => new()
        {
            TransactionDate = r.ReferenceDate,
            TransactionStatus = StorageTransactionsStatus.Pending,
            TransactionType = type,
            GrossWeight = line.Quantity,
            NetWeight = line.Quantity,
            AvaiableVolumeToAllocate = decimal.Zero,
            BranchCode = r.BranchCode,
            CardCode = cardCode,
            ItemCode = r.ItemCode,
            UnitOfMeasureCode = r.UnitOfMeasureCode,
            WarehouseCode = r.WarehouseCode,
            StorageAddressCode = null,
            ShipmentReleaseKey = line.ShipmentReleaseKey,
            Comments = $"Conferência de saldo de armazém {r.Code}",
        };
}
```

  Implementation notes:
  - **Confirmed purchase `NetWeight`:** `StorageTransactionsConfirmedService` recomputes
    `NetWeight` through `CalculateReceipt`/`CalculateNetWeight`. With no quality inspections, it
    must equal `GrossWeight`, and the test asserts 600. If it does not, stop and report; do not
    patch the confirm service.
  - **Movement guard:** `storageCreate.ExecuteAsync` calls `movementGuard.EnsureCanShipAsync`, which
    now covers type 13. The release is `Actived` (guarded), so it passes.
  - **Recalculation outside the transaction:** a failure there leaves the release stale but correct
    on the next recalc. That is the same trade-off as `ShippingTransactionsCreateService`. Keep it.

- [ ] **Step 5: Register nothing new** (all dependencies are already in DI). Build the solution:
  `dotnet build SiagroB1.sln` → 0 errors.

- [ ] **Step 6: Run the tests.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationsApprovalFlowTests|FullyQualifiedName~WarehouseReconciliationsDistributeLossServiceTests"`
Expected: PASS.

- [ ] **Step 7: Stage** the service, the test context and the test file.

---

### Task 6: Cancel undoes the consumption

**Files:**
- Rewrite: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsCancelService.cs`
- Modify: `WarehouseReconciliationsTestContext.Cancel.cs`
- Rewrite: `WarehouseReconciliationsCancelServiceTests.cs`
- Modify: `Resource.resx`, `Resource.pt-br.resx` (remove nothing; `GAIN_CANCEL_NEGATIVE` becomes unused, leave it)

**Interfaces:**
- Consumes: `CreateApprovedAsync(reported, date, lines)` (Task 5).
- Produces: `WarehouseReconciliationsCancelService` constructor
  `(IUnitOfWork db, StorageTransactionsCancelService storageTransactionsCancelService, PurchaseContractsAllocationDeleteService allocationDelete, ShipmentReleasesRecalculateShippedService recalcShipped, IStringLocalizer<Resource> resource, ILogger<WarehouseReconciliationsCancelService> logger)`.

- [ ] **Step 1: Update the test context** `WarehouseReconciliationsTestContext.Cancel.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.WarehouseReconciliations;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public WarehouseReconciliationsCancelService Cancel() =>
        new(Db,
            new StorageTransactionsCancelService(Db, new ShipmentReleasesRecalculateShippedService(Db.Context)),
            new PurchaseContractsAllocationDeleteService(Db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance),
            new ShipmentReleasesRecalculateShippedService(Db.Context),
            Resource,
            NullLogger<WarehouseReconciliationsCancelService>.Instance);
}
```

- [ ] **Step 2: Rewrite `WarehouseReconciliationsCancelServiceTests.cs`:**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsCancelServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;

    private Task<ShipmentRelease> SeedReleaseAsync(decimal q, DateTime d) => _ctx.SeedReleaseAsync(q, d);

    [Fact]
    public async Task Cancelling_a_draft_has_no_effect()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.DistributeAsync(r.Key, (release, 100m));

        await _ctx.Cancel().ExecuteAsync(r.Key, "digitado errado", "tester");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Cancelled, saved.Status);
        Assert.Equal("digitado errado", saved.CancellationReason);
        Assert.Equal("tester", saved.CanceledBy);
        Assert.Equal(1_000m, (await _ctx.ReloadReleaseAsync(release.Key)).AvailableQuantity);
    }

    [Fact]
    public async Task Cancelling_an_approved_standard_loss_restores_the_releases_and_removes_the_allocation()
    {
        var a = await SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var b = await SeedReleaseAsync(20_000m, Today.AddDays(-30));
        var r = await _ctx.CreateApprovedAsync(39_000m, Today.AddDays(-2), (a, 600m), (b, 400m));

        await _ctx.Cancel().ExecuteAsync(r.Key, "armazém corrigiu o extrato", "tester");

        Assert.Equal(WarehouseReconciliationStatus.Cancelled, (await _ctx.ReloadAsync(r.Key)).Status);
        Assert.Equal(20_000m, (await _ctx.ReloadReleaseAsync(a.Key)).AvailableQuantity);
        Assert.Equal(20_000m, (await _ctx.ReloadReleaseAsync(b.Key)).AvailableQuantity);
        Assert.False(await _ctx.Db.Context.PurchaseContractsAllocations.AnyAsync());

        var generated = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .Where(x => x.TransactionOrigin == TransactionCode.WarehouseReconciliation).ToListAsync();
        Assert.Equal(4, generated.Count);
        Assert.All(generated, t => Assert.Equal(StorageTransactionsStatus.Cancelled, t.TransactionStatus));

        var contract = await _ctx.Db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == a.PurchaseContractKey);
        Assert.Equal(0m, contract.AllocatedVolume);
    }

    [Fact]
    public async Task Cancelling_an_approved_sales_return_loss_restores_release_and_warehouse()
    {
        await _ctx.SeedStockAsync(StorageTransactionType.SalesShipmentReturn, 29_400m, Today.AddDays(-30));
        var release = await _ctx.SeedReleaseAsync(29_400m, Today.AddDays(-30), origin: ReleaseOrigin.SalesReturn);
        var r = await _ctx.CreateApprovedAsync(29_000m, Today, (release, 400m));

        await _ctx.Cancel().ExecuteAsync(r.Key, "motivo", "tester");

        Assert.Equal(29_400m, (await _ctx.ReloadReleaseAsync(release.Key)).AvailableQuantity);
        Assert.Equal(29_400m, await _ctx.CurrentBalanceAsync());
    }

    /// <summary>Conferência aprovada ANTES da revisão de 17/09: sem linhas, cancela pelo romaneio único.</summary>
    [Fact]
    public async Task Cancelling_a_legacy_approved_reconciliation_cancels_its_single_transaction()
    {
        await _ctx.SeedStockAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var loss = await _ctx.SeedStockAsync(StorageTransactionType.WarehouseLoss, 120m, Today.AddDays(-5));
        loss.TransactionOrigin = TransactionCode.WarehouseReconciliation;
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Approved, Today.AddDays(-5), -120m);
        r.StorageTransactionKey = loss.Key;
        await _ctx.Db.Context.SaveChangesAsync();

        await _ctx.Cancel().ExecuteAsync(r.Key, "legado", "tester");

        Assert.Equal(StorageTransactionsStatus.Cancelled,
            (await _ctx.Db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == loss.Key)).TransactionStatus);
        Assert.Equal(1_000m, await _ctx.CurrentBalanceAsync());
    }

    [Fact]
    public async Task Only_the_latest_approved_can_be_cancelled()
    {
        var release = await SeedReleaseAsync(1_000m, Today.AddDays(-30));
        var older = await _ctx.CreateApprovedAsync(900m, Today.AddDays(-10), (release, 100m));
        await _ctx.CreateApprovedAsync(850m, Today.AddDays(-5), (release, 50m));

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(older.Key, "motivo", "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_LATEST", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.Approved, (await _ctx.ReloadAsync(older.Key)).Status);
    }

    [Theory]
    [InlineData(WarehouseReconciliationStatus.InApproval)]
    [InlineData(WarehouseReconciliationStatus.Rejected)]
    [InlineData(WarehouseReconciliationStatus.Cancelled)]
    public async Task Other_statuses_cannot_be_cancelled(WarehouseReconciliationStatus status)
    {
        var r = await _ctx.SeedWithStatusAsync(status, Today);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(r.Key, "motivo", "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_CANNOT_CANCEL", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Cancellation_reason_is_required(string? reason)
    {
        var r = await _ctx.CreateDraftAsync(10m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(r.Key, reason, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_CANCELLATION_REASON_REQUIRED", ex.Message);
    }
}
```

  **Latest-approved test:** the second conference sees 900 at its date and reports 850, so its
  difference is −50. After the first approval the release has 900 available, so it passes. If the
  numbers do not close, recompute them; do not weaken the assertion.

- [ ] **Step 3: Run the tests.** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationsCancelServiceTests"` → compile error.

- [ ] **Step 4: Rewrite the cancel service.** Keep the constructor from **Interfaces**,
  `EnsureApprovedCanBeUndoneAsync` without the gain branch (drop the `difference` parameter), and
  this `ExecuteAsync` body after the existing reason/status checks:

```csharp
        if (r.Status == WarehouseReconciliationStatus.Approved)
            await EnsureApprovedCanBeUndoneAsync(r.Key, r.WarehouseCode, r.ItemCode);

        var lines = await db.Context.WarehouseReconciliationReleases
            .Where(x => x.WarehouseReconciliationKey == r.Key)
            .ToListAsync();

        try
        {
            await db.BeginTransactionAsync();

            if (r.Status == WarehouseReconciliationStatus.Approved)
            {
                if (lines.Count == 0 && r.StorageTransactionKey is Guid legacyKey)
                {
                    // Aprovada antes da revisão de 17/09/2026: um romaneio só, sem liberação.
                    await storageTransactionsCancelService.ExecuteAsync(
                        legacyKey, userName, TransactionCode.WarehouseReconciliation);
                }
                else
                {
                    await UndoLinesAsync(lines, userName);
                }
            }

            r.Status = WarehouseReconciliationStatus.Cancelled;
            r.CancellationReason = reason.Trim();
            r.CanceledAt = DateTime.Now;
            r.CanceledBy = userName;
            r.UpdatedAt = DateTime.Now;
            r.UpdatedBy = userName;

            await db.SaveChangesAsync();

            // Depois do SaveChanges (a consulta precisa enxergar o Cancelled) e dentro da transação,
            // como em ShippingTransactionsReverseService: falha aqui reverte o cancelamento inteiro.
            foreach (var releaseKey in lines.Select(x => x.ShipmentReleaseKey).Distinct())
                await recalcShipped.RecalculateAsync(releaseKey);

            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, e.Message);
            throw new DefaultException($"Erro ao cancelar a conferência de saldo: {e.Message}");
        }
```

  And:

```csharp
    /// <summary>
    /// Desfaz cada linha como o estorno da Expedição: remove a alocação da Compra, cancela a Compra e
    /// a Perda escrevendo o status direto (StorageTransactionsCancelService recusaria a Compra por
    /// ter alocação, e é por isso que o recálculo da liberação é explícito no chamador).
    /// </summary>
    private async Task UndoLinesAsync(List<WarehouseReconciliationRelease> lines, string userName)
    {
        foreach (var line in lines)
        {
            if (line.PurchaseStorageTransactionKey is Guid purchaseKey)
            {
                var allocationKey = await db.Context.PurchaseContractsAllocations
                    .Where(x => x.StorageTransactionKey == purchaseKey)
                    .Select(x => x.Key)
                    .FirstOrDefaultAsync();

                if (allocationKey != Guid.Empty)
                    await allocationDelete.ExecuteAsync(allocationKey, userName, CommitMode.Deferred);

                await MarkCancelledAsync(purchaseKey, userName);
            }

            if (line.LossStorageTransactionKey is Guid lossKey)
                await MarkCancelledAsync(lossKey, userName);
        }
    }

    private async Task MarkCancelledAsync(Guid key, string userName)
    {
        var transaction = await db.Context.StorageTransactions.FirstAsync(x => x.Key == key);
        transaction.TransactionStatus = StorageTransactionsStatus.Cancelled;
        transaction.CanceledAt = DateTime.Now;
        transaction.CanceledBy = userName;
    }
```

  Implementation notes:
  - Add `using SiagroB1.Application.Services.PurchaseContracts;`,
    `using SiagroB1.Application.Services.ShipmentReleases;`, `using SiagroB1.Domain.Entities;` and
    `using SiagroB1.Infra.Enums;`.
  - **Deferred allocation delete:** `allocationDelete.ExecuteAsync` in `CommitMode.Deferred` sums
    the remaining allocations from the database while the removal is only tracked. It excludes the
    removed key explicitly (`x.Key != alloc.Key`), so it is safe. Keep Deferred.
  - **Stale snapshot on the latest approved:** in `EnsureApprovedCanBeUndoneAsync` keep the
    "latest" rule exactly.
  - **Update the class summary:** "Rascunho cancela sem efeito. Aprovada desfaz o consumo das
    liberações (GAC-1164 §9.7) — ou cancela o romaneio único da conferência legada — e só se for a
    ÚLTIMA aprovada do armazém+produto."

- [ ] **Step 5: Run the whole reconciliation area and its neighbours.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliation|FullyQualifiedName~WarehouseAdjustment|FullyQualifiedName~ShipmentRelease|FullyQualifiedName~ShippingTransactions"`
Expected: PASS.

- [ ] **Step 6: Stage** the service, the test context and the test file.

---

### Task 7: Saved lines for the detail and approval screens

**Files:**
- Create: `SiagroB1.Domain/Dtos/WarehouseReconciliationReleaseLineDto.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsListReleasesService.cs`
- Create: `SiagroB1.Web/Functions/WarehouseReconciliations/WarehouseReconciliationsListReleasesController.cs`
- Modify: `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`, `WarehouseReconciliationEdmModelTests.cs`
- Test: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationsListReleasesServiceTests.cs` (create)

**Interfaces:**
- Consumes: `CreateApprovedAsync` (Task 5).
- Produces: function `WarehouseReconciliationsListReleases(Key: Guid)` returning
  `List<WarehouseReconciliationReleaseLineDto>` with the fields `ShipmentReleaseKey`,
  `ReleaseDate`, `PurchaseContractCode`, `CardCode`, `CardName`, `Quantity`,
  `PurchaseTransactionCode` and `LossTransactionCode`. The frontend plan consumes this.

- [ ] **Step 1: Write the failing test.**

```csharp
namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsListReleasesServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();

    [Fact]
    public async Task Lists_lines_with_contract_producer_and_generated_codes()
    {
        var release = await _ctx.SeedReleaseAsync(20_000m, DateTime.Today.AddDays(-30));
        var r = await _ctx.CreateApprovedAsync(19_000m, DateTime.Today, (release, 1_000m));

        var rows = await new Services.WarehouseReconciliations.WarehouseReconciliationsListReleasesService(_ctx.Db)
            .ExecuteAsync(r.Key);

        var row = Assert.Single(rows);
        Assert.Equal(1_000m, row.Quantity);
        Assert.StartsWith("PC-", row.PurchaseContractCode);
        Assert.Equal(WarehouseReconciliationsTestContext.Producer, row.CardCode);
        Assert.False(string.IsNullOrEmpty(row.PurchaseTransactionCode));
        Assert.False(string.IsNullOrEmpty(row.LossTransactionCode));
    }
}
```

  The namespace prefix `Services.` resolves from `SiagroB1.Application.Tests`. If it does not
  compile, add `using SiagroB1.Application.Services.WarehouseReconciliations;` and drop the prefix.

- [ ] **Step 2: Run it** → compile error.

- [ ] **Step 3: Implement.**

```csharp
namespace SiagroB1.Domain.Dtos;

public class WarehouseReconciliationReleaseLineDto
{
    public Guid ShipmentReleaseKey { get; set; }
    public DateTime ReleaseDate { get; set; }
    public string? PurchaseContractCode { get; set; }
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public decimal Quantity { get; set; }
    public string? PurchaseTransactionCode { get; set; }
    public string? LossTransactionCode { get; set; }
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>Distribuição gravada da perda, para as telas de detalhe e aprovação (GAC-1164 §9.9).</summary>
public class WarehouseReconciliationsListReleasesService(IUnitOfWork db)
{
    public async Task<List<WarehouseReconciliationReleaseLineDto>> ExecuteAsync(Guid reconciliationKey)
    {
        var lines = await db.Context.WarehouseReconciliationReleases
            .AsNoTracking()
            .Where(x => x.WarehouseReconciliationKey == reconciliationKey)
            .Select(x => new
            {
                x.ShipmentReleaseKey,
                x.ShipmentRelease!.ReleaseDate,
                ContractCode = x.ShipmentRelease.PurchaseContract!.Code,
                x.ShipmentRelease.PurchaseContract.CardCode,
                x.ShipmentRelease.PurchaseContract.CardName,
                x.Quantity,
                x.PurchaseStorageTransactionKey,
                x.LossStorageTransactionKey,
            })
            .ToListAsync();

        var transactionKeys = lines
            .SelectMany(x => new[] { x.PurchaseStorageTransactionKey, x.LossStorageTransactionKey })
            .Where(k => k.HasValue).Select(k => k!.Value).ToList();

        var codes = await db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => transactionKeys.Contains(x.Key))
            .ToDictionaryAsync(x => x.Key, x => x.Code);

        string? CodeOf(Guid? key) => key is Guid k && codes.TryGetValue(k, out var c) ? c : null;

        return lines
            .OrderBy(x => x.ReleaseDate).ThenBy(x => x.ContractCode)
            .Select(x => new WarehouseReconciliationReleaseLineDto
            {
                ShipmentReleaseKey = x.ShipmentReleaseKey,
                ReleaseDate = x.ReleaseDate,
                PurchaseContractCode = x.ContractCode,
                CardCode = x.CardCode,
                CardName = x.CardName,
                Quantity = x.Quantity,
                PurchaseTransactionCode = CodeOf(x.PurchaseStorageTransactionKey),
                LossTransactionCode = CodeOf(x.LossStorageTransactionKey),
            })
            .ToList();
    }
}
```

  **Controller:** copy `WarehouseReconciliationsAttachmentsListController` (same folder) with
  route `odata/WarehouseReconciliationsListReleases(Key={key})` and `[FromRoute] Guid key`,
  returning `Ok(await service.ExecuteAsync(key))`. Read that controller first and mirror its
  attribute and parameter style exactly.

  **EDM:**

```csharp
        var warehouseReconciliationsListReleases = modelBuilder.Function("WarehouseReconciliationsListReleases");
        warehouseReconciliationsListReleases.Parameter<Guid>("Key");
        warehouseReconciliationsListReleases.Returns<IActionResult>();
```

  **DI:** `services.AddScoped<WarehouseReconciliationsListReleasesService>();`.
  **EDM test:** add `[InlineData("WarehouseReconciliationsListReleases", "Key")]` to
  `The_functions_declare_their_parameters`.

- [ ] **Step 4: Run** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationsListReleasesServiceTests|FullyQualifiedName~WarehouseReconciliationEdmModelTests"` → PASS.

- [ ] **Step 5: Stage** all new or changed files.

---

### Task 8: Full verification and local data

**Files:** none new (fixes only if something fails).

- [ ] **Step 1: Build and run the full suite.**

Run: `dotnet build SiagroB1.sln` → 0 errors.
Run: `dotnet test SiagroB1.Application.Tests` → all green. Report the count; the baseline on
17/09/2026 was 1846 + this plan's tests.

- [ ] **Step 2: Check the model and migrations.**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Expected: "No changes".

- [ ] **Step 3: Handle the legacy approved reconciliations on localhost** (`IDX_SIAGRO_DEV`).
  CS000001 and CS000002 (F024813/P026031) were approved without lines. Do NOT change them by SQL.
  Report their state in the summary; the frontend plan's browser task cancels them through the UI
  (latest first), which exercises the legacy cancel path.

- [ ] **Step 4: Real SQL Server check of the distribution replace.** Start Web+Gateway with the
  `yktb` profile (memory "Subir a stack local"). Then:
  - log in as admin/1234 and create a Draft conference through the API or the screen;
  - call `POST /odata/WarehouseReconciliationsDistributeLoss` twice for the SAME release with
    different quantities;
  - the second call must return 200, not a unique-index 500. If it fails, split
    remove/insert into two `SaveChanges` inside a transaction, add a comment explaining the SQL
    Server ordering, and re-run.

  Stop the apps afterwards.

- [ ] **Step 5: Stage any fixes. Report:**
  - test count;
  - migration applied on localhost only;
  - the legacy CS000001/CS000002 state;
  - the result of the unique-index check;
  - any release delete service that needed the Task 2 guard.
