# Locais de Entrega (1:N) no Contrato de Venda — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir cadastrar vários locais de entrega (clientes) por contrato de venda (relação 1:N), espelhando o stack de `PurchaseContractBroker`.

**Architecture:** Nova entidade-filha `SalesContractDeliveryLocation` (tabela `SALES_CONTRACTS_DELIVERY_LOCATIONS`) com `CardCode`/`CardName` (cliente `CardType='C'`), coleção de navegação `SalesContract.DeliveryLocations`, 4 serviços + controller-filho com rotas OData aninhadas, gravação por deep insert no Add e POST/DELETE por linha no Edit. Frontend: tabela editável num fragmento embutido em Add/Edit/Detail, reutilizando o value help de clientes existente.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server), ASP.NET Core OData v4, xUnit + EF InMemory (`SiagroB1.Application.Tests`); OpenUI5 1.141 + TypeScript.

## Global Constraints

- **Sem propriedade de navegação para `BusinessPartner`** em nenhuma entidade — em modo SAPB1 a tabela local `BUSINESS_PARTNERS` está vazia e um INNER JOIN zeraria a coleção; o nome é sempre desnormalizado na gravação via `IBusinessPartnerService.GetByIdAsync`.
- **Coleção opcional** (zero ou mais locais); editável **apenas com o contrato em `Draft`** (regra já existente do contrato).
- **Sem cota/volume por local** — só `CardCode` + `CardName`.
- **Cliente duplicado no mesmo contrato é bloqueado.**
- **Serviços registrados à mão** em `AddApplicationServices()` (não há assembly scanning).
- **Migrations**: gerar via `dotnet ef migrations add` com `ASPNETCORE_ENVIRONMENT` explícito; conferir `Up`/`Down` + snapshot; aplicar só no ambiente `Yokotobi`.
- **Commit por task AUTORIZADO para esta feature** (exceção pontual à regra de commits manuais, confirmada pelo usuário) — ao fim de cada task, stagear APENAS os arquivos nomeados com `git add <path>` explícito (nunca `git add -A`/`.`), commit com mensagem simples; não tocar no outro repo.
- Colunas: `CardCode` = `VARCHAR(10) NOT NULL`; `CardName` = `VARCHAR(200)`.

---

### Task 1: Entidade-filha + navegação + DbSet

**Files:**
- Create: `SiagroB1.Domain/Entities/SalesContractDeliveryLocation.cs`
- Modify: `SiagroB1.Domain/Entities/SalesContract.cs` (adicionar coleção de navegação, junto às demais `ICollection` ~linha 106-114)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (adicionar `DbSet`, junto aos demais DbSets de sales ~linha 39-40)
- Test: `SiagroB1.Application.Tests/Infra/SalesContractDeliveryLocationModelTests.cs`

**Interfaces:**
- Produces: `SalesContractDeliveryLocation { Guid? Key; Guid? SalesContractKey; SalesContract? SalesContract; string CardCode; string? CardName }`; `SalesContract.DeliveryLocations` (`ICollection<SalesContractDeliveryLocation>`); `AppDbContext.SalesContractsDeliveryLocations` (`DbSet<SalesContractDeliveryLocation>`).

- [ ] **Step 1: Escrever o teste que falha**

```csharp
// SiagroB1.Application.Tests/Infra/SalesContractDeliveryLocationModelTests.cs
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class SalesContractDeliveryLocationModelTests
{
    [Fact]
    public void SalesContractDeliveryLocation_IsMappedToExpectedTableAndColumns()
    {
        // Usa o provider SqlServer só para materializar o modelo relacional; sem conexão.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;

        using var context = new AppDbContext(options);

        var entityType = context.Model.FindEntityType(typeof(SalesContractDeliveryLocation));
        Assert.NotNull(entityType);
        Assert.Equal("SALES_CONTRACTS_DELIVERY_LOCATIONS", entityType!.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains("Key", props);
        Assert.Contains("SalesContractKey", props);
        Assert.Contains("CardCode", props);
        Assert.Contains("CardName", props);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar (não compila: tipo inexistente)**

Run: `dotnet build SiagroB1.Application.Tests`
Expected: FAIL — `The type or namespace name 'SalesContractDeliveryLocation' could not be found`.

- [ ] **Step 3: Criar a entidade**

```csharp
// SiagroB1.Domain/Entities/SalesContractDeliveryLocation.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Um local de entrega do contrato de VENDA: aponta para o cadastro de clientes
/// (<see cref="BusinessPartner"/> com CardType 'C'). Relação 1:N com o contrato —
/// um contrato pode entregar em vários terminais/portos conforme a cota disponível.
/// </summary>
[Table("SALES_CONTRACTS_DELIVERY_LOCATIONS")]
public class SalesContractDeliveryLocation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    /// <summary>
    /// SAP ENTITY (cliente). Sem propriedade de navegação para BusinessPartner:
    /// em modo SAPB1 a tabela local BUSINESS_PARTNERS está vazia — o INNER JOIN
    /// zeraria a coleção. O nome é desnormalizado na gravação.
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }
}
```

- [ ] **Step 4: Adicionar a coleção de navegação em `SalesContract`**

Em `SiagroB1.Domain/Entities/SalesContract.cs`, junto às demais coleções (após `Attachments`, ~linha 114):

```csharp
    public ICollection<SalesContractDeliveryLocation> DeliveryLocations { get; set; } = [];
```

- [ ] **Step 5: Adicionar o `DbSet` em `AppDbContext`**

Em `SiagroB1.Infra/Context/AppDbContext.cs`, junto aos DbSets de sales (~linha 40):

```csharp
    public DbSet<SalesContractDeliveryLocation> SalesContractsDeliveryLocations { get; set; }
```

- [ ] **Step 6: Rodar o teste e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter SalesContractDeliveryLocationModelTests`
Expected: PASS.

- [ ] **Step 7: Commit** (manual pelo usuário)

```
git add SiagroB1.Domain/Entities/SalesContractDeliveryLocation.cs SiagroB1.Domain/Entities/SalesContract.cs SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Application.Tests/Infra/SalesContractDeliveryLocationModelTests.cs
git commit -m "feat: add SalesContractDeliveryLocation entity (1:N delivery locations)"
```

---

### Task 2: Serviços CRUD do filho + guarda de duplicidade

**Files:**
- Create: `SiagroB1.Application/Services/SalesContracts/SalesContractsDeliveryLocationsCreateService.cs`
- Create: `SiagroB1.Application/Services/SalesContracts/SalesContractsDeliveryLocationsUpdateService.cs`
- Create: `SiagroB1.Application/Services/SalesContracts/SalesContractsDeliveryLocationsDeleteService.cs`
- Create: `SiagroB1.Application/Services/SalesContracts/SalesContractsDeliveryLocationsGetService.cs`
- Test: `SiagroB1.Application.Tests/SalesContracts/SalesContractsDeliveryLocationsCreateServiceTests.cs`

**Interfaces:**
- Consumes: `AppDbContext.SalesContractsDeliveryLocations`, `IBusinessPartnerService.GetByIdAsync(string)`, `DefaultException`, `NotFoundException` (ambos em `SiagroB1.Domain.Exceptions`, ambos `: Exception`).
- Produces:
  - `SalesContractsDeliveryLocationsCreateService.ExecuteAsync(Guid salesContractKey, SalesContractDeliveryLocation entity) : Task<SalesContractDeliveryLocation>`
  - `SalesContractsDeliveryLocationsUpdateService.ExecuteAsync(Guid parentKey, Guid associationKey, SalesContractDeliveryLocation entity)` **e** `ExecuteAsync(Guid associationKey, SalesContractDeliveryLocation entity)`
  - `SalesContractsDeliveryLocationsDeleteService.ExecuteAsync(Guid associationKey) : Task<bool>` **e** `ExecuteAsync(Guid parentKey, Guid associationKey)`
  - `SalesContractsDeliveryLocationsGetService.GetByIdAsync(Guid)` / `GetByIdAsync(Guid key, Guid associationKey)` / `QueryAll(Guid parentKey) : IQueryable<...>`

- [ ] **Step 1: Escrever os testes que falham**

```csharp
// SiagroB1.Application.Tests/SalesContracts/SalesContractsDeliveryLocationsCreateServiceTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractsDeliveryLocationsCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeBusinessPartnerService _partners =
        new(new() { ["C0001"] = "Terminal Santos", ["C0002"] = "Terminal Paranagua" });

    private SalesContractsDeliveryLocationsCreateService Service() =>
        new(_db.Context, _partners,
            NullLogger<SalesContractsDeliveryLocationsCreateService>.Instance);

    private async Task<SalesContract> SeedContractAsync()
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "SC-001",
            CardCode = "C9999",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            Status = ContractStatus.Draft,
        };
        _db.Context.SalesContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task Create_ResolvesCardName_AndLinksToContract()
    {
        var contract = await SeedContractAsync();

        var created = await Service().ExecuteAsync(contract.Key!.Value,
            new SalesContractDeliveryLocation { CardCode = "C0001" });

        Assert.Equal("Terminal Santos", created.CardName);
        Assert.Equal(contract.Key, created.SalesContractKey);
    }

    [Fact]
    public async Task Create_DuplicateCardCodeInSameContract_ThrowsDefaultException()
    {
        var contract = await SeedContractAsync();
        await Service().ExecuteAsync(contract.Key!.Value,
            new SalesContractDeliveryLocation { CardCode = "C0001" });

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service().ExecuteAsync(contract.Key!.Value,
                new SalesContractDeliveryLocation { CardCode = "C0001" }));
    }

    [Fact]
    public async Task Create_ContractNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service().ExecuteAsync(Guid.NewGuid(),
                new SalesContractDeliveryLocation { CardCode = "C0001" }));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet build SiagroB1.Application.Tests`
Expected: FAIL — `SalesContractsDeliveryLocationsCreateService` não existe.

- [ ] **Step 3: Implementar o Create service (com guarda de duplicidade)**

```csharp
// SiagroB1.Application/Services/SalesContracts/SalesContractsDeliveryLocationsCreateService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsDeliveryLocationsCreateService(
    AppDbContext context,
    IBusinessPartnerService businessPartnerService,
    ILogger<SalesContractsDeliveryLocationsCreateService> logger)
{
    public async Task<SalesContractDeliveryLocation> ExecuteAsync(
        Guid salesContractKey, SalesContractDeliveryLocation associationEntity)
    {
        try
        {
            var contract = await context.SalesContracts.FindAsync(salesContractKey)
                ?? throw new NotFoundException("Sales contract not found");

            var duplicate = await context.SalesContractsDeliveryLocations
                .AnyAsync(x => x.SalesContractKey == salesContractKey
                               && x.CardCode == associationEntity.CardCode);
            if (duplicate)
                throw new DefaultException("Este local de entrega ja foi informado no contrato.");

            associationEntity.SalesContract = contract;
            associationEntity.CardName =
                (await businessPartnerService.GetByIdAsync(associationEntity.CardCode))?.CardName;

            await context.AddAsync(associationEntity);
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

- [ ] **Step 4: Implementar Update, Delete e Get (espelho dos de Broker)**

```csharp
// SalesContractsDeliveryLocationsUpdateService.cs
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsDeliveryLocationsUpdateService(
    AppDbContext context,
    IBusinessPartnerService businessPartnerService,
    ILogger<SalesContractsDeliveryLocationsUpdateService> logger)
{
    public Task<SalesContractDeliveryLocation?> ExecuteAsync(
        Guid associationKey, SalesContractDeliveryLocation associationEntity) =>
        UpdateAsync(associationKey, associationEntity);

    public async Task<SalesContractDeliveryLocation?> ExecuteAsync(
        Guid parentKey, Guid associationKey, SalesContractDeliveryLocation associationEntity)
    {
        if (!context.SalesContracts.Any(x => x.Key == parentKey))
            throw new NotFoundException("Sales contract not found");
        return await UpdateAsync(associationKey, associationEntity);
    }

    private async Task<SalesContractDeliveryLocation?> UpdateAsync(
        Guid associationKey, SalesContractDeliveryLocation associationEntity)
    {
        try
        {
            var existingEntity = await context.SalesContractsDeliveryLocations.FindAsync(associationKey)
                ?? throw new NotFoundException("Delivery location not found");

            context.Entry(existingEntity).CurrentValues.SetValues(associationEntity);
            // Depois do SetValues e em `existingEntity`, senao a gravacao se perde.
            existingEntity.CardName =
                (await businessPartnerService.GetByIdAsync(associationEntity.CardCode))?.CardName;

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

```csharp
// SalesContractsDeliveryLocationsDeleteService.cs
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsDeliveryLocationsDeleteService(
    AppDbContext context, ILogger<SalesContractsDeliveryLocationsDeleteService> logger)
{
    public Task<bool> ExecuteAsync(Guid associationKey) => Delete(associationKey);

    public async Task<bool> ExecuteAsync(Guid parentKey, Guid associationKey)
    {
        if (!context.SalesContracts.Any(x => x.Key == parentKey))
            throw new NotFoundException("Sales contract not found");
        return await Delete(associationKey);
    }

    private async Task<bool> Delete(Guid associationKey)
    {
        try
        {
            var existingEntity = await context.SalesContractsDeliveryLocations.FindAsync(associationKey)
                ?? throw new NotFoundException("Delivery location not found");

            context.SalesContractsDeliveryLocations.Remove(existingEntity);
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }
}
```

```csharp
// SalesContractsDeliveryLocationsGetService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsDeliveryLocationsGetService(
    AppDbContext context, ILogger<SalesContractsDeliveryLocationsGetService> logger)
{
    public async Task<SalesContractDeliveryLocation?> GetByIdAsync(Guid associationKey)
    {
        try
        {
            return await context.SalesContractsDeliveryLocations.FindAsync(associationKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", associationKey);
            throw new DefaultException("Error fetching entity");
        }
    }

    public async Task<SalesContractDeliveryLocation?> GetByIdAsync(Guid key, Guid associationKey)
    {
        try
        {
            if (!context.SalesContracts.Any(x => x.Key == key))
                throw new NotFoundException("Sales contract key not found");
            return await context.SalesContractsDeliveryLocations.FindAsync(associationKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<SalesContractDeliveryLocation> QueryAll(Guid parentKey) =>
        context.SalesContractsDeliveryLocations
            .Where(x => x.SalesContractKey == parentKey)
            .AsNoTracking();
}
```

- [ ] **Step 5: Rodar os testes e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter SalesContractsDeliveryLocationsCreateServiceTests`
Expected: PASS (3 testes).

- [ ] **Step 6: Commit** (manual)

```
git add SiagroB1.Application/Services/SalesContracts/SalesContractsDeliveryLocations*.cs SiagroB1.Application.Tests/SalesContracts/SalesContractsDeliveryLocationsCreateServiceTests.cs
git commit -m "feat: add SalesContractDeliveryLocation CRUD services with duplicate guard"
```

---

### Task 3: Deep insert no `SalesContractsCreateService`

**Files:**
- Modify: `SiagroB1.Application/Services/SalesContracts/SalesContractsCreateService.cs`
- Test: `SiagroB1.Application.Tests/SalesContracts/SalesContractDeliveryLocationDuplicateGuardTests.cs`

**Interfaces:**
- Consumes: `entity.DeliveryLocations`; `IBusinessPartnerService.GetByIdAsync`.
- Produces: `SalesContractsCreateService.HasDuplicateDeliveryLocation(IEnumerable<SalesContractDeliveryLocation>) : bool` (método estático puro).

**Contexto e restrição de harness:** `SalesContractsCreateService` NÃO é testável ponta-a-ponta no harness — o `DocNumberSequenceService` recebe `IDbConnection` e executa SQL (Dapper), o que o provider EF InMemory não fornece; por isso NÃO existe nenhum teste construindo esse serviço hoje. Estratégia: extrair a regra de duplicidade para um **método estático puro** (testável isoladamente, sem construir o serviço) e verificar a resolução de nomes no deep insert pelo **caminho do usuário no browser** (Task 9). Além disso, o `catch` do create mascara toda exceção como "Unable to create sales contract.", então a validação de duplicidade fica **fora do `try`** (como a validação de preço já faz, ~linha 24-25).

- [ ] **Step 1: Escrever o teste que falha (guarda pura)**

```csharp
// SiagroB1.Application.Tests/SalesContracts/SalesContractDeliveryLocationDuplicateGuardTests.cs
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.SalesContracts;

public class SalesContractDeliveryLocationDuplicateGuardTests
{
    private static SalesContractDeliveryLocation Loc(string card) => new() { CardCode = card };

    [Fact]
    public void HasDuplicate_WithRepeatedCardCode_ReturnsTrue()
    {
        var locations = new[] { Loc("C0001"), Loc("C0002"), Loc("C0001") };
        Assert.True(SalesContractsCreateService.HasDuplicateDeliveryLocation(locations));
    }

    [Fact]
    public void HasDuplicate_WithDistinctCardCodes_ReturnsFalse()
    {
        var locations = new[] { Loc("C0001"), Loc("C0002") };
        Assert.False(SalesContractsCreateService.HasDuplicateDeliveryLocation(locations));
    }

    [Fact]
    public void HasDuplicate_WithEmptyCollection_ReturnsFalse()
    {
        Assert.False(SalesContractsCreateService.HasDuplicateDeliveryLocation([]));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet build SiagroB1.Application.Tests`
Expected: FAIL — `HasDuplicateDeliveryLocation` não existe.

- [ ] **Step 3: Implementar o método estático puro**

Em `SalesContractsCreateService` (mesma classe), adicionar:

```csharp
    /// <summary>
    /// True se dois locais de entrega apontarem para o mesmo cliente (CardCode).
    /// Puro/estático de propósito: é a única parte da criação testável em unidade
    /// (o restante depende de DocNumberSequenceService, que exige IDbConnection real).
    /// </summary>
    public static bool HasDuplicateDeliveryLocation(IEnumerable<SalesContractDeliveryLocation> locations) =>
        locations.GroupBy(l => l.CardCode).Any(g => g.Count() > 1);
```

- [ ] **Step 4: Rodar o teste e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter SalesContractDeliveryLocationDuplicateGuardTests`
Expected: PASS (3 testes).

- [ ] **Step 5: Chamar a guarda FORA do try (antes do `try`, junto à validação de preço ~linha 25)**

```csharp
        // Duplicidade de local de entrega barrada ANTES do try — o catch abaixo
        // mascara toda excecao como "Unable to create sales contract.".
        if (HasDuplicateDeliveryLocation(entity.DeliveryLocations))
            throw new ApplicationException("Local de entrega repetido no contrato.");
```

- [ ] **Step 6: Resolver back-ref + CardName DENTRO do try**

Dentro do `try`, junto ao bloco que resolve `CardName` do contrato (após ~linha 38, antes de `AddAsync`):

```csharp
            foreach (var location in entity.DeliveryLocations)
            {
                location.SalesContract = entity;
                location.CardName =
                    (await businessPartnerService.GetByIdAsync(location.CardCode))?.CardName;
            }
```

- [ ] **Step 7: Build da solução**

Run: `dotnet build SiagroB1.sln`
Expected: 0 erros. (A resolução de nomes no deep insert é verificada no browser — Task 9.)

- [ ] **Step 8: Commit** (manual)

```
git add SiagroB1.Application/Services/SalesContracts/SalesContractsCreateService.cs SiagroB1.Application.Tests/SalesContracts/SalesContractDeliveryLocationDuplicateGuardTests.cs
git commit -m "feat: guard duplicate delivery locations and resolve names on deep insert"
```

---

### Task 4: OData EDM + controller-filho + DI

**Files:**
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (junto aos EntitySets de sales, ~linha 62-64)
- Create: `SiagroB1.Web/Controllers/SalesContractsDeliveryLocationsController.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (`AddApplicationServices()`)

**Interfaces:**
- Consumes: os 4 serviços da Task 2; entity set `SalesContractsDeliveryLocations`; nav property `SalesContracts({key})/DeliveryLocations`.

- [ ] **Step 1: Registrar o EntitySet no EDM**

Em `ODataConfigurations.cs`, junto aos demais sets de sales (~linha 62):

```csharp
        modelBuilder.EntitySet<SalesContractDeliveryLocation>("SalesContractsDeliveryLocations");
```

- [ ] **Step 2: Criar o controller-filho (espelho de `PurchaseContractsBrokersController`)**

```csharp
// SiagroB1.Web/Controllers/SalesContractsDeliveryLocationsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class SalesContractsDeliveryLocationsController(
    SalesContractsDeliveryLocationsCreateService createService,
    SalesContractsDeliveryLocationsUpdateService updateService,
    SalesContractsDeliveryLocationsDeleteService deleteService,
    SalesContractsDeliveryLocationsGetService getService)
    : ODataController
{
    [HttpPost("odata/SalesContracts({key:guid})/DeliveryLocations")]
    [HttpPost("odata/SalesContracts/{key:guid}/DeliveryLocations")]
    public async Task<ActionResult<SalesContractDeliveryLocation>> PostAsync(
        [FromRoute] Guid key, [FromBody] SalesContractDeliveryLocation associationEntity)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            await createService.ExecuteAsync(key, associationEntity);
            return Created(associationEntity);
        }
        catch (Exception ex)
        {
            if (ex is DefaultException) return BadRequest(ex.Message);
            return StatusCode(500, ex.Message);
        }
    }

    [HttpPut("odata/SalesContracts({parentKey:guid})/DeliveryLocations({associationKey:guid})")]
    [HttpPut("odata/SalesContracts/{parentKey:guid}/DeliveryLocations/{associationKey:guid}")]
    public async Task<IActionResult> PutAsync(
        [FromRoute] Guid parentKey, [FromRoute] Guid associationKey,
        [FromBody] SalesContractDeliveryLocation associationEntity)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            await updateService.ExecuteAsync(parentKey, associationKey, associationEntity);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex)
        {
            if (ex is DefaultException) return BadRequest(ex.Message);
            return StatusCode(500, ex.Message);
        }
        return NoContent();
    }

    [HttpDelete("odata/SalesContractsDeliveryLocations({associationKey:guid})")]
    [HttpDelete("odata/SalesContractsDeliveryLocations/{associationKey:guid}")]
    [HttpDelete("odata/SalesContracts({parentKey:guid})/DeliveryLocations({associationKey:guid})")]
    [HttpDelete("odata/SalesContracts/{parentKey:guid}/DeliveryLocations/{associationKey:guid}")]
    public async Task<IActionResult> DeleteAsync([FromRoute] Guid associationKey)
    {
        try
        {
            await deleteService.ExecuteAsync(associationKey);
        }
        catch (NotFoundException) { return NotFound(); }
        catch (Exception ex)
        {
            if (ex is DefaultException) return BadRequest(ex.Message);
            return StatusCode(500, ex.Message);
        }
        return NoContent();
    }

    [HttpGet("odata/SalesContracts({key:guid})/DeliveryLocations")]
    [HttpGet("odata/SalesContracts/{key:guid}/DeliveryLocations")]
    [EnableQuery]
    public ActionResult<IEnumerable<SalesContractDeliveryLocation>> GetAsync([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }

    [HttpGet("odata/SalesContracts({key:guid})/DeliveryLocations({associationKey:guid})")]
    [HttpGet("odata/SalesContracts/{key:guid}/DeliveryLocations/{associationKey:guid}")]
    [EnableQuery]
    public async Task<ActionResult<SalesContractDeliveryLocation>> GetAsync(
        [FromRoute] Guid key, [FromRoute] Guid associationKey)
    {
        var item = await getService.GetByIdAsync(key, associationKey);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public virtual async Task<IActionResult> Patch(
        [FromRoute] Guid key, [FromBody] Delta<SalesContractDeliveryLocation> patch)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        SalesContractDeliveryLocation? t = await getService.GetByIdAsync(key);
        if (t == null) return NotFound();

        try
        {
            patch.Patch(t);
            await updateService.ExecuteAsync(key, t);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex)
        {
            if (ex is DefaultException) return BadRequest(ex.Message);
            return StatusCode(500, ex.Message);
        }
        return NoContent();
    }
}
```

- [ ] **Step 3: Registrar os 4 serviços na DI**

Em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, dentro de `AddApplicationServices()` (junto aos registros de brokers/sales):

```csharp
        services.AddScoped<SalesContractsDeliveryLocationsCreateService>();
        services.AddScoped<SalesContractsDeliveryLocationsUpdateService>();
        services.AddScoped<SalesContractsDeliveryLocationsDeleteService>();
        services.AddScoped<SalesContractsDeliveryLocationsGetService>();
```

- [ ] **Step 4: Build + testes (nada quebrou)**

Run: `dotnet build SiagroB1.sln` → Expected: 0 erros.
Run: `dotnet test SiagroB1.Application.Tests` → Expected: todos verdes.

- [ ] **Step 5: Commit** (manual)

```
git add SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Web/Controllers/SalesContractsDeliveryLocationsController.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat: expose SalesContractsDeliveryLocations OData set, controller and DI"
```

---

### Task 5: Migration + snapshot

**Files:**
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddSalesContractDeliveryLocations.cs` (+ `.Designer.cs`)
- Modify: `SiagroB1.Migrations/AppContext/AppDbContextModelSnapshot.cs` (auto)

- [ ] **Step 1: Gerar a migration (sem aplicar ao banco)**

Run (a partir de `siagro-b1-backend/`):
```
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add AddSalesContractDeliveryLocations \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```
Expected: `Done.`

- [ ] **Step 2: Conferir o `Up`/`Down`**

Abrir o `.cs` gerado e confirmar:
- `CreateTable("SALES_CONTRACTS_DELIVERY_LOCATIONS")` com colunas `Key` (uniqueidentifier, PK), `SalesContractKey` (uniqueidentifier, nullable), `CardCode` (`VARCHAR(10)`, not null), `CardName` (`VARCHAR(200)`, nullable);
- FK `SalesContractKey` → `SALES_CONTRACTS.Key` + índice em `SalesContractKey`;
- `Down` faz `DropTable`.

- [ ] **Step 2b: Limpar a churn espúria do snapshot (IMPORTANTE)**

O EF 10 regenera `AppDbContextModelSnapshot.cs` num formato mais novo e reescreve
`b.ToTable("X")` → `b.ToTable("X", (string)null)` em TODAS as ~51 tabelas — churn que
não tem nada a ver com esta feature (memória: snapshot já tem drift, migrations são
hand-edited). O diff do snapshot deve conter APENAS o novo bloco da entidade.

Fazer:
1. `git diff SiagroB1.Migrations/AppContext/AppDbContextModelSnapshot.cs` — inspecionar.
2. Reverter todas as linhas `ToTable(..., (string)null)` das tabelas pré-existentes de
   volta para `ToTable(...)` (formato do arquivo commitado). Ferramenta prática:
   `git add -p` para stagear só os hunks do novo bloco, OU editar à mão.
3. No bloco NOVO da entidade `SalesContractDeliveryLocation`, usar o mesmo formato antigo
   do arquivo: `b.ToTable("SALES_CONTRACTS_DELIVERY_LOCATIONS");` (sem `, (string)null`).
4. Conferir `git diff --stat` do snapshot: deve mostrar só a adição do bloco novo
   (uma dúzia de linhas), não ~100 linhas de reformatação.

(O `.Designer.cs` da migration embute o modelo no formato novo — tudo bem, é arquivo
novo por migration; só o snapshot COMPARTILHADO precisa do diff mínimo.)

- [ ] **Step 3: Rodar o guard de modelo + build**

Run: `dotnet test SiagroB1.Application.Tests --filter AppDbContextModelTests`
Expected: PASS (parênteses balanceados — pega TypeName malformado).
Run: `dotnet build SiagroB1.sln` → 0 erros.

- [ ] **Step 4: Commit** (manual)

```
git add SiagroB1.Migrations/AppContext/
git commit -m "feat: migration for SALES_CONTRACTS_DELIVERY_LOCATIONS table"
```

---

### Task 6: Fragmento da tabela editável (frontend)

**Files:**
- Create: `siagro-b1-frontend/webapp/view/salesContracts/fragments/SalesContractDeliveryLocations.fragment.xml`

- [ ] **Step 1: Criar o fragmento (espelho de `PurchaseContractBrokers`, sem comissão)**

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:l="sap.ui.layout"
    xmlns:t="sap.ui.table"
    xmlns:core="sap.ui.core"
>
  <t:Table
    id="salesContractDeliveryLocationsTable"
    class="sapUiSizeCondensed"
    alternateRowColors="true"
    enableBusyIndicator="true"
    enableSelectAll="false"
    selectionBehavior="Row"
    selectionMode="Single"
    busyIndicatorDelay="0"
    rows="{
      path: 'DeliveryLocations',
      parameters: { '$$ownRequest': true }
    }"
    visible="true"
    >
    <t:extension>
      <OverflowToolbar>
        <content>
          <Title text="Locais de Entrega do Contrato" />
          <ToolbarSpacer />
          <Button
            visible="{ui>/editable}"
            text="Incluir"
            type="Transparent"
            icon="sap-icon://add"
            press=".onAddDeliveryLocation"
          />
          <Button
            visible="{ui>/editable}"
            text="Remover"
            type="Transparent"
            icon="sap-icon://delete"
            press=".onRemoveDeliveryLocation"
          />
        </content>
      </OverflowToolbar>
    </t:extension>
    <t:columns>
      <t:Column label="Codigo">
        <t:template>
          <Input
            editable="{ui>/editable}"
            required="true"
            value="{CardCode}"
            showValueHelp="true"
            valueHelpOnly="true"
            valueHelpRequest=".openCostumersValueHelp">
            <customData>
              <core:CustomData key="descriptionProperty" value="CardName" />
            </customData>
          </Input>
        </t:template>
      </t:Column>
      <t:Column label="Nome">
        <t:template>
          <Text text="{CardName}" />
        </t:template>
      </t:Column>
    </t:columns>
  </t:Table>
</core:FragmentDefinition>
```

- [ ] **Step 2: Validar parse do fragmento**

Run (a partir de `siagro-b1-frontend/`): `yarn ui5lint webapp/view/salesContracts/fragments/SalesContractDeliveryLocations.fragment.xml`
Expected: sem erro de parse; podem aparecer os mesmos findings de baseline do projeto (`valueHelpOnly` deprecated) — aceitável, é o padrão da casa.

- [ ] **Step 3: Commit** (manual)

```
git add webapp/view/salesContracts/fragments/SalesContractDeliveryLocations.fragment.xml
git commit -m "feat: sales contract delivery locations table fragment"
```

---

### Task 7: Handlers no `SalesContractsBaseController.ts`

**Files:**
- Modify: `siagro-b1-frontend/webapp/controller/salesContracts/SalesContractsBaseController.ts` (junto a `onAddQualityParameter`/`onRemoveQualityParameter`, ~linha 455-476)

**Interfaces:**
- Consumes: imports já presentes no arquivo — `Table`, `ODataListBinding`, `ODataModel`, `Context`, `MessageBox`.
- Produces: `onAddDeliveryLocation()`, `onRemoveDeliveryLocation()` (referenciados pelo fragmento da Task 6).

- [ ] **Step 1: Adicionar os handlers (espelho de `onAddQualityParameter`/`onRemoveQualityParameter`)**

```ts
  onAddDeliveryLocation() {
    const oTable = this.byId("salesContractDeliveryLocationsTable") as Table;
    const oBinding = oTable.getBinding("rows") as ODataListBinding;
    oBinding.create({}, false, true, false);
  }

  onRemoveDeliveryLocation() {
    const oModel = this.getView().getModel() as ODataModel;
    const oTable = this.byId("salesContractDeliveryLocationsTable") as Table;
    const aSelectedIndices = oTable.getSelectedIndices();

    if (aSelectedIndices.length === 0) {
      MessageBox.alert("Selecione um item para remover.");
      return;
    }

    const index = aSelectedIndices[0];
    const oContext = oTable.getContextByIndex(index) as Context;
    void oContext.delete(oModel.getUpdateGroupId());
  }
```

- [ ] **Step 2: Typecheck**

Run: `yarn ts-typecheck`
Expected: `Done` sem erros.

- [ ] **Step 3: Commit** (manual)

```
git add webapp/controller/salesContracts/SalesContractsBaseController.ts
git commit -m "feat: add/remove delivery location row handlers"
```

---

### Task 8: Embutir a seção em Add/Edit/Detail

**Files:**
- Modify: `siagro-b1-frontend/webapp/view/salesContracts/Add.view.xml`
- Modify: `siagro-b1-frontend/webapp/view/salesContracts/Edit.view.xml`
- Modify: `siagro-b1-frontend/webapp/view/salesContracts/Detail.view.xml`

- [ ] **Step 1: Add.view.xml — nova seção após "Dados do Contrato" (após a `</uxap:ObjectPageSection>` da linha ~43)**

```xml
      <uxap:ObjectPageSection titleUppercase="false" title="Locais de Entrega">
        <uxap:subSections>
          <uxap:ObjectPageSubSection>
            <uxap:blocks>
              <core:Fragment fragmentName="siagrob1.view.salesContracts.fragments.SalesContractDeliveryLocations" type="XML" />
            </uxap:blocks>
          </uxap:ObjectPageSubSection>
        </uxap:subSections>
      </uxap:ObjectPageSection>
```

- [ ] **Step 2: Edit.view.xml — mesma seção após "Dados do Contrato" (após a `</uxap:ObjectPageSection>` da linha ~43)**

(Colar o mesmo bloco XML do Step 1.)

- [ ] **Step 3: Detail.view.xml — mesma seção após "Dados do Contrato" (após a `</uxap:ObjectPageSection>` da linha ~210, antes de "Fixações de Preço")**

(Colar o mesmo bloco XML do Step 1.)

- [ ] **Step 4: Gates do frontend**

Run: `yarn ts-typecheck` → `Done`.
Run: `yarn ui5lint` → sem erro **novo** de parse nas 3 views (apenas o baseline pré-existente do projeto).

- [ ] **Step 5: Commit** (manual)

```
git add webapp/view/salesContracts/Add.view.xml webapp/view/salesContracts/Edit.view.xml webapp/view/salesContracts/Detail.view.xml
git commit -m "feat: embed delivery locations section in add/edit/detail views"
```

---

### Task 9: Verificação integrada (migration + stack + caminho do usuário)

Ambiente autorizado: profile **`yktb`** (environment **Yokotobi**), dev **admin/1234**, migrations autorizadas.

- [ ] **Step 1: Aplicar a migration no ambiente Yokotobi**

Conferir a connection string do ambiente ANTES (prática de migrations). Aplicar:
```
ASPNETCORE_ENVIRONMENT=Yokotobi dotnet ef database update \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```
Confirmar criação da tabela `SALES_CONTRACTS_DELIVERY_LOCATIONS`.

- [ ] **Step 2: Subir a stack**

Backend profile `yktb` (Web + Gateway); frontend `yarn start:dev`. Login `admin/1234`.

- [ ] **Step 3: Caminho do usuário no browser (obrigatório)**

Contratos de Venda → Novo:
- a seção "Locais de Entrega" aparece com botões Incluir/Remover;
- incluir 2 linhas; cada value help lista só clientes (`CardType='C'`), preenchendo Código + Nome;
- salvar cria o contrato com os 2 locais (deep insert) — conferir em Detail e/ou via `GET /odata/SalesContracts({key})/DeliveryLocations`;
- incluir o mesmo cliente 2× é bloqueado com mensagem de negócio;
- abrir em Edit (Draft): incluir e remover linha persistem (POST/DELETE no controller filho); Detail mostra a lista read-only (botões ocultos);
- criar contrato **sem** nenhum local grava normalmente (coleção opcional).

- [ ] **Step 4: Verificação final**

Usar `superpowers:verification-before-completion` antes de declarar concluído: colar a saída do `dotnet test`, dos gates do frontend e o resultado observado no browser (não declarar "pronto" sem a evidência visual — memória `verify-via-user-path-not-just-my-layer`).

---

## Self-Review (feito)

- **Cobertura da spec:** entidade (T1), serviços + duplicidade (T2), deep insert + guarda pura (T3), OData/controller/DI (T4), migration (T5), fragmento (T6), handlers (T7), views (T8), verificação (T9). Todos os itens da spec têm task.
- **Placeholders:** nenhum. A Task 3 foi reescrita para testar um método estático puro (`HasDuplicateDeliveryLocation`) — viável no harness — em vez de construir o `SalesContractsCreateService` inteiro (inviável: `DocNumberSequenceService` exige `IDbConnection` real). A resolução de nomes no deep insert é verificada explicitamente no browser (T9).
- **Consistência de tipos:** nomes de serviço, `ExecuteAsync` (assinaturas), `DeliveryLocations` (nav), `SalesContractsDeliveryLocations` (DbSet/EntitySet), `salesContractDeliveryLocationsTable` (id), `onAddDeliveryLocation`/`onRemoveDeliveryLocation` conferidos entre backend, controller, fragmento e handlers.
