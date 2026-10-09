# Relatórios de Logística de Venda (pacote 2) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Quatro relatórios PDF de logística de venda (Cargas por Período, Liberações de Venda, Liberações de Compra, Romaneios de Venda) que funcionam em SAPB1 e STANDALONE, agrupados por produto + UM, com tela de filtros e item de menu.

**Architecture:** Cada relatório segue o padrão do pacote 1 no serviço `SiagroB1.Reports`: controller `POST /reports/<Nome>` → `<Nome>ReportService.BuildRowsAsync` (EF só com filtros traduzíveis sobre tabelas locais e snapshots; cliente da carga, saldos, rótulos e agrupamento em C#) → `IFastReportService.GeneratePdfAsync` com um `.frx` paisagem. Os textos genéricos do pacote 1 saem de `InvoiceReportText` para um `ReportText` comum (sem mudar nenhuma string do pacote 1); as regras de logística ficam em `LogisticsReportText`. Total geral de quantidade só aparece quando o resultado tem uma única UM (`pSingleUom` + prefixos `uomSum`/`uomMixed`, escondidos pelo `FastReportService`). Frontend: uma tela UI5 por relatório sobre o controller-base `InvoiceReportController` já existente.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), FastReport.OpenSource 2026.1.3, xUnit; OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-08-logistics-reports-design.md`

**Pré-validação (08/10, ao escrever o plano):** todo o código C# e os quatro `.frx` deste plano foram aplicados num worktree descartável sobre `125aa5a`: build limpo e `dotnet test --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"` com 438 testes verdes (os do pacote 1 inclusive); os PDFs gerados foram conferidos a olho. O TypeScript da Task 7 passou em `tsc --noEmit` e `eslint`. Se algo não compilar na sua execução, o código-base mudou depois — investigue antes de "consertar" o plano.

## Global Constraints

- Branch `feature/logistics-reports` nos **dois** repos (`siagro-b1-backend`, `siagro-b1-frontend`) — já criados a partir de `main`. Confira `git branch --show-current` antes de **cada** commit. Nunca push, nunca merge.
- Todo arquivo novo: `git add <path>` logo após criar.
- Commits no formato do `CLAUDE.md` do repo: `tipo(escopo): descrição pt-BR`, escopo `reports`; migration exige trailer `DB: <NomeDaMigration>`. Rodapé obrigatório (as duas linhas):
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
  ```
- Identificadores em inglês; texto que o usuário lê (tela, PDF, mensagens) em pt-BR.
- Relatório **nunca** faz JOIN com `ITEMS`, `BUSINESS_PARTNERS`, `WAREHOUSES` (vazias em SAPB1). Só tabelas locais: `ShipmentLoads`, `SalesInvoices`, `SalesShipmentReleases`, `ShipmentReleases`, `SalesContracts`, `PurchaseContracts`, `LogisticRegions`, `StorageTransactions`, `Branchs`. Nomes de parceiro/produto/armazém/transportadora vêm dos snapshots gravados nos documentos.
- Queries EF só com filtros traduzíveis (igualdade, faixa de data, `Contains` sobre **array** de enum). Nada de `Contains` sobre lista de chaves — as notas da carga vêm por `Include`. Agregação, saldos e rótulos em C#.
- Período: fim exclusivo `ToDate.Date.AddDays(1)`. Cargas filtram `LoadDate`, liberações `ReleaseDate`, romaneios `TransactionDate`.
- Situação vazia = todas menos a cancelada. Rótulos pt-BR **iguais** aos de `siagro-b1-frontend/webapp/model/formatter.ts` (ver `LogisticsReportText`, Task 1).
- Saldos pelo domínio: `ShipmentLoad.AvailableQuantity`, `SalesShipmentRelease.AvailableQuantity`, `ShipmentRelease.AvailableQuantity` (0 quando cancelada; negativo não é clampado).
- Ordem: grupo com `StringComparer.CurrentCultureIgnoreCase` (como o pacote 1), depois data, depois código com `StringComparer.Ordinal`. **Testes de ordem só com nomes cuja ordem é a mesma em ordinal e em qualquer cultura** (iniciais maiúsculas ASCII diferentes: `MILHO` < `SOJA`; UMs `KG` < `TN`). Nada de acento ou caixa mista decidindo ordem em teste.
- `.frx`: copie o XML **completo** dado em cada task (gerado e conferido contra as regras abaixo). Todo template tem `picLogo` e `pCompanyName`; `GroupHeaderBand SortOrder="None"`; larguras somam 1084 px; Consolas 7pt ≈ 5,2 px/char **+ 4 px de margem de reticência + 4 px de padding** (ver Appendix A — a regra "5,2 + 4" do pacote 1 corta `99.999.999,999` em 78 px); subtotais/totais em Consolas **6pt bold numa linha só**; totais gerais de quantidade com nome `uomSum*`; nota de UM mista `uomMixedNote`.
- Não altere nenhuma string, teste ou template do pacote 1. `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"` tem de continuar verde ao fim de cada task.
- **Nenhum `dotnet ef database update`** neste plano. A migration é validada só com `dotnet ef migrations script`.
- Rodar testes a partir de `siagro-b1-backend/`: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`.

## Review Focus

Os cinco jeitos mais prováveis de o relatório sair errado com dado real — cada um com teste na task dona:

1. **Carga com várias notas / vários clientes, e uma nota cancelada no meio** — Cliente(s) = nomes distintos das notas vivas, ordem ordinal, sem o cliente da nota cancelada; nota com `InvoiceStatus` nulo conta como viva. Teste `BuildRows_CustomersComeFromTheLiveInvoicesOfTheLoad` (Task 2) e `LoadCustomers_JoinsTheLiveInvoicesDistinctAndSorted` (Task 1).
2. **Carga sem nota (planejada, ou só com nota cancelada)** — mostra o cliente do planejamento com " (planejado)"; sem planejamento, vazio; o filtro de Cliente casa com o planejado só quando não há nota viva. Testes `BuildRows_LoadWithoutInvoiceShowsThePlannedCustomer` e `BuildRows_CustomerFilterUsesTheInvoicesThenThePlan` (Task 2).
3. **Liberação com quantidades de borda** (os campos não são anuláveis; o risco real é o saldo): cancelada com retirada parcial ⇒ saldo 0; sem nada retirado ⇒ saldo = liberado; retirada acima do liberado ⇒ saldo negativo, sem clamp. Testes `BuildRows_BalanceFollowsTheDomainRule` (Task 3 e Task 4).
4. **Romaneio sem carga** — Carga e Cliente vazios, linha não some; filtro "Vínculo com carga" separa; filtro de Cliente nunca o inclui. Testes `BuildRows_ShipmentWithoutLoadHasNoLoadNorCustomer`, `BuildRows_LoadLinkFilter`, `BuildRows_CustomerFilterNeverMatchesAShipmentWithoutLoad` (Task 5).
5. **UM mista** — mesmo produto em KG e TN não pode somar junto: grupo = produto + UM, e o total geral de quantidade some (fica a nota) quando há mais de uma UM. Testes `ApplySingleUom_HidesTheQuantityTotalsOrTheNote` (Task 1), `BuildRows_GroupsByProductAndUnit` e `Execute_FlagsWhetherTheResultHasASingleUnit` (Tasks 2–5).

---

## Appendix A — Regras dos templates `.frx`

Os quatro templates são dados **completos** nas tasks; não os redesenhe. Eles seguem o esqueleto de `SalesInvoiceItems.frx` (pacote 1) com estas regras, que `LogisticsReportsLayoutTests` confere:

1. Paisagem A4 (`Landscape="true" PaperWidth="297" PaperHeight="210"`), bandas com `Width="1084"`; `PageHeader1` com `picLogo`, `txtCompany` (`[pCompanyName]`), `txtDate`, `txtTitle`, `txtFilters` (`[pFilters]`).
2. `ColumnHeader1` com um `hdr<Campo>` por coluna; `Data1` com o `txt<Campo>` correspondente, **mesmo `Left` e `Width`**.
3. `GroupHeader1` com `SortOrder="None"` e `Condition="[<Fonte>.Group]"`; `Data1` dentro dele; `GroupFooter1` com `txtGrpLabel` e `txtGrp<Campo>` para cada coluna numérica.
4. `ReportSummary1`: `txtSumLabel`, `txtSumCount` (`[TotalCountAll] <coisa>(s)`), `uomMixedNote`, `uomSum<Campo>` para cada quantidade e `txtSum<Campo>` para dinheiro, e `txtEmpty` ("Nenhum registro encontrado." quando `TotalCountAll == 0`).
5. **Regra de largura** (medida no PDF real, não só na conta): `(caracteres × 5,2 + 4) × corpo/7 + 4` px. O 5,2 é a largura do caractere Consolas a 7pt; o primeiro 4 é a margem que o GDI+ reserva ao aparar com reticências (`Trimming="EllipsisCharacter"`), proporcional ao corpo; o segundo é o padding do `TextObject`. Conferido em 08/10 no PDF gerado: `99.999.999,999` (14 car.) sai "99.999.999,9…" em 78 **e em 80** px; `9.999.999,999` (13) cabe em 78; data de 10 car. corta em 56 e cabe em 60; `9.999.999,99` e `CV2026000123` (12) cortam em 70; "Transferência" (13) cabe em 76.
6. Números: `Format="Number" Format.UseLocale="true"`, `DecimalDigits` 3 (quantidade/peso) ou 2 (dinheiro), `HorzAlign="Right"`. Larguras: quantidade **82 px** (`99.999.999,999`, 14 car. ⇒ 80,8); dinheiro **72 px** (`9.999.999,99`, 12 car. ⇒ 70,4); data **62 px** (60,0). Subtotais/totais em **6pt** (`999.999.999,999`, 15 car. ⇒ 74,3 ≤ 82; `999.999.999,99` ⇒ 69,8 ≤ 72) e todos com o mesmo `Top` na banda.
7. Texto de célula: `WordWrap="false"` + `Trimming="EllipsisCharacter"`. Cabeçalho: mesma regra sobre o texto do cabeçalho (por isso "Transp." nas cargas e "Entrega até" nas liberações de venda).
8. Parâmetros `pCompanyName`, `pFilters`, `pSingleUom` (`System.Boolean`). O `FastReportService` esconde `uomSum*` quando `pSingleUom = false` e `uomMixed*` quando `true`.

Para conferir: o teste de PDF de cada task grava `%TEMP%/siagro-logistics-reports/<Nome>-<modo>.pdf`; abra com a ferramenta Read e procure coluna cortada, número com "…" ou cabeçalho truncado.

---

### Task 1: Base comum — `ReportText`, `LogisticsReportText`, `pSingleUom` e seed de teste

**Files:**
- Create: `SiagroB1.Reports/Helpers/ReportText.cs`
- Modify: `SiagroB1.Reports/Helpers/InvoiceReportText.cs` (passa a delegar; mesma API, mesmas strings)
- Create: `SiagroB1.Reports/Dtos/LogisticsReportRequest.cs`
- Create: `SiagroB1.Reports/Helpers/LogisticsReportText.cs`
- Modify: `SiagroB1.Reports/Services/FastReportService.cs`
- Create: `SiagroB1.Application.Tests/Support/LogisticsReportSeed.cs`
- Test: `SiagroB1.Application.Tests/Reports/ReportTextTests.cs`
- Test: `SiagroB1.Application.Tests/Reports/LogisticsReportTextTests.cs`
- Test: `SiagroB1.Application.Tests/Reports/FastReportServiceUomTests.cs`

**Interfaces:**
- Produces:
  - `static class ReportText` com `const string NoProduct = "Sem produto vinculado"`, `const string InvertedPeriod = "A data final não pode ser anterior à inicial."`, `string? ValidatePeriod(DateTime from, DateTime to, string missingMessage)`, `string Period(string label, DateTime from, DateTime to)`, `string Date(DateTime?)`, `string DocumentNumber(string?, string?)`, `string Partner(string? code, string? name)`, `string Product(string? code, string? name)`, `string BranchName(Branch?, string?)`, `string NameOrCode(string? code, string? name)`, `string JoinDistinct(IEnumerable<string?>)`, `string Describe(string? description, string fallback)`, `string JoinFilters(IEnumerable<string>)`, `bool IsSingleUnit(IEnumerable<string?>)`, `string? ProductOfGroup(string?)`.
  - `abstract class LogisticsReportRequest { DateTime FromDate; DateTime ToDate; string? BranchCode; string? ItemCode; }`
  - `static class LogisticsReportText` com `const MissingPeriod = "Informe o período."`, `const PlannedSuffix = " (planejado)"`, `string? Validate(LogisticsReportRequest)`, `ShipmentLoadStatus[] EffectiveLoadStatuses(IReadOnlyCollection<ShipmentLoadStatus>?)`, `ReleaseStatus[] EffectiveReleaseStatuses(...)`, `StorageTransactionsStatus[] EffectiveTransactionStatuses(...)`, `string LoadStatusText(ShipmentLoadStatus)`, `string LoadTypeText(ShipmentLoadType)`, `string ReleaseStatusText(ReleaseStatus)`, `string OriginText(ReleaseOrigin)`, `string TransactionStatusText(StorageTransactionsStatus)`, `string StatusFilter<T>(IReadOnlyCollection<T> effective, Func<T,string> label, T cancelled)`, `string LoadCustomers(ShipmentLoad?)`, `bool LoadHasCustomer(ShipmentLoad?, string cardCode)`, `string? CustomerName(IEnumerable<ShipmentLoad?>, string? cardCode)`, `string BuildFilters(string periodLabel, LogisticsReportRequest, string? branchName, string? productName, string statusFilter, IEnumerable<string> extra)`, `Task<string?> BranchNameAsync(AppDbContext, string?)`.
  - `FastReportService.SingleUomParameter = "pSingleUom"`, `UomTotalsPrefix = "uomSum"`, `MixedUomNotePrefix = "uomMixed"`, `static void ApplySingleUom(Report, bool singleUom)`.
  - Test support `LogisticsReportSeed`: `Jul01`, `Jul15`, `Jul31`; `Load(...)`, `LoadInvoice(...)`, `SalesContract(...)`, `Region(...)`, `SalesRelease(...)`, `PurchaseContract(...)`, `PurchaseRelease(...)`, `SalesShipment(...)` (assinaturas no Step 3).

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Reports/ReportTextTests.cs`:

```csharp
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Application.Tests.Reports;

public class ReportTextTests
{
    private static readonly DateTime Jul01 = new(2026, 7, 1);
    private static readonly DateTime Jul31 = new(2026, 7, 31);

    [Fact]
    public void ValidatePeriod_RequiresBothDates()
    {
        Assert.Equal("Informe o período.", ReportText.ValidatePeriod(default, Jul31, "Informe o período."));
        Assert.Equal("Informe o período.", ReportText.ValidatePeriod(Jul01, default, "Informe o período."));
    }

    [Fact]
    public void ValidatePeriod_RejectsAnInvertedPeriod() =>
        Assert.Equal("A data final não pode ser anterior à inicial.", ReportText.ValidatePeriod(Jul31, Jul01, "x"));

    [Fact]
    public void ValidatePeriod_AcceptsASingleDay() =>
        Assert.Null(ReportText.ValidatePeriod(Jul01, Jul01, "x"));

    [Fact]
    public void Period_FormatsBothEnds() =>
        Assert.Equal("Data da carga: 01/07/2026 a 31/07/2026", ReportText.Period("Data da carga", Jul01, Jul31));

    [Theory]
    [InlineData("ARM01", "ARMAZÉM CENTRAL", "ARMAZÉM CENTRAL")]
    [InlineData("ARM01", null, "ARM01")]
    [InlineData("ARM01", " ", "ARM01")]
    [InlineData(null, null, "")]
    public void NameOrCode_PrefersTheName(string? code, string? name, string expected) =>
        Assert.Equal(expected, ReportText.NameOrCode(code, name));

    [Fact]
    public void JoinDistinct_SkipsBlanksAndDuplicatesInOrdinalOrder() =>
        Assert.Equal(
            "AGRO NORTE, COOPERATIVA CENTRAL",
            ReportText.JoinDistinct(["COOPERATIVA CENTRAL", "AGRO NORTE", "COOPERATIVA CENTRAL ", " ", null]));

    [Fact]
    public void IsSingleUnit_IgnoresCaseAndTreatsEmptyAsSingle()
    {
        Assert.True(ReportText.IsSingleUnit([]));
        Assert.True(ReportText.IsSingleUnit(["KG", "kg", "KG "]));
        Assert.False(ReportText.IsSingleUnit(["KG", "TN"]));
    }

    [Theory]
    [InlineData("SOJA EM GRÃOS (10001) - KG", "SOJA EM GRÃOS (10001)")]
    [InlineData("SOJA EM GRÃOS (10001)", "SOJA EM GRÃOS (10001)")]
    [InlineData(null, null)]
    public void ProductOfGroup_DropsTheUnitSuffix(string? group, string? expected) =>
        Assert.Equal(expected, ReportText.ProductOfGroup(group));

    [Fact]
    public void InvoiceReportText_DelegatesWithTheSameStrings()
    {
        Assert.Equal("(C001) COOPERATIVA", InvoiceReportText.Partner("C001", "COOPERATIVA"));
        Assert.Equal("SOJA (10001)", InvoiceReportText.Product("10001", "SOJA"));
        Assert.Equal(ReportText.NoProduct, InvoiceReportText.NoProduct);
        Assert.Equal("123/1", InvoiceReportText.DocumentNumber("123", "1"));
        Assert.Equal("01/07/2026", InvoiceReportText.Date(Jul01));
    }
}
```

`SiagroB1.Application.Tests/Reports/LogisticsReportTextTests.cs`:

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using static SiagroB1.Application.Tests.Support.LogisticsReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class LogisticsReportTextTests
{
    private sealed class Request : LogisticsReportRequest;

    [Theory]
    [InlineData(ShipmentLoadStatus.Planned, "Planejada")]
    [InlineData(ShipmentLoadStatus.Open, "Carregada")]
    [InlineData(ShipmentLoadStatus.PartiallyInvoiced, "Faturada Parcial")]
    [InlineData(ShipmentLoadStatus.Invoiced, "Faturada")]
    [InlineData(ShipmentLoadStatus.Cancelled, "Cancelada")]
    [InlineData(ShipmentLoadStatus.Returned, "Devolvida")]
    [InlineData(ShipmentLoadStatus.Completed, "Concluída")]
    [InlineData(ShipmentLoadStatus.InTransshipment, "Em Transbordo")]
    [InlineData(ShipmentLoadStatus.Discharged, "Descarregada")]
    public void LoadStatusText_MatchesTheScreen(ShipmentLoadStatus status, string expected) =>
        Assert.Equal(expected, LogisticsReportText.LoadStatusText(status));

    [Theory]
    [InlineData(ShipmentLoadType.Normal, "Normal")]
    [InlineData(ShipmentLoadType.Removal, "Remoção")]
    public void LoadTypeText_MatchesTheScreen(ShipmentLoadType type, string expected) =>
        Assert.Equal(expected, LogisticsReportText.LoadTypeText(type));

    [Theory]
    [InlineData(ReleaseStatus.Pending, "Pendente")]
    [InlineData(ReleaseStatus.Actived, "Ativo")]
    [InlineData(ReleaseStatus.Completed, "Finalizado")]
    [InlineData(ReleaseStatus.Cancelled, "Cancelado")]
    [InlineData(ReleaseStatus.Paused, "Pausado")]
    public void ReleaseStatusText_MatchesTheScreen(ReleaseStatus status, string expected) =>
        Assert.Equal(expected, LogisticsReportText.ReleaseStatusText(status));

    [Theory]
    [InlineData(ReleaseOrigin.Standard, "Compra")]
    [InlineData(ReleaseOrigin.OwnershipTransfer, "Transferência")]
    [InlineData(ReleaseOrigin.SalesReturn, "Devolução")]
    [InlineData(ReleaseOrigin.Transshipment, "Transbordo")]
    public void OriginText_MatchesTheScreen(ReleaseOrigin origin, string expected) =>
        Assert.Equal(expected, LogisticsReportText.OriginText(origin));

    [Theory]
    [InlineData(StorageTransactionsStatus.Pending, "Pendente")]
    [InlineData(StorageTransactionsStatus.Confirmed, "Confirmado")]
    [InlineData(StorageTransactionsStatus.Cancelled, "Cancelado")]
    [InlineData(StorageTransactionsStatus.Invoiced, "Faturado")]
    [InlineData(StorageTransactionsStatus.Returned, "Devolvido")]
    public void TransactionStatusText_MatchesTheScreen(StorageTransactionsStatus status, string expected) =>
        Assert.Equal(expected, LogisticsReportText.TransactionStatusText(status));

    [Fact]
    public void EffectiveStatuses_EmptyMeansEverythingButCancelled()
    {
        var loads = LogisticsReportText.EffectiveLoadStatuses(null);
        Assert.Equal(8, loads.Length);
        Assert.DoesNotContain(ShipmentLoadStatus.Cancelled, loads);

        Assert.Equal(
            new[] { ReleaseStatus.Pending, ReleaseStatus.Actived, ReleaseStatus.Completed, ReleaseStatus.Paused },
            LogisticsReportText.EffectiveReleaseStatuses([]));

        Assert.Equal(
            new[]
            {
                StorageTransactionsStatus.Pending, StorageTransactionsStatus.Confirmed,
                StorageTransactionsStatus.Invoiced, StorageTransactionsStatus.Returned,
            },
            LogisticsReportText.EffectiveTransactionStatuses(null));
    }

    [Fact]
    public void EffectiveStatuses_KeepsAnExplicitChoiceWithoutDuplicates() =>
        Assert.Equal(
            new[] { ShipmentLoadStatus.Cancelled },
            LogisticsReportText.EffectiveLoadStatuses([ShipmentLoadStatus.Cancelled, ShipmentLoadStatus.Cancelled]));

    [Fact]
    public void StatusFilter_DescribesTheDefaultAsAllButCancelled() =>
        Assert.Equal(
            "Situação: todas, exceto Cancelada",
            LogisticsReportText.StatusFilter(
                LogisticsReportText.EffectiveLoadStatuses(null), LogisticsReportText.LoadStatusText, ShipmentLoadStatus.Cancelled));

    [Fact]
    public void StatusFilter_DescribesEverythingAsAll() =>
        Assert.Equal(
            "Situação: todas",
            LogisticsReportText.StatusFilter(
                Enum.GetValues<ReleaseStatus>(), LogisticsReportText.ReleaseStatusText, ReleaseStatus.Cancelled));

    [Fact]
    public void StatusFilter_ListsAnExplicitChoiceInEnumOrder() =>
        Assert.Equal(
            "Situação: Carregada, Faturada",
            LogisticsReportText.StatusFilter(
                new[] { ShipmentLoadStatus.Invoiced, ShipmentLoadStatus.Open },
                LogisticsReportText.LoadStatusText, ShipmentLoadStatus.Cancelled));

    [Fact]
    public void LoadCustomers_JoinsTheLiveInvoicesDistinctAndSorted()
    {
        var load = Load("CG000001", plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA");
        load.Invoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        load.Invoices.Add(LoadInvoice(load, "000000102", "C002", "AGRO NORTE"));
        load.Invoices.Add(LoadInvoice(load, "000000103", "C001", "COOPERATIVA CENTRAL", status: null));
        load.Invoices.Add(LoadInvoice(load, "000000104", "C003", "CLIENTE CANCELADO", status: InvoiceStatus.Cancelled));

        Assert.Equal("AGRO NORTE, COOPERATIVA CENTRAL", LogisticsReportText.LoadCustomers(load));
    }

    [Fact]
    public void LoadCustomers_WithoutALiveInvoiceFallsBackToThePlan()
    {
        var planned = Load("CG000001", plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA");
        planned.Invoices.Add(LoadInvoice(planned, "000000104", "C003", "CLIENTE CANCELADO", status: InvoiceStatus.Cancelled));

        Assert.Equal("FAZENDA BOA VISTA (planejado)", LogisticsReportText.LoadCustomers(planned));
        Assert.Equal("", LogisticsReportText.LoadCustomers(Load("CG000002")));
        Assert.Equal("", LogisticsReportText.LoadCustomers(null));
    }

    [Fact]
    public void LoadHasCustomer_UsesTheInvoicesAndOnlyThenThePlan()
    {
        var invoiced = Load("CG000001", plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA");
        invoiced.Invoices.Add(LoadInvoice(invoiced, "000000101", "C001", "COOPERATIVA CENTRAL"));
        var planned = Load("CG000002", plannedCardCode: "C001", plannedCardName: "COOPERATIVA CENTRAL");

        Assert.True(LogisticsReportText.LoadHasCustomer(invoiced, "C001"));
        Assert.False(LogisticsReportText.LoadHasCustomer(invoiced, "C009"));
        Assert.True(LogisticsReportText.LoadHasCustomer(planned, "C001"));
        Assert.False(LogisticsReportText.LoadHasCustomer(null, "C001"));
    }

    [Fact]
    public void CustomerName_ComesFromTheInvoiceOrThePlan()
    {
        var invoiced = Load("CG000001");
        invoiced.Invoices.Add(LoadInvoice(invoiced, "000000101", "C001", "COOPERATIVA CENTRAL"));
        var planned = Load("CG000002", plannedCardCode: "C002", plannedCardName: "AGRO NORTE");

        Assert.Equal("COOPERATIVA CENTRAL", LogisticsReportText.CustomerName([invoiced, planned], "C001"));
        Assert.Equal("AGRO NORTE", LogisticsReportText.CustomerName([invoiced, planned], "C002"));
        Assert.Null(LogisticsReportText.CustomerName([invoiced, planned], "C777"));
        Assert.Null(LogisticsReportText.CustomerName([invoiced, null], null));
    }

    [Fact]
    public void Validate_RequiresAValidPeriod()
    {
        Assert.Equal("Informe o período.", LogisticsReportText.Validate(new Request { ToDate = Jul31 }));
        Assert.Equal("A data final não pode ser anterior à inicial.",
            LogisticsReportText.Validate(new Request { FromDate = Jul31, ToDate = Jul01 }));
        Assert.Null(LogisticsReportText.Validate(new Request { FromDate = Jul01, ToDate = Jul31 }));
    }

    [Fact]
    public void BuildFilters_ListsOnlyWhatWasInformed()
    {
        var request = new Request { FromDate = Jul01, ToDate = Jul31, BranchCode = "01", ItemCode = "10001" };

        Assert.Equal(
            "Data da carga: 01/07/2026 a 31/07/2026 | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | Situação: todas, exceto Cancelada | Tipo: Normal",
            LogisticsReportText.BuildFilters("Data da carga", request, "MATRIZ", "SOJA EM GRÃOS (10001)",
                "Situação: todas, exceto Cancelada", ["Tipo: Normal"]));

        Assert.Equal(
            "Data da carga: 01/07/2026 a 31/07/2026 | Filial: 01 | Produto: 10001 | Situação: todas",
            LogisticsReportText.BuildFilters("Data da carga", request, null, null, "Situação: todas", []));

        Assert.Equal(
            "Data da carga: 01/07/2026 a 31/07/2026 | Situação: todas",
            LogisticsReportText.BuildFilters("Data da carga", new Request { FromDate = Jul01, ToDate = Jul31 },
                "MATRIZ", "SOJA", "Situação: todas", []));
    }

    [Fact]
    public async Task BranchNameAsync_ReadsTheLocalBranchTable()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        await db.Context.SaveChangesAsync();

        Assert.Equal("MATRIZ", await LogisticsReportText.BranchNameAsync(db.Context, "01"));
        Assert.Null(await LogisticsReportText.BranchNameAsync(db.Context, "99"));
        Assert.Null(await LogisticsReportText.BranchNameAsync(db.Context, null));
    }
}
```

`SiagroB1.Application.Tests/Reports/FastReportServiceUomTests.cs`:

```csharp
using FastReport;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class FastReportServiceUomTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplySingleUom_HidesTheQuantityTotalsOrTheNote(bool singleUom)
    {
        using var report = new Report();
        var page = new ReportPage { Name = "Page1" };
        report.Pages.Add(page);
        var band = new DataBand { Name = "Data1" };
        page.Bands.Add(band);
        var quantityTotal = new TextObject { Name = "uomSumTotalQuantity" };
        var note = new TextObject { Name = "uomMixedNote" };
        var freightTotal = new TextObject { Name = "txtSumFreightValue" };
        band.Objects.Add(quantityTotal);
        band.Objects.Add(note);
        band.Objects.Add(freightTotal);

        FastReportService.ApplySingleUom(report, singleUom);

        Assert.Equal(singleUom, quantityTotal.Visible);
        Assert.Equal(!singleUom, note.Visible);
        Assert.True(freightTotal.Visible);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ReportTextTests|FullyQualifiedName~LogisticsReportTextTests|FullyQualifiedName~FastReportServiceUomTests"`
Expected: build FAIL — `ReportText`, `LogisticsReportRequest`, `LogisticsReportText`, `LogisticsReportSeed`, `ApplySingleUom` not found.

- [ ] **Step 3: Implement**

`SiagroB1.Reports/Helpers/ReportText.cs` (criar e `git add`):

```csharp
using System.Globalization;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Textos pt-BR comuns a todos os relatórios em lista (documentos fiscais e logística). Tudo sai
/// de snapshots gravados nos documentos e de tabelas locais — nunca de ITEMS/BUSINESS_PARTNERS/
/// WAREHOUSES, vazias em modo SAPB1.
/// </summary>
public static class ReportText
{
    public const string NoProduct = "Sem produto vinculado";
    public const string InvertedPeriod = "A data final não pode ser anterior à inicial.";

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Período obrigatório e não invertido; devolve a mensagem do 400 ou null.</summary>
    public static string? ValidatePeriod(DateTime from, DateTime to, string missingMessage)
    {
        if (from == default || to == default)
            return missingMessage;

        if (to.Date < from.Date)
            return InvertedPeriod;

        return null;
    }

    /// <summary>"Rótulo: dd/MM/yyyy a dd/MM/yyyy" — primeira parte da linha de filtros.</summary>
    public static string Period(string label, DateTime from, DateTime to) =>
        $"{label}: {Date(from)} a {Date(to)}";

    public static string Date(DateTime? value) =>
        value is { } date ? date.ToString("dd/MM/yyyy", Culture) : "";

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

    /// <summary>Nome do snapshot; sem nome, o código; sem nenhum, vazio.</summary>
    public static string NameOrCode(string? code, string? name) =>
        string.IsNullOrWhiteSpace(name) ? (code ?? "").Trim() : name.Trim();

    /// <summary>Valores distintos e não vazios, em ordem ORDINAL (estável em qualquer cultura), separados por ", ".</summary>
    public static string JoinDistinct(IEnumerable<string?> values) =>
        string.Join(", ", values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

    public static string Describe(string? description, string fallback) =>
        string.IsNullOrWhiteSpace(description) ? fallback : description;

    public static string JoinFilters(IEnumerable<string> parts) => string.Join(" | ", parts);

    /// <summary>
    /// Verdadeiro quando todas as linhas têm a mesma UM (ou não há linha). Só então o total geral
    /// de quantidade faz sentido — KG e TN nunca se somam.
    /// </summary>
    public static bool IsSingleUnit(IEnumerable<string?> units) =>
        units.Select(u => (u ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1;

    /// <summary>Produto de um grupo "Produto (código) - UM", sem o sufixo da UM.</summary>
    public static string? ProductOfGroup(string? group)
    {
        if (group is null)
            return null;

        var cut = group.LastIndexOf(" - ", StringComparison.Ordinal);
        return cut > 0 ? group[..cut] : group;
    }
}
```

`SiagroB1.Reports/Helpers/InvoiceReportText.cs` — substitua o conteúdo inteiro por (mesma API pública, mesmas strings; o genérico passa a vir de `ReportText`):

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Textos pt-BR dos relatórios de documentos fiscais. O que não é fiscal (data, parceiro,
/// produto, filial, período) mora em <see cref="ReportText"/>, compartilhado com a logística;
/// aqui ficam os delegadores — mantidos para não mexer nos serviços do pacote 1.
/// </summary>
public static class InvoiceReportText
{
    public const string NoOrigin = "—";
    public const string NoProduct = ReportText.NoProduct;

    private static readonly InvoiceStatus[] DefaultStatuses =
        [InvoiceStatus.Pending, InvoiceStatus.Confirmed, InvoiceStatus.Returned];

    public static string? Validate(InvoiceReportRequest request) =>
        ReportText.ValidatePeriod(request.FromDate, request.ToDate, "Informe o período de emissão.");

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

    public static string DocumentNumber(string? number, string? series) => ReportText.DocumentNumber(number, series);

    public static string Partner(string? code, string? name) => ReportText.Partner(code, name);

    public static string Product(string? code, string? name) => ReportText.Product(code, name);

    public static string BranchName(Branch? branch, string? code) => ReportText.BranchName(branch, code);

    public static string Date(DateTime? value) => ReportText.Date(value);

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
        var parts = new List<string> { ReportText.Period("Emissão", request.FromDate, request.ToDate) };

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            parts.Add($"Filial: {ReportText.Describe(branchName, request.BranchCode)}");

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            parts.Add($"{partnerLabel}: {ReportText.Describe(partnerName, request.CardCode)}");

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            parts.Add($"Produto: {ReportText.Describe(productName, request.ItemCode)}");

        parts.Add("Situação: " + string.Join(", ", EffectiveStatuses(request.Statuses).Select(s => Status(s))));

        if (standalone && request.NfeStatuses is { Count: > 0 } nfe)
            parts.Add("Situação NF-e: " + string.Join(", ", nfe.Distinct().Select(s => s == NfeStatus.None ? "Não emitida" : Nfe(s))));

        parts.AddRange(extra);

        return ReportText.JoinFilters(parts);
    }
}
```

`SiagroB1.Reports/Dtos/LogisticsReportRequest.cs` (criar e `git add`):

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros comuns aos relatórios de logística de venda (cargas, liberações, romaneios). Só o
/// período é obrigatório; a data filtrada depende do relatório.
/// </summary>
public abstract class LogisticsReportRequest
{
    /// <summary>Início do período.</summary>
    public DateTime FromDate { get; set; }

    /// <summary>Fim do período, inclusivo até o fim do dia.</summary>
    public DateTime ToDate { get; set; }

    public string? BranchCode { get; set; }

    public string? ItemCode { get; set; }
}
```

`SiagroB1.Reports/Helpers/LogisticsReportText.cs` (criar e `git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Textos pt-BR e regras comuns aos relatórios de logística de venda. Os rótulos de situação,
/// tipo e origem são OS MESMOS de <c>siagro-b1-frontend/webapp/model/formatter.ts</c>: o PDF não
/// pode chamar de "Aberta" a carga que a tela chama de "Carregada".
/// </summary>
public static class LogisticsReportText
{
    public const string MissingPeriod = "Informe o período.";
    public const string PlannedSuffix = " (planejado)";

    // Arrays (e não listas) para o EF traduzir o Contains em IN.
    private static readonly ShipmentLoadStatus[] DefaultLoadStatuses =
    [
        ShipmentLoadStatus.Open, ShipmentLoadStatus.PartiallyInvoiced, ShipmentLoadStatus.Invoiced,
        ShipmentLoadStatus.Planned, ShipmentLoadStatus.Returned, ShipmentLoadStatus.Completed,
        ShipmentLoadStatus.InTransshipment, ShipmentLoadStatus.Discharged,
    ];

    private static readonly ReleaseStatus[] DefaultReleaseStatuses =
        [ReleaseStatus.Pending, ReleaseStatus.Actived, ReleaseStatus.Completed, ReleaseStatus.Paused];

    private static readonly StorageTransactionsStatus[] DefaultTransactionStatuses =
    [
        StorageTransactionsStatus.Pending, StorageTransactionsStatus.Confirmed,
        StorageTransactionsStatus.Invoiced, StorageTransactionsStatus.Returned,
    ];

    public static string? Validate(LogisticsReportRequest request) =>
        ReportText.ValidatePeriod(request.FromDate, request.ToDate, MissingPeriod);

    public static ShipmentLoadStatus[] EffectiveLoadStatuses(IReadOnlyCollection<ShipmentLoadStatus>? requested) =>
        Effective(requested, DefaultLoadStatuses);

    public static ReleaseStatus[] EffectiveReleaseStatuses(IReadOnlyCollection<ReleaseStatus>? requested) =>
        Effective(requested, DefaultReleaseStatuses);

    public static StorageTransactionsStatus[] EffectiveTransactionStatuses(
        IReadOnlyCollection<StorageTransactionsStatus>? requested) =>
        Effective(requested, DefaultTransactionStatuses);

    private static T[] Effective<T>(IReadOnlyCollection<T>? requested, T[] defaults) where T : struct, Enum =>
        requested is { Count: > 0 } ? requested.Distinct().ToArray() : defaults;

    public static string LoadStatusText(ShipmentLoadStatus status) => status switch
    {
        ShipmentLoadStatus.Planned => "Planejada",
        ShipmentLoadStatus.Open => "Carregada",
        ShipmentLoadStatus.PartiallyInvoiced => "Faturada Parcial",
        ShipmentLoadStatus.Invoiced => "Faturada",
        ShipmentLoadStatus.Cancelled => "Cancelada",
        ShipmentLoadStatus.Returned => "Devolvida",
        ShipmentLoadStatus.Completed => "Concluída",
        ShipmentLoadStatus.InTransshipment => "Em Transbordo",
        ShipmentLoadStatus.Discharged => "Descarregada",
        _ => status.ToString(),
    };

    public static string LoadTypeText(ShipmentLoadType type) =>
        type == ShipmentLoadType.Removal ? "Remoção" : "Normal";

    public static string ReleaseStatusText(ReleaseStatus status) => status switch
    {
        ReleaseStatus.Pending => "Pendente",
        ReleaseStatus.Actived => "Ativo",
        ReleaseStatus.Completed => "Finalizado",
        ReleaseStatus.Cancelled => "Cancelado",
        ReleaseStatus.Paused => "Pausado",
        _ => status.ToString(),
    };

    public static string OriginText(ReleaseOrigin origin) => origin switch
    {
        ReleaseOrigin.OwnershipTransfer => "Transferência",
        ReleaseOrigin.SalesReturn => "Devolução",
        ReleaseOrigin.Transshipment => "Transbordo",
        _ => "Compra",
    };

    public static string TransactionStatusText(StorageTransactionsStatus status) => status switch
    {
        StorageTransactionsStatus.Pending => "Pendente",
        StorageTransactionsStatus.Confirmed => "Confirmado",
        StorageTransactionsStatus.Cancelled => "Cancelado",
        StorageTransactionsStatus.Invoiced => "Faturado",
        StorageTransactionsStatus.Returned => "Devolvido",
        _ => status.ToString(),
    };

    /// <summary>
    /// "Situação: todas" / "Situação: todas, exceto Cancelada" / lista na ordem numérica do enum.
    /// A tela envia o padrão já marcado, então o padrão também chega aqui como lista explícita.
    /// </summary>
    public static string StatusFilter<T>(IReadOnlyCollection<T> effective, Func<T, string> label, T cancelled)
        where T : struct, Enum
    {
        var chosen = effective.ToHashSet();
        var all = Enum.GetValues<T>();

        if (all.All(chosen.Contains))
            return "Situação: todas";

        if (!chosen.Contains(cancelled) && all.Where(s => !s.Equals(cancelled)).All(chosen.Contains))
            return $"Situação: todas, exceto {label(cancelled)}";

        return "Situação: " + string.Join(", ", chosen.OrderBy(s => Convert.ToInt32(s)).Select(label));
    }

    /// <summary>
    /// Cliente(s) da carga: nomes distintos das notas de saída VIVAS (cancelada não conta; status
    /// nulo é Pendente). Sem nota viva, o cliente do planejamento com " (planejado)" — campo
    /// informativo da Logística, ver <see cref="ShipmentLoad.CardCode"/>. Sem carga, vazio.
    /// </summary>
    public static string LoadCustomers(ShipmentLoad? load)
    {
        if (load is null)
            return "";

        var live = LiveInvoices(load);
        if (live.Count > 0)
            return ReportText.JoinDistinct(live.Select(i => ReportText.NameOrCode(i.CardCode, i.CardName)));

        var planned = ReportText.NameOrCode(load.CardCode, load.CardName);
        return planned.Length == 0 ? "" : planned + PlannedSuffix;
    }

    /// <summary>Mesmo critério de <see cref="LoadCustomers"/>, para o filtro de Cliente.</summary>
    public static bool LoadHasCustomer(ShipmentLoad? load, string cardCode)
    {
        if (load is null)
            return false;

        var live = LiveInvoices(load);
        return live.Count > 0 ? live.Any(i => i.CardCode == cardCode) : load.CardCode == cardCode;
    }

    /// <summary>Nome do cliente filtrado, para a linha de filtros; null se não aparecer em nenhuma carga.</summary>
    public static string? CustomerName(IEnumerable<ShipmentLoad?> loads, string? cardCode)
    {
        if (string.IsNullOrWhiteSpace(cardCode))
            return null;

        foreach (var load in loads)
        {
            if (load is null)
                continue;

            var live = LiveInvoices(load);
            var invoice = live.FirstOrDefault(i => i.CardCode == cardCode);
            if (invoice is not null)
                return ReportText.NameOrCode(invoice.CardCode, invoice.CardName);

            if (live.Count == 0 && load.CardCode == cardCode)
                return ReportText.NameOrCode(load.CardCode, load.CardName);
        }

        return null;
    }

    /// <summary>
    /// Linha de filtros: período, filial, produto, situação e os filtros próprios do relatório.
    /// Descrições ausentes caem para o código digitado.
    /// </summary>
    public static string BuildFilters(
        string periodLabel,
        LogisticsReportRequest request,
        string? branchName,
        string? productName,
        string statusFilter,
        IEnumerable<string> extra)
    {
        var parts = new List<string> { ReportText.Period(periodLabel, request.FromDate, request.ToDate) };

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            parts.Add($"Filial: {ReportText.Describe(branchName, request.BranchCode)}");

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            parts.Add($"Produto: {ReportText.Describe(productName, request.ItemCode)}");

        parts.Add(statusFilter);
        parts.AddRange(extra);

        return ReportText.JoinFilters(parts);
    }

    /// <summary>Nome da filial pela tabela local BRANCHS (existe nos dois modos).</summary>
    public static async Task<string?> BranchNameAsync(AppDbContext context, string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var branch = await context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == code);
        return branch is null ? null : ReportText.BranchName(branch, code);
    }

    private static List<SalesInvoice> LiveInvoices(ShipmentLoad load) =>
        load.Invoices.Where(i => (i.InvoiceStatus ?? InvoiceStatus.Pending) != InvoiceStatus.Cancelled).ToList();
}
```

Em `SiagroB1.Reports/Services/FastReportService.cs`, substitua o bloco

```csharp
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

por

```csharp
    /// <summary>
    /// Parâmetro reservado dos relatórios agrupados por produto + UM: com <c>true</c> (uma só UM no
    /// resultado) some a nota "UMs diferentes" (<c>uomMixed*</c>); com <c>false</c> somem os totais
    /// gerais de quantidade (<c>uomSum*</c>) — KG e TN não se somam. Ausente, nada muda.
    /// </summary>
    public const string SingleUomParameter = "pSingleUom";
    public const string UomTotalsPrefix = "uomSum";
    public const string MixedUomNotePrefix = "uomMixed";

    public static void HideFiscalObjects(Report report) => HideObjectsWithPrefix(report, "fiscal");

    public static void ApplySingleUom(Report report, bool singleUom) =>
        HideObjectsWithPrefix(report, singleUom ? MixedUomNotePrefix : UomTotalsPrefix);

    private static void HideObjectsWithPrefix(Report report, string prefix)
    {
        foreach (var component in report.AllObjects.OfType<ReportComponentBase>())
        {
            if (component.Name.StartsWith(prefix, StringComparison.Ordinal))
                component.Visible = false;
        }
    }

    private static void ApplyParameters(Report report, Dictionary<string, object> parameters)
    {
        foreach (var param in parameters)
            report.SetParameterValue(param.Key, param.Value);

        if (parameters.TryGetValue(StandaloneParameter, out var standalone) && standalone is false)
            HideFiscalObjects(report);

        if (parameters.TryGetValue(SingleUomParameter, out var single) && single is bool singleUom)
            ApplySingleUom(report, singleUom);
    }
```

`SiagroB1.Application.Tests/Support/LogisticsReportSeed.cs` (criar e `git add`):

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Documentos mínimos para os testes dos relatórios de logística de venda.</summary>
public static class LogisticsReportSeed
{
    public static readonly DateTime Jul01 = new(2026, 7, 1);
    public static readonly DateTime Jul15 = new(2026, 7, 15);
    public static readonly DateTime Jul31 = new(2026, 7, 31);

    public static ShipmentLoad Load(
        string code,
        DateTime? date = null,
        ShipmentLoadStatus status = ShipmentLoadStatus.Open,
        ShipmentLoadType type = ShipmentLoadType.Normal,
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string uom = "KG",
        decimal total = 30_000m,
        string branchCode = "01",
        string truckCode = "ABC1D23",
        string warehouseCode = "ARM01",
        string carrierCardCode = "T001",
        string? plannedCardCode = null,
        string? plannedCardName = null) => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        LoadDate = date ?? Jul15,
        Status = status,
        LoadType = type,
        ItemCode = itemCode,
        ItemName = itemName,
        UnitOfMeasureCode = uom,
        TotalQuantity = total,
        BranchCode = branchCode,
        TruckCode = truckCode,
        WarehouseCode = warehouseCode,
        WarehouseName = "ARMAZÉM CENTRAL",
        CarrierCardCode = carrierCardCode,
        CarrierName = "TRANSPORTES RÁPIDO",
        CardCode = plannedCardCode,
        CardName = plannedCardName,
    };

    /// <summary>Nota de saída ligada à carga (o cliente da carga sai daqui).</summary>
    public static SalesInvoice LoadInvoice(
        ShipmentLoad load,
        string number,
        string cardCode = "C001",
        string cardName = "COOPERATIVA CENTRAL",
        InvoiceStatus? status = InvoiceStatus.Confirmed) => new()
    {
        Key = Guid.NewGuid(),
        InvoiceNumber = number,
        InvoiceDate = load.LoadDate,
        InvoiceStatus = status,
        InvoiceType = SalesInvoiceType.Normal,
        BranchCode = load.BranchCode,
        CardCode = cardCode,
        CardName = cardName,
        ShipmentLoadKey = load.Key,
    };

    public static SalesContract SalesContract(
        string code = "CV000001",
        string cardCode = "C001",
        string cardName = "COOPERATIVA CENTRAL",
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string uom = "KG",
        int? agentCode = 7,
        string? agentName = "JOÃO VENDEDOR",
        string? regionCode = "R01",
        string branchCode = "01") => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        BranchCode = branchCode,
        CardCode = cardCode,
        CardName = cardName,
        ItemCode = itemCode,
        ItemName = itemName,
        UnitOfMeasureCode = uom,
        HarvestSeasonCode = "25/26",
        AgentCode = agentCode,
        AgentName = agentName,
        LogisticRegionCode = regionCode,
        DeliveryStartDate = Jul01,
        DeliveryEndDate = new DateTime(2026, 9, 30),
        TotalVolume = 1_000_000m,
    };

    public static LogisticRegion Region(string code = "R01", string name = "NORTE") => new() { Code = code, Name = name };

    public static SalesShipmentRelease SalesRelease(
        SalesContract contract,
        DateTime? date = null,
        decimal released = 1_000m,
        decimal shipped = 0m,
        ReleaseStatus status = ReleaseStatus.Actived,
        string deliveryLocationCode = "L01",
        string? deliveryLocationName = "PORTO DE PARANAGUÁ",
        string branchCode = "01") => new()
    {
        Key = Guid.NewGuid(),
        SalesContractKey = contract.Key,
        ReleaseDate = date ?? Jul15,
        ReleasedQuantity = released,
        ShippedQuantity = shipped,
        Status = status,
        DeliveryLocationCode = deliveryLocationCode,
        DeliveryLocationName = deliveryLocationName,
        BranchCode = branchCode,
    };

    public static PurchaseContract PurchaseContract(
        string code = "PC000001",
        string cardCode = "F001",
        string cardName = "PRODUTOR RURAL",
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string uom = "KG",
        string branchCode = "01") => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        BranchCode = branchCode,
        CardCode = cardCode,
        CardName = cardName,
        ItemCode = itemCode,
        ItemName = itemName,
        UnitOfMeasureCode = uom,
        HarvestSeasonCode = "25/26",
        DeliveryLocationCode = "ARM01",
        DeliveryLocationName = "ARMAZÉM CENTRAL",
        DeliveryStartDate = Jul01,
        DeliveryEndDate = new DateTime(2026, 9, 30),
        TotalVolume = 1_000_000m,
    };

    public static ShipmentRelease PurchaseRelease(
        PurchaseContract contract,
        DateTime? date = null,
        decimal released = 1_000m,
        decimal shipped = 0m,
        ReleaseStatus status = ReleaseStatus.Actived,
        ReleaseOrigin origin = ReleaseOrigin.Standard,
        string deliveryLocationCode = "ARM01",
        string? deliveryLocationName = "ARMAZÉM CENTRAL",
        string branchCode = "01") => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contract.Key,
        ReleaseDate = date ?? Jul15,
        ReleasedQuantity = released,
        ShippedQuantity = shipped,
        Status = status,
        Origin = origin,
        DeliveryLocationCode = deliveryLocationCode,
        DeliveryLocationName = deliveryLocationName,
        BranchCode = branchCode,
    };

    /// <summary>
    /// Romaneio de venda (SalesShipment = 7). <c>CardCode</c> é o FORNECEDOR da perna de compra;
    /// o cliente chega pela carga.
    /// </summary>
    public static StorageTransaction SalesShipment(
        string code,
        DateTime? date = null,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        ShipmentLoad? load = null,
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string uom = "KG",
        decimal gross = 30_000m,
        decimal drying = 0m,
        decimal cleaning = 0m,
        decimal others = 0m,
        string branchCode = "01",
        string warehouseCode = "ARM01",
        string truckCode = "ABC1D23",
        string cardCode = "F001",
        string cardName = "PRODUTOR RURAL") => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        TransactionDate = date ?? Jul15,
        TransactionType = StorageTransactionType.SalesShipment,
        TransactionStatus = status,
        ShipmentLoadKey = load?.Key,
        ItemCode = itemCode,
        ItemName = itemName,
        UnitOfMeasureCode = uom,
        GrossWeight = gross,
        DryingDiscount = drying,
        CleaningDiscount = cleaning,
        OthersDicount = others,
        NetWeight = gross - drying - cleaning - others,
        BranchCode = branchCode,
        WarehouseCode = warehouseCode,
        WarehouseName = "ARMAZÉM CENTRAL",
        TruckCode = truckCode,
        CardCode = cardCode,
        CardName = cardName,
        InvoiceNumber = "000123",
        InvoiceSerie = "1",
    };
}
```

Se o compilador acusar `CS9035` (propriedade `required` não preenchida) em alguma entidade, preencha-a no seed com valor neutro e registre no relatório da task.

- [ ] **Step 4: Run tests to verify they pass — inclusive os do pacote 1**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"`
Expected: PASS — os três arquivos novos **e** todos os do pacote 1 (`InvoiceReportTextTests`, `*InvoicesByPeriodReportServiceTests`, `*InvoiceItemsReportServiceTests`, `SalesReturnsReportServiceTests`, `InvoiceReportsPdfTests`, `InvoiceReportsLayoutTests`, `FastReportServiceFiscalTests`) sem alteração nenhuma.

- [ ] **Step 5: Commit**

```bash
git branch --show-current   # feature/logistics-reports
git add SiagroB1.Reports/Helpers/ReportText.cs SiagroB1.Reports/Helpers/InvoiceReportText.cs SiagroB1.Reports/Dtos/LogisticsReportRequest.cs SiagroB1.Reports/Helpers/LogisticsReportText.cs SiagroB1.Reports/Services/FastReportService.cs SiagroB1.Application.Tests/Support/LogisticsReportSeed.cs SiagroB1.Application.Tests/Reports/ReportTextTests.cs SiagroB1.Application.Tests/Reports/LogisticsReportTextTests.cs SiagroB1.Application.Tests/Reports/FastReportServiceUomTests.cs
git commit -F - <<'EOF'
refactor(reports): base comum dos relatórios de logística

Extrai de InvoiceReportText o que não é fiscal (datas, parceiro, produto,
filial, período) para ReportText, sem mudar nenhuma string do pacote 1, e
cria LogisticsReportText com os rótulos das telas de logística e a regra
do cliente da carga (notas vivas; sem nota, o planejado).

O parâmetro pSingleUom esconde o total geral de quantidade quando o
resultado mistura UMs — KG e TN não se somam.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
EOF
```

---

### Task 2: Cargas por Período

**Files:**
- Create: `SiagroB1.Reports/Dtos/ShipmentLoadsByPeriodRequest.cs`, `SiagroB1.Reports/Dtos/ShipmentLoadRowDto.cs`
- Create: `SiagroB1.Reports/Services/ShipmentLoadsByPeriodReportService.cs`
- Create: `SiagroB1.Reports/Controllers/ShipmentLoadsByPeriodController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/ShipmentLoadsByPeriod.frx`
- Test: `SiagroB1.Application.Tests/Reports/ShipmentLoadsByPeriodReportServiceTests.cs`
- Test: `SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs` (cria a classe)
- Test: `SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs` (cria a classe)

**Interfaces:**
- Consumes: Task 1 (`LogisticsReportRequest`, `LogisticsReportText`, `ReportText`, `FastReportService.SingleUomParameter`, `LogisticsReportSeed`); pacote 1: `InvoiceItemGrouping.BuildGroupResolver`, `RecordingFastReportService`, `TestDb`, `TestWebHostEnvironment`, `TestLogger`.
- Produces:
  - `ShipmentLoadsByPeriodReportService(IUnitOfWork db, IFastReportService reportService)` com `Task<List<ShipmentLoadRowDto>> BuildRowsAsync(ShipmentLoadsByPeriodRequest)` e `Task<byte[]> ExecuteAsync(ShipmentLoadsByPeriodRequest)`.
  - `POST /reports/ShipmentLoadsByPeriod` (consumido pela Task 7).
  - `LogisticsReportsPdfTests` com helpers privados `FastReport(IConfiguration)`, `Configuration(string erp)`, `Keep(string report, string erp, byte[] pdf)`, `Save(IUnitOfWork)` — reutilizados pelas Tasks 3–5.
  - `LogisticsReportsLayoutTests` com `Templates` e `LongestText` — as Tasks 3–5 acrescentam entradas.

- [ ] **Step 1: Write the failing service tests**

`SiagroB1.Application.Tests/Reports/ShipmentLoadsByPeriodReportServiceTests.cs`:

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.LogisticsReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class ShipmentLoadsByPeriodReportServiceTests
{
    [Fact]
    public async Task BuildRows_IncludesTheWholeLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001", new DateTime(2026, 7, 31, 23, 45, 0)));
        db.Context.ShipmentLoads.Add(Load("CG000002", new DateTime(2026, 8, 1)));
        db.Context.ShipmentLoads.Add(Load("CG000003", new DateTime(2026, 6, 30)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CG000001" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001", status: ShipmentLoadStatus.Open));
        db.Context.ShipmentLoads.Add(Load("CG000002", status: ShipmentLoadStatus.Planned, total: 0m));
        db.Context.ShipmentLoads.Add(Load("CG000003", status: ShipmentLoadStatus.Cancelled));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CG000001", "CG000002" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_CancelledLoadHasNoBalance()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000001", status: ShipmentLoadStatus.Cancelled);
        load.InvoicedQuantity = 10_000m;
        db.Context.ShipmentLoads.Add(load);
        await Save(db);

        var request = Request();
        request.Statuses = [ShipmentLoadStatus.Cancelled];
        var row = (await Service(db).BuildRowsAsync(request)).Single();

        Assert.Equal("Cancelada", row.Status);
        Assert.Equal(0m, row.BalanceQuantity);
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("ItemCode")]
    [InlineData("LoadType")]
    [InlineData("WarehouseCode")]
    [InlineData("CarrierCardCode")]
    [InlineData("TruckCode")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001"));
        db.Context.ShipmentLoads.Add(Load("CG000002", type: ShipmentLoadType.Removal, itemCode: "20001", itemName: "MILHO",
            branchCode: "99", truckCode: "XYZ9Z99", warehouseCode: "ARM99", carrierCardCode: "T999"));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "LoadType": request.LoadType = ShipmentLoadType.Normal; break;
            case "WarehouseCode": request.WarehouseCode = "ARM01"; break;
            case "CarrierCardCode": request.CarrierCardCode = "T001"; break;
            case "TruckCode": request.TruckCode = "ABC1D23"; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "CG000001" }, rows.Select(r => r.Code));
    }

    // Review Focus 1: várias notas, cliente repetido, nota cancelada, status nulo.
    [Fact]
    public async Task BuildRows_CustomersComeFromTheLiveInvoicesOfTheLoad()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000001", plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA");
        db.Context.ShipmentLoads.Add(load);
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000102", "C002", "AGRO NORTE"));
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000103", "C001", "COOPERATIVA CENTRAL", status: null));
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000104", "C003", "CLIENTE CANCELADO", status: InvoiceStatus.Cancelled));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("AGRO NORTE, COOPERATIVA CENTRAL", row.Customers);
    }

    // Review Focus 2: carga sem nota.
    [Fact]
    public async Task BuildRows_LoadWithoutInvoiceShowsThePlannedCustomer()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001", status: ShipmentLoadStatus.Planned, total: 0m,
            plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA"));
        db.Context.ShipmentLoads.Add(Load("CG000002", status: ShipmentLoadStatus.Planned, total: 0m));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "FAZENDA BOA VISTA (planejado)", "" }, rows.Select(r => r.Customers));
    }

    // Review Focus 2: o filtro de Cliente segue a mesma regra (nota viva primeiro, planejado só sem nota).
    [Fact]
    public async Task BuildRows_CustomerFilterUsesTheInvoicesThenThePlan()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoiced = Load("CG000001", plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA");
        var planned = Load("CG000002", plannedCardCode: "C001", plannedCardName: "COOPERATIVA CENTRAL");
        var other = Load("CG000003");
        db.Context.ShipmentLoads.AddRange(invoiced, planned, other);
        db.Context.SalesInvoices.Add(LoadInvoice(invoiced, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.SalesInvoices.Add(LoadInvoice(other, "000000102", "C002", "AGRO NORTE"));
        await Save(db);

        var request = Request();
        request.CardCode = "C001";
        Assert.Equal(new[] { "CG000001", "CG000002" }, (await Service(db).BuildRowsAsync(request)).Select(r => r.Code));

        request.CardCode = "C009";
        Assert.Empty(await Service(db).BuildRowsAsync(request));
    }

    [Fact]
    public async Task BuildRows_FormatsColumnsAndUsesTheDomainBalance()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000051", status: ShipmentLoadStatus.PartiallyInvoiced, total: 30_000m);
        load.InvoicedQuantity = 20_000m;
        load.ReturnedToWarehouseQuantity = 1_000m;
        load.TransshippedQuantity = 2_000m;
        load.DischargedQuantity = 19_950m;
        load.FreightPrice = 4_500.50m;
        db.Context.ShipmentLoads.Add(load);
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("SOJA EM GRÃOS (10001) - KG", row.Group);
        Assert.Equal("CG000051", row.Code);
        Assert.Equal("15/07/2026", row.LoadDate);
        Assert.Equal("Normal", row.Type);
        Assert.Equal("Faturada Parcial", row.Status);
        Assert.Equal("ABC1D23", row.Truck);
        Assert.Equal("TRANSPORTES RÁPIDO", row.Carrier);
        Assert.Equal("ARMAZÉM CENTRAL", row.Warehouse);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(30_000m, row.TotalQuantity);
        Assert.Equal(20_000m, row.InvoicedQuantity);
        Assert.Equal(1_000m, row.ReturnedQuantity);
        Assert.Equal(2_000m, row.TransshippedQuantity);
        Assert.Equal(19_950m, row.DischargedQuantity);
        Assert.Equal(7_000m, row.BalanceQuantity);
        Assert.Equal(4_500.50m, row.FreightValue);
    }

    [Fact]
    public async Task BuildRows_WithoutFreightPrintsZeroAndCarrierFallsBackToTheCode()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000001");
        load.FreightPrice = null;
        load.CarrierName = null;
        db.Context.ShipmentLoads.Add(load);
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal(0m, row.FreightValue);
        Assert.Equal("T001", row.Carrier);
    }

    // Review Focus 5: UM mista não se mistura.
    [Fact]
    public async Task BuildRows_GroupsByProductAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001", new DateTime(2026, 7, 10)));
        db.Context.ShipmentLoads.Add(Load("CG000002", new DateTime(2026, 7, 5), uom: "TN", total: 30m));
        db.Context.ShipmentLoads.Add(Load("CG000003", new DateTime(2026, 7, 20), itemCode: "20001", itemName: "MILHO"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { "MILHO (20001) - KG", "SOJA EM GRÃOS (10001) - KG", "SOJA EM GRÃOS (10001) - TN" },
            rows.Select(r => r.Group));
        Assert.Equal(new[] { "CG000003", "CG000001", "CG000002" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_OrdersByDateThenCodeInsideTheGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000002", new DateTime(2026, 7, 10)));
        db.Context.ShipmentLoads.Add(Load("CG000003", new DateTime(2026, 7, 5)));
        db.Context.ShipmentLoads.Add(Load("CG000001", new DateTime(2026, 7, 10)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CG000003", "CG000001", "CG000002" }, rows.Select(r => r.Code));
    }

    // Review Focus 5: total geral de quantidade só com uma UM.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_FlagsWhetherTheResultHasASingleUnit(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(Load("CG000001"));
        if (withTonnes)
            db.Context.ShipmentLoads.Add(Load("CG000002", uom: "TN", total: 30m));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new ShipmentLoadsByPeriodReportService(db, recorder).ExecuteAsync(Request());

        Assert.Equal("ShipmentLoadsByPeriod.frx", recorder.LastReportName);
        Assert.Equal(singleUom, recorder.LastParameters![FastReportService.SingleUomParameter]);
        Assert.Equal(withTonnes ? 2 : 1, ((ICollection<ShipmentLoadRowDto>)recorder.LastData!).Count);
    }

    [Fact]
    public async Task Execute_DescribesEveryFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        var load = Load("CG000001", type: ShipmentLoadType.Removal);
        db.Context.ShipmentLoads.Add(load);
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        await Save(db);
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.BranchCode = "01";
        request.ItemCode = "10001";
        request.LoadType = ShipmentLoadType.Removal;
        request.WarehouseCode = "ARM01";
        request.CarrierCardCode = "T001";
        request.TruckCode = "ABC1D23";
        request.CardCode = "C001";
        await new ShipmentLoadsByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data da carga: 01/07/2026 a 31/07/2026 | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | " +
            "Situação: todas, exceto Cancelada | Tipo: Remoção | Armazém: ARMAZÉM CENTRAL | " +
            "Transportadora: TRANSPORTES RÁPIDO | Placa: ABC1D23 | Cliente: COOPERATIVA CENTRAL",
            recorder.LastParameters!["pFilters"]);
    }

    [Fact]
    public async Task Execute_WithoutResultFallsBackToCodes()
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.ItemCode = "10001";
        request.WarehouseCode = "ARM01";
        request.CardCode = "C001";
        request.Statuses = [ShipmentLoadStatus.Invoiced, ShipmentLoadStatus.Open];
        await new ShipmentLoadsByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data da carga: 01/07/2026 a 31/07/2026 | Produto: 10001 | Situação: Carregada, Faturada | " +
            "Armazém: ARM01 | Cliente: C001",
            recorder.LastParameters!["pFilters"]);
        Assert.True((bool)recorder.LastParameters[FastReportService.SingleUomParameter]);
    }

    private static ShipmentLoadsByPeriodReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService());

    private static ShipmentLoadsByPeriodRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentLoadsByPeriodReportServiceTests"`
Expected: build FAIL — `ShipmentLoadsByPeriodReportService`, `ShipmentLoadsByPeriodRequest`, `ShipmentLoadRowDto` not found.

- [ ] **Step 3: Implement DTOs, service and controller**

`SiagroB1.Reports/Dtos/ShipmentLoadsByPeriodRequest.cs` (criar e `git add`):

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros de "Cargas por Período" (período sobre <c>LoadDate</c>). Situação vazia = todas menos
/// Cancelada; Tipo vazio = Normal e Remoção. Cliente segue a regra de
/// <see cref="Helpers.LogisticsReportText.LoadHasCustomer"/>.
/// </summary>
public class ShipmentLoadsByPeriodRequest : LogisticsReportRequest
{
    public List<ShipmentLoadStatus>? Statuses { get; set; }

    public ShipmentLoadType? LoadType { get; set; }

    public string? WarehouseCode { get; set; }

    public string? CarrierCardCode { get; set; }

    public string? TruckCode { get; set; }

    public string? CardCode { get; set; }
}
```

`SiagroB1.Reports/Dtos/ShipmentLoadRowDto.cs` (criar e `git add`):

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>Linha de "Cargas por Período". <see cref="Group"/> = produto + UM.</summary>
public class ShipmentLoadRowDto
{
    public string Group { get; set; } = "";
    public string Code { get; set; } = "";
    public string LoadDate { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string Truck { get; set; } = "";
    public string Carrier { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string Customers { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal TotalQuantity { get; set; }
    public decimal InvoicedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal TransshippedQuantity { get; set; }
    public decimal DischargedQuantity { get; set; }
    public decimal BalanceQuantity { get; set; }
    public decimal FreightValue { get; set; }
}
```

`SiagroB1.Reports/Services/ShipmentLoadsByPeriodReportService.cs` (criar e `git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Cargas por período, agrupadas por produto + UM. Tudo sai de SHIPMENT_LOADS (snapshots de
/// produto, armazém, transportadora) e das notas de saída da carga (cliente); nada de
/// ITEMS/BUSINESS_PARTNERS/WAREHOUSES, vazias em SAPB1. O saldo é o do domínio
/// (<see cref="ShipmentLoad.AvailableQuantity"/>): zero quando cancelada.
/// </summary>
public class ShipmentLoadsByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    public async Task<List<ShipmentLoadRowDto>> BuildRowsAsync(ShipmentLoadsByPeriodRequest request) =>
        ToRows(await LoadAsync(request));

    public async Task<byte[]> ExecuteAsync(ShipmentLoadsByPeriodRequest request)
    {
        var loads = await LoadAsync(request);
        var rows = ToRows(loads);
        var first = rows.Count > 0 ? rows[0] : null;
        var statuses = LogisticsReportText.EffectiveLoadStatuses(request.Statuses);

        var extra = new List<string>();
        if (request.LoadType is { } type)
            extra.Add($"Tipo: {LogisticsReportText.LoadTypeText(type)}");
        if (!string.IsNullOrWhiteSpace(request.WarehouseCode))
            extra.Add($"Armazém: {ReportText.Describe(first?.Warehouse, request.WarehouseCode)}");
        if (!string.IsNullOrWhiteSpace(request.CarrierCardCode))
            extra.Add($"Transportadora: {ReportText.Describe(first?.Carrier, request.CarrierCardCode)}");
        if (!string.IsNullOrWhiteSpace(request.TruckCode))
            extra.Add($"Placa: {request.TruckCode}");
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            extra.Add($"Cliente: {ReportText.Describe(LogisticsReportText.CustomerName(loads, request.CardCode), request.CardCode)}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = LogisticsReportText.BuildFilters(
                "Data da carga",
                request,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                ReportText.ProductOfGroup(first?.Group),
                LogisticsReportText.StatusFilter(statuses, LogisticsReportText.LoadStatusText, ShipmentLoadStatus.Cancelled),
                extra),
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "ShipmentLoadsByPeriod.frx", rows, "ShipmentLoads", "ShipmentLoads", parameters);
    }

    private async Task<List<ShipmentLoad>> LoadAsync(ShipmentLoadsByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = LogisticsReportText.EffectiveLoadStatuses(request.Statuses);

        // As notas vêm por Include (e não por um segundo SELECT com IN de chaves): o cliente da
        // carga é o snapshot CardCode/CardName das notas vivas.
        var query = db.Context.ShipmentLoads
            .AsNoTracking()
            .Include(l => l.Invoices)
            .Where(l => l.LoadDate >= from && l.LoadDate < toExclusive)
            .Where(l => statuses.Contains(l.Status));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(l => l.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(l => l.ItemCode == request.ItemCode);

        if (request.LoadType is { } type)
            query = query.Where(l => l.LoadType == type);

        if (!string.IsNullOrWhiteSpace(request.WarehouseCode))
            query = query.Where(l => l.WarehouseCode == request.WarehouseCode);

        if (!string.IsNullOrWhiteSpace(request.CarrierCardCode))
            query = query.Where(l => l.CarrierCardCode == request.CarrierCardCode);

        if (!string.IsNullOrWhiteSpace(request.TruckCode))
            query = query.Where(l => l.TruckCode == request.TruckCode);

        var loads = await query.ToListAsync();

        // Cliente depende de regra (nota viva, senão o planejado): filtrado em C#.
        if (request.CardCode is { } cardCode && !string.IsNullOrWhiteSpace(cardCode))
            loads = loads.Where(l => LogisticsReportText.LoadHasCustomer(l, cardCode)).ToList();

        return loads;
    }

    private static List<ShipmentLoadRowDto> ToRows(List<ShipmentLoad> loads)
    {
        var groupOf = InvoiceItemGrouping.BuildGroupResolver(
            loads, l => l.ItemCode, l => l.ItemName, l => l.UnitOfMeasureCode);

        return loads
            .OrderBy(l => groupOf(l), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(l => l.LoadDate)
            .ThenBy(l => l.Code ?? "", StringComparer.Ordinal)
            .Select(l => ToRow(l, groupOf(l)))
            .ToList();
    }

    private static ShipmentLoadRowDto ToRow(ShipmentLoad l, string group) => new()
    {
        Group = group,
        Code = l.Code ?? "",
        LoadDate = ReportText.Date(l.LoadDate),
        Type = LogisticsReportText.LoadTypeText(l.LoadType),
        Status = LogisticsReportText.LoadStatusText(l.Status),
        Truck = l.TruckCode ?? "",
        Carrier = ReportText.NameOrCode(l.CarrierCardCode, l.CarrierName),
        Warehouse = ReportText.NameOrCode(l.WarehouseCode, l.WarehouseName),
        Customers = LogisticsReportText.LoadCustomers(l),
        UnitOfMeasure = l.UnitOfMeasureCode,
        TotalQuantity = l.TotalQuantity,
        InvoicedQuantity = l.InvoicedQuantity,
        ReturnedQuantity = l.ReturnedToWarehouseQuantity,
        TransshippedQuantity = l.TransshippedQuantity,
        DischargedQuantity = l.DischargedQuantity,
        BalanceQuantity = l.AvailableQuantity,
        FreightValue = l.FreightPrice ?? 0m,
    };
}
```

`SiagroB1.Reports/Controllers/ShipmentLoadsByPeriodController.cs` (criar e `git add`):

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/ShipmentLoadsByPeriod")]
public class ShipmentLoadsByPeriodController(ShipmentLoadsByPeriodReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] ShipmentLoadsByPeriodRequest request)
    {
        if (LogisticsReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"shipment-loads-by-period.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

O Scrutor registra o serviço por si mesmo (sufixo `Service`); não mexa em `DI/ServiceCollectionExtensions.cs`.

- [ ] **Step 4: Run service tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentLoadsByPeriodReportServiceTests"`
Expected: PASS.

- [ ] **Step 5: Template**

Crie `SiagroB1.Reports/Reports/Templates/ShipmentLoadsByPeriod.frx` exatamente com o conteúdo abaixo (e `git add`). Colunas:

| Coluna (cabeçalho) | Campo | Left | Largura | Formato | Objeto (cabeçalho / dado) |
|---|---|---|---|---|---|
| Código | Code | 0 | 50 | texto | `hdrCode` / `txtCode` |
| Data | LoadDate | 50 | 62 | texto | `hdrLoadDate` / `txtLoadDate` |
| Tipo | Type | 112 | 46 | texto | `hdrType` / `txtType` |
| Situação | Status | 158 | 92 | texto | `hdrStatus` / `txtStatus` |
| Placa | Truck | 250 | 50 | texto | `hdrTruck` / `txtTruck` |
| Transp. | Carrier | 300 | 62 | texto | `hdrCarrier` / `txtCarrier` |
| Armazém | Warehouse | 362 | 62 | texto | `hdrWarehouse` / `txtWarehouse` |
| Cliente(s) | Customers | 424 | 96 | texto | `hdrCustomers` / `txtCustomers` |
| Total | TotalQuantity | 520 | 82 | Number, 3 casas | `hdrTotalQuantity` / `txtTotalQuantity` |
| Faturado | InvoicedQuantity | 602 | 82 | Number, 3 casas | `hdrInvoicedQuantity` / `txtInvoicedQuantity` |
| Devolvido | ReturnedQuantity | 684 | 82 | Number, 3 casas | `hdrReturnedQuantity` / `txtReturnedQuantity` |
| Transbordado | TransshippedQuantity | 766 | 82 | Number, 3 casas | `hdrTransshippedQuantity` / `txtTransshippedQuantity` |
| Descarregado | DischargedQuantity | 848 | 82 | Number, 3 casas | `hdrDischargedQuantity` / `txtDischargedQuantity` |
| Saldo | BalanceQuantity | 930 | 82 | Number, 3 casas | `hdrBalanceQuantity` / `txtBalanceQuantity` |
| Frete | FreightValue | 1012 | 72 | Number, 2 casas | `hdrFreightValue` / `txtFreightValue` |
| **Soma** | | | **1084** | | |

Subtotal (6pt) de todas as colunas numéricas por produto/UM; no sumário, `uomSum*` para as seis quantidades, `txtSumFreightValue` para o frete, `uomMixedNote` e a contagem de cargas. Com sete colunas numéricas (564 px) sobra pouco para texto: o cabeçalho da transportadora é "Transp." ("Transportadora" exigiria 80,8 px) e transportadora, armazém e clientes saem só pelo nome, com reticências.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Report ScriptLanguage="CSharp" ReportInfo.Created="10/08/2026 10:00:00" ReportInfo.Modified="10/08/2026 10:00:00" ReportInfo.CreatorVersion="2026.1.0.0">
  <Styles>
    <Style Name="EvenRows" Fill.Color="Gainsboro" Font="Arial, 10pt"/>
  </Styles>
  <Dictionary>
    <BusinessObjectDataSource Name="ShipmentLoads" ReferenceName="ShipmentLoads" DataType="System.Int32" Enabled="true">
      <Column Name="Group" DataType="System.String"/>
      <Column Name="Code" DataType="System.String"/>
      <Column Name="LoadDate" DataType="System.String"/>
      <Column Name="Type" DataType="System.String"/>
      <Column Name="Status" DataType="System.String"/>
      <Column Name="Truck" DataType="System.String"/>
      <Column Name="Carrier" DataType="System.String"/>
      <Column Name="Warehouse" DataType="System.String"/>
      <Column Name="Customers" DataType="System.String"/>
      <Column Name="TotalQuantity" DataType="System.Decimal"/>
      <Column Name="InvoicedQuantity" DataType="System.Decimal"/>
      <Column Name="ReturnedQuantity" DataType="System.Decimal"/>
      <Column Name="TransshippedQuantity" DataType="System.Decimal"/>
      <Column Name="DischargedQuantity" DataType="System.Decimal"/>
      <Column Name="BalanceQuantity" DataType="System.Decimal"/>
      <Column Name="FreightValue" DataType="System.Decimal"/>
      <Column Name="UnitOfMeasure" DataType="System.String"/>
    </BusinessObjectDataSource>
    <Parameter Name="pCompanyName" DataType="System.String" AsString=""/>
    <Parameter Name="pFilters" DataType="System.String" AsString=""/>
    <Parameter Name="pSingleUom" DataType="System.Boolean" AsString="true"/>
    <Total Name="TotalGrpTotalQuantity" Expression="[ShipmentLoads.TotalQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpInvoicedQuantity" Expression="[ShipmentLoads.InvoicedQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpReturnedQuantity" Expression="[ShipmentLoads.ReturnedQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpTransshippedQuantity" Expression="[ShipmentLoads.TransshippedQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpDischargedQuantity" Expression="[ShipmentLoads.DischargedQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpBalanceQuantity" Expression="[ShipmentLoads.BalanceQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpFreightValue" Expression="[ShipmentLoads.FreightValue]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalSumTotalQuantity" Expression="[ShipmentLoads.TotalQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumInvoicedQuantity" Expression="[ShipmentLoads.InvoicedQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumReturnedQuantity" Expression="[ShipmentLoads.ReturnedQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumTransshippedQuantity" Expression="[ShipmentLoads.TransshippedQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumDischargedQuantity" Expression="[ShipmentLoads.DischargedQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumBalanceQuantity" Expression="[ShipmentLoads.BalanceQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumFreightValue" Expression="[ShipmentLoads.FreightValue]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalCountAll" TotalType="Count" Evaluator="Data1" PrintOn="ReportSummary1"/>
  </Dictionary>
  <ReportPage Name="Page1" Landscape="true" PaperWidth="297" PaperHeight="210" LeftMargin="5" TopMargin="5" RightMargin="5" BottomMargin="5" RawPaperSize="9" Watermark.Font="Arial, 60pt">
    <PageHeaderBand Name="PageHeader1" Width="1084" Height="113.4">
      <PictureObject Name="picLogo" Left="0" Top="2" Width="94.5" Height="37.8" SizeMode="Zoom"/>
      <TextObject Name="txtCompany" Left="100" Top="2" Width="700" Height="37.8" Text="[pCompanyName]" VertAlign="Center" Font="Arial, 11pt, style=Bold"/>
      <TextObject Name="txtDate" Left="937" Top="2" Width="147" Height="18.9" Text="[Date]" Format="Date" Format.Format="d" HorzAlign="Right" VertAlign="Center" Font="Tahoma, 6pt"/>
      <TextObject Name="txtTitle" Top="45" Width="1084" Height="28.35" Text="Cargas por Período" HorzAlign="Center" VertAlign="Center" Font="Arial, 14pt, style=Bold, Italic"/>
      <TextObject Name="txtFilters" Top="75" Width="1084" Height="37.8" Text="[pFilters]" HorzAlign="Center" VertAlign="Top" WordWrap="true" Font="Consolas, 8pt, style=Italic"/>
    </PageHeaderBand>
    <ColumnHeaderBand Name="ColumnHeader1" Top="116.6" Width="1084" Height="17">
      <TextObject Name="hdrCode" Left="0" Width="50" Height="17" Text="Código" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrLoadDate" Left="50" Width="62" Height="17" Text="Data" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrType" Left="112" Width="46" Height="17" Text="Tipo" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrStatus" Left="158" Width="92" Height="17" Text="Situação" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrTruck" Left="250" Width="50" Height="17" Text="Placa" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrCarrier" Left="300" Width="62" Height="17" Text="Transp." VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrWarehouse" Left="362" Width="62" Height="17" Text="Armazém" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrCustomers" Left="424" Width="96" Height="17" Text="Cliente(s)" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrTotalQuantity" Left="520" Width="82" Height="17" Text="Total" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrInvoicedQuantity" Left="602" Width="82" Height="17" Text="Faturado" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrReturnedQuantity" Left="684" Width="82" Height="17" Text="Devolvido" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrTransshippedQuantity" Left="766" Width="82" Height="17" Text="Transbordado" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrDischargedQuantity" Left="848" Width="82" Height="17" Text="Descarregado" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrBalanceQuantity" Left="930" Width="82" Height="17" Text="Saldo" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrFreightValue" Left="1012" Width="72" Height="17" Text="Frete" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
    </ColumnHeaderBand>
    <GroupHeaderBand Name="GroupHeader1" SortOrder="None" Top="136.7" Width="1084" Height="20" Condition="[ShipmentLoads.Group]">
      <TextObject Name="txtGroup" Left="0" Top="2" Width="700" Height="17" Text="[ShipmentLoads.Group]" VertAlign="Center" WordWrap="false" Font="Arial, 8pt, style=Bold"/>
      <DataBand Name="Data1" Top="159.75" Width="1084" Height="17" CanGrow="true" EvenStyle="EvenRows" DataSource="ShipmentLoads">
        <TextObject Name="txtCode" Left="0" Width="50" Height="17" Text="[ShipmentLoads.Code]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtLoadDate" Left="50" Width="62" Height="17" Text="[ShipmentLoads.LoadDate]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtType" Left="112" Width="46" Height="17" Text="[ShipmentLoads.Type]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtStatus" Left="158" Width="92" Height="17" Text="[ShipmentLoads.Status]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtTruck" Left="250" Width="50" Height="17" Text="[ShipmentLoads.Truck]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtCarrier" Left="300" Width="62" Height="17" Text="[ShipmentLoads.Carrier]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtWarehouse" Left="362" Width="62" Height="17" Text="[ShipmentLoads.Warehouse]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtCustomers" Left="424" Width="96" Height="17" Text="[ShipmentLoads.Customers]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtTotalQuantity" Left="520" Width="82" Height="17" Text="[ShipmentLoads.TotalQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtInvoicedQuantity" Left="602" Width="82" Height="17" Text="[ShipmentLoads.InvoicedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtReturnedQuantity" Left="684" Width="82" Height="17" Text="[ShipmentLoads.ReturnedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtTransshippedQuantity" Left="766" Width="82" Height="17" Text="[ShipmentLoads.TransshippedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtDischargedQuantity" Left="848" Width="82" Height="17" Text="[ShipmentLoads.DischargedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtBalanceQuantity" Left="930" Width="82" Height="17" Text="[ShipmentLoads.BalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtFreightValue" Left="1012" Width="72" Height="17" Text="[ShipmentLoads.FreightValue]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="2" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
      </DataBand>
      <GroupFooterBand Name="GroupFooter1" Top="180" Width="1084" Height="20">
        <TextObject Name="txtGrpLabel" Left="0" Top="2" Width="520" Height="17" Text="Subtotal do produto/UM:" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpTotalQuantity" Left="520" Top="2" Width="82" Height="17" Text="[TotalGrpTotalQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpInvoicedQuantity" Left="602" Top="2" Width="82" Height="17" Text="[TotalGrpInvoicedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpReturnedQuantity" Left="684" Top="2" Width="82" Height="17" Text="[TotalGrpReturnedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpTransshippedQuantity" Left="766" Top="2" Width="82" Height="17" Text="[TotalGrpTransshippedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpDischargedQuantity" Left="848" Top="2" Width="82" Height="17" Text="[TotalGrpDischargedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpBalanceQuantity" Left="930" Top="2" Width="82" Height="17" Text="[TotalGrpBalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpFreightValue" Left="1012" Top="2" Width="72" Height="17" Text="[TotalGrpFreightValue]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="2" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
      </GroupFooterBand>
    </GroupHeaderBand>
    <ReportSummaryBand Name="ReportSummary1" Top="203" Width="1084" Height="48">
      <TextObject Name="txtSumLabel" Left="0" Top="4" Width="100" Height="17" Text="TOTAL GERAL:" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Bold"/>
      <TextObject Name="txtSumCount" Left="100" Top="4" Width="130" Height="17" Text="[TotalCountAll] carga(s)" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="uomMixedNote" Left="232" Top="4" Width="288" Height="17" Text="UMs diferentes: quantidades só nos subtotais." VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="uomSumTotalQuantity" Left="520" Top="4" Width="82" Height="17" Text="[TotalSumTotalQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumInvoicedQuantity" Left="602" Top="4" Width="82" Height="17" Text="[TotalSumInvoicedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumReturnedQuantity" Left="684" Top="4" Width="82" Height="17" Text="[TotalSumReturnedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumTransshippedQuantity" Left="766" Top="4" Width="82" Height="17" Text="[TotalSumTransshippedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumDischargedQuantity" Left="848" Top="4" Width="82" Height="17" Text="[TotalSumDischargedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumBalanceQuantity" Left="930" Top="4" Width="82" Height="17" Text="[TotalSumBalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="txtSumFreightValue" Left="1012" Top="4" Width="72" Height="17" Text="[TotalSumFreightValue]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="2" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="txtEmpty" Left="0" Top="26" Width="1084" Height="17" Text="[IIf([TotalCountAll] == 0, &quot;Nenhum registro encontrado.&quot;, &quot;&quot;)]" HorzAlign="Center" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Italic"/>
    </ReportSummaryBand>
    <PageFooterBand Name="PageFooter1" Top="254" Width="1084" Height="26">
      <TextObject Name="txtPage" Left="937" Width="147" Height="17" Text="Página [Page#] de [TotalPages#]" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
    </PageFooterBand>
  </ReportPage>
</Report>
```

- [ ] **Step 6: Layout tests**

`SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs` (criar e `git add`):

```csharp
using FastReport;
using FastReport.Format;
using FastReport.Utils;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Confere, sem renderizar, a geometria dos relatórios de logística: número cabe no maior valor
/// previsto ((5,2 px/char + 4 px de margem de reticência) a 7pt, proporcional ao corpo, + 4 px
/// de padding — medido no PDF: 14 caracteres cortam em 80 px), cabeçalho não corta,
/// campos curtos (data, código, placa, situação) cabem no maior valor real, totais em 6pt numa
/// linha só, nada passa de 1084 px nem se sobrepõe, e o grupo não é reordenado pelo FastReport.
/// </summary>
public class LogisticsReportsLayoutTests
{
    private const float CharWidth = 5.2f;  // Consolas 7pt
    private const float TrimMargin = 4f;   // GDI+ ao aparar com reticências, a 7pt (medido)
    private const float Padding = 4f;
    private const float PageWidth = 1084f;

    public static TheoryData<string> Templates => new()
    {
        "ShipmentLoadsByPeriod",
    };

    /// <summary>Maior valor realista de cada campo de texto curto, em caracteres.</summary>
    private static readonly Dictionary<string, Dictionary<string, int>> LongestText = new()
    {
        ["ShipmentLoadsByPeriod"] = new()
        {
            ["txtCode"] = 8,      // CG000051
            ["txtLoadDate"] = 10, // 31/12/2026
            ["txtType"] = 7,      // Remoção
            ["txtStatus"] = 16,   // Faturada Parcial
            ["txtTruck"] = 8,     // ABC-1D23
        },
    };

    private static Report Load(string template)
    {
        Config.WebMode = true;
        var report = new Report();
        report.Load(Path.Combine(AppContext.BaseDirectory, "ReportTemplates", template + ".frx"));
        return report;
    }

    private static List<TextObject> TextObjects(Report report) =>
        report.AllObjects.OfType<TextObject>().ToList();

    private static float Required(int chars, float fontSize) =>
        (chars * CharWidth + TrimMargin) * fontSize / 7f + Padding;

    private static bool IsTotal(TextObject obj) => obj.Parent is GroupFooterBand or ReportSummaryBand;

    [Theory]
    [MemberData(nameof(Templates))]
    public void NumericObjects_FitTheLargestRealisticValue(string template)
    {
        using var report = Load(template);
        var problems = new List<string>();
        var checkedCount = 0;

        foreach (var obj in TextObjects(report))
        {
            if (obj.Format is not NumberFormat number) continue;
            checkedCount++;
            // dado: 99.999.999,999 (14) / 9.999.999,99 (12); total: 999.999.999,999 (15) / 999.999.999,99 (14)
            var chars = IsTotal(obj)
                ? (number.DecimalDigits == 3 ? 15 : 14)
                : (number.DecimalDigits == 3 ? 14 : 12);
            var required = Required(chars, obj.Font.Size);
            if (required > obj.Width)
                problems.Add($"{obj.Name}: precisa {required:0.#}px, tem {obj.Width:0.#}px");
        }

        Assert.True(checkedCount >= 3, $"{template}: nenhum campo numérico encontrado.");
        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Headers_FitTheirText(string template)
    {
        using var report = Load(template);
        var problems = TextObjects(report)
            .Where(o => o.Parent is ColumnHeaderBand)
            .Where(o => Required(o.Text.Length, o.Font.Size) > o.Width)
            .Select(o => $"{o.Name} (\"{o.Text}\"): precisa {Required(o.Text.Length, o.Font.Size):0.#}px, tem {o.Width:0.#}px")
            .ToList();

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void ShortTextCells_FitTheLongestRealisticValue(string template)
    {
        using var report = Load(template);
        var objects = TextObjects(report);
        var problems = new List<string>();

        foreach (var (name, chars) in LongestText[template])
        {
            var obj = objects.SingleOrDefault(o => o.Name == name);
            if (obj is null)
            {
                problems.Add($"{name} não existe");
                continue;
            }

            if (Required(chars, obj.Font.Size) > obj.Width)
                problems.Add($"{name}: precisa {Required(chars, obj.Font.Size):0.#}px, tem {obj.Width:0.#}px");
        }

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Objects_StayInsideThePage(string template)
    {
        using var report = Load(template);
        var problems = TextObjects(report)
            .Where(o => o.Left + o.Width > PageWidth + 0.01f)
            .Select(o => $"{o.Name} passa de {PageWidth}px ({o.Left + o.Width:0.#})")
            .ToList();

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void HeaderAndDataCells_ShareLeftAndWidth(string template)
    {
        using var report = Load(template);
        var objects = TextObjects(report);
        var problems = new List<string>();

        foreach (var header in objects.Where(o => o.Parent is ColumnHeaderBand))
        {
            var dataName = "txt" + header.Name["hdr".Length..];
            var data = objects.SingleOrDefault(o => o.Name == dataName && o.Parent is DataBand);
            if (data is null)
                problems.Add($"{header.Name} sem {dataName}");
            else if (Math.Abs(data.Left - header.Left) > 0.01f || Math.Abs(data.Width - header.Width) > 0.01f)
                problems.Add($"{header.Name} x {data.Name}");
        }

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void DataCells_DoNotOverlap(string template)
    {
        using var report = Load(template);
        var cells = TextObjects(report).Where(o => o.Parent is DataBand).OrderBy(o => o.Left).ToList();
        var problems = new List<string>();

        for (var i = 0; i + 1 < cells.Count; i++)
        {
            if (cells[i].Left + cells[i].Width > cells[i + 1].Left + 0.01f)
                problems.Add($"{cells[i].Name} sobrepõe {cells[i + 1].Name}");
        }

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Totals_UseSixPointInASingleRow(string template)
    {
        using var report = Load(template);
        var problems = new List<string>();

        foreach (var band in report.AllObjects.OfType<BandBase>().Where(b => b is GroupFooterBand or ReportSummaryBand))
        {
            var numbers = band.Objects.OfType<TextObject>().Where(o => o.Format is NumberFormat).ToList();
            problems.AddRange(numbers.Where(o => Math.Abs(o.Font.Size - 6f) > 0.01f).Select(o => $"{o.Name} não está em 6pt"));
            if (numbers.Select(o => o.Top).Distinct().Count() > 1)
                problems.Add($"{band.Name}: totais em mais de uma linha");
        }

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void GroupHeaders_KeepTheServiceOrder(string template)
    {
        using var report = Load(template);
        var groups = report.AllObjects.OfType<GroupHeaderBand>().ToList();

        Assert.NotEmpty(groups);
        Assert.All(groups, g => Assert.Equal(SortOrder.None, g.SortOrder));
    }
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~LogisticsReportsLayoutTests"`
Expected: PASS (8 testes × 1 template). Se algum falhar, corrija o `.frx` (não o teste) e rode de novo.

- [ ] **Step 7: PDF tests**

`SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs` (criar e `git add`):

```csharp
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.LogisticsReportSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Gera o PDF de ponta a ponta com o template real, nos dois modos (o relatório não tem coluna
/// fiscal; os dois modos provam que nada depende de tabela do SAP). Pega fonte de dados com nome
/// divergente e coluna que o FastReport não converte. Grava em %TEMP%/siagro-logistics-reports.
/// </summary>
public class LogisticsReportsPdfTests : IDisposable
{
    private readonly string _contentRoot;

    public LogisticsReportsPdfTests()
    {
        global::FastReport.Utils.RegisteredObjects.AddConnection(typeof(global::FastReport.Data.MsSqlDataConnection));
        global::FastReport.Utils.Config.WebMode = true;

        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-logistics-pdf", Guid.NewGuid().ToString("N"));
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
    public async Task ShipmentLoadsByPeriod_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        var several = Load("CG000001");
        several.InvoicedQuantity = 20_000m;
        several.FreightPrice = 4_500.50m;
        db.Context.ShipmentLoads.Add(several);
        db.Context.SalesInvoices.Add(LoadInvoice(several, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.SalesInvoices.Add(LoadInvoice(several, "000000102", "C002", "AGRO NORTE"));
        db.Context.ShipmentLoads.Add(Load("CG000002", status: ShipmentLoadStatus.Planned, total: 0m,
            plannedCardCode: "C009", plannedCardName: "FAZENDA BOA VISTA"));
        db.Context.ShipmentLoads.Add(Load("CG000003", itemCode: "20001", itemName: "MILHO", uom: "TN", total: 30m));
        await Save(db);

        var pdf = await new ShipmentLoadsByPeriodReportService(db, FastReport(Configuration(erp)))
            .ExecuteAsync(new ShipmentLoadsByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("ShipmentLoadsByPeriod", erp, pdf);
    }

    [Fact]
    public async Task ShipmentLoadsByPeriod_EmptyResultStillProducesAPdf()
    {
        var db = TestDb.CreateUnitOfWork();

        var pdf = await new ShipmentLoadsByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new ShipmentLoadsByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Assert.NotEmpty(pdf);
    }

    // Valores grandes: exercitam a largura dos campos numéricos (ver LogisticsReportsLayoutTests).
    [Fact]
    public async Task ShipmentLoadsByPeriod_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        foreach (var code in new[] { "CG000001", "CG000002" })
        {
            var load = Load(code, total: 99_999_999.999m);
            load.InvoicedQuantity = 88_888_888.888m;
            load.ReturnedToWarehouseQuantity = 9_999_999.999m;
            load.TransshippedQuantity = 1_111_111.112m;
            load.DischargedQuantity = 99_999_999.999m;
            load.FreightPrice = 9_999_999.99m;
            db.Context.ShipmentLoads.Add(load);
            db.Context.SalesInvoices.Add(LoadInvoice(load, code, "C001", "COOPERATIVA AGROINDUSTRIAL DOS PRODUTORES DO NORTE"));
        }
        await Save(db);

        var pdf = await new ShipmentLoadsByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new ShipmentLoadsByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("ShipmentLoadsByPeriod-large", "STANDALONE", pdf);
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

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }

    private static void Keep(string report, string erp, byte[] pdf)
    {
        Assert.NotEmpty(pdf);
        var folder = Path.Combine(Path.GetTempPath(), "siagro-logistics-reports");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, $"{report}-{erp}.pdf"), pdf);
    }
}
```

- [ ] **Step 8: Run all report tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"`
Expected: PASS — incluindo `ReportTemplateHeaderTests` e `ReportTemplateRenderSmokeTests`, que descobrem `ShipmentLoadsByPeriod.frx` sozinhos, e todos os do pacote 1.

- [ ] **Step 9: Conferência visual**

Abra com a ferramenta Read `%TEMP%/siagro-logistics-reports/ShipmentLoadsByPeriod-STANDALONE.pdf`, `-SAPB1.pdf` e `ShipmentLoadsByPeriod-large-STANDALONE.pdf`. Confira: cabeçalhos inteiros; números grandes sem "…"; "AGRO NORTE, COOPERATIVA CENTRAL" na CG000001; "FAZENDA BOA VISTA (planejado)" na CG000002; grupos MILHO/TN e SOJA/KG separados; no PDF normal (KG + TN) **sem** total geral de quantidade e com a nota "UMs diferentes…"; no `-large` (só KG) com o total geral e sem a nota. Se algo cortar, ajuste o `.frx` mantendo as regras do Appendix A e rode o Step 8 de novo.

- [ ] **Step 10: Build e commit**

Run: `dotnet build SiagroB1.sln` — Expected: 0 erros.

```bash
git branch --show-current   # feature/logistics-reports
git add SiagroB1.Reports/Dtos/ShipmentLoadsByPeriodRequest.cs SiagroB1.Reports/Dtos/ShipmentLoadRowDto.cs SiagroB1.Reports/Services/ShipmentLoadsByPeriodReportService.cs SiagroB1.Reports/Controllers/ShipmentLoadsByPeriodController.cs SiagroB1.Reports/Reports/Templates/ShipmentLoadsByPeriod.frx SiagroB1.Application.Tests/Reports/ShipmentLoadsByPeriodReportServiceTests.cs SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs
git commit -F - <<'EOF'
feat(reports): relatório de cargas por período

Cargas agrupadas por produto e UM, com total, faturado, devolvido,
transbordado, descarregado, saldo (o do domínio) e frete. O cliente sai
das notas vivas da carga; sem nota, o do planejamento com "(planejado)".

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
EOF
```

---

### Task 3: Liberações de Venda

**Files:**
- Create: `SiagroB1.Reports/Dtos/SalesShipmentReleasesByPeriodRequest.cs`, `SiagroB1.Reports/Dtos/SalesShipmentReleaseRowDto.cs`
- Create: `SiagroB1.Reports/Services/SalesShipmentReleasesByPeriodReportService.cs`
- Create: `SiagroB1.Reports/Controllers/SalesShipmentReleasesByPeriodController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/SalesShipmentReleasesByPeriod.frx`
- Test: `SiagroB1.Application.Tests/Reports/SalesShipmentReleasesByPeriodReportServiceTests.cs`
- Modify (test): `SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs`, `SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs`

**Interfaces:**
- Consumes: Task 1; helpers privados `FastReport`, `Configuration`, `Keep`, `Save` de `LogisticsReportsPdfTests` e `Templates`/`LongestText` de `LogisticsReportsLayoutTests` (Task 2).
- Produces: `SalesShipmentReleasesByPeriodReportService(IUnitOfWork, IFastReportService)` com `BuildRowsAsync`/`ExecuteAsync(SalesShipmentReleasesByPeriodRequest)`; `POST /reports/SalesShipmentReleasesByPeriod`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Reports/SalesShipmentReleasesByPeriodReportServiceTests.cs`:

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.LogisticsReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class SalesShipmentReleasesByPeriodReportServiceTests
{
    [Fact]
    public async Task BuildRows_IncludesTheWholeLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContract();
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, new DateTime(2026, 7, 31, 23, 0, 0)));
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, new DateTime(2026, 8, 1)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "31/07/2026" }, rows.Select(r => r.ReleaseDate));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContract();
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, status: ReleaseStatus.Actived, deliveryLocationCode: "L01"));
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, status: ReleaseStatus.Paused, deliveryLocationCode: "L02"));
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, status: ReleaseStatus.Cancelled, deliveryLocationCode: "L03"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "Ativo", "Pausado" }, rows.Select(r => r.Status));
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("ItemCode")]
    [InlineData("ContractCode")]
    [InlineData("CardCode")]
    [InlineData("AgentCode")]
    [InlineData("LogisticRegionCode")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        var match = SalesContract("CV000001");
        var other = SalesContract("CV000002", cardCode: "C999", itemCode: "20001", itemName: "MILHO",
            agentCode: 99, regionCode: "R99");
        db.Context.SalesContracts.AddRange(match, other);
        db.Context.SalesShipmentReleases.Add(SalesRelease(match));
        db.Context.SalesShipmentReleases.Add(SalesRelease(other, branchCode: "99"));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "ContractCode": request.ContractCode = "CV000001"; break;
            case "CardCode": request.CardCode = "C001"; break;
            case "AgentCode": request.AgentCode = 7; break;
            case "LogisticRegionCode": request.LogisticRegionCode = "R01"; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "CV000001" }, rows.Select(r => r.Contract));
    }

    [Fact]
    public async Task BuildRows_FormatsColumnsFromTheContract()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.LogisticRegions.Add(Region());
        var contract = SalesContract();
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, released: 1_000m, shipped: 400m));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("SOJA EM GRÃOS (10001) - KG", row.Group);
        Assert.Equal("15/07/2026", row.ReleaseDate);
        Assert.Equal("30/09/2026", row.DeliveryDeadline);
        Assert.Equal("CV000001", row.Contract);
        Assert.Equal("(C001) COOPERATIVA CENTRAL", row.Customer);
        Assert.Equal("JOÃO VENDEDOR", row.Agent);
        Assert.Equal("PORTO DE PARANAGUÁ", row.DeliveryLocation);
        Assert.Equal("NORTE", row.Region);
        Assert.Equal("Ativo", row.Status);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(1_000m, row.ReleasedQuantity);
        Assert.Equal(400m, row.ConsumedQuantity);
        Assert.Equal(600m, row.BalanceQuantity);
    }

    [Fact]
    public async Task BuildRows_FallsBackToCodesWhenNamesAreMissing()
    {
        var db = TestDb.CreateUnitOfWork();
        var noAgentName = SalesContract("CV000001", agentName: null);
        var noAgent = SalesContract("CV000002", agentCode: null, agentName: null);
        db.Context.SalesContracts.AddRange(noAgentName, noAgent);
        db.Context.SalesShipmentReleases.Add(SalesRelease(noAgentName, deliveryLocationName: null));
        db.Context.SalesShipmentReleases.Add(SalesRelease(noAgent));
        await Save(db); // sem LogisticRegion "R01" na tabela: a região cai para o código

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "7", "" }, rows.Select(r => r.Agent));
        Assert.Equal(new[] { "L01", "PORTO DE PARANAGUÁ" }, rows.Select(r => r.DeliveryLocation));
        Assert.All(rows, r => Assert.Equal("R01", r.Region));
    }

    // Review Focus 3: saldo pela regra do domínio (cancelada = 0; sem clamp de negativo).
    [Fact]
    public async Task BuildRows_BalanceFollowsTheDomainRule()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContract();
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, released: 1_000m, shipped: 400m,
            status: ReleaseStatus.Cancelled, deliveryLocationCode: "L01"));
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, released: 1_000m, shipped: 0m,
            status: ReleaseStatus.Actived, deliveryLocationCode: "L02"));
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, released: 1_000m, shipped: 1_200m,
            status: ReleaseStatus.Completed, deliveryLocationCode: "L03"));
        await Save(db);

        var request = Request();
        request.Statuses = [ReleaseStatus.Cancelled, ReleaseStatus.Actived, ReleaseStatus.Completed];
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { 400m, 0m, 1_200m }, rows.Select(r => r.ConsumedQuantity));
        Assert.Equal(new[] { 0m, 1_000m, -200m }, rows.Select(r => r.BalanceQuantity));
    }

    // Review Focus 5.
    [Fact]
    public async Task BuildRows_GroupsByProductAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        var sojaKg = SalesContract("CV000001");
        var sojaTn = SalesContract("CV000002", uom: "TN");
        var milho = SalesContract("CV000003", itemCode: "20001", itemName: "MILHO");
        db.Context.SalesContracts.AddRange(sojaKg, sojaTn, milho);
        db.Context.SalesShipmentReleases.Add(SalesRelease(sojaKg));
        db.Context.SalesShipmentReleases.Add(SalesRelease(sojaTn));
        db.Context.SalesShipmentReleases.Add(SalesRelease(milho));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { "MILHO (20001) - KG", "SOJA EM GRÃOS (10001) - KG", "SOJA EM GRÃOS (10001) - TN" },
            rows.Select(r => r.Group));
        Assert.Equal(new[] { "CV000003", "CV000001", "CV000002" }, rows.Select(r => r.Contract));
    }

    [Fact]
    public async Task BuildRows_OrdersByDateThenContractInsideTheGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        var a = SalesContract("CV000001");
        var b = SalesContract("CV000002");
        db.Context.SalesContracts.AddRange(a, b);
        db.Context.SalesShipmentReleases.Add(SalesRelease(b, new DateTime(2026, 7, 10)));
        db.Context.SalesShipmentReleases.Add(SalesRelease(a, new DateTime(2026, 7, 10)));
        db.Context.SalesShipmentReleases.Add(SalesRelease(b, new DateTime(2026, 7, 5)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "05/07/2026", "10/07/2026", "10/07/2026" }, rows.Select(r => r.ReleaseDate));
        Assert.Equal(new[] { "CV000002", "CV000001", "CV000002" }, rows.Select(r => r.Contract));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_FlagsWhetherTheResultHasASingleUnit(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        var kg = SalesContract("CV000001");
        db.Context.SalesContracts.Add(kg);
        db.Context.SalesShipmentReleases.Add(SalesRelease(kg));
        if (withTonnes)
        {
            var tn = SalesContract("CV000002", uom: "TN");
            db.Context.SalesContracts.Add(tn);
            db.Context.SalesShipmentReleases.Add(SalesRelease(tn));
        }
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new SalesShipmentReleasesByPeriodReportService(db, recorder).ExecuteAsync(Request());

        Assert.Equal("SalesShipmentReleasesByPeriod.frx", recorder.LastReportName);
        Assert.Equal(singleUom, recorder.LastParameters![FastReportService.SingleUomParameter]);
    }

    [Fact]
    public async Task Execute_DescribesEveryFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        db.Context.LogisticRegions.Add(Region());
        var contract = SalesContract();
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract));
        await Save(db);
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.BranchCode = "01";
        request.ItemCode = "10001";
        request.ContractCode = "CV000001";
        request.CardCode = "C001";
        request.AgentCode = 7;
        request.LogisticRegionCode = "R01";
        await new SalesShipmentReleasesByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data da liberação: 01/07/2026 a 31/07/2026 | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | " +
            "Situação: todas, exceto Cancelado | Contrato: CV000001 | Cliente: (C001) COOPERATIVA CENTRAL | " +
            "Vendedor: JOÃO VENDEDOR | Região: NORTE",
            recorder.LastParameters!["pFilters"]);
    }

    [Fact]
    public async Task Execute_WithoutResultFallsBackToCodes()
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.CardCode = "C001";
        request.AgentCode = 7;
        request.LogisticRegionCode = "R01";
        request.Statuses = [ReleaseStatus.Cancelled];
        await new SalesShipmentReleasesByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data da liberação: 01/07/2026 a 31/07/2026 | Situação: Cancelado | Cliente: C001 | Vendedor: 7 | Região: R01",
            recorder.LastParameters!["pFilters"]);
    }

    private static SalesShipmentReleasesByPeriodReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService());

    private static SalesShipmentReleasesByPeriodRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesShipmentReleasesByPeriodReportServiceTests"`
Expected: build FAIL — tipos inexistentes.

- [ ] **Step 3: Implement DTOs, service and controller**

`SiagroB1.Reports/Dtos/SalesShipmentReleasesByPeriodRequest.cs` (criar e `git add`):

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros de "Liberações de Venda" (período sobre <c>ReleaseDate</c>). Situação vazia = todas
/// menos Cancelado. Contrato, cliente, vendedor, produto e região são do contrato de venda.
/// </summary>
public class SalesShipmentReleasesByPeriodRequest : LogisticsReportRequest
{
    public List<ReleaseStatus>? Statuses { get; set; }

    /// <summary>Código do contrato de venda (SalesContract.Code).</summary>
    public string? ContractCode { get; set; }

    public string? CardCode { get; set; }

    public int? AgentCode { get; set; }

    public string? LogisticRegionCode { get; set; }
}
```

`SiagroB1.Reports/Dtos/SalesShipmentReleaseRowDto.cs` (criar e `git add`):

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>Linha de "Liberações de Venda". <see cref="Group"/> = produto + UM do contrato.</summary>
public class SalesShipmentReleaseRowDto
{
    public string Group { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public string DeliveryDeadline { get; set; } = "";
    public string Contract { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Agent { get; set; } = "";
    public string DeliveryLocation { get; set; } = "";
    public string Region { get; set; } = "";
    public string Status { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal ReleasedQuantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal BalanceQuantity { get; set; }
}
```

`SiagroB1.Reports/Services/SalesShipmentReleasesByPeriodReportService.cs` (criar e `git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Liberações de entrega de contratos de VENDA, agrupadas pelo produto + UM do contrato. A
/// liberação não tem código próprio: a linha mostra o do contrato. Cliente, vendedor, produto e
/// região são snapshots do SALES_CONTRACTS (tabela local, FK obrigatória); região por LEFT JOIN em
/// LOGISTIC_REGIONS. Consumido = ShippedQuantity; saldo = <see cref="SalesShipmentRelease.AvailableQuantity"/>.
/// </summary>
public class SalesShipmentReleasesByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    public async Task<List<SalesShipmentReleaseRowDto>> BuildRowsAsync(SalesShipmentReleasesByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = LogisticsReportText.EffectiveReleaseStatuses(request.Statuses);

        var query = db.Context.SalesShipmentReleases
            .AsNoTracking()
            .Include(r => r.SalesContract).ThenInclude(c => c!.LogisticRegion)
            .Where(r => r.ReleaseDate >= from && r.ReleaseDate < toExclusive)
            .Where(r => statuses.Contains(r.Status));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(r => r.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(r => r.SalesContract!.ItemCode == request.ItemCode);

        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            query = query.Where(r => r.SalesContract!.Code == request.ContractCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(r => r.SalesContract!.CardCode == request.CardCode);

        if (request.AgentCode is { } agent)
            query = query.Where(r => r.SalesContract!.AgentCode == agent);

        if (!string.IsNullOrWhiteSpace(request.LogisticRegionCode))
            query = query.Where(r => r.SalesContract!.LogisticRegionCode == request.LogisticRegionCode);

        var releases = (await query.ToListAsync()).Where(r => r.SalesContract is not null).ToList();

        var groupOf = InvoiceItemGrouping.BuildGroupResolver(
            releases, r => r.SalesContract!.ItemCode, r => r.SalesContract!.ItemName, r => r.SalesContract!.UnitOfMeasureCode);

        return releases
            .OrderBy(r => groupOf(r), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.ReleaseDate)
            .ThenBy(r => r.SalesContract!.Code ?? "", StringComparer.Ordinal)
            .ThenBy(r => r.DeliveryLocationCode, StringComparer.Ordinal)
            .Select(r => ToRow(r, groupOf(r)))
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(SalesShipmentReleasesByPeriodRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var first = rows.Count > 0 ? rows[0] : null;
        var statuses = LogisticsReportText.EffectiveReleaseStatuses(request.Statuses);

        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            extra.Add($"Contrato: {request.ContractCode}");
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            extra.Add($"Cliente: {ReportText.Describe(first?.Customer, request.CardCode)}");
        if (request.AgentCode is { } agent)
            extra.Add($"Vendedor: {ReportText.Describe(first?.Agent, agent.ToString())}");
        if (!string.IsNullOrWhiteSpace(request.LogisticRegionCode))
            extra.Add($"Região: {ReportText.Describe(first?.Region, request.LogisticRegionCode)}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = LogisticsReportText.BuildFilters(
                "Data da liberação",
                request,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                ReportText.ProductOfGroup(first?.Group),
                LogisticsReportText.StatusFilter(statuses, LogisticsReportText.ReleaseStatusText, ReleaseStatus.Cancelled),
                extra),
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "SalesShipmentReleasesByPeriod.frx", rows, "SalesShipmentReleases", "SalesShipmentReleases", parameters);
    }

    private static SalesShipmentReleaseRowDto ToRow(SalesShipmentRelease r, string group)
    {
        var contract = r.SalesContract!;

        return new SalesShipmentReleaseRowDto
        {
            Group = group,
            ReleaseDate = ReportText.Date(r.ReleaseDate),
            DeliveryDeadline = ReportText.Date(contract.DeliveryEndDate),
            Contract = contract.Code ?? "",
            Customer = ReportText.Partner(contract.CardCode, contract.CardName),
            Agent = ReportText.NameOrCode(contract.AgentCode?.ToString(), contract.AgentName),
            DeliveryLocation = ReportText.NameOrCode(r.DeliveryLocationCode, r.DeliveryLocationName),
            Region = ReportText.NameOrCode(contract.LogisticRegionCode, contract.LogisticRegion?.Name),
            Status = LogisticsReportText.ReleaseStatusText(r.Status),
            UnitOfMeasure = contract.UnitOfMeasureCode,
            ReleasedQuantity = r.ReleasedQuantity,
            ConsumedQuantity = r.ShippedQuantity,
            BalanceQuantity = r.AvailableQuantity,
        };
    }
}
```

`SiagroB1.Reports/Controllers/SalesShipmentReleasesByPeriodController.cs` (criar e `git add`):

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/SalesShipmentReleasesByPeriod")]
public class SalesShipmentReleasesByPeriodController(SalesShipmentReleasesByPeriodReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] SalesShipmentReleasesByPeriodRequest request)
    {
        if (LogisticsReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"sales-shipment-releases-by-period.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesShipmentReleasesByPeriodReportServiceTests"`
Expected: PASS.

- [ ] **Step 5: Template**

Crie `SiagroB1.Reports/Reports/Templates/SalesShipmentReleasesByPeriod.frx` exatamente com o conteúdo abaixo (e `git add`). Colunas:

| Coluna (cabeçalho) | Campo | Left | Largura | Formato | Objeto (cabeçalho / dado) |
|---|---|---|---|---|---|
| Data | ReleaseDate | 0 | 62 | texto | `hdrReleaseDate` / `txtReleaseDate` |
| Entrega até | DeliveryDeadline | 62 | 66 | texto | `hdrDeliveryDeadline` / `txtDeliveryDeadline` |
| Contrato | Contract | 128 | 72 | texto | `hdrContract` / `txtContract` |
| Cliente | Customer | 200 | 200 | texto | `hdrCustomer` / `txtCustomer` |
| Vendedor | Agent | 400 | 100 | texto | `hdrAgent` / `txtAgent` |
| Local de entrega | DeliveryLocation | 500 | 166 | texto | `hdrDeliveryLocation` / `txtDeliveryLocation` |
| Região | Region | 666 | 110 | texto | `hdrRegion` / `txtRegion` |
| Situação | Status | 776 | 62 | texto | `hdrStatus` / `txtStatus` |
| Liberado | ReleasedQuantity | 838 | 82 | Number, 3 casas | `hdrReleasedQuantity` / `txtReleasedQuantity` |
| Consumido | ConsumedQuantity | 920 | 82 | Number, 3 casas | `hdrConsumedQuantity` / `txtConsumedQuantity` |
| Saldo | BalanceQuantity | 1002 | 82 | Number, 3 casas | `hdrBalanceQuantity` / `txtBalanceQuantity` |
| **Soma** | | | **1084** | | |

O cabeçalho é "Entrega até" (e não "Limite entrega", 14 caracteres = 80,8 px) para caber em 66 px.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Report ScriptLanguage="CSharp" ReportInfo.Created="10/08/2026 10:00:00" ReportInfo.Modified="10/08/2026 10:00:00" ReportInfo.CreatorVersion="2026.1.0.0">
  <Styles>
    <Style Name="EvenRows" Fill.Color="Gainsboro" Font="Arial, 10pt"/>
  </Styles>
  <Dictionary>
    <BusinessObjectDataSource Name="SalesShipmentReleases" ReferenceName="SalesShipmentReleases" DataType="System.Int32" Enabled="true">
      <Column Name="Group" DataType="System.String"/>
      <Column Name="ReleaseDate" DataType="System.String"/>
      <Column Name="DeliveryDeadline" DataType="System.String"/>
      <Column Name="Contract" DataType="System.String"/>
      <Column Name="Customer" DataType="System.String"/>
      <Column Name="Agent" DataType="System.String"/>
      <Column Name="DeliveryLocation" DataType="System.String"/>
      <Column Name="Region" DataType="System.String"/>
      <Column Name="Status" DataType="System.String"/>
      <Column Name="ReleasedQuantity" DataType="System.Decimal"/>
      <Column Name="ConsumedQuantity" DataType="System.Decimal"/>
      <Column Name="BalanceQuantity" DataType="System.Decimal"/>
      <Column Name="UnitOfMeasure" DataType="System.String"/>
    </BusinessObjectDataSource>
    <Parameter Name="pCompanyName" DataType="System.String" AsString=""/>
    <Parameter Name="pFilters" DataType="System.String" AsString=""/>
    <Parameter Name="pSingleUom" DataType="System.Boolean" AsString="true"/>
    <Total Name="TotalGrpReleasedQuantity" Expression="[SalesShipmentReleases.ReleasedQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpConsumedQuantity" Expression="[SalesShipmentReleases.ConsumedQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpBalanceQuantity" Expression="[SalesShipmentReleases.BalanceQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalSumReleasedQuantity" Expression="[SalesShipmentReleases.ReleasedQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumConsumedQuantity" Expression="[SalesShipmentReleases.ConsumedQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumBalanceQuantity" Expression="[SalesShipmentReleases.BalanceQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalCountAll" TotalType="Count" Evaluator="Data1" PrintOn="ReportSummary1"/>
  </Dictionary>
  <ReportPage Name="Page1" Landscape="true" PaperWidth="297" PaperHeight="210" LeftMargin="5" TopMargin="5" RightMargin="5" BottomMargin="5" RawPaperSize="9" Watermark.Font="Arial, 60pt">
    <PageHeaderBand Name="PageHeader1" Width="1084" Height="113.4">
      <PictureObject Name="picLogo" Left="0" Top="2" Width="94.5" Height="37.8" SizeMode="Zoom"/>
      <TextObject Name="txtCompany" Left="100" Top="2" Width="700" Height="37.8" Text="[pCompanyName]" VertAlign="Center" Font="Arial, 11pt, style=Bold"/>
      <TextObject Name="txtDate" Left="937" Top="2" Width="147" Height="18.9" Text="[Date]" Format="Date" Format.Format="d" HorzAlign="Right" VertAlign="Center" Font="Tahoma, 6pt"/>
      <TextObject Name="txtTitle" Top="45" Width="1084" Height="28.35" Text="Liberações de Venda" HorzAlign="Center" VertAlign="Center" Font="Arial, 14pt, style=Bold, Italic"/>
      <TextObject Name="txtFilters" Top="75" Width="1084" Height="37.8" Text="[pFilters]" HorzAlign="Center" VertAlign="Top" WordWrap="true" Font="Consolas, 8pt, style=Italic"/>
    </PageHeaderBand>
    <ColumnHeaderBand Name="ColumnHeader1" Top="116.6" Width="1084" Height="17">
      <TextObject Name="hdrReleaseDate" Left="0" Width="62" Height="17" Text="Data" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrDeliveryDeadline" Left="62" Width="66" Height="17" Text="Entrega até" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrContract" Left="128" Width="72" Height="17" Text="Contrato" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrCustomer" Left="200" Width="200" Height="17" Text="Cliente" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrAgent" Left="400" Width="100" Height="17" Text="Vendedor" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrDeliveryLocation" Left="500" Width="166" Height="17" Text="Local de entrega" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrRegion" Left="666" Width="110" Height="17" Text="Região" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrStatus" Left="776" Width="62" Height="17" Text="Situação" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrReleasedQuantity" Left="838" Width="82" Height="17" Text="Liberado" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrConsumedQuantity" Left="920" Width="82" Height="17" Text="Consumido" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrBalanceQuantity" Left="1002" Width="82" Height="17" Text="Saldo" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
    </ColumnHeaderBand>
    <GroupHeaderBand Name="GroupHeader1" SortOrder="None" Top="136.7" Width="1084" Height="20" Condition="[SalesShipmentReleases.Group]">
      <TextObject Name="txtGroup" Left="0" Top="2" Width="700" Height="17" Text="[SalesShipmentReleases.Group]" VertAlign="Center" WordWrap="false" Font="Arial, 8pt, style=Bold"/>
      <DataBand Name="Data1" Top="159.75" Width="1084" Height="17" CanGrow="true" EvenStyle="EvenRows" DataSource="SalesShipmentReleases">
        <TextObject Name="txtReleaseDate" Left="0" Width="62" Height="17" Text="[SalesShipmentReleases.ReleaseDate]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtDeliveryDeadline" Left="62" Width="66" Height="17" Text="[SalesShipmentReleases.DeliveryDeadline]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtContract" Left="128" Width="72" Height="17" Text="[SalesShipmentReleases.Contract]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtCustomer" Left="200" Width="200" Height="17" Text="[SalesShipmentReleases.Customer]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtAgent" Left="400" Width="100" Height="17" Text="[SalesShipmentReleases.Agent]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtDeliveryLocation" Left="500" Width="166" Height="17" Text="[SalesShipmentReleases.DeliveryLocation]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtRegion" Left="666" Width="110" Height="17" Text="[SalesShipmentReleases.Region]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtStatus" Left="776" Width="62" Height="17" Text="[SalesShipmentReleases.Status]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtReleasedQuantity" Left="838" Width="82" Height="17" Text="[SalesShipmentReleases.ReleasedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtConsumedQuantity" Left="920" Width="82" Height="17" Text="[SalesShipmentReleases.ConsumedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtBalanceQuantity" Left="1002" Width="82" Height="17" Text="[SalesShipmentReleases.BalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
      </DataBand>
      <GroupFooterBand Name="GroupFooter1" Top="180" Width="1084" Height="20">
        <TextObject Name="txtGrpLabel" Left="0" Top="2" Width="838" Height="17" Text="Subtotal do produto/UM:" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpReleasedQuantity" Left="838" Top="2" Width="82" Height="17" Text="[TotalGrpReleasedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpConsumedQuantity" Left="920" Top="2" Width="82" Height="17" Text="[TotalGrpConsumedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpBalanceQuantity" Left="1002" Top="2" Width="82" Height="17" Text="[TotalGrpBalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
      </GroupFooterBand>
    </GroupHeaderBand>
    <ReportSummaryBand Name="ReportSummary1" Top="203" Width="1084" Height="48">
      <TextObject Name="txtSumLabel" Left="0" Top="4" Width="100" Height="17" Text="TOTAL GERAL:" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Bold"/>
      <TextObject Name="txtSumCount" Left="100" Top="4" Width="130" Height="17" Text="[TotalCountAll] liberação(ões)" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="uomMixedNote" Left="232" Top="4" Width="606" Height="17" Text="UMs diferentes: quantidades só nos subtotais." VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="uomSumReleasedQuantity" Left="838" Top="4" Width="82" Height="17" Text="[TotalSumReleasedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumConsumedQuantity" Left="920" Top="4" Width="82" Height="17" Text="[TotalSumConsumedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumBalanceQuantity" Left="1002" Top="4" Width="82" Height="17" Text="[TotalSumBalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="txtEmpty" Left="0" Top="26" Width="1084" Height="17" Text="[IIf([TotalCountAll] == 0, &quot;Nenhum registro encontrado.&quot;, &quot;&quot;)]" HorzAlign="Center" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Italic"/>
    </ReportSummaryBand>
    <PageFooterBand Name="PageFooter1" Top="254" Width="1084" Height="26">
      <TextObject Name="txtPage" Left="937" Width="147" Height="17" Text="Página [Page#] de [TotalPages#]" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
    </PageFooterBand>
  </ReportPage>
</Report>
```

- [ ] **Step 6: Layout and PDF tests**

Em `LogisticsReportsLayoutTests`, acrescente `"SalesShipmentReleasesByPeriod",` à lista `Templates` e esta entrada ao dicionário `LongestText`:

```csharp
        ["SalesShipmentReleasesByPeriod"] = new()
        {
            ["txtReleaseDate"] = 10,      // 31/12/2026
            ["txtDeliveryDeadline"] = 10, // 31/12/2026
            ["txtContract"] = 12,         // CV2026000123
            ["txtStatus"] = 10,           // Finalizado
        },
```

Em `LogisticsReportsPdfTests`, acrescente:

```csharp
    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task SalesShipmentReleasesByPeriod_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.LogisticRegions.Add(Region());
        var soja = SalesContract("CV000001");
        var milho = SalesContract("CV000002", itemCode: "20001", itemName: "MILHO", agentName: null);
        db.Context.SalesContracts.AddRange(soja, milho);
        db.Context.SalesShipmentReleases.Add(SalesRelease(soja, released: 1_000m, shipped: 400m));
        db.Context.SalesShipmentReleases.Add(SalesRelease(soja, released: 1_000m, shipped: 1_200m, status: ReleaseStatus.Completed));
        db.Context.SalesShipmentReleases.Add(SalesRelease(milho, deliveryLocationName: null));
        await Save(db);

        var pdf = await new SalesShipmentReleasesByPeriodReportService(db, FastReport(Configuration(erp)))
            .ExecuteAsync(new SalesShipmentReleasesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesShipmentReleasesByPeriod", erp, pdf);
    }

    [Fact]
    public async Task SalesShipmentReleasesByPeriod_EmptyResultStillProducesAPdf()
    {
        var db = TestDb.CreateUnitOfWork();

        var pdf = await new SalesShipmentReleasesByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new SalesShipmentReleasesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Assert.NotEmpty(pdf);
    }

    [Fact]
    public async Task SalesShipmentReleasesByPeriod_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContract("CV2026000123",
            cardName: "COOPERATIVA AGROINDUSTRIAL DOS PRODUTORES DO NORTE DO PARANÁ");
        db.Context.SalesContracts.Add(contract);
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, released: 99_999_999.999m, shipped: 11_111_111.111m,
            deliveryLocationName: "TERMINAL PORTUÁRIO DE PARANAGUÁ - CORREDOR DE EXPORTAÇÃO"));
        db.Context.SalesShipmentReleases.Add(SalesRelease(contract, released: 99_999_999.999m, shipped: 11_111_111.111m));
        await Save(db);

        var pdf = await new SalesShipmentReleasesByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new SalesShipmentReleasesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesShipmentReleasesByPeriod-large", "STANDALONE", pdf);
    }
```

- [ ] **Step 7: Run report tests + visual check**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"`
Expected: PASS.

Abra `%TEMP%/siagro-logistics-reports/SalesShipmentReleasesByPeriod-STANDALONE.pdf` e `-large-STANDALONE.pdf`: "Entrega até" inteiro, saldo −200,000 na liberação acima do liberado, região "NORTE", local de entrega longo com "…", total geral presente (só KG) no `-large`; no normal (SOJA e MILHO, ambos KG) total geral presente e sem a nota.

- [ ] **Step 8: Commit**

```bash
git branch --show-current   # feature/logistics-reports
git add SiagroB1.Reports/Dtos/SalesShipmentReleasesByPeriodRequest.cs SiagroB1.Reports/Dtos/SalesShipmentReleaseRowDto.cs SiagroB1.Reports/Services/SalesShipmentReleasesByPeriodReportService.cs SiagroB1.Reports/Controllers/SalesShipmentReleasesByPeriodController.cs SiagroB1.Reports/Reports/Templates/SalesShipmentReleasesByPeriod.frx SiagroB1.Application.Tests/Reports/SalesShipmentReleasesByPeriodReportServiceTests.cs SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs
git commit -F - <<'EOF'
feat(reports): relatório de liberações de venda

Liberações de contratos de venda agrupadas pelo produto e UM do contrato,
com cliente, vendedor, local de entrega e região do contrato, e saldo pela
regra do domínio (cancelada não tem saldo; negativo não é escondido).

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
EOF
```

---

### Task 4: Liberações de Compra

**Files:**
- Create: `SiagroB1.Reports/Dtos/ShipmentReleasesByPeriodRequest.cs`, `SiagroB1.Reports/Dtos/ShipmentReleaseRowDto.cs`
- Create: `SiagroB1.Reports/Services/ShipmentReleasesByPeriodReportService.cs`
- Create: `SiagroB1.Reports/Controllers/ShipmentReleasesByPeriodController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/ShipmentReleasesByPeriod.frx`
- Test: `SiagroB1.Application.Tests/Reports/ShipmentReleasesByPeriodReportServiceTests.cs`
- Modify (test): `SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs`, `SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs`

**Interfaces:**
- Consumes: Task 1; helpers de `LogisticsReportsPdfTests` e `LogisticsReportsLayoutTests` (Task 2).
- Produces: `ShipmentReleasesByPeriodReportService(IUnitOfWork, IFastReportService)` com `BuildRowsAsync`/`ExecuteAsync(ShipmentReleasesByPeriodRequest)`; `POST /reports/ShipmentReleasesByPeriod`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Reports/ShipmentReleasesByPeriodReportServiceTests.cs`:

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.LogisticsReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class ShipmentReleasesByPeriodReportServiceTests
{
    [Fact]
    public async Task BuildRows_IncludesTheWholeLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = PurchaseContract();
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, new DateTime(2026, 7, 31, 23, 0, 0)));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, new DateTime(2026, 8, 1)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "31/07/2026" }, rows.Select(r => r.ReleaseDate));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = PurchaseContract();
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, status: ReleaseStatus.Pending, deliveryLocationCode: "ARM01"));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, status: ReleaseStatus.Completed, deliveryLocationCode: "ARM02"));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, status: ReleaseStatus.Cancelled, deliveryLocationCode: "ARM03"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "Pendente", "Finalizado" }, rows.Select(r => r.Status));
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("ItemCode")]
    [InlineData("ContractCode")]
    [InlineData("CardCode")]
    [InlineData("DeliveryLocationCode")]
    [InlineData("Origin")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        var match = PurchaseContract("PC000001");
        var other = PurchaseContract("PC000002", cardCode: "F999", itemCode: "20001", itemName: "MILHO");
        db.Context.PurchaseContracts.AddRange(match, other);
        db.Context.ShipmentReleases.Add(PurchaseRelease(match));
        db.Context.ShipmentReleases.Add(PurchaseRelease(other, origin: ReleaseOrigin.Transshipment,
            deliveryLocationCode: "ARM99", branchCode: "99"));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "ContractCode": request.ContractCode = "PC000001"; break;
            case "CardCode": request.CardCode = "F001"; break;
            case "DeliveryLocationCode": request.DeliveryLocationCode = "ARM01"; break;
            case "Origin": request.Origin = ReleaseOrigin.Standard; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "PC000001" }, rows.Select(r => r.Contract));
    }

    [Fact]
    public async Task BuildRows_FormatsColumnsFromTheContractAndTheRelease()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = PurchaseContract();
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, released: 1_000m, shipped: 250m));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("SOJA EM GRÃOS (10001) - KG", row.Group);
        Assert.Equal("15/07/2026", row.ReleaseDate);
        Assert.Equal("PC000001", row.Contract);
        Assert.Equal("(F001) PRODUTOR RURAL", row.Supplier);
        Assert.Equal("ARMAZÉM CENTRAL", row.DeliveryLocation);
        Assert.Equal("Compra", row.Origin);
        Assert.Equal("Ativo", row.Status);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(1_000m, row.ReleasedQuantity);
        Assert.Equal(250m, row.WithdrawnQuantity);
        Assert.Equal(750m, row.BalanceQuantity);
    }

    [Fact]
    public async Task BuildRows_ShowsTheOriginAndFallsBackToTheWarehouseCode()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = PurchaseContract();
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, origin: ReleaseOrigin.SalesReturn,
            deliveryLocationCode: "ARM01"));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, origin: ReleaseOrigin.Transshipment,
            deliveryLocationCode: "ARM02", deliveryLocationName: null));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "Devolução", "Transbordo" }, rows.Select(r => r.Origin));
        Assert.Equal(new[] { "ARMAZÉM CENTRAL", "ARM02" }, rows.Select(r => r.DeliveryLocation));
    }

    // Review Focus 3: saldo pela regra do domínio (cancelada = 0; sem clamp de negativo).
    [Fact]
    public async Task BuildRows_BalanceFollowsTheDomainRule()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = PurchaseContract();
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, released: 1_000m, shipped: 400m,
            status: ReleaseStatus.Cancelled, deliveryLocationCode: "ARM01"));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, released: 1_000m, shipped: 0m,
            status: ReleaseStatus.Actived, deliveryLocationCode: "ARM02"));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, released: 1_000m, shipped: 1_200m,
            status: ReleaseStatus.Completed, deliveryLocationCode: "ARM03"));
        await Save(db);

        var request = Request();
        request.Statuses = [ReleaseStatus.Cancelled, ReleaseStatus.Actived, ReleaseStatus.Completed];
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { 400m, 0m, 1_200m }, rows.Select(r => r.WithdrawnQuantity));
        Assert.Equal(new[] { 0m, 1_000m, -200m }, rows.Select(r => r.BalanceQuantity));
    }

    // Review Focus 5.
    [Fact]
    public async Task BuildRows_GroupsByProductAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        var sojaKg = PurchaseContract("PC000001");
        var sojaTn = PurchaseContract("PC000002", uom: "TN");
        var milho = PurchaseContract("PC000003", itemCode: "20001", itemName: "MILHO");
        db.Context.PurchaseContracts.AddRange(sojaKg, sojaTn, milho);
        db.Context.ShipmentReleases.Add(PurchaseRelease(sojaKg));
        db.Context.ShipmentReleases.Add(PurchaseRelease(sojaTn));
        db.Context.ShipmentReleases.Add(PurchaseRelease(milho));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { "MILHO (20001) - KG", "SOJA EM GRÃOS (10001) - KG", "SOJA EM GRÃOS (10001) - TN" },
            rows.Select(r => r.Group));
        Assert.Equal(new[] { "PC000003", "PC000001", "PC000002" }, rows.Select(r => r.Contract));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_FlagsWhetherTheResultHasASingleUnit(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        var kg = PurchaseContract("PC000001");
        db.Context.PurchaseContracts.Add(kg);
        db.Context.ShipmentReleases.Add(PurchaseRelease(kg));
        if (withTonnes)
        {
            var tn = PurchaseContract("PC000002", uom: "TN");
            db.Context.PurchaseContracts.Add(tn);
            db.Context.ShipmentReleases.Add(PurchaseRelease(tn));
        }
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new ShipmentReleasesByPeriodReportService(db, recorder).ExecuteAsync(Request());

        Assert.Equal("ShipmentReleasesByPeriod.frx", recorder.LastReportName);
        Assert.Equal(singleUom, recorder.LastParameters![FastReportService.SingleUomParameter]);
    }

    [Fact]
    public async Task Execute_DescribesEveryFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        var contract = PurchaseContract();
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract));
        await Save(db);
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.BranchCode = "01";
        request.ItemCode = "10001";
        request.ContractCode = "PC000001";
        request.CardCode = "F001";
        request.DeliveryLocationCode = "ARM01";
        request.Origin = ReleaseOrigin.Standard;
        await new ShipmentReleasesByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data da liberação: 01/07/2026 a 31/07/2026 | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | " +
            "Situação: todas, exceto Cancelado | Contrato: PC000001 | Fornecedor: (F001) PRODUTOR RURAL | " +
            "Armazém de retirada: ARMAZÉM CENTRAL | Origem: Compra",
            recorder.LastParameters!["pFilters"]);
    }

    private static ShipmentReleasesByPeriodReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService());

    private static ShipmentReleasesByPeriodRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentReleasesByPeriodReportServiceTests"`
Expected: build FAIL — tipos inexistentes.

- [ ] **Step 3: Implement DTOs, service and controller**

`SiagroB1.Reports/Dtos/ShipmentReleasesByPeriodRequest.cs` (criar e `git add`):

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros de "Liberações de Compra" (período sobre <c>ReleaseDate</c>). Situação vazia = todas
/// menos Cancelado; Origem vazia = todas. Contrato, fornecedor e produto são do contrato de compra;
/// o armazém de retirada é o <c>DeliveryLocationCode</c> da própria liberação.
/// </summary>
public class ShipmentReleasesByPeriodRequest : LogisticsReportRequest
{
    public List<ReleaseStatus>? Statuses { get; set; }

    /// <summary>Código do contrato de compra (PurchaseContract.Code).</summary>
    public string? ContractCode { get; set; }

    public string? CardCode { get; set; }

    public string? DeliveryLocationCode { get; set; }

    public ReleaseOrigin? Origin { get; set; }
}
```

`SiagroB1.Reports/Dtos/ShipmentReleaseRowDto.cs` (criar e `git add`):

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>Linha de "Liberações de Compra". <see cref="Group"/> = produto + UM do contrato.</summary>
public class ShipmentReleaseRowDto
{
    public string Group { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public string Contract { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string DeliveryLocation { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Status { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal ReleasedQuantity { get; set; }
    public decimal WithdrawnQuantity { get; set; }
    public decimal BalanceQuantity { get; set; }
}
```

`SiagroB1.Reports/Services/ShipmentReleasesByPeriodReportService.cs` (criar e `git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Liberações de embarque de contratos de COMPRA, agrupadas pelo produto + UM do contrato.
/// Fornecedor e produto são snapshots de PURCHASE_CONTRACTS (tabela local); o armazém de retirada
/// é o snapshot DeliveryLocationCode/Name da liberação — nada de WAREHOUSES. Retirado =
/// ShippedQuantity; saldo = <see cref="ShipmentRelease.AvailableQuantity"/>.
/// </summary>
public class ShipmentReleasesByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    public async Task<List<ShipmentReleaseRowDto>> BuildRowsAsync(ShipmentReleasesByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = LogisticsReportText.EffectiveReleaseStatuses(request.Statuses);

        var query = db.Context.ShipmentReleases
            .AsNoTracking()
            .Include(r => r.PurchaseContract)
            .Where(r => r.ReleaseDate >= from && r.ReleaseDate < toExclusive)
            .Where(r => statuses.Contains(r.Status));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(r => r.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(r => r.PurchaseContract!.ItemCode == request.ItemCode);

        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            query = query.Where(r => r.PurchaseContract!.Code == request.ContractCode);

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(r => r.PurchaseContract!.CardCode == request.CardCode);

        if (!string.IsNullOrWhiteSpace(request.DeliveryLocationCode))
            query = query.Where(r => r.DeliveryLocationCode == request.DeliveryLocationCode);

        if (request.Origin is { } origin)
            query = query.Where(r => r.Origin == origin);

        var releases = (await query.ToListAsync()).Where(r => r.PurchaseContract is not null).ToList();

        var groupOf = InvoiceItemGrouping.BuildGroupResolver(
            releases, r => r.PurchaseContract!.ItemCode, r => r.PurchaseContract!.ItemName, r => r.PurchaseContract!.UnitOfMeasureCode);

        return releases
            .OrderBy(r => groupOf(r), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.ReleaseDate)
            .ThenBy(r => r.PurchaseContract!.Code ?? "", StringComparer.Ordinal)
            .ThenBy(r => r.DeliveryLocationCode, StringComparer.Ordinal)
            .Select(r => ToRow(r, groupOf(r)))
            .ToList();
    }

    public async Task<byte[]> ExecuteAsync(ShipmentReleasesByPeriodRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var first = rows.Count > 0 ? rows[0] : null;
        var statuses = LogisticsReportText.EffectiveReleaseStatuses(request.Statuses);

        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ContractCode))
            extra.Add($"Contrato: {request.ContractCode}");
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            extra.Add($"Fornecedor: {ReportText.Describe(first?.Supplier, request.CardCode)}");
        if (!string.IsNullOrWhiteSpace(request.DeliveryLocationCode))
            extra.Add($"Armazém de retirada: {ReportText.Describe(first?.DeliveryLocation, request.DeliveryLocationCode)}");
        if (request.Origin is { } origin)
            extra.Add($"Origem: {LogisticsReportText.OriginText(origin)}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = LogisticsReportText.BuildFilters(
                "Data da liberação",
                request,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                ReportText.ProductOfGroup(first?.Group),
                LogisticsReportText.StatusFilter(statuses, LogisticsReportText.ReleaseStatusText, ReleaseStatus.Cancelled),
                extra),
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "ShipmentReleasesByPeriod.frx", rows, "ShipmentReleases", "ShipmentReleases", parameters);
    }

    private static ShipmentReleaseRowDto ToRow(ShipmentRelease r, string group)
    {
        var contract = r.PurchaseContract!;

        return new ShipmentReleaseRowDto
        {
            Group = group,
            ReleaseDate = ReportText.Date(r.ReleaseDate),
            Contract = contract.Code ?? "",
            Supplier = ReportText.Partner(contract.CardCode, contract.CardName),
            DeliveryLocation = ReportText.NameOrCode(r.DeliveryLocationCode, r.DeliveryLocationName),
            Origin = LogisticsReportText.OriginText(r.Origin),
            Status = LogisticsReportText.ReleaseStatusText(r.Status),
            UnitOfMeasure = contract.UnitOfMeasureCode,
            ReleasedQuantity = r.ReleasedQuantity,
            WithdrawnQuantity = r.ShippedQuantity,
            BalanceQuantity = r.AvailableQuantity,
        };
    }
}
```

`SiagroB1.Reports/Controllers/ShipmentReleasesByPeriodController.cs` (criar e `git add`):

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/ShipmentReleasesByPeriod")]
public class ShipmentReleasesByPeriodController(ShipmentReleasesByPeriodReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] ShipmentReleasesByPeriodRequest request)
    {
        if (LogisticsReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"shipment-releases-by-period.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentReleasesByPeriodReportServiceTests"`
Expected: PASS.

- [ ] **Step 5: Template**

Crie `SiagroB1.Reports/Reports/Templates/ShipmentReleasesByPeriod.frx` exatamente com o conteúdo abaixo (e `git add`). Colunas:

| Coluna (cabeçalho) | Campo | Left | Largura | Formato | Objeto (cabeçalho / dado) |
|---|---|---|---|---|---|
| Data | ReleaseDate | 0 | 62 | texto | `hdrReleaseDate` / `txtReleaseDate` |
| Contrato | Contract | 62 | 72 | texto | `hdrContract` / `txtContract` |
| Fornecedor | Supplier | 134 | 290 | texto | `hdrSupplier` / `txtSupplier` |
| Armazém de retirada | DeliveryLocation | 424 | 274 | texto | `hdrDeliveryLocation` / `txtDeliveryLocation` |
| Origem | Origin | 698 | 78 | texto | `hdrOrigin` / `txtOrigin` |
| Situação | Status | 776 | 62 | texto | `hdrStatus` / `txtStatus` |
| Liberado | ReleasedQuantity | 838 | 82 | Number, 3 casas | `hdrReleasedQuantity` / `txtReleasedQuantity` |
| Retirado | WithdrawnQuantity | 920 | 82 | Number, 3 casas | `hdrWithdrawnQuantity` / `txtWithdrawnQuantity` |
| Saldo | BalanceQuantity | 1002 | 82 | Number, 3 casas | `hdrBalanceQuantity` / `txtBalanceQuantity` |
| **Soma** | | | **1084** | | |

```xml
<?xml version="1.0" encoding="utf-8"?>
<Report ScriptLanguage="CSharp" ReportInfo.Created="10/08/2026 10:00:00" ReportInfo.Modified="10/08/2026 10:00:00" ReportInfo.CreatorVersion="2026.1.0.0">
  <Styles>
    <Style Name="EvenRows" Fill.Color="Gainsboro" Font="Arial, 10pt"/>
  </Styles>
  <Dictionary>
    <BusinessObjectDataSource Name="ShipmentReleases" ReferenceName="ShipmentReleases" DataType="System.Int32" Enabled="true">
      <Column Name="Group" DataType="System.String"/>
      <Column Name="ReleaseDate" DataType="System.String"/>
      <Column Name="Contract" DataType="System.String"/>
      <Column Name="Supplier" DataType="System.String"/>
      <Column Name="DeliveryLocation" DataType="System.String"/>
      <Column Name="Origin" DataType="System.String"/>
      <Column Name="Status" DataType="System.String"/>
      <Column Name="ReleasedQuantity" DataType="System.Decimal"/>
      <Column Name="WithdrawnQuantity" DataType="System.Decimal"/>
      <Column Name="BalanceQuantity" DataType="System.Decimal"/>
      <Column Name="UnitOfMeasure" DataType="System.String"/>
    </BusinessObjectDataSource>
    <Parameter Name="pCompanyName" DataType="System.String" AsString=""/>
    <Parameter Name="pFilters" DataType="System.String" AsString=""/>
    <Parameter Name="pSingleUom" DataType="System.Boolean" AsString="true"/>
    <Total Name="TotalGrpReleasedQuantity" Expression="[ShipmentReleases.ReleasedQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpWithdrawnQuantity" Expression="[ShipmentReleases.WithdrawnQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpBalanceQuantity" Expression="[ShipmentReleases.BalanceQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalSumReleasedQuantity" Expression="[ShipmentReleases.ReleasedQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumWithdrawnQuantity" Expression="[ShipmentReleases.WithdrawnQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumBalanceQuantity" Expression="[ShipmentReleases.BalanceQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalCountAll" TotalType="Count" Evaluator="Data1" PrintOn="ReportSummary1"/>
  </Dictionary>
  <ReportPage Name="Page1" Landscape="true" PaperWidth="297" PaperHeight="210" LeftMargin="5" TopMargin="5" RightMargin="5" BottomMargin="5" RawPaperSize="9" Watermark.Font="Arial, 60pt">
    <PageHeaderBand Name="PageHeader1" Width="1084" Height="113.4">
      <PictureObject Name="picLogo" Left="0" Top="2" Width="94.5" Height="37.8" SizeMode="Zoom"/>
      <TextObject Name="txtCompany" Left="100" Top="2" Width="700" Height="37.8" Text="[pCompanyName]" VertAlign="Center" Font="Arial, 11pt, style=Bold"/>
      <TextObject Name="txtDate" Left="937" Top="2" Width="147" Height="18.9" Text="[Date]" Format="Date" Format.Format="d" HorzAlign="Right" VertAlign="Center" Font="Tahoma, 6pt"/>
      <TextObject Name="txtTitle" Top="45" Width="1084" Height="28.35" Text="Liberações de Compra" HorzAlign="Center" VertAlign="Center" Font="Arial, 14pt, style=Bold, Italic"/>
      <TextObject Name="txtFilters" Top="75" Width="1084" Height="37.8" Text="[pFilters]" HorzAlign="Center" VertAlign="Top" WordWrap="true" Font="Consolas, 8pt, style=Italic"/>
    </PageHeaderBand>
    <ColumnHeaderBand Name="ColumnHeader1" Top="116.6" Width="1084" Height="17">
      <TextObject Name="hdrReleaseDate" Left="0" Width="62" Height="17" Text="Data" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrContract" Left="62" Width="72" Height="17" Text="Contrato" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrSupplier" Left="134" Width="290" Height="17" Text="Fornecedor" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrDeliveryLocation" Left="424" Width="274" Height="17" Text="Armazém de retirada" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrOrigin" Left="698" Width="78" Height="17" Text="Origem" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrStatus" Left="776" Width="62" Height="17" Text="Situação" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrReleasedQuantity" Left="838" Width="82" Height="17" Text="Liberado" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrWithdrawnQuantity" Left="920" Width="82" Height="17" Text="Retirado" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrBalanceQuantity" Left="1002" Width="82" Height="17" Text="Saldo" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
    </ColumnHeaderBand>
    <GroupHeaderBand Name="GroupHeader1" SortOrder="None" Top="136.7" Width="1084" Height="20" Condition="[ShipmentReleases.Group]">
      <TextObject Name="txtGroup" Left="0" Top="2" Width="700" Height="17" Text="[ShipmentReleases.Group]" VertAlign="Center" WordWrap="false" Font="Arial, 8pt, style=Bold"/>
      <DataBand Name="Data1" Top="159.75" Width="1084" Height="17" CanGrow="true" EvenStyle="EvenRows" DataSource="ShipmentReleases">
        <TextObject Name="txtReleaseDate" Left="0" Width="62" Height="17" Text="[ShipmentReleases.ReleaseDate]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtContract" Left="62" Width="72" Height="17" Text="[ShipmentReleases.Contract]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtSupplier" Left="134" Width="290" Height="17" Text="[ShipmentReleases.Supplier]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtDeliveryLocation" Left="424" Width="274" Height="17" Text="[ShipmentReleases.DeliveryLocation]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtOrigin" Left="698" Width="78" Height="17" Text="[ShipmentReleases.Origin]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtStatus" Left="776" Width="62" Height="17" Text="[ShipmentReleases.Status]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtReleasedQuantity" Left="838" Width="82" Height="17" Text="[ShipmentReleases.ReleasedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtWithdrawnQuantity" Left="920" Width="82" Height="17" Text="[ShipmentReleases.WithdrawnQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtBalanceQuantity" Left="1002" Width="82" Height="17" Text="[ShipmentReleases.BalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
      </DataBand>
      <GroupFooterBand Name="GroupFooter1" Top="180" Width="1084" Height="20">
        <TextObject Name="txtGrpLabel" Left="0" Top="2" Width="838" Height="17" Text="Subtotal do produto/UM:" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpReleasedQuantity" Left="838" Top="2" Width="82" Height="17" Text="[TotalGrpReleasedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpWithdrawnQuantity" Left="920" Top="2" Width="82" Height="17" Text="[TotalGrpWithdrawnQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpBalanceQuantity" Left="1002" Top="2" Width="82" Height="17" Text="[TotalGrpBalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
      </GroupFooterBand>
    </GroupHeaderBand>
    <ReportSummaryBand Name="ReportSummary1" Top="203" Width="1084" Height="48">
      <TextObject Name="txtSumLabel" Left="0" Top="4" Width="100" Height="17" Text="TOTAL GERAL:" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Bold"/>
      <TextObject Name="txtSumCount" Left="100" Top="4" Width="130" Height="17" Text="[TotalCountAll] liberação(ões)" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="uomMixedNote" Left="232" Top="4" Width="606" Height="17" Text="UMs diferentes: quantidades só nos subtotais." VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="uomSumReleasedQuantity" Left="838" Top="4" Width="82" Height="17" Text="[TotalSumReleasedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumWithdrawnQuantity" Left="920" Top="4" Width="82" Height="17" Text="[TotalSumWithdrawnQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumBalanceQuantity" Left="1002" Top="4" Width="82" Height="17" Text="[TotalSumBalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="txtEmpty" Left="0" Top="26" Width="1084" Height="17" Text="[IIf([TotalCountAll] == 0, &quot;Nenhum registro encontrado.&quot;, &quot;&quot;)]" HorzAlign="Center" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Italic"/>
    </ReportSummaryBand>
    <PageFooterBand Name="PageFooter1" Top="254" Width="1084" Height="26">
      <TextObject Name="txtPage" Left="937" Width="147" Height="17" Text="Página [Page#] de [TotalPages#]" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
    </PageFooterBand>
  </ReportPage>
</Report>
```

- [ ] **Step 6: Layout and PDF tests**

Em `LogisticsReportsLayoutTests`, acrescente `"ShipmentReleasesByPeriod",` à lista `Templates` e ao dicionário `LongestText`:

```csharp
        ["ShipmentReleasesByPeriod"] = new()
        {
            ["txtReleaseDate"] = 10, // 31/12/2026
            ["txtContract"] = 12,    // PC2026000123
            ["txtOrigin"] = 13,      // Transferência
            ["txtStatus"] = 10,      // Finalizado
        },
```

Em `LogisticsReportsPdfTests`, acrescente:

```csharp
    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task ShipmentReleasesByPeriod_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        var soja = PurchaseContract("PC000001");
        var sojaTn = PurchaseContract("PC000002", uom: "TN");
        db.Context.PurchaseContracts.AddRange(soja, sojaTn);
        db.Context.ShipmentReleases.Add(PurchaseRelease(soja, released: 1_000m, shipped: 250m));
        db.Context.ShipmentReleases.Add(PurchaseRelease(soja, origin: ReleaseOrigin.OwnershipTransfer, deliveryLocationCode: "ARM02"));
        db.Context.ShipmentReleases.Add(PurchaseRelease(sojaTn, released: 30m, origin: ReleaseOrigin.Transshipment));
        await Save(db);

        var pdf = await new ShipmentReleasesByPeriodReportService(db, FastReport(Configuration(erp)))
            .ExecuteAsync(new ShipmentReleasesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("ShipmentReleasesByPeriod", erp, pdf);
    }

    [Fact]
    public async Task ShipmentReleasesByPeriod_EmptyResultStillProducesAPdf()
    {
        var db = TestDb.CreateUnitOfWork();

        var pdf = await new ShipmentReleasesByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new ShipmentReleasesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Assert.NotEmpty(pdf);
    }

    [Fact]
    public async Task ShipmentReleasesByPeriod_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = PurchaseContract("PC2026000123", cardName: "AGROPECUÁRIA DOS PRODUTORES RURAIS DO VALE DO RIO GRANDE LTDA");
        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, released: 99_999_999.999m, shipped: 11_111_111.111m,
            origin: ReleaseOrigin.OwnershipTransfer,
            deliveryLocationName: "ARMAZÉM GERAL DE GRÃOS DA COOPERATIVA AGROINDUSTRIAL - UNIDADE 12"));
        db.Context.ShipmentReleases.Add(PurchaseRelease(contract, released: 99_999_999.999m, shipped: 11_111_111.111m));
        await Save(db);

        var pdf = await new ShipmentReleasesByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new ShipmentReleasesByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("ShipmentReleasesByPeriod-large", "STANDALONE", pdf);
    }
```

- [ ] **Step 7: Run report tests + visual check**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"`
Expected: PASS.

Abra `%TEMP%/siagro-logistics-reports/ShipmentReleasesByPeriod-STANDALONE.pdf` e `-large-STANDALONE.pdf`: origem "Transferência" inteira; grupos KG e TN separados e, no normal, sem total geral de quantidade e com a nota; no `-large`, total geral presente.

- [ ] **Step 8: Commit**

```bash
git branch --show-current   # feature/logistics-reports
git add SiagroB1.Reports/Dtos/ShipmentReleasesByPeriodRequest.cs SiagroB1.Reports/Dtos/ShipmentReleaseRowDto.cs SiagroB1.Reports/Services/ShipmentReleasesByPeriodReportService.cs SiagroB1.Reports/Controllers/ShipmentReleasesByPeriodController.cs SiagroB1.Reports/Reports/Templates/ShipmentReleasesByPeriod.frx SiagroB1.Application.Tests/Reports/ShipmentReleasesByPeriodReportServiceTests.cs SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs
git commit -F - <<'EOF'
feat(reports): relatório de liberações de compra

Liberações de contratos de compra agrupadas pelo produto e UM do contrato,
com fornecedor, armazém de retirada (snapshot da liberação), origem e
saldo pela regra do domínio.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
EOF
```

---

### Task 5: Romaneios de Venda

**Files:**
- Create: `SiagroB1.Reports/Dtos/SalesShipmentsByPeriodRequest.cs`, `SiagroB1.Reports/Dtos/SalesShipmentRowDto.cs`
- Create: `SiagroB1.Reports/Services/SalesShipmentsByPeriodReportService.cs`
- Create: `SiagroB1.Reports/Controllers/SalesShipmentsByPeriodController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/SalesShipmentsByPeriod.frx`
- Test: `SiagroB1.Application.Tests/Reports/SalesShipmentsByPeriodReportServiceTests.cs`
- Modify (test): `SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs`, `SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs`

**Interfaces:**
- Consumes: Task 1 (`LogisticsReportText.LoadCustomers/LoadHasCustomer/CustomerName`, seed `SalesShipment`, `Load`, `LoadInvoice`); helpers de teste da Task 2.
- Produces: `SalesShipmentsByPeriodReportService(IUnitOfWork, IFastReportService)` com `BuildRowsAsync`/`ExecuteAsync(SalesShipmentsByPeriodRequest)`; `POST /reports/SalesShipmentsByPeriod`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Reports/SalesShipmentsByPeriodReportServiceTests.cs`:

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.LogisticsReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class SalesShipmentsByPeriodReportServiceTests
{
    [Fact]
    public async Task BuildRows_TakesOnlySalesShipments()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000001"));
        var purchaseLeg = SalesShipment("RO000002");
        purchaseLeg.TransactionType = StorageTransactionType.Purchase;
        db.Context.StorageTransactions.Add(purchaseLeg);
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "RO000001" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_IncludesTheWholeLastDay()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", new DateTime(2026, 7, 31, 23, 0, 0)));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002", new DateTime(2026, 8, 1)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "RO000001" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_DefaultSkipsCancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", status: StorageTransactionsStatus.Confirmed));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002", status: StorageTransactionsStatus.Invoiced));
        db.Context.StorageTransactions.Add(SalesShipment("RO000003", status: StorageTransactionsStatus.Cancelled));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "RO000001", "RO000002" }, rows.Select(r => r.Code));
        Assert.Equal(new[] { "Confirmado", "Faturado" }, rows.Select(r => r.Status));
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("ItemCode")]
    [InlineData("WarehouseCode")]
    [InlineData("TruckCode")]
    public async Task BuildRows_EachOptionalFilterRestrictsTheResult(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000001"));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002", itemCode: "20001", itemName: "MILHO",
            branchCode: "99", warehouseCode: "ARM99", truckCode: "XYZ9Z99"));
        await Save(db);

        var request = Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "WarehouseCode": request.WarehouseCode = "ARM01"; break;
            case "TruckCode": request.TruckCode = "ABC1D23"; break;
        }

        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "RO000001" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_FormatsColumnsAndSumsTheDiscounts()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000051");
        db.Context.ShipmentLoads.Add(load);
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", load: load, gross: 30_000m,
            drying: 300m, cleaning: 150m, others: 50m));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(Request())).Single();

        Assert.Equal("SOJA EM GRÃOS (10001) - KG", row.Group);
        Assert.Equal("15/07/2026", row.TransactionDate);
        Assert.Equal("RO000001", row.Code);
        Assert.Equal("CG000051", row.Load);
        Assert.Equal("ABC1D23", row.Truck);
        Assert.Equal("(F001) PRODUTOR RURAL", row.Supplier);
        Assert.Equal("COOPERATIVA CENTRAL", row.Customers);
        Assert.Equal("ARMAZÉM CENTRAL", row.Warehouse);
        Assert.Equal("000123/1", row.InvoiceNumber);
        Assert.Equal("Confirmado", row.Status);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(30_000m, row.GrossWeight);
        Assert.Equal(500m, row.DiscountWeight);
        Assert.Equal(29_500m, row.NetWeight);
    }

    [Fact]
    public async Task BuildRows_SupplierInvoiceNeverLeavesALooseSlash()
    {
        var db = TestDb.CreateUnitOfWork();
        var noSeries = SalesShipment("RO000001");
        noSeries.InvoiceSerie = null;
        var noNumber = SalesShipment("RO000002");
        noNumber.InvoiceNumber = null;
        db.Context.StorageTransactions.AddRange(noSeries, noNumber);
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "000123", "" }, rows.Select(r => r.InvoiceNumber));
    }

    // Review Focus 4: romaneio sem carga não some e não inventa cliente.
    [Fact]
    public async Task BuildRows_ShipmentWithoutLoadHasNoLoadNorCustomer()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000051");
        db.Context.ShipmentLoads.Add(load);
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", load: load));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "CG000051", "" }, rows.Select(r => r.Load));
        Assert.Equal(new[] { "COOPERATIVA CENTRAL", "" }, rows.Select(r => r.Customers));
    }

    // Review Focus 4.
    [Theory]
    [InlineData(true, new[] { "RO000001" })]
    [InlineData(false, new[] { "RO000002" })]
    [InlineData(null, new[] { "RO000001", "RO000002" })]
    public async Task BuildRows_LoadLinkFilter(bool? hasLoad, string[] expected)
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000051");
        db.Context.ShipmentLoads.Add(load);
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", load: load));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002"));
        await Save(db);

        var request = Request();
        request.HasLoad = hasLoad;
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(expected, rows.Select(r => r.Code));
    }

    // Review Focus 4: o filtro de Cliente usa as notas da carga; sem carga, nunca casa.
    [Fact]
    public async Task BuildRows_CustomerFilterNeverMatchesAShipmentWithoutLoad()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoiced = Load("CG000051");
        var planned = Load("CG000052", plannedCardCode: "C001", plannedCardName: "COOPERATIVA CENTRAL");
        var other = Load("CG000053");
        db.Context.ShipmentLoads.AddRange(invoiced, planned, other);
        db.Context.SalesInvoices.Add(LoadInvoice(invoiced, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.SalesInvoices.Add(LoadInvoice(other, "000000102", "C002", "AGRO NORTE"));
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", load: invoiced));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002", load: planned));
        db.Context.StorageTransactions.Add(SalesShipment("RO000003", load: other));
        db.Context.StorageTransactions.Add(SalesShipment("RO000004"));
        await Save(db);

        var request = Request();
        request.CardCode = "C001";
        var rows = await Service(db).BuildRowsAsync(request);

        Assert.Equal(new[] { "RO000001", "RO000002" }, rows.Select(r => r.Code));
        Assert.Equal("COOPERATIVA CENTRAL (planejado)", rows[1].Customers);
    }

    // Review Focus 5.
    [Fact]
    public async Task BuildRows_GroupsByProductAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", new DateTime(2026, 7, 10)));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002", new DateTime(2026, 7, 5), uom: "TN", gross: 30m));
        db.Context.StorageTransactions.Add(SalesShipment("RO000003", new DateTime(2026, 7, 20), itemCode: "20001", itemName: "MILHO"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(
            new[] { "MILHO (20001) - KG", "SOJA EM GRÃOS (10001) - KG", "SOJA EM GRÃOS (10001) - TN" },
            rows.Select(r => r.Group));
        Assert.Equal(new[] { "RO000003", "RO000001", "RO000002" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_OrdersByDateThenCodeInsideTheGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000002", new DateTime(2026, 7, 10)));
        db.Context.StorageTransactions.Add(SalesShipment("RO000003", new DateTime(2026, 7, 5)));
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", new DateTime(2026, 7, 10)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "RO000003", "RO000001", "RO000002" }, rows.Select(r => r.Code));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_FlagsWhetherTheResultHasASingleUnit(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageTransactions.Add(SalesShipment("RO000001"));
        if (withTonnes)
            db.Context.StorageTransactions.Add(SalesShipment("RO000002", uom: "TN", gross: 30m));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new SalesShipmentsByPeriodReportService(db, recorder).ExecuteAsync(Request());

        Assert.Equal("SalesShipmentsByPeriod.frx", recorder.LastReportName);
        Assert.Equal(singleUom, recorder.LastParameters![FastReportService.SingleUomParameter]);
    }

    [Fact]
    public async Task Execute_DescribesEveryFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        var load = Load("CG000051");
        db.Context.ShipmentLoads.Add(load);
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", load: load));
        await Save(db);
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.BranchCode = "01";
        request.ItemCode = "10001";
        request.WarehouseCode = "ARM01";
        request.TruckCode = "ABC1D23";
        request.CardCode = "C001";
        request.HasLoad = true;
        await new SalesShipmentsByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data do romaneio: 01/07/2026 a 31/07/2026 | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | " +
            "Situação: todas, exceto Cancelado | Armazém: ARMAZÉM CENTRAL | Placa: ABC1D23 | " +
            "Cliente: COOPERATIVA CENTRAL | Vínculo com carga: Com carga",
            recorder.LastParameters!["pFilters"]);
    }

    [Fact]
    public async Task Execute_WithoutResultFallsBackToCodes()
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        var request = Request();
        request.WarehouseCode = "ARM01";
        request.CardCode = "C001";
        request.HasLoad = false;
        await new SalesShipmentsByPeriodReportService(db, recorder).ExecuteAsync(request);

        Assert.Equal(
            "Data do romaneio: 01/07/2026 a 31/07/2026 | Situação: todas, exceto Cancelado | Armazém: ARM01 | " +
            "Cliente: C001 | Vínculo com carga: Sem carga",
            recorder.LastParameters!["pFilters"]);
    }

    private static SalesShipmentsByPeriodReportService Service(IUnitOfWork db) =>
        new(db, new RecordingFastReportService());

    private static SalesShipmentsByPeriodRequest Request() => new() { FromDate = Jul01, ToDate = Jul31 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesShipmentsByPeriodReportServiceTests"`
Expected: build FAIL — tipos inexistentes.

- [ ] **Step 3: Implement DTOs, service and controller**

`SiagroB1.Reports/Dtos/SalesShipmentsByPeriodRequest.cs` (criar e `git add`):

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros de "Romaneios de Venda" (SalesShipment, período sobre <c>TransactionDate</c>). Situação
/// vazia = todas menos Cancelado. <see cref="HasLoad"/>: true = só com carga, false = só sem
/// carga, null = ambos. Cliente segue a regra da carga; romaneio sem carga nunca casa.
/// </summary>
public class SalesShipmentsByPeriodRequest : LogisticsReportRequest
{
    public List<StorageTransactionsStatus>? Statuses { get; set; }

    public string? WarehouseCode { get; set; }

    public string? TruckCode { get; set; }

    public string? CardCode { get; set; }

    public bool? HasLoad { get; set; }
}
```

`SiagroB1.Reports/Dtos/SalesShipmentRowDto.cs` (criar e `git add`):

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>Linha de "Romaneios de Venda". <see cref="Group"/> = produto + UM do romaneio.</summary>
public class SalesShipmentRowDto
{
    public string Group { get; set; } = "";
    public string TransactionDate { get; set; } = "";
    public string Code { get; set; } = "";
    public string Load { get; set; } = "";
    public string Truck { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string Customers { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public string InvoiceNumber { get; set; } = "";
    public string Status { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal GrossWeight { get; set; }
    public decimal DiscountWeight { get; set; }
    public decimal NetWeight { get; set; }
}
```

`SiagroB1.Reports/Services/SalesShipmentsByPeriodReportService.cs` (criar e `git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Romaneios de venda (<see cref="StorageTransactionType.SalesShipment"/>), agrupados por produto
/// + UM. O CardCode/CardName do romaneio de venda é o FORNECEDOR da perna de compra; o cliente
/// chega pela carga (notas vivas; sem nota, o planejado) — romaneio sem carga fica sem cliente.
/// Descontos = secagem + limpeza + outros (o campo chama-se OthersDicount).
/// </summary>
public class SalesShipmentsByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    public async Task<List<SalesShipmentRowDto>> BuildRowsAsync(SalesShipmentsByPeriodRequest request) =>
        ToRows(await LoadAsync(request));

    public async Task<byte[]> ExecuteAsync(SalesShipmentsByPeriodRequest request)
    {
        var transactions = await LoadAsync(request);
        var rows = ToRows(transactions);
        var first = rows.Count > 0 ? rows[0] : null;
        var statuses = LogisticsReportText.EffectiveTransactionStatuses(request.Statuses);

        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.WarehouseCode))
            extra.Add($"Armazém: {ReportText.Describe(first?.Warehouse, request.WarehouseCode)}");
        if (!string.IsNullOrWhiteSpace(request.TruckCode))
            extra.Add($"Placa: {request.TruckCode}");
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            extra.Add($"Cliente: {ReportText.Describe(LogisticsReportText.CustomerName(transactions.Select(t => t.ShipmentLoad), request.CardCode), request.CardCode)}");
        if (request.HasLoad is { } hasLoad)
            extra.Add($"Vínculo com carga: {(hasLoad ? "Com carga" : "Sem carga")}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = LogisticsReportText.BuildFilters(
                "Data do romaneio",
                request,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                ReportText.ProductOfGroup(first?.Group),
                LogisticsReportText.StatusFilter(statuses, LogisticsReportText.TransactionStatusText, StorageTransactionsStatus.Cancelled),
                extra),
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "SalesShipmentsByPeriod.frx", rows, "SalesShipments", "SalesShipments", parameters);
    }

    private async Task<List<StorageTransaction>> LoadAsync(SalesShipmentsByPeriodRequest request)
    {
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);
        var statuses = LogisticsReportText.EffectiveTransactionStatuses(request.Statuses);

        // Carga e notas da carga por Include (LEFT JOIN, FK opcional): o cliente sai das notas.
        var query = db.Context.StorageTransactions
            .AsNoTracking()
            .Include(t => t.ShipmentLoad).ThenInclude(l => l!.Invoices)
            .Where(t => t.TransactionType == StorageTransactionType.SalesShipment)
            .Where(t => t.TransactionDate >= from && t.TransactionDate < toExclusive)
            .Where(t => statuses.Contains(t.TransactionStatus));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(t => t.BranchCode == request.BranchCode);

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(t => t.ItemCode == request.ItemCode);

        if (!string.IsNullOrWhiteSpace(request.WarehouseCode))
            query = query.Where(t => t.WarehouseCode == request.WarehouseCode);

        if (!string.IsNullOrWhiteSpace(request.TruckCode))
            query = query.Where(t => t.TruckCode == request.TruckCode);

        if (request.HasLoad is true)
            query = query.Where(t => t.ShipmentLoadKey != null);
        else if (request.HasLoad is false)
            query = query.Where(t => t.ShipmentLoadKey == null);

        var transactions = await query.ToListAsync();

        if (request.CardCode is { } cardCode && !string.IsNullOrWhiteSpace(cardCode))
            transactions = transactions.Where(t => LogisticsReportText.LoadHasCustomer(t.ShipmentLoad, cardCode)).ToList();

        return transactions;
    }

    private static List<SalesShipmentRowDto> ToRows(List<StorageTransaction> transactions)
    {
        var groupOf = InvoiceItemGrouping.BuildGroupResolver(
            transactions, t => t.ItemCode, t => t.ItemName, t => t.UnitOfMeasureCode);

        return transactions
            .OrderBy(t => groupOf(t), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.TransactionDate)
            .ThenBy(t => t.Code ?? "", StringComparer.Ordinal)
            .Select(t => ToRow(t, groupOf(t)))
            .ToList();
    }

    private static SalesShipmentRowDto ToRow(StorageTransaction t, string group) => new()
    {
        Group = group,
        TransactionDate = ReportText.Date(t.TransactionDate),
        Code = t.Code ?? "",
        Load = t.ShipmentLoad?.Code ?? "",
        Truck = t.TruckCode ?? "",
        Supplier = ReportText.Partner(t.CardCode, t.CardName),
        Customers = LogisticsReportText.LoadCustomers(t.ShipmentLoad),
        Warehouse = ReportText.NameOrCode(t.WarehouseCode, t.WarehouseName),
        InvoiceNumber = ReportText.DocumentNumber(t.InvoiceNumber, t.InvoiceSerie),
        Status = LogisticsReportText.TransactionStatusText(t.TransactionStatus),
        UnitOfMeasure = t.UnitOfMeasureCode,
        GrossWeight = t.GrossWeight,
        DiscountWeight = t.DryingDiscount + t.CleaningDiscount + t.OthersDicount,
        NetWeight = t.NetWeight,
    };
}
```

`SiagroB1.Reports/Controllers/SalesShipmentsByPeriodController.cs` (criar e `git add`):

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/SalesShipmentsByPeriod")]
public class SalesShipmentsByPeriodController(SalesShipmentsByPeriodReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] SalesShipmentsByPeriodRequest request)
    {
        if (LogisticsReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"sales-shipments-by-period.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesShipmentsByPeriodReportServiceTests"`
Expected: PASS.

- [ ] **Step 5: Template**

Crie `SiagroB1.Reports/Reports/Templates/SalesShipmentsByPeriod.frx` exatamente com o conteúdo abaixo (e `git add`). Colunas:

| Coluna (cabeçalho) | Campo | Left | Largura | Formato | Objeto (cabeçalho / dado) |
|---|---|---|---|---|---|
| Data | TransactionDate | 0 | 62 | texto | `hdrTransactionDate` / `txtTransactionDate` |
| Código | Code | 62 | 62 | texto | `hdrCode` / `txtCode` |
| Carga | Load | 124 | 50 | texto | `hdrLoad` / `txtLoad` |
| Placa | Truck | 174 | 50 | texto | `hdrTruck` / `txtTruck` |
| Fornecedor (origem) | Supplier | 224 | 176 | texto | `hdrSupplier` / `txtSupplier` |
| Cliente | Customers | 400 | 176 | texto | `hdrCustomers` / `txtCustomers` |
| Armazém | Warehouse | 576 | 122 | texto | `hdrWarehouse` / `txtWarehouse` |
| Peso bruto | GrossWeight | 698 | 82 | Number, 3 casas | `hdrGrossWeight` / `txtGrossWeight` |
| Descontos | DiscountWeight | 780 | 82 | Number, 3 casas | `hdrDiscountWeight` / `txtDiscountWeight` |
| Peso líquido | NetWeight | 862 | 82 | Number, 3 casas | `hdrNetWeight` / `txtNetWeight` |
| NF fornecedor | InvoiceNumber | 944 | 78 | texto | `hdrInvoiceNumber` / `txtInvoiceNumber` |
| Situação | Status | 1022 | 62 | texto | `hdrStatus` / `txtStatus` |
| **Soma** | | | **1084** | | |

```xml
<?xml version="1.0" encoding="utf-8"?>
<Report ScriptLanguage="CSharp" ReportInfo.Created="10/08/2026 10:00:00" ReportInfo.Modified="10/08/2026 10:00:00" ReportInfo.CreatorVersion="2026.1.0.0">
  <Styles>
    <Style Name="EvenRows" Fill.Color="Gainsboro" Font="Arial, 10pt"/>
  </Styles>
  <Dictionary>
    <BusinessObjectDataSource Name="SalesShipments" ReferenceName="SalesShipments" DataType="System.Int32" Enabled="true">
      <Column Name="Group" DataType="System.String"/>
      <Column Name="TransactionDate" DataType="System.String"/>
      <Column Name="Code" DataType="System.String"/>
      <Column Name="Load" DataType="System.String"/>
      <Column Name="Truck" DataType="System.String"/>
      <Column Name="Supplier" DataType="System.String"/>
      <Column Name="Customers" DataType="System.String"/>
      <Column Name="Warehouse" DataType="System.String"/>
      <Column Name="GrossWeight" DataType="System.Decimal"/>
      <Column Name="DiscountWeight" DataType="System.Decimal"/>
      <Column Name="NetWeight" DataType="System.Decimal"/>
      <Column Name="InvoiceNumber" DataType="System.String"/>
      <Column Name="Status" DataType="System.String"/>
      <Column Name="UnitOfMeasure" DataType="System.String"/>
    </BusinessObjectDataSource>
    <Parameter Name="pCompanyName" DataType="System.String" AsString=""/>
    <Parameter Name="pFilters" DataType="System.String" AsString=""/>
    <Parameter Name="pSingleUom" DataType="System.Boolean" AsString="true"/>
    <Total Name="TotalGrpGrossWeight" Expression="[SalesShipments.GrossWeight]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpDiscountWeight" Expression="[SalesShipments.DiscountWeight]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpNetWeight" Expression="[SalesShipments.NetWeight]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalSumGrossWeight" Expression="[SalesShipments.GrossWeight]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumDiscountWeight" Expression="[SalesShipments.DiscountWeight]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumNetWeight" Expression="[SalesShipments.NetWeight]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalCountAll" TotalType="Count" Evaluator="Data1" PrintOn="ReportSummary1"/>
  </Dictionary>
  <ReportPage Name="Page1" Landscape="true" PaperWidth="297" PaperHeight="210" LeftMargin="5" TopMargin="5" RightMargin="5" BottomMargin="5" RawPaperSize="9" Watermark.Font="Arial, 60pt">
    <PageHeaderBand Name="PageHeader1" Width="1084" Height="113.4">
      <PictureObject Name="picLogo" Left="0" Top="2" Width="94.5" Height="37.8" SizeMode="Zoom"/>
      <TextObject Name="txtCompany" Left="100" Top="2" Width="700" Height="37.8" Text="[pCompanyName]" VertAlign="Center" Font="Arial, 11pt, style=Bold"/>
      <TextObject Name="txtDate" Left="937" Top="2" Width="147" Height="18.9" Text="[Date]" Format="Date" Format.Format="d" HorzAlign="Right" VertAlign="Center" Font="Tahoma, 6pt"/>
      <TextObject Name="txtTitle" Top="45" Width="1084" Height="28.35" Text="Romaneios de Venda" HorzAlign="Center" VertAlign="Center" Font="Arial, 14pt, style=Bold, Italic"/>
      <TextObject Name="txtFilters" Top="75" Width="1084" Height="37.8" Text="[pFilters]" HorzAlign="Center" VertAlign="Top" WordWrap="true" Font="Consolas, 8pt, style=Italic"/>
    </PageHeaderBand>
    <ColumnHeaderBand Name="ColumnHeader1" Top="116.6" Width="1084" Height="17">
      <TextObject Name="hdrTransactionDate" Left="0" Width="62" Height="17" Text="Data" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrCode" Left="62" Width="62" Height="17" Text="Código" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrLoad" Left="124" Width="50" Height="17" Text="Carga" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrTruck" Left="174" Width="50" Height="17" Text="Placa" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrSupplier" Left="224" Width="176" Height="17" Text="Fornecedor (origem)" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrCustomers" Left="400" Width="176" Height="17" Text="Cliente" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrWarehouse" Left="576" Width="122" Height="17" Text="Armazém" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrGrossWeight" Left="698" Width="82" Height="17" Text="Peso bruto" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrDiscountWeight" Left="780" Width="82" Height="17" Text="Descontos" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrNetWeight" Left="862" Width="82" Height="17" Text="Peso líquido" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrInvoiceNumber" Left="944" Width="78" Height="17" Text="NF fornecedor" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrStatus" Left="1022" Width="62" Height="17" Text="Situação" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
    </ColumnHeaderBand>
    <GroupHeaderBand Name="GroupHeader1" SortOrder="None" Top="136.7" Width="1084" Height="20" Condition="[SalesShipments.Group]">
      <TextObject Name="txtGroup" Left="0" Top="2" Width="700" Height="17" Text="[SalesShipments.Group]" VertAlign="Center" WordWrap="false" Font="Arial, 8pt, style=Bold"/>
      <DataBand Name="Data1" Top="159.75" Width="1084" Height="17" CanGrow="true" EvenStyle="EvenRows" DataSource="SalesShipments">
        <TextObject Name="txtTransactionDate" Left="0" Width="62" Height="17" Text="[SalesShipments.TransactionDate]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtCode" Left="62" Width="62" Height="17" Text="[SalesShipments.Code]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtLoad" Left="124" Width="50" Height="17" Text="[SalesShipments.Load]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtTruck" Left="174" Width="50" Height="17" Text="[SalesShipments.Truck]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtSupplier" Left="224" Width="176" Height="17" Text="[SalesShipments.Supplier]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtCustomers" Left="400" Width="176" Height="17" Text="[SalesShipments.Customers]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtWarehouse" Left="576" Width="122" Height="17" Text="[SalesShipments.Warehouse]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtGrossWeight" Left="698" Width="82" Height="17" Text="[SalesShipments.GrossWeight]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtDiscountWeight" Left="780" Width="82" Height="17" Text="[SalesShipments.DiscountWeight]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtNetWeight" Left="862" Width="82" Height="17" Text="[SalesShipments.NetWeight]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtInvoiceNumber" Left="944" Width="78" Height="17" Text="[SalesShipments.InvoiceNumber]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtStatus" Left="1022" Width="62" Height="17" Text="[SalesShipments.Status]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
      </DataBand>
      <GroupFooterBand Name="GroupFooter1" Top="180" Width="1084" Height="20">
        <TextObject Name="txtGrpLabel" Left="0" Top="2" Width="698" Height="17" Text="Subtotal do produto/UM:" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpGrossWeight" Left="698" Top="2" Width="82" Height="17" Text="[TotalGrpGrossWeight]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpDiscountWeight" Left="780" Top="2" Width="82" Height="17" Text="[TotalGrpDiscountWeight]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpNetWeight" Left="862" Top="2" Width="82" Height="17" Text="[TotalGrpNetWeight]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
      </GroupFooterBand>
    </GroupHeaderBand>
    <ReportSummaryBand Name="ReportSummary1" Top="203" Width="1084" Height="48">
      <TextObject Name="txtSumLabel" Left="0" Top="4" Width="100" Height="17" Text="TOTAL GERAL:" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Bold"/>
      <TextObject Name="txtSumCount" Left="100" Top="4" Width="130" Height="17" Text="[TotalCountAll] romaneio(s)" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="uomMixedNote" Left="232" Top="4" Width="466" Height="17" Text="UMs diferentes: quantidades só nos subtotais." VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="uomSumGrossWeight" Left="698" Top="4" Width="82" Height="17" Text="[TotalSumGrossWeight]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumDiscountWeight" Left="780" Top="4" Width="82" Height="17" Text="[TotalSumDiscountWeight]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumNetWeight" Left="862" Top="4" Width="82" Height="17" Text="[TotalSumNetWeight]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="txtEmpty" Left="0" Top="26" Width="1084" Height="17" Text="[IIf([TotalCountAll] == 0, &quot;Nenhum registro encontrado.&quot;, &quot;&quot;)]" HorzAlign="Center" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Italic"/>
    </ReportSummaryBand>
    <PageFooterBand Name="PageFooter1" Top="254" Width="1084" Height="26">
      <TextObject Name="txtPage" Left="937" Width="147" Height="17" Text="Página [Page#] de [TotalPages#]" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
    </PageFooterBand>
  </ReportPage>
</Report>
```

- [ ] **Step 6: Layout and PDF tests**

Em `LogisticsReportsLayoutTests`, acrescente `"SalesShipmentsByPeriod",` à lista `Templates` e ao dicionário `LongestText`:

```csharp
        ["SalesShipmentsByPeriod"] = new()
        {
            ["txtTransactionDate"] = 10, // 31/12/2026
            ["txtCode"] = 10,            // RO00000123
            ["txtLoad"] = 8,             // CG000051
            ["txtTruck"] = 8,            // ABC-1D23
            ["txtInvoiceNumber"] = 13,   // 000123456/001
            ["txtStatus"] = 10,          // Confirmado
        },
```

Em `LogisticsReportsPdfTests`, acrescente:

```csharp
    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task SalesShipmentsByPeriod_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load("CG000051");
        db.Context.ShipmentLoads.Add(load);
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000101", "C001", "COOPERATIVA CENTRAL"));
        db.Context.SalesInvoices.Add(LoadInvoice(load, "000000102", "C002", "AGRO NORTE"));
        db.Context.StorageTransactions.Add(SalesShipment("RO000001", load: load, drying: 300m, cleaning: 150m, others: 50m));
        db.Context.StorageTransactions.Add(SalesShipment("RO000002")); // sem carga: Carga e Cliente vazios
        db.Context.StorageTransactions.Add(SalesShipment("RO000003", itemCode: "20001", itemName: "MILHO", uom: "TN", gross: 30m));
        await Save(db);

        var pdf = await new SalesShipmentsByPeriodReportService(db, FastReport(Configuration(erp)))
            .ExecuteAsync(new SalesShipmentsByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesShipmentsByPeriod", erp, pdf);
    }

    [Fact]
    public async Task SalesShipmentsByPeriod_EmptyResultStillProducesAPdf()
    {
        var db = TestDb.CreateUnitOfWork();

        var pdf = await new SalesShipmentsByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new SalesShipmentsByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Assert.NotEmpty(pdf);
    }

    [Fact]
    public async Task SalesShipmentsByPeriod_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        foreach (var code in new[] { "RO00000123", "RO00000124" })
        {
            var shipment = SalesShipment(code, gross: 99_999_999.999m, drying: 9_999_999.999m,
                cleaning: 1_111_111.111m, others: 1_111_111.111m,
                cardName: "COOPERATIVA AGROINDUSTRIAL DOS PRODUTORES DO NORTE DO PARANÁ");
            shipment.InvoiceNumber = "000123456";
            shipment.InvoiceSerie = "001";
            db.Context.StorageTransactions.Add(shipment);
        }
        await Save(db);

        var pdf = await new SalesShipmentsByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new SalesShipmentsByPeriodRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("SalesShipmentsByPeriod-large", "STANDALONE", pdf);
    }
```

- [ ] **Step 7: Run all report tests + visual check**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"`
Expected: PASS.

Abra `%TEMP%/siagro-logistics-reports/SalesShipmentsByPeriod-STANDALONE.pdf` e `-large-STANDALONE.pdf`: RO000002 com Carga e Cliente em branco; descontos 500,000; "000123456/001" inteiro em "NF fornecedor"; KG e TN em grupos separados e, no normal, sem total geral de peso (com a nota); no `-large`, total geral presente.

- [ ] **Step 8: Commit**

```bash
git branch --show-current   # feature/logistics-reports
git add SiagroB1.Reports/Dtos/SalesShipmentsByPeriodRequest.cs SiagroB1.Reports/Dtos/SalesShipmentRowDto.cs SiagroB1.Reports/Services/SalesShipmentsByPeriodReportService.cs SiagroB1.Reports/Controllers/SalesShipmentsByPeriodController.cs SiagroB1.Reports/Reports/Templates/SalesShipmentsByPeriod.frx SiagroB1.Application.Tests/Reports/SalesShipmentsByPeriodReportServiceTests.cs SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs
git commit -F - <<'EOF'
feat(reports): relatório de romaneios de venda

Romaneios de venda agrupados por produto e UM, com carga, fornecedor da
perna de compra, cliente (das notas da carga), pesos, descontos somados e
NF do fornecedor. Romaneio sem carga aparece sem cliente; o filtro de
vínculo com carga separa os dois casos.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
EOF
```

---

### Task 6: Itens de menu

**Files:**
- Create: `SiagroB1.Migrations/CommonContext/<timestamp>_AddLogisticsReportMenus.cs` (+ `.Designer.cs` gerado)

**Interfaces:**
- Produces: chaves de menu `shipmentLoadsReport`, `salesShipmentReleasesReport`, `shipmentReleasesReport`, `salesShipmentsReport` (= nomes de rota da Task 7), sob o pai `reports`, `Order` 12–15.

- [ ] **Step 1: Confirmar a próxima ordem livre**

Run (Git Bash, em `siagro-b1-backend/`): `grep -rn "\"reports\"" SiagroB1.Migrations/CommonContext/*.cs | grep -v Designer | grep -v Snapshot`
Expected: o maior `Order` sob `reports` é **11** (`salesReturnsReport`, em `20261008222911_AddFiscalDocumentsReportMenus.cs`). Se aparecer algo maior que 11, use os quatro números seguintes a ele no Step 3 e registre no relatório da task.

- [ ] **Step 2: Gerar a migration vazia**

Run (Git Bash): `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddLogisticsReportMenus --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir CommonContext`
Expected: cria `SiagroB1.Migrations/CommonContext/<timestamp>_AddLogisticsReportMenus.cs` e `.Designer.cs` com `Up`/`Down` vazios. Confira com `git status --short SiagroB1.Migrations`: só os dois arquivos novos — **`CommonDbContextModelSnapshot.cs` não pode mudar** (a migration é só de dados). Se o snapshot mudar, pare: há drift de modelo não relacionado; investigue antes de seguir. `git add` os dois arquivos.

- [ ] **Step 3: Preencher Up/Down**

Substitua os métodos `Up` e `Down` gerados por:

```csharp
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // "Romaneios de Venda" é relatório novo; o antigo "Romaneios de Saída"
            // (storageTransactionsShipmentsReport) continua no menu.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "shipmentLoadsReport", "Cargas por Período", "sap-icon://folder-blank", true, false, 12, "reports", false },
                    { "salesShipmentReleasesReport", "Liberações de Venda", "sap-icon://folder-blank", true, false, 13, "reports", false },
                    { "shipmentReleasesReport", "Liberações de Compra", "sap-icon://folder-blank", true, false, 14, "reports", false },
                    { "salesShipmentsReport", "Romaneios de Venda", "sap-icon://folder-blank", true, false, 15, "reports", false },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F01", "ADMIN", "shipmentLoadsReport" },
                    { "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F02", "ADMIN", "salesShipmentReleasesReport" },
                    { "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F03", "ADMIN", "shipmentReleasesReport" },
                    { "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F04", "ADMIN", "salesShipmentsReport" },
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
                    "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F01", "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F02",
                    "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F03", "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F04",
                ]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues:
                [
                    "shipmentLoadsReport", "salesShipmentReleasesReport", "shipmentReleasesReport",
                    "salesShipmentsReport",
                ]);
        }
```

É a mesma forma de `20261008222911_AddFiscalDocumentsReportMenus.cs` (colunas, tipo string do `Id`, `StandaloneOnly`); confira lá se o gerador da sua versão do EF produzir outro cabeçalho de classe.

- [ ] **Step 4: Validar com `migrations script` (sem tocar em banco)**

**Não rode `dotnet ef database update`.** Gere o SQL das duas direções:

Run (Git Bash): `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations script AddFiscalDocumentsReportMenus AddLogisticsReportMenus --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`
Expected: SQL com um `INSERT INTO [MENU_ITEMS] ([Key], [Title], [Icon], [Enabled], [Expanded], [Order], [ParentKey], [StandaloneOnly])` com as 4 linhas (Order 12–15, `ParentKey` `reports`), um `INSERT INTO [ROLE_MENUS]` com 4 linhas ADMIN e o `INSERT INTO [__EFMigrationsHistory]` de `<timestamp>_AddLogisticsReportMenus`. Nenhum `CREATE`/`ALTER`.

Run (Git Bash): `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations script AddLogisticsReportMenus AddFiscalDocumentsReportMenus --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`
Expected: `DELETE FROM [ROLE_MENUS]` dos 4 Ids, `DELETE FROM [MENU_ITEMS]` das 4 chaves e o `DELETE` do histórico.

- [ ] **Step 5: Build e commit**

Run: `dotnet build SiagroB1.sln` — Expected: 0 erros.

```bash
git branch --show-current   # feature/logistics-reports
git add SiagroB1.Migrations/CommonContext/*_AddLogisticsReportMenus*.cs
git commit -F - <<'EOF'
feat(reports): itens de menu dos relatórios de logística

Cargas por Período, Liberações de Venda, Liberações de Compra e Romaneios
de Venda sob Relatórios, liberados para o perfil ADMIN.

DB: AddLogisticsReportMenus
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
EOF
```

---

### Task 7: Frontend — quatro telas de filtro

Repo: `siagro-b1-frontend` (branch `feature/logistics-reports` já existe; confira `git -C ../siagro-b1-frontend branch --show-current` e que a árvore está limpa com `git status`). Todos os caminhos abaixo são relativos a `siagro-b1-frontend/`.

**Files:**
- Modify: `webapp/model/ServerRoutes.ts`
- Create: `webapp/view/reports/fragments/ReportDateRangeFilters.fragment.xml`
- Create: `webapp/controller/reports/{shipmentLoads,salesShipmentReleases,shipmentReleases,salesShipments}/Main.controller.ts`
- Create: `webapp/view/reports/{shipmentLoads,salesShipmentReleases,shipmentReleases,salesShipments}/Main.view.xml`
- Modify: `webapp/manifest.json` (rotas + targets)

**Interfaces:**
- Consumes: endpoints das Tasks 2–5; chaves de menu da Task 6; `InvoiceReportController` (sem alteração: `routeName`/`formId`/`serverRoute` abstratos, `defaults()`, `buildPayload()`, `onPrintReport`); fragmento existente `InvoiceReportCommonFilters` (Filial + Produto); `CommonController.openWarehouseValueHelp`, `openSuppliersValueHelp`, `openTrucksValueHelp`, `openCostumersValueHelp`, `openAgentsValueHelp`, `openLogisticRegionsValueHelp`.
- Produces: rotas `shipmentLoadsReport`, `salesShipmentReleasesReport`, `shipmentReleasesReport`, `salesShipmentsReport`.

Antes de começar: invoque o skill `ui5:ui5-best-practices`. Regras que valem para as quatro telas:

- **Enums viajam como números**: o `SiagroB1.Reports` não registra `JsonStringEnumConverter`. As chaves dos `MultiComboBox`/`Select` são os números do enum **em string** (o `selectedKeys` só casa com string); o `buildPayload` do base converte `Statuses` com `Number`, e cada tela converte seus `Select`s.
- A situação padrão de cada tela vem do `defaults()`: o `routeMatched` do base faz `{ Statuses: ["0","1","3"], NfeStatuses: [], ...this.defaults() }`, então o `Statuses` do `defaults()` sobrescreve o das notas. `NfeStatuses: []` chega ao backend e é ignorado (propriedade desconhecida).
- Layout igual ao das telas do pacote 1 (`SimpleForm` `ResponsiveGridLayout` em duas colunas, "Período e situação" | "Filtros").

- [ ] **Step 1: ServerRoutes**

Em `webapp/model/ServerRoutes.ts`, logo abaixo da linha `  salesReturnsReport: '/reports/SalesReturns',`, acrescente:

```ts
  shipmentLoadsByPeriodReport: '/reports/ShipmentLoadsByPeriod',
  salesShipmentReleasesByPeriodReport: '/reports/SalesShipmentReleasesByPeriod',
  shipmentReleasesByPeriodReport: '/reports/ShipmentReleasesByPeriod',
  salesShipmentsByPeriodReport: '/reports/SalesShipmentsByPeriod',
```

- [ ] **Step 2: Fragmento de período**

O `InvoiceReportPeriodFilters` diz "Emissão de/até" e traz a situação das notas; a logística precisa de "Data de/até" e de situação própria. `webapp/view/reports/fragments/ReportDateRangeFilters.fragment.xml` (criar e `git add`):

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core">
	<Label text="Data de" required="true"/>
	<DatePicker
		value="{ path: 'params>/FromDate', type: 'sap.ui.model.odata.type.DateTimeOffset', constraints: { precision: 7 }, formatOptions: { pattern: 'dd/MM/yyyy' } }"
		liveChange=".validateField"
		required="true"/>
	<Label text="Data até" required="true"/>
	<DatePicker
		value="{ path: 'params>/ToDate', type: 'sap.ui.model.odata.type.DateTimeOffset', constraints: { precision: 7 }, formatOptions: { pattern: 'dd/MM/yyyy' } }"
		liveChange=".validateField"
		required="true"/>
</core:FragmentDefinition>
```

- [ ] **Step 3: Tela "Cargas por Período"**

`webapp/controller/reports/shipmentLoads/Main.controller.ts` (criar e `git add`):

```ts
import InvoiceReportController from "../InvoiceReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * Cargas por Período. Situação padrão: todas menos Cancelada (3).
 * ShipmentLoadStatus: 0 Carregada, 1 Faturada Parcial, 2 Faturada, 3 Cancelada, 4 Planejada,
 * 5 Devolvida, 6 Concluída, 7 Em Transbordo, 8 Descarregada. LoadType: 0 Normal, 1 Remoção.
 * @namespace siagrob1.controller.reports.shipmentLoads
 */
export default class Main extends InvoiceReportController {
	protected readonly routeName = "shipmentLoadsReport";
	protected readonly formId = "shipmentLoadsReportForm";
	protected readonly serverRoute = ServerRoutes.shipmentLoadsByPeriodReport;

	protected defaults() {
		return { Statuses: ["4", "0", "1", "2", "7", "8", "6", "5"], LoadType: "" };
	}

	protected buildPayload() {
		const data = super.buildPayload();
		data.LoadType = data.LoadType == null ? null : Number(data.LoadType);
		return data;
	}
}
```

`webapp/view/reports/shipmentLoads/Main.view.xml` (criar e `git add`):

```xml
<mvc:View
	controllerName="siagrob1.controller.reports.shipmentLoads.Main"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form"
	xmlns:l="sap.ui.layout">
	<Page title="Cargas por Período">
		<f:SimpleForm
			id="shipmentLoadsReportForm"
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
			columnsXL="2"
			columnsL="2"
			columnsM="2"
			singleContainerFullSize="false"
			busyIndicatorDelay="0">
			<f:content>
				<core:Title text="Período e situação" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.ReportDateRangeFilters" type="XML"/>
				<Label text="Situação"/>
				<MultiComboBox selectedKeys="{params>/Statuses}">
					<core:Item key="4" text="Planejada"/>
					<core:Item key="0" text="Carregada"/>
					<core:Item key="1" text="Faturada Parcial"/>
					<core:Item key="2" text="Faturada"/>
					<core:Item key="7" text="Em Transbordo"/>
					<core:Item key="8" text="Descarregada"/>
					<core:Item key="6" text="Concluída"/>
					<core:Item key="5" text="Devolvida"/>
					<core:Item key="3" text="Cancelada"/>
				</MultiComboBox>
				<core:Title text="Filtros" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.InvoiceReportCommonFilters" type="XML"/>
				<Label text="Tipo"/>
				<Select selectedKey="{params>/LoadType}">
					<core:Item key="" text="Todos"/>
					<core:Item key="0" text="Normal"/>
					<core:Item key="1" text="Remoção"/>
				</Select>
				<Label text="Armazém"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openWarehouseValueHelp" value="{params>/WarehouseCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData>
						<core:CustomData key="descriptionProperty" value="WarehouseName:Name"/>
					</customData>
				</Input>
				<Input value="{params>/WarehouseName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
				<Label text="Transportadora"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openSuppliersValueHelp" value="{params>/CarrierCardCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData>
						<core:CustomData key="descriptionProperty" value="CarrierName:CardName"/>
					</customData>
				</Input>
				<Input value="{params>/CarrierName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
				<Label text="Placa"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openTrucksValueHelp" value="{params>/TruckCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
				</Input>
				<Label text="Cliente"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openCostumersValueHelp" value="{params>/CardCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData>
						<core:CustomData key="descriptionProperty" value="CardName"/>
					</customData>
				</Input>
				<Input value="{params>/CardName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
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

(Transportadora usa `openSuppliersValueHelp`, como o formulário da carga — `LoadForm.fragment.xml`.)

- [ ] **Step 4: Tela "Liberações de Venda"**

`webapp/controller/reports/salesShipmentReleases/Main.controller.ts` (criar e `git add`):

```ts
import InvoiceReportController from "../InvoiceReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * Liberações de Venda. ReleaseStatus: 0 Pendente, 1 Ativo, 2 Finalizado, 3 Cancelado,
 * 4 Pausado — padrão sem Cancelado. Vendedor = AgentCode (inteiro) do contrato.
 * @namespace siagrob1.controller.reports.salesShipmentReleases
 */
export default class Main extends InvoiceReportController {
	protected readonly routeName = "salesShipmentReleasesReport";
	protected readonly formId = "salesShipmentReleasesReportForm";
	protected readonly serverRoute = ServerRoutes.salesShipmentReleasesByPeriodReport;

	protected defaults() {
		return { Statuses: ["0", "1", "2", "4"], ContractCode: "", AgentCode: "" };
	}

	protected buildPayload() {
		const data = super.buildPayload();
		data.AgentCode = data.AgentCode == null ? null : Number(data.AgentCode);
		return data;
	}
}
```

`webapp/view/reports/salesShipmentReleases/Main.view.xml` (criar e `git add`):

```xml
<mvc:View
	controllerName="siagrob1.controller.reports.salesShipmentReleases.Main"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form"
	xmlns:l="sap.ui.layout">
	<Page title="Liberações de Venda">
		<f:SimpleForm
			id="salesShipmentReleasesReportForm"
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
			columnsXL="2"
			columnsL="2"
			columnsM="2"
			singleContainerFullSize="false"
			busyIndicatorDelay="0">
			<f:content>
				<core:Title text="Período e situação" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.ReportDateRangeFilters" type="XML"/>
				<Label text="Situação"/>
				<MultiComboBox selectedKeys="{params>/Statuses}">
					<core:Item key="0" text="Pendente"/>
					<core:Item key="1" text="Ativo"/>
					<core:Item key="2" text="Finalizado"/>
					<core:Item key="4" text="Pausado"/>
					<core:Item key="3" text="Cancelado"/>
				</MultiComboBox>
				<core:Title text="Filtros" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.InvoiceReportCommonFilters" type="XML"/>
				<Label text="Contrato"/>
				<Input value="{params>/ContractCode}" maxLength="50">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
				</Input>
				<Label text="Cliente"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openCostumersValueHelp" value="{params>/CardCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData>
						<core:CustomData key="descriptionProperty" value="CardName"/>
					</customData>
				</Input>
				<Input value="{params>/CardName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
				<Label text="Vendedor"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openAgentsValueHelp" value="{params>/AgentCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData>
						<core:CustomData key="descriptionProperty" value="AgentName:Name"/>
					</customData>
				</Input>
				<Input value="{params>/AgentName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
				<Label text="Região logística"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openLogisticRegionsValueHelp" value="{params>/LogisticRegionCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData>
						<core:CustomData key="descriptionProperty" value="LogisticRegionName:Name"/>
					</customData>
				</Input>
				<Input value="{params>/LogisticRegionName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
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

- [ ] **Step 5: Tela "Liberações de Compra"**

`webapp/controller/reports/shipmentReleases/Main.controller.ts` (criar e `git add`):

```ts
import InvoiceReportController from "../InvoiceReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * Liberações de Compra. ReleaseStatus: 0 Pendente, 1 Ativo, 2 Finalizado, 3 Cancelado,
 * 4 Pausado — padrão sem Cancelado. ReleaseOrigin: 0 Compra, 1 Transferência, 2 Devolução,
 * 3 Transbordo.
 * @namespace siagrob1.controller.reports.shipmentReleases
 */
export default class Main extends InvoiceReportController {
	protected readonly routeName = "shipmentReleasesReport";
	protected readonly formId = "shipmentReleasesReportForm";
	protected readonly serverRoute = ServerRoutes.shipmentReleasesByPeriodReport;

	protected defaults() {
		return { Statuses: ["0", "1", "2", "4"], ContractCode: "", Origin: "" };
	}

	protected buildPayload() {
		const data = super.buildPayload();
		data.Origin = data.Origin == null ? null : Number(data.Origin);
		return data;
	}
}
```

`webapp/view/reports/shipmentReleases/Main.view.xml` (criar e `git add`):

```xml
<mvc:View
	controllerName="siagrob1.controller.reports.shipmentReleases.Main"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form"
	xmlns:l="sap.ui.layout">
	<Page title="Liberações de Compra">
		<f:SimpleForm
			id="shipmentReleasesReportForm"
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
			columnsXL="2"
			columnsL="2"
			columnsM="2"
			singleContainerFullSize="false"
			busyIndicatorDelay="0">
			<f:content>
				<core:Title text="Período e situação" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.ReportDateRangeFilters" type="XML"/>
				<Label text="Situação"/>
				<MultiComboBox selectedKeys="{params>/Statuses}">
					<core:Item key="0" text="Pendente"/>
					<core:Item key="1" text="Ativo"/>
					<core:Item key="2" text="Finalizado"/>
					<core:Item key="4" text="Pausado"/>
					<core:Item key="3" text="Cancelado"/>
				</MultiComboBox>
				<core:Title text="Filtros" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.InvoiceReportCommonFilters" type="XML"/>
				<Label text="Contrato"/>
				<Input value="{params>/ContractCode}" maxLength="50">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
				</Input>
				<Label text="Fornecedor"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openSuppliersValueHelp" value="{params>/CardCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData>
						<core:CustomData key="descriptionProperty" value="CardName"/>
					</customData>
				</Input>
				<Input value="{params>/CardName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
				<Label text="Armazém de retirada"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openWarehouseValueHelp" value="{params>/DeliveryLocationCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData>
						<core:CustomData key="descriptionProperty" value="DeliveryLocationName:Name"/>
					</customData>
				</Input>
				<Input value="{params>/DeliveryLocationName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
				<Label text="Origem"/>
				<Select selectedKey="{params>/Origin}">
					<core:Item key="" text="Todas"/>
					<core:Item key="0" text="Compra"/>
					<core:Item key="1" text="Transferência"/>
					<core:Item key="2" text="Devolução"/>
					<core:Item key="3" text="Transbordo"/>
				</Select>
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

- [ ] **Step 6: Tela "Romaneios de Venda"**

`webapp/controller/reports/salesShipments/Main.controller.ts` (criar e `git add`):

```ts
import InvoiceReportController from "../InvoiceReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * Romaneios de Venda. StorageTransactionsStatus: 0 Pendente, 1 Confirmado, 2 Cancelado,
 * 3 Faturado, 4 Devolvido — padrão sem Cancelado. "Vínculo com carga" viaja como booleano
 * (true = com carga, false = sem carga, null = ambos).
 * @namespace siagrob1.controller.reports.salesShipments
 */
export default class Main extends InvoiceReportController {
	protected readonly routeName = "salesShipmentsReport";
	protected readonly formId = "salesShipmentsReportForm";
	protected readonly serverRoute = ServerRoutes.salesShipmentsByPeriodReport;

	protected defaults() {
		return { Statuses: ["0", "1", "3", "4"], HasLoad: "" };
	}

	protected buildPayload() {
		const data = super.buildPayload();
		data.HasLoad = data.HasLoad == null ? null : data.HasLoad === "true";
		return data;
	}
}
```

`webapp/view/reports/salesShipments/Main.view.xml` (criar e `git add`):

```xml
<mvc:View
	controllerName="siagrob1.controller.reports.salesShipments.Main"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form"
	xmlns:l="sap.ui.layout">
	<Page title="Romaneios de Venda">
		<f:SimpleForm
			id="salesShipmentsReportForm"
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
			columnsXL="2"
			columnsL="2"
			columnsM="2"
			singleContainerFullSize="false"
			busyIndicatorDelay="0">
			<f:content>
				<core:Title text="Período e situação" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.ReportDateRangeFilters" type="XML"/>
				<Label text="Situação"/>
				<MultiComboBox selectedKeys="{params>/Statuses}">
					<core:Item key="0" text="Pendente"/>
					<core:Item key="1" text="Confirmado"/>
					<core:Item key="3" text="Faturado"/>
					<core:Item key="4" text="Devolvido"/>
					<core:Item key="2" text="Cancelado"/>
				</MultiComboBox>
				<core:Title text="Filtros" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.InvoiceReportCommonFilters" type="XML"/>
				<Label text="Armazém"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openWarehouseValueHelp" value="{params>/WarehouseCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData>
						<core:CustomData key="descriptionProperty" value="WarehouseName:Name"/>
					</customData>
				</Input>
				<Input value="{params>/WarehouseName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
				<Label text="Placa"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openTrucksValueHelp" value="{params>/TruckCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
				</Input>
				<Label text="Cliente"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openCostumersValueHelp" value="{params>/CardCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData>
						<core:CustomData key="descriptionProperty" value="CardName"/>
					</customData>
				</Input>
				<Input value="{params>/CardName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
				<Label text="Vínculo com carga"/>
				<Select selectedKey="{params>/HasLoad}">
					<core:Item key="" text="Com e sem carga"/>
					<core:Item key="true" text="Com carga"/>
					<core:Item key="false" text="Sem carga"/>
				</Select>
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

- [ ] **Step 7: manifest.json**

Em `routing.routes`, logo depois da rota `salesReturnsReport` (o objeto com `"pattern": "sales-returns/report"`), acrescente:

```json
        {
          "pattern": "shipment-loads/report",
          "name": "shipmentLoadsReport",
          "target": "shipmentLoadsReport"
        },
        {
          "pattern": "sales-shipment-releases/report",
          "name": "salesShipmentReleasesReport",
          "target": "salesShipmentReleasesReport"
        },
        {
          "pattern": "shipment-releases/report",
          "name": "shipmentReleasesReport",
          "target": "shipmentReleasesReport"
        },
        {
          "pattern": "sales-shipments/report",
          "name": "salesShipmentsReport",
          "target": "salesShipmentsReport"
        },
```

Em `routing.targets`, logo depois do target `salesReturnsReport`, acrescente:

```json
        "shipmentLoadsReport": {
          "id": "shipmentLoadsReport",
          "level": 1,
          "name": "siagrob1.view.reports.shipmentLoads.Main",
          "clearControlAggregation": true
        },
        "salesShipmentReleasesReport": {
          "id": "salesShipmentReleasesReport",
          "level": 1,
          "name": "siagrob1.view.reports.salesShipmentReleases.Main",
          "clearControlAggregation": true
        },
        "shipmentReleasesReport": {
          "id": "shipmentReleasesReport",
          "level": 1,
          "name": "siagrob1.view.reports.shipmentReleases.Main",
          "clearControlAggregation": true
        },
        "salesShipmentsReport": {
          "id": "salesShipmentsReport",
          "level": 1,
          "name": "siagrob1.view.reports.salesShipments.Main",
          "clearControlAggregation": true
        },
```

Os padrões não colidem com rotas existentes (`shipment-loads/{id}/detail`, `shipment-releases/{id}/detail` e `sales-shipment-releases/{id}/detail` têm três segmentos). Confira que o JSON continua válido: `node -e "JSON.parse(require('fs').readFileSync('webapp/manifest.json','utf8'))"` — Expected: sem saída.

- [ ] **Step 8: Gates do frontend**

Run (em `siagro-b1-frontend/`): `yarn ts-typecheck` — Expected: 0 erros.
Run: `yarn lint` — Expected: sem erros novos nos arquivos tocados.
(`yarn test` não passa por causa do gate de cobertura irreal — não use como critério.)

- [ ] **Step 9: Commit (frontend)**

```bash
git -C ../siagro-b1-frontend branch --show-current   # feature/logistics-reports
cd ../siagro-b1-frontend
git add webapp/model/ServerRoutes.ts webapp/view/reports/fragments/ReportDateRangeFilters.fragment.xml webapp/controller/reports/shipmentLoads webapp/view/reports/shipmentLoads webapp/controller/reports/salesShipmentReleases webapp/view/reports/salesShipmentReleases webapp/controller/reports/shipmentReleases webapp/view/reports/shipmentReleases webapp/controller/reports/salesShipments webapp/view/reports/salesShipments webapp/manifest.json
git commit -F - <<'EOF'
feat(reports): telas dos relatórios de logística

Cargas por Período, Liberações de Venda, Liberações de Compra e Romaneios
de Venda, sobre o controller-base dos relatórios fiscais. Situação padrão
sem cancelado; enums viajam como número porque o serviço de relatórios não
tem conversor de enum por nome.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
EOF
```

---

### Task 8: Verificação pelo caminho do usuário

Sem código novo; corrige o que aparecer (cada correção com teste quando couber, e commit próprio no repo certo).

- [ ] **Step 1: Suíte do backend**

Run (em `siagro-b1-backend/`): `dotnet test SiagroB1.Application.Tests` — Expected: PASS (inclui pacote 1 inteiro).

- [ ] **Step 2: STANDALONE (CEAGUI dev)**

Suba Web, Gateway e Reports com o profile `ceagui` e o frontend com `yarn start:dev` (ver memória "Subir a stack local"; login admin/1234). O item de menu **não** aparece enquanto a migration da Task 6 não for aplicada — e aplicá-la é escrita em banco: **peça autorização ao usuário** antes (`ASPNETCORE_ENVIRONMENT=Ceagui-Development` explícito, `--context CommonDbContext`). Sem autorização, abra cada tela pela rota (hash `shipment-loads/report`, `sales-shipment-releases/report`, `shipment-releases/report`, `sales-shipments/report` na URL do app). Em cada tela: período de julho a outubro/2026, Imprimir → PDF abre em nova aba, com filtros no cabeçalho, grupos por produto/UM e subtotais. Teste pelo menos: situação padrão, Cancelada/Cancelado sozinha, um filtro de value help (Armazém ou Cliente), Tipo = Remoção nas cargas e "Sem carga" nos romaneios.

- [ ] **Step 3: Período inválido**

Na tela de cargas, "Data até" antes de "Data de" → mensagem "A data final não pode ser anterior à inicial." (vinda do 400).

- [ ] **Step 4: SAPB1 (Yokotobi dev)**

Se `IDX_SIAGRO_DEV` ainda estiver atrás das migrations, o Web recusa subir: **peça autorização ao usuário** para atualizá-la (`ASPNETCORE_ENVIRONMENT=Yokotobi-Development` explícito, contexts `AppDbContext` e `CommonDbContext`). Com o ok, suba com o profile `yktb` e repita o Step 2. Confira que cliente, fornecedor, produto, armazém e transportadora aparecem preenchidos (snapshots; nada vem de tabela do SAP).

- [ ] **Step 5: Fechamento**

Use `superpowers:verification-before-completion` antes de declarar pronto. Atualize a memória do projeto (arquivo novo sobre o pacote 2 + ponteiro no `MEMORY.md`). Merge em `main` e push continuam sendo decisão do usuário.
