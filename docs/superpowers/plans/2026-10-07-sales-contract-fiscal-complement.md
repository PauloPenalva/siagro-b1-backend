# Complemento Fiscal do Contrato de Venda — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Na filial que emite NF-e pelo Siagro (STANDALONE + "Emite NF-e"), cada contrato de venda tem um complemento
fiscal (natureza, condição de pagamento, informações adicionais, pedido do cliente) editável só com a permissão
`SALES_CONTRACT_FISCAL_EDIT`; o faturamento aplica esse complemento e recusa contrato sem ele; a NF-e leva
`xPed`/`nItemPed`.

**Architecture:** Tabela 1:1 `SALES_CONTRACT_FISCAL_COMPLEMENTS` (molde do `ITEM_COMPLEMENTS`: function de leitura +
action de gravação, serviço próprio). Um serviço novo `SalesInvoicesFiscalComplementApplier` roda na criação do
documento de saída e na inclusão/alteração de linha, só com `TaxCalculationGate` ativo, e escreve natureza/pedido na
linha e condição + texto no cabeçalho antes do guard de natureza e do cálculo de tributos. O montador da NF-e copia o
pedido da linha para o `det/prod`. No frontend, uma seção no detalhe do contrato e campos só leitura no diálogo de
faturamento compartilhado.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), OData v4, xUnit, Zeus.Net.NFe.NFCe; OpenUI5 + TS.

**Spec:** `docs/superpowers/specs/2026-10-07-sales-contract-fiscal-complement-design.md` (backend, commit 93cdedc).

## Global Constraints

- Branch `feature/sales-contract-fiscal-complement` nos dois repos; conferir antes de cada commit; **nunca push**.
  Commit com pathspec explícito: no backend `docs/superpowers/{plans,specs}/2026-10-01-nfe-standalone-taxation*`
  staged **não** entram; no frontend `.vscode/.advpl/*` **não** entra.
- Mensagem `tipo(escopo): descrição pt-BR` (escopo `sales-contract`; `invoice` para documento de saída/NF-e;
  `platform` para permissão), rodapé
  `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>` e
  `Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw`. Commit com migration leva o trailer `DB:`
  com o(s) nome(s) da(s) migration(s).
- Identificadores em inglês; texto que o usuário lê em pt-BR, **exatamente** os textos abaixo:
  - recusa no faturamento: `"O contrato {Code} não tem complemento fiscal com natureza de operação e condição de pagamento. Peça ao fiscal para completá-lo."`
  - condições divergentes: `"Os contratos deste documento têm condições de pagamento diferentes no complemento fiscal."`
  - sem permissão: `"Você não tem permissão para alterar o complemento fiscal do contrato."`
  - natureza inválida: `"A natureza de operação {código} não é uma natureza de saída ativa."`
  - condição inexistente: `"Condição de pagamento {código} não encontrada."`
  - pedido longo: `"O pedido do cliente tem no máximo 15 caracteres."`; item do pedido: `"O item do pedido do cliente tem de 1 a 6 dígitos."`
- A regra de faturamento (aplicar/recusar) **só** age com `TaxCalculationGate.IsActiveAsync(branchCode)` verdadeiro, documento
  `InvoiceType == Normal`, não `IsNfeReturn`, e linha com `SalesContractKey`. Fora disso o comportamento é o de hoje.
- Natureza e condição do complemento **sobrescrevem** o que vier no corpo (D6 do spec).
- Permissão: `PermissionCodes.SalesContractFiscalEdit = "SALES_CONTRACT_FISCAL_EDIT"`; `USERS.IsAdmin` passa sem ela
  (comportamento atual de `UserPermissionsService.HasAsync`).
- `xPed` ≤ 15 caracteres; `nItemPed` 1–6 dígitos.
- OData: parâmetros numéricos de action como `int?`/`double?`, nunca `decimal` (memória); `ODataActionParameters` pode vir nulo.
- Frontend: `targetType: 'any'` em enum; `&amp;&amp;` em XML; comentário XML sem `--`; nunca `SimpleForm` em código
  novo; value help `valueHelpOnly` precisa de botão "Limpar".
- Gates finais: `dotnet build SiagroB1.sln` 0 erros; `SiagroB1.Application.Tests` e `SiagroB1.Fiscal.Tests` verdes;
  frontend `yarn ts-typecheck` e `yarn lint`. ⚠️ `yarn test` completo nunca passa: não usar.

## Desvios do spec (decididos aqui, menores)

- Na inclusão/alteração de **linha** (não na criação do documento), o applier escreve natureza e pedido na linha e a
  condição no cabeçalho só se ele estiver vazio; se o cabeçalho já tiver outra condição, recusa com a mensagem de
  condições divergentes. O texto do contrato no cabeçalho só é aplicado na criação.
- O `xPed`/`nItemPed` sai pelo montador lendo `SalesInvoiceItem` diretamente (cast do `INfeTaxedLine`), sem mexer na
  interface compartilhada com o Documento de Entrada.

## Review Focus

1. **Faturamento da carga sem complemento** — esperado: recusa antes de criar documento, liberação e carga intactas.
   → teste na Task 3 + Task 7.
2. **Natureza que o operador escolheu no avulso para uma linha com contrato** — esperado: o servidor troca pela do
   complemento (o operador não decide). → teste na Task 3.
3. **SAPB1 / MH Agro** — esperado: nada muda (sem recusa, natureza padrão, condição do cliente). → teste na Task 3.
4. **Excluir contrato que tem complemento** — esperado: exclui (cascade), sem erro de FK. → teste na Task 1/2.
5. **Reaplicação não duplica o texto do contrato** nas informações adicionais. → teste na Task 3.

---

## File Structure

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Entities/SalesContractFiscalComplement.cs` (novo) | entidade 1:1 |
| `SiagroB1.Domain/Entities/SalesInvoiceItem.cs` | `CustomerOrderNumber`, `CustomerOrderItem` |
| `SiagroB1.Infra/Context/AppDbContext.cs` | DbSet + chave + FK cascade |
| `SiagroB1.Application/Interfaces/IUserPermissions.cs` | constante da permissão |
| `SiagroB1.Migrations/AppContext/<ts>_AddSalesContractFiscalComplement.cs` (novo) | tabela + colunas |
| `SiagroB1.Migrations/CommonContext/<ts>_SeedSalesContractFiscalEditPermission.cs` (novo) | permissão + ADMIN |
| `SiagroB1.Domain/Dtos/SalesContractFiscalComplementDto.cs` (novo) | DTO |
| `SiagroB1.Application/Services/SalesContracts/SalesContractFiscalComplementService.cs` (novo) | get/set + validação |
| `SiagroB1.Web/Functions/SalesContracts/SalesContractsGetFiscalComplementController.cs` (novo) | function |
| `SiagroB1.Web/Actions/SalesContracts/SalesContractsSetFiscalComplementController.cs` (novo) | action |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesFiscalComplementApplier.cs` (novo) | aplica/recusa |
| `SalesInvoicesCreateService.cs`, `SalesInvoicesItemsCreateService.cs`, `SalesInvoicesItemsUpdateService.cs` | chamam o applier |
| `SalesInvoiceNfeLock.cs` | pedido na trava da NF-e |
| `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs`, `NfeXmlBuilder.cs`, `SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs` | `xPed`/`nItemPed` |
| frontend `view/salesContracts/fragments/SalesContractFiscalComplement.fragment.xml` + `FiscalComplementDialog.fragment.xml` (novos), `view/salesContracts/Detail.view.xml`, `controller/salesContracts/Detail.controller.ts`, `model/ServerRoutes.ts` | seção + edição |
| frontend `helpers/ShipmentBillingDialog.ts`, `view/shipmentBilling/fragments/Billing.fragment.xml` | complemento no diálogo |

---

### Task 1: Modelo, migrations e permissão

**Files:**
- Create: `SiagroB1.Domain/Entities/SalesContractFiscalComplement.cs`
- Modify: `SiagroB1.Domain/Entities/SalesInvoiceItem.cs` (perto de `UsageCode`, ~linha 173)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSet perto de `ItemComplements` ~linha 104; config perto de ~linha 250)
- Modify: `SiagroB1.Application/Interfaces/IUserPermissions.cs:4-7`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs` (`ItemFiscalFields`)
- Create: migrations App e Common
- Test: `SiagroB1.Application.Tests/SalesContracts/SalesContractFiscalComplementModelTests.cs` (novo)

**Interfaces:**
- Produces: `SalesContractFiscalComplement { Guid SalesContractKey; int? UsageCode; int? PaymentConditionCode; string? AdditionalInfo; string? CustomerOrderNumber; string? CustomerOrderItem; DateTime? UpdatedAt; string? UpdatedBy; SalesContract? SalesContract }`;
  `AppDbContext.SalesContractFiscalComplements`; `SalesInvoiceItem.CustomerOrderNumber` / `CustomerOrderItem` (string?);
  `PermissionCodes.SalesContractFiscalEdit`.

- [ ] **Step 1: Failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractFiscalComplementModelTests
{
    [Fact]
    public async Task Complement_is_saved_by_contract_key()
    {
        var db = TestDb.CreateUnitOfWork();
        var key = Guid.NewGuid();
        db.Context.SalesContractFiscalComplements.Add(new SalesContractFiscalComplement
        {
            SalesContractKey = key, UsageCode = 3, PaymentConditionCode = 11,
            AdditionalInfo = "Pedido 77", CustomerOrderNumber = "PO-77", CustomerOrderItem = "1",
        });
        await db.SaveChangesAsync();

        var saved = await db.Context.SalesContractFiscalComplements.AsNoTracking().SingleAsync(x => x.SalesContractKey == key);
        Assert.Equal(3, saved.UsageCode);
        Assert.Equal("PO-77", saved.CustomerOrderNumber);
    }
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesContractFiscalComplementModelTests"` → FAIL (compilação).

- [ ] **Step 2: Entity**

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Complemento FISCAL do contrato de venda (spec 2026-10-07): a instrução de faturamento do cliente — natureza de
/// operação, condição de pagamento, informações adicionais e pedido do cliente. Separado do contrato para não
/// misturar o fiscal com o comercial e para ter permissão própria de edição (<c>SALES_CONTRACT_FISCAL_EDIT</c>).
/// <para>
/// ⚠️ Não confundir com <see cref="SalesContract.Complement"/>, que é um rótulo comercial.
/// Sem FK para natureza e condição: em SAPB1 a natureza vem do <c>OUSG</c> (mesmo motivo do <see cref="ItemComplement"/>).
/// </para>
/// </summary>
[Table("SALES_CONTRACT_FISCAL_COMPLEMENTS")]
public class SalesContractFiscalComplement
{
    public Guid SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    public int? UsageCode { get; set; }

    public int? PaymentConditionCode { get; set; }

    [Column(TypeName = "VARCHAR(2000)")]
    public string? AdditionalInfo { get; set; }

    /// <summary>Pedido de compra do cliente (<c>det/prod/xPed</c>), até 15 caracteres.</summary>
    [Column(TypeName = "VARCHAR(15)")]
    public string? CustomerOrderNumber { get; set; }

    /// <summary>Item do pedido do cliente (<c>det/prod/nItemPed</c>), 1 a 6 dígitos.</summary>
    [Column(TypeName = "VARCHAR(6)")]
    public string? CustomerOrderItem { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }
}
```

`SalesInvoiceItem.cs`, junto de `UsageCode`:

```csharp
    /// <summary>Pedido do cliente (<c>xPed</c>), copiado do complemento fiscal do contrato na criação da linha.</summary>
    [Column(TypeName = "VARCHAR(15)")]
    public string? CustomerOrderNumber { get; set; }

    /// <summary>Item do pedido do cliente (<c>nItemPed</c>).</summary>
    [Column(TypeName = "VARCHAR(6)")]
    public string? CustomerOrderItem { get; set; }
```

`AppDbContext.cs`: `public DbSet<SalesContractFiscalComplement> SalesContractFiscalComplements { get; set; }` e, no
`OnModelCreating`, perto do `ItemComplement`:

```csharp
        // 1:1 com o contrato; excluir o contrato exclui o complemento (tabela filha não pode quebrar o delete do pai).
        modelBuilder.Entity<SalesContractFiscalComplement>(e =>
        {
            e.HasKey(x => x.SalesContractKey);
            e.HasOne(x => x.SalesContract).WithOne()
                .HasForeignKey<SalesContractFiscalComplement>(x => x.SalesContractKey)
                .OnDelete(DeleteBehavior.Cascade);
        });
```

`IUserPermissions.cs`, na classe `PermissionCodes`:
`public const string SalesContractFiscalEdit = "SALES_CONTRACT_FISCAL_EDIT";`

`SalesInvoiceNfeLock.ItemFiscalFields`: acrescente `nameof(SalesInvoiceItem.CustomerOrderNumber), nameof(SalesInvoiceItem.CustomerOrderItem)`.

- [ ] **Step 3: Migrations**

```bash
dotnet build SiagroB1.sln
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddSalesContractFiscalComplement --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add SeedSalesContractFiscalEditPermission --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build
```

Confira: a de App só cria `SALES_CONTRACT_FISCAL_COMPLEMENTS` (com a FK cascade) e as duas colunas em
`SALES_INVOICES_ITEMS`; a de Common nasce vazia — escreva nela, no molde de
`CommonContext/20260808021149_SeedWeighingManualEntryPermission.cs`:

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'SALES_CONTRACT_FISCAL_EDIT')
    INSERT INTO PERMISSIONS (Code, Description) VALUES ('SALES_CONTRACT_FISCAL_EDIT', 'Editar o complemento fiscal do contrato de venda');
IF NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'ADMIN' AND PermissionCode = 'SALES_CONTRACT_FISCAL_EDIT')
    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode) VALUES (NEWID(), 'ADMIN', 'SALES_CONTRACT_FISCAL_EDIT');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM ROLE_PERMISSIONS WHERE PermissionCode = 'SALES_CONTRACT_FISCAL_EDIT';
DELETE FROM PERMISSIONS WHERE Code = 'SALES_CONTRACT_FISCAL_EDIT';");
        }
```

(confira os nomes reais das colunas de `PERMISSIONS`/`ROLE_PERMISSIONS` no seed precedente e nas entidades
`Domain/Entities/Common/Permission.cs`/`RolePermission.cs`). **Não** aplique em banco nenhum nesta task.

- [ ] **Step 4: Tests pass + full suite** — filtro do Step 1 PASS; `dotnet test SiagroB1.Application.Tests` verde.

- [ ] **Step 5: Commit**

```bash
git branch --show-current
git add SiagroB1.Domain/Entities/SalesContractFiscalComplement.cs SiagroB1.Application.Tests/SalesContracts/SalesContractFiscalComplementModelTests.cs
git commit -m "feat(sales-contract): tabela do complemento fiscal e permissão de edição" -m "Complemento 1:1 do contrato de venda com natureza, condição, informações adicionais e pedido do cliente; a linha do documento de saída ganha o pedido (xPed/nItemPed). Permissão SALES_CONTRACT_FISCAL_EDIT ligada ao ADMIN." -m "DB: AddSalesContractFiscalComplement, SeedSalesContractFiscalEditPermission" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DBPxMjj5y1ygnQdgHuhgtw" -- SiagroB1.Domain SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Application/Interfaces/IUserPermissions.cs SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs SiagroB1.Migrations SiagroB1.Application.Tests/SalesContracts/SalesContractFiscalComplementModelTests.cs
```

---

### Task 2: Serviço, function e action do complemento

**Files:**
- Create: `SiagroB1.Domain/Dtos/SalesContractFiscalComplementDto.cs`
- Create: `SiagroB1.Application/Services/SalesContracts/SalesContractFiscalComplementService.cs`
- Create: `SiagroB1.Web/Functions/SalesContracts/SalesContractsGetFiscalComplementController.cs`
- Create: `SiagroB1.Web/Actions/SalesContracts/SalesContractsSetFiscalComplementController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (perto do `ItemsGetComplement`, ~linha 427)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/SalesContracts/SalesContractFiscalComplementServiceTests.cs` (novo)

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `SalesContractFiscalComplementDto { Guid SalesContractKey; int? UsageCode; string? UsageName; int? PaymentConditionCode; string? PaymentConditionName; string? AdditionalInfo; string? CustomerOrderNumber; string? CustomerOrderItem; DateTime? UpdatedAt; string? UpdatedBy; bool IsComplete }` (`IsComplete` = natureza e condição preenchidas).
  - `SalesContractFiscalComplementService.GetAsync(Guid salesContractKey)` → `Task<SalesContractFiscalComplementDto?>`;
    `SetAsync(Guid salesContractKey, SalesContractFiscalComplementInput input, string userName)` → `Task<SalesContractFiscalComplementDto>`;
    `record SalesContractFiscalComplementInput(int? UsageCode, int? PaymentConditionCode, string? AdditionalInfo, string? CustomerOrderNumber, string? CustomerOrderItem)`.
  - Function `GET odata/SalesContractsGetFiscalComplement(Key={key})`; action `POST odata/SalesContractsSetFiscalComplement`
    (`Key` Guid, `UsageCode` int?, `PaymentConditionCode` int?, `AdditionalInfo`/`CustomerOrderNumber`/`CustomerOrderItem` string, todos opcionais menos `Key`).

- [ ] **Step 1: Failing tests** — `SalesContractFiscalComplementServiceTests` (use os helpers de teste existentes:
  `TestDb`, `UsageService(db, NullLogger)` para criar naturezas, e um `IUserPermissions` falso — procure
  `FakeUserPermissions` em `Tests/Support`; se não existir, crie `FakeUserPermissions(params string[] granted)` que
  implementa `IUserPermissions.HasAsync` devolvendo `granted.Contains(code)` e `GetAsync` com a lista):
  - `Without_permission_is_refused` → `DefaultException` com "Você não tem permissão para alterar o complemento fiscal do contrato."
  - `Incoming_usage_is_refused` e `Inactive_usage_is_refused` → "A natureza de operação {código} não é uma natureza de saída ativa."
  - `Unknown_payment_condition_is_refused` → "Condição de pagamento {código} não encontrada."
  - `Order_item_with_letters_or_seven_digits_is_refused` (Theory "1A", "1234567") → "O item do pedido do cliente tem de 1 a 6 dígitos."
  - `Order_number_longer_than_15_is_refused` → "O pedido do cliente tem no máximo 15 caracteres."
  - `Set_creates_then_updates_and_stamps_the_user` → segunda chamada atualiza a mesma linha; `UpdatedBy` = usuário.
  - `Get_returns_names_and_completeness` → `UsageName`, `PaymentConditionName`, `IsComplete == true`; sem condição → `IsComplete == false`.
  - `Get_without_row_returns_null`.
  - `Deleting_the_contract_deletes_the_complement` → adicione o contrato e o complemento, remova o contrato e salve:
    sem exceção, e a tabela fica vazia (o InMemory aplica cascade de navegação configurada; se não aplicar, documente e
    deixe a prova para o SQL Server na Task 7).

  Run: filtro `~SalesContractFiscalComplementServiceTests` → FAIL (compilação).

- [ ] **Step 2: Service**

```csharp
public record SalesContractFiscalComplementInput(
    int? UsageCode, int? PaymentConditionCode, string? AdditionalInfo, string? CustomerOrderNumber, string? CustomerOrderItem);

/// <summary>Complemento fiscal do contrato de venda (spec 2026-10-07 §4.1). Só quem tem SALES_CONTRACT_FISCAL_EDIT grava.</summary>
public class SalesContractFiscalComplementService(IUnitOfWork db, IUsage usageService, IUserPermissions permissions)
{
    public async Task<SalesContractFiscalComplementDto?> GetAsync(Guid salesContractKey)
    {
        var entity = await db.Context.SalesContractFiscalComplements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SalesContractKey == salesContractKey);
        if (entity is null) return null;

        // Nomes só para exibição. A natureza pelo IUsage (dual-mode: USAGES ou OUSG); a condição é tabela local.
        var usageName = entity.UsageCode is { } usageCode ? (await usageService.GetByIdAsync(usageCode))?.Name : null;
        var conditionName = entity.PaymentConditionCode is { } conditionCode
            ? await db.Context.PaymentConditions.AsNoTracking().Where(x => x.Code == conditionCode).Select(x => x.Name).FirstOrDefaultAsync()
            : null;

        return new SalesContractFiscalComplementDto
        {
            SalesContractKey = entity.SalesContractKey,
            UsageCode = entity.UsageCode,
            UsageName = usageName,
            PaymentConditionCode = entity.PaymentConditionCode,
            PaymentConditionName = conditionName,
            AdditionalInfo = entity.AdditionalInfo,
            CustomerOrderNumber = entity.CustomerOrderNumber,
            CustomerOrderItem = entity.CustomerOrderItem,
            UpdatedAt = entity.UpdatedAt,
            UpdatedBy = entity.UpdatedBy,
            IsComplete = entity.UsageCode.HasValue && entity.PaymentConditionCode.HasValue,
        };
    }

    public async Task<SalesContractFiscalComplementDto> SetAsync(Guid salesContractKey, SalesContractFiscalComplementInput input, string userName)
    {
        if (!await permissions.HasAsync(userName, PermissionCodes.SalesContractFiscalEdit))
            throw new DefaultException("Você não tem permissão para alterar o complemento fiscal do contrato.");

        if (!await db.Context.SalesContracts.AnyAsync(x => x.Key == salesContractKey))
            throw new NotFoundException($"Sales contract not found key {salesContractKey}");

        if (input.UsageCode is { } usageCode)
        {
            var usage = await usageService.GetByIdAsync(usageCode);
            if (usage is null || usage.Inactive || usage.Direction != UsageDirection.Outgoing)
                throw new DefaultException($"A natureza de operação {usageCode} não é uma natureza de saída ativa.");
        }

        if (input.PaymentConditionCode is { } conditionCode &&
            !await db.Context.PaymentConditions.AnyAsync(x => x.Code == conditionCode))
            throw new DefaultException($"Condição de pagamento {conditionCode} não encontrada.");

        var orderNumber = Blank(input.CustomerOrderNumber);
        if (orderNumber is { Length: > 15 })
            throw new DefaultException("O pedido do cliente tem no máximo 15 caracteres.");

        var orderItem = Blank(input.CustomerOrderItem);
        if (orderItem is not null && (orderItem.Length > 6 || !orderItem.All(char.IsAsciiDigit)))
            throw new DefaultException("O item do pedido do cliente tem de 1 a 6 dígitos.");

        var entity = await db.Context.SalesContractFiscalComplements.FirstOrDefaultAsync(x => x.SalesContractKey == salesContractKey);
        if (entity is null)
        {
            entity = new SalesContractFiscalComplement { SalesContractKey = salesContractKey };
            await db.Context.SalesContractFiscalComplements.AddAsync(entity);
        }

        entity.UsageCode = input.UsageCode;
        entity.PaymentConditionCode = input.PaymentConditionCode;
        entity.AdditionalInfo = Blank(input.AdditionalInfo);
        entity.CustomerOrderNumber = orderNumber;
        entity.CustomerOrderItem = orderItem;
        entity.UpdatedAt = DateTime.Now;
        entity.UpdatedBy = userName;

        await db.SaveChangesAsync();
        return (await GetAsync(salesContractKey))!;
    }

    // String vazia do diálogo = não informado (mesma regra do ItemComplementService).
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

Confira os nomes reais: `IUsage` (interface do `UsageService`, dual-mode), `UsageModel.Inactive`/`Direction`,
`UsageDirection.Outgoing`, `db.Context.PaymentConditions` (`Code`, `Name`), `IUserPermissions` e o namespace de
`PermissionCodes`. Em SAPB1 o `IUsage` resolve pelo `OUSG`: a validação funciona nos dois modos.

- [ ] **Step 3: Web** — controllers no molde de `ItemsGetComplementController`/`ItemsSetComplementController`
  (function devolve `Ok(null)` sem linha; action lê `ODataActionParameters` com nulo tratado, usuário por
  `User.Identity?.Name ?? "Unknown"`, `DefaultException` → `BadRequest(e.Message)`, `NotFoundException` → `NotFound()`).
  EDM:

```csharp
        var salesContractsGetFiscalComplement = modelBuilder.Function("SalesContractsGetFiscalComplement");
        salesContractsGetFiscalComplement.Parameter<Guid>("Key");
        salesContractsGetFiscalComplement.Returns<SalesContractFiscalComplementDto>();

        var salesContractsSetFiscalComplement = modelBuilder.Action("SalesContractsSetFiscalComplement");
        salesContractsSetFiscalComplement.Parameter<Guid>("Key");
        salesContractsSetFiscalComplement.Parameter<int?>("UsageCode").Optional();
        salesContractsSetFiscalComplement.Parameter<int?>("PaymentConditionCode").Optional();
        salesContractsSetFiscalComplement.Parameter<string>("AdditionalInfo").Optional();
        salesContractsSetFiscalComplement.Parameter<string>("CustomerOrderNumber").Optional();
        salesContractsSetFiscalComplement.Parameter<string>("CustomerOrderItem").Optional();
        salesContractsSetFiscalComplement.Returns<SalesContractFiscalComplementDto>();
```

  Rota da function: `[HttpGet("odata/SalesContractsGetFiscalComplement(Key={key})")]` com `Guid key`. DI:
  `services.AddScoped<SalesContractFiscalComplementService>();`. Acrescente um caso ao teste de EDM mais próximo
  (por exemplo um `SalesContractFiscalComplementEdmModelTests` novo no molde de `PurchaseInvoiceNfeEdmModelTests`)
  conferindo `Edm.Int32` em `UsageCode` e `PaymentConditionCode`.

- [ ] **Step 4: Tests pass + build + full suite.**

- [ ] **Step 5: Commit** — `feat(sales-contract): ler e gravar o complemento fiscal do contrato` com o rodapé; pathspec
  com os arquivos criados/alterados desta task (faça `git add` dos novos antes).

---

### Task 3: Faturamento aplica o complemento (e recusa sem ele)

**Files:**
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesFiscalComplementApplier.cs`
- Modify: `SalesInvoicesCreateService.cs` (chamar antes do `usageGuard.ValidateAsync`, ~linha 54)
- Modify: `SalesInvoicesItemsCreateService.cs` (antes do `taxApply.ApplyAsync`, dentro do `if (invoice is not null)`)
- Modify: `SalesInvoicesItemsUpdateService.cs` (antes do cálculo de tributos da linha)
- Modify: `ServiceCollectionExtensions.cs` (DI)
- Modify: construções desses serviços nos testes (`Tests/Support/TaxTestServices.cs` ou onde forem montados) — parâmetro novo
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesFiscalComplementApplierTests.cs` (novo)

**Interfaces:**
- Consumes: Task 1 (`SalesContractFiscalComplements`, colunas do pedido).
- Produces:
  - `SalesInvoicesFiscalComplementApplier(IUnitOfWork db, TaxCalculationGate gate)`
  - `Task ApplyToDocumentAsync(SalesInvoice invoice)` — criação do documento.
  - `Task ApplyToLineAsync(SalesInvoice invoice, SalesInvoiceItem item)` — inclusão/alteração de linha.

- [ ] **Step 1: Failing tests** (InMemory; filial com `IssuesNfe` e `TaxTestServices.Gate(db, "STANDALONE")` como em
  `SalesInvoicesConfirmNfeGuardTests`; contrato com e sem complemento):
  - `Gate_active_without_complement_is_refused` → `DefaultException` com o texto exato (com o `Code` do contrato).
  - `Gate_active_with_incomplete_complement_is_refused` (sem condição).
  - `Gate_active_overwrites_usage_and_payment_condition` — linha com `UsageCode = 2` no corpo e documento com
    `PaymentConditionCode = 5` no corpo: depois de aplicar, linha `UsageCode == 3` (do complemento), documento `== 11`.
  - `Order_fields_are_copied_to_the_line`.
  - `Contract_text_goes_before_the_operator_text_and_is_not_duplicated` — `TaxPayerComments = "Placa ABC"`:
    vira `"Pedido 77 | Placa ABC"`; aplicar de novo não repete.
  - `Two_contracts_with_different_conditions_are_refused`.
  - `Gate_inactive_changes_nothing` (SAPB1 e STANDALONE sem a chave) — natureza/condição do corpo intactas, sem recusa mesmo sem complemento.
  - `Return_and_own_return_change_nothing`; `Line_without_contract_changes_nothing`.
  - `Line_path_sets_condition_only_when_header_is_empty_and_refuses_a_different_one` (`ApplyToLineAsync`).
  - Integração: `Shipment_billing_without_complement_creates_no_document` — monte como os testes de
    `ShipmentLoadBillingServiceTests` (Task F3 da peça 1 criou um caminho real) e confira que nenhuma linha de
    `SalesInvoices` foi gravada.

- [ ] **Step 2: Applier**

```csharp
/// <summary>
/// Aplica o complemento fiscal do contrato ao documento de saída (spec 2026-10-07 §4.2). Só na filial que emite NF-e
/// pelo Siagro, em documento Normal (não devolução), e só nas linhas com contrato. Natureza e condição do complemento
/// SOBRESCREVEM o corpo: quem fatura não decide o fiscal (D6).
/// </summary>
public class SalesInvoicesFiscalComplementApplier(IUnitOfWork db, TaxCalculationGate gate)
{
    private const string Separator = " | ";

    public async Task ApplyToDocumentAsync(SalesInvoice invoice)
    {
        var complements = await ResolveAsync(invoice, invoice.Items);
        if (complements is null) return;

        foreach (var item in invoice.Items)
            if (item.SalesContractKey is { } key) ApplyLine(item, complements[key]);

        var conditions = complements.Values.Select(c => c.PaymentConditionCode).Distinct().ToList();
        if (conditions.Count > 1)
            throw new DefaultException("Os contratos deste documento têm condições de pagamento diferentes no complemento fiscal.");
        invoice.PaymentConditionCode = conditions[0];

        foreach (var text in complements.Values.Select(c => c.AdditionalInfo).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct())
            if (invoice.TaxPayerComments?.Contains(text!) != true)
                invoice.TaxPayerComments = string.IsNullOrWhiteSpace(invoice.TaxPayerComments) ? text : text + Separator + invoice.TaxPayerComments;
    }

    public async Task ApplyToLineAsync(SalesInvoice invoice, SalesInvoiceItem item)
    {
        var complements = await ResolveAsync(invoice, [item]);
        if (complements is null || item.SalesContractKey is not { } key) return;

        var complement = complements[key];
        ApplyLine(item, complement);

        if (invoice.PaymentConditionCode is null)
            invoice.PaymentConditionCode = complement.PaymentConditionCode;
        else if (invoice.PaymentConditionCode != complement.PaymentConditionCode)
            throw new DefaultException("Os contratos deste documento têm condições de pagamento diferentes no complemento fiscal.");
    }

    /// <summary>Null = regra não se aplica. Recusa contrato sem complemento completo, antes de qualquer gravação.</summary>
    private async Task<Dictionary<Guid, SalesContractFiscalComplement>?> ResolveAsync(SalesInvoice invoice, IEnumerable<SalesInvoiceItem> items)
    {
        if (invoice.InvoiceType != SalesInvoiceType.Normal || invoice.IsNfeReturn) return null;

        var keys = items.Where(i => i.SalesContractKey.HasValue).Select(i => i.SalesContractKey!.Value).Distinct().ToList();
        if (keys.Count == 0 || !await gate.IsActiveAsync(invoice.BranchCode)) return null;

        var complements = await db.Context.SalesContractFiscalComplements.AsNoTracking()
            .Where(c => keys.Contains(c.SalesContractKey)).ToDictionaryAsync(c => c.SalesContractKey);

        foreach (var key in keys)
        {
            if (complements.TryGetValue(key, out var c) && c.UsageCode.HasValue && c.PaymentConditionCode.HasValue) continue;

            var code = await db.Context.SalesContracts.AsNoTracking().Where(x => x.Key == key).Select(x => x.Code).FirstOrDefaultAsync();
            throw new DefaultException(
                $"O contrato {code} não tem complemento fiscal com natureza de operação e condição de pagamento. Peça ao fiscal para completá-lo.");
        }

        return complements;
    }

    private static void ApplyLine(SalesInvoiceItem item, SalesContractFiscalComplement complement)
    {
        item.UsageCode = complement.UsageCode;
        item.CustomerOrderNumber = complement.CustomerOrderNumber;
        item.CustomerOrderItem = complement.CustomerOrderItem;
    }
}
```

- [ ] **Step 3: Wiring**
  - `SalesInvoicesCreateService`: injete o applier e chame `await fiscalComplement.ApplyToDocumentAsync(salesInvoice);`
    logo **antes** de `var lineUsages = await usageGuard.ValidateAsync(salesInvoice);` (o comentário: "Complemento fiscal
    do contrato antes da natureza: na filial que emite NF-e ele decide a natureza e a condição"). A cópia da condição
    do cliente (`??=`) logo abaixo continua — só preenche se o complemento não preencheu.
  - `SalesInvoicesItemsCreateService`: dentro do `if (invoice is not null)`, depois das travas e **antes** de
    `taxApply.ApplyAsync`: `await fiscalComplement.ApplyToLineAsync(invoice, salesInvoiceItem);`.
  - `SalesInvoicesItemsUpdateService`: carregue o documento da linha (se ainda não for carregado) e chame
    `ApplyToLineAsync(invoice, entity)` antes do cálculo de tributos/`SetValues` — leia o arquivo e escolha o ponto em que
    a natureza da linha ainda pode ser trocada antes de o imposto ser recalculado; descreva a escolha no relatório.
  - DI: `services.AddScoped<SalesInvoicesFiscalComplementApplier>();`
  - Atualize todos os lugares de teste que constroem esses três serviços (o compilador aponta) passando
    `new SalesInvoicesFiscalComplementApplier(db, TaxTestServices.Gate(db, erp))` ou o equivalente "inativo".

- [ ] **Step 4: Tests pass + full suite** (`dotnet test SiagroB1.Application.Tests`).

- [ ] **Step 5: Commit** — `feat(invoice): faturamento aplica o complemento fiscal do contrato` (corpo: a regra, só na
  filial que emite NF-e; "Atenção: natureza e condição do corpo são sobrescritas").

---

### Task 4: NF-e com `xPed`/`nItemPed`

**Files:**
- Modify: `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs` (`NfeItem`, perto de `Cest`)
- Modify: `SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs` (`BuildItem`, `prod = new prod { … }` ~linha 248)
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs` (~linha 157, o `with { Cest = … }`)
- Test: `SiagroB1.Fiscal.Tests` (teste do builder existente que verifica `det/prod`, ex.: o que cobre `CEST`) e
  `SiagroB1.Application.Tests/Nfe/NfeIssueInputAssemblerTests.cs`

**Interfaces:**
- Produces: `NfeItem.OrderNumber` (string?), `NfeItem.OrderItem` (string?).

- [ ] **Step 1: Failing tests**
  - Fiscal: item com `OrderNumber = "PO-77"`, `OrderItem = "1"` → o XML tem `<xPed>PO-77</xPed><nItemPed>1</nItemPed>`
    dentro de `det/prod`; sem os dois → nenhuma das tags. Siga o teste do `CEST` (procure `Cest` em `SiagroB1.Fiscal.Tests`).
  - Assembler: linha do documento de saída com `CustomerOrderNumber`/`CustomerOrderItem` → `NfeItem` com os mesmos valores.

- [ ] **Step 2: Implement**
  - `NfeItem`: `/// <summary>Pedido do cliente (xPed, até 15) e item (nItemPed, 1-6 dígitos); vazios = omitidos.</summary>`
    `public string? OrderNumber { get; init; }` e `public string? OrderItem { get; init; }`.
  - Builder, no `prod = new prod { … }`: `xPed = string.IsNullOrWhiteSpace(item.OrderNumber) ? null : Truncate(item.OrderNumber, 15),`
    e `nItemPed = string.IsNullOrWhiteSpace(item.OrderItem) ? null : item.OrderItem,` (confira o tipo de `nItemPed`
    na classe `prod` da Zeus — se for numérico, converta com `int.Parse`; registre no relatório).
    A ordem das tags no XSD (`xPed`/`nItemPed` vêm depois de `indTot` e antes de `nFCI`) é garantida pela
    serialização da Zeus; a validação de schema existente no fluxo de emissão confirma.
  - Assembler, no `with { … }`: `OrderNumber = (item as SalesInvoiceItem)?.CustomerOrderNumber,` e
    `OrderItem = (item as SalesInvoiceItem)?.CustomerOrderItem,`.

- [ ] **Step 3: Tests pass** — `dotnet test SiagroB1.Fiscal.Tests` e `dotnet test SiagroB1.Application.Tests` verdes.

- [ ] **Step 4: Commit** — `feat(invoice): pedido do cliente (xPed/nItemPed) na NF-e de saída`.

---

### Task 5: Frontend — seção "Complemento Fiscal" no contrato de venda

Repo `siagro-b1-frontend` (caminhos relativos a `webapp/`).

**Files:**
- Create: `view/salesContracts/fragments/SalesContractFiscalComplement.fragment.xml`
- Create: `view/salesContracts/fragments/FiscalComplementDialog.fragment.xml`
- Modify: `view/salesContracts/Detail.view.xml` (seção nova depois de "Dados do Contrato", ~linha 229)
- Modify: `controller/salesContracts/Detail.controller.ts`
- Modify: `model/ServerRoutes.ts` (junto de `itemsGetComplement`)

**Interfaces:**
- Consumes: function `SalesContractsGetFiscalComplement(Key=…)` → DTO (`UsageCode`, `UsageName`,
  `PaymentConditionCode`, `PaymentConditionName`, `AdditionalInfo`, `CustomerOrderNumber`, `CustomerOrderItem`,
  `UpdatedAt`, `UpdatedBy`, `IsComplete`) ou `null`; action `SalesContractsSetFiscalComplement`.

- [ ] **Step 1: Rotas** — `ServerRoutes.ts`: `salesContractsGetFiscalComplement: '/SalesContractsGetFiscalComplement(...)'`,
  `salesContractsSetFiscalComplement: '/SalesContractsSetFiscalComplement(...)'`.

- [ ] **Step 2: Seção** — `uxap:ObjectPageSection title="Complemento Fiscal"` com
  `visible="{= ${ui>/standalone} === true }"` (confira como o detalhe obtém `ui>/standalone` — `refreshStandaloneFlag`
  do `CommonController`; chame-o no `routeMatched` se ainda não for chamado). O fragmento mostra, ligado a um JSONModel
  `fiscalComplement` (não ao OData: os dados vêm da function):
  - `MessageStrip type="Warning"` visível quando `${fiscalComplement>/IsComplete} !== true`: "Este contrato ainda não
    tem complemento fiscal completo. Na filial que emite NF-e, ele não pode ser faturado sem natureza de operação e
    condição de pagamento.";
  - `sap.ui.layout.form.Form` + `ColumnLayout`, só leitura (`Text`): Natureza (`{fiscalComplement>/UsageCode} - {fiscalComplement>/UsageName}`
    — dois `Text` separados, binding composto quebra no V4 só para OData, aqui é JSON, mas mantenha um campo por propriedade),
    Condição de pagamento, Pedido do cliente / Item, Informações adicionais, Alterado por / em;
  - toolbar com **"Editar"** `visible="{fiscalComplement>/canEdit}"`.
  No controller: `loadFiscalComplement(contractKey)` (bindContext da function, `invoke`, `getObject()` — resposta pode ser
  nula → objeto vazio), `canEdit = SessionService.hasPermission("SALES_CONTRACT_FISCAL_EDIT")`; chamado no
  `routeMatched` e depois de salvar.

- [ ] **Step 3: Diálogo de edição** — `FiscalComplementDialog.fragment.xml` (Dialog, `Form`/`ColumnLayout`):
  - Natureza: `Input` `valueHelpOnly` com value help das naturezas de SAÍDA ativas — reaproveite o padrão de
    `openThirdPartyReturnUsageValueHelp` (`DialogHelper.openTableSelectDialog(this, "UsagesSelectDialog", ["Name",
    "Description"], [new Filter("Inactive", FilterOperator.EQ, false)], undefined, "Direction eq 'Outgoing'")`) num
    handler novo `openFiscalComplementUsageValueHelp` que grava `UsageCode`/`UsageName` no modelo do diálogo; botão
    "Limpar" ao lado.
  - Condição: idem com `openPaymentConditionsValueHelp` adaptado (handler novo que grava no modelo do diálogo); "Limpar".
  - Informações adicionais: `TextArea maxLength="2000" rows="4"`.
  - Pedido: `Input maxLength="15"`; Item do pedido: `Input maxLength="6"`.
  - Rodapé: Salvar (`onSaveFiscalComplement`, trava de reentrância antes do primeiro await) / Cancelar.
  Salvar: bindContext da action com `Key` e os campos (`UsageCode`/`PaymentConditionCode` como número ou `null`),
  `invoke`; sucesso → `MessageToast.show("Complemento fiscal salvo.")`, fecha, recarrega a seção; erro → a mensagem do
  servidor (handler global do OData), diálogo aberto.

- [ ] **Step 4: Gates** — `yarn ts-typecheck`, `yarn lint`.

- [ ] **Step 5: Commit** — `feat(sales-contract): seção e edição do complemento fiscal no contrato de venda`.

---

### Task 6: Frontend — complemento no diálogo de faturamento

**Files:**
- Modify: `helpers/ShipmentBillingDialog.ts`
- Modify: `view/shipmentBilling/fragments/Billing.fragment.xml`

**Interfaces:**
- Consumes: function `SalesContractsGetFiscalComplement` (Task 2); `ServerRoutes.salesContractsGetFiscalComplement` (Task 5).

- [ ] **Step 1** — No helper, quando a seleção da tabela de liberações (`shipmentBillingSalesContractsTable`, `t:Table`
  `selectionMode="Single"`) muda e `billing>/TaxLocked === true`: leia o `SalesContractKey` da liberação escolhida,
  chame a function e grave em `billing>/FiscalComplement` (objeto ou `null`). Ligue o evento `rowSelectionChange` da
  tabela a um handler do controller dono que delega ao helper (o fragmento tem `controller: host.controller`), no
  mesmo padrão de `saveBillingDialog`/`closeBillingDialog` (acrescente um `onBillingReleaseSelect` público nas duas
  telas donas: `shipmentBilling/Main.controller.ts` e `shipmentLoads/Detail.controller.ts`). Ao abrir o diálogo, zere
  `FiscalComplement`.
- [ ] **Step 2** — No fragmento, visível só com `billing>/TaxLocked`: um `MessageStrip type="Error"` quando houver
  liberação escolhida e `${billing>/FiscalComplement/IsComplete} !== true` com o texto da recusa do servidor (com o
  código do contrato da liberação: `billingReleases>` da linha ou guarde `ContractCode` no modelo ao selecionar); e os
  campos só leitura: Natureza (`UsageCode` + `UsageName`), Condição (`PaymentConditionName`), Pedido / Item e
  "Informações do contrato" (`AdditionalInfo`). O "Inf.Ad.Nota Fiscal" do operador continua como está.
- [ ] **Step 3: Gates** — `yarn ts-typecheck`, `yarn lint`.
- [ ] **Step 4: Commit** — `feat(invoice): diálogo de faturamento mostra o complemento fiscal do contrato`.

---

### Task 7: Verificação ponta a ponta (CEAGUI, homologação)

- [ ] **Step 1** — Aplicar as duas migrations **só** no `CEAGUI_SIAGRO_DEV`/`CEAGUI_SIAGRO_COMMON_DEV`, com
  `ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext …` e `--context CommonDbContext`.
  Conferir por SQL a tabela, as colunas e a permissão ligada ao ADMIN.
- [ ] **Step 2** — Stack `ceagui` + `yarn start:dev`, branch conferido nos dois repos.
- [ ] **Step 3: Roteiro** (dados fictícios; nunca os DS000001–146 reais):
  1. Contrato de venda fictício sem complemento → seção mostra o aviso; admin edita (natureza 3, condição 11, texto
     "Pedido do cliente 77 - descarga Porto X", pedido "PO77", item "1"); seção mostra os dados.
  2. Usuário sem a permissão (criar um usuário fictício com perfil sem ADMIN) → vê a seção, não vê "Editar"; a action
     chamada pela API devolve 400 com a mensagem de permissão.
  3. Faturar pela carga um contrato SEM complemento → o diálogo mostra o erro; confirmar → servidor recusa, nenhum
     documento criado (SQL).
  4. Faturar pela carga o contrato COM complemento → diálogo mostra natureza/condição/pedido só leitura; documento
     Confirmado com linha natureza 3, condição 11, `TaxPayerComments` começando pelo texto do contrato; Transmitir NF-e
     em homologação → autorizada; conferir no XML autorizado `natOp`, `xPed`/`nItemPed`, `infCpl` e as duplicatas.
  5. Documento avulso com linha do contrato e natureza 2 escolhida → após salvar, a linha tem natureza 3.
  6. Excluir um contrato fictício que tenha complemento (se a tela permitir excluir) → sem erro; a linha do complemento
     some (SQL).
- [ ] **Step 4** — Derrubar a stack por PID (portas 50000, 5246, 8081, 58000, 8080) e relatório.
