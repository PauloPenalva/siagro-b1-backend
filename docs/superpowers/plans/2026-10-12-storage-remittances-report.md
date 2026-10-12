# Relatório "Remessas para Depósito" — Plano de Implementação (backend)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PDF consolidado das remessas para depósito em armazém de terceiro (abertas e já retornadas), com Remetido / Retornado / A retornar / Expedido sem retorno e detalhe opcional de retornos e romaneios.

**Architecture:** Consultas próprias no `SiagroB1.Reports` (`Helpers/StorageRemittanceData.cs`), que espelham `StorageDepositRules` da Application (o Reports não referencia a Application). O serviço `StorageRemittancesByPeriodReportService` monta linhas achatadas (remessa + filhas) para um `.frx` com uma `DataBand` e um `GroupHeaderBand` por produto/UM. Um teste cruzado compara cada remessa com `ShipmentReleasesStorageRemittanceSummaryService`, que alimenta a tela da liberação.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), FastReport.OpenSource, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-12-storage-remittances-report-design.md`

**Frontend:** plano irmão em `siagro-b1-frontend/docs/superpowers/plans/2026-10-12-storage-remittances-report.md` (executar DEPOIS deste).

## Global Constraints

- Só STANDALONE: fora dele o endpoint devolve 400 "Disponível somente no modo STANDALONE.".
- Identificadores em inglês; texto que o usuário lê em pt-BR.
- Remessa = `SalesInvoice` com `ShipmentReleaseKey != null` e `InvoiceStatus != Cancelled`; retorno = linha de entrada não cancelada com `SalesInvoiceItemKey` da remessa; romaneio pelo eixo de `StorageDepositRules.ShippedTransactions`.
- Período sobre `SalesInvoice.InvoiceDate`, fim inclusivo; Retornado/A retornar = posição atual.
- Situação: Open = A retornar > 0; Returned = A retornar ≤ 0; padrão All.
- Larguras do `.frx`: `(chars×5,2+4)×tam/7+4` px, Consolas 7pt, totais 6pt, página 1084 px; `SortOrder="None"` nos grupos.
- Menu `storageRemittancesReport`, `ParentKey = reports`, `Order = 19`, `StandaloneOnly = true`, ROLE_MENUS ADMIN.
- Branch `feature/storage-remittances-report`; conferir o branch antes de cada commit; nunca push.
- Commits: `tipo(escopo): descrição pt-BR`; escopo `reports`; commit com migration leva trailer `DB: <NomeDaMigration>`; terminar com as linhas `Co-Authored-By`/`Claude-Session` da sessão.

## Review Focus

- Retorno com quantidade MAIOR que a remessa: A retornar negativo, conta como "Totalmente retornada" e o texto `-20,000` cabe na coluna (Task 1 — teste de situação; Task 3 — largura 15 car.).
- Remessa Pendente (NF-e rejeitada) aparece com "Pendente" e "Rejeitada"; cancelada some (Task 1).
- Romaneio de Conferência (`TransactionOrigin = WarehouseReconciliation`) e Saída(7) em liberação Standard não contam; em liberação de transferência só Saída(7) conta (Task 1).
- Retorno lançado DEPOIS do período ainda abate o A retornar; remessa às 23:00 do último dia entra (Task 1).
- Remessa editada para duas linhas soma as duas, sem 500 (Task 2 — teste cruzado, remessa C).

---

## File Structure

| Arquivo | Responsabilidade |
|---|---|
| Create `SiagroB1.Reports/Dtos/StorageRemittancesByPeriodRequest.cs` | Filtros + enum `StorageRemittanceSituation`. |
| Create `SiagroB1.Reports/Dtos/StorageRemittanceRowDto.cs` | Linha do PDF + constantes de tipo de linha. |
| Create `SiagroB1.Reports/Helpers/StorageRemittanceData.cs` | IQueryables espelho de `StorageDepositRules` + regra do romaneio por origem. |
| Create `SiagroB1.Reports/Services/StorageRemittancesByPeriodReportService.cs` | Posição por remessa, filtro de situação, ordem, linhas, PDF. |
| Create `SiagroB1.Reports/Controllers/StorageRemittancesByPeriodController.cs` | POST, guarda de STANDALONE e de período. |
| Create `SiagroB1.Reports/Reports/Templates/StorageRemittancesByPeriod.frx` | Template paisagem. |
| Create `SiagroB1.Migrations/CommonContext/<ts>_AddStorageRemittancesReportMenu.cs` (+ Designer gerado) | Menu. |
| Modify `SiagroB1.Application.Tests/Support/StorageDepositSeed.cs` | Parâmetros opcionais: liberação extra, data/número da remessa e do retorno, data/código do romaneio. |
| Create `SiagroB1.Application.Tests/Reports/StorageRemittancesByPeriodReportServiceTests.cs` | Regras, detalhe, cruzado, tradução, parâmetros. |
| Create `SiagroB1.Application.Tests/Reports/StorageRemittancesByPeriodControllerTests.cs` | 400s e PDF. |
| Modify `SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs` | Inclui o template novo. |
| Modify `SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs` | PDF de ponta a ponta. |

Os services do Reports são registrados por varredura (Scrutor, `DI/ServiceCollectionExtensions.cs`, sufixo `Service`) — nada a registrar à mão. O `.csproj` dos testes já copia `*.frx` para `ReportTemplates/` (link por wildcard).

---

### Task 1: Consultas, posição por remessa e linhas de resumo

**Files:**
- Modify: `SiagroB1.Application.Tests/Support/StorageDepositSeed.cs`
- Create: `SiagroB1.Reports/Dtos/StorageRemittancesByPeriodRequest.cs`
- Create: `SiagroB1.Reports/Dtos/StorageRemittanceRowDto.cs`
- Create: `SiagroB1.Reports/Helpers/StorageRemittanceData.cs`
- Create: `SiagroB1.Reports/Services/StorageRemittancesByPeriodReportService.cs`
- Test: `SiagroB1.Application.Tests/Reports/StorageRemittancesByPeriodReportServiceTests.cs`

**Interfaces:**
- Produces: `StorageRemittancesByPeriodRequest : LogisticsReportRequest` com `string? CardCode`, `StorageRemittanceSituation ReturnSituation`, `bool Detailed`; `enum StorageRemittanceSituation { All = 0, Open = 1, Returned = 2 }`; `StorageRemittanceRowDto` (campos abaixo); `static class StorageRemittanceRowKind { Remittance, Return, Transaction }` (strings); `StorageRemittanceData.RemittancesQuery/ReleaseKeys/RemittanceItemKeys/ReturnLinesQuery/TransactionsQuery/IsShipped`; `StorageRemittancesByPeriodReportService(IUnitOfWork db, IFastReportService reportService)` com `Task<List<StorageRemittanceRowDto>> BuildRowsAsync(StorageRemittancesByPeriodRequest)`.
- Seed: `AddReleaseAsync(ReleaseOrigin origin = Standard)`, `AddRemittanceAsync(decimal, InvoiceStatus = Confirmed, ShipmentRelease? release = null, DateTime? date = null, string number = "000000123")`, `AddReturnAsync(SalesInvoice, decimal, InvoiceStatus = Pending, DateTime? date = null, string number = "000000456")`, `AddShipmentAsync(decimal, StorageTransactionType = Purchase, TransactionCode? origin = null, ShipmentRelease? release = null, DateTime? date = null, string? code = null)`.

- [ ] **Step 1: Estender o seed (parâmetros opcionais no FIM, chamadas atuais continuam valendo)**

Em `StorageDepositSeed.cs`, substituir `AddRemittanceAsync`, `AddReturnAsync` e `AddShipmentAsync` e acrescentar `AddReleaseAsync`:

```csharp
    /// <summary>Outra liberação no mesmo contrato e armazém (uma remessa ativa por liberação).</summary>
    public async Task<ShipmentRelease> AddReleaseAsync(ReleaseOrigin origin = ReleaseOrigin.Standard, decimal released = 100_000m)
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(), BranchCode = Branch, PurchaseContractKey = Contract.Key, ReleasedQuantity = released,
            DeliveryLocationCode = Warehouse, DeliveryLocationName = "ARMAZEM TERCEIRO", Status = ReleaseStatus.Actived,
            Origin = origin,
        };
        _db.Context.ShipmentReleases.Add(release);
        await _db.SaveChangesAsync();
        return release;
    }

    public async Task<SalesInvoice> AddRemittanceAsync(
        decimal quantity, InvoiceStatus status = InvoiceStatus.Confirmed, ShipmentRelease? release = null,
        DateTime? date = null, string number = "000000123")
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = Branch, CardCode = Warehouse, InvoiceNumber = "S" + Guid.NewGuid().ToString("N")[..6],
            TaxDocumentNumber = number, InvoiceStatus = status, ShipmentReleaseKey = (release ?? Release).Key,
            InvoiceDate = date ?? DateTime.Now.Date,
            Items = [new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 2m, UsageCode = RemittanceUsageCode }],
        };
        _db.Context.SalesInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    public async Task<PurchaseInvoice> AddReturnAsync(
        SalesInvoice remittance, decimal quantity, InvoiceStatus status = InvoiceStatus.Pending,
        DateTime? date = null, string number = "000000456")
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = Branch, CardCode = Warehouse, InvoiceNumber = "E" + Guid.NewGuid().ToString("N")[..6],
            TaxDocumentNumber = number, InvoiceStatus = status, IssueDate = date ?? DateTime.Now.Date,
            Items = [new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 2m,
                UsageCode = ReturnUsageCode, SalesInvoiceItemKey = remittance.Items.First().Key,
            }],
        };
        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    public async Task<StorageTransaction> AddShipmentAsync(
        decimal netWeight, StorageTransactionType type = StorageTransactionType.Purchase, TransactionCode? origin = null,
        ShipmentRelease? release = null, DateTime? date = null, string? code = null)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code ?? "R" + Guid.NewGuid().ToString("N")[..6], BranchCode = Branch,
            CardCode = Supplier, ItemCode = Item, UnitOfMeasureCode = "KG", WarehouseCode = Warehouse,
            TransactionType = type, TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = netWeight, GrossWeight = netWeight, ShipmentReleaseKey = (release ?? Release).Key, TransactionOrigin = origin,
            TruckCode = "ABC1D23", TransactionDate = date ?? DateTime.Now.Date,
        };
        _db.Context.StorageTransactions.Add(transaction);
        await _db.SaveChangesAsync();
        return transaction;
    }
```

(`remittance.Items.Single()` virou `First()`: o teste cruzado tem remessa de duas linhas; o retorno aponta a primeira.)

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageDeposit"`
Expected: PASS (nada mudou para quem já usava o seed).

- [ ] **Step 2: Escrever os testes que falham**

Criar `SiagroB1.Application.Tests/Reports/StorageRemittancesByPeriodReportServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class StorageRemittancesByPeriodReportServiceTests
{
    private static readonly DateTime Oct01 = new(2026, 10, 1);
    private static readonly DateTime Oct10 = new(2026, 10, 10);
    private static readonly DateTime Oct11 = new(2026, 10, 11);
    private static readonly DateTime Oct12 = new(2026, 10, 12);
    private static readonly DateTime Oct31 = new(2026, 10, 31);

    [Fact]
    public async Task BuildRows_OneRowPerRemittanceWithTheFourQuantities()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db, released: 100_000m);
        var remittance = await seed.AddRemittanceAsync(100_000m, date: Oct10, number: "000000123");
        var ret = await seed.AddReturnAsync(remittance, 29_500m, InvoiceStatus.Confirmed);
        await seed.AddReturnAsync(remittance, 50_000m, InvoiceStatus.Cancelled);
        var s1 = await seed.AddShipmentAsync(30_000m);
        await seed.AddShipmentAsync(20_000m);
        await Reconcile(db, ret, s1);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal(StorageRemittanceRowKind.Remittance, row.Kind);
        Assert.Equal("000000123", row.Remittance);
        Assert.Equal("10/10/2026", row.InvoiceDate);
        Assert.Equal("Confirmado", row.Status);
        Assert.Equal("", row.NfeStatus);
        Assert.Equal("ARM001", row.Warehouse);
        Assert.Equal("00000001", row.Contract);
        Assert.Equal("SOJA - KG", row.Group);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(100_000m, row.RemittedQuantity);
        Assert.Equal(29_500m, row.ReturnedQuantity);
        Assert.Equal(70_500m, row.PendingReturnQuantity);
        Assert.Equal(20_000m, row.ShippedWithoutReturnQuantity);
        Assert.Equal("100.000,000", row.RemittedText);
        Assert.Equal("29.500,000", row.ReturnedText);
        Assert.Equal("70.500,000", row.PendingReturnText);
        Assert.Equal("20.000,000", row.ShippedWithoutReturnText);
        Assert.Equal(1, row.RemittanceCount);
        Assert.Equal("", row.Detail);
    }

    [Fact]
    public async Task BuildRows_SkipsCancelledRemittancesAndKeepsPendingOnes()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(10m, InvoiceStatus.Cancelled, date: Oct10, number: "000000001");
        var pending = await seed.AddRemittanceAsync(20m, InvoiceStatus.Pending, date: Oct10, number: "000000002");
        pending.NfeStatus = NfeStatus.Rejected;
        await db.SaveChangesAsync();

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("000000002", row.Remittance);
        Assert.Equal("Pendente", row.Status);
        Assert.Equal("Rejeitada", row.NfeStatus);
    }

    [Fact]
    public async Task BuildRows_FiltersByRemittanceDateAndCountsReturnsIssuedLater()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var inside = await seed.AddRemittanceAsync(100m, date: new DateTime(2026, 10, 31, 23, 0, 0), number: "000000001");
        await seed.AddRemittanceAsync(100m, release: await seed.AddReleaseAsync(), date: new DateTime(2026, 11, 1), number: "000000002");
        await seed.AddRemittanceAsync(100m, release: await seed.AddReleaseAsync(), date: new DateTime(2026, 9, 30), number: "000000003");
        await seed.AddReturnAsync(inside, 100m, InvoiceStatus.Confirmed, date: new DateTime(2026, 11, 5));

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("000000001", row.Remittance);
        Assert.Equal(0m, row.PendingReturnQuantity);
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("ItemCode")]
    [InlineData("CardCode")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100m, date: Oct10, number: "000000001");
        var other = await seed.AddRemittanceAsync(100m, release: await seed.AddReleaseAsync(), date: Oct10, number: "000000002");
        other.BranchCode = "99";
        other.CardCode = "ARM999";
        other.Items.Single().ItemCode = "MILHO";
        await db.SaveChangesAsync();

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = StorageDepositSeed.Branch; break;
            case "ItemCode": request.ItemCode = StorageDepositSeed.Item; break;
            case "CardCode": request.CardCode = StorageDepositSeed.Warehouse; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "000000001" }, rows.Select(r => r.Remittance));
    }

    [Theory]
    [InlineData(StorageRemittanceSituation.All, new[] { "000000001", "000000002", "000000003" })]
    [InlineData(StorageRemittanceSituation.Open, new[] { "000000001" })]
    [InlineData(StorageRemittanceSituation.Returned, new[] { "000000002", "000000003" })]
    public async Task BuildRows_SituationSplitsOpenFromFullyReturned(StorageRemittanceSituation situation, string[] expected)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100m, date: Oct10, number: "000000001");
        var exact = await seed.AddRemittanceAsync(100m, release: await seed.AddReleaseAsync(), date: Oct10, number: "000000002");
        await seed.AddReturnAsync(exact, 100m, InvoiceStatus.Confirmed);
        var excess = await seed.AddRemittanceAsync(100m, release: await seed.AddReleaseAsync(), date: Oct10, number: "000000003");
        await seed.AddReturnAsync(excess, 120m, InvoiceStatus.Confirmed);

        var request = Request();
        request.ReturnSituation = situation;
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(expected, rows.Select(r => r.Remittance));
        if (situation == StorageRemittanceSituation.Returned)
            Assert.Equal("-20,000", rows.Single(r => r.Remittance == "000000003").PendingReturnText);
    }

    [Fact]
    public async Task BuildRows_CountsOnlyTheLoadingTransactionsOfEachOrigin()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100_000m, date: Oct10, number: "000000001");
        await seed.AddShipmentAsync(30_000m);                                                       // Compra(8): conta
        await seed.AddShipmentAsync(5_000m, origin: TransactionCode.WarehouseReconciliation);      // conferência: fora
        await seed.AddShipmentAsync(7_000m, StorageTransactionType.SalesShipment);                 // Saída(7) em Standard: fora
        var cancelled = await seed.AddShipmentAsync(17_000m);
        cancelled.TransactionStatus = StorageTransactionsStatus.Cancelled;                           // cancelado: fora
        await db.SaveChangesAsync();
        var transfer = await seed.AddReleaseAsync(ReleaseOrigin.OwnershipTransfer);
        await seed.AddRemittanceAsync(50_000m, release: transfer, date: Oct10, number: "000000002");
        await seed.AddShipmentAsync(11_000m, StorageTransactionType.SalesShipment, release: transfer); // conta
        await seed.AddShipmentAsync(13_000m, release: transfer);                                     // Compra sem perna: fora

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { 30_000m, 11_000m }, rows.Select(r => r.ShippedWithoutReturnQuantity));
    }

    [Fact]
    public async Task BuildRows_GroupsByProductAndOrdersByDateThenNumber()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(1m, date: Oct11, number: "000000001");
        await seed.AddRemittanceAsync(1m, release: await seed.AddReleaseAsync(), date: Oct10, number: "000000009");
        await seed.AddRemittanceAsync(1m, release: await seed.AddReleaseAsync(), date: Oct10, number: "000000005");
        var milho = await seed.AddRemittanceAsync(1m, release: await seed.AddReleaseAsync(), date: Oct12, number: "000000007");
        milho.Items.Single().ItemCode = "MILHO";
        await db.SaveChangesAsync();

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "MILHO - KG", "SOJA - KG", "SOJA - KG", "SOJA - KG" }, rows.Select(r => r.Group));
        Assert.Equal(new[] { "000000007", "000000005", "000000009", "000000001" }, rows.Select(r => r.Remittance));
    }

    [Fact]
    public void Queries_TranslateToSqlServer()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);
        var request = Request();
        request.BranchCode = "01";
        request.ItemCode = "SOJA";
        request.CardCode = "ARM001";
        var remittances = StorageRemittanceData.RemittancesQuery(context, request);

        var sql = new[]
        {
            remittances.ToQueryString(),
            StorageRemittanceData.ReturnLinesQuery(context, StorageRemittanceData.RemittanceItemKeys(remittances)).ToQueryString(),
            StorageRemittanceData.TransactionsQuery(context, StorageRemittanceData.ReleaseKeys(remittances)).ToQueryString(),
        };

        Assert.Contains("[SALES_INVOICES]", sql[0]);
        // O tradutor quebra a linha depois do "IN (": aceita espaço em branco antes do SELECT.
        Assert.All(sql[1..], s => Assert.Matches(@"IN \(\s*SELECT", s));
        Assert.Contains("[PURCHASE_INVOICES_ITEMS]", sql[1]);
        Assert.Contains("[STORAGE_RETURN_RECONCILIATIONS]", sql[2]);
    }

    private static StorageRemittancesByPeriodReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService());

    private static StorageRemittancesByPeriodRequest Request() => new() { FromDate = Oct01, ToDate = Oct31 };

    private static async Task Reconcile(UnitOfWork db, PurchaseInvoice ret, StorageTransaction transaction)
    {
        db.Context.StorageReturnReconciliations.Add(new StorageReturnReconciliation
        {
            PurchaseInvoiceKey = ret.Key, StorageTransactionKey = transaction.Key,
        });
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageRemittancesByPeriodReportServiceTests"`
Expected: FAIL de compilação (`StorageRemittancesByPeriodRequest`, `StorageRemittanceData` etc. não existem).

- [ ] **Step 4: Request e linha**

`SiagroB1.Reports/Dtos/StorageRemittancesByPeriodRequest.cs`:

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>Situação de retorno da remessa: Em aberto = A retornar &gt; 0; Totalmente retornada = A retornar ≤ 0.</summary>
public enum StorageRemittanceSituation
{
    All = 0,
    Open = 1,
    Returned = 2,
}

/// <summary>
/// Filtros de "Remessas para Depósito" (período sobre a data da remessa, <c>SalesInvoice.InvoiceDate</c>).
/// Armazém = parceiro da remessa; produto = alguma linha da remessa.
/// </summary>
public class StorageRemittancesByPeriodRequest : LogisticsReportRequest
{
    public string? CardCode { get; set; }

    public StorageRemittanceSituation ReturnSituation { get; set; } = StorageRemittanceSituation.All;

    /// <summary>Acrescenta, abaixo de cada remessa, os retornos e os romaneios de carregamento.</summary>
    public bool Detailed { get; set; }
}
```

`SiagroB1.Reports/Dtos/StorageRemittanceRowDto.cs`:

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>Tipo da linha achatada do relatório (string: o FastReport não precisa converter enum).</summary>
public static class StorageRemittanceRowKind
{
    public const string Remittance = "Remessa";
    public const string Return = "Retorno";
    public const string Transaction = "Romaneio";
}

/// <summary>
/// Linha de "Remessas para Depósito". Na remessa, as colunas vêm preenchidas e <see cref="Detail"/> vazio; nas
/// filhas (Detalhado), só <see cref="Detail"/>. As quantidades decimais valem 0 nas filhas — são elas que os
/// totais do template somam; os textos são o que a célula imprime (vazios nas filhas).
/// </summary>
public class StorageRemittanceRowDto
{
    public string Group { get; set; } = "";
    public string Kind { get; set; } = StorageRemittanceRowKind.Remittance;
    public string Remittance { get; set; } = "";
    public string InvoiceDate { get; set; } = "";
    public string Status { get; set; } = "";
    public string NfeStatus { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string Contract { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public string Detail { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public string RemittedText { get; set; } = "";
    public string ReturnedText { get; set; } = "";
    public string PendingReturnText { get; set; } = "";
    public string ShippedWithoutReturnText { get; set; } = "";
    public decimal RemittedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal PendingReturnQuantity { get; set; }
    public decimal ShippedWithoutReturnQuantity { get; set; }
    public int RemittanceCount { get; set; }
}
```

- [ ] **Step 5: Consultas**

`SiagroB1.Reports/Helpers/StorageRemittanceData.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Consultas de "Remessas para Depósito". ESPELHAM <c>StorageDepositRules</c> da Application, que o Reports não
/// referencia: remessa ativa, retorno não cancelado, romaneio de carregamento por origem e reconciliação.
/// Mudou lá, muda aqui — <c>StorageRemittancesByPeriodReportServiceTests</c> compara com o resumo da liberação.
/// Chaves vão como IQueryable para virar IN (SELECT ...), sem lista de parâmetros.
/// </summary>
public static class StorageRemittanceData
{
    public sealed record ReturnLine(
        Guid RemittanceItemKey, Guid PurchaseInvoiceKey, string? Number, DateTime? IssueDate, InvoiceStatus Status,
        decimal Quantity);

    public sealed record TransactionLine(
        Guid Key, Guid ReleaseKey, StorageTransactionType Type, TransactionCode? Origin, string? Code,
        DateTime? TransactionDate, string? TruckCode, decimal NetWeight, string? ReturnNumber, bool HasReturn);

    /// <summary>Remessas não canceladas (Pendente e Confirmada) emitidas no período, com os filtros opcionais.</summary>
    public static IQueryable<SalesInvoice> RemittancesQuery(AppDbContext context, StorageRemittancesByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);

        var query = context.SalesInvoices.Where(i => i.ShipmentReleaseKey != null
                                                     && i.InvoiceStatus != InvoiceStatus.Cancelled
                                                     && i.InvoiceDate >= from && i.InvoiceDate < toExclusive);

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(i => i.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(i => i.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(i => i.Items.Any(l => l.ItemCode == request.ItemCode));

        return query;
    }

    public static IQueryable<Guid> ReleaseKeys(IQueryable<SalesInvoice> remittances) =>
        remittances.Select(i => i.ShipmentReleaseKey!.Value);

    public static IQueryable<Guid> RemittanceItemKeys(IQueryable<SalesInvoice> remittances) =>
        remittances.SelectMany(i => i.Items).Where(l => l.Key != null).Select(l => l.Key!.Value);

    /// <summary>Linhas de retorno não canceladas que apontam para as linhas de remessa dadas.</summary>
    public static IQueryable<ReturnLine> ReturnLinesQuery(AppDbContext context, IQueryable<Guid> remittanceItemKeys) =>
        context.PurchaseInvoicesItems
            .Where(l => l.SalesInvoiceItemKey != null
                        && remittanceItemKeys.Contains(l.SalesInvoiceItemKey.Value)
                        && l.PurchaseInvoice!.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(l => new ReturnLine(
                l.SalesInvoiceItemKey!.Value,
                l.PurchaseInvoiceKey!.Value,
                l.PurchaseInvoice!.TaxDocumentNumber ?? l.PurchaseInvoice.InvoiceNumber,
                l.PurchaseInvoice.IssueDate,
                l.PurchaseInvoice.InvoiceStatus,
                l.Quantity));

    /// <summary>
    /// Romaneios não cancelados de Compra(8) e Saída(7) das liberações, com o retorno reconciliado. A escolha
    /// entre os dois tipos depende da origem da liberação e é feita em memória por <see cref="IsShipped"/>.
    /// </summary>
    public static IQueryable<TransactionLine> TransactionsQuery(AppDbContext context, IQueryable<Guid> releaseKeys) =>
        context.StorageTransactions
            .Where(t => t.ShipmentReleaseKey != null
                        && releaseKeys.Contains(t.ShipmentReleaseKey.Value)
                        && t.TransactionStatus != StorageTransactionsStatus.Cancelled
                        && (t.TransactionType == StorageTransactionType.SalesShipment
                            || t.TransactionType == StorageTransactionType.Purchase))
            .Select(t => new TransactionLine(
                t.Key,
                t.ShipmentReleaseKey!.Value,
                t.TransactionType,
                t.TransactionOrigin,
                t.Code,
                t.TransactionDate,
                t.TruckCode,
                t.NetWeight,
                context.StorageReturnReconciliations
                    .Where(r => r.StorageTransactionKey == t.Key)
                    .Select(r => r.PurchaseInvoice!.TaxDocumentNumber ?? r.PurchaseInvoice.InvoiceNumber)
                    .FirstOrDefault(),
                context.StorageReturnReconciliations.Any(r => r.StorageTransactionKey == t.Key)));

    /// <summary>
    /// Mesmo eixo de <c>StorageDepositRules.ShippedTransactions</c>: Saída(7) na liberação sem perna de compra;
    /// Compra(8) nas demais, menos a gerada pela Conferência de Armazém (perda de papel, não caminhão).
    /// </summary>
    public static bool IsShipped(TransactionLine transaction, ReleaseOrigin origin) =>
        ReleaseOriginRules.ShipsWithoutPurchaseLeg(origin)
            ? transaction.Type == StorageTransactionType.SalesShipment
            : transaction.Type == StorageTransactionType.Purchase
              && transaction.Origin != TransactionCode.WarehouseReconciliation;
}
```

- [ ] **Step 6: Serviço (linhas de resumo; o Detalhado entra na Task 2)**

`SiagroB1.Reports/Services/StorageRemittancesByPeriodReportService.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// "Remessas para Depósito": uma linha por remessa ativa do período, com a posição ATUAL de Remetido, Retornado,
/// A retornar e Expedido sem retorno — os mesmos números da seção da liberação
/// (<c>ShipmentReleasesStorageRemittanceSummaryService</c>). Agrupado pelo produto + UM da primeira linha da
/// remessa. Só STANDALONE (a guarda fica no controller).
/// </summary>
public class StorageRemittancesByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private sealed record ReturnDocument(string Number, DateTime? IssueDate, InvoiceStatus Status, decimal Quantity);

    private sealed record RemittancePosition(
        SalesInvoice Remittance,
        ShipmentRelease? Release,
        string Group,
        string Number,
        string UnitOfMeasure,
        List<ReturnDocument> Returns,
        List<StorageRemittanceData.TransactionLine> Shipped,
        decimal Remitted,
        decimal Returned,
        decimal ShippedWithoutReturn)
    {
        public decimal PendingReturn => Remitted - Returned;
    }

    public async Task<List<StorageRemittanceRowDto>> BuildRowsAsync(StorageRemittancesByPeriodRequest request)
    {
        var context = db.Context;
        var query = StorageRemittanceData.RemittancesQuery(context, request);

        var remittances = await query.AsNoTracking().Include(i => i.Items).ToListAsync();
        if (remittances.Count == 0)
            return [];

        var releaseKeys = StorageRemittanceData.ReleaseKeys(query);
        var releases = await context.ShipmentReleases.AsNoTracking()
            .Include(r => r.PurchaseContract)
            .Where(r => releaseKeys.Contains(r.Key))
            .ToDictionaryAsync(r => r.Key);
        var returnLines = await StorageRemittanceData
            .ReturnLinesQuery(context, StorageRemittanceData.RemittanceItemKeys(query))
            .ToListAsync();
        var transactions = await StorageRemittanceData.TransactionsQuery(context, releaseKeys).ToListAsync();

        var groupOf = InvoiceItemGrouping.BuildGroupResolver(
            remittances, i => FirstLine(i)?.ItemCode, i => FirstLine(i)?.ItemName, i => FirstLine(i)?.UnitOfMeasureCode);

        var positions = remittances
            .Select(i => Position(i, releases.GetValueOrDefault(i.ShipmentReleaseKey!.Value), groupOf(i), returnLines, transactions))
            .Where(p => Matches(p, request.ReturnSituation))
            .OrderBy(p => p.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Remittance.InvoiceDate)
            .ThenBy(p => p.Number, StringComparer.Ordinal)
            .ThenBy(p => p.Remittance.Key);

        var rows = new List<StorageRemittanceRowDto>();
        foreach (var position in positions)
            rows.Add(RemittanceRow(position));

        return rows;
    }

    private static RemittancePosition Position(
        SalesInvoice remittance,
        ShipmentRelease? release,
        string group,
        List<StorageRemittanceData.ReturnLine> returnLines,
        List<StorageRemittanceData.TransactionLine> transactions)
    {
        var itemKeys = remittance.Items.Where(l => l.Key != null).Select(l => l.Key!.Value).ToHashSet();

        var returns = returnLines
            .Where(l => itemKeys.Contains(l.RemittanceItemKey))
            .GroupBy(l => l.PurchaseInvoiceKey)
            .Select(g => new ReturnDocument(
                g.Max(l => l.Number) ?? "", g.Max(l => l.IssueDate), g.Max(l => l.Status), g.Sum(l => l.Quantity)))
            .OrderBy(r => r.IssueDate)
            .ThenBy(r => r.Number, StringComparer.Ordinal)
            .ToList();

        var origin = release?.Origin ?? ReleaseOrigin.Standard;
        var shipped = transactions
            .Where(t => t.ReleaseKey == remittance.ShipmentReleaseKey && StorageRemittanceData.IsShipped(t, origin))
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.Code, StringComparer.Ordinal)
            .ToList();

        return new RemittancePosition(
            remittance,
            release,
            group,
            DocumentNumber(remittance.TaxDocumentNumber, remittance.InvoiceNumber),
            FirstLine(remittance)?.UnitOfMeasureCode ?? "",
            returns,
            shipped,
            remittance.Items.Sum(l => l.Quantity),
            returns.Sum(r => r.Quantity),
            shipped.Where(t => !t.HasReturn).Sum(t => t.NetWeight));
    }

    private static bool Matches(RemittancePosition position, StorageRemittanceSituation situation) => situation switch
    {
        StorageRemittanceSituation.Open => position.PendingReturn > 0,
        StorageRemittanceSituation.Returned => position.PendingReturn <= 0,
        _ => true,
    };

    private static StorageRemittanceRowDto RemittanceRow(RemittancePosition p) => new()
    {
        Group = p.Group,
        Kind = StorageRemittanceRowKind.Remittance,
        Remittance = p.Number,
        InvoiceDate = ReportText.Date(p.Remittance.InvoiceDate),
        Status = InvoiceReportText.Status(p.Remittance.InvoiceStatus),
        NfeStatus = InvoiceReportText.Nfe(p.Remittance.NfeStatus),
        Warehouse = ReportText.Partner(p.Remittance.CardCode, p.Remittance.CardName),
        Contract = p.Release?.PurchaseContract?.Code ?? "",
        ReleaseDate = ReportText.Date(p.Release?.ReleaseDate),
        UnitOfMeasure = p.UnitOfMeasure,
        RemittedQuantity = p.Remitted,
        ReturnedQuantity = p.Returned,
        PendingReturnQuantity = p.PendingReturn,
        ShippedWithoutReturnQuantity = p.ShippedWithoutReturn,
        RemittedText = Quantity(p.Remitted),
        ReturnedText = Quantity(p.Returned),
        PendingReturnText = Quantity(p.PendingReturn),
        ShippedWithoutReturnText = Quantity(p.ShippedWithoutReturn),
        RemittanceCount = 1,
    };

    /// <summary>"Primeira" linha: o item não tem número de linha, então ItemCode e depois Key (determinístico).</summary>
    private static SalesInvoiceItem? FirstLine(SalesInvoice remittance) =>
        remittance.Items.OrderBy(l => l.ItemCode, StringComparer.Ordinal).ThenBy(l => l.Key).FirstOrDefault();

    /// <summary>Mesma regra de <c>StorageDepositRules.DocumentNumber</c>.</summary>
    private static string DocumentNumber(string? taxDocumentNumber, string? invoiceNumber) =>
        string.IsNullOrWhiteSpace(taxDocumentNumber) ? invoiceNumber ?? "" : taxDocumentNumber;

    private static string Quantity(decimal value) => value.ToString("N3", PtBr);
}
```

(`reportService` fica sem uso até a Task 3 — o aviso do compilador é esperado.)

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageRemittancesByPeriodReportServiceTests"`
Expected: PASS em todos os casos da classe.

- [ ] **Step 8: Commit**

```bash
git branch --show-current   # feature/storage-remittances-report
git add SiagroB1.Application.Tests/Support/StorageDepositSeed.cs SiagroB1.Reports/Dtos/StorageRemittancesByPeriodRequest.cs SiagroB1.Reports/Dtos/StorageRemittanceRowDto.cs SiagroB1.Reports/Helpers/StorageRemittanceData.cs SiagroB1.Reports/Services/StorageRemittancesByPeriodReportService.cs SiagroB1.Application.Tests/Reports/StorageRemittancesByPeriodReportServiceTests.cs
git commit -m "feat(reports): posição das remessas para depósito por período"
```
(mensagem com corpo pt-BR explicando o porquê e as linhas `Co-Authored-By`/`Claude-Session`.)

---

### Task 2: Detalhado (retornos e romaneios) + teste cruzado com a tela da liberação

**Files:**
- Modify: `SiagroB1.Reports/Services/StorageRemittancesByPeriodReportService.cs`
- Test: `SiagroB1.Application.Tests/Reports/StorageRemittancesByPeriodReportServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 (`RemittancePosition`, seed com `date`/`code`/`number`).
- Produces: com `Detailed = true`, depois de cada linha `Remittance` vêm linhas `Return` (ordem data, número) e `Transaction` (ordem data, código), com `Detail` preenchido, mesmo `Group`/`UnitOfMeasure`, quantidades 0, textos vazios, `RemittanceCount = 0`.

- [ ] **Step 1: Testes que falham** (acrescentar à classe da Task 1; precisa de `using SiagroB1.Application.Services.StorageDeposits;` no topo)

```csharp
    [Fact]
    public async Task BuildRows_DetailedAddsReturnsThenTransactionsUnderTheRemittance()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, date: Oct10, number: "000000123");
        await seed.AddReturnAsync(remittance, 20_000m, InvoiceStatus.Confirmed, date: Oct12, number: "000000502");
        var first = await seed.AddReturnAsync(remittance, 10_000m, InvoiceStatus.Pending, date: Oct11, number: "000000501");
        await seed.AddShipmentAsync(20_000m, date: Oct12, code: "RM000002");
        var loaded = await seed.AddShipmentAsync(10_000m, date: Oct11, code: "RM000001");
        await Reconcile(db, first, loaded);

        var request = Request();
        request.Detailed = true;
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(
            new[]
            {
                StorageRemittanceRowKind.Remittance, StorageRemittanceRowKind.Return, StorageRemittanceRowKind.Return,
                StorageRemittanceRowKind.Transaction, StorageRemittanceRowKind.Transaction,
            },
            rows.Select(r => r.Kind));
        Assert.Equal(
            new[]
            {
                "",
                "Retorno 000000501 de 11/10/2026 - Pendente - 10.000,000 KG",
                "Retorno 000000502 de 12/10/2026 - Confirmado - 20.000,000 KG",
                "Romaneio RM000001 de 11/10/2026 - placa ABC1D23 - 10.000,000 KG - retorno 000000501",
                "Romaneio RM000002 de 12/10/2026 - placa ABC1D23 - 20.000,000 KG - sem retorno",
            },
            rows.Select(r => r.Detail));
        Assert.All(rows.Skip(1), r =>
        {
            Assert.Equal("SOJA - KG", r.Group);
            Assert.Equal("KG", r.UnitOfMeasure);
            Assert.Equal("", r.Remittance);
            Assert.Equal("", r.RemittedText);
            Assert.Equal(0m, r.RemittedQuantity + r.ReturnedQuantity + r.PendingReturnQuantity + r.ShippedWithoutReturnQuantity);
            Assert.Equal(0, r.RemittanceCount);
        });
    }

    [Fact]
    public async Task BuildRows_WithoutDetailedHasOnlyRemittanceRows()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100m, date: Oct10);
        await seed.AddReturnAsync(remittance, 10m, InvoiceStatus.Confirmed);
        await seed.AddShipmentAsync(10m);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { StorageRemittanceRowKind.Remittance }, rows.Select(r => r.Kind));
    }

    /// <summary>
    /// O relatório reescreve as regras de StorageDepositRules (o Reports não enxerga a Application). Este teste
    /// garante que, remessa a remessa, os quatro números são os da seção "Remessa para depósito" da liberação.
    /// </summary>
    [Fact]
    public async Task BuildRows_MatchesTheReleaseScreenForEveryRemittance()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        // A: Standard, retorno parcial, um romaneio reconciliado, um sem retorno, um de conferência.
        var a = await seed.AddRemittanceAsync(100_000m, date: Oct10, number: "000000001");
        var aReturn = await seed.AddReturnAsync(a, 30_000m, InvoiceStatus.Confirmed);
        await seed.AddReturnAsync(a, 5_000m, InvoiceStatus.Cancelled);
        var aLoaded = await seed.AddShipmentAsync(30_000m);
        await seed.AddShipmentAsync(12_000m);
        await seed.AddShipmentAsync(4_000m, origin: TransactionCode.WarehouseReconciliation);
        await Reconcile(db, aReturn, aLoaded);

        // B: transferência (Saída 7), totalmente retornada.
        var transfer = await seed.AddReleaseAsync(ReleaseOrigin.OwnershipTransfer);
        var b = await seed.AddRemittanceAsync(40_000m, release: transfer, date: Oct10, number: "000000002");
        await seed.AddReturnAsync(b, 40_000m, InvoiceStatus.Confirmed);
        await seed.AddShipmentAsync(40_000m, StorageTransactionType.SalesShipment, release: transfer);
        await seed.AddShipmentAsync(9_000m, release: transfer);

        // C: remessa editada para duas linhas, sem retorno.
        var other = await seed.AddReleaseAsync();
        var c = await seed.AddRemittanceAsync(60_000m, release: other, date: Oct10, number: "000000003");
        db.Context.SalesInvoicesItems.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = c.Key, ItemCode = StorageDepositSeed.Item, UnitOfMeasureCode = "KG",
            Quantity = 40_000m, UsageCode = seed.RemittanceUsageCode,
        });
        await db.SaveChangesAsync();

        var rows = await Service(db).BuildRowsAsync(Request());
        var screen = new ShipmentReleasesStorageRemittanceSummaryService(db, TestConfiguration.Erp("STANDALONE"));

        foreach (var (release, number) in new[] { (seed.Release, "000000001"), (transfer, "000000002"), (other, "000000003") })
        {
            var expected = await screen.ExecuteAsync(release.Key);
            var row = rows.Single(r => r.Remittance == number);
            Assert.Equal(expected.RemittedQuantity, row.RemittedQuantity);
            Assert.Equal(expected.ReturnedQuantity, row.ReturnedQuantity);
            Assert.Equal(expected.PendingReturnQuantity, row.PendingReturnQuantity);
            Assert.Equal(expected.ShippedWithoutReturnQuantity, row.ShippedWithoutReturnQuantity);
        }
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageRemittancesByPeriodReportServiceTests"`
Expected: FAIL em `BuildRows_DetailedAddsReturnsThenTransactionsUnderTheRemittance` (só sai a linha de remessa). Os outros dois devem PASSAR já — se o cruzado falhar, a regra da Task 1 diverge da Application: corrija `StorageRemittanceData`, não o teste.

- [ ] **Step 3: Implementar**

No serviço, trocar o laço final de `BuildRowsAsync`:

```csharp
        var rows = new List<StorageRemittanceRowDto>();
        foreach (var position in positions)
        {
            rows.Add(RemittanceRow(position));
            if (!request.Detailed)
                continue;

            rows.AddRange(position.Returns.Select(r => ReturnRow(position, r)));
            rows.AddRange(position.Shipped.Select(t => TransactionRow(position, t)));
        }

        return rows;
```

e acrescentar, depois de `RemittanceRow`:

```csharp
    private static StorageRemittanceRowDto ReturnRow(RemittancePosition p, ReturnDocument r) => new()
    {
        Group = p.Group,
        Kind = StorageRemittanceRowKind.Return,
        UnitOfMeasure = p.UnitOfMeasure,
        Detail = Join(
            Head("Retorno", r.Number, r.IssueDate),
            InvoiceReportText.Status(r.Status),
            $"{Quantity(r.Quantity)} {p.UnitOfMeasure}".Trim()),
    };

    private static StorageRemittanceRowDto TransactionRow(RemittancePosition p, StorageRemittanceData.TransactionLine t) => new()
    {
        Group = p.Group,
        Kind = StorageRemittanceRowKind.Transaction,
        UnitOfMeasure = p.UnitOfMeasure,
        Detail = Join(
            Head("Romaneio", t.Code, t.TransactionDate),
            string.IsNullOrWhiteSpace(t.TruckCode) ? "" : $"placa {t.TruckCode.Trim()}",
            $"{Quantity(t.NetWeight)} {p.UnitOfMeasure}".Trim(),
            t.HasReturn ? $"retorno {t.ReturnNumber}" : "sem retorno"),
    };

    /// <summary>"Rótulo número de dd/MM/yyyy" — sem data, sem o "de".</summary>
    private static string Head(string label, string? number, DateTime? date)
    {
        var head = $"{label} {number}".Trim();
        return date is null ? head : $"{head} de {ReportText.Date(date)}";
    }

    private static string Join(params string[] parts) =>
        string.Join(" - ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageRemittancesByPeriodReportServiceTests"`
Expected: PASS em todos.

- [ ] **Step 5: Commit**

```bash
git branch --show-current
git add SiagroB1.Reports/Services/StorageRemittancesByPeriodReportService.cs SiagroB1.Application.Tests/Reports/StorageRemittancesByPeriodReportServiceTests.cs
git commit -m "feat(reports): detalhe de retornos e romaneios nas remessas para depósito"
```
(corpo pt-BR + linhas de atribuição.)

---

### Task 3: PDF — parâmetros, template, controller

**Files:**
- Modify: `SiagroB1.Reports/Services/StorageRemittancesByPeriodReportService.cs`
- Create: `SiagroB1.Reports/Reports/Templates/StorageRemittancesByPeriod.frx`
- Create: `SiagroB1.Reports/Controllers/StorageRemittancesByPeriodController.cs`
- Test: `SiagroB1.Application.Tests/Reports/StorageRemittancesByPeriodReportServiceTests.cs`
- Test: `SiagroB1.Application.Tests/Reports/StorageRemittancesByPeriodControllerTests.cs`
- Modify: `SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs`
- Modify: `SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs`

**Interfaces:**
- Consumes: `BuildRowsAsync` (Tasks 1–2).
- Produces: `Task<byte[]> ExecuteAsync(StorageRemittancesByPeriodRequest)`; `POST /reports/StorageRemittancesByPeriod` (o frontend chama esta rota); template com fonte de dados `StorageRemittances`.

- [ ] **Step 1: Testes que falham — parâmetros** (acrescentar à classe de serviço)

```csharp
    [Fact]
    public async Task Execute_DescribesEveryFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100m, date: Oct10);
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.BranchCode = StorageDepositSeed.Branch;
        request.ItemCode = StorageDepositSeed.Item;
        request.CardCode = StorageDepositSeed.Warehouse;
        request.ReturnSituation = StorageRemittanceSituation.Open;
        request.Detailed = true;
        await new StorageRemittancesByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal("StorageRemittancesByPeriod.frx", recorder.LastReportName);
        Assert.Equal(
            "Data da remessa: 01/10/2026 a 31/10/2026 | Filial: MTZ | Produto: SOJA | Situação: em aberto | " +
            "Armazém: ARM001 | Detalhado",
            recorder.LastParameters!["pFilters"]);
        Assert.Equal(true, recorder.LastParameters![FastReportService.SingleUomParameter]);
    }

    [Theory]
    [InlineData(StorageRemittanceSituation.All, "Situação: todas")]
    [InlineData(StorageRemittanceSituation.Returned, "Situação: totalmente retornada")]
    public async Task Execute_DescribesTheSituation(StorageRemittanceSituation situation, string expected)
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();
        var request = Request();
        request.ReturnSituation = situation;

        await new StorageRemittancesByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal($"Data da remessa: 01/10/2026 a 31/10/2026 | {expected}", recorder.LastParameters!["pFilters"]);
    }
```

- [ ] **Step 2: Testes que falham — controller**

`SiagroB1.Application.Tests/Reports/StorageRemittancesByPeriodControllerTests.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Controllers;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class StorageRemittancesByPeriodControllerTests
{
    private static StorageRemittancesByPeriodController Controller(string erp) =>
        new(new StorageRemittancesByPeriodReportService(TestDb.CreateUnitOfWork(), new RecordingFastReportService()),
            TestConfiguration.Erp(erp))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static StorageRemittancesByPeriodRequest Request() =>
        new() { FromDate = new DateTime(2026, 10, 1), ToDate = new DateTime(2026, 10, 31) };

    [Fact]
    public async Task Refuses_in_sap_mode()
    {
        var result = await Controller("SAPB1").Report(Request());

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Disponível somente no modo STANDALONE.", bad.Value);
    }

    [Fact]
    public async Task Refuses_an_inverted_period()
    {
        var request = Request();
        request.ToDate = new DateTime(2026, 9, 30);

        var result = await Controller("STANDALONE").Report(request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("A data final não pode ser anterior à inicial.", bad.Value);
    }

    [Fact]
    public async Task Returns_the_pdf_inline()
    {
        var controller = Controller("STANDALONE");

        var result = await controller.Report(Request());

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("inline; filename=\"storage-remittances-by-period.pdf\"",
            controller.Response.Headers.ContentDisposition.ToString());
    }
}
```

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageRemittancesByPeriod"`
Expected: FAIL de compilação (`ExecuteAsync`, controller inexistentes).

- [ ] **Step 4: `ExecuteAsync`** (no serviço, depois de `BuildRowsAsync`)

```csharp
    public async Task<byte[]> ExecuteAsync(StorageRemittancesByPeriodRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var first = rows.FirstOrDefault(r => r.Kind == StorageRemittanceRowKind.Remittance);

        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            extra.Add($"Armazém: {ReportText.Describe(first?.Warehouse, request.CardCode)}");
        if (request.Detailed)
            extra.Add("Detalhado");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = LogisticsReportText.BuildFilters(
                "Data da remessa",
                request,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                ReportText.ProductOfGroup(first?.Group),
                SituationText(request.ReturnSituation),
                extra),
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "StorageRemittancesByPeriod.frx", rows, "StorageRemittances", "StorageRemittances", parameters);
    }

    private static string SituationText(StorageRemittanceSituation situation) => situation switch
    {
        StorageRemittanceSituation.Open => "Situação: em aberto",
        StorageRemittanceSituation.Returned => "Situação: totalmente retornada",
        _ => "Situação: todas",
    };
```

- [ ] **Step 5: Controller**

`SiagroB1.Reports/Controllers/StorageRemittancesByPeriodController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/StorageRemittancesByPeriod")]
public class StorageRemittancesByPeriodController(
    StorageRemittancesByPeriodReportService service,
    IConfiguration configuration) : ControllerBase
{
    /// <summary>Mesma mensagem de <c>StorageDepositRules.StandaloneOnlyMessage</c>: a remessa só existe em STANDALONE.</summary>
    public const string StandaloneOnlyMessage = "Disponível somente no modo STANDALONE.";

    [HttpPost]
    public async Task<IActionResult> Report([FromBody] StorageRemittancesByPeriodRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(StandaloneOnlyMessage);

        if (LogisticsReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"storage-remittances-by-period.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

- [ ] **Step 6: Template**

`SiagroB1.Reports/Reports/Templates/StorageRemittancesByPeriod.frx` — colunas: Remessa 0/86, Emissão 86/62, Situação 148/62, NF-e 210/92, Armazém 302/296, Contrato 598/72, Liberação 670/62, Remetido 732/88, Retornado 820/88, A retornar 908/88, Exp. s/ retorno 996/88; `txtDetail` 20/712 (só as filhas preenchem):

```xml
<?xml version="1.0" encoding="utf-8"?>
<Report ScriptLanguage="CSharp" ReportInfo.Created="10/12/2026 10:00:00" ReportInfo.Modified="10/12/2026 10:00:00" ReportInfo.CreatorVersion="2026.1.0.0">
  <Styles>
    <Style Name="EvenRows" Fill.Color="Gainsboro" Font="Arial, 10pt"/>
  </Styles>
  <Dictionary>
    <BusinessObjectDataSource Name="StorageRemittances" ReferenceName="StorageRemittances" DataType="System.Int32" Enabled="true">
      <Column Name="Group" DataType="System.String"/>
      <Column Name="Kind" DataType="System.String"/>
      <Column Name="Remittance" DataType="System.String"/>
      <Column Name="InvoiceDate" DataType="System.String"/>
      <Column Name="Status" DataType="System.String"/>
      <Column Name="NfeStatus" DataType="System.String"/>
      <Column Name="Warehouse" DataType="System.String"/>
      <Column Name="Contract" DataType="System.String"/>
      <Column Name="ReleaseDate" DataType="System.String"/>
      <Column Name="Detail" DataType="System.String"/>
      <Column Name="UnitOfMeasure" DataType="System.String"/>
      <Column Name="RemittedText" DataType="System.String"/>
      <Column Name="ReturnedText" DataType="System.String"/>
      <Column Name="PendingReturnText" DataType="System.String"/>
      <Column Name="ShippedWithoutReturnText" DataType="System.String"/>
      <Column Name="RemittedQuantity" DataType="System.Decimal"/>
      <Column Name="ReturnedQuantity" DataType="System.Decimal"/>
      <Column Name="PendingReturnQuantity" DataType="System.Decimal"/>
      <Column Name="ShippedWithoutReturnQuantity" DataType="System.Decimal"/>
      <Column Name="RemittanceCount" DataType="System.Int32"/>
    </BusinessObjectDataSource>
    <Parameter Name="pCompanyName" DataType="System.String" AsString=""/>
    <Parameter Name="pFilters" DataType="System.String" AsString=""/>
    <Parameter Name="pSingleUom" DataType="System.Boolean" AsString="true"/>
    <Total Name="TotalGrpRemitted" Expression="[StorageRemittances.RemittedQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpReturned" Expression="[StorageRemittances.ReturnedQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpPendingReturn" Expression="[StorageRemittances.PendingReturnQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpShippedWithoutReturn" Expression="[StorageRemittances.ShippedWithoutReturnQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalSumRemitted" Expression="[StorageRemittances.RemittedQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumReturned" Expression="[StorageRemittances.ReturnedQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumPendingReturn" Expression="[StorageRemittances.PendingReturnQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumShippedWithoutReturn" Expression="[StorageRemittances.ShippedWithoutReturnQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalRemittances" Expression="[StorageRemittances.RemittanceCount]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalCountAll" TotalType="Count" Evaluator="Data1" PrintOn="ReportSummary1"/>
  </Dictionary>
  <ReportPage Name="Page1" Landscape="true" PaperWidth="297" PaperHeight="210" LeftMargin="5" TopMargin="5" RightMargin="5" BottomMargin="5" RawPaperSize="9" Watermark.Font="Arial, 60pt">
    <PageHeaderBand Name="PageHeader1" Width="1084" Height="113.4">
      <PictureObject Name="picLogo" Left="0" Top="2" Width="94.5" Height="37.8" SizeMode="Zoom"/>
      <TextObject Name="txtCompany" Left="100" Top="2" Width="700" Height="37.8" Text="[pCompanyName]" VertAlign="Center" Font="Arial, 11pt, style=Bold"/>
      <TextObject Name="txtDate" Left="937" Top="2" Width="147" Height="18.9" Text="[Date]" Format="Date" Format.Format="d" HorzAlign="Right" VertAlign="Center" Font="Tahoma, 6pt"/>
      <TextObject Name="txtTitle" Top="45" Width="1084" Height="28.35" Text="Remessas para Depósito" HorzAlign="Center" VertAlign="Center" Font="Arial, 14pt, style=Bold, Italic"/>
      <TextObject Name="txtFilters" Top="75" Width="1084" Height="37.8" Text="[pFilters]" HorzAlign="Center" VertAlign="Top" WordWrap="true" Font="Consolas, 8pt, style=Italic"/>
    </PageHeaderBand>
    <ColumnHeaderBand Name="ColumnHeader1" Top="116.6" Width="1084" Height="17">
      <TextObject Name="hdrRemittance" Left="0" Width="86" Height="17" Text="Remessa" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrInvoiceDate" Left="86" Width="62" Height="17" Text="Emissão" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrStatus" Left="148" Width="62" Height="17" Text="Situação" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrNfeStatus" Left="210" Width="92" Height="17" Text="NF-e" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrWarehouse" Left="302" Width="296" Height="17" Text="Armazém" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrContract" Left="598" Width="72" Height="17" Text="Contrato" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrReleaseDate" Left="670" Width="62" Height="17" Text="Liberação" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrRemitted" Left="732" Width="88" Height="17" Text="Remetido" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrReturned" Left="820" Width="88" Height="17" Text="Retornado" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrPendingReturn" Left="908" Width="88" Height="17" Text="A retornar" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrShippedWithoutReturn" Left="996" Width="88" Height="17" Text="Exp. s/ retorno" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
    </ColumnHeaderBand>
    <GroupHeaderBand Name="GroupHeader1" SortOrder="None" Top="136.7" Width="1084" Height="20" Condition="[StorageRemittances.Group]">
      <TextObject Name="txtGroup" Left="0" Top="2" Width="700" Height="17" Text="[StorageRemittances.Group]" VertAlign="Center" WordWrap="false" Font="Arial, 8pt, style=Bold"/>
      <DataBand Name="Data1" Top="159.75" Width="1084" Height="17" CanGrow="true" EvenStyle="EvenRows" DataSource="StorageRemittances">
        <TextObject Name="txtRemittance" Left="0" Width="86" Height="17" Text="[StorageRemittances.Remittance]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtInvoiceDate" Left="86" Width="62" Height="17" Text="[StorageRemittances.InvoiceDate]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtStatus" Left="148" Width="62" Height="17" Text="[StorageRemittances.Status]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtNfeStatus" Left="210" Width="92" Height="17" Text="[StorageRemittances.NfeStatus]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtWarehouse" Left="302" Width="296" Height="17" Text="[StorageRemittances.Warehouse]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtContract" Left="598" Width="72" Height="17" Text="[StorageRemittances.Contract]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtReleaseDate" Left="670" Width="62" Height="17" Text="[StorageRemittances.ReleaseDate]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtDetail" Left="20" Width="712" Height="17" Text="[StorageRemittances.Detail]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
        <TextObject Name="txtRemitted" Left="732" Width="88" Height="17" Text="[StorageRemittances.RemittedText]" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtReturned" Left="820" Width="88" Height="17" Text="[StorageRemittances.ReturnedText]" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtPendingReturn" Left="908" Width="88" Height="17" Text="[StorageRemittances.PendingReturnText]" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtShippedWithoutReturn" Left="996" Width="88" Height="17" Text="[StorageRemittances.ShippedWithoutReturnText]" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
      </DataBand>
      <GroupFooterBand Name="GroupFooter1" Top="180" Width="1084" Height="20">
        <TextObject Name="txtGrpLabel" Left="0" Top="2" Width="732" Height="17" Text="Subtotal do produto/UM:" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpRemitted" Left="732" Top="2" Width="88" Height="17" Text="[TotalGrpRemitted]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpReturned" Left="820" Top="2" Width="88" Height="17" Text="[TotalGrpReturned]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpPendingReturn" Left="908" Top="2" Width="88" Height="17" Text="[TotalGrpPendingReturn]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpShippedWithoutReturn" Left="996" Top="2" Width="88" Height="17" Text="[TotalGrpShippedWithoutReturn]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
      </GroupFooterBand>
    </GroupHeaderBand>
    <ReportSummaryBand Name="ReportSummary1" Top="203" Width="1084" Height="48">
      <TextObject Name="txtSumLabel" Left="0" Top="4" Width="100" Height="17" Text="TOTAL GERAL:" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Bold"/>
      <TextObject Name="txtSumCount" Left="100" Top="4" Width="130" Height="17" Text="[TotalRemittances] remessa(s)" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="uomMixedNote" Left="228" Top="4" Width="504" Height="17" Text="UMs diferentes: quantidades só nos subtotais." VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="uomSumRemitted" Left="732" Top="4" Width="88" Height="17" Text="[TotalSumRemitted]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumReturned" Left="820" Top="4" Width="88" Height="17" Text="[TotalSumReturned]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumPendingReturn" Left="908" Top="4" Width="88" Height="17" Text="[TotalSumPendingReturn]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumShippedWithoutReturn" Left="996" Top="4" Width="88" Height="17" Text="[TotalSumShippedWithoutReturn]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="txtEmpty" Left="0" Top="26" Width="1084" Height="17" Text="[IIf([TotalCountAll] == 0, &quot;Nenhum registro encontrado.&quot;, &quot;&quot;)]" HorzAlign="Center" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Italic"/>
    </ReportSummaryBand>
    <PageFooterBand Name="PageFooter1" Top="254" Width="1084" Height="26">
      <TextObject Name="txtPage" Left="937" Width="147" Height="17" Text="Página [Page#] de [TotalPages#]" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
    </PageFooterBand>
  </ReportPage>
</Report>
```

- [ ] **Step 7: Layout — incluir o template**

Em `LogisticsReportsLayoutTests.cs`:

1. Em `Templates`, acrescentar `"StorageRemittancesByPeriod",`.
2. Em `LongestText`, acrescentar:

```csharp
        ["StorageRemittancesByPeriod"] = new()
        {
            ["txtRemittance"] = 15,           // S1a2b3c... / 000000123 (nº interno longo)
            ["txtInvoiceDate"] = 10,          // 31/12/2026
            ["txtStatus"] = 10,               // Confirmado
            ["txtNfeStatus"] = 16,            // Em processamento
            ["txtContract"] = 12,             // PC2026000123
            ["txtReleaseDate"] = 10,          // 31/12/2026
            ["txtRemitted"] = 14,             // 99.999.999,999 (texto formatado no serviço)
            ["txtReturned"] = 14,
            ["txtPendingReturn"] = 15,        // -10.000.000,000 (retorno a maior)
            ["txtShippedWithoutReturn"] = 14,
        },
```

3. Em `DataCells_DoNotOverlap`, trocar a seleção de células por:

```csharp
        // txtDetail (Remessas para Depósito) cobre de propósito as colunas de texto: só as linhas filhas o
        // preenchem, e nelas as outras células vêm vazias. Conferido à parte em DetailCell_EndsWhereTheQuantitiesStart.
        var cells = TextObjects(report).Where(o => o.Parent is DataBand && o.Name != "txtDetail").OrderBy(o => o.Left).ToList();
```

4. Acrescentar:

```csharp
    [Fact]
    public void DetailCell_EndsWhereTheQuantitiesStart()
    {
        using var report = Load("StorageRemittancesByPeriod");
        var objects = TextObjects(report);
        var detail = objects.Single(o => o.Name == "txtDetail");
        var firstQuantity = objects.Single(o => o.Name == "txtRemitted");

        Assert.True(detail.Left + detail.Width <= firstQuantity.Left + 0.01f,
            $"txtDetail termina em {detail.Left + detail.Width}px; Remetido começa em {firstQuantity.Left}px");
    }
```

- [ ] **Step 8: PDF de ponta a ponta**

Em `LogisticsReportsPdfTests.cs` (acrescentar `using SiagroB1.Domain.Entities;` se faltar):

```csharp
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StorageRemittancesByPeriod_ProducesAPdf(bool detailed)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var oct10 = new DateTime(2026, 10, 10);
        var open = await seed.AddRemittanceAsync(99_999_999.999m, date: oct10, number: "000000123");
        var ret = await seed.AddReturnAsync(open, 30_000m, InvoiceStatus.Confirmed, date: oct10);
        var loaded = await seed.AddShipmentAsync(30_000m, date: oct10, code: "RM000048");
        await seed.AddShipmentAsync(20_000m, date: oct10, code: "RM000049");
        db.Context.StorageReturnReconciliations.Add(new StorageReturnReconciliation
        {
            PurchaseInvoiceKey = ret.Key, StorageTransactionKey = loaded.Key,
        });
        var excess = await seed.AddRemittanceAsync(100m, release: await seed.AddReleaseAsync(), date: oct10, number: "000000124");
        await seed.AddReturnAsync(excess, 10_000_100m, InvoiceStatus.Confirmed, date: oct10, number: "000000999");
        await db.SaveChangesAsync();

        var pdf = await new StorageRemittancesByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new StorageRemittancesByPeriodRequest
            {
                FromDate = new DateTime(2026, 10, 1), ToDate = new DateTime(2026, 10, 31), Detailed = detailed,
            });

        Keep(detailed ? "StorageRemittancesByPeriod-detailed" : "StorageRemittancesByPeriod", "STANDALONE", pdf);
    }

    [Fact]
    public async Task StorageRemittancesByPeriod_EmptyResultStillProducesAPdf()
    {
        var db = TestDb.CreateUnitOfWork();

        var pdf = await new StorageRemittancesByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new StorageRemittancesByPeriodRequest { FromDate = new DateTime(2026, 10, 1), ToDate = new DateTime(2026, 10, 31) });

        Assert.NotEmpty(pdf);
    }
```

- [ ] **Step 9: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageRemittancesByPeriod|FullyQualifiedName~LogisticsReports"`
Expected: PASS. Abrir `%TEMP%/siagro-logistics-reports/StorageRemittancesByPeriod-detailed-STANDALONE.pdf` e conferir à vista: linhas filhas recuadas e em itálico, `-10.000.000,000` sem reticências, subtotal e "2 remessa(s)" no total geral (sem poppler: ver a memória `render-report-template-locally`).

- [ ] **Step 10: Suíte completa**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS (a contagem anterior + os novos; nenhum teste existente quebra com o seed estendido).

- [ ] **Step 11: Commit**

```bash
git branch --show-current
git add SiagroB1.Reports/Services/StorageRemittancesByPeriodReportService.cs SiagroB1.Reports/Controllers/StorageRemittancesByPeriodController.cs SiagroB1.Reports/Reports/Templates/StorageRemittancesByPeriod.frx SiagroB1.Application.Tests/Reports/StorageRemittancesByPeriodReportServiceTests.cs SiagroB1.Application.Tests/Reports/StorageRemittancesByPeriodControllerTests.cs SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs
git commit -m "feat(reports): PDF e endpoint do relatório de remessas para depósito"
```
(corpo pt-BR + linhas de atribuição.)

---

### Task 4: Menu

**Files:**
- Create: `SiagroB1.Migrations/CommonContext/<timestamp>_AddStorageRemittancesReportMenu.cs` (+ `.Designer.cs` gerado)

**Interfaces:**
- Produces: item de menu `storageRemittancesReport` — a MESMA string é o `name` da rota no `manifest.json` do frontend.

- [ ] **Step 1: Gerar a migration vazia**

Run (só gera arquivo; não toca banco):
`dotnet ef migrations add AddStorageRemittancesReportMenu --project SiagroB1.Migrations --startup-project SiagroB1.Web --context CommonDbContext --output-dir CommonContext`
Expected: `Up`/`Down` vazios e `CommonDbContextModelSnapshot.cs` sem mudança (se o snapshot mudar, há drift alheio: pare e reporte).

- [ ] **Step 2: Preencher**

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddStorageRemittancesReportMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // StandaloneOnly: a remessa para depósito só existe em STANDALONE.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "storageRemittancesReport", "Remessas para Depósito", "sap-icon://folder-blank", true, false, 19, "reports", true });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { "9B4E2C71-6A3D-4F85-B1E0-7C2D5A8F3E19", "ADMIN", "storageRemittancesReport" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValue: "9B4E2C71-6A3D-4F85-B1E0-7C2D5A8F3E19");

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: "storageRemittancesReport");
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build SiagroB1.sln`
Expected: 0 erros.

- [ ] **Step 4: Commit**

```bash
git branch --show-current
git add SiagroB1.Migrations/CommonContext/*_AddStorageRemittancesReportMenu*.cs
git commit -m "feat(reports): menu do relatório de remessas para depósito"
```
Corpo pt-BR + trailer obrigatório `DB: AddStorageRemittancesReportMenu` + linhas de atribuição.

A migration é aplicada no banco da CEAGUI dev na verificação do plano do frontend — nunca em outro ambiente sem pedido do usuário.
