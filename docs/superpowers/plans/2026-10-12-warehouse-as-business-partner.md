# Armazém como Parceiro de Negócio — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Em STANDALONE, armazém passa a ser o Parceiro de Negócio com `QryGroup23 = 'Y'` (como no SAPB1), com um único cadastro completo para NF-e.

**Architecture:** O `WarehouseService` STANDALONE troca a fonte de `WAREHOUSES` para `BUSINESS_PARTNERS` filtrado pela marca, espelhando `Services/SAP/WarehouseService.cs`; todos os consumidores já passam pelo `IWarehouseService`. O cadastro de PN passa a gravar a marca; a tela Armazéns vira lista + Complemento; uma migration só-SQL converte cada linha de `WAREHOUSES` em PN marcado, sem regravar documentos.

**Tech Stack:** .NET 10, EF Core (SQL Server; testes com InMemory), OData v4, xUnit; OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-12-warehouse-as-business-partner-design.md` (backend)

## Global Constraints

- Branch `feature/warehouse-as-business-partner` nos DOIS repos (`siagro-b1-backend`, `siagro-b1-frontend`); conferir o branch antes de cada commit. Nunca push, nunca merge.
- Identificadores em inglês; texto que o usuário lê em pt-BR.
- Mensagem de recusa exata: `Cadastre o armazém no Parceiro de Negócio, marcando 'Armazém'.`
- Marca do armazém: `QryGroup23` com valores `"Y"` / `"N"`.
- Commits no padrão `tipo(escopo): descrição pt-BR` (escopo `master-data`, ou `reports` para o relatório); commit com migration leva o trailer `DB: MigrateWarehousesToBusinessPartners`. Rodapé:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ
  ```
- Todo arquivo novo: `git add` logo após criar.
- Modo SAPB1 não muda (exceto a tela Armazéns, Task 6).
- Migration aplicada SÓ no `CEAGUI_SIAGRO_DEV` local. Nunca em `IDX_SIAGRO_DEV`, homologação ou produção.

## Review Focus

1. **PUT/PATCH de PN sem `QryGroup23`** (corpo antigo, legado com `NULL`): esperado manter a marca atual, nunca desmarcar o armazém em silêncio — teste na Task 2.
2. **Valor inválido em `QryGroup23`** (`"y"`, `"X"`, `""`): esperado gravar `"N"` na inclusão e `"N"` na edição — teste na Task 2.
3. **Armazém com vários endereços** (cobrança + duas entregas): cidade/UF vêm do endereço de ENTREGA de menor `AddressName`; cobrança ignorada — teste na Task 1.
4. **Migration reexecutada / PN já existente com o código do armazém**: esperado só ligar a marca, sem duplicar nem sobrescrever nome/CNPJ — verificação SQL na Task 4.
5. **Banco SAPB1 com `WAREHOUSES` não vazia**: a migration inseriria PN locais inúteis — conferência só-leitura do `IDX_SIAGRO_DEV` na Task 4 antes de commitar.

---

### Task 1: `WarehouseService` STANDALONE lê o Parceiro de Negócio

**Files:**
- Modify: `SiagroB1.Application/Services/WarehouseService.cs` (arquivo inteiro)
- Modify: `SiagroB1.Domain/Entities/Warehouse.cs` (comentário de obsolescência)
- Create: `SiagroB1.Application.Tests/Warehouses/WarehouseServiceStandaloneTests.cs`

**Interfaces:**
- Consumes: `AppDbContext.BusinessPartners` (`BusinessPartner.CardCode/CardName/CardFName/TaxId/Notes/QryGroup23/Addresses`), `Address.AdresType/AddressName/City/State`.
- Produces: `WarehouseService.ManagedByBusinessPartnerMessage` (`public const string`); comportamento de `IWarehouseService` inalterado na assinatura.

- [ ] **Step 1: Escrever os testes (falham)**

`SiagroB1.Application.Tests/Warehouses/WarehouseServiceStandaloneTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Warehouses;

/// <summary>
/// Em STANDALONE o armazém é o parceiro de negócio com QryGroup23 = 'Y', como no SAPB1. A tabela
/// WAREHOUSES não serve para NF-e (sem IE/endereço) e obrigava a cadastrar o armazém duas vezes
/// para emitir a remessa para depósito.
/// </summary>
public class WarehouseServiceStandaloneTests
{
    private static WarehouseService Service(UnitOfWork db) =>
        new(db.Context, NullLogger<WarehouseService>.Instance);

    private static async Task<UnitOfWork> Seeded()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.BusinessPartners.AddRange(
            new BusinessPartner
            {
                CardCode = "ARM001", CardName = "ARMAZEM GERAL", CardFName = "AG", CardType = "S",
                TaxId = "11222333000181", Notes = "OBS", QryGroup23 = "Y",
            },
            new BusinessPartner { CardCode = "F001", CardName = "PRODUTOR", CardType = "S", QryGroup23 = "N" },
            new BusinessPartner { CardCode = "F002", CardName = "LEGADO", CardType = "S", QryGroup23 = null });
        db.Context.Addresses.AddRange(
            new Address { CardCode = "ARM001", AddressName = "COBRANCA", AdresType = "B", City = "SAO PAULO", State = "SP" },
            new Address { CardCode = "ARM001", AddressName = "Z-ENTREGA", AdresType = "S", City = "CURITIBA", State = "PR" },
            new Address { CardCode = "ARM001", AddressName = "A-ENTREGA", AdresType = "S", City = "ITABERA", State = "SP" });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task QueryAll_lists_only_partners_flagged_as_warehouse()
    {
        var db = await Seeded();

        var codes = await Service(db).QueryAll().Select(w => w.Code).ToListAsync();

        Assert.Equal(new[] { "ARM001" }, codes);
    }

    [Fact]
    public async Task QueryAll_projects_partner_fields_and_first_ship_to_address()
    {
        var db = await Seeded();

        var warehouse = await Service(db).QueryAll().SingleAsync();

        Assert.Equal("ARMAZEM GERAL", warehouse.Name);
        Assert.Equal("AG", warehouse.FName);
        Assert.Equal("11222333000181", warehouse.TaxId);
        Assert.Equal("OBS", warehouse.Notes);
        Assert.Equal("ITABERA", warehouse.City);
        Assert.Equal("SP", warehouse.State);
    }

    [Fact]
    public async Task GetById_returns_null_for_partner_not_flagged()
    {
        var db = await Seeded();

        Assert.Null(await Service(db).GetByIdAsync("F001"));
        Assert.Equal("ARMAZEM GERAL", (await Service(db).GetByIdAsync("ARM001"))!.Name);
    }

    [Fact]
    public async Task GetAll_returns_the_flagged_partners()
    {
        var db = await Seeded();

        var all = (await Service(db).GetAllAsync()).ToList();

        Assert.Single(all);
        Assert.Equal("ARM001", all[0].Code);
    }

    [Fact]
    public async Task Create_update_and_delete_are_refused_with_guidance()
    {
        var db = await Seeded();
        var model = new WarehouseModel { Code = "NOVO", Name = "NOVO" };

        var create = await Assert.ThrowsAsync<DefaultException>(() => Service(db).CreateAsync(model));
        var update = await Assert.ThrowsAsync<DefaultException>(() => Service(db).UpdateAsync("ARM001", model));
        var delete = await Assert.ThrowsAsync<DefaultException>(() => Service(db).DeleteAsync("ARM001"));

        Assert.Equal("Cadastre o armazém no Parceiro de Negócio, marcando 'Armazém'.", create.Message);
        Assert.Equal(create.Message, update.Message);
        Assert.Equal(create.Message, delete.Message);
        Assert.Equal(3, await db.Context.BusinessPartners.CountAsync());
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run (de `siagro-b1-backend/`): `dotnet test SiagroB1.Application.Tests -m:1 --filter "FullyQualifiedName~WarehouseServiceStandaloneTests"`
Expected: FAIL — `ManagedBy…`/projeção vazia (o serviço ainda lê `WAREHOUSES`); `Create` não lança.

- [ ] **Step 3: Implementar**

Substituir `SiagroB1.Application/Services/WarehouseService.cs` por:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

/// <summary>
/// Armazém em STANDALONE: o parceiro de negócio com <c>QryGroup23 = 'Y'</c>, espelhando
/// <see cref="SAP.WarehouseService"/>. A tabela <c>WAREHOUSES</c> não serve para NF-e (sem IE nem
/// endereço) e ficou sem uso; o cadastro é feito no Parceiro de Negócio.
/// </summary>
public class WarehouseService(AppDbContext context, ILogger<WarehouseService> logger)
    : IWarehouseService
{
    public const string ManagedByBusinessPartnerMessage =
        "Cadastre o armazém no Parceiro de Negócio, marcando 'Armazém'.";

    public Task<WarehouseModel> CreateAsync(WarehouseModel model) =>
        throw new DefaultException(ManagedByBusinessPartnerMessage);

    public Task<WarehouseModel?> UpdateAsync(string code, WarehouseModel model) =>
        throw new DefaultException(ManagedByBusinessPartnerMessage);

    public Task<bool> DeleteAsync(string code) =>
        throw new DefaultException(ManagedByBusinessPartnerMessage);

    public IQueryable<WarehouseModel> QueryAll()
    {
        return context.BusinessPartners
            .AsNoTracking()
            .Where(x => x.QryGroup23 == "Y")
            .Select(x => new WarehouseModel
            {
                Code = x.CardCode,
                Name = x.CardName,
                FName = x.CardFName,
                TaxId = x.TaxId,
                Notes = x.Notes,
                // Mesma regra do LoadWarehousesAsync: primeiro endereço de ENTREGA pelo nome.
                City = x.Addresses
                    .Where(a => a.AdresType == "S")
                    .OrderBy(a => a.AddressName)
                    .Select(a => a.City)
                    .FirstOrDefault(),
                State = x.Addresses
                    .Where(a => a.AdresType == "S")
                    .OrderBy(a => a.AddressName)
                    .Select(a => a.State)
                    .FirstOrDefault(),
            });
    }

    public async Task<IEnumerable<WarehouseModel>> GetAllAsync() =>
        await QueryAll().ToListAsync();

    public async Task<WarehouseModel?> GetByIdAsync(string code)
    {
        try
        {
            return await QueryAll().FirstOrDefaultAsync(x => x.Code == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }

    public async Task<Dictionary<string, WarehouseInfo>> LoadWarehousesAsync()
    {
        return await context.BusinessPartners
            .AsNoTracking()
            .Where(x => x.QryGroup23 == "Y")
            .Select(bp => new WarehouseInfo
            {
                CardCode = bp.CardCode,
                CardName = bp.CardName,
                CardFName = bp.CardFName,
                TaxId = bp.TaxId,
                Notes = bp.Notes,
                Address = bp.Addresses
                    .Where(a => a.AdresType == "S")
                    .OrderBy(a => a.AddressName)
                    .Select(a => new WarehouseAddress
                    {
                        City = a.City,
                        State = a.State
                    })
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.CardCode);
    }
}
```

Em `SiagroB1.Domain/Entities/Warehouse.cs`, acima de `[Table("WAREHOUSES")]`:

```csharp
/// <summary>
/// OBSOLETA desde 12/10/2026: o armazém é o parceiro de negócio com <c>QryGroup23 = 'Y'</c> nos dois
/// modos. A migration <c>MigrateWarehousesToBusinessPartners</c> converteu as linhas; a tabela fica
/// só até ser apagada numa etapa posterior. Não leia nem grave aqui.
/// </summary>
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests -m:1 --filter "FullyQualifiedName~WarehouseServiceStandaloneTests"`
Expected: 5 PASS.

- [ ] **Step 5: Suíte completa** — `dotnet test SiagroB1.Application.Tests -m:1` — Expected: 0 falhas (base 4082 + 5).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application.Tests/Warehouses/WarehouseServiceStandaloneTests.cs SiagroB1.Application/Services/WarehouseService.cs SiagroB1.Domain/Entities/Warehouse.cs
git commit -m "feat(master-data): armazém STANDALONE vem do parceiro de negócio" -m "Espelha o modo SAPB1: o armazém é o PN com QryGroup23 = 'Y'. A tabela WAREHOUSES não serve para NF-e e obrigava cadastro duplicado para a remessa para depósito. Incluir/editar/excluir pela entidade Warehouses passa a ser recusado com orientação." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

### Task 2: Cadastro de PN STANDALONE grava a marca "Armazém"

**Files:**
- Modify: `SiagroB1.Application/Services/BusinessPartnerService.cs` (`CreateAsync` ~linha 100; `UpdateAsync` ~linhas 152-162; novo helper privado no fim da classe)
- Create: `SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerWarehouseFlagTests.cs`

**Interfaces:**
- Consumes: `BusinessPartnerModel.QryGroup23` (`string?`, já existe).
- Produces: `BUSINESS_PARTNERS.QryGroup23` gravado pela inclusão e edição.

- [ ] **Step 1: Escrever os testes (falham)**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.BusinessPartners;

/// <summary>
/// A marca "Armazém" (QryGroup23) do PN STANDALONE: é ela que faz o parceiro aparecer como armazém.
/// A inclusão forçava "N" e a edição a ignorava. Corpo sem a marca não pode desmarcar um armazém.
/// </summary>
public class BusinessPartnerWarehouseFlagTests
{
    private static BusinessPartnerService Service(UnitOfWork db) =>
        new(db, NullLogger<BusinessPartnerService>.Instance, new FakeStringLocalizer<Resource>());

    private static BusinessPartnerModel Partner(string? flag) => new()
    {
        CardCode = "ARM900", CardName = "ARMAZEM TESTE", CardType = "S", QryGroup23 = flag,
    };

    private static async Task<string?> StoredFlag(UnitOfWork db) =>
        (await db.Context.BusinessPartners.AsNoTracking().SingleAsync(b => b.CardCode == "ARM900")).QryGroup23;

    [Theory]
    [InlineData("Y", "Y")]
    [InlineData("N", "N")]
    [InlineData(null, "N")]
    [InlineData("y", "N")]
    [InlineData("X", "N")]
    public async Task Create_persists_the_warehouse_flag(string? sent, string expected)
    {
        var db = TestDb.CreateUnitOfWork();

        await Service(db).CreateAsync(Partner(sent));

        Assert.Equal(expected, await StoredFlag(db));
    }

    [Fact]
    public async Task Update_marks_and_unmarks_the_partner_as_warehouse()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Partner("N"));

        await Service(db).UpdateAsync("ARM900", Partner("Y"));
        Assert.Equal("Y", await StoredFlag(db));

        await Service(db).UpdateAsync("ARM900", Partner("N"));
        Assert.Equal("N", await StoredFlag(db));
    }

    [Fact]
    public async Task Update_without_the_flag_keeps_the_current_one()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Partner("Y"));

        await Service(db).UpdateAsync("ARM900", Partner(null));

        Assert.Equal("Y", await StoredFlag(db));
    }

    [Fact]
    public async Task Update_with_an_invalid_flag_stores_no()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Partner("Y"));

        await Service(db).UpdateAsync("ARM900", Partner("X"));

        Assert.Equal("N", await StoredFlag(db));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests -m:1 --filter "FullyQualifiedName~BusinessPartnerWarehouseFlagTests"`
Expected: FAIL em `Create_persists_the_warehouse_flag("Y","Y")` (grava "N") e nos de Update.

- [ ] **Step 3: Implementar**

Em `CreateAsync`, trocar `QryGroup23 = "N",` por:

```csharp
            QryGroup23 = WarehouseFlag(model.QryGroup23),
```

Em `UpdateAsync`, logo após `entity.PaymentConditionCode = model.PaymentConditionCode;`:

```csharp
        // Corpo sem a marca (cliente antigo, PUT parcial) não desmarca o armazém em silêncio.
        if (model.QryGroup23 is not null)
            entity.QryGroup23 = WarehouseFlag(model.QryGroup23);
```

No fim da classe:

```csharp
    /// <summary>QryGroup23 = 'Y' marca o parceiro como ARMAZÉM; qualquer outro valor vira 'N'.</summary>
    private static string WarehouseFlag(string? value) => value == "Y" ? "Y" : "N";
```

- [ ] **Step 4: Rodar e ver passar** — mesmo filtro; Expected: 8 PASS.

- [ ] **Step 5: Suíte completa** — `dotnet test SiagroB1.Application.Tests -m:1` — Expected: 0 falhas.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerWarehouseFlagTests.cs SiagroB1.Application/Services/BusinessPartnerService.cs
git commit -m "feat(partner): marca de armazém no cadastro de parceiro STANDALONE" -m "A inclusão forçava QryGroup23 = 'N' e a edição a ignorava, então nenhum PN STANDALONE podia ser armazém. Corpo sem a marca mantém a atual; valor diferente de 'Y' vira 'N'." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

### Task 3: Pré-contrato busca o CNPJ do armazém pelo parceiro

**Files:**
- Modify: `SiagroB1.Reports/Services/PrePurchaseContractReportService.cs` (linhas ~72 e ~93-95, ~128)
- Create: `SiagroB1.Application.Tests/Reports/PrePurchaseContractReportServiceTests.cs`

**Interfaces:**
- Consumes: `IPartnerSource.GetByCardCodeAsync(string, CancellationToken)` → `ReportPartnerDto?` (`TaxId`, `Cnpj`); `ContractPositionSeed.Purchase(...)` (gera contrato com `DeliveryLocationCode = "ARM01"`, `CardCode = "F001"`).
- Produces: `PrePurchaseContractReportService.GetAsync(Guid)` passa a ser `public` (era `private`), devolvendo `PrePurchaseContractPrintDto`.

- [ ] **Step 1: Escrever o teste (falha)**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.PartnerSources;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// O CNPJ do armazém (local de entrega) no pré-contrato vinha da tabela WAREHOUSES — vazia em SAPB1,
/// único modo em que o pré-contrato imprime, e obsoleta em STANDALONE. Agora vem do parceiro.
/// </summary>
public class PrePurchaseContractReportServiceTests
{
    private sealed class StubPartners(Dictionary<string, ReportPartnerDto> partners) : IPartnerSource
    {
        public Task<ReportPartnerDto?> GetByCardCodeAsync(string cardCode, CancellationToken ct = default) =>
            Task.FromResult(partners.GetValueOrDefault(cardCode));
    }

    private static PrePurchaseContractReportService Service(SiagroB1.Infra.UnitOfWork db, IPartnerSource partners)
    {
        var env = new TestWebHostEnvironment();
        var configuration = new ConfigurationBuilder().Build();
        return new PrePurchaseContractReportService(
            db, env, partners, configuration,
            new ReportHeaderService(env, configuration, NullLogger<ReportHeaderService>.Instance));
    }

    [Theory]
    [InlineData("11222333000181", null, "11222333000181")]
    [InlineData("99999999999999", "11222333000181", "11222333000181")]
    public async Task Warehouse_tax_id_comes_from_the_delivery_location_partner(
        string taxId, string? cnpj, string expected)
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = ContractPositionSeed.Purchase();
        db.Context.PurchaseContracts.Add(contract);
        await db.SaveChangesAsync();
        var partners = new StubPartners(new()
        {
            ["ARM01"] = new ReportPartnerDto { CardCode = "ARM01", TaxId = taxId, Cnpj = cnpj },
        });

        var data = await Service(db, partners).GetAsync(contract.Key);

        Assert.Equal(expected, data.WarehouseTaxId);
    }

    [Fact]
    public async Task Warehouse_tax_id_is_empty_when_the_partner_is_unknown()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = ContractPositionSeed.Purchase();
        db.Context.PurchaseContracts.Add(contract);
        await db.SaveChangesAsync();

        var data = await Service(db, new StubPartners(new())).GetAsync(contract.Key);

        Assert.Null(data.WarehouseTaxId);
    }
}
```

Se o construtor de `TestWebHostEnvironment` exigir argumentos, copiar a forma usada em `SiagroB1.Application.Tests/Reports/DanfeReportServiceTests.cs:30-40`.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests -m:1 --filter "FullyQualifiedName~PrePurchaseContractReportServiceTests"`
Expected: erro de compilação `GetAsync is inaccessible due to its protection level`.

- [ ] **Step 3: Implementar**

Trocar `private async Task<PrePurchaseContractPrintDto> GetAsync(Guid key)` por `public async Task<PrePurchaseContractPrintDto> GetAsync(Guid key)`.

Substituir:

```csharp
        var warehouse = await db.Context.Warehouses
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Code == contract.DeliveryLocationCode);
```

por:

```csharp
        // O armazém é parceiro de negócio nos dois modos (QryGroup23 = 'Y'); WAREHOUSES é obsoleta.
        var warehouse = string.IsNullOrWhiteSpace(contract.DeliveryLocationCode)
            ? null
            : await partnerSource.GetByCardCodeAsync(contract.DeliveryLocationCode);
```

e `WarehouseTaxId = warehouse?.TaxId,` por:

```csharp
            // Na origem SAP o CNPJ detalhado vem do CRD7 (Cnpj); na standalone só o TaxId.
            WarehouseTaxId = warehouse?.Cnpj ?? warehouse?.TaxId,
```

- [ ] **Step 4: Rodar e ver passar** — mesmo filtro; Expected: 3 PASS. Se falhar por `Include` de navegação ausente (InMemory), semear o que faltar na própria Arrange, sem mexer no serviço.

- [ ] **Step 5: Suíte completa** — `dotnet test SiagroB1.Application.Tests -m:1` — Expected: 0 falhas.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application.Tests/Reports/PrePurchaseContractReportServiceTests.cs SiagroB1.Reports/Services/PrePurchaseContractReportService.cs
git commit -m "fix(reports): CNPJ do armazém do pré-contrato vem do parceiro" -m "Lia a tabela WAREHOUSES, vazia em SAPB1 (único modo em que o pré-contrato imprime) e obsoleta em STANDALONE; o campo saía sempre em branco." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

### Task 4: Migration que converte `WAREHOUSES` em PN marcado

**Files:**
- Create (gerados): `SiagroB1.Migrations/AppContext/<timestamp>_MigrateWarehousesToBusinessPartners.cs` e `.Designer.cs`
- Modify (gerado, deve ficar sem diferença de modelo): `SiagroB1.Migrations/AppContext/AppDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: tabelas `WAREHOUSES(Code, Name, TAXID)` e `BUSINESS_PARTNERS(CardCode, CardName, CardType, QryGroup23, U_YKT_CNPJ_CPF)`.
- Produces: migration `MigrateWarehousesToBusinessPartners` (só SQL, idempotente).

- [ ] **Step 1: Conferir que nenhum banco SAPB1 tem armazém local** (só leitura)

Usar o script do scratchpad desta sessão (`q.ps1`, que lê a connection string do `appsettings.<Env>.json` e roda SELECT) com `-Env Yokotobi-Development` e o SQL `SELECT COUNT(*) n FROM WAREHOUSES`. Expected: `0`. Se vier > 0, PARE e reporte ao usuário (a migration criaria PN locais em base SAPB1).

- [ ] **Step 2: Gerar a migration vazia**

Run (de `siagro-b1-backend/`, Bash):

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add MigrateWarehousesToBusinessPartners \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```

Conferir: os arquivos caíram em `SiagroB1.Migrations/AppContext/`; `Up`/`Down` vazios; `git diff AppDbContextModelSnapshot.cs` só com a linha de versão ou vazio. Qualquer outra operação de schema = drift do snapshot: pare e investigue. `git add` dos arquivos gerados.

- [ ] **Step 3: Escrever o SQL**

No `.cs` gerado:

```csharp
        /// <summary>
        /// O armazém passa a ser o parceiro de negócio com QryGroup23 = 'Y' (como no SAPB1). Cada linha
        /// de WAREHOUSES vira PN com o MESMO código, então nenhum documento é regravado (lotes,
        /// movimentos, contratos, liberações e cargas guardam o código). PN já existente com o código
        /// só ganha a marca. Idempotente; em base SAPB1 WAREHOUSES é vazia e nada acontece.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE bp SET QryGroup23 = 'Y'
                FROM BUSINESS_PARTNERS bp
                INNER JOIN WAREHOUSES w ON w.Code = bp.CardCode
                WHERE ISNULL(bp.QryGroup23, '') <> 'Y';

                INSERT INTO BUSINESS_PARTNERS (CardCode, CardName, CardType, QryGroup23, U_YKT_CNPJ_CPF)
                SELECT w.Code, w.Name, 'S', 'Y', NULLIF(LTRIM(RTRIM(w.TAXID)), '')
                FROM WAREHOUSES w
                WHERE NOT EXISTS (SELECT 1 FROM BUSINESS_PARTNERS bp WHERE bp.CardCode = w.Code);
                """);
        }

        /// <summary>Sem volta: desmarcar/apagar PN poderia atingir parceiros que o usuário já completou.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
```

- [ ] **Step 4: Estado ANTES no `CEAGUI_SIAGRO_DEV`** (só leitura, `-Env Ceagui-Development`):

```sql
SELECT CardCode, CardName, CardType, QryGroup23, U_YKT_CNPJ_CPF FROM BUSINESS_PARTNERS WHERE CardCode IN (SELECT Code FROM WAREHOUSES);
SELECT COUNT(*) n FROM BUSINESS_PARTNERS;
```

Expected: só `FTESTEPJ` (QryGroup23 `N`); anotar o total.

- [ ] **Step 5: Aplicar no `CEAGUI_SIAGRO_DEV`**

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Expected: aplica só `MigrateWarehousesToBusinessPartners`. Se começar a replayar migrations antigas, PARE (banco errado na connection string).

- [ ] **Step 6: Estado DEPOIS** — mesmas consultas do Step 4. Expected: `0000000001` "CEAGUI" tipo `S`, `Y`, CNPJ NULL (novo); `FTESTEPJ` agora `Y` com nome/CNPJ intactos; total = antes + 1.

- [ ] **Step 7: Idempotência** — rodar o bloco SQL do Step 3 de novo direto no `CEAGUI_SIAGRO_DEV` (via `q.ps1`, envolvendo em `BEGIN TRAN ... ROLLBACK` e mostrando `@@ROWCOUNT` de cada comando). Expected: 0 e 0.

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Migrations/AppContext/
git commit -m "feat(master-data): migrar armazéns para parceiro de negócio" -m "Cada linha de WAREHOUSES vira PN com o mesmo código e QryGroup23 = 'Y' (ou só ganha a marca se o PN já existe), sem regravar documentos. Levantamento de 12/10: MH Agro cria 0001, CEAGUI cria 0000000001, sem colisão." -m "Atenção: o PN criado nasce sem endereço nem IE; para ser destinatário de NF-e o cadastro precisa ser completado." -m "DB: MigrateWarehousesToBusinessPartners
Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

### Task 5 (frontend): marca "Armazém" no formulário do PN

**Files (em `siagro-b1-frontend/`):**
- Modify: `webapp/view/parceirosNegocio/fragments/Form.fragment.xml` (após o `Select` de Tipo, ~linha 62)
- Modify: `webapp/controller/parceirosNegocio/BaseController.ts` (novo handler)
- Modify: `webapp/controller/parceirosNegocio/Add.controller.ts:31` (comentário)

**Interfaces:**
- Consumes: propriedade OData `QryGroup23` (`"Y"`/`"N"`) de `BusinessPartners`; modelo `ui>/standalone`.
- Produces: handler `onWarehouseFlagSelect(event: CheckBox$SelectEvent): void` no `BaseController` de `parceirosNegocio`.

- [ ] **Step 1: Fragment** — depois do `</Select>` do Tipo:

```xml
          <Label text="Armazém" visible="{ui>/standalone}" />
          <CheckBox
              selected="{= ${QryGroup23} === 'Y' }"
              select=".onWarehouseFlagSelect"
              visible="{ui>/standalone}"
          />
```

(Expressão é só leitura: o handler grava a propriedade.)

- [ ] **Step 2: Handler** — em `webapp/controller/parceirosNegocio/BaseController.ts`, importar `import { CheckBox$SelectEvent } from "sap/m/CheckBox";` e `import Context from "sap/ui/model/odata/v4/Context";` (se ainda não importados) e adicionar:

```ts
	/** QryGroup23 = 'Y' marca o parceiro como ARMAZÉM (STANDALONE); é o que o lista em Armazéns. */
	onWarehouseFlagSelect(event: CheckBox$SelectEvent): void {
		const context = this.getView().getBindingContext() as Context;
		context.setProperty("QryGroup23", event.getParameter("selected") ? "Y" : "N");
	}
```

- [ ] **Step 3: Comentário** — em `Add.controller.ts`, trocar `//QryGroup23 = Y --> Cadastro de Armazem` por `// QryGroup23 = 'Y' marca o parceiro como armazém (checkbox "Armazém" do formulário).`

- [ ] **Step 4: Verificar** — `npm run ts-typecheck` e `npm run lint` (de `siagro-b1-frontend/`). Expected: limpos.

- [ ] **Step 5: Commit** (branch `feature/warehouse-as-business-partner` do FRONTEND)

```bash
git add webapp/view/parceirosNegocio/fragments/Form.fragment.xml webapp/controller/parceirosNegocio/BaseController.ts webapp/controller/parceirosNegocio/Add.controller.ts
git commit -m "feat(partner): marca de armazém no formulário do parceiro" -m "Em STANDALONE o armazém passou a ser o PN com QryGroup23 = 'Y'; o formulário ganha o checkbox para marcá-lo." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

### Task 6 (frontend): tela Armazéns vira lista + Complemento

**Files (em `siagro-b1-frontend/`):**
- Modify: `webapp/view/armazem/Main.view.xml`
- Modify: `webapp/controller/armazem/Main.controller.ts` (remover `onCreate`, `onEdit`, `onDelete` e imports órfãos)
- Delete: `webapp/controller/armazem/Add.controller.ts`, `webapp/controller/armazem/Edit.controller.ts`, `webapp/view/armazem/Add.view.xml`, `webapp/view/armazem/Edit.view.xml`, `webapp/view/armazem/fragments/Form.fragment.xml` (usado só por Add/Edit — conferir com `grep -rn "armazem.fragments.Form" webapp` antes)
- Modify: `webapp/manifest.json` (rotas `armazensNew`/`armazensEdit` ~linhas 207-215 e targets ~1308-1319)

**Interfaces:**
- Consumes: entity set `/Warehouses` (`Code`, `Name`, `TaxId`, `City`, `State`), rota `armazensComplement`.
- Produces: nada para tarefas seguintes.

- [ ] **Step 1: View** — no `t:Table`, adicionar `noData="Nenhum armazém. Marque 'Armazém' no cadastro do Parceiro de Negócio."`; trocar o título da Page para `title="Armazéns"`, o label `Codigo` para `Código` e `Descrição` para `Nome`; após a coluna CNPJ, adicionar:

```xml
					<t:Column label="Cidade/UF" sortProperty="City" filterProperty="City">
						<t:template>
							<Text text="{= ${City} ? ${City} + '/' + ${State} : '' }" wrapping="false" />
						</t:template>
					</t:Column>
```

No footer, deixar só:

```xml
				<Button text="Atualizar" icon="sap-icon://refresh" press=".onRefresh" />
				<ToolbarSpacer />
				<Button text="Complemento" icon="sap-icon://add-product" press=".onComplement"/>
```

- [ ] **Step 2: Controller** — remover `onCreate`, `onEdit`, `onDelete`; remover imports que ficarem sem uso (`ODataModel`, `confirmDialog`). Trocar a mensagem do `onComplement` para `"Selecione um armazém."`.

- [ ] **Step 3: Apagar Add/Edit** — `git rm` dos 5 arquivos listados; no `manifest.json` remover os objetos de rota `armazensNew`/`armazensEdit` e os targets homônimos, cuidando das vírgulas do JSON.

- [ ] **Step 4: Sem referências órfãs** — `grep -rn "armazensNew\|armazensEdit\|armazem.Add\|armazem.Edit" webapp` → Expected: nada.

- [ ] **Step 5: Verificar** — `npm run ts-typecheck` e `npm run lint`. Expected: limpos.

- [ ] **Step 6: Commit**

```bash
git add -A webapp/view/armazem webapp/controller/armazem webapp/manifest.json
git commit -m "feat(master-data): tela de armazéns só lista e complemento" -m "O armazém é cadastrado no Parceiro de Negócio (marca Armazém); incluir/editar/excluir aqui não existe mais. Em SAPB1 esses botões já davam erro. Ganha Cidade/UF e orientação quando a lista está vazia." -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NfKCUyvWCYfeLQ36wn4oVQ"
```

---

### Task 7: Verificação end-to-end (CEAGUI dev, STANDALONE)

Seguir `superpowers:verification-before-completion`. Ambiente: profile `ceagui` (Web, Gateway, Reports; `ASPNETCORE_ENVIRONMENT=Ceagui-Development`, banco `CEAGUI_SIAGRO_DEV`, migration da Task 4 já aplicada), frontend `yarn start:dev`, usuário `admin`/`1234`. Matar processos só pelo PID que você subiu.

- [ ] **Step 1:** Tela Armazéns: lista `0000000001` CEAGUI e `FTESTEPJ`; só Atualizar e Complemento; Complemento abre para o `0000000001`.
- [ ] **Step 2:** Parceiros: abrir um fornecedor sem marca, marcar "Armazém", salvar; reabrir e conferir marcado; ele aparece em Armazéns. Desmarcar, salvar; some de Armazéns.
- [ ] **Step 3:** Incluir PN novo com "Armazém" marcado e endereço de entrega; aparece em Armazéns com Cidade/UF.
- [ ] **Step 4:** Liberação de entrega: o value help de armazém lista o PN novo; criar uma liberação para ele e emitir a remessa para depósito sem cadastro duplicado (até a tela de confirmação da NF-e; transmitir só se a natureza/cadastro estiverem completos — em homologação).
- [ ] **Step 5:** Lotes/movimentos antigos do `0000000001` e cargas continuam com o nome do armazém.
- [ ] **Step 6:** `PATCH /odata/Warehouses('0000000001')` e `POST /odata/Warehouses` pelo Gateway → 400 com a mensagem de orientação.
- [ ] **Step 7:** `dotnet test SiagroB1.Application.Tests -m:1` (0 falhas), `npm run ts-typecheck`, `npm run lint` nos branches finais.
- [ ] **Step 8:** Relatar ao usuário o resultado e os pendentes de deploy: migration vai junto no próximo deploy da CEAGUI e da MH Agro (esta com migrations paradas em 15/09); completar CNPJ/IE/endereço dos PN-armazém criados se algum for destinatário de NF-e.
