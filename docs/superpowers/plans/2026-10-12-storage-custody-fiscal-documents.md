# Notas fiscais de custódia em armazém próprio — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Amarrar cada romaneio de lote de armazenagem de terceiro (somos o depositário) à cadeia fiscal que ele exige —
entrada para beneficiamento → devolução simbólica → entrada para armazenagem; retorno na retirada; retorno simbólico
(e entrada no destino) na transferência — gerando as notas próprias por ação no romaneio ou aceitando a escrituração
da NF-e do depositante, com seção no romaneio e relatório de pendências. Só STANDALONE.

**Architecture:** Nenhuma entidade nova de "cadeia". O papel mora na natureza (`USAGE_EFFECTS.StorageCustodyRole`);
o Documento de Entrada/Saída ganha `CustodyStorageTransactionKey` (FK Restrict para o romaneio) e `CustodyRole`
(gravado SÓ pela guarda, a partir da natureza das linhas). As etapas esperadas por romaneio são derivadas por regras
puras no Domain (`StorageCustodyRules`) sobre uma projeção compartilhada no Infra (`StorageCustodyQuery`), lida pela
Web (seção do romaneio) e pelo Reports (PDF) — a mesma regra nos dois lados, como o `RoyaltyBalanceQuery`. Uma guarda
estática (`StorageCustodyGuard`) é plantada nos pontos de gravação, confirmação, emissão, cancelamento e estorno.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes InMemory + `ToQueryString` em SqlServer), OData v4
(Microsoft.AspNetCore.OData), FastReport.OpenSource, xUnit; OpenUI5 + TypeScript, QUnit.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-12-storage-custody-fiscal-documents-design.md`

## Global Constraints

- Somente STANDALONE: a action nova e o relatório recusam em SAPB1 com `"Disponível somente no modo STANDALONE."`
  (`ErpMode.IsStandalone(IConfiguration)`, namespace `SiagroB1.Infra`); a natureza recusa papel ≠ None no
  `SAP/UsageService`; as telas escondem tudo em SAPB1.
- Identificadores em inglês; textos para o usuário (mensagens, rótulos) em pt-BR.
- Branch `feature/storage-custody-fiscal-documents` nos DOIS repos (o backend já existe com a spec; criar o do
  frontend a partir de `main`). Conferir o branch antes de cada commit. **Nunca push, nunca merge.**
- Commits: `tipo(escopo): descrição pt-BR`, tipos `feat|fix|refactor|test|docs|chore`, escopos fechados (usar
  `storage`, `invoice`, `master-data`, `reports`). Commit com migration leva trailer `DB: <NomeDaMigration>`.
  Terminar a mensagem com:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ
  ```
- Todo arquivo novo: `git add` logo após criar.
- Todo serviço novo da Application: registrar à mão em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
  (`services.AddScoped<T>()`). Serviços do Reports são registrados por scan (nada a fazer).
- Toda função OData: rota declarada à mão no controller (`[HttpGet("odata/Nome(Key={key})")]`). Parâmetro decimal de
  action é `double` no EDM; parâmetro enum de action vai como `string` (nome do membro) e é convertido no controller.
- FKs novas com `DeleteBehavior.Restrict`. Não trocar por `NoAction` (não são intercambiáveis no EF).
- "Nota ativa" = `InvoiceStatus != Cancelled` (Pendente conta).
- Gate do backend: `dotnet build SiagroB1.sln` sem erros e `dotnet test SiagroB1.Application.Tests` verde.
- Gate do frontend: `yarn ts-typecheck` + `yarn lint` + QUnit sem gate de cobertura
  (`npx ui5 serve --port 8090` de pé e
  `npx ui5-test-runner --url "http://localhost:8090/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests" --report-dir <scratchpad>`).
  `yarn test` NUNCA passa (gate de cobertura irreal) — não usar como critério. A porta 8080 costuma estar com o
  EfisCloud.
- Migrations geradas com `ASPNETCORE_ENVIRONMENT=Ceagui-Development` (nunca o perfil `db-migration`) e aplicadas SÓ
  no `CEAGUI_SIAGRO_DEV` / `CEAGUI_SIAGRO_COMMON_DEV`.

## Review Focus

1. **Transferência cancelada** — o cancelamento cria romaneios de contrapartida (Recepção na origem, Expedição no
   destino) com o MESMO `OwnershipTransferKey`. Nenhum romaneio de transferência cancelada pode esperar nota (teste na
   Task 3: `Romaneios_of_a_cancelled_transfer_expect_nothing`).
2. **Bruto ≠ líquido** — todo seed antigo grava `NetWeight = GrossWeight`; a entrada para beneficiamento é pelo bruto e
   a armazenagem pelo líquido (teste na Task 6: `Quantities_follow_gross_gross_net`).
3. **Devolução simbólica herdando a escrituração do depositante com peso diferente da balança** — a quantidade é a da
   nota do papel 1, não a do romaneio (teste na Task 6: `Symbolic_return_takes_the_quantity_of_the_producer_invoice`).
4. **Edição da nota gerada pela tela** — o usuário consegue mudar quantidade/produto/parceiro da nota Pendente; a
   confirmação (saída) e a reserva de número (entrada própria) recusam o que deixou de bater com o romaneio (testes na
   Task 4: `Confirm_refuses_sales_custody_document_with_changed_quantity` e
   `Own_entry_with_changed_quantity_is_refused`).
5. **Linha removida/natureza trocada deixando o cabeçalho com romaneio** — documento com `CustodyStorageTransactionKey`
   e nenhuma linha de custódia é recusado (teste na Task 4: `Removing_the_custody_line_is_refused`).

---

## Mapa de arquivos

**Backend (`siagro-b1-backend/`)**

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Enums/StorageCustodyRole.cs` (novo) | Enum dos 5 papéis |
| `SiagroB1.Domain/Enums/StorageCustodyRules.cs` (novo) | Regras puras: etapas esperadas, quantidade, situação, textos, montagem das etapas |
| `SiagroB1.Domain/Dtos/StorageCustodyDtos.cs` (novo) | Linhas da projeção, etapa e resumo |
| `SiagroB1.Domain/Entities/UsageEffect.cs`, `Models/UsageModel.cs` | + `StorageCustodyRole` |
| `SiagroB1.Domain/Entities/StorageAddress.cs` | + `RequiresProcessing` |
| `SiagroB1.Domain/Entities/PurchaseInvoice.cs`, `SalesInvoice.cs` | + `CustodyStorageTransactionKey`, `CustodyRole` |
| `SiagroB1.Infra/Context/AppDbContext.cs` | FKs Restrict + índices |
| `SiagroB1.Infra/Queries/StorageCustodyQuery.cs` (novo) | Projeção de romaneios e notas ativas (Web + Reports) |
| `SiagroB1.Migrations/AppContext/*_AddStorageCustodyFiscalDocuments.cs` (gerado) | Migration do App |
| `SiagroB1.Migrations/CommonContext/*_AddStorageCustodyReportMenu.cs` (à mão) | Menu do relatório |
| `SiagroB1.Application/Services/UsageEffectWriter.cs`, `UsageService.cs`, `SAP/UsageService.cs` | Papel gravado/lido/validado |
| `SiagroB1.Application/Services/StorageCustody/StorageCustodyGuard.cs` (novo) | Guardas das notas, cancelamento e estorno |
| `SiagroB1.Application/Services/StorageCustody/StorageTransactionsCreateCustodyDocumentService.cs` (novo) | "Gerar nota" |
| `SiagroB1.Application/Services/StorageCustody/StorageTransactionsCustodySummaryService.cs` (novo) | Seção do romaneio |
| `PurchaseInvoicesCreateService`, `PurchaseInvoicesUpdateService`, `PurchaseInvoicesItems{Create,Update,Delete}Service`, `PurchaseInvoicesConfirmService`, `PurchaseInvoicesCancelService`, `PurchaseInvoicesDeleteService` | Guarda na entrada |
| `SalesInvoicesCreateService`, `SalesInvoicesConfirmService`, `SalesInvoicesUpdateService`, `SalesInvoicesCancelService`, `SalesInvoicesDeleteService`, `SiagroB1.Web/Controllers/SalesInvoicesController.cs` | Guarda na saída |
| `StorageTransactionsCancelService`, `StorageTransactionsReverseService`, `OwnershipTransfersCancelService`, `StorageAddressesUpdateService` | Travas de estorno e do lote |
| `Services/Nfe/NfeIssueContext.cs`, `NfeReadinessValidator.cs`, `NfeIssueInputAssembler.cs`, `SalesInvoicesNfeIssueService.cs`, `PurchaseInvoicesNfeIssueService.cs` | Sem pagamento, NFref da devolução simbólica, revalidação antes do número |
| `SiagroB1.Web/Actions/StorageCustody/…Controller.cs`, `SiagroB1.Web/Functions/StorageCustody/…Controller.cs` (novos) | Endpoints |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` | Declarações EDM |
| `SiagroB1.Reports/Dtos/StorageCustodyByPeriodRequest.cs`, `StorageCustodyRowDto.cs`, `Services/StorageCustodyByPeriodReportService.cs`, `Controllers/StorageCustodyByPeriodController.cs`, `Reports/Templates/StorageCustodyByPeriod.frx` (novos) | Relatório |
| `SiagroB1.Application.Tests/Support/StorageCustodySeed.cs` (novo) | Cenário de teste |

**Frontend (`siagro-b1-frontend/`)**

| Arquivo | Responsabilidade |
|---|---|
| `webapp/helpers/StorageCustodyHelpers.ts` (novo) + `webapp/test/unit/helpers/StorageCustodyHelpers.qunit.ts` (novo) | Regras puras da tela |
| `webapp/model/ServerRoutes.ts` | Rotas novas |
| `webapp/view/usages/fragments/Form.fragment.xml`, `webapp/controller/usages/Add.controller.ts` | Papel na custódia |
| `webapp/view/storageAddresses/fragments/Form.fragment.xml`, `webapp/controller/storageAddresses/Add.controller.ts` | Passa por beneficiamento |
| `webapp/view/storageTransactions/Detail.view.xml`, `controller/storageTransactions/Detail.controller.ts`, `view/storageTransactions/fragments/CustodyDocumentDialog.fragment.xml` (novo) | Seção + "Gerar nota" |
| `webapp/view/purchaseInvoices/fragments/Form.fragment.xml`, `controller/purchaseInvoices/BaseController.ts`, `webapp/dialogs/fragments/StorageCustodyTransactionsSelectDialog.fragment.xml` (novo) | Romaneio na escrituração |
| `webapp/view/salesInvoices/Detail.view.xml`, `controller/salesInvoices/Detail.controller.ts` | Link para o romaneio |
| `webapp/view/reports/storageCustody/Main.view.xml`, `webapp/controller/reports/storageCustody/Main.controller.ts`, `webapp/manifest.json` (novos/alterado) | Tela do relatório |

---

## Task 1: Modelo de dados e migration

**Files:**
- Create: `SiagroB1.Domain/Enums/StorageCustodyRole.cs`
- Modify: `SiagroB1.Domain/Entities/UsageEffect.cs`, `SiagroB1.Domain/Models/UsageModel.cs`, `SiagroB1.Domain/Entities/StorageAddress.cs`, `SiagroB1.Domain/Entities/PurchaseInvoice.cs`, `SiagroB1.Domain/Entities/SalesInvoice.cs`, `SiagroB1.Infra/Context/AppDbContext.cs`
- Generate: `SiagroB1.Migrations/AppContext/<timestamp>_AddStorageCustodyFiscalDocuments.cs`
- Test: `SiagroB1.Application.Tests/StorageCustody/StorageCustodyModelTests.cs` (novo)

**Interfaces:**
- Produces: `enum StorageCustodyRole { None = 0, ProcessingEntry = 1, ProcessingSymbolicReturn = 2, StorageEntry = 3, StorageReturn = 4, StorageSymbolicReturn = 5 }`;
  `UsageEffect.StorageCustodyRole`, `UsageModel.StorageCustodyRole`; `StorageAddress.RequiresProcessing : bool`;
  `PurchaseInvoice.CustodyStorageTransactionKey : Guid?`, `PurchaseInvoice.CustodyRole : StorageCustodyRole`;
  `SalesInvoice.CustodyStorageTransactionKey : Guid?`, `SalesInvoice.CustodyRole : StorageCustodyRole`.
  Sem propriedade de navegação (FK "sombra" de navegação, como as FKs de descarga): nada novo entra no EDM além dos
  escalares.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/StorageCustody/StorageCustodyModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Models;
using SiagroB1.Infra.Context;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.StorageCustody;

/// <summary>Notas de custódia (spec 2026-10-12 §3): FKs Restrict para o romaneio e campos expostos no EDM.</summary>
public class StorageCustodyModelTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Theory]
    [InlineData(typeof(PurchaseInvoice))]
    [InlineData(typeof(SalesInvoice))]
    public void Custody_transaction_key_is_a_restrict_fk(Type invoice)
    {
        using var context = CreateContext();

        var fk = context.Model.FindEntityType(invoice)!
            .GetForeignKeys()
            .Single(x => x.Properties.Single().Name == "CustodyStorageTransactionKey");

        Assert.Equal(typeof(StorageTransaction), fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public void Role_and_processing_flag_are_not_nullable()
    {
        using var context = CreateContext();

        Assert.False(context.Model.FindEntityType(typeof(UsageEffect))!.FindProperty("StorageCustodyRole")!.IsNullable);
        Assert.False(context.Model.FindEntityType(typeof(StorageAddress))!.FindProperty("RequiresProcessing")!.IsNullable);
        Assert.False(context.Model.FindEntityType(typeof(PurchaseInvoice))!.FindProperty("CustodyRole")!.IsNullable);
        Assert.False(context.Model.FindEntityType(typeof(SalesInvoice))!.FindProperty("CustodyRole")!.IsNullable);
    }

    [Fact]
    public void Edm_exposes_the_new_fields()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();
        IEdmEntityType Type(string name) => model.SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == name);

        Assert.NotNull(Type(nameof(PurchaseInvoice)).FindProperty("CustodyStorageTransactionKey"));
        Assert.NotNull(Type(nameof(PurchaseInvoice)).FindProperty("CustodyRole"));
        Assert.NotNull(Type(nameof(SalesInvoice)).FindProperty("CustodyStorageTransactionKey"));
        Assert.NotNull(Type(nameof(StorageAddress)).FindProperty("RequiresProcessing"));
        Assert.NotNull(Type(nameof(UsageModel)).FindProperty("StorageCustodyRole"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageCustodyModelTests"`
Expected: FAIL de compilação (`CustodyStorageTransactionKey`, `StorageCustodyRole` não existem).

- [ ] **Step 3: Implement**

`SiagroB1.Domain/Enums/StorageCustodyRole.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Papel da natureza nas notas de custódia em armazém PRÓPRIO, em que somos o depositário (spec 2026-10-12 §3.1).
/// Espelho de <see cref="StorageDepositRole"/>, que é o caso oposto (nosso grão em armazém de terceiro) — não misturar.
/// Entradas (1 e 3) são Documento de Entrada; devolução e retornos (2, 4 e 5), Documento de Saída. Nenhum mexe em
/// contrato.
/// </summary>
public enum StorageCustodyRole
{
    None = 0,
    /// <summary>Entrada para beneficiamento (CFOP 1.901/2.901).</summary>
    ProcessingEntry = 1,
    /// <summary>Devolução simbólica do beneficiamento (5.902/6.902), com NFref para a entrada.</summary>
    ProcessingSymbolicReturn = 2,
    /// <summary>Entrada (simbólica ou física) para armazenagem (1.905/2.905).</summary>
    StorageEntry = 3,
    /// <summary>Retorno de armazenagem na retirada (5.906/6.906).</summary>
    StorageReturn = 4,
    /// <summary>Retorno simbólico de armazenagem na transferência (5.907/6.907).</summary>
    StorageSymbolicReturn = 5,
}
```

`UsageEffect.cs` — ao fim da classe, depois de `StorageDepositRole`:

```csharp
    /// <summary>
    /// Papel nas notas de custódia em armazém próprio (spec 2026-10-12 §3.1). Mora aqui pelo mesmo motivo do
    /// <see cref="StorageDepositRole"/>: nasce pronto para o modo SAPB1, embora a feature seja só STANDALONE.
    /// </summary>
    public StorageCustodyRole StorageCustodyRole { get; set; }
```

`UsageModel.cs` — logo depois de `StorageDepositRole`:

```csharp
    /// <summary>Papel nas notas de custódia — ver <see cref="Entities.UsageEffect.StorageCustodyRole"/>.</summary>
    public StorageCustodyRole StorageCustodyRole { get; set; }
```

`StorageAddress.cs` — logo depois de `ProcessingCost`:

```csharp
    /// <summary>
    /// O lote passa pelo beneficiamento FISCAL (spec 2026-10-12 §3.2): cada Recepção espera entrada para
    /// beneficiamento, devolução simbólica e entrada para armazenagem; sem ele, só a entrada para armazenagem. Só vale
    /// em lote de terceiro. Travado enquanto houver nota de custódia ativa (StorageAddressesUpdateService).
    /// </summary>
    public bool RequiresProcessing { get; set; }
```

`PurchaseInvoice.cs` — logo depois de `ProducerInvoiceKey`/`ProducerInvoice`:

```csharp
    /// <summary>
    /// Romaneio de lote de armazenagem de terceiro que esta nota de custódia cumpre (spec 2026-10-12 §3.3). Própria
    /// (gerada pela ação no romaneio) ou escrituração da NF-e do depositante, escolhida pelo usuário.
    /// </summary>
    public Guid? CustodyStorageTransactionKey { get; set; }

    /// <summary>
    /// Papel da nota de custódia, copiado da natureza das linhas SÓ pela StorageCustodyGuard — o corpo da API não o
    /// define. Congela o papel mesmo se a natureza for editada depois.
    /// </summary>
    public StorageCustodyRole CustodyRole { get; set; }
```

`SalesInvoice.cs` — logo depois de `ShipmentReleaseKey`/`ShipmentRelease`:

```csharp
    /// <summary>
    /// Romaneio de lote de armazenagem de terceiro desta nota de custódia (devolução simbólica, retorno ou retorno
    /// simbólico — spec 2026-10-12 §3.3). Só o "Gerar nota" do romaneio grava; PUT/PATCH não o move.
    /// </summary>
    public Guid? CustodyStorageTransactionKey { get; set; }

    /// <summary>Papel da nota de custódia, gravado SÓ pela StorageCustodyGuard.</summary>
    public StorageCustodyRole CustodyRole { get; set; }
```

(Os dois arquivos de nota já fazem `using SiagroB1.Domain.Enums;`; confira.)

`AppDbContext.cs` — no fim de `OnModelCreating`, depois do bloco da remessa para depósito:

```csharp
        // Notas de custódia em armazém próprio (spec 2026-10-12 §3.3). Sem navegação (não entra no EDM) e Restrict: o
        // romaneio com nota vinculada não some — o estorno é barrado pela StorageCustodyGuard antes.
        modelBuilder.Entity<PurchaseInvoice>()
            .HasOne<StorageTransaction>().WithMany()
            .HasForeignKey(x => x.CustodyStorageTransactionKey)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SalesInvoice>()
            .HasOne<StorageTransaction>().WithMany()
            .HasForeignKey(x => x.CustodyStorageTransactionKey)
            .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 4: Generate the migration**

Run (de `siagro-b1-backend/`, Bash):

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddStorageCustodyFiscalDocuments \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```

Conferir o `.cs` gerado. Ele deve conter só:
- `AddColumn<int>("StorageCustodyRole", "USAGE_EFFECTS", nullable: false, defaultValue: 0)`;
- `AddColumn<bool>("RequiresProcessing", "STORAGE_ADDRESSES", nullable: false, defaultValue: false)`;
- `CustodyStorageTransactionKey` (Guid nulável) e `CustodyRole` (int, default 0) em `PURCHASE_INVOICES` e
  `SALES_INVOICES`;
- os dois índices e as duas FKs com `ReferentialAction.Restrict`.

Se aparecer alteração alheia, é drift do snapshot: pare e investigue. Faça `git add` dos três arquivos gerados.

Aplicar no banco da CEAGUI dev (Bash):

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageCustodyModelTests"`
Expected: PASS (4 testes).

- [ ] **Step 6: Commit**

```bash
git add -A SiagroB1.Domain SiagroB1.Infra SiagroB1.Migrations SiagroB1.Application.Tests/StorageCustody
git commit -m "feat(storage): modelo das notas de custódia em armazém próprio" -m "A natureza ganha o papel de custódia, o lote diz se passa por beneficiamento fiscal e as notas de entrada e saída apontam o romaneio do lote de terceiro que cumprem, com o papel congelado no cabeçalho." -m "DB: AddStorageCustodyFiscalDocuments" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

## Task 2: Papel da natureza no cadastro

**Files:**
- Modify: `SiagroB1.Application/Services/UsageEffectWriter.cs`, `SiagroB1.Application/Services/UsageService.cs`, `SiagroB1.Application/Services/SAP/UsageService.cs`
- Test: `SiagroB1.Application.Tests/Usages/UsageStorageCustodyRoleTests.cs` (novo)

**Interfaces:**
- Consumes: `StorageCustodyRole`, `UsageModel.StorageCustodyRole` (Task 1).
- Produces: `UsageModel.StorageCustodyRole` preenchido pelos dois `UsageService.QueryAll()`; regras de cadastro.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Usages/UsageStorageCustodyRoleTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>"Papel na custódia" da natureza (spec 2026-10-12 §3.1): vai e volta, casa com a direção e não convive com
/// contrato, remessa para depósito nem contra-nota.</summary>
public class UsageStorageCustodyRoleTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Entry() => new()
    {
        Name = "Entrada para beneficiamento", Direction = UsageDirection.Incoming,
        CfopIncomingInState = "1901", CfopIncomingOutState = "2901", PisCst = "98", CofinsCst = "98",
        RequiresQuantity = true, StorageCustodyRole = StorageCustodyRole.ProcessingEntry,
    };

    private static UsageModel Return() => new()
    {
        Name = "Retorno de armazenagem", Direction = UsageDirection.Outgoing,
        CfopOutgoingInState = "5906", CfopOutgoingOutState = "6906", PisCst = "08", CofinsCst = "08",
        RequiresQuantity = true, StorageCustodyRole = StorageCustodyRole.StorageReturn,
    };

    [Fact]
    public async Task Role_is_saved_and_read_back()
    {
        var name = Guid.NewGuid().ToString();
        var created = await Service(TestDb.CreateUnitOfWork(name)).CreateAsync(Entry());

        var read = await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code);

        Assert.Equal(StorageCustodyRole.ProcessingEntry, read!.StorageCustodyRole);
    }

    [Theory]
    [InlineData(StorageCustodyRole.ProcessingEntry)]
    [InlineData(StorageCustodyRole.StorageEntry)]
    public async Task Entry_roles_must_be_incoming(StorageCustodyRole role)
    {
        var model = Return();
        model.StorageCustodyRole = role;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de entrada de custódia (beneficiamento ou armazenagem) precisa ser de entrada.", e.Message);
    }

    [Theory]
    [InlineData(StorageCustodyRole.ProcessingSymbolicReturn)]
    [InlineData(StorageCustodyRole.StorageReturn)]
    [InlineData(StorageCustodyRole.StorageSymbolicReturn)]
    public async Task Return_roles_must_be_outgoing(StorageCustodyRole role)
    {
        var model = Entry();
        model.StorageCustodyRole = role;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de devolução ou retorno de custódia precisa ser de saída.", e.Message);
    }

    [Fact]
    public async Task Custody_role_cannot_touch_the_contract()
    {
        var model = Entry();
        model.RequiresContract = true;
        model.ContractBalanceEffect = ContractBalanceEffect.Consume;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de custódia (armazenagem) não pode alterar o contrato.", e.Message);
    }

    [Fact]
    public async Task Custody_role_cannot_be_a_deposit_role_too()
    {
        var model = Entry();
        model.StorageDepositRole = StorageDepositRole.Return;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("A natureza tem papel na remessa para depósito ou na custódia, não nos dois.", e.Message);
    }

    [Fact]
    public async Task Custody_role_cannot_allow_counter_note()
    {
        var model = Entry();
        model.AllowsCounterNote = true;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de custódia (armazenagem) não permite contra-nota.", e.Message);
    }

    [Fact]
    public async Task Update_can_clear_the_role()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var created = await Service(db).CreateAsync(Return());

        var model = Return();
        model.StorageCustodyRole = StorageCustodyRole.None;
        await Service(db).UpdateAsync(created.Code, model);

        Assert.Equal(StorageCustodyRole.None,
            (await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code))!.StorageCustodyRole);
    }
}
```

Mais um teste no arquivo que já cobre a recusa do `StorageDepositRole` no modo SAP. Ache-o com
`grep -rln "Sap_mode_refuses_a_deposit_role" SiagroB1.Application.Tests` e copie a construção do serviço SAP usada lá:

```csharp
    [Fact]
    public async Task Sap_mode_refuses_a_custody_role()
    {
        // mesma construção de sapService usada em Sap_mode_refuses_a_deposit_role
        var model = new UsageModel { Name = "X", StorageCustodyRole = StorageCustodyRole.StorageReturn };

        var e = await Assert.ThrowsAsync<DefaultException>(() => sapService.UpdateAsync(1, model));

        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~UsageStorageCustodyRole|FullyQualifiedName~Sap_mode_refuses_a_custody_role"`
Expected: FAIL (o papel volta `None`; nenhuma mensagem é lançada).

- [ ] **Step 3: Implement**

`UsageEffectWriter.ValidateEffects` — acrescentar ao fim do método:

```csharp
        if (model.StorageCustodyRole != StorageCustodyRole.None)
        {
            if (hasEffect)
                throw new DefaultException("Natureza de custódia (armazenagem) não pode alterar o contrato.");

            if (model.StorageDepositRole != StorageDepositRole.None)
                throw new DefaultException("A natureza tem papel na remessa para depósito ou na custódia, não nos dois.");

            if (model.AllowsCounterNote)
                throw new DefaultException("Natureza de custódia (armazenagem) não permite contra-nota.");

            var incoming = model.StorageCustodyRole is StorageCustodyRole.ProcessingEntry or StorageCustodyRole.StorageEntry;

            if (incoming && direction != UsageDirection.Incoming)
                throw new DefaultException(
                    "Natureza de entrada de custódia (beneficiamento ou armazenagem) precisa ser de entrada.");

            if (!incoming && direction != UsageDirection.Outgoing)
                throw new DefaultException("Natureza de devolução ou retorno de custódia precisa ser de saída.");
        }
```

(`direction` já é declarada acima no método, para as regras do `StorageDepositRole`.)

`UsageEffectWriter.WriteAsync` — logo depois de `effect.StorageDepositRole = model.StorageDepositRole;`:

```csharp
        effect.StorageCustodyRole = model.StorageCustodyRole;
```

`UsageService.QueryAll` (STANDALONE) — logo depois da linha `StorageDepositRole = ...`:

```csharp
                    StorageCustodyRole = effect != null ? effect.StorageCustodyRole : StorageCustodyRole.None,
```

`SAP/UsageService.QueryAll` — logo depois da linha `StorageDepositRole = ...`:

```csharp
                StorageCustodyRole = x.effect?.StorageCustodyRole ?? StorageCustodyRole.None,
```

`SAP/UsageService.UpdateAsync` — trocar a condição da recusa do início por:

```csharp
        // Remessa para depósito e custódia em armazém próprio são só STANDALONE por enquanto (specs 2026-10-10 e 2026-10-12).
        if (model.StorageDepositRole != StorageDepositRole.None || model.StorageCustodyRole != StorageCustodyRole.None)
            throw new DefaultException("Disponível somente no modo STANDALONE.");
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~UsageStorageCustodyRole|FullyQualifiedName~Usage"`
Expected: PASS (inclusive os testes antigos de natureza).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application SiagroB1.Application.Tests/Usages
git commit -m "feat(master-data): papel da natureza nas notas de custódia" -m "Entrada para beneficiamento e para armazenagem só em natureza de entrada; devolução simbólica e retornos só em natureza de saída; o papel não mexe em contrato e não convive com a remessa para depósito nem com a contra-nota." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

## Task 3: Regras puras e consulta compartilhada

**Files:**
- Create: `SiagroB1.Domain/Dtos/StorageCustodyDtos.cs`, `SiagroB1.Domain/Enums/StorageCustodyRules.cs`, `SiagroB1.Infra/Queries/StorageCustodyQuery.cs`, `SiagroB1.Application.Tests/Support/StorageCustodySeed.cs`
- Test: `SiagroB1.Application.Tests/StorageCustody/StorageCustodyRulesTests.cs`, `SiagroB1.Application.Tests/StorageCustody/StorageCustodyQueryTests.cs` (novos)

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `enum StorageCustodyStepSituation { Missing = 0, Generated = 1, Authorized = 2, Recorded = 3 }`
  - `class StorageCustodyTransactionRow` (romaneio + lote), `class StorageCustodyDocumentRow` (nota ativa),
    `class StorageCustodyStepDto`, `class StorageCustodySummaryDto` — campos abaixo.
  - `StorageCustodyRules.ExpectedRoles(StorageCustodyTransactionRow) : IReadOnlyList<StorageCustodyRole>`,
    `IsPurchaseRole(role)`, `IsSalesRole(role)`, `ExpectedQuantity(role, row, decimal? processingEntryQuantity) : decimal?`,
    `Situation(StorageCustodyDocumentRow?)`, `RoleText(role)`, `ShortRoleText(role)`, `SituationText(situation)`,
    `BuildSteps(row, IEnumerable<StorageCustodyDocumentRow>) : List<StorageCustodyStepDto>`,
    `NextRole(IEnumerable<StorageCustodyStepDto>) : StorageCustodyRole`, `DocumentNumber(row) : string`.
  - `StorageCustodyQuery.Transactions(AppDbContext) : IQueryable<StorageCustodyTransactionRow>`,
    `PurchaseDocuments(ctx, IReadOnlyCollection<Guid>)`, `SalesDocuments(ctx, IReadOnlyCollection<Guid>)`,
    `ActiveDocumentsAsync(ctx, IReadOnlyCollection<Guid>) : Task<List<StorageCustodyDocumentRow>>`.
  - `StorageCustodySeed` (teste): lote, naturezas 1901/5902/1905/5906/5907, romaneios e notas.

- [ ] **Step 1: Write the seed and the failing tests**

`SiagroB1.Application.Tests/Support/StorageCustodySeed.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Lote de armazenagem de TERCEIRO (somos o depositário) do produtor P0001, as cinco naturezas de custódia e atalhos para
/// romaneios e notas (spec 2026-10-12). Pesos bruto e líquido DIFERENTES por padrão: os seeds antigos gravam iguais e
/// escondem a troca entre eles.
/// </summary>
public sealed class StorageCustodySeed
{
    public const string Depositor = "P0001";
    public const string Item = "SOJA";
    public const string Branch = "01";
    public const string Warehouse = "ARM01";
    public const int ProcessingEntryUsage = 1901;
    public const int SymbolicReturnUsage = 5902;
    public const int StorageEntryUsage = 1905;
    public const int StorageReturnUsage = 5906;
    public const int SymbolicStorageReturnUsage = 5907;
    public const string AccessKey = "35261068583898000101550090000000011990112066";

    private readonly UnitOfWork _db;
    private StorageCustodySeed(UnitOfWork db) => _db = db;

    public required StorageAddress Lot { get; init; }

    public static async Task<StorageCustodySeed> CreateAsync(UnitOfWork db, bool requiresProcessing = true, string lotCode = "L0001")
    {
        if (!db.Context.Branchs.Any(b => b.Code == Branch))
            db.Context.Branchs.Add(new Branch { Code = Branch, BranchName = "Matriz", ShortName = "MTZ", StateCode = "SP" });

        if (!db.Context.Usages.Any(u => u.Code == ProcessingEntryUsage))
        {
            db.Context.Usages.AddRange(
                new Usage { Code = ProcessingEntryUsage, Name = "ENTRADA P/ BENEFICIAMENTO", Direction = UsageDirection.Incoming, CfopIncomingInState = "1901", CfopIncomingOutState = "2901" },
                new Usage { Code = SymbolicReturnUsage, Name = "DEVOLUCAO SIMBOLICA BENEFICIAMENTO", Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5902", CfopOutgoingOutState = "6902" },
                new Usage { Code = StorageEntryUsage, Name = "ENTRADA P/ ARMAZENAGEM", Direction = UsageDirection.Incoming, CfopIncomingInState = "1905", CfopIncomingOutState = "2905" },
                new Usage { Code = StorageReturnUsage, Name = "RETORNO DE ARMAZENAGEM", Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5906", CfopOutgoingOutState = "6906" },
                new Usage { Code = SymbolicStorageReturnUsage, Name = "RETORNO SIMBOLICO DE ARMAZENAGEM", Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5907", CfopOutgoingOutState = "6907" });
            db.Context.UsageEffects.AddRange(
                new UsageEffect { UsageCode = ProcessingEntryUsage, StorageCustodyRole = StorageCustodyRole.ProcessingEntry },
                new UsageEffect { UsageCode = SymbolicReturnUsage, StorageCustodyRole = StorageCustodyRole.ProcessingSymbolicReturn },
                new UsageEffect { UsageCode = StorageEntryUsage, StorageCustodyRole = StorageCustodyRole.StorageEntry },
                new UsageEffect { UsageCode = StorageReturnUsage, StorageCustodyRole = StorageCustodyRole.StorageReturn },
                new UsageEffect { UsageCode = SymbolicStorageReturnUsage, StorageCustodyRole = StorageCustodyRole.StorageSymbolicReturn });
        }

        var lot = new StorageAddress
        {
            Code = lotCode, BranchCode = Branch, Description = "Lote do produtor", CardCode = Depositor, CardName = "PRODUTOR UM",
            ItemCode = Item, ItemName = "SOJA EM GRAOS", WarehouseCode = Warehouse, WarehouseName = "ARMAZEM PROPRIO", UoM = "KG",
            OwnershipType = StorageOwnershipType.ThirdParty, Nature = StorageAddressNature.Regular,
            RequiresProcessing = requiresProcessing, TransactionOrigin = TransactionCode.StorageAddress,
        };
        db.Context.StorageAddresses.Add(lot);
        await db.SaveChangesAsync();

        return new StorageCustodySeed(db) { Lot = lot };
    }

    public static int UsageOf(StorageCustodyRole role) => role switch
    {
        StorageCustodyRole.ProcessingEntry => ProcessingEntryUsage,
        StorageCustodyRole.ProcessingSymbolicReturn => SymbolicReturnUsage,
        StorageCustodyRole.StorageEntry => StorageEntryUsage,
        StorageCustodyRole.StorageReturn => StorageReturnUsage,
        _ => SymbolicStorageReturnUsage,
    };

    public async Task<StorageTransaction> AddTransactionAsync(
        StorageTransactionType type = StorageTransactionType.Receipt, decimal gross = 30_000m, decimal net = 28_000m,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed, Guid? ownershipTransferKey = null,
        DateTime? date = null, string? code = null)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code ?? "RM" + Guid.NewGuid().ToString("N")[..6], BranchCode = Branch,
            StorageAddressCode = Lot.Code, CardCode = Depositor, CardName = "PRODUTOR UM", ItemCode = Item,
            ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG", WarehouseCode = Warehouse, TransactionType = type,
            TransactionStatus = status, GrossWeight = gross, NetWeight = net, TruckCode = "ABC1D23",
            TransactionDate = date ?? DateTime.Now.Date, OwnershipTransferKey = ownershipTransferKey,
            IsOwnershipTransfer = ownershipTransferKey is null ? null : "Y",
        };
        _db.Context.StorageTransactions.Add(transaction);
        await _db.SaveChangesAsync();
        return transaction;
    }

    public async Task<OwnershipTransfer> AddTransferAsync(OwnershipTransferStatus status, string destinationLot)
    {
        var transfer = new OwnershipTransfer
        {
            Key = Guid.NewGuid(), TransferCode = "OT" + Guid.NewGuid().ToString("N")[..6], Date = DateTime.Now.Date,
            TransferStatus = status, ItemCode = Item, ItemName = "SOJA EM GRAOS", UomCode = "KG", Quantity = 5_000m,
            StorageAddressOriginCode = Lot.Code!, StorageAddressDestinationCode = destinationLot,
        };
        _db.Context.OwnershipTransfers.Add(transfer);
        await _db.SaveChangesAsync();
        return transfer;
    }

    /// <summary>Nota de custódia de ENTRADA já gravada (sem passar pela guarda).</summary>
    public async Task<PurchaseInvoice> AddPurchaseDocumentAsync(
        StorageTransaction transaction, StorageCustodyRole role, decimal quantity,
        DocumentIssuerType issuer = DocumentIssuerType.Own, InvoiceStatus status = InvoiceStatus.Pending,
        NfeStatus nfeStatus = NfeStatus.None, string? accessKey = null, string number = "000000101")
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = Branch, CardCode = Depositor, IssuerType = issuer, InvoiceStatus = status,
            NfeStatus = nfeStatus, ChaveNFe = accessKey, InvoiceNumber = "DE" + Guid.NewGuid().ToString("N")[..6],
            TaxDocumentNumber = number, TaxDocumentSeries = "1", IssueDate = DateTime.Now.Date,
            CustodyStorageTransactionKey = transaction.Key, CustodyRole = role,
            Items = [new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 2m,
                UsageCode = UsageOf(role), Cfop = role == StorageCustodyRole.ProcessingEntry ? "1901" : "1905",
            }],
        };
        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    /// <summary>Nota de custódia de SAÍDA já gravada (sem passar pela guarda).</summary>
    public async Task<SalesInvoice> AddSalesDocumentAsync(
        StorageTransaction transaction, StorageCustodyRole role, decimal quantity,
        InvoiceStatus status = InvoiceStatus.Pending, NfeStatus nfeStatus = NfeStatus.None, string number = "000000201")
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = Branch, CardCode = Depositor, InvoiceStatus = status, NfeStatus = nfeStatus,
            InvoiceNumber = "DS" + Guid.NewGuid().ToString("N")[..6], TaxDocumentNumber = number, TaxDocumentSeries = "1",
            InvoiceDate = DateTime.Now.Date, CustodyStorageTransactionKey = transaction.Key, CustodyRole = role,
            Items = [new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 2m,
                UsageCode = UsageOf(role),
            }],
        };
        _db.Context.SalesInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }
}
```

(Se `Usage`, `StorageAddress` ou `OwnershipTransfer` exigirem outra propriedade `required`, o compilador acusa no
Step 2 — complete com um valor fixo e siga.)

`SiagroB1.Application.Tests/StorageCustody/StorageCustodyRulesTests.cs`:

```csharp
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.StorageCustody;

/// <summary>Etapas esperadas por romaneio (spec 2026-10-12 §3.4) e montagem da seção.</summary>
public class StorageCustodyRulesTests
{
    private static StorageCustodyTransactionRow Row(
        StorageTransactionType type = StorageTransactionType.Receipt, bool processing = true, bool transfer = false,
        bool transferCancelled = false, StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        StorageOwnershipType ownership = StorageOwnershipType.ThirdParty,
        StorageAddressNature nature = StorageAddressNature.Regular) => new()
    {
        Key = Guid.NewGuid(), Code = "RM000001", TransactionType = type, TransactionStatus = status,
        GrossWeight = 30_000m, NetWeight = 28_000m, IsOwnershipTransfer = transfer, TransferCancelled = transferCancelled,
        OwnershipType = ownership, Nature = nature, RequiresProcessing = processing,
        StorageAddressCode = "L0001", CardCode = "P0001", ItemCode = "SOJA", UoM = "KG",
    };

    [Fact]
    public void Receipt_with_processing_expects_the_three_entry_steps() =>
        Assert.Equal(
            [StorageCustodyRole.ProcessingEntry, StorageCustodyRole.ProcessingSymbolicReturn, StorageCustodyRole.StorageEntry],
            StorageCustodyRules.ExpectedRoles(Row()));

    [Fact]
    public void Receipt_without_processing_expects_only_the_storage_entry() =>
        Assert.Equal([StorageCustodyRole.StorageEntry], StorageCustodyRules.ExpectedRoles(Row(processing: false)));

    [Fact]
    public void Transfer_receipt_expects_only_the_storage_entry_even_with_processing() =>
        Assert.Equal([StorageCustodyRole.StorageEntry], StorageCustodyRules.ExpectedRoles(Row(transfer: true)));

    [Fact]
    public void Shipment_expects_the_return() =>
        Assert.Equal([StorageCustodyRole.StorageReturn], StorageCustodyRules.ExpectedRoles(Row(StorageTransactionType.Shipment)));

    [Fact]
    public void Transfer_shipment_expects_the_symbolic_return() =>
        Assert.Equal([StorageCustodyRole.StorageSymbolicReturn],
            StorageCustodyRules.ExpectedRoles(Row(StorageTransactionType.Shipment, transfer: true)));

    [Fact]
    public void Romaneios_of_a_cancelled_transfer_expect_nothing()
    {
        Assert.Empty(StorageCustodyRules.ExpectedRoles(Row(transfer: true, transferCancelled: true)));
        Assert.Empty(StorageCustodyRules.ExpectedRoles(Row(StorageTransactionType.Shipment, transfer: true, transferCancelled: true)));
    }

    [Theory]
    [InlineData(StorageTransactionsStatus.Pending)]
    [InlineData(StorageTransactionsStatus.Cancelled)]
    public void Unconfirmed_romaneio_expects_nothing(StorageTransactionsStatus status) =>
        Assert.Empty(StorageCustodyRules.ExpectedRoles(Row(status: status)));

    [Fact]
    public void Invoiced_romaneio_counts_as_confirmed() =>
        Assert.NotEmpty(StorageCustodyRules.ExpectedRoles(Row(status: StorageTransactionsStatus.Invoiced)));

    [Fact]
    public void Own_lot_and_transshipment_lot_expect_nothing()
    {
        Assert.Empty(StorageCustodyRules.ExpectedRoles(Row(ownership: StorageOwnershipType.OwnedInOurCustody)));
        Assert.Empty(StorageCustodyRules.ExpectedRoles(Row(nature: StorageAddressNature.Transshipment)));
    }

    [Fact]
    public void Other_transaction_types_expect_nothing() =>
        Assert.Empty(StorageCustodyRules.ExpectedRoles(Row(StorageTransactionType.TechnicalLoss)));

    [Fact]
    public void Quantities_follow_gross_gross_net()
    {
        var row = Row();

        Assert.Equal(30_000m, StorageCustodyRules.ExpectedQuantity(StorageCustodyRole.ProcessingEntry, row, null));
        Assert.Equal(29_500m, StorageCustodyRules.ExpectedQuantity(StorageCustodyRole.ProcessingSymbolicReturn, row, 29_500m));
        Assert.Null(StorageCustodyRules.ExpectedQuantity(StorageCustodyRole.ProcessingSymbolicReturn, row, null));
        Assert.Equal(28_000m, StorageCustodyRules.ExpectedQuantity(StorageCustodyRole.StorageEntry, row, null));
        Assert.Equal(28_000m, StorageCustodyRules.ExpectedQuantity(StorageCustodyRole.StorageReturn, row, null));
        Assert.Equal(28_000m, StorageCustodyRules.ExpectedQuantity(StorageCustodyRole.StorageSymbolicReturn, row, null));
    }

    [Fact]
    public void Steps_show_situation_quantity_and_difference()
    {
        var row = Row();
        var entry = new StorageCustodyDocumentRow
        {
            TransactionKey = row.Key, Role = StorageCustodyRole.ProcessingEntry, DocumentKey = Guid.NewGuid(),
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceStatus = InvoiceStatus.Confirmed, Quantity = 29_500m,
            InvoiceNumber = "DE000001", TaxDocumentNumber = "000000123",
        };
        var symbolic = new StorageCustodyDocumentRow
        {
            TransactionKey = row.Key, Role = StorageCustodyRole.ProcessingSymbolicReturn, DocumentKey = Guid.NewGuid(),
            IsSalesInvoice = true, IssuerType = DocumentIssuerType.Own, InvoiceStatus = InvoiceStatus.Confirmed,
            NfeStatus = NfeStatus.Authorized, Quantity = 29_500m,
        };
        var otherRomaneio = new StorageCustodyDocumentRow { TransactionKey = Guid.NewGuid(), Role = StorageCustodyRole.StorageEntry };

        var steps = StorageCustodyRules.BuildSteps(row, [entry, symbolic, otherRomaneio]);

        Assert.Equal(3, steps.Count);
        Assert.Equal(StorageCustodyStepSituation.Recorded, steps[0].Situation);
        Assert.Equal(-500m, steps[0].Difference);
        Assert.Equal("000000123", steps[0].Number);
        Assert.Equal(StorageCustodyStepSituation.Authorized, steps[1].Situation);
        Assert.Equal(0m, steps[1].Difference);
        Assert.Equal(StorageCustodyStepSituation.Missing, steps[2].Situation);
        Assert.Null(steps[2].DocumentKey);
        Assert.Equal(28_000m, steps[2].ExpectedQuantity);
        Assert.Equal(StorageCustodyRole.StorageEntry, StorageCustodyRules.NextRole(steps));
    }

    [Theory]
    [InlineData(DocumentIssuerType.Own, InvoiceStatus.Pending, NfeStatus.None, StorageCustodyStepSituation.Generated)]
    [InlineData(DocumentIssuerType.Own, InvoiceStatus.Confirmed, NfeStatus.Authorized, StorageCustodyStepSituation.Authorized)]
    [InlineData(DocumentIssuerType.Own, InvoiceStatus.Pending, NfeStatus.Rejected, StorageCustodyStepSituation.Generated)]
    [InlineData(DocumentIssuerType.ThirdParty, InvoiceStatus.Pending, NfeStatus.None, StorageCustodyStepSituation.Generated)]
    [InlineData(DocumentIssuerType.ThirdParty, InvoiceStatus.Confirmed, NfeStatus.None, StorageCustodyStepSituation.Recorded)]
    public void Situation_follows_issuer_status_and_nfe(
        DocumentIssuerType issuer, InvoiceStatus status, NfeStatus nfe, StorageCustodyStepSituation expected) =>
        Assert.Equal(expected, StorageCustodyRules.Situation(new StorageCustodyDocumentRow
        {
            IssuerType = issuer, InvoiceStatus = status, NfeStatus = nfe,
        }));
}
```

`SiagroB1.Application.Tests/StorageCustody/StorageCustodyQueryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Tests.StorageCustody;

public class StorageCustodyQueryTests
{
    [Fact]
    public async Task Transactions_carry_the_lot_and_the_transfer_status()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var plain = await seed.AddTransactionAsync();
        var transfer = await seed.AddTransferAsync(OwnershipTransferStatus.Cancelled, "L0002");
        var fromTransfer = await seed.AddTransactionAsync(StorageTransactionType.Shipment, ownershipTransferKey: transfer.Key);

        var rows = await StorageCustodyQuery.Transactions(db.Context).ToListAsync();

        var row = rows.Single(r => r.Key == plain.Key);
        Assert.Equal(StorageCustodySeed.Depositor, row.CardCode);
        Assert.Equal("L0001", row.StorageAddressCode);
        Assert.Equal(StorageCustodySeed.Branch, row.BranchCode);
        Assert.True(row.RequiresProcessing);
        Assert.False(row.IsOwnershipTransfer);
        var transferRow = rows.Single(r => r.Key == fromTransfer.Key);
        Assert.True(transferRow.IsOwnershipTransfer);
        Assert.True(transferRow.TransferCancelled);
    }

    [Fact]
    public async Task Own_lot_romaneio_is_not_a_custody_transaction()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        seed.Lot.OwnershipType = StorageOwnershipType.OwnedInOurCustody;
        await db.SaveChangesAsync();
        await seed.AddTransactionAsync();

        Assert.Empty(await StorageCustodyQuery.Transactions(db.Context).ToListAsync());
    }

    [Fact]
    public async Task Active_documents_skip_cancelled_and_sum_the_lines()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m, status: InvoiceStatus.Cancelled);
        var live = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);
        await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m);

        var docs = await StorageCustodyQuery.ActiveDocumentsAsync(db.Context, [t.Key]);

        Assert.Equal(2, docs.Count);
        var entry = docs.Single(d => !d.IsSalesInvoice);
        Assert.Equal(live.Key, entry.DocumentKey);
        Assert.Equal(30_000m, entry.Quantity);
        Assert.Equal("1901", entry.Cfop);
        Assert.Equal(DocumentIssuerType.Own, docs.Single(d => d.IsSalesInvoice).IssuerType);
    }

    [Fact]
    public void Queries_translate_to_sql_server()
    {
        using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none").Options);

        Assert.NotEmpty(StorageCustodyQuery.Transactions(ctx).ToQueryString());
        // Composição sobre a projeção (guarda, resumo e relatório filtram depois do Select).
        var key = Guid.NewGuid();
        var until = new DateTime(2026, 11, 1);
        Assert.NotEmpty(StorageCustodyQuery.Transactions(ctx)
            .Where(t => t.Key == key || (t.StorageAddressCode == "L1" && t.TransactionDate < until)).ToQueryString());
        Assert.NotEmpty(StorageCustodyQuery.PurchaseDocuments(ctx, [Guid.NewGuid()]).ToQueryString());
        Assert.NotEmpty(StorageCustodyQuery.SalesDocuments(ctx, [Guid.NewGuid()]).ToQueryString());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageCustodyRulesTests|FullyQualifiedName~StorageCustodyQueryTests"`
Expected: FAIL de compilação (`StorageCustodyRules`, `StorageCustodyQuery`, DTOs não existem).

- [ ] **Step 3: Implement**

`SiagroB1.Domain/Dtos/StorageCustodyDtos.cs`:

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Situação de uma etapa da cadeia de custódia (spec 2026-10-12 §5).</summary>
public enum StorageCustodyStepSituation
{
    /// <summary>Sem nota ativa.</summary>
    Missing = 0,
    /// <summary>Nota própria sem NF-e autorizada, ou escrituração ainda Pendente.</summary>
    Generated = 1,
    /// <summary>NF-e própria autorizada.</summary>
    Authorized = 2,
    /// <summary>Escrituração da NF-e do depositante confirmada.</summary>
    Recorded = 3,
}

/// <summary>Romaneio de Recepção/Expedição em lote de terceiro, com o lote e a transferência (StorageCustodyQuery).</summary>
public sealed class StorageCustodyTransactionRow
{
    public Guid Key { get; set; }
    public string? Code { get; set; }
    public StorageTransactionType TransactionType { get; set; }
    public StorageTransactionsStatus TransactionStatus { get; set; }
    public DateTime? TransactionDate { get; set; }
    public decimal GrossWeight { get; set; }
    public decimal NetWeight { get; set; }
    public string? TruckCode { get; set; }
    public bool IsOwnershipTransfer { get; set; }
    public bool TransferCancelled { get; set; }
    public string StorageAddressCode { get; set; } = "";
    public string? BranchCode { get; set; }
    public string CardCode { get; set; } = "";
    public string? CardName { get; set; }
    public string ItemCode { get; set; } = "";
    public string? ItemName { get; set; }
    public string UoM { get; set; } = "";
    public StorageOwnershipType OwnershipType { get; set; }
    public StorageAddressNature Nature { get; set; }
    public bool RequiresProcessing { get; set; }
}

/// <summary>Nota de custódia ATIVA (não cancelada) de um romaneio — entrada ou saída.</summary>
public sealed class StorageCustodyDocumentRow
{
    public Guid TransactionKey { get; set; }
    public StorageCustodyRole Role { get; set; }
    public Guid DocumentKey { get; set; }
    public bool IsSalesInvoice { get; set; }
    /// <summary>Documento de saída é sempre próprio.</summary>
    public DocumentIssuerType IssuerType { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? TaxDocumentNumber { get; set; }
    public string? TaxDocumentSeries { get; set; }
    public string? ChaveNFe { get; set; }
    public InvoiceStatus InvoiceStatus { get; set; }
    public NfeStatus NfeStatus { get; set; }
    public DateTime? IssueDate { get; set; }
    public decimal Quantity { get; set; }
    public string? Cfop { get; set; }
}

/// <summary>Uma etapa esperada do romaneio, com a nota que a cumpre (ou nenhuma).</summary>
public sealed class StorageCustodyStepDto
{
    public StorageCustodyRole Role { get; set; }
    public string RoleText { get; set; } = "";
    public StorageCustodyStepSituation Situation { get; set; }
    public string SituationText { get; set; } = "";
    public Guid? DocumentKey { get; set; }
    public bool IsSalesInvoice { get; set; }
    /// <summary>Número interno (DE…/DS…).</summary>
    public string? InvoiceNumber { get; set; }
    /// <summary>Número da NF quando existe; senão o interno.</summary>
    public string? Number { get; set; }
    public string? TaxDocumentSeries { get; set; }
    public string? ChaveNFe { get; set; }
    public DateTime? IssueDate { get; set; }
    public string? Cfop { get; set; }
    public NfeStatus? NfeStatus { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? ExpectedQuantity { get; set; }
    /// <summary>Quantidade da nota − quantidade esperada (balança, ou nota do papel 1 na devolução simbólica).</summary>
    public decimal? Difference { get; set; }
}

/// <summary>Seção "Notas fiscais de custódia" do romaneio.</summary>
public sealed class StorageCustodySummaryDto
{
    public Guid TransactionKey { get; set; }
    /// <summary>False = romaneio fora do escopo (lote próprio, não confirmado, transferência cancelada…).</summary>
    public bool Applies { get; set; }
    /// <summary>Primeira etapa sem nota; None quando completa.</summary>
    public StorageCustodyRole NextRole { get; set; }
    public List<StorageCustodyStepDto> Steps { get; set; } = [];
}
```

`SiagroB1.Domain/Enums/StorageCustodyRules.cs`:

```csharp
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Domain.Enums;

/// <summary>
/// Regras puras das notas de custódia em armazém próprio (spec 2026-10-12 §3.4, §4.2). Usadas pela guarda e pela seção
/// do romaneio (Application) e pelo relatório (Reports): a mesma regra nos dois lados.
/// </summary>
public static class StorageCustodyRules
{
    /// <summary>
    /// Etapas que o romaneio espera, em ordem. Só romaneio Confirmado/Faturado de lote de terceiro Regular. Romaneios de
    /// transferência CANCELADA — os originais e as contrapartidas que o cancelamento cria, todos com o mesmo
    /// OwnershipTransferKey — não esperam nada.
    /// </summary>
    public static IReadOnlyList<StorageCustodyRole> ExpectedRoles(StorageCustodyTransactionRow t)
    {
        if (t.OwnershipType != StorageOwnershipType.ThirdParty || t.Nature != StorageAddressNature.Regular)
            return [];

        if (t.TransactionStatus is not (StorageTransactionsStatus.Confirmed or StorageTransactionsStatus.Invoiced))
            return [];

        if (t.TransferCancelled)
            return [];

        return t.TransactionType switch
        {
            StorageTransactionType.Receipt when t.IsOwnershipTransfer => [StorageCustodyRole.StorageEntry],
            StorageTransactionType.Receipt when t.RequiresProcessing =>
                [StorageCustodyRole.ProcessingEntry, StorageCustodyRole.ProcessingSymbolicReturn, StorageCustodyRole.StorageEntry],
            StorageTransactionType.Receipt => [StorageCustodyRole.StorageEntry],
            StorageTransactionType.Shipment when t.IsOwnershipTransfer => [StorageCustodyRole.StorageSymbolicReturn],
            StorageTransactionType.Shipment => [StorageCustodyRole.StorageReturn],
            _ => [],
        };
    }

    /// <summary>Entradas: Documento de Entrada (própria ou escrituração).</summary>
    public static bool IsPurchaseRole(StorageCustodyRole role) =>
        role is StorageCustodyRole.ProcessingEntry or StorageCustodyRole.StorageEntry;

    /// <summary>Devolução e retornos: Documento de Saída, sempre próprio.</summary>
    public static bool IsSalesRole(StorageCustodyRole role) =>
        role is StorageCustodyRole.ProcessingSymbolicReturn or StorageCustodyRole.StorageReturn or StorageCustodyRole.StorageSymbolicReturn;

    /// <summary>
    /// Bruto / bruto / líquido (spec D7): entrada para beneficiamento pelo bruto; devolução simbólica pela quantidade da
    /// nota do papel 1 (null sem ela); armazenagem e retornos pelo líquido, que é o que o saldo do lote soma.
    /// </summary>
    public static decimal? ExpectedQuantity(StorageCustodyRole role, StorageCustodyTransactionRow t, decimal? processingEntryQuantity) =>
        role switch
        {
            StorageCustodyRole.ProcessingEntry => t.GrossWeight,
            StorageCustodyRole.ProcessingSymbolicReturn => processingEntryQuantity,
            StorageCustodyRole.None => null,
            _ => t.NetWeight,
        };

    public static StorageCustodyStepSituation Situation(StorageCustodyDocumentRow? d)
    {
        if (d is null)
            return StorageCustodyStepSituation.Missing;

        if (d.IsSalesInvoice || d.IssuerType == DocumentIssuerType.Own)
            return d.NfeStatus == NfeStatus.Authorized ? StorageCustodyStepSituation.Authorized : StorageCustodyStepSituation.Generated;

        return d.InvoiceStatus == InvoiceStatus.Confirmed ? StorageCustodyStepSituation.Recorded : StorageCustodyStepSituation.Generated;
    }

    public static string RoleText(StorageCustodyRole role) => role switch
    {
        StorageCustodyRole.ProcessingEntry => "Entrada para beneficiamento",
        StorageCustodyRole.ProcessingSymbolicReturn => "Devolução simbólica do beneficiamento",
        StorageCustodyRole.StorageEntry => "Entrada para armazenagem",
        StorageCustodyRole.StorageReturn => "Retorno de armazenagem",
        StorageCustodyRole.StorageSymbolicReturn => "Retorno simbólico de armazenagem",
        _ => "Nenhum",
    };

    /// <summary>Rótulo curto do relatório (cabe na coluna).</summary>
    public static string ShortRoleText(StorageCustodyRole role) => role switch
    {
        StorageCustodyRole.ProcessingEntry => "Entrada beneficiamento",
        StorageCustodyRole.ProcessingSymbolicReturn => "Devolução simbólica",
        StorageCustodyRole.StorageEntry => "Entrada armazenagem",
        StorageCustodyRole.StorageReturn => "Retorno",
        StorageCustodyRole.StorageSymbolicReturn => "Retorno simbólico",
        _ => "",
    };

    public static string SituationText(StorageCustodyStepSituation situation) => situation switch
    {
        StorageCustodyStepSituation.Generated => "Gerada",
        StorageCustodyStepSituation.Authorized => "Autorizada",
        StorageCustodyStepSituation.Recorded => "Escriturada",
        _ => "PENDENTE",
    };

    /// <summary>Número da NF quando existe; senão o interno.</summary>
    public static string DocumentNumber(StorageCustodyDocumentRow d) =>
        string.IsNullOrWhiteSpace(d.TaxDocumentNumber) ? d.InvoiceNumber ?? "" : d.TaxDocumentNumber;

    public static List<StorageCustodyStepDto> BuildSteps(StorageCustodyTransactionRow t, IEnumerable<StorageCustodyDocumentRow> documents)
    {
        var mine = documents.Where(d => d.TransactionKey == t.Key).ToList();
        var processingEntryQuantity = mine.FirstOrDefault(d => d.Role == StorageCustodyRole.ProcessingEntry)?.Quantity;

        return ExpectedRoles(t).Select(role =>
        {
            var doc = mine.FirstOrDefault(d => d.Role == role);
            var expected = ExpectedQuantity(role, t, processingEntryQuantity);
            var situation = Situation(doc);

            return new StorageCustodyStepDto
            {
                Role = role,
                RoleText = RoleText(role),
                Situation = situation,
                SituationText = SituationText(situation),
                DocumentKey = doc?.DocumentKey,
                IsSalesInvoice = doc?.IsSalesInvoice ?? IsSalesRole(role),
                InvoiceNumber = doc?.InvoiceNumber,
                Number = doc is null ? null : DocumentNumber(doc),
                TaxDocumentSeries = doc?.TaxDocumentSeries,
                ChaveNFe = doc?.ChaveNFe,
                IssueDate = doc?.IssueDate,
                Cfop = doc?.Cfop,
                NfeStatus = doc?.NfeStatus,
                Quantity = doc?.Quantity,
                ExpectedQuantity = expected,
                Difference = doc is not null && expected is not null ? doc.Quantity - expected : null,
            };
        }).ToList();
    }

    public static StorageCustodyRole NextRole(IEnumerable<StorageCustodyStepDto> steps) =>
        steps.FirstOrDefault(s => s.Situation == StorageCustodyStepSituation.Missing)?.Role ?? StorageCustodyRole.None;
}
```

`SiagroB1.Infra/Queries/StorageCustodyQuery.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Romaneios de lote de armazenagem de terceiro e as notas de custódia ativas deles (spec 2026-10-12). Mora no Infra
/// porque a Web (seção do romaneio, guarda) e o Reports (PDF) precisam da MESMA consulta, e o Reports não referencia o
/// Application — mesmo motivo do RoyaltyBalanceQuery.
/// </summary>
public static class StorageCustodyQuery
{
    /// <summary>Recepções e Expedições de lote de terceiro Regular, com o lote e a situação da transferência.</summary>
    public static IQueryable<StorageCustodyTransactionRow> Transactions(AppDbContext ctx) =>
        from t in ctx.StorageTransactions.AsNoTracking()
        join a in ctx.StorageAddresses.AsNoTracking() on t.StorageAddressCode equals a.Code
        where (t.TransactionType == StorageTransactionType.Receipt || t.TransactionType == StorageTransactionType.Shipment)
              && a.OwnershipType == StorageOwnershipType.ThirdParty
              && a.Nature == StorageAddressNature.Regular
        select new StorageCustodyTransactionRow
        {
            Key = t.Key,
            Code = t.Code,
            TransactionType = t.TransactionType,
            TransactionStatus = t.TransactionStatus,
            TransactionDate = t.TransactionDate,
            GrossWeight = t.GrossWeight,
            NetWeight = t.NetWeight,
            TruckCode = t.TruckCode,
            IsOwnershipTransfer = t.OwnershipTransferKey != null || t.IsOwnershipTransfer == "Y",
            TransferCancelled = t.OwnershipTransferKey != null && ctx.OwnershipTransfers.Any(o =>
                o.Key == t.OwnershipTransferKey && o.TransferStatus == OwnershipTransferStatus.Cancelled),
            StorageAddressCode = a.Code!,
            BranchCode = a.BranchCode,
            CardCode = a.CardCode,
            CardName = a.CardName,
            ItemCode = a.ItemCode,
            ItemName = a.ItemName,
            UoM = a.UoM,
            OwnershipType = a.OwnershipType,
            Nature = a.Nature,
            RequiresProcessing = a.RequiresProcessing,
        };

    /// <summary>Documentos de entrada não cancelados ligados aos romaneios dados.</summary>
    public static IQueryable<StorageCustodyDocumentRow> PurchaseDocuments(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys) =>
        ctx.PurchaseInvoices.AsNoTracking()
            .Where(i => i.CustodyStorageTransactionKey != null
                        && transactionKeys.Contains(i.CustodyStorageTransactionKey.Value)
                        && i.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(i => new StorageCustodyDocumentRow
            {
                TransactionKey = i.CustodyStorageTransactionKey!.Value,
                Role = i.CustodyRole,
                DocumentKey = i.Key,
                IsSalesInvoice = false,
                IssuerType = i.IssuerType,
                InvoiceNumber = i.InvoiceNumber,
                TaxDocumentNumber = i.TaxDocumentNumber,
                TaxDocumentSeries = i.TaxDocumentSeries,
                ChaveNFe = i.ChaveNFe,
                InvoiceStatus = i.InvoiceStatus,
                NfeStatus = i.NfeStatus,
                IssueDate = i.IssueDate,
                Quantity = i.Items.Sum(l => l.Quantity),
                Cfop = i.Items.Select(l => l.Cfop).FirstOrDefault(),
            });

    /// <summary>Documentos de saída não cancelados ligados aos romaneios dados (sempre próprios).</summary>
    public static IQueryable<StorageCustodyDocumentRow> SalesDocuments(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys) =>
        ctx.SalesInvoices.AsNoTracking()
            .Where(i => i.CustodyStorageTransactionKey != null
                        && transactionKeys.Contains(i.CustodyStorageTransactionKey.Value)
                        && i.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(i => new StorageCustodyDocumentRow
            {
                TransactionKey = i.CustodyStorageTransactionKey!.Value,
                Role = i.CustodyRole,
                DocumentKey = i.Key,
                IsSalesInvoice = true,
                IssuerType = DocumentIssuerType.Own,
                InvoiceNumber = i.InvoiceNumber,
                TaxDocumentNumber = i.TaxDocumentNumber,
                TaxDocumentSeries = i.TaxDocumentSeries,
                ChaveNFe = i.ChaveNFe,
                InvoiceStatus = i.InvoiceStatus,
                NfeStatus = i.NfeStatus,
                IssueDate = i.InvoiceDate,
                Quantity = i.Items.Sum(l => l.Quantity),
                Cfop = i.Items.Select(l => l.Cfop).FirstOrDefault(),
            });

    public static async Task<List<StorageCustodyDocumentRow>> ActiveDocumentsAsync(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys)
    {
        if (transactionKeys.Count == 0)
            return [];

        var purchases = await PurchaseDocuments(ctx, transactionKeys).ToListAsync();
        var sales = await SalesDocuments(ctx, transactionKeys).ToListAsync();
        return [.. purchases, .. sales];
    }
}
```

(Se `PurchaseInvoiceItem.Cfop`/`SalesInvoiceItem.Cfop` tiver outro nome, ache com
`grep -n "Cfop" SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs SiagroB1.Domain/Entities/SalesInvoiceItem.cs`.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageCustodyRulesTests|FullyQualifiedName~StorageCustodyQueryTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Domain SiagroB1.Infra SiagroB1.Application.Tests/Support/StorageCustodySeed.cs SiagroB1.Application.Tests/StorageCustody
git commit -m "feat(storage): etapas fiscais esperadas por romaneio de lote de terceiro" -m "Regras puras (Domain) e consulta compartilhada (Infra) das notas de custódia: a seção do romaneio e o relatório leem a mesma regra. Romaneio de transferência cancelada não espera nota." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

## Task 4: Guarda das notas de custódia (gravação, confirmação e emissão)

**Files:**
- Create: `SiagroB1.Application/Services/StorageCustody/StorageCustodyGuard.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs`, `PurchaseInvoicesUpdateService.cs`, `PurchaseInvoicesItemsCreateService.cs`, `PurchaseInvoicesItemsUpdateService.cs`, `PurchaseInvoicesItemsDeleteService.cs`, `PurchaseInvoicesConfirmService.cs`, `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs`, `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs`, `SalesInvoicesConfirmService.cs`, `SalesInvoicesUpdateService.cs`, `SiagroB1.Web/Controllers/SalesInvoicesController.cs`
- Test: `SiagroB1.Application.Tests/StorageCustody/StorageCustodyGuardTests.cs` (novo)

**Interfaces:**
- Consumes: Tasks 1 e 3.
- Produces:
  - `StorageCustodyGuard.StandaloneOnlyMessage`
  - `EnsurePurchaseAsync(AppDbContext, PurchaseInvoice, IReadOnlyList<PurchaseInvoiceItem>) : Task` — grava `invoice.CustodyRole`
  - `EnsureSalesAsync(AppDbContext, SalesInvoice, IReadOnlyList<SalesInvoiceItem>) : Task` — idem
  - `HasAccessKey(StorageCustodyDocumentRow) : bool`
  - `ProcessingEntryAccessKeyAsync(AppDbContext, SalesInvoice) : Task<string?>`
  - (Task 5 acrescenta `EnsurePurchaseHasNoLaterStepAsync`, `EnsureSalesHasNoLaterStepAsync`, `EnsureTransactionsHaveNoCustodyAsync`.)

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/StorageCustody/StorageCustodyGuardTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageCustody;

/// <summary>Guarda das notas de custódia (spec 2026-10-12 §3.3, §4.3, §4.4).</summary>
public class StorageCustodyGuardTests
{
    private static PurchaseInvoice Purchase(StorageTransaction? t, StorageCustodyRole role, decimal quantity,
        DocumentIssuerType issuer = DocumentIssuerType.Own, string card = StorageCustodySeed.Depositor,
        string item = StorageCustodySeed.Item, string branch = StorageCustodySeed.Branch) => new()
    {
        Key = Guid.NewGuid(), BranchCode = branch, CardCode = card, IssuerType = issuer,
        CustodyStorageTransactionKey = t?.Key,
        Items = [new PurchaseInvoiceItem { Key = Guid.NewGuid(), ItemCode = item, Quantity = quantity, UsageCode = StorageCustodySeed.UsageOf(role) }],
    };

    private static SalesInvoice Sales(StorageTransaction? t, StorageCustodyRole role, decimal quantity) => new()
    {
        Key = Guid.NewGuid(), BranchCode = StorageCustodySeed.Branch, CardCode = StorageCustodySeed.Depositor,
        CustodyStorageTransactionKey = t?.Key,
        Items = [new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = StorageCustodySeed.Item, Quantity = quantity, UsageCode = StorageCustodySeed.UsageOf(role) }],
    };

    private static Task Ensure(UnitOfWork db, PurchaseInvoice i) => StorageCustodyGuard.EnsurePurchaseAsync(db.Context, i, i.Items.ToList());
    private static Task Ensure(UnitOfWork db, SalesInvoice i) => StorageCustodyGuard.EnsureSalesAsync(db.Context, i, i.Items.ToList());

    [Fact]
    public async Task Own_processing_entry_with_the_gross_weight_passes_and_records_the_role()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(gross: 30_000m, net: 28_000m);
        var invoice = Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m);

        await Ensure(db, invoice);

        Assert.Equal(StorageCustodyRole.ProcessingEntry, invoice.CustodyRole);
    }

    [Fact]
    public async Task Own_entry_with_changed_quantity_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(gross: 30_000m, net: 28_000m, code: "RM000010");

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 28_000m)));

        Assert.Equal("A nota de entrada para beneficiamento precisa ter a quantidade do romaneio RM000010 (30.000,000).", e.Message);
    }

    [Fact]
    public async Task Producer_invoice_keeps_its_own_quantity()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(gross: 30_000m);

        await Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 29_500m, DocumentIssuerType.ThirdParty));
    }

    [Fact]
    public async Task Custody_usage_without_romaneio_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        await StorageCustodySeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Ensure(db, Purchase(null, StorageCustodyRole.ProcessingEntry, 1m, DocumentIssuerType.ThirdParty)));

        Assert.Equal("A nota de entrada para beneficiamento precisa indicar o romaneio do lote de armazenagem.", e.Message);
    }

    [Fact]
    public async Task Removing_the_custody_line_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var invoice = Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageCustodyGuard.EnsurePurchaseAsync(db.Context, invoice, []));

        Assert.Equal("O documento aponta um romaneio de armazenagem, mas nenhuma linha tem natureza de custódia.", e.Message);
    }

    [Fact]
    public async Task Mixed_roles_are_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var invoice = Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m);
        invoice.Items.Add(new PurchaseInvoiceItem { ItemCode = StorageCustodySeed.Item, Quantity = 1m, UsageCode = StorageCustodySeed.StorageEntryUsage });

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, invoice));

        Assert.Equal("Nota de custódia não mistura naturezas: todas as linhas precisam ter o mesmo papel.", e.Message);
    }

    [Theory]
    [InlineData("P9999", StorageCustodySeed.Item, StorageCustodySeed.Branch, "A nota de custódia precisa ser do depositante do lote (P0001).")]
    [InlineData(StorageCustodySeed.Depositor, "MILHO", StorageCustodySeed.Branch, "A nota de custódia precisa ser do produto do lote (SOJA).")]
    [InlineData(StorageCustodySeed.Depositor, StorageCustodySeed.Item, "02", "A nota de custódia precisa ser da filial do lote (01).")]
    public async Task Partner_item_and_branch_must_be_the_lot_ones(string card, string item, string branch, string message)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m, DocumentIssuerType.ThirdParty, card, item, branch)));

        Assert.Equal(message, e.Message);
    }

    [Fact]
    public async Task Role_not_expected_by_the_romaneio_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db, requiresProcessing: false);
        var t = await seed.AddTransactionAsync(code: "RM000011");

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m)));

        Assert.Equal("O romaneio RM000011 não espera a nota de entrada para beneficiamento.", e.Message);
    }

    [Fact]
    public async Task Cancelled_romaneio_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(status: StorageTransactionsStatus.Cancelled, code: "RM000012");

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m)));

        Assert.Equal("O romaneio RM000012 não espera a nota de entrada para beneficiamento.", e.Message);
    }

    [Fact]
    public async Task Second_active_document_for_the_same_step_is_refused_until_the_first_is_cancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(code: "RM000013");
        var first = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m, number: "000000055");

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m)));
        Assert.Equal("O romaneio RM000013 já tem a nota de entrada para beneficiamento nº 000000055 ativa.", e.Message);

        first.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();
        await Ensure(db, Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m));
    }

    [Fact]
    public async Task Editing_the_same_document_does_not_count_itself()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var saved = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);

        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, saved, saved.Items.ToList());
    }

    [Fact]
    public async Task Symbolic_return_needs_the_processing_entry_with_access_key()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m); // própria, sem NF-e

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, Sales(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m)));

        Assert.Equal("A devolução simbólica exige a entrada para beneficiamento com chave de acesso (NF-e própria autorizada ou escrituração confirmada).", e.Message);
    }

    [Fact]
    public async Task Symbolic_return_takes_the_quantity_of_the_producer_invoice()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(gross: 30_000m);
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 29_500m, DocumentIssuerType.ThirdParty,
            InvoiceStatus.Confirmed, accessKey: StorageCustodySeed.AccessKey);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, Sales(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m)));
        Assert.Equal("A devolução simbólica precisa ter a quantidade da entrada para beneficiamento (29.500,000).", e.Message);

        await Ensure(db, Sales(t, StorageCustodyRole.ProcessingSymbolicReturn, 29_500m));
    }

    [Fact]
    public async Task Storage_entry_after_processing_needs_the_symbolic_return()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(net: 28_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, Purchase(t, StorageCustodyRole.StorageEntry, 28_000m)));
        Assert.Equal("Gere antes a devolução simbólica do beneficiamento deste romaneio.", e.Message);

        await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m);
        await Ensure(db, Purchase(t, StorageCustodyRole.StorageEntry, 28_000m));
    }

    [Fact]
    public async Task Sales_custody_usage_without_romaneio_points_to_the_action()
    {
        var db = TestDb.CreateUnitOfWork();
        await StorageCustodySeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, Sales(null, StorageCustodyRole.StorageReturn, 1m)));

        Assert.Equal("Natureza de retorno de armazenagem: gere a nota pelo romaneio (Notas fiscais de custódia).", e.Message);
    }

    [Fact]
    public async Task Entry_role_in_a_sales_document_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Ensure(db, Sales(t, StorageCustodyRole.ProcessingEntry, 30_000m)));

        Assert.Equal("Natureza de entrada para beneficiamento é de entrada: lance no Documento de Entrada.", e.Message);
    }

    [Fact]
    public async Task Shipment_return_uses_the_net_weight()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(StorageTransactionType.Shipment, gross: 15_200m, net: 15_000m);

        await Ensure(db, Sales(t, StorageCustodyRole.StorageReturn, 15_000m));
    }

    [Fact]
    public async Task Regular_document_without_romaneio_is_untouched()
    {
        var db = TestDb.CreateUnitOfWork();
        await StorageCustodySeed.CreateAsync(db);
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "F1",
            Items = [new PurchaseInvoiceItem { ItemCode = "X", Quantity = 1m }],
        };

        await Ensure(db, invoice);

        Assert.Equal(StorageCustodyRole.None, invoice.CustodyRole);
    }

    [Fact]
    public async Task Processing_entry_access_key_feeds_the_symbolic_return_reference()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m, DocumentIssuerType.Own,
            InvoiceStatus.Confirmed, NfeStatus.Authorized, StorageCustodySeed.AccessKey);
        var symbolic = await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m);

        Assert.Equal(StorageCustodySeed.AccessKey, await StorageCustodyGuard.ProcessingEntryAccessKeyAsync(db.Context, symbolic));
    }
}
```

Testes de ligação (os serviços realmente chamam a guarda). Acrescente ao mesmo arquivo; as construções são as já
usadas nos testes citados:

```csharp
    [Fact]
    public async Task Purchase_create_runs_the_guard()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(gross: 30_000m);
        var create = new SiagroB1.Application.Services.PurchaseInvoices.PurchaseInvoicesCreateService(
            db, new FakeBusinessPartnerService(), new FakeItemService(), TaxTestServices.InactivePurchaseApply(db),
            new FakeDocNumberSequenceService());

        await Assert.ThrowsAsync<DefaultException>(() =>
            create.ExecuteAsync(Purchase(t, StorageCustodyRole.ProcessingEntry, 1m), "tester"));

        var ok = Purchase(t, StorageCustodyRole.ProcessingEntry, 30_000m);
        await create.ExecuteAsync(ok, "tester");
        Assert.Equal(StorageCustodyRole.ProcessingEntry,
            (await db.Context.PurchaseInvoices.SingleAsync(i => i.Key == ok.Key)).CustodyRole);
    }

    [Fact]
    public async Task Purchase_item_delete_cannot_leave_the_romaneio_without_custody_line()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var saved = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);

        await Assert.ThrowsAsync<DefaultException>(() =>
            new SiagroB1.Application.Services.PurchaseInvoices.PurchaseInvoicesItemsDeleteService(db, TaxTestServices.Gate(db, "STANDALONE"))
                .ExecuteAsync(saved.Items.Single().Key!.Value));
    }
```

E em `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesConfirmNfeGuardTests.cs` (que já constrói o
`SalesInvoicesConfirmService` num método `Service(...)`; reaproveite-o), um teste:

```csharp
    [Fact]
    public async Task Confirm_refuses_sales_custody_document_with_changed_quantity()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(StorageTransactionType.Shipment, net: 15_000m);
        var doc = await seed.AddSalesDocumentAsync(t, StorageCustodyRole.StorageReturn, 14_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() => /* serviço construído como nos testes vizinhos */ Service(db).ExecuteAsync(doc.Key, "tester"));

        Assert.Contains("precisa ter a quantidade do romaneio", e.Message);
    }
```

(Se `Service(...)` daquele arquivo tiver outra assinatura, adapte a chamada; o que importa é usar o serviço real de
confirmação. Se `TaxTestServices.Gate`/`InactivePurchaseApply` tiverem outra assinatura, use a dos testes vizinhos em
`PurchaseInvoicesItemsTests.cs`.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageCustodyGuardTests|FullyQualifiedName~Confirm_refuses_sales_custody_document"`
Expected: FAIL de compilação (`StorageCustodyGuard` não existe).

- [ ] **Step 3: Implement the guard**

`SiagroB1.Application/Services/StorageCustody/StorageCustodyGuard.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.StorageCustody;

/// <summary>
/// Guarda das notas de custódia em armazém próprio (spec 2026-10-12 §4). Recebe TODAS as linhas que o documento terá
/// depois da operação, decide o papel pela natureza delas e o grava em <c>CustodyRole</c> — o corpo da API nunca o
/// define. Plantada na gravação (create/update/linhas), na confirmação, na reserva do número da NF-e, no cancelamento,
/// na exclusão e nos estornos de romaneio/transferência. Fora do STANDALONE nenhuma natureza tem papel (o cadastro SAP
/// recusa), então a guarda é inerte lá.
/// </summary>
public static class StorageCustodyGuard
{
    public const string StandaloneOnlyMessage = "Disponível somente no modo STANDALONE.";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static string Label(StorageCustodyRole role) =>
        StorageCustodyRules.RoleText(role).ToLower(PtBr);

    private static async Task<StorageCustodyRole> DocumentRoleAsync(AppDbContext ctx, IReadOnlyList<int?> usageCodes)
    {
        var known = usageCodes.Where(c => c != null).Select(c => c!.Value).Distinct().ToList();
        var roles = known.Count == 0
            ? new Dictionary<int, StorageCustodyRole>()
            : await ctx.UsageEffects.AsNoTracking()
                .Where(e => known.Contains(e.UsageCode))
                .ToDictionaryAsync(e => e.UsageCode, e => e.StorageCustodyRole);

        var distinct = usageCodes
            .Select(c => c is { } code ? roles.GetValueOrDefault(code) : StorageCustodyRole.None)
            .Distinct()
            .ToList();

        if (distinct.Count > 1)
            throw new DefaultException("Nota de custódia não mistura naturezas: todas as linhas precisam ter o mesmo papel.");

        return distinct.SingleOrDefault();
    }

    public static async Task EnsurePurchaseAsync(AppDbContext ctx, PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines)
    {
        var role = await DocumentRoleAsync(ctx, lines.Select(l => l.UsageCode).ToList());
        invoice.CustodyRole = role;

        if (role == StorageCustodyRole.None)
        {
            EnsureNoDanglingRomaneio(invoice.CustodyStorageTransactionKey);
            return;
        }

        if (!StorageCustodyRules.IsPurchaseRole(role))
            throw new DefaultException($"Natureza de {Label(role)} é de saída: gere a nota pelo romaneio (Notas fiscais de custódia).");

        if (invoice.InvoiceType != PurchaseInvoiceType.Normal)
            throw new DefaultException("Nota de custódia precisa ser do tipo Normal.");

        await EnsureCoreAsync(ctx, role, invoice.CustodyStorageTransactionKey, invoice.Key, invoice.CardCode, invoice.BranchCode,
            lines.Select(l => l.ItemCode).ToList(), lines.Sum(l => l.Quantity),
            checkQuantity: invoice.IssuerType == DocumentIssuerType.Own);
    }

    public static async Task EnsureSalesAsync(AppDbContext ctx, SalesInvoice invoice, IReadOnlyList<SalesInvoiceItem> lines)
    {
        var role = await DocumentRoleAsync(ctx, lines.Select(l => l.UsageCode).ToList());
        invoice.CustodyRole = role;

        if (role == StorageCustodyRole.None)
        {
            EnsureNoDanglingRomaneio(invoice.CustodyStorageTransactionKey);
            return;
        }

        if (!StorageCustodyRules.IsSalesRole(role))
            throw new DefaultException($"Natureza de {Label(role)} é de entrada: lance no Documento de Entrada.");

        if (invoice.CustodyStorageTransactionKey is null)
            throw new DefaultException($"Natureza de {Label(role)}: gere a nota pelo romaneio (Notas fiscais de custódia).");

        if (invoice.InvoiceType != SalesInvoiceType.Normal)
            throw new DefaultException("Nota de custódia precisa ser do tipo Normal.");

        await EnsureCoreAsync(ctx, role, invoice.CustodyStorageTransactionKey, invoice.Key, invoice.CardCode, invoice.BranchCode,
            lines.Select(l => l.ItemCode).ToList(), lines.Sum(l => l.Quantity), checkQuantity: true);
    }

    private static void EnsureNoDanglingRomaneio(Guid? transactionKey)
    {
        if (transactionKey != null)
            throw new DefaultException("O documento aponta um romaneio de armazenagem, mas nenhuma linha tem natureza de custódia.");
    }

    private static async Task EnsureCoreAsync(
        AppDbContext ctx, StorageCustodyRole role, Guid? transactionKey, Guid documentKey, string? cardCode,
        string? branchCode, IReadOnlyList<string?> itemCodes, decimal quantity, bool checkQuantity)
    {
        if (transactionKey is not { } key)
            throw new DefaultException($"A nota de {Label(role)} precisa indicar o romaneio do lote de armazenagem.");

        var transaction = await StorageCustodyQuery.Transactions(ctx).FirstOrDefaultAsync(t => t.Key == key)
                          ?? throw new DefaultException("O romaneio indicado não é de lote de armazenagem de terceiro.");

        var expected = StorageCustodyRules.ExpectedRoles(transaction);
        if (!expected.Contains(role))
            throw new DefaultException($"O romaneio {transaction.Code} não espera a nota de {Label(role)}.");

        if (cardCode != transaction.CardCode)
            throw new DefaultException($"A nota de custódia precisa ser do depositante do lote ({transaction.CardCode}).");

        if (itemCodes.Any(c => c != transaction.ItemCode))
            throw new DefaultException($"A nota de custódia precisa ser do produto do lote ({transaction.ItemCode}).");

        if (branchCode != transaction.BranchCode)
            throw new DefaultException($"A nota de custódia precisa ser da filial do lote ({transaction.BranchCode}).");

        var others = (await StorageCustodyQuery.ActiveDocumentsAsync(ctx, [key]))
            .Where(d => d.DocumentKey != documentKey)
            .ToList();

        if (others.FirstOrDefault(d => d.Role == role) is { } same)
            throw new DefaultException(
                $"O romaneio {transaction.Code} já tem a nota de {Label(role)} nº {StorageCustodyRules.DocumentNumber(same)} ativa.");

        if (role == StorageCustodyRole.ProcessingSymbolicReturn)
        {
            var entry = others.FirstOrDefault(d => d.Role == StorageCustodyRole.ProcessingEntry);
            if (entry is null || !HasAccessKey(entry))
                throw new DefaultException(
                    "A devolução simbólica exige a entrada para beneficiamento com chave de acesso (NF-e própria autorizada ou escrituração confirmada).");

            if (quantity != entry.Quantity)
                throw new DefaultException(
                    $"A devolução simbólica precisa ter a quantidade da entrada para beneficiamento ({entry.Quantity.ToString("N3", PtBr)}).");

            return;
        }

        if (role == StorageCustodyRole.StorageEntry
            && expected.Contains(StorageCustodyRole.ProcessingSymbolicReturn)
            && others.All(d => d.Role != StorageCustodyRole.ProcessingSymbolicReturn))
            throw new DefaultException("Gere antes a devolução simbólica do beneficiamento deste romaneio.");

        if (checkQuantity)
        {
            var expectedQuantity = StorageCustodyRules.ExpectedQuantity(role, transaction, null)!.Value;
            if (quantity != expectedQuantity)
                throw new DefaultException(
                    $"A nota de {Label(role)} precisa ter a quantidade do romaneio {transaction.Code} ({expectedQuantity.ToString("N3", PtBr)}).");
        }
    }

    /// <summary>Chave de 44 dígitos que a SEFAZ conhece: NF-e própria autorizada ou escrituração confirmada.</summary>
    public static bool HasAccessKey(StorageCustodyDocumentRow d) =>
        d.ChaveNFe is { Length: 44 } k && k.All(char.IsAsciiDigit)
        && (d.IssuerType == DocumentIssuerType.Own ? d.NfeStatus == NfeStatus.Authorized : d.InvoiceStatus == InvoiceStatus.Confirmed);

    /// <summary>NFref da devolução simbólica: a chave da entrada para beneficiamento do mesmo romaneio.</summary>
    public static async Task<string?> ProcessingEntryAccessKeyAsync(AppDbContext ctx, SalesInvoice invoice)
    {
        if (invoice.CustodyRole != StorageCustodyRole.ProcessingSymbolicReturn || invoice.CustodyStorageTransactionKey is not { } key)
            return null;

        var entry = (await StorageCustodyQuery.ActiveDocumentsAsync(ctx, [key]))
                    .FirstOrDefault(d => d.Role == StorageCustodyRole.ProcessingEntry && HasAccessKey(d))
                    ?? throw new DefaultException("A devolução simbólica está sem a entrada para beneficiamento com chave de acesso.");

        return entry.ChaveNFe;
    }
}
```

- [ ] **Step 4: Plant the guard in the invoice flows**

`PurchaseInvoicesCreateService.ExecuteAsync` — logo depois de `await StorageDepositReturnGuard.EnsureAsync(...)`:

```csharp
        // Nota de custódia em armazém próprio (spec 2026-10-12 §4): romaneio, depositante, produto, filial, ordem e
        // quantidade; grava o papel no cabeçalho.
        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, invoice, invoice.Items.ToList());
```

`PurchaseInvoicesUpdateService.ExecuteAsync` — logo depois de `existing.PurchaseInvoiceOriginKey = entity.PurchaseInvoiceOriginKey;`:

```csharp
        existing.CustodyStorageTransactionKey = entity.CustodyStorageTransactionKey;
```

e logo depois de `await StorageDepositReturnGuard.EnsureAsync(db.Context, existing.CardCode, existing.Key, existing.Items.ToList());`:

```csharp
        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, existing, existing.Items.ToList());
```

`PurchaseInvoicesItemsCreateService.ExecuteAsync` — logo depois da chamada do `StorageDepositReturnGuard`:

```csharp
        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, invoice, [.. others, item]);
```

`PurchaseInvoicesItemsUpdateService.ExecuteAsync` — logo depois da chamada do `StorageDepositReturnGuard`:

```csharp
        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, invoice, [.. others, existing]);
```

`PurchaseInvoicesItemsDeleteService.ExecuteAsync` — logo antes de `db.Context.PurchaseInvoicesItems.Remove(item);`:

```csharp
        // A linha que sai não pode deixar o romaneio de custódia sem nota (spec 2026-10-12 §3.3).
        var remaining = await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == item.PurchaseInvoiceKey && x.Key != item.Key).ToListAsync();
        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, invoice, remaining);
```

`PurchaseInvoicesConfirmService.ExecuteAsync` — logo antes de `invoice.InvoiceStatus = InvoiceStatus.Confirmed;`:

```csharp
        // A escrituração/entrada de custódia precisa continuar batendo com o romaneio (spec 2026-10-12 §4.4).
        var lines = await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Where(l => l.PurchaseInvoiceKey == invoice.Key).ToListAsync();
        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, invoice, lines);
```

`PurchaseInvoicesNfeIssueService.EnsureBeforeReservationAsync` — no início do método:

```csharp
        // Nota de custódia editada pela tela depois de gerada: revalida antes de numerar (spec 2026-10-12 §4.1).
        if (invoice.CustodyStorageTransactionKey != null || invoice.CustodyRole != StorageCustodyRole.None)
            await StorageCustodyGuard.EnsurePurchaseAsync(_db.Context, invoice, invoice.Items.ToList());
```

`SalesInvoicesCreateService.ExecuteAsync` — logo depois de `var lineUsages = await usageGuard.ValidateAsync(salesInvoice);`:

```csharp
        // Nota de custódia (spec 2026-10-12 §4): só nasce pelo romaneio; grava o papel no cabeçalho.
        await StorageCustodyGuard.EnsureSalesAsync(db.Context, salesInvoice, salesInvoice.Items.ToList());
```

`SalesInvoicesConfirmService.ExecuteAsync` — logo depois de `await StorageDepositRules.EnsureRemittanceMatchesReleaseAsync(db.Context, invoice);`:

```csharp
        // Nota de custódia editada pela tela depois de gerada: a confirmação recusa o que deixou de bater com o romaneio.
        await StorageCustodyGuard.EnsureSalesAsync(db.Context, invoice, invoice.Items.ToList());
```

`SalesInvoicesUpdateService.ExecuteAsync` — logo depois do bloco que restaura `ShipmentReleaseKey`:

```csharp
            // O vínculo da nota de custódia só nasce no "Gerar nota" do romaneio: PUT/PATCH não o move.
            entry.Property(nameof(SalesInvoice.CustodyStorageTransactionKey)).CurrentValue =
                entry.OriginalValues[nameof(SalesInvoice.CustodyStorageTransactionKey)];
            entry.Property(nameof(SalesInvoice.CustodyRole)).CurrentValue =
                entry.OriginalValues[nameof(SalesInvoice.CustodyRole)];
```

`SiagroB1.Web/Controllers/SalesInvoicesController.Post` — logo depois de `entity.ShipmentReleaseKey = null;`:

```csharp
        // O vínculo da nota de custódia só nasce no "Gerar nota" do romaneio, que não passa por aqui.
        entity.CustodyStorageTransactionKey = null;
```

Acrescentar `using SiagroB1.Application.Services.StorageCustody;` (e `using SiagroB1.Domain.Enums;` onde faltar) em
cada arquivo tocado.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageCustody|FullyQualifiedName~SalesInvoices|FullyQualifiedName~PurchaseInvoices"`
Expected: PASS (os novos e os antigos de documento).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application SiagroB1.Web SiagroB1.Application.Tests
git commit -m "feat(invoice): guarda das notas de custódia na entrada e na saída" -m "A nota de custódia precisa apontar o romaneio do lote de terceiro, ser do depositante, do produto e da filial do lote, respeitar a ordem da cadeia e, quando própria, a quantidade do romaneio (bruto, nota da entrada ou líquido). A devolução simbólica exige a entrada para beneficiamento com chave. O papel fica gravado no cabeçalho." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

## Task 5: Cancelamento, exclusão e estorno na ordem inversa; trava do lote

**Files:**
- Modify: `SiagroB1.Application/Services/StorageCustody/StorageCustodyGuard.cs`, `PurchaseInvoicesCancelService.cs`, `PurchaseInvoicesDeleteService.cs`, `SalesInvoicesCancelService.cs`, `SalesInvoicesDeleteService.cs`, `StorageTransactionsCancelService.cs`, `StorageTransactionsReverseService.cs`, `OwnershipTransfersCancelService.cs`, `StorageAddressesUpdateService.cs`
- Test: `SiagroB1.Application.Tests/StorageCustody/StorageCustodyLockTests.cs` (novo)

**Interfaces:**
- Consumes: Task 4.
- Produces: `StorageCustodyGuard.EnsurePurchaseHasNoLaterStepAsync(AppDbContext, Guid)`,
  `EnsureSalesHasNoLaterStepAsync(AppDbContext, Guid)`, `EnsureTransactionsHaveNoCustodyAsync(AppDbContext, IReadOnlyCollection<Guid>)`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/StorageCustody/StorageCustodyLockTests.cs`:

```csharp
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.StorageCustody;

/// <summary>A cadeia se desfaz na ordem inversa; romaneio e transferência com nota ativa não são estornados (spec §4.5).</summary>
public class StorageCustodyLockTests
{
    [Fact]
    public async Task Processing_entry_is_not_cancelled_with_the_symbolic_return_active()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var entry = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m, DocumentIssuerType.ThirdParty, InvoiceStatus.Confirmed);
        var symbolic = await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m, number: "000000077");

        var e = await Assert.ThrowsAsync<DefaultException>(() => new PurchaseInvoicesCancelService(db).ExecuteAsync(entry.Key, "tester"));
        Assert.Equal("Cancele primeiro a nota de devolução simbólica do beneficiamento nº 000000077 deste romaneio.", e.Message);

        symbolic.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();
        await new PurchaseInvoicesCancelService(db).ExecuteAsync(entry.Key, "tester");
    }

    [Fact]
    public async Task Symbolic_return_is_not_cancelled_with_the_storage_entry_active()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var symbolic = await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m);
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.StorageEntry, 28_000m, number: "000000088");

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageCustodyGuard.EnsureSalesHasNoLaterStepAsync(db.Context, symbolic.Key));

        Assert.Equal("Cancele primeiro a nota de entrada para armazenagem nº 000000088 deste romaneio.", e.Message);
    }

    [Fact]
    public async Task Pending_processing_entry_is_not_deleted_with_the_symbolic_return_active()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var entry = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m, DocumentIssuerType.ThirdParty);
        await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m);

        await Assert.ThrowsAsync<DefaultException>(() => new PurchaseInvoicesDeleteService(db).ExecuteAsync(entry.Key));
    }

    [Fact]
    public async Task Romaneio_with_active_custody_document_is_not_cancelled_or_reversed()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        t.TransactionOrigin = TransactionCode.StorageTransaction;
        await db.SaveChangesAsync();
        var doc = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);
        var recalc = new ShipmentReleasesRecalculateShippedService(db.Context);

        var cancel = await Assert.ThrowsAsync<ApplicationException>(() =>
            new StorageTransactionsCancelService(db, recalc).ExecuteAsync(t.Key, "tester"));
        Assert.StartsWith("O romaneio tem nota fiscal de custódia ativa", cancel.Message);

        var reverse = await Assert.ThrowsAsync<ApplicationException>(() =>
            new StorageTransactionsReverseService(db, new StorageAddressesGetBalanceService(null!), recalc, new FakeStringLocalizer<Resource>())
                .ExecuteAsync(t.Key, "tester"));
        Assert.Equal("O romaneio tem nota fiscal de custódia ativa (nº 000000101). Cancele as notas fiscais de custódia antes de estornar.", reverse.Message);

        doc.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();
        await new StorageTransactionsCancelService(db, recalc).ExecuteAsync(t.Key, "tester");
    }

    [Fact]
    public async Task Requires_processing_is_locked_while_the_lot_has_custody_documents()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);
        var service = new StorageAddressesUpdateService(
            db.Context, new FakeBusinessPartnerService(), new FakeItemService(), new FakeWarehouseService(),
            new TestLogger<StorageAddressesUpdateService>());

        seed.Lot.RequiresProcessing = false; // PATCH: a instância rastreada chega já mutada
        var e = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(seed.Lot.Code!, seed.Lot, "tester"));

        Assert.Equal("O lote já possui notas fiscais de custódia; não é possível alterar o beneficiamento.", e.Message);
    }

    [Fact]
    public async Task Requires_processing_changes_freely_without_documents()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        await seed.AddTransactionAsync();
        var service = new StorageAddressesUpdateService(
            db.Context, new FakeBusinessPartnerService(), new FakeItemService(), new FakeWarehouseService(),
            new TestLogger<StorageAddressesUpdateService>());

        seed.Lot.RequiresProcessing = false;
        await service.ExecuteAsync(seed.Lot.Code!, seed.Lot, "tester");
    }
}
```

E em `SiagroB1.Application.Tests/OwnershipTransfers/OwnershipTransfersGuardsTests.cs` (usa o
`OwnershipTransfersTestContext` interno):

```csharp
    [Fact]
    public async Task Cancel_is_refused_while_a_transfer_romaneio_has_custody_document()
    {
        var ctx = new OwnershipTransfersTestContext();
        var origin = OwnershipTransfersTestContext.Lot("L1", "P0001", StorageOwnershipType.ThirdParty);
        var destination = OwnershipTransfersTestContext.Lot("L2", "P0002", StorageOwnershipType.ThirdParty);
        origin.BranchCode = destination.BranchCode = "01";
        var transfer = OwnershipTransfersTestContext.Transfer(origin, destination, status: OwnershipTransferStatus.Closed);
        ctx.Db.Context.StorageAddresses.AddRange(origin, destination);
        ctx.Db.Context.OwnershipTransfers.Add(transfer);
        var shipment = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "RM1", BranchCode = "01", StorageAddressCode = "L1", CardCode = "P0001",
            ItemCode = "SOJA", UnitOfMeasureCode = "KG", WarehouseCode = "01", TransactionType = StorageTransactionType.Shipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed, NetWeight = 1000m, GrossWeight = 1000m,
            OwnershipTransferKey = transfer.Key, IsOwnershipTransfer = "Y",
        };
        ctx.Db.Context.StorageTransactions.Add(shipment);
        ctx.Db.Context.SalesInvoices.Add(new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "P0001", InvoiceNumber = "DS1", TaxDocumentNumber = "000000009",
            CustodyStorageTransactionKey = shipment.Key, CustodyRole = StorageCustodyRole.StorageSymbolicReturn,
        });
        await ctx.Db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<ApplicationException>(() => ctx.Cancel().ExecuteAsync(transfer.Key, "tester"));

        Assert.Equal("O romaneio tem nota fiscal de custódia ativa (nº 000000009). Cancele as notas fiscais de custódia antes de estornar.", e.Message);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageCustodyLockTests|FullyQualifiedName~Cancel_is_refused_while_a_transfer_romaneio_has_custody_document"`
Expected: FAIL (métodos da guarda não existem; serviços não recusam).

- [ ] **Step 3: Implement**

Acrescentar ao `StorageCustodyGuard`:

```csharp
    /// <summary>Etapa que depende desta: cancelar/excluir esta exige cancelar aquela antes (spec §4.5).</summary>
    private static StorageCustodyRole LaterStep(StorageCustodyRole role) => role switch
    {
        StorageCustodyRole.ProcessingEntry => StorageCustodyRole.ProcessingSymbolicReturn,
        StorageCustodyRole.ProcessingSymbolicReturn => StorageCustodyRole.StorageEntry,
        _ => StorageCustodyRole.None,
    };

    private static async Task EnsureNoLaterStepAsync(AppDbContext ctx, Guid? transactionKey, StorageCustodyRole role, Guid documentKey)
    {
        var later = LaterStep(role);
        if (transactionKey is not { } key || later == StorageCustodyRole.None)
            return;

        var hit = (await StorageCustodyQuery.ActiveDocumentsAsync(ctx, [key]))
            .FirstOrDefault(d => d.Role == later && d.DocumentKey != documentKey);

        if (hit != null)
            throw new DefaultException(
                $"Cancele primeiro a nota de {Label(later)} nº {StorageCustodyRules.DocumentNumber(hit)} deste romaneio.");
    }

    public static async Task EnsurePurchaseHasNoLaterStepAsync(AppDbContext ctx, Guid purchaseInvoiceKey)
    {
        var doc = await ctx.PurchaseInvoices.AsNoTracking()
            .Where(i => i.Key == purchaseInvoiceKey)
            .Select(i => new { i.CustodyStorageTransactionKey, i.CustodyRole })
            .FirstOrDefaultAsync();

        if (doc != null)
            await EnsureNoLaterStepAsync(ctx, doc.CustodyStorageTransactionKey, doc.CustodyRole, purchaseInvoiceKey);
    }

    public static async Task EnsureSalesHasNoLaterStepAsync(AppDbContext ctx, Guid salesInvoiceKey)
    {
        var doc = await ctx.SalesInvoices.AsNoTracking()
            .Where(i => i.Key == salesInvoiceKey)
            .Select(i => new { i.CustodyStorageTransactionKey, i.CustodyRole })
            .FirstOrDefaultAsync();

        if (doc != null)
            await EnsureNoLaterStepAsync(ctx, doc.CustodyStorageTransactionKey, doc.CustodyRole, salesInvoiceKey);
    }

    /// <summary>
    /// Romaneio (ou transferência) com nota de custódia ativa não é cancelado nem estornado: a nota ficaria apontando um
    /// movimento que não existe mais. ApplicationException, como as travas vizinhas desses serviços.
    /// </summary>
    public static async Task EnsureTransactionsHaveNoCustodyAsync(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys)
    {
        var hit = (await StorageCustodyQuery.ActiveDocumentsAsync(ctx, transactionKeys)).FirstOrDefault();

        if (hit != null)
            throw new ApplicationException(
                $"O romaneio tem nota fiscal de custódia ativa (nº {StorageCustodyRules.DocumentNumber(hit)}). " +
                "Cancele as notas fiscais de custódia antes de estornar.");
    }
```

Plantar:

- `PurchaseInvoicesCancelService.EnsureBusinessRulesAsync` — ao fim do método:
  ```csharp
        // Cadeia de custódia na ordem inversa (spec 2026-10-12 §4.5).
        await StorageCustodyGuard.EnsurePurchaseHasNoLaterStepAsync(db.Context, key);
  ```
- `PurchaseInvoicesDeleteService.ExecuteAsync` — logo depois de `await PurchaseInvoiceCounterNote.EnsureNotReferencedAsync(...)`:
  ```csharp
        await StorageCustodyGuard.EnsurePurchaseHasNoLaterStepAsync(db.Context, invoice.Key);
  ```
- `SalesInvoicesCancelService.EnsureBusinessRulesAsync` — logo depois de `await StorageDepositRules.EnsureRemittanceHasNoReturnAsync(...)`:
  ```csharp
        await StorageCustodyGuard.EnsureSalesHasNoLaterStepAsync(db.Context, invoice.Key);
  ```
- `SalesInvoicesDeleteService.ExecuteAsync` — dentro do lambda, logo depois de `SalesInvoiceNfeLock.EnsureDeletable(entity);`:
  ```csharp
            await StorageCustodyGuard.EnsureSalesHasNoLaterStepAsync(db.Context, entity.Key);
  ```
- `StorageTransactionsCancelService.ExecuteAsync` e `StorageTransactionsReverseService.ExecuteAsync` — logo depois de
  `await StorageDepositRules.EnsureTransactionsNotReconciledAsync(db.Context, [doc.Key]);`:
  ```csharp
        await StorageCustodyGuard.EnsureTransactionsHaveNoCustodyAsync(db.Context, [doc.Key]);
  ```
- `OwnershipTransfersCancelService.ExecuteAsync` — logo depois da recusa de transferência já cancelada, FORA do try:
  ```csharp
        // Notas de custódia (spec 2026-10-12 §4.5): retorno simbólico na origem e entrada no destino precisam ser
        // canceladas antes — os romaneios que o cancelamento cria não esperam nota, e as notas ficariam órfãs.
        var transferTransactionKeys = await db.Context.StorageTransactions.AsNoTracking()
            .Where(t => t.OwnershipTransferKey == ownershipTransfer.Key).Select(t => t.Key).ToListAsync();
        await StorageCustodyGuard.EnsureTransactionsHaveNoCustodyAsync(db.Context, transferTransactionKeys);
  ```
- `StorageAddressesUpdateService.ExecuteAsync` — logo depois da trava da natureza:
  ```csharp
        // Beneficiamento fiscal (spec 2026-10-12 §3.2): trocar com nota de custódia ativa mudaria as etapas esperadas
        // de romaneios que já têm nota. OriginalValues pelo mesmo motivo da natureza (PATCH chega mutado).
        var originalProcessing = context.Entry(existingAddress).OriginalValues.GetValue<bool>(nameof(StorageAddress.RequiresProcessing));
        if (entity.RequiresProcessing != originalProcessing)
        {
            var lotTransactionKeys = await context.StorageTransactions.AsNoTracking()
                .Where(t => t.StorageAddressCode == code).Select(t => t.Key).ToListAsync();

            if ((await StorageCustodyQuery.ActiveDocumentsAsync(context, lotTransactionKeys)).Count > 0)
                throw new ApplicationException("O lote já possui notas fiscais de custódia; não é possível alterar o beneficiamento.");
        }
  ```
  (`using SiagroB1.Infra.Queries;`.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageCustody|FullyQualifiedName~OwnershipTransfers|FullyQualifiedName~StorageTransactions|FullyQualifiedName~StorageAddress|FullyQualifiedName~Cancel|FullyQualifiedName~Delete"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application SiagroB1.Application.Tests
git commit -m "feat(storage): notas de custódia travam estorno e se desfazem na ordem inversa" -m "Cancelar ou excluir a entrada para beneficiamento exige cancelar antes a devolução simbólica, e esta a entrada para armazenagem. Romaneio e transferência com nota de custódia ativa não são estornados, e o lote não troca o beneficiamento fiscal com nota emitida." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

## Task 6: "Gerar nota" e seção do romaneio (serviços + endpoints)

**Files:**
- Create: `SiagroB1.Application/Services/StorageCustody/StorageTransactionsCreateCustodyDocumentService.cs`, `SiagroB1.Application/Services/StorageCustody/StorageTransactionsCustodySummaryService.cs`, `SiagroB1.Web/Actions/StorageCustody/StorageTransactionsCreateCustodyDocumentController.cs`, `SiagroB1.Web/Functions/StorageCustody/StorageTransactionsCustodySummaryController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/StorageCustody/StorageTransactionsCreateCustodyDocumentServiceTests.cs`, `SiagroB1.Application.Tests/StorageCustody/StorageTransactionsCustodySummaryServiceTests.cs` (novos)

**Interfaces:**
- Consumes: Tasks 3–4.
- Produces:
  - `record StorageCustodyDocumentRequest(Guid StorageTransactionKey, StorageCustodyRole Role, int UsageCode, decimal UnitPrice)`
  - `StorageTransactionsCreateCustodyDocumentService.ExecuteAsync(StorageCustodyDocumentRequest, string userName) : Task<Guid>` (chave do documento criado)
  - `StorageTransactionsCustodySummaryService.ExecuteAsync(Guid key) : Task<StorageCustodySummaryDto>`
  - OData: action `StorageTransactionsCreateCustodyDocument(StorageTransactionKey: Guid, Role: string, UsageCode: int, UnitPrice: double) → Guid`;
    function `StorageTransactionsCustodySummary(Key=…) → StorageCustodySummaryDto`.

- [ ] **Step 1: Write the failing tests**

`StorageTransactionsCreateCustodyDocumentServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageCustody;

public class StorageTransactionsCreateCustodyDocumentServiceTests
{
    private static StorageTransactionsCreateCustodyDocumentService Service(UnitOfWork db, string erp = "STANDALONE")
    {
        var partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [StorageCustodySeed.Depositor] = "PRODUTOR UM" },
            states: new Dictionary<string, string> { [StorageCustodySeed.Depositor] = "SP" });
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var sales = new SalesInvoicesCreateService(
            db, partners, new FakeItemService(), new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages), new SalesInvoicesCfopResolveService(db, usages, partners),
            TaxTestServices.InactiveApply(db), TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesCreateService>.Instance);
        var purchases = new PurchaseInvoicesCreateService(
            db, partners, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db), new FakeDocNumberSequenceService());
        return new(db, TestConfiguration.Erp(erp), partners, usages, purchases, sales,
            NullLogger<StorageTransactionsCreateCustodyDocumentService>.Instance);
    }

    private static async Task<(UnitOfWork Db, StorageCustodySeed Seed)> SeedAsync(bool processing = true)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db, processing);
        // A natureza precisa de efeito configurado para o documento de saída (SalesInvoicesUsageGuardService).
        return (db, seed);
    }

    [Fact]
    public async Task Quantities_follow_gross_gross_net()
    {
        var (db, seed) = await SeedAsync();
        var t = await seed.AddTransactionAsync(gross: 30_000m, net: 28_000m);

        var entryKey = await Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingEntry, StorageCustodySeed.ProcessingEntryUsage, 2.5m), "tester");

        var entry = await db.Context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == entryKey);
        Assert.Equal(DocumentIssuerType.Own, entry.IssuerType);
        Assert.Equal(InvoiceStatus.Pending, entry.InvoiceStatus);
        Assert.Equal(StorageCustodySeed.Depositor, entry.CardCode);
        Assert.Equal(t.Key, entry.CustodyStorageTransactionKey);
        Assert.Equal(StorageCustodyRole.ProcessingEntry, entry.CustodyRole);
        Assert.Equal(30_000m, Assert.Single(entry.Items).Quantity);
        Assert.Equal(2.5m, entry.Items.Single().UnitPrice);
        Assert.Equal(30_000m, entry.GrossWeight);
        Assert.Equal("ABC1D23", entry.TruckCode);

        // A entrada é "autorizada" para liberar a devolução simbólica.
        entry.NfeStatus = NfeStatus.Authorized;
        entry.InvoiceStatus = InvoiceStatus.Confirmed;
        entry.ChaveNFe = StorageCustodySeed.AccessKey;
        await db.SaveChangesAsync();

        var symbolicKey = await Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingSymbolicReturn, StorageCustodySeed.SymbolicReturnUsage, 2.5m), "tester");
        var symbolic = await db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == symbolicKey);
        Assert.Equal(30_000m, Assert.Single(symbolic.Items).Quantity);
        Assert.Equal(StorageCustodyRole.ProcessingSymbolicReturn, symbolic.CustodyRole);

        var storageKey = await Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.StorageEntry, StorageCustodySeed.StorageEntryUsage, 2.5m), "tester");
        Assert.Equal(28_000m, (await db.Context.PurchaseInvoicesItems.SingleAsync(l => l.PurchaseInvoiceKey == storageKey)).Quantity);
    }

    [Fact]
    public async Task Symbolic_return_takes_the_quantity_of_the_producer_invoice()
    {
        var (db, seed) = await SeedAsync();
        var t = await seed.AddTransactionAsync(gross: 30_000m);
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 29_500m, DocumentIssuerType.ThirdParty,
            InvoiceStatus.Confirmed, accessKey: StorageCustodySeed.AccessKey);

        var key = await Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingSymbolicReturn, StorageCustodySeed.SymbolicReturnUsage, 2m), "tester");

        Assert.Equal(29_500m, (await db.Context.SalesInvoicesItems.SingleAsync(l => l.SalesInvoiceKey == key)).Quantity);
    }

    [Fact]
    public async Task Shipment_generates_the_return_by_the_net_weight()
    {
        var (db, seed) = await SeedAsync();
        var t = await seed.AddTransactionAsync(StorageTransactionType.Shipment, gross: 15_200m, net: 15_000m);

        var key = await Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.StorageReturn, StorageCustodySeed.StorageReturnUsage, 2m), "tester");

        Assert.Equal(15_000m, (await db.Context.SalesInvoicesItems.SingleAsync(l => l.SalesInvoiceKey == key)).Quantity);
    }

    [Fact]
    public async Task Refuses_in_sap_mode()
    {
        var (db, seed) = await SeedAsync();
        var t = await seed.AddTransactionAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, "SAPB1").ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingEntry, StorageCustodySeed.ProcessingEntryUsage, 2m), "tester"));

        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }

    [Fact]
    public async Task Refuses_usage_of_another_role_and_non_positive_price()
    {
        var (db, seed) = await SeedAsync();
        var t = await seed.AddTransactionAsync();

        var usage = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingEntry, StorageCustodySeed.StorageEntryUsage, 2m), "tester"));
        Assert.Equal("Escolha uma natureza de entrada para beneficiamento.", usage.Message);

        var price = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingEntry, StorageCustodySeed.ProcessingEntryUsage, 0m), "tester"));
        Assert.Equal("Informe o preço unitário da nota.", price.Message);
    }

    [Fact]
    public async Task Symbolic_return_without_processing_entry_is_refused()
    {
        var (db, seed) = await SeedAsync();
        var t = await seed.AddTransactionAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingSymbolicReturn, StorageCustodySeed.SymbolicReturnUsage, 2m), "tester"));

        Assert.Equal("Gere antes a entrada para beneficiamento deste romaneio.", e.Message);
    }

    [Fact]
    public async Task Regenerates_after_the_previous_was_cancelled()
    {
        var (db, seed) = await SeedAsync(processing: false);
        var t = await seed.AddTransactionAsync();
        var first = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.StorageEntry, 28_000m);

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.StorageEntry, StorageCustodySeed.StorageEntryUsage, 2m), "tester"));

        first.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();
        await Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.StorageEntry, StorageCustodySeed.StorageEntryUsage, 2m), "tester");
    }
}
```

Se o `SalesInvoicesUsageGuardService` recusar "natureza sem efeito configurado", o efeito já existe no seed (as
`UsageEffect` de custódia). Se `TaxTestServices.InactivePurchaseApply` não existir com esse nome, use o helper de
`PurchaseInvoicesItemsTests`.

`StorageTransactionsCustodySummaryServiceTests.cs`:

```csharp
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.StorageCustody;

public class StorageTransactionsCustodySummaryServiceTests
{
    [Fact]
    public async Task Lists_the_expected_steps_with_their_documents()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);

        var summary = await new StorageTransactionsCustodySummaryService(db).ExecuteAsync(t.Key);

        Assert.True(summary.Applies);
        Assert.Equal(3, summary.Steps.Count);
        Assert.Equal(StorageCustodyStepSituation.Generated, summary.Steps[0].Situation);
        Assert.Equal(StorageCustodyRole.ProcessingSymbolicReturn, summary.NextRole);
    }

    [Fact]
    public async Task Own_lot_romaneio_does_not_apply()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        seed.Lot.OwnershipType = StorageOwnershipType.OwnedInOurCustody;
        await db.SaveChangesAsync();
        var t = await seed.AddTransactionAsync();

        var summary = await new StorageTransactionsCustodySummaryService(db).ExecuteAsync(t.Key);

        Assert.False(summary.Applies);
        Assert.Empty(summary.Steps);
    }

    [Fact]
    public async Task Unknown_romaneio_is_not_found()
    {
        var db = TestDb.CreateUnitOfWork();

        await Assert.ThrowsAsync<NotFoundException>(() => new StorageTransactionsCustodySummaryService(db).ExecuteAsync(Guid.NewGuid()));
    }
}
```

E no `StorageCustodyModelTests` (Task 1), um teste do EDM das operações:

```csharp
    [Fact]
    public void Edm_declares_the_custody_operations()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();

        var action = model.SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "StorageTransactionsCreateCustodyDocument");
        Assert.Equal(["StorageTransactionKey", "Role", "UsageCode", "UnitPrice"], action.Parameters.Select(p => p.Name));
        Assert.Contains(model.SchemaElements.OfType<IEdmFunction>(), f => f.Name == "StorageTransactionsCustodySummary");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageTransactionsCreateCustodyDocument|FullyQualifiedName~StorageTransactionsCustodySummary|FullyQualifiedName~StorageCustodyModelTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implement the services**

`StorageTransactionsCreateCustodyDocumentService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.StorageCustody;

public sealed record StorageCustodyDocumentRequest(Guid StorageTransactionKey, StorageCustodyRole Role, int UsageCode, decimal UnitPrice);

/// <summary>
/// "Gerar nota" no romaneio (spec 2026-10-12 §4.1): cria a nota de custódia PRÓPRIA Pendente — Documento de Entrada
/// para as entradas, de Saída para a devolução simbólica e os retornos — ligada ao romaneio, para o depositante, com o
/// produto e a quantidade da etapa. Quem confere ordem, unicidade e chave é a StorageCustodyGuard, chamada pelos
/// serviços de criação. Não confirma nem transmite.
/// </summary>
public class StorageTransactionsCreateCustodyDocumentService(
    IUnitOfWork db,
    IConfiguration configuration,
    IBusinessPartnerService businessPartners,
    IUsage usages,
    PurchaseInvoicesCreateService purchaseCreate,
    SalesInvoicesCreateService salesCreate,
    ILogger<StorageTransactionsCreateCustodyDocumentService> logger)
{
    public async Task<Guid> ExecuteAsync(StorageCustodyDocumentRequest request, string userName)
    {
        // TODA a validação antes de qualquer escrita.
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(StorageCustodyGuard.StandaloneOnlyMessage);

        var transaction = await StorageCustodyQuery.Transactions(db.Context)
                              .FirstOrDefaultAsync(t => t.Key == request.StorageTransactionKey)
                          ?? throw new NotFoundException("Romaneio de lote de armazenagem de terceiro não encontrado.");

        var label = StorageCustodyRules.RoleText(request.Role).ToLowerInvariant();

        if (!StorageCustodyRules.ExpectedRoles(transaction).Contains(request.Role))
            throw new DefaultException($"O romaneio {transaction.Code} não espera a nota de {label}.");

        var usage = await usages.GetByIdAsync(request.UsageCode);
        if (usage is null || usage.StorageCustodyRole != request.Role)
            throw new DefaultException($"Escolha uma natureza de {label}.");

        if (request.UnitPrice <= 0)
            throw new DefaultException("Informe o preço unitário da nota.");

        if (await businessPartners.GetByIdAsync(transaction.CardCode) is null)
            throw new DefaultException(
                $"O depositante {transaction.CardCode} não tem parceiro de negócio cadastrado: a nota precisa de destinatário.");

        var quantity = request.Role == StorageCustodyRole.ProcessingSymbolicReturn
            ? (await StorageCustodyQuery.ActiveDocumentsAsync(db.Context, [transaction.Key]))
                  .FirstOrDefault(d => d.Role == StorageCustodyRole.ProcessingEntry)?.Quantity
              ?? throw new DefaultException("Gere antes a entrada para beneficiamento deste romaneio.")
            : StorageCustodyRules.ExpectedQuantity(request.Role, transaction, null)!.Value;

        var today = DateTime.Now.Date;
        var comments = $"{StorageCustodyRules.RoleText(request.Role)} — romaneio {transaction.Code}, lote {transaction.StorageAddressCode}.";

        try
        {
            await db.BeginTransactionAsync();
            Guid key;

            if (StorageCustodyRules.IsPurchaseRole(request.Role))
            {
                var invoice = new PurchaseInvoice
                {
                    Key = Guid.NewGuid(),
                    BranchCode = transaction.BranchCode,
                    CardCode = transaction.CardCode,
                    CardName = transaction.CardName,
                    IssuerType = DocumentIssuerType.Own,
                    InvoiceType = PurchaseInvoiceType.Normal,
                    TaxDocumentKind = TaxDocumentKind.Nfe,
                    IssueDate = today,
                    PostingDate = today,
                    CustodyStorageTransactionKey = transaction.Key,
                    // A emissão exige pesos; nascem iguais à quantidade e ficam editáveis enquanto Pendente.
                    GrossWeight = quantity,
                    NetWeight = quantity,
                    TruckCode = transaction.TruckCode,
                    Comments = comments,
                };
                invoice.AddItem(new PurchaseInvoiceItem
                {
                    Key = Guid.NewGuid(),
                    ItemCode = transaction.ItemCode,
                    ItemName = transaction.ItemName,
                    UnitOfMeasureCode = transaction.UoM,
                    Quantity = quantity,
                    UnitPrice = request.UnitPrice,
                    UsageCode = usage.Code,
                });

                await purchaseCreate.ExecuteAsync(invoice, userName);
                key = invoice.Key;
            }
            else
            {
                var invoice = new SalesInvoice
                {
                    Key = Guid.NewGuid(),
                    BranchCode = transaction.BranchCode,
                    CardCode = transaction.CardCode,
                    InvoiceType = SalesInvoiceType.Normal,
                    InvoiceDate = today,
                    CustodyStorageTransactionKey = transaction.Key,
                    GrossWeight = quantity,
                    NetWeight = quantity,
                    TruckCode = transaction.TruckCode,
                    Comments = comments,
                    Items =
                    [
                        new SalesInvoiceItem
                        {
                            ItemCode = transaction.ItemCode,
                            UnitOfMeasureCode = transaction.UoM,
                            Quantity = quantity,
                            UnitPrice = request.UnitPrice,
                            UsageCode = usage.Code,
                        },
                    ],
                };

                await salesCreate.ExecuteAsync(invoice, userName, CommitMode.Deferred);
                await db.SaveChangesAsync();
                key = invoice.Key;
            }

            await db.CommitAsync();
            return key;
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, "Erro ao gerar a nota de custódia do romaneio {Key}", transaction.Key);
            throw;
        }
    }
}
```

(Se `PurchaseInvoice.AddItem` não aceitar a linha sem `PurchaseInvoiceKey`, ou o compilador recusar outro campo,
compare com `PurchaseInvoicesCounterNoteCreateService`, que monta uma entrada própria do mesmo jeito.)

`StorageTransactionsCustodySummaryService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.StorageCustody;

/// <summary>Seção "Notas fiscais de custódia" do romaneio (spec 2026-10-12 §5): mesmas regras do relatório.</summary>
public class StorageTransactionsCustodySummaryService(IUnitOfWork db)
{
    public async Task<StorageCustodySummaryDto> ExecuteAsync(Guid key)
    {
        if (!await db.Context.StorageTransactions.AsNoTracking().AnyAsync(t => t.Key == key))
            throw new NotFoundException("Romaneio não encontrado.");

        var transaction = await StorageCustodyQuery.Transactions(db.Context).FirstOrDefaultAsync(t => t.Key == key);
        if (transaction is null)
            return new StorageCustodySummaryDto { TransactionKey = key };

        var steps = StorageCustodyRules.BuildSteps(transaction, await StorageCustodyQuery.ActiveDocumentsAsync(db.Context, [key]));

        return new StorageCustodySummaryDto
        {
            TransactionKey = key,
            Applies = steps.Count > 0,
            Steps = steps,
            NextRole = StorageCustodyRules.NextRole(steps),
        };
    }
}
```

- [ ] **Step 4: Endpoints, EDM and DI**

`SiagroB1.Web/Actions/StorageCustody/StorageTransactionsCreateCustodyDocumentController.cs`:

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.StorageCustody;

/// <summary>"Gerar nota" no romaneio: cria a nota de custódia Pendente e devolve a chave do documento.</summary>
public class StorageTransactionsCreateCustodyDocumentController(StorageTransactionsCreateCustodyDocumentService service) : ODataController
{
    [HttpPost("odata/StorageTransactionsCreateCustodyDocument")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM. Role vai como o NOME do enum.
        if (parameters is null
            || !parameters.TryGetValue("StorageTransactionKey", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key)
            || !parameters.TryGetValue("Role", out var roleObj) || !Enum.TryParse<StorageCustodyRole>(roleObj?.ToString(), out var role)
            || role == StorageCustodyRole.None
            || !parameters.TryGetValue("UsageCode", out var usageObj) || usageObj is null
            || !parameters.TryGetValue("UnitPrice", out var priceObj) || priceObj is null)
            return BadRequest("Parâmetros obrigatórios: StorageTransactionKey, Role, UsageCode, UnitPrice.");

        try
        {
            var created = await service.ExecuteAsync(
                new StorageCustodyDocumentRequest(key, role, Convert.ToInt32(usageObj, CultureInfo.InvariantCulture),
                    Convert.ToDecimal(priceObj, CultureInfo.InvariantCulture)),
                User.Identity?.Name ?? "Unknown");
            return Ok(created);
        }
        catch (NotFoundException e) { return NotFound(e.Message); }
        catch (DefaultException e) { return BadRequest(e.Message); }
        catch (ApplicationException e) { return BadRequest(e.Message); }
    }
}
```

`SiagroB1.Web/Functions/StorageCustody/StorageTransactionsCustodySummaryController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.StorageCustody;

/// <summary>Notas fiscais de custódia do romaneio.</summary>
public class StorageTransactionsCustodySummaryController(StorageTransactionsCustodySummaryService service) : ODataController
{
    /// <summary>Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.</summary>
    [HttpGet("odata/StorageTransactionsCustodySummary(Key={key})")]
    public async Task<ActionResult<StorageCustodySummaryDto>> Get([FromRoute] Guid key)
    {
        try
        {
            return Ok(await service.ExecuteAsync(key));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }
}
```

`ODataConfigurations.cs` — logo depois do bloco `shipmentReleasesStorageRemittanceSummary`:

```csharp
        // Notas de custódia em armazém próprio (spec 2026-10-12). Role vai como string (nome do enum) e UnitPrice como
        // double: enum e Edm.Decimal em parâmetro de action dão trabalho no OData.
        var storageTransactionsCreateCustodyDocument = modelBuilder.Action("StorageTransactionsCreateCustodyDocument");
        storageTransactionsCreateCustodyDocument.Parameter<Guid>("StorageTransactionKey");
        storageTransactionsCreateCustodyDocument.Parameter<string>("Role");
        storageTransactionsCreateCustodyDocument.Parameter<int>("UsageCode");
        storageTransactionsCreateCustodyDocument.Parameter<double>("UnitPrice");
        storageTransactionsCreateCustodyDocument.Returns<Guid>();

        var storageTransactionsCustodySummary = modelBuilder.Function("StorageTransactionsCustodySummary");
        storageTransactionsCustodySummary.Parameter<Guid>("Key");
        storageTransactionsCustodySummary.Returns<StorageCustodySummaryDto>();
```

`ServiceCollectionExtensions.AddApplicationServices` — perto de `ShipmentReleasesCreateStorageRemittanceService`:

```csharp
        services.AddScoped<StorageTransactionsCreateCustodyDocumentService>();
        services.AddScoped<StorageTransactionsCustodySummaryService>();
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageCustody|FullyQualifiedName~StorageTransactions"`
Expected: build sem erros; PASS.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application SiagroB1.Web SiagroB1.Application.Tests
git commit -m "feat(storage): gerar nota de custódia e resumo das etapas no romaneio" -m "A ação do romaneio cria a nota própria Pendente da etapa (entrada no Documento de Entrada; devolução simbólica e retornos no de Saída) com a quantidade da etapa, o depositante e a placa do romaneio; a função lista as etapas esperadas com a nota de cada uma." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

## Task 7: NF-e das notas de custódia (sem pagamento, NFref da devolução simbólica)

**Files:**
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueContext.cs`, `NfeReadinessValidator.cs`, `NfeIssueInputAssembler.cs`, `SalesInvoicesNfeIssueService.cs`
- Test: `SiagroB1.Application.Tests/Nfe/StorageCustodyNfeTests.cs` (novo)

**Interfaces:**
- Consumes: `SalesInvoice.CustodyRole`, `PurchaseInvoice.CustodyRole`, `StorageCustodyGuard.ProcessingEntryAccessKeyAsync` (Task 4).
- Produces: `NfeIssueContext.CustodyReferencedKey : string?` (último parâmetro opcional).

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Nfe/StorageCustodyNfeTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Payments;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Nota de custódia não tem pagamento (tPag 90); a devolução simbólica referencia a entrada (spec §4.2, §4.3).</summary>
public class StorageCustodyNfeTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(-3));
    private const string EntryKey = "35261068583898000101550090000000011990112066";

    private static async Task<(SalesInvoice Invoice, NfeIssueContext Context, NfeOptions Options)> PrepareAsync(StorageCustodyRole role)
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var invoice = await scenario.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == scenario.InvoiceKey);
        invoice.TaxDocumentNumber = "000000001";
        invoice.TaxDocumentSeries = "1";
        invoice.NfeRandomCode = "48151623";
        invoice.CustodyRole = role;
        invoice.PaymentConditionCode = null;
        await scenario.Db.SaveChangesAsync();

        var options = new NfeOptions(NfeTestSeed.Config());
        // Sem condição de pagamento e sem recusa: a nota de custódia não tem pagamento.
        var context = await new NfeReadinessValidator(scenario.Db, options).ValidateAsync(invoice);
        return (invoice, context, options);
    }

    [Fact]
    public async Task Symbolic_return_references_the_processing_entry_and_has_no_payment()
    {
        var (invoice, context, options) = await PrepareAsync(StorageCustodyRole.ProcessingSymbolicReturn);

        var input = NfeIssueInputAssembler.Build(invoice, context with { CustodyReferencedKey = EntryKey }, IssuedAt, options.TechnicalResponsible);

        Assert.Equal([EntryKey], input.ReferencedKeys);
        Assert.Equal(PaymentMeansCodes.NoPayment, input.Payment.PaymentMeans);
    }

    [Fact]
    public async Task Storage_return_has_no_reference_and_no_payment()
    {
        var (invoice, context, options) = await PrepareAsync(StorageCustodyRole.StorageReturn);

        var input = NfeIssueInputAssembler.Build(invoice, context, IssuedAt, options.TechnicalResponsible);

        Assert.Empty(input.ReferencedKeys);
        Assert.Equal(PaymentMeansCodes.NoPayment, input.Payment.PaymentMeans);
    }
}
```

(Se o `NfeTestSeed` deixar o documento de outro jeito que a prontidão recuse — pesos, por exemplo — siga o que
`NfeIssueInputAssemblerTests.BuildAsync` faz antes de validar.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageCustodyNfeTests"`
Expected: FAIL (`CustodyReferencedKey` não existe; a prontidão exige condição de pagamento).

- [ ] **Step 3: Implement**

`NfeIssueContext.cs` — último parâmetro do record e o `<param>`:

```csharp
/// <param name="CustodyReferencedKey">Na devolução simbólica do beneficiamento, a chave da entrada (NFref; spec 2026-10-12 §4.3).</param>
...
    NfeCounterNoteOrigin? ProducerInvoice = null,
    string? CustodyReferencedKey = null);
```

`NfeReadinessValidator` — nos dois `ValidateAsync`, trocar `RequiresPayment: !invoice.IsNfeReturn,` por:

```csharp
                // Devolução e nota de custódia (armazenagem/beneficiamento) não têm pagamento (tPag 90).
                RequiresPayment: !invoice.IsNfeReturn && invoice.CustodyRole == StorageCustodyRole.None,
```

`NfeIssueInputAssembler`:

1. `NfeDocumentView` ganha o último parâmetro `bool WithoutPayment`.
2. `Build(SalesInvoice …)`:
   ```csharp
        IReadOnlyList<string> referencedKeys = returnOrigin is not null ? [returnOrigin.AccessKey]
            : context.CustodyReferencedKey is { } custodyKey ? [custodyKey]
            : [];
   ```
   passar `referencedKeys` no lugar de `returnOrigin is null ? [] : [returnOrigin.AccessKey]` e, no fim do
   `new NfeDocumentView(...)`, `WithoutPayment: returnOrigin is not null || invoice.CustodyRole != StorageCustodyRole.None`.
3. `Build(PurchaseInvoice …)`: no fim do `new NfeDocumentView(...)`,
   `WithoutPayment: returnOrigin is not null || invoice.CustodyRole != StorageCustodyRole.None`.
4. No `Build` privado, trocar a condição do pagamento:
   ```csharp
            // Devolução e nota de custódia: tPag 90 com vPag 0 (rejeição 871) e sem cobr.
            Payment = view.WithoutPayment
                ? new PaymentPlan(PaymentMeansCodes.NoPayment, null, 0m, [])
                : PaymentInstallmentCalculator.Calculate(…como hoje…),
   ```

`SalesInvoicesNfeIssueService.ValidateReadinessAsync`:

```csharp
    /// <summary>Devolução simbólica do beneficiamento: NFref para a entrada do mesmo romaneio (spec 2026-10-12 §4.3).</summary>
    protected override async Task<NfeIssueContext> ValidateReadinessAsync(SalesInvoice invoice)
    {
        var context = await readiness.ValidateAsync(invoice);
        var custodyKey = await StorageCustodyGuard.ProcessingEntryAccessKeyAsync(_db.Context, invoice);

        return custodyKey is null ? context : context with { CustodyReferencedKey = custodyKey };
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Nfe"`
Expected: PASS (novos e todos os antigos de NF-e — a venda continua com pagamento).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application SiagroB1.Application.Tests/Nfe
git commit -m "feat(invoice): NF-e das notas de custódia sem pagamento e com NFref na devolução simbólica" -m "Notas de beneficiamento e armazenagem não têm pagamento (tPag 90) e não exigem condição; a devolução simbólica do beneficiamento referencia a chave da entrada do mesmo romaneio, própria autorizada ou escriturada." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

## Task 8: Relatório "Custódia de Armazenagem"

**Files:**
- Create: `SiagroB1.Reports/Dtos/StorageCustodyByPeriodRequest.cs`, `SiagroB1.Reports/Dtos/StorageCustodyRowDto.cs`, `SiagroB1.Reports/Services/StorageCustodyByPeriodReportService.cs`, `SiagroB1.Reports/Controllers/StorageCustodyByPeriodController.cs`, `SiagroB1.Reports/Reports/Templates/StorageCustodyByPeriod.frx`, `SiagroB1.Migrations/CommonContext/<timestamp>_AddStorageCustodyReportMenu.cs` (+ `.Designer.cs`)
- Modify: `SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs`, `SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs`
- Test: `SiagroB1.Application.Tests/Reports/StorageCustodyByPeriodReportServiceTests.cs`, `StorageCustodyByPeriodControllerTests.cs` (novos)

**Interfaces:**
- Consumes: `StorageCustodyQuery`, `StorageCustodyRules` (Task 3); `StorageTransactionsCustodySummaryService` (teste cruzado, Task 6).
- Produces: `POST /reports/StorageCustodyByPeriod` → `storage-custody-by-period.pdf`; menu `storageCustodyReport`.

- [ ] **Step 1: Write the failing tests**

`StorageCustodyByPeriodReportServiceTests.cs`:

```csharp
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class StorageCustodyByPeriodReportServiceTests
{
    private static StorageCustodyByPeriodRequest Request(StorageCustodySituationFilter situation = StorageCustodySituationFilter.All) =>
        new() { FromDate = new DateTime(2026, 10, 1), ToDate = new DateTime(2026, 10, 31), Situation = situation };

    private static readonly DateTime Oct10 = new(2026, 10, 10);

    [Fact]
    public async Task Each_romaneio_is_followed_by_its_expected_steps()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(gross: 30_000m, net: 28_000m, date: Oct10, code: "RM000048");
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 29_500m, DocumentIssuerType.ThirdParty, InvoiceStatus.Confirmed);

        var rows = await new StorageCustodyByPeriodReportService(db, new RecordingFastReportService()).BuildRowsAsync(Request());

        Assert.Equal(4, rows.Count);
        Assert.Equal(StorageCustodyRowKind.Romaneio, rows[0].Kind);
        Assert.Equal("RM000048", rows[0].Transaction);
        Assert.Equal("30.000,000", rows[0].GrossWeightText);
        Assert.Equal("Escriturada", rows[1].Situation);
        Assert.Equal("-500,000", rows[1].DifferenceText);
        Assert.Equal("PENDENTE", rows[2].Situation);
        Assert.Equal("PENDENTE", rows[3].Situation);
        Assert.Equal(1, rows[0].PendingCount);
    }

    [Fact]
    public async Task Lot_balances_compare_physical_and_fiscal_at_the_end_date()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db, requiresProcessing: false);
        var receipt = await seed.AddTransactionAsync(net: 28_000m, date: Oct10);
        var shipment = await seed.AddTransactionAsync(StorageTransactionType.Shipment, net: 10_000m, date: Oct10);
        await seed.AddPurchaseDocumentAsync(receipt, StorageCustodyRole.StorageEntry, 28_000m);
        await seed.AddSalesDocumentAsync(shipment, StorageCustodyRole.StorageReturn, 10_000m);
        await seed.AddTransactionAsync(net: 5_000m, date: new DateTime(2026, 11, 2)); // depois do período: fora

        var rows = await new StorageCustodyByPeriodReportService(db, new RecordingFastReportService()).BuildRowsAsync(Request());

        Assert.All(rows, r => Assert.Equal(18_000m, r.LotPhysicalBalance));
        Assert.All(rows, r => Assert.Equal(18_000m, r.LotFiscalBalance));
        Assert.All(rows, r => Assert.Equal(0m, r.LotDifference));
    }

    [Fact]
    public async Task Situation_filter_keeps_pending_or_complete_romaneios()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db, requiresProcessing: false);
        var complete = await seed.AddTransactionAsync(date: Oct10, code: "RM000001");
        await seed.AddPurchaseDocumentAsync(complete, StorageCustodyRole.StorageEntry, 28_000m);
        await seed.AddTransactionAsync(date: Oct10, code: "RM000002");
        var service = new StorageCustodyByPeriodReportService(db, new RecordingFastReportService());

        var pending = await service.BuildRowsAsync(Request(StorageCustodySituationFilter.Pending));
        var done = await service.BuildRowsAsync(Request(StorageCustodySituationFilter.Complete));

        Assert.Equal(["RM000002"], pending.Where(r => r.Kind == StorageCustodyRowKind.Romaneio).Select(r => r.Transaction));
        Assert.Equal(["RM000001"], done.Where(r => r.Kind == StorageCustodyRowKind.Romaneio).Select(r => r.Transaction));
    }

    [Fact]
    public async Task Romaneio_without_expected_steps_is_left_out()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        await seed.AddTransactionAsync(status: StorageTransactionsStatus.Pending, date: Oct10);

        Assert.Empty(await new StorageCustodyByPeriodReportService(db, new RecordingFastReportService()).BuildRowsAsync(Request()));
    }

    [Fact]
    public async Task Steps_match_the_romaneio_section()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync(date: Oct10);
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);

        var rows = await new StorageCustodyByPeriodReportService(db, new RecordingFastReportService()).BuildRowsAsync(Request());
        var summary = await new StorageTransactionsCustodySummaryService(db).ExecuteAsync(t.Key);

        Assert.Equal(
            summary.Steps.Select(s => s.SituationText),
            rows.Where(r => r.Kind == StorageCustodyRowKind.Step).Select(r => r.Situation));
    }
}
```

`StorageCustodyByPeriodControllerTests.cs` — copie `StorageRemittancesByPeriodControllerTests` trocando o tipo do
controller/serviço/request, o nome do arquivo (`storage-custody-by-period.pdf`) e mantendo os três testes (SAPB1
recusado, período invertido, PDF inline).

`LogisticsReportsLayoutTests.cs`:
- acrescentar `"StorageCustodyByPeriod",` em `Templates`;
- acrescentar em `LongestText`:
  ```csharp
        ["StorageCustodyByPeriod"] = new()
        {
            ["txtTransaction"] = 9,   // RM0000123 (VARCHAR do código)
            ["txtDescription"] = 25,  // Expedição (transferência)
            ["txtDate"] = 10,         // 31/12/2026
            ["txtCfop"] = 4,          // 5902
            ["txtDocument"] = 9,      // DE0000026
            ["txtTaxDocument"] = 13,  // 000000038-001
            ["txtSituation"] = 11,    // Escriturada
            ["txtNfeStatus"] = 16,    // Em processamento
            ["txtGrossWeight"] = 14,  // 99.999.999,999
            ["txtNetWeight"] = 14,
            ["txtQuantity"] = 14,
            ["txtDifference"] = 15,   // -10.000.000,000
        },
  ```

`LogisticsReportsPdfTests.cs` — dois testes no fim da classe, no molde dos de remessas:

```csharp
    [Fact]
    public async Task StorageCustodyByPeriod_ProducesAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var oct10 = new DateTime(2026, 10, 10);
        var t = await seed.AddTransactionAsync(gross: 99_999_999.999m, net: 99_000_000m, date: oct10, code: "RM000048");
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 99_999_999.999m, DocumentIssuerType.ThirdParty, InvoiceStatus.Confirmed);
        await seed.AddTransactionAsync(StorageTransactionType.Shipment, net: 10_000m, date: oct10, code: "RM000049");

        var pdf = await new StorageCustodyByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new StorageCustodyByPeriodRequest { FromDate = new DateTime(2026, 10, 1), ToDate = new DateTime(2026, 10, 31) });

        Keep("StorageCustodyByPeriod", "STANDALONE", pdf);
    }

    [Fact]
    public async Task StorageCustodyByPeriod_EmptyResultStillProducesAPdf()
    {
        var db = TestDb.CreateUnitOfWork();

        var pdf = await new StorageCustodyByPeriodReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new StorageCustodyByPeriodRequest { FromDate = new DateTime(2026, 10, 1), ToDate = new DateTime(2026, 10, 31) });

        Assert.NotEmpty(pdf);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageCustodyByPeriod|FullyQualifiedName~LogisticsReports"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implement DTOs, service and controller**

`SiagroB1.Reports/Dtos/StorageCustodyByPeriodRequest.cs`:

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>Situação fiscal do romaneio: Completa = toda etapa esperada tem nota ativa.</summary>
public enum StorageCustodySituationFilter
{
    All = 0,
    Pending = 1,
    Complete = 2,
}

/// <summary>Filtros de "Custódia de Armazenagem" (período sobre a data do romaneio). Filial/depositante/produto/lote do LOTE.</summary>
public class StorageCustodyByPeriodRequest : LogisticsReportRequest
{
    public string? CardCode { get; set; }

    public string? StorageAddressCode { get; set; }

    public StorageCustodySituationFilter Situation { get; set; } = StorageCustodySituationFilter.All;
}
```

`SiagroB1.Reports/Dtos/StorageCustodyRowDto.cs`:

```csharp
namespace SiagroB1.Reports.Dtos;

public static class StorageCustodyRowKind
{
    public const string Romaneio = "Romaneio";
    public const string Step = "Etapa";
}

/// <summary>
/// Linha achatada de "Custódia de Armazenagem": o romaneio (negrito) e, logo abaixo, uma linha por etapa esperada
/// (itálico; "PENDENTE" em negrito quando sem nota). Os saldos do lote se repetem em toda linha do grupo — o rodapé do
/// grupo imprime os da última. Textos já formatados; os decimais só alimentam o rodapé.
/// </summary>
public class StorageCustodyRowDto
{
    public string Group { get; set; } = "";
    public string Kind { get; set; } = StorageCustodyRowKind.Romaneio;
    public string Transaction { get; set; } = "";
    public string Description { get; set; } = "";
    public string Date { get; set; } = "";
    public string Cfop { get; set; } = "";
    public string Document { get; set; } = "";
    public string TaxDocument { get; set; } = "";
    public string Situation { get; set; } = "";
    public string NfeStatus { get; set; } = "";
    public string GrossWeightText { get; set; } = "";
    public string NetWeightText { get; set; } = "";
    public string QuantityText { get; set; } = "";
    public string DifferenceText { get; set; } = "";
    public string LotBalanceLabel { get; set; } = "";
    public decimal LotPhysicalBalance { get; set; }
    public decimal LotFiscalBalance { get; set; }
    public decimal LotDifference { get; set; }
    public int RomaneioCount { get; set; }
    public int PendingCount { get; set; }
}
```

`SiagroB1.Reports/Services/StorageCustodyByPeriodReportService.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Reports.Services;

/// <summary>
/// "Custódia de Armazenagem" (spec 2026-10-12 §6): romaneios de lote de terceiro do período com as etapas fiscais
/// esperadas e a nota de cada uma, agrupados por depositante e lote, com o saldo físico e o fiscal em depósito do lote
/// na data final. Mesmas regras da seção do romaneio (StorageCustodyQuery + StorageCustodyRules). Só STANDALONE (a
/// guarda fica no controller).
/// </summary>
public class StorageCustodyByPeriodReportService(IUnitOfWork db, IFastReportService reportService)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<List<StorageCustodyRowDto>> BuildRowsAsync(StorageCustodyByPeriodRequest request)
    {
        var ctx = db.Context;
        var from = request.FromDate.Date;
        var toExclusive = request.ToDate.Date.AddDays(1);

        var lots = StorageCustodyQuery.Transactions(ctx);
        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            lots = lots.Where(t => t.BranchCode == request.BranchCode);
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            lots = lots.Where(t => t.CardCode == request.CardCode);
        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            lots = lots.Where(t => t.ItemCode == request.ItemCode);
        if (!string.IsNullOrWhiteSpace(request.StorageAddressCode))
            lots = lots.Where(t => t.StorageAddressCode == request.StorageAddressCode);

        // Todos os romaneios dos lotes filtrados até a data final: o período escolhe as linhas, o saldo fiscal soma tudo.
        var untilEnd = await lots.Where(t => t.TransactionDate < toExclusive).ToListAsync();
        var inPeriod = untilEnd.Where(t => t.TransactionDate >= from).ToList();
        if (inPeriod.Count == 0)
            return [];

        var documents = await StorageCustodyQuery.ActiveDocumentsAsync(ctx, untilEnd.Select(t => t.Key).ToList());
        var lotCodes = inPeriod.Select(t => t.StorageAddressCode).Distinct().ToList();
        var physical = await PhysicalBalancesAsync(lotCodes, toExclusive);
        var fiscal = FiscalBalances(untilEnd, documents);
        var balanceLabel = $"Saldo do lote em {ReportText.Date(request.ToDate)} — físico, fiscal em depósito e diferença:";

        var rows = new List<StorageCustodyRowDto>();
        foreach (var t in inPeriod
                     .OrderBy(t => GroupOf(t), StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(t => t.TransactionDate)
                     .ThenBy(t => t.Code, StringComparer.Ordinal))
        {
            var steps = StorageCustodyRules.BuildSteps(t, documents);
            if (steps.Count == 0)
                continue;

            var pending = steps.Any(s => s.Situation == StorageCustodyStepSituation.Missing);
            if (request.Situation == StorageCustodySituationFilter.Pending && !pending) continue;
            if (request.Situation == StorageCustodySituationFilter.Complete && pending) continue;

            var lotPhysical = physical.GetValueOrDefault(t.StorageAddressCode);
            var lotFiscal = fiscal.GetValueOrDefault(t.StorageAddressCode);

            StorageCustodyRowDto Base() => new()
            {
                Group = GroupOf(t),
                LotBalanceLabel = balanceLabel,
                LotPhysicalBalance = lotPhysical,
                LotFiscalBalance = lotFiscal,
                LotDifference = lotPhysical - lotFiscal,
            };

            var romaneio = Base();
            romaneio.Kind = StorageCustodyRowKind.Romaneio;
            romaneio.Transaction = t.Code ?? "";
            romaneio.Description = TypeText(t);
            romaneio.Date = ReportText.Date(t.TransactionDate);
            romaneio.GrossWeightText = Quantity(t.GrossWeight);
            romaneio.NetWeightText = Quantity(t.NetWeight);
            romaneio.RomaneioCount = 1;
            romaneio.PendingCount = pending ? 1 : 0;
            rows.Add(romaneio);

            foreach (var s in steps)
            {
                var step = Base();
                step.Kind = StorageCustodyRowKind.Step;
                step.Description = "· " + StorageCustodyRules.ShortRoleText(s.Role);
                step.Date = ReportText.Date(s.IssueDate);
                step.Cfop = s.Cfop ?? "";
                step.Document = s.InvoiceNumber ?? "";
                step.TaxDocument = TaxDocument(s.Number, s.TaxDocumentSeries, s.InvoiceNumber);
                step.Situation = s.SituationText;
                step.NfeStatus = s.NfeStatus is { } nfe ? InvoiceReportText.Nfe(nfe) : "";
                step.QuantityText = s.Quantity is { } q ? Quantity(q) : "";
                step.DifferenceText = s.Difference is { } d ? Quantity(d) : "";
                rows.Add(step);
            }
        }

        return rows;
    }

    public async Task<byte[]> ExecuteAsync(StorageCustodyByPeriodRequest request)
    {
        var rows = await BuildRowsAsync(request);

        var extra = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.CardCode))
            extra.Add($"Depositante: {request.CardCode}");
        if (!string.IsNullOrWhiteSpace(request.StorageAddressCode))
            extra.Add($"Lote: {request.StorageAddressCode}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = LogisticsReportText.BuildFilters(
                "Data do romaneio", request, await LogisticsReportText.BranchNameAsync(db.Context, request.BranchCode),
                productName: null, SituationText(request.Situation), extra),
        };

        return await reportService.GeneratePdfAsync(
            "StorageCustodyByPeriod.frx", rows, "StorageCustody", "StorageCustody", parameters);
    }

    /// <summary>Saldo físico do lote na data final: mesma fórmula de StorageAddress.Balance (NetWeight).</summary>
    private async Task<Dictionary<string, decimal>> PhysicalBalancesAsync(List<string> lotCodes, DateTime toExclusive)
    {
        var sums = await db.Context.StorageTransactions.AsNoTracking()
            .Where(t => t.StorageAddressCode != null && lotCodes.Contains(t.StorageAddressCode)
                        && t.TransactionDate < toExclusive
                        && (t.TransactionStatus == StorageTransactionsStatus.Confirmed || t.TransactionStatus == StorageTransactionsStatus.Invoiced))
            .GroupBy(t => new { t.StorageAddressCode, t.TransactionType })
            .Select(g => new { g.Key.StorageAddressCode, g.Key.TransactionType, Net = g.Sum(t => t.NetWeight) })
            .ToListAsync();

        return sums
            .GroupBy(s => s.StorageAddressCode!)
            .ToDictionary(g => g.Key, g => g.Sum(s => Sign(s.TransactionType) * s.Net));
    }

    private static decimal Sign(StorageTransactionType type) => type switch
    {
        StorageTransactionType.Receipt or StorageTransactionType.ShipmentReleased or StorageTransactionType.SalesShipmentReturn => 1m,
        StorageTransactionType.Shipment or StorageTransactionType.SalesShipment or StorageTransactionType.TechnicalLoss => -1m,
        _ => 0m,
    };

    /// <summary>Saldo fiscal em depósito (spec §3.5): entradas para armazenagem − retornos, só notas ativas.</summary>
    private static Dictionary<string, decimal> FiscalBalances(List<StorageCustodyTransactionRow> transactions, List<StorageCustodyDocumentRow> documents)
    {
        var lotOf = transactions.ToDictionary(t => t.Key, t => t.StorageAddressCode);

        return documents
            .Where(d => lotOf.ContainsKey(d.TransactionKey))
            .GroupBy(d => lotOf[d.TransactionKey])
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Role switch
            {
                StorageCustodyRole.StorageEntry => d.Quantity,
                StorageCustodyRole.StorageReturn or StorageCustodyRole.StorageSymbolicReturn => -d.Quantity,
                _ => 0m,
            }));
    }

    private static string GroupOf(StorageCustodyTransactionRow t) =>
        $"{ReportText.Partner(t.CardCode, t.CardName)} — Lote {t.StorageAddressCode} — {t.ItemName ?? t.ItemCode} ({t.UoM})";

    private static string TypeText(StorageCustodyTransactionRow t) =>
        (t.TransactionType == StorageTransactionType.Receipt ? "Recepção" : "Expedição") + (t.IsOwnershipTransfer ? " (transferência)" : "");

    private static string SituationText(StorageCustodySituationFilter situation) => situation switch
    {
        StorageCustodySituationFilter.Pending => "Situação: com pendência fiscal",
        StorageCustodySituationFilter.Complete => "Situação: completa",
        _ => "Situação: todas",
    };

    /// <summary>"número-série" quando há NF; senão vazio (o número interno já sai na coluna Documento).</summary>
    private static string TaxDocument(string? number, string? series, string? internalNumber)
    {
        if (string.IsNullOrWhiteSpace(number) || number == internalNumber)
            return "";
        return string.IsNullOrWhiteSpace(series) ? number.Trim() : $"{number.Trim()}-{series.Trim()}";
    }

    private static string Quantity(decimal value) => value.ToString("N3", PtBr);
}
```

`SiagroB1.Reports/Controllers/StorageCustodyByPeriodController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/StorageCustodyByPeriod")]
public class StorageCustodyByPeriodController(
    StorageCustodyByPeriodReportService service,
    IConfiguration configuration) : ControllerBase
{
    /// <summary>Mesma mensagem de StorageCustodyGuard.StandaloneOnlyMessage.</summary>
    public const string StandaloneOnlyMessage = "Disponível somente no modo STANDALONE.";

    [HttpPost]
    public async Task<IActionResult> Report([FromBody] StorageCustodyByPeriodRequest request)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest(StandaloneOnlyMessage);

        if (LogisticsReportText.Validate(request) is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"storage-custody-by-period.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

- [ ] **Step 4: Template**

`SiagroB1.Reports/Reports/Templates/StorageCustodyByPeriod.frx` (paisagem, 1084 px; colunas: Romaneio 0/70, Tipo/Etapa
70/200, Data 270/62, CFOP 332/40, Documento 372/62, NF/Série 434/78, Situação 512/110, NF-e 622/92, Peso bruto
714/88, Peso líquido 802/88, Qtde. nota 890/88, Diferença 978/88):

```xml
<?xml version="1.0" encoding="utf-8"?>
<Report ScriptLanguage="CSharp" ReportInfo.Created="10/12/2026 10:00:00" ReportInfo.Modified="10/12/2026 10:00:00" ReportInfo.CreatorVersion="2026.1.0.0">
  <Dictionary>
    <BusinessObjectDataSource Name="StorageCustody" ReferenceName="StorageCustody" DataType="System.Int32" Enabled="true">
      <Column Name="Group" DataType="System.String"/>
      <Column Name="Kind" DataType="System.String"/>
      <Column Name="Transaction" DataType="System.String"/>
      <Column Name="Description" DataType="System.String"/>
      <Column Name="Date" DataType="System.String"/>
      <Column Name="Cfop" DataType="System.String"/>
      <Column Name="Document" DataType="System.String"/>
      <Column Name="TaxDocument" DataType="System.String"/>
      <Column Name="Situation" DataType="System.String"/>
      <Column Name="NfeStatus" DataType="System.String"/>
      <Column Name="GrossWeightText" DataType="System.String"/>
      <Column Name="NetWeightText" DataType="System.String"/>
      <Column Name="QuantityText" DataType="System.String"/>
      <Column Name="DifferenceText" DataType="System.String"/>
      <Column Name="LotBalanceLabel" DataType="System.String"/>
      <Column Name="LotPhysicalBalance" DataType="System.Decimal"/>
      <Column Name="LotFiscalBalance" DataType="System.Decimal"/>
      <Column Name="LotDifference" DataType="System.Decimal"/>
      <Column Name="RomaneioCount" DataType="System.Int32"/>
      <Column Name="PendingCount" DataType="System.Int32"/>
    </BusinessObjectDataSource>
    <Parameter Name="pCompanyName" DataType="System.String" AsString=""/>
    <Parameter Name="pFilters" DataType="System.String" AsString=""/>
    <Total Name="TotalRomaneios" Expression="[StorageCustody.RomaneioCount]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalPending" Expression="[StorageCustody.PendingCount]" Evaluator="Data1" PrintOn="ReportSummary1"/>
    <Total Name="TotalCountAll" TotalType="Count" Evaluator="Data1" PrintOn="ReportSummary1"/>
  </Dictionary>
  <ReportPage Name="Page1" Landscape="true" PaperWidth="297" PaperHeight="210" LeftMargin="5" TopMargin="5" RightMargin="5" BottomMargin="5" RawPaperSize="9" Watermark.Font="Arial, 60pt">
    <PageHeaderBand Name="PageHeader1" Width="1084" Height="113.4">
      <PictureObject Name="picLogo" Left="0" Top="2" Width="94.5" Height="37.8" SizeMode="Zoom"/>
      <TextObject Name="txtCompany" Left="100" Top="2" Width="700" Height="37.8" Text="[pCompanyName]" VertAlign="Center" Font="Arial, 11pt, style=Bold"/>
      <TextObject Name="txtPrintDate" Left="937" Top="2" Width="147" Height="18.9" Text="[Date]" Format="Date" Format.Format="d" HorzAlign="Right" VertAlign="Center" Font="Tahoma, 6pt"/>
      <TextObject Name="txtTitle" Top="45" Width="1084" Height="28.35" Text="Custódia de Armazenagem" HorzAlign="Center" VertAlign="Center" Font="Arial, 14pt, style=Bold, Italic"/>
      <TextObject Name="txtFilters" Top="75" Width="1084" Height="37.8" Text="[pFilters]" HorzAlign="Center" VertAlign="Top" WordWrap="true" Font="Consolas, 8pt, style=Italic"/>
    </PageHeaderBand>
    <ColumnHeaderBand Name="ColumnHeader1" Top="116.6" Width="1084" Height="17">
      <TextObject Name="hdrTransaction" Left="0" Width="70" Height="17" Text="Romaneio" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrDescription" Left="70" Width="200" Height="17" Text="Tipo / Etapa" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrDate" Left="270" Width="62" Height="17" Text="Data" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrCfop" Left="332" Width="40" Height="17" Text="CFOP" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrDocument" Left="372" Width="62" Height="17" Text="Documento" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrTaxDocument" Left="434" Width="78" Height="17" Text="NF/Série" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrSituation" Left="512" Width="110" Height="17" Text="Situação" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrNfeStatus" Left="622" Width="92" Height="17" Text="NF-e" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrGrossWeight" Left="714" Width="88" Height="17" Text="Peso bruto" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrNetWeight" Left="802" Width="88" Height="17" Text="Peso líquido" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrQuantity" Left="890" Width="88" Height="17" Text="Qtde. nota" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrDifference" Left="978" Width="88" Height="17" Text="Diferença" HorzAlign="Right" VertAlign="Bottom" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
    </ColumnHeaderBand>
    <GroupHeaderBand Name="GroupHeader1" SortOrder="None" Top="136.7" Width="1084" Height="20" Condition="[StorageCustody.Group]">
      <TextObject Name="txtGroup" Left="0" Top="2" Width="1000" Height="17" Text="[StorageCustody.Group]" VertAlign="Center" WordWrap="false" Font="Arial, 8pt, style=Bold"/>
      <DataBand Name="Data1" Top="159.75" Width="1084" Height="17" CanGrow="true" CanShrink="true" DataSource="StorageCustody">
        <TextObject Name="txtTransaction" Left="0" Width="70" Height="17" Text="[StorageCustody.Transaction]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
        <TextObject Name="txtDescription" Left="70" Width="200" Height="17" Text="[StorageCustody.Description]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold">
          <Highlight><Condition Expression="[StorageCustody.Kind] == &quot;Etapa&quot;" Font="Consolas, 7pt, style=Italic" ApplyFont="true" ApplyTextFill="false"/></Highlight>
        </TextObject>
        <TextObject Name="txtDate" Left="270" Width="62" Height="17" Text="[StorageCustody.Date]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold">
          <Highlight><Condition Expression="[StorageCustody.Kind] == &quot;Etapa&quot;" Font="Consolas, 7pt, style=Italic" ApplyFont="true" ApplyTextFill="false"/></Highlight>
        </TextObject>
        <TextObject Name="txtCfop" Left="332" Width="40" Height="17" Text="[StorageCustody.Cfop]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
        <TextObject Name="txtDocument" Left="372" Width="62" Height="17" Text="[StorageCustody.Document]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
        <TextObject Name="txtTaxDocument" Left="434" Width="78" Height="17" Text="[StorageCustody.TaxDocument]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
        <TextObject Name="txtSituation" Left="512" Width="110" Height="17" Text="[StorageCustody.Situation]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic">
          <Highlight><Condition Expression="[StorageCustody.Situation] == &quot;PENDENTE&quot;" Font="Consolas, 7pt, style=Bold" ApplyFont="true" ApplyTextFill="false"/></Highlight>
        </TextObject>
        <TextObject Name="txtNfeStatus" Left="622" Width="92" Height="17" Text="[StorageCustody.NfeStatus]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
        <TextObject Name="txtGrossWeight" Left="714" Width="88" Height="17" Text="[StorageCustody.GrossWeightText]" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
        <TextObject Name="txtNetWeight" Left="802" Width="88" Height="17" Text="[StorageCustody.NetWeightText]" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
        <TextObject Name="txtQuantity" Left="890" Width="88" Height="17" Text="[StorageCustody.QuantityText]" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
        <TextObject Name="txtDifference" Left="978" Width="88" Height="17" Text="[StorageCustody.DifferenceText]" HorzAlign="Right" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Italic"/>
      </DataBand>
      <GroupFooterBand Name="GroupFooter1" Top="180" Width="1084" Height="20">
        <TextObject Name="txtLotLabel" Left="0" Top="2" Width="802" Height="17" Text="[StorageCustody.LotBalanceLabel]" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold, Italic"/>
        <TextObject Name="txtLotPhysical" Left="802" Top="2" Width="88" Height="17" Text="[StorageCustody.LotPhysicalBalance]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtLotFiscal" Left="890" Top="2" Width="88" Height="17" Text="[StorageCustody.LotFiscalBalance]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
        <TextObject Name="txtLotDifference" Left="978" Top="2" Width="88" Height="17" Text="[StorageCustody.LotDifference]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 6pt, style=Bold, Italic"/>
      </GroupFooterBand>
    </GroupHeaderBand>
    <ReportSummaryBand Name="ReportSummary1" Top="203" Width="1084" Height="48">
      <TextObject Name="txtSumLabel" Left="0" Top="4" Width="100" Height="17" Text="TOTAL GERAL:" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Bold"/>
      <TextObject Name="txtSumCount" Left="100" Top="4" Width="500" Height="17" Text="[TotalRomaneios] romaneio(s), [TotalPending] com pendência fiscal" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="txtEmpty" Left="0" Top="26" Width="1084" Height="17" Text="[IIf([TotalCountAll] == 0, &quot;Nenhum registro encontrado.&quot;, &quot;&quot;)]" HorzAlign="Center" VertAlign="Center" WordWrap="false" Font="Consolas, 8pt, style=Italic"/>
    </ReportSummaryBand>
    <PageFooterBand Name="PageFooter1" Top="254" Width="1084" Height="26">
      <TextObject Name="txtPage" Left="937" Width="147" Height="17" Text="Página [Page#] de [TotalPages#]" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
    </PageFooterBand>
  </ReportPage>
</Report>
```

Notas:
- `txtPrintDate` (e não `txtDate`): `txtDate` é a coluna de dados e os testes de layout procuram nomes únicos.
- O teste `NumericObjects_FitTheLargestRealisticValue` exige ≥ 3 campos com `Format="Number"`: são os três saldos
  do rodapé, em 6pt numa linha só.
- Se o `FastReportService` exigir `pSingleUom` ou o prefixo `uomSum`, compare com o template de remessas; aqui não há
  totais por UM, então ele não deve ser exigido.
- Antes do commit, renderize o PDF gerado por `StorageCustodyByPeriod_ProducesAPdf` (o `Keep` grava o arquivo; ver
  a memória "Ver um .frx renderizado localmente") e olhe: as colunas não se sobrepõem, "PENDENTE" sai em negrito e o
  rodapé do lote mostra os três saldos.

- [ ] **Step 5: Menu migration**

Gerar o esqueleto e escrever o corpo à mão (Bash):

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddStorageCustodyReportMenu \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context CommonDbContext
```

Substituir `Up`/`Down` do `.cs` gerado (o `Designer.cs` fica como veio):

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // StandaloneOnly: as notas de custódia só existem em STANDALONE.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "storageCustodyReport", "Custódia de Armazenagem", "sap-icon://folder-blank", true, false, 20, "reports", true });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { "5C2E8A41-9D7B-4E36-A0F2-3B6D1C9E7A58", "ADMIN", "storageCustodyReport" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "5C2E8A41-9D7B-4E36-A0F2-3B6D1C9E7A58");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "storageCustodyReport");
        }
```

Se o `.cs` gerado trouxer alteração de esquema além disso, pare (drift do snapshot do Common). Aplicar no
`CEAGUI_SIAGRO_COMMON_DEV` com `dotnet ef database update --context CommonDbContext` (mesmo ambiente). `git add` dos
arquivos gerados.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageCustodyByPeriod|FullyQualifiedName~LogisticsReports"`
Expected: PASS (inclusive todos os testes de layout com o template novo).

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Reports SiagroB1.Migrations SiagroB1.Application.Tests/Reports
git commit -m "feat(reports): relatório de custódia de armazenagem" -m "Romaneios de lote de terceiro do período com as etapas fiscais esperadas e a nota de cada uma (PENDENTE em destaque), agrupados por depositante e lote, com o saldo físico e o fiscal em depósito do lote na data final. Mesma consulta e mesmas regras da seção do romaneio." -m "DB: AddStorageCustodyReportMenu" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

## Task 9 (frontend): regras puras, natureza e lote

Repositório `siagro-b1-frontend/`. Antes: `git switch -c feature/storage-custody-fiscal-documents` a partir de `main`.

**Files:**
- Create: `webapp/helpers/StorageCustodyHelpers.ts`, `webapp/test/unit/helpers/StorageCustodyHelpers.qunit.ts`
- Modify: `webapp/test/unit/unitTests.qunit.ts`, `webapp/model/ServerRoutes.ts`, `webapp/view/usages/fragments/Form.fragment.xml`, `webapp/controller/usages/Add.controller.ts`, `webapp/view/storageAddresses/fragments/Form.fragment.xml`, `webapp/controller/storageAddresses/Add.controller.ts`

**Interfaces:**
- Produces (TS):
  - `type StorageCustodyStep`, `type StorageCustodySummary` (espelham os DTOs da Task 3; enums como string)
  - `isPurchaseRole(role: string): boolean`
  - `custodyUsageFilter(role: string): string`
  - `canGenerateCustody(summary, isStandalone): boolean`
  - `isCustodyPriceValid(value: unknown): boolean`
  - `custodyStepState(situation: string): "Success" | "Warning" | "Error" | "None"`
- `ServerRoutes.storageTransactionsCreateCustodyDocument`, `storageTransactionsCustodySummary`, `storageCustodyByPeriodReport`.

- [ ] **Step 1: Write the failing QUnit tests**

`webapp/test/unit/helpers/StorageCustodyHelpers.qunit.ts`:

```ts
import { canGenerateCustody, custodyStepState, custodyUsageFilter, isCustodyPriceValid, isPurchaseRole, StorageCustodySummary } from "siagrob1/helpers/StorageCustodyHelpers";

const summary = (next: string, applies = true): StorageCustodySummary => ({
  TransactionKey: "k", Applies: applies, NextRole: next, Steps: [],
});

QUnit.module("StorageCustodyHelpers");

QUnit.test("isPurchaseRole: entradas vão para o Documento de Entrada", (assert) => {
  assert.strictEqual(isPurchaseRole("ProcessingEntry"), true);
  assert.strictEqual(isPurchaseRole("StorageEntry"), true);
  assert.strictEqual(isPurchaseRole("ProcessingSymbolicReturn"), false);
  assert.strictEqual(isPurchaseRole("StorageReturn"), false);
  assert.strictEqual(isPurchaseRole("StorageSymbolicReturn"), false);
});

QUnit.test("custodyUsageFilter: natureza do papel e da direção", (assert) => {
  assert.strictEqual(custodyUsageFilter("ProcessingEntry"), "StorageCustodyRole eq 'ProcessingEntry' and Direction eq 'Incoming'");
  assert.strictEqual(custodyUsageFilter("StorageReturn"), "StorageCustodyRole eq 'StorageReturn' and Direction eq 'Outgoing'");
});

QUnit.test("canGenerateCustody: há etapa pendente em STANDALONE", (assert) => {
  assert.strictEqual(canGenerateCustody(summary("StorageEntry"), true), true);
  assert.strictEqual(canGenerateCustody(summary("None"), true), false, "cadeia completa");
  assert.strictEqual(canGenerateCustody(summary("StorageEntry", false), true), false, "fora do escopo");
  assert.strictEqual(canGenerateCustody(summary("StorageEntry"), false), false, "SAPB1");
  assert.strictEqual(canGenerateCustody(undefined, true), false, "não carregado");
});

QUnit.test("isCustodyPriceValid: número maior que zero", (assert) => {
  assert.strictEqual(isCustodyPriceValid(2.5), true);
  assert.strictEqual(isCustodyPriceValid("2,5"), true);
  assert.strictEqual(isCustodyPriceValid(0), false);
  assert.strictEqual(isCustodyPriceValid(""), false);
});

QUnit.test("custodyStepState: pendente em vermelho, gerada em amarelo, autorizada/escriturada em verde", (assert) => {
  assert.strictEqual(custodyStepState("Missing"), "Error");
  assert.strictEqual(custodyStepState("Generated"), "Warning");
  assert.strictEqual(custodyStepState("Authorized"), "Success");
  assert.strictEqual(custodyStepState("Recorded"), "Success");
});
```

E acrescentar `import "./helpers/StorageCustodyHelpers.qunit";` ao fim de `webapp/test/unit/unitTests.qunit.ts`.

- [ ] **Step 2: Run to verify it fails**

Run: `yarn ts-typecheck`
Expected: FAIL (`siagrob1/helpers/StorageCustodyHelpers` não existe).

- [ ] **Step 3: Implement**

`webapp/helpers/StorageCustodyHelpers.ts`:

```ts
/* eslint-disable @typescript-eslint/no-redundant-type-constituents -- strictNullChecks está desligado no projeto; "| null" documenta o contrato do servidor */
import { isRemittancePriceValid } from "siagrob1/helpers/StorageDepositHelpers";

/** Regras puras das notas de custódia em armazém próprio (spec backend 2026-10-12 §5). */

export type StorageCustodyStep = {
  Role: string; RoleText: string; Situation: string; SituationText: string;
  DocumentKey: string | null; IsSalesInvoice: boolean; InvoiceNumber: string | null; Number: string | null;
  TaxDocumentSeries: string | null; ChaveNFe: string | null; IssueDate: string | null; Cfop: string | null;
  NfeStatus: string | null; Quantity: number | null; ExpectedQuantity: number | null; Difference: number | null;
};

export type StorageCustodySummary = {
  TransactionKey: string; Applies: boolean; NextRole: string; Steps: StorageCustodyStep[];
};

/** Entradas viram Documento de Entrada; devolução simbólica e retornos, Documento de Saída. */
export function isPurchaseRole(role: string): boolean {
  return role === "ProcessingEntry" || role === "StorageEntry";
}

/** $filter estático do value help de natureza (enum em sap.ui.model.Filter não serializa no OData V4). */
export function custodyUsageFilter(role: string): string {
  return `StorageCustodyRole eq '${role}' and Direction eq '${isPurchaseRole(role) ? "Incoming" : "Outgoing"}'`;
}

export function canGenerateCustody(summary: StorageCustodySummary | undefined, isStandalone: boolean): boolean {
  return isStandalone && !!summary && summary.Applies && !!summary.NextRole && summary.NextRole !== "None";
}

/** Mesma regra do preço da remessa para depósito. */
export function isCustodyPriceValid(value: unknown): boolean {
  return isRemittancePriceValid(value);
}

export function custodyStepState(situation: string): "Success" | "Warning" | "Error" | "None" {
  switch (situation) {
    case "Missing": return "Error";
    case "Generated": return "Warning";
    case "Authorized":
    case "Recorded": return "Success";
    default: return "None";
  }
}
```

`webapp/model/ServerRoutes.ts` — logo depois das rotas da remessa para depósito:

```ts
  // Notas de custódia em armazém próprio: gerar a nota do romaneio e resumo das etapas.
  storageTransactionsCreateCustodyDocument: '/odata/StorageTransactionsCreateCustodyDocument',
  storageTransactionsCustodySummary: '/odata/StorageTransactionsCustodySummary',
```

e, junto dos relatórios, logo depois de `storageRemittancesByPeriodReport`:

```ts
  storageCustodyByPeriodReport: '/reports/StorageCustodyByPeriod',
```

`webapp/view/usages/fragments/Form.fragment.xml` — logo depois do `<Select>` do "Papel no depósito":

```xml
          <!-- Notas de custódia em armazém próprio (spec backend 2026-10-12 §3.1): só STANDALONE. Entradas são
               naturezas de entrada; devolução e retornos, de saída — o servidor recusa a combinação errada. -->
          <Label text="Papel na custódia (armazenagem)" visible="{ui>/identityEditable}" />
          <Select
            visible="{ui>/identityEditable}"
            selectedKey="{ path: 'StorageCustodyRole', targetType: 'any' }"
            forceSelection="false">
            <core:ListItem key="None" text="Nenhum" />
            <core:ListItem key="ProcessingEntry" text="Entrada para beneficiamento" />
            <core:ListItem key="ProcessingSymbolicReturn" text="Devolução simbólica do beneficiamento" />
            <core:ListItem key="StorageEntry" text="Entrada para armazenagem" />
            <core:ListItem key="StorageReturn" text="Retorno de armazenagem" />
            <core:ListItem key="StorageSymbolicReturn" text="Retorno simbólico de armazenagem" />
          </Select>
```

`webapp/controller/usages/Add.controller.ts` — no objeto inicial, logo depois de `StorageDepositRole: "None",`:

```ts
			StorageCustodyRole: "None",
```

`webapp/view/storageAddresses/fragments/Form.fragment.xml` — logo depois do `<Select>` de `OwnershipType`:

```xml
            <!-- Beneficiamento fiscal (spec backend 2026-10-12 §3.2): só STANDALONE e só tem efeito em lote de terceiro.
                 Com nota de custódia ativa no lote o servidor recusa a troca. -->
            <Label text="Passa por beneficiamento (fiscal)" visible="{ui>/standalone}" />
            <CheckBox
              visible="{ui>/standalone}"
              editable="{ui>/editable}"
              selected="{ path: 'RequiresProcessing', targetType: 'any' }" />
```

Se `ui>/standalone` não for publicado nas telas do lote, chame `void this.refreshStandaloneFlag();` no route matched
de `controller/storageAddresses/Add.controller.ts` e `Edit.controller.ts` (o método existe no `BaseController`). No
objeto inicial do `Add.controller.ts`, logo depois de `"Nature": "Regular",`:

```ts
      "RequiresProcessing": false,
```

- [ ] **Step 4: Run gates**

Run: `yarn ts-typecheck && yarn lint` e o QUnit (comando em Global Constraints).
Expected: sem erros; `StorageCustodyHelpers` verde.

- [ ] **Step 5: Commit**

```bash
git add webapp/helpers/StorageCustodyHelpers.ts webapp/test/unit webapp/model/ServerRoutes.ts webapp/view/usages webapp/controller/usages webapp/view/storageAddresses webapp/controller/storageAddresses
git commit -m "feat(master-data): papel na custódia na natureza e beneficiamento fiscal no lote" -m "A natureza ganha o papel nas notas de custódia em armazém próprio e o lote diz se passa por beneficiamento fiscal; as regras puras da tela ficam testadas à parte." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

## Task 10 (frontend): seção "Notas fiscais de custódia" no romaneio

**Files:**
- Create: `webapp/view/storageTransactions/fragments/CustodyDocumentDialog.fragment.xml`
- Modify: `webapp/view/storageTransactions/Detail.view.xml`, `webapp/controller/storageTransactions/Detail.controller.ts`

**Interfaces:**
- Consumes: Task 9 (helpers, rotas); backend Task 6 (action e function).

- [ ] **Step 1: Implement the section**

`Detail.view.xml` — nova `ObjectPageSection` logo depois de "Dados do Romaneio":

```xml
      <!-- Notas de custódia em armazém próprio (spec backend 2026-10-12 §5): só STANDALONE e romaneio de lote de
           terceiro. Uma linha por etapa esperada; "Gerar nota" cria a próxima pendente. -->
      <uxap:ObjectPageSection titleUppercase="false" title="Notas fiscais de custódia" visible="{custody>/visible}">
        <uxap:subSections>
          <uxap:ObjectPageSubSection>
            <uxap:blocks>
              <VBox>
                <MessageStrip text="{custody>/loadError}" type="Error" visible="{= !!${custody>/loadError} }" showIcon="true" class="sapUiSmallMarginBottom"/>
                <OverflowToolbar style="Clear">
                  <ToolbarSpacer/>
                  <Button text="Gerar nota" type="Emphasized" enabled="{custody>/canGenerate}" press=".onOpenCustodyDialog"/>
                </OverflowToolbar>
                <Table items="{custody>/summary/Steps}" noDataText="Este romaneio não espera nota de custódia.">
                  <columns>
                    <Column><Text text="Etapa"/></Column>
                    <Column><Text text="Situação"/></Column>
                    <Column><Text text="Documento"/></Column>
                    <Column><Text text="NF/Série"/></Column>
                    <Column><Text text="Chave"/></Column>
                    <Column hAlign="End"><Text text="Quantidade"/></Column>
                    <Column hAlign="End"><Text text="Diferença"/></Column>
                  </columns>
                  <ColumnListItem>
                    <cells>
                      <Text text="{custody>RoleText}" wrapping="false"/>
                      <ObjectStatus text="{custody>SituationText}" state="{ path: 'custody>Situation', formatter: '.custodyStepState' }"/>
                      <Link text="{custody>InvoiceNumber}" press=".onOpenCustodyDocument" enabled="{= !!${custody>DocumentKey} }"/>
                      <Text text="{= ${custody>Number} ? ${custody>Number} + (${custody>TaxDocumentSeries} ? '-' + ${custody>TaxDocumentSeries} : '') : '' }" wrapping="false"/>
                      <Text text="{custody>ChaveNFe}" wrapping="false"/>
                      <Text text="{ path: 'custody>Quantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
                      <Text text="{ path: 'custody>Difference', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
                    </cells>
                  </ColumnListItem>
                </Table>
              </VBox>
            </uxap:blocks>
          </uxap:ObjectPageSubSection>
        </uxap:subSections>
      </uxap:ObjectPageSection>
```

`webapp/view/storageTransactions/fragments/CustodyDocumentDialog.fragment.xml`:

```xml
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core" xmlns:f="sap.ui.layout.form">
	<Dialog title="Gerar nota de custódia" contentWidth="32rem">
		<f:Form editable="true">
			<f:layout>
				<f:ColumnLayout columnsM="1" columnsL="1" columnsXL="1"/>
			</f:layout>
			<f:formContainers>
				<f:FormContainer>
					<f:formElements>
						<f:FormElement label="Etapa">
							<f:fields>
								<Select selectedKey="{custody>/dialog/role}" items="{custody>/dialog/roles}" change=".onCustodyRoleChange">
									<core:Item key="{custody>Role}" text="{custody>RoleText}"/>
								</Select>
							</f:fields>
						</f:FormElement>
						<f:FormElement label="Natureza">
							<f:fields>
								<Input value="{custody>/dialog/usageName}" required="true" showValueHelp="true" valueHelpOnly="true"
									valueHelpRequest=".onCustodyUsageValueHelp"/>
							</f:fields>
						</f:FormElement>
						<f:FormElement label="Quantidade">
							<f:fields>
								<Text text="{ path: 'custody>/dialog/quantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
							</f:fields>
						</f:FormElement>
						<f:FormElement label="Preço unitário">
							<f:fields>
								<Input
									value="{ path: 'custody>/dialog/unitPrice', type: 'sap.ui.model.type.Float', formatOptions: { decimalSeparator: ',', groupingSeparator: '.', groupingEnabled: true, maxFractionDigits: 6 } }"
									required="true"
									parseError=".onCustodyPriceValidationError"
									validationError=".onCustodyPriceValidationError"
									validationSuccess=".onCustodyPriceValidationSuccess"/>
							</f:fields>
						</f:FormElement>
					</f:formElements>
				</f:FormContainer>
			</f:formContainers>
		</f:Form>
		<beginButton>
			<Button text="Gerar" type="Emphasized" press=".onConfirmCustody"/>
		</beginButton>
		<endButton>
			<Button text="Cancelar" press=".onCancelCustody"/>
		</endButton>
	</Dialog>
</core:FragmentDefinition>
```

`Detail.controller.ts` — acrescentar imports:

```ts
import Dialog from "sap/m/Dialog";
import Input from "sap/m/Input";
import MessageBox from "sap/m/MessageBox";
import Fragment from "sap/ui/core/Fragment";
import Control from "sap/ui/core/Control";
import Event from "sap/ui/base/Event";
import ServerRoutes from "siagrob1/model/ServerRoutes";
import { sendJson, odataValue } from "siagrob1/helpers/FetchHelpers";
import { canGenerateCustody, custodyStepState, custodyUsageFilter, isCustodyPriceValid, isPurchaseRole, StorageCustodyStep, StorageCustodySummary } from "siagrob1/helpers/StorageCustodyHelpers";
```

Campos e `onInit`:

```ts
	private custodyDialog?: Dialog;

	/** Formatter da situação da etapa (usado na tabela). */
	custodyStepState = custodyStepState;

	onInit(): void  {
		this.getView().setModel(new JSONModel({ visible: false, loadError: "", canGenerate: false, summary: undefined, dialog: {} }), "custody");
		this.getRouter().getRoute("storageTransactionsDetail").attachPatternMatched((ev) => this.detailRouteMatched(ev));
	}
```

No fim de `setData(id)`:

```ts
    void this.loadCustody(id);
```

Métodos novos:

```ts
  /**
   * Notas de custódia (spec backend 2026-10-12 §5): só STANDALONE; em SAPB1 a seção some e nada é pedido. Resposta de
   * outro romaneio (o usuário navegou enquanto esperava) é descartada.
   */
  private async loadCustody(id: string): Promise<void> {
    const model = this.getView().getModel("custody") as JSONModel;
    model.setData({ visible: false, loadError: "", canGenerate: false, summary: undefined, dialog: {} });

    const isStandalone = (await this.getSystemInfo())?.erp?.trim().toUpperCase() === "STANDALONE";
    if (!isStandalone) return;

    const result = await sendJson("GET", `${ServerRoutes.storageTransactionsCustodySummary}(Key=${id})`);
    const current = (this.getView().getBindingContext() as Context | undefined)?.getProperty("Key") as string | undefined;
    if (current && current.toLowerCase() !== id.toLowerCase()) return;

    if (!result.ok) {
      model.setProperty("/visible", true);
      model.setProperty("/loadError", result.message);
      return;
    }

    const summary = odataValue<StorageCustodySummary>(result.data);
    model.setProperty("/summary", summary);
    model.setProperty("/visible", summary.Applies);
    model.setProperty("/canGenerate", canGenerateCustody(summary, true));
  }

  async onOpenCustodyDialog(): Promise<void> {
    const model = this.getView().getModel("custody") as JSONModel;
    const summary = model.getProperty("/summary") as StorageCustodySummary;
    const missing = summary.Steps.filter((s) => s.Situation === "Missing");
    model.setProperty("/dialog", { roles: missing, usageCode: null, usageName: "", unitPrice: null, priceInvalid: false });
    this.selectCustodyRole(summary.NextRole);

    if (!this.custodyDialog) {
      this.custodyDialog = await Fragment.load({
        id: this.getView().getId(),
        name: "siagrob1.view.storageTransactions.fragments.CustodyDocumentDialog",
        controller: this,
      }) as Dialog;
      this.getView().addDependent(this.custodyDialog);
    }
    this.custodyDialog.open();
  }

  /** Etapa escolhida: troca a natureza (é por papel) e mostra a quantidade que a nota terá. */
  private selectCustodyRole(role: string): void {
    const model = this.getView().getModel("custody") as JSONModel;
    const step = (model.getProperty("/dialog/roles") as StorageCustodyStep[]).find((s) => s.Role === role);
    model.setProperty("/dialog/role", role);
    model.setProperty("/dialog/quantity", step?.ExpectedQuantity ?? null);
    model.setProperty("/dialog/usageCode", null);
    model.setProperty("/dialog/usageName", "");
  }

  onCustodyRoleChange(): void {
    const model = this.getView().getModel("custody") as JSONModel;
    this.selectCustodyRole(model.getProperty("/dialog/role") as string);
  }

  async onCustodyUsageValueHelp(): Promise<void> {
    const model = this.getView().getModel("custody") as JSONModel;
    const role = model.getProperty("/dialog/role") as string;
    const selected = await DialogHelper.openTableSelectDialog(
      this, "UsagesSelectDialog", ["Name", "Description"],
      [new Filter("Inactive", FilterOperator.EQ, false)], undefined, custodyUsageFilter(role));
    if (!selected) return;
    model.setProperty("/dialog/usageCode", selected.getProperty("Code"));
    model.setProperty("/dialog/usageName", selected.getProperty("Name"));
  }

  async onConfirmCustody(): Promise<void> {
    const model = this.getView().getModel("custody") as JSONModel;
    const dialog = model.getProperty("/dialog") as { role: string; usageCode: number | null; unitPrice: number | null; priceInvalid: boolean };
    if (!dialog.usageCode) { MessageBox.warning("Escolha a natureza da etapa."); return; }
    if (dialog.priceInvalid || !isCustodyPriceValid(dialog.unitPrice)) { MessageBox.warning("Informe o preço unitário da nota."); return; }

    const key = (this.getView().getBindingContext() as Context).getProperty("Key") as string;
    this.setBusy(true);
    try {
      const result = await sendJson("POST", ServerRoutes.storageTransactionsCreateCustodyDocument,
        { StorageTransactionKey: key, Role: dialog.role, UsageCode: dialog.usageCode, UnitPrice: dialog.unitPrice });
      if (!result.ok) { MessageBox.error(result.message); return; }
      this.custodyDialog?.close();
      MessageToast.show("Nota criada. Confira transporte e pesos e emita a NF-e.", { closeOnBrowserNavigation: false });
      const created = odataValue<string>(result.data);
      this.navTo(isPurchaseRole(dialog.role) ? "purchaseInvoicesDetail" : "salesInvoicesDetail", { id: created });
    } finally {
      this.setBusy(false);
    }
  }

  onCustodyPriceValidationError(ev: Event): void {
    const input = ev.getSource<Input>();
    input.setValueState("Error");
    input.setValueStateText("Preço unitário inválido.");
    (this.getView().getModel("custody") as JSONModel).setProperty("/dialog/priceInvalid", true);
  }

  onCustodyPriceValidationSuccess(ev: Event): void {
    ev.getSource<Input>().setValueState("None");
    (this.getView().getModel("custody") as JSONModel).setProperty("/dialog/priceInvalid", false);
  }

  onCancelCustody(): void { this.custodyDialog?.close(); }

  onOpenCustodyDocument(ev: Event): void {
    const ctx = ev.getSource<Control>().getBindingContext("custody");
    const key = ctx?.getProperty("DocumentKey") as string | undefined;
    if (!key) return;
    this.navTo(ctx.getProperty("IsSalesInvoice") ? "salesInvoicesDetail" : "purchaseInvoicesDetail", { id: key });
  }
```

(Se `getSystemInfo` não estiver acessível neste `BaseController`, use o mesmo caminho do
`shipmentReleases/Detail.controller.ts`. `DialogHelper`, `Filter`, `FilterOperator`, `MessageToast` e `Context` já
são importados no arquivo.)

- [ ] **Step 2: Run gates**

Run: `yarn ts-typecheck && yarn lint` e o QUnit.
Expected: sem erros.

- [ ] **Step 3: Commit**

```bash
git add webapp/view/storageTransactions webapp/controller/storageTransactions
git commit -m "feat(storage): notas fiscais de custódia no detalhe do romaneio" -m "O romaneio de lote de terceiro mostra as etapas fiscais esperadas com a nota de cada uma e gera a próxima pendente pelo diálogo (etapa, natureza do papel e preço), indo direto ao documento criado." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

## Task 11 (frontend): romaneio na escrituração e link no documento de saída

**Files:**
- Create: `webapp/dialogs/fragments/StorageCustodyTransactionsSelectDialog.fragment.xml`
- Modify: `webapp/view/purchaseInvoices/fragments/Form.fragment.xml`, `webapp/controller/purchaseInvoices/BaseController.ts`, `webapp/view/salesInvoices/Detail.view.xml`, `webapp/controller/salesInvoices/Detail.controller.ts`

**Interfaces:**
- Consumes: `PurchaseInvoice.CustodyStorageTransactionKey`, `SalesInvoice.CustodyStorageTransactionKey` (EDM, Task 1);
  `ui>/storageDepositMode` (já publicado pelo `refreshNfeMode`, = STANDALONE).

- [ ] **Step 1: Implement**

`webapp/dialogs/fragments/StorageCustodyTransactionsSelectDialog.fragment.xml`:

```xml
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core">
<!-- Recepções confirmadas de lote de armazenagem de terceiro. Depositante e produto mudam a cada documento e entram pelo
     controller; a etapa pendente é conferida pelo servidor ao gravar (spec backend 2026-10-12 §4.4). Enum vai no
     $filter estático: em sap.ui.model.Filter o OData V4 não o serializa. -->
<TableSelectDialog class="sapUiSizeCompact" growing="true" growingThreshold="20"
  items="{ path: '/StorageTransactions', parameters: {
    $filter: 'TransactionType eq \'Receipt\' and (TransactionStatus eq \'Confirmed\' or TransactionStatus eq \'Invoiced\') and StorageAddress/OwnershipType eq \'ThirdParty\'',
    $orderby: 'TransactionDate desc' } }"
  rememberSelections="true" title="Romaneio de recepção (lote de terceiro)">
  <columns>
    <Column><header><Text text="Romaneio" /></header></Column>
    <Column><header><Text text="Data" /></header></Column>
    <Column><header><Text text="Lote" /></header></Column>
    <Column hAlign="End"><header><Text text="Peso bruto" /></header></Column>
    <Column hAlign="End"><header><Text text="Peso líquido" /></header></Column>
  </columns>
  <ColumnListItem>
    <cells>
      <Text text="{Code}" />
      <Text text="{ path: 'TransactionDate', type: 'sap.ui.model.odata.type.DateTimeOffset', formatOptions: { pattern: 'dd/MM/yyyy' } }" />
      <Text text="{StorageAddressCode}" />
      <ObjectNumber number="{ path: 'GrossWeight', type: 'sap.ui.model.odata.type.Decimal', formatOptions: { maxFractionDigits: 3 } }" unit="{UnitOfMeasureCode}" />
      <ObjectNumber number="{ path: 'NetWeight', type: 'sap.ui.model.odata.type.Decimal', formatOptions: { maxFractionDigits: 3 } }" unit="{UnitOfMeasureCode}" />
    </cells>
  </ColumnListItem>
</TableSelectDialog>
</core:FragmentDefinition>
```

(O tipo de `TransactionDate` no EDM pode ser `Edm.Date`; se a coluna sair vazia, troque para
`sap.ui.model.odata.type.Date` como no `Detail.controller.ts` do romaneio. Se `StorageAddress/OwnershipType` no
`$filter` der 400, a navegação não está no EDM: tire esse predicado — o servidor recusa lote que não é de terceiro.)

`webapp/view/purchaseInvoices/fragments/Form.fragment.xml` — logo depois do par "Escrituração do produtor":

```xml
          <!-- Nota de custódia em armazém próprio (spec backend 2026-10-12 §4.4): romaneio do lote de terceiro que esta
               entrada cumpre. Escolhido à mão na escrituração da NF-e do depositante (o XML não o identifica); a nota
               própria gerada pelo romaneio já nasce com ele. O servidor exige quando a natureza da linha é de custódia. -->
          <Label text="Romaneio (custódia)"
                 visible="{= ${ui>/storageDepositMode} === true &amp;&amp; ${path: 'InvoiceType', targetType: 'any'} === 'Normal' }" />
          <Input
            visible="{= ${ui>/editable} === true &amp;&amp; ${ui>/storageDepositMode} === true &amp;&amp; ${path: 'InvoiceType', targetType: 'any'} === 'Normal' }"
            value="{ui>/custody/code}"
            showValueHelp="true"
            valueHelpOnly="true"
            valueHelpRequest=".openCustodyTransactionValueHelp" />
          <Link text="{ui>/custody/code}"
                visible="{= ${ui>/editable} !== true &amp;&amp; !!${ui>/custody/key} }"
                press=".onOpenCustodyTransaction" />
```

`controller/purchaseInvoices/BaseController.ts`:

1. Em `refreshNfeMode`, no bloco `if (reset || !oContext)`:
   ```ts
      uiModel.setProperty("/custody", { key: "", code: "" });
   ```
2. No `try`, junto das leituras do contexto:
   ```ts
      const custodyKey = await oContext.requestProperty("CustodyStorageTransactionKey") as string;
   ```
   e, depois de `if (!isCurrent()) { return; }`:
   ```ts
      void this.refreshCustodyCode(custodyKey, isCurrent);
   ```
3. Métodos novos na classe:
   ```ts
  /** Código do romaneio de custódia do documento (o EDM só traz a chave). */
  private async refreshCustodyCode(key: string, isCurrent: () => boolean): Promise<void> {
    const uiModel = this.getModel("ui") as JSONModel;
    if (!key) {
      uiModel.setProperty("/custody", { key: "", code: "" });
      return;
    }
    try {
      const code = await (this.getModel() as ODataModel).bindContext(`/StorageTransactions(${key})`).requestObject("Code") as string;
      if (isCurrent()) uiModel.setProperty("/custody", { key, code });
    } catch {
      if (isCurrent()) uiModel.setProperty("/custody", { key, code: key });
    }
  }

  /**
   * Value help do romaneio de custódia: Recepções do mesmo depositante (emitente) e produto da primeira linha. Grava a
   * CHAVE no documento e mostra o código; a etapa pendente é conferida pelo servidor.
   */
  async openCustodyTransactionValueHelp(): Promise<void> {
    const oInvoice = this.getView().getBindingContext() as Context;
    const cardCode = oInvoice.getProperty("CardCode") as string;
    if (!cardCode) {
      MessageBox.warning("Informe o emitente (depositante) antes do romaneio.");
      return;
    }
    const filters = [new Filter("CardCode", FilterOperator.EQ, cardCode)];
    const firstItem = (this.byId("itemsTable") as Table | undefined)?.getContextByIndex?.(0)?.getProperty("ItemCode") as string | undefined;
    if (firstItem) filters.push(new Filter("ItemCode", FilterOperator.EQ, firstItem));

    const selected = await DialogHelper.openTableSelectDialog(this, "StorageCustodyTransactionsSelectDialog", ["Code"], filters);
    if (!selected) return;

    await oInvoice.setProperty("CustodyStorageTransactionKey", selected.getProperty("Key"));
    (this.getModel("ui") as JSONModel).setProperty("/custody", { key: selected.getProperty("Key"), code: selected.getProperty("Code") });
  }

  onOpenCustodyTransaction(): void {
    const key = (this.getModel("ui") as JSONModel).getProperty("/custody/key") as string;
    if (key) this.navTo("storageTransactionsDetail", { id: key });
  }
   ```
   (Importe `ODataModel` de `sap/ui/model/odata/v4/ODataModel`. O id da tabela de linhas pode não ser `itemsTable` —
   confira em `Items.fragment.xml`; se não houver como ler a primeira linha de forma simples, filtre só pelo
   depositante.)

`webapp/view/salesInvoices/Detail.view.xml` — logo depois do Link "Liberação da remessa para depósito":

```xml
        <!-- Nota de custódia (spec backend 2026-10-12 §5): volta para o romaneio de origem. -->
        <Link class="sapUiSmallMarginBegin" text="Romaneio da nota de custódia"
              visible="{= !!${path: 'CustodyStorageTransactionKey', targetType: 'any'} }" press=".onOpenCustodyTransaction" />
```

`controller/salesInvoices/Detail.controller.ts` — logo depois de `onOpenShipmentRelease`:

```ts
  /** Nota de custódia: volta para o romaneio de origem. */
  onOpenCustodyTransaction(): void {
    const key = (this.getView().getBindingContext() as Context)?.getProperty("CustodyStorageTransactionKey") as string;
    if (key) this.navTo("storageTransactionsDetail", { id: key });
  }
```

- [ ] **Step 2: Run gates**

Run: `yarn ts-typecheck && yarn lint` e o QUnit.
Expected: sem erros.

- [ ] **Step 3: Commit**

```bash
git add webapp/dialogs/fragments/StorageCustodyTransactionsSelectDialog.fragment.xml webapp/view/purchaseInvoices webapp/controller/purchaseInvoices webapp/view/salesInvoices webapp/controller/salesInvoices
git commit -m "feat(invoice): romaneio de custódia na escrituração e no documento de saída" -m "A escrituração da NF-e do depositante indica o romaneio de recepção do lote de terceiro que cumpre (value help por depositante e produto); o documento de entrada e o de saída gerados pelo romaneio mostram o link de volta." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

## Task 12 (frontend): tela do relatório

**Files:**
- Create: `webapp/view/reports/storageCustody/Main.view.xml`, `webapp/controller/reports/storageCustody/Main.controller.ts`
- Modify: `webapp/manifest.json`

- [ ] **Step 1: Implement**

`webapp/controller/reports/storageCustody/Main.controller.ts`:

```ts
import InvoiceReportController from "../InvoiceReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * Custódia de Armazenagem (só STANDALONE — o menu some em SAPB1). Situação: 0 Todas, 1 Com pendência fiscal,
 * 2 Completa. Cada romaneio de lote de terceiro sai com as etapas fiscais esperadas logo abaixo.
 * @namespace siagrob1.controller.reports.storageCustody
 */
export default class Main extends InvoiceReportController {
	protected readonly routeName = "storageCustodyReport";
	protected readonly formId = "storageCustodyReportForm";
	protected readonly serverRoute = ServerRoutes.storageCustodyByPeriodReport;

	protected defaults() {
		return { Situation: "0", CardCode: "", CardName: "", StorageAddressCode: "", StorageAddressDescription: "" };
	}

	/** Este relatório não filtra situação do documento nem da NF-e; a situação aqui é a fiscal do romaneio. */
	protected buildPayload() {
		const data = super.buildPayload();
		delete data.Statuses;
		delete data.NfeStatuses;
		delete data.CardName;
		delete data.StorageAddressDescription;
		data.Situation = Number(data.Situation ?? 0);
		return data;
	}
}
```

`webapp/view/reports/storageCustody/Main.view.xml`:

```xml
<mvc:View
	controllerName="siagrob1.controller.reports.storageCustody.Main"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form"
	xmlns:l="sap.ui.layout">
	<Page title="Custódia de Armazenagem">
		<f:SimpleForm
			id="storageCustodyReportForm"
			editable="true"
			layout="ResponsiveGridLayout"
			labelSpanXL="4" labelSpanL="4" labelSpanM="4" labelSpanS="12"
			adjustLabelSpan="false"
			emptySpanXL="0" emptySpanL="0" emptySpanM="0" emptySpanS="0"
			columnsXL="2" columnsL="2" columnsM="2"
			singleContainerFullSize="false"
			busyIndicatorDelay="0">
			<f:content>
				<core:Title text="Período e situação" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.ReportDateRangeFilters" type="XML"/>
				<Label text="Situação"/>
				<Select selectedKey="{params>/Situation}">
					<core:Item key="0" text="Todas"/>
					<core:Item key="1" text="Com pendência fiscal"/>
					<core:Item key="2" text="Completa"/>
				</Select>
				<core:Title text="Filtros" emphasized="true"/>
				<core:Fragment fragmentName="siagrob1.view.reports.fragments.InvoiceReportCommonFilters" type="XML"/>
				<Label text="Depositante"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openBusinessPartnersValueHelp" value="{params>/CardCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData>
						<core:CustomData key="descriptionProperty" value="CardName"/>
					</customData>
				</Input>
				<Input value="{params>/CardName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
				<Label text="Lote"/>
				<Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openStorageAddressesValueHelp" value="{params>/StorageAddressCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData>
						<core:CustomData key="descriptionProperty" value="StorageAddressDescription:Description"/>
					</customData>
				</Input>
				<Input value="{params>/StorageAddressDescription}" editable="false">
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

(Confira o formato de `descriptionProperty` ("destino:origem") nos outros relatórios; o de remessas usa
`CardName:Name` para o armazém.)

`webapp/manifest.json` — rota logo depois da de `storageRemittancesReport`:

```json
        {
          "pattern": "storage-custody/report",
          "name": "storageCustodyReport",
          "target": "storageCustodyReport"
        },
```

e target logo depois do `storageRemittancesReport`:

```json
        "storageCustodyReport": {
          "id": "storageCustodyReport",
          "level": 1,
          "name": "siagrob1.view.reports.storageCustody.Main",
          "clearControlAggregation": true
        },
```

- [ ] **Step 2: Run gates**

Run: `yarn ts-typecheck && yarn lint` e o QUnit.
Expected: sem erros.

- [ ] **Step 3: Commit**

```bash
git add webapp/view/reports/storageCustody webapp/controller/reports/storageCustody webapp/manifest.json
git commit -m "feat(reports): tela do relatório de custódia de armazenagem" -m "Filtros de período, situação fiscal, filial, produto, depositante e lote; chama POST /reports/StorageCustodyByPeriod." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

## Task 13: Verificação ponta a ponta na CEAGUI dev

Sem código novo. Use `superpowers:verification-before-completion`. Suba a stack (memória "Subir a stack local": perfil
`ceagui` para Web/Gateway/Reports + `npx ui5 serve --port 8090` no frontend) e dirija o navegador com Playwright. ⚠️
Mate processos só pelo PID que você abriu (memória "Subagente mata só pelo PID").

- [ ] **Step 1: Gates completos**

Backend: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests` (suíte inteira). Frontend:
`yarn ts-typecheck`, `yarn lint`, QUnit. Registre os números.

- [ ] **Step 2: Cadastros**

Pela tela:
- cinco naturezas com papel e CFOP:
  - 1901/2901 Entrada para beneficiamento;
  - 5902/6902 Devolução simbólica;
  - 1905/2905 Entrada para armazenagem;
  - 5906/6906 Retorno;
  - 5907/6907 Retorno simbólico;
- tributos de cada uma com ICMS suspenso/não incidente (CST 41 ou 50, conforme a CEAGUI) e PIS/COFINS 08 ou 98;
- um produtor PJ de teste como parceiro;
- lote de terceiro com "Passa por beneficiamento" marcado;
- um segundo lote, do mesmo produtor, sem beneficiamento;
- um lote de outro depositante, destino da transferência.

- [ ] **Step 3: Cadeia com beneficiamento**

1. Recepção com bruto ≠ líquido (secagem).
2. Na seção do romaneio, gere a entrada para beneficiamento e transmita-a.
3. Gere a devolução simbólica, confira o `refNFe` no XML assinado e transmita-a.
4. Gere a entrada para armazenagem (quantidade = líquido).

- [ ] **Step 4: Escrituração do depositante**

1. Faça uma segunda Recepção.
2. Lance um Documento de Entrada De terceiro com a natureza 1901, escolhendo o romaneio pelo value help e com quantidade
   diferente da balança.
3. Confirme o documento.
4. Gere a devolução simbólica: ela tem que sair com a quantidade da escrituração.

- [ ] **Step 5: Sem beneficiamento, retirada e transferência**

- No lote sem beneficiamento, faça uma Recepção: só a entrada para armazenagem é esperada.
- Faça uma Expedição e gere o retorno 5906.
- Faça uma transferência de titularidade para o outro depositante:
  - o retorno simbólico 5907 sai na origem;
  - a entrada para armazenagem entra no destino;
  - tente cancelar a transferência e veja a recusa.
- Cancele a entrada para beneficiamento com a devolução ativa e veja a recusa (ordem inversa).

- [ ] **Step 6: Relatório**

Abra "Custódia de Armazenagem" (menu Relatórios) e confira:
- as etapas e as situações batem com as seções dos romaneios;
- "PENDENTE" aparece onde falta nota;
- o saldo físico e o fiscal de cada lote estão corretos;
- os filtros de situação funcionam.

- [ ] **Step 7: Registro**

A SEFAZ de homologação deve rejeitar por CNPJ fictício (181), como nas features anteriores. O que vale é o XML
assinado conferido (CFOP, `refNFe`, `tPag 90`, quantidades).

Guarde em `C:\Projetos\SiagroB1\nfe-homologacao-testes\custodia-2026-10-12\`:
- os prints;
- os números dos documentos;
- a lista do que foi recusado e com qual mensagem.

O modo SAPB1 só se verifica como recusa: a natureza com papel recusada no cadastro SAP. Atualize a memória do projeto
com o estado.
