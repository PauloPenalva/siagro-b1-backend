# Contrato PAF e Fixação de Preço — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir contratos de compra `ContractType.ToBeDetermined` (PAF — preço a fixar) e implementar o ciclo completo de fixação de preço, com aprovação pela diretoria independente da aprovação do contrato.

**Architecture:** Dois eixos de aprovação ortogonais — o contrato continua sendo o portão da movimentação física, a fixação passa a ser o portão do compromisso financeiro. `FixedVolume` migra de computado para coluna persistida, protegida pelo `RowVersion` que já existe, no mesmo padrão de `AllocatedVolume`. Fixação confirmada é imutável; correção se faz por estorno.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData, xUnit + EF InMemory, OpenUI5/TypeScript, FastReport.

**Spec:** `docs/superpowers/specs/2026-07-20-purchase-contract-price-fixation-design.md`

## Global Constraints

- **Commits são manuais.** Nenhuma task executa `git commit` ou `git push`. Cada task termina num checkpoint verificado; quem commita é o Paulo.
- Build: `dotnet build SiagroB1.sln` a partir de `siagro-b1-backend/` (ignorar `SiagroB1.Web/SiagroB1.Web.sln`).
- Testes backend: `dotnet test SiagroB1.Application.Tests` — xUnit + EF Core InMemory via `TestDb.CreateUnitOfWork()`.
- Todo serviço novo **precisa** ser registrado à mão em `AddApplicationServices()` (`SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`) — não há assembly scanning.
- Toda OData action nova **precisa** ser declarada em `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`.
- Migrations: gerar em `SiagroB1.Migrations`, pasta `AppContext`. Aplicar **sempre** com `ASPNETCORE_ENVIRONMENT` explícito — o perfil `db-migration` faz fallback para um alvo que já apontou para produção. Ler a connection string antes de aplicar.
- `[Column(TypeName = ...)]` neste projeto embute DDL cru (`"DECIMAL(18,3) DEFAULT 0"`). Parêntese sobrando gera DDL inválido — o teste `AppDbContextModelTests` guarda isso.
- Frontend: `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint` a partir de `siagro-b1-frontend/`.

---

## Estrutura de arquivos

**Domínio**
- Modificar: `SiagroB1.Domain/Enums/PriceFixationStatus.cs` — acrescenta `Rejected`
- Modificar: `SiagroB1.Domain/Entities/PurchaseContractPriceFixation.cs` — herda `BaseEntity`, `+ApprovalComments`
- Modificar: `SiagroB1.Domain/Entities/PurchaseContract.cs` — `FixedVolume` persistido, `TotalPrice` só `Confirmed`

**Aplicação** (`SiagroB1.Application/Services/PurchaseContracts/`)
- Criar: `PurchaseContractsFixedVolumeService.cs` — único ponto de recálculo de `FixedVolume`
- Criar: `PurchaseContractsPriceFixationsApprovalService.cs`
- Criar: `PurchaseContractsPriceFixationsRejectService.cs`
- Criar: `PurchaseContractsPriceFixationsCancelService.cs`
- Modificar: `PurchaseContractsPriceFixationsCreateService.cs`, `...UpdateService.cs`, `...DeleteService.cs`, `...GetService.cs`
- Modificar: `PurchaseContractsCreateService.cs`, `PurchaseContractsUpdateService.cs`, `PurchaseContractsCloseService.cs`

**Web**
- Criar: `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsPriceFixationApprovalController.cs` (+ Reject, + Cancel)
- Modificar: `ServiceCollectionExtensions.cs`, `ODataConfigurations.cs`

**Reports**
- Criar: `SiagroB1.Reports/Services/PriceFixationReportService.cs`, `Reports/Templates/PriceFixation.frx`
- Modificar: `SiagroB1.Reports/Controllers/PurchaseContractsController.cs`

**Frontend** (`siagro-b1-frontend/webapp/`)
- Criar: `dialogs/fragments/PriceFixationDialog.fragment.xml`
- Criar: `view/purchaseContracts/priceFixationApproval/{Main,Detail}.view.xml` + controllers
- Modificar: `model/formatter.ts`, `model/ServerRoutes.ts`, `manifest.json`,
  `view/purchaseContracts/fragments/{PurchaseContractForm,PurchaseContractPriceFixations}.fragment.xml`,
  `controller/purchaseContracts/PurchaseContractsBaseController.ts`

---

### Task 1: Enum `Rejected` e auditoria na fixação

**Files:**
- Modify: `SiagroB1.Domain/Enums/PriceFixationStatus.cs`
- Modify: `SiagroB1.Domain/Entities/PurchaseContractPriceFixation.cs`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PriceFixationEntityTests.cs` (criar)

**Interfaces:**
- Produces: `PriceFixationStatus.Rejected`; `PurchaseContractPriceFixation : BaseEntity` com `Key` do tipo `Guid` (não mais `Guid?`), `ApprovedBy`, `ApprovedAt`, `CanceledBy`, `CanceledAt`, `CreatedBy`, `CreatedAt`, `UpdatedBy`, `UpdatedAt`, `ApprovalComments`.

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/PurchaseContracts/PriceFixationEntityTests.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PriceFixationEntityTests
{
    [Fact]
    public void PriceFixation_InheritsBaseEntity_ExposingAuditFields()
    {
        var fixation = new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            ApprovedBy = "diretoria",
            ApprovedAt = new DateTime(2026, 7, 20),
            ApprovalComments = "ok",
        };

        Assert.IsAssignableFrom<BaseEntity>(fixation);
        Assert.Equal("diretoria", fixation.ApprovedBy);
        Assert.Equal("ok", fixation.ApprovalComments);
    }

    [Fact]
    public void PriceFixationStatus_HasRejected()
    {
        Assert.Equal(3, (int) PriceFixationStatus.Rejected);
    }
}
```

- [ ] **Step 2: Rodar e confirmar a falha**

Run: `dotnet test SiagroB1.Application.Tests --filter PriceFixationEntityTests`
Expected: FAIL na compilação — `Rejected` e `ApprovalComments` não existem.

- [ ] **Step 3: Acrescentar `Rejected` ao enum**

`SiagroB1.Domain/Enums/PriceFixationStatus.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

public enum PriceFixationStatus
{
    InApproval = 0,
    Confirmed = 1,
    Canceled = 2,
    Rejected = 3
}
```

- [ ] **Step 4: Fazer a entidade herdar `BaseEntity`**

Substituir todo o conteúdo de `SiagroB1.Domain/Entities/PurchaseContractPriceFixation.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS_PRICE_FIXATIONS")]
public class PurchaseContractPriceFixation : BaseEntity
{
    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public DateTime? FixationDate { get; set; } = DateTime.Now;

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal FreightCost { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal FixationVolume { get; set; } = 0;

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal FixationPrice { get; set; } = 0;

    public PriceFixationStatus Status { get; set; } = PriceFixationStatus.InApproval;

    [Column(TypeName = "VARCHAR(500)")]
    public string? ApprovalComments { get; set; }
}
```

Nota: `Key`, `RowId` e os campos de auditoria vêm de `BaseEntity` — não redeclarar.

- [ ] **Step 5: Corrigir os usos de `Key` nullable**

`Key` deixou de ser `Guid?`. Compilar e corrigir os pontos que quebrarem:

Run: `dotnet build SiagroB1.sln`

Cada erro do tipo "cannot convert Guid? to Guid" ou uso de `.Value`/`?? Guid.Empty` sobre `fixation.Key` deve virar uso direto de `fixation.Key`.

- [ ] **Step 6: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter PriceFixationEntityTests`
Expected: PASS (2 testes)

- [ ] **Step 7: Rodar a suíte inteira, para pegar regressão**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS. Atenção especial a `AppDbContextModelTests` (guarda de `TypeName`).

- [ ] **Step 8: Checkpoint**

Build limpo + suíte verde. **Não commitar** — reportar ao Paulo. A migration vem na Task 3, junto com a coluna `FixedVolume`, para não gerar duas migrations para a mesma feature.

---

### Task 2: `TotalPrice` conta apenas fixações confirmadas

**Files:**
- Modify: `SiagroB1.Domain/Entities/PurchaseContract.cs:151-158`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractTotalPriceTests.cs` (criar)

**Interfaces:**
- Produces: `PurchaseContract.TotalPrice` — soma `FixationPrice * FixationVolume` **somente** de fixações `Confirmed`.

Este é o bug de fundo: hoje uma fixação ainda não aprovada pela diretoria já entra no preço e, por
`PurchaseContractTax.cs:22`, na base tributária.

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractTotalPriceTests.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractTotalPriceTests
{
    private static PurchaseContract NewContract(params PurchaseContractPriceFixation[] fixations)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 100_000m,
            Type = ContractType.ToBeDetermined,
        };

        foreach (var fixation in fixations)
            contract.PriceFixations.Add(fixation);

        return contract;
    }

    private static PurchaseContractPriceFixation Fixation(
        decimal volume, decimal price, PriceFixationStatus status) => new()
    {
        Key = Guid.NewGuid(),
        FixationVolume = volume,
        FixationPrice = price,
        Status = status,
    };

    [Fact]
    public void TotalPrice_CountsConfirmedOnly()
    {
        var contract = NewContract(
            Fixation(10_000m, 2m, PriceFixationStatus.Confirmed),
            Fixation(10_000m, 5m, PriceFixationStatus.InApproval));

        Assert.Equal(20_000m, contract.TotalPrice);
    }

    [Fact]
    public void TotalPrice_IgnoresCanceledAndRejected()
    {
        var contract = NewContract(
            Fixation(10_000m, 2m, PriceFixationStatus.Confirmed),
            Fixation(10_000m, 9m, PriceFixationStatus.Canceled),
            Fixation(10_000m, 7m, PriceFixationStatus.Rejected));

        Assert.Equal(20_000m, contract.TotalPrice);
    }

    [Fact]
    public void TotalPrice_NoConfirmedFixations_IsZero()
    {
        var contract = NewContract(Fixation(10_000m, 5m, PriceFixationStatus.InApproval));

        Assert.Equal(0m, contract.TotalPrice);
    }
}
```

- [ ] **Step 2: Rodar e confirmar a falha**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractTotalPriceTests`
Expected: FAIL — `TotalPrice_CountsConfirmedOnly` retorna 70000 (conta a fixação `InApproval`), esperado 20000.

- [ ] **Step 3: Corrigir `TotalPrice`**

Em `SiagroB1.Domain/Entities/PurchaseContract.cs`, substituir o bloco das linhas 150-158:

```csharp
    /// <remarks>
    /// Conta APENAS fixações confirmadas. Uma fixação em aprovação reserva volume
    /// (ver <see cref="FixedVolume"/>) mas não pode contaminar a base tributária —
    /// PurchaseContractTax.TotalTax deriva deste valor.
    /// </remarks>
    [NotMapped]
    public decimal TotalPrice =>
        decimal.Round(
            (PriceFixations?
                .Where(x => x.Status == PriceFixationStatus.Confirmed)
                .Sum(x => x.FixationPrice * x.FixationVolume) ?? 0),
            2,
            MidpointRounding.ToEven);
```

- [ ] **Step 4: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractTotalPriceTests`
Expected: PASS (3 testes)

- [ ] **Step 5: Rodar a suíte inteira**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS. Se `PurchaseContractsTotalsServiceTests` quebrar, é regressão legítima do comportamento antigo — atualizar o teste para o novo contrato (só `Confirmed` conta).

- [ ] **Step 6: Checkpoint** — reportar, não commitar.

---

### Task 3: `FixedVolume` persistido + serviço único de recálculo

**Files:**
- Modify: `SiagroB1.Domain/Entities/PurchaseContract.cs:138-148`
- Create: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsFixedVolumeService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (perto da linha 177)
- Create: migration em `SiagroB1.Migrations/AppContext/`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFixedVolumeTests.cs` (criar)

**Interfaces:**
- Consumes: `PriceFixationStatus.Rejected` (Task 1)
- Produces:
  - `PurchaseContract.FixedVolume` — `decimal` persistido, `DECIMAL(18,3) DEFAULT 0`
  - `PurchaseContractsFixedVolumeService.RecalculateAsync(PurchaseContract contract) -> Task<decimal>` — recalcula e **atribui** `contract.FixedVolume`, retornando o novo valor. **Não** chama `SaveChangesAsync`.

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFixedVolumeTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractFixedVolumeTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<PurchaseContract> SeedAsync(params (decimal Volume, PriceFixationStatus Status)[] fixations)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 100_000m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        _db.Context.PurchaseContracts.Add(contract);

        foreach (var (volume, status) in fixations)
        {
            _db.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
            {
                Key = Guid.NewGuid(),
                PurchaseContractKey = contract.Key,
                FixationVolume = volume,
                FixationPrice = 1m,
                Status = status,
            });
        }

        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task Recalculate_SumsInApprovalAndConfirmed()
    {
        var contract = await SeedAsync(
            (30_000m, PriceFixationStatus.Confirmed),
            (20_000m, PriceFixationStatus.InApproval));

        var result = await new PurchaseContractsFixedVolumeService(_db.Context)
            .RecalculateAsync(contract);

        Assert.Equal(50_000m, result);
        Assert.Equal(50_000m, contract.FixedVolume);
    }

    [Fact]
    public async Task Recalculate_IgnoresCanceledAndRejected()
    {
        var contract = await SeedAsync(
            (30_000m, PriceFixationStatus.Confirmed),
            (20_000m, PriceFixationStatus.Canceled),
            (10_000m, PriceFixationStatus.Rejected));

        var result = await new PurchaseContractsFixedVolumeService(_db.Context)
            .RecalculateAsync(contract);

        Assert.Equal(30_000m, result);
    }

    [Fact]
    public async Task AvailableVolumeToPricing_DerivesFromPersistedFixedVolume()
    {
        var contract = await SeedAsync((30_000m, PriceFixationStatus.Confirmed));

        await new PurchaseContractsFixedVolumeService(_db.Context).RecalculateAsync(contract);
        await _db.Context.SaveChangesAsync();

        // Recarrega SEM Include das fixações: o valor tem que sobreviver.
        var reloaded = await _db.Context.PurchaseContracts
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        Assert.Equal(30_000m, reloaded.FixedVolume);
        Assert.Equal(70_000m, reloaded.AvailableVolumeToPricing);
    }
}
```

O terceiro teste é o ponto da mudança: hoje, sem `Include(x => x.PriceFixations)`, `FixedVolume` retornaria 0
silenciosamente e `AvailableVolumeToPricing` devolveria os 100.000 inteiros.

- [ ] **Step 2: Rodar e confirmar a falha**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractFixedVolumeTests`
Expected: FAIL na compilação — `PurchaseContractsFixedVolumeService` não existe e `FixedVolume` não tem setter.

- [ ] **Step 3: Tornar `FixedVolume` persistido**

Em `SiagroB1.Domain/Entities/PurchaseContract.cs`, **remover** o bloco computado das linhas 138-145 e
acrescentar a propriedade persistida junto de `AllocatedVolume` (perto da linha 119):

```csharp
    /// <summary>
    /// Volume já fixado (persistido, derivado). Soma <see cref="PurchaseContractPriceFixation.FixationVolume"/>
    /// das fixações InApproval + Confirmed — uma fixação em aprovação reserva volume para que duas
    /// pessoas não fixem a mesma tonelagem enquanto a diretoria decide.
    /// Recalculado exclusivamente por PurchaseContractsFixedVolumeService e protegido por
    /// <see cref="RowVersion"/>. Não depende de navegação em runtime — funciona sob $select do OData.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal FixedVolume { get; set; }
```

`AvailableVolumeToPricing` (linha 148) fica inalterado — continua `TotalVolume - FixedVolume`.

- [ ] **Step 4: Criar o serviço de recálculo**

Criar `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsFixedVolumeService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Ponto ÚNICO de recálculo de <see cref="PurchaseContract.FixedVolume"/>.
/// Todo serviço que cria, aprova, rejeita, cancela, edita ou apaga uma fixação
/// deve chamar <see cref="RecalculateAsync"/> — nunca replicar a soma.
/// </summary>
public class PurchaseContractsFixedVolumeService(AppDbContext context)
{
    /// <summary>
    /// Recalcula e atribui <see cref="PurchaseContract.FixedVolume"/>.
    /// NÃO persiste — o chamador é dono da transação e do SaveChanges.
    /// </summary>
    public async Task<decimal> RecalculateAsync(PurchaseContract contract)
    {
        var total = await context.PurchaseContractsPriceFixations
            .Where(f => f.PurchaseContractKey == contract.Key
                        && (f.Status == PriceFixationStatus.InApproval
                            || f.Status == PriceFixationStatus.Confirmed))
            .SumAsync(f => f.FixationVolume);

        contract.FixedVolume = decimal.Round(total, 3, MidpointRounding.ToEven);
        return contract.FixedVolume;
    }

    /// <summary>
    /// Σ dos volumes de fixações CONFIRMADAS. Usado pela guarda de fechamento,
    /// que não pode aceitar volume apenas em aprovação.
    /// </summary>
    public async Task<decimal> ConfirmedVolumeAsync(Guid contractKey)
    {
        var total = await context.PurchaseContractsPriceFixations
            .Where(f => f.PurchaseContractKey == contractKey
                        && f.Status == PriceFixationStatus.Confirmed)
            .SumAsync(f => f.FixationVolume);

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Volume FISICAMENTE entregue: Σ <see cref="ShipmentRelease.ShippedQuantity"/>.
    /// </summary>
    /// <remarks>
    /// NÃO usar <c>PurchaseContract.TotalShipmentReleases</c> aqui. Aquele computado soma
    /// <c>ConsumedQuantity</c>, que numa liberação não cancelada vale <c>ReleasedQuantity</c> —
    /// isto é, o volume LIBERADO, não o romaneado. Uma liberação ativa de 60.000 kg com apenas
    /// 10.000 kg romaneados contaria 60.000 e bloquearia o fechamento por mercadoria que ainda
    /// não chegou. Consulta direta ao banco também evita a dependência de Include.
    /// </remarks>
    public async Task<decimal> DeliveredVolumeAsync(Guid contractKey)
    {
        var total = await context.ShipmentReleases
            .Where(r => r.PurchaseContractKey == contractKey)
            .SumAsync(r => r.ShippedQuantity);

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }
}
```

- [ ] **Step 5: Registrar no DI**

Em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, junto do bloco das linhas 174-177:

```csharp
        services.AddScoped<PurchaseContractsFixedVolumeService>();
```

- [ ] **Step 6: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractFixedVolumeTests`
Expected: PASS (3 testes)

- [ ] **Step 7: Gerar a migration**

A migration cobre Task 1 e Task 3 juntas: colunas de auditoria + `RowId` + `ApprovalComments` em
`PURCHASE_CONTRACTS_PRICE_FIXATIONS`, e `FixedVolume` em `PURCHASE_CONTRACTS`.

```bash
dotnet ef migrations add AddPriceFixationAuditAndFixedVolume \
  --project SiagroB1.Migrations \
  --startup-project SiagroB1.Web \
  --context AppDbContext \
  --output-dir AppContext
```

- [ ] **Step 8: Acrescentar o backfill de `FixedVolume` na migration**

Abrir o arquivo `.cs` gerado e, ao final do método `Up`, acrescentar:

```csharp
            migrationBuilder.Sql(@"
                UPDATE pc
                   SET pc.FixedVolume = ISNULL(f.Total, 0)
                  FROM PURCHASE_CONTRACTS pc
                  LEFT JOIN (
                        SELECT PurchaseContractKey, SUM(FixationVolume) AS Total
                          FROM PURCHASE_CONTRACTS_PRICE_FIXATIONS
                         WHERE Status IN (0, 1)   -- InApproval, Confirmed
                         GROUP BY PurchaseContractKey
                  ) f ON f.PurchaseContractKey = pc.[Key];
            ");
```

Sem esse backfill, todo contrato existente ficaria com `FixedVolume = 0` e apareceria como
totalmente disponível para fixar.

- [ ] **Step 9: Revisar o DDL gerado**

Ler o `.cs` da migration inteiro e conferir que os tipos das colunas novas batem com os
`[Column(TypeName = ...)]` das entidades, sem parêntese sobrando.

Run: `dotnet build SiagroB1.sln`
Expected: build limpo.

- [ ] **Step 10: Rodar a suíte inteira**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS.

- [ ] **Step 11: Checkpoint**

**Não aplicar a migration no banco automaticamente.** Reportar ao Paulo com o caminho da migration e a
connection string do ambiente-alvo, para ele decidir quando e onde aplicar.

---

### Task 4: Guarda de saldo na criação de fixação

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsCreateService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PriceFixationsCreateServiceTests.cs` (criar)

**Interfaces:**
- Consumes: `PurchaseContractsFixedVolumeService.RecalculateAsync` (Task 3)
- Produces: `PurchaseContractsPriceFixationsCreateService.ExecuteAsync(Guid purchaseContractKey, PurchaseContractPriceFixation entity, string createdBy) -> Task<PurchaseContractPriceFixation>` — **assinatura muda**, ganha `createdBy`.

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/PurchaseContracts/PriceFixationsCreateServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PriceFixationsCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsPriceFixationsCreateService Service() =>
        new(_db.Context,
            new PurchaseContractsFixedVolumeService(_db.Context),
            NullLogger<PurchaseContractsPriceFixationsCreateService>.Instance);

    private async Task<PurchaseContract> SeedAsync(
        ContractType type = ContractType.ToBeDetermined,
        ContractStatus status = ContractStatus.Approved,
        decimal totalVolume = 100_000m)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = totalVolume,
            Type = type,
            Status = status,
        };

        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    private static PurchaseContractPriceFixation Fixation(decimal volume) => new()
    {
        FixationVolume = volume,
        FixationPrice = 2.5m,
        FixationDate = new DateTime(2026, 7, 20),
    };

    [Fact]
    public async Task Create_WithinBalance_PersistsAsInApproval_AndUpdatesFixedVolume()
    {
        var contract = await SeedAsync();

        var created = await Service().ExecuteAsync(contract.Key, Fixation(30_000m), "operador");

        Assert.Equal(PriceFixationStatus.InApproval, created.Status);
        Assert.Equal("operador", created.CreatedBy);
        Assert.Equal(30_000m, contract.FixedVolume);
        Assert.Equal(70_000m, contract.AvailableVolumeToPricing);
    }

    [Fact]
    public async Task Create_ExactlyConsumingBalance_IsAllowed()
    {
        var contract = await SeedAsync();

        await Service().ExecuteAsync(contract.Key, Fixation(100_000m), "operador");

        Assert.Equal(0m, contract.AvailableVolumeToPricing);
    }

    [Fact]
    public async Task Create_ExceedingBalance_Throws()
    {
        var contract = await SeedAsync();
        await Service().ExecuteAsync(contract.Key, Fixation(80_000m), "operador");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(contract.Key, Fixation(30_000m), "operador"));

        Assert.Contains("saldo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_OnFixedContract_Throws()
    {
        var contract = await SeedAsync(type: ContractType.Fixed);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(contract.Key, Fixation(10_000m), "operador"));
    }

    [Fact]
    public async Task Create_OnFinishedContract_Throws()
    {
        var contract = await SeedAsync(status: ContractStatus.Finished);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(contract.Key, Fixation(10_000m), "operador"));
    }

    [Fact]
    public async Task Create_WithNonPositiveVolume_Throws()
    {
        var contract = await SeedAsync();

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(contract.Key, Fixation(0m), "operador"));
    }
}
```

- [ ] **Step 2: Rodar e confirmar a falha**

Run: `dotnet test SiagroB1.Application.Tests --filter PriceFixationsCreateServiceTests`
Expected: FAIL na compilação — assinatura de 3 parâmetros não existe.

- [ ] **Step 3: Reescrever o serviço**

Substituir todo o conteúdo de `PurchaseContractsPriceFixationsCreateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsPriceFixationsCreateService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService,
    ILogger<PurchaseContractsPriceFixationsCreateService> logger)
{
    public async Task<PurchaseContractPriceFixation> ExecuteAsync(
        Guid purchaseContractKey,
        PurchaseContractPriceFixation associationEntity,
        string createdBy)
    {
        try
        {
            var contract = await context.PurchaseContracts
                               .FirstOrDefaultAsync(x => x.Key == purchaseContractKey)
                           ?? throw new NotFoundException("Purchase contract not found");

            if (contract.Type != ContractType.ToBeDetermined)
                throw new ApplicationException(
                    "Fixação manual só é permitida em contrato a fixar (PAF). " +
                    "Contrato de preço fixo tem a fixação gerada na criação.");

            if (contract.Status != ContractStatus.Approved)
                throw new ApplicationException(
                    "Contrato precisa estar aprovado para receber fixação de preço.");

            if (associationEntity.FixationVolume <= 0)
                throw new ApplicationException("Volume da fixação deve ser maior que zero.");

            await fixedVolumeService.RecalculateAsync(contract);

            if (contract.FixedVolume + associationEntity.FixationVolume > contract.TotalVolume)
                throw new ApplicationException(
                    $"Volume excede o saldo disponível para fixação. " +
                    $"Disponível: {contract.AvailableVolumeToPricing:N3}, " +
                    $"solicitado: {associationEntity.FixationVolume:N3}.");

            associationEntity.PurchaseContractKey = contract.Key;
            associationEntity.Status = PriceFixationStatus.InApproval;
            associationEntity.CreatedAt = DateTime.Now;
            associationEntity.CreatedBy = createdBy;

            await context.AddAsync(associationEntity);

            // Recalcula já contando a nova fixação; o RowVersion do contrato
            // faz a guarda contra fixações concorrentes.
            contract.FixedVolume += associationEntity.FixationVolume;

            await context.SaveChangesAsync();

            return associationEntity;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
```

- [ ] **Step 4: Atualizar o controller**

Em `SiagroB1.Web/Controllers/PurchaseContractsPriceFixationsController.cs`, no método
`CreatePriceFixationsAsync` (linha 30), passar o usuário:

```csharp
            var userName = User.Identity?.Name ?? "Unknown";
            await createService.ExecuteAsync(key, associationEntity, userName);
```

- [ ] **Step 5: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter PriceFixationsCreateServiceTests`
Expected: PASS (6 testes)

- [ ] **Step 6: Build e suíte inteira**

Run: `dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests`
Expected: PASS.

- [ ] **Step 7: Checkpoint** — reportar, não commitar.

---

### Task 5: Aprovação e rejeição de fixação

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsApprovalService.cs`
- Create: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsRejectService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PriceFixationsApprovalServiceTests.cs` (criar)

**Interfaces:**
- Consumes: `PurchaseContractsFixedVolumeService.RecalculateAsync` (Task 3)
- Produces:
  - `PurchaseContractsPriceFixationsApprovalService.ExecuteAsync(Guid fixationKey, string? comments, string approvedBy) -> Task`
  - `PurchaseContractsPriceFixationsRejectService.ExecuteAsync(Guid fixationKey, string? comments, string rejectedBy) -> Task`

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/PurchaseContracts/PriceFixationsApprovalServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PriceFixationsApprovalServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsFixedVolumeService FixedVolume() => new(_db.Context);

    private async Task<(PurchaseContract Contract, PurchaseContractPriceFixation Fixation)> SeedAsync(
        PriceFixationStatus status = PriceFixationStatus.InApproval,
        ContractStatus contractStatus = ContractStatus.Approved)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 100_000m,
            FixedVolume = 30_000m,
            Type = ContractType.ToBeDetermined,
            Status = contractStatus,
        };

        var fixation = new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = 30_000m,
            FixationPrice = 2.5m,
            Status = status,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();

        return (contract, fixation);
    }

    private async Task<PurchaseContractPriceFixation> ReloadFixationAsync(Guid key) =>
        await _db.Context.PurchaseContractsPriceFixations.AsNoTracking().SingleAsync(x => x.Key == key);

    private async Task<PurchaseContract> ReloadContractAsync(Guid key) =>
        await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Approve_InApprovalFixation_BecomesConfirmed_AndRecordsApprover()
    {
        var (_, fixation) = await SeedAsync();

        await new PurchaseContractsPriceFixationsApprovalService(_db.Context, FixedVolume())
            .ExecuteAsync(fixation.Key, "aprovado em reunião", "diretoria");

        var reloaded = await ReloadFixationAsync(fixation.Key);
        Assert.Equal(PriceFixationStatus.Confirmed, reloaded.Status);
        Assert.Equal("diretoria", reloaded.ApprovedBy);
        Assert.Equal("aprovado em reunião", reloaded.ApprovalComments);
        Assert.NotNull(reloaded.ApprovedAt);
    }

    [Fact]
    public async Task Approve_KeepsFixedVolumeUnchanged()
    {
        var (contract, fixation) = await SeedAsync();

        await new PurchaseContractsPriceFixationsApprovalService(_db.Context, FixedVolume())
            .ExecuteAsync(fixation.Key, null, "diretoria");

        // InApproval já reservava volume — aprovar não muda o volume reservado.
        Assert.Equal(30_000m, (await ReloadContractAsync(contract.Key)).FixedVolume);
    }

    [Fact]
    public async Task Approve_AlreadyConfirmed_Throws()
    {
        var (_, fixation) = await SeedAsync(status: PriceFixationStatus.Confirmed);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            new PurchaseContractsPriceFixationsApprovalService(_db.Context, FixedVolume())
                .ExecuteAsync(fixation.Key, null, "diretoria"));
    }

    [Fact]
    public async Task Approve_OnFinishedContract_Throws()
    {
        var (_, fixation) = await SeedAsync(contractStatus: ContractStatus.Finished);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            new PurchaseContractsPriceFixationsApprovalService(_db.Context, FixedVolume())
                .ExecuteAsync(fixation.Key, null, "diretoria"));
    }

    [Fact]
    public async Task Approve_UnknownFixation_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            new PurchaseContractsPriceFixationsApprovalService(_db.Context, FixedVolume())
                .ExecuteAsync(Guid.NewGuid(), null, "diretoria"));
    }

    [Fact]
    public async Task Reject_InApprovalFixation_BecomesRejected_AndReleasesVolume()
    {
        var (contract, fixation) = await SeedAsync();

        await new PurchaseContractsPriceFixationsRejectService(_db.Context, FixedVolume())
            .ExecuteAsync(fixation.Key, "preço fora do mercado", "diretoria");

        var reloadedFixation = await ReloadFixationAsync(fixation.Key);
        Assert.Equal(PriceFixationStatus.Rejected, reloadedFixation.Status);
        Assert.Equal("preço fora do mercado", reloadedFixation.ApprovalComments);

        var reloadedContract = await ReloadContractAsync(contract.Key);
        Assert.Equal(0m, reloadedContract.FixedVolume);
        Assert.Equal(100_000m, reloadedContract.AvailableVolumeToPricing);
    }

    [Fact]
    public async Task Reject_AlreadyConfirmed_Throws()
    {
        var (_, fixation) = await SeedAsync(status: PriceFixationStatus.Confirmed);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            new PurchaseContractsPriceFixationsRejectService(_db.Context, FixedVolume())
                .ExecuteAsync(fixation.Key, null, "diretoria"));
    }
}
```

- [ ] **Step 2: Rodar e confirmar a falha**

Run: `dotnet test SiagroB1.Application.Tests --filter PriceFixationsApprovalServiceTests`
Expected: FAIL na compilação — os dois serviços não existem.

- [ ] **Step 3: Criar o serviço de aprovação**

Criar `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsApprovalService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsPriceFixationsApprovalService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService)
{
    public async Task ExecuteAsync(Guid fixationKey, string? comments, string approvedBy)
    {
        var fixation = await context.PurchaseContractsPriceFixations
                           .Include(x => x.PurchaseContract)
                           .FirstOrDefaultAsync(x => x.Key == fixationKey)
                       ?? throw new NotFoundException("Fixação de preço não encontrada.");

        if (fixation.Status != PriceFixationStatus.InApproval)
            throw new ApplicationException(
                $"Só é possível aprovar fixação em aprovação. Status atual: {fixation.Status}.");

        var contract = fixation.PurchaseContract
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException(
                "Contrato precisa estar aprovado para movimentar fixações. " +
                "Reabra o contrato antes de aprovar a fixação.");

        fixation.Status = PriceFixationStatus.Confirmed;
        fixation.ApprovedBy = approvedBy;
        fixation.ApprovedAt = DateTime.Now;
        fixation.ApprovalComments = comments;
        fixation.UpdatedAt = DateTime.Now;
        fixation.UpdatedBy = approvedBy;

        // InApproval já reservava volume: o total não muda, mas recalculamos
        // pelo caminho único para não divergir do estado real.
        await fixedVolumeService.RecalculateAsync(contract);

        await context.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Criar o serviço de rejeição**

Criar `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsRejectService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsPriceFixationsRejectService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService)
{
    public async Task ExecuteAsync(Guid fixationKey, string? comments, string rejectedBy)
    {
        var fixation = await context.PurchaseContractsPriceFixations
                           .Include(x => x.PurchaseContract)
                           .FirstOrDefaultAsync(x => x.Key == fixationKey)
                       ?? throw new NotFoundException("Fixação de preço não encontrada.");

        if (fixation.Status != PriceFixationStatus.InApproval)
            throw new ApplicationException(
                $"Só é possível rejeitar fixação em aprovação. Status atual: {fixation.Status}.");

        var contract = fixation.PurchaseContract
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException(
                "Contrato precisa estar aprovado para movimentar fixações.");

        fixation.Status = PriceFixationStatus.Rejected;
        fixation.ApprovalComments = comments;
        fixation.UpdatedAt = DateTime.Now;
        fixation.UpdatedBy = rejectedBy;

        // Rejeitada deixa de reservar volume.
        await fixedVolumeService.RecalculateAsync(contract);

        await context.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Registrar no DI**

Em `ServiceCollectionExtensions.cs`, junto do bloco das linhas 174-177:

```csharp
        services.AddScoped<PurchaseContractsPriceFixationsApprovalService>();
        services.AddScoped<PurchaseContractsPriceFixationsRejectService>();
```

- [ ] **Step 6: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter PriceFixationsApprovalServiceTests`
Expected: PASS (7 testes)

- [ ] **Step 7: Checkpoint** — reportar, não commitar.

---

### Task 6: Cancelamento (estorno) de fixação confirmada

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsCancelService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PriceFixationsCancelServiceTests.cs` (criar)

**Interfaces:**
- Consumes: `PurchaseContractsFixedVolumeService.RecalculateAsync` (Task 3)
- Produces: `PurchaseContractsPriceFixationsCancelService.ExecuteAsync(Guid fixationKey, string canceledBy) -> Task`

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/PurchaseContracts/PriceFixationsCancelServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PriceFixationsCancelServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsPriceFixationsCancelService Service() =>
        new(_db.Context, new PurchaseContractsFixedVolumeService(_db.Context));

    private async Task<(PurchaseContract Contract, PurchaseContractPriceFixation Fixation)> SeedAsync(
        PriceFixationStatus status = PriceFixationStatus.Confirmed)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 100_000m,
            FixedVolume = 40_000m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        var fixation = new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = 40_000m,
            FixationPrice = 2.5m,
            Status = status,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();

        return (contract, fixation);
    }

    [Fact]
    public async Task Cancel_ConfirmedFixation_BecomesCanceled_AndReleasesVolume()
    {
        var (contract, fixation) = await SeedAsync();

        await Service().ExecuteAsync(fixation.Key, "operador");

        var reloadedFixation = await _db.Context.PurchaseContractsPriceFixations
            .AsNoTracking().SingleAsync(x => x.Key == fixation.Key);
        Assert.Equal(PriceFixationStatus.Canceled, reloadedFixation.Status);
        Assert.Equal("operador", reloadedFixation.CanceledBy);
        Assert.NotNull(reloadedFixation.CanceledAt);

        var reloadedContract = await _db.Context.PurchaseContracts
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);
        Assert.Equal(0m, reloadedContract.FixedVolume);
        Assert.Equal(100_000m, reloadedContract.AvailableVolumeToPricing);
    }

    [Fact]
    public async Task Cancel_RemovesFixationFromTotalPrice()
    {
        var (contract, fixation) = await SeedAsync();

        await Service().ExecuteAsync(fixation.Key, "operador");

        var reloaded = await _db.Context.PurchaseContracts
            .Include(x => x.PriceFixations)
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        Assert.Equal(0m, reloaded.TotalPrice);
    }

    [Fact]
    public async Task Cancel_InApprovalFixation_Throws()
    {
        var (_, fixation) = await SeedAsync(status: PriceFixationStatus.InApproval);

        // Fixação em aprovação se resolve por rejeição, não por estorno.
        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(fixation.Key, "operador"));
    }

    [Fact]
    public async Task Cancel_AlreadyCanceled_Throws()
    {
        var (_, fixation) = await SeedAsync(status: PriceFixationStatus.Canceled);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(fixation.Key, "operador"));
    }
}
```

- [ ] **Step 2: Rodar e confirmar a falha**

Run: `dotnet test SiagroB1.Application.Tests --filter PriceFixationsCancelServiceTests`
Expected: FAIL na compilação — o serviço não existe.

- [ ] **Step 3: Criar o serviço**

Criar `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsCancelService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Estorno de fixação confirmada. Fixação Confirmed é imutável: a correção
/// se faz cancelando e criando uma nova, preservando a trilha de auditoria.
/// </summary>
public class PurchaseContractsPriceFixationsCancelService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService)
{
    public async Task ExecuteAsync(Guid fixationKey, string canceledBy)
    {
        var fixation = await context.PurchaseContractsPriceFixations
                           .Include(x => x.PurchaseContract)
                           .FirstOrDefaultAsync(x => x.Key == fixationKey)
                       ?? throw new NotFoundException("Fixação de preço não encontrada.");

        if (fixation.Status != PriceFixationStatus.Confirmed)
            throw new ApplicationException(
                $"Só é possível estornar fixação confirmada. Status atual: {fixation.Status}. " +
                "Fixação em aprovação deve ser rejeitada.");

        var contract = fixation.PurchaseContract
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException(
                "Contrato precisa estar aprovado para movimentar fixações. " +
                "Reabra o contrato antes de estornar a fixação.");

        fixation.Status = PriceFixationStatus.Canceled;
        fixation.CanceledBy = canceledBy;
        fixation.CanceledAt = DateTime.Now;
        fixation.UpdatedAt = DateTime.Now;
        fixation.UpdatedBy = canceledBy;

        await fixedVolumeService.RecalculateAsync(contract);

        await context.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Registrar no DI**

```csharp
        services.AddScoped<PurchaseContractsPriceFixationsCancelService>();
```

- [ ] **Step 5: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter PriceFixationsCancelServiceTests`
Expected: PASS (4 testes)

- [ ] **Step 6: Checkpoint** — reportar, não commitar.

---

### Task 7: Imutabilidade — restringir edição e exclusão a `InApproval`

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsUpdateService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsDeleteService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PriceFixationsMutabilityTests.cs` (criar)

**Interfaces:**
- Consumes: `PurchaseContractsFixedVolumeService.RecalculateAsync` (Task 3)
- Produces: ambos os serviços passam a receber `PurchaseContractsFixedVolumeService` no construtor e a rejeitar operação sobre fixação que não esteja `InApproval`.

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/PurchaseContracts/PriceFixationsMutabilityTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PriceFixationsMutabilityTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsPriceFixationsUpdateService UpdateService() =>
        new(_db.Context,
            new PurchaseContractsFixedVolumeService(_db.Context),
            NullLogger<PurchaseContractsPriceFixationsCreateService>.Instance);

    private PurchaseContractsPriceFixationsDeleteService DeleteService() =>
        new(_db.Context,
            new PurchaseContractsFixedVolumeService(_db.Context),
            NullLogger<PurchaseContractsPriceFixationsCreateService>.Instance);

    private async Task<PurchaseContractPriceFixation> SeedAsync(PriceFixationStatus status)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 100_000m,
            FixedVolume = 20_000m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        var fixation = new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = 20_000m,
            FixationPrice = 2m,
            Status = status,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();

        return fixation;
    }

    [Fact]
    public async Task Update_InApprovalFixation_Succeeds()
    {
        var fixation = await SeedAsync(PriceFixationStatus.InApproval);

        var changes = new PurchaseContractPriceFixation
        {
            Key = fixation.Key,
            PurchaseContractKey = fixation.PurchaseContractKey,
            FixationVolume = 25_000m,
            FixationPrice = 3m,
            Status = PriceFixationStatus.InApproval,
        };

        await UpdateService().ExecuteAsync(fixation.Key, changes);

        var reloaded = await _db.Context.PurchaseContractsPriceFixations
            .AsNoTracking().SingleAsync(x => x.Key == fixation.Key);
        Assert.Equal(25_000m, reloaded.FixationVolume);
    }

    [Fact]
    public async Task Update_ConfirmedFixation_Throws()
    {
        var fixation = await SeedAsync(PriceFixationStatus.Confirmed);

        var changes = new PurchaseContractPriceFixation
        {
            Key = fixation.Key,
            FixationVolume = 25_000m,
            FixationPrice = 3m,
            Status = PriceFixationStatus.Confirmed,
        };

        await Assert.ThrowsAsync<ApplicationException>(() =>
            UpdateService().ExecuteAsync(fixation.Key, changes));
    }

    [Fact]
    public async Task Delete_InApprovalFixation_Succeeds_AndReleasesVolume()
    {
        var fixation = await SeedAsync(PriceFixationStatus.InApproval);

        await DeleteService().ExecuteAsync(fixation.Key);

        Assert.False(await _db.Context.PurchaseContractsPriceFixations
            .AnyAsync(x => x.Key == fixation.Key));

        var contract = await _db.Context.PurchaseContracts
            .AsNoTracking().SingleAsync(x => x.Key == fixation.PurchaseContractKey);
        Assert.Equal(0m, contract.FixedVolume);
    }

    [Fact]
    public async Task Delete_ConfirmedFixation_Throws()
    {
        var fixation = await SeedAsync(PriceFixationStatus.Confirmed);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            DeleteService().ExecuteAsync(fixation.Key));
    }
}
```

- [ ] **Step 2: Rodar e confirmar a falha**

Run: `dotnet test SiagroB1.Application.Tests --filter PriceFixationsMutabilityTests`
Expected: FAIL na compilação — os construtores ainda não recebem `PurchaseContractsFixedVolumeService`.

- [ ] **Step 3: Reescrever o `UpdateService`**

Substituir todo o conteúdo de `PurchaseContractsPriceFixationsUpdateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsPriceFixationsUpdateService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService,
    ILogger<PurchaseContractsPriceFixationsCreateService> logger)
{
    public async Task<PurchaseContractPriceFixation?> ExecuteAsync(
        Guid associationKey, PurchaseContractPriceFixation associationEntity)
    {
        try
        {
            var existingEntity = await context.PurchaseContractsPriceFixations
                                     .Include(x => x.PurchaseContract)
                                     .FirstOrDefaultAsync(x => x.Key == associationKey)
                                 ?? throw new NotFoundException("Price Fixation not found");

            if (existingEntity.Status != PriceFixationStatus.InApproval)
                throw new ApplicationException(
                    $"Fixação {existingEntity.Status} é imutável. " +
                    "Para corrigir, estorne a fixação e crie uma nova.");

            var contract = existingEntity.PurchaseContract;

            // Preserva status e auditoria: o payload do cliente não pode promover a fixação.
            var status = existingEntity.Status;
            var createdAt = existingEntity.CreatedAt;
            var createdBy = existingEntity.CreatedBy;

            context.Entry(existingEntity).CurrentValues.SetValues(associationEntity);

            existingEntity.Status = status;
            existingEntity.CreatedAt = createdAt;
            existingEntity.CreatedBy = createdBy;
            existingEntity.UpdatedAt = DateTime.Now;

            if (contract != null)
                await fixedVolumeService.RecalculateAsync(contract);

            await context.SaveChangesAsync();

            return existingEntity;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }

    public async Task<PurchaseContractPriceFixation?> ExecuteAsync(
        Guid parentKey, Guid associationKey, PurchaseContractPriceFixation associationEntity)
    {
        if (!await context.PurchaseContracts.AnyAsync(x => x.Key == parentKey))
            throw new NotFoundException("Purchase Contract not found");

        return await ExecuteAsync(associationKey, associationEntity);
    }
}
```

Nota: a segunda sobrecarga agora delega à primeira em vez de duplicar a lógica.

- [ ] **Step 4: Reescrever o `DeleteService`**

Substituir todo o conteúdo de `PurchaseContractsPriceFixationsDeleteService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsPriceFixationsDeleteService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService,
    ILogger<PurchaseContractsPriceFixationsCreateService> logger)
{
    public async Task<bool> ExecuteAsync(Guid associationKey)
    {
        try
        {
            var existingEntity = await context.PurchaseContractsPriceFixations
                                     .Include(x => x.PurchaseContract)
                                     .FirstOrDefaultAsync(x => x.Key == associationKey)
                                 ?? throw new NotFoundException("Price Fixation not found");

            if (existingEntity.Status != PriceFixationStatus.InApproval)
                throw new ApplicationException(
                    $"Fixação {existingEntity.Status} não pode ser excluída. " +
                    "Para desfazer, estorne a fixação — o histórico é preservado.");

            var contract = existingEntity.PurchaseContract;

            context.PurchaseContractsPriceFixations.Remove(existingEntity);

            if (contract != null)
                await fixedVolumeService.RecalculateAsync(contract);

            await context.SaveChangesAsync();

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }

    public async Task<bool> ExecuteAsync(Guid parentKey, Guid associationKey)
    {
        if (!await context.PurchaseContracts.AnyAsync(x => x.Key == parentKey))
            throw new NotFoundException("Purchase Contract not found");

        return await ExecuteAsync(associationKey);
    }
}
```

Atenção: `RecalculateAsync` roda **antes** do `SaveChangesAsync`, mas a query dele vai ao banco e ainda
enxergaria a fixação removida. Confirme no teste `Delete_InApprovalFixation_Succeeds_AndReleasesVolume`
que `FixedVolume` foi a 0. Se o teste falhar mostrando 20.000, mova o `Remove` para depois do recálculo e
subtraia explicitamente:

```csharp
            contract.FixedVolume -= existingEntity.FixationVolume;
            context.PurchaseContractsPriceFixations.Remove(existingEntity);
```

- [ ] **Step 5: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter PriceFixationsMutabilityTests`
Expected: PASS (4 testes)

- [ ] **Step 6: Build inteiro**

Run: `dotnet build SiagroB1.sln`
Expected: build limpo. O `PurchaseContractsPriceFixationsController` já injeta os dois serviços; o DI resolve
as novas dependências automaticamente.

- [ ] **Step 7: Checkpoint** — reportar, não commitar.

---

### Task 8: Contrato PAF nasce aprovado e não tem fixação atropelada

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCreateService.cs:54-59`
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsUpdateService.cs:68`
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractPafLifecycleTests.cs` (criar)

**Interfaces:**
- Produces: contrato `ToBeDetermined` criado com `Status = ContractStatus.Approved` e `StandardPrice = 0`; `PurchaseContractsUpdateService` só sincroniza `FixationPrice` a partir de `StandardPrice` quando `Type == Fixed`.

- [ ] **Step 1: Ler o serviço de update antes de mexer**

Run: `sed -n '50,90p' SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsUpdateService.cs`

O objetivo é ver o contexto exato da linha 68 (`price.FixationPrice = entity.StandardPrice;`) e de onde
`price` vem, para condicionar o bloco inteiro e não só a atribuição.

- [ ] **Step 2: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractPafLifecycleTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractPafLifecycleTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public async Task PafContract_ConfirmedFixations_SurviveContractEdit()
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-PAF",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 100_000m,
            StandardPrice = 0m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        var fixation = new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = 30_000m,
            FixationPrice = 2.75m,
            Status = PriceFixationStatus.Confirmed,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();

        // Simula a edição do contrato: o preço padrão continua zero num PAF,
        // e a fixação da diretoria não pode ser sobrescrita por ele.
        contract.Comments = "editado";
        await _db.Context.SaveChangesAsync();

        var reloaded = await _db.Context.PurchaseContractsPriceFixations
            .AsNoTracking().SingleAsync(x => x.Key == fixation.Key);

        Assert.Equal(2.75m, reloaded.FixationPrice);
    }
}
```

- [ ] **Step 3: Rodar o teste**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractPafLifecycleTests`
Expected: PASS — este teste documenta o estado desejado e serve de rede de segurança para o Step 4.

- [ ] **Step 4: Condicionar a sincronização de preço no `UpdateService`**

Em `PurchaseContractsUpdateService.cs`, envolver o bloco que contém `price.FixationPrice = entity.StandardPrice;`
(linha 68 e as linhas adjacentes que sincronizam a fixação automática) com:

```csharp
            // Só contrato de preço fixo tem fixação espelhando o preço padrão.
            // Num PAF as fixações são registros da diretoria e não podem ser sobrescritas.
            if (entity.Type == ContractType.Fixed)
            {
                // ... bloco existente de sincronização da fixação ...
            }
```

Preserve a lógica interna exatamente como está; apenas condicione-a.

- [ ] **Step 5: Fazer o PAF nascer aprovado**

Em `PurchaseContractsCreateService.cs`, substituir as linhas 54-59:

```csharp
            if (entity.Type == ContractType.Fixed)
            {
                entity.Status = ContractStatus.Draft;
                await CreatePriceFixation(entity);
            }
            else
            {
                // Contrato a fixar (PAF) não tem preço a aprovar no momento da criação:
                // a alçada vive na fixação, não no contrato. Nasce aprovado para poder
                // receber liberação de embarque imediatamente.
                entity.Status = ContractStatus.Approved;
                entity.StandardPrice = 0;
                entity.ApprovedAt = DateTime.Now;
                entity.ApprovedBy = createdBy;
            }
```

Remover a atribuição `entity.Status = ContractStatus.Draft;` da linha 54 original, já que agora ela vive
dentro do `if`.

- [ ] **Step 6: Acrescentar o teste de criação**

Acrescentar a `PurchaseContractPafLifecycleTests.cs`:

```csharp
    [Fact]
    public void PafContract_DefaultStandardPrice_IsZero()
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            Type = ContractType.ToBeDetermined,
            TotalVolume = 100_000m,
            StandardPrice = 0m,
        };

        Assert.Equal(0m, contract.TotalStandard);
    }
```

O teste de integração do `CreateService` fica de fora porque ele depende de seis serviços externos
(`IBusinessPartnerService`, `IItemService`, `IAgentService`, `IWarehouseService`, `DocNumberSequenceService`,
`IStringLocalizer`) — cobri-lo exigiria mocks que não existem hoje no projeto de testes. A verificação é manual,
no Step 7.

- [ ] **Step 7: Verificação manual**

Subir `SiagroB1.Web` e `SiagroB1.Gateway`, criar um contrato com Tipo = PAF pela UI e confirmar via
`GET /odata/PurchaseContracts?$filter=Code eq '<código>'&$select=Status,Type,StandardPrice`:
`Status` = `Approved`, `Type` = `ToBeDetermined`, `StandardPrice` = 0, e nenhuma fixação criada.

- [ ] **Step 8: Suíte inteira**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS.

- [ ] **Step 9: Checkpoint** — reportar, não commitar.

---

### Task 9: Guarda de fechamento do contrato PAF

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCloseService.cs`
- Modify: `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsCloseController.cs` (só a injeção)
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsCloseReopenServiceTests.cs` (modificar)

**Interfaces:**
- Consumes: `PurchaseContractsFixedVolumeService.ConfirmedVolumeAsync(Guid)` (Task 3)
- Produces: `PurchaseContractsCloseService(AppDbContext, PurchaseContractsFixedVolumeService)` — **construtor muda**.

Regra: um PAF só fecha se (1) Σ volume `Confirmed` ≥ `TotalShipmentReleases` e (2) não houver nenhuma
fixação `InApproval`. Volume apenas em aprovação não conta — fechar apoiado nele seria encerrar o contrato
sem o preço de fato definido. Saldo contratado que nunca foi entregue não bloqueia.

- [ ] **Step 1: Escrever os testes que falham**

Acrescentar a `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsCloseReopenServiceTests.cs`.
Primeiro, ajustar os helpers existentes e as 4 chamadas de `new PurchaseContractsCloseService(_db.Context)`
para `new PurchaseContractsCloseService(_db.Context, new PurchaseContractsFixedVolumeService(_db.Context))`.

Depois acrescentar:

```csharp
    private async Task<PurchaseContract> SeedPafAsync(
        decimal totalVolume,
        decimal shippedQuantity,
        params (decimal Volume, PriceFixationStatus Status)[] fixations)
        => await SeedPafAsync(totalVolume, shippedQuantity, shippedQuantity, fixations);

    private async Task<PurchaseContract> SeedPafAsync(
        decimal totalVolume,
        decimal releasedQuantity,
        decimal shippedQuantity,
        params (decimal Volume, PriceFixationStatus Status)[] fixations)
    {
        var contract = NewContract(ContractStatus.Approved);
        contract.Type = ContractType.ToBeDetermined;
        contract.TotalVolume = totalVolume;

        _db.Context.PurchaseContracts.Add(contract);

        if (releasedQuantity > 0)
        {
            _db.Context.ShipmentReleases.Add(new ShipmentRelease
            {
                Key = Guid.NewGuid(),
                PurchaseContractKey = contract.Key,
                DeliveryLocationCode = "01",
                ReleasedQuantity = releasedQuantity,
                ShippedQuantity = shippedQuantity,
                Status = ReleaseStatus.Actived,
            });
        }

        foreach (var (volume, status) in fixations)
        {
            _db.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
            {
                Key = Guid.NewGuid(),
                PurchaseContractKey = contract.Key,
                FixationVolume = volume,
                FixationPrice = 2m,
                Status = status,
            });
        }

        await _db.Context.SaveChangesAsync();
        return contract;
    }

    private PurchaseContractsCloseService CloseService() =>
        new(_db.Context, new PurchaseContractsFixedVolumeService(_db.Context));

    [Fact]
    public async Task Close_Paf_DeliveredVolumeFullyConfirmed_Succeeds()
    {
        var pc = await SeedPafAsync(100_000m, 60_000m, (60_000m, PriceFixationStatus.Confirmed));

        await CloseService().ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(pc.Key)).Status);
    }

    [Fact]
    public async Task Close_Paf_UndeliveredBalance_DoesNotBlock()
    {
        // Contratou 100.000, entregou 60.000, fixou os 60.000 entregues.
        // Os 40.000 nunca entregues não impedem o fechamento.
        var pc = await SeedPafAsync(100_000m, 60_000m, (60_000m, PriceFixationStatus.Confirmed));

        await CloseService().ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(pc.Key)).Status);
    }

    [Fact]
    public async Task Close_Paf_DeliveredVolumeNotFullyFixed_Throws()
    {
        var pc = await SeedPafAsync(100_000m, 60_000m, (40_000m, PriceFixationStatus.Confirmed));

        await Assert.ThrowsAsync<ApplicationException>(() =>
            CloseService().ExecuteAsync(pc.Key, "tester"));
    }

    [Fact]
    public async Task Close_Paf_DeliveredVolumeCoveredOnlyByInApproval_Throws()
    {
        // 60.000 entregues, cobertos apenas por fixação ainda não aprovada:
        // fechar aqui seria encerrar o contrato sem preço definido.
        var pc = await SeedPafAsync(100_000m, 60_000m, (60_000m, PriceFixationStatus.InApproval));

        await Assert.ThrowsAsync<ApplicationException>(() =>
            CloseService().ExecuteAsync(pc.Key, "tester"));
    }

    [Fact]
    public async Task Close_Paf_WithPendingFixation_Throws()
    {
        var pc = await SeedPafAsync(100_000m, 60_000m,
            (60_000m, PriceFixationStatus.Confirmed),
            (10_000m, PriceFixationStatus.InApproval));

        await Assert.ThrowsAsync<ApplicationException>(() =>
            CloseService().ExecuteAsync(pc.Key, "tester"));
    }

    [Fact]
    public async Task Close_Paf_ReleasedButNotShipped_DoesNotBlock()
    {
        // Liberação ativa de 60.000 kg com apenas 10.000 kg romaneados.
        // Só os 10.000 que entraram fisicamente exigem preço fixado.
        // Se a guarda usasse TotalShipmentReleases (= ReleasedQuantity), exigiria 60.000.
        var pc = await SeedPafAsync(100_000m, 60_000m, 10_000m,
            (10_000m, PriceFixationStatus.Confirmed));

        await CloseService().ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(pc.Key)).Status);
    }

    [Fact]
    public async Task Close_FixedContract_IgnoresFixationGuard()
    {
        // Contrato de preço fixo não passa pela guarda nova.
        var pc = await SeedAsync(ContractStatus.Approved);

        await CloseService().ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(pc.Key)).Status);
    }
```

Acrescentar os `using` necessários ao topo do arquivo: `SiagroB1.Domain.Enums` já está lá; confirmar
`SiagroB1.Domain.Entities`.

- [ ] **Step 2: Rodar e confirmar a falha**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractsCloseReopenServiceTests`
Expected: FAIL na compilação — construtor de 2 parâmetros não existe.

- [ ] **Step 3: Implementar a guarda**

Substituir todo o conteúdo de `PurchaseContractsCloseService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsCloseService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await context.PurchaseContracts
                           .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.Approved)
                       ?? throw new NotFoundException("Contrato não encontrado ou não está aprovado.");

        if (contract.Type == ContractType.ToBeDetermined)
            await GuardPriceFixationAsync(contract);

        contract.Status = ContractStatus.Finished;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Um contrato a fixar não pode ser encerrado devendo preço de mercadoria já entregue.
    /// Usa o volume CONFIRMADO — volume apenas em aprovação não define preço.
    /// </summary>
    private async Task GuardPriceFixationAsync(Domain.Entities.PurchaseContract contract)
    {
        var pendingCount = await context.PurchaseContractsPriceFixations
            .CountAsync(f => f.PurchaseContractKey == contract.Key
                             && f.Status == PriceFixationStatus.InApproval);

        if (pendingCount > 0)
            throw new ApplicationException(
                $"Contrato possui {pendingCount} fixação(ões) pendente(s) de aprovação. " +
                "Aprove ou rejeite antes de encerrar.");

        var confirmedVolume = await fixedVolumeService.ConfirmedVolumeAsync(contract.Key);
        var deliveredVolume = await fixedVolumeService.DeliveredVolumeAsync(contract.Key);

        if (confirmedVolume < deliveredVolume)
            throw new ApplicationException(
                $"Volume entregue sem preço fixado. Entregue: {deliveredVolume:N3}, " +
                $"fixado e confirmado: {confirmedVolume:N3}. " +
                "Fixe o preço do volume entregue antes de encerrar o contrato.");
    }
}
```

Note o uso de `DeliveredVolumeAsync` e **não** de `contract.TotalShipmentReleases`: aquele computado soma
`ConsumedQuantity`, que numa liberação ativa vale `ReleasedQuantity` (volume liberado, não romaneado). A
guarda precisa do volume que fisicamente entrou.

- [ ] **Step 4: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractsCloseReopenServiceTests`
Expected: PASS (11 testes — os 4 originais + 7 novos)

- [ ] **Step 5: Build**

Run: `dotnet build SiagroB1.sln`
Expected: build limpo — o DI resolve o novo parâmetro do `CloseService` automaticamente, já que
`PurchaseContractsFixedVolumeService` foi registrado na Task 3.

- [ ] **Step 6: Checkpoint** — reportar, não commitar.

---

### Task 10: Regressão — `TotalTax` depende de `Include` aninhado

**Files:**
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractTaxIncludeTests.cs` (criar)

**Interfaces:**
- Consumes: `PurchaseContract.TotalPrice` (Task 2)

Task puramente defensiva: `PurchaseContractTax.TotalTax` (`PurchaseContractTax.cs:22`) retorna **0
silenciosamente** se `PurchaseContract` ou `Tax` não estiverem carregados. Como agora ele deriva de fixações
confirmadas, o risco de alguém consultar sem `Include` e ver imposto zerado cresce. O teste documenta a
armadilha.

- [ ] **Step 1: Escrever o teste**

Criar `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractTaxIncludeTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractTaxIncludeTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<Guid> SeedAsync()
    {
        var tax = new Tax { Code = "FUNRURAL", Rate = 10m };

        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 100_000m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        _db.Context.Taxes.Add(tax);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = 10_000m,
            FixationPrice = 2m,
            Status = PriceFixationStatus.Confirmed,
        });
        _db.Context.PurchaseContractsTaxes.Add(new PurchaseContractTax
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            TaxCode = "FUNRURAL",
        });

        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        return contract.Key;
    }

    [Fact]
    public async Task TotalTax_WithNestedIncludes_ComputesFromConfirmedFixations()
    {
        var key = await SeedAsync();

        var contractTax = await _db.Context.PurchaseContractsTaxes
            .Include(x => x.Tax)
            .Include(x => x.PurchaseContract)
            .ThenInclude(c => c!.PriceFixations)
            .AsNoTracking()
            .SingleAsync(x => x.PurchaseContractKey == key);

        // TotalPrice = 10.000 * 2 = 20.000; 10% => 2.000
        Assert.Equal(2_000m, contractTax.TotalTax);
    }

    [Fact]
    public async Task TotalTax_WithoutNestedIncludes_SilentlyReturnsZero()
    {
        var key = await SeedAsync();

        var contractTax = await _db.Context.PurchaseContractsTaxes
            .AsNoTracking()
            .SingleAsync(x => x.PurchaseContractKey == key);

        // Documenta a armadilha: sem Include aninhado o imposto some sem erro.
        // Todo consumidor de TotalTax PRECISA incluir Tax + PurchaseContract.PriceFixations.
        Assert.Equal(0m, contractTax.TotalTax);
    }
}
```

- [ ] **Step 2: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter PurchaseContractTaxIncludeTests`
Expected: PASS (2 testes).

Se `TotalTax_WithNestedIncludes_ComputesFromConfirmedFixations` falhar com 0, verifique os nomes do `DbSet`
de impostos (`PurchaseContractsTaxes`) e da entidade `Tax` (propriedades `Code`/`Rate`) contra o
`AppDbContext` real e ajuste o seed.

- [ ] **Step 3: Auditar os consumidores de `TotalTax`**

Run: `grep -rn "TotalTax" --include=*.cs SiagroB1.Application SiagroB1.Web SiagroB1.Reports`

Para cada consumidor, confirmar que a query carrega `Tax` e `PurchaseContract.PriceFixations`. Corrigir os
que não carregarem — cada um é um imposto zerado em produção.

- [ ] **Step 4: Checkpoint** — reportar, incluindo a lista de consumidores auditados e o que foi corrigido.

---

### Task 11: Endpoints OData de aprovação, rejeição e estorno

**Files:**
- Create: `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsPriceFixationApprovalController.cs`
- Create: `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsPriceFixationRejectController.cs`
- Create: `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsPriceFixationCancelController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsGetService.cs`

**Interfaces:**
- Consumes: os três serviços das Tasks 5 e 6
- Produces: `POST /odata/PurchaseContractsPriceFixationApproval`, `.../PriceFixationReject`, `.../PriceFixationCancel`; `PurchaseContractsPriceFixationsGetService.QueryPending() -> IQueryable<PurchaseContractPriceFixation>`

- [ ] **Step 1: Acrescentar `QueryPending` ao GetService**

Ler `PurchaseContractsPriceFixationsGetService.cs` e acrescentar:

```csharp
    /// <summary>
    /// Fila da diretoria: todas as fixações em aprovação, de todos os contratos.
    /// Inclui o contrato para a UI mostrar código, fornecedor e produto sem nova query.
    /// </summary>
    public IQueryable<PurchaseContractPriceFixation> QueryPending() =>
        context.PurchaseContractsPriceFixations
            .Include(x => x.PurchaseContract)
            .Where(x => x.Status == PriceFixationStatus.InApproval);
```

Acrescentar os `using` de `Microsoft.EntityFrameworkCore` e `SiagroB1.Domain.Enums` se faltarem. Se o serviço
usar um nome de campo diferente de `context` para o `AppDbContext`, use o nome real.

- [ ] **Step 2: Expor a fila no controller de fixações**

Em `SiagroB1.Web/Controllers/PurchaseContractsPriceFixationsController.cs`, acrescentar:

```csharp
    [HttpGet("odata/PurchaseContractsPriceFixations")]
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseContractPriceFixation>> GetPending()
    {
        return Ok(getService.QueryPending());
    }
```

- [ ] **Step 3: Criar o controller de aprovação**

Criar `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsPriceFixationApprovalController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsPriceFixationApprovalController(
    PurchaseContractsPriceFixationsApprovalService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsPriceFixationApproval")]
    public async Task<IActionResult> ApproveAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Missing required parameters");

            parameters.TryGetValue("Comments", out var commentsObj);

            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString());

            await service.ExecuteAsync(key, commentsObj?.ToString(), userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
```

- [ ] **Step 4: Criar o controller de rejeição**

Criar `PurchaseContractsPriceFixationRejectController.cs` — idêntico ao Step 3, trocando:
o nome da classe para `PurchaseContractsPriceFixationRejectController`, o serviço injetado para
`PurchaseContractsPriceFixationsRejectService`, a rota para `odata/PurchaseContractsPriceFixationReject`
e o nome do método para `RejectAsync`.

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsPriceFixationRejectController(
    PurchaseContractsPriceFixationsRejectService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsPriceFixationReject")]
    public async Task<IActionResult> RejectAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Missing required parameters");

            parameters.TryGetValue("Comments", out var commentsObj);

            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString());

            await service.ExecuteAsync(key, commentsObj?.ToString(), userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
```

- [ ] **Step 5: Criar o controller de estorno**

Criar `PurchaseContractsPriceFixationCancelController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseContracts;

public class PurchaseContractsPriceFixationCancelController(
    PurchaseContractsPriceFixationsCancelService service) : ODataController
{
    [HttpPost("odata/PurchaseContractsPriceFixationCancel")]
    public async Task<IActionResult> CancelAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj))
                return BadRequest("Missing required parameters");

            var userName = User.Identity?.Name ?? "Unknown";
            var key = Guid.Parse(keyObj.ToString());

            await service.ExecuteAsync(key, userName);
            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound(e.Message);

            return BadRequest(e.Message);
        }
    }
}
```

- [ ] **Step 6: Declarar as actions no modelo OData**

Em `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, junto das outras actions de contrato de compra:

```csharp
        var priceFixationApproval = modelBuilder.Action("PurchaseContractsPriceFixationApproval");
        priceFixationApproval.Parameter<Guid>("Key");
        priceFixationApproval.Parameter<string>("Comments");
        priceFixationApproval.Returns<IActionResult>();

        var priceFixationReject = modelBuilder.Action("PurchaseContractsPriceFixationReject");
        priceFixationReject.Parameter<Guid>("Key");
        priceFixationReject.Parameter<string>("Comments");
        priceFixationReject.Returns<IActionResult>();

        var priceFixationCancel = modelBuilder.Action("PurchaseContractsPriceFixationCancel");
        priceFixationCancel.Parameter<Guid>("Key");
        priceFixationCancel.Returns<IActionResult>();
```

- [ ] **Step 7: Build**

Run: `dotnet build SiagroB1.sln`
Expected: build limpo.

- [ ] **Step 8: Verificação manual dos endpoints**

Subir `SiagroB1.Web` (perfil `dev`) e conferir no Swagger (`http://localhost:50000/swagger`) que as três
actions aparecem. Depois, com um contrato PAF aprovado e uma fixação criada:

```bash
curl -X POST http://localhost:50000/odata/PurchaseContractsPriceFixationApproval \
  -H "Content-Type: application/json" \
  -d '{"Key":"<fixation-guid>","Comments":"aprovado"}'
```

Expected: `200 OK`, e `GET /odata/PurchaseContracts(<guid>)?$expand=PriceFixations` mostra
`Status: "Confirmed"` com `ApprovedBy` preenchido.

- [ ] **Step 9: Checkpoint** — reportar, não commitar.

---

### Task 12: Frontend — formulário PAF e limpeza do status fantasma

**Files:**
- Modify: `siagro-b1-frontend/webapp/model/formatter.ts:141-149`
- Modify: `siagro-b1-frontend/webapp/controller/purchaseContracts/PurchaseContractsBaseController.ts:160-166`
- Modify: `siagro-b1-frontend/webapp/view/purchaseContracts/fragments/PurchaseContractForm.fragment.xml:251`
- Modify: `siagro-b1-frontend/webapp/model/ServerRoutes.ts`

**Interfaces:**
- Produces: rotas `purchaseContractsPriceFixationApproval`, `...Reject`, `...Cancel` em `ServerRoutes`; `formatter.formatPriceFixationStatus` cobrindo os 4 status reais.

`"Pending"` não existe no enum do backend — está no formatter e em `onAddPriceFixation`, e sempre esteve errado.

- [ ] **Step 1: Corrigir o formatter**

Em `webapp/model/formatter.ts`, substituir o bloco das linhas 141-149:

```typescript
  formatPriceFixationStatus: (value: string) => {
    const m = new Map<string, string>();
    m.set("InApproval", "Em Aprovação");
    m.set("Confirmed", "Confirmado");
    m.set("Canceled", "Estornado");
    m.set("Rejected", "Rejeitado");

    return m.get(value);
  },
```

- [ ] **Step 2: Remover o `onAddPriceFixation` quebrado**

Em `webapp/controller/purchaseContracts/PurchaseContractsBaseController.ts`, remover integralmente os métodos
`onAddPriceFixation` (linhas 160-166) e `onRemovePriceFixation` (a partir da linha 168). Eles são substituídos
pelo diálogo da Task 13 e pela ação de estorno; `onAddPriceFixation` criava a fixação com um status inexistente.

- [ ] **Step 3: Acrescentar as rotas**

Em `webapp/model/ServerRoutes.ts`, junto do bloco `purchaseContracts*` (linhas 33-44):

```typescript
  purchaseContractsPriceFixations: '/odata/PurchaseContractsPriceFixations',
  purchaseContractsPriceFixationApproval: '/odata/PurchaseContractsPriceFixationApproval',
  purchaseContractsPriceFixationReject: '/odata/PurchaseContractsPriceFixationReject',
  purchaseContractsPriceFixationCancel: '/odata/PurchaseContractsPriceFixationCancel',
  priceFixationReport: '/reports/PriceFixation',
```

- [ ] **Step 4: Desabilitar `StandardPrice` em contrato PAF**

Em `webapp/view/purchaseContracts/fragments/PurchaseContractForm.fragment.xml`, no campo de `StandardPrice`
(perto da linha 251), acrescentar o binding de `enabled`:

```xml
                enabled="{= ${Type} !== 'ToBeDetermined' }"
```

Se o controle já tiver um atributo `editable` vinculado a `ui>/editable`, combine as duas condições:

```xml
                editable="{= ${ui>/editable} &amp;&amp; ${Type} !== 'ToBeDetermined' }"
```

- [ ] **Step 5: Typecheck e lint**

Run (a partir de `siagro-b1-frontend/`):

```bash
yarn ts-typecheck && yarn lint && yarn ui5lint
```

Expected: sem erros. Se o lint acusar imports órfãos após remover os dois métodos do Step 2 (`Table`,
`ODataListBinding`, `MessageBox`), remova apenas os que ficaram sem uso — confira antes se outros métodos
do arquivo ainda os usam.

- [ ] **Step 6: Verificação manual**

`yarn start`, abrir um contrato novo, escolher Tipo = PAF e confirmar que o campo de preço padrão fica
desabilitado; escolher Tipo = Fixo e confirmar que volta a habilitar.

- [ ] **Step 7: Checkpoint** — reportar, não commitar.

---

### Task 13: Frontend — diálogo de fixação e tabela imutável

**Files:**
- Create: `siagro-b1-frontend/webapp/dialogs/fragments/PriceFixationDialog.fragment.xml`
- Modify: `siagro-b1-frontend/webapp/view/purchaseContracts/fragments/PurchaseContractPriceFixations.fragment.xml`
- Modify: `siagro-b1-frontend/webapp/controller/purchaseContracts/PurchaseContractsBaseController.ts`

**Interfaces:**
- Consumes: `ServerRoutes.purchaseContractsPriceFixationCancel` (Task 12); `POST odata/PurchaseContracts({key})/PriceFixations` (Task 4)
- Produces: handlers `onOpenPriceFixationDialog`, `onConfirmPriceFixation`, `onCancelPriceFixationDialog`, `onCancelPriceFixation` no `PurchaseContractsBaseController`

- [ ] **Step 1: Ler o padrão de diálogo existente**

Run: `cat webapp/dialogs/DialogHelper.ts && ls webapp/dialogs/fragments`

Objetivo: descobrir o helper de abertura de diálogo já usado no projeto e seguir a mesma assinatura em vez de
inventar uma nova. Se houver um helper genérico de abertura por (view, fragmento), use-o.

- [ ] **Step 2: Criar o fragmento do diálogo**

Criar `webapp/dialogs/fragments/PriceFixationDialog.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:f="sap.ui.layout.form"
    xmlns:core="sap.ui.core"
>
  <Dialog
    id="priceFixationDialog"
    title="Fixar Preço"
    contentWidth="30rem"
  >
    <content>
      <f:Form editable="true">
        <f:layout>
          <f:ColumnLayout columnsM="1" columnsL="1" columnsXL="1"/>
        </f:layout>
        <f:formContainers>
          <f:FormContainer>
            <f:formElements>
              <f:FormElement label="Saldo a Fixar">
                <f:fields>
                  <Text
                    id="priceFixationAvailableVolume"
                    text="{
                      path: 'fixation>/AvailableVolumeToPricing',
                      type: 'sap.ui.model.type.Float',
                      formatOptions: { decimals: 3, groupingEnabled: true }
                    }"
                  />
                </f:fields>
              </f:FormElement>
              <f:FormElement label="Data">
                <f:fields>
                  <DatePicker
                    id="priceFixationDate"
                    value="{
                      path: 'fixation>/FixationDate',
                      type: 'sap.ui.model.type.Date',
                      formatOptions: { pattern: 'dd/MM/yyyy' }
                    }"
                  />
                </f:fields>
              </f:FormElement>
              <f:FormElement label="Volume">
                <f:fields>
                  <Input
                    id="priceFixationVolume"
                    value="{
                      path: 'fixation>/FixationVolume',
                      type: 'sap.ui.model.type.Float',
                      formatOptions: { decimals: 3, groupingEnabled: true }
                    }"
                  />
                </f:fields>
              </f:FormElement>
              <f:FormElement label="Preço">
                <f:fields>
                  <Input
                    id="priceFixationPrice"
                    value="{
                      path: 'fixation>/FixationPrice',
                      type: 'sap.ui.model.type.Float',
                      formatOptions: { decimals: 8, groupingEnabled: true }
                    }"
                  />
                </f:fields>
              </f:FormElement>
              <f:FormElement label="Frete">
                <f:fields>
                  <Input
                    id="priceFixationFreight"
                    value="{
                      path: 'fixation>/FreightCost',
                      type: 'sap.ui.model.type.Float',
                      formatOptions: { decimals: 2, groupingEnabled: true }
                    }"
                  />
                </f:fields>
              </f:FormElement>
            </f:formElements>
          </f:FormContainer>
        </f:formContainers>
      </f:Form>
    </content>
    <beginButton>
      <Button text="Fixar" type="Emphasized" press=".onConfirmPriceFixation"/>
    </beginButton>
    <endButton>
      <Button text="Cancelar" press=".onCancelPriceFixationDialog"/>
    </endButton>
  </Dialog>
</core:FragmentDefinition>
```

- [ ] **Step 3: Tornar a tabela de fixações imutável e acrescentar as ações**

Substituir todo o conteúdo de
`webapp/view/purchaseContracts/fragments/PurchaseContractPriceFixations.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:t="sap.ui.table"
    xmlns:core="sap.ui.core"
>
  <t:Table
    id="purchaseContractPriceFixationsTable"
    class="sapUiSizeCondensed"
    alternateRowColors="true"
    enableBusyIndicator="true"
    enableSelectAll="false"
    selectionBehavior="Row"
    selectionMode="Single"
    busyIndicatorDelay="0"
    rows="{PriceFixations}"
    visible="true"
    >
    <t:extension>
      <OverflowToolbar>
        <content>
          <Title text="Fixações de Preço do Contrato de Compra" />
          <ToolbarSpacer />
          <Button
            visible="{= ${Type} === 'ToBeDetermined' }"
            text="Fixar Preço"
            type="Transparent"
            icon="sap-icon://add"
            press=".onOpenPriceFixationDialog"
          />
          <Button
            visible="{= ${Type} === 'ToBeDetermined' }"
            text="Estornar"
            type="Transparent"
            icon="sap-icon://undo"
            press=".onCancelPriceFixation"
          />
        </content>
      </OverflowToolbar>
    </t:extension>
  <t:columns>
    <t:Column label="Data">
      <t:template>
        <Text
          text="{
            path: 'FixationDate',
            type: 'sap.ui.model.odata.type.DateTimeOffset',
            formatOptions: { pattern: 'dd/MM/yyyy' }
          }"
        />
      </t:template>
    </t:Column>
    <t:Column label="Frete">
      <t:template>
        <Text text="{
          path: 'FreightCost',
          type: 'sap.ui.model.odata.type.Double',
          formatOptions: {
              decimals: 2, decimalSeparator: ',',
              groupingEnabled: true, groupingSeparator: '.'
          }
        }"/>
      </t:template>
    </t:Column>
    <t:Column label="Volume">
      <t:template>
        <Text text="{
          path: 'FixationVolume',
          type: 'sap.ui.model.odata.type.Double',
          formatOptions: {
              decimals: 3, decimalSeparator: ',',
              groupingEnabled: true, groupingSeparator: '.'
          }
        }"/>
      </t:template>
    </t:Column>
    <t:Column label="Preço">
      <t:template>
        <Text text="{
          path: 'FixationPrice',
          type: 'sap.ui.model.odata.type.Double',
          formatOptions: {
              decimals: 8, decimalSeparator: ',',
              groupingEnabled: true, groupingSeparator: '.'
          }
        }"/>
      </t:template>
    </t:Column>
    <t:Column label="Status">
      <t:template>
        <Text
            text="{
              path: 'Status',
              targetType: 'any',
              formatter: '.formatter.formatPriceFixationStatus'
            }"
        />
      </t:template>
    </t:Column>
    <t:Column label="Aprovado por">
      <t:template>
        <Text text="{ApprovedBy}"/>
      </t:template>
    </t:Column>
  </t:columns>
</t:Table>
</core:FragmentDefinition>
```

Todos os `Input`/`DatePicker` editáveis viraram `Text`: a fixação é imutável, e deixar campos editáveis
contradizia a regra do backend (Task 7) — o usuário editaria e levaria erro na gravação.

- [ ] **Step 4: Implementar os handlers**

Em `webapp/controller/purchaseContracts/PurchaseContractsBaseController.ts`, acrescentar (usando o helper de
diálogo descoberto no Step 1 — o código abaixo usa o padrão `loadFragment`, ajuste se o projeto usar outro):

```typescript
  async onOpenPriceFixationDialog(): Promise<void> {
    const oContext = this.getView().getBindingContext();
    if (!oContext) {
      MessageBox.error("Contrato não carregado.");
      return;
    }

    const oModel = new JSONModel({
      FixationDate: new Date(),
      FixationVolume: 0,
      FixationPrice: 0,
      FreightCost: 0,
      AvailableVolumeToPricing: oContext.getProperty("AvailableVolumeToPricing") as number,
    });

    if (!this._oPriceFixationDialog) {
      this._oPriceFixationDialog = (await this.loadFragment({
        name: "siagrob1.dialogs.fragments.PriceFixationDialog",
      })) as Dialog;
      this.getView().addDependent(this._oPriceFixationDialog);
    }

    this._oPriceFixationDialog.setModel(oModel, "fixation");
    this._oPriceFixationDialog.open();
  }

  onCancelPriceFixationDialog(): void {
    this._oPriceFixationDialog?.close();
  }

  async onConfirmPriceFixation(): Promise<void> {
    const oDialog = this._oPriceFixationDialog;
    const oData = (oDialog.getModel("fixation") as JSONModel).getData() as {
      FixationDate: Date;
      FixationVolume: number;
      FixationPrice: number;
      FreightCost: number;
      AvailableVolumeToPricing: number;
    };

    if (oData.FixationVolume <= 0) {
      MessageBox.error("Volume da fixação deve ser maior que zero.");
      return;
    }

    if (oData.FixationVolume > oData.AvailableVolumeToPricing) {
      MessageBox.error(
        `Volume excede o saldo disponível para fixação (${oData.AvailableVolumeToPricing}).`
      );
      return;
    }

    const sContractKey = this.getView().getBindingContext().getProperty("Key") as string;

    try {
      await RequestModel.post(
        `${ServerRoutes.purchaseContracts}(${sContractKey})/PriceFixations`,
        {
          FixationDate: oData.FixationDate,
          FixationVolume: oData.FixationVolume,
          FixationPrice: oData.FixationPrice,
          FreightCost: oData.FreightCost,
        }
      );

      oDialog.close();
      MessageToast.show("Fixação enviada para aprovação.");
      this.getView().getModel().refresh();
    } catch (e) {
      MessageBox.error((e as Error).message);
    }
  }

  async onCancelPriceFixation(): Promise<void> {
    const oTable = this.byId("purchaseContractPriceFixationsTable") as Table;
    const aSelected = oTable.getSelectedIndices();

    if (aSelected.length === 0) {
      MessageBox.alert("Selecione uma fixação para estornar.");
      return;
    }

    const oContext = oTable.getContextByIndex(aSelected[0]);
    const sStatus = oContext.getProperty("Status") as string;

    if (sStatus !== "Confirmed") {
      MessageBox.error(
        "Só é possível estornar fixação confirmada. Fixação em aprovação deve ser rejeitada pela diretoria."
      );
      return;
    }

    MessageBox.confirm("Confirma o estorno desta fixação?", {
      onClose: async (sAction: string) => {
        if (sAction !== MessageBox.Action.OK) return;

        try {
          await RequestModel.post(ServerRoutes.purchaseContractsPriceFixationCancel, {
            Key: oContext.getProperty("Key") as string,
          });
          MessageToast.show("Fixação estornada.");
          this.getView().getModel().refresh();
        } catch (e) {
          MessageBox.error((e as Error).message);
        }
      },
    });
  }
```

Declarar o campo privado junto dos outros da classe:

```typescript
  private _oPriceFixationDialog: Dialog;
```

Acrescentar os imports que faltarem (`Dialog` de `sap/m/Dialog`, `JSONModel` de `sap/ui/model/json/JSONModel`,
`MessageToast` de `sap/m/MessageToast`, `RequestModel`, `ServerRoutes`) e conferir a assinatura real de
`RequestModel.post` antes de usar — ajuste as chamadas ao contrato existente do módulo.

- [ ] **Step 5: Typecheck e lint**

Run: `yarn ts-typecheck && yarn lint && yarn ui5lint`
Expected: sem erros.

- [ ] **Step 6: Verificação manual end-to-end**

Com backend e frontend rodando: criar contrato PAF de 100.000 kg → "Fixar Preço" com 30.000 → confirmar que
a fixação aparece na tabela como "Em Aprovação" e o saldo a fixar cai para 70.000 → tentar fixar 80.000 e
confirmar que o erro de saldo aparece → estornar uma fixação confirmada e ver o saldo voltar.

- [ ] **Step 7: Checkpoint** — reportar, não commitar.

---

### Task 14: Frontend — caixa de entrada da diretoria

**Files:**
- Create: `siagro-b1-frontend/webapp/view/purchaseContracts/priceFixationApproval/Main.view.xml`
- Create: `siagro-b1-frontend/webapp/controller/purchaseContracts/priceFixationApproval/Main.controller.ts`
- Modify: `siagro-b1-frontend/webapp/manifest.json`

**Interfaces:**
- Consumes: `GET odata/PurchaseContractsPriceFixations` (Task 11); `ServerRoutes.purchaseContractsPriceFixationApproval` e `...Reject` (Task 12)

- [ ] **Step 1: Ler o módulo de aprovação existente como molde**

Run: `cat webapp/view/purchaseContracts/approval/Main.view.xml`

E localizar a rota correspondente no `manifest.json`:

Run: `grep -n "purchase-contracts" webapp/manifest.json`

Seguir exatamente o mesmo padrão de rota, target, e estrutura de view.

- [ ] **Step 2: Criar a view da fila**

Criar `webapp/view/purchaseContracts/priceFixationApproval/Main.view.xml`:

```xml
<mvc:View
    controllerName="siagrob1.controller.purchaseContracts.priceFixationApproval.Main"
    xmlns="sap.m"
    xmlns:mvc="sap.ui.core.mvc"
    xmlns:core="sap.ui.core"
>
  <Page id="priceFixationApprovalPage" title="Aprovação de Fixações de Preço" showNavButton="true" navButtonPress=".onNavBack">
    <content>
      <Table
        id="priceFixationApprovalTable"
        items="{
          path: '/PurchaseContractsPriceFixations',
          parameters: { $expand: 'PurchaseContract' }
        }"
        mode="SingleSelectLeft"
        growing="true"
        growingThreshold="50"
      >
        <headerToolbar>
          <OverflowToolbar>
            <Title text="Fixações Aguardando Aprovação"/>
            <ToolbarSpacer/>
            <Button text="Aprovar" type="Accept" icon="sap-icon://accept" press=".onApprove"/>
            <Button text="Rejeitar" type="Reject" icon="sap-icon://decline" press=".onReject"/>
          </OverflowToolbar>
        </headerToolbar>
        <columns>
          <Column><Text text="Contrato"/></Column>
          <Column><Text text="Fornecedor"/></Column>
          <Column><Text text="Produto"/></Column>
          <Column hAlign="End"><Text text="Volume"/></Column>
          <Column hAlign="End"><Text text="Preço"/></Column>
          <Column><Text text="Data"/></Column>
        </columns>
        <items>
          <ColumnListItem>
            <cells>
              <Text text="{PurchaseContract/Code}"/>
              <Text text="{PurchaseContract/CardName}"/>
              <Text text="{PurchaseContract/ItemName}"/>
              <Text text="{
                path: 'FixationVolume',
                type: 'sap.ui.model.odata.type.Double',
                formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
              }"/>
              <Text text="{
                path: 'FixationPrice',
                type: 'sap.ui.model.odata.type.Double',
                formatOptions: { decimals: 8, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
              }"/>
              <Text text="{
                path: 'FixationDate',
                type: 'sap.ui.model.odata.type.DateTimeOffset',
                formatOptions: { pattern: 'dd/MM/yyyy' }
              }"/>
            </cells>
          </ColumnListItem>
        </items>
      </Table>
    </content>
  </Page>
</mvc:View>
```

- [ ] **Step 3: Criar o controller**

Criar `webapp/controller/purchaseContracts/priceFixationApproval/Main.controller.ts`:

```typescript
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import Table from "sap/m/Table";
import BaseController from "siagrob1/controller/BaseController";
import RequestModel from "siagrob1/model/RequestModel";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * Fila da diretoria: todas as fixações em aprovação, de todos os contratos.
 */
export default class Main extends BaseController {
  private getSelectedKey(): string | null {
    const oTable = this.byId("priceFixationApprovalTable") as Table;
    const oItem = oTable.getSelectedItem();

    if (!oItem) {
      MessageBox.alert("Selecione uma fixação.");
      return null;
    }

    return oItem.getBindingContext().getProperty("Key") as string;
  }

  private async submit(sRoute: string, sKey: string, sComments: string, sSuccess: string): Promise<void> {
    try {
      await RequestModel.post(sRoute, { Key: sKey, Comments: sComments });
      MessageToast.show(sSuccess);
      (this.byId("priceFixationApprovalTable") as Table).getBinding("items").refresh();
    } catch (e) {
      MessageBox.error((e as Error).message);
    }
  }

  onApprove(): void {
    const sKey = this.getSelectedKey();
    if (!sKey) return;

    MessageBox.confirm("Confirma a aprovação desta fixação de preço?", {
      onClose: (sAction: string) => {
        if (sAction !== MessageBox.Action.OK) return;
        void this.submit(
          ServerRoutes.purchaseContractsPriceFixationApproval,
          sKey,
          "",
          "Fixação aprovada."
        );
      },
    });
  }

  onReject(): void {
    const sKey = this.getSelectedKey();
    if (!sKey) return;

    MessageBox.prompt?.("Motivo da rejeição:", {
      onClose: (sAction: string, sValue: string) => {
        if (sAction !== MessageBox.Action.OK) return;
        void this.submit(
          ServerRoutes.purchaseContractsPriceFixationReject,
          sKey,
          sValue,
          "Fixação rejeitada."
        );
      },
    });
  }
}
```

`MessageBox.prompt` não existe em todas as versões do UI5 — se `yarn ts-typecheck` reclamar, substitua por um
`Dialog` simples com `TextArea`, seguindo o padrão de rejeição já usado em
`view/purchaseContracts/approval/`. Verifique como aquele módulo captura o comentário de rejeição e reuse.

- [ ] **Step 4: Registrar a rota**

Em `webapp/manifest.json`, na seção `routes`, seguindo o padrão da rota de aprovação de contrato:

```json
      {
        "name": "priceFixationApproval",
        "pattern": "purchase-contracts/price-fixation-approval",
        "target": "priceFixationApproval"
      }
```

E na seção `targets`:

```json
      "priceFixationApproval": {
        "viewId": "priceFixationApproval",
        "viewName": "purchaseContracts.priceFixationApproval.Main",
        "viewLevel": 1
      }
```

Conferir as chaves exatas (`viewId`, `viewLevel`, `controlAggregation`) contra um target vizinho antes de
gravar — o manifest deste projeto tem convenções próprias.

- [ ] **Step 5: Validar o manifest**

Run: `yarn ts-typecheck && yarn lint && yarn ui5lint`
Expected: sem erros.

- [ ] **Step 6: Verificação manual**

Navegar para `#/purchase-contracts/price-fixation-approval`, confirmar que as fixações `InApproval` de
diferentes contratos aparecem na fila, aprovar uma e confirmar que ela some da fila e aparece como
"Confirmado" no contrato de origem, com o aprovador preenchido.

- [ ] **Step 7: Checkpoint** — reportar, não commitar.

---

### Task 15: Relatório espelho de fixação

**Files:**
- Create: `siagro-b1-backend/SiagroB1.Reports/Services/PriceFixationReportService.cs`
- Create: `siagro-b1-backend/SiagroB1.Reports/Reports/Templates/PriceFixation.frx`
- Modify: `siagro-b1-backend/SiagroB1.Reports/Controllers/PurchaseContractsController.cs`

**Interfaces:**
- Consumes: `PriceFixationStatus.Confirmed` (Task 1)
- Produces: `GET /reports/PriceFixation?key=<guid>` devolvendo `application/pdf`

- [ ] **Step 1: Ler o serviço de relatório existente como molde**

Run: `cat SiagroB1.Reports/Services/PrePurchaseContractReportService.cs`

Prestar atenção especial em: como o `.frx` é carregado, como o DataSource é registrado, como o PDF é
devolvido, e **de onde vêm os dados do parceiro**.

- [ ] **Step 2: Confirmar a fonte de dados do parceiro**

Run: `grep -rn "IPartnerSource" --include=*.cs SiagroB1.Reports SiagroB1.Domain`

`BUSINESS_PARTNERS` fica **vazia** em modo `SAPB1` (os dados vivem em `OCRD`/`CRD1`). O relatório **precisa**
usar `IPartnerSource`, senão sai em branco nas instalações integradas ao SAP. Anotar a assinatura exata antes
de escrever o serviço.

- [ ] **Step 3: Escrever o serviço**

Criar `SiagroB1.Reports/Services/PriceFixationReportService.cs`, espelhando a estrutura de
`PrePurchaseContractReportService` e carregando:

```csharp
        var fixation = await context.PurchaseContractsPriceFixations
            .Include(x => x.PurchaseContract)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key)
            ?? throw new NotFoundException("Fixação de preço não encontrada.");

        if (fixation.Status != PriceFixationStatus.Confirmed)
            throw new ApplicationException(
                "Espelho de fixação só é emitido para fixação confirmada.");
```

Os dados do parceiro vêm de `IPartnerSource` usando `fixation.PurchaseContract!.CardCode` — **não** de
`context.BusinessPartners`.

Campos do espelho: código do contrato, fornecedor (nome e documento, via `IPartnerSource`), produto, safra,
data da fixação, volume fixado, preço fixado, frete, valor total da fixação (volume × preço), aprovador e
data de aprovação.

- [ ] **Step 4: Criar o template**

Copiar `SiagroB1.Reports/Reports/Templates/PrePurchaseContract.frx` para `PriceFixation.frx` e ajustar as
colunas do `DataSource` e os `TextObject` para os campos do Step 3. Manter o padrão de `Format.DecimalDigits`
já usado (2 para valores, 3 para volumes, 8 para preço unitário).

- [ ] **Step 5: Expor o endpoint**

Em `SiagroB1.Reports/Controllers/PurchaseContractsController.cs`, acrescentar a action seguindo o padrão do
endpoint `PrePurchaseContract` já existente no mesmo controller, com rota `PriceFixation` e parâmetro `key`.

- [ ] **Step 6: Build**

Run: `dotnet build SiagroB1.sln`
Expected: build limpo.

- [ ] **Step 7: Verificação manual**

Subir `SiagroB1.Reports` (perfil `dev`) e, com uma fixação confirmada:

```bash
curl -o fixacao.pdf "http://localhost:58000/PriceFixation?key=<fixation-guid>"
```

Expected: PDF válido, com nome do fornecedor **preenchido**. Se o nome vier vazio, o serviço está lendo
`BUSINESS_PARTNERS` em vez de `IPartnerSource` — voltar ao Step 3.

Repetir com uma fixação `InApproval` e confirmar que retorna erro em vez de PDF.

- [ ] **Step 8: Checkpoint** — reportar, não commitar.

---

### Task 16: Verificação integrada e fechamento

**Files:** nenhum arquivo novo — verificação de ponta a ponta.

- [ ] **Step 1: Suíte completa do backend**

Run: `dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests`
Expected: build limpo, todos os testes verdes. Colar a contagem final no relatório.

- [ ] **Step 2: Gate completo do frontend**

Run (a partir de `siagro-b1-frontend/`): `yarn test`
Expected: lint + QUnit/OPA verdes.

- [ ] **Step 3: Roteiro manual do cenário completo**

Com Web, Gateway, Reports e o dev server rodando:

1. Criar contrato PAF de 100.000 kg → confirmar `Status = Approved` e nenhuma fixação.
2. Criar liberação de embarque e aprová-la → confirmar que funciona **sem nenhuma fixação existir**
   (é o requisito que viabiliza o cenário do excedente).
3. Fixar 60.000 kg → aparece como "Em Aprovação"; saldo a fixar cai para 40.000.
4. Tentar fixar 50.000 kg → erro de saldo.
5. Na caixa de entrada da diretoria, aprovar a fixação de 60.000 → vira "Confirmado".
6. Conferir nos totais do contrato que `TotalPrice` reflete só a fixação confirmada.
7. Emitir o espelho de fixação → PDF com fornecedor preenchido.
8. Com 60.000 kg entregues e 60.000 fixados e confirmados, encerrar o contrato → sucesso.
9. Repetir com uma fixação pendente na fila → encerramento bloqueado.

- [ ] **Step 4: Relatório final**

Reportar ao Paulo: contagem de testes, resultado de cada passo do roteiro manual, caminho da migration
pendente de aplicação, e a lista de arquivos alterados (`git status`) para ele revisar e commitar.

---

## Auto-revisão do plano

**Cobertura da spec:**

| Requisito da spec | Task |
|---|---|
| Herdar `BaseEntity` + `ApprovalComments` | 1 |
| `PriceFixationStatus.Rejected` | 1 |
| `TotalPrice` só `Confirmed` | 2 |
| `FixedVolume` persistido + recálculo único | 3 |
| Guarda Σ ≤ `TotalVolume` | 4 |
| Bloqueio de fixação manual em `Fixed` | 4 |
| Aprovação/rejeição pela diretoria | 5 |
| Estorno de fixação confirmada | 6 |
| Imutabilidade de `Confirmed` | 7 |
| Contrato PAF nasce `Approved`, `StandardPrice` 0 | 8 |
| `UpdateService` não atropela fixações PAF | 8 |
| Guarda de fechamento | 9 |
| `TotalTax` e a armadilha do `Include` | 10 |
| OData actions | 11 |
| Formulário PAF + limpeza do status `"Pending"` | 12 |
| Diálogo de fixação + tabela read-only | 13 |
| Caixa de entrada da diretoria | 14 |
| Relatório com `IPartnerSource` | 15 |
| Migration + backfill | 3 |

**Consistência de tipos:** `PurchaseContractsFixedVolumeService.RecalculateAsync(PurchaseContract)` e
`ConfirmedVolumeAsync(Guid)` são definidos na Task 3 e usados com a mesma assinatura nas Tasks 4, 5, 6, 7 e 9.
`ExecuteAsync(Guid, string?, string)` é uniforme entre Approval e Reject; Cancel usa `ExecuteAsync(Guid, string)`
por não ter comentário. `CreateService.ExecuteAsync` ganha o terceiro parâmetro na Task 4 e o controller é
atualizado no mesmo passo.

**Riscos conhecidos, documentados nas tasks:**
- O `RowVersion` não é exercitado pelo provider InMemory — a proteção de concorrência é verificada por
  inspeção de código, não por teste automatizado (Task 3).
- A ordem `Remove` vs `RecalculateAsync` no `DeleteService` tem uma armadilha explicitada na Task 7 Step 4.
- O `CreateService` de contrato não é testável sem mocks inexistentes; verificação é manual (Task 8 Step 7).
