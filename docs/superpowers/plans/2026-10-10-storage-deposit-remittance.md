# Remessa para depósito em armazém de terceiro — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Emitir a NF-e de remessa para depósito a partir da liberação de compra, vincular os retornos do armazém
(Documento de Entrada) à remessa, reconciliar cada retorno com os romaneios expedidos e mostrar na liberação
Remetido / Retornado / A retornar / Expedido sem retorno — só em STANDALONE.

**Architecture:** Nenhuma entidade de remessa própria. A remessa é um `SalesInvoice` com `ShipmentReleaseKey`
preenchido; o retorno é um `PurchaseInvoice` de terceiro cujas linhas apontam a linha da remessa pelo
`SalesInvoiceItemKey` já existente; a reconciliação é uma tabela nova retorno ↔ romaneio. O papel da natureza
(`StorageDepositRole`) mora em `USAGE_EFFECTS`. Regras e consultas compartilhadas ficam num único
`StorageDepositRules` estático, chamado pelos serviços existentes nos pontos de guarda.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes com InMemory + `ToQueryString` em SqlServer), OData v4
(Microsoft.AspNetCore.OData), xUnit; OpenUI5 + TypeScript, QUnit.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-10-storage-deposit-remittance-design.md`

## Global Constraints

- Somente STANDALONE: toda ação nova recusa em SAPB1 com `"Disponível somente no modo STANDALONE."`
  (`ErpMode.IsStandalone(IConfiguration)`, namespace `SiagroB1.Infra`); telas escondem tudo em SAPB1.
- Identificadores em inglês; textos para o usuário (mensagens de erro, rótulos) em pt-BR.
- Branch `feature/storage-deposit-remittance` nos DOIS repos (o backend já existe; criar o do frontend a partir
  de `main`). Conferir o branch antes de cada commit. **Nunca push, nunca merge.**
- Commits: `tipo(escopo): descrição pt-BR`, tipos `feat|fix|refactor|test|docs|chore`, escopos fechados (usar
  `storage`, `invoice`, `master-data`, `shipment`). Commit com migration leva trailer `DB: AddStorageDepositRemittance`.
  Terminar a mensagem com:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
  ```
- Todo arquivo novo: `git add` logo após criar.
- Todo serviço novo: registrar à mão em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
  (`AddApplicationServices`), `services.AddScoped<T>()`.
- Toda função OData: rota declarada à mão no controller (`[HttpGet("odata/Nome(Key={key})")]`) — a forma não
  declarada toma 404. Todo parâmetro decimal de action é declarado como `double` no EDM (o OData recusa
  `Edm.Decimal` vindo como string).
- FKs novas com `DeleteBehavior.Restrict` (gera `NO ACTION` no SQL Server), como as vizinhas. Não trocar por
  `NoAction` — os dois não são intercambiáveis no EF e a troca gera migration inerte.
- Gate do backend: `dotnet build SiagroB1.sln` sem erros e `dotnet test SiagroB1.Application.Tests` verde.
- Gate do frontend: `yarn ts-typecheck` + `yarn lint` + testes unitários QUnit sem gate de cobertura
  (`npx ui5 serve --port 8080` de pé e
  `npx ui5-test-runner --url "http://localhost:8080/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests" --report-dir <scratchpad>`).
  `yarn test` NUNCA passa (gate de cobertura de 50% irreal) — não usar como critério.
- Nada desta feature altera saldo ou valor de contrato: a natureza com papel diferente de `None` tem efeitos
  `None/None`.

## Review Focus

1. **Retorno do XML com código de produto do armazém** — o `cProd` do armazém quase nunca é o nosso código. A
   importação deve vincular quando a NF-e referencia UMA remessa e tem UM item, trocando o código do rascunho
   pelo nosso (teste na Task 11: `Single_remittance_and_single_item_links_and_takes_our_item_code`).
2. **Compra da Conferência de Armazém não é carregamento** — o par Compra(8)+Perda(13) gerado pela Conferência
   (GAC-1164) tem `TransactionOrigin = WarehouseReconciliation`; não pode virar romaneio candidato nem entrar em
   "Expedido sem retorno" (teste na Task 3: `Warehouse_reconciliation_purchase_is_not_a_shipment`).
3. **Edição da remessa Pendente depois de criada** — o usuário consegue mudar quantidade/cliente pela tela do
   Documento de Saída; a confirmação deve recusar remessa que diverge da liberação (teste na Task 4:
   `Confirm_refuses_remittance_whose_quantity_differs_from_the_release`).
4. **Várias linhas de retorno da mesma remessa num documento só** — a soma das linhas do próprio documento conta
   contra o saldo, e editar uma linha não pode contar a versão antiga dela (teste na Task 7:
   `Two_lines_of_the_same_document_add_up_against_the_balance` e `Editing_a_line_does_not_count_its_old_quantity`).
5. **Troca de liberação (GAC-1177) sobre romaneio reconciliado** — o caminho da troca estorna a compra original
   sem passar pelo `StorageTransactionsCancelService`; precisa da mesma trava (teste na Task 9:
   `Release_change_refuses_reconciled_purchase`).

---

## Mapa de arquivos

**Backend (`siagro-b1-backend/`)**

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Enums/StorageDepositRole.cs` (novo) | Enum None/Remittance/Return |
| `SiagroB1.Domain/Entities/UsageEffect.cs` | + `StorageDepositRole` |
| `SiagroB1.Domain/Models/UsageModel.cs` | + `StorageDepositRole` |
| `SiagroB1.Domain/Entities/SalesInvoice.cs` | + `ShipmentReleaseKey` / `ShipmentRelease` |
| `SiagroB1.Domain/Entities/StorageReturnReconciliation.cs` (novo) | Retorno ↔ romaneio |
| `SiagroB1.Domain/Dtos/StorageDepositDtos.cs` (novo) | DTOs de resumo e de romaneios |
| `SiagroB1.Domain/Dtos/PurchaseInvoiceDraftDto.cs` | + vínculo com a remessa no rascunho do XML |
| `SiagroB1.Infra/Context/AppDbContext.cs` | DbSet + FKs + índice único |
| `SiagroB1.Migrations/AppContext/*_AddStorageDepositRemittance.cs` (gerado) | Migration |
| `SiagroB1.Application/Services/UsageEffectWriter.cs`, `UsageService.cs`, `SAP/UsageService.cs` | Papel gravado/lido/validado |
| `SiagroB1.Application/Services/StorageDeposits/StorageDepositRules.cs` (novo) | Consultas e guardas compartilhadas |
| `SiagroB1.Application/Services/StorageDeposits/StorageDepositReturnGuard.cs` (novo) | Guard das linhas de retorno |
| `SiagroB1.Application/Services/StorageDeposits/ShipmentReleasesCreateStorageRemittanceService.cs` (novo) | "Emitir remessa" |
| `SiagroB1.Application/Services/StorageDeposits/ShipmentReleasesStorageRemittanceSummaryService.cs` (novo) | Resumo da liberação |
| `SiagroB1.Application/Services/StorageDeposits/StorageReturnReconciliationsService.cs` (novo) | Romaneios do retorno, reconciliar, desfazer |
| `SalesInvoicesUsageGuardService`, `SalesInvoicesConfirmService`, `SalesInvoicesCancelService`, `SalesInvoicesReverseConfirmService` | Guardas da remessa |
| `ShipmentReleasesCancelationService`, `ShipmentReleasesDeleteService` | Guarda da liberação |
| `PurchaseInvoicesCreateService`, `PurchaseInvoicesUpdateService`, `PurchaseInvoicesItemsCreateService`, `PurchaseInvoicesItemsUpdateService` | Guard de retorno |
| `PurchaseInvoicesCancelService`, `PurchaseInvoicesDeleteService` | Apagam reconciliações |
| `StorageTransactionsCancelService`, `StorageTransactionsReverseService`, `ShippingTransactionsReverseService`, `ShippingTransactionsChangeReleaseService` | Trava do romaneio reconciliado |
| `SupplierNfeXmlReader.cs`, `PurchaseInvoicesImportXmlService.cs` | `NFref` → vínculo |
| `SiagroB1.Web/Actions/StorageDeposits/*.cs`, `SiagroB1.Web/Functions/StorageDeposits/*.cs` (novos) | Endpoints |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` | Declarações EDM |

**Frontend (`siagro-b1-frontend/`)**

| Arquivo | Responsabilidade |
|---|---|
| `webapp/helpers/StorageDepositHelpers.ts` (novo) + `webapp/test/unit/helpers/StorageDepositHelpers.qunit.ts` (novo) | Regras puras da tela |
| `webapp/model/ServerRoutes.ts` | Rotas novas |
| `webapp/view/usages/fragments/Form.fragment.xml`, `webapp/controller/usages/Add.controller.ts` | Papel no depósito |
| `webapp/controller/shipmentReleases/Detail.controller.ts`, `webapp/view/shipmentReleases/Detail.view.xml`, `fragments/StorageRemittance.fragment.xml` (novo), `fragments/StorageRemittanceDialog.fragment.xml` (novo) | Emitir remessa + saldos |
| `webapp/view/purchaseInvoices/fragments/Items.fragment.xml`, `controller/purchaseInvoices/BaseController.ts`, `Detail.controller.ts`, `Add.controller.ts`, `view/purchaseInvoices/Detail.view.xml`, `fragments/StorageReturnTransactions.fragment.xml` (novo), `fragments/StorageReturnReconcileDialog.fragment.xml` (novo), `webapp/dialogs/fragments/StorageRemittanceItemsSelectDialog.fragment.xml` (novo) | Retorno |
| `webapp/view/salesInvoices/Detail.view.xml`, `controller/salesInvoices/Detail.controller.ts` | Link para a liberação |

---

## Task 1: Modelo de dados e migration

**Files:**
- Create: `SiagroB1.Domain/Enums/StorageDepositRole.cs`, `SiagroB1.Domain/Entities/StorageReturnReconciliation.cs`
- Modify: `SiagroB1.Domain/Entities/UsageEffect.cs`, `SiagroB1.Domain/Models/UsageModel.cs`, `SiagroB1.Domain/Entities/SalesInvoice.cs`, `SiagroB1.Infra/Context/AppDbContext.cs`
- Generate: `SiagroB1.Migrations/AppContext/<timestamp>_AddStorageDepositRemittance.cs`
- Test: `SiagroB1.Application.Tests/Infra/StorageDepositModelTests.cs` (novo), `SiagroB1.Application.Tests/StorageDeposits/StorageDepositSchemaTests.cs` (novo)

**Interfaces:**
- Produces: `enum StorageDepositRole { None = 0, Remittance = 1, Return = 2 }`; `UsageEffect.StorageDepositRole`;
  `UsageModel.StorageDepositRole`; `SalesInvoice.ShipmentReleaseKey : Guid?`, `SalesInvoice.ShipmentRelease : ShipmentRelease?`;
  `StorageReturnReconciliation { Guid Key; Guid PurchaseInvoiceKey; PurchaseInvoice? PurchaseInvoice; Guid StorageTransactionKey; StorageTransaction? StorageTransaction; DateTime CreatedAt; string? CreatedBy }`;
  `AppDbContext.StorageReturnReconciliations : DbSet<StorageReturnReconciliation>`.

- [ ] **Step 1: Write the failing tests**

`SiagroB1.Application.Tests/Infra/StorageDepositModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

/// <summary>Remessa para depósito (spec 2026-10-10 §2): FKs Restrict e um retorno por romaneio.</summary>
public class StorageDepositModelTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void Storage_transaction_is_reconciled_at_most_once()
    {
        using var context = CreateContext();

        var index = context.Model.FindEntityType(typeof(StorageReturnReconciliation))!
            .GetIndexes()
            .Single(x => x.Properties.Select(p => p.Name)
                .SequenceEqual([nameof(StorageReturnReconciliation.StorageTransactionKey)]));

        Assert.True(index.IsUnique);
    }

    [Theory]
    [InlineData(typeof(StorageReturnReconciliation), typeof(PurchaseInvoice))]
    [InlineData(typeof(StorageReturnReconciliation), typeof(StorageTransaction))]
    [InlineData(typeof(SalesInvoice), typeof(ShipmentRelease))]
    public void Foreign_keys_are_restrict(Type dependent, Type principal)
    {
        using var context = CreateContext();

        var fk = context.Model.FindEntityType(dependent)!
            .GetForeignKeys()
            .Single(x => x.PrincipalEntityType.ClrType == principal);

        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public void Role_defaults_to_none_in_the_database()
    {
        using var context = CreateContext();

        var role = context.Model.FindEntityType(typeof(UsageEffect))!
            .FindProperty(nameof(UsageEffect.StorageDepositRole))!;

        Assert.False(role.IsNullable);
    }
}
```

`SiagroB1.Application.Tests/StorageDeposits/StorageDepositSchemaTests.cs`:

```csharp
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.StorageDeposits;

/// <summary>A tela lê a liberação da remessa e o papel da natureza pelo EDM.</summary>
public class StorageDepositSchemaTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Sales_invoice_exposes_the_release_link()
    {
        var type = Model().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == nameof(SalesInvoice));

        Assert.NotNull(type.FindProperty("ShipmentReleaseKey"));
        Assert.NotNull(type.FindProperty("ShipmentRelease"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageDepositModelTests|FullyQualifiedName~StorageDepositSchemaTests"`
Expected: FAIL de compilação (`StorageReturnReconciliation`, `StorageDepositRole`, `ShipmentReleaseKey` não existem).

- [ ] **Step 3: Implement**

`SiagroB1.Domain/Enums/StorageDepositRole.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Papel da natureza na remessa para depósito em armazém de terceiro (spec 2026-10-10). Remessa é o nosso documento
/// de saída para o armazém; Retorno é a NF-e que o armazém emite a cada carregamento. Nenhum dos dois mexe no
/// contrato.
/// </summary>
public enum StorageDepositRole
{
    None = 0,
    Remittance = 1,
    Return = 2,
}
```

`UsageEffect.cs` — acrescentar ao fim da classe:

```csharp
    /// <summary>
    /// Papel na remessa para depósito (spec 2026-10-10 §2.1). Mora aqui, e não em USAGES, para nascer pronto para o
    /// modo SAPB1 (chaveado pelo OUSG.ID), embora a feature hoje seja só STANDALONE.
    /// </summary>
    public StorageDepositRole StorageDepositRole { get; set; }
```

`UsageModel.cs` — ao lado de `IsDefault`:

```csharp
    /// <summary>Papel na remessa para depósito — ver <see cref="Entities.UsageEffect.StorageDepositRole"/>.</summary>
    public StorageDepositRole StorageDepositRole { get; set; }
```

`SalesInvoice.cs` — logo após `SalesInvoiceOriginKey`/`SalesInvoiceOrigin`:

```csharp
    /// <summary>
    /// Remessa para depósito: a liberação de compra cuja quantidade este documento remete ao armazém de terceiro
    /// (spec 2026-10-10 §2.2). Só o "Emitir remessa" grava. Uma liberação tem no máximo uma remessa não cancelada —
    /// regra do serviço, não do banco, porque as canceladas ficam.
    /// </summary>
    public Guid? ShipmentReleaseKey { get; set; }
    public virtual ShipmentRelease? ShipmentRelease { get; set; }
```

`SiagroB1.Domain/Entities/StorageReturnReconciliation.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Um romaneio expedido coberto por uma NF-e de retorno do armazém (spec 2026-10-10 §2.4). Um retorno cobre N
/// romaneios; cada romaneio, inteiro, no máximo um retorno (índice único).
/// </summary>
[Table("STORAGE_RETURN_RECONCILIATIONS")]
public class StorageReturnReconciliation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    public Guid PurchaseInvoiceKey { get; set; }
    public PurchaseInvoice? PurchaseInvoice { get; set; }

    public Guid StorageTransactionKey { get; set; }
    public StorageTransaction? StorageTransaction { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }
}
```

`AppDbContext.cs` — DbSet perto de `RoyaltyWriteOffLines`:

```csharp
    public DbSet<StorageReturnReconciliation> StorageReturnReconciliations { get; set; }
```

e no fim de `OnModelCreating`:

```csharp
        // Remessa para depósito (spec 2026-10-10 §2). Restrict como as vizinhas: nada some em cascata — quem apaga
        // a reconciliação é o cancelamento/exclusão do retorno, no serviço.
        modelBuilder.Entity<SalesInvoice>()
            .HasOne(x => x.ShipmentRelease)
            .WithMany()
            .HasForeignKey(x => x.ShipmentReleaseKey)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StorageReturnReconciliation>()
            .HasOne(x => x.PurchaseInvoice)
            .WithMany()
            .HasForeignKey(x => x.PurchaseInvoiceKey)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<StorageReturnReconciliation>()
            .HasOne(x => x.StorageTransaction)
            .WithMany()
            .HasForeignKey(x => x.StorageTransactionKey)
            .OnDelete(DeleteBehavior.Restrict);

        // Romaneio inteiro, no máximo um retorno (D3).
        modelBuilder.Entity<StorageReturnReconciliation>()
            .HasIndex(x => x.StorageTransactionKey)
            .IsUnique();
```

- [ ] **Step 4: Generate the migration**

Run (de `siagro-b1-backend/`, Bash):

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddStorageDepositRemittance \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```

Conferir o `.cs` gerado: `AddColumn<int>("StorageDepositRole", "USAGE_EFFECTS", nullable: false, defaultValue: 0)`,
`AddColumn<Guid>("ShipmentReleaseKey", "SALES_INVOICES", nullable: true)` + índice + FK `ReferentialAction.Restrict`,
`CreateTable("STORAGE_RETURN_RECONCILIATIONS")` com índice único em `StorageTransactionKey`. Nada mais (se
aparecer alteração alheia, é drift do snapshot: pare e investigue). `git add` dos três arquivos gerados.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageDepositModelTests|FullyQualifiedName~StorageDepositSchemaTests"`
Expected: PASS (5 testes).

- [ ] **Step 6: Commit**

```bash
git add -A SiagroB1.Domain SiagroB1.Infra SiagroB1.Migrations SiagroB1.Application.Tests/Infra/StorageDepositModelTests.cs SiagroB1.Application.Tests/StorageDeposits
git commit -m "feat(storage): modelo da remessa para depósito e da reconciliação" -m "A remessa é um documento de saída ligado à liberação e o retorno é um documento de entrada; a tabela nova liga cada romaneio expedido ao retorno que o cobre, no máximo um retorno por romaneio." -m "DB: AddStorageDepositRemittance" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 2: Papel da natureza no cadastro

**Files:**
- Modify: `SiagroB1.Application/Services/UsageEffectWriter.cs`, `SiagroB1.Application/Services/UsageService.cs`, `SiagroB1.Application/Services/SAP/UsageService.cs`
- Test: `SiagroB1.Application.Tests/Usages/UsageStorageDepositRoleTests.cs` (novo)

**Interfaces:**
- Consumes: `StorageDepositRole`, `UsageModel.StorageDepositRole` (Task 1).
- Produces: `UsageModel.StorageDepositRole` preenchido pelos dois `UsageService.QueryAll()`; regras de cadastro.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>"Papel no depósito" da natureza (spec 2026-10-10 §2.1): vai e volta, casa com o tipo e não mexe no contrato.</summary>
public class UsageStorageDepositRoleTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Remittance() => new()
    {
        Name = "Remessa para depósito", Direction = UsageDirection.Outgoing,
        CfopOutgoingInState = "5905", CfopOutgoingOutState = "6905", PisCst = "08", CofinsCst = "08",
        RequiresQuantity = true, StorageDepositRole = StorageDepositRole.Remittance,
    };

    private static UsageModel Return() => new()
    {
        Name = "Retorno de depósito", Direction = UsageDirection.Incoming,
        CfopIncomingInState = "1906", CfopIncomingOutState = "2906", PisCst = "98", CofinsCst = "98",
        RequiresQuantity = true, StorageDepositRole = StorageDepositRole.Return,
    };

    [Fact]
    public async Task Role_is_saved_and_read_back()
    {
        var name = Guid.NewGuid().ToString();
        var created = await Service(TestDb.CreateUnitOfWork(name)).CreateAsync(Remittance());

        var read = await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code);

        Assert.Equal(StorageDepositRole.Remittance, read!.StorageDepositRole);
    }

    [Fact]
    public async Task Remittance_must_be_outgoing()
    {
        var model = Remittance();
        model.Direction = UsageDirection.Incoming;
        model.CfopIncomingInState = "1905";
        model.CfopIncomingOutState = "2905";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de remessa para depósito precisa ser de saída.", e.Message);
    }

    [Fact]
    public async Task Return_must_be_incoming()
    {
        var model = Return();
        model.Direction = UsageDirection.Outgoing;
        model.CfopOutgoingInState = "5906";
        model.CfopOutgoingOutState = "6906";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de retorno de depósito precisa ser de entrada.", e.Message);
    }

    [Fact]
    public async Task Deposit_role_cannot_touch_the_contract()
    {
        var model = Remittance();
        model.RequiresContract = true;
        model.ContractBalanceEffect = ContractBalanceEffect.Consume;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(TestDb.CreateUnitOfWork()).CreateAsync(model));

        Assert.Equal("Natureza de remessa ou retorno de depósito não pode alterar o contrato.", e.Message);
    }

    [Fact]
    public async Task Update_can_clear_the_role()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var created = await Service(db).CreateAsync(Return());

        var model = Return();
        model.StorageDepositRole = StorageDepositRole.None;
        await Service(db).UpdateAsync(created.Code, model);

        Assert.Equal(StorageDepositRole.None,
            (await Service(TestDb.CreateUnitOfWork(name)).GetByIdAsync(created.Code))!.StorageDepositRole);
    }
}
```

Mais um teste no arquivo de testes do `SAP/UsageService` existente (procure com
`grep -rln "Services.SAP" SiagroB1.Application.Tests/Usages`; se não houver um que construa o serviço SAP, crie o
teste copiando a construção usada por `grep -rn "new SiagroB1.Application.Services.SAP.UsageService\|SAP.UsageService(" SiagroB1.Application.Tests`):

```csharp
    [Fact]
    public async Task Sap_mode_refuses_a_deposit_role()
    {
        // construa o UsageService do SAP como nos testes vizinhos
        var model = /* UsageModel com efeito None e */ new UsageModel { Name = "X", StorageDepositRole = StorageDepositRole.Remittance };

        var e = await Assert.ThrowsAsync<DefaultException>(() => sapService.UpdateAsync(1, model));

        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~UsageStorageDepositRole"`
Expected: FAIL (papel volta `None`; nenhuma das mensagens é lançada).

- [ ] **Step 3: Implement**

`UsageEffectWriter.ValidateEffects` — acrescentar ao fim:

```csharp
        if (model.StorageDepositRole != StorageDepositRole.None && hasEffect)
        {
            throw new DefaultException(
                "Natureza de remessa ou retorno de depósito não pode alterar o contrato.");
        }

        var direction = model.Direction ?? UsageDirection.Outgoing;

        if (model.StorageDepositRole == StorageDepositRole.Remittance && direction != UsageDirection.Outgoing)
            throw new DefaultException("Natureza de remessa para depósito precisa ser de saída.");

        if (model.StorageDepositRole == StorageDepositRole.Return && direction != UsageDirection.Incoming)
            throw new DefaultException("Natureza de retorno de depósito precisa ser de entrada.");
```

(Em SAPB1 `Direction` vem do OUSG; se ele chegar nulo, a regra trata como saída — mas em SAPB1 o papel já é
recusado abaixo, então não há caminho em que isso importe.)

`UsageEffectWriter.WriteAsync` — junto das demais atribuições:

```csharp
        effect.StorageDepositRole = model.StorageDepositRole;
```

`UsageService.QueryAll()` (STANDALONE) — junto de `IsDefault = ...`:

```csharp
                    StorageDepositRole = effect != null ? effect.StorageDepositRole : StorageDepositRole.None,
```

`SAP/UsageService.cs` — na projeção, junto de `IsDefault = x.effect?.IsDefault ?? false,`:

```csharp
                StorageDepositRole = x.effect?.StorageDepositRole ?? StorageDepositRole.None,
```

e no início de `UpdateAsync` do SAP:

```csharp
        // A remessa para depósito é só STANDALONE por enquanto (spec 2026-10-10 D10).
        if (entity.StorageDepositRole != StorageDepositRole.None)
            throw new DefaultException("Disponível somente no modo STANDALONE.");
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Usage"`
Expected: PASS (inclui os testes de natureza já existentes).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application SiagroB1.Application.Tests/Usages
git commit -m "feat(master-data): papel da natureza na remessa para depósito" -m "Remessa e retorno de depósito são reconhecidos pela natureza. O cadastro amarra o papel ao tipo (remessa é saída, retorno é entrada) e proíbe efeito no contrato, porque o grão em depósito continua nosso." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 3: Regras compartilhadas (`StorageDepositRules`) e semente de teste

**Files:**
- Create: `SiagroB1.Application/Services/StorageDeposits/StorageDepositRules.cs`
- Create: `SiagroB1.Application.Tests/Support/StorageDepositSeed.cs`
- Test: `SiagroB1.Application.Tests/StorageDeposits/StorageDepositRulesTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces (todas `public static` em `SiagroB1.Application.Services.StorageDeposits.StorageDepositRules`):
  - `const string StandaloneOnlyMessage = "Disponível somente no modo STANDALONE."`
  - `IQueryable<SalesInvoice> ActiveRemittances(AppDbContext ctx)`
  - `Task<SalesInvoice?> FindActiveRemittanceAsync(AppDbContext ctx, Guid shipmentReleaseKey)` (com `Items`)
  - `IQueryable<StorageTransaction> ShippedTransactions(AppDbContext ctx, Guid shipmentReleaseKey, ReleaseOrigin origin)`
  - `Task<Dictionary<Guid, decimal>> ReturnedByRemittanceItemAsync(AppDbContext ctx, IReadOnlyCollection<Guid> remittanceItemKeys, Guid? excludingPurchaseInvoiceKey)`
  - `Task EnsureNoActiveRemittanceAsync(AppDbContext ctx, Guid shipmentReleaseKey, string action)` — lança `ApplicationException`
  - `Task EnsureTransactionsNotReconciledAsync(AppDbContext ctx, IEnumerable<Guid> storageTransactionKeys)` — lança `ApplicationException`
  - `string DocumentNumber(string? taxDocumentNumber, string? invoiceNumber)`
- Seed (`SiagroB1.Application.Tests.Support.StorageDepositSeed`): `static async Task<StorageDepositSeed> CreateAsync(UnitOfWork db, decimal released = 100_000m)` com propriedades
  `PurchaseContract Contract`, `ShipmentRelease Release`, `int RemittanceUsageCode`, `int ReturnUsageCode`,
  e métodos `Task<SalesInvoice> AddRemittanceAsync(decimal quantity, InvoiceStatus status = InvoiceStatus.Confirmed)`,
  `Task<PurchaseInvoice> AddReturnAsync(SalesInvoice remittance, decimal quantity, InvoiceStatus status = InvoiceStatus.Pending)`,
  `Task<StorageTransaction> AddShipmentAsync(decimal netWeight, StorageTransactionType type = StorageTransactionType.Purchase, TransactionCode? origin = null)`.
  Constantes: `Warehouse = "ARM001"`, `Supplier = "F0001"`, `Item = "SOJA"`, `Branch = "01"`.

- [ ] **Step 1: Write the seed**

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Contrato de compra + liberação no armazém de terceiro ARM001, e as naturezas de remessa e de retorno.</summary>
public sealed class StorageDepositSeed
{
    public const string Warehouse = "ARM001";
    public const string Supplier = "F0001";
    public const string Item = "SOJA";
    public const string Branch = "01";

    private readonly UnitOfWork _db;
    private StorageDepositSeed(UnitOfWork db) => _db = db;

    public required PurchaseContract Contract { get; init; }
    public required ShipmentRelease Release { get; init; }
    public int RemittanceUsageCode { get; } = 905;
    public int ReturnUsageCode { get; } = 906;

    public static async Task<StorageDepositSeed> CreateAsync(UnitOfWork db, decimal released = 100_000m)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "00000001", BranchCode = Branch, CardCode = Supplier, ItemCode = Item,
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "2526", DeliveryLocationCode = Warehouse,
            Status = ContractStatus.Approved,
        };
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(), BranchCode = Branch, PurchaseContractKey = contract.Key, ReleasedQuantity = released,
            DeliveryLocationCode = Warehouse, DeliveryLocationName = "ARMAZEM TERCEIRO", Status = ReleaseStatus.Actived,
        };

        db.Context.PurchaseContracts.Add(contract);
        db.Context.ShipmentReleases.Add(release);
        db.Context.Usages.AddRange(
            new Usage { Code = 905, Name = "REMESSA P/ DEPOSITO", Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5905", CfopOutgoingOutState = "6905" },
            new Usage { Code = 906, Name = "RETORNO DE DEPOSITO", Direction = UsageDirection.Incoming, CfopIncomingInState = "1906", CfopIncomingOutState = "2906" });
        db.Context.UsageEffects.AddRange(
            new UsageEffect { UsageCode = 905, StorageDepositRole = StorageDepositRole.Remittance },
            new UsageEffect { UsageCode = 906, StorageDepositRole = StorageDepositRole.Return });
        await db.SaveChangesAsync();

        return new StorageDepositSeed(db) { Contract = contract, Release = release };
    }

    public async Task<SalesInvoice> AddRemittanceAsync(decimal quantity, InvoiceStatus status = InvoiceStatus.Confirmed)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = Branch, CardCode = Warehouse, InvoiceNumber = "S" + Guid.NewGuid().ToString("N")[..6],
            TaxDocumentNumber = "000000123", InvoiceStatus = status, ShipmentReleaseKey = Release.Key,
            Items = [new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 2m, UsageCode = RemittanceUsageCode }],
        };
        _db.Context.SalesInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    public async Task<PurchaseInvoice> AddReturnAsync(SalesInvoice remittance, decimal quantity, InvoiceStatus status = InvoiceStatus.Pending)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = Branch, CardCode = Warehouse, InvoiceNumber = "E" + Guid.NewGuid().ToString("N")[..6],
            TaxDocumentNumber = "000000456", InvoiceStatus = status,
            Items = [new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 2m,
                UsageCode = ReturnUsageCode, SalesInvoiceItemKey = remittance.Items.Single().Key,
            }],
        };
        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    public async Task<StorageTransaction> AddShipmentAsync(
        decimal netWeight, StorageTransactionType type = StorageTransactionType.Purchase, TransactionCode? origin = null)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "R" + Guid.NewGuid().ToString("N")[..6], BranchCode = Branch,
            CardCode = Supplier, ItemCode = Item, UnitOfMeasureCode = "KG", WarehouseCode = Warehouse,
            TransactionType = type, TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = netWeight, GrossWeight = netWeight, ShipmentReleaseKey = Release.Key, TransactionOrigin = origin,
            TruckCode = "ABC1D23",
        };
        _db.Context.StorageTransactions.Add(transaction);
        await _db.SaveChangesAsync();
        return transaction;
    }
}
```

(Se o compilador exigir outros membros `required` ou se `Usage`/`ContractStatus` tiverem nomes diferentes,
ajuste pelo erro — a semente não testa nada por si.)

- [ ] **Step 2: Write the failing tests**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.StorageDeposits;

public class StorageDepositRulesTests
{
    [Fact]
    public async Task Active_remittance_ignores_cancelled_ones()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Cancelled);
        var active = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);

        var found = await StorageDepositRules.FindActiveRemittanceAsync(db.Context, seed.Release.Key);

        Assert.Equal(active.Key, found!.Key);
        Assert.Single(found.Items);
    }

    [Fact]
    public async Task Returned_counts_pending_and_confirmed_but_not_cancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        await seed.AddReturnAsync(remittance, 30_000m, InvoiceStatus.Pending);
        await seed.AddReturnAsync(remittance, 20_000m, InvoiceStatus.Confirmed);
        await seed.AddReturnAsync(remittance, 99_000m, InvoiceStatus.Cancelled);
        var itemKey = remittance.Items.Single().Key!.Value;

        var returned = await StorageDepositRules.ReturnedByRemittanceItemAsync(db.Context, [itemKey], null);

        Assert.Equal(50_000m, returned[itemKey]);
    }

    [Fact]
    public async Task Returned_can_exclude_one_document()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var mine = await seed.AddReturnAsync(remittance, 30_000m);
        await seed.AddReturnAsync(remittance, 20_000m);
        var itemKey = remittance.Items.Single().Key!.Value;

        var returned = await StorageDepositRules.ReturnedByRemittanceItemAsync(db.Context, [itemKey], mine.Key);

        Assert.Equal(20_000m, returned[itemKey]);
    }

    [Fact]
    public async Task Shipped_transactions_of_a_standard_release_are_the_purchases()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var purchase = await seed.AddShipmentAsync(30_000m);
        await seed.AddShipmentAsync(30_000m, StorageTransactionType.SalesShipment);
        await seed.AddShipmentAsync(1_000m, StorageTransactionType.PurchaseReturn);

        var keys = await StorageDepositRules.ShippedTransactions(db.Context, seed.Release.Key, ReleaseOrigin.Standard)
            .Select(t => t.Key).ToListAsync();

        Assert.Equal([purchase.Key], keys);
    }

    [Fact]
    public async Task Warehouse_reconciliation_purchase_is_not_a_shipment()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddShipmentAsync(500m, StorageTransactionType.Purchase, TransactionCode.WarehouseReconciliation);

        Assert.False(await StorageDepositRules
            .ShippedTransactions(db.Context, seed.Release.Key, ReleaseOrigin.Standard).AnyAsync());
    }

    [Fact]
    public async Task Release_without_purchase_leg_ships_by_sales_shipment()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var sales = await seed.AddShipmentAsync(30_000m, StorageTransactionType.SalesShipment);
        await seed.AddShipmentAsync(500m, StorageTransactionType.WarehouseLoss);

        var keys = await StorageDepositRules
            .ShippedTransactions(db.Context, seed.Release.Key, ReleaseOrigin.OwnershipTransfer)
            .Select(t => t.Key).ToListAsync();

        Assert.Equal([sales.Key], keys);
    }

    [Fact]
    public async Task Release_with_active_remittance_refuses_the_action()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);

        var e = await Assert.ThrowsAsync<ApplicationException>(() =>
            StorageDepositRules.EnsureNoActiveRemittanceAsync(db.Context, seed.Release.Key, "cancelar"));

        Assert.Equal("A liberação tem a remessa para depósito nº 000000123 ativa. Cancele a remessa antes de cancelar a liberação.", e.Message);
    }

    [Fact]
    public async Task Reconciled_transaction_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var ret = await seed.AddReturnAsync(remittance, 30_000m);
        var shipment = await seed.AddShipmentAsync(30_000m);
        db.Context.StorageReturnReconciliations.Add(new StorageReturnReconciliation
            { PurchaseInvoiceKey = ret.Key, StorageTransactionKey = shipment.Key });
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<ApplicationException>(() =>
            StorageDepositRules.EnsureTransactionsNotReconciledAsync(db.Context, [shipment.Key]));

        Assert.Equal($"O romaneio {shipment.Code} está reconciliado com o retorno de depósito nº 000000456. Desfaça a reconciliação antes.", e.Message);
    }

    /// <summary>InMemory esconde LINQ intraduzível: as consultas precisam virar SQL no provedor real.</summary>
    [Fact]
    public void Queries_translate_to_sql_server()
    {
        using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none").Options);

        Assert.NotEmpty(StorageDepositRules.ActiveRemittances(ctx).ToQueryString());
        Assert.NotEmpty(StorageDepositRules.ShippedTransactions(ctx, Guid.NewGuid(), ReleaseOrigin.Standard).ToQueryString());
        Assert.NotEmpty(StorageDepositRules.ShippedTransactions(ctx, Guid.NewGuid(), ReleaseOrigin.OwnershipTransfer).ToQueryString());
        Assert.NotEmpty(StorageDepositRules.ReturnLinesQuery(ctx, [Guid.NewGuid()], Guid.NewGuid()).ToQueryString());
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageDepositRulesTests"`
Expected: FAIL de compilação (`StorageDepositRules` não existe).

- [ ] **Step 4: Implement `StorageDepositRules`**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.StorageDeposits;

/// <summary>
/// Regras e consultas da remessa para depósito em armazém de terceiro (spec 2026-10-10), num lugar só para os
/// serviços novos e para as guardas plantadas nos fluxos existentes (liberação, documento de saída, documento de
/// entrada, romaneio). Consultas devolvem IQueryable para o teste conferir a tradução no SQL Server.
/// </summary>
public static class StorageDepositRules
{
    public const string StandaloneOnlyMessage = "Disponível somente no modo STANDALONE.";

    /// <summary>Remessas não canceladas (Pendente e Confirmada contam: duas abertas remeteriam o dobro).</summary>
    public static IQueryable<SalesInvoice> ActiveRemittances(AppDbContext ctx) =>
        ctx.SalesInvoices.Where(i => i.ShipmentReleaseKey != null && i.InvoiceStatus != InvoiceStatus.Cancelled);

    public static Task<SalesInvoice?> FindActiveRemittanceAsync(AppDbContext ctx, Guid shipmentReleaseKey) =>
        ActiveRemittances(ctx).Include(i => i.Items).FirstOrDefaultAsync(i => i.ShipmentReleaseKey == shipmentReleaseKey);

    /// <summary>
    /// Romaneios que representam CARREGAMENTO da liberação — os candidatos à reconciliação. Mesmo eixo do
    /// <see cref="ShipmentReleasesRecalculateShippedService.ShippedSign"/>, só com os tipos que somam: Compra(8) na
    /// liberação com perna de compra, Saída(7) na sem. Fica fora a Compra gerada pela Conferência de Armazém
    /// (TransactionOrigin = WarehouseReconciliation): é perda de papel, não caminhão, e a falta é quebra técnica (D3).
    /// A Perda(13) também fica fora, pelo mesmo motivo. Cancelado não conta.
    /// </summary>
    public static IQueryable<StorageTransaction> ShippedTransactions(AppDbContext ctx, Guid shipmentReleaseKey, ReleaseOrigin origin)
    {
        var query = ctx.StorageTransactions.Where(t => t.ShipmentReleaseKey == shipmentReleaseKey
                                                       && t.TransactionStatus != StorageTransactionsStatus.Cancelled);

        return ReleaseOriginRules.ShipsWithoutPurchaseLeg(origin)
            ? query.Where(t => t.TransactionType == StorageTransactionType.SalesShipment)
            : query.Where(t => t.TransactionType == StorageTransactionType.Purchase
                               && (t.TransactionOrigin == null || t.TransactionOrigin != TransactionCode.WarehouseReconciliation));
    }

    /// <summary>Linhas de retorno não canceladas que apontam para as linhas de remessa dadas.</summary>
    public static IQueryable<PurchaseInvoiceItem> ReturnLinesQuery(
        AppDbContext ctx, IReadOnlyCollection<Guid> remittanceItemKeys, Guid? excludingPurchaseInvoiceKey) =>
        ctx.PurchaseInvoicesItems.Where(l => l.SalesInvoiceItemKey != null
                                             && remittanceItemKeys.Contains(l.SalesInvoiceItemKey.Value)
                                             && l.PurchaseInvoice!.InvoiceStatus != InvoiceStatus.Cancelled
                                             && l.PurchaseInvoiceKey != excludingPurchaseInvoiceKey);

    public static Task<Dictionary<Guid, decimal>> ReturnedByRemittanceItemAsync(
        AppDbContext ctx, IReadOnlyCollection<Guid> remittanceItemKeys, Guid? excludingPurchaseInvoiceKey) =>
        ReturnLinesQuery(ctx, remittanceItemKeys, excludingPurchaseInvoiceKey)
            .AsNoTracking()
            .GroupBy(l => l.SalesInvoiceItemKey!.Value)
            .Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity);

    public static async Task EnsureNoActiveRemittanceAsync(AppDbContext ctx, Guid shipmentReleaseKey, string action)
    {
        var remittance = await ActiveRemittances(ctx).AsNoTracking()
            .Where(i => i.ShipmentReleaseKey == shipmentReleaseKey)
            .Select(i => new { i.TaxDocumentNumber, i.InvoiceNumber })
            .FirstOrDefaultAsync();

        if (remittance != null)
            throw new ApplicationException(
                $"A liberação tem a remessa para depósito nº {DocumentNumber(remittance.TaxDocumentNumber, remittance.InvoiceNumber)} " +
                $"ativa. Cancele a remessa antes de {action} a liberação.");
    }

    /// <summary>
    /// Romaneio reconciliado não sai de cena (D8): estorná-lo deixaria o retorno apontando um carregamento que não
    /// existe mais. Vale também para a troca de liberação, que estorna sem passar pelo cancelamento comum.
    /// </summary>
    public static async Task EnsureTransactionsNotReconciledAsync(AppDbContext ctx, IEnumerable<Guid> storageTransactionKeys)
    {
        var keys = storageTransactionKeys.ToList();
        if (keys.Count == 0)
            return;

        var hit = await ctx.StorageReturnReconciliations.AsNoTracking()
            .Where(r => keys.Contains(r.StorageTransactionKey))
            .Select(r => new
            {
                r.StorageTransaction!.Code,
                r.PurchaseInvoice!.TaxDocumentNumber,
                r.PurchaseInvoice.InvoiceNumber,
            })
            .FirstOrDefaultAsync();

        if (hit != null)
            throw new ApplicationException(
                $"O romaneio {hit.Code} está reconciliado com o retorno de depósito nº " +
                $"{DocumentNumber(hit.TaxDocumentNumber, hit.InvoiceNumber)}. Desfaça a reconciliação antes.");
    }

    /// <summary>Número da NF-e quando existe; senão o número interno do documento.</summary>
    public static string DocumentNumber(string? taxDocumentNumber, string? invoiceNumber) =>
        string.IsNullOrWhiteSpace(taxDocumentNumber) ? invoiceNumber ?? string.Empty : taxDocumentNumber;
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageDepositRulesTests"`
Expected: PASS (9 testes).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/StorageDeposits SiagroB1.Application.Tests/Support/StorageDepositSeed.cs SiagroB1.Application.Tests/StorageDeposits
git commit -m "feat(storage): regras compartilhadas da remessa para depósito" -m "Concentra a remessa ativa da liberação, o retornado por remessa e os romaneios que contam como carregamento. A compra gerada pela Conferência de Armazém fica fora: é quebra de papel, não caminhão." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 4: Guarda de papel no Documento de Saída (criação e confirmação)

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesUsageGuardService.cs`, `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs`, `SiagroB1.Application/Services/StorageDeposits/StorageDepositRules.cs`
- Test: `SiagroB1.Application.Tests/StorageDeposits/SalesInvoiceRemittanceGuardTests.cs`

**Interfaces:**
- Consumes: `UsageModel.StorageDepositRole` (Task 2), `StorageDepositSeed` (Task 3).
- Produces: `StorageDepositRules.EnsureSalesRoles(SalesInvoice invoice, IReadOnlyList<SalesInvoiceItemUsage> resolved)` (sync, lança `DefaultException`);
  `StorageDepositRules.EnsureRemittanceMatchesReleaseAsync(AppDbContext ctx, SalesInvoice invoice)` (lança `DefaultException`).

- [ ] **Step 1: Write the failing tests**

Construa o `SalesInvoicesUsageGuardService` com `new SalesInvoicesUsageGuardService(new UsageService(db, NullLogger<UsageService>.Instance))`.

```csharp
public class SalesInvoiceRemittanceGuardTests
{
    private static SalesInvoicesUsageGuardService Guard(UnitOfWork db) =>
        new(new UsageService(db, NullLogger<UsageService>.Instance));

    private static SalesInvoice Invoice(int usageCode, Guid? releaseKey) => new()
    {
        Key = Guid.NewGuid(), CardCode = StorageDepositSeed.Warehouse, ShipmentReleaseKey = releaseKey,
        Items = [new SalesInvoiceItem { ItemCode = StorageDepositSeed.Item, UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 1m, UsageCode = usageCode }],
    };

    [Fact]
    public async Task Remittance_usage_requires_the_release()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Guard(db).ValidateAsync(Invoice(seed.RemittanceUsageCode, null)));

        Assert.Equal("Natureza REMESSA P/ DEPOSITO é de remessa para depósito: emita pela liberação (Emitir remessa).", e.Message);
    }

    [Fact]
    public async Task Release_link_requires_remittance_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        db.Context.Usages.Add(new Usage { Code = 1, Name = "VENDA", Direction = UsageDirection.Outgoing });
        db.Context.UsageEffects.Add(new UsageEffect { UsageCode = 1 });
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Guard(db).ValidateAsync(Invoice(1, seed.Release.Key)));

        Assert.Equal("Remessa para depósito só aceita natureza de remessa para depósito.", e.Message);
    }

    [Fact]
    public async Task Return_usage_is_refused_on_outgoing_document()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Guard(db).ValidateAsync(Invoice(seed.ReturnUsageCode, null)));

        Assert.Equal("Natureza RETORNO DE DEPOSITO é de retorno de depósito e não pode ser usada no documento de saída.", e.Message);
    }

    [Fact]
    public async Task Confirm_refuses_remittance_whose_quantity_differs_from_the_release()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db, released: 100_000m);
        var remittance = await seed.AddRemittanceAsync(90_000m, InvoiceStatus.Pending);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositRules.EnsureRemittanceMatchesReleaseAsync(db.Context, remittance));

        Assert.Equal("A remessa para depósito precisa ter uma linha com a quantidade liberada (100.000,000) para o armazém ARM001.", e.Message);
    }

    [Fact]
    public async Task Confirm_refuses_remittance_to_another_recipient()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);
        remittance.CardCode = "OUTRO";

        await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositRules.EnsureRemittanceMatchesReleaseAsync(db.Context, remittance));
    }

    [Fact]
    public async Task Matching_remittance_passes()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);

        await StorageDepositRules.EnsureRemittanceMatchesReleaseAsync(db.Context, remittance);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoiceRemittanceGuardTests"`
Expected: FAIL.

- [ ] **Step 3: Implement**

Em `StorageDepositRules` (usings: `System.Globalization`, `SiagroB1.Application.Services.SalesInvoices`, `SiagroB1.Domain.Exceptions`):

```csharp
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Spec §3.2: o documento de saída e o papel da natureza andam juntos.</summary>
    public static void EnsureSalesRoles(SalesInvoice invoice, IReadOnlyList<SalesInvoiceItemUsage> resolved)
    {
        foreach (var (_, usage) in resolved)
        {
            if (usage.StorageDepositRole == StorageDepositRole.Return)
                throw new DefaultException(
                    $"Natureza {usage.Name} é de retorno de depósito e não pode ser usada no documento de saída.");

            if (usage.StorageDepositRole == StorageDepositRole.Remittance && invoice.ShipmentReleaseKey is null)
                throw new DefaultException(
                    $"Natureza {usage.Name} é de remessa para depósito: emita pela liberação (Emitir remessa).");
        }

        if (invoice.ShipmentReleaseKey != null &&
            (resolved.Count != invoice.Items.Count || resolved.Any(r => r.Usage.StorageDepositRole != StorageDepositRole.Remittance)))
            throw new DefaultException("Remessa para depósito só aceita natureza de remessa para depósito.");
    }

    /// <summary>
    /// A remessa Pendente pode ser editada pela tela do Documento de Saída; na confirmação ela precisa continuar
    /// sendo a remessa da liberação: uma linha, quantidade liberada, destinatário = armazém, liberação viva.
    /// </summary>
    public static async Task EnsureRemittanceMatchesReleaseAsync(AppDbContext ctx, SalesInvoice invoice)
    {
        if (invoice.ShipmentReleaseKey is not { } releaseKey)
            return;

        var release = await ctx.ShipmentReleases.AsNoTracking().FirstOrDefaultAsync(r => r.Key == releaseKey)
                      ?? throw new DefaultException("Liberação da remessa para depósito não encontrada.");

        if (release.Status == ReleaseStatus.Cancelled)
            throw new DefaultException("A liberação desta remessa para depósito está cancelada.");

        var lines = invoice.Items.Count > 0
            ? invoice.Items.ToList()
            : await ctx.SalesInvoicesItems.AsNoTracking().Where(i => i.SalesInvoiceKey == invoice.Key).ToListAsync();

        if (lines.Count != 1 || lines[0].Quantity != release.ReleasedQuantity || invoice.CardCode != release.DeliveryLocationCode)
            throw new DefaultException(
                $"A remessa para depósito precisa ter uma linha com a quantidade liberada " +
                $"({release.ReleasedQuantity.ToString("N3", PtBr)}) para o armazém {release.DeliveryLocationCode}.");
    }
```

`SalesInvoicesUsageGuardService.ValidateAsync` — antes de `ValidateWeight(invoice, resolved);`:

```csharp
        StorageDepositRules.EnsureSalesRoles(invoice, resolved);
```

`SalesInvoicesConfirmService` — no início do caminho de confirmação, logo após carregar o documento (antes do
roteamento por origem), acrescentar:

```csharp
        // Remessa para depósito (spec 2026-10-10 §3.1): a tela permite editar o rascunho, a confirmação recusa o que
        // deixou de ser a remessa da liberação.
        await StorageDepositRules.EnsureRemittanceMatchesReleaseAsync(db.Context, invoice);
```

(use o nome real da variável do documento carregado e do `IUnitOfWork`/contexto nesse serviço.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoice"`
Expected: PASS (inclui os testes já existentes do documento de saída).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application SiagroB1.Application.Tests/StorageDeposits
git commit -m "feat(invoice): natureza de remessa para depósito só com a liberação" -m "A remessa só nasce pela liberação e só com natureza de remessa; o retorno de depósito não sai em documento de saída. Na confirmação, a remessa editada à mão precisa continuar batendo com a liberação." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 5: "Emitir remessa" (serviço + action)

**Files:**
- Create: `SiagroB1.Application/Services/StorageDeposits/ShipmentReleasesCreateStorageRemittanceService.cs`
- Create: `SiagroB1.Web/Actions/StorageDeposits/ShipmentReleasesCreateStorageRemittanceController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/StorageDeposits/ShipmentReleasesCreateStorageRemittanceServiceTests.cs`

**Interfaces:**
- Consumes: `StorageDepositRules.FindActiveRemittanceAsync`, `SalesInvoicesCreateService.ExecuteAsync(SalesInvoice, string, CommitMode, bool)`.
- Produces: `public sealed record StorageRemittanceRequest(Guid ShipmentReleaseKey, int UsageCode, decimal UnitPrice);`
  `ShipmentReleasesCreateStorageRemittanceService.ExecuteAsync(StorageRemittanceRequest request, string userName) : Task<SalesInvoice>`;
  EDM action `ShipmentReleasesCreateStorageRemittance(ShipmentReleaseKey: Guid, UsageCode: int, UnitPrice: double) → Guid`.

- [ ] **Step 1: Write the failing tests**

Construa o serviço com o `SalesInvoicesCreateService` montado como em `InvoiceLineChargeRulesTests.SalesCreate`
(copie a fábrica para este arquivo), `FakeBusinessPartnerService(new() { [StorageDepositSeed.Warehouse] = "ARMAZEM TERCEIRO" })`,
`new UsageService(db, NullLogger<UsageService>.Instance)` e `TestConfiguration.Erp(mode)`.

```csharp
public class ShipmentReleasesCreateStorageRemittanceServiceTests
{
    private static ShipmentReleasesCreateStorageRemittanceService Service(UnitOfWork db, string erp = "STANDALONE")
    {
        var partners = new FakeBusinessPartnerService(new Dictionary<string, string> { [StorageDepositSeed.Warehouse] = "ARMAZEM TERCEIRO" });
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var create = new SalesInvoicesCreateService(
            db, partners, new FakeItemService(), new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages), new SalesInvoicesCfopResolveService(db, usages, partners),
            TaxTestServices.InactiveApply(db), TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesCreateService>.Instance);
        return new(db, TestConfiguration.Erp(erp), partners, usages, create,
            NullLogger<ShipmentReleasesCreateStorageRemittanceService>.Instance);
    }

    [Fact]
    public async Task Creates_pending_remittance_to_the_warehouse_with_the_released_quantity()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db, released: 100_000m);

        var created = await Service(db).ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, 2.5m), "tester");

        var saved = await db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == created.Key);
        Assert.Equal(StorageDepositSeed.Warehouse, saved.CardCode);
        Assert.Equal(seed.Release.Key, saved.ShipmentReleaseKey);
        Assert.Equal(InvoiceStatus.Pending, saved.InvoiceStatus);
        Assert.Equal(StorageDepositSeed.Branch, saved.BranchCode);
        var line = Assert.Single(saved.Items);
        Assert.Equal(StorageDepositSeed.Item, line.ItemCode);
        Assert.Equal(100_000m, line.Quantity);
        Assert.Equal(2.5m, line.UnitPrice);
        Assert.Equal(seed.RemittanceUsageCode, line.UsageCode);
        Assert.Null(line.SalesContractKey);
    }

    [Fact]
    public async Task Refuses_in_sap_mode()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, "SAPB1").ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, 2m), "tester"));

        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }

    [Fact]
    public async Task Refuses_cancelled_release()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        seed.Release.Status = ReleaseStatus.Cancelled;
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, 2m), "tester"));

        Assert.Equal("Liberação cancelada não pode ter remessa para depósito.", e.Message);
    }

    [Fact]
    public async Task Refuses_second_active_remittance_but_allows_after_cancelling()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var first = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, 2m), "tester"));
        Assert.Equal("A liberação já tem a remessa para depósito nº 000000123 ativa.", e.Message);

        first.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();
        await Service(db).ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, 2m), "tester");
    }

    [Fact]
    public async Task Refuses_warehouse_without_business_partner()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        seed.Release.DeliveryLocationCode = "INTERNO";
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, 2m), "tester"));

        Assert.Equal("O armazém INTERNO não tem parceiro de negócio cadastrado: a remessa precisa de destinatário.", e.Message);
    }

    [Fact]
    public async Task Refuses_usage_without_remittance_role()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(seed.Release.Key, seed.ReturnUsageCode, 2m), "tester"));

        Assert.Equal("Escolha uma natureza de remessa para depósito.", e.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Refuses_non_positive_price(decimal price)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(seed.Release.Key, seed.RemittanceUsageCode, price), "tester"));

        Assert.Equal("Informe o preço unitário da remessa.", e.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~CreateStorageRemittance"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implement the service**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.StorageDeposits;

public sealed record StorageRemittanceRequest(Guid ShipmentReleaseKey, int UsageCode, decimal UnitPrice);

/// <summary>
/// "Emitir remessa" (spec 2026-10-10 §3.1): cria o Documento de Saída Pendente que remete ao armazém de terceiro a
/// quantidade liberada. Não confirma nem transmite — daí em diante é o fluxo comum do Documento de Saída.
/// </summary>
public class ShipmentReleasesCreateStorageRemittanceService(
    IUnitOfWork db,
    IConfiguration configuration,
    IBusinessPartnerService businessPartners,
    IUsage usages,
    SalesInvoicesCreateService createService,
    ILogger<ShipmentReleasesCreateStorageRemittanceService> logger)
{
    public async Task<SalesInvoice> ExecuteAsync(StorageRemittanceRequest request, string userName)
    {
        // TODA a validação antes de qualquer escrita.
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(StorageDepositRules.StandaloneOnlyMessage);

        var release = await db.Context.ShipmentReleases
                          .Include(r => r.PurchaseContract)
                          .FirstOrDefaultAsync(r => r.Key == request.ShipmentReleaseKey)
                      ?? throw new NotFoundException("Liberação não encontrada.");

        if (release.Status == ReleaseStatus.Cancelled)
            throw new DefaultException("Liberação cancelada não pode ter remessa para depósito.");

        var active = await StorageDepositRules.FindActiveRemittanceAsync(db.Context, release.Key);
        if (active != null)
            throw new DefaultException(
                $"A liberação já tem a remessa para depósito nº {StorageDepositRules.DocumentNumber(active.TaxDocumentNumber, active.InvoiceNumber)} ativa.");

        if (await businessPartners.GetByIdAsync(release.DeliveryLocationCode) is null)
            throw new DefaultException(
                $"O armazém {release.DeliveryLocationCode} não tem parceiro de negócio cadastrado: a remessa precisa de destinatário.");

        var usage = await usages.GetByIdAsync(request.UsageCode);
        if (usage is null || usage.StorageDepositRole != StorageDepositRole.Remittance)
            throw new DefaultException("Escolha uma natureza de remessa para depósito.");

        if (request.UnitPrice <= 0)
            throw new DefaultException("Informe o preço unitário da remessa.");

        var contract = release.PurchaseContract!;
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            BranchCode = release.BranchCode ?? contract.BranchCode,
            CardCode = release.DeliveryLocationCode,
            InvoiceType = SalesInvoiceType.Normal,
            InvoiceDate = DateTime.Now.Date,
            ShipmentReleaseKey = release.Key,
            Comments = $"Remessa para depósito da liberação do contrato {contract.Code}.",
            Items =
            [
                new SalesInvoiceItem
                {
                    ItemCode = contract.ItemCode,
                    UnitOfMeasureCode = contract.UnitOfMeasureCode,
                    Quantity = release.ReleasedQuantity,
                    UnitPrice = request.UnitPrice,
                    UsageCode = usage.Code,
                },
            ],
        };

        try
        {
            await db.BeginTransactionAsync();
            await createService.ExecuteAsync(invoice, userName, CommitMode.Deferred);
            await db.SaveChangesAsync();
            await db.CommitAsync();
            return invoice;
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e, "Erro ao criar a remessa para depósito da liberação {Key}", release.Key);
            throw;
        }
    }
}
```

(Se `PurchaseContract.BranchCode` não existir com esse nome, use o campo de filial do contrato; o teste confere a
filial. Se `IUnitOfWork.BeginTransactionAsync` falhar no InMemory, siga o padrão de `SalesInvoicesNfeReturnCreateService`,
que já é testado assim.)

- [ ] **Step 4: Action controller, EDM, DI**

`SiagroB1.Web/Actions/StorageDeposits/ShipmentReleasesCreateStorageRemittanceController.cs`:

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.StorageDeposits;

/// <summary>"Emitir remessa" na liberação: cria a remessa Pendente e devolve a chave do Documento de Saída.</summary>
public class ShipmentReleasesCreateStorageRemittanceController(ShipmentReleasesCreateStorageRemittanceService service) : ODataController
{
    [HttpPost("odata/ShipmentReleasesCreateStorageRemittance")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM.
        if (parameters is null
            || !parameters.TryGetValue("ShipmentReleaseKey", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key)
            || !parameters.TryGetValue("UsageCode", out var usageObj) || usageObj is null
            || !parameters.TryGetValue("UnitPrice", out var priceObj) || priceObj is null)
            return BadRequest("Parâmetros obrigatórios: ShipmentReleaseKey, UsageCode, UnitPrice.");

        try
        {
            var created = await service.ExecuteAsync(
                new StorageRemittanceRequest(key, Convert.ToInt32(usageObj, CultureInfo.InvariantCulture),
                    Convert.ToDecimal(priceObj, CultureInfo.InvariantCulture)),
                User.Identity?.Name ?? "Unknown");
            return Ok(created.Key);
        }
        catch (NotFoundException e) { return NotFound(e.Message); }
        catch (DefaultException e) { return BadRequest(e.Message); }
        catch (ApplicationException e) { return BadRequest(e.Message); }
    }
}
```

`ODataConfigurations.cs` — perto das actions do documento de entrada:

```csharp
        // Remessa para depósito em armazém de terceiro (spec 2026-10-10). UnitPrice é double: Edm.Decimal chega como
        // string e o OData recusa.
        var shipmentReleasesCreateStorageRemittance = modelBuilder.Action("ShipmentReleasesCreateStorageRemittance");
        shipmentReleasesCreateStorageRemittance.Parameter<Guid>("ShipmentReleaseKey");
        shipmentReleasesCreateStorageRemittance.Parameter<int>("UsageCode");
        shipmentReleasesCreateStorageRemittance.Parameter<double>("UnitPrice");
        shipmentReleasesCreateStorageRemittance.Returns<Guid>();
```

`ServiceCollectionExtensions.cs` — `services.AddScoped<ShipmentReleasesCreateStorageRemittanceService>();` (+ `using SiagroB1.Application.Services.StorageDeposits;`).

- [ ] **Step 5: Run tests and build**

Run: `dotnet build SiagroB1.sln` → 0 erros; `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageDeposit"` → PASS.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application SiagroB1.Web SiagroB1.Application.Tests/StorageDeposits
git commit -m "feat(storage): emitir remessa para depósito a partir da liberação" -m "O armazém de terceiro que exige nota recebe uma remessa com a quantidade liberada; o documento nasce Pendente e segue o fluxo comum de confirmação e NF-e. Uma remessa ativa por liberação; reemitir exige cancelar a anterior." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 6: Guardas na liberação e na remessa

**Files:**
- Modify: `ShipmentReleasesCancelationService.cs`, `ShipmentReleasesDeleteService.cs`, `SalesInvoicesCancelService.cs`, `SalesInvoicesReverseConfirmService.cs`, `StorageDepositRules.cs`
- Test: `SiagroB1.Application.Tests/StorageDeposits/StorageDepositCrossGuardsTests.cs`

**Interfaces:**
- Consumes: `StorageDepositRules.EnsureNoActiveRemittanceAsync` (Task 3).
- Produces: `StorageDepositRules.EnsureRemittanceHasNoReturnAsync(AppDbContext ctx, Guid salesInvoiceKey)` (lança `DefaultException`).

`ShipmentReleasesUpdateService` lança `MethodAccessException` sempre — a quantidade da liberação não muda, então
não há guarda de alteração de quantidade a escrever (spec §3.4, primeira linha, fica coberta).

- [ ] **Step 1: Write the failing tests**

Construa os serviços como nos testes vizinhos (procure `new ShipmentReleasesCancelationService(`,
`new ShipmentReleasesDeleteService(`, `new SalesInvoicesCancelService(` e `new SalesInvoicesReverseConfirmService(`
em `SiagroB1.Application.Tests` e copie a construção).

```csharp
    [Fact]
    public async Task Release_with_active_remittance_cannot_be_cancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);

        var e = await Assert.ThrowsAsync<ApplicationException>(() =>
            CancelRelease(db).ExecuteAsync(seed.Release.Key, "tester", "troca de armazém"));

        Assert.StartsWith("A liberação tem a remessa para depósito nº", e.Message);
    }

    [Fact]
    public async Task Release_with_active_remittance_cannot_be_deleted()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        seed.Release.Status = ReleaseStatus.Pending;
        await db.SaveChangesAsync();
        await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);

        await Assert.ThrowsAnyAsync<Exception>(() => DeleteRelease(db).ExecuteAsync(seed.Release.Key));
        Assert.True(await db.Context.ShipmentReleases.AnyAsync(r => r.Key == seed.Release.Key));
    }

    [Fact]
    public async Task Release_with_cancelled_remittance_can_be_cancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Cancelled);

        await CancelRelease(db).ExecuteAsync(seed.Release.Key, "tester", "troca de armazém");
    }

    [Fact]
    public async Task Remittance_with_return_cannot_be_cancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);
        await seed.AddReturnAsync(remittance, 10_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() => CancelSales(db).ExecuteAsync(remittance.Key, "tester"));

        Assert.Equal("A remessa para depósito tem retorno lançado (nº 000000456). Cancele os retornos antes.", e.Message);
    }

    [Fact]
    public async Task Remittance_with_cancelled_return_can_be_cancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);
        await seed.AddReturnAsync(remittance, 10_000m, InvoiceStatus.Cancelled);

        await CancelSales(db).ExecuteAsync(remittance.Key, "tester");
    }

    [Fact]
    public async Task Remittance_with_return_cannot_have_confirmation_reversed()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Confirmed);
        await seed.AddReturnAsync(remittance, 10_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() => ReverseSales(db).ExecuteAsync(remittance.Key, "tester"));

        Assert.StartsWith("A remessa para depósito tem retorno lançado", e.Message);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageDepositCrossGuardsTests"`
Expected: FAIL (ações passam).

- [ ] **Step 3: Implement**

`StorageDepositRules`:

```csharp
    public static async Task EnsureRemittanceHasNoReturnAsync(AppDbContext ctx, Guid salesInvoiceKey)
    {
        var ret = await ctx.PurchaseInvoicesItems.AsNoTracking()
            .Where(l => l.SalesInvoiceItem!.SalesInvoiceKey == salesInvoiceKey
                        && l.SalesInvoiceItem.SalesInvoice!.ShipmentReleaseKey != null
                        && l.PurchaseInvoice!.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(l => new { l.PurchaseInvoice!.TaxDocumentNumber, l.PurchaseInvoice.InvoiceNumber })
            .FirstOrDefaultAsync();

        if (ret != null)
            throw new DefaultException(
                $"A remessa para depósito tem retorno lançado (nº {DocumentNumber(ret.TaxDocumentNumber, ret.InvoiceNumber)}). " +
                "Cancele os retornos antes.");
    }
```

- `ShipmentReleasesCancelationService.ExecuteAsync` — logo depois do teste de status
  (`Cancelled or Completed`): `await StorageDepositRules.EnsureNoActiveRemittanceAsync(context, sr.Key, "cancelar");`
- `ShipmentReleasesDeleteService.ExecuteAsync` — depois de `if (entity.Status != ReleaseStatus.Pending)`:
  `await StorageDepositRules.EnsureNoActiveRemittanceAsync(db.Context, entity.Key, "excluir");`
  (se o serviço captura exceção e devolve `false`, mantenha a mensagem subindo: ajuste o `catch` para relançar
  `ApplicationException`, como o teste exige que a liberação continue lá).
- `SalesInvoicesCancelService.EnsureBusinessRules` é síncrono e chamado em dois lugares; torne-o
  `EnsureBusinessRulesAsync` e acrescente ao fim `await StorageDepositRules.EnsureRemittanceHasNoReturnAsync(db.Context, invoice.Key);`
  (vale também para `EnsureCanCancelAsync`, que a NF-e chama antes de pedir o cancelamento à SEFAZ).
- `SalesInvoicesReverseConfirmService.ExecuteAsync` — logo depois de carregar o documento:
  `await StorageDepositRules.EnsureRemittanceHasNoReturnAsync(db.Context, key);`

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ShipmentRelease|FullyQualifiedName~SalesInvoice|FullyQualifiedName~StorageDeposit"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application SiagroB1.Application.Tests/StorageDeposits
git commit -m "feat(storage): travar liberação e remessa enquanto houver vínculo" -m "A liberação com remessa ativa não é cancelada nem excluída, e a remessa com retorno lançado não é cancelada nem estornada: cada lado só sai depois de desfazer o seguinte, para nenhum documento ficar apontando o vazio." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 7: Guard das linhas de retorno no Documento de Entrada

**Files:**
- Create: `SiagroB1.Application/Services/StorageDeposits/StorageDepositReturnGuard.cs`
- Modify: `PurchaseInvoicesCreateService.cs`, `PurchaseInvoicesUpdateService.cs`, `PurchaseInvoicesItemsCreateService.cs`, `PurchaseInvoicesItemsUpdateService.cs`
- Test: `SiagroB1.Application.Tests/StorageDeposits/StorageDepositReturnGuardTests.cs`

**Interfaces:**
- Consumes: `StorageDepositRules.ReturnedByRemittanceItemAsync`, `StorageDepositRules.DocumentNumber`.
- Produces: `public static class StorageDepositReturnGuard { public static Task EnsureAsync(AppDbContext ctx, string cardCode, Guid? purchaseInvoiceKey, IReadOnlyList<PurchaseInvoiceItem> documentLines); }`
  — `documentLines` = TODAS as linhas que o documento terá depois da operação. O papel é lido de
  `USAGE_EFFECTS` direto (vale nos dois modos, sem depender de `IUsage`).

- [ ] **Step 1: Write the failing tests**

Os testes chamam o guard direto (é a regra); um teste por caminho garante que ele está plantado.

```csharp
public class StorageDepositReturnGuardTests
{
    private static PurchaseInvoiceItem Line(StorageDepositSeed seed, Guid? remittanceItemKey, decimal qty, string item = StorageDepositSeed.Item) => new()
    {
        Key = Guid.NewGuid(), ItemCode = item, UnitOfMeasureCode = "KG", Quantity = qty, UnitPrice = 1m,
        UsageCode = seed.ReturnUsageCode, SalesInvoiceItemKey = remittanceItemKey,
    };

    [Fact]
    public async Task Return_line_requires_the_remittance()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null, [Line(seed, null, 1m)]));

        Assert.Equal("Linha SOJA com natureza de retorno de depósito precisa indicar a remessa.", e.Message);
    }

    [Fact]
    public async Task Linked_document_must_be_an_active_remittance()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Cancelled);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null,
                [Line(seed, remittance.Items.Single().Key, 1m)]));

        Assert.Equal("A linha SOJA aponta um documento que não é remessa para depósito ativa.", e.Message);
    }

    [Fact]
    public async Task Return_must_be_of_the_same_item()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null,
                [Line(seed, remittance.Items.Single().Key, 1m, item: "MILHO")]));

        Assert.Equal("A linha MILHO é de outro produto que a remessa nº 000000123 (SOJA).", e.Message);
    }

    [Fact]
    public async Task Return_must_come_from_the_warehouse()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, "OUTRO", null, [Line(seed, remittance.Items.Single().Key, 1m)]));

        Assert.Equal("O retorno precisa ser do armazém ARM001, destinatário da remessa nº 000000123.", e.Message);
    }

    [Fact]
    public async Task Quantity_cannot_pass_the_pending_balance_counting_other_documents()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        await seed.AddReturnAsync(remittance, 70_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null,
                [Line(seed, remittance.Items.Single().Key, 30_001m)]));

        Assert.Equal("Retorno de depósito: a quantidade (30.001,000) passa do saldo a retornar da remessa nº 000000123 (30.000,000).", e.Message);
    }

    [Fact]
    public async Task Two_lines_of_the_same_document_add_up_against_the_balance()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var key = remittance.Items.Single().Key;

        await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null,
                [Line(seed, key, 60_000m), Line(seed, key, 40_001m)]));
    }

    [Fact]
    public async Task Editing_a_line_does_not_count_its_old_quantity()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var mine = await seed.AddReturnAsync(remittance, 90_000m);
        var edited = mine.Items.Single();
        edited.Quantity = 100_000m;

        await StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, mine.Key, [edited]);
    }

    [Fact]
    public async Task Cancelled_return_gives_the_balance_back()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        await seed.AddReturnAsync(remittance, 100_000m, InvoiceStatus.Cancelled);

        await StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null,
            [Line(seed, remittance.Items.Single().Key, 100_000m)]);
    }

    [Fact]
    public async Task Remittance_usage_is_refused_on_incoming_document()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var line = Line(seed, null, 1m);
        line.UsageCode = seed.RemittanceUsageCode;

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null, [line]));

        Assert.Equal("Natureza de remessa para depósito não pode ser usada no documento de entrada.", e.Message);
    }

    [Fact]
    public async Task Lines_without_deposit_role_pass()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var line = Line(seed, null, 1m);
        line.UsageCode = null;

        await StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null, [line]);
    }
}
```

Mais um teste por caminho de gravação (Create, Update/SyncItems, ItemsCreate, ItemsUpdate): monte o serviço como
nos testes existentes desse serviço (procure `new PurchaseInvoicesCreateService(` etc. em
`SiagroB1.Application.Tests`) e mande uma linha de retorno sem `SalesInvoiceItemKey`; esperado
`DefaultException` com a mensagem de `Return_line_requires_the_remittance`. Nomes:
`Create_path_runs_the_guard`, `Header_sync_path_runs_the_guard`, `Line_create_path_runs_the_guard`, `Line_update_path_runs_the_guard`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageDepositReturnGuardTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implement the guard**

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.StorageDeposits;

/// <summary>
/// Linha de retorno de depósito no Documento de Entrada (spec 2026-10-10 §3.3, D7). Recebe TODAS as linhas que o
/// documento terá depois da operação e exclui o próprio documento do retornado no banco — assim a mesma conta vale
/// para o deep-insert, o SyncItems do cabeçalho e os POST/PATCH de linha, e editar uma linha não conta a versão antiga.
/// </summary>
public static class StorageDepositReturnGuard
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static async Task EnsureAsync(
        AppDbContext ctx, string cardCode, Guid? purchaseInvoiceKey, IReadOnlyList<PurchaseInvoiceItem> documentLines)
    {
        var usageCodes = documentLines.Where(l => l.UsageCode != null).Select(l => l.UsageCode!.Value).Distinct().ToList();
        if (usageCodes.Count == 0)
            return;

        var roles = await ctx.UsageEffects.AsNoTracking()
            .Where(e => usageCodes.Contains(e.UsageCode) && e.StorageDepositRole != StorageDepositRole.None)
            .ToDictionaryAsync(e => e.UsageCode, e => e.StorageDepositRole);

        if (roles.Count == 0)
            return;

        var returnLines = new List<PurchaseInvoiceItem>();
        foreach (var line in documentLines.Where(l => l.UsageCode != null && roles.ContainsKey(l.UsageCode.Value)))
        {
            if (roles[line.UsageCode!.Value] == StorageDepositRole.Remittance)
                throw new DefaultException("Natureza de remessa para depósito não pode ser usada no documento de entrada.");

            if (line.SalesInvoiceItemKey is null)
                throw new DefaultException(
                    $"Linha {line.ItemCode} com natureza de retorno de depósito precisa indicar a remessa.");

            returnLines.Add(line);
        }

        var remittanceKeys = returnLines.Select(l => l.SalesInvoiceItemKey!.Value).Distinct().ToList();
        var remittances = await ctx.SalesInvoicesItems.AsNoTracking()
            .Where(i => i.Key != null && remittanceKeys.Contains(i.Key.Value)
                        && i.SalesInvoice!.ShipmentReleaseKey != null
                        && i.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(i => new
            {
                Key = i.Key!.Value, i.ItemCode, i.Quantity, i.SalesInvoice!.CardCode,
                Number = i.SalesInvoice.TaxDocumentNumber, i.SalesInvoice.InvoiceNumber,
            })
            .ToDictionaryAsync(x => x.Key);

        var returned = await StorageDepositRules.ReturnedByRemittanceItemAsync(ctx, remittanceKeys, purchaseInvoiceKey);

        foreach (var group in returnLines.GroupBy(l => l.SalesInvoiceItemKey!.Value))
        {
            var first = group.First();
            if (!remittances.TryGetValue(group.Key, out var remittance))
                throw new DefaultException(
                    $"A linha {first.ItemCode} aponta um documento que não é remessa para depósito ativa.");

            var number = StorageDepositRules.DocumentNumber(remittance.Number, remittance.InvoiceNumber);

            if (remittance.CardCode != cardCode)
                throw new DefaultException(
                    $"O retorno precisa ser do armazém {remittance.CardCode}, destinatário da remessa nº {number}.");

            foreach (var line in group.Where(l => l.ItemCode != remittance.ItemCode))
                throw new DefaultException(
                    $"A linha {line.ItemCode} é de outro produto que a remessa nº {number} ({remittance.ItemCode}).");

            var available = remittance.Quantity - returned.GetValueOrDefault(group.Key);
            var requested = group.Sum(l => l.Quantity);

            if (requested > available)
                throw new DefaultException(
                    $"Retorno de depósito: a quantidade ({requested.ToString("N3", PtBr)}) passa do saldo a retornar " +
                    $"da remessa nº {number} ({available.ToString("N3", PtBr)}).");
        }
    }
}
```

- [ ] **Step 4: Plant the guard in the four write paths**

Cada chamada fica DEPOIS das validações de linha já existentes e ANTES do `SaveChanges`:

- `PurchaseInvoicesCreateService.ExecuteAsync`: `await StorageDepositReturnGuard.EnsureAsync(db.Context, invoice.CardCode, null, invoice.Items.ToList());`
- `PurchaseInvoicesUpdateService.ExecuteAsync` (SyncItems): depois de aplicar o payload às linhas em memória,
  `await StorageDepositReturnGuard.EnsureAsync(db.Context, current.CardCode, key, <lista final das linhas do documento>);`
  (a lista que vai ser persistida — as linhas mantidas/alteradas + as novas, sem as removidas).
- `PurchaseInvoicesItemsCreateService.ExecuteAsync`: carregar o cabeçalho e as demais linhas:
  ```csharp
  var header = await db.Context.PurchaseInvoices.AsNoTracking()
      .Where(x => x.Key == item.PurchaseInvoiceKey).Select(x => x.CardCode).FirstAsync();
  var others = await db.Context.PurchaseInvoicesItems.AsNoTracking()
      .Where(x => x.PurchaseInvoiceKey == item.PurchaseInvoiceKey).ToListAsync();
  await StorageDepositReturnGuard.EnsureAsync(db.Context, header, item.PurchaseInvoiceKey, [.. others, item]);
  ```
- `PurchaseInvoicesItemsUpdateService.ExecuteAsync`: igual, trocando a versão gravada da linha (`existing`) pela
  editada: `[.. others.Where(x => x.Key != existing.Key), existing]` — chamar DEPOIS de copiar os campos do
  `entity` para o `existing` (incluindo `SalesInvoiceItemKey`, `UsageCode`, `Quantity`, `ItemCode`).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoice|FullyQualifiedName~StorageDeposit"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application SiagroB1.Application.Tests/StorageDeposits
git commit -m "feat(invoice): retorno de depósito amarrado à remessa" -m "A linha de entrada com natureza de retorno de depósito precisa apontar a remessa, do mesmo produto e do mesmo armazém, e não pode retornar mais do que foi remetido. A regra vale nos quatro caminhos que gravam linha." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 8: Reconciliação retorno ↔ romaneio

**Files:**
- Create: `SiagroB1.Application/Services/StorageDeposits/StorageReturnReconciliationsService.cs`
- Create: `SiagroB1.Domain/Dtos/StorageDepositDtos.cs`
- Create: `SiagroB1.Web/Actions/StorageDeposits/PurchaseInvoicesReconcileStorageReturnController.cs`, `SiagroB1.Web/Actions/StorageDeposits/PurchaseInvoicesUndoStorageReturnReconciliationController.cs`, `SiagroB1.Web/Functions/StorageDeposits/PurchaseInvoicesStorageReturnTransactionsController.cs`
- Modify: `PurchaseInvoicesCancelService.cs`, `PurchaseInvoicesDeleteService.cs`, `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/StorageDeposits/StorageReturnReconciliationsServiceTests.cs`

**Interfaces:**
- Consumes: `StorageDepositRules.ShippedTransactions`, `StorageDepositSeed`.
- Produces:
  ```csharp
  public class StorageReturnTransactionDto
  {
      public Guid Key { get; set; }
      public string? Code { get; set; }
      public DateTime? TransactionDate { get; set; }
      public string? TruckCode { get; set; }
      public decimal NetWeight { get; set; }
      public string? UnitOfMeasureCode { get; set; }
      public bool IsReconciled { get; set; }   // reconciliado com ESTE retorno
  }
  ```
  `StorageReturnReconciliationsService` com
  `Task<List<StorageReturnTransactionDto>> ListAsync(Guid purchaseInvoiceKey)`,
  `Task ReconcileAsync(Guid purchaseInvoiceKey, IReadOnlyList<Guid> storageTransactionKeys, string userName)`,
  `Task UndoAsync(Guid purchaseInvoiceKey, Guid storageTransactionKey)`;
  `static Task RemoveForInvoiceAsync(AppDbContext ctx, Guid purchaseInvoiceKey)` (sem SaveChanges).
  EDM: action `PurchaseInvoicesReconcileStorageReturn(Key: Guid, TransactionKeys: Collection(Guid))`,
  action `PurchaseInvoicesUndoStorageReturnReconciliation(Key: Guid, TransactionKey: Guid)`,
  function `PurchaseInvoicesStorageReturnTransactions(Key) → Collection(StorageReturnTransactionDto)`.

- [ ] **Step 1: Write the failing tests**

Construa com `new StorageReturnReconciliationsService(db, TestConfiguration.Erp("STANDALONE"))`.

```csharp
public class StorageReturnReconciliationsServiceTests
{
    private static StorageReturnReconciliationsService Service(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, TestConfiguration.Erp(erp));

    [Fact]
    public async Task Lists_unreconciled_shipments_of_the_remittance_release()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var ret = await seed.AddReturnAsync(remittance, 30_000m);
        var s1 = await seed.AddShipmentAsync(15_000m);
        var s2 = await seed.AddShipmentAsync(15_000m);

        var rows = await Service(db).ListAsync(ret.Key);

        Assert.Equal(new[] { s1.Key, s2.Key }.OrderBy(x => x), rows.Select(r => r.Key).OrderBy(x => x));
        Assert.All(rows, r => Assert.False(r.IsReconciled));
    }

    [Fact]
    public async Task Reconcile_marks_rows_and_hides_them_from_other_returns()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var ret1 = await seed.AddReturnAsync(remittance, 30_000m);
        var ret2 = await seed.AddReturnAsync(remittance, 30_000m);
        var s1 = await seed.AddShipmentAsync(30_000m);

        await Service(db).ReconcileAsync(ret1.Key, [s1.Key], "tester");

        Assert.True((await Service(db).ListAsync(ret1.Key)).Single().IsReconciled);
        Assert.Empty(await Service(db).ListAsync(ret2.Key));
    }

    [Fact]
    public async Task Refuses_shipment_of_another_release()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var ret = await seed.AddReturnAsync(remittance, 30_000m);
        var foreign = await seed.AddShipmentAsync(30_000m);
        foreign.ShipmentReleaseKey = Guid.NewGuid();
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db).ReconcileAsync(ret.Key, [foreign.Key], "tester"));

        Assert.Equal($"O romaneio {foreign.Code} não é carregamento da liberação da remessa ou já está reconciliado.", e.Message);
    }

    [Fact]
    public async Task Refuses_already_reconciled_shipment()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var ret1 = await seed.AddReturnAsync(remittance, 30_000m);
        var ret2 = await seed.AddReturnAsync(remittance, 30_000m);
        var s1 = await seed.AddShipmentAsync(30_000m);
        await Service(db).ReconcileAsync(ret1.Key, [s1.Key], "tester");

        await Assert.ThrowsAsync<DefaultException>(() => Service(db).ReconcileAsync(ret2.Key, [s1.Key], "tester"));
    }

    [Fact]
    public async Task Refuses_cancelled_return_and_document_without_return_line()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var cancelled = await seed.AddReturnAsync(remittance, 30_000m, InvoiceStatus.Cancelled);
        var s1 = await seed.AddShipmentAsync(30_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db).ReconcileAsync(cancelled.Key, [s1.Key], "tester"));
        Assert.Equal("Documento de entrada cancelado não pode ser reconciliado.", e.Message);
    }

    [Fact]
    public async Task Refuses_empty_selection()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var ret = await seed.AddReturnAsync(await seed.AddRemittanceAsync(100_000m), 1m);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db).ReconcileAsync(ret.Key, [], "tester"));
        Assert.Equal("Selecione ao menos um romaneio.", e.Message);
    }

    [Fact]
    public async Task Undo_removes_the_link()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var ret = await seed.AddReturnAsync(await seed.AddRemittanceAsync(100_000m), 30_000m);
        var s1 = await seed.AddShipmentAsync(30_000m);
        await Service(db).ReconcileAsync(ret.Key, [s1.Key], "tester");

        await Service(db).UndoAsync(ret.Key, s1.Key);

        Assert.False(await db.Context.StorageReturnReconciliations.AnyAsync());
    }

    [Fact]
    public async Task Refuses_in_sap_mode()
    {
        var db = TestDb.CreateUnitOfWork();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db, "SAPB1").ListAsync(Guid.NewGuid()));
        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }

    [Fact]
    public async Task Cancelling_the_return_removes_its_reconciliations()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var ret = await seed.AddReturnAsync(await seed.AddRemittanceAsync(100_000m), 30_000m);
        var s1 = await seed.AddShipmentAsync(30_000m);
        await Service(db).ReconcileAsync(ret.Key, [s1.Key], "tester");

        await new PurchaseInvoicesCancelService(db).ExecuteAsync(ret.Key, "tester");

        Assert.False(await db.Context.StorageReturnReconciliations.AnyAsync());
    }

    [Fact]
    public async Task Deleting_the_return_removes_its_reconciliations()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var ret = await seed.AddReturnAsync(await seed.AddRemittanceAsync(100_000m), 30_000m);
        var s1 = await seed.AddShipmentAsync(30_000m);
        await Service(db).ReconcileAsync(ret.Key, [s1.Key], "tester");

        await new PurchaseInvoicesDeleteService(db).ExecuteAsync(ret.Key);

        Assert.False(await db.Context.StorageReturnReconciliations.AnyAsync());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageReturnReconciliationsServiceTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implement DTOs and service**

`SiagroB1.Domain/Dtos/StorageDepositDtos.cs` — `StorageReturnTransactionDto` conforme Interfaces (mais os DTOs da
Task 10, que entram no mesmo arquivo lá).

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.StorageDeposits;

/// <summary>
/// Reconciliação do retorno de depósito com os romaneios expedidos (spec 2026-10-10 §3.5). Candidato = carregamento
/// da liberação de alguma remessa referenciada pelas linhas do retorno, ainda não reconciliado com outro retorno.
/// Diferença de peso entre nota e romaneios é informativa (D3) — a tela mostra, o serviço não barra.
/// </summary>
public class StorageReturnReconciliationsService(IUnitOfWork db, IConfiguration configuration)
{
    public async Task<List<StorageReturnTransactionDto>> ListAsync(Guid purchaseInvoiceKey)
    {
        EnsureStandalone();
        var releases = await ReleasesOfAsync(purchaseInvoiceKey);
        var rows = new List<StorageReturnTransactionDto>();

        foreach (var (releaseKey, origin) in releases)
        {
            rows.AddRange(await StorageDepositRules.ShippedTransactions(db.Context, releaseKey, origin)
                .Where(t => !db.Context.StorageReturnReconciliations.Any(r => r.StorageTransactionKey == t.Key
                                                                              && r.PurchaseInvoiceKey != purchaseInvoiceKey))
                .Select(t => new StorageReturnTransactionDto
                {
                    Key = t.Key, Code = t.Code, TransactionDate = t.TransactionDate, TruckCode = t.TruckCode,
                    NetWeight = t.NetWeight, UnitOfMeasureCode = t.UnitOfMeasureCode,
                    IsReconciled = db.Context.StorageReturnReconciliations.Any(r => r.StorageTransactionKey == t.Key),
                })
                .AsNoTracking()
                .ToListAsync());
        }

        return rows.OrderBy(r => r.TransactionDate).ThenBy(r => r.Code).ToList();
    }

    public async Task ReconcileAsync(Guid purchaseInvoiceKey, IReadOnlyList<Guid> storageTransactionKeys, string userName)
    {
        EnsureStandalone();

        var status = await db.Context.PurchaseInvoices.AsNoTracking()
                         .Where(x => x.Key == purchaseInvoiceKey).Select(x => (InvoiceStatus?)x.InvoiceStatus).FirstOrDefaultAsync()
                     ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (status == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento de entrada cancelado não pode ser reconciliado.");

        if (storageTransactionKeys.Count == 0)
            throw new DefaultException("Selecione ao menos um romaneio.");

        var available = (await ListAsync(purchaseInvoiceKey)).Where(r => !r.IsReconciled).Select(r => r.Key).ToHashSet();

        foreach (var key in storageTransactionKeys.Distinct())
        {
            if (!available.Contains(key))
            {
                var code = await db.Context.StorageTransactions.Where(t => t.Key == key).Select(t => t.Code).FirstOrDefaultAsync();
                throw new DefaultException(
                    $"O romaneio {code ?? key.ToString()} não é carregamento da liberação da remessa ou já está reconciliado.");
            }

            db.Context.StorageReturnReconciliations.Add(new StorageReturnReconciliation
            {
                PurchaseInvoiceKey = purchaseInvoiceKey, StorageTransactionKey = key,
                CreatedAt = DateTime.Now, CreatedBy = userName,
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task UndoAsync(Guid purchaseInvoiceKey, Guid storageTransactionKey)
    {
        EnsureStandalone();

        var row = await db.Context.StorageReturnReconciliations
                      .FirstOrDefaultAsync(r => r.PurchaseInvoiceKey == purchaseInvoiceKey && r.StorageTransactionKey == storageTransactionKey)
                  ?? throw new NotFoundException("Reconciliação não encontrada.");

        db.Context.StorageReturnReconciliations.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>Cancelar/excluir o retorno solta os romaneios (spec §3.4). Sem SaveChanges: quem chama é dono.</summary>
    public static async Task RemoveForInvoiceAsync(AppDbContext ctx, Guid purchaseInvoiceKey) =>
        ctx.StorageReturnReconciliations.RemoveRange(
            await ctx.StorageReturnReconciliations.Where(r => r.PurchaseInvoiceKey == purchaseInvoiceKey).ToListAsync());

    private async Task<List<(Guid Key, ReleaseOrigin Origin)>> ReleasesOfAsync(Guid purchaseInvoiceKey)
    {
        var releases = await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Where(l => l.PurchaseInvoiceKey == purchaseInvoiceKey && l.SalesInvoiceItem!.SalesInvoice!.ShipmentReleaseKey != null)
            .Select(l => new { Key = l.SalesInvoiceItem!.SalesInvoice!.ShipmentReleaseKey!.Value, l.SalesInvoiceItem.SalesInvoice.ShipmentRelease!.Origin })
            .Distinct()
            .ToListAsync();

        return releases.Select(r => (r.Key, r.Origin)).ToList();
    }

    private void EnsureStandalone()
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(StorageDepositRules.StandaloneOnlyMessage);
    }
}
```

`PurchaseInvoicesCancelService.CancelAsync` — antes do `SaveChangesAsync`:
`await StorageReturnReconciliationsService.RemoveForInvoiceAsync(db.Context, invoice.Key);`
`PurchaseInvoicesDeleteService.ExecuteAsync` — junto dos `RemoveRange`: mesma chamada.

- [ ] **Step 4: Endpoints, EDM, DI**

Controllers no molde de `PurchaseInvoicesCreateCounterNoteController` (action) e de
`PurchaseInvoicesNfeReturnableItemsController` (function, `[HttpGet("odata/PurchaseInvoicesStorageReturnTransactions(Key={key})")]`).
A action de reconciliar lê `TransactionKeys` como `IEnumerable<Guid>` de `parameters["TransactionKeys"]`
(`(parameters["TransactionKeys"] as IEnumerable<Guid>)?.ToList()`; nulo → `BadRequest`).

EDM:

```csharp
        var purchaseInvoicesReconcileStorageReturn = modelBuilder.Action("PurchaseInvoicesReconcileStorageReturn");
        purchaseInvoicesReconcileStorageReturn.Parameter<Guid>("Key");
        purchaseInvoicesReconcileStorageReturn.CollectionParameter<Guid>("TransactionKeys");

        var purchaseInvoicesUndoStorageReturnReconciliation = modelBuilder.Action("PurchaseInvoicesUndoStorageReturnReconciliation");
        purchaseInvoicesUndoStorageReturnReconciliation.Parameter<Guid>("Key");
        purchaseInvoicesUndoStorageReturnReconciliation.Parameter<Guid>("TransactionKey");

        var purchaseInvoicesStorageReturnTransactions = modelBuilder.Function("PurchaseInvoicesStorageReturnTransactions");
        purchaseInvoicesStorageReturnTransactions.Parameter<Guid>("Key");
        purchaseInvoicesStorageReturnTransactions.ReturnsCollection<StorageReturnTransactionDto>();
```

DI: `services.AddScoped<StorageReturnReconciliationsService>();`

- [ ] **Step 5: Run tests and build**

Run: `dotnet build SiagroB1.sln`; `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoice|FullyQualifiedName~StorageDeposit|FullyQualifiedName~StorageReturn"`
Expected: 0 erros; PASS.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Domain SiagroB1.Application SiagroB1.Web SiagroB1.Application.Tests/StorageDeposits
git commit -m "feat(storage): reconciliar o retorno de depósito com os romaneios" -m "Cada retorno do armazém é conferido contra os carregamentos da liberação; um romaneio inteiro fica com no máximo um retorno, e o que não tem retorno aparece como pendente. Cancelar ou excluir o retorno solta os romaneios." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 9: Trava do romaneio reconciliado

**Files:**
- Modify: `StorageTransactionsCancelService.cs`, `StorageTransactionsReverseService.cs`, `ShippingTransactionsReverseService.cs`, `ShippingTransactionsChangeReleaseService.cs`
- Test: `SiagroB1.Application.Tests/StorageDeposits/ReconciledTransactionGuardTests.cs`

**Interfaces:**
- Consumes: `StorageDepositRules.EnsureTransactionsNotReconciledAsync` (Task 3).

`ShipmentLoadsCancelService` não cancela romaneio (só desvincula) e `WarehouseReconciliationsCancelService` cancela
apenas a Compra da Conferência, que nunca é candidata — nenhum dos dois precisa da trava.

- [ ] **Step 1: Write the failing tests**

Um teste por serviço, construindo-o como nos testes vizinhos (procure `new StorageTransactionsCancelService(`,
`new StorageTransactionsReverseService(`, `new ShippingTransactionsReverseService(`,
`new ShippingTransactionsChangeReleaseService(` em `SiagroB1.Application.Tests`). Semente comum:

```csharp
    private static async Task<(UnitOfWork Db, StorageTransaction Shipment)> ReconciledAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var ret = await seed.AddReturnAsync(await seed.AddRemittanceAsync(100_000m), 30_000m);
        var shipment = await seed.AddShipmentAsync(30_000m);
        db.Context.StorageReturnReconciliations.Add(new StorageReturnReconciliation
            { PurchaseInvoiceKey = ret.Key, StorageTransactionKey = shipment.Key });
        await db.SaveChangesAsync();
        return (db, shipment);
    }

    [Fact]
    public async Task Cancel_refuses_reconciled_transaction()
    {
        var (db, shipment) = await ReconciledAsync();

        var e = await Assert.ThrowsAsync<ApplicationException>(() => CancelService(db).ExecuteAsync(shipment.Key, "tester"));

        Assert.Contains("está reconciliado com o retorno de depósito", e.Message);
    }

    [Fact]
    public async Task Reverse_refuses_reconciled_transaction() { /* idem com StorageTransactionsReverseService */ }

    [Fact]
    public async Task Shipping_reverse_refuses_reconciled_purchase()
    {
        // Monte um ShippingTransaction cuja PurchaseStorageTransactionKey = shipment.Key (veja os testes de
        // ShippingTransactionsReverseService para o mínimo exigido) e chame o estorno.
    }

    [Fact]
    public async Task Release_change_refuses_reconciled_purchase()
    {
        // Mesmo, com ShippingTransactionsChangeReleaseService: a Compra original do plano reconciliada.
    }
```

Os dois últimos precisam do mesmo arranjo dos testes existentes desses serviços; reutilize o helper deles e só
acrescente a reconciliação da Compra. Esperado nos quatro: `ApplicationException` contendo
`"está reconciliado com o retorno de depósito"`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ReconciledTransactionGuardTests"`
Expected: FAIL (estornos passam).

- [ ] **Step 3: Implement**

Logo depois de cada `RoyaltyRules.EnsureNoActiveWriteOffAsync` existente:

- `StorageTransactionsCancelService` e `StorageTransactionsReverseService`:
  `await StorageDepositRules.EnsureTransactionsNotReconciledAsync(db.Context, [doc.Key]);`
- `ShippingTransactionsReverseService` (antes do `try`):
  ```csharp
  await StorageDepositRules.EnsureTransactionsNotReconciledAsync(db.Context,
      new[] { shipping.PurchaseStorageTransactionKey, shipping.SalesStorageTransactionKey }
          .Where(k => k != null).Select(k => k!.Value));
  ```
- `ShippingTransactionsChangeReleaseService` (no bloco "Validação completa antes de qualquer escrita"):
  ```csharp
  // A compra e a saída originais saem de cena na troca: reconciliadas, o retorno ficaria apontando o nada (D8).
  await StorageDepositRules.EnsureTransactionsNotReconciledAsync(db.Context,
      plans.SelectMany(p => new[] { p.Purchase?.Key, (Guid?)p.Sales.Key }).Where(k => k != null).Select(k => k!.Value));
  ```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageTransaction|FullyQualifiedName~ShippingTransaction|FullyQualifiedName~StorageDeposit"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application SiagroB1.Application.Tests/StorageDeposits
git commit -m "feat(storage): romaneio reconciliado não é estornado" -m "O romaneio coberto por um retorno de depósito só sai de cena depois de desfeita a reconciliação, inclusive pela troca de liberação, que estorna a compra sem passar pelo cancelamento comum." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 10: Resumo da remessa na liberação

**Files:**
- Create: `SiagroB1.Application/Services/StorageDeposits/ShipmentReleasesStorageRemittanceSummaryService.cs`
- Create: `SiagroB1.Web/Functions/StorageDeposits/ShipmentReleasesStorageRemittanceSummaryController.cs`
- Modify: `SiagroB1.Domain/Dtos/StorageDepositDtos.cs`, `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/StorageDeposits/StorageRemittanceSummaryServiceTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public class StorageRemittanceSummaryDto
  {
      public Guid? RemittanceKey { get; set; }
      public string? RemittanceNumber { get; set; }        // TaxDocumentNumber ?? InvoiceNumber
      public InvoiceStatus? RemittanceStatus { get; set; }
      public NfeStatus? RemittanceNfeStatus { get; set; }
      public decimal RemittedQuantity { get; set; }
      public decimal ReturnedQuantity { get; set; }
      public decimal PendingReturnQuantity { get; set; }
      public decimal ShippedWithoutReturnQuantity { get; set; }
      public List<StorageRemittanceReturnDto> Returns { get; set; } = [];
      public List<StorageRemittanceTransactionDto> Transactions { get; set; } = [];
  }
  public class StorageRemittanceReturnDto { public Guid PurchaseInvoiceKey { get; set; } public string? Number { get; set; } public DateTime? InvoiceDate { get; set; } public decimal Quantity { get; set; } public InvoiceStatus InvoiceStatus { get; set; } }
  public class StorageRemittanceTransactionDto { public Guid Key { get; set; } public string? Code { get; set; } public DateTime? TransactionDate { get; set; } public string? TruckCode { get; set; } public decimal NetWeight { get; set; } public Guid? ReturnPurchaseInvoiceKey { get; set; } public string? ReturnNumber { get; set; } }
  ```
  `ShipmentReleasesStorageRemittanceSummaryService.ExecuteAsync(Guid releaseKey) : Task<StorageRemittanceSummaryDto>`;
  function `ShipmentReleasesStorageRemittanceSummary(Key) → StorageRemittanceSummaryDto`.

(Confira o nome do campo de data do `PurchaseInvoice` — `InvoiceDate` ou equivalente — e use o real.)

- [ ] **Step 1: Write the failing tests**

```csharp
public class StorageRemittanceSummaryServiceTests
{
    private static ShipmentReleasesStorageRemittanceSummaryService Service(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, TestConfiguration.Erp(erp));

    [Fact]
    public async Task Without_remittance_everything_is_zero()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddShipmentAsync(10_000m);

        var summary = await Service(db).ExecuteAsync(seed.Release.Key);

        Assert.Null(summary.RemittanceKey);
        Assert.Equal(0m, summary.RemittedQuantity);
        Assert.Equal(10_000m, summary.ShippedWithoutReturnQuantity);
    }

    [Fact]
    public async Task Balances_follow_returns_and_reconciliations()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db, released: 100_000m);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var ret = await seed.AddReturnAsync(remittance, 29_500m, InvoiceStatus.Confirmed);
        await seed.AddReturnAsync(remittance, 50_000m, InvoiceStatus.Cancelled);
        var s1 = await seed.AddShipmentAsync(30_000m);
        await seed.AddShipmentAsync(20_000m);
        db.Context.StorageReturnReconciliations.Add(new StorageReturnReconciliation { PurchaseInvoiceKey = ret.Key, StorageTransactionKey = s1.Key });
        await db.SaveChangesAsync();

        var summary = await Service(db).ExecuteAsync(seed.Release.Key);

        Assert.Equal(remittance.Key, summary.RemittanceKey);
        Assert.Equal(100_000m, summary.RemittedQuantity);
        Assert.Equal(29_500m, summary.ReturnedQuantity);
        Assert.Equal(70_500m, summary.PendingReturnQuantity);
        Assert.Equal(20_000m, summary.ShippedWithoutReturnQuantity);
        Assert.Single(summary.Returns);
        Assert.Equal(2, summary.Transactions.Count);
        Assert.Equal(ret.Key, summary.Transactions.Single(t => t.Key == s1.Key).ReturnPurchaseInvoiceKey);
    }

    [Fact]
    public async Task Refuses_in_sap_mode()
    {
        var db = TestDb.CreateUnitOfWork();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db, "SAPB1").ExecuteAsync(Guid.NewGuid()));
        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageRemittanceSummaryServiceTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implement**

```csharp
/// <summary>Remetido / Retornado / A retornar / Expedido sem retorno da liberação (spec 2026-10-10 §2.5, §5.1).</summary>
public class ShipmentReleasesStorageRemittanceSummaryService(IUnitOfWork db, IConfiguration configuration)
{
    public async Task<StorageRemittanceSummaryDto> ExecuteAsync(Guid releaseKey)
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(StorageDepositRules.StandaloneOnlyMessage);

        var release = await db.Context.ShipmentReleases.AsNoTracking().FirstOrDefaultAsync(r => r.Key == releaseKey)
                      ?? throw new NotFoundException("Liberação não encontrada.");

        var summary = new StorageRemittanceSummaryDto();
        var remittance = await StorageDepositRules.FindActiveRemittanceAsync(db.Context, releaseKey);

        if (remittance != null)
        {
            var itemKey = remittance.Items.Single().Key!.Value;
            summary.RemittanceKey = remittance.Key;
            summary.RemittanceNumber = StorageDepositRules.DocumentNumber(remittance.TaxDocumentNumber, remittance.InvoiceNumber);
            summary.RemittanceStatus = remittance.InvoiceStatus;
            summary.RemittanceNfeStatus = remittance.NfeStatus;
            summary.RemittedQuantity = remittance.Items.Single().Quantity;
            summary.Returns = await StorageDepositRules.ReturnLinesQuery(db.Context, [itemKey], null)
                .AsNoTracking()
                .GroupBy(l => new { l.PurchaseInvoiceKey, l.PurchaseInvoice!.TaxDocumentNumber, l.PurchaseInvoice.InvoiceNumber, l.PurchaseInvoice.InvoiceDate, l.PurchaseInvoice.InvoiceStatus })
                .Select(g => new StorageRemittanceReturnDto
                {
                    PurchaseInvoiceKey = g.Key.PurchaseInvoiceKey!.Value,
                    Number = g.Key.TaxDocumentNumber ?? g.Key.InvoiceNumber,
                    InvoiceDate = g.Key.InvoiceDate, InvoiceStatus = g.Key.InvoiceStatus,
                    Quantity = g.Sum(l => l.Quantity),
                })
                .ToListAsync();
            summary.ReturnedQuantity = summary.Returns.Sum(r => r.Quantity);
            summary.PendingReturnQuantity = summary.RemittedQuantity - summary.ReturnedQuantity;
        }

        summary.Transactions = await StorageDepositRules.ShippedTransactions(db.Context, releaseKey, release.Origin)
            .AsNoTracking()
            .Select(t => new StorageRemittanceTransactionDto
            {
                Key = t.Key, Code = t.Code, TransactionDate = t.TransactionDate, TruckCode = t.TruckCode, NetWeight = t.NetWeight,
                ReturnPurchaseInvoiceKey = db.Context.StorageReturnReconciliations
                    .Where(r => r.StorageTransactionKey == t.Key).Select(r => (Guid?)r.PurchaseInvoiceKey).FirstOrDefault(),
                ReturnNumber = db.Context.StorageReturnReconciliations
                    .Where(r => r.StorageTransactionKey == t.Key)
                    .Select(r => r.PurchaseInvoice!.TaxDocumentNumber ?? r.PurchaseInvoice.InvoiceNumber).FirstOrDefault(),
            })
            .OrderBy(t => t.TransactionDate)
            .ToListAsync();
        summary.ShippedWithoutReturnQuantity = summary.Transactions.Where(t => t.ReturnPurchaseInvoiceKey == null).Sum(t => t.NetWeight);

        return summary;
    }
}
```

Function controller no molde de `PurchaseInvoicesNfeReturnableItemsController`, rota
`[HttpGet("odata/ShipmentReleasesStorageRemittanceSummary(Key={key})")]`, `DefaultException` → `BadRequest`.
EDM: `modelBuilder.Function("ShipmentReleasesStorageRemittanceSummary")` com `Parameter<Guid>("Key")` e
`Returns<StorageRemittanceSummaryDto>()`. DI do serviço.

Acrescentar ao `Queries_translate_to_sql_server` da Task 3 uma verificação da subconsulta dos romaneios: extraia a
projeção para `StorageDepositRules.TransactionsWithReturn(AppDbContext, Guid, ReleaseOrigin) : IQueryable<StorageRemittanceTransactionDto>`
e use-a aqui e no teste (`ToQueryString()` não vazio).

- [ ] **Step 4: Run tests and build**

Run: `dotnet build SiagroB1.sln`; `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageDeposit|FullyQualifiedName~StorageRemittance"`
Expected: 0 erros; PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Domain SiagroB1.Application SiagroB1.Web SiagroB1.Application.Tests/StorageDeposits
git commit -m "feat(storage): saldo da remessa para depósito na liberação" -m "A liberação passa a mostrar quanto foi remetido ao armazém, quanto voltou em nota, quanto falta retornar e quanto foi carregado sem nota de retorno." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 11: Importação do XML do retorno (`NFref` → remessa)

**Files:**
- Modify: `SupplierNfeXmlReader.cs` (record `SupplierNfe`), `PurchaseInvoicesImportXmlService.cs`, `SiagroB1.Domain/Dtos/PurchaseInvoiceDraftDto.cs`
- Test: `SiagroB1.Application.Tests/StorageDeposits/StorageReturnXmlImportTests.cs` (use `Support/SupplierNfeXml.cs` para montar o XML; se ele não aceitar `NFref`, acrescente um parâmetro opcional `referencedKeys`)

**Interfaces:**
- Produces: `SupplierNfe.ReferencedKeys : IReadOnlyList<string>`; `PurchaseInvoiceDraftItemDto.SalesInvoiceItemKey : Guid?`,
  `PurchaseInvoiceDraftItemDto.RemittanceNumber : string?`; `PurchaseInvoicesImportXmlService(IBusinessPartnerService, IUnitOfWork)`.

Regra (refina §4 da spec, porque o `cProd` do armazém quase nunca é o nosso código): remessas candidatas = remessas
ativas cujo `ChaveNFe` está no `NFref` e cujo `CardCode` é o emitente. Se houver exatamente UMA remessa candidata e
o XML tiver exatamente UM item → vincula e troca `ItemCode`/`ItemName`/`UnitOfMeasureCode` do rascunho pelos da
remessa. Senão, vincula só os itens cujo `ItemCode` coincide com o da linha de alguma remessa candidata. O resto
fica para o value help.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task Single_remittance_and_single_item_links_and_takes_our_item_code() { ... }
    // seed + remessa com ChaveNFe "3526...(44)"; XML do armazém (CNPJ do ARM001, cProd "GRAO-01", NFref = chave);
    // espera: draft.Items[0].SalesInvoiceItemKey == linha da remessa; ItemCode == "SOJA"; RemittanceNumber == "000000123".

    [Fact]
    public async Task Unknown_reference_leaves_the_draft_unlinked() { ... }
    // NFref com chave que não existe: SalesInvoiceItemKey nulo e ItemCode "GRAO-01" intacto.

    [Fact]
    public async Task Remittance_of_another_warehouse_is_not_linked() { ... }
    // remessa com a chave certa mas CardCode diferente do emitente: sem vínculo.

    [Fact]
    public async Task Cancelled_remittance_is_not_linked() { ... }
```

Escreva os corpos completos com `FakeBusinessPartnerService(taxIds: new() { [StorageDepositSeed.Warehouse] = "11222333000181" })`
e o XML montado pelo helper de suporte; grave `remittance.ChaveNFe` na semente antes de importar.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageReturnXmlImportTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implement**

`SupplierNfeXmlReader`:

```csharp
public sealed record SupplierNfe(XElement InfNfe, string AccessKey, IReadOnlyList<SupplierNfeItem> Items,
    IReadOnlyList<string> ReferencedKeys);
...
        var referenced = infNfe.Element(Nfe + "ide")?.Elements(Nfe + "NFref")
            .Select(r => r.Element(Nfe + "refNFe")?.Value)
            .Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k!.Trim()).ToList() ?? [];

        return new SupplierNfe(infNfe, accessKey, infNfe.Elements(Nfe + "det").Select(ReadItem).ToList(), referenced);
```

(Atualize os outros `new SupplierNfe(...)` que o compilador apontar.)

`PurchaseInvoiceDraftItemDto` — acrescentar:

```csharp
    /// <summary>Retorno de depósito: a linha da remessa que o NFref do XML referencia (spec 2026-10-10 §4).</summary>
    public Guid? SalesInvoiceItemKey { get; set; }

    /// <summary>Número da remessa vinculada, para a tela mostrar sem nova leitura.</summary>
    public string? RemittanceNumber { get; set; }
```

`PurchaseInvoicesImportXmlService` — construtor `(IBusinessPartnerService businessPartnerService, IUnitOfWork db)`;
o método passa a `async Task<PurchaseInvoiceDraftDto>` (remova o `Task.FromResult`) e, depois de montar os itens:

```csharp
        await LinkStorageRemittancesAsync(draft, nfe.ReferencedKeys);
...
    private async Task LinkStorageRemittancesAsync(PurchaseInvoiceDraftDto draft, IReadOnlyList<string> referencedKeys)
    {
        if (referencedKeys.Count == 0)
            return;

        var remittances = await StorageDepositRules.ActiveRemittances(db.Context).AsNoTracking()
            .Where(i => i.ChaveNFe != null && referencedKeys.Contains(i.ChaveNFe) && i.CardCode == draft.CardCode)
            .SelectMany(i => i.Items.Select(l => new
            {
                Key = l.Key!.Value, l.ItemCode, l.ItemName, l.UnitOfMeasureCode,
                Number = i.TaxDocumentNumber ?? i.InvoiceNumber, InvoiceKey = i.Key,
            }))
            .ToListAsync();

        if (remittances.Count == 0)
            return;

        // O armazém usa o próprio código de produto: com uma remessa e um item, a ligação é inequívoca e o rascunho
        // passa a usar o NOSSO produto.
        if (remittances.Select(r => r.InvoiceKey).Distinct().Count() == 1 && remittances.Count == 1 && draft.Items.Count == 1)
        {
            var only = remittances[0];
            var item = draft.Items[0];
            item.SalesInvoiceItemKey = only.Key;
            item.RemittanceNumber = only.Number;
            item.ItemCode = only.ItemCode;
            item.ItemName = only.ItemName;
            item.UnitOfMeasureCode = only.UnitOfMeasureCode;
            return;
        }

        foreach (var item in draft.Items)
        {
            var match = remittances.FirstOrDefault(r => r.ItemCode == item.ItemCode);
            if (match == null) continue;
            item.SalesInvoiceItemKey = match.Key;
            item.RemittanceNumber = match.Number;
        }
    }
```

Atualize as construções de `PurchaseInvoicesImportXmlService` nos testes existentes (passe `TestDb.CreateUnitOfWork()`)
e no controller, se ele não usa DI (`grep -rn "new PurchaseInvoicesImportXmlService(" .`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ImportXml|FullyQualifiedName~SupplierNfe|FullyQualifiedName~StorageReturnXmlImportTests"`
Expected: PASS.

- [ ] **Step 5: Full backend gate**

Run: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests`
Expected: 0 erros; tudo verde. Anote o total de testes.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Domain SiagroB1.Application SiagroB1.Web SiagroB1.Application.Tests
git commit -m "feat(invoice): vincular o retorno de depósito pela chave da remessa no XML" -m "A NF-e de retorno do armazém referencia a remessa no NFref; a importação acha a remessa e preenche o vínculo. Como o armazém usa o próprio código de produto, com uma remessa e um item o rascunho passa a usar o nosso produto." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 12: Frontend — branch, rotas, helpers e papel na natureza

**Files:**
- Create: `webapp/helpers/StorageDepositHelpers.ts`, `webapp/test/unit/helpers/StorageDepositHelpers.qunit.ts`
- Modify: `webapp/test/unit/unitTests.qunit.ts`, `webapp/model/ServerRoutes.ts`, `webapp/view/usages/fragments/Form.fragment.xml`, `webapp/controller/usages/Add.controller.ts`

**Interfaces:**
- Produces (`siagrob1/helpers/StorageDepositHelpers`):
  ```ts
  export type StorageRemittanceSummary = {
    RemittanceKey: string | null; RemittanceNumber: string | null; RemittanceStatus: string | null;
    RemittanceNfeStatus: string | null; RemittedQuantity: number; ReturnedQuantity: number;
    PendingReturnQuantity: number; ShippedWithoutReturnQuantity: number;
    Returns: { PurchaseInvoiceKey: string; Number: string | null; InvoiceDate: string | null; Quantity: number; InvoiceStatus: string }[];
    Transactions: { Key: string; Code: string | null; TransactionDate: string | null; TruckCode: string | null; NetWeight: number; ReturnPurchaseInvoiceKey: string | null; ReturnNumber: string | null }[];
  };
  export function canIssueRemittance(releaseStatus: string | undefined, summary: StorageRemittanceSummary | undefined, isStandalone: boolean): boolean;
  export function reconciliationDifference(invoiceQuantity: number, selectedWeights: number[]): { selected: number; difference: number };
  export function isRemittancePriceValid(value: unknown): boolean;
  ```
  `ServerRoutes`: `shipmentReleasesCreateStorageRemittance: '/odata/ShipmentReleasesCreateStorageRemittance'`,
  `shipmentReleasesStorageRemittanceSummary: '/odata/ShipmentReleasesStorageRemittanceSummary'`,
  `purchaseInvoicesStorageReturnTransactions: '/odata/PurchaseInvoicesStorageReturnTransactions'`,
  `purchaseInvoicesReconcileStorageReturn: '/odata/PurchaseInvoicesReconcileStorageReturn'`,
  `purchaseInvoicesUndoStorageReturnReconciliation: '/odata/PurchaseInvoicesUndoStorageReturnReconciliation'`.

- [ ] **Step 1: Create the frontend branch**

```bash
cd ../siagro-b1-frontend && git switch main && git status --short && git switch -c feature/storage-deposit-remittance
```

- [ ] **Step 2: Write the failing QUnit tests**

```ts
import { canIssueRemittance, reconciliationDifference, isRemittancePriceValid, StorageRemittanceSummary } from "siagrob1/helpers/StorageDepositHelpers";

const empty: StorageRemittanceSummary = {
  RemittanceKey: null, RemittanceNumber: null, RemittanceStatus: null, RemittanceNfeStatus: null,
  RemittedQuantity: 0, ReturnedQuantity: 0, PendingReturnQuantity: 0, ShippedWithoutReturnQuantity: 0,
  Returns: [], Transactions: [],
};

QUnit.module("StorageDepositHelpers");

QUnit.test("canIssueRemittance: liberação viva sem remessa ativa em STANDALONE", (assert) => {
  assert.true(canIssueRemittance("Actived", empty, true));
  assert.true(canIssueRemittance("Pending", empty, true));
  assert.false(canIssueRemittance("Cancelled", empty, true), "cancelada");
  assert.false(canIssueRemittance("Actived", { ...empty, RemittanceKey: "k" }, true), "já tem remessa");
  assert.false(canIssueRemittance("Actived", empty, false), "SAPB1");
  assert.false(canIssueRemittance("Actived", undefined, true), "resumo não carregado");
});

QUnit.test("reconciliationDifference: nota menos soma dos romaneios", (assert) => {
  assert.deepEqual(reconciliationDifference(30000, [15000, 14500]), { selected: 29500, difference: 500 });
  assert.deepEqual(reconciliationDifference(30000, []), { selected: 0, difference: 30000 });
});

QUnit.test("isRemittancePriceValid: número maior que zero", (assert) => {
  assert.true(isRemittancePriceValid(2.5));
  assert.true(isRemittancePriceValid("2,5"));
  assert.false(isRemittancePriceValid(0));
  assert.false(isRemittancePriceValid(""));
  assert.false(isRemittancePriceValid("abc"));
});
```

Registrar em `unitTests.qunit.ts`: `import "./helpers/StorageDepositHelpers.qunit";`

- [ ] **Step 3: Run tests to verify they fail**

Com `npx ui5 serve --port 8080` de pé (em background), rode o runner da seção Global Constraints.
Expected: falha de carga do módulo `StorageDepositHelpers`.

- [ ] **Step 4: Implement**

```ts
/** Regras puras da remessa para depósito (spec backend 2026-10-10 §5). */

export type StorageRemittanceSummary = { /* conforme Interfaces */ };

export function canIssueRemittance(
  releaseStatus: string | undefined, summary: StorageRemittanceSummary | undefined, isStandalone: boolean): boolean {
  return isStandalone && !!summary && !summary.RemittanceKey && !!releaseStatus && releaseStatus !== "Cancelled";
}

export function reconciliationDifference(invoiceQuantity: number, selectedWeights: number[]): { selected: number; difference: number } {
  const selected = selectedWeights.reduce((sum, w) => sum + w, 0);
  return { selected, difference: invoiceQuantity - selected };
}

/** Aceita número ou texto pt-BR ("2,5"); o servidor recusa ≤ 0 de novo. */
export function isRemittancePriceValid(value: unknown): boolean {
  const n = typeof value === "number" ? value : Number(String(value ?? "").replace(/\./g, "").replace(",", "."));
  return String(value ?? "").trim() !== "" && Number.isFinite(n) && n > 0;
}
```

`ServerRoutes.ts`: as cinco rotas de Interfaces, perto de `purchaseInvoicesCreateCounterNote`.

`usages/fragments/Form.fragment.xml` — logo depois do bloco "Permite contra-nota":

```xml
          <!-- Remessa para depósito em armazém de terceiro (spec backend 2026-10-10 §2.1): só STANDALONE. Remessa é
               natureza de saída e Retorno de entrada; o servidor recusa a combinação errada. -->
          <Label text="Papel no depósito" visible="{ui>/identityEditable}" />
          <Select
            visible="{ui>/identityEditable}"
            selectedKey="{ path: 'StorageDepositRole', targetType: 'any' }"
            forceSelection="false">
            <core:ListItem key="None" text="Nenhum" />
            <core:ListItem key="Remittance" text="Remessa para depósito" />
            <core:ListItem key="Return" text="Retorno de depósito" />
          </Select>
```

`usages/Add.controller.ts` — no objeto inicial, junto de `AllowsCounterNote: false,`: `StorageDepositRole: "None",`.

- [ ] **Step 5: Run gates**

Run: runner QUnit (PASS nos 3 testes novos + os existentes), `yarn ts-typecheck`, `yarn lint`.

- [ ] **Step 6: Commit (frontend)**

```bash
git add webapp
git commit -m "feat(master-data): papel no depósito na natureza e regras da remessa" -m "A natureza ganha o papel de remessa ou retorno de depósito, e as regras puras da tela (quando emitir remessa, diferença da reconciliação, preço) ficam testadas à parte." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 13: Frontend — "Emitir remessa" e saldos na liberação

**Files:**
- Create: `webapp/view/shipmentReleases/fragments/StorageRemittance.fragment.xml`, `webapp/view/shipmentReleases/fragments/StorageRemittanceDialog.fragment.xml`
- Modify: `webapp/controller/shipmentReleases/Detail.controller.ts`, `webapp/view/shipmentReleases/Detail.view.xml`

**Interfaces:**
- Consumes: helpers e rotas da Task 12; function/action das Tasks 5 e 10.
- Produces: modelo JSON `remittance` na view (`/summary`, `/canIssue`, `/visible`, `/dialog/{usageCode,usageName,unitPrice}`).

- [ ] **Step 1: Controller**

Em `Detail.controller.ts`:

```ts
import { sendJson, odataValue } from "siagrob1/helpers/FetchHelpers";
import ServerRoutes from "siagrob1/model/ServerRoutes";
import { canIssueRemittance, isRemittancePriceValid, StorageRemittanceSummary } from "siagrob1/helpers/StorageDepositHelpers";
import DialogHelper from "siagrob1/dialogs/DialogHelper";
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import Fragment from "sap/ui/core/Fragment";
...
  private remittanceDialog?: Dialog;

  onInit(): void {
    this.getView().setModel(new JSONModel({ visible: false, canIssue: false, summary: undefined, dialog: {} }), "remittance");
    // ... rota como hoje
  }

  private detailRouteMatched(ev: Route$MatchedEvent) {
    // ... como hoje, e ao fim:
    void this.loadRemittance(id);
  }

  /** Só STANDALONE: em SAPB1 a seção e o botão ficam ocultos e nada é pedido ao servidor. */
  private async loadRemittance(id: string): Promise<void> {
    const model = this.getView().getModel("remittance") as JSONModel;
    const isStandalone = (await this.getSystemInfo())?.erp === "STANDALONE";
    model.setProperty("/visible", isStandalone);
    if (!isStandalone) return;

    const result = await sendJson("GET", `${ServerRoutes.shipmentReleasesStorageRemittanceSummary}(Key=${id})`);
    const summary = result.ok ? (result.data as StorageRemittanceSummary) : undefined;
    const status = await (this.getView().getBindingContext() as Context | undefined)?.requestProperty("Status") as string | undefined;
    model.setProperty("/summary", summary);
    model.setProperty("/canIssue", canIssueRemittance(status, summary, true));
  }

  async onOpenRemittanceDialog(): Promise<void> {
    const model = this.getView().getModel("remittance") as JSONModel;
    model.setProperty("/dialog", { usageCode: null, usageName: "", unitPrice: "" });
    this.remittanceDialog ??= await Fragment.load({
      id: this.getView().getId(), name: "siagrob1.view.shipmentReleases.fragments.StorageRemittanceDialog", controller: this,
    }) as Dialog;
    this.getView().addDependent(this.remittanceDialog);
    this.remittanceDialog.open();
  }

  async onRemittanceUsageValueHelp(): Promise<void> {
    const selected = await DialogHelper.openTableSelectDialog(
      this, "UsagesSelectDialog", ["Name", "Description"],
      [new Filter("Inactive", FilterOperator.EQ, false)], undefined,
      "StorageDepositRole eq 'Remittance' and Direction eq 'Outgoing'");
    if (!selected) return;
    const model = this.getView().getModel("remittance") as JSONModel;
    model.setProperty("/dialog/usageCode", selected.getProperty("Code"));
    model.setProperty("/dialog/usageName", selected.getProperty("Name"));
  }

  async onConfirmRemittance(): Promise<void> {
    const model = this.getView().getModel("remittance") as JSONModel;
    const dialog = model.getProperty("/dialog") as { usageCode: number | null; unitPrice: string | number };
    if (!dialog.usageCode) { MessageBox.warning("Escolha a natureza de remessa para depósito."); return; }
    if (!isRemittancePriceValid(dialog.unitPrice)) { MessageBox.warning("Informe o preço unitário da remessa."); return; }

    const key = (this.getView().getBindingContext() as Context).getProperty("Key") as string;
    const price = typeof dialog.unitPrice === "number" ? dialog.unitPrice : Number(String(dialog.unitPrice).replace(/\./g, "").replace(",", "."));

    this.setBusy(true);
    try {
      const result = await sendJson("POST", ServerRoutes.shipmentReleasesCreateStorageRemittance,
        { ShipmentReleaseKey: key, UsageCode: dialog.usageCode, UnitPrice: price });
      if (!result.ok) { MessageBox.error(result.message); return; }
      this.remittanceDialog?.close();
      MessageToast.show("Remessa criada. Confira transporte e pesos, confirme e emita a NF-e.", { closeOnBrowserNavigation: false });
      this.navTo("salesInvoicesDetail", { id: odataValue<string>(result.data) as string });
    } finally {
      this.setBusy(false);
    }
  }

  onCancelRemittance(): void { this.remittanceDialog?.close(); }

  onOpenRemittance(): void {
    const key = (this.getView().getModel("remittance") as JSONModel).getProperty("/summary/RemittanceKey") as string;
    if (key) this.navTo("salesInvoicesDetail", { id: key });
  }

  onOpenReturn(ev: Event): void { /* navTo("purchaseInvoicesDetail", { id: PurchaseInvoiceKey da linha do modelo remittance }) */ }
```

(Confirme no `BaseController` raiz o tipo de retorno de `getSystemInfo()`, a existência de `setBusy`, e como
`odataValue` extrai o valor de um `Edm.Guid` retornado — veja o uso em `purchaseInvoices/Detail.controller.ts`.
`onOpenReturn`: pegue o contexto do item pressionado (`(ev.getSource() as Control).getBindingContext("remittance")`)
e navegue com `PurchaseInvoiceKey`; escreva o corpo completo.)

- [ ] **Step 2: Fragments and view**

`StorageRemittanceDialog.fragment.xml`:

```xml
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core" xmlns:f="sap.ui.layout.form">
  <Dialog title="Emitir remessa para depósito" contentWidth="32rem">
    <f:Form editable="true">
      <f:layout><f:ColumnLayout columnsM="1" columnsL="1" columnsXL="1" /></f:layout>
      <f:formContainers>
        <f:FormContainer>
          <f:formElements>
            <f:FormElement label="Armazém"><f:fields><Text text="{DeliveryLocationCode} - {DeliveryLocationName}" /></f:fields></f:FormElement>
            <f:FormElement label="Natureza">
              <f:fields>
                <Input value="{remittance>/dialog/usageName}" required="true" showValueHelp="true" valueHelpOnly="true"
                       valueHelpRequest=".onRemittanceUsageValueHelp" />
              </f:fields>
            </f:FormElement>
            <f:FormElement label="Quantidade">
              <f:fields>
                <Text text="{ path: 'ReleasedQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }" />
              </f:fields>
            </f:FormElement>
            <f:FormElement label="Preço unitário">
              <f:fields><Input value="{remittance>/dialog/unitPrice}" required="true" /></f:fields>
            </f:FormElement>
          </f:formElements>
        </f:FormContainer>
      </f:formContainers>
    </f:Form>
    <beginButton><Button text="Emitir" type="Emphasized" press=".onConfirmRemittance" /></beginButton>
    <endButton><Button text="Cancelar" press=".onCancelRemittance" /></endButton>
  </Dialog>
</core:FragmentDefinition>
```

⚠️ Não use `{A} - {B}` composto num `Text` ligado ao OData v4 (estoura com `sap.ui.model.odata.type.Raw`): troque o
"Armazém" por dois `Text` (`{DeliveryLocationCode}` e `{DeliveryLocationName}`) dentro de um `HBox`.
⚠️ Comentário XML não pode conter `--`.

`StorageRemittance.fragment.xml`: um `VBox` com
- `ObjectStatus`/`Link` "Remessa nº {remittance>/summary/RemittanceNumber}" (press `.onOpenRemittance`, visível com `RemittanceKey`), e o texto "Sem remessa" quando não há;
- quatro `ObjectNumber` (Remetido, Retornado, A retornar, Expedido sem retorno) ligados a `remittance>/summary/...` com o mesmo `formatOptions` de 3 casas do cabeçalho;
- `sap.m.Table` "Retornos" (`items="{remittance>/summary/Returns}"`: Número com `Link` → `.onOpenReturn`, Data via `.formatter.formatDate`, Quantidade);
- `sap.m.Table` "Romaneios expedidos" (`items="{remittance>/summary/Transactions}"`: Código, Data, Placa, Peso Líquido, Retorno = `{= ${remittance>ReturnNumber} || 'Sem retorno' }`).

`Detail.view.xml`:
- em `uxap:actions`, antes do "Voltar": `<Button text="Emitir remessa" type="Emphasized" visible="{remittance>/canIssue}" press=".onOpenRemittanceDialog"/>`;
- nova seção depois de "Romaneios": `<uxap:ObjectPageSection titleUppercase="false" title="Remessa para depósito" visible="{remittance>/visible}">` com o fragmento.

- [ ] **Step 3: Gates**

Run: `yarn ts-typecheck`, `yarn lint`, runner QUnit.
Expected: sem erros.

- [ ] **Step 4: Commit (frontend)**

```bash
git add webapp
git commit -m "feat(storage): emitir remessa e ver o saldo na liberação" -m "A liberação ganha o botão Emitir remessa e a seção com remetido, retornado, a retornar e expedido sem retorno, mais os retornos e os romaneios. Só no modo STANDALONE." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 14: Frontend — retorno no Documento de Entrada

**Files:**
- Create: `webapp/dialogs/fragments/StorageRemittanceItemsSelectDialog.fragment.xml`, `webapp/view/purchaseInvoices/fragments/StorageReturnTransactions.fragment.xml`, `webapp/view/purchaseInvoices/fragments/StorageReturnReconcileDialog.fragment.xml`
- Modify: `webapp/view/purchaseInvoices/fragments/Items.fragment.xml`, `webapp/controller/purchaseInvoices/BaseController.ts`, `webapp/controller/purchaseInvoices/Detail.controller.ts`, `webapp/controller/purchaseInvoices/Add.controller.ts`, `webapp/view/purchaseInvoices/Detail.view.xml`

**Interfaces:**
- Consumes: rotas e `reconciliationDifference` (Task 12); action/function da Task 8; `SalesInvoiceItemKey`/`RemittanceNumber` do rascunho (Task 11).
- Produces: `ui>/storageDepositMode` (true em STANDALONE); modelo JSON `storageReturn` (`/rows`, `/visible`, `/canReconcile`, `/dialog`).

- [ ] **Step 1: Coluna "Remessa" e value help**

`StorageRemittanceItemsSelectDialog.fragment.xml` (molde de `PurchaseInvoiceOriginItemsSelectDialog`):

```xml
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core">
<!-- Linhas de remessa para depósito ativas. O armazém (CardCode) muda a cada documento e entra pelo controller; o
     saldo a retornar é conferido pelo servidor ao salvar. -->
<TableSelectDialog class="sapUiSizeCompact" growing="true" growingThreshold="20"
  items="{ path: '/SalesInvoicesItems', parameters: {
    $filter: 'SalesInvoice/ShipmentReleaseKey ne null and SalesInvoice/InvoiceStatus ne \'Cancelled\'',
    $expand: 'SalesInvoice' } }"
  rememberSelections="true" title="Remessa para depósito">
  <columns>
    <Column><header><Text text="Documento" /></header></Column>
    <Column><header><Text text="NF-e" /></header></Column>
    <Column><header><Text text="Produto" /></header></Column>
    <Column hAlign="End"><header><Text text="Remetido" /></header></Column>
  </columns>
  <ColumnListItem>
    <cells>
      <Text text="{SalesInvoice/InvoiceNumber}" />
      <Text text="{SalesInvoice/TaxDocumentNumber}" />
      <Text text="{ItemName}" />
      <ObjectNumber number="{ path: 'Quantity', type: 'sap.ui.model.odata.type.Decimal', formatOptions: { maxFractionDigits: 3 } }" unit="{UnitOfMeasureCode}" />
    </cells>
  </ColumnListItem>
</TableSelectDialog>
</core:FragmentDefinition>
```

`Items.fragment.xml` — nova coluna logo depois da "NF de Origem":

```xml
      <!-- Retorno de depósito (spec backend 2026-10-10 §5.3): a linha aponta a linha da remessa. OneWay pelo mesmo
           motivo da NF de Origem: o campo exibe uma NAVEGAÇÃO e quem grava é o SalesInvoiceItemKey. -->
      <t:Column label="Remessa" width="10rem"
                visible="{= ${ui>/storageDepositMode} === true &amp;&amp; ${path: 'InvoiceType', targetType: 'any'} !== 'Return' }">
        <t:template>
          <Input
            editable="{= ${ui>/editable} === true &amp;&amp; ${ui>/isCounterNote} !== true }"
            value="{path: 'SalesInvoiceItem/SalesInvoice/TaxDocumentNumber', mode: 'OneWay'}"
            showValueHelp="{= ${ui>/editable} === true &amp;&amp; ${ui>/isCounterNote} !== true }"
            valueHelpOnly="true"
            valueHelpRequest=".openRemittanceItemValueHelp" />
        </t:template>
      </t:Column>
```

`purchaseInvoices/BaseController.ts`:
- onde `ui>/identityEditable`/`isCounterNote` são inicializados (linhas ~50–110), definir
  `uiModel.setProperty("/storageDepositMode", (await this.getSystemInfo())?.erp === "STANDALONE");`
- método novo, no molde de `openOriginItemValueHelp`:

```ts
  /** Value help da REMESSA PARA DEPÓSITO da linha de retorno: remessas ativas do armazém emitente do documento. */
  async openRemittanceItemValueHelp(ev: Input$ValueHelpRequestEvent) {
    const oInput = ev.getSource();
    const oTarget = oInput.getBindingContext() as Context;
    const cardCode = (this.getView().getBindingContext() as Context)?.getProperty("CardCode") as string;

    if (!cardCode) {
      MessageBox.warning("Informe o emitente (o armazém) antes de indicar a remessa.");
      return;
    }

    const oSelected = await DialogHelper.openTableSelectDialog(
      this, "StorageRemittanceItemsSelectDialog", ["SalesInvoice/InvoiceNumber", "SalesInvoice/TaxDocumentNumber", "ItemName"],
      [new Filter("SalesInvoice/CardCode", FilterOperator.EQ, cardCode)]);

    if (!oSelected) {
      return;
    }

    oInput.setValue(oSelected.getProperty("SalesInvoice/TaxDocumentNumber") as string);
    await oTarget.setProperty("SalesInvoiceItemKey", oSelected.getProperty("Key"));
  }
```

`Add.controller.ts` — onde os itens do rascunho do XML viram linhas do POST, copiar também
`SalesInvoiceItemKey: draftItem.SalesInvoiceItemKey ?? null` (procure o mapeamento de `NfeItemNumber` e acrescente
ao lado; o `RemittanceNumber` só serve para exibir, não vai no POST). Se o `create()` da linha declara todas as
propriedades editáveis, declare `SalesInvoiceItemKey` também.

- [ ] **Step 2: Reconciliação no Detail**

`Detail.controller.ts`:

```ts
  /** Romaneios do retorno de depósito: candidatos e já reconciliados (spec backend 2026-10-10 §3.5). */
  private async loadStorageReturn(ctx: Context): Promise<void> {
    const model = this.getView().getModel("storageReturn") as JSONModel;
    const ui = this.getView().getModel("ui") as JSONModel;
    const isReturn = ui.getProperty("/storageDepositMode") === true
      && (await this.hasRemittanceLine(ctx));
    model.setProperty("/visible", isReturn);
    if (!isReturn) return;

    const result = await sendJson("GET", `${ServerRoutes.purchaseInvoicesStorageReturnTransactions}(Key=${ctx.getProperty("Key") as string})`);
    const rows = result.ok ? (odataValue<StorageReturnRow[]>(result.data) ?? []) : [];
    model.setProperty("/rows", rows);
    model.setProperty("/reconciled", rows.filter((r) => r.IsReconciled));
    model.setProperty("/canReconcile", ctx.getProperty("InvoiceStatus") !== "Cancelled" && rows.some((r) => !r.IsReconciled));
  }
```

- `hasRemittanceLine`: lê as linhas (`requestContexts` de `Items` com `$select: "SalesInvoiceItemKey"`, como o
  `readAllUsagesAllowCounterNote` faz) e devolve `true` se alguma tiver `SalesInvoiceItemKey` e o documento for
  `InvoiceType !== 'Return'`.
- `type StorageReturnRow = { Key: string; Code: string; TransactionDate: string; TruckCode: string; NetWeight: number; UnitOfMeasureCode: string; IsReconciled: boolean }`.
- Registrar o modelo `storageReturn` no `onInit` e chamar `loadStorageReturn` onde o Detail recalcula o estado
  (perto de `canCreateCounterNote`).
- `onOpenReconcileDialog`: abre `StorageReturnReconcileDialog` com uma `sap.m.Table` (`mode="MultiSelect"`) sobre
  `storageReturn>/rows` filtrado a `IsReconciled === false` (`/candidates`), e um rodapé que mostra
  `reconciliationDifference(<quantidade das linhas de retorno>, <pesos selecionados>)` atualizado no
  `selectionChange`: "Nota: X · Romaneios: Y · Diferença: Z" (3 casas, pt-BR).
- `onConfirmReconcile`: `sendJson("POST", ServerRoutes.purchaseInvoicesReconcileStorageReturn, { Key, TransactionKeys })`;
  erro → `MessageBox.error(result.message)`; sucesso → fecha, `MessageToast.show("Romaneios reconciliados.")`, recarrega.
- `onUndoReconciliation` (botão por linha da seção): `confirmDialog("Desfazer a reconciliação do romaneio X ?")`, depois
  `sendJson("POST", ServerRoutes.purchaseInvoicesUndoStorageReturnReconciliation, { Key, TransactionKey })` e recarrega.

`Detail.view.xml`:
- botão no cabeçalho junto do "Gerar contra-nota": `<Button text="Reconciliar romaneios" visible="{storageReturn>/canReconcile}" press=".onOpenReconcileDialog" />`;
- seção `<uxap:ObjectPageSection titleUppercase="false" title="Romaneios reconciliados" visible="{storageReturn>/visible}">`
  com `StorageReturnTransactions.fragment.xml` (tabela `storageReturn>/reconciled`: Código, Data, Placa, Peso Líquido,
  botão "Desfazer" com `press=".onUndoReconciliation"`).

Escreva os corpos completos dos métodos acima; siga o estilo de `onCreateCounterNote` (busy, confirm, toast).

- [ ] **Step 3: Gates**

Run: `yarn ts-typecheck`, `yarn lint`, runner QUnit.
Expected: sem erros.

- [ ] **Step 4: Commit (frontend)**

```bash
git add webapp
git commit -m "feat(invoice): indicar a remessa e reconciliar romaneios no retorno" -m "No documento de entrada do armazém, cada linha de retorno indica a remessa (vem preenchida da importação do XML) e o documento reconcilia os romaneios expedidos, mostrando a diferença entre a nota e os pesos." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 15: Frontend — liberação no Documento de Saída

**Files:**
- Modify: `webapp/view/salesInvoices/Detail.view.xml`, `webapp/controller/salesInvoices/Detail.controller.ts`

- [ ] **Step 1: Implement**

`Detail.view.xml` — no `HBox` do `headerContent`, depois dos `ObjectStatus`:

```xml
        <!-- Remessa para depósito (spec backend 2026-10-10 §5.2): volta para a liberação de origem. -->
        <Link class="sapUiSmallMarginBegin" text="Liberação da remessa para depósito"
              visible="{= !!${ShipmentReleaseKey} }" press=".onOpenShipmentRelease" />
```

`Detail.controller.ts`:

```ts
  onOpenShipmentRelease(): void {
    const key = (this.getView().getBindingContext() as Context)?.getProperty("ShipmentReleaseKey") as string;
    if (key) this.navTo("shipmentReleasesDetail", { id: key });
  }
```

(Se o binding do Detail usa `$select`, acrescente `ShipmentReleaseKey`.)

- [ ] **Step 2: Gates**

Run: `yarn ts-typecheck`, `yarn lint`.

- [ ] **Step 3: Commit (frontend)**

```bash
git add webapp
git commit -m "feat(invoice): link da remessa para a liberação" -m "O documento de saída que é remessa para depósito mostra o caminho de volta para a liberação." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn"
```

---

## Task 16: Verificação end-to-end (CEAGUI, STANDALONE, homologação)

Seguir `superpowers:verification-before-completion`. Ambiente: profile **`ceagui`** (Web, Gateway, Reports;
`ASPNETCORE_ENVIRONMENT=Ceagui-Development`, banco `CEAGUI_SIAGRO_DEV`), frontend com `yarn start:dev`, usuário
`admin`/`1234`. A migration `AddStorageDepositRemittance` precisa estar aplicada ANTES de subir o Web (ele recusa
subir com migration pendente):

```bash
cd siagro-b1-backend
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

(Só no `CEAGUI_SIAGRO_DEV`. Não aplicar em `IDX_SIAGRO_DEV` nem em homologação/produção.)

- [ ] **Step 1:** Naturezas: cadastrar "REMESSA P/ DEPOSITO" (Saída, 5905/6905, papel Remessa, sem efeito, sem
  financeiro) e "RETORNO DE DEPOSITO" (Entrada, 1906/2906, papel Retorno). Conferir que trocar o tipo com o papel
  errado é recusado.
- [ ] **Step 2:** Pela home → Liberações → uma liberação de contrato aprovado num armazém de terceiro com parceiro
  cadastrado: seção "Remessa para depósito" zerada; "Emitir remessa" → natureza + preço → abre o Documento de Saída
  com destinatário = armazém e quantidade = liberada. Preencher transporte/pesos, confirmar, **emitir em
  homologação**. Esperado: NF-e Autorizada (CFOP 5905 ou 6905). Se a SEFAZ rejeitar, registrar código e motivo e
  parar para decidir com o usuário.
- [ ] **Step 3:** Expedir dois romaneios dessa liberação (Expedição de Grãos). A seção mostra "Expedido sem retorno"
  = soma dos dois.
- [ ] **Step 4:** Montar o XML do retorno do armazém (modelo: `C:\Projetos\SiagroB1\nfe-homologacao-testes\fornecedor-ftestepj-nfe-789.xml`,
  emitente = CNPJ do armazém, um `det` com `cProd` próprio do armazém, CFOP 5907, `ide/NFref/refNFe` = chave da
  remessa). Importar no Documento de Entrada: a coluna "Remessa" vem preenchida e o produto vira o nosso. Salvar.
- [ ] **Step 5:** "Reconciliar romaneios" → marcar os dois → diferença exibida → confirmar. Na liberação:
  Retornado = quantidade da nota, A retornar = remetido − retornado, Expedido sem retorno = 0.
- [ ] **Step 6:** Guardas pela tela: cancelar a remessa (recusado: tem retorno); estornar um dos romaneios
  reconciliados (recusado); cancelar a liberação (recusado: remessa ativa); desfazer a reconciliação de um romaneio
  e estorná-lo (permitido).
- [ ] **Step 7:** Subir com um profile SAPB1 que esteja com o banco em dia (perguntar ao usuário qual, se o `yktb`
  estiver com migrations pendentes) e conferir que botão, seção, coluna "Remessa" e campo "Papel no depósito" não
  aparecem. Se nenhum ambiente SAPB1 estiver em dia, registrar o passo como não executado.
- [ ] **Step 8:** Derrubar a stack por PID (portas 50000, 5246, 58000/8081, 8080), conferir as portas livres de novo
  no fim.
- [ ] **Step 9:** Revisão final do branch inteiro (`superpowers:requesting-code-review`) nos dois repos; corrigir o
  que for confirmado em commits próprios.
