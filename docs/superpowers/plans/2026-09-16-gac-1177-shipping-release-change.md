# GAC-1177 — Troca de Liberação da Expedição — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir trocar a liberação de embarque (e, com ela, o contrato de compra/fornecedor) de uma Expedição de Grãos que está numa carga, mesmo faturada, sem estornar documentos de saída — incluindo inverter duas Expedições.

**Architecture:** Um serviço de aplicação em lote (`ShippingTransactionsChangeReleaseService`) valida tudo antes de escrever, depois, numa transação: remove as alocações atuais, regrava as pernas (criando ou cancelando a perna de compra quando a origem da liberação muda), realoca, recalcula `ShippedQuantity` de todas as liberações tocadas, confere saldo e narra na Movimentação da carga. Exposto por uma action OData e acionado de um botão no grid "Romaneios da Carga".

**Tech Stack:** .NET 10, EF Core (SQL Server; testes em InMemory), ASP.NET Core OData v4, xUnit; OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-09-16-gac-1177-shipping-release-change-design.md`

## Global Constraints

- Identificadores em inglês; texto que o usuário lê (mensagens de negócio, rótulos) em pt-BR.
- **Nunca commitar nem dar push.** Todo arquivo NOVO recebe `git add <path>` imediatamente, no repo a que pertence (`siagro-b1-backend` ou `siagro-b1-frontend`). Os passos "Commit" do template viram "Stage".
- Sem migration: `ShipmentLoadMovementType` é int persistido; valor novo só no fim (`= 14`).
- Motivo obrigatório; contrato `Finished` na origem ou no destino recusa; destino = mesmo produto, mesmo armazém, liberação `Actived`, com saldo (conferido após liberar todo o lote).
- Documentos de saída e carga (`TotalQuantity`, `Status`, `SalesInvoiceKey`) **não** são alterados.

Comandos (a partir de `C:\Projetos\SiagroB1\siagro-b1-backend`):
- Testes do recorte: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShippingTransactions"`
- Build: `dotnet build SiagroB1.sln`

---

## File map

| Arquivo | Responsabilidade |
|---|---|
| Create `SiagroB1.Application/Services/ShippingTransactions/ShipmentReleaseLotRules.cs` | Regra do lote da liberação (extraída do Create) — fonte única |
| Modify `.../ShippingTransactions/ShippingTransactionsCreateService.cs` | Passa a usar `ShipmentReleaseLotRules` |
| Create `.../ShippingTransactions/ShippingTransactionsChangeReleaseService.cs` | O serviço da troca |
| Modify `SiagroB1.Domain/Enums/ShipmentLoadMovementType.cs` | `ReleaseChanged = 14` |
| Create `SiagroB1.Web/Actions/ShippingTransactions/ShippingTransactionsChangeReleaseController.cs` | Action OData |
| Modify `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` | Declara a action |
| Modify `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` | DI |
| Create `SiagroB1.Application.Tests/ShippingTransactions/ShippingTransactionsChangeReleaseServiceTests.cs` | Testes |
| Front: Modify `webapp/view/shipmentLoads/Detail.view.xml`, `webapp/controller/shipmentLoads/Detail.controller.ts`, `webapp/model/formatter.ts`; Create `webapp/view/shipmentLoads/fragments/ChangeRelease.fragment.xml` | UI |

---

### Task 1: Extrair a regra do lote para `ShipmentReleaseLotRules`

Refatoração sem mudança de comportamento. O serviço novo precisa da mesma regra com uma quantidade exigida ajustada (lote que já contém o próprio romaneio).

**Files:**
- Create: `SiagroB1.Application/Services/ShippingTransactions/ShipmentReleaseLotRules.cs`
- Modify: `SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsCreateService.cs` (remove `ResolveReleaseLotAsync`, L149-204; chamada na L47)
- Test (regressão existente): `SiagroB1.Application.Tests/ShippingTransactions/ShippingTransactionsLotAwarenessTests.cs`

**Interfaces:**
- Produces: `static Task<StorageAddress?> ShipmentReleaseLotRules.ResolveAsync(AppDbContext context, IStorageAddressBalanceReader balanceReader, ShipmentRelease? release, string itemCode, string warehouseCode, decimal requiredQuantity)`

- [ ] **Step 1: Rodar a suíte de lote antes (linha de base verde)**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShippingTransactions"`
Expected: PASS (todos).

- [ ] **Step 2: Criar o helper** — move o corpo de `ResolveReleaseLotAsync` com XML-doc/remarks, trocando `purchase.X` por parâmetros:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShippingTransactions;

/// <summary>
/// Lote de onde a mercadoria vai sair, quando a liberação aponta para um. Fonte única usada pela
/// Expedição de Grãos e pela troca de liberação (GAC-1177).
/// </summary>
/// <remarks>
/// (copiar aqui, sem alteração, o &lt;summary&gt; e o &lt;remarks&gt; atuais de
/// ResolveReleaseLotAsync — o aviso de RequiresStorageAddress ≠ ShipsWithoutPurchaseLeg)
/// <para>
/// <paramref name="requiredQuantity"/> é o volume que precisa CABER no saldo do lote. Na criação é o
/// peso bruto do embarque; na troca é descontado o peso dos romaneios do mesmo lote que estão
/// saindo dele na mesma operação — senão inverter duas expedições do mesmo lote falharia por
/// saldo que a própria troca devolve.
/// </para>
/// </remarks>
public static class ShipmentReleaseLotRules
{
    public static async Task<StorageAddress?> ResolveAsync(
        AppDbContext context,
        IStorageAddressBalanceReader balanceReader,
        ShipmentRelease? release,
        string itemCode,
        string warehouseCode,
        decimal requiredQuantity)
    {
        if (release == null)
            return null;

        if (string.IsNullOrEmpty(release.StorageAddressCode))
        {
            if (ReleaseOriginRules.RequiresStorageAddress(release.Origin))
                throw new ApplicationException(
                    "Liberação de transferência de propriedade sem lote de armazenagem vinculado.");

            return null;
        }

        var lot = await context.StorageAddresses
                      .AsNoTracking()
                      .FirstOrDefaultAsync(x => x.Code == release.StorageAddressCode)
                  ?? throw new ApplicationException(
                      $"Lote de armazenagem {release.StorageAddressCode} não encontrado.");

        if (!string.Equals(lot.ItemCode, itemCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O produto do lote ({lot.ItemCode}) é diferente do produto do embarque ({itemCode}).");

        if (balanceReader.GetBalance(lot.Code!) < requiredQuantity)
            throw new ApplicationException(
                $"Saldo insuficiente no lote {lot.Code}: o produto desta liberação já foi movimentado.");

        if (!string.Equals(lot.WarehouseCode, warehouseCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O armazém do embarque ({warehouseCode}) é diferente do armazém do lote {lot.Code} ({lot.WarehouseCode}).");

        return lot;
    }
}
```

- [ ] **Step 3: Usar no Create** — na L47 substituir:

```csharp
var lot = await ShipmentReleaseLotRules.ResolveAsync(
    unitOfWork.Context, balanceReader, release, purchase.ItemCode, purchase.WarehouseCode, purchase.GrossWeight);
```
e apagar o método privado `ResolveReleaseLotAsync` inteiro (com seus comentários, que agora vivem no helper).

- [ ] **Step 4: Rodar de novo**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShippingTransactions"`
Expected: PASS, mesma contagem do Step 1.

- [ ] **Step 5: Stage** — `git add SiagroB1.Application/Services/ShippingTransactions/ShipmentReleaseLotRules.cs`

---

### Task 2: Serviço — validações + troca Standard→Standard + inversão + Movimentação

**Files:**
- Create: `SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsChangeReleaseService.cs`
- Modify: `SiagroB1.Domain/Enums/ShipmentLoadMovementType.cs`
- Test: `SiagroB1.Application.Tests/ShippingTransactions/ShippingTransactionsChangeReleaseServiceTests.cs`

**Interfaces:**
- Consumes: `ShipmentReleaseLotRules.ResolveAsync` (Task 1); `PurchaseContractsAllocationDeleteService.ExecuteAsync(Guid key, string userName, CommitMode = Auto)`; `PurchaseContractsAllocationCreateService.ExecuteAsync(Guid purchaseContractKey, StorageTransaction st, decimal volume, string userName, CommitMode = Auto)`; `ShipmentReleasesRecalculateShippedService.RecalculateAsync(Guid)`; `ShipmentLoadsMovementLogService.Register(...)`.
- Produces:
  - `public sealed record ShippingReleaseChangeItem(Guid SalesStorageTransactionKey, Guid TargetShipmentReleaseKey);`
  - `public Task ShippingTransactionsChangeReleaseService.ExecuteAsync(IReadOnlyList<ShippingReleaseChangeItem> items, string? reason, string userName)`
  - `ShipmentLoadMovementType.ReleaseChanged = 14`

- [ ] **Step 1: Enum** — em `ShipmentLoadMovementType.cs`, trocar a última linha:

```csharp
    ReturnedToWarehouse = 13,  // Mercadoria devolvida a armazém — retira o saldo da carga
    ReleaseChanged = 14        // Liberação/contrato de compra de um romaneio trocado (GAC-1177) — não mexe no saldo
```

- [ ] **Step 2: Escrever o arquivo de testes (fixture + casos Standard)**

Fixture copiada de `ShippingTransactionsReverseServiceTests` (mesmos fakes). O cenário real é criado pelo `ShippingTransactionsCreateService` e então a perna de venda é posta numa carga faturada.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShippingTransactions;

/// <summary>
/// GAC-1177: trocar a liberação (e o contrato de compra) de uma Expedição que está numa carga,
/// mesmo faturada, sem estornar documento de saída. O peso da carga não muda.
/// </summary>
public class ShippingTransactionsChangeReleaseServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private decimal _lotBalance = 100_000m;

    private ShipmentReleasesRecalculateShippedService Recalc() => new(_db.Context);

    private StorageTransactionsGetService GetService() =>
        new(_db, NullLogger<StorageTransactionsGetService>.Instance);

    private StorageTransactionsCreateService StorageCreate(FakeDocNumberSequenceService docNumbers) =>
        new(_db,
            docNumbers,
            new FakeBusinessPartnerService(new() { ["F0001"] = "Produtor Um", ["F0002"] = "Produtor Dois" }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { ["01"] = "Armazém 01" }),
            Recalc(),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    private StorageTransactionsConfirmedService StorageConfirmed() =>
        new(_db, new FakeStringLocalizer<Resource>(), Recalc(),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

    private ShippingTransactionsCreateService CreateService()
    {
        var docNumbers = new FakeDocNumberSequenceService();
        var storageCreate = StorageCreate(docNumbers);
        return new ShippingTransactionsCreateService(
            _db, storageCreate, StorageConfirmed(),
            new StorageTransactionsCopyService(_db, docNumbers, storageCreate, new FakeStringLocalizer<Resource>()),
            new PurchaseContractsAllocationCreateService(_db, GetService()),
            Recalc(),
            new FakeStorageAddressBalanceReader(_lotBalance));
    }

    private ShippingTransactionsChangeReleaseService Service() =>
        new(_db,
            StorageCreate(new FakeDocNumberSequenceService()),
            StorageConfirmed(),
            new PurchaseContractsAllocationCreateService(_db, GetService()),
            new PurchaseContractsAllocationDeleteService(_db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance),
            Recalc(),
            new ShipmentLoadsMovementLogService(_db.Context),
            new FakeStorageAddressBalanceReader(_lotBalance));

    private async Task<(PurchaseContract Contract, ShipmentRelease Release)> SeedReleaseAsync(
        string contractCode,
        string cardCode,
        decimal releasedQuantity = 1500m,
        ReleaseOrigin origin = ReleaseOrigin.Standard,
        string? lotCode = null,
        ContractStatus contractStatus = ContractStatus.Approved,
        ReleaseStatus releaseStatus = ReleaseStatus.Actived,
        string itemCode = "SOJA",
        string warehouseCode = "01")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = contractCode,
            CardCode = cardCode,
            CardName = cardCode == "F0001" ? "Produtor Um" : "Produtor Dois",
            ItemCode = itemCode,
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = warehouseCode,
            Status = ContractStatus.Approved,
            TotalVolume = 10_000m,
            AllocatedVolume = 0m,
        };

        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = warehouseCode,
            ReleasedQuantity = releasedQuantity,
            ShippedQuantity = 0m,
            Status = ReleaseStatus.Actived,
            Origin = origin,
            StorageAddressCode = lotCode,
        };

        if (lotCode != null && !await _db.Context.StorageAddresses.AnyAsync(x => x.Code == lotCode))
        {
            _db.Context.StorageAddresses.Add(new StorageAddress
            {
                Code = lotCode,
                Description = "Lote próprio",
                CardCode = "E0001",
                ItemCode = itemCode,
                WarehouseCode = warehouseCode,
                UoM = "KG",
                OwnershipType = StorageOwnershipType.OwnedInOurCustody,
                Status = StorageAddressStatus.Open,
            });
        }

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();

        // Status finais só depois de expedir: um contrato encerrado/liberação pausada não
        // aceitaria o embarque que monta o cenário.
        contract.Status = contractStatus;
        release.Status = releaseStatus;
        await _db.Context.SaveChangesAsync();

        return (contract, release);
    }

    /// <summary>
    /// Expedição real (pelo serviço de criação) sobre a liberação, com a perna de venda montada
    /// numa carga já faturada — o cenário do chamado.
    /// </summary>
    private async Task<(ShippingTransaction Shipping, ShipmentLoad Load)> ShipIntoInvoicedLoadAsync(
        PurchaseContract contract, ShipmentRelease release, decimal gross, ShipmentLoad? load = null)
    {
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            CardCode = contract.CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = StorageTransactionType.Purchase,
            TransactionStatus = StorageTransactionsStatus.Pending,
            GrossWeight = gross,
            ShipmentReleaseKey = release.Key,
        };

        var withoutPurchaseLeg = ReleaseOriginRules.ShipsWithoutPurchaseLeg(release.Origin);
        var shipping = await CreateService().ExecuteAsync(
            withoutPurchaseLeg ? null : contract.Key, st, "tester");

        load ??= new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "01",
            Status = ShipmentLoadStatus.Invoiced,
        };

        if (_db.Context.Entry(load).State == EntityState.Detached)
            _db.Context.ShipmentLoads.Add(load);

        var sales = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == shipping.SalesStorageTransactionKey);
        sales.ShipmentLoadKey = load.Key;
        sales.TransactionStatus = StorageTransactionsStatus.Invoiced;
        sales.SalesInvoiceKey = Guid.NewGuid();
        load.TotalQuantity += gross;
        load.InvoicedQuantity += gross;
        await _db.Context.SaveChangesAsync();

        return (shipping, load);
    }

    private Task<StorageTransaction> Tx(Guid key) =>
        _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == key);

    private Task<ShipmentRelease> ReleaseOf(Guid key) =>
        _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key);

    private Task<PurchaseContract> ContractOf(Guid key) =>
        _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task StandardToStandard_MovesAllocationReleaseAndSupplier_LeavingInvoiceAndLoadIntact()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, load) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        var salesBefore = await Tx(shipping.SalesStorageTransactionKey);

        await Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "Relatório do armazém", "tester");

        var purchase = await Tx(shipping.PurchaseStorageTransactionKey!.Value);
        var sales = await Tx(shipping.SalesStorageTransactionKey);

        Assert.Equal(r2.Key, purchase.ShipmentReleaseKey);
        Assert.Equal(r2.Key, sales.ShipmentReleaseKey);
        Assert.Equal("F0002", purchase.CardCode);
        Assert.Equal("F0002", sales.CardCode);
        Assert.Equal(StorageTransactionsStatus.Confirmed, purchase.TransactionStatus);

        var allocations = await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .Where(x => x.StorageTransactionKey == purchase.Key).ToListAsync();
        var allocation = Assert.Single(allocations);
        Assert.Equal(c2.Key, allocation.PurchaseContractKey);

        Assert.Equal(0m, (await ContractOf(c1.Key)).AllocatedVolume);
        Assert.Equal(purchase.NetWeight, (await ContractOf(c2.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ReleaseOf(r1.Key)).ShippedQuantity);
        Assert.Equal(purchase.NetWeight, (await ReleaseOf(r2.Key)).ShippedQuantity);

        // Nota e carga intactas.
        Assert.Equal(StorageTransactionsStatus.Invoiced, sales.TransactionStatus);
        Assert.Equal(salesBefore.SalesInvoiceKey, sales.SalesInvoiceKey);
        Assert.Equal(load.Key, sales.ShipmentLoadKey);
        var loadAfter = await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key);
        Assert.Equal(1000m, loadAfter.TotalQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, loadAfter.Status);

        var movement = await _db.Context.ShipmentLoadMovements.AsNoTracking()
            .SingleAsync(x => x.ShipmentLoadKey == load.Key && x.MovementType == ShipmentLoadMovementType.ReleaseChanged);
        Assert.Equal(0m, movement.Quantity);
        Assert.Equal("Relatório do armazém", movement.Reason);
        Assert.Equal(sales.Key, movement.StorageTransactionKey);
        Assert.Contains("PC-001", movement.Description);
        Assert.Contains("PC-002", movement.Description);
    }

    /// <summary>
    /// Inversão: as duas liberações só têm saldo para o volume que já consumiram. Conferir o
    /// saldo antes de liberar as duas recusaria uma troca que, no fim, fecha certinho.
    /// </summary>
    [Fact]
    public async Task Swap_TwoShipmentsWithTightReleases_Succeeds()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001", releasedQuantity: 1000m);
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002", releasedQuantity: 1000m);
        var (s1, load) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        var (s2, _) = await ShipIntoInvoicedLoadAsync(c2, r2, 1000m, load);

        await Service().ExecuteAsync(
            [new(s1.SalesStorageTransactionKey, r2.Key), new(s2.SalesStorageTransactionKey, r1.Key)],
            "Ordem financeira", "tester");

        Assert.Equal(r2.Key, (await Tx(s1.SalesStorageTransactionKey)).ShipmentReleaseKey);
        Assert.Equal(r1.Key, (await Tx(s2.SalesStorageTransactionKey)).ShipmentReleaseKey);
        Assert.Equal(1000m, (await ReleaseOf(r1.Key)).ShippedQuantity);
        Assert.Equal(1000m, (await ReleaseOf(r2.Key)).ShippedQuantity);
        Assert.Equal(1000m, (await ContractOf(c1.Key)).AllocatedVolume);
        Assert.Equal(1000m, (await ContractOf(c2.Key)).AllocatedVolume);
        Assert.Equal(2, await _db.Context.ShipmentLoadMovements
            .CountAsync(x => x.MovementType == ShipmentLoadMovementType.ReleaseChanged));
    }

    [Fact]
    public async Task Rejects_WhenTargetReleaseHasNoBalance()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", releasedQuantity: 500m);
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("Saldo insuficiente na liberação", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Rejects_WithoutReason(string? reason)
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], reason, "tester"));

        Assert.Contains("motivo", ex.Message);
    }

    [Fact]
    public async Task Rejects_WhenSourceContractIsFinished()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        var tracked = await _db.Context.PurchaseContracts.SingleAsync(x => x.Key == c1.Key);
        tracked.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("PC-001", ex.Message);
        Assert.Contains("encerrado", ex.Message);
    }

    [Fact]
    public async Task Rejects_WhenTargetContractIsFinished()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", contractStatus: ContractStatus.Finished);
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("PC-002", ex.Message);
        Assert.Contains("encerrado", ex.Message);
    }

    [Theory]
    [InlineData(ReleaseStatus.Paused)]
    [InlineData(ReleaseStatus.Completed)]
    [InlineData(ReleaseStatus.Cancelled)]
    public async Task Rejects_WhenTargetReleaseIsNotActive(ReleaseStatus status)
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", releaseStatus: status);
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("não está ativa", ex.Message);
    }

    [Fact]
    public async Task Rejects_WhenTargetIsOtherWarehouse()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", warehouseCode: "02");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("armazém", ex.Message);
    }

    [Fact]
    public async Task Rejects_WhenTargetIsOtherItem()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", itemCode: "MILHO");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("produto", ex.Message);
    }

    [Fact]
    public async Task Rejects_WhenTargetIsTheCurrentRelease()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r1.Key)], "motivo", "tester"));

        Assert.Contains("já está", ex.Message);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Cancelled)]
    [InlineData(ShipmentLoadStatus.Returned)]
    public async Task Rejects_WhenLoadIsCancelledOrReturned(ShipmentLoadStatus status)
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, load) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        load.Status = status;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("CG000001", ex.Message);
    }

    [Fact]
    public async Task Rejects_DuplicatedShipment()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key), new(shipping.SalesStorageTransactionKey, r1.Key)],
            "motivo", "tester"));
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShippingTransactionsChangeReleaseServiceTests"`
Expected: FAIL de compilação — `ShippingTransactionsChangeReleaseService` / `ShippingReleaseChangeItem` não existem.

- [ ] **Step 4: Implementar o serviço** (já com os 4 ramos de origem; os testes dos ramos cruzados vêm na Task 3):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.StorageTransactions.Factories;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.ShippingTransactions;

public sealed record ShippingReleaseChangeItem(Guid SalesStorageTransactionKey, Guid TargetShipmentReleaseKey);

/// <summary>
/// Troca a liberação de embarque — e com ela o contrato de compra e o fornecedor — de Expedições
/// que já estão numa carga, inclusive faturada (GAC-1177). O caso: o armazém informa depois qual
/// contrato baixou, o produtor diz que a baixa foi do lote da Yokotobi, ou o financeiro pede outro
/// contrato primeiro.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que não passa pelo guard de composição da carga:</b> o PESO não muda. A perna de saída
/// continua na carga com o mesmo bruto e o mesmo documento de saída; muda só o vínculo comercial.
/// A carga e as notas não são tocadas.
/// </para>
/// <para>
/// <b>Lote:</b> vários itens numa chamada é o que permite INVERTER duas expedições. Por isso tudo
/// é validado antes de escrever, as alocações atuais saem TODAS antes de qualquer alocação nova e
/// o saldo das liberações é conferido só no fim, depois do recálculo.
/// </para>
/// <para>
/// <b>Mudança de origem</b> (<see cref="ReleaseOriginRules.ShipsWithoutPurchaseLeg"/>): indo para
/// transferência/devolução, a perna de compra é cancelada (o grão já é nosso, no lote); vindo delas
/// para uma compra comum, a perna de compra é criada a partir da de saída, como a Expedição faria.
/// Ver <see cref="ShippingTransactionsCreateService"/>.
/// </para>
/// </remarks>
public class ShippingTransactionsChangeReleaseService(
    IUnitOfWork db,
    StorageTransactionsCreateService storageCreateService,
    StorageTransactionsConfirmedService storageConfirmedService,
    PurchaseContractsAllocationCreateService allocationCreateService,
    PurchaseContractsAllocationDeleteService allocationDeleteService,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    ShipmentLoadsMovementLogService movementLog,
    IStorageAddressBalanceReader balanceReader)
{
    private const decimal Tolerance = 0.001m;

    private sealed class Plan
    {
        public required ShippingTransaction Shipping { get; init; }
        public required StorageTransaction Sales { get; init; }
        public StorageTransaction? Purchase { get; init; }
        public required ShipmentLoad Load { get; init; }
        public required ShipmentRelease Source { get; init; }
        public required ShipmentRelease Target { get; init; }
        public StorageAddress? TargetLot { get; set; }
    }

    public async Task ExecuteAsync(IReadOnlyList<ShippingReleaseChangeItem> items, string? reason, string userName)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo da troca de liberação.");

        if (items.Count == 0)
            throw new ApplicationException("Selecione ao menos um romaneio para trocar a liberação.");

        if (items.Select(x => x.SalesStorageTransactionKey).Distinct().Count() != items.Count)
            throw new ApplicationException("O mesmo romaneio foi informado mais de uma vez.");

        var plans = new List<Plan>();
        foreach (var item in items)
            plans.Add(await BuildPlanAsync(item));

        await ResolveLotsAsync(plans);

        try
        {
            await db.BeginTransactionAsync();

            // Fase 1 — devolve TODO o volume aos contratos antes de alocar de novo. Auto (salva a
            // cada remoção) de propósito: o delete recalcula o contrato por SumAsync no banco, que
            // não enxerga remoção rastreada de um item anterior do mesmo contrato.
            foreach (var plan in plans.Where(p => p.Purchase != null))
            {
                var allocationKeys = await db.Context.PurchaseContractsAllocations
                    .Where(x => x.StorageTransactionKey == plan.Purchase!.Key)
                    .Select(x => x.Key)
                    .ToListAsync();

                foreach (var allocationKey in allocationKeys)
                    await allocationDeleteService.ExecuteAsync(allocationKey, userName);
            }

            // Fase 2 — regrava cada expedição conforme a origem da liberação de destino.
            foreach (var plan in plans)
                await ApplyAsync(plan, userName);

            await db.SaveChangesAsync();

            // Fase 3 — recálculo de TODAS as liberações tocadas (origem e destino), dentro da
            // transação: a regravação escreve direto nas pernas, sem os hooks de ShippedQuantity.
            var releaseKeys = plans.SelectMany(p => new[] { p.Source.Key, p.Target.Key }).Distinct();
            foreach (var releaseKey in releaseKeys)
                await recalcShipped.RecalculateAsync(releaseKey);

            // Fase 4 — saldo das liberações de destino, só agora que o lote inteiro foi aplicado.
            foreach (var target in plans.Select(p => p.Target).DistinctBy(x => x.Key))
            {
                var balance = target.ReleasedQuantity - target.ShippedQuantity;
                if (balance < -Tolerance)
                    throw new ApplicationException(
                        $"Saldo insuficiente na liberação do contrato {target.PurchaseContract!.Code}: " +
                        $"faltam {-balance:N3}.");
            }

            // Fase 5 — narrativa na carga. Quantidade zero: a troca não mexe no saldo dela.
            foreach (var plan in plans)
            {
                var from = plan.Source.PurchaseContract!;
                var to = plan.Target.PurchaseContract!;

                movementLog.Register(
                    plan.Load.Key,
                    ShipmentLoadMovementType.ReleaseChanged,
                    0m,
                    plan.Load.AvailableQuantity,
                    $"Romaneio {plan.Sales.Code}: liberação do contrato {from.Code} ({from.CardName}) " +
                    $"trocada pela do contrato {to.Code} ({to.CardName}).",
                    userName,
                    movementContext: new ShipmentLoadMovementContext(
                        CardCode: to.CardCode,
                        CardName: to.CardName,
                        Reason: reason,
                        StorageTransactionKey: plan.Sales.Key));
            }

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }

    private async Task<Plan> BuildPlanAsync(ShippingReleaseChangeItem item)
    {
        var shipping = await db.Context.ShippingTransactions
                           .Include(x => x.PurchaseStorageTransaction)
                           .Include(x => x.SalesStorageTransaction)
                           .FirstOrDefaultAsync(x => x.SalesStorageTransactionKey == item.SalesStorageTransactionKey)
                       ?? throw new NotFoundException("Expedição não encontrada para o romaneio informado.");

        var sales = shipping.SalesStorageTransaction!;

        if (sales.TransactionStatus is StorageTransactionsStatus.Cancelled or StorageTransactionsStatus.Returned)
            throw new ApplicationException(
                $"O romaneio {sales.Code} está cancelado ou devolvido e não pode ter a liberação trocada.");

        if (sales.ShipmentLoadKey is null)
            throw new ApplicationException($"O romaneio {sales.Code} não está em nenhuma carga.");

        var load = await db.Context.ShipmentLoads.FirstAsync(x => x.Key == sales.ShipmentLoadKey);

        if (load.Status is ShipmentLoadStatus.Cancelled or ShipmentLoadStatus.Returned)
            throw new ApplicationException(
                $"A carga {load.Code} está cancelada ou devolvida: a liberação dos romaneios não pode ser trocada.");

        if (sales.ShipmentReleaseKey is null)
            throw new ApplicationException($"O romaneio {sales.Code} não tem liberação de embarque.");

        if (sales.ShipmentReleaseKey == item.TargetShipmentReleaseKey)
            throw new ApplicationException($"O romaneio {sales.Code} já está na liberação escolhida.");

        var source = await LoadReleaseAsync(sales.ShipmentReleaseKey.Value);
        var target = await LoadReleaseAsync(item.TargetShipmentReleaseKey);
        var targetContract = target.PurchaseContract!;

        if (!string.Equals(targetContract.ItemCode, sales.ItemCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"A liberação do contrato {targetContract.Code} é de outro produto ({targetContract.ItemCode}).");

        if (!string.Equals(target.DeliveryLocationCode, sales.WarehouseCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"A liberação do contrato {targetContract.Code} é de outro armazém ({target.DeliveryLocationCode}).");

        if (target.Status != ReleaseStatus.Actived)
            throw new ApplicationException($"A liberação do contrato {targetContract.Code} não está ativa.");

        foreach (var contract in new[] { source.PurchaseContract!, targetContract })
        {
            if (contract.Status == ContractStatus.Finished)
                throw new ApplicationException(
                    $"O contrato {contract.Code} está encerrado: não é possível trocar a liberação.");
        }

        return new Plan
        {
            Shipping = shipping,
            Sales = sales,
            Purchase = shipping.PurchaseStorageTransaction,
            Load = load,
            Source = source,
            Target = target,
        };
    }

    private async Task<ShipmentRelease> LoadReleaseAsync(Guid key) =>
        await db.Context.ShipmentReleases
            .Include(x => x.PurchaseContract)
            .FirstOrDefaultAsync(x => x.Key == key)
        ?? throw new NotFoundException("Liberação de embarque não encontrada.");

    /// <summary>
    /// Lote de destino de cada item. O volume exigido de um lote desconta o bruto dos romaneios
    /// que estão SAINDO dele nesta mesma troca — o saldo que o leitor enxerga ainda os debita.
    /// </summary>
    private async Task ResolveLotsAsync(List<Plan> plans)
    {
        foreach (var plan in plans)
        {
            var lotCode = plan.Target.StorageAddressCode;

            var required = lotCode == null
                ? plan.Sales.GrossWeight
                : plans.Where(p => p.Target.StorageAddressCode == lotCode).Sum(p => p.Sales.GrossWeight)
                  - plans.Where(p => p.Sales.StorageAddressCode == lotCode).Sum(p => p.Sales.GrossWeight);

            plan.TargetLot = await ShipmentReleaseLotRules.ResolveAsync(
                db.Context, balanceReader, plan.Target, plan.Sales.ItemCode, plan.Sales.WarehouseCode, required);
        }
    }

    private async Task ApplyAsync(Plan plan, string userName)
    {
        var contract = plan.Target.PurchaseContract!;
        var sales = plan.Sales;

        // Perna de saída primeiro: a perna de compra criada abaixo é CÓPIA dela.
        sales.ShipmentReleaseKey = plan.Target.Key;
        sales.CardCode = contract.CardCode;
        sales.CardName = contract.CardName;
        sales.StorageAddressCode = plan.TargetLot?.Code;
        Touch(sales, userName);

        var targetHasPurchaseLeg = !ReleaseOriginRules.ShipsWithoutPurchaseLeg(plan.Target.Origin);

        if (targetHasPurchaseLeg)
        {
            var purchase = plan.Purchase ?? await CreatePurchaseLegAsync(plan, userName);

            purchase.ShipmentReleaseKey = plan.Target.Key;
            purchase.CardCode = contract.CardCode;
            purchase.CardName = contract.CardName;
            Touch(purchase, userName);

            await db.SaveChangesAsync();

            if (purchase.NetWeight > contract.AvaiableVolume + Tolerance)
                throw new ApplicationException(
                    $"O contrato {contract.Code} não tem saldo para {purchase.NetWeight:N3} " +
                    $"(disponível {contract.AvaiableVolume:N3}).");

            await allocationCreateService.ExecuteAsync(contract.Key, purchase, purchase.NetWeight, userName);
            return;
        }

        // Destino sem perna de compra: o grão já é nosso. A compra desta expedição deixa de existir.
        if (plan.Purchase != null)
        {
            plan.Purchase.TransactionStatus = StorageTransactionsStatus.Cancelled;
            plan.Purchase.CanceledAt = DateTime.Now;
            plan.Purchase.CanceledBy = userName;
            plan.Shipping.PurchaseStorageTransaction = null;
            plan.Shipping.PurchaseStorageTransactionKey = null;
        }
    }

    /// <summary>
    /// Perna de compra de uma expedição que nasceu sem ela (liberação de transferência/devolução)
    /// e passa a baixar um contrato comum. Mesmo caminho da Expedição: criar Pending, confirmar
    /// (calcula descontos pela tabela de custo e o NetWeight) e só então alocar.
    /// </summary>
    private async Task<StorageTransaction> CreatePurchaseLegAsync(Plan plan, string userName)
    {
        await db.Context.Entry(plan.Sales).Collection(x => x.QualityInspections).LoadAsync();

        var purchase = StorageTransactionCopyFactory.CreateFrom(plan.Sales, userName);
        purchase.TransactionType = StorageTransactionType.Purchase;
        // Só a perna de SAÍDA carrega lote (ver ShippingTransactionsCreateService).
        purchase.StorageAddressCode = null;

        await storageCreateService.ExecuteAsync(
            purchase, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);
        await storageConfirmedService.ExecuteAsync(purchase, userName, CommitMode.Deferred, true);

        plan.Shipping.PurchaseStorageTransaction = purchase;

        return purchase;
    }

    private static void Touch(StorageTransaction st, string userName)
    {
        st.UpdatedAt = DateTime.Now;
        st.UpdatedBy = userName;
    }
}
```

> Checar ao compilar: o namespace de `TransactionCode` (usado igual em `ShippingTransactionsCreateService` — copiar o `using` de lá) e se `ShipmentLoadsMovementLogService` recebe `AppDbContext` ou `IUnitOfWork` no construtor (ajustar só a fixture de teste).

- [ ] **Step 5: Rodar os testes da classe**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShippingTransactionsChangeReleaseServiceTests"`
Expected: PASS. Se `Rejects_WhenTargetReleaseHasNoBalance` falhar pela mensagem de contrato em vez da de liberação, ajustar só o seed (`TotalVolume` alto já está) — não a ordem das fases.

- [ ] **Step 6: Stage**

```bash
git add SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsChangeReleaseService.cs SiagroB1.Application.Tests/ShippingTransactions/ShippingTransactionsChangeReleaseServiceTests.cs
```

---

### Task 3: Ramos que cruzam a origem (produtor ↔ Yokotobi) + regressão do estorno

**Files:**
- Modify: `SiagroB1.Application.Tests/ShippingTransactions/ShippingTransactionsChangeReleaseServiceTests.cs`
- Modify (só se algum teste falhar): `ShippingTransactionsChangeReleaseService.cs`

**Interfaces:**
- Consumes: fixture e serviço da Task 2.

- [ ] **Step 1: Acrescentar os testes**

```csharp
    [Fact]
    public async Task StandardToOwnershipTransfer_CancelsPurchaseLeg_AndDrainsTheLot()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002", origin: ReleaseOrigin.OwnershipTransfer, lotCode: "L-YKT");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        var purchaseKey = shipping.PurchaseStorageTransactionKey!.Value;

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "Produtor informou", "tester");

        var purchase = await Tx(purchaseKey);
        var sales = await Tx(shipping.SalesStorageTransactionKey);
        var link = await _db.Context.ShippingTransactions.AsNoTracking()
            .SingleAsync(x => x.SalesStorageTransactionKey == sales.Key);

        Assert.Equal(StorageTransactionsStatus.Cancelled, purchase.TransactionStatus);
        Assert.Null(link.PurchaseStorageTransactionKey);
        Assert.Equal("L-YKT", sales.StorageAddressCode);
        Assert.Equal(r2.Key, sales.ShipmentReleaseKey);
        Assert.Empty(await _db.Context.PurchaseContractsAllocations.Where(x => x.StorageTransactionKey == purchaseKey).ToListAsync());
        Assert.Equal(0m, (await ContractOf(c1.Key)).AllocatedVolume);
        // A transferência já alocou o contrato dela; a expedição não aloca de novo.
        Assert.Equal(0m, (await ContractOf(c2.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ReleaseOf(r1.Key)).ShippedQuantity);
        Assert.Equal(1000m, (await ReleaseOf(r2.Key)).ShippedQuantity);
    }

    [Fact]
    public async Task OwnershipTransferToStandard_CreatesConfirmedPurchaseLeg_AllocatesAndFreesTheLot()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001", origin: ReleaseOrigin.OwnershipTransfer, lotCode: "L-YKT");
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        Assert.Null(shipping.PurchaseStorageTransactionKey);

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "Armazém baixou do produtor", "tester");

        var link = await _db.Context.ShippingTransactions.AsNoTracking()
            .SingleAsync(x => x.SalesStorageTransactionKey == shipping.SalesStorageTransactionKey);
        Assert.NotNull(link.PurchaseStorageTransactionKey);

        var purchase = await Tx(link.PurchaseStorageTransactionKey!.Value);
        var sales = await Tx(shipping.SalesStorageTransactionKey);

        Assert.Equal(StorageTransactionType.Purchase, purchase.TransactionType);
        Assert.Equal(StorageTransactionsStatus.Confirmed, purchase.TransactionStatus);
        Assert.Null(purchase.StorageAddressCode);
        Assert.Null(purchase.ShipmentLoadKey);
        Assert.Null(purchase.SalesInvoiceKey);
        Assert.False(string.IsNullOrEmpty(purchase.Code));
        Assert.Equal("F0002", purchase.CardCode);
        Assert.Null(sales.StorageAddressCode);
        Assert.Equal(StorageTransactionsStatus.Invoiced, sales.TransactionStatus);

        var allocation = await _db.Context.PurchaseContractsAllocations.AsNoTracking()
            .SingleAsync(x => x.StorageTransactionKey == purchase.Key);
        Assert.Equal(c2.Key, allocation.PurchaseContractKey);
        Assert.Equal(purchase.NetWeight, (await ContractOf(c2.Key)).AllocatedVolume);
        Assert.Equal(0m, (await ReleaseOf(r1.Key)).ShippedQuantity);
        Assert.Equal(purchase.NetWeight, (await ReleaseOf(r2.Key)).ShippedQuantity);
    }

    [Fact]
    public async Task OwnershipTransferToOwnershipTransfer_SwapsLotAndRelease()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001", origin: ReleaseOrigin.OwnershipTransfer, lotCode: "L-A");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", origin: ReleaseOrigin.OwnershipTransfer, lotCode: "L-B");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester");

        var sales = await Tx(shipping.SalesStorageTransactionKey);
        Assert.Equal("L-B", sales.StorageAddressCode);
        Assert.Equal(0m, (await ReleaseOf(r1.Key)).ShippedQuantity);
        Assert.Equal(1000m, (await ReleaseOf(r2.Key)).ShippedQuantity);
    }

    [Fact]
    public async Task Rejects_WhenTargetLotHasNoBalance()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (_, r2) = await SeedReleaseAsync("PC-002", "F0002", origin: ReleaseOrigin.OwnershipTransfer, lotCode: "L-YKT");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);
        _lotBalance = 10m;

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(
            [new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester"));

        Assert.Contains("Saldo insuficiente no lote", ex.Message);
    }

    /// <summary>
    /// Depois da troca a expedição continua estornável pelo caminho de sempre (desvincular +
    /// estornar), devolvendo o saldo à liberação NOVA — prova de que as duas pernas e a alocação
    /// ficaram coerentes.
    /// </summary>
    [Fact]
    public async Task ChangedShipment_CanStillBeReversed_ReturningBalanceToTheNewRelease()
    {
        var (c1, r1) = await SeedReleaseAsync("PC-001", "F0001");
        var (c2, r2) = await SeedReleaseAsync("PC-002", "F0002");
        var (shipping, _) = await ShipIntoInvoicedLoadAsync(c1, r1, 1000m);

        await Service().ExecuteAsync([new(shipping.SalesStorageTransactionKey, r2.Key)], "motivo", "tester");

        // Simula o que a tela exige antes do estorno: nota desfeita e romaneio fora da carga.
        var sales = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == shipping.SalesStorageTransactionKey);
        sales.ShipmentLoadKey = null;
        sales.SalesInvoiceKey = null;
        sales.TransactionStatus = StorageTransactionsStatus.Confirmed;
        await _db.Context.SaveChangesAsync();

        var reverse = new ShippingTransactionsReverseService(
            _db, GetService(),
            new PurchaseContractsAllocationDeleteService(_db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance),
            Recalc(), NullLogger<ShippingTransactionsReverseService>.Instance);

        await reverse.ExecuteAsync(shipping.SalesStorageTransactionKey, "tester");

        Assert.Equal(0m, (await ReleaseOf(r2.Key)).ShippedQuantity);
        Assert.Equal(0m, (await ContractOf(c2.Key)).AllocatedVolume);
    }
```

- [ ] **Step 2: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShippingTransactionsChangeReleaseServiceTests"`
Expected: PASS. Se `OwnershipTransferToStandard` falhar na criação da perna (ex.: `StorageTransactionsCreateService` exigindo `DocNumberKey`/`BranchCode`, ou o `ShipmentReleaseMovementGuardService`), usar superpowers:systematic-debugging: ler a exceção, comparar com o payload que `ShippingTransactionsCreateService` passa no ramo normal e corrigir o serviço (não o teste).

- [ ] **Step 3: Suíte inteira**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS, nenhuma regressão.

---

### Task 4: Action OData `ShippingTransactionsChangeRelease`

Duas coleções paralelas de Guid (precedente: `CollectionParameter<Guid>` em `ShipmentLoadsDetachTransactions`) em vez de tipo complexo novo no EDM.

**Files:**
- Create: `SiagroB1.Web/Actions/ShippingTransactions/ShippingTransactionsChangeReleaseController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (junto das actions de ShipmentLoads, ~L612)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (junto de `ShippingTransactionsReverseService`, L355)

**Interfaces:**
- Consumes: `ShippingTransactionsChangeReleaseService.ExecuteAsync(IReadOnlyList<ShippingReleaseChangeItem>, string?, string)`
- Produces: `POST odata/ShippingTransactionsChangeRelease` com `{ SalesStorageTransactionKeys: Guid[], TargetShipmentReleaseKeys: Guid[], Reason: string }` (mesmo índice = mesmo item). 200 `{ Count }`; 400 com a mensagem de negócio.

- [ ] **Step 1: EDM**

```csharp
        var shippingTransactionsChangeRelease = modelBuilder.Action("ShippingTransactionsChangeRelease");
        shippingTransactionsChangeRelease.CollectionParameter<Guid>("SalesStorageTransactionKeys");
        shippingTransactionsChangeRelease.CollectionParameter<Guid>("TargetShipmentReleaseKeys");
        shippingTransactionsChangeRelease.Parameter<string>("Reason");
        shippingTransactionsChangeRelease.Returns<IActionResult>();
```

- [ ] **Step 2: DI** — após a L355:

```csharp
        services.AddScoped<ShippingTransactionsChangeReleaseService>();
```

- [ ] **Step 3: Controller**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShippingTransactions;

public class ShippingTransactionsChangeReleaseController(
    ShippingTransactionsChangeReleaseService changeReleaseService
    ) : ODataController
{
    [HttpPost("odata/ShippingTransactionsChangeRelease")]
    public async Task<ActionResult> ChangeRelease(ODataActionParameters parameters)
    {
        try
        {
            // Parâmetro ausente do corpo deixa `parameters` NULO; string presente pode vir null.
            if (parameters == null)
                return BadRequest("Missing required parameters");

            if (!parameters.TryGetValue("SalesStorageTransactionKeys", out var salesObj) ||
                salesObj is not IEnumerable<Guid> salesKeys ||
                !parameters.TryGetValue("TargetShipmentReleaseKeys", out var targetsObj) ||
                targetsObj is not IEnumerable<Guid> targetKeys)
            {
                return BadRequest("Selecione ao menos um romaneio para trocar a liberação.");
            }

            var sales = salesKeys.ToList();
            var targets = targetKeys.ToList();

            if (sales.Count != targets.Count)
                return BadRequest("Cada romaneio precisa de exatamente uma liberação de destino.");

            parameters.TryGetValue("Reason", out var reasonObj);
            var reason = reasonObj as string;

            var items = sales.Zip(targets, (s, t) => new ShippingReleaseChangeItem(s, t)).ToList();
            var userName = User.Identity?.Name ?? "Unknown";

            await changeReleaseService.ExecuteAsync(items, reason, userName);

            return Ok(new { Count = items.Count });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound();

            return BadRequest(e.Message);
        }
    }
}
```

- [ ] **Step 4: Build + suíte**

Run: `dotnet build SiagroB1.sln` → Expected: 0 erros.
Run: `dotnet test SiagroB1.Application.Tests` → Expected: PASS. (Se existir teste de EDM que enumera as actions — ver `ShipmentLoadEdmModelTests` — e ele quebrar, acrescentar a action nova à expectativa.)

- [ ] **Step 5: Stage** — `git add SiagroB1.Web/Actions/ShippingTransactions/ShippingTransactionsChangeReleaseController.cs`

---

### Task 5: Frontend — botão "Trocar Liberação" no detalhe da carga

Repo: `C:\Projetos\SiagroB1\siagro-b1-frontend`.

**Files:**
- Modify: `webapp/view/shipmentLoads/Detail.view.xml` (toolbar do `loadTransactionsTable`, L182-186; binding `rows`, L180)
- Create: `webapp/view/shipmentLoads/fragments/ChangeRelease.fragment.xml`
- Modify: `webapp/controller/shipmentLoads/Detail.controller.ts`
- Modify: `webapp/model/formatter.ts` (`formatShipmentLoadMovementType`, ~L538)

**Interfaces:**
- Consumes: action `ShippingTransactionsChangeRelease` (Task 4); function existente `ShipmentReleasesGetPurchaseContracts(ItemCode, WarehouseCode)` → itens com `ShipmentReleaseKey`, `PurchaseContractCode`, `FName`, `Origin` (int), `StandardCashFlowDate`, `AvailableQuantity`, `UnitOfMeasureCode`.

- [ ] **Step 1: Formatter** — após `ReturnedToWarehouse`:

```ts
    m.set("ReleaseChanged", "Troca de Liberação");
```

- [ ] **Step 2: View** — no `t:Table id="loadTransactionsTable"`, trocar o `rows` para garantir as chaves não exibidas (coluna invisível não cria binding):

```xml
								rows="{ path: 'Transactions', parameters: { '$$ownRequest': true, '$select': 'Key,ShipmentReleaseKey,ItemCode,WarehouseCode' } }">
```

e na toolbar, depois do "Desvincular":

```xml
										<Button
											type="Transparent"
											text="Trocar Liberação"
											icon="sap-icon://switch-views"
											tooltip="Troca o contrato de compra baixado (1 romaneio) ou inverte os contratos de 2 romaneios"
											visible="{= ${path: 'Status', targetType: 'any'} !== 'Cancelled' &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Returned' }"
											press=".onChangeRelease"/>
```

- [ ] **Step 3: Fragmento** `webapp/view/shipmentLoads/fragments/ChangeRelease.fragment.xml` (model `changeRelease`; sem `--` em comentários):

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form">
	<Dialog
		id="changeReleaseDialog"
		title="{= ${changeRelease>/IsSwap} ? 'Inverter Liberações' : 'Trocar Liberação' }"
		contentWidth="60rem"
		class="sapUiSizeCompact"
		busy="{changeRelease>/busy}"
		busyIndicatorDelay="0">
		<content>
			<!-- Situação atual de cada romaneio selecionado e, na inversão, o destino de cada um. -->
			<Table items="{changeRelease>/Rows}">
				<headerToolbar>
					<OverflowToolbar><Title text="Romaneios"/></OverflowToolbar>
				</headerToolbar>
				<columns>
					<Column width="8rem"><Text text="Romaneio"/></Column>
					<Column hAlign="End" width="9rem"><Text text="Peso Bruto"/></Column>
					<Column><Text text="Contrato Atual"/></Column>
					<Column visible="{changeRelease>/IsSwap}"><Text text="Novo Contrato"/></Column>
				</columns>
				<items>
					<ColumnListItem>
						<Text text="{changeRelease>Code}"/>
						<ObjectNumber
							number="{ path: 'changeRelease>GrossWeight', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
						<Text text="{changeRelease>ContractCode} {changeRelease>CardName}"/>
						<Text text="{changeRelease>NewContractCode} {changeRelease>NewCardName}"/>
					</ColumnListItem>
				</items>
			</Table>

			<!-- Troca simples: escolher a liberação de destino. Na inversão o destino é o outro romaneio. -->
			<Table
				id="changeReleaseTargets"
				visible="{= !${changeRelease>/IsSwap} }"
				mode="SingleSelectLeft"
				growing="true"
				noDataText="Nenhuma outra liberação ativa com saldo para este produto e armazém."
				items="{changeRelease>/Targets}">
				<headerToolbar>
					<OverflowToolbar><Title text="Liberação de Destino"/></OverflowToolbar>
				</headerToolbar>
				<columns>
					<Column width="8rem"><Text text="Contrato"/></Column>
					<Column width="8rem"><Text text="Prev. Pagto."/></Column>
					<Column width="9rem"><Text text="Origem"/></Column>
					<Column><Text text="Fornecedor"/></Column>
					<Column hAlign="End" width="9rem"><Text text="Saldo"/></Column>
				</columns>
				<items>
					<ColumnListItem>
						<Text text="{changeRelease>PurchaseContractCode}"/>
						<Text text="{ path: 'changeRelease>StandardCashFlowDate', formatter: '.formatter.formatDate' }"/>
						<Text text="{ path: 'changeRelease>Origin', formatter: '.formatter.releaseOriginText' }"/>
						<Text text="{changeRelease>FName}"/>
						<ObjectNumber
							number="{ path: 'changeRelease>AvailableQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"
							unit="{changeRelease>UnitOfMeasureCode}"/>
					</ColumnListItem>
				</items>
			</Table>

			<f:Form editable="true">
				<f:layout><f:ColumnLayout columnsM="1" columnsL="1" columnsXL="1"/></f:layout>
				<f:formContainers>
					<f:FormContainer>
						<f:formElements>
							<f:FormElement label="Motivo">
								<f:fields>
									<TextArea
										value="{changeRelease>/Reason}"
										required="true"
										rows="3"
										maxLength="500"
										width="100%"
										placeholder="Ex.: relatório do armazém, ordem do financeiro"/>
								</f:fields>
							</f:FormElement>
						</f:formElements>
					</f:FormContainer>
				</f:formContainers>
			</f:Form>
		</content>
		<beginButton>
			<Button type="Emphasized" text="Confirmar" press=".onConfirmChangeRelease"/>
		</beginButton>
		<endButton>
			<Button text="Cancelar" press=".onCloseChangeRelease"/>
		</endButton>
	</Dialog>
</core:FragmentDefinition>
```

- [ ] **Step 4: Controller** — tipos no topo (junto de `RefusalForm`):

```ts
/** Romaneio selecionado no grid, lido do contexto (getObject + acesso opcional). */
type ChangeReleaseRow = {
  Key: string;
  Code: string;
  GrossWeight: number;
  ItemCode: string;
  WarehouseCode: string;
  ShipmentReleaseKey: string;
  ContractCode?: string;
  CardName?: string;
  NewContractCode?: string;
  NewCardName?: string;
};

type ReleaseTarget = { ShipmentReleaseKey: string };

type ChangeReleaseForm = {
  IsSwap: boolean;
  Rows: ChangeReleaseRow[];
  Targets: ReleaseTarget[];
  Reason: string;
  busy: boolean;
};
```

Campos privados junto de `_refusalInFlight`:

```ts
  private _changeReleaseDialog: Dialog;

  private _changeReleaseInFlight = false;
```

Métodos (depois de `onDetachShipments`):

```ts
  /**
   * Troca a liberação (contrato de compra) de 1 romaneio, ou inverte a de 2 (GAC-1177).
   * Não mexe no peso da carga nem nas notas; o backend valida saldo, contrato encerrado e
   * liberação ativa, e registra o motivo na Movimentação.
   */
  async onChangeRelease(): Promise<void> {
    const table = this.byId("loadTransactionsTable") as Table;
    const selected = table.getSelectedIndices();

    if (selected.length < 1 || selected.length > 2) {
      MessageBox.warning(
        "Selecione 1 romaneio para trocar a liberação ou 2 romaneios para inverter as liberações entre eles.");
      return;
    }

    // getObject + acesso opcional: getProperty("Nav/Campo") estoura com navegação nula.
    const rows: ChangeReleaseRow[] = selected.map(i => {
      const o = (table.getContextByIndex(i) as Context).getObject() as {
        Key: string; Code: string; GrossWeight: number; ItemCode: string; WarehouseCode: string;
        ShipmentReleaseKey: string; CardName?: string;
        ShipmentRelease?: { PurchaseContract?: { Code?: string } };
      };
      return {
        Key: o.Key,
        Code: o.Code,
        GrossWeight: o.GrossWeight,
        ItemCode: o.ItemCode,
        WarehouseCode: o.WarehouseCode,
        ShipmentReleaseKey: o.ShipmentReleaseKey,
        ContractCode: o.ShipmentRelease?.PurchaseContract?.Code,
        CardName: o.CardName,
      };
    });

    if (rows.some(r => !r.ShipmentReleaseKey)) {
      MessageBox.warning("Há romaneio selecionado sem liberação de embarque.");
      return;
    }

    const isSwap = rows.length === 2;

    if (isSwap) {
      if (rows[0].ShipmentReleaseKey === rows[1].ShipmentReleaseKey) {
        MessageBox.warning("Os dois romaneios já estão na mesma liberação: não há o que inverter.");
        return;
      }
      rows[0].NewContractCode = rows[1].ContractCode;
      rows[0].NewCardName = rows[1].CardName;
      rows[1].NewContractCode = rows[0].ContractCode;
      rows[1].NewCardName = rows[0].CardName;
    }

    this.setBusy(true);
    try {
      let targets: ReleaseTarget[] = [];

      if (!isSwap) {
        const func = (this.getModel() as ODataModel)
          .bindContext("/ShipmentReleasesGetPurchaseContracts(...)");
        func.setParameter("ItemCode", rows[0].ItemCode);
        func.setParameter("WarehouseCode", rows[0].WarehouseCode);
        await func.invoke();

        // Coleção pode chegar como array ou como envelope { value: [...] }.
        const result = func.getBoundContext().getObject() as ReleaseTarget[] | { value?: ReleaseTarget[] };
        const all = Array.isArray(result) ? result : result?.value ?? [];
        targets = all.filter(t => t.ShipmentReleaseKey?.toLowerCase() !== rows[0].ShipmentReleaseKey.toLowerCase());
      }

      this.getView().setModel(new JSONModel({
        IsSwap: isSwap, Rows: rows, Targets: targets, Reason: "", busy: false,
      } as ChangeReleaseForm), "changeRelease");

      if (!this._changeReleaseDialog) {
        this._changeReleaseDialog = await Fragment.load({
          id: this.getView().getId(),
          name: "siagrob1.view.shipmentLoads.fragments.ChangeRelease",
          controller: this,
        }) as Dialog;
        this.getView().addDependent(this._changeReleaseDialog);
      }

      (this.byId("changeReleaseTargets") as SapMTable).removeSelections(true);
      this._changeReleaseDialog.open();
    } catch (e) {
      MessageBox.error((e as Error).message);
    } finally {
      this.setBusy(false);
    }
  }

  async onConfirmChangeRelease(): Promise<void> {
    // Trava ANTES do primeiro await: duplo clique dispararia duas trocas.
    if (this._changeReleaseInFlight) return;

    const form = this.getView().getModel("changeRelease") as JSONModel;
    const data = form.getData() as ChangeReleaseForm;

    if (!data.Reason?.trim()) {
      MessageBox.warning("Informe o motivo da troca.");
      return;
    }

    let salesKeys: string[];
    let targetKeys: string[];

    if (data.IsSwap) {
      salesKeys = [data.Rows[0].Key, data.Rows[1].Key];
      targetKeys = [data.Rows[1].ShipmentReleaseKey, data.Rows[0].ShipmentReleaseKey];
    } else {
      const item = (this.byId("changeReleaseTargets") as SapMTable).getSelectedItem();
      if (!item) {
        MessageBox.warning("Selecione a liberação de destino.");
        return;
      }
      const target = item.getBindingContext("changeRelease").getObject() as ReleaseTarget;
      salesKeys = [data.Rows[0].Key];
      targetKeys = [target.ShipmentReleaseKey];
    }

    this._changeReleaseInFlight = true;
    form.setProperty("/busy", true);

    const action = (this.getModel() as ODataModel).bindContext("/ShippingTransactionsChangeRelease(...)");
    action.setParameter("SalesStorageTransactionKeys", salesKeys);
    action.setParameter("TargetShipmentReleaseKeys", targetKeys);
    action.setParameter("Reason", data.Reason.trim());

    try {
      await action.invoke();
      this._changeReleaseDialog.close();
      (this.byId("loadTransactionsTable") as Table).clearSelection();
      this.refreshAll();
      MessageToast.show(data.IsSwap ? "Liberações invertidas." : "Liberação trocada.");
    } catch (e) {
      MessageBox.error((e as Error).message);
    } finally {
      form.setProperty("/busy", false);
      this._changeReleaseInFlight = false;
    }
  }

  onCloseChangeRelease(): void {
    this._changeReleaseDialog.close();
  }
```

Import no topo: `import SapMTable from "sap/m/Table";`

> Conferir: `refreshAll()` deve atualizar também `loadMovementsTable` (a Movimentação); se não atualizar, incluir. Conferir o nome exato do formatter de origem (`releaseOriginText`, usado em `SelectShipmentRelease.view.xml`) e de data (`formatDate`).

- [ ] **Step 5: Gates**

Run (em `siagro-b1-frontend`): `yarn ts-typecheck` → Expected: sem erros.
Run: `yarn lint` → Expected: sem erros novos.

- [ ] **Step 6: Stage** — `git add webapp/view/shipmentLoads/fragments/ChangeRelease.fragment.xml`

---

### Task 6: Verificação ponta a ponta no navegador

Seguir `superpowers:verification-before-completion`. Pronto = alcançável pelo caminho do usuário, não endpoint por curl.

- [ ] **Step 1:** Subir o stack (profile `yktb` Web + Gateway; `yarn start:dev` no frontend). Login admin/1234.
- [ ] **Step 2:** Menu → Montagem de Carga → abrir uma carga Faturada (ou Parcialmente Faturada) com romaneio de liberação comum. Selecionar 1 romaneio → Trocar Liberação → escolher outra liberação, informar motivo → Confirmar. Conferir: colunas Fornecedor/Contrato de Compra do grid mudaram; aba Movimentação tem "Troca de Liberação" com o motivo; Documentos de Saída intactos; saldo da carga igual; na tela de liberações, saldo das duas liberações ajustado; alocações dos dois contratos (tela de alocação do contrato de compra).
- [ ] **Step 3:** Selecionar 2 romaneios de contratos diferentes → Inverter → conferir o de/para no grid e as duas movimentações.
- [ ] **Step 4:** Se houver liberação de transferência de titularidade com lote no mesmo produto/armazém: trocar um romaneio de produtor para ela e voltar; conferir saldo do lote.
- [ ] **Step 5:** Tentar com contrato Finalizado e com motivo vazio — mensagens em pt-BR, nada muda.
- [ ] **Step 6:** Derrubar o stack. Relatar ao usuário com evidências; **não commitar**.
