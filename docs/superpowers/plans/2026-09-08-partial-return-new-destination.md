# Retorno parcial com destino "novo destino" — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir devolver parte da quantidade de um romaneio no destino "novo destino" (`Rebilling`) do retorno de documento de saída, partindo o romaneio em dois — a parte que ficou com o cliente e a parte que segue no caminhão.

**Architecture:** A origem encolhe para `W − q` e continua faturada na nota; nasce um romaneio irmão de `q`, `Confirmed` e solto, que reaparece no Faturamento de Expedição e na Montagem de Carga. Como toda fórmula de saldo do sistema é um somatório por tipo de transação, `15 + 25 = 40` preserva armazém, endereço/lote e liberação sem tocar em nenhum serviço de saldo. Uma coluna nova (`SplitFromStorageTransactionKey`) liga irmão à origem e é por ela que o estorno funde de volta.

**Tech Stack:** .NET 10, EF Core (SQL Server), xUnit + EF Core InMemory, OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-09-08-partial-return-new-destination-design.md`

## Global Constraints

- **NUNCA commitar ou dar push.** Os commits são feitos manualmente pelo usuário. A única escrita em git permitida é `git add` — e todo arquivo NOVO deve ser staged com `git add <path>` assim que criado, para que `git status`/`git diff` mostrem o conjunto completo da mudança.
- Repositórios são **dois e independentes**: `siagro-b1-backend/` e `siagro-b1-frontend/`. Comandos git sempre com `git -C "<caminho do repo>"` ou dentro do repo exato.
- **Identificadores em inglês, texto de usuário em pt-BR.** Classes, métodos, propriedades e colunas em inglês; mensagens de erro de negócio, rótulos de tela e comentários em pt-BR.
- Backend: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj` a partir de `siagro-b1-backend/`.
- Frontend: os gates são `yarn ts-typecheck` e `yarn lint`. **Não rode `yarn test`** — ele tem gate de cobertura de 50% contra ~2,4% reais e nunca passa neste projeto.
- Tolerância de comparação de peso em todo o backend: `0.001m`. Arredondamento sempre `decimal.Round(x, 3, MidpointRounding.ToEven)`.
- Migrations: gerar com `dotnet ef migrations add`, **ler a migration gerada antes de aplicar**, e aplicar com ambiente explícito: `ASPNETCORE_ENVIRONMENT=Yokotobi dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`. Nunca usar o perfil `db-migration`, que aponta produção.
- `dotnet build` antes de qualquer `dotnet ef ... --no-build`, senão o assembly velho esconde a migration nova.

---

## File Structure

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Entities/StorageTransaction.cs` | ganha `SplitFromStorageTransactionKey` + navegação |
| `SiagroB1.Infra/Context/AppDbContext.cs` | FK auto-referente declarada à mão, `NoAction` |
| `SiagroB1.Migrations/AppContext/*_AddStorageTransactionSplitFrom.cs` | coluna + FK + índice |
| `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsSplitService.cs` | **novo** — parte um romaneio de venda em origem encolhida + irmão livre |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReturnService.cs` | classifica linha cheia × parcial; chama o split |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReverseConfirmService.cs` | discriminador `isNewFlow`, escopo do cancelamento da devolução, desfazer a divisão |
| `SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsReverseService.cs` | guard: não estorna Expedição de romaneio dividido |
| `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` | registro do serviço novo |
| `siagro-b1-frontend/webapp/view/salesInvoices/fragments/Return.fragment.xml` | quantidade editável nos dois destinos |
| `siagro-b1-frontend/webapp/controller/salesInvoices/Main.controller.ts` | validação e payload nos dois destinos; confirmação do split |

---

### Task 1: Coluna `SplitFromStorageTransactionKey`

**Files:**
- Modify: `SiagroB1.Domain/Entities/StorageTransaction.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (bloco de FKs de `StorageTransaction`, por volta da linha 235)
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddStorageTransactionSplitFrom.cs` (gerada)
- Test: `SiagroB1.Application.Tests/Infra/StorageTransactionSplitModelTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `StorageTransaction.SplitFromStorageTransactionKey` (`Guid?`) e `StorageTransaction.SplitFromStorageTransaction` (`StorageTransaction?`).

- [ ] **Step 1: Escrever o teste que falha**

Crie `SiagroB1.Application.Tests/Infra/StorageTransactionSplitModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class StorageTransactionSplitModelTests
{
    /// <summary>
    /// A divisão de romaneio é auto-referente e NUNCA em cascata: apagar a origem com irmão vivo
    /// levaria junto um romaneio que pode já estar faturado noutra nota.
    /// </summary>
    [Fact]
    public void SplitFromStorageTransaction_IsASelfReferenceWithNoAction()
    {
        // Usa o provider SqlServer só para materializar o modelo relacional; sem conexão.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;

        using var context = new AppDbContext(options);

        var entityType = context.Model.FindEntityType(typeof(StorageTransaction));
        Assert.NotNull(entityType);

        var props = entityType!.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains("SplitFromStorageTransactionKey", props);

        var fk = entityType.GetForeignKeys()
            .Single(f => f.Properties.Any(p => p.Name == "SplitFromStorageTransactionKey"));

        Assert.Equal(typeof(StorageTransaction), fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
    }
}
```

- [ ] **Step 2: Rodar o teste e confirmar que falha**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~StorageTransactionSplitModelTests"
```

Esperado: FALHA de compilação — `StorageTransaction` não tem `SplitFromStorageTransactionKey`.

- [ ] **Step 3: Adicionar a propriedade na entidade**

Em `SiagroB1.Domain/Entities/StorageTransaction.cs`, logo abaixo do bloco de `GeneratedByReturnInvoiceKey`:

```csharp
    /// <summary>
    /// Romaneio de ORIGEM do qual este nasceu, quando um retorno parcial com destino "novo
    /// destino" partiu a carreta em duas. Preenchida só no romaneio IRMÃO — o que carrega a
    /// quantidade devolvida e volta ao pool de faturamento.
    /// </summary>
    /// <remarks>
    /// A divisão preserva o somatório: origem <c>W − q</c> + irmão <c>q</c> = <c>W</c>, que é o
    /// que dispensa mexer em qualquer consulta de saldo (armazém, endereço/lote, liberação).
    /// <para>
    /// É por esta coluna que <c>SalesInvoicesReverseConfirmService</c> funde o irmão de volta na
    /// origem ao estornar o retorno, e que <c>ShippingTransactionsReverseService</c> recusa
    /// estornar a Expedição de um romaneio dividido — depois do split o par compra/venda deixa de
    /// bater 1:1.
    /// </para>
    /// <para>
    /// FK auto-referente com <c>NoAction</c>, como todo o projeto: o irmão pode já estar faturado
    /// noutra nota, e apagar a origem em cascata o levaria junto.
    /// </para>
    /// </remarks>
    public Guid? SplitFromStorageTransactionKey { get; set; }
    public virtual StorageTransaction? SplitFromStorageTransaction { get; set; }
```

- [ ] **Step 4: Declarar a FK à mão no `AppDbContext`**

Em `SiagroB1.Infra/Context/AppDbContext.cs`, logo depois do bloco de `GeneratedByReturnInvoice`:

```csharp
        // Auto-referência sem coleção inversa: a origem não precisa navegar para os irmãos (quem
        // faz isso é a consulta por SplitFromStorageTransactionKey), e uma coleção a mais em
        // StorageTransaction entraria no EDM do OData sem ninguém pedir.
        modelBuilder.Entity<StorageTransaction>()
            .HasOne(x => x.SplitFromStorageTransaction)
            .WithMany()
            .HasForeignKey(x => x.SplitFromStorageTransactionKey)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<StorageTransaction>()
            .HasIndex(x => x.SplitFromStorageTransactionKey);
```

- [ ] **Step 5: Rodar o teste e confirmar que passa**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~StorageTransactionSplitModelTests"
```

Esperado: PASSA.

- [ ] **Step 6: Gerar a migration e LER o que ela produziu**

```bash
dotnet build
dotnet ef migrations add AddStorageTransactionSplitFrom --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Abra o arquivo gerado em `SiagroB1.Migrations/AppContext/`. O `Up()` deve conter **apenas**:
`AddColumn<Guid>` de `SplitFromStorageTransactionKey` em `STORAGE_TRANSACTIONS` (nullable), o
`CreateIndex` correspondente e o `AddForeignKey` auto-referente com
`onDelete: ReferentialAction.NoAction`.

⚠️ Se aparecer qualquer `AlterColumn` ou `DropColumn` de outra coluna, é drift de snapshot: apague
essas operações do `Up()`/`Down()` à mão antes de aplicar. Nunca aplique uma migration com
operações que você não pediu.

- [ ] **Step 7: Aplicar a migration no banco de desenvolvimento**

```bash
ASPNETCORE_ENVIRONMENT=Yokotobi dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

- [ ] **Step 8: Stage dos arquivos novos**

```bash
git -C . add SiagroB1.Application.Tests/Infra/StorageTransactionSplitModelTests.cs
git -C . add SiagroB1.Migrations/AppContext/
```

(Não commitar — os commits são do usuário.)

---

### Task 2: `StorageTransactionsSplitService`

**Files:**
- Create: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsSplitService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (perto da linha 432, junto de `StorageTransactionsCopyService`)
- Test: `SiagroB1.Application.Tests/StorageTransactions/StorageTransactionsSplitServiceTests.cs`

**Interfaces:**
- Consumes: `StorageTransaction.SplitFromStorageTransactionKey` (Task 1); `StorageTransactionCopyFactory.CreateFrom(StorageTransaction, string)`; `StorageTransactionsCreateService.ExecuteAsync(StorageTransaction, string, TransactionCode, CommitMode)`; `StorageTransactionsConfirmedService.ExecuteAsync(StorageTransaction, string, CommitMode, bool isShipmentTransaction)`.
- Produces:
  - `public sealed record StorageTransactionSplit(StorageTransaction Origin, StorageTransaction Sibling)`
  - `StorageTransactionsSplitService.ExecuteAsync(StorageTransaction origin, decimal quantity, Guid returnInvoiceKey, string userName) → Task<StorageTransactionSplit>`

- [ ] **Step 1: Escrever os testes que falham**

Crie `SiagroB1.Application.Tests/StorageTransactions/StorageTransactionsSplitServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// Divisão de um romaneio de venda em origem encolhida + irmão livre, que é o que permite o
/// retorno parcial com destino "novo destino".
/// </summary>
public class StorageTransactionsSplitServiceTests
{
    private const string CardCode = "C0001";
    private const string Warehouse = "ARM01";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static IBusinessPartnerService Partners() =>
        new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [CardCode] = "CLIENTE TESTE" },
            states: new Dictionary<string, string> { [CardCode] = "RS" });

    private static FakeItemService Items() =>
        new(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" });

    private static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string> { [Warehouse] = "ARMAZEM CEAGESP" });

    private StorageTransactionsCreateService StorageCreate() =>
        new(_db,
            new FakeDocNumberSequenceService(),
            Partners(),
            Items(),
            Warehouses(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    private StorageTransactionsConfirmedService StorageConfirm() =>
        new(_db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

    private StorageTransactionsSplitService Service() =>
        new(_db, StorageCreate(), StorageConfirm());

    /// <summary>Romaneio de venda faturado de 40.000, do jeito que o faturamento legado deixa.</summary>
    private async Task<StorageTransaction> SeedShipmentAsync(
        Guid? shipmentLoadKey = null,
        Guid? shipmentReleaseKey = null,
        StorageTransactionsStatus status = StorageTransactionsStatus.Invoiced)
    {
        var shipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1",
            CardCode = CardCode,
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            WarehouseCode = Warehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = 40_000m,
            NetWeight = 40_000m,
            InvoiceQty = 40_000m,
            InvoiceNumber = "12345",
            InvoiceSerie = "1",
            IsInvoiced = true,
            WeighingTicketKey = Guid.NewGuid(),
            ShipmentLoadKey = shipmentLoadKey,
            ShipmentReleaseKey = shipmentReleaseKey,
            SalesInvoiceKey = Guid.NewGuid(),
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = status,
        };

        _db.Context.StorageTransactions.Add(shipment);

        if (!await _db.Context.Branchs.AnyAsync(x => x.Code == "01"))
            _db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "RS" });

        await _db.SaveChangesAsync();

        return shipment;
    }

    /// <summary>
    /// A divisão encolhe a origem e cria o irmão com o restante, livre para ser faturado de novo.
    /// </summary>
    [Fact]
    public async Task Splitting_shrinks_the_origin_and_creates_a_free_sibling()
    {
        var origin = await SeedShipmentAsync();
        var returnInvoiceKey = Guid.NewGuid();

        var split = await Service().ExecuteAsync(origin, 25_000m, returnInvoiceKey, "tester");

        Assert.Equal(15_000m, split.Origin.NetWeight);
        Assert.Equal(15_000m, split.Origin.GrossWeight);
        Assert.Equal(15_000m, split.Origin.InvoiceQty);
        Assert.True(split.Origin.IsInvoiced);
        Assert.Equal(StorageTransactionsStatus.Invoiced, split.Origin.TransactionStatus);
        Assert.NotNull(split.Origin.SalesInvoiceKey);

        Assert.Equal(25_000m, split.Sibling.NetWeight);
        Assert.Equal(25_000m, split.Sibling.GrossWeight);
        Assert.Equal(StorageTransactionsStatus.Confirmed, split.Sibling.TransactionStatus);
        Assert.Null(split.Sibling.SalesInvoiceKey);
        Assert.Null(split.Sibling.ShipmentLoadKey);
        Assert.Equal(origin.Key, split.Sibling.SplitFromStorageTransactionKey);
        Assert.Equal(returnInvoiceKey, split.Sibling.GeneratedByReturnInvoiceKey);
    }

    /// <summary>
    /// ⚠️ O invariante que dispensa mexer em qualquer serviço de saldo: 15 + 25 = 40. Se a
    /// divisão mudasse o saldo do armazém, o grão teria sido inventado ou destruído.
    /// </summary>
    [Fact]
    public async Task Splitting_does_not_change_the_warehouse_balance()
    {
        var origin = await SeedShipmentAsync();

        var before = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _db.Context, Warehouse, "SOJA");

        await Service().ExecuteAsync(origin, 25_000m, Guid.NewGuid(), "tester");

        var after = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
            _db.Context, Warehouse, "SOJA");

        Assert.Equal(before, after);
    }

    /// <summary>
    /// O irmão HERDA a liberação. Sem a chave, encolher a origem devolveria 25.000 de saldo à
    /// liberação de origem OwnershipTransfer/SalesReturn — como se o grão não tivesse saído.
    /// </summary>
    [Fact]
    public async Task The_sibling_inherits_the_shipment_release_so_the_shipped_quantity_holds()
    {
        var releaseKey = Guid.NewGuid();
        var origin = await SeedShipmentAsync(shipmentReleaseKey: releaseKey);

        var recalc = new ShipmentReleasesRecalculateShippedService(_db.Context);

        var before = await recalc.CalculateShippedAsync(releaseKey, ReleaseOrigin.OwnershipTransfer);

        await Service().ExecuteAsync(origin, 25_000m, Guid.NewGuid(), "tester");

        var after = await recalc.CalculateShippedAsync(releaseKey, ReleaseOrigin.OwnershipTransfer);

        Assert.Equal(before, after);
    }

    /// <summary>
    /// O irmão NÃO herda a pesagem: WeighingTicketsCancelService chega ao romaneio da pesagem por
    /// FirstOrDefault, e dois romaneios com o mesmo ticket fariam cancelar/reabrir o errado.
    /// </summary>
    [Fact]
    public async Task The_sibling_does_not_inherit_the_weighing_ticket()
    {
        var origin = await SeedShipmentAsync();

        var split = await Service().ExecuteAsync(origin, 25_000m, Guid.NewGuid(), "tester");

        Assert.Null(split.Sibling.WeighingTicketKey);
        Assert.False(split.Sibling.IsInvoiced);
        Assert.Null(split.Sibling.InvoiceNumber);
        Assert.Equal(decimal.Zero, split.Sibling.InvoiceQty);
    }

    /// <summary>Romaneio dentro de carga tem tela de recusa própria e não pode ser dividido aqui.</summary>
    [Fact]
    public async Task Splitting_a_shipment_inside_a_load_is_refused()
    {
        var origin = await SeedShipmentAsync(shipmentLoadKey: Guid.NewGuid());

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(origin, 25_000m, Guid.NewGuid(), "tester"));

        Assert.Contains("carga", error.Message);
    }

    /// <summary>
    /// Dividir pelo peso inteiro não é divisão, é retorno cheio — e deixaria a origem com zero.
    /// O valor de teste é claramente acima do peso, e não NetWeight + 0.001: a tolerância de
    /// arredondamento engoliria a diferença e o teste passaria sem provar nada.
    /// </summary>
    [Fact]
    public async Task Splitting_by_the_whole_weight_or_more_is_refused()
    {
        var origin = await SeedShipmentAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(origin, 40_000m, Guid.NewGuid(), "tester"));

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(origin, 41_000m, Guid.NewGuid(), "tester"));
    }

    /// <summary>Quantidade zerada ou negativa é erro de digitação, não regra de negócio.</summary>
    [Fact]
    public async Task Splitting_by_zero_is_refused()
    {
        var origin = await SeedShipmentAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(origin, decimal.Zero, Guid.NewGuid(), "tester"));
    }
}
```

- [ ] **Step 2: Rodar os testes e confirmar que falham**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~StorageTransactionsSplitServiceTests"
```

Esperado: FALHA de compilação — `StorageTransactionsSplitService` não existe.

- [ ] **Step 3: Escrever o serviço**

Crie `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsSplitService.cs`:

```csharp
using System.Globalization;
using SiagroB1.Application.Services.StorageTransactions.Factories;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.StorageTransactions;

/// <summary>A origem encolhida e o irmão que nasceu dela.</summary>
public sealed record StorageTransactionSplit(
    StorageTransaction Origin,
    StorageTransaction Sibling);

/// <summary>
/// Parte um romaneio de venda faturado em dois: a origem encolhe para o que ficou com o cliente e
/// nasce um irmão, <c>Confirmed</c> e solto, com a quantidade que voltou.
/// </summary>
/// <remarks>
/// É o que torna possível o retorno PARCIAL com destino "novo destino": ali a mercadoria continua
/// embarcada e precisa de um romaneio próprio para ser faturada a outro cliente, enquanto a parte
/// entregue segue faturada no documento original.
/// <para>
/// <b>O invariante:</b> <c>(W − q) + q = W</c>. Toda fórmula de saldo do sistema — armazém,
/// endereço/lote, liberação — é um somatório por tipo de transação, então a divisão não muda
/// nenhuma delas e nenhum serviço de saldo precisou ser tocado. Quebrar esse invariante (deixar a
/// origem cheia, ou dar peso ao irmão sem tirar da origem) inventa grão no armazém.
/// </para>
/// <para>
/// ⚠️ <b>O irmão herda <c>ShipmentReleaseKey</c> de propósito.</b> Em liberação de origem
/// <c>OwnershipTransfer</c> ou <c>SalesReturn</c> o consumo é medido pelo eixo de venda
/// (<c>SalesShipment − SalesShipmentReturn</c>); sem a chave, encolher a origem devolveria à
/// liberação um saldo de grão que já saiu.
/// </para>
/// <para>
/// ⚠️ <b>O irmão NÃO herda <c>WeighingTicketKey</c>.</b> <c>WeighingTicketsCancelService</c> e
/// <c>WeighingTicketsReOpenService</c> chegam ao romaneio da pesagem por
/// <c>FirstOrDefault(x =&gt; x.WeighingTicketKey == key)</c>; dois romaneios apontando o mesmo
/// ticket fariam cancelar/reabrir escolher o errado.
/// </para>
/// <para>
/// <b>Tudo em <see cref="CommitMode.Deferred"/>:</b> a divisão sempre roda dentro da transação de
/// um retorno, e <c>UnitOfWork.CommitAsync</c> comita e zera a transação incondicionalmente — um
/// serviço interno em <c>Auto</c> comitaria a transação do chamador no meio da operação.
/// </para>
/// </remarks>
public class StorageTransactionsSplitService(
    IUnitOfWork db,
    StorageTransactionsCreateService createService,
    StorageTransactionsConfirmedService confirmService)
{
    private const decimal Tolerance = 0.001m;

    /// <summary>Cultura das quantidades que vão para texto lido pelo operador.</summary>
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<StorageTransactionSplit> ExecuteAsync(
        StorageTransaction origin,
        decimal quantity,
        Guid returnInvoiceKey,
        string userName)
    {
        Validate(origin, quantity);

        var returned = decimal.Round(quantity, 3, MidpointRounding.ToEven);
        var share = returned / origin.NetWeight;
        var remaining = decimal.Round(origin.NetWeight - returned, 3, MidpointRounding.ToEven);

        // Descontos rateados na proporção; a sobra de arredondamento fica na origem, que é a que
        // continua valendo para o faturamento já emitido.
        var siblingDrying = Round(origin.DryingDiscount * share);
        var siblingCleaning = Round(origin.CleaningDiscount * share);
        var siblingOthers = Round(origin.OthersDicount * share);

        var sibling = StorageTransactionCopyFactory.CreateFrom(origin, userName);

        sibling.GrossWeight = returned;
        sibling.NetWeight = returned;
        sibling.DryingDiscount = siblingDrying;
        sibling.CleaningDiscount = siblingCleaning;
        sibling.OthersDicount = siblingOthers;

        // Nada do faturamento da origem viaja para o irmão: ele nasce para ser faturado de novo.
        sibling.InvoiceNumber = null;
        sibling.InvoiceSerie = null;
        sibling.InvoiceQty = decimal.Zero;
        sibling.ChaveNFe = null;
        sibling.InvoicedAt = null;
        sibling.IsInvoiced = false;

        sibling.WeighingTicketKey = null;
        sibling.SalesShipmentReleaseKey = null;
        sibling.ReturnInvoiceKey = null;
        sibling.ReturnedAt = null;
        sibling.ReturnedBy = null;

        sibling.SplitFromStorageTransactionKey = origin.Key;
        sibling.GeneratedByReturnInvoiceKey = returnInvoiceKey;

        sibling.Comments = Truncate(
            $"Romaneio gerado pela divisão do {origin.Code} num retorno parcial: " +
            $"{returned.ToString("N3", PtBr)} de {origin.NetWeight.ToString("N3", PtBr)}.");

        // A origem encolhe ANTES de o irmão ser confirmado: o confirm de romaneio de venda lê o
        // saldo do armazém, e sem encolher primeiro a mesma quantidade estaria contada duas vezes.
        origin.DryingDiscount = Round(origin.DryingDiscount - siblingDrying);
        origin.CleaningDiscount = Round(origin.CleaningDiscount - siblingCleaning);
        origin.OthersDicount = Round(origin.OthersDicount - siblingOthers);
        origin.GrossWeight = remaining;
        origin.NetWeight = remaining;
        origin.InvoiceQty = remaining;
        origin.Comments = Append(
            origin.Comments,
            $"Dividido por retorno parcial: {returned.ToString("N3", PtBr)} seguiram para novo " +
            $"destino em romaneio próprio; restam {remaining.ToString("N3", PtBr)} faturados.");
        origin.UpdatedAt = DateTime.Now;
        origin.UpdatedBy = userName;

        await db.SaveChangesAsync();

        await createService.ExecuteAsync(
            sibling, userName, TransactionCode.StorageTransaction, CommitMode.Deferred);

        await db.SaveChangesAsync();

        // isShipmentTransaction: true pula a validação "quantidade embarcada superior ao saldo do
        // armazém". Ela não se aplica: o grão já saiu do armazém, contado pela origem, e a origem
        // acabou de encolher pelo mesmo tanto — não há saída nova a validar.
        await confirmService.ExecuteAsync(
            sibling, userName, CommitMode.Deferred, isShipmentTransaction: true);

        await db.SaveChangesAsync();

        return new StorageTransactionSplit(origin, sibling);
    }

    private static void Validate(StorageTransaction origin, decimal quantity)
    {
        if (origin.TransactionType != StorageTransactionType.SalesShipment)
        {
            throw new ApplicationException(
                $"O documento {origin.Code} não é um romaneio de embarque e não pode ser dividido.");
        }

        if (origin.TransactionStatus != StorageTransactionsStatus.Invoiced)
        {
            throw new ApplicationException(
                $"O romaneio {origin.Code} está em situação {origin.TransactionStatus} e não pode " +
                "ser dividido.");
        }

        // Romaneio de carga tem tela de recusa própria, que mexe no saldo da carga; dividi-lo aqui
        // deixaria o total da carga apontando um peso que o romaneio não tem mais.
        if (origin.ShipmentLoadKey != null)
        {
            throw new ApplicationException(
                $"O romaneio {origin.Code} está montado numa carga. Registre a recusa pela tela de " +
                "Montagem de Carga.");
        }

        if (quantity <= Tolerance)
        {
            throw new ApplicationException(
                $"Informe a quantidade a devolver do romaneio {origin.Code}.");
        }

        if (quantity >= origin.NetWeight - Tolerance)
        {
            throw new ApplicationException(
                $"A quantidade a devolver do romaneio {origin.Code} " +
                $"({quantity.ToString("N3", PtBr)}) tem de ser menor que o peso líquido dele " +
                $"({origin.NetWeight.ToString("N3", PtBr)}) para que a carreta seja dividida.");
        }
    }

    private static decimal Round(decimal value) =>
        decimal.Round(value, 3, MidpointRounding.ToEven);

    /// <summary>Concatena respeitando o VARCHAR(500) da coluna.</summary>
    private static string Append(string? current, string addition)
    {
        var merged = string.IsNullOrWhiteSpace(current) ? addition : $"{current} {addition}";
        return Truncate(merged);
    }

    private static string Truncate(string value) =>
        value.Length <= 500 ? value : value[..500];
}
```

- [ ] **Step 4: Registrar no contêiner de DI**

Em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, junto do registro de
`StorageTransactionsCopyService` (por volta da linha 432):

```csharp
        services.AddScoped<StorageTransactionsSplitService>();
```

- [ ] **Step 5: Rodar os testes e confirmar que passam**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~StorageTransactionsSplitServiceTests"
```

Esperado: os 7 testes PASSAM.

- [ ] **Step 6: Stage dos arquivos novos**

```bash
git -C . add SiagroB1.Application/Services/StorageTransactions/StorageTransactionsSplitService.cs
git -C . add SiagroB1.Application.Tests/StorageTransactions/StorageTransactionsSplitServiceTests.cs
```

---

### Task 3: O retorno passa a aceitar parcial em `Rebilling`

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReturnService.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesReturnServiceTests.cs`

**Interfaces:**
- Consumes: `StorageTransactionsSplitService.ExecuteAsync(...)` (Task 2).
- Produces: nenhum tipo público novo. O construtor de `SalesInvoicesReturnService` ganha um parâmetro `StorageTransactionsSplitService splitService` — **todo teste que constrói o serviço à mão precisa passá-lo**.

- [ ] **Step 1: Escrever os testes que falham**

Acrescente ao fim de `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesReturnServiceTests.cs`
(antes da chave final da classe):

```csharp
    // ─── Retorno PARCIAL POR QUANTIDADE com destino "novo destino" ───

    /// <summary>
    /// Parcial com novo destino divide a carreta: a origem encolhe e continua faturada na nota, e
    /// o irmão nasce livre para ser faturado a outro cliente.
    /// </summary>
    [Fact]
    public async Task A_partial_quantity_rebilling_return_splits_the_shipment()
    {
        var (invoice, r1, _) = await SeedAsync();

        var returnInvoice = await Service().ExecuteAsync(
            Request(invoice, [r1.Key], quantities: [15_000m]), "tester");

        var origin = await ShipmentAsync(r1.Key);

        Assert.Equal(5_000m, origin.NetWeight);
        Assert.Equal(5_000m, origin.InvoiceQty);
        Assert.Equal(StorageTransactionsStatus.Invoiced, origin.TransactionStatus);
        Assert.Equal(invoice.Key, origin.SalesInvoiceKey);

        var sibling = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.SplitFromStorageTransactionKey == r1.Key);

        Assert.Equal(15_000m, sibling.NetWeight);
        Assert.Equal(StorageTransactionsStatus.Confirmed, sibling.TransactionStatus);
        Assert.Null(sibling.SalesInvoiceKey);
        Assert.Equal(returnInvoice.Key, sibling.GeneratedByReturnInvoiceKey);
    }

    /// <summary>
    /// Linha CHEIA com novo destino segue o caminho de sempre: o romaneio inteiro volta ao pool e
    /// nenhum irmão é criado.
    /// </summary>
    [Fact]
    public async Task A_full_quantity_rebilling_return_frees_the_shipment_without_splitting()
    {
        var (invoice, r1, _) = await SeedAsync();

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key], quantities: [20_000m]), "tester");

        var freed = await ShipmentAsync(r1.Key);

        Assert.Equal(20_000m, freed.NetWeight);
        Assert.Equal(StorageTransactionsStatus.Confirmed, freed.TransactionStatus);
        Assert.Null(freed.SalesInvoiceKey);

        Assert.False(await _db.Context.StorageTransactions
            .AnyAsync(x => x.SplitFromStorageTransactionKey == r1.Key));
    }

    /// <summary>
    /// Um romaneio inteiro e outro pela metade no mesmo retorno: cada um segue a sua regra, e o
    /// documento de devolução leva a soma dos dois.
    /// </summary>
    [Fact]
    public async Task A_mixed_return_frees_one_shipment_and_splits_the_other()
    {
        var (invoice, r1, r2) = await SeedAsync();

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key, r2.Key], quantities: [20_000m, 8_000m]), "tester");

        var freed = await ShipmentAsync(r1.Key);
        Assert.Equal(StorageTransactionsStatus.Confirmed, freed.TransactionStatus);
        Assert.Null(freed.SalesInvoiceKey);

        var split = await ShipmentAsync(r2.Key);
        Assert.Equal(12_000m, split.NetWeight);
        Assert.Equal(StorageTransactionsStatus.Invoiced, split.TransactionStatus);
        Assert.Equal(invoice.Key, split.SalesInvoiceKey);

        var sibling = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.SplitFromStorageTransactionKey == r2.Key);

        Assert.Equal(8_000m, sibling.NetWeight);
    }

    /// <summary>
    /// ⚠️ Regressão do destino ARMAZÉM: ali o parcial NÃO divide nada — o romaneio de origem fica
    /// inteiro e Invoiced, e o volume devolvido entra como romaneio tipo 12.
    /// </summary>
    [Fact]
    public async Task A_partial_warehouse_return_still_does_not_split_the_shipment()
    {
        var (invoice, r1, _) = await SeedAsync();

        await Service().ExecuteAsync(
            Request(invoice, [r1.Key],
                destination: RefusalDestination.Warehouse,
                warehouseCode: DestinationWarehouse,
                quantities: [15_000m]),
            "tester");

        var origin = await ShipmentAsync(r1.Key);

        Assert.Equal(20_000m, origin.NetWeight);
        Assert.Equal(StorageTransactionsStatus.Invoiced, origin.TransactionStatus);

        Assert.False(await _db.Context.StorageTransactions
            .AnyAsync(x => x.SplitFromStorageTransactionKey == r1.Key));
    }
```

- [ ] **Step 2: Rodar os testes e confirmar que falham**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~SalesInvoicesReturnServiceTests"
```

Esperado: os 4 testes novos FALHAM — os três primeiros com a mensagem
"só pode ser devolvido em quantidade parcial quando a mercadoria retorna a um armazém"; o de
regressão do armazém já passa.

- [ ] **Step 3: Injetar o serviço de split**

Em `SalesInvoicesReturnService`, acrescente o parâmetro no construtor primário, depois de
`storageConfirm`:

```csharp
    StorageTransactionsSplitService splitService,
```

E no teste, em `SalesInvoicesReturnServiceTests.Service()`, passe-o na mesma posição:

```csharp
    private SalesInvoicesReturnService Service() =>
        new(_db,
            CreateService(),
            ConfirmService(),
            StorageCreate(),
            StorageConfirm(),
            new StorageTransactionsSplitService(_db, StorageCreate(), StorageConfirm()),
            new ShipmentReleasesFromReturnService(_db.Context),
            Warehouses(),
            NullLogger<SalesInvoicesReturnService>.Instance);
```

- [ ] **Step 4: Classificar a linha e remover a trava de destino**

Em `SalesInvoicesReturnService`, troque o record interno:

```csharp
    /// <summary>
    /// Um romaneio da nota, quanto dele voltou, e se isso é a carreta inteira. O parcial só muda
    /// de caminho no destino "novo destino", onde ele parte o romaneio em dois.
    /// </summary>
    private sealed record ResolvedShipment(
        StorageTransaction Shipment,
        decimal Quantity,
        bool IsPartial);
```

Em `ResolveShipments`, monte o record com a classificação:

```csharp
            var quantity = ResolveQuantity(shipment, line);

            resolved.Add(new ResolvedShipment(
                shipment,
                quantity,
                IsPartial: Math.Abs(quantity - shipment.NetWeight) > Tolerance));
```

E reescreva `ResolveQuantity`, que deixa de precisar do request inteiro:

```csharp
    /// <summary>Quanto voltou deste romaneio: o informado, ou a carreta inteira.</summary>
    /// <remarks>
    /// O teto por linha é o <c>NetWeight</c> do próprio romaneio. A trava que recusava quantidade
    /// parcial no destino "novo destino" saiu daqui: aquele destino agora sabe partir a carreta em
    /// dois registros (<c>StorageTransactionsSplitService</c>), que era exatamente o que faltava.
    /// </remarks>
    private static decimal ResolveQuantity(
        StorageTransaction shipment,
        SalesInvoiceReturnShipment line)
    {
        if (line.Quantity is not { } quantity)
            return shipment.NetWeight;

        if (quantity <= Tolerance)
        {
            throw new ApplicationException(
                $"Informe a quantidade a devolver do romaneio {shipment.Code}.");
        }

        if (quantity > shipment.NetWeight + Tolerance)
        {
            throw new ApplicationException(
                $"A quantidade a devolver do romaneio {shipment.Code} " +
                $"({quantity.ToString("N3", PtBr)}) é maior que o peso líquido dele " +
                $"({shipment.NetWeight.ToString("N3", PtBr)}).");
        }

        return decimal.Round(quantity, 3, MidpointRounding.ToEven);
    }
```

- [ ] **Step 5: Deixar a linha parcial fora do `shipmentOutcomes`**

Em `ExecuteAsync`, troque a montagem de `outcomes`:

```csharp
        // ⚠️ A linha PARCIAL de "novo destino" fica FORA dos outcomes de propósito. O bloco que
        // os aplica em SalesInvoicesConfirmService zera IsInvoiced e InvoiceQty e carimba
        // ReturnInvoiceKey em todo romaneio listado — o oposto do que a origem parcial precisa,
        // que é continuar faturada pelo que ficou com o cliente. Ela é tratada pelo split, depois
        // do confirm. No destino ARMAZÉM nada muda: lá o parcial não divide nada.
        var outcomes = shipments
            .Where(s => request.Destination == RefusalDestination.Warehouse || !s.IsPartial)
            .ToDictionary(
                s => s.Shipment.Key,
                _ => request.Destination == RefusalDestination.Warehouse
                    ? StorageTransactionsStatus.Invoiced
                    : StorageTransactionsStatus.Confirmed);
```

- [ ] **Step 6: Chamar o split depois do confirm**

Em `ExecuteAsync`, logo depois do `await db.SaveChangesAsync();` que segue
`confirmService.ExecuteAsync(...)` e ANTES do bloco `if (request.Destination == RefusalDestination.Warehouse)`:

```csharp
            // Cada linha parcial de "novo destino" parte a carreta: a origem fica com o que o
            // cliente reteve e o irmão leva o que segue no caminhão, livre para novo faturamento.
            if (request.Destination == RefusalDestination.Rebilling)
            {
                foreach (var line in shipments.Where(s => s.IsPartial))
                {
                    await splitService.ExecuteAsync(
                        line.Shipment, line.Quantity, returnInvoice.Key, userName);
                }
            }
```

- [ ] **Step 7: Atualizar o `<remarks>` da classe**

Substitua o parágrafo que começa com "⚠️ <b>Quantidade parcial só existe no destino
<c>Warehouse</c>.</b>" por:

```csharp
/// <para>
/// <b>Quantidade parcial nos DOIS destinos, por caminhos diferentes.</b> Em <c>Warehouse</c> o
/// romaneio de origem fica inteiro e <c>Invoiced</c>, e o volume devolvido entra como romaneio
/// tipo 12 no armazém escolhido. Em <c>Rebilling</c> a carreta é PARTIDA por
/// <c>StorageTransactionsSplitService</c>: a origem encolhe para o que ficou com o cliente e
/// nasce um irmão <c>Confirmed</c> e solto com o que segue no caminhão. Sem a divisão, devolver
/// meia carreta em "novo destino" des-faturaria o volume que o cliente recebeu.
/// </para>
```

- [ ] **Step 8: Rodar os testes e confirmar que passam**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~SalesInvoicesReturnServiceTests"
```

Esperado: todos PASSAM, inclusive os antigos (o parcial por ESCOLHA de romaneio, sem quantidade,
continua caindo em `IsPartial == false`, porque `ResolveQuantity` devolve o `NetWeight` cheio).

---

### Task 4: O estorno reconhece o retorno parcial puro

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReverseConfirmService.cs` (discriminador por volta da linha 59; `CancelWarehouseReturnAsync` por volta da linha 255)
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesReverseInvoiceReturnTests.cs`

**Interfaces:**
- Consumes: `StorageTransaction.SplitFromStorageTransactionKey` (Task 1); o comportamento de split de `SalesInvoicesReturnService` (Task 3).
- Produces: nada público. Prepara o terreno para o `UnsplitAsync` da Task 5.

- [ ] **Step 1: Passar o serviço de split para o `ReturnService()` do arquivo de testes**

`SalesInvoicesReverseInvoiceReturnTests.ReturnService()` constrói `SalesInvoicesReturnService` à
mão e não compila mais depois da Task 3. Insira o novo parâmetro **entre** o
`StorageTransactionsConfirmedService` e o `ShipmentReleasesFromReturnService`:

```csharp
            new StorageTransactionsSplitService(
                _db,
                new StorageTransactionsCreateService(
                    _db,
                    new FakeDocNumberSequenceService(),
                    Partners(),
                    Items(),
                    Warehouses(),
                    new ShipmentReleasesRecalculateShippedService(_db.Context),
                    new ShipmentReleaseMovementGuardService(_db.Context),
                    NullLogger<StorageTransactionsCreateService>.Instance),
                new StorageTransactionsConfirmedService(
                    _db,
                    new FakeStringLocalizer<Resource>(),
                    new ShipmentReleasesRecalculateShippedService(_db.Context),
                    new ShipmentReleaseMovementGuardService(_db.Context),
                    NullLogger<StorageTransactionsConfirmedService>.Instance)),
```

- [ ] **Step 2: Escrever os testes que falham**

⚠️ Neste arquivo o `SeedAsync()` devolve **duas** posições — `(SalesInvoice Invoice,
StorageTransaction R1)` — e a nota tem **um só** romaneio de `20.000`. Não existe helper `Request`
aqui: o pedido é montado inline. Os testes abaixo já seguem esse formato.

Acrescente a `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesReverseInvoiceReturnTests.cs`:

```csharp
    /// <summary>
    /// ⚠️ Num retorno 100% PARCIAL nenhum romaneio carrega ReturnInvoiceKey — nenhum entrou no
    /// shipmentOutcomes. Com o discriminador antigo o estorno cairia no ramo LEGADO, cuja consulta
    /// de órfãos (casada só por CardCode/ItemCode) já sequestrou romaneio alheio uma vez.
    /// </summary>
    [Fact]
    public async Task Reversing_a_purely_partial_return_does_not_fall_into_the_legacy_branch()
    {
        var (invoice, r1) = await SeedAsync();

        var returnInvoice = await ReturnService().ExecuteAsync(
            new SalesInvoiceReturnRequest(
                invoice.Key,
                [new SalesInvoiceReturnShipment(r1.Key, 15_000m)],
                RefusalDestination.Rebilling, null, "Recusa parcial"),
            "tester");

        // Romaneio solto de OUTRO carregamento, mesmo cliente e mesmo produto: é exatamente o que
        // a consulta de órfãos do ramo legado sequestraria.
        var stranger = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "ALHEIO",
            CardCode = CardCode,
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse,
            BranchCode = "01",
            GrossWeight = 30_000m,
            NetWeight = 30_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };

        _db.Context.StorageTransactions.Add(stranger);
        await _db.SaveChangesAsync();

        await ReverseService().ExecuteAsync(returnInvoice.Key, "tester");

        var untouched = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == stranger.Key);

        Assert.Null(untouched.SalesInvoiceKey);
        Assert.Equal(StorageTransactionsStatus.Confirmed, untouched.TransactionStatus);
    }

    /// <summary>
    /// O irmão do split usa a MESMA coluna que a devolução ao armazém
    /// (GeneratedByReturnInvoiceKey). Sem filtrar por tipo, o estorno o cancelaria como se fosse
    /// uma entrada de armazém — e a origem ficaria encolhida para sempre.
    /// </summary>
    [Fact]
    public async Task Reversing_does_not_cancel_the_split_sibling_as_a_warehouse_return()
    {
        var (invoice, r1) = await SeedAsync();

        var returnInvoice = await ReturnService().ExecuteAsync(
            new SalesInvoiceReturnRequest(
                invoice.Key,
                [new SalesInvoiceReturnShipment(r1.Key, 15_000m)],
                RefusalDestination.Rebilling, null, "Recusa parcial"),
            "tester");

        var siblingKey = await _db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.SplitFromStorageTransactionKey == r1.Key)
            .Select(x => x.Key)
            .SingleAsync();

        await ReverseService().ExecuteAsync(returnInvoice.Key, "tester");

        var sibling = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == siblingKey);

        Assert.Equal(StorageTransactionType.SalesShipment, sibling.TransactionType);
    }
```

- [ ] **Step 3: Rodar os testes e confirmar que falham**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~SalesInvoicesReverseInvoiceReturnTests"
```

Esperado: FALHAM — o primeiro porque o ramo legado sequestra o romaneio alheio; o segundo porque o
irmão é cancelado pela busca sem filtro de tipo.

- [ ] **Step 4: Corrigir o discriminador `isNewFlow`**

Em `SalesInvoicesReverseConfirmService`, por volta da linha 59:

```csharp
                // ⚠️ As DUAS colunas, e não só ReturnInvoiceKey. Num retorno 100% parcial com
                // destino "novo destino" nenhum romaneio de origem recebe ReturnInvoiceKey — a
                // origem não foi devolvida, foi DIVIDIDA —, e quem marca o retorno é o irmão do
                // split, por GeneratedByReturnInvoiceKey. Sem a segunda condição esse retorno cai
                // no ramo legado, cuja consulta de órfãos sequestra romaneio alheio.
                var isNewFlow = await db.Context.StorageTransactions
                    .AnyAsync(x =>
                        x.ReturnInvoiceKey == invoice.Key ||
                        x.GeneratedByReturnInvoiceKey == invoice.Key);
```

- [ ] **Step 5: Escopar o cancelamento da devolução ao armazém**

Em `CancelWarehouseReturnAsync`:

```csharp
        var entries = await db.Context.StorageTransactions
            .Where(x => x.GeneratedByReturnInvoiceKey == returnInvoice.Key &&
                        // ⚠️ Filtro de TIPO obrigatório: o irmão de um split usa a mesma coluna e
                        // é um SalesShipment. Sem isto ele seria cancelado como se fosse a entrada
                        // de armazém, e a origem ficaria encolhida sem nada que a completasse.
                        x.TransactionType == StorageTransactionType.SalesShipmentReturn &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .ToListAsync();
```

- [ ] **Step 6: Rodar os testes e confirmar que passam**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~SalesInvoicesReverseInvoiceReturnTests"
```

Esperado: PASSAM, e nenhum teste antigo do arquivo quebra.

---

### Task 5: O estorno desfaz a divisão

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReverseConfirmService.cs` (`ReverseNewReturnAsync`, por volta da linha 160)
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesReverseInvoiceReturnTests.cs`

**Interfaces:**
- Consumes: `StorageTransaction.SplitFromStorageTransactionKey`; o comportamento das Tasks 3 e 4.
- Produces: nada público.

- [ ] **Step 1: Escrever os testes que falham**

Acrescente ao mesmo arquivo de testes:

```csharp
    /// <summary>
    /// Com o irmão ainda livre, o estorno funde os pesos de volta na origem e cancela o irmão —
    /// cancelar, e não apagar: o Code já saiu da sequência e apagar apaga a auditoria da divisão.
    /// </summary>
    [Fact]
    public async Task Reversing_merges_a_free_sibling_back_into_the_origin()
    {
        var (invoice, r1) = await SeedAsync();

        var returnInvoice = await ReturnService().ExecuteAsync(
            new SalesInvoiceReturnRequest(
                invoice.Key,
                [new SalesInvoiceReturnShipment(r1.Key, 15_000m)],
                RefusalDestination.Rebilling, null, "Recusa parcial"),
            "tester");

        await ReverseService().ExecuteAsync(returnInvoice.Key, "tester");

        var origin = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == r1.Key);

        Assert.Equal(20_000m, origin.NetWeight);
        Assert.Equal(20_000m, origin.GrossWeight);
        Assert.Equal(20_000m, origin.InvoiceQty);

        var sibling = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.SplitFromStorageTransactionKey == r1.Key);

        Assert.Equal(StorageTransactionsStatus.Cancelled, sibling.TransactionStatus);
    }

    /// <summary>
    /// Irmão já faturado noutra nota: o estorno é recusado inteiro, nomeando o documento. Fundi-lo
    /// de volta deixaria o mesmo volume faturado em duas notas.
    /// </summary>
    [Fact]
    public async Task Reversing_is_refused_when_the_sibling_was_already_invoiced()
    {
        var (invoice, r1) = await SeedAsync();

        var returnInvoice = await ReturnService().ExecuteAsync(
            new SalesInvoiceReturnRequest(
                invoice.Key,
                [new SalesInvoiceReturnShipment(r1.Key, 15_000m)],
                RefusalDestination.Rebilling, null, "Recusa parcial"),
            "tester");

        var sibling = await _db.Context.StorageTransactions
            .SingleAsync(x => x.SplitFromStorageTransactionKey == r1.Key);

        var otherInvoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = CardCode,
            CardName = "CLIENTE TESTE",
            BranchCode = "01",
            InvoiceNumber = "000999",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
        };

        _db.Context.SalesInvoices.Add(otherInvoice);
        sibling.SalesInvoiceKey = otherInvoice.Key;
        await _db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => ReverseService().ExecuteAsync(returnInvoice.Key, "tester"));

        Assert.Contains("000999", error.Message);

        // Nada gravado: a origem continua encolhida, como estava antes da tentativa.
        var origin = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == r1.Key);

        Assert.Equal(5_000m, origin.NetWeight);
    }

    /// <summary>
    /// Irmão já montado numa carga: mesma recusa, nomeando a carga. Arrancá-lo de lá mudaria o
    /// volume de uma carga possivelmente já faturada em parte.
    /// </summary>
    [Fact]
    public async Task Reversing_is_refused_when_the_sibling_is_attached_to_a_load()
    {
        var (invoice, r1) = await SeedAsync();

        var returnInvoice = await ReturnService().ExecuteAsync(
            new SalesInvoiceReturnRequest(
                invoice.Key,
                [new SalesInvoiceReturnShipment(r1.Key, 15_000m)],
                RefusalDestination.Rebilling, null, "Recusa parcial"),
            "tester");

        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000009",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
        };

        var sibling = await _db.Context.StorageTransactions
            .SingleAsync(x => x.SplitFromStorageTransactionKey == r1.Key);

        _db.Context.ShipmentLoads.Add(load);
        sibling.ShipmentLoadKey = load.Key;
        await _db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => ReverseService().ExecuteAsync(returnInvoice.Key, "tester"));

        Assert.Contains("CG000009", error.Message);
    }
```

- [ ] **Step 2: Rodar os testes e confirmar que falham**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~SalesInvoicesReverseInvoiceReturnTests"
```

Esperado: os 3 FALHAM — nada desfaz a divisão hoje.

- [ ] **Step 3: Chamar o passo de desfazer a divisão**

Em `ReverseNewReturnAsync`, como PRIMEIRA linha do método (antes de qualquer leitura ou escrita):

```csharp
        // ANTES de qualquer escrita: se um irmão não puder mais ser fundido de volta, o estorno é
        // recusado inteiro e nada é gravado.
        await UnsplitAsync(returnInvoice, userName);
```

- [ ] **Step 4: Implementar o desfazer**

Acrescente a `SalesInvoicesReverseConfirmService`, logo depois de `ReverseNewReturnAsync`:

```csharp
    /// <summary>
    /// Funde de volta na origem os romaneios irmãos que este retorno criou ao partir uma carreta,
    /// e recusa o estorno inteiro quando algum deles já saiu do lugar.
    /// </summary>
    /// <remarks>
    /// A divisão só é desfeita enquanto o irmão continua livre: assim que ele é faturado ou
    /// montado numa carga, devolver o peso à origem deixaria o mesmo volume contado duas vezes.
    /// <para>
    /// O laço que já existe em <see cref="ReverseNewReturnAsync"/> — o que re-anexa à nota de
    /// origem os romaneios com <c>ReturnInvoiceKey</c> deste retorno — <b>não tem nada a fazer numa
    /// linha parcial</b>, e é isso que se espera: a origem nunca foi solta da nota nem carimbada
    /// com a chave do retorno. Num retorno 100% parcial aquele laço roda vazio e todo o trabalho
    /// está aqui.
    /// </para>
    /// <para>
    /// O irmão é CANCELADO, e não apagado: o <c>Code</c> já saiu da sequência de numeração e
    /// apagar o registro apaga a auditoria da divisão. <c>Cancelled</c> já está fora de toda
    /// consulta de saldo, então a origem restaurada volta a valer sozinha.
    /// </para>
    /// </remarks>
    private async Task UnsplitAsync(SalesInvoice returnInvoice, string userName)
    {
        var siblings = await db.Context.StorageTransactions
            .Where(x => x.GeneratedByReturnInvoiceKey == returnInvoice.Key &&
                        x.SplitFromStorageTransactionKey != null &&
                        x.TransactionType == StorageTransactionType.SalesShipment &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .ToListAsync();

        if (siblings.Count == 0)
            return;

        // Duas passadas: TODAS as recusas antes de qualquer escrita. Um retorno que dividiu dois
        // romaneios não pode fundir o primeiro e estourar no segundo.
        foreach (var sibling in siblings)
            await EnsureSiblingCanBeMergedBackAsync(sibling);

        foreach (var sibling in siblings)
        {
            var origin = await db.Context.StorageTransactions
                             .FirstOrDefaultAsync(
                                 x => x.Key == sibling.SplitFromStorageTransactionKey!.Value)
                         ?? throw new ApplicationException(
                             $"O romaneio de origem da divisão do {sibling.Code} não foi " +
                             "encontrado; o estorno não pode desfazer a divisão.");

            origin.GrossWeight += sibling.GrossWeight;
            origin.NetWeight += sibling.NetWeight;
            origin.DryingDiscount += sibling.DryingDiscount;
            origin.CleaningDiscount += sibling.CleaningDiscount;
            origin.OthersDicount += sibling.OthersDicount;
            origin.InvoiceQty += sibling.NetWeight;
            origin.UpdatedAt = DateTime.Now;
            origin.UpdatedBy = userName;

            sibling.TransactionStatus = StorageTransactionsStatus.Cancelled;
            sibling.UpdatedAt = DateTime.Now;
            sibling.UpdatedBy = userName;
        }
    }

    /// <summary>
    /// Recusa o estorno quando o irmão da divisão já foi usado, com a mensagem que diz ao operador
    /// exatamente onde ele está.
    /// </summary>
    private async Task EnsureSiblingCanBeMergedBackAsync(StorageTransaction sibling)
    {
        if (sibling.SalesInvoiceKey is { } invoiceKey)
        {
            var number = await db.Context.SalesInvoices
                .Where(x => x.Key == invoiceKey)
                .Select(x => x.InvoiceNumber)
                .FirstOrDefaultAsync();

            throw new ApplicationException(
                $"O romaneio {sibling.Code}, nascido da divisão deste retorno, já foi faturado no " +
                $"documento de saída {number}. Cancele aquele faturamento antes de estornar o retorno.");
        }

        if (sibling.ShipmentLoadKey is { } loadKey)
        {
            var code = await db.Context.ShipmentLoads
                .Where(x => x.Key == loadKey)
                .Select(x => x.Code)
                .FirstOrDefaultAsync();

            throw new ApplicationException(
                $"O romaneio {sibling.Code}, nascido da divisão deste retorno, está montado na " +
                $"carga {code}. Desvincule-o da carga antes de estornar o retorno.");
        }

        if (sibling.TransactionStatus != StorageTransactionsStatus.Confirmed)
        {
            throw new ApplicationException(
                $"O romaneio {sibling.Code}, nascido da divisão deste retorno, está em situação " +
                $"{sibling.TransactionStatus} e já foi movimentado. Desfaça a movimentação antes " +
                "de estornar o retorno.");
        }

        var splitAgain = await db.Context.StorageTransactions
            .AnyAsync(x => x.SplitFromStorageTransactionKey == sibling.Key &&
                           x.TransactionStatus != StorageTransactionsStatus.Cancelled);

        if (splitAgain)
        {
            throw new ApplicationException(
                $"O romaneio {sibling.Code}, nascido da divisão deste retorno, já foi dividido de " +
                "novo por outro retorno. Estorne o retorno mais recente antes deste.");
        }
    }
```

- [ ] **Step 5: Rodar os testes e confirmar que passam**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~SalesInvoicesReverseInvoiceReturnTests"
```

Esperado: PASSAM.

- [ ] **Step 6: Rodar a suíte inteira do backend**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj
```

Esperado: verde. Se algum teste de outro módulo quebrar, o culpado provável é o construtor novo de
`SalesInvoicesReturnService` (Task 3) — procure por outros lugares que o instanciam à mão.

---

### Task 6: Guard no estorno da Expedição de Grãos

**Files:**
- Modify: `SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsReverseService.cs` (depois do guard de romaneio `Returned`, por volta da linha 55)
- Test: `SiagroB1.Application.Tests/ShippingTransactions/ShippingTransactionsReverseServiceTests.cs`

**Interfaces:**
- Consumes: `StorageTransaction.SplitFromStorageTransactionKey` (Task 1).
- Produces: nada público.

- [ ] **Step 1: Escrever o teste que falha**

Acrescente a `SiagroB1.Application.Tests/ShippingTransactions/ShippingTransactionsReverseServiceTests.cs`.
⚠️ Neste arquivo o `SeedAsync()` devolve `(PurchaseContract Contract, ShipmentRelease Release)`, o
romaneio de compra vem de `NewPurchase(releaseKey, grossWeight)` e os nomes dos testes são em
português — siga esse estilo local:

```csharp
    /// <summary>
    /// Depois de uma divisão o par compra/venda deixa de bater 1:1 — o ShippingTransaction cobre
    /// 40.000 e a perna de venda vale 15.000. Estornar por aqui devolveria contrato de compra e
    /// saldo de armazém pelo valor errado, em silêncio.
    /// </summary>
    [Fact]
    public async Task Execute_ComRomaneioDividido_Recusa()
    {
        var (contract, release) = await SeedAsync();
        var purchase = NewPurchase(release.Key, 1000m);
        var shipping = await CreateService().ExecuteAsync(contract.Key, purchase, "tester");

        var salesKey = shipping.SalesStorageTransactionKey;

        // O irmão que uma divisão por retorno parcial teria criado a partir da perna de venda.
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1-B",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            GrossWeight = 400m,
            NetWeight = 400m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            SplitFromStorageTransactionKey = salesKey,
        });

        await _db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => ReverseService().ExecuteAsync(salesKey, "tester"));

        Assert.Contains("R1-B", error.Message);
    }
```

- [ ] **Step 2: Rodar o teste e confirmar que falha**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~ShippingTransactionsReverseServiceTests"
```

Esperado: FALHA — o estorno passa reto.

- [ ] **Step 3: Implementar o guard**

Em `ShippingTransactionsReverseService.ExecuteAsync`, logo depois do bloco que recusa romaneio
`Returned`:

```csharp
        // Romaneio DIVIDIDO por um retorno parcial: depois do split o par compra/venda deixa de
        // bater 1:1 — o ShippingTransaction cobre o peso original e a perna de venda vale só o que
        // ficou com o cliente. Estornar por aqui devolveria ao contrato de compra e ao armazém um
        // volume que não corresponde a nada. O caminho certo é estornar o RETORNO, que sabe fundir
        // o irmão de volta antes de desfazer qualquer coisa.
        if (shipping.SalesStorageTransaction is { } salesLeg)
        {
            var siblingCode = await db.Context.StorageTransactions
                .Where(x => x.SplitFromStorageTransactionKey == salesLeg.Key &&
                            x.TransactionStatus != StorageTransactionsStatus.Cancelled)
                .Select(x => x.Code)
                .FirstOrDefaultAsync();

            if (siblingCode != null)
            {
                throw new ApplicationException(
                    $"O romaneio {salesLeg.Code} foi dividido por um retorno parcial e deu origem " +
                    $"ao romaneio {siblingCode}. Estorne o documento de retorno para desfazer a " +
                    "divisão antes de estornar o embarque.");
            }
        }
```

- [ ] **Step 4: Rodar o teste e confirmar que passa**

```bash
dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~ShippingTransactionsReverseServiceTests"
```

Esperado: PASSA.

---

### Task 7: Tela — quantidade editável nos dois destinos

**Files:**
- Modify: `siagro-b1-frontend/webapp/view/salesInvoices/fragments/Return.fragment.xml`
- Modify: `siagro-b1-frontend/webapp/controller/salesInvoices/Main.controller.ts`

**Interfaces:**
- Consumes: a action OData `SalesInvoicesReturn` com `Quantities` (`Collection(Edm.Double)`, opcional, paralelo a `StorageTransactionKeys`) — já existe no EDM, nada a mudar no backend.
- Produces: nada.

⚠️ Não há gate de teste automatizado útil aqui (`yarn test` nunca passa neste projeto). Os gates são
`yarn ts-typecheck` e `yarn lint`, e a prova real é a verificação no navegador da Task 8.

- [ ] **Step 1: Liberar a coluna no fragmento**

Em `Return.fragment.xml`, substitua o comentário acima da `t:Table` por:

```xml
			<!-- A escolha é por ROMANEIO; a quantidade é opcional dentro dele. Sem ajuste, cada
			     romaneio volta inteiro. Quantidade parcial vale nos DOIS destinos: no armazém o
			     volume devolvido entra como romaneio tipo 12, e em "novo destino" a carreta é
			     PARTIDA — a origem fica com o que o cliente reteve e nasce um romaneio novo com o
			     que segue no caminhão. -->
```

Troque o texto que alternava por destino:

```xml
						<Text text="Selecione os romaneios e ajuste quanto voltou de cada um."/>
```

E a coluna de quantidade:

```xml
					<t:Column label="Qtde a Devolver" hAlign="End" width="10rem">
						<t:template>
							<Input
								textAlign="End"
								change=".updateReturnTotal"
								value="{ path: 'returnShipments>ReturnQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
						</t:template>
					</t:Column>
```

- [ ] **Step 2: Atualizar o comentário do tipo no controller**

Em `Main.controller.ts`, no `interface ReturnableShipment`:

```ts
  /**
   * Quanto deste romaneio volta. Nasce igual ao `NetWeight` — a carreta inteira — e é editável
   * nos dois destinos: no armazém o volume vira romaneio tipo 12; em "novo destino" o romaneio é
   * partido em dois.
   */
  ReturnQuantity: number;
```

- [ ] **Step 3: Parar de devolver as quantidades ao trocar de destino**

```ts
  /**
   * Trocar para "segue para novo destino" limpa o armazém: deixá-lo preenchido e invisível
   * mandaria um código de armazém junto de um retorno que não devolve nada a armazém nenhum.
   *
   * As quantidades digitadas ficam: os dois destinos aceitam parcial, cada um do seu jeito.
   */
  onReturnDestinationChange(): void {
    const form = this.getModel("return") as JSONModel;

    if (form.getProperty("/DestinationIndex") !== 1) {
      form.setProperty("/DestinationWarehouseCode", "");
      form.setProperty("/DestinationWarehouseName", "");
    }

    this.updateReturnTotal();
  }
```

- [ ] **Step 4: Validar, confirmar e enviar a quantidade nos dois destinos**

Em `onConfirmReturn`, substitua o bloco que vai de `// A quantidade só existe no destino armazém`
até a chamada de `DialogHelper.confirmDialog` por:

```ts
      // Number() explícito porque o Input escreve string no JSONModel quando o usuário digita.
      const invalid = chosen.find(s => {
        const quantity = Number(s.ReturnQuantity);
        return !(quantity > 0) || quantity > s.NetWeight;
      });

      if (invalid) {
        MessageBox.warning(
          `Informe uma quantidade a devolver entre 0 e ${invalid.NetWeight} ` +
          `para o romaneio ${invalid.Code}.`);
        return;
      }

      const total = chosen.reduce((sum, s) => sum + Number(s.ReturnQuantity), 0);

      // Romaneios que voltam pela metade em "novo destino" serão PARTIDOS em dois, e a divisão
      // não tem mais volta depois que o romaneio novo for usado. O operador precisa ler isso.
      const splits = toWarehouse
        ? []
        : chosen.filter(s => Number(s.ReturnQuantity) < s.NetWeight);

      const splitMessage = splits
        .map(s =>
          `O romaneio ${s.Code} será dividido: ` +
          `${formatter.formatDecimal(s.NetWeight - Number(s.ReturnQuantity), 3)} seguem faturados ` +
          `neste documento e ${formatter.formatDecimal(Number(s.ReturnQuantity), 3)} voltam a ` +
          "ficar disponíveis num romaneio novo.")
        .join(" ");

      const confirmed = await DialogHelper.confirmDialog(
        toWarehouse
          ? `Confirma o retorno de ${formatter.formatDecimal(total, 3)}, devolvendo a ` +
            "mercadoria ao armazém informado ?"
          : splits.length > 0
            ? `Confirma o retorno ? ${splitMessage}`
            : "Confirma o retorno ? Os romaneios voltarão a ficar disponíveis para faturamento.");
```

E o parâmetro da action:

```ts
      // Array PARALELO ao de chaves, agora sempre enviado: os dois destinos aceitam parcial.
      // ⚠️ Números crus — o EDM declara Collection(Edm.Double). Edm.Decimal o UI5 serializaria
      // como string, e o backend recusa com um 400 que não nomeia o campo.
      action.setParameter("Quantities", chosen.map(s => Number(s.ReturnQuantity)));
```

- [ ] **Step 5: Rodar os gates do frontend**

```bash
cd ../siagro-b1-frontend
yarn ts-typecheck
yarn lint
```

Esperado: ambos limpos. **Não rode `yarn test`.**

---

### Task 8: Verificação no navegador

**Files:** nenhum. Esta task é a prova de que a feature existe pelo caminho do usuário — nenhum gate
anterior exercita OData, binding ou o EDM.

- [ ] **Step 1: Subir a stack**

Backend: rodar `SiagroB1.Web` e `SiagroB1.Gateway` **no mesmo perfil `yktb`** (ambiente `Yokotobi`).
O perfil `dev` aponta um CommonDB atrás nas migrations e o login estoura "Erro interno no servidor".

Frontend: `yarn start:dev` em `siagro-b1-frontend`. Login `admin` / `1234`.

- [ ] **Step 2: Retorno parcial com novo destino**

Em `/sales-invoices`, num documento de saída LEGADO confirmado (com romaneios próprios, sem carga),
clicar **Retornar**, escolher "Caminhão segue para novo destino", selecionar um romaneio, **digitar
uma quantidade menor que o peso líquido** e confirmar.

Conferir:
- o campo de quantidade **aceita digitação** (era o defeito relatado);
- o diálogo de confirmação anuncia a divisão com os dois números;
- depois de confirmar, o romaneio de origem aparece com o peso reduzido e ainda faturado;
- um romaneio NOVO existe com a quantidade devolvida, e ele aparece no **Faturamento de Expedição**
  e na **Montagem de Carga**.

- [ ] **Step 3: Estorno com o irmão livre**

Estornar o documento de retorno recém-criado. Conferir que o romaneio de origem volta ao peso cheio
e que o romaneio novo fica `Cancelado`.

- [ ] **Step 4: Estorno com o irmão já usado**

Repetir o retorno parcial, faturar o romaneio novo em outro documento de saída e então tentar
estornar o retorno. Conferir que a recusa nomeia o documento onde o romaneio foi faturado, e que
nada mudou no banco depois da recusa.

- [ ] **Step 5: Recusa de carga com novo destino e quantidade parcial**

Em `/shipment-loads`, abrir uma carga faturada, clicar em recusar, escolher "Caminhão segue para
novo destino" e digitar uma quantidade parcial num documento.

Pelo código o campo é editável e o serviço não tem trava de destino — o esperado é que funcione.
**Se estiver travado na prática, é bug**: anotar o comportamento observado e tratá-lo como correção
de escopo próprio, seguindo `superpowers:systematic-debugging`.

- [ ] **Step 6: Anotar a sujeira deixada no banco**

Registrar quais documentos e romaneios do `IDX_SIAGRO_DEV` ficaram em estado de teste, para que o
usuário saiba o que ignorar.

---

## Notas de encerramento

- Antes de declarar a feature pronta, use `superpowers:verification-before-completion`: rode a
  suíte completa do backend e os dois gates do frontend, e relate a saída real, não a expectativa.
- Todo arquivo novo tem de estar staged (`git add`) no repo certo. Nada de `git commit` ou
  `git push`.
