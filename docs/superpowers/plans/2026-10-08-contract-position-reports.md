# Relatórios de Posição de Contratos (pacote 3) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dois relatórios PDF de posição atual de contratos — "Contratos — Compra x Venda" e "Posição Comprado x Vendido por Mês" — com entregue e saldo recalculados do razão (iguais ao `AvaiableVolume` do domínio depois de um recálculo), funcionando em SAPB1 e STANDALONE, com tela de filtros e item de menu.

**Architecture:** Padrão dos pacotes 1 e 2 no serviço `SiagroB1.Reports`: controller `POST /reports/<Nome>` → `<Nome>ReportService.BuildRowsAsync` → `IFastReportService.GeneratePdfAsync` com um `.frx` paisagem. Base comum nova: `ContractPositionData` lê contratos (só snapshots e tabelas locais) e soma o razão em lote (GROUP BY por contrato), com as MESMAS fórmulas dos serviços de recálculo do `SiagroB1.Application` — que o Reports não referencia; um teste cruzado no `SiagroB1.Application.Tests` (que referencia os dois) roda as duas contas sobre o mesmo dado. `ContractPositionText` concentra rótulos, situações, "sem prazo", linha de filtros e ordem. Frontend: duas telas UI5 sobre um controller-base novo que estende o `InvoiceReportController` existente.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), FastReport.OpenSource 2026.1.3, xUnit; OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-08-contract-position-reports-design.md`

**Pré-validação (08–09/10, ao escrever o plano):** todo o código deste plano (C#, os dois `.frx`, a migration e o TypeScript/XML) foi aplicado em worktrees descartáveis sobre `1ef3eb8` (backend) e `f88e0d1` (frontend): `dotnet build SiagroB1.sln` com 0 erros; `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"` com **534** testes verdes e a suíte inteira com **3759**; o estado intermediário do fim da Task 2 também foi rodado (510 verdes); os seis PDFs foram conferidos a olho; `dotnet ef migrations script` gerou o SQL esperado nas duas direções; `tsc --noEmit` e `eslint webapp` sem erro. Se algo não compilar na sua execução, o código-base mudou depois — investigue antes de "consertar" o plano.

## Global Constraints

- Branch `feature/contract-position-reports` nos **dois** repos (`siagro-b1-backend`, `siagro-b1-frontend`) — já criados a partir de `main`. Confira `git branch --show-current` antes de **cada** commit. Nunca push, nunca merge.
- Todo arquivo novo: `git add <path>` logo após criar.
- Commits no formato do `CLAUDE.md` do repo: `tipo(escopo): descrição pt-BR`, escopo `reports`; migration exige trailer `DB: <NomeDaMigration>`. Rodapé obrigatório (as duas linhas):
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
  ```
- Identificadores em inglês; texto que o usuário lê (tela, PDF, mensagens) em pt-BR.
- **O `SiagroB1.Reports` não referencia o `SiagroB1.Application`** (Reports → Infra/Commons/Security). Não acrescente essa referência e não mova código do Application: a fórmula é reimplementada em `ContractPositionData` e travada pelo teste cruzado `ContractPositionDataTests` (Task 1).
- Relatório **nunca** faz JOIN com `ITEMS`, `BUSINESS_PARTNERS`, `UNITS_OF_MEASURE` (vazias em SAPB1) nem com `HARVEST_SEASONS` (FK obrigatória: `Include` vira INNER JOIN). Só `PurchaseContracts`, `SalesContracts`, `PurchaseContractsAllocations`, `PurchaseContractsWashouts`, `SalesContractsAllocations`, `SalesInvoicesItems`, `Branchs`. Nomes vêm dos snapshots do contrato.
- **Nunca leia `AllocatedVolume` nem `WashedOutVolume` persistidos** — derivados que derivam. Os seeds de teste gravam valores errados neles de propósito.
- Queries EF só com filtros traduzíveis; somas do razão em `GROUP BY` com `IN (SELECT …)` da própria consulta de contratos (nada de `Contains` sobre lista de chaves em memória). Rótulos, ordem, grupos e prazos em C#.
- Sem período: nenhum filtro é obrigatório e o controller não devolve 400 de negócio.
- Situação vazia = só Aprovado. Rótulos pt-BR **iguais** aos de `siagro-b1-frontend/webapp/model/formatter.ts` (`formatContractStatus`, `formatContractType`).
- Ordem: grupo com `StringComparer.CurrentCultureIgnoreCase` (como os pacotes 1 e 2); códigos com `StringComparer.Ordinal`. **Testes de ordem só com nomes cuja ordem é a mesma em ordinal e em qualquer cultura** (`MILHO` < `SOJA`; `KG` < `TN`).
- `.frx`: copie o XML **completo** dado em cada task. Regras no Appendix A.
- Não altere nenhuma string, teste ou template dos pacotes 1 e 2. `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"` tem de continuar verde ao fim de cada task.
- **Nenhum `dotnet ef database update`** neste plano. A migration é validada só com `dotnet ef migrations script`.
- Rodar testes a partir de `siagro-b1-backend/`: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`.
- Num worktree novo, copie antes `SiagroB1.Reports/wwwroot` do checkout principal (está no `.git/info/exclude`; sem ele os testes de PDF falham com "Logo fixture missing").

## Review Focus

Os cinco jeitos mais prováveis de o relatório sair errado com dado real — cada um com teste na task dona:

1. **Devolução de compra (alocação negativa) e sobre-entrega** — o entregue é Σ com sinal (devolução subtrai; tipos 10/11 gravam 0) e o saldo pode ficar negativo, igual ao domínio. Testes `PurchaseBalance_EqualsTheDomainAfterARecalc` (Task 1) e `BuildRows_PurchaseReturnReducesTheDelivered` (Task 2).
2. **Washout** — só InApproval + Approved reservam volume; Rejected e Reversed não; o persistido `WashedOutVolume` é ignorado; na venda a coluna sai em branco. Testes `PurchaseBalance_EqualsTheDomainAfterARecalc` (Task 1), `BuildRows_OnlyActiveWashoutsReduceThePurchaseBalance` (Task 2), `ContractPosition_WashoutHidesZeros` (Task 2) e `BuildRows_GroupHeaderCarriesContractedDeliveredAndWashout` (Task 3).
3. **Quebra de entrega na venda** — só a linha dona (`OwnsDeliveryDifference`) desconta, e só com o item `Closed`; item aberto conta o nominal; saldo negativo entra como está (inclusive reduzindo o mês). Testes `SalesBalance_EqualsTheDomainAfterARecalc` (Task 1), `BuildRows_SalesBalanceUsesTheShortageAndMayBeNegative` (Task 2), `BuildRows_NegativeBalanceReducesTheMonth` (Task 3).
4. **Contrato sem previsão de pagamento e/ou sem prazo de entrega** (`DeliveryEndDate` vazio = antes de 1901) — a linha não some, as datas saem em branco, vai para o fim da ordem, para a linha "Sem prazo" do relatório 2 e fica fora do filtro "Término da entrega até". Testes `DeliveryEnd_IsEmptyWithoutDeadline`, `InSectionOrder_CashFlowThenDeadlineThenCode`, `Query_DeliveryEndUntilLeavesContractsWithoutDeadlineOut` (Task 1), `BuildRows_ContractWithoutCashFlowNorDeadlineKeepsTheRow` (Task 2), `BuildRows_OverdueThenMonthsThenNoDeadline` (Task 3).
5. **UM mista e contrato finalizado** — grupo = produto + UM (+ safra no relatório 2) e total geral só com uma UM; contrato Finalizado só aparece quando pedido, com o saldo do domínio no relatório 1 e nada a entregar no relatório 2. Testes `BuildRows_PurchasesThenSalesInsideEachProductAndUnit`, `Execute_FlagsWhetherTheResultHasASingleUnit`, `BuildRows_FinishedContractOnlyWhenChosen` (Task 2), `BuildRows_GroupsByProductHarvestAndUnit`, `BuildRows_FinishedContractHasNothingLeftToDeliver` (Task 3).

---

## Appendix A — Regras dos templates `.frx`

Os dois templates são dados **completos** nas tasks; não os redesenhe. Seguem o esqueleto dos templates do pacote 2, que `ContractPositionReportsLayoutTests` confere:

1. Paisagem A4 (`Landscape="true" PaperWidth="297" PaperHeight="210"`), bandas com `Width="1084"`; `PageHeader1` com `picLogo`, `txtCompany` (`[pCompanyName]`), `txtDate`, `txtTitle`, `txtFilters` (`[pFilters]`).
2. `ColumnHeader1` com um `hdr<Campo>` por coluna; `Data1` com o `txt<Campo>` correspondente, **mesmo `Left` e `Width`**.
3. Todo `GroupHeaderBand` com `SortOrder="None"` (o serviço já ordena; o FastReport reordenaria). O relatório 1 tem dois níveis: `GroupHeader1` (`[ContractPositions.Group]`) e, dentro, `GroupHeader2` (`[ContractPositions.Section]`).
4. `ReportSummary1`: `txtSumLabel`, `txtSumCount`, `uomMixedNote`, `uomSum*` (escondidos pelo `FastReportService` quando `pSingleUom = false`), `txtEmpty` ("Nenhum registro encontrado." quando `TotalCountAll == 0`).
5. **Regra de largura** (medida no PDF real): `(caracteres × 5,2 + 4) × corpo/7 + 4` px. Quantidade **82 px** (`99.999.999,999`, 14 car.); dinheiro **72 px** (`9.999.999,99`, 12); data **62 px** (10). **Saldo com sinal: 88 px** — medido em 08/10 ao escrever este plano: `-10.000.000,000` (15 car.) a 7pt saiu `-10.000.000,0…` com 86 px (a conta dá 86,0, no limite) e coube com 88.
6. Números: `Format="Number" Format.UseLocale="true"`, `DecimalDigits` 3 (quantidade) ou 2 (preço), `HorzAlign="Right"`. Subtotais e totais em **Consolas 6pt bold numa linha só** (mesmo `Top` na banda).
7. Texto de célula: `WordWrap="false"` + `Trimming="EllipsisCharacter"`. Cabeçalho cabe inteiro pela mesma regra.
8. Washout (dado e total da seção) com `HideZeros="true"`: venda e compra sem washout saem em branco.
9. Parâmetros: `pCompanyName`, `pFilters`, `pSingleUom` (`System.Boolean`); relatório 1 também `pGeneralLabel` (`System.String`); relatório 2 também `pContractCount` (`System.Int32`).

Para conferir: o teste de PDF grava `%TEMP%/siagro-contract-position-reports/<Nome>-<modo>.pdf`; abra com a ferramenta Read e procure coluna cortada, número com "…" ou cabeçalho truncado.

---

### Task 1: Base comum — `ContractPositionData`, `ContractPositionText` e seed de teste

**Files:**
- Create: `SiagroB1.Reports/Dtos/ContractPositionReportRequest.cs`
- Create: `SiagroB1.Reports/Dtos/ContractPositionSide.cs`
- Create: `SiagroB1.Reports/Dtos/ContractPosition.cs`
- Create: `SiagroB1.Reports/Helpers/ContractPositionText.cs`
- Create: `SiagroB1.Reports/Helpers/ContractPositionData.cs`
- Create: `SiagroB1.Application.Tests/Support/ContractPositionSeed.cs`
- Test: `SiagroB1.Application.Tests/Reports/ContractPositionTextTests.cs`
- Test: `SiagroB1.Application.Tests/Reports/ContractPositionDataTests.cs`

**Interfaces:**
- Consumes (já em `main`): `ReportText` (`Date`, `Partner`, `Product`, `Describe`, `JoinFilters`), `LogisticsReportText.StatusFilter`, `TestDb`; e, só nos testes, `PurchaseContractsRecalculateBalanceService.CalculateAllocatedAsync(AppDbContext, Guid)`, `PurchaseContractsWashedOutVolumeService.ActiveVolumesAsync(AppDbContext, Guid, Guid?)` e `SalesContractsRecalculateBalanceService.CalculateAllocatedAsync(AppDbContext, Guid)` (Application).
- Produces:
  - `abstract class ContractPositionReportRequest { List<ContractStatus>? Statuses; string? BranchCode; string? ItemCode; string? HarvestSeasonCode; string? CardCode; ContractType? Type; DateTime? DeliveryEndDateUntil; }`
  - `enum ContractPositionSide { Both = 0, Purchase = 1, Sales = 2 }`
  - `class ContractPosition` (Side, Key, Code, CreationDate, CardCode, CardName, ItemCode, ItemName, UnitOfMeasureCode, HarvestSeasonCode, Type, Status, StandardCashFlowDate, DeliveryEndDate, Price, Contracted, Delivered, WashedOut, Balance, `ToDeliver`).
  - `static class ContractPositionText`: `Overdue = "Vencido"`, `NoDeadline = "Sem prazo"`, `NoDeadlineBefore = 1901-01-01`, `EffectiveStatuses`, `QueryStatuses`, `StatusText`, `TypeText`, `TypeShort`, `SideText`, `HasDeadline`, `DeliveryEnd`, `StatusFilter`, `BuildFilters(DateTime today, ContractPositionReportRequest, ContractPositionSide? side, string? branchName, IReadOnlyCollection<ContractPosition>)`, `InSectionOrder`.
  - `static class ContractPositionData`: `PurchaseQuery`, `SalesQuery`, `PurchaseDeliveredQuery`, `PurchaseWashedOutQuery`, `SalesNominalQuery`, `SalesShortageQuery`, `PurchasePositionsAsync(AppDbContext, IQueryable<PurchaseContract>)`, `SalesPositionsAsync(AppDbContext, IQueryable<SalesContract>)`.
  - Test support `ContractPositionSeed`: `Today` (08/10/2026), `Purchase(...)`, `Sales(...)`, `PurchaseAllocation(...)`, `Washout(...)`, `SalesItem(...)`, `SalesAllocation(...)` (assinaturas no Step 3).

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Reports/ContractPositionTextTests.cs` (criar e `git add`):

```csharp
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

public class ContractPositionTextTests
{
    private sealed class Request : ContractPositionReportRequest;

    [Theory]
    [InlineData(ContractStatus.Draft, "Rascunho")]
    [InlineData(ContractStatus.Approved, "Aprovado")]
    [InlineData(ContractStatus.Finished, "Finalizado")]
    [InlineData(ContractStatus.Canceled, "Cancelado")]
    [InlineData(ContractStatus.InApproval, "Em Aprovação")]
    [InlineData(ContractStatus.Rejected, "Rejeitado")]
    public void StatusText_MatchesTheScreen(ContractStatus status, string expected) =>
        Assert.Equal(expected, ContractPositionText.StatusText(status));

    [Fact]
    public void StatusText_NullIsDraft() => Assert.Equal("Rascunho", ContractPositionText.StatusText(null));

    [Theory]
    [InlineData(ContractType.Fixed, "FIX - Preço Fixo", "FIX")]
    [InlineData(ContractType.ToBeDetermined, "PAF - Preço a Fixar", "PAF")]
    public void TypeText_MatchesTheScreen(ContractType type, string full, string shortText)
    {
        Assert.Equal(full, ContractPositionText.TypeText(type));
        Assert.Equal(shortText, ContractPositionText.TypeShort(type));
    }

    [Fact]
    public void EffectiveStatuses_EmptyMeansOnlyApproved()
    {
        Assert.Equal(new[] { ContractStatus.Approved }, ContractPositionText.EffectiveStatuses(null));
        Assert.Equal(new[] { ContractStatus.Approved }, ContractPositionText.EffectiveStatuses([]));
        Assert.Equal(
            new[] { ContractStatus.Finished, ContractStatus.Approved },
            ContractPositionText.EffectiveStatuses([ContractStatus.Finished, ContractStatus.Approved, ContractStatus.Finished]));
    }

    [Fact]
    public void QueryStatuses_AddsNullOnlyWhenDraftIsChosen()
    {
        Assert.DoesNotContain(null, ContractPositionText.QueryStatuses(null));
        Assert.Contains(null, ContractPositionText.QueryStatuses([ContractStatus.Draft]));
    }

    [Fact]
    public void StatusFilter_DescribesTheChoice()
    {
        Assert.Equal("Situação: Aprovado", ContractPositionText.StatusFilter(ContractPositionText.EffectiveStatuses(null)));
        Assert.Equal("Situação: todas", ContractPositionText.StatusFilter(Enum.GetValues<ContractStatus>()));
        Assert.Equal(
            "Situação: Aprovado, Finalizado",
            ContractPositionText.StatusFilter([ContractStatus.Finished, ContractStatus.Approved]));
    }

    [Fact]
    public void DeliveryEnd_IsEmptyWithoutDeadline()
    {
        Assert.Equal("31/12/2026", ContractPositionText.DeliveryEnd(new DateTime(2026, 12, 31)));
        Assert.Equal("", ContractPositionText.DeliveryEnd(default));
        Assert.Equal("", ContractPositionText.DeliveryEnd(new DateTime(1900, 1, 1)));
        Assert.False(ContractPositionText.HasDeadline(new DateTime(1900, 1, 1)));
    }

    [Fact]
    public void BuildFilters_ListsOnlyWhatWasInformed()
    {
        var positions = new List<ContractPosition>
        {
            new() { ItemCode = "10001", ItemName = "SOJA EM GRÃOS", CardCode = "F001", CardName = "PRODUTOR RURAL" },
        };

        Assert.Equal(
            "Posição em: 08/10/2026 | Lado: Compra e venda | Situação: Aprovado",
            ContractPositionText.BuildFilters(Today, new Request(), ContractPositionSide.Both, null, positions));

        var request = new Request
        {
            BranchCode = "01",
            ItemCode = "10001",
            HarvestSeasonCode = "25/26",
            CardCode = "F001",
            Type = ContractType.ToBeDetermined,
            DeliveryEndDateUntil = new DateTime(2026, 12, 31),
            Statuses = [ContractStatus.Approved, ContractStatus.Finished],
        };
        Assert.Equal(
            "Posição em: 08/10/2026 | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | Safra: 25/26 | " +
            "Parceiro: PRODUTOR RURAL | Tipo: PAF - Preço a Fixar | Término da entrega até: 31/12/2026 | " +
            "Situação: Aprovado, Finalizado",
            ContractPositionText.BuildFilters(Today, request, null, "MATRIZ", positions));

        Assert.Equal(
            "Posição em: 08/10/2026 | Lado: Venda | Filial: 01 | Produto: 10001 | Safra: 25/26 | Parceiro: F001 | " +
            "Tipo: PAF - Preço a Fixar | Término da entrega até: 31/12/2026 | Situação: Aprovado, Finalizado",
            ContractPositionText.BuildFilters(Today, request, ContractPositionSide.Sales, null, []));
    }

    [Fact]
    public void InSectionOrder_CashFlowThenDeadlineThenCode()
    {
        var positions = new List<ContractPosition>
        {
            new() { Code = "PC000005", StandardCashFlowDate = null, DeliveryEndDate = new DateTime(2026, 11, 30) },
            new() { Code = "PC000004", StandardCashFlowDate = new DateTime(2026, 11, 10), DeliveryEndDate = default },
            new() { Code = "PC000003", StandardCashFlowDate = new DateTime(2026, 11, 10), DeliveryEndDate = new DateTime(2026, 12, 31) },
            new() { Code = "PC000002", StandardCashFlowDate = new DateTime(2026, 11, 10), DeliveryEndDate = new DateTime(2026, 11, 30) },
            new() { Code = "PC000001", StandardCashFlowDate = new DateTime(2026, 11, 10), DeliveryEndDate = new DateTime(2026, 11, 30) },
            new() { Code = "PC000006", StandardCashFlowDate = new DateTime(2026, 10, 15), DeliveryEndDate = new DateTime(2027, 1, 31) },
        };

        Assert.Equal(
            new[] { "PC000006", "PC000001", "PC000002", "PC000003", "PC000004", "PC000005" },
            ContractPositionText.InSectionOrder(positions).Select(p => p.Code));
    }
}
```

`SiagroB1.Application.Tests/Reports/ContractPositionDataTests.cs` (criar e `git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// O saldo do relatório TEM de ser o AvaiableVolume do domínio depois de um recálculo. O Reports
/// não referencia o Application, então a soma é refeita lá em lote; estes testes rodam as duas
/// contas sobre o mesmo dado e exigem o mesmo número.
/// </summary>
public class ContractPositionDataTests
{
    private sealed class Request : ContractPositionReportRequest;

    // Review Focus 1 e 2: devolução negativa, washout em todos os status, persistido defasado.
    [Fact]
    public async Task PurchaseBalance_EqualsTheDomainAfterARecalc()
    {
        var db = TestDb.CreateUnitOfWork();
        var plain = Purchase("PC000001", total: 100_000m, staleAllocated: 999m, staleWashedOut: 999m);
        var withReturn = Purchase("PC000002", total: 50_000m);
        var withWashouts = Purchase("PC000003", total: 80_000m, type: ContractType.ToBeDetermined);
        var untouched = Purchase("PC000004", total: 10_000m);
        var overDelivered = Purchase("PC000005", total: 1_000m);
        db.Context.PurchaseContracts.AddRange(plain, withReturn, withWashouts, untouched, overDelivered);
        db.Context.PurchaseContractsAllocations.AddRange(
            PurchaseAllocation(plain, 30_000.125m),
            PurchaseAllocation(plain, 12_345.678m),
            PurchaseAllocation(withReturn, 20_000m),
            PurchaseAllocation(withReturn, -5_000m), // devolução (tipo 9) entra negativa
            PurchaseAllocation(withReturn, 0m),      // tipos 10/11 gravam 0
            PurchaseAllocation(withWashouts, 10_000m),
            PurchaseAllocation(overDelivered, 1_200m));
        db.Context.PurchaseContractsWashouts.AddRange(
            Washout(withWashouts, 1, 5_000m, 1_000m, PurchaseContractWashoutStatus.Approved),
            Washout(withWashouts, 2, 0m, 2_000m, PurchaseContractWashoutStatus.InApproval),
            Washout(withWashouts, 3, 7_000m, 0m, PurchaseContractWashoutStatus.Rejected),
            Washout(withWashouts, 4, 3_000m, 0m, PurchaseContractWashoutStatus.Reversed));
        await Save(db);

        var positions = await ContractPositionData.PurchasePositionsAsync(
            db.Context, ContractPositionData.PurchaseQuery(db.Context, new Request()));

        Assert.Equal(5, positions.Count);
        foreach (var position in positions)
        {
            var contract = await db.Context.PurchaseContracts.SingleAsync(c => c.Key == position.Key);
            contract.AllocatedVolume = await PurchaseContractsRecalculateBalanceService.CalculateAllocatedAsync(db.Context, contract.Key);
            contract.WashedOutVolume = (await PurchaseContractsWashedOutVolumeService.ActiveVolumesAsync(db.Context, contract.Key)).Total;

            Assert.Equal(contract.AllocatedVolume, position.Delivered);
            Assert.Equal(contract.WashedOutVolume, position.WashedOut);
            Assert.Equal(contract.AvaiableVolume, position.Balance);
        }

        Assert.Equal(15_000m, positions.Single(p => p.Code == "PC000002").Delivered);
        Assert.Equal(8_000m, positions.Single(p => p.Code == "PC000003").WashedOut);
        Assert.Equal(62_000m, positions.Single(p => p.Code == "PC000003").Balance);
        Assert.Equal(-200m, positions.Single(p => p.Code == "PC000005").Balance);
    }

    // Review Focus 3: quebra de entrega só da linha dona e só com o item conferido.
    [Fact]
    public async Task SalesBalance_EqualsTheDomainAfterARecalc()
    {
        var db = TestDb.CreateUnitOfWork();
        var owner = Sales("CV000001", total: 1_000m, staleAllocated: 999m);
        var partner = Sales("CV000002", total: 1_000m);
        var open = Sales("CV000003", total: 1_000m);
        var returned = Sales("CV000004", total: 1_000m);
        var closedItem = SalesItem(100m, closed: true, delivered: 92m, loss: 2.5m);
        var openItem = SalesItem(300m, delivered: 250m);
        var returnedItem = SalesItem(400m, closed: true, delivered: 400m);
        db.Context.SalesContracts.AddRange(owner, partner, open, returned);
        db.Context.SalesInvoicesItems.AddRange(closedItem, openItem, returnedItem);
        db.Context.SalesContractsAllocations.AddRange(
            SalesAllocation(owner, closedItem, 60m, owner: true),
            SalesAllocation(partner, closedItem, 40m, owner: false),
            SalesAllocation(open, openItem, 300m),
            SalesAllocation(returned, returnedItem, 400m),
            SalesAllocation(returned, returnedItem, -150m, owner: false)); // devolução
        await Save(db);

        var positions = await ContractPositionData.SalesPositionsAsync(
            db.Context, ContractPositionData.SalesQuery(db.Context, new Request()));

        Assert.Equal(4, positions.Count);
        foreach (var position in positions)
        {
            var contract = await db.Context.SalesContracts.SingleAsync(c => c.Key == position.Key);
            contract.AllocatedVolume = await SalesContractsRecalculateBalanceService.CalculateAllocatedAsync(db.Context, contract.Key);

            Assert.Equal(contract.AllocatedVolume, position.Delivered);
            Assert.Equal(contract.AvaiableVolume, position.Balance);
            Assert.Equal(0m, position.WashedOut);
        }

        Assert.Equal(49.5m, positions.Single(p => p.Code == "CV000001").Delivered); // 60 − (100 − 89,5)
        Assert.Equal(40m, positions.Single(p => p.Code == "CV000002").Delivered);
        Assert.Equal(300m, positions.Single(p => p.Code == "CV000003").Delivered);  // aberto: nominal
        Assert.Equal(250m, positions.Single(p => p.Code == "CV000004").Delivered);
    }

    [Fact]
    public async Task Positions_CarryTheContractSnapshots()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(Purchase("PC000001", price: 120.50m, cashFlow: new DateTime(2026, 11, 10),
            type: ContractType.ToBeDetermined));
        db.Context.SalesContracts.Add(Sales("CV000001", price: 130.25m));
        await Save(db);

        var purchase = (await ContractPositionData.PurchasePositionsAsync(
            db.Context, ContractPositionData.PurchaseQuery(db.Context, new Request()))).Single();
        var sale = (await ContractPositionData.SalesPositionsAsync(
            db.Context, ContractPositionData.SalesQuery(db.Context, new Request()))).Single();

        Assert.Equal(ContractPositionSide.Purchase, purchase.Side);
        Assert.Equal(120.50m, purchase.Price);
        Assert.Equal(new DateTime(2026, 11, 10), purchase.StandardCashFlowDate);
        Assert.Equal(ContractType.ToBeDetermined, purchase.Type);
        Assert.Equal("PRODUTOR RURAL", purchase.CardName);
        Assert.Equal("25/26", purchase.HarvestSeasonCode);
        Assert.Equal(100_000m, purchase.Balance);
        Assert.Equal(ContractPositionSide.Sales, sale.Side);
        Assert.Equal(130.25m, sale.Price);
        Assert.Equal("COOPERATIVA CENTRAL", sale.CardName);
    }

    [Fact]
    public async Task Query_DefaultStatusIsOnlyApproved()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001", status: ContractStatus.Approved),
            Purchase("PC000002", status: ContractStatus.Draft),
            Purchase("PC000003", status: null),
            Purchase("PC000004", status: ContractStatus.Finished),
            Purchase("PC000005", status: ContractStatus.Canceled));
        await Save(db);

        Assert.Equal(new[] { "PC000001" }, await PurchaseCodes(db, new Request()));
        Assert.Equal(
            new[] { "PC000002", "PC000003" },
            await PurchaseCodes(db, new Request { Statuses = [ContractStatus.Draft] }));
        Assert.Equal(
            new[] { "PC000001", "PC000004" },
            await PurchaseCodes(db, new Request { Statuses = [ContractStatus.Approved, ContractStatus.Finished] }));
    }

    [Theory]
    [InlineData("BranchCode")]
    [InlineData("ItemCode")]
    [InlineData("HarvestSeasonCode")]
    [InlineData("CardCode")]
    [InlineData("Type")]
    [InlineData("DeliveryEndDateUntil")]
    public async Task Query_EachOptionalFilterRestrictsBothSides(string filter)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001", deliveryEnd: new DateTime(2026, 11, 30)),
            Purchase("PC000002", branchCode: "99", itemCode: "20001", itemName: "MILHO", harvest: "26/27",
                cardCode: "F999", type: ContractType.ToBeDetermined, deliveryEnd: new DateTime(2026, 12, 1)));
        db.Context.SalesContracts.AddRange(
            Sales("CV000001", cardCode: "F001", deliveryEnd: new DateTime(2026, 11, 30, 18, 0, 0)),
            Sales("CV000002", branchCode: "99", itemCode: "20001", itemName: "MILHO", harvest: "26/27",
                cardCode: "C999", type: ContractType.ToBeDetermined, deliveryEnd: new DateTime(2026, 12, 1)));
        await Save(db);

        var request = new Request();
        switch (filter)
        {
            case "BranchCode": request.BranchCode = "01"; break;
            case "ItemCode": request.ItemCode = "10001"; break;
            case "HarvestSeasonCode": request.HarvestSeasonCode = "25/26"; break;
            case "CardCode": request.CardCode = "F001"; break;
            case "Type": request.Type = ContractType.Fixed; break;
            case "DeliveryEndDateUntil": request.DeliveryEndDateUntil = new DateTime(2026, 11, 30); break;
        }

        Assert.Equal(new[] { "PC000001" }, await PurchaseCodes(db, request));
        Assert.Equal(new[] { "CV000001" }, await SalesCodes(db, request));
    }

    // Review Focus 4: sem prazo não "termina até" data nenhuma.
    [Fact]
    public async Task Query_DeliveryEndUntilLeavesContractsWithoutDeadlineOut()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001", deliveryEnd: new DateTime(2026, 11, 30)),
            Purchase("PC000002", deliveryEnd: new DateTime(1900, 1, 1)));
        await Save(db);

        Assert.Equal(new[] { "PC000001", "PC000002" }, await PurchaseCodes(db, new Request()));
        Assert.Equal(
            new[] { "PC000001" },
            await PurchaseCodes(db, new Request { DeliveryEndDateUntil = new DateTime(2026, 12, 31) }));
    }

    /// <summary>
    /// O InMemory aceita LINQ que o SQL Server recusa (500 só em produção). Aqui as consultas
    /// passam pelo tradutor do SQL Server sem conexão: tem de sair GROUP BY e IN (SELECT …).
    /// </summary>
    [Fact]
    public void Queries_TranslateToSqlServer()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);
        var request = new Request
        {
            Statuses = [ContractStatus.Draft, ContractStatus.Approved],
            BranchCode = "01",
            ItemCode = "10001",
            HarvestSeasonCode = "25/26",
            CardCode = "F001",
            Type = ContractType.Fixed,
            DeliveryEndDateUntil = new DateTime(2026, 12, 31),
        };
        var purchaseKeys = ContractPositionData.PurchaseQuery(context, request).Select(c => c.Key);
        var salesKeys = ContractPositionData.SalesQuery(context, request).Select(c => c.Key);

        var sql = new[]
        {
            ContractPositionData.PurchaseQuery(context, request).ToQueryString(),
            ContractPositionData.SalesQuery(context, request).ToQueryString(),
            ContractPositionData.PurchaseDeliveredQuery(context, purchaseKeys).ToQueryString(),
            ContractPositionData.PurchaseWashedOutQuery(context, purchaseKeys).ToQueryString(),
            ContractPositionData.SalesNominalQuery(context, salesKeys).ToQueryString(),
            ContractPositionData.SalesShortageQuery(context, salesKeys).ToQueryString(),
        };

        Assert.All(sql[2..], s => Assert.Contains("GROUP BY", s, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("[SALES_INVOICES_ITEMS]", sql[5]);
        Assert.DoesNotContain(sql, s => s.Contains("[BUSINESS_PARTNERS]") || s.Contains("[ITEMS]"));
    }

    private static async Task<string[]> PurchaseCodes(IUnitOfWork db, ContractPositionReportRequest request) =>
        (await ContractPositionData.PurchaseQuery(db.Context, request).Select(c => c.Code!).ToListAsync())
        .Order(StringComparer.Ordinal).ToArray();

    private static async Task<string[]> SalesCodes(IUnitOfWork db, ContractPositionReportRequest request) =>
        (await ContractPositionData.SalesQuery(db.Context, request).Select(c => c.Code!).ToListAsync())
        .Order(StringComparer.Ordinal).ToArray();

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractPositionTextTests|FullyQualifiedName~ContractPositionDataTests"`
Expected: build FAIL — `ContractPositionReportRequest`, `ContractPositionSide`, `ContractPosition`, `ContractPositionText`, `ContractPositionData`, `ContractPositionSeed` not found.

- [ ] **Step 3: Implement**

`SiagroB1.Reports/Dtos/ContractPositionReportRequest.cs` (criar e `git add`):

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Filtros comuns aos relatórios de posição de contratos. Não há período: o relatório é a posição
/// no momento da emissão. Tudo é opcional; Situação vazia = só Aprovado.
/// </summary>
public abstract class ContractPositionReportRequest
{
    public List<ContractStatus>? Statuses { get; set; }

    public string? BranchCode { get; set; }

    public string? ItemCode { get; set; }

    public string? HarvestSeasonCode { get; set; }

    /// <summary>Parceiro: fornecedor no contrato de compra, cliente no de venda.</summary>
    public string? CardCode { get; set; }

    /// <summary>FIX (Fixed) ou PAF (ToBeDetermined); vazio = os dois.</summary>
    public ContractType? Type { get; set; }

    /// <summary>Término da entrega até (inclusive). Contrato sem prazo não passa neste filtro.</summary>
    public DateTime? DeliveryEndDateUntil { get; set; }
}
```

`SiagroB1.Reports/Dtos/ContractPositionSide.cs` (criar e `git add`):

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>Lado do relatório "Contratos — Compra x Venda". Viaja como número (sem conversor de enum por nome).</summary>
public enum ContractPositionSide
{
    Both = 0,
    Purchase = 1,
    Sales = 2,
}
```

`SiagroB1.Reports/Dtos/ContractPosition.cs` (criar e `git add`):

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Um contrato (de compra ou de venda) com o entregue e o saldo recalculados do razão no momento
/// da emissão — a mesma conta dos serviços de recálculo de saldo, nunca o AllocatedVolume
/// persistido. É a matéria-prima comum dos dois relatórios de posição.
/// </summary>
public class ContractPosition
{
    public ContractPositionSide Side { get; init; }
    public Guid Key { get; init; }
    public string Code { get; init; } = "";
    public DateTime? CreationDate { get; init; }
    public string CardCode { get; init; } = "";
    public string? CardName { get; init; }
    public string ItemCode { get; init; } = "";
    public string? ItemName { get; init; }
    public string UnitOfMeasureCode { get; init; } = "";
    public string HarvestSeasonCode { get; init; } = "";
    public ContractType Type { get; init; }
    public ContractStatus? Status { get; init; }
    public DateTime? StandardCashFlowDate { get; init; }
    public DateTime DeliveryEndDate { get; init; }
    public decimal Price { get; init; }

    /// <summary>Volume contratado (TotalVolume).</summary>
    public decimal Contracted { get; init; }

    /// <summary>Entregue pelo razão: compra = Σ alocações com sinal; venda = Σ alocações − quebra apurada.</summary>
    public decimal Delivered { get; init; }

    /// <summary>Washout ativo (em aprovação + aprovado). Sempre 0 na venda.</summary>
    public decimal WashedOut { get; init; }

    /// <summary>Saldo = o AvaiableVolume do domínio depois de um recálculo. Pode ser negativo.</summary>
    public decimal Balance { get; init; }

    /// <summary>
    /// Saldo que ainda vai ser entregue: o próprio saldo, ou 0 para contrato Finalizado (encerrar é
    /// abrir mão do não entregue), Cancelado ou Rejeitado.
    /// </summary>
    public decimal ToDeliver =>
        Status is ContractStatus.Finished or ContractStatus.Canceled or ContractStatus.Rejected ? 0m : Balance;
}
```

`SiagroB1.Reports/Helpers/ContractPositionText.cs` (criar e `git add`):

```csharp
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Textos pt-BR e regras comuns aos relatórios de posição de contratos. Situação e tipo usam OS
/// MESMOS rótulos de <c>siagro-b1-frontend/webapp/model/formatter.ts</c>
/// (<c>formatContractStatus</c>, <c>formatContractType</c>).
/// </summary>
public static class ContractPositionText
{
    public const string Overdue = "Vencido";
    public const string NoDeadline = "Sem prazo";

    /// <summary>
    /// DeliveryEndDate é NOT NULL no banco: contrato "sem prazo" só existe como data vazia
    /// (0001-01-01, ou 1900-01-01 vinda de carga legada). Tudo antes de 1901 é "sem prazo".
    /// </summary>
    public static readonly DateTime NoDeadlineBefore = new(1901, 1, 1);

    private static readonly ContractStatus[] DefaultStatuses = [ContractStatus.Approved];

    /// <summary>Situação vazia = só Aprovado (posição viva).</summary>
    public static ContractStatus[] EffectiveStatuses(IReadOnlyCollection<ContractStatus>? requested) =>
        requested is { Count: > 0 } ? requested.Distinct().ToArray() : DefaultStatuses;

    /// <summary>
    /// Array anulável para o EF traduzir o Contains em IN sobre a coluna anulável. Situação nula
    /// no banco é tratada como Rascunho (o padrão da entidade): entra quando Rascunho é pedido.
    /// </summary>
    public static ContractStatus?[] QueryStatuses(IReadOnlyCollection<ContractStatus>? requested)
    {
        var effective = EffectiveStatuses(requested);
        var query = effective.Select(s => (ContractStatus?)s).ToList();
        if (effective.Contains(ContractStatus.Draft))
            query.Add(null);
        return query.ToArray();
    }

    public static string StatusText(ContractStatus? status) => status switch
    {
        ContractStatus.Approved => "Aprovado",
        ContractStatus.Finished => "Finalizado",
        ContractStatus.Canceled => "Cancelado",
        ContractStatus.InApproval => "Em Aprovação",
        ContractStatus.Rejected => "Rejeitado",
        _ => "Rascunho",
    };

    public static string TypeText(ContractType type) =>
        type == ContractType.ToBeDetermined ? "PAF - Preço a Fixar" : "FIX - Preço Fixo";

    /// <summary>Sigla da coluna Tipo (a coluna é estreita; o rótulo inteiro vai na linha de filtros).</summary>
    public static string TypeShort(ContractType type) =>
        type == ContractType.ToBeDetermined ? "PAF" : "FIX";

    public static string SideText(ContractPositionSide side) => side switch
    {
        ContractPositionSide.Purchase => "Compra",
        ContractPositionSide.Sales => "Venda",
        _ => "Compra e venda",
    };

    public static bool HasDeadline(DateTime deliveryEndDate) => deliveryEndDate >= NoDeadlineBefore;

    /// <summary>Término da entrega; vazio quando o contrato não tem prazo.</summary>
    public static string DeliveryEnd(DateTime deliveryEndDate) =>
        HasDeadline(deliveryEndDate) ? ReportText.Date(deliveryEndDate) : "";

    public static string StatusFilter(IReadOnlyCollection<ContractStatus> effective) =>
        LogisticsReportText.StatusFilter(effective, s => StatusText(s), ContractStatus.Canceled);

    /// <summary>
    /// Linha de filtros: data da posição, lado (só no relatório 1), filtros informados e situação.
    /// Descrições de produto e parceiro saem das posições lidas (snapshots); sem resultado, o código.
    /// </summary>
    public static string BuildFilters(
        DateTime today,
        ContractPositionReportRequest request,
        ContractPositionSide? side,
        string? branchName,
        IReadOnlyCollection<ContractPosition> positions)
    {
        var parts = new List<string> { $"Posição em: {ReportText.Date(today)}" };

        if (side is { } chosenSide)
            parts.Add($"Lado: {SideText(chosenSide)}");

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            parts.Add($"Filial: {ReportText.Describe(branchName, request.BranchCode)}");

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
        {
            var product = positions.FirstOrDefault(p => p.ItemCode == request.ItemCode && !string.IsNullOrWhiteSpace(p.ItemName));
            parts.Add($"Produto: {(product is null ? request.ItemCode : ReportText.Product(product.ItemCode, product.ItemName))}");
        }

        if (!string.IsNullOrWhiteSpace(request.HarvestSeasonCode))
            parts.Add($"Safra: {request.HarvestSeasonCode}");

        if (!string.IsNullOrWhiteSpace(request.CardCode))
        {
            var partner = positions.FirstOrDefault(p => p.CardCode == request.CardCode);
            parts.Add($"Parceiro: {ReportText.Describe(partner?.CardName?.Trim(), request.CardCode)}");
        }

        if (request.Type is { } type)
            parts.Add($"Tipo: {TypeText(type)}");

        if (request.DeliveryEndDateUntil is { } until)
            parts.Add($"Término da entrega até: {ReportText.Date(until)}");

        parts.Add(StatusFilter(EffectiveStatuses(request.Statuses)));

        return ReportText.JoinFilters(parts);
    }

    /// <summary>
    /// Ordem dentro de uma seção: previsão de pagamento (sem previsão por último), término da
    /// entrega (sem prazo por último), código (ordinal).
    /// </summary>
    public static IOrderedEnumerable<ContractPosition> InSectionOrder(IEnumerable<ContractPosition> positions) =>
        positions
            .OrderBy(p => p.StandardCashFlowDate is null)
            .ThenBy(p => p.StandardCashFlowDate)
            .ThenBy(p => !HasDeadline(p.DeliveryEndDate))
            .ThenBy(p => p.DeliveryEndDate)
            .ThenBy(p => p.Code, StringComparer.Ordinal);
}
```

`SiagroB1.Reports/Helpers/ContractPositionData.cs` (criar e `git add`). Os nomes não terminam em `Service`, então o Scrutor não os registra — são estáticos de propósito:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Leitura dos contratos e do razão para os relatórios de posição. As fórmulas de entregue e saldo
/// são AS MESMAS dos serviços do SiagroB1.Application (que o Reports não pode referenciar):
/// <c>PurchaseContractsRecalculateBalanceService.CalculateAllocatedAsync</c>,
/// <c>PurchaseContractsWashedOutVolumeService.ActiveVolumesAsync</c> e
/// <c>SalesContractsRecalculateBalanceService.CalculateAllocatedAsync</c> — só que somadas em lote
/// (GROUP BY por contrato) em vez de uma consulta por contrato. <c>ContractPositionDataTests</c>
/// compara as duas contas sobre o mesmo dado: se a regra mudar lá, o teste daqui quebra.
/// Nada de JOIN com ITEMS/BUSINESS_PARTNERS (vazias em SAPB1): só snapshots do contrato.
/// </summary>
public static class ContractPositionData
{
    /// <summary>Soma por contrato (projeção traduzível para SQL).</summary>
    public sealed class KeyedSum
    {
        public Guid Key { get; init; }
        public decimal Sum { get; init; }
    }

    public static IQueryable<PurchaseContract> PurchaseQuery(AppDbContext context, ContractPositionReportRequest request)
    {
        var statuses = ContractPositionText.QueryStatuses(request.Statuses);
        var query = context.PurchaseContracts.AsNoTracking().Where(c => statuses.Contains(c.Status));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(c => c.BranchCode == request.BranchCode);
        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(c => c.ItemCode == request.ItemCode);
        if (!string.IsNullOrWhiteSpace(request.HarvestSeasonCode))
            query = query.Where(c => c.HarvestSeasonCode == request.HarvestSeasonCode);
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(c => c.CardCode == request.CardCode);
        if (request.Type is { } type)
            query = query.Where(c => c.Type == type);
        if (request.DeliveryEndDateUntil is { } until)
        {
            var untilExclusive = until.Date.AddDays(1);
            query = query.Where(c => c.DeliveryEndDate >= ContractPositionText.NoDeadlineBefore
                                     && c.DeliveryEndDate < untilExclusive);
        }

        return query;
    }

    public static IQueryable<SalesContract> SalesQuery(AppDbContext context, ContractPositionReportRequest request)
    {
        var statuses = ContractPositionText.QueryStatuses(request.Statuses);
        var query = context.SalesContracts.AsNoTracking().Where(c => statuses.Contains(c.Status));

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            query = query.Where(c => c.BranchCode == request.BranchCode);
        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            query = query.Where(c => c.ItemCode == request.ItemCode);
        if (!string.IsNullOrWhiteSpace(request.HarvestSeasonCode))
            query = query.Where(c => c.HarvestSeasonCode == request.HarvestSeasonCode);
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            query = query.Where(c => c.CardCode == request.CardCode);
        if (request.Type is { } type)
            query = query.Where(c => c.Type == type);
        if (request.DeliveryEndDateUntil is { } until)
        {
            var untilExclusive = until.Date.AddDays(1);
            query = query.Where(c => c.DeliveryEndDate >= ContractPositionText.NoDeadlineBefore
                                     && c.DeliveryEndDate < untilExclusive);
        }

        return query;
    }

    /// <summary>Σ Volume COM SINAL das alocações (devolução = negativo) — igual ao recálculo de compra.</summary>
    public static IQueryable<KeyedSum> PurchaseDeliveredQuery(AppDbContext context, IQueryable<Guid> contractKeys) =>
        context.PurchaseContractsAllocations
            .Where(a => contractKeys.Contains(a.PurchaseContractKey))
            .GroupBy(a => a.PurchaseContractKey)
            .Select(g => new KeyedSum { Key = g.Key, Sum = g.Sum(a => a.Volume) });

    /// <summary>Σ (fixado + não fixado) dos washouts ATIVOS (em aprovação + aprovado).</summary>
    public static IQueryable<KeyedSum> PurchaseWashedOutQuery(AppDbContext context, IQueryable<Guid> contractKeys) =>
        context.PurchaseContractsWashouts
            .Where(w => contractKeys.Contains(w.PurchaseContractKey)
                        && (w.Status == PurchaseContractWashoutStatus.InApproval
                            || w.Status == PurchaseContractWashoutStatus.Approved))
            .GroupBy(w => w.PurchaseContractKey)
            .Select(g => new KeyedSum { Key = g.Key, Sum = g.Sum(w => w.FixedVolume + w.UnfixedVolume) });

    /// <summary>Σ Volume COM SINAL das alocações de venda (nominal).</summary>
    public static IQueryable<KeyedSum> SalesNominalQuery(AppDbContext context, IQueryable<Guid> contractKeys) =>
        context.SalesContractsAllocations
            .Where(a => contractKeys.Contains(a.SalesContractKey))
            .GroupBy(a => a.SalesContractKey)
            .Select(g => new KeyedSum { Key = g.Key, Sum = g.Sum(a => a.Volume) });

    /// <summary>
    /// Quebra apurada: só a linha DONA da diferença, e só com a entrega do item conferida
    /// (Closed): Quantity − (DeliveredQuantity − QuantityLoss). Projeta antes de agrupar para o
    /// SQL Server traduzir a navegação.
    /// </summary>
    public static IQueryable<KeyedSum> SalesShortageQuery(AppDbContext context, IQueryable<Guid> contractKeys) =>
        context.SalesContractsAllocations
            .Where(a => contractKeys.Contains(a.SalesContractKey)
                        && a.OwnsDeliveryDifference
                        && a.SalesInvoiceItem!.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed)
            .Select(a => new
            {
                a.SalesContractKey,
                Shortage = a.SalesInvoiceItem!.Quantity
                           - (a.SalesInvoiceItem.DeliveredQuantity - a.SalesInvoiceItem.QuantityLoss),
            })
            .GroupBy(x => x.SalesContractKey)
            .Select(g => new KeyedSum { Key = g.Key, Sum = g.Sum(x => x.Shortage) });

    public static async Task<List<ContractPosition>> PurchasePositionsAsync(
        AppDbContext context, IQueryable<PurchaseContract> query)
    {
        var contracts = await query.ToListAsync();
        if (contracts.Count == 0)
            return [];

        var keys = query.Select(c => c.Key);
        var delivered = await ToDictionaryAsync(PurchaseDeliveredQuery(context, keys));
        var washedOut = await ToDictionaryAsync(PurchaseWashedOutQuery(context, keys));

        return contracts.Select(c =>
        {
            var contractDelivered = delivered.GetValueOrDefault(c.Key);
            var contractWashedOut = Round3(washedOut.GetValueOrDefault(c.Key));
            return new ContractPosition
            {
                Side = ContractPositionSide.Purchase,
                Key = c.Key,
                Code = c.Code ?? "",
                CreationDate = c.CreationDate,
                CardCode = c.CardCode,
                CardName = c.CardName,
                ItemCode = c.ItemCode,
                ItemName = c.ItemName,
                UnitOfMeasureCode = c.UnitOfMeasureCode,
                HarvestSeasonCode = c.HarvestSeasonCode,
                Type = c.Type,
                Status = c.Status,
                StandardCashFlowDate = c.StandardCashFlowDate,
                DeliveryEndDate = c.DeliveryEndDate,
                Price = c.StandardPrice,
                Contracted = c.TotalVolume,
                Delivered = contractDelivered,
                WashedOut = contractWashedOut,
                // = PurchaseContract.AvaiableVolume com AllocatedVolume e WashedOutVolume recalculados.
                Balance = decimal.Round(c.TotalVolume - contractDelivered - contractWashedOut, 2, MidpointRounding.ToEven),
            };
        }).ToList();
    }

    public static async Task<List<ContractPosition>> SalesPositionsAsync(
        AppDbContext context, IQueryable<SalesContract> query)
    {
        var contracts = await query.ToListAsync();
        if (contracts.Count == 0)
            return [];

        var keys = query.Select(c => c.Key);
        var nominal = await ToDictionaryAsync(SalesNominalQuery(context, keys));
        var shortage = await ToDictionaryAsync(SalesShortageQuery(context, keys));

        return contracts.Select(c =>
        {
            var contractDelivered = Round3(nominal.GetValueOrDefault(c.Key) - shortage.GetValueOrDefault(c.Key));
            return new ContractPosition
            {
                Side = ContractPositionSide.Sales,
                Key = c.Key,
                Code = c.Code ?? "",
                CreationDate = c.CreationDate,
                CardCode = c.CardCode,
                CardName = c.CardName,
                ItemCode = c.ItemCode,
                ItemName = c.ItemName,
                UnitOfMeasureCode = c.UnitOfMeasureCode,
                HarvestSeasonCode = c.HarvestSeasonCode,
                Type = c.Type,
                Status = c.Status,
                StandardCashFlowDate = c.StandardCashFlowDate,
                DeliveryEndDate = c.DeliveryEndDate,
                Price = c.Price,
                Contracted = c.TotalVolume,
                Delivered = contractDelivered,
                WashedOut = 0m,
                // = SalesContract.AvaiableVolume com AllocatedVolume recalculado.
                Balance = Round3(c.TotalVolume - contractDelivered),
            };
        }).ToList();
    }

    private static async Task<Dictionary<Guid, decimal>> ToDictionaryAsync(IQueryable<KeyedSum> query) =>
        (await query.ToListAsync()).ToDictionary(s => s.Key, s => s.Sum);

    private static decimal Round3(decimal value) => decimal.Round(value, 3, MidpointRounding.ToEven);
}
```

`SiagroB1.Application.Tests/Support/ContractPositionSeed.cs` (criar e `git add`). Atenção ao "sem prazo" nos testes: o parâmetro `deliveryEnd` é `DateTime?` com padrão 31/12/2026, então `deliveryEnd: default` vira **null** e cai no padrão — para "sem prazo" passe `deliveryEnd: DateTime.MinValue` (ou `new DateTime(1900, 1, 1)`), como os testes deste plano fazem:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Contratos e razão mínimos para os testes dos relatórios de posição de contratos.</summary>
public static class ContractPositionSeed
{
    /// <summary>"Hoje" fixo dos testes (a posição é calculada nesta data).</summary>
    public static readonly DateTime Today = new(2026, 10, 8);

    public static PurchaseContract Purchase(
        string code = "PC000001",
        decimal total = 100_000m,
        ContractStatus? status = ContractStatus.Approved,
        ContractType type = ContractType.Fixed,
        string cardCode = "F001",
        string cardName = "PRODUTOR RURAL",
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string uom = "KG",
        string harvest = "25/26",
        string branchCode = "01",
        decimal price = 120.50m,
        DateTime? cashFlow = null,
        DateTime? deliveryEnd = null,
        DateTime? creation = null,
        decimal staleAllocated = 0m,
        decimal staleWashedOut = 0m) => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CreationDate = creation ?? new DateTime(2026, 7, 1),
        Status = status,
        Type = type,
        BranchCode = branchCode,
        CardCode = cardCode,
        CardName = cardName,
        ItemCode = itemCode,
        ItemName = itemName,
        UnitOfMeasureCode = uom,
        HarvestSeasonCode = harvest,
        DeliveryLocationCode = "ARM01",
        DeliveryLocationName = "ARMAZÉM CENTRAL",
        DeliveryStartDate = new DateTime(2026, 7, 1),
        DeliveryEndDate = deliveryEnd ?? new DateTime(2026, 12, 31),
        StandardCashFlowDate = cashFlow,
        StandardPrice = price,
        TotalVolume = total,
        // Valores persistidos de propósito ERRADOS: o relatório nunca os lê.
        AllocatedVolume = staleAllocated,
        WashedOutVolume = staleWashedOut,
    };

    public static SalesContract Sales(
        string code = "CV000001",
        decimal total = 100_000m,
        ContractStatus? status = ContractStatus.Approved,
        ContractType type = ContractType.Fixed,
        string cardCode = "C001",
        string cardName = "COOPERATIVA CENTRAL",
        string itemCode = "10001",
        string itemName = "SOJA EM GRÃOS",
        string uom = "KG",
        string harvest = "25/26",
        string branchCode = "01",
        decimal price = 130.25m,
        DateTime? cashFlow = null,
        DateTime? deliveryEnd = null,
        DateTime? creation = null,
        decimal staleAllocated = 0m) => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CreationDate = creation ?? new DateTime(2026, 7, 1),
        Status = status,
        Type = type,
        BranchCode = branchCode,
        CardCode = cardCode,
        CardName = cardName,
        ItemCode = itemCode,
        ItemName = itemName,
        UnitOfMeasureCode = uom,
        HarvestSeasonCode = harvest,
        DeliveryStartDate = new DateTime(2026, 7, 1),
        DeliveryEndDate = deliveryEnd ?? new DateTime(2026, 12, 31),
        StandardCashFlowDate = cashFlow,
        Price = price,
        TotalVolume = total,
        AllocatedVolume = staleAllocated,
    };

    /// <summary>Linha do razão de compra (romaneio alocado). Devolução = volume negativo.</summary>
    public static PurchaseContractAllocation PurchaseAllocation(PurchaseContract contract, decimal volume) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contract.Key,
        StorageTransactionKey = Guid.NewGuid(),
        Volume = volume,
    };

    public static PurchaseContractWashout Washout(
        PurchaseContract contract,
        int sequence,
        decimal fixedVolume,
        decimal unfixedVolume = 0m,
        PurchaseContractWashoutStatus status = PurchaseContractWashoutStatus.Approved) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contract.Key,
        Sequence = sequence,
        FixedVolume = fixedVolume,
        UnfixedVolume = unfixedVolume,
        Status = status,
    };

    /// <summary>Item de nota de venda. Entrega conferida = Closed, com entregue e perda.</summary>
    public static SalesInvoiceItem SalesItem(
        decimal quantity,
        bool closed = false,
        decimal delivered = 0m,
        decimal loss = 0m,
        string itemCode = "10001",
        string uom = "KG") => new()
    {
        Key = Guid.NewGuid(),
        ItemCode = itemCode,
        UnitOfMeasureCode = uom,
        Quantity = quantity,
        DeliveredQuantity = delivered,
        QuantityLoss = loss,
        DeliveryStatus = closed ? SalesInvoiceDeliveryStatus.Closed : SalesInvoiceDeliveryStatus.Open,
    };

    /// <summary>Linha do razão de venda. Devolução = volume negativo; a dona carrega a quebra.</summary>
    public static SalesContractAllocation SalesAllocation(
        SalesContract contract, SalesInvoiceItem item, decimal volume, bool owner = true) => new()
    {
        Key = Guid.NewGuid(),
        SalesContractKey = contract.Key,
        SalesInvoiceItemKey = item.Key!.Value,
        Volume = volume,
        OwnsDeliveryDifference = owner,
        Origin = SalesContractAllocationOrigin.Billing,
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractPositionTextTests|FullyQualifiedName~ContractPositionDataTests"`
Expected: PASS (27 testes).

Se `PurchaseBalance_EqualsTheDomainAfterARecalc` ou `SalesBalance_EqualsTheDomainAfterARecalc` falhar, a regra do Application mudou depois deste plano: **ajuste `ContractPositionData` para reproduzir a regra atual do Application** (nunca o contrário) e registre no relatório da task. Se `Queries_TranslateToSqlServer` lançar `InvalidOperationException ... could not be translated`, a consulta não roda no SQL Server real (o InMemory esconde isso) — reescreva a projeção, não o teste.

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"`
Expected: PASS — inclusive todos os testes dos pacotes 1 e 2, sem alteração.

- [ ] **Step 5: Commit**

```bash
git branch --show-current   # feature/contract-position-reports
git add SiagroB1.Reports/Dtos/ContractPositionReportRequest.cs SiagroB1.Reports/Dtos/ContractPositionSide.cs SiagroB1.Reports/Dtos/ContractPosition.cs SiagroB1.Reports/Helpers/ContractPositionText.cs SiagroB1.Reports/Helpers/ContractPositionData.cs SiagroB1.Application.Tests/Support/ContractPositionSeed.cs SiagroB1.Application.Tests/Reports/ContractPositionTextTests.cs SiagroB1.Application.Tests/Reports/ContractPositionDataTests.cs
git commit -F - <<'EOF'
feat(reports): base dos relatórios de posição de contratos

Lê contratos de compra e venda só pelos snapshots e soma o razão em lote
(GROUP BY por contrato): entregue com sinal, washout ativo e quebra de
entrega da linha dona. A fórmula é a dos serviços de recálculo do
Application, que o Reports não referencia; um teste cruzado roda as duas
contas sobre o mesmo dado e exige o mesmo saldo.

Atenção: nunca ler AllocatedVolume/WashedOutVolume persistidos — derivam.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
EOF
```

---

### Task 2: Contratos — Compra x Venda

**Files:**
- Create: `SiagroB1.Reports/Dtos/ContractPositionRequest.cs`, `SiagroB1.Reports/Dtos/ContractPositionRowDto.cs`
- Create: `SiagroB1.Reports/Services/ContractPositionReportService.cs`
- Create: `SiagroB1.Reports/Controllers/ContractPositionController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/ContractPosition.frx`
- Test: `SiagroB1.Application.Tests/Reports/ContractPositionReportServiceTests.cs`
- Test: `SiagroB1.Application.Tests/Reports/ContractPositionReportsLayoutTests.cs` (cria a classe)
- Test: `SiagroB1.Application.Tests/Reports/ContractPositionReportsPdfTests.cs` (cria a classe)

**Interfaces:**
- Consumes: Task 1; pacotes 1/2: `InvoiceItemGrouping.BuildGroupResolver`, `ReportText`, `LogisticsReportText.BranchNameAsync`, `FastReportService.SingleUomParameter`, `RecordingFastReportService`, `TestDb`, `TestWebHostEnvironment`, `TestLogger`.
- Produces:
  - `ContractPositionReportService(IUnitOfWork db, IFastReportService reportService)` com `Task<List<ContractPositionRowDto>> BuildRowsAsync(ContractPositionRequest)`, `Task<byte[]> ExecuteAsync(ContractPositionRequest)`, `Task<byte[]> ExecuteAsync(ContractPositionRequest, DateTime today)`, `static string GeneralLabel(ContractPositionSide)`, constantes `PurchaseSection = "Compras"`, `SalesSection = "Vendas"`.
  - `POST /reports/ContractPosition` (consumido pela Task 5). Corpo: os filtros comuns + `Side` (número).
  - `ContractPositionReportsLayoutTests` (`Templates`, `LongestText`) e `ContractPositionReportsPdfTests` (`SeedMixedAsync`, `FastReport`, `Configuration`, `Save`, `Keep`) — a Task 3 acrescenta entradas.

- [ ] **Step 1: Write the failing service tests**

`SiagroB1.Application.Tests/Reports/ContractPositionReportServiceTests.cs` (criar e `git add`):

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

public class ContractPositionReportServiceTests
{
    [Fact]
    public async Task BuildRows_PurchasesThenSalesInsideEachProductAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesContracts.Add(Sales("CV000001"));
        db.Context.PurchaseContracts.Add(Purchase("PC000001"));
        db.Context.PurchaseContracts.Add(Purchase("PC000002", itemCode: "20001", itemName: "MILHO"));
        db.Context.SalesContracts.Add(Sales("CV000002", uom: "TN", total: 100m));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractPositionRequest());

        Assert.Equal(
            new[]
            {
                "MILHO (20001) - KG", "SOJA EM GRÃOS (10001) - KG", "SOJA EM GRÃOS (10001) - KG",
                "SOJA EM GRÃOS (10001) - TN",
            },
            rows.Select(r => r.Group));
        Assert.Equal(new[] { "PC000002", "PC000001", "CV000001", "CV000002" }, rows.Select(r => r.Code));
        Assert.Equal(new[] { "Compras", "Compras", "Vendas", "Vendas" }, rows.Select(r => r.Section));
    }

    [Fact]
    public async Task BuildRows_OrdersBySectionCashFlowThenDeadlineThenCode()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000003", cashFlow: null, deliveryEnd: new DateTime(2026, 11, 30)),
            Purchase("PC000002", cashFlow: new DateTime(2026, 11, 10), deliveryEnd: new DateTime(2026, 12, 31)),
            Purchase("PC000001", cashFlow: new DateTime(2026, 11, 10), deliveryEnd: new DateTime(2026, 11, 30)),
            Purchase("PC000004", cashFlow: new DateTime(2026, 10, 20), deliveryEnd: new DateTime(1900, 1, 1)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractPositionRequest());

        Assert.Equal(new[] { "PC000004", "PC000001", "PC000002", "PC000003" }, rows.Select(r => r.Code));
    }

    [Fact]
    public async Task BuildRows_FormatsTheColumns()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC000001", total: 100_000m, type: ContractType.ToBeDetermined, price: 120.50m,
            cashFlow: new DateTime(2026, 11, 10), deliveryEnd: new DateTime(2026, 12, 31),
            creation: new DateTime(2026, 7, 2, 15, 30, 0), staleAllocated: 1m, staleWashedOut: 1m);
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(purchase, 30_000m));
        db.Context.PurchaseContractsWashouts.Add(Washout(purchase, 1, 0m, 5_000m));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(new ContractPositionRequest())).Single();

        Assert.Equal("SOJA EM GRÃOS (10001) - KG", row.Group);
        Assert.Equal("Compras", row.Section);
        Assert.Equal("PC000001", row.Code);
        Assert.Equal("02/07/2026", row.CreationDate);
        Assert.Equal("(F001) PRODUTOR RURAL", row.Partner);
        Assert.Equal("25/26", row.HarvestSeason);
        Assert.Equal("PAF", row.Type);
        Assert.Equal("10/11/2026", row.CashFlowDate);
        Assert.Equal("31/12/2026", row.DeliveryEndDate);
        Assert.Equal("Aprovado", row.Status);
        Assert.Equal("KG", row.UnitOfMeasure);
        Assert.Equal(120.50m, row.Price);
        Assert.Equal(100_000m, row.ContractedQuantity);
        Assert.Equal(30_000m, row.DeliveredQuantity);
        Assert.Equal(5_000m, row.WashedOutQuantity);
        Assert.Equal(65_000m, row.BalanceQuantity);
        Assert.Equal(65_000m, row.SignedBalanceQuantity);
    }

    // Review Focus 4: contrato sem previsão de pagamento e sem prazo de entrega.
    [Fact]
    public async Task BuildRows_ContractWithoutCashFlowNorDeadlineKeepsTheRow()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesContracts.Add(Sales("CV000001", cashFlow: null, deliveryEnd: DateTime.MinValue));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(new ContractPositionRequest())).Single();

        Assert.Equal("", row.CashFlowDate);
        Assert.Equal("", row.DeliveryEndDate);
        Assert.Equal(100_000m, row.BalanceQuantity);
    }

    // Review Focus 3: venda com quebra de entrega em item conferido, e saldo negativo.
    [Fact]
    public async Task BuildRows_SalesBalanceUsesTheShortageAndMayBeNegative()
    {
        var db = TestDb.CreateUnitOfWork();
        var sale = Sales("CV000001", total: 100m, staleAllocated: 0m);
        var closed = SalesItem(120m, closed: true, delivered: 110m, loss: 0m);
        db.Context.SalesContracts.Add(sale);
        db.Context.SalesInvoicesItems.Add(closed);
        db.Context.SalesContractsAllocations.Add(SalesAllocation(sale, closed, 120m));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(new ContractPositionRequest())).Single();

        Assert.Equal(110m, row.DeliveredQuantity);
        Assert.Equal(-10m, row.BalanceQuantity);
        Assert.Equal(0m, row.WashedOutQuantity);
        Assert.Equal(10m, row.SignedBalanceQuantity); // ambos os lados: venda subtrai
    }

    [Theory]
    [InlineData(ContractPositionSide.Both, new[] { "PC000001", "CV000001" }, new[] { 70_000.0, -40_000.0 })]
    [InlineData(ContractPositionSide.Purchase, new[] { "PC000001" }, new[] { 70_000.0 })]
    [InlineData(ContractPositionSide.Sales, new[] { "CV000001" }, new[] { 40_000.0 })]
    public async Task BuildRows_SideChoosesTheSectionsAndTheSignOfTheGeneralBalance(
        ContractPositionSide side, string[] codes, double[] signed)
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC000001", total: 100_000m);
        var sale = Sales("CV000001", total: 50_000m);
        var item = SalesItem(10_000m);
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.SalesContracts.Add(sale);
        db.Context.SalesInvoicesItems.Add(item);
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(purchase, 30_000m));
        db.Context.SalesContractsAllocations.Add(SalesAllocation(sale, item, 10_000m));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractPositionRequest { Side = side });

        Assert.Equal(codes, rows.Select(r => r.Code));
        Assert.Equal(signed.Select(s => (decimal)s), rows.Select(r => r.SignedBalanceQuantity));
    }

    // Review Focus 5: contrato finalizado aparece só quando pedido, com o saldo do domínio.
    [Fact]
    public async Task BuildRows_FinishedContractOnlyWhenChosen()
    {
        var db = TestDb.CreateUnitOfWork();
        var finished = Purchase("PC000001", total: 100_000m, status: ContractStatus.Finished);
        db.Context.PurchaseContracts.Add(finished);
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(finished, 90_000m));
        await Save(db);

        Assert.Empty(await Service(db).BuildRowsAsync(new ContractPositionRequest()));

        var row = (await Service(db).BuildRowsAsync(
            new ContractPositionRequest { Statuses = [ContractStatus.Finished] })).Single();
        Assert.Equal("Finalizado", row.Status);
        Assert.Equal(10_000m, row.BalanceQuantity);
    }

    // Review Focus 2: washout em aprovação reserva volume; rejeitado e estornado não.
    [Fact]
    public async Task BuildRows_OnlyActiveWashoutsReduceThePurchaseBalance()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC000001", total: 100_000m, staleWashedOut: 50_000m);
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.PurchaseContractsWashouts.AddRange(
            Washout(purchase, 1, 1_000m, 0m, PurchaseContractWashoutStatus.InApproval),
            Washout(purchase, 2, 2_000m, 0m, PurchaseContractWashoutStatus.Approved),
            Washout(purchase, 3, 4_000m, 0m, PurchaseContractWashoutStatus.Rejected),
            Washout(purchase, 4, 8_000m, 0m, PurchaseContractWashoutStatus.Reversed));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(new ContractPositionRequest())).Single();

        Assert.Equal(3_000m, row.WashedOutQuantity);
        Assert.Equal(97_000m, row.BalanceQuantity);
    }

    // Review Focus 1: devolução de compra entra negativa no entregue.
    [Fact]
    public async Task BuildRows_PurchaseReturnReducesTheDelivered()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC000001", total: 100_000m);
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.PurchaseContractsAllocations.AddRange(
            PurchaseAllocation(purchase, 40_000m),
            PurchaseAllocation(purchase, -15_000m));
        await Save(db);

        var row = (await Service(db).BuildRowsAsync(new ContractPositionRequest())).Single();

        Assert.Equal(25_000m, row.DeliveredQuantity);
        Assert.Equal(75_000m, row.BalanceQuantity);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_FlagsWhetherTheResultHasASingleUnit(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(Purchase("PC000001"));
        if (withTonnes)
            db.Context.SalesContracts.Add(Sales("CV000001", uom: "TN", total: 100m));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new ContractPositionReportService(db, recorder).ExecuteAsync(new ContractPositionRequest(), Today);

        Assert.Equal("ContractPosition.frx", recorder.LastReportName);
        Assert.Equal(singleUom, recorder.LastParameters![FastReportService.SingleUomParameter]);
        Assert.Equal(withTonnes ? 2 : 1, ((ICollection<ContractPositionRowDto>)recorder.LastData!).Count);
    }

    [Theory]
    [InlineData(ContractPositionSide.Both, "Saldo geral (compra - venda):")]
    [InlineData(ContractPositionSide.Purchase, "Saldo geral (compra):")]
    [InlineData(ContractPositionSide.Sales, "Saldo geral (venda):")]
    public async Task Execute_LabelsTheGeneralBalanceBySide(ContractPositionSide side, string label)
    {
        var db = TestDb.CreateUnitOfWork();
        var recorder = new RecordingFastReportService();

        await new ContractPositionReportService(db, recorder)
            .ExecuteAsync(new ContractPositionRequest { Side = side }, Today);

        Assert.Equal(label, recorder.LastParameters!["pGeneralLabel"]);
    }

    [Fact]
    public async Task Execute_DescribesEveryFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        db.Context.PurchaseContracts.Add(Purchase("PC000001", type: ContractType.ToBeDetermined));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new ContractPositionReportService(db, recorder).ExecuteAsync(new ContractPositionRequest
        {
            Side = ContractPositionSide.Purchase,
            BranchCode = "01",
            ItemCode = "10001",
            HarvestSeasonCode = "25/26",
            CardCode = "F001",
            Type = ContractType.ToBeDetermined,
            DeliveryEndDateUntil = new DateTime(2026, 12, 31),
        }, Today);

        Assert.Equal(
            "Posição em: 08/10/2026 | Lado: Compra | Filial: MATRIZ | Produto: SOJA EM GRÃOS (10001) | " +
            "Safra: 25/26 | Parceiro: PRODUTOR RURAL | Tipo: PAF - Preço a Fixar | " +
            "Término da entrega até: 31/12/2026 | Situação: Aprovado",
            recorder.LastParameters!["pFilters"]);
    }

    private static ContractPositionReportService Service(IUnitOfWork db) => new(db, new RecordingFastReportService());

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractPositionReportServiceTests"`
Expected: build FAIL — `ContractPositionReportService`, `ContractPositionRequest`, `ContractPositionRowDto` not found.

- [ ] **Step 3: Implement DTOs, service and controller**

`SiagroB1.Reports/Dtos/ContractPositionRequest.cs` (criar e `git add`):

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Contratos — Compra x Venda". Lado vazio = Ambos.</summary>
public class ContractPositionRequest : ContractPositionReportRequest
{
    public ContractPositionSide Side { get; set; } = ContractPositionSide.Both;
}
```

`SiagroB1.Reports/Dtos/ContractPositionRowDto.cs` (criar e `git add`):

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Linha de "Contratos — Compra x Venda": um contrato. <see cref="Group"/> = produto + UM;
/// <see cref="Section"/> = "Compras" ou "Vendas". <see cref="SignedBalanceQuantity"/> alimenta o
/// saldo geral do bloco: com os dois lados, compra soma e venda subtrai; com um lado só, é o
/// próprio saldo.
/// </summary>
public class ContractPositionRowDto
{
    public string Group { get; set; } = "";
    public string Section { get; set; } = "";
    public string Code { get; set; } = "";
    public string CreationDate { get; set; } = "";
    public string Partner { get; set; } = "";
    public string HarvestSeason { get; set; } = "";
    public string Type { get; set; } = "";
    public string CashFlowDate { get; set; } = "";
    public string DeliveryEndDate { get; set; } = "";
    public string Status { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal Price { get; set; }
    public decimal ContractedQuantity { get; set; }
    public decimal DeliveredQuantity { get; set; }
    public decimal WashedOutQuantity { get; set; }
    public decimal BalanceQuantity { get; set; }
    public decimal SignedBalanceQuantity { get; set; }
}
```

`SiagroB1.Reports/Services/ContractPositionReportService.cs` (criar e `git add`):

```csharp
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// "Contratos — Compra x Venda": posição atual dos contratos por produto + UM, com a seção de
/// compras, a de vendas e o saldo geral do bloco (compra − venda). Entregue e saldo vêm do razão
/// recalculado na hora (<see cref="ContractPositionData"/>), nunca do AllocatedVolume persistido.
/// Só snapshots de PURCHASE_CONTRACTS/SALES_CONTRACTS e a tabela local BRANCHS: funciona nos dois modos.
/// </summary>
public class ContractPositionReportService(IUnitOfWork db, IFastReportService reportService)
{
    public const string PurchaseSection = "Compras";
    public const string SalesSection = "Vendas";

    public async Task<List<ContractPositionRowDto>> BuildRowsAsync(ContractPositionRequest request) =>
        ToRows(await LoadAsync(request), request.Side);

    public Task<byte[]> ExecuteAsync(ContractPositionRequest request) => ExecuteAsync(request, DateTime.Today);

    /// <summary><paramref name="today"/> só entra na linha de filtros ("Posição em").</summary>
    public async Task<byte[]> ExecuteAsync(ContractPositionRequest request, DateTime today)
    {
        var positions = await LoadAsync(request);
        var rows = ToRows(positions, request.Side);

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = ContractPositionText.BuildFilters(
                today,
                request,
                request.Side,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                positions),
            ["pGeneralLabel"] = GeneralLabel(request.Side),
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "ContractPosition.frx", rows, "ContractPositions", "ContractPositions", parameters);
    }

    public static string GeneralLabel(ContractPositionSide side) => side switch
    {
        ContractPositionSide.Purchase => "Saldo geral (compra):",
        ContractPositionSide.Sales => "Saldo geral (venda):",
        _ => "Saldo geral (compra - venda):",
    };

    private async Task<List<ContractPosition>> LoadAsync(ContractPositionRequest request)
    {
        var positions = new List<ContractPosition>();

        if (request.Side != ContractPositionSide.Sales)
            positions.AddRange(await ContractPositionData.PurchasePositionsAsync(
                db.Context, ContractPositionData.PurchaseQuery(db.Context, request)));

        if (request.Side != ContractPositionSide.Purchase)
            positions.AddRange(await ContractPositionData.SalesPositionsAsync(
                db.Context, ContractPositionData.SalesQuery(db.Context, request)));

        return positions;
    }

    private static List<ContractPositionRowDto> ToRows(List<ContractPosition> positions, ContractPositionSide side)
    {
        var groupOf = InvoiceItemGrouping.BuildGroupResolver(
            positions, p => p.ItemCode, p => p.ItemName, p => p.UnitOfMeasureCode);

        return positions
            .GroupBy(p => (Group: groupOf(p), p.Side))
            .OrderBy(g => g.Key.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.Key.Side) // Compras antes de Vendas
            .SelectMany(g => ContractPositionText.InSectionOrder(g).Select(p => ToRow(p, g.Key.Group, side)))
            .ToList();
    }

    private static ContractPositionRowDto ToRow(ContractPosition p, string group, ContractPositionSide side) => new()
    {
        Group = group,
        Section = p.Side == ContractPositionSide.Purchase ? PurchaseSection : SalesSection,
        Code = p.Code,
        CreationDate = ReportText.Date(p.CreationDate),
        Partner = ReportText.Partner(p.CardCode, p.CardName),
        HarvestSeason = p.HarvestSeasonCode,
        Type = ContractPositionText.TypeShort(p.Type),
        CashFlowDate = ReportText.Date(p.StandardCashFlowDate),
        DeliveryEndDate = ContractPositionText.DeliveryEnd(p.DeliveryEndDate),
        Status = ContractPositionText.StatusText(p.Status),
        UnitOfMeasure = p.UnitOfMeasureCode,
        Price = p.Price,
        ContractedQuantity = p.Contracted,
        DeliveredQuantity = p.Delivered,
        WashedOutQuantity = p.WashedOut,
        BalanceQuantity = p.Balance,
        // Saldo geral = compra − venda; com um lado só, o próprio saldo daquele lado.
        SignedBalanceQuantity = side == ContractPositionSide.Both && p.Side == ContractPositionSide.Sales
            ? -p.Balance
            : p.Balance,
    };
}
```

`SiagroB1.Reports/Controllers/ContractPositionController.cs` (criar e `git add`):

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>Sem período obrigatório: o relatório é a posição no momento da emissão.</summary>
[ApiController]
[Route("/reports/ContractPosition")]
public class ContractPositionController(ContractPositionReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] ContractPositionRequest request)
    {
        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"contract-position.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

O Scrutor registra o serviço por si mesmo (sufixo `Service`); não mexa em `DI/ServiceCollectionExtensions.cs`.

- [ ] **Step 4: Run service tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractPositionReportServiceTests"`
Expected: PASS.

- [ ] **Step 5: Template**

Crie `SiagroB1.Reports/Reports/Templates/ContractPosition.frx` exatamente com o conteúdo abaixo (e `git add`). Colunas:

| Coluna (cabeçalho) | Campo | Left | Largura | Formato | Por quê |
|---|---|---|---|---|---|
| Código | Code | 0 | 72 | texto | `PC2026000123` (12) ⇒ 70,4 |
| Emissão | CreationDate | 72 | 62 | texto | data (10) ⇒ 60,0 |
| Fornecedor / Cliente | Partner | 134 | 250 | texto, reticências | o que sobra |
| Safra | HarvestSeason | 384 | 60 | texto | VARCHAR(10) ⇒ 60,0 |
| Tipo | Type | 444 | 30 | texto | cabeçalho "Tipo" (4) ⇒ 28,8; dado FIX/PAF |
| Prev. pagto | CashFlowDate | 474 | 66 | texto | cabeçalho (11) ⇒ 65,2 |
| Entrega até | DeliveryEndDate | 540 | 66 | texto | cabeçalho (11) ⇒ 65,2 |
| Preço | Price | 606 | 72 | Number, 2 casas | `9.999.999,99` (12) ⇒ 70,4 |
| Contratado | ContractedQuantity | 678 | 82 | Number, 3 casas | (14) ⇒ 80,8 |
| Entregue | DeliveredQuantity | 760 | 82 | Number, 3 casas | (14) ⇒ 80,8 |
| Washout | WashedOutQuantity | 842 | 82 | Number, 3 casas, `HideZeros` | (14) ⇒ 80,8 |
| Saldo | BalanceQuantity | 924 | 88 | Number, 3 casas | `-10.000.000,000` (15): medido, 86 corta |
| Situação | Status | 1012 | 72 | texto | "Em Aprovação" (12) ⇒ 70,4 |
| **Soma** | | | **1084** | | |

Bandas: `GroupHeader1` (produto/UM) → `GroupHeader2` (seção "Compras"/"Vendas") → `Data1` → `GroupFooter2` ("Total Compras:" / "Total Vendas:" com Contratado, Entregue, Washout e Saldo) → `GroupFooter1` (`[pGeneralLabel]` + saldo geral do bloco, soma de `SignedBalanceQuantity`). Sumário: contagem de contratos, `uomMixedNote`, `uomSumLabel` + `uomSumSignedBalanceQuantity`. O rótulo "Total [ContractPositions.Section]:" do rodapé da seção usa o valor da última linha do grupo — conferido no PDF.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Report ScriptLanguage="CSharp" ReportInfo.Created="10/08/2026 12:00:00" ReportInfo.Modified="10/08/2026 12:00:00" ReportInfo.CreatorVersion="2026.1.0.0">
  <Styles>
    <Style Name="EvenRows" Fill.Color="Gainsboro" Font="Arial, 10pt"/>
  </Styles>
  <Dictionary>
    <BusinessObjectDataSource Name="ContractPositions" ReferenceName="ContractPositions" DataType="System.Int32" Enabled="true">
      <Column Name="Group" DataType="System.String"/>
      <Column Name="Section" DataType="System.String"/>
      <Column Name="Code" DataType="System.String"/>
      <Column Name="CreationDate" DataType="System.String"/>
      <Column Name="Partner" DataType="System.String"/>
      <Column Name="HarvestSeason" DataType="System.String"/>
      <Column Name="Type" DataType="System.String"/>
      <Column Name="CashFlowDate" DataType="System.String"/>
      <Column Name="DeliveryEndDate" DataType="System.String"/>
      <Column Name="Status" DataType="System.String"/>
      <Column Name="UnitOfMeasure" DataType="System.String"/>
      <Column Name="Price" DataType="System.Decimal"/>
      <Column Name="ContractedQuantity" DataType="System.Decimal"/>
      <Column Name="DeliveredQuantity" DataType="System.Decimal"/>
      <Column Name="WashedOutQuantity" DataType="System.Decimal"/>
      <Column Name="BalanceQuantity" DataType="System.Decimal"/>
      <Column Name="SignedBalanceQuantity" DataType="System.Decimal"/>
    </BusinessObjectDataSource>
    <Parameter Name="pCompanyName" DataType="System.String" AsString=""/>
    <Parameter Name="pFilters" DataType="System.String" AsString=""/>
    <Parameter Name="pGeneralLabel" DataType="System.String" AsString="Saldo geral (compra - venda):"/>
    <Parameter Name="pSingleUom" DataType="System.Boolean" AsString="true"/>
    <Total Name="TotalSecContractedQuantity" Expression="[ContractPositions.ContractedQuantity]" Evaluator="Data1" PrintOn="GroupFooter2"/>
    <Total Name="TotalSecDeliveredQuantity" Expression="[ContractPositions.DeliveredQuantity]" Evaluator="Data1" PrintOn="GroupFooter2"/>
    <Total Name="TotalSecWashedOutQuantity" Expression="[ContractPositions.WashedOutQuantity]" Evaluator="Data1" PrintOn="GroupFooter2"/>
    <Total Name="TotalSecBalanceQuantity" Expression="[ContractPositions.BalanceQuantity]" Evaluator="Data1" PrintOn="GroupFooter2"/>
    <Total Name="TotalGrpSignedBalanceQuantity" Expression="[ContractPositions.SignedBalanceQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalSumSignedBalanceQuantity" Expression="[ContractPositions.SignedBalanceQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalCountAll" TotalType="Count" Evaluator="Data1" PrintOn="ReportSummary1"/>
  </Dictionary>
  <ReportPage Name="Page1" Landscape="true" PaperWidth="297" PaperHeight="210" LeftMargin="5" TopMargin="5" RightMargin="5" BottomMargin="5" RawPaperSize="9" Watermark.Font="Arial, 60pt">
    <PageHeaderBand Name="PageHeader1" Width="1084" Height="113.4">
      <PictureObject Name="picLogo" Left="0" Top="2" Width="94.5" Height="37.8" SizeMode="Zoom"/>
      <TextObject Name="txtCompany" Left="100" Top="2" Width="700" Height="37.8" Text="[pCompanyName]" VertAlign="Center" Font="Arial, 11pt, style=Bold"/>
      <TextObject Name="txtDate" Left="937" Top="2" Width="147" Height="18.9" Text="[Date]" Format="Date" Format.Format="d" HorzAlign="Right" VertAlign="Center" Font="Tahoma, 6pt"/>
      <TextObject Name="txtTitle" Top="45" Width="1084" Height="28.35" Text="Contratos — Compra x Venda" HorzAlign="Center" VertAlign="Center" Font="Arial, 14pt, style=Bold, Italic"/>
      <TextObject Name="txtFilters" Top="75" Width="1084" Height="37.8" Text="[pFilters]" HorzAlign="Center" VertAlign="Top" WordWrap="true" Font="Consolas, 8pt, style=Italic"/>
    </PageHeaderBand>
    <ColumnHeaderBand Name="ColumnHeader1" Top="116.6" Width="1084" Height="17">
      <TextObject Name="hdrCode" Left="0" Width="72" Height="17" Text="Código" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrCreationDate" Left="72" Width="62" Height="17" Text="Emissão" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrPartner" Left="134" Width="250" Height="17" Text="Fornecedor / Cliente" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrHarvestSeason" Left="384" Width="60" Height="17" Text="Safra" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrType" Left="444" Width="30" Height="17" Text="Tipo" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrCashFlowDate" Left="474" Width="66" Height="17" Text="Prev. pagto" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrDeliveryEndDate" Left="540" Width="66" Height="17" Text="Entrega até" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrPrice" Left="606" Width="72" Height="17" Text="Preço" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrContractedQuantity" Left="678" Width="82" Height="17" Text="Contratado" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrDeliveredQuantity" Left="760" Width="82" Height="17" Text="Entregue" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrWashedOutQuantity" Left="842" Width="82" Height="17" Text="Washout" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrBalanceQuantity" Left="924" Width="88" Height="17" Text="Saldo" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrStatus" Left="1012" Width="72" Height="17" Text="Situação" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
    </ColumnHeaderBand>
    <GroupHeaderBand Name="GroupHeader1" SortOrder="None" Top="136.7" Width="1084" Height="20" Condition="[ContractPositions.Group]">
      <TextObject Name="txtGroup" Left="0" Top="2" Width="700" Height="17" Text="[ContractPositions.Group]" VertAlign="Center" WordWrap="false" Font="Arial, 8pt, style=Bold"/>
      <GroupHeaderBand Name="GroupHeader2" SortOrder="None" Top="159.75" Width="1084" Height="17" Condition="[ContractPositions.Section]">
        <TextObject Name="txtSection" Left="0" Width="300" Height="17" Text="[ContractPositions.Section]" VertAlign="Center" WordWrap="false" Font="Arial, 8pt, style=Bold, Italic"/>
        <DataBand Name="Data1" Top="179.85" Width="1084" Height="17" CanGrow="true" EvenStyle="EvenRows" DataSource="ContractPositions">
          <TextObject Name="txtCode" Left="0" Width="72" Height="17" Text="[ContractPositions.Code]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
          <TextObject Name="txtCreationDate" Left="72" Width="62" Height="17" Text="[ContractPositions.CreationDate]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
          <TextObject Name="txtPartner" Left="134" Width="250" Height="17" Text="[ContractPositions.Partner]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
          <TextObject Name="txtHarvestSeason" Left="384" Width="60" Height="17" Text="[ContractPositions.HarvestSeason]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
          <TextObject Name="txtType" Left="444" Width="30" Height="17" Text="[ContractPositions.Type]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
          <TextObject Name="txtCashFlowDate" Left="474" Width="66" Height="17" Text="[ContractPositions.CashFlowDate]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
          <TextObject Name="txtDeliveryEndDate" Left="540" Width="66" Height="17" Text="[ContractPositions.DeliveryEndDate]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
          <TextObject Name="txtPrice" Left="606" Width="72" Height="17" Text="[ContractPositions.Price]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="2" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
          <TextObject Name="txtContractedQuantity" Left="678" Width="82" Height="17" Text="[ContractPositions.ContractedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
          <TextObject Name="txtDeliveredQuantity" Left="760" Width="82" Height="17" Text="[ContractPositions.DeliveredQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
          <TextObject Name="txtWashedOutQuantity" Left="842" Width="82" Height="17" Text="[ContractPositions.WashedOutQuantity]" HideZeros="true" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
          <TextObject Name="txtBalanceQuantity" Left="924" Width="88" Height="17" Text="[ContractPositions.BalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
          <TextObject Name="txtStatus" Left="1012" Width="72" Height="17" Text="[ContractPositions.Status]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        </DataBand>
        <GroupFooterBand Name="GroupFooter2" Top="200" Width="1084" Height="20">
          <TextObject Name="txtSecLabel" Left="0" Top="2" Width="678" Height="17" Text="Total [ContractPositions.Section]:" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
          <TextObject Name="txtSecContractedQuantity" Left="678" Top="2" Width="82" Height="17" Text="[TotalSecContractedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
          <TextObject Name="txtSecDeliveredQuantity" Left="760" Top="2" Width="82" Height="17" Text="[TotalSecDeliveredQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
          <TextObject Name="txtSecWashedOutQuantity" Left="842" Top="2" Width="82" Height="17" Text="[TotalSecWashedOutQuantity]" HideZeros="true" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
          <TextObject Name="txtSecBalanceQuantity" Left="924" Top="2" Width="88" Height="17" Text="[TotalSecBalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        </GroupFooterBand>
      </GroupHeaderBand>
      <GroupFooterBand Name="GroupFooter1" Top="223" Width="1084" Height="22">
        <TextObject Name="txtGrpLabel" Left="0" Top="3" Width="924" Height="17" Text="[pGeneralLabel]" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
        <TextObject Name="txtGrpSignedBalanceQuantity" Left="924" Top="3" Width="88" Height="17" Text="[TotalGrpSignedBalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      </GroupFooterBand>
    </GroupHeaderBand>
    <ReportSummaryBand Name="ReportSummary1" Top="248" Width="1084" Height="48">
      <TextObject Name="txtSumLabel" Left="0" Top="4" Width="100" Height="17" Text="TOTAL GERAL:" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Bold"/>
      <TextObject Name="txtSumCount" Left="100" Top="4" Width="130" Height="17" Text="[TotalCountAll] contrato(s)" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="uomMixedNote" Left="232" Top="4" Width="400" Height="17" Text="UMs diferentes: saldo geral só por produto/UM." VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="uomSumLabel" Left="630" Top="4" Width="294" Height="17" Text="[pGeneralLabel]" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="uomSumSignedBalanceQuantity" Left="924" Top="4" Width="88" Height="17" Text="[TotalSumSignedBalanceQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="txtEmpty" Left="0" Top="26" Width="1084" Height="17" Text="[IIf([TotalCountAll] == 0, &quot;Nenhum registro encontrado.&quot;, &quot;&quot;)]" HorzAlign="Center" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Italic"/>
    </ReportSummaryBand>
    <PageFooterBand Name="PageFooter1" Top="299" Width="1084" Height="26">
      <TextObject Name="txtPage" Left="937" Width="147" Height="17" Text="Página [Page#] de [TotalPages#]" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
    </PageFooterBand>
  </ReportPage>
</Report>
```

- [ ] **Step 6: Layout tests**

`SiagroB1.Application.Tests/Reports/ContractPositionReportsLayoutTests.cs` (criar e `git add`) — nesta task só com o template `ContractPosition`; a Task 3 acrescenta o outro:

```csharp
using FastReport;
using FastReport.Format;
using FastReport.Utils;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Geometria dos relatórios de posição de contratos, sem renderizar — mesmas regras de
/// <see cref="LogisticsReportsLayoutTests"/> (medidas no PDF em 08/10): (5,2 px/char + 4 px de
/// margem de reticência) a 7pt, proporcional ao corpo, + 4 px de padding; cabeçalho não corta;
/// campos curtos cabem no maior valor real; totais em 6pt numa linha só; nada passa de 1084 px nem
/// se sobrepõe; grupos não são reordenados pelo FastReport. Aqui os saldos podem ser negativos.
/// </summary>
public class ContractPositionReportsLayoutTests
{
    private const float CharWidth = 5.2f;  // Consolas 7pt
    private const float TrimMargin = 4f;   // GDI+ ao aparar com reticências, a 7pt (medido)
    private const float Padding = 4f;
    private const float PageWidth = 1084f;

    public static TheoryData<string> Templates => new()
    {
        "ContractPosition",
    };

    /// <summary>Maior valor realista de cada campo de texto curto, em caracteres.</summary>
    private static readonly Dictionary<string, Dictionary<string, int>> LongestText = new()
    {
        ["ContractPosition"] = new()
        {
            ["txtCode"] = 12,            // PC2026000123
            ["txtCreationDate"] = 10,    // 31/12/2026
            ["txtHarvestSeason"] = 10,   // VARCHAR(10): 2025/2026
            ["txtType"] = 3,             // FIX / PAF
            ["txtCashFlowDate"] = 10,    // 31/12/2026
            ["txtDeliveryEndDate"] = 10, // 31/12/2026
            ["txtStatus"] = 12,          // Em Aprovação
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

    /// <summary>
    /// Medido no PDF em 08/10: "-10.000.000,000" (15 car.) a 7pt saiu "-10.000.000,0…" com 86 px
    /// (a conta dá 86,0 — a regra fica no limite) e coube com 88.
    /// </summary>
    private const float NegativeBalanceAt7pt = 88f;

    /// <summary>
    /// Saldo pode ser negativo (sem clamp): "-10.000.000,000" tem 15 caracteres e precisa caber no
    /// dado, no subtotal da seção, no saldo geral do bloco e no total.
    /// </summary>
    [Fact]
    public void ContractPosition_BalanceObjectsFitANegative15CharValue()
    {
        using var report = Load("ContractPosition");
        var balances = TextObjects(report)
            .Where(o => o.Name.EndsWith("BalanceQuantity", StringComparison.Ordinal))
            .ToList();
        float Need(TextObject o) => Math.Abs(o.Font.Size - 7f) < 0.01f
            ? Math.Max(Required(15, 7f), NegativeBalanceAt7pt)
            : Required(15, o.Font.Size);
        var problems = balances
            .Where(o => Need(o) > o.Width)
            .Select(o => $"{o.Name}: precisa {Need(o):0.#}px, tem {o.Width:0.#}px")
            .ToList();

        Assert.True(balances.Count >= 5, "ContractPosition: faltam objetos de Saldo (cabeçalho, dado, seção, bloco e total).");
        Assert.True(problems.Count == 0, "ContractPosition: " + string.Join("; ", problems));
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

    /// <summary>Washout é só da compra: zero (venda, ou compra sem washout) sai em branco, não "0,000".</summary>
    [Fact]
    public void ContractPosition_WashoutHidesZeros()
    {
        using var report = Load("ContractPosition");
        var washouts = TextObjects(report)
            .Where(o => o.Name.EndsWith("WashedOutQuantity", StringComparison.Ordinal) && o.Format is NumberFormat)
            .ToList();

        Assert.Equal(2, washouts.Count);
        Assert.All(washouts, o => Assert.True(o.HideZeros, $"{o.Name} sem HideZeros"));
    }
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractPositionReportsLayoutTests"`
Expected: PASS. Se algum falhar, corrija o `.frx` (não o teste) e rode de novo.

- [ ] **Step 7: PDF tests**

`SiagroB1.Application.Tests/Reports/ContractPositionReportsPdfTests.cs` (criar e `git add`):

```csharp
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Gera o PDF de ponta a ponta com o template real, nos dois modos (sem coluna fiscal: os dois
/// modos provam que nada depende de tabela do SAP). Pega fonte de dados com nome divergente, coluna
/// que o FastReport não converte e grupo aninhado quebrado. Grava em %TEMP%/siagro-contract-position-reports.
/// </summary>
public class ContractPositionReportsPdfTests : IDisposable
{
    private readonly string _contentRoot;

    public ContractPositionReportsPdfTests()
    {
        global::FastReport.Utils.RegisteredObjects.AddConnection(typeof(global::FastReport.Data.MsSqlDataConnection));
        global::FastReport.Utils.Config.WebMode = true;

        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-contract-position-pdf", Guid.NewGuid().ToString("N"));
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
    public async Task ContractPosition_ProducesAPdf(string erp)
    {
        var db = await SeedMixedAsync();

        var pdf = await new ContractPositionReportService(db, FastReport(Configuration(erp)))
            .ExecuteAsync(new ContractPositionRequest(), Today);

        Keep("ContractPosition", erp, pdf);
    }

    [Fact]
    public async Task ContractPosition_EmptyResultStillProducesAPdf()
    {
        var pdf = await new ContractPositionReportService(TestDb.CreateUnitOfWork(), FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new ContractPositionRequest(), Today);

        Assert.NotEmpty(pdf);
    }

    // Valores grandes e negativos, uma UM só (total geral visível): exercitam as larguras.
    [Fact]
    public async Task ContractPosition_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC2026000123", total: 99_999_999.999m, price: 9_999_999.99m,
            cardName: "COOPERATIVA AGROINDUSTRIAL DOS PRODUTORES RURAIS DO NORTE DO PARANÁ",
            status: ContractStatus.InApproval, harvest: "2025/2026", cashFlow: new DateTime(2026, 12, 31));
        var sale = Sales("CV2026000123", total: 10_000_000m, price: 9_999_999.99m);
        var item = SalesItem(20_000_000m);
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.SalesContracts.Add(sale);
        db.Context.SalesInvoicesItems.Add(item);
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(purchase, 88_888_888.888m));
        db.Context.PurchaseContractsWashouts.Add(Washout(purchase, 1, 9_999_999.999m));
        db.Context.SalesContractsAllocations.Add(SalesAllocation(sale, item, 20_000_000m)); // saldo −10.000.000
        await Save(db);

        var pdf = await new ContractPositionReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new ContractPositionRequest { Statuses = [ContractStatus.Approved, ContractStatus.InApproval] }, Today);

        Keep("ContractPosition-large", "STANDALONE", pdf);
    }

    /// <summary>Soja KG (compra com washout e venda com quebra), milho TN, um contrato sem prazo.</summary>
    private static async Task<IUnitOfWork> SeedMixedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC000001", total: 100_000m, cashFlow: new DateTime(2026, 11, 10),
            deliveryEnd: new DateTime(2026, 11, 30));
        var paf = Purchase("PC000002", total: 50_000m, type: ContractType.ToBeDetermined, deliveryEnd: new DateTime(2026, 9, 30));
        var sale = Sales("CV000001", total: 80_000m, deliveryEnd: new DateTime(2026, 12, 31));
        var noDeadline = Sales("CV000002", total: 10_000m, deliveryEnd: DateTime.MinValue, cardName: "AGRO NORTE");
        var corn = Purchase("PC000003", total: 300m, itemCode: "20001", itemName: "MILHO", uom: "TN");
        var item = SalesItem(30_000m, closed: true, delivered: 29_500m, loss: 100m);
        db.Context.PurchaseContracts.AddRange(purchase, paf, corn);
        db.Context.SalesContracts.AddRange(sale, noDeadline);
        db.Context.SalesInvoicesItems.Add(item);
        db.Context.PurchaseContractsAllocations.AddRange(
            PurchaseAllocation(purchase, 40_000m), PurchaseAllocation(purchase, -2_000m));
        db.Context.PurchaseContractsWashouts.Add(Washout(paf, 1, 0m, 5_000m));
        db.Context.SalesContractsAllocations.Add(SalesAllocation(sale, item, 30_000m));
        await Save(db);
        return db;
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
        var folder = Path.Combine(Path.GetTempPath(), "siagro-contract-position-reports");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, $"{report}-{erp}.pdf"), pdf);
    }
}
```

- [ ] **Step 8: Run all report tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"`
Expected: PASS (510 na pré-validação) — incluindo `ReportTemplateHeaderTests` e `ReportTemplateRenderSmokeTests`, que descobrem `ContractPosition.frx` sozinhos, e todos os dos pacotes 1 e 2.

- [ ] **Step 9: Conferência visual**

Abra com a ferramenta Read `%TEMP%/siagro-contract-position-reports/ContractPosition-STANDALONE.pdf`, `-SAPB1.pdf` e `ContractPosition-large-STANDALONE.pdf`. Confira: grupo "MILHO (20001) - TN" antes de "SOJA EM GRÃOS (10001) - KG"; dentro da soja, "Compras" (PC000001, PC000002) e depois "Vendas" (CV000001, CV000002); "Total Compras:"/"Total Vendas:" nos rodapés; Washout em branco na venda e só 5.000,000 no PC000002; CV000002 sem "Entrega até"; "Saldo geral (compra - venda):" 46.400,000 na soja; no PDF normal (KG + TN) a nota "UMs diferentes: saldo geral só por produto/UM." e **sem** saldo geral no total; no `-large` (só KG) o saldo geral 11.111.111,110 no total, `-10.000.000,000` inteiro na coluna Saldo, nome longo com reticências e "Em Aprovação" inteiro. Se algo cortar, ajuste o `.frx` mantendo o Appendix A e rode o Step 8 de novo.

- [ ] **Step 10: Build e commit**

Run: `dotnet build SiagroB1.sln` — Expected: 0 erros.

```bash
git branch --show-current   # feature/contract-position-reports
git add SiagroB1.Reports/Dtos/ContractPositionRequest.cs SiagroB1.Reports/Dtos/ContractPositionRowDto.cs SiagroB1.Reports/Services/ContractPositionReportService.cs SiagroB1.Reports/Controllers/ContractPositionController.cs SiagroB1.Reports/Reports/Templates/ContractPosition.frx SiagroB1.Application.Tests/Reports/ContractPositionReportServiceTests.cs SiagroB1.Application.Tests/Reports/ContractPositionReportsLayoutTests.cs SiagroB1.Application.Tests/Reports/ContractPositionReportsPdfTests.cs
git commit -F - <<'EOF'
feat(reports): relatório de contratos compra x venda

Posição atual por produto e UM: a seção de compras e a de vendas, cada
uma com contratado, entregue, washout e saldo recalculados do razão, e o
saldo geral do bloco (compra - venda; com um lado só, o daquele lado).
Total geral só com uma UM. Sem período: a posição é a da emissão.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
EOF
```

---

### Task 3: Posição Comprado x Vendido por Mês

**Files:**
- Create: `SiagroB1.Reports/Dtos/ContractMonthlyPositionRequest.cs`, `SiagroB1.Reports/Dtos/ContractMonthlyPositionRowDto.cs`
- Create: `SiagroB1.Reports/Services/ContractMonthlyPositionReportService.cs`
- Create: `SiagroB1.Reports/Controllers/ContractMonthlyPositionController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/ContractMonthlyPosition.frx`
- Test: `SiagroB1.Application.Tests/Reports/ContractMonthlyPositionReportServiceTests.cs`
- Modify: `SiagroB1.Application.Tests/Reports/ContractPositionReportsLayoutTests.cs`, `SiagroB1.Application.Tests/Reports/ContractPositionReportsPdfTests.cs`

**Interfaces:**
- Consumes: Task 1 (`ContractPositionData`, `ContractPositionText`, `ContractPosition.ToDeliver`, `ContractPositionSeed`), Task 2 (classes de teste de layout e PDF).
- Produces:
  - `ContractMonthlyPositionReportService(IUnitOfWork db, IFastReportService reportService)` com `BuildRowsAsync(ContractMonthlyPositionRequest)`, `BuildRowsAsync(ContractMonthlyPositionRequest, DateTime today)`, `ExecuteAsync(ContractMonthlyPositionRequest)`, `ExecuteAsync(ContractMonthlyPositionRequest, DateTime today)`.
  - `POST /reports/ContractMonthlyPosition` (consumido pela Task 5). Corpo: os filtros comuns.

- [ ] **Step 1: Write the failing service tests**

`SiagroB1.Application.Tests/Reports/ContractMonthlyPositionReportServiceTests.cs` (criar e `git add`):

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>"Hoje" nestes testes é <see cref="ContractPositionSeed.Today"/> = 08/10/2026.</summary>
public class ContractMonthlyPositionReportServiceTests
{
    [Fact]
    public async Task BuildRows_OverdueThenMonthsThenNoDeadline()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001", total: 1_000m, deliveryEnd: new DateTime(2026, 10, 7)),   // ontem: vencido
            Purchase("PC000002", total: 2_000m, deliveryEnd: new DateTime(2026, 10, 8)),   // hoje: 10/2026
            Purchase("PC000003", total: 4_000m, deliveryEnd: new DateTime(2026, 12, 15)),
            Purchase("PC000004", total: 8_000m, deliveryEnd: new DateTime(2026, 6, 30)),   // vencido
            Purchase("PC000005", total: 16_000m, deliveryEnd: DateTime.MinValue));                  // sem prazo
        db.Context.SalesContracts.AddRange(
            Sales("CV000001", total: 500m, deliveryEnd: new DateTime(2026, 12, 1)),
            Sales("CV000002", total: 300m, deliveryEnd: new DateTime(2027, 1, 31)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.Equal(new[] { "Vencido", "10/2026", "12/2026", "01/2027", "Sem prazo" }, rows.Select(r => r.Bucket));
        Assert.Equal(new[] { 9_000m, 2_000m, 4_000m, 0m, 16_000m }, rows.Select(r => r.PurchaseQuantity));
        Assert.Equal(new[] { 0m, 0m, 500m, 300m, 0m }, rows.Select(r => r.SalesQuantity));
        Assert.Equal(new[] { 9_000m, 2_000m, 3_500m, -300m, 16_000m }, rows.Select(r => r.NetQuantity));
        Assert.Equal(new[] { 9_000m, 11_000m, 14_500m, 14_200m, 30_200m }, rows.Select(r => r.AccumulatedQuantity));
    }

    [Fact]
    public async Task BuildRows_OverdueRowAlwaysOpensTheGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesContracts.Add(Sales("CV000001", total: 500m, deliveryEnd: new DateTime(2026, 11, 30)));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.Equal(new[] { "Vencido", "11/2026" }, rows.Select(r => r.Bucket));
        Assert.Equal(new[] { 0m, -500m }, rows.Select(r => r.AccumulatedQuantity));
    }

    [Fact]
    public async Task BuildRows_GroupHeaderCarriesContractedDeliveredAndWashout()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC000001", total: 100_000m, deliveryEnd: new DateTime(2026, 11, 30));
        var sale = Sales("CV000001", total: 60_000m, deliveryEnd: new DateTime(2026, 11, 30));
        var item = SalesItem(20_000m);
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.SalesContracts.Add(sale);
        db.Context.SalesInvoicesItems.Add(item);
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(purchase, 30_000m));
        db.Context.PurchaseContractsWashouts.Add(Washout(purchase, 1, 10_000m));
        db.Context.SalesContractsAllocations.Add(SalesAllocation(sale, item, 20_000m));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.All(rows, r =>
        {
            Assert.Equal("SOJA EM GRÃOS (10001) - Safra 25/26 - KG", r.Group);
            Assert.Equal(100_000m, r.ContractedPurchase);
            Assert.Equal(60_000m, r.ContractedSales);
            Assert.Equal(40_000m, r.ContractedNet);
            Assert.Equal(30_000m, r.DeliveredPurchase);
            Assert.Equal(20_000m, r.DeliveredSales);
            Assert.Equal(10_000m, r.DeliveredNet);
            Assert.Equal(10_000m, r.WashedOutPurchase);
        });
        var november = rows.Single(r => r.Bucket == "11/2026");
        Assert.Equal(60_000m, november.PurchaseQuantity); // 100.000 − 30.000 − 10.000
        Assert.Equal(40_000m, november.SalesQuantity);    // 60.000 − 20.000
        Assert.Equal(20_000m, november.NetQuantity);
    }

    [Fact]
    public async Task BuildRows_GroupsByProductHarvestAndUnit()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.AddRange(
            Purchase("PC000001"),
            Purchase("PC000002", harvest: "26/27"),
            Purchase("PC000003", uom: "TN", total: 100m),
            Purchase("PC000004", itemCode: "20001", itemName: "MILHO"));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.Equal(
            new[]
            {
                "MILHO (20001) - Safra 25/26 - KG",
                "SOJA EM GRÃOS (10001) - Safra 25/26 - KG",
                "SOJA EM GRÃOS (10001) - Safra 25/26 - TN",
                "SOJA EM GRÃOS (10001) - Safra 26/27 - KG",
            },
            rows.Select(r => r.Group).Distinct());
    }

    // Review Focus 3: saldo negativo entra como está e reduz o mês.
    [Fact]
    public async Task BuildRows_NegativeBalanceReducesTheMonth()
    {
        var db = TestDb.CreateUnitOfWork();
        var sale = Sales("CV000001", total: 100m, deliveryEnd: new DateTime(2026, 11, 30));
        var item = SalesItem(130m);
        db.Context.SalesContracts.AddRange(sale, Sales("CV000002", total: 50m, deliveryEnd: new DateTime(2026, 11, 15)));
        db.Context.SalesInvoicesItems.Add(item);
        db.Context.SalesContractsAllocations.Add(SalesAllocation(sale, item, 130m));
        await Save(db);

        var november = (await Service(db).BuildRowsAsync(new ContractMonthlyPositionRequest(), Today))
            .Single(r => r.Bucket == "11/2026");

        Assert.Equal(20m, november.SalesQuantity); // −30 + 50
    }

    // Review Focus 5: finalizado conta no contratado e no entregue, mas não tem mais o que entregar.
    [Fact]
    public async Task BuildRows_FinishedContractHasNothingLeftToDeliver()
    {
        var db = TestDb.CreateUnitOfWork();
        var finished = Purchase("PC000001", total: 1_000m, status: ContractStatus.Finished,
            deliveryEnd: new DateTime(2026, 11, 30));
        db.Context.PurchaseContracts.AddRange(finished,
            Purchase("PC000002", total: 500m, deliveryEnd: new DateTime(2026, 11, 30)));
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(finished, 900m));
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(
            new ContractMonthlyPositionRequest { Statuses = [ContractStatus.Approved, ContractStatus.Finished] }, Today);

        var november = rows.Single(r => r.Bucket == "11/2026");
        Assert.Equal(500m, november.PurchaseQuantity);
        Assert.Equal(1_500m, november.ContractedPurchase);
        Assert.Equal(900m, november.DeliveredPurchase);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Execute_FlagsWhetherTheResultHasASingleUnit(bool withTonnes, bool singleUom)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseContracts.Add(Purchase("PC000001"));
        db.Context.SalesContracts.Add(Sales("CV000001"));
        if (withTonnes)
            db.Context.SalesContracts.Add(Sales("CV000002", uom: "TN", total: 100m));
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new ContractMonthlyPositionReportService(db, recorder).ExecuteAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.Equal("ContractMonthlyPosition.frx", recorder.LastReportName);
        Assert.Equal(singleUom, recorder.LastParameters![FastReportService.SingleUomParameter]);
        Assert.Equal(withTonnes ? 3 : 2, recorder.LastParameters["pContractCount"]);
    }

    [Fact]
    public async Task Execute_DescribesTheFiltersWithoutSide()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        await Save(db);
        var recorder = new RecordingFastReportService();

        await new ContractMonthlyPositionReportService(db, recorder).ExecuteAsync(
            new ContractMonthlyPositionRequest { BranchCode = "01", CardCode = "C001" }, Today);

        Assert.Equal(
            "Posição em: 08/10/2026 | Filial: MATRIZ | Parceiro: C001 | Situação: Aprovado",
            recorder.LastParameters!["pFilters"]);
        Assert.Empty((ICollection<ContractMonthlyPositionRowDto>)recorder.LastData!);
    }

    private static ContractMonthlyPositionReportService Service(IUnitOfWork db) => new(db, new RecordingFastReportService());

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractMonthlyPositionReportServiceTests"`
Expected: build FAIL — `ContractMonthlyPositionReportService`, `ContractMonthlyPositionRequest`, `ContractMonthlyPositionRowDto` not found.

- [ ] **Step 3: Implement DTOs, service and controller**

`SiagroB1.Reports/Dtos/ContractMonthlyPositionRequest.cs` (criar e `git add`):

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Posição Comprado x Vendido por Mês": os comuns, sem Lado (os dois sempre).</summary>
public class ContractMonthlyPositionRequest : ContractPositionReportRequest;
```

`SiagroB1.Reports/Dtos/ContractMonthlyPositionRowDto.cs` (criar e `git add`):

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Linha de "Posição Comprado x Vendido por Mês": um prazo (Vencido, mm/aaaa ou Sem prazo) de um
/// grupo produto + safra + UM, com o saldo a entregar de compra e venda, o líquido e o líquido
/// acumulado desde o Vencido. Os campos Contracted*/Delivered*/WashedOut* são do GRUPO (repetidos
/// em todas as linhas dele) e saem no cabeçalho do grupo.
/// </summary>
public class ContractMonthlyPositionRowDto
{
    public string Group { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public string Bucket { get; set; } = "";
    public decimal ContractedPurchase { get; set; }
    public decimal ContractedSales { get; set; }
    public decimal ContractedNet { get; set; }
    public decimal DeliveredPurchase { get; set; }
    public decimal DeliveredSales { get; set; }
    public decimal DeliveredNet { get; set; }
    public decimal WashedOutPurchase { get; set; }
    public decimal PurchaseQuantity { get; set; }
    public decimal SalesQuantity { get; set; }
    public decimal NetQuantity { get; set; }
    public decimal AccumulatedQuantity { get; set; }
}
```

`SiagroB1.Reports/Services/ContractMonthlyPositionReportService.cs` (criar e `git add`):

```csharp
using System.Globalization;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// "Posição Comprado x Vendido por Mês": por produto + safra + UM, o contratado, o entregue e o
/// washout no cabeçalho do grupo, e o saldo a entregar distribuído pelo término da entrega:
/// Vencido (término antes de hoje), um mês por linha a partir do mês atual, e Sem prazo. O líquido
/// acumulado começa no Vencido. Saldo negativo entra como está (reduz o mês); contrato Finalizado,
/// Cancelado ou Rejeitado não tem mais o que entregar (<see cref="ContractPosition.ToDeliver"/>).
/// </summary>
public class ContractMonthlyPositionReportService(IUnitOfWork db, IFastReportService reportService)
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public Task<List<ContractMonthlyPositionRowDto>> BuildRowsAsync(ContractMonthlyPositionRequest request) =>
        BuildRowsAsync(request, DateTime.Today);

    /// <summary><paramref name="today"/> = data do servidor na emissão; os testes fixam a data.</summary>
    public async Task<List<ContractMonthlyPositionRowDto>> BuildRowsAsync(ContractMonthlyPositionRequest request, DateTime today) =>
        ToRows(await LoadAsync(request), today.Date);

    public Task<byte[]> ExecuteAsync(ContractMonthlyPositionRequest request) => ExecuteAsync(request, DateTime.Today);

    public async Task<byte[]> ExecuteAsync(ContractMonthlyPositionRequest request, DateTime today)
    {
        var positions = await LoadAsync(request);
        var rows = ToRows(positions, today.Date);

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = ContractPositionText.BuildFilters(
                today,
                request,
                side: null,
                await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                positions),
            ["pContractCount"] = positions.Count,
            [FastReportService.SingleUomParameter] = ReportText.IsSingleUnit(rows.Select(r => r.UnitOfMeasure)),
        };

        return await reportService.GeneratePdfAsync(
            "ContractMonthlyPosition.frx", rows, "ContractMonthlyPositions", "ContractMonthlyPositions", parameters);
    }

    private async Task<List<ContractPosition>> LoadAsync(ContractMonthlyPositionRequest request)
    {
        var positions = await ContractPositionData.PurchasePositionsAsync(
            db.Context, ContractPositionData.PurchaseQuery(db.Context, request));
        positions.AddRange(await ContractPositionData.SalesPositionsAsync(
            db.Context, ContractPositionData.SalesQuery(db.Context, request)));
        return positions;
    }

    private static List<ContractMonthlyPositionRowDto> ToRows(List<ContractPosition> positions, DateTime today)
    {
        // Sem a UM no resolver: o rótulo é "Produto (código) - Safra x - UM".
        var productOf = InvoiceItemGrouping.BuildGroupResolver(positions, p => p.ItemCode, p => p.ItemName, _ => null);

        return positions
            .GroupBy(p => $"{productOf(p)} - Safra {p.HarvestSeasonCode} - {p.UnitOfMeasureCode}")
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(g => GroupRows(g.Key, g.ToList(), today))
            .ToList();
    }

    private static IEnumerable<ContractMonthlyPositionRowDto> GroupRows(
        string group, List<ContractPosition> positions, DateTime today)
    {
        var purchases = positions.Where(p => p.Side == ContractPositionSide.Purchase).ToList();
        var sales = positions.Where(p => p.Side == ContractPositionSide.Sales).ToList();
        var contractedPurchase = purchases.Sum(p => p.Contracted);
        var contractedSales = sales.Sum(p => p.Contracted);
        var deliveredPurchase = purchases.Sum(p => p.Delivered);
        var deliveredSales = sales.Sum(p => p.Delivered);

        var buckets = new List<(string Label, List<ContractPosition> Items)>
        {
            // Vencido sai sempre: é onde o acumulado começa.
            (ContractPositionText.Overdue, positions
                .Where(p => ContractPositionText.HasDeadline(p.DeliveryEndDate) && p.DeliveryEndDate.Date < today)
                .ToList()),
        };

        buckets.AddRange(positions
            .Where(p => ContractPositionText.HasDeadline(p.DeliveryEndDate) && p.DeliveryEndDate.Date >= today)
            .GroupBy(p => new DateTime(p.DeliveryEndDate.Year, p.DeliveryEndDate.Month, 1))
            .OrderBy(m => m.Key)
            .Select(m => (m.Key.ToString("MM/yyyy", Culture), m.ToList())));

        var withoutDeadline = positions.Where(p => !ContractPositionText.HasDeadline(p.DeliveryEndDate)).ToList();
        if (withoutDeadline.Count > 0)
            buckets.Add((ContractPositionText.NoDeadline, withoutDeadline));

        var accumulated = 0m;
        foreach (var (label, items) in buckets)
        {
            var purchase = items.Where(p => p.Side == ContractPositionSide.Purchase).Sum(p => p.ToDeliver);
            var sale = items.Where(p => p.Side == ContractPositionSide.Sales).Sum(p => p.ToDeliver);
            accumulated += purchase - sale;

            yield return new ContractMonthlyPositionRowDto
            {
                Group = group,
                UnitOfMeasure = positions[0].UnitOfMeasureCode,
                Bucket = label,
                ContractedPurchase = contractedPurchase,
                ContractedSales = contractedSales,
                ContractedNet = contractedPurchase - contractedSales,
                DeliveredPurchase = deliveredPurchase,
                DeliveredSales = deliveredSales,
                DeliveredNet = deliveredPurchase - deliveredSales,
                WashedOutPurchase = purchases.Sum(p => p.WashedOut),
                PurchaseQuantity = purchase,
                SalesQuantity = sale,
                NetQuantity = purchase - sale,
                AccumulatedQuantity = accumulated,
            };
        }
    }
}
```

`SiagroB1.Reports/Controllers/ContractMonthlyPositionController.cs` (criar e `git add`):

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

/// <summary>Sem período obrigatório: o relatório é a posição no momento da emissão.</summary>
[ApiController]
[Route("/reports/ContractMonthlyPosition")]
public class ContractMonthlyPositionController(ContractMonthlyPositionReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] ContractMonthlyPositionRequest request)
    {
        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"contract-monthly-position.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

- [ ] **Step 4: Run service tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractMonthlyPositionReportServiceTests"`
Expected: PASS.

- [ ] **Step 5: Template**

Crie `SiagroB1.Reports/Reports/Templates/ContractMonthlyPosition.frx` exatamente com o conteúdo abaixo (e `git add`). Colunas (toda quantidade pode ser negativa; 210 px dá folga para `-999.999.999,999`):

| Coluna (cabeçalho) | Campo | Left | Largura | Formato |
|---|---|---|---|---|
| Prazo de entrega | Bucket | 0 | 244 | texto ("Vencido", "MM/aaaa", "Sem prazo") |
| Compra | PurchaseQuantity | 244 | 210 | Number, 3 casas |
| Venda | SalesQuantity | 454 | 210 | Number, 3 casas |
| Líquido (compra - venda) | NetQuantity | 664 | 210 | Number, 3 casas |
| Líquido acumulado | AccumulatedQuantity | 874 | 210 | Number, 3 casas |
| **Soma** | | | **1084** | |

O `GroupHeader1` (`KeepWithData="true"`) traz o título do grupo e três linhas com os campos do grupo: Contratado (compra, venda, líquido), Entregue (compra, venda, líquido) e "Washout (compra)" (compra e líquido) — o FastReport mostra no cabeçalho os valores da primeira linha do grupo, e o serviço repete esses campos em todas as linhas. `GroupFooter1`: "Saldo a entregar:" com as somas. Sumário: `[pContractCount] contrato(s)`, `uomMixedNote` e `uomSum*` das três somas.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Report ScriptLanguage="CSharp" ReportInfo.Created="10/08/2026 12:00:00" ReportInfo.Modified="10/08/2026 12:00:00" ReportInfo.CreatorVersion="2026.1.0.0">
  <Styles>
    <Style Name="EvenRows" Fill.Color="Gainsboro" Font="Arial, 10pt"/>
  </Styles>
  <Dictionary>
    <BusinessObjectDataSource Name="ContractMonthlyPositions" ReferenceName="ContractMonthlyPositions" DataType="System.Int32" Enabled="true">
      <Column Name="Group" DataType="System.String"/>
      <Column Name="UnitOfMeasure" DataType="System.String"/>
      <Column Name="Bucket" DataType="System.String"/>
      <Column Name="ContractedPurchase" DataType="System.Decimal"/>
      <Column Name="ContractedSales" DataType="System.Decimal"/>
      <Column Name="ContractedNet" DataType="System.Decimal"/>
      <Column Name="DeliveredPurchase" DataType="System.Decimal"/>
      <Column Name="DeliveredSales" DataType="System.Decimal"/>
      <Column Name="DeliveredNet" DataType="System.Decimal"/>
      <Column Name="WashedOutPurchase" DataType="System.Decimal"/>
      <Column Name="PurchaseQuantity" DataType="System.Decimal"/>
      <Column Name="SalesQuantity" DataType="System.Decimal"/>
      <Column Name="NetQuantity" DataType="System.Decimal"/>
      <Column Name="AccumulatedQuantity" DataType="System.Decimal"/>
    </BusinessObjectDataSource>
    <Parameter Name="pCompanyName" DataType="System.String" AsString=""/>
    <Parameter Name="pFilters" DataType="System.String" AsString=""/>
    <Parameter Name="pContractCount" DataType="System.Int32" AsString="0"/>
    <Parameter Name="pSingleUom" DataType="System.Boolean" AsString="true"/>
    <Total Name="TotalGrpPurchaseQuantity" Expression="[ContractMonthlyPositions.PurchaseQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpSalesQuantity" Expression="[ContractMonthlyPositions.SalesQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalGrpNetQuantity" Expression="[ContractMonthlyPositions.NetQuantity]" Evaluator="Data1" PrintOn="GroupFooter1"/>
    <Total Name="TotalSumPurchaseQuantity" Expression="[ContractMonthlyPositions.PurchaseQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumSalesQuantity" Expression="[ContractMonthlyPositions.SalesQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalSumNetQuantity" Expression="[ContractMonthlyPositions.NetQuantity]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalCountAll" TotalType="Count" Evaluator="Data1" PrintOn="ReportSummary1"/>
  </Dictionary>
  <ReportPage Name="Page1" Landscape="true" PaperWidth="297" PaperHeight="210" LeftMargin="5" TopMargin="5" RightMargin="5" BottomMargin="5" RawPaperSize="9" Watermark.Font="Arial, 60pt">
    <PageHeaderBand Name="PageHeader1" Width="1084" Height="113.4">
      <PictureObject Name="picLogo" Left="0" Top="2" Width="94.5" Height="37.8" SizeMode="Zoom"/>
      <TextObject Name="txtCompany" Left="100" Top="2" Width="700" Height="37.8" Text="[pCompanyName]" VertAlign="Center" Font="Arial, 11pt, style=Bold"/>
      <TextObject Name="txtDate" Left="937" Top="2" Width="147" Height="18.9" Text="[Date]" Format="Date" Format.Format="d" HorzAlign="Right" VertAlign="Center" Font="Tahoma, 6pt"/>
      <TextObject Name="txtTitle" Top="45" Width="1084" Height="28.35" Text="Posição Comprado x Vendido por Mês" HorzAlign="Center" VertAlign="Center" Font="Arial, 14pt, style=Bold, Italic"/>
      <TextObject Name="txtFilters" Top="75" Width="1084" Height="37.8" Text="[pFilters]" HorzAlign="Center" VertAlign="Top" WordWrap="true" Font="Consolas, 8pt, style=Italic"/>
    </PageHeaderBand>
    <ColumnHeaderBand Name="ColumnHeader1" Top="116.6" Width="1084" Height="17">
      <TextObject Name="hdrBucket" Left="0" Width="244" Height="17" Text="Prazo de entrega" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrPurchaseQuantity" Left="244" Width="210" Height="17" Text="Compra" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrSalesQuantity" Left="454" Width="210" Height="17" Text="Venda" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrNetQuantity" Left="664" Width="210" Height="17" Text="Líquido (compra - venda)" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrAccumulatedQuantity" Left="874" Width="210" Height="17" Text="Líquido acumulado" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
    </ColumnHeaderBand>
    <GroupHeaderBand Name="GroupHeader1" SortOrder="None" KeepWithData="true" Top="136.7" Width="1084" Height="70" Condition="[ContractMonthlyPositions.Group]">
      <TextObject Name="txtGroup" Left="0" Top="2" Width="1084" Height="17" Text="[ContractMonthlyPositions.Group]" VertAlign="Center" WordWrap="false" Font="Arial, 8pt, style=Bold"/>
      <TextObject Name="txtContractedLabel" Left="0" Top="20" Width="244" Height="15" Text="Contratado" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="txtContractedPurchase" Left="244" Top="20" Width="210" Height="15" Text="[ContractMonthlyPositions.ContractedPurchase]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="txtContractedSales" Left="454" Top="20" Width="210" Height="15" Text="[ContractMonthlyPositions.ContractedSales]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="txtContractedNet" Left="664" Top="20" Width="210" Height="15" Text="[ContractMonthlyPositions.ContractedNet]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="txtDeliveredLabel" Left="0" Top="35" Width="244" Height="15" Text="Entregue" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="txtDeliveredPurchase" Left="244" Top="35" Width="210" Height="15" Text="[ContractMonthlyPositions.DeliveredPurchase]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="txtDeliveredSales" Left="454" Top="35" Width="210" Height="15" Text="[ContractMonthlyPositions.DeliveredSales]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="txtDeliveredNet" Left="664" Top="35" Width="210" Height="15" Text="[ContractMonthlyPositions.DeliveredNet]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="txtWashedOutLabel" Left="0" Top="50" Width="244" Height="15" Text="Washout (compra)" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="txtWashedOutPurchase" Left="244" Top="50" Width="210" Height="15" Text="[ContractMonthlyPositions.WashedOutPurchase]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="txtWashedOutNet" Left="664" Top="50" Width="210" Height="15" Text="[ContractMonthlyPositions.WashedOutPurchase]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <DataBand Name="Data1" Top="209.9" Width="1084" Height="17" CanGrow="true" EvenStyle="EvenRows" DataSource="ContractMonthlyPositions">
        <TextObject Name="txtBucket" Left="0" Width="244" Height="17" Text="[ContractMonthlyPositions.Bucket]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtPurchaseQuantity" Left="244" Width="210" Height="17" Text="[ContractMonthlyPositions.PurchaseQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtSalesQuantity" Left="454" Width="210" Height="17" Text="[ContractMonthlyPositions.SalesQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtNetQuantity" Left="664" Width="210" Height="17" Text="[ContractMonthlyPositions.NetQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
        <TextObject Name="txtAccumulatedQuantity" Left="874" Width="210" Height="17" Text="[ContractMonthlyPositions.AccumulatedQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
      </DataBand>
      <GroupFooterBand Name="GroupFooter1" Top="230" Width="1084" Height="22">
        <TextObject Name="txtGrpLabel" Left="0" Top="3" Width="244" Height="17" Text="Saldo a entregar:" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpPurchaseQuantity" Left="244" Top="3" Width="210" Height="17" Text="[TotalGrpPurchaseQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpSalesQuantity" Left="454" Top="3" Width="210" Height="17" Text="[TotalGrpSalesQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtGrpNetQuantity" Left="664" Top="3" Width="210" Height="17" Text="[TotalGrpNetQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
      </GroupFooterBand>
    </GroupHeaderBand>
    <ReportSummaryBand Name="ReportSummary1" Top="255" Width="1084" Height="48">
      <TextObject Name="txtSumLabel" Left="0" Top="4" Width="100" Height="17" Text="TOTAL GERAL:" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Bold"/>
      <TextObject Name="txtSumCount" Left="100" Top="4" Width="140" Height="17" Text="[pContractCount] contrato(s)" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="uomMixedNote" Left="244" Top="4" Width="630" Height="17" Text="UMs diferentes: saldo a entregar só por produto/safra/UM." VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      <TextObject Name="uomSumPurchaseQuantity" Left="244" Top="4" Width="210" Height="17" Text="[TotalSumPurchaseQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumSalesQuantity" Left="454" Top="4" Width="210" Height="17" Text="[TotalSumSalesQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="uomSumNetQuantity" Left="664" Top="4" Width="210" Height="17" Text="[TotalSumNetQuantity]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold"/>
      <TextObject Name="txtEmpty" Left="0" Top="26" Width="1084" Height="17" Text="[IIf([TotalCountAll] == 0, &quot;Nenhum registro encontrado.&quot;, &quot;&quot;)]" HorzAlign="Center" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Italic"/>
    </ReportSummaryBand>
    <PageFooterBand Name="PageFooter1" Top="306" Width="1084" Height="26">
      <TextObject Name="txtPage" Left="937" Width="147" Height="17" Text="Página [Page#] de [TotalPages#]" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
    </PageFooterBand>
  </ReportPage>
</Report>
```

- [ ] **Step 6: Layout tests**

Em `SiagroB1.Application.Tests/Reports/ContractPositionReportsLayoutTests.cs`:

1. Em `Templates`, acrescente a linha `"ContractMonthlyPosition",` logo abaixo de `"ContractPosition",`.
2. Em `LongestText`, depois da entrada `["ContractPosition"] = new() { ... },`, acrescente:

```csharp
        ["ContractMonthlyPosition"] = new()
        {
            ["txtBucket"] = 9,           // Sem prazo
        },
```

3. Logo antes do método `Headers_FitTheirText` (a linha `[Theory]` que o precede), acrescente:

```csharp
    /// <summary>Na posição mensal TODA quantidade pode ser negativa: "-999.999.999,999" (16) cabe.</summary>
    [Fact]
    public void ContractMonthlyPosition_EveryQuantityFitsANegativeTotal()
    {
        using var report = Load("ContractMonthlyPosition");
        var problems = TextObjects(report)
            .Where(o => o.Format is NumberFormat)
            .Where(o => Required(16, o.Font.Size) > o.Width)
            .Select(o => $"{o.Name}: precisa {Required(16, o.Font.Size):0.#}px, tem {o.Width:0.#}px")
            .ToList();

        Assert.True(problems.Count == 0, "ContractMonthlyPosition: " + string.Join("; ", problems));
    }
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ContractPositionReportsLayoutTests"`
Expected: PASS.

- [ ] **Step 7: PDF tests**

Em `SiagroB1.Application.Tests/Reports/ContractPositionReportsPdfTests.cs`, logo antes do comentário `/// <summary>Soja KG (compra com washout e venda com quebra), milho TN, um contrato sem prazo.</summary>` (o `SeedMixedAsync`), acrescente:

```csharp
    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task ContractMonthlyPosition_ProducesAPdf(string erp)
    {
        var db = await SeedMixedAsync();

        var pdf = await new ContractMonthlyPositionReportService(db, FastReport(Configuration(erp)))
            .ExecuteAsync(new ContractMonthlyPositionRequest(), Today);

        Keep("ContractMonthlyPosition", erp, pdf);
    }

    [Fact]
    public async Task ContractMonthlyPosition_EmptyResultStillProducesAPdf()
    {
        var pdf = await new ContractMonthlyPositionReportService(TestDb.CreateUnitOfWork(), FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new ContractMonthlyPositionRequest(), Today);

        Assert.NotEmpty(pdf);
    }

    [Fact]
    public async Task ContractMonthlyPosition_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        foreach (var (code, month) in new[] { ("PC000001", 10), ("PC000002", 11), ("PC000003", 12) })
            db.Context.PurchaseContracts.Add(Purchase(code, total: 99_999_999.999m, deliveryEnd: new DateTime(2026, month, 28)));
        db.Context.SalesContracts.Add(Sales("CV000001", total: 99_999_999.999m, deliveryEnd: new DateTime(2026, 9, 30)));
        await Save(db);

        var pdf = await new ContractMonthlyPositionReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new ContractMonthlyPositionRequest(), Today);

        Keep("ContractMonthlyPosition-large", "STANDALONE", pdf);
    }
```

- [ ] **Step 8: Run all report tests + visual check**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SiagroB1.Application.Tests.Reports"`
Expected: PASS (534 na pré-validação).

Abra `%TEMP%/siagro-contract-position-reports/ContractMonthlyPosition-STANDALONE.pdf`, `-SAPB1.pdf` e `ContractMonthlyPosition-large-STANDALONE.pdf`. Confira no PDF normal: grupos "MILHO (20001) - Safra 25/26 - TN" e "SOJA EM GRÃOS (10001) - Safra 25/26 - KG"; na soja, Contratado 150.000,000 / 90.000,000 / 60.000,000, Entregue 38.000,000 / 29.400,000 / 8.600,000, Washout 5.000,000; linhas Vencido (45.000,000), 11/2026 (62.000,000), 12/2026 (venda 50.600,000), Sem prazo (venda 10.000,000) com acumulado 45.000,000 → 107.000,000 → 56.400,000 → 46.400,000; "Saldo a entregar:" 107.000,000 / 60.600,000 / 46.400,000; nota de UMs diferentes no total. No `-large`: números com sinal inteiros e o total geral visível (uma UM). (Os 100.000.000,000 de compra no `-large` são o arredondamento a 2 casas do domínio sobre 99.999.999,999 — esperado.)

- [ ] **Step 9: Build e commit**

Run: `dotnet build SiagroB1.sln` — Expected: 0 erros.

```bash
git branch --show-current   # feature/contract-position-reports
git add SiagroB1.Reports/Dtos/ContractMonthlyPositionRequest.cs SiagroB1.Reports/Dtos/ContractMonthlyPositionRowDto.cs SiagroB1.Reports/Services/ContractMonthlyPositionReportService.cs SiagroB1.Reports/Controllers/ContractMonthlyPositionController.cs SiagroB1.Reports/Reports/Templates/ContractMonthlyPosition.frx SiagroB1.Application.Tests/Reports/ContractMonthlyPositionReportServiceTests.cs SiagroB1.Application.Tests/Reports/ContractPositionReportsLayoutTests.cs SiagroB1.Application.Tests/Reports/ContractPositionReportsPdfTests.cs
git commit -F - <<'EOF'
feat(reports): relatório de posição comprado x vendido por mês

Por produto, safra e UM: contratado, entregue e washout no cabeçalho, e
o saldo a entregar de compra e venda distribuído pelo término da entrega
(Vencido, um mês por linha a partir do atual, Sem prazo), com o líquido
acumulado desde o Vencido. Contrato finalizado, cancelado ou rejeitado
não tem mais o que entregar; saldo negativo reduz o mês.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
EOF
```

---

### Task 4: Itens de menu

**Files:**
- Create: `SiagroB1.Migrations/CommonContext/<timestamp>_AddContractPositionReportMenus.cs` (+ `.Designer.cs` gerado)

**Interfaces:**
- Produces: chaves de menu `contractPositionReport` e `contractMonthlyPositionReport` (= nomes de rota da Task 5), sob o pai `reports`, `Order` 16 e 17.

- [ ] **Step 1: Confirmar a próxima ordem livre**

Run (Git Bash, em `siagro-b1-backend/`): `grep -rhn "\"reports\"" SiagroB1.Migrations/CommonContext/*.cs | grep -v Designer | grep -v Snapshot`
Expected: o maior `Order` sob `reports` é **15** (`salesShipmentsReport`, em `20261009014313_AddLogisticsReportMenus.cs`). Se aparecer algo maior, use os dois números seguintes no Step 3 e registre no relatório da task.

- [ ] **Step 2: Gerar a migration vazia**

Run (Git Bash): `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddContractPositionReportMenus --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir CommonContext`
Expected: cria `SiagroB1.Migrations/CommonContext/<timestamp>_AddContractPositionReportMenus.cs` e `.Designer.cs` com `Up`/`Down` vazios. A migration é só de dados: **`CommonDbContextModelSnapshot.cs` não pode mudar de conteúdo**. Na pré-validação o `git status` acusou o snapshot como modificado só por fim de linha (CRLF → LF): confira com `git diff SiagroB1.Migrations/CommonContext/CommonDbContextModelSnapshot.cs` — se sair vazio, descarte com `git checkout -- SiagroB1.Migrations/CommonContext/CommonDbContextModelSnapshot.cs`; se sair diferença de conteúdo, pare: há drift de modelo não relacionado, investigue antes de seguir. `git add` os dois arquivos novos.

- [ ] **Step 3: Preencher Up/Down**

Substitua os métodos `Up` e `Down` gerados por:

```csharp
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // Os relatórios antigos de contratos (PurchaseContractsByItem/SalesContractsByItem)
            // continuam no menu.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "contractPositionReport", "Contratos — Compra x Venda", "sap-icon://folder-blank", true, false, 16, "reports", false },
                    { "contractMonthlyPositionReport", "Posição Comprado x Vendido por Mês", "sap-icon://folder-blank", true, false, 17, "reports", false },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "3E8B1C47-6A2D-4F95-B7C1-2D9E5A8F0B01", "ADMIN", "contractPositionReport" },
                    { "3E8B1C47-6A2D-4F95-B7C1-2D9E5A8F0B02", "ADMIN", "contractMonthlyPositionReport" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues: ["3E8B1C47-6A2D-4F95-B7C1-2D9E5A8F0B01", "3E8B1C47-6A2D-4F95-B7C1-2D9E5A8F0B02"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["contractPositionReport", "contractMonthlyPositionReport"]);
        }
```

É a mesma forma de `20261009014313_AddLogisticsReportMenus.cs` (colunas, `Id` string, `StandaloneOnly`). `MENU_ITEMS.Title` é `VARCHAR(100)`: o travessão "—" do título existe na página de código 1252 das bases (como o "ç" e o "õ" dos outros itens) — a Task 6 confere no menu.

- [ ] **Step 4: Validar com `migrations script` (sem tocar em banco)**

**Não rode `dotnet ef database update`.** Gere o SQL das duas direções:

Run (Git Bash): `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations script AddLogisticsReportMenus AddContractPositionReportMenus --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`
Expected: um `INSERT INTO [MENU_ITEMS] ([Key], [Title], [Icon], [Enabled], [Expanded], [Order], [ParentKey], [StandaloneOnly])` com `('contractPositionReport', 'Contratos — Compra x Venda', ..., 16, 'reports', CAST(0 AS bit))` e `('contractMonthlyPositionReport', 'Posição Comprado x Vendido por Mês', ..., 17, 'reports', CAST(0 AS bit))`, um `INSERT INTO [ROLE_MENUS]` com as 2 linhas ADMIN e o `INSERT INTO [__EFMigrationsHistory]` de `<timestamp>_AddContractPositionReportMenus`. Nenhum `CREATE`/`ALTER`.

Run (Git Bash): `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations script AddContractPositionReportMenus AddLogisticsReportMenus --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build`
Expected: `DELETE FROM [ROLE_MENUS]` dos 2 Ids, `DELETE FROM [MENU_ITEMS]` das 2 chaves e o `DELETE` do histórico.

- [ ] **Step 5: Build e commit**

Run: `dotnet build SiagroB1.sln` — Expected: 0 erros.

```bash
git branch --show-current   # feature/contract-position-reports
git status --short SiagroB1.Migrations   # só os dois arquivos novos
git add SiagroB1.Migrations/CommonContext/*_AddContractPositionReportMenus*.cs
git commit -F - <<'EOF'
feat(reports): itens de menu dos relatórios de posição de contratos

Contratos — Compra x Venda e Posição Comprado x Vendido por Mês sob
Relatórios, liberados para o perfil ADMIN.

DB: AddContractPositionReportMenus
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
EOF
```

---

### Task 5: Frontend — duas telas de filtro

Repo: `siagro-b1-frontend` (branch `feature/contract-position-reports` já existe; confira `git -C ../siagro-b1-frontend branch --show-current` e que a árvore está limpa com `git status`). Todos os caminhos abaixo são relativos a `siagro-b1-frontend/`.

**Files:**
- Modify: `webapp/model/ServerRoutes.ts`
- Create: `webapp/view/reports/fragments/ContractPositionStatusFilters.fragment.xml`
- Create: `webapp/view/reports/fragments/ContractPositionFilters.fragment.xml`
- Create: `webapp/controller/reports/ContractPositionReportController.ts`
- Create: `webapp/controller/reports/{contractPosition,contractMonthlyPosition}/Main.controller.ts`
- Create: `webapp/view/reports/{contractPosition,contractMonthlyPosition}/Main.view.xml`
- Modify: `webapp/manifest.json` (rotas + targets)

**Interfaces:**
- Consumes: endpoints das Tasks 2–3; chaves de menu da Task 4; `InvoiceReportController` (sem alteração: `routeName`/`formId`/`serverRoute` abstratos, `defaults()`, `buildPayload()`, `onPrintReport`); fragmento existente `InvoiceReportCommonFilters` (Filial + Produto); `CommonController.openHarvestSeasonsValueHelp` e `openBusinessPartnersValueHelp`.
- Produces: rotas `contractPositionReport` (`contract-position/report`) e `contractMonthlyPositionReport` (`contract-monthly-position/report`).

Antes de começar: invoque o skill `ui5:ui5-best-practices`. Regras que valem para as duas telas:

- **Sem período e sem campo obrigatório.** O `onPrintReport` do base chama `validateForm(formId)`, e o `validateForm` do `BaseController` só barra `Input`/`Select`/`ComboBox`/`DatePicker` com `required="true"`. Nenhum controle destas telas é `required`, então o base serve sem alteração — não acrescente `required` em nada.
- **Enums viajam como números**: o `SiagroB1.Reports` não registra `JsonStringEnumConverter`. As chaves do `MultiComboBox`/`Select` são os números em string; o `buildPayload` do base converte `Statuses`, o `ContractPositionReportController` converte `Type` e a tela 1 converte `Side`.
- A situação padrão vem do `defaults()`: o `routeMatched` do base faz `{ Statuses: ["0","1","3"], NfeStatuses: [], ...this.defaults() }`, então o `Statuses: ["1"]` (só Aprovado) do `defaults()` sobrescreve o das notas. `NfeStatuses: []`, `BranchName`, `ItemName` e `CardName` chegam ao backend e são ignorados (propriedades desconhecidas).
- Layout igual ao das telas dos pacotes 1 e 2 (`SimpleForm` `ResponsiveGridLayout` em duas colunas).

- [ ] **Step 1: ServerRoutes**

Em `webapp/model/ServerRoutes.ts`, logo abaixo da linha `  salesShipmentsByPeriodReport: '/reports/SalesShipmentsByPeriod',`, acrescente:

```ts
  contractPositionReport: '/reports/ContractPosition',
  contractMonthlyPositionReport: '/reports/ContractMonthlyPosition',
```

- [ ] **Step 2: Fragmentos**

`webapp/view/reports/fragments/ContractPositionStatusFilters.fragment.xml` (criar e `git add`) — Situação (padrão Aprovado, as 6 oferecidas) e "Término da entrega até" opcional; o tipo do `DatePicker` vem por `core:require` (o `ui5lint` acusa o global `sap.ui.model...` dos fragmentos antigos):

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core">
	<Label text="Situação"/>
	<MultiComboBox selectedKeys="{params>/Statuses}">
		<core:Item key="0" text="Rascunho"/>
		<core:Item key="4" text="Em Aprovação"/>
		<core:Item key="1" text="Aprovado"/>
		<core:Item key="2" text="Finalizado"/>
		<core:Item key="5" text="Rejeitado"/>
		<core:Item key="3" text="Cancelado"/>
	</MultiComboBox>
	<Label text="Término da entrega até"/>
	<DatePicker
		core:require="{ DateTimeOffset: 'sap/ui/model/odata/type/DateTimeOffset' }"
		value="{ path: 'params>/DeliveryEndDateUntil', type: 'DateTimeOffset', constraints: { precision: 7 }, formatOptions: { pattern: 'dd/MM/yyyy' } }"/>
</core:FragmentDefinition>
```

`webapp/view/reports/fragments/ContractPositionFilters.fragment.xml` (criar e `git add`) — Safra, Parceiro (de qualquer tipo: fornecedor na compra, cliente na venda) e Tipo:

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:l="sap.ui.layout">
	<Label text="Safra"/>
	<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openHarvestSeasonsValueHelp" value="{params>/HarvestSeasonCode}">
		<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
	</Input>
	<Label text="Parceiro"/>
	<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openBusinessPartnersValueHelp" value="{params>/CardCode}">
		<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
		<customData>
			<core:CustomData key="descriptionProperty" value="CardName"/>
		</customData>
	</Input>
	<Input value="{params>/CardName}" editable="false">
		<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
	</Input>
	<Label text="Tipo"/>
	<Select selectedKey="{params>/Type}">
		<core:Item key="" text="Todos"/>
		<core:Item key="0" text="FIX - Preço Fixo"/>
		<core:Item key="1" text="PAF - Preço a Fixar"/>
	</Select>
</core:FragmentDefinition>
```

(O `ui5lint` acusa `valueHelpOnly` como depreciado — é o padrão de todos os filtros de relatório da casa, inclusive `InvoiceReportCommonFilters`; não é gate.)

- [ ] **Step 3: Controller-base**

`webapp/controller/reports/ContractPositionReportController.ts` (criar e `git add`):

```ts
import InvoiceReportController from "./InvoiceReportController";

/**
 * Base das telas de posição de contratos. Não há período: a posição é a do momento da emissão,
 * e nenhum campo é obrigatório (o validateForm do base só barra controles com required=true).
 * ContractStatus: 0 Rascunho, 1 Aprovado, 2 Finalizado, 3 Cancelado, 4 Em Aprovação, 5 Rejeitado
 * — padrão só Aprovado. ContractType: 0 FIX, 1 PAF.
 * @namespace siagrob1.controller.reports
 */
export default abstract class ContractPositionReportController extends InvoiceReportController {

	protected defaults(): Record<string, unknown> {
		return { Statuses: ["1"], Type: "" };
	}

	protected buildPayload(): Record<string, unknown> {
		const data = super.buildPayload();
		data.Type = data.Type == null ? null : Number(data.Type);
		return data;
	}
}
```

- [ ] **Step 4: Tela "Contratos — Compra x Venda"**

`webapp/controller/reports/contractPosition/Main.controller.ts` (criar e `git add`):

```ts
import ContractPositionReportController from "../ContractPositionReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * Contratos — Compra x Venda. Lado (ContractPositionSide): 0 Ambos, 1 Compra, 2 Venda.
 * @namespace siagrob1.controller.reports.contractPosition
 */
export default class Main extends ContractPositionReportController {
	protected readonly routeName = "contractPositionReport";
	protected readonly formId = "contractPositionReportForm";
	protected readonly serverRoute = ServerRoutes.contractPositionReport;

	protected defaults() {
		return { ...super.defaults(), Side: "0" };
	}

	protected buildPayload() {
		const data = super.buildPayload();
		data.Side = data.Side == null ? 0 : Number(data.Side);
		return data;
	}
}
```

`webapp/view/reports/contractPosition/Main.view.xml` (criar e `git add`):

```xml
<mvc:View
	controllerName="siagrob1.controller.reports.contractPosition.Main"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form">
	<Page title="Contratos — Compra x Venda">
		<f:SimpleForm
			id="contractPositionReportForm"
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
				<core:Title text="Lado e situação" emphasized="true"/>
				<Label text="Lado"/>
				<Select selectedKey="{params>/Side}">
					<core:Item key="0" text="Compra e venda"/>
					<core:Item key="1" text="Compra"/>
					<core:Item key="2" text="Venda"/>
				</Select>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.ContractPositionStatusFilters" type="XML"/>
				<core:Title text="Filtros" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.InvoiceReportCommonFilters" type="XML"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.ContractPositionFilters" type="XML"/>
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

- [ ] **Step 5: Tela "Posição Comprado x Vendido por Mês"**

`webapp/controller/reports/contractMonthlyPosition/Main.controller.ts` (criar e `git add`):

```ts
import ContractPositionReportController from "../ContractPositionReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * Posição Comprado x Vendido por Mês. Mesmos filtros da base, sem Lado (os dois sempre).
 * @namespace siagrob1.controller.reports.contractMonthlyPosition
 */
export default class Main extends ContractPositionReportController {
	protected readonly routeName = "contractMonthlyPositionReport";
	protected readonly formId = "contractMonthlyPositionReportForm";
	protected readonly serverRoute = ServerRoutes.contractMonthlyPositionReport;
}
```

`webapp/view/reports/contractMonthlyPosition/Main.view.xml` (criar e `git add`):

```xml
<mvc:View
	controllerName="siagrob1.controller.reports.contractMonthlyPosition.Main"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form">
	<Page title="Posição Comprado x Vendido por Mês">
		<f:SimpleForm
			id="contractMonthlyPositionReportForm"
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
				<core:Title text="Situação" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.ContractPositionStatusFilters" type="XML"/>
				<core:Title text="Filtros" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.InvoiceReportCommonFilters" type="XML"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.ContractPositionFilters" type="XML"/>
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

- [ ] **Step 6: manifest.json**

Em `routing.routes`, logo depois da rota `salesShipmentsReport` (o objeto com `"pattern": "sales-shipments/report"`), acrescente:

```json
        {
          "pattern": "contract-position/report",
          "name": "contractPositionReport",
          "target": "contractPositionReport"
        },
        {
          "pattern": "contract-monthly-position/report",
          "name": "contractMonthlyPositionReport",
          "target": "contractMonthlyPositionReport"
        },
```

Em `routing.targets`, logo depois do target `salesShipmentsReport`, acrescente:

```json
        "contractPositionReport": {
          "id": "contractPositionReport",
          "level": 1,
          "name": "siagrob1.view.reports.contractPosition.Main",
          "clearControlAggregation": true
        },
        "contractMonthlyPositionReport": {
          "id": "contractMonthlyPositionReport",
          "level": 1,
          "name": "siagrob1.view.reports.contractMonthlyPosition.Main",
          "clearControlAggregation": true
        },
```

Os padrões não colidem com rotas existentes (as de contrato começam com `purchase-contracts/` ou `sales-contracts/`). Confira que o JSON continua válido: `node -e "JSON.parse(require('fs').readFileSync('webapp/manifest.json','utf8'))"` — Expected: sem saída.

- [ ] **Step 7: Gates do frontend**

Run (em `siagro-b1-frontend/`): `yarn ts-typecheck` — Expected: 0 erros.
Run: `yarn lint` — Expected: sem erros.
(`yarn test` não passa por causa do gate de cobertura irreal — não use como critério.)

- [ ] **Step 8: Commit (frontend)**

```bash
git -C ../siagro-b1-frontend branch --show-current   # feature/contract-position-reports
cd ../siagro-b1-frontend
git add webapp/model/ServerRoutes.ts webapp/view/reports/fragments/ContractPositionStatusFilters.fragment.xml webapp/view/reports/fragments/ContractPositionFilters.fragment.xml webapp/controller/reports/ContractPositionReportController.ts webapp/controller/reports/contractPosition webapp/view/reports/contractPosition webapp/controller/reports/contractMonthlyPosition webapp/view/reports/contractMonthlyPosition webapp/manifest.json
git commit -F - <<'EOF'
feat(reports): telas dos relatórios de posição de contratos

Contratos — Compra x Venda e Posição Comprado x Vendido por Mês, sobre o
controller-base dos relatórios. Sem período e sem campo obrigatório: a
posição é a da emissão. Situação padrão só Aprovado; enums viajam como
número porque o serviço de relatórios não tem conversor de enum por nome.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01JSKjXkjGAVzfY8hPmEDxCG
EOF
```

---

### Task 6: Verificação pelo caminho do usuário

Sem código novo; corrige o que aparecer (cada correção com teste quando couber, e commit próprio no repo certo).

- [ ] **Step 1: Suíte do backend**

Run (em `siagro-b1-backend/`): `dotnet test SiagroB1.Application.Tests` — Expected: PASS (3759 na pré-validação, incluindo pacotes 1 e 2 inteiros).

- [ ] **Step 2: Autorização para a migration**

O item de menu **não** aparece enquanto a migration da Task 4 não for aplicada — e aplicá-la é escrita em banco: **peça autorização ao usuário** antes. Com o ok: `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` (ambiente explícito sempre; nunca o profile `db-migration`). Sem autorização, abra as telas pela rota (hash `contract-position/report` e `contract-monthly-position/report` na URL do app) e registre que o menu não foi verificado.

- [ ] **Step 3: STANDALONE (CEAGUI dev)**

Suba Web, Gateway e Reports com o profile `ceagui` e o frontend com `yarn start:dev` (ver memória "Subir a stack local"; login admin/1234). Com a migration aplicada, confira os dois itens em Relatórios, depois de "Romaneios de Venda", com o título "Contratos — Compra x Venda" sem "?" no lugar do travessão. Em cada tela: Imprimir sem preencher nada → PDF em nova aba com "Posição em: <hoje> | … | Situação: Aprovado". Na tela 1, teste Lado = Compra e Lado = Venda (a outra seção some e o rótulo do saldo geral muda) e Situação com Finalizado. Nas duas: um filtro de value help (Safra ou Parceiro), Tipo = PAF e "Término da entrega até".

- [ ] **Step 4: Bater o saldo com a tela do contrato**

Escolha um contrato de compra com washout e um de venda com entrega conferida (quebra) na CEAGUI dev. Na tela de detalhe de cada um, rode "Recalcular Saldo" (só se o usuário autorizar — grava o `AllocatedVolume` recalculado) ou leia o "Saldo (Físico)" de um contrato recém-recalculado; o Saldo do relatório 1 tem de ser **o mesmo número**. Divergência aqui é bug do `ContractPositionData`: reproduza com teste em `ContractPositionDataTests` antes de corrigir.

- [ ] **Step 5: SAPB1 (Yokotobi dev)**

Se `IDX_SIAGRO_DEV` ainda estiver atrás das migrations, o Web recusa subir: **peça autorização ao usuário** para atualizá-la (`ASPNETCORE_ENVIRONMENT=Yokotobi-Development` explícito, contexts `AppDbContext` e `CommonDbContext`). Com o ok, suba com o profile `yktb` e repita o Step 3. Confira que fornecedor, cliente e produto aparecem preenchidos (snapshots; nada vem de tabela do SAP). O ambiente Yokotobi tem SAP instável (500 e timeouts nas Liberações) — não é bug destes relatórios.

- [ ] **Step 6: Fechamento**

Use `superpowers:verification-before-completion` antes de declarar pronto. Atualize a memória do projeto (arquivo novo sobre o pacote 3 + ponteiro no `MEMORY.md`, incluindo a medição de 88 px do saldo negativo e o corte provável no Saldo de 86 px do pacote 2). Merge em `main` e push continuam sendo decisão do usuário.
