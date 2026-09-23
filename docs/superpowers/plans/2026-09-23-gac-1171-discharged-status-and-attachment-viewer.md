# GAC-1171 (melhorias) — Descarregada / Concluída e visualizador de anexos — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A carga ganha o status **Descarregada**, marcado à mão e reversível, e passa a ser
**Concluída** sozinha quando toda a Conferência de Entregas dela estiver encerrada. Os anexos da
carga e dos contratos ganham um visualizador em diálogo, e o defeito "anexo manual não abre" é
reproduzido e corrigido.

**Architecture:** `ShipmentLoad.Status` continua derivado, com um escritor único
(`ShipmentLoadsRecalculateInvoicedService`). O botão grava só a marca `IsDischarged`. Uma função
pura, `ResolveClosure`, refina o resultado `Invoiced` do `ResolveStatus` em
Faturada, Descarregada ou Concluída. Quem muda `DeliveryStatus` de item de nota de carga recalcula a
carga. No frontend, um módulo `AttachmentViewer` montado em código faz fetch, cria o blob e mostra o
arquivo num diálogo (iframe ou imagem). Os helpers puros ficam num arquivo separado, testado por QUnit.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes em EF InMemory + xUnit), OData v4
(`Microsoft.AspNetCore.OData`), OpenUI5 + TypeScript (QUnit, ui5-test-runner).

**Spec:** `docs/superpowers/specs/2026-09-23-gac-1171-discharged-status-and-attachment-viewer-design.md`
(commit `13f1dcd`). Leia o spec INTEIRO antes da primeira task. Ele argumenta cada regra, e este
plano só diz como executá-las.

## Global Constraints

- Repositórios: **backend** em `C:\Projetos\SiagroB1\siagro-b1-backend-gac-1171`, **frontend** em
  `C:\Projetos\SiagroB1\siagro-b1-frontend-gac-1171`. São dois repos. Os dois worktrees estão na
  branch `feature/gac-1171-discharged-status`. **Nunca** trabalhe em `siagro-b1-backend\`: aquele
  checkout é de outro chamado (assinatura eletrônica).
- Confira `git branch --show-current` antes de **todo** commit. **Nunca** faça push nem merge.
- Arquivo novo → `git add <path>` logo depois de criá-lo.
- Commits: `tipo(escopo): descrição em pt-BR, imperativo, minúscula, sem ponto final`. Tipos:
  `feat fix refactor perf chore docs test`. Escopos: `shipment`, `purchase-contract`,
  `sales-contract`, `invoice`, `platform`. O corpo diz o PORQUÊ. Rodapé: `Refs: GAC-1171`. Commit
  com migration também leva `DB: AddShipmentLoadIsDischarged`. Os dois últimos rodapés:
  `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>` e
  `Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM`.
- Identificadores de código em **inglês**. Texto que o usuário lê (rótulos, mensagens de erro de
  negócio, logs) e comentários em **pt-BR**.
- Enums persistidos como `int`: valor novo **sempre no fim**. `ShipmentLoadStatus.Discharged = 8`,
  `ShipmentLoadMovementType.Discharged = 23`, `ShipmentLoadMovementType.DischargeUndone = 24`.
- "Concluída" **reaproveita** `ShipmentLoadStatus.Completed = 6`. Não crie outro valor.
- Rótulos exatos: `Discharged` → "Descarregada"; movimentos → "Carga Descarregada" e
  "Descarregada Desfeita".
- Em XML de view UI5, toda expressão sobre enum usa `targetType: 'any'`. **Comentário XML não pode
  conter `--`**: ele mata o fragmento inteiro sem erro de build.
- `yarn test` do frontend **não é gate**, porque a trava de cobertura é irreal. Os gates do
  frontend são `yarn ts-typecheck`, `yarn lint` e os testes QUnit rodados pelo `test-runner`
  (Task 10).
- Backend: a suíte inteira (`dotnet test SiagroB1.sln`) passa antes de cada commit. Não aceite
  "já falhava" sem o número do baseline da Task 0.

## Review Focus

1. **Mudança rastreada ainda não salva quando o recálculo roda.** O cancelamento e a exclusão de
   uma devolução reabrem a nota de origem (`SalesInvoicesReturnOriginRestoreService`) sem
   `SaveChanges` antes do hook da carga. Espera-se que a carga **não** fique Concluída com item
   reaberto. Os testes estão na Task 2 (rastreado × banco) e na Task 6 (fluxo real).
2. **Nota recém-faturada ainda Pendente** numa carga cujas outras notas estão todas conferidas:
   espera-se Faturada, **nunca** Concluída, porque a Pendente nem entrou na Conferência. Teste na
   Task 2.
3. **Marcar como Descarregada com a conferência já toda encerrada:** a carga vai direto a Concluída,
   e o log diz "Concluída", não "Descarregada". Teste na Task 3.
4. **PDF gravado como `application/octet-stream`**, comum nos anexos de contrato antigos: espera-se
   que abra como PDF pela extensão. Também um nome acentuado vindo em `filename*=UTF-8''...`: espera-se
   o nome decodificado no Baixar. Testes na Task 10.
5. **Anexo HTML, SVG ou outro tipo ativo:** o visualizador **não** pode renderizá-lo no iframe. Blob
   herda a origem da aplicação, e um HTML anexado rodaria script com a sessão do usuário. Espera-se
   o aviso "Pré-visualização indisponível" com o botão Baixar. Teste na Task 10:
   `resolveViewerKind("text/html") === "unsupported"`; só `text/plain` é texto.

---

## Mapa de arquivos

**Backend** (`siagro-b1-backend-gac-1171/`)

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Enums/ShipmentLoadStatus.cs` | + `Discharged = 8`; comentário de `Completed` |
| `SiagroB1.Domain/Enums/ShipmentLoadMovementType.cs` | + `Discharged = 23`, `DischargeUndone = 24` |
| `SiagroB1.Domain/Entities/ShipmentLoad.cs` | + `bool IsDischarged` |
| `SiagroB1.Domain/Entities/ShipmentLoadChangeLogFields.cs` | rótulos Descarregada e Em Transbordo |
| `SiagroB1.Migrations/AppContext/*_AddShipmentLoadIsDischarged.cs` | coluna nova (gerada) |
| `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRecalculateInvoicedService.cs` | `ResolveClosure`, `AreAllDeliveriesClosedAsync`, apagar a marca, projeção |
| `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsMarkDischargedService.cs` (novo) | ação Marcar |
| `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsUndoDischargedService.cs` (novo) | ação Desfazer |
| `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadCompletionRules.cs` (novo) | texto "como desfazer a Concluída", por tipo de carga |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsUpdateService.cs` | gancho da Conferência |
| `SiagroB1.Application/Services/ShipmentLoads/{Reopen,Refuse,Update,AttachTransactions,DetachTransactions,Cancel}Service.cs`, `ShipmentLoadTransshipmentRules.cs`, `ShippingTransactions/ShippingTransactionsChangeReleaseService.cs` | varredura das travas |
| `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoads{MarkDischarged,UndoDischarged}Controller.cs` (novos) | endpoints |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` | EDM + DI |
| `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargedModelTests.cs` (novo) | enum, rótulos, coluna |
| `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadClosureTests.cs` (novo) | regra de fechamento |
| `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsDischargedServiceTests.cs` (novo) | Marcar/Desfazer |
| `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesItemsUpdateLoadClosureTests.cs` (novo) | gancho da Conferência |
| `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadClosureInvoiceWritersTests.cs` (novo) | outros escritores de `DeliveryStatus` |
| `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadClosureGuardsTests.cs` (novo) | varredura |

**Frontend** (`siagro-b1-frontend-gac-1171/webapp/`)

| Arquivo | Responsabilidade |
|---|---|
| `model/formatter.ts` | rótulos e estados de `Discharged`, movimentos novos |
| `controller/shipmentLoads/{Main,Panel,Detail}.controller.ts`, `view/shipmentLoads/{Panel,Detail}.view.xml`, `view/shipmentLoads/fragments/ShipmentLoadTransshipments.fragment.xml` | status na tela |
| `controller/storageTransactions/sales/Main.controller.ts` | filtro e aviso |
| `helpers/AttachmentViewerHelpers.ts` (novo) | funções puras: tipo, espécie, nome |
| `test/unit/helpers/AttachmentViewerHelpers.qunit.ts` (novo) + `test/unit/unitTests.qunit.ts` | testes |
| `dialogs/AttachmentViewer.ts` (novo) | o diálogo |
| `view/shipmentLoads/fragments/ShipmentLoad{Attachments,Discharges}.fragment.xml`, `controller/shipmentLoads/BaseController.ts` | carga |
| `view/{purchase,sales}Contracts/fragments/*ContractAttachments.fragment.xml`, `controller/{purchase,sales}Contracts/*ContractsBaseController.ts` | contratos |

---

### Task 0: Preparar os worktrees e medir o baseline

**Files:** nenhum versionado.

- [ ] **Step 1: Copiar os artefatos locais não versionados para o worktree do backend**

`SiagroB1.Reports/wwwroot` e os `appsettings` por ambiente do Web ficam fora do git (exclude
local). Sem eles, 13 testes de relatório falham e o profile `yktb` não sobe.

```bash
cd "C:/Projetos/SiagroB1"
cp -r siagro-b1-backend/SiagroB1.Reports/wwwroot siagro-b1-backend-gac-1171/SiagroB1.Reports/
for f in appsettings.Yokotobi-Development.json appsettings.Yokotobi-Staging.json appsettings.Yokotobi-Production.json appsettings.MhAgro-Production.json; do
  [ -f "siagro-b1-backend-gac-1171/SiagroB1.Web/$f" ] || cp "siagro-b1-backend/SiagroB1.Web/$f" siagro-b1-backend-gac-1171/SiagroB1.Web/
done
diff siagro-b1-backend/SiagroB1.Web/Properties/launchSettings.json siagro-b1-backend-gac-1171/SiagroB1.Web/Properties/launchSettings.json && echo "launchSettings iguais"
git -C siagro-b1-backend-gac-1171 status --short
```

Expected: `git status` **vazio**. Se algum `appsettings` copiado aparecer como untracked, **não** o
adicione: acrescente-o a `.git/info/exclude` do repo principal. Se o `launchSettings.json` diferir,
pare e reporte.

- [ ] **Step 2: Baseline do backend**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend-gac-1171"
dotnet build SiagroB1.sln -v q
dotnet test SiagroB1.sln --no-build -v q 2>&1 | tail -5
```

Expected: build sem erro, com o total de testes e **zero falhas**. Anote o total (ex.:
`Passed: 2231`) no relatório da task. Com falha, pare: não comece sobre um baseline vermelho.

- [ ] **Step 3: Dependências e baseline do frontend**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-frontend-gac-1171"
yarn install --frozen-lockfile
yarn ts-typecheck && yarn lint
```

Expected: os dois limpos. Anote qualquer aviso preexistente.

---

### Task 1: Domínio — enum, marca, rótulos e migration

**Files:**
- Modify: `SiagroB1.Domain/Enums/ShipmentLoadStatus.cs`
- Modify: `SiagroB1.Domain/Enums/ShipmentLoadMovementType.cs`
- Modify: `SiagroB1.Domain/Entities/ShipmentLoad.cs` (logo depois de `DischargedQuantity`, ~linha 149)
- Modify: `SiagroB1.Domain/Entities/ShipmentLoadChangeLogFields.cs:63-72`
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddShipmentLoadIsDischarged.cs` (+ Designer, + snapshot; gerados)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargedModelTests.cs`

**Interfaces:**
- Produces: `ShipmentLoadStatus.Discharged` (= 8); `ShipmentLoadMovementType.Discharged` (= 23) e
  `.DischargeUndone` (= 24); `bool ShipmentLoad.IsDischarged`;
  `ShipmentLoadChangeLogFields.DescribeStatus(ShipmentLoadStatus.Discharged) == "Descarregada"`.

- [ ] **Step 1: Escrever o teste que falha**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 (melhorias): o status Descarregada e a marca manual que o produz.
/// </summary>
/// <remarks>
/// Os valores numéricos são o contrato com o banco: enum persistido como int, e renumerar
/// reescreveria o significado de toda linha gravada sem nenhuma migration avisar.
/// </remarks>
public class ShipmentLoadDischargedModelTests
{
    [Fact]
    public void Discharged_status_is_persisted_as_8()
    {
        Assert.Equal(8, (int)ShipmentLoadStatus.Discharged);
        Assert.Equal(7, (int)ShipmentLoadStatus.InTransshipment);
        Assert.Equal(6, (int)ShipmentLoadStatus.Completed);
    }

    [Fact]
    public void Discharge_movements_are_appended_at_the_end()
    {
        Assert.Equal(23, (int)ShipmentLoadMovementType.Discharged);
        Assert.Equal(24, (int)ShipmentLoadMovementType.DischargeUndone);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Discharged, "Descarregada")]
    [InlineData(ShipmentLoadStatus.InTransshipment, "Em Transbordo")]
    [InlineData(ShipmentLoadStatus.Completed, "Concluída")]
    public void Change_log_describes_the_status_in_portuguese(ShipmentLoadStatus status, string label)
    {
        Assert.Equal(label, ShipmentLoadChangeLogFields.DescribeStatus(status));
    }

    [Fact]
    public async Task A_new_load_is_not_discharged()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
        });
        await db.Context.SaveChangesAsync();

        Assert.False((await db.Context.ShipmentLoads.AsNoTracking().SingleAsync()).IsDischarged);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadDischargedModelTests`
Expected: FAIL de compilação: `'ShipmentLoadStatus' does not contain a definition for 'Discharged'`.

- [ ] **Step 3: Implementar**

`ShipmentLoadStatus.cs`: troque as duas últimas linhas do enum por:

```csharp
    Completed = 6,          // Remoção: encerrada à mão. Normal: toda a conferência de entrega encerrada (GAC-1171)
    InTransshipment = 7,    // Descarregada em armazém intermediário, aguardando a saída (GAC-1181)
    Discharged = 8          // Faturada e marcada à mão como descarregada no destino (GAC-1171)
```

Acrescente ao `<summary>` do enum, depois da frase das exceções:

```csharp
/// <para>
/// GAC-1171 (melhorias): <c>Discharged</c> e o <c>Completed</c> da carga Normal também são
/// derivados. Eles refinam o <c>Invoiced</c> pela marca manual <c>ShipmentLoad.IsDischarged</c> e
/// pela Conferência de Entregas; ver <c>ShipmentLoadsRecalculateInvoicedService.ResolveClosure</c>.
/// </para>
```

`ShipmentLoadMovementType.cs`: troque a última linha por:

```csharp
    TransshipmentLotExitAttached = 22, // Saída do lote vinculada (fase 2) — emite a liberação, não fecha o transbordo
    Discharged = 23,             // Carga marcada à mão como descarregada no destino (GAC-1171)
    DischargeUndone = 24         // Marcação de descarregada desfeita — o status volta ao do recálculo
```

`ShipmentLoad.cs`, logo depois da propriedade `DischargedQuantity`:

```csharp
    /// <summary>
    /// Marca MANUAL de "a mercadoria foi descarregada no destino" (GAC-1171, melhorias). Não é
    /// o status: quem traduz a marca em <see cref="ShipmentLoadStatus.Discharged"/> é o
    /// recálculo, e só enquanto a carga está Faturada. Fora de Faturada o recálculo apaga a
    /// marca, porque ela se referia a uma mercadoria que já não é a faturada.
    /// </summary>
    /// <remarks>
    /// Independe dos tickets (<see cref="DischargedQuantity"/>): o usuário pode marcar sem ticket
    /// nenhum, e registrar ticket não marca nada.
    /// </remarks>
    public bool IsDischarged { get; set; }
```

`ShipmentLoadChangeLogFields.DescribeStatus`: acrescente, antes do `_ =>`:

```csharp
        ShipmentLoadStatus.InTransshipment => "Em Transbordo",
        ShipmentLoadStatus.Discharged => "Descarregada",
```

- [ ] **Step 4: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadDischargedModelTests`
Expected: PASS (6 testes).

- [ ] **Step 5: Gerar a migration**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend-gac-1171"
dotnet build SiagroB1.sln -v q
dotnet ef migrations add AddShipmentLoadIsDischarged --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir AppContext --no-build
```

Abra o `<timestamp>_AddShipmentLoadIsDischarged.cs` gerado. `Up()` precisa conter **somente**:

```csharp
            migrationBuilder.AddColumn<bool>(
                name: "IsDischarged",
                table: "SHIPMENT_LOADS",
                type: "bit",
                nullable: false,
                defaultValue: false);
```

e `Down()` precisa conter só o `DropColumn` correspondente. **Qualquer outra operação é drift do
snapshot**: pare e reporte, sem editar à mão (ver `migrations-hand-edited-baseline-sync` na
memória). Depois:

```bash
dotnet build SiagroB1.sln -v q
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
git add SiagroB1.Migrations/AppContext/*_AddShipmentLoadIsDischarged*.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargedModelTests.cs
```

Expected: "No changes have been made to the model since the last migration."

- [ ] **Step 6: Suíte inteira e commit**

```bash
dotnet test SiagroB1.sln -v q 2>&1 | tail -3
git branch --show-current   # feature/gac-1171-discharged-status
git add -A SiagroB1.Domain SiagroB1.Migrations SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargedModelTests.cs
git commit -F - <<'EOF'
feat(shipment): acrescentar o status Descarregada e a marca manual na carga

A marca IsDischarged é o que o botão grava. O status continua saindo do
recálculo, que é o escritor único, e a marca só vira Descarregada
enquanto a carga está Faturada. Os movimentos novos entram no fim do enum
porque ele é persistido como int.

Refs: GAC-1171
DB: AddShipmentLoadIsDischarged
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM
EOF
```

Expected: a suíte com baseline + 6 testes, zero falhas.

---

### Task 2: Regra de fechamento no recálculo da carga

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRecalculateInvoicedService.cs` (bloco das linhas ~93-104 e métodos novos depois de `ResolveStatus`)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadClosureTests.cs`

**Interfaces:**
- Consumes: `ShipmentLoad.IsDischarged`, `ShipmentLoadStatus.Discharged` (Task 1).
- Produces:
  - `public static ShipmentLoadStatus ResolveClosure(ShipmentLoadStatus baseStatus, bool isDischarged, bool allDeliveriesClosed)`
  - `public static Task<bool> AreAllDeliveriesClosedAsync(AppDbContext context, Guid shipmentLoadKey, ICollection<Guid>? excludedInvoiceKeys)`
  - A semântica de `RecalculateAsync(AppDbContext, Guid, ICollection<Guid>?)`: apaga
    `IsDischarged` fora de Faturada e projeta `Invoiced` nos romaneios também em Descarregada e
    Concluída.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 (melhorias): como o ramo Faturada se desdobra em Faturada, Descarregada e Concluída.
/// </summary>
/// <remarks>
/// Concluída = todos os itens das notas Normais CONFIRMADAS da carga com a entrega encerrada, sem
/// nota Pendente. A Pendente ainda não entrou na Conferência, que exige Confirmed, e as
/// Canceladas/Retornadas ficam fora como ficam fora da tela.
/// </remarks>
public class ShipmentLoadClosureTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsRecalculateInvoicedService Service() => new(_db);

    private ShipmentLoad Load(decimal total = 90_000, bool isDischarged = false)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000031",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = total,
            IsDischarged = isDischarged,
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

    private SalesInvoice Invoice(
        ShipmentLoad load,
        decimal quantity,
        InvoiceStatus status = InvoiceStatus.Confirmed,
        SalesInvoiceDeliveryStatus delivery = SalesInvoiceDeliveryStatus.Open)
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
        });

        _db.Context.SalesInvoices.Add(invoice);
        return invoice;
    }

    private Task<ShipmentLoad> SavedAsync() => _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();

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
        ShipmentLoadStatus baseStatus, bool isDischarged, bool allClosed, ShipmentLoadStatus expected)
    {
        Assert.Equal(expected,
            ShipmentLoadsRecalculateInvoicedService.ResolveClosure(baseStatus, isDischarged, allClosed));
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
    public async Task The_manual_mark_turns_invoiced_into_discharged_and_keeps_the_shipments_invoiced()
    {
        var load = Load(isDischarged: true);
        var shipment = Shipment(load);
        Invoice(load, 90_000);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        var saved = await SavedAsync();
        Assert.Equal(ShipmentLoadStatus.Discharged, saved.Status);
        Assert.True(saved.IsDischarged);
        var savedShipment = await _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == shipment.Key);
        Assert.Equal(StorageTransactionsStatus.Invoiced, savedShipment.TransactionStatus);
    }

    /// <summary>Review Focus 2: a Pendente ainda não entrou na Conferência.</summary>
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
    public async Task Leaving_invoiced_clears_the_manual_mark()
    {
        var load = Load(isDischarged: true);
        Invoice(load, 90_000, InvoiceStatus.Cancelled);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key);

        var saved = await SavedAsync();
        Assert.Equal(ShipmentLoadStatus.Open, saved.Status);
        Assert.False(saved.IsDischarged);
    }

    /// <summary>
    /// Review Focus 1: o recálculo roda DENTRO de transações alheias, às vezes antes do flush
    /// que tornaria a mudança do item visível no banco. A regra precisa ler o estado RASTREADO.
    /// </summary>
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
    public async Task A_load_without_confirmed_invoices_is_never_completed()
    {
        Assert.False(await ShipmentLoadsRecalculateInvoicedService.AreAllDeliveriesClosedAsync(
            _db.Context, Guid.NewGuid(), excludedInvoiceKeys: null));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadClosureTests`
Expected: FAIL de compilação: `ResolveClosure` / `AreAllDeliveriesClosedAsync` não existem.

- [ ] **Step 3: Implementar**

Em `RecalculateAsync` (estático), troque o bloco

```csharp
        load.InvoicedQuantity = invoiced;
        load.ReturnedToWarehouseQuantity = returned;
        load.TransshippedQuantity = transshipped;
        load.Status = ResolveStatus(load.TotalQuantity, invoiced, returned, transshipped, hasOpenTransshipment);
        load.UpdatedAt = DateTime.Now;

        // Carga ENCERRADA (faturada ou devolvida ao armazém) não devolve romaneio para
        // Confirmed, que é o filtro da tela de Montagem: a mercadoria já saiu, por venda ou por
        // devolução, e o romaneio não pode reaparecer como disponível para outra carga.
        var shipmentStatus = load.Status is ShipmentLoadStatus.Invoiced or ShipmentLoadStatus.Returned
            ? StorageTransactionsStatus.Invoiced
            : StorageTransactionsStatus.Confirmed;
```

por

```csharp
        var baseStatus = ResolveStatus(load.TotalQuantity, invoiced, returned, transshipped, hasOpenTransshipment);

        // GAC-1171 (melhorias): a marca manual só vale para a mercadoria que estava faturada
        // quando o usuário a marcou. Uma nota cancelada, excluída ou devolvida (ou um transbordo)
        // tira a carga de Faturada, e um faturamento novo depois disso não pode herdar em
        // silêncio um "Descarregada" que se referia a outra mercadoria.
        if (baseStatus != ShipmentLoadStatus.Invoiced)
            load.IsDischarged = false;

        // Só o ramo Faturada usa a Conferência: fora dele a consulta seria trabalho à toa.
        var allDeliveriesClosed = baseStatus == ShipmentLoadStatus.Invoiced
            && await AreAllDeliveriesClosedAsync(context, shipmentLoadKey, excludedInvoiceKeys);

        load.InvoicedQuantity = invoiced;
        load.ReturnedToWarehouseQuantity = returned;
        load.TransshippedQuantity = transshipped;
        load.Status = ResolveClosure(baseStatus, load.IsDischarged, allDeliveriesClosed);
        load.UpdatedAt = DateTime.Now;

        // Carga ENCERRADA (faturada, descarregada, concluída ou devolvida ao armazém) não
        // devolve romaneio para Confirmed, que é o filtro da tela de Montagem: a mercadoria já
        // saiu, por venda ou por devolução, e o romaneio não pode reaparecer como disponível
        // para outra carga. Sem Discharged/Completed aqui, marcar a carga como descarregada
        // devolveria os romaneios à Montagem.
        var shipmentStatus = load.Status is ShipmentLoadStatus.Invoiced or ShipmentLoadStatus.Returned
            or ShipmentLoadStatus.Discharged or ShipmentLoadStatus.Completed
            ? StorageTransactionsStatus.Invoiced
            : StorageTransactionsStatus.Confirmed;
```

Depois do método `ResolveStatus`, acrescente:

```csharp
    /// <summary>
    /// Desdobra o <c>Invoiced</c> de <see cref="ResolveStatus"/> pela marca manual e pela
    /// Conferência de Entregas (GAC-1171, melhorias). Qualquer outro status passa intacto.
    /// </summary>
    /// <remarks>
    /// A Concluída vence a marca: ela é a afirmação mais forte ("tudo foi conferido"), e passar
    /// por Descarregada antes é opcional. É por isso que desfazer a descarga com a carga
    /// Concluída é recusado (<c>ShipmentLoadsUndoDischargedService</c>): o botão não teria
    /// efeito visível.
    /// </remarks>
    public static ShipmentLoadStatus ResolveClosure(
        ShipmentLoadStatus baseStatus,
        bool isDischarged,
        bool allDeliveriesClosed)
    {
        if (baseStatus != ShipmentLoadStatus.Invoiced)
            return baseStatus;

        if (allDeliveriesClosed)
            return ShipmentLoadStatus.Completed;

        return isDischarged ? ShipmentLoadStatus.Discharged : ShipmentLoadStatus.Invoiced;
    }

    /// <summary>
    /// Verdadeiro quando a Conferência de Entregas da carga está toda encerrada: há ao menos um
    /// item em nota Normal Confirmada, nenhuma nota Normal Pendente, e todos os itens das
    /// Confirmadas estão <c>Closed</c>. Canceladas e Retornadas ficam fora, como ficam fora da
    /// tela de Conferência.
    /// </summary>
    /// <remarks>
    /// ⚠️ Materializa as notas com os itens em vez de agregar no servidor, e filtra o status EM
    /// MEMÓRIA. O motivo é o mesmo do <c>excludedInvoiceKeys</c> de
    /// <see cref="CalculateInvoicedAsync"/>: o recálculo roda dentro de transações alheias, às
    /// vezes antes do flush. O cancelamento de uma devolução, por exemplo, reabre a nota de
    /// origem e chama o hook da carga sem salvar antes. Uma consulta com o status no WHERE leria
    /// o banco, veria a origem ainda Retornada e concluiria a carga com um item reaberto. Com a
    /// consulta rastreada, o EF devolve as instâncias já rastreadas com os valores atuais, e o
    /// filtro em memória enxerga a mudança. São poucas notas por carga, então o custo é
    /// irrelevante.
    /// </remarks>
    public static async Task<bool> AreAllDeliveriesClosedAsync(
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
            return false;

        var items = live
            .Where(i => i.InvoiceStatus == InvoiceStatus.Confirmed)
            .SelectMany(i => i.Items)
            .Where(item => context.Entry(item).State != EntityState.Deleted)
            .ToList();

        return items.Count > 0
               && items.All(item => item.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed);
    }
```

Por fim, estenda o `<remarks>` da classe com um `<para>`: "GAC-1171 (melhorias): o ramo Faturada
se desdobra em Faturada, Descarregada e Concluída por <see cref="ResolveClosure"/>. A marca
<c>IsDischarged</c> é escrita pelos serviços Marcar/Desfazer, mas o status continua saindo daqui."

- [ ] **Step 4: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentLoadClosureTests|FullyQualifiedName~ShipmentLoadsRecalculateInvoicedServiceTests|FullyQualifiedName~ShipmentLoadResolveStatusTests"`
Expected: PASS em todos.

- [ ] **Step 5: Suíte inteira e commit**

Rode `dotnet test SiagroB1.sln -v q 2>&1 | tail -3`. Se algum teste existente passar a enxergar
`Completed` onde esperava `Invoiced`, é porque o fixture dele fecha as entregas. Leia o teste: se
ele descreve uma carga com a conferência toda encerrada, **a nova expectativa é Concluída** e o
teste é atualizado com um comentário citando o GAC-1171. Se não descreve, é bug, e ele volta para
esta task.

```bash
git add SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadClosureTests.cs
git commit -F - <<'EOF'
feat(shipment): derivar Descarregada e Concluída no recálculo da carga

O ramo Faturada passa a olhar a marca manual e a Conferência de Entregas.
A Concluída exige toda nota Normal confirmada com a entrega encerrada e
nenhuma nota pendente. A leitura é feita em memória porque o recálculo
roda antes do flush em transações alheias.

Refs: GAC-1171
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM
EOF
```

---

### Task 3: Serviços Marcar e Desfazer Descarregada

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsMarkDischargedService.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsUndoDischargedService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsDischargedServiceTests.cs`

**Interfaces:**
- Consumes: `ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(AppDbContext, Guid, ICollection<Guid>?)`
  (Task 2); `ShipmentLoadsChangeLogService.Register(Guid, string, string?, string?, string)`;
  `ShipmentLoadsMovementLogService.Register(Guid, ShipmentLoadMovementType, decimal quantity, decimal balanceAfter, string description, string userName, ...)`.
- Produces: `ShipmentLoadsMarkDischargedService.ExecuteAsync(Guid key, string userName)` e
  `ShipmentLoadsUndoDischargedService.ExecuteAsync(Guid key, string userName)`. Os dois têm o
  construtor `(IUnitOfWork db, ShipmentLoadsMovementLogService movementLog, ShipmentLoadsChangeLogService changeLog)`.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 (melhorias): marcar a carga como descarregada no destino, e desfazer. As duas ações
/// gravam só a marca, e o status resultante é o do recálculo.
/// </summary>
public class ShipmentLoadsDischargedServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsMarkDischargedService Mark() => new(
        _db, new ShipmentLoadsMovementLogService(_db.Context), new ShipmentLoadsChangeLogService(_db.Context));

    private ShipmentLoadsUndoDischargedService Undo() => new(
        _db, new ShipmentLoadsMovementLogService(_db.Context), new ShipmentLoadsChangeLogService(_db.Context));

    /// <summary>
    /// Carga coerente com o recálculo: 90 t montadas e uma nota confirmada de 90 t. O status e
    /// a marca semeados precisam casar com o que o recálculo derivaria.
    /// </summary>
    private async Task<ShipmentLoad> SeedAsync(
        ShipmentLoadStatus status = ShipmentLoadStatus.Invoiced,
        bool isDischarged = false,
        SalesInvoiceDeliveryStatus delivery = SalesInvoiceDeliveryStatus.Open,
        ShipmentLoadType loadType = ShipmentLoadType.Normal,
        decimal invoiced = 90_000)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000041",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            LoadType = loadType,
            Status = status,
            IsDischarged = isDischarged,
            TotalQuantity = 90_000,
            InvoicedQuantity = invoiced,
        };
        _db.Context.ShipmentLoads.Add(load);

        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceNumber = "000000777",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
            ShipmentLoadKey = load.Key,
        };
        invoice.Items.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = invoiced,
            DeliveredQuantity = delivery == SalesInvoiceDeliveryStatus.Closed ? invoiced : 0m,
            DeliveryStatus = delivery,
        });
        _db.Context.SalesInvoices.Add(invoice);

        await _db.Context.SaveChangesAsync();
        return load;
    }

    private Task<ShipmentLoad> SavedAsync() => _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();

    [Fact]
    public async Task Marking_an_invoiced_load_makes_it_discharged_with_log_and_movement()
    {
        var load = await SeedAsync();

        await Mark().ExecuteAsync(load.Key, "tester");

        var saved = await SavedAsync();
        Assert.Equal(ShipmentLoadStatus.Discharged, saved.Status);
        Assert.True(saved.IsDischarged);
        Assert.Equal("tester", saved.UpdatedBy);

        var log = await _db.Context.ShipmentLoadsChangeLogs.SingleAsync();
        Assert.Equal(ShipmentLoadChangeLogFields.Status, log.Field);
        Assert.Equal("Faturada", log.OldValue);
        Assert.Equal("Descarregada", log.NewValue);

        var movement = await _db.Context.ShipmentLoadMovements.SingleAsync();
        Assert.Equal(ShipmentLoadMovementType.Discharged, movement.MovementType);
    }

    /// <summary>Review Focus 3: com a conferência toda encerrada, o resultado é Concluída.</summary>
    [Fact]
    public async Task Marking_with_every_delivery_closed_completes_the_load_and_logs_it()
    {
        var load = await SeedAsync(delivery: SalesInvoiceDeliveryStatus.Closed);

        await Mark().ExecuteAsync(load.Key, "tester");

        Assert.Equal(ShipmentLoadStatus.Completed, (await SavedAsync()).Status);
        Assert.Equal("Concluída", (await _db.Context.ShipmentLoadsChangeLogs.SingleAsync()).NewValue);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Discharged, "já está marcada como descarregada")]
    [InlineData(ShipmentLoadStatus.Completed, "já foi concluída")]
    [InlineData(ShipmentLoadStatus.PartiallyInvoiced, "ainda tem saldo a faturar")]
    [InlineData(ShipmentLoadStatus.Cancelled, "está cancelada")]
    [InlineData(ShipmentLoadStatus.Returned, "foi devolvida ao armazém")]
    [InlineData(ShipmentLoadStatus.InTransshipment, "está em transbordo")]
    [InlineData(ShipmentLoadStatus.Open, "ainda não foi faturada")]
    [InlineData(ShipmentLoadStatus.Planned, "ainda não foi faturada")]
    public async Task Marking_outside_invoiced_is_refused(ShipmentLoadStatus status, string reason)
    {
        var load = await SeedAsync(status: status);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Mark().ExecuteAsync(load.Key, "tester"));

        Assert.Equal($"A carga CG000041 {reason}.", ex.Message);
        Assert.Empty(_db.Context.ShipmentLoadsChangeLogs);
    }

    [Fact]
    public async Task Marking_a_removal_load_is_refused()
    {
        var load = await SeedAsync(loadType: ShipmentLoadType.Removal);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Mark().ExecuteAsync(load.Key, "tester"));

        Assert.Contains("Remoção", ex.Message);
    }

    [Fact]
    public async Task Marking_an_unknown_load_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Mark().ExecuteAsync(Guid.NewGuid(), "tester"));
    }

    [Fact]
    public async Task Undoing_returns_the_load_to_invoiced_with_log_and_movement()
    {
        var load = await SeedAsync(status: ShipmentLoadStatus.Discharged, isDischarged: true);

        await Undo().ExecuteAsync(load.Key, "tester");

        var saved = await SavedAsync();
        Assert.Equal(ShipmentLoadStatus.Invoiced, saved.Status);
        Assert.False(saved.IsDischarged);

        var log = await _db.Context.ShipmentLoadsChangeLogs.SingleAsync();
        Assert.Equal("Descarregada", log.OldValue);
        Assert.Equal("Faturada", log.NewValue);

        var movement = await _db.Context.ShipmentLoadMovements.SingleAsync();
        Assert.Equal(ShipmentLoadMovementType.DischargeUndone, movement.MovementType);
    }

    [Fact]
    public async Task Undoing_a_completed_load_is_refused_pointing_to_the_reconciliation()
    {
        var load = await SeedAsync(
            status: ShipmentLoadStatus.Completed, isDischarged: true, delivery: SalesInvoiceDeliveryStatus.Closed);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Undo().ExecuteAsync(load.Key, "tester"));

        Assert.Equal(
            "A carga CG000041 está concluída. Estorne a conferência de entrega antes de desfazer a descarga.",
            ex.Message);
        Assert.True((await SavedAsync()).IsDischarged);
    }

    [Fact]
    public async Task Undoing_a_load_that_is_not_discharged_is_refused()
    {
        var load = await SeedAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Undo().ExecuteAsync(load.Key, "tester"));

        Assert.Equal("A carga CG000041 não está marcada como descarregada.", ex.Message);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadsDischargedServiceTests`
Expected: FAIL de compilação: os dois serviços não existem.

- [ ] **Step 3: Implementar `ShipmentLoadsMarkDischargedService.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Marca a carga como descarregada no destino (GAC-1171, melhorias).
/// </summary>
/// <remarks>
/// Grava SÓ a marca <see cref="ShipmentLoad.IsDischarged"/>. O status continua saindo de
/// <see cref="ShipmentLoadsRecalculateInvoicedService"/>, que é o escritor único. Por isso o
/// resultado pode ser Concluída em vez de Descarregada, quando a Conferência de Entregas já
/// estiver toda encerrada. O log registra o status RESULTANTE, não o pedido.
/// <para>
/// Não exige ticket de descarga: decisão do usuário. O ticket pode nunca chegar, e quem sabe que a
/// mercadoria foi entregue é a operação. O par de desfazer é <see cref="ShipmentLoadsUndoDischargedService"/>,
/// pelo mesmo motivo do Concluir/Reabrir da Remoção: um encerramento manual precisa de um desfazer manual.
/// </para>
/// </remarks>
public class ShipmentLoadsMarkDischargedService(
    IUnitOfWork db,
    ShipmentLoadsMovementLogService movementLog,
    ShipmentLoadsChangeLogService changeLog)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == key) ??
                   throw new NotFoundException($"Shipment load not found key {key}");

        if (load.LoadType == ShipmentLoadType.Removal)
            throw new ApplicationException(
                $"A carga {load.Code} é do tipo Remoção e não passa por descarga.");

        if (load.Status != ShipmentLoadStatus.Invoiced)
        {
            var reason = load.Status switch
            {
                ShipmentLoadStatus.Discharged => "já está marcada como descarregada",
                ShipmentLoadStatus.Completed => "já foi concluída",
                ShipmentLoadStatus.PartiallyInvoiced => "ainda tem saldo a faturar",
                ShipmentLoadStatus.Cancelled => "está cancelada",
                ShipmentLoadStatus.Returned => "foi devolvida ao armazém",
                ShipmentLoadStatus.InTransshipment => "está em transbordo",
                _ => "ainda não foi faturada",
            };

            throw new ApplicationException($"A carga {load.Code} {reason}.");
        }

        try
        {
            await db.BeginTransactionAsync();

            var previousStatus = load.Status;

            load.IsDischarged = true;

            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, load.Key, excludedInvoiceKeys: null);

            load.UpdatedAt = DateTime.Now;
            load.UpdatedBy = userName;

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.Status,
                ShipmentLoadChangeLogFields.DescribeStatus(previousStatus),
                ShipmentLoadChangeLogFields.DescribeStatus(load.Status),
                userName);

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.Discharged,
                decimal.Zero,
                load.AvailableQuantity,
                "Carga marcada como descarregada no destino.",
                userName);

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }
}
```

- [ ] **Step 4: Implementar `ShipmentLoadsUndoDischargedService.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Desfaz a marca de descarregada (GAC-1171, melhorias): o "voltar ao status anterior".
/// </summary>
/// <remarks>
/// O status anterior NÃO é guardado: ele sai do recálculo, como no <c>ShipmentLoadsReopenService</c>.
/// Guardar um "status anterior" criaria uma segunda fonte de verdade, e ela mentiria assim que
/// uma nota fosse cancelada no meio do caminho.
/// <para>
/// Recusado com a carga Concluída: ali a marca não decide nada (a Conferência vence), e
/// desfazê-la não mudaria o status. O caminho é estornar a conferência de entrega.
/// </para>
/// </remarks>
public class ShipmentLoadsUndoDischargedService(
    IUnitOfWork db,
    ShipmentLoadsMovementLogService movementLog,
    ShipmentLoadsChangeLogService changeLog)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == key) ??
                   throw new NotFoundException($"Shipment load not found key {key}");

        if (load.Status == ShipmentLoadStatus.Completed && load.LoadType == ShipmentLoadType.Normal)
            throw new ApplicationException(
                $"A carga {load.Code} está concluída. " +
                "Estorne a conferência de entrega antes de desfazer a descarga.");

        if (load.Status != ShipmentLoadStatus.Discharged)
            throw new ApplicationException(
                $"A carga {load.Code} não está marcada como descarregada.");

        try
        {
            await db.BeginTransactionAsync();

            var previousStatus = load.Status;

            load.IsDischarged = false;

            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, load.Key, excludedInvoiceKeys: null);

            load.UpdatedAt = DateTime.Now;
            load.UpdatedBy = userName;

            changeLog.Register(
                load.Key,
                ShipmentLoadChangeLogFields.Status,
                ShipmentLoadChangeLogFields.DescribeStatus(previousStatus),
                ShipmentLoadChangeLogFields.DescribeStatus(load.Status),
                userName);

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.DischargeUndone,
                decimal.Zero,
                load.AvailableQuantity,
                "Marcação de descarregada desfeita.",
                userName);

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }
}
```

- [ ] **Step 5: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadsDischargedServiceTests`
Expected: PASS (17 casos). Se o `InMemory` recusar `BeginTransactionAsync`, veja como
`ShipmentLoadsCompleteServiceTests` passa com o mesmo `TestDb.CreateUnitOfWork()`: ele passa, e o
motivo é o mesmo aqui.

- [ ] **Step 6: Suíte inteira e commit**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsMarkDischargedService.cs SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsUndoDischargedService.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsDischargedServiceTests.cs
dotnet test SiagroB1.sln -v q 2>&1 | tail -3
git commit -F - <<'EOF'
feat(shipment): marcar e desfazer carga descarregada no destino

As duas ações gravam só a marca e deixam o recálculo decidir o status, de
modo que desfazer volta ao status que os números dão, sem guardar um
"anterior". Desfazer com a carga concluída é recusado: ali quem manda é a
conferência de entrega.

Refs: GAC-1171
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM
EOF
```

---

### Task 4: Actions OData de Marcar e Desfazer

**Files:**
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsMarkDischargedController.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsUndoDischargedController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (logo depois do bloco `shipmentLoadsReopen`, ~linha 633)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (logo depois de `AddScoped<ShipmentLoadsReopenService>()`, ~linha 404)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadEdmModelTests.cs`

**Interfaces:**
- Consumes: os dois serviços da Task 3.
- Produces: `POST odata/ShipmentLoadsMarkDischarged` e `POST odata/ShipmentLoadsUndoDischarged`, com
  corpo `{ "Key": "<guid>" }`. No EDM: `ShipmentLoad.IsDischarged` e o membro `Discharged` no enum
  `ShipmentLoadStatus`.

- [ ] **Step 1: Escrever o teste que falha**

Acrescente a `ShipmentLoadEdmModelTests.cs`:

```csharp
    /// <summary>GAC-1171 (melhorias) — Marcar e Desfazer Descarregada.</summary>
    [Theory]
    [InlineData("ShipmentLoadsMarkDischarged")]
    [InlineData("ShipmentLoadsUndoDischarged")]
    public void Discharged_actions_are_declared_with_the_load_key(string actionName)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == actionName);

        Assert.Equal(new[] { "Key" }, action.Parameters.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void The_discharged_mark_and_status_are_in_the_edm()
    {
        Assert.NotNull(EntityType("ShipmentLoad").FindProperty("IsDischarged"));

        var status = Model().SchemaElements.OfType<IEdmEnumType>().Single(t => t.Name == "ShipmentLoadStatus");
        Assert.Contains(status.Members, m => m.Name == "Discharged");
    }
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadEdmModelTests`
Expected: FAIL em `Discharged_actions_are_declared_with_the_load_key` (`Sequence contains no matching
element`). O teste do EDM já passa (o convention builder pega a propriedade), e fica como guarda.

- [ ] **Step 3: Implementar**

`ShipmentLoadsMarkDischargedController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Marca a carga como descarregada no destino (GAC-1171, melhorias).
/// </summary>
public class ShipmentLoadsMarkDischargedController(
    ShipmentLoadsMarkDischargedService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsMarkDischarged")]
    public async Task<ActionResult> MarkDischarged([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ parameters chega NULO quando nenhum parâmetro do EDM é enviado.
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
            return BadRequest("Informe a carga.");

        try
        {
            await service.ExecuteAsync((Guid)keyObj, User.Identity?.Name ?? "unknown");
            return Ok();
        }
        catch (Exception e)
        {
            // Mesma triagem do upload de anexo: 400 só para erro de negócio. Erro de servidor
            // devolvido como 400 chegaria ao usuário como texto cru em inglês.
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
```

`ShipmentLoadsUndoDischargedController.cs`: igual, com a classe `ShipmentLoadsUndoDischargedController`,
o serviço `ShipmentLoadsUndoDischargedService`, a rota `odata/ShipmentLoadsUndoDischarged`, o método
`UndoDischarged` e o summary "Desfaz a marca de descarregada da carga (GAC-1171, melhorias)."
Escreva o arquivo inteiro. Não herde do outro.

`ODataConfigurations.cs`, logo depois de `shipmentLoadsReopen.Returns<IActionResult>();`:

```csharp

        // GAC-1171 (melhorias): marca manual de descarga no destino, e o desfazer.
        var shipmentLoadsMarkDischarged = modelBuilder.Action("ShipmentLoadsMarkDischarged");
        shipmentLoadsMarkDischarged.Parameter<Guid>("Key");
        shipmentLoadsMarkDischarged.Returns<IActionResult>();

        var shipmentLoadsUndoDischarged = modelBuilder.Action("ShipmentLoadsUndoDischarged");
        shipmentLoadsUndoDischarged.Parameter<Guid>("Key");
        shipmentLoadsUndoDischarged.Returns<IActionResult>();
```

`ServiceCollectionExtensions.cs`, logo depois de `services.AddScoped<ShipmentLoadsReopenService>();`:

```csharp
        services.AddScoped<ShipmentLoadsMarkDischargedService>();
        services.AddScoped<ShipmentLoadsUndoDischargedService>();
```

- [ ] **Step 4: Rodar os testes e o build**

Run: `dotnet build SiagroB1.sln -v q && dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadEdmModelTests`
Expected: build sem erro; PASS.

- [ ] **Step 5: Suíte inteira e commit**

```bash
git add SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsMarkDischargedController.cs SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsUndoDischargedController.cs
dotnet test SiagroB1.sln -v q 2>&1 | tail -3
git add -A SiagroB1.Web SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadEdmModelTests.cs
git commit -F - <<'EOF'
feat(shipment): expor Marcar e Desfazer Descarregada como actions OData

Refs: GAC-1171
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM
EOF
```

---

### Task 5: Gancho da Conferência de Entregas

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsUpdateService.cs` (construtor e bloco `if (deliveryChanged)` das linhas ~79-93)
- Modify: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesItemsUpdateServiceTests.cs:22-29` (fábrica `Service()`)
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesItemsUpdateLoadClosureTests.cs`

**Interfaces:**
- Consumes: `ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(AppDbContext, Guid, ICollection<Guid>?)`,
  `SalesInvoiceOriginResolver.ResolveShipmentLoadKeyAsync(AppDbContext, SalesInvoice)`,
  `ShipmentLoadsChangeLogService`.
- Produces: o construtor novo,
  `SalesInvoicesItemsUpdateService(IUnitOfWork db, IItemService itemService, ShipmentLoadsChangeLogService loadChangeLog, ILogger<SalesInvoicesUpdateService> logger)`.
  O DI resolve sozinho, porque `ShipmentLoadsChangeLogService` já é scoped.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// GAC-1171 (melhorias): encerrar ou estornar a entrega na Conferência
/// (/sales-invoices/reconciliation e /open-reconciliation) recalcula a situação da carga.
/// </summary>
/// <remarks>
/// Mesmo formato do PATCH real: o controller carrega o item RASTREADO e muta a mesma instância,
/// ver <see cref="SalesInvoicesItemsUpdateServiceTests"/>.
/// </remarks>
public class SalesInvoicesItemsUpdateLoadClosureTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesInvoicesItemsUpdateService Service() => new(
        _db,
        new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
        new ShipmentLoadsChangeLogService(_db.Context),
        new TestLogger<SalesInvoicesUpdateService>());

    /// <summary>Carga Faturada de 100 t com uma nota confirmada de dois itens de 50 t.</summary>
    private async Task<(ShipmentLoad Load, SalesInvoiceItem First, SalesInvoiceItem Second)> SeedAsync(
        SalesInvoiceDeliveryStatus first,
        SalesInvoiceDeliveryStatus second,
        ShipmentLoadStatus loadStatus = ShipmentLoadStatus.Invoiced,
        bool isDischarged = false,
        bool withLoad = true)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000051",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = 100_000,
            InvoicedQuantity = 100_000,
            Status = loadStatus,
            IsDischarged = isDischarged,
        };
        _db.Context.ShipmentLoads.Add(load);

        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C001",
            InvoiceNumber = "000000888",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
            ShipmentLoadKey = withLoad ? load.Key : null,
        };

        SalesInvoiceItem Item(SalesInvoiceDeliveryStatus delivery) => new()
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 50_000,
            DeliveredQuantity = delivery == SalesInvoiceDeliveryStatus.Closed ? 50_000 : 0m,
            DeliveryStatus = delivery,
        };

        var a = Item(first);
        var b = Item(second);
        invoice.Items.Add(a);
        invoice.Items.Add(b);
        _db.Context.SalesInvoices.Add(invoice);

        await _db.Context.SaveChangesAsync();
        return (load, a, b);
    }

    private async Task CloseAsync(SalesInvoiceItem item)
    {
        var tracked = await _db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == item.Key);
        tracked.DeliveredQuantity = 50_000;
        tracked.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;
        await Service().ExecuteAsync(item.Key!.Value, tracked, "conferente");
    }

    private async Task ReopenAsync(SalesInvoiceItem item)
    {
        var tracked = await _db.Context.SalesInvoicesItems.SingleAsync(x => x.Key == item.Key);
        tracked.DeliveryStatus = SalesInvoiceDeliveryStatus.Open;
        await Service().ExecuteAsync(item.Key!.Value, tracked, "conferente");
    }

    private Task<ShipmentLoad> LoadAsync() => _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();

    [Fact]
    public async Task Closing_the_last_open_delivery_completes_the_load_and_logs_who_did_it()
    {
        var (_, _, second) = await SeedAsync(SalesInvoiceDeliveryStatus.Closed, SalesInvoiceDeliveryStatus.Open);

        await CloseAsync(second);

        Assert.Equal(ShipmentLoadStatus.Completed, (await LoadAsync()).Status);

        var log = await _db.Context.ShipmentLoadsChangeLogs.SingleAsync();
        Assert.Equal(ShipmentLoadChangeLogFields.Status, log.Field);
        Assert.Equal("Faturada", log.OldValue);
        Assert.Equal("Concluída", log.NewValue);
        Assert.Equal("conferente", log.ChangedBy);
    }

    [Fact]
    public async Task Closing_one_of_two_deliveries_keeps_the_load_invoiced_without_log()
    {
        var (_, first, _) = await SeedAsync(SalesInvoiceDeliveryStatus.Open, SalesInvoiceDeliveryStatus.Open);

        await CloseAsync(first);

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync()).Status);
        Assert.Empty(_db.Context.ShipmentLoadsChangeLogs);
    }

    [Fact]
    public async Task Reopening_a_delivery_of_a_completed_load_returns_it_to_invoiced()
    {
        var (_, first, _) = await SeedAsync(
            SalesInvoiceDeliveryStatus.Closed, SalesInvoiceDeliveryStatus.Closed, ShipmentLoadStatus.Completed);

        await ReopenAsync(first);

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync()).Status);
        Assert.Equal("Concluída", (await _db.Context.ShipmentLoadsChangeLogs.SingleAsync()).OldValue);
    }

    [Fact]
    public async Task Reopening_a_delivery_of_a_completed_and_marked_load_returns_it_to_discharged()
    {
        var (_, first, _) = await SeedAsync(
            SalesInvoiceDeliveryStatus.Closed, SalesInvoiceDeliveryStatus.Closed,
            ShipmentLoadStatus.Completed, isDischarged: true);

        await ReopenAsync(first);

        Assert.Equal(ShipmentLoadStatus.Discharged, (await LoadAsync()).Status);
    }

    [Fact]
    public async Task An_invoice_outside_any_load_leaves_loads_alone()
    {
        var (_, _, second) = await SeedAsync(
            SalesInvoiceDeliveryStatus.Closed, SalesInvoiceDeliveryStatus.Open, withLoad: false);

        await CloseAsync(second);

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync()).Status);
        Assert.Empty(_db.Context.ShipmentLoadsChangeLogs);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~SalesInvoicesItemsUpdateLoadClosureTests`
Expected: FAIL de compilação: o construtor não aceita quatro argumentos.

- [ ] **Step 3: Implementar**

`SalesInvoicesItemsUpdateService.cs`: acrescente `using SiagroB1.Application.Services.ShipmentLoads;`
e troque o construtor:

```csharp
public class SalesInvoicesItemsUpdateService(
    IUnitOfWork db, 
    IItemService itemService,
    ShipmentLoadsChangeLogService loadChangeLog,
    ILogger<SalesInvoicesUpdateService> logger)
```

Dentro de `if (deliveryChanged)`, **depois** do último
`await SalesShipmentReleasesRecalculateShippedService.RecalculateForItemsAsync(...); await db.SaveChangesAsync();`:

```csharp

                // GAC-1171 (melhorias): encerrar ou estornar a entrega muda a situação da carga
                // (Faturada/Descarregada ↔ Concluída). Depois dos flushes acima, embora a regra
                // já leia o estado rastreado: manter o gancho no fim deixa a ordem igual à dos
                // outros recálculos.
                await RecalculateShipmentLoadAsync(existingEntity, userName);
                await db.SaveChangesAsync();
```

E o método privado, depois de `ExecuteAsync`:

```csharp
    /// <summary>
    /// Recalcula a carga da nota do item e, se a situação mudou, registra no log da carga quem
    /// causou a mudança: a transição Concluída vem de um ato do conferente, e sem o log a carga
    /// "se concluiria sozinha" sem rastro. No-op para nota sem carga (legada ou avulsa), como o
    /// <c>ShipmentLoadsBalanceHookService</c>.
    /// </summary>
    private async Task RecalculateShipmentLoadAsync(SalesInvoiceItem item, string userName)
    {
        if (item.SalesInvoiceKey is not { } invoiceKey)
            return;

        var invoice = await db.Context.SalesInvoices.FirstOrDefaultAsync(x => x.Key == invoiceKey);
        if (invoice is null)
            return;

        var loadKey = await SalesInvoiceOriginResolver.ResolveShipmentLoadKeyAsync(db.Context, invoice);
        if (loadKey is null)
            return;

        var load = await db.Context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == loadKey.Value);
        if (load is null)
            return;

        var before = load.Status;

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
            db.Context, load.Key, excludedInvoiceKeys: null);

        if (load.Status == before)
            return;

        load.UpdatedBy = userName;

        loadChangeLog.Register(
            load.Key,
            ShipmentLoadChangeLogFields.Status,
            ShipmentLoadChangeLogFields.DescribeStatus(before),
            ShipmentLoadChangeLogFields.DescribeStatus(load.Status),
            userName);
    }
```

`SalesInvoicesItemsUpdateServiceTests.cs`: acrescente `using SiagroB1.Application.Services.ShipmentLoads;`
e, em `Service()`, o argumento `new ShipmentLoadsChangeLogService(_db.Context),` entre o
`FakeItemService` e o `TestLogger`.

- [ ] **Step 4: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesItemsUpdate"`
Expected: PASS nos dois arquivos.

- [ ] **Step 5: Suíte inteira e commit**

```bash
git add SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesItemsUpdateLoadClosureTests.cs
dotnet build SiagroB1.sln -v q && dotnet test SiagroB1.sln --no-build -v q 2>&1 | tail -3
git add -A SiagroB1.Application SiagroB1.Application.Tests
git commit -F - <<'EOF'
feat(invoice): recalcular a situação da carga na conferência de entregas

Encerrar o último item leva a carga a Concluída, e estornar volta a
Descarregada ou Faturada. A mudança fica no log da carga com o nome do
conferente, porque é o ato dele que a causa.

Refs: GAC-1171
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM
EOF
```

---

### Task 6: Os outros escritores de `DeliveryStatus`

O spec (§2.7) pede prova, caminho a caminho, de que quem muda a entrega de um item de nota de
carga recalcula a carga. A análise para quem executa:

- **Cancelar / Excluir uma devolução** (`SalesInvoicesCancelService`, `SalesInvoicesDeleteService`):
  eles chamam `SalesInvoicesReturnOriginRestoreService`, que reabre a origem (Confirmed + itens
  Open) **sem salvar**, e depois o `loadHook.ApplyAsync`. É o caso do Review Focus 1, e a Task 2 já
  garante a leitura rastreada. Aqui o teste prova de ponta a ponta.
- **Estornar a confirmação** (`SalesInvoicesReverseConfirmService`): chama o `loadHook`. Uma nota
  normal que volta a Pendente impede a Concluída (Task 2).
- **Criar a devolução** (`SalesInvoicesReturnService`): fecha os itens da origem só no retorno
  TOTAL, e a confirmação embutida já tirou a carga de Faturada (o volume devolvido sai do
  faturado). A Concluída não se aplica, e nenhum gancho é acrescentado. O teste abaixo trava essa
  conclusão.

**Files:**
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadClosureInvoiceWritersTests.cs`
- Modify (só se algum teste falhar): o serviço do caminho que falhou, acrescentando o recálculo da
  carga no molde da Task 5.

**Interfaces:**
- Consumes: as fábricas de serviço copiadas de
  `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesReturnOriginStateTests.cs:31-62` e o
  `SalesContractsAllocationTestSupport.NewInvoice/NewItem`.

- [ ] **Step 1: Escrever os testes**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 (melhorias): os caminhos do documento de saída que mudam a entrega de um item também
/// precisam desfazer uma Concluída. O caso perigoso é o cancelamento ou a exclusão de uma
/// devolução, que reabre a origem sem salvar antes do hook da carga.
/// </summary>
public class ShipmentLoadClosureInvoiceWritersTests
{
    private static SalesInvoicesReverseConfirmService Reverse(UnitOfWork db) =>
        new(db,
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new FakeStringLocalizer<Resource>());

    private static SalesInvoicesCancelService Cancel(UnitOfWork db) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            NullLogger<SalesInvoicesCancelService>.Instance);

    private static SalesInvoicesDeleteService Delete(UnitOfWork db) =>
        new(db,
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            NullLogger<SalesInvoicesDeleteService>.Instance);

    /// <summary>
    /// Carga de 200 t "Concluída" no banco: a nota C (100 t, confirmada, entregue) e a nota A
    /// (100 t) já retornada, com a entrega fechada pelo retorno e uma devolução PENDENTE sobre
    /// ela. Uma devolução pendente não abate o faturado, então a carga segue Faturada nos números.
    /// </summary>
    private static async Task<(ShipmentLoad Load, SalesInvoice Return)> SeedPendingReturnAsync(UnitOfWork db)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000061",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = 200m,
            InvoicedQuantity = 200m,
            Status = ShipmentLoadStatus.Completed,
        };
        db.Context.ShipmentLoads.Add(load);

        var origin = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Returned);
        origin.ShipmentLoadKey = load.Key;
        origin.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;
        var originItem = SalesContractsAllocationTestSupport.NewItem(origin, contractKey: null, releaseKey: null, quantity: 100m);
        originItem.DeliveredQuantity = 100m;
        originItem.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

        var other = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Confirmed);
        other.ShipmentLoadKey = load.Key;
        var otherItem = SalesContractsAllocationTestSupport.NewItem(other, contractKey: null, releaseKey: null, quantity: 100m);
        otherItem.DeliveredQuantity = 100m;
        otherItem.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

        var returnInvoice = SalesContractsAllocationTestSupport.NewInvoice(
            InvoiceStatus.Pending, SalesInvoiceType.Return, originKey: origin.Key);
        SalesContractsAllocationTestSupport.NewItem(
            returnInvoice, contractKey: null, releaseKey: null, quantity: 100m, originItemKey: originItem.Key);
        SalesInvoicesReturnWeightService.Apply(returnInvoice);

        db.Context.SalesInvoices.AddRange(origin, other, returnInvoice);
        await db.SaveChangesAsync();

        return (load, returnInvoice);
    }

    private static Task<ShipmentLoad> LoadAsync(UnitOfWork db) =>
        db.Context.ShipmentLoads.AsNoTracking().SingleAsync();

    [Fact]
    public async Task Cancelling_a_pending_return_reopens_the_origin_and_undoes_the_completion()
    {
        var db = TestDb.CreateUnitOfWork();
        var (_, returnInvoice) = await SeedPendingReturnAsync(db);

        await Cancel(db).ExecuteAsync(returnInvoice.Key, "tester");

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync(db)).Status);
    }

    [Fact]
    public async Task Deleting_a_pending_return_reopens_the_origin_and_undoes_the_completion()
    {
        var db = TestDb.CreateUnitOfWork();
        var (_, returnInvoice) = await SeedPendingReturnAsync(db);

        await Delete(db).ExecuteAsync(returnInvoice.Key, "tester");

        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync(db)).Status);
    }
}
```

- [ ] **Step 2: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadClosureInvoiceWritersTests`
Expected: PASS nos dois. A Task 2 já tornou a leitura rastreada, e este é o teste de ponta a ponta
dela.

- Se falhar com a carga em **Completed**: o hook rodou antes do `RestoreService`, ou leu o banco.
  Leia `SalesInvoicesCancelService.cs:75-100` / `SalesInvoicesDeleteService.cs:35-80`, confirme a
  ordem restore → hook e corrija a causa (não o teste).
- Se falhar por **exceção de seed** (o serviço exige dado que o seed não tem, como filial ou
  romaneio): compare com o `SeedAsync` de `SalesInvoicesReturnOriginStateTests.cs:69-100`, que
  passa por esses mesmos serviços, e complete o seed com o que falta.

- [ ] **Step 3: Acrescentar o teste do estorno de confirmação**

Na mesma classe:

```csharp
    /// <summary>
    /// Estornar a confirmação devolve a nota a Pendente, e uma nota Pendente ainda não entrou
    /// na Conferência: a carga deixa de ser Concluída.
    /// </summary>
    [Fact]
    public async Task Reversing_the_confirmation_of_a_load_invoice_undoes_the_completion()
    {
        var db = TestDb.CreateUnitOfWork();
        var (_, _) = await SeedPendingReturnAsync(db);
        var other = await db.Context.SalesInvoices
            .SingleAsync(i => i.InvoiceType == SalesInvoiceType.Normal && i.InvoiceStatus == InvoiceStatus.Confirmed);

        await Reverse(db).ExecuteAsync(other.Key, "tester");

        Assert.NotEqual(ShipmentLoadStatus.Completed, (await LoadAsync(db)).Status);
    }
```

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadClosureInvoiceWritersTests`
Expected: PASS. Se o estorno de uma nota de carga exigir romaneios para achar o ramo, monte-os como
`SalesInvoicesReverseConfirmLoadReturnTests.cs` monta (o arquivo de teste que já cobre o estorno
de nota de carga) e explique no comentário do teste o que foi preciso.

- [ ] **Step 4: Suíte inteira e commit**

```bash
git add SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadClosureInvoiceWritersTests.cs
dotnet test SiagroB1.sln -v q 2>&1 | tail -3
git commit -F - <<'EOF'
test(shipment): provar que cancelar, excluir e estornar notas desfazem a Concluída

O cancelamento e a exclusão de uma devolução reabrem a nota de origem sem
salvar antes do recálculo da carga. Os testes travam que a carga não
fica concluída com um item reaberto.

Refs: GAC-1171
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM
EOF
```

Se o Step 2 exigiu mudança num serviço, o tipo do commit é `fix(invoice)` e o corpo diz qual
caminho não recalculava.

---

### Task 7: Varredura das travas (Descarregada = Faturada; Concluída Normal = Completed)

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadCompletionRules.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsReopenService.cs:28-30`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRefuseService.cs:411-418` (`Validate`)
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsUpdateService.cs:54-55`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsAttachTransactionsService.cs:138-139`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsDetachTransactionsService.cs:48-50`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsCancelService.cs:48-50`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadTransshipmentRules.cs:61-69`
- Modify: `SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsChangeReleaseService.cs:204-212`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadClosureGuardsTests.cs`

**Interfaces:**
- Produces: `ShipmentLoadCompletionRules.UndoHint(ShipmentLoad load)`. Devolve `"Reabra-a"` para
  Remoção e `"Estorne a conferência de entrega"` para Normal.

A decisão marcada no spec (§2.8, ⚠️) fica **como escrita**: a Concluída Normal **bloqueia** a Troca
de Liberação. O usuário aprovou o spec sem mudá-la.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 (melhorias): Descarregada se comporta como Faturada em toda trava, e a Concluída da
/// carga Normal como o Completed que já existia para a Remoção, com a mensagem certa para cada tipo.
/// </summary>
public class ShipmentLoadClosureGuardsTests
{
    private static ShipmentLoad Load(ShipmentLoadStatus status, ShipmentLoadType type = ShipmentLoadType.Normal) => new()
    {
        Key = Guid.NewGuid(),
        Code = "CG000071",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        LoadType = type,
        Status = status,
        TotalQuantity = 90_000,
        InvoicedQuantity = 90_000,
    };

    [Fact]
    public void The_undo_hint_depends_on_the_load_type()
    {
        Assert.Equal("Reabra-a", ShipmentLoadCompletionRules.UndoHint(Load(ShipmentLoadStatus.Completed, ShipmentLoadType.Removal)));
        Assert.Equal("Estorne a conferência de entrega", ShipmentLoadCompletionRules.UndoHint(Load(ShipmentLoadStatus.Completed)));
    }

    [Fact]
    public async Task Reopening_a_completed_normal_load_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = Load(ShipmentLoadStatus.Completed);
        db.Context.ShipmentLoads.Add(load);
        await db.Context.SaveChangesAsync();

        var service = new ShipmentLoadsReopenService(
            db, new ShipmentLoadsMovementLogService(db.Context), new ShipmentLoadsChangeLogService(db.Context));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(load.Key, "tester"));

        Assert.Equal(
            "A carga CG000071 é concluída pela conferência de entrega: estorne a conferência para reabri-la.",
            ex.Message);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Discharged)]
    [InlineData(ShipmentLoadStatus.Completed)]
    public void Transshipment_is_refused_on_a_discharged_or_completed_load(ShipmentLoadStatus status)
    {
        var ex = Assert.Throws<ApplicationException>(
            () => ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment(Load(status)));

        Assert.Contains("encerrada", ex.Message);
    }
}
```

Depois disso, acrescente um teste por serviço de trava, **cada um no arquivo de teste que já
existe para aquele serviço** e usando o fixture dele:

| Arquivo de teste | Teste novo | Expectativa |
|---|---|---|
| `ShipmentLoadsRefuseServiceTests.cs` | `Refusing_a_discharged_load_is_refused` + `Refusing_a_completed_normal_load_is_refused` | `ApplicationException` com a mensagem exata `"A carga {Code} já foi descarregada no destino. Desfaça a descarga antes de registrar recusa."` |
| `ShipmentLoadsUpdateServiceTests.cs` | `Fiscal_fields_are_locked_on_a_discharged_load` | mesma exceção que o teste existente de campos fiscais em `Invoiced` espera. Duplique aquele teste trocando o status para `Discharged`, e depois para `Completed` num segundo teste |
| `ShipmentLoadsAttachTransactionsServiceTests.cs` | `Attaching_to_a_completed_normal_load_asks_to_reverse_the_reconciliation` | a mensagem contém `"já foi concluída — estorne a conferência de entrega antes de alterar a composição"` (minúscula: está no meio da frase, como o `"reabra-a"` atual da Remoção) |
| `ShipmentLoadsDetachTransactionsServiceTests.cs` | `Detaching_from_a_completed_normal_load_asks_to_reverse_the_reconciliation` | `"A carga {Code} já foi concluída. Estorne a conferência de entrega antes de alterar a composição."` |
| `ShipmentLoadsCancelServiceTests.cs` | `Cancelling_a_completed_normal_load_asks_to_reverse_the_reconciliation` | `"A carga {Code} já foi concluída. Estorne a conferência de entrega antes de cancelá-la."` |

Para cada um: localize no arquivo o teste existente mais próximo (o que usa `Completed` ou
`Invoiced`), copie o fixture dele e troque só o status/tipo. Os testes existentes da Remoção
**continuam** esperando `"Reabra-a"`, e isso prova que a mensagem antiga não mudou para ela.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentLoadClosureGuardsTests|FullyQualifiedName~ShipmentLoadsRefuseServiceTests|FullyQualifiedName~ShipmentLoadsUpdateServiceTests|FullyQualifiedName~ShipmentLoadsAttachTransactionsServiceTests|FullyQualifiedName~ShipmentLoadsDetachTransactionsServiceTests|FullyQualifiedName~ShipmentLoadsCancelServiceTests"`
Expected: FAIL de compilação (`ShipmentLoadCompletionRules` não existe). Depois de criá-la, falham
os testes novos: Reopen aceita Normal, Refuse aceita Descarregada, transbordo aceita Descarregada, e
as mensagens dizem "Reabra-a".

- [ ] **Step 3: Implementar**

`ShipmentLoadCompletionRules.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Como se desfaz uma carga <see cref="ShipmentLoadStatus.Completed"/>, dito ao usuário nas
/// travas que a recusam (GAC-1171, melhorias).
/// </summary>
/// <remarks>
/// A mesma situação tem dois caminhos de volta. A Remoção é concluída à mão e se desfaz pelo
/// "Reabrir". A carga Normal é concluída pela Conferência de Entregas, e "Reabrir" nela seria
/// desfeito na hora pelo recálculo. Mandar a pessoa ao botão errado era o que as mensagens
/// antigas fariam na carga Normal.
/// </remarks>
public static class ShipmentLoadCompletionRules
{
    public static string UndoHint(ShipmentLoad load) =>
        load.LoadType == ShipmentLoadType.Removal
            ? "Reabra-a"
            : "Estorne a conferência de entrega";
}
```

`ShipmentLoadsReopenService.cs`, antes de `if (load.Status != ShipmentLoadStatus.Completed)`:

```csharp
        // GAC-1171 (melhorias): a carga Normal também chega a Completed, mas pela Conferência de
        // Entregas. Reabri-la aqui seria desfeito na hora pelo recálculo, que a concluiria de novo.
        if (load.LoadType != ShipmentLoadType.Removal)
            throw new ApplicationException(
                $"A carga {load.Code} é concluída pela conferência de entrega: " +
                "estorne a conferência para reabri-la.");
```

`ShipmentLoadsRefuseService.Validate`, depois da trava de `Planned`:

```csharp
        // GAC-1171 (melhorias): a carga descarregada ou concluída foi ACEITA no destino. Uma
        // recusa por cima contradiria a marca, então o caminho é desfazer a descarga primeiro.
        if (load.Status is ShipmentLoadStatus.Discharged
            || (load.Status == ShipmentLoadStatus.Completed && load.LoadType == ShipmentLoadType.Normal))
            throw new ApplicationException(
                $"A carga {load.Code} já foi descarregada no destino. " +
                "Desfaça a descarga antes de registrar recusa.");
```

`ShipmentLoadsUpdateService.cs:54-55`:

```csharp
        // Descarregada e Concluída são estados POSTERIORES a Faturada (GAC-1171): os campos
        // fiscais seguem travados.
        var fiscalFieldsLocked = load.Status is ShipmentLoadStatus.PartiallyInvoiced
            or ShipmentLoadStatus.Invoiced or ShipmentLoadStatus.Discharged
            or ShipmentLoadStatus.Completed;
```

`ShipmentLoadsAttachTransactionsService.EnsureLoadAcceptsShipments`, no switch. A frase atual da
Remoção é `"já foi concluída — reabra-a antes de alterar a composição"`, com a dica no meio da frase
e em minúscula, e o `ToLowerInvariant()` preserva isso:

```csharp
            ShipmentLoadStatus.Completed =>
                $"já foi concluída — {ShipmentLoadCompletionRules.UndoHint(load).ToLowerInvariant()} " +
                "antes de alterar a composição",
```

`ShipmentLoadsDetachTransactionsService.cs:48-50`:

```csharp
        if (load.Status == ShipmentLoadStatus.Completed)
            throw new ApplicationException(
                $"A carga {load.Code} já foi concluída. " +
                $"{ShipmentLoadCompletionRules.UndoHint(load)} antes de alterar a composição.");
```

`ShipmentLoadsCancelService.cs:48-50`:

```csharp
        if (load.Status == ShipmentLoadStatus.Completed)
            throw new ApplicationException(
                $"A carga {load.Code} já foi concluída. " +
                $"{ShipmentLoadCompletionRules.UndoHint(load)} antes de cancelá-la.");
```

`ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment`: troque o comentário "Completed é
defensivo e hoje inalcançável…" por "Completed e Discharged: a carga Normal chega a eles pelo
GAC-1171 (melhorias). A mercadoria já foi entregue no destino, e não há o que transbordar." Na
condição, acrescente `or ShipmentLoadStatus.Discharged`.

`ShippingTransactionsChangeReleaseService.cs:204-212`: troque o comentário "Completed entra na
lista por defesa…" por "Completed (Remoção, ou Normal com a conferência encerrada) e Discharged
(GAC-1171): a composição de uma carga entregue não muda." Na condição, acrescente
`or ShipmentLoadStatus.Discharged`.

- [ ] **Step 4: Rodar os testes**

Run: o mesmo filtro do Step 2.
Expected: PASS, incluindo os testes antigos da Remoção com "Reabra-a" / "reabra-a".

- [ ] **Step 5: Suíte inteira e commit**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadCompletionRules.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadClosureGuardsTests.cs
dotnet test SiagroB1.sln -v q 2>&1 | tail -3
git add -A SiagroB1.Application SiagroB1.Application.Tests
git commit -F - <<'EOF'
feat(shipment): estender as travas da carga para Descarregada e Concluída

Descarregada trava tudo o que Faturada trava, e a Concluída da carga
Normal herda as travas do Completed da Remoção. As mensagens passam a
indicar o caminho de volta certo: estornar a conferência na carga
Normal, reabrir na Remoção. Reabrir fica restrito à Remoção porque, na
Normal, o recálculo concluiria a carga de novo.

Refs: GAC-1171
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM
EOF
```

---

### Task 8: Reproduzir o defeito do item 3 (anexo manual não abre)

**Use `superpowers:systematic-debugging`.** Esta task não escreve código de produção: ela produz a
causa raiz, e ela decide o que a Task 11 corrige além do visualizador.

**Files:** nenhum.

- [ ] **Step 1: Subir a stack pelo WORKTREE**

Em **PowerShell**, a partir de `siagro-b1-backend-gac-1171`, mostrando só `Server=` e `Database=`
(nunca a string inteira, que tem credencial):

```powershell
$cs = (Get-Content SiagroB1.Web/appsettings.Yokotobi-Development.json -Raw | ConvertFrom-Json).ConnectionStrings.SiagroDB
($cs -split ';' | Where-Object { $_ -match '^(Server|Data Source|Database|Initial Catalog)=' }) -join '; '
```

Expected: `Server=localhost` e `Database=IDX_SIAGRO_DEV`. Com qualquer outro banco, **pare**.

```powershell
$env:ASPNETCORE_ENVIRONMENT="Yokotobi-Development"; dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet run --project SiagroB1.Web --launch-profile yktb      # background → localhost:50000
dotnet run --project SiagroB1.Gateway --launch-profile yktb  # background → localhost:5246
cd ../siagro-b1-frontend-gac-1171 && yarn start:dev          # background → localhost:8080
```

- [ ] **Step 2: Reproduzir (Chrome, admin/1234)**

1. Abra uma carga (Expedição → Cargas → Detalhe). Na seção **Anexos**, clique em **Anexar**, com
   tipo "Outro", descrição "teste manual" e um PDF pequeno.
2. Tente abrir o anexo de todas as formas: clique na linha, duplo clique, selecione e **Baixar**.
3. Com o DevTools aberto (Network + Console), registre para cada tentativa: houve requisição a
   `ShipmentLoadsAttachmentsDownload(Key=...)`? Qual o status? Qual o `Content-Type` e o
   `Content-Disposition`? Qual o tamanho? Houve erro no console?
4. Repita com um anexo criado pelo **Registrar Descarga** (com arquivo) e abra pelo clip. Compare
   os mesmos quatro dados.

- [ ] **Step 3: Registrar a causa**

Escreva no relatório da task uma das conclusões, com a evidência:

- **(a) Affordance:** Baixar com a linha selecionada funciona (200, arquivo íntegro). O usuário não
  tinha como "abrir" pela linha. A Task 11 resolve com o Link na coluna Arquivo, sem mais correção.
- **(b) Falha real:** descreva o que falhou (status, corpo, bytes corrompidos, chave `undefined`,
  popup bloqueado…). A Task 11 ganha um Step de correção **com teste que falha antes**. Acrescente
  ao spec, na §4.3, uma nota "Causa encontrada em 23/09" e commite o spec junto com a correção.

Não mexa em código nesta task. Deixe a stack de pé para a Task 11, ou derrube-a (porta 50000, 5246
e 8080, por PID) se a próxima task for demorar.

---

### Task 9: Status Descarregada/Concluída na tela da carga

**Files:**
- Modify: `webapp/model/formatter.ts` (`formatShipmentLoadStatus`, `stateShipmentLoadStatus`, `formatShipmentLoadMovementType`)
- Modify: `webapp/controller/shipmentLoads/Main.controller.ts:22-32` (`SHIPMENT_LOAD_STATUSES`)
- Modify: `webapp/controller/storageTransactions/sales/Main.controller.ts:25-35` e `:230`
- Modify: `webapp/controller/shipmentLoads/Panel.controller.ts:13-23` + `webapp/view/shipmentLoads/Panel.view.xml` (depois do Panel "Faturada", ~linha 112)
- Modify: `webapp/view/shipmentLoads/Detail.view.xml` (ações do cabeçalho, ~linha 52; Trocar Liberação, ~linha 229)
- Modify: `webapp/controller/shipmentLoads/Detail.controller.ts` (depois de `onReopenLoad`, ~linha 152)
- Modify: `webapp/view/shipmentLoads/fragments/ShipmentLoadTransshipments.fragment.xml:61`

**Interfaces:**
- Consumes: as actions `/ShipmentLoadsMarkDischarged(...)` e `/ShipmentLoadsUndoDischarged(...)`
  com o parâmetro `Key` (Task 4); o `invokeLoadAction(actionPath, successMessage)` que já existe
  em `Detail.controller.ts:155`.

- [ ] **Step 1: Rótulos (`formatter.ts`)**

Em `formatShipmentLoadStatus`, depois de `m.set("InTransshipment", "Em Transbordo");`:

```ts
    // GAC-1171 (melhorias): faturada e marcada à mão como descarregada no destino.
    m.set("Discharged", "Descarregada");
```

Em `stateShipmentLoadStatus`, depois de `m.set("InTransshipment", "Warning");`:

```ts
    // GAC-1171 (melhorias): Information, e não Success, para não se confundir com Faturada na
    // lista. A Concluída (Success) é o fecho, e a Descarregada é o passo intermediário.
    m.set("Discharged", "Information");
```

Em `formatShipmentLoadMovementType`, depois da última linha do mapa:

```ts
    m.set("Discharged", "Carga Descarregada");
    m.set("DischargeUndone", "Descarregada Desfeita");
```

Confira se `TransshipmentLotExitAttached` está no mapa. Se não estiver, **não** acrescente: é outro
chamado. Anote no relatório.

- [ ] **Step 2: Filtros e aviso**

Nos dois `SHIPMENT_LOAD_STATUSES` (`shipmentLoads/Main.controller.ts` e
`storageTransactions/sales/Main.controller.ts`), depois de `{ key: "Invoiced", text: "Faturada" },`:

```ts
  // GAC-1171 (melhorias): faturada e marcada à mão como descarregada no destino.
  { key: "Discharged", text: "Descarregada" },
```

`storageTransactions/sales/Main.controller.ts:230`: troque a condição por

```ts
    // Descarregada e Concluída vêm depois de Faturada (GAC-1171): o mesmo aviso vale.
    if (["PartiallyInvoiced", "Invoiced", "Discharged", "Completed"].includes(shipment.LoadStatus)) {
```

- [ ] **Step 3: Painel**

`Panel.controller.ts` `LANES`, depois de `listInvoiced`:

```ts
  // GAC-1171 (melhorias): entre Faturada e Devolvida, a carga marcada como descarregada no destino.
  { id: "listDischarged", status: "Discharged", count: "countDischarged" },
```

`Panel.view.xml`, logo depois do `</Panel>` da raia "Faturada":

```xml

				<!-- GAC-1171 (melhorias): carga faturada e marcada como descarregada no destino.
				     A Concluída da carga Normal cai na raia Concluída, junto da Remoção. -->
				<Panel headerText="Descarregada ({panel>/countDischarged})" expandable="false">
					<List
						id="listDischarged"
						updateFinished=".onLaneUpdated"
						noDataText="Nenhuma carga descarregada."
						items="{ path: '/ShipmentLoads', parameters: { $orderby: 'Code' } }">
						<CustomListItem type="Navigation" press=".onOpenLoad">
							<core:Fragment fragmentName="siagrob1.view.shipmentLoads.fragments.LoadCard" type="XML"/>
						</CustomListItem>
					</List>
				</Panel>
```

Atualize o comentário da raia Concluída ("raia da carga de REMOÇÃO encerrada") para: "Raia das
cargas concluídas: a Remoção encerrada à mão e a Normal com a conferência de entrega encerrada
(GAC-1171)."

- [ ] **Step 4: Botões do Detalhe**

`Detail.view.xml`, logo depois do `<Button text="Reabrir Carga" .../>`:

```xml
					<!-- GAC-1171 (melhorias): marca MANUAL de descarga no destino, com o desfazer ao
					     lado. O status resultante sai do recálculo no servidor: com a conferência
					     de entrega toda encerrada, marcar leva direto a Concluída. -->
					<Button
						text="Marcar como Descarregada"
						type="Transparent"
						press=".onMarkDischarged"
						visible="{= ${path: 'LoadType', targetType: 'any'} !== 'Removal' &amp;&amp; ${path: 'Status', targetType: 'any'} === 'Invoiced' }"/>
					<Button
						text="Desfazer Descarregada"
						type="Transparent"
						press=".onUndoDischarged"
						visible="{= ${path: 'Status', targetType: 'any'} === 'Discharged' }"/>
```

No botão "Trocar Liberação" (~linha 229), o `visible` passa a ser:

```xml
											visible="{= ${path: 'LoadType', targetType: 'any'} !== 'Removal' &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Cancelled' &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Returned' &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Discharged' &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Completed' }"
```

`ShipmentLoadTransshipments.fragment.xml:61`: acrescente
`&amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Discharged'` antes do `}"` final.

`Detail.controller.ts`, logo depois de `onReopenLoad`:

```ts
  /** GAC-1171 (melhorias): marca a carga como descarregada no destino. */
  async onMarkDischarged(): Promise<void> {
    if (!await DialogHelper.confirmDialog("Marcar a carga como descarregada ?")) return;

    await this.invokeLoadAction("/ShipmentLoadsMarkDischarged(...)", "Carga marcada como descarregada.");
  }

  /** GAC-1171 (melhorias): desfaz a marca. O status volta ao que o recálculo der. */
  async onUndoDischarged(): Promise<void> {
    if (!await DialogHelper.confirmDialog("Desfazer a marcação de descarregada ?")) return;

    await this.invokeLoadAction("/ShipmentLoadsUndoDischarged(...)", "Marcação de descarregada desfeita.");
  }
```

- [ ] **Step 5: Gates**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-frontend-gac-1171"
yarn ts-typecheck && yarn lint
grep -n -- "--" webapp/view/shipmentLoads/Panel.view.xml webapp/view/shipmentLoads/Detail.view.xml | grep "<!--" -v | grep -v -- "-->" || true
```

Expected: typecheck e lint limpos. O `grep` **não** pode mostrar `--` dentro de comentário.

- [ ] **Step 6: Verificação no navegador (stack da Task 8, com o backend das Tasks 1-7)**

Reinicie o Web e o Gateway pelo worktree, porque as Tasks 1-7 mudaram o backend. Aplique a
migration (`database update`, Task 8 Step 1) se ainda não aplicou. Depois:

1. Carga com status Faturada → **Marcar como Descarregada** → confirma → toast; cabeçalho
   "Descarregada"; log de alterações "Faturada → Descarregada"; histórico "Carga Descarregada".
2. Lista de Cargas: filtro "Descarregada" encontra a carga. No Painel (na data da carga), a raia
   "Descarregada (1)" mostra o cartão.
3. **Desfazer Descarregada** → volta a "Faturada".
4. Na Descarregada: "Registrar Recusa", "Trocar Liberação" e "Iniciar Transbordo" não aparecem.

Anote o código da carga usada, porque a Task 13 continua dela.

- [ ] **Step 7: Commit**

```bash
git branch --show-current
git add -A webapp
git commit -F - <<'EOF'
feat(shipment): marcar e desfazer carga descarregada na tela da carga

A carga Faturada ganha o botão para marcar a descarga no destino, e a
Descarregada ganha o desfazer. O status novo aparece na lista, no
filtro, no Painel e no histórico. Recusa, troca de liberação e transbordo
ficam escondidos como o servidor já recusa.

Refs: GAC-1171
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM
EOF
```

---

### Task 10: Helpers puros do visualizador (+ QUnit)

**Files:**
- Create: `webapp/helpers/AttachmentViewerHelpers.ts`
- Create: `webapp/test/unit/helpers/AttachmentViewerHelpers.qunit.ts`
- Modify: `webapp/test/unit/unitTests.qunit.ts`

**Interfaces:**
- Produces:
  - `export type AttachmentViewerKind = "pdf" | "image" | "text" | "unsupported"`
  - `export function resolveContentType(contentType: string | null | undefined, fileName: string | null | undefined): string`
  - `export function resolveViewerKind(contentType: string): AttachmentViewerKind`
  - `export function parseContentDispositionFileName(header: string | null | undefined): string | null`

- [ ] **Step 1: Escrever os testes que falham**

`webapp/test/unit/helpers/AttachmentViewerHelpers.qunit.ts`:

```ts
import {
	parseContentDispositionFileName,
	resolveContentType,
	resolveViewerKind
} from "siagrob1/helpers/AttachmentViewerHelpers";

QUnit.module("AttachmentViewerHelpers - visualizador de anexos (GAC-1171)");

QUnit.test("o Content-Type informado decide, sem parâmetros e sem caixa", function (assert) {
	assert.strictEqual(resolveContentType("application/PDF; charset=binary", "x.bin"), "application/pdf");
	assert.strictEqual(resolveContentType("image/png", "sem-extensao"), "image/png");
});

QUnit.test("octet-stream ou vazio cai na extensão do arquivo (anexos antigos dos contratos)", function (assert) {
	assert.strictEqual(resolveContentType("application/octet-stream", "Contrato 123.PDF"), "application/pdf");
	assert.strictEqual(resolveContentType("", "foto.jpeg"), "image/jpeg");
	assert.strictEqual(resolveContentType(null, "ticket.jpg"), "image/jpeg");
	assert.strictEqual(resolveContentType(undefined, "nota.txt"), "text/plain");
});

QUnit.test("extensão desconhecida continua octet-stream", function (assert) {
	assert.strictEqual(resolveContentType("application/octet-stream", "planilha.xlsx"), "application/octet-stream");
	assert.strictEqual(resolveContentType("", "anexo"), "application/octet-stream");
});

QUnit.test("PDF, imagem e texto puro são exibíveis", function (assert) {
	assert.strictEqual(resolveViewerKind("application/pdf"), "pdf");
	assert.strictEqual(resolveViewerKind("image/png"), "image");
	assert.strictEqual(resolveViewerKind("image/jpeg"), "image");
	assert.strictEqual(resolveViewerKind("text/plain"), "text");
});

QUnit.test("tipo ativo NÃO é exibido: blob herda a origem da aplicação", function (assert) {
	assert.strictEqual(resolveViewerKind("text/html"), "unsupported");
	assert.strictEqual(resolveViewerKind("image/svg+xml"), "unsupported");
	assert.strictEqual(resolveViewerKind("application/xhtml+xml"), "unsupported");
	assert.strictEqual(resolveViewerKind("text/javascript"), "unsupported");
	assert.strictEqual(resolveViewerKind("application/vnd.openxmlformats-officedocument.wordprocessingml.document"), "unsupported");
});

QUnit.test("nome do arquivo pelo Content-Disposition do ASP.NET", function (assert) {
	assert.strictEqual(
		parseContentDispositionFileName("attachment; filename=ticket.pdf; filename*=UTF-8''Ticket%20descarga%20a%C3%A7%C3%A3o.pdf"),
		"Ticket descarga ação.pdf");
	assert.strictEqual(parseContentDispositionFileName("attachment; filename=\"nota fiscal.pdf\""), "nota fiscal.pdf");
	assert.strictEqual(parseContentDispositionFileName("attachment; filename=nota.pdf"), "nota.pdf");
});

QUnit.test("sem nome utilizável devolve null", function (assert) {
	assert.strictEqual(parseContentDispositionFileName(null), null);
	assert.strictEqual(parseContentDispositionFileName(""), null);
	assert.strictEqual(parseContentDispositionFileName("inline"), null);
});

QUnit.test("filename* malformado cai no filename simples", function (assert) {
	assert.strictEqual(
		parseContentDispositionFileName("attachment; filename=reserva.pdf; filename*=UTF-8''%E0%A4%A"),
		"reserva.pdf");
});
```

Em `webapp/test/unit/unitTests.qunit.ts`, acrescente a última linha:

```ts
import "./helpers/AttachmentViewerHelpers.qunit";
```

- [ ] **Step 2: Rodar e ver falhar**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-frontend-gac-1171"
yarn ts-typecheck
```

Expected: FAIL: `Cannot find module 'siagrob1/helpers/AttachmentViewerHelpers'`.

- [ ] **Step 3: Implementar `webapp/helpers/AttachmentViewerHelpers.ts`**

```ts
/**
 * Regras puras do visualizador de anexos (GAC-1171, melhorias): qual o tipo real do arquivo,
 * se o diálogo pode exibi-lo, e com que nome ele é baixado. Ficam fora do diálogo para serem
 * testadas sem DOM.
 */

export type AttachmentViewerKind = "pdf" | "image" | "text" | "unsupported";

/** Só extensões de tipo EXIBÍVEL: o resto não precisa ser adivinhado, vai para Baixar. */
const EXTENSION_TYPES: Record<string, string> = {
  pdf: "application/pdf",
  png: "image/png",
  jpg: "image/jpeg",
  jpeg: "image/jpeg",
  gif: "image/gif",
  webp: "image/webp",
  bmp: "image/bmp",
  txt: "text/plain",
};

/**
 * Tipo do arquivo: o `Content-Type` da resposta, ou a extensão do nome quando ele vier vazio ou
 * genérico. Anexos antigos de contrato foram gravados como `application/octet-stream`, e sem o
 * fallback nenhum PDF deles abriria.
 */
export function resolveContentType(
  contentType: string | null | undefined,
  fileName: string | null | undefined,
): string {
  const type = (contentType ?? "").split(";")[0].trim().toLowerCase();

  if (type && type !== "application/octet-stream") return type;

  const name = fileName ?? "";
  const dot = name.lastIndexOf(".");
  const extension = dot >= 0 ? name.slice(dot + 1).toLowerCase() : "";

  return EXTENSION_TYPES[extension] ?? "application/octet-stream";
}

/**
 * O que o diálogo faz com o tipo.
 *
 * ⚠️ Lista FECHADA, e de propósito. Um blob URL herda a ORIGEM da aplicação: um HTML ou SVG
 * anexado e aberto no iframe rodaria script com a sessão do usuário logado. Por isso só PDF,
 * imagem raster e `text/plain` são exibidos; o resto, inclusive `text/html` e `image/svg+xml`,
 * cai no aviso com o botão Baixar. `sandbox` no iframe não resolve, porque bloqueia o leitor de
 * PDF do Chrome.
 */
export function resolveViewerKind(contentType: string): AttachmentViewerKind {
  if (contentType === "application/pdf") return "pdf";
  if (contentType === "image/svg+xml") return "unsupported";
  if (contentType.startsWith("image/")) return "image";
  if (contentType === "text/plain") return "text";
  return "unsupported";
}

/**
 * Nome do arquivo no `Content-Disposition` que o `File(bytes, type, name)` do ASP.NET manda.
 * Prefere `filename*` (RFC 5987, UTF-8 percent-encoded), que é o único que preserva acento, e
 * cai no `filename` simples se o estendido vier malformado.
 */
export function parseContentDispositionFileName(header: string | null | undefined): string | null {
  if (!header) return null;

  const extended = /filename\*\s*=\s*utf-8''([^;]+)/i.exec(header);
  if (extended) {
    try {
      return decodeURIComponent(extended[1].trim());
    } catch {
      // percent-encoding inválido: tenta o filename simples abaixo.
    }
  }

  const plain = /filename\s*=\s*(?:"([^"]*)"|([^;]+))/i.exec(header);
  const value = (plain?.[1] ?? plain?.[2] ?? "").trim();

  return value || null;
}
```

- [ ] **Step 4: Rodar os testes**

```bash
yarn ts-typecheck && yarn lint
yarn start          # background, porta 8080 (servidor ui5 puro, sem proxy, suficiente para QUnit)
```

Abra `http://localhost:8080/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests`
pelo navegador (ferramentas do Chrome/Playwright) e leia o resumo. Se o `test-runner` do projeto
aceitar só a unit suite, `npx ui5-test-runner --url "<a mesma URL>"` também serve.

Expected: o módulo "AttachmentViewerHelpers" com 8 testes verdes, e os módulos preexistentes com o
mesmo resultado do baseline. Derrube o servidor da porta 8080 ao terminar.

- [ ] **Step 5: Commit**

```bash
git add webapp/helpers/AttachmentViewerHelpers.ts webapp/test/unit/helpers/AttachmentViewerHelpers.qunit.ts
git add webapp/test/unit/unitTests.qunit.ts
git commit -F - <<'EOF'
feat(platform): regras puras do visualizador de anexos

O tipo sai do Content-Type ou da extensão, porque anexos antigos foram
gravados como octet-stream. Só PDF, imagem raster e texto puro são
exibidos: o blob herda a origem da aplicação, e um HTML ou SVG anexado
rodaria script com a sessão do usuário.

Refs: GAC-1171
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM
EOF
```

---

### Task 11: O diálogo `AttachmentViewer` e os anexos da carga

**Files:**
- Create: `webapp/dialogs/AttachmentViewer.ts`
- Modify: `webapp/view/shipmentLoads/fragments/ShipmentLoadAttachments.fragment.xml` (toolbar e coluna "Arquivo")
- Modify: `webapp/view/shipmentLoads/fragments/ShipmentLoadDischarges.fragment.xml` (coluna "Anexo")
- Modify: `webapp/controller/shipmentLoads/BaseController.ts` (`onDownloadDischargeAttachment` ~427; seção "Anexos" ~480-640)

**Interfaces:**
- Consumes: os helpers da Task 10; `readErrorMessage(response: Response): Promise<string>` de
  `siagrob1/helpers/FetchHelpers`; `ServerRoutes.shipmentLoadsAttachmentsDownload`.
- Produces: `export async function openAttachmentViewer(options: { url: string; fileName?: string; title?: string }): Promise<void>`.

- [ ] **Step 1: Implementar `webapp/dialogs/AttachmentViewer.ts`**

```ts
import Button from "sap/m/Button";
import Dialog from "sap/m/Dialog";
import Image from "sap/m/Image";
import MessageBox from "sap/m/MessageBox";
import MessageStrip from "sap/m/MessageStrip";
import BusyIndicator from "sap/ui/core/BusyIndicator";
import HTML from "sap/ui/core/HTML";
import Control from "sap/ui/core/Control";
import Device from "sap/ui/Device";
import { readErrorMessage } from "siagrob1/helpers/FetchHelpers";
import {
  parseContentDispositionFileName,
  resolveContentType,
  resolveViewerKind,
} from "siagrob1/helpers/AttachmentViewerHelpers";

export type AttachmentViewerOptions = {
  /** Rota que devolve o binário, ex.: `/odata/ShipmentLoadsAttachmentsDownload(Key=...)`. */
  url: string;
  /** Nome para o Baixar e o título. Ausente: lido do Content-Disposition. */
  fileName?: string;
  /** Título do diálogo. Default: o nome do arquivo. */
  title?: string;
};

/**
 * Visualiza um anexo num diálogo, sem baixar (GAC-1171, melhorias). Reaproveitado pela carga e
 * pelos contratos.
 *
 * Montado em código, e não por fragmento: qualquer controller chama sem depender do id da view
 * nem de `addDependent`. Cada abertura cria o seu diálogo e o destrói ao fechar.
 *
 * O arquivo vem por `fetch` → blob → object URL. Um blob não tem `Content-Disposition`, então as
 * rotas de download existentes, que mandam o navegador BAIXAR, servem sem mudança no backend. O
 * Baixar do rodapé reaproveita o mesmo blob, sem refazer a requisição.
 */
export async function openAttachmentViewer(options: AttachmentViewerOptions): Promise<void> {
  BusyIndicator.show(0);

  let response: Response | undefined;
  let blob: Blob | undefined;

  try {
    response = await fetch(options.url);

    if (!response.ok) {
      const message = await readErrorMessage(response);
      MessageBox.error(message || "Não foi possível abrir o anexo.");
      return;
    }

    blob = await response.blob();
  } catch {
    MessageBox.error("Não foi possível abrir o anexo.");
    return;
  } finally {
    BusyIndicator.hide();
  }

  // Inalcançável na prática (os dois caminhos de erro já retornaram), mas o TypeScript não
  // prova atribuição feita dentro de try/catch.
  if (!response || !blob) return;

  const fileName = options.fileName
    || parseContentDispositionFileName(response.headers.get("Content-Disposition"))
    || "anexo";

  const contentType = resolveContentType(blob.type || response.headers.get("Content-Type"), fileName);
  const kind = resolveViewerKind(contentType);

  // O blob é refeito com o tipo resolvido: com octet-stream o iframe BAIXARIA o PDF em vez de
  // exibi-lo, que é justamente o defeito que o diálogo existe para resolver.
  const typedBlob = blob.type === contentType ? blob : new Blob([blob], { type: contentType });
  const objectUrl = URL.createObjectURL(typedBlob);

  const dialog = new Dialog({
    title: options.title ?? fileName,
    contentWidth: "80%",
    contentHeight: "85%",
    resizable: true,
    draggable: true,
    stretch: Device.system.phone,
    horizontalScrolling: false,
    verticalScrolling: kind === "image",
    content: [buildContent(kind, objectUrl, fileName)],
    beginButton: new Button({
      text: "Baixar",
      icon: "sap-icon://download",
      press: () => download(objectUrl, fileName),
    }),
    endButton: new Button({
      text: "Fechar",
      press: () => dialog.close(),
    }),
    afterClose: () => {
      URL.revokeObjectURL(objectUrl);
      dialog.destroy();
    },
  });

  dialog.open();
}

function buildContent(kind: string, objectUrl: string, fileName: string): Control {
  if (kind === "pdf" || kind === "text") {
    // O src é um blob: URL que este módulo criou, nunca texto do usuário, por isso pode entrar
    // no HTML sem escape. O nome do arquivo NÃO entra aqui.
    return new HTML({
      content: `<iframe src="${objectUrl}" title="Visualização do anexo" style="width:100%;height:100%;border:0;display:block"></iframe>`,
      sanitizeContent: false,
      preferDOM: false,
    });
  }

  if (kind === "image") {
    return new Image({
      src: objectUrl,
      alt: fileName,
      densityAware: false,
      width: "100%",
    });
  }

  return new MessageStrip({
    text: "Pré-visualização indisponível para este tipo de arquivo. Use Baixar.",
    type: "Information",
    showIcon: true,
  }).addStyleClass("sapUiSmallMargin");
}

function download(objectUrl: string, fileName: string): void {
  const link = document.createElement("a");
  link.href = objectUrl;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
}
```

Rode `yarn ts-typecheck`. Se o typing de `sap/ui/core/HTML` recusar `sanitizeContent: false`, use
o MCP `ui5` (`get_api_reference` para `sap.ui.core.HTML`) ou o `context7`, e ajuste pela API real.
Não use `as any`.

- [ ] **Step 2: Carga, aba Anexos**

`ShipmentLoadAttachments.fragment.xml`: na toolbar, entre "Anexar" e "Baixar":

```xml
				<Button type="Transparent" text="Visualizar" icon="sap-icon://show" press=".onViewAttachment"/>
```

Coluna "Arquivo": troque o `<Text .../>` por

```xml
				<t:template>
					<!-- GAC-1171 (melhorias): o nome abre o visualizador direto da linha, sem exigir
					     seleção. Antes o único caminho era selecionar e Baixar. -->
					<Link text="{viewModel>FileName}" wrapping="false" press=".onViewAttachmentFromRow"/>
				</t:template>
```

`BaseController.ts`: acrescente `import { openAttachmentViewer } from "siagrob1/dialogs/AttachmentViewer";`,
`import { Link$PressEvent } from "sap/m/Link";` e, logo depois de `onDownloadAttachment`:

```ts
  /** GAC-1171 (melhorias): visualiza o anexo selecionado, sem baixar. */
  async onViewAttachment(): Promise<void> {
    const row = this.selectedAttachmentRow();

    if (!row) return;

    await this.viewShipmentLoadAttachment(row.Key, row.FileName);
  }

  /** O Link da coluna Arquivo: usa o contexto da PRÓPRIA linha, não a seleção. */
  async onViewAttachmentFromRow(event: Link$PressEvent): Promise<void> {
    const row = event.getSource().getBindingContext("viewModel")?.getObject() as
      { Key: string; FileName?: string } | undefined;

    if (!row?.Key) return;

    await this.viewShipmentLoadAttachment(row.Key, row.FileName);
  }

  private viewShipmentLoadAttachment(key: string, fileName?: string): Promise<void> {
    return openAttachmentViewer({
      url: `${ServerRoutes.shipmentLoadsAttachmentsDownload}(Key=${key})`,
      fileName,
    });
  }
```

Troque o tipo de retorno de `selectedAttachmentRow()` para `{ Key: string; FileName?: string } | null`,
e o cast do `return` para `{ Key: string; FileName?: string }`.

- [ ] **Step 3: Carga, aba Descargas**

`ShipmentLoadDischarges.fragment.xml`, coluna "Anexo": troque `tooltip="Baixar o ticket anexado"`
por `tooltip="Visualizar o ticket anexado"`.

`BaseController.onDownloadDischargeAttachment`: renomeie para `onViewDischargeAttachment` (e o
`press` do fragmento junto), com o corpo:

```ts
  /**
   * O clip do ticket abre o visualizador (GAC-1171, melhorias). O ticket só guarda a
   * AttachmentKey: o nome do arquivo vem do Content-Disposition, e o `$select` do grid não
   * precisa mudar.
   */
  async onViewDischargeAttachment(event: Button$PressEvent): Promise<void> {
    const key = event.getSource().getBindingContext()?.getProperty("AttachmentKey") as string;

    if (!key) return;

    await openAttachmentViewer({ url: `${ServerRoutes.shipmentLoadsAttachmentsDownload}(Key=${key})` });
  }
```

Mantenha o comentário antigo do método (`EntitySet` inexistente no EDM) acima do novo.

- [ ] **Step 4: Correção do item 3, só se a Task 8 concluiu (b)**

Aplique a correção da causa encontrada, com **teste que falha antes** no nível em que a causa está:
xUnit, se estiver no backend (`ShipmentLoadAttachmentsServiceTests.cs`, ou o controller de
download); QUnit num helper, se estiver no frontend. Acrescente a nota ao spec (§4.3). Se a Task 8
concluiu (a), pule este Step e registre "(a), sem correção adicional" no relatório.

- [ ] **Step 5: Gates e navegador**

```bash
yarn ts-typecheck && yarn lint
```

Com a stack de pé (`yarn start:dev`, Web e Gateway pelo worktree):

1. Aba Anexos da carga da Task 9: anexe um PDF, uma imagem PNG, um `.docx` e um `.html`.
2. Clique no **nome** de cada um: o PDF abre no diálogo, a imagem aparece, o `.docx` e o `.html`
   mostram o aviso. **Baixar** baixa com o nome certo; **Fechar** fecha.
3. Selecione a linha e clique em **Visualizar**: mesmo resultado.
4. Aba Descargas, no clip de um ticket com anexo: o diálogo abre, com título = nome do arquivo.
5. DevTools: nenhuma requisição duplicada ao clicar em Baixar dentro do diálogo; nenhum erro no
   console; após fechar, o `blob:` não fica referenciado (o iframe some do DOM).
6. O defeito do item 3 **não** se reproduz mais pelo caminho da Task 8.

- [ ] **Step 6: Commit**

```bash
git branch --show-current
git add webapp/dialogs/AttachmentViewer.ts
git add -A webapp
git commit -F - <<'EOF'
feat(shipment): visualizar anexos da carga num diálogo sem baixar

O nome do arquivo na aba Anexos e o clip da aba Descargas passam a abrir
o documento na própria tela. Antes o único caminho era selecionar a
linha e baixar, e o anexo manual parecia não abrir.

Refs: GAC-1171
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM
EOF
```

Se o Step 4 corrigiu uma causa (b), faça um commit **separado** antes deste: `fix(shipment): ...`
no repo em que a causa estava, com o corpo descrevendo a causa.

---

### Task 12: Visualizador nos anexos dos contratos de compra e de venda

**Files:**
- Modify: `webapp/view/purchaseContracts/fragments/PurchaseContractAttachments.fragment.xml` (toolbar ~33, coluna "Arquivo" ~46-50)
- Modify: `webapp/controller/purchaseContracts/PurchaseContractsBaseController.ts` (junto de `onDownload`, ~linha 30-70)
- Modify: `webapp/view/salesContracts/fragments/SalesContractAttachments.fragment.xml` (toolbar, coluna "Arquivo")
- Modify: `webapp/controller/salesContracts/SalesContractsBaseController.ts` (junto de `onDownload`, ~linha 28-70)

**Interfaces:**
- Consumes: `openAttachmentViewer` (Task 11). As linhas vêm do JSONModel `attachmentsModel`
  (`/value` na compra, `/` na venda), com `Key` e `FileName`.

- [ ] **Step 1: Contrato de compra**

No fragmento, logo antes do botão `text="Download"`:

```xml
          <Button
            text="Visualizar"
            type="Transparent"
            icon="sap-icon://show"
            press=".onViewAttachment"
          />
```

Coluna "Arquivo": troque `<Text text="{attachmentsModel>FileName}" />` por

```xml
          <!-- GAC-1171 (melhorias): o nome abre o visualizador direto da linha. -->
          <Link text="{attachmentsModel>FileName}" press=".onViewAttachmentFromRow" />
```

No controller, acrescente `import { openAttachmentViewer } from "siagrob1/dialogs/AttachmentViewer";`,
`import { Link$PressEvent } from "sap/m/Link";` e, logo depois de `onDownload`:

```ts
  /** GAC-1171 (melhorias): visualiza o anexo selecionado, sem baixar. */
  async onViewAttachment(): Promise<void> {
    const table = this.byId("purchaseContractAttachmentsTable") as Table;
    const selected = table.getSelectedIndex();

    if (selected < 0) {
      MessageBox.alert("Selecione um item na tabela.");
      return;
    }

    const ctx = table.getContextByIndex(selected);
    await this.viewContractAttachment(ctx.getProperty("Key") as string, ctx.getProperty("FileName") as string);
  }

  /** O Link da coluna Arquivo: usa o contexto da PRÓPRIA linha, não a seleção. */
  async onViewAttachmentFromRow(event: Link$PressEvent): Promise<void> {
    const ctx = event.getSource().getBindingContext("attachmentsModel");
    const key = ctx?.getProperty("Key") as string;

    if (!key) return;

    await this.viewContractAttachment(key, ctx.getProperty("FileName") as string);
  }

  private viewContractAttachment(key: string, fileName?: string): Promise<void> {
    return openAttachmentViewer({
      url: `/odata/PurchaseContractsAttachmentsDownload(Key=${key})`,
      fileName,
    });
  }
```

- [ ] **Step 2: Contrato de venda**

Faça o mesmo em `SalesContractAttachments.fragment.xml` e `SalesContractsBaseController.ts`, com o
código abaixo (completo, sem herdar da compra):

```ts
  /** GAC-1171 (melhorias): visualiza o anexo selecionado, sem baixar. */
  async onViewAttachment(): Promise<void> {
    const table = this.byId("salesContractAttachmentsTable") as Table;
    const selected = table.getSelectedIndex();

    if (selected < 0) {
      MessageBox.alert("Selecione um item na tabela.");
      return;
    }

    const ctx = table.getContextByIndex(selected);
    await this.viewContractAttachment(ctx.getProperty("Key") as string, ctx.getProperty("FileName") as string);
  }

  /** O Link da coluna Arquivo: usa o contexto da PRÓPRIA linha, não a seleção. */
  async onViewAttachmentFromRow(event: Link$PressEvent): Promise<void> {
    const ctx = event.getSource().getBindingContext("attachmentsModel");
    const key = ctx?.getProperty("Key") as string;

    if (!key) return;

    await this.viewContractAttachment(key, ctx.getProperty("FileName") as string);
  }

  private viewContractAttachment(key: string, fileName?: string): Promise<void> {
    return openAttachmentViewer({
      url: `/odata/SalesContractsAttachmentsDownload(Key=${key})`,
      fileName,
    });
  }
```

O botão e o Link do fragmento de venda são idênticos aos da compra.

Antes de editar, confirme com `grep -rn "PurchaseContractAttachments\|SalesContractAttachments" webapp/view`
quais views incluem os fragmentos e se **todos** os controllers delas herdam do BaseController
correspondente. Se alguma view usar um controller que não herda, o `press` quebraria ali: reporte
e trate antes do commit.

- [ ] **Step 3: Gates e navegador**

```bash
yarn ts-typecheck && yarn lint
```

Num contrato de compra e num de venda com anexos (anexe um PDF se não houver): clique no nome →
diálogo; selecione e **Visualizar** → diálogo; **Download** antigo continua baixando. Em cada tela
que inclui o fragmento (Detalhe e Edição, conforme o grep), um clique no nome.

- [ ] **Step 4: Commit**

```bash
git branch --show-current
git add -A webapp
git commit -F - <<'EOF'
feat(sales-contract): visualizar anexos dos contratos num diálogo sem baixar

Mesmo visualizador da carga, pedido pelo usuário também para os anexos
dos contratos de compra e de venda. O Download continua disponível.

Refs: GAC-1171
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01D9azYj2FzqPz5xBpqDerHM
EOF
```

---

### Task 13: Verificação de ponta a ponta, revisão final e encerramento

**Files:** memória do projeto (fora dos repos).

- [ ] **Step 1: A Conferência pelo caminho do usuário**

Com a stack pelo worktree (backend das Tasks 1-7, frontend das Tasks 9-12):

1. Carga Faturada, com notas **Confirmadas**, da Task 9. Anote os itens das notas dela.
2. **Documentos de Saída → Conferência de Entregas** (`/sales-invoices/reconciliation`): encerre
   (Closed, com quantidade entregue) **todos** os itens da carga, menos um, e salve. A carga continua
   Faturada (ou Descarregada, se marcada).
3. Encerre o último e salve. A carga fica **Concluída** na lista, no Painel (raia Concluída) e no
   Detalhe. O log de alterações mostra "… → Concluída" com o seu usuário.
4. **Estornar Conferência de entregas** (`/sales-invoices/open-reconciliation`): reabra um item. A
   carga volta a Faturada, ou a Descarregada se a marca existir.
5. Com a carga Concluída: "Desfazer Descarregada" e "Reabrir Carga" não aparecem. Chamando a action
   pelo console do navegador, o servidor recusa com a mensagem da Task 3 / Task 7.

Se não houver carga com nota confirmada e item na Conferência: fature uma expedição para criar uma
(memória `gac-1171-discharge-tickets-feature`). Notas Retornadas ficam fora da Conferência.

- [ ] **Step 2: Derrubar a stack**

Pare Web (50000), Gateway (5246) e o dev server (8080). Confira as portas de novo e mate por PID
(`Get-NetTCPConnection -LocalPort <p> -State Listen` → `Stop-Process -Force`). A stack volta
sozinha com o file watcher.

- [ ] **Step 3: Suítes finais**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend-gac-1171" && dotnet test SiagroB1.sln -v q 2>&1 | tail -3
cd "C:/Projetos/SiagroB1/siagro-b1-frontend-gac-1171" && yarn ts-typecheck && yarn lint
git -C "C:/Projetos/SiagroB1/siagro-b1-backend-gac-1171" status --short
git -C "C:/Projetos/SiagroB1/siagro-b1-frontend-gac-1171" status --short
```

Expected: backend = baseline + testes novos, zero falhas; frontend limpo; `git status` limpo nos
dois.

- [ ] **Step 4: Revisão da branch inteira**

Use `superpowers:requesting-code-review` sobre `main..feature/gac-1171-discharged-status` **nos dois
repos**. Instrua o revisor a checar **requisito a requisito contra o spec** (§2.1-§2.9, §3, §4),
não só os arquivos. A memória do GAC-1171 registra que revisões por task deixaram passar uma coluna
inteira do spec. Corrija o que for real, com commit próprio.

- [ ] **Step 5: Memória**

Atualize `C:\Users\Penalva\.claude\projects\C--Projetos-SiagroB1\memory\gac-1171-improvements-discharged-status-attachment-viewer.md`:
de "spec commitado" para "IMPLEMENTADO e verificado <data>", com os commits dos dois repos, a causa
real do item 3 e as armadilhas encontradas. Atualize a linha do `MEMORY.md`. **Não** faça push nem
merge: reporte ao usuário as duas branches prontas para ele decidir.

---

## Cobertura do spec (autoverificação)

| Spec | Task |
|---|---|
| §2.1 marca, enum, Completed reaproveitado | 1 |
| §2.2 ResolveClosure + apagar a marca | 2 |
| §2.3 "todos conferidos" | 2 (e Review Focus 1-2) |
| §2.4 projeção nos romaneios | 2 |
| §2.5 transições | 2, 3, 5, 6 |
| §2.6 Marcar/Desfazer + movimentos + OData | 1, 3, 4 |
| §2.7 gancho da Conferência + outros escritores | 5, 6 |
| §2.8 varredura | 7 (decisão ⚠️ mantida como escrita) |
| §2.9 migration sem backfill | 1 (sem backfill; deploy aplica a migration) |
| §3.1 botões + visibilidades | 9 |
| §3.2 rótulos | 9 |
| §3.3 listas, aviso, Painel | 9 |
| §4.1 componente | 10, 11 |
| §4.2 pontos de uso | 11, 12 |
| §4.3 item 3 | 8, 11 (Step 4) |
| §5 testes e verificação | todas; 13 |
