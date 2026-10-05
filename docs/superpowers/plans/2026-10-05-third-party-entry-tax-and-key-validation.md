# Documento de Entrada de terceiro: natureza, cálculo e validação da chave — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Na filial que emite NF-e pelo Siagro, a entrada de terceiro Normal ganha natureza e cálculo de tributos como a própria; o documento eletrônico exige uma chave coerente e, com a filial em Produção, só confirma com a NF-e do fornecedor autorizada na SEFAZ.

**Architecture:** Backend: `PurchaseInvoicesTaxApplyService` passa a calcular o terceiro Normal; a cópia da tributação do XML vira só a conferência do `nItem`; um guard estático (`SupplierNfeKeyGuard`) confere a chave localmente ao salvar e ao confirmar; um serviço novo (`SupplierNfeAuthorizationService`) consulta a SEFAZ (consSitNFe) no confirmar quando a filial está em Produção, roteando pela UF da chave. Frontend: uma regra nova "calcula tributos" separa natureza/CFOP/tributos do "modo NF-e" (emissão), e o formulário ganha "Tipo de documento".

**Tech Stack:** .NET 10, EF Core (SQL Server; testes com InMemory), OData v4, xUnit; OpenUI5 1.141 TypeScript, QUnit.

**Spec:** `docs/superpowers/specs/2026-10-05-third-party-entry-tax-and-key-validation-design.md` (commit 252f0d9).

## Global Constraints

- Repos e branch: backend `C:\Projetos\SiagroB1\siagro-b1-backend` e frontend `C:\Projetos\SiagroB1\siagro-b1-frontend`, ambos no branch `feature/third-party-entry-tax-and-key` (o do frontend nasce de `main` na Task 5). Conferir o branch antes de cada commit.
- **Regra ativa** = `Erp == "STANDALONE"` **e** `Branch.IssuesNfe`, sempre perguntada ao `TaxCalculationGate.IsActiveAsync(branchCode)` (ou `taxApply.IsBranchActiveAsync`). Sem ela (Yokotobi/SAPB1, MH Agro): comportamento idêntico ao de hoje.
- **Terceiro Normal** = `IssuerType == ThirdParty` **e** `InvoiceType == Normal`. "De terceiro + Devolução" é a devolução DO CLIENTE e fica exatamente como está (sem natureza, sem cálculo, sem chave).
- **Documento eletrônico** = `TaxDocumentKind == Nfe` (enum `TaxDocumentKind { Nfe = 0, Other = 1 }`). Emissão própria é sempre `Nfe`.
- Mensagens ao usuário em pt-BR, exatamente como escritas neste plano; identificadores, tabelas e colunas em inglês; comentários em pt-BR.
- Migration única `AddPurchaseInvoiceTaxDocumentKind` (`AppDbContext`). Gerar a partir de `siagro-b1-backend/` com `ASPNETCORE_ENVIRONMENT=Ceagui-Development`. **Ler a migration gerada antes de seguir.** Aplicar só no `CEAGUI_SIAGRO_DEV` (Task 7), nunca em outro banco.
- Commits: mensagem no padrão do `CLAUDE.md` do repo (`tipo(escopo): descrição em pt-BR`; escopo `invoice`); o commit da migration leva o trailer `DB: AddPurchaseInvoiceTaxDocumentKind`. Sempre com pathspec explícito: **nunca** incluir os arquivos já staged `docs/superpowers/{specs,plans}/2026-10-01-nfe-standalone-taxation*` (backend) nem `.vscode/.advpl/*.tlpp` (frontend). Todo arquivo novo é `git add` logo depois de criado. **Nunca dar push.**
- Testes: `dotnet build SiagroB1.sln` com 0 erros; `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests` inteiros verdes ao fim de cada task de backend. As asserções dos testes existentes não mudam, **exceto** as listadas nas tasks (cada uma com o motivo).
- Frontend: `yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (≤ 919 problemas) e a suíte QUnit de unidade (rodar com `npx ui5 serve --port 8081` e abrir `http://localhost:8081/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests`; parar o servidor pelo PID no fim; não deixar pasta `report/`). **Nunca mexer na porta 8080 nem em processos que você não subiu** — o usuário costuma ter a própria stack de pé.
- UI5: expressões de binding sobre propriedades OData com `${path: 'X', targetType: 'any'}`; nada de `--` dentro de comentário XML; `setProperty` em grupo diferido sem `await`.
- Desvio consciente da spec §5.3: o rascunho da importação (`PurchaseInvoiceDraftDto`) não ganha `TaxDocumentKind`. Os dois caminhos da inclusão (importar e digitar) passam pelo mesmo `createDraft` do frontend, que já começa em `"Nfe"` (Task 5) — o efeito pedido (R1: "importar XML grava Nfe") é o mesmo, sem um campo a mais no contrato.

## Review Focus

1. **Fornecedor pessoa física (produtor rural com CPF):** a chave traz o CPF com três zeros à esquerda nas posições 7–20 — tem de passar na regra do emitente. (Task 3)
2. **Chave colada do DANFE com espaços** ("3526 1000 0529 …"): o operador espera que funcione — o servidor normaliza para os 44 dígitos antes de conferir e grava normalizada. (Task 3)
3. **Número/série digitados com zeros à esquerda** ("000000456", "001") contra a chave: comparados como números, não recusam. (Task 3)
4. **Terceiro trocado para Devolução (devolução do cliente)** depois de escolher naturezas: não calcula no servidor e a tela esconde natureza/CFOP. (Tasks 2 e 5)
5. **Entrada de terceiro antiga, ainda Pendente, sem natureza** (importada antes desta mudança): ao salvar, a recusa nomeia o item ("O item TRIGO está sem natureza de operação."), em vez de um erro genérico. (Task 2)

---

### Task 1: Tipo de documento e autorização no cabeçalho (+ migration)

**Files:**
- Create: `SiagroB1.Domain/Enums/TaxDocumentKind.cs`
- Modify: `SiagroB1.Domain/Entities/PurchaseInvoice.cs` (depois de `ChaveNFe`, ~linha 60)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs:28-29`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs:39-40`
- Create (gerado): `SiagroB1.Migrations/AppContext/<timestamp>_AddPurchaseInvoiceTaxDocumentKind.cs` (+ Designer, snapshot)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceTaxDocumentKindTests.cs`

**Interfaces:**
- Produces: `enum TaxDocumentKind { Nfe = 0, Other = 1 }` (namespace `SiagroB1.Domain.Enums`); em `PurchaseInvoice`: `TaxDocumentKind TaxDocumentKind` (default `Nfe`), `string? SupplierNfeProtocol` (VARCHAR(20)), `DateTime? SupplierNfeCheckedAt`. Create/Update: emissão própria sempre `Nfe`; `SupplierNfeProtocol`/`SupplierNfeCheckedAt` nunca vêm do corpo da requisição.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Tipo do documento fiscal e autorização da NF-e do fornecedor no cabeçalho (spec terceiro-chave §5). Regra
/// inativa de propósito: aqui só o modelo e a gravação, sem cálculo nem conferência de chave.
/// </summary>
public class PurchaseInvoiceTaxDocumentKindTests
{
    private static PurchaseInvoice Invoice(DocumentIssuerType issuer, TaxDocumentKind kind)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = "F-001", IssuerType = issuer, TaxDocumentKind = kind,
            SupplierNfeProtocol = "999", SupplierNfeCheckedAt = new DateTime(2020, 1, 1),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
        });
        return invoice;
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(), new FakeItemService(), TaxTestServices.InactivePurchaseApply(db));

    private static PurchaseInvoicesUpdateService Update(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(), new FakeItemService(), TaxTestServices.InactivePurchaseApply(db));

    [Fact]
    public void Edm_exposes_the_kind_and_the_supplier_authorization()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var type = builder.GetEdmModel().EntityContainer.FindEntitySet("PurchaseInvoices")!.EntityType;

        Assert.EndsWith("TaxDocumentKind", type.FindProperty("TaxDocumentKind")!.Type.FullName());
        Assert.NotNull(type.FindProperty("SupplierNfeProtocol"));
        Assert.NotNull(type.FindProperty("SupplierNfeCheckedAt"));
    }

    [Fact]
    public async Task Own_entry_is_always_nfe()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = Invoice(DocumentIssuerType.Own, TaxDocumentKind.Other);

        await Create(db).ExecuteAsync(invoice, "tester");

        Assert.Equal(TaxDocumentKind.Nfe, (await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync()).TaxDocumentKind);
    }

    [Fact]
    public async Task Third_party_keeps_the_kind_chosen_and_never_takes_the_authorization_from_the_body()
    {
        var db = TestDb.CreateUnitOfWork();

        await Create(db).ExecuteAsync(Invoice(DocumentIssuerType.ThirdParty, TaxDocumentKind.Other), "tester");

        var saved = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync();
        Assert.Equal((TaxDocumentKind.Other, (string?)null, (DateTime?)null),
            (saved.TaxDocumentKind, saved.SupplierNfeProtocol, saved.SupplierNfeCheckedAt));
    }

    [Fact]
    public async Task Update_changes_the_kind_and_keeps_the_stored_authorization()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = Invoice(DocumentIssuerType.ThirdParty, TaxDocumentKind.Nfe);
        await Create(db).ExecuteAsync(invoice, "tester");
        var stored = await db.Context.PurchaseInvoices.SingleAsync();
        (stored.SupplierNfeProtocol, stored.SupplierNfeCheckedAt) = ("135260000000001", new DateTime(2026, 10, 5, 10, 0, 0));
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        changed.TaxDocumentKind = TaxDocumentKind.Other;
        (changed.SupplierNfeProtocol, changed.SupplierNfeCheckedAt) = ("FORJADO", null);

        await Update(db).ExecuteAsync(changed.Key, changed, "tester");

        var saved = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync();
        Assert.Equal((TaxDocumentKind.Other, "135260000000001"), (saved.TaxDocumentKind, saved.SupplierNfeProtocol));
        Assert.NotNull(saved.SupplierNfeCheckedAt);
    }

    [Fact]
    public async Task Update_turns_an_own_entry_back_to_nfe()
    {
        var db = TestDb.CreateUnitOfWork();
        await Create(db).ExecuteAsync(Invoice(DocumentIssuerType.Own, TaxDocumentKind.Nfe), "tester");
        db.Context.ChangeTracker.Clear();
        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        changed.TaxDocumentKind = TaxDocumentKind.Other;

        await Update(db).ExecuteAsync(changed.Key, changed, "tester");

        Assert.Equal(TaxDocumentKind.Nfe, (await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync()).TaxDocumentKind);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceTaxDocumentKindTests"`. Esperado: erro de compilação (`TaxDocumentKind` não existe).

- [ ] **Step 3: Implementar**

`SiagroB1.Domain/Enums/TaxDocumentKind.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Tipo do documento fiscal do Documento de Entrada (spec terceiro-chave D2). <c>Nfe</c> é o eletrônico ("tipo
/// SPED", modelo 55), com chave obrigatória e conferida; <c>Other</c> é nota de serviço, nota de papel/talão etc.,
/// sem chave. A emissão própria é sempre <c>Nfe</c>.
/// </summary>
public enum TaxDocumentKind
{
    Nfe = 0,
    Other = 1,
}
```

Em `PurchaseInvoice.cs`, logo depois da propriedade `ChaveNFe`:

```csharp
    /// <summary>Tipo do documento fiscal (spec terceiro-chave D2). Emissão própria é sempre <see cref="TaxDocumentKind.Nfe"/>.</summary>
    public TaxDocumentKind TaxDocumentKind { get; set; } = TaxDocumentKind.Nfe;

    /// <summary>
    /// Protocolo de autorização da NF-e do fornecedor, devolvido pela consulta à SEFAZ no confirmar (filial em
    /// Produção). Só o servidor grava.
    /// </summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? SupplierNfeProtocol { get; set; }

    /// <summary>Quando a consulta à SEFAZ autorizou a NF-e do fornecedor. Só o servidor grava.</summary>
    public DateTime? SupplierNfeCheckedAt { get; set; }
```

Em `PurchaseInvoicesCreateService.ExecuteAsync`, logo depois de `PurchaseInvoiceNfeLock.ResetIssuanceFields(invoice);`:

```csharp
        // A autorização da NF-e do fornecedor só o confirmar grava; emissão própria é sempre NF-e.
        invoice.SupplierNfeProtocol = null;
        invoice.SupplierNfeCheckedAt = null;
        if (invoice.IssuerType == DocumentIssuerType.Own)
            invoice.TaxDocumentKind = TaxDocumentKind.Nfe;
```

Em `PurchaseInvoicesUpdateService.ExecuteAsync`, logo depois de `existing.IssuerType = entity.IssuerType;` (não copiar `SupplierNfeProtocol`/`SupplierNfeCheckedAt`):

```csharp
        existing.TaxDocumentKind = existing.IssuerType == DocumentIssuerType.Own ? TaxDocumentKind.Nfe : entity.TaxDocumentKind;
```

Gerar a migration (de `siagro-b1-backend/`):

```bash
dotnet build SiagroB1.sln
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddPurchaseInvoiceTaxDocumentKind --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build
```

Ler o arquivo gerado: tem de conter só os três `AddColumn` em `PURCHASE_INVOICES` (`TaxDocumentKind` int not null default 0, `SupplierNfeProtocol` VARCHAR(20) null, `SupplierNfeCheckedAt` datetime2 null) e os `DropColumn` no `Down`. Acrescentar no FIM do `Up`:

```csharp
            // Documento antigo de terceiro sem chave de 44 caracteres não é eletrônico: não passa a exigir chave.
            migrationBuilder.Sql(
                "UPDATE PURCHASE_INVOICES SET TaxDocumentKind = 1 WHERE IssuerType = 0 AND (ChaveNFe IS NULL OR LEN(ChaveNFe) <> 44)");
```

(`IssuerType`: ThirdParty = 0, Own = 1; `TaxDocumentKind`: Nfe = 0, Other = 1.)

- [ ] **Step 4: Rodar e ver passar** — o filtro da Step 2, depois `dotnet build SiagroB1.sln` e as duas suítes completas.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Domain/Enums/TaxDocumentKind.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceTaxDocumentKindTests.cs SiagroB1.Migrations/AppContext/*AddPurchaseInvoiceTaxDocumentKind*.cs
git commit -m "feat(invoice): tipo de documento e autorização da NF-e do fornecedor na entrada" -m "DB: AddPurchaseInvoiceTaxDocumentKind" -- SiagroB1.Domain/Enums/TaxDocumentKind.cs SiagroB1.Domain/Entities/PurchaseInvoice.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceTaxDocumentKindTests.cs SiagroB1.Migrations/AppContext/
```

(`SiagroB1.Migrations/AppContext/` inclui o `AppDbContextModelSnapshot.cs` alterado; conferir com `git status` que nada além disso entrou.)

---

### Task 2: Terceiro Normal calculado pela natureza

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs:11-32,74-79`
- Rename + rewrite: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceSupplierTaxes.cs` → `PurchaseInvoiceSupplierItemNumbers.cs` (`git mv`)
- Modify: `PurchaseInvoicesCreateService.cs:78-81`, `PurchaseInvoicesUpdateService.cs:99-101`, `PurchaseInvoicesItemsCreateService.cs:31-38`
- Create: `SiagroB1.Application.Tests/Support/LegacySupplierSnapshot.cs`
- Modify: `SiagroB1.Application.Tests/Support/ThirdPartyPurchaseSeed.cs:49`, `SiagroB1.Application.Tests/Support/PurchaseNfeTestSeed.cs` (helpers)
- Modify: `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesThirdPartyReturnTests.cs:295` (+ teste novo)
- Rename + rewrite: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceSupplierTaxesTests.cs` → `PurchaseInvoiceSupplierItemNumbersTests.cs` (`git mv`)
- Modify: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesTaxApplyTests.cs` (`Third_party_document_is_not_calculated`, ~linha 161)
- Create: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceThirdPartyTaxationTests.cs`

**Interfaces:**
- Consumes: `TaxDocumentKind` (Task 1).
- Produces: `PurchaseInvoicesTaxApplyService.IsActiveForAsync` verdadeiro também para terceiro Normal; `PurchaseInvoiceSupplierItemNumbers.Ensure(PurchaseInvoice, IEnumerable<PurchaseInvoiceItem>)` (só confere `nItem` contra o XML); test helpers `PurchaseNfeTestSeed.Partners()` (fornecedor `F-SP` com nome, UF SP e CPF `52998224725`) e `PurchaseNfeTestSeed.PurchaseUsageCodeAsync(UnitOfWork)`; `LegacySupplierSnapshot.Apply(PurchaseInvoice)` (test support).
- Some do código de produção: `PurchaseInvoiceSupplierTaxes` (Apply/Clear/Copy).

**Asserções existentes que mudam (motivo: D1 da spec):** `PurchaseInvoicesTaxApplyTests.Third_party_document_is_not_calculated` (o terceiro Normal agora É calculado — vira o teste novo abaixo); os testes de `PurchaseInvoiceSupplierTaxesTests` que afirmavam a tributação copiada do XML (substituídos por `PurchaseInvoiceSupplierItemNumbersTests` e `PurchaseInvoiceThirdPartyTaxationTests`). Os testes da devolução de terceiro mantêm as asserções: a origem deles passa a ser montada por `LegacySupplierSnapshot` (documento confirmado antes desta mudança, estado que continua existindo no banco).

- [ ] **Step 1: Helpers de teste**

Em `PurchaseNfeTestSeed` (classe estática), acrescentar:

```csharp
    /// <summary>O fornecedor F-SP como o cadastro o vê: nome, UF (SP) e CPF — o CPF é o que a chave de acesso traz.</summary>
    public static FakeBusinessPartnerService Partners() => new(
        names: new Dictionary<string, string> { [Supplier] = "PRODUTOR RURAL TESTE" },
        states: new Dictionary<string, string> { [Supplier] = "SP" },
        taxIds: new Dictionary<string, string> { [Supplier] = "52998224725" });

    /// <summary>Código da natureza de Entrada "COMPRA DE MERCADORIA" semeada.</summary>
    public static Task<int> PurchaseUsageCodeAsync(UnitOfWork db) =>
        db.Context.Usages.Where(u => u.Name == "COMPRA DE MERCADORIA").Select(u => u.Code).SingleAsync();
```

Criar `SiagroB1.Application.Tests/Support/LegacySupplierSnapshot.cs` (é o `Copy` que sai da produção, para montar origens antigas):

```csharp
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Fotografia que a entrada de terceiro importada gravava ANTES da spec terceiro-chave: a tributação do det do
/// fornecedor, por nItem. Documentos confirmados nessa época continuam no banco assim, e os testes da devolução de
/// terceiro partem deles.
/// </summary>
public static class LegacySupplierSnapshot
{
    public static void Apply(PurchaseInvoice invoice)
    {
        var nfe = SupplierNfeXmlReader.Read(invoice.XmlData!);

        foreach (var item in invoice.Items)
        {
            if (item.NfeItemNumber is not { } number)
                continue;

            var det = nfe.Items.Single(d => d.ItemNumber == number);
            item.Cfop = det.Cfop;
            item.Ncm = det.Ncm;
            item.GoodsOrigin = det.GoodsOrigin;
            item.IcmsBenefitCode = det.BenefitCode;
            item.CstIcms = det.CstIcms;
            item.IcmsBase = det.IcmsBase;
            item.IcmsBaseReduction = det.IcmsBaseReduction;
            item.IcmsRate = det.IcmsRate;
            item.IcmsValue = det.IcmsValue;
            item.IcmsDeferral = det.IcmsDeferral;
            item.IcmsOperationValue = det.IcmsOperationValue;
            item.IcmsDeferredValue = det.IcmsDeferredValue;
            item.CstPis = det.CstPis;
            item.PisBase = det.PisBase;
            item.PisRate = det.PisRate;
            item.PisValue = det.PisValue;
            item.CstCofins = det.CstCofins;
            item.CofinsBase = det.CofinsBase;
            item.CofinsRate = det.CofinsRate;
            item.CofinsValue = det.CofinsValue;
            item.IbsCbsCst = det.IbsCbsCst;
            item.IbsCbsClassCode = det.IbsCbsClassCode;
            item.IbsCbsBase = det.IbsCbsBase;
            item.IbsStateRate = det.IbsStateRate;
            item.IbsMunicipalRate = det.IbsMunicipalRate;
            item.IbsRateReduction = det.IbsRateReduction;
            item.IbsStateValue = det.IbsStateValue;
            item.IbsMunicipalValue = det.IbsMunicipalValue;
            item.CbsRate = det.CbsRate;
            item.CbsRateReduction = det.CbsRateReduction;
            item.CbsValue = det.CbsValue;
        }
    }
}
```

Em `ThirdPartyPurchaseSeed.cs:49` trocar `PurchaseInvoiceSupplierTaxes.Apply(origin, origin.Items);` por:

```csharp
        if (withXml)
            LegacySupplierSnapshot.Apply(origin);
```

Em `PurchaseInvoicesThirdPartyReturnTests.cs:295` trocar `PurchaseInvoiceSupplierTaxes.Apply(stored, stored.Items);` por `LegacySupplierSnapshot.Apply(stored);`.

- [ ] **Step 2: Escrever os testes que falham**

`SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceThirdPartyTaxationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Terceiro Normal na filial que emite NF-e pelo Siagro: natureza obrigatória (de Entrada) e o motor calcula tudo,
/// como na entrada própria — inclusive no importado do XML (spec terceiro-chave D1, §6).
/// </summary>
public class PurchaseInvoiceThirdPartyTaxationTests
{
    private static PurchaseInvoice ThirdParty(int? usage, string? xml = null)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = PurchaseInvoiceType.Normal,
            TaxDocumentKind = TaxDocumentKind.Nfe, ChaveNFe = SupplierNfeXml.AccessKey,
            TaxDocumentNumber = "456", TaxDocumentSeries = "1", IssueDate = new DateTime(2026, 10, 1),
            XmlData = xml is null ? null : SupplierNfeXml.Bytes(xml),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1000m, UnitPrice = 1.5m,
            UsageCode = usage, NfeItemNumber = xml is null ? null : 1,
        });
        return invoice;
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners(), erp));

    private static PurchaseInvoicesUpdateService Update(UnitOfWork db) =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners()));

    [Fact]
    public async Task Third_party_normal_line_is_calculated_by_its_nature()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = ThirdParty(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));

        await Create(db).ExecuteAsync(invoice, "tester");

        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal(("1102", "51", 18m, 100m, "SP053521", "74"),
            (line.Cfop, line.CstIcms, line.IcmsRate, line.IcmsDeferral, line.IcmsBenefitCode, line.CstPis));
        Assert.Equal("COMPRA DE MERCADORIA", line.UsageName);
    }

    [Fact]
    public async Task Imported_line_gets_the_calculated_taxes_and_keeps_the_supplier_item_number()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var xml = SupplierNfeXml.Build(SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms00(1500m, 12m)));
        var invoice = ThirdParty(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db), xml);

        await Create(db).ExecuteAsync(invoice, "tester");

        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal(("51", 18m, "1102", 1), (line.CstIcms, line.IcmsRate, line.Cfop, line.NfeItemNumber!.Value));
    }

    [Fact]
    public async Task Line_without_nature_is_refused()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(db).ExecuteAsync(ThirdParty(null), "tester"));

        Assert.Equal("O item TRIGO está sem natureza de operação.", e.Message);
    }

    [Fact]
    public async Task Outgoing_nature_is_refused()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Create(scenario.Db).ExecuteAsync(ThirdParty(scenario.ReturnUsage), "tester"));

        Assert.Equal("A natureza de operação DEVOLUCAO DE COMPRA é de saída e não pode ser usada na entrada.", e.Message);
    }

    [Fact]
    public async Task Customer_return_is_not_calculated()
    {
        // Review Focus 4: "De terceiro + Devolução" é a devolução do cliente e fica como chegou.
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = ThirdParty(null);
        invoice.InvoiceType = PurchaseInvoiceType.Return;
        invoice.Items.Single().CstIcms = "00";

        await Create(db).ExecuteAsync(invoice, "tester");

        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal(("00", (string?)null), (line.CstIcms, line.Cfop));
    }

    [Fact]
    public async Task Branch_without_the_rule_keeps_the_third_party_as_typed()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = ThirdParty(null);
        invoice.Items.Single().CstIcms = "00";

        await Create(db, "SAPB1").ExecuteAsync(invoice, "tester");

        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal(("00", (string?)null), (line.CstIcms, line.Cfop));
    }

    [Fact]
    public async Task Legacy_pending_third_party_without_nature_is_refused_on_save()
    {
        // Review Focus 5: entrada importada antes desta mudança, ainda Pendente e sem natureza nas linhas.
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var legacy = ThirdParty(null);
        db.Context.PurchaseInvoices.Add(legacy);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == legacy.Key);
        changed.Comments = "conferido";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(db).ExecuteAsync(changed.Key, changed, "tester"));

        Assert.Equal("O item TRIGO está sem natureza de operação.", e.Message);
    }

    [Fact]
    public async Task Line_added_to_a_third_party_document_is_calculated_without_item_number()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var usage = await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db);
        var invoice = ThirdParty(usage);
        await Create(db).ExecuteAsync(invoice, "tester");
        var added = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = "MILHO", UnitOfMeasureCode = "KG",
            Quantity = 10m, UnitPrice = 1m, UsageCode = usage, NfeItemNumber = 7,
        };

        await new PurchaseInvoicesItemsCreateService(db, new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners())).ExecuteAsync(added, "tester");

        var line = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.ItemCode == "MILHO" && i.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal(("1102", (int?)null), (line.Cfop, line.NfeItemNumber));
    }
}
```

Renomear o teste do XML: `git mv SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceSupplierTaxesTests.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceSupplierItemNumbersTests.cs` e substituir TODO o conteúdo por:

```csharp
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// O nItem da NF-e do fornecedor na linha do terceiro: com o XML guardado, todo nItem informado precisa existir na
/// nota (é o que a devolução referencia). A tributação não vem mais do XML (spec terceiro-chave §6.2).
/// </summary>
public class PurchaseInvoiceSupplierItemNumbersTests
{
    private static string Xml() => SupplierNfeXml.Build(
        SupplierNfeXml.Det(1, "TRG", "TRIGO", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m), SupplierNfeXml.IbsCbs()),
        SupplierNfeXml.Det(2, "MLH", "MILHO", 500m, 1m, SupplierNfeXml.Icms00(500m, 12m)));

    private static PurchaseInvoice Invoice(DocumentIssuerType issuer, PurchaseInvoiceType type, string? xml, params int?[] numbers)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), CardCode = PurchaseNfeTestSeed.Supplier, IssuerType = issuer, InvoiceType = type,
            XmlData = xml is null ? null : SupplierNfeXml.Bytes(xml),
        };
        foreach (var number in numbers)
            invoice.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
                NfeItemNumber = number, CstIcms = "00",
            });
        return invoice;
    }

    [Fact]
    public void Item_numbers_of_the_xml_pass_and_nothing_is_copied()
    {
        var invoice = Invoice(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Normal, Xml(), 1, 2, null);

        PurchaseInvoiceSupplierItemNumbers.Ensure(invoice, invoice.Items);

        Assert.All(invoice.Items, i => Assert.Equal("00", i.CstIcms));
    }

    [Fact]
    public void Item_number_missing_from_the_xml_is_refused()
    {
        var invoice = Invoice(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Normal, Xml(), 9);

        var e = Assert.Throws<DefaultException>(() => PurchaseInvoiceSupplierItemNumbers.Ensure(invoice, invoice.Items));

        Assert.Equal("Item TRIGO: o item 9 não existe na NF-e do fornecedor.", e.Message);
    }

    [Fact]
    public void Document_without_xml_accepts_any_typed_item_number()
    {
        var invoice = Invoice(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Normal, null, 9);

        PurchaseInvoiceSupplierItemNumbers.Ensure(invoice, invoice.Items);

        Assert.Equal(9, invoice.Items.Single().NfeItemNumber);
    }

    [Theory]
    [InlineData(DocumentIssuerType.Own, PurchaseInvoiceType.Normal)]
    [InlineData(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Return)]
    public void Own_entry_and_customer_return_are_not_checked(DocumentIssuerType issuer, PurchaseInvoiceType type)
    {
        var invoice = Invoice(issuer, type, Xml(), 9);

        PurchaseInvoiceSupplierItemNumbers.Ensure(invoice, invoice.Items);

        Assert.Equal(9, invoice.Items.Single().NfeItemNumber);
    }
}
```

Em `PurchaseInvoicesTaxApplyTests.cs`, trocar o teste `Third_party_document_is_not_calculated` por (mesmo lugar):

```csharp
    [Fact]
    public async Task Third_party_normal_document_is_calculated_like_the_own()
    {
        // Antes desta mudança o terceiro nunca calculava; a spec terceiro-chave D1 o põe igual à própria.
        var seed = await SeedAsync();
        var invoice = OwnEntry(seed.PurchaseUsage);
        invoice.IssuerType = DocumentIssuerType.ThirdParty;
        invoice.TaxDocumentKind = TaxDocumentKind.Other;

        await Create(seed).ExecuteAsync(invoice, "tester");

        Assert.NotNull((await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync()).Cfop);
    }
```

(`OwnEntry(int? usageCode, ...)`, `seed.PurchaseUsage` e `Create(Seed seed, ...)` são os helpers que o arquivo já tem.)

Em `PurchaseInvoicesThirdPartyReturnTests.cs`, acrescentar (origem DIGITADA, calculada pelo motor, agora conferida — spec §6.3):

```csharp
    [Fact]
    public async Task Typed_entry_calculated_by_the_engine_is_conferred_and_mirrored()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(withXml: false);
        var context = scenario.Db.Context;
        var usage = await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(scenario.Db);
        var stored = await context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);
        stored.InvoiceStatus = InvoiceStatus.Pending;
        foreach (var item in stored.Items)
            item.UsageCode = usage;
        await TaxTestServices.PurchaseApply(scenario.Db, PurchaseNfeTestSeed.Partners()).ApplyAsync(stored, stored.Items);
        stored.InvoiceStatus = InvoiceStatus.Confirmed;
        await scenario.Db.SaveChangesAsync();
        context.ChangeTracker.Clear();
        origin = await context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);
        Assert.Equal("51", origin.Items.Single(i => i.ItemCode == "TRIGO").CstIcms);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 100m, 1)), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal(("5202", "51", 100m), (line.Cfop, line.CstIcms, line.IcmsDeferral));
    }
```

- [ ] **Step 3: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceThirdPartyTaxationTests|FullyQualifiedName~PurchaseInvoiceSupplierItemNumbersTests|FullyQualifiedName~PurchaseInvoicesTaxApplyTests|FullyQualifiedName~PurchaseInvoicesThirdPartyReturnTests"`. Esperado: compilação falha (`PurchaseInvoiceSupplierItemNumbers` não existe) e, depois dela, os testes de cálculo do terceiro falham (CFOP nulo).

- [ ] **Step 4: Implementar**

`PurchaseInvoicesTaxApplyService.cs` — summary da classe e `IsActiveForAsync`:

```csharp
/// <summary>
/// Tributos da linha do Documento de Entrada pela natureza (spec §6.2). Só age com a regra ativa e documento
/// Pendente. Calcula a emissão própria — a entrada Normal (natureza de Entrada, CFOP 1xxx/2xxx) e a devolução de
/// compra criada pelo Devolver (natureza de Saída, CFOP 5xxx/6xxx, conferida contra a linha comprada) — e, desde a
/// spec terceiro-chave (D1), o documento de terceiro Normal, igual à entrada própria. A devolução do cliente
/// (terceiro + Devolução) fica exatamente como chegou.
/// </summary>
```

```csharp
    public async Task<bool> IsActiveForAsync(PurchaseInvoice invoice) =>
        (invoice.IssuerType == DocumentIssuerType.Own
         && (invoice.InvoiceType == PurchaseInvoiceType.Normal || IsOwnNfeReturn(invoice))
         || invoice.IssuerType == DocumentIssuerType.ThirdParty && invoice.InvoiceType == PurchaseInvoiceType.Normal)
        && await gate.IsActiveAsync(invoice.BranchCode);
```

No bloco `if (ownReturn)` do `ApplyAsync`, trocar o comentário da conferência por:

```csharp
                // Origem sem fotografia (entrada de terceiro antiga digitada, ou linha incluída depois da importação
                // antes da spec terceiro-chave): não há com o que conferir. A origem calculada pelo motor é conferida.
```

`git mv SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceSupplierTaxes.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceSupplierItemNumbers.cs` e substituir TODO o conteúdo por:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// O nItem da NF-e do fornecedor em cada linha do Documento de Entrada de terceiro — é o que a devolução referencia
/// (DFeReferenciado). Com o XML guardado, todo nItem informado precisa existir na nota. A tributação NÃO vem daqui:
/// o terceiro Normal é calculado pela natureza, como o próprio (spec terceiro-chave D1, §6.2).
/// </summary>
public static class PurchaseInvoiceSupplierItemNumbers
{
    public static void Ensure(PurchaseInvoice invoice, IEnumerable<PurchaseInvoiceItem> items)
    {
        if (invoice.IssuerType != DocumentIssuerType.ThirdParty || invoice.InvoiceType != PurchaseInvoiceType.Normal ||
            invoice.XmlData is not { Length: > 0 })
            return;

        var numbers = SupplierNfeXmlReader.Read(invoice.XmlData).Items.Select(d => d.ItemNumber).ToHashSet();

        foreach (var item in items)
            if (item.NfeItemNumber is { } number && !numbers.Contains(number))
                throw new DefaultException($"Item {item.ItemCode}: o item {number} não existe na NF-e do fornecedor.");
    }
}
```

`PurchaseInvoicesCreateService.cs` — trocar o bloco do `PurchaseInvoiceSupplierTaxes.Apply` por:

```csharp
        // Documento de terceiro com o XML: todo nItem informado precisa existir na nota do fornecedor (é o que a
        // devolução referencia). Só na filial que emite NF-e pelo Siagro; fora dela o documento grava como veio.
        if (await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            PurchaseInvoiceSupplierItemNumbers.Ensure(invoice, invoice.Items);
```

`PurchaseInvoicesUpdateService.cs` — trocar o bloco equivalente por:

```csharp
        // Só na filial que emite NF-e pelo Siagro (regra ativa); fora dela o documento grava como veio.
        if (await taxApply.IsBranchActiveAsync(existing.BranchCode))
            PurchaseInvoiceSupplierItemNumbers.Ensure(existing, existing.Items);
```

`PurchaseInvoicesItemsCreateService.cs` — trocar o bloco do terceiro por:

```csharp
        // Linha incluída depois da importação não é item da nota do fornecedor: sem nItem (informado no "Devolver").
        // A tributação vem da natureza, como em toda linha do terceiro Normal.
        // Só na filial que emite NF-e pelo Siagro (regra ativa); fora dela a linha grava como veio.
        if (invoice.IssuerType == DocumentIssuerType.ThirdParty && invoice.InvoiceType == PurchaseInvoiceType.Normal &&
            await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            item.NfeItemNumber = null;
```

e ajustar o comentário da chamada `taxApply.ApplyAsync(invoice, [item])` para `// Tributos pela natureza (no-op com a regra inativa, devolução do cliente ou documento confirmado).`

- [ ] **Step 5: Rodar e ver passar** — o filtro da Step 3, depois as duas suítes completas. Qualquer outro teste que falhe por criar/alterar terceiro Normal numa filial ativa sem natureza: se o objetivo do teste não é a tributação, dar à linha a natureza de compra semeada (ou `TaxDocumentKind`/tipo que o isole) sem mudar a asserção, e listar cada caso no relatório.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application.Tests/Support/LegacySupplierSnapshot.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceThirdPartyTaxationTests.cs
git commit -m "feat(invoice): entrada de terceiro calculada pela natureza como a própria" -- SiagroB1.Application/Services/PurchaseInvoices/ SiagroB1.Application.Tests/
```

(O `git mv` já deixa a renomeação no índice. Se o commit com a renomeação for recusado pelo classificador de permissões, manter os nomes antigos dos dois arquivos com o conteúdo novo e registrar no relatório.)

---

### Task 3: Conferência local da chave de acesso

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseInvoices/SupplierNfeKeyGuard.cs`
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceChaveNFe.cs`
- Modify: `PurchaseInvoicesCreateService.cs` (chamada da unicidade e do guard; remover o método privado `EnsureChaveNFeIsFreeAsync`)
- Modify: `PurchaseInvoicesUpdateService.cs` (guard + unicidade)
- Modify: `SiagroB1.Application.Tests/Support/SupplierNfeXml.cs:12` (DV da chave de teste)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/SupplierNfeKeyGuardTests.cs`

**Interfaces:**
- Consumes: `TaxDocumentKind` (Task 1); `PurchaseNfeTestSeed.Partners()` (Task 2).
- Produces: `SupplierNfeKeyGuard.AppliesTo(PurchaseInvoice) : bool` (terceiro + Normal + `Nfe`); `SupplierNfeKeyGuard.Ensure(PurchaseInvoice invoice, string? supplierTaxId)` (lança `DefaultException`; normaliza `ChaveNFe`; preenche número/série em branco); `SupplierNfeKeyGuard.CheckDigit(string first43) : int`; `PurchaseInvoiceChaveNFe.EnsureFreeAsync(IUnitOfWork db, string? chaveNFe, Guid key)`.

**Chaves de teste (DV conferidos):**
- fornecedor F-SP (CPF 52998224725), série 1, nº 456: `35261000052998224725550010000004561123456782` (vira o novo `SupplierNfeXml.AccessKey`; a antiga terminava em 0, DV inválido)
- mesma, DV errado: `35261000052998224725550010000004561123456781`
- CNPJ 11222333000181, série 1, nº 789: `35261011222333000181550010000007891876543211`
- modelo 65, mesmo CNPJ: `35261011222333000181650010000007891876543214`

- [ ] **Step 1: Escrever os testes que falham**

Em `SupplierNfeXml.cs` trocar a constante para `public const string AccessKey = "35261000052998224725550010000004561123456782";`.

`SiagroB1.Application.Tests/PurchaseInvoices/SupplierNfeKeyGuardTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Conferência local da chave da NF-e do fornecedor (spec terceiro-chave §7), nos dois ambientes.</summary>
public class SupplierNfeKeyGuardTests
{
    private const string Cpf = "52998224725";

    private static PurchaseInvoice Doc(string? key, string? number = "456", string? series = "1") => new()
    {
        Key = Guid.NewGuid(), CardCode = PurchaseNfeTestSeed.Supplier, IssuerType = DocumentIssuerType.ThirdParty,
        InvoiceType = PurchaseInvoiceType.Normal, TaxDocumentKind = TaxDocumentKind.Nfe, ChaveNFe = key,
        TaxDocumentNumber = number, TaxDocumentSeries = series,
    };

    private static string Refusal(PurchaseInvoice doc, string? taxId = Cpf) =>
        Assert.Throws<DefaultException>(() => SupplierNfeKeyGuard.Ensure(doc, taxId)).Message;

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Missing_key_is_refused(string? key) =>
        Assert.Equal("Informe a chave de acesso da NF-e do fornecedor.", Refusal(Doc(key)));

    [Theory]
    [InlineData("3526100005299822472555001000000456112345678")]
    [InlineData("3526100005299822472555001000000456112345678A")]
    public void Key_that_is_not_44_digits_is_refused(string key) =>
        Assert.Equal("A chave de acesso tem 44 dígitos.", Refusal(Doc(key)));

    [Fact]
    public void Wrong_check_digit_is_refused() =>
        Assert.Equal("Chave de acesso inválida: o dígito verificador não confere.",
            Refusal(Doc("35261000052998224725550010000004561123456781")));

    [Fact]
    public void Nfce_key_is_refused() =>
        Assert.Equal("A chave não é de NF-e (modelo 55).",
            Refusal(Doc("35261011222333000181650010000007891876543214", "789"), "11222333000181"));

    [Fact]
    public void Key_of_another_issuer_is_refused() =>
        Assert.Equal("A chave é de outro emitente: o CNPJ/CPF não é o do fornecedor.",
            Refusal(Doc(SupplierNfeXml.AccessKey), "11222333000181"));

    [Fact]
    public void Supplier_without_tax_id_is_refused_as_another_issuer() =>
        Assert.Equal("A chave é de outro emitente: o CNPJ/CPF não é o do fornecedor.", Refusal(Doc(SupplierNfeXml.AccessKey), null));

    [Theory]
    [InlineData("457", "1")]
    [InlineData("456", "2")]
    public void Number_or_series_different_from_the_key_is_refused(string number, string series) =>
        Assert.Equal("O número/série da chave não conferem com os do documento.", Refusal(Doc(SupplierNfeXml.AccessKey, number, series)));

    [Fact]
    public void Cpf_supplier_key_passes()
    {
        // Review Focus 1: produtor rural pessoa física — o CPF vem com três zeros à esquerda na chave.
        SupplierNfeKeyGuard.Ensure(Doc(SupplierNfeXml.AccessKey), "529.982.247-25");
    }

    [Fact]
    public void Cnpj_supplier_key_passes() =>
        SupplierNfeKeyGuard.Ensure(Doc("35261011222333000181550010000007891876543211", "789"), "11.222.333/0001-81");

    [Fact]
    public void Key_pasted_with_spaces_is_normalized()
    {
        // Review Focus 2: o DANFE imprime a chave em grupos de 4.
        var doc = Doc("3526 1000 0529 9822 4725 5500 1000 0004 5611 2345 6782");

        SupplierNfeKeyGuard.Ensure(doc, Cpf);

        Assert.Equal(SupplierNfeXml.AccessKey, doc.ChaveNFe);
    }

    [Fact]
    public void Number_and_series_with_leading_zeros_pass()
    {
        // Review Focus 3.
        SupplierNfeKeyGuard.Ensure(Doc(SupplierNfeXml.AccessKey, "000000456", "001"), Cpf);
    }

    [Fact]
    public void Blank_number_and_series_are_filled_from_the_key_without_leading_zeros()
    {
        var doc = Doc(SupplierNfeXml.AccessKey, null, " ");

        SupplierNfeKeyGuard.Ensure(doc, Cpf);

        Assert.Equal(("456", "1"), (doc.TaxDocumentNumber, doc.TaxDocumentSeries));
    }

    [Theory]
    [InlineData(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Normal, TaxDocumentKind.Nfe, true)]
    [InlineData(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Normal, TaxDocumentKind.Other, false)]
    [InlineData(DocumentIssuerType.ThirdParty, PurchaseInvoiceType.Return, TaxDocumentKind.Nfe, false)]
    [InlineData(DocumentIssuerType.Own, PurchaseInvoiceType.Normal, TaxDocumentKind.Nfe, false)]
    public void Applies_only_to_the_third_party_normal_electronic_document(
        DocumentIssuerType issuer, PurchaseInvoiceType type, TaxDocumentKind kind, bool expected)
    {
        var doc = Doc(null);
        (doc.IssuerType, doc.InvoiceType, doc.TaxDocumentKind) = (issuer, type, kind);

        Assert.Equal(expected, SupplierNfeKeyGuard.AppliesTo(doc));
    }

    // --- gravação ---

    private static PurchaseInvoice Saved(string? key, TaxDocumentKind kind = TaxDocumentKind.Nfe)
    {
        var doc = Doc(key);
        doc.TaxDocumentKind = kind;
        doc.BranchCode = "01";
        doc.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
        });
        return doc;
    }

    private static async Task<(UnitOfWork Db, int Usage)> ActiveAsync()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        return (db, await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners(), erp));

    [Fact]
    public async Task Create_refuses_a_key_with_a_wrong_check_digit()
    {
        var (db, usage) = await ActiveAsync();
        var doc = Saved("35261000052998224725550010000004561123456781");
        doc.Items.Single().UsageCode = usage;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(db).ExecuteAsync(doc, "tester"));

        Assert.Equal("Chave de acesso inválida: o dígito verificador não confere.", e.Message);
    }

    [Fact]
    public async Task Other_document_saves_without_a_key()
    {
        var (db, usage) = await ActiveAsync();
        var doc = Saved(null, TaxDocumentKind.Other);
        doc.Items.Single().UsageCode = usage;

        await Create(db).ExecuteAsync(doc, "tester");

        Assert.True(await db.Context.PurchaseInvoices.AnyAsync(i => i.Key == doc.Key));
    }

    [Fact]
    public async Task Branch_without_the_rule_saves_any_key()
    {
        var (db, _) = await ActiveAsync();

        await Create(db, "SAPB1").ExecuteAsync(Saved("123"), "tester");

        Assert.Equal("123", (await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.ChaveNFe == "123")).ChaveNFe);
    }

    [Fact]
    public async Task Update_refuses_a_key_already_used_by_another_document()
    {
        var (db, usage) = await ActiveAsync();
        var first = Saved(SupplierNfeXml.AccessKey);
        first.Items.Single().UsageCode = usage;
        await Create(db).ExecuteAsync(first, "tester");
        var second = Saved(null, TaxDocumentKind.Other);
        second.Items.Single().UsageCode = usage;
        await Create(db).ExecuteAsync(second, "tester");
        db.Context.ChangeTracker.Clear();
        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == second.Key);
        changed.TaxDocumentKind = TaxDocumentKind.Nfe;
        changed.ChaveNFe = SupplierNfeXml.AccessKey;

        var e = await Assert.ThrowsAsync<DefaultException>(() => new PurchaseInvoicesUpdateService(db, PurchaseNfeTestSeed.Partners(),
            new FakeItemService(), TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners())).ExecuteAsync(changed.Key, changed, "tester"));

        Assert.Equal($"Já existe documento de entrada com a chave de NF-e {SupplierNfeXml.AccessKey}.", e.Message);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SupplierNfeKeyGuardTests"`. Esperado: compilação falha (`SupplierNfeKeyGuard` não existe).

- [ ] **Step 3: Implementar**

`SupplierNfeKeyGuard.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Conferência local da chave de acesso da NF-e do fornecedor (spec terceiro-chave §7). Vale para o documento de
/// terceiro Normal do tipo NF-e, na filial que emite pelo Siagro, ao salvar e ao confirmar, nos dois ambientes —
/// quem pergunta pela regra ativa é o chamador. A consulta à SEFAZ (só Produção) é do
/// <c>SupplierNfeAuthorizationService</c>.
/// </summary>
public static class SupplierNfeKeyGuard
{
    public static bool AppliesTo(PurchaseInvoice invoice) =>
        invoice.IssuerType == DocumentIssuerType.ThirdParty
        && invoice.InvoiceType == PurchaseInvoiceType.Normal
        && invoice.TaxDocumentKind == TaxDocumentKind.Nfe;

    /// <summary>
    /// Recusa a chave incoerente, na ordem da spec. Normaliza a chave colada do DANFE (com espaços) e preenche número
    /// e série em branco a partir dela. <paramref name="supplierTaxId"/>: CNPJ/CPF do fornecedor no cadastro.
    /// </summary>
    public static void Ensure(PurchaseInvoice invoice, string? supplierTaxId)
    {
        var key = string.Concat((invoice.ChaveNFe ?? string.Empty).Where(c => !char.IsWhiteSpace(c)));

        if (key.Length == 0)
            throw new DefaultException("Informe a chave de acesso da NF-e do fornecedor.");

        if (key.Length != 44 || !key.All(char.IsAsciiDigit))
            throw new DefaultException("A chave de acesso tem 44 dígitos.");

        if (CheckDigit(key[..43]) != key[43] - '0')
            throw new DefaultException("Chave de acesso inválida: o dígito verificador não confere.");

        if (key.Substring(20, 2) != "55")
            throw new DefaultException("A chave não é de NF-e (modelo 55).");

        // CPF (produtor rural pessoa física) vem na chave com três zeros à esquerda.
        var supplier = string.Concat((supplierTaxId ?? string.Empty).Where(char.IsAsciiDigit));
        if (supplier.Length == 0 || key.Substring(6, 14) != supplier.PadLeft(14, '0'))
            throw new DefaultException("A chave é de outro emitente: o CNPJ/CPF não é o do fornecedor.");

        var series = key.Substring(22, 3);
        var number = key.Substring(25, 9);

        if (!SameNumber(invoice.TaxDocumentSeries, series) || !SameNumber(invoice.TaxDocumentNumber, number))
            throw new DefaultException("O número/série da chave não conferem com os do documento.");

        invoice.ChaveNFe = key;

        // Sem zeros à esquerda, como a importação grava nNF/serie.
        if (string.IsNullOrWhiteSpace(invoice.TaxDocumentSeries))
            invoice.TaxDocumentSeries = long.Parse(series).ToString();
        if (string.IsNullOrWhiteSpace(invoice.TaxDocumentNumber))
            invoice.TaxDocumentNumber = long.Parse(number).ToString();
    }

    /// <summary>Dígito verificador da chave: módulo 11 com pesos 2 a 9 da direita para a esquerda; resto 0 ou 1 → 0.</summary>
    public static int CheckDigit(string first43)
    {
        var sum = 0;
        var weight = 2;

        for (var i = first43.Length - 1; i >= 0; i--)
        {
            sum += (first43[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var rest = sum % 11;
        return rest < 2 ? 0 : 11 - rest;
    }

    /// <summary>Em branco no documento: será preenchido. Digitado: comparado como número ("001" = "1").</summary>
    private static bool SameNumber(string? typed, string fromKey) =>
        string.IsNullOrWhiteSpace(typed) || (long.TryParse(typed.Trim(), out var value) && value == long.Parse(fromKey));
}
```

`PurchaseInvoiceChaveNFe.cs` (o método que sai do Create, sem mudança de regra):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Uma chave de NF-e, um documento registrado. O índice único no banco é a rede de segurança; esta checagem existe
/// para a mensagem sair legível em pt-BR — na inclusão e, desde a spec terceiro-chave, também na alteração.
///
/// Documento CANCELADO não segura a chave — relançar é caminho legítimo, e é por isso que o índice do banco também é
/// filtrado por status. Chave vazia ou em branco é tratada como AUSENTE.
/// </summary>
public static class PurchaseInvoiceChaveNFe
{
    public static async Task EnsureFreeAsync(IUnitOfWork db, string? chaveNFe, Guid key)
    {
        if (string.IsNullOrWhiteSpace(chaveNFe))
            return;

        var duplicated = await db.Context.PurchaseInvoices
            .AnyAsync(x => x.ChaveNFe == chaveNFe && x.InvoiceStatus != InvoiceStatus.Cancelled && x.Key != key);

        if (duplicated)
            throw new DefaultException($"Já existe documento de entrada com a chave de NF-e {chaveNFe}.");
    }
}
```

`PurchaseInvoicesCreateService.cs`: apagar a linha `await EnsureChaveNFeIsFreeAsync(invoice.ChaveNFe, invoice.Key);` e o método privado `EnsureChaveNFeIsFreeAsync` (com o summary dele). Logo depois de `var partner = await businessPartnerService.GetByIdAsync(invoice.CardCode);`, inserir:

```csharp
        // Documento eletrônico de terceiro: chave coerente com o fornecedor, número e série (spec terceiro-chave §7).
        // Antes da unicidade: o guard normaliza a chave colada com espaços.
        if (SupplierNfeKeyGuard.AppliesTo(invoice) && await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            SupplierNfeKeyGuard.Ensure(invoice, partner?.TaxId);

        await PurchaseInvoiceChaveNFe.EnsureFreeAsync(db, invoice.ChaveNFe, invoice.Key);
```

`PurchaseInvoicesUpdateService.cs`: logo depois do bloco que recusa a devolução de compra manual (antes de `await SyncItemsAsync(existing, entity);`), inserir:

```csharp
        // Documento eletrônico de terceiro: chave coerente com o fornecedor, número e série (spec terceiro-chave §7).
        if (SupplierNfeKeyGuard.AppliesTo(existing) && await taxApply.IsBranchActiveAsync(existing.BranchCode))
            SupplierNfeKeyGuard.Ensure(existing, (await businessPartnerService.GetByIdAsync(existing.CardCode))?.TaxId);

        await PurchaseInvoiceChaveNFe.EnsureFreeAsync(db, existing.ChaveNFe, existing.Key);
```

- [ ] **Step 4: Rodar e ver passar** — o filtro da Step 2, depois as duas suítes completas (a troca do DV de `SupplierNfeXml.AccessKey` precisa manter verdes os testes da importação e da devolução de terceiro; se algum compara a chave literal antiga, atualizar para a constante).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseInvoices/SupplierNfeKeyGuard.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceChaveNFe.cs SiagroB1.Application.Tests/PurchaseInvoices/SupplierNfeKeyGuardTests.cs
git commit -m "feat(invoice): conferir a chave de acesso da NF-e do fornecedor ao salvar" -- SiagroB1.Application/Services/PurchaseInvoices/ SiagroB1.Application.Tests/
```

---

### Task 4: Consulta à SEFAZ no confirmar (Produção)

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/SupplierNfeAuthorizationService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesConfirmService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (registrar o serviço novo, perto de `services.AddScoped<BranchNfeSettingsService>();`)
- Modify: `SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs` (guardar a configuração consultada)
- Modify: `SiagroB1.Application.Tests/Support/TaxTestServices.cs` (helper `PurchaseConfirm`)
- Modify (só a construção do serviço): `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeIssueServiceTests.cs:17`, `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceNfeLockTests.cs:300,310,323`, `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesLifecycleTests.cs:47,60,169`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesConfirmSupplierNfeTests.cs`

**Interfaces:**
- Consumes: `SupplierNfeKeyGuard` (Task 3), `TaxDocumentKind`/`SupplierNfeProtocol`/`SupplierNfeCheckedAt` (Task 1), `PurchaseNfeTestSeed.Partners()` (Task 2), `BranchNfeSettingsService.OpenAsync(string branchCode, NfeEnvironment? environment = null) : Task<NfeServiceContext>` (existente), `INfeSefazClient.ConsultProtocolAsync(string accessKey, NfeServiceSettings settings, CancellationToken)` (existente; `NfeCommunicationException` = sem resposta).
- Produces: `SupplierNfeAuthorizationService(IUnitOfWork db, BranchNfeSettingsService settingsService, INfeSefazClient sefaz)` com `Task EnsureAuthorizedAsync(PurchaseInvoice invoice)` (no-op fora de Produção; grava protocolo/data no `invoice` rastreado; lança `DefaultException` nas recusas); `PurchaseInvoicesConfirmService(IUnitOfWork db, TaxCalculationGate gate, IBusinessPartnerService businessPartnerService, SupplierNfeAuthorizationService supplierNfe)`; test helper `TaxTestServices.PurchaseConfirm(UnitOfWork db, string? erp = "STANDALONE", IBusinessPartnerService? partners = null, FakeNfeSefazClient? sefaz = null)`.

**Asserção existente com montagem ajustada (motivo: D2/D3):** `PurchaseInvoiceNfeLockTests.Third_party_document_confirms_as_before` — o documento passa a ser `TaxDocumentKind.Other` (a intenção do teste é "terceiro confirma sem NF-e própria"; o eletrônico agora exige a chave do fornecedor, coberto pelos testes novos). A asserção não muda.

- [ ] **Step 1: Suporte de teste**

Em `FakeNfeSefazClient`, acrescentar a propriedade e registrar a configuração em `ConsultProtocolAsync` (logo depois de `Consulted.Add(accessKey);`):

```csharp
    public List<NfeServiceSettings> ConsultedSettings { get; } = [];
```

```csharp
        ConsultedSettings.Add(settings);
```

Em `TaxTestServices`, acrescentar (com os `using` necessários: `SiagroB1.Application.Services.Nfe`, `SiagroB1.Application.Services.PurchaseInvoices`):

```csharp
    /// <summary>Confirmação do documento de entrada com a SEFAZ simulada (consulta da NF-e do fornecedor).</summary>
    public static PurchaseInvoicesConfirmService PurchaseConfirm(
        UnitOfWork db, string? erp = "STANDALONE", IBusinessPartnerService? partners = null, FakeNfeSefazClient? sefaz = null)
    {
        sefaz ??= new FakeNfeSefazClient();
        var config = NfeTestSeed.Config(erp);

        return new PurchaseInvoicesConfirmService(db, new TaxCalculationGate(db, config), partners ?? new FakeBusinessPartnerService(),
            new SupplierNfeAuthorizationService(db, new BranchNfeSettingsService(db, new NfeOptions(config), sefaz), sefaz));
    }
```

Trocar cada `new PurchaseInvoicesConfirmService(db, TaxTestServices.Gate(db, "X"))` dos arquivos listados por `TaxTestServices.PurchaseConfirm(db, "X")`, e o de `PurchaseInvoicesNfeIssueServiceTests.cs:17` por `TaxTestServices.PurchaseConfirm(scenario.Db)`. Em `PurchaseInvoiceNfeLockTests.Third_party_document_confirms_as_before`, junto de `entity.IssuerType = DocumentIssuerType.ThirdParty;`, acrescentar `entity.TaxDocumentKind = TaxDocumentKind.Other;`.

- [ ] **Step 2: Escrever os testes que falham**

`SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesConfirmSupplierNfeTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Confirmar o documento eletrônico de terceiro: chave conferida sempre; com a filial em Produção, a NF-e do fornecedor
/// consultada na SEFAZ da UF da chave e só autorizada confirma (spec terceiro-chave §8).
/// </summary>
public class PurchaseInvoicesConfirmSupplierNfeTests
{
    private const string PrKey = "41261000052998224725550010000004561123456780";

    private static async Task<(UnitOfWork Db, PurchaseInvoice Doc)> SeedAsync(
        NfeEnvironment environment = NfeEnvironment.Production, string? key = SupplierNfeXml.AccessKey,
        TaxDocumentKind kind = TaxDocumentKind.Nfe, PurchaseInvoiceType type = PurchaseInvoiceType.Normal)
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        (await db.Context.BranchNfeSettings.SingleAsync(s => s.BranchCode == "01")).Environment = environment;
        var doc = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = type, InvoiceStatus = InvoiceStatus.Pending,
            TaxDocumentKind = kind, ChaveNFe = key, TaxDocumentNumber = "456", TaxDocumentSeries = "1",
        };
        doc.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
        });
        db.Context.PurchaseInvoices.Add(doc);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return (db, doc);
    }

    private static Task ConfirmAsync(UnitOfWork db, PurchaseInvoice doc, FakeNfeSefazClient sefaz, string erp = "STANDALONE") =>
        TaxTestServices.PurchaseConfirm(db, erp, PurchaseNfeTestSeed.Partners(), sefaz).ExecuteAsync(doc.Key, "tester");

    private static Task<PurchaseInvoice> LoadAsync(UnitOfWork db, PurchaseInvoice doc) =>
        db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == doc.Key);

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public async Task Authorized_key_confirms_and_stores_the_protocol(int status)
    {
        var (db, doc) = await SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key, status));

        await ConfirmAsync(db, doc, sefaz);

        var saved = await LoadAsync(db, doc);
        Assert.Equal((InvoiceStatus.Confirmed, "135260000000001"), (saved.InvoiceStatus, saved.SupplierNfeProtocol));
        Assert.NotNull(saved.SupplierNfeCheckedAt);
        Assert.Equal(new[] { SupplierNfeXml.AccessKey }, sefaz.Consulted);
        Assert.Equal("SP", sefaz.ConsultedSettings.Single().IssuerState);
    }

    [Theory]
    [InlineData(101, "A NF-e do fornecedor está cancelada na SEFAZ.")]
    [InlineData(151, "A NF-e do fornecedor está cancelada na SEFAZ.")]
    [InlineData(155, "A NF-e do fornecedor está cancelada na SEFAZ.")]
    [InlineData(110, "A NF-e do fornecedor teve o uso denegado na SEFAZ.")]
    [InlineData(301, "A NF-e do fornecedor teve o uso denegado na SEFAZ.")]
    [InlineData(217, "A chave de acesso não consta na SEFAZ.")]
    [InlineData(999, "A SEFAZ não confirmou a NF-e do fornecedor (999 – Rejeição: teste). Tente novamente.")]
    public async Task Unauthorized_key_is_refused_and_the_document_stays_pending(int status, string message)
    {
        var (db, doc) = await SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(_ => new NfeSefazResult(status, "Rejeição: teste"));

        var e = await Assert.ThrowsAsync<DefaultException>(() => ConfirmAsync(db, doc, sefaz));

        Assert.Equal(message, e.Message);
        var saved = await LoadAsync(db, doc);
        Assert.Equal((InvoiceStatus.Pending, (string?)null), (saved.InvoiceStatus, saved.SupplierNfeProtocol));
    }

    [Fact]
    public async Task No_answer_from_sefaz_is_refused()
    {
        var (db, doc) = await SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(FakeNfeSefazClient.NoResponse);

        var e = await Assert.ThrowsAsync<DefaultException>(() => ConfirmAsync(db, doc, sefaz));

        Assert.Equal("A SEFAZ não respondeu. Tente novamente.", e.Message);
        Assert.Equal(InvoiceStatus.Pending, (await LoadAsync(db, doc)).InvoiceStatus);
    }

    [Fact]
    public async Task Key_from_another_state_is_consulted_at_that_state()
    {
        var (db, doc) = await SeedAsync(key: PrKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await ConfirmAsync(db, doc, sefaz);

        Assert.Equal("PR", sefaz.ConsultedSettings.Single().IssuerState);
    }

    [Fact]
    public async Task Homologation_does_not_consult()
    {
        var (db, doc) = await SeedAsync(NfeEnvironment.Homologation);
        var sefaz = new FakeNfeSefazClient();

        await ConfirmAsync(db, doc, sefaz);

        var saved = await LoadAsync(db, doc);
        Assert.Equal((InvoiceStatus.Confirmed, (string?)null), (saved.InvoiceStatus, saved.SupplierNfeProtocol));
        Assert.Empty(sefaz.Consulted);
    }

    [Theory]
    [InlineData(NfeEnvironment.Homologation)]
    [InlineData(NfeEnvironment.Production)]
    public async Task Electronic_document_without_key_is_refused_in_both_environments(NfeEnvironment environment)
    {
        var (db, doc) = await SeedAsync(environment, key: null);

        var e = await Assert.ThrowsAsync<DefaultException>(() => ConfirmAsync(db, doc, new FakeNfeSefazClient()));

        Assert.Equal("Informe a chave de acesso da NF-e do fornecedor.", e.Message);
    }

    [Fact]
    public async Task Other_document_confirms_without_key_and_without_consulting()
    {
        var (db, doc) = await SeedAsync(key: null, kind: TaxDocumentKind.Other);
        var sefaz = new FakeNfeSefazClient();

        await ConfirmAsync(db, doc, sefaz);

        Assert.Equal(InvoiceStatus.Confirmed, (await LoadAsync(db, doc)).InvoiceStatus);
        Assert.Empty(sefaz.Consulted);
    }

    [Fact]
    public async Task Customer_return_confirms_without_consulting()
    {
        var (db, doc) = await SeedAsync(key: null, type: PurchaseInvoiceType.Return);
        var sefaz = new FakeNfeSefazClient();

        await ConfirmAsync(db, doc, sefaz);

        Assert.Equal(InvoiceStatus.Confirmed, (await LoadAsync(db, doc)).InvoiceStatus);
        Assert.Empty(sefaz.Consulted);
    }

    [Fact]
    public async Task Branch_without_the_rule_confirms_as_before()
    {
        var (db, doc) = await SeedAsync(key: null);
        var sefaz = new FakeNfeSefazClient();

        await ConfirmAsync(db, doc, sefaz, "SAPB1");

        Assert.Equal(InvoiceStatus.Confirmed, (await LoadAsync(db, doc)).InvoiceStatus);
        Assert.Empty(sefaz.Consulted);
    }
}
```

- [ ] **Step 3: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesConfirmSupplierNfeTests"`. Esperado: compilação falha (`SupplierNfeAuthorizationService` e o construtor novo não existem).

- [ ] **Step 4: Implementar**

`SupplierNfeAuthorizationService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Consulta à SEFAZ da NF-e do fornecedor no confirmar do documento eletrônico de terceiro (spec terceiro-chave §8).
/// Só com o ambiente NF-e da filial em Produção (D4); a chave já foi conferida localmente pelo
/// <c>SupplierNfeKeyGuard</c>. Consulta Situação (consSitNFe) na SEFAZ autorizadora da UF da CHAVE, com o certificado
/// da filial. Sem resposta, recusa e pede para tentar de novo (D5).
/// </summary>
public class SupplierNfeAuthorizationService(IUnitOfWork db, BranchNfeSettingsService settingsService, INfeSefazClient sefaz)
{
    /// <summary>Código IBGE da UF (2 primeiros dígitos da chave) → sigla usada pela configuração da SEFAZ.</summary>
    private static readonly Dictionary<string, string> States = new()
    {
        ["11"] = "RO", ["12"] = "AC", ["13"] = "AM", ["14"] = "RR", ["15"] = "PA", ["16"] = "AP", ["17"] = "TO",
        ["21"] = "MA", ["22"] = "PI", ["23"] = "CE", ["24"] = "RN", ["25"] = "PB", ["26"] = "PE", ["27"] = "AL",
        ["28"] = "SE", ["29"] = "BA", ["31"] = "MG", ["32"] = "ES", ["33"] = "RJ", ["35"] = "SP", ["41"] = "PR",
        ["42"] = "SC", ["43"] = "RS", ["50"] = "MS", ["51"] = "MT", ["52"] = "GO", ["53"] = "DF",
    };

    /// <summary>
    /// Fora de Produção não faz nada. Autorizada: grava protocolo e data no documento (rastreado; quem salva é o
    /// confirmar). Qualquer outra situação: <see cref="DefaultException"/> com a mensagem da spec.
    /// </summary>
    public async Task EnsureAuthorizedAsync(PurchaseInvoice invoice)
    {
        var environment = await db.Context.BranchNfeSettings.AsNoTracking()
            .Where(s => s.BranchCode == invoice.BranchCode)
            .Select(s => (NfeEnvironment?)s.Environment)
            .FirstOrDefaultAsync();

        if (environment != NfeEnvironment.Production)
            return;

        var key = invoice.ChaveNFe!;

        if (!States.TryGetValue(key[..2], out var state))
            throw new DefaultException($"Chave de acesso inválida: UF {key[..2]} desconhecida.");

        using var context = await settingsService.OpenAsync(invoice.BranchCode!);

        NfeSefazResult result;
        try
        {
            result = await sefaz.ConsultProtocolAsync(key, context.Settings with { IssuerState = state });
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException("A SEFAZ não respondeu. Tente novamente.");
        }

        if (NfeStatusCodes.IsAuthorized(result.StatusCode))
        {
            invoice.SupplierNfeProtocol = result.Protocol;
            invoice.SupplierNfeCheckedAt = DateTime.Now;
            return;
        }

        throw new DefaultException(result.StatusCode switch
        {
            101 or 151 or 155 => "A NF-e do fornecedor está cancelada na SEFAZ.",
            _ when NfeStatusCodes.IsDenied(result.StatusCode) => "A NF-e do fornecedor teve o uso denegado na SEFAZ.",
            NfeStatusCodes.NotFound => "A chave de acesso não consta na SEFAZ.",
            _ => $"A SEFAZ não confirmou a NF-e do fornecedor ({result.StatusCode} – {result.Reason}). Tente novamente.",
        });
    }
}
```

`PurchaseInvoicesConfirmService.cs` — construtor e a nova etapa (manter o resto):

```csharp
public class PurchaseInvoicesConfirmService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    IBusinessPartnerService businessPartnerService,
    SupplierNfeAuthorizationService supplierNfe)
```

(com `using SiagroB1.Application.Services.Nfe;` e `using SiagroB1.Domain.Interfaces;`). Logo depois da recusa da emissão própria sem NF-e autorizada:

```csharp
        // Documento eletrônico de terceiro (spec terceiro-chave §7/§8): a chave é conferida de novo e, com a filial em
        // Produção, a NF-e do fornecedor precisa estar autorizada na SEFAZ. A recusa deixa o documento Pendente.
        if (SupplierNfeKeyGuard.AppliesTo(invoice) && await gate.IsActiveAsync(invoice.BranchCode))
        {
            SupplierNfeKeyGuard.Ensure(invoice, (await businessPartnerService.GetByIdAsync(invoice.CardCode))?.TaxId);
            await supplierNfe.EnsureAuthorizedAsync(invoice);
        }
```

Atualizar o `<summary>` da classe: acrescentar ao fim "Documento eletrônico de terceiro: conferência da chave e, em Produção, consulta à SEFAZ (spec terceiro-chave §8)."

`ServiceCollectionExtensions.cs`, logo depois de `services.AddScoped<BranchNfeSettingsService>();`:

```csharp
        services.AddScoped<SupplierNfeAuthorizationService>();
```

- [ ] **Step 5: Rodar e ver passar** — o filtro da Step 3, depois `dotnet build SiagroB1.sln` e as duas suítes completas.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/Nfe/SupplierNfeAuthorizationService.cs SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesConfirmSupplierNfeTests.cs
git commit -m "feat(invoice): consultar a NF-e do fornecedor na SEFAZ ao confirmar em produção" -- SiagroB1.Application/ SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/
```

---

### Task 5: Regra "calcula tributos" e travas da tela (frontend)

**Files (repo `siagro-b1-frontend`):**
- Branch: `git checkout main && git checkout -b feature/third-party-entry-tax-and-key` (conferir antes com `git status` que só os `.tlpp` estão modificados)
- Modify: `webapp/helpers/PurchaseInvoiceNfeHelpers.ts`
- Modify: `webapp/test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts`
- Modify: `webapp/controller/purchaseInvoices/BaseController.ts` (`refreshNfeMode`, novo `onInvoiceTypeChange`)
- Modify: `webapp/view/purchaseInvoices/fragments/Items.fragment.xml:25,63,75`
- Modify: `webapp/controller/purchaseInvoices/Add.controller.ts` (`createDraft` ~linha 144; `onSave` ~linha 238-250)
- Modify: `webapp/controller/purchaseInvoices/Edit.controller.ts` (~linha 118-126)

**Interfaces:**
- Consumes: propriedades `TaxDocumentKind` ("Nfe" | "Other"), `SupplierNfeProtocol`, `SupplierNfeCheckedAt` do EDM (Task 1).
- Produces: `isPurchaseTaxMode(taxLocked: boolean, issuerType: string, invoiceType: string): boolean`; `requiresSupplierKey(doc: { IssuerType: string; InvoiceType: string; TaxDocumentKind: string }, taxLocked: boolean): boolean`; `supplierNfeAuthorizationText(protocol: string, checkedAt: string): string`; flag `ui>/taxMode`; handler `onInvoiceTypeChange()` (a Task 6 liga no Select do Tipo).

- [ ] **Step 1: Escrever os testes que falham** — acrescentar ao fim de `PurchaseInvoiceNfeHelpers.qunit.ts` (e incluir os três nomes no `import` do topo do arquivo):

```ts
QUnit.module("PurchaseInvoiceNfeHelpers - terceiro calculado e chave do fornecedor");

QUnit.test("calcula tributos: própria e terceiro Normal na filial ativa", function (assert) {
	assert.strictEqual(isPurchaseTaxMode(true, "Own", "Normal"), true);
	assert.strictEqual(isPurchaseTaxMode(true, "Own", "Return"), true);
	assert.strictEqual(isPurchaseTaxMode(true, "ThirdParty", "Normal"), true);
});

QUnit.test("não calcula: devolução do cliente e filial sem a regra", function (assert) {
	assert.strictEqual(isPurchaseTaxMode(true, "ThirdParty", "Return"), false);
	assert.strictEqual(isPurchaseTaxMode(false, "ThirdParty", "Normal"), false);
	assert.strictEqual(isPurchaseTaxMode(false, "Own", "Normal"), false);
});

QUnit.test("chave obrigatória só no terceiro Normal do tipo NF-e na filial ativa", function (assert) {
	const doc = { IssuerType: "ThirdParty", InvoiceType: "Normal", TaxDocumentKind: "Nfe" };
	assert.strictEqual(requiresSupplierKey(doc, true), true);
	assert.strictEqual(requiresSupplierKey({ ...doc, TaxDocumentKind: "Other" }, true), false);
	assert.strictEqual(requiresSupplierKey({ ...doc, InvoiceType: "Return" }, true), false);
	assert.strictEqual(requiresSupplierKey({ ...doc, IssuerType: "Own" }, true), false);
	assert.strictEqual(requiresSupplierKey(doc, false), false);
});

QUnit.test("texto da autorização na SEFAZ com protocolo e data", function (assert) {
	const text = supplierNfeAuthorizationText("135260000000001", "2026-10-05T12:30:00-03:00");
	assert.ok(text.startsWith("Autorizada na SEFAZ — protocolo 135260000000001 em 05/10/2026"), text);
});

QUnit.test("sem protocolo não há texto; sem data, só o protocolo", function (assert) {
	assert.strictEqual(supplierNfeAuthorizationText(null, null), "");
	assert.strictEqual(supplierNfeAuthorizationText("135260000000001", null), "Autorizada na SEFAZ — protocolo 135260000000001");
});
```

- [ ] **Step 2: Rodar e ver falhar** — QUnit (ver Global Constraints) filtrado em `module=PurchaseInvoiceNfeHelpers%20-%20terceiro%20calculado%20e%20chave%20do%20fornecedor`. Esperado: o módulo não carrega (funções inexistentes).

- [ ] **Step 3: Implementar**

Em `PurchaseInvoiceNfeHelpers.ts`, depois de `isPurchaseNfeMode`:

```ts
/**
 * Filial que calcula tributos (regra ativa) e documento que o motor calcula: emissão própria ou terceiro Normal (spec
 * terceiro-chave D1). A devolução do cliente (terceiro + Devolução) fica sem natureza. Diferente de `isPurchaseNfeMode`,
 * que é só a emissão própria (travas de número/série/chave, emissão).
 */
export function isPurchaseTaxMode(taxLocked: boolean, issuerType: string, invoiceType: string): boolean {
	return taxLocked === true && (issuerType === "Own" || (issuerType === "ThirdParty" && invoiceType === "Normal"));
}

/** Chave da NF-e do fornecedor obrigatória: terceiro Normal do tipo NF-e na filial que emite pelo Siagro (spec terceiro-chave D3). */
export function requiresSupplierKey(
	doc: { IssuerType: string; InvoiceType: string; TaxDocumentKind: string }, taxLocked: boolean
): boolean {
	return taxLocked === true && doc.IssuerType === "ThirdParty" && doc.InvoiceType === "Normal" &&
		doc.TaxDocumentKind === "Nfe";
}

/** "Autorizada na SEFAZ — protocolo X em dd/mm/aaaa hh:mm" (consulta do confirmar em Produção); vazio sem protocolo. */
export function supplierNfeAuthorizationText(protocol: string, checkedAt: string): string {
	if (!protocol) {
		return "";
	}

	const date = checkedAt ? new Date(checkedAt) : null;
	const when = date && !isNaN(date.getTime())
		? ` em ${date.toLocaleString("pt-BR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" })}`
		: "";

	return `Autorizada na SEFAZ — protocolo ${protocol}${when}`;
}
```

Em `BaseController.ts`: importar `isPurchaseTaxMode` junto de `isPurchaseNfeMode`. Em `refreshNfeMode`, no bloco `if (reset || !oContext)` e no `catch`, acrescentar `uiModel.setProperty("/taxMode", false);`; depois de `uiModel.setProperty("/nfeReturn", isNfeReturn);`, acrescentar:

```ts
      // Natureza, CFOP e "Tributos do item": própria e terceiro Normal (spec terceiro-chave D1).
      uiModel.setProperty("/taxMode", isPurchaseTaxMode(taxLocked, issuerType, invoiceType));
```

Depois de `onIssuerTypeChange`, acrescentar:

```ts
  /** Trocar o tipo (Normal/Devolução) do terceiro liga ou desliga o cálculo: a devolução do cliente não tem natureza. */
  onInvoiceTypeChange() {
    void this.refreshNfeMode(false);
  }
```

Em `Items.fragment.xml`: no botão "Tributos do item" (linha 25), na coluna "Natureza" (linha 63) e na coluna "CFOP" (linha 75), trocar `visible="{= ${ui>/nfeMode} === true }"` por `visible="{= ${ui>/taxMode} === true }"`; no comentário acima da coluna Natureza, trocar "(modo NF-e)" por "(própria e terceiro Normal na filial que calcula tributos)".

Em `Add.controller.ts`: no `oBinding.create({...})` do `createDraft`, logo depois de `IssuerType: "ThirdParty",`, acrescentar `TaxDocumentKind: "Nfe",`. No `onSave`, importar `requiresSupplierKey` de `siagrob1/helpers/PurchaseInvoiceNfeHelpers` e trocar o bloco `if (nfeMode) { const withoutUsage ... }` por:

```ts
    const uiModel = this.getModel("ui") as JSONModel;
    const taxMode = uiModel.getProperty("/taxMode") === true;

    if (taxMode) {
      const withoutUsage = (oBinding?.getAllCurrentContexts() ?? []).filter(ctx => !ctx.getProperty("UsageCode"));
      if (withoutUsage.length > 0) {
        MessageBox.warning("Informe a natureza de operação de todos os itens: os tributos da entrada são calculados por ela.");
        return;
      }
    }

    const keyDoc = {
      IssuerType: oContext.getProperty("IssuerType") as string,
      InvoiceType: oContext.getProperty("InvoiceType") as string,
      TaxDocumentKind: oContext.getProperty("TaxDocumentKind") as string,
    };
    if (requiresSupplierKey(keyDoc, uiModel.getProperty("/taxLocked") === true) &&
        !((oContext.getProperty("ChaveNFe") as string) ?? "").trim()) {
      MessageBox.warning("Informe a chave de acesso da NF-e do fornecedor.");
      return;
    }
```

(Se `uiModel`/`JSONModel` já existirem no método com outro nome, reaproveitar; manter intacta a checagem anterior da devolução própria, que continua por `nfeMode`.)

Em `Edit.controller.ts`, mesma troca no bloco equivalente (a variável `nfeMode` do trecho vira `taxMode`, lida de `/taxMode`, e entra a checagem da chave igual à do Add).

- [ ] **Step 4: Rodar e ver passar** — QUnit do módulo e a suíte de unidade inteira; `yarn ts-typecheck`; `yarn lint`; `npx ui5lint` (≤ 919).

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(invoice): natureza e tributos na entrada de terceiro normal" -- webapp/helpers/PurchaseInvoiceNfeHelpers.ts webapp/test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts webapp/controller/purchaseInvoices/BaseController.ts webapp/controller/purchaseInvoices/Add.controller.ts webapp/controller/purchaseInvoices/Edit.controller.ts webapp/view/purchaseInvoices/fragments/Items.fragment.xml
```

---

### Task 6: "Tipo de documento" e autorização no formulário (frontend)

**Files:**
- Modify: `webapp/view/purchaseInvoices/fragments/Form.fragment.xml` (`core:require` da raiz, linha 6; Select "Tipo" ~linha 67; grupo "Dados da NF-e" ~linhas 289-301)

**Interfaces:**
- Consumes: `supplierNfeAuthorizationText` e `onInvoiceTypeChange` (Task 5); `TaxDocumentKind`, `SupplierNfeProtocol`, `SupplierNfeCheckedAt` (Task 1).

- [ ] **Step 1: Implementar**

Raiz do fragmento: `core:require="{ DoubleType: 'sap/ui/model/odata/type/Double', nfeHelpers: 'siagrob1/helpers/PurchaseInvoiceNfeHelpers' }"`.

No Select "Tipo", acrescentar `change=".onInvoiceTypeChange"`.

No grupo "Dados da NF-e", logo depois de `<core:Title text="Dados da NF-e" />`:

```xml
          <!-- Documento de terceiro: NF-e (eletrônica, com a chave conferida) ou outro documento (serviço, papel/talão),
               sem chave. A emissão própria é sempre NF-e e não mostra o campo. -->
          <Label text="Tipo de documento" visible="{= ${path: 'IssuerType', targetType: 'any'} === 'ThirdParty' }" />
          <Select
            visible="{= ${path: 'IssuerType', targetType: 'any'} === 'ThirdParty' }"
            selectedKey="{path: 'TaxDocumentKind', targetType: 'any'}"
            enabled="{= ${ui>/editable} === true }"
            forceSelection="false"
            width="100%">
            <core:Item key="Nfe" text="NF-e (eletrônica)" />
            <core:Item key="Other" text="Outro (serviço, papel/talão…)" />
          </Select>
```

Trocar o `<Label text="Chave de Acesso" />` por:

```xml
          <Label text="Chave de Acesso"
            required="{= ${ui>/taxLocked} === true &amp;&amp; ${path: 'IssuerType', targetType: 'any'} === 'ThirdParty' &amp;&amp; ${path: 'InvoiceType', targetType: 'any'} === 'Normal' &amp;&amp; ${path: 'TaxDocumentKind', targetType: 'any'} === 'Nfe' }" />
```

Logo depois do `<Input value="{ChaveNFe}" .../>`:

```xml
          <!-- Autorização da NF-e do fornecedor gravada pelo confirmar com a filial em Produção (spec terceiro-chave R4). -->
          <Label text="Autorização na SEFAZ" visible="{= !!${path: 'SupplierNfeProtocol', targetType: 'any'} }" />
          <Text
            visible="{= !!${path: 'SupplierNfeProtocol', targetType: 'any'} }"
            text="{parts: [{path: 'SupplierNfeProtocol', targetType: 'any'}, {path: 'SupplierNfeCheckedAt', targetType: 'any'}], formatter: 'nfeHelpers.supplierNfeAuthorizationText'}" />
```

Atualizar o comentário acima de `<core:Title text="Dados da NF-e" />` para mencionar o tipo de documento.

- [ ] **Step 2: Verificar** — `yarn ts-typecheck`; `yarn lint`; `npx ui5lint` (≤ 919; nenhum achado novo em `Form.fragment.xml`); suíte QUnit de unidade inteira verde.

- [ ] **Step 3: Commit**

```bash
git commit -m "feat(invoice): tipo de documento e autorização da SEFAZ no formulário da entrada" -- webapp/view/purchaseInvoices/fragments/Form.fragment.xml
```

---

### Task 7: Verificação fim a fim (controlador, não subagente)

- [ ] Conferir as portas 8080/5246/50000/58000: se houver processos que você não subiu (a stack do usuário), pedir ao usuário para derrubá-los antes de subir a deste branch. Nunca matar processo alheio.
- [ ] Aplicar a migration no `CEAGUI_SIAGRO_DEV`: `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`; conferir as 3 colunas, o `__EFMigrationsHistory` e o preenchimento: `SELECT IssuerType, TaxDocumentKind, COUNT(*) FROM PURCHASE_INVOICES GROUP BY IssuerType, TaxDocumentKind` (terceiro com chave de 44 → 0; terceiro sem chave → 1; próprio → 0).
- [ ] Subir Gateway (esperar `Loading proxy data from config`), Web e Reports com o perfil `ceagui` e o frontend deste branch (`npx ui5 serve --port 8080`).
- [ ] Navegador, terceiro importado: criar uma cópia do XML fictício `C:\Projetos\SiagroB1\nfe-homologacao-testes\fornecedor-ftestepj-nfe-789.xml` com `nNF` 791 e a chave correspondente (DV recalculado; a 789 já está em uso). Importar → escolher a natureza de Entrada em cada linha → salvar: CFOP 1102 e tributos calculados (conferir no banco), `TaxDocumentKind` 0, `NfeItemNumber` preservado. Confirmar: homologação → confirma sem consultar (`SupplierNfeProtocol` nulo).
- [ ] Navegador, terceiro digitado: "NF-e" com chave de DV errado → "Chave de acesso inválida: o dígito verificador não confere."; trocar para "Outro" sem chave → salva e confirma. Trocar um terceiro para "Devolução" → natureza/CFOP somem da grade.
- [ ] Navegador: "Devolver" da entrada importada nova → a devolução nasce com CFOP 5202 e a conferência passa.
- [ ] Consulta real em homologação (script descartável no scratchpad, NÃO commitado, sem imprimir certificado nem chave de criptografia): com o certificado da filial 01 do `CEAGUI_SIAGRO_DEV` (descriptografado com a variável de ambiente `Nfe__CertificateKey`), chamar `ZeusNfeSefazClient.ConsultProtocolAsync` em homologação para (a) a chave da entrada própria nº 5 autorizada, com `IssuerState = "SP"` → esperar 100 e o protocolo; (b) a chave fictícia do PR `41261000052998224725550010000004561123456780`, com `IssuerState = "PR"` → esperar 217 (não consta). 226 em (b) = roteamento errado: parar e relatar.
- [ ] Produção: só os testes com o cliente falso (não há NF-e real de fornecedor de teste). Oferecer ao usuário, sem executar, uma consulta real em Produção a pedido dele.
- [ ] Derrubar tudo o que subiu (tasks e PIDs nas portas), fechar o navegador; ledger e memória atualizados.
