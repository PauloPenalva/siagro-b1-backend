# Washout no Contrato de Compra — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Register a washout as a child event of a purchase contract (GAC-1164 case 2). Once
approved, it takes the washed volume out of the contract balances, adjusts the fixation's provisional
payable in place, and generates a firm receivable from the producer.

**Architecture:**
- **Entity.** `PurchaseContractWashout` is a child of `PurchaseContract`, modeled on
  `PurchaseContractPriceFixation`. It has one service per operation: Create, Approval, Reject and
  Reverse.
- **Persisted aggregates.** `PurchaseContract.WashedOutVolume` and `WashedOutUnfixedVolume` are
  persisted and derived, recalculated only by `PurchaseContractsWashedOutVolumeService`. The
  computed balances subtract them.
- **Provisional payable.** It is adjusted in place (`FinancialDocumentsAdjustProvisionalService`)
  instead of being cancelled and regenerated.
- **Receivable.** A new enqueue-only generator creates it. Every Dapper doc-number call runs
  BEFORE `BeginTransactionAsync`.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory in tests), ASP.NET Core OData 8, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-14-purchase-contract-washout-design.md` (read it first; §5.6 lists the guards added to existing services).

## Global Constraints

- **Language:** code identifiers, tables and columns are English. User-facing messages are pt-BR.
  - Most messages are inline `ApplicationException` strings, as the fixation services already do.
  - The one exception is `PurchaseContractsWithdrawApprovalService`, which already uses `.resx`
    keys. Add its new key to both `Resource.pt-br.resx` and `Resource.resx`.
- **Git:** stage every new file with `git add` immediately. **Never commit or push.** The
  "Stage" steps are `git add` only.
- **Ordering inside services:**
  1. Run business guards BEFORE `BeginTransactionAsync`.
  2. Call anything that may reach `DocNumberSequenceService` (the provisional generator, the adjust
     service, the receivable generator) BEFORE `BeginTransactionAsync`. It runs Dapper on a separate
     connection holding `UPDLOCK`.
  3. Save the status BEFORE `RecalculateAsync`. Recalculation reads the database and does not see
     changes that are only tracked.
- **Enqueue-only financial services:** they `Add`/mutate and never `SaveChanges`. The caller saves.
- **Status sets:**
  - `InApproval` and `Approved` washouts are ACTIVE: they reserve volume.
  - Only `Approved` washouts reduce the fixation's provisional.
- **Amount:** `round(max(MarketPrice − ContractPrice, 0) × FixedVolume, 2) + round(PenaltyAmount, 2)`.
- **Migrations:**
  - Generate with `$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'`.
  - Apply ONLY with that explicit environment (localhost `IDX_SIAGRO_DEV` / `IDX_SIAGRO_COMMON`), after
    confirming with `migrations list`.
  - Never use the `db-migration` profile.
- **Build:** stop any `SiagroB1.Web` process YOU started before `dotnet build`/`dotnet test`, because
  it locks the DLLs. Never touch port 8080 (another app of the user).
- **Test command** (from `siagro-b1-backend/`):
  `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Name>"`.
- **OData:**
  - A GET by key returns `SingleResult<T>` over an `IQueryable`; that is how `$expand` works.
  - Navigation routes are declared by hand with attributes.
  - `ODataActionParameters` can be null, and a string parameter can be null.

## Rulings made while writing this plan

1. **Provisional adjustment.**
   - The provisional is adjusted in place, with a `NetAmount` change log entry.
   - When the remaining volume is zero, it is cancelled.
   - When no provisional is open, one is generated with the explicit remaining volume.
   - **Why:** this avoids the cancel-then-insert ordering against the filtered unique index, and
     avoids Dapper inside the transaction.
2. **Generator override.** The provisional generator takes an optional `remainingVolume`.
   - The approval passes it because the washout being approved is not saved yet.
   - Every other caller lets the generator subtract the approved washouts it reads from the
     database.
3. **Unfixed-only washouts** store `PriceFixationKey = null` and `ContractPrice = 0`. That way they
   never block a fixation reversal.
4. **Settled receivable.** A receivable counts as settled when `SettledAmount != 0`. That is the
   same test `FinancialDocumentsCancelService.ExecuteAsync` uses: a settlement followed by its
   reversal nets to zero.
5. **Display code.** The "WO-{Sequence}" code is formatted in the frontend; there is no `[NotMapped]`
   property.

---

## File map

**Domain (`SiagroB1.Domain/`)**
- Create: `Enums/PurchaseContractWashoutStatus.cs`, `Entities/PurchaseContractWashout.cs`
- Modify:
  - `Entities/PurchaseContract.cs`
  - `Enums/FinancialDocumentOrigin.cs`
  - `Enums/NotificationEventType.cs`
  - `Entities/FinancialDocumentChangeLogFields.cs`
  - `Entities/ContractChangeLogFields.cs`
  - `Entities/NotificationEventLabels.cs`
  - `Dtos/PurchaseContractTotalsResponseDto.cs`

**Infra (`SiagroB1.Infra/`)**
- Modify: `Context/AppDbContext.cs`, `Interceptors/FinishedContractMutationGuardInterceptor.cs`

**Migrations (`SiagroB1.Migrations/`)**
- Create: `AppContext/*_AddPurchaseContractWashouts`, `CommonContext/*_AddPurchaseContractWashoutApprovalMenu`

**Application: purchase contracts (`SiagroB1.Application/Services/PurchaseContracts/`)**
- Create:
  - `PurchaseContractsWashedOutVolumeService.cs`
  - `PurchaseContractsWashoutGuard.cs`
  - `PurchaseContractsWashoutCreateService.cs`
  - `PurchaseContractsWashoutApprovalService.cs`
  - `PurchaseContractsWashoutRejectService.cs`
  - `PurchaseContractsWashoutReverseService.cs`
  - `PurchaseContractsWashoutsGetService.cs`
- Modify:
  - `PurchaseContractsCloseService.cs`
  - `PurchaseContractsGetShipmentReleasesAvailableService.cs`
  - `PurchaseContractsPriceFixationCreateService.cs`
  - `PurchaseContractsPriceFixationsCancelService.cs`
  - `PurchaseContractsWithdrawApprovalService.cs`
  - `PurchaseContractsTotalsService.cs`

**Application: financials (`SiagroB1.Application/Services/Financials/`)**
- Create: `FinancialDocumentsAdjustProvisionalService.cs`, `FinancialDocumentsGenerateWashoutReceivableService.cs`
- Modify: `FinancialDocumentsGenerateService.cs`, `FinancialDocumentsCancelService.cs`

**Commons**
- Modify: `SiagroB1.Commons/Resources/Resource.pt-br.resx`, `Resource.resx`

**Web (`SiagroB1.Web/`)**
- Create:
  - `Controllers/PurchaseContractsWashoutsController.cs`
  - `Actions/PurchaseContracts/WashoutActionResults.cs`
  - `Actions/PurchaseContracts/PurchaseContractsWashout{Create,Approval,Reject,Reverse}Controller.cs`
- Modify: `ODataConfig/ODataConfigurations.cs`, `Extensions/ServiceCollectionExtensions.cs`

**Tests (`SiagroB1.Application.Tests/`)**
- Create in `PurchaseContracts/Washouts/`:
  - `WashoutTestData.cs`
  - `PurchaseContractWashoutEntityTests.cs`
  - `PurchaseContractsWashedOutVolumeTests.cs`
  - `WashoutBalanceConsumersTests.cs`
  - `PurchaseContractsWashoutCreateServiceTests.cs`
  - `PurchaseContractsWashoutApprovalServiceTests.cs`
  - `PurchaseContractsWashoutReverseServiceTests.cs`
  - `PurchaseContractWashoutWebTests.cs`
- Create: `Financials/FinancialDocumentsWashoutTests.cs`
- Modify: `Infra/FinishedContractMutationGuardInterceptorTests.cs`

---

### Task 1: Domain model, formulas, interceptor and migration

**Files:**
- Create:
  - `SiagroB1.Domain/Enums/PurchaseContractWashoutStatus.cs`
  - `SiagroB1.Domain/Entities/PurchaseContractWashout.cs`
  - `SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractWashoutEntityTests.cs`
- Modify:
  - `SiagroB1.Domain/Entities/PurchaseContract.cs` (FixedVolume ~149, collections ~132, computed 169/202/216/232)
  - `SiagroB1.Domain/Enums/FinancialDocumentOrigin.cs`
  - `SiagroB1.Domain/Enums/NotificationEventType.cs`
  - `SiagroB1.Domain/Entities/FinancialDocumentChangeLogFields.cs`
  - `SiagroB1.Domain/Entities/ContractChangeLogFields.cs`
  - `SiagroB1.Domain/Entities/NotificationEventLabels.cs`
  - `SiagroB1.Infra/Context/AppDbContext.cs` (DbSets ~26, relationships after ~277)
  - `SiagroB1.Infra/Interceptors/FinishedContractMutationGuardInterceptor.cs` (~81)
  - `SiagroB1.Application.Tests/Infra/FinishedContractMutationGuardInterceptorTests.cs`

**Interfaces:**
- Produces:
  - `enum PurchaseContractWashoutStatus { InApproval = 0, Approved = 1, Rejected = 2, Reversed = 3 }`
  - `PurchaseContractWashout`, with the fields below and
    `static decimal CalculateAmount(decimal marketPrice, decimal contractPrice, decimal fixedVolume, decimal penaltyAmount)`
  - `PurchaseContract` members:
    - `decimal WashedOutVolume`, `decimal WashedOutUnfixedVolume` (persisted)
    - `ICollection<PurchaseContractWashout> Washouts`
  - `AppDbContext.PurchaseContractsWashouts`
  - `FinancialDocumentOrigin.PurchaseContractWashout = 6`
  - `FinancialDocumentChangeLogFields.NetAmount = "NetAmount"`
  - `ContractChangeLogFields.Washout = "Washout"` and
    `ContractChangeLogFields.DescribeWashout(PurchaseContractWashout washout, string? unitOfMeasureCode = null)`
  - `NotificationEventType`: `WashoutCreated = 20`, `WashoutApproved = 21`, `WashoutRejected = 22`,
    `WashoutReversed = 23`

- [ ] **Step 1: Write the failing entity tests**

`SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractWashoutEntityTests.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class PurchaseContractWashoutEntityTests
{
    private static PurchaseContract Contract() => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = 100_000m,
    };

    [Fact]
    public void Market_above_contract_charges_the_difference_on_the_fixed_volume_plus_the_penalty()
    {
        // (2,75 − 2,50) × 10.000 = 2.500 + 1.000 de multa
        Assert.Equal(3_500m, PurchaseContractWashout.CalculateAmount(2.75m, 2.50m, 10_000m, 1_000m));
    }

    [Fact]
    public void Market_below_contract_charges_only_the_penalty()
    {
        Assert.Equal(500m, PurchaseContractWashout.CalculateAmount(2.00m, 2.50m, 10_000m, 500m));
    }

    [Fact]
    public void Unfixed_only_washout_charges_only_the_penalty()
    {
        Assert.Equal(300m, PurchaseContractWashout.CalculateAmount(9m, 0m, 0m, 300m));
    }

    [Fact]
    public void The_difference_is_rounded_to_two_decimals()
    {
        // 0,005 × 333,333 = 1,666665 → 1,67
        Assert.Equal(1.67m, PurchaseContractWashout.CalculateAmount(2.505m, 2.5m, 333.333m, 0m));
    }

    [Fact]
    public void Washed_out_volume_reduces_the_physical_balance()
    {
        var contract = Contract();
        contract.AllocatedVolume = 20_000m;
        contract.WashedOutVolume = 30_000m;

        Assert.Equal(50_000m, contract.AvaiableVolume);
    }

    [Fact]
    public void Washed_out_volume_reduces_both_balances_to_release()
    {
        var contract = Contract();
        contract.WashedOutVolume = 10_000m;
        contract.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 40_000m,
            Status = ReleaseStatus.Actived,
        });

        Assert.Equal(50_000m, contract.TotalAvailableToRelease);
        Assert.Equal(50_000m, contract.TotalAvailableToReleaseWithoutProvisioning);
    }

    [Fact]
    public void Unfixed_washed_volume_is_no_longer_available_to_pricing()
    {
        var contract = Contract();
        contract.FixedVolume = 30_000m;
        contract.WashedOutUnfixedVolume = 20_000m;

        Assert.Equal(50_000m, contract.AvailableVolumeToPricing);
    }

    [Fact]
    public void Change_log_describes_code_volumes_amount_and_status()
    {
        var washout = new PurchaseContractWashout
        {
            Sequence = 2,
            FixedVolume = 1_000m,
            UnfixedVolume = 500m,
            Amount = 1_234.5m,
            Status = PurchaseContractWashoutStatus.InApproval,
        };

        Assert.Equal(
            "WO-2: 1.000,000 KG fixado + 500,000 KG não fixado, R$ 1.234,50 — Em aprovação",
            ContractChangeLogFields.DescribeWashout(washout, "KG"));
    }
}
```

Append to `SiagroB1.Application.Tests/Infra/FinishedContractMutationGuardInterceptorTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task AddingWashoutToFinishedContract_Throws()
    {
        await using var ctx = NewContext();
        var pc = NewContract(ContractStatus.Finished);
        ctx.PurchaseContracts.Add(pc);
        await ctx.SaveChangesAsync();

        ctx.PurchaseContractsWashouts.Add(new PurchaseContractWashout
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = pc.Key,
            Sequence = 1,
            UnfixedVolume = 10m,
            Reason = "falta",
        });

        await Assert.ThrowsAsync<DefaultException>(() => ctx.SaveChangesAsync());
    }
```

- [ ] **Step 2: Run the tests and confirm they fail to compile**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractWashoutEntityTests|FullyQualifiedName~FinishedContractMutationGuardInterceptorTests"`

Expected: build error. `PurchaseContractWashout`, `WashedOutVolume` and `PurchaseContractsWashouts` do not exist yet.

- [ ] **Step 3: Create the status enum**

`SiagroB1.Domain/Enums/PurchaseContractWashoutStatus.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Ciclo do washout do contrato de compra. InApproval e Approved RESERVAM volume
/// (<c>PurchaseContract.WashedOutVolume</c>); Rejected e Reversed o devolvem. Os valores são
/// contrato com o banco e com o frontend — não renumere.
/// </summary>
public enum PurchaseContractWashoutStatus
{
    InApproval = 0,
    Approved = 1,
    Rejected = 2,
    Reversed = 3
}
```

- [ ] **Step 4: Create the entity**

`SiagroB1.Domain/Entities/PurchaseContractWashout.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Washout: desistência registrada de parte ou de todo o volume NÃO entregue de um contrato de
/// compra. Tira o volume do saldo, reduz o provisório a pagar da fixação e gera um título a
/// receber do produtor com a diferença de mercado e a multa.
///
/// Registro filho do contrato, no molde de <see cref="PurchaseContractPriceFixation"/> — e não um
/// contrato de venda "WO" (decisão de 14/09/2026, GAC-1164).
/// </summary>
[Table("PURCHASE_CONTRACTS_WASHOUTS")]
[Index(nameof(PurchaseContractKey), nameof(Sequence), IsUnique = true)]
public class PurchaseContractWashout : BaseEntity
{
    public Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    /// <summary>Sequencial por contrato (1, 2, 3…). A tela exibe "WO-{Sequence}".</summary>
    public int Sequence { get; set; }

    /// <summary>
    /// Fixação cujo preço vale para o volume fixado. Nula quando <see cref="FixedVolume"/> é zero —
    /// washout só de volume não fixado não prende nenhuma fixação.
    /// </summary>
    public Guid? PriceFixationKey { get; set; }
    public virtual PurchaseContractPriceFixation? PriceFixation { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal FixedVolume { get; set; }

    /// <summary>Só em contrato a fixar (PAF); no preço fixo é sempre zero.</summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal UnfixedVolume { get; set; }

    /// <summary>Cópia do FixationPrice da fixação no momento do registro.</summary>
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal ContractPrice { get; set; }

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal MarketPrice { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal PenaltyAmount { get; set; }

    /// <summary>Valor do título a receber. Ver <see cref="CalculateAmount"/>.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal Amount { get; set; }

    /// <summary>Vencimento do título; obrigatório quando <see cref="Amount"/> é maior que zero.</summary>
    public DateTime? DueDate { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Reason { get; set; }

    public PurchaseContractWashoutStatus Status { get; set; } = PurchaseContractWashoutStatus.InApproval;

    [Column(TypeName = "VARCHAR(500)")]
    public string? ApprovalComments { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? ReversalReason { get; set; }

    /// <summary>Título a receber gerado na aprovação.</summary>
    public Guid? FinancialDocumentKey { get; set; }
    public virtual FinancialDocument? FinancialDocument { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// <c>round(max(mercado − contrato, 0) × volume fixado, 2) + multa</c>. Mercado abaixo do
    /// contrato vira zero: o produtor que desiste nunca recebe crédito por isso.
    /// </summary>
    public static decimal CalculateAmount(
        decimal marketPrice, decimal contractPrice, decimal fixedVolume, decimal penaltyAmount) =>
        decimal.Round(Math.Max(marketPrice - contractPrice, 0m) * fixedVolume, 2, MidpointRounding.ToEven)
        + decimal.Round(penaltyAmount, 2, MidpointRounding.ToEven);
}
```

- [ ] **Step 5: Extend `PurchaseContract`**

In `SiagroB1.Domain/Entities/PurchaseContract.cs`:

After the `FixedVolume` property, add:

```csharp
    /// <summary>
    /// Volume lavado (persistido, derivado): Σ (FixedVolume + UnfixedVolume) dos washouts InApproval
    /// + Approved. Recalculado exclusivamente por PurchaseContractsWashedOutVolumeService.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal WashedOutVolume { get; set; }

    /// <summary>
    /// Parte NÃO FIXADA de <see cref="WashedOutVolume"/>: volume de contrato a fixar que o produtor
    /// desistiu de entregar e que, por isso, não pode mais ser fixado. Mesmo recálculo.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal WashedOutUnfixedVolume { get; set; }
```

After the `ChangeLogs` collection, add:

```csharp
    public ICollection<PurchaseContractWashout> Washouts { get; set; } = [];
```

Replace the four computed properties with:

```csharp
    [NotMapped]
    public decimal AvailableVolumeToPricing => TotalVolume - FixedVolume - WashedOutUnfixedVolume;
```

```csharp
    [NotMapped]
    public decimal TotalAvailableToRelease => 
        decimal.Round(TotalVolume - TotalShipmentReleases - WashedOutVolume, 2, MidpointRounding.ToEven);
```

```csharp
    [NotMapped]
    public decimal TotalAvailableToReleaseWithoutProvisioning => 
        decimal.Round(TotalVolume - TotalShipmentReleasesWithoutProvisioning - WashedOutVolume, 2, MidpointRounding.ToEven);
```

```csharp
    [NotMapped]
    public decimal AvaiableVolume =>
        decimal.Round(TotalVolume - AllocatedVolume - WashedOutVolume, 2, MidpointRounding.ToEven);
```

Keep the existing XML comments above each property, and append this sentence to the `AvaiableVolume` summary:
`Desconta o volume lavado (<see cref="WashedOutVolume"/>).`

- [ ] **Step 6: Extend enums, change log fields and labels**

In `SiagroB1.Domain/Enums/FinancialDocumentOrigin.cs`, change the last member to `FinancialDocument = 5,` and add:

```csharp
    /// <summary>Título a receber do produtor gerado pela aprovação de um washout de compra.</summary>
    PurchaseContractWashout = 6
```

In `SiagroB1.Domain/Enums/NotificationEventType.cs`, after `PriceFixationReversed = 13,` add:

```csharp

    // Faixa 20+: washout do contrato de compra.
    WashoutCreated = 20,
    WashoutApproved = 21,
    WashoutRejected = 22,
    WashoutReversed = 23,
```

In `SiagroB1.Domain/Entities/NotificationEventLabels.cs`, inside `Event(...)` and before the `_ =>` arm, add:

```csharp
        NotificationEventType.WashoutCreated => "Washout incluído",
        NotificationEventType.WashoutApproved => "Washout aprovado",
        NotificationEventType.WashoutRejected => "Washout rejeitado",
        NotificationEventType.WashoutReversed => "Washout estornado",
```

In `SiagroB1.Domain/Entities/FinancialDocumentChangeLogFields.cs`, add:

```csharp
    /// <summary>Valor do provisório ajustado no lugar pelo washout (valores formatados N2 pt-BR).</summary>
    public const string NetAmount = "NetAmount";
```

In `SiagroB1.Domain/Entities/ContractChangeLogFields.cs`:

After `public const string PriceFixation = "PriceFixation";`, add:

```csharp
    /// <summary>Ciclo de vida do washout do contrato de compra.</summary>
    public const string Washout = "Washout";
```

Before the private `DescribeStatus`, add:

```csharp
    /// <summary>
    /// Como o washout aparece no log. Código, volumes e valor vão em TODAS as linhas, pelo mesmo
    /// motivo da fixação: um contrato pode ter vários washouts.
    /// </summary>
    public static string DescribeWashout(PurchaseContractWashout washout, string? unitOfMeasureCode = null)
    {
        var unit = string.IsNullOrWhiteSpace(unitOfMeasureCode) ? "" : $" {unitOfMeasureCode}";

        return string.Format(
            PtBr,
            "WO-{0}: {1:N3}{2} fixado + {3:N3}{2} não fixado, R$ {4:N2} — {5}",
            washout.Sequence, washout.FixedVolume, unit, washout.UnfixedVolume, washout.Amount,
            DescribeWashoutStatus(washout.Status));
    }

    private static string DescribeWashoutStatus(PurchaseContractWashoutStatus status) => status switch
    {
        PurchaseContractWashoutStatus.InApproval => "Em aprovação",
        PurchaseContractWashoutStatus.Approved => "Aprovado",
        PurchaseContractWashoutStatus.Rejected => "Rejeitado",
        PurchaseContractWashoutStatus.Reversed => "Estornado",
        _ => status.ToString(),
    };
```

- [ ] **Step 7: Wire Infra**

In `SiagroB1.Infra/Context/AppDbContext.cs`, after the `PurchaseContractsPriceFixations` DbSet, add:

```csharp
    public DbSet<PurchaseContractWashout> PurchaseContractsWashouts { get; set; }
```

In `OnModelCreating`, right after the `FinancialDocument` → `SalesContract` relationship, add:

```csharp
        // Washout: Restrict para o contrato e para a fixação — nenhum dos dois pode sumir debaixo
        // de um washout (o estorno de fixação e a retirada da aprovação já recusam antes). NoAction
        // para o título, no mesmo padrão das FKs de FinancialDocument.
        modelBuilder.Entity<PurchaseContractWashout>()
            .HasOne(x => x.PurchaseContract).WithMany(x => x.Washouts)
            .HasForeignKey(x => x.PurchaseContractKey).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PurchaseContractWashout>()
            .HasOne(x => x.PriceFixation).WithMany()
            .HasForeignKey(x => x.PriceFixationKey).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PurchaseContractWashout>()
            .HasOne(x => x.FinancialDocument).WithMany()
            .HasForeignKey(x => x.FinancialDocumentKey).OnDelete(DeleteBehavior.NoAction);
```

In `SiagroB1.Infra/Interceptors/FinishedContractMutationGuardInterceptor.cs`, add this arm to the `switch` after the `PurchaseContractPriceFixation` arm:

```csharp
                PurchaseContractWashout w => w.PurchaseContractKey,
```

Also add "washouts" to the class summary list.

- [ ] **Step 8: Run the tests and confirm they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractWashoutEntityTests|FullyQualifiedName~FinishedContractMutationGuardInterceptorTests"`

Expected: all PASS.

- [ ] **Step 9: Generate the migration**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef migrations add AddPurchaseContractWashouts --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o AppContext
```

Open the generated `AppContext/*_AddPurchaseContractWashouts.cs`. It must contain ONLY:
- two `AddColumn` calls on `PURCHASE_CONTRACTS` (`WashedOutVolume`, `WashedOutUnfixedVolume`,
  `DECIMAL(18,3) DEFAULT 0`, non-null, default 0);
- a `CreateTable` for `PURCHASE_CONTRACTS_WASHOUTS`, with FKs to `PURCHASE_CONTRACTS` (Restrict),
  `PURCHASE_CONTRACTS_PRICE_FIXATIONS` (Restrict) and `FINANCIAL_DOCUMENTS` (NoAction);
- indexes: unique `(PurchaseContractKey, Sequence)`, `PriceFixationKey` and `FinancialDocumentKey`.

If it contains anything else, stop: that is model drift from someone else, so do not hand-edit it
away. Report it instead. Do NOT apply the migration here; Task 7 applies it.

Also add a summary comment above the class:
`/// Washout do contrato de compra (GAC-1164 caso 2): tabela filha + volumes lavados persistidos.`

- [ ] **Step 10: Build and stage**

Run: `dotnet build SiagroB1.sln`. Expected: 0 errors.

```powershell
git add SiagroB1.Domain/Enums/PurchaseContractWashoutStatus.cs SiagroB1.Domain/Entities/PurchaseContractWashout.cs SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractWashoutEntityTests.cs SiagroB1.Migrations/AppContext
```

---

### Task 2: Washed-out volume service, balance consumers and guards in existing services

**Files:**
- Create:
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashedOutVolumeService.cs`
  - `SiagroB1.Application.Tests/PurchaseContracts/Washouts/WashoutTestData.cs`
  - `SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractsWashedOutVolumeTests.cs`
  - `SiagroB1.Application.Tests/PurchaseContracts/Washouts/WashoutBalanceConsumersTests.cs`
- Modify:
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsGetShipmentReleasesAvailableService.cs:24-31`
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCloseService.cs:26, 79-90`
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationCreateService.cs:48`
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsCancelService.cs:49`
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWithdrawApprovalService.cs:28`
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsTotalsService.cs`
  - `SiagroB1.Domain/Dtos/PurchaseContractTotalsResponseDto.cs`
  - `SiagroB1.Commons/Resources/Resource.pt-br.resx`, `Resource.resx`

**Interfaces:**
- Consumes (from Task 1): `PurchaseContractWashout`, `PurchaseContractWashoutStatus`,
  `PurchaseContract.WashedOutVolume/WashedOutUnfixedVolume`, `AppDbContext.PurchaseContractsWashouts`.
- Produces:
  - `PurchaseContractsWashedOutVolumeService(AppDbContext)` with:
    - `Task RecalculateAsync(PurchaseContract contract)` — assigns both aggregates and does NOT save.
    - `static Task<(decimal Total, decimal Unfixed)> ActiveVolumesAsync(AppDbContext context, Guid contractKey, Guid? excludingWashoutKey = null)`
    - `static Task<decimal> ActiveFixedVolumeAsync(AppDbContext context, Guid fixationKey, Guid? excludingWashoutKey = null)`
    - `static Task<decimal> ApprovedFixedVolumeAsync(AppDbContext context, Guid fixationKey, Guid? excludingWashoutKey = null)`
  - Test helper `internal static class WashoutTestData` with `Contract`, `Fixation`, `Washout` and
    `Provisional` factories, used by Tasks 3 through 6.
  - `PurchaseContractTotalsResponseDto.WashedOutVolume`.

- [ ] **Step 1: Create the test data helper**

`SiagroB1.Application.Tests/PurchaseContracts/Washouts/WashoutTestData.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

/// <summary>Sementes compartilhadas pelos testes de washout (serviços e financeiro).</summary>
internal static class WashoutTestData
{
    public static PurchaseContract Contract(
        ContractType type = ContractType.Fixed,
        ContractStatus status = ContractStatus.Approved,
        decimal totalVolume = 100_000m) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        ItemName = "SOJA GRAO",
        BranchCode = "01",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = totalVolume,
        StandardPrice = 2.5m,
        StandardCurrency = CurrencyType.Brl,
        StandardCashFlowDate = new DateTime(2026, 12, 31),
        Type = type,
        Status = status,
    };

    public static PurchaseContractPriceFixation Fixation(
        PurchaseContract contract,
        decimal volume,
        decimal price = 2.5m,
        PriceFixationStatus status = PriceFixationStatus.Confirmed) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contract.Key,
        FixationVolume = volume,
        FixationPrice = price,
        FinancialDueDate = new DateTime(2026, 12, 31),
        Status = status,
    };

    public static PurchaseContractWashout Washout(
        PurchaseContract contract,
        PurchaseContractPriceFixation? fixation = null,
        decimal fixedVolume = 0m,
        decimal unfixedVolume = 0m,
        PurchaseContractWashoutStatus status = PurchaseContractWashoutStatus.InApproval,
        int sequence = 1,
        decimal marketPrice = 2.75m,
        decimal penaltyAmount = 1_000m)
    {
        var contractPrice = fixation?.FixationPrice ?? 0m;

        return new PurchaseContractWashout
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            PriceFixationKey = fixedVolume > 0 ? fixation?.Key : null,
            Sequence = sequence,
            FixedVolume = fixedVolume,
            UnfixedVolume = unfixedVolume,
            ContractPrice = contractPrice,
            MarketPrice = marketPrice,
            PenaltyAmount = penaltyAmount,
            Amount = PurchaseContractWashout.CalculateAmount(marketPrice, contractPrice, fixedVolume, penaltyAmount),
            DueDate = new DateTime(2026, 10, 31),
            Reason = "Produtor sem produto",
            Status = status,
        };
    }

    public static FinancialDocument Provisional(PurchaseContract contract, PurchaseContractPriceFixation fixation) => new()
    {
        Key = Guid.NewGuid(),
        Code = "FD-0001",
        CardCode = contract.CardCode,
        Direction = FinancialDirection.Payable,
        Nature = FinancialDocumentNature.Provisional,
        Status = FinancialDocumentStatus.Open,
        DueDate = new DateTime(2026, 12, 31),
        NetAmount = decimal.Round(fixation.FixationVolume * fixation.FixationPrice, 2, MidpointRounding.ToEven),
        OriginType = FinancialDocumentOrigin.PurchaseContractPriceFixation,
        OriginKey = fixation.Key,
        OriginDocNumber = contract.Code,
        PurchaseContractKey = contract.Key,
    };
}
```

- [ ] **Step 2: Write the failing volume service tests**

`SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractsWashedOutVolumeTests.cs`:

```csharp
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class PurchaseContractsWashedOutVolumeTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public async Task Recalculate_counts_only_in_approval_and_approved_washouts()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        var fixation = WashoutTestData.Fixation(contract, 30_000m);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.PurchaseContractsWashouts.AddRange(
            WashoutTestData.Washout(contract, fixation, fixedVolume: 1_000m, sequence: 1),
            WashoutTestData.Washout(contract, unfixedVolume: 2_000m, status: PurchaseContractWashoutStatus.Approved, sequence: 2),
            WashoutTestData.Washout(contract, unfixedVolume: 5_000m, status: PurchaseContractWashoutStatus.Rejected, sequence: 3),
            WashoutTestData.Washout(contract, unfixedVolume: 7_000m, status: PurchaseContractWashoutStatus.Reversed, sequence: 4));
        await _db.Context.SaveChangesAsync();

        await new PurchaseContractsWashedOutVolumeService(_db.Context).RecalculateAsync(contract);

        Assert.Equal(3_000m, contract.WashedOutVolume);
        Assert.Equal(2_000m, contract.WashedOutUnfixedVolume);
    }

    [Fact]
    public async Task Active_volumes_can_exclude_one_washout()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        var kept = WashoutTestData.Washout(contract, unfixedVolume: 1_000m, sequence: 1);
        var excluded = WashoutTestData.Washout(contract, unfixedVolume: 4_000m, sequence: 2);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsWashouts.AddRange(kept, excluded);
        await _db.Context.SaveChangesAsync();

        var (total, unfixed) = await PurchaseContractsWashedOutVolumeService
            .ActiveVolumesAsync(_db.Context, contract.Key, excluded.Key);

        Assert.Equal(1_000m, total);
        Assert.Equal(1_000m, unfixed);
    }

    [Fact]
    public async Task Fixed_volume_per_fixation_distinguishes_active_from_approved()
    {
        var contract = WashoutTestData.Contract();
        var fixation = WashoutTestData.Fixation(contract, 100_000m);
        var pending = WashoutTestData.Washout(contract, fixation, fixedVolume: 3_000m, sequence: 1);
        var approved = WashoutTestData.Washout(contract, fixation, fixedVolume: 4_000m,
            status: PurchaseContractWashoutStatus.Approved, sequence: 2);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.PurchaseContractsWashouts.AddRange(pending, approved);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(7_000m, await PurchaseContractsWashedOutVolumeService.ActiveFixedVolumeAsync(_db.Context, fixation.Key));
        Assert.Equal(3_000m, await PurchaseContractsWashedOutVolumeService.ActiveFixedVolumeAsync(_db.Context, fixation.Key, approved.Key));
        Assert.Equal(4_000m, await PurchaseContractsWashedOutVolumeService.ApprovedFixedVolumeAsync(_db.Context, fixation.Key));
        Assert.Equal(0m, await PurchaseContractsWashedOutVolumeService.ApprovedFixedVolumeAsync(_db.Context, fixation.Key, approved.Key));
    }
}
```

- [ ] **Step 3: Write the failing consumer tests**

`SiagroB1.Application.Tests/PurchaseContracts/Washouts/WashoutBalanceConsumersTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class WashoutBalanceConsumersTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsCloseService CloseService() =>
        new(_db.Context, new PurchaseContractsFixedVolumeService(_db.Context), TestNotificationOutbox.For(_db.Context),
            FinancialDocumentTestServices.Cancel(_db.Context));

    private async Task<PurchaseContract> SeedAsync(PurchaseContract contract, params object[] children)
    {
        _db.Context.PurchaseContracts.Add(contract);
        foreach (var child in children) _db.Context.Add(child);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task Fully_washed_contract_leaves_the_release_selector()
    {
        var contract = WashoutTestData.Contract(totalVolume: 1_000m);
        contract.WashedOutVolume = 1_000m;
        await SeedAsync(contract);

        var service = new PurchaseContractsGetShipmentReleasesAvailableService(
            _db, NullLogger<PurchaseContractsGetShipmentReleasesAvailableService>.Instance);

        Assert.DoesNotContain(await service.Query().ToListAsync(), c => c.Key == contract.Key);
    }

    [Fact]
    public async Task Close_refuses_a_pending_washout()
    {
        var contract = WashoutTestData.Contract(totalVolume: 1_000m);
        await SeedAsync(contract, WashoutTestData.Washout(contract, unfixedVolume: 100m));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => CloseService().ExecuteAsync(contract.Key, "tester"));

        Assert.Contains("washout", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Close_counts_approved_washouts_in_the_negative_balance_guard()
    {
        var contract = WashoutTestData.Contract(totalVolume: 1_000m);
        await SeedAsync(contract, WashoutTestData.Washout(contract, unfixedVolume: 1_500m,
            status: PurchaseContractWashoutStatus.Approved));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => CloseService().ExecuteAsync(contract.Key, "tester"));

        Assert.Contains("lavado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Close_with_an_approved_washout_inside_the_balance_succeeds()
    {
        var contract = WashoutTestData.Contract(totalVolume: 1_000m);
        await SeedAsync(contract, WashoutTestData.Washout(contract, unfixedVolume: 400m,
            status: PurchaseContractWashoutStatus.Approved));

        await CloseService().ExecuteAsync(contract.Key, "tester");

        Assert.Equal(ContractStatus.Finished,
            (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).Status);
    }

    [Fact]
    public async Task Price_fixation_cannot_use_volume_already_washed_unfixed()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        contract.WashedOutUnfixedVolume = 60_000m;
        await SeedAsync(contract);

        var service = new PurchaseContractsPriceFixationCreateService(
            _db.Context, new PurchaseContractsFixedVolumeService(_db.Context),
            new PurchaseContractsChangeLogService(_db.Context), TestNotificationOutbox.For(_db.Context),
            NullLogger<PurchaseContractsPriceFixationCreateService>.Instance);

        await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(contract.Key,
            new PurchaseContractPriceFixation { FixationVolume = 50_000m, FixationPrice = 2.5m }, "tester"));
    }

    [Fact]
    public async Task Price_fixation_reversal_refuses_a_fixation_with_an_active_washout()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        var fixation = WashoutTestData.Fixation(contract, 30_000m);
        await SeedAsync(contract, fixation,
            WashoutTestData.Washout(contract, fixation, fixedVolume: 1_000m, status: PurchaseContractWashoutStatus.Approved));

        var service = new PurchaseContractsPriceFixationsCancelService(
            _db.Context, new PurchaseContractsFixedVolumeService(_db.Context),
            new PurchaseContractsChangeLogService(_db.Context), TestNotificationOutbox.For(_db.Context),
            FinancialDocumentTestServices.Cancel(_db.Context));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(fixation.Key, "tester"));

        Assert.Contains("washout", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Withdraw_approval_refuses_a_contract_with_any_washout()
    {
        var contract = WashoutTestData.Contract();
        await SeedAsync(contract, WashoutTestData.Washout(contract, unfixedVolume: 10m,
            status: PurchaseContractWashoutStatus.Rejected));

        var service = new PurchaseContractsWithdrawApprovalService(
            _db, new FakeStringLocalizer<Resource>(), TestNotificationOutbox.For(_db.Context));

        await Assert.ThrowsAsync<BusinessException>(() => service.ExecuteAsync(contract.Key, "tester"));
    }

    [Fact]
    public async Task Totals_expose_the_washed_volume_and_discount_it_from_the_balance()
    {
        var contract = WashoutTestData.Contract(totalVolume: 1_000m);
        contract.WashedOutVolume = 300m;
        await SeedAsync(contract);

        var totals = await new PurchaseContractsTotalsService(_db.Context).GetTotals(contract.Key);

        Assert.Equal(300m, totals.WashedOutVolume);
        Assert.Equal(700m, totals.AvaiableVolume);
    }
}
```

- [ ] **Step 4: Run the tests and confirm they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContracts.Washouts"`

Expected: build error. `PurchaseContractsWashedOutVolumeService` and `WashedOutVolume` do not exist on the DTO yet.

- [ ] **Step 5: Create the volume service**

`SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashedOutVolumeService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Ponto ÚNICO das somas de washout. <see cref="RecalculateAsync"/> é o único escritor de
/// <see cref="PurchaseContract.WashedOutVolume"/> e <see cref="PurchaseContract.WashedOutUnfixedVolume"/>.
///
/// Os métodos estáticos existem para quem não pode ganhar dependência no construtor sem quebrar
/// dezenas de testes (encerramento, gerador de provisório) — mesmo idioma de
/// PurchaseContractsRecalculateBalanceService.CalculateAllocatedAsync.
///
/// Todas as somas leem o BANCO: mudança só rastreada não entra. Quem muda status salva antes.
/// </summary>
public class PurchaseContractsWashedOutVolumeService(AppDbContext context)
{
    /// <summary>Recalcula e atribui os dois agregados. NÃO persiste.</summary>
    public async Task RecalculateAsync(PurchaseContract contract)
    {
        var (total, unfixed) = await ActiveVolumesAsync(context, contract.Key);
        contract.WashedOutVolume = total;
        contract.WashedOutUnfixedVolume = unfixed;
    }

    /// <summary>Σ dos washouts ATIVOS (InApproval + Approved) do contrato.</summary>
    public static async Task<(decimal Total, decimal Unfixed)> ActiveVolumesAsync(
        AppDbContext context, Guid contractKey, Guid? excludingWashoutKey = null)
    {
        var rows = await context.PurchaseContractsWashouts
            .Where(w => w.PurchaseContractKey == contractKey
                        && (w.Status == PurchaseContractWashoutStatus.InApproval
                            || w.Status == PurchaseContractWashoutStatus.Approved)
                        && (excludingWashoutKey == null || w.Key != excludingWashoutKey))
            .Select(w => new { w.FixedVolume, w.UnfixedVolume })
            .ToListAsync();

        return (Round(rows.Sum(r => r.FixedVolume + r.UnfixedVolume)), Round(rows.Sum(r => r.UnfixedVolume)));
    }

    /// <summary>Volume fixado já reservado por washouts ATIVOS de uma fixação (trava de saldo).</summary>
    public static Task<decimal> ActiveFixedVolumeAsync(
        AppDbContext context, Guid fixationKey, Guid? excludingWashoutKey = null) =>
        SumFixedAsync(context, fixationKey, excludingWashoutKey, onlyApproved: false);

    /// <summary>Volume fixado lavado por washouts APROVADOS de uma fixação (valor do provisório).</summary>
    public static Task<decimal> ApprovedFixedVolumeAsync(
        AppDbContext context, Guid fixationKey, Guid? excludingWashoutKey = null) =>
        SumFixedAsync(context, fixationKey, excludingWashoutKey, onlyApproved: true);

    private static async Task<decimal> SumFixedAsync(
        AppDbContext context, Guid fixationKey, Guid? excludingWashoutKey, bool onlyApproved)
    {
        var total = await context.PurchaseContractsWashouts
            .Where(w => w.PriceFixationKey == fixationKey
                        && (w.Status == PurchaseContractWashoutStatus.Approved
                            || (!onlyApproved && w.Status == PurchaseContractWashoutStatus.InApproval))
                        && (excludingWashoutKey == null || w.Key != excludingWashoutKey))
            .SumAsync(w => w.FixedVolume);

        return Round(total);
    }

    private static decimal Round(decimal value) => decimal.Round(value, 3, MidpointRounding.ToEven);
}
```

- [ ] **Step 6: Update the consumers**

**a) Release selector.** In `PurchaseContractsGetShipmentReleasesAvailableService.Query()`, change the
balance expression's first line from `(p.TotalVolume - p.ShipmentReleases` to
`(p.TotalVolume - p.WashedOutVolume - p.ShipmentReleases`. Also add this sentence to the summary:
"Desconta o volume lavado (WashedOutVolume), como a propriedade."

**b) Close service.** In `PurchaseContractsCloseService`:
- In `ExecuteAsync`, call `await GuardPendingWashoutsAsync(contract);` right before
  `await GuardNegativeBalanceAsync(contract);`.
- Replace `GuardNegativeBalanceAsync` with the version below, and add the new method:

```csharp
    /// <summary>
    /// Washout em aprovação reserva volume mas ainda pode ser rejeitado. Encerrar com ele pendente
    /// congelaria o contrato com um volume "lavado" que ninguém decidiu.
    /// </summary>
    private async Task GuardPendingWashoutsAsync(PurchaseContract contract)
    {
        var pendingCount = await context.PurchaseContractsWashouts
            .CountAsync(w => w.PurchaseContractKey == contract.Key
                             && w.Status == PurchaseContractWashoutStatus.InApproval);

        if (pendingCount > 0)
            throw new ApplicationException(
                $"Contrato possui {pendingCount} washout(s) pendente(s) de aprovação. " +
                "Aprove ou rejeite antes de encerrar.");
    }

    /// <summary>
    /// Espelha <c>SalesContractsCloseService</c>: contrato consumido ALÉM do volume contratado
    /// não pode ser congelado. Decide sobre o saldo RECALCULADO do ledger e dos washouts ativos,
    /// não sobre os agregados persistidos, que podem estar defasados. Não persiste o recálculo.
    /// </summary>
    private async Task GuardNegativeBalanceAsync(PurchaseContract contract)
    {
        var allocated = await PurchaseContractsRecalculateBalanceService
            .CalculateAllocatedAsync(context, contract.Key);
        var (washedOut, _) = await PurchaseContractsWashedOutVolumeService
            .ActiveVolumesAsync(context, contract.Key);
        var balance = decimal.Round(contract.TotalVolume - allocated - washedOut, 2, MidpointRounding.ToEven);

        if (balance < 0)
            throw new ApplicationException(
                $"Contrato faturado além do volume contratado. Contratado: {contract.TotalVolume:N2}, " +
                $"alocado: {allocated:N2}, lavado: {washedOut:N2}, saldo: {balance:N2}. " +
                "Ajuste as alocações antes de encerrar.");
    }
```

**c) Fixation create guard.** In `PurchaseContractsPriceFixationCreateService`, change the check to:

```csharp
            // Volume não fixado lavado por washout não pode mais ser fixado.
            if (contract.FixedVolume + contract.WashedOutUnfixedVolume + associationEntity.FixationVolume > contract.TotalVolume)
```

**d) Fixation reversal guard.** In `PurchaseContractsPriceFixationsCancelService`, right after the
`contract.Status != ContractStatus.Approved` guard and before `BeginTransactionAsync`, add:

```csharp
        // Voltar a fixação para InApproval deixaria o washout sem preço de referência e com o
        // provisório já reduzido.
        var hasActiveWashout = await context.PurchaseContractsWashouts.AnyAsync(w =>
            w.PriceFixationKey == fixation.Key &&
            (w.Status == PurchaseContractWashoutStatus.InApproval || w.Status == PurchaseContractWashoutStatus.Approved));

        if (hasActiveWashout)
            throw new ApplicationException(
                "Fixação possui washout registrado. Rejeite ou estorne o washout antes de estornar a fixação.");
```

**e) Withdraw-approval guard.** In `PurchaseContractsWithdrawApprovalService`, right after the
`HasShipmentReleases` guard, add:

```csharp
        // Em rascunho o contrato pode ser excluído, e a FK Restrict do washout (de QUALQUER status)
        // barraria a exclusão com erro 547.
        if (await db.Context.PurchaseContractsWashouts.AnyAsync(w => w.PurchaseContractKey == contract.Key))
            throw new BusinessException(resource["PURCHASE_CONTRACT_HAS_WASHOUTS"]);
```

In `SiagroB1.Commons/Resources/Resource.pt-br.resx`, right after the `PURCHASE_CONTRACT_HAS_SHIPMENT_RELEASES` entry, add:

```xml
    <data name="PURCHASE_CONTRACT_HAS_WASHOUTS" xml:space="preserve">
        <value>Contrato de Compra possui washout registrado, não é possivel remover da aprovação.</value>
    </data>
```

In `SiagroB1.Commons/Resources/Resource.resx`, at the same place, add:

```xml
    <data name="PURCHASE_CONTRACT_HAS_WASHOUTS" xml:space="preserve">
        <value>Purchase contract has washouts; it cannot be withdrawn from approval.</value>
    </data>
```

**f) Totals.** In `PurchaseContractTotalsResponseDto`, add:

```csharp
    [JsonPropertyName("WashedOutVolume")]
    public decimal WashedOutVolume { get; set; }
```

In `PurchaseContractsTotalsService.GetTotals`, add `WashedOutVolume = ctr.WashedOutVolume,` to the initializer.

- [ ] **Step 7: Run the tests and confirm they pass**

1. Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContracts.Washouts"`. Expected: all PASS.
2. Run the neighbors whose code changed:
   `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsCloseReopenServiceTests|FullyQualifiedName~PriceFixations|FullyQualifiedName~FinancialDocumentUndoHooksTests"`.
   Expected: all PASS. If a close test asserted the old negative-balance message verbatim, update
   that assertion to the new text (with "lavado:") and note it in your report.

- [ ] **Step 8: Stage**

```powershell
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashedOutVolumeService.cs SiagroB1.Application.Tests/PurchaseContracts/Washouts
```

---

### Task 3: Financial pieces (generator discount, in-place adjustment, receivable, cancel by key)

**Files:**
- Create:
  - `SiagroB1.Application/Services/Financials/FinancialDocumentsAdjustProvisionalService.cs`
  - `SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateWashoutReceivableService.cs`
  - `SiagroB1.Application.Tests/Financials/FinancialDocumentsWashoutTests.cs`
- Modify:
  - `SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateService.cs:34-52`
  - `SiagroB1.Application/Services/Financials/FinancialDocumentsCancelService.cs` (the private
    `Cancel` becomes internal; add `EnqueueCancelByKeyAsync`)

**Interfaces:**
- Consumes (from Task 2): `PurchaseContractsWashedOutVolumeService.ApprovedFixedVolumeAsync`, and
  `WashoutTestData` in the tests.
- Produces:
  - `FinancialDocumentsGenerateService.EnqueueForPurchaseFixationAsync(PurchaseContract contract, PurchaseContractPriceFixation fixation, string userName, decimal? remainingVolume = null)`
  - `FinancialDocumentsAdjustProvisionalService(AppDbContext context, FinancialDocumentsGenerateService generateService, FinancialDocumentChangeLogService changeLog)`:
    - `Task EnqueueForPurchaseFixationAsync(PurchaseContract contract, PurchaseContractPriceFixation fixation, decimal remainingVolume, string userName)`
    - `const string WashedOutCancellationReason = "Volume lavado por washout"`
  - `FinancialDocumentsGenerateWashoutReceivableService(AppDbContext context, DocNumberSequenceService docNumberSequence, IBusinessPartnerService businessPartnerService)`:
    - `Task<FinancialDocument> EnqueueAsync(PurchaseContract contract, PurchaseContractWashout washout, string userName)`
  - `FinancialDocumentsCancelService.EnqueueCancelByKeyAsync(Guid key, string reason, string userName)`:
    enqueue-only; throws `ApplicationException` when `SettledAmount != 0`; does nothing when the
    document is already cancelled.
  - `internal static void FinancialDocumentsCancelService.Cancel(FinancialDocument, string, string)`

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsWashoutTests.cs`:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.PurchaseContracts.Washouts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsWashoutTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsGenerateService Generate() => FinancialDocumentTestServices.Generate(_db.Context);

    private FinancialDocumentsAdjustProvisionalService Adjust() =>
        new(_db.Context, Generate(), new FinancialDocumentChangeLogService(_db.Context));

    private FinancialDocumentsGenerateWashoutReceivableService Receivable() => new(
        _db.Context, new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" }));

    private async Task<(PurchaseContract Contract, PurchaseContractPriceFixation Fixation)> SeedFixationAsync(
        params (decimal FixedVolume, PurchaseContractWashoutStatus Status)[] washouts)
    {
        var contract = WashoutTestData.Contract();
        var fixation = WashoutTestData.Fixation(contract, 100_000m);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);

        var sequence = 1;
        foreach (var (fixedVolume, status) in washouts)
            _db.Context.PurchaseContractsWashouts.Add(
                WashoutTestData.Washout(contract, fixation, fixedVolume: fixedVolume, status: status, sequence: sequence++));

        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    [Fact]
    public async Task Generator_discounts_the_approved_washouts_of_the_fixation()
    {
        var (contract, fixation) = await SeedFixationAsync((40_000m, PurchaseContractWashoutStatus.Approved));

        await Generate().EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        // (100.000 − 40.000) × 2,5
        Assert.Equal(150_000m, _db.Context.FinancialDocuments.Single().NetAmount);
    }

    [Fact]
    public async Task Generator_ignores_washouts_that_are_not_approved()
    {
        var (contract, fixation) = await SeedFixationAsync(
            (40_000m, PurchaseContractWashoutStatus.InApproval),
            (10_000m, PurchaseContractWashoutStatus.Reversed));

        await Generate().EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(250_000m, _db.Context.FinancialDocuments.Single().NetAmount);
    }

    [Fact]
    public async Task Generator_skips_a_fully_washed_fixation()
    {
        var (contract, fixation) = await SeedFixationAsync((100_000m, PurchaseContractWashoutStatus.Approved));

        await Generate().EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Empty(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Generator_uses_the_explicit_remaining_volume()
    {
        var (contract, fixation) = await SeedFixationAsync();

        await Generate().EnqueueForPurchaseFixationAsync(contract, fixation, "tester", remainingVolume: 10_000m);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(25_000m, _db.Context.FinancialDocuments.Single().NetAmount);
    }

    [Fact]
    public async Task Adjust_reduces_the_open_provisional_in_place_and_logs_the_change()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var provisional = WashoutTestData.Provisional(contract, fixation);
        _db.Context.FinancialDocuments.Add(provisional);
        await _db.Context.SaveChangesAsync();

        await Adjust().EnqueueForPurchaseFixationAsync(contract, fixation, 60_000m, "tester");
        await _db.Context.SaveChangesAsync();

        var document = Assert.Single(_db.Context.FinancialDocuments);
        Assert.Equal(provisional.Key, document.Key);
        Assert.Equal(150_000m, document.NetAmount);

        var log = Assert.Single(_db.Context.FinancialDocumentChangeLogs);
        Assert.Equal(FinancialDocumentChangeLogFields.NetAmount, log.Field);
        Assert.Equal("250.000,00", log.OldValue);
        Assert.Equal("150.000,00", log.NewValue);
    }

    [Fact]
    public async Task Adjust_with_an_unchanged_amount_logs_nothing()
    {
        var (contract, fixation) = await SeedFixationAsync();
        _db.Context.FinancialDocuments.Add(WashoutTestData.Provisional(contract, fixation));
        await _db.Context.SaveChangesAsync();

        await Adjust().EnqueueForPurchaseFixationAsync(contract, fixation, 100_000m, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Empty(_db.Context.FinancialDocumentChangeLogs);
    }

    [Fact]
    public async Task Adjust_cancels_the_provisional_when_nothing_remains()
    {
        var (contract, fixation) = await SeedFixationAsync();
        _db.Context.FinancialDocuments.Add(WashoutTestData.Provisional(contract, fixation));
        await _db.Context.SaveChangesAsync();

        await Adjust().EnqueueForPurchaseFixationAsync(contract, fixation, 0m, "tester");
        await _db.Context.SaveChangesAsync();

        var document = Assert.Single(_db.Context.FinancialDocuments);
        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
        Assert.Equal(FinancialDocumentsAdjustProvisionalService.WashedOutCancellationReason, document.CancellationReason);
    }

    [Fact]
    public async Task Adjust_generates_a_provisional_when_none_is_open()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var canceled = WashoutTestData.Provisional(contract, fixation);
        canceled.Status = FinancialDocumentStatus.Canceled;
        _db.Context.FinancialDocuments.Add(canceled);
        await _db.Context.SaveChangesAsync();

        await Adjust().EnqueueForPurchaseFixationAsync(contract, fixation, 30_000m, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(2, _db.Context.FinancialDocuments.Count());
        Assert.Equal(75_000m, _db.Context.FinancialDocuments.Single(x => x.Status == FinancialDocumentStatus.Open).NetAmount);
    }

    [Fact]
    public async Task Receivable_is_a_firm_open_document_pointing_to_the_washout()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var washout = WashoutTestData.Washout(contract, fixation, fixedVolume: 10_000m);

        var document = await Receivable().EnqueueAsync(contract, washout, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(FinancialDirection.Receivable, document.Direction);
        Assert.Equal(FinancialDocumentNature.Firm, document.Nature);
        Assert.Equal(FinancialDocumentStatus.Open, document.Status);
        Assert.Equal(3_500m, document.NetAmount);
        Assert.Equal(new DateTime(2026, 10, 31), document.DueDate);
        Assert.Equal(FinancialDocumentOrigin.PurchaseContractWashout, document.OriginType);
        Assert.Equal(washout.Key, document.OriginKey);
        Assert.Equal("PC-001", document.OriginDocNumber);
        Assert.Equal(contract.Key, document.PurchaseContractKey);
        Assert.Equal("PRODUTOR TESTE", document.CardName);
        Assert.False(document.IsBlockedForSettlement);
    }

    [Fact]
    public async Task Receivable_is_not_duplicated_for_the_same_washout()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var washout = WashoutTestData.Washout(contract, fixation, fixedVolume: 10_000m);
        var service = Receivable();

        await service.EnqueueAsync(contract, washout, "tester");
        await _db.Context.SaveChangesAsync();
        await service.EnqueueAsync(contract, washout, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Single(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Cancel_by_key_refuses_a_settled_document()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var document = WashoutTestData.Provisional(contract, fixation);
        document.SettledAmount = 100m;
        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            FinancialDocumentTestServices.Cancel(_db.Context).EnqueueCancelByKeyAsync(document.Key, "estorno", "tester"));

        Assert.Contains("baixa", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancel_by_key_cancels_an_open_document()
    {
        var (contract, fixation) = await SeedFixationAsync();
        var document = WashoutTestData.Provisional(contract, fixation);
        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();

        await FinancialDocumentTestServices.Cancel(_db.Context).EnqueueCancelByKeyAsync(document.Key, "estorno", "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(FinancialDocumentStatus.Canceled, _db.Context.FinancialDocuments.Single().Status);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsWashoutTests"`

Expected: build error. The new services and the `remainingVolume` parameter do not exist yet.

- [ ] **Step 3: Discount approved washouts in the generator**

In `FinancialDocumentsGenerateService`:
- Add `using SiagroB1.Application.Services.PurchaseContracts;`.
- Replace `EnqueueForPurchaseFixationAsync` with:

```csharp
    /// <param name="remainingVolume">
    /// Volume a faturar já calculado pelo chamador. Só o ajuste do washout passa este valor: ele
    /// roda ANTES do SaveChanges que grava o washout aprovado, e a soma pelo banco ainda não o
    /// enxergaria. Nulo = FixationVolume menos os washouts APROVADOS da fixação, lidos do banco —
    /// é o que impede a reabertura do contrato e o backlog de ressuscitarem o valor cheio.
    /// </param>
    public async Task EnqueueForPurchaseFixationAsync(
        PurchaseContract contract, PurchaseContractPriceFixation fixation, string userName,
        decimal? remainingVolume = null)
    {
        var volume = remainingVolume ?? fixation.FixationVolume -
            await PurchaseContractsWashedOutVolumeService.ApprovedFixedVolumeAsync(context, fixation.Key);

        // Fixação inteiramente lavada: não há o que faturar.
        if (volume <= 0) return;

        await EnqueueAsync(
            direction: FinancialDirection.Payable,
            originType: FinancialDocumentOrigin.PurchaseContractPriceFixation,
            originKey: fixation.Key,
            contractCode: contract.Code,
            cardCode: contract.CardCode,
            branchCode: contract.BranchCode,
            currency: contract.StandardCurrency ?? CurrencyType.Brl,
            contractType: contract.Type,
            volume: volume,
            price: fixation.FixationPrice,
            fixationDueDate: fixation.FinancialDueDate,
            contractCashFlowDate: contract.StandardCashFlowDate,
            paymentTermsText: fixation.PaymentDetails ?? contract.PaymentTerms,
            purchaseContractKey: contract.Key,
            salesContractKey: null,
            userName: userName);
    }
```

- [ ] **Step 4: Update the cancel service**

In `FinancialDocumentsCancelService`:
- Change `private static void Cancel(` to `internal static void Cancel(`.
- Add after `EnqueueCancelByOriginAsync`:

```csharp
    /// <summary>
    /// Enqueue-only: cancela UM documento pela chave — hoje, o título a receber do washout
    /// estornado. Recusa documento com baixa: o dinheiro já entrou, e cancelar o título apagaria
    /// o rastro dele. Documento já cancelado passa em silêncio (idempotente).
    /// </summary>
    public async Task EnqueueCancelByKeyAsync(Guid key, string reason, string userName)
    {
        var document = await Context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Status == FinancialDocumentStatus.Canceled) return;

        if (document.SettledAmount != 0m)
            throw new ApplicationException(
                $"O documento {document.Code} possui baixas. Estorne a baixa antes.");

        Cancel(document, reason, userName);
    }
```

- [ ] **Step 5: Create the adjust service**

`SiagroB1.Application/Services/Financials/FinancialDocumentsAdjustProvisionalService.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Ajusta NO LUGAR o provisório a pagar de uma fixação cujo volume mudou por washout.
///
/// No lugar, e não cancelar-e-gerar: provisório não aceita baixa, então mudar o valor não afeta
/// nada realizado; e cancelar-e-gerar exigiria gravar o cancelamento antes do insert (índice único
/// filtrado IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin) e buscar número por Dapper no meio da
/// transação.
///
/// ENQUEUE-ONLY. Chame ANTES de abrir transação: sem provisório aberto, delega ao gerador, que
/// busca número em DOC_NUMBERS.
/// </summary>
public class FinancialDocumentsAdjustProvisionalService(
    AppDbContext context,
    FinancialDocumentsGenerateService generateService,
    FinancialDocumentChangeLogService changeLog)
{
    public const string WashedOutCancellationReason = "Volume lavado por washout";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <param name="remainingVolume">Volume da fixação que continua a faturar, já descontados os washouts aprovados.</param>
    public async Task EnqueueForPurchaseFixationAsync(
        PurchaseContract contract, PurchaseContractPriceFixation fixation, decimal remainingVolume, string userName)
    {
        var document = await context.FinancialDocuments.FirstOrDefaultAsync(x =>
            x.OriginType == FinancialDocumentOrigin.PurchaseContractPriceFixation &&
            x.OriginKey == fixation.Key &&
            x.Nature == FinancialDocumentNature.Provisional &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (document is null)
        {
            // Cancelado por volume zero (e agora estornado) ou dado antigo sem provisório.
            if (remainingVolume > 0)
                await generateService.EnqueueForPurchaseFixationAsync(contract, fixation, userName, remainingVolume);
            return;
        }

        if (remainingVolume <= 0)
        {
            FinancialDocumentsCancelService.Cancel(document, WashedOutCancellationReason, userName);
            return;
        }

        var newAmount = decimal.Round(remainingVolume * fixation.FixationPrice, 2, MidpointRounding.ToEven);
        if (newAmount == document.NetAmount) return;

        changeLog.Register(
            document.Key,
            FinancialDocumentChangeLogFields.NetAmount,
            document.NetAmount.ToString("N2", PtBr),
            newAmount.ToString("N2", PtBr),
            userName);

        document.NetAmount = newAmount;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;
    }
}
```

- [ ] **Step 6: Create the receivable generator**

`SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateWashoutReceivableService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Gera o título a RECEBER do produtor na aprovação de um washout: FIRME (dívida real, liberada
/// para baixa manual) e com origem <see cref="FinancialDocumentOrigin.PurchaseContractWashout"/>.
///
/// ENQUEUE-ONLY, como <see cref="FinancialDocumentsGenerateService"/>: faz Add e devolve o
/// documento para o chamador gravar a chave no washout. Chame ANTES de abrir transação (Dapper).
/// </summary>
public class FinancialDocumentsGenerateWashoutReceivableService(
    AppDbContext context,
    DocNumberSequenceService docNumberSequence,
    IBusinessPartnerService businessPartnerService)
{
    public async Task<FinancialDocument> EnqueueAsync(
        PurchaseContract contract, PurchaseContractWashout washout, string userName)
    {
        if (washout.Amount <= 0)
            throw new ApplicationException("Washout sem valor não gera título a receber.");

        var dueDate = washout.DueDate
                      ?? throw new ApplicationException("Informe o vencimento do título a receber do washout.");

        var existing = await context.FinancialDocuments.FirstOrDefaultAsync(x =>
            x.OriginType == FinancialDocumentOrigin.PurchaseContractWashout &&
            x.OriginKey == washout.Key &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (existing is not null) return existing;

        var partner = await businessPartnerService.GetByIdAsync(contract.CardCode);
        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);

        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = await docNumberSequence.GetDocNumber(docNumberKey),
            DocNumberKey = docNumberKey,
            BranchCode = contract.BranchCode,
            Direction = FinancialDirection.Receivable,
            Nature = FinancialDocumentNature.Firm,
            Status = FinancialDocumentStatus.Open,
            CardCode = contract.CardCode,
            CardName = partner?.CardName ?? contract.CardName,
            DocumentDate = DateTime.Now,
            DueDate = dueDate,
            Currency = contract.StandardCurrency ?? CurrencyType.Brl,
            NetAmount = washout.Amount,
            SettledAmount = 0m,
            OriginType = FinancialDocumentOrigin.PurchaseContractWashout,
            OriginKey = washout.Key,
            OriginDocNumber = contract.Code,
            PurchaseContractKey = contract.Key,
            Comments = washout.Reason,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        context.FinancialDocuments.Add(document);
        return document;
    }
}
```

If `DocNumberKey` does not compile on `FinancialDocument`, check how `FinancialDocumentsGenerateService`
sets it, and mirror that exactly.

- [ ] **Step 7: Run the tests and confirm they pass**

1. Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsWashoutTests"`. Expected: all PASS.
2. Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Financials"`. Expected: all PASS.
   The existing generator tests have no washouts, so their amounts do not change.

- [ ] **Step 8: Stage**

```powershell
git add SiagroB1.Application/Services/Financials/FinancialDocumentsAdjustProvisionalService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateWashoutReceivableService.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsWashoutTests.cs
```

---

### Task 4: Shared guard and the Create service

**Files:**
- Create:
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutGuard.cs`
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutCreateService.cs`
  - `SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractsWashoutCreateServiceTests.cs`

**Interfaces:**
- Consumes:
  - From Task 1: `PurchaseContractWashout.CalculateAmount`, `ContractChangeLogFields.DescribeWashout`,
    `NotificationEventType.WashoutCreated`.
  - From Task 2: `PurchaseContractsWashedOutVolumeService` (instance `RecalculateAsync`; static
    `ActiveVolumesAsync` and `ActiveFixedVolumeAsync`), and `WashoutTestData`.
- Produces:
  - `internal static class PurchaseContractsWashoutGuard` with
    `static Task ValidateAsync(AppDbContext context, PurchaseContract contract, PurchaseContractWashout washout, PurchaseContractPriceFixation? fixation)`.
    - The contract MUST be loaded with `.Include(x => x.ShipmentReleases)`.
    - The sums exclude `washout.Key`, so the same guard serves both creation and approval.
  - `PurchaseContractsWashoutCreateService(AppDbContext context, PurchaseContractsWashedOutVolumeService washedOutVolumeService, PurchaseContractsChangeLogService changeLog, ContractNotificationOutboxService notificationOutbox)` with
    `Task<PurchaseContractWashout> ExecuteAsync(Guid purchaseContractKey, PurchaseContractWashout input, string userName)`.
    - The input fields it reads: `PriceFixationKey`, `FixedVolume`, `UnfixedVolume`, `MarketPrice`,
      `PenaltyAmount`, `DueDate`, `Reason`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractsWashoutCreateServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class PurchaseContractsWashoutCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsWashoutCreateService Service() => new(
        _db.Context,
        new PurchaseContractsWashedOutVolumeService(_db.Context),
        new PurchaseContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context));

    /// <summary>Preço fixo: 100.000 kg com a fixação automática de 100.000 @ 2,50.</summary>
    private async Task<(PurchaseContract Contract, PurchaseContractPriceFixation Fixation)> SeedFixAsync(
        Action<PurchaseContract>? tweak = null)
    {
        var contract = WashoutTestData.Contract();
        contract.FixedVolume = 100_000m;
        tweak?.Invoke(contract);
        var fixation = WashoutTestData.Fixation(contract, 100_000m);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    /// <summary>A fixar: 100.000 kg, 30.000 fixados @ 2,50.</summary>
    private async Task<(PurchaseContract Contract, PurchaseContractPriceFixation Fixation)> SeedPafAsync()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        contract.FixedVolume = 30_000m;
        var fixation = WashoutTestData.Fixation(contract, 30_000m);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();
        return (contract, fixation);
    }

    private static PurchaseContractWashout Input(
        Guid? fixationKey, decimal fixedVolume, decimal unfixedVolume = 0m,
        decimal marketPrice = 2.75m, decimal penaltyAmount = 1_000m,
        DateTime? dueDate = null, string? reason = "Produtor sem produto", bool noDueDate = false) => new()
    {
        PriceFixationKey = fixationKey,
        FixedVolume = fixedVolume,
        UnfixedVolume = unfixedVolume,
        MarketPrice = marketPrice,
        PenaltyAmount = penaltyAmount,
        DueDate = noDueDate ? null : dueDate ?? new DateTime(2026, 10, 31),
        Reason = reason,
    };

    private async Task<ApplicationException> RefusesAsync(Guid contractKey, PurchaseContractWashout input, string fragment)
    {
        var error = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(contractKey, input, "tester"));
        Assert.Contains(fragment, error.Message, StringComparison.OrdinalIgnoreCase);
        return error;
    }

    [Fact]
    public async Task Creates_in_approval_with_amount_sequence_log_notification_and_reserved_volume()
    {
        var (contract, fixation) = await SeedFixAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 10_000m), "tester");

        Assert.Equal(PurchaseContractWashoutStatus.InApproval, washout.Status);
        Assert.Equal(1, washout.Sequence);
        Assert.Equal(2.5m, washout.ContractPrice);
        Assert.Equal(3_500m, washout.Amount);
        Assert.Equal("tester", washout.CreatedBy);

        var reloaded = await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key);
        Assert.Equal(10_000m, reloaded.WashedOutVolume);
        Assert.Equal(0m, reloaded.WashedOutUnfixedVolume);

        Assert.Equal(ContractChangeLogFields.Washout, _db.Context.PurchaseContractsChangeLogs.Single().Field);
        Assert.Equal(NotificationEventType.WashoutCreated, _db.Context.NotificationOutboxMessages.Single().EventType);
    }

    [Fact]
    public async Task Second_washout_gets_the_next_sequence()
    {
        var (contract, fixation) = await SeedFixAsync();

        await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 1_000m), "tester");
        var second = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 1_000m), "tester");

        Assert.Equal(2, second.Sequence);
    }

    [Fact]
    public async Task Market_below_contract_charges_only_the_penalty()
    {
        var (contract, fixation) = await SeedFixAsync();

        var washout = await Service().ExecuteAsync(contract.Key, Input(fixation.Key, 10_000m, marketPrice: 2.0m), "tester");

        Assert.Equal(1_000m, washout.Amount);
    }

    [Fact]
    public async Task Zero_amount_needs_no_due_date()
    {
        var (contract, fixation) = await SeedFixAsync();

        var washout = await Service().ExecuteAsync(contract.Key,
            Input(fixation.Key, 10_000m, marketPrice: 2.0m, penaltyAmount: 0m, noDueDate: true), "tester");

        Assert.Equal(0m, washout.Amount);
        Assert.Null(washout.DueDate);
    }

    [Fact]
    public async Task Paf_unfixed_only_washout_keeps_no_fixation_and_charges_only_the_penalty()
    {
        var (contract, fixation) = await SeedPafAsync();

        var washout = await Service().ExecuteAsync(contract.Key,
            Input(fixation.Key, 0m, unfixedVolume: 20_000m, marketPrice: 9m), "tester");

        Assert.Null(washout.PriceFixationKey);
        Assert.Equal(0m, washout.ContractPrice);
        Assert.Equal(1_000m, washout.Amount);
        Assert.Equal(20_000m, (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).WashedOutUnfixedVolume);
    }

    [Fact]
    public async Task Refuses_a_contract_that_is_not_approved()
    {
        var (contract, fixation) = await SeedFixAsync(c => c.Status = ContractStatus.Draft);
        await RefusesAsync(contract.Key, Input(fixation.Key, 1_000m), "aprovado");
    }

    [Fact]
    public async Task Refuses_zero_volume()
    {
        var (contract, fixation) = await SeedFixAsync();
        await RefusesAsync(contract.Key, Input(fixation.Key, 0m), "volume");
    }

    [Fact]
    public async Task Refuses_a_missing_reason()
    {
        var (contract, fixation) = await SeedFixAsync();
        await RefusesAsync(contract.Key, Input(fixation.Key, 1_000m, reason: " "), "motivo");
    }

    [Fact]
    public async Task Refuses_unfixed_volume_on_a_fixed_price_contract()
    {
        var (contract, fixation) = await SeedFixAsync();
        await RefusesAsync(contract.Key, Input(fixation.Key, 0m, unfixedVolume: 1_000m), "preço fixo");
    }

    [Fact]
    public async Task Refuses_fixed_volume_without_a_fixation()
    {
        var (contract, _) = await SeedFixAsync();
        await RefusesAsync(contract.Key, Input(null, 1_000m), "fixação");
    }

    [Fact]
    public async Task Refuses_a_fixation_of_another_contract()
    {
        var (contract, _) = await SeedFixAsync();
        var other = WashoutTestData.Contract();
        other.Code = "PC-002";
        var otherFixation = WashoutTestData.Fixation(other, 100_000m);
        _db.Context.PurchaseContracts.Add(other);
        _db.Context.PurchaseContractsPriceFixations.Add(otherFixation);
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(otherFixation.Key, 1_000m), "não pertence");
    }

    [Fact]
    public async Task Refuses_a_fixation_that_is_not_confirmed()
    {
        var (contract, _) = await SeedPafAsync();
        var pending = WashoutTestData.Fixation(contract, 10_000m, status: PriceFixationStatus.InApproval);
        _db.Context.PurchaseContractsPriceFixations.Add(pending);
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(pending.Key, 1_000m), "confirmada");
    }

    [Fact]
    public async Task Refuses_fixed_volume_above_the_fixation_balance()
    {
        var (contract, fixation) = await SeedPafAsync();
        _db.Context.PurchaseContractsWashouts.Add(WashoutTestData.Washout(contract, fixation, fixedVolume: 25_000m,
            status: PurchaseContractWashoutStatus.Approved));
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 10_000m), "saldo da fixação");
    }

    [Fact]
    public async Task Refuses_unfixed_volume_above_the_volume_available_to_pricing()
    {
        var (contract, fixation) = await SeedPafAsync();
        await RefusesAsync(contract.Key, Input(fixation.Key, 0m, unfixedVolume: 80_000m), "a fixar");
    }

    [Fact]
    public async Task Refuses_volume_above_the_physical_balance()
    {
        var (contract, fixation) = await SeedFixAsync(c => c.AllocatedVolume = 95_000m);
        await RefusesAsync(contract.Key, Input(fixation.Key, 10_000m), "saldo físico");
    }

    [Fact]
    public async Task Refuses_volume_above_the_balance_not_yet_released()
    {
        var (contract, fixation) = await SeedFixAsync();
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 95_000m,
            Status = ReleaseStatus.Actived,
        });
        await _db.Context.SaveChangesAsync();

        await RefusesAsync(contract.Key, Input(fixation.Key, 10_000m), "não liberado");
    }

    [Fact]
    public async Task Refuses_a_positive_amount_without_due_date()
    {
        var (contract, fixation) = await SeedFixAsync();
        await RefusesAsync(contract.Key, Input(fixation.Key, 10_000m, noDueDate: true), "vencimento");
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsWashoutCreateServiceTests"`

Expected: build error. `PurchaseContractsWashoutCreateService` does not exist yet.

- [ ] **Step 3: Create the guard**

`SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutGuard.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Travas do washout, compartilhadas pela criação e pela aprovação (que revalida com o estado de
/// agora: entre o registro e a decisão o contrato pode ter recebido alocação, liberação ou outro
/// washout). Todas as somas EXCLUEM o próprio washout, então o mesmo código serve aos dois.
///
/// Exige o contrato carregado com <c>ShipmentReleases</c> (saldo a liberar) e
/// <c>washout.Amount</c> já calculado.
/// </summary>
internal static class PurchaseContractsWashoutGuard
{
    public static async Task ValidateAsync(
        AppDbContext context,
        PurchaseContract contract,
        PurchaseContractWashout washout,
        PurchaseContractPriceFixation? fixation)
    {
        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException("Contrato precisa estar aprovado para movimentar washout.");

        if (washout.FixedVolume < 0 || washout.UnfixedVolume < 0)
            throw new ApplicationException("Os volumes do washout não podem ser negativos.");

        var total = washout.FixedVolume + washout.UnfixedVolume;
        if (total <= 0)
            throw new ApplicationException("Informe o volume do washout.");

        if (washout.MarketPrice < 0)
            throw new ApplicationException("O preço de mercado não pode ser negativo.");

        if (washout.PenaltyAmount < 0)
            throw new ApplicationException("A multa não pode ser negativa.");

        if (string.IsNullOrWhiteSpace(washout.Reason))
            throw new ApplicationException("Informe o motivo do washout.");

        if (contract.Type != ContractType.ToBeDetermined && washout.UnfixedVolume > 0)
            throw new ApplicationException(
                "Contrato de preço fixo não tem volume a fixar: informe só o volume fixado.");

        if (washout.FixedVolume > 0)
        {
            if (fixation is null)
                throw new ApplicationException("Informe a fixação de preço do volume fixado.");

            if (fixation.PurchaseContractKey != contract.Key)
                throw new ApplicationException("A fixação informada não pertence a este contrato.");

            if (fixation.Status != PriceFixationStatus.Confirmed)
                throw new ApplicationException("A fixação informada não está confirmada.");

            var washedFromFixation = await PurchaseContractsWashedOutVolumeService
                .ActiveFixedVolumeAsync(context, fixation.Key, washout.Key);
            var fixationBalance = fixation.FixationVolume - washedFromFixation;

            if (washout.FixedVolume > fixationBalance)
                throw new ApplicationException(
                    $"Volume fixado excede o saldo da fixação. Disponível: {fixationBalance:N3}, " +
                    $"solicitado: {washout.FixedVolume:N3}.");
        }

        var (activeTotal, activeUnfixed) = await PurchaseContractsWashedOutVolumeService
            .ActiveVolumesAsync(context, contract.Key, washout.Key);

        if (washout.UnfixedVolume > 0)
        {
            var availableToPricing = contract.TotalVolume - contract.FixedVolume - activeUnfixed;
            if (washout.UnfixedVolume > availableToPricing)
                throw new ApplicationException(
                    $"Volume não fixado excede o saldo a fixar. Disponível: {availableToPricing:N3}, " +
                    $"solicitado: {washout.UnfixedVolume:N3}.");
        }

        var physical = decimal.Round(contract.TotalVolume - contract.AllocatedVolume - activeTotal, 3, MidpointRounding.ToEven);
        if (total > physical)
            throw new ApplicationException(
                $"Volume do washout excede o saldo físico do contrato. Disponível: {physical:N3}, " +
                $"solicitado: {total:N3}.");

        var toRelease = decimal.Round(contract.TotalVolume - contract.TotalShipmentReleases - activeTotal, 3, MidpointRounding.ToEven);
        if (total > toRelease)
            throw new ApplicationException(
                $"Volume do washout excede o saldo não liberado do contrato. Disponível: {toRelease:N3}, " +
                $"solicitado: {total:N3}. Encerre ou cancele a liberação antes.");

        if (washout.Amount > 0 && washout.DueDate is null)
            throw new ApplicationException("Informe o vencimento do título a receber.");
    }
}
```

- [ ] **Step 4: Create the service**

`SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutCreateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Registra um washout em aprovação. Já RESERVA o volume (WashedOutVolume), para que duas pessoas
/// não lavem a mesma tonelagem enquanto a decisão não sai. Exposto como a OData action
/// <c>PurchaseContractsWashoutCreate</c>.
/// </summary>
public class PurchaseContractsWashoutCreateService(
    AppDbContext context,
    PurchaseContractsWashedOutVolumeService washedOutVolumeService,
    PurchaseContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task<PurchaseContractWashout> ExecuteAsync(
        Guid purchaseContractKey, PurchaseContractWashout input, string userName)
    {
        var contract = await context.PurchaseContracts
                           .Include(x => x.ShipmentReleases)
                           .FirstOrDefaultAsync(x => x.Key == purchaseContractKey)
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        var fixedVolume = decimal.Round(input.FixedVolume, 3, MidpointRounding.ToEven);

        // Washout só de volume não fixado não prende fixação: sem isso, ele bloquearia à toa o
        // estorno da fixação escolhida na tela.
        var fixation = fixedVolume > 0 && input.PriceFixationKey is { } fixationKey
            ? await context.PurchaseContractsPriceFixations.FirstOrDefaultAsync(x => x.Key == fixationKey)
              ?? throw new NotFoundException("Fixação de preço não encontrada.")
            : null;

        var washout = new PurchaseContractWashout
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            PriceFixationKey = fixation?.Key,
            FixedVolume = fixedVolume,
            UnfixedVolume = decimal.Round(input.UnfixedVolume, 3, MidpointRounding.ToEven),
            ContractPrice = fixation?.FixationPrice ?? 0m,
            MarketPrice = input.MarketPrice,
            PenaltyAmount = decimal.Round(input.PenaltyAmount, 2, MidpointRounding.ToEven),
            DueDate = input.DueDate?.Date,
            Reason = input.Reason?.Trim(),
            Status = PurchaseContractWashoutStatus.InApproval,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName,
            ApprovedBy = null,
            CanceledBy = null,
        };

        washout.Amount = PurchaseContractWashout.CalculateAmount(
            washout.MarketPrice, washout.ContractPrice, washout.FixedVolume, washout.PenaltyAmount);

        await PurchaseContractsWashoutGuard.ValidateAsync(context, contract, washout, fixation);

        var lastSequence = await context.PurchaseContractsWashouts
            .Where(w => w.PurchaseContractKey == contract.Key)
            .MaxAsync(w => (int?)w.Sequence);
        washout.Sequence = (lastSequence ?? 0) + 1;

        context.PurchaseContractsWashouts.Add(washout);

        // Recalcula pelo banco (sem o novo) e soma o novo, que ainda só está rastreado. O RowVersion
        // do contrato e o índice único (contrato, sequência) barram o registro concorrente.
        await washedOutVolumeService.RecalculateAsync(contract);
        contract.WashedOutVolume += washout.FixedVolume + washout.UnfixedVolume;
        contract.WashedOutUnfixedVolume += washout.UnfixedVolume;

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.Washout,
            null,
            ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode),
            userName);

        notificationOutbox.Register(contract, NotificationEventType.WashoutCreated, userName);

        await context.SaveChangesAsync();

        return washout;
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsWashoutCreateServiceTests"`

Expected: all PASS.

- [ ] **Step 6: Stage**

```powershell
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutGuard.cs SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutCreateService.cs SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractsWashoutCreateServiceTests.cs
```

---

### Task 5: Approval and Reject services

**Files:**
- Create:
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutApprovalService.cs`
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutRejectService.cs`
  - `SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractsWashoutApprovalServiceTests.cs`

**Interfaces:**
- Consumes:
  - From Task 4: `PurchaseContractsWashoutGuard.ValidateAsync`.
  - From Task 3: `FinancialDocumentsAdjustProvisionalService`, `FinancialDocumentsGenerateWashoutReceivableService`.
  - From Task 2: `PurchaseContractsWashedOutVolumeService`, and `WashoutTestData`.
- Produces:
  - `PurchaseContractsWashoutApprovalService(AppDbContext context, PurchaseContractsWashedOutVolumeService washedOutVolumeService, PurchaseContractsChangeLogService changeLog, ContractNotificationOutboxService notificationOutbox, FinancialDocumentsAdjustProvisionalService provisionalAdjust, FinancialDocumentsGenerateWashoutReceivableService receivableGenerator)`
    with `Task ExecuteAsync(Guid washoutKey, string? comments, string approvedBy)`.
  - `PurchaseContractsWashoutRejectService(AppDbContext context, PurchaseContractsWashedOutVolumeService washedOutVolumeService, PurchaseContractsChangeLogService changeLog, ContractNotificationOutboxService notificationOutbox)`
    with `Task ExecuteAsync(Guid washoutKey, string? comments, string rejectedBy)`. The comments
    (the rejection reason) are mandatory.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractsWashoutApprovalServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class PurchaseContractsWashoutApprovalServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsWashoutApprovalService Approval() => new(
        _db.Context,
        new PurchaseContractsWashedOutVolumeService(_db.Context),
        new PurchaseContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context),
        new FinancialDocumentsAdjustProvisionalService(
            _db.Context, FinancialDocumentTestServices.Generate(_db.Context), new FinancialDocumentChangeLogService(_db.Context)),
        new FinancialDocumentsGenerateWashoutReceivableService(
            _db.Context, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" })));

    private PurchaseContractsWashoutRejectService Reject() => new(
        _db.Context,
        new PurchaseContractsWashedOutVolumeService(_db.Context),
        new PurchaseContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context));

    /// <summary>
    /// Preço fixo 100.000 kg; fixação automática 100.000 @ 2,50 com provisório de 250.000; um
    /// washout em aprovação de <paramref name="fixedVolume"/> a mercado 2,75 + multa 1.000.
    /// </summary>
    private async Task<(PurchaseContract Contract, PurchaseContractWashout Washout)> SeedFixAsync(
        decimal fixedVolume = 10_000m, decimal marketPrice = 2.75m, decimal penaltyAmount = 1_000m)
    {
        var contract = WashoutTestData.Contract();
        contract.FixedVolume = 100_000m;
        contract.WashedOutVolume = fixedVolume;
        var fixation = WashoutTestData.Fixation(contract, 100_000m);
        var washout = WashoutTestData.Washout(contract, fixation, fixedVolume: fixedVolume,
            marketPrice: marketPrice, penaltyAmount: penaltyAmount);

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.FinancialDocuments.Add(WashoutTestData.Provisional(contract, fixation));
        _db.Context.PurchaseContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();
        return (contract, washout);
    }

    private IQueryable<FinancialDocument> Payables() =>
        _db.Context.FinancialDocuments.AsNoTracking().Where(x => x.Direction == FinancialDirection.Payable);

    private IQueryable<FinancialDocument> Receivables() =>
        _db.Context.FinancialDocuments.AsNoTracking().Where(x => x.Direction == FinancialDirection.Receivable);

    private async Task<PurchaseContractWashout> ReloadAsync(Guid key) =>
        await _db.Context.PurchaseContractsWashouts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Approve_marks_approved_and_records_the_approver()
    {
        var (_, washout) = await SeedFixAsync();

        await Approval().ExecuteAsync(washout.Key, "ok", "diretoria");

        var reloaded = await ReloadAsync(washout.Key);
        Assert.Equal(PurchaseContractWashoutStatus.Approved, reloaded.Status);
        Assert.Equal("diretoria", reloaded.ApprovedBy);
        Assert.Equal("ok", reloaded.ApprovalComments);
        Assert.NotNull(reloaded.ApprovedAt);
        Assert.Contains(_db.Context.NotificationOutboxMessages, m => m.EventType == NotificationEventType.WashoutApproved);
    }

    [Fact]
    public async Task Approve_reduces_the_fixation_provisional_in_place()
    {
        var (_, washout) = await SeedFixAsync();

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        var provisional = Assert.Single(Payables());
        Assert.Equal(FinancialDocumentStatus.Open, provisional.Status);
        Assert.Equal(225_000m, provisional.NetAmount);
        Assert.Equal(FinancialDocumentChangeLogFields.NetAmount, _db.Context.FinancialDocumentChangeLogs.Single().Field);
    }

    [Fact]
    public async Task Approve_generates_the_receivable_and_links_it()
    {
        var (contract, washout) = await SeedFixAsync();

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        var receivable = Assert.Single(Receivables());
        Assert.Equal(FinancialDocumentNature.Firm, receivable.Nature);
        Assert.Equal(3_500m, receivable.NetAmount);
        Assert.Equal(new DateTime(2026, 10, 31), receivable.DueDate);
        Assert.Equal(FinancialDocumentOrigin.PurchaseContractWashout, receivable.OriginType);
        Assert.Equal(washout.Key, receivable.OriginKey);
        Assert.Equal(contract.Key, receivable.PurchaseContractKey);
        Assert.Equal(receivable.Key, (await ReloadAsync(washout.Key)).FinancialDocumentKey);
    }

    [Fact]
    public async Task Approve_without_amount_generates_no_receivable()
    {
        var (_, washout) = await SeedFixAsync(marketPrice: 2.0m, penaltyAmount: 0m);

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        Assert.Empty(Receivables());
        Assert.Null((await ReloadAsync(washout.Key)).FinancialDocumentKey);
    }

    [Fact]
    public async Task Approve_washing_the_whole_fixation_cancels_the_provisional()
    {
        var (_, washout) = await SeedFixAsync(fixedVolume: 100_000m);

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        Assert.Equal(FinancialDocumentStatus.Canceled, Assert.Single(Payables()).Status);
    }

    [Fact]
    public async Task Approve_unfixed_only_washout_does_not_touch_the_provisional()
    {
        var contract = WashoutTestData.Contract(ContractType.ToBeDetermined);
        contract.FixedVolume = 30_000m;
        contract.WashedOutVolume = 20_000m;
        contract.WashedOutUnfixedVolume = 20_000m;
        var fixation = WashoutTestData.Fixation(contract, 30_000m);
        var washout = WashoutTestData.Washout(contract, unfixedVolume: 20_000m);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.FinancialDocuments.Add(WashoutTestData.Provisional(contract, fixation));
        _db.Context.PurchaseContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();

        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        Assert.Equal(75_000m, Assert.Single(Payables()).NetAmount);
    }

    [Fact]
    public async Task Approve_revalidates_the_balance_with_the_current_contract()
    {
        var (contract, washout) = await SeedFixAsync();
        var tracked = await _db.Context.PurchaseContracts.SingleAsync(x => x.Key == contract.Key);
        tracked.AllocatedVolume = 95_000m;
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Approval().ExecuteAsync(washout.Key, null, "diretoria"));

        Assert.Contains("saldo físico", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Approve_refuses_a_washout_that_is_not_in_approval()
    {
        var (_, washout) = await SeedFixAsync();
        await Approval().ExecuteAsync(washout.Key, null, "diretoria");

        await Assert.ThrowsAsync<ApplicationException>(() => Approval().ExecuteAsync(washout.Key, null, "diretoria"));
    }

    [Fact]
    public async Task Reject_requires_a_reason()
    {
        var (_, washout) = await SeedFixAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Reject().ExecuteAsync(washout.Key, "  ", "diretoria"));
    }

    [Fact]
    public async Task Reject_marks_rejected_and_releases_the_volume()
    {
        var (contract, washout) = await SeedFixAsync();

        await Reject().ExecuteAsync(washout.Key, "mercado errado", "diretoria");

        var reloaded = await ReloadAsync(washout.Key);
        Assert.Equal(PurchaseContractWashoutStatus.Rejected, reloaded.Status);
        Assert.Equal("mercado errado", reloaded.ApprovalComments);
        Assert.Equal("diretoria", reloaded.CanceledBy);
        Assert.Equal(0m, (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).WashedOutVolume);
        Assert.Contains(_db.Context.NotificationOutboxMessages, m => m.EventType == NotificationEventType.WashoutRejected);
        Assert.Equal(250_000m, Assert.Single(Payables()).NetAmount);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsWashoutApprovalServiceTests"`

Expected: build error. The two services do not exist yet.

- [ ] **Step 3: Create the approval service**

`SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutApprovalService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Aprova o washout: reduz o provisório a pagar da fixação (no lugar) e gera o título a receber
/// do produtor. O volume já estava reservado desde o registro.
/// </summary>
public class PurchaseContractsWashoutApprovalService(
    AppDbContext context,
    PurchaseContractsWashedOutVolumeService washedOutVolumeService,
    PurchaseContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsAdjustProvisionalService provisionalAdjust,
    FinancialDocumentsGenerateWashoutReceivableService receivableGenerator)
{
    public async Task ExecuteAsync(Guid washoutKey, string? comments, string approvedBy)
    {
        var washout = await context.PurchaseContractsWashouts
                          .Include(x => x.PurchaseContract).ThenInclude(c => c!.ShipmentReleases)
                          .Include(x => x.PriceFixation)
                          .FirstOrDefaultAsync(x => x.Key == washoutKey)
                      ?? throw new NotFoundException("Washout não encontrado.");

        if (washout.Status != PurchaseContractWashoutStatus.InApproval)
            throw new ApplicationException(
                $"Só é possível aprovar washout em aprovação. Status atual: {washout.Status}.");

        var contract = washout.PurchaseContract
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        await PurchaseContractsWashoutGuard.ValidateAsync(context, contract, washout, washout.PriceFixation);

        var previous = ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode);

        washout.Status = PurchaseContractWashoutStatus.Approved;
        washout.ApprovedBy = approvedBy;
        washout.ApprovedAt = DateTime.Now;
        washout.ApprovalComments = comments;
        washout.UpdatedAt = DateTime.Now;
        washout.UpdatedBy = approvedBy;

        // ANTES de abrir a transação: o ajuste (quando não há provisório aberto) e o título a
        // receber buscam número em DOC_NUMBERS por Dapper, numa conexão fora da transação do EF.
        if (washout.FixedVolume > 0 && washout.PriceFixation is { } fixation)
        {
            // O banco ainda não vê este washout como aprovado: soma os OUTROS e subtrai este.
            var otherApproved = await PurchaseContractsWashedOutVolumeService
                .ApprovedFixedVolumeAsync(context, fixation.Key, washout.Key);

            await provisionalAdjust.EnqueueForPurchaseFixationAsync(
                contract, fixation, fixation.FixationVolume - otherApproved - washout.FixedVolume, approvedBy);
        }

        if (washout.Amount > 0)
        {
            var receivable = await receivableGenerator.EnqueueAsync(contract, washout, approvedBy);
            washout.FinancialDocumentKey = receivable.Key;
        }

        await using var transaction = await context.Database.BeginTransactionAsync();

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.Washout,
            previous,
            ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode),
            approvedBy);

        notificationOutbox.Register(contract, NotificationEventType.WashoutApproved, approvedBy);

        // Salva o status ANTES de recalcular (a soma lê o banco). O total não muda — InApproval e
        // Approved reservam igual —, mas manter a ordem evita depender dessa coincidência.
        await context.SaveChangesAsync();

        await washedOutVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
```

- [ ] **Step 4: Create the reject service**

`SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutRejectService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>Rejeita o washout em aprovação e devolve o volume reservado ao saldo.</summary>
public class PurchaseContractsWashoutRejectService(
    AppDbContext context,
    PurchaseContractsWashedOutVolumeService washedOutVolumeService,
    PurchaseContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task ExecuteAsync(Guid washoutKey, string? comments, string rejectedBy)
    {
        if (string.IsNullOrWhiteSpace(comments))
            throw new ApplicationException("Informe o motivo da rejeição.");

        var washout = await context.PurchaseContractsWashouts
                          .Include(x => x.PurchaseContract)
                          .FirstOrDefaultAsync(x => x.Key == washoutKey)
                      ?? throw new NotFoundException("Washout não encontrado.");

        if (washout.Status != PurchaseContractWashoutStatus.InApproval)
            throw new ApplicationException(
                $"Só é possível rejeitar washout em aprovação. Status atual: {washout.Status}.");

        var contract = washout.PurchaseContract
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        var previous = ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode);

        await using var transaction = await context.Database.BeginTransactionAsync();

        washout.Status = PurchaseContractWashoutStatus.Rejected;
        washout.ApprovalComments = comments.Trim();
        washout.CanceledBy = rejectedBy;
        washout.CanceledAt = DateTime.Now;
        washout.UpdatedAt = DateTime.Now;
        washout.UpdatedBy = rejectedBy;

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.Washout,
            previous,
            ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode),
            rejectedBy);

        notificationOutbox.Register(contract, NotificationEventType.WashoutRejected, rejectedBy);

        // Salva ANTES de recalcular: recalcular antes somaria o rejeitado de volta.
        await context.SaveChangesAsync();

        await washedOutVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsWashoutApprovalServiceTests"`

Expected: all PASS.

- [ ] **Step 6: Stage**

```powershell
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutApprovalService.cs SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutRejectService.cs SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractsWashoutApprovalServiceTests.cs
```

---

### Task 6: Reverse service

**Files:**
- Create:
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutReverseService.cs`
  - `SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractsWashoutReverseServiceTests.cs`

**Interfaces:**
- Consumes:
  - From Task 3: `FinancialDocumentsCancelService.EnqueueCancelByKeyAsync` and
    `FinancialDocumentsAdjustProvisionalService`.
  - From Task 2: `PurchaseContractsWashedOutVolumeService` (instance and static
    `ApprovedFixedVolumeAsync`), and `WashoutTestData`.
- Produces: `PurchaseContractsWashoutReverseService(AppDbContext context, PurchaseContractsWashedOutVolumeService washedOutVolumeService, PurchaseContractsChangeLogService changeLog, ContractNotificationOutboxService notificationOutbox, FinancialDocumentsAdjustProvisionalService provisionalAdjust, FinancialDocumentsCancelService financialDocumentsCancel)`
  - `Task ExecuteAsync(Guid washoutKey, string? reason, string reversedBy)`
  - `const string ReceivableCancellationReason = "Washout estornado"`

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractsWashoutReverseServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class PurchaseContractsWashoutReverseServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsWashoutReverseService Service() => new(
        _db.Context,
        new PurchaseContractsWashedOutVolumeService(_db.Context),
        new PurchaseContractsChangeLogService(_db.Context),
        TestNotificationOutbox.For(_db.Context),
        new FinancialDocumentsAdjustProvisionalService(
            _db.Context, FinancialDocumentTestServices.Generate(_db.Context), new FinancialDocumentChangeLogService(_db.Context)),
        FinancialDocumentTestServices.Cancel(_db.Context));

    /// <summary>
    /// Estado de um washout JÁ APROVADO sobre contrato de preço fixo 100.000 @ 2,50: provisório
    /// reduzido (ou cancelado, quando lavou tudo) e título a receber aberto.
    /// </summary>
    private async Task<(PurchaseContract Contract, PurchaseContractWashout Washout, FinancialDocument Receivable)> SeedApprovedAsync(
        decimal fixedVolume = 10_000m, ContractStatus contractStatus = ContractStatus.Approved)
    {
        var contract = WashoutTestData.Contract(status: contractStatus);
        contract.FixedVolume = 100_000m;
        contract.WashedOutVolume = fixedVolume;
        var fixation = WashoutTestData.Fixation(contract, 100_000m);
        var washout = WashoutTestData.Washout(contract, fixation, fixedVolume: fixedVolume,
            status: PurchaseContractWashoutStatus.Approved);

        var provisional = WashoutTestData.Provisional(contract, fixation);
        provisional.NetAmount = decimal.Round((100_000m - fixedVolume) * 2.5m, 2);
        if (fixedVolume >= 100_000m) provisional.Status = FinancialDocumentStatus.Canceled;

        var receivable = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FD-0002",
            CardCode = contract.CardCode,
            Direction = FinancialDirection.Receivable,
            Nature = FinancialDocumentNature.Firm,
            Status = FinancialDocumentStatus.Open,
            DueDate = new DateTime(2026, 10, 31),
            NetAmount = washout.Amount,
            OriginType = FinancialDocumentOrigin.PurchaseContractWashout,
            OriginKey = washout.Key,
            OriginDocNumber = contract.Code,
            PurchaseContractKey = contract.Key,
        };
        washout.FinancialDocumentKey = receivable.Key;

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        _db.Context.FinancialDocuments.AddRange(provisional, receivable);
        _db.Context.PurchaseContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();
        return (contract, washout, receivable);
    }

    private IQueryable<FinancialDocument> OpenPayables() => _db.Context.FinancialDocuments.AsNoTracking()
        .Where(x => x.Direction == FinancialDirection.Payable && x.Status != FinancialDocumentStatus.Canceled);

    private async Task<PurchaseContractWashout> ReloadAsync(Guid key) =>
        await _db.Context.PurchaseContractsWashouts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Reverse_marks_reversed_and_records_the_reason()
    {
        var (_, washout, _) = await SeedApprovedAsync();

        await Service().ExecuteAsync(washout.Key, "lançado errado", "gerente");

        var reloaded = await ReloadAsync(washout.Key);
        Assert.Equal(PurchaseContractWashoutStatus.Reversed, reloaded.Status);
        Assert.Equal("lançado errado", reloaded.ReversalReason);
        Assert.Equal("gerente", reloaded.CanceledBy);
        Assert.Contains(_db.Context.NotificationOutboxMessages, m => m.EventType == NotificationEventType.WashoutReversed);
    }

    [Fact]
    public async Task Reverse_cancels_the_receivable()
    {
        var (_, washout, receivable) = await SeedApprovedAsync();

        await Service().ExecuteAsync(washout.Key, "lançado errado", "gerente");

        var reloaded = await _db.Context.FinancialDocuments.AsNoTracking().SingleAsync(x => x.Key == receivable.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, reloaded.Status);
        Assert.Equal(PurchaseContractsWashoutReverseService.ReceivableCancellationReason, reloaded.CancellationReason);
    }

    [Fact]
    public async Task Reverse_restores_the_provisional_amount()
    {
        var (_, washout, _) = await SeedApprovedAsync();

        await Service().ExecuteAsync(washout.Key, "lançado errado", "gerente");

        Assert.Equal(250_000m, Assert.Single(OpenPayables()).NetAmount);
    }

    [Fact]
    public async Task Reverse_regenerates_the_provisional_canceled_by_a_full_washout()
    {
        var (_, washout, _) = await SeedApprovedAsync(fixedVolume: 100_000m);

        await Service().ExecuteAsync(washout.Key, "lançado errado", "gerente");

        Assert.Equal(250_000m, Assert.Single(OpenPayables()).NetAmount);
    }

    [Fact]
    public async Task Reverse_releases_the_washed_volume()
    {
        var (contract, washout, _) = await SeedApprovedAsync();

        await Service().ExecuteAsync(washout.Key, "lançado errado", "gerente");

        Assert.Equal(0m, (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).WashedOutVolume);
    }

    [Fact]
    public async Task Reverse_refuses_a_receivable_with_settlements()
    {
        var (_, washout, receivable) = await SeedApprovedAsync();
        var tracked = await _db.Context.FinancialDocuments.SingleAsync(x => x.Key == receivable.Key);
        tracked.SettledAmount = 3_500m;
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(washout.Key, "lançado errado", "gerente"));

        Assert.Contains("baixa", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PurchaseContractWashoutStatus.Approved, (await ReloadAsync(washout.Key)).Status);
    }

    [Fact]
    public async Task Reverse_refuses_a_finished_contract()
    {
        var (_, washout, _) = await SeedApprovedAsync(contractStatus: ContractStatus.Finished);

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(washout.Key, "lançado errado", "gerente"));
    }

    [Fact]
    public async Task Reverse_requires_a_reason()
    {
        var (_, washout, _) = await SeedApprovedAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(washout.Key, " ", "gerente"));
    }

    [Fact]
    public async Task Reverse_refuses_a_washout_that_is_not_approved()
    {
        var (_, washout, _) = await SeedApprovedAsync();
        var tracked = await _db.Context.PurchaseContractsWashouts.SingleAsync(x => x.Key == washout.Key);
        tracked.Status = PurchaseContractWashoutStatus.InApproval;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(washout.Key, "lançado errado", "gerente"));
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsWashoutReverseServiceTests"`

Expected: build error. `PurchaseContractsWashoutReverseService` does not exist yet.

- [ ] **Step 3: Create the service**

`SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutReverseService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Estorna um washout aprovado: cancela o título a receber, restaura o provisório da fixação e
/// devolve o volume ao saldo. Recusado se o título já teve baixa — o dinheiro entrou, e o acerto
/// passa pelo estorno da baixa antes.
/// </summary>
public class PurchaseContractsWashoutReverseService(
    AppDbContext context,
    PurchaseContractsWashedOutVolumeService washedOutVolumeService,
    PurchaseContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsAdjustProvisionalService provisionalAdjust,
    FinancialDocumentsCancelService financialDocumentsCancel)
{
    public const string ReceivableCancellationReason = "Washout estornado";

    public async Task ExecuteAsync(Guid washoutKey, string? reason, string reversedBy)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo do estorno.");

        var washout = await context.PurchaseContractsWashouts
                          .Include(x => x.PurchaseContract)
                          .Include(x => x.PriceFixation)
                          .FirstOrDefaultAsync(x => x.Key == washoutKey)
                      ?? throw new NotFoundException("Washout não encontrado.");

        if (washout.Status != PurchaseContractWashoutStatus.Approved)
            throw new ApplicationException(
                $"Só é possível estornar washout aprovado. Status atual: {washout.Status}.");

        var contract = washout.PurchaseContract
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException(
                "Contrato precisa estar aprovado para estornar washout. Reabra o contrato antes.");

        // Primeiro o título: é a trava de baixa, e precisa falhar ANTES de qualquer mutação.
        if (washout.FinancialDocumentKey is { } receivableKey)
            await financialDocumentsCancel.EnqueueCancelByKeyAsync(receivableKey, ReceivableCancellationReason, reversedBy);

        var previous = ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode);

        washout.Status = PurchaseContractWashoutStatus.Reversed;
        washout.ReversalReason = reason.Trim();
        washout.CanceledBy = reversedBy;
        washout.CanceledAt = DateTime.Now;
        washout.UpdatedAt = DateTime.Now;
        washout.UpdatedBy = reversedBy;

        // ANTES da transação: se o provisório tinha sido cancelado por volume zero, o gerador
        // busca número novo por Dapper.
        if (washout.FixedVolume > 0 && washout.PriceFixation is { } fixation)
        {
            var stillWashed = await PurchaseContractsWashedOutVolumeService
                .ApprovedFixedVolumeAsync(context, fixation.Key, washout.Key);

            await provisionalAdjust.EnqueueForPurchaseFixationAsync(
                contract, fixation, fixation.FixationVolume - stillWashed, reversedBy);
        }

        await using var transaction = await context.Database.BeginTransactionAsync();

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.Washout,
            previous,
            ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode),
            reversedBy);

        notificationOutbox.Register(contract, NotificationEventType.WashoutReversed, reversedBy);

        // Salva ANTES de recalcular: recalcular antes somaria o estornado de volta.
        await context.SaveChangesAsync();

        await washedOutVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsWashoutReverseServiceTests"`

Expected: all PASS.

- [ ] **Step 5: Stage**

```powershell
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutReverseService.cs SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractsWashoutReverseServiceTests.cs
```

---

### Task 7: OData surface, DI, approval menu, migrations applied, full suite

**Files:**
- Create:
  - `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutsGetService.cs`
  - `SiagroB1.Web/Controllers/PurchaseContractsWashoutsController.cs`
  - `SiagroB1.Web/Actions/PurchaseContracts/WashoutActionResults.cs`
  - `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsWashoutCreateController.cs`
  - `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsWashoutApprovalController.cs`
  - `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsWashoutRejectController.cs`
  - `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsWashoutReverseController.cs`
  - `SiagroB1.Migrations/CommonContext/*_AddPurchaseContractWashoutApprovalMenu.cs`
  - `SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractWashoutWebTests.cs`
- Modify:
  - `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (entity sets ~37, actions after ~469)
  - `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (~226 and ~524)

**Interfaces:**
- Consumes: every service from Tasks 2-6.
- Produces the contract the frontend plan depends on:
  - **Entity set** `PurchaseContractsWashouts`. `GET odata/PurchaseContractsWashouts` returns only
    the InApproval washouts; `GET odata/PurchaseContractsWashouts({key})` returns a `SingleResult`.
  - **Navigation** `GET odata/PurchaseContracts({key})/Washouts`.
  - **Actions:**
    - `PurchaseContractsWashoutCreate(PurchaseContractKey: Guid, Washout: PurchaseContractWashout)`
    - `PurchaseContractsWashoutApproval(Key: Guid, Comments: string)`
    - `PurchaseContractsWashoutReject(Key: Guid, Comments: string)`
    - `PurchaseContractsWashoutReverse(Key: Guid, Reason: string)`
  - **Menu** Key `purchaseContractsWashoutApproval`, under `purchases`, Order 13, linked to ADMIN.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractWashoutWebTests.cs`:

```csharp
using Microsoft.AspNetCore.OData.Results;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.Controllers;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

public class PurchaseContractWashoutWebTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void The_entity_set_is_exposed()
    {
        Assert.NotNull(BuildModel().EntityContainer.FindEntitySet("PurchaseContractsWashouts"));
    }

    [Fact]
    public void The_contract_exposes_the_washed_volumes_and_the_navigation()
    {
        var contractType = BuildModel().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == "PurchaseContract");

        Assert.NotNull(contractType.FindProperty("WashedOutVolume"));
        Assert.NotNull(contractType.FindProperty("WashedOutUnfixedVolume"));
        Assert.NotNull(contractType.FindProperty("Washouts"));
    }

    [Theory]
    [InlineData("PurchaseContractsWashoutCreate", "PurchaseContractKey,Washout")]
    [InlineData("PurchaseContractsWashoutApproval", "Key,Comments")]
    [InlineData("PurchaseContractsWashoutReject", "Key,Comments")]
    [InlineData("PurchaseContractsWashoutReverse", "Key,Reason")]
    public void The_actions_declare_their_parameters(string action, string parameters)
    {
        var edmAction = BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == action);

        foreach (var parameter in parameters.Split(','))
            Assert.Contains(edmAction.Parameters, p => p.Name == parameter);
    }

    [Fact]
    public async Task The_pending_queue_lists_only_washouts_in_approval()
    {
        var contract = WashoutTestData.Contract();
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsWashouts.AddRange(
            WashoutTestData.Washout(contract, unfixedVolume: 1m, sequence: 1),
            WashoutTestData.Washout(contract, unfixedVolume: 1m, status: PurchaseContractWashoutStatus.Approved, sequence: 2));
        await _db.Context.SaveChangesAsync();

        var pending = new PurchaseContractsWashoutsGetService(_db.Context).QueryPending().ToList();

        Assert.Equal(PurchaseContractWashoutStatus.InApproval, Assert.Single(pending).Status);
    }

    [Fact]
    public async Task Get_by_key_returns_a_single_result_so_expand_works()
    {
        var contract = WashoutTestData.Contract();
        var washout = WashoutTestData.Washout(contract, unfixedVolume: 1m);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsWashouts.Add(washout);
        await _db.Context.SaveChangesAsync();

        var result = new PurchaseContractsWashoutsController(new PurchaseContractsWashoutsGetService(_db.Context))
            .GetByKey(washout.Key);

        var single = Assert.IsType<SingleResult<PurchaseContractWashout>>(result);
        Assert.Equal(washout.Key, Assert.Single(single.Queryable).Key);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractWashoutWebTests"`

Expected: build error. The get service and the controller do not exist yet.

- [ ] **Step 3: Create the get service and the entity-set controller**

`SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutsGetService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Leituras do washout. Devolve IQueryable sem Include: o [EnableQuery] compõe $expand/$select
/// sobre a consulta (PurchaseContract, PriceFixation, FinancialDocument).
/// </summary>
public class PurchaseContractsWashoutsGetService(AppDbContext context)
{
    public IQueryable<PurchaseContractWashout> QueryByContract(Guid contractKey) =>
        context.PurchaseContractsWashouts.Where(x => x.PurchaseContractKey == contractKey).AsNoTracking();

    /// <summary>Fila de aprovação: washouts em aprovação de todos os contratos.</summary>
    public IQueryable<PurchaseContractWashout> QueryPending() =>
        context.PurchaseContractsWashouts
            .Where(x => x.Status == PurchaseContractWashoutStatus.InApproval)
            .AsNoTracking();

    public IQueryable<PurchaseContractWashout> QueryByKey(Guid key) =>
        context.PurchaseContractsWashouts.Where(x => x.Key == key).AsNoTracking();
}
```

`SiagroB1.Web/Controllers/PurchaseContractsWashoutsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Leitura do washout. Toda mutação é OData action (PurchaseContractsWashout*). As rotas são
/// declaradas à mão: navegação e GET por chave dão 404 sem isso.
/// </summary>
public class PurchaseContractsWashoutsController(PurchaseContractsWashoutsGetService getService) : ODataController
{
    [HttpGet("odata/PurchaseContracts({key:guid})/Washouts")]
    [HttpGet("odata/PurchaseContracts/{key:guid}/Washouts")]
    [EnableQuery]
    public ActionResult<IQueryable<PurchaseContractWashout>> GetWashouts([FromRoute] Guid key) =>
        Ok(getService.QueryByContract(key));

    /// <summary>Fila da tela "Aprovação de Washouts".</summary>
    [HttpGet("odata/PurchaseContractsWashouts")]
    [EnableQuery]
    public ActionResult<IQueryable<PurchaseContractWashout>> GetPending() =>
        Ok(getService.QueryPending());

    /// <summary>
    /// SingleResult sobre IQueryable: materializado, o $expand seria aceito e voltaria nulo
    /// (bug do Motivo em branco da Conferência, 14/09/2026).
    /// </summary>
    [HttpGet("odata/PurchaseContractsWashouts({key:guid})")]
    [HttpGet("odata/PurchaseContractsWashouts/{key:guid}")]
    [EnableQuery]
    public SingleResult<PurchaseContractWashout> GetByKey([FromRoute] Guid key) =>
        SingleResult.Create(getService.QueryByKey(key));
}
```

- [ ] **Step 4: Create the action controllers**

`SiagroB1.Web/Actions/PurchaseContracts/WashoutActionResults.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

/// <summary>
/// Mapeamento de erro das actions de washout. Regra de negócio é 400 com a mensagem pura no
/// corpo (a tela a exibe); não previsto continua 500.
/// </summary>
internal static class WashoutActionResults
{
    public static IActionResult FromException(ControllerBase controller, Exception e) => e switch
    {
        NotFoundException or KeyNotFoundException => controller.NotFound(e.Message),
        DbUpdateConcurrencyException => controller.BadRequest(
            "O contrato foi alterado por outra operação. Recarregue a tela e tente de novo."),
        DefaultException or BusinessException or ApplicationException => controller.BadRequest(e.Message),
        _ => controller.StatusCode(500, e.Message),
    };
}
```

`SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsWashoutCreateController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsWashoutCreateController(
    PurchaseContractsWashoutCreateService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsWashoutCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // ODataActionParameters vem NULO quando o corpo não casa com o EDM.
            if (parameters is null
                || !parameters.TryGetValue("PurchaseContractKey", out var keyObj) || keyObj is not Guid contractKey)
                return BadRequest("Missing required parameters");

            if (!parameters.TryGetValue("Washout", out var washoutObj) || washoutObj is not PurchaseContractWashout washout)
                return BadRequest("Missing washout payload");

            await service.ExecuteAsync(contractKey, washout, User.Identity?.Name ?? "Unknown");

            // Sem corpo, como a criação de fixação: a tela recarrega a lista.
            return Ok();
        }
        catch (Exception e)
        {
            return WashoutActionResults.FromException(this, e);
        }
    }
}
```

`SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsWashoutApprovalController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsWashoutApprovalController(
    PurchaseContractsWashoutApprovalService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsWashoutApproval")]
    public async Task<IActionResult> ApproveAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is not Guid key)
                return BadRequest("Missing required parameters");

            // Parâmetro string do OData chega null: nunca .ToString() direto.
            parameters.TryGetValue("Comments", out var commentsObj);

            await service.ExecuteAsync(key, commentsObj as string, User.Identity?.Name ?? "Unknown");
            return Ok();
        }
        catch (Exception e)
        {
            return WashoutActionResults.FromException(this, e);
        }
    }
}
```

`SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsWashoutRejectController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsWashoutRejectController(
    PurchaseContractsWashoutRejectService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsWashoutReject")]
    public async Task<IActionResult> RejectAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is not Guid key)
                return BadRequest("Missing required parameters");

            parameters.TryGetValue("Comments", out var commentsObj);

            await service.ExecuteAsync(key, commentsObj as string, User.Identity?.Name ?? "Unknown");
            return Ok();
        }
        catch (Exception e)
        {
            return WashoutActionResults.FromException(this, e);
        }
    }
}
```

`SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsWashoutReverseController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsWashoutReverseController(
    PurchaseContractsWashoutReverseService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsWashoutReverse")]
    public async Task<IActionResult> ReverseAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is not Guid key)
                return BadRequest("Missing required parameters");

            parameters.TryGetValue("Reason", out var reasonObj);

            await service.ExecuteAsync(key, reasonObj as string, User.Identity?.Name ?? "Unknown");
            return Ok();
        }
        catch (Exception e)
        {
            return WashoutActionResults.FromException(this, e);
        }
    }
}
```

If an existing action controller parses `Key` as a string (`Guid.Parse(keyObj.ToString())`) because
the EDM `Guid` does not arrive as `Guid`, verify it with the smoke test in Step 10. If the key
arrives as a string, switch these four controllers to
`Guid.TryParse(keyObj?.ToString(), out var key)`.

- [ ] **Step 5: Register the EDM and DI**

In `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, right after `modelBuilder.EntitySet<PurchaseContractComment>("PurchaseContractsComments");`, add:

```csharp
        modelBuilder.EntitySet<PurchaseContractWashout>("PurchaseContractsWashouts");
```

Right after the `priceFixationCancel` action block (the one ending `priceFixationCancel.Returns<IActionResult>();`), add:

```csharp
        // Washout do contrato de compra. A criação recebe a entidade inteira, como a fixação; nas
        // demais, Key é a chave do WASHOUT, não a do contrato.
        var washoutCreate = modelBuilder.Action("PurchaseContractsWashoutCreate");
        washoutCreate.Parameter<Guid>("PurchaseContractKey");
        washoutCreate.EntityParameter<PurchaseContractWashout>("Washout");
        washoutCreate.Returns<IActionResult>();

        var washoutApproval = modelBuilder.Action("PurchaseContractsWashoutApproval");
        washoutApproval.Parameter<Guid>("Key");
        washoutApproval.Parameter<string>("Comments").Nullable = true;
        washoutApproval.Returns<IActionResult>();

        var washoutReject = modelBuilder.Action("PurchaseContractsWashoutReject");
        washoutReject.Parameter<Guid>("Key");
        washoutReject.Parameter<string>("Comments").Nullable = true;
        washoutReject.Returns<IActionResult>();

        var washoutReverse = modelBuilder.Action("PurchaseContractsWashoutReverse");
        washoutReverse.Parameter<Guid>("Key");
        washoutReverse.Parameter<string>("Reason").Nullable = true;
        washoutReverse.Returns<IActionResult>();
```

In `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, right after `services.AddScoped<PurchaseContractsPriceFixationsUpdateService>();`, add:

```csharp
        services.AddScoped<PurchaseContractsWashedOutVolumeService>();
        services.AddScoped<PurchaseContractsWashoutCreateService>();
        services.AddScoped<PurchaseContractsWashoutApprovalService>();
        services.AddScoped<PurchaseContractsWashoutRejectService>();
        services.AddScoped<PurchaseContractsWashoutReverseService>();
        services.AddScoped<PurchaseContractsWashoutsGetService>();
```

Right after `services.AddScoped<FinancialDocumentChangeLogService>();`, add:

```csharp
        services.AddScoped<FinancialDocumentsAdjustProvisionalService>();
        services.AddScoped<FinancialDocumentsGenerateWashoutReceivableService>();
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractWashoutWebTests"`

Expected: all PASS.

- [ ] **Step 7: Create the approval menu migration**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef migrations add AddPurchaseContractWashoutApprovalMenu --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o CommonContext
```

Replace the generated class (keep the generated class name and namespace) with:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu da fila de aprovação de washouts do contrato de compra, no grupo "Compras". Order 13
    /// porque 1 a 12 já estão ocupados em "purchases" (10 a 12 são da Conferência de Saldo).
    ///
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddPurchaseContractWashoutApprovalMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "purchaseContractsWashoutApproval", "Aprovação de Washouts", "sap-icon://folder-blank", true, false, 13, "purchases" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "5E6F7A8B-9C0D-4E1F-8A2B-3C4D5E6F7A95", "ADMIN", "purchaseContractsWashoutApproval" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValue: "5E6F7A8B-9C0D-4E1F-8A2B-3C4D5E6F7A95");

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: "purchaseContractsWashoutApproval");
        }
    }
}
```

Keep the generated `.Designer.cs` as it is. Check that the regenerated `CommonDbContextModelSnapshot`
has no unrelated diff.

- [ ] **Step 8: Full build, full suite, no pending model changes**

```powershell
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef migrations has-pending-model-changes --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Expected:
- Build: 0 errors.
- Tests: all green, with the total count reported. Any failure outside the washout tests must be
  investigated and fixed, not skipped.
- `has-pending-model-changes`: "No changes" for both contexts.

- [ ] **Step 9: Apply the migrations to localhost**

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef migrations list --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

1. Confirm that the output reaches the localhost `IDX_SIAGRO_DEV` database and lists
   `AddPurchaseContractWashouts` as `(Pending)` and nothing unexpected. If the target is not localhost,
   STOP and report.
2. Apply:

```powershell
dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef migrations list --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef database update --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

3. Expected: both updates finish with "Done."

- [ ] **Step 10: Smoke the API**

1. Start the Web app in the background: `dotnet run --project SiagroB1.Web --launch-profile yktb`.
   It refuses to start if a migration is pending.
2. Unauthenticated calls through the Gateway are rejected, so hit Web directly on
   `http://localhost:50000`:
   - `GET /odata/$metadata`: contains `PurchaseContractsWashouts` and the 4 actions.
   - `GET /odata/PurchaseContractsWashouts`: 200 with `{"value":[]}`, or with the pending rows.
3. Stop the process you started, and only that one.

- [ ] **Step 11: Stage**

```powershell
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsWashoutsGetService.cs SiagroB1.Web/Controllers/PurchaseContractsWashoutsController.cs SiagroB1.Web/Actions/PurchaseContracts SiagroB1.Migrations/CommonContext SiagroB1.Application.Tests/PurchaseContracts/Washouts/PurchaseContractWashoutWebTests.cs
```

