# Contra-nota a partir da escrituração do produtor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gerar a contra-nota (NF-e própria de entrada com NFref para a chave do produtor) a partir da escrituração confirmada da NF-e do produtor, com a compra contada uma vez só e a devolução referenciando a nota do produtor.

**Architecture:** A natureza ganha a flag `AllowsCounterNote`. A contra-nota é um `PurchaseInvoice` Própria/Normal comum, ligada à escrituração por `ProducerInvoiceKey` (cabeçalho) e `ProducerInvoiceItemKey` (linha), criada por um serviço novo que COPIA a fotografia fiscal e zera o Funrural. Ela é emitida pelo fluxo de entrada própria que já existe; a única novidade na emissão é o NFref com a chave da escrituração. Travas impedem editar o que veio da escrituração, estornar/cancelar a escrituração com contra-nota válida e devolver a contra-nota; os relatórios de entrada ignoram a contra-nota.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes em EF InMemory), xUnit, OData v4; OpenUI5 + TypeScript, QUnit.

**Spec:** `docs/superpowers/specs/2026-10-10-producer-counter-note-design.md` (repo backend, commit 7d17e30).

## Global Constraints

- Branch `feature/producer-counter-note` nos DOIS repos. O backend já está nele (spec commitado); crie no frontend na Task 7 (`git -C ../siagro-b1-frontend switch -c feature/producer-counter-note` a partir do `main`). Rode `git branch --show-current` antes de CADA commit. **Nunca push, nunca merge.**
- Todo arquivo novo: `git add <path>` logo após criar.
- Identificadores em inglês; texto que o usuário lê (mensagens, labels) em pt-BR.
- Commits: `tipo(escopo): descrição em pt-BR, imperativo, minúscula, sem ponto` — escopos `invoice` (documento de entrada/NF-e), `master-data` (natureza), `reports`. A migration exige o trailer `DB: AddPurchaseInvoiceCounterNote`. Terminar TODA mensagem com:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
  ```
- "Contra-nota válida" = `InvoiceStatus != Cancelled` E `NfeStatus` fora de {`Cancelled`, `Voided`, `Denied`} (spec §7). Só existe UMA definição: `PurchaseInvoiceCounterNote.FindValidAsync` (backend) e `isValidCounterNote` (frontend).
- A contra-nota NÃO é recalculada pelo motor e NÃO tem Funrural (spec D5, D7).
- Só a escrituração `IssuerType = ThirdParty`, `InvoiceType = Normal`, `TaxDocumentKind = Nfe`, `InvoiceStatus = Confirmed`, `ChaveNFe` com 44 dígitos gera contra-nota (spec D3).
- A Emissão Própria avulsa (criada pelo seletor "Emissão") NÃO muda (spec D6).
- Comandos backend (de `siagro-b1-backend/`): `dotnet build SiagroB1.sln`, `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~X" --nologo`.
- Comandos frontend (de `siagro-b1-frontend/`): `yarn ts-typecheck`, `yarn lint`. `yarn test` tem gate de cobertura que nunca passa — rode os QUnit pela página `http://localhost:8080/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests` com `yarn start`.
- Matar processo só pelo PID (nunca `taskkill /IM node.exe` / `dotnet.exe`).

## Review Focus

1. **POST/PATCH da API tentando ligar um documento qualquer a uma escrituração** (`ProducerInvoiceKey` no corpo) — o servidor descarta; só o "Gerar contra-nota" grava o vínculo. Teste na Task 3 (`Body_cannot_link_an_entry_to_a_producer`).
2. **Contra-nota cancelada, inutilizada ou excluída** — a escrituração volta a aceitar estorno, cancelamento e uma nova contra-nota. Testes na Task 3 (`Cancelled_counter_note_does_not_block_a_new_one`) e Task 4 (`Deleting_the_counter_note_frees_the_producer`).
3. **Contra-nota Autorizada abrindo o "Devolver"** — ela é Própria/Normal/Autorizada, exatamente o que o "Devolver" da entrada própria aceita hoje; precisa ser recusada no servidor e o botão escondido na tela. Teste na Task 4 (`Counter_note_is_not_returnable`) e na Task 8 (QUnit `canReturnPurchase` com `ProducerInvoiceKey`).
4. **Escrituração estornada por fora depois de a contra-nota nascer** (dado mexido no banco, ou contra-nota criada antes da trava) — a emissão recusa antes de reservar número. Teste na Task 5 (`Producer_no_longer_confirmed_blocks_the_issue_before_the_number`).
5. **PATCH da linha da contra-nota pela grade** (`PurchaseInvoicesItems({key})`, que não passa pelo Update do cabeçalho) — quantidade/produto/natureza voltam ao copiado. Teste na Task 4 (`Line_patch_is_restored`).

---

### Task 1: Modelo de dados e migration

**Files:**
- Modify: `SiagroB1.Domain/Entities/Usage.cs` (depois de `ReturnUsage`, ~linha 123)
- Modify: `SiagroB1.Domain/Entities/PurchaseInvoice.cs` (depois de `PurchaseInvoiceOrigin`, ~linha 190)
- Modify: `SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs` (depois de `PurchaseInvoiceItemOrigin`, ~linha 71)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (depois da auto-relação `PurchaseInvoiceItemOrigin`, ~linha 233)
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddPurchaseInvoiceCounterNote.cs` (+ `.Designer.cs`, snapshot)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceNfeSchemaTests.cs`

**Interfaces:**
- Produces: `Usage.AllowsCounterNote` (bool); `PurchaseInvoice.ProducerInvoiceKey` (Guid?) + `ProducerInvoice` (nav); `PurchaseInvoiceItem.ProducerInvoiceItemKey` (Guid?) + `ProducerInvoiceItem` (nav).

- [ ] **Step 1: Write the failing test** — em `PurchaseInvoiceNfeSchemaTests.cs`, acrescente:

```csharp
    /// <summary>A contra-nota aponta a escrituração do produtor (spec 2026-10-10 §4); a tela lê o vínculo e expande o número.</summary>
    [Fact]
    public void Counter_note_link_is_exposed()
    {
        var model = Model();
        var invoice = model.SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == nameof(PurchaseInvoice));
        var item = model.SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == nameof(PurchaseInvoiceItem));

        Assert.NotNull(invoice.FindProperty("ProducerInvoiceKey"));
        Assert.NotNull(invoice.FindProperty("ProducerInvoice"));
        Assert.NotNull(item.FindProperty("ProducerInvoiceItemKey"));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceNfeSchemaTests" --nologo`
Expected: FAIL (`ProducerInvoiceKey` não existe — assert nulo).

- [ ] **Step 3: Write minimal implementation**

`Usage.cs`, depois de `public Usage? ReturnUsage { get; set; }`:

```csharp

    /// <summary>
    /// Permite gerar a contra-nota a partir da escrituração da NF-e do produtor (spec 2026-10-10 D4). Só natureza de
    /// Entrada; a contra-nota usa esta MESMA natureza e os mesmos tributos da escrituração (D5).
    /// </summary>
    public bool AllowsCounterNote { get; set; }
```

`PurchaseInvoice.cs`, depois de `public virtual PurchaseInvoice? PurchaseInvoiceOrigin { get; set; }`:

```csharp

    /// <summary>
    /// Contra-nota: a escrituração da NF-e do produtor de que esta entrada própria nasceu (spec 2026-10-10 §4). Só o
    /// "Gerar contra-nota" grava. Preenchido = documento só para a SEFAZ: estoque fiscal, título, tributação e relatórios
    /// olham a escrituração (D13). Colunas próprias, e não <see cref="PurchaseInvoiceOriginKey"/>, que é remessa/devolução
    /// e alimenta o saldo devolvível (D12).
    /// </summary>
    public Guid? ProducerInvoiceKey { get; set; }
    public virtual PurchaseInvoice? ProducerInvoice { get; set; }
```

`PurchaseInvoiceItem.cs`, depois de `public virtual PurchaseInvoiceItem? PurchaseInvoiceItemOrigin { get; set; }`:

```csharp

    /// <summary>Na contra-nota, a linha da escrituração do produtor que esta linha reproduz (spec 2026-10-10 §4).</summary>
    public Guid? ProducerInvoiceItemKey { get; set; }
    public virtual PurchaseInvoiceItem? ProducerInvoiceItem { get; set; }
```

`AppDbContext.cs`, logo depois do bloco `HasOne(x => x.PurchaseInvoiceItemOrigin)...OnDelete(DeleteBehavior.Restrict);`:

```csharp

        // Contra-nota → escrituração do produtor (spec 2026-10-10 D12). Restrict como as auto-relações acima: a
        // escrituração confirmada não é excluída, e a contra-nota (dependente) some sem levar nada junto.
        modelBuilder.Entity<PurchaseInvoice>()
            .HasOne(x => x.ProducerInvoice)
            .WithMany()
            .HasForeignKey(x => x.ProducerInvoiceKey)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PurchaseInvoiceItem>()
            .HasOne(x => x.ProducerInvoiceItem)
            .WithMany()
            .HasForeignKey(x => x.ProducerInvoiceItemKey)
            .OnDelete(DeleteBehavior.Restrict);
```

(O spec §4 fala em `NoAction`; `Restrict` gera o mesmo `NO ACTION` no SQL Server e segue o padrão das duas auto-relações vizinhas — não troque um pelo outro depois, ver memória "Restrict e NoAction não são intercambiáveis".)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceNfeSchemaTests" --nologo`
Expected: PASS.

- [ ] **Step 5: Generate the migration**

Run: `dotnet ef migrations add AddPurchaseInvoiceCounterNote --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext`

Abra o `<timestamp>_AddPurchaseInvoiceCounterNote.cs` gerado e confira:
- `USAGES`: `AddColumn<bool>("AllowsCounterNote", nullable: false, defaultValue: false)`.
- `PURCHASE_INVOICES`: `AddColumn<Guid>("ProducerInvoiceKey", nullable: true)`, `CreateIndex` não único, `AddForeignKey` com `onDelete: ReferentialAction.Restrict`.
- `PURCHASE_INVOICES_ITEMS`: `AddColumn<Guid>("ProducerInvoiceItemKey", nullable: true)`, `CreateIndex` não único, `AddForeignKey` `Restrict`.
- Nada além disso. Se aparecer alteração em outra tabela/coluna, PARE e reporte — é drift do snapshot, não deste trabalho.
- `Down()` desfaz as 3 colunas, 2 índices e 2 FKs.

Run: `dotnet build SiagroB1.sln` → 0 erros.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Migrations/AppContext/*_AddPurchaseInvoiceCounterNote*.cs
git add SiagroB1.Domain/Entities/Usage.cs SiagroB1.Domain/Entities/PurchaseInvoice.cs SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs \
  SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Migrations/AppContext/AppDbContextModelSnapshot.cs \
  SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceNfeSchemaTests.cs
git commit  # feat(invoice): vínculo da contra-nota com a escrituração do produtor — trailer "DB: AddPurchaseInvoiceCounterNote"
```

---

### Task 2: Flag "Permite contra-nota" na natureza

**Files:**
- Modify: `SiagroB1.Domain/Models/UsageModel.cs` (depois de `ReturnUsageName`)
- Modify: `SiagroB1.Application/Services/UsageService.cs` (projeção ~linha 69; `ValidateDirectionRules` ~linha 253)
- Modify: `SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs` (fim de `CopyToEntity`, ~linha 68)
- Test: `SiagroB1.Application.Tests/Usages/UsageCounterNoteTests.cs` (novo)

**Interfaces:**
- Consumes: `Usage.AllowsCounterNote` (Task 1).
- Produces: `UsageModel.AllowsCounterNote` (bool) — lido pelo frontend (Task 7).

- [ ] **Step 1: Write the failing test** — `SiagroB1.Application.Tests/Usages/UsageCounterNoteTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>"Permite contra-nota" na natureza (spec 2026-10-10 D4): vai e volta, e só existe na natureza de entrada.</summary>
public class UsageCounterNoteTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Purchase() => new()
    {
        Name = "Compra produtor rural", Direction = UsageDirection.Incoming,
        CfopIncomingInState = "1102", CfopIncomingOutState = "2102", PisCst = "74", CofinsCst = "74",
        RequiresQuantity = true, AllowsCounterNote = true,
    };

    [Fact]
    public async Task Flag_is_saved_and_read_back()
    {
        var name = Guid.NewGuid().ToString();
        var created = await Service(TestDb.CreateUnitOfWork(name)).CreateAsync(Purchase());

        var read = await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code);

        Assert.True(read!.AllowsCounterNote);
    }

    [Fact]
    public async Task Update_can_clear_the_flag()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var created = await Service(db).CreateAsync(Purchase());

        var model = Purchase();
        model.AllowsCounterNote = false;
        await Service(db).UpdateAsync(created.Code, model);

        Assert.False((await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code))!.AllowsCounterNote);
    }

    [Fact]
    public async Task Outgoing_usage_cannot_allow_a_counter_note()
    {
        var model = Purchase();
        model.Direction = UsageDirection.Outgoing;
        model.CfopOutgoingInState = "5102";
        model.CfopOutgoingOutState = "6102";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Só natureza de entrada pode permitir contra-nota.", e.Message);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~UsageCounterNoteTests" --nologo`
Expected: FAIL — não compila (`UsageModel.AllowsCounterNote` não existe).

- [ ] **Step 3: Write minimal implementation**

`UsageModel.cs`, depois de `public string? ReturnUsageName { get; set; }`:

```csharp

    /// <summary>Permite gerar a contra-nota a partir da escrituração da NF-e do produtor (só natureza de entrada).</summary>
    public bool AllowsCounterNote { get; set; }
```

`UsageService.cs`, na projeção do `UsageModel`, depois de `ReturnUsageName = ...,`:

```csharp
                    AllowsCounterNote = x.usage.AllowsCounterNote,
```

`UsageService.cs`, no fim de `ValidateDirectionRules(UsageModel model)` (antes da `}` final do método):

```csharp

        // A contra-nota é entrada própria com a mesma natureza da escrituração (spec 2026-10-10 D4/D5).
        if (model.AllowsCounterNote && (model.Direction ?? UsageDirection.Outgoing) != UsageDirection.Incoming)
            throw new DefaultException("Só natureza de entrada pode permitir contra-nota.");
```

`UsageTaxationMapper.cs`, logo depois de `usage.ReturnUsageCode = model.ReturnUsageCode;`:

```csharp

        usage.AllowsCounterNote = model.AllowsCounterNote;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Usages" --nologo`
Expected: PASS (os testes antigos de natureza continuam verdes).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application.Tests/Usages/UsageCounterNoteTests.cs
git add SiagroB1.Domain/Models/UsageModel.cs SiagroB1.Application/Services/UsageService.cs SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs
git commit  # feat(master-data): natureza de entrada que permite contra-nota
```

---

### Task 3: "Gerar contra-nota" (serviço + action)

**Files:**
- Modify: `SiagroB1.Application/Services/Taxes/TaxSnapshot.cs` (novo método `Copy`)
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceCounterNote.cs`
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCounterNoteCreateService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs` (parâmetro `counterNote`)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsCreateService.cs` (descarta o vínculo do corpo)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs` (`IsActiveForAsync`)
- Create: `SiagroB1.Web/Actions/PurchaseInvoices/PurchaseInvoicesCreateCounterNoteController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (action, perto de `PurchaseInvoicesCreateNfeReturn`, ~linha 166)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (DI, perto da linha 359)
- Create: `SiagroB1.Application.Tests/Support/CounterNoteSeed.cs`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesCounterNoteTests.cs` (novo)

**Interfaces:**
- Consumes: Task 1 (colunas), Task 2 (`Usage.AllowsCounterNote`).
- Produces:
  - `PurchaseInvoiceCounterNote.FindValidAsync(AppDbContext context, Guid producerInvoiceKey) : Task<PurchaseInvoice?>`
  - `PurchaseInvoiceCounterNote.EnsureNoValidAsync(AppDbContext context, Guid producerInvoiceKey, string action) : Task`
  - `PurchaseInvoicesCounterNoteCreateService.ExecuteAsync(Guid producerInvoiceKey, string userName) : Task<PurchaseInvoice>`
  - `PurchaseInvoicesCreateService.ExecuteAsync(PurchaseInvoice invoice, string userName, bool nfeReturn = false, bool counterNote = false)`
  - `TaxSnapshot.Copy(INfeTaxedLine source, INfeTaxedLine target)`
  - `CounterNoteSeed.SeedAsync(bool allowsCounterNote = true) : Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Producer)>`
  - `PurchaseInvoicesCounterNoteTests.Service(PurchaseNfeScenario scenario, string? erp = "STANDALONE")` (internal static, usado pelas Tasks 4 e 5)
  - Action OData `POST odata/PurchaseInvoicesCreateCounterNote` `{ "Key": Guid }` → `Guid` da contra-nota.

- [ ] **Step 1: Create the seed** — `SiagroB1.Application.Tests/Support/CounterNoteSeed.cs` (e `git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Escrituração da NF-e do produtor pronta para a contra-nota (spec 2026-10-10): a entrada de terceiro confirmada do
/// <see cref="ThirdPartyPurchaseSeed"/> com as linhas calculadas pela natureza "COMPRA DE MERCADORIA" (CFOP 1102), a
/// natureza marcada "Permite contra-nota", a condição de pagamento preenchida e Funrural (SENAR 0,2%) na linha do TRIGO.
/// </summary>
public static class CounterNoteSeed
{
    public static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Producer)> SeedAsync(bool allowsCounterNote = true)
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        var context = scenario.Db.Context;

        var usage = await context.Usages.SingleAsync(u => u.Name == "COMPRA DE MERCADORIA");
        usage.AllowsCounterNote = allowsCounterNote;

        var producer = await context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);
        producer.PaymentConditionCode = (await context.PaymentConditions.FirstAsync()).Code;

        foreach (var line in producer.Items)
        {
            line.UsageCode = usage.Code;
            line.UsageName = usage.Name;
            line.Cfop = "1102";

            if (line.ItemCode == "TRIGO")
            {
                line.FunruralBase = 1500m;
                line.FunruralSenarRate = 0.2m;
                line.FunruralSenarValue = 3m;
            }
        }

        await scenario.Db.SaveChangesAsync();
        context.ChangeTracker.Clear();

        return (scenario, await context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key));
    }
}
```

- [ ] **Step 2: Write the failing tests** — `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesCounterNoteTests.cs` (e `git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>"Gerar contra-nota" (spec 2026-10-10 §5): cópia da escrituração, sem recálculo e sem Funrural, e as recusas.</summary>
public class PurchaseInvoicesCounterNoteTests
{
    internal static PurchaseInvoicesCreateService Create(PurchaseNfeScenario scenario, string? erp = "STANDALONE") =>
        new(scenario.Db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(scenario.Db, PurchaseNfeTestSeed.Partners(), erp), new FakeDocNumberSequenceService());

    internal static PurchaseInvoicesCounterNoteCreateService Service(PurchaseNfeScenario scenario, string? erp = "STANDALONE") =>
        new(scenario.Db, new TaxCalculationGate(scenario.Db, NfeTestSeed.Config(erp)), Create(scenario, erp),
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

    private const string OriginRule =
        "A contra-nota parte da escrituração de uma NF-e de produtor: documento de terceiro, Normal, confirmado e com a chave de 44 dígitos.";

    [Fact]
    public async Task Creates_a_pending_own_entry_with_the_producer_lines_and_taxes()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();

        var created = await Service(scenario).ExecuteAsync(producer.Key, "tester");

        var note = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key);
        Assert.Equal((DocumentIssuerType.Own, PurchaseInvoiceType.Normal, InvoiceStatus.Pending, TaxDocumentKind.Nfe),
            (note.IssuerType, note.InvoiceType, note.InvoiceStatus, note.TaxDocumentKind));
        Assert.Equal(producer.Key, note.ProducerInvoiceKey);
        Assert.Equal((producer.CardCode, producer.PaymentConditionCode, producer.GrossWeight, producer.NetWeight),
            (note.CardCode, note.PaymentConditionCode, note.GrossWeight, note.NetWeight));
        Assert.False(note.IsNfeReturn);
        Assert.Null(note.ChaveNFe);
        Assert.Equal(2, note.Items.Count);

        foreach (var line in note.Items)
        {
            var source = producer.Items.Single(i => i.Key == line.ProducerInvoiceItemKey);
            Assert.Equal((source.ItemCode, source.Quantity, source.UnitPrice, source.UsageCode, source.Cfop),
                (line.ItemCode, line.Quantity, line.UnitPrice, line.UsageCode, line.Cfop));
            Assert.Equal((source.CstIcms, source.IcmsBase, source.IcmsOperationValue, source.IcmsDeferredValue, source.IcmsBenefitCode),
                (line.CstIcms, line.IcmsBase, line.IcmsOperationValue, line.IcmsDeferredValue, line.IcmsBenefitCode));
            Assert.Equal(0m, line.FunruralValue);
            Assert.Null(line.NfeItemNumber);
        }
    }

    [Fact]
    public async Task Producer_is_not_changed()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();

        await Service(scenario).ExecuteAsync(producer.Key, "tester");

        var reloaded = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, producer.Key);
        Assert.Equal(InvoiceStatus.Confirmed, reloaded.InvoiceStatus);
        Assert.Null(reloaded.ProducerInvoiceKey);
        Assert.Equal(3m, reloaded.TotalFunrural);
    }

    [Fact]
    public async Task Body_cannot_link_an_entry_to_a_producer()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier, IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Normal, IssueDate = new DateTime(2026, 10, 2), ProducerInvoiceKey = producer.Key,
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", ItemName = "TRIGO EM GRAOS", UnitOfMeasureCode = "KG", Quantity = 1m,
            UnitPrice = 1m, UsageCode = producer.Items.First().UsageCode, ProducerInvoiceItemKey = producer.Items.First().Key,
        });

        await Create(scenario).ExecuteAsync(invoice, "tester");

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, invoice.Key);
        Assert.Null(saved.ProducerInvoiceKey);
        Assert.Null(saved.Items.Single().ProducerInvoiceItemKey);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("keyless")]
    [InlineData("other-kind")]
    [InlineData("return")]
    public async Task Producer_outside_the_rule_is_refused(string kind)
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var stored = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == producer.Key);
        switch (kind)
        {
            case "pending": stored.InvoiceStatus = InvoiceStatus.Pending; break;
            case "keyless": stored.ChaveNFe = null; break;
            case "other-kind": stored.TaxDocumentKind = TaxDocumentKind.Other; break;
            case "return": stored.InvoiceType = PurchaseInvoiceType.Return; break;
        }
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(scenario).ExecuteAsync(producer.Key, "tester"));

        Assert.Equal(OriginRule, e.Message);
    }

    [Fact]
    public async Task Own_entry_cannot_originate_a_counter_note()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(scenario).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal(OriginRule, e.Message);
    }

    [Fact]
    public async Task Usage_without_the_flag_is_refused()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync(allowsCounterNote: false);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(scenario).ExecuteAsync(producer.Key, "tester"));

        Assert.Equal("A natureza COMPRA DE MERCADORIA do item TRIGO não permite contra-nota.", e.Message);
    }

    [Fact]
    public async Task Line_without_usage_is_refused()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        (await scenario.Db.Context.PurchaseInvoicesItems.SingleAsync(i => i.PurchaseInvoiceKey == producer.Key && i.ItemCode == "MILHO"))
            .UsageCode = null;
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(scenario).ExecuteAsync(producer.Key, "tester"));

        Assert.Equal("O item MILHO da escrituração está sem natureza de operação.", e.Message);
    }

    [Fact]
    public async Task Branch_without_nfe_is_refused()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(scenario, erp: "SAPB1").ExecuteAsync(producer.Key, "tester"));

        Assert.Equal("A filial 01 não emite NF-e pelo Siagro.", e.Message);
    }

    [Fact]
    public async Task Second_counter_note_is_refused()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var first = await Service(scenario).ExecuteAsync(producer.Key, "tester");

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(scenario).ExecuteAsync(producer.Key, "tester"));

        Assert.Equal($"A escrituração já tem a contra-nota {first.InvoiceNumber}.", e.Message);
    }

    [Theory]
    [InlineData("document-cancelled")]
    [InlineData("nfe-voided")]
    [InlineData("nfe-denied")]
    public async Task Cancelled_counter_note_does_not_block_a_new_one(string kind)
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var first = await Service(scenario).ExecuteAsync(producer.Key, "tester");
        var stored = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == first.Key);
        switch (kind)
        {
            case "document-cancelled": stored.InvoiceStatus = InvoiceStatus.Cancelled; break;
            case "nfe-voided": stored.NfeStatus = NfeStatus.Voided; break;
            case "nfe-denied": stored.NfeStatus = NfeStatus.Denied; break;
        }
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();

        var second = await Service(scenario).ExecuteAsync(producer.Key, "tester");

        Assert.NotEqual(first.Key, second.Key);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesCounterNoteTests" --nologo`
Expected: FAIL — não compila (`PurchaseInvoicesCounterNoteCreateService` não existe).

- [ ] **Step 4: Write the implementation**

`TaxSnapshot.cs`, depois de `Write(...)`:

```csharp

    /// <summary>
    /// Copia natureza, CFOP, NCM, origem, tributos e flags de uma linha para outra, sem recalcular: a contra-nota reproduz
    /// a escrituração do produtor (spec 2026-10-10 D5). Usa <see cref="LockedProperties"/> para a cópia e a trava não
    /// divergirem.
    /// </summary>
    public static void Copy(INfeTaxedLine source, INfeTaxedLine target)
    {
        foreach (var name in LockedProperties)
        {
            var property = typeof(INfeTaxedLine).GetProperty(name)!;
            property.SetValue(target, property.GetValue(source));
        }

        target.UsageCode = source.UsageCode;
        target.UsageName = source.UsageName;
    }
```

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceCounterNote.cs` (e `git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Regras da contra-nota (spec 2026-10-10): a entrada própria que nasce da escrituração da NF-e do produtor e é só
/// documento para a SEFAZ. "Válida" é a que trava a escrituração e impede gerar outra (§7): não cancelada como
/// documento, e com a NF-e fora de cancelada, inutilizada ou denegada.
/// </summary>
public static class PurchaseInvoiceCounterNote
{
    public static Task<PurchaseInvoice?> FindValidAsync(AppDbContext context, Guid producerInvoiceKey) =>
        context.PurchaseInvoices.AsNoTracking()
            .Where(i => i.ProducerInvoiceKey == producerInvoiceKey
                        && i.InvoiceStatus != InvoiceStatus.Cancelled
                        && i.NfeStatus != NfeStatus.Cancelled
                        && i.NfeStatus != NfeStatus.Voided
                        && i.NfeStatus != NfeStatus.Denied)
            .FirstOrDefaultAsync();

    /// <summary>Escrituração com contra-nota válida não volta a Pendente nem é cancelada (D10).</summary>
    public static async Task EnsureNoValidAsync(AppDbContext context, Guid producerInvoiceKey, string action)
    {
        if (await FindValidAsync(context, producerInvoiceKey) is { } note)
            throw new DefaultException(
                $"A escrituração tem a contra-nota {note.InvoiceNumber}: cancele ou exclua a contra-nota antes de {action}.");
    }
}
```

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCounterNoteCreateService.cs` (e `git add`):

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// "Gerar contra-nota" (spec 2026-10-10 §5): a partir da escrituração confirmada da NF-e do produtor, cria a entrada
/// PRÓPRIA Pendente com a mesma natureza e a mesma fotografia fiscal de cada linha — copiada, não recalculada (D5) — e
/// sem Funrural (D7). Quem emite é o "Emitir NF-e" da contra-nota; a escrituração não é alterada.
/// </summary>
public class PurchaseInvoicesCounterNoteCreateService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    PurchaseInvoicesCreateService createService,
    Func<DateTimeOffset>? clock = null,
    TimeZoneInfo? storageZone = null)
{
    private readonly Func<DateTimeOffset> _now = clock ?? NfeIssueInputAssembler.BrasiliaNow;

    // Mesmo fuso em que o OData grava as datas (o do servidor): a emissão o converte de volta para Brasília.
    private readonly TimeZoneInfo _storageZone = storageZone ?? TimeZoneInfo.Local;

    public async Task<PurchaseInvoice> ExecuteAsync(Guid producerInvoiceKey, string userName)
    {
        var producer = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
                           .FirstOrDefaultAsync(i => i.Key == producerInvoiceKey)
                       ?? throw new NotFoundException("Documento de entrada não encontrado.");

        // TODA a validação antes de qualquer escrita.
        if (!await gate.IsActiveAsync(producer.BranchCode))
            throw new DefaultException($"A filial {producer.BranchCode} não emite NF-e pelo Siagro.");

        if (producer.IssuerType != DocumentIssuerType.ThirdParty || producer.InvoiceType != PurchaseInvoiceType.Normal ||
            producer.InvoiceStatus != InvoiceStatus.Confirmed || producer.TaxDocumentKind != TaxDocumentKind.Nfe ||
            producer.ChaveNFe is not { Length: 44 } key || !key.All(char.IsAsciiDigit))
            throw new DefaultException(
                "A contra-nota parte da escrituração de uma NF-e de produtor: documento de terceiro, Normal, confirmado e com a chave de 44 dígitos.");

        await EnsureUsagesAllowAsync(producer);

        if (await PurchaseInvoiceCounterNote.FindValidAsync(db.Context, producer.Key) is { } existing)
            throw new DefaultException($"A escrituração já tem a contra-nota {existing.InvoiceNumber}.");

        var today = TimeZoneInfo.ConvertTime(_now(), _storageZone).DateTime;

        var note = new PurchaseInvoice
        {
            Key = Guid.NewGuid(),
            BranchCode = producer.BranchCode,
            CardCode = producer.CardCode,
            CardName = producer.CardName,
            IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Normal,
            TaxDocumentKind = TaxDocumentKind.Nfe,
            IssueDate = today,
            PostingDate = today,
            ProducerInvoiceKey = producer.Key,
            PaymentConditionCode = producer.PaymentConditionCode,
            // Transporte, pesos e volume copiados e editáveis enquanto Pendente (a importação do XML não lê os pesos).
            NetWeight = producer.NetWeight,
            GrossWeight = producer.GrossWeight,
            TruckCode = producer.TruckCode,
            TruckingCompanyCode = producer.TruckingCompanyCode,
            TruckingCompanyName = producer.TruckingCompanyName,
            FreightTerms = producer.FreightTerms,
            VolumeQuantity = producer.VolumeQuantity,
            VolumeSpecies = producer.VolumeSpecies,
            VolumeBrand = producer.VolumeBrand,
            VolumeNumbering = producer.VolumeNumbering,
            Comments = $"Contra-nota da NF-e {NumberText(producer.TaxDocumentNumber)} série {producer.TaxDocumentSeries} do produtor.",
        };

        foreach (var bought in NfeItemNumbering.Ordered(producer.Items))
        {
            var line = new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(),
                ItemCode = bought.ItemCode,
                ItemName = bought.ItemName,
                UnitOfMeasureCode = bought.UnitOfMeasureCode,
                Quantity = bought.Quantity,
                UnitPrice = bought.UnitPrice,
                PurchaseContractKey = bought.PurchaseContractKey,
                FreightValue = bought.FreightValue,
                InsuranceValue = bought.InsuranceValue,
                DiscountValue = bought.DiscountValue,
                OtherExpensesValue = bought.OtherExpensesValue,
                ProducerInvoiceItemKey = bought.Key,
            };

            TaxSnapshot.Copy(bought, line);
            note.AddItem(line);
        }

        // O create descarta o Funrural (a contra-nota não retém — D7), não recalcula (o motor ignora a contra-nota) e
        // grava num SaveChanges só, com o número interno.
        await createService.ExecuteAsync(note, userName, counterNote: true);

        return note;
    }

    private async Task EnsureUsagesAllowAsync(PurchaseInvoice producer)
    {
        foreach (var line in NfeItemNumbering.Ordered(producer.Items))
        {
            if (line.UsageCode is not { } code)
                throw new DefaultException($"O item {line.ItemCode} da escrituração está sem natureza de operação.");

            var usage = await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code);

            if (usage is null || !usage.AllowsCounterNote)
                throw new DefaultException(
                    $"A natureza {usage?.Name ?? code.ToString(CultureInfo.InvariantCulture)} do item {line.ItemCode} não permite contra-nota.");

            if (string.IsNullOrWhiteSpace(line.Cfop))
                throw new DefaultException($"O item {line.ItemCode} da escrituração está sem os tributos calculados.");
        }
    }

    private static string? NumberText(string? number) =>
        long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : number;
}
```

`PurchaseInvoicesCreateService.cs`:
- Assinatura: `public async Task ExecuteAsync(PurchaseInvoice invoice, string userName, bool nfeReturn = false, bool counterNote = false)`.
- Logo depois de `invoice.IsNfeReturn = nfeReturn && invoice.InvoiceType == PurchaseInvoiceType.Return;`:

```csharp

        // Só o "Gerar contra-nota" (PurchaseInvoicesCounterNoteCreateService) liga a entrada à escrituração do produtor;
        // o corpo da API nunca — senão um POST tiraria a compra dos relatórios e da emissão normal (spec 2026-10-10 §8).
        if (!counterNote)
        {
            invoice.ProducerInvoiceKey = null;
            foreach (var item in invoice.Items)
                item.ProducerInvoiceItemKey = null;
        }
```

`PurchaseInvoicesItemsCreateService.cs`, logo depois de `InvoiceLineChargeRules.Ensure(item);`:

```csharp

        // O vínculo com a linha da escrituração só nasce no "Gerar contra-nota" (spec 2026-10-10 §4).
        item.ProducerInvoiceItemKey = null;
```

`PurchaseInvoicesTaxApplyService.cs`, substitua `IsActiveForAsync` por:

```csharp
    /// <summary>
    /// A contra-nota fica de fora (spec 2026-10-10 D5/D7): a fotografia é a da escrituração, copiada, e o Funrural é da
    /// escrituração. Fora da regra, o Update zera o Funrural que vier no corpo.
    /// </summary>
    public async Task<bool> IsActiveForAsync(PurchaseInvoice invoice) =>
        invoice.ProducerInvoiceKey is null
        && (invoice.IssuerType == DocumentIssuerType.Own
            && (invoice.InvoiceType == PurchaseInvoiceType.Normal || IsOwnNfeReturn(invoice))
            || invoice.IssuerType == DocumentIssuerType.ThirdParty && invoice.InvoiceType == PurchaseInvoiceType.Normal)
        && await gate.IsActiveAsync(invoice.BranchCode);
```

`SiagroB1.Web/Actions/PurchaseInvoices/PurchaseInvoicesCreateCounterNoteController.cs` (crie a pasta se não existir; `git add`):

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseInvoices;

/// <summary>"Gerar contra-nota": cria a contra-nota Pendente a partir da escrituração e devolve a chave dela (spec 2026-10-10 §5).</summary>
public class PurchaseInvoicesCreateCounterNoteController(PurchaseInvoicesCounterNoteCreateService service) : ODataController
{
    [HttpPost("odata/PurchaseInvoicesCreateCounterNote")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM.
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        try
        {
            var created = await service.ExecuteAsync(key, User.Identity?.Name ?? "Unknown");
            return Ok(created.Key);
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
        catch (ApplicationException e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

Antes de criar, confira onde ficam as outras actions do documento de entrada (`grep -rl "odata/PurchaseInvoicesConfirm" SiagroB1.Web`) e, se houver pasta própria, use-a (ajuste o `namespace`).

`ODataConfigurations.cs`, logo depois do bloco `purchaseInvoicesCreateNfeReturn` (que termina em `.Returns<Guid>();`):

```csharp

        // Contra-nota a partir da escrituração da NF-e do produtor (spec 2026-10-10 §5).
        var purchaseInvoicesCreateCounterNote = modelBuilder.Action("PurchaseInvoicesCreateCounterNote");
        purchaseInvoicesCreateCounterNote.Parameter<Guid>("Key");
        purchaseInvoicesCreateCounterNote.Returns<Guid>();
```

`ServiceCollectionExtensions.cs`, logo depois de `services.AddScoped<PurchaseInvoicesNfeReturnCreateService>();`:

```csharp
        services.AddScoped<PurchaseInvoicesCounterNoteCreateService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesCounterNoteTests|FullyQualifiedName~PurchaseInvoices|FullyQualifiedName~Nfe" --nologo`
Expected: PASS (inclui os testes antigos de entrada/NF-e).

Run: `dotnet build SiagroB1.sln` → 0 erros.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/Taxes/TaxSnapshot.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsCreateService.cs \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs \
  SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs
git commit  # feat(invoice): gerar a contra-nota a partir da escrituração do produtor
```

---

### Task 4: Travas da contra-nota e da escrituração

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceNfeLock.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsUpdateService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsDeleteService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesReverseConfirmService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCancelService.cs`
- Modify: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeReturnCreateService.cs` (`ValidateOriginAsync`)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesCounterNoteLockTests.cs` (novo)

**Interfaces:**
- Consumes: `PurchaseInvoiceCounterNote.EnsureNoValidAsync`, `PurchaseInvoicesCounterNoteTests.Service`, `CounterNoteSeed.SeedAsync` (Task 3).
- Produces: `PurchaseInvoiceNfeLock.RestoreCounterNoteHeader(EntityEntry<PurchaseInvoice>)`, `PurchaseInvoiceNfeLock.RestoreCounterNoteLine(EntityEntry<PurchaseInvoiceItem>)`, `PurchaseInvoiceNfeLock.EnsureLinesRemovable(PurchaseInvoice)`.

- [ ] **Step 1: Write the failing tests** — `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesCounterNoteLockTests.cs` (e `git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Travas da contra-nota e da escrituração com contra-nota (spec 2026-10-10 §7, D9, D10).</summary>
public class PurchaseInvoicesCounterNoteLockTests
{
    private const string LinesMessage =
        "A contra-nota tem os mesmos itens da escrituração: não é possível incluir nem excluir item.";

    private static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Producer, PurchaseInvoice Note)> SeedAsync()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var created = await PurchaseInvoicesCounterNoteTests.Service(scenario).ExecuteAsync(producer.Key, "tester");
        scenario.Db.Context.ChangeTracker.Clear();

        return (scenario, producer, await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key));
    }

    private static PurchaseInvoicesTaxApplyService Apply(PurchaseNfeScenario scenario) =>
        TaxTestServices.PurchaseApply(scenario.Db, PurchaseNfeTestSeed.Partners());

    [Fact]
    public async Task Update_keeps_what_came_from_the_producer_and_saves_the_transport()
    {
        var (scenario, _, note) = await SeedAsync();
        var incoming = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, note.Key);
        var originalCondition = incoming.PaymentConditionCode;
        incoming.CardCode = "OUTRO";
        incoming.PaymentConditionCode = 999;
        incoming.GrossWeight = 2000m;
        incoming.NetWeight = 1900m;
        var trigo = incoming.Items.Single(i => i.ItemCode == "TRIGO");
        trigo.Quantity = 1m;
        trigo.UnitPrice = 9m;

        await new PurchaseInvoicesUpdateService(scenario.Db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), Apply(scenario))
            .ExecuteAsync(note.Key, incoming, "tester");

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, note.Key);
        Assert.Equal((PurchaseNfeTestSeed.Supplier, originalCondition), (saved.CardCode, saved.PaymentConditionCode));
        Assert.Equal((2000m, 1900m), (saved.GrossWeight, saved.NetWeight));
        var line = saved.Items.Single(i => i.ItemCode == "TRIGO");
        Assert.Equal((1000m, 1.5m), (line.Quantity, line.UnitPrice));
        Assert.Equal(note.Items.Single(i => i.ItemCode == "TRIGO").IcmsOperationValue, line.IcmsOperationValue);
        Assert.Equal(0m, saved.TotalFunrural);
    }

    [Fact]
    public async Task Line_patch_is_restored()
    {
        var (scenario, _, note) = await SeedAsync();
        var line = note.Items.Single(i => i.ItemCode == "TRIGO");
        var patch = new PurchaseInvoiceItem
        {
            Key = line.Key, PurchaseInvoiceKey = note.Key, ItemCode = "MILHO", ItemName = line.ItemName, UnitOfMeasureCode = "KG",
            Quantity = 1m, UnitPrice = 9m, UsageCode = line.UsageCode,
        };

        await new PurchaseInvoicesItemsUpdateService(scenario.Db, new FakeItemService(), Apply(scenario))
            .ExecuteAsync(line.Key!.Value, patch, "tester");

        var saved = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, note.Key)).Items.Single(i => i.Key == line.Key);
        Assert.Equal(("TRIGO", 1000m, 1.5m), (saved.ItemCode, saved.Quantity, saved.UnitPrice));
    }

    [Fact]
    public async Task Lines_cannot_be_added()
    {
        var (scenario, _, note) = await SeedAsync();
        var extra = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = note.Key, ItemCode = "MILHO", ItemName = "MILHO EM GRAOS",
            UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
        };

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(scenario.Db, new FakeItemService(), Apply(scenario)).ExecuteAsync(extra, "tester"));

        Assert.Equal(LinesMessage, e.Message);
    }

    [Fact]
    public async Task Lines_cannot_be_removed()
    {
        var (scenario, _, note) = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsDeleteService(scenario.Db, new TaxCalculationGate(scenario.Db, NfeTestSeed.Config()))
                .ExecuteAsync(note.Items.First().Key!.Value));

        Assert.Equal(LinesMessage, e.Message);
    }

    [Fact]
    public async Task Producer_with_counter_note_cannot_be_reversed()
    {
        var (scenario, producer, note) = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesReverseConfirmService(scenario.Db).ExecuteAsync(producer.Key, "tester"));

        Assert.Equal($"A escrituração tem a contra-nota {note.InvoiceNumber}: cancele ou exclua a contra-nota antes de estornar a confirmação.", e.Message);
    }

    [Fact]
    public async Task Producer_with_counter_note_cannot_be_cancelled()
    {
        var (scenario, producer, note) = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesCancelService(scenario.Db).ExecuteAsync(producer.Key, "tester"));

        Assert.Equal($"A escrituração tem a contra-nota {note.InvoiceNumber}: cancele ou exclua a contra-nota antes de cancelar a escrituração.", e.Message);
    }

    [Fact]
    public async Task Deleting_the_counter_note_frees_the_producer()
    {
        var (scenario, producer, note) = await SeedAsync();

        await new PurchaseInvoicesDeleteService(scenario.Db).ExecuteAsync(note.Key);
        scenario.Db.Context.ChangeTracker.Clear();
        await new PurchaseInvoicesReverseConfirmService(scenario.Db).ExecuteAsync(producer.Key, "tester");

        Assert.Equal(InvoiceStatus.Pending, (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, producer.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Counter_note_is_not_returnable()
    {
        var (scenario, _, note) = await SeedAsync();
        var stored = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == note.Key);
        stored.InvoiceStatus = InvoiceStatus.Confirmed;
        stored.NfeStatus = NfeStatus.Authorized;
        stored.ChaveNFe = new string('3', 44);
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        var request = new PurchaseInvoiceNfeReturnRequest(
            note.Key, [new PurchaseInvoiceNfeReturnItem(note.Items.First().Key!.Value, 1m)], "grão fora do padrão");

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesThirdPartyReturnTests.Returns(scenario, PurchaseNfeTestSeed.Partners()).ExecuteAsync(request, "tester"));

        Assert.Equal("A contra-nota não é devolvida: a devolução parte da escrituração da NF-e do produtor.", e.Message);
    }

    [Fact]
    public async Task Producer_with_counter_note_is_still_returnable()
    {
        var (scenario, producer, _) = await SeedAsync();
        var request = new PurchaseInvoiceNfeReturnRequest(
            producer.Key, [new PurchaseInvoiceNfeReturnItem(producer.Items.Single(i => i.ItemCode == "MILHO").Key!.Value, 100m)],
            "grão fora do padrão");

        var created = await PurchaseInvoicesThirdPartyReturnTests.Returns(scenario, PurchaseNfeTestSeed.Partners())
            .ExecuteAsync(request, "tester");

        Assert.Equal(producer.Key, created.PurchaseInvoiceOriginKey);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesCounterNoteLockTests" --nologo`
Expected: FAIL nos 8 primeiros (atualizações gravam, linhas incluídas/excluídas, estorno/cancelamento passam, devolução da contra-nota passa); `Producer_with_counter_note_is_still_returnable` já pode passar.

- [ ] **Step 3: Write the implementation**

`PurchaseInvoiceNfeLock.cs` — campos e métodos novos (depois de `ReturnLineFields` e de `RestoreReturnLine`, respectivamente):

```csharp
    /// <summary>
    /// Na contra-nota o que veio da escrituração não muda (spec 2026-10-10 §7): só transporte, pesos, volume, datas e
    /// observações são editáveis.
    /// </summary>
    private static readonly string[] CounterNoteHeaderFields =
    [
        nameof(PurchaseInvoice.CardCode), nameof(PurchaseInvoice.CardName), nameof(PurchaseInvoice.BranchCode),
        nameof(PurchaseInvoice.InvoiceType), nameof(PurchaseInvoice.IssuerType), nameof(PurchaseInvoice.PaymentConditionCode),
        nameof(PurchaseInvoice.PurchaseInvoiceOriginKey), nameof(PurchaseInvoice.ProducerInvoiceKey),
    ];

    /// <summary>Na linha da contra-nota nada que veio da escrituração muda (a fotografia fiscal nem é copiada do corpo).</summary>
    private static readonly string[] CounterNoteLineFields =
    [
        .. ItemFiscalFields, nameof(PurchaseInvoiceItem.ItemName), nameof(PurchaseInvoiceItem.PurchaseContractKey),
        nameof(PurchaseInvoiceItem.SalesInvoiceItemKey), nameof(PurchaseInvoiceItem.PurchaseInvoiceItemOriginKey),
        nameof(PurchaseInvoiceItem.ProducerInvoiceItemKey),
    ];

    private const string CounterNoteLinesMessage =
        "A contra-nota tem os mesmos itens da escrituração: não é possível incluir nem excluir item.";
```

```csharp
    public static void RestoreCounterNoteHeader(EntityEntry<PurchaseInvoice> entry)
    {
        if (entry.OriginalValues[nameof(PurchaseInvoice.ProducerInvoiceKey)] is not null)
            NfeLockRules.Restore(entry, CounterNoteHeaderFields);
    }

    public static void RestoreCounterNoteLine(EntityEntry<PurchaseInvoiceItem> entry) =>
        NfeLockRules.Restore(entry, CounterNoteLineFields);

    /// <summary>Excluir linha: as travas da NF-e e a da contra-nota.</summary>
    public static void EnsureLinesRemovable(PurchaseInvoice invoice)
    {
        EnsureLinesChangeable(invoice.NfeStatus);

        if (invoice.ProducerInvoiceKey is not null)
            throw new DefaultException(CounterNoteLinesMessage);
    }
```

Em `EnsureLineCanBeAdded(PurchaseInvoice invoice)`, depois do `if (invoice.IsNfeReturn) throw ...;`:

```csharp

        if (invoice.ProducerInvoiceKey is not null)
            throw new DefaultException(CounterNoteLinesMessage);
```

`PurchaseInvoicesUpdateService.cs`:
- Depois de `PurchaseInvoiceNfeLock.RestoreReturnHeader(entry);` acrescente `PurchaseInvoiceNfeLock.RestoreCounterNoteHeader(entry);`.
- Em `SyncItemsAsync`, troque `PurchaseInvoiceNfeLock.EnsureLinesChangeable(existing.NfeStatus);` (bloco `if (removed.Count > 0)`) por `PurchaseInvoiceNfeLock.EnsureLinesRemovable(existing);`.
- Em `SyncItemsAsync`, logo depois do bloco `if (existing.IsNfeReturn) { ... }` que restaura a linha da devolução:

```csharp
            // Contra-nota: a linha é a da escrituração (spec 2026-10-10 §7).
            if (existing.ProducerInvoiceKey is not null)
            {
                PurchaseInvoiceNfeLock.RestoreCounterNoteLine(lineEntry);
                InvoiceLineChargeRules.Ensure(current);
            }
```

`PurchaseInvoicesItemsUpdateService.cs`, logo depois do bloco `if (invoice.IsNfeReturn) { PurchaseInvoiceNfeLock.RestoreReturnLine(entry); ... }`:

```csharp
        // Contra-nota: a linha é a da escrituração (spec 2026-10-10 §7) — a grade faz PATCH aqui, não no cabeçalho.
        if (invoice.ProducerInvoiceKey is not null)
        {
            PurchaseInvoiceNfeLock.RestoreCounterNoteLine(entry);
            InvoiceLineChargeRules.Ensure(existing);
        }
```

`PurchaseInvoicesItemsDeleteService.cs`: troque `PurchaseInvoiceNfeLock.EnsureLinesChangeable(invoice.NfeStatus);` por `PurchaseInvoiceNfeLock.EnsureLinesRemovable(invoice);`.

`PurchaseInvoicesReverseConfirmService.cs`, logo depois do `if (invoice.InvoiceStatus != InvoiceStatus.Confirmed) throw ...;`:

```csharp

        // A contra-nota referencia a chave desta escrituração (spec 2026-10-10 D10).
        await PurchaseInvoiceCounterNote.EnsureNoValidAsync(db.Context, invoice.Key, "estornar a confirmação");
```

`PurchaseInvoicesCancelService.cs`, no fim de `EnsureBusinessRulesAsync` (depois da recusa "Documento de entrada possui devolução."):

```csharp

        // A contra-nota referencia a chave desta escrituração (spec 2026-10-10 D10).
        await PurchaseInvoiceCounterNote.EnsureNoValidAsync(db.Context, key, "cancelar a escrituração");
```

`PurchaseInvoicesNfeReturnCreateService.cs`, em `ValidateOriginAsync`, logo depois do bloco `if (origin.IssuerType == DocumentIssuerType.ThirdParty) { ... return true; }` (antes da recusa da própria):

```csharp

        // A contra-nota é só documento para a SEFAZ: devolver por ela referenciaria a nossa chave e a SEFAZ-SP rejeita
        // (1194). A devolução parte da escrituração, que referencia a chave do produtor (spec 2026-10-10 D9).
        if (origin.ProducerInvoiceKey is not null)
            throw new DefaultException("A contra-nota não é devolvida: a devolução parte da escrituração da NF-e do produtor.");
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoices|FullyQualifiedName~Nfe" --nologo`
Expected: PASS (os novos e os antigos de entrada/NF-e).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceNfeLock.cs \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsUpdateService.cs \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesItemsDeleteService.cs \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesReverseConfirmService.cs \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCancelService.cs \
  SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeReturnCreateService.cs
git commit  # feat(invoice): travar a contra-nota e a escrituração que a originou
```

---

### Task 5: Emissão com NFref para a chave do produtor

**Files:**
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueContext.cs` (parâmetro novo + record)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceCounterNote.cs` (`LoadIssueOriginAsync`)
- Modify: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs` (`ValidateReadinessAsync`)
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs` (`Build(PurchaseInvoice ...)`)
- Modify: `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs` (comentário de `ReferencedKeys`)
- Test: `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesCounterNoteIssueTests.cs` (novo)

**Interfaces:**
- Consumes: `PurchaseInvoicesCounterNoteTests.Service`, `CounterNoteSeed` (Task 3); `PurchaseInvoicesNfeIssueServiceTests.Issue/ReloadAsync` (existentes).
- Produces: `NfeCounterNoteOrigin(string AccessKey, string? FunruralText)`; `NfeIssueContext.ProducerInvoice` (último parâmetro, padrão `null`); `PurchaseInvoiceCounterNote.LoadIssueOriginAsync(AppDbContext, Guid) : Task<NfeCounterNoteOrigin>`.

- [ ] **Step 1: Write the failing tests** — `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesCounterNoteIssueTests.cs` (e `git add`):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Emissão da contra-nota (spec 2026-10-10 §6): NFref com a chave do produtor e o Funrural da escrituração no infCpl.</summary>
public class PurchaseInvoicesCounterNoteIssueTests
{
    [Fact]
    public async Task Counter_note_references_the_producer_key_and_carries_the_producer_funrural()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var note = await PurchaseInvoicesCounterNoteTests.Service(scenario).ExecuteAsync(producer.Key, "tester");
        scenario.Db.Context.ChangeTracker.Clear();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(note.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        var signed = Assert.Single(sefaz.Sent).Xml;
        Assert.Contains("<tpNF>0</tpNF>", signed);
        Assert.Contains("<finNFe>1</finNFe>", signed);
        Assert.Contains($"<NFref><refNFe>{SupplierNfeXml.AccessKey}</refNFe></NFref>", signed);
        Assert.Contains("Contribuição Social denominada Funrural", signed);

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, note.Key);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal(0m, saved.TotalFunrural);
    }

    [Fact]
    public async Task Producer_no_longer_confirmed_blocks_the_issue_before_the_number()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var note = await PurchaseInvoicesCounterNoteTests.Service(scenario).ExecuteAsync(producer.Key, "tester");
        var context = TestDb.CreateUnitOfWork(scenario.DatabaseName).Context;
        (await context.PurchaseInvoices.SingleAsync(i => i.Key == producer.Key)).InvoiceStatus = InvoiceStatus.Pending;
        await context.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        var reservation = new FakeNfeNumberReservationService(10);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, new FakeNfeSefazClient(), reservation).ExecuteAsync(note.Key, "tester"));

        Assert.Equal("A escrituração da NF-e do produtor precisa estar confirmada e com a chave de 44 dígitos para emitir a contra-nota.", e.Message);
        Assert.Equal(0, reservation.Calls);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesCounterNoteIssueTests" --nologo`
Expected: FAIL — o primeiro por falta de `<NFref>` no XML; o segundo emite em vez de recusar.

Se o primeiro falhar porque a tag sai com namespace/espaços diferentes de `<NFref><refNFe>…</refNFe></NFref>`, ajuste o assert para dois `Assert.Contains` (`"<NFref>"` e `$"<refNFe>{SupplierNfeXml.AccessKey}</refNFe>"`) — não mexa no montador do XML por causa disso.

- [ ] **Step 3: Write the implementation**

`NfeIssueContext.cs` — no `NfeIssueContext`, acrescente o último parâmetro (depois de `NfeReturnOrigin? ReturnOrigin = null`) e documente:

```csharp
    NfeReturnOrigin? ReturnOrigin = null,
    NfeCounterNoteOrigin? ProducerInvoice = null);
```

Acrescente ao `<summary>` dos parâmetros: `/// <param name="ProducerInvoice">Na contra-nota, a escrituração do produtor (NFref e Funrural); nula nos demais.</param>`. No fim do arquivo:

```csharp

/// <summary>
/// A escrituração da NF-e do produtor que a contra-nota referencia (spec 2026-10-10 §6): a chave vai no
/// <c>ide/NFref/refNFe</c> e o Funrural dela no <c>infCpl</c> (a contra-nota não retém).
/// </summary>
public sealed record NfeCounterNoteOrigin(string AccessKey, string? FunruralText);
```

`PurchaseInvoiceCounterNote.cs` — acrescente `using SiagroB1.Application.Services.Nfe;` e o método:

```csharp

    /// <summary>
    /// A escrituração conferida na emissão da contra-nota, antes de reservar o número: precisa continuar confirmada e com
    /// a chave — é ela que vai no NFref.
    /// </summary>
    public static async Task<NfeCounterNoteOrigin> LoadIssueOriginAsync(AppDbContext context, Guid producerInvoiceKey)
    {
        var producer = await context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Key == producerInvoiceKey);

        if (producer is null || producer.InvoiceStatus != InvoiceStatus.Confirmed ||
            producer.ChaveNFe is not { Length: 44 } key || !key.All(char.IsAsciiDigit))
            throw new DefaultException(
                "A escrituração da NF-e do produtor precisa estar confirmada e com a chave de 44 dígitos para emitir a contra-nota.");

        return new NfeCounterNoteOrigin(key, PurchaseInvoiceFunrural.Text(producer));
    }
```

`PurchaseInvoicesNfeIssueService.cs` — substitua `ValidateReadinessAsync` por:

```csharp
    /// <summary>Contra-nota: além do cadastro, a escrituração do produtor (NFref e Funrural, spec 2026-10-10 §6). Antes da reserva.</summary>
    protected override async Task<NfeIssueContext> ValidateReadinessAsync(PurchaseInvoice invoice)
    {
        var producer = invoice.ProducerInvoiceKey is { } producerKey
            ? await PurchaseInvoiceCounterNote.LoadIssueOriginAsync(_db.Context, producerKey)
            : null;
        var context = await readiness.ValidateAsync(invoice);

        return producer is null ? context : context with { ProducerInvoice = producer };
    }
```

(A escrituração é conferida ANTES da prontidão para a mensagem dela não se misturar com as lacunas do cadastro.)

`NfeIssueInputAssembler.cs`, em `Build(PurchaseInvoice invoice, ...)`:
- Troque o `<summary>` do método por: `Entrada própria: ENTRADA normal, destinatário = fornecedor; a contra-nota leva no NFref a chave da escrituração do produtor e o Funrural dela no infCpl (spec 2026-10-10 §6). Devolução de compra: SAÍDA com finalidade 4, o item da entrada no DFeReferenciado, sem pagamento.`
- Troque `IReadOnlyList<string> referencedKeys = returnOrigin is not null ? [returnOrigin.AccessKey] : [];` por:

```csharp
        var producer = returnOrigin is null ? context.ProducerInvoice : null;

        IReadOnlyList<string> referencedKeys = returnOrigin is not null ? [returnOrigin.AccessKey]
            : producer is not null ? [producer.AccessKey]
            : [];
```

- No `new NfeDocumentView(...)`, troque o argumento `PurchaseInvoiceFunrural.Text(invoice),` por `producer is not null ? producer.FunruralText : PurchaseInvoiceFunrural.Text(invoice),`.

`SiagroB1.Fiscal/Nfe/NfeIssueInput.cs` — o comentário de `ReferencedKeys` vira:

```csharp
    /// <summary><c>ide/NFref/refNFe</c> — na devolução, a chave da nota de origem; na contra-nota, a NF-e do produtor escriturada. Só vai ao cabeçalho quando nenhum item tem <see cref="NfeItem.Reference"/> (rejeição 1010).</summary>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Nfe" --nologo` → PASS (o antigo `Authorized_entry_is_saved_and_the_document_confirmed` continua sem `<NFref>`).
Run: `dotnet test SiagroB1.Fiscal.Tests --nologo` → PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/Nfe/NfeIssueContext.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceCounterNote.cs \
  SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs \
  SiagroB1.Fiscal/Nfe/NfeIssueInput.cs
git commit  # feat(invoice): contra-nota referencia a NF-e do produtor no NFref
```

---

### Task 6: Relatórios sem a contra-nota e lista com a escrituração

**Files:**
- Modify: `SiagroB1.Reports/Services/PurchaseInvoicesByPeriodReportService.cs` (~linha 31)
- Modify: `SiagroB1.Reports/Services/PurchaseInvoiceItemsReportService.cs` (~linha 31)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesGetService.cs` (`QueryAll`)
- Test: `SiagroB1.Application.Tests/Reports/PurchaseInvoicesByPeriodReportServiceTests.cs`, `SiagroB1.Application.Tests/Reports/PurchaseInvoiceItemsReportServiceTests.cs`

**Interfaces:**
- Consumes: `PurchaseInvoice.ProducerInvoiceKey` (Task 1); `InvoiceReportSeed.Purchase/PurchaseItem` (existentes).
- Produces: `$expand=ProducerInvoice` funcional na coleção `PurchaseInvoices` (usado pela lista na Task 8).

- [ ] **Step 1: Write the failing tests**

Em `PurchaseInvoicesByPeriodReportServiceTests.cs`:

```csharp
    [Fact]
    public async Task BuildRows_SkipsTheCounterNote()
    {
        // A compra é a escrituração; a contra-nota é só para a SEFAZ (spec 2026-10-10 D8).
        var db = TestDb.CreateUnitOfWork();
        var producer = Purchase("PROD");
        var note = Purchase("CN");
        note.ProducerInvoiceKey = producer.Key;
        db.Context.PurchaseInvoices.AddRange(producer, note);
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Equal(new[] { "PROD" }, rows.Select(r => r.InternalNumber));
    }
```

Em `PurchaseInvoiceItemsReportServiceTests.cs`:

```csharp
    [Fact]
    public async Task BuildRows_SkipsTheCounterNoteLines()
    {
        // A compra é a escrituração; a contra-nota é só para a SEFAZ (spec 2026-10-10 D8).
        var db = TestDb.CreateUnitOfWork();
        var producer = Purchase("1", new DateTime(2026, 7, 10), items: [PurchaseItem("1", "TRIGO EM GRÃOS")]);
        var note = Purchase("2", new DateTime(2026, 7, 10), items: [PurchaseItem("1", "TRIGO EM GRÃOS")]);
        note.ProducerInvoiceKey = producer.Key;
        db.Context.PurchaseInvoices.AddRange(producer, note);
        await Save(db);

        var rows = await Service(db).BuildRowsAsync(Request());

        Assert.Single(rows);
    }
```

Se `InvoiceReportSeed.Purchase` não preencher `Key`, atribua `producer.Key = Guid.NewGuid()` antes de usá-lo (abra `Support/InvoiceReportSeed.cs` linha 58 e confira).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SkipsTheCounterNote" --nologo`
Expected: FAIL (2 linhas/documentos em vez de 1).

- [ ] **Step 3: Write the implementation**

`PurchaseInvoicesByPeriodReportService.cs`, logo depois de `.Where(x => statuses.Contains(x.InvoiceStatus));`:

```csharp

        // A contra-nota é só documento para a SEFAZ: a compra é a escrituração (spec 2026-10-10 D8).
        query = query.Where(x => x.ProducerInvoiceKey == null);
```

`PurchaseInvoiceItemsReportService.cs`, logo depois de `.Where(i => statuses.Contains(i.PurchaseInvoice!.InvoiceStatus));`:

```csharp

        // A contra-nota é só documento para a SEFAZ: a compra é a escrituração (spec 2026-10-10 D8).
        query = query.Where(i => i.PurchaseInvoice!.ProducerInvoiceKey == null);
```

(Se a variável não se chamar `query` ou o encadeamento for diferente, encaixe o `.Where` no mesmo encadeamento, antes dos filtros opcionais.)

`PurchaseInvoicesGetService.cs`, em `QueryAll()`, antes de `.AsNoTracking();`:

```csharp
            // A lista mostra "Contra-nota de" pelo $expand=ProducerInvoice: sem o Include a navegação volta nula
            // (mesma armadilha do SalesInvoice acima).
            .Include(x => x.ProducerInvoice)
```

NÃO mexa em `GetByIdAsync` (é o que o PATCH usa).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Reports|FullyQualifiedName~PurchaseInvoices" --nologo`
Expected: PASS.

Run: `dotnet test SiagroB1.Application.Tests --nologo` (suíte inteira) e `dotnet build SiagroB1.sln` → tudo verde, 0 erros. Anote a contagem de testes para o relatório final.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Reports/Services/PurchaseInvoicesByPeriodReportService.cs SiagroB1.Reports/Services/PurchaseInvoiceItemsReportService.cs \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesGetService.cs \
  SiagroB1.Application.Tests/Reports/PurchaseInvoicesByPeriodReportServiceTests.cs \
  SiagroB1.Application.Tests/Reports/PurchaseInvoiceItemsReportServiceTests.cs
git commit  # feat(reports): relatórios de entrada sem a contra-nota
```

---

### Task 7: Frontend — "Permite contra-nota" na natureza

Repo: `siagro-b1-frontend`. Primeiro: `git switch main && git switch -c feature/producer-counter-note`.

**Files:**
- Modify: `webapp/view/usages/fragments/Form.fragment.xml` (depois do `Input` de `ReturnUsageName`, ~linha 126)
- Modify: `webapp/controller/usages/Add.controller.ts` (rascunho, ~linha 54)

**Interfaces:**
- Consumes: `UsageModel.AllowsCounterNote` (Task 2).

- [ ] **Step 1: Implement**

`Form.fragment.xml`, logo depois de `<Input value="{ReturnUsageName}" editable="false" visible="{ui>/identityEditable}" />`:

```xml
          <!-- Contra-nota a partir da escrituração da NF-e do produtor (spec 2026-10-10 D4): só natureza de entrada, só
               STANDALONE. A contra-nota usa esta mesma natureza e os mesmos tributos da escrituração. -->
          <Label text="Permite contra-nota"
                 visible="{= ${ui>/identityEditable} === true &amp;&amp; ${path: 'Direction', targetType: 'any'} === 'Incoming' }" />
          <CheckBox selected="{AllowsCounterNote}"
                    visible="{= ${ui>/identityEditable} === true &amp;&amp; ${path: 'Direction', targetType: 'any'} === 'Incoming' }" />
```

`Add.controller.ts`, no objeto do rascunho, depois de `ReturnUsageName: null,`:

```ts
			AllowsCounterNote: false,
```

- [ ] **Step 2: Verify**

Run (de `siagro-b1-frontend/`): `yarn ts-typecheck` → sem erros; `yarn lint` → sem erros.

- [ ] **Step 3: Commit**

```bash
git add webapp/view/usages/fragments/Form.fragment.xml webapp/controller/usages/Add.controller.ts
git commit  # feat(master-data): marcar a natureza de entrada que permite contra-nota
```

---

### Task 8: Frontend — "Gerar contra-nota", vínculos e travas no Documento de Entrada

**Files:**
- Modify: `webapp/helpers/PurchaseInvoiceNfeHelpers.ts`
- Modify: `webapp/test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts`
- Modify: `webapp/model/ServerRoutes.ts` (perto de `purchaseInvoicesCreateNfeReturn`, ~linha 200)
- Modify: `webapp/controller/purchaseInvoices/BaseController.ts` (`refreshNfeMode` + handlers de link)
- Modify: `webapp/controller/purchaseInvoices/Detail.controller.ts`
- Modify: `webapp/view/purchaseInvoices/Detail.view.xml` (rodapé)
- Modify: `webapp/view/purchaseInvoices/fragments/Form.fragment.xml`
- Modify: `webapp/view/purchaseInvoices/fragments/Items.fragment.xml`
- Modify: `webapp/view/purchaseInvoices/Edit.view.xml` (botões Incluir/Excluir Item)
- Modify: `webapp/view/purchaseInvoices/Main.view.xml` (coluna + `$expand`)

**Interfaces:**
- Consumes: action `PurchaseInvoicesCreateCounterNote` (Task 3); `ProducerInvoiceKey`/`ProducerInvoice` no EDM (Task 1); `$expand=ProducerInvoice` (Task 6).
- Produces: `canCreateCounterNote(doc, taxLocked, hasValidCounterNote)`, `isValidCounterNote(note)`, `counterNoteText(note)`; propriedades do modelo `ui`: `/isCounterNote` (bool), `/canCreateCounterNote` (bool), `/counterNote` e `/producerInvoice` (`{ key: string; text: string }`).

- [ ] **Step 1: Write the failing QUnit tests** — em `PurchaseInvoiceNfeHelpers.qunit.ts`, inclua `canCreateCounterNote, counterNoteText, isValidCounterNote` no `import` e acrescente no fim:

```ts
const producer: PurchaseNfeState = {
	InvoiceStatus: "Confirmed", InvoiceType: "Normal", IssuerType: "ThirdParty", IsNfeReturn: false, NfeStatus: "None",
	ChaveNFe: "35261011222333000181550010000007891000067823", TaxDocumentKind: "Nfe",
};

QUnit.test("gera contra-nota da escrituração NF-e confirmada, com a chave e sem contra-nota válida", function (assert) {
	assert.strictEqual(canCreateCounterNote(producer, true, false), true);
	assert.strictEqual(canCreateCounterNote(producer, true, true), false);
	assert.strictEqual(canCreateCounterNote(producer, false, false), false);
	assert.strictEqual(canCreateCounterNote({ ...producer, TaxDocumentKind: "Other" }, true, false), false);
	assert.strictEqual(canCreateCounterNote({ ...producer, InvoiceStatus: "Pending" }, true, false), false);
	assert.strictEqual(canCreateCounterNote({ ...producer, IssuerType: "Own" }, true, false), false);
	assert.strictEqual(canCreateCounterNote({ ...producer, ChaveNFe: "" }, true, false), false);
});

QUnit.test("contra-nota válida: não cancelada e NF-e fora de cancelada, inutilizada ou denegada", function (assert) {
	assert.strictEqual(isValidCounterNote({ InvoiceStatus: "Pending", NfeStatus: "None" }), true);
	assert.strictEqual(isValidCounterNote({ InvoiceStatus: "Pending", NfeStatus: "Rejected" }), true);
	assert.strictEqual(isValidCounterNote({ InvoiceStatus: "Confirmed", NfeStatus: "Authorized" }), true);
	assert.strictEqual(isValidCounterNote({ InvoiceStatus: "Cancelled", NfeStatus: "Rejected" }), false);
	assert.strictEqual(isValidCounterNote({ InvoiceStatus: "Confirmed", NfeStatus: "Cancelled" }), false);
	assert.strictEqual(isValidCounterNote({ InvoiceStatus: "Pending", NfeStatus: "Voided" }), false);
	assert.strictEqual(isValidCounterNote({ InvoiceStatus: "Pending", NfeStatus: "Denied" }), false);
});

QUnit.test("a contra-nota autorizada não abre o Devolver", function (assert) {
	const authorized: PurchaseNfeState = { ...pending, InvoiceStatus: "Confirmed", NfeStatus: "Authorized" };
	assert.strictEqual(canReturnPurchase(authorized, true), true);
	assert.strictEqual(canReturnPurchase({ ...authorized, ProducerInvoiceKey: "a1b2" }, true), false);
});

QUnit.test("texto do vínculo da contra-nota", function (assert) {
	assert.strictEqual(counterNoteText({ InvoiceNumber: "DE000123", TaxDocumentNumber: "000000010", NfeStatus: "Authorized" }),
		"DE000123 — NF-e 10 Autorizada");
	assert.strictEqual(counterNoteText({ InvoiceNumber: "DE000124", TaxDocumentNumber: null, NfeStatus: "None" }),
		"DE000124 — NF-e não emitida");
});
```

- [ ] **Step 2: Run to verify they fail**

Run: `yarn ts-typecheck` → FAIL (funções e campos inexistentes).

- [ ] **Step 3: Implement the helpers** — em `PurchaseInvoiceNfeHelpers.ts`:

No tipo `PurchaseNfeState`, acrescente:

```ts
	TaxDocumentKind?: string;
	/** Preenchido = contra-nota (spec 2026-10-10): não é devolvida; a devolução parte da escrituração. */
	ProducerInvoiceKey?: string;
```

Troque `canReturnPurchase` por:

```ts
/** "Devolver": entrada própria Normal, confirmada, com NF-e autorizada — e que não seja contra-nota (spec 2026-10-10 D9). */
export function canReturnPurchase(doc: PurchaseNfeState, nfeMode: boolean): boolean {
	return nfeMode === true && doc.InvoiceType === "Normal" && doc.IsNfeReturn !== true && !doc.ProducerInvoiceKey &&
		doc.InvoiceStatus === "Confirmed" && doc.NfeStatus === "Authorized";
}
```

Acrescente depois de `canReturnThirdPartyPurchase`:

```ts
/**
 * "Gerar contra-nota" (spec 2026-10-10 §5): escrituração NF-e do produtor (terceiro, Normal, confirmada, chave de 44) na
 * filial que emite pelo Siagro, sem contra-nota válida. A flag "Permite contra-nota" de cada natureza quem confere é o
 * servidor.
 */
export function canCreateCounterNote(doc: PurchaseNfeState, taxLocked: boolean, hasValidCounterNote: boolean): boolean {
	return canReturnThirdPartyPurchase(doc, taxLocked) && doc.TaxDocumentKind === "Nfe" && hasValidCounterNote !== true;
}

/** Contra-nota que ainda trava a escrituração: não cancelada e com a NF-e fora de cancelada, inutilizada ou denegada (§7). */
export function isValidCounterNote(note: { InvoiceStatus: string; NfeStatus: string }): boolean {
	return note.InvoiceStatus !== "Cancelled" && !["Cancelled", "Voided", "Denied"].includes(note.NfeStatus);
}

const NFE_STATUS_TEXT: Record<string, string> = {
	Processing: "em processamento", Authorized: "Autorizada", Rejected: "Rejeitada", Denied: "Denegada",
	Cancelled: "Cancelada", Voided: "Inutilizada",
};

/** "DE000123 — NF-e 10 Autorizada"; sem número, "DE000123 — NF-e não emitida". */
export function counterNoteText(note: { InvoiceNumber: string; TaxDocumentNumber: string; NfeStatus: string }): string {
	const number = note.TaxDocumentNumber ? String(Number(note.TaxDocumentNumber)) : "";
	const status = NFE_STATUS_TEXT[note.NfeStatus];

	return number && status ? `${note.InvoiceNumber} — NF-e ${number} ${status}` : `${note.InvoiceNumber} — NF-e não emitida`;
}
```

(Se o lint reclamar do `null` em `TaxDocumentNumber: null` no teste, mude o tipo do parâmetro para `TaxDocumentNumber?: string | null` com o mesmo `eslint-disable-next-line @typescript-eslint/no-redundant-type-constituents` usado no arquivo.)

- [ ] **Step 4: Route** — `ServerRoutes.ts`, depois de `purchaseInvoicesCreateNfeReturn: ...,`:

```ts
  purchaseInvoicesCreateCounterNote: '/PurchaseInvoicesCreateCounterNote(...)',
```

- [ ] **Step 5: BaseController** — em `refreshNfeMode`:
- No bloco `if (reset || !oContext)`, acrescente `uiModel.setProperty("/isCounterNote", false);`.
- Depois de `const paymentConditionCode = ...;`: `const producerInvoiceKey = await oContext.requestProperty("ProducerInvoiceKey") as string;`
- Depois de `uiModel.setProperty("/nfeReturn", isNfeReturn);`: `uiModel.setProperty("/isCounterNote", !!producerInvoiceKey);`
- No `catch`, acrescente `uiModel.setProperty("/isCounterNote", false);`.

E acrescente os handlers dos links (o fragmento `Form` é o mesmo no Add/Edit/Detail):

```ts
  /** Link "Contra-nota" na escrituração. */
  onOpenCounterNote() {
    const key = (this.getModel("ui") as JSONModel).getProperty("/counterNote/key") as string;
    if (key) {
      this.navTo("purchaseInvoicesDetail", { id: key });
    }
  }

  /** Link "Escrituração" na contra-nota. */
  onOpenProducerInvoice() {
    const key = (this.getModel("ui") as JSONModel).getProperty("/producerInvoice/key") as string;
    if (key) {
      this.navTo("purchaseInvoicesDetail", { id: key });
    }
  }
```

- [ ] **Step 6: Detail controller**

Imports: acrescente `canCreateCounterNote, counterNoteText, isValidCounterNote` ao import de `PurchaseInvoiceNfeHelpers`, e `import Filter from "sap/ui/model/Filter";` / `import FilterOperator from "sap/ui/model/FilterOperator";`.

No `routeMatched` (onde hoje há `uiModel.setProperty("/canReturn", false);`), acrescente:

```ts
    uiModel.setProperty("/canCreateCounterNote", false);
    uiModel.setProperty("/counterNote", { key: "", text: "" });
    uiModel.setProperty("/producerInvoice", { key: "", text: "" });
```

Em `refreshDetailNfe`, troque o trecho final (as duas linhas `uiModel.setProperty("/canIssueNfe"...)` e `("/canReturn"...)`) por:

```ts
    const taxLocked = uiModel.getProperty("/taxLocked") === true;
    const hasValidCounterNote = state ? await this.refreshCounterNoteLinks(oContext, state) : false;

    if (this.getView().getBindingContext() !== oContext) {
      return;
    }

    uiModel.setProperty("/canIssueNfe", !!state && canIssuePurchaseNfe(state, nfeMode));
    uiModel.setProperty("/canReturn", !!state && (canReturnPurchase(state, nfeMode) ||
      canReturnThirdPartyPurchase(state, taxLocked)));
    uiModel.setProperty("/canCreateCounterNote", !!state && canCreateCounterNote(state, taxLocked, hasValidCounterNote));
```

E acrescente os métodos:

```ts
  /**
   * Vínculos da contra-nota (spec 2026-10-10 §9): na contra-nota, a escrituração de origem; na escrituração, a contra-nota
   * válida. Devolve se há contra-nota válida (esconde o "Gerar contra-nota").
   */
  private async refreshCounterNoteLinks(oContext: Context, state: PurchaseNfeState): Promise<boolean> {
    const uiModel = this.getModel("ui") as JSONModel;
    const model = oContext.getModel() as ODataModel;
    uiModel.setProperty("/counterNote", { key: "", text: "" });
    uiModel.setProperty("/producerInvoice", { key: "", text: "" });

    if (state.ProducerInvoiceKey) {
      const producer = await model.bindContext(`/PurchaseInvoices(${state.ProducerInvoiceKey})`, undefined,
        { $select: "Key,TaxDocumentNumber,TaxDocumentSeries" }).requestObject() as { TaxDocumentNumber: string; TaxDocumentSeries: string };
      uiModel.setProperty("/producerInvoice", {
        key: state.ProducerInvoiceKey, text: `NF ${producer.TaxDocumentNumber} série ${producer.TaxDocumentSeries}`,
      });
      return false;
    }

    if (state.IssuerType !== "ThirdParty") {
      return false;
    }

    const key = oContext.getProperty("Key") as string;
    const notes = await model.bindList("/PurchaseInvoices", undefined, undefined,
      [new Filter("ProducerInvoiceKey", FilterOperator.EQ, key)],
      { $select: "Key,InvoiceNumber,TaxDocumentNumber,InvoiceStatus,NfeStatus" }).requestContexts(0, 50);
    const valid = notes.map((ctx) => ctx.getObject() as {
      Key: string; InvoiceNumber: string; TaxDocumentNumber: string; InvoiceStatus: string; NfeStatus: string;
    }).find((note) => isValidCounterNote(note));

    if (valid) {
      uiModel.setProperty("/counterNote", { key: valid.Key, text: counterNoteText(valid) });
    }

    return !!valid;
  }

  /** Cria a contra-nota da escrituração e abre a tela dela, onde fica o "Emitir NF-e". */
  async onCreateCounterNote() {
    const ctx = this.getView().getBindingContext() as Context;
    if (!ctx || !(await confirmDialog(
      "Gerar a contra-nota desta escrituração ? Ela sai com a mesma natureza e os mesmos tributos, e referencia a chave da NF-e do produtor.",
      "Gerar contra-nota ?"))) {
      return;
    }

    this.setBusy(true);
    try {
      const action = (ctx.getModel() as ODataModel).bindContext(ServerRoutes.purchaseInvoicesCreateCounterNote);
      action.setParameter("Key", ctx.getProperty("Key"));
      await action.invoke();

      const key = action.getBoundContext().getProperty("value") as string;
      MessageToast.show("Contra-nota criada. Confira o transporte e os pesos e emita a NF-e.", { closeOnBrowserNavigation: false });
      this.navTo("purchaseInvoicesDetail", { id: key });
    } catch {
      // O handler global de mensagens do OData (Component) já mostrou o erro do servidor.
    } finally {
      this.setBusy(false);
    }
  }
```

Confira que `this.setBusy` existe no `BaseController` (é usado por `runNfeAction`); se o nome for outro, use o mesmo que `runNfeAction` usa. Confira também que o `$select` automático do `bindElement` traz `ProducerInvoiceKey` e `TaxDocumentKind` para o `requestObject()` do `state`: o Form (Step 7) e o Detail passam a usá-los; se o `state` vier sem eles, acrescente `ProducerInvoiceKey,TaxDocumentKind` ao `$select` do `bindElement` no `routeMatched`.

- [ ] **Step 7: Views**

`Detail.view.xml` — no rodapé, logo antes de `<Button text="Devolver" .../>`:

```xml
        <Button text="Gerar contra-nota" icon="sap-icon://create-form" visible="{ui>/canCreateCounterNote}" press=".onCreateCounterNote" />
```

`Form.fragment.xml` — logo depois do `</Select>` do campo "Emissão" (o `Select` de `IssuerType`):

```xml

          <!-- Vínculos da contra-nota (spec 2026-10-10 §9): a escrituração mostra a contra-nota válida; a contra-nota mostra
               a escrituração do produtor de que nasceu. -->
          <Label text="Contra-nota" visible="{= !!${ui>/counterNote/key} }" />
          <Link text="{ui>/counterNote/text}" visible="{= !!${ui>/counterNote/key} }" press=".onOpenCounterNote" />
          <Label text="Escrituração do produtor" visible="{= !!${ui>/producerInvoice/key} }" />
          <Link text="{ui>/producerInvoice/text}" visible="{= !!${ui>/producerInvoice/key} }" press=".onOpenProducerInvoice" />
```

`Form.fragment.xml` — travas do cabeçalho da contra-nota (o servidor já restaura; isto evita o ida e volta):
- No `Select` de `BranchCode` e no `Input` de `CardCode` (`editable` e `showValueHelp`), troque `${IsNfeReturn} !== true` por `${IsNfeReturn} !== true &amp;&amp; !${path: 'ProducerInvoiceKey', targetType: 'any'}`.
- No `Input` de `PaymentConditionCode`, troque `editable="{ui>/editable}"` por `editable="{= ${ui>/editable} === true &amp;&amp; !${path: 'ProducerInvoiceKey', targetType: 'any'} }"`.

`Items.fragment.xml` — linhas da contra-nota somente leitura:
- Em toda expressão `editable`/`showValueHelp` que hoje é `{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true }`, acrescente `&amp;&amp; ${ui>/isCounterNote} !== true` antes do `}`.
- Em todo `editable="{ui>/editable}"` / `showValueHelp="{ui>/editable}"`, troque por `{= ${ui>/editable} === true &amp;&amp; ${ui>/isCounterNote} !== true }`.
- Confira com `grep -n "ui>/editable" webapp/view/purchaseInvoices/fragments/Items.fragment.xml` que não sobrou nenhum sem o `isCounterNote`.

`Edit.view.xml` — nos botões "Incluir Item" e "Excluir Item", troque `enabled="{= ${ui>/nfeReturn} !== true }"` por `enabled="{= ${ui>/nfeReturn} !== true &amp;&amp; ${ui>/isCounterNote} !== true }"`.

`Main.view.xml`:
- No `rows` da tabela, troque `parameters: { $select: 'NfeStatus,NfeCancelledAt,InvoiceStatus,IssuerType,IsNfeReturn' }` por `parameters: { $select: 'NfeStatus,NfeCancelledAt,InvoiceStatus,IssuerType,IsNfeReturn,ProducerInvoiceKey', $expand: 'ProducerInvoice($select=Key,TaxDocumentNumber)' }`.
- Logo depois da coluna "Emissão própria":

```xml
					<!-- Contra-nota (spec 2026-10-10 §9): o número da NF-e do produtor escriturada de que ela nasceu. -->
					<t:Column label="Contra-nota de" width="9rem" hAlign="Center">
						<t:template>
							<Text text="{ProducerInvoice/TaxDocumentNumber}" wrapping="false" />
						</t:template>
					</t:Column>
```

- [ ] **Step 8: Verify**

Run (de `siagro-b1-frontend/`): `yarn ts-typecheck` → sem erros; `yarn lint` → sem erros.
Rode os QUnit pela página (ver Global Constraints) com `yarn start` → os 4 testes novos e os antigos de `PurchaseInvoiceNfeHelpers` verdes.

- [ ] **Step 9: Commit**

```bash
git add webapp/helpers/PurchaseInvoiceNfeHelpers.ts webapp/test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts webapp/model/ServerRoutes.ts \
  webapp/controller/purchaseInvoices/BaseController.ts webapp/controller/purchaseInvoices/Detail.controller.ts \
  webapp/view/purchaseInvoices/Detail.view.xml webapp/view/purchaseInvoices/fragments/Form.fragment.xml \
  webapp/view/purchaseInvoices/fragments/Items.fragment.xml webapp/view/purchaseInvoices/Edit.view.xml webapp/view/purchaseInvoices/Main.view.xml
git commit  # feat(invoice): gerar a contra-nota pela tela da escrituração do produtor
```

---

### Task 9: Verificação ponta a ponta (CEAGUI, homologação)

Sem código novo, salvo correções do que a verificação achar (cada correção vira commit próprio no repo certo, com teste quando der).

- [ ] **Step 1: Aplicar a migration SÓ no `CEAGUI_SIAGRO_DEV`**

De `siagro-b1-backend/`:
`$env:ASPNETCORE_ENVIRONMENT='Ceagui-Development'; dotnet ef database update --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext`
Confira na saída que o banco é o `CEAGUI_SIAGRO_DEV` e que só `AddPurchaseInvoiceCounterNote` foi aplicada. Nenhum outro ambiente.

- [ ] **Step 2: Subir a stack** — Web, Gateway e Reports com `--launch-profile ceagui`; frontend `yarn start:dev` (ver memória "Subir a stack local"). Anote os PIDs para encerrar depois.

- [ ] **Step 3: Pelo caminho do usuário, no navegador**
1. Naturezas: abrir a natureza de compra de produtor (entrada) → marcar "Permite contra-nota" → salvar → reabrir e conferir marcado. Numa natureza de saída o campo não aparece.
2. Documento de Entrada → Incluir → Importar XML de `C:\Projetos\SiagroB1\nfe-homologacao-testes\fornecedor-ftestepj-nfe-791.xml` (se a chave já estiver usada por um documento não cancelado, use outro XML de fornecedor fictício da mesma pasta) → natureza com a flag em cada linha → salvar → Confirmar.
3. No Detail da escrituração: botão "Gerar contra-nota" visível → gerar → a tela abre na contra-nota: Própria, Pendente, link "Escrituração do produtor", linhas e tributos iguais, Funrural zerado, sem "Devolver".
4. Editar a contra-nota: produto/quantidade/natureza/condição travados; informar pesos → salvar.
5. Emitir NF-e em homologação. Autorizada → baixar o XML e conferir `<NFref><refNFe>` com a chave do fornecedor e o texto do Funrural no `infCpl`. Rejeitada por causa da chave fictícia referenciada → registrar código e motivo e NÃO insistir com chave real (spec §11).
6. Voltar à escrituração: link "Contra-nota: DE… — NF-e …", sem "Gerar contra-nota"; Estornar/Cancelar recusados com a mensagem da contra-nota; "Devolver" disponível e o diálogo abre.
7. Lista de Documentos de Entrada: coluna "Contra-nota de" com o número da NF do produtor na linha da contra-nota.
8. Relatório "Documentos de entrada por período" do dia: só a escrituração aparece.

- [ ] **Step 4: Encerrar** os processos pelos PIDs anotados. Relatar: contagem de testes (Application/Fiscal/QUnit), resultado de cada passo do Step 3, códigos da SEFAZ, e os documentos de teste que ficaram no `CEAGUI_SIAGRO_DEV`.
