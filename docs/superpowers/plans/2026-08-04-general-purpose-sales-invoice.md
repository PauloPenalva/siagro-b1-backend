# Documento de saída de propósito geral — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recomendado) ou superpowers:executing-plans para executar tarefa a tarefa. Os passos usam checkbox (`- [ ]`).
>
> **Ao iniciar a execução, copiar este arquivo para `siagro-b1-backend/docs/superpowers/plans/2026-08-04-general-purpose-sales-invoice.md` e `git add` nele** (convenção do repo; o arquivo aqui é o de trabalho do plan mode).

## Context

Hoje o documento de saída (`SALES_INVOICES`) só nasce do faturamento de romaneio. Não existe caminho para lançar documento avulso e, por consequência, não existe manutenção fiscal de contrato: não dá para emitir complemento de preço de um PAF fixado depois da entrega, corrigir uma quebra apurada no destino, lançar devolução que não venha de romaneio, nem emitir uma saída sem contrato. Faltam duas coisas estruturais: **natureza de operação** (não existe CFOP em lugar nenhum do modelo — `DOC_TYPES` é numerador, não natureza) e **dado fiscal na linha** do documento.

Este é o **primeiro dos 4 sub-projetos** do roadmap fiscal/financeiro (saída → entrada → NF-e → financeiro). A spec aprovada está em `siagro-b1-backend/docs/superpowers/specs/2026-08-04-general-purpose-sales-invoice-design.md` — **leia-a antes de executar**; este plano a implementa e não a repete.

Resultado esperado: cadastrar uma natureza de operação, lançar um documento de saída avulso contra um contrato com diferença de preço apurada, confirmar, e ver a diferença liquidada no ledger com o saldo físico intacto — e o cancelamento estornando exatamente isso.

**Goal:** permitir lançar documentos de saída avulsos, com natureza de operação, dado fiscal na linha e efeito configurável (saldo/valor) sobre o contrato de venda.

**Architecture:** `USAGES` novo, cadastro local dual-mode (`IUsage`), sem FK. `SALES_INVOICES` ganha `UsageCode` (com backfill), `SALES_INVOICES_ITEMS` ganha os campos fiscais e contábeis. O efeito no contrato **não cria mecanismo novo**: vira linha no ledger `SALES_CONTRACTS_ALLOCATIONS` com origem nova `FiscalAdjustment`. Confirmação aplica, cancelamento/estorno já removem por invoice.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData v4 (ASP.NET Core OData), xUnit + EF InMemory, OpenUI5 1.141 + TypeScript.

## Global Constraints

- **Identificadores em inglês; texto ao usuário em pt-BR.** Vale para classes, entidades, tabelas e colunas. Só label de tela, título de menu e mensagem de negócio ficam em português.
- **Todo arquivo novo recebe `git add` imediatamente**, no sub-repo a que pertence. **Nunca commitar nem dar push** — commits são manuais do usuário.
- **Todo serviço novo é registrado à mão** em `AddApplicationServices()` (`SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`); não há varredura de assembly.
- **Modo alvo é STANDALONE.** SAPB1 só lê `OUSG` (escrita `NotImplementedException`). Nada é gravado no SAP via Service Layer.
- **Sem FK** para `USAGES`, `COST_CENTERS` e `LEDGER_ACCOUNTS` — validação no serviço. FK obrigatória para tabela local vira INNER JOIN e zera a coleção inteira em modo SAPB1.
- **Sem coluna sem leitor.** Nada de flag de estoque/financeiro "para depois".
- **Migration**: gerar com `dotnet ef migrations add`, **ler a migration gerada antes de aplicar**, e aplicar com ambiente explícito: `ASPNETCORE_ENVIRONMENT=Yokotobi dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`. Nunca usar o profile `db-migration`.
- **Toda tela nova exige migration de menu** no `CommonContext`: `MENU_ITEMS` (Key = nome da rota do manifest) + `ROLE_MENUS` para ADMIN.
- **Binding UI5**: todo binding de enum/booleano que passe por formatter precisa de `targetType: 'any'`; toda propriedade que o formulário edita precisa existir no `create()` inicial (nem que seja `null`).

## Decisões de execução (desvios conscientes da spec)

1. **Valor da linha é sempre `item.Total` (Quantity × UnitPrice).** A spec sugere complemento de preço "sem quantidade". Se a linha ficar com `Quantity = 0`, `Total` e `TotalInvoiceItems` ficam zero e o documento exibiria total 0 enquanto liquida milhares no ledger. Então: **complemento de preço é lançado com a quantidade complementada e `UnitPrice` = a diferença de preço unitária** (é como a NF complementar de preço é emitida na prática), e `ContractBalanceEffect = None` é o que impede o volume de ser consumido de novo. `RequiresQuantity = false` passa a significar apenas "não exijo quantidade > 0".
2. **O caminho de confirmação é escolhido pela origem do documento, não pela natureza.** Ordem: (a) `InvoiceType == Return` → serviço de devolução atual, inalterado; (b) documento **com** romaneios → alocação de faturamento atual, inalterada; (c) documento **sem** romaneios (avulso) → serviço novo de ajuste fiscal, dirigido pelos efeitos da natureza. Isso não regride nenhum fluxo que hoje funciona.
3. **A natureza semente do backfill nasce `Consume` + `ContractValueEffect.None`** (e não `Consume/Subtract` como diz a spec): o `PriceDifference` do faturamento de romaneio é *apuração*, não liquidação, e quem o grava é o caminho (b), que ignora os efeitos.
4. **Campos fiscais em diálogo por item**, não em colunas novas do grid (decisão do usuário) — a `sap.ui.table` de itens ficaria com ~22 colunas.
5. **Cadastro de natureza segue só o padrão dual-mode `IUsage`** (decisão do usuário), espelhando Centro de Custo / Conta Contábil. Não haverá `Services/Usages/*` por operação.

## File Structure

**Backend — criar**

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Enums/ContractBalanceEffect.cs` | `None`/`Consume`/`Restore` |
| `SiagroB1.Domain/Enums/ContractValueEffect.cs` | `None`/`Add`/`Subtract` |
| `SiagroB1.Domain/Entities/Usage.cs` | `USAGES` local (identidade fiscal + efeitos) |
| `SiagroB1.Application/Services/UsageService.cs` | CRUD local + validações (STANDALONE) |
| `SiagroB1.Application/Services/SAP/UsageService.cs` | leitura de `OUSG`; escrita `NotImplementedException` |
| `SiagroB1.Web/Controllers/UsagesController.cs` | controller OData fino |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCfopResolveService.cs` | resolve CFOP por UF filial × UF destinatário |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesUsageGuardService.cs` | obrigatoriedade condicional no serviço |
| `SiagroB1.Application/Services/SalesContracts/SalesContractsAllocationCreateForFiscalAdjustmentService.cs` | materializa os efeitos como linha de ledger |

**Backend — modificar:** `Domain/Models/UsageModel.cs`, `Domain/Entities/Branch.cs`, `Domain/Entities/SalesInvoice.cs`, `Domain/Entities/SalesInvoiceItem.cs`, `Domain/Enums/SalesContractAllocationOrigin.cs`, `Infra/Context/AppDbContext.cs`, `Application/Services/SalesInvoices/SalesInvoicesCreateService.cs`, `Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs`, `Web/ODataConfig/ODataConfigurations.cs`, `Web/Extensions/ServiceCollectionExtensions.cs`.

**Frontend — criar:** `controller|view/usages/*` (Main/Add/Edit + `fragments/Form.fragment.xml`), `controller|view/salesInvoices/Add.*`, `view/salesInvoices/fragments/ItemFiscalDialog.fragment.xml`, `dialogs/fragments/UsagesSelectDialog.fragment.xml`.
**Frontend — modificar:** `manifest.json`, `model/ServerRoutes.ts`, `controller/common/CommonController.ts`, `view/salesInvoices/fragments/Items.fragment.xml`, `view/salesInvoices/fragments/Form.fragment.xml`, `view/branchs/*` (UF).

---

### Task 1: Cadastro de natureza de operação (`USAGES`), dual-mode

**Files:**
- Create: `SiagroB1.Domain/Enums/ContractBalanceEffect.cs`, `SiagroB1.Domain/Enums/ContractValueEffect.cs`, `SiagroB1.Domain/Entities/Usage.cs`, `SiagroB1.Application/Services/UsageService.cs`, `SiagroB1.Application/Services/SAP/UsageService.cs`, `SiagroB1.Web/Controllers/UsagesController.cs`
- Modify: `SiagroB1.Domain/Models/UsageModel.cs`, `SiagroB1.Infra/Context/AppDbContext.cs`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Usages/UsageServiceTests.cs`

**Interfaces:**
- Consumes: `IUsage` (já existe em `Domain/Interfaces/IUsage.cs`, hoje sem nenhuma implementação e sem registro no DI) — a assinatura atual (`GetAllAsync`, `GetByIdAsync(int)`, `CreateAsync`, `UpdateAsync(int, …)`, `DeleteAsync(int)`, `QueryAll`) serve como está, **não alterar**.
- Produces: `UsageModel` com os campos de efeito; `IUsage` registrado nos dois modos.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
// SiagroB1.Application.Tests/Usages/UsageServiceTests.cs
public class UsageServiceTests
{
    private static UsageService Service(UnitOfWork db) =>
        new(db, NullLogger<UsageService>.Instance);

    private static UsageModel Model(
        ContractBalanceEffect balance = ContractBalanceEffect.None,
        ContractValueEffect value = ContractValueEffect.None,
        bool requiresContract = false) => new()
        {
            Name = "Complemento de preço",
            CfopOutgoingInState = "5949",
            CfopOutgoingOutState = "6949",
            ContractBalanceEffect = balance,
            ContractValueEffect = value,
            RequiresContract = requiresContract,
            RequiresQuantity = true,
        };

    [Fact]
    public async Task Create_rejects_effect_without_requiring_contract()
    {
        var db = TestDb.CreateUnitOfWork();

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).CreateAsync(Model(value: ContractValueEffect.Add)));
    }

    [Fact]
    public async Task Create_accepts_effect_when_contract_is_required()
    {
        var db = TestDb.CreateUnitOfWork();

        var created = await Service(db).CreateAsync(
            Model(value: ContractValueEffect.Add, requiresContract: true));

        Assert.True(created.Code > 0);
    }

    [Fact]
    public async Task Delete_is_blocked_when_a_sales_invoice_references_the_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var created = await Service(db).CreateAsync(Model());

        db.Context.SalesInvoices.Add(new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C0001",
            UsageCode = created.Code,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).DeleteAsync(created.Code));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

`dotnet test SiagroB1.Application.Tests --filter UsageServiceTests`
Esperado: erro de compilação — `Usage`, `ContractBalanceEffect`, `UsageService` e `SalesInvoice.UsageCode` não existem.

- [ ] **Step 3: Enums e entidade**

```csharp
// SiagroB1.Domain/Enums/ContractBalanceEffect.cs
namespace SiagroB1.Domain.Enums;

/// <summary>Efeito da natureza de operação sobre o SALDO FÍSICO do contrato de venda.</summary>
public enum ContractBalanceEffect
{
    /// <summary>Não move saldo (ex.: complemento de preço).</summary>
    None = 0,
    /// <summary>Consome saldo — linha de Volume positivo, como o faturamento.</summary>
    Consume = 1,
    /// <summary>Devolve saldo — linha de Volume negativo (devolução, ajuste de quebra).</summary>
    Restore = 2,
}
```

```csharp
// SiagroB1.Domain/Enums/ContractValueEffect.cs
namespace SiagroB1.Domain.Enums;

/// <summary>Efeito da natureza de operação sobre o VALOR apurado do contrato (PriceDifference).</summary>
public enum ContractValueEffect
{
    None = 0,
    Add = 1,
    Subtract = 2,
}
```

```csharp
// SiagroB1.Domain/Entities/Usage.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Natureza de operação mantida localmente (modo STANDALONE): identidade fiscal (nome,
/// CFOP) e efeito de negócio (saldo/valor/obrigatoriedades) na MESMA linha.
/// Em modo SAPB1 esta tabela fica vazia e a identidade fiscal vem de OUSG — ver
/// <see cref="SAP.Usage"/>. A chave é INT justamente para casar com OUSG.ID quando
/// esse modo entrar.
/// </summary>
[Table("USAGES")]
public class Usage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Code { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? Description { get; set; }

    /// <summary>CFOP de saída dentro do estado.</summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? CfopOutgoingInState { get; set; }

    /// <summary>CFOP de saída interestadual.</summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? CfopOutgoingOutState { get; set; }

    public ContractBalanceEffect ContractBalanceEffect { get; set; }

    public ContractValueEffect ContractValueEffect { get; set; }

    public bool RequiresContract { get; set; }

    public bool RequiresQuantity { get; set; } = true;

    public bool RequiresWeight { get; set; }

    public bool Inactive { get; set; }
}
```

Em `UsageModel.cs`, acrescentar (mantendo os 6 CFOPs que já existem — o SAP preenche todos, o local só os dois de saída):

```csharp
    public ContractBalanceEffect ContractBalanceEffect { get; set; }
    public ContractValueEffect ContractValueEffect { get; set; }
    public bool RequiresContract { get; set; }
    public bool RequiresQuantity { get; set; } = true;
    public bool RequiresWeight { get; set; }
    public bool Inactive { get; set; }
```

Em `AppDbContext.cs`, junto de `CostCenters`/`LedgerAccounts`: `public DbSet<Usage> Usages { get; set; }`.

- [ ] **Step 4: Serviço local**

`SiagroB1.Application/Services/UsageService.cs` — copiar a estrutura de `Application/Services/CostCenterService.cs` (mesmo formato: `QueryAll` projetando o Model com `AsNoTracking`, `GetByIdAsync`, `GetAllAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`), trocando `string code` por `int key`, e acrescentando:

```csharp
    /// <summary>
    /// Efeito no contrato sem exigir contrato é configuração impossível: a linha de ledger
    /// tem SalesContractKey NÃO anulável, então o documento não teria onde aplicar o efeito.
    /// </summary>
    private static void ValidateEffects(UsageModel model)
    {
        var hasEffect = model.ContractBalanceEffect != ContractBalanceEffect.None
                        || model.ContractValueEffect != ContractValueEffect.None;

        if (hasEffect && !model.RequiresContract)
        {
            throw new DefaultException(
                "Natureza de operação com efeito no contrato precisa exigir contrato.");
        }
    }
```

Chamado no início de `CreateAsync` e de `UpdateAsync`. E, em `DeleteAsync`, antes de remover:

```csharp
        // Não há FK para USAGES (dual-mode), então a integridade é aqui.
        if (await db.Context.SalesInvoices.AnyAsync(x => x.UsageCode == key))
        {
            throw new DefaultException(
                "Natureza de operação já utilizada em documento de saída. Inative-a em vez de excluir.");
        }
```

`SiagroB1.Application/Services/SAP/UsageService.cs` — copiar `Application/Services/SAP/CostCenterService.cs`, lendo `context.Usages` (`OUSG`, já mapeado em `Domain/Entities/SAP/Usage.cs` e no `SapErpDbContext`), projetando os 6 CFOPs e deixando os efeitos em `None`/`false`. Escrita lança `NotImplementedException("Not implemented on SAP context.")`. Comentário de topo registrando que a configuração dos efeitos por usage do SAP é o próximo passo quando SAPB1 entrar (spec, "Preparação para SAPB1") — **não criar coluna nem tabela para isso agora**.

- [ ] **Step 5: Controller, DI e EDM**

`SiagroB1.Web/Controllers/UsagesController.cs` — copiar `CostCentersController.cs`, trocando `ICostCenterService`→`IUsage`, `CostCenterModel`→`UsageModel` e `string key`→`int key`.

Em `ServiceCollectionExtensions.cs`:
```csharp
// AddSapServices()
services.AddScoped<IUsage, Application.Services.SAP.UsageService>();
// AddStandAloneServices()
services.AddScoped<IUsage, Application.Services.UsageService>();
```

Em `ODataConfigurations.cs`, junto do bloco dual-mode de `CostCenters`/`LedgerAccounts`:
```csharp
modelBuilder.EntitySet<UsageModel>("Usages");
```

- [ ] **Step 6: Migration da tabela**

```bash
dotnet build SiagroB1.sln
dotnet ef migrations add CreateUsages --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Ler a migration gerada: deve conter **apenas** `CreateTable("USAGES")`. Se trouxer qualquer outra operação, é drift de snapshot — remover as operações estranhas à mão antes de aplicar.

- [ ] **Step 7: Rodar os testes**

`dotnet test SiagroB1.Application.Tests --filter UsageServiceTests` → PASS.

- [ ] **Step 8: Stage**

```bash
git add SiagroB1.Domain/Enums/ContractBalanceEffect.cs SiagroB1.Domain/Enums/ContractValueEffect.cs \
        SiagroB1.Domain/Entities/Usage.cs SiagroB1.Application/Services/UsageService.cs \
        SiagroB1.Application/Services/SAP/UsageService.cs SiagroB1.Web/Controllers/UsagesController.cs \
        SiagroB1.Application.Tests/Usages/UsageServiceTests.cs SiagroB1.Migrations/AppContext/*CreateUsages*
```

---

### Task 2: UF na filial (`BRANCHS.StateCode`)

**Files:**
- Modify: `SiagroB1.Domain/Entities/Branch.cs`, `webapp/view/branchs/fragments/Form.fragment.xml` (nome exato a confirmar em `webapp/view/branchs/`)
- Create: migration `AddBranchStateCode`

- [ ] **Step 1: Coluna na entidade**

```csharp
    /// <summary>
    /// UF da filial. Sem FK para STATES, coerente com o restante do cadastro. Nulável
    /// porque as filiais existentes não têm o dado; a resolução de CFOP trata ausência
    /// como erro de negócio explícito, nunca como silêncio.
    /// </summary>
    [Column(TypeName = "VARCHAR(2)")]
    public string? StateCode { get; set; }
```

- [ ] **Step 2: Migration**

`dotnet ef migrations add AddBranchStateCode …` (mesmos parâmetros da Task 1). Conferir que só há `AddColumn("StateCode", "BRANCHS")`.

- [ ] **Step 3: Campo na tela de filial**

Em `webapp/view/branchs/`, acrescentar o Input de UF com o value help que já existe (`openStatesValueHelp` em `CommonController`, dialog `StatesSelectDialog`, propriedade `Code`):

```xml
<Label text="UF" required="true" />
<Input
  value="{StateCode}"
  showValueHelp="true"
  valueHelpOnly="true"
  valueHelpRequest=".openStatesValueHelp" />
```

⚠️ Campo obrigatório sobre coluna nulável sem backfill trava a edição de filial legada. Marcar `required="true"` **só se** a `validateForm` da tela isentar registros não editáveis; caso contrário deixar sem `required` e cobrar a UF apenas na emissão do documento (a resolução de CFOP já rejeita com mensagem clara).

- [ ] **Step 4: Verificar**

`dotnet build SiagroB1.sln` e, no frontend, `yarn ts-typecheck && yarn lint`. Stage dos arquivos novos.

---

### Task 3: Campos do documento — `UsageCode`, fiscais na linha e origem nova de ledger

**Files:**
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs`, `SiagroB1.Domain/Entities/SalesInvoiceItem.cs`, `SiagroB1.Domain/Enums/SalesContractAllocationOrigin.cs`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`
- Create: migrations `AddSalesInvoiceUsageAndFiscalFields` e `SeedDefaultUsageAndBackfillSalesInvoices`

- [ ] **Step 1: Cabeçalho**

Em `SalesInvoice.cs`:
```csharp
    /// <summary>
    /// Natureza de operação. Sem FK (dual-mode) — validada no serviço. Obrigatória na
    /// criação; nulável na coluna apenas por causa do legado, que a migration de backfill
    /// preenche com a natureza semente.
    /// </summary>
    public int? UsageCode { get; set; }
```

- [ ] **Step 2: Linha**

Em `SalesInvoiceItem.cs`, acrescentar (o CFOP é resolvido na gravação e **congelado**: se o cadastro da natureza mudar depois, o documento emitido não muda junto):
```csharp
    [Column(TypeName = "VARCHAR(4)")] public string? Cfop { get; set; }
    [Column(TypeName = "VARCHAR(8)")] public string? Ncm { get; set; }

    [Column(TypeName = "VARCHAR(3)")]  public string? CstIcms { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IcmsBase { get; set; }
    [Column(TypeName = "DECIMAL(5,4) DEFAULT 0")]  public decimal IcmsRate { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal IcmsValue { get; set; }

    [Column(TypeName = "VARCHAR(3)")]  public string? CstPis { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal PisBase { get; set; }
    [Column(TypeName = "DECIMAL(5,4) DEFAULT 0")]  public decimal PisRate { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal PisValue { get; set; }

    [Column(TypeName = "VARCHAR(3)")]  public string? CstCofins { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal CofinsBase { get; set; }
    [Column(TypeName = "DECIMAL(5,4) DEFAULT 0")]  public decimal CofinsRate { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal CofinsValue { get; set; }

    /// <summary>Centro de custo da linha. Sem FK (dual-mode OPRC/COST_CENTERS).</summary>
    [Column(TypeName = "VARCHAR(10)")] public string? CostCenterCode { get; set; }

    /// <summary>Conta contábil da linha. Sem FK (dual-mode OACT/LEDGER_ACCOUNTS).</summary>
    [Column(TypeName = "VARCHAR(20)")] public string? LedgerAccountCode { get; set; }

    /// <summary>Total de impostos da linha, derivado — sem coluna persistida (evita drift).</summary>
    [NotMapped]
    public decimal TotalTaxes => IcmsValue + PisValue + CofinsValue;
```

Em `SalesInvoice.cs`, o total de impostos do cabeçalho segue o padrão de `TotalInvoiceItems`:
```csharp
    [NotMapped]
    public decimal TotalInvoiceTaxes => Items.Sum(i => i.TotalTaxes);
```

⚠️ **Propriedade `[NotMapped]` só entra no EDM se for declarada com `AddProperty`** — sem isso, `$select`/`$orderby` sobre ela devolve 400. Em `ODataConfigurations.cs`, ao lado das linhas já existentes de `SalesInvoice.TotalInvoiceItems` e `SalesInvoiceItem.Total`:
```csharp
modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesInvoice))
    .AddProperty(typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.TotalInvoiceTaxes)));
modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(SalesInvoiceItem))
    .AddProperty(typeof(SalesInvoiceItem).GetProperty(nameof(SalesInvoiceItem.TotalTaxes)));
```

- [ ] **Step 3: Origem nova no ledger**

Em `SalesContractAllocationOrigin.cs`:
```csharp
    /// <summary>
    /// Linha criada por documento de saída AVULSO, segundo os efeitos configurados na
    /// natureza de operação (complemento de preço, ajuste de quebra, devolução sem
    /// romaneio). Volume 0 quando o efeito é só de valor.
    /// </summary>
    FiscalAdjustment = 5,
```

- [ ] **Step 4: Migration de schema**

`dotnet ef migrations add AddSalesInvoiceUsageAndFiscalFields …`. Conferir: só `AddColumn` em `SALES_INVOICES` e `SALES_INVOICES_ITEMS`.

- [ ] **Step 5: Migration de semente + backfill (escrita à mão)**

```csharp
// SiagroB1.Migrations/AppContext/<timestamp>_SeedDefaultUsageAndBackfillSalesInvoices.cs
/// <summary>
/// Documento de saída existente nasceu de romaneio e não tem natureza de operação.
/// Deixar nulo e marcar o campo como obrigatório na tela travaria a edição de todo
/// registro legado — armadilha já vivida nesta base. Por isso a natureza semente é
/// criada e aplicada a todos os documentos existentes.
///
/// Efeitos da semente: Consume + None. O PriceDifference do faturamento de romaneio é
/// APURAÇÃO (gravada pelo caminho de faturamento, que ignora os efeitos), não liquidação.
/// </summary>
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
        IF NOT EXISTS (SELECT 1 FROM USAGES WHERE Name = 'Venda de grãos')
        BEGIN
            INSERT INTO USAGES
                (Name, Description, CfopOutgoingInState, CfopOutgoingOutState,
                 ContractBalanceEffect, ContractValueEffect,
                 RequiresContract, RequiresQuantity, RequiresWeight, Inactive)
            VALUES
                ('Venda de grãos', 'Natureza padrão do faturamento de romaneio',
                 '5102', '6102', 1, 0, 1, 1, 1, 0);
        END;

        UPDATE SALES_INVOICES
           SET UsageCode = (SELECT TOP 1 Code FROM USAGES WHERE Name = 'Venda de grãos')
         WHERE UsageCode IS NULL;
    ");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
        UPDATE SALES_INVOICES
           SET UsageCode = NULL
         WHERE UsageCode = (SELECT TOP 1 Code FROM USAGES WHERE Name = 'Venda de grãos');

        DELETE FROM USAGES WHERE Name = 'Venda de grãos';
    ");
}
```
(`ContractBalanceEffect = 1` é `Consume`; `ContractValueEffect = 0` é `None`.) Gerar o esqueleto com `dotnet ef migrations add SeedDefaultUsageAndBackfillSalesInvoices …` e **esvaziar o `Up`/`Down` gerado** antes de colar o SQL acima.

- [ ] **Step 6: Build + stage**

`dotnet build SiagroB1.sln`; `git add` das migrations novas.

---

### Task 4: Resolução do CFOP

**Files:**
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCfopResolveService.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesCfopResolveServiceTests.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: `IUsage.GetByIdAsync(int)`; `IBusinessPartnerService.GetByIdAsync(string)` → `BusinessPartnerModel` com `Addresses` (`AddressModel.State`); `AppDbContext.Branchs`.
- Produces: `Task<string> ResolveAsync(int usageCode, string branchCode, string cardCode)`.

- [ ] **Step 1: Testes que falham**

Quatro casos da spec. Usar `TestDb.CreateUnitOfWork()` e os fakes de `SiagroB1.Application.Tests/Support/` (há `FakeBusinessPartnerService`; se ele não expuser endereços, estender **esse** fake em vez de criar outro).

```csharp
[Fact] public async Task Same_state_uses_the_in_state_cfop()          // "MT"/"MT" → 5102
[Fact] public async Task Different_state_uses_the_out_of_state_cfop() // "MT"/"GO" → 6102
[Fact] public async Task Missing_branch_state_is_a_business_error()   // DefaultException
[Fact] public async Task Missing_cfop_on_usage_is_a_business_error()  // DefaultException
```

- [ ] **Step 2: Rodar e ver falhar** — `dotnet test SiagroB1.Application.Tests --filter Cfop` → não compila.

- [ ] **Step 3: Implementar**

```csharp
namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Resolve o CFOP da linha comparando a UF da filial do documento com a UF do
/// destinatário. Chamado tanto pelo caminho avulso quanto pelo faturamento de
/// romaneio — um só lugar decide. Nunca devolve vazio em silêncio.
/// </summary>
public class SalesInvoicesCfopResolveService(
    IUnitOfWork db,
    IUsage usageService,
    IBusinessPartnerService businessPartnerService)
{
    public async Task<string> ResolveAsync(int usageCode, string? branchCode, string cardCode)
    {
        var usage = await usageService.GetByIdAsync(usageCode)
            ?? throw new DefaultException("Natureza de operação não encontrada.");

        var branch = await db.Context.Branchs.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Code == branchCode)
            ?? throw new DefaultException("Filial do documento não encontrada.");

        if (string.IsNullOrWhiteSpace(branch.StateCode))
            throw new DefaultException(
                $"Filial {branch.Code} está sem UF cadastrada. Informe a UF da filial antes de emitir o documento.");

        var partner = await businessPartnerService.GetByIdAsync(cardCode)
            ?? throw new DefaultException($"Parceiro {cardCode} não encontrado.");

        var partnerState = partner.Addresses.FirstOrDefault()?.State;

        if (string.IsNullOrWhiteSpace(partnerState))
            throw new DefaultException(
                $"Parceiro {cardCode} está sem UF no endereço de faturamento.");

        var sameState = string.Equals(branch.StateCode, partnerState, StringComparison.OrdinalIgnoreCase);
        var cfop = sameState ? usage.CfopOutgoingInState : usage.CfopOutgoingOutState;

        if (string.IsNullOrWhiteSpace(cfop))
            throw new DefaultException(sameState
                ? $"Natureza de operação {usage.Name} está sem CFOP de saída dentro do estado."
                : $"Natureza de operação {usage.Name} está sem CFOP de saída fora do estado.");

        return cfop;
    }
}
```

- [ ] **Step 4: Registrar e rodar** — `services.AddScoped<SalesInvoicesCfopResolveService>();` no bloco de sales invoices de `AddApplicationServices()`. `dotnet test … --filter Cfop` → PASS. `git add` dos arquivos novos.

---

### Task 5: Criação avulsa com obrigatoriedade condicional

**Files:**
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesUsageGuardService.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesUsageGuardServiceTests.cs`

**Interfaces:**
- Produces: `Task<UsageModel> ValidateAsync(SalesInvoice invoice)` — devolve a natureza já resolvida para o chamador não buscá-la de novo.

- [ ] **Step 1: Testes que falham**

```csharp
[Fact] public async Task Price_complement_without_weight_passes()            // RequiresWeight=false
[Fact] public async Task Loss_adjustment_without_quantity_fails()            // RequiresQuantity=true, Quantity=0
[Fact] public async Task Invoice_without_contract_passes_when_not_required() // RequiresContract=false
[Fact] public async Task Invoice_without_contract_fails_when_required()
[Fact] public async Task Invoice_without_usage_fails()
```

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Implementar o guard**

```csharp
/// <summary>
/// Obrigatoriedade condicional dirigida pela natureza de operação. Validada AQUI, e não
/// só na tela: a tela some com o campo, o serviço é quem garante.
/// </summary>
public class SalesInvoicesUsageGuardService(IUsage usageService)
{
    public async Task<UsageModel> ValidateAsync(SalesInvoice invoice)
    {
        if (invoice.UsageCode is not { } usageCode)
            throw new DefaultException("Natureza de operação é obrigatória.");

        var usage = await usageService.GetByIdAsync(usageCode)
            ?? throw new DefaultException("Natureza de operação não encontrada.");

        if (usage.Inactive)
            throw new DefaultException($"Natureza de operação {usage.Name} está inativa.");

        if (usage.RequiresContract && invoice.Items.Any(i => i.SalesContractKey is null))
            throw new DefaultException(
                $"Natureza de operação {usage.Name} exige contrato de venda em todos os itens.");

        if (usage.RequiresQuantity && invoice.Items.Any(i => i.Quantity <= 0))
            throw new DefaultException(
                $"Natureza de operação {usage.Name} exige quantidade em todos os itens.");

        if (usage.RequiresWeight && (invoice.GrossWeight <= 0 || invoice.NetWeight <= 0))
            throw new DefaultException(
                $"Natureza de operação {usage.Name} exige peso bruto e peso líquido.");

        return usage;
    }
}
```

- [ ] **Step 4: Ligar no create**

Em `SalesInvoicesCreateService.ExecuteAsync`, injetar `SalesInvoicesUsageGuardService` e `SalesInvoicesCfopResolveService` e, logo depois da guarda de itens vazios:

```csharp
        var usage = await usageGuard.ValidateAsync(salesInvoice);
        var cfop = await cfopResolve.ResolveAsync(
            usage.Code, salesInvoice.BranchCode, salesInvoice.CardCode);
```
e, dentro do `foreach (var item in salesInvoice.Items)` que já preenche `ItemName`, congelar o CFOP: `item.Cfop = cfop;`.

O resto do método fica intacto: o laço de romaneios já é no-op quando `SalesTransactions` está vazio, que é exatamente o caso avulso — **não criar serviço de criação paralelo**.

- [ ] **Step 5: Registrar, rodar, stage** — `services.AddScoped<SalesInvoicesUsageGuardService>();`. `dotnet test SiagroB1.Application.Tests` inteiro (o create é usado por vários testes existentes — se algum quebrar por falta de `UsageCode`, ajustar o **setup do teste**, não afrouxar o guard).

---

### Task 6: Efeito no contrato via ledger

**Files:**
- Create: `SiagroB1.Application/Services/SalesContracts/SalesContractsAllocationCreateForFiscalAdjustmentService.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesFiscalAdjustmentTests.cs`

**Interfaces:**
- Consumes: `SalesContractsFixedVolumeService.ConfirmedUnitPriceAsync(Guid, decimal)`; `SalesContractsRecalculateBalanceService.CalculateAllocatedAsync(AppDbContext, Guid)` e `EffectiveFactor(SalesInvoiceItem)`.
- Produces: `Task ExecuteAsync(SalesInvoice invoice, UsageModel usage, string userName, CommitMode commitMode = CommitMode.Auto)`.

- [ ] **Step 1: Testes que falham**

```csharp
[Fact] public async Task Consume_writes_a_positive_volume_line()
[Fact] public async Task Restore_writes_a_negative_volume_line()
[Fact] public async Task None_effects_write_no_ledger_line_at_all()
[Fact] public async Task Price_complement_writes_zero_volume_and_opposite_price_difference()
[Fact] public async Task Price_difference_sum_converges_to_zero_after_the_complement()
[Fact] public async Task Ledger_invariant_survives_a_zero_volume_line()   // Σ Volume por item
[Fact] public async Task Pending_invoice_does_not_move_the_balance()
[Fact] public async Task Cancel_reverses_exactly_what_confirm_applied()   // via SalesInvoicesCancelService
```

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Implementar o serviço**

```csharp
/// <summary>
/// Materializa os efeitos da natureza de operação de um documento de saída AVULSO como
/// linha do ledger SALES_CONTRACTS_ALLOCATIONS (origem FiscalAdjustment) — nenhum
/// mecanismo novo, nenhuma coluna nova no contrato.
///
/// Saldo: Consume → Volume positivo; Restore → negativo; None → nenhuma linha de volume.
/// Valor: Add → PriceDifference positivo; Subtract → negativo; None → zero.
/// O complemento de preço (None/Add) grava Volume = 0: não toca o saldo físico e não
/// altera a invariante "Σ Volume por item = consumo nominal do item".
///
/// SalesShipmentReleaseKey fica NULL de propósito: ajuste fiscal não consome liberação,
/// e linha de liberação negativa inflaria o saldo físico do contrato.
///
/// Roda DENTRO da transação do chamador (CommitMode.Deferred, sem SaveChanges próprio).
/// </summary>
public class SalesContractsAllocationCreateForFiscalAdjustmentService(
    IUnitOfWork db,
    SalesContractsFixedVolumeService fixedVolumeService)
{
    public async Task ExecuteAsync(SalesInvoice invoice, UsageModel usage, string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        if (usage.ContractBalanceEffect == ContractBalanceEffect.None
            && usage.ContractValueEffect == ContractValueEffect.None)
            return;

        var items = invoice.Items
            .Where(i => i.SalesContractKey != null && i.Key != null)
            .ToList();

        if (items.Count == 0)
            return;

        // Idempotência (reconfirmação): item que já tem linha não gera outra.
        var itemKeys = items.Select(i => i.Key!.Value).ToList();
        var alreadyAllocated = await db.Context.SalesContractsAllocations
            .Where(a => itemKeys.Contains(a.SalesInvoiceItemKey))
            .Select(a => a.SalesInvoiceItemKey)
            .Distinct()
            .ToListAsync();

        items = items.Where(i => !alreadyAllocated.Contains(i.Key!.Value)).ToList();
        if (items.Count == 0)
            return;

        var contractKeys = items.Select(i => i.SalesContractKey!.Value).Distinct().ToList();
        var contracts = await db.Context.SalesContracts
            .Where(c => contractKeys.Contains(c.Key))
            .ToDictionaryAsync(c => c.Key);

        var unitPrices = new Dictionary<Guid, decimal>();
        foreach (var contractKey in contractKeys)
        {
            if (!contracts.TryGetValue(contractKey, out var contract))
                throw new ApplicationException("Contrato de venda não encontrado.");

            if (contract.Status == ContractStatus.Finished)
                throw new ApplicationException("Contrato encerrado: não é possível alocar.");

            unitPrices[contractKey] =
                await fixedVolumeService.ConfirmedUnitPriceAsync(contractKey, contract.Price);
        }

        var pending = new List<SalesContractAllocation>();
        foreach (var item in items)
        {
            var contractKey = item.SalesContractKey!.Value;

            var volume = usage.ContractBalanceEffect switch
            {
                ContractBalanceEffect.Consume => item.Quantity,
                ContractBalanceEffect.Restore => -item.Quantity,
                _ => 0m,
            };

            var lineValue = decimal.Round(item.Quantity * item.UnitPrice, 2, MidpointRounding.ToEven);

            var priceDifference = usage.ContractValueEffect switch
            {
                ContractValueEffect.Add => lineValue,
                ContractValueEffect.Subtract => -lineValue,
                _ => 0m,
            };

            var allocation = new SalesContractAllocation
            {
                SalesContractKey = contractKey,
                SalesInvoiceItemKey = item.Key!.Value,
                SalesShipmentReleaseKey = null,
                Volume = volume,
                InvoiceUnitPrice = item.UnitPrice,
                ContractPrice = unitPrices[contractKey],
                PriceDifference = priceDifference,
                Origin = SalesContractAllocationOrigin.FiscalAdjustment,
                ApprovedAt = DateTime.Now,
                ApprovedBy = userName,
            };

            pending.Add(allocation);
            await db.Context.SalesContractsAllocations.AddAsync(allocation);
        }

        // Derivado-da-soma (nunca incremental): Σ do banco + linhas pendentes desta chamada.
        foreach (var contractKey in contractKeys)
        {
            var contract = contracts[contractKey];
            var persisted = await SalesContractsRecalculateBalanceService
                .CalculateAllocatedAsync(db.Context, contractKey);
            var pendingSum = pending
                .Where(a => a.SalesContractKey == contractKey)
                .Sum(a => a.Volume * SalesContractsRecalculateBalanceService.EffectiveFactor(
                    items.First(i => i.Key!.Value == a.SalesInvoiceItemKey)));

            contract.AllocatedVolume = decimal.Round(persisted + pendingSum, 3, MidpointRounding.ToEven);
        }

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Ligar na confirmação**

Em `SalesInvoicesConfirmService`, injetar `SalesInvoicesUsageGuardService` e o serviço acima, e trocar o `else` do bloco `if (invoice.InvoiceType == SalesInvoiceType.Return)` por:

```csharp
            else if (invoice.SalesTransactions.Count > 0)
            {
                // Documento nascido de romaneio: caminho de faturamento, inalterado.
                await ProcessNormalInvoiceAsync(invoice, userName);
                await allocationCreate.ExecuteForInvoiceAsync(invoice, userName, CommitMode.Deferred);

                foreach (var item in invoice.Items)
                {
                    if (item.SalesShipmentReleaseKey is { } itemReleaseKey)
                        affectedReleaseKeys.Add(itemReleaseKey);
                }
            }
            else
            {
                // Documento AVULSO: quem decide o efeito é a natureza de operação.
                // Escolher pela ORIGEM (tem romaneio ou não), e não pela natureza, é o que
                // impede o caminho novo de mexer no faturamento que já funciona.
                var usage = await usageGuard.ValidateAsync(invoice);
                await fiscalAdjustment.ExecuteAsync(invoice, usage, userName, CommitMode.Deferred);
            }
```

⚠️ `Include(x => x.SalesTransactions)` já existe na consulta do topo do método — manter. Sem ele, `Count > 0` daria falso e todo documento cairia no caminho avulso.

⚠️ O recálculo mora **no serviço**, não na entidade: o `CommitMode.Deferred` acima é correto porque o `SaveChangesAsync` do próprio `ExecuteAsync` vem logo depois, dentro da transação. Não mover essa chamada para fora dela.

- [ ] **Step 5: Conferir o estorno (sem código novo)**

`SalesInvoicesCancelService` e `SalesInvoicesReverseConfirmService` já chamam `SalesContractsAllocationDeleteForInvoiceService`, que apaga **por item da invoice** — logo cobre `FiscalAdjustment` sem alteração. Provar isso com o teste `Cancel_reverses_exactly_what_confirm_applied`, não por leitura.

- [ ] **Step 6: Registrar, rodar tudo, stage** — `services.AddScoped<SalesContractsAllocationCreateForFiscalAdjustmentService>();`, `dotnet test SiagroB1.Application.Tests` completo.

---

### Task 7: Tela de cadastro de natureza de operação

**Files:**
- Create: `webapp/controller/usages/{Main,Add,Edit}.controller.ts`, `webapp/view/usages/{Main,Add,Edit}.view.xml`, `webapp/view/usages/fragments/Form.fragment.xml`
- Modify: `webapp/manifest.json`, `webapp/model/ServerRoutes.ts`
- Create: migration `AddUsageMenu` no `CommonContext`

- [ ] **Step 1: Telas** — copiar `controller/costCenters/*` e `view/costCenters/*` (padrão de cadastro mestre simples, feito há um dia), trocando `CostCenters`→`Usages` e as colunas. Rotas `usages`, `usagesNew`, `usagesEdit`; patterns `usages`, `usages/add`, `usages/{id}/edit`.

- [ ] **Step 2: As duas armadilhas de binding** (passam em build, ts-typecheck, lint e ui5lint; quebram só no navegador):

```xml
<!-- enum/booleano com formatter SEMPRE com targetType: 'any' -->
<Text text="{ path: 'Inactive', targetType: 'any', formatter: 'formatter.formatBooleanYesNo' }" />
```

```ts
// toda propriedade que o formulário edita precisa existir no create() inicial
const oContext = oBinding.create({
  ContractBalanceEffect: "None",
  ContractValueEffect: "None",
  RequiresContract: false,
  RequiresQuantity: true,
  RequiresWeight: false,
  Inactive: false,
}, false, false, false);
```

- [ ] **Step 3: Migration de menu** — copiar `20260804004632_AddCostCenterAndLedgerAccountMenus.cs`, inserindo `{"usages", "Naturezas de Operação", "sap-icon://receipt", true, false, 13, "registers"}` em `MENU_ITEMS` e a linha correspondente em `ROLE_MENUS` para `ADMIN` (**Guid novo**, não reaproveitar).

- [ ] **Step 4: Verificar** — `yarn ts-typecheck && yarn lint && yarn ui5lint`. (`yarn test` **não** é gate útil aqui: o limiar de cobertura de 50% contra ~2,4% reais faz o script falhar sempre, não é regressão.) `git add` dos arquivos novos.

---

### Task 8: Rota `sales-invoices/add` e formulário reagindo à natureza

**Files:**
- Create: `webapp/controller/salesInvoices/Add.controller.ts`, `webapp/view/salesInvoices/Add.view.xml`, `webapp/dialogs/fragments/UsagesSelectDialog.fragment.xml`
- Modify: `webapp/manifest.json`, `webapp/view/salesInvoices/fragments/Form.fragment.xml`, `webapp/controller/common/CommonController.ts`

- [ ] **Step 1: Rota e tela** — acrescentar ao `manifest.json` a rota `{"pattern": "sales-invoices/add", "name": "salesInvoicesAdd", "target": "salesInvoicesAdd"}` + target. `Add.view.xml` reusa `fragments/Form.fragment.xml` e `fragments/Items.fragment.xml`, como `view/storageInvoices/Add.view.xml` faz.

- [ ] **Step 2: Controller** — espelhar `controller/storageInvoices/Add.controller.ts`: `clearStates`, `ui` model (`editable`, `editableGrid`), `getDocNumberInfoByTransaction("SalesInvoice")` para o `DocNumberKey` default, `oBinding.create({...}, false, false, false)` e `submitBatch` no save (o POST já cai em `SalesInvoicesController.Post` → `SalesInvoicesCreateService`, que agora valida a natureza).

- [ ] **Step 3: Value help da natureza** — em `CommonController.ts`, ao lado dos demais:

```ts
openUsagesValueHelp(ev: Input$ValueHelpRequestEvent) {
  void this.applyValueHelp(ev, "UsagesSelectDialog", ["Name", "Description"], "Code",
    [ new Filter("Inactive", FilterOperator.EQ, false) ]);
}
```
Fragmento `dialogs/fragments/UsagesSelectDialog.fragment.xml` copiado de um `*SelectDialog` existente, ligado a `/Usages`.

- [ ] **Step 4: Campos reagindo à natureza** — no `Add.controller.ts`, ao escolher a natureza, ler o registro (`GET /Usages(<code>)`) e escrever no model `ui` os flags que governam a tela:

```ts
uiModel.setProperty("/requiresWeight", usage.RequiresWeight);
uiModel.setProperty("/requiresQuantity", usage.RequiresQuantity);
uiModel.setProperty("/requiresContract", usage.RequiresContract);
```
Os campos de peso usam `visible="{ui>/requiresWeight}"` e `required="{ui>/requiresWeight}"`.

⚠️ `visible` com binding **undefined** resolve para `true` — inicializar os três flags como `false` no `newRouteMatched()`, antes de qualquer render.

- [ ] **Step 5: Menu** — acrescentar a entrada de menu da tela nova (se o acesso for por item de menu próprio) na mesma migration da Task 7 **ou** um botão "Incluir" na `view/salesInvoices/Main.view.xml` navegando para `salesInvoicesAdd`. Preferir o botão: a lista de documentos de saída já está no menu.

- [ ] **Step 6: Verificar e stage** — `yarn ts-typecheck && yarn lint && yarn ui5lint`; `git add`.

---

### Task 9: Diálogo fiscal do item

**Files:**
- Create: `webapp/view/salesInvoices/fragments/ItemFiscalDialog.fragment.xml`
- Modify: `webapp/view/salesInvoices/fragments/Items.fragment.xml`, `webapp/controller/salesInvoices/BaseController.ts` (ou `Add.controller.ts`), `webapp/controller/common/CommonController.ts`

- [ ] **Step 1: Botão na toolbar dos itens** — em `Items.fragment.xml`, ao lado de Incluir/Remover: `<Button visible="{ui>/editable}" text="Fiscal" icon="sap-icon://official-service" press=".onOpenItemFiscal" />`. Acrescentar também a coluna somente-leitura `CFOP` (`<Text text="{Cfop}" />`), que é o dado que o usuário confere no grid.

- [ ] **Step 2: Diálogo** — `ItemFiscalDialog.fragment.xml` com CFOP (readonly), NCM, e os três blocos ICMS/PIS/COFINS (CST, Base, Alíquota, Valor), mais Centro de Custo e Conta Contábil com value help. Usar `sap.ui.layout.form.Form` com `ColumnLayout` (nunca `SimpleForm`). Campos decimais com `type: 'sap.ui.model.odata.type.Decimal'` e `constraints: { precision: 18, scale: 2 }` (alíquotas: `scale: 4`).

- [ ] **Step 3: Abertura ligada à linha selecionada**

```ts
async onOpenItemFiscal() {
  const oTable = this.byId("tableSalesInvoicesItems") as Table;
  const iIndex = oTable.getSelectedIndex();

  if (iIndex < 0) {
    MessageBox.warning("Selecione um item.");
    return;
  }

  this.itemFiscalDialog ??= await DialogHelper.createDialog(
    this, "siagrob1.view.salesInvoices.fragments.ItemFiscalDialog");

  this.itemFiscalDialog.setBindingContext(oTable.getContextByIndex(iIndex));
  this.itemFiscalDialog.open();
}
```

- [ ] **Step 4: Value helps contábeis** — em `CommonController.ts`:

```ts
openCostCentersValueHelp(ev: Input$ValueHelpRequestEvent) {
  void this.applyValueHelp(ev, "CostCentersSelectDialog", ["Code", "Name"], "Code",
    [ new Filter("Inactive", FilterOperator.EQ, false) ]);
}

openLedgerAccountsValueHelp(ev: Input$ValueHelpRequestEvent) {
  void this.applyValueHelp(ev, "LedgerAccountsSelectDialog", ["Code", "Name"], "Code",
    [ new Filter("Inactive", FilterOperator.EQ, false) ]);
}
```
Criar os dois fragmentos em `dialogs/fragments/` (os serviços dual-mode já estão prontos desde 03/08).

- [ ] **Step 5: Verificar e stage** — `yarn ts-typecheck && yarn lint && yarn ui5lint`; `git add`.

---

## Verificação

Teste passando não é conclusão — a verificação é **pelo caminho do usuário**, no navegador.

1. **Backend**: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests` (suíte inteira, não só os filtros novos).
2. **Aplicar as migrations** com ambiente explícito:
   `ASPNETCORE_ENVIRONMENT=Yokotobi dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` e o mesmo para `--context CommonDbContext`.
3. **Subir a stack**: `dotnet run --project SiagroB1.Web --launch-profile yktb` e `dotnet run --project SiagroB1.Gateway --launch-profile yktb`; frontend com `yarn start:dev`; login `admin`/`1234`.
   O ambiente Yokotobi roda em **SAPB1**; como o alvo é STANDALONE, exercitar o CRUD local com `Erp=STANDALONE dotnet run --project SiagroB1.Web --launch-profile yktb` (a env var sobrescreve a chave `Erp` sem tocar em arquivo de config, mantendo as connection strings de localhost). **Verificar nos dois modos**: em SAPB1 a tela de naturezas deve listar `OUSG` em leitura, sem quebrar.
4. **Roteiro end-to-end** (o que define "pronto"):
   a. Chegar em **Naturezas de Operação pelo menu** (não por URL) e cadastrar "Complemento de preço": CFOP `5949`/`6949`, saldo `None`, valor `Add`, exige contrato, não exige peso.
   b. Escolher um contrato de venda com `PriceDifference` apurada diferente de zero (somar `SALES_CONTRACTS_ALLOCATIONS.PriceDifference` do contrato).
   c. Lançar o documento avulso por **Documentos de Saída → Incluir**, com essa natureza, contra esse contrato; conferir que os campos de peso sumiram e que o CFOP apareceu na linha.
   d. Preencher o diálogo fiscal do item (NCM, CST/base/alíquota/valor, centro de custo, conta contábil) e salvar.
   e. **Confirmar** e verificar: a diferença de preço do contrato foi liquidada (soma converge para zero) e o **saldo físico ficou intacto**.
   f. **Cancelar** e ver o estorno exato — diferença e saldo voltam ao estado anterior.
   g. Repetir com uma natureza `Restore`/`Subtract` (ajuste de quebra) e confirmar que aí sim o saldo físico volta.
5. **Derrubar a stack ao terminar** (Web, Gateway e dev server) — não deixar processos órfãos nas portas 50000/5246/8080.

Enquanto o item 4 não for feito no navegador, a feature está **pendente**, não pronta.
