# GAC-1171 (rateio) — Ticket de descarga rateado + "Descarregada" automática — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O ticket de descarga passa a ser rateado entre as linhas dos documentos de saída Normais e
Confirmados da carga, a carga vira "Descarregada" sozinha quando toda linha entregue tem peso de
ticket, e o documento parcialmente devolvido ganha um indicador (sem status novo).

**Architecture:** O ticket (`ShipmentLoadDischarge`) vira cabeçalho e ganha a tabela filha de rateio
(`ShipmentLoadDischargeItem`). A marca manual `IsDischarged` sai: `ShipmentLoadsRecalculateInvoicedService`
deriva a Descarregada do `TicketDeliveredQuantity` das linhas. `ReturnedQuantity` (persistido-derivado,
escritor único) alimenta a base do rateio e o selo "Dev. parcial". O frontend troca o Select de nota
por um grid de rateio num buffer JSON.

**Tech Stack:** .NET 10 + EF Core (SQL Server; testes em EF InMemory, xUnit) + OData v4 (actions com
coleções paralelas); OpenUI5 1.141 + TypeScript (sap.m.Table sobre JSONModel, sap.ui.table, QUnit).

**Spec:** `docs/superpowers/specs/2026-09-28-gac-1171-discharge-distribution-design.md` (neste repo).
Leia o spec inteiro antes da primeira task: as decisões D1–D6 não se reabrem.

## Global Constraints

- Repositórios: backend em `C:\Projetos\SiagroB1\siagro-b1-backend-gac-1171`, frontend em
  `C:\Projetos\SiagroB1\siagro-b1-frontend-gac-1171`. Branch `feature/gac-1171-discharge-distribution`
  nos dois. **Confira `git branch --show-current` antes de todo commit.** Nunca `push`, nunca merge.
- Todo arquivo novo recebe `git add <caminho>` logo depois de criado.
- Identificadores em inglês; o que o usuário lê (mensagens, rótulos, log) em pt-BR.
- Quantidades em `DECIMAL(18,3)`; tolerância de fechamento **0,001** nas duas pontas.
- Parâmetro numérico de action é `Edm.Double` (nunca `Edm.Decimal`); data é `string` `yyyy-MM-dd`;
  o rateio viaja como coleções PARALELAS `SalesInvoiceItemKeys: Collection(Edm.Guid)` +
  `Quantities: Collection(Edm.Double)`.
- O ticket NUNCA escreve `DeliveredQuantity`, `QuantityLoss` ou `DeliveryStatus` e NUNCA dispara
  recálculo de contrato ou liberação (a regra central do GAC-1171).
- Ordem do ramo Faturada: **Concluída > Descarregada > Faturada**. Nenhum outro status é refinado:
  "Devolvida" continua "Devolvida" mesmo com ticket (D6).
- Enums persistidos como `int`, valores append-only: `ShipmentLoadStatus.Invoiced = 2`,
  `Discharged = 8`; `ShipmentLoadMovementType.Discharged = 23` e `DischargeUndone = 24` FICAM (linhas
  antigas). `SalesInvoiceType.Return = 1`, `InvoiceStatus.Confirmed = 1`.
- Mensagem de commit: `tipo(escopo): descrição pt-BR` + corpo com o porquê + `Refs: GAC-1171`; o
  commit que traz migration leva `DB: AddShipmentLoadDischargeItemsAndReturnedQuantity`. Termine com
  `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`. Grave a mensagem num arquivo
  do scratchpad e use `git commit -F <arquivo>` (o PowerShell não passa here-string por stdin).
- Commit que APAGA arquivo rastreado já foi negado pelo classificador (não deterministicamente). Tente
  normalmente; se for negado, deixe o arquivo da mensagem pronto e peça o comando ao usuário. Nunca
  contorne.
- Backend: build `dotnet build SiagroB1.sln -nodeReuse:false -v q`; testes
  `dotnet test SiagroB1.Application.Tests --no-build --filter "FullyQualifiedName~<Classe>"` (build ANTES,
  sempre — `--no-build` roda o assembly velho). Suíte inteira: sem `--filter`.
- Frontend: gates `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint`, mais o QUnit unitário (`yarn test`
  inteiro NÃO é gate: o limiar de cobertura nunca passa neste repo).
- Comentário XML de fragmento/view **não pode conter dois hífens seguidos** — mata o fragmento sem
  nenhum gate acusar.
- `dotnet ef` e o app SEMPRE com ambiente explícito (`Yokotobi-Development`); nunca o profile
  `db-migration` nem `dev`.

## Review Focus

1. **Nota do rateio cancelada depois do ticket** — ao editar o ticket sem ela, a parcela sai e a soma
   daquela linha zera (Task 4: `Editing_after_an_invoice_was_cancelled_drops_its_line_and_zeroes_its_sum`).
2. **`Quantities` chegando como array de inteiros** (`[20000, 15000]`) — é lido, e não vira lista vazia
   que recusaria o ticket por "rateio vazio" (Task 4: `Reads_integer_quantities_as_numbers`).
3. **Ruído de ponto flutuante no total** (35000,0004) — aceito dentro da tolerância e gravado em 3 casas;
   diferença real recusada com a mensagem nomeando os dois números (Task 3 e Task 4).
4. **Nota devolvida em parte DEPOIS do ticket** — a carga continua Descarregada; nota devolvida por
   inteiro sai da exigência (Task 2: `A_line_partially_returned_after_the_ticket_keeps_the_load_discharged`,
   `A_returned_invoice_without_ticket_does_not_block_discharged`).
5. **Ticket em carga "Faturada Parcial"** — aceito, sem mudar o status e sem linha "Situação" no log
   (Task 4: `A_partially_invoiced_load_accepts_a_ticket_without_changing_its_status`).

---

### Task 1: Quantidade devolvida por linha (`ReturnedQuantity`)

Repo: backend.

**Files:**
- Modify: `SiagroB1.Domain/Entities/SalesInvoiceItem.cs` (depois de `TicketDeliveredQuantity`, ~linha 68)
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs` (depois de `DeliveryDate`, ~linha 93)
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesRecalculateReturnedService.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs:140-144`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReverseConfirmService.cs:95-98`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargeRules.cs` (constante `Tolerance` + `RemainingQuantity`)
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesReturnedQuantityTests.cs`

**Interfaces:**
- Produces: `SalesInvoiceItem.ReturnedQuantity` e `SalesInvoice.ReturnedQuantity` (`decimal`, coluna
  `DECIMAL(18,3) DEFAULT 0`); `SalesInvoicesRecalculateReturnedService.RecalculateAsync(AppDbContext context, Guid? originInvoiceKey) : Task`
  (estático, sem SaveChanges); `ShipmentLoadDischargeRules.Tolerance = 0.001m`;
  `ShipmentLoadDischargeRules.RemainingQuantity(SalesInvoiceItem item) : decimal`.

- [ ] **Step 1: Escrever os testes que falham**

Criar `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesReturnedQuantityTests.cs` e `git add`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// GAC-1171 (rateio): quanto de cada linha já voltou em devolução CONFIRMADA. É a base do rateio do
/// ticket de descarga e o selo "Dev. parcial" das telas — sem mudar o status da nota.
/// </summary>
public class SalesInvoicesReturnedQuantityTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    /// <summary>Recusa de carga pelo caminho REAL, com faturamento de verdade antes.</summary>
    private readonly ShipmentLoadsRefuseServiceTests _refusal = new();

    private static SalesInvoice Return(
        SalesInvoice origin, InvoiceStatus status, params (SalesInvoiceItem Origin, decimal Quantity)[] lines)
    {
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(
            status, SalesInvoiceType.Return, originKey: origin.Key);

        foreach (var (originItem, quantity) in lines)
            SalesContractsAllocationTestSupport.NewItem(
                invoice, contractKey: null, releaseKey: null, quantity: quantity, originItemKey: originItem.Key);

        return invoice;
    }

    private static SalesInvoicesReverseConfirmService Reverse(UnitOfWork db) =>
        new(db,
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>());

    private static Task<SalesInvoice> OriginAsync(UnitOfWork db, Guid key) =>
        db.Context.SalesInvoices.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Only_confirmed_returns_count_on_each_line_and_on_the_header()
    {
        var origin = SalesContractsAllocationTestSupport.NewInvoice();
        var soy = SalesContractsAllocationTestSupport.NewItem(origin, null, null, 40_000m);
        var corn = SalesContractsAllocationTestSupport.NewItem(origin, null, null, 10_000m, itemCode: "MILHO");

        _db.Context.SalesInvoices.AddRange(
            origin,
            Return(origin, InvoiceStatus.Confirmed, (soy, 5_000m), (corn, 1_000m)),
            Return(origin, InvoiceStatus.Confirmed, (soy, 2_500m)),
            Return(origin, InvoiceStatus.Pending, (soy, 9_000m)),
            Return(origin, InvoiceStatus.Cancelled, (corn, 3_000m)));
        await _db.Context.SaveChangesAsync();

        await SalesInvoicesRecalculateReturnedService.RecalculateAsync(_db.Context, origin.Key);

        Assert.Equal(7_500m, soy.ReturnedQuantity);
        Assert.Equal(1_000m, corn.ReturnedQuantity);
        Assert.Equal(8_500m, origin.ReturnedQuantity);
        Assert.Equal(InvoiceStatus.Confirmed, origin.InvoiceStatus);
    }

    /// <summary>
    /// Quem chama acabou de trocar o status da devolução e ainda não salvou. O escritor precisa ler
    /// o status RASTREADO, senão gravaria o valor anterior ao próprio ato que o chamou.
    /// </summary>
    [Fact]
    public async Task An_unsaved_status_change_of_the_return_is_seen()
    {
        var origin = SalesContractsAllocationTestSupport.NewInvoice();
        var soy = SalesContractsAllocationTestSupport.NewItem(origin, null, null, 40_000m);
        var confirming = Return(origin, InvoiceStatus.Pending, (soy, 10_000m));
        var reversing = Return(origin, InvoiceStatus.Confirmed, (soy, 4_000m));
        _db.Context.SalesInvoices.AddRange(origin, confirming, reversing);
        await _db.Context.SaveChangesAsync();

        confirming.InvoiceStatus = InvoiceStatus.Confirmed; // sem SaveChanges
        reversing.InvoiceStatus = InvoiceStatus.Pending;    // sem SaveChanges

        await SalesInvoicesRecalculateReturnedService.RecalculateAsync(_db.Context, origin.Key);

        Assert.Equal(10_000m, soy.ReturnedQuantity);
    }

    [Fact]
    public async Task A_missing_origin_is_a_no_op()
    {
        await SalesInvoicesRecalculateReturnedService.RecalculateAsync(_db.Context, null);
        await SalesInvoicesRecalculateReturnedService.RecalculateAsync(_db.Context, Guid.NewGuid());
    }

    [Fact]
    public void Remaining_quantity_is_billed_minus_returned()
    {
        var item = new SalesInvoiceItem
        {
            ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 40_000m, ReturnedQuantity = 10_000.0004m,
        };

        Assert.Equal(30_000.000m, ShipmentLoadDischargeRules.RemainingQuantity(item));
    }

    [Fact]
    public async Task A_partial_refusal_marks_the_returned_quantity_and_keeps_the_invoice_confirmed()
    {
        var (load, invoice) = await _refusal.BilledLoadAsync(40_000m);

        await _refusal.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(load, invoice, 10_000m), "tester");

        var origin = await OriginAsync(_refusal._db, invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, origin.InvoiceStatus);
        Assert.Equal(10_000m, origin.ReturnedQuantity);
        Assert.Equal(10_000m, Assert.Single(origin.Items).ReturnedQuantity);
    }

    [Fact]
    public async Task A_total_refusal_returns_everything_and_the_invoice_becomes_returned()
    {
        var (load, invoice) = await _refusal.BilledLoadAsync(40_000m);

        await _refusal.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(load, invoice, 40_000m), "tester");

        var origin = await OriginAsync(_refusal._db, invoice.Key);
        Assert.Equal(InvoiceStatus.Returned, origin.InvoiceStatus);
        Assert.Equal(40_000m, origin.ReturnedQuantity);
    }

    [Fact]
    public async Task Reversing_the_return_confirmation_zeroes_the_returned_quantity()
    {
        var (load, invoice) = await _refusal.BilledLoadAsync(40_000m);
        await _refusal.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(load, invoice, 10_000m), "tester");

        var returnKey = await _refusal._db.Context.SalesInvoices.AsNoTracking()
            .Where(x => x.InvoiceType == SalesInvoiceType.Return)
            .Select(x => x.Key)
            .SingleAsync();

        await Reverse(_refusal._db).ExecuteAsync(returnKey, "tester");

        var origin = await OriginAsync(_refusal._db, invoice.Key);
        Assert.Equal(0m, origin.ReturnedQuantity);
        Assert.Equal(0m, Assert.Single(origin.Items).ReturnedQuantity);
    }

    /// <summary>A lista de documentos e o grid de rateio leem as duas colunas pelo OData.</summary>
    [Fact]
    public void The_returned_quantity_is_in_the_edm_for_the_list_and_the_line()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();

        var invoice = (IEdmStructuredType)model.FindDeclaredType(typeof(SalesInvoice).FullName);
        var item = (IEdmStructuredType)model.FindDeclaredType(typeof(SalesInvoiceItem).FullName);

        Assert.NotNull(invoice.FindProperty(nameof(SalesInvoice.ReturnedQuantity)));
        Assert.NotNull(item.FindProperty(nameof(SalesInvoiceItem.ReturnedQuantity)));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet build SiagroB1.sln -nodeReuse:false -v q`
Expected: FAIL de compilação — `ReturnedQuantity`, `SalesInvoicesRecalculateReturnedService` e
`RemainingQuantity` não existem.

- [ ] **Step 3: Colunas nas entidades**

Em `SalesInvoiceItem.cs`, logo depois de `TicketDeliveredQuantity`:

```csharp
    /// <summary>
    /// Quanto desta linha já voltou em devoluções CONFIRMADAS (GAC-1171, rateio): soma de
    /// <c>Quantity</c> das linhas de devolução com <see cref="SalesInvoiceItemOriginKey"/> = esta
    /// linha. Persistido-derivado, escritor único <c>SalesInvoicesRecalculateReturnedService</c>.
    /// </summary>
    /// <remarks>
    /// "Confirmada" é o mesmo critério do saldo da carga: o projeto considera que a devolução ocorreu
    /// na confirmação. Serve à base do rateio do ticket de descarga (faturado − devolvido) e ao selo
    /// "Dev. parcial" das telas. NÃO muda o <c>InvoiceStatus</c>: a nota devolvida em parte continua
    /// Confirmada.
    /// </remarks>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal ReturnedQuantity { get; set; }
```

Em `SalesInvoice.cs`, logo depois de `DeliveryDate`:

```csharp
    /// <summary>
    /// Soma de <see cref="SalesInvoiceItem.ReturnedQuantity"/> das linhas (GAC-1171, rateio). Existe
    /// no cabeçalho porque a lista de documentos mostra o selo "Dev. parcial", e uma coleção não se
    /// binda em célula de linha. Mesmo escritor único das linhas.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal ReturnedQuantity { get; set; }
```

- [ ] **Step 4: O escritor único**

Criar `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesRecalculateReturnedService.cs` e `git add`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Escritor ÚNICO de <c>SalesInvoiceItem.ReturnedQuantity</c> e <c>SalesInvoice.ReturnedQuantity</c>
/// (GAC-1171, rateio): quanto de cada linha da nota de ORIGEM já voltou em devoluções CONFIRMADAS.
/// </summary>
/// <remarks>
/// Estático e sem <c>SaveChanges</c>, como <c>SalesInvoicesReturnOriginRestoreService</c>: roda dentro
/// da transação de quem confirma ou estorna a devolução, e o valor entra no mesmo <c>SaveChanges</c>
/// do status que o causou.
/// <para>
/// ⚠️ Lê as devoluções RASTREADAS e filtra o status EM MEMÓRIA. Quem chama acabou de trocar o status
/// da devolução e ainda não salvou: com o status no WHERE, o banco devolveria o valor antigo. A
/// consulta rastreada devolve a instância já rastreada com o valor atual, e o filtro em memória
/// enxerga a troca — mesma defesa de <c>ShipmentLoadsRecalculateInvoicedService.EvaluateClosureAsync</c>.
/// </para>
/// <para>
/// Cancelar e excluir uma devolução não chamam este serviço, e não precisam: os dois só alcançam
/// devolução NÃO confirmada (<c>SalesInvoicesCancelService</c> recusa retorno confirmado), que não
/// entra na soma.
/// </para>
/// </remarks>
public static class SalesInvoicesRecalculateReturnedService
{
    public static async Task RecalculateAsync(AppDbContext context, Guid? originInvoiceKey)
    {
        if (originInvoiceKey is null)
            return;

        var origin = await context.SalesInvoices
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Key == originInvoiceKey);

        if (origin is null)
            return;

        var returns = await context.SalesInvoices
            .Include(x => x.Items)
            .Where(x => x.InvoiceType == SalesInvoiceType.Return
                        && x.SalesInvoiceOriginKey == originInvoiceKey)
            .ToListAsync();

        var confirmedReturnItems = returns
            .Where(x => context.Entry(x).State != EntityState.Deleted
                        && x.InvoiceStatus == InvoiceStatus.Confirmed)
            .SelectMany(x => x.Items)
            .Where(x => context.Entry(x).State != EntityState.Deleted)
            .ToList();

        foreach (var item in origin.Items)
        {
            item.ReturnedQuantity = decimal.Round(
                confirmedReturnItems
                    .Where(x => x.SalesInvoiceItemOriginKey == item.Key)
                    .Sum(x => x.Quantity),
                3, MidpointRounding.ToEven);
        }

        origin.ReturnedQuantity = decimal.Round(
            origin.Items.Sum(x => x.ReturnedQuantity), 3, MidpointRounding.ToEven);
    }
}
```

- [ ] **Step 5: Chamar na confirmação e no estorno**

Em `SalesInvoicesConfirmService.ExecuteAsync`, entre `invoice.ApprovedAt = DateTime.Now;` (linha 142) e
`await db.SaveChangesAsync();` (linha 144):

```csharp
            // GAC-1171 (rateio): a quantidade devolvida da ORIGEM nasce aqui, com a devolução já
            // Confirmada no rastreador. A Recusa de carga passa por este ponto, e o recálculo da
            // carga (loadHook, logo abaixo) já enxerga o valor novo.
            if (invoice.InvoiceType == SalesInvoiceType.Return)
                await SalesInvoicesRecalculateReturnedService.RecalculateAsync(
                    db.Context, invoice.SalesInvoiceOriginKey);
```

Em `SalesInvoicesReverseConfirmService.ExecuteAsync`, logo depois de `invoice.ApprovedBy = null;` (linha 98):

```csharp
            // GAC-1171 (rateio): a devolução estornada deixa de contar. Vale para os três ramos
            // (carga, novo e legado), por isso fica aqui e não dentro de um deles.
            if (invoice.InvoiceType == SalesInvoiceType.Return)
                await SalesInvoicesRecalculateReturnedService.RecalculateAsync(
                    db.Context, invoice.SalesInvoiceOriginKey);
```

- [ ] **Step 6: `RemainingQuantity` nas regras do ticket**

Em `ShipmentLoadDischargeRules.cs`, no topo da classe (antes de `NormalizeTicketNumber`):

```csharp
    /// <summary>Tolerância de fechamento, a mesma casa decimal das quantidades.</summary>
    public const decimal Tolerance = 0.001m;

    /// <summary>
    /// Faturado que não voltou: <c>Quantity − ReturnedQuantity</c>, em 3 casas. É a base do rateio e
    /// o que torna a linha elegível ao ticket (GAC-1171, rateio).
    /// </summary>
    /// <remarks>
    /// ⚠️ Não é <see cref="SalesInvoiceItem.NetQuantity"/>, que é o conferido menos a quebra.
    /// </remarks>
    public static decimal RemainingQuantity(SalesInvoiceItem item) =>
        decimal.Round(item.Quantity - item.ReturnedQuantity, 3, MidpointRounding.ToEven);
```

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet build SiagroB1.sln -nodeReuse:false -v q` e
`dotnet test SiagroB1.Application.Tests --no-build --filter "FullyQualifiedName~SalesInvoicesReturnedQuantityTests"`
Expected: 8 aprovados. Depois a suíte inteira: `dotnet test SiagroB1.Application.Tests --no-build`
→ todos verdes (baseline 2332 + 8).

- [ ] **Step 8: Commit**

```text
feat(invoice): gravar a quantidade devolvida por linha do documento de saída

A devolução parcial (recusa de carga ou retorno com quantidade) deixava a nota
Confirmada sem rastro visível do que voltou. ReturnedQuantity, na linha e no
cabeçalho, soma as devoluções CONFIRMADAS — o mesmo critério do saldo da carga —
e é a base do rateio do ticket de descarga e do selo "Dev. parcial".

Escritor único e estático, chamado na confirmação e no estorno da devolução;
lê o status rastreado porque roda antes do SaveChanges de quem o chama.

Refs: GAC-1171
```

`git add` dos 6 arquivos alterados/criados e `git commit -F <arquivo>`.

---

### Task 2: "Descarregada" derivada dos tickets (sai a marca manual)

Repo: backend.

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRecalculateInvoicedService.cs:40-43,138-152,296-364`
- Modify: `SiagroB1.Domain/Entities/ShipmentLoad.cs:143-161` (sai `IsDischarged`)
- Modify: `SiagroB1.Domain/Enums/ShipmentLoadStatus.cs:10-14,32`
- Delete: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsMarkDischargedService.cs`
- Delete: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsUndoDischargedService.cs`
- Delete: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsMarkDischargedController.cs`
- Delete: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsUndoDischargedController.cs`
- Delete: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsDischargedServiceTests.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs:635-642` (saem as duas actions)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs:406-407`
- Modify (mensagens): `ShipmentLoadsRefuseService.cs:420-433`, `ShipmentLoadTransshipmentRules.cs:61-66`,
  `SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsChangeReleaseService.cs:204-209`
- Test (rewrite): `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadClosureTests.cs`
- Test (edit): `ShipmentLoadClosureInvoiceWritersTests.cs`, `SalesInvoices/SalesInvoicesItemsUpdateLoadClosureTests.cs`,
  `ShipmentLoadsCancelServiceTests.cs`, `ShipmentLoadsDetachTransactionsServiceTests.cs`,
  `ShipmentLoadClosureGuardsTests.cs`, `ShipmentLoadsRefuseServiceTests.cs`,
  `ShippingTransactions/ShippingTransactionsChangeReleaseServiceTests.cs`, `ShipmentLoadEdmModelTests.cs`,
  `ShipmentLoadDischargedModelTests.cs`

**Interfaces:**
- Consumes: `ShipmentLoadDischargeRules.RemainingQuantity` (Task 1).
- Produces: `ShipmentLoadsRecalculateInvoicedService.ClosureFacts` (`readonly record struct (bool AllDeliveriesClosed, bool AllDischarged)`, com `ClosureFacts.None`);
  `ShipmentLoadsRecalculateInvoicedService.EvaluateClosureAsync(AppDbContext, Guid, ICollection<Guid>?) : Task<ClosureFacts>`;
  `ResolveClosure(ShipmentLoadStatus baseStatus, bool allDischarged, bool allDeliveriesClosed)`.
  `AreAllDeliveriesClosedAsync` continua existindo (delegando).

- [ ] **Step 1: Reescrever `ShipmentLoadClosureTests.cs` com a regra nova (falha)**

Substituir o arquivo inteiro por:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171: como o ramo Faturada se desdobra em Faturada, Descarregada e Concluída.
/// </summary>
/// <remarks>
/// Concluída = todos os itens das notas Normais CONFIRMADAS da carga com a entrega encerrada, sem
/// nota Pendente. Descarregada (rateio) = toda linha dessas notas que não voltou inteira tem peso de
/// ticket, sem nota Pendente. As Canceladas/Retornadas ficam fora das duas, como ficam fora da tela.
/// </remarks>
public class ShipmentLoadClosureTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsRecalculateInvoicedService Service() => new(
        _db, new ShipmentLoadsChangeLogService(_db.Context));

    private ShipmentLoad Load(decimal total = 90_000)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000031",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = total,
        };
        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private StorageTransaction Shipment(ShipmentLoad load)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            GrossWeight = load.TotalQuantity,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
        };
        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    /// <summary>
    /// Nota Normal de uma linha. <paramref name="ticket"/> é o peso de ticket já somado na linha
    /// (<c>TicketDeliveredQuantity</c>) e <paramref name="returned"/>, o que já voltou dela.
    /// </summary>
    private SalesInvoice Invoice(
        ShipmentLoad load,
        decimal quantity,
        InvoiceStatus status = InvoiceStatus.Confirmed,
        SalesInvoiceDeliveryStatus delivery = SalesInvoiceDeliveryStatus.Open,
        decimal ticket = 0m,
        decimal returned = 0m)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceNumber = "000000123",
            InvoiceStatus = status,
            InvoiceType = SalesInvoiceType.Normal,
            ShipmentLoadKey = load.Key,
        };

        invoice.Items.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
            DeliveredQuantity = delivery == SalesInvoiceDeliveryStatus.Closed ? quantity : 0m,
            DeliveryStatus = delivery,
            TicketDeliveredQuantity = ticket,
            ReturnedQuantity = returned,
        });

        _db.Context.SalesInvoices.Add(invoice);
        return invoice;
    }

    /// <summary>
    /// Lê o estado REALMENTE persistido. <c>RecalculateAsync</c> só ENFILEIRA as mudanças no
    /// contexto; sem o <c>SaveChangesAsync</c> aqui, o <c>AsNoTracking</c> leria o valor antigo.
    /// </summary>
    private async Task<ShipmentLoad> SavedAsync()
    {
        await _db.Context.SaveChangesAsync();
        return await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Invoiced, false, false, ShipmentLoadStatus.Invoiced)]
    [InlineData(ShipmentLoadStatus.Invoiced, true, false, ShipmentLoadStatus.Discharged)]
    [InlineData(ShipmentLoadStatus.Invoiced, false, true, ShipmentLoadStatus.Completed)]
    [InlineData(ShipmentLoadStatus.Invoiced, true, true, ShipmentLoadStatus.Completed)]
    [InlineData(ShipmentLoadStatus.PartiallyInvoiced, true, true, ShipmentLoadStatus.PartiallyInvoiced)]
    [InlineData(ShipmentLoadStatus.Returned, true, true, ShipmentLoadStatus.Returned)]
    [InlineData(ShipmentLoadStatus.InTransshipment, true, true, ShipmentLoadStatus.InTransshipment)]
    [InlineData(ShipmentLoadStatus.Open, true, true, ShipmentLoadStatus.Open)]
    public void Closure_only_refines_the_invoiced_branch(
        ShipmentLoadStatus baseStatus, bool allDischarged, bool allClosed, ShipmentLoadStatus expected)
    {
        Assert.Equal(expected,
            ShipmentLoadsRecalculateInvoicedService.ResolveClosure(baseStatus, allDischarged, allClosed));
    }

    [Fact]
    public async Task All_deliveries_closed_completes_the_load_and_keeps_the_shipments_invoiced()
    {
        var load = Load();
        var shipment = Shipment(load);
        Invoice(load, 40_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        Invoice(load, 50_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Completed, (await SavedAsync()).Status);
        var savedShipment = await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == shipment.Key);
        Assert.Equal(StorageTransactionsStatus.Invoiced, savedShipment.TransactionStatus);
    }

    [Fact]
    public async Task Recalculating_on_request_logs_the_status_change_signed_by_the_user()
    {
        var load = Load();
        load.Status = ShipmentLoadStatus.Invoiced;
        load.InvoicedQuantity = 90_000;
        Shipment(load).TransactionStatus = StorageTransactionsStatus.Invoiced;
        Invoice(load, 90_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key, "tester");

        var saved = await SavedAsync();
        Assert.Equal(ShipmentLoadStatus.Completed, saved.Status);
        Assert.Equal("tester", saved.UpdatedBy);

        var log = await _db.Context.ShipmentLoadsChangeLogs.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadChangeLogFields.Status, log.Field);
        Assert.Equal("Faturada", log.OldValue);
        Assert.Equal("Concluída", log.NewValue);
        Assert.Equal("tester", log.ChangedBy);
    }

    [Fact]
    public async Task Recalculating_on_request_without_a_status_change_writes_no_log()
    {
        var load = Load();
        load.Status = ShipmentLoadStatus.Invoiced;
        load.InvoicedQuantity = 90_000;
        Invoice(load, 90_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key, "tester");

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await SavedAsync()).Status);
        Assert.Empty(_db.Context.ShipmentLoadsChangeLogs);
    }

    [Fact]
    public async Task One_open_delivery_keeps_the_load_invoiced()
    {
        var load = Load();
        Invoice(load, 40_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        Invoice(load, 50_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await SavedAsync()).Status);
    }

    [Fact]
    public async Task Tickets_on_every_delivered_line_make_the_load_discharged_and_keep_the_shipments_invoiced()
    {
        var load = Load();
        var shipment = Shipment(load);
        Invoice(load, 40_000, ticket: 39_800);
        Invoice(load, 50_000, ticket: 49_700);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Discharged, (await SavedAsync()).Status);
        var savedShipment = await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == shipment.Key);
        Assert.Equal(StorageTransactionsStatus.Invoiced, savedShipment.TransactionStatus);
    }

    [Fact]
    public async Task One_line_without_ticket_keeps_the_load_invoiced()
    {
        var load = Load();
        Invoice(load, 40_000, ticket: 39_800);
        Invoice(load, 50_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await SavedAsync()).Status);
    }

    [Fact]
    public async Task Every_delivery_closed_wins_over_the_tickets()
    {
        var load = Load();
        Invoice(load, 90_000, delivery: SalesInvoiceDeliveryStatus.Closed, ticket: 89_500);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Completed, (await SavedAsync()).Status);
    }

    /// <summary>A Pendente ainda não recebe ticket nem entrou na Conferência.</summary>
    [Fact]
    public async Task A_pending_invoice_prevents_discharged_even_with_the_rest_ticketed()
    {
        var load = Load();
        Invoice(load, 40_000, ticket: 39_800);
        Invoice(load, 50_000, InvoiceStatus.Pending, ticket: 49_700);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await SavedAsync()).Status);
    }

    [Fact]
    public async Task A_pending_invoice_prevents_completion_even_with_the_rest_closed()
    {
        var load = Load();
        Invoice(load, 40_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        Invoice(load, 50_000, InvoiceStatus.Pending);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await SavedAsync()).Status);
    }

    /// <summary>
    /// Review Focus 4: parte da nota voltou DEPOIS do ticket. O ticket continua valendo para o que
    /// ficou (o saldo aqui não importa: a regra lê só a linha).
    /// </summary>
    [Fact]
    public async Task A_line_partially_returned_after_the_ticket_keeps_the_load_discharged()
    {
        var load = Load();
        Invoice(load, 90_000, ticket: 90_000, returned: 30_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Discharged, (await SavedAsync()).Status);
    }

    /// <summary>Linha que voltou inteira não chegou ao destino: não espera ticket.</summary>
    [Fact]
    public async Task A_line_returned_in_full_does_not_wait_for_a_ticket()
    {
        var load = Load();
        Invoice(load, 80_000, ticket: 79_500);
        Invoice(load, 10_000, returned: 10_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Discharged, (await SavedAsync()).Status);
    }

    /// <summary>Review Focus 4: a nota devolvida por inteiro (Returned) sai da exigência.</summary>
    [Fact]
    public async Task A_returned_invoice_without_ticket_does_not_block_discharged()
    {
        var load = Load();
        Invoice(load, 80_000, ticket: 79_500);
        Invoice(load, 10_000, InvoiceStatus.Returned);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Discharged, (await SavedAsync()).Status);
    }

    [Fact]
    public async Task Cancelled_and_returned_invoices_do_not_block_completion()
    {
        var load = Load(total: 100_000);
        Invoice(load, 90_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        Invoice(load, 10_000, InvoiceStatus.Returned);   // Returned continua consumindo
        Invoice(load, 5_000, InvoiceStatus.Cancelled);   // Cancelled não consome
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Completed, (await SavedAsync()).Status);
    }

    [Fact]
    public async Task Leaving_invoiced_is_never_discharged_even_with_tickets()
    {
        var load = Load();
        Invoice(load, 90_000, InvoiceStatus.Cancelled, ticket: 89_500);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        Assert.Equal(ShipmentLoadStatus.Open, (await SavedAsync()).Status);
    }

    /// <summary>
    /// O recálculo roda DENTRO de transações alheias, às vezes antes do flush. A regra lê o estado
    /// RASTREADO: é assim que o ticket e a Descarregada entram no mesmo SaveChanges.
    /// </summary>
    [Fact]
    public async Task An_unsaved_ticket_sum_is_seen()
    {
        var load = Load();
        var invoice = Invoice(load, 90_000);
        await _db.Context.SaveChangesAsync();

        invoice.Items.Single().TicketDeliveredQuantity = 89_500; // sem SaveChanges

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(_db.Context, load.Key, excludedInvoiceKeys: null);

        Assert.Equal(ShipmentLoadStatus.Discharged, load.Status);
    }

    [Fact]
    public async Task An_unsaved_reopen_of_a_tracked_item_is_seen()
    {
        var load = Load();
        var invoice = Invoice(load, 90_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        await _db.Context.SaveChangesAsync();

        invoice.Items.Single().DeliveryStatus = SalesInvoiceDeliveryStatus.Open; // sem SaveChanges

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(_db.Context, load.Key, excludedInvoiceKeys: null);

        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);
    }

    [Fact]
    public async Task An_unsaved_status_change_of_a_tracked_invoice_is_seen()
    {
        var load = Load(total: 100_000);
        Invoice(load, 90_000, delivery: SalesInvoiceDeliveryStatus.Closed);
        var returned = Invoice(load, 10_000, InvoiceStatus.Returned);
        await _db.Context.SaveChangesAsync();

        returned.InvoiceStatus = InvoiceStatus.Confirmed; // origem restaurada, item ainda Open

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(_db.Context, load.Key, excludedInvoiceKeys: null);

        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);
    }

    [Fact]
    public async Task A_load_without_confirmed_invoices_is_never_completed_nor_discharged()
    {
        Assert.False(await ShipmentLoadsRecalculateInvoicedService.AreAllDeliveriesClosedAsync(
            _db.Context, Guid.NewGuid(), excludedInvoiceKeys: null));

        var facts = await ShipmentLoadsRecalculateInvoicedService.EvaluateClosureAsync(
            _db.Context, Guid.NewGuid(), excludedInvoiceKeys: null);

        Assert.False(facts.AllDeliveriesClosed);
        Assert.False(facts.AllDischarged);
    }
}
```

- [ ] **Step 2: Ajustar os outros testes que usavam `IsDischarged`**

`ShipmentLoadClosureInvoiceWritersTests.cs`:
- No XML-doc de `SeedPendingReturnAsync`, trocar `<paramref name="isDischarged"/> liga a marca manual de Descarregada.`
  por `<paramref name="withTickets"/> põe peso de ticket na nota C (a única Normal confirmada).`
- Assinatura: `UnitOfWork db, bool isDischarged = false)` → `UnitOfWork db, bool withTickets = false)`.
- Remover a linha `IsDischarged = isDischarged,` do `new ShipmentLoad`.
- Depois de `otherItem.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;` acrescentar
  `otherItem.TicketDeliveredQuantity = withTickets ? 100m : 0m;`.
- Substituir o teste `Reversing_the_confirmation_of_a_discharged_load_invoice_returns_to_discharged` inteiro por:

```csharp
    /// <summary>
    /// GAC-1171 (rateio): a Descarregada é derivada e exige que nenhuma nota Normal esteja
    /// Pendente. O estorno devolve a nota a Pendente, então a carga cai para Faturada mesmo com
    /// ticket — e volta sozinha ao reconfirmar.
    /// </summary>
    [Fact]
    public async Task Reversing_the_confirmation_of_a_load_invoice_with_tickets_returns_to_invoiced()
    {
        var db = TestDb.CreateUnitOfWork();
        await SeedPendingReturnAsync(db, withTickets: true);
        var other = await ConfirmedNormalInvoiceAsync(db);

        await Reverse(db).ExecuteAsync(other.Key, "tester");

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync(db)).Status);
        await AssertSingleStatusLogAndNoMovementAsync(
            db, ShipmentLoadStatus.Completed, ShipmentLoadStatus.Invoiced);
    }
```

`SalesInvoices/SalesInvoicesItemsUpdateLoadClosureTests.cs`:
- Parâmetro `bool isDischarged = false,` → `bool withTickets = false,`; remover `IsDischarged = isDischarged,`.
- Dentro de `Item(...)`, acrescentar `TicketDeliveredQuantity = withTickets ? 50_000 : 0m,`.
- O teste `Reopening_a_delivery_of_a_completed_and_marked_load_returns_it_to_discharged` passa a se
  chamar `Reopening_a_delivery_of_a_completed_load_with_tickets_returns_it_to_discharged` e usa
  `ShipmentLoadStatus.Completed, withTickets: true);`.

`ShipmentLoadsCancelServiceTests.cs` (teste `Cancelling_a_discharged_load_is_refused_by_the_composition_guard`)
e `ShipmentLoadsDetachTransactionsServiceTests.cs` (teste `Detaching_from_a_discharged_load_is_refused_by_the_composition_guard`):
remover as linhas `load.IsDischarged = true;` e `Assert.True(saved.IsDischarged);`. O resto fica.

`ShipmentLoadClosureGuardsTests.cs`: substituir o último teste por:

```csharp
    /// <summary>
    /// A Descarregada nasce dos tickets (GAC-1171, rateio): o caminho de volta é excluir o ticket, e
    /// a mensagem aponta para ele em vez de dizer só "está encerrada".
    /// </summary>
    [Fact]
    public void Transshipment_on_a_discharged_load_asks_to_delete_the_ticket()
    {
        var ex = Assert.Throws<ApplicationException>(
            () => ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment(Load(ShipmentLoadStatus.Discharged)));

        Assert.Equal(
            "A carga CG000071 já foi descarregada no destino. Exclua o ticket de descarga antes de iniciar o transbordo.",
            ex.Message);
    }
```

`ShipmentLoadsRefuseServiceTests.cs` (`Refusing_a_discharged_load_is_refused`): remover
`tracked.IsDischarged = true;`; a mensagem esperada vira
`"A carga CG000007 já foi descarregada no destino. Exclua o ticket de descarga antes de registrar recusa."`;
no XML-doc, trocar "o caminho é desfazer a descarga primeiro" por "o caminho é excluir o ticket primeiro".

`ShippingTransactions/ShippingTransactionsChangeReleaseServiceTests.cs`: o teste
`Rejects_WhenLoadIsDischarged_AsksToUndoTheDischarge` vira `Rejects_WhenLoadIsDischarged_AsksToDeleteTheTicket`,
perde `load.IsDischarged = true;` e espera
`"A carga CG000001 já foi descarregada no destino. Exclua o ticket de descarga antes de trocar a liberação."`;
no XML-doc, "tem caminho de volta próprio (o \"Desfazer Descarregada\")" vira "tem caminho de volta próprio (excluir o ticket de descarga)".

`ShipmentLoadEdmModelTests.cs`: substituir os dois testes finais (linhas 272-290) por:

```csharp
    /// <summary>
    /// GAC-1171 (rateio): a Descarregada é derivada dos tickets. As actions manuais e a marca saíram
    /// do EDM — uma tela velha que as chamasse levaria 404, e não um status errado.
    /// </summary>
    [Theory]
    [InlineData("ShipmentLoadsMarkDischarged")]
    [InlineData("ShipmentLoadsUndoDischarged")]
    public void The_manual_discharged_actions_are_gone(string actionName)
    {
        Assert.DoesNotContain(Model().SchemaElements.OfType<IEdmAction>(), a => a.Name == actionName);
    }

    [Fact]
    public void The_discharged_status_stays_in_the_edm_without_the_manual_mark()
    {
        Assert.Null(EntityType("ShipmentLoad").FindProperty("IsDischarged"));

        var status = Model().SchemaElements.OfType<IEdmEnumType>().Single(t => t.Name == "ShipmentLoadStatus");
        Assert.Contains(status.Members, m => m.Name == "Discharged");
    }
```

`ShipmentLoadDischargedModelTests.cs`: apagar o teste `A_new_load_is_not_discharged` e os `using`
que ficarem sem uso (`Microsoft.EntityFrameworkCore`, `SiagroB1.Application.Tests.Support`); no
`<summary>` da classe, "o status Descarregada e a marca manual que o produz" vira "o status Descarregada".

Apagar `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsDischargedServiceTests.cs` (`git rm`).

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet build SiagroB1.sln -nodeReuse:false -v q`
Expected: FAIL — `EvaluateClosureAsync` não existe; `IsDischarged` ainda existe mas os testes novos
esperam Discharged vindo dos tickets.

- [ ] **Step 4: A regra no recálculo**

Em `ShipmentLoadsRecalculateInvoicedService.cs`:

(a) No `<remarks>` da classe, o parágrafo "GAC-1171 (melhorias): o ramo Faturada se desdobra ..." vira:

```csharp
/// <para>
/// GAC-1171: o ramo Faturada se desdobra em Faturada, Descarregada e Concluída por
/// <see cref="ResolveClosure"/>. As duas são DERIVADAS: a Concluída da Conferência de Entregas, a
/// Descarregada (rateio) do peso de ticket em cada linha entregue. Não há marca manual.
/// </para>
```

(b) Substituir as linhas 138-152 (do comentário "GAC-1171 (melhorias): a marca manual só vale ..." até
`load.Status = ResolveClosure(...)`) por:

```csharp
        // Só o ramo Faturada usa a Conferência e os tickets: fora dele a consulta seria trabalho à toa.
        var closure = baseStatus == ShipmentLoadStatus.Invoiced
            ? await EvaluateClosureAsync(context, shipmentLoadKey, excludedInvoiceKeys)
            : ClosureFacts.None;

        load.InvoicedQuantity = invoiced;
        load.ReturnedToWarehouseQuantity = returned;
        load.TransshippedQuantity = transshipped;
        load.Status = ResolveClosure(baseStatus, closure.AllDischarged, closure.AllDeliveriesClosed);
```

(c) Substituir `ResolveClosure` e `AreAllDeliveriesClosedAsync` (linhas 296-364) por:

```csharp
    /// <summary>
    /// Desdobra o <c>Invoiced</c> de <see cref="ResolveStatus"/> pela Conferência de Entregas e pelos
    /// tickets de descarga (GAC-1171). Qualquer outro status passa intacto — inclusive a Devolvida
    /// da carga mista, que aceita ticket e continua Devolvida.
    /// </summary>
    /// <remarks>
    /// A Concluída vence a Descarregada: ela é a afirmação mais forte ("tudo foi conferido"), e passar
    /// por Descarregada antes é opcional. Por isso excluir o ticket de uma carga Concluída não muda o
    /// status.
    /// </remarks>
    public static ShipmentLoadStatus ResolveClosure(
        ShipmentLoadStatus baseStatus,
        bool allDischarged,
        bool allDeliveriesClosed)
    {
        if (baseStatus != ShipmentLoadStatus.Invoiced)
            return baseStatus;

        if (allDeliveriesClosed)
            return ShipmentLoadStatus.Completed;

        return allDischarged ? ShipmentLoadStatus.Discharged : ShipmentLoadStatus.Invoiced;
    }

    /// <summary>As duas respostas que refinam o ramo Faturada.</summary>
    public readonly record struct ClosureFacts(bool AllDeliveriesClosed, bool AllDischarged)
    {
        public static readonly ClosureFacts None = new(false, false);
    }

    /// <summary>
    /// Numa leitura só das notas Normais da carga:
    /// <list type="bullet">
    /// <item><b>AllDeliveriesClosed</b> — nenhuma nota Normal Pendente, ao menos um item em nota
    /// Normal Confirmada, e todos os itens das Confirmadas <c>Closed</c>.</item>
    /// <item><b>AllDischarged</b> (rateio) — nenhuma nota Normal Pendente, ao menos uma linha de nota
    /// Normal Confirmada com faturado que não voltou, e todas essas linhas com peso de ticket
    /// (<c>TicketDeliveredQuantity</c>). A linha que voltou inteira não chegou ao destino e não
    /// espera ticket.</item>
    /// </list>
    /// Canceladas e Retornadas ficam fora das duas, como ficam fora da tela de Conferência.
    /// </summary>
    /// <remarks>
    /// ⚠️ Materializa as notas com os itens em vez de agregar no servidor, e filtra EM MEMÓRIA. O
    /// recálculo roda dentro de transações alheias: quem mudou o status, a entrega ou o peso de ticket
    /// de uma linha e recalcula antes de salvar leria, com o filtro no WHERE, o valor antigo do banco.
    /// Com a consulta rastreada, o EF devolve as instâncias já rastreadas com os valores atuais. É o
    /// que faz o ticket e a Descarregada entrarem no mesmo SaveChanges. São poucas notas por carga.
    /// </remarks>
    public static async Task<ClosureFacts> EvaluateClosureAsync(
        AppDbContext context,
        Guid shipmentLoadKey,
        ICollection<Guid>? excludedInvoiceKeys)
    {
        var invoices = await context.SalesInvoices
            .Include(i => i.Items)
            .Where(i => i.ShipmentLoadKey == shipmentLoadKey
                        && i.InvoiceType == SalesInvoiceType.Normal)
            .ToListAsync();

        var live = invoices
            .Where(i => context.Entry(i).State != EntityState.Deleted)
            .Where(i => excludedInvoiceKeys is not { Count: > 0 } || !excludedInvoiceKeys.Contains(i.Key))
            .ToList();

        if (live.Any(i => i.InvoiceStatus == InvoiceStatus.Pending))
            return ClosureFacts.None;

        var items = live
            .Where(i => i.InvoiceStatus == InvoiceStatus.Confirmed)
            .SelectMany(i => i.Items)
            .Where(item => context.Entry(item).State != EntityState.Deleted)
            .ToList();

        var allDeliveriesClosed = items.Count > 0
            && items.All(item => item.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed);

        var delivered = items
            .Where(item => ShipmentLoadDischargeRules.RemainingQuantity(item) > Tolerance)
            .ToList();

        var allDischarged = delivered.Count > 0
            && delivered.All(item => item.TicketDeliveredQuantity > Tolerance);

        return new ClosureFacts(allDeliveriesClosed, allDischarged);
    }

    /// <summary>Atalho de <see cref="EvaluateClosureAsync"/> para quem só quer a Concluída.</summary>
    public static async Task<bool> AreAllDeliveriesClosedAsync(
        AppDbContext context,
        Guid shipmentLoadKey,
        ICollection<Guid>? excludedInvoiceKeys) =>
        (await EvaluateClosureAsync(context, shipmentLoadKey, excludedInvoiceKeys)).AllDeliveriesClosed;
```

- [ ] **Step 5: Tirar a marca manual**

- `ShipmentLoad.cs`: apagar a propriedade `IsDischarged` com o XML-doc dela (linhas 151-161). No
  XML-doc de `DischargedQuantity`, "NÃO entra em saldo, faturamento nem status." vira
  "NÃO entra em saldo nem faturamento; a Descarregada lê o ticket por LINHA
  (<c>SalesInvoiceItem.TicketDeliveredQuantity</c>), não esta soma."
- `ShipmentLoadStatus.cs`: no `<summary>`, "Eles refinam o <c>Invoiced</c> pela marca manual
  <c>ShipmentLoad.IsDischarged</c> e pela Conferência de Entregas" vira "Eles refinam o <c>Invoiced</c>
  pelos tickets de descarga (rateio) e pela Conferência de Entregas"; o comentário do
  `Discharged = 8` vira `// Faturada e com peso de ticket em toda linha entregue (GAC-1171)`.
- `git rm` de `ShipmentLoadsMarkDischargedService.cs`, `ShipmentLoadsUndoDischargedService.cs`,
  `ShipmentLoadsMarkDischargedController.cs`, `ShipmentLoadsUndoDischargedController.cs`.
- `ODataConfigurations.cs`: apagar o bloco das linhas 635-642 (comentário + as duas actions).
- `ServiceCollectionExtensions.cs`: apagar `services.AddScoped<ShipmentLoadsMarkDischargedService>();` e
  `services.AddScoped<ShipmentLoadsUndoDischargedService>();`.

- [ ] **Step 6: Mensagens das travas**

`ShipmentLoadsRefuseService.Validate` (linhas 420-433):

```csharp
        // GAC-1171: a carga descarregada foi ACEITA no destino — há ticket em toda linha entregue.
        // Uma recusa por cima contradiria o ticket, então o caminho é excluí-lo primeiro.
        if (load.Status is ShipmentLoadStatus.Discharged)
            throw new ApplicationException(
                $"A carga {load.Code} já foi descarregada no destino. " +
                "Exclua o ticket de descarga antes de registrar recusa.");

        // A Concluída Normal também foi aceita, mas tem mensagem própria: excluir o ticket não a
        // desfaz (a Concluída vence a Descarregada). O primeiro passo de volta é estornar a conferência.
        if (load.Status == ShipmentLoadStatus.Completed && load.LoadType == ShipmentLoadType.Normal)
            throw new ApplicationException(
                $"A carga {load.Code} já foi concluída. " +
                $"{ShipmentLoadCompletionRules.UndoHint(load)} antes de registrar recusa.");
```

`ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment` (linhas 61-66):

```csharp
        // Completed e Discharged: a carga Normal chega a eles pelo GAC-1171. A mercadoria já foi
        // entregue no destino, e não há o que transbordar. A Descarregada tem frase própria porque
        // tem caminho de volta próprio: excluir o ticket de descarga.
        if (load.Status == ShipmentLoadStatus.Discharged)
            throw new ApplicationException(
                $"A carga {load.Code} já foi descarregada no destino. Exclua o ticket de descarga antes de iniciar o transbordo.");
```

`ShippingTransactionsChangeReleaseService.cs` (linhas 204-209):

```csharp
        // Completed (Remoção, ou Normal com a conferência encerrada) e Discharged (GAC-1171): a
        // composição de uma carga entregue não muda. A Descarregada tem frase própria porque tem
        // caminho de volta próprio: excluir o ticket de descarga.
        if (load.Status == ShipmentLoadStatus.Discharged)
            throw new ApplicationException(
                $"A carga {load.Code} já foi descarregada no destino. Exclua o ticket de descarga antes de trocar a liberação.");
```

- [ ] **Step 7: Rodar e ver passar**

Run: `dotnet build SiagroB1.sln -nodeReuse:false -v q` (0 erros; se aparecer referência a
`IsDischarged`/`MarkDischarged` em arquivo não listado, é resto a limpar) e depois
`dotnet test SiagroB1.Application.Tests --no-build`
Expected: todos verdes.

- [ ] **Step 8: Commit**

```text
feat(shipment): derivar a carga Descarregada dos tickets de descarga

O usuário pediu que a carga vire Descarregada sozinha quando todos os
documentos de saída Normais e Confirmados tiverem peso de descarga, no lugar
do botão "Marcar como Descarregada". A regra lê o TicketDeliveredQuantity de
cada linha entregue (faturado − devolvido > 0), na mesma leitura rastreada da
Conferência; a Concluída continua vencendo.

Saem a marca IsDischarged, as actions Marcar/Desfazer e seus serviços. O
caminho de volta passa a ser excluir o ticket, e as travas de Recusa,
Transbordo e Troca de Liberação dizem isso.

Atenção: a coluna IsDischarged só sai do banco na migration da Task 5.

Refs: GAC-1171
```

`git add -A` dos arquivos listados (inclui as remoções) e `git commit -F <arquivo>`. Se o commit for
negado pelo classificador por causa das remoções, siga a Global Constraint.

---

### Task 3: Validação pura do rateio

Repo: backend.

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargeLine.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargeRules.cs`
- Modify: `SiagroB1.Domain/Entities/ShipmentLoadChangeLogFields.cs:81-82`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargeRulesTests.cs`

**Interfaces:**
- Produces: `public sealed record ShipmentLoadDischargeLine(Guid SalesInvoiceItemKey, decimal Quantity)`;
  `ShipmentLoadDischargeRules.RoundQuantity(decimal) : decimal`;
  `ShipmentLoadDischargeRules.NormalizeDistribution(decimal ticketQuantity, IReadOnlyList<ShipmentLoadDischargeLine>? lines) : IReadOnlyList<ShipmentLoadDischargeLine>`;
  `ShipmentLoadChangeLogFields.DescribeDischarge(string ticketNumber, decimal quantity, IEnumerable<(string? InvoiceNumber, decimal Quantity)> shares) : string`.

- [ ] **Step 1: Testes que falham**

Criar `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargeRulesTests.cs` e `git add`:

```csharp
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 (rateio): a soma das parcelas fecha com o peso do ticket, em 3 casas, com tolerância de
/// 0,001. É a mesma regra da tela — o servidor confere de novo porque nada impede a tela de mandar
/// outra coisa.
/// </summary>
public class ShipmentLoadDischargeRulesTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void Drops_the_zero_shares_and_keeps_the_order()
    {
        var lines = ShipmentLoadDischargeRules.NormalizeDistribution(
            35_000m, [new(A, 20_000m), new(Guid.NewGuid(), 0m), new(B, 15_000m)]);

        Assert.Equal(new[] { A, B }, lines.Select(l => l.SalesInvoiceItemKey));
    }

    [Fact]
    public void Rounds_each_share_to_three_decimals()
    {
        var lines = ShipmentLoadDischargeRules.NormalizeDistribution(
            1_000m, [new(A, 333.3334m), new(B, 666.6666m)]);

        Assert.Equal(new[] { 333.333m, 666.667m }, lines.Select(l => l.Quantity));
    }

    /// <summary>Review Focus 3: ruído de ponto flutuante vindo da tela não é erro do usuário.</summary>
    [Fact]
    public void Float_noise_within_the_tolerance_is_accepted()
    {
        var lines = ShipmentLoadDischargeRules.NormalizeDistribution(
            35_000.0004m, [new(A, 20_000m), new(B, 15_000m)]);

        Assert.Equal(2, lines.Count);
    }

    /// <summary>Review Focus 3: a mensagem nomeia os dois números, em pt-BR.</summary>
    [Fact]
    public void A_distribution_that_does_not_close_is_refused_naming_both_numbers()
    {
        var ex = Assert.Throws<DefaultException>(() => ShipmentLoadDischargeRules.NormalizeDistribution(
            35_000m, [new(A, 20_000m), new(B, 5_000m)]));

        Assert.Equal("O rateio (25.000,000) não fecha com o peso descarregado (35.000,000).", ex.Message);
    }

    [Fact]
    public void A_negative_share_is_refused()
    {
        var ex = Assert.Throws<DefaultException>(() => ShipmentLoadDischargeRules.NormalizeDistribution(
            10_000m, [new(A, 12_000m), new(B, -2_000m)]));

        Assert.Equal("O peso rateado não pode ser negativo.", ex.Message);
    }

    [Fact]
    public void A_repeated_item_is_refused()
    {
        var ex = Assert.Throws<DefaultException>(() => ShipmentLoadDischargeRules.NormalizeDistribution(
            10_000m, [new(A, 5_000m), new(A, 5_000m)]));

        Assert.Equal("O mesmo item de documento de saída aparece duas vezes no rateio.", ex.Message);
    }

    [Fact]
    public void Nothing_distributed_is_refused()
    {
        const string message = "Distribua o peso descarregado entre os documentos de saída.";

        Assert.Equal(message, Assert.Throws<DefaultException>(
            () => ShipmentLoadDischargeRules.NormalizeDistribution(10_000m, [new(A, 0m)])).Message);
        Assert.Equal(message, Assert.Throws<DefaultException>(
            () => ShipmentLoadDischargeRules.NormalizeDistribution(10_000m, [])).Message);
        Assert.Equal(message, Assert.Throws<DefaultException>(
            () => ShipmentLoadDischargeRules.NormalizeDistribution(10_000m, null)).Message);
    }

    [Fact]
    public void Rounds_the_ticket_weight_to_three_decimals()
    {
        Assert.Equal(39_500.000m, ShipmentLoadDischargeRules.RoundQuantity(39_500.0004m));
    }

    [Fact]
    public void Change_log_describes_the_distribution()
    {
        Assert.Equal(
            "T-1 — 35.000,000 (000100: 20.000,000; (sem número): 15.000,000)",
            ShipmentLoadChangeLogFields.DescribeDischarge("T-1", 35_000m, [("000100", 20_000m), (null, 15_000m)]));

        Assert.Equal("T-1 — 35.000,000", ShipmentLoadChangeLogFields.DescribeDischarge("T-1", 35_000m, []));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet build SiagroB1.sln -nodeReuse:false -v q`
Expected: FAIL de compilação — `ShipmentLoadDischargeLine`, `NormalizeDistribution`, `RoundQuantity` e a
sobrecarga de `DescribeDischarge` não existem.

- [ ] **Step 3: Implementar**

Criar `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargeLine.cs` e `git add`:

```csharp
namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Uma parcela do rateio do ticket de descarga, como chega da tela (GAC-1171, rateio): a linha da
/// nota e o peso dela. A nota não viaja — o servidor a resolve pela linha.
/// </summary>
public sealed record ShipmentLoadDischargeLine(Guid SalesInvoiceItemKey, decimal Quantity);
```

Em `ShipmentLoadDischargeRules.cs`: acrescentar `using System.Globalization;` e, depois de
`RemainingQuantity`:

```csharp
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Peso em 3 casas, a escala de <c>DECIMAL(18,3)</c>.</summary>
    public static decimal RoundQuantity(decimal quantity) =>
        decimal.Round(quantity, 3, MidpointRounding.ToEven);

    /// <summary>
    /// Normaliza e valida o rateio (GAC-1171, rateio): arredonda cada parcela em 3 casas, recusa
    /// parcela negativa e item repetido, descarta as parcelas zero, exige ao menos uma, e exige que
    /// a soma feche com o peso do ticket dentro de <see cref="Tolerance"/>.
    /// </summary>
    /// <returns>As parcelas maiores que zero, na ordem recebida.</returns>
    public static IReadOnlyList<ShipmentLoadDischargeLine> NormalizeDistribution(
        decimal ticketQuantity, IReadOnlyList<ShipmentLoadDischargeLine>? lines)
    {
        var rounded = (lines ?? [])
            .Select(line => line with { Quantity = RoundQuantity(line.Quantity) })
            .ToList();

        if (rounded.Any(line => line.Quantity < decimal.Zero))
            throw new DefaultException("O peso rateado não pode ser negativo.");

        if (rounded.GroupBy(line => line.SalesInvoiceItemKey).Any(group => group.Count() > 1))
            throw new DefaultException("O mesmo item de documento de saída aparece duas vezes no rateio.");

        var positive = rounded.Where(line => line.Quantity > decimal.Zero).ToList();

        if (positive.Count == 0)
            throw new DefaultException("Distribua o peso descarregado entre os documentos de saída.");

        var distributed = positive.Sum(line => line.Quantity);

        if (Math.Abs(distributed - ticketQuantity) > Tolerance)
            throw new DefaultException(
                $"O rateio ({distributed.ToString("N3", PtBr)}) não fecha com o peso descarregado " +
                $"({RoundQuantity(ticketQuantity).ToString("N3", PtBr)}).");

        return positive;
    }
```

Em `ShipmentLoadChangeLogFields.cs`, depois do `DescribeDischarge` de 2 parâmetros:

```csharp
    /// <summary>
    /// O ticket com o rateio (GAC-1171, rateio): "123 — 35.000,000 (000100: 20.000,000; 000101:
    /// 15.000,000)". Nota ainda sem número aparece como "(sem número)". O serviço de log trunca em
    /// 500 caracteres.
    /// </summary>
    public static string DescribeDischarge(
        string ticketNumber, decimal quantity, IEnumerable<(string? InvoiceNumber, decimal Quantity)> shares)
    {
        var parts = shares
            .Select(share =>
                $"{(string.IsNullOrWhiteSpace(share.InvoiceNumber) ? "(sem número)" : share.InvoiceNumber)}: " +
                share.Quantity.ToString("N3", PtBr))
            .ToList();

        var head = DescribeDischarge(ticketNumber, quantity);

        return parts.Count == 0 ? head : $"{head} ({string.Join("; ", parts)})";
    }
```

- [ ] **Step 4: Rodar e ver passar**

Run: build + `dotnet test SiagroB1.Application.Tests --no-build --filter "FullyQualifiedName~ShipmentLoadDischargeRulesTests"`
Expected: 9 aprovados.

- [ ] **Step 5: Commit**

```text
feat(shipment): validar o rateio do ticket de descarga

O ticket passa a ser rateado entre as linhas dos documentos de saída da carga.
A regra que a tela aplica é conferida de novo no servidor: parcelas em 3 casas,
sem negativa nem item repetido, e a soma fechando com o peso do ticket dentro
de 0,001. O log descreve o rateio junto com o ticket.

Refs: GAC-1171
```

---

### Task 4: Ticket = cabeçalho + rateio, de ponta a ponta no backend

Repo: backend. Esta task é grande porque a troca de modelo quebra a compilação de tudo o que lê
`ShipmentLoadDischarge.SalesInvoiceKey`: entidade, serviços, controllers, EDM e testes andam juntos.

**Files:**
- Create: `SiagroB1.Domain/Entities/ShipmentLoadDischargeItem.cs`
- Modify: `SiagroB1.Domain/Entities/ShipmentLoadDischarge.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs:73,270-280`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs:203,693-731`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargeRules.cs` (`EnsureLoadAcceptsChanges`, `ResolveLinesAsync`)
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargesRecalculateService.cs` (reescrita)
- Modify: `ShipmentLoadDischargesCreateService.cs`, `ShipmentLoadDischargesUpdateService.cs`, `ShipmentLoadDischargesDeleteService.cs` (reescritas)
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsClosureHookService.cs` (sobrecarga por carga)
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsDeleteService.cs:87-89,123`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesDeleteService.cs:23-32`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsDeleteService.cs:39-47`
- Modify: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadActionParameters.cs` (`TryReadDistribution`)
- Modify: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsDischargeCreateController.cs`, `...UpdateController.cs`, `...DeleteController.cs`
- Modify: `SiagroB1.Web/Controllers/ShipmentLoadsDischargesController.cs:23-26` (comentário)
- Test (rewrite): `ShipmentLoadDischargesServiceTests.cs`, `ShipmentLoadDischargesRecalculateServiceTests.cs`
- Test (edit): `SalesInvoices/SalesInvoicesDeleteDischargeGuardTests.cs`, `ShipmentLoadDischargeModelTests.cs`,
  `ShipmentLoadDischargeEdmModelTests.cs`, `ShipmentLoadsDeleteServiceTests.cs`, `ShipmentLoadActionParametersTests.cs`

**Interfaces:**
- Consumes: Task 1 (`RemainingQuantity`, `Tolerance`), Task 2 (status derivado), Task 3
  (`ShipmentLoadDischargeLine`, `NormalizeDistribution`, `RoundQuantity`, `DescribeDischarge` com rateio).
- Produces:
  - `ShipmentLoadDischargeItem { Guid? Key; Guid DischargeKey; Guid SalesInvoiceKey; Guid SalesInvoiceItemKey; decimal Quantity; navegações Discharge, SalesInvoice, SalesInvoiceItem }`
  - `ShipmentLoadDischarge.Items : ICollection<ShipmentLoadDischargeItem>`; `AppDbContext.ShipmentLoadsDischargesItems`; entity set `ShipmentLoadsDischargesItems`
  - `ShipmentLoadDischargeRules.ResolveLinesAsync(AppDbContext, Guid loadKey, IReadOnlyList<ShipmentLoadDischargeLine>) : Task<IReadOnlyList<ShipmentLoadDischargeItem>>`
  - `ShipmentLoadsClosureHookService.ApplyAsync(Guid shipmentLoadKey, string userName) : Task`
  - `ShipmentLoadDischargesCreateService.ExecuteAsync(Guid loadKey, string? ticketNumber, DateTime dischargeDate, decimal quantity, IReadOnlyList<ShipmentLoadDischargeLine> lines, string? comments, Guid? attachmentKey, string userName) : Task<ShipmentLoadDischarge>`
  - `ShipmentLoadDischargesUpdateService.ExecuteAsync(Guid dischargeKey, string? ticketNumber, DateTime dischargeDate, decimal quantity, IReadOnlyList<ShipmentLoadDischargeLine> lines, string? comments, string userName) : Task`
  - `ShipmentLoadActionParameters.TryReadDistribution(IDictionary<string, object> parameters, out List<ShipmentLoadDischargeLine> lines, out string? error) : bool`
  - Actions `ShipmentLoadsDischargeCreate`/`Update` com `SalesInvoiceItemKeys` + `Quantities`; sem `SalesInvoiceKey`/`SalesInvoiceItemKey`.

- [ ] **Step 1: Reescrever os testes de serviço (falham)**

Substituir `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargesServiceTests.cs` inteiro por:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Registro de descarga RATEADO (GAC-1171, rateio): o ticket e as parcelas, os guards, as somas por
/// linha e por carga, a Descarregada automática e o log.
/// </summary>
public class ShipmentLoadDischargesServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadDischargesRecalculateService Recalculate() => new(_db.Context);
    private ShipmentLoadsChangeLogService ChangeLog() => new(_db.Context);
    private ShipmentLoadsClosureHookService ClosureHook() => new(_db.Context, ChangeLog());

    private ShipmentLoadDischargesCreateService CreateService() => new(
        _db.Context, Recalculate(), ClosureHook(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesCreateService>.Instance);

    private ShipmentLoadDischargesUpdateService UpdateService() => new(
        _db.Context, Recalculate(), ClosureHook(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesUpdateService>.Instance);

    private ShipmentLoadDischargesDeleteService DeleteService() => new(
        _db.Context, Recalculate(), ClosureHook(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesDeleteService>.Instance);

    private ShipmentLoad _load = null!;
    private SalesInvoice _invoice = null!;
    private SalesInvoiceItem _item = null!;
    private SalesInvoice _other = null!;
    private SalesInvoiceItem _otherItem = null!;

    /// <summary>
    /// Um caminhão de 40 t faturado em DUAS notas Normais de 20 t (000100 e 000101) — o caso do
    /// pedido: um ticket só, rateado entre as notas.
    /// </summary>
    private async Task SeedAsync(
        ShipmentLoadStatus status = ShipmentLoadStatus.Invoiced,
        InvoiceStatus firstInvoiceStatus = InvoiceStatus.Confirmed,
        decimal totalQuantity = 40_000m)
    {
        _load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = status,
            TotalQuantity = totalQuantity,
            InvoicedQuantity = 40_000m,
        };
        _db.Context.ShipmentLoads.Add(_load);

        (_invoice, _item) = AddInvoice("000100", 20_000m, firstInvoiceStatus);
        (_other, _otherItem) = AddInvoice("000101", 20_000m, InvoiceStatus.Confirmed);

        await _db.Context.SaveChangesAsync();
    }

    private (SalesInvoice Invoice, SalesInvoiceItem Item) AddInvoice(
        string number,
        decimal quantity,
        InvoiceStatus status,
        SalesInvoiceType type = SalesInvoiceType.Normal,
        Guid? loadKey = null)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceNumber = number,
            ShipmentLoadKey = loadKey ?? _load.Key,
            InvoiceStatus = status,
            InvoiceType = type,
        };

        var item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
        };

        invoice.Items.Add(item);
        _db.Context.SalesInvoices.Add(invoice);

        return (invoice, item);
    }

    private static ShipmentLoadDischargeLine Line(SalesInvoiceItem item, decimal quantity) =>
        new(item.Key!.Value, quantity);

    /// <summary>O ticket do caso comum: 39,5 t rateadas meio a meio entre as duas notas.</summary>
    private ShipmentLoadDischargeLine[] HalfAndHalf() =>
        [Line(_item, 19_750m), Line(_otherItem, 19_750m)];

    private Task<ShipmentLoadDischarge> CreateAsync(
        decimal quantity, IReadOnlyList<ShipmentLoadDischargeLine> lines, string ticket = "T-1") =>
        CreateService().ExecuteAsync(
            _load.Key, ticket, new DateTime(2026, 9, 17), quantity, lines,
            "descarga na trading", null, "paulo");

    private Task UpdateAsync(
        ShipmentLoadDischarge discharge, decimal quantity, IReadOnlyList<ShipmentLoadDischargeLine> lines) =>
        UpdateService().ExecuteAsync(
            discharge.Key!.Value, "T-1A", new DateTime(2026, 9, 18), quantity, lines, "corrigido", "paulo");

    private List<ShipmentLoadChangeLog> LogsOf(string field) =>
        _db.Context.ShipmentLoadsChangeLogs
            .Where(l => l.ShipmentLoadKey == _load.Key && l.Field == field)
            .ToList();

    [Fact]
    public async Task Create_stores_the_ticket_and_its_distribution_and_sums_each_line()
    {
        await SeedAsync();

        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        Assert.Equal("T-1", discharge.TicketNumber);
        Assert.Equal(39_500m, discharge.DischargedQuantity);
        Assert.Equal("paulo", discharge.CreatedBy);
        Assert.Equal(2, discharge.Items.Count);
        Assert.All(discharge.Items, line => Assert.Equal(19_750m, line.Quantity));
        Assert.Contains(discharge.Items, line => line.SalesInvoiceKey == _invoice.Key);
        Assert.Contains(discharge.Items, line => line.SalesInvoiceKey == _other.Key);

        Assert.Equal(19_750m, _item.TicketDeliveredQuantity);
        Assert.Equal(19_750m, _otherItem.TicketDeliveredQuantity);
        Assert.Equal(39_500m, _load.DischargedQuantity);

        var log = Assert.Single(LogsOf(ShipmentLoadChangeLogFields.Discharge));
        Assert.Null(log.OldValue);
        Assert.Equal("T-1 — 39.500,000 (000100: 19.750,000; 000101: 19.750,000)", log.NewValue);
    }

    [Fact]
    public async Task The_ticket_that_completes_every_line_makes_the_load_discharged_and_logs_who_did_it()
    {
        await SeedAsync();

        await CreateAsync(39_500m, HalfAndHalf());

        Assert.Equal(ShipmentLoadStatus.Discharged, _load.Status);
        var status = Assert.Single(LogsOf(ShipmentLoadChangeLogFields.Status));
        Assert.Equal("Faturada", status.OldValue);
        Assert.Equal("Descarregada", status.NewValue);
        Assert.Equal("paulo", status.ChangedBy);
    }

    [Fact]
    public async Task A_ticket_on_one_of_two_invoices_keeps_the_load_invoiced_until_the_second_one()
    {
        await SeedAsync();

        await CreateAsync(20_000m, [Line(_item, 20_000m)], "T-1");

        Assert.Equal(ShipmentLoadStatus.Invoiced, _load.Status);
        Assert.Empty(LogsOf(ShipmentLoadChangeLogFields.Status));

        await CreateAsync(19_800m, [Line(_otherItem, 19_800m)], "T-2");

        Assert.Equal(ShipmentLoadStatus.Discharged, _load.Status);
    }

    [Fact]
    public async Task Deleting_the_ticket_zeroes_the_sums_and_returns_the_load_to_invoiced()
    {
        await SeedAsync();
        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        await DeleteService().ExecuteAsync(discharge.Key!.Value, "paulo");

        Assert.Empty(_db.Context.ShipmentLoadsDischarges);
        Assert.Empty(_db.Context.ShipmentLoadsDischargesItems);
        Assert.Equal(0m, _item.TicketDeliveredQuantity);
        Assert.Equal(0m, _otherItem.TicketDeliveredQuantity);
        Assert.Equal(0m, _load.DischargedQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, _load.Status);

        var deletion = Assert.Single(LogsOf(ShipmentLoadChangeLogFields.Discharge), l => l.NewValue is null);
        Assert.Contains("000101: 19.750,000", deletion.OldValue);
    }

    /// <summary>As três operações que o ticket conhece, para a Theory da regra central.</summary>
    public enum DischargeOperation
    {
        Create,
        Update,
        Delete,
    }

    /// <summary>
    /// ⚠️ O TESTE QUE GUARDA A REGRA CENTRAL do GAC-1171. Registrar, alterar ou excluir um ticket NÃO
    /// pode mexer na conferência de entrega nem no saldo do contrato ou da liberação — com a entrega
    /// ABERTA ou ENCERRADA. Continua valendo com o rateio e com o recálculo de status que o ticket
    /// agora dispara.
    /// </summary>
    [Theory]
    [InlineData(DischargeOperation.Create, SalesInvoiceDeliveryStatus.Open)]
    [InlineData(DischargeOperation.Create, SalesInvoiceDeliveryStatus.Closed)]
    [InlineData(DischargeOperation.Update, SalesInvoiceDeliveryStatus.Open)]
    [InlineData(DischargeOperation.Update, SalesInvoiceDeliveryStatus.Closed)]
    [InlineData(DischargeOperation.Delete, SalesInvoiceDeliveryStatus.Open)]
    [InlineData(DischargeOperation.Delete, SalesInvoiceDeliveryStatus.Closed)]
    public async Task Ticket_leaves_the_reconciliation_and_the_balances_untouched(
        DischargeOperation operation, SalesInvoiceDeliveryStatus deliveryStatus)
    {
        await SeedAsync();
        var (contract, release) = await SeedContractAndReleaseAsync();

        _item.DeliveredQuantity = 19_000m;
        _item.QuantityLoss = 250m;
        _item.DeliveryStatus = deliveryStatus;
        await _db.Context.SaveChangesAsync();

        var discharge = operation == DischargeOperation.Create ? null : await CreateAsync(39_500m, HalfAndHalf());

        var deliveredBefore = _item.DeliveredQuantity;
        var lossBefore = _item.QuantityLoss;
        var deliveryStatusBefore = _item.DeliveryStatus;
        var allocatedBefore = contract.AllocatedVolume;
        var availableBefore = contract.AvaiableVolume;
        var totalReleasesBefore = contract.TotalShipmentReleases;
        var shippedBefore = release.ShippedQuantity;
        var releaseAvailableBefore = release.AvailableQuantity;

        var (expectedLine, expectedLoad) = operation switch
        {
            DischargeOperation.Create => (19_750m, 39_500m),
            DischargeOperation.Update => (40_100m, 40_100m),
            _ => (0m, 0m),
        };

        switch (operation)
        {
            case DischargeOperation.Create:
                await CreateAsync(39_500m, HalfAndHalf());
                break;
            case DischargeOperation.Update:
                await UpdateAsync(discharge!, 40_100m, [Line(_item, 40_100m)]);
                break;
            default:
                await DeleteService().ExecuteAsync(discharge!.Key!.Value, "paulo");
                break;
        }

        Assert.Equal(expectedLine, _item.TicketDeliveredQuantity);
        Assert.Equal(expectedLoad, _load.DischargedQuantity);

        Assert.Equal(deliveredBefore, _item.DeliveredQuantity);
        Assert.Equal(lossBefore, _item.QuantityLoss);
        Assert.Equal(deliveryStatusBefore, _item.DeliveryStatus);

        Assert.Equal(allocatedBefore, contract.AllocatedVolume);
        Assert.Equal(availableBefore, contract.AvaiableVolume);
        Assert.Equal(totalReleasesBefore, contract.TotalShipmentReleases);

        Assert.Equal(shippedBefore, release.ShippedQuantity);
        Assert.Equal(releaseAvailableBefore, release.AvailableQuantity);
    }

    private async Task<(SalesContract Contract, SalesShipmentRelease Release)> SeedContractAndReleaseAsync()
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC-1171",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            TotalVolume = 100000m,
            AllocatedVolume = 40000m,
            Status = ContractStatus.Approved,
        };

        var release = new SalesShipmentRelease
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            DeliveryLocationCode = "01",
            ReleasedQuantity = 60000m,
            ShippedQuantity = 40000m,
            Status = ReleaseStatus.Actived,
        };

        contract.SalesShipmentReleases.Add(release);
        _db.Context.SalesContracts.Add(contract);

        _item.SalesContractKey = contract.Key;
        _item.SalesShipmentReleaseKey = release.Key;

        await _db.Context.SaveChangesAsync();

        return (contract, release);
    }

    [Fact]
    public async Task Update_redistributes_and_resums_the_line_that_left_the_distribution()
    {
        await SeedAsync();
        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        await UpdateAsync(discharge, 40_100m, [Line(_item, 40_100m)]);

        Assert.Equal("T-1A", discharge.TicketNumber);
        Assert.Equal(40_100m, discharge.DischargedQuantity);
        Assert.Equal("paulo", discharge.UpdatedBy);
        Assert.Equal(40_100m, Assert.Single(discharge.Items).Quantity);
        Assert.Equal(40_100m, _item.TicketDeliveredQuantity);
        Assert.Equal(0m, _otherItem.TicketDeliveredQuantity);
        Assert.Equal(40_100m, _load.DischargedQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, _load.Status);
    }

    /// <summary>
    /// Review Focus 1: uma nota do rateio foi cancelada depois do ticket. A tela não a oferece mais,
    /// e alterar o ticket sem ela precisa tirar a parcela e zerar a soma daquela linha.
    /// </summary>
    [Fact]
    public async Task Editing_after_an_invoice_was_cancelled_drops_its_line_and_zeroes_its_sum()
    {
        await SeedAsync();
        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        _other.InvoiceStatus = InvoiceStatus.Cancelled;
        await _db.Context.SaveChangesAsync();

        await UpdateAsync(discharge, 39_500m, [Line(_item, 39_500m)]);

        Assert.Equal(_item.Key!.Value, Assert.Single(discharge.Items).SalesInvoiceItemKey);
        Assert.Equal(39_500m, _item.TicketDeliveredQuantity);
        Assert.Equal(0m, _otherItem.TicketDeliveredQuantity);
    }

    [Fact]
    public async Task Editing_with_the_line_of_a_cancelled_invoice_is_refused()
    {
        await SeedAsync();
        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        _other.InvoiceStatus = InvoiceStatus.Cancelled;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => UpdateAsync(discharge, 39_500m, HalfAndHalf()));

        Assert.Contains("000101", ex.Message);
    }

    [Fact]
    public async Task A_distribution_that_does_not_close_is_refused_and_nothing_is_saved()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateAsync(39_500m, [Line(_item, 19_750m), Line(_otherItem, 19_000m)]));

        Assert.Contains("não fecha", ex.Message);
        Assert.Empty(_db.Context.ShipmentLoadsDischarges);
        Assert.Equal(0m, _item.TicketDeliveredQuantity);
    }

    [Fact]
    public async Task A_line_of_another_load_is_refused()
    {
        await SeedAsync();
        var (_, stranger) = AddInvoice("000999", 10_000m, InvoiceStatus.Confirmed, loadKey: Guid.NewGuid());
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateAsync(10_000m, [Line(stranger, 10_000m)]));

        Assert.Equal("O documento de saída 000999 não pertence a esta carga.", ex.Message);
    }

    [Fact]
    public async Task A_pending_invoice_line_is_refused()
    {
        await SeedAsync(firstInvoiceStatus: InvoiceStatus.Pending);

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateAsync(20_000m, [Line(_item, 20_000m)]));

        Assert.Equal(
            "O documento de saída 000100 não está confirmado. Só documento confirmado recebe descarga.",
            ex.Message);
    }

    [Fact]
    public async Task A_return_invoice_line_is_refused()
    {
        await SeedAsync();
        var (_, returnItem) = AddInvoice("000200", 5_000m, InvoiceStatus.Confirmed, SalesInvoiceType.Return);
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateAsync(5_000m, [Line(returnItem, 5_000m)]));

        Assert.Equal("O documento 000200 é de devolução e não recebe descarga.", ex.Message);
    }

    [Fact]
    public async Task A_line_returned_in_full_is_refused()
    {
        await SeedAsync();
        _item.ReturnedQuantity = 20_000m;
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateAsync(20_000m, [Line(_item, 20_000m)]));

        Assert.Equal(
            "O documento de saída 000100 foi devolvido por inteiro e não recebe descarga.",
            ex.Message);
    }

    [Fact]
    public async Task A_cancelled_load_refuses_the_three_operations()
    {
        await SeedAsync();
        var discharge = await CreateAsync(39_500m, HalfAndHalf());

        _load.Status = ShipmentLoadStatus.Cancelled;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(1_000m, [Line(_item, 1_000m)], "T-2"));
        await Assert.ThrowsAsync<DefaultException>(() => UpdateAsync(discharge, 39_500m, HalfAndHalf()));
        await Assert.ThrowsAsync<DefaultException>(
            () => DeleteService().ExecuteAsync(discharge.Key!.Value, "paulo"));
    }

    /// <summary>
    /// D6: recusa parcial com volta ao armazém deixa a carga "Devolvida". A parte entregue ainda tem
    /// ticket para lançar (o frete depende dele), e lançá-lo ou excluí-lo não tira a carga de Devolvida.
    /// </summary>
    [Fact]
    public async Task A_mixed_returned_load_accepts_and_deletes_a_ticket_and_stays_returned()
    {
        await SeedAsync(status: ShipmentLoadStatus.Returned);

        // Metade da carga voltou ao armazém: a nota dela saiu do jogo e o romaneio de devolução abate
        // o saldo. Os números fecham em "Devolvida" no recálculo.
        _other.InvoiceStatus = InvoiceStatus.Cancelled;
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R9",
            CardCode = "C001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM99",
            GrossWeight = 20_000m,
            NetWeight = 20_000m,
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            RefusedFromShipmentLoadKey = _load.Key,
        });
        await _db.Context.SaveChangesAsync();

        var discharge = await CreateAsync(19_900m, [Line(_item, 19_900m)]);

        Assert.Equal(19_900m, _item.TicketDeliveredQuantity);
        Assert.Equal(ShipmentLoadStatus.Returned, _load.Status);

        await DeleteService().ExecuteAsync(discharge.Key!.Value, "paulo");

        Assert.Equal(0m, _item.TicketDeliveredQuantity);
        Assert.Equal(ShipmentLoadStatus.Returned, _load.Status);
    }

    /// <summary>Review Focus 5: o ticket não "promove" a carga que ainda tem saldo a faturar.</summary>
    [Fact]
    public async Task A_partially_invoiced_load_accepts_a_ticket_without_changing_its_status()
    {
        await SeedAsync(status: ShipmentLoadStatus.PartiallyInvoiced, totalQuantity: 60_000m);

        await CreateAsync(39_500m, HalfAndHalf());

        Assert.Equal(ShipmentLoadStatus.PartiallyInvoiced, _load.Status);
        Assert.Empty(LogsOf(ShipmentLoadChangeLogFields.Status));
    }

    /// <summary>Review Focus 3: o peso do ticket é gravado na escala da coluna.</summary>
    [Fact]
    public async Task The_ticket_weight_is_stored_in_three_decimals()
    {
        await SeedAsync();

        var discharge = await CreateAsync(39_500.0004m, HalfAndHalf());

        Assert.Equal(39_500.000m, discharge.DischargedQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Non_positive_weight_is_refused(decimal quantity)
    {
        await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(quantity, HalfAndHalf()));
    }

    [Fact]
    public async Task Blank_ticket_number_is_refused()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(39_500m, HalfAndHalf(), "   "));
    }
}
```

Em `ShipmentLoadDischargesRecalculateServiceTests.cs`:
- Substituir o helper `AddTicket` por:

```csharp
    private ShipmentLoadDischarge AddTicket(ShipmentLoad load, SalesInvoice invoice, SalesInvoiceItem item, decimal weight)
    {
        var discharge = new ShipmentLoadDischarge
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = load.Key,
            TicketNumber = "T1",
            DischargeDate = DateTime.Now.Date,
            DischargedQuantity = weight,
        };

        discharge.Items.Add(new ShipmentLoadDischargeItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            SalesInvoiceItemKey = item.Key!.Value,
            Quantity = weight,
        });

        _db.Context.ShipmentLoadsDischarges.Add(discharge);
        return discharge;
    }
```

- Em `Zeroes_the_item_when_the_last_ticket_is_gone`, antes de
  `_db.Context.ShipmentLoadsDischarges.RemoveRange(...)`, acrescentar
  `_db.Context.ShipmentLoadsDischargesItems.RemoveRange(_db.Context.ShipmentLoadsDischargesItems);`.
- Em `Discounts_a_removed_ticket_before_the_delete_is_saved`, antes de
  `_db.Context.ShipmentLoadsDischarges.Remove(toRemove);`, acrescentar
  `_db.Context.ShipmentLoadsDischargesItems.RemoveRange(toRemove.Items);`.
- Acrescentar ao fim da classe:

```csharp
    /// <summary>GAC-1171 (rateio): UM ticket, duas linhas — cada linha soma a sua parcela.</summary>
    [Fact]
    public async Task Sums_the_shares_of_a_ticket_spread_over_two_lines()
    {
        var (load, invoice, item) = await SeedAsync();
        var other = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 15_000m,
        };
        _db.Context.SalesInvoicesItems.Add(other);

        var discharge = new ShipmentLoadDischarge
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = load.Key,
            TicketNumber = "T1",
            DischargeDate = DateTime.Now.Date,
            DischargedQuantity = 35_000m,
        };
        discharge.Items.Add(new ShipmentLoadDischargeItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, SalesInvoiceItemKey = item.Key!.Value, Quantity = 20_000m,
        });
        discharge.Items.Add(new ShipmentLoadDischargeItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, SalesInvoiceItemKey = other.Key!.Value, Quantity = 15_000m,
        });
        _db.Context.ShipmentLoadsDischarges.Add(discharge);
        // A linha nova precisa estar no banco: o recálculo a busca por consulta, e o InMemory não
        // devolve entidade só Added.
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key, [item.Key!.Value, other.Key!.Value]);

        Assert.Equal(20_000m, item.TicketDeliveredQuantity);
        Assert.Equal(15_000m, other.TicketDeliveredQuantity);
        Assert.Equal(35_000m, load.DischargedQuantity);
    }
```

Em `SalesInvoices/SalesInvoicesDeleteDischargeGuardTests.cs`:
- No `<remarks>`, "As quatro FKs de SHIPMENT_LOAD_DISCHARGES são `NoAction`" vira "As FKs de
  SHIPMENT_LOAD_DISCHARGE_ITEMS para a nota e para a linha são `NoAction`", e "registrar ticket em nota
  `Pending` é permitido" vira "o ticket nasce em nota Confirmada e o estorno da confirmação a devolve
  a `Pending`, que é a nota que o delete aceita".
- Substituir `RegisterTicketAsync` por:

```csharp
    /// <summary>
    /// Ticket gravado direto, sobre a nota PENDENTE. Pela tela ele só nasce em nota Confirmada; o
    /// caminho real até aqui é estornar a confirmação depois do ticket.
    /// </summary>
    private async Task RegisterTicketAsync()
    {
        var discharge = new ShipmentLoadDischarge
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = _load.Key,
            TicketNumber = "T-1",
            DischargeDate = new DateTime(2026, 9, 17),
            DischargedQuantity = 39500m,
        };

        discharge.Items.Add(new ShipmentLoadDischargeItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = _invoice.Key,
            SalesInvoiceItemKey = _item.Key!.Value,
            Quantity = 39500m,
        });

        _db.Context.ShipmentLoadsDischarges.Add(discharge);
        await _db.Context.SaveChangesAsync();
    }
```

- Nos dois testes de recusa, depois de `Assert.Single(_db.Context.ShipmentLoadsDischarges);`, acrescentar
  `Assert.Single(_db.Context.ShipmentLoadsDischargesItems);`.

Em `ShipmentLoadDischargeModelTests.cs`:
- Em `ShipmentLoadDischarge_IsMappedToExpectedTableAndColumns`, trocar as duas linhas
  `Assert.Contains(nameof(ShipmentLoadDischarge.SalesInvoiceKey), props);` e `...SalesInvoiceItemKey...` por
  `Assert.DoesNotContain("SalesInvoiceKey", props);` e `Assert.DoesNotContain("SalesInvoiceItemKey", props);`.
- Acrescentar:

```csharp
    [Fact]
    public void ShipmentLoadDischargeItem_is_the_distribution_line()
    {
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(ShipmentLoadDischargeItem));
        Assert.NotNull(entityType);
        Assert.Equal("SHIPMENT_LOAD_DISCHARGE_ITEMS", entityType!.GetTableName());

        // Um ticket não rateia duas vezes a mesma linha. Sem filtro: as colunas não são anuláveis,
        // e índice filtrado exigiria QUOTED_IDENTIFIER em todo script manual.
        var unique = Assert.Single(entityType.GetIndexes(), index => index.IsUnique);
        Assert.Equal(
            new[] { nameof(ShipmentLoadDischargeItem.DischargeKey), nameof(ShipmentLoadDischargeItem.SalesInvoiceItemKey) },
            unique.Properties.Select(p => p.Name));
        Assert.Null(unique.GetFilter());
    }

    [Fact]
    public void The_distribution_goes_with_the_ticket_but_never_with_the_invoice()
    {
        using var context = ModelOnlyContext();

        var foreignKeys = context.Model.FindEntityType(typeof(ShipmentLoadDischargeItem))!
            .GetForeignKeys().ToList();

        DeleteBehavior BehaviorTo(Type principal) =>
            foreignKeys.Single(fk => fk.PrincipalEntityType.ClrType == principal).DeleteBehavior;

        Assert.Equal(DeleteBehavior.Cascade, BehaviorTo(typeof(ShipmentLoadDischarge)));
        Assert.Equal(DeleteBehavior.NoAction, BehaviorTo(typeof(SalesInvoice)));
        Assert.Equal(DeleteBehavior.NoAction, BehaviorTo(typeof(SalesInvoiceItem)));
    }
```

Em `ShipmentLoadDischargeEdmModelTests.cs`:
- Em `Create_action_takes_the_ticket_parameters`, trocar `Assert.Contains("SalesInvoiceKey", names);` e
  `Assert.Contains("SalesInvoiceItemKey", names);` por `Assert.Contains("SalesInvoiceItemKeys", names);` e
  `Assert.Contains("Quantities", names);`.
- Acrescentar:

```csharp
    /// <summary>
    /// GAC-1171 (rateio): arrays PARALELOS de Guid e Double, o precedente provado de
    /// ShipmentLoadsRefuse. A nota não viaja — o servidor a resolve pela linha.
    /// </summary>
    [Theory]
    [InlineData("ShipmentLoadsDischargeCreate")]
    [InlineData("ShipmentLoadsDischargeUpdate")]
    public void The_distribution_travels_as_parallel_guid_and_double_collections(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal(
            "Collection(Edm.Guid)",
            action.Parameters.Single(p => p.Name == "SalesInvoiceItemKeys").Type.Definition.FullTypeName());
        Assert.Equal(
            "Collection(Edm.Double)",
            action.Parameters.Single(p => p.Name == "Quantities").Type.Definition.FullTypeName());
        Assert.DoesNotContain(action.Parameters, p => p.Name is "SalesInvoiceKey" or "SalesInvoiceItemKey");
    }

    [Fact]
    public void Declares_the_distribution_entity_set()
    {
        Assert.NotNull(Model().EntityContainer.FindEntitySet("ShipmentLoadsDischargesItems"));
    }
```

Em `ShipmentLoadsDeleteServiceTests.cs`, no teste `Deletes_a_planned_load_with_a_discharge_ticket_and_an_attachment`,
o ticket ganha uma linha (`Items = { new ShipmentLoadDischargeItem { Key = Guid.NewGuid(), SalesInvoiceKey = Guid.NewGuid(), SalesInvoiceItemKey = Guid.NewGuid(), Quantity = 100m } },`
dentro do inicializador) e o fim do teste ganha `Assert.Empty(_db.Context.ShipmentLoadsDischargesItems);`.

Em `ShipmentLoadActionParametersTests.cs`, acrescentar (precisa de `using SiagroB1.Application.Services.ShipmentLoads;`):

```csharp
    [Fact]
    public void Reads_the_parallel_distribution_arrays()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var parameters = new Dictionary<string, object>
        {
            ["SalesInvoiceItemKeys"] = new List<Guid> { a, b },
            ["Quantities"] = new List<double> { 20000.5, 15000 },
        };

        Assert.True(ShipmentLoadActionParameters.TryReadDistribution(parameters, out var lines, out var error));
        Assert.Null(error);
        Assert.Equal(new[] { new ShipmentLoadDischargeLine(a, 20000.5m), new ShipmentLoadDischargeLine(b, 15000m) }, lines);
    }

    /// <summary>
    /// Review Focus 2: um array de INTEIROS pode chegar como coleção de int/long. O cast direto para
    /// double devolveria lista vazia sem erro, e o ticket seria recusado por "rateio vazio".
    /// </summary>
    [Fact]
    public void Reads_integer_quantities_as_numbers()
    {
        var parameters = new Dictionary<string, object>
        {
            ["SalesInvoiceItemKeys"] = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
            ["Quantities"] = new List<object> { 20000, 15000L },
        };

        Assert.True(ShipmentLoadActionParameters.TryReadDistribution(parameters, out var lines, out _));
        Assert.Equal(new[] { 20000m, 15000m }, lines.Select(l => l.Quantity));
    }

    [Fact]
    public void Refuses_arrays_of_different_sizes_and_a_missing_distribution()
    {
        var mismatched = new Dictionary<string, object>
        {
            ["SalesInvoiceItemKeys"] = new List<Guid> { Guid.NewGuid() },
            ["Quantities"] = new List<double> { 1, 2 },
        };

        Assert.False(ShipmentLoadActionParameters.TryReadDistribution(mismatched, out _, out var error));
        Assert.Equal("A lista de itens e a de pesos do rateio têm tamanhos diferentes.", error);

        Assert.False(ShipmentLoadActionParameters.TryReadDistribution(
            new Dictionary<string, object>(), out _, out var missing));
        Assert.Equal(ShipmentLoadActionParameters.MissingDistributionMessage, missing);
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet build SiagroB1.sln -nodeReuse:false -v q`
Expected: FAIL de compilação — `ShipmentLoadDischargeItem`, `ShipmentLoadsDischargesItems`,
`TryReadDistribution` e as novas assinaturas não existem.

- [ ] **Step 3: Entidade de rateio e cabeçalho**

Criar `SiagroB1.Domain/Entities/ShipmentLoadDischargeItem.cs` e `git add`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Parcela do ticket de descarga numa linha de documento de saída (GAC-1171, rateio).
/// </summary>
/// <remarks>
/// O ticket pesa o caminhão inteiro, e a carga pode ter vários documentos de saída: o peso do papel
/// (<see cref="ShipmentLoadDischarge.DischargedQuantity"/>) é rateado entre as linhas, e a soma das
/// parcelas fecha com ele (<c>ShipmentLoadDischargeRules.NormalizeDistribution</c>).
/// <para>
/// Guarda a nota E a linha: a linha é o alvo do peso, a nota existe para a aba chegar ao número dela
/// com um <c>$expand</c> de um nível. O par é resolvido no servidor a partir da linha.
/// </para>
/// </remarks>
[Table("SHIPMENT_LOAD_DISCHARGE_ITEMS")]
[Index(nameof(DischargeKey), nameof(SalesInvoiceItemKey), IsUnique = true)]
[Index(nameof(SalesInvoiceItemKey))]
public class ShipmentLoadDischargeItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid DischargeKey { get; set; }
    public virtual ShipmentLoadDischarge? Discharge { get; set; }

    public Guid SalesInvoiceKey { get; set; }
    public virtual SalesInvoice? SalesInvoice { get; set; }

    public Guid SalesInvoiceItemKey { get; set; }
    public virtual SalesInvoiceItem? SalesInvoiceItem { get; set; }

    /// <summary>Parcela do peso do ticket. Mesma escala de <c>DischargedQuantity</c>.</summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }
}
```

Em `ShipmentLoadDischarge.cs`:
- Remover `[Index(nameof(SalesInvoiceItemKey))]`, as propriedades `SalesInvoiceKey`, `SalesInvoice`,
  `SalesInvoiceItemKey`, `SalesInvoiceItem`.
- Trocar o terceiro `<para>` do `<remarks>` ("Guarda a nota E a linha da nota...") por:
  `/// <para>` / `/// É o CABEÇALHO do ticket (GAC-1171, rateio): o peso do papel mora aqui, e o rateio`
  `/// entre as linhas dos documentos de saída mora em <see cref="Items"/>.` / `/// </para>`.
- No XML-doc de `DischargedQuantity`: `/// <summary>Peso do ticket — o número do papel, que o rateio fecha. Mesma escala de <c>StorageTransaction.GrossWeight</c>.</summary>`.
- Acrescentar ao fim da classe:

```csharp
    /// <summary>Rateio do peso entre as linhas dos documentos de saída (GAC-1171, rateio).</summary>
    public virtual ICollection<ShipmentLoadDischargeItem> Items { get; } = [];
```

Em `AppDbContext.cs`:
- Depois de `public DbSet<ShipmentLoadDischarge> ShipmentLoadsDischarges { get; set; }`:
  `public DbSet<ShipmentLoadDischargeItem> ShipmentLoadsDischargesItems { get; set; }`.
- Substituir os dois blocos `modelBuilder.Entity<ShipmentLoadDischarge>()...HasOne(x => x.SalesInvoice)...`
  e `...HasOne(x => x.SalesInvoiceItem)...` (linhas 270-280) por:

```csharp
        // GAC-1171 (rateio): o rateio pertence ao ticket e vai junto com ele (Cascade). As FKs para
        // a nota e para a linha seguem NoAction, como eram no ticket: é o que sustenta a trava de
        // exclusão da nota com descarga registrada (o ticket é a evidência do frete).
        modelBuilder.Entity<ShipmentLoadDischargeItem>()
            .HasOne(x => x.Discharge)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.DischargeKey)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ShipmentLoadDischargeItem>()
            .HasOne(x => x.SalesInvoice)
            .WithMany()
            .HasForeignKey(x => x.SalesInvoiceKey)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ShipmentLoadDischargeItem>()
            .HasOne(x => x.SalesInvoiceItem)
            .WithMany()
            .HasForeignKey(x => x.SalesInvoiceItemKey)
            .OnDelete(DeleteBehavior.NoAction);
```

Em `ODataConfigurations.cs`, depois de `modelBuilder.EntitySet<ShipmentLoadDischarge>("ShipmentLoadsDischarges");`:
`modelBuilder.EntitySet<ShipmentLoadDischargeItem>("ShipmentLoadsDischargesItems");`.

- [ ] **Step 4: Regras — carga e linhas**

Em `ShipmentLoadDischargeRules.cs`, acrescentar os `using` `Microsoft.EntityFrameworkCore;` e
`SiagroB1.Infra.Context;`, e substituir `EnsureLoadAcceptsChanges` por:

```csharp
    /// <summary>
    /// Carga cancelada está congelada: as três operações mexem em quantidade. Nas demais situações
    /// quem decide é a elegibilidade das linhas (<see cref="ResolveLinesAsync"/>) — é o que deixa a
    /// carga mista "Devolvida" registrar o ticket da parte que foi entregue (GAC-1171, rateio, D6).
    /// </summary>
    public static void EnsureLoadAcceptsChanges(ShipmentLoad load)
    {
        if (load.Status == ShipmentLoadStatus.Cancelled)
            throw new DefaultException("Carga cancelada não aceita registro de descarga.");
    }

    /// <summary>
    /// Resolve no servidor a nota de cada parcela e confere a elegibilidade: nota desta carga,
    /// Normal, Confirmada, e com faturado que não voltou. A tela manda só a linha — o par nota/linha
    /// nunca é aceito dela.
    /// </summary>
    /// <returns>
    /// As parcelas prontas para o ticket, na ordem recebida, com as navegações <c>SalesInvoice</c> e
    /// <c>SalesInvoiceItem</c> apontando as instâncias rastreadas (o log usa o número da nota).
    /// </returns>
    public static async Task<IReadOnlyList<ShipmentLoadDischargeItem>> ResolveLinesAsync(
        AppDbContext context, Guid loadKey, IReadOnlyList<ShipmentLoadDischargeLine> lines)
    {
        var keys = lines.Select(line => (Guid?)line.SalesInvoiceItemKey).ToList();

        var items = await context.SalesInvoicesItems
            .Include(x => x.SalesInvoice)
            .Where(x => keys.Contains(x.Key))
            .ToListAsync();

        var result = new List<ShipmentLoadDischargeItem>();

        foreach (var line in lines)
        {
            var item = items.FirstOrDefault(x => x.Key == line.SalesInvoiceItemKey)
                ?? throw new NotFoundException("Item do documento de saída não encontrado.");

            var invoice = item.SalesInvoice
                ?? throw new NotFoundException("Documento de saída não encontrado.");

            var number = string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? "(sem número)" : invoice.InvoiceNumber;

            if (invoice.ShipmentLoadKey != loadKey)
                throw new DefaultException($"O documento de saída {number} não pertence a esta carga.");

            if (invoice.InvoiceType != SalesInvoiceType.Normal)
                throw new DefaultException($"O documento {number} é de devolução e não recebe descarga.");

            if (invoice.InvoiceStatus != InvoiceStatus.Confirmed)
                throw new DefaultException(
                    $"O documento de saída {number} não está confirmado. Só documento confirmado recebe descarga.");

            if (RemainingQuantity(item) <= Tolerance)
                throw new DefaultException(
                    $"O documento de saída {number} foi devolvido por inteiro e não recebe descarga.");

            result.Add(new ShipmentLoadDischargeItem
            {
                SalesInvoiceKey = invoice.Key,
                SalesInvoice = invoice,
                SalesInvoiceItemKey = item.Key!.Value,
                SalesInvoiceItem = item,
                Quantity = line.Quantity,
            });
        }

        return result;
    }
```

Atualizar o `<remarks>` da classe: "O ticket é aceito em qualquer estado da conferência" continua; e
acrescentar ao `<summary>`: "Desde o rateio, também a validação das parcelas e a elegibilidade das linhas."

- [ ] **Step 5: Recálculo das somas pelas linhas**

Substituir o corpo de `ShipmentLoadDischargesRecalculateService` (mantendo o XML-doc da classe, que só
troca "soma de `ShipmentLoadDischarge.DischargedQuantity`" por "soma das parcelas do rateio" no
`TicketDeliveredQuantity`):

```csharp
public class ShipmentLoadDischargesRecalculateService(AppDbContext context)
{
    public async Task RecalculateAsync(Guid shipmentLoadKey, IEnumerable<Guid> salesInvoiceItemKeys)
    {
        foreach (var itemKey in salesInvoiceItemKeys.Distinct())
        {
            var item = await context.SalesInvoicesItems
                .FirstOrDefaultAsync(x => x.Key == itemKey);

            // Chave desconhecida não é erro: a exclusão em lote pode citar item já removido.
            if (item is null) continue;

            item.TicketDeliveredQuantity = await SumSharesAsync(itemKey);
        }

        var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == shipmentLoadKey);

        if (load is not null)
            load.DischargedQuantity = await SumTicketsAsync(shipmentLoadKey);
    }

    /// <summary>Linha da nota: soma das PARCELAS do rateio que apontam para ela.</summary>
    private Task<decimal> SumSharesAsync(Guid itemKey) =>
        SumAsync(
            context.ShipmentLoadsDischargesItems.Where(x => x.SalesInvoiceItemKey == itemKey),
            x => x.SalesInvoiceItemKey == itemKey,
            x => x.Key,
            x => x.Quantity);

    /// <summary>Carga: soma do peso do PAPEL de cada ticket.</summary>
    private Task<decimal> SumTicketsAsync(Guid loadKey) =>
        SumAsync(
            context.ShipmentLoadsDischarges.Where(x => x.ShipmentLoadKey == loadKey),
            x => x.ShipmentLoadKey == loadKey,
            x => x.Key,
            x => x.DischargedQuantity);

    /// <summary>
    /// Soma o que está no banco MAIS o que está no rastreador, em vez de usar <c>SumAsync</c> do EF:
    /// a agregação no servidor não vê a entidade recém-adicionada e ainda não gravada.
    /// </summary>
    /// <remarks>
    /// A consulta entra FILTRADA por chave (<paramref name="persistedQuery"/>); o predicado repete o
    /// filtro para as entidades do rastreador, que o SQL não alcança.
    /// <para>
    /// ⚠️ <c>trackedKeys</c> precisa incluir as entidades <c>Deleted</c> — é o que descarta a linha
    /// ainda física no banco quando o chamador a removeu e recalculou ANTES do <c>SaveChanges</c>. Só
    /// a lista somada (<c>tracked</c>) exclui <c>Deleted</c> — a chave, não.
    /// </para>
    /// </remarks>
    private async Task<decimal> SumAsync<T>(
        IQueryable<T> persistedQuery,
        Func<T, bool> predicate,
        Func<T, Guid?> keyOf,
        Func<T, decimal> selector) where T : class
    {
        var persisted = await persistedQuery.AsNoTracking().ToListAsync();

        var allTracked = context.ChangeTracker.Entries<T>().ToList();

        var trackedKeys = allTracked.Select(e => keyOf(e.Entity)).ToHashSet();

        var tracked = allTracked
            .Where(e => e.State != EntityState.Deleted)
            .Select(e => e.Entity);

        return persisted
            .Where(x => !trackedKeys.Contains(keyOf(x)))
            .Concat(tracked)
            .Where(predicate)
            .Sum(selector);
    }
}
```

- [ ] **Step 6: Gancho de situação por carga**

Em `ShipmentLoadsClosureHookService.cs`, substituir `ApplyAsync(SalesInvoice, string)` por:

```csharp
    public async Task ApplyAsync(SalesInvoice invoice, string userName)
    {
        var loadKey = await SalesInvoiceOriginResolver.ResolveShipmentLoadKeyAsync(context, invoice);
        if (loadKey is null)
            return;

        await ApplyAsync(loadKey.Value, userName);
    }

    /// <summary>
    /// GAC-1171 (rateio): o ticket de descarga muda a situação da carga (Faturada ↔ Descarregada) sem
    /// passar por nota nenhuma — chega aqui pela chave da carga.
    /// </summary>
    public async Task ApplyAsync(Guid shipmentLoadKey, string userName)
    {
        var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == shipmentLoadKey);
        if (load is null)
            return;

        var before = load.Status;

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
            context, load.Key, excludedInvoiceKeys: null);

        if (load.Status == before)
            return;

        load.UpdatedBy = userName;

        changeLog.Register(
            load.Key,
            ShipmentLoadChangeLogFields.Status,
            ShipmentLoadChangeLogFields.DescribeStatus(before),
            ShipmentLoadChangeLogFields.DescribeStatus(load.Status),
            userName);
    }
```

No `<summary>` da classe, acrescentar "e pelas três escritas do ticket de descarga (GAC-1171, rateio)".

- [ ] **Step 7: Os três serviços de escrita**

Substituir o corpo de `ShipmentLoadDischargesCreateService` por:

```csharp
/// <summary>
/// Registra um ticket de descarga na carga (GAC-1171), RATEADO entre linhas de documentos de saída.
/// </summary>
public class ShipmentLoadDischargesCreateService(
    AppDbContext context,
    ShipmentLoadDischargesRecalculateService recalculate,
    ShipmentLoadsClosureHookService closureHook,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadDischargesCreateService> logger)
{
    public async Task<ShipmentLoadDischarge> ExecuteAsync(
        Guid loadKey,
        string? ticketNumber,
        DateTime dischargeDate,
        decimal quantity,
        IReadOnlyList<ShipmentLoadDischargeLine> lines,
        string? comments,
        Guid? attachmentKey,
        string userName)
    {
        try
        {
            var ticket = ShipmentLoadDischargeRules.NormalizeTicketNumber(ticketNumber);
            var total = ShipmentLoadDischargeRules.RoundQuantity(quantity);
            ShipmentLoadDischargeRules.EnsurePositiveQuantity(total);
            var distribution = ShipmentLoadDischargeRules.NormalizeDistribution(total, lines);

            var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == loadKey)
                ?? throw new NotFoundException("Carga não encontrada.");

            ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(load);

            var shares = await ShipmentLoadDischargeRules.ResolveLinesAsync(context, loadKey, distribution);

            var discharge = new ShipmentLoadDischarge
            {
                ShipmentLoadKey = loadKey,
                TicketNumber = ticket,
                DischargeDate = dischargeDate.Date,
                DischargedQuantity = total,
                Comments = comments,
                AttachmentKey = attachmentKey,
                CreatedAt = DateTime.Now,
                CreatedBy = userName,
            };

            foreach (var share in shares)
                discharge.Items.Add(share);

            await context.AddAsync(discharge);

            changeLog.Register(
                loadKey,
                ShipmentLoadChangeLogFields.Discharge,
                null,
                ShipmentLoadChangeLogFields.DescribeDischarge(ticket, total, Describe(discharge.Items)),
                userName);

            await recalculate.RecalculateAsync(loadKey, shares.Select(x => x.SalesInvoiceItemKey));

            // Depois das somas: a Descarregada lê o TicketDeliveredQuantity que acabou de mudar.
            await closureHook.ApplyAsync(loadKey, userName);

            await context.SaveChangesAsync();

            return discharge;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }

    internal static IEnumerable<(string? InvoiceNumber, decimal Quantity)> Describe(
        IEnumerable<ShipmentLoadDischargeItem> items) =>
        items.Select(x => (x.SalesInvoice?.InvoiceNumber, x.Quantity));
}
```

Substituir o corpo de `ShipmentLoadDischargesUpdateService` por (o XML-doc da classe vira "Altera um
ticket de descarga já registrado (GAC-1171), inclusive o RATEIO: as parcelas enviadas substituem as
gravadas."):

```csharp
public class ShipmentLoadDischargesUpdateService(
    AppDbContext context,
    ShipmentLoadDischargesRecalculateService recalculate,
    ShipmentLoadsClosureHookService closureHook,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadDischargesUpdateService> logger)
{
    public async Task ExecuteAsync(
        Guid dischargeKey,
        string? ticketNumber,
        DateTime dischargeDate,
        decimal quantity,
        IReadOnlyList<ShipmentLoadDischargeLine> lines,
        string? comments,
        string userName)
    {
        try
        {
            var ticket = ShipmentLoadDischargeRules.NormalizeTicketNumber(ticketNumber);
            var total = ShipmentLoadDischargeRules.RoundQuantity(quantity);
            ShipmentLoadDischargeRules.EnsurePositiveQuantity(total);
            var distribution = ShipmentLoadDischargeRules.NormalizeDistribution(total, lines);

            var discharge = await context.ShipmentLoadsDischarges
                .Include(x => x.Items)
                .ThenInclude(x => x.SalesInvoice)
                .FirstOrDefaultAsync(x => x.Key == dischargeKey)
                ?? throw new NotFoundException("Registro de descarga não encontrado.");

            var load = await context.ShipmentLoads
                .FirstOrDefaultAsync(x => x.Key == discharge.ShipmentLoadKey)
                ?? throw new NotFoundException("Carga não encontrada.");

            ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(load);

            var shares = await ShipmentLoadDischargeRules.ResolveLinesAsync(context, load.Key, distribution);

            var before = ShipmentLoadChangeLogFields.DescribeDischarge(
                discharge.TicketNumber,
                discharge.DischargedQuantity,
                ShipmentLoadDischargesCreateService.Describe(discharge.Items).ToList());

            // Linhas que saem também precisam ser recalculadas: senão o peso fica pendurado nelas.
            var touchedItemKeys = discharge.Items.Select(x => x.SalesInvoiceItemKey)
                .Concat(shares.Select(x => x.SalesInvoiceItemKey))
                .ToList();

            // Atualiza no lugar a linha que continua, em vez de apagar e reinserir: o índice único
            // (ticket, linha) não pode ver a mesma linha duas vezes no mesmo SaveChanges.
            var incoming = shares.ToDictionary(x => x.SalesInvoiceItemKey);

            foreach (var existing in discharge.Items.ToList())
            {
                if (incoming.Remove(existing.SalesInvoiceItemKey, out var replacement))
                {
                    existing.Quantity = replacement.Quantity;
                    continue;
                }

                discharge.Items.Remove(existing);
                context.ShipmentLoadsDischargesItems.Remove(existing);
            }

            foreach (var added in incoming.Values)
                discharge.Items.Add(added);

            discharge.TicketNumber = ticket;
            discharge.DischargeDate = dischargeDate.Date;
            discharge.DischargedQuantity = total;
            discharge.Comments = comments;
            discharge.UpdatedAt = DateTime.Now;
            discharge.UpdatedBy = userName;

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.Discharge,
                before,
                ShipmentLoadChangeLogFields.DescribeDischarge(
                    ticket, total, ShipmentLoadDischargesCreateService.Describe(discharge.Items)),
                userName);

            await recalculate.RecalculateAsync(load.Key, touchedItemKeys);

            await closureHook.ApplyAsync(load.Key, userName);

            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
```

Substituir o corpo de `ShipmentLoadDischargesDeleteService` por:

```csharp
public class ShipmentLoadDischargesDeleteService(
    AppDbContext context,
    ShipmentLoadDischargesRecalculateService recalculate,
    ShipmentLoadsClosureHookService closureHook,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadDischargesDeleteService> logger)
{
    public async Task ExecuteAsync(Guid dischargeKey, string userName)
    {
        try
        {
            var discharge = await context.ShipmentLoadsDischarges
                .Include(x => x.Items)
                .ThenInclude(x => x.SalesInvoice)
                .FirstOrDefaultAsync(x => x.Key == dischargeKey)
                ?? throw new NotFoundException("Registro de descarga não encontrado.");

            var load = await context.ShipmentLoads
                .FirstOrDefaultAsync(x => x.Key == discharge.ShipmentLoadKey)
                ?? throw new NotFoundException("Carga não encontrada.");

            ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(load);

            // Lidas ANTES do Remove: depois dele o recálculo não saberia quais linhas zerar.
            var itemKeys = discharge.Items.Select(x => x.SalesInvoiceItemKey).ToList();

            var before = ShipmentLoadChangeLogFields.DescribeDischarge(
                discharge.TicketNumber,
                discharge.DischargedQuantity,
                ShipmentLoadDischargesCreateService.Describe(discharge.Items).ToList());

            // Explícito, além do Cascade do banco: o EF InMemory dos testes não aplica FK.
            context.ShipmentLoadsDischargesItems.RemoveRange(discharge.Items);
            context.ShipmentLoadsDischarges.Remove(discharge);

            changeLog.Register(load.Key, ShipmentLoadChangeLogFields.Discharge, before, null, userName);

            await recalculate.RecalculateAsync(load.Key, itemKeys);

            await closureHook.ApplyAsync(load.Key, userName);

            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
```

(O XML-doc da classe de exclusão fica como está.)

- [ ] **Step 8: Travas e exclusão da carga**

`SalesInvoicesDeleteService.cs:23-32`: o comentário passa a citar `SHIPMENT_LOAD_DISCHARGE_ITEMS` e o
guard vira `if (await db.Context.ShipmentLoadsDischargesItems.AnyAsync(x => x.SalesInvoiceKey == entity.Key))`.
Trocar também "Registrar ticket em nota Pending é permitido, então o caminho é real." por "O ticket
nasce em nota Confirmada, e o estorno da confirmação a devolve a Pending: o caminho é real."

`SalesInvoicesItemsDeleteService.cs:39-47`: comentário citando `SHIPMENT_LOAD_DISCHARGE_ITEMS`; guard
`if (await db.Context.ShipmentLoadsDischargesItems.AnyAsync(x => x.SalesInvoiceItemKey == key))`.

`ShipmentLoadsDeleteService.cs`: a consulta de `discharges` ganha `.Include(x => x.Items)` antes do
`.Where`, e antes de `db.Context.ShipmentLoadsDischarges.RemoveRange(discharges);` entra
`db.Context.ShipmentLoadsDischargesItems.RemoveRange(discharges.SelectMany(x => x.Items));`.

- [ ] **Step 9: Leitura do rateio nos parâmetros da action**

Em `ShipmentLoadActionParameters.cs`, acrescentar `using System.Collections;` e
`using SiagroB1.Application.Services.ShipmentLoads;`, e ao fim da classe:

```csharp
    public const string MissingDistributionMessage =
        "Distribua o peso descarregado entre os documentos de saída.";

    /// <summary>
    /// Lê o rateio (GAC-1171, rateio): <c>SalesInvoiceItemKeys</c> (Collection(Edm.Guid)) e
    /// <c>Quantities</c> (Collection(Edm.Double)) como arrays PARALELOS, o precedente de
    /// ShipmentLoadsRefuse. Tamanhos diferentes são erro de montagem do payload, recusado aqui.
    /// </summary>
    /// <remarks>
    /// Um array de INTEIROS (<c>[20000, 15000]</c>) pode chegar como coleção de <c>int</c>/<c>long</c>.
    /// O cast direto para <c>double</c> devolveria lista vazia sem erro, e o ticket seria recusado por
    /// "rateio vazio" com o usuário vendo os números na tela.
    /// </remarks>
    public static bool TryReadDistribution(
        IDictionary<string, object> parameters,
        out List<ShipmentLoadDischargeLine> lines,
        out string? error)
    {
        lines = [];
        error = null;

        if (!parameters.TryGetValue("SalesInvoiceItemKeys", out var keysObj) || keysObj is not IEnumerable<Guid> keys ||
            !parameters.TryGetValue("Quantities", out var quantitiesObj) || quantitiesObj is not IEnumerable sequence)
        {
            error = MissingDistributionMessage;
            return false;
        }

        var quantities = new List<decimal>();

        foreach (var value in sequence)
        {
            if (!TryReadNumber(value, out var number))
            {
                error = $"Peso rateado inválido: {value}.";
                return false;
            }

            quantities.Add(number);
        }

        var keyList = keys.ToList();

        if (keyList.Count != quantities.Count)
        {
            error = "A lista de itens e a de pesos do rateio têm tamanhos diferentes.";
            return false;
        }

        lines = keyList
            .Select((key, index) => new ShipmentLoadDischargeLine(key, quantities[index]))
            .ToList();

        return true;
    }

    private static bool TryReadNumber(object? value, out decimal number)
    {
        switch (value)
        {
            case double d:
                number = (decimal)d;
                return true;
            case decimal m:
                number = m;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case float f:
                number = (decimal)f;
                return true;
            default:
                return decimal.TryParse(
                    value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
        }
    }
```

- [ ] **Step 10: Controllers e EDM**

`ODataConfigurations.cs` (bloco das linhas 693-731): no `shipmentLoadsDischargeCreate`, trocar as linhas
`Parameter<Guid>("SalesInvoiceKey")` e `Parameter<Guid>("SalesInvoiceItemKey")` por:

```csharp
        // GAC-1171 (rateio): o rateio viaja em arrays PARALELOS (linha da nota + peso), mesmo
        // precedente PROVADO de ShipmentLoadsRefuse. A nota não viaja: o servidor a resolve pela linha.
        shipmentLoadsDischargeCreate.CollectionParameter<Guid>("SalesInvoiceItemKeys");
        shipmentLoadsDischargeCreate.CollectionParameter<double>("Quantities");
```

e no `shipmentLoadsDischargeUpdate`, trocar o comentário "Nota e item não entram: apontar o ticket
para outra linha é excluir e registrar de novo, senão a soma da linha antiga fica órfã." por
"O rateio inteiro viaja de novo e substitui o gravado (GAC-1171, rateio)." e acrescentar, depois de
`Parameter<Guid>("Key")`:

```csharp
        shipmentLoadsDischargeUpdate.CollectionParameter<Guid>("SalesInvoiceItemKeys");
        shipmentLoadsDischargeUpdate.CollectionParameter<double>("Quantities");
```

`ShipmentLoadsDischargeCreateController.Post` — substituir a primeira guarda e o fim do `try`:

```csharp
            // ⚠️ parameters chega NULO quando nenhum parâmetro do EDM é enviado, e o TryGetValue
            // de um parâmetro anulável devolve true com valor nulo — daí as duas checagens.
            if (parameters is null ||
                !parameters.TryGetValue("LoadKey", out var loadKeyObj) || loadKeyObj is null)
                return BadRequest("Carga não informada.");

            if (!ShipmentLoadActionParameters.TryReadDistribution(parameters, out var lines, out var distributionError))
                return BadRequest(distributionError);
```

Logo depois do cálculo de `dischargeDate` e ANTES do bloco do anexo:

```csharp
            var quantity = Convert.ToDecimal(quantityObj ?? 0d, CultureInfo.InvariantCulture);

            // Rateio conferido ANTES do anexo: rateio que não fecha é o erro mais provável deste
            // diálogo, e cada tentativa recusada deixaria um anexo órfão na aba.
            ShipmentLoadDischargeRules.NormalizeDistribution(
                ShipmentLoadDischargeRules.RoundQuantity(quantity), lines);
```

e a chamada do serviço vira:

```csharp
            await service.ExecuteAsync(
                loadKey,
                ticketObj as string,
                dischargeDate,
                quantity,
                lines,
                commentsObj as string,
                attachmentKey,
                userName);
```

Acrescentar, nos TRÊS controllers de descarga (Create, Update, Delete), `using Microsoft.EntityFrameworkCore;`
e um `catch` antes do `catch (Exception e)`:

```csharp
        catch (DbUpdateConcurrencyException)
        {
            // A carga tem RowVersion e o ticket agora escreve nela (soma e situação): duas gravações
            // simultâneas na mesma carga derrubam a segunda aqui, e não com a mensagem crua do EF.
            return BadRequest(
                "A carga foi alterada por outro usuário enquanto a descarga era gravada. " +
                "Reabra a tela e tente novamente.");
        }
```

`ShipmentLoadsDischargeUpdateController.Post`: depois da guarda do `Key`, ler o rateio com
`TryReadDistribution` (mesma forma do Create) e passar `lines` na chamada:

```csharp
            await service.ExecuteAsync(
                (Guid) keyObj,
                ticketObj as string,
                parsedDate.Value,
                Convert.ToDecimal(quantityObj ?? 0d, CultureInfo.InvariantCulture),
                lines,
                commentsObj as string,
                User.Identity?.Name ?? "Unknown");
```

O `<summary>` do Update controller vira "Altera um ticket de descarga já registrado (GAC-1171),
inclusive o rateio."

`ShipmentLoadsDischargesController.cs:23-26` — o comentário vira:

```csharp
    // Sem MaxExpansionDepth: a aba expande Items/SalesInvoice (GAC-1171, rateio), profundidade 2,
    // que cabe no limite padrão — medido contra o servidor: depth 2 responde 200 e depth 3 devolve
    // 400 "$expand path which is too deep". Só o GetTransactions de ShipmentLoadsController precisa de 3.
```

- [ ] **Step 11: Rodar e ver passar**

Run: `dotnet build SiagroB1.sln -nodeReuse:false -v q` (0 erros) e
`dotnet test SiagroB1.Application.Tests --no-build`
Expected: todos verdes. Se algum teste fora desta lista quebrar por assinatura
(`ShipmentLoadDischargesCreateService` construído à mão), corrija para a assinatura nova — o grep
`new ShipmentLoadDischargesCreateService(` nos testes acha todos.

- [ ] **Step 12: Commit**

```text
feat(shipment): ratear o ticket de descarga entre os documentos de saída

O ticket da balança do destino pesa o caminhão inteiro, e a carga pode ter
vários documentos de saída: o registro apontava UMA linha de nota, e o usuário
lançava o mesmo ticket várias vezes, rateando de cabeça. O ticket vira
cabeçalho (peso do papel) com uma tabela filha de rateio; a nota é resolvida
no servidor pela linha, que precisa ser de nota Normal, Confirmada e com
faturado que não voltou.

As três escritas recalculam a situação da carga pelo gancho de fechamento, e
só a carga Cancelada congela o ticket: a carga mista Devolvida aceita o ticket
da parte entregue e continua Devolvida.

Atenção: o índice único (ticket, linha) é o motivo de a alteração atualizar a
linha que continua em vez de apagar e reinserir.

Refs: GAC-1171
```

---

### Task 5: Migration e aplicação numa cópia do `IDX_SIAGRO_DEV`

Repo: backend. **Por que uma cópia:** esta migration REMOVE colunas (`IsDischarged`, e
`SalesInvoiceKey`/`SalesInvoiceItemKey` do ticket) que os outros branches em andamento ainda leem no
`IDX_SIAGRO_DEV` compartilhado — aplicá-la nele derrubaria as telas de carga das outras sessões com
"Invalid column name". A verificação desta branch roda contra `IDX_SIAGRO_DEV_GAC1171`.

**Files:**
- Create (gerado + editado à mão): `SiagroB1.Migrations/AppContext/<timestamp>_AddShipmentLoadDischargeItemsAndReturnedQuantity.cs` e `.Designer.cs`
- Modify (gerado): `SiagroB1.Migrations/AppContext/AppDbContextModelSnapshot.cs`
- Create (fora do repo): `C:\Users\Penalva\AppData\Local\Temp\claude\C--Projetos-SiagroB1\ae971f0a-0143-4b96-8d11-27018b389a2e\scratchpad\gac1171-env.ps1`

**Interfaces:**
- Consumes: o modelo final das Tasks 1, 2 e 4.
- Produces: banco `IDX_SIAGRO_DEV_GAC1171` migrado; script de ambiente `gac1171-env.ps1` usado pela Task 9.

- [ ] **Step 1: Script de ambiente (não imprime credencial)**

Criar `C:\Users\Penalva\AppData\Local\Temp\claude\C--Projetos-SiagroB1\ae971f0a-0143-4b96-8d11-27018b389a2e\scratchpad\gac1171-env.ps1`:

```powershell
# Ambiente da verificação do GAC-1171 (rateio): Yokotobi-Development, mas no banco COPIADO.
$env:ASPNETCORE_ENVIRONMENT = "Yokotobi-Development"
$settings = Get-Content "C:\Projetos\SiagroB1\siagro-b1-backend-gac-1171\SiagroB1.Web\appsettings.Yokotobi-Development.json" -Raw | ConvertFrom-Json
$source = $settings.ConnectionStrings.SiagroDB
if ($source -notmatch '(?i)(Database|Initial Catalog)=IDX_SIAGRO_DEV\b') { throw "SiagroDB do Yokotobi-Development não aponta para IDX_SIAGRO_DEV: pare e confira." }
$env:ConnectionStrings__SiagroDB = $source -replace '(?i)((Database|Initial Catalog)=)IDX_SIAGRO_DEV\b', '${1}IDX_SIAGRO_DEV_GAC1171'
$global:Gac1171Master = $source -replace '(?i)((Database|Initial Catalog)=)IDX_SIAGRO_DEV\b', '${1}master'
```

Cada comando que depender dele começa com `. <caminho>\gac1171-env.ps1;` na MESMA chamada (o estado do
shell não persiste entre chamadas).

- [ ] **Step 2: Copiar o banco**

```powershell
. C:\Users\Penalva\AppData\Local\Temp\claude\C--Projetos-SiagroB1\ae971f0a-0143-4b96-8d11-27018b389a2e\scratchpad\gac1171-env.ps1
$conn = New-Object System.Data.SqlClient.SqlConnection $global:Gac1171Master; $conn.Open()
function Scalar($sql) { $c = $conn.CreateCommand(); $c.CommandText = $sql; $c.ExecuteScalar() }
function Exec($sql) { $c = $conn.CreateCommand(); $c.CommandTimeout = 1200; $c.CommandText = $sql; [void]$c.ExecuteNonQuery() }
if (Scalar "SELECT DB_ID('IDX_SIAGRO_DEV_GAC1171')" -ne [DBNull]::Value) { throw "IDX_SIAGRO_DEV_GAC1171 já existe: pergunte ao usuário antes de sobrescrever." }
$backupDir = Scalar "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))"
$dataDir = Scalar "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000))"
$logDir = Scalar "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000))"
$bak = Join-Path $backupDir 'IDX_SIAGRO_DEV_gac1171.bak'
Exec "BACKUP DATABASE [IDX_SIAGRO_DEV] TO DISK = N'$bak' WITH COPY_ONLY, INIT"
$c = $conn.CreateCommand(); $c.CommandText = "RESTORE FILELISTONLY FROM DISK = N'$bak'"; $r = $c.ExecuteReader()
$moves = @(); while ($r.Read()) { $logical = [string]$r['LogicalName']; $dir = if ([string]$r['Type'] -eq 'L') { $logDir } else { $dataDir }; $ext = if ([string]$r['Type'] -eq 'L') { 'ldf' } else { 'mdf' }; $moves += "MOVE N'$logical' TO N'$(Join-Path $dir "IDX_SIAGRO_DEV_GAC1171_$logical.$ext")'" }; $r.Close()
Exec "RESTORE DATABASE [IDX_SIAGRO_DEV_GAC1171] FROM DISK = N'$bak' WITH $($moves -join ', ')"
$conn.Close(); "copia criada"
```

Expected: `copia criada`. Se o login não tiver permissão de BACKUP/RESTORE, pare e peça ao usuário
(não aplique no `IDX_SIAGRO_DEV` como atalho).

- [ ] **Step 3: Contagens ANTES (na cópia)**

```powershell
. C:\Users\Penalva\AppData\Local\Temp\claude\C--Projetos-SiagroB1\ae971f0a-0143-4b96-8d11-27018b389a2e\scratchpad\gac1171-env.ps1
$conn = New-Object System.Data.SqlClient.SqlConnection $env:ConnectionStrings__SiagroDB; $conn.Open()
$c = $conn.CreateCommand(); $c.CommandText = @"
SELECT (SELECT COUNT(*) FROM SHIPMENT_LOAD_DISCHARGES) AS Tickets,
       (SELECT ISNULL(SUM(DischargedQuantity),0) FROM SHIPMENT_LOAD_DISCHARGES) AS TicketWeight,
       (SELECT COUNT(*) FROM SHIPMENT_LOAD_DISCHARGES WHERE SalesInvoiceItemKey IS NULL) AS TicketsWithoutLine,
       (SELECT COUNT(*) FROM SHIPMENT_LOADS WHERE Status = 8) AS DischargedLoads
"@; $r = $c.ExecuteReader(); $r.Read() | Out-Null; "Tickets=$($r[0]) Peso=$($r[1]) SemLinha=$($r[2]) Descarregadas=$($r[3])"; $r.Close(); $conn.Close()
```

Anote os quatro números para o Step 7.

- [ ] **Step 4: Gerar a migration**

```powershell
. C:\Users\Penalva\AppData\Local\Temp\claude\C--Projetos-SiagroB1\ae971f0a-0143-4b96-8d11-27018b389a2e\scratchpad\gac1171-env.ps1; Set-Location C:\Projetos\SiagroB1\siagro-b1-backend-gac-1171; dotnet build SiagroB1.sln -nodeReuse:false -v q; dotnet ef migrations add AddShipmentLoadDischargeItemsAndReturnedQuantity --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir AppContext --namespace SiagroB1.Migrations.AppContext --no-build
```

Confira que os dois arquivos novos caíram em `SiagroB1.Migrations/AppContext/` e `git add` os dois.
Leia o `Up()` gerado: ele deve conter só operações sobre `SHIPMENT_LOAD_DISCHARGE_ITEMS`,
`SHIPMENT_LOAD_DISCHARGES` (2 FKs, 2 índices, 2 colunas), `SHIPMENT_LOADS.IsDischarged` e as duas
`ReturnedQuantity`. Qualquer outra operação é drift do snapshot: pare e investigue antes de seguir.

- [ ] **Step 5: Reordenar à mão e acrescentar os dados**

O EF gera os `Drop` antes do `CreateTable`, o que apagaria o vínculo nota/linha dos tickets antes de
copiá-lo. Substituir `Up()` e `Down()` pelo abaixo, **conferindo cada nome de FK e de índice contra o
arquivo gerado** (se o gerado usar outro nome, fique com o do gerado):

```csharp
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Rateio do ticket (GAC-1171, rateio): tabela nova.
            migrationBuilder.CreateTable(
                name: "SHIPMENT_LOAD_DISCHARGE_ITEMS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DischargeKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceItemKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_LOAD_DISCHARGE_ITEMS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_DISCHARGE_ITEMS_SALES_INVOICES_ITEMS_SalesInvoiceItemKey",
                        column: x => x.SalesInvoiceItemKey,
                        principalTable: "SALES_INVOICES_ITEMS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_DISCHARGE_ITEMS_SALES_INVOICES_SalesInvoiceKey",
                        column: x => x.SalesInvoiceKey,
                        principalTable: "SALES_INVOICES",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_DISCHARGE_ITEMS_SHIPMENT_LOAD_DISCHARGES_DischargeKey",
                        column: x => x.DischargeKey,
                        principalTable: "SHIPMENT_LOAD_DISCHARGES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGE_ITEMS_DischargeKey_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGE_ITEMS",
                columns: new[] { "DischargeKey", "SalesInvoiceItemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGE_ITEMS_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGE_ITEMS",
                column: "SalesInvoiceItemKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGE_ITEMS_SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGE_ITEMS",
                column: "SalesInvoiceKey");

            // 2) Cada ticket existente vira cabeçalho + UMA linha, com o peso inteiro. Nenhum dado se
            //    perde. Ticket sem nota/linha não deveria existir (a action sempre exigiu as duas); se
            //    existir, fica sem rateio e aparece na aba com a coluna Documentos vazia.
            migrationBuilder.Sql(@"
INSERT INTO SHIPMENT_LOAD_DISCHARGE_ITEMS ([Key], DischargeKey, SalesInvoiceKey, SalesInvoiceItemKey, Quantity)
SELECT NEWID(), d.[Key], d.SalesInvoiceKey, d.SalesInvoiceItemKey, d.DischargedQuantity
FROM SHIPMENT_LOAD_DISCHARGES d
WHERE d.SalesInvoiceKey IS NOT NULL AND d.SalesInvoiceItemKey IS NOT NULL;");

            // 3) O vínculo nota/linha sai do ticket.
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_LOAD_DISCHARGES_SALES_INVOICES_ITEMS_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_LOAD_DISCHARGES_SALES_INVOICES_SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGES_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGES_SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES");

            // 4) A Descarregada deixa de ter marca manual: Descarregada (8) volta a Faturada (2).
            //    "Recalcular Saldo" (ou a próxima escrita de ticket) reaplica a regra nova. Sem backfill
            //    em C#, pela mesma decisão de 23/09.
            migrationBuilder.Sql("UPDATE SHIPMENT_LOADS SET Status = 2 WHERE Status = 8;");

            migrationBuilder.DropColumn(
                name: "IsDischarged",
                table: "SHIPMENT_LOADS");

            // 5) Quantidade devolvida (persistida-derivada) e o histórico dela: devoluções CONFIRMADAS
            //    (InvoiceType 1 = Return, InvoiceStatus 1 = Confirmed; enums gravados como int).
            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedQuantity",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedQuantity",
                table: "SALES_INVOICES",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(@"
UPDATE origin SET ReturnedQuantity = returned.Quantity
FROM SALES_INVOICES_ITEMS origin
CROSS APPLY (
    SELECT ISNULL(SUM(ri.Quantity), 0) AS Quantity
    FROM SALES_INVOICES_ITEMS ri
    INNER JOIN SALES_INVOICES r ON r.[Key] = ri.SalesInvoiceKey
    WHERE ri.SalesInvoiceItemOriginKey = origin.[Key]
      AND r.InvoiceType = 1
      AND r.InvoiceStatus = 1
) returned
WHERE returned.Quantity <> 0;

UPDATE inv SET ReturnedQuantity = items.Quantity
FROM SALES_INVOICES inv
CROSS APPLY (
    SELECT ISNULL(SUM(i.ReturnedQuantity), 0) AS Quantity
    FROM SALES_INVOICES_ITEMS i
    WHERE i.SalesInvoiceKey = inv.[Key]
) items
WHERE items.Quantity <> 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.AddColumn<bool>(
                name: "IsDischarged",
                table: "SHIPMENT_LOADS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                type: "uniqueidentifier",
                nullable: true);

            // ⚠️ COM PERDA: o ticket antigo aponta UMA linha. O ticket rateado em várias volta
            // apontando a de maior peso e mantém o peso total.
            migrationBuilder.Sql(@"
UPDATE d SET SalesInvoiceKey = firstLine.SalesInvoiceKey, SalesInvoiceItemKey = firstLine.SalesInvoiceItemKey
FROM SHIPMENT_LOAD_DISCHARGES d
CROSS APPLY (
    SELECT TOP 1 i.SalesInvoiceKey, i.SalesInvoiceItemKey
    FROM SHIPMENT_LOAD_DISCHARGE_ITEMS i
    WHERE i.DischargeKey = d.[Key]
    ORDER BY i.Quantity DESC
) firstLine;");

            migrationBuilder.DropTable(
                name: "SHIPMENT_LOAD_DISCHARGE_ITEMS");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGES_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                column: "SalesInvoiceItemKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGES_SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                column: "SalesInvoiceKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_LOAD_DISCHARGES_SALES_INVOICES_ITEMS_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                column: "SalesInvoiceItemKey",
                principalTable: "SALES_INVOICES_ITEMS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_LOAD_DISCHARGES_SALES_INVOICES_SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                column: "SalesInvoiceKey",
                principalTable: "SALES_INVOICES",
                principalColumn: "Key");
        }
```

Confira no `AppDbContextModelSnapshot.cs` que `SalesInvoice.InvoiceType` e `InvoiceStatus` são `int`
(`b.Property<int>("InvoiceType")`, `b.Property<int?>("InvoiceStatus")`). Se forem string, os `= 1` do
SQL estão errados: pare.

- [ ] **Step 6: Modelo em dia e aplicação na cópia**

```powershell
. C:\Users\Penalva\AppData\Local\Temp\claude\C--Projetos-SiagroB1\ae971f0a-0143-4b96-8d11-27018b389a2e\scratchpad\gac1171-env.ps1; Set-Location C:\Projetos\SiagroB1\siagro-b1-backend-gac-1171; dotnet build SiagroB1.sln -nodeReuse:false -v q; dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build; dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build
```

Expected: "No changes have been made to the model since the last migration." e a migration aplicada.
Antes do `update`, o log do `dotnet ef` precisa mostrar `IDX_SIAGRO_DEV_GAC1171` (se mostrar
`IDX_SIAGRO_DEV` sem sufixo, pare: o override não pegou).

- [ ] **Step 7: Conferir os dados migrados (na cópia)**

```powershell
. C:\Users\Penalva\AppData\Local\Temp\claude\C--Projetos-SiagroB1\ae971f0a-0143-4b96-8d11-27018b389a2e\scratchpad\gac1171-env.ps1
$conn = New-Object System.Data.SqlClient.SqlConnection $env:ConnectionStrings__SiagroDB; $conn.Open()
$c = $conn.CreateCommand(); $c.CommandText = @"
SELECT (SELECT COUNT(*) FROM SHIPMENT_LOAD_DISCHARGES) AS Tickets,
       (SELECT COUNT(*) FROM SHIPMENT_LOAD_DISCHARGE_ITEMS) AS Lines,
       (SELECT ISNULL(SUM(DischargedQuantity),0) FROM SHIPMENT_LOAD_DISCHARGES) AS TicketWeight,
       (SELECT ISNULL(SUM(Quantity),0) FROM SHIPMENT_LOAD_DISCHARGE_ITEMS) AS LineWeight,
       (SELECT COUNT(*) FROM SHIPMENT_LOADS WHERE Status = 8) AS DischargedLoads,
       (SELECT COUNT(*) FROM SALES_INVOICES WHERE ReturnedQuantity > 0) AS InvoicesWithReturn
"@; $r = $c.ExecuteReader(); $r.Read() | Out-Null
"Tickets=$($r[0]) Linhas=$($r[1]) PesoTicket=$($r[2]) PesoLinhas=$($r[3]) Descarregadas=$($r[4]) NotasComDevolucao=$($r[5])"; $r.Close(); $conn.Close()
```

Expected: `Tickets` igual ao do Step 3; `Linhas = Tickets − SemLinha`; `PesoLinhas = PesoTicket` (se
`SemLinha` foi 0); `Descarregadas = 0`; `NotasComDevolucao` ≥ 0 (anote para a Task 9).

- [ ] **Step 8: Suíte e commit**

Run: `dotnet test SiagroB1.Application.Tests --no-build` → todos verdes.

```text
feat(shipment): migrar tickets de descarga para cabeçalho e rateio

Cria SHIPMENT_LOAD_DISCHARGE_ITEMS e move para ela o vínculo nota/linha de
cada ticket existente (uma linha com o peso inteiro), antes de tirar as duas
colunas do ticket. A carga Descarregada pela marca manual volta a Faturada
e a coluna IsDischarged sai; ReturnedQuantity nasce com o histórico das
devoluções confirmadas.

Atenção: o EF gera os Drop antes do CreateTable — a ordem foi refeita à mão
para copiar o vínculo antes de apagá-lo. O Down() tem perda no ticket rateado
em mais de uma linha. Deploy: sem backfill de status; "Recalcular Saldo"
reaplica a Descarregada nas cargas que já têm ticket em todas as notas.

Refs: GAC-1171
DB: AddShipmentLoadDischargeItemsAndReturnedQuantity
```

---

### Task 6: Frontend — helpers de rateio e formatters

Repo: frontend.

**Files:**
- Create: `webapp/helpers/DischargeDistributionHelpers.ts`
- Create: `webapp/test/unit/helpers/DischargeDistributionHelpers.qunit.ts`
- Modify: `webapp/test/unit/unitTests.qunit.ts`
- Modify: `webapp/model/formatter.ts` (perto de `formatSalesInvoiceStatus`, ~linha 899)
- Modify: `webapp/test/unit/model/formatter.qunit.ts`

**Interfaces:**
- Produces (`siagrob1/helpers/DischargeDistributionHelpers`): `DISTRIBUTION_TOLERANCE`; tipos
  `LoadInvoiceItem`, `LoadInvoice`, `DistributionLine`, `DistributionSummary`, `DistributionInfo`;
  `round3(value: unknown): number`; `distributeProportionally(total: number, bases: number[]): number[]`;
  `summarizeDistribution(total: number, shares: number[]): DistributionSummary`;
  `describeDistribution(total: number, shares: number[]): DistributionInfo`;
  `buildDistributionLines(invoices: LoadInvoice[], ownShares?: Map<string, number>): DistributionLine[]`;
  `noEligibleLinesMessage(invoices: LoadInvoice[]): string`.
- Produces (`formatter`): `formatDischargeShare(invoiceNumber, quantity): string`,
  `isPartiallyReturned(status, returnedQuantity): boolean`, `formatPartialReturn(returnedQuantity): string`.

- [ ] **Step 1: Testes que falham**

Criar `webapp/test/unit/helpers/DischargeDistributionHelpers.qunit.ts` e `git add`:

```ts
import {
	buildDistributionLines,
	describeDistribution,
	distributeProportionally,
	LoadInvoice,
	noEligibleLinesMessage,
	round3,
	summarizeDistribution
} from "siagrob1/helpers/DischargeDistributionHelpers";

QUnit.module("DischargeDistributionHelpers - rateio do ticket de descarga (GAC-1171)");

QUnit.test("round3 lê Edm.Decimal em string e zera o que não é número", function (assert) {
	assert.strictEqual(round3("20000.000"), 20000);
	assert.strictEqual(round3(333.33349), 333.333);
	assert.strictEqual(round3(undefined), 0);
	assert.strictEqual(round3("abc"), 0);
});

QUnit.test("rateia proporcional à base e fecha com o total", function (assert) {
	assert.deepEqual(distributeProportionally(35000, [20000, 15000]), [20000, 15000]);
	assert.deepEqual(distributeProportionally(39500, [20000, 20000]), [19750, 19750]);
});

QUnit.test("o resíduo do arredondamento vai para a última linha com base", function (assert) {
	assert.deepEqual(distributeProportionally(1000, [1, 1, 1]), [333.333, 333.333, 333.334]);
	assert.deepEqual(distributeProportionally(39500, [20000, 0, 20000]), [19750, 0, 19750]);
});

QUnit.test("uma linha só recebe o total; sem total ou sem base, nada é rateado", function (assert) {
	assert.deepEqual(distributeProportionally(39500, [20000]), [39500]);
	assert.deepEqual(distributeProportionally(0, [20000, 15000]), [0, 0]);
	assert.deepEqual(distributeProportionally(1000, [0, 0]), [0, 0]);
});

QUnit.test("summarizeDistribution fecha dentro de 0,001 e informa o que falta ou sobra", function (assert) {
	assert.deepEqual(summarizeDistribution(35000, [20000, 15000]), { distributed: 35000, remaining: 0, closed: true });
	assert.deepEqual(summarizeDistribution(35000.0004, [20000, 15000]).closed, true);
	assert.deepEqual(summarizeDistribution(35000, [20000, 10000]), { distributed: 30000, remaining: 5000, closed: false });
	assert.deepEqual(summarizeDistribution(35000, [20000, 16000]).remaining, -1000);
});

QUnit.test("describeDistribution monta o texto da faixa em pt-BR", function (assert) {
	assert.strictEqual(describeDistribution(35000, [20000, 15000]).text, "Rateado 35.000,000 de 35.000,000.");
	assert.strictEqual(
		describeDistribution(35000, [20000, 10000]).text,
		"Rateado 30.000,000 de 35.000,000. Falta distribuir 5.000,000.");
	assert.strictEqual(
		describeDistribution(35000, [20000, 16000]).text,
		"Rateado 36.000,000 de 35.000,000. Excede em 1.000,000.");
});

const invoices: LoadInvoice[] = [
	{
		Key: "inv-1", InvoiceNumber: "000100", InvoiceType: "Normal", InvoiceStatus: "Confirmed",
		Items: [{
			Key: "item-1", ItemCode: "SOJA", ItemName: "SOJA EM GRAOS", Quantity: "20000.000",
			ReturnedQuantity: "5000.000", TicketDeliveredQuantity: "9000.000", SalesContract: { Code: "CV-1" }
		}]
	},
	{
		Key: "inv-2", InvoiceNumber: "", InvoiceType: "Normal", InvoiceStatus: "Confirmed",
		Items: [{ Key: "item-2", ItemCode: "SOJA", ItemName: "SOJA EM GRAOS", Quantity: "20000.000", ReturnedQuantity: "0.000" }]
	},
	{ Key: "inv-3", InvoiceNumber: "000102", InvoiceType: "Normal", InvoiceStatus: "Pending", Items: [{ Key: "item-3", Quantity: "1" }] },
	{ Key: "inv-4", InvoiceNumber: "000103", InvoiceType: "Normal", InvoiceStatus: "Cancelled", Items: [{ Key: "item-4", Quantity: "1" }] },
	{ Key: "inv-5", InvoiceNumber: "000104", InvoiceType: "Return", InvoiceStatus: "Confirmed", Items: [{ Key: "item-5", Quantity: "1" }] },
	{
		Key: "inv-6", InvoiceNumber: "000105", InvoiceType: "Normal", InvoiceStatus: "Confirmed",
		Items: [{ Key: "item-6", Quantity: "8000.000", ReturnedQuantity: "8000.000" }]
	}
];

QUnit.test("buildDistributionLines oferece só Normal + Confirmada com faturado que não voltou", function (assert) {
	const lines = buildDistributionLines(invoices);

	assert.deepEqual(lines.map(line => line.salesInvoiceItemKey), ["item-1", "item-2"]);
	assert.strictEqual(lines[0].invoiceNumber, "000100");
	assert.strictEqual(lines[1].invoiceNumber, "(sem número)");
	assert.strictEqual(lines[0].contractCode, "CV-1");
	assert.strictEqual(lines[0].itemText, "(SOJA) SOJA EM GRAOS");
	assert.strictEqual(lines[0].quantity, 20000);
	assert.strictEqual(lines[0].returnedQuantity, 5000);
	assert.strictEqual(lines[0].remainingQuantity, 15000);
	assert.strictEqual(lines[0].otherTickets, 9000);
	assert.strictEqual(lines[0].share, 0);
});

QUnit.test("na edição, o rateio gravado preenche o Peso Rateado e sai do Já descarregado", function (assert) {
	const lines = buildDistributionLines(invoices, new Map([["item-1", 9000]]));

	assert.strictEqual(lines[0].share, 9000);
	assert.strictEqual(lines[0].otherTickets, 0);
});

QUnit.test("noEligibleLinesMessage diz por que o diálogo não abre", function (assert) {
	assert.ok(noEligibleLinesMessage([]).startsWith("Esta carga ainda não tem documento de saída."));
	assert.strictEqual(
		noEligibleLinesMessage([{ Key: "a", InvoiceType: "Normal", InvoiceStatus: "Cancelled" }]),
		"Todos os documentos de saída desta carga estão cancelados.");
	assert.ok(noEligibleLinesMessage([{ Key: "a", InvoiceType: "Normal", InvoiceStatus: "Pending" }])
		.startsWith("Nenhum documento de saída desta carga está confirmado."));
	assert.ok(noEligibleLinesMessage([{ Key: "a", InvoiceType: "Normal", InvoiceStatus: "Confirmed" }])
		.startsWith("Os documentos de saída confirmados desta carga foram devolvidos por inteiro."));
});
```

Em `webapp/test/unit/unitTests.qunit.ts`, acrescentar `import "./helpers/DischargeDistributionHelpers.qunit";`
depois de `import "./helpers/PlateHelpers.qunit";`.

Em `webapp/test/unit/model/formatter.qunit.ts`, acrescentar ao fim:

```ts
QUnit.module("formatter - rateio do ticket e devolução parcial (GAC-1171)");

QUnit.test("formatDischargeShare mostra o documento e a parcela em pt-BR", function (assert) {
	assert.strictEqual(formatter.formatDischargeShare("000100", "20000.000"), "000100 (20.000,000)");
	assert.strictEqual(formatter.formatDischargeShare("", 15000), "(sem número) (15.000,000)");
});

QUnit.test("isPartiallyReturned só marca a nota Confirmada com devolução", function (assert) {
	assert.strictEqual(formatter.isPartiallyReturned("Confirmed", "5000.000"), true);
	assert.strictEqual(formatter.isPartiallyReturned("Confirmed", "0.000"), false);
	assert.strictEqual(formatter.isPartiallyReturned("Returned", "40000.000"), false);
	assert.strictEqual(formatter.isPartiallyReturned("Confirmed", null), false);
});

QUnit.test("formatPartialReturn mostra quanto voltou", function (assert) {
	assert.strictEqual(formatter.formatPartialReturn("5000.000"), "Devolução parcial: 5.000,000");
});
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `yarn ts-typecheck`
Expected: FAIL — o módulo `siagrob1/helpers/DischargeDistributionHelpers` e os formatters não existem.

- [ ] **Step 3: Implementar o helper**

Criar `webapp/helpers/DischargeDistributionHelpers.ts` e `git add`:

```ts
/**
 * Rateio do ticket de descarga entre as linhas dos documentos de saída (GAC-1171, rateio).
 *
 * O ticket pesa o caminhão inteiro, e a carga pode ter vários documentos de saída: o peso do papel
 * é rateado entre as linhas, proporcional ao que foi faturado e não voltou. Mesma regra do
 * `ShipmentLoadDischargeRules` do backend: 3 casas, tolerância 0,001, e a soma fecha com o ticket.
 */

export const DISTRIBUTION_TOLERANCE = 0.001;

/** Linha de documento de saída como o `$expand=Items` de `/ShipmentLoads(...)/Invoices` a entrega. */
export type LoadInvoiceItem = {
  Key: string;
  ItemCode?: string;
  ItemName?: string;
  Quantity?: number | string;
  ReturnedQuantity?: number | string;
  TicketDeliveredQuantity?: number | string;
  SalesContract?: { Code?: string } | null;
};

/** Documento de saída da carga. Enum do OData v4 chega como string ("Normal", "Confirmed"). */
export type LoadInvoice = {
  Key: string;
  InvoiceNumber?: string;
  InvoiceType?: string;
  InvoiceStatus?: string;
  Items?: LoadInvoiceItem[];
};

/** Linha do grid de rateio. `share` (Peso Rateado) é o único campo editável. */
export type DistributionLine = {
  salesInvoiceKey: string;
  salesInvoiceItemKey: string;
  invoiceNumber: string;
  contractCode: string;
  itemText: string;
  quantity: number;
  returnedQuantity: number;
  /** Faturado que não voltou: a base do rateio. */
  remainingQuantity: number;
  /** Peso de ticket que a linha já recebeu de OUTROS tickets. */
  otherTickets: number;
  share: number;
};

export type DistributionSummary = { distributed: number; remaining: number; closed: boolean };

/** O resumo e o texto da faixa "Rateado X de Y". */
export type DistributionInfo = DistributionSummary & { text: string };

/** Número em 3 casas. Edm.Decimal chega como STRING, e o que não é número vira 0. */
export function round3(value: unknown): number {
  const number = Number(value);
  return Number.isFinite(number) ? Math.round(number * 1000) / 1000 : 0;
}

const formatWeight = (value: number): string =>
  value.toLocaleString("pt-BR", { minimumFractionDigits: 3, maximumFractionDigits: 3 });

/**
 * Rateia `total` proporcionalmente a `bases`, em 3 casas. Cada parcela é TRUNCADA, e o resíduo vai
 * para a última linha com base positiva: nenhuma parcela fica negativa e a soma fecha exatamente com
 * o total. Sem total ou sem base, nada é rateado.
 */
export function distributeProportionally(total: number, bases: number[]): number[] {
  const amount = round3(total);
  const weights = bases.map(base => Math.max(0, round3(base)));
  const weightSum = weights.reduce((sum, weight) => sum + weight, 0);
  const shares = weights.map(() => 0);

  if (!(amount > 0) || !(weightSum > 0)) return shares;

  let last = -1;
  weights.forEach((weight, index) => {
    if (weight > 0) last = index;
  });

  let allocated = 0;

  weights.forEach((weight, index) => {
    if (weight <= 0 || index === last) return;

    // O epsilon protege do 332,9999999 do ponto flutuante, que o floor transformaria em 332.
    const share = Math.floor((amount * weight / weightSum) * 1000 + 1e-6) / 1000;
    shares[index] = share;
    allocated = round3(allocated + share);
  });

  shares[last] = round3(amount - allocated);

  return shares;
}

export function summarizeDistribution(total: number, shares: number[]): DistributionSummary {
  const distributed = round3(shares.reduce((sum, share) => sum + round3(share), 0));
  const remaining = round3(round3(total) - distributed);

  return { distributed, remaining, closed: Math.abs(remaining) <= DISTRIBUTION_TOLERANCE };
}

export function describeDistribution(total: number, shares: number[]): DistributionInfo {
  const summary = summarizeDistribution(total, shares);
  const head = `Rateado ${formatWeight(summary.distributed)} de ${formatWeight(round3(total))}.`;

  if (summary.closed) return { ...summary, text: head };

  const tail = summary.remaining > 0
    ? `Falta distribuir ${formatWeight(summary.remaining)}.`
    : `Excede em ${formatWeight(-summary.remaining)}.`;

  return { ...summary, text: `${head} ${tail}` };
}

/**
 * Linhas elegíveis ao ticket: documento Normal e Confirmado, com faturado que não voltou — a mesma
 * elegibilidade que o servidor confere (`ShipmentLoadDischargeRules.ResolveLinesAsync`).
 *
 * `ownShares` é o rateio gravado do ticket em edição (chave = linha da nota): preenche o Peso
 * Rateado e é descontado do "Já descarregado", que mostra só o que veio de OUTROS tickets.
 */
export function buildDistributionLines(
  invoices: LoadInvoice[],
  ownShares: Map<string, number> = new Map<string, number>()
): DistributionLine[] {
  const lines: DistributionLine[] = [];

  invoices
    .filter(invoice => invoice.InvoiceType === "Normal" && invoice.InvoiceStatus === "Confirmed")
    .forEach(invoice => {
      (invoice.Items ?? []).forEach(item => {
        const quantity = round3(item.Quantity);
        const returnedQuantity = round3(item.ReturnedQuantity);
        const remainingQuantity = round3(quantity - returnedQuantity);

        if (remainingQuantity <= DISTRIBUTION_TOLERANCE) return;

        const own = ownShares.get(item.Key) ?? 0;

        lines.push({
          salesInvoiceKey: invoice.Key,
          salesInvoiceItemKey: item.Key,
          invoiceNumber: invoice.InvoiceNumber || "(sem número)",
          contractCode: item.SalesContract?.Code ?? "",
          itemText: `(${item.ItemCode ?? ""}) ${item.ItemName ?? ""}`.trim(),
          quantity,
          returnedQuantity,
          remainingQuantity,
          otherTickets: Math.max(0, round3(round3(item.TicketDeliveredQuantity) - own)),
          share: own,
        });
      });
    });

  return lines;
}

/** Por que não há linha elegível: a mensagem do diálogo que não abre. */
export function noEligibleLinesMessage(invoices: LoadInvoice[]): string {
  const normal = invoices.filter(invoice => invoice.InvoiceType === "Normal");

  if (normal.length === 0) {
    return "Esta carga ainda não tem documento de saída. O ticket de descarga é rateado entre os "
      + "documentos da carga.";
  }

  if (normal.every(invoice => invoice.InvoiceStatus === "Cancelled")) {
    return "Todos os documentos de saída desta carga estão cancelados.";
  }

  if (!normal.some(invoice => invoice.InvoiceStatus === "Confirmed")) {
    return "Nenhum documento de saída desta carga está confirmado. Confirme o documento antes de "
      + "registrar a descarga.";
  }

  return "Os documentos de saída confirmados desta carga foram devolvidos por inteiro. Não há "
    + "quantidade entregue para registrar descarga.";
}
```

- [ ] **Step 4: Formatters**

Em `webapp/model/formatter.ts`, logo depois de `stateSalesInvoiceStatus`:

```ts
  /**
   * GAC-1171 (rateio): uma parcela do ticket na aba Descargas — "000100 (20.000,000)". Nota ainda
   * sem número aparece como "(sem número)". Edm.Decimal chega como string.
   */
  formatDischargeShare: (invoiceNumber: string, quantity: number | string): string =>
    `${invoiceNumber || "(sem número)"} (${Number(quantity ?? 0).toLocaleString("pt-BR", { minimumFractionDigits: 3, maximumFractionDigits: 3 })})`,

  /** GAC-1171 (rateio): nota Confirmada com parte dela devolvida — o selo "Dev. parcial". */
  isPartiallyReturned: (status: string, returnedQuantity: number | string): boolean =>
    status === "Confirmed" && Number(returnedQuantity ?? 0) > 0,

  /** Texto do selo no cabeçalho do documento: quanto voltou. */
  formatPartialReturn: (returnedQuantity: number | string): string =>
    `Devolução parcial: ${Number(returnedQuantity ?? 0).toLocaleString("pt-BR", { minimumFractionDigits: 3, maximumFractionDigits: 3 })}`,
```

- [ ] **Step 5: Rodar e ver passar**

Run: `yarn ts-typecheck` e `yarn lint` (0 erros). Depois o QUnit unitário, com o servidor em background
numa porta livre:

```powershell
Set-Location C:\Projetos\SiagroB1\siagro-b1-frontend-gac-1171; npx ui5 serve --port 8095
```
(em background) e, quando responder,
```powershell
Set-Location C:\Projetos\SiagroB1\siagro-b1-frontend-gac-1171; npx ui5-test-runner --url "http://localhost:8095/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests" --report-dir "C:\Users\Penalva\AppData\Local\Temp\claude\C--Projetos-SiagroB1\ae971f0a-0143-4b96-8d11-27018b389a2e\scratchpad\qunit"
```
Expected: todos os testes unitários verdes, incluindo os 12 novos. Derrube o `ui5 serve` depois
(por porta: `Get-NetTCPConnection -LocalPort 8095 -State Listen` → `Stop-Process -Force`).

- [ ] **Step 6: Commit**

```text
feat(shipment): calcular o rateio do ticket de descarga na tela

O diálogo de descarga vai sugerir o peso de cada documento de saída,
proporcional ao que foi faturado e não voltou. O cálculo fica num helper puro,
com a mesma regra do backend: 3 casas, resíduo na última linha com base, soma
fechando com o ticket dentro de 0,001. Formatters novos para a coluna
Documentos e para o selo "Dev. parcial".

Refs: GAC-1171
```

---

### Task 7: Frontend — diálogo de rateio, aba Descargas e cabeçalho da carga

Repo: frontend.

**Files:**
- Modify (rewrite): `webapp/view/shipmentLoads/fragments/ShipmentLoadDischargeDialog.fragment.xml`
- Modify (rewrite): `webapp/view/shipmentLoads/fragments/ShipmentLoadDischarges.fragment.xml`
- Modify: `webapp/controller/shipmentLoads/BaseController.ts:1-39,143-398`
- Modify: `webapp/view/shipmentLoads/Detail.view.xml:52-64`
- Modify: `webapp/controller/shipmentLoads/Detail.controller.ts:154-170`

**Interfaces:**
- Consumes: Task 6 (helpers e `formatter.formatDischargeShare`); actions da Task 4
  (`SalesInvoiceItemKeys`, `Quantities`); `SalesInvoiceItem.ReturnedQuantity` da Task 1.
- Produces: handlers `onDischargeQuantityChange`, `onRedistributeDischarge`, `onDischargeShareChange`
  (usados pelo fragmento); saem `onDischargeInvoiceChange`, `onMarkDischarged`, `onUndoDischarged`.

- [ ] **Step 1: Fragmento do diálogo**

Substituir `ShipmentLoadDischargeDialog.fragment.xml` inteiro por:

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form"
	xmlns:u="sap.ui.unified">
	<!-- Registro e edição do ticket de descarga (GAC-1171), com o RATEIO do peso entre as linhas dos
	     documentos de saída Normais e Confirmados da carga.

	     Os campos escrevem num BUFFER JSON (`viewModel>/dischargeDialog/...`), nunca no contexto
	     OData: two way binding num Detail deixaria PATCH pendente no update group diferido e
	     derrubaria o batch inteiro. Ao confirmar, tudo vai por action.

	     Digitar o Peso Descarregado rateia na hora, proporcional à Qtd. Líquida (faturado menos
	     devolvido). Editar uma parcela só atualiza a faixa "Rateado X de Y". A soma tem de fechar
	     com o peso do ticket: a tela recusa antes, e o servidor confere de novo. -->
	<Dialog
		id="shipmentLoadDischargeDialog"
		title="{viewModel>/dischargeDialog/title}"
		contentWidth="60rem">
		<content>
			<VBox class="sapUiSmallMargin">
				<f:Form editable="true">
					<f:layout>
						<f:ColumnLayout columnsM="2" columnsL="2" columnsXL="2"/>
					</f:layout>
					<f:formContainers>
						<f:FormContainer>
							<f:formElements>
								<f:FormElement label="Nº do Ticket">
									<f:fields>
										<Input
											value="{viewModel>/dischargeDialog/ticketNumber}"
											required="true"
											maxLength="50"/>
									</f:fields>
								</f:FormElement>
								<f:FormElement label="Data da Descarga">
									<f:fields>
										<!-- O buffer guarda a data no formato que a action exige
										     (yyyy-MM-dd); o usuário vê dd/MM/yyyy. -->
										<DatePicker
											value="{viewModel>/dischargeDialog/dischargeDate}"
											required="true"
											valueFormat="yyyy-MM-dd"
											displayFormat="dd/MM/yyyy"/>
									</f:fields>
								</f:FormElement>
								<f:FormElement label="Peso Descarregado">
									<f:fields>
										<!-- O change (e não o liveChange) rateia: é quando o valor já
										     foi lido em pt-BR e gravado no buffer. -->
										<Input
											value="{ path: 'viewModel>/dischargeDialog/quantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"
											required="true"
											textAlign="End"
											change=".onDischargeQuantityChange"/>
									</f:fields>
								</f:FormElement>
							</f:formElements>
						</f:FormContainer>
						<f:FormContainer>
							<f:formElements>
								<f:FormElement label="Observação">
									<f:fields>
										<TextArea
											value="{viewModel>/dischargeDialog/comments}"
											rows="2"
											width="100%"
											maxLength="500"/>
									</f:fields>
								</f:FormElement>
								<!-- O arquivo sobe junto com o ticket, num único POST. Na edição o campo
								     some: trocar o anexo é assunto da aba de anexos da carga. -->
								<f:FormElement label="Ticket digitalizado" visible="{= !${viewModel>/dischargeDialog/key} }">
									<f:fields>
										<u:FileUploader
											id="dischargeFileUploader"
											width="100%"
											placeholder="Selecione o arquivo do ticket"
											buttonText="Procurar"
											uploadOnChange="false"/>
									</f:fields>
								</f:FormElement>
							</f:formElements>
						</f:FormContainer>
					</f:formContainers>
				</f:Form>
				<MessageStrip
					class="sapUiSmallMarginTop"
					showIcon="true"
					visible="{= ${viewModel>/dischargeDialog/quantity} > 0 }"
					type="{= ${viewModel>/dischargeDialog/info/closed} ? 'Success' : 'Warning' }"
					text="{viewModel>/dischargeDialog/info/text}"/>
				<Table
					class="sapUiSmallMarginTop"
					items="{viewModel>/dischargeDialog/lines}"
					noDataText="Nenhum documento de saída confirmado nesta carga.">
					<headerToolbar>
						<OverflowToolbar>
							<Title text="Rateio entre os documentos de saída"/>
							<ToolbarSpacer/>
							<Button
								text="Ratear proporcionalmente"
								type="Transparent"
								icon="sap-icon://synchronize"
								press=".onRedistributeDischarge"/>
						</OverflowToolbar>
					</headerToolbar>
					<columns>
						<Column width="7rem"><Text text="Documento"/></Column>
						<Column width="7rem" minScreenWidth="Tablet" demandPopin="true"><Text text="Contrato"/></Column>
						<Column minScreenWidth="Tablet" demandPopin="true"><Text text="Produto"/></Column>
						<Column hAlign="End" minScreenWidth="Desktop" demandPopin="true"><Text text="Qtd. Faturada"/></Column>
						<Column hAlign="End" minScreenWidth="Desktop" demandPopin="true"><Text text="Qtd. Devolvida"/></Column>
						<Column hAlign="End"><Text text="Qtd. Líquida"/></Column>
						<Column hAlign="End" minScreenWidth="Desktop" demandPopin="true"><Text text="Já descarregado"/></Column>
						<Column hAlign="End" width="10rem"><Text text="Peso Rateado"/></Column>
					</columns>
					<items>
						<ColumnListItem>
							<cells>
								<Text text="{viewModel>invoiceNumber}" wrapping="false"/>
								<Text text="{viewModel>contractCode}" wrapping="false"/>
								<Text text="{viewModel>itemText}"/>
								<ObjectNumber number="{ path: 'viewModel>quantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
								<ObjectNumber number="{ path: 'viewModel>returnedQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
								<ObjectNumber number="{ path: 'viewModel>remainingQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
								<ObjectNumber number="{ path: 'viewModel>otherTickets', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
								<Input
									textAlign="End"
									change=".onDischargeShareChange"
									value="{ path: 'viewModel>share', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }, constraints: { minimum: 0 } }"/>
							</cells>
						</ColumnListItem>
					</items>
				</Table>
			</VBox>
		</content>
		<beginButton>
			<Button text="Gravar" type="Emphasized" press=".onConfirmDischarge"/>
		</beginButton>
		<endButton>
			<Button text="Cancelar" press=".onCloseDischargeDialog"/>
		</endButton>
	</Dialog>
</core:FragmentDefinition>
```

- [ ] **Step 2: Fragmento da aba Descargas**

Substituir `ShipmentLoadDischarges.fragment.xml` inteiro por (mantém o comentário de topo, reescrito):

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:t="sap.ui.table">
	<!-- Tickets de descarga da carga (GAC-1171): o que a balança do DESTINO pesou, segundo o
	     documento que o transportador entregou, com o RATEIO entre os documentos de saída.

	     ⚠️ O peso do ticket NÃO é o peso conferido: ele soma em colunas próprias e nenhuma
	     operação desta aba mexe na conferência de entrega da nota.

	     Uma linha por TICKET. A tabela é somente leitura: tudo é gravado por action, que carimba o
	     autor, recalcula as somas e a situação da carga e grava o log na mesma transação.

	     `$$ownRequest` é obrigatório: sem ele a coleção vira $expand no GET da carga e o refresh()
	     depois de gravar não recarrega nada. O `$expand` de Items/SalesInvoice tem profundidade 2,
	     que cabe no MaxExpansionDepth padrão.

	     O `$select` explícito também é obrigatório: com `autoExpandSelect` ele é montado a partir dos
	     BINDINGS DE CONTROLE, e `Items/SalesInvoiceItemKey` só é lido pelo controller ao abrir a
	     edição. Sem ele o rateio gravado chegaria sem a linha e o grid do diálogo abriria zerado.

	     O `sorter` também é obrigatório: o OrderByDescending do service só vale na consulta sem
	     paginação. -->
	<t:Table
		id="loadDischargesTable"
		class="sapUiSizeCondensed"
		alternateRowColors="true"
		enableBusyIndicator="true"
		enableSelectAll="false"
		selectionMode="Single"
		selectionBehavior="Row"
		busyIndicatorDelay="0"
		visibleRowCount="6"
		noData="Nenhuma descarga registrada nesta carga."
		rows="{
			path: 'Discharges',
			parameters: {
				'$$ownRequest': true,
				'$select': 'Key,TicketNumber,DischargeDate,DischargedQuantity,AttachmentKey,Comments,CreatedBy',
				'$expand': 'Items($select=Key,Quantity,SalesInvoiceItemKey;$expand=SalesInvoice($select=Key,InvoiceNumber))'
			},
			sorter: { path: 'DischargeDate', descending: true }
		}">
		<t:extension>
			<OverflowToolbar>
				<content>
					<Title text="Descargas"/>
					<ToolbarSpacer/>
					<!-- Mesmo guard do servidor (ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges):
					     só a carga cancelada está congelada. A Devolvida entra: a carga mista tem parte
					     entregue, e o ticket dela libera o frete. `targetType: 'any'` em toda expressão
					     sobre enum, senão o valor não chega como string. -->
					<Button
						type="Transparent"
						text="Registrar Descarga"
						icon="sap-icon://add"
						visible="{= ${path: 'Status', targetType: 'any'} !== 'Cancelled' }"
						press=".onAddDischarge"/>
					<Button
						type="Transparent"
						text="Editar"
						icon="sap-icon://edit"
						visible="{= ${path: 'Status', targetType: 'any'} !== 'Cancelled' }"
						press=".onEditDischarge"/>
					<Button
						type="Transparent"
						text="Excluir"
						icon="sap-icon://delete"
						visible="{= ${path: 'Status', targetType: 'any'} !== 'Cancelled' }"
						press=".onRemoveDischarge"/>
				</content>
			</OverflowToolbar>
		</t:extension>
		<t:columns>
			<t:Column label="Ticket" width="10rem">
				<t:template><Text text="{TicketNumber}" wrapping="false"/></t:template>
			</t:Column>
			<t:Column label="Data da Descarga" width="9rem">
				<t:template>
					<Text wrapping="false" text="{ path: 'DischargeDate', targetType: 'any', formatter: '.formatter.formatDate' }"/>
				</t:template>
			</t:Column>
			<t:Column label="Peso Descarregado" hAlign="End" width="10rem">
				<t:template>
					<ObjectNumber
						textAlign="End"
						number="{ path: 'DischargedQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
				</t:template>
			</t:Column>
			<t:Column label="Documentos" width="24rem">
				<t:template>
					<!-- Lista aninhada RELATIVA à linha: lê o `$expand=Items` que o grid já trouxe, sem
					     requisição própria, e acompanha a reciclagem de linhas da sap.ui.table (um
					     binding OneTime não acompanharia). -->
					<HBox items="{ path: 'Items', templateShareable: false }" wrap="NoWrap">
						<Text
							class="sapUiTinyMarginEnd"
							wrapping="false"
							text="{ parts: [ { path: 'SalesInvoice/InvoiceNumber' }, { path: 'Quantity', targetType: 'any' } ], formatter: '.formatter.formatDischargeShare' }"/>
					</HBox>
				</t:template>
			</t:Column>
			<t:Column label="Anexo" hAlign="Center" width="6rem">
				<t:template>
					<!-- `targetType: 'any'` no Edm.Guid anulável: sem ele a chave chega convertida e
					     o `!!` nem chega a executar. -->
					<Button
						type="Transparent"
						icon="sap-icon://attachment"
						tooltip="Visualizar o ticket anexado"
						visible="{= !!${path: 'AttachmentKey', targetType: 'any'} }"
						press=".onViewDischargeAttachment"/>
				</t:template>
			</t:Column>
			<t:Column label="Observação" width="20rem">
				<t:template><Text text="{Comments}" tooltip="{Comments}" wrapping="false"/></t:template>
			</t:Column>
			<t:Column label="Registrado por" width="12rem">
				<t:template><Text text="{CreatedBy}" wrapping="false"/></t:template>
			</t:Column>
		</t:columns>
	</t:Table>
</core:FragmentDefinition>
```

- [ ] **Step 3: Controller do diálogo**

Em `webapp/controller/shipmentLoads/BaseController.ts`:

(a) Acrescentar aos imports:

```ts
import {
  buildDistributionLines,
  describeDistribution,
  distributeProportionally,
  DistributionInfo,
  DistributionLine,
  LoadInvoice,
  noEligibleLinesMessage,
  round3,
  summarizeDistribution
} from "siagrob1/helpers/DischargeDistributionHelpers";
```

(b) Substituir as linhas 20-39 (`DischargeOption`, `AttachmentPayload`, `DischargeForm`) por:

```ts
/** Arquivo do anexo já em base64, no formato que as actions da carga esperam. */
type AttachmentPayload = { File: string; FileName: string; ContentType: string };

/**
 * Buffer JSON do diálogo de descarga (GAC-1171, rateio). `key` nulo significa inclusão; `lines` é o
 * grid de rateio e `info`, a faixa "Rateado X de Y".
 */
type DischargeForm = {
  title: string;
  key?: string;
  ticketNumber?: string;
  dischargeDate?: string;
  quantity?: number;
  comments?: string;
  lines: DistributionLine[];
  info: DistributionInfo;
};

/** Parcela gravada, como o `$expand=Items` do grid de tickets a entrega. */
type SavedDischargeItem = { SalesInvoiceItemKey: string; Quantity: number | string };
```

(c) Substituir o bloco de `loadInvoiceOptionsAsync` até o fim de `onConfirmDischarge` (linhas 143-398),
mantendo `openDischargeDialog` e `onCloseDischargeDialog` como estão, por:

```ts
  /**
   * Documentos de saída da carga com as linhas, lidos ANTES de abrir o diálogo (GAC-1171, rateio).
   * O grid é um JSONModel estático: o que o usuário digita não pode virar PATCH pendente no update
   * group diferido da página.
   */
  private async loadLoadInvoicesAsync(): Promise<LoadInvoice[]> {
    const model = this.getModel() as ODataModel;

    const binding = model.bindList(
      `/ShipmentLoads(${this.currentLoadKey()})/Invoices`,
      undefined,
      undefined,
      undefined,
      {
        $select: "Key,InvoiceNumber,InvoiceType,InvoiceStatus",
        $expand: "Items($select=Key,ItemCode,ItemName,Quantity,ReturnedQuantity,TicketDeliveredQuantity;"
          + "$expand=SalesContract($select=Key,Code))",
      }
    );

    // A coleção inteira, e não a janela visível: a lista alimenta o grid do diálogo.
    const contexts = await binding.requestContexts(0, Infinity);

    return contexts.map(context => context.getObject() as LoadInvoice);
  }

  async onAddDischarge(): Promise<void> {
    if (!this.getView().getBindingContext()) {
      MessageBox.alert("Carga não carregada.");
      return;
    }

    this.setBusy(true);

    try {
      const invoices = await this.loadLoadInvoicesAsync();
      const lines = buildDistributionLines(invoices);

      if (lines.length === 0) {
        MessageBox.alert(noEligibleLinesMessage(invoices));
        return;
      }

      this.viewModel().setProperty("/dischargeDialog", {
        title: "Registrar Descarga",
        key: null,
        ticketNumber: "",
        // A action exige yyyy-MM-dd, que é o `valueFormat` do DatePicker.
        dischargeDate: this.todayIso(),
        quantity: null,
        comments: "",
        lines,
        info: describeDistribution(0, lines.map(line => line.share)),
      } as DischargeForm);

      await this.openDischargeDialog();
    } catch (e) {
      MessageBox.error((e as Error).message || "Erro ao preparar o registro de descarga.");
    } finally {
      this.setBusy(false);
    }
  }

  async onEditDischarge(): Promise<void> {
    const context = this.selectedDischargeContext();

    if (!context) return;

    this.setBusy(true);

    try {
      // O rateio gravado vem do `$expand=Items` do grid de tickets, já em cache.
      const saved = ((await context.requestObject("Items")) ?? []) as SavedDischargeItem[];
      // A tupla anotada é obrigatória: sem ela o TS infere (string | number)[] e o Map não compila.
      const ownShares = new Map<string, number>(
        saved.map((item): [string, number] => [item.SalesInvoiceItemKey, round3(item.Quantity)]));

      const invoices = await this.loadLoadInvoicesAsync();
      const lines = buildDistributionLines(invoices, ownShares);

      if (lines.length === 0) {
        MessageBox.alert(`${noEligibleLinesMessage(invoices)} Exclua o ticket se ele não vale mais.`);
        return;
      }

      // Edm.Decimal chega como STRING: `round3` converte antes do tipo Float do campo.
      const quantity = round3(context.getProperty("DischargedQuantity"));

      this.viewModel().setProperty("/dischargeDialog", {
        title: "Editar Descarga",
        key: context.getProperty("Key") as string,
        ticketNumber: context.getProperty("TicketNumber") as string,
        // A data chega com hora (datetime2); a action só aceita o dia.
        dischargeDate: ((context.getProperty("DischargeDate") as string) ?? "").slice(0, 10),
        quantity,
        comments: (context.getProperty("Comments") as string) ?? "",
        lines,
        info: describeDistribution(quantity, lines.map(line => line.share)),
      } as DischargeForm);

      await this.openDischargeDialog();
    } catch (e) {
      MessageBox.error((e as Error).message || "Erro ao abrir a descarga.");
    } finally {
      this.setBusy(false);
    }
  }

  /** Peso total digitado: rateia de novo, proporcional à Qtd. Líquida (sobrescreve o grid). */
  onDischargeQuantityChange(): void {
    this.redistributeDischarge();
  }

  /** Botão "Ratear proporcionalmente": refaz a sugestão depois de um ajuste à mão. */
  onRedistributeDischarge(): void {
    this.redistributeDischarge();
  }

  /** Uma parcela editada: só a faixa de fechamento muda. */
  onDischargeShareChange(): void {
    this.updateDischargeInfo();
  }

  private redistributeDischarge(): void {
    const viewModel = this.viewModel();
    const total = round3(viewModel.getProperty("/dischargeDialog/quantity"));
    const lines = (viewModel.getProperty("/dischargeDialog/lines") as DistributionLine[]) ?? [];
    const shares = distributeProportionally(total, lines.map(line => line.remainingQuantity));

    // Por caminho, e não reescrevendo o array: o JSONModel avisa só as células que mudaram.
    shares.forEach((share, index) =>
      viewModel.setProperty(`/dischargeDialog/lines/${index}/share`, share));

    this.updateDischargeInfo();
  }

  private updateDischargeInfo(): void {
    const viewModel = this.viewModel();
    const total = round3(viewModel.getProperty("/dischargeDialog/quantity"));
    const lines = (viewModel.getProperty("/dischargeDialog/lines") as DistributionLine[]) ?? [];

    viewModel.setProperty("/dischargeDialog/info", describeDistribution(total, lines.map(line => line.share)));
  }
```

Depois de `onCloseDischargeDialog`, substituir `onConfirmDischarge` por:

```ts
  async onConfirmDischarge(): Promise<void> {
    if (this._dischargeInFlight) return;

    const form = this.viewModel().getProperty("/dischargeDialog") as DischargeForm;

    const ticketNumber = (form.ticketNumber ?? "").trim();
    const dischargeDate = (form.dischargeDate ?? "").trim();
    const quantity = round3(form.quantity);

    if (ticketNumber === "") {
      MessageBox.alert("Informe o número do ticket.");
      return;
    }

    if (dischargeDate === "") {
      MessageBox.alert("Informe a data da descarga.");
      return;
    }

    if (!(quantity > 0)) {
      MessageBox.alert("Informe o peso descarregado.");
      return;
    }

    // Mesma normalização do servidor: 3 casas e sem as parcelas zero.
    const shares = (form.lines ?? [])
      .map(line => ({ key: line.salesInvoiceItemKey, share: round3(line.share) }))
      .filter(line => line.share > 0);

    if (shares.length === 0) {
      MessageBox.alert("Distribua o peso descarregado entre os documentos de saída.");
      return;
    }

    if (!summarizeDistribution(quantity, shares.map(line => line.share)).closed) {
      MessageBox.alert(
        `${describeDistribution(quantity, shares.map(line => line.share)).text} Ajuste o rateio antes de gravar.`);
      return;
    }

    this._dischargeInFlight = true;
    this._dischargeDialog?.setBusy(true);
    this.setBusy(true);

    try {
      const model = this.getModel() as ODataModel;

      if (form.key) {
        const action = model.bindContext("/ShipmentLoadsDischargeUpdate(...)");
        action.setParameter("Key", form.key);
        action.setParameter("TicketNumber", ticketNumber);
        action.setParameter("DischargeDate", dischargeDate);
        action.setParameter("Quantity", quantity);
        action.setParameter("Comments", form.comments ?? "");
        action.setParameter("SalesInvoiceItemKeys", shares.map(line => line.key));
        action.setParameter("Quantities", shares.map(line => line.share));
        await action.invoke();
        MessageToast.show("Descarga alterada.");
      } else {
        const file = await this.loadAttachmentBase64(this.dischargeFileUploader());

        const action = model.bindContext("/ShipmentLoadsDischargeCreate(...)");
        action.setParameter("LoadKey", this.currentLoadKey());
        action.setParameter("TicketNumber", ticketNumber);
        action.setParameter("DischargeDate", dischargeDate);
        action.setParameter("Quantity", quantity);
        action.setParameter("Comments", form.comments ?? "");
        action.setParameter("SalesInvoiceItemKeys", shares.map(line => line.key));
        action.setParameter("Quantities", shares.map(line => line.share));

        if (file) {
          action.setParameter("File", file.File);
          action.setParameter("FileName", file.FileName);
          action.setParameter("ContentType", file.ContentType);
        }

        await action.invoke();
        MessageToast.show("Descarga registrada.");
      }

      // O diálogo só fecha DEPOIS do resolve: fechar antes descartaria o que foi digitado se a
      // action recusasse o ticket.
      this.onCloseDischargeDialog();
      this.refreshDischarges();
    } catch (e) {
      MessageBox.error((e as Error).message || "Erro ao gravar a descarga.");
    } finally {
      this._dischargeInFlight = false;
      this._dischargeDialog?.setBusy(false);
      this.setBusy(false);
    }
  }
```

`onRemoveDischarge`, `onViewDischargeAttachment`, `selectedDischargeContext` e `refreshDischarges`
ficam como estão. `refreshDischarges` já faz `context.refresh()` da carga — é o que traz o status novo
(Descarregada) para o cabeçalho.

- [ ] **Step 4: Tirar os botões manuais**

`webapp/view/shipmentLoads/Detail.view.xml`: apagar o comentário "GAC-1171 (melhorias): marca MANUAL de
descarga ..." e os dois `<Button>` "Marcar como Descarregada" e "Desfazer Descarregada" (linhas 52-64).

`webapp/controller/shipmentLoads/Detail.controller.ts`: apagar `onMarkDischarged` e `onUndoDischarged`
com os XML-docs (linhas 154-170). O `<summary>` de `invokeLoadAction` ("o formato de Concluir e
Reabrir") continua certo.

- [ ] **Step 5: Gates**

Run: `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint` e o QUnit unitário (como na Task 6, Step 5).
Expected: 0 erros; QUnit todo verde. Um grep por `onDischargeInvoiceChange`, `DischargeOption`,
`MarkDischarged` e `UndoDischarged` em `webapp/` não pode achar nada.

- [ ] **Step 6: Commit**

```text
feat(shipment): registrar o ticket de descarga com rateio entre os documentos

O diálogo de descarga troca o Select de nota por um grid com as linhas dos
documentos de saída Normais e Confirmados da carga. Digitar o peso do ticket
já rateia proporcional ao que foi faturado e não voltou; o usuário só confere,
e o Gravar exige que a soma feche. A aba Descargas passa a ter uma linha por
ticket, com os documentos e as parcelas.

Saem "Marcar como Descarregada" e "Desfazer Descarregada": a situação vem dos
tickets. A carga Devolvida (mista) volta a aceitar ticket.

Refs: GAC-1171
```

---

### Task 8: Frontend — selo "Dev. parcial"

Repo: frontend.

**Files:**
- Modify: `webapp/view/salesInvoices/Main.view.xml:133-153`
- Modify: `webapp/view/salesInvoices/Detail.view.xml:39-53`
- Modify: `webapp/view/salesInvoices/fragments/Items.fragment.xml` (depois da coluna "Quantidade", ~linha 101)
- Modify: `webapp/view/shipmentLoads/Detail.view.xml:358-365,384-390`

**Interfaces:**
- Consumes: `formatter.isPartiallyReturned`, `formatter.formatPartialReturn` (Task 6);
  `ReturnedQuantity` no cabeçalho e na linha (Task 1).

- [ ] **Step 1: Lista de documentos de saída**

Em `salesInvoices/Main.view.xml`, a coluna Status passa a `width="15rem"` e o template vira:

```xml
						<t:template>
							<!-- GAC-1171 (rateio): a nota devolvida EM PARTE continua Confirmada — o selo
							     avisa. Status novo foi recusado: Confirmed é testado em ~20 regras. -->
							<HBox justifyContent="Center" alignItems="Center">
								<ObjectStatus
									inverted="true"
									state="{ path: 'InvoiceStatus', targetType: 'any', formatter: '.formatter.stateSalesInvoiceStatus' }"
									text="{ path: 'InvoiceStatus', targetType: 'any', formatter: '.formatter.formatSalesInvoiceStatus' }"/>
								<ObjectStatus
									class="sapUiTinyMarginBegin"
									state="Warning"
									text="Dev. parcial"
									visible="{ parts: [ { path: 'InvoiceStatus', targetType: 'any' }, { path: 'ReturnedQuantity', targetType: 'any' } ], formatter: '.formatter.isPartiallyReturned' }"/>
							</HBox>
						</t:template>
```

- [ ] **Step 2: Detalhe do documento**

Em `salesInvoices/Detail.view.xml`, dentro do `<HBox>` do `headerContent`, depois do `ObjectStatus` do status:

```xml
        <ObjectStatus
          class="sapUiSmallMarginBegin"
          state="Warning"
          inverted="true"
          text="{ path: 'ReturnedQuantity', targetType: 'any', formatter: '.formatter.formatPartialReturn' }"
          visible="{ parts: [ { path: 'InvoiceStatus', targetType: 'any' }, { path: 'ReturnedQuantity', targetType: 'any' } ], formatter: '.formatter.isPartiallyReturned' }"/>
```

Em `salesInvoices/fragments/Items.fragment.xml`, depois da coluna "Quantidade":

```xml
      <!-- GAC-1171 (rateio): quanto da linha já voltou em devolução confirmada. Só no Detalhe: na
           inclusão e na edição o contexto é JSON e a coluna não existe. -->
      <t:Column label="Qtd. Devolvida" hAlign="End" visible="{= !${ui>/editable} }">
        <t:template>
          <ObjectNumber
            textAlign="End"
            number="{ path: 'ReturnedQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
        </t:template>
      </t:Column>
```

- [ ] **Step 3: Aba Documentos de Saída da carga**

Em `shipmentLoads/Detail.view.xml`, a coluna Status do `loadInvoicesTable` passa a `width="14rem"` com o
template:

```xml
										<t:template>
											<!-- GAC-1171 (rateio): nota Confirmada com parte devolvida pela Recusa. -->
											<HBox justifyContent="Center" alignItems="Center">
												<ObjectStatus
													inverted="true"
													state="{ path: 'InvoiceStatus', targetType: 'any', formatter: '.formatter.stateSalesInvoiceStatus' }"
													text="{ path: 'InvoiceStatus', targetType: 'any', formatter: '.formatter.formatSalesInvoiceStatus' }"/>
												<ObjectStatus
													class="sapUiTinyMarginBegin"
													state="Warning"
													text="Dev. parcial"
													visible="{ parts: [ { path: 'InvoiceStatus', targetType: 'any' }, { path: 'ReturnedQuantity', targetType: 'any' } ], formatter: '.formatter.isPartiallyReturned' }"/>
											</HBox>
										</t:template>
```

e, depois da coluna "Qtde":

```xml
										<t:Column label="Qtd. Devolvida" hAlign="End" width="9rem">
											<t:template>
												<ObjectNumber
													textAlign="End"
													number="{ path: 'ReturnedQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
											</t:template>
										</t:Column>
```

- [ ] **Step 4: Gates**

Run: `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint`. Expected: 0 erros. Confira que nenhum comentário
XML novo tem dois hífens seguidos.

- [ ] **Step 5: Commit**

```text
feat(invoice): sinalizar documento de saída com devolução parcial

A recusa parcial de carga deixava o documento "Confirmada" sem nada que
mostrasse o que voltou. A lista, o detalhe e a aba de documentos da carga
ganham o selo "Dev. parcial" e a coluna Qtd. Devolvida, lidos do
ReturnedQuantity. O status continua Confirmada de propósito: um status novo
teria de ser aceito por ~20 regras que testam Confirmada.

Refs: GAC-1171
```

---

### Task 9: Verificação pelo caminho do usuário (navegador)

Repos: os dois. Sem commit de código, salvo correção de defeito achado aqui (volta para a task dona).

- [ ] **Step 1: Subir a stack contra a cópia**

Gateway primeiro, e só depois do "Loading proxy data from config" o Web (build em paralelo dá CS2012):

```powershell
. C:\Users\Penalva\AppData\Local\Temp\claude\C--Projetos-SiagroB1\ae971f0a-0143-4b96-8d11-27018b389a2e\scratchpad\gac1171-env.ps1; Set-Location C:\Projetos\SiagroB1\siagro-b1-backend-gac-1171; dotnet run --project SiagroB1.Gateway --launch-profile yktb
```
```powershell
. C:\Users\Penalva\AppData\Local\Temp\claude\C--Projetos-SiagroB1\ae971f0a-0143-4b96-8d11-27018b389a2e\scratchpad\gac1171-env.ps1; Set-Location C:\Projetos\SiagroB1\siagro-b1-backend-gac-1171; dotnet run --project SiagroB1.Web --launch-profile yktb
```
```powershell
Set-Location C:\Projetos\SiagroB1\siagro-b1-frontend-gac-1171; yarn start:dev
```

(os três em background). Login `admin` / `1234`. Navegue pelo MENU, não por `goto` de URL com hash.

- [ ] **Step 2: Achar as cargas de teste (na cópia)**

```sql
-- Carga Faturada com 2+ notas Normais confirmadas
SELECT l.Code, l.Status, COUNT(*) AS Normais
FROM SHIPMENT_LOADS l JOIN SALES_INVOICES i ON i.ShipmentLoadKey = l.[Key]
WHERE i.InvoiceType = 0 AND i.InvoiceStatus = 1
GROUP BY l.Code, l.Status HAVING COUNT(*) >= 2;

-- Carga mista: Devolvida (5) com nota Normal confirmada que não voltou inteira
SELECT DISTINCT l.Code FROM SHIPMENT_LOADS l
JOIN SALES_INVOICES i ON i.ShipmentLoadKey = l.[Key]
JOIN SALES_INVOICES_ITEMS it ON it.SalesInvoiceKey = i.[Key]
WHERE l.Status = 5 AND i.InvoiceType = 0 AND i.InvoiceStatus = 1 AND it.Quantity - it.ReturnedQuantity > 0.001;

-- Nota com devolução parcial
SELECT TOP 5 InvoiceNumber FROM SALES_INVOICES WHERE InvoiceStatus = 1 AND ReturnedQuantity > 0;
```

Se não houver carga com 2 notas, fature uma carga em duas notas pelo Faturamento de Expedição (na
cópia). Se o SAP (192.168.1.144) estiver fora e bloquear o faturamento, PARE e pergunte ao usuário
antes de fabricar dado por SQL.

- [ ] **Step 3: Cenários**

1. **Rateio sugerido:** na carga de 2 notas, Registrar Descarga → o grid mostra as duas linhas com
   Qtd. Líquida; digitar o peso rateia na hora; a faixa fica verde.
2. **Ajuste e trava:** mudar uma parcela → a faixa fica amarela com "Falta distribuir"/"Excede em"; o
   Gravar recusa. "Ratear proporcionalmente" volta ao verde.
3. **Descarregada automática:** gravar com as duas linhas → a carga vira **Descarregada** no cabeçalho,
   o log mostra "Situação: Faturada → Descarregada" com o usuário, a aba mostra UMA linha de ticket com
   a coluna Documentos listando as duas notas. Os botões Marcar/Desfazer não existem mais.
4. **Edição:** Editar o ticket → o grid abre com o rateio gravado e "Já descarregado" = 0.
5. **Volta:** Excluir o ticket → a carga volta a Faturada; a coluna Documentos da aba segue certa depois
   de gravar/excluir e de rolar (prova da lista aninhada; se falhar, aplique o plano B do spec §5.3 na
   Task 7 e reverifique).
6. **Carga mista:** na carga Devolvida, Registrar Descarga aparece e grava; o status continua Devolvida.
7. **Selo:** a nota com devolução parcial mostra "Dev. parcial" na lista de documentos de saída, no
   detalhe (com a quantidade) e na aba Documentos de Saída da carga.
8. **Anexo:** registrar um ticket COM arquivo → o clip da aba abre o visualizador.
9. **Trava de recusa:** numa carga Descarregada, "Registrar Recusa" é recusado com "Exclua o ticket de
   descarga antes de registrar recusa."

- [ ] **Step 4: Derrubar a stack**

Parar as três tasks de background E matar por porta (5246, 50000, 8080):
`Get-NetTCPConnection -LocalPort <p> -State Listen` → `Stop-Process -Id <OwningProcess> -Force`.
Conferir as portas de novo no fim (o file watcher ressuscita processos).

- [ ] **Step 5: Relatório e memória**

Relatar ao usuário o que foi verificado e o que não foi (com o motivo). Atualizar a memória
`gac-1171-discharge-distribution.md` com o estado final (commits, verificação, pendências de deploy:
migration destrutiva, "Recalcular Saldo" nas cargas com ticket, e que o `IDX_SIAGRO_DEV` original NÃO
recebeu a migration — só a cópia `IDX_SIAGRO_DEV_GAC1171`).
