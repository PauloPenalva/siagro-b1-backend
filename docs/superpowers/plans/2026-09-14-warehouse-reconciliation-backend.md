# Conferência de Saldo de Armazém — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the "Conferência de Saldo de Armazém" backend for GAC-1164. A reconciliation that
has been approved generates a Perda(13) or Sobra(14) storage transaction. That transaction affects
only the warehouse balance.

**Architecture:**
- **Transaction types.** Two new `StorageTransactionType` values enter
  `StorageTransactionsWarehouseBalanceService`, which also gains an "as of date" cut.
- **Reconciliation document.** The new `WarehouseReconciliation` is a `DocumentEntity`. It has one
  service per operation (Create, Update, SendApproval, WithdrawApproval, Approval, Reject, Cancel),
  following the `OwnershipTransfer` and `PurchaseContracts*Approval*` patterns.
- **Shared guards.** `WarehouseReconciliationsGuardService` holds the rules used by several services.
- **Supporting pieces.** The reasons register and the attachments copy existing patterns.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory in tests), ASP.NET Core OData, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-14-warehouse-reconciliation-design.md` (read it first).

## Global Constraints

- **Language:** code identifiers, tables and columns are English. User-facing messages are pt-BR,
  and they go in `SiagroB1.Commons/Resources/Resource.pt-br.resx` with an English copy in
  `Resource.resx`.
- **Git:** stage every new file with `git add` right after creating it. **Never commit or push.**
  The "Commit" steps in this plan are `git add` only.
- **Business guards run BEFORE `BeginTransactionAsync` and outside `try`.** The catch wraps
  everything in `DefaultException` and would hide the message.
- **Scope:** only warehouses with `WarehouseComplement.IsOwn == false`. A warehouse with no
  complement row counts as third-party.
- **Partner:** `CardCode = WarehouseCode`, always set by the server.
- **Types 13/14 stay OUT of** `IsAllocatable`, `AffectsShippedQuantity`,
  `ShipmentReleaseMovementGuardService`, the allocation `allowedTypes`, and every lot formula.
- **Migrations:** apply them only with an explicit environment, `ASPNETCORE_ENVIRONMENT=Yokotobi-Development`
  (profile `yktb` → `IDX_SIAGRO_DEV` on localhost). Confirm the target with `migrations list` before
  `database update`. Never use the `db-migration` profile.
- **Stop the running app first:** stop `SiagroB1.Web` before `dotnet build`/`dotnet test`, because
  it locks the DLLs.
- **Test commands** are run from `siagro-b1-backend/`:
  `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Name>"`.

---

## File map

**Domain (`SiagroB1.Domain/`)**
- Modify:
  - `Enums/StorageTransactionType.cs`
  - `Enums/TransactionCode.cs`
- Create:
  - `Enums/WarehouseReconciliationStatus.cs`
  - `Entities/WarehouseReconciliation.cs`
  - `Entities/WarehouseReconciliationReason.cs`
  - `Entities/WarehouseReconciliationAttachment.cs`
  - `Dtos/WarehouseReconciliationBalancePreviewDto.cs`
  - `Dtos/WarehouseReconciliationAttachmentDto.cs`

**Infra**
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`

**Migrations (`SiagroB1.Migrations/`)**
- Create:
  - `AppContext/*_AddWarehouseReconciliations`
  - `AppContext/*_SeedWarehouseReconciliationDocNumber`
  - `AppContext/*_SeedWarehouseReconciliationReasons`
  - `CommonContext/*_AddWarehouseReconciliationMenus`

**Application: existing storage services (`SiagroB1.Application/Services/StorageTransactions/`)**
- Modify:
  - `StorageTransactionsWarehouseBalanceService.cs`
  - `StorageTransactionsConfirmedService.cs`

**Application: new services (`SiagroB1.Application/Services/WarehouseReconciliations/`)**
- Create:
  - `WarehouseReconciliationsGuardService.cs`
  - `WarehouseReconciliationsGetService.cs`
  - `WarehouseReconciliationsCreateService.cs`
  - `WarehouseReconciliationsUpdateService.cs`
  - `WarehouseReconciliationsGetBalancePreviewService.cs`
  - `WarehouseReconciliationsSendApprovalService.cs`
  - `WarehouseReconciliationsWithdrawApprovalService.cs`
  - `WarehouseReconciliationsRejectService.cs`
  - `WarehouseReconciliationsApprovalService.cs`
  - `WarehouseReconciliationsCancelService.cs`
  - `WarehouseReconciliationReasonsGetService.cs`
  - `WarehouseReconciliationReasonsCreateService.cs`
  - `WarehouseReconciliationReasonsUpdateService.cs`
  - `WarehouseReconciliationReasonsDeleteService.cs`
  - `WarehouseReconciliationAttachmentsCreateService.cs`
  - `WarehouseReconciliationAttachmentsGetService.cs`
  - `WarehouseReconciliationAttachmentsDeleteService.cs`

**Reports**
- Modify: `SiagroB1.Reports/Helpers/StorageStatementReportHelper.cs`

**Web (`SiagroB1.Web/`)**
- Create:
  - `Controllers/WarehouseReconciliationsController.cs`
  - `Controllers/WarehouseReconciliationReasonsController.cs`
  - `Controllers/WarehouseReconciliationAttachmentsController.cs`
  - `Actions/WarehouseReconciliations/*Controller.cs` (6 files)
  - `Functions/WarehouseReconciliations/*Controller.cs` (3 files)
- Modify:
  - `ODataConfig/ODataConfigurations.cs`
  - `Extensions/ServiceCollectionExtensions.cs`

**Resources**
- Modify:
  - `SiagroB1.Commons/Resources/Resource.resx`
  - `SiagroB1.Commons/Resources/Resource.pt-br.resx`

**Tests (`SiagroB1.Application.Tests/`)**
- Modify: `StorageTransactions/StorageTransactionsWarehouseBalanceTests.cs`
- Create:
  - `StorageTransactions/WarehouseAdjustmentTransactionGuardsTests.cs`
  - `Reports/StorageStatementReportHelperTests.cs`
  - `WarehouseReconciliations/WarehouseReconciliationsTestContext.cs`
  - `WarehouseReconciliations/WarehouseReconciliationReasonsServiceTests.cs`
  - `WarehouseReconciliations/WarehouseReconciliationsCreateServiceTests.cs`
  - `WarehouseReconciliations/WarehouseReconciliationsApprovalFlowTests.cs`
  - `WarehouseReconciliations/WarehouseReconciliationsCancelServiceTests.cs`
  - `WarehouseReconciliations/WarehouseReconciliationAttachmentsServiceTests.cs`
  - `WarehouseReconciliations/WarehouseReconciliationEdmModelTests.cs`

---

### Task 1: Perda/Sobra transaction types in the warehouse balance

**Files:**
- Modify: `SiagroB1.Domain/Enums/StorageTransactionType.cs`
- Modify: `SiagroB1.Domain/Enums/TransactionCode.cs`
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsWarehouseBalanceService.cs`
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsConfirmedService.cs` (switch at ~55-72)
- Modify: `SiagroB1.Reports/Helpers/StorageStatementReportHelper.cs`
- Modify: `SiagroB1.Commons/Resources/Resource.resx`, `Resource.pt-br.resx`
- Test: `SiagroB1.Application.Tests/StorageTransactions/StorageTransactionsWarehouseBalanceTests.cs`
- Test: `SiagroB1.Application.Tests/StorageTransactions/WarehouseAdjustmentTransactionGuardsTests.cs`
- Test: `SiagroB1.Application.Tests/Reports/StorageStatementReportHelperTests.cs`

**Interfaces:**
- Produces:
  - `StorageTransactionType.WarehouseLoss = 13` and `StorageTransactionType.WarehouseGain = 14`.
  - `TransactionCode.WarehouseReconciliation = 13`.
  - `StorageTransactionsWarehouseBalanceService.CalculateAsync(AppDbContext context, string warehouseCode, string itemCode, DateTime? upToDate = null)` returns `Task<decimal>`.
  - Resource key `WAREHOUSE_RECONCILIATION_TRANSACTION_NOT_CONFIRMABLE`.

- [ ] **Step 1: Write the failing balance tests.** Append these to `StorageTransactionsWarehouseBalanceTests`
  (the class already has `Add(type, status, weight, code)`, `_db` and `Service()`).

```csharp
    private StorageTransaction AddDated(
        StorageTransactionType type, decimal weight, string code, DateTime? date)
    {
        var transaction = Add(type, StorageTransactionsStatus.Confirmed, weight, code);
        transaction.TransactionDate = date;
        return transaction;
    }

    /// <summary>
    /// Sobra de armazém (14) credita e Perda de armazém (13) debita o saldo do armazém — é o
    /// efeito da Conferência de Saldo, sem contrato e sem lote.
    /// </summary>
    [Fact]
    public async Task Warehouse_gain_adds_and_warehouse_loss_subtracts()
    {
        Add(StorageTransactionType.Purchase, StorageTransactionsStatus.Confirmed, 1_000m, "P1");
        Add(StorageTransactionType.WarehouseGain, StorageTransactionsStatus.Confirmed, 50m, "G1");
        Add(StorageTransactionType.WarehouseLoss, StorageTransactionsStatus.Confirmed, 200m, "L1");
        await _db.SaveChangesAsync();

        var balance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _db.Context, Warehouse, Item);

        Assert.Equal(850m, balance);
    }

    [Fact]
    public async Task Cancelled_warehouse_loss_does_not_count()
    {
        Add(StorageTransactionType.Purchase, StorageTransactionsStatus.Confirmed, 1_000m, "P1");
        Add(StorageTransactionType.WarehouseLoss, StorageTransactionsStatus.Cancelled, 200m, "L1");
        await _db.SaveChangesAsync();

        var balance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _db.Context, Warehouse, Item);

        Assert.Equal(1_000m, balance);
    }

    /// <summary>
    /// "Saldo até a data": o que foi lançado DEPOIS da data de referência fica fora; a própria
    /// data entra inteira, inclusive com hora (o corte é o início do dia seguinte).
    /// </summary>
    [Fact]
    public async Task Up_to_date_excludes_later_transactions_and_includes_the_whole_day()
    {
        var reference = new DateTime(2026, 9, 10);
        AddDated(StorageTransactionType.Purchase, 1_000m, "P1", reference.AddDays(-5));
        AddDated(StorageTransactionType.Purchase, 300m, "P2", reference.AddHours(23).AddMinutes(59));
        AddDated(StorageTransactionType.SalesShipment, 400m, "S1", reference.AddDays(1));
        await _db.SaveChangesAsync();

        var balance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _db.Context, Warehouse, Item, reference);

        Assert.Equal(1_300m, balance);
    }

    /// <summary>
    /// Romaneio legado sem data conta no "até a data": sem isso o saldo "até hoje" divergiria
    /// do saldo atual e a Conferência apuraria uma diferença fantasma.
    /// </summary>
    [Fact]
    public async Task Up_to_date_counts_transactions_without_date()
    {
        AddDated(StorageTransactionType.Purchase, 1_000m, "P1", null);
        await _db.SaveChangesAsync();

        var balance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _db.Context, Warehouse, Item, new DateTime(2026, 9, 10));

        Assert.Equal(1_000m, balance);
    }

    /// <summary>
    /// A Perda baixa o que a Expedição pode embarcar: 1.000 comprados, 300 perdidos, um
    /// embarque de 800 tem de ser recusado.
    /// </summary>
    [Fact]
    public async Task A_shipment_above_the_balance_after_a_loss_is_refused()
    {
        Add(StorageTransactionType.Purchase, StorageTransactionsStatus.Confirmed, 1_000m, "P1");
        Add(StorageTransactionType.WarehouseLoss, StorageTransactionsStatus.Confirmed, 300m, "L1");
        var shipment = Add(StorageTransactionType.SalesShipment, StorageTransactionsStatus.Pending, 800m, "S1");
        await _db.SaveChangesAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => Service().ExecuteAsync(shipment, "tester"));

        Assert.Equal(StorageTransactionsStatus.Pending, shipment.TransactionStatus);
    }
```

- [ ] **Step 2: Run the tests and confirm they fail to compile.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageTransactionsWarehouseBalanceTests"`
Expected: build error `'StorageTransactionType' does not contain a definition for 'WarehouseGain'`.

- [ ] **Step 3: Add the enum values.** In `StorageTransactionType.cs`, after `SalesShipmentReturn = 12,`:

```csharp
    WarehouseLoss = 13,           // Perda de armazém - gerada pela Conferência de Saldo de Armazém
    WarehouseGain = 14,           // Sobra de armazém - gerada pela Conferência de Saldo de Armazém
```

  In `TransactionCode.cs`, after `FinancialDocument = 12,`:

```csharp
    WarehouseReconciliation = 13,
```

- [ ] **Step 4: Change the balance formula.** Replace the body of `CalculateAsync` in
  `StorageTransactionsWarehouseBalanceService.cs` with the version below, and add the new
  paragraph to the XML-doc `<remarks>`.

```csharp
    /// <param name="upToDate">
    /// Quando informado, soma só o que foi lançado até o FIM desse dia (<c>TransactionDate</c>
    /// anterior ao início do dia seguinte). Romaneio sem data conta sempre — é legado, e deixá-lo
    /// de fora faria o saldo "até hoje" divergir do saldo atual.
    /// </param>
    public static async Task<decimal> CalculateAsync(
        AppDbContext context, string warehouseCode, string itemCode, DateTime? upToDate = null)
    {
        var query = context.StorageTransactions
            .AsNoTracking()
            .Where(x => (x.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                         x.TransactionStatus == StorageTransactionsStatus.Invoiced) &&
                        x.WarehouseCode == warehouseCode &&
                        x.ItemCode == itemCode &&
                        (x.TransactionType == StorageTransactionType.Purchase ||
                         x.TransactionType == StorageTransactionType.PurchaseReturn ||
                         x.TransactionType == StorageTransactionType.SalesShipment ||
                         x.TransactionType == StorageTransactionType.SalesShipmentReturn ||
                         x.TransactionType == StorageTransactionType.WarehouseLoss ||
                         x.TransactionType == StorageTransactionType.WarehouseGain));

        if (upToDate.HasValue)
        {
            var limit = upToDate.Value.Date.AddDays(1);
            query = query.Where(x => x.TransactionDate == null || x.TransactionDate < limit);
        }

        var total = await query
            .SumAsync(x => (x.TransactionType == StorageTransactionType.Purchase ||
                            x.TransactionType == StorageTransactionType.SalesShipmentReturn ||
                            x.TransactionType == StorageTransactionType.WarehouseGain)
                ? x.NetWeight
                : -x.NetWeight);

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }
```

  Remarks paragraph to add:

```csharp
/// <para>
/// <c>WarehouseLoss(13)</c> e <c>WarehouseGain(14)</c> são o efeito da Conferência de Saldo de
/// Armazém (GAC-1164): quebra ou sobra informada pelo armazém de terceiros sobre grão da empresa.
/// Não têm contrato nem lote — só este saldo os enxerga.
/// </para>
```

- [ ] **Step 5: Run the balance tests and confirm they pass.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageTransactionsWarehouseBalanceTests"`
Expected: PASS, including the 5 existing tests.

- [ ] **Step 6: Write the failing guard tests.** Create `StorageTransactions/WarehouseAdjustmentTransactionGuardsTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// Romaneio de Perda/Sobra de armazém só nasce e morre pela Conferência de Saldo. As telas
/// genéricas de Romaneios (confirmar, cancelar, estornar) não podem mexer nele.
/// </summary>
public class WarehouseAdjustmentTransactionGuardsTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<StorageTransaction> SeedAsync(
        StorageTransactionType type, StorageTransactionsStatus status)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "AJ-0001",
            CardCode = "ARM-T",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM-T",
            BranchCode = "01",
            TransactionType = type,
            TransactionStatus = status,
            TransactionOrigin = TransactionCode.WarehouseReconciliation,
            GrossWeight = 100m,
            NetWeight = 100m,
        };

        _db.Context.StorageTransactions.Add(transaction);
        await _db.SaveChangesAsync();
        return transaction;
    }

    private async Task<StorageTransactionsStatus> StatusOf(Guid key) =>
        (await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == key))
        .TransactionStatus;

    [Theory]
    [InlineData(StorageTransactionType.WarehouseLoss)]
    [InlineData(StorageTransactionType.WarehouseGain)]
    public async Task Generic_confirmation_refuses_warehouse_adjustments(StorageTransactionType type)
    {
        var transaction = await SeedAsync(type, StorageTransactionsStatus.Pending);
        var service = new StorageTransactionsConfirmedService(
            _db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(transaction, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_TRANSACTION_NOT_CONFIRMABLE", ex.Message);
        Assert.Equal(StorageTransactionsStatus.Pending, await StatusOf(transaction.Key));
    }

    [Fact]
    public async Task Generic_cancel_refuses_reconciliation_origin()
    {
        var transaction = await SeedAsync(
            StorageTransactionType.WarehouseLoss, StorageTransactionsStatus.Confirmed);
        var service = new StorageTransactionsCancelService(
            _db, new ShipmentReleasesRecalculateShippedService(_db.Context));

        await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(transaction.Key, "tester"));

        Assert.Equal(StorageTransactionsStatus.Confirmed, await StatusOf(transaction.Key));
    }

    [Fact]
    public async Task Generic_reverse_refuses_reconciliation_origin()
    {
        var transaction = await SeedAsync(
            StorageTransactionType.WarehouseGain, StorageTransactionsStatus.Confirmed);
        // O saldo de lote não é consultado: o guard de origem dispara antes.
        var service = new StorageTransactionsReverseService(
            _db,
            new StorageAddressesGetBalanceService(null!),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new FakeStringLocalizer<Resource>());

        await Assert.ThrowsAsync<ApplicationException>(
            () => service.ExecuteAsync(transaction.Key, "tester"));

        Assert.Equal(StorageTransactionsStatus.Confirmed, await StatusOf(transaction.Key));
    }
}
```

  Create `Reports/StorageStatementReportHelperTests.cs`:

```csharp
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Application.Tests.Reports;

public class StorageStatementReportHelperTests
{
    [Fact]
    public void Warehouse_gain_is_an_entry_labelled_sobra()
    {
        Assert.Equal("Sobra Armazém",
            StorageStatementReportHelper.GetTransactionTypeName(StorageTransactionType.WarehouseGain));
        Assert.True(StorageStatementReportHelper.IsEntry(StorageTransactionType.WarehouseGain));
        Assert.Equal(10m,
            StorageStatementReportHelper.GetSignedQuantity(StorageTransactionType.WarehouseGain, 10m));
    }

    [Fact]
    public void Warehouse_loss_is_an_exit_labelled_perda()
    {
        Assert.Equal("Perda Armazém",
            StorageStatementReportHelper.GetTransactionTypeName(StorageTransactionType.WarehouseLoss));
        Assert.True(StorageStatementReportHelper.IsExit(StorageTransactionType.WarehouseLoss));
        Assert.Equal(-10m,
            StorageStatementReportHelper.GetSignedQuantity(StorageTransactionType.WarehouseLoss, 10m));
    }
}
```

- [ ] **Step 7: Run the new tests and confirm they fail.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseAdjustmentTransactionGuardsTests|FullyQualifiedName~StorageStatementReportHelperTests"`
Expected:
- the confirmation theory FAILS, because the `default` branch runs the purchase flow;
- the helper tests FAIL ("Outros" != "Sobra Armazém");
- the cancel and reverse tests already PASS, because the origin guards exist. Keep them as regression tests.

- [ ] **Step 8: Implement the confirmation case.** In `StorageTransactionsConfirmedService.Processing`,
  add these cases before `default:`:

```csharp
            // Perda/Sobra de armazém nascem Confirmadas pela aprovação da Conferência de Saldo.
            // Sem este case caíam no default, que trata como COMPRA: aplicava descontos e
            // tornava o volume alocável a contrato.
            case StorageTransactionType.WarehouseLoss:
            case StorageTransactionType.WarehouseGain:
                throw new ApplicationException(
                    resource["WAREHOUSE_RECONCILIATION_TRANSACTION_NOT_CONFIRMABLE"].Value);
```

  In `Resource.pt-br.resx`, add before `</root>`:

```xml
    <data name="WAREHOUSE_RECONCILIATION_TRANSACTION_NOT_CONFIRMABLE" xml:space="preserve">
        <value>Romaneio de perda/sobra de armazém é gerado pela Conferência de Saldo de Armazém e não pode ser confirmado por aqui.</value>
    </data>
```

  In `Resource.resx`, add before `</root>`:

```xml
    <data name="WAREHOUSE_RECONCILIATION_TRANSACTION_NOT_CONFIRMABLE" xml:space="preserve">
        <value>Warehouse loss/gain transactions are generated by the Warehouse Balance Reconciliation and cannot be confirmed here.</value>
    </data>
```

- [ ] **Step 9: Implement the report helper.** In `StorageStatementReportHelper.cs`:
  - add to `GetTransactionTypeName`, before `_ =>`:

```csharp
        StorageTransactionType.WarehouseLoss => "Perda Armazém",
        StorageTransactionType.WarehouseGain => "Sobra Armazém",
```

  - add `StorageTransactionType.WarehouseGain => true,` to `IsEntry`;
  - add `StorageTransactionType.WarehouseLoss => true,` to `IsExit`.

- [ ] **Step 10: Run the full test suite and confirm it passes.**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS, with zero failures and 10 new tests.

- [ ] **Step 11: Stage the changes.**

```bash
git add SiagroB1.Application.Tests/StorageTransactions/WarehouseAdjustmentTransactionGuardsTests.cs SiagroB1.Application.Tests/Reports/StorageStatementReportHelperTests.cs
```

  Do not commit. Modified tracked files are left for the user to review.

---

### Task 2: Domain model, EF mapping and migrations

**Files:**
- Create: `SiagroB1.Domain/Enums/WarehouseReconciliationStatus.cs`
- Create: `SiagroB1.Domain/Entities/WarehouseReconciliationReason.cs`
- Create: `SiagroB1.Domain/Entities/WarehouseReconciliation.cs`
- Create: `SiagroB1.Domain/Entities/WarehouseReconciliationAttachment.cs`
- Create: `SiagroB1.Domain/Dtos/WarehouseReconciliationBalancePreviewDto.cs`
- Create: `SiagroB1.Domain/Dtos/WarehouseReconciliationAttachmentDto.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`, adding DbSets near line 63 and the model config at the end of `OnModelCreating`
- Create (generated, then edited): the 3 AppContext migrations and the 1 CommonContext migration

**Interfaces:**
- Produces:
  - Entities `WarehouseReconciliation`, `WarehouseReconciliationReason` and
    `WarehouseReconciliationAttachment`, with the properties below.
  - DbSets `WarehouseReconciliations`, `WarehouseReconciliationReasons` and
    `WarehouseReconciliationAttachments`.
  - Enum `WarehouseReconciliationStatus` with values `Draft = 0`, `InApproval = 1`, `Approved = 2`,
    `Rejected = 3` and `Cancelled = 4`.
  - DTOs `WarehouseReconciliationBalancePreviewDto` and `WarehouseReconciliationAttachmentDto`.

- [ ] **Step 1: Create the enum.** `WarehouseReconciliationStatus.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

public enum WarehouseReconciliationStatus
{
    Draft = 0,       // Rascunho
    InApproval = 1,  // Em aprovação
    Approved = 2,    // Aprovada - gerou o romaneio de Perda/Sobra
    Rejected = 3,    // Rejeitada (final)
    Cancelled = 4,   // Cancelada
}
```

- [ ] **Step 2: Create the reason entity.** `WarehouseReconciliationReason.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Motivo da diferença apurada numa Conferência de Saldo de Armazém (quebra técnica,
/// sinistro, umidade...). Cadastro editável; motivo em uso só pode ser desativado.
/// </summary>
[Table("WAREHOUSE_RECONCILIATION_REASONS")]
public class WarehouseReconciliationReason : BaseEntity
{
    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }

    public bool Active { get; set; } = true;
}
```

- [ ] **Step 3: Create the reconciliation and attachment entities.**
  `WarehouseReconciliation.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Conferência de Saldo de Armazém (GAC-1164): o armazém de TERCEIROS informa o saldo físico
/// do grão da empresa e a diferença para o sistema vira, na aprovação, um romaneio de
/// <see cref="StorageTransactionType.WarehouseLoss"/> ou <see cref="StorageTransactionType.WarehouseGain"/>.
/// </summary>
/// <remarks>
/// Nunca toca contrato: a quebra de estoque próprio não é do produtor. <see cref="SystemBalance"/>
/// e <see cref="Difference"/> são SNAPSHOT recalculado no envio e na aprovação, sobre o saldo de
/// armazém até <see cref="ReferenceDate"/>.
/// </remarks>
[Table("WAREHOUSE_RECONCILIATIONS")]
public class WarehouseReconciliation : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50)")]
    public string? Code { get; set; }

    public WarehouseReconciliationStatus Status { get; set; } = WarehouseReconciliationStatus.Draft;

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string WarehouseCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? WarehouseName { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string UnitOfMeasureCode { get; set; }

    /// <summary>Parceiro do romaneio gerado: o próprio armazém (armazém é parceiro com QryGroup23).</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    /// <summary>Data do extrato do armazém. O saldo do sistema é apurado até o fim deste dia.</summary>
    public DateTime ReferenceDate { get; set; }

    /// <summary>Saldo físico informado pelo armazém.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal ReportedBalance { get; set; }

    /// <summary>Snapshot do saldo de armazém do sistema até <see cref="ReferenceDate"/>.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal SystemBalance { get; set; }

    /// <summary><c>ReportedBalance − SystemBalance</c>: negativo = Perda, positivo = Sobra.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal Difference { get; set; }

    public Guid ReasonKey { get; set; }
    public virtual WarehouseReconciliationReason? Reason { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    public DateTime? SentAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? SentBy { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? ApprovalComments { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? CancellationReason { get; set; }

    /// <summary>Romaneio de Perda/Sobra gerado na aprovação.</summary>
    public Guid? StorageTransactionKey { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public virtual ICollection<WarehouseReconciliationAttachment> Attachments { get; set; } = [];
}
```

  `WarehouseReconciliationAttachment.cs`, copied from `PurchaseContractAttachment`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Extrato do armazém (ou outra evidência) anexado à Conferência de Saldo. Opcional.</summary>
[Table("WAREHOUSE_RECONCILIATION_ATTACHMENTS")]
public class WarehouseReconciliationAttachment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid WarehouseReconciliationKey { get; set; }
    public virtual WarehouseReconciliation? WarehouseReconciliation { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string FileName { get; set; }

    [Column(TypeName = "VARBINARY(MAX)")]
    public required byte[] FileData { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string ContentType { get; set; }

    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }
}
```

- [ ] **Step 4: Create the DTOs.** `WarehouseReconciliationBalancePreviewDto.cs`:

```csharp
namespace SiagroB1.Domain.Dtos;

public class WarehouseReconciliationBalancePreviewDto
{
    public decimal SystemBalance { get; set; }
    public bool IsOwnWarehouse { get; set; }
    public DateTime? LastApprovedReferenceDate { get; set; }
    public bool HasOpenReconciliation { get; set; }
}
```

  `WarehouseReconciliationAttachmentDto.cs`:

```csharp
namespace SiagroB1.Domain.Dtos;

public class WarehouseReconciliationAttachmentDto
{
    public Guid? Key { get; set; }
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
}
```

- [ ] **Step 5: Map the entities in `AppDbContext`.**
  - Add after `public DbSet<OwnershipTransfer> OwnershipTransfers { get; set; }`:

```csharp
    public DbSet<WarehouseReconciliation> WarehouseReconciliations { get; set; }
    public DbSet<WarehouseReconciliationReason> WarehouseReconciliationReasons { get; set; }
    public DbSet<WarehouseReconciliationAttachment> WarehouseReconciliationAttachments { get; set; }
```

  - Add at the end of `OnModelCreating`, after the `FinancialSettlement` index:

```csharp
        // Conferência de Saldo de Armazém: no máximo UMA em aberto (Rascunho/Em aprovação) por
        // armazém+produto. O serviço já recusa; o índice é a trava contra caminho novo.
        modelBuilder.Entity<WarehouseReconciliation>()
            .HasIndex(x => new { x.WarehouseCode, x.ItemCode }, "IX_WAREHOUSE_RECONCILIATIONS_OpenPerWarehouseItem")
            .IsUnique()
            .HasFilter($"[Status] IN ({(int)WarehouseReconciliationStatus.Draft}, " +
                       $"{(int)WarehouseReconciliationStatus.InApproval})");

        // Motivo em uso não se apaga (o serviço desativa); Restrict impede também no banco.
        modelBuilder.Entity<WarehouseReconciliation>()
            .HasOne(x => x.Reason)
            .WithMany()
            .HasForeignKey(x => x.ReasonKey)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WarehouseReconciliation>()
            .HasMany(x => x.Attachments)
            .WithOne(x => x.WarehouseReconciliation)
            .HasForeignKey(x => x.WarehouseReconciliationKey)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WarehouseReconciliationReason>()
            .HasIndex(x => x.Code)
            .IsUnique();
```

  - Add `using SiagroB1.Domain.Enums;` if it is missing.

- [ ] **Step 6: Build.**

Run: `dotnet build SiagroB1.sln`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 7: Run the model guard tests.** They include `AppDbContextModelTests`, which validates `TypeName`.

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~AppDbContextModelTests"`
Expected: PASS.

- [ ] **Step 8: Generate the schema migration and review it.**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef migrations add AddWarehouseReconciliations --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o AppContext
```

  Open the generated `AppContext/*_AddWarehouseReconciliations.cs`. It must contain ONLY:
  - `CreateTable` for `WAREHOUSE_RECONCILIATION_REASONS`, `WAREHOUSE_RECONCILIATIONS` and
    `WAREHOUSE_RECONCILIATION_ATTACHMENTS`;
  - the FKs to `DOC_NUMBERS`, `BRANCHES` and the reason;
  - the filtered unique index and the unique index on `Code`.

  If it touches any other table, the snapshot has drifted. Stop and report instead of applying.

- [ ] **Step 9: Create the doc-number seed.**

```powershell
dotnet ef migrations add SeedWarehouseReconciliationDocNumber --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o AppContext
```

  Replace the class body with:

```csharp
    /// <summary>
    /// Numeração padrão da Conferência de Saldo de Armazém (TransactionCode = 13). Mesmo motivo de
    /// <c>SeedShipmentLoadDocNumber</c>: sem linha <c>[Default] = 1</c> o
    /// <c>DocNumberSequenceService</c> lança e a primeira conferência falha. SQL idempotente e GUID
    /// fixo para o Down remover só esta linha.
    /// </summary>
    public partial class SeedWarehouseReconciliationDocNumber : Migration
    {
        private const string SeedKey = "3F6A2B91-7C4D-4E58-A1B2-9D0E8C7F6A13";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                IF NOT EXISTS (SELECT 1 FROM DOC_NUMBERS WHERE TransactionCode = 13)
                INSERT INTO DOC_NUMBERS ([Key], TransactionCode, Name, FirstNumber, LastNumber,
                                         NextNumber, [Default], Prefix, Suffix, BranchCode,
                                         Inactive, IsManual, NumberSize)
                VALUES ('{SeedKey}', 13, 'CONFERENCIA SALDO', 1, 0, 1, 1, 'CS', '', NULL, 0, 0, '6');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DELETE FROM DOC_NUMBERS WHERE [Key] = '{SeedKey}';");
        }
    }
```

- [ ] **Step 10: Create the reasons seed.**

```powershell
dotnet ef migrations add SeedWarehouseReconciliationReasons --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o AppContext
```

  Replace the class body with:

```csharp
    /// <summary>Motivos iniciais da Conferência de Saldo de Armazém. Idempotente por Code; GUIDs fixos.</summary>
    public partial class SeedWarehouseReconciliationReasons : Migration
    {
        private static readonly (string Key, string Code, string Description)[] Reasons =
        [
            ("6B1C0E27-3A94-4F1D-8E62-1A7B3C9D0E41", "QUEBRA_TECNICA", "Quebra técnica"),
            ("7C2D1F38-4BA5-4028-9F73-2B8C4DAE1F52", "SINISTRO", "Sinistro/Tombamento"),
            ("8D3E2049-5CB6-4139-A084-3C9D5EBF2063", "UMIDADE", "Umidade/Secagem"),
            ("9E4F315A-6DC7-424A-B195-4DAE6FC03174", "PESAGEM", "Divergência de pesagem"),
            ("AF50426B-7ED8-435B-C2A6-5EBF70D14285", "SOBRA", "Sobra de estoque"),
        ];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (key, code, description) in Reasons)
            {
                migrationBuilder.Sql($"""
                    IF NOT EXISTS (SELECT 1 FROM WAREHOUSE_RECONCILIATION_REASONS WHERE Code = '{code}')
                    INSERT INTO WAREHOUSE_RECONCILIATION_REASONS
                        ([Key], Code, Description, Active, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, ApprovedBy, CanceledBy)
                    VALUES ('{key}', '{code}', N'{description}', 1, GETDATE(), 'system', GETDATE(), 'system', '', '');
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (key, _, _) in Reasons)
                migrationBuilder.Sql($"DELETE FROM WAREHOUSE_RECONCILIATION_REASONS WHERE [Key] = '{key}';");
        }
    }
```

  Check that the generated `CreateTable` of `WAREHOUSE_RECONCILIATION_REASONS` has exactly these
  column names. `RowId` is an identity column and is not inserted. Adjust the column list if the
  names differ.

  Also check the fifth GUID. `AF50426B-7ED8-435B-C2A6-5EBF70D14285` is valid hex, but if SQL Server
  rejects it, replace it with any fixed GUID.

- [ ] **Step 11: Create the menu migration.**

```powershell
dotnet ef migrations add AddWarehouseReconciliationMenus --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o CommonContext
```

  Replace the class body with the code below. The "storage" children take Order 1-9 in
  `InitialMenuSeed`, so the new items take 10, 11 and 12. Each Key must equal the route name in the
  frontend `manifest.json`.

```csharp
    public partial class AddWarehouseReconciliationMenus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "warehouseReconciliations", "Conferência de Saldo de Armazém", "sap-icon://folder-blank", true, false, 10, "storage" },
                    { "warehouseReconciliationsApproval", "Aprovação de Conferências de Saldo", "sap-icon://folder-blank", true, false, 11, "storage" },
                    { "warehouseReconciliationReasons", "Motivos de Conferência de Saldo", "sap-icon://folder-blank", true, false, 12, "storage" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "1A2B3C4D-5E6F-4A7B-8C9D-0E1F2A3B4C51", "ADMIN", "warehouseReconciliations" },
                    { "2B3C4D5E-6F7A-4B8C-9D0E-1F2A3B4C5D62", "ADMIN", "warehouseReconciliationsApproval" },
                    { "3C4D5E6F-7A8B-4C9D-8E1F-2A3B4C5D6E73", "ADMIN", "warehouseReconciliationReasons" },
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues:
                [
                    "1A2B3C4D-5E6F-4A7B-8C9D-0E1F2A3B4C51",
                    "2B3C4D5E-6F7A-4B8C-9D0E-1F2A3B4C5D62",
                    "3C4D5E6F-7A8B-4C9D-8E1F-2A3B4C5D6E73",
                ]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["warehouseReconciliations", "warehouseReconciliationsApproval", "warehouseReconciliationReasons"]);
        }
    }
```

- [ ] **Step 12: Check that the model is in sync.**

Run: `dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`
Expected: `No changes have been made to the model since the last migration.`

- [ ] **Step 13: Confirm the target database, then apply.**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
(Get-Content SiagroB1.Web\appsettings.Yokotobi-Development.json -Raw | ConvertFrom-Json).ConnectionStrings.SiagroDB -split ';' | Where-Object { $_ -match '^(Server|Data Source|Database|Initial Catalog)=' }
dotnet ef migrations list --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

  Expected: Server = localhost and Database = `IDX_SIAGRO_DEV`, and only the 3 new migrations
  listed as `(Pending)`. If anything else shows as Pending, STOP and report.

```powershell
dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef database update --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

  Expected: both commands end with `Done.`

- [ ] **Step 14: Stage the changes.**

```bash
git add SiagroB1.Domain/Enums/WarehouseReconciliationStatus.cs SiagroB1.Domain/Entities/WarehouseReconciliation.cs SiagroB1.Domain/Entities/WarehouseReconciliationReason.cs SiagroB1.Domain/Entities/WarehouseReconciliationAttachment.cs SiagroB1.Domain/Dtos/WarehouseReconciliationBalancePreviewDto.cs SiagroB1.Domain/Dtos/WarehouseReconciliationAttachmentDto.cs SiagroB1.Migrations/AppContext SiagroB1.Migrations/CommonContext
```

---

### Task 3: Reasons register and the shared test context

**Files:**
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationReasonsGetService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationReasonsCreateService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationReasonsUpdateService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationReasonsDeleteService.cs`
- Create: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationsTestContext.cs`
- Test: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationReasonsServiceTests.cs`
- Modify: `Resource.resx`, `Resource.pt-br.resx`

**Interfaces:**
- Consumes (from Task 2): `WarehouseReconciliationReason`, `DbSet WarehouseReconciliationReasons`,
  and `DbSet WarehouseReconciliations` (for the "in use" check).
- Produces:
  - `WarehouseReconciliationReasonsGetService`: `IQueryable<WarehouseReconciliationReason> QueryAll()`
    and `Task<WarehouseReconciliationReason?> GetByIdAsync(Guid key)` (AsNoTracking).
  - `WarehouseReconciliationReasonsCreateService.ExecuteAsync(WarehouseReconciliationReason reason, string userName)`
  - `WarehouseReconciliationReasonsUpdateService.ExecuteAsync(Guid key, WarehouseReconciliationReason input, string userName)`
  - `WarehouseReconciliationReasonsDeleteService.ExecuteAsync(Guid key)`
  - `WarehouseReconciliationsTestContext`, with constants `ThirdPartyWarehouse = "ARM-T"`,
    `OwnWarehouse = "ARM-P"`, `Item = "SOJA"`, `Branch = "01"` and members `Db`, `Resource`,
    `SeedReasonAsync`, `SeedOwnWarehouseAsync`, `SeedStockAsync` and `NewReconciliation`. Tasks 4-7
    add factory methods to this class.
  - Resource keys `WAREHOUSE_RECONCILIATION_REASON_NOT_FOUND`,
    `WAREHOUSE_RECONCILIATION_REASON_REQUIRED_FIELDS`,
    `WAREHOUSE_RECONCILIATION_REASON_CODE_DUPLICATE` and `WAREHOUSE_RECONCILIATION_REASON_IN_USE`.

- [ ] **Step 1: Create the test context.** `WarehouseReconciliationsTestContext.cs`:

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

/// <summary>
/// Monta os serviços da Conferência de Saldo de Armazém sobre um banco InMemory. Usa o
/// <c>WarehouseComplementService</c> REAL (lê a mesma WAREHOUSE_COMPLEMENTS semeada aqui), para
/// que o gate "armazém de terceiros" seja exercitado de ponta a ponta.
/// </summary>
internal sealed partial class WarehouseReconciliationsTestContext
{
    public const string ThirdPartyWarehouse = "ARM-T";
    public const string OwnWarehouse = "ARM-P";
    public const string Item = "SOJA";
    public const string Branch = "01";

    public UnitOfWork Db { get; } = TestDb.CreateUnitOfWork();
    public FakeStringLocalizer<Resource> Resource { get; } = new();

    private int _seq;

    public async Task<WarehouseReconciliationReason> SeedReasonAsync(bool active = true, string? code = null)
    {
        var reason = new WarehouseReconciliationReason
        {
            Key = Guid.NewGuid(),
            Code = code ?? $"R{++_seq}",
            Description = "Quebra técnica",
            Active = active,
        };
        Db.Context.WarehouseReconciliationReasons.Add(reason);
        await Db.Context.SaveChangesAsync();
        return reason;
    }

    public async Task SeedOwnWarehouseAsync()
    {
        Db.Context.WarehouseComplements.Add(new WarehouseComplement { WarehouseCode = OwnWarehouse, IsOwn = true });
        await Db.Context.SaveChangesAsync();
    }

    public async Task<StorageTransaction> SeedStockAsync(
        StorageTransactionType type,
        decimal quantity,
        DateTime? date,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        string warehouse = ThirdPartyWarehouse)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = $"SEED-{++_seq:0000}",
            CardCode = "C0001",
            ItemCode = Item,
            UnitOfMeasureCode = "KG",
            WarehouseCode = warehouse,
            BranchCode = Branch,
            TransactionType = type,
            TransactionStatus = status,
            TransactionDate = date,
            GrossWeight = quantity,
            NetWeight = quantity,
        };
        Db.Context.StorageTransactions.Add(transaction);
        await Db.Context.SaveChangesAsync();
        return transaction;
    }

    public static WarehouseReconciliation NewReconciliation(
        Guid reasonKey,
        decimal reportedBalance,
        DateTime? referenceDate = null,
        string warehouse = ThirdPartyWarehouse) => new()
        {
            WarehouseCode = warehouse,
            ItemCode = Item,
            UnitOfMeasureCode = "KG",
            BranchCode = Branch,
            ReferenceDate = referenceDate ?? DateTime.Today,
            ReportedBalance = reportedBalance,
            ReasonKey = reasonKey,
        };

    public WarehouseReconciliationReasonsCreateService ReasonsCreate() => new(Db, Resource);
    public WarehouseReconciliationReasonsUpdateService ReasonsUpdate() => new(Db, Resource);
    public WarehouseReconciliationReasonsDeleteService ReasonsDelete() => new(Db, Resource);
}
```

  The class is `partial` so Tasks 4-7 can add factory methods in separate files. Add
  `using SiagroB1.Application.Services.WarehouseReconciliations;` at the top.

- [ ] **Step 2: Write the failing tests.** `WarehouseReconciliationReasonsServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationReasonsServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();

    [Fact]
    public async Task Create_normalizes_code_and_starts_active()
    {
        var reason = new WarehouseReconciliationReason { Code = " quebra ", Description = "Quebra técnica" };

        await _ctx.ReasonsCreate().ExecuteAsync(reason, "tester");

        var saved = await _ctx.Db.Context.WarehouseReconciliationReasons.AsNoTracking().SingleAsync();
        Assert.Equal("QUEBRA", saved.Code);
        Assert.True(saved.Active);
        Assert.Equal("tester", saved.CreatedBy);
    }

    [Fact]
    public async Task Create_refuses_duplicate_code()
    {
        await _ctx.SeedReasonAsync(code: "QUEBRA");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.ReasonsCreate().ExecuteAsync(
            new WarehouseReconciliationReason { Code = "quebra", Description = "Outra" }, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_REASON_CODE_DUPLICATE", ex.Message);
    }

    [Fact]
    public async Task Create_refuses_blank_description()
    {
        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.ReasonsCreate().ExecuteAsync(
            new WarehouseReconciliationReason { Code = "X", Description = " " }, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_REASON_REQUIRED_FIELDS", ex.Message);
    }

    [Fact]
    public async Task Update_can_deactivate()
    {
        var reason = await _ctx.SeedReasonAsync(code: "QUEBRA");

        await _ctx.ReasonsUpdate().ExecuteAsync(reason.Key,
            new WarehouseReconciliationReason { Code = "QUEBRA", Description = "Quebra", Active = false }, "tester");

        var saved = await _ctx.Db.Context.WarehouseReconciliationReasons.AsNoTracking().SingleAsync();
        Assert.False(saved.Active);
    }

    [Fact]
    public async Task Update_refuses_code_of_another_reason()
    {
        await _ctx.SeedReasonAsync(code: "A");
        var b = await _ctx.SeedReasonAsync(code: "B");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.ReasonsUpdate().ExecuteAsync(b.Key,
            new WarehouseReconciliationReason { Code = "A", Description = "B", Active = true }, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_REASON_CODE_DUPLICATE", ex.Message);
    }

    [Fact]
    public async Task Delete_refuses_reason_in_use()
    {
        var reason = await _ctx.SeedReasonAsync();
        var reconciliation = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m);
        reconciliation.Key = Guid.NewGuid();
        _ctx.Db.Context.WarehouseReconciliations.Add(reconciliation);
        await _ctx.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.ReasonsDelete().ExecuteAsync(reason.Key));

        Assert.Equal("WAREHOUSE_RECONCILIATION_REASON_IN_USE", ex.Message);
    }

    [Fact]
    public async Task Delete_removes_unused_reason()
    {
        var reason = await _ctx.SeedReasonAsync();

        await _ctx.ReasonsDelete().ExecuteAsync(reason.Key);

        Assert.False(await _ctx.Db.Context.WarehouseReconciliationReasons.AnyAsync());
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationReasonsServiceTests"`
Expected: build error, `The type or namespace name 'WarehouseReconciliationReasonsCreateService' could not be found`.

- [ ] **Step 4: Implement the services.** `WarehouseReconciliationReasonsGetService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationReasonsGetService(IUnitOfWork db)
{
    public IQueryable<WarehouseReconciliationReason> QueryAll() =>
        db.Context.WarehouseReconciliationReasons.AsNoTracking();

    // AsNoTracking: o PATCH aplica o Delta sobre esta instância e o Update carrega a sua cópia.
    public Task<WarehouseReconciliationReason?> GetByIdAsync(Guid key) =>
        db.Context.WarehouseReconciliationReasons.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
}
```

  `WarehouseReconciliationReasonsCreateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationReasonsCreateService(IUnitOfWork db, IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(WarehouseReconciliationReason reason, string userName)
    {
        reason.Code = (reason.Code ?? string.Empty).Trim().ToUpperInvariant();
        reason.Description = (reason.Description ?? string.Empty).Trim();

        if (reason.Code.Length == 0 || reason.Description.Length == 0)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_REASON_REQUIRED_FIELDS"].Value);

        if (await db.Context.WarehouseReconciliationReasons.AnyAsync(x => x.Code == reason.Code))
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_REASON_CODE_DUPLICATE"].Value);

        reason.Active = true;
        reason.CreatedAt = DateTime.Now;
        reason.CreatedBy = userName;
        reason.UpdatedAt = DateTime.Now;
        reason.UpdatedBy = userName;

        await db.Context.WarehouseReconciliationReasons.AddAsync(reason);
        await db.SaveChangesAsync();
    }
}
```

  `WarehouseReconciliationReasonsUpdateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationReasonsUpdateService(IUnitOfWork db, IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, WarehouseReconciliationReason input, string userName)
    {
        var reason = await db.Context.WarehouseReconciliationReasons.FirstOrDefaultAsync(x => x.Key == key)
                     ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_REASON_NOT_FOUND"].Value);

        var code = (input.Code ?? string.Empty).Trim().ToUpperInvariant();
        var description = (input.Description ?? string.Empty).Trim();

        if (code.Length == 0 || description.Length == 0)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_REASON_REQUIRED_FIELDS"].Value);

        if (await db.Context.WarehouseReconciliationReasons.AnyAsync(x => x.Key != key && x.Code == code))
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_REASON_CODE_DUPLICATE"].Value);

        // Atribuição explícita: um PATCH não pode reescrever auditoria.
        reason.Code = code;
        reason.Description = description;
        reason.Active = input.Active;
        reason.UpdatedAt = DateTime.Now;
        reason.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
```

  `WarehouseReconciliationReasonsDeleteService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationReasonsDeleteService(IUnitOfWork db, IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key)
    {
        var reason = await db.Context.WarehouseReconciliationReasons.FirstOrDefaultAsync(x => x.Key == key)
                     ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_REASON_NOT_FOUND"].Value);

        // Motivo em uso é histórico de conferência: desativa-se, não se apaga.
        if (await db.Context.WarehouseReconciliations.AnyAsync(x => x.ReasonKey == key))
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_REASON_IN_USE"].Value);

        db.Context.WarehouseReconciliationReasons.Remove(reason);
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Add the resource keys.** Add them before `</root>` in both files.

  `Resource.pt-br.resx`:

```xml
    <data name="WAREHOUSE_RECONCILIATION_REASON_NOT_FOUND" xml:space="preserve">
        <value>Motivo de conferência não encontrado.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_REASON_REQUIRED_FIELDS" xml:space="preserve">
        <value>Informe o código e a descrição do motivo.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_REASON_CODE_DUPLICATE" xml:space="preserve">
        <value>Já existe um motivo com este código.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_REASON_IN_USE" xml:space="preserve">
        <value>Motivo em uso por conferências de saldo. Desative-o em vez de excluir.</value>
    </data>
```

  `Resource.resx`:

```xml
    <data name="WAREHOUSE_RECONCILIATION_REASON_NOT_FOUND" xml:space="preserve">
        <value>Reconciliation reason not found.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_REASON_REQUIRED_FIELDS" xml:space="preserve">
        <value>Reason code and description are required.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_REASON_CODE_DUPLICATE" xml:space="preserve">
        <value>A reason with this code already exists.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_REASON_IN_USE" xml:space="preserve">
        <value>Reason is used by reconciliations. Deactivate it instead of deleting.</value>
    </data>
```

- [ ] **Step 6: Run the tests and confirm they pass.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationReasonsServiceTests"`
Expected: PASS (7 tests).

- [ ] **Step 7: Stage the new files.**

```bash
git add SiagroB1.Application/Services/WarehouseReconciliations SiagroB1.Application.Tests/WarehouseReconciliations
```

---

### Task 4: Guards, create, update, get and balance preview

**Files:**
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsGuardService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsDescriptionService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsGetService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsCreateService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsUpdateService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsGetBalancePreviewService.cs`
- Create: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationsTestContext.Reconciliations.cs`
- Test: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationsCreateServiceTests.cs`
- Modify: `Resource.resx`, `Resource.pt-br.resx`

**Interfaces:**
- Consumes:
  - `StorageTransactionsWarehouseBalanceService.CalculateAsync(context, warehouse, item, upToDate)` (Task 1)
  - the entities and DbSets (Task 2)
  - `WarehouseReconciliationsTestContext` (Task 3)
  - `IWarehouseComplementService.GetAsync(string)` → `WarehouseComplementDto?` with `IsOwn`
- Produces:
  - `WarehouseReconciliationsGuardService`:
    - `Task EnsureCanPersistAsync(WarehouseReconciliation r)`
    - `Task RefreshSnapshotAsync(WarehouseReconciliation r)`
    - `Task<DateTime?> LastApprovedReferenceDateAsync(string warehouseCode, string itemCode, Guid? ignoreKey = null)`
    - `Task<bool> HasOpenAsync(string warehouseCode, string itemCode, Guid? ignoreKey = null)`
  - `WarehouseReconciliationsDescriptionService.FillAsync(WarehouseReconciliation r)`
  - `WarehouseReconciliationsGetService`: `QueryAll()` and `GetByIdAsync(Guid)`, both AsNoTracking
  - `WarehouseReconciliationsCreateService.ExecuteAsync(WarehouseReconciliation r, string userName)`
  - `WarehouseReconciliationsUpdateService.ExecuteAsync(Guid key, WarehouseReconciliation input, string userName)`
  - `WarehouseReconciliationsGetBalancePreviewService.ExecuteAsync(string warehouseCode, string itemCode, DateTime referenceDate)` → `WarehouseReconciliationBalancePreviewDto`
  - Resource keys `WAREHOUSE_RECONCILIATION_NOT_FOUND`, `..._REQUIRED_FIELDS`, `..._OWN_WAREHOUSE`,
    `..._NEGATIVE_REPORTED_BALANCE`, `..._FUTURE_DATE`, `..._REASON_INACTIVE`,
    `..._DATE_BEFORE_LAST_APPROVED`, `..._ALREADY_OPEN` and `..._NOT_DRAFT`

- [ ] **Step 1: Add the test factories.** `WarehouseReconciliationsTestContext.Reconciliations.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public WarehouseReconciliationsGuardService Guard() =>
        new(Db, new WarehouseComplementService(Db), Resource);

    public WarehouseReconciliationsDescriptionService Descriptions() =>
        new(new FakeItemService(new() { [Item] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { [ThirdPartyWarehouse] = "Armazém Terceiro" }),
            new FakeBusinessPartnerService(new() { [ThirdPartyWarehouse] = "Armazém Terceiro Ltda" }));

    public WarehouseReconciliationsCreateService Create() =>
        new(Db, new FakeDocNumberSequenceService(), Descriptions(), Guard());

    public WarehouseReconciliationsUpdateService Update() =>
        new(Db, Descriptions(), Guard(), Resource);

    public WarehouseReconciliationsGetBalancePreviewService Preview() =>
        new(Db, new WarehouseComplementService(Db), Guard());

    public async Task<WarehouseReconciliation> CreateDraftAsync(decimal reportedBalance, DateTime? referenceDate = null)
    {
        var reason = await SeedReasonAsync();
        var reconciliation = NewReconciliation(reason.Key, reportedBalance, referenceDate);
        await Create().ExecuteAsync(reconciliation, "tester");
        return reconciliation;
    }

    /// <summary>Grava direto no banco, sem passar pelos serviços — para montar cenário de guard.</summary>
    public async Task<WarehouseReconciliation> SeedWithStatusAsync(
        WarehouseReconciliationStatus status, DateTime referenceDate, decimal difference = -10m)
    {
        var reason = await SeedReasonAsync();
        var reconciliation = NewReconciliation(reason.Key, 100m, referenceDate);
        reconciliation.Key = Guid.NewGuid();
        reconciliation.Status = status;
        reconciliation.Difference = difference;
        reconciliation.ApprovedAt = status == WarehouseReconciliationStatus.Approved ? DateTime.Now : null;
        Db.Context.WarehouseReconciliations.Add(reconciliation);
        await Db.Context.SaveChangesAsync();
        return reconciliation;
    }

    public Task<WarehouseReconciliation> ReloadAsync(Guid key) =>
        Db.Context.WarehouseReconciliations.AsNoTracking().SingleAsync(x => x.Key == key);
}
```

- [ ] **Step 2: Write the failing tests.** `WarehouseReconciliationsCreateServiceTests.cs`:

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsCreateServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;

    private Task SeedPurchaseAsync(decimal quantity, DateTime date) =>
        _ctx.SeedStockAsync(StorageTransactionType.Purchase, quantity, date);

    [Fact]
    public async Task Create_stores_a_draft_with_snapshot_code_names_and_the_warehouse_as_partner()
    {
        await SeedPurchaseAsync(1_000m, Today.AddDays(-30));

        var r = await _ctx.CreateDraftAsync(950m, Today.AddHours(15));

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Draft, saved.Status);
        Assert.Equal(Today, saved.ReferenceDate);
        Assert.Equal(1_000m, saved.SystemBalance);
        Assert.Equal(-50m, saved.Difference);
        Assert.Equal(WarehouseReconciliationsTestContext.ThirdPartyWarehouse, saved.CardCode);
        Assert.Equal("Armazém Terceiro Ltda", saved.CardName);
        Assert.Equal("Armazém Terceiro", saved.WarehouseName);
        Assert.Equal("SOJA EM GRAOS", saved.ItemName);
        Assert.False(string.IsNullOrEmpty(saved.Code));
        Assert.Equal("tester", saved.CreatedBy);
    }

    [Fact]
    public async Task Snapshot_ignores_transactions_after_the_reference_date()
    {
        await SeedPurchaseAsync(1_000m, Today.AddDays(-30));
        await SeedPurchaseAsync(500m, Today.AddDays(-1));

        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));

        Assert.Equal(1_000m, (await _ctx.ReloadAsync(r.Key)).SystemBalance);
    }

    [Fact]
    public async Task Own_warehouse_is_refused()
    {
        await _ctx.SeedOwnWarehouseAsync();
        var reason = await _ctx.SeedReasonAsync();
        var r = WarehouseReconciliationsTestContext.NewReconciliation(
            reason.Key, 10m, warehouse: WarehouseReconciliationsTestContext.OwnWarehouse);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_OWN_WAREHOUSE", ex.Message);
    }

    [Fact]
    public async Task Negative_reported_balance_is_refused()
    {
        var reason = await _ctx.SeedReasonAsync();
        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, -1m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NEGATIVE_REPORTED_BALANCE", ex.Message);
    }

    [Fact]
    public async Task Future_reference_date_is_refused()
    {
        var reason = await _ctx.SeedReasonAsync();
        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m, Today.AddDays(1));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_FUTURE_DATE", ex.Message);
    }

    [Fact]
    public async Task Inactive_reason_is_refused()
    {
        var reason = await _ctx.SeedReasonAsync(active: false);
        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_REASON_INACTIVE", ex.Message);
    }

    [Fact]
    public async Task Missing_branch_is_refused()
    {
        var reason = await _ctx.SeedReasonAsync();
        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m);
        r.BranchCode = null;

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_REQUIRED_FIELDS", ex.Message);
    }

    [Theory]
    [InlineData(WarehouseReconciliationStatus.Draft)]
    [InlineData(WarehouseReconciliationStatus.InApproval)]
    public async Task A_second_open_reconciliation_for_the_same_warehouse_and_item_is_refused(
        WarehouseReconciliationStatus openStatus)
    {
        await _ctx.SeedWithStatusAsync(openStatus, Today.AddDays(-5));
        var reason = await _ctx.SeedReasonAsync();
        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ALREADY_OPEN", ex.Message);
    }

    [Fact]
    public async Task Rejected_or_cancelled_do_not_block_a_new_one()
    {
        await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Rejected, Today.AddDays(-5));
        await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Cancelled, Today.AddDays(-5));

        var r = await _ctx.CreateDraftAsync(10m);

        Assert.Equal(WarehouseReconciliationStatus.Draft, (await _ctx.ReloadAsync(r.Key)).Status);
    }

    [Fact]
    public async Task Reference_date_before_the_last_approved_is_refused()
    {
        await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Approved, Today.AddDays(-5));
        var reason = await _ctx.SeedReasonAsync();
        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m, Today.AddDays(-6));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Create().ExecuteAsync(r, "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_DATE_BEFORE_LAST_APPROVED", ex.Message);
    }

    [Fact]
    public async Task Update_recomputes_the_difference()
    {
        await SeedPurchaseAsync(1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(950m);
        var input = WarehouseReconciliationsTestContext.NewReconciliation(r.ReasonKey, 1_100m);

        await _ctx.Update().ExecuteAsync(r.Key, input, "editor");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(1_100m, saved.ReportedBalance);
        Assert.Equal(100m, saved.Difference);
        Assert.Equal("editor", saved.UpdatedBy);
    }

    [Fact]
    public async Task Update_outside_draft_is_refused()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.InApproval, Today);
        var input = WarehouseReconciliationsTestContext.NewReconciliation(r.ReasonKey, 1m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Update().ExecuteAsync(r.Key, input, "editor"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_DRAFT", ex.Message);
    }

    [Fact]
    public async Task Preview_reports_balance_ownership_open_and_last_approved()
    {
        await SeedPurchaseAsync(1_000m, Today.AddDays(-30));
        await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Approved, Today.AddDays(-20));
        await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Draft, Today.AddDays(-10));

        var preview = await _ctx.Preview().ExecuteAsync(
            WarehouseReconciliationsTestContext.ThirdPartyWarehouse, WarehouseReconciliationsTestContext.Item, Today);

        Assert.Equal(1_000m, preview.SystemBalance);
        Assert.False(preview.IsOwnWarehouse);
        Assert.True(preview.HasOpenReconciliation);
        Assert.Equal(Today.AddDays(-20), preview.LastApprovedReferenceDate);
    }

    [Fact]
    public async Task Preview_flags_own_warehouse()
    {
        await _ctx.SeedOwnWarehouseAsync();

        var preview = await _ctx.Preview().ExecuteAsync(
            WarehouseReconciliationsTestContext.OwnWarehouse, WarehouseReconciliationsTestContext.Item, Today);

        Assert.True(preview.IsOwnWarehouse);
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationsCreateServiceTests"`
Expected: build error, `'WarehouseReconciliationsGuardService' could not be found`.

- [ ] **Step 4: Implement the guard service.** `WarehouseReconciliationsGuardService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>
/// Regras da Conferência de Saldo de Armazém compartilhadas por criar, editar, enviar e aprovar.
/// Todas lançam <see cref="ApplicationException"/> com mensagem de negócio e devem ser chamadas
/// ANTES de abrir transação (o catch dos serviços embrulharia a mensagem).
/// </summary>
public class WarehouseReconciliationsGuardService(
    IUnitOfWork db,
    IWarehouseComplementService complements,
    IStringLocalizer<Resource> resource)
{
    public async Task EnsureCanPersistAsync(WarehouseReconciliation r)
    {
        if (string.IsNullOrWhiteSpace(r.BranchCode) ||
            string.IsNullOrWhiteSpace(r.WarehouseCode) ||
            string.IsNullOrWhiteSpace(r.ItemCode) ||
            string.IsNullOrWhiteSpace(r.UnitOfMeasureCode) ||
            r.ReasonKey == Guid.Empty)
            throw Fail("WAREHOUSE_RECONCILIATION_REQUIRED_FIELDS");

        // Sem linha de complemento = terceiros. Armazém próprio tem lotes e quebra técnica por lote.
        if ((await complements.GetAsync(r.WarehouseCode))?.IsOwn == true)
            throw Fail("WAREHOUSE_RECONCILIATION_OWN_WAREHOUSE");

        if (r.ReportedBalance < decimal.Zero)
            throw Fail("WAREHOUSE_RECONCILIATION_NEGATIVE_REPORTED_BALANCE");

        if (r.ReferenceDate.Date > DateTime.Today)
            throw Fail("WAREHOUSE_RECONCILIATION_FUTURE_DATE");

        var reason = await db.Context.WarehouseReconciliationReasons
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == r.ReasonKey);

        if (reason is not { Active: true })
            throw Fail("WAREHOUSE_RECONCILIATION_REASON_INACTIVE");

        // Conferência anterior à última aprovada apuraria diferença sobre um saldo que já recebeu
        // o ajuste posterior — ajustaria duas vezes.
        var lastApproved = await LastApprovedReferenceDateAsync(r.WarehouseCode, r.ItemCode, r.Key);
        if (lastApproved.HasValue && r.ReferenceDate.Date < lastApproved.Value.Date)
            throw Fail("WAREHOUSE_RECONCILIATION_DATE_BEFORE_LAST_APPROVED");

        if (await HasOpenAsync(r.WarehouseCode, r.ItemCode, r.Key))
            throw Fail("WAREHOUSE_RECONCILIATION_ALREADY_OPEN");
    }

    /// <summary>Recalcula o saldo do sistema até a data de referência e a diferença.</summary>
    public async Task RefreshSnapshotAsync(WarehouseReconciliation r)
    {
        r.SystemBalance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            db.Context, r.WarehouseCode, r.ItemCode, r.ReferenceDate);
        r.Difference = r.ReportedBalance - r.SystemBalance;
    }

    public Task<DateTime?> LastApprovedReferenceDateAsync(
        string warehouseCode, string itemCode, Guid? ignoreKey = null) =>
        db.Context.WarehouseReconciliations
            .AsNoTracking()
            .Where(x => x.WarehouseCode == warehouseCode &&
                        x.ItemCode == itemCode &&
                        x.Status == WarehouseReconciliationStatus.Approved &&
                        x.Key != ignoreKey)
            .MaxAsync(x => (DateTime?)x.ReferenceDate);

    public Task<bool> HasOpenAsync(string warehouseCode, string itemCode, Guid? ignoreKey = null) =>
        db.Context.WarehouseReconciliations
            .AsNoTracking()
            .AnyAsync(x => x.WarehouseCode == warehouseCode &&
                           x.ItemCode == itemCode &&
                           x.Key != ignoreKey &&
                           (x.Status == WarehouseReconciliationStatus.Draft ||
                            x.Status == WarehouseReconciliationStatus.InApproval));

    private ApplicationException Fail(string key) => new(resource[key].Value);
}
```

- [ ] **Step 5: Implement the remaining services.** `WarehouseReconciliationsDescriptionService.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>Nomes desnormalizados para exibição. O parceiro é o próprio armazém.</summary>
public class WarehouseReconciliationsDescriptionService(
    IItemService items,
    IWarehouseService warehouses,
    IBusinessPartnerService partners)
{
    public async Task FillAsync(WarehouseReconciliation r)
    {
        r.CardCode = r.WarehouseCode;
        r.ItemName = (await items.GetByIdAsync(r.ItemCode))?.ItemName;
        r.WarehouseName = (await warehouses.GetByIdAsync(r.WarehouseCode))?.Name;
        r.CardName = (await partners.GetByIdAsync(r.CardCode))?.CardName;
    }
}
```

  `WarehouseReconciliationsGetService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationsGetService(IUnitOfWork db)
{
    public IQueryable<WarehouseReconciliation> QueryAll() =>
        db.Context.WarehouseReconciliations.AsNoTracking();

    // AsNoTracking é o que faz o PATCH funcionar (mesmo motivo de OwnershipTransfersGetService).
    public Task<WarehouseReconciliation?> GetByIdAsync(Guid key) =>
        db.Context.WarehouseReconciliations.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
}
```

  `WarehouseReconciliationsCreateService.cs`:

```csharp
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationsCreateService(
    IUnitOfWork db,
    DocNumberSequenceService numbers,
    WarehouseReconciliationsDescriptionService descriptions,
    WarehouseReconciliationsGuardService guard)
{
    public async Task ExecuteAsync(WarehouseReconciliation r, string userName)
    {
        // O cliente não escolhe status, parceiro nem carimbos de fluxo.
        r.Status = WarehouseReconciliationStatus.Draft;
        r.ReferenceDate = r.ReferenceDate.Date;
        r.SentAt = null;
        r.SentBy = null;
        r.ApprovedAt = null;
        r.ApprovedBy = null;
        r.CanceledAt = null;
        r.CanceledBy = null;
        r.ApprovalComments = null;
        r.CancellationReason = null;
        r.StorageTransactionKey = null;

        await guard.EnsureCanPersistAsync(r);

        r.DocNumberKey ??= await numbers.GetKeyByTransactionCode(TransactionCode.WarehouseReconciliation);
        r.Code = await numbers.GetDocNumber(r.DocNumberKey.Value);
        await descriptions.FillAsync(r);
        await guard.RefreshSnapshotAsync(r);

        r.CreatedAt = DateTime.Now;
        r.CreatedBy = userName;
        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;

        await db.Context.WarehouseReconciliations.AddAsync(r);
        await db.SaveChangesAsync();
    }
}
```

  `WarehouseReconciliationsUpdateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationsUpdateService(
    IUnitOfWork db,
    WarehouseReconciliationsDescriptionService descriptions,
    WarehouseReconciliationsGuardService guard,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, WarehouseReconciliation input, string userName)
    {
        var r = await db.Context.WarehouseReconciliations.FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        if (r.Status != WarehouseReconciliationStatus.Draft)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_DRAFT"].Value);

        // Atribuição explícita dos campos editáveis: um PATCH não reescreve status nem auditoria.
        r.BranchCode = input.BranchCode ?? r.BranchCode;
        r.WarehouseCode = input.WarehouseCode;
        r.ItemCode = input.ItemCode;
        r.UnitOfMeasureCode = input.UnitOfMeasureCode;
        r.ReferenceDate = input.ReferenceDate.Date;
        r.ReportedBalance = input.ReportedBalance;
        r.ReasonKey = input.ReasonKey;
        r.Comments = input.Comments;

        await guard.EnsureCanPersistAsync(r);
        await descriptions.FillAsync(r);
        await guard.RefreshSnapshotAsync(r);

        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
```

  `WarehouseReconciliationsGetBalancePreviewService.cs`:

```csharp
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>Prévia para o formulário: saldo do sistema até a data e as travas que se aplicariam.</summary>
public class WarehouseReconciliationsGetBalancePreviewService(
    IUnitOfWork db,
    IWarehouseComplementService complements,
    WarehouseReconciliationsGuardService guard)
{
    public async Task<WarehouseReconciliationBalancePreviewDto> ExecuteAsync(
        string warehouseCode, string itemCode, DateTime referenceDate) => new()
        {
            SystemBalance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
                db.Context, warehouseCode, itemCode, referenceDate),
            IsOwnWarehouse = (await complements.GetAsync(warehouseCode))?.IsOwn == true,
            LastApprovedReferenceDate = await guard.LastApprovedReferenceDateAsync(warehouseCode, itemCode),
            HasOpenReconciliation = await guard.HasOpenAsync(warehouseCode, itemCode),
        };
}
```

- [ ] **Step 6: Add the resource keys.** Add them before `</root>`.

  `Resource.pt-br.resx`:

```xml
    <data name="WAREHOUSE_RECONCILIATION_NOT_FOUND" xml:space="preserve">
        <value>Conferência de saldo de armazém não encontrada.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_REQUIRED_FIELDS" xml:space="preserve">
        <value>Informe filial, armazém, produto, unidade de medida e motivo.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_OWN_WAREHOUSE" xml:space="preserve">
        <value>A conferência de saldo só pode ser feita para armazém de terceiros.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_NEGATIVE_REPORTED_BALANCE" xml:space="preserve">
        <value>O saldo informado pelo armazém não pode ser negativo.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_FUTURE_DATE" xml:space="preserve">
        <value>A data de referência não pode ser futura.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_REASON_INACTIVE" xml:space="preserve">
        <value>Motivo inexistente ou inativo.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_DATE_BEFORE_LAST_APPROVED" xml:space="preserve">
        <value>A data de referência é anterior à da última conferência aprovada deste armazém e produto.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_ALREADY_OPEN" xml:space="preserve">
        <value>Já existe uma conferência em rascunho ou em aprovação para este armazém e produto.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_NOT_DRAFT" xml:space="preserve">
        <value>Somente conferências em rascunho podem ser alteradas ou enviadas para aprovação.</value>
    </data>
```

  `Resource.resx`:

```xml
    <data name="WAREHOUSE_RECONCILIATION_NOT_FOUND" xml:space="preserve">
        <value>Warehouse balance reconciliation not found.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_REQUIRED_FIELDS" xml:space="preserve">
        <value>Branch, warehouse, item, unit of measure and reason are required.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_OWN_WAREHOUSE" xml:space="preserve">
        <value>Balance reconciliation is only allowed for third-party warehouses.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_NEGATIVE_REPORTED_BALANCE" xml:space="preserve">
        <value>The balance reported by the warehouse cannot be negative.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_FUTURE_DATE" xml:space="preserve">
        <value>The reference date cannot be in the future.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_REASON_INACTIVE" xml:space="preserve">
        <value>Reason not found or inactive.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_DATE_BEFORE_LAST_APPROVED" xml:space="preserve">
        <value>The reference date is earlier than the last approved reconciliation for this warehouse and item.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_ALREADY_OPEN" xml:space="preserve">
        <value>There is already a draft or in-approval reconciliation for this warehouse and item.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_NOT_DRAFT" xml:space="preserve">
        <value>Only draft reconciliations can be changed or sent for approval.</value>
    </data>
```

- [ ] **Step 7: Run the tests and confirm they pass.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliations"`
Expected: PASS (15 new tests plus the 7 from Task 3).

- [ ] **Step 8: Stage the new files.**

```bash
git add SiagroB1.Application/Services/WarehouseReconciliations SiagroB1.Application.Tests/WarehouseReconciliations
```

---

### Task 5: Approval flow (send, withdraw, reject and approve with transaction generation)

**Files:**
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsSendApprovalService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsWithdrawApprovalService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsRejectService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsApprovalService.cs`
- Create: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationsTestContext.Approval.cs`
- Test: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationsApprovalFlowTests.cs`
- Modify: `Resource.resx`, `Resource.pt-br.resx`

**Interfaces:**
- Consumes:
  - `WarehouseReconciliationsGuardService` (Task 4)
  - `StorageTransactionsCreateService.ExecuteAsync(StorageTransaction, string, TransactionCode, CommitMode)`
  - `CalculateAsync` (Task 1)
  - `TransactionCode.WarehouseReconciliation` and `StorageTransactionType.WarehouseLoss`/`WarehouseGain` (Task 1)
- Produces:
  - `WarehouseReconciliationsSendApprovalService.ExecuteAsync(Guid key, string userName)`
  - `WarehouseReconciliationsWithdrawApprovalService.ExecuteAsync(Guid key, string userName)`
  - `WarehouseReconciliationsRejectService.ExecuteAsync(Guid key, string? comments, string userName)`
  - `WarehouseReconciliationsApprovalService.ExecuteAsync(Guid key, string? comments, string userName)`
  - Test context members `SendApproval()`, `Withdraw()`, `Reject()`, `Approval()`,
    `CreateApprovedAsync(decimal reported, DateTime referenceDate)` and `CurrentBalanceAsync()`
  - Resource keys `WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL`, `..._ZERO_DIFFERENCE` and
    `..._LOSS_EXCEEDS_BALANCE`

- [ ] **Step 1: Add the test factories.** `WarehouseReconciliationsTestContext.Approval.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public StorageTransactionsCreateService StorageCreate() =>
        new(Db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new() { [ThirdPartyWarehouse] = "Armazém Terceiro Ltda" }),
            new FakeItemService(new() { [Item] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { [ThirdPartyWarehouse] = "Armazém Terceiro" }),
            new ShipmentReleasesRecalculateShippedService(Db.Context),
            new ShipmentReleaseMovementGuardService(Db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    public WarehouseReconciliationsSendApprovalService SendApproval() => new(Db, Guard(), Resource);
    public WarehouseReconciliationsWithdrawApprovalService Withdraw() => new(Db, Resource);
    public WarehouseReconciliationsRejectService Reject() => new(Db, Resource);

    public WarehouseReconciliationsApprovalService Approval() =>
        new(Db, Guard(), StorageCreate(), Resource,
            NullLogger<WarehouseReconciliationsApprovalService>.Instance);

    public async Task<WarehouseReconciliation> CreateApprovedAsync(decimal reportedBalance, DateTime referenceDate)
    {
        var r = await CreateDraftAsync(reportedBalance, referenceDate);
        await SendApproval().ExecuteAsync(r.Key, "tester");
        await Approval().ExecuteAsync(r.Key, "ok", "approver");
        return r;
    }

    public Task<decimal> CurrentBalanceAsync() =>
        StorageTransactionsWarehouseBalanceService.CalculateAsync(Db.Context, ThirdPartyWarehouse, Item);
}
```

- [ ] **Step 2: Write the failing tests.** `WarehouseReconciliationsApprovalFlowTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsApprovalFlowTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;

    private Task SeedAsync(StorageTransactionType type, decimal quantity, DateTime date) =>
        _ctx.SeedStockAsync(type, quantity, date);

    [Fact]
    public async Task Send_moves_draft_to_in_approval_and_stamps_sender()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);

        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.InApproval, saved.Status);
        Assert.Equal("sender", saved.SentBy);
        Assert.NotNull(saved.SentAt);
    }

    [Fact]
    public async Task Send_recomputes_a_stale_snapshot()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));
        // Romaneio lançado depois do rascunho, mas com data anterior à referência.
        await SeedAsync(StorageTransactionType.Purchase, 50m, Today.AddDays(-20));

        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(1_050m, saved.SystemBalance);
        Assert.Equal(-150m, saved.Difference);
    }

    [Fact]
    public async Task Send_refuses_zero_difference()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(1_000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ZERO_DIFFERENCE", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.Draft, (await _ctx.ReloadAsync(r.Key)).Status);
    }

    [Fact]
    public async Task Send_refuses_non_draft()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Rejected, Today);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.SendApproval().ExecuteAsync(r.Key, "sender"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_DRAFT", ex.Message);
    }

    [Fact]
    public async Task Withdraw_returns_to_draft_and_clears_sender()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        await _ctx.Withdraw().ExecuteAsync(r.Key, "sender");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Draft, saved.Status);
        Assert.Null(saved.SentBy);
    }

    [Fact]
    public async Task Reject_is_final_and_keeps_comments()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");

        await _ctx.Reject().ExecuteAsync(r.Key, "extrato ilegível", "approver");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Rejected, saved.Status);
        Assert.Equal("extrato ilegível", saved.ApprovalComments);
        Assert.Null(saved.StorageTransactionKey);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Withdraw().ExecuteAsync(r.Key, "sender"));
        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL", ex.Message);
    }

    [Fact]
    public async Task Approving_a_loss_generates_a_confirmed_warehouse_loss_transaction()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var referenceDate = Today.AddDays(-5);

        var r = await _ctx.CreateApprovedAsync(880m, referenceDate);

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Approved, saved.Status);
        Assert.Equal("approver", saved.ApprovedBy);
        Assert.Equal("ok", saved.ApprovalComments);
        Assert.NotNull(saved.StorageTransactionKey);

        var transaction = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == saved.StorageTransactionKey);
        Assert.Equal(StorageTransactionType.WarehouseLoss, transaction.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, transaction.TransactionStatus);
        Assert.Equal(TransactionCode.WarehouseReconciliation, transaction.TransactionOrigin);
        Assert.Equal(120m, transaction.NetWeight);
        Assert.Equal(120m, transaction.GrossWeight);
        Assert.Equal(decimal.Zero, transaction.AvaiableVolumeToAllocate);
        Assert.Equal(referenceDate, transaction.TransactionDate);
        Assert.Null(transaction.StorageAddressCode);
        Assert.Equal(WarehouseReconciliationsTestContext.ThirdPartyWarehouse, transaction.CardCode);
        Assert.Equal(WarehouseReconciliationsTestContext.ThirdPartyWarehouse, transaction.WarehouseCode);

        Assert.Equal(880m, await _ctx.CurrentBalanceAsync());
    }

    [Fact]
    public async Task Approving_a_gain_generates_a_warehouse_gain_transaction()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));

        var r = await _ctx.CreateApprovedAsync(1_030m, Today);

        var saved = await _ctx.ReloadAsync(r.Key);
        var transaction = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == saved.StorageTransactionKey);
        Assert.Equal(StorageTransactionType.WarehouseGain, transaction.TransactionType);
        Assert.Equal(30m, transaction.NetWeight);
        Assert.Equal(1_030m, await _ctx.CurrentBalanceAsync());
    }

    [Fact]
    public async Task Approval_recomputes_the_snapshot_before_generating()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");
        await SeedAsync(StorageTransactionType.PurchaseReturn, 40m, Today.AddDays(-15));

        await _ctx.Approval().ExecuteAsync(r.Key, null, "approver");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(960m, saved.SystemBalance);
        Assert.Equal(-60m, saved.Difference);
    }

    [Fact]
    public async Task Approval_refuses_when_the_difference_became_zero()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m, Today.AddDays(-10));
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");
        await SeedAsync(StorageTransactionType.PurchaseReturn, 100m, Today.AddDays(-15));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Approval().ExecuteAsync(r.Key, null, "approver"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ZERO_DIFFERENCE", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.InApproval, (await _ctx.ReloadAsync(r.Key)).Status);
        Assert.False(await _ctx.Db.Context.StorageTransactions.AnyAsync(
            x => x.TransactionOrigin == TransactionCode.WarehouseReconciliation));
    }

    /// <summary>
    /// A perda apurada na data de referência não pode exceder o saldo ATUAL: 1.000 em estoque
    /// até a referência, 900 embarcados depois, perda de 300 deixaria o armazém em −200.
    /// </summary>
    [Fact]
    public async Task Approval_refuses_a_loss_bigger_than_the_current_balance()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(700m, Today.AddDays(-10));
        await _ctx.SendApproval().ExecuteAsync(r.Key, "sender");
        await SeedAsync(StorageTransactionType.SalesShipment, 900m, Today.AddDays(-2));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Approval().ExecuteAsync(r.Key, null, "approver"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_LOSS_EXCEEDS_BALANCE", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.InApproval, (await _ctx.ReloadAsync(r.Key)).Status);
    }

    [Fact]
    public async Task Approval_refuses_non_in_approval()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Draft, Today);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => _ctx.Approval().ExecuteAsync(r.Key, null, "approver"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL", ex.Message);
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationsApprovalFlowTests"`
Expected: build error, `'WarehouseReconciliationsSendApprovalService' could not be found`.

- [ ] **Step 4: Implement send, withdraw and reject.** `WarehouseReconciliationsSendApprovalService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationsSendApprovalService(
    IUnitOfWork db,
    WarehouseReconciliationsGuardService guard,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var r = await db.Context.WarehouseReconciliations.FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        if (r.Status != WarehouseReconciliationStatus.Draft)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_DRAFT"].Value);

        await guard.EnsureCanPersistAsync(r);

        // O aprovador vê números reais, não os do dia em que o rascunho foi digitado.
        await guard.RefreshSnapshotAsync(r);

        if (r.Difference == decimal.Zero)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_ZERO_DIFFERENCE"].Value);

        r.Status = WarehouseReconciliationStatus.InApproval;
        r.SentAt = DateTime.Now;
        r.SentBy = userName;
        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
```

  `WarehouseReconciliationsWithdrawApprovalService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationsWithdrawApprovalService(IUnitOfWork db, IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var r = await db.Context.WarehouseReconciliations.FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        if (r.Status != WarehouseReconciliationStatus.InApproval)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL"].Value);

        r.Status = WarehouseReconciliationStatus.Draft;
        r.SentAt = null;
        r.SentBy = null;
        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
```

  `WarehouseReconciliationsRejectService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationsRejectService(IUnitOfWork db, IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string? comments, string userName)
    {
        var r = await db.Context.WarehouseReconciliations.FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        if (r.Status != WarehouseReconciliationStatus.InApproval)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL"].Value);

        // Mesmo carimbo dos contratos rejeitados (PurchaseContractsRejectService): CanceledAt/By.
        r.Status = WarehouseReconciliationStatus.Rejected;
        r.ApprovalComments = comments;
        r.CanceledAt = DateTime.Now;
        r.CanceledBy = userName;
        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Implement approval.** `WarehouseReconciliationsApprovalService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>
/// Aprova a conferência e gera o romaneio de Perda(13)/Sobra(14) — já Confirmado, sem contrato,
/// sem lote, com a data de referência. É o ÚNICO caminho que cria esses tipos.
/// </summary>
public class WarehouseReconciliationsApprovalService(
    IUnitOfWork db,
    WarehouseReconciliationsGuardService guard,
    StorageTransactionsCreateService storageTransactionsCreateService,
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

        var quantity = Math.Abs(r.Difference);

        // Mesma regra da confirmação de embarque: o armazém nunca fica negativo HOJE, ainda que a
        // perda tenha sido apurada numa data passada.
        if (r.Difference < decimal.Zero)
        {
            var current = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
                db.Context, r.WarehouseCode, r.ItemCode);

            if (quantity > current)
                throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_LOSS_EXCEEDS_BALANCE"].Value);
        }

        try
        {
            await db.BeginTransactionAsync();

            var transaction = new StorageTransaction
            {
                TransactionDate = r.ReferenceDate,
                TransactionStatus = StorageTransactionsStatus.Confirmed,
                TransactionType = r.Difference < decimal.Zero
                    ? StorageTransactionType.WarehouseLoss
                    : StorageTransactionType.WarehouseGain,
                GrossWeight = quantity,
                NetWeight = quantity,
                AvaiableVolumeToAllocate = decimal.Zero,
                BranchCode = r.BranchCode,
                CardCode = r.CardCode ?? r.WarehouseCode,
                ItemCode = r.ItemCode,
                UnitOfMeasureCode = r.UnitOfMeasureCode,
                WarehouseCode = r.WarehouseCode,
                StorageAddressCode = null,
                Comments = $"Conferência de saldo de armazém {r.Code}",
            };

            await storageTransactionsCreateService.ExecuteAsync(
                transaction, userName, TransactionCode.WarehouseReconciliation, CommitMode.Deferred);

            r.StorageTransactionKey = transaction.Key;
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
    }
}
```

  Note: EF assigns the client-side GUID `transaction.Key` inside `AddAsync`, which runs within
  `StorageTransactionsCreateService`. The test `Approving_a_loss_...` asserts the link. If it fails
  with an empty key, save first and then assign the key.

- [ ] **Step 6: Add the resource keys.** Add them before `</root>`.

  `Resource.pt-br.resx`:

```xml
    <data name="WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL" xml:space="preserve">
        <value>Somente conferências em aprovação podem ser aprovadas, rejeitadas ou retiradas da aprovação.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_ZERO_DIFFERENCE" xml:space="preserve">
        <value>Não há diferença entre o saldo informado pelo armazém e o saldo do sistema na data de referência.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_LOSS_EXCEEDS_BALANCE" xml:space="preserve">
        <value>A perda apurada é maior que o saldo atual do armazém para o produto.</value>
    </data>
```

  `Resource.resx`:

```xml
    <data name="WAREHOUSE_RECONCILIATION_NOT_IN_APPROVAL" xml:space="preserve">
        <value>Only reconciliations in approval can be approved, rejected or withdrawn.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_ZERO_DIFFERENCE" xml:space="preserve">
        <value>There is no difference between the reported balance and the system balance at the reference date.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_LOSS_EXCEEDS_BALANCE" xml:space="preserve">
        <value>The loss is greater than the current warehouse balance for the item.</value>
    </data>
```

- [ ] **Step 7: Run the tests and confirm they pass.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliations"`
Expected: PASS, including the 12 new tests.

- [ ] **Step 8: Stage the new files.**

```bash
git add SiagroB1.Application/Services/WarehouseReconciliations SiagroB1.Application.Tests/WarehouseReconciliations
```

---

### Task 6: Cancellation

**Files:**
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationsCancelService.cs`
- Create: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationsTestContext.Cancel.cs`
- Test: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationsCancelServiceTests.cs`
- Modify: `Resource.resx`, `Resource.pt-br.resx`

**Interfaces:**
- Consumes:
  - `StorageTransactionsCancelService.ExecuteAsync(Guid key, string username, TransactionCode transactionCode)`
  - `CalculateAsync` (Task 1)
  - `CreateApprovedAsync` and `CurrentBalanceAsync` (Task 5)
- Produces:
  - `WarehouseReconciliationsCancelService.ExecuteAsync(Guid key, string? reason, string userName)`
  - Test context member `Cancel()`
  - Resource keys `WAREHOUSE_RECONCILIATION_CANCELLATION_REASON_REQUIRED`,
    `..._CANNOT_CANCEL`, `..._NOT_LATEST` and `..._GAIN_CANCEL_NEGATIVE`

- [ ] **Step 1: Add the test factory.** `WarehouseReconciliationsTestContext.Cancel.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.WarehouseReconciliations;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public WarehouseReconciliationsCancelService Cancel() =>
        new(Db,
            new StorageTransactionsCancelService(Db, new ShipmentReleasesRecalculateShippedService(Db.Context)),
            Resource,
            NullLogger<WarehouseReconciliationsCancelService>.Instance);
}
```

- [ ] **Step 2: Write the failing tests.** `WarehouseReconciliationsCancelServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationsCancelServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();
    private static readonly DateTime Today = DateTime.Today;

    private Task SeedAsync(StorageTransactionType type, decimal quantity, DateTime date) =>
        _ctx.SeedStockAsync(type, quantity, date);

    [Fact]
    public async Task Cancelling_a_draft_has_no_stock_effect()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateDraftAsync(900m);

        await _ctx.Cancel().ExecuteAsync(r.Key, "digitado errado", "tester");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Cancelled, saved.Status);
        Assert.Equal("digitado errado", saved.CancellationReason);
        Assert.Equal("tester", saved.CanceledBy);
        Assert.Equal(1_000m, await _ctx.CurrentBalanceAsync());
    }

    [Fact]
    public async Task Cancelling_an_approved_loss_cancels_its_transaction_and_restores_the_balance()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var r = await _ctx.CreateApprovedAsync(880m, Today.AddDays(-5));
        Assert.Equal(880m, await _ctx.CurrentBalanceAsync());

        await _ctx.Cancel().ExecuteAsync(r.Key, "armazém corrigiu o extrato", "tester");

        var saved = await _ctx.ReloadAsync(r.Key);
        Assert.Equal(WarehouseReconciliationStatus.Cancelled, saved.Status);
        var transaction = await _ctx.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(x => x.Key == saved.StorageTransactionKey);
        Assert.Equal(StorageTransactionsStatus.Cancelled, transaction.TransactionStatus);
        Assert.Equal(1_000m, await _ctx.CurrentBalanceAsync());
    }

    [Fact]
    public async Task Only_the_latest_approved_can_be_cancelled()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var older = await _ctx.CreateApprovedAsync(900m, Today.AddDays(-10));
        await _ctx.CreateApprovedAsync(950m, Today.AddDays(-5));

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(older.Key, "motivo", "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_NOT_LATEST", ex.Message);
        Assert.Equal(WarehouseReconciliationStatus.Approved, (await _ctx.ReloadAsync(older.Key)).Status);
    }

    /// <summary>
    /// A sobra de 200 já foi embarcada: 1.200 no armazém, 1.150 saíram. Cancelar a sobra
    /// deixaria o armazém em −150.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_gain_that_was_already_shipped_is_refused()
    {
        await SeedAsync(StorageTransactionType.Purchase, 1_000m, Today.AddDays(-30));
        var gain = await _ctx.CreateApprovedAsync(1_200m, Today.AddDays(-10));
        await SeedAsync(StorageTransactionType.SalesShipment, 1_150m, Today.AddDays(-1));

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => _ctx.Cancel().ExecuteAsync(gain.Key, "motivo", "tester"));

        Assert.Equal("WAREHOUSE_RECONCILIATION_GAIN_CANCEL_NEGATIVE", ex.Message);
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

- [ ] **Step 3: Run the tests and confirm they fail.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationsCancelServiceTests"`
Expected: build error, `'WarehouseReconciliationsCancelService' could not be found`.

- [ ] **Step 4: Implement the service.** `WarehouseReconciliationsCancelService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>
/// Rascunho cancela sem efeito. Aprovada cancela o romaneio de Perda/Sobra, e só se for a
/// ÚLTIMA aprovada do armazém+produto — desfazer uma do meio invalidaria o snapshot das seguintes.
/// </summary>
public class WarehouseReconciliationsCancelService(
    IUnitOfWork db,
    StorageTransactionsCancelService storageTransactionsCancelService,
    IStringLocalizer<Resource> resource,
    ILogger<WarehouseReconciliationsCancelService> logger)
{
    public async Task ExecuteAsync(Guid key, string? reason, string userName)
    {
        var r = await db.Context.WarehouseReconciliations.FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_CANCELLATION_REASON_REQUIRED"].Value);

        if (r.Status is not (WarehouseReconciliationStatus.Draft or WarehouseReconciliationStatus.Approved))
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_CANNOT_CANCEL"].Value);

        if (r.Status == WarehouseReconciliationStatus.Approved)
            await EnsureApprovedCanBeUndoneAsync(r.Key, r.WarehouseCode, r.ItemCode, r.Difference);

        try
        {
            await db.BeginTransactionAsync();

            if (r.Status == WarehouseReconciliationStatus.Approved && r.StorageTransactionKey is Guid transactionKey)
            {
                await storageTransactionsCancelService.ExecuteAsync(
                    transactionKey, userName, TransactionCode.WarehouseReconciliation);
            }

            r.Status = WarehouseReconciliationStatus.Cancelled;
            r.CancellationReason = reason.Trim();
            r.CanceledAt = DateTime.Now;
            r.CanceledBy = userName;
            r.UpdatedAt = DateTime.Now;
            r.UpdatedBy = userName;

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, e.Message);
            throw new DefaultException($"Erro ao cancelar a conferência de saldo: {e.Message}");
        }
    }

    private async Task EnsureApprovedCanBeUndoneAsync(
        Guid key, string warehouseCode, string itemCode, decimal difference)
    {
        var latestKey = await db.Context.WarehouseReconciliations
            .AsNoTracking()
            .Where(x => x.WarehouseCode == warehouseCode &&
                        x.ItemCode == itemCode &&
                        x.Status == WarehouseReconciliationStatus.Approved)
            .OrderByDescending(x => x.ReferenceDate)
            .ThenByDescending(x => x.ApprovedAt)
            .Select(x => x.Key)
            .FirstAsync();

        if (latestKey != key)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_LATEST"].Value);

        // Cancelar uma SOBRA tira grão do saldo: não pode deixar o armazém negativo hoje.
        if (difference > decimal.Zero)
        {
            var current = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
                db.Context, warehouseCode, itemCode);

            if (current - difference < decimal.Zero)
                throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_GAIN_CANCEL_NEGATIVE"].Value);
        }
    }
}
```

- [ ] **Step 5: Add the resource keys.** Add them before `</root>`.

  `Resource.pt-br.resx`:

```xml
    <data name="WAREHOUSE_RECONCILIATION_CANCELLATION_REASON_REQUIRED" xml:space="preserve">
        <value>Informe o motivo do cancelamento.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_CANNOT_CANCEL" xml:space="preserve">
        <value>Somente conferências em rascunho ou aprovadas podem ser canceladas.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_NOT_LATEST" xml:space="preserve">
        <value>Somente a última conferência aprovada deste armazém e produto pode ser cancelada.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_GAIN_CANCEL_NEGATIVE" xml:space="preserve">
        <value>Cancelar esta sobra deixaria o saldo do armazém negativo.</value>
    </data>
```

  `Resource.resx`:

```xml
    <data name="WAREHOUSE_RECONCILIATION_CANCELLATION_REASON_REQUIRED" xml:space="preserve">
        <value>A cancellation reason is required.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_CANNOT_CANCEL" xml:space="preserve">
        <value>Only draft or approved reconciliations can be cancelled.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_NOT_LATEST" xml:space="preserve">
        <value>Only the latest approved reconciliation for this warehouse and item can be cancelled.</value>
    </data>
    <data name="WAREHOUSE_RECONCILIATION_GAIN_CANCEL_NEGATIVE" xml:space="preserve">
        <value>Cancelling this gain would make the warehouse balance negative.</value>
    </data>
```

- [ ] **Step 6: Run the tests and confirm they pass.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliations"`
Expected: PASS, including the 9 new test cases.

- [ ] **Step 7: Stage the new files.**

```bash
git add SiagroB1.Application/Services/WarehouseReconciliations SiagroB1.Application.Tests/WarehouseReconciliations
```

---

### Task 7: Attachments

**Files:**
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationAttachmentsCreateService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationAttachmentsGetService.cs`
- Create: `SiagroB1.Application/Services/WarehouseReconciliations/WarehouseReconciliationAttachmentsDeleteService.cs`
- Test: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationAttachmentsServiceTests.cs`

**Interfaces:**
- Consumes: `WarehouseReconciliationAttachment` and `WarehouseReconciliationAttachmentDto` (Task 2).
- Produces:
  - `WarehouseReconciliationAttachmentsCreateService.SaveAsync(Guid reconciliationKey, WarehouseReconciliationAttachment attachment)`
  - `WarehouseReconciliationAttachmentsGetService.ListByReconciliation(Guid reconciliationKey)` → `IEnumerable<WarehouseReconciliationAttachmentDto>`
  - `WarehouseReconciliationAttachmentsGetService.GetByKey(Guid key)` → `Task<WarehouseReconciliationAttachment?>`
  - `WarehouseReconciliationAttachmentsDeleteService.Delete(Guid key)`

- [ ] **Step 1: Write the failing tests.** `WarehouseReconciliationAttachmentsServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationAttachmentsServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();

    private static WarehouseReconciliationAttachment NewAttachment() => new()
    {
        Description = "Extrato do armazém",
        FileName = "extrato.pdf",
        ContentType = "application/pdf",
        FileData = [1, 2, 3],
        CreatedBy = "tester",
    };

    [Fact]
    public async Task Save_list_download_and_delete()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Draft, DateTime.Today);
        var create = new WarehouseReconciliationAttachmentsCreateService(_ctx.Db);
        var get = new WarehouseReconciliationAttachmentsGetService(_ctx.Db);
        var delete = new WarehouseReconciliationAttachmentsDeleteService(_ctx.Db);

        await create.SaveAsync(r.Key, NewAttachment());

        var listed = Assert.Single(get.ListByReconciliation(r.Key));
        Assert.Equal("extrato.pdf", listed.FileName);
        var file = await get.GetByKey(listed.Key!.Value);
        Assert.Equal(new byte[] { 1, 2, 3 }, file!.FileData);

        await delete.Delete(listed.Key!.Value);
        Assert.Empty(get.ListByReconciliation(r.Key));
    }

    [Fact]
    public async Task Save_refuses_unknown_reconciliation()
    {
        var create = new WarehouseReconciliationAttachmentsCreateService(_ctx.Db);

        await Assert.ThrowsAsync<NotFoundException>(() => create.SaveAsync(Guid.NewGuid(), NewAttachment()));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationAttachmentsServiceTests"`
Expected: build error, the service types are not found.

- [ ] **Step 3: Implement the services.** They are simpler copies of `PurchaseContractsAttachments*Service`.

  `WarehouseReconciliationAttachmentsCreateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationAttachmentsCreateService(IUnitOfWork db)
{
    public async Task SaveAsync(Guid reconciliationKey, WarehouseReconciliationAttachment attachment)
    {
        if (!await db.Context.WarehouseReconciliations.AnyAsync(x => x.Key == reconciliationKey))
            throw new NotFoundException("Conferência de saldo não encontrada.");

        attachment.WarehouseReconciliationKey = reconciliationKey;
        attachment.CreatedAt ??= DateTime.Now;

        await db.Context.WarehouseReconciliationAttachments.AddAsync(attachment);
        await db.SaveChangesAsync();
    }
}
```

  `WarehouseReconciliationAttachmentsGetService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationAttachmentsGetService(IUnitOfWork db)
{
    // Projeção sem FileData: a lista não arrasta o binário.
    public IEnumerable<WarehouseReconciliationAttachmentDto> ListByReconciliation(Guid reconciliationKey) =>
        db.Context.WarehouseReconciliationAttachments
            .AsNoTracking()
            .Where(x => x.WarehouseReconciliationKey == reconciliationKey)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new WarehouseReconciliationAttachmentDto
            {
                Key = x.Key,
                Description = x.Description,
                FileName = x.FileName,
                CreatedBy = x.CreatedBy,
                CreatedAt = x.CreatedAt,
            })
            .ToList();

    public Task<WarehouseReconciliationAttachment?> GetByKey(Guid key) =>
        db.Context.WarehouseReconciliationAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
}
```

  `WarehouseReconciliationAttachmentsDeleteService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public class WarehouseReconciliationAttachmentsDeleteService(IUnitOfWork db)
{
    public async Task Delete(Guid key)
    {
        var attachment = await db.Context.WarehouseReconciliationAttachments.FirstOrDefaultAsync(x => x.Key == key)
                         ?? throw new NotFoundException("Anexo não encontrado.");

        db.Context.WarehouseReconciliationAttachments.Remove(attachment);
        await db.SaveChangesAsync();
    }
}
```

  Remove the unused `using Microsoft.Extensions.Logging.Abstractions;` from the test file if the
  build warns about it.

- [ ] **Step 4: Run the tests and confirm they pass.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationAttachmentsServiceTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Stage the new files.**

```bash
git add SiagroB1.Application/Services/WarehouseReconciliations SiagroB1.Application.Tests/WarehouseReconciliations
```

---

### Task 8: OData endpoints and DI wiring

**Files:**
- Create: `SiagroB1.Web/Controllers/WarehouseReconciliationsController.cs`
- Create: `SiagroB1.Web/Controllers/WarehouseReconciliationReasonsController.cs`
- Create: `SiagroB1.Web/Controllers/WarehouseReconciliationAttachmentsController.cs`
- Create: `SiagroB1.Web/Actions/WarehouseReconciliations/WarehouseReconciliationsSendApprovalController.cs`
- Create: `SiagroB1.Web/Actions/WarehouseReconciliations/WarehouseReconciliationsWithdrawApprovalController.cs`
- Create: `SiagroB1.Web/Actions/WarehouseReconciliations/WarehouseReconciliationsApprovalController.cs`
- Create: `SiagroB1.Web/Actions/WarehouseReconciliations/WarehouseReconciliationsRejectController.cs`
- Create: `SiagroB1.Web/Actions/WarehouseReconciliations/WarehouseReconciliationsCancelController.cs`
- Create: `SiagroB1.Web/Actions/WarehouseReconciliations/WarehouseReconciliationsAttachmentUploadController.cs`
- Create: `SiagroB1.Web/Functions/WarehouseReconciliations/WarehouseReconciliationsGetBalancePreviewController.cs`
- Create: `SiagroB1.Web/Functions/WarehouseReconciliations/WarehouseReconciliationsAttachmentsListController.cs`
- Create: `SiagroB1.Web/Functions/WarehouseReconciliations/WarehouseReconciliationsAttachmentsDownloadController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, adding entity sets near line 202 (after `OwnershipTransfers`) and operations after the `OwnershipTransfersListStorageAddressesBalanceByProduct` function (~line 896)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, adding the block after line ~485
- Test: `SiagroB1.Application.Tests/WarehouseReconciliations/WarehouseReconciliationEdmModelTests.cs`

**Interfaces:**
- Consumes: every service from Tasks 3-7.
- Produces the HTTP contract for the frontend plan:

| Verb | Route | Body / params |
|---|---|---|
| GET/POST | `odata/WarehouseReconciliations` | entity |
| GET/PATCH/PUT | `odata/WarehouseReconciliations({key})` | entity. DELETE → 400 |
| GET/POST | `odata/WarehouseReconciliationReasons` | entity |
| GET/PATCH/DELETE | `odata/WarehouseReconciliationReasons({key})` | entity |
| DELETE | `odata/WarehouseReconciliationAttachments({key})` | — |
| POST | `odata/WarehouseReconciliationsSendApproval` | `{ Key }` |
| POST | `odata/WarehouseReconciliationsWithdrawApproval` | `{ Key }` |
| POST | `odata/WarehouseReconciliationsApproval` | `{ Key, Comments }` |
| POST | `odata/WarehouseReconciliationsReject` | `{ Key, Comments }` |
| POST | `odata/WarehouseReconciliationsCancel` | `{ Key, Reason }` |
| POST | `odata/WarehouseReconciliationsAttachmentUpload` | `{ ReconciliationKey, Description, File (base64), FileName, ContentType }` |
| GET | `odata/WarehouseReconciliationsGetBalancePreview(WarehouseCode='..',ItemCode='..',ReferenceDate='yyyy-MM-dd')` | → preview DTO (**camelCase** JSON) |
| GET | `odata/WarehouseReconciliationsAttachmentsList(ReconciliationKey={key})` | → DTO list |
| GET | `odata/WarehouseReconciliationsAttachmentsDownload(Key={key})` | → file |

  Business errors return `400` with a plain **string** body. `NotFoundException` returns `404`.

- [ ] **Step 1: Write the failing EDM test.** `WarehouseReconciliationEdmModelTests.cs`:

```csharp
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("WarehouseReconciliations")]
    [InlineData("WarehouseReconciliationReasons")]
    [InlineData("WarehouseReconciliationAttachments")]
    public void The_entity_sets_are_exposed(string entitySet)
    {
        Assert.NotNull(BuildModel().EntityContainer.FindEntitySet(entitySet));
    }

    [Theory]
    [InlineData("WarehouseReconciliationsSendApproval", "Key")]
    [InlineData("WarehouseReconciliationsWithdrawApproval", "Key")]
    [InlineData("WarehouseReconciliationsApproval", "Key,Comments")]
    [InlineData("WarehouseReconciliationsReject", "Key,Comments")]
    [InlineData("WarehouseReconciliationsCancel", "Key,Reason")]
    [InlineData("WarehouseReconciliationsAttachmentUpload", "ReconciliationKey,Description,File,FileName,ContentType")]
    public void The_actions_declare_their_parameters(string action, string parameters)
    {
        var edmAction = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == action);

        foreach (var parameter in parameters.Split(','))
            Assert.Contains(edmAction.Parameters, p => p.Name == parameter);
    }

    [Theory]
    [InlineData("WarehouseReconciliationsGetBalancePreview", "WarehouseCode,ItemCode,ReferenceDate")]
    [InlineData("WarehouseReconciliationsAttachmentsList", "ReconciliationKey")]
    [InlineData("WarehouseReconciliationsAttachmentsDownload", "Key")]
    public void The_functions_declare_their_parameters(string function, string parameters)
    {
        var edmFunction = BuildModel().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == function);

        foreach (var parameter in parameters.Split(','))
            Assert.Contains(edmFunction.Parameters, p => p.Name == parameter);
    }

    [Fact]
    public void Reference_date_is_a_string_parameter()
    {
        var function = BuildModel().SchemaElements.OfType<IEdmFunction>()
            .Single(f => f.Name == "WarehouseReconciliationsGetBalancePreview");

        Assert.Equal("Edm.String", function.Parameters.Single(p => p.Name == "ReferenceDate").Type.FullName());
    }
}
```

- [ ] **Step 2: Run the test and confirm it fails.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationEdmModelTests"`
Expected: FAIL, because `FindEntitySet` returns null and `Single` throws.

- [ ] **Step 3: Register the EDM.** In `ODataConfigurations.cs`, add after
  `modelBuilder.EntitySet<OwnershipTransfer>("OwnershipTransfers");`:

```csharp
        modelBuilder.EntitySet<WarehouseReconciliation>("WarehouseReconciliations");
        modelBuilder.EntitySet<WarehouseReconciliationReason>("WarehouseReconciliationReasons");
        modelBuilder.EntitySet<WarehouseReconciliationAttachment>("WarehouseReconciliationAttachments");
```

  Add after the `ownershipTransfersListStorageAddressesBalanceByProduct` block:

```csharp
        // Conferência de Saldo de Armazém (GAC-1164)
        var warehouseReconciliationsSendApproval = modelBuilder.Action("WarehouseReconciliationsSendApproval");
        warehouseReconciliationsSendApproval.Parameter<Guid>("Key");
        warehouseReconciliationsSendApproval.Returns<IActionResult>();

        var warehouseReconciliationsWithdrawApproval = modelBuilder.Action("WarehouseReconciliationsWithdrawApproval");
        warehouseReconciliationsWithdrawApproval.Parameter<Guid>("Key");
        warehouseReconciliationsWithdrawApproval.Returns<IActionResult>();

        var warehouseReconciliationsApproval = modelBuilder.Action("WarehouseReconciliationsApproval");
        warehouseReconciliationsApproval.Parameter<Guid>("Key");
        warehouseReconciliationsApproval.Parameter<string>("Comments");
        warehouseReconciliationsApproval.Returns<IActionResult>();

        var warehouseReconciliationsReject = modelBuilder.Action("WarehouseReconciliationsReject");
        warehouseReconciliationsReject.Parameter<Guid>("Key");
        warehouseReconciliationsReject.Parameter<string>("Comments");
        warehouseReconciliationsReject.Returns<IActionResult>();

        var warehouseReconciliationsCancel = modelBuilder.Action("WarehouseReconciliationsCancel");
        warehouseReconciliationsCancel.Parameter<Guid>("Key");
        warehouseReconciliationsCancel.Parameter<string>("Reason");
        warehouseReconciliationsCancel.Returns<IActionResult>();

        var warehouseReconciliationsAttachmentUpload = modelBuilder.Action("WarehouseReconciliationsAttachmentUpload");
        warehouseReconciliationsAttachmentUpload.Parameter<Guid>("ReconciliationKey");
        warehouseReconciliationsAttachmentUpload.Parameter<string>("Description");
        warehouseReconciliationsAttachmentUpload.Parameter<string>("File");
        warehouseReconciliationsAttachmentUpload.Parameter<string>("FileName");
        warehouseReconciliationsAttachmentUpload.Parameter<string>("ContentType");
        warehouseReconciliationsAttachmentUpload.Returns<IActionResult>();

        var warehouseReconciliationsGetBalancePreview = modelBuilder.Function("WarehouseReconciliationsGetBalancePreview");
        warehouseReconciliationsGetBalancePreview.Parameter<string>("WarehouseCode");
        warehouseReconciliationsGetBalancePreview.Parameter<string>("ItemCode");
        warehouseReconciliationsGetBalancePreview.Parameter<string>("ReferenceDate");
        warehouseReconciliationsGetBalancePreview.Returns<IActionResult>();

        var warehouseReconciliationsAttachmentsList = modelBuilder.Function("WarehouseReconciliationsAttachmentsList");
        warehouseReconciliationsAttachmentsList.Parameter<Guid>("ReconciliationKey");
        warehouseReconciliationsAttachmentsList.Returns<IActionResult>();

        var warehouseReconciliationsAttachmentsDownload = modelBuilder.Function("WarehouseReconciliationsAttachmentsDownload");
        warehouseReconciliationsAttachmentsDownload.Parameter<Guid>("Key");
        warehouseReconciliationsAttachmentsDownload.Returns<IActionResult>();
```

- [ ] **Step 4: Run the EDM test and confirm it passes.**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WarehouseReconciliationEdmModelTests"`
Expected: PASS (13 cases).

- [ ] **Step 5: Register DI.** In `ServiceCollectionExtensions.cs`, add
  `using SiagroB1.Application.Services.WarehouseReconciliations;` and, after
  `services.AddScoped<OwnershipTransfersListStorageAddressesBalanceByProductService>();`:

```csharp
        // warehouse reconciliations (GAC-1164)
        services.AddScoped<WarehouseReconciliationsGuardService>();
        services.AddScoped<WarehouseReconciliationsDescriptionService>();
        services.AddScoped<WarehouseReconciliationsGetService>();
        services.AddScoped<WarehouseReconciliationsCreateService>();
        services.AddScoped<WarehouseReconciliationsUpdateService>();
        services.AddScoped<WarehouseReconciliationsGetBalancePreviewService>();
        services.AddScoped<WarehouseReconciliationsSendApprovalService>();
        services.AddScoped<WarehouseReconciliationsWithdrawApprovalService>();
        services.AddScoped<WarehouseReconciliationsRejectService>();
        services.AddScoped<WarehouseReconciliationsApprovalService>();
        services.AddScoped<WarehouseReconciliationsCancelService>();
        services.AddScoped<WarehouseReconciliationReasonsGetService>();
        services.AddScoped<WarehouseReconciliationReasonsCreateService>();
        services.AddScoped<WarehouseReconciliationReasonsUpdateService>();
        services.AddScoped<WarehouseReconciliationReasonsDeleteService>();
        services.AddScoped<WarehouseReconciliationAttachmentsCreateService>();
        services.AddScoped<WarehouseReconciliationAttachmentsGetService>();
        services.AddScoped<WarehouseReconciliationAttachmentsDeleteService>();
```

- [ ] **Step 6: Create the entity controllers.** `WarehouseReconciliationsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class WarehouseReconciliationsController(
    WarehouseReconciliationsCreateService createService,
    WarehouseReconciliationsUpdateService updateService,
    WarehouseReconciliationsGetService getService)
    : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<WarehouseReconciliation>> Get() => Ok(getService.QueryAll());

    [EnableQuery]
    public async Task<ActionResult<WarehouseReconciliation>> Get([FromRoute] Guid key)
    {
        var item = await getService.GetByIdAsync(key);
        return item == null ? NotFound() : Ok(item);
    }

    public async Task<IActionResult> Post([FromBody] WarehouseReconciliation entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await createService.ExecuteAsync(entity, User.Identity?.Name ?? "Unknown");
            return Created(entity);
        }
        catch (Exception ex) when (ex is ApplicationException or DefaultException)
        {
            return BadRequest(ex.Message);
        }
    }

    public Task<IActionResult> Put([FromRoute] Guid key, [FromBody] WarehouseReconciliation entity) =>
        UpdateAsync(key, entity);

    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<WarehouseReconciliation> patch)
    {
        var current = await getService.GetByIdAsync(key);
        if (current == null)
            return NotFound();

        patch.Patch(current);
        return await UpdateAsync(key, current);
    }

    public IActionResult Delete([FromRoute] Guid key) =>
        BadRequest("Não é possível excluir uma conferência de saldo. Efetue o cancelamento.");

    private async Task<IActionResult> UpdateAsync(Guid key, WarehouseReconciliation entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await updateService.ExecuteAsync(key, entity, User.Identity?.Name ?? "Unknown");
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is ApplicationException or DefaultException)
        {
            return BadRequest(ex.Message);
        }
    }
}
```

  `WarehouseReconciliationReasonsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class WarehouseReconciliationReasonsController(
    WarehouseReconciliationReasonsCreateService createService,
    WarehouseReconciliationReasonsUpdateService updateService,
    WarehouseReconciliationReasonsDeleteService deleteService,
    WarehouseReconciliationReasonsGetService getService)
    : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<WarehouseReconciliationReason>> Get() => Ok(getService.QueryAll());

    [EnableQuery]
    public async Task<ActionResult<WarehouseReconciliationReason>> Get([FromRoute] Guid key)
    {
        var item = await getService.GetByIdAsync(key);
        return item == null ? NotFound() : Ok(item);
    }

    public async Task<IActionResult> Post([FromBody] WarehouseReconciliationReason entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await createService.ExecuteAsync(entity, User.Identity?.Name ?? "Unknown");
            return Created(entity);
        }
        catch (ApplicationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<WarehouseReconciliationReason> patch)
    {
        var current = await getService.GetByIdAsync(key);
        if (current == null)
            return NotFound();

        patch.Patch(current);

        try
        {
            await updateService.ExecuteAsync(key, current, User.Identity?.Name ?? "Unknown");
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ApplicationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        try
        {
            await deleteService.ExecuteAsync(key);
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ApplicationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
```

  `WarehouseReconciliationAttachmentsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class WarehouseReconciliationAttachmentsController(
    WarehouseReconciliationAttachmentsDeleteService service) : ODataController
{
    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        try
        {
            await service.Delete(key);
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }
}
```

- [ ] **Step 7: Create the action controllers.** Every one follows the same shape. The
  `parameters is null` check comes first, because the OData binder hands over NULL when a declared
  parameter is missing.

  `WarehouseReconciliationsSendApprovalController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WarehouseReconciliations;

public class WarehouseReconciliationsSendApprovalController(
    WarehouseReconciliationsSendApprovalService service) : ODataController
{
    [HttpPost("odata/WarehouseReconciliationsSendApproval")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
            return BadRequest("Key é obrigatório.");

        try
        {
            await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!), User.Identity?.Name ?? "Unknown");
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

  `WarehouseReconciliationsWithdrawApprovalController.cs` is the same file with these changes:
  - class `WarehouseReconciliationsWithdrawApprovalController`;
  - constructor parameter `WarehouseReconciliationsWithdrawApprovalService service`;
  - route `"odata/WarehouseReconciliationsWithdrawApproval"`.

  `WarehouseReconciliationsApprovalController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WarehouseReconciliations;

public class WarehouseReconciliationsApprovalController(
    WarehouseReconciliationsApprovalService service) : ODataController
{
    [HttpPost("odata/WarehouseReconciliationsApproval")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
            return BadRequest("Key é obrigatório.");

        // String OData é anulável: TryGetValue devolve true com null.
        parameters.TryGetValue("Comments", out var commentsObj);

        try
        {
            await service.ExecuteAsync(
                Guid.Parse(keyObj.ToString()!), commentsObj?.ToString(), User.Identity?.Name ?? "Unknown");
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

  `WarehouseReconciliationsRejectController.cs` is the same file with these changes:
  - class `WarehouseReconciliationsRejectController`;
  - service `WarehouseReconciliationsRejectService`;
  - route `"odata/WarehouseReconciliationsReject"`.

  `WarehouseReconciliationsCancelController.cs` is the Approval file with these changes:
  - class `WarehouseReconciliationsCancelController`;
  - service `WarehouseReconciliationsCancelService`;
  - route `"odata/WarehouseReconciliationsCancel"`;
  - parameter name `"Reason"` instead of `"Comments"` (variable `reasonObj`).

  `WarehouseReconciliationsAttachmentUploadController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WarehouseReconciliations;

public class WarehouseReconciliationsAttachmentUploadController(
    WarehouseReconciliationAttachmentsCreateService service) : ODataController
{
    [HttpPost("odata/WarehouseReconciliationsAttachmentUpload")]
    public async Task<ActionResult> Upload([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null ||
            !parameters.TryGetValue("ReconciliationKey", out var keyObj) || keyObj is null ||
            !parameters.TryGetValue("File", out var fileObj) || fileObj is null ||
            !parameters.TryGetValue("Description", out var descriptionObj) || descriptionObj is null)
            return BadRequest("ReconciliationKey, File e Description são obrigatórios.");

        parameters.TryGetValue("FileName", out var fileNameObj);
        parameters.TryGetValue("ContentType", out var contentTypeObj);

        try
        {
            await service.SaveAsync((Guid)keyObj, new WarehouseReconciliationAttachment
            {
                Description = descriptionObj.ToString()!,
                FileName = fileNameObj?.ToString() ?? "anexo",
                ContentType = contentTypeObj?.ToString() ?? "application/octet-stream",
                FileData = Convert.FromBase64String(fileObj.ToString()!),
                CreatedAt = DateTime.Now,
                CreatedBy = User.Identity?.Name ?? "unknown",
            });
            return Ok();
        }
        catch (NotFoundException e)
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

- [ ] **Step 8: Create the function controllers.** `WarehouseReconciliationsGetBalancePreviewController.cs`:

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;

namespace SiagroB1.Web.Functions.WarehouseReconciliations;

public class WarehouseReconciliationsGetBalancePreviewController(
    WarehouseReconciliationsGetBalancePreviewService service) : ODataController
{
    [HttpGet("odata/WarehouseReconciliationsGetBalancePreview(WarehouseCode={warehouseCode},ItemCode={itemCode},ReferenceDate={referenceDate})")]
    public async Task<ActionResult> Get([FromRoute] string warehouseCode, [FromRoute] string itemCode, [FromRoute] string referenceDate)
    {
        // A data vai como string 'yyyy-MM-dd': Date/DateTimeOffset em URL de função é frágil no UI5 v4.
        if (!DateTime.TryParseExact(referenceDate.Trim('\''), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            return BadRequest("ReferenceDate deve estar no formato yyyy-MM-dd.");

        return Ok(await service.ExecuteAsync(warehouseCode.Trim('\''), itemCode.Trim('\''), date));
    }
}
```

  `WarehouseReconciliationsAttachmentsListController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;

namespace SiagroB1.Web.Functions.WarehouseReconciliations;

public class WarehouseReconciliationsAttachmentsListController(
    WarehouseReconciliationAttachmentsGetService service) : ODataController
{
    [HttpGet("odata/WarehouseReconciliationsAttachmentsList(ReconciliationKey={key})")]
    public ActionResult List([FromRoute] Guid key) => Ok(service.ListByReconciliation(key));
}
```

  `WarehouseReconciliationsAttachmentsDownloadController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;

namespace SiagroB1.Web.Functions.WarehouseReconciliations;

public class WarehouseReconciliationsAttachmentsDownloadController(
    WarehouseReconciliationAttachmentsGetService service) : ODataController
{
    [HttpGet("odata/WarehouseReconciliationsAttachmentsDownload(Key={key})")]
    public async Task<ActionResult> Get([FromRoute] Guid key)
    {
        var attachment = await service.GetByKey(key);
        return attachment == null
            ? NotFound("Anexo não encontrado.")
            : File(attachment.FileData, attachment.ContentType, attachment.FileName);
    }
}
```

- [ ] **Step 9: Build and run the full test suite.**

Run: `dotnet build SiagroB1.sln` and then `dotnet test SiagroB1.Application.Tests`
Expected: build succeeded and every test passes. The suite should end with roughly 60 more tests
than before.

- [ ] **Step 10: Smoke test that the Web app starts.** The migrations from Task 2 are already
  applied, so the app should start.

Run: `dotnet run --project SiagroB1.Web --launch-profile yktb` in the background.
Expected: the log reaches `Now listening on: http://localhost:50000` with no `PendingMigrationsGuard`
refusal and no DI activation error. Stop the process afterwards. The end-to-end browser check
belongs to the frontend plan.

- [ ] **Step 11: Stage the new files.**

```bash
git add SiagroB1.Web/Controllers/WarehouseReconciliationsController.cs SiagroB1.Web/Controllers/WarehouseReconciliationReasonsController.cs SiagroB1.Web/Controllers/WarehouseReconciliationAttachmentsController.cs SiagroB1.Web/Actions/WarehouseReconciliations SiagroB1.Web/Functions/WarehouseReconciliations SiagroB1.Application.Tests/WarehouseReconciliations
```

---

## Final verification (backend)

- [ ] `dotnet build SiagroB1.sln`: 0 errors.
- [ ] `dotnet test SiagroB1.Application.Tests`: all tests pass. Report the count.
- [ ] `dotnet ef migrations has-pending-model-changes --context AppDbContext ...`: "No changes".
- [ ] `git -C siagro-b1-backend status --short`: every new file shows `A`, and nothing is committed.
- [ ] The frontend plan, `siagro-b1-frontend/docs/superpowers/plans/2026-09-14-warehouse-reconciliation-frontend.md`,
  covers the screens and the browser verification script from the spec.
