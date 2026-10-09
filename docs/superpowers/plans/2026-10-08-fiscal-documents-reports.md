# Relatórios de Documentos Fiscais (pacote 1) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cinco relatórios PDF (notas de saída/entrada por período, itens de saída/entrada, devoluções de venda) que funcionam em SAPB1 e STANDALONE, com tela de filtros e item de menu.

**Architecture:** Cada relatório segue o padrão de `SalesContractsByItem` no serviço `SiagroB1.Reports`: controller `POST /reports/<Nome>` → `<Nome>ReportService.BuildRowsAsync` (EF, só tabelas locais e snapshots do documento; agregação em C#) → `IFastReportService.GeneratePdfAsync` com um `.frx`. Em SAPB1 o serviço passa `pStandalone = false` e o `FastReportService` esconde os objetos do template cujo nome começa com `fiscal`. Frontend: uma tela UI5 por relatório, compartilhando um controller-base de impressão.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), FastReport.OpenSource 2026.1.3, xUnit; OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-08-fiscal-documents-reports-design.md`

## Global Constraints

- Branch `feature/fiscal-documents-reports` nos **dois** repos (`siagro-b1-backend`, `siagro-b1-frontend`); confira `git branch --show-current` antes de cada commit. Nunca push, nunca merge.
- Todo arquivo novo: `git add <path>` logo após criar.
- Commits no formato do `siagro-b1-backend/CLAUDE.md`: `tipo(escopo): descrição pt-BR`; escopo `reports`; migration exige trailer `DB: <NomeDaMigration>`. Rodapé `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.
- Identificadores em inglês; texto que o usuário lê (tela, PDF, mensagens) em pt-BR.
- Relatório **nunca** faz JOIN com `ITEMS`, `BUSINESS_PARTNERS`, `WAREHOUSES` (vazias em SAPB1). Parceiro/produto/UM/CFOP/natureza/tributos vêm do snapshot do documento; `Branch` e contratos por LEFT JOIN (FK opcional).
- Saída só PDF.
- Situação padrão (lista vazia) = Pendente, Confirmado, Retornado (sem Cancelado). `SalesInvoice.InvoiceStatus` nulo conta como Pendente.
- Período: fim exclusivo `ToDate.Date.AddDays(1)`. Saída filtra `InvoiceDate`; entrada `IssueDate`.
- Todo `.frx` novo precisa de `picLogo` (PictureObject) e do parâmetro `pCompanyName` — `ReportTemplateHeaderTests` descobre todos os templates da pasta automaticamente.
- Objetos fiscais do `.frx` (cabeçalho, célula, subtotal, total das colunas ICMS/PIS/COFINS/IBS-CBS/Tributos/Sit. NF-e) têm `Name` começando com `fiscal` e ficam na ponta direita da página.
- Rodar testes: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"` a partir de `siagro-b1-backend/`.

## Review Focus

1. **Documento sem número de NF / sem série** (≈54% das saídas da Yokotobi) — coluna NF/Série vazia, sem "/" solto. Teste em Task 1 (`DocumentNumber`).
2. **Saída com `InvoiceStatus` nulo** (registros antigos) — tratada como Pendente, entra no padrão. Teste em Task 2.
3. **Item de entrada sem `ItemCode`** (XML importado sem vínculo) — agrupa em "Sem produto vinculado", não some. Teste em Task 5.
4. **Devolução sem vínculo com a nota original** — linha impressa com "—", não é descartada nem quebra. Teste em Task 6.
5. **Mesmo produto com UMs diferentes no grupo** — subtotal de quantidade não pode somar KG com TN. O grupo é por produto **e** UM. Teste em Task 4.

---

## Appendix A — Esqueleto de template `.frx`

Todos os templates novos partem de uma **cópia** de `SiagroB1.Reports/Reports/Templates/SalesContractsByItem.frx` (paisagem A4, `PageHeader1` com `picLogo`/`pCompanyName`/título/`pFilters`, `ColumnHeader1`, `GroupHeader1`, `Data1`, `GroupFooter1`, `ReportSummary1`, `PageFooter1`). Para cada template novo:

1. Copie o arquivo para o nome novo.
2. No `<Dictionary>`, troque o `BusinessObjectDataSource` `Name`/`ReferenceName` `SalesContractsByItem` pelo nome do relatório e substitua as `<Column>` pelas propriedades do RowDto (uma `<Column Name="X" DataType="System.String|System.Decimal|System.Int32"/>` por propriedade).
3. Mantenha os parâmetros `pCompanyName` e `pFilters`; acrescente `<Parameter Name="pStandalone" DataType="System.Boolean" AsString="true"/>`.
4. Troque o texto do título no `PageHeader1` pelo título do relatório.
5. Refaça `ColumnHeader1`/`Data1` com as colunas listadas na task, na ordem e larguras dadas (soma ≤ 1084 px em paisagem; 718 px em retrato — retrato: `Landscape="false" PaperWidth="210" PaperHeight="297"` e bandas com `Width="718"`). Números: `Format="Number" Format.UseLocale="true" Format.DecimalDigits="2"` (quantidade: 3), `HorzAlign="Right"`. Todo texto de célula: `WordWrap="false"` + `Trimming="EllipsisCharacter"`.
6. Objetos das colunas fiscais: `Name` com prefixo `fiscal` (ex.: `fiscalHdrIcms`, `fiscalIcms`, `fiscalGrpIcms`, `fiscalSumIcms`), posicionados à direita.
7. `Total`s (`<Total Name=... Expression="[<Fonte>.<Coluna>]" Evaluator="Data1" PrintOn="GroupFooter1|ReportSummary1"/>`) para cada coluna somada; `TotalType="Count"` para a contagem.
8. Relatórios sem agrupamento: remova `GroupHeader1`/`GroupFooter1` e mova `Data1` para filho direto da página.
9. Resultado vazio: com lista vazia o FastReport imprime só cabeçalho e sumário. No `ReportSummary1`, um `TextObject` `txtEmpty` com `Text="[IIf([TotalCountAll] == 0, &quot;Nenhum registro encontrado.&quot;, &quot;&quot;)]"`, onde `TotalCountAll` é o `Total` de contagem (`TotalType="Count"`) impresso no sumário.

Valide abrindo o PDF gerado pelo teste de PDF da task (o arquivo também é gravado em `%TEMP%/siagro-invoice-reports/<Nome>-<modo>.pdf` pelo teste — veja Task 2, Step 7) e conferindo colunas cortadas.

---

### Task 1: Base comum — request, textos e ocultação fiscal

**Files:**
- Create: `SiagroB1.Reports/Dtos/InvoiceReportRequest.cs`
- Create: `SiagroB1.Reports/Helpers/InvoiceReportText.cs`
- Modify: `SiagroB1.Reports/Services/FastReportService.cs`
- Create: `SiagroB1.Application.Tests/Support/RecordingFastReportService.cs`
- Create: `SiagroB1.Application.Tests/Support/TestConfiguration.cs`
- Test: `SiagroB1.Application.Tests/Reports/InvoiceReportTextTests.cs`
- Test: `SiagroB1.Application.Tests/Reports/FastReportServiceFiscalTests.cs`

**Interfaces:**
- Produces:
  - `abstract class InvoiceReportRequest { DateTime FromDate; DateTime ToDate; string? BranchCode; string? CardCode; string? ItemCode; List<InvoiceStatus>? Statuses; List<NfeStatus>? NfeStatuses; }`
  - `static class InvoiceReportText` com `string? Validate(InvoiceReportRequest)`, `InvoiceStatus[] EffectiveStatuses(IReadOnlyCollection<InvoiceStatus>?)`, `string Status(InvoiceStatus?)`, `string Nfe(NfeStatus)`, `string DocumentNumber(string?, string?)`, `string Partner(string?, string?)`, `string Product(string?, string?)`, `string BranchName(Branch?, string?)`, `string Date(DateTime?)`, `string BuildFilters(InvoiceReportRequest, bool standalone, string partnerLabel, string? partnerName, string? productName, string? branchName, IEnumerable<string> extra)`, const `NoOrigin = "—"`, const `NoProduct = "Sem produto vinculado"`.
  - `FastReportService.StandaloneParameter = "pStandalone"`, `static void FastReportService.HideFiscalObjects(Report)`.
  - Test support: `RecordingFastReportService : IFastReportService` (expõe `LastReportName`, `LastParameters`, `LastData` como `object?`), `TestConfiguration.Erp(string mode) : IConfiguration`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Reports/InvoiceReportTextTests.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Application.Tests.Reports;

public class InvoiceReportTextTests
{
    private sealed class Request : InvoiceReportRequest;

    [Theory]
    [InlineData("123", "1", "123/1")]
    [InlineData("123", null, "123")]
    [InlineData("123", " ", "123")]
    [InlineData(null, "1", "")]
    [InlineData("", "", "")]
    public void DocumentNumber_NeverLeavesALooseSlash(string? number, string? series, string expected) =>
        Assert.Equal(expected, InvoiceReportText.DocumentNumber(number, series));

    [Fact]
    public void EffectiveStatuses_EmptyMeansEverythingButCancelled() =>
        Assert.Equal(
            new[] { InvoiceStatus.Pending, InvoiceStatus.Confirmed, InvoiceStatus.Returned },
            InvoiceReportText.EffectiveStatuses(null));

    [Fact]
    public void EffectiveStatuses_KeepsAnExplicitChoice() =>
        Assert.Equal(
            new[] { InvoiceStatus.Cancelled },
            InvoiceReportText.EffectiveStatuses([InvoiceStatus.Cancelled]));

    [Theory]
    [InlineData(null, "Pendente")]
    [InlineData(InvoiceStatus.Pending, "Pendente")]
    [InlineData(InvoiceStatus.Confirmed, "Confirmado")]
    [InlineData(InvoiceStatus.Cancelled, "Cancelado")]
    [InlineData(InvoiceStatus.Returned, "Retornado")]
    public void Status_TranslatesToPtBr(InvoiceStatus? status, string expected) =>
        Assert.Equal(expected, InvoiceReportText.Status(status));

    [Theory]
    [InlineData(NfeStatus.None, "")]
    [InlineData(NfeStatus.Processing, "Em processamento")]
    [InlineData(NfeStatus.Authorized, "Autorizada")]
    [InlineData(NfeStatus.Rejected, "Rejeitada")]
    [InlineData(NfeStatus.Denied, "Denegada")]
    [InlineData(NfeStatus.Cancelled, "Cancelada")]
    [InlineData(NfeStatus.Voided, "Inutilizada")]
    public void Nfe_TranslatesToPtBr(NfeStatus status, string expected) =>
        Assert.Equal(expected, InvoiceReportText.Nfe(status));

    [Theory]
    [InlineData("C001", "COOPERATIVA", "(C001) COOPERATIVA")]
    [InlineData("C001", null, "C001")]
    [InlineData(null, "COOPERATIVA", "COOPERATIVA")]
    [InlineData(null, null, "")]
    public void Partner_PrefixesTheCode(string? code, string? name, string expected) =>
        Assert.Equal(expected, InvoiceReportText.Partner(code, name));

    [Theory]
    [InlineData("10001", "SOJA", "SOJA (10001)")]
    [InlineData("10001", null, "10001")]
    [InlineData(null, "SOJA DO XML", "SOJA DO XML")]
    [InlineData(null, null, "Sem produto vinculado")]
    public void Product_FallsBack(string? code, string? name, string expected) =>
        Assert.Equal(expected, InvoiceReportText.Product(code, name));

    [Fact]
    public void BranchName_PrefersShortNameThenNameThenCode()
    {
        Assert.Equal("MATRIZ", InvoiceReportText.BranchName(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" }, "01"));
        Assert.Equal("MATRIZ LTDA", InvoiceReportText.BranchName(new Branch { Code = "01", BranchName = "MATRIZ LTDA" }, "01"));
        Assert.Equal("01", InvoiceReportText.BranchName(null, "01"));
    }

    [Theory]
    [InlineData(false, false, "Informe o período de emissão.")]
    [InlineData(true, false, "Informe o período de emissão.")]
    public void Validate_RequiresBothDates(bool hasFrom, bool hasTo, string expected)
    {
        var request = new Request
        {
            FromDate = hasFrom ? new DateTime(2026, 7, 1) : default,
            ToDate = hasTo ? new DateTime(2026, 7, 31) : default,
        };
        Assert.Equal(expected, InvoiceReportText.Validate(request));
    }

    [Fact]
    public void Validate_RejectsInvertedPeriod()
    {
        var request = new Request { FromDate = new DateTime(2026, 7, 31), ToDate = new DateTime(2026, 7, 1) };
        Assert.Equal("A data final não pode ser anterior à inicial.", InvoiceReportText.Validate(request));
    }

    [Fact]
    public void Validate_AcceptsAValidPeriod()
    {
        var request = new Request { FromDate = new DateTime(2026, 7, 1), ToDate = new DateTime(2026, 7, 1) };
        Assert.Null(InvoiceReportText.Validate(request));
    }

    [Fact]
    public void BuildFilters_ListsOnlyWhatWasInformed()
    {
        var request = new Request
        {
            FromDate = new DateTime(2026, 7, 1),
            ToDate = new DateTime(2026, 7, 31),
            CardCode = "C001",
            NfeStatuses = [NfeStatus.Authorized],
        };

        var text = InvoiceReportText.BuildFilters(request, standalone: true, "Cliente", "COOPERATIVA", null, null, ["Tipo: Normal"]);

        Assert.Equal(
            "Emissão: 01/07/2026 a 31/07/2026 | Cliente: COOPERATIVA | Situação: Pendente, Confirmado, Retornado | Situação NF-e: Autorizada | Tipo: Normal",
            text);
    }

    [Fact]
    public void BuildFilters_InSapB1_OmitsTheNfeFilter()
    {
        var request = new Request
        {
            FromDate = new DateTime(2026, 7, 1),
            ToDate = new DateTime(2026, 7, 31),
            NfeStatuses = [NfeStatus.Authorized],
        };

        var text = InvoiceReportText.BuildFilters(request, standalone: false, "Cliente", null, null, null, []);

        Assert.Equal("Emissão: 01/07/2026 a 31/07/2026 | Situação: Pendente, Confirmado, Retornado", text);
    }

    [Fact]
    public void BuildFilters_WithoutDescriptions_FallsBackToCodes()
    {
        var request = new Request
        {
            FromDate = new DateTime(2026, 7, 1),
            ToDate = new DateTime(2026, 7, 31),
            BranchCode = "01",
            ItemCode = "10001",
            CardCode = "C001",
        };

        var text = InvoiceReportText.BuildFilters(request, true, "Emitente", null, null, null, []);

        Assert.Equal(
            "Emissão: 01/07/2026 a 31/07/2026 | Filial: 01 | Emitente: C001 | Produto: 10001 | Situação: Pendente, Confirmado, Retornado",
            text);
    }
}
```

`SiagroB1.Application.Tests/Reports/FastReportServiceFiscalTests.cs`:

```csharp
using FastReport;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class FastReportServiceFiscalTests
{
    [Fact]
    public void HideFiscalObjects_HidesOnlyObjectsPrefixedWithFiscal()
    {
        using var report = new Report();
        var page = new ReportPage { Name = "Page1" };
        report.Pages.Add(page);
        var band = new DataBand { Name = "Data1" };
        page.Bands.Add(band);
        var icms = new TextObject { Name = "fiscalIcms" };
        var total = new TextObject { Name = "txtTotal" };
        band.Objects.Add(icms);
        band.Objects.Add(total);

        FastReportService.HideFiscalObjects(report);

        Assert.False(icms.Visible);
        Assert.True(total.Visible);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~InvoiceReportTextTests|FullyQualifiedName~FastReportServiceFiscalTests"`
Expected: build FAIL — `InvoiceReportRequest`, `InvoiceReportText`, `HideFiscalObjects` not found.

- [ ] **Step 3: Implement**

`SiagroB1.Reports/Dtos/InvoiceReportRequest.cs`:

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros comuns aos relatórios de documentos fiscais (notas, itens e devoluções).
/// Só o período é obrigatório. Situação vazia = todas menos Cancelado; Situação NF-e
/// só vale em STANDALONE (em SAPB1 a NF-e não é emitida pelo Siagro e o filtro é ignorado).
/// </summary>
public abstract class InvoiceReportRequest
{
    /// <summary>Início do período de emissão.</summary>
    public DateTime FromDate { get; set; }

    /// <summary>Fim do período de emissão, inclusivo até o fim do dia.</summary>
    public DateTime ToDate { get; set; }

    public string? BranchCode { get; set; }

    /// <summary>Cliente (saída) ou emitente (entrada).</summary>
    public string? CardCode { get; set; }

    public string? ItemCode { get; set; }

    public List<InvoiceStatus>? Statuses { get; set; }

    public List<NfeStatus>? NfeStatuses { get; set; }
}
```

`SiagroB1.Reports/Helpers/InvoiceReportText.cs`:

```csharp
using System.Globalization;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Textos pt-BR comuns aos relatórios de documentos fiscais. Tudo sai do snapshot gravado
/// no documento — nunca de ITEMS/BUSINESS_PARTNERS, vazias em modo SAPB1.
/// </summary>
public static class InvoiceReportText
{
    public const string NoOrigin = "—";
    public const string NoProduct = "Sem produto vinculado";

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly InvoiceStatus[] DefaultStatuses =
        [InvoiceStatus.Pending, InvoiceStatus.Confirmed, InvoiceStatus.Returned];

    public static string? Validate(InvoiceReportRequest request)
    {
        if (request.FromDate == default || request.ToDate == default)
            return "Informe o período de emissão.";

        if (request.ToDate.Date < request.FromDate.Date)
            return "A data final não pode ser anterior à inicial.";

        return null;
    }

    /// <summary>Array (e não lista) para o EF traduzir o Contains em IN.</summary>
    public static InvoiceStatus[] EffectiveStatuses(IReadOnlyCollection<InvoiceStatus>? requested) =>
        requested is { Count: > 0 } ? requested.Distinct().ToArray() : DefaultStatuses;

    public static string Status(InvoiceStatus? status) => status switch
    {
        InvoiceStatus.Confirmed => "Confirmado",
        InvoiceStatus.Cancelled => "Cancelado",
        InvoiceStatus.Returned => "Retornado",
        _ => "Pendente",
    };

    public static string Nfe(NfeStatus status) => status switch
    {
        NfeStatus.Processing => "Em processamento",
        NfeStatus.Authorized => "Autorizada",
        NfeStatus.Rejected => "Rejeitada",
        NfeStatus.Denied => "Denegada",
        NfeStatus.Cancelled => "Cancelada",
        NfeStatus.Voided => "Inutilizada",
        _ => "",
    };

    /// <summary>"número/série"; sem número não há o que imprimir, nem a barra.</summary>
    public static string DocumentNumber(string? number, string? series)
    {
        if (string.IsNullOrWhiteSpace(number))
            return "";

        return string.IsNullOrWhiteSpace(series) ? number.Trim() : $"{number.Trim()}/{series.Trim()}";
    }

    public static string Partner(string? code, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return code ?? "";

        return string.IsNullOrWhiteSpace(code) ? name : $"({code}) {name}";
    }

    public static string Product(string? code, string? name)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.IsNullOrWhiteSpace(name) ? NoProduct : name;

        return string.IsNullOrWhiteSpace(name) ? code : $"{name} ({code})";
    }

    public static string BranchName(Branch? branch, string? code)
    {
        var name = branch?.ShortName;
        if (string.IsNullOrWhiteSpace(name))
            name = branch?.BranchName;

        return string.IsNullOrWhiteSpace(name) ? code ?? "" : name;
    }

    public static string Date(DateTime? value) =>
        value is { } date ? date.ToString("dd/MM/yyyy", Culture) : "";

    /// <summary>
    /// Linha de filtros do cabeçalho. Descrições (nome do parceiro, produto, filial) vêm das
    /// linhas do resultado; sem resultado, resta o código.
    /// </summary>
    public static string BuildFilters(
        InvoiceReportRequest request,
        bool standalone,
        string partnerLabel,
        string? partnerName,
        string? productName,
        string? branchName,
        IEnumerable<string> extra)
    {
        var parts = new List<string> { $"Emissão: {Date(request.FromDate)} a {Date(request.ToDate)}" };

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            parts.Add($"Filial: {Describe(branchName, request.BranchCode)}");

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            parts.Add($"{partnerLabel}: {Describe(partnerName, request.CardCode)}");

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            parts.Add($"Produto: {Describe(productName, request.ItemCode)}");

        parts.Add("Situação: " + string.Join(", ", EffectiveStatuses(request.Statuses).Select(s => Status(s))));

        if (standalone && request.NfeStatuses is { Count: > 0 } nfe)
            parts.Add("Situação NF-e: " + string.Join(", ", nfe.Distinct().Select(Nfe)));

        parts.AddRange(extra);

        return string.Join(" | ", parts);
    }

    private static string Describe(string? description, string fallback) =>
        string.IsNullOrWhiteSpace(description) ? fallback : description;
}
```

Em `SiagroB1.Reports/Services/FastReportService.cs`, acrescente no topo da classe:

```csharp
    /// <summary>
    /// Parâmetro reservado: com <c>false</c> (modo SAPB1) os objetos cujo nome começa com
    /// "fiscal" somem do PDF — tributos e situação da NF-e não são calculados pelo Siagro nesse modo.
    /// </summary>
    public const string StandaloneParameter = "pStandalone";

    public static void HideFiscalObjects(Report report)
    {
        foreach (var component in report.AllObjects.OfType<ReportComponentBase>())
        {
            if (component.Name.StartsWith("fiscal", StringComparison.Ordinal))
                component.Visible = false;
        }
    }

    private static void ApplyParameters(Report report, Dictionary<string, object> parameters)
    {
        foreach (var param in parameters)
            report.SetParameterValue(param.Key, param.Value);

        if (parameters.TryGetValue(StandaloneParameter, out var standalone) && standalone is false)
            HideFiscalObjects(report);
    }
```

e, nos **dois** `GeneratePdfAsync`, troque o laço

```csharp
        foreach (var param in parameters)
        {
            report.SetParameterValue(param.Key, param.Value);
        }
```

por

```csharp
        ApplyParameters(report, parameters);
```

`SiagroB1.Application.Tests/Support/RecordingFastReportService.cs`:

```csharp
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Não gera PDF: guarda o que o serviço de relatório pediu, para o teste conferir.</summary>
public sealed class RecordingFastReportService : IFastReportService
{
    public string? LastReportName { get; private set; }
    public Dictionary<string, object>? LastParameters { get; private set; }
    public object? LastData { get; private set; }

    public Task<byte[]> GeneratePdfAsync(string reportName, Dictionary<string, object> parameters)
    {
        LastReportName = reportName;
        LastParameters = parameters;
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task<byte[]> GeneratePdfAsync<T>(
        string reportName,
        ICollection<T> data,
        string dataSourceName,
        string refName,
        Dictionary<string, object> parameters)
    {
        LastReportName = reportName;
        LastParameters = parameters;
        LastData = data;
        return Task.FromResult(Array.Empty<byte>());
    }
}
```

`SiagroB1.Application.Tests/Support/TestConfiguration.cs`:

```csharp
using Microsoft.Extensions.Configuration;

namespace SiagroB1.Application.Tests.Support;

public static class TestConfiguration
{
    /// <summary>Configuração mínima com a chave <c>Erp</c> ("SAPB1" ou "STANDALONE").</summary>
    public static IConfiguration Erp(string mode) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = mode })
            .Build();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~InvoiceReportTextTests|FullyQualifiedName~FastReportServiceFiscalTests|FullyQualifiedName~ContractsByItem"`
Expected: PASS (os de `ContractsByItem` provam que a troca do laço de parâmetros não quebrou nada).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Reports/Dtos/InvoiceReportRequest.cs SiagroB1.Reports/Helpers/InvoiceReportText.cs SiagroB1.Reports/Services/FastReportService.cs SiagroB1.Application.Tests/Support/RecordingFastReportService.cs SiagroB1.Application.Tests/Support/TestConfiguration.cs SiagroB1.Application.Tests/Reports/InvoiceReportTextTests.cs SiagroB1.Application.Tests/Reports/FastReportServiceFiscalTests.cs
git commit -m "feat(reports): base comum dos relatórios de documentos fiscais" -m "Filtros, textos pt-BR e ocultação das colunas fiscais em SAPB1 via parâmetro pStandalone." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Notas de Saída por Período (substitui "Documentos de Saída")

**Files:**
- Create: `SiagroB1.Reports/Dtos/SalesInvoicesByPeriodRequest.cs`, `SiagroB1.Reports/Dtos/SalesInvoicesByPeriodRowDto.cs`
- Create: `SiagroB1.Reports/Services/SalesInvoicesByPeriodReportService.cs`
- Create: `SiagroB1.Reports/Controllers/SalesInvoicesByPeriodController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/SalesInvoicesByPeriod.frx`
- Delete: `SiagroB1.Reports/Controllers/SalesInvoicesController.cs`, `SiagroB1.Reports/Reports/Templates/SalesInvoices.frx`
- Create: `SiagroB1.Application.Tests/Support/InvoiceReportSeed.cs`
- Test: `SiagroB1.Application.Tests/Reports/SalesInvoicesByPeriodReportServiceTests.cs`
- Test: `SiagroB1.Application.Tests/Reports/InvoiceReportsPdfTests.cs`

**Interfaces:**
- Consumes: Task 1 (`InvoiceReportRequest`, `InvoiceReportText`, `FastReportService.StandaloneParameter`, `RecordingFastReportService`, `TestConfiguration`).
- Produces:
  - `SalesInvoicesByPeriodReportService(IUnitOfWork db, IFastReportService reportService, IConfiguration configuration)` com `Task<List<SalesInvoicesByPeriodRowDto>> BuildRowsAsync(SalesInvoicesByPeriodRequest)` e `Task<byte[]> ExecuteAsync(SalesInvoicesByPeriodRequest)`.
  - `InvoiceReportSeed` (test support): `SalesInvoice Sale(string number, DateTime? date = null, InvoiceStatus? status = InvoiceStatus.Confirmed, SalesInvoiceType type = SalesInvoiceType.Normal, string branchCode = "01", string cardCode = "C001", string cardName = "COOPERATIVA CENTRAL", params SalesInvoiceItem[] items)`, `SalesInvoiceItem SaleItem(string itemCode = "10001", string itemName = "SOJA EM GRÃOS", decimal quantity = 10m, decimal unitPrice = 100m, string uom = "TN")`, `PurchaseInvoice Purchase(string number, DateTime? date = null, InvoiceStatus status = InvoiceStatus.Confirmed, PurchaseInvoiceType type = PurchaseInvoiceType.Normal, DocumentIssuerType issuer = DocumentIssuerType.ThirdParty, string branchCode = "01", string cardCode = "F001", string cardName = "PRODUTOR RURAL", params PurchaseInvoiceItem[] items)`, `PurchaseInvoiceItem PurchaseItem(string? itemCode = "10001", string? itemName = "SOJA EM GRÃOS", decimal quantity = 10m, decimal unitPrice = 100m, string? uom = "TN")`, `static readonly DateTime Jul01, Jul31`.
  - `InvoiceReportsPdfTests.RenderAsync(...)` helper reutilizado pelas Tasks 3–6 (cada task acrescenta um `[Theory]` à classe).

- [ ] **Step 1: Seed de teste**

Antes de escrever, abra `SiagroB1.Domain/Entities/SalesInvoice.cs`, `SalesInvoiceItem.cs`, `PurchaseInvoice.cs`, `PurchaseInvoiceItem.cs` e confirme quais propriedades são `required` — o seed precisa preenchê-las (já se sabe: `SalesInvoiceItem.ItemCode` e `UnitOfMeasureCode` são `required`; `SalesInvoice.CardCode` provavelmente também).

`SiagroB1.Application.Tests/Support/InvoiceReportSeed.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Documentos mínimos para os testes dos relatórios fiscais.</summary>
public static class InvoiceReportSeed
{
    public static readonly DateTime Jul01 = new(2026, 7, 1);
    public static readonly DateTime Jul31 = new(2026, 7, 31);

    public static SalesInvoice Sale(
        string number,
        DateTime? date = null,
        InvoiceStatus? status = InvoiceStatus.Confirmed,
        SalesInvoiceType type = SalesInvoiceType.Normal,
        string branchCode = "01",
        string cardCode = "C001",
        string cardName = "COOPERATIVA CENTRAL",
        params SalesInvoiceItem[] items)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            InvoiceNumber = number,
            InvoiceDate = date ?? new DateTime(2026, 7, 15),
            InvoiceStatus = status,
            InvoiceType = type,
            BranchCode = branchCode,
            CardCode = cardCode,
            CardName = cardName,
        };

        foreach (var item in items.Length > 0 ? items : [SaleItem()])
        {
            item.SalesInvoiceKey = invoice.Key;
            invoice.Items.Add(item);
        }

        return invoice;
    }

    public static SalesInvoiceItem SaleItem(
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        decimal quantity = 10m,
        decimal unitPrice = 100m,
        string uom = "TN") => new()
    {
        Key = Guid.NewGuid(),
        ItemCode = itemCode,
        ItemName = itemName,
        Quantity = quantity,
        UnitPrice = unitPrice,
        UnitOfMeasureCode = uom,
    };

    public static PurchaseInvoice Purchase(
        string number,
        DateTime? date = null,
        InvoiceStatus status = InvoiceStatus.Confirmed,
        PurchaseInvoiceType type = PurchaseInvoiceType.Normal,
        DocumentIssuerType issuer = DocumentIssuerType.ThirdParty,
        string branchCode = "01",
        string cardCode = "F001",
        string cardName = "PRODUTOR RURAL",
        params PurchaseInvoiceItem[] items)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(),
            InvoiceNumber = number,
            IssueDate = date ?? new DateTime(2026, 7, 15),
            PostingDate = date ?? new DateTime(2026, 7, 16),
            InvoiceStatus = status,
            InvoiceType = type,
            IssuerType = issuer,
            BranchCode = branchCode,
            CardCode = cardCode,
            CardName = cardName,
        };

        foreach (var item in items.Length > 0 ? items : [PurchaseItem()])
        {
            item.PurchaseInvoiceKey = invoice.Key;
            invoice.Items.Add(item);
        }

        return invoice;
    }

    public static PurchaseInvoiceItem PurchaseItem(
        string? itemCode = "10001",
        string? itemName = "SOJA EM GRÃOS",
        decimal quantity = 10m,
        decimal unitPrice = 100m,
        string? uom = "TN") => new()
    {
        Key = Guid.NewGuid(),
        ItemCode = itemCode,
        ItemName = itemName,
        Quantity = quantity,
        UnitPrice = unitPrice,
        UnitOfMeasureCode = uom,
    };
}
```

Se alguma entidade tiver outro `required` (o compilador acusa `CS9035`), preencha-o no seed com valor neutro.

- [ ] **Step 2: Write the failing service tests**

`SiagroB1.Application.Tests/Reports/SalesInvoicesByPeriodReportServiceTests.cs`:

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class SalesInvoicesByPeriodReportServiceTests
{
    [Fact]
    public async Task BuildRows_IncludesTheWholeLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("1", new DateTime(2026, 7, 31, 23, 45, 0)));
        db.Context.SalesInvoices.Add(Sale("2", new DateTime(2026, 8, 1, 0, 5, 0)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "1" }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelledAndTreatsNullStatusAsPending()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("OK", status: InvoiceStatus.Confirmed));
        db.Context.SalesInvoices.Add(Sale("NULL", status: null));
        db.Context.SalesInvoices.Add(Sale("CANC", status: InvoiceStatus.Cancelled));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "NULL", "OK" }, rows.Select(r => r.InternalNumber).Order());
        Assert.Equal("Pendente", rows.Single(r => r.InternalNumber == "NULL").Status);
    }

    [Fact]
    public async Task BuildRows_ExplicitCancelledFilterReturnsOnlyCancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("OK"));
        db.Context.SalesInvoices.Add(Sale("CANC", status: InvoiceStatus.Cancelled));
        await Save(db);

        var request = Request();
        request.Statuses = [InvoiceStatus.Cancelled];
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "CANC" }, rows.Select(r => r.InternalNumber));
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("CardCode")]
    [InlineData("ItemCode")]
    [InlineData("InvoiceType")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("MATCH"));
        db.Context.SalesInvoices.Add(Sale("OTHER", type: SalesInvoiceType.Return, branchCode: "99", cardCode: "C999",
            items: [SaleItem(itemCode: "99999")]));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "CardCode": request.CardCode = "C001"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "InvoiceType": request.InvoiceType = SalesInvoiceType.Normal; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "MATCH" }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_NfeFilterAppliesInStandaloneAndIsIgnoredInSapB1()
    {
        var db = TestDb.CreateUnitOfWork();
        var authorized = Sale("AUT");
        authorized.NfeStatus = NfeStatus.Authorized;
        db.Context.SalesInvoices.Add(authorized);
        db.Context.SalesInvoices.Add(Sale("NONE"));
        await Save(db);

        var request = Request();
        request.NfeStatuses = [NfeStatus.Authorized];

        Assert.Equal(new[] { "AUT" }, (await Service(db, "STANDALONE").BuildRowsAsync(request)).Select(r => r.InternalNumber));
        Assert.Equal(2, (await Service(db, "SAPB1").BuildRowsAsync(request)).Count);
    }

    [Fact]
    public async Task BuildRows_TotalsMatchTheDomainProperties()
    {
        var db = TestDb.CreateUnitOfWork();
        var a = SaleItem(quantity: 10m, unitPrice: 100m);
        a.FreightValue = 50m;
        a.DiscountValue = 20m;
        a.IcmsValue = 12m;
        a.PisValue = 1.65m;
        a.CofinsValue = 7.6m;
        var b = SaleItem(quantity: 2m, unitPrice: 50m);
        var invoice = Sale("1", items: [a, b]);
        invoice.NetWeight = 12000m;
        invoice.TaxDocumentNumber = "000123";
        invoice.TaxDocumentSeries = "1";
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        db.Context.SalesInvoices.Add(invoice);
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("MATRIZ", row.Branch);
        Assert.Equal("000123/1", row.DocumentNumber);
        Assert.Equal("15/07/2026", row.IssueDate);
        Assert.Equal("(C001) COOPERATIVA CENTRAL", row.Customer);
        Assert.Equal("Normal", row.Type);
        Assert.Equal("Confirmado", row.Status);
        Assert.Equal(12000m, row.NetWeight);
        Assert.Equal(1100m, row.ProductsTotal);
        Assert.Equal(50m, row.Freight);
        Assert.Equal(20m, row.Discount);
        Assert.Equal(21.25m, row.Taxes);
        Assert.Equal(1130m, row.GrandTotal);
    }

    [Fact]
    public async Task BuildRows_OrdersByDateThenNumber()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("B", new DateTime(2026, 7, 10)));
        db.Context.SalesInvoices.Add(Sale("C", new DateTime(2026, 7, 5)));
        db.Context.SalesInvoices.Add(Sale("A", new DateTime(2026, 7, 10)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "C", "A", "B" }, rows.Select(r => r.InternalNumber));
    }

    [Theory]
    [InlineData("STANDALONE", true)]
    [InlineData("SAPB1", false)]
    public async Task Execute_PassesTheModeAndFiltersToTheTemplate(string erp, bool standalone)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("1"));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new SalesInvoicesByPeriodReportService(db, recorder, TestConfiguration.Erp(erp)).ExecuteAsync(Request());

        Assert.Equal("SalesInvoicesByPeriod.frx", recorder.LastReportName);
        Assert.Equal(standalone, recorder.LastParameters![FastReportService.StandaloneParameter]);
        Assert.StartsWith("Emissão: 01/07/2026 a 31/07/2026", (string)recorder.LastParameters["pFilters"]);
    }

    private static SalesInvoicesByPeriodReportService Service(IUnitOfWork db, string erp = "STANDALONE") =>
        new(db, new RecordingFastReportService(), TestConfiguration.Erp(erp));

    private static SalesInvoicesByPeriodRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesByPeriodReportServiceTests"`
Expected: build FAIL — `SalesInvoicesByPeriodReportService` not found.

- [ ] **Step 4: Implement DTOs, service and controller**

`SiagroB1.Reports/Dtos/SalesInvoicesByPeriodRequest.cs`:

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Notas de Saída por Período". Tipo vazio = Normal e Devolução.</summary>
public class SalesInvoicesByPeriodRequest : InvoiceReportRequest
{
    public SalesInvoiceType? InvoiceType { get; set; }
}
```

`SiagroB1.Reports/Dtos/SalesInvoicesByPeriodRowDto.cs`:

```csharp
namespace SiagroB1.Reports.Dtos;

public class SalesInvoicesByPeriodRowDto
{
    public string Branch { get; set; } = "";
    public string InternalNumber { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string IssueDate { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string NfeStatus { get; set; } = "";
    public decimal NetWeight { get; set; }
    public decimal ProductsTotal { get; set; }
    public decimal Freight { get; set; }
    public decimal Discount { get; set; }
    public decimal Taxes { get; set; }
    public decimal IbsCbs { get; set; }
    public decimal GrandTotal { get; set; }
}
```

`SiagroB1.Reports/Services/SalesInvoicesByPeriodReportService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Notas de saída por período, uma linha por documento. Substitui o antigo "Documentos de
/// Saída" (SQL embutido no .frx, sem filtro). Os totais não têm coluna no banco: são as
/// propriedades [NotMapped] do domínio, somadas das linhas — por isso o Include de Items.
/// </summary>
public class SalesInvoicesByPeriodReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private bool Standalone => ErpMode.IsStandalone(configuration);

    public async Task<List<SalesInvoicesByPeriodRowDto>> BuildRowsAsync(SalesInvoicesByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        // Branch é tabela local com FK opcional -> LEFT JOIN, seguro em SAPB1.
        var query = db.Context.SalesInvoices
            .AsNoTracking()
            .Include(x => x.Branch)
            .Include(x => x.Items)
            .Where(x => x.InvoiceDate >= from && x.InvoiceDate < toExclusive)
            .Where(x => statuses.Contains(x.InvoiceStatus ?? InvoiceStatus.Pending));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(x => x.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(x => x.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(x => x.Items.Any(i => i.ItemCode == request.ItemCode));

        if (request.InvoiceType is { } type)
            query = query.Where(x => x.InvoiceType == type);

        if (Standalone && request.NfeStatuses is { Count: > 0 })
        {
            var nfe = request.NfeStatuses.Distinct().ToArray();
            query = query.Where(x => nfe.Contains(x.NfeStatus));
        }

        var invoices = await query.ToListAsync();

        return invoices
            .OrderBy(x => x.InvoiceDate)
            .ThenBy(x => x.InvoiceNumber ?? "", StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(SalesInvoicesByPeriodRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var standalone = Standalone;

        var extra = request.InvoiceType is { } type ? new[] { $"Tipo: {DescribeType(type)}" } : [];
        var first = rows.Count > 0 ? rows[0] : null;

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = InvoiceReportText.BuildFilters(
                request, standalone, "Cliente", first?.Customer, null, first?.Branch, extra),
            [FastReportService.StandaloneParameter] = standalone,
        };

        return await reportService.GeneratePdfAsync(
            "SalesInvoicesByPeriod.frx", rows, "SalesInvoicesByPeriod", "SalesInvoicesByPeriod", parameters);
    }

    private static SalesInvoicesByPeriodRowDto ToRow(SalesInvoice x) => new()
    {
        Branch = InvoiceReportText.BranchName(x.Branch, x.BranchCode),
        InternalNumber = x.InvoiceNumber ?? "",
        DocumentNumber = InvoiceReportText.DocumentNumber(x.TaxDocumentNumber, x.TaxDocumentSeries),
        IssueDate = InvoiceReportText.Date(x.InvoiceDate),
        Customer = InvoiceReportText.Partner(x.CardCode, x.CardName),
        Type = DescribeType(x.InvoiceType),
        Status = InvoiceReportText.Status(x.InvoiceStatus),
        NfeStatus = InvoiceReportText.Nfe(x.NfeStatus),
        NetWeight = x.NetWeight,
        ProductsTotal = x.TotalInvoiceItems,
        Freight = x.TotalFreight,
        Discount = x.TotalDiscount,
        Taxes = x.TotalInvoiceTaxes,
        IbsCbs = x.TotalInvoiceIbsCbs,
        GrandTotal = x.GrandTotal,
    };

    private static string DescribeType(SalesInvoiceType type) =>
        type == SalesInvoiceType.Return ? "Devolução" : "Normal";
}
```

Nota: `BuildFilters` recebe a descrição do parceiro do primeiro resultado. Quando o filtro de cliente está ativo, todas as linhas são daquele cliente, então isso está correto; mas `first?.Customer` vem como "(C001) NOME". Isso é aceitável no cabeçalho.

`SiagroB1.Reports/Controllers/SalesInvoicesByPeriodController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/SalesInvoicesByPeriod")]
public class SalesInvoicesByPeriodController(SalesInvoicesByPeriodReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] SalesInvoicesByPeriodRequest request)
    {
        if (InvoiceReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"sales-invoices-by-period.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

Confirme que o `IConfiguration` chega ao serviço: o Scrutor registra a classe por si mesma e o container resolve `IConfiguration` sozinho (não é preciso alterar `DI/ServiceCollectionExtensions.cs`).

- [ ] **Step 5: Run service tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesByPeriodReportServiceTests"`
Expected: PASS.

- [ ] **Step 6: Template**

Crie `SalesInvoicesByPeriod.frx` seguindo o Appendix A. **Retrato não cabe — use paisagem.** Sem agrupamento (passo 8 do apêndice). Título: `Notas de Saída por Período`. Fonte de dados `SalesInvoicesByPeriod`.

| Coluna (cabeçalho) | Campo | Largura | Formato | Nome do objeto |
|---|---|---|---|---|
| Filial | Branch | 70 | texto | txtBranch |
| Nº interno | InternalNumber | 60 | texto | txtInternalNumber |
| NF/Série | DocumentNumber | 70 | texto | txtDocumentNumber |
| Emissão | IssueDate | 60 | texto | txtIssueDate |
| Cliente | Customer | 190 | texto | txtCustomer |
| Tipo | Type | 55 | texto | txtType |
| Situação | Status | 65 | texto | txtStatus |
| Peso líquido | NetWeight | 70 | 3 casas | txtNetWeight |
| Produtos | ProductsTotal | 75 | 2 casas | txtProducts |
| Frete | Freight | 60 | 2 casas | txtFreight |
| Desconto | Discount | 60 | 2 casas | txtDiscount |
| Total geral | GrandTotal | 80 | 2 casas | txtGrandTotal |
| Tributos | Taxes | 65 | 2 casas | fiscalTaxes |
| IBS/CBS | IbsCbs | 60 | 2 casas | fiscalIbsCbs |
| Sit. NF-e | NfeStatus | 64 | texto | fiscalNfeStatus |

Sumário: contagem de documentos (`TotalCountAll`) e somas de NetWeight, ProductsTotal, Freight, Discount, GrandTotal (`txtSum*`) e Taxes, IbsCbs (`fiscalSum*`), alinhadas sob as colunas.

Apague `SiagroB1.Reports/Reports/Templates/SalesInvoices.frx` e `SiagroB1.Reports/Controllers/SalesInvoicesController.cs` com `git rm`.

- [ ] **Step 7: PDF tests**

`SiagroB1.Application.Tests/Reports/InvoiceReportsPdfTests.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Gera o PDF de ponta a ponta com o template real, nos dois modos. Pega fonte de dados com
/// nome divergente, coluna que o FastReport não converte e objeto fiscal mal nomeado.
/// Grava o PDF em %TEMP%/siagro-invoice-reports para conferência visual.
/// </summary>
public class InvoiceReportsPdfTests : IDisposable
{
    private readonly string _contentRoot;

    public InvoiceReportsPdfTests()
    {
        FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));
        FastReport.Utils.Config.WebMode = true;

        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-invoice-pdf", Guid.NewGuid().ToString("N"));
        var templates = Path.Combine(_contentRoot, "Reports", "Templates");
        Directory.CreateDirectory(templates);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ReportTemplates"), "*.frx"))
            File.Copy(file, Path.Combine(templates, Path.GetFileName(file)));

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

    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task SalesInvoicesByPeriod_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = Sale("1");
        invoice.TaxDocumentNumber = "000123";
        invoice.TaxDocumentSeries = "1";
        db.Context.SalesInvoices.Add(invoice);
        db.Context.SalesInvoices.Add(Sale("2")); // sem NF: coluna vazia
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration(erp);
        var pdf = await new SalesInvoicesByPeriodReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new SalesInvoicesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesInvoicesByPeriod", erp, pdf);
    }

    [Fact]
    public async Task SalesInvoicesByPeriod_EmptyResultStillProducesAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        var configuration = Configuration("STANDALONE");

        var pdf = await new SalesInvoicesByPeriodReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new SalesInvoicesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Assert.NotEmpty(pdf);
    }

    private FastReportService FastReport(IConfiguration configuration)
    {
        var env = new TestWebHostEnvironment(_contentRoot);
        return new FastReportService(env, configuration,
            new ReportHeaderService(env, configuration, new TestLogger<ReportHeaderService>()));
    }

    private static IConfiguration Configuration(string erp) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Erp"] = erp,
                ["CompanyName"] = "ACME AGRO LTDA",
                ["CompanyLogoPath"] = "wwwroot/images/logo.png",
            })
            .Build();

    private static void Keep(string report, string erp, byte[] pdf)
    {
        Assert.NotEmpty(pdf);
        var folder = Path.Combine(Path.GetTempPath(), "siagro-invoice-reports");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, $"{report}-{erp}.pdf"), pdf);
    }
}
```

- [ ] **Step 8: Run all report tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"`
Expected: PASS — incluindo `ReportTemplateHeaderTests` e `ReportTemplateRenderSmokeTests`, que agora descobrem `SalesInvoicesByPeriod.frx` e já não encontram `SalesInvoices.frx`. Se algum teste citar `SalesInvoices.frx` pelo nome, remova a referência.

- [ ] **Step 9: Conferência visual**

Abra `%TEMP%/siagro-invoice-reports/SalesInvoicesByPeriod-STANDALONE.pdf` e `-SAPB1.pdf` (Read tool lê PDF). Confira: nada cortado, NF vazia sem "/", em SAPB1 sem Tributos/IBS-CBS/Sit. NF-e. Ajuste larguras se preciso e rode o Step 8 de novo.

- [ ] **Step 10: Build e commit**

Run: `dotnet build SiagroB1.sln` — Expected: 0 erros.

```bash
git add SiagroB1.Reports/Dtos/SalesInvoicesByPeriodRequest.cs SiagroB1.Reports/Dtos/SalesInvoicesByPeriodRowDto.cs SiagroB1.Reports/Services/SalesInvoicesByPeriodReportService.cs SiagroB1.Reports/Controllers/SalesInvoicesByPeriodController.cs SiagroB1.Reports/Reports/Templates/SalesInvoicesByPeriod.frx SiagroB1.Application.Tests/Support/InvoiceReportSeed.cs SiagroB1.Application.Tests/Reports/SalesInvoicesByPeriodReportServiceTests.cs SiagroB1.Application.Tests/Reports/InvoiceReportsPdfTests.cs
git rm SiagroB1.Reports/Controllers/SalesInvoicesController.cs SiagroB1.Reports/Reports/Templates/SalesInvoices.frx
git commit -m "feat(reports): relatório de notas de saída por período" -m "Substitui o antigo Documentos de Saída, que não tinha filtro e imprimia a situação como número." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

Se o classificador bloquear o commit por causa da exclusão (ver memória), faça o commit das adições e deixe a exclusão para um commit separado, avisando o usuário.

---

### Task 3: Notas de Entrada por Período

**Files:**
- Create: `SiagroB1.Reports/Dtos/PurchaseInvoicesByPeriodRequest.cs`, `SiagroB1.Reports/Dtos/PurchaseInvoicesByPeriodRowDto.cs`
- Create: `SiagroB1.Reports/Services/PurchaseInvoicesByPeriodReportService.cs`
- Create: `SiagroB1.Reports/Controllers/PurchaseInvoicesByPeriodController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/PurchaseInvoicesByPeriod.frx`
- Test: `SiagroB1.Application.Tests/Reports/PurchaseInvoicesByPeriodReportServiceTests.cs`
- Modify (test): `SiagroB1.Application.Tests/Reports/InvoiceReportsPdfTests.cs`

**Interfaces:**
- Consumes: Task 1; `InvoiceReportSeed.Purchase/PurchaseItem` e os helpers privados `FastReport`, `Configuration`, `Keep` de `InvoiceReportsPdfTests` (Task 2).
- Produces: `PurchaseInvoicesByPeriodReportService(IUnitOfWork, IFastReportService, IConfiguration)` com `BuildRowsAsync`/`ExecuteAsync(PurchaseInvoicesByPeriodRequest)`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Reports/PurchaseInvoicesByPeriodReportServiceTests.cs`:

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class PurchaseInvoicesByPeriodReportServiceTests
{
    [Fact]
    public async Task BuildRows_FiltersByIssueDateIncludingTheLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("1", new DateTime(2026, 7, 31, 23, 0, 0)));
        db.Context.PurchaseInvoices.Add(Purchase("2", new DateTime(2026, 8, 1)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "1" }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("OK"));
        db.Context.PurchaseInvoices.Add(Purchase("CANC", status: InvoiceStatus.Cancelled));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "OK" }, rows.Select(r => r.InternalNumber));
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("CardCode")]
    [InlineData("ItemCode")]
    [InlineData("InvoiceType")]
    [InlineData("IssuerType")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("MATCH"));
        db.Context.PurchaseInvoices.Add(Purchase("OTHER", type: PurchaseInvoiceType.Return,
            issuer: DocumentIssuerType.Own, branchCode: "99", cardCode: "F999",
            items: [PurchaseItem(itemCode: "99999")]));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "CardCode": request.CardCode = "F001"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "InvoiceType": request.InvoiceType = PurchaseInvoiceType.Normal; break;
            case "IssuerType": request.IssuerType = DocumentIssuerType.ThirdParty; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "MATCH" }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_FormatsColumnsAndTotals()
    {
        var db = TestDb.CreateUnitOfWork();
        var item = PurchaseItem(quantity: 10m, unitPrice: 100m);
        item.FreightValue = 30m;
        item.IcmsValue = 12m;
        var invoice = Purchase("7", items: [item], issuer: DocumentIssuerType.Own);
        invoice.TaxDocumentNumber = "55";
        invoice.TaxDocumentSeries = "9";
        invoice.TotalDocumentValue = 1030m;
        db.Context.PurchaseInvoices.Add(invoice);
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("55/9", row.DocumentNumber);
        Assert.Equal("15/07/2026", row.IssueDate);
        Assert.Equal("16/07/2026", row.PostingDate);
        Assert.Equal("(F001) PRODUTOR RURAL", row.Issuer);
        Assert.Equal("Normal", row.Type);
        Assert.Equal("Própria", row.IssuerType);
        Assert.Equal(1030m, row.DeclaredValue);
        Assert.Equal(1000m, row.ProductsTotal);
        Assert.Equal(30m, row.Freight);
        Assert.Equal(12m, row.Taxes);
        Assert.Equal(1030m, row.GrandTotal);
    }

    [Theory]
    [InlineData("STANDALONE", true)]
    [InlineData("SAPB1", false)]
    public async Task Execute_PassesTheMode(string erp, bool standalone)
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        await new PurchaseInvoicesByPeriodReportService(db, recorder, TestConfiguration.Erp(erp)).ExecuteAsync(Request());

        Assert.Equal("PurchaseInvoicesByPeriod.frx", recorder.LastReportName);
        Assert.Equal(standalone, recorder.LastParameters![FastReportService.StandaloneParameter]);
    }

    private static PurchaseInvoicesByPeriodReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService(), TestConfiguration.Erp("STANDALONE"));

    private static PurchaseInvoicesByPeriodRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesByPeriodReportServiceTests"`
Expected: build FAIL — tipos inexistentes.

- [ ] **Step 3: Implement**

`SiagroB1.Reports/Dtos/PurchaseInvoicesByPeriodRequest.cs`:

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Notas de Entrada por Período". Tipo e Emissão vazios = todos.</summary>
public class PurchaseInvoicesByPeriodRequest : InvoiceReportRequest
{
    public PurchaseInvoiceType? InvoiceType { get; set; }

    public DocumentIssuerType? IssuerType { get; set; }
}
```

`SiagroB1.Reports/Dtos/PurchaseInvoicesByPeriodRowDto.cs`:

```csharp
namespace SiagroB1.Reports.Dtos;

public class PurchaseInvoicesByPeriodRowDto
{
    public string Branch { get; set; } = "";
    public string InternalNumber { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string IssueDate { get; set; } = "";
    public string PostingDate { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Type { get; set; } = "";
    public string IssuerType { get; set; } = "";
    public string Status { get; set; } = "";
    public string NfeStatus { get; set; } = "";
    public decimal DeclaredValue { get; set; }
    public decimal ProductsTotal { get; set; }
    public decimal Freight { get; set; }
    public decimal Discount { get; set; }
    public decimal Taxes { get; set; }
    public decimal GrandTotal { get; set; }
}
```

`SiagroB1.Reports/Services/PurchaseInvoicesByPeriodReportService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Notas de entrada por período, uma linha por documento. Inclui as próprias (emissão própria,
/// devolução de compra) e as de terceiro (fornecedor, cliente devolvendo).
/// </summary>
public class PurchaseInvoicesByPeriodReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private bool Standalone => ErpMode.IsStandalone(configuration);

    public async Task<List<PurchaseInvoicesByPeriodRowDto>> BuildRowsAsync(PurchaseInvoicesByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        var query = db.Context.PurchaseInvoices
            .AsNoTracking()
            .Include(x => x.Branch)
            .Include(x => x.Items)
            .Where(x => x.IssueDate >= from && x.IssueDate < toExclusive)
            .Where(x => statuses.Contains(x.InvoiceStatus));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(x => x.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(x => x.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(x => x.Items.Any(i => i.ItemCode == request.ItemCode));

        if (request.InvoiceType is { } type)
            query = query.Where(x => x.InvoiceType == type);

        if (request.IssuerType is { } issuer)
            query = query.Where(x => x.IssuerType == issuer);

        if (Standalone && request.NfeStatuses is { Count: > 0 })
        {
            var nfe = request.NfeStatuses.Distinct().ToArray();
            query = query.Where(x => nfe.Contains(x.NfeStatus));
        }

        var invoices = await query.ToListAsync();

        return invoices
            .OrderBy(x => x.IssueDate)
            .ThenBy(x => x.InvoiceNumber ?? "", StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(PurchaseInvoicesByPeriodRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var standalone = Standalone;
        var first = rows.Count > 0 ? rows[0] : null;

        var extra = new List<string>();
        if (request.InvoiceType is { } type)
            extra.Add($"Tipo: {DescribeType(type)}");
        if (request.IssuerType is { } issuer)
            extra.Add($"Emissão: {DescribeIssuer(issuer)}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = InvoiceReportText.BuildFilters(
                request, standalone, "Emitente", first?.Issuer, null, first?.Branch, extra),
            [FastReportService.StandaloneParameter] = standalone,
        };

        return await reportService.GeneratePdfAsync(
            "PurchaseInvoicesByPeriod.frx", rows, "PurchaseInvoicesByPeriod", "PurchaseInvoicesByPeriod", parameters);
    }

    private static PurchaseInvoicesByPeriodRowDto ToRow(PurchaseInvoice x) => new()
    {
        Branch = InvoiceReportText.BranchName(x.Branch, x.BranchCode),
        InternalNumber = x.InvoiceNumber ?? "",
        DocumentNumber = InvoiceReportText.DocumentNumber(x.TaxDocumentNumber, x.TaxDocumentSeries),
        IssueDate = InvoiceReportText.Date(x.IssueDate),
        PostingDate = InvoiceReportText.Date(x.PostingDate),
        Issuer = InvoiceReportText.Partner(x.CardCode, x.CardName),
        Type = DescribeType(x.InvoiceType),
        IssuerType = DescribeIssuer(x.IssuerType),
        Status = InvoiceReportText.Status(x.InvoiceStatus),
        NfeStatus = InvoiceReportText.Nfe(x.NfeStatus),
        DeclaredValue = x.TotalDocumentValue,
        ProductsTotal = x.TotalInvoiceItems,
        Freight = x.TotalFreight,
        Discount = x.TotalDiscount,
        Taxes = x.TotalInvoiceTaxes,
        GrandTotal = x.GrandTotal,
    };

    internal static string DescribeType(PurchaseInvoiceType type) =>
        type == PurchaseInvoiceType.Return ? "Devolução" : "Normal";

    internal static string DescribeIssuer(DocumentIssuerType issuer) =>
        issuer == DocumentIssuerType.Own ? "Própria" : "Terceiro";
}
```

`SiagroB1.Reports/Controllers/PurchaseInvoicesByPeriodController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/PurchaseInvoicesByPeriod")]
public class PurchaseInvoicesByPeriodController(PurchaseInvoicesByPeriodReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] PurchaseInvoicesByPeriodRequest request)
    {
        if (InvoiceReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"purchase-invoices-by-period.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesByPeriodReportServiceTests"`
Expected: PASS.

- [ ] **Step 5: Template**

`PurchaseInvoicesByPeriod.frx` pelo Appendix A, paisagem, sem agrupamento, título `Notas de Entrada por Período`, fonte `PurchaseInvoicesByPeriod`.

| Coluna | Campo | Largura | Formato | Nome |
|---|---|---|---|---|
| Filial | Branch | 65 | texto | txtBranch |
| Nº interno | InternalNumber | 55 | texto | txtInternalNumber |
| NF/Série | DocumentNumber | 65 | texto | txtDocumentNumber |
| Emissão | IssueDate | 58 | texto | txtIssueDate |
| Entrada | PostingDate | 58 | texto | txtPostingDate |
| Emitente | Issuer | 175 | texto | txtIssuer |
| Tipo | Type | 55 | texto | txtType |
| Emissão própria | IssuerType | 55 | texto | txtIssuerType |
| Situação | Status | 62 | texto | txtStatus |
| Valor declarado | DeclaredValue | 75 | 2 casas | txtDeclared |
| Produtos | ProductsTotal | 75 | 2 casas | txtProducts |
| Frete | Freight | 55 | 2 casas | txtFreight |
| Desconto | Discount | 55 | 2 casas | txtDiscount |
| Total geral | GrandTotal | 75 | 2 casas | txtGrandTotal |
| Tributos | Taxes | 60 | 2 casas | fiscalTaxes |
| Sit. NF-e | NfeStatus | 61 | texto | fiscalNfeStatus |

Sumário: contagem e somas de DeclaredValue, ProductsTotal, Freight, Discount, GrandTotal; `fiscalSumTaxes`.

- [ ] **Step 6: PDF test**

Acrescente a `InvoiceReportsPdfTests`:

```csharp
    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task PurchaseInvoicesByPeriod_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("1"));
        db.Context.PurchaseInvoices.Add(Purchase("2", issuer: DocumentIssuerType.Own, type: PurchaseInvoiceType.Return));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration(erp);
        var pdf = await new PurchaseInvoicesByPeriodReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new PurchaseInvoicesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("PurchaseInvoicesByPeriod", erp, pdf);
    }
```

- [ ] **Step 7: Run report tests + visual check**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"` — Expected: PASS. Abra `%TEMP%/siagro-invoice-reports/PurchaseInvoicesByPeriod-*.pdf` e confira cortes e a ausência das colunas fiscais em SAPB1.

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Reports/Dtos/PurchaseInvoicesByPeriodRequest.cs SiagroB1.Reports/Dtos/PurchaseInvoicesByPeriodRowDto.cs SiagroB1.Reports/Services/PurchaseInvoicesByPeriodReportService.cs SiagroB1.Reports/Controllers/PurchaseInvoicesByPeriodController.cs SiagroB1.Reports/Reports/Templates/PurchaseInvoicesByPeriod.frx SiagroB1.Application.Tests/Reports/PurchaseInvoicesByPeriodReportServiceTests.cs SiagroB1.Application.Tests/Reports/InvoiceReportsPdfTests.cs
git commit -m "feat(reports): relatório de notas de entrada por período" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Itens dos Documentos de Saída

**Files:**
- Create: `SiagroB1.Reports/Dtos/SalesInvoiceItemsRequest.cs`, `SiagroB1.Reports/Dtos/InvoiceItemRowDto.cs`
- Create: `SiagroB1.Reports/Services/SalesInvoiceItemsReportService.cs`
- Create: `SiagroB1.Reports/Controllers/SalesInvoiceItemsController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/SalesInvoiceItems.frx`
- Test: `SiagroB1.Application.Tests/Reports/SalesInvoiceItemsReportServiceTests.cs`
- Modify (test): `SiagroB1.Application.Tests/Reports/InvoiceReportsPdfTests.cs`

**Interfaces:**
- Consumes: Task 1, `InvoiceReportSeed` (Task 2).
- Produces: `InvoiceItemRowDto` (reutilizado na Task 5) com `Group`, `IssueDate`, `DocumentNumber`, `InternalNumber`, `Partner`, `Cfop`, `Usage`, `Quantity`, `UnitOfMeasure`, `UnitPrice`, `Total`, `Icms`, `Pis`, `Cofins`, `IbsCbs`, `GrandTotal`, `Contract`; `SalesInvoiceItemsReportService(IUnitOfWork, IFastReportService, IConfiguration)`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Reports/SalesInvoiceItemsReportServiceTests.cs`:

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class SalesInvoiceItemsReportServiceTests
{
    [Fact]
    public async Task BuildRows_OneRowPerItemGroupedByProductAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("2", new DateTime(2026, 7, 20),
            items: [SaleItem("10001", "SOJA", 5m), SaleItem("10002", "MILHO", 3m)]));
        db.Context.SalesInvoices.Add(Sale("1", new DateTime(2026, 7, 10),
            items: [SaleItem("10001", "SOJA", 7m), SaleItem("10001", "SOJA", 900m, uom: "KG")]));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { "MILHO (10002) - TN", "SOJA (10001) - KG", "SOJA (10001) - TN", "SOJA (10001) - TN" },
            rows.Select(r => r.Group));
        Assert.Equal(new[] { 3m, 900m, 7m, 5m }, rows.Select(r => r.Quantity));
    }

    [Fact]
    public async Task BuildRows_ItemFilterRestrictsLinesNotOnlyDocuments()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("1", items: [SaleItem("10001", "SOJA"), SaleItem("10002", "MILHO")]));
        await Save(db);

        var request = Request();
        request.ItemCode = "10002";
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "MILHO (10002) - TN" }, rows.Select(r => r.Group));
    }

    [Theory]
    [InlineData("Cfop")]
    [InlineData("ContractCode")]
    [InlineData("InvoiceType")]
    [InlineData("Status")]
    public async Task BuildRows_EachLineFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "CV-1", CardCode = "C001", ItemCode = "10001",
            Status = ContractStatus.Approved, CreationDate = Jul01,
        };
        db.Context.SalesContracts.Add(contract);
        var match = SaleItem();
        match.Cfop = "5101";
        match.SalesContractKey = contract.Key;
        var other = SaleItem();
        other.Cfop = "6101";
        db.Context.SalesInvoices.Add(Sale("MATCH", items: [match]));
        db.Context.SalesInvoices.Add(Sale("OTHER", status: InvoiceStatus.Cancelled, type: SalesInvoiceType.Return, items: [other]));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "Cfop": request.Cfop = "5101"; request.Statuses = [InvoiceStatus.Confirmed, InvoiceStatus.Cancelled]; break;
            case "ContractCode": request.ContractCode = "CV-1"; request.Statuses = [InvoiceStatus.Confirmed, InvoiceStatus.Cancelled]; break;
            case "InvoiceType": request.InvoiceType = SalesInvoiceType.Normal; request.Statuses = [InvoiceStatus.Confirmed, InvoiceStatus.Cancelled]; break;
            case "Status": break; // padrão já exclui o cancelado
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "MATCH" }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_FormatsTheLine()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "CV-9", CardCode = "C001", ItemCode = "10001",
            Status = ContractStatus.Approved, CreationDate = Jul01,
        };
        db.Context.SalesContracts.Add(contract);
        var item = SaleItem(quantity: 10m, unitPrice: 100m);
        item.Cfop = "5101";
        item.UsageName = "VENDA DE PRODUÇÃO";
        item.FreightValue = 10m;
        item.IcmsValue = 12m;
        item.PisValue = 1m;
        item.CofinsValue = 2m;
        item.SalesContractKey = contract.Key;
        var invoice = Sale("1", items: [item]);
        invoice.TaxDocumentNumber = "321";
        invoice.TaxDocumentSeries = "1";
        db.Context.SalesInvoices.Add(invoice);
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("15/07/2026", row.IssueDate);
        Assert.Equal("321/1", row.DocumentNumber);
        Assert.Equal("(C001) COOPERATIVA CENTRAL", row.Partner);
        Assert.Equal("5101", row.Cfop);
        Assert.Equal("VENDA DE PRODUÇÃO", row.Usage);
        Assert.Equal(10m, row.Quantity);
        Assert.Equal("TN", row.UnitOfMeasure);
        Assert.Equal(100m, row.UnitPrice);
        Assert.Equal(1000m, row.Total);
        Assert.Equal(12m, row.Icms);
        Assert.Equal(1m, row.Pis);
        Assert.Equal(2m, row.Cofins);
        Assert.Equal(1010m, row.GrandTotal);
        Assert.Equal("CV-9", row.Contract);
    }

    [Theory]
    [InlineData("STANDALONE", true)]
    [InlineData("SAPB1", false)]
    public async Task Execute_PassesTheMode(string erp, bool standalone)
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        await new SalesInvoiceItemsReportService(db, recorder, TestConfiguration.Erp(erp)).ExecuteAsync(Request());

        Assert.Equal("SalesInvoiceItems.frx", recorder.LastReportName);
        Assert.Equal(standalone, recorder.LastParameters![FastReportService.StandaloneParameter]);
    }

    private static SalesInvoiceItemsReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService(), TestConfiguration.Erp("STANDALONE"));

    private static SalesInvoiceItemsRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
```

Antes de rodar: abra `SiagroB1.Domain/Entities/SalesContract.cs` e confirme os `required` do contrato; complete o inicializador do teste se o compilador acusar `CS9035`.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoiceItemsReportServiceTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement**

`SiagroB1.Reports/Dtos/SalesInvoiceItemsRequest.cs`:

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Itens dos Documentos de Saída". Produto filtra a LINHA.</summary>
public class SalesInvoiceItemsRequest : InvoiceReportRequest
{
    public SalesInvoiceType? InvoiceType { get; set; }

    /// <summary>Código do contrato de venda (SalesContract.Code).</summary>
    public string? ContractCode { get; set; }

    public string? Cfop { get; set; }
}
```

`SiagroB1.Reports/Dtos/InvoiceItemRowDto.cs`:

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>Linha dos relatórios de itens (saída e entrada). <see cref="Group"/> = produto + UM.</summary>
public class InvoiceItemRowDto
{
    public string Group { get; set; } = "";
    public string IssueDate { get; set; } = "";
    public string InternalNumber { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string Partner { get; set; } = "";
    public string Cfop { get; set; } = "";
    public string Usage { get; set; } = "";
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
    public decimal Icms { get; set; }
    public decimal Pis { get; set; }
    public decimal Cofins { get; set; }
    public decimal IbsCbs { get; set; }
    public decimal GrandTotal { get; set; }
    public string Contract { get; set; } = "";
}
```

`SiagroB1.Reports/Services/SalesInvoiceItemsReportService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Itens dos documentos de saída, agrupados por produto E unidade de medida — o subtotal de
/// quantidade nunca soma KG com TN. Produto, UM, CFOP, natureza e tributos vêm do snapshot da
/// linha; contrato por LEFT JOIN (FK opcional).
/// </summary>
public class SalesInvoiceItemsReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private bool Standalone => ErpMode.IsStandalone(configuration);

    public async Task<List<InvoiceItemRowDto>> BuildRowsAsync(SalesInvoiceItemsRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        var query = db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Include(i => i.SalesInvoice)
            .Include(i => i.SalesContract)
            .Where(i => i.SalesInvoice != null)
            .Where(i => i.SalesInvoice!.InvoiceDate >= from && i.SalesInvoice.InvoiceDate < toExclusive)
            .Where(i => statuses.Contains(i.SalesInvoice!.InvoiceStatus ?? InvoiceStatus.Pending));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(i => i.SalesInvoice!.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(i => i.SalesInvoice!.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(i => i.ItemCode == request.ItemCode);

        if (request.InvoiceType is { } type)
            query = query.Where(i => i.SalesInvoice!.InvoiceType == type);

        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            query = query.Where(i => i.SalesContract != null && i.SalesContract.Code == request.ContractCode);

        if (!string.IsNullOrWhiteSpace(request.Cfop))
            query = query.Where(i => i.Cfop == request.Cfop);

        if (Standalone && request.NfeStatuses is { Count: > 0 })
        {
            var nfe = request.NfeStatuses.Distinct().ToArray();
            query = query.Where(i => nfe.Contains(i.SalesInvoice!.NfeStatus));
        }

        var items = await query.ToListAsync();

        return items
            .OrderBy(i => GroupOf(i), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.SalesInvoice!.InvoiceDate)
            .ThenBy(i => i.SalesInvoice!.InvoiceNumber ?? "", StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(SalesInvoiceItemsRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var standalone = Standalone;
        var first = rows.Count > 0 ? rows[0] : null;

        var extra = new List<string>();
        if (request.InvoiceType is { } type)
            extra.Add($"Tipo: {(type == SalesInvoiceType.Return ? "Devolução" : "Normal")}");
        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            extra.Add($"Contrato: {request.ContractCode}");
        if (!string.IsNullOrWhiteSpace(request.Cfop))
            extra.Add($"CFOP: {request.Cfop}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = InvoiceReportText.BuildFilters(
                request, standalone, "Cliente", first?.Partner, ProductName(first), null, extra),
            [FastReportService.StandaloneParameter] = standalone,
        };

        return await reportService.GeneratePdfAsync(
            "SalesInvoiceItems.frx", rows, "InvoiceItems", "InvoiceItems", parameters);
    }

    private static string GroupOf(SalesInvoiceItem i) =>
        $"{InvoiceReportText.Product(i.ItemCode, i.ItemName)} - {i.UnitOfMeasureCode}";

    private static InvoiceItemRowDto ToRow(SalesInvoiceItem i) => new()
    {
        Group = GroupOf(i),
        IssueDate = InvoiceReportText.Date(i.SalesInvoice!.InvoiceDate),
        InternalNumber = i.SalesInvoice.InvoiceNumber ?? "",
        DocumentNumber = InvoiceReportText.DocumentNumber(i.SalesInvoice.TaxDocumentNumber, i.SalesInvoice.TaxDocumentSeries),
        Partner = InvoiceReportText.Partner(i.SalesInvoice.CardCode, i.SalesInvoice.CardName),
        Cfop = i.Cfop ?? "",
        Usage = i.UsageName ?? "",
        Quantity = i.Quantity,
        UnitOfMeasure = i.UnitOfMeasureCode,
        UnitPrice = i.UnitPrice,
        Total = i.Total,
        Icms = i.IcmsValue,
        Pis = i.PisValue,
        Cofins = i.CofinsValue,
        IbsCbs = i.TotalIbsCbs,
        GrandTotal = i.GrandTotal,
        Contract = i.SalesContract?.Code ?? "",
    };

    /// <summary>Nome do produto filtrado, sem o sufixo " - UM" do grupo.</summary>
    private static string? ProductName(InvoiceItemRowDto? row)
    {
        if (row is null)
            return null;

        var cut = row.Group.LastIndexOf(" - ", StringComparison.Ordinal);
        return cut > 0 ? row.Group[..cut] : row.Group;
    }
}
```

`SiagroB1.Reports/Controllers/SalesInvoiceItemsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/SalesInvoiceItems")]
public class SalesInvoiceItemsController(SalesInvoiceItemsReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] SalesInvoiceItemsRequest request)
    {
        if (InvoiceReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"sales-invoice-items.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoiceItemsReportServiceTests"`
Expected: PASS.

- [ ] **Step 5: Template**

`SalesInvoiceItems.frx` pelo Appendix A, paisagem, **com** agrupamento: `GroupHeader1 Condition="[InvoiceItems.Group]"` imprimindo `[InvoiceItems.Group]` em negrito. Título `Itens dos Documentos de Saída`, fonte `InvoiceItems`.

| Coluna | Campo | Largura | Formato | Nome |
|---|---|---|---|---|
| Emissão | IssueDate | 58 | texto | txtIssueDate |
| NF/Série | DocumentNumber | 62 | texto | txtDocumentNumber |
| Nº interno | InternalNumber | 52 | texto | txtInternalNumber |
| Cliente | Partner | 170 | texto | txtPartner |
| CFOP | Cfop | 38 | texto | txtCfop |
| Natureza | Usage | 120 | texto | txtUsage |
| Qtd | Quantity | 70 | 3 casas | txtQuantity |
| UM | UnitOfMeasure | 30 | texto | txtUom |
| Preço | UnitPrice | 65 | 4 casas | txtUnitPrice |
| Total | Total | 72 | 2 casas | txtTotal |
| Total geral | GrandTotal | 75 | 2 casas | txtGrandTotal |
| Contrato | Contract | 62 | texto | txtContract |
| ICMS | Icms | 52 | 2 casas | fiscalIcms |
| PIS | Pis | 45 | 2 casas | fiscalPis |
| COFINS | Cofins | 50 | 2 casas | fiscalCofins |
| IBS/CBS | IbsCbs | 50 | 2 casas | fiscalIbsCbs |

`GroupFooter1`: subtotal de Quantity, Total, GrandTotal (`txtGrp*`) e Icms/Pis/Cofins/IbsCbs (`fiscalGrp*`). Sumário: Total, GrandTotal, `fiscalSum*` e contagem de itens — **sem** soma geral de Quantity (UMs diferentes).

- [ ] **Step 6: PDF test**

Acrescente a `InvoiceReportsPdfTests`:

```csharp
    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task SalesInvoiceItems_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("1", items: [SaleItem("10001", "SOJA"), SaleItem("10002", "MILHO", uom: "KG")]));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration(erp);
        var pdf = await new SalesInvoiceItemsReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new SalesInvoiceItemsRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesInvoiceItems", erp, pdf);
    }
```

- [ ] **Step 7: Run report tests + visual check**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"` — Expected: PASS. Confira `%TEMP%/siagro-invoice-reports/SalesInvoiceItems-*.pdf`.

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Reports/Dtos/SalesInvoiceItemsRequest.cs SiagroB1.Reports/Dtos/InvoiceItemRowDto.cs SiagroB1.Reports/Services/SalesInvoiceItemsReportService.cs SiagroB1.Reports/Controllers/SalesInvoiceItemsController.cs SiagroB1.Reports/Reports/Templates/SalesInvoiceItems.frx SiagroB1.Application.Tests/Reports/SalesInvoiceItemsReportServiceTests.cs SiagroB1.Application.Tests/Reports/InvoiceReportsPdfTests.cs
git commit -m "feat(reports): relatório de itens dos documentos de saída" -m "Agrupa por produto e unidade de medida para o subtotal de quantidade não somar KG com TN." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Itens dos Documentos de Entrada

**Files:**
- Create: `SiagroB1.Reports/Dtos/PurchaseInvoiceItemsRequest.cs`
- Create: `SiagroB1.Reports/Services/PurchaseInvoiceItemsReportService.cs`
- Create: `SiagroB1.Reports/Controllers/PurchaseInvoiceItemsController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/PurchaseInvoiceItems.frx`
- Test: `SiagroB1.Application.Tests/Reports/PurchaseInvoiceItemsReportServiceTests.cs`
- Modify (test): `SiagroB1.Application.Tests/Reports/InvoiceReportsPdfTests.cs`

**Interfaces:**
- Consumes: Task 1; `InvoiceItemRowDto` (Task 4); `PurchaseInvoicesByPeriodReportService.DescribeType/DescribeIssuer` (`internal static`, Task 3); `InvoiceReportSeed` (Task 2).
- Produces: `PurchaseInvoiceItemsReportService(IUnitOfWork, IFastReportService, IConfiguration)`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Reports/PurchaseInvoiceItemsReportServiceTests.cs`:

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class PurchaseInvoiceItemsReportServiceTests
{
    [Fact]
    public async Task BuildRows_ItemWithoutProductGoesToItsOwnGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("1", items:
            [PurchaseItem("10001", "SOJA"), PurchaseItem(null, "SOJA DO XML", uom: null), PurchaseItem(null, null, uom: null)]));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { "Sem produto vinculado", "SOJA (10001) - TN", "SOJA DO XML" },
            rows.Select(r => r.Group).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("ItemCode")]
    [InlineData("IssuerType")]
    [InlineData("ContractCode")]
    [InlineData("Cfop")]
    public async Task BuildRows_EachFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "CC-1", CardCode = "F001", ItemCode = "10001",
            Status = ContractStatus.Approved, CreationDate = Jul01,
        };
        db.Context.PurchaseContracts.Add(contract);
        var match = PurchaseItem();
        match.Cfop = "1101";
        match.PurchaseContractKey = contract.Key;
        var other = PurchaseItem(itemCode: "99999");
        other.Cfop = "2101";
        db.Context.PurchaseInvoices.Add(Purchase("MATCH", items: [match]));
        db.Context.PurchaseInvoices.Add(Purchase("OTHER", issuer: DocumentIssuerType.Own, items: [other]));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "ItemCode": request.ItemCode = "10001"; break;
            case "IssuerType": request.IssuerType = DocumentIssuerType.ThirdParty; break;
            case "ContractCode": request.ContractCode = "CC-1"; break;
            case "Cfop": request.Cfop = "1101"; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "MATCH" }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_FormatsTheLine()
    {
        var db = TestDb.CreateUnitOfWork();
        var item = PurchaseItem(quantity: 4m, unitPrice: 25m);
        item.Cfop = "1101";
        item.UsageName = "COMPRA PARA COMERCIALIZAÇÃO";
        item.IcmsValue = 3m;
        db.Context.PurchaseInvoices.Add(Purchase("8", items: [item]));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("(F001) PRODUTOR RURAL", row.Partner);
        Assert.Equal("1101", row.Cfop);
        Assert.Equal("COMPRA PARA COMERCIALIZAÇÃO", row.Usage);
        Assert.Equal(100m, row.Total);
        Assert.Equal(3m, row.Icms);
        Assert.Equal("", row.Contract);
    }

    [Theory]
    [InlineData("STANDALONE", true)]
    [InlineData("SAPB1", false)]
    public async Task Execute_PassesTheMode(string erp, bool standalone)
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        await new PurchaseInvoiceItemsReportService(db, recorder, TestConfiguration.Erp(erp)).ExecuteAsync(Request());

        Assert.Equal("PurchaseInvoiceItems.frx", recorder.LastReportName);
        Assert.Equal(standalone, recorder.LastParameters![FastReportService.StandaloneParameter]);
    }

    private static PurchaseInvoiceItemsReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService(), TestConfiguration.Erp("STANDALONE"));

    private static PurchaseInvoiceItemsRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
```

Antes de rodar: confirme os `required` de `PurchaseContract` (`SiagroB1.Domain/Entities/PurchaseContract.cs`) e complete o inicializador.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceItemsReportServiceTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement**

`SiagroB1.Reports/Dtos/PurchaseInvoiceItemsRequest.cs`:

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Itens dos Documentos de Entrada". Produto filtra a LINHA.</summary>
public class PurchaseInvoiceItemsRequest : InvoiceReportRequest
{
    public PurchaseInvoiceType? InvoiceType { get; set; }

    public DocumentIssuerType? IssuerType { get; set; }

    /// <summary>Código do contrato de compra (PurchaseContract.Code).</summary>
    public string? ContractCode { get; set; }

    public string? Cfop { get; set; }
}
```

`SiagroB1.Reports/Services/PurchaseInvoiceItemsReportService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Itens dos documentos de entrada, agrupados por produto e UM. Entrada importada de XML pode
/// não ter produto vinculado (ItemCode nulo): agrupa pelo nome do XML ou em
/// "Sem produto vinculado" — nunca some do relatório.
/// </summary>
public class PurchaseInvoiceItemsReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private bool Standalone => ErpMode.IsStandalone(configuration);

    public async Task<List<InvoiceItemRowDto>> BuildRowsAsync(PurchaseInvoiceItemsRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        var query = db.Context.PurchaseInvoicesItems
            .AsNoTracking()
            .Include(i => i.PurchaseInvoice)
            .Include(i => i.PurchaseContract)
            .Where(i => i.PurchaseInvoice != null)
            .Where(i => i.PurchaseInvoice!.IssueDate >= from && i.PurchaseInvoice.IssueDate < toExclusive)
            .Where(i => statuses.Contains(i.PurchaseInvoice!.InvoiceStatus));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(i => i.PurchaseInvoice!.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(i => i.PurchaseInvoice!.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(i => i.ItemCode == request.ItemCode);

        if (request.InvoiceType is { } type)
            query = query.Where(i => i.PurchaseInvoice!.InvoiceType == type);

        if (request.IssuerType is { } issuer)
            query = query.Where(i => i.PurchaseInvoice!.IssuerType == issuer);

        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            query = query.Where(i => i.PurchaseContract != null && i.PurchaseContract.Code == request.ContractCode);

        if (!string.IsNullOrWhiteSpace(request.Cfop))
            query = query.Where(i => i.Cfop == request.Cfop);

        if (Standalone && request.NfeStatuses is { Count: > 0 })
        {
            var nfe = request.NfeStatuses.Distinct().ToArray();
            query = query.Where(i => nfe.Contains(i.PurchaseInvoice!.NfeStatus));
        }

        var items = await query.ToListAsync();

        return items
            .OrderBy(i => GroupOf(i), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.PurchaseInvoice!.IssueDate)
            .ThenBy(i => i.PurchaseInvoice!.InvoiceNumber ?? "", StringComparer.Ordinal)
            .Select(ToRow)
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(PurchaseInvoiceItemsRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var standalone = Standalone;
        var first = rows.Count > 0 ? rows[0] : null;

        var extra = new List<string>();
        if (request.InvoiceType is { } type)
            extra.Add($"Tipo: {PurchaseInvoicesByPeriodReportService.DescribeType(type)}");
        if (request.IssuerType is { } issuer)
            extra.Add($"Emissão: {PurchaseInvoicesByPeriodReportService.DescribeIssuer(issuer)}");
        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            extra.Add($"Contrato: {request.ContractCode}");
        if (!string.IsNullOrWhiteSpace(request.Cfop))
            extra.Add($"CFOP: {request.Cfop}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = InvoiceReportText.BuildFilters(
                request, standalone, "Emitente", first?.Partner, null, null, extra),
            [FastReportService.StandaloneParameter] = standalone,
        };

        return await reportService.GeneratePdfAsync(
            "PurchaseInvoiceItems.frx", rows, "InvoiceItems", "InvoiceItems", parameters);
    }

    private static string GroupOf(PurchaseInvoiceItem i)
    {
        var product = InvoiceReportText.Product(i.ItemCode, i.ItemName);
        return string.IsNullOrWhiteSpace(i.UnitOfMeasureCode) ? product : $"{product} - {i.UnitOfMeasureCode}";
    }

    private static InvoiceItemRowDto ToRow(PurchaseInvoiceItem i) => new()
    {
        Group = GroupOf(i),
        IssueDate = InvoiceReportText.Date(i.PurchaseInvoice!.IssueDate),
        InternalNumber = i.PurchaseInvoice.InvoiceNumber ?? "",
        DocumentNumber = InvoiceReportText.DocumentNumber(i.PurchaseInvoice.TaxDocumentNumber, i.PurchaseInvoice.TaxDocumentSeries),
        Partner = InvoiceReportText.Partner(i.PurchaseInvoice.CardCode, i.PurchaseInvoice.CardName),
        Cfop = i.Cfop ?? "",
        Usage = i.UsageName ?? "",
        Quantity = i.Quantity,
        UnitOfMeasure = i.UnitOfMeasureCode ?? "",
        UnitPrice = i.UnitPrice,
        Total = i.Total,
        Icms = i.IcmsValue,
        Pis = i.PisValue,
        Cofins = i.CofinsValue,
        IbsCbs = i.TotalIbsCbs,
        GrandTotal = i.GrandTotal,
        Contract = i.PurchaseContract?.Code ?? "",
    };
}
```


`SiagroB1.Reports/Controllers/PurchaseInvoiceItemsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/PurchaseInvoiceItems")]
public class PurchaseInvoiceItemsController(PurchaseInvoiceItemsReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] PurchaseInvoiceItemsRequest request)
    {
        if (InvoiceReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"purchase-invoice-items.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~InvoiceItemsReportServiceTests"`
Expected: PASS (saída e entrada).

- [ ] **Step 5: Template**

Copie `SalesInvoiceItems.frx` (Task 4) para `PurchaseInvoiceItems.frx`; troque o título para `Itens dos Documentos de Entrada` e o cabeçalho da coluna `Cliente` para `Emitente`. Mesma fonte `InvoiceItems`, mesmas colunas, larguras e nomes.

- [ ] **Step 6: PDF test**

Acrescente a `InvoiceReportsPdfTests`:

```csharp
    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task PurchaseInvoiceItems_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("1", items: [PurchaseItem(), PurchaseItem(null, "SOJA DO XML", uom: null)]));
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration(erp);
        var pdf = await new PurchaseInvoiceItemsReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new PurchaseInvoiceItemsRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("PurchaseInvoiceItems", erp, pdf);
    }
```

- [ ] **Step 7: Run report tests + visual check**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"` — Expected: PASS. Confira `%TEMP%/siagro-invoice-reports/PurchaseInvoiceItems-*.pdf`.

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Reports/Dtos/PurchaseInvoiceItemsRequest.cs SiagroB1.Reports/Services/PurchaseInvoiceItemsReportService.cs SiagroB1.Reports/Controllers/PurchaseInvoiceItemsController.cs SiagroB1.Reports/Reports/Templates/PurchaseInvoiceItems.frx SiagroB1.Application.Tests/Reports/PurchaseInvoiceItemsReportServiceTests.cs SiagroB1.Application.Tests/Reports/InvoiceReportsPdfTests.cs
git commit -m "feat(reports): relatório de itens dos documentos de entrada" -m "Item sem produto vinculado (XML importado) agrupa pelo nome do XML em vez de sumir." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Devoluções de Venda

**Files:**
- Create: `SiagroB1.Reports/Dtos/SalesReturnsRequest.cs`, `SiagroB1.Reports/Dtos/SalesReturnRowDto.cs`
- Create: `SiagroB1.Reports/Services/SalesReturnsReportService.cs`
- Create: `SiagroB1.Reports/Controllers/SalesReturnsController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/SalesReturns.frx`
- Test: `SiagroB1.Application.Tests/Reports/SalesReturnsReportServiceTests.cs`
- Modify (test): `SiagroB1.Application.Tests/Reports/InvoiceReportsPdfTests.cs`

**Interfaces:**
- Consumes: Task 1, `InvoiceReportSeed` (Task 2).
- Produces: `enum SalesReturnSource { Own, Customer }`; `SalesReturnsReportService(IUnitOfWork, IFastReportService, IConfiguration)`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Reports/SalesReturnsReportServiceTests.cs`:

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class SalesReturnsReportServiceTests
{
    [Fact]
    public async Task BuildRows_BringsBothSourcesLinkedToTheOriginalInvoice()
    {
        var db = TestDb.CreateUnitOfWork();
        var originalItem = SaleItem(quantity: 30m);
        var original = Sale("100", new DateTime(2026, 6, 20), items: [originalItem]);
        original.TaxDocumentNumber = "5000";
        original.TaxDocumentSeries = "1";
        db.Context.SalesInvoices.Add(original);

        var ownItem = SaleItem(quantity: 5m, unitPrice: 100m);
        ownItem.SalesInvoiceItemOriginKey = originalItem.Key;
        db.Context.SalesInvoices.Add(Sale("101", new DateTime(2026, 7, 10), type: SalesInvoiceType.Return, items: [ownItem]));

        var customerItem = PurchaseItem(quantity: 2m, unitPrice: 100m);
        customerItem.SalesInvoiceItemKey = originalItem.Key;
        db.Context.PurchaseInvoices.Add(Purchase("900", new DateTime(2026, 7, 12), type: PurchaseInvoiceType.Return,
            cardCode: "C001", cardName: "COOPERATIVA CENTRAL", items: [customerItem]));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "Própria", "Cliente" }, rows.Select(r => r.Source));
        Assert.All(rows, r => Assert.Equal("5000/1 de 20/06/2026", r.Origin));
        Assert.Equal(new[] { 5m, 2m }, rows.Select(r => r.Quantity));
        Assert.Equal(new[] { 500m, 200m }, rows.Select(r => r.Value));
        Assert.All(rows, r => Assert.Equal("(C001) COOPERATIVA CENTRAL", r.Customer));
    }

    [Fact]
    public async Task BuildRows_IgnoresNonReturnsAndOwnPurchaseReturns()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("NORMAL"));
        db.Context.PurchaseInvoices.Add(Purchase("ENTRADA"));
        // Devolução de COMPRA (emissão própria) não é devolução de venda.
        db.Context.PurchaseInvoices.Add(Purchase("DEV-COMPRA", type: PurchaseInvoiceType.Return, issuer: DocumentIssuerType.Own));
        await Save(db);

        Assert.Empty(await Service(db).BuildRowsAsync(Request()));
    }

    [Fact]
    public async Task BuildRows_WithoutLinkPrintsADash()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("101", type: SalesInvoiceType.Return));
        db.Context.PurchaseInvoices.Add(Purchase("900", type: PurchaseInvoiceType.Return));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(InvoiceReportText.NoOrigin, r.Origin));
    }

    [Theory]
    [InlineData(SalesReturnSource.Own, "101")]
    [InlineData(SalesReturnSource.Customer, "900")]
    public async Task BuildRows_SourceFilter(SalesReturnSource source, string expected)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("101", type: SalesInvoiceType.Return));
        db.Context.PurchaseInvoices.Add(Purchase("900", type: PurchaseInvoiceType.Return));
        await Save(db);

        var request = Request();
        request.Source = source;
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { expected }, rows.Select(r => r.InternalNumber));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelledReturns()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("101", type: SalesInvoiceType.Return, status: InvoiceStatus.Cancelled));
        db.Context.PurchaseInvoices.Add(Purchase("900", type: PurchaseInvoiceType.Return, status: InvoiceStatus.Cancelled));
        await Save(db);

        Assert.Empty(await Service(db).BuildRowsAsync(Request()));
    }

    [Fact]
    public async Task BuildRows_OrdersByCustomerThenDate()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sale("B2", new DateTime(2026, 7, 20), type: SalesInvoiceType.Return, cardCode: "C2", cardName: "BETA"));
        db.Context.SalesInvoices.Add(Sale("A1", new DateTime(2026, 7, 25), type: SalesInvoiceType.Return, cardCode: "C1", cardName: "ALFA"));
        db.Context.SalesInvoices.Add(Sale("B1", new DateTime(2026, 7, 5), type: SalesInvoiceType.Return, cardCode: "C2", cardName: "BETA"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "A1", "B1", "B2" }, rows.Select(r => r.InternalNumber));
    }

    [Theory]
    [InlineData("STANDALONE", true)]
    [InlineData("SAPB1", false)]
    public async Task Execute_PassesTheMode(string erp, bool standalone)
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        await new SalesReturnsReportService(db, recorder, TestConfiguration.Erp(erp)).ExecuteAsync(Request());

        Assert.Equal("SalesReturns.frx", recorder.LastReportName);
        Assert.Equal(standalone, recorder.LastParameters![FastReportService.StandaloneParameter]);
    }

    private static SalesReturnsReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService(), TestConfiguration.Erp("STANDALONE"));

    private static SalesReturnsRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesReturnsReportServiceTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement**

`SiagroB1.Reports/Dtos/SalesReturnsRequest.cs`:

```csharp
namespace SiagroB1.Reports.Dtos;

public enum SalesReturnSource
{
    /// <summary>Devolução emitida pela empresa: documento de saída tipo Devolução.</summary>
    Own,

    /// <summary>Nota de devolução emitida pelo cliente, lançada como entrada de terceiro.</summary>
    Customer,
}

/// <summary>Filtros de "Devoluções de Venda". Origem vazia = as duas.</summary>
public class SalesReturnsRequest : InvoiceReportRequest
{
    public SalesReturnSource? Source { get; set; }
}
```

`SiagroB1.Reports/Dtos/SalesReturnRowDto.cs`:

```csharp
namespace SiagroB1.Reports.Dtos;

public class SalesReturnRowDto
{
    public string Customer { get; set; } = "";
    public string Date { get; set; } = "";
    public string Source { get; set; } = "";
    public string InternalNumber { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    /// <summary>"NF/Série de dd/MM/yyyy" da nota original, ou "—" sem vínculo.</summary>
    public string Origin { get; set; } = "";
    public string Product { get; set; } = "";
    public decimal Quantity { get; set; }
    public string UnitOfMeasure { get; set; } = "";
    public decimal Value { get; set; }
    public string Status { get; set; } = "";

    internal DateTime SortDate { get; set; }
}
```

`SiagroB1.Reports/Services/SalesReturnsReportService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Devoluções de venda, por item devolvido, juntando as duas formas que existem no sistema:
/// <list type="bullet">
/// <item>Própria: SalesInvoice tipo Return; a original vem de SalesInvoiceItemOriginKey.</item>
/// <item>Do cliente: PurchaseInvoice tipo Return emitida por TERCEIRO; a original vem de
/// PurchaseInvoiceItem.SalesInvoiceItemKey (vínculo manual, pode faltar). Devolução de compra
/// (emissão própria) não entra.</item>
/// </list>
/// Só leitura: a devolução do cliente não move saldo, o relatório também não.
/// </summary>
public class SalesReturnsReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private bool Standalone => ErpMode.IsStandalone(configuration);

    public async Task<List<SalesReturnRowDto>> BuildRowsAsync(SalesReturnsRequest request)
    {
        var rows = new List<SalesReturnRowDto>();

        if (request.Source is null or SalesReturnSource.Own)
            rows.AddRange(await OwnAsync(request));

        if (request.Source is null or SalesReturnSource.Customer)
            rows.AddRange(await CustomerAsync(request));

        return rows
            .OrderBy(r => r.Customer, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.SortDate)
            .ThenBy(r => r.InternalNumber, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(SalesReturnsRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var standalone = Standalone;
        var first = rows.Count > 0 ? rows[0] : null;

        var extra = request.Source switch
        {
            SalesReturnSource.Own => new[] { "Emissão: Própria" },
            SalesReturnSource.Customer => ["Emissão: Cliente"],
            _ => [],
        };

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = InvoiceReportText.BuildFilters(
                request, standalone, "Cliente", first?.Customer, first?.Product, null, extra),
            [FastReportService.StandaloneParameter] = standalone,
        };

        return await reportService.GeneratePdfAsync(
            "SalesReturns.frx", rows, "SalesReturns", "SalesReturns", parameters);
    }

    private async Task<List<SalesReturnRowDto>> OwnAsync(SalesReturnsRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        var query = db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Include(i => i.SalesInvoice)
            .Include(i => i.SalesInvoiceItemOrigin).ThenInclude(o => o!.SalesInvoice)
            .Where(i => i.SalesInvoice != null && i.SalesInvoice.InvoiceType == SalesInvoiceType.Return)
            .Where(i => i.SalesInvoice!.InvoiceDate >= from && i.SalesInvoice.InvoiceDate < toExclusive)
            .Where(i => statuses.Contains(i.SalesInvoice!.InvoiceStatus ?? InvoiceStatus.Pending));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(i => i.SalesInvoice!.BranchCode == request.BranchCode);
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(i => i.SalesInvoice!.CardCode == request.CardCode);
        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(i => i.ItemCode == request.ItemCode);

        var items = await query.ToListAsync();

        return items.Select(i => new SalesReturnRowDto
        {
            Customer = InvoiceReportText.Partner(i.SalesInvoice!.CardCode, i.SalesInvoice.CardName),
            Date = InvoiceReportText.Date(i.SalesInvoice.InvoiceDate),
            SortDate = i.SalesInvoice.InvoiceDate ?? DateTime.MinValue,
            Source = "Própria",
            InternalNumber = i.SalesInvoice.InvoiceNumber ?? "",
            DocumentNumber = InvoiceReportText.DocumentNumber(i.SalesInvoice.TaxDocumentNumber, i.SalesInvoice.TaxDocumentSeries),
            Origin = DescribeOrigin(i.SalesInvoiceItemOrigin?.SalesInvoice),
            Product = InvoiceReportText.Product(i.ItemCode, i.ItemName),
            Quantity = i.Quantity,
            UnitOfMeasure = i.UnitOfMeasureCode,
            Value = i.GrandTotal,
            Status = InvoiceReportText.Status(i.SalesInvoice.InvoiceStatus),
        }).ToList();
    }

    private async Task<List<SalesReturnRowDto>> CustomerAsync(SalesReturnsRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = InvoiceReportText.EffectiveStatuses(request.Statuses);

        var query = db.Context.PurchaseInvoicesItems
            .AsNoTracking()
            .Include(i => i.PurchaseInvoice)
            .Include(i => i.SalesInvoiceItem).ThenInclude(o => o!.SalesInvoice)
            .Where(i => i.PurchaseInvoice != null
                        && i.PurchaseInvoice.InvoiceType == PurchaseInvoiceType.Return
                        && i.PurchaseInvoice.IssuerType == DocumentIssuerType.ThirdParty)
            .Where(i => i.PurchaseInvoice!.IssueDate >= from && i.PurchaseInvoice.IssueDate < toExclusive)
            .Where(i => statuses.Contains(i.PurchaseInvoice!.InvoiceStatus));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(i => i.PurchaseInvoice!.BranchCode == request.BranchCode);
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(i => i.PurchaseInvoice!.CardCode == request.CardCode);
        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(i => i.ItemCode == request.ItemCode);

        var items = await query.ToListAsync();

        return items.Select(i => new SalesReturnRowDto
        {
            Customer = InvoiceReportText.Partner(i.PurchaseInvoice!.CardCode, i.PurchaseInvoice.CardName),
            Date = InvoiceReportText.Date(i.PurchaseInvoice.IssueDate),
            SortDate = i.PurchaseInvoice.IssueDate ?? DateTime.MinValue,
            Source = "Cliente",
            InternalNumber = i.PurchaseInvoice.InvoiceNumber ?? "",
            DocumentNumber = InvoiceReportText.DocumentNumber(i.PurchaseInvoice.TaxDocumentNumber, i.PurchaseInvoice.TaxDocumentSeries),
            Origin = DescribeOrigin(i.SalesInvoiceItem?.SalesInvoice),
            Product = InvoiceReportText.Product(i.ItemCode, i.ItemName),
            Quantity = i.Quantity,
            UnitOfMeasure = i.UnitOfMeasureCode ?? "",
            Value = i.GrandTotal,
            Status = InvoiceReportText.Status(i.PurchaseInvoice.InvoiceStatus),
        }).ToList();
    }

    /// <summary>
    /// "NF/Série de data" da nota original; sem NF, o número interno; sem vínculo, "—".
    /// </summary>
    private static string DescribeOrigin(SalesInvoice? original)
    {
        if (original is null)
            return InvoiceReportText.NoOrigin;

        var number = InvoiceReportText.DocumentNumber(original.TaxDocumentNumber, original.TaxDocumentSeries);
        if (number.Length == 0)
            number = original.InvoiceNumber ?? InvoiceReportText.NoOrigin;

        return $"{number} de {InvoiceReportText.Date(original.InvoiceDate)}";
    }
}
```

`SiagroB1.Reports/Controllers/SalesReturnsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/SalesReturns")]
public class SalesReturnsController(SalesReturnsReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] SalesReturnsRequest request)
    {
        if (InvoiceReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"sales-returns.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

`SalesReturnSource` chega do frontend como número (0/1) — o Reports não configura `JsonStringEnumConverter`; confira em `SiagroB1.Reports/Program.cs`. Se houver o conversor, o frontend manda `"Own"`/`"Customer"`; ajuste a Task 8 conforme o que encontrar.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesReturnsReportServiceTests"`
Expected: PASS. Se `InvoiceDate ?? DateTime.MinValue` falhar por `InvoiceDate` não ser anulável em alguma entidade, troque pelo valor direto.

- [ ] **Step 5: Template**

`SalesReturns.frx` pelo Appendix A, paisagem, agrupado: `GroupHeader1 Condition="[SalesReturns.Customer]"`. Título `Devoluções de Venda`, fonte `SalesReturns`. Não há coluna fiscal; o template mesmo assim declara `pStandalone`.

| Coluna | Campo | Largura | Formato | Nome |
|---|---|---|---|---|
| Data | Date | 62 | texto | txtDate |
| Emissão | Source | 55 | texto | txtSource |
| Nº interno | InternalNumber | 60 | texto | txtInternalNumber |
| NF/Série | DocumentNumber | 75 | texto | txtDocumentNumber |
| Nota original | Origin | 140 | texto | txtOrigin |
| Produto | Product | 260 | texto | txtProduct |
| Qtd devolvida | Quantity | 85 | 3 casas | txtQuantity |
| UM | UnitOfMeasure | 35 | texto | txtUom |
| Valor | Value | 95 | 2 casas | txtValue |
| Situação | Status | 75 | texto | txtStatus |

`GroupFooter1`: subtotal de Value e contagem; **sem** subtotal de quantidade (o cliente pode ter devolvido produtos/UMs diferentes). Sumário: total de Value e contagem.

- [ ] **Step 6: PDF test**

Acrescente a `InvoiceReportsPdfTests`:

```csharp
    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task SalesReturns_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        var originalItem = SaleItem();
        db.Context.SalesInvoices.Add(Sale("100", new DateTime(2026, 6, 20), items: [originalItem]));
        var own = SaleItem(quantity: 2m);
        own.SalesInvoiceItemOriginKey = originalItem.Key;
        db.Context.SalesInvoices.Add(Sale("101", type: SalesInvoiceType.Return, items: [own]));
        db.Context.PurchaseInvoices.Add(Purchase("900", type: PurchaseInvoiceType.Return)); // sem vínculo
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = Configuration(erp);
        var pdf = await new SalesReturnsReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new SalesReturnsRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesReturns", erp, pdf);
    }
```

- [ ] **Step 7: Run all backend tests + visual check**

Run: `dotnet test SiagroB1.Application.Tests` — Expected: PASS (suíte inteira). Confira os 10 PDFs em `%TEMP%/siagro-invoice-reports/`.

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Reports/Dtos/SalesReturnsRequest.cs SiagroB1.Reports/Dtos/SalesReturnRowDto.cs SiagroB1.Reports/Services/SalesReturnsReportService.cs SiagroB1.Reports/Controllers/SalesReturnsController.cs SiagroB1.Reports/Reports/Templates/SalesReturns.frx SiagroB1.Application.Tests/Reports/SalesReturnsReportServiceTests.cs SiagroB1.Application.Tests/Reports/InvoiceReportsPdfTests.cs
git commit -m "feat(reports): relatório de devoluções de venda" -m "Junta a devolução própria (saída tipo Devolução) e a emitida pelo cliente (entrada de terceiro tipo Devolução), cada uma ligada à nota original pelo seu vínculo de item." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Itens de menu

**Files:**
- Create: `SiagroB1.Migrations/CommonContext/<timestamp>_AddFiscalDocumentsReportMenus.cs` (+ `.Designer.cs` gerado)

**Interfaces:**
- Produces: chaves de menu `salesInvoicesReport`, `purchaseInvoicesReport`, `salesInvoiceItemsReport`, `purchaseInvoiceItemsReport`, `salesReturnsReport` (= nomes de rota da Task 8), sob o pai `reports`.

- [ ] **Step 1: Descobrir a próxima ordem**

Run (Git Bash): `grep -rn "\"reports\" *}" SiagroB1.Migrations/CommonContext/*.cs | grep -v Designer`
Anote o maior `Order` usado sob `reports` (as migrations `AddPurchaseContractsByItemReportMenu`=5 e `AddSalesContractsByItemReportMenu`=6 indicam 6). Os novos usam 7–11.

- [ ] **Step 2: Gerar a migration vazia**

Run: `dotnet ef migrations add AddFiscalDocumentsReportMenus --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir CommonContext`
Expected: cria `<timestamp>_AddFiscalDocumentsReportMenus.cs` com `Up`/`Down` vazios e **sem** mudança no `CommonDbContextModelSnapshot.cs` (só dados). Se o snapshot mudar, pare: há drift de modelo não relacionado — investigue antes de seguir. `git add` os arquivos gerados.

- [ ] **Step 3: Preencher Up/Down**

```csharp
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // salesInvoicesReport já tinha rota (relatório antigo, sem menu) e agora ganha o item.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "salesInvoicesReport", "Notas de Saída por Período", "sap-icon://folder-blank", true, false, 7, "reports", false },
                    { "purchaseInvoicesReport", "Notas de Entrada por Período", "sap-icon://folder-blank", true, false, 8, "reports", false },
                    { "salesInvoiceItemsReport", "Itens dos Documentos de Saída", "sap-icon://folder-blank", true, false, 9, "reports", false },
                    { "purchaseInvoiceItemsReport", "Itens dos Documentos de Entrada", "sap-icon://folder-blank", true, false, 10, "reports", false },
                    { "salesReturnsReport", "Devoluções de Venda", "sap-icon://folder-blank", true, false, 11, "reports", false },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E01", "ADMIN", "salesInvoicesReport" },
                    { "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E02", "ADMIN", "purchaseInvoicesReport" },
                    { "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E03", "ADMIN", "salesInvoiceItemsReport" },
                    { "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E04", "ADMIN", "purchaseInvoiceItemsReport" },
                    { "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E05", "ADMIN", "salesReturnsReport" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues:
                [
                    "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E01", "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E02",
                    "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E03", "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E04",
                    "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E05",
                ]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues:
                [
                    "salesInvoicesReport", "purchaseInvoicesReport", "salesInvoiceItemsReport",
                    "purchaseInvoiceItemsReport", "salesReturnsReport",
                ]);
        }
```

Confira o tipo da coluna `ROLE_MENUS.Id` em `20260727145036_AddSalesContractsByItemReportMenu.cs` (lá é string GUID) e o nome exato das colunas de `MENU_ITEMS` em `20261002181753_AddMenuItemStandaloneOnly.cs` — copie a forma deles.

- [ ] **Step 4: Aplicar na base de desenvolvimento STANDALONE e conferir**

Run (PowerShell): `$env:ASPNETCORE_ENVIRONMENT='Ceagui-Development'; dotnet ef database update --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`
**Sempre** com `ASPNETCORE_ENVIRONMENT` explícito (o padrão aponta para produção). Expected: `Done.`
Depois: `sqlcmd -S localhost -E -I -d CEAGUI_SIAGRO_COMMON_DEV -Q "SELECT [Key],[Order] FROM MENU_ITEMS WHERE ParentKey='reports' ORDER BY [Order]"` (confira o nome real do banco Common no `appsettings.Ceagui-Development.json`, chave `SiagroCommon`). Expected: os 5 itens novos.

- [ ] **Step 5: Build e commit**

Run: `dotnet build SiagroB1.sln` — Expected: 0 erros.

```bash
git add SiagroB1.Migrations/CommonContext/*_AddFiscalDocumentsReportMenus*.cs
git commit -m "feat(reports): itens de menu dos relatórios de documentos fiscais" -m "DB: AddFiscalDocumentsReportMenus" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Frontend — cinco telas de filtro

Repo: `siagro-b1-frontend` (crie o branch `feature/fiscal-documents-reports` a partir de `main`, confira com `git status` que a árvore está limpa).

**Files:**
- Create: `webapp/controller/reports/InvoiceReportController.ts`
- Create: `webapp/view/reports/fragments/InvoiceReportCommonFilters.fragment.xml`
- Modify: `webapp/controller/reports/salesInvoices/Main.controller.ts`, `webapp/view/reports/salesInvoices/Main.view.xml`
- Create: `webapp/controller/reports/{purchaseInvoices,salesInvoiceItems,purchaseInvoiceItems,salesReturns}/Main.controller.ts`
- Create: `webapp/view/reports/{purchaseInvoices,salesInvoiceItems,purchaseInvoiceItems,salesReturns}/Main.view.xml`
- Modify: `webapp/manifest.json` (rotas + targets), `webapp/model/ServerRoutes.ts`

**Interfaces:**
- Consumes: endpoints das Tasks 2–6; chaves de menu da Task 7; `CommonController.open*ValueHelp`, `validateForm`, `clearStates`, `setBusy`, `BaseController.refreshStandaloneFlag()` (publica `ui>/standalone`).
- Produces: rotas `salesInvoicesReport` (existente), `purchaseInvoicesReport`, `salesInvoiceItemsReport`, `purchaseInvoiceItemsReport`, `salesReturnsReport`.

Antes de começar: invoque o skill `ui5:ui5-best-practices`. Confira em `webapp/controller/BaseController.ts` que `refreshStandaloneFlag` é `protected` e que existe um model `ui` na view/componente (se não houver, crie um `JSONModel` `ui` no `onInit` do controller-base). Confira de onde `CommonController` herda (`refreshStandaloneFlag` precisa estar acessível a partir dele; se não estiver, chame `getSystemInfo()` direto, como em `usages/Main.controller.ts`).

- [ ] **Step 1: ServerRoutes**

Em `webapp/model/ServerRoutes.ts`, junto de `salesContractsByItemReport`, acrescente:

```ts
  salesInvoicesByPeriodReport: '/reports/SalesInvoicesByPeriod',
  purchaseInvoicesByPeriodReport: '/reports/PurchaseInvoicesByPeriod',
  salesInvoiceItemsReport: '/reports/SalesInvoiceItems',
  purchaseInvoiceItemsReport: '/reports/PurchaseInvoiceItems',
  salesReturnsReport: '/reports/SalesReturns',
```

e remova a entrada antiga que apontava para `/reports/SalesInvoices` se existir (`grep -n "reports/SalesInvoices'" webapp/model/ServerRoutes.ts`).

- [ ] **Step 2: Controller-base**

`webapp/controller/reports/InvoiceReportController.ts`:

```ts
import JSONModel from "sap/ui/model/json/JSONModel";
import MessageBox from "sap/m/MessageBox";
import CommonController from "siagrob1/controller/common/CommonController";

/**
 * Base das telas dos relatórios de documentos fiscais: filtros num JSONModel `params`,
 * situação padrão sem Cancelado e impressão via POST que abre o PDF em nova aba.
 * `ui>/standalone` esconde o filtro de Situação NF-e em SAPB1 (lá a NF-e não sai do Siagro).
 * @namespace siagrob1.controller.reports
 */
export default abstract class InvoiceReportController extends CommonController {

	protected abstract readonly routeName: string;
	protected abstract readonly formId: string;
	protected abstract readonly serverRoute: string;

	onInit(): void {
		this.getView().setModel(new JSONModel(), "params");
		if (!this.getView().getModel("ui")) {
			this.getView().setModel(new JSONModel({ standalone: false }), "ui");
		}

		this.getRouter()
			.getRoute(this.routeName)
			.attachPatternMatched(() => this.routeMatched());
	}

	/** Valores iniciais dos filtros próprios da tela, além dos comuns. */
	protected defaults(): Record<string, unknown> {
		return {};
	}

	private routeMatched() {
		this.clearStates(this.formId);
		(this.getModel("params") as JSONModel).setData({
			// InvoiceStatus: 0 Pendente, 1 Confirmado, 3 Retornado (2 = Cancelado fica de fora).
			// Strings: as chaves dos itens do MultiComboBox são strings; buildPayload converte.
			Statuses: ["0", "1", "3"],
			NfeStatuses: [],
			...this.defaults(),
		});
		void this.refreshStandaloneFlag();
	}

	async onPrintReport() {
		if (!this.validateForm(this.formId)) {
			MessageBox.warning("Por favor, preencha corretamente todos os campos obrigatórios.");
			return;
		}

		const payload = this.buildPayload();

		try {
			this.setBusy(true);

			const response = await fetch(this.serverRoute, {
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify(payload),
			});

			if (!response.ok) {
				const message = await response.text();
				throw new Error(message || "Falha ao gerar relatório.");
			}

			const fileURL = URL.createObjectURL(await response.blob());
			window.open(fileURL, "_blank");
			setTimeout(() => URL.revokeObjectURL(fileURL), 60000);
		} catch (error) {
			MessageBox.error((error as Error)?.message);
		} finally {
			this.setBusy(false);
		}
	}

	/**
	 * MultiComboBox devolve as chaves como string; o backend espera os números dos enums.
	 * Campos vazios viram null para não filtrarem nada.
	 */
	protected buildPayload(): Record<string, unknown> {
		const data = { ...(this.getModel("params") as JSONModel).getData() } as Record<string, unknown>;
		data.Statuses = ((data.Statuses as unknown[]) ?? []).map(Number);
		data.NfeStatuses = ((data.NfeStatuses as unknown[]) ?? []).map(Number);
		for (const key of Object.keys(data)) {
			if (data[key] === "") data[key] = null;
		}
		return data;
	}
}
```

Se `refreshStandaloneFlag` não estiver acessível a partir de `CommonController`, substitua `void this.refreshStandaloneFlag();` por:

```ts
		const ui = this.getView().getModel("ui") as JSONModel;
		ui.setProperty("/standalone", false);
		void this.getSystemInfo().then((info) => ui.setProperty("/standalone", info?.erp !== "SAPB1"));
```

- [ ] **Step 3: Fragmento de filtros comuns**

`webapp/view/reports/fragments/InvoiceReportCommonFilters.fragment.xml` (período, filial, produto, situação, situação NF-e; o parceiro fica em cada view porque o value help muda):

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core">
	<Label text="Emissão de" required="true"/>
	<DatePicker
		value="{ path: 'params>/FromDate', type: 'sap.ui.model.odata.type.DateTimeOffset', constraints: { precision: 7 }, formatOptions: { pattern: 'dd/MM/yyyy' } }"
		liveChange=".validateField"
		required="true"/>
	<Label text="Emissão até" required="true"/>
	<DatePicker
		value="{ path: 'params>/ToDate', type: 'sap.ui.model.odata.type.DateTimeOffset', constraints: { precision: 7 }, formatOptions: { pattern: 'dd/MM/yyyy' } }"
		liveChange=".validateField"
		required="true"/>
	<Label text="Filial"/>
	<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openBranchsValueHelp" value="{params>/BranchCode}">
		<customData>
			<core:CustomData key="descriptionProperty" value="BranchName:ShortName"/>
		</customData>
	</Input>
	<Input value="{params>/BranchName}" editable="false"/>
	<Label text="Produto"/>
	<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openItemValueHelp" value="{params>/ItemCode}">
		<customData>
			<core:CustomData key="descriptionProperty" value="ItemName"/>
		</customData>
	</Input>
	<Input value="{params>/ItemName}" editable="false"/>
	<Label text="Situação"/>
	<MultiComboBox selectedKeys="{params>/Statuses}">
		<core:Item key="0" text="Pendente"/>
		<core:Item key="1" text="Confirmado"/>
		<core:Item key="2" text="Cancelado"/>
		<core:Item key="3" text="Retornado"/>
	</MultiComboBox>
	<Label text="Situação NF-e" visible="{ui>/standalone}"/>
	<MultiComboBox selectedKeys="{params>/NfeStatuses}" visible="{ui>/standalone}">
		<core:Item key="0" text="Não emitida"/>
		<core:Item key="1" text="Em processamento"/>
		<core:Item key="2" text="Autorizada"/>
		<core:Item key="3" text="Rejeitada"/>
		<core:Item key="4" text="Denegada"/>
		<core:Item key="5" text="Cancelada"/>
		<core:Item key="6" text="Inutilizada"/>
	</MultiComboBox>
</core:FragmentDefinition>
```

`selectedKeys` só casa com strings — por isso o `routeMatched` grava `Statuses: ["0", "1", "3"]` e o `buildPayload` converte para número.

- [ ] **Step 4: Tela "Notas de Saída por Período" (substitui a antiga)**

`webapp/controller/reports/salesInvoices/Main.controller.ts` (substitui o conteúdo):

```ts
import InvoiceReportController from "../InvoiceReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * @namespace siagrob1.controller.reports.salesInvoices
 */
export default class Main extends InvoiceReportController {
	protected readonly routeName = "salesInvoicesReport";
	protected readonly formId = "salesInvoicesReportForm";
	protected readonly serverRoute = ServerRoutes.salesInvoicesByPeriodReport;

	protected defaults() {
		return { InvoiceType: "" };
	}

	protected buildPayload() {
		const data = super.buildPayload();
		data.InvoiceType = data.InvoiceType == null ? null : Number(data.InvoiceType);
		return data;
	}
}
```

Apague `webapp/controller/reports/salesInvoices/BaseController.ts` se nada mais o importar (`grep -rn "reports/salesInvoices/BaseController" webapp`).

`webapp/view/reports/salesInvoices/Main.view.xml` (substitui o conteúdo):

```xml
<mvc:View
	controllerName="siagrob1.controller.reports.salesInvoices.Main"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form">
	<Page title="Notas de Saída por Período">
		<f:Form id="salesInvoicesReportForm" editable="true">
			<f:layout>
				<f:ColumnLayout columnsM="2" columnsL="2" columnsXL="3"/>
			</f:layout>
			<f:formContainers>
				<f:FormContainer title="Filtros">
					<f:formElements>
						<f:FormElement>
							<f:fields>
								<core:Fragment fragmentName="siagrob1.view.reports.fragments.InvoiceReportCommonFilters" type="XML"/>
							</f:fields>
						</f:FormElement>
					</f:formElements>
				</f:FormContainer>
			</f:formContainers>
		</f:Form>
		<footer>
			<OverflowToolbar>
				<ToolbarSpacer/>
				<Button text="Imprimir" type="Emphasized" press=".onPrintReport"/>
			</OverflowToolbar>
		</footer>
	</Page>
</mvc:View>
```

**Problema conhecido:** um `FormElement` com vários `fields` não reproduz o par Label/campo do `SimpleForm`. Antes de replicar, abra a tela no navegador (Step 9) com esta primeira versão. Se o layout ficar ruim, use o mesmo `f:SimpleForm` (`layout="ResponsiveGridLayout"`) das telas de relatório existentes, com o fragmento dentro de `<f:content>` — consistência com as telas vizinhas vale mais que a regra genérica; registre a escolha no commit. Siga com o formato que funcionar para as cinco telas.

Campos próprios desta tela, acrescentados após o fragmento (dentro do mesmo contêiner):

```xml
	<Label text="Cliente"/>
	<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openCostumersValueHelp" value="{params>/CardCode}">
		<customData>
			<core:CustomData key="descriptionProperty" value="CardName"/>
		</customData>
	</Input>
	<Input value="{params>/CardName}" editable="false"/>
	<Label text="Tipo"/>
	<Select selectedKey="{params>/InvoiceType}">
		<core:Item key="" text="Todos"/>
		<core:Item key="0" text="Normal"/>
		<core:Item key="1" text="Devolução"/>
	</Select>
```

`buildPayload` envia `CardName`/`BranchName`/`ItemName` também — o backend ignora propriedades desconhecidas (System.Text.Json padrão); não é preciso removê-las.

- [ ] **Step 5: Tela "Notas de Entrada por Período"**

`webapp/controller/reports/purchaseInvoices/Main.controller.ts`:

```ts
import InvoiceReportController from "../InvoiceReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * @namespace siagrob1.controller.reports.purchaseInvoices
 */
export default class Main extends InvoiceReportController {
	protected readonly routeName = "purchaseInvoicesReport";
	protected readonly formId = "purchaseInvoicesReportForm";
	protected readonly serverRoute = ServerRoutes.purchaseInvoicesByPeriodReport;

	protected defaults() {
		return { InvoiceType: "", IssuerType: "" };
	}

	protected buildPayload() {
		const data = super.buildPayload();
		data.InvoiceType = data.InvoiceType == null ? null : Number(data.InvoiceType);
		data.IssuerType = data.IssuerType == null ? null : Number(data.IssuerType);
		return data;
	}
}
```

`webapp/view/reports/purchaseInvoices/Main.view.xml`: mesma estrutura do Step 4 (no formato que funcionou lá), com `controllerName="siagrob1.controller.reports.purchaseInvoices.Main"`, título `Notas de Entrada por Período`, form id `purchaseInvoicesReportForm`, e campos próprios:

```xml
	<Label text="Emitente"/>
	<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openBusinessPartnersValueHelp" value="{params>/CardCode}">
		<customData>
			<core:CustomData key="descriptionProperty" value="CardName"/>
		</customData>
	</Input>
	<Input value="{params>/CardName}" editable="false"/>
	<Label text="Tipo"/>
	<Select selectedKey="{params>/InvoiceType}">
		<core:Item key="" text="Todos"/>
		<core:Item key="0" text="Normal"/>
		<core:Item key="1" text="Devolução"/>
	</Select>
	<Label text="Emissão"/>
	<Select selectedKey="{params>/IssuerType}">
		<core:Item key="" text="Todas"/>
		<core:Item key="0" text="De terceiro"/>
		<core:Item key="1" text="Própria"/>
	</Select>
```

(Emitente usa `openBusinessPartnersValueHelp`: a entrada pode ser de fornecedor ou de cliente devolvendo.)

- [ ] **Step 6: Telas de itens**

`webapp/controller/reports/salesInvoiceItems/Main.controller.ts`:

```ts
import InvoiceReportController from "../InvoiceReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * @namespace siagrob1.controller.reports.salesInvoiceItems
 */
export default class Main extends InvoiceReportController {
	protected readonly routeName = "salesInvoiceItemsReport";
	protected readonly formId = "salesInvoiceItemsReportForm";
	protected readonly serverRoute = ServerRoutes.salesInvoiceItemsReport;

	protected defaults() {
		return { InvoiceType: "", ContractCode: "", Cfop: "" };
	}

	protected buildPayload() {
		const data = super.buildPayload();
		data.InvoiceType = data.InvoiceType == null ? null : Number(data.InvoiceType);
		return data;
	}
}
```

View `webapp/view/reports/salesInvoiceItems/Main.view.xml`: estrutura do Step 4, título `Itens dos Documentos de Saída`, form id `salesInvoiceItemsReportForm`, campos próprios = os do Step 4 (Cliente + Tipo) mais:

```xml
	<Label text="Contrato"/>
	<Input value="{params>/ContractCode}" maxLength="30"/>
	<Label text="CFOP"/>
	<Input value="{params>/Cfop}" maxLength="4"/>
```

`webapp/controller/reports/purchaseInvoiceItems/Main.controller.ts`:

```ts
import InvoiceReportController from "../InvoiceReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * @namespace siagrob1.controller.reports.purchaseInvoiceItems
 */
export default class Main extends InvoiceReportController {
	protected readonly routeName = "purchaseInvoiceItemsReport";
	protected readonly formId = "purchaseInvoiceItemsReportForm";
	protected readonly serverRoute = ServerRoutes.purchaseInvoiceItemsReport;

	protected defaults() {
		return { InvoiceType: "", IssuerType: "", ContractCode: "", Cfop: "" };
	}

	protected buildPayload() {
		const data = super.buildPayload();
		data.InvoiceType = data.InvoiceType == null ? null : Number(data.InvoiceType);
		data.IssuerType = data.IssuerType == null ? null : Number(data.IssuerType);
		return data;
	}
}
```

View `webapp/view/reports/purchaseInvoiceItems/Main.view.xml`: título `Itens dos Documentos de Entrada`, form id `purchaseInvoiceItemsReportForm`, campos próprios = os do Step 5 (Emitente + Tipo + Emissão) mais Contrato e CFOP (bloco acima).

- [ ] **Step 7: Tela "Devoluções de Venda"**

`webapp/controller/reports/salesReturns/Main.controller.ts`:

```ts
import InvoiceReportController from "../InvoiceReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * @namespace siagrob1.controller.reports.salesReturns
 */
export default class Main extends InvoiceReportController {
	protected readonly routeName = "salesReturnsReport";
	protected readonly formId = "salesReturnsReportForm";
	protected readonly serverRoute = ServerRoutes.salesReturnsReport;

	protected defaults() {
		return { Source: "" };
	}

	protected buildPayload() {
		const data = super.buildPayload();
		// SalesReturnSource: 0 Própria, 1 Cliente (ver Task 6 sobre enum como número).
		data.Source = data.Source == null ? null : Number(data.Source);
		return data;
	}
}
```

View `webapp/view/reports/salesReturns/Main.view.xml`: título `Devoluções de Venda`, form id `salesReturnsReportForm`, campos próprios: Cliente (bloco do Step 4) e

```xml
	<Label text="Emissão"/>
	<Select selectedKey="{params>/Source}">
		<core:Item key="" text="Própria e do cliente"/>
		<core:Item key="0" text="Própria"/>
		<core:Item key="1" text="Do cliente"/>
	</Select>
```

Nesta tela o fragmento comum mostra "Emissão de/até" — o rótulo serve (é a data da devolução).

- [ ] **Step 8: manifest.json**

Em `routing.routes`, junto de `salesContractsByItemReport`:

```json
        {
          "pattern": "purchase-invoices/report",
          "name": "purchaseInvoicesReport",
          "target": "purchaseInvoicesReport"
        },
        {
          "pattern": "sales-invoice-items/report",
          "name": "salesInvoiceItemsReport",
          "target": "salesInvoiceItemsReport"
        },
        {
          "pattern": "purchase-invoice-items/report",
          "name": "purchaseInvoiceItemsReport",
          "target": "purchaseInvoiceItemsReport"
        },
        {
          "pattern": "sales-returns/report",
          "name": "salesReturnsReport",
          "target": "salesReturnsReport"
        },
```

Em `routing.targets`, junto de `salesContractsByItemReport`:

```json
        "purchaseInvoicesReport": {
          "id": "purchaseInvoicesReport",
          "level": 1,
          "name": "siagrob1.view.reports.purchaseInvoices.Main",
          "clearControlAggregation": true
        },
        "salesInvoiceItemsReport": {
          "id": "salesInvoiceItemsReport",
          "level": 1,
          "name": "siagrob1.view.reports.salesInvoiceItems.Main",
          "clearControlAggregation": true
        },
        "purchaseInvoiceItemsReport": {
          "id": "purchaseInvoiceItemsReport",
          "level": 1,
          "name": "siagrob1.view.reports.purchaseInvoiceItems.Main",
          "clearControlAggregation": true
        },
        "salesReturnsReport": {
          "id": "salesReturnsReport",
          "level": 1,
          "name": "siagrob1.view.reports.salesReturns.Main",
          "clearControlAggregation": true
        },
```

A rota/target `salesInvoicesReport` já existe e continua apontando para `siagrob1.view.reports.salesInvoices.Main`.

- [ ] **Step 9: Gates do frontend**

Run (em `siagro-b1-frontend/`): `yarn ts-typecheck` — Expected: 0 erros.
Run: `yarn lint` — Expected: sem erros novos nos arquivos tocados.
(`yarn test` não passa por causa do gate de cobertura irreal — não use como critério.)

- [ ] **Step 10: Commit (frontend)**

```bash
git -C ../siagro-b1-frontend branch --show-current   # feature/fiscal-documents-reports
git add webapp/controller/reports/InvoiceReportController.ts webapp/view/reports/fragments/InvoiceReportCommonFilters.fragment.xml webapp/controller/reports/salesInvoices/Main.controller.ts webapp/view/reports/salesInvoices/Main.view.xml webapp/controller/reports/purchaseInvoices webapp/view/reports/purchaseInvoices webapp/controller/reports/salesInvoiceItems webapp/view/reports/salesInvoiceItems webapp/controller/reports/purchaseInvoiceItems webapp/view/reports/purchaseInvoiceItems webapp/controller/reports/salesReturns webapp/view/reports/salesReturns webapp/manifest.json webapp/model/ServerRoutes.ts
git commit -m "feat(reports): telas dos relatórios de documentos fiscais" -m "Notas de saída/entrada por período, itens de saída/entrada e devoluções de venda. O filtro de situação da NF-e some em SAPB1." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

(Siga a convenção de commit do `siagro-b1-frontend/CLAUDE.md` se ela diferir.)

---

### Task 9: Verificação pelo caminho do usuário

Sem código novo; corrige o que aparecer (cada correção com teste quando couber, e commit próprio).

- [ ] **Step 1: STANDALONE (CEAGUI dev)**

Suba Web (`--launch-profile ceagui`), Gateway (`ceagui`), Reports (`ceagui`) e `yarn start:dev` no frontend (ver memória "Subir a stack local"). Login admin/1234. Pela **home** → menu Relatórios: os 5 itens aparecem. Em cada tela: período julho–outubro/2026, Imprimir → PDF abre em nova aba com dados, filtros no cabeçalho, colunas fiscais presentes. Teste pelo menos: filtro de cliente, de produto, situação Cancelado sozinha, Situação NF-e = Autorizada, Tipo = Devolução.

- [ ] **Step 2: Período inválido**

Na tela de notas de saída, "até" antes de "de" → mensagem "A data final não pode ser anterior à inicial." (vinda do 400).

- [ ] **Step 3: Devolução emitida pelo cliente**

Não há nenhuma nas bases dev. **Peça autorização ao usuário** para lançar uma entrada de terceiro tipo Devolução na CEAGUI dev, ligada a um item de saída; com o ok, lance pela tela e confira a linha "Cliente" no relatório de devoluções com a nota original preenchida.

- [ ] **Step 4: SAPB1 (Yokotobi dev)**

`IDX_SIAGRO_DEV` está parada na migration `20261002032155` e o Web recusa subir com migration pendente. **Peça autorização ao usuário** para aplicar as migrations pendentes nessa base (`ASPNETCORE_ENVIRONMENT=Yokotobi-Development` explícito, contexts `AppDbContext` e `CommonDbContext`). Com o ok, suba com o profile `yktb` e repita o Step 1: os PDFs saem **sem** Tributos/ICMS/PIS/COFINS/IBS-CBS/Sit. NF-e, e a tela **sem** o filtro de Situação NF-e. Confira que parceiro e produto aparecem preenchidos (vêm do snapshot).

- [ ] **Step 5: Suíte completa e fechamento**

Run: `dotnet test SiagroB1.Application.Tests` — Expected: PASS.
Use `superpowers:verification-before-completion` antes de declarar pronto. Atualize a memória do projeto (novo arquivo sobre o pacote 1 + ponteiro no `MEMORY.md`). Merge em `main` continua sendo decisão do usuário.
