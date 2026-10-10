# Controle de royalties Bayer/Monsanto (baixas no ITS) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apurar situação de royalty, origem e código do teste na entrada de soja (ticket → romaneio), listar o saldo a baixar no ITS (tela + PDF) e registrar as baixas feitas no site do ITS (N:N, parcial), abatendo o saldo de cada romaneio.

**Architecture:** Campos novos em `WEIGHING_TICKETS`/`STORAGE_TRANSACTIONS` e um indicador em `ITEM_COMPLEMENTS`; documento novo `ROYALTY_WRITE_OFFS` + `ROYALTY_WRITE_OFF_LINES`. O saldo é **calculado** (peso líquido − soma das linhas ativas) por uma consulta única em `SiagroB1.Infra` (`RoyaltyBalanceQuery`), reaproveitada pelo OData (lista/totais) e pelo `SiagroB1.Reports` (PDF) — o Reports não referencia o Application. Regras de obrigatoriedade num helper estático (`RoyaltyRules`) chamado pela conclusão do ticket e pela confirmação do romaneio de Recebimento.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), OData v4 (`Microsoft.AspNetCore.OData`), xUnit, FastReport.OpenSource; OpenUI5 + TypeScript (OData v4 model).

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-10-royalty-its-write-off-design.md`

## Global Constraints

- Identificadores, tabelas e colunas em **inglês**; tudo que o usuário lê (labels, mensagens de negócio, título de menu) em **pt-BR**.
- Situações: `Declared` = "Declarada", `Tested` = "Testada", `Conventional` = "Convencional", `ParticipantWarehouse` = "Armazém Participante".
- Base da baixa = **peso líquido** (`StorageTransaction.NetWeight`, kg). `Weight` das linhas é `DECIMAL(18,3)` (mesma escala do `NetWeight`, senão sobra 0,001 kg pendente para sempre).
- Elegível: `TransactionType = Receipt`, `TransactionStatus ∈ {Confirmed, Invoiced}`, `RoyaltySituation != null`, produto com `ItemComplement.ControlsRoyalties`.
- Erros de negócio: `ApplicationException` (os controllers mapeiam para 400); não-encontrado: `NotFoundException`.
- Serviços são classes concretas registradas à mão em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (`services.AddScoped<X>()`).
- Parâmetros decimais editáveis trafegam como `double` no EDM (o UI5 v4 serializa `Edm.Decimal` como string). Parâmetros de action podem chegar nulos: sempre `TryGetValue`, nunca `.ToString()` direto; `parameters` inteiro pode ser nulo.
- Enum no frontend: binding com `targetType: 'any'`; filtro de enum só por `$filter` cru (`Situation eq 'Declared'`), nunca `sap.ui.model.Filter`.
- Migrations: gerar, **ler o arquivo gerado**, nunca aplicar em banco real. Comando (de `siagro-b1-backend/`):
  `dotnet ef migrations add <Name> --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o AppContext` (ou `CommonDbContext` / `-o CommonContext`).
- Commits: `tipo(escopo): descrição pt-BR` (escopos fechados: `storage`, `weighing`, `master-data`, `reports`, `purchase-contract`…); trailer `DB: <Migration>` obrigatório quando houver migration; terminar com as linhas `Co-Authored-By`/`Claude-Session` da sessão. Commitar em `feature/royalty-its-write-off` nos **dois** repos; **nunca push, nunca merge**.
- Todo arquivo novo: `git add` imediatamente no repo a que pertence.
- Baseline de testes backend: `dotnet test SiagroB1.Application.Tests` (se estiver numa worktree, copie antes `SiagroB1.Reports/wwwroot` do repo original — os testes de PDF precisam do `logo.png`).
- Frontend: `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint` (o `yarn test` tem gate de cobertura que nunca passa — não é critério).

## Desvios da spec (decididos ao ler o código; a Task 17 atualiza a spec)

1. Chaves: o projeto não usa `DocEntry`/`LineId`. Cabeçalho herda `BaseEntity` (`Guid Key`, `RowId` identidade, auditoria); linha é POCO com `Guid Key` + `RoyaltyWriteOffKey`. O `RowId` serve de número da baixa na tela.
2. `Weight` vira `DECIMAL(18,3)` (ver Global Constraints).
3. Cópia dos campos: só a conclusão do ticket e `StorageTransactionCopyFactory` constroem romaneio de recebimento a partir de outro. O estorno não cria documento (volta o mesmo para Pendente, campos ficam); a troca de liberação só mexe em romaneios de compra/venda. A **Entrada em armazenagem própria** cria o Recebimento clonando a Compra pela `CopyFactory` — então a tela de criação da entrada passa a ter os campos (Task 13), senão a soja não confirmaria.
4. Correção da situação num romaneio já confirmado: pelo caminho existente (estornar → editar → confirmar). O update de romaneio já só aceita Pendente, e estornar é bloqueado com baixa ativa — a trava "não alterar com baixa ativa" sai de graça.
5. `RoyaltyOriginName` é resolvido **no backend** a partir do código (o value help grava a descrição com grupo `null`, que não vai no PATCH). Origem vazia em produto controlado assume o próprio parceiro (`CardCode`).
6. A situação da baixa não é parâmetro: é a situação comum dos romaneios escolhidos.

## Review Focus

1. **Romaneios antigos (antes do recurso)** — recebimentos confirmados sem situação não podem aparecer como pendentes nem travar nada. A consulta exige `RoyaltySituation != null` (teste na Task 5).
2. **Baixa do saldo inteiro com 3 casas** — linha com peso = saldo `1234,567` precisa zerar o saldo, não deixar resíduo (teste na Task 6).
3. **Duas linhas do mesmo romaneio na mesma baixa** — somadas não podem passar do saldo; o serviço recusa chave repetida (teste na Task 6).
4. **ID do ITS reaproveitado depois de cancelar** — uma baixa cancelada libera o ID; ativa duplicada é recusada (testes nas Tasks 6 e 7).
5. **Produto que deixa de controlar royalties** — romaneios dele somem da lista de pendentes, mas baixas já feitas continuam consultáveis na aba Baixas (teste na Task 5).

---

## Parte A — Backend (`C:\Projetos\SiagroB1\siagro-b1-backend`, branch `feature/royalty-its-write-off`, já criada)

### Task 1: Modelo de dados + migration

**Files:**
- Create: `SiagroB1.Domain/Enums/RoyaltySituation.cs`
- Create: `SiagroB1.Domain/Enums/RoyaltyWriteOffStatus.cs`
- Create: `SiagroB1.Domain/Entities/RoyaltyWriteOff.cs`
- Create: `SiagroB1.Domain/Entities/RoyaltyWriteOffLine.cs`
- Modify: `SiagroB1.Domain/Entities/ItemComplement.cs` (após `CommercialFactor`, linha 32)
- Modify: `SiagroB1.Domain/Entities/WeighingTicket.cs` (após `FreightPrice`, linha 99)
- Modify: `SiagroB1.Domain/Entities/StorageTransaction.cs` (após `FreightPrice`, linha 239)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSets perto da linha 106; configuração no fim de `OnModelCreating`)
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddRoyaltyWriteOffs.cs` (gerada)
- Test: `SiagroB1.Application.Tests/Infra/RoyaltyWriteOffModelTests.cs`

**Interfaces:**
- Produces: `enum RoyaltySituation { Declared = 0, Tested = 1, Conventional = 2, ParticipantWarehouse = 3 }`; `enum RoyaltyWriteOffStatus { Active = 0, Cancelled = 1 }`; entidades `RoyaltyWriteOff` (`WriteOffDate`, `ItsId`, `TaxId`, `Name`, `Situation`, `Status`, `Comments`, `Lines`), `RoyaltyWriteOffLine` (`Key`, `RoyaltyWriteOffKey`, `RoyaltyWriteOff`, `StorageTransactionKey`, `StorageTransaction`, `Weight`); propriedades `RoyaltySituation?`, `RoyaltyTestCode`, `RoyaltyOriginCode`, `RoyaltyOriginName` em `WeighingTicket` e `StorageTransaction`; `ItemComplement.ControlsRoyalties`; `AppDbContext.RoyaltyWriteOffs`, `AppDbContext.RoyaltyWriteOffLines`.

- [ ] **Step 1: Write the failing model test**

```csharp
// SiagroB1.Application.Tests/Infra/RoyaltyWriteOffModelTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class RoyaltyWriteOffModelTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void ItsId_is_unique_only_among_active_write_offs()
    {
        using var context = CreateContext();

        var index = context.Model.FindEntityType(typeof(RoyaltyWriteOff))!
            .GetIndexes()
            .Single(x => x.Properties.Select(p => p.Name).SequenceEqual([nameof(RoyaltyWriteOff.ItsId)]));

        Assert.True(index.IsUnique);
        Assert.Equal($"[Status] = {(int)RoyaltyWriteOffStatus.Active}", index.GetFilter());
    }

    [Fact]
    public void Line_weight_has_the_same_scale_as_the_net_weight()
    {
        using var context = CreateContext();

        var weight = context.Model.FindEntityType(typeof(RoyaltyWriteOffLine))!
            .FindProperty(nameof(RoyaltyWriteOffLine.Weight))!;

        Assert.Equal("DECIMAL(18,3)", weight.GetColumnType());
    }

    [Fact]
    public void Line_cannot_delete_its_storage_transaction()
    {
        using var context = CreateContext();

        var fk = context.Model.FindEntityType(typeof(RoyaltyWriteOffLine))!
            .GetForeignKeys()
            .Single(x => x.PrincipalEntityType.ClrType == typeof(StorageTransaction));

        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~RoyaltyWriteOffModelTests"`
Expected: build FAIL — `RoyaltyWriteOff` / `RoyaltyWriteOffLine` / `RoyaltyWriteOffStatus` não existem.

- [ ] **Step 3: Create enums and entities**

```csharp
// SiagroB1.Domain/Enums/RoyaltySituation.cs
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação de royalty (tecnologia Bayer/Monsanto) apurada na entrada da soja e lançada no ITS.
/// Declarada e Testada consomem crédito no ITS; Convencional e Armazém Participante só informam.
/// </summary>
public enum RoyaltySituation
{
    Declared = 0,
    Tested = 1,
    Conventional = 2,
    ParticipantWarehouse = 3,
}
```

```csharp
// SiagroB1.Domain/Enums/RoyaltyWriteOffStatus.cs
namespace SiagroB1.Domain.Enums;

public enum RoyaltyWriteOffStatus
{
    Active = 0,
    Cancelled = 1,
}
```

```csharp
// SiagroB1.Domain/Entities/RoyaltyWriteOff.cs
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Baixa feita no ITS (sistema da Bayer) registrada no SiagroB1. Uma baixa cobre um ou mais
/// romaneios de recebimento, cada um com o peso baixado (pode ser parcial). Não se edita:
/// errou, cancela e refaz — cancelar devolve o saldo aos romaneios.
/// </summary>
[Table("ROYALTY_WRITE_OFFS")]
public class RoyaltyWriteOff : BaseEntity
{
    [Column(TypeName = "DATE")]
    public DateTime WriteOffDate { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ItsId { get; set; }

    /// <summary>CPF/CNPJ usado na baixa do crédito — digitado, pode não ser o parceiro do romaneio.</summary>
    [Column(TypeName = "VARCHAR(18) NOT NULL")]
    public required string TaxId { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>Situação comum a todas as linhas (no ITS cada situação é uma operação diferente).</summary>
    public RoyaltySituation Situation { get; set; }

    public RoyaltyWriteOffStatus Status { get; set; } = RoyaltyWriteOffStatus.Active;

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    public ICollection<RoyaltyWriteOffLine> Lines { get; set; } = [];
}
```

```csharp
// SiagroB1.Domain/Entities/RoyaltyWriteOffLine.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("ROYALTY_WRITE_OFF_LINES")]
public class RoyaltyWriteOffLine
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    public Guid RoyaltyWriteOffKey { get; set; }
    public RoyaltyWriteOff? RoyaltyWriteOff { get; set; }

    public Guid StorageTransactionKey { get; set; }
    public StorageTransaction? StorageTransaction { get; set; }

    /// <summary>Peso baixado em kg — mesma escala do <see cref="StorageTransaction.NetWeight"/>.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal Weight { get; set; }
}
```

- [ ] **Step 4: Add the new properties to the existing entities**

`ItemComplement.cs`, depois de `CommercialFactor`:

```csharp
    /// <summary>
    /// Produto sujeito ao controle de royalties (ITS): tickets e romaneios de recebimento dele exigem
    /// situação de royalty e origem, e entram na lista de pendentes de baixa.
    /// </summary>
    [Column(TypeName = "bit")]
    public bool ControlsRoyalties { get; set; }
```

`WeighingTicket.cs` **e** `StorageTransaction.cs`, depois de `FreightPrice` (mesmo bloco nos dois; em `StorageTransaction.cs` adicionar `using SiagroB1.Domain.Enums;` se faltar — já existe):

```csharp
    /// <summary>Situação de royalty (ITS). Obrigatória quando o produto controla royalties.</summary>
    public RoyaltySituation? RoyaltySituation { get; set; }

    /// <summary>Código do teste — obrigatório só na situação Testada.</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? RoyaltyTestCode { get; set; }

    /// <summary>Parceiro de origem da carga (produtor, ou o armazém participante de onde veio).</summary>
    [Column(TypeName = "VARCHAR(10)")]
    public string? RoyaltyOriginCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? RoyaltyOriginName { get; set; }
```

- [ ] **Step 5: Register in AppDbContext**

Perto da linha 106 (`ItemComplements`):

```csharp
    public DbSet<RoyaltyWriteOff> RoyaltyWriteOffs { get; set; }
    public DbSet<RoyaltyWriteOffLine> RoyaltyWriteOffLines { get; set; }
```

No fim de `OnModelCreating` (depois do loop global de NoAction, que já roda antes):

```csharp
        // Royalties (ITS): o ID do ITS é único entre baixas ATIVAS — cancelar libera o número.
        modelBuilder.Entity<RoyaltyWriteOff>()
            .HasIndex(x => x.ItsId)
            .IsUnique()
            .HasFilter($"[Status] = {(int)RoyaltyWriteOffStatus.Active}");

        modelBuilder.Entity<RoyaltyWriteOff>()
            .HasMany(x => x.Lines)
            .WithOne(x => x.RoyaltyWriteOff)
            .HasForeignKey(x => x.RoyaltyWriteOffKey)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RoyaltyWriteOffLine>()
            .HasOne(x => x.StorageTransaction)
            .WithMany()
            .HasForeignKey(x => x.StorageTransactionKey)
            .OnDelete(DeleteBehavior.Restrict);

        // O saldo a baixar é SUM(Weight) por romaneio: índice de cobertura.
        modelBuilder.Entity<RoyaltyWriteOffLine>()
            .HasIndex(x => x.StorageTransactionKey)
            .IncludeProperties(x => x.Weight);
```

- [ ] **Step 6: Run the model tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~RoyaltyWriteOffModelTests"`
Expected: 3 PASS.

- [ ] **Step 7: Generate and read the migration**

Run: `dotnet ef migrations add AddRoyaltyWriteOffs --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o AppContext`

Ler o arquivo gerado e conferir: `AddColumn` de `ControlsRoyalties` (`bit`, `nullable: false`, `defaultValue: false`) em `ITEM_COMPLEMENTS`; 4 colunas anuláveis em `WEIGHING_TICKETS` e em `STORAGE_TRANSACTIONS` (`RoyaltySituation int`); `CreateTable` das duas tabelas novas; índice `IX_ROYALTY_WRITE_OFFS_ItsId` com `filter: "[Status] = 0"`; FK de linha → `STORAGE_TRANSACTIONS` com `onDelete: ReferentialAction.Restrict`. Nenhuma alteração em outra tabela (se aparecer, o snapshot estava sujo — parar e investigar). **Não aplicar.**

Índice filtrado em SQL Server exige `QUOTED_IDENTIFIER ON` na sessão que aplicar — anotar no corpo do commit.

- [ ] **Step 8: Build and full test run**

Run: `dotnet build SiagroB1.sln` → 0 erros. `dotnet test SiagroB1.Application.Tests` → tudo verde (mesmo total do baseline + 3).

- [ ] **Step 9: Commit**

```bash
git add SiagroB1.Domain/Enums/RoyaltySituation.cs SiagroB1.Domain/Enums/RoyaltyWriteOffStatus.cs \
  SiagroB1.Domain/Entities/RoyaltyWriteOff.cs SiagroB1.Domain/Entities/RoyaltyWriteOffLine.cs \
  SiagroB1.Domain/Entities/ItemComplement.cs SiagroB1.Domain/Entities/WeighingTicket.cs \
  SiagroB1.Domain/Entities/StorageTransaction.cs SiagroB1.Infra/Context/AppDbContext.cs \
  SiagroB1.Migrations/AppContext/*AddRoyaltyWriteOffs* SiagroB1.Migrations/AppContext/AppDbContextModelSnapshot.cs \
  SiagroB1.Application.Tests/Infra/RoyaltyWriteOffModelTests.cs
git commit -F- <<'EOF'
feat(storage): modelo de dados do controle de royalties no ITS

Situação de royalty, código do teste e origem no ticket e no romaneio;
indicador "controla royalties" no complemento do produto; baixa ITS com
linhas por romaneio (peso parcial, escala do peso líquido).

Atenção: o índice único filtrado do ID do ITS exige QUOTED_IDENTIFIER ON
ao aplicar a migration (sqlcmd -I).

DB: AddRoyaltyWriteOffs

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 2: "Controla royalties" no complemento do produto (API)

**Files:**
- Modify: `SiagroB1.Domain/Dtos/ItemComplementDto.cs`
- Modify: `SiagroB1.Domain/Interfaces/IItemComplementService.cs`
- Modify: `SiagroB1.Application/Services/ItemComplementService.cs`
- Modify: `SiagroB1.Web/Actions/Items/ItemsSetComplementController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs:446-455` (action `ItemsSetComplement`)
- Test: `SiagroB1.Application.Tests/Items/ItemComplementServiceTests.cs`

**Interfaces:**
- Consumes: `ItemComplement.ControlsRoyalties` (Task 1).
- Produces: `ItemComplementDto.ControlsRoyalties` (bool); `IItemComplementService.SetAsync(string itemCode, string? uom, decimal? factor, bool? controlsRoyalties = null)` — `null` mantém o valor atual (outros chamadores não mudam); parâmetro EDM opcional `ControlsRoyalties` (bool) em `ItemsSetComplement`.

- [ ] **Step 1: Write the failing tests** (acrescentar à classe existente, usando o mesmo setup de `ItemComplementServiceTests`; se a classe usa um campo `_db`/método `Service()`, reaproveite-os)

```csharp
    [Fact]
    public async Task Set_persists_controls_royalties()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new ItemComplementService(db);

        var result = await service.SetAsync("SOJA", null, null, controlsRoyalties: true);

        Assert.True(result.ControlsRoyalties);
        Assert.True((await service.GetAsync("SOJA"))!.ControlsRoyalties);
    }

    [Fact]
    public async Task Set_without_controls_royalties_keeps_the_current_value()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new ItemComplementService(db);
        await service.SetAsync("SOJA", null, null, controlsRoyalties: true);

        await service.SetAsync("SOJA", "SC", 60m);

        Assert.True((await service.GetAsync("SOJA"))!.ControlsRoyalties);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ItemComplementServiceTests"`
Expected: build FAIL — `SetAsync` não aceita 4 argumentos / `ControlsRoyalties` não existe no DTO.

- [ ] **Step 3: Implement**

DTO:

```csharp
    [JsonPropertyName("ControlsRoyalties")]
    public bool ControlsRoyalties { get; set; }
```

Interface:

```csharp
    /// <summary>
    /// Upsert por <c>ItemCode</c>. UoM e fator são opcionais: passar ambos nulos limpa a UoM
    /// comercial, que é uma operação válida. <paramref name="controlsRoyalties"/> nulo mantém o
    /// valor atual (o diálogo de faturamento não conhece esse campo).
    /// </summary>
    Task<ItemComplementDto> SetAsync(
        string itemCode, string? commercialUnitOfMeasureCode, decimal? commercialFactor,
        bool? controlsRoyalties = null);
```

Serviço: no `GetAsync`, acrescentar `ControlsRoyalties = x.ControlsRoyalties,` ao `Select`; no `SetAsync`, mesma assinatura da interface e, antes do `SaveChangesAsync`:

```csharp
        if (controlsRoyalties.HasValue)
            entity.ControlsRoyalties = controlsRoyalties.Value;
```

e `ControlsRoyalties = entity.ControlsRoyalties,` no DTO de retorno.

EDM, logo após os parâmetros existentes de `itemsSetComplement`:

```csharp
        itemsSetComplement.Parameter<bool>("ControlsRoyalties").Optional();
```

Controller, junto dos outros `TryGetValue`:

```csharp
            parameters.TryGetValue("ControlsRoyalties", out var royaltiesObj);
```

e passar `royaltiesObj is null ? null : Convert.ToBoolean(royaltiesObj)` como 4º argumento de `SetAsync`.

- [ ] **Step 4: Run tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~ItemComplement"` → PASS. `dotnet build SiagroB1.sln` → 0 erros (outros chamadores de `SetAsync` continuam compilando pelo default).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Domain/Dtos/ItemComplementDto.cs SiagroB1.Domain/Interfaces/IItemComplementService.cs \
  SiagroB1.Application/Services/ItemComplementService.cs SiagroB1.Web/Actions/Items/ItemsSetComplementController.cs \
  SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Application.Tests/Items/ItemComplementServiceTests.cs
git commit -F- <<'EOF'
feat(master-data): indicador de controle de royalties no complemento do produto

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 3: Regras de royalty no ticket de pesagem

**Files:**
- Create: `SiagroB1.Application/Services/Royalties/RoyaltyRules.cs`
- Modify: `SiagroB1.Application/Services/WeighingTickets/WeighingTicketsCompletedService.cs` (antes de `Validate`, linha 38; `Validate` linhas 124-172; inicializador do romaneio linhas 58-82)
- Modify: `SiagroB1.Application/Services/WeighingTickets/WeighingTicketsUpdateService.cs` (depois do `SetValues`, linha 35)
- Test: `SiagroB1.Application.Tests/Royalties/WeighingTicketRoyaltyTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces (usado nas Tasks 4 e 6):

```csharp
public static class RoyaltyRules
{
    Task<bool> ControlsRoyaltiesAsync(AppDbContext ctx, string? itemCode);
    void EnsureFilled(RoyaltySituation? situation, string? originCode, string? testCode);
    string? NormalizeTestCode(RoyaltySituation? situation, string? testCode);
    Task EnsureNoActiveWriteOffAsync(AppDbContext ctx, Guid storageTransactionKey);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
// SiagroB1.Application.Tests/Royalties/WeighingTicketRoyaltyTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.WeighingTickets;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Royalties;

public class WeighingTicketRoyaltyTests
{
    private const string CardCode = "P0001";
    private const string OtherWarehouse = "P0099";
    private const string ItemCode = "SOJA";
    private const string LotCode = "LOTE-SOJA";
    private const string Warehouse = "ARM01";

    private static readonly Dictionary<string, string> Partners = new()
    {
        [CardCode] = "Produtor Teste",
        [OtherWarehouse] = "Armazém Parceiro",
    };

    // Mesma montagem de WeighingTicketTransshipmentLotExitTriggerTests.Service — confira os
    // usings/namespaces lá se algum não resolver.
    private static WeighingTicketsCompletedService Completed(IUnitOfWork db) => new(
        db,
        new FakeBusinessPartnerService(Partners),
        new FakeItemService(new() { [ItemCode] = "Soja em grãos" }),
        new StorageTransactionsCreateService(
            db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(Partners),
            new FakeItemService(new() { [ItemCode] = "Soja em grãos" }),
            new FakeWarehouseService(new() { [Warehouse] = "Armazém" }),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new ShipmentReleaseMovementGuardService(db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance),
        new StorageTransactionsConfirmedService(
            db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new ShipmentReleaseMovementGuardService(db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance),
        new StorageAddressesGetService(db, NullLogger<StorageAddressesGetService>.Instance),
        new ShipmentLoadsTransshipmentAttachLotExitService(
            db,
            new FakeWarehouseService(new() { [Warehouse] = "Armazém" }),
            new ShipmentReleasesFromReturnService(db.Context),
            new ShipmentLoadsMovementLogService(db.Context)),
        new FakeStringLocalizer<Resource>(),
        NullLogger<WeighingTicketsCompletedService>.Instance);

    private static async Task<IUnitOfWork> SeedAsync(bool controlsRoyalties)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageAddresses.Add(new StorageAddress
        {
            Code = LotCode, Description = "Lote soja", CardCode = CardCode, ItemCode = ItemCode,
            WarehouseCode = Warehouse, UoM = "KG", Nature = StorageAddressNature.Regular,
        });
        db.Context.ItemComplements.Add(new ItemComplement { ItemCode = ItemCode, ControlsRoyalties = controlsRoyalties });
        await db.SaveChangesAsync();
        return db;
    }

    private static WeighingTicket ReceiptTicket() => new()
    {
        Key = Guid.NewGuid(),
        Type = WeighingTicketType.Receipt,
        ItemCode = ItemCode,
        CardCode = CardCode,
        BranchCode = "01",
        TruckCode = "ABC1D23",
        TruckDriverCode = "1",
        Stage = WeighingTicketStage.ReadyForCompleting,
        StorageAddressCode = LotCode,
        FirstWeighValue = 50_000,
        SecondWeighValue = 20_000,
    };

    [Fact]
    public async Task Completing_requires_a_situation_when_the_item_controls_royalties()
    {
        var db = await SeedAsync(controlsRoyalties: true);
        var ticket = ReceiptTicket();
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Completed(db).ExecuteAsync(ticket.Key, "tester"));

        Assert.Contains("situação de royalty", ex.Message);
    }

    [Fact]
    public async Task Completing_requires_the_test_code_when_tested()
    {
        var db = await SeedAsync(controlsRoyalties: true);
        var ticket = ReceiptTicket();
        ticket.RoyaltySituation = RoyaltySituation.Tested;
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Completed(db).ExecuteAsync(ticket.Key, "tester"));

        Assert.Contains("código do teste", ex.Message);
    }

    [Fact]
    public async Task Completing_without_royalty_control_ignores_the_fields()
    {
        var db = await SeedAsync(controlsRoyalties: false);
        var ticket = ReceiptTicket();
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        await Completed(db).ExecuteAsync(ticket.Key, "tester");

        var st = await db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.WeighingTicketKey == ticket.Key);
        Assert.Null(st.RoyaltySituation);
    }

    [Fact]
    public async Task Completing_copies_the_royalty_fields_and_defaults_the_origin_to_the_partner()
    {
        var db = await SeedAsync(controlsRoyalties: true);
        var ticket = ReceiptTicket();
        ticket.RoyaltySituation = RoyaltySituation.Tested;
        ticket.RoyaltyTestCode = "T-123";
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        await Completed(db).ExecuteAsync(ticket.Key, "tester");

        var st = await db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.WeighingTicketKey == ticket.Key);
        Assert.Equal(RoyaltySituation.Tested, st.RoyaltySituation);
        Assert.Equal("T-123", st.RoyaltyTestCode);
        Assert.Equal(CardCode, st.RoyaltyOriginCode);
        Assert.Equal("Produtor Teste", st.RoyaltyOriginName);
    }

    [Fact]
    public async Task Update_resolves_the_origin_name_and_clears_the_test_code_outside_tested()
    {
        var db = await SeedAsync(controlsRoyalties: true);
        var ticket = ReceiptTicket();
        ticket.Stage = WeighingTicketStage.ReadyForFirstWeighing;
        db.Context.WeighingTickets.Add(ticket);
        await db.SaveChangesAsync();

        var patch = ReceiptTicket();
        patch.Key = ticket.Key;
        patch.Stage = ticket.Stage;
        patch.RoyaltySituation = RoyaltySituation.ParticipantWarehouse;
        patch.RoyaltyTestCode = "sobrou";
        patch.RoyaltyOriginCode = OtherWarehouse;

        var service = new WeighingTicketsUpdateService(
            db, new FakeBusinessPartnerService(Partners), new FakeItemService(new() { [ItemCode] = "Soja em grãos" }),
            NullLogger<WeighingTicketsUpdateService>.Instance);
        await service.ExecuteAsync(ticket.Key, patch, "tester");

        var saved = await db.Context.WeighingTickets.AsNoTracking().SingleAsync(x => x.Key == ticket.Key);
        Assert.Equal("Armazém Parceiro", saved.RoyaltyOriginName);
        Assert.Null(saved.RoyaltyTestCode);
    }
}
```

> Se `GrossWeight` do ticket não for calculado a partir de `FirstWeighValue`/`SecondWeighValue` e o
> `Validate` reclamar de peso, atribua `GrossWeight = 30_000` no `ReceiptTicket()`.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WeighingTicketRoyaltyTests"`
Expected: os 2 primeiros e o 4º falham (nenhuma validação/cópia); o 5º falha (nome não resolvido); o 3º passa.

- [ ] **Step 3: Create `RoyaltyRules`**

```csharp
// SiagroB1.Application/Services/Royalties/RoyaltyRules.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Royalties;

/// <summary>
/// Regras do controle de royalties (ITS) compartilhadas por ticket de pesagem, romaneio e baixa.
/// </summary>
public static class RoyaltyRules
{
    public static async Task<bool> ControlsRoyaltiesAsync(AppDbContext ctx, string? itemCode) =>
        !string.IsNullOrWhiteSpace(itemCode) &&
        await ctx.ItemComplements.AnyAsync(x => x.ItemCode == itemCode && x.ControlsRoyalties);

    public static void EnsureFilled(RoyaltySituation? situation, string? originCode, string? testCode)
    {
        if (situation is null || string.IsNullOrWhiteSpace(originCode))
            throw new ApplicationException("Informe a situação de royalty e a origem da carga.");

        if (situation == RoyaltySituation.Tested && string.IsNullOrWhiteSpace(testCode))
            throw new ApplicationException("Informe o código do teste para a situação Testada.");
    }

    /// <summary>O código do teste só existe na situação Testada.</summary>
    public static string? NormalizeTestCode(RoyaltySituation? situation, string? testCode) =>
        situation == RoyaltySituation.Tested && !string.IsNullOrWhiteSpace(testCode) ? testCode.Trim() : null;

    public static async Task EnsureNoActiveWriteOffAsync(AppDbContext ctx, Guid storageTransactionKey)
    {
        var itsId = await ctx.RoyaltyWriteOffLines
            .Where(l => l.StorageTransactionKey == storageTransactionKey &&
                        l.RoyaltyWriteOff!.Status == RoyaltyWriteOffStatus.Active)
            .Select(l => l.RoyaltyWriteOff!.ItsId)
            .FirstOrDefaultAsync();

        if (itsId != null)
            throw new ApplicationException($"Romaneio possui baixa ITS ativa ({itsId}); cancele a baixa antes.");
    }
}
```

- [ ] **Step 4: Wire into the ticket services**

`WeighingTicketsCompletedService.ExecuteAsync`, logo depois de carregar `existingTicket` e **antes** de `await Validate(existingTicket);`:

```csharp
        var controlsRoyalties = await RoyaltyRules.ControlsRoyaltiesAsync(db.Context, existingTicket.ItemCode);
        if (controlsRoyalties && existingTicket.Type == WeighingTicketType.Receipt)
        {
            // Origem não informada = o próprio produtor do ticket.
            if (string.IsNullOrWhiteSpace(existingTicket.RoyaltyOriginCode))
                existingTicket.RoyaltyOriginCode = existingTicket.CardCode;
            RoyaltyRules.EnsureFilled(
                existingTicket.RoyaltySituation, existingTicket.RoyaltyOriginCode, existingTicket.RoyaltyTestCode);
            existingTicket.RoyaltyTestCode =
                RoyaltyRules.NormalizeTestCode(existingTicket.RoyaltySituation, existingTicket.RoyaltyTestCode);
        }
```

Dentro do `try`, depois de `existingTicket.ItemName = ...` (linha 54):

```csharp
            existingTicket.RoyaltyOriginName = string.IsNullOrWhiteSpace(existingTicket.RoyaltyOriginCode)
                ? null
                : (await businessPartnerService.GetByIdAsync(existingTicket.RoyaltyOriginCode))?.CardName;
```

No inicializador `new StorageTransaction { ... }`, depois de `FreightPrice = existingTicket.FreightPrice`:

```csharp
                FreightPrice = existingTicket.FreightPrice,
                RoyaltySituation = existingTicket.RoyaltySituation,
                RoyaltyTestCode = existingTicket.RoyaltyTestCode,
                RoyaltyOriginCode = existingTicket.RoyaltyOriginCode,
                RoyaltyOriginName = existingTicket.RoyaltyOriginName,
```

`WeighingTicketsUpdateService`, depois de `existingEntity.ItemName = ...`:

```csharp
            existingEntity.RoyaltyTestCode =
                RoyaltyRules.NormalizeTestCode(existingEntity.RoyaltySituation, existingEntity.RoyaltyTestCode);
            // O value help grava a descrição só na tela (grupo null); o nome vem sempre do código.
            existingEntity.RoyaltyOriginName = string.IsNullOrWhiteSpace(existingEntity.RoyaltyOriginCode)
                ? null
                : (await businessPartnerService.GetByIdAsync(existingEntity.RoyaltyOriginCode))?.CardName;
```

Adicionar `using SiagroB1.Application.Services.Royalties;` nos dois arquivos.

- [ ] **Step 5: Run tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~WeighingTicket"` → tudo PASS (incluindo os testes de transbordo existentes).

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/Royalties/RoyaltyRules.cs \
  SiagroB1.Application/Services/WeighingTickets/WeighingTicketsCompletedService.cs \
  SiagroB1.Application/Services/WeighingTickets/WeighingTicketsUpdateService.cs \
  SiagroB1.Application.Tests/Royalties/WeighingTicketRoyaltyTests.cs
git commit -F- <<'EOF'
feat(weighing): exigir situação de royalty e origem ao concluir o ticket de soja

Produto com "controla royalties" no complemento só conclui o ticket de
recebimento com situação e origem (padrão: o próprio produtor), e com o
código do teste quando Testada. Os campos seguem para o romaneio.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 4: Regras de royalty no romaneio

**Files:**
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsConfirmedService.cs:221-248` (`ExecuteReceiptTransactionAsync`)
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsUpdateService.cs` (depois do `SetValues`)
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCreateService.cs` (onde resolve `CardName`)
- Modify: `SiagroB1.Application/Services/StorageTransactions/Factories/StorageTransactionCopyFactory.cs:42-58`
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCancelService.cs` (guards antes do `try`, ~linha 99)
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsReverseService.cs` (guards antes do `try`, ~linha 98)
- Test: `SiagroB1.Application.Tests/Royalties/StorageTransactionRoyaltyTests.cs`

**Interfaces:**
- Consumes: `RoyaltyRules` (Task 3), `RoyaltyWriteOff`/`RoyaltyWriteOffLine` (Task 1).

- [ ] **Step 1: Write the failing tests**

```csharp
// SiagroB1.Application.Tests/Royalties/StorageTransactionRoyaltyTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Services.StorageTransactions.Factories;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Royalties;

public class StorageTransactionRoyaltyTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransactionsConfirmedService Confirmed() => new(
        _db,
        new FakeStringLocalizer<Resource>(),
        new ShipmentReleasesRecalculateShippedService(_db.Context),
        new ShipmentReleaseMovementGuardService(_db.Context),
        NullLogger<StorageTransactionsConfirmedService>.Instance);

    private async Task<StorageTransaction> SeedReceiptAsync(
        bool controlsRoyalties,
        StorageTransactionsStatus status = StorageTransactionsStatus.Pending,
        RoyaltySituation? situation = null)
    {
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = controlsRoyalties });
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1",
            CardCode = "P0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            BranchCode = "01",
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = status,
            TransactionOrigin = TransactionCode.StorageTransaction,
            GrossWeight = 30_000m,
            NetWeight = 30_000m,
            RoyaltySituation = situation,
            RoyaltyOriginCode = situation is null ? null : "P0001",
        };
        _db.Context.StorageTransactions.Add(st);
        await _db.SaveChangesAsync();
        return st;
    }

    private async Task AddActiveWriteOffAsync(StorageTransaction st)
    {
        var writeOff = new RoyaltyWriteOff
        {
            ItsId = "ITS-1", TaxId = "12345678909", Name = "Produtor", Situation = RoyaltySituation.Declared,
            WriteOffDate = DateTime.Today,
        };
        writeOff.Lines.Add(new RoyaltyWriteOffLine { StorageTransactionKey = st.Key, Weight = 1_000m });
        _db.Context.RoyaltyWriteOffs.Add(writeOff);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Confirming_a_controlled_receipt_without_situation_is_refused()
    {
        var st = await SeedReceiptAsync(controlsRoyalties: true);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Confirmed().ExecuteAsync(st, "tester"));

        Assert.Contains("situação de royalty", ex.Message);
        Assert.Equal(StorageTransactionsStatus.Pending, st.TransactionStatus);
    }

    [Fact]
    public async Task Confirming_a_controlled_receipt_with_situation_works()
    {
        var st = await SeedReceiptAsync(controlsRoyalties: true, situation: RoyaltySituation.Conventional);

        await Confirmed().ExecuteAsync(st, "tester");

        Assert.Equal(StorageTransactionsStatus.Confirmed, st.TransactionStatus);
    }

    [Fact]
    public async Task Confirming_a_receipt_of_an_uncontrolled_item_ignores_royalties()
    {
        var st = await SeedReceiptAsync(controlsRoyalties: false);

        await Confirmed().ExecuteAsync(st, "tester");

        Assert.Equal(StorageTransactionsStatus.Confirmed, st.TransactionStatus);
    }

    [Fact]
    public void Copy_carries_the_royalty_fields()
    {
        var original = new StorageTransaction
        {
            CardCode = "P0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG", WarehouseCode = "ARM01",
            RoyaltySituation = RoyaltySituation.Tested, RoyaltyTestCode = "T-9",
            RoyaltyOriginCode = "P0002", RoyaltyOriginName = "Origem",
        };

        var copy = StorageTransactionCopyFactory.CreateFrom(original, "tester");

        Assert.Equal(RoyaltySituation.Tested, copy.RoyaltySituation);
        Assert.Equal("T-9", copy.RoyaltyTestCode);
        Assert.Equal("P0002", copy.RoyaltyOriginCode);
        Assert.Equal("Origem", copy.RoyaltyOriginName);
    }

    [Fact]
    public async Task Cancel_is_refused_while_an_active_write_off_exists()
    {
        var st = await SeedReceiptAsync(true, StorageTransactionsStatus.Confirmed, RoyaltySituation.Declared);
        await AddActiveWriteOffAsync(st);
        var service = new StorageTransactionsCancelService(_db, new ShipmentReleasesRecalculateShippedService(_db.Context));

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(st.Key, "tester"));

        Assert.Contains("baixa ITS ativa (ITS-1)", ex.Message);
    }

    [Fact]
    public async Task Reverse_is_refused_while_an_active_write_off_exists()
    {
        var st = await SeedReceiptAsync(true, StorageTransactionsStatus.Confirmed, RoyaltySituation.Declared);
        await AddActiveWriteOffAsync(st);
        var service = new StorageTransactionsReverseService(
            _db,
            new StorageAddressesGetBalanceService(_db.Context),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new FakeStringLocalizer<Resource>());

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(st.Key, "tester"));

        Assert.Contains("baixa ITS ativa", ex.Message);
    }

    [Fact]
    public async Task Cancelled_write_off_no_longer_blocks_the_reversal_guard()
    {
        var st = await SeedReceiptAsync(true, StorageTransactionsStatus.Confirmed, RoyaltySituation.Declared);
        await AddActiveWriteOffAsync(st);
        var writeOff = _db.Context.RoyaltyWriteOffs.Single();
        writeOff.Status = RoyaltyWriteOffStatus.Cancelled;
        await _db.SaveChangesAsync();

        await Services.Royalties.RoyaltyRules.EnsureNoActiveWriteOffAsync(_db.Context, st.Key);
    }
}
```

> `StorageAddressesGetBalanceService`: confira o construtor real (`grep -n "class StorageAddressesGetBalanceService" -A6`) e ajuste a instanciação — o teste só precisa que o guard de baixa rode **antes** do `ValidateBalance`.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageTransactionRoyaltyTests"`
Expected: falham `Confirming_..._without_situation`, `Copy_carries...`, `Cancel_is_refused...`, `Reverse_is_refused...`.

- [ ] **Step 3: Implement**

`StorageTransactionsConfirmedService.ExecuteReceiptTransactionAsync`, logo depois do guard de status Pending (linha 228) e **antes** do `try`:

```csharp
        if (await RoyaltyRules.ControlsRoyaltiesAsync(db.Context, st.ItemCode))
            RoyaltyRules.EnsureFilled(st.RoyaltySituation, st.RoyaltyOriginCode, st.RoyaltyTestCode);
```

`StorageTransactionCopyFactory.CreateFrom`, no inicializador, depois de `ProcessingCostCode = original.ProcessingCostCode,`:

```csharp
            RoyaltySituation = original.RoyaltySituation,
            RoyaltyTestCode = original.RoyaltyTestCode,
            RoyaltyOriginCode = original.RoyaltyOriginCode,
            RoyaltyOriginName = original.RoyaltyOriginName,
```

`StorageTransactionsUpdateService`, depois de `existingEntity.WarehouseName = ...`:

```csharp
            existingEntity.RoyaltyTestCode =
                RoyaltyRules.NormalizeTestCode(existingEntity.RoyaltySituation, existingEntity.RoyaltyTestCode);
            existingEntity.RoyaltyOriginName = string.IsNullOrWhiteSpace(existingEntity.RoyaltyOriginCode)
                ? null
                : (await businessPartnerService.GetByIdAsync(existingEntity.RoyaltyOriginCode))?.CardName;
```

`StorageTransactionsCreateService`: no ponto em que ele resolve `entity.CardName` pelo `businessPartnerService`, acrescentar o mesmo par de linhas (`RoyaltyTestCode` normalizado e `RoyaltyOriginName` resolvido) sobre a entidade que está sendo criada. Isso cobre o romaneio criado pela tela e o recebimento da Entrada em armazenagem própria.

`StorageTransactionsCancelService` e `StorageTransactionsReverseService`: como **primeiro** guard depois de carregar `doc` (antes dos guards de carga/transbordo), uma linha:

```csharp
        await RoyaltyRules.EnsureNoActiveWriteOffAsync(db.Context, doc.Key);
```

Adicionar `using SiagroB1.Application.Services.Royalties;` em cada arquivo tocado.

- [ ] **Step 4: Run tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~StorageTransaction|FullyQualifiedName~StorageEntry|FullyQualifiedName~Royalt"` → PASS. Depois `dotnet test SiagroB1.Application.Tests` completo → verde.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/StorageTransactions SiagroB1.Application.Tests/Royalties/StorageTransactionRoyaltyTests.cs
git commit -F- <<'EOF'
feat(storage): situação de royalty obrigatória no recebimento e trava de baixa ITS

Romaneio de recebimento de produto controlado só confirma com situação e
origem. Romaneio com baixa ITS ativa não pode ser cancelado nem estornado
(e por isso também não volta a ser editado). A cópia de romaneio — usada
pela entrada em armazenagem própria — leva os campos de royalty.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 5: Consulta de saldo (Infra) + OData de saldos e totais

**Files:**
- Create: `SiagroB1.Domain/Dtos/RoyaltyBalanceRow.cs`
- Create: `SiagroB1.Domain/Dtos/RoyaltyBalanceFilter.cs`
- Create: `SiagroB1.Infra/Queries/RoyaltyBalanceQuery.cs`
- Create: `SiagroB1.Web/Controllers/RoyaltyTransactionBalancesController.cs`
- Create: `SiagroB1.Web/Functions/Royalties/RoyaltyBalanceTotalsController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (entity set + function)
- Test: `SiagroB1.Application.Tests/Royalties/RoyaltyBalanceQueryTests.cs`

**Interfaces:**
- Produces:

```csharp
public class RoyaltyBalanceRow   // SiagroB1.Domain.Dtos
{
    Guid StorageTransactionKey; string? Code; DateTime? TransactionDate; string? BranchCode;
    string CardCode; string? CardName; string? RoyaltyOriginCode; string? RoyaltyOriginName;
    string ItemCode; string? ItemName; string? StorageAddressCode;
    RoyaltySituation Situation; string? RoyaltyTestCode;
    decimal NetWeight; decimal WrittenOffWeight; decimal Balance;
}
public class RoyaltyBalanceFilter  // SiagroB1.Domain.Dtos — todos opcionais
{
    DateTime? FromDate; DateTime? ToDate; string? CardCode; string? OriginCode; string? ItemCode;
    RoyaltySituation? Situation; string? StorageAddressCode; bool PendingOnly = true;
}
public class RoyaltyBalanceTotal { RoyaltySituation Situation; decimal NetWeight; decimal WrittenOffWeight; decimal Balance; int Count; }
public static class RoyaltyBalanceQuery  // SiagroB1.Infra.Queries
{
    IQueryable<RoyaltyBalanceRow> Build(AppDbContext ctx);
    IQueryable<RoyaltyBalanceRow> Apply(IQueryable<RoyaltyBalanceRow> query, RoyaltyBalanceFilter filter);
    Task<List<RoyaltyBalanceTotal>> TotalsAsync(IQueryable<RoyaltyBalanceRow> query);
}
```
- OData: entity set `RoyaltyTransactionBalances` (chave `StorageTransactionKey`, todos os elegíveis, com e sem saldo); function `RoyaltyBalanceTotals(FromDate, ToDate, CardCode, OriginCode, ItemCode, Situation, StorageAddressCode)` → coleção de `RoyaltyBalanceTotal` (só pendentes).

- [ ] **Step 1: Write the failing tests**

```csharp
// SiagroB1.Application.Tests/Royalties/RoyaltyBalanceQueryTests.cs
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Application.Tests.Royalties;

public class RoyaltyBalanceQueryTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransaction Receipt(
        string code, decimal net, RoyaltySituation? situation = RoyaltySituation.Declared,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        StorageTransactionType type = StorageTransactionType.Receipt, string item = "SOJA")
    {
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "P0001", ItemCode = item, UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01", TransactionType = type, TransactionStatus = status,
            TransactionDate = new DateTime(2026, 10, 1), NetWeight = net, RoyaltySituation = situation,
            RoyaltyOriginCode = "P0001",
        };
        _db.Context.StorageTransactions.Add(st);
        return st;
    }

    private void WriteOff(StorageTransaction st, decimal weight, RoyaltyWriteOffStatus status = RoyaltyWriteOffStatus.Active)
    {
        var w = new RoyaltyWriteOff
        {
            ItsId = Guid.NewGuid().ToString("N")[..10], TaxId = "12345678909", Name = "X",
            Situation = st.RoyaltySituation!.Value, Status = status, WriteOffDate = DateTime.Today,
        };
        w.Lines.Add(new RoyaltyWriteOffLine { StorageTransactionKey = st.Key, Weight = weight });
        _db.Context.RoyaltyWriteOffs.Add(w);
    }

    private async Task<List<RoyaltyBalanceRow>> RunAsync(RoyaltyBalanceFilter? filter = null) =>
        await RoyaltyBalanceQuery.Apply(RoyaltyBalanceQuery.Build(_db.Context), filter ?? new RoyaltyBalanceFilter())
            .OrderBy(x => x.Code).ToListAsync();

    public RoyaltyBalanceQueryTests()
    {
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "MILHO", ControlsRoyalties = false });
    }

    [Fact]
    public async Task Balance_is_net_weight_minus_active_write_offs()
    {
        var st = Receipt("R1", 30_000m);
        WriteOff(st, 10_000m);
        WriteOff(st, 5_000m, RoyaltyWriteOffStatus.Cancelled);
        await _db.SaveChangesAsync();

        var row = Assert.Single(await RunAsync());

        Assert.Equal(10_000m, row.WrittenOffWeight);
        Assert.Equal(20_000m, row.Balance);
    }

    [Fact]
    public async Task Only_confirmed_or_invoiced_receipts_of_controlled_items_with_situation_are_eligible()
    {
        Receipt("A-ok", 1m);
        Receipt("B-invoiced", 1m, status: StorageTransactionsStatus.Invoiced);
        Receipt("C-pending", 1m, status: StorageTransactionsStatus.Pending);
        Receipt("D-cancelled", 1m, status: StorageTransactionsStatus.Cancelled);
        Receipt("E-legacy", 1m, situation: null);
        Receipt("F-transshipment", 1m, type: StorageTransactionType.TransshipmentReceipt);
        Receipt("G-purchase", 1m, type: StorageTransactionType.Purchase);
        Receipt("H-corn", 1m, item: "MILHO");
        await _db.SaveChangesAsync();

        var codes = (await RunAsync()).Select(x => x.Code);

        Assert.Equal(["A-ok", "B-invoiced"], codes);
    }

    [Fact]
    public async Task Pending_only_hides_fully_written_off_receipts()
    {
        var st = Receipt("R1", 1_234.567m);
        WriteOff(st, 1_234.567m);
        await _db.SaveChangesAsync();

        Assert.Empty(await RunAsync());
        Assert.Single(await RunAsync(new RoyaltyBalanceFilter { PendingOnly = false }));
    }

    [Fact]
    public async Task Filters_by_situation_and_period()
    {
        Receipt("R1", 1m, RoyaltySituation.Declared);
        var tested = Receipt("R2", 1m, RoyaltySituation.Tested);
        tested.TransactionDate = new DateTime(2026, 10, 5);
        await _db.SaveChangesAsync();

        var rows = await RunAsync(new RoyaltyBalanceFilter
        {
            Situation = RoyaltySituation.Tested, FromDate = new DateTime(2026, 10, 5), ToDate = new DateTime(2026, 10, 5),
        });

        Assert.Equal(["R2"], rows.Select(x => x.Code));
    }

    [Fact]
    public async Task Totals_group_by_situation()
    {
        Receipt("R1", 100m, RoyaltySituation.Declared);
        Receipt("R2", 50m, RoyaltySituation.Declared);
        var conv = Receipt("R3", 40m, RoyaltySituation.Conventional);
        WriteOff(conv, 10m);
        await _db.SaveChangesAsync();

        var totals = await RoyaltyBalanceQuery.TotalsAsync(
            RoyaltyBalanceQuery.Apply(RoyaltyBalanceQuery.Build(_db.Context), new RoyaltyBalanceFilter()));

        var declared = totals.Single(t => t.Situation == RoyaltySituation.Declared);
        Assert.Equal(150m, declared.Balance);
        Assert.Equal(2, declared.Count);
        Assert.Equal(30m, totals.Single(t => t.Situation == RoyaltySituation.Conventional).Balance);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~RoyaltyBalanceQueryTests"`
Expected: build FAIL (`RoyaltyBalanceQuery` não existe).

- [ ] **Step 3: Implement DTOs and the query**

```csharp
// SiagroB1.Domain/Dtos/RoyaltyBalanceRow.cs
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Romaneio de recebimento elegível ao controle de royalties, com o saldo a baixar no ITS.</summary>
public class RoyaltyBalanceRow
{
    public Guid StorageTransactionKey { get; set; }
    public string? Code { get; set; }
    public DateTime? TransactionDate { get; set; }
    public string? BranchCode { get; set; }
    public string CardCode { get; set; } = "";
    public string? CardName { get; set; }
    public string? RoyaltyOriginCode { get; set; }
    public string? RoyaltyOriginName { get; set; }
    public string ItemCode { get; set; } = "";
    public string? ItemName { get; set; }
    public string? StorageAddressCode { get; set; }
    public RoyaltySituation Situation { get; set; }
    public string? RoyaltyTestCode { get; set; }
    public decimal NetWeight { get; set; }
    public decimal WrittenOffWeight { get; set; }
    public decimal Balance { get; set; }
}

public class RoyaltyBalanceTotal
{
    public RoyaltySituation Situation { get; set; }
    public int Count { get; set; }
    public decimal NetWeight { get; set; }
    public decimal WrittenOffWeight { get; set; }
    public decimal Balance { get; set; }
}
```

```csharp
// SiagroB1.Domain/Dtos/RoyaltyBalanceFilter.cs
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Filtros comuns da lista de pendentes, dos totais e do PDF de royalties ITS.</summary>
public class RoyaltyBalanceFilter
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? CardCode { get; set; }
    public string? OriginCode { get; set; }
    public string? ItemCode { get; set; }
    public RoyaltySituation? Situation { get; set; }
    public string? StorageAddressCode { get; set; }
    /// <summary>Só romaneios com saldo &gt; 0 (padrão).</summary>
    public bool PendingOnly { get; set; } = true;
}
```

```csharp
// SiagroB1.Infra/Queries/RoyaltyBalanceQuery.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Saldo a baixar no ITS por romaneio. Mora no Infra porque o Web (OData) e o Reports (PDF)
/// precisam da MESMA regra de elegibilidade, e o Reports não referencia o Application.
/// Saldo calculado (peso líquido − linhas de baixas ativas), não persistido.
/// </summary>
public static class RoyaltyBalanceQuery
{
    public static IQueryable<RoyaltyBalanceRow> Build(AppDbContext ctx) =>
        from st in ctx.StorageTransactions.AsNoTracking()
        where st.TransactionType == StorageTransactionType.Receipt
              && (st.TransactionStatus == StorageTransactionsStatus.Confirmed ||
                  st.TransactionStatus == StorageTransactionsStatus.Invoiced)
              // Recebimentos anteriores ao recurso não têm situação: ficam fora.
              && st.RoyaltySituation != null
              && ctx.ItemComplements.Any(c => c.ItemCode == st.ItemCode && c.ControlsRoyalties)
        let writtenOff = ctx.RoyaltyWriteOffLines
            .Where(l => l.StorageTransactionKey == st.Key &&
                        l.RoyaltyWriteOff!.Status == RoyaltyWriteOffStatus.Active)
            .Sum(l => (decimal?)l.Weight) ?? 0m
        select new RoyaltyBalanceRow
        {
            StorageTransactionKey = st.Key,
            Code = st.Code,
            TransactionDate = st.TransactionDate,
            BranchCode = st.BranchCode,
            CardCode = st.CardCode,
            CardName = st.CardName,
            RoyaltyOriginCode = st.RoyaltyOriginCode,
            RoyaltyOriginName = st.RoyaltyOriginName,
            ItemCode = st.ItemCode,
            ItemName = st.ItemName,
            StorageAddressCode = st.StorageAddressCode,
            Situation = st.RoyaltySituation!.Value,
            RoyaltyTestCode = st.RoyaltyTestCode,
            NetWeight = st.NetWeight,
            WrittenOffWeight = writtenOff,
            Balance = st.NetWeight - writtenOff,
        };

    public static IQueryable<RoyaltyBalanceRow> Apply(IQueryable<RoyaltyBalanceRow> query, RoyaltyBalanceFilter filter)
    {
        if (filter.PendingOnly)
            query = query.Where(x => x.Balance > 0);
        if (filter.FromDate is { } from)
            query = query.Where(x => x.TransactionDate >= from.Date);
        if (filter.ToDate is { } to)
        {
            var toExclusive = to.Date.AddDays(1);
            query = query.Where(x => x.TransactionDate < toExclusive);
        }
        if (!string.IsNullOrWhiteSpace(filter.CardCode))
            query = query.Where(x => x.CardCode == filter.CardCode);
        if (!string.IsNullOrWhiteSpace(filter.OriginCode))
            query = query.Where(x => x.RoyaltyOriginCode == filter.OriginCode);
        if (!string.IsNullOrWhiteSpace(filter.ItemCode))
            query = query.Where(x => x.ItemCode == filter.ItemCode);
        if (filter.Situation is { } situation)
            query = query.Where(x => x.Situation == situation);
        if (!string.IsNullOrWhiteSpace(filter.StorageAddressCode))
            query = query.Where(x => x.StorageAddressCode == filter.StorageAddressCode);
        return query;
    }

    public static Task<List<RoyaltyBalanceTotal>> TotalsAsync(IQueryable<RoyaltyBalanceRow> query) =>
        query.GroupBy(x => x.Situation)
            .Select(g => new RoyaltyBalanceTotal
            {
                Situation = g.Key,
                Count = g.Count(),
                NetWeight = g.Sum(x => x.NetWeight),
                WrittenOffWeight = g.Sum(x => x.WrittenOffWeight),
                Balance = g.Sum(x => x.Balance),
            })
            .OrderBy(x => x.Situation)
            .ToListAsync();
}
```

- [ ] **Step 4: Run query tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~RoyaltyBalanceQueryTests"` → 5 PASS.

- [ ] **Step 5: OData surface**

```csharp
// SiagroB1.Web/Controllers/RoyaltyTransactionBalancesController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Dtos;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Saldo de royalties (ITS) por romaneio de recebimento — com e sem saldo; a aba Pendentes
/// filtra <c>Balance gt 0</c>. Somente leitura.
/// </summary>
public class RoyaltyTransactionBalancesController(IUnitOfWork unitOfWork) : ODataController
{
    [EnableQuery(MaxTop = 5000)]
    public ActionResult<IEnumerable<RoyaltyBalanceRow>> Get() =>
        Ok(RoyaltyBalanceQuery.Build(unitOfWork.Context));

    [EnableQuery]
    public SingleResult<RoyaltyBalanceRow> Get([FromRoute] Guid key) =>
        SingleResult.Create(RoyaltyBalanceQuery.Build(unitOfWork.Context).Where(x => x.StorageTransactionKey == key));
}
```

```csharp
// SiagroB1.Web/Functions/Royalties/RoyaltyBalanceTotalsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Web.Functions.Royalties;

/// <summary>Totais por situação dos pendentes, com os mesmos filtros da tela.</summary>
public class RoyaltyBalanceTotalsController(IUnitOfWork unitOfWork) : ODataController
{
    [HttpGet("odata/RoyaltyBalanceTotals(FromDate={fromDate},ToDate={toDate},CardCode={cardCode},OriginCode={originCode},ItemCode={itemCode},Situation={situation},StorageAddressCode={storageAddressCode})")]
    public async Task<IActionResult> Get(
        string? fromDate, string? toDate, string? cardCode, string? originCode,
        string? itemCode, string? situation, string? storageAddressCode)
    {
        var filter = new RoyaltyBalanceFilter
        {
            FromDate = ParseDate(fromDate),
            ToDate = ParseDate(toDate),
            CardCode = Clean(cardCode),
            OriginCode = Clean(originCode),
            ItemCode = Clean(itemCode),
            Situation = Enum.TryParse<RoyaltySituation>(Clean(situation), out var s) ? s : null,
            StorageAddressCode = Clean(storageAddressCode),
        };

        var totals = await RoyaltyBalanceQuery.TotalsAsync(
            RoyaltyBalanceQuery.Apply(RoyaltyBalanceQuery.Build(unitOfWork.Context), filter));
        return Ok(totals);
    }

    // Os parâmetros chegam como literal OData: 'texto' (com aspas) ou null.
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "null") return null;
        var trimmed = value.Trim('\'');
        return trimmed.Length == 0 ? null : Uri.UnescapeDataString(trimmed).Replace("''", "'");
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParseExact(Clean(value), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d)
            ? d : null;
}
```

`ODataConfigurations.cs`, junto dos entity sets de armazenagem (linha ~45) e das functions de itens (linha ~440):

```csharp
        var royaltyBalances = modelBuilder.EntitySet<RoyaltyBalanceRow>("RoyaltyTransactionBalances");
        royaltyBalances.EntityType.HasKey(x => x.StorageTransactionKey);
```

```csharp
        // Parâmetros como string: datas yyyy-MM-dd e enum pelo nome; vazio/null = sem filtro.
        var royaltyBalanceTotals = modelBuilder.Function("RoyaltyBalanceTotals");
        royaltyBalanceTotals.Parameter<string>("FromDate").Optional();
        royaltyBalanceTotals.Parameter<string>("ToDate").Optional();
        royaltyBalanceTotals.Parameter<string>("CardCode").Optional();
        royaltyBalanceTotals.Parameter<string>("OriginCode").Optional();
        royaltyBalanceTotals.Parameter<string>("ItemCode").Optional();
        royaltyBalanceTotals.Parameter<string>("Situation").Optional();
        royaltyBalanceTotals.Parameter<string>("StorageAddressCode").Optional();
        royaltyBalanceTotals.ReturnsCollection<RoyaltyBalanceTotal>();
```

- [ ] **Step 6: Smoke the endpoints**

Run: `dotnet build SiagroB1.sln` → 0 erros. Subir Web e Gateway com o profile local da sua memória `run-stack-locally` (ex.: `ASPNETCORE_ENVIRONMENT=Yokotobi-Development`) **somente se o banco local já tiver a migration da Task 1 aplicada** (banco de DEV, nunca HOM/PRD); senão, adie esta verificação para a Task 17. Conferir:
- `GET /odata/$metadata` contém `RoyaltyTransactionBalances` e `RoyaltyBalanceTotals`.
- `GET /odata/RoyaltyTransactionBalances?$filter=Balance gt 0 and Situation eq 'Declared'&$count=true` → 200.
- `GET /odata/RoyaltyBalanceTotals(FromDate=null,ToDate=null,CardCode=null,OriginCode=null,ItemCode=null,Situation='Declared',StorageAddressCode=null)` → 200 com array.

Se a rota da function der 404, comparar com a rota de outra function com vários parâmetros em `SiagroB1.Web/Functions` e alinhar o template.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Domain/Dtos/RoyaltyBalanceRow.cs SiagroB1.Domain/Dtos/RoyaltyBalanceFilter.cs \
  SiagroB1.Infra/Queries/RoyaltyBalanceQuery.cs SiagroB1.Web/Controllers/RoyaltyTransactionBalancesController.cs \
  SiagroB1.Web/Functions/Royalties/RoyaltyBalanceTotalsController.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs \
  SiagroB1.Application.Tests/Royalties/RoyaltyBalanceQueryTests.cs
git commit -F- <<'EOF'
feat(storage): saldo de royalties a baixar no ITS por romaneio

Consulta única no Infra (Web e Reports usam a mesma regra): recebimentos
confirmados/faturados de produto controlado, com situação apurada; saldo
= peso líquido menos as linhas de baixas ativas.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 6: Registrar baixa ITS

**Files:**
- Create: `SiagroB1.Commons/Validation/TaxIdValidator.cs`
- Create: `SiagroB1.Application/Services/RoyaltyWriteOffs/RoyaltyWriteOffInput.cs`
- Create: `SiagroB1.Application/Services/RoyaltyWriteOffs/RoyaltyWriteOffsCreateService.cs`
- Create: `SiagroB1.Web/Actions/RoyaltyWriteOffs/RoyaltyWriteOffsCreateController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (perto da linha 525)
- Test: `SiagroB1.Application.Tests/Royalties/TaxIdValidatorTests.cs`, `SiagroB1.Application.Tests/Royalties/RoyaltyWriteOffsCreateServiceTests.cs`

**Interfaces:**
- Consumes: `RoyaltyBalanceQuery` (Task 5).
- Produces:

```csharp
public static class TaxIdValidator { static string Digits(string? value); static bool IsValid(string? value); } // SiagroB1.Commons.Validation
public record RoyaltyWriteOffLineInput(Guid StorageTransactionKey, decimal Weight);
public record RoyaltyWriteOffInput(DateTime WriteOffDate, string ItsId, string TaxId, string Name, string? Comments,
    IReadOnlyList<RoyaltyWriteOffLineInput> Lines);
public class RoyaltyWriteOffsCreateService(IUnitOfWork db) { Task<RoyaltyWriteOff> ExecuteAsync(RoyaltyWriteOffInput input, string userName); }
```
- OData action `RoyaltyWriteOffsCreate(WriteOffDate: string yyyy-MM-dd, ItsId, TaxId, Name, Comments?, StorageTransactionKeys: Guid[], Weights: double[])` → `{ Key, RowId }`.

- [ ] **Step 1: Write the failing tests**

```csharp
// SiagroB1.Application.Tests/Royalties/TaxIdValidatorTests.cs
using SiagroB1.Commons.Validation;

namespace SiagroB1.Application.Tests.Royalties;

public class TaxIdValidatorTests
{
    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11222333000181", true)]
    [InlineData("529.982.247-24", false)]
    [InlineData("111.111.111-11", false)]
    [InlineData("11.222.333/0001-80", false)]
    [InlineData("123", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_cpf_and_cnpj(string? value, bool expected) =>
        Assert.Equal(expected, TaxIdValidator.IsValid(value));
}
```

```csharp
// SiagroB1.Application.Tests/Royalties/RoyaltyWriteOffsCreateServiceTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.RoyaltyWriteOffs;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Royalties;

public class RoyaltyWriteOffsCreateServiceTests
{
    private const string ValidCpf = "529.982.247-25";
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    public RoyaltyWriteOffsCreateServiceTests() =>
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });

    private StorageTransaction Receipt(string code, decimal net, RoyaltySituation situation = RoyaltySituation.Declared,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed)
    {
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "P0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01", TransactionType = StorageTransactionType.Receipt, TransactionStatus = status,
            NetWeight = net, RoyaltySituation = situation, RoyaltyOriginCode = "P0001",
        };
        _db.Context.StorageTransactions.Add(st);
        return st;
    }

    private static RoyaltyWriteOffInput Input(string itsId, params (Guid Key, decimal Weight)[] lines) =>
        new(new DateTime(2026, 10, 10), itsId, ValidCpf, "Produtor Teste", null,
            lines.Select(l => new RoyaltyWriteOffLineInput(l.Key, l.Weight)).ToList());

    private RoyaltyWriteOffsCreateService Service() => new(_db);

    private decimal WrittenOff(Guid key) =>
        _db.Context.RoyaltyWriteOffLines
            .Where(l => l.StorageTransactionKey == key && l.RoyaltyWriteOff!.Status == RoyaltyWriteOffStatus.Active)
            .Sum(l => l.Weight);

    [Fact]
    public async Task Creates_a_write_off_covering_several_receipts()
    {
        var a = Receipt("R1", 30_000m);
        var b = Receipt("R2", 20_000m);
        await _db.SaveChangesAsync();

        var created = await Service().ExecuteAsync(Input("ITS-1", (a.Key, 30_000m), (b.Key, 5_000m)), "tester");

        var saved = await _db.Context.RoyaltyWriteOffs.Include(x => x.Lines).AsNoTracking().SingleAsync(x => x.Key == created.Key);
        Assert.Equal(RoyaltySituation.Declared, saved.Situation);
        Assert.Equal(RoyaltyWriteOffStatus.Active, saved.Status);
        Assert.Equal("52998224725", saved.TaxId);
        Assert.Equal("tester", saved.CreatedBy);
        Assert.Equal(2, saved.Lines.Count);
        Assert.Equal(5_000m, WrittenOff(b.Key));
    }

    [Fact]
    public async Task A_receipt_can_be_written_off_in_parts_down_to_zero_with_three_decimals()
    {
        var a = Receipt("R1", 1_234.567m);
        await _db.SaveChangesAsync();

        await Service().ExecuteAsync(Input("ITS-1", (a.Key, 1_000m)), "tester");
        await Service().ExecuteAsync(Input("ITS-2", (a.Key, 234.567m)), "tester");

        Assert.Equal(1_234.567m, WrittenOff(a.Key));
    }

    [Fact]
    public async Task Refuses_weight_above_the_balance()
    {
        var a = Receipt("R1", 1_000m);
        await _db.SaveChangesAsync();
        await Service().ExecuteAsync(Input("ITS-1", (a.Key, 600m)), "tester");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-2", (a.Key, 500m)), "tester"));

        Assert.Contains("excede o saldo do romaneio R1", ex.Message);
        Assert.Equal(600m, WrittenOff(a.Key));
    }

    [Fact]
    public async Task Refuses_the_same_receipt_twice_in_one_write_off()
    {
        var a = Receipt("R1", 1_000m);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-1", (a.Key, 600m), (a.Key, 400m)), "tester"));

        Assert.Contains("mais de uma vez", ex.Message);
    }

    [Fact]
    public async Task Refuses_mixed_situations()
    {
        var a = Receipt("R1", 1_000m, RoyaltySituation.Declared);
        var b = Receipt("R2", 1_000m, RoyaltySituation.Conventional);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-1", (a.Key, 1m), (b.Key, 1m)), "tester"));

        Assert.Equal("Selecione romaneios de uma mesma situação.", ex.Message);
    }

    [Fact]
    public async Task Refuses_a_receipt_that_is_not_eligible()
    {
        var pending = Receipt("R1", 1_000m, status: StorageTransactionsStatus.Pending);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-1", (pending.Key, 1m)), "tester"));

        Assert.Contains("não está apto", ex.Message);
    }

    [Fact]
    public async Task Refuses_an_its_id_already_used_by_an_active_write_off()
    {
        var a = Receipt("R1", 1_000m);
        await _db.SaveChangesAsync();
        await Service().ExecuteAsync(Input("ITS-1", (a.Key, 100m)), "tester");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-1", (a.Key, 100m)), "tester"));

        Assert.Contains("ID do ITS ITS-1 já utilizado", ex.Message);
    }

    [Theory]
    [InlineData("", ValidCpf, "Nome", "Informe o ID do ITS.")]
    [InlineData("ITS-1", "123", "Nome", "CPF/CNPJ inválido.")]
    [InlineData("ITS-1", ValidCpf, " ", "Informe o nome.")]
    public async Task Validates_the_header(string itsId, string taxId, string name, string message)
    {
        var a = Receipt("R1", 1_000m);
        await _db.SaveChangesAsync();
        var input = new RoyaltyWriteOffInput(DateTime.Today, itsId, taxId, name, null,
            [new RoyaltyWriteOffLineInput(a.Key, 1m)]);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(input, "tester"));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task Refuses_an_empty_write_off_and_non_positive_weights()
    {
        var a = Receipt("R1", 1_000m);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(() => Service().ExecuteAsync(Input("ITS-1"), "tester"));
        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Input("ITS-1", (a.Key, 0m)), "tester"));
        Assert.Contains("maior que zero", ex.Message);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~TaxIdValidatorTests|FullyQualifiedName~RoyaltyWriteOffsCreateServiceTests"`
Expected: build FAIL.

- [ ] **Step 3: Implement validator, input and service**

```csharp
// SiagroB1.Commons/Validation/TaxIdValidator.cs
namespace SiagroB1.Commons.Validation;

/// <summary>Validação de CPF (11 dígitos) e CNPJ (14 dígitos) pelos dígitos verificadores.</summary>
public static class TaxIdValidator
{
    public static string Digits(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    public static bool IsValid(string? value)
    {
        var d = Digits(value);
        if (d.Distinct().Count() == 1) return false;
        return d.Length switch
        {
            11 => Check(d, 9, [10, 9, 8, 7, 6, 5, 4, 3, 2]) && Check(d, 10, [11, 10, 9, 8, 7, 6, 5, 4, 3, 2]),
            14 => Check(d, 12, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]) && Check(d, 13, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]),
            _ => false,
        };
    }

    private static bool Check(string digits, int position, int[] weights)
    {
        var sum = weights.Select((w, i) => (digits[i] - '0') * w).Sum();
        var rest = sum % 11;
        var expected = rest < 2 ? 0 : 11 - rest;
        return digits[position] - '0' == expected;
    }
}
```

```csharp
// SiagroB1.Application/Services/RoyaltyWriteOffs/RoyaltyWriteOffInput.cs
namespace SiagroB1.Application.Services.RoyaltyWriteOffs;

public record RoyaltyWriteOffLineInput(Guid StorageTransactionKey, decimal Weight);

public record RoyaltyWriteOffInput(
    DateTime WriteOffDate,
    string ItsId,
    string TaxId,
    string Name,
    string? Comments,
    IReadOnlyList<RoyaltyWriteOffLineInput> Lines);
```

```csharp
// SiagroB1.Application/Services/RoyaltyWriteOffs/RoyaltyWriteOffsCreateService.cs
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Commons.Validation;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.RoyaltyWriteOffs;

/// <summary>
/// Registra no SiagroB1 a baixa feita no site do ITS. Uma baixa cobre um ou mais romaneios de
/// recebimento da MESMA situação, cada um com peso ≤ saldo (baixa parcial permitida).
/// </summary>
public class RoyaltyWriteOffsCreateService(IUnitOfWork db)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<RoyaltyWriteOff> ExecuteAsync(RoyaltyWriteOffInput input, string userName)
    {
        var itsId = input.ItsId?.Trim() ?? "";
        var name = input.Name?.Trim() ?? "";
        if (itsId.Length == 0) throw new ApplicationException("Informe o ID do ITS.");
        if (!TaxIdValidator.IsValid(input.TaxId)) throw new ApplicationException("CPF/CNPJ inválido.");
        if (name.Length == 0) throw new ApplicationException("Informe o nome.");
        if (input.Lines is not { Count: > 0 }) throw new ApplicationException("Selecione ao menos um romaneio.");
        if (input.Lines.Any(l => l.Weight <= 0))
            throw new ApplicationException("O peso baixado deve ser maior que zero.");
        if (input.Lines.GroupBy(l => l.StorageTransactionKey).Any(g => g.Count() > 1))
            throw new ApplicationException("Um romaneio não pode aparecer mais de uma vez na mesma baixa.");

        try
        {
            await db.BeginTransactionAsync();

            var keys = input.Lines.Select(l => l.StorageTransactionKey).ToList();
            var balances = await RoyaltyBalanceQuery.Build(db.Context)
                .Where(x => keys.Contains(x.StorageTransactionKey))
                .ToDictionaryAsync(x => x.StorageTransactionKey);

            var missing = keys.FirstOrDefault(k => !balances.ContainsKey(k));
            if (missing != Guid.Empty)
            {
                var code = await db.Context.StorageTransactions.Where(x => x.Key == missing)
                    .Select(x => x.Code).FirstOrDefaultAsync();
                throw new ApplicationException($"Romaneio {code ?? missing.ToString()} não está apto para baixa ITS.");
            }

            var situations = balances.Values.Select(x => x.Situation).Distinct().ToList();
            if (situations.Count > 1)
                throw new ApplicationException("Selecione romaneios de uma mesma situação.");

            foreach (var line in input.Lines)
            {
                var row = balances[line.StorageTransactionKey];
                if (line.Weight > row.Balance)
                    throw new ApplicationException(
                        $"O peso baixado excede o saldo do romaneio {row.Code} (saldo {row.Balance.ToString("N3", PtBr)} kg).");
            }

            var duplicated = await db.Context.RoyaltyWriteOffs
                .Where(x => x.ItsId == itsId && x.Status == RoyaltyWriteOffStatus.Active)
                .Select(x => (int?)x.RowId)
                .FirstOrDefaultAsync();
            if (duplicated != null)
                throw new ApplicationException($"ID do ITS {itsId} já utilizado na baixa {duplicated}.");

            var writeOff = new RoyaltyWriteOff
            {
                WriteOffDate = input.WriteOffDate.Date,
                ItsId = itsId,
                TaxId = TaxIdValidator.Digits(input.TaxId),
                Name = name,
                Situation = situations[0],
                Status = RoyaltyWriteOffStatus.Active,
                Comments = string.IsNullOrWhiteSpace(input.Comments) ? null : input.Comments.Trim(),
                CreatedBy = userName,
                CreatedAt = DateTime.Now,
                UpdatedBy = userName,
                UpdatedAt = DateTime.Now,
            };
            foreach (var line in input.Lines)
                writeOff.Lines.Add(new RoyaltyWriteOffLine
                {
                    StorageTransactionKey = line.StorageTransactionKey,
                    Weight = line.Weight,
                });

            await db.Context.RoyaltyWriteOffs.AddAsync(writeOff);
            await db.SaveChangesAsync();
            await db.CommitAsync();
            return writeOff;
        }
        catch (DbUpdateException)
        {
            // Corrida com outra baixa do mesmo ID: o índice único filtrado é a trava final.
            await db.RollbackAsync();
            throw new ApplicationException($"ID do ITS {itsId} já utilizado em outra baixa ativa.");
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            throw e as ApplicationException ?? new ApplicationException(e.Message);
        }
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~TaxIdValidatorTests|FullyQualifiedName~RoyaltyWriteOffsCreateServiceTests"` → PASS.

- [ ] **Step 5: Action, EDM and DI**

```csharp
// SiagroB1.Web/Actions/RoyaltyWriteOffs/RoyaltyWriteOffsCreateController.cs
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.RoyaltyWriteOffs;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.RoyaltyWriteOffs;

public class RoyaltyWriteOffsCreateController(RoyaltyWriteOffsCreateService service) : ODataController
{
    /// <remarks>
    /// <paramref name="parameters"/> vem NULO sem nenhum parâmetro do EDM, e opcionais chegam nulos:
    /// nada de <c>.ToString()</c> direto. Pesos chegam como double (o UI5 manda Edm.Decimal como string).
    /// </remarks>
    [HttpPost("odata/RoyaltyWriteOffsCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null ||
                !parameters.TryGetValue("WriteOffDate", out var dateObj) ||
                !DateTime.TryParseExact(dateObj?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var writeOffDate))
                return BadRequest("Informe a data da baixa.");

            parameters.TryGetValue("ItsId", out var itsIdObj);
            parameters.TryGetValue("TaxId", out var taxIdObj);
            parameters.TryGetValue("Name", out var nameObj);
            parameters.TryGetValue("Comments", out var commentsObj);
            parameters.TryGetValue("StorageTransactionKeys", out var keysObj);
            parameters.TryGetValue("Weights", out var weightsObj);

            var keys = (keysObj as IEnumerable<Guid>)?.ToList() ?? [];
            var weights = (weightsObj as IEnumerable<double>)?.ToList() ?? [];
            if (keys.Count != weights.Count)
                return BadRequest("Romaneios e pesos não conferem.");

            var input = new RoyaltyWriteOffInput(
                writeOffDate,
                itsIdObj?.ToString() ?? "",
                taxIdObj?.ToString() ?? "",
                nameObj?.ToString() ?? "",
                commentsObj?.ToString(),
                keys.Select((k, i) => new RoyaltyWriteOffLineInput(k, Math.Round((decimal)weights[i], 3))).ToList());

            var userName = User.Identity?.Name ?? "Unknown";
            var writeOff = await service.ExecuteAsync(input, userName);
            return Ok(new { writeOff.Key, writeOff.RowId });
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);
            if (e is ApplicationException)
                return BadRequest(e.Message);
            return StatusCode(500, e.Message);
        }
    }
}
```

EDM (junto das actions de `StorageEntryTransactions`, linha ~524):

```csharp
        var royaltyWriteOffsCreate = modelBuilder.Action("RoyaltyWriteOffsCreate");
        royaltyWriteOffsCreate.Parameter<string>("WriteOffDate");
        royaltyWriteOffsCreate.Parameter<string>("ItsId");
        royaltyWriteOffsCreate.Parameter<string>("TaxId");
        royaltyWriteOffsCreate.Parameter<string>("Name");
        royaltyWriteOffsCreate.Parameter<string>("Comments").Optional();
        royaltyWriteOffsCreate.CollectionParameter<Guid>("StorageTransactionKeys");
        // double, não decimal: o UI5 v4 serializa Edm.Decimal como string.
        royaltyWriteOffsCreate.CollectionParameter<double>("Weights");
        royaltyWriteOffsCreate.Returns<IActionResult>();
```

DI (`ServiceCollectionExtensions.cs`, depois do bloco de storage entry transactions):

```csharp
        // royalties (baixas no ITS)
        services.AddScoped<RoyaltyWriteOffsCreateService>();
```

com `using SiagroB1.Application.Services.RoyaltyWriteOffs;` no topo.

- [ ] **Step 6: Build + full tests**

Run: `dotnet build SiagroB1.sln` → 0 erros; `dotnet test SiagroB1.Application.Tests` → verde.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Commons/Validation/TaxIdValidator.cs SiagroB1.Application/Services/RoyaltyWriteOffs \
  SiagroB1.Web/Actions/RoyaltyWriteOffs SiagroB1.Web/ODataConfig/ODataConfigurations.cs \
  SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs \
  SiagroB1.Application.Tests/Royalties/TaxIdValidatorTests.cs SiagroB1.Application.Tests/Royalties/RoyaltyWriteOffsCreateServiceTests.cs
git commit -F- <<'EOF'
feat(storage): registrar baixa de royalties feita no ITS

Uma baixa cobre vários romaneios de recebimento da mesma situação, com
peso parcial limitado ao saldo; ID do ITS único entre baixas ativas;
CPF/CNPJ validado pelos dígitos verificadores.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 7: Cancelar baixa + entity sets de consulta

**Files:**
- Create: `SiagroB1.Application/Services/RoyaltyWriteOffs/RoyaltyWriteOffsCancelService.cs`
- Create: `SiagroB1.Web/Actions/RoyaltyWriteOffs/RoyaltyWriteOffsCancelController.cs`
- Create: `SiagroB1.Web/Controllers/RoyaltyWriteOffsController.cs`
- Create: `SiagroB1.Web/Controllers/RoyaltyWriteOffLinesController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Royalties/RoyaltyWriteOffsCancelServiceTests.cs`

**Interfaces:**
- Produces: `RoyaltyWriteOffsCancelService.ExecuteAsync(Guid key, string userName)`; action `RoyaltyWriteOffsCancel(Key: Guid)`; entity sets `RoyaltyWriteOffs` (expand `Lines`) e `RoyaltyWriteOffLines` (expand `RoyaltyWriteOff`, `StorageTransaction`), somente leitura.

- [ ] **Step 1: Write the failing tests**

```csharp
// SiagroB1.Application.Tests/Royalties/RoyaltyWriteOffsCancelServiceTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.RoyaltyWriteOffs;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Tests.Royalties;

public class RoyaltyWriteOffsCancelServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly StorageTransaction _receipt;

    public RoyaltyWriteOffsCancelServiceTests()
    {
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        _receipt = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "R1", CardCode = "P0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01", TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed, NetWeight = 1_000m,
            RoyaltySituation = RoyaltySituation.Declared, RoyaltyOriginCode = "P0001",
        };
        _db.Context.StorageTransactions.Add(_receipt);
        _db.Context.SaveChanges();
    }

    private Task<RoyaltyWriteOff> CreateAsync(string itsId, decimal weight) =>
        new RoyaltyWriteOffsCreateService(_db).ExecuteAsync(
            new RoyaltyWriteOffInput(DateTime.Today, itsId, "52998224725", "Produtor", null,
                [new RoyaltyWriteOffLineInput(_receipt.Key, weight)]), "tester");

    private async Task<decimal> BalanceAsync() =>
        (await RoyaltyBalanceQuery.Build(_db.Context).SingleAsync(x => x.StorageTransactionKey == _receipt.Key)).Balance;

    [Fact]
    public async Task Cancel_returns_the_balance_and_records_who()
    {
        var w = await CreateAsync("ITS-1", 1_000m);

        await new RoyaltyWriteOffsCancelService(_db).ExecuteAsync(w.Key, "canceler");

        var saved = await _db.Context.RoyaltyWriteOffs.AsNoTracking().SingleAsync(x => x.Key == w.Key);
        Assert.Equal(RoyaltyWriteOffStatus.Cancelled, saved.Status);
        Assert.Equal("canceler", saved.CanceledBy);
        Assert.Equal(1_000m, await BalanceAsync());
    }

    [Fact]
    public async Task Cancel_frees_the_its_id()
    {
        var w = await CreateAsync("ITS-1", 500m);
        await new RoyaltyWriteOffsCancelService(_db).ExecuteAsync(w.Key, "canceler");

        await CreateAsync("ITS-1", 500m);

        Assert.Equal(500m, await BalanceAsync());
    }

    [Fact]
    public async Task Cancelling_twice_is_refused()
    {
        var w = await CreateAsync("ITS-1", 500m);
        await new RoyaltyWriteOffsCancelService(_db).ExecuteAsync(w.Key, "canceler");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => new RoyaltyWriteOffsCancelService(_db).ExecuteAsync(w.Key, "canceler"));

        Assert.Contains("já está cancelada", ex.Message);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~RoyaltyWriteOffsCancelServiceTests"` → build FAIL.

- [ ] **Step 3: Implement the service**

```csharp
// SiagroB1.Application/Services/RoyaltyWriteOffs/RoyaltyWriteOffsCancelService.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.RoyaltyWriteOffs;

/// <summary>Cancela a baixa (o ITS foi estornado/corrigido lá): o saldo volta aos romaneios.</summary>
public class RoyaltyWriteOffsCancelService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var writeOff = await db.Context.RoyaltyWriteOffs.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Baixa ITS não encontrada.");

        if (writeOff.Status == RoyaltyWriteOffStatus.Cancelled)
            throw new ApplicationException("Esta baixa ITS já está cancelada.");

        writeOff.Status = RoyaltyWriteOffStatus.Cancelled;
        writeOff.CanceledBy = userName;
        writeOff.CanceledAt = DateTime.Now;
        writeOff.UpdatedBy = userName;
        writeOff.UpdatedAt = DateTime.Now;

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Run tests** → `dotnet test ... --filter "FullyQualifiedName~RoyaltyWriteOffsCancelServiceTests"` PASS.

- [ ] **Step 5: Controllers, EDM, DI**

```csharp
// SiagroB1.Web/Actions/RoyaltyWriteOffs/RoyaltyWriteOffsCancelController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.RoyaltyWriteOffs;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.RoyaltyWriteOffs;

public class RoyaltyWriteOffsCancelController(RoyaltyWriteOffsCancelService service) : ODataController
{
    [HttpPost("odata/RoyaltyWriteOffsCancel")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Missing required parameters");

            await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!), User.Identity?.Name ?? "Unknown");
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

```csharp
// SiagroB1.Web/Controllers/RoyaltyWriteOffsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Baixas ITS — somente leitura; criar/cancelar só pelas actions.</summary>
public class RoyaltyWriteOffsController(IUnitOfWork unitOfWork) : ODataController
{
    [EnableQuery(MaxExpansionDepth = 3)]
    public ActionResult<IEnumerable<RoyaltyWriteOff>> Get() =>
        Ok(unitOfWork.Context.RoyaltyWriteOffs.AsQueryable());

    // Sem este overload o OData devolve 404 em /RoyaltyWriteOffs(key); SingleResult para o $expand.
    [EnableQuery(MaxExpansionDepth = 3)]
    public SingleResult<RoyaltyWriteOff> Get([FromRoute] Guid key) =>
        SingleResult.Create(unitOfWork.Context.RoyaltyWriteOffs.Where(x => x.Key == key));
}
```

```csharp
// SiagroB1.Web/Controllers/RoyaltyWriteOffLinesController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>Linhas de baixa ITS — consultadas pela seção "Baixas ITS" do romaneio.</summary>
public class RoyaltyWriteOffLinesController(IUnitOfWork unitOfWork) : ODataController
{
    [EnableQuery(MaxExpansionDepth = 2)]
    public ActionResult<IEnumerable<RoyaltyWriteOffLine>> Get() =>
        Ok(unitOfWork.Context.RoyaltyWriteOffLines.AsQueryable());
}
```

EDM:

```csharp
        modelBuilder.EntitySet<RoyaltyWriteOff>("RoyaltyWriteOffs");
        modelBuilder.EntitySet<RoyaltyWriteOffLine>("RoyaltyWriteOffLines");
```

```csharp
        var royaltyWriteOffsCancel = modelBuilder.Action("RoyaltyWriteOffsCancel");
        royaltyWriteOffsCancel.Parameter<Guid>("Key");
        royaltyWriteOffsCancel.Returns<IActionResult>();
```

DI: `services.AddScoped<RoyaltyWriteOffsCancelService>();` logo abaixo do Create.

- [ ] **Step 6: Build + full tests; smoke (se o banco de DEV já tem a migration)**

`dotnet build SiagroB1.sln`; `dotnet test SiagroB1.Application.Tests` verde. Smoke: `GET /odata/RoyaltyWriteOffs?$expand=Lines($expand=StorageTransaction($select=Code))` → 200. Se o `$expand` de `StorageTransaction` der 404/erro de navegação, declarar a navegação no EDM (`modelBuilder.EntityType<RoyaltyWriteOffLine>().HasRequired(x => x.StorageTransaction)`) — ver memória `odata-nav-and-late-property-routes-404`.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Application/Services/RoyaltyWriteOffs/RoyaltyWriteOffsCancelService.cs \
  SiagroB1.Web/Actions/RoyaltyWriteOffs/RoyaltyWriteOffsCancelController.cs \
  SiagroB1.Web/Controllers/RoyaltyWriteOffsController.cs SiagroB1.Web/Controllers/RoyaltyWriteOffLinesController.cs \
  SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs \
  SiagroB1.Application.Tests/Royalties/RoyaltyWriteOffsCancelServiceTests.cs
git commit -F- <<'EOF'
feat(storage): cancelar baixa ITS e consultar baixas e linhas

Cancelar devolve o saldo aos romaneios e libera o ID do ITS.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 8: Relatório PDF de pendentes

**Files:**
- Create: `SiagroB1.Reports/Dtos/RoyaltyPendingRequest.cs`
- Create: `SiagroB1.Reports/Dtos/RoyaltyPendingRowDto.cs`
- Create: `SiagroB1.Reports/Services/RoyaltyPendingReportService.cs` (registro automático pelo Scrutor — nome termina em `Service`)
- Create: `SiagroB1.Reports/Controllers/RoyaltyPendingController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/RoyaltyPending.frx`
- Modify: `SiagroB1.Application.Tests/Reports/InvoiceReportsPdfTests.cs`, `SiagroB1.Application.Tests/Reports/InvoiceReportsLayoutTests.cs`
- Test: `SiagroB1.Application.Tests/Reports/RoyaltyPendingReportServiceTests.cs`

**Interfaces:**
- Consumes: `RoyaltyBalanceQuery`, `RoyaltyBalanceFilter`, `RoyaltyBalanceRow` (Task 5).
- Produces: `POST /reports/RoyaltyPending` com corpo `RoyaltyPendingRequest { FromDate, ToDate, CardCode?, OriginCode?, ItemCode?, Situation? (nome do enum), StorageAddressCode? }` → PDF.

- [ ] **Step 1: Write the failing service tests**

```csharp
// SiagroB1.Application.Tests/Reports/RoyaltyPendingReportServiceTests.cs
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class RoyaltyPendingReportServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private void Receipt(string code, string card, RoyaltySituation situation, decimal net)
    {
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code, CardCode = card, CardName = "Nome " + card, ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "ARM01", TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed, TransactionDate = new DateTime(2026, 10, 1),
            NetWeight = net, RoyaltySituation = situation, RoyaltyOriginCode = card, RoyaltyOriginName = "Nome " + card,
        });
    }

    private static RoyaltyPendingReportService Service(IUnitOfWork db) =>
        new(db, null!, new ConfigurationBuilder().Build());

    private static RoyaltyPendingRequest Request() =>
        new() { FromDate = new DateTime(2026, 10, 1), ToDate = new DateTime(2026, 10, 31) };

    [Fact]
    public async Task Rows_are_grouped_by_situation_then_partner_with_subtotals()
    {
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        Receipt("R1", "P2", RoyaltySituation.Declared, 100m);
        Receipt("R2", "P1", RoyaltySituation.Declared, 50m);
        Receipt("R3", "P1", RoyaltySituation.Conventional, 30m);
        await _db.SaveChangesAsync();

        var data = await Service(_db).BuildAsync(Request());

        Assert.Equal(
            ["Item:R2", "Partner:", "Item:R1", "Partner:", "Situation:", "Item:R3", "Partner:", "Situation:"],
            data.Rows.Select(r => $"{r.Kind}:{r.Code}"));
        Assert.Equal(150m, data.Rows[4].Balance);   // subtotal Declarada
        Assert.Equal(180m, data.TotalBalance);
        Assert.Equal(3, data.ItemCount);
    }
}
```

- [ ] **Step 2: Run to verify failure** → build FAIL.

- [ ] **Step 3: Implement DTOs, service and controller**

```csharp
// SiagroB1.Reports/Dtos/RoyaltyPendingRequest.cs
namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Royalties ITS — Pendentes de Baixa" (período = data do romaneio).</summary>
public class RoyaltyPendingRequest
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string? CardCode { get; set; }
    public string? OriginCode { get; set; }
    public string? ItemCode { get; set; }
    /// <summary>Nome do membro de RoyaltySituation (ex.: "Declared"); vazio = todas.</summary>
    public string? Situation { get; set; }
    public string? StorageAddressCode { get; set; }
}
```

```csharp
// SiagroB1.Reports/Dtos/RoyaltyPendingRowDto.cs
namespace SiagroB1.Reports.Dtos;

public enum RoyaltyPendingRowKind
{
    Item = 0,
    /// <summary>Subtotal do parceiro dentro da situação.</summary>
    Partner = 1,
    /// <summary>Subtotal da situação.</summary>
    Situation = 2,
}

public class RoyaltyPendingRowDto
{
    public RoyaltyPendingRowKind Kind { get; set; }
    public string Situation { get; set; } = "";
    public string Code { get; set; } = "";
    public string Date { get; set; } = "";
    public string Partner { get; set; } = "";
    public string Origin { get; set; } = "";
    public string TestCode { get; set; } = "";
    public decimal NetWeight { get; set; }
    public decimal WrittenOff { get; set; }
    public decimal Balance { get; set; }
}

public record RoyaltyPendingData(List<RoyaltyPendingRowDto> Rows, int ItemCount,
    decimal TotalNetWeight, decimal TotalWrittenOff, decimal TotalBalance);
```

```csharp
// SiagroB1.Reports/Services/RoyaltyPendingReportService.cs
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Services;

public class RoyaltyPendingReportService(
    IUnitOfWork db,
    IFastReportService reportService,
    IConfiguration configuration)
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public static string Label(RoyaltySituation s) => s switch
    {
        RoyaltySituation.Declared => "Declarada",
        RoyaltySituation.Tested => "Testada",
        RoyaltySituation.Conventional => "Convencional",
        RoyaltySituation.ParticipantWarehouse => "Armazém Participante",
        _ => s.ToString(),
    };

    public async Task<RoyaltyPendingData> BuildAsync(RoyaltyPendingRequest request)
    {
        var filter = new RoyaltyBalanceFilter
        {
            FromDate = request.FromDate, ToDate = request.ToDate,
            CardCode = request.CardCode, OriginCode = request.OriginCode, ItemCode = request.ItemCode,
            Situation = Enum.TryParse<RoyaltySituation>(request.Situation, out var s) ? s : null,
            StorageAddressCode = request.StorageAddressCode,
        };
        var items = await RoyaltyBalanceQuery.Apply(RoyaltyBalanceQuery.Build(db.Context), filter).ToListAsync();

        var rows = new List<RoyaltyPendingRowDto>();
        foreach (var bySituation in items.GroupBy(x => x.Situation).OrderBy(g => g.Key))
        {
            foreach (var byPartner in bySituation
                         .GroupBy(x => (x.CardCode, x.CardName))
                         .OrderBy(g => g.Key.CardName ?? g.Key.CardCode, StringComparer.CurrentCultureIgnoreCase))
            {
                foreach (var x in byPartner.OrderBy(x => x.TransactionDate).ThenBy(x => x.Code, StringComparer.Ordinal))
                    rows.Add(new RoyaltyPendingRowDto
                    {
                        Kind = RoyaltyPendingRowKind.Item,
                        Situation = Label(x.Situation),
                        Code = x.Code ?? "",
                        Date = x.TransactionDate?.ToString("dd/MM/yyyy", Culture) ?? "",
                        Partner = $"{x.CardCode} - {x.CardName}",
                        Origin = x.RoyaltyOriginCode == null ? "" : $"{x.RoyaltyOriginCode} - {x.RoyaltyOriginName}",
                        TestCode = x.RoyaltyTestCode ?? "",
                        NetWeight = x.NetWeight, WrittenOff = x.WrittenOffWeight, Balance = x.Balance,
                    });
                rows.Add(Subtotal(RoyaltyPendingRowKind.Partner, $"Total {byPartner.Key.CardName ?? byPartner.Key.CardCode}", byPartner));
            }
            rows.Add(Subtotal(RoyaltyPendingRowKind.Situation, $"Total {Label(bySituation.Key)}", bySituation));
        }

        return new RoyaltyPendingData(rows, items.Count,
            items.Sum(x => x.NetWeight), items.Sum(x => x.WrittenOffWeight), items.Sum(x => x.Balance));
    }

    private static RoyaltyPendingRowDto Subtotal(RoyaltyPendingRowKind kind, string label, IEnumerable<RoyaltyBalanceRow> rows)
    {
        var list = rows.ToList();
        return new RoyaltyPendingRowDto
        {
            Kind = kind, Partner = label,
            NetWeight = list.Sum(x => x.NetWeight), WrittenOff = list.Sum(x => x.WrittenOffWeight),
            Balance = list.Sum(x => x.Balance),
        };
    }

    public async Task<byte[]> ExecuteAsync(RoyaltyPendingRequest request)
    {
        var data = await BuildAsync(request);

        var filters = new List<string>
        {
            $"Período: {request.FromDate:dd/MM/yyyy} a {request.ToDate:dd/MM/yyyy}",
        };
        if (Enum.TryParse<RoyaltySituation>(request.Situation, out var s)) filters.Add($"Situação: {Label(s)}");
        if (!string.IsNullOrWhiteSpace(request.CardCode)) filters.Add($"Parceiro: {request.CardCode}");
        if (!string.IsNullOrWhiteSpace(request.OriginCode)) filters.Add($"Origem: {request.OriginCode}");
        if (!string.IsNullOrWhiteSpace(request.ItemCode)) filters.Add($"Produto: {request.ItemCode}");
        if (!string.IsNullOrWhiteSpace(request.StorageAddressCode)) filters.Add($"Lote: {request.StorageAddressCode}");

        var parameters = new Dictionary<string, object>
        {
            ["pFilters"] = string.Join("  |  ", filters),
            [FastReportService.StandaloneParameter] = ErpMode.IsStandalone(configuration),
            ["pItemCount"] = data.ItemCount,
            ["pTotalNetWeight"] = data.TotalNetWeight,
            ["pTotalWrittenOff"] = data.TotalWrittenOff,
            ["pTotalBalance"] = data.TotalBalance,
        };

        return await reportService.GeneratePdfAsync(
            "RoyaltyPending.frx", data.Rows, "RoyaltyRows", "RoyaltyRows", parameters);
    }
}
```

> `ErpMode` e `FastReportService.StandaloneParameter` são os mesmos usados por `FunruralWithholdingReportService` — copie de lá os `using` que faltarem.

```csharp
// SiagroB1.Reports/Controllers/RoyaltyPendingController.cs
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/RoyaltyPending")]
public class RoyaltyPendingController(RoyaltyPendingReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] RoyaltyPendingRequest request)
    {
        if (ReportText.ValidatePeriod(request.FromDate, request.ToDate, "Informe o período de entrada.") is { } error)
            return BadRequest(error);

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"royalties-its-pendentes.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

- [ ] **Step 4: Create the template**

`SiagroB1.Reports/Reports/Templates/RoyaltyPending.frx` — copie a estrutura de `FunruralWithholding.frx` (paisagem A4, `PageHeaderBand` com `picLogo`/`txtCompany`/`txtDate`/`txtTitle`/`txtFilters`, `ColumnHeaderBand`, `DataBand`, `ReportSummaryBand`, `PageFooterBand` com `txtPage`) trocando o conteúdo:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Report ScriptLanguage="CSharp" ReportInfo.Created="10/10/2026 10:00:00" ReportInfo.Modified="10/10/2026 10:00:00" ReportInfo.CreatorVersion="2026.1.0.0">
  <Dictionary>
    <BusinessObjectDataSource Name="RoyaltyRows" ReferenceName="RoyaltyRows" DataType="System.Int32" Enabled="true">
      <Column Name="Kind" DataType="System.Int32"/>
      <Column Name="Situation" DataType="System.String"/>
      <Column Name="Code" DataType="System.String"/>
      <Column Name="Date" DataType="System.String"/>
      <Column Name="Partner" DataType="System.String"/>
      <Column Name="Origin" DataType="System.String"/>
      <Column Name="TestCode" DataType="System.String"/>
      <Column Name="NetWeight" DataType="System.Decimal"/>
      <Column Name="WrittenOff" DataType="System.Decimal"/>
      <Column Name="Balance" DataType="System.Decimal"/>
    </BusinessObjectDataSource>
    <Parameter Name="pCompanyName" DataType="System.String" AsString=""/>
    <Parameter Name="pFilters" DataType="System.String" AsString=""/>
    <Parameter Name="pStandalone" DataType="System.Boolean" AsString="true"/>
    <Parameter Name="pItemCount" DataType="System.Int32" AsString="0"/>
    <Parameter Name="pTotalNetWeight" DataType="System.Decimal" AsString="0"/>
    <Parameter Name="pTotalWrittenOff" DataType="System.Decimal" AsString="0"/>
    <Parameter Name="pTotalBalance" DataType="System.Decimal" AsString="0"/>
  </Dictionary>
  <ReportPage Name="Page1" Landscape="true" PaperWidth="297" PaperHeight="210" LeftMargin="5" TopMargin="5" RightMargin="5" BottomMargin="5" RawPaperSize="9" Watermark.Font="Arial, 60pt">
    <PageHeaderBand Name="PageHeader1" Width="1084" Height="113.4">
      <PictureObject Name="picLogo" Left="0" Top="2" Width="94.5" Height="37.8" SizeMode="Zoom"/>
      <TextObject Name="txtCompany" Left="100" Top="2" Width="700" Height="37.8" Text="[pCompanyName]" VertAlign="Center" Font="Arial, 11pt, style=Bold"/>
      <TextObject Name="txtDate" Left="937" Top="2" Width="147" Height="18.9" Text="[Date]" Format="Date" Format.Format="d" HorzAlign="Right" VertAlign="Center" Font="Tahoma, 6pt"/>
      <TextObject Name="txtTitle" Top="45" Width="1084" Height="28.35" Text="Royalties ITS — Pendentes de Baixa" HorzAlign="Center" VertAlign="Center" Font="Arial, 14pt, style=Bold, Italic"/>
      <TextObject Name="txtFilters" Top="75" Width="1084" Height="37.8" Text="[pFilters]" HorzAlign="Center" VertAlign="Top" WordWrap="true" Font="Consolas, 8pt, style=Italic"/>
    </PageHeaderBand>
    <ColumnHeaderBand Name="ColumnHeader1" Top="116.6" Width="1084" Height="20" Border.Lines="Bottom">
      <TextObject Name="hdrSituation" Left="0" Width="110" Height="18" Text="Situação" VertAlign="Center" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrCode" Left="110" Width="90" Height="18" Text="Romaneio" VertAlign="Center" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrDate" Left="200" Width="62" Height="18" Text="Data" VertAlign="Center" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrPartner" Left="262" Width="230" Height="18" Text="Parceiro" VertAlign="Center" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrOrigin" Left="492" Width="230" Height="18" Text="Origem" VertAlign="Center" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrTestCode" Left="722" Width="80" Height="18" Text="Cód. Teste" VertAlign="Center" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrNetWeight" Left="802" Width="94" Height="18" Text="Peso Líquido" HorzAlign="Right" VertAlign="Center" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrWrittenOff" Left="896" Width="94" Height="18" Text="Baixado" HorzAlign="Right" VertAlign="Center" Font="Consolas, 7pt, style=Bold, Italic"/>
      <TextObject Name="hdrBalance" Left="990" Width="94" Height="18" Text="Saldo (kg)" HorzAlign="Right" VertAlign="Center" Font="Consolas, 7pt, style=Bold, Italic"/>
    </ColumnHeaderBand>
    <DataBand Name="Data1" Top="139.8" Width="1084" Height="17" CanGrow="true" DataSource="RoyaltyRows">
      <TextObject Name="txtSituation" Left="0" Width="110" Height="17" Text="[RoyaltyRows.Situation]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
      <TextObject Name="txtCode" Left="110" Width="90" Height="17" Text="[RoyaltyRows.Code]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
      <TextObject Name="txtDate" Left="200" Width="62" Height="17" Text="[RoyaltyRows.Date]" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
      <TextObject Name="txtPartner" Left="262" Width="230" Height="17" Text="[RoyaltyRows.Partner]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt">
        <Highlight><Condition Expression="[RoyaltyRows.Kind] != 0" Font="Consolas, 7pt, style=Bold" ApplyFont="true" ApplyTextFill="false"/></Highlight>
      </TextObject>
      <TextObject Name="txtOrigin" Left="492" Width="230" Height="17" Text="[RoyaltyRows.Origin]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
      <TextObject Name="txtTestCode" Left="722" Width="80" Height="17" Text="[RoyaltyRows.TestCode]" Trimming="EllipsisCharacter" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt"/>
      <TextObject Name="txtNetWeight" Left="802" Width="94" Height="17" Text="[RoyaltyRows.NetWeight]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt">
        <Highlight><Condition Expression="[RoyaltyRows.Kind] != 0" Font="Consolas, 7pt, style=Bold" ApplyFont="true" ApplyTextFill="false"/></Highlight>
      </TextObject>
      <TextObject Name="txtWrittenOff" Left="896" Width="94" Height="17" Text="[RoyaltyRows.WrittenOff]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt">
        <Highlight><Condition Expression="[RoyaltyRows.Kind] != 0" Font="Consolas, 7pt, style=Bold" ApplyFont="true" ApplyTextFill="false"/></Highlight>
      </TextObject>
      <TextObject Name="txtBalance" Left="990" Width="94" Height="17" Text="[RoyaltyRows.Balance]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" WordWrap="false" Font="Consolas, 7pt">
        <Highlight><Condition Expression="[RoyaltyRows.Kind] != 0" Font="Consolas, 7pt, style=Bold" ApplyFont="true" ApplyTextFill="false"/></Highlight>
      </TextObject>
    </DataBand>
    <ReportSummaryBand Name="ReportSummary1" Top="160" Width="1084" Height="40" Border.Lines="Top">
      <TextObject Name="txtSumCount" Left="0" Top="4" Width="400" Height="17" Text="[pItemCount] romaneio(s)" VertAlign="Center" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="txtSumNetWeight" Left="796" Top="4" Width="100" Height="17" Text="[pTotalNetWeight]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="txtSumWrittenOff" Left="896" Top="4" Width="94" Height="17" Text="[pTotalWrittenOff]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="txtSumBalance" Left="990" Top="4" Width="94" Height="17" Text="[pTotalBalance]" Format="Number" Format.UseLocale="true" Format.DecimalDigits="3" HorzAlign="Right" VertAlign="Center" Font="Consolas, 7pt, style=Bold"/>
      <TextObject Name="txtEmpty" Left="0" Top="22" Width="1084" Height="17" Text="[IIf([pItemCount] == 0, &quot;Nenhum registro encontrado.&quot;, &quot;&quot;)]" HorzAlign="Center" Font="Consolas, 8pt, style=Italic"/>
    </ReportSummaryBand>
    <PageFooterBand Name="PageFooter1" Top="204" Width="1084" Height="26">
      <TextObject Name="txtFooterNote" Width="900" Height="18.9" Text="Saldo = peso líquido do romaneio menos as baixas ITS ativas. Romaneios de recebimento confirmados de produtos que controlam royalties." VertAlign="Center" Font="Tahoma, 6pt"/>
      <TextObject Name="txtPage" Left="937" Width="147" Height="18.9" Text="Página [Page#] de [TotalPages#]" HorzAlign="Right" VertAlign="Center" Font="Tahoma, 6pt"/>
    </PageFooterBand>
  </ReportPage>
</Report>
```

> Se `FunruralWithholding.frx` tiver atributos de página/objeto diferentes destes (ex.: `ReportInfo`, posição do `PictureObject`), siga o Funrural — ele é a referência validada pelo `InvoiceReportsLayoutTests`.

- [ ] **Step 5: PDF + layout tests**

Em `InvoiceReportsLayoutTests.Templates`, acrescentar `"RoyaltyPending"`. Em `Budget(name)`: os totais (`txtSum*`) já caem na regra `NetWeight ? 15 : 14`; no `switch` de itens, acrescentar:

```csharp
            "txtWrittenOff" or "txtBalance" => 14,                       // 99.999.999,999
```

Em `InvoiceReportsPdfTests`, um teste ao lado do Funrural:

```csharp
    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task RoyaltyPending_ProducesAPdf(string erp)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "R1", CardCode = "P0001", CardName = "Produtor", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "ARM01", TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed, TransactionDate = Jul01,
            NetWeight = 31_234.567m, RoyaltySituation = RoyaltySituation.Tested, RoyaltyTestCode = "T-1",
            RoyaltyOriginCode = "P0001", RoyaltyOriginName = "Produtor",
        });
        await db.SaveChangesAsync();
        var configuration = Configuration(erp);

        var pdf = await new RoyaltyPendingReportService(db, FastReport(configuration), configuration)
            .ExecuteAsync(new RoyaltyPendingRequest { FromDate = Jul01, ToDate = Jul31 });

        Keep("RoyaltyPending", erp, pdf);
    }
```

(`Keep` já falha se o PDF vier vazio — confira; se não falhar, acrescente `Assert.NotEmpty(pdf);`.)

- [ ] **Step 6: Run report tests**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Reports"` → PASS (incluindo `NumericObjects_FitTheirLargestValue("RoyaltyPending")` e `Headers_FitTheirText("RoyaltyPending")`; se algum campo ficar estreito, alargar e reposicionar as colunas — total 1084). Abrir o PDF guardado por `Keep` e conferir visualmente (subtotais em negrito, colunas alinhadas). Ver memória `render-report-template-locally`.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Reports/Dtos/RoyaltyPendingRequest.cs SiagroB1.Reports/Dtos/RoyaltyPendingRowDto.cs \
  SiagroB1.Reports/Services/RoyaltyPendingReportService.cs SiagroB1.Reports/Controllers/RoyaltyPendingController.cs \
  SiagroB1.Reports/Reports/Templates/RoyaltyPending.frx SiagroB1.Application.Tests/Reports
git commit -F- <<'EOF'
feat(reports): relatório de royalties pendentes de baixa no ITS

Agrupa por situação e parceiro, com subtotais de peso líquido, baixado e
saldo; mesma consulta de saldo da tela.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 9: Item de menu "Royalties ITS"

**Files:**
- Create: `SiagroB1.Migrations/CommonContext/<timestamp>_AddRoyaltyWriteOffsMenu.cs` (gerada vazia e preenchida)

- [ ] **Step 1: Generate an empty migration**

Run: `dotnet ef migrations add AddRoyaltyWriteOffsMenu --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web -o CommonContext`
Expected: `Up`/`Down` vazios (nenhuma mudança de modelo). Se vier algo, parar — o snapshot do CommonContext estava sujo.

- [ ] **Step 2: Fill Up/Down**

```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "royaltyWriteOffs", "Royalties ITS", "sap-icon://folder-blank", true, false, 10, "storage", false });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { "8E4B2C71-6A3D-4F95-B1E8-2D7C9A0F5B63", "ADMIN", "royaltyWriteOffs" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValue: "8E4B2C71-6A3D-4F95-B1E8-2D7C9A0F5B63");

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: "royaltyWriteOffs");
        }
```

- [ ] **Step 3: Build** → `dotnet build SiagroB1.sln` 0 erros.

- [ ] **Step 4: Commit**

```bash
git add SiagroB1.Migrations/CommonContext/*AddRoyaltyWriteOffsMenu*
git commit -F- <<'EOF'
feat(storage): menu Royalties ITS em Armazenagem

DB: AddRoyaltyWriteOffsMenu

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

## Parte B — Frontend (`C:\Projetos\SiagroB1\siagro-b1-frontend`)

Antes da Task 10: `git -C C:\Projetos\SiagroB1\siagro-b1-frontend status` limpo e `git -C ... checkout -b feature/royalty-its-write-off` a partir de `main`. Labels pt-BR direto no XML (o projeto não usa i18n nas views).

### Task 10: Corrigir a chave "Convencional" do contrato de compra

**Files:**
- Modify: `webapp/view/purchaseContracts/fragments/PurchaseContractForm.fragment.xml:215`

- [ ] **Step 1: Fix the key**

```xml
            <core:ListItem key="Conventional" text="Convencional"/>
```

- [ ] **Step 2: Check** — `grep -rn "Conventinal" webapp` → nenhum resultado. `yarn ui5lint` sem erro novo.

- [ ] **Step 3: Commit (commit próprio, separado do recurso)**

```bash
git add webapp/view/purchaseContracts/fragments/PurchaseContractForm.fragment.xml
git commit -F- <<'EOF'
fix(purchase-contract): gravar o tipo de tecnologia Convencional

A chave do item era "Conventinal", que não existe no enum
TechnologyType (Conventional); escolher Convencional não gravava.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

A verificação no navegador (gravar contrato com Convencional e reabrir) entra na Task 17.

---

### Task 11: Rotas de servidor + checkbox no complemento do produto

**Files:**
- Modify: `webapp/model/ServerRoutes.ts`
- Modify: `webapp/view/produtos/Complement.view.xml` (depois do bloco da UoM comercial, antes do `<core:Title />` final, ~linha 57)
- Modify: `webapp/controller/produtos/Complement.controller.ts`

**Interfaces:**
- Produces (`ServerRoutes`): `royaltyWriteOffsCreate: '/RoyaltyWriteOffsCreate(...)'`, `royaltyWriteOffsCancel: '/RoyaltyWriteOffsCancel(...)'`, `royaltyBalanceTotals: '/RoyaltyBalanceTotals'`, `royaltyPendingReport: '/reports/RoyaltyPending'`.

- [ ] **Step 1: ServerRoutes** — junto de `itemsSetComplement` e de `funruralWithholdingReport`:

```ts
  royaltyWriteOffsCreate: '/RoyaltyWriteOffsCreate(...)',
  royaltyWriteOffsCancel: '/RoyaltyWriteOffsCancel(...)',
  royaltyBalanceTotals: '/RoyaltyBalanceTotals',
```

```ts
  royaltyPendingReport: '/reports/RoyaltyPending',
```

- [ ] **Step 2: View**

```xml
          <core:Title text="Royalties (ITS)" emphasized="true" />
          <Label text="Controla royalties" />
          <CheckBox id="controlsRoyalties" selected="{complement>/ControlsRoyalties}" />
          <Label text="" />
          <Text text="Marcado, os tickets e romaneios de entrada deste produto exigem a situação de royalty (Declarada, Testada, Convencional ou Armazém Participante) e entram no controle de baixas do ITS." />
```

- [ ] **Step 3: Controller** — `ComplementForm` ganha `ControlsRoyalties?: boolean`; no `setData` (linha ~44) `ControlsRoyalties: !!complement?.ControlsRoyalties,`; no `onSave`, junto dos outros `setParameter`:

```ts
    action.setParameter("ControlsRoyalties", !!model.getProperty("/ControlsRoyalties"));
```

e, depois do `invoke`, nada a mudar (o checkbox já reflete o model). No `onClear`, se ele zera os campos, **não** mexer em `ControlsRoyalties` (limpar é só da UoM comercial).

- [ ] **Step 4: Check** — `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint` → sem erros novos.

- [ ] **Step 5: Commit**

```bash
git add webapp/model/ServerRoutes.ts webapp/view/produtos/Complement.view.xml webapp/controller/produtos/Complement.controller.ts
git commit -F- <<'EOF'
feat(master-data): marcar no complemento se o produto controla royalties

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 12: Campos de royalty no ticket e no romaneio

**Files:**
- Create: `webapp/view/royalties/fragments/RoyaltyFields.fragment.xml`
- Create: `webapp/controller/royalties/RoyaltyFieldsHelper.ts`
- Modify: `webapp/view/weighingTicket/fragments/Form.fragment.xml` (depois do bloco "Frete/Remoção", antes de "Observações", ~linha 245)
- Modify: `webapp/view/storageTransactions/fragments/Form.fragment.xml` (depois do bloco "Frete/Remoção", ~linha 261)
- Modify: `webapp/controller/common/CommonController.ts` (novo handler ao lado de `openBusinessPartnersValueHelp`, linha ~372)
- Modify: controllers que ligam esses forms: `controller/weighingTicket/{Edit,Confirm,Detail}.controller.ts`, `controller/weighingTicket/completed/{Edit,Detail}.controller.ts`, `controller/storageTransactions/{Add,Edit,Detail}.controller.ts`

**Interfaces:**
- Produces: `RoyaltyFieldsHelper.apply(controller: CommonController, itemCode: string | undefined, editable: boolean): Promise<void>` — seta `ui>/royaltyVisible` (produto controla) e `ui>/royaltyEditable`. `CommonController.openRoyaltyOriginValueHelp(ev)`.

- [ ] **Step 1: Shared fragment** (filhos diretos do `f:content` da SimpleForm)

```xml
<!-- webapp/view/royalties/fragments/RoyaltyFields.fragment.xml -->
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core" xmlns:l="sap.ui.layout">
  <Label visible="{ui>/royaltyVisible}" text="Situação Royalty" required="true" />
  <Select
    visible="{ui>/royaltyVisible}"
    editable="{ui>/royaltyEditable}"
    forceSelection="false"
    selectedKey="{ path: 'RoyaltySituation', targetType: 'any' }">
    <core:ListItem key="Declared" text="Declarada" />
    <core:ListItem key="Tested" text="Testada" />
    <core:ListItem key="Conventional" text="Convencional" />
    <core:ListItem key="ParticipantWarehouse" text="Armazém Participante" />
    <layoutData>
      <l:GridData span="XL3 L3 M3 S8" />
    </layoutData>
  </Select>
  <Input
    visible="{ui>/royaltyVisible}"
    editable="{= ${ui>/royaltyEditable} === true &amp;&amp; ${ path: 'RoyaltySituation', targetType: 'any' } === 'Tested' }"
    required="{= ${ path: 'RoyaltySituation', targetType: 'any' } === 'Tested' }"
    placeholder="Código do teste"
    maxLength="50"
    value="{RoyaltyTestCode}" />

  <Label visible="{ui>/royaltyVisible}" text="Origem da Carga" required="true" />
  <Input
    visible="{ui>/royaltyVisible}"
    editable="{ui>/royaltyEditable}"
    showValueHelp="true"
    valueHelpOnly="true"
    valueHelpRequest=".openRoyaltyOriginValueHelp"
    value="{RoyaltyOriginCode}">
    <customData>
      <core:CustomData key="descriptionProperty" value="RoyaltyOriginName:CardName" />
    </customData>
    <layoutData>
      <l:GridData span="XL3 L3 M3 S8" />
    </layoutData>
  </Input>
  <Input visible="{ui>/royaltyVisible}" value="{RoyaltyOriginName}" editable="false" />
</core:FragmentDefinition>
```

> Atenção (memória `xml-comment-double-hyphen-kills-fragment`): nenhum `--` dentro de comentário XML.

- [ ] **Step 2: Include it in both forms**

Nos dois `Form.fragment.xml`, logo após o `core:Icon` do "Frete/Remoção":

```xml
  <core:Fragment fragmentName="siagrob1.view.royalties.fragments.RoyaltyFields" type="XML" />
```

- [ ] **Step 3: Helper + value help**

```ts
// webapp/controller/royalties/RoyaltyFieldsHelper.ts
import JSONModel from "sap/ui/model/json/JSONModel";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import type CommonController from "siagrob1/controller/common/CommonController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * Mostra os campos de royalty (ITS) só para produto com "Controla royalties" no complemento.
 * O backend valida de qualquer forma; aqui é só para não poluir a tela de milho.
 */
export default class RoyaltyFieldsHelper {
	static async apply(controller: CommonController, itemCode: string | undefined, editable: boolean): Promise<void> {
		const ui = controller.getView().getModel("ui") as JSONModel;
		ui.setProperty("/royaltyEditable", editable);
		ui.setProperty("/royaltyVisible", itemCode ? await RoyaltyFieldsHelper.controls(controller, itemCode) : false);
	}

	static async controls(controller: CommonController, itemCode: string): Promise<boolean> {
		const model = controller.getView().getModel() as ODataModel;
		const func = model.bindContext(ServerRoutes.itemsGetComplement);
		func.setParameter("ItemCode", itemCode);
		try {
			await func.invoke();
			return !!(func.getBoundContext().getObject() as { ControlsRoyalties?: boolean })?.ControlsRoyalties;
		} catch {
			return false; // produto sem complemento
		}
	}
}
```

`CommonController.ts`, ao lado de `openBusinessPartnersValueHelp`:

```ts
  openRoyaltyOriginValueHelp(ev: Input$ValueHelpRequestEvent) {
    void this.applyValueHelp(ev, "BusinessPartnersSelectDialog", ['CardCode', 'CardName', 'CardFName'], "CardCode");
  }
```

- [ ] **Step 4: Call the helper in each controller**

Em cada controller listado, depois do `bindElement(sPath)` (ou do `setBindingContext` no Add), quando o contexto estiver carregado — padrão:

```ts
		const ctx = this.getView().getBindingContext() as Context;
		await ctx?.requestObject();
		await RoyaltyFieldsHelper.apply(this, ctx?.getProperty("ItemCode") as string, <editable>);
```

com `<editable>` = o mesmo valor que o controller já usa para `ui>/editable` (no ticket `Edit`/`Confirm`/`completed/Edit`: `true`; nos `Detail`: `false`). No `storageTransactions/Add`, o produto é escolhido na tela: chamar `RoyaltyFieldsHelper.apply(this, itemCode, true)` também no handler que aplica o value help de produto (procure o `openItemsValueHelp`/handler de `ItemCode` do Add e encadeie depois do `applyValueHelp`). Se o controller já tem um `routeMatched` assíncrono, coloque a chamada no fim dele.

- [ ] **Step 5: Check** — `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint` sem erros novos.

- [ ] **Step 6: Commit**

```bash
git add webapp/view/royalties/fragments/RoyaltyFields.fragment.xml webapp/controller/royalties/RoyaltyFieldsHelper.ts \
  webapp/view/weighingTicket/fragments/Form.fragment.xml webapp/view/storageTransactions/fragments/Form.fragment.xml \
  webapp/controller/common/CommonController.ts webapp/controller/weighingTicket webapp/controller/storageTransactions
git commit -F- <<'EOF'
feat(weighing): situação de royalty, origem e código do teste no ticket e no romaneio

Campos visíveis só para produto que controla royalties; o nome da origem
é resolvido no backend pelo código.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 13: Campos de royalty na Entrada em armazenagem própria

**Files:**
- Modify: `webapp/controller/storageEntryTransaction/Create.controller.ts` (tipo `StorageEntryForm` linhas 35-55; `setData` linhas 128-150; `toPayload` linhas 176-195)
- Modify: `webapp/view/storageEntryTransaction/fragments/Form.fragment.xml` (ou `TransactionFields.fragment.xml` — o que contém os campos do romaneio de compra; inserir depois do último campo de dados do romaneio)

- [ ] **Step 1: Model** — `StorageTransaction` do tipo ganha:

```ts
    RoyaltySituation?: string,
    RoyaltyTestCode?: string,
    RoyaltyOriginCode?: string,
    RoyaltyOriginName?: string,
```

No `setData`, dentro de `StorageTransaction: { ... }`:

```ts
          RoyaltySituation: null,
          RoyaltyTestCode: null,
          RoyaltyOriginCode: data?.PurchaseContract?.CardCode,
          RoyaltyOriginName: data?.PurchaseContract?.CardName,
```

e, depois do `viewModel.setData(...)`, `ui>/royaltyVisible`:

```ts
      const uiModel = this.getModel("ui") as JSONModel;
      uiModel.setProperty("/royaltyVisible",
        await RoyaltyFieldsHelper.controls(this, data?.PurchaseContract?.ItemCode as string));
```

No `toPayload`, remover `RoyaltyOriginName` (o backend resolve) e converter vazios:

```ts
    delete oPayload.RoyaltyOriginName;
    if (!oPayload.RoyaltySituation) { oPayload.RoyaltySituation = null; }
    if (oPayload.RoyaltySituation !== "Tested") { oPayload.RoyaltyTestCode = null; }
```

No `onSave`, antes de montar a action, se `ui>/royaltyVisible` e faltar situação/origem (ou código em Testada): `MessageBox.warning("Informe a situação de royalty e a origem da carga.")` e `return`.

- [ ] **Step 2: View** — mesmo conteúdo do fragmento da Task 12, mas com bindings no JSONModel (`viewModel>/StorageTransaction/RoyaltySituation`, `.../RoyaltyTestCode`, `.../RoyaltyOriginCode`, `.../RoyaltyOriginName`), `editable="true"`, `visible="{ui>/royaltyVisible}"` e expressões de "Testada" usando `${viewModel>/StorageTransaction/RoyaltySituation}`. O `applyValueHelp` com JSONModel grava a descrição no caminho irmão (`/StorageTransaction/RoyaltyOriginName`) — mantenha o `descriptionProperty` igual ao da Task 12.

- [ ] **Step 3: Check** — `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint`.

- [ ] **Step 4: Commit**

```bash
git add webapp/controller/storageEntryTransaction/Create.controller.ts webapp/view/storageEntryTransaction/fragments
git commit -F- <<'EOF'
feat(storage): situação de royalty e origem na entrada em armazenagem própria

O recebimento da entrada nasce da cópia da compra e precisa da situação
para confirmar quando o produto controla royalties.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 14: Tela "Royalties ITS" — aba Pendentes (filtros, totais, PDF)

**Files:**
- Modify: `webapp/manifest.json` (rota perto das de armazenagem; target no objeto `targets`)
- Create: `webapp/view/royalties/Main.view.xml`
- Create: `webapp/view/royalties/fragments/Filterbar.fragment.xml`
- Create: `webapp/view/royalties/fragments/PendingTable.fragment.xml`
- Create: `webapp/controller/royalties/Main.controller.ts`

**Interfaces:**
- Consumes: entity set `RoyaltyTransactionBalances`, function `RoyaltyBalanceTotals`, `POST /reports/RoyaltyPending`, `ServerRoutes` (Task 11).
- Produces: rota `royaltyWriteOffs` (pattern `royalty-write-offs`), tabela `pendingTable` (seleção múltipla), model `filter`, model `totals`.

- [ ] **Step 1: manifest.json**

Rota:

```json
        { "pattern": "royalty-write-offs", "name": "royaltyWriteOffs", "target": "royaltyWriteOffs" },
```

Target:

```json
      "royaltyWriteOffs": {
        "id": "royaltyWriteOffs",
        "level": 1,
        "name": "siagrob1.view.royalties.Main",
        "clearControlAggregation": true
      },
```

- [ ] **Step 2: Views**

```xml
<!-- webapp/view/royalties/Main.view.xml -->
<mvc:View
  controllerName="siagrob1.controller.royalties.Main"
  displayBlock="true"
  xmlns="sap.m"
  xmlns:mvc="sap.ui.core.mvc"
  xmlns:core="sap.ui.core"
  xmlns:f="sap.f">
  <f:DynamicPage busyIndicatorDelay="0" busy="{ui>/busy}">
    <f:title>
      <f:DynamicPageTitle>
        <f:heading><Title text="Royalties ITS" /></f:heading>
      </f:DynamicPageTitle>
    </f:title>
    <f:header>
      <f:DynamicPageHeader>
        <f:content>
          <core:Fragment fragmentName="siagrob1.view.royalties.fragments.Filterbar" type="XML" />
        </f:content>
      </f:DynamicPageHeader>
    </f:header>
    <f:content>
      <IconTabBar id="royaltyTabs" expandable="false" stretchContentHeight="true" select=".onTabSelect">
        <items>
          <IconTabFilter key="pending" text="Pendentes">
            <content>
              <core:Fragment fragmentName="siagrob1.view.royalties.fragments.PendingTable" type="XML" />
            </content>
          </IconTabFilter>
          <IconTabFilter key="writeOffs" text="Baixas">
            <content>
              <core:Fragment fragmentName="siagrob1.view.royalties.fragments.WriteOffsTable" type="XML" />
            </content>
          </IconTabFilter>
        </items>
      </IconTabBar>
    </f:content>
  </f:DynamicPage>
</mvc:View>
```

> A aba "Baixas" referencia `WriteOffsTable`, criado na Task 15. Para esta task compilar e abrir, crie já o arquivo com um `core:FragmentDefinition` contendo só `<Text text="" />` e substitua na Task 15.

```xml
<!-- webapp/view/royalties/fragments/Filterbar.fragment.xml -->
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core" xmlns:fb="sap.ui.comp.filterbar">
  <fb:FilterBar id="royaltyFb" search=".onSearch" showClearOnFB="true" clear=".onClearFilters">
    <fb:filterGroupItems>
      <fb:FilterGroupItem name="fromDate" label="Entrada de" groupName="G" visibleInFilterBar="true">
        <fb:control>
          <DatePicker value="{ path: 'filter>/FromDate', type: 'sap.ui.model.type.String' }"
            displayFormat="dd/MM/yyyy" valueFormat="yyyy-MM-dd" />
        </fb:control>
      </fb:FilterGroupItem>
      <fb:FilterGroupItem name="toDate" label="Entrada até" groupName="G" visibleInFilterBar="true">
        <fb:control>
          <DatePicker value="{ path: 'filter>/ToDate', type: 'sap.ui.model.type.String' }"
            displayFormat="dd/MM/yyyy" valueFormat="yyyy-MM-dd" />
        </fb:control>
      </fb:FilterGroupItem>
      <fb:FilterGroupItem name="situation" label="Situação" groupName="G" visibleInFilterBar="true">
        <fb:control>
          <Select forceSelection="false" selectedKey="{filter>/Situation}">
            <core:ListItem key="" text="Todas" />
            <core:ListItem key="Declared" text="Declarada" />
            <core:ListItem key="Tested" text="Testada" />
            <core:ListItem key="Conventional" text="Convencional" />
            <core:ListItem key="ParticipantWarehouse" text="Armazém Participante" />
          </Select>
        </fb:control>
      </fb:FilterGroupItem>
      <fb:FilterGroupItem name="cardCode" label="Parceiro" groupName="G" visibleInFilterBar="true">
        <fb:control>
          <Input showClearIcon="true" showValueHelp="true" valueHelpRequest=".openBusinessPartnersValueHelp" value="{filter>/CardCode}" />
        </fb:control>
      </fb:FilterGroupItem>
      <fb:FilterGroupItem name="originCode" label="Origem" groupName="G" visibleInFilterBar="true">
        <fb:control>
          <Input showClearIcon="true" showValueHelp="true" valueHelpRequest=".openBusinessPartnersValueHelp" value="{filter>/OriginCode}" />
        </fb:control>
      </fb:FilterGroupItem>
      <fb:FilterGroupItem name="itemCode" label="Produto" groupName="G" visibleInFilterBar="true">
        <fb:control>
          <Input showClearIcon="true" showValueHelp="true" valueHelpRequest=".openItemsValueHelp" value="{filter>/ItemCode}" />
        </fb:control>
      </fb:FilterGroupItem>
      <fb:FilterGroupItem name="lot" label="Lote" groupName="G" visibleInFilterBar="true">
        <fb:control>
          <Input showClearIcon="true" showValueHelp="true" valueHelpRequest=".openStorageAddressesValueHelp" value="{filter>/StorageAddressCode}" />
        </fb:control>
      </fb:FilterGroupItem>
    </fb:filterGroupItems>
  </fb:FilterBar>
</core:FragmentDefinition>
```

> Confira em `CommonController.ts` os nomes reais dos handlers de produto e lote (`openItemsValueHelp`, `openStorageAddressesValueHelp`); use os existentes.

```xml
<!-- webapp/view/royalties/fragments/PendingTable.fragment.xml -->
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core" xmlns:t="sap.ui.table">
  <t:Table
    id="pendingTable"
    busyIndicatorDelay="0"
    selectionMode="MultiToggle"
    selectionBehavior="RowSelector"
    class="sapUiSizeCondensed"
    visibleRowCountMode="Auto"
    enableBusyIndicator="true"
    alternateRowColors="true"
    rows="{ path: '/RoyaltyTransactionBalances', parameters: { $filter: 'Balance gt 0', $count: true },
            sorter: [ { path: 'TransactionDate' }, { path: 'Code' } ] }">
    <t:extension>
      <OverflowToolbar>
        <Title text="Pendentes de baixa" />
        <ToolbarSpacer />
        <Text text="{totals>/summary}" />
        <ToolbarSpacer />
        <Button type="Transparent" text="Exportar PDF" icon="sap-icon://pdf-attachment" press=".onPrintPdf" />
        <Button type="Emphasized" text="Baixar selecionados" press=".onOpenWriteOff" />
        <Button type="Transparent" text="Atualizar" press=".onRefresh" />
      </OverflowToolbar>
    </t:extension>
    <t:columns>
      <t:Column width="8rem"><Label text="Romaneio" /><t:template><Text text="{Code}" wrapping="false" /></t:template></t:Column>
      <t:Column width="6rem"><Label text="Data" /><t:template>
        <Text text="{ path: 'TransactionDate', type: 'sap.ui.model.odata.type.DateTimeOffset', formatOptions: { pattern: 'dd/MM/yyyy' } }" wrapping="false" />
      </t:template></t:Column>
      <t:Column width="14rem"><Label text="Parceiro" /><t:template><Text text="{CardCode} - {CardName}" wrapping="false" /></t:template></t:Column>
      <t:Column width="14rem"><Label text="Origem" /><t:template><Text text="{RoyaltyOriginCode} - {RoyaltyOriginName}" wrapping="false" /></t:template></t:Column>
      <t:Column width="10rem"><Label text="Produto" /><t:template><Text text="{ItemName}" wrapping="false" /></t:template></t:Column>
      <t:Column width="10rem"><Label text="Situação" /><t:template>
        <Text text="{ path: 'Situation', targetType: 'any', formatter: 'formatter.royaltySituation' }" wrapping="false" />
      </t:template></t:Column>
      <t:Column width="8rem"><Label text="Cód. Teste" /><t:template><Text text="{RoyaltyTestCode}" wrapping="false" /></t:template></t:Column>
      <t:Column width="9rem" hAlign="End"><Label text="Peso Líquido" /><t:template>
        <Text text="{ path: 'NetWeight', type: 'sap.ui.model.odata.type.Decimal', formatOptions: { maxFractionDigits: 3 } }" wrapping="false" />
      </t:template></t:Column>
      <t:Column width="9rem" hAlign="End"><Label text="Baixado" /><t:template>
        <Text text="{ path: 'WrittenOffWeight', type: 'sap.ui.model.odata.type.Decimal', formatOptions: { maxFractionDigits: 3 } }" wrapping="false" />
      </t:template></t:Column>
      <t:Column width="9rem" hAlign="End"><Label text="Saldo (kg)" /><t:template>
        <Text text="{ path: 'Balance', type: 'sap.ui.model.odata.type.Decimal', formatOptions: { maxFractionDigits: 3 } }" wrapping="false" />
      </t:template></t:Column>
    </t:columns>
  </t:Table>
</core:FragmentDefinition>
```

> `formatter.royaltySituation`: acrescentar a `webapp/model/formatter.ts` (ou onde o `formatter` do CommonController vive) e o `core:require` do formatter no `Main.view.xml`, no mesmo padrão de `accountsPayable/Main.view.xml`:
> ```ts
> royaltySituation(value: string): string {
>   return ({ Declared: "Declarada", Tested: "Testada", Conventional: "Convencional",
>             ParticipantWarehouse: "Armazém Participante" } as Record<string, string>)[value] ?? value ?? "";
> },
> ```
> Se o tipo de `TransactionDate` no `$metadata` for `Edm.Date` (não `DateTimeOffset`), troque o `type` da coluna Data para `sap.ui.model.odata.type.Date`.

- [ ] **Step 3: Controller**

```ts
// webapp/controller/royalties/Main.controller.ts
import JSONModel from "sap/ui/model/json/JSONModel";
import MessageBox from "sap/m/MessageBox";
import Table from "sap/ui/table/Table";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import CommonController from "../common/CommonController";

type RoyaltyFilter = {
	FromDate?: string, ToDate?: string, Situation?: string, CardCode?: string,
	OriginCode?: string, ItemCode?: string, StorageAddressCode?: string,
};
type RoyaltyTotal = { Situation: string, Count: number, Balance: number };

const SITUATION_LABEL: Record<string, string> = {
	Declared: "Declarada", Tested: "Testada", Conventional: "Convencional", ParticipantWarehouse: "Armazém Participante",
};

/**
 * Royalties ITS: o que falta baixar no site da Bayer (aba Pendentes) e as baixas registradas (aba Baixas).
 * @namespace siagrob1.controller.royalties
 */
export default class Main extends CommonController {
	onInit(): void {
		this.createFilterModel(Main.defaultFilter());
		this.getView().setModel(new JSONModel({ summary: "" }), "totals");
		this.getRouter().getRoute("royaltyWriteOffs").attachPatternMatched(() => this.onRouteMatched());
	}

	/** Mês corrente: datas locais, nunca toISOString (vira o dia seguinte depois das 21h). */
	private static defaultFilter(): RoyaltyFilter {
		const now = new Date();
		const pad = (n: number) => String(n).padStart(2, "0");
		const first = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-01`;
		const today = `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
		return { FromDate: first, ToDate: today, Situation: "", CardCode: "", OriginCode: "", ItemCode: "", StorageAddressCode: "" };
	}

	private onRouteMatched(): void {
		this.applyFilters();
	}

	protected pendingBinding(): ODataListBinding {
		return this.byId("pendingTable").getBinding("rows") as ODataListBinding;
	}

	protected filterData(): RoyaltyFilter {
		return (this.getModel("filter") as JSONModel).getData() as RoyaltyFilter;
	}

	private static literal(value: string): string {
		return `'${String(value).replace(/'/g, "''")}'`;
	}

	/** $filter cru: sap.ui.model.Filter não formata enum OData ("Unsupported type"). */
	protected applyFilters(): void {
		const f = this.filterData();
		const parts = ["Balance gt 0"];
		if (f.FromDate) { parts.push(`TransactionDate ge ${f.FromDate}T00:00:00Z`); }
		if (f.ToDate) { parts.push(`TransactionDate le ${f.ToDate}T23:59:59Z`); }
		if (f.Situation) { parts.push(`Situation eq ${Main.literal(f.Situation)}`); }
		if (f.CardCode) { parts.push(`CardCode eq ${Main.literal(f.CardCode)}`); }
		if (f.OriginCode) { parts.push(`RoyaltyOriginCode eq ${Main.literal(f.OriginCode)}`); }
		if (f.ItemCode) { parts.push(`ItemCode eq ${Main.literal(f.ItemCode)}`); }
		if (f.StorageAddressCode) { parts.push(`StorageAddressCode eq ${Main.literal(f.StorageAddressCode)}`); }
		this.pendingBinding()?.changeParameters({ $filter: parts.join(" and ") });
		(this.byId("pendingTable") as Table).clearSelection();
		void this.loadTotals();
	}

	private async loadTotals(): Promise<void> {
		const f = this.filterData();
		const arg = (v?: string) => (v ? Main.literal(v) : "null");
		const path = `${this.api.royaltyBalanceTotals}(FromDate=${arg(f.FromDate)},ToDate=${arg(f.ToDate)},` +
			`CardCode=${arg(f.CardCode)},OriginCode=${arg(f.OriginCode)},ItemCode=${arg(f.ItemCode)},` +
			`Situation=${arg(f.Situation)},StorageAddressCode=${arg(f.StorageAddressCode)})`;
		try {
			const ctx = (this.getModel() as ODataModel).bindContext(path);
			const result = await ctx.requestObject() as { value?: RoyaltyTotal[] };
			const fmt = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 3 });
			const summary = (result?.value ?? [])
				.map(t => `${SITUATION_LABEL[t.Situation] ?? t.Situation}: ${fmt.format(t.Balance)} kg (${t.Count})`)
				.join("   |   ");
			(this.getModel("totals") as JSONModel).setProperty("/summary", summary || "Nenhum saldo pendente.");
		} catch (e) {
			(this.getModel("totals") as JSONModel).setProperty("/summary", "");
		}
	}

	onSearch(): void { this.applyFilters(); }
	onClearFilters(): void { this.clearFilters(); this.applyFilters(); }
	onRefresh(): void { this.pendingBinding()?.refresh(); void this.loadTotals(); }

	async onPrintPdf(): Promise<void> {
		const f = this.filterData();
		if (!f.FromDate || !f.ToDate) {
			MessageBox.warning("Informe o período de entrada.");
			return;
		}
		const payload = Object.fromEntries(Object.entries(f).map(([k, v]) => [k, v === "" ? null : v]));
		try {
			this.setBusy(true);
			const response = await fetch(this.api.royaltyPendingReport, {
				method: "POST",
				headers: { "Content-Type": "application/json" },
				body: JSON.stringify(payload),
			});
			if (!response.ok) {
				throw new Error((await response.text()) || "Falha ao gerar relatório.");
			}
			const fileURL = URL.createObjectURL(await response.blob());
			window.open(fileURL, "_blank");
			setTimeout(() => URL.revokeObjectURL(fileURL), 60000);
		} catch (error) {
			MessageBox.error((error as Error)?.message);
		} finally {
			this.setBusy(false);
		}
	}

	onTabSelect(): void { /* a aba Baixas aplica o próprio filtro na Task 15 */ }

	/** Implementado na Task 15. */
	onOpenWriteOff(): void { /* Task 15 */ }
}
```

> Os valores do filtro de parceiro/origem/produto/lote: os handlers `open*ValueHelp` do CommonController escrevem o código no `Input`, e o binding `filter>/...` atualiza o model — mesmo comportamento do FilterBar de `financialDocuments`.
> Se `TransactionDate` for `Edm.Date` no `$metadata`, troque os literais por `TransactionDate ge ${f.FromDate}` / `le ${f.ToDate}`.

- [ ] **Step 4: Check** — `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint`.

- [ ] **Step 5: Commit**

```bash
git add webapp/manifest.json webapp/view/royalties webapp/controller/royalties/Main.controller.ts webapp/model/formatter.ts
git commit -F- <<'EOF'
feat(storage): tela Royalties ITS com pendentes de baixa, totais e PDF

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 15: Registrar baixa (diálogo) + aba Baixas com cancelamento

**Files:**
- Create: `webapp/view/royalties/fragments/WriteOffDialog.fragment.xml`
- Create: `webapp/view/royalties/fragments/WriteOffDetailDialog.fragment.xml`
- Replace: `webapp/view/royalties/fragments/WriteOffsTable.fragment.xml`
- Modify: `webapp/controller/royalties/Main.controller.ts`

**Interfaces:**
- Consumes: `royaltyWriteOffsCreate`, `royaltyWriteOffsCancel`, entity set `RoyaltyWriteOffs` (`$expand=Lines($expand=StorageTransaction($select=Code))`).

- [ ] **Step 1: Write-off dialog** (Form + ColumnLayout, padrão do `SettleDialog`)

```xml
<!-- webapp/view/royalties/fragments/WriteOffDialog.fragment.xml -->
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core" xmlns:f="sap.ui.layout.form" xmlns:t="sap.ui.table">
  <Dialog id="royaltyWriteOffDialog" title="Registrar baixa ITS" contentWidth="900px">
    <content>
      <VBox class="sapUiSmallMargin">
        <f:Form editable="true">
          <f:layout><f:ColumnLayout columnsM="2" columnsL="2" columnsXL="2" /></f:layout>
          <f:formContainers>
            <f:FormContainer>
              <f:formElements>
                <f:FormElement label="Situação"><f:fields><Text text="{viewModel>/writeOff/situationLabel}" /></f:fields></f:FormElement>
                <f:FormElement label="Data da baixa"><f:fields>
                  <DatePicker value="{viewModel>/writeOff/writeOffDate}" required="true" displayFormat="dd/MM/yyyy" valueFormat="yyyy-MM-dd" />
                </f:fields></f:FormElement>
                <f:FormElement label="ID do ITS"><f:fields>
                  <Input value="{viewModel>/writeOff/itsId}" required="true" maxLength="50" />
                </f:fields></f:FormElement>
              </f:formElements>
            </f:FormContainer>
            <f:FormContainer>
              <f:formElements>
                <f:FormElement label="CPF/CNPJ da baixa"><f:fields>
                  <Input value="{viewModel>/writeOff/taxId}" required="true" maxLength="18" />
                </f:fields></f:FormElement>
                <f:FormElement label="Nome"><f:fields>
                  <Input value="{viewModel>/writeOff/name}" required="true" maxLength="200" />
                </f:fields></f:FormElement>
                <f:FormElement label="Observações"><f:fields>
                  <TextArea value="{viewModel>/writeOff/comments}" rows="2" maxLength="500" />
                </f:fields></f:FormElement>
              </f:formElements>
            </f:FormContainer>
          </f:formContainers>
        </f:Form>
        <t:Table rows="{viewModel>/writeOff/lines}" selectionMode="None" visibleRowCount="6" class="sapUiSizeCondensed">
          <t:columns>
            <t:Column width="8rem"><Label text="Romaneio" /><t:template><Text text="{viewModel>code}" /></t:template></t:Column>
            <t:Column width="16rem"><Label text="Parceiro" /><t:template><Text text="{viewModel>partner}" wrapping="false" /></t:template></t:Column>
            <t:Column width="9rem" hAlign="End"><Label text="Saldo (kg)" /><t:template>
              <Text text="{ path: 'viewModel>balance', type: 'sap.ui.model.type.Float', formatOptions: { maxFractionDigits: 3, groupingEnabled: true, decimalSeparator: ',', groupingSeparator: '.' } }" />
            </t:template></t:Column>
            <t:Column width="11rem" hAlign="End"><Label text="Peso a baixar (kg)" /><t:template>
              <Input change=".onWriteOffWeightChange" value="{ path: 'viewModel>weight', type: 'sap.ui.model.type.Float', formatOptions: { maxFractionDigits: 3, groupingEnabled: true, decimalSeparator: ',', groupingSeparator: '.' } }" />
            </t:template></t:Column>
          </t:columns>
        </t:Table>
        <Text class="sapUiSmallMarginTop" text="Total a baixar: {viewModel>/writeOff/totalText} kg" />
      </VBox>
    </content>
    <footer>
      <OverflowToolbar>
        <ToolbarSpacer />
        <Button text="Registrar baixa" type="Emphasized" press=".onConfirmWriteOff" />
        <Button text="Fechar" press=".onCloseWriteOff" />
      </OverflowToolbar>
    </footer>
  </Dialog>
</core:FragmentDefinition>
```

- [ ] **Step 2: Write-offs tab and detail dialog**

```xml
<!-- webapp/view/royalties/fragments/WriteOffsTable.fragment.xml -->
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core" xmlns:t="sap.ui.table">
  <t:Table
    id="writeOffsTable"
    busyIndicatorDelay="0"
    selectionMode="Single"
    selectionBehavior="Row"
    class="sapUiSizeCondensed"
    visibleRowCountMode="Auto"
    enableBusyIndicator="true"
    alternateRowColors="true"
    rows="{ path: '/RoyaltyWriteOffs', parameters: { $expand: 'Lines($expand=StorageTransaction($select=Code))', $count: true },
            sorter: { path: 'RowId', descending: true } }">
    <t:extension>
      <OverflowToolbar>
        <Title text="Baixas registradas" />
        <ToolbarSpacer />
        <Button type="Transparent" text="Visualizar" press=".onWriteOffDetail" />
        <Button type="Transparent" text="Atualizar" press=".onRefreshWriteOffs" />
      </OverflowToolbar>
    </t:extension>
    <t:columns>
      <t:Column width="5rem"><Label text="Nº" /><t:template><Text text="{RowId}" /></t:template></t:Column>
      <t:Column width="7rem"><Label text="Data" /><t:template>
        <Text text="{ path: 'WriteOffDate', type: 'sap.ui.model.odata.type.Date', formatOptions: { pattern: 'dd/MM/yyyy' } }" />
      </t:template></t:Column>
      <t:Column width="10rem"><Label text="ID do ITS" /><t:template><Text text="{ItsId}" /></t:template></t:Column>
      <t:Column width="10rem"><Label text="Situação" /><t:template>
        <Text text="{ path: 'Situation', targetType: 'any', formatter: 'formatter.royaltySituation' }" />
      </t:template></t:Column>
      <t:Column width="10rem"><Label text="CPF/CNPJ" /><t:template><Text text="{TaxId}" /></t:template></t:Column>
      <t:Column width="16rem"><Label text="Nome" /><t:template><Text text="{Name}" wrapping="false" /></t:template></t:Column>
      <t:Column width="7rem"><Label text="Status" /><t:template>
        <ObjectStatus text="{= ${ path: 'Status', targetType: 'any' } === 'Cancelled' ? 'Cancelada' : 'Ativa' }"
          state="{= ${ path: 'Status', targetType: 'any' } === 'Cancelled' ? 'Error' : 'Success' }" />
      </t:template></t:Column>
    </t:columns>
  </t:Table>
</core:FragmentDefinition>
```

```xml
<!-- webapp/view/royalties/fragments/WriteOffDetailDialog.fragment.xml -->
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core">
  <Dialog id="royaltyWriteOffDetailDialog" title="Baixa ITS {ItsId}" contentWidth="700px">
    <content>
      <VBox class="sapUiSmallMargin">
        <ObjectAttribute title="Data" text="{ path: 'WriteOffDate', type: 'sap.ui.model.odata.type.Date', formatOptions: { pattern: 'dd/MM/yyyy' } }" />
        <ObjectAttribute title="CPF/CNPJ" text="{TaxId}" />
        <ObjectAttribute title="Nome" text="{Name}" />
        <ObjectAttribute title="Observações" text="{Comments}" />
        <ObjectAttribute title="Cancelada por" text="{CanceledBy}" visible="{= ${ path: 'Status', targetType: 'any' } === 'Cancelled' }" />
        <Table items="{Lines}" class="sapUiSmallMarginTop">
          <columns>
            <Column><Text text="Romaneio" /></Column>
            <Column hAlign="End"><Text text="Peso baixado (kg)" /></Column>
          </columns>
          <items>
            <ColumnListItem>
              <Text text="{StorageTransaction/Code}" />
              <Text text="{ path: 'Weight', type: 'sap.ui.model.odata.type.Decimal', formatOptions: { maxFractionDigits: 3 } }" />
            </ColumnListItem>
          </items>
        </Table>
      </VBox>
    </content>
    <footer>
      <OverflowToolbar>
        <ToolbarSpacer />
        <Button text="Cancelar baixa" type="Reject" press=".onCancelWriteOff"
          visible="{= ${ path: 'Status', targetType: 'any' } !== 'Cancelled' }" />
        <Button text="Fechar" press=".onCloseWriteOffDetail" />
      </OverflowToolbar>
    </footer>
  </Dialog>
</core:FragmentDefinition>
```

- [ ] **Step 3: Controller additions** (substituir os stubs `onOpenWriteOff`/`onTabSelect` da Task 14; imports novos: `Dialog`, `MessageToast`, `Context` de `sap/ui/model/odata/v4/Context`, `DialogHelper` de `siagrob1/dialogs/DialogHelper`)

```ts
	private writeOffDialog?: Dialog;
	private writeOffDetailDialog?: Dialog;

	private static localIsoDate(d: Date): string {
		const pad = (n: number) => String(n).padStart(2, "0");
		return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
	}

	async onOpenWriteOff(): Promise<void> {
		const table = this.byId("pendingTable") as Table;
		const contexts = table.getSelectedIndices()
			.map(i => table.getContextByIndex(i) as Context)
			.filter(Boolean);
		if (contexts.length < 1) {
			MessageBox.warning("Selecione ao menos 1 romaneio para baixar.");
			return;
		}
		const situations = [...new Set(contexts.map(c => c.getProperty("Situation") as string))];
		if (situations.length > 1) {
			MessageBox.warning("Selecione romaneios de uma mesma situação.");
			return;
		}
		const lines = contexts.map(c => ({
			key: c.getProperty("StorageTransactionKey") as string,
			code: c.getProperty("Code") as string,
			partner: `${c.getProperty("CardCode") as string} - ${c.getProperty("CardName") as string}`,
			balance: Number(c.getProperty("Balance")),
			weight: Number(c.getProperty("Balance")),
		}));
		(this.getModel("viewModel") as JSONModel).setProperty("/writeOff", {
			situationLabel: SITUATION_LABEL[situations[0]] ?? situations[0],
			writeOffDate: Main.localIsoDate(new Date()),
			itsId: "", taxId: "", name: "", comments: "",
			lines,
			totalText: "",
		});
		this.updateWriteOffTotal();
		this.writeOffDialog ??= await DialogHelper.createDialog(this, "siagrob1.view.royalties.fragments.WriteOffDialog");
		this.writeOffDialog.open();
	}

	onWriteOffWeightChange(): void { this.updateWriteOffTotal(); }

	private updateWriteOffTotal(): void {
		const vm = this.getModel("viewModel") as JSONModel;
		const lines = (vm.getProperty("/writeOff/lines") ?? []) as { weight: number }[];
		const total = lines.reduce((s, l) => s + (Number(l.weight) || 0), 0);
		vm.setProperty("/writeOff/totalText", new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 3 }).format(total));
	}

	onCloseWriteOff(): void { this.writeOffDialog?.close(); }

	async onConfirmWriteOff(): Promise<void> {
		const form = (this.getModel("viewModel") as JSONModel).getProperty("/writeOff") as {
			writeOffDate: string, itsId: string, taxId: string, name: string, comments: string,
			lines: { key: string, code: string, balance: number, weight: number }[],
		};
		if (!form.writeOffDate || !form.itsId?.trim() || !form.taxId?.trim() || !form.name?.trim()) {
			MessageBox.warning("Informe data, ID do ITS, CPF/CNPJ e nome.");
			return;
		}
		const invalid = form.lines.find(l => !(l.weight > 0) || l.weight > l.balance + 1e-9);
		if (invalid) {
			MessageBox.warning(`Peso inválido no romaneio ${invalid.code}: informe de 0 a ${invalid.balance} kg.`);
			return;
		}
		const action = (this.getModel() as ODataModel).bindContext(this.api.royaltyWriteOffsCreate);
		action.setParameter("WriteOffDate", form.writeOffDate);
		action.setParameter("ItsId", form.itsId.trim());
		action.setParameter("TaxId", form.taxId.trim());
		action.setParameter("Name", form.name.trim());
		// null explícito, nunca undefined: parâmetro do EDM que não chega derruba a action.
		action.setParameter("Comments", form.comments?.trim() || null);
		action.setParameter("StorageTransactionKeys", form.lines.map(l => l.key));
		action.setParameter("Weights", form.lines.map(l => Number(l.weight)));
		try {
			this.setBusy(true);
			await action.invoke();
			this.writeOffDialog?.close();
			MessageToast.show("Baixa ITS registrada.");
			(this.byId("pendingTable") as Table).clearSelection();
			this.onRefresh();
			this.onRefreshWriteOffs();
		} catch (err) {
			MessageBox.error((err as Error).message || "Erro ao registrar a baixa.");
		} finally {
			this.setBusy(false);
		}
	}

	onTabSelect(): void { this.onRefreshWriteOffs(); }

	onRefreshWriteOffs(): void {
		(this.byId("writeOffsTable").getBinding("rows") as ODataListBinding)?.refresh();
	}

	async onWriteOffDetail(): Promise<void> {
		const table = this.byId("writeOffsTable") as Table;
		const ctx = table.getContextByIndex(table.getSelectedIndex()) as Context;
		if (!ctx) {
			MessageBox.warning("Selecione uma baixa.");
			return;
		}
		this.writeOffDetailDialog ??= await DialogHelper.createDialog(this, "siagrob1.view.royalties.fragments.WriteOffDetailDialog");
		this.writeOffDetailDialog.setBindingContext(ctx);
		this.writeOffDetailDialog.open();
	}

	onCloseWriteOffDetail(): void { this.writeOffDetailDialog?.close(); }

	async onCancelWriteOff(): Promise<void> {
		const ctx = this.writeOffDetailDialog?.getBindingContext() as Context;
		if (!ctx) { return; }
		const confirmed = await new Promise<boolean>(resolve => MessageBox.confirm(
			`Cancelar a baixa ITS ${ctx.getProperty("ItsId") as string}? O saldo volta aos romaneios.`,
			{ onClose: (a: string) => resolve(a === MessageBox.Action.OK) }));
		if (!confirmed) { return; }
		const action = (this.getModel() as ODataModel).bindContext(this.api.royaltyWriteOffsCancel);
		action.setParameter("Key", ctx.getProperty("Key"));
		try {
			this.setBusy(true);
			await action.invoke();
			this.writeOffDetailDialog?.close();
			MessageToast.show("Baixa ITS cancelada.");
			this.onRefreshWriteOffs();
			this.onRefresh();
		} catch (err) {
			MessageBox.error((err as Error).message || "Erro ao cancelar a baixa.");
		} finally {
			this.setBusy(false);
		}
	}
```

> O `MessageBox.confirm` é diálogo UI5 (não `window.confirm`) — não bloqueia a automação do navegador.

- [ ] **Step 4: Check** — `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint`.

- [ ] **Step 5: Commit**

```bash
git add webapp/view/royalties/fragments webapp/controller/royalties/Main.controller.ts
git commit -F- <<'EOF'
feat(storage): registrar e cancelar baixas ITS pela tela de royalties

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 16: Seção "Baixas ITS" no detalhe do romaneio

**Files:**
- Modify: `webapp/view/storageTransactions/Detail.view.xml` (nova `uxap:ObjectPageSection` depois de "Dados do Romaneio", linha ~66)
- Modify: `webapp/controller/storageTransactions/Detail.controller.ts`

- [ ] **Step 1: Section**

```xml
        <uxap:ObjectPageSection titleUppercase="false" title="Baixas ITS" visible="{ui>/royaltyVisible}">
          <uxap:subSections>
            <uxap:ObjectPageSubSection>
              <uxap:blocks>
                <VBox>
                  <HBox class="sapUiSmallMarginBottom">
                    <ObjectAttribute title="Peso líquido" text="{ui>/royaltyBalance/netText} kg" class="sapUiMediumMarginEnd" />
                    <ObjectAttribute title="Baixado" text="{ui>/royaltyBalance/writtenOffText} kg" class="sapUiMediumMarginEnd" />
                    <ObjectAttribute title="Saldo" text="{ui>/royaltyBalance/balanceText} kg" />
                  </HBox>
                  <Table id="royaltyLinesTable" noDataText="Nenhuma baixa ITS para este romaneio.">
                    <columns>
                      <Column><Text text="Data" /></Column>
                      <Column><Text text="ID do ITS" /></Column>
                      <Column><Text text="CPF/CNPJ" /></Column>
                      <Column><Text text="Nome" /></Column>
                      <Column hAlign="End"><Text text="Peso (kg)" /></Column>
                      <Column><Text text="Status" /></Column>
                    </columns>
                  </Table>
                </VBox>
              </uxap:blocks>
            </uxap:ObjectPageSubSection>
          </uxap:subSections>
        </uxap:ObjectPageSection>
```

(declarar `xmlns` de `sap.m` se a view usa outro prefixo default).

- [ ] **Step 2: Controller** — depois do `bindElement` e do `RoyaltyFieldsHelper.apply` (Task 12), quando `ui>/royaltyVisible`:

```ts
	private bindRoyaltyLines(key: string): void {
		const table = this.byId("royaltyLinesTable") as Table;
		table.bindItems({
			path: "/RoyaltyWriteOffLines",
			parameters: { $expand: "RoyaltyWriteOff", $filter: `StorageTransactionKey eq ${key}` },
			template: new ColumnListItem({
				cells: [
					new Text({ text: { path: "RoyaltyWriteOff/WriteOffDate", type: "sap.ui.model.odata.type.Date", formatOptions: { pattern: "dd/MM/yyyy" } } }),
					new Text({ text: "{RoyaltyWriteOff/ItsId}" }),
					new Text({ text: "{RoyaltyWriteOff/TaxId}" }),
					new Text({ text: "{RoyaltyWriteOff/Name}" }),
					new Text({ text: { path: "Weight", type: "sap.ui.model.odata.type.Decimal", formatOptions: { maxFractionDigits: 3 } } }),
					new Text({ text: { path: "RoyaltyWriteOff/Status", targetType: "any", formatter: (s: string) => (s === "Cancelled" ? "Cancelada" : "Ativa") } }),
				],
			}),
		});
	}

	private async loadRoyaltyBalance(key: string): Promise<void> {
		const ui = this.getModel("ui") as JSONModel;
		const fmt = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 3 });
		try {
			const ctx = (this.getModel() as ODataModel).bindContext(`/RoyaltyTransactionBalances(${key})`);
			const row = await ctx.requestObject() as { NetWeight: number, WrittenOffWeight: number, Balance: number };
			ui.setProperty("/royaltyBalance", {
				netText: fmt.format(row.NetWeight), writtenOffText: fmt.format(row.WrittenOffWeight), balanceText: fmt.format(row.Balance),
			});
		} catch {
			// Romaneio não elegível (pendente, sem situação, saída...): seção sem saldo.
			ui.setProperty("/royaltyBalance", { netText: "-", writtenOffText: "-", balanceText: "-" });
		}
	}
```

e chamar `this.bindRoyaltyLines(id); void this.loadRoyaltyBalance(id);` no `routeMatched` quando `ui>/royaltyVisible` for true. Imports: `Table`/`ColumnListItem`/`Text` de `sap/m/...`.

> Memória `odata-nav-and-late-property-routes-404`: se `/RoyaltyTransactionBalances(<guid>)` der 404, conferir o formato da chave Guid no OData v4 (sem aspas) e o overload `Get([FromRoute] Guid key)` da Task 5.

- [ ] **Step 3: Check** — `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint`.

- [ ] **Step 4: Commit**

```bash
git add webapp/view/storageTransactions/Detail.view.xml webapp/controller/storageTransactions/Detail.controller.ts
git commit -F- <<'EOF'
feat(storage): baixas ITS e saldo de royalty no detalhe do romaneio

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

---

### Task 17: Verificação ponta a ponta + atualização da spec

**Files:**
- Modify: `siagro-b1-backend/docs/superpowers/specs/2026-10-10-royalty-its-write-off-design.md` (seções 2.4, 2.5, 2.3 "caminhos", 3 item 5 e 5 — refletir os "Desvios da spec" deste plano)

- [ ] **Step 1: Ambiente**

Banco de **DEV** (memória `run-stack-locally`; nunca HOM/PRD; `ASPNETCORE_ENVIRONMENT` sempre explícito — memória `db-migration-profile-targets-production`). Aplicar as duas migrations no banco de DEV:
`dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` e o mesmo com `CommonDbContext`, com o environment de DEV explícito. Subir Web, Gateway e Reports; `yarn start:dev` no frontend. Matar processos só pelo PID (memória `subagent-must-kill-by-pid`).

- [ ] **Step 2: Caminho do usuário (navegador, a partir da home)**

1. Menu Cadastros → produto da soja → Complemento → marcar "Controla royalties" → Salvar → reabrir: continua marcado.
2. Contrato de compra: gravar com Tipo Tecnologia "Convencional" → reabrir → mostra "Convencional" (Task 10).
3. Armazenagem → Pesagem: ticket de entrada de soja; na classificação aparecem Situação/Origem/Código do teste; origem vem vazia e, ao concluir sem situação, aparece "Informe a situação de royalty e a origem da carga."; com "Testada" e sem código, "Informe o código do teste…"; preencher e concluir.
4. Ticket de milho: os campos não aparecem e conclui normalmente.
5. Romaneios de Movimentação → o romaneio gerado mostra a situação, a origem (com nome) e a seção "Baixas ITS" com saldo = peso líquido.
6. Armazenagem → Royalties ITS (menu novo): o romaneio está em Pendentes; totais por situação batem; Exportar PDF abre o relatório com subtotais.
7. Selecionar dois romaneios de situações diferentes → "Selecione romaneios de uma mesma situação."
8. Baixar um romaneio parcialmente (ex.: metade) com CPF válido → saldo cai; aba Baixas mostra a baixa Ativa; detalhe do romaneio lista a baixa.
9. Tentar estornar esse romaneio → "Romaneio possui baixa ITS ativa (…); cancele a baixa antes."
10. Baixar o restante com outro ID; repetir um ID ativo → "ID do ITS … já utilizado…".
11. Cancelar uma baixa pela aba Baixas → saldo volta; o mesmo ID pode ser reutilizado.
12. Entrada em armazenagem própria de soja: os campos aparecem, origem vem com o fornecedor do contrato; sem situação, o salvar avisa; com situação, o recebimento aparece em Pendentes.

Registrar qualquer divergência e corrigir (TDD) antes de seguir. Se o ambiente local impedir um passo (ex.: SAP indisponível no modo SAPB1 — memória `yokotobi-verification-env-timeouts-and-sap-unreachable`), anotar qual passo ficou sem verificação.

- [ ] **Step 3: Full test runs**

Backend: `dotnet test SiagroB1.Application.Tests` → tudo verde (anotar o total). Frontend: `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint` → sem erros.

- [ ] **Step 4: Update the spec and commit (backend repo)**

Aplicar os 6 desvios listados no topo deste plano às seções correspondentes da spec.

```bash
git add docs/superpowers/specs/2026-10-10-royalty-its-write-off-design.md
git commit -F- <<'EOF'
docs(storage): alinhar o desenho de royalties ao que foi implementado

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_016riKPAkAjZmRzBJQVjRiPn
EOF
```

Não fazer push nem merge — informar ao usuário os dois branches prontos.
