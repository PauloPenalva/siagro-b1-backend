# Complemento fiscal do contrato de compra — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Em STANDALONE, o fiscal informa a natureza de operação do contrato de compra (tabela de complemento + permissão própria), a aprovação passa a exigi-la, e a grade "Impostos" digitada vira um totalizador de Funrural calculado pela natureza, com "Líquido ao produtor".

**Architecture:** Espelho da peça 2b do contrato de venda: tabela 1:1 `PURCHASE_CONTRACT_FISCAL_COMPLEMENTS`, serviço `Get/Set` com permissão `PURCHASE_CONTRACT_FISCAL_EDIT`, function + action OData, seção "Complemento Fiscal" no Detail com diálogo. O totalizador sai do `PurchaseContractsTotalsService` existente, usando o `FunruralCalculator` da NF de Entrada sobre `TotalPrice`. Em SAPB1 nada muda.

**Tech Stack:** .NET 10, EF Core (SQL Server, InMemory nos testes), OData v4, xUnit; OpenUI5 + TypeScript, QUnit.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-13-purchase-contract-fiscal-complement-design.md`

## Global Constraints

- Dois repositórios: `C:\Projetos\SiagroB1\siagro-b1-backend` (Tasks 1–6) e `C:\Projetos\SiagroB1\siagro-b1-frontend` (Tasks 7–9). Branch `feature/purchase-contract-fiscal-complement` nos dois (backend já existe; criar no frontend a partir de `main` na Task 7). Conferir o branch antes de cada commit. **Nunca push.**
- Todo arquivo novo: `git add <path>` logo depois de criado, no repo dono.
- Commits: `tipo(escopo): descrição pt-BR minúscula sem ponto`; escopo `purchase-contract`; trailer `DB: <Migration>` quando o commit tiver migration; terminar com
  `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>` e
  `Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT`.
- Identificadores em inglês; texto ao usuário (mensagens, rótulos) em pt-BR; comentários em pt-BR.
- Só STANDALONE: modo via `ErpMode.IsStandalone(configuration)` (`SiagroB1.Infra/ErpMode.cs`) — **sem** a chave "Emite NF-e" da filial (spec D7).
- `UsageCode` **sem FK** (em SAPB1 a natureza é `OUSG`).
- Mensagens exatas (copiar literalmente):
  - sem permissão: `Você não tem permissão para alterar o complemento fiscal do contrato.`
  - SAPB1: `Complemento fiscal do contrato de compra não se aplica ao modo SAPB1.`
  - natureza inválida: `A natureza de operação {code} não é uma natureza de entrada ativa.`
  - status: `Contrato encerrado ou cancelado: o complemento fiscal não pode ser alterado.`
  - limpar aprovado: `Contrato aprovado precisa de natureza de operação: escolha outra em vez de limpar.`
  - aprovação: `Informe a natureza de operação do contrato (Complemento Fiscal) antes de aprovar.`
- Funrural: `FunruralCalculator.Calculate` (cada parcela `AwayFromZero` a 2). Base = `TotalPrice` (fixações Confirmed). Líquido = base − total. `FunruralType` não entra em cálculo.
- Migrations: só `dotnet ef migrations add` (não toca banco). **Nunca** `database update` neste plano sem `ASPNETCORE_ENVIRONMENT` explícito apontando para CEAGUI dev.
- Frontend: `yarn ts-typecheck && yarn lint` com o dev server parado. `yarn test` não passa no repo (gate de cobertura) — não é regressão.

## Review Focus

1. **Mensagem da aprovação recusada some na tela** — o controller devolve `BadRequest(string)`; o diálogo de aprovação tem de exibir o texto exato, não um genérico. Verificado no roteiro de navegador (Task 9, passo 2); Task 4 fixa a mensagem no teste do serviço.
2. **Contrato aprovado antes do deploy, sem natureza** — Detail tem de abrir normalmente, com aviso, totalizador zerado e líquido = base, sem erro. Teste na Task 5 (`No_usage_gives_zero_funrural_and_net_equals_base`) e passo 3 do roteiro.
3. **Natureza desativada depois de escolhida** — o totalizador continua usando as alíquotas dela (leitura ao vivo não filtra `Inactive`), e salvar de novo a mesma natureza é recusado. Teste na Task 2 (`Saving_an_inactivated_usage_again_is_refused`) e na Task 5 (`Inactive_usage_still_feeds_the_totals`).
4. **Troca rápida de contrato no Detail** — resposta atrasada da function de outro contrato não pode sobrescrever a seção (guarda `_fiscalContractKey`, Task 7).
5. **Duplo clique em Salvar no diálogo** — uma action só (trava `_fiscalSaving`, Task 7); no backend, salvar a mesma natureza não duplica log (Task 2, `Saving_same_usage_does_not_log`).

---

## File Structure

Backend (`siagro-b1-backend`):

| Arquivo | Responsabilidade |
|---|---|
| Create `SiagroB1.Domain/Entities/PurchaseContractFiscalComplement.cs` | entidade 1:1 |
| Create `SiagroB1.Domain/Dtos/PurchaseContractFiscalComplementDto.cs` | DTO PascalCase da function/action |
| Modify `SiagroB1.Domain/Entities/ContractChangeLogFields.cs` | `Usage` |
| Modify `SiagroB1.Domain/Entities/PurchaseContract.cs:141` | comentário em `FunruralType` |
| Modify `SiagroB1.Domain/Dtos/PurchaseContractTotalsResponseDto.cs` | campos de Funrural |
| Modify `SiagroB1.Infra/Context/AppDbContext.cs` (~111, ~275) | DbSet + relação 1:1 cascade |
| Create migrations `AppContext/*_AddPurchaseContractFiscalComplement`, `CommonContext/*_SeedPurchaseContractFiscalEditPermission` | |
| Modify `SiagroB1.Application/Interfaces/IUserPermissions.cs` | `PurchaseContractFiscalEdit` |
| Create `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractFiscalComplementService.cs` | Get/Set |
| Modify `PurchaseContractsApprovalService.cs`, `PurchaseContractsTotalsService.cs`, `PurchaseContractsDeleteService.cs` | guard, totalizador, remoção |
| Create `SiagroB1.Web/Functions/PurchaseContracts/PurchaseContractsGetFiscalComplementController.cs`, `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsSetFiscalComplementController.cs` | endpoints |
| Modify `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (~527), `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (~273) | EDM, DI |
| Tests `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplement*Tests.cs`, `PurchaseContractsApprovalFiscalGuardTests.cs`, `PurchaseContractsTotalsServiceTests.cs` | |

Frontend (`siagro-b1-frontend/webapp`):

| Arquivo | Responsabilidade |
|---|---|
| Modify `model/ServerRoutes.ts` (~45) | rotas |
| Modify `types/PurchaseContractsTotal.ts` | campos de Funrural |
| Create `helpers/PurchaseContractFunruralHelpers.ts` + `test/unit/helpers/PurchaseContractFunruralHelpers.qunit.ts` | linhas do quadro (pura) |
| Create `view/purchaseContracts/fragments/PurchaseContractFiscalComplement.fragment.xml`, `PurchaseContractFiscalComplementDialog.fragment.xml`, `PurchaseContractFunruralTotals.fragment.xml` | |
| Modify `controller/purchaseContracts/Detail.controller.ts`, `PurchaseContractsBaseController.ts` (`refreshContractTotals`), `approval/Detail.controller.ts`, `Add.controller.ts`, `Edit.controller.ts` | |
| Modify `view/purchaseContracts/Detail.view.xml`, `approval/Detail.view.xml`, `Add.view.xml`, `Edit.view.xml` | seções |
| Modify `model/formatter.ts` (`formatContractChangeLogField`) | rótulo `Usage` |

---

### Task 1: Modelo — entidade, permissão, campo de log e migrations

**Files:**
- Create: `SiagroB1.Domain/Entities/PurchaseContractFiscalComplement.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`, `SiagroB1.Domain/Entities/ContractChangeLogFields.cs`, `SiagroB1.Domain/Entities/PurchaseContract.cs`, `SiagroB1.Application/Interfaces/IUserPermissions.cs`
- Create: migrations (geradas)
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementModelTests.cs`

**Interfaces:**
- Produces: `PurchaseContractFiscalComplement { Guid PurchaseContractKey; PurchaseContract? PurchaseContract; int? UsageCode; DateTime? UpdatedAt; string? UpdatedBy }`; `AppDbContext.PurchaseContractFiscalComplements`; `PermissionCodes.PurchaseContractFiscalEdit = "PURCHASE_CONTRACT_FISCAL_EDIT"`; `ContractChangeLogFields.Usage = "Usage"`.

- [ ] **Step 1: Teste que falha**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractFiscalComplementModelTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public async Task Complement_is_keyed_by_the_contract()
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
        };
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractFiscalComplements.Add(new PurchaseContractFiscalComplement
        {
            PurchaseContractKey = contract.Key, UsageCode = 7,
        });
        await _db.Context.SaveChangesAsync();

        var saved = await _db.Context.PurchaseContractFiscalComplements.AsNoTracking().SingleAsync();
        Assert.Equal(contract.Key, saved.PurchaseContractKey);
        Assert.Equal(7, saved.UsageCode);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractFiscalComplementModelTests"`
Expected: erro de compilação — `PurchaseContractFiscalComplement` / `PurchaseContractFiscalComplements` não existem.

- [ ] **Step 3: Entidade**

`SiagroB1.Domain/Entities/PurchaseContractFiscalComplement.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Complemento FISCAL do contrato de compra (spec 2026-10-13): a natureza de operação, que dá as alíquotas de
/// Funrural do totalizador e (sub-projeto 2) da retenção no pagamento ao produtor. Separado do contrato porque
/// quem alimenta é o fiscal, com permissão própria (<c>PURCHASE_CONTRACT_FISCAL_EDIT</c>) — molde de
/// <see cref="SalesContractFiscalComplement"/>.
/// <para>Sem FK para a natureza: em SAPB1 ela vem do <c>OUSG</c>. Só usado em STANDALONE.</para>
/// </summary>
[Table("PURCHASE_CONTRACT_FISCAL_COMPLEMENTS")]
public class PurchaseContractFiscalComplement
{
    public Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public int? UsageCode { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }
}
```

`git add SiagroB1.Domain/Entities/PurchaseContractFiscalComplement.cs`

- [ ] **Step 4: DbSet e relação**

Em `AppDbContext.cs`, logo abaixo da linha 111 (`SalesContractFiscalComplements`):

```csharp
    public DbSet<PurchaseContractFiscalComplement> PurchaseContractFiscalComplements { get; set; }
```

Logo depois do bloco `modelBuilder.Entity<SalesContractFiscalComplement>(...)` (~275-281):

```csharp
        // 1:1 com o contrato de compra; excluir o contrato exclui o complemento. Sem navegação inversa no
        // contrato: entraria no EDM do OData sem ninguém pedir (a leitura é pela function).
        modelBuilder.Entity<PurchaseContractFiscalComplement>(e =>
        {
            e.HasKey(x => x.PurchaseContractKey);
            e.HasOne(x => x.PurchaseContract).WithOne()
                .HasForeignKey<PurchaseContractFiscalComplement>(x => x.PurchaseContractKey)
                .OnDelete(DeleteBehavior.Cascade);
        });
```

- [ ] **Step 5: Permissão, campo de log e comentário do FunruralType**

`IUserPermissions.cs`, dentro de `PermissionCodes`, depois de `SalesContractFiscalEdit`:

```csharp

    /// <summary>Editar o complemento fiscal (natureza de operação) do contrato de compra.</summary>
    public const string PurchaseContractFiscalEdit = "PURCHASE_CONTRACT_FISCAL_EDIT";
```

`ContractChangeLogFields.cs`, depois de `Comment`:

```csharp

    /// <summary>Natureza de operação do complemento fiscal do contrato de compra. Valores "código - nome".</summary>
    public const string Usage = "Usage";
```

`PurchaseContract.cs:141` — acima de `public FunruralType? FunruralType`:

```csharp
    /// <summary>
    /// Só COMERCIAL (spec 2026-10-13 D4): diz ao produtor como o preço foi negociado e sai na impressão.
    /// Não entra em cálculo nenhum — o Funrural é sempre retido sobre o preço cheio, com as alíquotas da
    /// natureza do complemento fiscal. Não "conecte" este campo ao totalizador.
    /// </summary>
```

- [ ] **Step 6: Rodar o teste**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractFiscalComplementModelTests"`
Expected: PASS.

- [ ] **Step 7: Migration da tabela**

Run (na raiz do backend):
```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add AddPurchaseContractFiscalComplement --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Conferir no `Up` gerado: `CreateTable PURCHASE_CONTRACT_FISCAL_COMPLEMENTS` com PK `PurchaseContractKey`, FK para `PURCHASE_CONTRACTS` com `onDelete: ReferentialAction.Cascade`, e **nenhuma outra** operação (se aparecer alteração em tabela alheia, o snapshot estava sujo — parar e investigar). `git add` dos 3 arquivos gerados (migration, Designer, snapshot).

- [ ] **Step 8: Migration do seed da permissão**

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add SeedPurchaseContractFiscalEditPermission --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Substituir `Up`/`Down` do arquivo gerado por:

```csharp
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente: o código pode já ter sido cadastrado à mão. O vínculo com ADMIN só nasce se o papel existir (FK).
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PURCHASE_CONTRACT_FISCAL_EDIT')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('PURCHASE_CONTRACT_FISCAL_EDIT', 'Editar o complemento fiscal do contrato de compra');
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                   AND NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'ADMIN' AND PermissionCode = 'PURCHASE_CONTRACT_FISCAL_EDIT')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode)
                    VALUES (NEWID(), 'ADMIN', 'PURCHASE_CONTRACT_FISCAL_EDIT');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ROLE_PERMISSIONS WHERE PermissionCode = 'PURCHASE_CONTRACT_FISCAL_EDIT';");
            migrationBuilder.Sql("DELETE FROM PERMISSIONS WHERE Code = 'PURCHASE_CONTRACT_FISCAL_EDIT';");
        }
```

O snapshot do `CommonDbContext` **não** deve mudar (é só SQL). `git add` da migration e do Designer.

- [ ] **Step 9: Gates de modelo**

```bash
dotnet build SiagroB1.sln
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef migrations has-pending-model-changes --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Expected: build OK; os dois "No changes have been made to the model since the last migration."

- [ ] **Step 10: Commit**

```bash
git add -A SiagroB1.Domain SiagroB1.Infra SiagroB1.Application/Interfaces SiagroB1.Migrations SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementModelTests.cs
git commit -m "feat(purchase-contract): complemento fiscal do contrato de compra no modelo" -m "Natureza de operação do contrato de compra em tabela 1:1 à parte, alimentada pelo fiscal com permissão própria (molde da peça 2b da venda). FunruralType documentado como só comercial." -m "DB: AddPurchaseContractFiscalComplement, SeedPurchaseContractFiscalEditPermission" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT"
```

---

### Task 2: Serviço `PurchaseContractFiscalComplementService` (Get/Set)

**Files:**
- Create: `SiagroB1.Domain/Dtos/PurchaseContractFiscalComplementDto.cs`
- Create: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractFiscalComplementService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (~273)
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 (entidade, `PermissionCodes.PurchaseContractFiscalEdit`, `ContractChangeLogFields.Usage`); `IUsage.GetByIdAsync(int) → UsageModel?`; `PurchaseContractsChangeLogService.Register(Guid, string, string?, string?, string)`; `ErpMode.IsStandalone(IConfiguration)`.
- Produces:
  - `PurchaseContractFiscalComplementService(IUnitOfWork db, IUsage usageService, IUserPermissions permissions, PurchaseContractsChangeLogService changeLog, IConfiguration configuration)`
  - `Task<PurchaseContractFiscalComplementDto?> GetAsync(Guid purchaseContractKey)` — null sem linha
  - `Task<PurchaseContractFiscalComplementDto> SetAsync(Guid purchaseContractKey, int? usageCode, string userName)`
  - DTO `PurchaseContractFiscalComplementDto { Guid PurchaseContractKey; int? UsageCode; string? UsageName; decimal? FunruralInssRate; decimal? FunruralRatRate; decimal? FunruralSenarRate; DateTime? UpdatedAt; string? UpdatedBy; bool IsComplete }`

- [ ] **Step 1: Testes que falham**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using SiagroB1.Application.Interfaces;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractFiscalComplementServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private const string User = "fiscal";

    private UsageService Usages() => new(_db, NullLogger<UsageService>.Instance);

    private PurchaseContractFiscalComplementService Service(bool granted = true, string mode = "STANDALONE") =>
        new(_db, Usages(),
            granted ? new FakeUserPermissions(PermissionCodes.PurchaseContractFiscalEdit) : new FakeUserPermissions(),
            new PurchaseContractsChangeLogService(_db.Context),
            TestConfiguration.Erp(mode));

    private async Task<Guid> SeedContractAsync(ContractStatus status = ContractStatus.Draft)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01", Status = status,
        };
        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract.Key;
    }

    private async Task<int> SeedUsageAsync(
        string name = "Compra produtor PF", UsageDirection direction = UsageDirection.Incoming, bool inactive = false,
        decimal? inss = 1.32m, decimal? rat = 0.11m, decimal? senar = 0.20m)
    {
        var model = new UsageModel
        {
            Name = name, Direction = direction, Inactive = inactive,
            FunruralInssRate = inss, FunruralRatRate = rat, FunruralSenarRate = senar,
        };
        if (direction == UsageDirection.Incoming) { model.CfopIncomingInState = "1102"; model.CfopIncomingOutState = "2102"; }
        else { model.CfopOutgoingInState = "5102"; model.CfopOutgoingOutState = "6102"; }
        return (await Usages().CreateAsync(model)).Code;
    }

    [Fact]
    public async Task Without_permission_is_refused()
    {
        var key = await SeedContractAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(granted: false).SetAsync(key, null, User));
        Assert.Equal("Você não tem permissão para alterar o complemento fiscal do contrato.", e.Message);
    }

    [Fact]
    public async Task Sapb1_mode_is_refused()
    {
        var key = await SeedContractAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(mode: "SAPB1").SetAsync(key, null, User));
        Assert.Equal("Complemento fiscal do contrato de compra não se aplica ao modo SAPB1.", e.Message);
    }

    [Fact]
    public async Task Unknown_contract_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Service().SetAsync(Guid.NewGuid(), null, User));
    }

    [Theory]
    [InlineData(ContractStatus.Finished)]
    [InlineData(ContractStatus.Canceled)]
    public async Task Finished_or_canceled_contract_is_refused(ContractStatus status)
    {
        var key = await SeedContractAsync(status);
        var usage = await SeedUsageAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, usage, User));
        Assert.Equal("Contrato encerrado ou cancelado: o complemento fiscal não pode ser alterado.", e.Message);
    }

    [Fact]
    public async Task Outgoing_usage_is_refused()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync(direction: UsageDirection.Outgoing);
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, usage, User));
        Assert.Equal($"A natureza de operação {usage} não é uma natureza de entrada ativa.", e.Message);
    }

    [Fact]
    public async Task Inactive_usage_is_refused()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync(inactive: true);
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, usage, User));
        Assert.Equal($"A natureza de operação {usage} não é uma natureza de entrada ativa.", e.Message);
    }

    [Fact]
    public async Task Unknown_usage_is_refused()
    {
        var key = await SeedContractAsync();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, 999, User));
        Assert.Equal("A natureza de operação 999 não é uma natureza de entrada ativa.", e.Message);
    }

    [Fact]
    public async Task Usage_without_funrural_rates_is_accepted()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync(name: "Compra sem retenção", inss: null, rat: null, senar: null);

        var dto = await Service().SetAsync(key, usage, User);

        Assert.Equal(usage, dto.UsageCode);
        Assert.True(dto.IsComplete);
        Assert.Null(dto.FunruralInssRate);
    }

    [Fact]
    public async Task Set_creates_the_complement_and_returns_names_and_rates()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync();

        var dto = await Service().SetAsync(key, usage, User);

        Assert.Equal("Compra produtor PF", dto.UsageName);
        Assert.Equal(1.32m, dto.FunruralInssRate);
        Assert.Equal(0.11m, dto.FunruralRatRate);
        Assert.Equal(0.20m, dto.FunruralSenarRate);
        Assert.Equal(User, dto.UpdatedBy);
        Assert.True(dto.IsComplete);
    }

    [Fact]
    public async Task Get_without_complement_returns_null()
    {
        var key = await SeedContractAsync();
        Assert.Null(await Service().GetAsync(key));
    }

    [Fact]
    public async Task Changing_usage_logs_old_and_new()
    {
        var key = await SeedContractAsync(ContractStatus.Approved);
        var first = await SeedUsageAsync(name: "Compra PF");
        var second = await SeedUsageAsync(name: "Compra PJ", inss: 1.98m, rat: null, senar: 0.25m);

        await Service().SetAsync(key, first, User);
        await Service().SetAsync(key, second, User);

        var logs = await _db.Context.PurchaseContractsChangeLogs.AsNoTracking()
            .Where(x => x.PurchaseContractKey == key && x.Field == ContractChangeLogFields.Usage)
            .OrderBy(x => x.ChangedAt).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Null(logs[0].OldValue);
        Assert.Equal($"{first} - Compra PF", logs[0].NewValue);
        Assert.Equal($"{first} - Compra PF", logs[1].OldValue);
        Assert.Equal($"{second} - Compra PJ", logs[1].NewValue);
    }

    [Fact]
    public async Task Saving_same_usage_does_not_log()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync();

        await Service().SetAsync(key, usage, User);
        await Service().SetAsync(key, usage, User);

        Assert.Equal(1, await _db.Context.PurchaseContractsChangeLogs
            .CountAsync(x => x.PurchaseContractKey == key && x.Field == ContractChangeLogFields.Usage));
    }

    [Fact]
    public async Task Clearing_on_draft_is_accepted()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync();
        await Service().SetAsync(key, usage, User);

        var dto = await Service().SetAsync(key, null, User);

        Assert.Null(dto.UsageCode);
        Assert.False(dto.IsComplete);
    }

    [Fact]
    public async Task Clearing_on_approved_contract_is_refused()
    {
        var key = await SeedContractAsync(ContractStatus.Approved);
        var usage = await SeedUsageAsync();
        await Service().SetAsync(key, usage, User);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, null, User));
        Assert.Equal("Contrato aprovado precisa de natureza de operação: escolha outra em vez de limpar.", e.Message);
    }

    [Fact]
    public async Task Saving_an_inactivated_usage_again_is_refused()
    {
        var key = await SeedContractAsync();
        var usage = await SeedUsageAsync();
        await Service().SetAsync(key, usage, User);
        var row = await _db.Context.Usages.SingleAsync(x => x.Code == usage);
        row.Inactive = true;
        await _db.Context.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().SetAsync(key, usage, User));
        Assert.Equal($"A natureza de operação {usage} não é uma natureza de entrada ativa.", e.Message);
    }
}
```

> Se `_db.Context.Usages` tiver outro nome de DbSet, ajustar pelo `AppDbContext` (é o `DbSet<Usage>` de `USAGES`).

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractFiscalComplementServiceTests"`
Expected: erro de compilação — serviço e DTO não existem.

- [ ] **Step 3: DTO**

`SiagroB1.Domain/Dtos/PurchaseContractFiscalComplementDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Complemento fiscal do contrato de compra, com nome e alíquotas da natureza só para exibição.
/// PascalCase explícito: function com <c>Returns&lt;IActionResult&gt;</c> sai camelCase sem isto.
/// </summary>
public class PurchaseContractFiscalComplementDto
{
    [JsonPropertyName("PurchaseContractKey")]
    public Guid PurchaseContractKey { get; set; }

    [JsonPropertyName("UsageCode")]
    public int? UsageCode { get; set; }

    [JsonPropertyName("UsageName")]
    public string? UsageName { get; set; }

    [JsonPropertyName("FunruralInssRate")]
    public decimal? FunruralInssRate { get; set; }

    [JsonPropertyName("FunruralRatRate")]
    public decimal? FunruralRatRate { get; set; }

    [JsonPropertyName("FunruralSenarRate")]
    public decimal? FunruralSenarRate { get; set; }

    [JsonPropertyName("UpdatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("UpdatedBy")]
    public string? UpdatedBy { get; set; }

    /// <summary>Natureza preenchida — é o que a aprovação exige.</summary>
    [JsonPropertyName("IsComplete")]
    public bool IsComplete { get; set; }
}
```

`git add` o arquivo.

- [ ] **Step 4: Serviço**

`SiagroB1.Application/Services/PurchaseContracts/PurchaseContractFiscalComplementService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using SiagroB1.Application.Interfaces;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Complemento fiscal do contrato de compra (spec 2026-10-13). Só STANDALONE; só quem tem
/// PURCHASE_CONTRACT_FISCAL_EDIT grava. Toda troca efetiva de natureza vai para o log do contrato, no mesmo
/// SaveChanges.
/// </summary>
public class PurchaseContractFiscalComplementService(
    IUnitOfWork db,
    IUsage usageService,
    IUserPermissions permissions,
    PurchaseContractsChangeLogService changeLog,
    IConfiguration configuration)
{
    public async Task<PurchaseContractFiscalComplementDto?> GetAsync(Guid purchaseContractKey)
    {
        var entity = await db.Context.PurchaseContractFiscalComplements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PurchaseContractKey == purchaseContractKey);
        if (entity is null) return null;

        // Leitura ao vivo pelo IUsage: natureza desativada depois de escolhida continua mostrando as alíquotas.
        var usage = entity.UsageCode is { } code ? await usageService.GetByIdAsync(code) : null;

        return new PurchaseContractFiscalComplementDto
        {
            PurchaseContractKey = entity.PurchaseContractKey,
            UsageCode = entity.UsageCode,
            UsageName = usage?.Name,
            FunruralInssRate = usage?.FunruralInssRate,
            FunruralRatRate = usage?.FunruralRatRate,
            FunruralSenarRate = usage?.FunruralSenarRate,
            UpdatedAt = entity.UpdatedAt,
            UpdatedBy = entity.UpdatedBy,
            IsComplete = entity.UsageCode.HasValue,
        };
    }

    public async Task<PurchaseContractFiscalComplementDto> SetAsync(Guid purchaseContractKey, int? usageCode, string userName)
    {
        if (!await permissions.HasAsync(userName, PermissionCodes.PurchaseContractFiscalEdit))
            throw new DefaultException("Você não tem permissão para alterar o complemento fiscal do contrato.");

        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException("Complemento fiscal do contrato de compra não se aplica ao modo SAPB1.");

        var status = await db.Context.PurchaseContracts.AsNoTracking()
            .Where(x => x.Key == purchaseContractKey)
            .Select(x => (ContractStatus?)x.Status)
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException($"Purchase contract not found key {purchaseContractKey}");

        if (status is ContractStatus.Finished or ContractStatus.Canceled)
            throw new DefaultException("Contrato encerrado ou cancelado: o complemento fiscal não pode ser alterado.");

        UsageModel? newUsage = null;
        if (usageCode is { } code)
        {
            newUsage = await usageService.GetByIdAsync(code);
            if (newUsage is null || newUsage.Inactive || newUsage.Direction != UsageDirection.Incoming)
                throw new DefaultException($"A natureza de operação {code} não é uma natureza de entrada ativa.");
        }
        else if (status == ContractStatus.Approved)
        {
            // A aprovação exigiu a natureza; limpar reabriria a condição sem passar por ela de novo.
            throw new DefaultException("Contrato aprovado precisa de natureza de operação: escolha outra em vez de limpar.");
        }

        var entity = await db.Context.PurchaseContractFiscalComplements
            .FirstOrDefaultAsync(x => x.PurchaseContractKey == purchaseContractKey);
        if (entity is null)
        {
            entity = new PurchaseContractFiscalComplement { PurchaseContractKey = purchaseContractKey };
            await db.Context.PurchaseContractFiscalComplements.AddAsync(entity);
        }

        if (entity.UsageCode != usageCode)
        {
            var oldUsage = entity.UsageCode is { } oldCode ? await usageService.GetByIdAsync(oldCode) : null;
            changeLog.Register(purchaseContractKey, ContractChangeLogFields.Usage,
                Describe(entity.UsageCode, oldUsage), Describe(usageCode, newUsage), userName);
        }

        entity.UsageCode = usageCode;
        entity.UpdatedAt = DateTime.Now;
        entity.UpdatedBy = userName;

        await db.SaveChangesAsync();
        return (await GetAsync(purchaseContractKey))!;
    }

    private static string? Describe(int? code, UsageModel? usage) =>
        code is null ? null : $"{code} - {usage?.Name}";
}
```

`git add` o arquivo.

> Conferir que `PurchaseContractsChangeLogService` recebe o **mesmo** `AppDbContext` do `IUnitOfWork` no DI (ambos scoped) — é o que põe log e alteração no mesmo `SaveChanges`. Nos testes é `_db.Context`.

- [ ] **Step 5: DI**

`ServiceCollectionExtensions.cs`, abaixo de `services.AddScoped<SalesContractFiscalComplementService>();` (~273):

```csharp
        services.AddScoped<PurchaseContractFiscalComplementService>();
```

- [ ] **Step 6: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractFiscalComplementServiceTests"`
Expected: todos PASS.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractFiscalComplementService.cs SiagroB1.Domain/Dtos/PurchaseContractFiscalComplementDto.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementServiceTests.cs
git commit -m "feat(purchase-contract): gravar a natureza de operação do contrato de compra" -m "Só o fiscal (PURCHASE_CONTRACT_FISCAL_EDIT) grava, só em STANDALONE, só natureza de entrada ativa. Cada troca vai para o log do contrato. Limpar em contrato aprovado é recusado porque a aprovação exige a natureza." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT"
```

---

### Task 3: Endpoints OData

**Files:**
- Create: `SiagroB1.Web/Functions/PurchaseContracts/PurchaseContractsGetFiscalComplementController.cs`
- Create: `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsSetFiscalComplementController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (~527, logo após `salesContractsSetFiscalComplement`)
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementEdmModelTests.cs`

**Interfaces:**
- Consumes: Task 2 (`GetAsync`, `SetAsync(Guid, int?, string)`, DTO).
- Produces: `GET odata/PurchaseContractsGetFiscalComplement(Key={key})` → DTO ou `null`; `POST odata/PurchaseContractsSetFiscalComplement` body `{ Key, UsageCode }` → DTO.

- [ ] **Step 1: Teste de EDM que falha**

```csharp
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractFiscalComplementEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void The_action_usage_code_travels_as_nullable_int32()
    {
        var action = BuildModel().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "PurchaseContractsSetFiscalComplement");
        var parameter = action.Parameters.Single(p => p.Name == "UsageCode");
        Assert.Equal("Edm.Int32", parameter.Type.Definition.FullTypeName());
        Assert.True(parameter.Type.IsNullable);
    }

    [Fact]
    public void The_function_is_exposed_with_its_key()
    {
        var function = BuildModel().SchemaElements.OfType<IEdmFunction>()
            .Single(f => f.Name == "PurchaseContractsGetFiscalComplement");
        Assert.Contains(function.Parameters, p => p.Name == "Key");
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractFiscalComplementEdmModelTests"`
Expected: FAIL — `Sequence contains no matching element`.

- [ ] **Step 3: EDM**

`ODataConfigurations.cs`, logo depois de `salesContractsSetFiscalComplement.Returns<SalesContractFiscalComplementDto>();`:

```csharp

        var purchaseContractsGetFiscalComplement = modelBuilder.Function("PurchaseContractsGetFiscalComplement");
        purchaseContractsGetFiscalComplement.Parameter<Guid>("Key");
        purchaseContractsGetFiscalComplement.Returns<PurchaseContractFiscalComplementDto>();

        var purchaseContractsSetFiscalComplement = modelBuilder.Action("PurchaseContractsSetFiscalComplement");
        purchaseContractsSetFiscalComplement.Parameter<Guid>("Key");
        // Opcional e anulável: null limpa a natureza (só fora de contrato aprovado — regra no serviço).
        purchaseContractsSetFiscalComplement.Parameter<int?>("UsageCode").Optional();
        purchaseContractsSetFiscalComplement.Returns<PurchaseContractFiscalComplementDto>();
```

- [ ] **Step 4: Controllers**

`SiagroB1.Web/Functions/PurchaseContracts/PurchaseContractsGetFiscalComplementController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;

using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Web.Functions.PurchaseContracts;

public class PurchaseContractsGetFiscalComplementController(PurchaseContractFiscalComplementService service)
    : ODataController
{
    /// <summary>Devolve <c>Ok(null)</c> sem linha: contrato sem complemento é o caso comum, não um erro.</summary>
    [HttpGet("odata/PurchaseContractsGetFiscalComplement(Key={key})")]
    public async Task<IActionResult> Get([FromRoute] Guid key)
    {
        var result = await service.GetAsync(key);

        return Ok(result);
    }
}
```

`SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsSetFiscalComplementController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;

using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsSetFiscalComplementController(PurchaseContractFiscalComplementService service)
    : ODataController
{
    /// <remarks>
    /// <paramref name="parameters"/> vem NULO sem nenhum parâmetro do EDM, e <c>UsageCode</c> presente pode vir
    /// nulo (limpar é válido). Código numérico chega como int; nada de <c>.ToString()</c> direto.
    /// </remarks>
    [HttpPost("odata/PurchaseContractsSetFiscalComplement")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null ||
                !parameters.TryGetValue("Key", out var keyObj) ||
                keyObj is not Guid key)
            {
                return BadRequest("Missing required parameters");
            }

            parameters.TryGetValue("UsageCode", out var usageObj);

            var result = await service.SetAsync(
                key, usageObj is null ? null : Convert.ToInt32(usageObj), User.Identity?.Name ?? "Unknown");

            return Ok(result);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }
}
```

`git add` os dois arquivos.

- [ ] **Step 5: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractFiscalComplementEdmModelTests"` e `dotnet build SiagroB1.sln`
Expected: PASS; build OK.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Web/Functions/PurchaseContracts/PurchaseContractsGetFiscalComplementController.cs SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsSetFiscalComplementController.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementEdmModelTests.cs
git commit -m "feat(purchase-contract): endpoints do complemento fiscal do contrato de compra" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT"
```

---

### Task 4: Aprovação exige a natureza (STANDALONE)

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsApprovalService.cs`
- Modify (call sites do construtor): `SiagroB1.Application.Tests/Financials/FinancialDocumentHooksTests.cs:98,134`, `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractNotificationEventTests.cs:80`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsApprovalFiscalGuardTests.cs`

**Interfaces:**
- Consumes: Task 1 (`PurchaseContractFiscalComplements`).
- Produces: construtor `PurchaseContractsApprovalService(AppDbContext context, ContractNotificationOutboxService notificationOutbox, FinancialDocumentsGenerateService financialDocuments, IConfiguration configuration)`.

- [ ] **Step 1: Teste que falha**

Ler `PurchaseContractNotificationEventTests.cs:25-60` e copiar os helpers `Outbox()` e `FinancialDocuments()` para dentro do teste novo (mesmo corpo). Depois:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
// + os usings que os helpers copiados exigirem

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractsApprovalFiscalGuardTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    // Outbox() e FinancialDocuments(): cópia literal de PurchaseContractNotificationEventTests.

    private PurchaseContractsApprovalService Service(string mode) =>
        new(_db.Context, Outbox(), FinancialDocuments(), TestConfiguration.Erp(mode));

    /// <summary>Contrato a fixar em aprovação: sem fixação confirmada, a aprovação não gera título nem exige vencimento.</summary>
    private async Task<Guid> SeedInApprovalAsync()
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
            Type = ContractType.ToBeDetermined, Status = ContractStatus.InApproval,
        };
        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract.Key;
    }

    [Fact]
    public async Task Standalone_without_complement_is_refused_and_nothing_changes()
    {
        var key = await SeedInApprovalAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service("STANDALONE").ExecuteAsync(key, null, "diretoria"));

        Assert.Equal("Informe a natureza de operação do contrato (Complemento Fiscal) antes de aprovar.", e.Message);
        var status = await _db.Context.PurchaseContracts.AsNoTracking().Where(x => x.Key == key).Select(x => x.Status).SingleAsync();
        Assert.Equal(ContractStatus.InApproval, status);
    }

    [Fact]
    public async Task Standalone_with_empty_complement_is_refused()
    {
        var key = await SeedInApprovalAsync();
        _db.Context.PurchaseContractFiscalComplements.Add(new PurchaseContractFiscalComplement { PurchaseContractKey = key });
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => Service("STANDALONE").ExecuteAsync(key, null, "diretoria"));
    }

    [Fact]
    public async Task Standalone_with_usage_is_approved()
    {
        var key = await SeedInApprovalAsync();
        _db.Context.PurchaseContractFiscalComplements.Add(new PurchaseContractFiscalComplement { PurchaseContractKey = key, UsageCode = 7 });
        await _db.Context.SaveChangesAsync();

        await Service("STANDALONE").ExecuteAsync(key, null, "diretoria");

        var status = await _db.Context.PurchaseContracts.AsNoTracking().Where(x => x.Key == key).Select(x => x.Status).SingleAsync();
        Assert.Equal(ContractStatus.Approved, status);
    }

    [Fact]
    public async Task Sapb1_does_not_require_the_usage()
    {
        var key = await SeedInApprovalAsync();

        await Service("SAPB1").ExecuteAsync(key, null, "diretoria");

        var status = await _db.Context.PurchaseContracts.AsNoTracking().Where(x => x.Key == key).Select(x => x.Status).SingleAsync();
        Assert.Equal(ContractStatus.Approved, status);
    }
}
```

> Se `PurchaseContract` exigir mais campos obrigatórios para salvar no InMemory, copiar do `Seed` de `PurchaseContractNotificationEventTests`.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsApprovalFiscalGuardTests"`
Expected: erro de compilação (construtor com 4 argumentos não existe).

- [ ] **Step 3: Implementação**

`PurchaseContractsApprovalService.cs` — usings `Microsoft.Extensions.Configuration` e `SiagroB1.Infra`; construtor ganha `IConfiguration configuration`; logo depois de buscar o contrato e **antes** de `contract.Status = ContractStatus.Approved;`:

```csharp
        // Spec 2026-10-13 D5/D7: em STANDALONE o contrato só aprova com natureza de operação — é ela que dá as
        // alíquotas de Funrural do pagamento ao produtor. Todas as filiais STANDALONE, emitam NF-e ou não.
        // Antes de qualquer atribuição: nada pode ficar sujo no contexto se recusar.
        if (ErpMode.IsStandalone(configuration) &&
            !await context.PurchaseContractFiscalComplements.AnyAsync(x => x.PurchaseContractKey == key && x.UsageCode != null))
        {
            throw new DefaultException("Informe a natureza de operação do contrato (Complemento Fiscal) antes de aprovar.");
        }
```

(`DefaultException` vem de `SiagroB1.Domain.Exceptions`, já importado.)

- [ ] **Step 4: Ajustar os call sites existentes**

Os testes antigos de aprovação não são sobre natureza: rodam em SAPB1 para continuarem focados no que testam.

- `FinancialDocumentHooksTests.cs:98` e `:134`: `new PurchaseContractsApprovalService(act, Outbox(act), FinancialDocuments(act), TestConfiguration.Erp("SAPB1"))`
- `PurchaseContractNotificationEventTests.cs:80`: `new PurchaseContractsApprovalService(_db.Context, Outbox(), FinancialDocuments(), TestConfiguration.Erp("SAPB1"))`

Depois: `grep -rn "new PurchaseContractsApprovalService" SiagroB1.Application.Tests` não pode ter nenhum call site com 3 argumentos.

- [ ] **Step 5: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsApprovalFiscalGuardTests|FullyQualifiedName~FinancialDocumentHooksTests|FullyQualifiedName~PurchaseContractNotificationEventTests"`
Expected: todos PASS.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsApprovalService.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsApprovalFiscalGuardTests.cs SiagroB1.Application.Tests/Financials/FinancialDocumentHooksTests.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractNotificationEventTests.cs
git commit -m "feat(purchase-contract): aprovação exige a natureza de operação em STANDALONE" -m "Sem natureza não há alíquota de Funrural para reter no pagamento ao produtor. Vale para todas as filiais STANDALONE (inclusive a MH Agro, que não emite NF-e); contratos já aprovados não são afetados." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT"
```

---

### Task 5: Totalizador de Funrural nos totais do contrato

**Files:**
- Modify: `SiagroB1.Domain/Dtos/PurchaseContractTotalsResponseDto.cs`
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsTotalsService.cs`
- Modify (call sites): `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsTotalsServiceTests.cs:33`, `SiagroB1.Application.Tests/PurchaseContracts/Washouts/WashoutBalanceConsumersTests.cs:184`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsTotalsServiceTests.cs`

**Interfaces:**
- Consumes: Task 1; `IUsage.GetByIdAsync`; `SiagroB1.Fiscal.Taxes.FunruralCalculator.Calculate(decimal, decimal?, decimal?, decimal?) → FunruralResult`.
- Produces: construtor `PurchaseContractsTotalsService(AppDbContext context, IUsage usageService, IConfiguration configuration)`; DTO += `FunruralBase, FunruralInssRate, FunruralRatRate, FunruralSenarRate, FunruralInssValue, FunruralRatValue, FunruralSenarValue, TotalFunrural, NetToProducer` (todos `decimal`, JSON PascalCase).

- [ ] **Step 1: Testes que falham**

Acrescentar a `PurchaseContractsTotalsServiceTests` (e ajustar o teste existente ao novo construtor, ver Step 4):

```csharp
    private UsageService Usages() => new(_db, NullLogger<UsageService>.Instance);

    private PurchaseContractsTotalsService Service(string mode = "STANDALONE") =>
        new(_db.Context, Usages(), TestConfiguration.Erp(mode));

    /// <summary>Contrato com uma fixação CONFIRMADA de 1.234,567 × 2,345678 → TotalPrice = 2.895,92.</summary>
    private async Task<PurchaseContract> SeedFixedAsync(int? usageCode, bool withTypedTax = false)
    {
        var pc = NewContract(totalVolume: 1234.567m);
        pc.PriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(), FixationVolume = 1234.567m, FixationPrice = 2.345678m,
            Status = PriceFixationStatus.Confirmed,
        });
        if (withTypedTax)
        {
            _db.Context.Taxes.Add(new Tax { Code = "ICMS", Name = "ICMS", Rate = 12m });
            pc.Taxes.Add(new PurchaseContractTax { TaxCode = "ICMS" });
        }
        _db.Context.PurchaseContracts.Add(pc);
        if (usageCode is { } code)
            _db.Context.PurchaseContractFiscalComplements.Add(new PurchaseContractFiscalComplement { PurchaseContractKey = pc.Key, UsageCode = code });
        await _db.Context.SaveChangesAsync();
        return pc;
    }

    private async Task<int> SeedUsageAsync(decimal? inss, decimal? rat, decimal? senar, bool inactive = false)
    {
        var model = new UsageModel
        {
            Name = "Compra produtor", Direction = UsageDirection.Incoming, Inactive = inactive,
            CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
            FunruralInssRate = inss, FunruralRatRate = rat, FunruralSenarRate = senar,
        };
        return (await Usages().CreateAsync(model)).Code;
    }

    [Fact]
    public async Task Pf_usage_matches_the_purchase_invoice_calculator()
    {
        var usage = await SeedUsageAsync(1.32m, 0.11m, 0.20m);
        var pc = await SeedFixedAsync(usage);

        var totals = await Service().GetTotals(pc.Key);

        var expected = FunruralCalculator.Calculate(totals.TotalPrice, 1.32m, 0.11m, 0.20m);
        Assert.Equal(2895.92m, totals.FunruralBase);
        Assert.Equal(expected.InssValue, totals.FunruralInssValue);
        Assert.Equal(expected.RatValue, totals.FunruralRatValue);
        Assert.Equal(expected.SenarValue, totals.FunruralSenarValue);
        Assert.Equal(expected.Total, totals.TotalFunrural);
        Assert.Equal(totals.FunruralBase - expected.Total, totals.NetToProducer);
        Assert.Equal(1.32m, totals.FunruralInssRate);
    }

    [Fact]
    public async Task Standalone_total_tax_is_the_funrural_and_ignores_typed_taxes()
    {
        var usage = await SeedUsageAsync(1.32m, 0.11m, 0.20m);
        var pc = await SeedFixedAsync(usage, withTypedTax: true);

        var totals = await Service().GetTotals(pc.Key);

        Assert.Equal(totals.TotalFunrural, totals.TotalTax);
    }

    [Fact]
    public async Task Usage_without_rates_gives_zero_funrural()
    {
        var usage = await SeedUsageAsync(null, null, null);
        var pc = await SeedFixedAsync(usage);

        var totals = await Service().GetTotals(pc.Key);

        Assert.Equal(0m, totals.TotalFunrural);
        Assert.Equal(totals.TotalPrice, totals.FunruralBase);
        Assert.Equal(totals.TotalPrice, totals.NetToProducer);
    }

    [Fact]
    public async Task No_usage_gives_zero_funrural_and_net_equals_base()
    {
        var pc = await SeedFixedAsync(usageCode: null);

        var totals = await Service().GetTotals(pc.Key);

        Assert.Equal(0m, totals.TotalFunrural);
        Assert.Equal(0m, totals.TotalTax);
        Assert.Equal(totals.TotalPrice, totals.NetToProducer);
    }

    [Fact]
    public async Task Inactive_usage_still_feeds_the_totals()
    {
        var usage = await SeedUsageAsync(1.32m, 0.11m, 0.20m, inactive: true);
        var pc = await SeedFixedAsync(usage);

        var totals = await Service().GetTotals(pc.Key);

        Assert.True(totals.TotalFunrural > 0m);
    }

    [Fact]
    public async Task Paf_without_confirmed_fixation_has_zero_base()
    {
        var usage = await SeedUsageAsync(1.32m, 0.11m, 0.20m);
        var pc = NewContract(totalVolume: 1000m);
        _db.Context.PurchaseContracts.Add(pc);
        _db.Context.PurchaseContractFiscalComplements.Add(new PurchaseContractFiscalComplement { PurchaseContractKey = pc.Key, UsageCode = usage });
        await _db.Context.SaveChangesAsync();

        var totals = await Service().GetTotals(pc.Key);

        Assert.Equal(0m, totals.FunruralBase);
        Assert.Equal(0m, totals.TotalFunrural);
        Assert.Equal(0m, totals.NetToProducer);
    }

    [Fact]
    public async Task Sapb1_keeps_the_typed_taxes_and_zero_funrural_fields()
    {
        var pc = await SeedFixedAsync(usageCode: null, withTypedTax: true);

        var totals = await Service("SAPB1").GetTotals(pc.Key);

        Assert.Equal(decimal.Round(2895.92m / 100 * 12m, 2, MidpointRounding.ToEven), totals.TotalTax);
        Assert.Equal(0m, totals.TotalFunrural);
        Assert.Equal(0m, totals.FunruralBase);
        Assert.Equal(0m, totals.NetToProducer);
    }
```

Usings a acrescentar no arquivo: `Microsoft.Extensions.Logging.Abstractions`, `SiagroB1.Application.Services`, `SiagroB1.Domain.Enums`, `SiagroB1.Domain.Models`, `SiagroB1.Fiscal.Taxes`.

> Conferir pelo `PurchaseContractTotalPriceTests` que `2895.92m` é de fato `Round(1234.567 × 2.345678, 2, ToEven)` — se não for, trocar a constante pelo valor que o teste existente de `TotalPrice` dá, sem mudar a regra. Se `Taxes` não for o nome do DbSet de `TAXES`, ajustar.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsTotalsServiceTests"`
Expected: erro de compilação (construtor e campos novos).

- [ ] **Step 3: DTO**

Acrescentar a `PurchaseContractTotalsResponseDto`:

```csharp

    // Totalizador de Funrural (spec 2026-10-13, só STANDALONE; zerado em SAPB1). Base = TotalPrice.
    [JsonPropertyName("FunruralBase")]
    public decimal FunruralBase { get; set; }

    [JsonPropertyName("FunruralInssRate")]
    public decimal FunruralInssRate { get; set; }

    [JsonPropertyName("FunruralRatRate")]
    public decimal FunruralRatRate { get; set; }

    [JsonPropertyName("FunruralSenarRate")]
    public decimal FunruralSenarRate { get; set; }

    [JsonPropertyName("FunruralInssValue")]
    public decimal FunruralInssValue { get; set; }

    [JsonPropertyName("FunruralRatValue")]
    public decimal FunruralRatValue { get; set; }

    [JsonPropertyName("FunruralSenarValue")]
    public decimal FunruralSenarValue { get; set; }

    [JsonPropertyName("TotalFunrural")]
    public decimal TotalFunrural { get; set; }

    /// <summary>Base − Funrural: o que o produtor recebe (Funrural sempre retido sobre o preço cheio).</summary>
    [JsonPropertyName("NetToProducer")]
    public decimal NetToProducer { get; set; }
```

- [ ] **Step 4: Serviço**

`PurchaseContractsTotalsService.cs` completo:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsTotalsService(AppDbContext context, IUsage usageService, IConfiguration configuration)
{
    public async Task<PurchaseContractTotalsResponseDto> GetTotals(Guid key)
    {
        var ctr = await context.PurchaseContracts
                      .Include(x => x.Taxes)
                      .ThenInclude(x => x.Tax)
                      .Include(x => x.PriceFixations)
                      .Include(x => x.ShipmentReleases)
                      .Include(x => x.Allocations)
                      .ThenInclude(a => a.StorageTransaction)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key) ??
                  throw new KeyNotFoundException();

        var dto = new PurchaseContractTotalsResponseDto
        {
            FixedVolume = ctr.FixedVolume,
            AvailableVolumeToPricing = ctr.AvailableVolumeToPricing,
            TotalPrice = ctr.TotalPrice,
            TotalTax = ctr.TotalTax,
            TotalShipmentReleases = ctr.TotalShipmentReleases,
            TotalAvailableToRelease = ctr.TotalAvailableToRelease,
            TotalStandard = ctr.TotalStandard,
            TotalVolume = ctr.TotalVolume,
            AvaiableVolume = ctr.AvaiableVolume,
            WashedOutVolume = ctr.WashedOutVolume,
        };

        if (ErpMode.IsStandalone(configuration))
            await ApplyFunruralAsync(dto, key);

        return dto;
    }

    /// <summary>
    /// Spec 2026-10-13 regra 9: em STANDALONE a grade digitada (PURCHASE_CONTRACTS_TAXES) deixa de contar e
    /// "Total Impostos" passa a ser o Funrural da natureza do complemento fiscal, pelo MESMO cálculo da NF de
    /// Entrada (FunruralCalculator), sobre o total das fixações confirmadas. Alíquota lida ao vivo — natureza
    /// desativada depois de escolhida continua valendo. Sem natureza: tudo zero e líquido = base.
    /// </summary>
    private async Task ApplyFunruralAsync(PurchaseContractTotalsResponseDto dto, Guid key)
    {
        var usageCode = await context.PurchaseContractFiscalComplements.AsNoTracking()
            .Where(x => x.PurchaseContractKey == key)
            .Select(x => x.UsageCode)
            .FirstOrDefaultAsync();

        var usage = usageCode is { } code ? await usageService.GetByIdAsync(code) : null;
        var funrural = FunruralCalculator.Calculate(
            dto.TotalPrice, usage?.FunruralInssRate, usage?.FunruralRatRate, usage?.FunruralSenarRate);

        dto.FunruralBase = dto.TotalPrice;
        dto.FunruralInssRate = funrural.InssRate;
        dto.FunruralRatRate = funrural.RatRate;
        dto.FunruralSenarRate = funrural.SenarRate;
        dto.FunruralInssValue = funrural.InssValue;
        dto.FunruralRatValue = funrural.RatValue;
        dto.FunruralSenarValue = funrural.SenarValue;
        dto.TotalFunrural = funrural.Total;
        dto.NetToProducer = dto.TotalPrice - funrural.Total;
        dto.TotalTax = funrural.Total;
    }
}
```

> `SiagroB1.Application` já referencia `SiagroB1.Fiscal` (grafo do CLAUDE.md: Fiscal → Application). Se o build acusar referência ausente, parar e reportar — não adicionar referência sem checar o csproj.

- [ ] **Step 5: Ajustar call sites**

- `PurchaseContractsTotalsServiceTests.cs:33` (teste existente): `await Service("SAPB1").GetTotals(pc.Key)` — o teste é de volume, não de modo.
- `Washouts/WashoutBalanceConsumersTests.cs:184`: `new PurchaseContractsTotalsService(_db.Context, new UsageService(_db, NullLogger<UsageService>.Instance), TestConfiguration.Erp("SAPB1"))` (+ usings `Microsoft.Extensions.Logging.Abstractions`, `SiagroB1.Application.Services`).
- `grep -rn "new PurchaseContractsTotalsService" SiagroB1.Application.Tests SiagroB1.Web SiagroB1.Application` — nenhum call site com 1 argumento pode sobrar.

- [ ] **Step 6: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractsTotalsServiceTests|FullyQualifiedName~WashoutBalanceConsumersTests"`
Expected: todos PASS.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Domain/Dtos/PurchaseContractTotalsResponseDto.cs SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsTotalsService.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsTotalsServiceTests.cs SiagroB1.Application.Tests/PurchaseContracts/Washouts/WashoutBalanceConsumersTests.cs
git commit -m "feat(purchase-contract): totalizador de Funrural pela natureza do contrato" -m "Em STANDALONE o Total Impostos do contrato passa a ser o Funrural da natureza (INSS, RAT, SENAR) sobre o total fixado, pelo mesmo cálculo da NF de Entrada, com o líquido ao produtor. A grade digitada continua valendo em SAPB1." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT"
```

---

### Task 6: Excluir remove o complemento; copiar não o leva

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsDeleteService.cs` (~44-48)
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementLifecycleTests.cs`

**Interfaces:**
- Consumes: Task 1. `PurchaseContractsDeleteService.ExecuteAsync(Guid key, string userName)`; `PurchaseContractsCopyService` (ver o método público e o construtor em `PurchaseContractsCopyService.cs` antes de escrever o teste).

- [ ] **Step 1: Testes que falham**

Ler `PurchaseContractsDeleteService.cs` (construtor e o que `DeleteAsyncWithTransaction` exige — o contrato precisa estar em status deletável) e `PurchaseContractsCopyService.cs` (assinatura e retorno). Instanciar os dois com os mesmos dublês que outro teste do repo já usa (`grep -rn "new PurchaseContractsDeleteService\|new PurchaseContractsCopyService" SiagroB1.Application.Tests`; se não houver, montar a partir do construtor, reaproveitando `FinancialDocumentTestServices`).

```csharp
namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractFiscalComplementLifecycleTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<PurchaseContract> SeedDraftWithComplementAsync()
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
            Status = ContractStatus.Draft,
        };
        _db.Context.PurchaseContracts.Add(pc);
        _db.Context.PurchaseContractFiscalComplements.Add(new PurchaseContractFiscalComplement { PurchaseContractKey = pc.Key, UsageCode = 7 });
        await _db.Context.SaveChangesAsync();
        return pc;
    }

    [Fact]
    public async Task Deleting_the_contract_removes_the_complement()
    {
        var pc = await SeedDraftWithComplementAsync();

        await DeleteService().ExecuteAsync(pc.Key, "comercial");

        Assert.False(await _db.Context.PurchaseContractFiscalComplements.AnyAsync(x => x.PurchaseContractKey == pc.Key));
    }

    [Fact]
    public async Task Copying_the_contract_does_not_copy_the_complement()
    {
        var pc = await SeedDraftWithComplementAsync();

        var copyKey = await CopyAndReturnNewKey(pc.Key); // conforme a assinatura real do CopyService

        Assert.NotEqual(pc.Key, copyKey);
        Assert.False(await _db.Context.PurchaseContractFiscalComplements.AnyAsync(x => x.PurchaseContractKey == copyKey));
    }

    // DeleteService() e CopyAndReturnNewKey(...): montados pelos construtores reais (ver nota acima).
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractFiscalComplementLifecycleTests"`
Expected: `Deleting_the_contract_removes_the_complement` FAIL (o InMemory não aplica cascade de entidade não carregada — por isso a remoção é explícita). `Copying...` deve PASSAR já (o Copy não conhece a tabela) — é teste de regressão do D8.

- [ ] **Step 3: Implementação**

`PurchaseContractsDeleteService.cs`, depois de `if (entity.Brokers.Any()) ... RemoveRange(entity.Brokers);`:

```csharp

            // Complemento fiscal (1:1, cascade no banco): removido à mão pelo mesmo motivo dos filhos acima.
            var fiscalComplement = await context.PurchaseContractFiscalComplements
                .FirstOrDefaultAsync(x => x.PurchaseContractKey == entity.Key);
            if (fiscalComplement is not null)
                context.PurchaseContractFiscalComplements.Remove(fiscalComplement);
```

(using `Microsoft.EntityFrameworkCore` se faltar.)

- [ ] **Step 4: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractFiscalComplementLifecycleTests"`
Expected: PASS.

- [ ] **Step 5: Suíte inteira + gates**

```bash
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Expected: build OK; testes todos verdes (anotar a contagem); sem mudança pendente.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsDeleteService.cs SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementLifecycleTests.cs
git commit -m "feat(purchase-contract): excluir contrato remove o complemento fiscal" -m "Copiar contrato não leva a natureza de propósito: o fiscal confere de novo (spec D8)." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT"
```

---

### Task 7: Frontend — seção "Complemento Fiscal" no Detail

Repositório: `C:\Projetos\SiagroB1\siagro-b1-frontend`. Antes: `git checkout -b feature/purchase-contract-fiscal-complement` a partir de `main`.

**Files:**
- Modify: `webapp/model/ServerRoutes.ts` (~45)
- Create: `webapp/view/purchaseContracts/fragments/PurchaseContractFiscalComplement.fragment.xml`
- Create: `webapp/view/purchaseContracts/fragments/PurchaseContractFiscalComplementDialog.fragment.xml`
- Modify: `webapp/controller/purchaseContracts/Detail.controller.ts`
- Modify: `webapp/view/purchaseContracts/Detail.view.xml` (~238, depois de "Dados do Contrato")
- Modify: `webapp/model/formatter.ts` (`formatContractChangeLogField`, ~1066)

**Interfaces:**
- Consumes: endpoints da Task 3 (DTO PascalCase).
- Produces: `api.purchaseContractsGetFiscalComplement`, `api.purchaseContractsSetFiscalComplement`; JSONModels `fiscalComplement` / `fiscalComplementDialog` no Detail; método `loadFiscalComplement(key)`; após salvar, o Detail chama `refreshContractTotals()` (Task 8 o torna sensível ao Funrural).

- [ ] **Step 1: Rotas**

`ServerRoutes.ts`, depois de `salesContractsSetFiscalComplement`:

```ts
  purchaseContractsGetFiscalComplement: '/PurchaseContractsGetFiscalComplement(...)',
  purchaseContractsSetFiscalComplement: '/PurchaseContractsSetFiscalComplement(...)',
```

- [ ] **Step 2: Fragmento somente leitura**

`PurchaseContractFiscalComplement.fragment.xml` (atenção: **nenhum `--` dentro de comentário XML**):

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:f="sap.ui.layout.form"
    xmlns:core="sap.ui.core"
>
  <!--
    Complemento fiscal do contrato de compra: a natureza de operação, que dá as alíquotas de Funrural.
    Dados da function PurchaseContractsGetFiscalComplement no JSONModel "fiscalComplement"; gravação pela
    action dedicada, só com a permissão PURCHASE_CONTRACT_FISCAL_EDIT.
  -->
  <VBox width="100%">
    <MessageStrip
      type="Warning"
      showIcon="true"
      class="sapUiSmallMarginBottom"
      visible="{= ${fiscalComplement>/IsComplete} !== true }"
      text="Natureza de operação não informada: o contrato não pode ser aprovado sem ela." />
    <Toolbar>
      <ToolbarSpacer />
      <Button
        text="Editar"
        icon="sap-icon://edit"
        visible="{fiscalComplement>/canEdit}"
        press=".onOpenFiscalComplementDialog" />
    </Toolbar>
    <f:Form editable="false">
      <f:layout>
        <f:ColumnLayout columnsM="2" columnsL="2" columnsXL="3" />
      </f:layout>
      <f:formContainers>
        <f:FormContainer>
          <f:formElements>
            <f:FormElement label="Natureza da operação (código)">
              <f:fields>
                <Text text="{fiscalComplement>/UsageCode}" />
              </f:fields>
            </f:FormElement>
            <f:FormElement label="Natureza da operação">
              <f:fields>
                <Text text="{fiscalComplement>/UsageName}" />
              </f:fields>
            </f:FormElement>
          </f:formElements>
        </f:FormContainer>
        <f:FormContainer>
          <f:formElements>
            <f:FormElement label="Funrural INSS (%)">
              <f:fields>
                <Text text="{fiscalComplement>/InssRateText}" />
              </f:fields>
            </f:FormElement>
            <f:FormElement label="Funrural RAT (%)">
              <f:fields>
                <Text text="{fiscalComplement>/RatRateText}" />
              </f:fields>
            </f:FormElement>
            <f:FormElement label="Funrural SENAR (%)">
              <f:fields>
                <Text text="{fiscalComplement>/SenarRateText}" />
              </f:fields>
            </f:FormElement>
          </f:formElements>
        </f:FormContainer>
        <f:FormContainer>
          <f:formElements>
            <f:FormElement label="Alterado por">
              <f:fields>
                <Text text="{fiscalComplement>/UpdatedBy}" />
              </f:fields>
            </f:FormElement>
            <f:FormElement label="Alterado em">
              <f:fields>
                <Text text="{fiscalComplement>/UpdatedAtText}" />
              </f:fields>
            </f:FormElement>
          </f:formElements>
        </f:FormContainer>
      </f:formContainers>
    </f:Form>
  </VBox>
</core:FragmentDefinition>
```

`git add` o arquivo.

- [ ] **Step 3: Diálogo**

`PurchaseContractFiscalComplementDialog.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:f="sap.ui.layout.form"
    xmlns:core="sap.ui.core"
>
  <!--
    Edição da natureza de operação do contrato de compra. Escreve no JSONModel "fiscalComplementDialog";
    o valor só chega ao servidor ao salvar (action PurchaseContractsSetFiscalComplement).
  -->
  <Dialog id="purchaseFiscalComplementDialog" title="Complemento Fiscal" contentWidth="560px">
    <content>
      <f:Form editable="true">
        <f:layout>
          <f:ColumnLayout columnsM="1" columnsL="1" columnsXL="1" />
        </f:layout>
        <f:formContainers>
          <f:FormContainer>
            <f:formElements>
              <f:FormElement label="Natureza da operação">
                <f:fields>
                  <Input
                    value="{fiscalComplementDialog>/UsageCode}"
                    description="{fiscalComplementDialog>/UsageName}"
                    showValueHelp="true"
                    valueHelpOnly="true"
                    valueHelpRequest=".openFiscalComplementUsageValueHelp" />
                  <Button text="Limpar" press=".onClearFiscalComplementUsage" />
                </f:fields>
              </f:FormElement>
            </f:formElements>
          </f:FormContainer>
        </f:formContainers>
      </f:Form>
    </content>
    <footer>
      <OverflowToolbar>
        <ToolbarSpacer />
        <Button text="Salvar" type="Emphasized" press=".onSaveFiscalComplement" />
        <Button text="Cancelar" press=".onCloseFiscalComplementDialog" />
      </OverflowToolbar>
    </footer>
  </Dialog>
</core:FragmentDefinition>
```

`git add` o arquivo.

- [ ] **Step 4: Controller do Detail**

Em `controller/purchaseContracts/Detail.controller.ts`:

Imports a acrescentar:

```ts
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import Dialog from "sap/m/Dialog";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import DialogHelper from "siagrob1/dialogs/DialogHelper";
import SessionService from "siagrob1/services/SessionService";
```

Tipo antes da classe:

```ts
/** Complemento fiscal do contrato de compra (DTO PascalCase da function; nulo quando ainda não existe). */
type PurchaseFiscalComplement = {
  UsageCode?: number,
  UsageName?: string,
  FunruralInssRate?: number,
  FunruralRatRate?: number,
  FunruralSenarRate?: number,
  UpdatedAt?: string,
  UpdatedBy?: string,
  IsComplete?: boolean,
}

/** Status em que o complemento ainda pode mudar (backend recusa Finished/Canceled). */
const FISCAL_LOCKED_STATUSES = ["Finished", "Canceled"];
```

Campos e `onInit`:

```ts
  private _fiscalContractKey: string;
  private _fiscalComplementDialog: Dialog;
  private _fiscalSaving = false;

	onInit(): void  {	
		this.getView().setModel(new JSONModel({}), "fiscalComplement");
		this.getView().setModel(new JSONModel({}), "fiscalComplementDialog");
		this.initContractDrafts();
		this.getRouter().getRoute("purchaseContractsDetail").attachPatternMatched((ev) => this.detailRouteMatched(ev));
	}
```

Em `detailRouteMatched`, depois de `void this.reloadContractDrafts(id);`:

```ts
      this._fiscalContractKey = id;
      (this.getModel("fiscalComplement") as JSONModel).setData({});
      void this.refreshStandaloneFlag()
        .then(() => this.loadFiscalComplement(id))
        .catch(() => { /* sem systemInfo a seção fica oculta; nada depende disso */ });
```

Métodos novos (depois de `detailRouteMatched`):

```ts
  /**
   * Seção "Complemento Fiscal": só existe em STANDALONE (`ui>/standalone`). A function devolve null quando o
   * contrato ainda não tem complemento - caso comum, vira objeto vazio e o aviso aparece.
   */
  private async loadFiscalComplement(contractKey: string): Promise<void> {
    const model = this.getModel("fiscalComplement") as JSONModel;

    if ((this.getModel("ui") as JSONModel).getProperty("/standalone") !== true) {
      return;
    }

    await SessionService.whenReady();

    try {
      const func = (this.getModel() as ODataModel).bindContext(this.api.purchaseContractsGetFiscalComplement);
      func.setParameter("Key", contractKey);
      await func.invoke();

      const data = (func.getBoundContext().getObject() as PurchaseFiscalComplement) ?? {};
      // Resposta atrasada de outro contrato (troca rápida de rota) não pode sobrescrever a tela.
      if (contractKey !== this._fiscalContractKey) {
        return;
      }

      const rate = (value?: number) => value === null || value === undefined
        ? "" : value.toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 4 });
      const status = (this.getView().getBindingContext() as Context)?.getProperty("Status") as string;

      model.setData({
        ...data,
        InssRateText: rate(data.FunruralInssRate),
        RatRateText: rate(data.FunruralRatRate),
        SenarRateText: rate(data.FunruralSenarRate),
        UpdatedAtText: data.UpdatedAt ? new Date(data.UpdatedAt).toLocaleString("pt-BR") : "",
        canEdit: SessionService.hasPermission("PURCHASE_CONTRACT_FISCAL_EDIT")
          && !FISCAL_LOCKED_STATUSES.includes(status)
          && (this.getModel("ui") as JSONModel).getProperty("/readonly") !== true,
      });
    } catch {
      // O handler global do OData já mostra o erro do servidor.
      if (contractKey !== this._fiscalContractKey) {
        return;
      }
      model.setData({ canEdit: false });
    }
  }

  async onOpenFiscalComplementDialog() {
    const current = (this.getModel("fiscalComplement") as JSONModel).getData() as PurchaseFiscalComplement;

    (this.getModel("fiscalComplementDialog") as JSONModel).setData({
      UsageCode: current.UsageCode ?? null,
      UsageName: current.UsageName ?? "",
    });

    this._fiscalComplementDialog ??= await DialogHelper.createDialog(
      this, "siagrob1.view.purchaseContracts.fragments.PurchaseContractFiscalComplementDialog");

    this._fiscalComplementDialog.open();
  }

  onCloseFiscalComplementDialog() {
    this._fiscalComplementDialog?.close();
  }

  /** Só naturezas de Entrada ativas ($filter estático do enum: `Filter` sobre enum estoura). */
  async openFiscalComplementUsageValueHelp() {
    const selected = await DialogHelper.openTableSelectDialog(
      this, "UsagesSelectDialog", ["Name", "Description"],
      [new Filter("Inactive", FilterOperator.EQ, false)], undefined, "Direction eq 'Incoming'");

    if (!selected) {
      return;
    }

    const model = this.getModel("fiscalComplementDialog") as JSONModel;
    model.setProperty("/UsageCode", selected.getProperty("Code"));
    model.setProperty("/UsageName", selected.getProperty("Name"));
  }

  onClearFiscalComplementUsage() {
    const model = this.getModel("fiscalComplementDialog") as JSONModel;
    model.setProperty("/UsageCode", null);
    model.setProperty("/UsageName", "");
  }

  async onSaveFiscalComplement() {
    // Trava antes do primeiro await: duplo clique não pode disparar duas actions.
    if (this._fiscalSaving) {
      return;
    }
    this._fiscalSaving = true;

    try {
      const form = (this.getModel("fiscalComplementDialog") as JSONModel).getData() as PurchaseFiscalComplement;
      // Int do EDM: número ou null, nunca string (o binder do OData recusa o corpo inteiro).
      const usageCode = form.UsageCode === null || form.UsageCode === undefined || (form.UsageCode as unknown) === ""
        ? null : Number(form.UsageCode);

      const action = (this.getModel() as ODataModel).bindContext(this.api.purchaseContractsSetFiscalComplement);
      action.setParameter("Key", this._fiscalContractKey);
      action.setParameter("UsageCode", usageCode);

      this._fiscalComplementDialog.setBusy(true);
      try {
        await action.invoke();
      } catch {
        // Recusa do servidor (400 em pt-BR) já foi exibida pelo handler global; diálogo segue aberto.
        return;
      } finally {
        this._fiscalComplementDialog.setBusy(false);
      }

      this._fiscalComplementDialog.close();
      MessageToast.show("Complemento fiscal salvo.");
      await this.loadFiscalComplement(this._fiscalContractKey);
      this.refreshContractTotals();
      // O log de alterações ganhou uma linha: recarregar a seção se ela tiver binding próprio.
      this.getView().getElementBinding()?.refresh();
    } finally {
      this._fiscalSaving = false;
    }
  }
```

> `refreshContractTotals` é `private` em `PurchaseContractsBaseController` (~1122): trocar para `protected`. `Context` já é importado no Detail. Se `getElementBinding()?.refresh()` causar refetch pesado ou reset de seções, trocar pela forma que o Detail já usa para recarregar o Log de Alterações (procurar `ChangeLogs` no controller/view).

- [ ] **Step 5: Seção no Detail**

`Detail.view.xml`, entre a seção "Dados do Contrato" e a seção "Impostos":

```xml
      <uxap:ObjectPageSection titleUppercase="false" title="Complemento Fiscal" visible="{= ${ui>/standalone} === true }">
				<uxap:subSections>
					<uxap:ObjectPageSubSection titleUppercase="false">
						<core:Fragment fragmentName="siagrob1.view.purchaseContracts.fragments.PurchaseContractFiscalComplement" type="XML" />
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
```

- [ ] **Step 6: Rótulo no log**

`formatter.ts`, em `formatContractChangeLogField`, depois de `m.set("Washout", "Washout");`:

```ts
    m.set("Usage", "Natureza de operação");
```

Se existir teste do formatter em `test/unit/model/formatter.qunit.ts` cobrindo `formatContractChangeLogField`, acrescentar:

```ts
QUnit.test("formatContractChangeLogField traduz a natureza de operação", function (assert) {
	assert.strictEqual(formatter.formatContractChangeLogField("Usage"), "Natureza de operação");
});
```

- [ ] **Step 7: Gates**

Na raiz do frontend, dev server parado: `yarn ts-typecheck && yarn lint`
Expected: sem erro.

- [ ] **Step 8: Commit**

```bash
git add webapp/model/ServerRoutes.ts webapp/view/purchaseContracts/fragments/PurchaseContractFiscalComplement.fragment.xml webapp/view/purchaseContracts/fragments/PurchaseContractFiscalComplementDialog.fragment.xml webapp/controller/purchaseContracts/Detail.controller.ts webapp/controller/purchaseContracts/PurchaseContractsBaseController.ts webapp/view/purchaseContracts/Detail.view.xml webapp/model/formatter.ts webapp/test/unit/model/formatter.qunit.ts
git commit -m "feat(purchase-contract): seção Complemento Fiscal no contrato de compra" -m "Em STANDALONE o fiscal escolhe a natureza de operação (só de entrada, ativa) por diálogo próprio; quem não tem PURCHASE_CONTRACT_FISCAL_EDIT só lê. Aviso quando falta, porque a aprovação a exige." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT"
```

---

### Task 8: Frontend — quadro de Funrural e fim da grade digitada em STANDALONE

**Files:**
- Create: `webapp/helpers/PurchaseContractFunruralHelpers.ts`
- Create: `webapp/test/unit/helpers/PurchaseContractFunruralHelpers.qunit.ts`; Modify `webapp/test/unit/unitTests.qunit.ts`
- Create: `webapp/view/purchaseContracts/fragments/PurchaseContractFunruralTotals.fragment.xml`
- Modify: `webapp/types/PurchaseContractsTotal.ts`
- Modify: `webapp/controller/purchaseContracts/PurchaseContractsBaseController.ts` (`refreshContractTotals`, ~1122-1160), `Detail.controller.ts`, `approval/Detail.controller.ts`, `Add.controller.ts`, `Edit.controller.ts`
- Modify: `webapp/view/purchaseContracts/Detail.view.xml`, `approval/Detail.view.xml`, `Add.view.xml`, `Edit.view.xml`

**Interfaces:**
- Consumes: campos novos de `purchaseContractsTotals` (Task 5).
- Produces: `summarizeContractFunrural(totals: PurchaseContractsTotals): FunruralSummary`; `applyFunruralTotals(viewModel, data)` no BaseController; `viewModel>/funrural` = `{ rows, total, net }`.

- [ ] **Step 1: Teste QUnit que falha**

`test/unit/helpers/PurchaseContractFunruralHelpers.qunit.ts`:

```ts
import { summarizeContractFunrural } from "siagrob1/helpers/PurchaseContractFunruralHelpers";

QUnit.module("PurchaseContractFunruralHelpers - quadro de Funrural do contrato");

QUnit.test("três parcelas viram três linhas, com total e líquido", function (assert) {
	const result = summarizeContractFunrural({
		AvaiableVolume: 0, FunruralBase: 10000, FunruralInssRate: 1.32, FunruralRatRate: 0.11, FunruralSenarRate: 0.2,
		FunruralInssValue: 132, FunruralRatValue: 11, FunruralSenarValue: 20, TotalFunrural: 163, NetToProducer: 9837,
	});

	assert.deepEqual(result.rows.map((r) => r.Tax), ["Funrural - INSS", "Funrural - RAT", "Funrural - SENAR"]);
	assert.deepEqual(result.rows[0], { Tax: "Funrural - INSS", Base: 10000, Rate: 1.32, Value: 132 });
	assert.strictEqual(result.total, 163);
	assert.strictEqual(result.net, 9837);
});

QUnit.test("parcela de valor zero é omitida, como no quadro da Entrada", function (assert) {
	const result = summarizeContractFunrural({
		AvaiableVolume: 0, FunruralBase: 10000, FunruralInssRate: 0, FunruralRatRate: 0, FunruralSenarRate: 0.2,
		FunruralInssValue: 0, FunruralRatValue: 0, FunruralSenarValue: 20, TotalFunrural: 20, NetToProducer: 9980,
	});

	assert.deepEqual(result.rows.map((r) => r.Tax), ["Funrural - SENAR"]);
});

QUnit.test("sem natureza ou sem fixação: nenhuma linha, líquido igual à base", function (assert) {
	const result = summarizeContractFunrural({ AvaiableVolume: 0, FunruralBase: 500, NetToProducer: 500 });

	assert.deepEqual(result.rows, []);
	assert.strictEqual(result.total, 0);
	assert.strictEqual(result.net, 500);
});
```

Registrar em `test/unit/unitTests.qunit.ts`: `import "./helpers/PurchaseContractFunruralHelpers.qunit";`

- [ ] **Step 2: Tipo**

`types/PurchaseContractsTotal.ts` — acrescentar ao tipo:

```ts
  FunruralBase?: number,
  FunruralInssRate?: number,
  FunruralRatRate?: number,
  FunruralSenarRate?: number,
  FunruralInssValue?: number,
  FunruralRatValue?: number,
  FunruralSenarValue?: number,
  TotalFunrural?: number,
  NetToProducer?: number,
```

- [ ] **Step 3: Helper**

`helpers/PurchaseContractFunruralHelpers.ts`:

```ts
import { PurchaseContractsTotals } from "siagrob1/types/PurchaseContractsTotal";

export type FunruralRow = { Tax: string, Base: number, Rate: number, Value: number };
export type FunruralSummary = { rows: FunruralRow[], total: number, net: number };

/**
 * Quadro "Impostos" do contrato de compra em STANDALONE (spec 2026-10-13): as parcelas de Funrural que o backend
 * calculou pela natureza do complemento fiscal. Parcela de valor zero some, como no quadro "Tributos" da Entrada.
 */
export function summarizeContractFunrural(totals: PurchaseContractsTotals): FunruralSummary {
	const base = totals.FunruralBase ?? 0;
	const parts: FunruralRow[] = [
		{ Tax: "Funrural - INSS", Base: base, Rate: totals.FunruralInssRate ?? 0, Value: totals.FunruralInssValue ?? 0 },
		{ Tax: "Funrural - RAT", Base: base, Rate: totals.FunruralRatRate ?? 0, Value: totals.FunruralRatValue ?? 0 },
		{ Tax: "Funrural - SENAR", Base: base, Rate: totals.FunruralSenarRate ?? 0, Value: totals.FunruralSenarValue ?? 0 },
	];

	return {
		rows: parts.filter((p) => p.Value !== 0),
		total: totals.TotalFunrural ?? 0,
		net: totals.NetToProducer ?? base,
	};
}

/** Valor monetário pt-BR com 2 casas, para o quadro. */
export function formatFunruralAmount(value: number): string {
	return (value ?? 0).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/** Alíquota pt-BR com até 4 casas. */
export function formatFunruralRate(value: number): string {
	return (value ?? 0).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 4 });
}
```

`git add` helper e teste.

- [ ] **Step 4: Rodar o QUnit**

Rodar o QUnit do repo pelo comando do `package.json` que roda os unit tests no navegador headless (ver `scripts`; o `yarn test` inteiro falha no gate de cobertura — olhar só o resultado deste módulo).
Expected: os 3 testes PASS.

- [ ] **Step 5: Fragmento do quadro**

`PurchaseContractFunruralTotals.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:core="sap.ui.core"
    core:require="{
      funrural: 'siagrob1/helpers/PurchaseContractFunruralHelpers'
    }"
>
  <!-- Quadro "Impostos" do contrato de compra em STANDALONE: Funrural da natureza do complemento fiscal,
       calculado no backend (purchaseContractsTotals) e gravado em viewModel>/funrural. Somente leitura. -->
  <VBox width="100%">
    <Table
      id="tablePurchaseContractFunrural"
      class="sapUiSizeCondensed"
      width="48rem"
      items="{viewModel>/funrural/rows}"
      noDataText="Sem Funrural: informe a natureza de operação no Complemento Fiscal ou confirme uma fixação."
    >
      <columns>
        <Column width="12rem">
          <Text text="Tributo" />
        </Column>
        <Column hAlign="End">
          <Text text="Base de cálculo" />
        </Column>
        <Column hAlign="End">
          <Text text="Alíquota (%)" />
        </Column>
        <Column hAlign="End">
          <Text text="Valor" />
        </Column>
      </columns>
      <items>
        <ColumnListItem>
          <cells>
            <Text text="{viewModel>Tax}" />
            <Text text="{path: 'viewModel>Base', formatter: 'funrural.formatFunruralAmount'}" />
            <Text text="{path: 'viewModel>Rate', formatter: 'funrural.formatFunruralRate'}" />
            <Text text="{path: 'viewModel>Value', formatter: 'funrural.formatFunruralAmount'}" />
          </cells>
        </ColumnListItem>
      </items>
    </Table>
    <HBox class="sapUiSmallMarginTop">
      <Label text="Total Funrural:" class="sapUiMediumMarginEnd" />
      <Text text="{path: 'viewModel>/funrural/total', formatter: 'funrural.formatFunruralAmount'}" />
    </HBox>
    <HBox class="sapUiTinyMarginTop">
      <Label text="Líquido ao produtor:" design="Bold" class="sapUiMediumMarginEnd" />
      <Text text="{path: 'viewModel>/funrural/net', formatter: 'funrural.formatFunruralAmount'}" />
    </HBox>
  </VBox>
</core:FragmentDefinition>
```

`git add` o arquivo.

- [ ] **Step 6: Uma porta só para gravar os totais no viewModel**

Em `PurchaseContractsBaseController.ts`:
- import `{ summarizeContractFunrural } from "siagrob1/helpers/PurchaseContractFunruralHelpers"` e o tipo `PurchaseContractsTotals`;
- método novo:

```ts
  /**
   * Grava no viewModel os totais do contrato. Porta única: Detail, Aprovação e o refresh pós-ação liam cada um
   * a sua lista de campos e a da Aprovação já tinha esquecido o WashedOutVolume.
   */
  protected applyContractTotals(viewModel: JSONModel, data: PurchaseContractsTotals) {
    viewModel.setProperty("/TotalPrice", data.TotalPrice ?? 0);
    viewModel.setProperty("/TotalTax", data.TotalTax ?? 0);
    viewModel.setProperty("/AvailableVolumeToPricing", data.AvailableVolumeToPricing ?? 0);
    viewModel.setProperty("/FixedVolume", data.FixedVolume ?? 0);
    viewModel.setProperty("/TotalVolume", data.TotalVolume ?? 0);
    viewModel.setProperty("/TotalStandard", data.TotalStandard ?? 0);
    viewModel.setProperty("/TotalShipmentReleases", data.TotalShipmentReleases ?? 0);
    viewModel.setProperty("/TotalAvailableToRelease", data.TotalAvailableToRelease ?? 0);
    viewModel.setProperty("/AvaiableVolume", data.AvaiableVolume ?? 0);
    viewModel.setProperty("/WashedOutVolume", data.WashedOutVolume ?? 0);
    viewModel.setProperty("/NetToProducer", data.NetToProducer ?? 0);
    viewModel.setProperty("/funrural", summarizeContractFunrural(data));
  }
```

- `refreshContractTotals` (agora `protected`): trocar o `.then((data: {...}) => { ...setProperty... })` por `.then((data: PurchaseContractsTotals) => this.applyContractTotals(viewModel, data))`.
- `Detail.controller.ts` `detailRouteMatched`: trocar o bloco `.then((data) => { viewModel.setProperty(...) x10 })` por `.then((data: PurchaseContractsTotals) => this.applyContractTotals(viewModel, data))`.
- `approval/Detail.controller.ts`: idem com `this.viewModel` (`.then((data: PurchaseContractsTotals) => this.applyContractTotals(this.viewModel, data))`), e chamar `void this.refreshStandaloneFlag();` no início do `if (id != null)`.

- [ ] **Step 7: Views**

`Detail.view.xml` e `approval/Detail.view.xml` — a seção "Impostos" existente ganha `visible="{= ${ui>/standalone} !== true }"`, e logo depois dela entra:

```xml
      <uxap:ObjectPageSection titleUppercase="false" title="Impostos" visible="{= ${ui>/standalone} === true }">
				<uxap:subSections>
					<uxap:ObjectPageSubSection titleUppercase="false">
						<core:Fragment fragmentName="siagrob1.view.purchaseContracts.fragments.PurchaseContractFunruralTotals" type="XML" />
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
```

No cabeçalho dos dois, logo depois do `HBox` de "Total Impostos":

```xml
          <HBox class="sapUiTinyMarginBottom" visible="{= ${ui>/standalone} === true }">
            <Text text="Líquido ao produtor:" class="sapUiMediumMarginEnd" />
            <ObjectNumber number="{
              path: 'viewModel>/NetToProducer',
              type: 'sap.ui.model.odata.type.Double',
              formatOptions:{
                decimals: 2,
                decimalSeparator: ',',
                groupingEnabled: true,
                groupingSeparator: '.'
              }
            }" unit="{
              path: 'StandardCurrency',
              targetType: 'any',
              formatter: '.formatter.formatCurrency'
            }" />
          </HBox>
```

Na Aprovação, acrescentar também a seção "Complemento Fiscal" **somente leitura** (sem Editar): mesma seção da Task 7 Step 5 com `visible="{= ${ui>/standalone} === true }"`, e no `approval/Detail.controller.ts` registrar os JSONModels `fiscalComplement` no `onInit` e carregar como na Task 7, com `canEdit: false` fixo. Para não duplicar a função, mover `loadFiscalComplement` do Detail para `PurchaseContractsBaseController` com um parâmetro `allowEdit: boolean` (Detail passa `true`, Aprovação `false`), mantendo a guarda `_fiscalContractKey` no base.

`Add.view.xml` e `Edit.view.xml`: a seção "Impostos" ganha `visible="{= ${ui>/standalone} !== true }"`; em `Add.controller.ts` `newRouteMatched` e `Edit.controller.ts` `editRouteMatched`, chamar `void this.refreshStandaloneFlag();` no início.

- [ ] **Step 8: Gates**

`yarn ts-typecheck && yarn lint` (dev server parado). Expected: sem erro.

- [ ] **Step 9: Commit**

```bash
git add webapp/helpers/PurchaseContractFunruralHelpers.ts webapp/test/unit/helpers/PurchaseContractFunruralHelpers.qunit.ts webapp/test/unit/unitTests.qunit.ts webapp/view/purchaseContracts/fragments/PurchaseContractFunruralTotals.fragment.xml webapp/types/PurchaseContractsTotal.ts webapp/controller/purchaseContracts webapp/view/purchaseContracts
git commit -m "feat(purchase-contract): quadro de Funrural no lugar da grade de impostos em STANDALONE" -m "Detail e Aprovação mostram INSS, RAT e SENAR da natureza do contrato sobre o total fixado e o líquido ao produtor; Add e Edit deixam de oferecer a grade digitada. Em SAPB1 tudo como antes." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT"
```

---

### Task 9: Verificação ponta a ponta

**Files:** nenhum código novo — achados viram correção na task dona, com commit próprio.

- [ ] **Step 1: Gates completos**

Backend: `dotnet build SiagroB1.sln`, `dotnet test SiagroB1.Application.Tests`, os dois `has-pending-model-changes`. Frontend: `yarn ts-typecheck && yarn lint`, e o ui5lint do repo sem regra nova.

- [ ] **Step 2: Aplicar migrations só no CEAGUI dev**

Ver a memória `run-stack-locally` e `db-migration-profile-targets-production` antes. Com `ASPNETCORE_ENVIRONMENT` explícito apontando para o CEAGUI **dev** (`CEAGUI_SIAGRO_DEV` / `CEAGUI_SIAGRO_COMMON_DEV`):
```bash
dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef database update --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Conferir o banco-alvo na saída **antes** de confirmar. Nunca produção.

- [ ] **Step 3: Roteiro no navegador (CEAGUI, STANDALONE, profile `ceagui` + `yarn start:dev`)** — login pelo usuário.

1. Cadastrar natureza de **entrada** PF com 1,32 / 0,11 / 0,20.
2. Criar contrato de compra fixo: Add/Edit **sem** grade de impostos. Enviar para aprovação e tentar aprovar → recusa "Informe a natureza de operação do contrato (Complemento Fiscal) antes de aprovar." **visível na tela** (não um genérico).
3. Detail → Complemento Fiscal → Editar → escolher a natureza. Quadro "Impostos" com 3 linhas, total e líquido; conferir contra a conta à mão. Cabeçalho: Total Impostos = Total Funrural; Líquido ao produtor. Aprovar → passa.
4. Trocar a natureza no contrato aprovado → linha "Natureza de operação" no Log de Alterações. "Limpar" + Salvar → recusa com a mensagem da regra 3, diálogo segue aberto.
5. Login `teste.comercial` (sem a permissão) → seção visível, sem "Editar".
6. Tela de Aprovação de outro contrato → quadro, aviso e Complemento Fiscal somente leitura.
7. Contrato PAF sem fixação → base zero; confirmar uma fixação → quadro atualiza sem F5.
8. Yokotobi (`SAPB1`, profile `yktb`) → sem seção Complemento Fiscal, grade digitada presente em Add/Edit/Detail, aprovação sem exigência.
9. Derrubar a stack **matando por PID** nas portas 50000/5246/8080.

- [ ] **Step 4: Fechamento**

Anotar contagem de testes, achados e correções. Atualizar a memória do projeto (nova entrada para o sub-projeto 1, com as migrations aplicadas e onde). Merge em `main` é decisão do usuário; **nunca push**.
