# Expedir e Faturar dentro da Carga — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** No detail da carga, "Expedir" cria o romaneio já vinculado à carga (expedição + vínculo numa transação só)
e "Faturar" abre o mesmo diálogo do `/shipment-billing`, cuja lógica vira um helper compartilhado.

**Architecture:** `ShippingTransactionsCreateService` e `ShipmentLoadsAttachTransactionsService` ganham o modo
`CommitMode.Deferred` (não abrem nem confirmam transação). Um serviço novo, `ShipmentLoadsShipService`, monta o
romaneio a partir da carga e da liberação, abre a transação, chama os dois em `Deferred`, confirma e recalcula a
liberação depois do commit. Exposto pela action OData `ShipmentLoadsShip`. No frontend, o diálogo de faturamento sai
de `shipmentBilling/Main.controller.ts` para `helpers/ShipmentBillingDialog.ts` (os modelos do fragmento são
renomeados para não colidir com o `viewModel` do detalhe da carga) e o detalhe ganha os dois botões.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes com InMemory), OData v4, xUnit; OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-06-shipment-load-ship-and-bill-design.md` (backend repo, commit be19c71).

## Global Constraints

- Branch `feature/shipment-load-ship-and-bill` nos dois repos. Conferir o branch antes de cada commit. **Nunca push.**
  Commit com pathspec explícito: o índice do backend tem
  `docs/superpowers/{plans,specs}/2026-10-01-nfe-standalone-taxation*` staged que **não** podem entrar em commit; no
  frontend, `.vscode/.advpl/*` está modificado e **não** entra em commit.
- Mensagem de commit: `tipo(shipment): descrição pt-BR` (escopo `shipment`), rodapé
  `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>` e
  `Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw`. Sem migration nesta peça.
- Identificadores em inglês; tudo o que o usuário lê em pt-BR.
- As telas antigas (Expedição de Grãos, Vincular Romaneios, `/shipment-billing`) **não mudam de comportamento**.
  Os dois serviços existentes, chamados como hoje (`CommitMode.Auto`), fazem exatamente o que fazem hoje.
- Situações que aceitam expedição: carga `LoadType == Normal` em `Planned`, `Open` ou `InTransshipment`.
  Recusa: `"A carga {Code} não aceita expedição nesta situação."`.
- Placa, produto, filial e unidade do romaneio vêm da CARGA; o parceiro vem do contrato da liberação.
- Frontend: enum em binding OData V4 sempre com `targetType: 'any'`; `&&` em atributo XML é `&amp;&amp;`;
  comentário XML sem `--`. Navegação no navegador automatizado pelo MENU.
- Gates finais: `dotnet build SiagroB1.sln` 0 erros; `SiagroB1.Application.Tests` verde; frontend `yarn ts-typecheck`
  e `yarn lint`. ⚠️ `yarn test` completo nunca passa (gate de cobertura): não usar.

## Desvios do spec (decididos aqui, menores)

- **Sem o parâmetro `PurchaseContractKey` na action.** O servidor o tira da própria liberação
  (`ShipmentRelease.PurchaseContractKey`), e passa nulo quando a origem não tem perna de compra
  (`ReleaseOriginRules.ShipsWithoutPurchaseLeg`). A function de liberações que a tela usa
  (`ShipmentReleasesGetPurchaseContracts`) nem devolve a chave do contrato, e um parâmetro a menos é um erro a menos.
- **Modelos do diálogo de faturamento renomeados**: `viewModel` → `billing`, `releases` → `billingReleases`,
  `branches` → `billingBranches`. O detalhe da carga já usa `viewModel` (comentários, anexos, transbordo), e o
  `setData` do faturamento apagaria esse estado.
- **Recusa extra:** carga sem placa → `"A carga {Code} não tem placa: informe a placa na carga antes de expedir."`
  (`StorageTransaction.TruckCode` é NOT NULL no banco; sem a recusa o erro seria um 500 do SQL).
- **Recusa extra:** liberação de outro produto → `"A liberação escolhida é de outro produto."`.
- O retorno da action é `{ shipmentLoadKey, storageTransactionKey, storageTransactionCode }` (camelCase explícito: é
  como `Ok(new { ... })` serializa neste projeto).

## Review Focus

1. **Falha no vínculo depois da expedição** (ex.: outra pessoa cancelou a carga entre a leitura e o vínculo).
   Esperado: nada fica gravado — nem romaneio, nem alocação no contrato de compra. → o InMemory não desfaz o que já
   foi salvo, então o teste da Task 2 prova a ordem (os dois serviços chamados em `Deferred` dentro de UMA transação
   aberta pelo serviço novo, e `RollbackAsync` chamado na falha) com o `CountingUnitOfWork`; a Task 5 confere no SQL
   Server real com uma carga cancelada.
2. **Liberação recalculada só depois do commit.** Esperado: depois de expedir, o saldo da liberação de compra cai
   pelo peso líquido. → teste na Task 2.
3. **`/shipment-billing` igual a antes da extração** (seleção, saldo, trava de duplo clique, refresh da lista). →
   verificação na Task 5 e revisão da Task 3.
4. **Duplo clique em "Expedir"**. Esperado: uma expedição só. → trava setada antes do primeiro `await` (Task 4).
5. **Carga sem placa / carga de Remoção / carga Faturada**. Esperado: recusa com mensagem e nada gravado. → testes
   na Task 2.

---

## File Structure

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsCreateService.cs` | modo `Deferred` |
| `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsAttachTransactionsService.cs` | modo `Deferred` |
| `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsShipService.cs` (novo) | expedir na carga |
| `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsShipController.cs` (novo) | action `ShipmentLoadsShip` |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` | registro da action |
| `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` | DI do serviço novo |
| `SiagroB1.Application.Tests/Support/CountingUnitOfWork.cs` (novo) | conta Begin/Commit/Rollback |
| frontend `webapp/helpers/ShipmentBillingDialog.ts` (novo) | diálogo de faturamento compartilhado |
| frontend `webapp/view/shipmentBilling/fragments/Billing.fragment.xml` | modelos renomeados |
| frontend `webapp/controller/shipmentBilling/Main.controller.ts` | usa o helper |
| frontend `webapp/view/shipmentLoads/fragments/ShipDialog.fragment.xml` (novo) | diálogo "Expedir" |
| frontend `webapp/view/shipmentLoads/Detail.view.xml`, `controller/shipmentLoads/Detail.controller.ts` | botões e handlers |
| frontend `webapp/model/ServerRoutes.ts` (se o padrão de actions da carga usar) | — |

---

### Task 1: Modo `Deferred` na expedição e no vínculo

**Files:**
- Create: `SiagroB1.Application.Tests/Support/CountingUnitOfWork.cs`
- Modify: `SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsCreateService.cs:44-137`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsAttachTransactionsService.cs:29-107`
- Test: `SiagroB1.Application.Tests/ShippingTransactions/ShippingTransactionsCreateServiceTests.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsAttachTransactionsServiceTests.cs`

**Interfaces:**
- Produces:
  - `ShippingTransactionsCreateService.ExecuteAsync(Guid? purchaseContractKey, StorageTransaction purchase, string userName, CommitMode commitMode = CommitMode.Auto)` → `Task<ShippingTransaction>`; em `Deferred` não abre/confirma/desfaz transação e **não** recalcula a liberação (quem chama recalcula depois do commit).
  - `ShipmentLoadsAttachTransactionsService.ExecuteAsync(Guid shipmentLoadKey, ICollection<Guid> storageTransactionKeys, Guid? transshipmentKey, string userName, CommitMode commitMode = CommitMode.Auto)` → `Task<ShipmentLoad>`.
  - `SiagroB1.Application.Tests.Support.CountingUnitOfWork(UnitOfWork inner)` com `Begins`, `Commits`, `Rollbacks`.

- [ ] **Step 1: Test double**

`SiagroB1.Application.Tests/Support/CountingUnitOfWork.cs`:

```csharp
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Conta as chamadas de transação de um serviço. O InMemory não desfaz o que já foi salvo, então a
/// atomicidade de um fluxo composto é provada pela ORDEM: quem é dono da transação abre e fecha;
/// quem roda em <c>CommitMode.Deferred</c> não toca nela.
/// </summary>
public sealed class CountingUnitOfWork(UnitOfWork inner) : IUnitOfWork
{
    public int Begins { get; private set; }
    public int Commits { get; private set; }
    public int Rollbacks { get; private set; }

    public AppDbContext Context => inner.Context;

    public Task BeginTransactionAsync()
    {
        Begins++;
        return inner.BeginTransactionAsync();
    }

    public Task CommitAsync()
    {
        Commits++;
        return inner.CommitAsync();
    }

    public Task RollbackAsync()
    {
        Rollbacks++;
        return inner.RollbackAsync();
    }

    public Task SaveChangesAsync() => inner.SaveChangesAsync();
}
```

- [ ] **Step 2: Write the failing tests**

Em `ShippingTransactionsCreateServiceTests.cs`, mude a assinatura do helper para aceitar o unit of work de topo:

```csharp
    private ShippingTransactionsCreateService CreateService(decimal lotBalance = 100_000m, IUnitOfWork? top = null)
```

e, no `return new ShippingTransactionsCreateService(...)` do helper, troque o primeiro argumento `_db` por `top ?? _db`
(os subserviços continuam com `_db`). Depois acrescente no fim da classe (o arquivo já importa `SiagroB1.Infra`;
acrescente `using SiagroB1.Infra.Enums;` se faltar):

```csharp
    [Fact]
    public async Task Auto_mode_owns_the_transaction_as_today()
    {
        var (contract, release) = await SeedAsync();
        var counting = new CountingUnitOfWork(_db);

        await CreateService(top: counting).ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester");

        Assert.Equal(1, counting.Begins);
        Assert.Equal(1, counting.Commits);
    }

    /// <summary>
    /// Deferred: quem chama é o dono da transação. O par é gravado (SaveChanges), mas a liberação
    /// NÃO é recalculada — o recálculo antes do commit contaria a cópia de venda (ver o comentário do
    /// serviço), então ele fica com quem chama, depois do commit dele.
    /// </summary>
    [Fact]
    public async Task Deferred_mode_does_not_touch_the_transaction_nor_recalculate_the_release()
    {
        var (contract, release) = await SeedAsync();
        var counting = new CountingUnitOfWork(_db);

        var shipping = await CreateService(top: counting)
            .ExecuteAsync(contract.Key, NewPurchase(release.Key, 1000m), "tester", CommitMode.Deferred);

        Assert.Equal(0, counting.Begins);
        Assert.Equal(0, counting.Commits);
        Assert.Equal(0, counting.Rollbacks);
        Assert.NotNull(await _db.Context.StorageTransactions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Key == shipping.SalesStorageTransaction!.Key));
        Assert.Equal(0m, (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == release.Key)).ShippedQuantity);
    }
```

Em `ShipmentLoadsAttachTransactionsServiceTests.cs`, acrescente (usando os helpers `Load(...)`/`Shipment(...)` do
arquivo; o `Shipment` grava o romaneio no contexto — confira o helper e chame `SaveChangesAsync` se ele não chamar):

```csharp
    [Fact]
    public async Task Deferred_mode_attaches_without_touching_the_transaction()
    {
        var load = Load();
        var shipment = Shipment("ROM-DEF");
        await _db.SaveChangesAsync();
        var counting = new CountingUnitOfWork((UnitOfWork)_db);

        await new ShipmentLoadsAttachTransactionsService(counting, new ShipmentLoadsMovementLogService(_db.Context))
            .ExecuteAsync(load.Key, [shipment.Key], null, "tester", CommitMode.Deferred);

        Assert.Equal(0, counting.Begins);
        Assert.Equal(0, counting.Commits);
        Assert.Equal(load.Key, (await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == shipment.Key)).ShipmentLoadKey);
    }
```

(Se `_db` já for `UnitOfWork` no arquivo, dispense o cast; acrescente `using SiagroB1.Infra.Enums;`.)

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShippingTransactionsCreateServiceTests|FullyQualifiedName~ShipmentLoadsAttachTransactionsServiceTests"`
Expected: FAIL na compilação — `No overload for method 'ExecuteAsync' takes 4/5 arguments`.

- [ ] **Step 4: Implement `ShippingTransactionsCreateService`**

Troque a assinatura por:

```csharp
    /// <param name="commitMode">
    /// <c>Auto</c> (telas atuais): abre, confirma e desfaz a própria transação e recalcula a liberação depois do commit.
    /// <c>Deferred</c>: quem chama é o dono da transação — nada de Begin/Commit/Rollback aqui, e o recálculo da
    /// liberação (<see cref="ShipmentReleasesRecalculateShippedService"/>) fica com quem chama, DEPOIS do commit dele.
    /// </param>
    public async Task<ShippingTransaction> ExecuteAsync(
        Guid? purchaseContractKey, StorageTransaction purchase, string userName, CommitMode commitMode = CommitMode.Auto)
```

Logo antes do `try`, acrescente `var ownsTransaction = commitMode == CommitMode.Auto;`. Dentro do `try`, troque
`await unitOfWork.BeginTransactionAsync();` por `if (ownsTransaction) await unitOfWork.BeginTransactionAsync();`.
Depois do `await unitOfWork.SaveChangesAsync();` final, insira:

```csharp
            if (!ownsTransaction)
                return shipping;
```

(o `CommitAsync` e o recálculo seguintes ficam como estão). No `catch`, troque `await unitOfWork.RollbackAsync();`
por `if (ownsTransaction) await unitOfWork.RollbackAsync();`.

- [ ] **Step 5: Implement `ShipmentLoadsAttachTransactionsService`**

Acrescente o parâmetro `CommitMode commitMode = CommitMode.Auto` ao fim da assinatura de `ExecuteAsync` (com o mesmo
`<param>` explicando que em `Deferred` quem chama é o dono da transação), `var ownsTransaction = commitMode ==
CommitMode.Auto;` antes do `try`, e guarde as três chamadas: `if (ownsTransaction) await db.BeginTransactionAsync();`,
`if (ownsTransaction) await db.CommitAsync();`, `if (ownsTransaction) await db.RollbackAsync();`. Os dois
`SaveChangesAsync` ficam (o recálculo do total lê as FKs gravadas). Acrescente `using SiagroB1.Infra.Enums;`.

- [ ] **Step 6: Run to verify they pass**

Run: o mesmo filtro do Step 3. Expected: PASS. Depois `dotnet test SiagroB1.Application.Tests`: tudo verde.

- [ ] **Step 7: Commit**

```bash
git branch --show-current   # feature/shipment-load-ship-and-bill
git add SiagroB1.Application.Tests/Support/CountingUnitOfWork.cs
git commit -m "refactor(shipment): expedição e vínculo à carga aceitam transação de quem chama" -m "Modo Deferred nos dois serviços, para a expedição na carga gravar o romaneio e o vínculo numa transação só. Chamados como hoje, fazem o mesmo de antes." -m "Atenção: em Deferred a expedição NÃO recalcula a liberação; quem chama recalcula depois do commit, senão a cópia de venda seria contada." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw" -- SiagroB1.Application.Tests/Support/CountingUnitOfWork.cs SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsCreateService.cs SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsAttachTransactionsService.cs SiagroB1.Application.Tests/ShippingTransactions/ShippingTransactionsCreateServiceTests.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsAttachTransactionsServiceTests.cs
```

---

### Task 2: `ShipmentLoadsShipService` + action `ShipmentLoadsShip`

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsShipService.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsShipController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (junto da `ShipmentLoadsAttachTransactions`, ~linha 822)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (junto de `ShipmentLoadsAttachTransactionsService`, ~linha 471)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsShipServiceTests.cs` (novo)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadEdmModelTests.cs` (acrescentar caso)

**Interfaces:**
- Consumes: Task 1 (`CommitMode.Deferred` nos dois serviços; `CountingUnitOfWork`).
- Produces:
  - `record ShipmentLoadShipRequest(Guid ShipmentLoadKey, Guid ShipmentReleaseKey, string WarehouseCode, string TruckDriverCode, DateTime TransactionDate, decimal GrossWeight, string? Comments)`
  - `record ShipmentLoadShipResult(Guid ShipmentLoadKey, Guid StorageTransactionKey, string? StorageTransactionCode)`
  - `ShipmentLoadsShipService.ExecuteAsync(ShipmentLoadShipRequest request, string userName)` → `Task<ShipmentLoadShipResult>`
  - Action `POST odata/ShipmentLoadsShip` com `Key` (Guid), `ShipmentReleaseKey` (Guid), `WarehouseCode` (string),
    `TruckDriverCode` (string), `TransactionDate` (DateTimeOffset), `GrossWeight` (double), `Comments` (string,
    opcional); resposta `{ shipmentLoadKey, storageTransactionKey, storageTransactionCode }`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsShipServiceTests.cs` — monte os subserviços como
`ShippingTransactionsCreateServiceTests.CreateService` monta (copie a construção: `FakeDocNumberSequenceService`,
`FakeBusinessPartnerService(new() { ["F0001"] = "Fornecedor" })`, `FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" })`,
`FakeWarehouseService(new() { ["01"] = "Armazém 01" })`, `ShipmentReleaseMovementGuardService`,
`StorageTransactionsCreateService`, `StorageTransactionsConfirmedService`, `StorageTransactionsCopyService`,
`PurchaseContractsAllocationCreateService`, `FakeStorageAddressBalanceReader(100_000m)`):

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

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>"Expedir" no detalhe da carga: o romaneio nasce vinculado, numa transação só.</summary>
public class ShipmentLoadsShipServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private (ShipmentLoadsShipService Service, CountingUnitOfWork Counting) Build(
        ShipmentLoadsAttachTransactionsService? attachOverride = null)
    {
        var counting = new CountingUnitOfWork(_db);
        var recalc = new ShipmentReleasesRecalculateShippedService(_db.Context);
        var guard = new ShipmentReleaseMovementGuardService(_db.Context);
        var docNumbers = new FakeDocNumberSequenceService();
        var storageCreate = new StorageTransactionsCreateService(
            _db, docNumbers, new FakeBusinessPartnerService(new() { ["F0001"] = "Fornecedor" }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }), new FakeWarehouseService(new() { ["01"] = "Armazém 01" }),
            recalc, guard, NullLogger<StorageTransactionsCreateService>.Instance);
        var storageConfirmed = new StorageTransactionsConfirmedService(
            _db, new FakeStringLocalizer<Resource>(), recalc, guard, NullLogger<StorageTransactionsConfirmedService>.Instance);
        var storageCopy = new StorageTransactionsCopyService(_db, docNumbers, storageCreate, new FakeStringLocalizer<Resource>());
        var allocation = new PurchaseContractsAllocationCreateService(
            _db, new StorageTransactionsGetService(_db, NullLogger<StorageTransactionsGetService>.Instance));
        var shipping = new ShippingTransactionsCreateService(
            _db, storageCreate, storageConfirmed, storageCopy, allocation, recalc, new FakeStorageAddressBalanceReader(100_000m));
        var attach = attachOverride ?? new ShipmentLoadsAttachTransactionsService(_db, new ShipmentLoadsMovementLogService(_db.Context));

        return (new ShipmentLoadsShipService(counting, shipping, attach, recalc), counting);
    }

    private async Task<(ShipmentLoad Load, PurchaseContract Contract, ShipmentRelease Release)> SeedAsync(
        ShipmentLoadStatus status = ShipmentLoadStatus.Planned, ShipmentLoadType type = ShipmentLoadType.Normal,
        string? truckCode = "ABC1D23", string itemCode = "SOJA")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", CardName = "Fornecedor", ItemCode = itemCode,
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2026", DeliveryLocationCode = "01",
            Status = ContractStatus.Approved, TotalVolume = 10_000m, AllocatedVolume = 0m,
        };
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contract.Key, DeliveryLocationCode = "01",
            ReleasedQuantity = 5_000m, ShippedQuantity = 0m, Status = ReleaseStatus.Actived,
        };
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000001", BranchCode = "01", ItemCode = "SOJA", ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG", TruckCode = truckCode, WarehouseCode = "01", Status = status, LoadType = type,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(release);
        _db.Context.ShipmentLoads.Add(load);
        await _db.SaveChangesAsync();
        return (load, contract, release);
    }

    private static ShipmentLoadShipRequest Request(ShipmentLoad load, ShipmentRelease release, decimal weight = 1_000m) =>
        new(load.Key, release.Key, "01", "MOT01", new DateTime(2026, 10, 6), weight, "teste");

    [Fact]
    public async Task Ships_the_load_in_one_transaction()
    {
        var (load, contract, release) = await SeedAsync();
        var (service, counting) = Build();

        var result = await service.ExecuteAsync(Request(load, release), "tester");

        var exit = await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == result.StorageTransactionKey);
        Assert.Equal(StorageTransactionType.SalesShipment, exit.TransactionType);
        Assert.Equal(load.Key, exit.ShipmentLoadKey);
        Assert.Equal("ABC1D23", exit.TruckCode);
        Assert.Equal("01", exit.BranchCode);
        Assert.Equal(1_000m, (await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(x => x.Key == load.Key)).TotalQuantity);
        Assert.Equal(1_000m, (await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == contract.Key)).AllocatedVolume);
        Assert.Equal(1, counting.Begins);
        Assert.Equal(1, counting.Commits);
        Assert.Equal(0, counting.Rollbacks);
    }

    [Fact]
    public async Task Release_balance_is_recalculated_after_the_commit()
    {
        var (load, _, release) = await SeedAsync();

        await Build().Service.ExecuteAsync(Request(load, release), "tester");

        Assert.Equal(1_000m, (await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == release.Key)).ShippedQuantity);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Cancelled, ShipmentLoadType.Normal)]
    [InlineData(ShipmentLoadStatus.Invoiced, ShipmentLoadType.Normal)]
    [InlineData(ShipmentLoadStatus.Planned, ShipmentLoadType.Removal)]
    public async Task Load_that_does_not_accept_shipments_is_refused_before_writing(ShipmentLoadStatus status, ShipmentLoadType type)
    {
        var (load, _, release) = await SeedAsync(status, type);
        var (service, counting) = Build();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(Request(load, release), "tester"));

        Assert.Equal("A carga CG000001 não aceita expedição nesta situação.", ex.Message);
        Assert.Equal(0, counting.Begins);
        Assert.Empty(await _db.Context.StorageTransactions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Load_without_truck_is_refused()
    {
        var (load, _, release) = await SeedAsync(truckCode: null);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Build().Service.ExecuteAsync(Request(load, release), "tester"));

        Assert.Equal("A carga CG000001 não tem placa: informe a placa na carga antes de expedir.", ex.Message);
    }

    [Fact]
    public async Task Release_of_another_item_is_refused()
    {
        var (load, _, release) = await SeedAsync(itemCode: "MILHO");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Build().Service.ExecuteAsync(Request(load, release), "tester"));

        Assert.Equal("A liberação escolhida é de outro produto.", ex.Message);
    }

    [Fact]
    public async Task Zero_weight_is_refused()
    {
        var (load, _, release) = await SeedAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Build().Service.ExecuteAsync(Request(load, release, 0m), "tester"));

        Assert.Equal("Informe o peso bruto maior que zero.", ex.Message);
    }

    /// <summary>
    /// Falha no vínculo depois da expedição: a transação aberta pelo serviço é desfeita, nunca confirmada. O InMemory
    /// não desfaz o que já foi salvo, então o teste prova a ORDEM; o desfazer real é conferido no SQL Server (Task 5).
    /// </summary>
    [Fact]
    public async Task Attach_failure_rolls_back_the_whole_transaction()
    {
        var (load, _, release) = await SeedAsync();
        var (service, counting) = Build(new ThrowingAttach(_db));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(Request(load, release), "tester"));

        Assert.Equal("vínculo recusado", ex.Message);
        Assert.Equal(1, counting.Begins);
        Assert.Equal(0, counting.Commits);
        Assert.Equal(1, counting.Rollbacks);
    }

    private sealed class ThrowingAttach(UnitOfWork db)
        : ShipmentLoadsAttachTransactionsService(db, new ShipmentLoadsMovementLogService(db.Context))
    {
        public override Task<ShipmentLoad> ExecuteAsync(
            Guid shipmentLoadKey, ICollection<Guid> storageTransactionKeys, Guid? transshipmentKey, string userName,
            CommitMode commitMode = CommitMode.Auto) =>
            throw new ApplicationException("vínculo recusado");
    }
}
```

O `Build()` aceita o vínculo como parâmetro opcional — `Build(ShipmentLoadsAttachTransactionsService? attachOverride = null)`,
usando `attachOverride ?? new ShipmentLoadsAttachTransactionsService(...)`. `ShipmentLoadsAttachTransactionsService.ExecuteAsync`
passa a ser `public virtual` (mudança mínima, sem efeito em produção). Acrescente `using SiagroB1.Infra.Enums;`.

No `ShipmentLoadEdmModelTests.cs`, acrescente (seguindo o padrão dos outros casos do arquivo, que montam o
`ODataConventionModelBuilder` + `ConfigureODataEntities()`):

```csharp
    [Fact]
    public void Ship_action_takes_the_load_release_and_weight()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "ShipmentLoadsShip");

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "ShipmentReleaseKey").Type.FullName());
        Assert.Equal("Edm.Double", action.Parameters.Single(p => p.Name == "GrossWeight").Type.FullName());
        Assert.Equal("Edm.DateTimeOffset", action.Parameters.Single(p => p.Name == "TransactionDate").Type.FullName());
    }
```

(se o arquivo não tiver um helper `Model()`, crie-o como em `PurchaseInvoiceNfeEdmModelTests`.)

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentLoadsShipServiceTests|FullyQualifiedName~ShipmentLoadEdmModelTests"`
Expected: FAIL na compilação — `The type or namespace name 'ShipmentLoadsShipService' could not be found`.

- [ ] **Step 3: Implement the service**

`SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsShipService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.ShippingTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.ShipmentLoads;

public record ShipmentLoadShipRequest(
    Guid ShipmentLoadKey,
    Guid ShipmentReleaseKey,
    string WarehouseCode,
    string TruckDriverCode,
    DateTime TransactionDate,
    decimal GrossWeight,
    string? Comments);

public record ShipmentLoadShipResult(Guid ShipmentLoadKey, Guid StorageTransactionKey, string? StorageTransactionCode);

/// <summary>
/// "Expedir" no detalhe da carga (spec 2026-10-06 peça 2): cria o par de romaneios da Expedição de Grãos e vincula a
/// saída à carga numa transação só — ou o romaneio nasce vinculado, ou nada é gravado.
/// </summary>
/// <remarks>
/// Placa, produto, filial e unidade vêm da CARGA (a homogeneidade do vínculo passa por construção); o parceiro vem do
/// contrato da liberação. O recálculo da liberação de embarque fica para DEPOIS do commit, como a expedição faz no
/// modo dela — antes do commit ele contaria a cópia de venda.
/// </remarks>
public class ShipmentLoadsShipService(
    IUnitOfWork db,
    ShippingTransactionsCreateService shippingCreate,
    ShipmentLoadsAttachTransactionsService attach,
    ShipmentReleasesRecalculateShippedService recalcShipped)
{
    public async Task<ShipmentLoadShipResult> ExecuteAsync(ShipmentLoadShipRequest request, string userName)
    {
        if (request.GrossWeight <= decimal.Zero)
            throw new ApplicationException("Informe o peso bruto maior que zero.");

        var load = await db.Context.ShipmentLoads.AsNoTracking()
                       .FirstOrDefaultAsync(x => x.Key == request.ShipmentLoadKey)
                   ?? throw new NotFoundException($"Shipment load not found key {request.ShipmentLoadKey}");

        if (load.LoadType != ShipmentLoadType.Normal ||
            load.Status is not (ShipmentLoadStatus.Planned or ShipmentLoadStatus.Open or ShipmentLoadStatus.InTransshipment))
            throw new ApplicationException($"A carga {load.Code} não aceita expedição nesta situação.");

        if (string.IsNullOrWhiteSpace(load.TruckCode))
            throw new ApplicationException($"A carga {load.Code} não tem placa: informe a placa na carga antes de expedir.");

        var release = await db.Context.ShipmentReleases.AsNoTracking()
                          .Include(x => x.PurchaseContract)
                          .FirstOrDefaultAsync(x => x.Key == request.ShipmentReleaseKey)
                      ?? throw new ApplicationException("Liberação de embarque não encontrada.");

        if (release.PurchaseContract?.ItemCode != load.ItemCode)
            throw new ApplicationException("A liberação escolhida é de outro produto.");

        var purchaseContractKey = ReleaseOriginRules.ShipsWithoutPurchaseLeg(release.Origin)
            ? (Guid?)null
            : release.PurchaseContractKey;

        var purchase = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            BranchCode = load.BranchCode,
            CardCode = release.PurchaseContract.CardCode,
            CardName = release.PurchaseContract.CardName,
            ItemCode = load.ItemCode,
            ItemName = load.ItemName,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = request.WarehouseCode,
            TruckCode = load.TruckCode,
            TruckDriverCode = request.TruckDriverCode,
            TransactionType = StorageTransactionType.Purchase,
            TransactionStatus = StorageTransactionsStatus.Pending,
            TransactionDate = request.TransactionDate,
            GrossWeight = request.GrossWeight,
            ShipmentReleaseKey = release.Key,
            Comments = request.Comments,
        };

        try
        {
            await db.BeginTransactionAsync();

            var shipping = await shippingCreate.ExecuteAsync(purchaseContractKey, purchase, userName, CommitMode.Deferred);
            var exit = shipping.SalesStorageTransaction!;

            await attach.ExecuteAsync(load.Key, [exit.Key], transshipmentKey: null, userName, CommitMode.Deferred);

            await db.CommitAsync();

            await recalcShipped.RecalculateAsync(release.Key);

            return new ShipmentLoadShipResult(load.Key, exit.Key, exit.Code);
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }
}
```

Confira os nomes reais e ajuste se o compilador recusar: `ReleaseOriginRules` (namespace das regras de origem, em
`Services/ShipmentReleases`), `ShippingTransaction.SalesStorageTransaction`, `StorageTransaction.Comments`,
`ShipmentLoad.ItemName`. `required` em alguma propriedade de `StorageTransaction` que falte acima (ex.:
`UnitOfMeasureCode`) já está atribuída; se o compilador apontar outra `required`, atribua-a a partir da carga ou da
liberação e registre no relatório.

`ShipmentLoadsAttachTransactionsService.ExecuteAsync` passa a ser `public virtual` (para o teste de falha).

- [ ] **Step 4: Web action, EDM e DI**

`SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsShipController.cs` (padrão do `ShipmentLoadsAttachTransactionsController`):

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsShipController(ShipmentLoadsShipService shipService) : ODataController
{
    [HttpPost("odata/ShipmentLoadsShip")]
    public async Task<ActionResult> Ship(ODataActionParameters parameters)
    {
        try
        {
            if (parameters == null ||
                !parameters.TryGetValue("Key", out var keyObj) || keyObj == null ||
                !parameters.TryGetValue("ShipmentReleaseKey", out var releaseObj) || releaseObj == null)
                return BadRequest("Selecione a liberação de embarque.");

            parameters.TryGetValue("WarehouseCode", out var warehouseObj);
            parameters.TryGetValue("TruckDriverCode", out var driverObj);
            parameters.TryGetValue("TransactionDate", out var dateObj);
            parameters.TryGetValue("GrossWeight", out var weightObj);
            parameters.TryGetValue("Comments", out var commentsObj);

            var warehouse = warehouseObj as string;
            var driver = driverObj as string;
            if (string.IsNullOrWhiteSpace(warehouse) || string.IsNullOrWhiteSpace(driver))
                return BadRequest("Informe o armazém e o motorista.");

            var date = dateObj is DateTimeOffset dto ? dto.DateTime : DateTime.Now.Date;
            var weight = weightObj is double d ? (decimal)d : decimal.Zero;

            var result = await shipService.ExecuteAsync(
                new ShipmentLoadShipRequest(
                    Guid.Parse(keyObj.ToString()!), Guid.Parse(releaseObj.ToString()!), warehouse, driver, date, weight,
                    commentsObj as string),
                User.Identity?.Name ?? "Unknown");

            return Ok(new
            {
                shipmentLoadKey = result.ShipmentLoadKey,
                storageTransactionKey = result.StorageTransactionKey,
                storageTransactionCode = result.StorageTransactionCode,
            });
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

Em `ODataConfigurations.cs`, logo depois do bloco da `ShipmentLoadsAttachTransactions`:

```csharp
        // Peça 2 (2026-10-06): "Expedir" no detalhe da carga — expedição + vínculo numa transação só.
        // ⚠️ Edm.Double no peso, nunca Edm.Decimal (o cliente serializaria como string e o 400 não nomeia o campo).
        var shipmentLoadsShip = modelBuilder.Action("ShipmentLoadsShip");
        shipmentLoadsShip.Parameter<Guid>("Key");
        shipmentLoadsShip.Parameter<Guid>("ShipmentReleaseKey");
        shipmentLoadsShip.Parameter<string>("WarehouseCode");
        shipmentLoadsShip.Parameter<string>("TruckDriverCode");
        shipmentLoadsShip.Parameter<DateTimeOffset>("TransactionDate");
        shipmentLoadsShip.Parameter<double>("GrossWeight");
        shipmentLoadsShip.Parameter<string>("Comments").Optional();
        shipmentLoadsShip.Returns<IActionResult>();
```

Em `ServiceCollectionExtensions.cs`, logo depois de `services.AddScoped<ShipmentLoadsAttachTransactionsService>();`:
`services.AddScoped<ShipmentLoadsShipService>();`.

- [ ] **Step 5: Run to verify they pass**

Run: o filtro do Step 2. Expected: PASS. Depois `dotnet build SiagroB1.sln` (0 erros) e `dotnet test SiagroB1.Application.Tests` (tudo verde).

- [ ] **Step 6: Commit**

```bash
git branch --show-current
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsShipService.cs SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsShipController.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsShipServiceTests.cs
git commit -m "feat(shipment): expedir na carga com o romaneio já vinculado" -m "A action ShipmentLoadsShip cria o par da Expedição de Grãos e vincula a saída à carga numa transação só; placa, produto e filial vêm da carga, o contrato vem da liberação." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw" -- SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsShipService.cs SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsAttachTransactionsService.cs SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsShipController.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsShipServiceTests.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadEdmModelTests.cs
```

---

### Task 3: Frontend — diálogo de faturamento vira helper compartilhado (sem mudar o `/shipment-billing`)

Repo `siagro-b1-frontend`; caminhos relativos a `webapp/`.

**Files:**
- Create: `helpers/ShipmentBillingDialog.ts`
- Modify: `view/shipmentBilling/fragments/Billing.fragment.xml` (modelos renomeados)
- Modify: `controller/shipmentBilling/Main.controller.ts` (passa a usar o helper)

**Interfaces:**
- Produces (Task 4 consome):

```ts
/** Carga a faturar — os campos que o diálogo usa. */
export type BillingLoad = {
  Key: string; Code: string; ItemCode: string; ItemName: string; BranchCode: string; AvailableQuantity: number;
  TruckDriverCode: string; TruckDriverName: string; TruckCode: string; CarrierCardCode: string; CarrierName: string;
};

/** O que o helper precisa da tela dona (membros protegidos do controller chegam como funções). */
export type BillingDialogHost = {
  controller: Controller;                       // dono do fragmento (handlers .saveBillingDialog/.closeBillingDialog/value helps)
  view: View;
  setBusy(busy: boolean): void;
  validateForm(formId: string): boolean;
  registerTableLayouts(root: ManagedObject): void;
  isTaxCalculationActive(branchCode: string): Promise<boolean>;
  onBilled(): void;                             // depois de faturar (também no erro): a tela atualiza o que precisa
  onClosed?(): void;                            // ao fechar (o /shipment-billing limpa a seleção da lista)
};

export default class ShipmentBillingDialog {
  constructor(host: BillingDialogHost);
  open(load: BillingLoad): Promise<void>;       // valida AvailableQuantity > 0 ("Carga sem saldo a faturar.")
  save(): Promise<void>;                        // o antigo saveBillingDialog, com a trava de reentrância
  close(): void;                                // o antigo closeBillingDialog (limpa a seleção da tabela de liberações)
}
```

- [ ] **Step 1: Rename the fragment models**

Em `view/shipmentBilling/fragments/Billing.fragment.xml`, troque **todas** as ocorrências de `viewModel>` por
`billing>`, `releases>` por `billingReleases>` e `branches>` por `billingBranches>` (inclusive `path: 'releases>/'` e
afins). Não mexa em `ui>` nem no trecho comentado com `fatura>`.

```bash
sed -i -e 's/viewModel>/billing>/g' -e 's/\breleases>/billingReleases>/g' -e 's/\bbranches>/billingBranches>/g' webapp/view/shipmentBilling/fragments/Billing.fragment.xml
```

Confira com `grep -c "viewModel>\|[^g]releases>\|[^g]branches>" ...` que nada sobrou.

- [ ] **Step 2: Create the helper**

`helpers/ShipmentBillingDialog.ts` com a API acima. O corpo é o código que hoje está em
`controller/shipmentBilling/Main.controller.ts`, **movido**, com estas trocas mecânicas:

| Hoje (Main.controller.ts) | No helper |
|---|---|
| `createBillingDialog()` | `private ensureDialog()` — `Fragment.load({ id: host.view.getId(), name: "siagrob1.view.shipmentBilling.fragments.Billing", controller: host.controller })`, `host.view.addDependent`, `host.registerTableLayouts(dialog)` |
| `loadBranches()` | `private loadBranches()` — modelo `billingBranches` |
| `openBillingDialog()` | `open(load)` — sem a leitura da tabela da lista: recebe a carga; mantém a recusa "Carga sem saldo a faturar."; `setData` no modelo `billing` com os mesmos campos de hoje; `TaxLocked` por `host.isTaxCalculationActive(load.BranchCode)`; `clearSelection()` na tabela de liberações (`host.view.byId("shipmentBillingSalesContractsTable")`); `loadAvailableReleases(load.ItemCode)`; abre |
| `loadAvailableReleases()` | `private` — modelo `billingReleases` |
| `saveBillingDialog()` | `save()` — mesma lógica, mesmos textos, modelo `billing`, `host.validateForm("shipmentBillingSalesContractsForm")`; no `finally` do faturamento chama `host.onBilled()` no lugar de `refreshData()` |
| `closeBillingDialog()` | `close()` — limpa a seleção da tabela de liberações, fecha, chama `host.onClosed?.()` |
| `createBusyDialog()` | `private` — `Fragment.load({ name: "...BusyDialog", controller: host.controller })` + `addDependent` |
| `_billingInFlight`, `_billingDialog`, `_busyDialog` | campos privados do helper |
| `this.getModel("x")` | `host.view.getModel("x")` (os três modelos são criados pelo construtor do helper com `host.view.setModel(new JSONModel(...), "billing" | "billingReleases" | "billingBranches")`) |
| `this.getModel()` (OData) | `host.view.getModel()` |

Os comentários explicativos que estão nesses métodos vão junto (eles explicam armadilhas reais: Select com
`selectedKey` de JSONModel, function sem envelope, trava antes do primeiro `await`). Os tipos `BilledRelease` e
`BillingForm` vão para o helper.

- [ ] **Step 3: `/shipment-billing` usa o helper**

Em `controller/shipmentBilling/Main.controller.ts`:
- tire os três `setModel` de `onInit` e crie o helper ali:

```ts
    this._billing = new ShipmentBillingDialog({
      controller: this,
      view: this.getView(),
      setBusy: (busy) => this.setBusy(busy),
      validateForm: (formId) => this.validateForm(formId),
      registerTableLayouts: (root) => this.registerTableLayouts(root),
      isTaxCalculationActive: (branchCode) => this.isTaxCalculationActive(branchCode),
      onBilled: () => this.refreshData(),
      onClosed: () => (this.byId("shipmentBillingTable") as Table).clearSelection(),
    });
```

- `openBillingDialog()` passa a só ler a carga selecionada da tabela (mesma recusa "Selecione uma carga para
  faturar.") e chamar `await this._billing.open(load)`;
- `saveBillingDialog()` → `return this._billing.save();`; `closeBillingDialog()` → `this._billing.close();`
  (continuam públicos: o fragmento os chama);
- remova os métodos que foram para o helper e os imports que sobrarem sem uso. `refreshData()` fica.

- [ ] **Step 4: Gates**

Run: `yarn ts-typecheck` e `yarn lint`. Expected: limpos.

- [ ] **Step 5: Commit**

```bash
git branch --show-current   # feature/shipment-load-ship-and-bill
git add webapp/helpers/ShipmentBillingDialog.ts
git commit -m "refactor(shipment): diálogo de faturamento da carga vira helper compartilhado" -m "O detalhe da carga vai abrir o mesmo diálogo do Faturamento da Expedição. Os modelos do fragmento ganharam nomes próprios (billing, billingReleases, billingBranches) porque o detalhe já usa viewModel." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw" -- webapp/helpers/ShipmentBillingDialog.ts webapp/view/shipmentBilling/fragments/Billing.fragment.xml webapp/controller/shipmentBilling/Main.controller.ts
```

---

### Task 4: Frontend — "Expedir" e "Faturar" no detalhe da carga

Repo `siagro-b1-frontend`; caminhos relativos a `webapp/`.

**Files:**
- Create: `view/shipmentLoads/fragments/ShipDialog.fragment.xml`
- Modify: `view/shipmentLoads/Detail.view.xml` (botões no `uxap:actions`, ~linha 27)
- Modify: `controller/shipmentLoads/Detail.controller.ts`

**Interfaces:**
- Consumes: action `ShipmentLoadsShip` (Task 2: parâmetros `Key`, `ShipmentReleaseKey`, `WarehouseCode`,
  `TruckDriverCode`, `TransactionDate`, `GrossWeight`, `Comments`; resposta camelCase
  `{ shipmentLoadKey, storageTransactionKey, storageTransactionCode }`); `ShipmentBillingDialog` (Task 3).

- [ ] **Step 1: Buttons**

Em `Detail.view.xml`, no `uxap:actions`, **antes** de "Recalcular Saldo":

```xml
					<!-- Peça 2 (2026-10-06): expedir e faturar sem sair da carga. O servidor recusa igual fora destas situações. -->
					<Button
						text="Expedir"
						type="Emphasized"
						press=".onShip"
						visible="{= ${path: 'LoadType', targetType: 'any'} === 'Normal' &amp;&amp; (${path: 'Status', targetType: 'any'} === 'Planned' || ${path: 'Status', targetType: 'any'} === 'Open' || ${path: 'Status', targetType: 'any'} === 'InTransshipment') }"/>
					<Button
						text="Faturar"
						type="Emphasized"
						press=".onBill"
						visible="{= ${path: 'LoadType', targetType: 'any'} === 'Normal' &amp;&amp; (${path: 'Status', targetType: 'any'} === 'Open' || ${path: 'Status', targetType: 'any'} === 'PartiallyInvoiced') &amp;&amp; ${AvailableQuantity} > 0 }"/>
```

(o `Detail` precisa ter `AvailableQuantity` selecionado: se a propriedade não estiver ligada em nenhum controle da
view, o `autoExpandSelect` não a traz — confira e, se faltar, acrescente-a ao `$select` do `bindElement` da carga.)

- [ ] **Step 2: Ship dialog fragment**

`view/shipmentLoads/fragments/ShipDialog.fragment.xml` — `Dialog` com título "Expedir na carga {ship>/LoadCode}",
ligado ao JSONModel `ship` e ao JSONModel `shipReleases` (array da function):

- `sap.m.Table` (`id="shipReleasesTable"`, `mode="SingleSelectLeft"`, `items="{shipReleases>/}"`) com as colunas
  Contrato (`{shipReleases>PurchaseContractCode}`), Fornecedor (`{shipReleases>FName}`), Origem
  (`{ path: 'shipReleases>Origin', formatter: '.formatter.releaseOriginText' }`) e Saldo
  (`{ path: 'shipReleases>AvailableQuantity', type: 'sap.ui.model.type.Float', formatOptions: { maxFractionDigits: 3 } }`
  + `{shipReleases>UnitOfMeasureCode}`); `noDataText="Nenhuma liberação de embarque com saldo para este produto e armazém."`.
- `sap.ui.layout.form.Form` (`ColumnLayout`, regra do projeto: nunca SimpleForm em código novo) com:
  - Armazém: `Input` `value="{ship>/WarehouseCode}"`, `showValueHelp`, `valueHelpOnly`, `valueHelpRequest=".openWarehouseValueHelp"`,
    `change=".onShipWarehouseChange"`, `required`;
  - Motorista: `Input` `value="{ship>/TruckDriverCode}"`, `valueHelpRequest=".openTruckDriversValueHelp"`, `required`;
  - Data: `DatePicker` `value="{ship>/TransactionDate}"` com `valueFormat="yyyy-MM-dd"` e `displayFormat="dd/MM/yyyy"`, `required`;
  - Peso bruto: `Input` `type="Number"` `value="{ship>/GrossWeight}"`, `required`;
  - Observação: `TextArea` `value="{ship>/Comments}"`.
  Confira como `openWarehouseValueHelp`/`openTruckDriversValueHelp` (em `common/CommonController.ts`) escrevem o valor
  de volta (eles escrevem no Input da origem do evento; se dependerem de `CustomData` `descriptionProperty`, siga o
  mesmo padrão do `shippingTransaction/fragments/Form.fragment.xml`).
- Rodapé: `Button` "Expedir" (`type="Emphasized"`, `press=".onConfirmShip"`) e "Cancelar" (`press=".onCloseShip"`).

- [ ] **Step 3: Controller**

Em `Detail.controller.ts`:

```ts
  private _shipDialog: Dialog;
  /** Trava de reentrância do "Expedir": setada ANTES do primeiro await (mesmo padrão do faturamento). */
  private _shipInFlight = false;
  private _billing: ShipmentBillingDialog;
```

- `onInit`: `this.getView().setModel(new JSONModel({}), "ship"); this.getView().setModel(new JSONModel([]), "shipReleases");`
  e crie `this._billing = new ShipmentBillingDialog({ controller: this, view: this.getView(), setBusy: …, validateForm: …,
  registerTableLayouts: …, isTaxCalculationActive: …, onBilled: () => this.refreshAll() });` (mesmo formato da Task 3,
  sem `onClosed`).
- `async onShip()`: lê a carga do contexto (`Key`, `Code`, `ItemCode`, `WarehouseCode`, `TruckDriverCode`); preenche
  `ship` com `{ LoadKey, LoadCode, ItemCode, WarehouseCode, TruckDriverCode, TransactionDate: hoje "yyyy-MM-dd",
  GrossWeight: "", Comments: "" }`; carrega as liberações (`loadShipReleases`); carrega o fragmento uma vez
  (`Fragment.load({ id: view.getId(), name: "siagrob1.view.shipmentLoads.fragments.ShipDialog", controller: this })`,
  `addDependent`) e abre.
- `private async loadShipReleases()`: `bindContext("/ShipmentReleasesGetPurchaseContracts(...)")` com `ItemCode` e
  `WarehouseCode` do modelo `ship`, `invoke()`, `shipReleases.setData(getBoundContext().getObject())` — mesmo padrão
  de `shippingTransaction/SelectShipmentRelease.controller.ts:getPurchaseContracts`; limpa a seleção da tabela.
- `onShipWarehouseChange()`: `loadShipReleases()`.
- `async onConfirmShip()`:
  1. `if (this._shipInFlight) return; this._shipInFlight = true;` — antes de qualquer `await`;
  2. valida: liberação selecionada ("Selecione a liberação de embarque."), armazém e motorista ("Informe o armazém e o
     motorista."), peso `Number(GrossWeight) > 0` ("Informe o peso bruto maior que zero.");
  3. `bindContext("/ShipmentLoadsShip(...)")` com `Key`, `ShipmentReleaseKey` (da linha selecionada:
     `ShipmentReleaseKey`), `WarehouseCode`, `TruckDriverCode`, `TransactionDate` = `` `${TransactionDate}T12:00:00-03:00` ``
     (meio-dia de Brasília: a conversão para o fuso do servidor não muda o dia), `GrossWeight` = `Number(...)`,
     `Comments`; `invoke()`;
  4. sucesso: lê `storageTransactionCode` do resultado (`getBoundContext().getObject()`), fecha o diálogo,
     `MessageToast.show(\`Romaneio ${code} expedido na carga.\`)`, `this.refreshAll()`;
  5. erro: a mensagem do servidor já aparece pelo handler global de mensagens OData (mesmo comentário do faturamento);
     o diálogo continua aberto;
  6. `finally`: `this._shipInFlight = false`.
- `onCloseShip()`: fecha.
- `async onBill()`: lê a carga do contexto (os campos de `BillingLoad`; `requestObject()` se algum não estiver no
  cache) e chama `await this._billing.open(load)`.
- `saveBillingDialog()` → `return this._billing.save();`; `closeBillingDialog()` → `this._billing.close();` (o
  fragmento de faturamento chama esses nomes).
- Imports: `ShipmentBillingDialog` e `BillingLoad` de `siagrob1/helpers/ShipmentBillingDialog`.

- [ ] **Step 4: Gates**

Run: `yarn ts-typecheck` e `yarn lint`. Expected: limpos.

- [ ] **Step 5: Commit**

```bash
git branch --show-current
git add webapp/view/shipmentLoads/fragments/ShipDialog.fragment.xml
git commit -m "feat(shipment): expedir e faturar pelo detalhe da carga" -m "Expedir cria o romaneio já vinculado à carga; Faturar abre o mesmo diálogo do Faturamento da Expedição. A CEAGUI deixa de passar por quatro telas por caminhão." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw" -- webapp/view/shipmentLoads/fragments/ShipDialog.fragment.xml webapp/view/shipmentLoads/Detail.view.xml webapp/controller/shipmentLoads/Detail.controller.ts
```

---

### Task 5: Verificação ponta a ponta (CEAGUI, homologação)

**Files:** nenhum código. Banco `CEAGUI_SIAGRO_DEV` (local). Sem migration nesta peça.

- [ ] **Step 1: Subir a stack** — perfil `ceagui` (Gateway, esperar "Loading proxy data from config", depois Web e
  Reports) e `yarn start:dev`; `admin`/`1234`, filial CEAGUI; os dois repos no branch
  `feature/shipment-load-ship-and-bill`.

- [ ] **Step 2: Dados fictícios** (pela tela ou pela API como admin; ⚠️ nunca tocar nos documentos reais
  DS000001–DS000146 nem nos parceiros reais — crie parceiros fictícios se precisar). Monte:
  - contrato de **compra** aprovado de SOJA ou TRIGO (produto já cadastrado), com liberação de embarque **ativa** para
    um armazém;
  - contrato de **venda** aprovado do mesmo produto, com local de entrega e liberação de entrega **ativa**;
  - **carga** planejada do mesmo produto, filial, placa e armazém.

- [ ] **Step 3: Roteiro**
  1. **Expedir** pela carga (liberação de compra, peso 30.000): o romaneio aparece em "Romaneios", a carga passa a
     "Carregada", e o saldo da liberação de embarque cai 30.000.
  2. **Duplo clique** no "Expedir" do diálogo: só um romaneio.
  3. **Faturar** pela carga (liberação de venda, tipo NF-e): o documento aparece em "Documentos de Saída",
     Confirmado; a liberação de venda baixa. Transmitir a NF-e no documento (homologação).
  4. **Carga cancelada:** criar outra carga, cancelar, abrir o detalhe — o "Expedir" não aparece; chamar a action pela
     API devolve 400 "A carga … não aceita expedição nesta situação." e nenhum romaneio novo existe (conferir por SQL).
  5. **`/shipment-billing`:** faturar uma carga pela tela antiga (expedir outra carga primeiro) — mesmo comportamento
     de antes (seleção, saldo, documento criado, lista atualizada).
  6. **Telas antigas:** Expedição de Grãos e Vincular Romaneios continuam funcionando (uma expedição + vínculo).

- [ ] **Step 4: Derrubar a stack** (portas 50000, 5246, 8081/58000, 8080 por PID; conferir de novo depois) e
  relatório com o que foi visto em cada passo, números de NF-e de homologação consumidos e o que ficou sem exercer.
