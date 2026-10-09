# Recusa de carga com NF-e de entrada própria — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Na filial que emite NF-e pelo Siagro, a recusa de carga passa a funcionar em dois tempos: registra devoluções Pendentes com NF-e própria de entrada e só aplica os efeitos (saldo, armazém, transbordo) quando a última NF-e é autorizada.

**Architecture:** `ShipmentLoadsRefuseService` decide o modo pela regra de NF-e da filial da carga. No modo diferido, grava uma `ShipmentLoadRefusal` `Pending` e cria uma devolução `IsNfeReturn` por documento, montada pelo mesmo construtor do "Devolver" (`SalesInvoiceNfeReturnBuilder`). A emissão existente confirma cada devolução; `SalesInvoicesConfirmService` chama `ShipmentLoadRefusalCompleteService`, que, ao ver todas as devoluções da recusa confirmadas, aplica os efeitos do destino pelo serviço compartilhado `ShipmentLoadRefusalEffectsService` (extraído da recusa síncrona) e conclui a recusa. A carga fica em `ShipmentLoadStatus.RefusalPending` enquanto a recusa estiver pendente.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes com InMemory), ASP.NET Core OData, xUnit; OpenUI5 + TypeScript.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-09-shipment-load-refusal-nfe-design.md`

## Global Constraints

- Identificadores em inglês; texto que o usuário lê em pt-BR (labels, mensagens de negócio). Comentários em pt-BR.
- Enums gravados como int: valores novos **sempre no fim** — `ShipmentLoadStatus.RefusalPending = 9`, `ShipmentLoadMovementType.RefusalCancelled = 25`.
- Filial **sem** a regra de NF-e ativa: comportamento idêntico ao de hoje; nenhum registro em `SHIPMENT_LOAD_REFUSALS`.
- Todo serviço interno chamado de dentro de outra transação roda em `CommitMode.Deferred` (`UnitOfWork.CommitAsync` não é aninhável).
- Uma migration só, aditiva, no `AppDbContext`: `AddShipmentLoadRefusals`. Sem backfill. Commit com trailer `DB: AddShipmentLoadRefusals`.
- Commits: `tipo(escopo): descrição pt-BR`, escopo `shipment` (ou `invoice` quando for só documento de saída), terminando com `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. Branch `feature/load-refusal-nfe` nos dois repos. Nunca push. `git add` de todo arquivo novo logo após criá-lo.
- Testes: `dotnet test SiagroB1.Application.Tests --filter <Classe> --nologo` (rodar de `siagro-b1-backend/`). Ver o vermelho antes do verde.
- Frontend: `npm run ts-typecheck` e `npm run lint` (de `siagro-b1-frontend/`).
- Mensagem da trava da carga (literal): `"A carga {Code} tem uma recusa aguardando NF-e de entrada: emita as NF-e ou cancele a recusa."`

## Review Focus

1. **Romaneios da carga voltando à Montagem durante a recusa pendente** — o recálculo projeta `Confirmed` nos romaneios de qualquer carga fora da lista "encerrada"; `RefusalPending` precisa projetar `Invoiced` (teste na Task 2).
2. **Transbordo na conclusão com a carga travada** — `EnsureLoadAcceptsTransshipment` passa a recusar `RefusalPending`; a conclusão NÃO pode chamá-lo, só `EnsureIsLastAsync` (teste na Task 7).
3. **NF-e autorizada de uma devolução cancelada pelo 2b com a recusa ainda pendente** — o cancelamento pós-SEFAZ não pode ser barrado pela trava da devolução avulsa, e a conclusão soma só as devoluções Confirmadas (teste na Task 8).
4. **POST/PATCH tentando gravar `ShipmentLoadRefusalKey`** — a criação zera e o PATCH restaura (teste na Task 1).
5. **Duplo clique em "Cancelar recusa" / recusa já concluída** — o serviço recusa recusa não-`Pending` com mensagem de negócio (teste na Task 8).

---

### Task 1: Modelo — recusa, chave na devolução, situação e movimento

**Files:**
- Create: `SiagroB1.Domain/Enums/ShipmentLoadRefusalStatus.cs`
- Create: `SiagroB1.Domain/Entities/ShipmentLoadRefusal.cs`
- Modify: `SiagroB1.Domain/Enums/ShipmentLoadStatus.cs` (append `RefusalPending = 9`)
- Modify: `SiagroB1.Domain/Enums/ShipmentLoadMovementType.cs` (append `RefusalCancelled = 25`)
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs` (propriedade `ShipmentLoadRefusalKey`)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSet + configuração)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs` (`RestoreIssuanceFields`, `ResetIssuanceFields`)
- Create (gerado): `SiagroB1.Migrations/AppContext/<timestamp>_AddShipmentLoadRefusals.cs` (+ Designer, snapshot)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadRefusalModelTests.cs`

**Interfaces:**
- Produces: `ShipmentLoadRefusal { Guid? Key; Guid ShipmentLoadKey; RefusalDestination Destination; string? DestinationWarehouseCode; string? DestinationWarehouseName; string Reason; ShipmentLoadRefusalStatus Status; DateTime CreatedAt; string? CreatedBy; DateTime? CompletedAt; string? CompletedBy; DateTime? CancelledAt; string? CancelledBy }`; `AppDbContext.ShipmentLoadRefusals`; `SalesInvoice.ShipmentLoadRefusalKey (Guid?)`; `ShipmentLoadStatus.RefusalPending`; `ShipmentLoadMovementType.RefusalCancelled`; `ShipmentLoadRefusalStatus { Pending = 0, Completed = 1, Cancelled = 2 }`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadRefusalModelTests
{
    private static AppDbContext ModelOnlyContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void Refusal_maps_to_its_own_table_with_noaction_fks()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ShipmentLoadRefusal))!;

        Assert.Equal("SHIPMENT_LOAD_REFUSALS", entity.GetTableName());
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
    }

    [Fact]
    public void Only_one_pending_refusal_per_load()
    {
        using var context = ModelOnlyContext();
        var index = context.Model.FindEntityType(typeof(ShipmentLoadRefusal))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(ShipmentLoadRefusal.ShipmentLoadKey)]));

        Assert.True(index.IsUnique);
        Assert.Equal("[Status] = 0", index.GetFilter());
    }

    [Fact]
    public void Sales_invoice_points_to_the_refusal_with_noaction()
    {
        using var context = ModelOnlyContext();
        var fk = context.Model.FindEntityType(typeof(SalesInvoice))!
            .GetForeignKeys()
            .Single(f => f.Properties.Single().Name == nameof(SalesInvoice.ShipmentLoadRefusalKey));

        Assert.Equal(typeof(ShipmentLoadRefusal), fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
    }

    [Fact]
    public void Enum_values_are_appended()
    {
        Assert.Equal(9, (int)ShipmentLoadStatus.RefusalPending);
        Assert.Equal(25, (int)ShipmentLoadMovementType.RefusalCancelled);
    }

    [Fact]
    public void Creation_never_keeps_a_refusal_key_from_the_body()
    {
        var invoice = new SalesInvoice { ShipmentLoadRefusalKey = Guid.NewGuid() };

        SalesInvoiceNfeLock.ResetIssuanceFields(invoice);

        Assert.Null(invoice.ShipmentLoadRefusalKey);
    }

    [Fact]
    public async Task Patch_cannot_change_the_refusal_key()
    {
        var db = TestDb.CreateUnitOfWork();
        var original = Guid.NewGuid();
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", BranchCode = "01", InvoiceType = SalesInvoiceType.Return,
            InvoiceStatus = InvoiceStatus.Pending, ShipmentLoadRefusalKey = original,
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        invoice.ShipmentLoadRefusalKey = Guid.NewGuid();
        SalesInvoiceNfeLock.RestoreIssuanceFields(db.Context.Entry(invoice));

        Assert.Equal(original, invoice.ShipmentLoadRefusalKey);
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentLoadRefusalModelTests --nologo`
Expected: build error (`ShipmentLoadRefusal`, `ShipmentLoadRefusalKey`, `RefusalPending`, `RefusalCancelled` não existem).

- [ ] **Step 3: Implement**

`SiagroB1.Domain/Enums/ShipmentLoadRefusalStatus.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação da recusa de carga em dois tempos (spec 2026-10-09): a filial que emite NF-e pelo Siagro só conclui a
/// recusa com as NF-e de entrada autorizadas. Gravado como int — valores novos sempre no fim.
/// </summary>
public enum ShipmentLoadRefusalStatus
{
    /// <summary>Devoluções criadas, aguardando as NF-e de entrada. A carga fica travada.</summary>
    Pending = 0,

    /// <summary>Todas as devoluções confirmadas e os efeitos do destino aplicados.</summary>
    Completed = 1,

    /// <summary>Cancelada antes de qualquer NF-e autorizada; as devoluções pendentes foram canceladas.</summary>
    Cancelled = 2,
}
```

`SiagroB1.Domain/Entities/ShipmentLoadRefusal.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Recusa de carga registrada na filial que emite NF-e pelo Siagro (spec 2026-10-09). Guarda o destino escolhido
/// até a última NF-e de entrada ser autorizada — é a confirmação dessa devolução que aplica os efeitos.
/// </summary>
/// <remarks>
/// Uma recusa <see cref="ShipmentLoadRefusalStatus.Pending"/> por carga, no máximo (índice único filtrado em
/// <c>AppDbContext</c>). As devoluções apontam para cá por <see cref="SalesInvoice.ShipmentLoadRefusalKey"/>.
/// Não herda <c>BaseEntity</c>, como <see cref="ShipmentLoadTransshipment"/>: é registro filho da carga.
/// </remarks>
[Table("SHIPMENT_LOAD_REFUSALS")]
public class ShipmentLoadRefusal
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    public RefusalDestination Destination { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? DestinationWarehouseCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? DestinationWarehouseName { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public required string Reason { get; set; }

    public ShipmentLoadRefusalStatus Status { get; set; } = ShipmentLoadRefusalStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }

    public DateTime? CompletedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CompletedBy { get; set; }

    public DateTime? CancelledAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CancelledBy { get; set; }
}
```

`ShipmentLoadStatus.cs` — append after `Discharged = 8,`:

```csharp
    /// <summary>
    /// Recusa registrada na filial que emite NF-e pelo Siagro, aguardando as NF-e de entrada das devoluções
    /// (spec 2026-10-09). Vence todas as outras situações no recálculo e trava faturar, nova recusa, ticket,
    /// transbordo e campos fiscais.
    /// </summary>
    RefusalPending = 9
```

`ShipmentLoadMovementType.cs` — `DischargeUndone = 24` ganha vírgula e entra:

```csharp
    RefusalCancelled = 25        // Recusa aguardando NF-e cancelada — devoluções pendentes canceladas, carga destravada
```

`SalesInvoice.cs` — junto de `IsNfeReturn`:

```csharp
    /// <summary>
    /// Recusa de carga que gerou esta devolução (spec 2026-10-09). Só a recusa em dois tempos grava; a criação
    /// pela API zera e o PATCH restaura (<c>SalesInvoiceNfeLock</c>). Sem navegação de propósito: não expõe a
    /// recusa no EDM do documento.
    /// </summary>
    public Guid? ShipmentLoadRefusalKey { get; set; }
```

`AppDbContext.cs` — DbSet ao lado de `ShipmentLoadsTransshipments`:

```csharp
    public DbSet<ShipmentLoadRefusal> ShipmentLoadRefusals { get; set; }
```

e, logo depois da configuração de `ShipmentLoadTransshipment`:

```csharp
        // Recusa em dois tempos (spec 2026-10-09). NoAction nas duas FKs: a recusa é histórico da carga e das
        // devoluções. Índice único FILTRADO: no máximo uma recusa Pendente por carga — a trava de situação da carga
        // é a primeira linha, este índice a segunda. ⚠️ índice filtrado exige QUOTED_IDENTIFIER ON no sqlcmd (-I).
        modelBuilder.Entity<ShipmentLoadRefusal>()
            .HasOne(x => x.ShipmentLoad)
            .WithMany()
            .HasForeignKey(x => x.ShipmentLoadKey)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ShipmentLoadRefusal>()
            .HasIndex(x => x.ShipmentLoadKey)
            .IsUnique()
            .HasFilter($"[Status] = {(int)ShipmentLoadRefusalStatus.Pending}");

        modelBuilder.Entity<SalesInvoice>()
            .HasOne<ShipmentLoadRefusal>()
            .WithMany()
            .HasForeignKey(x => x.ShipmentLoadRefusalKey)
            .OnDelete(DeleteBehavior.NoAction);
```

`SalesInvoiceNfeLock.cs`:
- em `RestoreIssuanceFields`, depois da linha do `IsNfeReturn`:

```csharp
        // O vínculo com a recusa de carga nasce na recusa e nunca muda pela API.
        entry.Property(nameof(SalesInvoice.ShipmentLoadRefusalKey)).CurrentValue =
            entry.OriginalValues[nameof(SalesInvoice.ShipmentLoadRefusalKey)];
```

- em `ResetIssuanceFields`, junto dos demais campos:

```csharp
        invoice.ShipmentLoadRefusalKey = null;
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentLoadRefusalModelTests --nologo`
Expected: PASS (6 testes).

- [ ] **Step 5: Scaffold the migration**

Run (de `siagro-b1-backend/`): `dotnet ef migrations add AddShipmentLoadRefusals --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext`
Expected: arquivo novo em `SiagroB1.Migrations/AppContext/`. Conferir no `Up()`: `CreateTable("SHIPMENT_LOAD_REFUSALS", ...)`, `AddColumn<Guid>("ShipmentLoadRefusalKey", "SALES_INVOICES", nullable: true)`, `CreateIndex(... unique: true, filter: "[Status] = 0")`, os dois `AddForeignKey` sem `onDelete: Cascade`. Nada além disso (se aparecer mudança alheia, é drift do snapshot: parar e reportar).

- [ ] **Step 6: Build and run the whole suite**

Run: `dotnet build SiagroB1.sln --nologo` e `dotnet test SiagroB1.Application.Tests --nologo`
Expected: 0 erros; todos verdes.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Domain/Enums/ShipmentLoadRefusalStatus.cs SiagroB1.Domain/Entities/ShipmentLoadRefusal.cs SiagroB1.Migrations/AppContext SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadRefusalModelTests.cs
git add SiagroB1.Domain/Enums/ShipmentLoadStatus.cs SiagroB1.Domain/Enums/ShipmentLoadMovementType.cs SiagroB1.Domain/Entities/SalesInvoice.cs SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs
git commit -m "feat(shipment): modelo da recusa de carga aguardando NF-e

Tabela SHIPMENT_LOAD_REFUSALS, vínculo da devolução com a recusa e a
situação RefusalPending da carga, base da recusa em dois tempos na filial
que emite NF-e pelo Siagro.

DB: AddShipmentLoadRefusals

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Situação `RefusalPending` no recálculo da carga

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRecalculateInvoicedService.cs` (`ResolveStatus` :259, `RecalculateAsync` :99-182)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadRefusalPendingStatusTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces: `ResolveStatus(decimal total, decimal invoiced, decimal returned, decimal transshipped, bool hasOpenTransshipment, bool hasPendingRefusal = false)`; `RecalculateAsync` grava `RefusalPending` quando há recusa `Pending` da carga.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadRefusalPendingStatusTests
{
    [Theory]
    [InlineData(40_000, 40_000, 0, 0, false)]  // seria Invoiced
    [InlineData(40_000, 10_000, 0, 0, false)]  // seria PartiallyInvoiced
    [InlineData(40_000, 0, 0, 40_000, true)]   // seria InTransshipment
    public void Pending_refusal_wins_every_other_status(
        decimal total, decimal invoiced, decimal returned, decimal transshipped, bool openTransshipment)
    {
        Assert.Equal(
            ShipmentLoadStatus.RefusalPending,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
                total, invoiced, returned, transshipped, openTransshipment, hasPendingRefusal: true));
    }

    [Fact]
    public void Without_pending_refusal_nothing_changes()
    {
        Assert.Equal(
            ShipmentLoadStatus.Invoiced,
            ShipmentLoadsRecalculateInvoicedService.ResolveStatus(40_000m, 40_000m, 0m, 0m, false));
    }

    private static async Task<(Infra.UnitOfWork Db, ShipmentLoad Load, StorageTransaction Shipment)> SeedInvoicedLoadAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000001", BranchCode = "01", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            TotalQuantity = 30_000m, Status = ShipmentLoadStatus.Invoiced,
        };
        var shipment = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "R1", CardCode = "C1", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01", BranchCode = "01", GrossWeight = 30_000m, NetWeight = 30_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Invoiced, ShipmentLoadKey = load.Key,
        };
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", BranchCode = "01", ShipmentLoadKey = load.Key,
            InvoiceType = SalesInvoiceType.Normal, InvoiceStatus = InvoiceStatus.Confirmed,
        };
        invoice.AddItem(new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 30_000m });

        db.Context.ShipmentLoads.Add(load);
        db.Context.StorageTransactions.Add(shipment);
        db.Context.SalesInvoices.Add(invoice);
        db.Context.ShipmentLoadRefusals.Add(new ShipmentLoadRefusal
        {
            ShipmentLoadKey = load.Key, Destination = RefusalDestination.Rebilling, Reason = "Recusa",
        });
        await db.SaveChangesAsync();

        return (db, load, shipment);
    }

    [Fact]
    public async Task Recalculate_writes_refusal_pending_and_keeps_the_shipments_out_of_assembly()
    {
        var (db, load, shipment) = await SeedInvoicedLoadAsync();

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, load.Key, excludedInvoiceKeys: null);
        await db.SaveChangesAsync();

        Assert.Equal(ShipmentLoadStatus.RefusalPending,
            (await db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key)).Status);
        // Review Focus 1: Confirmed devolveria o romaneio à Montagem com a mercadoria faturada.
        Assert.Equal(StorageTransactionsStatus.Invoiced,
            (await db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == shipment.Key)).TransactionStatus);
    }

    [Fact]
    public async Task Completed_or_cancelled_refusal_does_not_lock_the_load()
    {
        var (db, load, _) = await SeedInvoicedLoadAsync();
        (await db.Context.ShipmentLoadRefusals.SingleAsync()).Status = ShipmentLoadRefusalStatus.Cancelled;
        await db.SaveChangesAsync();

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, load.Key, excludedInvoiceKeys: null);
        await db.SaveChangesAsync();

        Assert.Equal(ShipmentLoadStatus.Invoiced,
            (await db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key)).Status);
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentLoadRefusalPendingStatusTests --nologo`
Expected: build error (parâmetro `hasPendingRefusal` não existe).

- [ ] **Step 3: Implement**

Em `ResolveStatus`, novo parâmetro e primeira verificação:

```csharp
    public static ShipmentLoadStatus ResolveStatus(
        decimal totalQuantity,
        decimal invoicedQuantity,
        decimal returnedToWarehouseQuantity,
        decimal transshippedQuantity,
        bool hasOpenTransshipment,
        bool hasPendingRefusal = false)
    {
        // Spec 2026-10-09: a recusa aguardando NF-e vence tudo — a carga fica travada até concluir ou cancelar.
        // Não colide com o transbordo: a recusa para Transbordo exige que não haja transbordo aberto.
        if (hasPendingRefusal)
            return ShipmentLoadStatus.RefusalPending;

        if (hasOpenTransshipment)
            return ShipmentLoadStatus.InTransshipment;
        // ... resto inalterado
```

Atualizar o `<summary>`/`<remarks>` do método com uma linha sobre o novo parâmetro.

Em `RecalculateAsync`, depois de `hasOpenTransshipment`:

```csharp
        var hasPendingRefusal = await context.ShipmentLoadRefusals
            .AnyAsync(x => x.ShipmentLoadKey == shipmentLoadKey && x.Status == ShipmentLoadRefusalStatus.Pending);

        var baseStatus = ResolveStatus(
            load.TotalQuantity, invoiced, returned, transshipped, hasOpenTransshipment, hasPendingRefusal);
```

E a projeção nos romaneios inclui a nova situação:

```csharp
        // RefusalPending (spec 2026-10-09): a carga está faturada e a mercadoria saiu; devolver o romaneio a
        // Confirmed o ofereceria à Montagem enquanto a recusa aguarda NF-e.
        var shipmentStatus = load.Status is ShipmentLoadStatus.Invoiced or ShipmentLoadStatus.Returned
            or ShipmentLoadStatus.Discharged or ShipmentLoadStatus.Completed or ShipmentLoadStatus.RefusalPending
            ? StorageTransactionsStatus.Invoiced
            : StorageTransactionsStatus.Confirmed;
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "ShipmentLoadRefusalPendingStatusTests|ShipmentLoadResolveStatusTests|ShipmentLoadsRecalculateInvoicedServiceTests" --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadRefusalPendingStatusTests.cs SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRecalculateInvoicedService.cs
git commit -m "feat(shipment): carga em Recusa aguardando NF-e no recálculo

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Travas da carga com recusa pendente

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadRefusalRules.cs`
- Modify: `ShipmentLoadsBillingGuardService.cs` (após o ramo `InTransshipment`, ~:89)
- Modify: `ShipmentLoadDischargeRules.cs` (`EnsureLoadAcceptsChanges` :95)
- Modify: `ShipmentLoadTransshipmentRules.cs` (`EnsureLoadAcceptsTransshipment` :55)
- Modify: `ShipmentLoadsUpdateService.cs` (`fiscalFieldsLocked` ~:56)
- Modify: `ShipmentLoadsRefuseService.cs` (`Validate` :443)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadRefusalLockTests.cs`

**Interfaces:**
- Produces: `ShipmentLoadRefusalRules.PendingMessage(string loadCode) : string`; `ShipmentLoadRefusalRules.EnsureNoPendingRefusal(ShipmentLoad load)` (lança `DefaultException`).

- [ ] **Step 1: Write the failing tests**

```csharp
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadRefusalLockTests
{
    private const string Message =
        "A carga CG000001 tem uma recusa aguardando NF-e de entrada: emita as NF-e ou cancele a recusa.";

    private static ShipmentLoad Locked() => new()
    {
        Key = Guid.NewGuid(), Code = "CG000001", BranchCode = "01", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
        TotalQuantity = 30_000m, InvoicedQuantity = 30_000m, Status = ShipmentLoadStatus.RefusalPending,
        CarrierCardCode = "T-001", LoadType = ShipmentLoadType.Normal,
    };

    [Fact]
    public void Message_is_the_spec_text()
    {
        Assert.Equal(Message, ShipmentLoadRefusalRules.PendingMessage("CG000001"));
    }

    [Fact]
    public async Task Billing_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Locked();
        db.Context.ShipmentLoads.Add(load);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new ShipmentLoadsBillingGuardService(db.Context).EnsureCanBillAsync(load.Key, 1m, "T-001"));

        Assert.Equal(Message, ex.Message);
    }

    [Fact]
    public void Discharge_is_refused()
    {
        var ex = Assert.Throws<DefaultException>(() => ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(Locked()));
        Assert.Equal(Message, ex.Message);
    }

    [Fact]
    public void Transshipment_is_refused()
    {
        var ex = Assert.Throws<DefaultException>(() => ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment(Locked()));
        Assert.Equal(Message, ex.Message);
    }

    [Fact]
    public void Other_statuses_pass()
    {
        var load = Locked();
        load.Status = ShipmentLoadStatus.Invoiced;

        ShipmentLoadRefusalRules.EnsureNoPendingRefusal(load);
    }
}
```

Mais dois testes, nos arquivos que já constroem os serviços (copiar a montagem do serviço do arquivo vizinho):
- em `ShipmentLoadsUpdateServiceTests.cs`: `Fiscal_fields_are_locked_while_the_refusal_is_pending` — carga `RefusalPending`, mudar `CarrierCardCode` → espera a mesma exceção que o teste existente de campos fiscais travados em `Invoiced` espera (copiar aquele teste e trocar a situação);
- em `ShipmentLoadsRefuseServiceTests.cs`: `A_second_refusal_is_refused_while_one_is_pending` — `BilledLoadAsync()`, trocar a situação da carga para `RefusalPending` e salvar, `Service().ExecuteAsync(Request(load, invoice, 1m), "tester")` → `DefaultException` com `Message` (literal com o `Code` da semente: `"A carga CG000007 tem uma recusa aguardando NF-e de entrada: emita as NF-e ou cancele a recusa."`).

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "ShipmentLoadRefusalLockTests|ShipmentLoadsUpdateServiceTests|ShipmentLoadsRefuseServiceTests" --nologo`
Expected: build error (`ShipmentLoadRefusalRules` não existe).

- [ ] **Step 3: Implement**

`ShipmentLoadRefusalRules.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Trava da carga com recusa aguardando NF-e de entrada (spec 2026-10-09 §6). Uma regra e uma mensagem para todas
/// as operações que mexem em saldo, ticket ou transbordo — quem lê a mensagem precisa saber os dois caminhos.
/// </summary>
public static class ShipmentLoadRefusalRules
{
    public static string PendingMessage(string? loadCode) =>
        $"A carga {loadCode} tem uma recusa aguardando NF-e de entrada: emita as NF-e ou cancele a recusa.";

    public static void EnsureNoPendingRefusal(ShipmentLoad load)
    {
        if (load.Status == ShipmentLoadStatus.RefusalPending)
            throw new DefaultException(PendingMessage(load.Code));
    }
}
```

Chamadas (uma linha cada, antes das demais checagens de situação de cada método):
- `ShipmentLoadsBillingGuardService.EnsureCanBillAsync`: logo depois de carregar `load` e do `null` check → `ShipmentLoadRefusalRules.EnsureNoPendingRefusal(load);`
- `ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges`: depois do `Cancelled` → `ShipmentLoadRefusalRules.EnsureNoPendingRefusal(load);`
- `ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment`: depois do ramo `Removal` → `ShipmentLoadRefusalRules.EnsureNoPendingRefusal(load);`
- `ShipmentLoadsRefuseService.Validate`: depois do `Cancelled` → `ShipmentLoadRefusalRules.EnsureNoPendingRefusal(load);`
- `ShipmentLoadsUpdateService`: acrescentar `or ShipmentLoadStatus.RefusalPending` à expressão `fiscalFieldsLocked`.

⚠️ `ShipmentLoadsRefuseService.ExecuteAsync` hoje chama `EnsureNotIssuedBySiagroAsync` ANTES de carregar a carga; não mexer na ordem nesta task (a Task 6 reescreve esse ponto).

- [ ] **Step 4: Run the tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "ShipmentLoads" --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadRefusalRules.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadRefusalLockTests.cs
git add -u SiagroB1.Application SiagroB1.Application.Tests
git commit -m "feat(shipment): carga com recusa aguardando NF-e fica travada

Faturar, nova recusa, ticket de descarga, transbordo e campos fiscais
recusados com a mesma mensagem enquanto a recusa estiver pendente.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Construtor comum da devolução com NF-e própria

Refatoração sem mudança de comportamento: a montagem da devolução sai de `SalesInvoicesNfeReturnCreateService` para `SalesInvoiceNfeReturnBuilder`, que a recusa vai usar.

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoiceNfeReturnBuilder.cs`
- Modify: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeReturnCreateService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (registrar o builder, ao lado de `SalesInvoicesNfeReturnCreateService` :355)
- Modify: `SiagroB1.Application.Tests/Support/NfeReturnTestSeed.cs` (`CreateService`)
- Test: `SiagroB1.Application.Tests/Nfe/SalesInvoiceNfeReturnBuilderTests.cs`

**Interfaces:**
- Produces:
  - `SalesInvoiceNfeReturnBuilder(IUnitOfWork db, Func<DateTimeOffset>? clock = null, TimeZoneInfo? storageZone = null)`
  - `static void EnsureOriginAuthorized(SalesInvoice origin)` — lança `DefaultException` com a mensagem atual "O documento {n} não tem NF-e autorizada pelo Siagro: a devolução com NF-e parte de uma venda autorizada." (e a de `Returned`).
  - `Task<SalesInvoice> BuildAsync(SalesInvoice origin, IReadOnlyDictionary<Guid, decimal> quantitiesByOriginItemKey, string comments, string userName)` — valida natureza de devolução e numeração de itens; devolve a devolução montada, NÃO gravada.
  - `static string ReferenceText(SalesInvoice origin)` — `"Devolução da NF-e {nº} série {s} (doc.saída {InvoiceNumber})."`

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

public class SalesInvoiceNfeReturnBuilderTests
{
    private static SalesInvoiceNfeReturnBuilder Builder(NfeReturnScenario s) =>
        new(s.Sale.Db, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

    [Fact]
    public async Task Builds_the_same_return_the_devolver_creates()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var origin = await s.Sale.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Sale.InvoiceKey);

        var built = await Builder(s).BuildAsync(
            origin, new Dictionary<Guid, decimal> { [s.SaleItemKey] = 10_000m }, "Texto livre", "tester");

        Assert.Equal(new DateTime(2026, 10, 2, 12, 0, 0), built.InvoiceDate);
        Assert.Null(built.PaymentConditionCode);
        Assert.Null(built.DeliveryCardCode);
        Assert.Equal("Texto livre", built.Comments);
        Assert.Equal(s.ReturnUsageCode, built.Items.Single().UsageCode);
        Assert.Equal(10_000m, built.Items.Single().Quantity);
    }

    [Fact]
    public async Task Origin_without_return_usage_is_refused()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        (await s.Sale.Db.Context.Usages.SingleAsync(u => u.Name == "Venda de grãos")).ReturnUsageCode = null;
        await s.Sale.Db.SaveChangesAsync();
        var origin = await s.Sale.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Sale.InvoiceKey);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Builder(s).BuildAsync(
            origin, new Dictionary<Guid, decimal> { [s.SaleItemKey] = 1m }, "x", "tester"));

        Assert.Contains("não tem natureza de devolução cadastrada", ex.Message);
    }

    [Fact]
    public async Task Reference_text_names_the_nfe_and_the_document()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var origin = await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.Sale.InvoiceKey);

        Assert.Equal("Devolução da NF-e 1 série 1 (doc.saída 000002388).", SalesInvoiceNfeReturnBuilder.ReferenceText(origin));
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test SiagroB1.Application.Tests --filter SalesInvoiceNfeReturnBuilderTests --nologo`
Expected: build error.

- [ ] **Step 3: Implement**

`SalesInvoiceNfeReturnBuilder.cs` — mover para cá, sem mudar o texto das mensagens, `ResolveReturnUsagesAsync`, `EnsureSaleItemNumbers`, `NumberText`, o fuso/relógio e as atribuições pós-fábrica:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices.Factories;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Monta a devolução com NF-e PRÓPRIA de entrada (finalidade 4) a partir de uma venda autorizada. Um lugar só
/// para o "Devolver" (<see cref="SalesInvoicesNfeReturnCreateService"/>) e a recusa de carga em dois tempos
/// (<c>ShipmentLoadsRefuseService</c>, spec 2026-10-09) gerarem a mesma nota. Não grava nada.
/// </summary>
/// <remarks>
/// A regra "documento com carga não" NÃO mora aqui: ela é do "Devolver", que não sabe destravar a carga.
/// </remarks>
public class SalesInvoiceNfeReturnBuilder(
    IUnitOfWork db,
    Func<DateTimeOffset>? clock = null,
    TimeZoneInfo? storageZone = null)
{
    private readonly Func<DateTimeOffset> _now = clock ?? NfeIssueInputAssembler.BrasiliaNow;

    // Mesmo fuso em que o OData grava o InvoiceDate (o do servidor): a emissão o converte de volta
    // para Brasília antes de exigir "hoje".
    private readonly TimeZoneInfo _storageZone = storageZone ?? TimeZoneInfo.Local;

    public static void EnsureOriginAuthorized(SalesInvoice origin)
    {
        if (origin.InvoiceStatus == InvoiceStatus.Returned)
            throw new DefaultException($"O documento {origin.InvoiceNumber} já foi devolvido por inteiro.");

        if (origin.InvoiceStatus != InvoiceStatus.Confirmed || origin.NfeStatus != NfeStatus.Authorized ||
            origin.ChaveNFe is not { Length: 44 })
            throw new DefaultException(
                $"O documento {origin.InvoiceNumber} não tem NF-e autorizada pelo Siagro: " +
                "a devolução com NF-e parte de uma venda autorizada.");
    }

    public async Task<SalesInvoice> BuildAsync(
        SalesInvoice origin,
        IReadOnlyDictionary<Guid, decimal> quantitiesByOriginItemKey,
        string comments,
        string userName)
    {
        var returnUsages = await ResolveReturnUsagesAsync(origin, quantitiesByOriginItemKey.Keys);
        EnsureSaleItemNumbers(origin, quantitiesByOriginItemKey.Keys);

        var returnInvoice = SalesInvoiceReturnFactory.CreateFrom(origin, userName, quantitiesByOriginItemKey);
        returnInvoice.InvoiceDate = TimeZoneInfo.ConvertTime(_now(), _storageZone).DateTime;
        returnInvoice.PaymentConditionCode = null;
        // A devolução volta ao remetente: não leva o local de entrega da venda (sem <entrega> no XML).
        returnInvoice.DeliveryCardCode = null;
        returnInvoice.DeliveryCardName = null;
        returnInvoice.TaxPayerComments = null;
        returnInvoice.TaxComments = null;
        returnInvoice.VolumeQuantity = origin.VolumeQuantity;
        returnInvoice.VolumeSpecies = origin.VolumeSpecies;
        returnInvoice.VolumeBrand = origin.VolumeBrand;
        returnInvoice.VolumeNumbering = origin.VolumeNumbering;
        returnInvoice.Comments = comments;

        foreach (var item in returnInvoice.Items)
            item.UsageCode = returnUsages[item.SalesInvoiceItemOriginKey!.Value];

        return returnInvoice;
    }

    public static string ReferenceText(SalesInvoice origin) =>
        $"Devolução da NF-e {NumberText(origin.TaxDocumentNumber)} série {origin.TaxDocumentSeries} " +
        $"(doc.saída {origin.InvoiceNumber}).";

    // ResolveReturnUsagesAsync, EnsureSaleItemNumbers e NumberText: copiados SEM mudança de
    // SalesInvoicesNfeReturnCreateService (que deixa de tê-los).
}
```

(Colar os três métodos privados exatamente como estão hoje em `SalesInvoicesNfeReturnCreateService.cs`, inclusive `PtBr` se algum deles usar.)

`SalesInvoicesNfeReturnCreateService`: o construtor passa a receber `SalesInvoiceNfeReturnBuilder builder` no lugar de `clock`/`storageZone`:

```csharp
public class SalesInvoicesNfeReturnCreateService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    SalesInvoicesCreateService createService,
    SalesInvoiceNfeReturnBuilder builder,
    ILogger<SalesInvoicesNfeReturnCreateService> logger)
```

e o miolo de `ExecuteAsync` fica:

```csharp
        await ValidateOriginAsync(origin);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DefaultException("Informe o motivo da devolução.");

        var quantities = await ResolveQuantitiesAsync(origin, request.Items);
        var returnInvoice = await builder.BuildAsync(
            origin, quantities,
            $"{SalesInvoiceNfeReturnBuilder.ReferenceText(origin)} Motivo: {request.Reason.Trim()}",
            userName);
```

O texto resultante é idêntico ao de hoje (`"Devolução da NF-e X série Y (doc.saída Z). Motivo: ..."`). `ValidateOriginAsync` mantém gate, tipo Normal e a regra de carga/romaneio, e troca as duas checagens de situação por `SalesInvoiceNfeReturnBuilder.EnsureOriginAuthorized(origin);`.

`NfeReturnTestSeed.CreateService`:

```csharp
        return new SalesInvoicesNfeReturnCreateService(
            db, TaxTestServices.Gate(db, erp), create,
            new SalesInvoiceNfeReturnBuilder(db, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone),
            NullLogger<SalesInvoicesNfeReturnCreateService>.Instance);
```

Procurar outros `new SalesInvoicesNfeReturnCreateService(` nos testes (`grep -rn "new SalesInvoicesNfeReturnCreateService" SiagroB1.Application.Tests`) e ajustar igual.

DI: `services.AddScoped<SalesInvoiceNfeReturnBuilder>();` ao lado do registro de `SalesInvoicesNfeReturnCreateService`.
⚠️ O DI resolve `Func<DateTimeOffset>?` e `TimeZoneInfo?` pelos defaults só se não estiverem registrados — é o mesmo caso do serviço de hoje; conferir com `dotnet build` + a subida da Web na Task 9.

- [ ] **Step 4: Run the tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Nfe|FullyQualifiedName~SalesInvoices" --nologo`
Expected: PASS (incluídos os testes existentes do "Devolver", sem alteração).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/Nfe/SalesInvoiceNfeReturnBuilder.cs SiagroB1.Application.Tests/Nfe/SalesInvoiceNfeReturnBuilderTests.cs
git add -u SiagroB1.Application SiagroB1.Application.Tests SiagroB1.Web
git commit -m "refactor(invoice): montagem da devolução com NF-e própria num construtor comum

O Devolver e a recusa de carga em dois tempos geram a mesma nota.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Efeitos do destino da recusa num serviço compartilhado

Refatoração sem mudança de comportamento: `ReturnToWarehouseAsync`, `EmitReturnReleasesAsync`, `OpenTransshipmentAsync`, `AppendComment` e `FormatInvoiceNumbers` saem de `ShipmentLoadsRefuseService` para `ShipmentLoadRefusalEffectsService`.

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadRefusalEffectsService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRefuseService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (registrar ao lado de `ShipmentLoadsRefuseService` :484)
- Modify: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsRefuseServiceTests.cs` (`Service()`), `ShipmentLoadsRefuseTransshipmentTests.cs` e `SalesInvoices/SalesInvoicesNfeReturnLockTests.cs` (construções do serviço)

**Interfaces:**
- Produces:
  - `sealed record RefusalEffectsInput(ShipmentLoad Load, RefusalDestination Destination, string? WarehouseCode, string? WarehouseName, IReadOnlyList<SalesInvoice> RefusedInvoices, decimal TotalQuantity, string Reason)`
  - `ShipmentLoadRefusalEffectsService(IUnitOfWork db, StorageTransactionsCreateService storageCreate, StorageTransactionsConfirmedService storageConfirm, ShipmentLoadsMovementLogService movementLog, ShipmentReleasesFromReturnService returnReleases)`
  - `Task ApplyAsync(RefusalEffectsInput input, string userName)` — `Rebilling`: nada; `Warehouse`: romaneio 12 + liberações + movimento; `Transshipment`: abre o transbordo (sem rechecar regras — quem chama checa). Todos os internos em `Deferred`.
  - Novo construtor de `ShipmentLoadsRefuseService(IUnitOfWork db, SalesInvoicesCreateService createService, SalesInvoicesConfirmService confirmService, ShipmentLoadRefusalEffectsService effects, ShipmentLoadsMovementLogService movementLog, IWarehouseService warehouseService, ILogger<ShipmentLoadsRefuseService> logger, TaxCalculationGate? gate = null)`.

- [ ] **Step 1: Confirm the safety net is green**

Run: `dotnet test SiagroB1.Application.Tests --filter "ShipmentLoadsRefuseServiceTests|ShipmentLoadsRefuseTransshipmentTests" --nologo`
Expected: PASS. Esses testes são a rede da refatoração; nenhum teste novo nesta task.

- [ ] **Step 2: Create the service**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>O que a recusa precisa para aplicar o destino físico. <c>RefusedInvoices</c> são as notas de ORIGEM.</summary>
public sealed record RefusalEffectsInput(
    ShipmentLoad Load,
    RefusalDestination Destination,
    string? WarehouseCode,
    string? WarehouseName,
    IReadOnlyList<SalesInvoice> RefusedInvoices,
    decimal TotalQuantity,
    string Reason);

/// <summary>
/// Efeitos físicos do destino da recusa — devolução ao armazém ou abertura do transbordo. Usado pela recusa síncrona
/// (<see cref="ShipmentLoadsRefuseService"/>) e pela conclusão da recusa em dois tempos
/// (<c>ShipmentLoadRefusalCompleteService</c>, spec 2026-10-09), para que as três chaves proibidas do romaneio 12 e
/// a regra do transbordo vivam num lugar só.
/// </summary>
/// <remarks>
/// Roda SEMPRE dentro da transação de quem chama: todos os serviços internos em <see cref="CommitMode.Deferred"/>.
/// Não recheca a elegibilidade do transbordo — quem chama decide (a conclusão não pode chamar
/// <c>EnsureLoadAcceptsTransshipment</c>, porque a carga está em <c>RefusalPending</c>).
/// </remarks>
public class ShipmentLoadRefusalEffectsService(
    IUnitOfWork db,
    StorageTransactionsCreateService storageCreate,
    StorageTransactionsConfirmedService storageConfirm,
    ShipmentLoadsMovementLogService movementLog,
    ShipmentReleasesFromReturnService returnReleases)
{
    public async Task ApplyAsync(RefusalEffectsInput input, string userName)
    {
        switch (input.Destination)
        {
            case RefusalDestination.Warehouse:
                await ReturnToWarehouseAsync(input, userName);
                break;
            case RefusalDestination.Transshipment:
                await OpenTransshipmentAsync(input, userName);
                break;
        }
    }

    // ReturnToWarehouseAsync, EmitReturnReleasesAsync, OpenTransshipmentAsync, AppendComment, FormatInvoiceNumbers:
    // movidos de ShipmentLoadsRefuseService COM os comentários (<remarks> das três chaves proibidas inclusive).
}
```

Na mudança, os parâmetros viram `input`: `load` → `input.Load`; `warehouse.Code`/`warehouse.Name` → `input.WarehouseCode!`/`input.WarehouseName`; `totalQuantity` → `input.TotalQuantity`; `reason` → `input.Reason`; `lines` → `input.RefusedInvoices`, com estas substituições literais:
- `lines[0].Invoice.CardCode` → `input.RefusedInvoices[0].CardCode` (idem `CardName`, `DeliveryCardCode`, `DeliveryCardName`);
- `lines.Select(l => l.Invoice.CardCode)` → `input.RefusedInvoices.Select(i => i.CardCode)`;
- `FormatInvoiceNumbers(IReadOnlyList<SalesInvoice> invoices) => string.Join(", ", invoices.Select(i => i.InvoiceNumber));`

`EmitReturnReleasesAsync(ShipmentLoad load, StorageTransaction entry, string warehouseCode, string? warehouseName, decimal totalQuantity, string userName)` — chamada com `input.WarehouseCode!, input.WarehouseName`.

- [ ] **Step 3: Use it from the refusal**

Em `ShipmentLoadsRefuseService`: novo construtor (Interfaces acima); apagar os métodos movidos; no `ExecuteAsync`, trocar os dois ramos por:

```csharp
            await effects.ApplyAsync(
                new RefusalEffectsInput(
                    load, request.Destination, warehouse?.Code, warehouse?.Name,
                    lines.Select(l => l.Invoice).ToList(), totalQuantity, request.Reason),
                userName);
```

Atualizar o `<remarks>` da classe: os destinos continuam descritos lá, com "aplicados por `ShipmentLoadRefusalEffectsService`". DI: `services.AddScoped<ShipmentLoadRefusalEffectsService>();`.

Testes — `ShipmentLoadsRefuseServiceTests.Service()`:

```csharp
    internal ShipmentLoadRefusalEffectsService Effects(IWarehouseService? warehouses = null) =>
        new(_db,
            StorageCreate(warehouses),
            StorageConfirm(),
            new ShipmentLoadsMovementLogService(_db.Context),
            new ShipmentReleasesFromReturnService(_db.Context));

    internal ShipmentLoadsRefuseService Service(IWarehouseService? warehouses = null) =>
        new(_db,
            CreateService(),
            ConfirmService(),
            Effects(warehouses),
            new ShipmentLoadsMovementLogService(_db.Context),
            warehouses ?? Warehouses(),
            NullLogger<ShipmentLoadsRefuseService>.Instance);
```

Nas outras construções (`grep -rn "new ShipmentLoadsRefuseService(" SiagroB1.Application.Tests`), mesma ordem nova de parâmetros (as de `SalesInvoicesNfeReturnLockTests` passam `null!` nos seis do meio).

- [ ] **Step 4: Run the tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "ShipmentLoads|SalesInvoicesNfeReturnLockTests" --nologo`
Expected: PASS, com os mesmos totais de antes da refatoração.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadRefusalEffectsService.cs
git add -u SiagroB1.Application SiagroB1.Application.Tests SiagroB1.Web
git commit -m "refactor(shipment): efeitos do destino da recusa num serviço compartilhado

Devolução ao armazém e abertura do transbordo passam a ser reaproveitados
pela conclusão da recusa em dois tempos.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Registrar a recusa em dois tempos

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRefuseService.cs`
- Create: `SiagroB1.Application.Tests/Support/NfeLoadRefusalTestSeed.cs`
- Create: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsDeferredRefusalTests.cs`
- Modify: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesNfeReturnLockTests.cs` (remover os dois testes `Load_refusal_*`, substituídos)

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces:
  - `ShipmentLoadsRefuseService` ganha o último parâmetro `SalesInvoiceNfeReturnBuilder? nfeReturnBuilder = null`.
  - `ExecuteAsync` passa a devolver `RefusalResult(ShipmentLoad Load, Guid? RefusalKey)` (`sealed record` no mesmo arquivo). `RefusalKey` nulo no modo síncrono.
  - `NfeLoadRefusalTestSeed` (Task 7 e 8 usam): `SeedAsync(int documents = 1) : Task<NfeLoadRefusalScenario>`; `record NfeLoadRefusalScenario(UnitOfWork Db, ShipmentLoad Load, IReadOnlyList<Guid> SaleKeys)`; `RefuseService(UnitOfWork db)`; `ConfirmService(UnitOfWork db)`; `AuthorizeAndConfirmAsync(UnitOfWork db, Guid returnKey)`; constantes `DestinationWarehouse = "ARM99"`.

- [ ] **Step 1: Write the seed**

`NfeLoadRefusalTestSeed.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

public sealed record NfeLoadRefusalScenario(UnitOfWork Db, ShipmentLoad Load, IReadOnlyList<Guid> SaleKeys);

/// <summary>
/// Carga faturada na CEAGUI (filial com NF-e pelo Siagro): a venda AUTORIZADA do <see cref="NfeReturnTestSeed"/>
/// — e, com <c>documents = 2</c>, uma segunda venda autorizada igual — pendurada numa carga de 30 t por venda,
/// com o romaneio de embarque. A carga sai do recálculo REAL (Faturada).
/// </summary>
public static class NfeLoadRefusalTestSeed
{
    public const string OriginWarehouse = "ARM01";
    public const string DestinationWarehouse = "ARM99";

    public static async Task<NfeLoadRefusalScenario> SeedAsync(int documents = 1)
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var db = s.Sale.Db;
        var context = db.Context;

        var sale = await context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        var keys = new List<Guid> { sale.Key };

        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000001", BranchCode = "01", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG", TruckCode = "ABC-1D23", CarrierCardCode = "T-001",
            CarrierName = "TRANSPORTADORA TESTE LTDA", WarehouseCode = OriginWarehouse,
            TotalQuantity = 30_000m * documents, Status = ShipmentLoadStatus.Open,
        };
        context.ShipmentLoads.Add(load);
        context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "R1", CardCode = NfeTestSeed.CardCode, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse, BranchCode = "01", TruckCode = "ABC-1D23",
            GrossWeight = 30_000m * documents, NetWeight = 30_000m * documents,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed, ShipmentLoadKey = load.Key,
        });
        sale.ShipmentLoadKey = load.Key;

        if (documents == 2)
        {
            var item = sale.Items.Single();
            var second = new SalesInvoice
            {
                Key = Guid.NewGuid(), BranchCode = "01", CardCode = NfeTestSeed.CardCode, CardName = sale.CardName,
                InvoiceType = SalesInvoiceType.Normal, InvoiceStatus = InvoiceStatus.Confirmed, InvoiceNumber = "000002389",
                InvoiceDate = sale.InvoiceDate, GrossWeight = 30_000m, NetWeight = 30_000m,
                TruckingCompanyCode = "T-001", TruckCode = "ABC-1D23", FreightTerms = FreightTerms.Cif,
                ShipmentLoadKey = load.Key, NfeStatus = NfeStatus.Authorized, TaxDocumentNumber = "000000002",
                TaxDocumentSeries = "1", NfeRandomCode = "48151624",
                ChaveNFe = "35261012345678000195550010000000021481516240",
            };
            second.AddItem(new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
                Quantity = 30_000m, UnitPrice = 2m, UsageCode = item.UsageCode, UsageName = item.UsageName,
                Cfop = item.Cfop, Ncm = item.Ncm, GoodsOrigin = 0, CstIcms = item.CstIcms, IcmsRate = item.IcmsRate,
                CstPis = item.CstPis, CstCofins = item.CstCofins, IbsCbsCst = item.IbsCbsCst,
                IbsCbsClassCode = item.IbsCbsClassCode, CbsRate = item.CbsRate, IbsStateRate = item.IbsStateRate,
                NfeItemNumber = 1,
            });
            context.SalesInvoices.Add(second);
            keys.Add(second.Key);
        }

        await db.SaveChangesAsync();
        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(context, load.Key, excludedInvoiceKeys: null);
        await db.SaveChangesAsync();

        return new NfeLoadRefusalScenario(db, load, keys);
    }

    private static FakeBusinessPartnerService Partners() =>
        new(names: new() { [NfeTestSeed.CardCode] = "CLIENTE BA LTDA", ["T-001"] = "TRANSPORTADORA TESTE LTDA" },
            states: new() { [NfeTestSeed.CardCode] = "BA" });

    private static FakeItemService Items() => new(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" });

    public static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string> { [OriginWarehouse] = "ARMAZEM ORIGEM", [DestinationWarehouse] = "ARMAZEM RETAGUARDA" });

    private static SalesInvoicesCreateService Create(UnitOfWork db)
    {
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var partners = Partners();

        return new SalesInvoicesCreateService(
            db, partners, Items(), new FakeDocNumberSequenceService(), new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(db, usages, partners), TaxTestServices.Apply(db, partners, "STANDALONE"),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesCreateService>.Instance);
    }

    public static ShipmentLoadRefusalEffectsService Effects(UnitOfWork db) =>
        new(db,
            new StorageTransactionsCreateService(
                db, new FakeDocNumberSequenceService(), Partners(), Items(), Warehouses(),
                new ShipmentReleasesRecalculateShippedService(db.Context), new ShipmentReleaseMovementGuardService(db.Context),
                NullLogger<StorageTransactionsCreateService>.Instance),
            new StorageTransactionsConfirmedService(
                db, new FakeStringLocalizer<Resource>(), new ShipmentReleasesRecalculateShippedService(db.Context),
                new ShipmentReleaseMovementGuardService(db.Context), NullLogger<StorageTransactionsConfirmedService>.Instance),
            new ShipmentLoadsMovementLogService(db.Context),
            new ShipmentReleasesFromReturnService(db.Context));

    /// <param name="complete">Task 7 passa a conclusão; antes dela, <c>null</c>.</param>
    public static SalesInvoicesConfirmService ConfirmService(UnitOfWork db, ShipmentLoadRefusalCompleteService? complete = null) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationCreateService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesContractsAllocationCreateForReturnService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesInvoicesUsageGuardService(new UsageService(db, NullLogger<UsageService>.Instance)),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(db, new SalesContractsFixedVolumeService(db.Context)),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>(),
            TaxTestServices.Gate(db, "STANDALONE"),
            complete);

    public static ShipmentLoadsRefuseService RefuseService(UnitOfWork db, string erp = "STANDALONE") =>
        new(db,
            Create(db),
            ConfirmService(db),
            Effects(db),
            new ShipmentLoadsMovementLogService(db.Context),
            Warehouses(),
            NullLogger<ShipmentLoadsRefuseService>.Instance,
            TaxTestServices.Gate(db, erp),
            new SalesInvoiceNfeReturnBuilder(db, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone));

    /// <summary>O que o retorno da SEFAZ faz: grava Autorizada e confirma em transação própria (Auto).</summary>
    public static async Task AuthorizeAndConfirmAsync(UnitOfWork db, Guid returnKey, SalesInvoicesConfirmService confirm)
    {
        var invoice = await db.Context.SalesInvoices.SingleAsync(i => i.Key == returnKey);
        invoice.NfeStatus = NfeStatus.Authorized;
        await db.SaveChangesAsync();

        await confirm.ExecuteAsync(returnKey, "tester");
    }
}
```

⚠️ `ConfirmService` acima já passa o 11º argumento `complete`, que só existe a partir da Task 7. Nesta task, escreva o método SEM o parâmetro `complete` e sem o último argumento; a Task 7 os acrescenta.

- [ ] **Step 2: Write the failing tests**

`ShipmentLoadsDeferredRefusalTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>Recusa de carga na filial que emite NF-e pelo Siagro: o registro (spec 2026-10-09 §5.1).</summary>
public class ShipmentLoadsDeferredRefusalTests
{
    private static RefusalRequest Request(NfeLoadRefusalScenario s, decimal quantity,
        RefusalDestination destination = RefusalDestination.Rebilling, string? warehouse = null) =>
        new(s.Load.Key, s.SaleKeys.Select(k => new RefusalLine(k, quantity)).ToList(), destination, warehouse,
            "Recusado por umidade");

    [Fact]
    public async Task Registers_a_pending_refusal_with_one_pending_own_return_per_document()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync(documents: 2);

        var result = await NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(
            Request(s, 10_000m, RefusalDestination.Warehouse, NfeLoadRefusalTestSeed.DestinationWarehouse), "tester");

        var refusal = await s.Db.Context.ShipmentLoadRefusals.AsNoTracking().SingleAsync();
        Assert.Equal(refusal.Key, result.RefusalKey);
        Assert.Equal(ShipmentLoadRefusalStatus.Pending, refusal.Status);
        Assert.Equal(RefusalDestination.Warehouse, refusal.Destination);
        Assert.Equal("ARM99", refusal.DestinationWarehouseCode);
        Assert.Equal("ARMAZEM RETAGUARDA", refusal.DestinationWarehouseName);
        Assert.Equal("Recusado por umidade", refusal.Reason);

        var returns = await s.Db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items)
            .Where(i => i.InvoiceType == SalesInvoiceType.Return).ToListAsync();
        Assert.Equal(2, returns.Count);
        Assert.All(returns, r =>
        {
            Assert.True(r.IsNfeReturn);
            Assert.Equal(InvoiceStatus.Pending, r.InvoiceStatus);
            Assert.Equal(refusal.Key, r.ShipmentLoadRefusalKey);
            Assert.Equal(s.Load.Key, r.ShipmentLoadKey);
            Assert.Equal(10_000m, r.Items.Single().Quantity);
            Assert.StartsWith("Recusa da carga CG000001. Devolução da NF-e", r.Comments);
            Assert.EndsWith("Motivo: Recusado por umidade", r.Comments);
        });
    }

    [Fact]
    public async Task The_load_is_locked_and_its_balance_untouched()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync();

        await NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(
            Request(s, 10_000m, RefusalDestination.Warehouse, NfeLoadRefusalTestSeed.DestinationWarehouse), "tester");

        var load = await s.Db.Context.ShipmentLoads.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadStatus.RefusalPending, load.Status);
        Assert.Equal(30_000m, load.InvoicedQuantity);
        Assert.Equal(0m, load.ReturnedToWarehouseQuantity);
        Assert.False(await s.Db.Context.StorageTransactions.AnyAsync(t => t.TransactionType == StorageTransactionType.SalesShipmentReturn));
        Assert.False(await s.Db.Context.ShipmentLoadsTransshipments.AnyAsync());
        Assert.Contains(await s.Db.Context.ShipmentLoadMovements.AsNoTracking().ToListAsync(),
            m => m.MovementType == ShipmentLoadMovementType.Refused && m.Description!.Contains("aguardando NF-e de entrada"));
    }

    [Fact]
    public async Task Document_confirmed_but_not_transmitted_is_refused()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync();
        (await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.SaleKeys[0])).NfeStatus = NfeStatus.None;
        await s.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(Request(s, 1m), "tester"));

        Assert.Equal("O documento 000002388 não tem NF-e autorizada: transmita a NF-e ou cancele o documento.", ex.Message);
        Assert.False(await s.Db.Context.ShipmentLoadRefusals.AnyAsync());
    }

    [Fact]
    public async Task Document_with_cancelled_nfe_keeps_the_lock_message()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync();
        (await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.SaleKeys[0])).NfeStatus = NfeStatus.Cancelled;
        await s.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(Request(s, 1m), "tester"));

        Assert.Equal("A NF-e deste documento foi cancelada na SEFAZ: o documento não pode mudar.", ex.Message);
    }

    [Fact]
    public async Task Sale_usage_without_return_usage_is_refused_without_writing()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync();
        (await s.Db.Context.Usages.SingleAsync(u => u.Name == "Venda de grãos")).ReturnUsageCode = null;
        await s.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(Request(s, 1m), "tester"));

        Assert.Contains("não tem natureza de devolução cadastrada", ex.Message);
        Assert.False(await s.Db.Context.ShipmentLoadRefusals.AnyAsync());
        Assert.False(await s.Db.Context.SalesInvoices.AnyAsync(i => i.InvoiceType == SalesInvoiceType.Return));
    }

    [Fact]
    public async Task Outside_the_rule_the_refusal_stays_synchronous()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync();

        var result = await NfeLoadRefusalTestSeed.RefuseService(s.Db, erp: "SAPB1").ExecuteAsync(Request(s, 10_000m), "tester");

        Assert.Null(result.RefusalKey);
        Assert.False(await s.Db.Context.ShipmentLoadRefusals.AnyAsync());
        Assert.Equal(ShipmentLoadStatus.PartiallyInvoiced, (await s.Db.Context.ShipmentLoads.AsNoTracking().SingleAsync()).Status);
    }
}
```

Apagar de `SalesInvoicesNfeReturnLockTests.cs` os testes `Load_refusal_is_refused_for_a_sale_authorized_by_the_siagro` e `Load_refusal_is_refused_for_a_sale_with_cancelled_nfe` (o segundo foi para `Document_with_cancelled_nfe_keeps_the_lock_message`).

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentLoadsDeferredRefusalTests --nologo`
Expected: FAIL (o serviço ainda recusa com "...ainda não é suportada." / `RefusalResult` não existe).

⚠️ Se `Outside_the_rule_the_refusal_stays_synchronous` falhar por algo do caminho SAPB1 que não seja a recusa (ex.: o confirm exigir algo da natureza), anote e ajuste só a semente, nunca o serviço.

- [ ] **Step 3: Implement**

No topo de `ShipmentLoadsRefuseService.cs`:

```csharp
/// <summary>Resultado da recusa. <c>RefusalKey</c> só no modo em dois tempos (filial que emite NF-e pelo Siagro).</summary>
public sealed record RefusalResult(ShipmentLoad Load, Guid? RefusalKey);
```

Construtor: acrescentar `SalesInvoiceNfeReturnBuilder? nfeReturnBuilder = null` depois de `gate`.

Apagar `EnsureNotIssuedBySiagroAsync`. Novo começo de `ExecuteAsync`:

```csharp
    public async Task<RefusalResult> ExecuteAsync(RefusalRequest request, string userName)
    {
        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == request.ShipmentLoadKey) ??
                   throw new NotFoundException($"Shipment load not found key {request.ShipmentLoadKey}");

        // TODA a validação antes de qualquer escrita: uma recusa recusada não pode deixar
        // efeito no banco, nem meia devolução criada.
        Validate(load, request);

        // Spec 2026-10-09: na filial que emite NF-e pelo Siagro a devolução só confirma com a NF-e de entrada
        // autorizada — a recusa vira dois tempos. Fora dela, o fluxo síncrono de sempre.
        var deferred = gate is not null && await gate.IsActiveAsync(load.BranchCode);

        if (request.Destination == RefusalDestination.Transshipment)
        {
            // (bloco existente inalterado)
        }

        var warehouse = await ResolveWarehouseAsync(request);
        var lines = await ResolveLinesAsync(load, request);

        if (deferred)
            return new RefusalResult(load, await RegisterDeferredAsync(load, request, warehouse, lines, userName));

        // ... corpo síncrono existente; no fim: return new RefusalResult(load, null);
```

Novo método (antes de `ReturnAsync`):

```csharp
    /// <summary>
    /// Primeiro tempo da recusa na filial que emite NF-e pelo Siagro (spec 2026-10-09 §5.1): grava a recusa
    /// Pendente e uma devolução Pendente com NF-e própria por documento. Não confirma, não mexe no saldo, não cria
    /// romaneio nem transbordo — isso é da conclusão (<c>ShipmentLoadRefusalCompleteService</c>), quando a última
    /// NF-e for autorizada.
    /// </summary>
    private async Task<Guid> RegisterDeferredAsync(
        ShipmentLoad load, RefusalRequest request, WarehouseTarget? warehouse,
        IReadOnlyList<ResolvedLine> lines, string userName)
    {
        var builder = nfeReturnBuilder
                      ?? throw new InvalidOperationException("SalesInvoiceNfeReturnBuilder não registrado.");

        // Antes de qualquer escrita: cada origem com NF-e autorizada e a devolução montável (natureza de
        // devolução, numeração de itens). Montar aqui, fora da transação, é o que valida.
        var built = new List<SalesInvoice>();

        foreach (var line in lines)
        {
            if (line.Invoice.NfeStatus == NfeStatus.Cancelled)
                throw new DefaultException(NfeLockRules.CancelledMessage);

            if (line.Invoice.NfeStatus != NfeStatus.Authorized || line.Invoice.ChaveNFe is not { Length: 44 })
                throw new DefaultException(
                    $"O documento {line.Invoice.InvoiceNumber} não tem NF-e autorizada: " +
                    "transmita a NF-e ou cancele o documento.");

            built.Add(await builder.BuildAsync(
                line.Invoice,
                line.QuantitiesByOriginItemKey,
                $"Recusa da carga {load.Code}. {SalesInvoiceNfeReturnBuilder.ReferenceText(line.Invoice)} " +
                $"Motivo: {request.Reason.Trim()}",
                userName));
        }

        var refusal = new ShipmentLoadRefusal
        {
            ShipmentLoadKey = load.Key,
            Destination = request.Destination,
            DestinationWarehouseCode = warehouse?.Code,
            DestinationWarehouseName = warehouse?.Name,
            Reason = request.Reason.Trim(),
            CreatedBy = userName,
        };

        try
        {
            await db.BeginTransactionAsync();

            db.Context.ShipmentLoadRefusals.Add(refusal);
            await db.SaveChangesAsync();

            foreach (var returnInvoice in built)
            {
                await createService.ExecuteAsync(returnInvoice, userName, CommitMode.Deferred, nfeReturn: true);

                // DEPOIS da criação: ela zera o vínculo vindo do corpo (SalesInvoiceNfeLock.ResetIssuanceFields).
                returnInvoice.ShipmentLoadRefusalKey = refusal.Key;
                await db.SaveChangesAsync();
            }

            var totalQuantity = decimal.Round(lines.Sum(l => l.Quantity), 3, MidpointRounding.ToEven);

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.Refused,
                decimal.Zero,
                load.AvailableQuantity,
                $"Recusa registrada em {lines.Count} documento(s) de saída, aguardando NF-e de entrada: " +
                $"{totalQuantity:N3}. Documentos: {string.Join(", ", lines.Select(l => l.Invoice.InvoiceNumber))}.",
                userName,
                movementContext: ShipmentLoadMovementContext.FromInvoice(lines[0].Invoice, request.Reason));

            await db.SaveChangesAsync();

            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, load.Key, excludedInvoiceKeys: null);
            load.UpdatedAt = DateTime.Now;
            load.UpdatedBy = userName;

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, "Erro ao registrar a recusa da carga {Code}", load.Code);
            throw;
        }

        return refusal.Key!.Value;
    }
```

`using SiagroB1.Application.Services.Nfe;` já existe no arquivo; acrescentar `using SiagroB1.Application.Services.Nfe;` se faltar, e confirmar que `NfeLockRules` está no namespace importado (`grep -rn "class NfeLockRules" SiagroB1.Application`).

⚠️ `ShipmentLoadMovementContext.FromInvoice` e `Description` do movimento: conferir os nomes reais em `ShipmentLoadMovement`/`ShipmentLoadMovementContext` antes de escrever o teste (`grep -n "class ShipmentLoadMovement\b" -A40 SiagroB1.Domain/Entities/ShipmentLoadMovement.cs`); se a propriedade de texto tiver outro nome, ajuste o teste, não o serviço.

Controller `ShipmentLoadsRefuseController`: `var load = await refuseService.ExecuteAsync(...)` vira `var result = ...; var load = result.Load;` e o `Ok(new { ... })` ganha `RefusalKey = result.RefusalKey`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "ShipmentLoads|SalesInvoicesNfeReturnLockTests" --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application.Tests/Support/NfeLoadRefusalTestSeed.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsDeferredRefusalTests.cs
git add -u SiagroB1.Application SiagroB1.Application.Tests SiagroB1.Web
git commit -m "feat(shipment): recusa de carga registra devoluções aguardando NF-e

Na filial que emite NF-e pelo Siagro a recusa grava a recusa Pendente e
uma devolução com NF-e própria de entrada por documento, e trava a carga
sem mexer no saldo. Fora dela, o fluxo síncrono de sempre.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Conclusão automática na confirmação da última devolução

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadRefusalCompleteService.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs` (construtor; ramo de devolução após o `loadHook`, ~:184-193)
- Modify: `SiagroB1.Application.Tests/Support/NfeLoadRefusalTestSeed.cs` (`ConfirmService` com `complete`)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (registrar)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadRefusalCompleteTests.cs`

**Interfaces:**
- Consumes: `ShipmentLoadRefusalEffectsService.ApplyAsync(RefusalEffectsInput, string)`; `ShipmentLoadTransshipmentRules.EnsureIsLastAsync(AppDbContext, ShipmentLoad)`.
- Produces: `ShipmentLoadRefusalCompleteService(IUnitOfWork db, ShipmentLoadRefusalEffectsService effects)`; `Task ApplyAsync(SalesInvoice confirmedReturn, string userName)`; `SalesInvoicesConfirmService` ganha o último parâmetro opcional `ShipmentLoadRefusalCompleteService? refusalComplete = null`.

- [ ] **Step 1: Write the failing tests**

Primeiro acrescentar ao `NfeLoadRefusalTestSeed`:

```csharp
    public static ShipmentLoadRefusalCompleteService Complete(UnitOfWork db) => new(db, Effects(db));
```

e o parâmetro `complete` em `ConfirmService` (como mostrado na Task 6), passando `complete` como último argumento. A `RefuseService` continua usando `ConfirmService(db)` sem conclusão — o confirm dela é o do fluxo síncrono.

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>Segundo tempo da recusa (spec 2026-10-09 §5.3): a confirmação da última devolução conclui.</summary>
public class ShipmentLoadRefusalCompleteTests
{
    private static async Task<(NfeLoadRefusalScenario S, List<Guid> Returns)> RefuseAsync(
        int documents, decimal quantity, RefusalDestination destination, string? warehouse = null)
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync(documents);
        await NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(
            new RefusalRequest(s.Load.Key, s.SaleKeys.Select(k => new RefusalLine(k, quantity)).ToList(),
                destination, warehouse, "Recusado por umidade"),
            "tester");

        var returns = await s.Db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.InvoiceType == SalesInvoiceType.Return).Select(i => i.Key).ToListAsync();
        s.Db.Context.ChangeTracker.Clear();
        return (s, returns);
    }

    private static Task ConfirmAsync(NfeLoadRefusalScenario s, Guid returnKey) =>
        NfeLoadRefusalTestSeed.AuthorizeAndConfirmAsync(
            s.Db, returnKey, NfeLoadRefusalTestSeed.ConfirmService(s.Db, NfeLoadRefusalTestSeed.Complete(s.Db)));

    private static Task<Domain.Entities.ShipmentLoad> LoadAsync(NfeLoadRefusalScenario s) =>
        s.Db.Context.ShipmentLoads.AsNoTracking().SingleAsync();

    [Fact]
    public async Task First_of_two_returns_does_not_complete()
    {
        var (s, returns) = await RefuseAsync(2, 10_000m, RefusalDestination.Warehouse, NfeLoadRefusalTestSeed.DestinationWarehouse);

        await ConfirmAsync(s, returns[0]);

        Assert.Equal(ShipmentLoadRefusalStatus.Pending, (await s.Db.Context.ShipmentLoadRefusals.AsNoTracking().SingleAsync()).Status);
        var load = await LoadAsync(s);
        Assert.Equal(ShipmentLoadStatus.RefusalPending, load.Status);
        Assert.Equal(50_000m, load.InvoicedQuantity); // o saldo daquele documento já voltou
        Assert.False(await s.Db.Context.StorageTransactions.AnyAsync(t => t.TransactionType == StorageTransactionType.SalesShipmentReturn));
    }

    [Fact]
    public async Task Last_return_to_warehouse_creates_the_entry_and_unlocks()
    {
        var (s, returns) = await RefuseAsync(2, 10_000m, RefusalDestination.Warehouse, NfeLoadRefusalTestSeed.DestinationWarehouse);

        await ConfirmAsync(s, returns[0]);
        s.Db.Context.ChangeTracker.Clear();
        await ConfirmAsync(s, returns[1]);

        var refusal = await s.Db.Context.ShipmentLoadRefusals.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadRefusalStatus.Completed, refusal.Status);
        Assert.Equal("tester", refusal.CompletedBy);

        var entry = await s.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(t => t.TransactionType == StorageTransactionType.SalesShipmentReturn);
        Assert.Equal(StorageTransactionsStatus.Confirmed, entry.TransactionStatus);
        Assert.Equal("ARM99", entry.WarehouseCode);
        Assert.Equal(20_000m, entry.NetWeight);
        Assert.Equal(s.Load.Key, entry.RefusedFromShipmentLoadKey);
        // As três chaves proibidas (ver ShipmentLoadRefusalEffectsService).
        Assert.Null(entry.ShipmentLoadKey);
        Assert.Null(entry.ShipmentReleaseKey);
        Assert.Null(entry.ReturnInvoiceKey);

        var load = await LoadAsync(s);
        Assert.Equal(40_000m, load.InvoicedQuantity);
        Assert.Equal(20_000m, load.ReturnedToWarehouseQuantity);
        Assert.Equal(ShipmentLoadStatus.Returned, load.Status);
    }

    [Fact]
    public async Task Rebilling_puts_the_load_back_on_billing()
    {
        var (s, returns) = await RefuseAsync(1, 10_000m, RefusalDestination.Rebilling);

        await ConfirmAsync(s, returns[0]);

        var load = await LoadAsync(s);
        Assert.Equal(ShipmentLoadStatus.PartiallyInvoiced, load.Status);
        Assert.Equal(10_000m, load.AvailableQuantity);
        Assert.Equal(ShipmentLoadRefusalStatus.Completed, (await s.Db.Context.ShipmentLoadRefusals.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Transshipment_opens_with_the_refused_quantity_even_though_the_load_was_locked()
    {
        // Review Focus 2: a carga está em RefusalPending; a conclusão não pode passar por EnsureLoadAcceptsTransshipment.
        var (s, returns) = await RefuseAsync(1, 10_000m, RefusalDestination.Transshipment, NfeLoadRefusalTestSeed.DestinationWarehouse);

        await ConfirmAsync(s, returns[0]);

        var transshipment = await s.Db.Context.ShipmentLoadsTransshipments.AsNoTracking().SingleAsync();
        Assert.Equal(10_000m, transshipment.OutgoingQuantity);
        Assert.Equal(TransshipmentOrigin.Refusal, transshipment.Origin);
        Assert.Equal(ShipmentLoadStatus.InTransshipment, (await LoadAsync(s)).Status);
    }

    [Fact]
    public async Task Failure_in_the_effects_rolls_the_confirmation_back()
    {
        var (s, returns) = await RefuseAsync(1, 10_000m, RefusalDestination.Warehouse, "INEXISTENTE");

        await Assert.ThrowsAnyAsync<Exception>(() => ConfirmAsync(s, returns[0]));

        s.Db.Context.ChangeTracker.Clear();
        Assert.Equal(InvoiceStatus.Pending, (await s.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == returns[0])).InvoiceStatus);
        Assert.Equal(ShipmentLoadRefusalStatus.Pending, (await s.Db.Context.ShipmentLoadRefusals.AsNoTracking().SingleAsync()).Status);
    }
}
```

⚠️ `Failure_in_the_effects_rolls_the_confirmation_back` depende de o armazém "INEXISTENTE" passar no registro e quebrar só na criação do romaneio. O registro resolve o armazém por `IWarehouseService` e recusaria antes. Para provocar a falha só na conclusão: registrar com `ARM99` e, antes de confirmar, usar um `Effects` cujo `FakeWarehouseService` não conhece `ARM99` — acrescente ao seed `Complete(UnitOfWork db, FakeWarehouseService warehouses)` e `Effects(db, warehouses)`; se o `StorageTransactionsCreateService` não validar armazém, use `ThrowOnSaveInterceptor` (`Support/ThrowOnSaveInterceptor.cs`) para estourar no SaveChanges do romaneio. Escolha o que realmente quebra; o que o teste prova é o rollback.

⚠️ InMemory não tem transação real: `RollbackAsync` ali é no-op e a confirmação parcial pode ficar no ChangeTracker. Se o teste não conseguir provar rollback no InMemory, mude a asserção para o que o InMemory garante — a exceção sobe e `refusal.Status` continua `Pending` — e anote no relatório da task (o rollback real é provado no E2E da Task 11).

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentLoadRefusalCompleteTests --nologo`
Expected: build error (`ShipmentLoadRefusalCompleteService` não existe).

- [ ] **Step 3: Implement**

`ShipmentLoadRefusalCompleteService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Segundo tempo da recusa de carga na filial que emite NF-e pelo Siagro (spec 2026-10-09 §5.3). Chamado por
/// <c>SalesInvoicesConfirmService</c> a cada devolução confirmada; quando TODAS as devoluções vivas da recusa estão
/// confirmadas, aplica os efeitos do destino e conclui a recusa — na transação da confirmação.
/// </summary>
/// <remarks>
/// Falha aqui derruba a confirmação inteira; <c>NfeResultHandlerBase</c> mantém a NF-e Autorizada, grava o erro e o
/// "Concluir confirmação" refaz tudo. A quantidade é a soma das devoluções CONFIRMADAS: uma devolução cuja NF-e foi
/// cancelada pelo 2b sai da conta (Review Focus 3).
/// </remarks>
public class ShipmentLoadRefusalCompleteService(IUnitOfWork db, ShipmentLoadRefusalEffectsService effects)
{
    public async Task ApplyAsync(SalesInvoice confirmedReturn, string userName)
    {
        if (confirmedReturn.ShipmentLoadRefusalKey is not { } refusalKey)
            return;

        var refusal = await db.Context.ShipmentLoadRefusals.FirstOrDefaultAsync(x => x.Key == refusalKey);

        if (refusal is not { Status: ShipmentLoadRefusalStatus.Pending })
            return;

        var returns = await db.Context.SalesInvoices
            .Include(i => i.Items)
            .Where(i => i.ShipmentLoadRefusalKey == refusalKey && i.InvoiceStatus != InvoiceStatus.Cancelled)
            .ToListAsync();

        if (returns.Any(i => i.InvoiceStatus != InvoiceStatus.Confirmed))
            return;

        var load = await db.Context.ShipmentLoads.FirstAsync(x => x.Key == refusal.ShipmentLoadKey);

        var originKeys = returns.Select(i => i.SalesInvoiceOriginKey).ToList();
        var origins = await db.Context.SalesInvoices.Where(i => originKeys.Contains(i.Key)).ToListAsync();

        var totalQuantity = decimal.Round(returns.SelectMany(i => i.Items).Sum(i => i.Quantity), 3, MidpointRounding.ToEven);

        // Só a regra de "não empilhar": EnsureLoadAcceptsTransshipment recusaria a carga travada pela própria recusa.
        if (refusal.Destination == RefusalDestination.Transshipment)
            await ShipmentLoadTransshipmentRules.EnsureIsLastAsync(db.Context, load);

        // A recusa sai de Pending ANTES dos efeitos: os recálculos lá dentro precisam ver a carga destravada.
        refusal.Status = ShipmentLoadRefusalStatus.Completed;
        refusal.CompletedAt = DateTime.Now;
        refusal.CompletedBy = userName;
        await db.SaveChangesAsync();

        if (origins.Count > 0 && totalQuantity > decimal.Zero)
        {
            await effects.ApplyAsync(
                new RefusalEffectsInput(
                    load, refusal.Destination, refusal.DestinationWarehouseCode, refusal.DestinationWarehouseName,
                    origins, totalQuantity, refusal.Reason),
                userName);
        }

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, load.Key, excludedInvoiceKeys: null);
        load.UpdatedAt = DateTime.Now;
        load.UpdatedBy = userName;
        await db.SaveChangesAsync();
    }
}
```

⚠️ Conferir o tipo de `SalesInvoice.SalesInvoiceOriginKey` (`Guid` ou `Guid?`); se for anulável, filtre `.Where(k => k != null)` e use `.Contains(i.Key)` com a lista de `Guid?`.

`SalesInvoicesConfirmService`: novo último parâmetro do construtor `ShipmentLoadRefusalCompleteService? refusalComplete = null` (depois de `gate`), e no ramo de devolução:

```csharp
            if (invoice.InvoiceType == SalesInvoiceType.Return)
            {
                await loadHook.ApplyAsync(
                    invoice,
                    ShipmentLoadMovementType.Returned,
                    userName,
                    $"Devolução {invoice.InvoiceNumber} confirmada: saldo devolvido à carga.");

                await db.SaveChangesAsync();

                // Spec 2026-10-09: a última devolução de uma recusa aguardando NF-e conclui a recusa, na mesma
                // transação — falhar aqui desfaz a confirmação.
                if (refusalComplete is not null)
                    await refusalComplete.ApplyAsync(invoice, userName);
            }
```

DI: `services.AddScoped<ShipmentLoadRefusalCompleteService>();`.
⚠️ Ciclo de DI: `SalesInvoicesConfirmService → ShipmentLoadRefusalCompleteService → ShipmentLoadRefusalEffectsService → StorageTransactions*`. Nenhum deles depende de `SalesInvoicesConfirmService` — conferir com `grep -n "SalesInvoicesConfirmService" SiagroB1.Application/Services/StorageTransactions/*.cs SiagroB1.Application/Services/ShipmentReleases/*.cs` (esperado: nada).

- [ ] **Step 4: Run the tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "ShipmentLoadRefusalCompleteTests|ShipmentLoads|SalesInvoices|Nfe" --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadRefusalCompleteService.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadRefusalCompleteTests.cs
git add -u SiagroB1.Application SiagroB1.Application.Tests SiagroB1.Web
git commit -m "feat(shipment): autorização da última NF-e de entrada conclui a recusa

A confirmação da devolução aplica o destino (armazém, transbordo ou
refaturamento) quando todas as devoluções da recusa estão confirmadas,
na mesma transação; falha desfaz a confirmação e o Concluir confirmação
refaz.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Cancelar a recusa pendente + trava da devolução avulsa

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsCancelRefusalService.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCancelService.cs` (modo diferido + trava)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesDeleteService.cs` (trava)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsCancelRefusalTests.cs`

**Interfaces:**
- Produces:
  - `SalesInvoicesCancelService.CancelForRefusalAsync(Guid key, string userName) : Task` — cancela em `CommitMode.Deferred`, sem a trava da recusa.
  - `SalesInvoicesRefusalLink.EnsureNotInPendingRefusalAsync(AppDbContext context, SalesInvoice invoice) : Task` (static, em `SalesInvoices/`) — mensagem `"Esta devolução pertence à recusa da carga {Code}: cancele a recusa na Montagem de Carga."`
  - `ShipmentLoadsCancelRefusalService(IUnitOfWork db, SalesInvoicesCancelService cancelService, ShipmentLoadsMovementLogService movementLog, ILogger<ShipmentLoadsCancelRefusalService> logger)`; `Task<ShipmentLoad> ExecuteAsync(Guid shipmentLoadKey, string userName)`.

- [ ] **Step 1: Write the failing tests**

Acrescentar ao seed:

```csharp
    public static SalesInvoicesCancelService CancelService(UnitOfWork db) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationDeleteForInvoiceService(db, new SalesContractsFixedVolumeService(db.Context)),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            NullLogger<SalesInvoicesCancelService>.Instance);

    public static ShipmentLoadsCancelRefusalService CancelRefusalService(UnitOfWork db) =>
        new(db, CancelService(db), new ShipmentLoadsMovementLogService(db.Context),
            NullLogger<ShipmentLoadsCancelRefusalService>.Instance);
```

(Conferir o construtor real de `SalesContractsAllocationDeleteForInvoiceService` em `grep -rn "new SalesContractsAllocationDeleteForInvoiceService" SiagroB1.Application.Tests | head -1` e copiar.)

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadsCancelRefusalTests
{
    private static async Task<(NfeLoadRefusalScenario S, List<Guid> Returns)> RefuseAsync(int documents = 2)
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync(documents);
        await NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(
            new RefusalRequest(s.Load.Key, s.SaleKeys.Select(k => new RefusalLine(k, 10_000m)).ToList(),
                RefusalDestination.Rebilling, null, "Recusado"),
            "tester");
        var returns = await s.Db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.InvoiceType == SalesInvoiceType.Return).Select(i => i.Key).ToListAsync();
        s.Db.Context.ChangeTracker.Clear();
        return (s, returns);
    }

    [Fact]
    public async Task Cancels_pending_and_rejected_returns_and_unlocks_the_load()
    {
        var (s, returns) = await RefuseAsync();
        (await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returns[1])).NfeStatus = NfeStatus.Rejected;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();

        await NfeLoadRefusalTestSeed.CancelRefusalService(s.Db).ExecuteAsync(s.Load.Key, "tester");

        Assert.All(await s.Db.Context.SalesInvoices.AsNoTracking().Where(i => returns.Contains(i.Key)).ToListAsync(),
            r => Assert.Equal(InvoiceStatus.Cancelled, r.InvoiceStatus));
        var refusal = await s.Db.Context.ShipmentLoadRefusals.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadRefusalStatus.Cancelled, refusal.Status);
        Assert.Equal("tester", refusal.CancelledBy);
        var load = await s.Db.Context.ShipmentLoads.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);
        Assert.Contains(await s.Db.Context.ShipmentLoadMovements.AsNoTracking().ToListAsync(),
            m => m.MovementType == ShipmentLoadMovementType.RefusalCancelled);
    }

    [Theory]
    [InlineData(NfeStatus.Authorized)]
    [InlineData(NfeStatus.Processing)]
    public async Task Refused_when_a_return_nfe_is_authorized_or_processing(NfeStatus status)
    {
        var (s, returns) = await RefuseAsync();
        var r = await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returns[0]);
        r.NfeStatus = status;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeLoadRefusalTestSeed.CancelRefusalService(s.Db).ExecuteAsync(s.Load.Key, "tester"));

        Assert.Equal(
            $"A NF-e de entrada do documento {r.InvoiceNumber} já foi autorizada ou está em processamento: " +
            "cancele a NF-e ou aguarde o retorno antes de cancelar a recusa.",
            ex.Message);
        Assert.Equal(ShipmentLoadRefusalStatus.Pending, (await s.Db.Context.ShipmentLoadRefusals.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Refused_without_a_pending_refusal()
    {
        // Review Focus 5: duplo clique / recusa já concluída.
        var (s, _) = await RefuseAsync();
        await NfeLoadRefusalTestSeed.CancelRefusalService(s.Db).ExecuteAsync(s.Load.Key, "tester");
        s.Db.Context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeLoadRefusalTestSeed.CancelRefusalService(s.Db).ExecuteAsync(s.Load.Key, "tester"));

        Assert.Equal("A carga CG000001 não tem recusa aguardando NF-e.", ex.Message);
    }

    [Fact]
    public async Task Cancelling_a_linked_return_directly_is_refused()
    {
        var (s, returns) = await RefuseAsync(1);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            NfeLoadRefusalTestSeed.CancelService(s.Db).ExecuteAsync(returns[0], "tester"));

        Assert.Equal("Esta devolução pertence à recusa da carga CG000001: cancele a recusa na Montagem de Carga.", ex.Message);
    }

    [Fact]
    public async Task Cancelling_after_the_nfe_was_cancelled_by_sefaz_is_allowed()
    {
        // Review Focus 3: o 2b cancela a NF-e de uma devolução com a recusa ainda pendente.
        var (s, returns) = await RefuseAsync(2);
        var r = await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returns[0]);
        r.NfeStatus = NfeStatus.Cancelled;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();

        await NfeLoadRefusalTestSeed.CancelService(s.Db).CancelAfterNfeAsync(returns[0], "tester");

        Assert.Equal(InvoiceStatus.Cancelled, (await s.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == returns[0])).InvoiceStatus);
    }
}
```

E em `SalesInvoicesDeleteService` um teste no mesmo arquivo: `Deleting_a_linked_return_is_refused` — construir `new SalesInvoicesDeleteService(s.Db, new ShipmentLoadsBalanceHookService(s.Db.Context, new ShipmentLoadsMovementLogService(s.Db.Context)), NullLogger<SalesInvoicesDeleteService>.Instance)` e esperar a mesma mensagem (`Assert.ThrowsAnyAsync<Exception>` — o delete pode embrulhar a exceção; compare `ex.Message`).

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentLoadsCancelRefusalTests --nologo`
Expected: build error.

- [ ] **Step 3: Implement**

`SalesInvoices/SalesInvoicesRefusalLink.cs` (Create):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Devolução criada por uma recusa de carga aguardando NF-e (spec 2026-10-09 §5.4) não é cancelada nem excluída
/// avulsa: a recusa ficaria pendente com uma devolução a menos e a carga travada sem explicação.
/// </summary>
public static class SalesInvoicesRefusalLink
{
    public static async Task EnsureNotInPendingRefusalAsync(AppDbContext context, SalesInvoice invoice)
    {
        if (invoice.ShipmentLoadRefusalKey is not { } refusalKey)
            return;

        var loadCode = await context.ShipmentLoadRefusals.AsNoTracking()
            .Where(r => r.Key == refusalKey && r.Status == ShipmentLoadRefusalStatus.Pending)
            .Select(r => r.ShipmentLoad!.Code)
            .FirstOrDefaultAsync();

        if (loadCode is not null)
            throw new DefaultException(
                $"Esta devolução pertence à recusa da carga {loadCode}: cancele a recusa na Montagem de Carga.");
    }
}
```

⚠️ `db.Context` é `AppDbContext`? Conferir o tipo de `IUnitOfWork.Context`; se for outro, troque o tipo do parâmetro.

`SalesInvoicesCancelService`:
- `CancelAsync` ganha `CommitMode commitMode = CommitMode.Auto, bool fromRefusal = false`;
- novo método público:

```csharp
    /// <summary>Cancelamento da devolução pendente pela recusa de carga (spec 2026-10-09 §5.4), na transação dela.</summary>
    public Task CancelForRefusalAsync(Guid key, string userName) =>
        CancelAsync(key, userName, afterNfe: false, CommitMode.Deferred, fromRefusal: true);
```

- depois de `EnsureBusinessRules(existingInvoice);`:

```csharp
        // Pós-SEFAZ (2b) passa: a NF-e já foi cancelada e o documento precisa acompanhar (Review Focus 3).
        if (!afterNfe && !fromRefusal)
            await SalesInvoicesRefusalLink.EnsureNotInPendingRefusalAsync(db.Context, existingInvoice);
```

- `await db.BeginTransactionAsync();` → `if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();`; idem `CommitAsync` e `RollbackAsync` (no `catch`, só em `Auto`; em `Deferred` apenas `throw;` sem embrulhar):

```csharp
        catch (Exception e)
        {
            if (commitMode != CommitMode.Auto)
                throw;

            await db.RollbackAsync();
            logger.LogError(e.Message);
            throw new ApplicationException(e.Message);
        }
```

`SalesInvoicesDeleteService`: dentro do lambda, depois de `SalesInvoiceNfeLock.EnsureDeletable(entity);`:

```csharp
            await SalesInvoicesRefusalLink.EnsureNotInPendingRefusalAsync(db.Context, entity);
```

`ShipmentLoadsCancelRefusalService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// "Cancelar recusa" (spec 2026-10-09 §5.4): desfaz a recusa aguardando NF-e enquanto nenhuma NF-e de entrada dela
/// foi autorizada nem está em processamento. Cancela as devoluções vivas e destrava a carga.
/// </summary>
public class ShipmentLoadsCancelRefusalService(
    IUnitOfWork db,
    SalesInvoicesCancelService cancelService,
    ShipmentLoadsMovementLogService movementLog,
    ILogger<ShipmentLoadsCancelRefusalService> logger)
{
    public async Task<ShipmentLoad> ExecuteAsync(Guid shipmentLoadKey, string userName)
    {
        var load = await db.Context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == shipmentLoadKey)
                   ?? throw new NotFoundException($"Shipment load not found key {shipmentLoadKey}");

        var refusal = await db.Context.ShipmentLoadRefusals
                          .FirstOrDefaultAsync(x => x.ShipmentLoadKey == shipmentLoadKey &&
                                                    x.Status == ShipmentLoadRefusalStatus.Pending)
                      ?? throw new DefaultException($"A carga {load.Code} não tem recusa aguardando NF-e.");

        var returns = await db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.ShipmentLoadRefusalKey == refusal.Key && i.InvoiceStatus != InvoiceStatus.Cancelled)
            .ToListAsync();

        // Autorizada já é documento fiscal; em processamento pode virar um a qualquer momento.
        var blocking = returns.FirstOrDefault(i => i.NfeStatus is NfeStatus.Authorized or NfeStatus.Processing);
        if (blocking is not null)
            throw new DefaultException(
                $"A NF-e de entrada do documento {blocking.InvoiceNumber} já foi autorizada ou está em processamento: " +
                "cancele a NF-e ou aguarde o retorno antes de cancelar a recusa.");

        try
        {
            await db.BeginTransactionAsync();

            foreach (var returnInvoice in returns)
                await cancelService.CancelForRefusalAsync(returnInvoice.Key, userName);

            refusal.Status = ShipmentLoadRefusalStatus.Cancelled;
            refusal.CancelledAt = DateTime.Now;
            refusal.CancelledBy = userName;
            await db.SaveChangesAsync();

            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, load.Key, excludedInvoiceKeys: null);

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.RefusalCancelled,
                decimal.Zero,
                load.AvailableQuantity,
                $"Recusa aguardando NF-e cancelada: {returns.Count} devolução(ões) cancelada(s) " +
                $"({string.Join(", ", returns.Select(i => i.InvoiceNumber))}).",
                userName);

            load.UpdatedAt = DateTime.Now;
            load.UpdatedBy = userName;

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, "Erro ao cancelar a recusa da carga {Code}", load.Code);
            throw;
        }

        return load;
    }
}
```

⚠️ Conferir a assinatura de `ShipmentLoadsMovementLogService.Register` (se `movementContext` não for opcional, passe `null`).
⚠️ Ordem: o `loadHook` dentro do cancelamento recalcula a carga com a recusa ainda `Pending` (continua travada); o recálculo explícito depois de marcar `Cancelled` é o que destrava.

DI: `services.AddScoped<ShipmentLoadsCancelRefusalService>();`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "ShipmentLoadsCancelRefusalTests|SalesInvoices|Nfe|ShipmentLoads" --nologo`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsCancelRefusalService.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoicesRefusalLink.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsCancelRefusalTests.cs
git add -u SiagroB1.Application SiagroB1.Application.Tests SiagroB1.Web
git commit -m "feat(shipment): cancelar a recusa aguardando NF-e

Cancela as devoluções pendentes ou rejeitadas e destrava a carga; recusa
quando alguma NF-e de entrada já foi autorizada ou está em processamento.
A devolução ligada à recusa não é cancelada nem excluída avulsa.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: API — recusa pendente e cancelamento

**Files:**
- Create: `SiagroB1.Domain/Dtos/ShipmentLoadPendingRefusalReturnDto.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsPendingRefusalService.cs`
- Create: `SiagroB1.Web/Functions/ShipmentLoads/ShipmentLoadsGetPendingRefusalController.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsCancelRefusalController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (ao lado de `ShipmentLoadsRefuse` :916 e `ShipmentLoadsGetRefusableDocuments` :1031)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadPendingRefusalTests.cs`

**Interfaces:**
- Produces:
  - DTO (uma linha por devolução; cabeçalho repetido — a grade lê o primeiro):
    `ShipmentLoadPendingRefusalReturnDto { string RefusalKey; string Destination; string? DestinationWarehouseCode; string? DestinationWarehouseName; string Reason; string SalesInvoiceKey; string? InvoiceNumber; string? CardName; decimal Quantity; string InvoiceStatus; string NfeStatus; string? TaxDocumentNumber; string? TaxDocumentSeries; string? NfeConfirmationError }`
  - `GET odata/ShipmentLoadsGetPendingRefusal(Key={key})` → coleção (vazia sem recusa pendente).
  - `POST odata/ShipmentLoadsCancelRefusal` body `{ "Key": "<guid>" }` → `{ Key, Code, Status }`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadPendingRefusalTests
{
    [Fact]
    public async Task Lists_the_returns_of_the_pending_refusal()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync(documents: 2);
        await NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(
            new RefusalRequest(s.Load.Key, s.SaleKeys.Select(k => new RefusalLine(k, 5_000m)).ToList(),
                RefusalDestination.Warehouse, NfeLoadRefusalTestSeed.DestinationWarehouse, "Recusado"),
            "tester");

        var rows = await new ShipmentLoadsPendingRefusalService(s.Db).ExecuteAsync(s.Load.Key);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("Warehouse", r.Destination);
            Assert.Equal("ARMAZEM RETAGUARDA", r.DestinationWarehouseName);
            Assert.Equal("Recusado", r.Reason);
            Assert.Equal(5_000m, r.Quantity);
            Assert.Equal("Pending", r.InvoiceStatus);
            Assert.Equal("None", r.NfeStatus);
        });
    }

    [Fact]
    public async Task Empty_without_a_pending_refusal()
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync();

        Assert.Empty(await new ShipmentLoadsPendingRefusalService(s.Db).ExecuteAsync(s.Load.Key));
    }

    [Fact]
    public void Edm_declares_the_function_and_the_action()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();

        Assert.Single(model.SchemaElements.OfType<IEdmFunction>(), f => f.Name == "ShipmentLoadsGetPendingRefusal");
        Assert.Single(model.SchemaElements.OfType<IEdmAction>(), a => a.Name == "ShipmentLoadsCancelRefusal");
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test SiagroB1.Application.Tests --filter ShipmentLoadPendingRefusalTests --nologo`
Expected: build error.

- [ ] **Step 3: Implement**

DTO (namespace `SiagroB1.Domain.Dtos`, mesmo estilo de `ShipmentLoadRefusableDocumentDto` — copiar os atributos de chave que aquele usa, se usar `[Key]` para o OData aceitar a coleção):

```csharp
namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Uma devolução da recusa aguardando NF-e da carga (spec 2026-10-09 §7). O cabeçalho da recusa se repete em cada
/// linha: a tela lê o da primeira. Enums como string, como o resto das telas lê.
/// </summary>
public class ShipmentLoadPendingRefusalReturnDto
{
    public string RefusalKey { get; set; } = "";
    public string Destination { get; set; } = "";
    public string? DestinationWarehouseCode { get; set; }
    public string? DestinationWarehouseName { get; set; }
    public string Reason { get; set; } = "";
    public string SalesInvoiceKey { get; set; } = "";
    public string? InvoiceNumber { get; set; }
    public string? CardName { get; set; }
    public decimal Quantity { get; set; }
    public string InvoiceStatus { get; set; } = "";
    public string NfeStatus { get; set; } = "";
    public string? TaxDocumentNumber { get; set; }
    public string? TaxDocumentSeries { get; set; }
    public string? NfeConfirmationError { get; set; }
}
```

⚠️ `Quantity` como `decimal` na saída de function vira string no JSON (memória "Decimal editável usa Double"). Use `double` aqui se o `ShipmentLoadRefusableDocumentDto` usar `double` para `RefusableQuantity`; siga o vizinho.

Serviço:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>Devoluções da recusa aguardando NF-e da carga — alimenta o painel do detalhe da carga.</summary>
public class ShipmentLoadsPendingRefusalService(IUnitOfWork db)
{
    public async Task<IReadOnlyList<ShipmentLoadPendingRefusalReturnDto>> ExecuteAsync(Guid shipmentLoadKey)
    {
        var refusal = await db.Context.ShipmentLoadRefusals.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShipmentLoadKey == shipmentLoadKey && x.Status == ShipmentLoadRefusalStatus.Pending);

        if (refusal is null)
            return [];

        var returns = await db.Context.SalesInvoices.AsNoTracking()
            .Include(i => i.Items)
            .Where(i => i.ShipmentLoadRefusalKey == refusal.Key)
            .OrderBy(i => i.InvoiceNumber)
            .ToListAsync();

        return returns.Select(i => new ShipmentLoadPendingRefusalReturnDto
        {
            RefusalKey = refusal.Key!.Value.ToString(),
            Destination = refusal.Destination.ToString(),
            DestinationWarehouseCode = refusal.DestinationWarehouseCode,
            DestinationWarehouseName = refusal.DestinationWarehouseName,
            Reason = refusal.Reason,
            SalesInvoiceKey = i.Key.ToString(),
            InvoiceNumber = i.InvoiceNumber,
            CardName = i.CardName,
            Quantity = i.Items.Sum(x => x.Quantity),
            InvoiceStatus = i.InvoiceStatus.ToString(),
            NfeStatus = i.NfeStatus.ToString(),
            TaxDocumentNumber = i.TaxDocumentNumber,
            TaxDocumentSeries = i.TaxDocumentSeries,
            NfeConfirmationError = i.NfeConfirmationError,
        }).ToList();
    }
}
```

Function controller (molde de `ShipmentLoadsGetRefusableDocumentsController`):

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.ShipmentLoads;

/// <summary>Devoluções da recusa aguardando NF-e (spec 2026-10-09 §7). Vazia sem recusa pendente.</summary>
public class ShipmentLoadsGetPendingRefusalController(ShipmentLoadsPendingRefusalService service) : ODataController
{
    /// <summary>Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.</summary>
    [EnableQuery]
    [HttpGet("odata/ShipmentLoadsGetPendingRefusal(Key={key})")]
    public async Task<ActionResult<IEnumerable<ShipmentLoadPendingRefusalReturnDto>>> Get([FromRoute] Guid key) =>
        Ok(await service.ExecuteAsync(key));
}
```

Action controller (molde das guardas de `ShipmentLoadsRefuseController`):

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>"Cancelar recusa" da carga com recusa aguardando NF-e (spec 2026-10-09 §5.4).</summary>
public class ShipmentLoadsCancelRefusalController(ShipmentLoadsCancelRefusalService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsCancelRefusal")]
    public async Task<ActionResult> Cancel(ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta parâmetro declarado: sem a guarda, NRE = 500 vazio.
        if (parameters == null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is not Guid key)
            return BadRequest("Missing required parameters");

        try
        {
            var load = await service.ExecuteAsync(key, User.Identity?.Name ?? "system");
            return Ok(new { load.Key, load.Code, Status = load.Status.ToString() });
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

⚠️ Copiar de `ShipmentLoadsRefuseController` a forma como ele obtém o nome do usuário (pode ser um helper, não `User.Identity?.Name`) e o tratamento de `DbUpdateConcurrencyException`.

EDM (`ODataConfigurations.cs`), depois de `shipmentLoadsRefuse`:

```csharp
        // Recusa em dois tempos (spec 2026-10-09).
        var shipmentLoadsCancelRefusal = modelBuilder.Action("ShipmentLoadsCancelRefusal");
        shipmentLoadsCancelRefusal.Parameter<Guid>("Key");
        shipmentLoadsCancelRefusal.Returns<IActionResult>();
```

e depois de `shipmentLoadsGetRefusableDocuments`:

```csharp
        var shipmentLoadsGetPendingRefusal = modelBuilder.Function("ShipmentLoadsGetPendingRefusal");
        shipmentLoadsGetPendingRefusal.Parameter<Guid>("Key");
        shipmentLoadsGetPendingRefusal.ReturnsCollection<ShipmentLoadPendingRefusalReturnDto>();
```

DI: `services.AddScoped<ShipmentLoadsPendingRefusalService>();`.

- [ ] **Step 4: Run tests and boot the Web**

Run: `dotnet test SiagroB1.Application.Tests --filter "ShipmentLoadPendingRefusalTests|EdmModel" --nologo`
Expected: PASS.
Run: `dotnet build SiagroB1.sln --nologo` → 0 erros. A subida da Web (DI resolvendo todo o grafo novo) é feita na Task 11 contra o CEAGUI_SIAGRO_DEV.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Domain/Dtos/ShipmentLoadPendingRefusalReturnDto.cs SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsPendingRefusalService.cs SiagroB1.Web/Functions/ShipmentLoads/ShipmentLoadsGetPendingRefusalController.cs SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsCancelRefusalController.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadPendingRefusalTests.cs
git add -u SiagroB1.Web
git commit -m "feat(shipment): API da recusa aguardando NF-e

Function com as devoluções da recusa pendente e action para cancelá-la.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: Tela da carga (frontend)

Repo: `siagro-b1-frontend` — criar o branch antes: `git -C ../siagro-b1-frontend switch -c feature/load-refusal-nfe` (conferir com `git branch --show-current`).

**Files:**
- Create: `webapp/helpers/NfeActionRunner.ts`
- Create: `webapp/view/shipmentLoads/fragments/PendingRefusal.fragment.xml`
- Modify: `webapp/controller/salesInvoices/Detail.controller.ts` (`runNfeAction` usa o helper)
- Modify: `webapp/controller/shipmentLoads/Detail.controller.ts`
- Modify: `webapp/view/shipmentLoads/Detail.view.xml` (botão "Cancelar Recusa", seção nova)
- Modify: `webapp/model/formatter.ts` (`RefusalPending`)
- Modify: `webapp/model/ServerRoutes.ts`
- Modify: `webapp/controller/shipmentLoads/Main.controller.ts` (filtro de situação: `{ key: "RefusalPending", text: "Recusa aguardando NF-e" }` na lista de :26)

**Interfaces:**
- Produces: `runNfeAction(url: string, payload: Record<string, unknown>): Promise<void>` em `siagrob1/helpers/NfeActionRunner`; `ServerRoutes.shipmentLoadsPendingRefusal(key: string): string`, `ServerRoutes.shipmentLoadsCancelRefusal: string`.

- [ ] **Step 1: Helper de NF-e**

`webapp/helpers/NfeActionRunner.ts`:

```ts
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import { odataValue, sendJson } from "siagrob1/helpers/FetchHelpers";
import { NfeOutcome, nfeOutcomeMessage } from "siagrob1/helpers/NfeHelpers";

/**
 * Emitir/consultar/concluir/cancelar NF-e: 400 traz a mensagem de pré-condição/prontidão; 200 traz o desfecho
 * (autorizada, rejeitada, denegada, em processamento). Usado pelo detalhe do documento de saída e pelo painel da
 * recusa da carga — quem chama relê a tela.
 */
export async function runNfeAction(url: string, payload: Record<string, unknown>): Promise<void> {
  const result = await sendJson("POST", url, payload);

  if (!result.ok) {
    MessageBox.error(result.message);
    return;
  }

  const message = nfeOutcomeMessage(odataValue<NfeOutcome>(result.data));
  if (message.type === "success") {
    MessageToast.show(message.text);
  } else if (message.type === "warning") {
    MessageBox.warning(message.text);
  } else {
    MessageBox.error(message.text);
  }
}
```

Em `salesInvoices/Detail.controller.ts`, `runNfeAction` (privado) passa a ser:

```ts
  private async runNfeAction(url: string, ctx: Context, extra: Record<string, unknown> = {}) {
    this.setBusy(true);
    try {
      await runNfeAction(url, { Key: ctx.getProperty("Key") as string, ...extra });
    } finally {
      try {
        await ctx.requestRefresh();
      } catch {
        // a releitura falhar não pode deixar a tela ocupada
      }
      this.setBusy(false);
    }
  }
```

com `import { runNfeAction } from "siagrob1/helpers/NfeActionRunner";` — e remover dos imports o que ficar sem uso (`odataValue`, `nfeOutcomeMessage`, `NfeOutcome`, `MessageToast` só se nada mais usar; o lint acusa).

Run: `npm run ts-typecheck && npm run lint` (de `siagro-b1-frontend/`) → sem erros.

- [ ] **Step 2: Rotas, situação e filtro**

`ServerRoutes.ts`, junto das rotas da carga:

```ts
  shipmentLoadsPendingRefusal: (key: string) => `/odata/ShipmentLoadsGetPendingRefusal(Key=${key})`,
  shipmentLoadsCancelRefusal: '/odata/ShipmentLoadsCancelRefusal',
```

(Se `ServerRoutes` for um objeto só de strings, sem funções, use a string base `'/odata/ShipmentLoadsGetPendingRefusal'` e monte `(Key=...)` no controller.)

`formatter.ts`:
- `formatShipmentLoadStatus`: `m.set("RefusalPending", "Recusa aguardando NF-e");` com comentário `// Spec 2026-10-09: recusa registrada na filial com NF-e, aguardando as NF-e de entrada.`
- `stateShipmentLoadStatus`: `m.set("RefusalPending", "Error");` com comentário `// Error: a carga está travada até emitir as NF-e ou cancelar a recusa.`

`Main.controller.ts`: acrescentar `{ key: "RefusalPending", text: "Recusa aguardando NF-e" }` à lista de situações do filtro.

- [ ] **Step 3: Painel da recusa pendente**

`view/shipmentLoads/fragments/PendingRefusal.fragment.xml`:

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:t="sap.ui.table"
	xmlns:f="sap.ui.layout.form">
	<VBox>
		<MessageStrip
			type="Warning"
			showIcon="true"
			text="A recusa só é concluída quando todas as NF-e de entrada abaixo forem autorizadas. Enquanto isso a carga fica travada."
			class="sapUiSmallMarginBottom"/>
		<f:Form editable="false">
			<f:layout>
				<f:ColumnLayout columnsM="2" columnsL="3" columnsXL="3"/>
			</f:layout>
			<f:formContainers>
				<f:FormContainer>
					<f:formElements>
						<f:FormElement label="Destino">
							<f:fields><Text text="{pendingRefusal>/DestinationText}"/></f:fields>
						</f:FormElement>
						<f:FormElement label="Armazém" visible="{= !!${pendingRefusal>/DestinationWarehouseCode} }">
							<f:fields><Text text="{pendingRefusal>/DestinationWarehouseCode} - {pendingRefusal>/DestinationWarehouseName}"/></f:fields>
						</f:FormElement>
						<f:FormElement label="Motivo">
							<f:fields><Text text="{pendingRefusal>/Reason}"/></f:fields>
						</f:FormElement>
					</f:formElements>
				</f:FormContainer>
			</f:formContainers>
		</f:Form>
		<t:Table
			id="pendingRefusalTable"
			rows="{pendingRefusal>/Rows}"
			selectionMode="None"
			visibleRowCountMode="Auto"
			minAutoRowCount="2">
			<t:columns>
				<t:Column width="9rem">
					<Label text="Documento"/>
					<t:template><Link text="{pendingRefusal>InvoiceNumber}" press=".onOpenRefusalReturn"/></t:template>
				</t:Column>
				<t:Column>
					<Label text="Cliente"/>
					<t:template><Text text="{pendingRefusal>CardName}" wrapping="false"/></t:template>
				</t:Column>
				<t:Column width="9rem" hAlign="End">
					<Label text="Quantidade"/>
					<t:template>
						<Text text="{path: 'pendingRefusal>Quantity', type: 'sap.ui.model.type.Float', formatOptions: {minFractionDigits: 3, maxFractionDigits: 3}}"/>
					</t:template>
				</t:Column>
				<t:Column width="10rem">
					<Label text="NF-e"/>
					<t:template><Text text="{pendingRefusal>NfeText}" wrapping="false"/></t:template>
				</t:Column>
				<t:Column>
					<Label text="Erro de confirmação"/>
					<t:template><Text text="{pendingRefusal>NfeConfirmationError}" wrapping="false"/></t:template>
				</t:Column>
				<t:Column width="11rem">
					<Label text="Ação"/>
					<t:template>
						<HBox>
							<Button text="Emitir NF-e" type="Emphasized" press=".onIssueRefusalNfe"
								visible="{= ${pendingRefusal>InvoiceStatus} === 'Pending' &amp;&amp; (${pendingRefusal>NfeStatus} === 'None' || ${pendingRefusal>NfeStatus} === 'Rejected') }"/>
							<Button text="Consultar situação" press=".onConsultRefusalNfe"
								visible="{= ${pendingRefusal>NfeStatus} === 'Processing' }"/>
							<Button text="Concluir confirmação" press=".onCompleteRefusalNfe"
								visible="{= ${pendingRefusal>NfeStatus} === 'Authorized' &amp;&amp; ${pendingRefusal>InvoiceStatus} === 'Pending' }"/>
						</HBox>
					</t:template>
				</t:Column>
			</t:columns>
		</t:Table>
	</VBox>
</core:FragmentDefinition>
```

`Detail.view.xml`:
- botão no cabeçalho, logo depois de "Registrar Recusa":

```xml
					<!-- Spec 2026-10-09: só com recusa aguardando NF-e. O servidor recusa se alguma NF-e já saiu. -->
					<Button
						text="Cancelar Recusa"
						type="Transparent"
						press=".onCancelRefusal"
						visible="{= ${path: 'Status', targetType: 'any'} === 'RefusalPending' }"/>
```

- seção nova, logo antes de "Documentos de Saída" (:345):

```xml
			<uxap:ObjectPageSection titleUppercase="false" title="Recusa aguardando NF-e"
				visible="{pendingRefusal>/visible}">
				<uxap:subSections>
					<uxap:ObjectPageSubSection>
						<core:Fragment fragmentName="siagrob1.view.shipmentLoads.fragments.PendingRefusal" type="XML"/>
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
```

(Conferir como as outras seções incluem fragmento — `ShipmentLoadTransshipments.fragment.xml` — e copiar a forma exata, inclusive `id` se houver.)

⚠️ Comentário XML NÃO pode conter `--` (mata o fragmento sem erro nenhum).

- [ ] **Step 4: Controller da carga**

Em `shipmentLoads/Detail.controller.ts`:

Tipos no topo:

```ts
/** Linha da function ShipmentLoadsGetPendingRefusal (cabeçalho da recusa repetido por linha). */
type PendingRefusalRow = {
  RefusalKey: string;
  Destination: string;
  DestinationWarehouseCode?: string;
  DestinationWarehouseName?: string;
  Reason: string;
  SalesInvoiceKey: string;
  InvoiceNumber?: string;
  CardName?: string;
  Quantity: number;
  InvoiceStatus: string;
  NfeStatus: string;
  TaxDocumentNumber?: string;
  TaxDocumentSeries?: string;
  NfeConfirmationError?: string;
};

const REFUSAL_DESTINATION_TEXT: Record<string, string> = {
  Rebilling: "Caminhão segue viagem (refaturamento)",
  Warehouse: "Devolução ao armazém",
  Transshipment: "Transbordo",
};
```

Imports: `import { sendJson, odataCollection } from "siagrob1/helpers/FetchHelpers";`, `import { runNfeAction } from "siagrob1/helpers/NfeActionRunner";`, `import ServerRoutes from "siagrob1/model/ServerRoutes";` (conferir o caminho/forma de import de `ServerRoutes` no `salesInvoices/Detail.controller.ts`).

Em `onInit`: `this.getView().setModel(new JSONModel({ visible: false, Rows: [] }), "pendingRefusal");`

Em `detailRouteMatched` e no fim de `refreshAll` (com a `loadKey` lida antes do refresh, como os vizinhos):

```ts
    this.refreshPendingRefusal(id).catch(
      () => MessageBox.error("Erro ao carregar a recusa aguardando NF-e."));
```

Métodos:

```ts
  /* ------------------------------------------------------------------ */
  /* Recusa aguardando NF-e (spec 2026-10-09)                            */
  /* ------------------------------------------------------------------ */

  private async refreshPendingRefusal(loadKey: string): Promise<void> {
    const model = this.getView().getModel("pendingRefusal") as JSONModel;
    const result = await sendJson("GET", ServerRoutes.shipmentLoadsPendingRefusal(loadKey));

    if (!result.ok) {
      throw new Error(result.message);
    }

    const rows = odataCollection<PendingRefusalRow>(result.data);
    const first = rows[0];

    model.setData({
      visible: rows.length > 0,
      DestinationText: first ? REFUSAL_DESTINATION_TEXT[first.Destination] ?? first.Destination : "",
      DestinationWarehouseCode: first?.DestinationWarehouseCode ?? "",
      DestinationWarehouseName: first?.DestinationWarehouseName ?? "",
      Reason: first?.Reason ?? "",
      Rows: rows.map(r => ({
        ...r,
        NfeText: r.TaxDocumentNumber
          ? `${formatter.formatNfeStatus?.(r.NfeStatus) ?? r.NfeStatus} - nº ${Number(r.TaxDocumentNumber)} série ${r.TaxDocumentSeries}`
          : (formatter.formatNfeStatus?.(r.NfeStatus) ?? r.NfeStatus),
      })),
    });
  }

  private pendingRefusalRow(ev: { getSource(): unknown }): PendingRefusalRow {
    const source = ev.getSource() as { getBindingContext(model: string): { getObject(): PendingRefusalRow } };
    return source.getBindingContext("pendingRefusal").getObject();
  }

  private async runRefusalNfeAction(url: string, row: PendingRefusalRow): Promise<void> {
    this.setBusy(true);
    try {
      await runNfeAction(url, { Key: row.SalesInvoiceKey });
    } finally {
      this.setBusy(false);
      // A autorização da última NF-e conclui a recusa e muda saldo/situação: relê a carga inteira.
      this.refreshAll();
    }
  }

  async onIssueRefusalNfe(ev: Button$PressEvent): Promise<void> {
    const row = this.pendingRefusalRow(ev);
    if (!(await DialogHelper.confirmDialog(`Emitir a NF-e de entrada da devolução ${row.InvoiceNumber} ?`))) return;
    await this.runRefusalNfeAction(ServerRoutes.salesInvoicesIssueNfe, row);
  }

  async onConsultRefusalNfe(ev: Button$PressEvent): Promise<void> {
    await this.runRefusalNfeAction(ServerRoutes.salesInvoicesConsultNfe, this.pendingRefusalRow(ev));
  }

  async onCompleteRefusalNfe(ev: Button$PressEvent): Promise<void> {
    await this.runRefusalNfeAction(ServerRoutes.salesInvoicesCompleteNfeConfirmation, this.pendingRefusalRow(ev));
  }

  onOpenRefusalReturn(ev: Link$PressEvent): void {
    this.navTo("salesInvoicesDetail", { id: this.pendingRefusalRow(ev).SalesInvoiceKey });
  }

  async onCancelRefusal(): Promise<void> {
    if (!(await DialogHelper.confirmDialog(
      "Cancelar a recusa ? As devoluções ainda sem NF-e autorizada serão canceladas e a carga volta à situação anterior."))) {
      return;
    }

    this.setBusy(true);
    try {
      const result = await sendJson("POST", ServerRoutes.shipmentLoadsCancelRefusal, { Key: this._loadKey });
      if (!result.ok) {
        MessageBox.error(result.message);
        return;
      }
      MessageToast.show("Recusa cancelada.");
    } finally {
      this.setBusy(false);
      this.refreshAll();
    }
  }
```

Imports de tipo: `import { Button$PressEvent } from "sap/m/Button";` e `import { Link$PressEvent } from "sap/m/Link";`.
⚠️ `formatter.formatNfeStatus`: conferir o nome real do formatador de situação da NF-e em `formatter.ts` (`grep -n "Nfe" webapp/model/formatter.ts`) e usá-lo direto, sem `?.`; se não houver, criar um Map local com `None: "Não emitida"`, `Processing: "Em processamento"`, `Authorized: "Autorizada"`, `Rejected: "Rejeitada"`, `Denied: "Denegada"`, `Cancelled: "Cancelada"`.

Diálogo de recusa (`onConfirmRefusal`): antes do `confirmDialog`, ler se a filial emite NF-e pelo Siagro — o controller já tem `isTaxCalculationActive(branchCode)` (usado pelo faturamento); use-o com o `BranchCode` da carga (`await ctx.requestProperty("BranchCode")`). Se ativo, a pergunta vira:

```ts
"Confirma a recusa ? Serão criadas as devoluções com NF-e de entrada; a recusa só é concluída depois que todas forem autorizadas."
```

e o toast depois do `invoke()`:

```ts
"Recusa registrada. Emita as NF-e de entrada no painel \"Recusa aguardando NF-e\"."
```

- [ ] **Step 5: Typecheck and lint**

Run (de `siagro-b1-frontend/`): `npm run ts-typecheck && npm run lint`
Expected: sem erros.

- [ ] **Step 6: Commit (repo do frontend)**

```bash
git -C ../siagro-b1-frontend add webapp/helpers/NfeActionRunner.ts webapp/view/shipmentLoads/fragments/PendingRefusal.fragment.xml
git -C ../siagro-b1-frontend add -u webapp
git -C ../siagro-b1-frontend commit -m "feat(shipment): painel da recusa aguardando NF-e na carga

Emitir, consultar e concluir as NF-e de entrada das devoluções sem sair
da carga, e cancelar a recusa pendente.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

(Conferir no `CLAUDE.md` do frontend se o escopo/forma do commit difere do backend.)

---

### Task 11: Verificação ponta a ponta (CEAGUI_SIAGRO_DEV, homologação)

Sem código novo; corrigir o que aparecer em task própria (TDD) antes de seguir.

- [ ] **Step 1:** Suite inteira: `dotnet test SiagroB1.Application.Tests --nologo` e `dotnet test SiagroB1.Fiscal.Tests --nologo` → verdes; `npm run ts-typecheck && npm run lint` → limpos.
- [ ] **Step 2:** Aplicar `AddShipmentLoadRefusals` SÓ no `CEAGUI_SIAGRO_DEV` (perfil `ceagui`, `ASPNETCORE_ENVIRONMENT` explícito — ver memória `db-migration-profile-targets-production`). Conferir com `SELECT name, filter_definition FROM sys.indexes WHERE object_id = OBJECT_ID('SHIPMENT_LOAD_REFUSALS')`.
- [ ] **Step 3:** Subir Web + Gateway (perfil `ceagui`) e `yarn start:dev`; login admin.
- [ ] **Step 4:** Carga com dois documentos de saída com NF-e AUTORIZADA em homologação (criar se não houver: expedir, faturar, transmitir). Recusa parcial para Armazém nos dois. Conferir: carga "Recusa aguardando NF-e", painel com 2 linhas, Faturar/Registrar Recusa ocultos, saldo inalterado.
- [ ] **Step 5:** Emitir a 1ª NF-e pelo painel → autorizada; carga continua travada, painel mostra a 1ª como Autorizada sem botão. Emitir a 2ª → autorizada; painel some, carga "Devolvida"/"Faturada Parcial" conforme as quantidades, romaneio 12 no armazém de destino na seção "Devoluções ao Armazém", movimentos registrados. DANFE da devolução: entrada, finalidade 4, referência no item.
- [ ] **Step 6:** Nova carga (ou a mesma com saldo) → recusa para Refaturamento → "Cancelar Recusa" antes de emitir → devoluções Canceladas, carga de volta à situação anterior, movimento "Recusa cancelada".
- [ ] **Step 7:** Tentar cancelar pela tela do documento uma devolução de recusa pendente → mensagem "Esta devolução pertence à recusa da carga ...".
- [ ] **Step 8:** Registrar o resultado (números das NF-e, protocolos) na memória do projeto e no relatório final.
