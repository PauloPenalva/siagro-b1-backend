# Relatório de Contratos de Compra por Produto — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar um relatório PDF de contratos de compra emitidos num período, quebrado por produto, com filtros de filial, local de entrega, fornecedor, safra e período de entrega, alcançável pelo menu Relatórios.

**Architecture:** Segue o padrão já consolidado de relatórios do SiagroB1: tela UI5 com formulário de filtros → `POST /reports/PurchaseContractsByItem` no serviço `SiagroB1.Reports` → consulta EF Core em `AppDbContext` → linhas achatadas em DTO → template FastReport (`.frx`) → PDF aberto em nova aba pelo navegador. A regra de negócio (filtros, formatação de texto, ordenação) fica num serviço C# testável; o template só desenha.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server em produção, InMemory nos testes), FastReport.OpenSource, xUnit, OpenUI5/SAPUI5 1.141 + TypeScript.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-07-27-purchase-contracts-by-item-report-design.md`

## Global Constraints

- **Dois repositórios independentes.** Tasks 1, 2 e 4 são em `C:\Projetos\SiagroB1\siagro-b1-backend`; a Task 3 é em `C:\Projetos\SiagroB1\siagro-b1-frontend`. Confira em qual você está antes de qualquer comando git.
- **NUNCA rodar `git commit` ou `git push`.** Neste projeto os commits são sempre manuais, feitos pelo usuário — mesmo quando um passo de skill mandar commitar. Ao fim de cada task, liste os arquivos alterados e siga em frente.
- **Contratos cancelados (`ContractStatus.Canceled`) nunca entram no relatório.** Todos os outros status entram, inclusive `Draft`.
- **Preço é sempre `StandardPrice`.** Não consultar fixações de preço (contrato PAF sai zerado — é o comportamento pedido).
- **Nada de propriedade de navegação para entidade do SAP.** Em modo `Erp=SAPB1` as tabelas locais `BUSINESS_PARTNERS`, `ITEMS` e `WAREHOUSES` ficam vazias e o JOIN zeraria o resultado. Use os campos desnormalizados do próprio contrato: `CardName`, `ItemName`, `DeliveryLocationName`, `AgentName`. `Branch` (tabela local `BRANCHS`) é exceção e pode ser navegada.
- **Cultura pt-BR explícita** (`CultureInfo.GetCultureInfo("pt-BR")`) em toda formatação de número e data feita em C#, para o resultado não depender da cultura da máquina.
- Nomes de arquivo/recurso: **`PurchaseContractsByItem`** em todo o backend; rota e chave de menu **`purchaseContractsByItemReport`** no frontend.

---

### Task 1: Consulta e formatação das linhas (backend)

O coração do relatório: filtros, ordenação e o texto de cada célula. Sem PDF ainda — é tudo testável em memória.

**Files:**
- Create: `siagro-b1-backend/SiagroB1.Reports/Dtos/PurchaseContractsByItemRequest.cs`
- Create: `siagro-b1-backend/SiagroB1.Reports/Dtos/PurchaseContractsByItemRowDto.cs`
- Create: `siagro-b1-backend/SiagroB1.Reports/Services/PurchaseContractsByItemReportService.cs`
- Test: `siagro-b1-backend/SiagroB1.Application.Tests/Reports/PurchaseContractsByItemReportServiceTests.cs`

**Interfaces:**
- Consumes: `IUnitOfWork` (`SiagroB1.Infra`, expõe `Context` do tipo `AppDbContext`); `IFastReportService` (`SiagroB1.Reports.Services`); `TestDb.CreateUnitOfWork()` (`SiagroB1.Application.Tests.Support`, devolve `UnitOfWork` sobre EF InMemory).
- Produces:
  - `PurchaseContractsByItemRequest` — propriedades `DateTime FromDate`, `DateTime ToDate`, `string? ItemCode`, `string? HarvestSeasonCode`, `string? BranchCode`, `string? DeliveryLocationCode`, `string? CardCode`, `DateTime? DeliveryFromDate`, `DateTime? DeliveryToDate`.
  - `PurchaseContractsByItemRowDto` — `ItemCode`, `ItemName`, `Product`, `ContractCode`, `Status`, `Branch`, `DeliveryLocation`, `Supplier`, `Funrural`, `PaymentForecast`, `Commission`, `Freight`, `Buyer` (todos `string`), `Quantity` e `Price` (`decimal`).
  - `PurchaseContractsByItemReportService(IUnitOfWork db, IFastReportService reportService)` com `Task<List<PurchaseContractsByItemRowDto>> BuildRowsAsync(PurchaseContractsByItemRequest request)` e `static string BuildFiltersDescription(PurchaseContractsByItemRequest request, IReadOnlyList<PurchaseContractsByItemRowDto> rows)`.

**Por que só `Quantity` e `Price` são numéricos:** eles somam nos totais do template. Todo o resto vai como `string` já formatada — inclusive `PaymentForecast`. Coluna de data/hora no template faz o FastReport falhar ao renderizar sem dados ("Invalid cast from Int32 to DateTimeOffset"), que é exatamente o motivo de `WeighingTicket.frx` estar excluído do teste de render. Mantendo tudo como texto, o template novo passa no smoke test sem exceção.

- [ ] **Step 1: Criar os dois DTOs**

`SiagroB1.Reports/Dtos/PurchaseContractsByItemRequest.cs`:

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros do relatório de contratos de compra por produto e período.
/// Só o período de emissão (FromDate/ToDate) é obrigatório; os demais campos,
/// quando nulos ou vazios, não restringem o resultado.
/// </summary>
public class PurchaseContractsByItemRequest
{
    /// <summary>Início do período de EMISSÃO do contrato (PurchaseContract.CreationDate).</summary>
    public DateTime FromDate { get; set; }

    /// <summary>Fim do período de emissão, inclusivo até o fim do dia.</summary>
    public DateTime ToDate { get; set; }

    public string? ItemCode { get; set; }

    public string? HarvestSeasonCode { get; set; }

    public string? BranchCode { get; set; }

    public string? DeliveryLocationCode { get; set; }

    /// <summary>Fornecedor.</summary>
    public string? CardCode { get; set; }

    /// <summary>Início do período de ENTREGA. Filtra por sobreposição de janela.</summary>
    public DateTime? DeliveryFromDate { get; set; }

    public DateTime? DeliveryToDate { get; set; }
}
```

`SiagroB1.Reports/Dtos/PurchaseContractsByItemRowDto.cs`:

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Uma linha do relatório, já achatada e formatada para o template FastReport.
/// Só Quantity e Price são numéricos (são totalizados no .frx); o resto é texto
/// pronto, para o template não declarar colunas de data e quebrar ao renderizar
/// sem dados.
/// </summary>
public class PurchaseContractsByItemRowDto
{
    public string ItemCode { get; set; } = "";

    public string ItemName { get; set; } = "";

    /// <summary>Cabeçalho do grupo, ex.: "SOJA EM GRÃOS (10001)".</summary>
    public string Product { get; set; } = "";

    public string ContractCode { get; set; } = "";

    public string Status { get; set; } = "";

    /// <summary>Ex.: "01 - MATRIZ".</summary>
    public string Branch { get; set; } = "";

    /// <summary>Ex.: "AZ01 - SILO 1".</summary>
    public string DeliveryLocation { get; set; } = "";

    public string Supplier { get; set; } = "";

    public decimal Quantity { get; set; }

    public decimal Price { get; set; }

    public string Funrural { get; set; } = "";

    /// <summary>Previsão de pagamento no formato dd/MM/yyyy, ou vazio.</summary>
    public string PaymentForecast { get; set; } = "";

    /// <summary>Corretores concatenados, ex.: "João Silva - 2,00 TN; Maria Souza - 1,50 TN".</summary>
    public string Commission { get; set; } = "";

    /// <summary>Ex.: "CIF - 45,00" ou "Sem frete".</summary>
    public string Freight { get; set; } = "";

    public string Buyer { get; set; } = "";
}
```

- [ ] **Step 2: Escrever os testes que falham**

Crie `SiagroB1.Application.Tests/Reports/PurchaseContractsByItemReportServiceTests.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using SiagroB1.Application.Tests.Support;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Regras do relatório de contratos de compra por produto e período: quais contratos
/// entram, em que ordem, e como cada célula de texto é montada.
/// </summary>
public class PurchaseContractsByItemReportServiceTests
{
    private static readonly DateTime Jul01 = new(2026, 7, 1);
    private static readonly DateTime Jul31 = new(2026, 7, 31);

    [Fact]
    public async Task BuildRows_IncludesContractCreatedLateOnTheLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(NewContract("CC-1", creationDate: new DateTime(2026, 7, 31, 23, 45, 0)));
        db.Context.PurchaseContracts.Add(NewContract("CC-2", creationDate: new DateTime(2026, 8, 1, 0, 5, 0)));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CC-1" }, rows.Select(r => r.ContractCode).ToArray());
    }

    [Fact]
    public async Task BuildRows_ExcludesCanceledButKeepsDraft()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(NewContract("CC-DRAFT", status: ContractStatus.Draft));
        db.Context.PurchaseContracts.Add(NewContract("CC-CANC", status: ContractStatus.Canceled));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CC-DRAFT" }, rows.Select(r => r.ContractCode).ToArray());
        Assert.Equal("Rascunho", rows[0].Status);
    }

    [Theory]
    [InlineData("ItemCode")]
    [InlineData("HarvestSeasonCode")]
    [InlineData("BranchCode")]
    [InlineData("DeliveryLocationCode")]
    [InlineData("CardCode")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(NewContract("CC-MATCH"));
        db.Context.PurchaseContracts.Add(NewContract(
            "CC-OTHER",
            itemCode: "OUTRO",
            harvestSeasonCode: "OUTRO",
            branchCode: "99",
            deliveryLocationCode: "OUTRO",
            cardCode: "OUTRO"));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var request = Request();
        switch (filter)
        {
            case "ItemCode": request.ItemCode = "10001"; break;
            case "HarvestSeasonCode": request.HarvestSeasonCode = "2026"; break;
            case "BranchCode": request.BranchCode = "01"; break;
            case "DeliveryLocationCode": request.DeliveryLocationCode = "AZ01"; break;
            case "CardCode": request.CardCode = "F001"; break;
        }

        var rows = await CreateService(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "CC-MATCH" }, rows.Select(r => r.ContractCode).ToArray());
    }

    [Fact]
    public async Task BuildRows_DeliveryPeriodMatchesOverlappingWindows()
    {
        var db = TestDb.CreateUnitOfWork();
        // Começa antes e termina dentro da janela: entra.
        db.Context.PurchaseContracts.Add(NewContract(
            "CC-OVERLAP",
            deliveryStart: new DateTime(2026, 7, 20),
            deliveryEnd: new DateTime(2026, 8, 10)));
        // Inteiramente depois da janela: fica de fora.
        db.Context.PurchaseContracts.Add(NewContract(
            "CC-AFTER",
            deliveryStart: new DateTime(2026, 9, 1),
            deliveryEnd: new DateTime(2026, 9, 30)));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var request = Request();
        request.DeliveryFromDate = new DateTime(2026, 8, 1);
        request.DeliveryToDate = new DateTime(2026, 8, 31);

        var rows = await CreateService(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "CC-OVERLAP" }, rows.Select(r => r.ContractCode).ToArray());
    }

    [Fact]
    public async Task BuildRows_ConcatenatesBrokersIntoASingleRow()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = NewContract("CC-1");
        contract.Brokers =
        [
            new PurchaseContractBroker { CardCode = "B1", CardName = "João Silva", Commission = 2m, ComissionUmCode = "TN" },
            new PurchaseContractBroker { CardCode = "B2", CardName = "Maria Souza", Commission = 1.5m, ComissionUmCode = "TN" },
        ];
        db.Context.PurchaseContracts.Add(contract);
        db.Context.PurchaseContracts.Add(NewContract("CC-2"));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal("João Silva - 2,00 TN; Maria Souza - 1,50 TN", rows.Single(r => r.ContractCode == "CC-1").Commission);
        Assert.Equal("", rows.Single(r => r.ContractCode == "CC-2").Commission);
    }

    [Theory]
    [InlineData(FreightTerms.Cif, 45, "CIF - 45,00")]
    [InlineData(FreightTerms.Fob, 45, "FOB - 45,00")]
    [InlineData(FreightTerms.None, 45, "Sem frete")]
    public async Task BuildRows_FormatsFreight(FreightTerms terms, decimal cost, string expected)
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = NewContract("CC-1");
        contract.FreightTerms = terms;
        contract.FreightCostStandard = cost;
        db.Context.PurchaseContracts.Add(contract);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(expected, rows[0].Freight);
    }

    [Fact]
    public async Task BuildRows_OrdersByProductThenCreationDate()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(NewContract("CC-SOJA-2", itemCode: "10001", itemName: "SOJA", creationDate: new DateTime(2026, 7, 20)));
        db.Context.PurchaseContracts.Add(NewContract("CC-MILHO", itemCode: "10002", itemName: "MILHO", creationDate: new DateTime(2026, 7, 5)));
        db.Context.PurchaseContracts.Add(NewContract("CC-SOJA-1", itemCode: "10001", itemName: "SOJA", creationDate: new DateTime(2026, 7, 10)));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var rows = await CreateService(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CC-MILHO", "CC-SOJA-1", "CC-SOJA-2" }, rows.Select(r => r.ContractCode).ToArray());
        Assert.Equal("MILHO (10002)", rows[0].Product);
    }

    [Fact]
    public async Task BuildRows_FormatsRemainingColumns()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        var contract = NewContract("CC-1");
        contract.StandardCashFlowDate = new DateTime(2026, 8, 15);
        contract.FunruralType = FunruralType.Bruto;
        contract.Status = ContractStatus.Approved;
        db.Context.PurchaseContracts.Add(contract);
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var row = (await CreateService(db).BuildRowsAsync(Request()))[0];

        Assert.Equal("01 - MATRIZ", row.Branch);
        Assert.Equal("AZ01 - SILO 1", row.DeliveryLocation);
        Assert.Equal("AGRO SANTA FE", row.Supplier);
        Assert.Equal("Carlos Dias", row.Buyer);
        Assert.Equal("Bruto", row.Funrural);
        Assert.Equal("15/08/2026", row.PaymentForecast);
        Assert.Equal("Aprovado", row.Status);
        Assert.Equal(1500m, row.Quantity);
        Assert.Equal(128.5m, row.Price);
    }

    [Fact]
    public void BuildFiltersDescription_OmitsEmptyFiltersAndUsesRowDescriptions()
    {
        var request = Request();
        request.BranchCode = "01";
        request.DeliveryFromDate = new DateTime(2026, 8, 1);
        request.DeliveryToDate = new DateTime(2026, 9, 30);
        var rows = new List<PurchaseContractsByItemRowDto> { new() { Branch = "01 - MATRIZ" } };

        var text = PurchaseContractsByItemReportService.BuildFiltersDescription(request, rows);

        Assert.Equal(
            "Emissão: 01/07/2026 a 31/07/2026 | Filial: 01 - MATRIZ | Entrega: 01/08/2026 a 30/09/2026",
            text);
    }

    [Fact]
    public void BuildFiltersDescription_WithoutRows_FallsBackToTheRawCode()
    {
        var request = Request();
        request.ItemCode = "10001";

        var text = PurchaseContractsByItemReportService.BuildFiltersDescription(request, []);

        Assert.Equal("Emissão: 01/07/2026 a 31/07/2026 | Produto: 10001", text);
    }

    private static PurchaseContractsByItemReportService CreateService(IUnitOfWork db) =>
        new(db, new StubFastReportService());

    private static PurchaseContractsByItemRequest Request() =>
        new() { FromDate = Jul01, ToDate = Jul31 };

    private static PurchaseContract NewContract(
        string code,
        DateTime? creationDate = null,
        ContractStatus status = ContractStatus.Approved,
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string harvestSeasonCode = "2026",
        string branchCode = "01",
        string deliveryLocationCode = "AZ01",
        string cardCode = "F001",
        DateTime? deliveryStart = null,
        DateTime? deliveryEnd = null) => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CreationDate = creationDate ?? new DateTime(2026, 7, 15),
        Status = status,
        ItemCode = itemCode,
        ItemName = itemName,
        HarvestSeasonCode = harvestSeasonCode,
        BranchCode = branchCode,
        DeliveryLocationCode = deliveryLocationCode,
        DeliveryLocationName = "SILO 1",
        CardCode = cardCode,
        CardName = "AGRO SANTA FE",
        AgentName = "Carlos Dias",
        UnitOfMeasureCode = "TN",
        TotalVolume = 1500m,
        StandardPrice = 128.5m,
        DeliveryStartDate = deliveryStart ?? new DateTime(2026, 8, 1),
        DeliveryEndDate = deliveryEnd ?? new DateTime(2026, 8, 31),
    };

    /// <summary>O teste de linhas não gera PDF; o serviço só precisa de uma dependência válida.</summary>
    private sealed class StubFastReportService : IFastReportService
    {
        public Task<byte[]> GeneratePdfAsync(string reportName, Dictionary<string, object> parameters) =>
            Task.FromResult(Array.Empty<byte>());

        public Task<byte[]> GeneratePdfAsync<T>(
            string reportName,
            ICollection<T> data,
            string dataSourceName,
            string refName,
            Dictionary<string, object> parameters) => Task.FromResult(Array.Empty<byte>());
    }
}
```

Antes de rodar, abra `SiagroB1.Reports/Services/IFastReportService.cs` e confirme que as duas assinaturas do stub batem exatamente com a interface; ajuste o stub se divergirem. (`PurchaseContract.Key` é `Guid`, herdado de `BaseEntity`, e o `ChangeTracker.Clear()` depois de cada `SaveChangesAsync` segue o padrão dos testes já existentes em `SiagroB1.Application.Tests/PurchaseContracts`.)

- [ ] **Step 3: Rodar os testes e confirmar que falham**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend && dotnet test SiagroB1.Application.Tests --filter PurchaseContractsByItemReportServiceTests
```

Esperado: erro de compilação — `PurchaseContractsByItemReportService` não existe.

- [ ] **Step 4: Implementar o serviço**

`SiagroB1.Reports/Services/PurchaseContractsByItemReportService.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Relatório de contratos de compra por produto e período (conferência diária dos negócios).
/// A consulta e toda a formatação de texto ficam aqui; o .frx só desenha.
/// </summary>
public class PurchaseContractsByItemReportService(
    IUnitOfWork db,
    IFastReportService reportService)
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<List<PurchaseContractsByItemRowDto>> BuildRowsAsync(
        PurchaseContractsByItemRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);

        // Branch é tabela local (BRANCHS) e a FK é opcional -> LEFT JOIN, seguro em modo SAPB1.
        var query = db.Context.PurchaseContracts
            .AsNoTracking()
            .Include(x => x.Brokers)
            .Include(x => x.Branch)
            .Where(x => x.Status != ContractStatus.Canceled)
            .Where(x => x.CreationDate >= from && x.CreationDate < toExclusive);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(x => x.ItemCode == request.ItemCode);

        if (!string.IsNullOrWhiteSpace(request.HarvestSeasonCode))
            query = query.Where(x => x.HarvestSeasonCode == request.HarvestSeasonCode);

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(x => x.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.DeliveryLocationCode))
            query = query.Where(x => x.DeliveryLocationCode == request.DeliveryLocationCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(x => x.CardCode == request.CardCode);

        // Período de entrega por SOBREPOSIÇÃO: a janela do contrato precisa cruzar a janela pedida.
        if (request.DeliveryFromDate is { } deliveryFrom)
        {
            var deliveryFromDate = deliveryFrom.Date;
            query = query.Where(x => x.DeliveryEndDate >= deliveryFromDate);
        }

        if (request.DeliveryToDate is { } deliveryTo)
        {
            var deliveryToExclusive = deliveryTo.Date.AddDays(1);
            query = query.Where(x => x.DeliveryStartDate < deliveryToExclusive);
        }

        var contracts = await query.ToListAsync();

        return contracts
            .OrderBy(x => x.ItemName ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.CreationDate)
            .ThenBy(x => x.Code ?? "", StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(PurchaseContractsByItemRequest request)
    {
        var rows = await BuildRowsAsync(request);

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = BuildFiltersDescription(request, rows)
        };

        return await reportService.GeneratePdfAsync(
            "PurchaseContractsByItem.frx",
            rows,
            "PurchaseContractsByItem",
            "PurchaseContractsByItem",
            parameters);
    }

    /// <summary>
    /// Linha de filtros impressa no cabeçalho. As descrições saem das próprias linhas do
    /// resultado — resolver código -> nome consultando ITEMS/WAREHOUSES/BUSINESS_PARTNERS
    /// devolveria vazio em modo SAPB1. Sem resultado, imprime só o código.
    /// </summary>
    public static string BuildFiltersDescription(
        PurchaseContractsByItemRequest request,
        IReadOnlyList<PurchaseContractsByItemRowDto> rows)
    {
        var first = rows.Count > 0 ? rows[0] : null;

        var parts = new List<string>
        {
            $"Emissão: {Date(request.FromDate)} a {Date(request.ToDate)}"
        };

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            parts.Add($"Produto: {Describe(first?.Product, request.ItemCode)}");

        if (!string.IsNullOrWhiteSpace(request.HarvestSeasonCode))
            parts.Add($"Safra: {request.HarvestSeasonCode}");

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            parts.Add($"Filial: {Describe(first?.Branch, request.BranchCode)}");

        if (!string.IsNullOrWhiteSpace(request.DeliveryLocationCode))
            parts.Add($"Local de entrega: {Describe(first?.DeliveryLocation, request.DeliveryLocationCode)}");

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            parts.Add($"Fornecedor: {Describe(first?.Supplier, request.CardCode)}");

        if (request.DeliveryFromDate is { } deliveryFrom && request.DeliveryToDate is { } deliveryTo)
            parts.Add($"Entrega: {Date(deliveryFrom)} a {Date(deliveryTo)}");
        else if (request.DeliveryFromDate is { } onlyFrom)
            parts.Add($"Entrega a partir de: {Date(onlyFrom)}");
        else if (request.DeliveryToDate is { } onlyTo)
            parts.Add($"Entrega até: {Date(onlyTo)}");

        return string.Join(" | ", parts);
    }

    private static PurchaseContractsByItemRowDto ToRow(PurchaseContract contract) => new()
    {
        ItemCode = contract.ItemCode,
        ItemName = contract.ItemName ?? "",
        Product = string.IsNullOrWhiteSpace(contract.ItemName)
            ? contract.ItemCode
            : $"{contract.ItemName} ({contract.ItemCode})",
        ContractCode = contract.Code ?? "",
        Status = DescribeStatus(contract.Status),
        Branch = CodeAndName(
            contract.BranchCode,
            contract.Branch?.ShortName ?? contract.Branch?.BranchName),
        DeliveryLocation = CodeAndName(contract.DeliveryLocationCode, contract.DeliveryLocationName),
        Supplier = string.IsNullOrWhiteSpace(contract.CardName) ? contract.CardCode : contract.CardName,
        Quantity = contract.TotalVolume,
        Price = contract.StandardPrice,
        Funrural = DescribeFunrural(contract.FunruralType),
        PaymentForecast = contract.StandardCashFlowDate is { } forecast ? Date(forecast) : "",
        Commission = DescribeCommission(contract.Brokers),
        Freight = DescribeFreight(contract.FreightTerms, contract.FreightCostStandard),
        Buyer = contract.AgentName ?? "",
    };

    private static string DescribeCommission(IEnumerable<PurchaseContractBroker> brokers) =>
        string.Join("; ", brokers.Select(broker =>
        {
            var name = string.IsNullOrWhiteSpace(broker.CardName) ? broker.CardCode : broker.CardName;
            var unit = string.IsNullOrWhiteSpace(broker.ComissionUmCode) ? "" : $" {broker.ComissionUmCode}";
            return $"{name} - {broker.Commission.ToString("N2", Culture)}{unit}";
        }));

    private static string DescribeFreight(FreightTerms terms, decimal cost) => terms switch
    {
        FreightTerms.Cif => $"CIF - {cost.ToString("N2", Culture)}",
        FreightTerms.Fob => $"FOB - {cost.ToString("N2", Culture)}",
        _ => "Sem frete",
    };

    private static string DescribeStatus(ContractStatus? status) => status switch
    {
        ContractStatus.Draft => "Rascunho",
        ContractStatus.InApproval => "Em aprovação",
        ContractStatus.Approved => "Aprovado",
        ContractStatus.Finished => "Finalizado",
        ContractStatus.Rejected => "Rejeitado",
        ContractStatus.Canceled => "Cancelado",
        _ => "",
    };

    private static string DescribeFunrural(FunruralType? type) => type switch
    {
        FunruralType.Livre => "Livre",
        FunruralType.Bruto => "Bruto",
        _ => "",
    };

    private static string CodeAndName(string? code, string? name) =>
        string.IsNullOrWhiteSpace(name) ? code ?? "" : $"{code} - {name}";

    private static string Describe(string? description, string? fallback) =>
        string.IsNullOrWhiteSpace(description) ? fallback ?? "" : description;

    private static string Date(DateTime value) => value.ToString("dd/MM/yyyy", Culture);
}
```

- [ ] **Step 5: Rodar os testes e confirmar que passam**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend && dotnet test SiagroB1.Application.Tests --filter PurchaseContractsByItemReportServiceTests
```

Esperado: PASS em todos.

- [ ] **Step 6: Checkpoint (sem commit)**

Rode `dotnet build SiagroB1.sln` e liste os 4 arquivos criados. **Não commite** — os commits deste projeto são manuais.

---

### Task 2: Template FastReport e endpoint (backend)

**Files:**
- Create: `siagro-b1-backend/SiagroB1.Reports/Reports/Templates/PurchaseContractsByItem.frx`
- Create: `siagro-b1-backend/SiagroB1.Reports/Controllers/PurchaseContractsByItemController.cs`
- Test: `siagro-b1-backend/SiagroB1.Application.Tests/Reports/PurchaseContractsByItemPdfTests.cs`

**Interfaces:**
- Consumes: `PurchaseContractsByItemReportService.ExecuteAsync(PurchaseContractsByItemRequest)` (Task 1); `FastReportService(IWebHostEnvironment, IConfiguration, ReportHeaderService)`; `TestWebHostEnvironment(string contentRoot)` e `TestLogger<T>` (`SiagroB1.Application.Tests.Support`).
- Produces: endpoint `POST /reports/PurchaseContractsByItem` devolvendo `application/pdf`.

Notas sobre o template:

- O `.frx` é XML escrito à mão. A unidade é pixel a 96 dpi; A4 paisagem com margens padrão dá **1047** de largura útil (`RawPaperSize="9"` + `Landscape="true"`).
- Ele é descoberto automaticamente por `ReportTemplateHeaderTests` e `ReportTemplateRenderSmokeTests` (que varrem a pasta de templates), portanto **precisa** ter o objeto `picLogo` e o parâmetro `pCompanyName`, e precisa preparar/exportar sem dados registrados.
- `BusinessObjectDataSource.Name` e o `refName` passados no serviço são ambos `PurchaseContractsByItem`; os `Column Name` têm que bater exatamente com as propriedades do `PurchaseContractsByItemRowDto`.

- [ ] **Step 1: Escrever o teste de PDF que falha**

`SiagroB1.Application.Tests/Reports/PurchaseContractsByItemPdfTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Gera o PDF de ponta a ponta com o template real. Pega erros que só aparecem na
/// junção serviço + .frx, como fonte de dados com nome divergente (GetDataSource
/// devolvendo null) ou coluna que o FastReport não consegue converter.
/// </summary>
public class PurchaseContractsByItemPdfTests : IDisposable
{
    private readonly string _contentRoot;

    public PurchaseContractsByItemPdfTests()
    {
        FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));
        FastReport.Utils.Config.WebMode = true;

        // FastReportService procura o template em <ContentRoot>/Reports/Templates.
        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-pcbi-pdf", Guid.NewGuid().ToString("N"));
        var templates = Path.Combine(_contentRoot, "Reports", "Templates");
        Directory.CreateDirectory(templates);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "ReportTemplates", "PurchaseContractsByItem.frx"),
            Path.Combine(templates, "PurchaseContractsByItem.frx"));

        var images = Path.Combine(_contentRoot, "wwwroot", "images");
        Directory.CreateDirectory(images);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot", "wwwroot", "images", "logo.png"),
            Path.Combine(images, "logo.png"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ExecuteAsync_ProducesANonEmptyPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        db.Context.PurchaseContracts.Add(new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "CC-1",
            CreationDate = new DateTime(2026, 7, 15),
            Status = ContractStatus.Approved,
            ItemCode = "10001",
            ItemName = "SOJA EM GRÃOS",
            HarvestSeasonCode = "2026",
            BranchCode = "01",
            DeliveryLocationCode = "AZ01",
            DeliveryLocationName = "SILO 1",
            CardCode = "F001",
            CardName = "AGRO SANTA FE",
            AgentName = "Carlos Dias",
            UnitOfMeasureCode = "TN",
            TotalVolume = 1500m,
            StandardPrice = 128.5m,
            FreightTerms = FreightTerms.Cif,
            FreightCostStandard = 45m,
            DeliveryStartDate = new DateTime(2026, 8, 1),
            DeliveryEndDate = new DateTime(2026, 8, 31),
            Brokers = [new PurchaseContractBroker { CardCode = "B1", CardName = "João Silva", Commission = 2m, ComissionUmCode = "TN" }],
        });
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CompanyName"] = "ACME AGRO LTDA",
                ["CompanyLogoPath"] = "wwwroot/images/logo.png"
            })
            .Build();

        var env = new TestWebHostEnvironment(_contentRoot);
        var fastReport = new FastReportService(
            env,
            configuration,
            new ReportHeaderService(env, configuration, new TestLogger<ReportHeaderService>()));

        var pdf = await new PurchaseContractsByItemReportService(db, fastReport)
            .ExecuteAsync(new PurchaseContractsByItemRequest
            {
                FromDate = new DateTime(2026, 7, 1),
                ToDate = new DateTime(2026, 7, 31),
            });

        Assert.NotEmpty(pdf);
    }
}
```

Antes de rodar, abra `SiagroB1.Application.Tests/Support/TestWebHostEnvironment.cs` e `TestLogger.cs` e confirme os construtores; ajuste as chamadas se as assinaturas forem outras. Confirme também em `ReportHeaderService` o nome das chaves de configuração (`CompanyName`, `CompanyLogoPath`) — copie de `ReportTemplateRenderSmokeTests` se divergir.

- [ ] **Step 2: Rodar e confirmar que falha**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend && dotnet test SiagroB1.Application.Tests --filter PurchaseContractsByItemPdfTests
```

Esperado: FAIL — o arquivo `PurchaseContractsByItem.frx` não existe (`FileNotFoundException` no construtor do teste).

- [ ] **Step 3: Criar o template**

`SiagroB1.Reports/Reports/Templates/PurchaseContractsByItem.frx`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Report ScriptLanguage="CSharp" ReportInfo.Created="07/27/2026 09:00:00" ReportInfo.Modified="07/27/2026 09:00:00" ReportInfo.CreatorVersion="2026.1.0.0">
  <Styles>
    <Style Name="EvenRows" Fill.Color="Gainsboro" Font="Arial, 10pt"/>
  </Styles>
  <Dictionary>
    <BusinessObjectDataSource Name="PurchaseContractsByItem" ReferenceName="PurchaseContractsByItem" DataType="System.Int32" Enabled="true">
      <Column Name="ItemCode" DataType="System.String"/>
      <Column Name="ItemName" DataType="System.String"/>
      <Column Name="Product" DataType="System.String"/>
      <Column Name="ContractCode" DataType="System.String"/>
      <Column Name="Status" DataType="System.String"/>
      <Column Name="Branch" DataType="System.String"/>
      <Column Name="DeliveryLocation" DataType="System.String"/>
      <Column Name="Supplier" DataType="System.String"/>
      <Column Name="Quantity" DataType="System.Decimal"/>
      <Column Name="Price" DataType="System.Decimal"/>
      <Column Name="Funrural" DataType="System.String"/>
      <Column Name="PaymentForecast" DataType="System.String"/>
      <Column Name="Commission" DataType="System.String"/>
      <Column Name="Freight" DataType="System.String"/>
      <Column Name="Buyer" DataType="System.String"/>
    </BusinessObjectDataSource>
    <Parameter Name="pCompanyName" DataType="System.String" AsString=""/>
    <Parameter Name="pFilters" DataType="System.String" AsString=""/>
    <Total Name="TotalQtdeGrupo" Expression="[PurchaseContractsByItem.Quantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalContratosGrupo" TotalType="Count" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalQtdeGeral" Expression="[PurchaseContractsByItem.Quantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalContratosGeral" TotalType="Count" Evaluator="Data1" PrintOn="ReportSummary1"/>
  </Dictionary>
  <ReportPage Name="Page1" Landscape="true" RawPaperSize="9" Watermark.Font="Arial, 60pt">
    <PageHeaderBand Name="PageHeader1" Width="1047" Height="113.4">
      <PictureObject Name="picLogo" Left="0" Top="2" Width="94.5" Height="37.8" SizeMode="Zoom"/>
      <TextObject Name="txtCompany" Left="100" Top="2" Width="700" Height="37.8" Text="[pCompanyName]" VertAlign="Center" Font="Arial, 11pt, style=Bold"/>
      <TextObject Name="txtDate" Left="900" Top="2" Width="147" Height="18.9" Text="[Date]" Format="Date" Format.Format="d" HorzAlign="Right" VertAlign="Center" Font="Tahoma, 6pt"/>
      <TextObject Name="txtTitle" Top="45" Width="1047" Height="28.35" Text="Contratos de Compra por Produto" HorzAlign="Center" VertAlign="Center" Font="Arial, 14pt, style=Bold, Italic"/>
      <TextObject Name="txtFilters" Top="75" Width="1047" Height="37.8" Text="[pFilters]" HorzAlign="Center" VertAlign="Top" WordWrap="true" Font="Consolas, 8pt, style=Italic"/>
    </PageHeaderBand>
    <ColumnHeaderBand Name="ColumnHeader1" Top="116.6" Width="1047" Height="18.9">
      <TextObject Name="hContrato" Left="0" Width="65" Height="18.9" Text="Contrato" VertAlign="Bottom" Font="Consolas, 8pt, style=Bold, Italic"/>
      <TextObject Name="hStatus" Left="65" Width="60" Height="18.9" Text="Status" VertAlign="Bottom" Font="Consolas, 8pt, style=Bold, Italic"/>
      <TextObject Name="hFilial" Left="125" Width="85" Height="18.9" Text="Filial" VertAlign="Bottom" Font="Consolas, 8pt, style=Bold, Italic"/>
      <TextObject Name="hLocal" Left="210" Width="105" Height="18.9" Text="Local entrega" VertAlign="Bottom" Font="Consolas, 8pt, style=Bold, Italic"/>
      <TextObject Name="hFornecedor" Left="315" Width="140" Height="18.9" Text="Fornecedor" VertAlign="Bottom" Font="Consolas, 8pt, style=Bold, Italic"/>
      <TextObject Name="hQtde" Left="455" Width="70" Height="18.9" Text="Qtde" HorzAlign="Right" VertAlign="Bottom" Font="Consolas, 8pt, style=Bold, Italic"/>
      <TextObject Name="hPreco" Left="525" Width="60" Height="18.9" Text="Preço" HorzAlign="Right" VertAlign="Bottom" Font="Consolas, 8pt, style=Bold, Italic"/>
      <TextObject Name="hFunrural" Left="585" Width="45" Height="18.9" Text="Funrural" VertAlign="Bottom" Font="Consolas, 8pt, style=Bold, Italic"/>
      <TextObject Name="hPrevPagto" Left="630" Width="60" Height="18.9" Text="Prev.Pagto" VertAlign="Bottom" Font="Consolas, 8pt, style=Bold, Italic"/>
      <TextObject Name="hComissao" Left="690" Width="150" Height="18.9" Text="Comissão" VertAlign="Bottom" Font="Consolas, 8pt, style=Bold, Italic"/>
      <TextObject Name="hFrete" Left="840" Width="80" Height="18.9" Text="Frete" VertAlign="Bottom" Font="Consolas, 8pt, style=Bold, Italic"/>
      <TextObject Name="hComprador" Left="920" Width="127" Height="18.9" Text="Comprador" VertAlign="Bottom" Font="Consolas, 8pt, style=Bold, Italic"/>
    </ColumnHeaderBand>
    <GroupHeaderBand Name="GroupHeader1" Top="138.7" Width="1047" Height="22.05" Condition="[PurchaseContractsByItem.Product]">
      <TextObject Name="txtProduct" Left="0" Top="2" Width="600" Height="18.9" Text="[PurchaseContractsByItem.Product]" VertAlign="Center" Font="Arial, 9pt, style=Bold"/>
      <DataBand Name="Data1" Top="163.95" Width="1047" Height="18.9" CanGrow="true" EvenStyle="EvenRows" DataSource="PurchaseContractsByItem">
        <TextObject Name="dContrato" Left="0" Width="65" Height="18.9" Text="[PurchaseContractsByItem.ContractCode]" VertAlign="Center" Font="Consolas, 8pt"/>
        <TextObject Name="dStatus" Left="65" Width="60" Height="18.9" Text="[PurchaseContractsByItem.Status]" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt" Trimming="EllipsisCharacter"/>
        <TextObject Name="dFilial" Left="125" Width="85" Height="18.9" Text="[PurchaseContractsByItem.Branch]" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt" Trimming="EllipsisCharacter"/>
        <TextObject Name="dLocal" Left="210" Width="105" Height="18.9" Text="[PurchaseContractsByItem.DeliveryLocation]" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt" Trimming="EllipsisCharacter"/>
        <TextObject Name="dFornecedor" Left="315" Width="140" Height="18.9" Text="[PurchaseContractsByItem.Supplier]" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt" Trimming="EllipsisCharacter"/>
        <TextObject Name="dQtde" Left="455" Width="70" Height="18.9" Text="[PurchaseContractsByItem.Quantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt"/>
        <TextObject Name="dPreco" Left="525" Width="60" Height="18.9" Text="[PurchaseContractsByItem.Price]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="2" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt"/>
        <TextObject Name="dFunrural" Left="585" Width="45" Height="18.9" Text="[PurchaseContractsByItem.Funrural]" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt"/>
        <TextObject Name="dPrevPagto" Left="630" Width="60" Height="18.9" Text="[PurchaseContractsByItem.PaymentForecast]" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt"/>
        <TextObject Name="dComissao" Left="690" Width="150" Height="18.9" Text="[PurchaseContractsByItem.Commission]" VertAlign="Top" WordWrap="true" CanGrow="true" Font="Consolas, 7pt"/>
        <TextObject Name="dFrete" Left="840" Width="80" Height="18.9" Text="[PurchaseContractsByItem.Freight]" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt"/>
        <TextObject Name="dComprador" Left="920" Width="127" Height="18.9" Text="[PurchaseContractsByItem.Buyer]" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt" Trimming="EllipsisCharacter"/>
      </DataBand>
      <GroupFooterBand Name="GroupFooter1" Top="186.05" Width="1047" Height="22.05">
        <TextObject Name="fSubtotalLabel" Left="210" Top="2" Width="180" Height="18.9" Text="Subtotal do produto:" HorzAlign="Right" VertAlign="Center" Font="Consolas, 8pt, style=Bold, Italic"/>
        <TextObject Name="fSubtotalCount" Left="390" Top="2" Width="65" Height="18.9" Text="[TotalContratosGrupo]" HorzAlign="Right" VertAlign="Center" Font="Consolas, 8pt, style=Bold, Italic"/>
        <TextObject Name="fSubtotalQtde" Left="455" Top="2" Width="70" Height="18.9" Text="[TotalQtdeGrupo]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" Font="Consolas, 8pt, style=Bold, Italic"/>
      </GroupFooterBand>
    </GroupHeaderBand>
    <ReportSummaryBand Name="ReportSummary1" Top="211.3" Width="1047" Height="28.35">
      <TextObject Name="sTotalLabel" Left="210" Top="4" Width="180" Height="18.9" Text="TOTAL GERAL:" HorzAlign="Right" VertAlign="Center" Font="Consolas, 9pt, style=Bold"/>
      <TextObject Name="sTotalCount" Left="390" Top="4" Width="65" Height="18.9" Text="[TotalContratosGeral]" HorzAlign="Right" VertAlign="Center" Font="Consolas, 9pt, style=Bold"/>
      <TextObject Name="sTotalQtde" Left="455" Top="4" Width="70" Height="18.9" Text="[TotalQtdeGeral]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" Font="Consolas, 9pt, style=Bold"/>
    </ReportSummaryBand>
    <PageFooterBand Name="PageFooter1" Top="242.85" Width="1047" Height="28.35">
      <TextObject Name="txtPage" Left="900" Width="147" Height="18.9" Text="Página [Page#] de [TotalPages#]" HorzAlign="Right" VertAlign="Center" Font="Consolas, 7pt"/>
    </PageFooterBand>
  </ReportPage>
</Report>
```

- [ ] **Step 4: Rodar o teste de PDF e os testes de template**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend && dotnet test SiagroB1.Application.Tests --filter "PurchaseContractsByItemPdfTests|ReportTemplateHeaderTests|ReportTemplateRenderSmokeTests"
```

Esperado: PASS em todos, inclusive nos dois testes de template que agora varrem também o `.frx` novo. Se `EveryTemplate_PreparesAndExportsToPdf` falhar para `PurchaseContractsByItem.frx`, o problema está no template (banda, total ou coluna), não no serviço.

- [ ] **Step 5: Criar o controller**

`SiagroB1.Reports/Controllers/PurchaseContractsByItemController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/PurchaseContractsByItem")]
public class PurchaseContractsByItemController(
    PurchaseContractsByItemReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] PurchaseContractsByItemRequest request)
    {
        if (request.FromDate == default || request.ToDate == default)
            return BadRequest("Informe o período de emissão.");

        if (request.ToDate.Date < request.FromDate.Date)
            return BadRequest("A data final da emissão não pode ser anterior à inicial.");

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"purchase-contracts-by-item.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

O DI de `SiagroB1.Reports` registra por convenção tudo que termina em `Service` (ver `DI/ServiceCollectionExtensions.cs`), então **não há registro manual a fazer**.

- [ ] **Step 6: Build e checkpoint (sem commit)**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend && dotnet build SiagroB1.sln
```

Esperado: build sem erros. Liste os 3 arquivos criados. **Não commite.**

---

### Task 3: Tela de filtros (frontend)

**Repositório: `C:\Projetos\SiagroB1\siagro-b1-frontend`.**

**Files:**
- Create: `webapp/view/reports/purchaseContractsByItem/Main.view.xml`
- Create: `webapp/controller/reports/purchaseContractsByItem/BaseController.ts`
- Create: `webapp/controller/reports/purchaseContractsByItem/Main.controller.ts`
- Modify: `webapp/manifest.json` (uma entrada em `routes`, uma em `targets`)
- Modify: `webapp/model/ServerRoutes.ts`

**Interfaces:**
- Consumes: endpoint `POST /reports/PurchaseContractsByItem` (Task 2); helpers de `controller/BaseController.ts` — `setBusy(boolean)`, `validateForm(sFormId?)`, `clearStates(sFormId)`, `getRouter()`, `getModel(name)`; value helps de `controller/common/CommonController.ts` — `openItemValueHelp`, `openHarvestSeasonsValueHelp`, `openBranchsValueHelp`, `openWarehouseValueHelp`, `openSuppliersValueHelp`.
- Produces: rota `purchaseContractsByItemReport` (consumida pela migration de menu na Task 4).

- [ ] **Step 1: Criar a view**

`webapp/view/reports/purchaseContractsByItem/Main.view.xml`:

```xml
<mvc:View
	controllerName="siagrob1.controller.reports.purchaseContractsByItem.Main"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form">
  <Page title="Relatório de Contratos de Compra por Produto">
    <f:SimpleForm
      id="purchaseContractsByItemForm"
      editable="true"
      layout="ResponsiveGridLayout"
      labelSpanXL="4"
      labelSpanL="4"
      labelSpanM="4"
      labelSpanS="12"
      adjustLabelSpan="false"
      emptySpanXL="0"
      emptySpanL="0"
      emptySpanM="0"
      emptySpanS="0"
      columnsXL="3"
      columnsL="2"
      columnsM="2"
      singleContainerFullSize="true"
      busyIndicatorDelay="0">
        <f:content>
          <core:Title text="Período de emissão" emphasized="true" />
          <Label text="Emissão de"/>
          <DatePicker
                value="{
                  path: 'params>/FromDate',
                  type: 'sap.ui.model.odata.type.DateTimeOffset',
                  constraints: { precision: 7 },
                  formatOptions: { pattern: 'dd/MM/yyyy' }
                }"
                liveChange=".validateField"
                required="true"
            />
          <Label text="Emissão até"/>
          <DatePicker
                value="{
                  path: 'params>/ToDate',
                  type: 'sap.ui.model.odata.type.DateTimeOffset',
                  constraints: { precision: 7 },
                  formatOptions: { pattern: 'dd/MM/yyyy' }
                }"
                liveChange=".validateField"
                required="true"
            />

          <core:Title text="Filtros" emphasized="true" />
          <Label text="Produto" />
          <Input
            showValueHelp="true"
            valueHelpOnly="true"
            valueHelpRequest=".openItemValueHelp"
            value="{params>/ItemCode}">
            <customData>
              <core:CustomData key="descriptionProperty" value="ItemName" />
            </customData>
          </Input>
          <Input value="{params>/ItemName}" editable="false" />
          <Label text="Safra" />
          <Input
            showValueHelp="true"
            valueHelpOnly="true"
            valueHelpRequest=".openHarvestSeasonsValueHelp"
            value="{params>/HarvestSeasonCode}" />
          <Label text="Filial" />
          <Input
            showValueHelp="true"
            valueHelpOnly="true"
            valueHelpRequest=".openBranchsValueHelp"
            value="{params>/BranchCode}">
            <customData>
              <core:CustomData key="descriptionProperty" value="BranchName:ShortName" />
            </customData>
          </Input>
          <Input value="{params>/BranchName}" editable="false" />
          <Label text="Local de entrega" />
          <Input
            showValueHelp="true"
            valueHelpOnly="true"
            valueHelpRequest=".openWarehouseValueHelp"
            value="{params>/DeliveryLocationCode}">
            <customData>
              <core:CustomData key="descriptionProperty" value="DeliveryLocationName:Name" />
            </customData>
          </Input>
          <Input value="{params>/DeliveryLocationName}" editable="false" />
          <Label text="Fornecedor" />
          <Input
            showValueHelp="true"
            valueHelpOnly="true"
            valueHelpRequest=".openSuppliersValueHelp"
            value="{params>/CardCode}">
            <customData>
              <core:CustomData key="descriptionProperty" value="CardName" />
            </customData>
          </Input>
          <Input value="{params>/CardName}" editable="false" />

          <core:Title text="Período de entrega" emphasized="true" />
          <Label text="Entrega de"/>
          <DatePicker
                value="{
                  path: 'params>/DeliveryFromDate',
                  type: 'sap.ui.model.odata.type.DateTimeOffset',
                  constraints: { precision: 7 },
                  formatOptions: { pattern: 'dd/MM/yyyy' }
                }"
            />
          <Label text="Entrega até"/>
          <DatePicker
                value="{
                  path: 'params>/DeliveryToDate',
                  type: 'sap.ui.model.odata.type.DateTimeOffset',
                  constraints: { precision: 7 },
                  formatOptions: { pattern: 'dd/MM/yyyy' }
                }"
            />
        </f:content>
    </f:SimpleForm>
    <footer>
      <OverflowToolbar>
        <ToolbarSpacer/>
        <Button text="Imprimir" type="Emphasized" press=".onPrintReport"/>
      </OverflowToolbar>
    </footer>
  </Page>
</mvc:View>
```

O `descriptionProperty` no `customData` é o mecanismo padrão do projeto para preencher a descrição ao lado do código (funciona em modelo JSON também) — nunca use formatter assíncrono para isso.

- [ ] **Step 2: Criar os controllers**

`webapp/controller/reports/purchaseContractsByItem/BaseController.ts`:

```typescript
import CommonController from "siagrob1/controller/common/CommonController";

export default abstract class BaseController extends CommonController {

}
```

`webapp/controller/reports/purchaseContractsByItem/Main.controller.ts`:

```typescript
import JSONModel from "sap/ui/model/json/JSONModel";
import MessageBox from "sap/m/MessageBox";
import BaseController from "./BaseController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * @namespace siagrob1.controller.reports.purchaseContractsByItem
 */
export default class Main extends BaseController {

	onInit(): void {
		const paramsModel = new JSONModel();
		this.getView().setModel(paramsModel, "params");

		this.getRouter()
			.getRoute("purchaseContractsByItemReport")
			.attachPatternMatched(() => this.routeMatched());
	}

	private routeMatched() {
		this.clearStates("purchaseContractsByItemForm");

		const paramsModel = this.getModel("params") as JSONModel;
		paramsModel.setData({});
	}

	async onPrintReport() {
		if (!this.validateForm("purchaseContractsByItemForm")) {
			MessageBox.warning("Por favor, preencha corretamente todos os campos obrigatórios.");
			return;
		}

		const paramsModel = this.getModel("params") as JSONModel;
		const payload = paramsModel.getData() as object;

		try {
			this.setBusy(true);

			const response = await fetch(ServerRoutes.purchaseContractsByItemReport, {
				method: "POST",
				headers: {
					"Content-Type": "application/json"
				},
				body: JSON.stringify(payload)
			});

			if (!response.ok) {
				throw new Error("Falha ao gerar relatório.");
			}

			const blob = await response.blob();
			const fileURL = URL.createObjectURL(blob);

			window.open(fileURL, "_blank");

			setTimeout(() => URL.revokeObjectURL(fileURL), 60000);
		} catch (error) {
			const err = error as Error;
			MessageBox.error(err?.message);
		} finally {
			this.setBusy(false);
		}
	}
}
```

As descrições (`ItemName`, `BranchName`, `DeliveryLocationName`, `CardName`) vão junto no payload e são simplesmente ignoradas pelo `PurchaseContractsByItemRequest` no backend — não há nada a limpar antes de enviar.

- [ ] **Step 3: Registrar a rota no `webapp/manifest.json`**

Em `routes`, logo depois do bloco de `storageAddressesBalanceReport`, insira:

```json
        {
          "pattern": "purchase-contracts-by-item/report",
          "name": "purchaseContractsByItemReport",
          "target": "purchaseContractsByItemReport"
        },
```

Em `targets`, logo depois do bloco `"storageAddressesBalanceReport": { ... },`, insira:

```json
        "purchaseContractsByItemReport": {
          "id": "purchaseContractsByItemReport",
          "level": 1,
          "name": "siagrob1.view.reports.purchaseContractsByItem.Main",
          "clearControlAggregation": true
        },
```

- [ ] **Step 4: Registrar o endpoint em `webapp/model/ServerRoutes.ts`**

Junto das outras entradas de relatório (procure por `prePurchaseContractReport`), adicione:

```typescript
  purchaseContractsByItemReport: '/reports/PurchaseContractsByItem',
```

- [ ] **Step 5: Rodar os gates do frontend**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-frontend && yarn ts-typecheck && yarn lint && yarn ui5lint
```

Esperado: sem erros. Se `yarn lint` reclamar de `void` em promessa flutuante no `attachPatternMatched`, siga o padrão dos outros relatórios (`() => this.routeMatched()` já é síncrono, então não deve ocorrer).

- [ ] **Step 6: Checkpoint (sem commit)**

Liste os 3 arquivos criados e os 2 modificados. **Não commite.**

---

### Task 4: Migration do item de menu (backend)

Sem isso a tela existe mas não é alcançável — ninguém consegue abrir o relatório.

**Files:**
- Create: `siagro-b1-backend/SiagroB1.Migrations/CommonContext/<timestamp>_AddPurchaseContractsByItemReportMenu.cs` (+ `.Designer.cs` gerado)

**Interfaces:**
- Consumes: o nome de rota `purchaseContractsByItemReport` criado na Task 3 — a `Key` do `MENU_ITEMS` **tem** que ser idêntica, porque `App.controller.ts` navega com `navTo(item.getKey())`.

- [ ] **Step 1: Gerar o esqueleto da migration**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend && dotnet ef migrations add AddPurchaseContractsByItemReportMenu --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir CommonContext
```

Isso cria os dois arquivos com `Up`/`Down` vazios (não há mudança de schema — é só seed).

- [ ] **Step 2: Preencher `Up`/`Down` à mão**

No arquivo `<timestamp>_AddPurchaseContractsByItemReportMenu.cs`, substitua o corpo da classe por:

```csharp
        /// <summary>
        /// Menu do relatório de contratos de compra por produto e período, no grupo "Relatórios".
        ///
        /// A Key PRECISA ser igual ao nome da rota no manifest.json do frontend:
        /// App.controller.ts navega com navTo(item.getKey()). Sem o vínculo em ROLE_MENUS o
        /// item não aparece para ninguém.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns:
                [
                    "Key",
                    "Title",
                    "Icon",
                    "Enabled",
                    "Expanded",
                    "Order",
                    "ParentKey"
                ],
                values: new object[,]
                {
                    {
                        "purchaseContractsByItemReport", "Contratos de Compra por Produto",
                        "sap-icon://folder-blank", true, false, 5, "reports"
                    },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns:
                [
                    "Id",
                    "RoleCode",
                    "MenuItemKey"
                ],
                values: new object[,]
                {
                    {
                        "9C4B1E27-6D3A-4F58-8E70-1B5A2C9D6E40", "ADMIN",
                        "purchaseContractsByItemReport"
                    },
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValue: "9C4B1E27-6D3A-4F58-8E70-1B5A2C9D6E40");

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: "purchaseContractsByItemReport");
        }
```

Use `20260723143444_AddSalesPriceFixationApprovalMenu.cs` como referência de formatação. Confirme que o `Order = 5` não colide com outro item filho de `reports` (`InitialMenuSeed` usa 1 a 4); se já houver um 5, use o próximo livre.

- [ ] **Step 3: Conferir o alvo ANTES de aplicar**

⚠️ O perfil/ambiente padrão de migration pode apontar para **produção**. Antes de rodar o update, leia a connection string do ambiente que vai usar:

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend && cat SiagroB1.Web/appsettings.Yokotobi.json
```

Confirme com o usuário que é o banco local/de teste esperado antes de seguir.

- [ ] **Step 4: Aplicar a migration com o ambiente explícito**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend && ASPNETCORE_ENVIRONMENT=Yokotobi dotnet ef database update --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Esperado: `Done.` Nunca rode sem `ASPNETCORE_ENVIRONMENT` explícito.

- [ ] **Step 5: Verificar o seed**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend && dotnet build SiagroB1.sln
```

E confirme na base que existe uma linha em `MENU_ITEMS` com `Key = 'purchaseContractsByItemReport'` e outra em `ROLE_MENUS` ligando-a ao `ADMIN`.

- [ ] **Step 6: Checkpoint (sem commit)**

Liste os arquivos de migration criados. **Não commite.**

---

### Task 5: Verificação pelo caminho do usuário

Relatório pronto = o usuário loga, acha no menu e vê o PDF. `curl` no endpoint não conta.

**Files:** nenhum (só verificação).

- [ ] **Step 1: Subir o backend**

Em dois terminais, a partir de `C:\Projetos\SiagroB1\siagro-b1-backend`:

```bash
dotnet run --project SiagroB1.Web --launch-profile yktb
dotnet run --project SiagroB1.Gateway --launch-profile yktb
dotnet run --project SiagroB1.Reports --launch-profile dev
```

(O perfil `yktb` usa o ambiente `Yokotobi`. Se o nome do perfil divergir, abra `Properties/launchSettings.json` do projeto e use o perfil cujo `ASPNETCORE_ENVIRONMENT` é `Yokotobi`.)

- [ ] **Step 2: Subir o frontend**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-frontend && yarn start:dev
```

- [ ] **Step 3: Percorrer o caminho do usuário**

1. Abra `http://localhost:8080` e faça login (`admin` / `1234`).
2. No menu lateral, abra **Relatórios** → **Contratos de Compra por Produto**. Se o item não aparecer, o problema está na Task 4 (menu) — não siga adiante.
3. Preencha "Emissão de/até" cobrindo um período com contratos e clique **Imprimir**.
4. Confirme na nova aba: logo e nome da empresa, título, **linha de filtros aplicados**, quebra por produto com subtotal, total geral, e as 12 colunas com Contrato e Status preenchidos.
5. Repita com um filtro opcional (ex.: só uma filial) e confirme que a linha de filtros no cabeçalho reflete a escolha e que o resultado diminuiu.
6. Repita informando só o período de entrega e confirme que aparece contrato cuja janela de entrega apenas cruza o período (não precisa estar contida nele).

- [ ] **Step 4: Rodar a suíte de testes completa**

```bash
cd /c/Projetos/SiagroB1/siagro-b1-backend && dotnet test SiagroB1.Application.Tests
```

Esperado: toda a suíte verde, não só os testes novos.

- [ ] **Step 5: Relatar**

Descreva ao usuário o que foi verificado visualmente e o que não foi. Se algum passo do Step 3 não pôde ser executado (por exemplo, sem dados no período), diga explicitamente em vez de declarar pronto. **Os commits ficam com o usuário.**

---

## Correções aplicadas durante a execução (27/07/2026)

O template deste plano estava errado em dois pontos, descobertos só na verificação
visual do PDF. O arquivo `.frx` no repositório já está corrigido — em caso de dúvida,
ele é a fonte da verdade, não o bloco de código da Task 2:

1. **`Landscape="true"` sozinho não vira a página.** O FastReport manteve A4 retrato e
   tudo além de ~718 unidades (colunas Frete e Comprador) saiu cortado. É obrigatório
   declarar também `PaperWidth="297" PaperHeight="210"`, como fazem
   `PurchaseContracts.frx` e `StorageAddressesBalance.frx`. Com margens de 5 mm a
   largura útil passa a **1084** unidades (não 1047).
2. **Cabeçalhos e valores truncados.** Todos os `TextObject` de cabeçalho e de dados
   precisam de `WordWrap="false"` — sem isso o texto quebra em duas linhas e invade a
   banda de cima. As larguras finais também foram rebalanceadas (Qtde 88 para caber
   "10.000.000,000"; Status 92 para caber "Em aprovação").

Nenhum dos dois é detectável pelos testes automatizados: `ReportTemplateRenderSmokeTests`
prova que o template prepara e exporta, não que o layout cabe na página.

## Ordem e dependências

Task 1 → Task 2 → Task 3 → Task 4 → Task 5. A Task 3 (frontend) só depende do *nome* do endpoint, e a Task 4 só depende do *nome* da rota — ambos estão fixados nas Global Constraints, então em caso de execução paralela não há ambiguidade.
