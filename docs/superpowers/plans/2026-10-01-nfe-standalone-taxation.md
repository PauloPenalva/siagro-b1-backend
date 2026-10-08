# NF-e STANDALONE — natureza fiscal e cálculo de tributos — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A natureza de operação passa a calcular ICMS, PIS/COFINS e IBS/CBS na linha do documento de saída — travada — só em `Erp=STANDALONE` com a chave "Emite NF-e pelo Siagro" ligada na filial.

**Architecture:** Uma regra única (`TaxCalculationGate`) decide se a tributação está ativa. Um calculador puro (`TaxCalculator`) faz as contas; um orquestrador (`SalesInvoicesTaxApplyService`) carrega natureza/filial/cliente/produto/alíquota, aplica as guardas e grava a fotografia na linha. Os serviços existentes do documento de saída chamam o orquestrador; fora da regra, nada muda.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server), Microsoft.AspNetCore.OData 9.4.1, xUnit + EF InMemory; OpenUI5 1.141 + TypeScript (OData v4).

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-01-nfe-standalone-taxation-design.md`

## Global Constraints

- Código em inglês; texto que o usuário lê (labels, mensagens de negócio) em **pt-BR**; comentários em pt-BR.
- Toda regra/guarda nova só vale com `Erp == "STANDALONE"` (normalizado, ausente = STANDALONE como em `Program.cs`) **e** `Branch.IssuesNfe == true` **e** `InvoiceType == Normal`. SAPB1 e STANDALONE com chave desligada: comportamento idêntico ao de hoje.
- Percentuais gravados como **percentual** (`18.0000` = 18%), `DECIMAL(7,4)`. Valores em `DECIMAL(18,2)`.
- Arredondamento dos tributos: 2 casas, `MidpointRounding.AwayFromZero`, a cada base/valor antes do passo seguinte. O valor da linha é o `SalesInvoiceItem.Total` existente.
- Mensagens de guarda: `DefaultException` (o controller devolve 400), pt-BR, citando item/natureza.
- Novo arquivo ⇒ `git add` imediato no repo dele. Commits por tarefa com o padrão `tipo(escopo): descrição` (escopo `invoice` para fiscal, `partner` para parceiro, `master-data` para filial/produto); trailer `DB: <Migration>` quando houver migration; nunca push.
- Migrations: `dotnet ef migrations add <Nome> --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` a partir de `siagro-b1-backend/`. **Ler a migration gerada antes de seguir.** Nunca aplicar em banco sem `ASPNETCORE_ENVIRONMENT` explícito.
- Frontend: campo decimal **editável** usa `sap.ui.model.odata.type.Double` (nunca `Decimal`); enum em expression binding usa `${path: 'X', targetType: 'any'}`; filtro de enum vai como `$filter` estático.
- Testes backend: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"` a partir de `siagro-b1-backend/`.

## Review Focus

1. **Documento Confirmado editado na Conferência de entregas com a regra ativa** — o `SalesInvoicesItemsUpdateService` também serve à conferência; esperado: tributos intactos (nem recálculo, nem valor vindo do corpo). Teste na Task 11.
2. **PATCH do item que reenvia os tributos antigos** (o `Delta` aplicado sobre a entidade rastreada) — esperado: o cálculo sobrescreve, sem 400. Teste na Task 11.
3. **Natureza de Entrada escolhida numa linha de saída** — esperado: guarda com mensagem, não cálculo com CFOP de entrada. Teste na Task 9.
4. **Data de emissão anterior à primeira vigência de IBS/CBS** — esperado: guarda nomeando a data, só quando a natureza tem CST de IBS/CBS. Teste na Task 9.
5. **Base SAPB1 com `IssuesNfe = 1` gravado à mão** — esperado: regra inativa, faturamento de romaneio tolerante como hoje. Teste na Task 6 e na Task 10.

---

## Mapa de arquivos

**Backend (`siagro-b1-backend/`)**

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Enums/UsageDirection.cs` (novo) | Tipo da natureza (Saída/Entrada) |
| `SiagroB1.Domain/Enums/TaxRegime.cs` (novo) | CRT da filial |
| `SiagroB1.Domain/Entities/Usage.cs` | colunas de tributação |
| `SiagroB1.Domain/Models/UsageModel.cs` | idem, expostas pela API |
| `SiagroB1.Domain/Entities/Branch.cs` | `TaxRegime`, `IssuesNfe` |
| `SiagroB1.Domain/Entities/Item.cs`, `Models/ItemModel.cs` | `GoodsOrigin`, `Ncm` |
| `SiagroB1.Domain/Entities/IbsCbsRate.cs` (novo) | alíquotas IBS/CBS por vigência |
| `SiagroB1.Domain/Entities/SalesInvoiceItem.cs`, `SalesInvoice.cs` | fotografia + totais IBS/CBS |
| `SiagroB1.Infra/Context/AppDbContext.cs` | `DbSet<IbsCbsRate>` |
| `SiagroB1.Application/Services/Taxes/ErpMode.cs` (novo) | leitura única do modo |
| `SiagroB1.Application/Services/Taxes/FiscalCodes.cs` (novo) | catálogos de CST/CSOSN/origem |
| `SiagroB1.Application/Services/Taxes/UsageTaxationValidator.cs` (novo) | coerência da natureza |
| `SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs` (novo) | cópia modelo ↔ entidade |
| `SiagroB1.Application/Services/Taxes/TaxCalculationGate.cs` (novo) | a regra de ativação |
| `SiagroB1.Application/Services/Taxes/InterstateIcmsRate.cs` (novo) | 7/12/4 |
| `SiagroB1.Application/Services/Taxes/TaxCalculationModels.cs` (novo) | records de entrada/saída |
| `SiagroB1.Application/Services/Taxes/TaxCalculator.cs` (novo) | contas puras |
| `SiagroB1.Application/Services/Taxes/IbsCbsRatesService.cs` (novo) | CRUD + vigência |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceTaxSnapshot.cs` (novo) | grava/restaura a fotografia |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs` (novo) | orquestração + guardas |
| `SiagroB1.Application/Services/UsageService.cs` | mapeamento + validação + regras de tipo |
| `SiagroB1.Application/Services/BranchService.cs` | validação da chave |
| `SiagroB1.Application/Services/ItemService.cs` | mapeamento + validação |
| `SiagroB1.Application/Services/BusinessPartnerService.cs` | grava endereços na criação |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs`, `SalesInvoicesItemsCreateService.cs`, `SalesInvoicesItemsUpdateService.cs`, `SalesInvoicesUpdateService.cs`, `SalesInvoicesCfopResolveService.cs` | integração |
| `SiagroB1.Web/Controllers/IbsCbsRatesController.cs` (novo), `Actions/Taxes/TaxCalculationIsActiveController.cs` (novo), `Controllers/BusinessPartnersAddressesController.cs` | API |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `Extensions/ServiceCollectionExtensions.cs` | EDM + DI |
| `SiagroB1.Application.Tests/Support/TaxTestServices.cs` (novo) + testes por tarefa | testes |

**Frontend (`siagro-b1-frontend/webapp/`)**

| Arquivo | Responsabilidade |
|---|---|
| `view/usages/fragments/Form.fragment.xml`, `fragments/Taxation.fragment.xml` (novo), `Add.view.xml`, `Edit.view.xml`, `Main.view.xml`, `controller/usages/*.ts` | cadastro da natureza |
| `view/ibsCbsRates/Main.view.xml` (novo), `fragments/RateDialog.fragment.xml` (novo), `controller/ibsCbsRates/Main.controller.ts` (novo), `manifest.json` | alíquotas IBS/CBS |
| `view/branchs/fragments/Form.fragment.xml`, `controller/branchs/*.ts` | CRT + chave |
| `view/produtos/fragments/Form.fragment.xml`, `controller/produtos/*.ts` | origem + NCM |
| `view/salesInvoices/fragments/ItemFiscalDialog.fragment.xml`, `controller/salesInvoices/BaseController.ts`, `Add/Edit/Detail.controller.ts`, `dialogs/DialogHelper.ts`, `dialogs/fragments/UsagesSelectDialog.fragment.xml`, `model/ServerRoutes.ts`, `model/formatter.ts` | trava, campos novos, value help |

---

### Task 1: Natureza — colunas de tributação, tipo e flags (backend)

**Files:**
- Create: `SiagroB1.Domain/Enums/UsageDirection.cs`
- Create: `SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs`
- Modify: `SiagroB1.Domain/Entities/Usage.cs`, `SiagroB1.Domain/Models/UsageModel.cs`, `SiagroB1.Application/Services/UsageService.cs`
- Create (gerada): migration `AddUsageTaxation`
- Test: `SiagroB1.Application.Tests/Usages/UsageTaxationPersistenceTests.cs`

**Interfaces:**
- Produces: `enum UsageDirection { Outgoing = 1, Incoming = 2 }`; propriedades novas em `Usage`/`UsageModel` (nomes da spec §5.1; no modelo `Direction` é `UsageDirection?` e decimais são `decimal?`); `UsageTaxationMapper.CopyToEntity(UsageModel, Usage)` e `UsageTaxationMapper.Normalize(string?) : string?`.

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Application.Tests/Usages/UsageTaxationPersistenceTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>
/// A tributação da natureza mora em USAGES (só STANDALONE). Criar, ler e alterar precisam
/// levar todos os campos; o tipo decide em quais colunas o CFOP é gravado.
/// </summary>
public class UsageTaxationPersistenceTests
{
    private static UsageService Service(UnitOfWork db) => new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Taxed() => new()
    {
        Name = "Venda interestadual",
        Direction = UsageDirection.Outgoing,
        CfopOutgoingInState = "5102",
        CfopOutgoingOutState = "6102",
        InvoiceOperationText = "Venda de producao",
        DefaultAdditionalInfo = "Documento emitido por ME",
        MovesFiscalInventory = true,
        CreatesFinancialDocument = true,
        IcmsInStateCst = "51",
        IcmsInStateRate = 18m,
        IcmsInStateDeferral = 100m,
        IcmsOutStateCst = "00",
        IcmsOutStateCsosn = "900",
        PisCst = "01",
        PisRate = 1.65m,
        CofinsCst = "01",
        CofinsRate = 7.6m,
        ExcludeIcmsFromPisCofinsBase = true,
        IbsCbsCst = "000",
        IbsCbsClassCode = "000001",
        RequiresQuantity = true,
    };

    [Fact]
    public async Task Create_persists_and_projects_every_taxation_field()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db).CreateAsync(Taxed());

        var read = await Service(db).GetByIdAsync(created.Code);

        Assert.NotNull(read);
        Assert.Equal(UsageDirection.Outgoing, read!.Direction);
        Assert.Equal("Venda de producao", read.InvoiceOperationText);
        Assert.Equal("Documento emitido por ME", read.DefaultAdditionalInfo);
        Assert.True(read.MovesFiscalInventory);
        Assert.True(read.CreatesFinancialDocument);
        Assert.Equal("51", read.IcmsInStateCst);
        Assert.Equal(18m, read.IcmsInStateRate);
        Assert.Equal(100m, read.IcmsInStateDeferral);
        Assert.Equal("00", read.IcmsOutStateCst);
        Assert.Equal("900", read.IcmsOutStateCsosn);
        Assert.Equal("01", read.PisCst);
        Assert.Equal(1.65m, read.PisRate);
        Assert.Equal(7.6m, read.CofinsRate);
        Assert.True(read.ExcludeIcmsFromPisCofinsBase);
        Assert.Equal("000", read.IbsCbsCst);
        Assert.Equal("000001", read.IbsCbsClassCode);
    }

    [Fact]
    public async Task Create_without_direction_defaults_to_outgoing()
    {
        var db = TestDb.CreateUnitOfWork();
        var model = Taxed();
        model.Direction = null;

        var created = await Service(db).CreateAsync(model);
        var stored = await db.Context.Usages.SingleAsync(u => u.Code == created.Code);

        Assert.Equal(UsageDirection.Outgoing, stored.Direction);
    }

    [Fact]
    public async Task Incoming_usage_stores_cfops_in_the_incoming_columns()
    {
        var db = TestDb.CreateUnitOfWork();
        var model = new UsageModel
        {
            Name = "Compra de produtor",
            Direction = UsageDirection.Incoming,
            CfopIncomingInState = "1102",
            CfopIncomingOutState = "2102",
            // Lixo de quando o tipo era outro: tem de ser descartado.
            CfopOutgoingInState = "5102",
            RequiresQuantity = true,
        };

        var created = await Service(db).CreateAsync(model);
        var stored = await db.Context.Usages.SingleAsync(u => u.Code == created.Code);

        Assert.Equal("1102", stored.CfopIncomingInState);
        Assert.Equal("2102", stored.CfopIncomingOutState);
        Assert.Null(stored.CfopOutgoingInState);
        Assert.Null(stored.CfopOutgoingOutState);
    }

    [Fact]
    public async Task Blank_codes_are_stored_as_null()
    {
        var db = TestDb.CreateUnitOfWork();
        var model = Taxed();
        model.IcmsOutStateCsosn = "";
        model.IbsCbsCst = " ";
        model.IbsCbsClassCode = "";

        var created = await Service(db).CreateAsync(model);
        var stored = await db.Context.Usages.SingleAsync(u => u.Code == created.Code);

        Assert.Null(stored.IcmsOutStateCsosn);
        Assert.Null(stored.IbsCbsCst);
        Assert.Null(stored.IbsCbsClassCode);
    }

    [Fact]
    public async Task Update_changes_the_taxation_fields()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db).CreateAsync(Taxed());

        var edited = Taxed();
        edited.IcmsInStateRate = 12m;
        edited.PisCst = "09";
        edited.PisRate = null;

        await Service(db).UpdateAsync(created.Code, edited);
        var read = await Service(db).GetByIdAsync(created.Code);

        Assert.Equal(12m, read!.IcmsInStateRate);
        Assert.Equal("09", read.PisCst);
        Assert.Null(read.PisRate);
    }

    [Fact]
    public async Task Direction_cannot_change_once_a_sales_invoice_uses_the_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db).CreateAsync(Taxed());

        db.Context.SalesInvoicesItems.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", UsageCode = created.Code,
        });
        await db.SaveChangesAsync();

        var edited = Taxed();
        edited.Direction = UsageDirection.Incoming;
        edited.CfopIncomingInState = "1102";
        edited.CfopOutgoingInState = null;
        edited.CfopOutgoingOutState = null;
        // CST de PIS/COFINS de ENTRADA: senão a validação de coerência barra antes da regra de uso.
        edited.PisCst = "50";
        edited.CofinsCst = "50";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).UpdateAsync(created.Code, edited));
        Assert.Contains("já foi utilizada", ex.Message);
    }

    [Fact]
    public async Task Incoming_usage_cannot_be_the_shipment_billing_default()
    {
        var db = TestDb.CreateUnitOfWork();
        var model = new UsageModel
        {
            Name = "Compra", Direction = UsageDirection.Incoming, IsDefault = true, RequiresQuantity = true,
        };

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).CreateAsync(model));
        Assert.Contains("entrada", ex.Message);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~UsageTaxationPersistenceTests"`
Expected: FAIL — compile errors (`UsageDirection`, `InvoiceOperationText` etc. não existem).

- [ ] **Step 3: Write the implementation**

`SiagroB1.Domain/Enums/UsageDirection.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Tipo da natureza de operação: em que documento ela pode ser usada. Decide também em quais
/// colunas o CFOP é gravado (as de saída ou as de entrada).
/// </summary>
public enum UsageDirection
{
    Outgoing = 1,
    Incoming = 2,
}
```

`SiagroB1.Domain/Entities/Usage.cs` — acrescentar depois de `Inactive` (manter os comentários existentes; atualizar o `<summary>` da classe dizendo que a TRIBUTAÇÃO também mora aqui, só no STANDALONE):

```csharp
    /// <summary>Tipo da natureza. As existentes nasceram de saída — é o default da migration.</summary>
    public UsageDirection Direction { get; set; } = UsageDirection.Outgoing;

    /// <summary>CFOP de entrada dentro do estado (natureza de Entrada).</summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? CfopIncomingInState { get; set; }

    /// <summary>CFOP de entrada interestadual (natureza de Entrada).</summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? CfopIncomingOutState { get; set; }

    /// <summary>Texto da natureza que vai na NF-e (ide/natOp). Vazio = a NF-e usa o <see cref="Name"/>.</summary>
    [Column(TypeName = "VARCHAR(60)")]
    public string? InvoiceOperationText { get; set; }

    /// <summary>Informações complementares padrão da NF-e (infCpl) — lidas pela emissão.</summary>
    [Column(TypeName = "VARCHAR(2000)")]
    public string? DefaultAdditionalInfo { get; set; }

    /// <summary>
    /// "Movimenta estoque" — estoque FISCAL (saldo por produto/filial movido pelos documentos
    /// fiscais, base do Bloco H). Nada a ver com o saldo gerencial de grãos. Sem efeito por ora:
    /// é copiado para a linha do documento para o futuro estoque fiscal ler o que valia na emissão.
    /// </summary>
    public bool MovesFiscalInventory { get; set; }

    /// <summary>
    /// "Gera financeiro" — conta a receber (saída) ou a pagar (entrada). Sem efeito por ora:
    /// consumido pela Fase 2 do financeiro.
    /// </summary>
    public bool CreatesFinancialDocument { get; set; }

    // ICMS — bloco "dentro do estado". CST vale para CRT 2/3, CSOSN para CRT 1/4.
    [Column(TypeName = "VARCHAR(3)")] public string? IcmsInStateCst { get; set; }
    [Column(TypeName = "VARCHAR(3)")] public string? IcmsInStateCsosn { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? IcmsInStateRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? IcmsInStateBaseReduction { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? IcmsInStateDeferral { get; set; }
    [Column(TypeName = "VARCHAR(10)")] public string? IcmsInStateBenefitCode { get; set; }

    // ICMS — bloco "fora do estado". Sem alíquota: ela é automática (7%/12%/4%).
    [Column(TypeName = "VARCHAR(3)")] public string? IcmsOutStateCst { get; set; }
    [Column(TypeName = "VARCHAR(3)")] public string? IcmsOutStateCsosn { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? IcmsOutStateBaseReduction { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? IcmsOutStateDeferral { get; set; }
    [Column(TypeName = "VARCHAR(10)")] public string? IcmsOutStateBenefitCode { get; set; }

    // PIS/COFINS.
    [Column(TypeName = "VARCHAR(2)")] public string? PisCst { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? PisRate { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public string? CofinsCst { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? CofinsRate { get; set; }

    /// <summary>Tese do STF (Tema 69): a base do PIS/COFINS é o valor menos o ICMS destacado.</summary>
    public bool ExcludeIcmsFromPisCofinsBase { get; set; }

    // IBS/CBS (reforma tributária). As alíquotas vêm da tabela IBS_CBS_RATES por vigência.
    [Column(TypeName = "VARCHAR(3)")] public string? IbsCbsCst { get; set; }
    [Column(TypeName = "VARCHAR(6)")] public string? IbsCbsClassCode { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? IbsRateReduction { get; set; }
    [Column(TypeName = "DECIMAL(7,4)")] public decimal? CbsRateReduction { get; set; }
```

(adicionar `using SiagroB1.Domain.Enums;` no topo.)

`SiagroB1.Domain/Models/UsageModel.cs` — acrescentar antes de `ContractBalanceEffect` (e atualizar o `<summary>`: "em SAPB1 os campos de tributação voltam nulos"):

```csharp
    /// <summary>Nulo em SAPB1: o OUSG não tem tipo. Enum não anulável quebraria a serialização (0 não é membro).</summary>
    public UsageDirection? Direction { get; set; }

    public string? InvoiceOperationText { get; set; }
    public string? DefaultAdditionalInfo { get; set; }
    public bool MovesFiscalInventory { get; set; }
    public bool CreatesFinancialDocument { get; set; }

    public string? IcmsInStateCst { get; set; }
    public string? IcmsInStateCsosn { get; set; }
    public decimal? IcmsInStateRate { get; set; }
    public decimal? IcmsInStateBaseReduction { get; set; }
    public decimal? IcmsInStateDeferral { get; set; }
    public string? IcmsInStateBenefitCode { get; set; }

    public string? IcmsOutStateCst { get; set; }
    public string? IcmsOutStateCsosn { get; set; }
    public decimal? IcmsOutStateBaseReduction { get; set; }
    public decimal? IcmsOutStateDeferral { get; set; }
    public string? IcmsOutStateBenefitCode { get; set; }

    public string? PisCst { get; set; }
    public decimal? PisRate { get; set; }
    public string? CofinsCst { get; set; }
    public decimal? CofinsRate { get; set; }
    public bool ExcludeIcmsFromPisCofinsBase { get; set; }

    public string? IbsCbsCst { get; set; }
    public string? IbsCbsClassCode { get; set; }
    public decimal? IbsRateReduction { get; set; }
    public decimal? CbsRateReduction { get; set; }
```

`SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// Cópia da identidade e da tributação do modelo da API para a entidade USAGES (STANDALONE).
/// Num lugar só para o create e o update não divergirem campo a campo.
/// </summary>
internal static class UsageTaxationMapper
{
    /// <summary>Select vazio da tela chega como "": no banco o "não informado" é nulo.</summary>
    internal static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static void CopyToEntity(UsageModel model, Usage usage)
    {
        var direction = model.Direction ?? UsageDirection.Outgoing;
        var incoming = direction == UsageDirection.Incoming;

        usage.Name = model.Name;
        usage.Description = model.Description;
        usage.Inactive = model.Inactive;
        usage.Direction = direction;

        // O tipo decide as colunas do CFOP; as do outro tipo são descartadas para não sobrar
        // CFOP de saída numa natureza de entrada (e vice-versa).
        usage.CfopOutgoingInState = incoming ? null : Normalize(model.CfopOutgoingInState);
        usage.CfopOutgoingOutState = incoming ? null : Normalize(model.CfopOutgoingOutState);
        usage.CfopIncomingInState = incoming ? Normalize(model.CfopIncomingInState) : null;
        usage.CfopIncomingOutState = incoming ? Normalize(model.CfopIncomingOutState) : null;

        usage.InvoiceOperationText = Normalize(model.InvoiceOperationText);
        usage.DefaultAdditionalInfo = Normalize(model.DefaultAdditionalInfo);
        usage.MovesFiscalInventory = model.MovesFiscalInventory;
        usage.CreatesFinancialDocument = model.CreatesFinancialDocument;

        usage.IcmsInStateCst = Normalize(model.IcmsInStateCst);
        usage.IcmsInStateCsosn = Normalize(model.IcmsInStateCsosn);
        usage.IcmsInStateRate = model.IcmsInStateRate;
        usage.IcmsInStateBaseReduction = model.IcmsInStateBaseReduction;
        usage.IcmsInStateDeferral = model.IcmsInStateDeferral;
        usage.IcmsInStateBenefitCode = Normalize(model.IcmsInStateBenefitCode);

        usage.IcmsOutStateCst = Normalize(model.IcmsOutStateCst);
        usage.IcmsOutStateCsosn = Normalize(model.IcmsOutStateCsosn);
        usage.IcmsOutStateBaseReduction = model.IcmsOutStateBaseReduction;
        usage.IcmsOutStateDeferral = model.IcmsOutStateDeferral;
        usage.IcmsOutStateBenefitCode = Normalize(model.IcmsOutStateBenefitCode);

        usage.PisCst = Normalize(model.PisCst);
        usage.PisRate = model.PisRate;
        usage.CofinsCst = Normalize(model.CofinsCst);
        usage.CofinsRate = model.CofinsRate;
        usage.ExcludeIcmsFromPisCofinsBase = model.ExcludeIcmsFromPisCofinsBase;

        usage.IbsCbsCst = Normalize(model.IbsCbsCst);
        usage.IbsCbsClassCode = Normalize(model.IbsCbsClassCode);
        usage.IbsRateReduction = model.IbsRateReduction;
        usage.CbsRateReduction = model.CbsRateReduction;
    }
}
```

`SiagroB1.Application/Services/UsageService.cs`:
1. Na projeção do `QueryAll`, acrescentar ao `new UsageModel()` todos os campos novos (`Direction = x.usage.Direction`, `CfopIncomingInState = x.usage.CfopIncomingInState`, `CfopIncomingOutState = x.usage.CfopIncomingOutState`, `InvoiceOperationText`, `DefaultAdditionalInfo`, `MovesFiscalInventory`, `CreatesFinancialDocument`, os 6+5 de ICMS, os 4 de PIS/COFINS + `ExcludeIcmsFromPisCofinsBase`, os 4 de IBS/CBS) — cada um `= x.usage.<Mesmo nome>`.
2. `CreateAsync`: depois de `UsageEffectWriter.ValidateEffects(entity);` chamar `ValidateDirectionRules(entity);`, trocar o inicializador por `var usage = new Usage { Name = entity.Name }; UsageTaxationMapper.CopyToEntity(entity, usage);`.
3. `UpdateAsync`: depois de `ValidateEffects`, chamar `ValidateDirectionRules(entity);`; depois de achar `usage`, antes de copiar:

```csharp
        var newDirection = entity.Direction ?? UsageDirection.Outgoing;

        if (newDirection != usage.Direction &&
            await db.Context.SalesInvoicesItems.AnyAsync(x => x.UsageCode == key))
        {
            throw new DefaultException(
                $"Natureza de operação {usage.Name} já foi utilizada em documento de saída. " +
                "O tipo não pode ser alterado.");
        }

        UsageTaxationMapper.CopyToEntity(entity, usage);
```

   (substitui as 5 atribuições manuais de Name/Description/CFOPs/Inactive.)
4. Método novo:

```csharp
    /// <summary>
    /// Regras ligadas ao tipo. A natureza padrão é a do faturamento de romaneio, que é SAÍDA:
    /// uma de entrada ali faria o documento de saída nascer com CFOP de entrada.
    /// </summary>
    private static void ValidateDirectionRules(UsageModel model)
    {
        if (model.Direction == UsageDirection.Incoming && model.IsDefault)
        {
            throw new DefaultException(
                "Natureza de operação de entrada não pode ser a padrão do faturamento de romaneio.");
        }
    }
```

(usings: `SiagroB1.Application.Services.Taxes`, `SiagroB1.Domain.Enums`.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Usage"`
Expected: PASS (os 7 novos + `UsageServiceTests` + `SalesInvoicesUsageGuardServiceTests`).

- [ ] **Step 5: Gerar e revisar a migration**

Run: `dotnet ef migrations add AddUsageTaxation --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`
Conferir que o `Up()` só tem `AddColumn` em `USAGES`. **Editar à mão** o `AddColumn<int>("Direction", ...)` para `defaultValue: 1` (o EF gera 0, que não é membro do enum e transformaria as naturezas existentes em tipo inválido). Rodar `dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` → "No changes".

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Domain/Enums/UsageDirection.cs SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs SiagroB1.Application.Tests/Usages/UsageTaxationPersistenceTests.cs SiagroB1.Migrations/AppContext/*AddUsageTaxation*
git commit -m "feat(invoice): natureza de operação com tipo, tributação e flags de estoque e financeiro" -m "DB: AddUsageTaxation" -- <arquivos da task>
```

---

### Task 2: Natureza — catálogos fiscais e validação de coerência

**Files:**
- Create: `SiagroB1.Application/Services/Taxes/FiscalCodes.cs`, `SiagroB1.Application/Services/Taxes/UsageTaxationValidator.cs`
- Modify: `SiagroB1.Application/Services/UsageService.cs`
- Test: `SiagroB1.Application.Tests/Usages/UsageTaxationValidatorTests.cs`

**Interfaces:**
- Produces: `FiscalCodes` (sets `IcmsCst`, `IcmsCstTaxed`, `IcmsCstNoTax`, `IcmsCsosn`, `IcmsCsosnTaxed`, `IcmsCsosnNoTax`, `PisCofinsCstOutgoing`, `PisCofinsCstIncoming`, `PisCofinsCstNoTax`, `IbsCbsCst`, `IbsCbsCstNoTax`, `ImportedGoodsOrigins`); `UsageTaxationValidator.Validate(UsageModel)` lança `DefaultException`.

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Application.Tests/Usages/UsageTaxationValidatorTests.cs
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Usages;

/// <summary>
/// A tela da natureza valida COERÊNCIA, nunca exigência: natureza sem tributação nenhuma é
/// válida (a MH Agro, STANDALONE sem NF-e, não pode ser obrigada a preencher).
/// </summary>
public class UsageTaxationValidatorTests
{
    private static UsageModel Empty(UsageDirection direction = UsageDirection.Outgoing) =>
        new() { Name = "N", Direction = direction };

    private static void Invalid(UsageModel model, string expected)
    {
        var ex = Assert.Throws<DefaultException>(() => UsageTaxationValidator.Validate(model));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Usage_without_any_taxation_is_valid() =>
        UsageTaxationValidator.Validate(Empty());

    [Theory]
    [InlineData("6102")]
    [InlineData("1102")]
    [InlineData("510")]
    public void Outgoing_in_state_cfop_must_start_with_5(string cfop)
    {
        var m = Empty();
        m.CfopOutgoingInState = cfop;
        Invalid(m, "CFOP");
    }

    [Fact]
    public void Outgoing_out_state_cfop_must_start_with_6()
    {
        var m = Empty();
        m.CfopOutgoingOutState = "5102";
        Invalid(m, "CFOP");
    }

    [Fact]
    public void Incoming_cfops_must_start_with_1_and_2()
    {
        var m = Empty(UsageDirection.Incoming);
        m.CfopIncomingInState = "1102";
        m.CfopIncomingOutState = "2102";
        UsageTaxationValidator.Validate(m);

        m.CfopIncomingOutState = "6102";
        Invalid(m, "CFOP");
    }

    [Fact]
    public void Unknown_icms_cst_is_rejected()
    {
        var m = Empty();
        m.IcmsInStateCst = "10";
        Invalid(m, "CST de ICMS");
    }

    [Fact]
    public void Unknown_csosn_is_rejected()
    {
        var m = Empty();
        m.IcmsOutStateCsosn = "101";
        Invalid(m, "CSOSN");
    }

    [Fact]
    public void Taxed_in_state_cst_requires_a_rate()
    {
        var m = Empty();
        m.IcmsInStateCst = "00";
        Invalid(m, "alíquota");

        m.IcmsInStateRate = 18m;
        UsageTaxationValidator.Validate(m);
    }

    [Fact]
    public void Taxed_out_state_cst_does_not_need_a_rate()
    {
        var m = Empty();
        m.IcmsOutStateCst = "00";
        UsageTaxationValidator.Validate(m);
    }

    [Fact]
    public void Cst_20_requires_base_reduction()
    {
        var m = Empty();
        m.IcmsInStateCst = "20";
        m.IcmsInStateRate = 18m;
        Invalid(m, "redução");

        m.IcmsInStateBaseReduction = 33.33m;
        UsageTaxationValidator.Validate(m);
    }

    [Fact]
    public void Cst_51_requires_deferral_and_deferral_requires_cst_51()
    {
        var m = Empty();
        m.IcmsInStateCst = "51";
        m.IcmsInStateRate = 18m;
        Invalid(m, "diferimento");

        m.IcmsInStateDeferral = 100m;
        UsageTaxationValidator.Validate(m);

        m.IcmsInStateCst = "00";
        Invalid(m, "diferimento");
    }

    [Fact]
    public void Exempt_cst_rejects_rate_reduction_and_deferral()
    {
        var m = Empty();
        m.IcmsInStateCst = "40";
        m.IcmsInStateRate = 18m;
        Invalid(m, "não aceita");
    }

    [Fact]
    public void Exempt_csosn_rejects_rate()
    {
        var m = Empty();
        m.IcmsInStateCsosn = "102";
        m.IcmsInStateRate = 18m;
        Invalid(m, "não aceita");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    public void Percentages_must_be_between_0_and_100(double value)
    {
        var m = Empty();
        m.PisCst = "01";
        m.PisRate = (decimal)value;
        Invalid(m, "entre 0 e 100");
    }

    [Fact]
    public void Pis_cst_must_match_the_direction()
    {
        var m = Empty();
        m.PisCst = "50";
        Invalid(m, "CST de PIS");

        var incoming = Empty(UsageDirection.Incoming);
        incoming.CofinsCst = "01";
        Invalid(incoming, "CST de COFINS");
    }

    [Fact]
    public void Ibs_cbs_cst_requires_six_digit_class_code()
    {
        var m = Empty();
        m.IbsCbsCst = "000";
        Invalid(m, "cClassTrib");

        m.IbsCbsClassCode = "12345";
        Invalid(m, "cClassTrib");

        m.IbsCbsClassCode = "000001";
        UsageTaxationValidator.Validate(m);
    }

    [Fact]
    public void Ibs_cbs_cst_200_requires_a_reduction_and_410_rejects_it()
    {
        var m = Empty();
        m.IbsCbsCst = "200";
        m.IbsCbsClassCode = "200001";
        Invalid(m, "redução");

        m.CbsRateReduction = 60m;
        UsageTaxationValidator.Validate(m);

        m.IbsCbsCst = "410";
        Invalid(m, "não aceita");
    }

    [Fact]
    public void Unknown_ibs_cbs_cst_is_rejected()
    {
        var m = Empty();
        m.IbsCbsCst = "510";
        m.IbsCbsClassCode = "510001";
        Invalid(m, "CST de IBS/CBS");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~UsageTaxationValidatorTests"`
Expected: FAIL — `UsageTaxationValidator` não existe.

- [ ] **Step 3: Write the implementation**

```csharp
// SiagroB1.Application/Services/Taxes/FiscalCodes.cs
namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// Códigos fiscais que este sub-projeto sabe calcular. A tela da natureza só aceita estes, e o
/// <see cref="TaxCalculator"/> só trata estes — a lista é a mesma nos dois para nenhum código
/// passar pelo cadastro e cair num ramo que o cálculo não conhece.
/// ST (CST 10/30/60/70, CSOSN 201/202/203/500) e o CSOSN 101 ficam fora por decisão do spec.
/// </summary>
public static class FiscalCodes
{
    /// <summary>CST de ICMS que destacam imposto (base × alíquota).</summary>
    public static readonly IReadOnlySet<string> IcmsCstTaxed = new HashSet<string> { "00", "20", "51", "90" };

    /// <summary>CST de ICMS sem imposto: isenta (40), não tributada (41), suspensão (50).</summary>
    public static readonly IReadOnlySet<string> IcmsCstNoTax = new HashSet<string> { "40", "41", "50" };

    public static readonly IReadOnlySet<string> IcmsCst = IcmsCstTaxed.Concat(IcmsCstNoTax).ToHashSet();

    public static readonly IReadOnlySet<string> IcmsCsosnTaxed = new HashSet<string> { "900" };

    public static readonly IReadOnlySet<string> IcmsCsosnNoTax = new HashSet<string> { "102", "103", "300", "400" };

    public static readonly IReadOnlySet<string> IcmsCsosn = IcmsCsosnTaxed.Concat(IcmsCsosnNoTax).ToHashSet();

    /// <summary>CST de ICMS que aceitam redução de base.</summary>
    public static readonly IReadOnlySet<string> IcmsCodesWithReduction = new HashSet<string> { "20", "51", "90", "900" };

    public static readonly IReadOnlySet<string> PisCofinsCstOutgoing =
        new HashSet<string> { "01", "02", "03", "04", "05", "06", "07", "08", "09", "49" };

    public static readonly IReadOnlySet<string> PisCofinsCstIncoming = new HashSet<string>
    {
        "50", "51", "52", "53", "54", "55", "56",
        "60", "61", "62", "63", "64", "65", "66", "67",
        "70", "71", "72", "73", "74", "75", "98", "99",
    };

    /// <summary>Monofásico, ST, alíquota zero, isento, sem incidência, suspensão: base e valor zerados.</summary>
    public static readonly IReadOnlySet<string> PisCofinsCstNoTax =
        new HashSet<string> { "04", "05", "06", "07", "08", "09" };

    public static readonly IReadOnlySet<string> IbsCbsCst = new HashSet<string> { "000", "200", "400", "410" };

    /// <summary>Isenção (400) e imunidade/não incidência (410): sem base nem valor.</summary>
    public static readonly IReadOnlySet<string> IbsCbsCstNoTax = new HashSet<string> { "400", "410" };

    /// <summary>Origem da mercadoria com alíquota interestadual de 4% (Res. Senado 13/2012).</summary>
    public static readonly IReadOnlySet<byte> ImportedGoodsOrigins = new HashSet<byte> { 1, 2, 3, 8 };

    /// <summary>Regimes que usam CSOSN em vez de CST: Simples Nacional (1) e MEI (4).</summary>
    public static bool UsesCsosn(Domain.Enums.TaxRegime regime) =>
        regime is Domain.Enums.TaxRegime.SimplesNacional or Domain.Enums.TaxRegime.Mei;
}
```

(`UsesCsosn` depende do enum da Task 3 — se a Task 3 ainda não existir ao implementar a Task 2, criar `SiagroB1.Domain/Enums/TaxRegime.cs` aqui mesmo, com o conteúdo da Task 3, e a Task 3 só o usa.)

```csharp
// SiagroB1.Application/Services/Taxes/UsageTaxationValidator.cs
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// Coerência do cadastro tributário da natureza (STANDALONE). Cada regra só olha campo
/// PREENCHIDO: a natureza sem tributação é válida, porque quem exige a tributação é o
/// documento, e só quando a regra de ativação está ligada.
/// </summary>
public static class UsageTaxationValidator
{
    public static void Validate(UsageModel model)
    {
        var incoming = model.Direction == UsageDirection.Incoming;

        if (incoming)
        {
            ValidateCfop(model.CfopIncomingInState, '1', "CFOP de entrada dentro do estado");
            ValidateCfop(model.CfopIncomingOutState, '2', "CFOP de entrada fora do estado");
        }
        else
        {
            ValidateCfop(model.CfopOutgoingInState, '5', "CFOP de saída dentro do estado");
            ValidateCfop(model.CfopOutgoingOutState, '6', "CFOP de saída fora do estado");
        }

        ValidateIcmsBlock("dentro do estado", model.IcmsInStateCst, model.IcmsInStateCsosn,
            model.IcmsInStateRate, model.IcmsInStateBaseReduction, model.IcmsInStateDeferral, rateRequired: true);

        ValidateIcmsBlock("fora do estado", model.IcmsOutStateCst, model.IcmsOutStateCsosn,
            rate: null, model.IcmsOutStateBaseReduction, model.IcmsOutStateDeferral, rateRequired: false);

        var allowedPisCofins = incoming ? FiscalCodes.PisCofinsCstIncoming : FiscalCodes.PisCofinsCstOutgoing;
        ValidateCode(model.PisCst, allowedPisCofins, "CST de PIS", incoming);
        ValidateCode(model.CofinsCst, allowedPisCofins, "CST de COFINS", incoming);
        ValidatePercent(model.PisRate, "Alíquota de PIS");
        ValidatePercent(model.CofinsRate, "Alíquota de COFINS");

        ValidateIbsCbs(model);
    }

    private static void ValidateCfop(string? cfop, char prefix, string label)
    {
        var value = UsageTaxationMapper.Normalize(cfop);
        if (value is null) return;

        if (value.Length != 4 || !value.All(char.IsDigit) || value[0] != prefix)
        {
            throw new DefaultException($"{label} deve ter 4 dígitos e começar com {prefix}.");
        }
    }

    private static void ValidateIcmsBlock(
        string block, string? cstRaw, string? csosnRaw,
        decimal? rate, decimal? reduction, decimal? deferral, bool rateRequired)
    {
        var cst = UsageTaxationMapper.Normalize(cstRaw);
        var csosn = UsageTaxationMapper.Normalize(csosnRaw);

        if (cst is not null && !FiscalCodes.IcmsCst.Contains(cst))
            throw new DefaultException($"CST de ICMS {cst} ({block}) não é suportado.");

        if (csosn is not null && !FiscalCodes.IcmsCsosn.Contains(csosn))
            throw new DefaultException($"CSOSN {csosn} ({block}) não é suportado.");

        ValidatePercent(rate, $"Alíquota de ICMS ({block})");
        ValidatePercent(reduction, $"Redução de base do ICMS ({block})");
        ValidatePercent(deferral, $"Diferimento do ICMS ({block})");

        var anyTaxed = (cst is not null && FiscalCodes.IcmsCstTaxed.Contains(cst))
                       || (csosn is not null && FiscalCodes.IcmsCsosnTaxed.Contains(csosn));

        if (rateRequired && anyTaxed && (rate ?? 0) <= 0)
            throw new DefaultException($"Informe a alíquota de ICMS ({block}) para o código escolhido.");

        if (cst == "20" && (reduction ?? 0) <= 0)
            throw new DefaultException($"CST 20 ({block}) exige redução de base maior que zero.");

        if (cst == "51" && (deferral ?? 0) <= 0)
            throw new DefaultException($"CST 51 ({block}) exige percentual de diferimento.");

        if ((deferral ?? 0) > 0 && cst != "51")
            throw new DefaultException($"O diferimento ({block}) só vale com o CST 51.");

        var reductionOnlyOnNoTax = (reduction ?? 0) > 0 &&
                                   (cst is null || !FiscalCodes.IcmsCodesWithReduction.Contains(cst)) &&
                                   (csosn is null || !FiscalCodes.IcmsCodesWithReduction.Contains(csosn));

        var noTaxCodes = (cst is not null && FiscalCodes.IcmsCstNoTax.Contains(cst))
                         || (csosn is not null && FiscalCodes.IcmsCsosnNoTax.Contains(csosn));

        if (noTaxCodes && !anyTaxed && ((rate ?? 0) > 0 || (reduction ?? 0) > 0 || (deferral ?? 0) > 0))
            throw new DefaultException(
                $"O código de ICMS ({block}) sem imposto não aceita alíquota, redução nem diferimento.");

        if (reductionOnlyOnNoTax && anyTaxed)
            throw new DefaultException($"O CST de ICMS ({block}) escolhido não aceita redução de base.");
    }

    private static void ValidateCode(string? raw, IReadOnlySet<string> allowed, string label, bool incoming)
    {
        var code = UsageTaxationMapper.Normalize(raw);
        if (code is null) return;

        if (!allowed.Contains(code))
            throw new DefaultException(
                $"{label} {code} não é válido para natureza de {(incoming ? "entrada" : "saída")}.");
    }

    private static void ValidatePercent(decimal? value, string label)
    {
        if (value is < 0 or > 100)
            throw new DefaultException($"{label} deve estar entre 0 e 100.");
    }

    private static void ValidateIbsCbs(UsageModel model)
    {
        var cst = UsageTaxationMapper.Normalize(model.IbsCbsCst);
        var classCode = UsageTaxationMapper.Normalize(model.IbsCbsClassCode);

        ValidatePercent(model.IbsRateReduction, "Redução do IBS");
        ValidatePercent(model.CbsRateReduction, "Redução da CBS");

        if (cst is null) return;

        if (!FiscalCodes.IbsCbsCst.Contains(cst))
            throw new DefaultException($"CST de IBS/CBS {cst} não é suportado.");

        if (classCode is null || classCode.Length != 6 || !classCode.All(char.IsDigit))
            throw new DefaultException("Informe o cClassTrib com 6 dígitos para o CST de IBS/CBS.");

        var anyReduction = (model.IbsRateReduction ?? 0) > 0 || (model.CbsRateReduction ?? 0) > 0;

        if (cst == "200" && !anyReduction)
            throw new DefaultException("CST 200 de IBS/CBS exige redução de alíquota.");

        if (FiscalCodes.IbsCbsCstNoTax.Contains(cst) && anyReduction)
            throw new DefaultException($"CST {cst} de IBS/CBS não aceita redução.");
    }
}
```

`UsageService`: em `CreateAsync` e `UpdateAsync`, depois de `ValidateDirectionRules(entity);`, chamar `UsageTaxationValidator.Validate(entity);`.

**Atenção à regra `noTaxCodes && !anyTaxed`:** um bloco com CST 00 (tributado) e CSOSN 102 (sem imposto) é válido — cada código serve a um regime. A recusa de alíquota só vale quando NENHUM dos dois códigos do bloco é tributado.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Usage"`
Expected: PASS. Se algum teste anterior criar natureza com CFOP fora do padrão (`UsageServiceTests` usa 5949/6949 — válido), ajustar só o dado, não a regra.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/Taxes/FiscalCodes.cs SiagroB1.Application/Services/Taxes/UsageTaxationValidator.cs SiagroB1.Application.Tests/Usages/UsageTaxationValidatorTests.cs
git commit -m "feat(invoice): validar a coerência da tributação no cadastro da natureza" -- <arquivos da task>
```

---

### Task 3: Filial — regime tributário e chave "Emite NF-e pelo Siagro"

**Files:**
- Create: `SiagroB1.Domain/Enums/TaxRegime.cs` (se a Task 2 ainda não criou), `SiagroB1.Application/Services/Taxes/ErpMode.cs`
- Modify: `SiagroB1.Domain/Entities/Branch.cs`, `SiagroB1.Application/Services/BranchService.cs`
- Create (gerada): migration `AddBranchTaxRegimeAndIssuesNfe`
- Test: `SiagroB1.Application.Tests/Branches/BranchServiceIssuesNfeTests.cs`, `SiagroB1.Application.Tests/Support/TaxTestServices.cs`

**Interfaces:**
- Produces: `enum TaxRegime { SimplesNacional = 1, SimplesNacionalExcess = 2, Normal = 3, Mei = 4 }`; `Branch.TaxRegime : TaxRegime?`, `Branch.IssuesNfe : bool`; `ErpMode.IsStandalone(IConfiguration) : bool`; `BranchService(AppDbContext, IConfiguration)`; `TaxTestServices.Config(string erp) : IConfiguration`.

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Application.Tests/Support/TaxTestServices.cs
using Microsoft.Extensions.Configuration;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Configuração mínima com a chave <c>Erp</c>, para exercitar a regra de ativação.</summary>
public static class TaxTestServices
{
    public static IConfiguration Config(string? erp) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = erp })
            .Build();
}
```

```csharp
// SiagroB1.Application.Tests/Branches/BranchServiceIssuesNfeTests.cs
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Branches;

/// <summary>
/// Com a chave "Emite NF-e pelo Siagro" ligada, CRT e UF são obrigatórios — só em STANDALONE.
/// Em SAPB1 a chave é oculta e ignorada, então não pode travar o cadastro.
/// </summary>
public class BranchServiceIssuesNfeTests
{
    private static BranchService Service(Infra.UnitOfWork db, string erp) =>
        new(db.Context, TaxTestServices.Config(erp));

    private static Branch NewBranch(bool issuesNfe, TaxRegime? regime, string? state) => new()
    {
        Code = "01", BranchName = "MATRIZ", ShortName = "MTZ", TaxId = "68583898000101",
        IssuesNfe = issuesNfe, TaxRegime = regime, StateCode = state,
    };

    [Fact]
    public async Task Standalone_rejects_issuing_nfe_without_tax_regime()
    {
        var db = TestDb.CreateUnitOfWork();
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, "STANDALONE").CreateAsync(NewBranch(true, null, "SP")));
        Assert.Contains("regime tributário", ex.Message);
    }

    [Fact]
    public async Task Standalone_rejects_issuing_nfe_without_state()
    {
        var db = TestDb.CreateUnitOfWork();
        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, "STANDALONE").CreateAsync(NewBranch(true, TaxRegime.Normal, null)));
    }

    [Fact]
    public async Task Standalone_accepts_issuing_nfe_with_regime_and_state()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db, "STANDALONE").CreateAsync(NewBranch(true, TaxRegime.Normal, "SP"));
        Assert.True(created.IssuesNfe);
    }

    [Fact]
    public async Task Standalone_update_cannot_clear_the_regime_while_issuing_nfe()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db, "STANDALONE").CreateAsync(NewBranch(true, TaxRegime.Normal, "SP"));

        created.TaxRegime = null;

        await Assert.ThrowsAsync<DefaultException>(() => Service(db, "STANDALONE").UpdateAsync("01", created));
    }

    [Fact]
    public async Task Turning_the_switch_off_is_always_allowed()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db, "STANDALONE").CreateAsync(NewBranch(true, TaxRegime.Normal, "SP"));

        created.IssuesNfe = false;
        created.TaxRegime = null;

        var updated = await Service(db, "STANDALONE").UpdateAsync("01", created);
        Assert.False(updated!.IssuesNfe);
    }

    [Fact]
    public async Task Sapb1_ignores_the_switch()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db, "SAPB1").CreateAsync(NewBranch(true, null, null));
        Assert.True(created.IssuesNfe);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BranchServiceIssuesNfeTests"`
Expected: FAIL — compile errors.

- [ ] **Step 3: Write the implementation**

```csharp
// SiagroB1.Domain/Enums/TaxRegime.cs
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Código de Regime Tributário (CRT) da NF-e. Decide o código de ICMS da linha:
/// Simples Nacional e MEI usam CSOSN; os demais usam CST.
/// </summary>
public enum TaxRegime
{
    SimplesNacional = 1,
    SimplesNacionalExcess = 2,
    Normal = 3,
    Mei = 4,
}
```

```csharp
// SiagroB1.Application/Services/Taxes/ErpMode.cs
using Microsoft.Extensions.Configuration;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// Leitura única do modo de integração. Mesma regra do <c>Program.cs</c>: chave ausente vale
/// STANDALONE. O teste é POSITIVO de propósito: qualquer modo futuro (PROTHEUS, por exemplo)
/// nasce sem as regras da NF-e STANDALONE.
/// </summary>
public static class ErpMode
{
    public static bool IsStandalone(IConfiguration configuration) =>
        string.Equals(
            (configuration["Erp"] ?? "STANDALONE").Trim(),
            "STANDALONE",
            StringComparison.OrdinalIgnoreCase);
}
```

`Branch.cs` — acrescentar (com `using SiagroB1.Domain.Enums;`):

```csharp
        /// <summary>
        /// Regime tributário (CRT) da NF-e. Decide CST (CRT 2/3) ou CSOSN (CRT 1/4) na linha.
        /// Nulável: as filiais existentes não têm o dado, e só a emissão de NF-e o exige.
        /// </summary>
        public TaxRegime? TaxRegime { get; set; }

        /// <summary>
        /// "Emite NF-e pelo Siagro". Junto com Erp=STANDALONE é o que liga o cálculo de tributos
        /// e a trava da linha do documento de saída — ver <c>TaxCalculationGate</c>. Em SAPB1 é
        /// oculta e ignorada.
        /// </summary>
        public bool IssuesNfe { get; set; }
```

`BranchService.cs`: construtor `BranchService(AppDbContext context, IConfiguration configuration)`; em `CreateAsync` (antes do `AddAsync`) e em `UpdateAsync` (antes do `SaveChangesAsync`) chamar `ValidateNfeIssuance(entity);`:

```csharp
    /// <summary>
    /// Com a chave ligada, o cálculo de tributos depende do CRT e da UF da filial. Validado
    /// enquanto a chave está ligada (não só na virada), para ninguém apagar o CRT depois.
    /// Só em STANDALONE: em SAPB1 a chave nem aparece na tela.
    /// </summary>
    private void ValidateNfeIssuance(Branch entity)
    {
        if (!entity.IssuesNfe || !ErpMode.IsStandalone(configuration))
            return;

        if (entity.TaxRegime is null || string.IsNullOrWhiteSpace(entity.StateCode))
            throw new DefaultException(
                "Para emitir NF-e pelo Siagro, informe o regime tributário e a UF da filial.");
    }
```

(usings: `Microsoft.Extensions.Configuration`, `SiagroB1.Application.Services.Taxes`.)

**Spec:** atualizar §5.3 — a validação vale "enquanto a chave estiver ligada" (mais forte que "na virada"); `TaxRegime` é coluna INT (enum padrão da casa), não TINYINT. Mesmo para `UsageDirection` (§5.1).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BranchServiceIssuesNfeTests"`
Expected: PASS.

- [ ] **Step 5: Migration**

Run: `dotnet ef migrations add AddBranchTaxRegimeAndIssuesNfe --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`
Conferir: `AddColumn<int?>("TaxRegime")` nulável e `AddColumn<bool>("IssuesNfe", defaultValue: false)`. `has-pending-model-changes` → "No changes".

- [ ] **Step 6: Commit**

```bash
git add <TaxRegime.cs se novo> SiagroB1.Application/Services/Taxes/ErpMode.cs SiagroB1.Application.Tests/Support/TaxTestServices.cs SiagroB1.Application.Tests/Branches/BranchServiceIssuesNfeTests.cs SiagroB1.Migrations/AppContext/*AddBranchTaxRegimeAndIssuesNfe*
git commit -m "feat(master-data): regime tributário e chave de emissão de NF-e na filial" -m "DB: AddBranchTaxRegimeAndIssuesNfe" -- <arquivos da task>
```

---

### Task 4: Produto — origem da mercadoria e NCM

**Files:**
- Modify: `SiagroB1.Domain/Entities/Item.cs`, `SiagroB1.Domain/Models/ItemModel.cs`, `SiagroB1.Application/Services/ItemService.cs`
- Create (gerada): migration `AddItemGoodsOriginAndNcm`
- Test: `SiagroB1.Application.Tests/Items/ItemServiceFiscalFieldsTests.cs`

**Interfaces:**
- Produces: `Item.GoodsOrigin : byte?` (TINYINT), `Item.Ncm : string?` (VARCHAR(8)); mesmos nomes em `ItemModel`.

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Application.Tests/Items/ItemServiceFiscalFieldsTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.Items;

/// <summary>Origem e NCM do produto (STANDALONE): opcionais, mas coerentes quando preenchidos.</summary>
public class ItemServiceFiscalFieldsTests
{
    private static ItemService Service(Infra.UnitOfWork db) =>
        new(db, NullLogger<ItemService>.Instance, TaxTestServices.Config("STANDALONE"));

    private static ItemModel Soja(byte? origin = 0, string? ncm = "12019000") => new()
    {
        ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", ItmsGrpCod = 105, Enabled = "SIM",
        GoodsOrigin = origin, Ncm = ncm,
    };

    [Fact]
    public async Task Create_and_read_keep_origin_and_ncm()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Soja());

        var read = await Service(db).GetByIdAsync("SOJA");

        Assert.Equal((byte)0, read!.GoodsOrigin);
        Assert.Equal("12019000", read.Ncm);
    }

    [Fact]
    public async Task Update_changes_origin_and_ncm()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Soja());

        await Service(db).UpdateAsync("SOJA", Soja(origin: 1, ncm: "10059010"));
        var read = await Service(db).GetByIdAsync("SOJA");

        Assert.Equal((byte)1, read!.GoodsOrigin);
        Assert.Equal("10059010", read.Ncm);
    }

    [Fact]
    public async Task Both_fields_are_optional()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Soja(origin: null, ncm: null));

        var read = await Service(db).GetByIdAsync("SOJA");
        Assert.Null(read!.GoodsOrigin);
        Assert.Null(read.Ncm);
    }

    [Theory]
    [InlineData("1201900")]
    [InlineData("1201900A")]
    public async Task Ncm_must_have_eight_digits(string ncm)
    {
        var db = TestDb.CreateUnitOfWork();
        await Assert.ThrowsAsync<DefaultException>(() => Service(db).CreateAsync(Soja(ncm: ncm)));
    }

    [Fact]
    public async Task Origin_must_be_between_0_and_8()
    {
        var db = TestDb.CreateUnitOfWork();
        await Assert.ThrowsAsync<DefaultException>(() => Service(db).CreateAsync(Soja(origin: 9)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ItemServiceFiscalFieldsTests"` → FAIL (compile).

- [ ] **Step 3: Write the implementation**

`Item.cs`:

```csharp
    /// <summary>Origem da mercadoria (tabela da SEFAZ, 0 a 8). Vai no XML e decide os 4% interestaduais.</summary>
    [Column(TypeName = "TINYINT")]
    public byte? GoodsOrigin { get; set; }

    /// <summary>NCM, 8 dígitos. Copiado para a linha do documento fiscal.</summary>
    [Column(TypeName = "VARCHAR(8)")]
    public string? Ncm { get; set; }
```

`ItemModel.cs`: `public byte? GoodsOrigin { get; set; }` e `public string? Ncm { get; set; }` (comentário: "nulos em SAPB1 — o produto vem do OITM").

`ItemService.cs`: incluir `GoodsOrigin = x.GoodsOrigin, Ncm = x.Ncm` nas duas projeções (`QueryAll`, `GetByIdAsync`) e no retorno do `UpdateAsync`; no `CreateAsync` e no `UpdateAsync`, chamar `ValidateFiscalFields(entity)` antes de gravar e copiar `GoodsOrigin = entity.GoodsOrigin, Ncm = Normalize(entity.Ncm)`:

```csharp
    /// <summary>Opcionais no cadastro (a MH Agro não emite NF-e); quem exige é o documento.</summary>
    private static void ValidateFiscalFields(ItemModel entity)
    {
        var ncm = string.IsNullOrWhiteSpace(entity.Ncm) ? null : entity.Ncm.Trim();

        if (ncm is not null && (ncm.Length != 8 || !ncm.All(char.IsDigit)))
            throw new DefaultException("O NCM deve ter 8 dígitos.");

        if (entity.GoodsOrigin is > 8)
            throw new DefaultException("A origem da mercadoria deve estar entre 0 e 8.");
    }
```

- [ ] **Step 4: Run test to verify it passes** — mesmo comando → PASS.

- [ ] **Step 5: Migration** — `dotnet ef migrations add AddItemGoodsOriginAndNcm ...`; conferir 2 `AddColumn` em `ITEMS`; `has-pending-model-changes` → No changes.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application.Tests/Items/ItemServiceFiscalFieldsTests.cs SiagroB1.Migrations/AppContext/*AddItemGoodsOriginAndNcm*
git commit -m "feat(master-data): origem da mercadoria e NCM no cadastro de produto" -m "DB: AddItemGoodsOriginAndNcm" -- <arquivos da task>
```

---

### Task 5: Alíquotas IBS/CBS por vigência (entidade, serviço, API)

**Files:**
- Create: `SiagroB1.Domain/Entities/IbsCbsRate.cs`, `SiagroB1.Application/Services/Taxes/IbsCbsRatesService.cs`, `SiagroB1.Web/Controllers/IbsCbsRatesController.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Create (gerada + editada): migration `CreateIbsCbsRates`
- Test: `SiagroB1.Application.Tests/Taxes/IbsCbsRatesServiceTests.cs`, `SiagroB1.Application.Tests/Taxes/TaxationEdmModelTests.cs`

**Interfaces:**
- Produces: `IbsCbsRate { int Key; DateOnly StartDate; decimal CbsRate, IbsStateRate, IbsMunicipalRate }`; `IbsCbsRatesService(IUnitOfWork)` com `QueryAll()`, `GetByIdAsync(int)`, `CreateAsync(IbsCbsRate)`, `UpdateAsync(int, IbsCbsRate)`, `DeleteAsync(int) : bool`, `GetEffectiveAsync(DateOnly) : Task<IbsCbsRate?>`; entity set `IbsCbsRates`.

- [ ] **Step 1: Write the failing tests**

```csharp
// SiagroB1.Application.Tests/Taxes/IbsCbsRatesServiceTests.cs
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Taxes;

public class IbsCbsRatesServiceTests
{
    private static IbsCbsRate Rate(int year, decimal cbs, decimal ibsState, decimal ibsMun = 0m) => new()
    {
        StartDate = new DateOnly(year, 1, 1), CbsRate = cbs, IbsStateRate = ibsState, IbsMunicipalRate = ibsMun,
    };

    [Fact]
    public async Task Effective_rate_is_the_latest_start_date_on_or_before_the_date()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new IbsCbsRatesService(db);
        await service.CreateAsync(Rate(2026, 0.9m, 0.1m));
        await service.CreateAsync(Rate(2027, 8.8m, 0.05m, 0.05m));

        Assert.Equal(0.9m, (await service.GetEffectiveAsync(new DateOnly(2026, 12, 31)))!.CbsRate);
        Assert.Equal(8.8m, (await service.GetEffectiveAsync(new DateOnly(2027, 1, 1)))!.CbsRate);
        Assert.Null(await service.GetEffectiveAsync(new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public async Task Start_date_is_unique()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new IbsCbsRatesService(db);
        await service.CreateAsync(Rate(2026, 0.9m, 0.1m));

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.CreateAsync(Rate(2026, 1m, 1m)));
        Assert.Contains("vigência", ex.Message);
    }

    [Fact]
    public async Task Rates_must_be_between_0_and_100()
    {
        var db = TestDb.CreateUnitOfWork();
        await Assert.ThrowsAsync<DefaultException>(() =>
            new IbsCbsRatesService(db).CreateAsync(Rate(2026, -1m, 0.1m)));
    }

    [Fact]
    public async Task Update_and_delete_work()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new IbsCbsRatesService(db);
        var created = await service.CreateAsync(Rate(2026, 0.9m, 0.1m));

        created.CbsRate = 1m;
        await service.UpdateAsync(created.Key, created);
        Assert.Equal(1m, (await service.GetByIdAsync(created.Key))!.CbsRate);

        Assert.True(await service.DeleteAsync(created.Key));
        Assert.Null(await service.GetByIdAsync(created.Key));
    }
}
```

```csharp
// SiagroB1.Application.Tests/Taxes/TaxationEdmModelTests.cs
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Taxes;

/// <summary>O EDM real expõe o que a tela de tributação consome.</summary>
public class TaxationEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void IbsCbsRates_entity_set_uses_edm_date_for_start_date()
    {
        var set = Model().EntityContainer.FindEntitySet("IbsCbsRates");
        Assert.NotNull(set);
        var start = set!.EntityType.FindProperty("StartDate");
        Assert.Equal("Edm.Date", start.Type.FullName());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~IbsCbsRates|FullyQualifiedName~TaxationEdmModelTests"` → FAIL (compile).

- [ ] **Step 3: Write the implementation**

```csharp
// SiagroB1.Domain/Entities/IbsCbsRate.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Alíquotas de IBS e CBS a partir de uma data. São nacionais e mudam por ano durante a
/// transição da reforma tributária; a virada de ano é um cadastro só, em vez de editar cada
/// natureza. Vale para um documento a linha com o maior <see cref="StartDate"/> menor ou igual
/// à data de emissão. Só STANDALONE.
/// </summary>
[Table("IBS_CBS_RATES")]
[Index(nameof(StartDate), IsUnique = true)]
public class IbsCbsRate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Key { get; set; }

    /// <summary><c>DateOnly</c> ⇒ <c>Edm.Date</c>: sem fuso, a vigência não escorrega de dia.</summary>
    [Column(TypeName = "DATE")]
    public DateOnly StartDate { get; set; }

    [Column(TypeName = "DECIMAL(7,4)")]
    public decimal CbsRate { get; set; }

    [Column(TypeName = "DECIMAL(7,4)")]
    public decimal IbsStateRate { get; set; }

    [Column(TypeName = "DECIMAL(7,4)")]
    public decimal IbsMunicipalRate { get; set; }
}
```

`AppDbContext.cs`: `public DbSet<IbsCbsRate> IbsCbsRates { get; set; }` (perto de `UsageEffects`).

```csharp
// SiagroB1.Application/Services/Taxes/IbsCbsRatesService.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>Cadastro das alíquotas de IBS/CBS por vigência e a escolha da vigente numa data.</summary>
public class IbsCbsRatesService(IUnitOfWork db)
{
    public IQueryable<IbsCbsRate> QueryAll() => db.Context.IbsCbsRates.AsNoTracking();

    public Task<IbsCbsRate?> GetByIdAsync(int key) =>
        db.Context.IbsCbsRates.FirstOrDefaultAsync(x => x.Key == key);

    public Task<IbsCbsRate?> GetEffectiveAsync(DateOnly date) =>
        db.Context.IbsCbsRates.AsNoTracking()
            .Where(x => x.StartDate <= date)
            .OrderByDescending(x => x.StartDate)
            .FirstOrDefaultAsync();

    public async Task<IbsCbsRate> CreateAsync(IbsCbsRate entity)
    {
        await ValidateAsync(entity, ignoreKey: null);
        entity.Key = 0;
        await db.Context.IbsCbsRates.AddAsync(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<IbsCbsRate?> UpdateAsync(int key, IbsCbsRate entity)
    {
        var existing = await db.Context.IbsCbsRates.FirstOrDefaultAsync(x => x.Key == key);
        if (existing is null) return null;

        await ValidateAsync(entity, ignoreKey: key);

        existing.StartDate = entity.StartDate;
        existing.CbsRate = entity.CbsRate;
        existing.IbsStateRate = entity.IbsStateRate;
        existing.IbsMunicipalRate = entity.IbsMunicipalRate;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<bool> DeleteAsync(int key)
    {
        var existing = await db.Context.IbsCbsRates.FirstOrDefaultAsync(x => x.Key == key);
        if (existing is null) return false;

        db.Context.IbsCbsRates.Remove(existing);
        await db.SaveChangesAsync();
        return true;
    }

    private async Task ValidateAsync(IbsCbsRate entity, int? ignoreKey)
    {
        foreach (var (value, label) in new[]
                 {
                     (entity.CbsRate, "CBS"), (entity.IbsStateRate, "IBS estadual"),
                     (entity.IbsMunicipalRate, "IBS municipal"),
                 })
        {
            if (value is < 0 or > 100)
                throw new DefaultException($"Alíquota de {label} deve estar entre 0 e 100.");
        }

        var duplicated = await db.Context.IbsCbsRates
            .AnyAsync(x => x.StartDate == entity.StartDate && x.Key != ignoreKey);

        if (duplicated)
            throw new DefaultException(
                $"Já existe uma vigência de IBS/CBS iniciando em {entity.StartDate:dd/MM/yyyy}.");
    }
}
```

`IbsCbsRatesController.cs` — `ODataController` com `IbsCbsRatesService service` e `IConfiguration configuration`; ações `Get()` (`[EnableQuery]`), `Get(int key)`, `Post`, `Patch(int key, Delta<IbsCbsRate>)` (carrega, `patch.Patch(t)`, `UpdateAsync`), `Delete(int key)`. **Toda ação começa com**:

```csharp
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest("As alíquotas de IBS/CBS só existem no modo STANDALONE.");
```

`DefaultException` → `BadRequest(ex.Message)`; outras → `StatusCode(500, ex.Message)` (padrão de `UsagesController`).

`ODataConfigurations.cs`: perto de `EntitySet<UsageModel>("Usages")` acrescentar `modelBuilder.EntitySet<IbsCbsRate>("IbsCbsRates");`.

`ServiceCollectionExtensions.cs` (`AddApplicationServices`, perto do bloco de SalesInvoices): `services.AddScoped<IbsCbsRatesService>();`.

- [ ] **Step 4: Run tests** — mesmo comando → PASS.

- [ ] **Step 5: Migration com semente**

Run: `dotnet ef migrations add CreateIbsCbsRates ...`. No fim do `Up()` acrescentar:

```csharp
            // Vigência de 2026 (ano de teste da reforma). Idempotente, como as demais sementes.
            migrationBuilder.Sql(
                "IF NOT EXISTS (SELECT 1 FROM IBS_CBS_RATES WHERE StartDate = '2026-01-01') " +
                "INSERT INTO IBS_CBS_RATES (StartDate, CbsRate, IbsStateRate, IbsMunicipalRate) " +
                "VALUES ('2026-01-01', 0.9, 0.1, 0);");
```

`has-pending-model-changes` → No changes.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Domain/Entities/IbsCbsRate.cs SiagroB1.Application/Services/Taxes/IbsCbsRatesService.cs SiagroB1.Web/Controllers/IbsCbsRatesController.cs SiagroB1.Application.Tests/Taxes/*.cs SiagroB1.Migrations/AppContext/*CreateIbsCbsRates*
git commit -m "feat(invoice): alíquotas de IBS/CBS por vigência" -m "DB: CreateIbsCbsRates" -- <arquivos da task>
```

---

### Task 6: A regra de ativação (`TaxCalculationGate`) e a função `TaxCalculationIsActive`

**Files:**
- Create: `SiagroB1.Application/Services/Taxes/TaxCalculationGate.cs`, `SiagroB1.Web/Actions/Taxes/TaxCalculationIsActiveController.cs`
- Modify: `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`, `TaxTestServices.cs`, `TaxationEdmModelTests.cs`
- Test: `SiagroB1.Application.Tests/Taxes/TaxCalculationGateTests.cs`

**Interfaces:**
- Consumes: `ErpMode.IsStandalone`, `Branch.IssuesNfe`.
- Produces: `TaxCalculationGate(IUnitOfWork, IConfiguration)` com `bool IsStandalone` e `Task<bool> IsActiveAsync(string? branchCode)`; função OData `TaxCalculationIsActive(BranchCode)` → `bool`; `TaxTestServices.Gate(UnitOfWork, string erp)`.

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Application.Tests/Taxes/TaxCalculationGateTests.cs
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Taxes;

/// <summary>
/// A regra única: STANDALONE + chave ligada na filial. A contraprova obrigatória é SAPB1 com a
/// chave gravada à mão no banco — tem de continuar inativa.
/// </summary>
public class TaxCalculationGateTests
{
    private static async Task<Infra.UnitOfWork> DbWithBranch(bool issuesNfe)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "MATRIZ", StateCode = "SP", TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe,
        });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Standalone_with_switch_on_is_active() =>
        Assert.True(await TaxTestServices.Gate(await DbWithBranch(true), "STANDALONE").IsActiveAsync("01"));

    [Fact]
    public async Task Missing_erp_key_counts_as_standalone() =>
        Assert.True(await TaxTestServices.Gate(await DbWithBranch(true), null).IsActiveAsync("01"));

    [Fact]
    public async Task Standalone_with_switch_off_is_inactive() =>
        Assert.False(await TaxTestServices.Gate(await DbWithBranch(false), "STANDALONE").IsActiveAsync("01"));

    [Fact]
    public async Task Sapb1_with_switch_forced_on_is_inactive() =>
        Assert.False(await TaxTestServices.Gate(await DbWithBranch(true), "SAPB1").IsActiveAsync("01"));

    [Fact]
    public async Task Any_other_mode_is_inactive() =>
        Assert.False(await TaxTestServices.Gate(await DbWithBranch(true), "PROTHEUS").IsActiveAsync("01"));

    [Fact]
    public async Task Unknown_or_blank_branch_is_inactive()
    {
        var gate = TaxTestServices.Gate(await DbWithBranch(true), "STANDALONE");
        Assert.False(await gate.IsActiveAsync("99"));
        Assert.False(await gate.IsActiveAsync(null));
        Assert.False(await gate.IsActiveAsync(" "));
    }
}
```

Em `TaxationEdmModelTests` acrescentar:

```csharp
    [Fact]
    public void TaxCalculationIsActive_is_a_function_with_branch_code()
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "TaxCalculationIsActive");
        Assert.Equal("Edm.String", function.Parameters.Single(p => p.Name == "BranchCode").Type.FullName());
        Assert.Equal("Edm.Boolean", function.ReturnType.FullName());
    }
```

Em `TaxTestServices` acrescentar:

```csharp
    public static TaxCalculationGate Gate(UnitOfWork db, string? erp) => new(db, Config(erp));
```

(usings `SiagroB1.Application.Services.Taxes`, `SiagroB1.Infra`.)

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~TaxCalculationGateTests|FullyQualifiedName~TaxationEdmModelTests"` → FAIL.

- [ ] **Step 3: Write the implementation**

```csharp
// SiagroB1.Application/Services/Taxes/TaxCalculationGate.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// A regra única da tributação da NF-e STANDALONE: ativa quando o modo é STANDALONE E a filial
/// do documento tem a chave "Emite NF-e pelo Siagro". Cálculo, trava, guardas e telas perguntam
/// AQUI — ninguém combina modo e chave por conta própria. Em SAPB1 a chave é ignorada mesmo
/// gravada no banco; é isso que garante que nada desta feature reflita na Yokotobi, e a MH
/// Agro (STANDALONE sem NF-e) fica de fora por ter a chave desligada.
/// </summary>
public class TaxCalculationGate(IUnitOfWork db, IConfiguration configuration)
{
    public bool IsStandalone => ErpMode.IsStandalone(configuration);

    public async Task<bool> IsActiveAsync(string? branchCode)
    {
        if (!IsStandalone || string.IsNullOrWhiteSpace(branchCode))
            return false;

        return await db.Context.Branchs
            .AsNoTracking()
            .AnyAsync(b => b.Code == branchCode && b.IssuesNfe);
    }
}
```

```csharp
// SiagroB1.Web/Actions/Taxes/TaxCalculationIsActiveController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Taxes;

namespace SiagroB1.Web.Actions.Taxes;

/// <summary>
/// A tela pergunta ao servidor se a filial do documento calcula tributos, em vez de juntar o
/// modo e a chave por conta própria: a regra continua decidida num lugar só.
/// </summary>
public class TaxCalculationIsActiveController(TaxCalculationGate gate) : ODataController
{
    [HttpGet("odata/TaxCalculationIsActive(BranchCode={branchCode})")]
    public async Task<IActionResult> GetAsync([FromRoute] string branchCode)
    {
        // Rotas por atributo entregam o segmento COM as aspas simples do OData.
        return Ok(await gate.IsActiveAsync(branchCode?.Trim('\'')));
    }
}
```

`ODataConfigurations.cs` (perto de `SalesInvoicesResolveCfop`):

```csharp
        // Regra de ativação da tributação da NF-e STANDALONE, para a tela travar a linha.
        var taxCalculationIsActive = modelBuilder.Function("TaxCalculationIsActive");
        taxCalculationIsActive.Parameter<string>("BranchCode");
        taxCalculationIsActive.Returns<bool>();
```

DI: `services.AddScoped<TaxCalculationGate>();`.

- [ ] **Step 4: Run tests** → PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/Taxes/TaxCalculationGate.cs SiagroB1.Web/Actions/Taxes/TaxCalculationIsActiveController.cs SiagroB1.Application.Tests/Taxes/TaxCalculationGateTests.cs
git commit -m "feat(invoice): regra única de ativação da tributação STANDALONE" -- <arquivos da task>
```

---

### Task 7: O calculador puro (`TaxCalculator`) e a alíquota interestadual

**Files:**
- Create: `SiagroB1.Application/Services/Taxes/InterstateIcmsRate.cs`, `TaxCalculationModels.cs`, `TaxCalculator.cs`
- Test: `SiagroB1.Application.Tests/Taxes/InterstateIcmsRateTests.cs`, `SiagroB1.Application.Tests/Taxes/TaxCalculatorTests.cs`

**Interfaces:**
- Consumes: `FiscalCodes`, `TaxRegime`.
- Produces:

```csharp
public sealed record IcmsRule(string? Cst, string? Csosn, decimal? Rate, decimal? BaseReduction, decimal? Deferral, string? BenefitCode);
public sealed record PisCofinsRule(string PisCst, decimal? PisRate, string CofinsCst, decimal? CofinsRate, bool ExcludeIcmsFromBase);
public sealed record IbsCbsRule(string Cst, string ClassCode, decimal? IbsReduction, decimal? CbsReduction);
public sealed record IbsCbsRates(decimal Cbs, decimal IbsState, decimal IbsMunicipal);
public sealed record TaxCalculationInput(decimal Amount, bool InState, string BranchState, string CustomerState,
    TaxRegime Regime, byte GoodsOrigin, IcmsRule Icms, PisCofinsRule PisCofins, IbsCbsRule? IbsCbs, IbsCbsRates? Rates);
public sealed record TaxCalculationResult(...) // campos abaixo
public static class TaxCalculator { public static TaxCalculationResult Calculate(TaxCalculationInput input); }
public static class InterstateIcmsRate { public static decimal Resolve(string originState, string destinationState, byte goodsOrigin); }
```

- [ ] **Step 1: Write the failing tests**

```csharp
// SiagroB1.Application.Tests/Taxes/InterstateIcmsRateTests.cs
using SiagroB1.Application.Services.Taxes;

namespace SiagroB1.Application.Tests.Taxes;

public class InterstateIcmsRateTests
{
    [Theory]
    [InlineData("SP", "BA", 0, 7)]
    [InlineData("PR", "ES", 0, 7)]
    [InlineData("MG", "GO", 0, 7)]
    [InlineData("SP", "PR", 0, 12)]
    [InlineData("BA", "SP", 0, 12)]
    [InlineData("ES", "BA", 0, 12)]
    [InlineData("GO", "MT", 0, 12)]
    [InlineData("SP", "BA", 1, 4)]
    [InlineData("SP", "PR", 2, 4)]
    [InlineData("BA", "SP", 3, 4)]
    [InlineData("SP", "PR", 8, 4)]
    [InlineData("SP", "BA", 6, 7)]
    [InlineData("SP", "PR", 7, 12)]
    [InlineData("SP", "PR", 5, 12)]
    public void Resolves_the_senate_rate(string origin, string destination, byte goodsOrigin, int expected) =>
        Assert.Equal(expected, InterstateIcmsRate.Resolve(origin, destination, goodsOrigin));

    [Fact]
    public void State_comparison_ignores_case() =>
        Assert.Equal(7m, InterstateIcmsRate.Resolve("sp", "ba", 0));
}
```

```csharp
// SiagroB1.Application.Tests/Taxes/TaxCalculatorTests.cs
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Taxes;

/// <summary>
/// Contas da §6 do spec. A ordem é fixa (ICMS → PIS/COFINS → IBS/CBS) porque cada tributo usa o
/// anterior, e cada valor é arredondado antes de entrar no passo seguinte — é o que fecha com a
/// validação da SEFAZ (vBC do IBS/CBS = vProd − vICMS − vPIS − vCOFINS).
/// </summary>
public class TaxCalculatorTests
{
    private static readonly IbsCbsRates Rates2026 = new(0.9m, 0.1m, 0m);

    private static readonly PisCofinsRule PisCofins = new("01", 1.65m, "01", 7.6m, ExcludeIcmsFromBase: true);

    private static TaxCalculationInput Input(
        IcmsRule icms, bool inState = false, decimal amount = 60000m, TaxRegime regime = TaxRegime.Normal,
        byte origin = 0, PisCofinsRule? pisCofins = null, IbsCbsRule? ibsCbs = null, IbsCbsRates? rates = null,
        string branchState = "SP", string customerState = "BA") =>
        new(amount, inState, branchState, inState ? branchState : customerState, regime, origin, icms,
            pisCofins ?? PisCofins, ibsCbs, rates);

    private static IcmsRule Cst(string cst, decimal? rate = null, decimal? reduction = null, decimal? deferral = null) =>
        new(cst, null, rate, reduction, deferral, null);

    /// <summary>O exemplo de referência da §6.2 do spec, valor por valor.</summary>
    [Fact]
    public void Reference_example_interstate_sale_sp_to_ba()
    {
        var r = TaxCalculator.Calculate(Input(
            Cst("00"), ibsCbs: new IbsCbsRule("000", "000001", null, null), rates: Rates2026));

        Assert.Equal("00", r.IcmsCode);
        Assert.Equal(60000.00m, r.IcmsBase);
        Assert.Equal(7m, r.IcmsRate);
        Assert.Equal(4200.00m, r.IcmsValue);
        Assert.Equal(55800.00m, r.PisBase);
        Assert.Equal(920.70m, r.PisValue);
        Assert.Equal(55800.00m, r.CofinsBase);
        Assert.Equal(4240.80m, r.CofinsValue);
        Assert.Equal(50638.50m, r.IbsCbsBase);
        Assert.Equal(455.75m, r.CbsValue);
        Assert.Equal(50.64m, r.IbsStateValue);
        Assert.Equal(0.00m, r.IbsMunicipalValue);
    }

    [Fact]
    public void Reference_example_in_state_with_full_deferral()
    {
        var r = TaxCalculator.Calculate(Input(Cst("51", rate: 18m, deferral: 100m), inState: true));

        Assert.Equal(60000.00m, r.IcmsBase);
        Assert.Equal(18m, r.IcmsRate);
        Assert.Equal(10800.00m, r.IcmsOperationValue);
        Assert.Equal(100m, r.IcmsDeferral);
        Assert.Equal(10800.00m, r.IcmsDeferredValue);
        Assert.Equal(0.00m, r.IcmsValue);
        // Tema 69 com ICMS zero: a base do PIS é o valor cheio.
        Assert.Equal(60000.00m, r.PisBase);
    }

    [Fact]
    public void Partial_deferral_keeps_the_rest_of_the_icms()
    {
        var r = TaxCalculator.Calculate(Input(Cst("51", rate: 18m, deferral: 33.33m), inState: true, amount: 1000m));

        Assert.Equal(180.00m, r.IcmsOperationValue);
        Assert.Equal(59.99m, r.IcmsDeferredValue);
        Assert.Equal(120.01m, r.IcmsValue);
    }

    [Fact]
    public void Cst_20_reduces_the_base()
    {
        var r = TaxCalculator.Calculate(Input(Cst("20", rate: 18m, reduction: 33.33m), inState: true, amount: 1000m));

        Assert.Equal(666.70m, r.IcmsBase);
        Assert.Equal(33.33m, r.IcmsBaseReduction);
        Assert.Equal(120.01m, r.IcmsValue);
    }

    [Fact]
    public void Cst_90_without_reduction_taxes_the_full_amount()
    {
        var r = TaxCalculator.Calculate(Input(Cst("90", rate: 12m), inState: true, amount: 1000m));
        Assert.Equal(1000.00m, r.IcmsBase);
        Assert.Equal(120.00m, r.IcmsValue);
    }

    [Theory]
    [InlineData("40")]
    [InlineData("41")]
    [InlineData("50")]
    public void Exempt_cst_zeroes_the_icms(string cst)
    {
        var r = TaxCalculator.Calculate(Input(Cst(cst), inState: true));
        Assert.Equal(cst, r.IcmsCode);
        Assert.Equal(0m, r.IcmsBase);
        Assert.Equal(0m, r.IcmsRate);
        Assert.Equal(0m, r.IcmsValue);
    }

    [Fact]
    public void Out_state_ignores_any_rate_and_uses_the_automatic_one()
    {
        var r = TaxCalculator.Calculate(Input(Cst("00", rate: 18m), customerState: "PR"));
        Assert.Equal(12m, r.IcmsRate);
        Assert.Equal(7200.00m, r.IcmsValue);
    }

    [Fact]
    public void Imported_goods_get_four_percent_interstate()
    {
        var r = TaxCalculator.Calculate(Input(Cst("00"), origin: 1));
        Assert.Equal(4m, r.IcmsRate);
        Assert.Equal(2400.00m, r.IcmsValue);
    }

    [Fact]
    public void Simples_nacional_uses_the_csosn_of_the_block()
    {
        var icms = new IcmsRule("00", "102", null, null, null, null);
        var r = TaxCalculator.Calculate(Input(icms, regime: TaxRegime.SimplesNacional));

        Assert.Equal("102", r.IcmsCode);
        Assert.Equal(0m, r.IcmsValue);
    }

    [Fact]
    public void Mei_uses_csosn_and_csosn_900_taxes_like_cst_90()
    {
        var icms = new IcmsRule(null, "900", 18m, 10m, null, null);
        var r = TaxCalculator.Calculate(Input(icms, inState: true, regime: TaxRegime.Mei, amount: 1000m));

        Assert.Equal("900", r.IcmsCode);
        Assert.Equal(900.00m, r.IcmsBase);
        Assert.Equal(162.00m, r.IcmsValue);
    }

    [Fact]
    public void Excess_sublimit_regime_uses_cst()
    {
        var icms = new IcmsRule("00", "102", 18m, null, null, null);
        var r = TaxCalculator.Calculate(Input(icms, inState: true, regime: TaxRegime.SimplesNacionalExcess, amount: 100m));
        Assert.Equal("00", r.IcmsCode);
        Assert.Equal(18.00m, r.IcmsValue);
    }

    [Fact]
    public void Benefit_code_is_copied() =>
        Assert.Equal("SP800001",
            TaxCalculator.Calculate(Input(new IcmsRule("40", null, null, null, null, "SP800001"), inState: true))
                .IcmsBenefitCode);

    [Fact]
    public void Without_tema_69_the_pis_base_is_the_full_amount()
    {
        var r = TaxCalculator.Calculate(Input(Cst("00"), pisCofins: new("01", 1.65m, "01", 7.6m, false)));
        Assert.Equal(60000.00m, r.PisBase);
        Assert.Equal(990.00m, r.PisValue);
        Assert.Equal(4560.00m, r.CofinsValue);
    }

    [Theory]
    [InlineData("04")]
    [InlineData("06")]
    [InlineData("09")]
    public void No_tax_pis_cofins_cst_zeroes_base_and_value(string cst)
    {
        var r = TaxCalculator.Calculate(Input(Cst("00"), pisCofins: new(cst, 1.65m, cst, 7.6m, true)));
        Assert.Equal(0m, r.PisBase);
        Assert.Equal(0m, r.PisRate);
        Assert.Equal(0m, r.PisValue);
        Assert.Equal(0m, r.CofinsBase);
        Assert.Equal(0m, r.CofinsValue);
    }

    [Fact]
    public void Ibs_cbs_reductions_apply_to_their_own_tax()
    {
        var r = TaxCalculator.Calculate(Input(
            Cst("40"), inState: true, amount: 1000m,
            pisCofins: new("09", null, "09", null, false),
            ibsCbs: new IbsCbsRule("200", "200001", IbsReduction: 60m, CbsReduction: 40m),
            rates: new IbsCbsRates(10m, 5m, 5m)));

        Assert.Equal(1000.00m, r.IbsCbsBase);
        Assert.Equal(60.00m, r.CbsValue);        // 1000 × 10% × (1 − 40%)
        Assert.Equal(20.00m, r.IbsStateValue);   // 1000 × 5% × (1 − 60%)
        Assert.Equal(20.00m, r.IbsMunicipalValue);
        Assert.Equal(40m, r.CbsRateReduction);
        Assert.Equal(60m, r.IbsRateReduction);
    }

    [Theory]
    [InlineData("400")]
    [InlineData("410")]
    public void No_tax_ibs_cbs_cst_zeroes_everything(string cst)
    {
        var r = TaxCalculator.Calculate(Input(Cst("00"), ibsCbs: new IbsCbsRule(cst, "410001", null, null), rates: Rates2026));
        Assert.Equal(cst, r.IbsCbsCst);
        Assert.Equal("410001", r.IbsCbsClassCode);
        Assert.Equal(0m, r.IbsCbsBase);
        Assert.Equal(0m, r.CbsRate);
        Assert.Equal(0m, r.CbsValue);
        Assert.Equal(0m, r.IbsStateValue);
    }

    [Fact]
    public void Without_ibs_cbs_rule_the_group_is_empty()
    {
        var r = TaxCalculator.Calculate(Input(Cst("00")));
        Assert.Null(r.IbsCbsCst);
        Assert.Null(r.IbsCbsClassCode);
        Assert.Equal(0m, r.IbsCbsBase);
        Assert.Equal(0m, r.CbsValue);
    }

    [Fact]
    public void Rounding_is_half_away_from_zero()
    {
        // Meio centavo exato: 0,25 × 18% = 0,045 → 0,05 (o arredondamento bancário daria 0,04).
        var r = TaxCalculator.Calculate(Input(Cst("00", rate: 18m), inState: true, amount: 0.25m));
        Assert.Equal(0.05m, r.IcmsValue);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~InterstateIcmsRateTests|FullyQualifiedName~TaxCalculatorTests"` → FAIL.

- [ ] **Step 3: Write the implementation**

```csharp
// SiagroB1.Application/Services/Taxes/InterstateIcmsRate.cs
namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// Alíquota interestadual do ICMS pela Resolução do Senado — lei fixa, não motor de regra:
/// importado (origem 1, 2, 3, 8) = 4% (Res. 13/2012); de S/SE exceto ES para N/NE/CO/ES = 7%
/// (Res. 22/89); demais = 12%. Origens 6 e 7 (importado sem similar, lista CAMEX) ficam fora
/// dos 4% por definição da própria resolução.
/// </summary>
public static class InterstateIcmsRate
{
    private static readonly HashSet<string> SouthSoutheastExceptEs =
        new(StringComparer.OrdinalIgnoreCase) { "PR", "SC", "RS", "SP", "RJ", "MG" };

    private static readonly HashSet<string> NorthNortheastMidwestAndEs = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "AM", "AP", "PA", "RO", "RR", "TO",
        "AL", "BA", "CE", "MA", "PB", "PE", "PI", "RN", "SE",
        "DF", "GO", "MS", "MT", "ES",
    };

    public static decimal Resolve(string originState, string destinationState, byte goodsOrigin)
    {
        if (FiscalCodes.ImportedGoodsOrigins.Contains(goodsOrigin))
            return 4m;

        if (SouthSoutheastExceptEs.Contains(originState) && NorthNortheastMidwestAndEs.Contains(destinationState))
            return 7m;

        return 12m;
    }
}
```

```csharp
// SiagroB1.Application/Services/Taxes/TaxCalculationModels.cs
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>Bloco de ICMS da natureza já escolhido (dentro ou fora do estado).</summary>
public sealed record IcmsRule(
    string? Cst, string? Csosn, decimal? Rate, decimal? BaseReduction, decimal? Deferral, string? BenefitCode);

public sealed record PisCofinsRule(
    string PisCst, decimal? PisRate, string CofinsCst, decimal? CofinsRate, bool ExcludeIcmsFromBase);

public sealed record IbsCbsRule(string Cst, string ClassCode, decimal? IbsReduction, decimal? CbsReduction);

/// <summary>Alíquotas de IBS/CBS vigentes na data de emissão, em percentual.</summary>
public sealed record IbsCbsRates(decimal Cbs, decimal IbsState, decimal IbsMunicipal);

/// <summary>Tudo de que a conta precisa — sem banco, sem configuração.</summary>
public sealed record TaxCalculationInput(
    decimal Amount,
    bool InState,
    string BranchState,
    string CustomerState,
    TaxRegime Regime,
    byte GoodsOrigin,
    IcmsRule Icms,
    PisCofinsRule PisCofins,
    IbsCbsRule? IbsCbs,
    IbsCbsRates? Rates);

/// <summary>A fotografia que vai para a linha do documento.</summary>
public sealed record TaxCalculationResult(
    string IcmsCode,
    decimal IcmsBase,
    decimal IcmsRate,
    decimal IcmsBaseReduction,
    decimal IcmsOperationValue,
    decimal IcmsDeferral,
    decimal IcmsDeferredValue,
    decimal IcmsValue,
    string? IcmsBenefitCode,
    string PisCst,
    decimal PisBase,
    decimal PisRate,
    decimal PisValue,
    string CofinsCst,
    decimal CofinsBase,
    decimal CofinsRate,
    decimal CofinsValue,
    string? IbsCbsCst,
    string? IbsCbsClassCode,
    decimal IbsCbsBase,
    decimal CbsRate,
    decimal CbsRateReduction,
    decimal CbsValue,
    decimal IbsStateRate,
    decimal IbsMunicipalRate,
    decimal IbsRateReduction,
    decimal IbsStateValue,
    decimal IbsMunicipalValue);
```

```csharp
// SiagroB1.Application/Services/Taxes/TaxCalculator.cs
namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// As contas do spec §6, puras. Ordem fixa: ICMS → PIS/COFINS → IBS/CBS, cada base e cada valor
/// arredondado a 2 casas (meio para cima) antes do passo seguinte — PIS usa o ICMS já
/// arredondado e a base do IBS/CBS usa os três já arredondados, que é o que vai no XML e o que a
/// SEFAZ confere. Quem garante que os códigos são conhecidos é o cadastro
/// (<see cref="UsageTaxationValidator"/>) e a guarda do documento.
/// </summary>
public static class TaxCalculator
{
    public static TaxCalculationResult Calculate(TaxCalculationInput input)
    {
        var icms = CalculateIcms(input);

        var pisBase = 0m; var pisRate = 0m; var pisValue = 0m;
        var cofinsBase = 0m; var cofinsRate = 0m; var cofinsValue = 0m;
        var pisCofinsBase = Round(input.Amount - (input.PisCofins.ExcludeIcmsFromBase ? icms.Value : 0m));

        if (!FiscalCodes.PisCofinsCstNoTax.Contains(input.PisCofins.PisCst))
        {
            pisBase = pisCofinsBase;
            pisRate = input.PisCofins.PisRate ?? 0m;
            pisValue = Round(pisBase * pisRate / 100m);
        }

        if (!FiscalCodes.PisCofinsCstNoTax.Contains(input.PisCofins.CofinsCst))
        {
            cofinsBase = pisCofinsBase;
            cofinsRate = input.PisCofins.CofinsRate ?? 0m;
            cofinsValue = Round(cofinsBase * cofinsRate / 100m);
        }

        var ibsCbs = CalculateIbsCbs(input, icms.Value, pisValue, cofinsValue);

        return new TaxCalculationResult(
            icms.Code, icms.Base, icms.Rate, icms.Reduction, icms.OperationValue, icms.Deferral,
            icms.DeferredValue, icms.Value, input.Icms.BenefitCode,
            input.PisCofins.PisCst, pisBase, pisRate, pisValue,
            input.PisCofins.CofinsCst, cofinsBase, cofinsRate, cofinsValue,
            input.IbsCbs?.Cst, input.IbsCbs?.ClassCode, ibsCbs.Base, ibsCbs.CbsRate, ibsCbs.CbsReduction,
            ibsCbs.CbsValue, ibsCbs.IbsStateRate, ibsCbs.IbsMunicipalRate, ibsCbs.IbsReduction,
            ibsCbs.IbsStateValue, ibsCbs.IbsMunicipalValue);
    }

    private sealed record IcmsPart(
        string Code, decimal Base, decimal Rate, decimal Reduction, decimal OperationValue,
        decimal Deferral, decimal DeferredValue, decimal Value);

    private sealed record IbsCbsPart(
        decimal Base, decimal CbsRate, decimal CbsReduction, decimal CbsValue,
        decimal IbsStateRate, decimal IbsMunicipalRate, decimal IbsReduction,
        decimal IbsStateValue, decimal IbsMunicipalValue);

    private static IcmsPart CalculateIcms(TaxCalculationInput input)
    {
        var code = (FiscalCodes.UsesCsosn(input.Regime) ? input.Icms.Csosn : input.Icms.Cst)
                   ?? throw new InvalidOperationException("Código de ICMS ausente — a guarda deveria ter barrado.");

        var noTax = FiscalCodes.IcmsCstNoTax.Contains(code) || FiscalCodes.IcmsCsosnNoTax.Contains(code);
        if (noTax)
            return new IcmsPart(code, 0m, 0m, 0m, 0m, 0m, 0m, 0m);

        var rate = input.InState
            ? input.Icms.Rate ?? 0m
            : InterstateIcmsRate.Resolve(input.BranchState, input.CustomerState, input.GoodsOrigin);

        var reduction = FiscalCodes.IcmsCodesWithReduction.Contains(code) ? input.Icms.BaseReduction ?? 0m : 0m;
        var icmsBase = Round(input.Amount * (1m - reduction / 100m));

        if (code == "51")
        {
            var deferral = input.Icms.Deferral ?? 0m;
            var operation = Round(icmsBase * rate / 100m);
            var deferred = Round(operation * deferral / 100m);
            return new IcmsPart(code, icmsBase, rate, reduction, operation, deferral, deferred, operation - deferred);
        }

        return new IcmsPart(code, icmsBase, rate, reduction, 0m, 0m, 0m, Round(icmsBase * rate / 100m));
    }

    private static IbsCbsPart CalculateIbsCbs(TaxCalculationInput input, decimal icms, decimal pis, decimal cofins)
    {
        if (input.IbsCbs is null || FiscalCodes.IbsCbsCstNoTax.Contains(input.IbsCbs.Cst))
            return new IbsCbsPart(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);

        var rates = input.Rates
                    ?? throw new InvalidOperationException("Alíquotas de IBS/CBS ausentes — a guarda deveria ter barrado.");

        var ibsReduction = input.IbsCbs.IbsReduction ?? 0m;
        var cbsReduction = input.IbsCbs.CbsReduction ?? 0m;
        var taxBase = Round(input.Amount - icms - pis - cofins);

        decimal Tax(decimal rate, decimal reduction) => Round(taxBase * rate / 100m * (1m - reduction / 100m));

        return new IbsCbsPart(
            taxBase,
            rates.Cbs, cbsReduction, Tax(rates.Cbs, cbsReduction),
            rates.IbsState, rates.IbsMunicipal, ibsReduction,
            Tax(rates.IbsState, ibsReduction), Tax(rates.IbsMunicipal, ibsReduction));
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
```

- [ ] **Step 4: Run tests** → PASS. Se um valor esperado divergir por centavo, **recalcular à mão antes de mexer no teste** (os esperados vieram da spec §6.2 e de contas manuais com arredondamento a cada passo).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/Taxes/InterstateIcmsRate.cs SiagroB1.Application/Services/Taxes/TaxCalculationModels.cs SiagroB1.Application/Services/Taxes/TaxCalculator.cs SiagroB1.Application.Tests/Taxes/InterstateIcmsRateTests.cs SiagroB1.Application.Tests/Taxes/TaxCalculatorTests.cs
git commit -m "feat(invoice): calcular ICMS, PIS/COFINS e IBS/CBS a partir da natureza" -- <arquivos da task>
```

---

### Task 8: Fotografia na linha do documento de saída (colunas + totais)

**Files:**
- Modify: `SiagroB1.Domain/Entities/SalesInvoiceItem.cs`, `SiagroB1.Domain/Entities/SalesInvoice.cs`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceTaxSnapshot.cs`
- Create (gerada + **editada**): migration `AddSalesInvoiceItemTaxSnapshot`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceTaxSnapshotTests.cs`, `TaxationEdmModelTests.cs`

**Interfaces:**
- Consumes: `TaxCalculationResult`.
- Produces: colunas da spec §5.6 em `SalesInvoiceItem` (`GoodsOrigin`, `IcmsBaseReduction`, `IcmsDeferral`, `IcmsOperationValue`, `IcmsDeferredValue`, `IcmsBenefitCode`, `IbsCbsCst`, `IbsCbsClassCode`, `IbsCbsBase`, `CbsRate`, `CbsRateReduction`, `CbsValue`, `IbsStateRate`, `IbsMunicipalRate`, `IbsRateReduction`, `IbsStateValue`, `IbsMunicipalValue`, `MovesFiscalInventory`, `CreatesFinancialDocument`); `[NotMapped] TotalIbsCbs`; `SalesInvoice.TotalInvoiceIbsCbs`; `SalesInvoiceTaxSnapshot.Write(SalesInvoiceItem, TaxCalculationResult)`, `SalesInvoiceTaxSnapshot.LockedProperties : IReadOnlyList<string>`, `SalesInvoiceTaxSnapshot.RestoreLocked(EntityEntry<SalesInvoiceItem>)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceTaxSnapshotTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.SalesInvoices;

public class SalesInvoiceTaxSnapshotTests
{
    private static readonly TaxCalculationResult Result = new(
        "51", 1000m, 18m, 10m, 162m, 100m, 162m, 0m, "SP800001",
        "01", 1000m, 1.65m, 16.5m, "01", 1000m, 7.6m, 76m,
        "000", "000001", 907.5m, 0.9m, 0m, 8.17m, 0.1m, 0m, 0m, 0.91m, 0m);

    private static SalesInvoiceItem Item() => new()
    {
        Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 100m, UnitPrice = 10m,
    };

    [Fact]
    public void Write_copies_every_value()
    {
        var item = Item();
        SalesInvoiceTaxSnapshot.Write(item, Result);

        Assert.Equal("51", item.CstIcms);
        Assert.Equal(1000m, item.IcmsBase);
        Assert.Equal(18m, item.IcmsRate);
        Assert.Equal(10m, item.IcmsBaseReduction);
        Assert.Equal(162m, item.IcmsOperationValue);
        Assert.Equal(100m, item.IcmsDeferral);
        Assert.Equal(162m, item.IcmsDeferredValue);
        Assert.Equal(0m, item.IcmsValue);
        Assert.Equal("SP800001", item.IcmsBenefitCode);
        Assert.Equal("01", item.CstPis);
        Assert.Equal(16.5m, item.PisValue);
        Assert.Equal(76m, item.CofinsValue);
        Assert.Equal("000", item.IbsCbsCst);
        Assert.Equal("000001", item.IbsCbsClassCode);
        Assert.Equal(907.5m, item.IbsCbsBase);
        Assert.Equal(8.17m, item.CbsValue);
        Assert.Equal(0.91m, item.IbsStateValue);
        Assert.Equal(9.08m, item.TotalIbsCbs);
        // IBS/CBS informativo em 2026: fora do total de impostos de hoje.
        Assert.Equal(92.5m, item.TotalTaxes);
    }

    [Fact]
    public async Task Restore_locked_puts_back_the_stored_values()
    {
        var db = TestDb.CreateUnitOfWork();
        var item = Item();
        SalesInvoiceTaxSnapshot.Write(item, Result);
        db.Context.SalesInvoicesItems.Add(item);
        await db.SaveChangesAsync();

        item.IcmsValue = 999m;
        item.Cfop = "9999";
        item.CostCenterCode = "CC01";

        SalesInvoiceTaxSnapshot.RestoreLocked(db.Context.Entry(item));

        Assert.Equal(0m, item.IcmsValue);
        Assert.Null(item.Cfop);
        // Centro de custo não é travado.
        Assert.Equal("CC01", item.CostCenterCode);
    }

    [Fact]
    public void Invoice_total_ibs_cbs_sums_the_lines()
    {
        var a = Item(); SalesInvoiceTaxSnapshot.Write(a, Result);
        var b = Item(); SalesInvoiceTaxSnapshot.Write(b, Result);
        var invoice = new SalesInvoice { CardCode = "C1", Items = [a, b] };

        Assert.Equal(18.16m, invoice.TotalInvoiceIbsCbs);
    }
}
```

Em `TaxationEdmModelTests`:

```csharp
    [Fact]
    public void Ibs_cbs_totals_are_exposed()
    {
        var model = Model();
        Assert.NotNull(model.EntityContainer.FindEntitySet("SalesInvoicesItems")!.EntityType.FindProperty("TotalIbsCbs"));
        Assert.NotNull(model.EntityContainer.FindEntitySet("SalesInvoices")!.EntityType.FindProperty("TotalInvoiceIbsCbs"));
    }
```

- [ ] **Step 2: Run to verify it fails** → FAIL (compile).

- [ ] **Step 3: Write the implementation**

`SalesInvoiceItem.cs` — depois de `CofinsValue` (antes de `CostCenterCode`):

```csharp
    // --- Fotografia do cálculo de tributos da NF-e STANDALONE (só com a regra ativa). ---

    /// <summary>Origem da mercadoria copiada do produto na gravação.</summary>
    [Column(TypeName = "TINYINT")]
    public byte? GoodsOrigin { get; set; }

    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IcmsBaseReduction { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IcmsDeferral { get; set; }

    /// <summary>ICMS da operação (vICMSOp) — só no CST 51.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IcmsOperationValue { get; set; }

    /// <summary>ICMS diferido (vICMSDif) — só no CST 51.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IcmsDeferredValue { get; set; }

    [Column(TypeName = "VARCHAR(10)")] public string? IcmsBenefitCode { get; set; }

    [Column(TypeName = "VARCHAR(3)")] public string? IbsCbsCst { get; set; }
    [Column(TypeName = "VARCHAR(6)")] public string? IbsCbsClassCode { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IbsCbsBase { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal CbsRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal CbsRateReduction { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal CbsValue { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IbsStateRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IbsMunicipalRate { get; set; }
    [Column(TypeName = "DECIMAL(7,4) DEFAULT 0")] public decimal IbsRateReduction { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IbsStateValue { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IbsMunicipalValue { get; set; }

    /// <summary>Cópia das flags da natureza na gravação — sem efeito por ora (spec D14).</summary>
    public bool MovesFiscalInventory { get; set; }

    /// <summary>Cópia das flags da natureza na gravação — sem efeito por ora (spec D14).</summary>
    public bool CreatesFinancialDocument { get; set; }
```

e trocar os três `[Column(TypeName = "DECIMAL(5,4) DEFAULT 0")]` de `IcmsRate`, `PisRate`, `CofinsRate` por `DECIMAL(7,4) DEFAULT 0` (comentário: "percentual, como na NF-e: 18% = 18,0000"). Depois de `TotalTaxes`:

```csharp
    /// <summary>
    /// IBS + CBS da linha. Separado de <see cref="TotalTaxes"/> porque em 2026 é informativo e
    /// não compõe o total do documento.
    /// </summary>
    [NotMapped]
    public decimal TotalIbsCbs => CbsValue + IbsStateValue + IbsMunicipalValue;
```

`SalesInvoice.cs`, depois de `TotalInvoiceTaxes`:

```csharp
    /// <summary>IBS + CBS do documento. Informativo em 2026 — fora de <see cref="TotalInvoiceTaxes"/>.</summary>
    [NotMapped]
    public decimal TotalInvoiceIbsCbs => Items.Sum(i => i.TotalIbsCbs);
```

`ODataConfigurations.cs`: junto dos `AddProperty` existentes (linhas ~165 e ~178), acrescentar `AddProperty(typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.TotalInvoiceIbsCbs)))` e `AddProperty(typeof(SalesInvoiceItem).GetProperty(nameof(SalesInvoiceItem.TotalIbsCbs)))` no mesmo formato.

```csharp
// SiagroB1.Application/Services/SalesInvoices/SalesInvoiceTaxSnapshot.cs
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// A fotografia dos tributos na linha: o que o cálculo grava e o que fica TRAVADO com a regra
/// ativa. A lista de campos travados mora aqui para a trava e a escrita não divergirem.
/// </summary>
public static class SalesInvoiceTaxSnapshot
{
    public static readonly IReadOnlyList<string> LockedProperties =
    [
        nameof(SalesInvoiceItem.Cfop), nameof(SalesInvoiceItem.Ncm), nameof(SalesInvoiceItem.GoodsOrigin),
        nameof(SalesInvoiceItem.CstIcms), nameof(SalesInvoiceItem.IcmsBase), nameof(SalesInvoiceItem.IcmsRate),
        nameof(SalesInvoiceItem.IcmsValue), nameof(SalesInvoiceItem.IcmsBaseReduction),
        nameof(SalesInvoiceItem.IcmsDeferral), nameof(SalesInvoiceItem.IcmsOperationValue),
        nameof(SalesInvoiceItem.IcmsDeferredValue), nameof(SalesInvoiceItem.IcmsBenefitCode),
        nameof(SalesInvoiceItem.CstPis), nameof(SalesInvoiceItem.PisBase), nameof(SalesInvoiceItem.PisRate),
        nameof(SalesInvoiceItem.PisValue), nameof(SalesInvoiceItem.CstCofins), nameof(SalesInvoiceItem.CofinsBase),
        nameof(SalesInvoiceItem.CofinsRate), nameof(SalesInvoiceItem.CofinsValue),
        nameof(SalesInvoiceItem.IbsCbsCst), nameof(SalesInvoiceItem.IbsCbsClassCode),
        nameof(SalesInvoiceItem.IbsCbsBase), nameof(SalesInvoiceItem.CbsRate),
        nameof(SalesInvoiceItem.CbsRateReduction), nameof(SalesInvoiceItem.CbsValue),
        nameof(SalesInvoiceItem.IbsStateRate), nameof(SalesInvoiceItem.IbsMunicipalRate),
        nameof(SalesInvoiceItem.IbsRateReduction), nameof(SalesInvoiceItem.IbsStateValue),
        nameof(SalesInvoiceItem.IbsMunicipalValue), nameof(SalesInvoiceItem.MovesFiscalInventory),
        nameof(SalesInvoiceItem.CreatesFinancialDocument),
    ];

    public static void Write(SalesInvoiceItem item, TaxCalculationResult r)
    {
        item.CstIcms = r.IcmsCode;
        item.IcmsBase = r.IcmsBase;
        item.IcmsRate = r.IcmsRate;
        item.IcmsBaseReduction = r.IcmsBaseReduction;
        item.IcmsOperationValue = r.IcmsOperationValue;
        item.IcmsDeferral = r.IcmsDeferral;
        item.IcmsDeferredValue = r.IcmsDeferredValue;
        item.IcmsValue = r.IcmsValue;
        item.IcmsBenefitCode = r.IcmsBenefitCode;

        item.CstPis = r.PisCst;
        item.PisBase = r.PisBase;
        item.PisRate = r.PisRate;
        item.PisValue = r.PisValue;
        item.CstCofins = r.CofinsCst;
        item.CofinsBase = r.CofinsBase;
        item.CofinsRate = r.CofinsRate;
        item.CofinsValue = r.CofinsValue;

        item.IbsCbsCst = r.IbsCbsCst;
        item.IbsCbsClassCode = r.IbsCbsClassCode;
        item.IbsCbsBase = r.IbsCbsBase;
        item.CbsRate = r.CbsRate;
        item.CbsRateReduction = r.CbsRateReduction;
        item.CbsValue = r.CbsValue;
        item.IbsStateRate = r.IbsStateRate;
        item.IbsMunicipalRate = r.IbsMunicipalRate;
        item.IbsRateReduction = r.IbsRateReduction;
        item.IbsStateValue = r.IbsStateValue;
        item.IbsMunicipalValue = r.IbsMunicipalValue;
    }

    /// <summary>
    /// Documento que não está mais Pendente (ex.: a Conferência de entregas editando um item
    /// Confirmado): os campos travados voltam ao que está gravado, venha o que vier no corpo.
    /// </summary>
    public static void RestoreLocked(EntityEntry<SalesInvoiceItem> entry)
    {
        foreach (var name in LockedProperties)
        {
            var property = entry.Property(name);
            property.CurrentValue = property.OriginalValue;
        }
    }
}
```

- [ ] **Step 4: Run tests** → PASS.

- [ ] **Step 5: Migration — editar o alargamento à mão**

Run: `dotnet ef migrations add AddSalesInvoiceItemTaxSnapshot ...`. O EF vai gerar `AlterColumn` com `type: "DECIMAL(7,4) DEFAULT 0"` para `IcmsRate`, `PisRate` e `CofinsRate` — **SQL inválido** (`DEFAULT` não cabe em `ALTER COLUMN`) e o default existente bloqueia a troca de tipo. Substituir cada um dos três `AlterColumn` do `Up()` por:

```csharp
            WidenRate(migrationBuilder, "IcmsRate");
            WidenRate(migrationBuilder, "PisRate");
            WidenRate(migrationBuilder, "CofinsRate");
```

e no `Down()` por `NarrowRate(...)` equivalente com `DECIMAL(5,4)`; acrescentar na classe:

```csharp
        /// <summary>
        /// As alíquotas passam a guardar percentual (18% = 18,0000), que não cabe em DECIMAL(5,4).
        /// O default da coluna depende dela e impede o ALTER, então sai e volta. Só alarga:
        /// nenhum valor gravado muda.
        /// </summary>
        private static void WidenRate(MigrationBuilder migrationBuilder, string column) =>
            ChangeRatePrecision(migrationBuilder, column, "DECIMAL(7,4)");

        private static void NarrowRate(MigrationBuilder migrationBuilder, string column) =>
            ChangeRatePrecision(migrationBuilder, column, "DECIMAL(5,4)");

        private static void ChangeRatePrecision(MigrationBuilder migrationBuilder, string column, string type) =>
            migrationBuilder.Sql($@"
DECLARE @df sysname;
SELECT @df = dc.name FROM sys.default_constraints dc
JOIN sys.columns c ON c.default_object_id = dc.object_id
WHERE dc.parent_object_id = OBJECT_ID('SALES_INVOICES_ITEMS') AND c.name = '{column}';
IF @df IS NOT NULL EXEC('ALTER TABLE SALES_INVOICES_ITEMS DROP CONSTRAINT [' + @df + ']');
ALTER TABLE SALES_INVOICES_ITEMS ALTER COLUMN [{column}] {type} NOT NULL;
ALTER TABLE SALES_INVOICES_ITEMS ADD DEFAULT 0 FOR [{column}];");
```

Conferir os `AddColumn` das colunas novas (com `DEFAULT 0` no tipo, como as existentes). `has-pending-model-changes` → No changes. Gerar o script para conferir: `dotnet ef migrations script AddItemGoodsOriginAndNcm AddSalesInvoiceItemTaxSnapshot --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` e ler o SQL.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/SalesInvoices/SalesInvoiceTaxSnapshot.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceTaxSnapshotTests.cs SiagroB1.Migrations/AppContext/*AddSalesInvoiceItemTaxSnapshot*
git commit -m "feat(invoice): gravar a fotografia dos tributos na linha do documento de saída" -m "DB: AddSalesInvoiceItemTaxSnapshot" -- <arquivos da task>
```

---

### Task 9: O orquestrador `SalesInvoicesTaxApplyService` (guardas + gravação)

**Files:**
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs`
- Modify: `SalesInvoicesCfopResolveService.cs` (`ResolvePartnerState` passa a `internal static`), `ServiceCollectionExtensions.cs`, `TaxTestServices.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesTaxApplyServiceTests.cs`

**Interfaces:**
- Consumes: `TaxCalculationGate`, `IUsage`, `IBusinessPartnerService`, `IbsCbsRatesService`, `TaxCalculator`, `SalesInvoiceTaxSnapshot`, `SalesInvoicesCfopResolveService.ResolvePartnerState`.
- Produces: `SalesInvoicesTaxApplyService(IUnitOfWork, TaxCalculationGate, IUsage, IBusinessPartnerService, IbsCbsRatesService)` com `Task<bool> IsActiveForAsync(SalesInvoice)` (Normal + gate) e `Task ApplyAsync(SalesInvoice, IEnumerable<SalesInvoiceItem>)` (no-op se inativo ou se o status não for nulo/Pendente); `TaxTestServices.Apply(UnitOfWork, IBusinessPartnerService, string? erp = "STANDALONE")` e `TaxTestServices.InactiveApply(UnitOfWork)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesTaxApplyServiceTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Orquestração do cálculo: carrega natureza, filial, cliente, produto e alíquota, barra o que
/// falta com mensagem de negócio e grava a fotografia. Só age com a regra ativa.
/// </summary>
public class SalesInvoicesTaxApplyServiceTests
{
    private const string CardBa = "C-BA";
    private const string CardSp = "C-SP";

    private static FakeBusinessPartnerService Partners() => new(
        names: new() { [CardBa] = "CLIENTE BA", [CardSp] = "CLIENTE SP", ["C-SEM-UF"] = "SEM UF" },
        states: new() { [CardBa] = "BA", [CardSp] = "SP" });

    private static async Task<(UnitOfWork db, int usageCode)> Seed(
        bool issuesNfe = true, TaxRegime? regime = TaxRegime.Normal, byte? origin = 0, string? ncm = "12019000",
        bool withRate = true, Action<UsageModel>? tweak = null)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "MATRIZ", StateCode = "SP", TaxRegime = regime, IssuesNfe = issuesNfe,
        });
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA", GoodsOrigin = origin, Ncm = ncm });
        if (withRate)
            db.Context.IbsCbsRates.Add(new IbsCbsRate
            {
                StartDate = new DateOnly(2026, 1, 1), CbsRate = 0.9m, IbsStateRate = 0.1m, IbsMunicipalRate = 0m,
            });
        await db.SaveChangesAsync();

        var usage = new UsageModel
        {
            Name = "Venda de grãos", Direction = UsageDirection.Outgoing,
            CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
            IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m,
            IcmsOutStateCst = "00",
            PisCst = "01", PisRate = 1.65m, CofinsCst = "01", CofinsRate = 7.6m,
            ExcludeIcmsFromPisCofinsBase = true,
            IbsCbsCst = "000", IbsCbsClassCode = "000001",
            MovesFiscalInventory = true, CreatesFinancialDocument = true,
            RequiresQuantity = true, IsDefault = true,
        };
        tweak?.Invoke(usage);
        var created = await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(usage);

        return (db, created.Code);
    }

    private static SalesInvoice Invoice(string cardCode = CardBa, int? usageCode = null) => new()
    {
        Key = Guid.NewGuid(), BranchCode = "01", CardCode = cardCode, InvoiceDate = new DateTime(2026, 10, 1),
        Items =
        [
            new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG",
                Quantity = 30000m, UnitPrice = 2m, UsageCode = usageCode,
            },
        ],
    };

    private static async Task<SalesInvoiceItem> Apply(UnitOfWork db, SalesInvoice invoice)
    {
        await TaxTestServices.Apply(db, Partners()).ApplyAsync(invoice, invoice.Items);
        return invoice.Items.Single();
    }

    private static async Task Rejects(UnitOfWork db, SalesInvoice invoice, string expected)
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            TaxTestServices.Apply(db, Partners()).ApplyAsync(invoice, invoice.Items));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Interstate_line_gets_the_reference_example_snapshot()
    {
        var (db, code) = await Seed();
        var item = await Apply(db, Invoice(CardBa, code));

        Assert.Equal("6102", item.Cfop);
        Assert.Equal("12019000", item.Ncm);
        Assert.Equal((byte)0, item.GoodsOrigin);
        Assert.Equal("00", item.CstIcms);
        Assert.Equal(7m, item.IcmsRate);
        Assert.Equal(4200.00m, item.IcmsValue);
        Assert.Equal(920.70m, item.PisValue);
        Assert.Equal(4240.80m, item.CofinsValue);
        Assert.Equal(455.75m, item.CbsValue);
        Assert.Equal(50.64m, item.IbsStateValue);
        Assert.True(item.MovesFiscalInventory);
        Assert.True(item.CreatesFinancialDocument);
        Assert.Equal("Venda de grãos", item.UsageName);
    }

    [Fact]
    public async Task In_state_line_uses_the_in_state_block()
    {
        var (db, code) = await Seed();
        var item = await Apply(db, Invoice(CardSp, code));

        Assert.Equal("5102", item.Cfop);
        Assert.Equal("51", item.CstIcms);
        Assert.Equal(10800.00m, item.IcmsDeferredValue);
        Assert.Equal(0m, item.IcmsValue);
    }

    [Fact]
    public async Task Line_without_usage_falls_back_to_the_default()
    {
        var (db, code) = await Seed();
        var item = await Apply(db, Invoice(CardBa, usageCode: null));
        Assert.Equal(code, item.UsageCode);
    }

    [Fact]
    public async Task Line_without_usage_and_without_default_is_rejected()
    {
        var (db, _) = await Seed(tweak: u => u.IsDefault = false);
        await Rejects(db, Invoice(CardBa, usageCode: null), "sem natureza de operação");
    }

    [Fact]
    public async Task Incoming_usage_is_rejected_on_a_sales_invoice()
    {
        var (db, _) = await Seed();
        var incoming = await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Compra de produtor", Direction = UsageDirection.Incoming, CfopIncomingInState = "1102",
            PisCst = "50", CofinsCst = "50", IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m,
            RequiresQuantity = true,
        });

        await Rejects(db, Invoice(CardSp, incoming.Code), "é de entrada");
    }

    [Fact]
    public async Task Usage_without_icms_code_for_the_block_is_rejected()
    {
        var (db, code) = await Seed(tweak: u => u.IcmsOutStateCst = null);
        await Rejects(db, Invoice(CardBa, code), "sem CST de ICMS fora do estado");
    }

    [Fact]
    public async Task Simples_nacional_requires_the_csosn()
    {
        var (db, code) = await Seed(regime: TaxRegime.SimplesNacional);
        await Rejects(db, Invoice(CardBa, code), "sem CSOSN de ICMS fora do estado");
    }

    [Fact]
    public async Task Usage_without_pis_cofins_cst_is_rejected()
    {
        var (db, code) = await Seed(tweak: u => u.CofinsCst = null);
        await Rejects(db, Invoice(CardBa, code), "CST de PIS/COFINS");
    }

    [Fact]
    public async Task Branch_without_tax_regime_is_rejected()
    {
        var (db, code) = await Seed(regime: null);
        await Rejects(db, Invoice(CardBa, code), "regime tributário");
    }

    [Fact]
    public async Task Customer_without_state_is_rejected()
    {
        var (db, code) = await Seed();
        await Rejects(db, Invoice("C-SEM-UF", code), "sem UF");
    }

    [Fact]
    public async Task Product_without_ncm_is_rejected()
    {
        var (db, code) = await Seed(ncm: null);
        await Rejects(db, Invoice(CardBa, code), "sem NCM");
    }

    [Fact]
    public async Task Product_without_origin_is_rejected_even_in_state()
    {
        var (db, code) = await Seed(origin: null);
        await Rejects(db, Invoice(CardSp, code), "sem origem");
    }

    [Fact]
    public async Task Date_before_the_first_ibs_cbs_validity_is_rejected()
    {
        var (db, code) = await Seed(withRate: false);
        await Rejects(db, Invoice(CardBa, code), "01/10/2026");
    }

    [Fact]
    public async Task Missing_ibs_cbs_rate_is_fine_when_the_usage_has_no_ibs_cbs()
    {
        var (db, code) = await Seed(withRate: false, tweak: u => { u.IbsCbsCst = null; u.IbsCbsClassCode = null; });
        var item = await Apply(db, Invoice(CardBa, code));
        Assert.Null(item.IbsCbsCst);
        Assert.Equal(0m, item.CbsValue);
    }

    [Fact]
    public async Task Inactive_rule_leaves_typed_values_untouched()
    {
        var (db, code) = await Seed(issuesNfe: false);
        var invoice = Invoice(CardBa, code);
        invoice.Items.Single().IcmsValue = 123m;
        invoice.Items.Single().Cfop = "6949";

        var item = await Apply(db, invoice);

        Assert.Equal(123m, item.IcmsValue);
        Assert.Equal("6949", item.Cfop);
    }

    [Fact]
    public async Task Sapb1_with_switch_forced_on_does_nothing()
    {
        var (db, code) = await Seed();
        var invoice = Invoice(CardBa, code);
        invoice.Items.Single().IcmsValue = 123m;

        await TaxTestServices.Apply(db, Partners(), "SAPB1").ApplyAsync(invoice, invoice.Items);

        Assert.Equal(123m, invoice.Items.Single().IcmsValue);
    }

    [Fact]
    public async Task Return_invoices_are_skipped()
    {
        var (db, code) = await Seed();
        var invoice = Invoice(CardBa, code);
        invoice.InvoiceType = SalesInvoiceType.Return;
        invoice.Items.Single().IcmsValue = 123m;

        var item = await Apply(db, invoice);
        Assert.Equal(123m, item.IcmsValue);
    }

    [Fact]
    public async Task Confirmed_invoices_are_not_recalculated()
    {
        var (db, code) = await Seed();
        var invoice = Invoice(CardBa, code);
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        invoice.Items.Single().IcmsValue = 123m;

        var item = await Apply(db, invoice);
        Assert.Equal(123m, item.IcmsValue);
    }
}
```

Em `TaxTestServices` acrescentar:

```csharp
    public static SalesInvoicesTaxApplyService Apply(
        UnitOfWork db, IBusinessPartnerService partners, string? erp = "STANDALONE") =>
        new(db, Gate(db, erp), new UsageService(db, NullLogger<UsageService>.Instance), partners,
            new IbsCbsRatesService(db));

    /// <summary>Regra sempre inativa (modo SAPB1) — para os testes antigos, que não exercitam a tributação.</summary>
    public static SalesInvoicesTaxApplyService InactiveApply(UnitOfWork db) =>
        Apply(db, new FakeBusinessPartnerService(), "SAPB1");
```

- [ ] **Step 2: Run to verify it fails** → FAIL (compile).

- [ ] **Step 3: Write the implementation**

Em `SalesInvoicesCfopResolveService.cs`, `private static string? ResolvePartnerState` → `internal static string? ResolvePartnerState` (comentário: "reaproveitado pelo cálculo de tributos — a mesma UF tem de decidir CFOP e bloco de ICMS").

```csharp
// SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Cálculo dos tributos da linha pela natureza de operação (NF-e STANDALONE, spec §6–§7).
///
/// Só age com a regra ativa (<see cref="TaxCalculationGate"/>), em documento Normal e enquanto o
/// documento está Pendente: fora disso a linha fica exatamente como chegou — é isso que mantém a
/// Yokotobi (SAPB1) e a MH Agro (chave desligada) intocadas.
///
/// Toda lacuna de cadastro é <see cref="DefaultException"/> com mensagem de negócio: com a linha
/// travada, nota sem imposto em silêncio é o pior resultado possível.
/// </summary>
public class SalesInvoicesTaxApplyService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    IUsage usageService,
    IBusinessPartnerService businessPartnerService,
    IbsCbsRatesService ibsCbsRatesService)
{
    public async Task<bool> IsActiveForAsync(SalesInvoice invoice) =>
        invoice.InvoiceType == SalesInvoiceType.Normal && await gate.IsActiveAsync(invoice.BranchCode);

    public async Task ApplyAsync(SalesInvoice invoice, IEnumerable<SalesInvoiceItem> items)
    {
        if (invoice.InvoiceStatus is not (null or InvoiceStatus.Pending))
            return;

        if (!await IsActiveForAsync(invoice))
            return;

        var branch = await LoadBranchAsync(invoice.BranchCode);
        var customerState = await LoadCustomerStateAsync(invoice.CardCode);
        var inState = string.Equals(branch.StateCode, customerState, StringComparison.OrdinalIgnoreCase);
        var issueDate = DateOnly.FromDateTime(invoice.InvoiceDate ?? DateTime.Today);
        var usages = new Dictionary<int, UsageModel>();

        foreach (var item in items)
        {
            var usage = await ResolveUsageAsync(item, usages);
            var product = await LoadProductAsync(item.ItemCode);
            var cfop = ResolveCfop(usage, inState);
            var icms = ResolveIcmsRule(usage, inState, branch.TaxRegime!.Value);
            var pisCofins = ResolvePisCofinsRule(usage);
            var (ibsCbs, rates) = await ResolveIbsCbsAsync(usage, issueDate);

            var result = TaxCalculator.Calculate(new TaxCalculationInput(
                item.Total, inState, branch.StateCode!, customerState, branch.TaxRegime!.Value,
                product.GoodsOrigin!.Value, icms, pisCofins, ibsCbs, rates));

            item.UsageCode = usage.Code;
            item.UsageName = usage.Name;
            item.Cfop = cfop;
            item.Ncm = product.Ncm;
            item.GoodsOrigin = product.GoodsOrigin;
            item.MovesFiscalInventory = usage.MovesFiscalInventory;
            item.CreatesFinancialDocument = usage.CreatesFinancialDocument;

            SalesInvoiceTaxSnapshot.Write(item, result);
        }
    }

    private async Task<Branch> LoadBranchAsync(string? branchCode)
    {
        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode)
                     ?? throw new DefaultException($"Filial {branchCode} do documento não encontrada.");

        if (branch.TaxRegime is null || string.IsNullOrWhiteSpace(branch.StateCode))
            throw new DefaultException(
                $"Filial {branch.Code} está sem regime tributário ou UF. " +
                "Complete o cadastro da filial antes de emitir o documento.");

        return branch;
    }

    private async Task<string> LoadCustomerStateAsync(string cardCode)
    {
        var partner = await businessPartnerService.GetByIdAsync(cardCode)
                      ?? throw new DefaultException($"Parceiro {cardCode} não encontrado.");

        var state = SalesInvoicesCfopResolveService.ResolvePartnerState(partner);

        return string.IsNullOrWhiteSpace(state)
            ? throw new DefaultException($"Parceiro {cardCode} está sem UF no endereço de faturamento.")
            : state;
    }

    private async Task<UsageModel> ResolveUsageAsync(SalesInvoiceItem item, Dictionary<int, UsageModel> cache)
    {
        UsageModel usage;

        if (item.UsageCode is { } code)
        {
            if (!cache.TryGetValue(code, out usage!))
            {
                usage = await usageService.GetByIdAsync(code)
                        ?? throw new DefaultException("Natureza de operação não encontrada.");
                cache[code] = usage;
            }
        }
        else
        {
            usage = (await usageService.GetAllAsync()).FirstOrDefault(u => u is { IsDefault: true, Inactive: false })
                    ?? throw new DefaultException(
                        $"O item {item.ItemCode} está sem natureza de operação e não há natureza padrão cadastrada.");
        }

        if (usage.Direction == UsageDirection.Incoming)
            throw new DefaultException(
                $"A natureza de operação {usage.Name} é de entrada e não pode ser usada no documento de saída.");

        if (usage.Inactive)
            throw new DefaultException($"Natureza de operação {usage.Name} está inativa.");

        return usage;
    }

    private async Task<Item> LoadProductAsync(string itemCode)
    {
        var product = await db.Context.Items.AsNoTracking().FirstOrDefaultAsync(x => x.ItemCode == itemCode)
                      ?? throw new DefaultException($"Produto {itemCode} não encontrado no cadastro.");

        if (string.IsNullOrWhiteSpace(product.Ncm))
            throw new DefaultException($"Produto {itemCode} está sem NCM cadastrado.");

        if (product.GoodsOrigin is null)
            throw new DefaultException($"Produto {itemCode} está sem origem da mercadoria cadastrada.");

        return product;
    }

    private static string ResolveCfop(UsageModel usage, bool inState)
    {
        var cfop = inState ? usage.CfopOutgoingInState : usage.CfopOutgoingOutState;

        return string.IsNullOrWhiteSpace(cfop)
            ? throw new DefaultException(inState
                ? $"Natureza de operação {usage.Name} está sem CFOP de saída dentro do estado."
                : $"Natureza de operação {usage.Name} está sem CFOP de saída fora do estado.")
            : cfop;
    }

    private static IcmsRule ResolveIcmsRule(UsageModel usage, bool inState, TaxRegime regime)
    {
        var rule = inState
            ? new IcmsRule(usage.IcmsInStateCst, usage.IcmsInStateCsosn, usage.IcmsInStateRate,
                usage.IcmsInStateBaseReduction, usage.IcmsInStateDeferral, usage.IcmsInStateBenefitCode)
            : new IcmsRule(usage.IcmsOutStateCst, usage.IcmsOutStateCsosn, null,
                usage.IcmsOutStateBaseReduction, usage.IcmsOutStateDeferral, usage.IcmsOutStateBenefitCode);

        var csosn = FiscalCodes.UsesCsosn(regime);
        var code = csosn ? rule.Csosn : rule.Cst;

        if (string.IsNullOrWhiteSpace(code))
            throw new DefaultException(
                $"Natureza de operação {usage.Name} está sem {(csosn ? "CSOSN" : "CST")} de ICMS " +
                $"{(inState ? "dentro do estado" : "fora do estado")}. " +
                "Configure a tributação no cadastro de Naturezas de Operação.");

        return rule;
    }

    private static PisCofinsRule ResolvePisCofinsRule(UsageModel usage)
    {
        if (string.IsNullOrWhiteSpace(usage.PisCst) || string.IsNullOrWhiteSpace(usage.CofinsCst))
            throw new DefaultException(
                $"Natureza de operação {usage.Name} está sem CST de PIS/COFINS. " +
                "Configure a tributação no cadastro de Naturezas de Operação.");

        return new PisCofinsRule(usage.PisCst, usage.PisRate, usage.CofinsCst, usage.CofinsRate,
            usage.ExcludeIcmsFromPisCofinsBase);
    }

    private async Task<(IbsCbsRule? rule, IbsCbsRates? rates)> ResolveIbsCbsAsync(UsageModel usage, DateOnly issueDate)
    {
        if (string.IsNullOrWhiteSpace(usage.IbsCbsCst))
            return (null, null);

        var rule = new IbsCbsRule(usage.IbsCbsCst, usage.IbsCbsClassCode ?? string.Empty,
            usage.IbsRateReduction, usage.CbsRateReduction);

        var rate = await ibsCbsRatesService.GetEffectiveAsync(issueDate)
                   ?? throw new DefaultException(
                       $"Não há alíquota de IBS/CBS vigente em {issueDate:dd/MM/yyyy}. " +
                       "Cadastre-a em Naturezas de Operação > Alíquotas IBS/CBS.");

        return (rule, new IbsCbsRates(rate.CbsRate, rate.IbsStateRate, rate.IbsMunicipalRate));
    }
}
```

DI: `services.AddScoped<SalesInvoicesTaxApplyService>();` (perto de `SalesInvoicesCfopResolveService`).

- [ ] **Step 4: Run tests** — `--filter "FullyQualifiedName~SalesInvoicesTaxApplyServiceTests|FullyQualifiedName~SalesInvoicesCfopResolveServiceTests"` → PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesTaxApplyServiceTests.cs
git commit -m "feat(invoice): aplicar a tributação da natureza na linha com guardas de cadastro" -- <arquivos da task>
```

---

### Task 10: Integração na criação do documento (inclui faturamento de romaneio estrito)

**Files:**
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs`
- Modify (construtor): `SalesInvoicesShipmentBillingFiscalToleranceTests.cs`, `SalesInvoicesReturnContractBalanceTests.cs`, `SalesInvoicesReturnServiceTests.cs`, `SalesInvoicesReturnWeightTests.cs`, `SalesInvoicesReverseInvoiceReturnTests.cs`, `ShipmentLoads/ShipmentLoadBillingServiceTests.cs`, `ShipmentLoads/ShipmentLoadsRefuseServiceTests.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesCreateTaxationTests.cs`

**Interfaces:**
- Consumes: `SalesInvoicesTaxApplyService.IsActiveForAsync/ApplyAsync`.
- Produces: `SalesInvoicesCreateService(IUnitOfWork, IBusinessPartnerService, IItemService, DocNumberSequenceService, SalesInvoicesUsageGuardService, SalesInvoicesCfopResolveService, SalesInvoicesTaxApplyService, ILogger<SalesInvoicesCreateService>)`.

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesCreateTaxationTests.cs
// Mesmo setup de SalesInvoicesShipmentBillingFiscalToleranceTests (Seed de filial/produto/
// natureza/alíquota de SalesInvoicesTaxApplyServiceTests), com o Create montado assim:
//   new SalesInvoicesCreateService(db, partners, new FakeItemService(...), new FakeDocNumberSequenceService(),
//       new SalesInvoicesUsageGuardService(usages), new SalesInvoicesCfopResolveService(db, usages, partners),
//       TaxTestServices.Apply(db, partners), NullLogger<SalesInvoicesCreateService>.Instance)
```

Casos (cada um um `[Fact]` com corpo completo ao implementar, seguindo os helpers de `SalesInvoicesShipmentBillingFiscalToleranceTests`):

1. `Standalone_invoice_with_rule_active_is_created_with_the_tax_snapshot` — documento avulso SP→BA: `IcmsValue == 4200`, `CbsValue == 455.75`, `InvoiceStatus == Pending`.
2. `Shipment_billing_with_rule_active_fails_without_cfop` — filial com chave ligada, natureza padrão sem `CfopOutgoingOutState`, documento com romaneio (`AttachTransactionAsync`): `DefaultException` contendo "sem CFOP de saída fora do estado" (a tolerância NÃO vale com a regra ativa).
3. `Shipment_billing_with_rule_active_calculates_the_taxes` — cadastro completo + romaneio: linha com natureza padrão e `IcmsValue == 4200`.
4. `Shipment_billing_with_rule_inactive_keeps_the_tolerance` — filial com `IssuesNfe = false` e natureza sem CFOP: cria sem CFOP (idêntico ao teste existente).
5. `Return_invoice_with_rule_active_is_not_calculated` — `InvoiceType = Return`, linha com `IcmsValue = 5`: cria e mantém 5.

Nos 7 arquivos listados em **Files**, acrescentar `TaxTestServices.InactiveApply(db)` (ou o `UnitOfWork` local do arquivo) como penúltimo argumento de cada `new SalesInvoicesCreateService(...)`. **Nenhuma asserção existente muda.**

- [ ] **Step 2: Run to verify it fails** → FAIL (construtor).

- [ ] **Step 3: Write the implementation**

`SalesInvoicesCreateService.cs`:
1. Construtor ganha `SalesInvoicesTaxApplyService taxApply,` antes do `logger`.
2. Logo depois de `var fromShipmentBilling = ...`:

```csharp
        // Com a tributação da NF-e STANDALONE ativa, a cadeia fiscal volta a ser ESTRITA também
        // no romaneio: a tolerância abaixo existe para bases sem cadastro fiscal (SAPB1 recém-
        // implantada), e uma filial que emite NF-e não pode faturar com CFOP em branco.
        var taxActive = await taxApply.IsActiveForAsync(salesInvoice);
```

3. Na chamada `ResolveCfopAsync(salesInvoice, usage, fromShipmentBilling)` passar `fromShipmentBilling && !taxActive`.
4. Depois do `foreach (var (item, usage) in lineUsages)` e antes de `salesInvoice.DocNumberKey ??= ...`:

```csharp
        // Tributos pela natureza, ANTES de numerar: as guardas recusam com mensagem de negócio e
        // não faz sentido consumir número de um documento que não vai nascer. Fora do try de
        // propósito — lá dentro a DefaultException viraria ApplicationException e perderia o 400.
        if (taxActive)
        {
            await taxApply.ApplyAsync(salesInvoice, salesInvoice.Items);
        }
```

5. No laço do `try`, trocar `item.Cfop = cfopByItem.GetValueOrDefault(item);` por:

```csharp
                // Com a tributação ativa o CFOP já veio do cálculo (mesma regra, mesmo valor);
                // sem ela, é o do resolvedor — ausente quando o romaneio tolerou a lacuna.
                if (!taxActive)
                {
                    item.Cfop = cfopByItem.GetValueOrDefault(item);
                }
```

- [ ] **Step 4: Run tests** — `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoices|FullyQualifiedName~ShipmentLoad"` → PASS (inclusive os 7 casos de tolerância, sem alteração de asserção).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesCreateTaxationTests.cs
git commit -m "feat(invoice): calcular os tributos na criação do documento de saída" -- <arquivos da task>
```

---

### Task 11: Integração na inclusão/alteração de linha e no cabeçalho (trava)

**Files:**
- Modify: `SalesInvoicesItemsCreateService.cs`, `SalesInvoicesItemsUpdateService.cs`, `SalesInvoicesUpdateService.cs`
- Modify (construtor): `SalesInvoicesItemsUpdateLoadClosureTests.cs`, `SalesInvoicesItemsUpdateServiceTests.cs`, `SalesInvoicesReturnWeightTests.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesTaxLockTests.cs`

**Interfaces:**
- Produces: `SalesInvoicesItemsCreateService(IUnitOfWork, IItemService, SalesInvoicesTaxApplyService, ILogger<...>)`; `SalesInvoicesItemsUpdateService(IUnitOfWork, IItemService, ShipmentLoadsClosureHookService, SalesInvoicesTaxApplyService, ILogger<SalesInvoicesUpdateService>)`; `SalesInvoicesUpdateService(IUnitOfWork, IBusinessPartnerService, SalesInvoicesTaxApplyService, ILogger<...>)`.

- [ ] **Step 1: Write the failing tests** — `SalesInvoicesTaxLockTests` (seed igual ao da Task 9; documento gravado no banco com uma linha calculada):

1. `Adding_a_line_with_rule_active_calculates_it` — `ItemsCreateService.ExecuteAsync(novaLinha)` → linha com `IcmsValue` calculado.
2. `Adding_a_line_with_missing_setup_returns_a_business_error` — produto sem NCM → `DefaultException` (não `ApplicationException`).
3. `Patch_that_resends_old_taxes_is_overwritten_by_the_calculation` — carregar a linha rastreada, mudar `Quantity` para 15000 e `IcmsValue` para 999 (simula o Delta), `ItemsUpdateService.ExecuteAsync` → `IcmsValue == 2100` (7% de 30.000).
4. `Confirmed_invoice_line_keeps_its_taxes_on_reconciliation_edit` — documento `Confirmed`; linha rastreada com `DeliveredQuantity = 29000` e `IcmsValue = 999` → depois do update, `IcmsValue` volta ao gravado e `DeliveredQuantity == 29000`.
5. `Changing_the_customer_on_the_header_recalculates_every_line` — trocar `CardCode` de BA para SP pelo `SalesInvoicesUpdateService` → linhas com CFOP 5102 e CST 51.
6. `Header_change_with_rule_inactive_keeps_typed_values` — filial sem chave; trocar `CardCode` → valores digitados intactos.
7. `Line_update_with_rule_inactive_keeps_typed_values` — filial sem chave; `IcmsValue = 77` digitado → 77 gravado.

Nos 3 arquivos de teste existentes, passar `TaxTestServices.InactiveApply(db)` no novo parâmetro. Nenhuma asserção muda.

- [ ] **Step 2: Run to verify it fails** → FAIL.

- [ ] **Step 3: Write the implementation**

`SalesInvoicesItemsCreateService.ExecuteAsync` — antes do `try`:

```csharp
        // Tributação da NF-e STANDALONE: calcula a linha nova antes de gravar. Fora do try para a
        // guarda chegar à tela como 400, e não embrulhada em ApplicationException.
        var invoice = await db.Context.SalesInvoices
            .FirstOrDefaultAsync(x => x.Key == salesInvoiceItem.SalesInvoiceKey);

        if (invoice is not null)
        {
            await taxApply.ApplyAsync(invoice, [salesInvoiceItem]);
        }
```

`SalesInvoicesItemsUpdateService.ExecuteAsync` — logo depois de `db.Context.Entry(existingEntity).CurrentValues.SetValues(entity);`:

```csharp
            await ApplyTaxLockAsync(existingEntity);
```

e o método:

```csharp
    /// <summary>
    /// Trava da tributação STANDALONE. Pendente: o cálculo sobrescreve o que veio no corpo (o
    /// PATCH reenvia a entidade inteira, então recusar quebraria a tela). Já confirmado (a
    /// Conferência de entregas usa este mesmo serviço): os campos travados voltam ao gravado.
    /// Regra inativa: nada muda — é o caminho da Yokotobi e da MH Agro.
    /// </summary>
    private async Task ApplyTaxLockAsync(SalesInvoiceItem item)
    {
        var invoice = await db.Context.SalesInvoices.FirstOrDefaultAsync(x => x.Key == item.SalesInvoiceKey);

        if (invoice is null || !await taxApply.IsActiveForAsync(invoice))
            return;

        if (invoice.InvoiceStatus is null or InvoiceStatus.Pending)
        {
            await taxApply.ApplyAsync(invoice, [item]);
            return;
        }

        SalesInvoiceTaxSnapshot.RestoreLocked(db.Context.Entry(item));
    }
```

`SalesInvoicesUpdateService.ExecuteAsync` — antes do `SetValues`, ler o "antes" do rastreador (no PATCH a entidade chega já mutada, ver comentário de `SalesInvoicesItemsUpdateService`):

```csharp
            var original = db.Context.Entry(existingEntity).OriginalValues;
            var fiscalInputsChanged =
                !Equals(original[nameof(SalesInvoice.InvoiceDate)], entity.InvoiceDate) ||
                !Equals(original[nameof(SalesInvoice.CardCode)], entity.CardCode) ||
                !Equals(original[nameof(SalesInvoice.BranchCode)], entity.BranchCode);
```

e depois de preencher os nomes, antes do `SaveChangesAsync`:

```csharp
            // Data, cliente e filial são entradas do cálculo (vigência do IBS/CBS, UF, regime):
            // mudou um deles, todas as linhas se recalculam. No-op com a regra inativa.
            if (fiscalInputsChanged)
            {
                var items = await db.Context.SalesInvoicesItems
                    .Where(i => i.SalesInvoiceKey == existingEntity.Key)
                    .ToListAsync();

                await taxApply.ApplyAsync(existingEntity, items);
            }
```

- [ ] **Step 4: Run tests** — `--filter "FullyQualifiedName~SalesInvoices"` → PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesTaxLockTests.cs
git commit -m "feat(invoice): travar e recalcular os tributos da linha na edição do documento" -- <arquivos da task>
```

---

### Task 12: Endereço do parceiro STANDALONE (dois bugs)

**Files:**
- Modify: `SiagroB1.Application/Services/BusinessPartnerService.cs`, `SiagroB1.Web/Controllers/BusinessPartnersAddressesController.cs`
- Test: `SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerServiceAddressesTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerServiceAddressesTests.cs
// Construir o BusinessPartnerService STANDALONE com os mesmos colaboradores do construtor real
// (UnitOfWork de TestDb, FakeStringLocalizer, logger nulo — conferir a assinatura no arquivo).
```

Casos:
1. `Create_persists_nested_addresses` — `CreateAsync` com 2 endereços (B/PR e S/SP) → `db.Context.Set<Address>()` tem os 2 com `CardCode` do parceiro.
2. `Create_without_addresses_still_works` — lista vazia → parceiro criado, 0 endereços.
3. `GetById_returns_the_created_addresses` — depois do create, `GetByIdAsync` traz `Addresses.Count == 2` (garante a cadeia inteira que a resolução de UF usa).

- [ ] **Step 2: Run to verify it fails** → FAIL no caso 1/3.

- [ ] **Step 3: Implementation**

`BusinessPartnerService.CreateAsync` — antes do `AddAsync`:

```csharp
        // A tela cria o parceiro com os endereços aninhados (deep insert). Sem copiá-los o
        // parceiro nascia sem endereço, e é do endereço de faturamento que sai a UF do CFOP e
        // do cálculo de tributos.
        foreach (var address in model.Addresses)
        {
            entity.Addresses.Add(new Address
            {
                CardCode = model.CardCode,
                AddressName = address.AddressName,
                AdresType = address.AdresType,
                Street = address.Street,
                Block = address.Block,
                ZipCode = address.ZipCode,
                City = address.City,
                State = address.State,
                Country = address.Country,
            });
        }
```

(conferir o nome da coleção em `BusinessPartner` — o `UpdateAsync` usa `.Include(a => a.Addresses)`.)

`BusinessPartnersAddressesController.PostAsync`: `return Created(model);` → `return Ok(model);` com comentário: "`Created` monta o Location a partir do entity set, e esta rota de atributo não tem entity set (EdmUnknownEntitySet) — o endereço gravava e a resposta estourava 500. A verificação no navegador confirma a serialização; se o `Ok` também falhar, declarar `EntitySet<AddressModel>("BusinessPartnersAddresses")` + `HasManyBinding` no EDM."

- [ ] **Step 4: Run tests** → PASS.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerServiceAddressesTests.cs
git commit -m "fix(partner): gravar o endereço na criação do parceiro e não responder 500 na inclusão avulsa" -- <arquivos da task>
```

---

### Task 13: Frontend — cadastro da natureza

**Repo:** `siagro-b1-frontend` — antes de começar: `git switch -c feature/nfe-standalone-taxation`.

**Files:**
- Create: `webapp/view/usages/fragments/Taxation.fragment.xml`
- Modify: `webapp/view/usages/fragments/Form.fragment.xml`, `Add.view.xml`, `Edit.view.xml`, `Main.view.xml`, `webapp/controller/usages/Add.controller.ts`, `Main.controller.ts`, `webapp/model/formatter.ts`

- [ ] **Step 1: Form.fragment.xml (identidade)** — depois de "Descrição":

```xml
          <Label text="Tipo" visible="{ui>/identityEditable}" />
          <Select
            visible="{ui>/identityEditable}"
            selectedKey="{ path: 'Direction', targetType: 'any' }"
            forceSelection="false">
            <core:ListItem key="Outgoing" text="Saída" />
            <core:ListItem key="Incoming" text="Entrada" />
          </Select>
```

Os dois CFOPs de saída ganham `visible="{= ${path: 'Direction', targetType: 'any'} !== 'Incoming' }"` (no SAPB1 `Direction` é nulo ⇒ continuam visíveis) e, logo abaixo, os de entrada:

```xml
          <Label text="CFOP de entrada dentro do estado"
                 visible="{= ${path: 'Direction', targetType: 'any'} === 'Incoming' }" />
          <Input value="{CfopIncomingInState}" maxLength="4" editable="{ui>/identityEditable}"
                 visible="{= ${path: 'Direction', targetType: 'any'} === 'Incoming' }" />
          <Label text="CFOP de entrada fora do estado"
                 visible="{= ${path: 'Direction', targetType: 'any'} === 'Incoming' }" />
          <Input value="{CfopIncomingOutState}" maxLength="4" editable="{ui>/identityEditable}"
                 visible="{= ${path: 'Direction', targetType: 'any'} === 'Incoming' }" />
          <Label text="Texto na nota (natOp)" visible="{ui>/identityEditable}" />
          <Input value="{InvoiceOperationText}" maxLength="60" visible="{ui>/identityEditable}" />
          <Label text="Movimenta estoque ?" visible="{ui>/identityEditable}" />
          <CheckBox selected="{MovesFiscalInventory}" visible="{ui>/identityEditable}" />
          <Label text="Gera financeiro ?" visible="{ui>/identityEditable}" />
          <CheckBox selected="{CreatesFinancialDocument}" visible="{ui>/identityEditable}" />
```

Os blocos "Efeito no Contrato" e "Obrigatoriedades": cada `Label`/controle ganha `visible="{= ${path: 'Direction', targetType: 'any'} !== 'Incoming' }"` (a natureza de entrada não tem efeito em contrato de venda), exceto "Inativo ?", que fica sempre.

- [ ] **Step 2: Taxation.fragment.xml** — um `f:SimpleForm` `id="usagesTaxationForm"`, mesmo layout do Form, `visible="{ui>/identityEditable}"`, com títulos "ICMS dentro do estado", "ICMS fora do estado", "PIS/COFINS", "IBS/CBS", "Informações complementares". Selects com item vazio (`<core:ListItem key="" text="" />`) e `forceSelection="false"`:
  - CST ICMS: 00 Tributada integralmente; 20 Com redução de base; 40 Isenta; 41 Não tributada; 50 Suspensão; 51 Diferimento; 90 Outras.
  - CSOSN: 102 Sem permissão de crédito; 103 Isenção por faixa de receita; 300 Imune; 400 Não tributada; 900 Outros.
  - CST PIS/COFINS (saída): 01, 02, 03, 04, 05, 06, 07, 08, 09, 49; (entrada): 50–56, 60–67, 70–75, 98, 99 — dois Selects por tributo alternados por `Direction`.
  - CST IBS/CBS: 000 Tributação integral; 200 Alíquota reduzida; 400 Isenção; 410 Imunidade e não incidência.
  - Percentuais: `Input` com `value="{ path: 'IcmsInStateRate', type: 'sap.ui.model.odata.type.Double', formatOptions: { decimals: 4, groupingEnabled: false } }"` (idem para redução, diferimento, PIS, COFINS e reduções de IBS/CBS). O bloco "fora do estado" não tem alíquota; no lugar, um `Text` "Alíquota automática: 7%, 12% ou 4% (importado), pela UF do destinatário."
  - cBenef: `Input maxLength="10"`; cClassTrib: `Input maxLength="6"`; Tema 69: `CheckBox selected="{ExcludeIcmsFromPisCofinsBase}"` com label "Excluir o ICMS da base do PIS/COFINS ?"; Informações complementares: `TextArea value="{DefaultAdditionalInfo}" rows="4" maxLength="2000"`.

  Incluir o fragmento em `Add.view.xml` e `Edit.view.xml` logo depois do `Form`:

```xml
<core:Fragment fragmentName="siagrob1.view.usages.fragments.Taxation" type="XML" />
```

- [ ] **Step 3: Add.controller.ts** — no `oBinding.create({...})` acrescentar (toda propriedade editada precisa existir no payload inicial):

```ts
			Direction: "Outgoing",
			CfopIncomingInState: null,
			CfopIncomingOutState: null,
			InvoiceOperationText: null,
			DefaultAdditionalInfo: null,
			MovesFiscalInventory: false,
			CreatesFinancialDocument: false,
			IcmsInStateCst: null, IcmsInStateCsosn: null, IcmsInStateRate: null,
			IcmsInStateBaseReduction: null, IcmsInStateDeferral: null, IcmsInStateBenefitCode: null,
			IcmsOutStateCst: null, IcmsOutStateCsosn: null,
			IcmsOutStateBaseReduction: null, IcmsOutStateDeferral: null, IcmsOutStateBenefitCode: null,
			PisCst: null, PisRate: null, CofinsCst: null, CofinsRate: null,
			ExcludeIcmsFromPisCofinsBase: false,
			IbsCbsCst: null, IbsCbsClassCode: null, IbsRateReduction: null, CbsRateReduction: null,
```

- [ ] **Step 4: Main.view.xml + Main.controller.ts** — colunas novas visíveis só fora do SAPB1 (`visible="{ui>/identityEditable}"`): "Tipo" (`formatter.formatUsageDirection`), "ICMS Dentro" (`{IcmsInStateCst}` + `{IcmsInStateCsosn}` em dois `Text` dentro de um `HBox`, um campo por propriedade), "ICMS Fora" idem. Botão no footer, antes de "Incluir":

```xml
				<Button text="Alíquotas IBS/CBS" icon="sap-icon://percent" press=".onOpenIbsCbsRates" visible="{ui>/identityEditable}"/>
```

```ts
	onOpenIbsCbsRates() {
		this.navTo("ibsCbsRates");
	}
```

`formatter.ts`:

```ts
  /** Tipo da natureza. Nulo em SAPB1 (o OUSG não tem tipo). */
  formatUsageDirection(value: string): string {
    if (value === "Incoming") return "Entrada";
    if (value === "Outgoing") return "Saída";
    return "";
  },
```

- [ ] **Step 5: Gates** — `yarn ts-typecheck` e `yarn lint` (a partir de `siagro-b1-frontend/`) → sem erros novos.

- [ ] **Step 6: Commit** (frontend)

```bash
git add webapp/view/usages/fragments/Taxation.fragment.xml
git commit -m "feat(invoice): natureza de operação com tipo, tributação e flags de estoque e financeiro" -- <arquivos da task>
```

---

### Task 14: Frontend — tela de alíquotas IBS/CBS

**Files:**
- Create: `webapp/view/ibsCbsRates/Main.view.xml`, `webapp/view/ibsCbsRates/fragments/RateDialog.fragment.xml`, `webapp/controller/ibsCbsRates/Main.controller.ts`
- Modify: `webapp/manifest.json`

- [ ] **Step 1: Rota e target** — em `routes`, antes de `usages/{id}/edit`:

```json
        {
          "pattern": "usages/ibs-cbs-rates",
          "name": "ibsCbsRates",
          "target": "ibsCbsRates"
        },
```

em `targets`:

```json
        "ibsCbsRates": {
          "id": "ibsCbsRates",
          "level": 2,
          "name": "siagrob1.view.ibsCbsRates.Main",
          "clearControlAggregation": true
        },
```

- [ ] **Step 2: View** — `Page title="Alíquotas IBS/CBS" showNavButton="true" navButtonPress=".onNavBack"`, `t:Table id="ibsCbsRatesTable" selectionMode="Single" rows="{ path: '/IbsCbsRates', sorter: { path: 'StartDate', descending: true } }"`, colunas "Início da vigência" (`{ path: 'StartDate', type: 'sap.ui.model.odata.type.Date', formatOptions: { pattern: 'dd/MM/yyyy' } }`), "CBS %", "IBS estadual %", "IBS municipal %" (`type: 'sap.ui.model.odata.type.Decimal', constraints: { precision: 7, scale: 4 }` — só exibição). `MessageStrip` explicando: "As alíquotas são nacionais. Vale para o documento a vigência mais recente iniciada até a data de emissão." Footer: Atualizar, Incluir, Editar, Excluir.

- [ ] **Step 3: Diálogo** — `RateDialog.fragment.xml` ligado a um `JSONModel` nomeado `rate` (`{ key, startDate, cbsRate, ibsStateRate, ibsMunicipalRate }`): `DatePicker value="{rate>/startDate}" valueFormat="yyyy-MM-dd" displayFormat="dd/MM/yyyy"` e três `StepInput`/`Input type="Number"` para os percentuais. Botões Confirmar/Cancelar.

- [ ] **Step 4: Controller**

```ts
import BaseController from "../BaseController";
import JSONModel from "sap/ui/model/json/JSONModel";
import Dialog from "sap/m/Dialog";
import Table from "sap/ui/table/Table";
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import Context from "sap/ui/model/odata/v4/Context";
import DialogHelper from "siagrob1/dialogs/DialogHelper";
import { confirmDialog } from "siagrob1/helpers/DialogHelpers";

type RateForm = { key: number | null; startDate: string; cbsRate: number; ibsStateRate: number; ibsMunicipalRate: number };

/**
 * Alíquotas de IBS/CBS por vigência (só STANDALONE — o botão que traz até aqui some em SAPB1,
 * e o servidor recusa fora do modo). Inclusão e edição por diálogo com JSONModel, gravando pelo
 * modelo OData: `StartDate` é Edm.Date, então vai como "yyyy-MM-dd", sem fuso.
 *
 * @namespace siagrob1.controller.ibsCbsRates
 */
export default class Main extends BaseController {
  private rateDialog: Dialog;

  onInit(): void {
    this.getView().setModel(new JSONModel({}), "rate");
    this.getRouter().getRoute("ibsCbsRates").attachPatternMatched(() => this.onRefresh());
  }

  private table(): Table {
    return this.byId("ibsCbsRatesTable") as Table;
  }

  onRefresh(): void {
    (this.table().getBinding("rows") as ODataListBinding)?.refresh();
  }

  private selected(): Context | undefined {
    const i = this.table().getSelectedIndex();
    return i < 0 ? undefined : (this.table().getContextByIndex(i) as Context);
  }

  async onCreate() {
    await this.openDialog({ key: null, startDate: "", cbsRate: 0, ibsStateRate: 0, ibsMunicipalRate: 0 });
  }

  async onEdit() {
    const ctx = this.selected();
    if (!ctx) {
      MessageBox.alert("Selecione uma vigência para editar.");
      return;
    }

    await this.openDialog({
      key: ctx.getProperty("Key") as number,
      startDate: ctx.getProperty("StartDate") as string,
      cbsRate: Number(ctx.getProperty("CbsRate")),
      ibsStateRate: Number(ctx.getProperty("IbsStateRate")),
      ibsMunicipalRate: Number(ctx.getProperty("IbsMunicipalRate")),
    });
  }

  private async openDialog(data: RateForm) {
    (this.getView().getModel("rate") as JSONModel).setData(data);
    this.rateDialog ??= await DialogHelper.createDialog(this, "siagrob1.view.ibsCbsRates.fragments.RateDialog");
    this.rateDialog.open();
  }

  onCancelRate() {
    this.rateDialog?.close();
  }

  async onConfirmRate() {
    const data = (this.getView().getModel("rate") as JSONModel).getData() as RateForm;

    if (!data.startDate) {
      MessageBox.warning("Informe o início da vigência.");
      return;
    }

    const model = this.getView().getModel() as ODataModel;
    const groupId = model.getUpdateGroupId();
    const payload = {
      StartDate: data.startDate,
      CbsRate: Number(data.cbsRate),
      IbsStateRate: Number(data.ibsStateRate),
      IbsMunicipalRate: Number(data.ibsMunicipalRate),
    };

    try {
      this.setBusy(true);

      if (data.key == null) {
        (this.table().getBinding("rows") as ODataListBinding).create(payload, true);
      } else {
        const ctx = this.selected();
        await Promise.all(Object.entries(payload).map(([k, v]) => ctx.setProperty(k, v)));
      }

      await model.submitBatch(groupId);

      if (model.hasPendingChanges(groupId)) {
        // A mensagem do servidor já foi mostrada pelo handler global; desfaz para não reenviar.
        model.resetChanges(groupId);
        return;
      }

      this.rateDialog.close();
      MessageToast.show("Alíquotas gravadas.");
      this.onRefresh();
    } finally {
      this.setBusy(false);
    }
  }

  async onDelete() {
    const ctx = this.selected();
    if (!ctx) {
      MessageBox.alert("Selecione uma vigência para excluir.");
      return;
    }

    if (!(await confirmDialog("Excluir a vigência selecionada?"))) return;

    await ctx.delete();
    MessageToast.show("Vigência excluída.");
  }
}
```

(conferir a assinatura real de `confirmDialog` em `helpers/DialogHelpers.ts` e de `DialogHelper.createDialog` antes de usar; ajustar o import se `BaseController` estiver em `../BaseController` como em `usages`.)

- [ ] **Step 5: Gates** — `yarn ts-typecheck`, `yarn lint`.

- [ ] **Step 6: Commit**

```bash
git add webapp/view/ibsCbsRates webapp/controller/ibsCbsRates
git commit -m "feat(invoice): tela de alíquotas de IBS/CBS por vigência" -- <arquivos da task>
```

---

### Task 15: Frontend — filial (CRT + chave) e produto (origem + NCM)

**Files:**
- Modify: `webapp/view/branchs/fragments/Form.fragment.xml`, `webapp/controller/branchs/Add.controller.ts`, `Edit.controller.ts`, `webapp/view/produtos/fragments/Form.fragment.xml`, `webapp/controller/produtos/Add.controller.ts`, `Edit.controller.ts`

- [ ] **Step 1: Modo nas duas telas** — nos controllers Add/Edit de filial e produto, no `routeMatched`, inicializar `ui>/standalone` como `false` e depois `(await this.getSystemInfo())?.erp !== "SAPB1"` (mesmo padrão de `usages/Main.controller.ts`, com o modelo `ui` existente: `this.getModel("ui") as JSONModel`).

- [ ] **Step 2: Filial** — no Form, depois de UF:

```xml
          <Label text="Regime tributário (CRT)" visible="{ui>/standalone}" />
          <Select
            visible="{ui>/standalone}"
            selectedKey="{ path: 'TaxRegime', targetType: 'any' }"
            forceSelection="false">
            <core:ListItem key="" text="" />
            <core:ListItem key="SimplesNacional" text="1 - Simples Nacional" />
            <core:ListItem key="SimplesNacionalExcess" text="2 - Simples Nacional, excesso de sublimite" />
            <core:ListItem key="Normal" text="3 - Regime normal" />
            <core:ListItem key="Mei" text="4 - MEI" />
          </Select>
          <Label text="Emite NF-e pelo Siagro ?" visible="{ui>/standalone}" />
          <CheckBox selected="{IssuesNfe}" visible="{ui>/standalone}" />
```

`branchs/Add.controller.ts`: `oBinding.create({ StateCode: null, TaxRegime: null, IssuesNfe: false }, ...)`. **Atenção:** o Select com `key=""` grava `""` num enum — o leitor OData recusa. Tratar no `onSave` das duas telas: se `TaxRegime === ""`, `await ctx.setProperty("TaxRegime", null)` antes do `submitBatch`.

- [ ] **Step 3: Produto** — no Form, depois de Descrição:

```xml
          <Label text="Origem da mercadoria" visible="{ui>/standalone}" />
          <Select
            visible="{ui>/standalone}"
            selectedKey="{ path: 'GoodsOrigin', targetType: 'any', formatter: '.formatOriginKey' }"
            change=".onGoodsOriginChange"
            forceSelection="false">
            <core:ListItem key="" text="" />
            <core:ListItem key="0" text="0 - Nacional" />
            <core:ListItem key="1" text="1 - Estrangeira, importação direta" />
            <core:ListItem key="2" text="2 - Estrangeira, adquirida no mercado interno" />
            <core:ListItem key="3" text="3 - Nacional com mais de 40% de conteúdo importado" />
            <core:ListItem key="4" text="4 - Nacional, processos produtivos básicos" />
            <core:ListItem key="5" text="5 - Nacional com até 40% de conteúdo importado" />
            <core:ListItem key="6" text="6 - Estrangeira, importação direta, sem similar (CAMEX)" />
            <core:ListItem key="7" text="7 - Estrangeira, mercado interno, sem similar (CAMEX)" />
            <core:ListItem key="8" text="8 - Nacional com mais de 70% de conteúdo importado" />
          </Select>
          <Label text="NCM" visible="{ui>/standalone}" />
          <Input value="{Ncm}" maxLength="8" visible="{ui>/standalone}" />
```

`GoodsOrigin` é `Edm.Byte`: o Select trabalha com string. Sem formatter no binding de duas vias, usar `selectedKey` **sem** binding e sincronizar à mão: no `routeMatched`/`dataReceived`, `select.setSelectedKey(String(ctx.getProperty("GoodsOrigin") ?? ""))`; no `change`, `ctx.setProperty("GoodsOrigin", key === "" ? null : Number(key))`. (Remover o `formatter` do XML acima nesse caso — fica `id="goodsOriginSelect"` e `change=".onGoodsOriginChange"`.) O método vai em `produtos/BaseController.ts`, compartilhado por Add e Edit. `produtos/Add.controller.ts`: acrescentar `GoodsOrigin: null, Ncm: null` ao `create`.

- [ ] **Step 4: Gates** — `yarn ts-typecheck`, `yarn lint`.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(master-data): regime tributário e emissão de NF-e na filial, origem e NCM no produto" -- <arquivos da task>
```

---

### Task 16: Frontend — documento de saída (trava, campos novos, totais, value help)

**Files:**
- Modify: `webapp/model/ServerRoutes.ts`, `webapp/controller/salesInvoices/BaseController.ts`, `Add.controller.ts`, `Edit.controller.ts`, `Detail.controller.ts`, `webapp/view/salesInvoices/fragments/ItemFiscalDialog.fragment.xml`, `webapp/view/salesInvoices/fragments/Form.fragment.xml` (total IBS/CBS), `webapp/dialogs/DialogHelper.ts`, `webapp/dialogs/fragments/UsagesSelectDialog.fragment.xml`

- [ ] **Step 1: Rota + leitura da regra** — `ServerRoutes.ts`: `taxCalculationIsActive: "/TaxCalculationIsActive(...)",` (padrão do `usersSyncFromSap`). Em `salesInvoices/BaseController.ts`:

```ts
    /**
     * Pergunta ao servidor se a filial do documento calcula tributos (NF-e STANDALONE). A regra
     * mora no servidor (`TaxCalculationGate`); a tela só a consulta. Falha = trava desligada: o
     * servidor continua sendo quem recalcula e sobrescreve.
     */
    protected async refreshTaxLock(branchCode: string | undefined) {
      const uiModel = this.getModel("ui") as JSONModel;
      uiModel.setProperty("/taxLocked", false);

      if (!branchCode) return;

      try {
        const oModel = this.getView().getModel() as ODataModel;
        const oFunction = oModel.bindContext("/TaxCalculationIsActive(...)");
        oFunction.setParameter("BranchCode", branchCode);
        await oFunction.invoke();
        uiModel.setProperty("/taxLocked", oFunction.getBoundContext().getProperty("value") === true);
      } catch {
        uiModel.setProperty("/taxLocked", false);
      }
    }
```

Chamar `void this.refreshTaxLock(...)` no `routeMatched` do Edit e do Detail (no `dataReceived` do `bindElement`, com `BranchCode` do contexto) e no Add (com a filial escolhida no formulário — `BranchCode` do contexto transitório, recalculado no `change` do campo de filial se houver handler; senão no `onSave` antes de submeter não é necessário: o servidor calcula de qualquer jeito).

- [ ] **Step 2: Diálogo fiscal** — em `ItemFiscalDialog.fragment.xml`:
  - Todo `editable="{ui>/editable}"` dos campos de tributo (NCM, CST, base, alíquota, valor de ICMS/PIS/COFINS) vira `editable="{= ${ui>/editable} &amp;&amp; !${ui>/taxLocked} }"`. Centro de Custo e Conta Contábil ficam como estão.
  - As três alíquotas trocam `constraints: { precision: 5, scale: 4 }` por `{ precision: 7, scale: 4 }`.
  - Acrescentar, com `visible="{ui>/taxLocked}"` (sem a regra o diálogo fica idêntico ao de hoje): em Identificação, "Origem" (`Text text="{GoodsOrigin}"`); em ICMS, "Redução %", "ICMS da operação", "Diferimento %", "ICMS diferido", "cBenef" (`Text`, `Decimal` só de exibição); um `FormContainer title="IBS/CBS" visible="{ui>/taxLocked}"` com CST, cClassTrib, Base, CBS % / Redução / Valor, IBS estadual % / Valor, IBS municipal % / Valor, Redução IBS; e em Identificação, "Movimenta estoque" / "Gera financeiro" (`formatter.formatBooleanYesNo` com `targetType: 'any'`).

- [ ] **Step 3: Total IBS/CBS no cabeçalho** — no `Form.fragment.xml` do documento, perto de onde o total de impostos/total geral é exibido, acrescentar `Label "Total IBS/CBS"` + `Text text="{ path: 'TotalInvoiceIbsCbs', type: 'sap.ui.model.odata.type.Decimal', constraints: { precision: 18, scale: 2 } }"` com `visible="{ui>/taxLocked}"`.

- [ ] **Step 4: Value help só de Saída** — `DialogHelper.openTableSelectDialog` ganha um 6º parâmetro opcional `staticFilter?: string`; depois do `getSelectDialog`:

```ts
    // $filter estático (ex.: enum, que sap.ui.model.Filter não sabe formatar). undefined remove
    // o parâmetro — o diálogo é reaproveitado entre aberturas, então é aplicado sempre.
    (oDlg.getBinding("items") as ODataListBinding).changeParameters({ $filter: staticFilter });
```

Em `openUsageValueHelp`, passar `undefined` como `elementPath` e, como `staticFilter`, `this.getModel("sessionModel").getProperty("/erp") === "SAPB1" ? undefined : "Direction eq 'Outgoing'"` (em STANDALONE a coluna é NOT NULL — não há nulo para o `eq` perder). Conferir que nenhum outro chamador de `openTableSelectDialog` passa 6 argumentos.

- [ ] **Step 5: Gates** — `yarn ts-typecheck`, `yarn lint`.

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(invoice): travar os tributos da linha e mostrar o IBS/CBS no documento de saída" -- <arquivos da task>
```

---

### Task 17: Verificação final

- [ ] **Step 1: Suíte inteira do backend** — `dotnet test SiagroB1.Application.Tests` → todos verdes; anotar o total.
- [ ] **Step 2: Modelo × migrations** — `dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` → "No changes"; gerar o script das 5 migrations e ler.
- [ ] **Step 3: Gates do frontend** — `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint` (o `yarn test` tem gate de cobertura irreal — não é critério).
- [ ] **Step 4: Banco de verificação** — NÃO aplicar no `IDX_SIAGRO_DEV` (há migration destrutiva do GAC-1171 pendente lá, decisão do usuário). Restaurar uma cópia a partir do backup COPY_ONLY existente (`...\Backup\IDX_SIAGRO_DEV_pre-GAC1171-rateio_20261001.bak`) como `IDX_SIAGRO_DEV_NFE`, aplicar **todas** as migrations pendentes nela com `--connection` explícito, e subir Web/Gateway apontando para ela (variável `ConnectionStrings__SiagroDB`) com `Erp=STANDALONE`.
- [ ] **Step 5: Roteiro no navegador (STANDALONE, chave ligada)** — pelo menu, a partir da home (admin/1234): filial com CRT 3 + UF SP + chave; produto com origem 0 + NCM 12019000; naturezas "Venda interna" (CST 51, 18%, diferimento 100%) e "Venda interestadual" (CST 00; PIS 01 1,65; COFINS 01 7,6; Tema 69; IBS/CBS 000 + 000001); clientes BA e SP criados **pela tela** com endereço de faturamento; documento avulso SP→BA 30.000 × 2,00 → diálogo fiscal mostra 4.200,00 / 920,70 / 4.240,80 / 455,75 / 50,64 e campos travados; SP→SP com diferimento; troca da data de emissão; alíquotas IBS/CBS pela tela.
- [ ] **Step 6: Chave desligada** — o mesmo documento novo fica com tributos digitáveis e sem campos novos no diálogo.
- [ ] **Step 7: SAPB1** — subir com o env da Yokotobi local (`yktb`) contra a cópia: natureza, filial, produto e documento sem campo novo; diálogo fiscal idêntico; botão "Alíquotas IBS/CBS" ausente.
- [ ] **Step 8: Derrubar a stack** — matar por PID as portas 50000/5246/8080 e conferir de novo no fim.
- [ ] **Step 9: Revisão final do branch** (reviewer novo, modelo mais capaz) e correções.
- [ ] **Step 10: Memória** — atualizar `nfe-standalone-emission-feature.md` com o estado final, contagens de teste e pendências.
