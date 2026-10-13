# Conciliação Fiscal de Compras — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Vincular linhas do Documento de Entrada (NF do fornecedor) a romaneios de compra (8/9/10/16/17), N:N com
quantidade, numa tela de conciliação, com reflexo na aba Entregas do contrato de compra e num relatório PDF.

**Architecture:** Tabela de ligação `PURCHASE_FISCAL_RECONCILIATIONS` + confirmação manual em `STORAGE_TRANSACTIONS`.
Leitura e regras puras no `SiagroB1.Infra/Queries` (compartilhadas por Web e Reports, padrão `RoyaltyBalanceQuery`);
gravação num serviço da Application; limpeza automática (cancelar/excluir NF, remover linha, cancelar romaneio) e as
duas recusas na edição da NF num `SaveChangesInterceptor` (padrão `FinishedContractMutationGuardInterceptor`).
Frontend: página nova com JSONModel + `sendJson`, padrão de `purchaseDischarges/Main`.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server; testes InMemory), OData v4 (ASP.NET Core OData), Dapper (aba Entregas),
FastReport.OpenSource; OpenUI5 1.141 + TypeScript.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-13-purchase-invoice-transaction-reconciliation-design.md`

## Global Constraints

- Modo alvo: **STANDALONE** (profile `ceagui`). Nenhum código depende de SAP; SAPB1 não é verificado.
- Identificadores em inglês; tudo que o usuário lê em **pt-BR**. Comentários em pt-BR.
- Vínculo **nunca** altera saldo, alocação, contrato, liberação, título ou estoque fiscal.
- Quantidade do vínculo sempre em **KG** (`PurchaseFiscalReconciliationRules.BaseUnit = "KG"`).
- Tipos de romaneio vinculáveis: `Purchase(8)`, `PurchaseReturn(9)`, `PurchaseQtyComplement(10)`,
  `PurchaseDischargeLoss(16)`, `PurchaseDischargeGain(17)`.
- Commits: backend e frontend são **dois repos, dois commits**; branch `feature/purchase-invoice-reconciliation` nos
  dois; mensagem no padrão do `CLAUDE.md` de cada repo (`tipo(escopo): ...` pt-BR); `DB:` obrigatório em commit com
  migration; **nunca push**; todo arquivo novo `git add` na hora.
- Migrations: o snapshot do `AppDbContext` é dessincronizado do banco — **depois de `migrations add`, apague do
  `Up()/Down()` tudo que não for desta feature** e confira o snapshot. Aplicar só no CEAGUI local com
  `ASPNETCORE_ENVIRONMENT=Ceagui-Development` explícito. Nunca o profile `db-migration`.
- Frontend: tipos TS das respostas do Web OData em **PascalCase** (o Web serializa PascalCase e enums como string).
- Decimal editável na UI: `sap.ui.model.type.Float`/Double — nunca `odata.type.Decimal`.

## Review Focus

1. **Romaneio cancelado por qualquer um dos 6 caminhos** → vínculos somem e a confirmação cai. Coberto pelo teste de
   ponta com o `StorageTransactionsCancelService` real (Task 6) além dos testes do interceptor.
2. **NF de 30.000 kg vinculada a romaneio de 29.820 kg (FOB)** → grava, Diferença +180, status Parcial até confirmar.
   Teste na Task 5 (`Save_allows_fiscal_above_physical_and_status_is_partial`).
3. **Linha da NF em SC (saca) sem fator no Complemento do Produto** → recusa clara, nada gravado; na lista a linha
   aparece com Disponível vazio. Testes nas Tasks 2, 3 e 5.
4. **Usuário edita a NF depois de vincular** (reduz a quantidade abaixo do vinculado / troca o produto) → recusa com
   "desvincule antes". Testes na Task 6.
5. **Emitente com outra IE (outro CardCode, mesmo CPF)** → aparece na lista por padrão, sem aviso; CardCode e CPF
   diferentes → só pelo filtro Emitente, com aviso. Teste na Task 3.

---

## Mapa de arquivos

**Backend (`siagro-b1-backend/`)**

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Domain/Entities/PurchaseFiscalReconciliation.cs` (novo) | Entidade do vínculo |
| `SiagroB1.Domain/Entities/StorageTransaction.cs` (mod) | `FiscalReconciledAt/By` |
| `SiagroB1.Domain/Enums/FiscalReconciliationStatus.cs` (novo) | Sem NF / Parcial / Conciliado |
| `SiagroB1.Domain/Dtos/FiscalReconciliationDtos.cs` (novo) | Filtro e DTOs de leitura/gravação |
| `SiagroB1.Infra/Context/AppDbContext.cs` (mod) | DbSet + FKs + índice único |
| `SiagroB1.Infra/Queries/PurchaseFiscalReconciliationRules.cs` (novo) | Regras puras (tipo, sentido, dono, UM, status) |
| `SiagroB1.Infra/Queries/PurchaseFiscalReconciliationQuery.cs` (novo) | Consultas compartilhadas |
| `SiagroB1.Infra/Interceptors/PurchaseFiscalReconciliationCleanupInterceptor.cs` (novo) | Limpeza + recusas da NF |
| `SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationPlanner.cs` (novo) | Distribuição sugerida |
| `SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationService.cs` (novo) | Ler, sugerir, gravar, confirmar |
| `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsGetAllocationsByContractService.cs` (mod) | Aba Entregas |
| `SiagroB1.Domain/Dtos/PurchaseContractAllocationsByContractDto.cs` (mod) | Colunas fiscais |
| `SiagroB1.Web/Functions/PurchaseFiscalReconciliations/*.cs`, `SiagroB1.Web/Actions/PurchaseFiscalReconciliations/*.cs` (novos) | Endpoints |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, `SiagroB1.Web/Program.cs` (mod) | EDM, DI, interceptor |
| `SiagroB1.Reports/Dtos/PurchaseFiscalReconciliationReport*.cs`, `SiagroB1.Reports/Services/PurchaseFiscalReconciliationReportService.cs`, `SiagroB1.Reports/Controllers/PurchaseFiscalReconciliationController.cs`, `SiagroB1.Reports/Reports/Templates/PurchaseFiscalReconciliation.frx` (novos) | Relatório |
| `SiagroB1.Migrations/AppContext/*_AddPurchaseFiscalReconciliations.cs`, `SiagroB1.Migrations/CommonContext/*_AddPurchaseFiscalReconciliationMenus.cs` (novos) | Migrations |
| `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/*.cs` (novos) | Testes |

**Frontend (`siagro-b1-frontend/`)**

| Arquivo | Responsabilidade |
|---|---|
| `webapp/model/ServerRoutes.ts` (mod) | Rotas novas |
| `webapp/types/PurchaseFiscalReconciliation.ts` (novo) | Tipos das respostas |
| `webapp/manifest.json` (mod) | Rotas `purchaseFiscalReconciliations` e `purchaseFiscalReconciliationReport` |
| `webapp/view/purchaseFiscalReconciliations/Main.view.xml` + `fragments/{Filterbar,LinkDialog,LinksDialog}.fragment.xml` (novos) | Tela |
| `webapp/controller/purchaseFiscalReconciliations/Main.controller.ts` (novo) | Controller da tela |
| `webapp/view/purchaseContracts/fragments/PurchaseContractAllocations.fragment.xml` (mod) + `PurchaseContractsBaseController.ts` (mod) | Aba Entregas |
| `webapp/view/reports/purchaseFiscalReconciliation/Main.view.xml`, `webapp/controller/reports/purchaseFiscalReconciliation/Main.controller.ts` (novos) | Tela do relatório |

---

### Task 1: Modelo de dados e migration

**Files:**
- Create: `SiagroB1.Domain/Entities/PurchaseFiscalReconciliation.cs`
- Create: `SiagroB1.Domain/Enums/FiscalReconciliationStatus.cs`
- Modify: `SiagroB1.Domain/Entities/StorageTransaction.cs` (depois de `Comments`, ~linha 240)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSet perto da linha 109; config logo depois do bloco de `StorageReturnReconciliation`, ~linha 701)
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_AddPurchaseFiscalReconciliations.cs` (gerada e podada)
- Test: `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationSchemaTests.cs`

**Interfaces:**
- Produces: `PurchaseFiscalReconciliation { Guid Key; Guid PurchaseInvoiceItemKey; PurchaseInvoiceItem? PurchaseInvoiceItem; Guid StorageTransactionKey; StorageTransaction? StorageTransaction; decimal Quantity; DateTime CreatedAt; string? CreatedBy }`;
  `AppDbContext.PurchaseFiscalReconciliations`; `StorageTransaction.FiscalReconciledAt (DateTime?)`, `StorageTransaction.FiscalReconciledBy (string?)`;
  `enum FiscalReconciliationStatus { WithoutInvoice = 0, Partial = 1, Reconciled = 2 }`.

- [ ] **Step 1: Escrever o teste de esquema (falha)**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

/// <summary>Esquema do vínculo NF × romaneio (spec 2026-10-13 §3), lido do modelo do EF sem banco.</summary>
public class PurchaseFiscalReconciliationSchemaTests
{
    private static AppDbContext Model() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=model-inspection-only;Database=none").Options);

    [Fact]
    public void Link_table_has_unique_pair_and_restrict_fks()
    {
        using var ctx = Model();
        var entity = ctx.Model.FindEntityType(typeof(PurchaseFiscalReconciliation))!;

        Assert.Equal("PURCHASE_FISCAL_RECONCILIATIONS", entity.GetTableName());
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(PurchaseFiscalReconciliation.PurchaseInvoiceItemKey), nameof(PurchaseFiscalReconciliation.StorageTransactionKey)]));
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Equal("DECIMAL(19,6)", entity.FindProperty(nameof(PurchaseFiscalReconciliation.Quantity))!.GetColumnType());
    }

    [Fact]
    public void Storage_transaction_has_confirmation_columns()
    {
        using var ctx = Model();
        var entity = ctx.Model.FindEntityType(typeof(StorageTransaction))!;

        Assert.NotNull(entity.FindProperty(nameof(StorageTransaction.FiscalReconciledAt)));
        Assert.Equal("VARCHAR(100)", entity.FindProperty(nameof(StorageTransaction.FiscalReconciledBy))!.GetColumnType());
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationSchemaTests"`
Expected: erro de compilação — `PurchaseFiscalReconciliation` não existe.

- [ ] **Step 3: Implementar entidade, enum, colunas e config**

`SiagroB1.Domain/Entities/PurchaseFiscalReconciliation.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Conciliação fiscal de compras (spec 2026-10-13 §3.1): quanto de uma linha de NF do fornecedor cobre um romaneio de
/// compra (8/9/10/16/17). N:N com quantidade em KG, sem efeito em saldo. Um par (linha, romaneio) só existe uma vez.
/// </summary>
[Table("PURCHASE_FISCAL_RECONCILIATIONS")]
public class PurchaseFiscalReconciliation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    public Guid PurchaseInvoiceItemKey { get; set; }
    public PurchaseInvoiceItem? PurchaseInvoiceItem { get; set; }

    public Guid StorageTransactionKey { get; set; }
    public StorageTransaction? StorageTransaction { get; set; }

    /// <summary>Quantidade fiscal atribuída ao romaneio, em KG. Sem teto pelo peso do romaneio (D7).</summary>
    [Column(TypeName = "DECIMAL(19,6)")]
    public decimal Quantity { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }
}
```

`SiagroB1.Domain/Enums/FiscalReconciliationStatus.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>Situação fiscal do romaneio de compra (spec 2026-10-13 §4.1). Calculada, nunca persistida.</summary>
public enum FiscalReconciliationStatus
{
    WithoutInvoice = 0, // Sem NF
    Partial = 1,        // Parcial
    Reconciled = 2,     // Conciliado
}
```

Em `StorageTransaction.cs`, logo depois de `Comments`:

```csharp
    /// <summary>
    /// Conciliação fiscal confirmada à mão (spec 2026-10-13 §3.2): o romaneio conta como Conciliado mesmo com
    /// diferença entre o fiscal e o físico. Qualquer mudança nos vínculos dele limpa os dois campos.
    /// </summary>
    public DateTime? FiscalReconciledAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? FiscalReconciledBy { get; set; }
```

Em `AppDbContext.cs`, DbSet ao lado de `StorageReturnReconciliations`:

```csharp
    public DbSet<PurchaseFiscalReconciliation> PurchaseFiscalReconciliations { get; set; }
```

e a config logo depois do índice único de `StorageReturnReconciliation`:

```csharp
        // Conciliação fiscal de compras (spec 2026-10-13): NF do fornecedor × romaneio, sem efeito em saldo. A limpeza
        // (NF cancelada/excluída, linha removida, romaneio cancelado) é do PurchaseFiscalReconciliationCleanupInterceptor.
        modelBuilder.Entity<PurchaseFiscalReconciliation>()
            .HasOne(x => x.PurchaseInvoiceItem)
            .WithMany()
            .HasForeignKey(x => x.PurchaseInvoiceItemKey)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PurchaseFiscalReconciliation>()
            .HasOne(x => x.StorageTransaction)
            .WithMany()
            .HasForeignKey(x => x.StorageTransactionKey)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PurchaseFiscalReconciliation>()
            .HasIndex(x => new { x.PurchaseInvoiceItemKey, x.StorageTransactionKey })
            .IsUnique();
```

- [ ] **Step 4: Rodar o teste e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationSchemaTests"`
Expected: 2 passed.

- [ ] **Step 5: Gerar a migration e podar**

```bash
cd siagro-b1-backend
dotnet ef migrations add AddPurchaseFiscalReconciliations --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir AppContext
```

Abra o `.cs` gerado. `Up()` deve conter SÓ: `CreateTable("PURCHASE_FISCAL_RECONCILIATIONS")` (com as 2 FKs `Restrict` e o
índice único composto + índice simples em `StorageTransactionKey`) e dois `AddColumn` em `STORAGE_TRANSACTIONS`
(`FiscalReconciledAt datetime2 NULL`, `FiscalReconciledBy VARCHAR(100) NULL`). **Apague qualquer outra operação**
(drift do snapshot) e o espelho dela no `Down()`. `Down()`: `DropTable` + dois `DropColumn`. Acrescente no topo da classe:

```csharp
    /// <summary>
    /// Conciliação fiscal de compras (spec 2026-10-13 §3): tabela do vínculo NF × romaneio e a confirmação manual no
    /// romaneio. Podada à mão — o snapshot deste contexto tem drift; só ficaram as operações desta feature.
    /// </summary>
```

Confira: `dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` → "No changes".

- [ ] **Step 6: Build + suíte inteira**

Run: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests`
Expected: build OK; todos os testes passam (o número atual + 2).

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Domain/Entities/PurchaseFiscalReconciliation.cs SiagroB1.Domain/Enums/FiscalReconciliationStatus.cs SiagroB1.Domain/Entities/StorageTransaction.cs SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Migrations/AppContext SiagroB1.Application.Tests/PurchaseFiscalReconciliations
git commit -m "feat(invoice): vínculo NF do fornecedor × romaneio de compra no modelo" -m "Tabela de ligação N:N com quantidade em KG e confirmação manual no romaneio, base da Conciliação Fiscal de Compras. Sem efeito em saldo." -m "DB: AddPurchaseFiscalReconciliations"
```

---

### Task 2: Regras puras

**Files:**
- Create: `SiagroB1.Infra/Queries/PurchaseFiscalReconciliationRules.cs`
- Test: `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationRulesTests.cs`

**Interfaces:**
- Consumes: `FiscalReconciliationStatus`, `ItemComplement`, `PurchaseInvoiceType`, `StorageTransactionType`.
- Produces (static class `SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules`):
  - `const string BaseUnit = "KG"`
  - `StorageTransactionType[] LinkableTypes`
  - `bool IsLinkable(StorageTransactionType type)`
  - `bool SenseMatches(PurchaseInvoiceType invoiceType, StorageTransactionType type)`
  - `string? OwnerRoot(string? taxId)`; `bool SameOwner(string? a, string? b)`
  - `decimal? KgFactor(string? lineUnit, ItemComplement? complement)`
  - `FiscalReconciliationStatus Status(decimal physical, decimal fiscal, bool hasLinks, bool confirmed)`
  - `string TypeName(StorageTransactionType type)`; `string StatusName(FiscalReconciliationStatus status)`
  - `string TaxDocument(string? taxNumber, string? series, string? internalNumber)`

- [ ] **Step 1: Escrever os testes (falham)**

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Queries;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

public class PurchaseFiscalReconciliationRulesTests
{
    [Theory]
    [InlineData(StorageTransactionType.Purchase, true)]
    [InlineData(StorageTransactionType.PurchaseReturn, true)]
    [InlineData(StorageTransactionType.PurchaseQtyComplement, true)]
    [InlineData(StorageTransactionType.PurchaseDischargeLoss, true)]
    [InlineData(StorageTransactionType.PurchaseDischargeGain, true)]
    [InlineData(StorageTransactionType.Receipt, false)]
    [InlineData(StorageTransactionType.SalesShipment, false)]
    [InlineData(StorageTransactionType.PurchasePriceComplement, false)]
    public void Only_purchase_side_transactions_are_linkable(StorageTransactionType type, bool expected) =>
        Assert.Equal(expected, R.IsLinkable(type));

    [Theory]
    [InlineData(PurchaseInvoiceType.Normal, StorageTransactionType.Purchase, true)]
    [InlineData(PurchaseInvoiceType.Normal, StorageTransactionType.PurchaseQtyComplement, true)]
    [InlineData(PurchaseInvoiceType.Normal, StorageTransactionType.PurchaseDischargeGain, true)]
    [InlineData(PurchaseInvoiceType.Normal, StorageTransactionType.PurchaseReturn, false)]
    [InlineData(PurchaseInvoiceType.Normal, StorageTransactionType.PurchaseDischargeLoss, false)]
    [InlineData(PurchaseInvoiceType.Return, StorageTransactionType.PurchaseReturn, true)]
    [InlineData(PurchaseInvoiceType.Return, StorageTransactionType.PurchaseDischargeLoss, true)]
    [InlineData(PurchaseInvoiceType.Return, StorageTransactionType.Purchase, false)]
    public void Invoice_sense_must_match_transaction(PurchaseInvoiceType invoice, StorageTransactionType type, bool expected) =>
        Assert.Equal(expected, R.SenseMatches(invoice, type));

    [Theory]
    [InlineData("12345678000199", "12345678000270", true)]   // mesma raiz de CNPJ
    [InlineData("12.345.678/0001-99", "12345678000199", true)] // máscara é ignorada
    [InlineData("12345678901", "12345678901", true)]         // mesmo CPF
    [InlineData("12345678901", "12345678902", false)]
    [InlineData("12345678000199", "87654321000199", false)]
    [InlineData(null, "12345678901", false)]
    [InlineData("", "", false)]
    public void Same_owner_compares_cpf_or_cnpj_root(string? a, string? b, bool expected) =>
        Assert.Equal(expected, R.SameOwner(a, b));

    [Fact]
    public void Kg_factor_is_one_for_kg_or_blank_unit() 
    {
        Assert.Equal(1m, R.KgFactor("KG", null));
        Assert.Equal(1m, R.KgFactor("kg", null));
        Assert.Equal(1m, R.KgFactor(null, null));
    }

    [Fact]
    public void Kg_factor_uses_commercial_unit_of_the_item()
    {
        var complement = new ItemComplement { ItemCode = "SOJA", CommercialUnitOfMeasureCode = "SC", CommercialFactor = 60m };

        Assert.Equal(60m, R.KgFactor("SC", complement));
        Assert.Null(R.KgFactor("TON", complement));
        Assert.Null(R.KgFactor("SC", null));
        Assert.Null(R.KgFactor("SC", new ItemComplement { ItemCode = "SOJA", CommercialUnitOfMeasureCode = "SC" }));
    }

    [Theory]
    [InlineData(100, 0, false, false, FiscalReconciliationStatus.WithoutInvoice)]
    [InlineData(100, 100, true, false, FiscalReconciliationStatus.Reconciled)]
    [InlineData(100, 101, true, false, FiscalReconciliationStatus.Partial)]
    [InlineData(100, 101, true, true, FiscalReconciliationStatus.Reconciled)]
    [InlineData(100, 40, true, false, FiscalReconciliationStatus.Partial)]
    public void Status_follows_links_difference_and_confirmation(
        decimal physical, decimal fiscal, bool hasLinks, bool confirmed, FiscalReconciliationStatus expected) =>
        Assert.Equal(expected, R.Status(physical, fiscal, hasLinks, confirmed));

    [Fact]
    public void Tax_document_is_number_dash_series_or_internal_number()
    {
        Assert.Equal("000123-001", R.TaxDocument("000123", "001", "DE000007"));
        Assert.Equal("000123", R.TaxDocument("000123", null, "DE000007"));
        Assert.Equal("DE000007", R.TaxDocument(null, "001", "DE000007"));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationRulesTests"`
Expected: erro de compilação — `PurchaseFiscalReconciliationRules` não existe.

- [ ] **Step 3: Implementar**

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Regras puras da Conciliação Fiscal de Compras (spec 2026-10-13 §4). Moram no Infra porque a tela (Web, via
/// Application) e o relatório (Reports, que não referencia a Application) precisam das mesmas.
/// </summary>
public static class PurchaseFiscalReconciliationRules
{
    /// <summary>Unidade física do romaneio — a do vínculo. Ver <see cref="ItemComplement.CommercialFactor"/>.</summary>
    public const string BaseUnit = "KG";

    public static readonly StorageTransactionType[] LinkableTypes =
    [
        StorageTransactionType.Purchase,
        StorageTransactionType.PurchaseReturn,
        StorageTransactionType.PurchaseQtyComplement,
        StorageTransactionType.PurchaseDischargeLoss,
        StorageTransactionType.PurchaseDischargeGain,
    ];

    public static bool IsLinkable(StorageTransactionType type) => LinkableTypes.Contains(type);

    /// <summary>NF de devolução cobre o que SAI do contrato (9, 16); a normal, o que ENTRA (8, 10, 17).</summary>
    public static bool SenseMatches(PurchaseInvoiceType invoiceType, StorageTransactionType type) =>
        invoiceType == PurchaseInvoiceType.Return
            ? type is StorageTransactionType.PurchaseReturn or StorageTransactionType.PurchaseDischargeLoss
            : type is StorageTransactionType.Purchase or StorageTransactionType.PurchaseQtyComplement
                or StorageTransactionType.PurchaseDischargeGain;

    /// <summary>Dono do documento: CPF inteiro (11) ou raiz do CNPJ (8 primeiros de 14). Outra coisa: null.</summary>
    public static string? OwnerRoot(string? taxId)
    {
        var digits = new string((taxId ?? "").Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            14 => digits[..8],
            11 => digits,
            _ => null,
        };
    }

    /// <summary>Produtor com 2+ inscrições = 2+ parceiros com o mesmo CPF/raiz de CNPJ (D6).</summary>
    public static bool SameOwner(string? a, string? b)
    {
        var root = OwnerRoot(a);
        return root != null && root == OwnerRoot(b);
    }

    /// <summary>
    /// KG por unidade da linha da NF. KG (ou vazio) = 1; a UM comercial do produto = o fator dela; qualquer outra
    /// coisa = null (o vínculo é recusado e a linha aparece sem Disponível).
    /// </summary>
    public static decimal? KgFactor(string? lineUnit, ItemComplement? complement)
    {
        if (string.IsNullOrWhiteSpace(lineUnit) || string.Equals(lineUnit.Trim(), BaseUnit, StringComparison.OrdinalIgnoreCase))
            return 1m;

        if (complement is { CommercialFactor: > 0 } &&
            string.Equals(lineUnit.Trim(), complement.CommercialUnitOfMeasureCode?.Trim(), StringComparison.OrdinalIgnoreCase))
            return complement.CommercialFactor;

        return null;
    }

    public static FiscalReconciliationStatus Status(decimal physical, decimal fiscal, bool hasLinks, bool confirmed)
    {
        if (!hasLinks)
            return FiscalReconciliationStatus.WithoutInvoice;

        return fiscal == physical || confirmed
            ? FiscalReconciliationStatus.Reconciled
            : FiscalReconciliationStatus.Partial;
    }

    /// <summary>Mesmos rótulos de <c>formatter.formatStorageTransactionType</c> no frontend.</summary>
    public static string TypeName(StorageTransactionType type) => type switch
    {
        StorageTransactionType.Purchase => "Compra",
        StorageTransactionType.PurchaseReturn => "Dev.Compra",
        StorageTransactionType.PurchaseQtyComplement => "Compl.Qtd.",
        StorageTransactionType.PurchaseDischargeLoss => "Quebra Descarga",
        StorageTransactionType.PurchaseDischargeGain => "Sobra Descarga",
        _ => type.ToString(),
    };

    public static string StatusName(FiscalReconciliationStatus status) => status switch
    {
        FiscalReconciliationStatus.WithoutInvoice => "Sem NF",
        FiscalReconciliationStatus.Partial => "Parcial",
        _ => "Conciliado",
    };

    /// <summary>"número-série" da NF; sem número fiscal, o número interno do documento.</summary>
    public static string TaxDocument(string? taxNumber, string? series, string? internalNumber)
    {
        if (string.IsNullOrWhiteSpace(taxNumber))
            return internalNumber?.Trim() ?? "";

        return string.IsNullOrWhiteSpace(series) ? taxNumber.Trim() : $"{taxNumber.Trim()}-{series.Trim()}";
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationRulesTests"`
Expected: todos passam.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Infra/Queries/PurchaseFiscalReconciliationRules.cs SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationRulesTests.cs
git commit -m "feat(invoice): regras da conciliação fiscal de compras" -m "Tipo e sentido do romaneio, dono do documento por CPF/raiz de CNPJ, fator para KG e situação Sem NF/Parcial/Conciliado."
```

---

### Task 3: DTOs, seed de teste e consultas compartilhadas

**Files:**
- Create: `SiagroB1.Domain/Dtos/FiscalReconciliationDtos.cs`
- Create: `SiagroB1.Infra/Queries/PurchaseFiscalReconciliationQuery.cs`
- Create: `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/FiscalReconciliationSeed.cs`
- Test: `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationQueryTests.cs`

**Interfaces:**
- Consumes: Task 1 (entidade/colunas), Task 2 (regras).
- Produces:
  - `FiscalReconciliationFilter { string CardCode; string ItemCode; string? ContractCode; DateTime? DateFrom; DateTime? DateTo; string? IssuerCardCode; bool IncludeFullyLinked }`
  - `FiscalReconciliationTransactionDto`, `FiscalReconciliationInvoiceLineDto`, `FiscalReconciliationLinkDto`, `FiscalReconciliationPairDto` (abaixo)
  - `PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(AppDbContext, FiscalReconciliationFilter) : Task<List<FiscalReconciliationTransactionDto>>`
  - `PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(AppDbContext, FiscalReconciliationFilter) : Task<List<FiscalReconciliationInvoiceLineDto>>`
  - `PurchaseFiscalReconciliationQuery.LoadLinksAsync(AppDbContext, IReadOnlyCollection<Guid> transactionKeys) : Task<List<FiscalReconciliationLinkDto>>`
  - `PurchaseFiscalReconciliationQuery.ActiveLinks(AppDbContext) : IQueryable<PurchaseFiscalReconciliation>`
  - `PurchaseFiscalReconciliationQuery.OwnerCardCodesAsync(AppDbContext, string cardCode) : Task<HashSet<string>>`
  - Seed: `FiscalReconciliationSeed.CreateAsync()`, `AddContractAsync`, `AddTransactionAsync`, `AddInvoiceAsync`, `AddPartnerAsync`, `LinkAsync` (abaixo)

- [ ] **Step 1: DTOs (sem teste próprio — exercitados pelas consultas)**

`SiagroB1.Domain/Dtos/FiscalReconciliationDtos.cs`:

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Recorte da Conciliação Fiscal de Compras (spec 2026-10-13 D5): fornecedor e produto obrigatórios.</summary>
public class FiscalReconciliationFilter
{
    public string CardCode { get; set; } = "";
    public string ItemCode { get; set; } = "";
    public string? ContractCode { get; set; }
    public DateTime? DateFrom { get; set; }
    /// <summary>Inclusivo até o fim do dia.</summary>
    public DateTime? DateTo { get; set; }
    /// <summary>Emitente das NFs; vazio = fornecedor + parceiros com o mesmo CPF/raiz de CNPJ (D6).</summary>
    public string? IssuerCardCode { get; set; }
    public bool IncludeFullyLinked { get; set; }
}

public class FiscalReconciliationLinkDto
{
    public Guid Key { get; set; }
    public Guid PurchaseInvoiceItemKey { get; set; }
    public Guid PurchaseInvoiceKey { get; set; }
    public Guid StorageTransactionKey { get; set; }
    public string TaxDocument { get; set; } = "";
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public DateTime? IssueDate { get; set; }
    public decimal Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class FiscalReconciliationTransactionDto
{
    public Guid Key { get; set; }
    public string? Code { get; set; }
    public DateTime? TransactionDate { get; set; }
    public StorageTransactionType TransactionType { get; set; }
    public string TransactionTypeName { get; set; } = "";
    public string CardCode { get; set; } = "";
    public string? CardName { get; set; }
    public string ItemCode { get; set; } = "";
    public string? TruckCode { get; set; }
    public string UnitOfMeasureCode { get; set; } = "";
    public List<string> ContractCodes { get; set; } = [];
    public string ContractCodesText => string.Join(", ", ContractCodes);
    public decimal PhysicalQuantity { get; set; }
    public decimal FiscalQuantity { get; set; }
    public decimal Difference => FiscalQuantity - PhysicalQuantity;
    public FiscalReconciliationStatus Status { get; set; }
    public string StatusName { get; set; } = "";
    public DateTime? FiscalReconciledAt { get; set; }
    public string? FiscalReconciledBy { get; set; }
    public List<FiscalReconciliationLinkDto> Links { get; set; } = [];
    public string InvoiceNumbersText => string.Join(", ", Links.Select(l => l.TaxDocument).Distinct());
}

public class FiscalReconciliationInvoiceLineDto
{
    public Guid PurchaseInvoiceItemKey { get; set; }
    public Guid PurchaseInvoiceKey { get; set; }
    public PurchaseInvoiceType InvoiceType { get; set; }
    public string TaxDocument { get; set; } = "";
    public DateTime? IssueDate { get; set; }
    public string CardCode { get; set; } = "";
    public string? CardName { get; set; }
    /// <summary>Emitente é o fornecedor ou tem o mesmo CPF/raiz de CNPJ. False = aviso na tela (D6).</summary>
    public bool IssuerMatchesSupplier { get; set; }
    public string? UsageName { get; set; }
    public string? ContractCode { get; set; }
    public string? UnitOfMeasureCode { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Null quando a UM da linha não converte para KG (sem fator no Complemento do Produto).</summary>
    public decimal? QuantityKg { get; set; }
    public decimal LinkedQuantity { get; set; }
    public decimal? AvailableQuantity => QuantityKg - LinkedQuantity;
}

/// <summary>Par sugerido/gravado: quanto da linha da NF vai para o romaneio (KG). <c>Quantity</c> 0 = remover.</summary>
public class FiscalReconciliationPairDto
{
    public Guid PurchaseInvoiceItemKey { get; set; }
    public Guid StorageTransactionKey { get; set; }
    public string TaxDocument { get; set; } = "";
    public string? TransactionCode { get; set; }
    public decimal CurrentQuantity { get; set; }
    public decimal Quantity { get; set; }
}
```

- [ ] **Step 2: Seed de teste**

`SiagroB1.Application.Tests/PurchaseFiscalReconciliations/FiscalReconciliationSeed.cs`:

```csharp
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

/// <summary>Fornecedor F0001 (CPF 12345678901), produto SOJA, romaneios e NFs de entrada para a conciliação.</summary>
internal sealed class FiscalReconciliationSeed
{
    public const string Supplier = "F0001";
    public const string Item = "SOJA";
    public UnitOfWork Db { get; }
    private int _seq;

    private FiscalReconciliationSeed(UnitOfWork db) => Db = db;

    public static async Task<FiscalReconciliationSeed> CreateAsync(UnitOfWork? db = null)
    {
        var seed = new FiscalReconciliationSeed(db ?? TestDb.CreateUnitOfWork());
        await seed.AddPartnerAsync(Supplier, "Produtor Fazenda A", "12345678901");
        return seed;
    }

    public async Task AddPartnerAsync(string cardCode, string name, string? taxId)
    {
        Db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = cardCode, CardName = name, TaxId = taxId, CardType = "S" });
        await Db.SaveChangesAsync();
    }

    public async Task<PurchaseContract> AddContractAsync(string code = "PC-1")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = Supplier, ItemCode = Item, UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "25/26", Status = ContractStatus.Approved, TotalVolume = 1_000_000m,
            FreightTerms = FreightTerms.Cif, DeliveryLocationCode = "01",
        };
        Db.Context.PurchaseContracts.Add(contract);
        await Db.SaveChangesAsync();
        return contract;
    }

    public async Task<StorageTransaction> AddTransactionAsync(
        decimal net, StorageTransactionType type = StorageTransactionType.Purchase, PurchaseContract? contract = null,
        DateTime? date = null, string? code = null, string cardCode = Supplier,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed)
    {
        var tx = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code ?? $"RM{++_seq:000000}", BranchCode = "01", CardCode = cardCode,
            ItemCode = Item, UnitOfMeasureCode = "KG", WarehouseCode = "PORTO", TruckCode = "ABC1D23",
            TransactionType = type, TransactionStatus = status, TransactionDate = date ?? new DateTime(2026, 10, 10),
            GrossWeight = net, NetWeight = net,
        };
        Db.Context.StorageTransactions.Add(tx);
        if (contract != null)
            Db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
                { Key = Guid.NewGuid(), PurchaseContractKey = contract.Key, StorageTransactionKey = tx.Key, Volume = net });
        await Db.SaveChangesAsync();
        return tx;
    }

    public async Task<(PurchaseInvoice Invoice, PurchaseInvoiceItem Line)> AddInvoiceAsync(
        decimal quantity, string unit = "KG", PurchaseInvoiceType type = PurchaseInvoiceType.Normal,
        string cardCode = Supplier, PurchaseContract? contract = null, DateTime? issueDate = null,
        InvoiceStatus status = InvoiceStatus.Confirmed, string? taxNumber = null, Guid? producerInvoiceKey = null,
        string itemCode = Item)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = cardCode, CardName = cardCode, InvoiceType = type,
            InvoiceStatus = status, InvoiceNumber = $"DE{++_seq:000000}", TaxDocumentNumber = taxNumber ?? $"{_seq:000000000}",
            TaxDocumentSeries = "1", IssueDate = issueDate ?? new DateTime(2026, 10, 9), ProducerInvoiceKey = producerInvoiceKey,
        };
        var line = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice.Key, ItemCode = itemCode, ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = unit, Quantity = quantity, UnitPrice = 2m, PurchaseContractKey = contract?.Key,
            UsageName = "Compra para comercialização",
        };
        Db.Context.PurchaseInvoices.Add(invoice);
        Db.Context.PurchaseInvoicesItems.Add(line);
        await Db.SaveChangesAsync();
        return (invoice, line);
    }

    public async Task<PurchaseFiscalReconciliation> LinkAsync(PurchaseInvoiceItem line, StorageTransaction tx, decimal quantity)
    {
        var link = new PurchaseFiscalReconciliation
            { Key = Guid.NewGuid(), PurchaseInvoiceItemKey = line.Key!.Value, StorageTransactionKey = tx.Key, Quantity = quantity };
        Db.Context.PurchaseFiscalReconciliations.Add(link);
        await Db.SaveChangesAsync();
        return link;
    }
}
```

Se `PurchaseInvoice`/`PurchaseContract` tiverem outro `required` que o compilador acuse, preencha com valor neutro
(ex.: `HarvestSeasonCode`), conferindo no `ThirdPartyPurchaseSeed`/`PurchaseDischargeTestContext`.

- [ ] **Step 3: Escrever os testes das consultas (falham)**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.Queries;
using static SiagroB1.Application.Tests.PurchaseFiscalReconciliations.FiscalReconciliationSeed;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

public class PurchaseFiscalReconciliationQueryTests
{
    private static FiscalReconciliationFilter Filter(Action<FiscalReconciliationFilter>? change = null)
    {
        var f = new FiscalReconciliationFilter { CardCode = Supplier, ItemCode = Item };
        change?.Invoke(f);
        return f;
    }

    [Fact]
    public async Task Transactions_bring_physical_fiscal_status_contracts_and_links()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var contract = await seed.AddContractAsync("PC-7");
        var tx = await seed.AddTransactionAsync(29_820m, contract: contract, code: "RM000001");
        var (_, line) = await seed.AddInvoiceAsync(30_000m, taxNumber: "000123");
        await seed.LinkAsync(line, tx, 30_000m);

        var row = Assert.Single(await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(seed.Db.Context, Filter()));

        Assert.Equal("RM000001", row.Code);
        Assert.Equal(29_820m, row.PhysicalQuantity);
        Assert.Equal(30_000m, row.FiscalQuantity);
        Assert.Equal(180m, row.Difference);
        Assert.Equal(FiscalReconciliationStatus.Partial, row.Status);
        Assert.Equal("Parcial", row.StatusName);
        Assert.Equal(["PC-7"], row.ContractCodes);
        Assert.Equal("000123-1", row.InvoiceNumbersText);
    }

    [Fact]
    public async Task Transactions_skip_cancelled_non_purchase_other_supplier_and_ignore_cancelled_invoice_links()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(100m);
        await seed.AddTransactionAsync(100m, status: StorageTransactionsStatus.Cancelled);
        await seed.AddTransactionAsync(100m, type: StorageTransactionType.Receipt);
        await seed.AddTransactionAsync(100m, cardCode: "F9999");
        var (_, line) = await seed.AddInvoiceAsync(100m, status: InvoiceStatus.Cancelled);
        await seed.LinkAsync(line, tx, 100m);

        var row = Assert.Single(await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(seed.Db.Context, Filter()));

        Assert.Equal(FiscalReconciliationStatus.WithoutInvoice, row.Status);
        Assert.Equal(0m, row.FiscalQuantity);
    }

    [Fact]
    public async Task Transactions_filter_by_contract_code_and_inclusive_period()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var c1 = await seed.AddContractAsync("PC-1");
        var c2 = await seed.AddContractAsync("PC-2");
        await seed.AddTransactionAsync(10m, contract: c1, date: new DateTime(2026, 10, 31, 23, 0, 0), code: "RM-A");
        await seed.AddTransactionAsync(10m, contract: c2, date: new DateTime(2026, 10, 15), code: "RM-B");
        await seed.AddTransactionAsync(10m, contract: c1, date: new DateTime(2026, 11, 1), code: "RM-C");

        var rows = await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(seed.Db.Context, Filter(f =>
        {
            f.ContractCode = "PC-1";
            f.DateFrom = new DateTime(2026, 10, 1);
            f.DateTo = new DateTime(2026, 10, 31);
        }));

        Assert.Equal(["RM-A"], rows.Select(r => r.Code));
    }

    [Fact]
    public async Task Confirmed_transaction_is_reconciled_even_with_difference()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(100m);
        tx.FiscalReconciledAt = DateTime.Now;
        var (_, line) = await seed.AddInvoiceAsync(90m);
        await seed.LinkAsync(line, tx, 90m);

        var row = Assert.Single(await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(seed.Db.Context, Filter()));

        Assert.Equal(FiscalReconciliationStatus.Reconciled, row.Status);
    }

    [Fact]
    public async Task Invoice_lines_default_to_supplier_and_same_owner_issuers()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        await seed.AddPartnerAsync("F0002", "Produtor Fazenda B (outra IE)", "123.456.789-01");
        await seed.AddPartnerAsync("F0003", "Outro produtor", "98765432100");
        await seed.AddInvoiceAsync(100m, cardCode: Supplier, taxNumber: "1");
        await seed.AddInvoiceAsync(200m, cardCode: "F0002", taxNumber: "2");
        await seed.AddInvoiceAsync(300m, cardCode: "F0003", taxNumber: "3");

        var lines = await PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(seed.Db.Context, Filter());

        Assert.Equal(["1-1", "2-1"], lines.Select(l => l.TaxDocument).Order());
        Assert.All(lines, l => Assert.True(l.IssuerMatchesSupplier));
    }

    [Fact]
    public async Task Invoice_lines_with_explicit_other_issuer_carry_the_warning_flag()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        await seed.AddPartnerAsync("F0003", "Outro produtor", "98765432100");
        await seed.AddInvoiceAsync(300m, cardCode: "F0003", taxNumber: "3");

        var line = Assert.Single(await PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(
            seed.Db.Context, Filter(f => f.IssuerCardCode = "F0003")));

        Assert.False(line.IssuerMatchesSupplier);
    }

    [Fact]
    public async Task Invoice_lines_convert_to_kg_hide_fully_linked_and_skip_cancelled_and_counter_notes()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        seed.Db.Context.ItemComplements.Add(new ItemComplement { ItemCode = Item, CommercialUnitOfMeasureCode = "SC", CommercialFactor = 60m });
        var tx = await seed.AddTransactionAsync(6_000m);
        await seed.AddInvoiceAsync(100m, unit: "SC", taxNumber: "1");            // 6.000 KG
        var (_, full) = await seed.AddInvoiceAsync(500m, taxNumber: "2");       // totalmente vinculada
        await seed.LinkAsync(full, tx, 500m);
        await seed.AddInvoiceAsync(10m, unit: "TON", taxNumber: "3");            // sem fator
        await seed.AddInvoiceAsync(10m, status: InvoiceStatus.Cancelled, taxNumber: "4");
        await seed.AddInvoiceAsync(10m, producerInvoiceKey: Guid.NewGuid(), taxNumber: "5"); // contra-nota

        var lines = await PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(seed.Db.Context, Filter());

        Assert.Equal(["1-1", "3-1"], lines.Select(l => l.TaxDocument).Order());
        Assert.Equal(6_000m, lines.Single(l => l.TaxDocument == "1-1").AvailableQuantity);
        Assert.Null(lines.Single(l => l.TaxDocument == "3-1").QuantityKg);

        var all = await PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(seed.Db.Context, Filter(f => f.IncludeFullyLinked = true));
        Assert.Equal(0m, all.Single(l => l.TaxDocument == "2-1").AvailableQuantity);
    }

    [Fact]
    public void Queries_translate_to_sql()
    {
        using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none").Options);
        var filter = Filter(f => { f.ContractCode = "PC-1"; f.DateFrom = DateTime.Today; f.DateTo = DateTime.Today; });

        Assert.NotEmpty(PurchaseFiscalReconciliationQuery.TransactionsBase(ctx, filter).ToQueryString());
        Assert.NotEmpty(PurchaseFiscalReconciliationQuery.LinksQuery(ctx, [Guid.NewGuid()]).ToQueryString());
        Assert.NotEmpty(PurchaseFiscalReconciliationQuery.ContractsQuery(ctx, [Guid.NewGuid()]).ToQueryString());
        Assert.NotEmpty(PurchaseFiscalReconciliationQuery.InvoiceLinesBase(ctx, filter, ["F0001"]).ToQueryString());
    }
}
```

- [ ] **Step 4: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationQueryTests"`
Expected: erro de compilação — `PurchaseFiscalReconciliationQuery` não existe.

- [ ] **Step 5: Implementar as consultas**

`SiagroB1.Infra/Queries/PurchaseFiscalReconciliationQuery.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Leitura da Conciliação Fiscal de Compras (spec 2026-10-13 §5). Mora no Infra porque a tela (Web) e o relatório
/// (Reports, que não referencia a Application) precisam dos MESMOS números. Os IQueryable são públicos para o teste de
/// tradução SQL; a composição (contratos, NFs, status) é feita em memória sobre poucas linhas.
/// </summary>
public static class PurchaseFiscalReconciliationQuery
{
    /// <summary>Vínculo que conta: romaneio não cancelado e NF não cancelada (defesa em profundidade do §4.2).</summary>
    public static IQueryable<PurchaseFiscalReconciliation> ActiveLinks(AppDbContext ctx) =>
        ctx.PurchaseFiscalReconciliations.AsNoTracking()
            .Where(l => l.StorageTransaction!.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                        l.PurchaseInvoiceItem!.PurchaseInvoice!.InvoiceStatus != InvoiceStatus.Cancelled);

    public static IQueryable<StorageTransaction> TransactionsBase(AppDbContext ctx, FiscalReconciliationFilter f)
    {
        var query = ctx.StorageTransactions.AsNoTracking()
            .Where(t => R.LinkableTypes.Contains(t.TransactionType) &&
                        t.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                        t.CardCode == f.CardCode && t.ItemCode == f.ItemCode);

        if (f.DateFrom is { } from)
        {
            var start = from.Date;
            query = query.Where(t => t.TransactionDate >= start);
        }

        if (f.DateTo is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(t => t.TransactionDate < end);
        }

        if (!string.IsNullOrWhiteSpace(f.ContractCode))
        {
            var code = f.ContractCode.Trim();
            query = query.Where(t => ctx.PurchaseContractsAllocations
                .Any(a => a.StorageTransactionKey == t.Key && a.PurchaseContract!.Code == code));
        }

        return query;
    }

    public static IQueryable<FiscalReconciliationLinkDto> LinksQuery(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys) =>
        ActiveLinks(ctx)
            .Where(l => transactionKeys.Contains(l.StorageTransactionKey))
            .Select(l => new FiscalReconciliationLinkDto
            {
                Key = l.Key,
                PurchaseInvoiceItemKey = l.PurchaseInvoiceItemKey,
                PurchaseInvoiceKey = l.PurchaseInvoiceItem!.PurchaseInvoiceKey!.Value,
                StorageTransactionKey = l.StorageTransactionKey,
                // TaxDocument é montado em memória (LoadLinksAsync): guarda os pedaços aqui.
                TaxDocument = (l.PurchaseInvoiceItem.PurchaseInvoice!.TaxDocumentNumber ?? "") + "|" +
                              (l.PurchaseInvoiceItem.PurchaseInvoice.TaxDocumentSeries ?? "") + "|" +
                              (l.PurchaseInvoiceItem.PurchaseInvoice.InvoiceNumber ?? ""),
                CardCode = l.PurchaseInvoiceItem.PurchaseInvoice.CardCode,
                CardName = l.PurchaseInvoiceItem.PurchaseInvoice.CardName,
                IssueDate = l.PurchaseInvoiceItem.PurchaseInvoice.IssueDate,
                Quantity = l.Quantity,
                CreatedAt = l.CreatedAt,
                CreatedBy = l.CreatedBy,
            });

    public sealed class ContractRef
    {
        public Guid StorageTransactionKey { get; set; }
        public string? Code { get; set; }
    }

    public static IQueryable<ContractRef> ContractsQuery(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys) =>
        ctx.PurchaseContractsAllocations.AsNoTracking()
            .Where(a => transactionKeys.Contains(a.StorageTransactionKey))
            .Select(a => new ContractRef { StorageTransactionKey = a.StorageTransactionKey, Code = a.PurchaseContract!.Code });

    public static async Task<List<FiscalReconciliationLinkDto>> LoadLinksAsync(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys)
    {
        var links = await LinksQuery(ctx, transactionKeys).ToListAsync();
        foreach (var link in links)
        {
            var parts = link.TaxDocument.Split('|');
            link.TaxDocument = R.TaxDocument(parts[0], parts[1], parts[2]);
        }

        return links.OrderBy(l => l.IssueDate).ThenBy(l => l.TaxDocument, StringComparer.Ordinal).ToList();
    }

    public static async Task<List<FiscalReconciliationTransactionDto>> LoadTransactionsAsync(AppDbContext ctx, FiscalReconciliationFilter f)
    {
        var transactions = await TransactionsBase(ctx, f)
            .OrderBy(t => t.TransactionDate).ThenBy(t => t.Code)
            .Select(t => new
            {
                t.Key, t.Code, t.TransactionDate, t.TransactionType, t.CardCode, t.CardName, t.ItemCode, t.TruckCode,
                t.UnitOfMeasureCode, t.NetWeight, t.FiscalReconciledAt, t.FiscalReconciledBy,
            })
            .ToListAsync();

        var keys = transactions.Select(t => t.Key).ToList();
        var links = (await LoadLinksAsync(ctx, keys)).ToLookup(l => l.StorageTransactionKey);
        var contracts = (await ContractsQuery(ctx, keys).ToListAsync()).ToLookup(c => c.StorageTransactionKey);

        return transactions.Select(t =>
        {
            var own = links[t.Key].ToList();
            var fiscal = own.Sum(l => l.Quantity);
            var status = R.Status(t.NetWeight, fiscal, own.Count > 0, t.FiscalReconciledAt != null);
            return new FiscalReconciliationTransactionDto
            {
                Key = t.Key, Code = t.Code, TransactionDate = t.TransactionDate, TransactionType = t.TransactionType,
                TransactionTypeName = R.TypeName(t.TransactionType), CardCode = t.CardCode, CardName = t.CardName,
                ItemCode = t.ItemCode, TruckCode = t.TruckCode, UnitOfMeasureCode = t.UnitOfMeasureCode,
                ContractCodes = contracts[t.Key].Select(c => c.Code ?? "").Distinct().Order(StringComparer.Ordinal).ToList(),
                PhysicalQuantity = t.NetWeight, FiscalQuantity = fiscal, Status = status, StatusName = R.StatusName(status),
                FiscalReconciledAt = t.FiscalReconciledAt, FiscalReconciledBy = t.FiscalReconciledBy, Links = own,
            };
        }).ToList();
    }

    /// <summary>Fornecedor + parceiros com o mesmo CPF/raiz de CNPJ (D6). Sem documento: só o próprio fornecedor.</summary>
    public static async Task<HashSet<string>> OwnerCardCodesAsync(AppDbContext ctx, string cardCode)
    {
        var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cardCode };
        var taxId = await ctx.BusinessPartners.AsNoTracking()
            .Where(bp => bp.CardCode == cardCode).Select(bp => bp.TaxId).FirstOrDefaultAsync();
        var root = R.OwnerRoot(taxId);
        if (root == null)
            return owners;

        // O TaxId pode estar mascarado no cadastro: candidatos pelos 3 primeiros dígitos, decisão em memória.
        var prefix = root[..3];
        var candidates = await ctx.BusinessPartners.AsNoTracking()
            .Where(bp => bp.TaxId != null && bp.TaxId.Contains(prefix))
            .Select(bp => new { bp.CardCode, bp.TaxId })
            .ToListAsync();

        foreach (var c in candidates.Where(c => R.SameOwner(c.TaxId, taxId)))
            owners.Add(c.CardCode);

        return owners;
    }

    public sealed class InvoiceLineRow
    {
        public Guid ItemKey { get; set; }
        public Guid InvoiceKey { get; set; }
        public PurchaseInvoiceType InvoiceType { get; set; }
        public string? TaxDocumentNumber { get; set; }
        public string? TaxDocumentSeries { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime? IssueDate { get; set; }
        public string CardCode { get; set; } = "";
        public string? CardName { get; set; }
        public string? UsageName { get; set; }
        public string? ContractCode { get; set; }
        public string? UnitOfMeasureCode { get; set; }
        public decimal Quantity { get; set; }
        public decimal Linked { get; set; }
    }

    public static IQueryable<InvoiceLineRow> InvoiceLinesBase(AppDbContext ctx, FiscalReconciliationFilter f, IReadOnlyCollection<string> issuers)
    {
        var query = ctx.PurchaseInvoicesItems.AsNoTracking()
            .Where(l => l.ItemCode == f.ItemCode &&
                        l.PurchaseInvoice!.InvoiceStatus != InvoiceStatus.Cancelled &&
                        l.PurchaseInvoice.ProducerInvoiceKey == null &&      // contra-nota: vincula-se a escrituração
                        issuers.Contains(l.PurchaseInvoice.CardCode));

        if (f.DateFrom is { } from)
        {
            var start = from.Date;
            query = query.Where(l => l.PurchaseInvoice!.IssueDate >= start);
        }

        if (f.DateTo is { } to)
        {
            var end = to.Date.AddDays(1);
            query = query.Where(l => l.PurchaseInvoice!.IssueDate < end);
        }

        if (!string.IsNullOrWhiteSpace(f.ContractCode))
        {
            var code = f.ContractCode.Trim();
            query = query.Where(l => l.PurchaseContractKey == null || l.PurchaseContract!.Code == code);
        }

        return query.Select(l => new InvoiceLineRow
        {
            ItemKey = l.Key!.Value,
            InvoiceKey = l.PurchaseInvoiceKey!.Value,
            InvoiceType = l.PurchaseInvoice!.InvoiceType,
            TaxDocumentNumber = l.PurchaseInvoice.TaxDocumentNumber,
            TaxDocumentSeries = l.PurchaseInvoice.TaxDocumentSeries,
            InvoiceNumber = l.PurchaseInvoice.InvoiceNumber,
            IssueDate = l.PurchaseInvoice.IssueDate,
            CardCode = l.PurchaseInvoice.CardCode,
            CardName = l.PurchaseInvoice.CardName,
            UsageName = l.UsageName,
            ContractCode = l.PurchaseContract!.Code,
            UnitOfMeasureCode = l.UnitOfMeasureCode,
            Quantity = l.Quantity,
            Linked = ctx.PurchaseFiscalReconciliations
                .Where(r => r.PurchaseInvoiceItemKey == l.Key &&
                            r.StorageTransaction!.TransactionStatus != StorageTransactionsStatus.Cancelled)
                .Sum(r => (decimal?)r.Quantity) ?? 0m,
        });
    }

    public static async Task<List<FiscalReconciliationInvoiceLineDto>> LoadInvoiceLinesAsync(AppDbContext ctx, FiscalReconciliationFilter f)
    {
        var owners = await OwnerCardCodesAsync(ctx, f.CardCode);
        IReadOnlyCollection<string> issuers = string.IsNullOrWhiteSpace(f.IssuerCardCode) ? owners : [f.IssuerCardCode.Trim()];
        var complement = await ctx.ItemComplements.AsNoTracking().FirstOrDefaultAsync(c => c.ItemCode == f.ItemCode);

        var rows = await InvoiceLinesBase(ctx, f, issuers.ToList()).ToListAsync();

        return rows
            .Select(r =>
            {
                var factor = R.KgFactor(r.UnitOfMeasureCode, complement);
                return new FiscalReconciliationInvoiceLineDto
                {
                    PurchaseInvoiceItemKey = r.ItemKey, PurchaseInvoiceKey = r.InvoiceKey, InvoiceType = r.InvoiceType,
                    TaxDocument = R.TaxDocument(r.TaxDocumentNumber, r.TaxDocumentSeries, r.InvoiceNumber),
                    IssueDate = r.IssueDate, CardCode = r.CardCode, CardName = r.CardName,
                    IssuerMatchesSupplier = owners.Contains(r.CardCode), UsageName = r.UsageName,
                    ContractCode = r.ContractCode, UnitOfMeasureCode = r.UnitOfMeasureCode, Quantity = r.Quantity,
                    QuantityKg = factor * r.Quantity, LinkedQuantity = r.Linked,
                };
            })
            .Where(l => f.IncludeFullyLinked || l.QuantityKg == null || l.AvailableQuantity > 0)
            .OrderBy(l => l.IssueDate).ThenBy(l => l.TaxDocument, StringComparer.Ordinal)
            .ToList();
    }
}
```

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationQueryTests"`
Expected: todos passam. Se `Queries_translate_to_sql` falhar em `LinksQuery`/`InvoiceLinesBase`, a mensagem do EF diz qual
expressão não traduz — corrija a projeção (nunca chame método C# dentro do `Select` do IQueryable).

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Domain/Dtos/FiscalReconciliationDtos.cs SiagroB1.Infra/Queries/PurchaseFiscalReconciliationQuery.cs SiagroB1.Application.Tests/PurchaseFiscalReconciliations/FiscalReconciliationSeed.cs SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationQueryTests.cs
git commit -m "feat(invoice): consultas da conciliação fiscal de compras" -m "Romaneios com físico, fiscal, diferença e situação; linhas de NF do fornecedor e das outras inscrições do mesmo CPF/CNPJ, em KG. No Infra para a tela e o relatório lerem os mesmos números."
```

---

### Task 4: Distribuição sugerida

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationPlanner.cs`
- Test: `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationPlannerTests.cs`

**Interfaces:**
- Consumes: `PurchaseFiscalReconciliationRules.SenseMatches`.
- Produces:
  - `record PlannerLine(Guid Key, PurchaseInvoiceType InvoiceType, DateTime? IssueDate, string Number, decimal Available)`
  - `record PlannerTransaction(Guid Key, StorageTransactionType Type, DateTime? Date, string Code, decimal OpenBalance)`
  - `record PlannerPair(Guid LineKey, Guid TransactionKey, decimal Quantity)`
  - `static List<PlannerPair> PurchaseFiscalReconciliationPlanner.Suggest(IReadOnlyList<PlannerLine>, IReadOnlyList<PlannerTransaction>)`

- [ ] **Step 1: Testes (falham)**

```csharp
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

public class PurchaseFiscalReconciliationPlannerTests
{
    private static readonly Guid L1 = Guid.NewGuid(), L2 = Guid.NewGuid(), T1 = Guid.NewGuid(), T2 = Guid.NewGuid();

    private static PlannerLine Line(Guid key, decimal available, int day = 1, PurchaseInvoiceType type = PurchaseInvoiceType.Normal) =>
        new(key, type, new DateTime(2026, 10, day), key.ToString(), available);

    private static PlannerTransaction Tx(Guid key, decimal open, int day = 1, StorageTransactionType type = StorageTransactionType.Purchase) =>
        new(key, type, new DateTime(2026, 10, day), key.ToString(), open);

    [Fact]
    public void One_invoice_two_trucks_fob_leftover_goes_to_last_truck()
    {
        var pairs = PurchaseFiscalReconciliationPlanner.Suggest([Line(L1, 60_000m)], [Tx(T1, 29_820m, 1), Tx(T2, 29_900m, 2)]);

        Assert.Equal([new PlannerPair(L1, T1, 29_820m), new PlannerPair(L1, T2, 30_180m)], pairs);
    }

    [Fact]
    public void Two_invoices_one_truck_both_go_whole()
    {
        var pairs = PurchaseFiscalReconciliationPlanner.Suggest([Line(L2, 500m, 2), Line(L1, 30_000m, 1)], [Tx(T1, 30_200m)]);

        Assert.Equal([new PlannerPair(L1, T1, 30_000m), new PlannerPair(L2, T1, 500m)], pairs);
    }

    [Fact]
    public void Smaller_invoice_fills_trucks_in_date_order_and_stops()
    {
        var pairs = PurchaseFiscalReconciliationPlanner.Suggest([Line(L1, 40_000m)], [Tx(T2, 30_000m, 2), Tx(T1, 30_000m, 1)]);

        Assert.Equal([new PlannerPair(L1, T1, 30_000m), new PlannerPair(L1, T2, 10_000m)], pairs);
    }

    [Fact]
    public void Return_invoice_only_reaches_return_and_discharge_loss()
    {
        var pairs = PurchaseFiscalReconciliationPlanner.Suggest(
            [Line(L1, 300m, type: PurchaseInvoiceType.Return)],
            [Tx(T1, 1_000m, 1, StorageTransactionType.Purchase), Tx(T2, 300m, 2, StorageTransactionType.PurchaseDischargeLoss)]);

        Assert.Equal([new PlannerPair(L1, T2, 300m)], pairs);
    }

    [Fact]
    public void Already_covered_truck_only_receives_leftover_when_it_is_the_last()
    {
        var pairs = PurchaseFiscalReconciliationPlanner.Suggest([Line(L1, 100m)], [Tx(T1, 0m, 1), Tx(T2, -50m, 2)]);

        Assert.Equal([new PlannerPair(L1, T2, 100m)], pairs);
    }

    [Fact]
    public void Line_without_available_or_matching_truck_suggests_nothing()
    {
        Assert.Empty(PurchaseFiscalReconciliationPlanner.Suggest([Line(L1, 0m)], [Tx(T1, 100m)]));
        Assert.Empty(PurchaseFiscalReconciliationPlanner.Suggest(
            [Line(L1, 10m)], [Tx(T1, 100m, type: StorageTransactionType.PurchaseReturn)]));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationPlannerTests"`
Expected: erro de compilação.

- [ ] **Step 3: Implementar**

```csharp
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.PurchaseFiscalReconciliations;

public sealed record PlannerLine(Guid Key, PurchaseInvoiceType InvoiceType, DateTime? IssueDate, string Number, decimal Available);

/// <summary><c>OpenBalance</c> = físico − fiscal já vinculado (pode ser negativo).</summary>
public sealed record PlannerTransaction(Guid Key, StorageTransactionType Type, DateTime? Date, string Code, decimal OpenBalance);

public sealed record PlannerPair(Guid LineKey, Guid TransactionKey, decimal Quantity);

/// <summary>
/// Distribuição sugerida do diálogo "Vincular" (spec 2026-10-13 §6.2). NFs por emissão, romaneios por data; cada NF
/// preenche o saldo aberto dos romaneios na ordem; o que sobrar da NF depois do último romaneio vai para ele (FOB: NF
/// no peso de origem maior que a balança). Pares de sentido inválido nunca são sugeridos. Só sugere — o usuário edita.
/// </summary>
public static class PurchaseFiscalReconciliationPlanner
{
    public static List<PlannerPair> Suggest(IReadOnlyList<PlannerLine> lines, IReadOnlyList<PlannerTransaction> transactions)
    {
        var open = transactions.ToDictionary(t => t.Key, t => Math.Max(t.OpenBalance, 0m));
        var orderedTransactions = transactions
            .OrderBy(t => t.Date).ThenBy(t => t.Code, StringComparer.Ordinal).ToList();
        var result = new List<PlannerPair>();

        foreach (var line in lines.OrderBy(l => l.IssueDate).ThenBy(l => l.Number, StringComparer.Ordinal))
        {
            var eligible = orderedTransactions
                .Where(t => PurchaseFiscalReconciliationRules.SenseMatches(line.InvoiceType, t.Type)).ToList();
            var remaining = line.Available;
            if (remaining <= 0 || eligible.Count == 0)
                continue;

            var assigned = new Dictionary<Guid, decimal>();
            foreach (var tx in eligible)
            {
                if (remaining <= 0) break;
                var take = Math.Min(open[tx.Key], remaining);
                if (take <= 0) continue;
                assigned[tx.Key] = take;
                open[tx.Key] -= take;
                remaining -= take;
            }

            if (remaining > 0)
            {
                var last = eligible[^1].Key;
                assigned[last] = assigned.GetValueOrDefault(last) + remaining;
            }

            result.AddRange(eligible.Where(t => assigned.ContainsKey(t.Key))
                .Select(t => new PlannerPair(line.Key, t.Key, assigned[t.Key])));
        }

        return result;
    }
}
```

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationPlannerTests"`
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationPlanner.cs SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationPlannerTests.cs
git commit -m "feat(invoice): distribuição sugerida da NF entre romaneios" -m "NF preenche o saldo aberto dos romaneios por data; a sobra da NF (FOB, peso de origem maior que a balança) fica no último."
```

---

### Task 5: Serviço — sugerir, gravar, remover, confirmar

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationServiceTests.cs`

**Interfaces:**
- Consumes: Tasks 2–4; `IUnitOfWork`; `DefaultException`, `NotFoundException` (`SiagroB1.Domain.Exceptions`).
- Produces (`PurchaseFiscalReconciliationService(IUnitOfWork db)`):
  - `Task<List<FiscalReconciliationTransactionDto>> ListTransactionsAsync(FiscalReconciliationFilter)`
  - `Task<List<FiscalReconciliationInvoiceLineDto>> ListInvoiceLinesAsync(FiscalReconciliationFilter)`
  - `Task<List<FiscalReconciliationLinkDto>> ListLinksAsync(Guid transactionKey)`
  - `Task<List<FiscalReconciliationPairDto>> SuggestAsync(IReadOnlyList<Guid> invoiceItemKeys, IReadOnlyList<Guid> transactionKeys)`
  - `Task SaveAsync(IReadOnlyList<FiscalReconciliationPairDto> pairs, string userName)` (usa só `PurchaseInvoiceItemKey`, `StorageTransactionKey`, `Quantity`)
  - `Task RemoveAsync(Guid linkKey)`; `Task ConfirmAsync(Guid transactionKey, string userName)`; `Task UnconfirmAsync(Guid transactionKey)`

- [ ] **Step 1: Testes (falham)**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.PurchaseFiscalReconciliations.FiscalReconciliationSeed;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

public class PurchaseFiscalReconciliationServiceTests
{
    private static FiscalReconciliationPairDto Pair(PurchaseInvoiceItem line, StorageTransaction tx, decimal qty) =>
        new() { PurchaseInvoiceItemKey = line.Key!.Value, StorageTransactionKey = tx.Key, Quantity = qty };

    private static async Task<string> RefusalAsync(Func<Task> act) =>
        (await Assert.ThrowsAsync<DefaultException>(act)).Message;

    [Fact]
    public async Task Save_allows_fiscal_above_physical_and_status_is_partial()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(29_820m);
        var (_, line) = await seed.AddInvoiceAsync(30_000m);
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        await service.SaveAsync([Pair(line, tx, 30_000m)], "ana");

        var row = Assert.Single(await service.ListTransactionsAsync(new() { CardCode = Supplier, ItemCode = Item }));
        Assert.Equal(180m, row.Difference);
        Assert.Equal(FiscalReconciliationStatus.Partial, row.Status);
        Assert.Equal("ana", (await seed.Db.Context.PurchaseFiscalReconciliations.SingleAsync()).CreatedBy);
    }

    [Fact]
    public async Task Save_upserts_and_zero_removes_and_clears_confirmation()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(100m);
        var (_, line) = await seed.AddInvoiceAsync(100m);
        await seed.LinkAsync(line, tx, 40m);
        tx.FiscalReconciledAt = DateTime.Now; tx.FiscalReconciledBy = "ana";
        await seed.Db.SaveChangesAsync();
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        await service.SaveAsync([Pair(line, tx, 70m)], "bia");
        Assert.Equal(70m, (await seed.Db.Context.PurchaseFiscalReconciliations.SingleAsync()).Quantity);
        Assert.Null((await seed.Db.Context.StorageTransactions.AsNoTracking().SingleAsync(t => t.Key == tx.Key)).FiscalReconciledAt);

        await service.SaveAsync([Pair(line, tx, 0m)], "bia");
        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations);
    }

    [Fact]
    public async Task Save_refuses_when_lines_total_exceeds_invoice_quantity_and_saves_nothing()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var t1 = await seed.AddTransactionAsync(30_000m);
        var t2 = await seed.AddTransactionAsync(30_000m);
        var (_, line) = await seed.AddInvoiceAsync(50_000m, taxNumber: "777");
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        var message = await RefusalAsync(() => service.SaveAsync([Pair(line, t1, 30_000m), Pair(line, t2, 30_000m)], "ana"));

        Assert.Contains("777-1", message);
        Assert.Contains("passa da quantidade da linha", message);
        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations);
    }

    [Fact]
    public async Task Save_converts_commercial_unit_and_refuses_without_factor()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(6_000m);
        var (_, sc) = await seed.AddInvoiceAsync(100m, unit: "SC");
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        Assert.Contains("Cadastre o fator da unidade SC", await RefusalAsync(() => service.SaveAsync([Pair(sc, tx, 6_000m)], "ana")));

        seed.Db.Context.ItemComplements.Add(new ItemComplement { ItemCode = Item, CommercialUnitOfMeasureCode = "SC", CommercialFactor = 60m });
        await seed.Db.SaveChangesAsync();
        await service.SaveAsync([Pair(sc, tx, 6_000m)], "ana");
        Assert.Single(seed.Db.Context.PurchaseFiscalReconciliations);
    }

    [Fact]
    public async Task Save_refuses_cancelled_invoice_counter_note_wrong_sense_product_and_contract()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var c1 = await seed.AddContractAsync("PC-1");
        var c2 = await seed.AddContractAsync("PC-2");
        var tx = await seed.AddTransactionAsync(100m, contract: c1, code: "RM-1");
        var ret = await seed.AddTransactionAsync(100m, StorageTransactionType.PurchaseReturn, code: "RM-2");
        var cancelledTx = await seed.AddTransactionAsync(100m, status: StorageTransactionsStatus.Cancelled, code: "RM-3");
        var receipt = await seed.AddTransactionAsync(100m, StorageTransactionType.Receipt, code: "RM-4");
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        var (_, cancelled) = await seed.AddInvoiceAsync(100m, status: InvoiceStatus.Cancelled);
        Assert.Contains("cancelad", await RefusalAsync(() => service.SaveAsync([Pair(cancelled, tx, 10m)], "a")));

        var (_, counter) = await seed.AddInvoiceAsync(100m, producerInvoiceKey: Guid.NewGuid());
        Assert.Contains("contra-nota", await RefusalAsync(() => service.SaveAsync([Pair(counter, tx, 10m)], "a")));

        var (_, normal) = await seed.AddInvoiceAsync(100m);
        Assert.Contains("NF de devolução", await RefusalAsync(() => service.SaveAsync([Pair(normal, ret, 10m)], "a")));
        Assert.Contains("RM-3", await RefusalAsync(() => service.SaveAsync([Pair(normal, cancelledTx, 10m)], "a")));
        Assert.Contains("não é romaneio de compra", await RefusalAsync(() => service.SaveAsync([Pair(normal, receipt, 10m)], "a")));

        var (_, milho) = await seed.AddInvoiceAsync(100m, itemCode: "MILHO");
        Assert.Contains("produto", await RefusalAsync(() => service.SaveAsync([Pair(milho, tx, 10m)], "a")));

        var (_, otherContract) = await seed.AddInvoiceAsync(100m, contract: c2);
        Assert.Contains("PC-2", await RefusalAsync(() => service.SaveAsync([Pair(otherContract, tx, 10m)], "a")));

        Assert.Contains("negativa", await RefusalAsync(() => service.SaveAsync([Pair(normal, tx, -1m)], "a")));
        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations);
    }

    [Fact]
    public async Task Save_accepts_other_issuer_without_refusing()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        await seed.AddPartnerAsync("F0003", "Outro produtor", "98765432100");
        var tx = await seed.AddTransactionAsync(100m);
        var (_, line) = await seed.AddInvoiceAsync(100m, cardCode: "F0003");

        await new PurchaseFiscalReconciliationService(seed.Db).SaveAsync([Pair(line, tx, 100m)], "a");

        Assert.Single(seed.Db.Context.PurchaseFiscalReconciliations);
    }

    [Fact]
    public async Task Suggest_adds_current_quantity_and_uses_open_balance()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var t1 = await seed.AddTransactionAsync(30_000m, date: new DateTime(2026, 10, 1));
        var t2 = await seed.AddTransactionAsync(30_000m, date: new DateTime(2026, 10, 2));
        var (_, l1) = await seed.AddInvoiceAsync(60_100m, taxNumber: "1");
        await seed.LinkAsync(l1, t1, 10_000m);
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        var pairs = await service.SuggestAsync([l1.Key!.Value], [t1.Key, t2.Key]);

        // disponível 50.100: 20.000 completa o t1 (atual 10.000 → 30.000), 30.100 vai para o t2 (último, leva a sobra).
        Assert.Equal(2, pairs.Count);
        var p1 = pairs.Single(p => p.StorageTransactionKey == t1.Key);
        Assert.Equal((10_000m, 30_000m), (p1.CurrentQuantity, p1.Quantity));
        Assert.Equal(30_100m, pairs.Single(p => p.StorageTransactionKey == t2.Key).Quantity);
    }

    [Fact]
    public async Task Confirm_needs_a_link_and_unconfirm_clears()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(100m);
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        Assert.Contains("Vincule ao menos uma NF", await RefusalAsync(() => service.ConfirmAsync(tx.Key, "ana")));

        var (_, line) = await seed.AddInvoiceAsync(90m);
        await seed.LinkAsync(line, tx, 90m);
        await service.ConfirmAsync(tx.Key, "ana");
        var row = Assert.Single(await service.ListTransactionsAsync(new() { CardCode = Supplier, ItemCode = Item }));
        Assert.Equal(FiscalReconciliationStatus.Reconciled, row.Status);
        Assert.Equal("ana", row.FiscalReconciledBy);

        await service.UnconfirmAsync(tx.Key);
        Assert.Equal(FiscalReconciliationStatus.Partial,
            Assert.Single(await service.ListTransactionsAsync(new() { CardCode = Supplier, ItemCode = Item })).Status);
    }

    [Fact]
    public async Task Remove_deletes_link_and_clears_confirmation()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(100m);
        var (_, line) = await seed.AddInvoiceAsync(100m);
        var link = await seed.LinkAsync(line, tx, 100m);
        var service = new PurchaseFiscalReconciliationService(seed.Db);
        await service.ConfirmAsync(tx.Key, "ana");

        await service.RemoveAsync(link.Key);

        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations);
        Assert.Null((await seed.Db.Context.StorageTransactions.AsNoTracking().SingleAsync(t => t.Key == tx.Key)).FiscalReconciledAt);
        await Assert.ThrowsAsync<NotFoundException>(() => service.RemoveAsync(link.Key));
    }

    [Fact]
    public async Task List_requires_supplier_and_item()
    {
        var service = new PurchaseFiscalReconciliationService((await FiscalReconciliationSeed.CreateAsync()).Db);

        Assert.Contains("fornecedor e o produto", await RefusalAsync(() => service.ListTransactionsAsync(new() { CardCode = Supplier })));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationServiceTests"`
Expected: erro de compilação.

- [ ] **Step 3: Implementar**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Application.Services.PurchaseFiscalReconciliations;

/// <summary>
/// Conciliação Fiscal de Compras (spec 2026-10-13): vínculo linha de NF do fornecedor × romaneio de compra, N:N com
/// quantidade em KG, posterior e SEM efeito em saldo. As travas de §4 moram aqui (toda gravação passa por
/// <see cref="SaveAsync"/>); a limpeza automática é do <c>PurchaseFiscalReconciliationCleanupInterceptor</c>.
/// </summary>
public class PurchaseFiscalReconciliationService(IUnitOfWork db)
{
    public Task<List<FiscalReconciliationTransactionDto>> ListTransactionsAsync(FiscalReconciliationFilter filter)
    {
        EnsureFilter(filter);
        return PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(db.Context, filter);
    }

    public Task<List<FiscalReconciliationInvoiceLineDto>> ListInvoiceLinesAsync(FiscalReconciliationFilter filter)
    {
        EnsureFilter(filter);
        return PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(db.Context, filter);
    }

    public Task<List<FiscalReconciliationLinkDto>> ListLinksAsync(Guid transactionKey) =>
        PurchaseFiscalReconciliationQuery.LoadLinksAsync(db.Context, [transactionKey]);

    public async Task<List<FiscalReconciliationPairDto>> SuggestAsync(IReadOnlyList<Guid> invoiceItemKeys, IReadOnlyList<Guid> transactionKeys)
    {
        if (invoiceItemKeys.Count == 0 || transactionKeys.Count == 0)
            throw new DefaultException("Selecione ao menos uma linha de NF e um romaneio.");

        var lines = await LoadLinesAsync(invoiceItemKeys);
        var transactions = await LoadTransactionsAsync(transactionKeys);
        var links = await PurchaseFiscalReconciliationQuery.ActiveLinks(db.Context)
            .Where(l => invoiceItemKeys.Contains(l.PurchaseInvoiceItemKey) || transactionKeys.Contains(l.StorageTransactionKey))
            .ToListAsync();

        var plannerLines = lines.Select(l => new PlannerLine(
            l.Key, l.InvoiceType, l.IssueDate, l.TaxDocument,
            QuantityKg(l) - links.Where(x => x.PurchaseInvoiceItemKey == l.Key).Sum(x => x.Quantity))).ToList();
        var plannerTransactions = transactions.Select(t => new PlannerTransaction(
            t.Key, t.TransactionType, t.TransactionDate, t.Code ?? "",
            t.NetWeight - links.Where(x => x.StorageTransactionKey == t.Key).Sum(x => x.Quantity))).ToList();

        var suggested = PurchaseFiscalReconciliationPlanner.Suggest(plannerLines, plannerTransactions)
            .ToDictionary(p => (p.LineKey, p.TransactionKey), p => p.Quantity);

        var result = new List<FiscalReconciliationPairDto>();
        foreach (var line in lines)
        foreach (var tx in transactions.Where(t => R.SenseMatches(line.InvoiceType, t.TransactionType)))
        {
            var current = links.Where(x => x.PurchaseInvoiceItemKey == line.Key && x.StorageTransactionKey == tx.Key).Sum(x => x.Quantity);
            var extra = suggested.GetValueOrDefault((line.Key, tx.Key));
            if (current == 0 && extra == 0) continue;

            result.Add(new FiscalReconciliationPairDto
            {
                PurchaseInvoiceItemKey = line.Key, StorageTransactionKey = tx.Key, TaxDocument = line.TaxDocument,
                TransactionCode = tx.Code, CurrentQuantity = current, Quantity = current + extra,
            });
        }

        return result;
    }

    public async Task SaveAsync(IReadOnlyList<FiscalReconciliationPairDto> pairs, string userName)
    {
        if (pairs.Count == 0)
            throw new DefaultException("Nenhum vínculo para gravar.");

        if (pairs.Any(p => p.Quantity < 0))
            throw new DefaultException("A quantidade do vínculo não pode ser negativa.");

        if (pairs.GroupBy(p => (p.PurchaseInvoiceItemKey, p.StorageTransactionKey)).Any(g => g.Count() > 1))
            throw new DefaultException("O mesmo par NF × romaneio aparece mais de uma vez.");

        var lineKeys = pairs.Select(p => p.PurchaseInvoiceItemKey).Distinct().ToList();
        var transactionKeys = pairs.Select(p => p.StorageTransactionKey).Distinct().ToList();
        var lines = (await LoadLinesAsync(lineKeys)).ToDictionary(l => l.Key);
        var transactions = (await LoadTransactionsAsync(transactionKeys)).ToDictionary(t => t.Key);
        var contractsByTransaction = (await db.Context.PurchaseContractsAllocations.AsNoTracking()
                .Where(a => transactionKeys.Contains(a.StorageTransactionKey))
                .Select(a => new { a.StorageTransactionKey, a.PurchaseContractKey })
                .ToListAsync())
            .ToLookup(a => a.StorageTransactionKey, a => a.PurchaseContractKey);

        foreach (var pair in pairs.Where(p => p.Quantity > 0))
            EnsurePair(lines[pair.PurchaseInvoiceItemKey], transactions[pair.StorageTransactionKey], contractsByTransaction);

        // Teto da NF (D7): o que a linha já tem em OUTROS romaneios + o que este pedido grava.
        var existing = await db.Context.PurchaseFiscalReconciliations
            .Where(l => lineKeys.Contains(l.PurchaseInvoiceItemKey) &&
                        l.StorageTransaction!.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .ToListAsync();

        foreach (var line in lines.Values)
        {
            var requested = pairs.Where(p => p.PurchaseInvoiceItemKey == line.Key).ToList();
            var others = existing
                .Where(e => e.PurchaseInvoiceItemKey == line.Key && requested.All(r => r.StorageTransactionKey != e.StorageTransactionKey))
                .Sum(e => e.Quantity);
            var total = others + requested.Sum(r => r.Quantity);
            var limit = QuantityKg(line);
            if (total > limit)
                throw new DefaultException(
                    $"A quantidade vinculada da NF {line.TaxDocument} ({total:N3} KG) passa da quantidade da linha ({limit:N3} KG).");
        }

        foreach (var pair in pairs)
        {
            var link = existing.FirstOrDefault(e =>
                e.PurchaseInvoiceItemKey == pair.PurchaseInvoiceItemKey && e.StorageTransactionKey == pair.StorageTransactionKey);

            if (pair.Quantity == 0)
            {
                if (link != null) db.Context.PurchaseFiscalReconciliations.Remove(link);
                continue;
            }

            if (link != null)
                link.Quantity = pair.Quantity;
            else
                db.Context.PurchaseFiscalReconciliations.Add(new PurchaseFiscalReconciliation
                {
                    PurchaseInvoiceItemKey = pair.PurchaseInvoiceItemKey, StorageTransactionKey = pair.StorageTransactionKey,
                    Quantity = pair.Quantity, CreatedAt = DateTime.Now, CreatedBy = userName,
                });
        }

        await ClearConfirmationAsync(transactionKeys);
        await db.SaveChangesAsync();
    }

    public async Task RemoveAsync(Guid linkKey)
    {
        var link = await db.Context.PurchaseFiscalReconciliations.FirstOrDefaultAsync(l => l.Key == linkKey)
                   ?? throw new NotFoundException("Vínculo não encontrado.");

        db.Context.PurchaseFiscalReconciliations.Remove(link);
        await ClearConfirmationAsync([link.StorageTransactionKey]);
        await db.SaveChangesAsync();
    }

    public async Task ConfirmAsync(Guid transactionKey, string userName)
    {
        var tx = await TrackedTransactionAsync(transactionKey);
        if (!await PurchaseFiscalReconciliationQuery.ActiveLinks(db.Context).AnyAsync(l => l.StorageTransactionKey == transactionKey))
            throw new DefaultException("Vincule ao menos uma NF antes de marcar o romaneio como conciliado.");

        tx.FiscalReconciledAt = DateTime.Now;
        tx.FiscalReconciledBy = userName;
        await db.SaveChangesAsync();
    }

    public async Task UnconfirmAsync(Guid transactionKey)
    {
        var tx = await TrackedTransactionAsync(transactionKey);
        tx.FiscalReconciledAt = null;
        tx.FiscalReconciledBy = null;
        await db.SaveChangesAsync();
    }

    private sealed record LineData(
        Guid Key, string ItemCode, string? Unit, decimal Quantity, Guid? ContractKey, string? ContractCode,
        PurchaseInvoiceType InvoiceType, InvoiceStatus InvoiceStatus, bool IsCounterNote, DateTime? IssueDate,
        string TaxDocument, ItemComplement? Complement);

    private async Task<List<LineData>> LoadLinesAsync(IReadOnlyCollection<Guid> keys)
    {
        var rows = await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Where(l => keys.Contains(l.Key!.Value))
            .Select(l => new
            {
                Key = l.Key!.Value, l.ItemCode, l.UnitOfMeasureCode, l.Quantity, l.PurchaseContractKey,
                ContractCode = l.PurchaseContract!.Code, l.PurchaseInvoice!.InvoiceType, l.PurchaseInvoice.InvoiceStatus,
                l.PurchaseInvoice.ProducerInvoiceKey, l.PurchaseInvoice.IssueDate, l.PurchaseInvoice.TaxDocumentNumber,
                l.PurchaseInvoice.TaxDocumentSeries, l.PurchaseInvoice.InvoiceNumber,
            })
            .ToListAsync();

        if (rows.Count != keys.Count)
            throw new NotFoundException("Linha de documento de entrada não encontrada.");

        var itemCodes = rows.Select(r => r.ItemCode).Distinct().ToList();
        var complements = await db.Context.ItemComplements.AsNoTracking()
            .Where(c => itemCodes.Contains(c.ItemCode)).ToDictionaryAsync(c => c.ItemCode);

        return rows.Select(r => new LineData(
            r.Key, r.ItemCode ?? "", r.UnitOfMeasureCode, r.Quantity, r.PurchaseContractKey, r.ContractCode,
            r.InvoiceType, r.InvoiceStatus, r.ProducerInvoiceKey != null, r.IssueDate,
            R.TaxDocument(r.TaxDocumentNumber, r.TaxDocumentSeries, r.InvoiceNumber),
            r.ItemCode == null ? null : complements.GetValueOrDefault(r.ItemCode))).ToList();
    }

    private async Task<List<StorageTransaction>> LoadTransactionsAsync(IReadOnlyCollection<Guid> keys)
    {
        var rows = await db.Context.StorageTransactions.AsNoTracking().Where(t => keys.Contains(t.Key)).ToListAsync();
        if (rows.Count != keys.Count)
            throw new NotFoundException("Romaneio não encontrado.");
        return rows;
    }

    private static decimal QuantityKg(LineData line) =>
        R.KgFactor(line.Unit, line.Complement) is { } factor
            ? line.Quantity * factor
            : throw new DefaultException(
                $"Cadastre o fator da unidade {line.Unit} no Complemento do Produto {line.ItemCode} para vincular a NF {line.TaxDocument}.");

    private static void EnsurePair(LineData line, StorageTransaction tx, ILookup<Guid, Guid> contractsByTransaction)
    {
        if (line.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException($"O documento de entrada {line.TaxDocument} está cancelado e não pode ser conciliado.");

        if (line.IsCounterNote)
            throw new DefaultException($"A NF {line.TaxDocument} é contra-nota: vincule a escrituração da NF do produtor.");

        if (!R.IsLinkable(tx.TransactionType))
            throw new DefaultException(
                $"O romaneio {tx.Code} não é romaneio de compra (Compra, Dev.Compra, Compl.Qtd., Quebra ou Sobra Descarga).");

        if (tx.TransactionStatus == StorageTransactionsStatus.Cancelled)
            throw new DefaultException($"O romaneio {tx.Code} está cancelado.");

        if (!R.SenseMatches(line.InvoiceType, tx.TransactionType))
            throw new DefaultException(
                "NF de devolução só se vincula a Dev.Compra ou Quebra Descarga; NF normal, a Compra, Compl.Qtd. ou Sobra Descarga " +
                $"(NF {line.TaxDocument} × romaneio {tx.Code}).");

        if (!string.Equals(line.ItemCode, tx.ItemCode, StringComparison.OrdinalIgnoreCase))
            throw new DefaultException(
                $"O produto da NF {line.TaxDocument} ({line.ItemCode}) difere do produto do romaneio {tx.Code} ({tx.ItemCode}).");

        if (line.ContractKey is { } contractKey && !contractsByTransaction[tx.Key].Contains(contractKey))
            throw new DefaultException(
                $"O romaneio {tx.Code} não está alocado no contrato {line.ContractCode} da linha da NF {line.TaxDocument}.");

        _ = QuantityKg(line);
    }

    private async Task ClearConfirmationAsync(IReadOnlyCollection<Guid> transactionKeys)
    {
        var tracked = await db.Context.StorageTransactions
            .Where(t => transactionKeys.Contains(t.Key) && t.FiscalReconciledAt != null).ToListAsync();
        foreach (var tx in tracked)
        {
            tx.FiscalReconciledAt = null;
            tx.FiscalReconciledBy = null;
        }
    }

    private async Task<StorageTransaction> TrackedTransactionAsync(Guid key) =>
        await db.Context.StorageTransactions.FirstOrDefaultAsync(t => t.Key == key)
        ?? throw new NotFoundException("Romaneio não encontrado.");

    private static void EnsureFilter(FiscalReconciliationFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.CardCode) || string.IsNullOrWhiteSpace(filter.ItemCode))
            throw new DefaultException("Informe o fornecedor e o produto.");
    }
}
```

> Nota: `Save_upserts...` muda a quantidade de um vínculo que está trackeado — `existing` vem da consulta tracked,
> então o `link.Quantity = ...` é persistido. Não use `AsNoTracking` nessa consulta.

- [ ] **Step 4: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationServiceTests"`
Expected: todos passam.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationService.cs SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationServiceTests.cs
git commit -m "feat(invoice): gravar, sugerir e confirmar a conciliação fiscal de compras" -m "Travas de status, contra-nota, tipo, sentido, produto, contrato e unidade num lugar só; teto só do lado da NF, porque no FOB a NF sai no peso de origem e passa da balança. Mudar vínculo derruba a confirmação."
```

---

### Task 6: Interceptor de limpeza e recusas da edição da NF

**Files:**
- Create: `SiagroB1.Infra/Interceptors/PurchaseFiscalReconciliationCleanupInterceptor.cs`
- Modify: `SiagroB1.Web/Program.cs:72` (registrar ao lado do `FinishedContractMutationGuardInterceptor`)
- Test: `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationCleanupInterceptorTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2; `TestDb.CreateUnitOfWork(IInterceptor)`.
- Produces: `public class PurchaseFiscalReconciliationCleanupInterceptor : SaveChangesInterceptor` (construtor sem parâmetros).

- [ ] **Step 1: Testes (falham)**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Interceptors;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

/// <summary>
/// A conciliação nunca trava o fluxo (D1): cancelar/excluir a NF, remover a linha ou cancelar o romaneio solta os
/// vínculos sozinho. As duas únicas recusas fora da tela: reduzir a quantidade abaixo do vinculado e trocar o produto.
/// </summary>
public class PurchaseFiscalReconciliationCleanupInterceptorTests
{
    private static async Task<(FiscalReconciliationSeed Seed, StorageTransaction Tx, StorageTransaction Other, PurchaseInvoice Invoice, PurchaseInvoiceItem Line)> LinkedAsync()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync(TestDb.CreateUnitOfWork(new PurchaseFiscalReconciliationCleanupInterceptor()));
        var tx = await seed.AddTransactionAsync(30_000m);
        var other = await seed.AddTransactionAsync(30_000m);
        var (invoice, line) = await seed.AddInvoiceAsync(60_000m);
        await seed.LinkAsync(line, tx, 30_000m);
        await seed.LinkAsync(line, other, 30_000m);
        other.FiscalReconciledAt = DateTime.Now;
        await seed.Db.SaveChangesAsync();
        return (seed, tx, other, invoice, line);
    }

    [Fact]
    public async Task Cancelling_a_transaction_removes_its_links_only()
    {
        var (seed, tx, other, _, _) = await LinkedAsync();

        tx.TransactionStatus = StorageTransactionsStatus.Cancelled;
        await seed.Db.SaveChangesAsync();

        var link = Assert.Single(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking());
        Assert.Equal(other.Key, link.StorageTransactionKey);
    }

    [Fact]
    public async Task Cancelling_the_invoice_removes_all_its_links_and_confirmations()
    {
        var (seed, _, other, invoice, _) = await LinkedAsync();

        invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        await seed.Db.SaveChangesAsync();

        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations);
        Assert.Null((await seed.Db.Context.StorageTransactions.AsNoTracking().SingleAsync(t => t.Key == other.Key)).FiscalReconciledAt);
    }

    [Fact]
    public async Task Deleting_the_invoice_or_the_line_removes_the_links()
    {
        var (seed, _, _, invoice, line) = await LinkedAsync();

        seed.Db.Context.PurchaseInvoicesItems.Remove(line);
        seed.Db.Context.PurchaseInvoices.Remove(invoice);
        await seed.Db.SaveChangesAsync();

        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations);
    }

    [Fact]
    public async Task Reducing_line_quantity_below_linked_is_refused_and_above_is_allowed()
    {
        var (seed, _, _, _, line) = await LinkedAsync();

        line.Quantity = 59_000m;
        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync());
        Assert.Contains("desvincule", error.Message);

        line.Quantity = 61_000m;
        await seed.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Changing_product_or_unit_of_a_linked_line_is_refused()
    {
        var (seed, _, _, _, line) = await LinkedAsync();

        line.ItemCode = "MILHO";
        Assert.Contains("produto", (await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync())).Message);

        line.ItemCode = FiscalReconciliationSeed.Item;
        line.UnitOfMeasureCode = "TON";
        Assert.Contains("desvincule", (await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync())).Message);
    }

    [Fact]
    public async Task Unlinked_line_edits_are_not_touched()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync(TestDb.CreateUnitOfWork(new PurchaseFiscalReconciliationCleanupInterceptor()));
        var (_, line) = await seed.AddInvoiceAsync(100m);

        line.Quantity = 1m;
        line.ItemCode = "MILHO";
        await seed.Db.SaveChangesAsync();
    }

    /// <summary>Ponta a ponta: o cancelamento real de romaneio passa pelo interceptor sem saber dele.</summary>
    [Fact]
    public async Task Real_cancel_service_releases_the_links()
    {
        var (seed, tx, _, _, _) = await LinkedAsync();
        var cancel = PurchaseFiscalReconciliationTestServices.CancelService(seed.Db);

        await cancel.ExecuteAsync(tx.Key, "ana");

        Assert.DoesNotContain(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking(), l => l.StorageTransactionKey == tx.Key);
    }
}
```

Para o último teste, crie `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationTestServices.cs`
com a fábrica `CancelService(UnitOfWork db)` que monta o `StorageTransactionsCancelService` real. **Antes de escrever,
abra `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCancelService.cs` e copie a montagem que
`SiagroB1.Application.Tests/StorageDeposits/ReconciledTransactionGuardTests.cs` usa no teste `Cancel_refuses_reconciled_transaction`**
(mesmas dependências e fakes; ajuste o nome do método `ExecuteAsync`/parâmetros ao que o serviço expõe). Se o
cancelamento exigir romaneio Pendente ou outro estado, ajuste `tx.TransactionStatus` antes da chamada, como aquele teste faz.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationCleanupInterceptorTests"`
Expected: erro de compilação — o interceptor não existe.

- [ ] **Step 3: Implementar**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Infra.Interceptors;

/// <summary>
/// Conciliação Fiscal de Compras (spec 2026-10-13 §4.2): a conciliação é posterior e NUNCA trava o fluxo. Num ponto só,
/// para todos os caminhos (6 serviços cancelam romaneio, cancelamento/exclusão da NF, SyncItems, PATCH direto):
/// <list type="bullet">
/// <item>romaneio cancelado/excluído, NF cancelada/excluída ou linha excluída → vínculos removidos e a confirmação manual
/// dos romaneios que ficaram é limpa;</item>
/// <item>linha vinculada com quantidade reduzida abaixo do vinculado, UM sem conversão ou produto trocado → recusa.</item>
/// </list>
/// As consultas são poucas e por chave; uma implementação síncrona atende os dois caminhos do SaveChanges.
/// </summary>
public class PurchaseFiscalReconciliationCleanupInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Apply(DbContext context)
    {
        var cancelledTransactions = new HashSet<Guid>();
        var cancelledInvoices = new HashSet<Guid>();
        var deletedLines = new HashSet<Guid>();
        var changedLines = new List<EntityEntry<PurchaseInvoiceItem>>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case StorageTransaction t when entry.State == EntityState.Deleted || BecameCancelled(entry, nameof(StorageTransaction.TransactionStatus), StorageTransactionsStatus.Cancelled):
                    cancelledTransactions.Add(t.Key);
                    break;
                case PurchaseInvoice i when entry.State == EntityState.Deleted || BecameCancelled(entry, nameof(PurchaseInvoice.InvoiceStatus), InvoiceStatus.Cancelled):
                    cancelledInvoices.Add(i.Key);
                    break;
                case PurchaseInvoiceItem { Key: { } key } when entry.State == EntityState.Deleted:
                    deletedLines.Add(key);
                    break;
                case PurchaseInvoiceItem { Key: not null } when entry.State == EntityState.Modified:
                    changedLines.Add(entry.Cast<PurchaseInvoiceItem>());
                    break;
            }
        }

        if (cancelledTransactions.Count + cancelledInvoices.Count + deletedLines.Count + changedLines.Count == 0)
            return;

        var links = context.Set<PurchaseFiscalReconciliation>();

        foreach (var line in changedLines)
            EnsureLineEditKeepsLinks(context, links, line);

        if (cancelledTransactions.Count + cancelledInvoices.Count + deletedLines.Count == 0)
            return;

        var linesOfInvoices = cancelledInvoices.Count == 0
            ? []
            : context.Set<PurchaseInvoiceItem>().AsNoTracking()
                .Where(l => cancelledInvoices.Contains(l.PurchaseInvoiceKey!.Value)).Select(l => l.Key!.Value).ToList();
        deletedLines.UnionWith(linesOfInvoices);

        var doomed = links
            .Where(l => cancelledTransactions.Contains(l.StorageTransactionKey) || deletedLines.Contains(l.PurchaseInvoiceItemKey))
            .ToList();
        if (doomed.Count == 0)
            return;

        links.RemoveRange(doomed);

        var touched = doomed.Select(l => l.StorageTransactionKey).Where(k => !cancelledTransactions.Contains(k)).Distinct().ToList();
        foreach (var tx in context.Set<StorageTransaction>().Where(t => touched.Contains(t.Key) && t.FiscalReconciledAt != null).ToList())
        {
            tx.FiscalReconciledAt = null;
            tx.FiscalReconciledBy = null;
        }
    }

    private static bool BecameCancelled<TStatus>(EntityEntry entry, string property, TStatus cancelled) where TStatus : struct, Enum
    {
        if (entry.State != EntityState.Modified) return false;
        var p = entry.Property(property);
        return p.IsModified && Equals(p.CurrentValue, cancelled) && !Equals(p.OriginalValue, cancelled);
    }

    private static void EnsureLineEditKeepsLinks(DbContext context, DbSet<PurchaseFiscalReconciliation> links, EntityEntry<PurchaseInvoiceItem> entry)
    {
        var quantity = entry.Property(x => x.Quantity);
        var item = entry.Property(x => x.ItemCode);
        var unit = entry.Property(x => x.UnitOfMeasureCode);
        if (!quantity.IsModified && !item.IsModified && !unit.IsModified)
            return;

        var key = entry.Entity.Key!.Value;
        var linked = links.AsNoTracking()
            .Where(l => l.PurchaseInvoiceItemKey == key && l.StorageTransaction!.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .Sum(l => (decimal?)l.Quantity) ?? 0m;
        if (linked == 0)
            return;

        if (item.IsModified && !string.Equals(item.OriginalValue, item.CurrentValue, StringComparison.OrdinalIgnoreCase))
            throw new DefaultException(
                "A linha da NF está vinculada a romaneios na Conciliação Fiscal de Compras; desvincule antes de trocar o produto.");

        var complement = context.Set<ItemComplement>().AsNoTracking().FirstOrDefault(c => c.ItemCode == entry.Entity.ItemCode);
        var factor = R.KgFactor(entry.Entity.UnitOfMeasureCode, complement);
        if (factor is null || entry.Entity.Quantity * factor.Value < linked)
            throw new DefaultException(
                $"A linha da NF tem {linked:N3} KG vinculados a romaneios; desvincule na Conciliação Fiscal de Compras antes de reduzir a quantidade ou trocar a unidade.");
    }
}
```

Registrar em `SiagroB1.Web/Program.cs`, logo depois da linha `options.AddInterceptors(new FinishedContractMutationGuardInterceptor());`:

```csharp
        options.AddInterceptors(new PurchaseFiscalReconciliationCleanupInterceptor());
```

- [ ] **Step 4: Rodar e ver passar; suíte inteira**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationCleanupInterceptorTests"` e depois `dotnet test SiagroB1.Application.Tests`
Expected: todos passam (os testes existentes não usam o interceptor, então não mudam).

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Infra/Interceptors/PurchaseFiscalReconciliationCleanupInterceptor.cs SiagroB1.Web/Program.cs SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationCleanupInterceptorTests.cs SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationTestServices.cs
git commit -m "feat(invoice): conciliação fiscal se desfaz sozinha ao cancelar NF ou romaneio" -m "Um interceptor de SaveChanges cobre os 6 serviços que cancelam romaneio, o cancelamento e a exclusão da NF e o PATCH direto, sem tocar em nenhum deles. Reduzir a linha abaixo do vinculado ou trocar o produto é recusado." -m "Atenção: a Conferência de Entregas recria os romaneios 16/17 a cada gravação, e com isso derruba os vínculos de NF feitos neles."
```

---

### Task 7: Endpoints OData

**Files:**
- Create: `SiagroB1.Web/Functions/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationParameters.cs`
- Create: `SiagroB1.Web/Functions/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationTransactionsController.cs`
- Create: `SiagroB1.Web/Functions/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationInvoiceLinesController.cs`
- Create: `SiagroB1.Web/Functions/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationLinksController.cs`
- Create: `SiagroB1.Web/Actions/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationActionsController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (logo depois do bloco `purchaseDischargesList`, ~linha 1464)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs:404` (ao lado de `PurchaseDischargesListService`)
- Test: `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationParametersTests.cs`

**Interfaces:**
- Consumes: Task 5.
- Produces (rotas usadas pelo frontend — nomes exatos):
  - `GET odata/PurchaseFiscalReconciliationTransactions(CardCode='..',ItemCode='..',ContractCode='..',DateFrom='yyyy-MM-dd',DateTo='yyyy-MM-dd')` → array cru de `FiscalReconciliationTransactionDto`
  - `GET odata/PurchaseFiscalReconciliationInvoiceLines(CardCode=..,ItemCode=..,ContractCode=..,DateFrom=..,DateTo=..,IssuerCardCode=..,IncludeFullyLinked=true|false)` → array cru de `FiscalReconciliationInvoiceLineDto`
  - `GET odata/PurchaseFiscalReconciliationLinks(TransactionKey=<guid>)` → array cru de `FiscalReconciliationLinkDto`
  - `POST odata/PurchaseFiscalReconciliationSuggest` `{ InvoiceItemKeys: guid[], TransactionKeys: guid[] }` → `{ value: FiscalReconciliationPairDto[] }`
  - `POST odata/PurchaseFiscalReconciliationSave` `{ InvoiceItemKeys: guid[], TransactionKeys: guid[], Quantities: number[] }` (paralelos) → 204
  - `POST odata/PurchaseFiscalReconciliationRemove` `{ Key: guid }` → 204
  - `POST odata/PurchaseFiscalReconciliationConfirm` `{ TransactionKey: guid }` → 204
  - `POST odata/PurchaseFiscalReconciliationUnconfirm` `{ TransactionKey: guid }` → 204
  - `PurchaseFiscalReconciliationParameters.Filter(...)` (Web, estático)

> O teste de parâmetros mora em `SiagroB1.Application.Tests`, que já referencia o `SiagroB1.Web` (csproj, linha 49).

- [ ] **Step 1: Teste do parser de parâmetros (falha)**

```csharp
using SiagroB1.Web.Functions.PurchaseFiscalReconciliations;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

public class PurchaseFiscalReconciliationParametersTests
{
    [Fact]
    public void Unquotes_and_turns_empty_into_null()
    {
        var (filter, error) = PurchaseFiscalReconciliationParameters.Filter("'F0001'", "'SOJA'", "''", "'2026-10-01'", "''", "'F0002'", true);

        Assert.Null(error);
        Assert.Equal(("F0001", "SOJA", null, new DateTime(2026, 10, 1), (DateTime?)null, "F0002", true),
            (filter!.CardCode, filter.ItemCode, filter.ContractCode, filter.DateFrom, filter.DateTo, filter.IssuerCardCode, filter.IncludeFullyLinked));
    }

    [Fact]
    public void Unescapes_doubled_quotes()
    {
        var (filter, _) = PurchaseFiscalReconciliationParameters.Filter("'D''AVILA'", "'SOJA'", null, null, null, null, false);

        Assert.Equal("D'AVILA", filter!.CardCode);
    }

    [Fact]
    public void Invalid_date_is_an_error()
    {
        var (filter, error) = PurchaseFiscalReconciliationParameters.Filter("'F0001'", "'SOJA'", "''", "'01/10/2026'", "''", "''", false);

        Assert.Null(filter);
        Assert.Equal("Informe as datas no formato aaaa-mm-dd.", error);
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationParametersTests"`
Expected: erro de compilação.

- [ ] **Step 3: Implementar parser, controllers, EDM e DI**

`PurchaseFiscalReconciliationParameters.cs`:

```csharp
using System.Globalization;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.PurchaseFiscalReconciliations;

/// <summary>
/// Parâmetro string de Function OData chega com as aspas e é anulável (memória odata-action-string-params-are-nullable):
/// tira as aspas, desfaz o '' de escape, vazio vira null; data estrita aaaa-mm-dd (lição do GAC-1171).
/// </summary>
public static class PurchaseFiscalReconciliationParameters
{
    public static string? Text(string? raw)
    {
        if (raw is null) return null;
        var value = raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'' ? raw[1..^1] : raw;
        value = value.Replace("''", "'").Trim();
        return value.Length == 0 ? null : value;
    }

    public static (FiscalReconciliationFilter? Filter, string? Error) Filter(
        string? cardCode, string? itemCode, string? contractCode, string? dateFrom, string? dateTo,
        string? issuerCardCode, bool includeFullyLinked)
    {
        if (!TryDate(dateFrom, out var from) || !TryDate(dateTo, out var to))
            return (null, "Informe as datas no formato aaaa-mm-dd.");

        return (new FiscalReconciliationFilter
        {
            CardCode = Text(cardCode) ?? "", ItemCode = Text(itemCode) ?? "", ContractCode = Text(contractCode),
            DateFrom = from, DateTo = to, IssuerCardCode = Text(issuerCardCode), IncludeFullyLinked = includeFullyLinked,
        }, null);
    }

    private static bool TryDate(string? raw, out DateTime? value)
    {
        value = null;
        var text = Text(raw);
        if (text is null) return true;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        value = parsed;
        return true;
    }
}
```

`PurchaseFiscalReconciliationTransactionsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.PurchaseFiscalReconciliations;

/// <summary>Romaneios do recorte da Conciliação Fiscal de Compras (spec 2026-10-13 §6.1). Rota declarada à mão.</summary>
public class PurchaseFiscalReconciliationTransactionsController(PurchaseFiscalReconciliationService service) : ODataController
{
    [HttpGet("odata/PurchaseFiscalReconciliationTransactions(CardCode={cardCode},ItemCode={itemCode},ContractCode={contractCode},DateFrom={dateFrom},DateTo={dateTo})")]
    public async Task<IActionResult> Get(
        [FromRoute] string? cardCode, [FromRoute] string? itemCode, [FromRoute] string? contractCode,
        [FromRoute] string? dateFrom, [FromRoute] string? dateTo)
    {
        var (filter, error) = PurchaseFiscalReconciliationParameters.Filter(cardCode, itemCode, contractCode, dateFrom, dateTo, null, false);
        if (filter is null) return BadRequest(error);

        try
        {
            return Ok(await service.ListTransactionsAsync(filter));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

`PurchaseFiscalReconciliationInvoiceLinesController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.PurchaseFiscalReconciliations;

/// <summary>Linhas de NF do recorte, com Disponível em KG e o aviso de emitente (D6). Rota declarada à mão.</summary>
public class PurchaseFiscalReconciliationInvoiceLinesController(PurchaseFiscalReconciliationService service) : ODataController
{
    [HttpGet("odata/PurchaseFiscalReconciliationInvoiceLines(CardCode={cardCode},ItemCode={itemCode},ContractCode={contractCode},DateFrom={dateFrom},DateTo={dateTo},IssuerCardCode={issuerCardCode},IncludeFullyLinked={includeFullyLinked})")]
    public async Task<IActionResult> Get(
        [FromRoute] string? cardCode, [FromRoute] string? itemCode, [FromRoute] string? contractCode,
        [FromRoute] string? dateFrom, [FromRoute] string? dateTo, [FromRoute] string? issuerCardCode,
        [FromRoute] bool includeFullyLinked)
    {
        var (filter, error) = PurchaseFiscalReconciliationParameters.Filter(
            cardCode, itemCode, contractCode, dateFrom, dateTo, issuerCardCode, includeFullyLinked);
        if (filter is null) return BadRequest(error);

        try
        {
            return Ok(await service.ListInvoiceLinesAsync(filter));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

`PurchaseFiscalReconciliationLinksController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;

namespace SiagroB1.Web.Functions.PurchaseFiscalReconciliations;

/// <summary>Vínculos de um romaneio — o diálogo de detalhe. Rota declarada à mão.</summary>
public class PurchaseFiscalReconciliationLinksController(PurchaseFiscalReconciliationService service) : ODataController
{
    [HttpGet("odata/PurchaseFiscalReconciliationLinks(TransactionKey={key})")]
    public async Task<IActionResult> Get([FromRoute] Guid key) => Ok(await service.ListLinksAsync(key));
}
```

`PurchaseFiscalReconciliationActionsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Web.Actions.Nfe;

namespace SiagroB1.Web.Actions.PurchaseFiscalReconciliations;

/// <summary>
/// Ações da Conciliação Fiscal de Compras. <c>InvoiceItemKeys</c>, <c>TransactionKeys</c> e <c>Quantities</c> do Save
/// são arrays PARALELOS (contagens diferentes = erro de montagem). ⚠️ ODataActionParameters chega NULO quando falta
/// parâmetro declarado no EDM.
/// </summary>
public class PurchaseFiscalReconciliationActionsController(PurchaseFiscalReconciliationService service) : ODataController
{
    [HttpPost("odata/PurchaseFiscalReconciliationSuggest")]
    public Task<IActionResult> Suggest([FromBody] ODataActionParameters parameters) => Run(async () =>
        Ok(await service.SuggestAsync(Guids(parameters, "InvoiceItemKeys"), Guids(parameters, "TransactionKeys"))));

    [HttpPost("odata/PurchaseFiscalReconciliationSave")]
    public Task<IActionResult> Save([FromBody] ODataActionParameters parameters) => Run(async () =>
    {
        var items = Guids(parameters, "InvoiceItemKeys");
        var transactions = Guids(parameters, "TransactionKeys");
        var quantities = parameters is null ? [] : NfeReturnActionParameters.Quantities(parameters);
        if (items.Count != transactions.Count || items.Count != quantities.Count)
            return BadRequest("As listas de linhas, romaneios e quantidades têm tamanhos diferentes.");

        var pairs = items.Select((item, i) => new FiscalReconciliationPairDto
            { PurchaseInvoiceItemKey = item, StorageTransactionKey = transactions[i], Quantity = quantities[i] }).ToList();
        await service.SaveAsync(pairs, User.Identity?.Name ?? "Unknown");
        return NoContent();
    });

    [HttpPost("odata/PurchaseFiscalReconciliationRemove")]
    public Task<IActionResult> Remove([FromBody] ODataActionParameters parameters) => Run(async () =>
    {
        if (!TryGuid(parameters, "Key", out var key)) return BadRequest("Parâmetro obrigatório: Key.");
        await service.RemoveAsync(key);
        return NoContent();
    });

    [HttpPost("odata/PurchaseFiscalReconciliationConfirm")]
    public Task<IActionResult> Confirm([FromBody] ODataActionParameters parameters) => Run(async () =>
    {
        if (!TryGuid(parameters, "TransactionKey", out var key)) return BadRequest("Parâmetro obrigatório: TransactionKey.");
        await service.ConfirmAsync(key, User.Identity?.Name ?? "Unknown");
        return NoContent();
    });

    [HttpPost("odata/PurchaseFiscalReconciliationUnconfirm")]
    public Task<IActionResult> Unconfirm([FromBody] ODataActionParameters parameters) => Run(async () =>
    {
        if (!TryGuid(parameters, "TransactionKey", out var key)) return BadRequest("Parâmetro obrigatório: TransactionKey.");
        await service.UnconfirmAsync(key);
        return NoContent();
    });

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
        catch (ApplicationException e)
        {
            return BadRequest(e.Message);
        }
    }

    private static List<Guid> Guids(ODataActionParameters? parameters, string name) =>
        parameters is not null && parameters.TryGetValue(name, out var value) && value is IEnumerable<Guid> keys ? keys.ToList() : [];

    private static bool TryGuid(ODataActionParameters? parameters, string name, out Guid key)
    {
        key = Guid.Empty;
        return parameters is not null && parameters.TryGetValue(name, out var value) && Guid.TryParse(value?.ToString(), out key);
    }
}
```

`ODataConfigurations.cs`, depois de `purchaseDischargesList.Returns<IActionResult>();`:

```csharp
        // Conciliação Fiscal de Compras (spec 2026-10-13): NF do fornecedor × romaneio de compra.
        var fiscalTransactions = modelBuilder.Function("PurchaseFiscalReconciliationTransactions");
        fiscalTransactions.Parameter<string>("CardCode");
        fiscalTransactions.Parameter<string>("ItemCode");
        fiscalTransactions.Parameter<string>("ContractCode");
        fiscalTransactions.Parameter<string>("DateFrom");
        fiscalTransactions.Parameter<string>("DateTo");
        fiscalTransactions.Returns<IActionResult>();

        var fiscalInvoiceLines = modelBuilder.Function("PurchaseFiscalReconciliationInvoiceLines");
        fiscalInvoiceLines.Parameter<string>("CardCode");
        fiscalInvoiceLines.Parameter<string>("ItemCode");
        fiscalInvoiceLines.Parameter<string>("ContractCode");
        fiscalInvoiceLines.Parameter<string>("DateFrom");
        fiscalInvoiceLines.Parameter<string>("DateTo");
        fiscalInvoiceLines.Parameter<string>("IssuerCardCode");
        fiscalInvoiceLines.Parameter<bool>("IncludeFullyLinked");
        fiscalInvoiceLines.Returns<IActionResult>();

        var fiscalLinks = modelBuilder.Function("PurchaseFiscalReconciliationLinks");
        fiscalLinks.Parameter<Guid>("TransactionKey");
        fiscalLinks.Returns<IActionResult>();

        var fiscalSuggest = modelBuilder.Action("PurchaseFiscalReconciliationSuggest");
        fiscalSuggest.CollectionParameter<Guid>("InvoiceItemKeys");
        fiscalSuggest.CollectionParameter<Guid>("TransactionKeys");
        fiscalSuggest.ReturnsCollection<FiscalReconciliationPairDto>();

        var fiscalSave = modelBuilder.Action("PurchaseFiscalReconciliationSave");
        fiscalSave.CollectionParameter<Guid>("InvoiceItemKeys");
        fiscalSave.CollectionParameter<Guid>("TransactionKeys");
        fiscalSave.CollectionParameter<double>("Quantities");

        var fiscalRemove = modelBuilder.Action("PurchaseFiscalReconciliationRemove");
        fiscalRemove.Parameter<Guid>("Key");

        var fiscalConfirm = modelBuilder.Action("PurchaseFiscalReconciliationConfirm");
        fiscalConfirm.Parameter<Guid>("TransactionKey");

        var fiscalUnconfirm = modelBuilder.Action("PurchaseFiscalReconciliationUnconfirm");
        fiscalUnconfirm.Parameter<Guid>("TransactionKey");
```

(adicione `using SiagroB1.Domain.Dtos;` se ainda não houver.)

`ServiceCollectionExtensions.cs`, ao lado de `services.AddScoped<PurchaseDischargesListService>();`:

```csharp
        services.AddScoped<PurchaseFiscalReconciliationService>();
```

(+ `using SiagroB1.Application.Services.PurchaseFiscalReconciliations;` no topo.)

- [ ] **Step 4: Rodar testes e build**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationParametersTests"` e `dotnet build SiagroB1.sln`
Expected: 3 passed; build sem erro.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Web/Functions/PurchaseFiscalReconciliations SiagroB1.Web/Actions/PurchaseFiscalReconciliations SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationParametersTests.cs
git commit -m "feat(invoice): endpoints da conciliação fiscal de compras" -m "Funções de leitura (romaneios, linhas de NF, vínculos) e ações de sugerir, gravar, desvincular e confirmar."
```

---

### Task 8: Aba Entregas do contrato de compra

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsGetAllocationsByContractService.cs`
- Modify: `SiagroB1.Domain/Dtos/PurchaseContractAllocationsByContractDto.cs`

**Interfaces:**
- Produces (novas propriedades do DTO, `[JsonPropertyName]` igual ao nome): `StorageTransactionKey (Guid?)`, `SupplierInvoiceNumbers (string?)`,
  `PhysicalQuantity (decimal?)`, `FiscalQuantity (decimal?)`, `FiscalDifference (decimal?)`, `FiscalStatus (string?)` = "Sem NF"/"Parcial"/"Conciliado",
  `IsSplitAcrossContracts (bool)`, `ItemCode (string?)`.

O serviço usa Dapper sobre SQL Server — não há teste InMemory possível. A verificação é rodar a consulta no banco
local (Step 3) e, na Task 13, conferir a aba no navegador.

- [ ] **Step 1: DTO**

Acrescente ao `PurchaseContractAllocationsByContractDto` (mesmo estilo dos campos existentes):

```csharp
    [JsonPropertyName("StorageTransactionKey")]
    public Guid? StorageTransactionKey { get; set; }

    [JsonPropertyName("ItemCode")]
    public string? ItemCode { get; set; }

    /// <summary>Conciliação fiscal (spec 2026-10-13 §7): NFs do fornecedor vinculadas ao ROMANEIO inteiro.</summary>
    [JsonPropertyName("SupplierInvoiceNumbers")]
    public string? SupplierInvoiceNumbers { get; set; }

    [JsonPropertyName("PhysicalQuantity")]
    public decimal? PhysicalQuantity { get; set; }

    [JsonPropertyName("FiscalQuantity")]
    public decimal? FiscalQuantity { get; set; }

    [JsonPropertyName("FiscalDifference")]
    public decimal? FiscalDifference { get; set; }

    /// <summary>"Sem NF" / "Parcial" / "Conciliado" — PurchaseFiscalReconciliationRules.StatusName.</summary>
    [JsonPropertyName("FiscalStatus")]
    public string? FiscalStatus { get; set; }

    /// <summary>Romaneio rateado em mais de um contrato: os números fiscais são do romaneio, não desta alocação.</summary>
    [JsonPropertyName("IsSplitAcrossContracts")]
    public bool IsSplitAcrossContracts { get; set; }
```

- [ ] **Step 2: SQL e composição**

No `SQL` do serviço:

1. No `CASE` do `StorageTransactionTypeName`, acrescente `WHEN ST.TransactionType = 10 THEN 'Compl.Qtd.'`; no `CASE` do
   `Volume`, `WHEN ST.TransactionType = 10 THEN PA.Volume`.
2. Na lista do `SELECT`, depois de `DLOAD.Code AS DischargeLoadCode`:

```sql
            ,
            ST.[Key] AS StorageTransactionKey,
            ST.ItemCode AS ItemCode,
            ST.NetWeight AS PhysicalQuantity,
            ST.FiscalReconciledAt AS FiscalReconciledAt,
            FIS.FiscalQuantity AS FiscalQuantity,
            FIS.LinkCount AS FiscalLinkCount,
            FIS.SupplierInvoiceNumbers AS SupplierInvoiceNumbers,
            SPLIT.ContractCount AS ContractCount
```

3. Antes do `WHERE PA.PurchaseContractKey = @purchaseContractKey`:

```sql
        -- Conciliação fiscal (spec 2026-10-13 §7): NFs do fornecedor vinculadas ao romaneio (NF cancelada não conta).
        OUTER APPLY (
            SELECT SUM(PFR.Quantity) AS FiscalQuantity,
                   COUNT(*) AS LinkCount,
                   STRING_AGG(CASE WHEN PI.TaxDocumentNumber IS NULL OR PI.TaxDocumentNumber = ''
                                   THEN PI.InvoiceNumber
                                   ELSE PI.TaxDocumentNumber + COALESCE('-' + NULLIF(PI.TaxDocumentSeries, ''), '') END, ', ')
                       AS SupplierInvoiceNumbers
            FROM PURCHASE_FISCAL_RECONCILIATIONS PFR
            JOIN PURCHASE_INVOICES_ITEMS PII ON PII.[Key] = PFR.PurchaseInvoiceItemKey
            JOIN PURCHASE_INVOICES PI ON PI.[Key] = PII.PurchaseInvoiceKey
            WHERE PFR.StorageTransactionKey = ST.[Key] AND PI.InvoiceStatus <> 2
        ) FIS
        OUTER APPLY (
            SELECT COUNT(DISTINCT PA2.PurchaseContractKey) AS ContractCount
            FROM PURCHASE_CONTRACTS_ALLOCATIONS PA2
            WHERE PA2.StorageTransactionKey = ST.[Key]
        ) SPLIT
```

4. No laço que monta o DTO, acrescente (com `using SiagroB1.Infra.Queries;`):

```csharp
                StorageTransactionKey = allocation.StorageTransactionKey,
                ItemCode = allocation.ItemCode,
                SupplierInvoiceNumbers = allocation.SupplierInvoiceNumbers,
                PhysicalQuantity = allocation.PhysicalQuantity,
                FiscalQuantity = (decimal?)(allocation.FiscalQuantity ?? 0m),
                FiscalDifference = (decimal?)((allocation.FiscalQuantity ?? 0m) - (allocation.PhysicalQuantity ?? 0m)),
                FiscalStatus = PurchaseFiscalReconciliationRules.StatusName(PurchaseFiscalReconciliationRules.Status(
                    (decimal)(allocation.PhysicalQuantity ?? 0m), (decimal)(allocation.FiscalQuantity ?? 0m),
                    (int)(allocation.FiscalLinkCount ?? 0) > 0, allocation.FiscalReconciledAt != null)),
                IsSplitAcrossContracts = (int)(allocation.ContractCount ?? 0) > 1,
```

(`allocation` é `dynamic` do Dapper; os casts explícitos evitam despacho dinâmico errado em decimal/int nulos.)

- [ ] **Step 3: Validar o SQL no banco local (CEAGUI)**

Com a migration da Task 1 aplicada no CEAGUI dev (Task 15, Step 1, pode ser antecipado) rode o SQL num contrato real:

```bash
sqlcmd -S localhost -E -I -d CEAGUI_SIAGRO_DEV -Q "SELECT TOP 1 [Key] FROM PURCHASE_CONTRACTS"
```

Copie o `SQL` montado para um arquivo no scratchpad, troque `@purchaseContractKey` pela chave e rode com
`sqlcmd -S localhost -E -I -d CEAGUI_SIAGRO_DEV -i <arquivo>`.
Expected: executa sem erro; colunas novas presentes (`FiscalQuantity` NULL quando não há vínculo).

- [ ] **Step 4: Build + suíte**

Run: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests`
Expected: verde.

- [ ] **Step 5: Commit**

```bash
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsGetAllocationsByContractService.cs SiagroB1.Domain/Dtos/PurchaseContractAllocationsByContractDto.cs
git commit -m "feat(purchase-contract): situação fiscal do romaneio na aba Entregas" -m "NFs do fornecedor, fiscal, diferença e situação por romaneio; o complemento de quantidade (10) passa a aparecer. Os números são do romaneio inteiro, com indicativo quando ele está rateado em mais de um contrato."
```

---

### Task 9: Relatório "Entregas × NF do Fornecedor"

**Files:**
- Create: `SiagroB1.Reports/Dtos/PurchaseFiscalReconciliationReportRequest.cs`
- Create: `SiagroB1.Reports/Dtos/PurchaseFiscalReconciliationRowDto.cs`
- Create: `SiagroB1.Reports/Services/PurchaseFiscalReconciliationReportService.cs`
- Create: `SiagroB1.Reports/Controllers/PurchaseFiscalReconciliationController.cs`
- Create: `SiagroB1.Reports/Reports/Templates/PurchaseFiscalReconciliation.frx`
- Modify: `SiagroB1.Application.Tests/Reports/LogisticsReportsLayoutTests.cs` (lista `Templates` + `LongestText`)
- Modify: `SiagroB1.Application.Tests/Reports/LogisticsReportsPdfTests.cs` (2 testes)
- Test: `SiagroB1.Application.Tests/Reports/PurchaseFiscalReconciliationReportServiceTests.cs`

**Interfaces:**
- Consumes: Task 3 (`PurchaseFiscalReconciliationQuery`), Task 2 (regras).
- Produces: `POST /reports/PurchaseFiscalReconciliation` com corpo `PurchaseFiscalReconciliationReportRequest` → PDF.
  Serviços do Reports são registrados por scan (Scrutor) — nada a registrar à mão.

- [ ] **Step 1: DTOs**

```csharp
namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Entregas × NF do Fornecedor" — os mesmos da tela de conciliação (spec 2026-10-13 §8).</summary>
public class PurchaseFiscalReconciliationReportRequest
{
    public string? CardCode { get; set; }
    public string? ItemCode { get; set; }
    public string? ContractCode { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    /// <summary>FiscalReconciliationStatus como int; vazio = todas.</summary>
    public List<int> Statuses { get; set; } = [];
}
```

```csharp
namespace SiagroB1.Reports.Dtos;

public static class PurchaseFiscalReconciliationRowKind
{
    public const string Transaction = "Romaneio";
    public const string Invoice = "NF";
    public const string Unlinked = "NF livre";
}

/// <summary>
/// Linha de "Entregas × NF do Fornecedor". Romaneio = linha cheia; NF vinculada = linha em itálico logo abaixo;
/// NF livre = seção final. <c>Group*</c> soma no subtotal do contrato; <c>Sum*</c> só na primeira aparição do romaneio
/// (rateado em 2 contratos aparece nos 2, mas o total geral conta uma vez).
/// </summary>
public class PurchaseFiscalReconciliationRowDto
{
    public string Group { get; set; } = "";
    public string Kind { get; set; } = PurchaseFiscalReconciliationRowKind.Transaction;
    public string Document { get; set; } = "";
    public string Date { get; set; } = "";
    public string Description { get; set; } = "";
    public string Party { get; set; } = "";
    public string PhysicalText { get; set; } = "";
    public string FiscalText { get; set; } = "";
    public string DifferenceText { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal GroupPhysical { get; set; }
    public decimal GroupFiscal { get; set; }
    public decimal GroupDifference { get; set; }
    public decimal SumPhysical { get; set; }
    public decimal SumFiscal { get; set; }
    public decimal SumDifference { get; set; }
    public int TransactionCount { get; set; }
}
```

- [ ] **Step 2: Testes do serviço (falham)**

```csharp
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class PurchaseFiscalReconciliationReportServiceTests
{
    private static PurchaseFiscalReconciliationReportService Service(FiscalReconciliationSeed seed) =>
        new(seed.Db, new RecordingFastReportService());

    private static PurchaseFiscalReconciliationReportRequest Request() =>
        new() { CardCode = FiscalReconciliationSeed.Supplier, ItemCode = FiscalReconciliationSeed.Item };

    [Fact]
    public async Task Groups_by_contract_with_links_below_and_unlinked_invoices_at_the_end()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var contract = await seed.AddContractAsync("PC-9");
        var tx = await seed.AddTransactionAsync(29_820m, contract: contract, code: "RM000010");
        var (_, line) = await seed.AddInvoiceAsync(30_000m, taxNumber: "000555");
        await seed.LinkAsync(line, tx, 30_000m);
        await seed.AddInvoiceAsync(1_000m, taxNumber: "000556");

        var rows = await Service(seed).BuildRowsAsync(Request());

        Assert.Equal(
            [("Contrato PC-9", "Romaneio", "RM000010"), ("Contrato PC-9", "NF", "000555-1"),
             (PurchaseFiscalReconciliationReportService.UnlinkedGroup, "NF livre", "000556-1")],
            rows.Select(r => (r.Group, r.Kind, r.Document)));
        var head = rows[0];
        Assert.Equal(("29.820,000", "30.000,000", "180,000", "Parcial"), (head.PhysicalText, head.FiscalText, head.DifferenceText, head.Status));
        Assert.Equal(1, head.TransactionCount);
    }

    [Fact]
    public async Task Split_transaction_appears_in_each_contract_but_counts_once_in_the_summary()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var c1 = await seed.AddContractAsync("PC-1");
        var c2 = await seed.AddContractAsync("PC-2");
        var tx = await seed.AddTransactionAsync(100m, contract: c1);
        seed.Db.Context.PurchaseContractsAllocations.Add(new() { Key = Guid.NewGuid(), PurchaseContractKey = c2.Key, StorageTransactionKey = tx.Key, Volume = 0m });
        await seed.Db.SaveChangesAsync();

        var rows = (await Service(seed).BuildRowsAsync(Request())).Where(r => r.Kind == "Romaneio").ToList();

        Assert.Equal(["Contrato PC-1", "Contrato PC-2"], rows.Select(r => r.Group));
        Assert.Equal([100m, 100m], rows.Select(r => r.GroupPhysical));
        Assert.Equal(100m, rows.Sum(r => r.SumPhysical));
        Assert.Equal(1, rows.Sum(r => r.TransactionCount));
    }

    [Fact]
    public async Task Status_filter_and_transaction_without_contract()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        await seed.AddTransactionAsync(100m, code: "RM-SEM");
        var request = Request();
        request.Statuses = [(int)FiscalReconciliationStatus.Reconciled];

        Assert.Empty((await Service(seed).BuildRowsAsync(request)).Where(r => r.Kind == "Romaneio"));
        Assert.Equal("Sem contrato", Assert.Single(await Service(seed).BuildRowsAsync(Request())).Group);
    }
}
```

Confira o nome/forma de `RecordingFastReportService` em `SiagroB1.Application.Tests/Support/RecordingFastReportService.cs`
e use-o como os testes de `StorageRemittancesByPeriodReportServiceTests` usam.

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliationReportServiceTests"`
Expected: erro de compilação.

- [ ] **Step 4: Serviço e controller**

```csharp
using System.Globalization;
using SiagroB1.Domain.Dtos;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Helpers;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Reports.Services;

/// <summary>
/// "Entregas × NF do Fornecedor" (spec 2026-10-13 §8). Lê pela <see cref="PurchaseFiscalReconciliationQuery"/>, a mesma
/// consulta da tela — os números batem por construção. Agrupado por contrato; romaneio rateado aparece em cada contrato
/// e conta uma vez no total geral; NFs com quantidade não vinculada no fim.
/// </summary>
public class PurchaseFiscalReconciliationReportService(IUnitOfWork db, IFastReportService reportService)
{
    public const string UnlinkedGroup = "NFs do fornecedor com quantidade não vinculada";
    public const string NoContractGroup = "Sem contrato";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<List<PurchaseFiscalReconciliationRowDto>> BuildRowsAsync(PurchaseFiscalReconciliationReportRequest request)
    {
        var filter = new FiscalReconciliationFilter
        {
            CardCode = request.CardCode ?? "", ItemCode = request.ItemCode ?? "", ContractCode = request.ContractCode,
            DateFrom = request.FromDate, DateTo = request.ToDate,
        };

        var transactions = await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(db.Context, filter);
        if (request.Statuses.Count > 0)
            transactions = transactions.Where(t => request.Statuses.Contains((int)t.Status)).ToList();

        var rows = new List<PurchaseFiscalReconciliationRowDto>();
        var counted = new HashSet<Guid>();

        var placed = transactions
            .SelectMany(t => (t.ContractCodes.Count == 0 ? [NoContractGroup] : t.ContractCodes.Select(c => $"Contrato {c}"))
                .Select(group => (Group: group, Tx: t)))
            .OrderBy(p => p.Group == NoContractGroup).ThenBy(p => p.Group, StringComparer.Ordinal)
            .ThenBy(p => p.Tx.TransactionDate).ThenBy(p => p.Tx.Code, StringComparer.Ordinal);

        foreach (var (group, tx) in placed)
        {
            var first = counted.Add(tx.Key);
            rows.Add(new PurchaseFiscalReconciliationRowDto
            {
                Group = group, Kind = PurchaseFiscalReconciliationRowKind.Transaction, Document = tx.Code ?? "",
                Date = ReportText.Date(tx.TransactionDate), Description = tx.TransactionTypeName, Party = tx.TruckCode ?? "",
                PhysicalText = Quantity(tx.PhysicalQuantity), FiscalText = Quantity(tx.FiscalQuantity),
                DifferenceText = Quantity(tx.Difference), Status = tx.StatusName,
                GroupPhysical = tx.PhysicalQuantity, GroupFiscal = tx.FiscalQuantity, GroupDifference = tx.Difference,
                SumPhysical = first ? tx.PhysicalQuantity : 0m, SumFiscal = first ? tx.FiscalQuantity : 0m,
                SumDifference = first ? tx.Difference : 0m, TransactionCount = first ? 1 : 0,
            });

            rows.AddRange(tx.Links.Select(link => new PurchaseFiscalReconciliationRowDto
            {
                Group = group, Kind = PurchaseFiscalReconciliationRowKind.Invoice, Document = link.TaxDocument,
                Date = ReportText.Date(link.IssueDate), Description = "NF do fornecedor",
                Party = ReportText.Partner(link.CardCode, link.CardName), FiscalText = Quantity(link.Quantity),
            }));
        }

        filter.IncludeFullyLinked = false;
        var unlinked = await PurchaseFiscalReconciliationQuery.LoadInvoiceLinesAsync(db.Context, filter);
        rows.AddRange(unlinked.Select(line => new PurchaseFiscalReconciliationRowDto
        {
            Group = UnlinkedGroup, Kind = PurchaseFiscalReconciliationRowKind.Unlinked, Document = line.TaxDocument,
            Date = ReportText.Date(line.IssueDate), Description = line.UsageName ?? "",
            Party = ReportText.Partner(line.CardCode, line.CardName),
            FiscalText = line.QuantityKg is { } kg ? Quantity(kg) : $"{line.Quantity.ToString("N3", PtBr)} {line.UnitOfMeasureCode}",
            Status = line.AvailableQuantity is { } available ? $"Livre: {Quantity(available)}" : "UM sem fator",
        }));

        return rows;
    }

    public async Task<byte[]> ExecuteAsync(PurchaseFiscalReconciliationReportRequest request)
    {
        var rows = await BuildRowsAsync(request);
        var filters = new List<string> { $"Fornecedor: {request.CardCode}", $"Produto: {request.ItemCode}" };
        if (!string.IsNullOrWhiteSpace(request.ContractCode)) filters.Add($"Contrato: {request.ContractCode}");
        if (request.FromDate != null || request.ToDate != null)
            filters.Add($"Período: {ReportText.Date(request.FromDate)} a {ReportText.Date(request.ToDate)}");
        if (request.Statuses.Count > 0)
            filters.Add("Situação: " + string.Join(", ", request.Statuses.Select(s => R.StatusName((Domain.Enums.FiscalReconciliationStatus)s))));

        var parameters = new Dictionary<string, object> { ["pFilters"] = string.Join("  |  ", filters) };

        return await reportService.GeneratePdfAsync(
            "PurchaseFiscalReconciliation.frx", rows, "FiscalReconciliation", "FiscalReconciliation", parameters);
    }

    private static string Quantity(decimal value) => value.ToString("N3", PtBr);
}
```

`ReportText.Date(DateTime?)` e `ReportText.Partner(string?, string?)` existem em `SiagroB1.Reports/Helpers/ReportText.cs`.

```csharp
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/PurchaseFiscalReconciliation")]
public class PurchaseFiscalReconciliationController(PurchaseFiscalReconciliationReportService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Report([FromBody] PurchaseFiscalReconciliationReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CardCode) || string.IsNullOrWhiteSpace(request.ItemCode))
            return BadRequest("Informe o fornecedor e o produto.");

        if (request.FromDate > request.ToDate)
            return BadRequest("A data inicial é maior que a final.");

        var pdf = await service.ExecuteAsync(request);

        Response.Headers.ContentDisposition = "inline; filename=\"purchase-fiscal-reconciliation.pdf\"";
        return File(pdf, "application/pdf");
    }
}
```

- [ ] **Step 5: Template `.frx`**

Crie `PurchaseFiscalReconciliation.frx` a partir de `StorageRemittancesByPeriod.frx` (mesma página paisagem 1084 px,
mesmos `PageHeader`/`PageFooter`, `pCompanyName`/`pFilters`, mesmas fontes). Diferenças:

- `BusinessObjectDataSource Name="FiscalReconciliation" ReferenceName="FiscalReconciliation"` com as colunas do
  `PurchaseFiscalReconciliationRowDto` (strings `System.String`, `Group*`/`Sum*` `System.Decimal`, `TransactionCount` `System.Int32`).
- `txtTitle`: `Entregas × NF do Fornecedor`. Sem `pSingleUom` (tudo é KG).
- Colunas (cabeçalho `hdr*` e dados `txt*`, `Left`/`Width`): Document 0/90 "Romaneio/NF"; Date 90/62 "Data";
  Description 152/150 "Tipo/Natureza"; Party 302/330 "Placa/Emitente"; Physical 632/100 "Físico" (Right);
  Fiscal 732/100 "Fiscal" (Right); Difference 832/100 "Diferença" (Right); Status 932/152 "Situação".
  Dados com texto pré-formatado (`[FiscalReconciliation.PhysicalText]` etc.), 7pt; romaneio em **negrito** com
  `txtSeparator` de fio no topo (visível só quando `Kind == "Romaneio"`); `Kind != "Romaneio"` → itálico (Highlight
  como o do template para "Retorno").
- `GroupHeader1` com `Condition="[FiscalReconciliation.Group]"`; `GroupFooter1` "Subtotal do grupo:" com
  `TotalGrpPhysical/Fiscal/Difference` sobre `Group*` (Format Number, 3 casas, 6pt, largura 100).
- `ReportSummary1` "TOTAL GERAL:" com `[TotalTransactions] romaneio(s)` (soma de `TransactionCount`) e
  `TotalSumPhysical/Fiscal/Difference` sobre `Sum*`; `txtEmpty` igual ao template.

Em `LogisticsReportsLayoutTests.cs`, acrescente `"PurchaseFiscalReconciliation"` à lista `Templates` e em `LongestText`:

```csharp
        ["PurchaseFiscalReconciliation"] = new()
        {
            ["txtDocument"] = 13,     // 000000038-001 / RM00000123
            ["txtDate"] = 10,         // 31/12/2026
            ["txtPhysical"] = 14,     // 99.999.999,999
            ["txtFiscal"] = 14,
            ["txtDifference"] = 15,   // -10.000.000,000
            ["txtStatus"] = 22,       // Livre: 99.999.999,999
        },
```

⚠️ O teste `txtDate` do cabeçalho (`[Date]`) e o `txtDate` da linha não podem ter o mesmo `Name` — no template base a
data do cabeçalho já se chama `txtDate`. Nomeie a da linha `txtRowDate` e use `["txtRowDate"] = 10` acima.

Em `LogisticsReportsPdfTests.cs`, dois testes no padrão dos de `StorageRemittancesByPeriod`:

```csharp
    [Fact]
    public async Task PurchaseFiscalReconciliation_ProducesAPdf()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var contract = await seed.AddContractAsync("PC2026000123");
        var tx = await seed.AddTransactionAsync(99_999_999.999m, contract: contract, code: "RM00000123");
        var (_, line) = await seed.AddInvoiceAsync(99_999_999.999m, taxNumber: "000000038");
        await seed.LinkAsync(line, tx, 99_999_999.999m);
        await seed.AddInvoiceAsync(10_000m, taxNumber: "000000039");

        var pdf = await new PurchaseFiscalReconciliationReportService(seed.Db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new PurchaseFiscalReconciliationReportRequest { CardCode = FiscalReconciliationSeed.Supplier, ItemCode = FiscalReconciliationSeed.Item });

        Keep("PurchaseFiscalReconciliation", "STANDALONE", pdf);
    }

    [Fact]
    public async Task PurchaseFiscalReconciliation_EmptyResultStillProducesAPdf()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();

        var pdf = await new PurchaseFiscalReconciliationReportService(seed.Db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new PurchaseFiscalReconciliationReportRequest { CardCode = "X", ItemCode = "Y" });

        Assert.NotEmpty(pdf);
    }
```

(`using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;`)

- [ ] **Step 6: Rodar e ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseFiscalReconciliation|FullyQualifiedName~LogisticsReports"`
Expected: verde. Abra o PDF gravado por `Keep` (em `%TEMP%/siagro-logistics-reports`) e confira visualmente: romaneio em
negrito com fio, NF em itálico abaixo, subtotal por contrato, seção final de NFs livres, nada cortado.

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Reports/Dtos/PurchaseFiscalReconciliationReportRequest.cs SiagroB1.Reports/Dtos/PurchaseFiscalReconciliationRowDto.cs SiagroB1.Reports/Services/PurchaseFiscalReconciliationReportService.cs SiagroB1.Reports/Controllers/PurchaseFiscalReconciliationController.cs SiagroB1.Reports/Reports/Templates/PurchaseFiscalReconciliation.frx SiagroB1.Application.Tests/Reports
git commit -m "feat(reports): relatório entregas × NF do fornecedor" -m "Romaneios por contrato com físico, fiscal, diferença e situação, as NFs vinculadas logo abaixo e, no fim, as NFs com quantidade ainda não vinculada. Lê a mesma consulta da tela."
```

---

### Task 10: Migration de menu

**Files:**
- Create: `SiagroB1.Migrations/CommonContext/<timestamp>_AddPurchaseFiscalReconciliationMenus.cs`

- [ ] **Step 1: Conferir as ordens livres**

```bash
sqlcmd -S localhost -E -I -d CEAGUI_SIAGRO_COMMON_DEV -Q "SELECT ParentKey, MAX([Order]) FROM MENU_ITEMS WHERE ParentKey IN ('purchases','reports') GROUP BY ParentKey"
```

Expected hoje: `purchases` 14, `reports` 20. Use MAX+1 de cada (abaixo, 15 e 21 — ajuste se o banco disser outro).

- [ ] **Step 2: Gerar vazia e preencher**

```bash
dotnet ef migrations add AddPurchaseFiscalReconciliationMenus --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir CommonContext
```

Substitua `Up`/`Down` (apague qualquer operação gerada por drift):

```csharp
    /// <summary>
    /// Conciliação Fiscal de Compras (spec 2026-10-13): a tela no grupo "Compras" e o relatório em "Relatórios". A Key
    /// PRECISA ser igual ao name da rota no manifest.json (App.controller navega com navTo(item.getKey())). Sem
    /// ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddPurchaseFiscalReconciliationMenus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "purchaseFiscalReconciliations", "Conciliação Fiscal de Compras", "sap-icon://folder-blank", true, false, 15, "purchases" },
                    { "purchaseFiscalReconciliationReport", "Entregas × NF do Fornecedor", "sap-icon://folder-blank", true, false, 21, "reports" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "3C1E5A7B-2D94-4F6A-9B08-6E2F1A4C7D35", "ADMIN", "purchaseFiscalReconciliations" },
                    { "8F2B6D41-7A3C-4E59-B1D0-2C9E5F7A3B68", "ADMIN", "purchaseFiscalReconciliationReport" },
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "3C1E5A7B-2D94-4F6A-9B08-6E2F1A4C7D35");
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "8F2B6D41-7A3C-4E59-B1D0-2C9E5F7A3B68");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "purchaseFiscalReconciliations");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "purchaseFiscalReconciliationReport");
        }
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build SiagroB1.sln`
Expected: OK.

- [ ] **Step 4: Commit**

```bash
git add SiagroB1.Migrations/CommonContext
git commit -m "feat(invoice): menus da conciliação fiscal de compras e do relatório" -m "DB: AddPurchaseFiscalReconciliationMenus"
```

---

### Task 11: Frontend — rotas, tipos e tela (leitura)

Repo `siagro-b1-frontend`; crie o branch `feature/purchase-invoice-reconciliation` a partir do `main` antes de começar.

**Files:**
- Modify: `webapp/model/ServerRoutes.ts` (perto de `purchaseDischargesList`, linha ~319, e de `storageRemittancesByPeriodReport`, ~117)
- Create: `webapp/types/PurchaseFiscalReconciliation.ts`
- Modify: `webapp/manifest.json` (rota ao lado de `purchaseDischarges` ~linha 528; target ao lado ~linha 1637)
- Create: `webapp/view/purchaseFiscalReconciliations/Main.view.xml`
- Create: `webapp/view/purchaseFiscalReconciliations/fragments/Filterbar.fragment.xml`
- Create: `webapp/controller/purchaseFiscalReconciliations/Main.controller.ts`

**Interfaces:**
- Consumes: rotas da Task 7.
- Produces: rota `purchaseFiscalReconciliations` (pattern `purchase-fiscal-reconciliations:?query:`), query aceita
  `cardCode`, `itemCode`, `contractCode`; métodos do controller usados pela Task 12: `load()`, `selectedTransactions()`,
  `selectedInvoiceLines()`.

- [ ] **Step 1: ServerRoutes**

```ts
  purchaseFiscalReconciliationTransactions: '/odata/PurchaseFiscalReconciliationTransactions',
  purchaseFiscalReconciliationInvoiceLines: '/odata/PurchaseFiscalReconciliationInvoiceLines',
  purchaseFiscalReconciliationLinks: '/odata/PurchaseFiscalReconciliationLinks',
  purchaseFiscalReconciliationSuggest: '/odata/PurchaseFiscalReconciliationSuggest',
  purchaseFiscalReconciliationSave: '/odata/PurchaseFiscalReconciliationSave',
  purchaseFiscalReconciliationRemove: '/odata/PurchaseFiscalReconciliationRemove',
  purchaseFiscalReconciliationConfirm: '/odata/PurchaseFiscalReconciliationConfirm',
  purchaseFiscalReconciliationUnconfirm: '/odata/PurchaseFiscalReconciliationUnconfirm',
```

e, junto dos relatórios:

```ts
  purchaseFiscalReconciliationReport: '/reports/PurchaseFiscalReconciliation',
```

- [ ] **Step 2: Tipos**

`webapp/types/PurchaseFiscalReconciliation.ts`:

```ts
/** Respostas do Web OData: PascalCase, enums como string (spec 2026-10-13). */
export type FiscalReconciliationStatus = "WithoutInvoice" | "Partial" | "Reconciled";

export type FiscalReconciliationLink = {
  Key: string;
  PurchaseInvoiceItemKey: string;
  PurchaseInvoiceKey: string;
  StorageTransactionKey: string;
  TaxDocument: string;
  CardCode: string;
  CardName: string;
  IssueDate: string;
  Quantity: number;
  CreatedAt: string;
  CreatedBy: string;
};

export type FiscalReconciliationTransaction = {
  Key: string;
  Code: string;
  TransactionDate: string;
  TransactionType: string;
  TransactionTypeName: string;
  TruckCode: string;
  UnitOfMeasureCode: string;
  ContractCodesText: string;
  PhysicalQuantity: number;
  FiscalQuantity: number;
  Difference: number;
  Status: FiscalReconciliationStatus;
  StatusName: string;
  FiscalReconciledAt: string | null;
  FiscalReconciledBy: string | null;
  Links: FiscalReconciliationLink[];
  InvoiceNumbersText: string;
};

export type FiscalReconciliationInvoiceLine = {
  PurchaseInvoiceItemKey: string;
  PurchaseInvoiceKey: string;
  InvoiceType: "Normal" | "Return";
  TaxDocument: string;
  IssueDate: string;
  CardCode: string;
  CardName: string;
  IssuerMatchesSupplier: boolean;
  UsageName: string;
  ContractCode: string;
  UnitOfMeasureCode: string;
  Quantity: number;
  QuantityKg: number | null;
  LinkedQuantity: number;
  AvailableQuantity: number | null;
};

export type FiscalReconciliationPair = {
  PurchaseInvoiceItemKey: string;
  StorageTransactionKey: string;
  TaxDocument: string;
  TransactionCode: string;
  CurrentQuantity: number;
  Quantity: number;
};
```

- [ ] **Step 3: Manifest**

Rota (no array `routes`, depois de `purchaseDischarges`):

```json
        {
          "pattern": "purchase-fiscal-reconciliations:?query:",
          "name": "purchaseFiscalReconciliations",
          "target": "purchaseFiscalReconciliations"
        },
```

Target (depois de `purchaseDischarges`):

```json
        "purchaseFiscalReconciliations": {
          "id": "purchaseFiscalReconciliations",
          "level": 1,
          "name": "siagrob1.view.purchaseFiscalReconciliations.Main",
          "clearControlAggregation": true
        },
```

- [ ] **Step 4: Filterbar**

`webapp/view/purchaseFiscalReconciliations/fragments/Filterbar.fragment.xml`:

```xml
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core" xmlns:fb="sap.ui.comp.filterbar">
<fb:FilterBar search=".onSearch" showClearOnFB="true" clear=".onClearFilters">
  <fb:filterGroupItems>
    <fb:FilterGroupItem name="supplier" label="Fornecedor" groupName="G1" visibleInFilterBar="true" mandatory="true">
      <fb:control>
        <Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openSuppliersValueHelp"
          value="{filter>/CardCode}" description="{filter>/CardName}" fieldWidth="40%">
          <customData><core:CustomData key="descriptionProperty" value="CardName"/></customData>
        </Input>
      </fb:control>
    </fb:FilterGroupItem>
    <fb:FilterGroupItem name="item" label="Produto" groupName="G2" visibleInFilterBar="true" mandatory="true">
      <fb:control>
        <Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openItemValueHelp"
          value="{filter>/ItemCode}" description="{filter>/ItemName}" fieldWidth="40%">
          <customData><core:CustomData key="descriptionProperty" value="ItemName"/></customData>
        </Input>
      </fb:control>
    </fb:FilterGroupItem>
    <fb:FilterGroupItem name="contract" label="Contrato de compra" groupName="G3" visibleInFilterBar="true">
      <fb:control><Input showClearIcon="true" value="{filter>/ContractCode}"/></fb:control>
    </fb:FilterGroupItem>
    <fb:FilterGroupItem name="dateFrom" label="Data de" groupName="G4" visibleInFilterBar="true">
      <fb:control>
        <DatePicker value="{ path: 'filter>/DateFrom', type: 'sap.ui.model.type.String' }" displayFormat="dd/MM/yyyy" valueFormat="yyyy-MM-dd"/>
      </fb:control>
    </fb:FilterGroupItem>
    <fb:FilterGroupItem name="dateTo" label="Data até" groupName="G5" visibleInFilterBar="true">
      <fb:control>
        <DatePicker value="{ path: 'filter>/DateTo', type: 'sap.ui.model.type.String' }" displayFormat="dd/MM/yyyy" valueFormat="yyyy-MM-dd"/>
      </fb:control>
    </fb:FilterGroupItem>
    <fb:FilterGroupItem name="status" label="Situação" groupName="G6" visibleInFilterBar="true">
      <fb:control>
        <MultiComboBox selectedKeys="{filter>/Statuses}">
          <core:Item key="WithoutInvoice" text="Sem NF"/>
          <core:Item key="Partial" text="Parcial"/>
          <core:Item key="Reconciled" text="Conciliado"/>
        </MultiComboBox>
      </fb:control>
    </fb:FilterGroupItem>
    <fb:FilterGroupItem name="issuer" label="Emitente da NF" groupName="G7" visibleInFilterBar="true">
      <fb:control>
        <Input showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openBusinessPartnersValueHelp"
          showClearIcon="true" value="{filter>/IssuerCardCode}" placeholder="Fornecedor e mesmas inscrições"/>
      </fb:control>
    </fb:FilterGroupItem>
    <fb:FilterGroupItem name="fullyLinked" label="NFs" groupName="G8" visibleInFilterBar="true">
      <fb:control><CheckBox text="Mostrar totalmente vinculadas" selected="{filter>/IncludeFullyLinked}"/></fb:control>
    </fb:FilterGroupItem>
  </fb:filterGroupItems>
</fb:FilterBar>
</core:FragmentDefinition>
```

> Antes de fixar o `customData` de descrição, abra `applyValueHelpDescription` em `CommonController.ts` (linha ~205) e
> confira como ele grava a descrição num Input ligado a JSONModel — o relatório de remessas usa `CardName:Name` num
> segundo Input. Siga o que o helper suporta; se ele só grava via contexto OData, troque o `description` por um segundo
> `Input editable="false" value="{filter>/CardName}"`, como `reports/storageRemittances/Main.view.xml`.

- [ ] **Step 5: View**

`webapp/view/purchaseFiscalReconciliations/Main.view.xml`:

```xml
<mvc:View
  controllerName="siagrob1.controller.purchaseFiscalReconciliations.Main"
  displayBlock="true"
  xmlns="sap.m"
  xmlns:mvc="sap.ui.core.mvc"
  xmlns:core="sap.ui.core"
  xmlns:t="sap.ui.table"
  xmlns:f="sap.f"
  core:require="{ formatter: 'siagrob1/model/formatter' }">
  <f:DynamicPage busyIndicatorDelay="0" busy="{fiscal>/busy}">
    <f:title>
      <f:DynamicPageTitle>
        <f:heading><Title text="Conciliação Fiscal de Compras"/></f:heading>
        <f:snappedContent>
          <Text text="NF de venda do fornecedor × romaneios de compra. Só conciliação: nada aqui altera saldo de contrato ou de armazém."/>
        </f:snappedContent>
      </f:DynamicPageTitle>
    </f:title>
    <f:header>
      <f:DynamicPageHeader>
        <f:content>
          <core:Fragment fragmentName="siagrob1.view.purchaseFiscalReconciliations.fragments.Filterbar" type="XML"/>
        </f:content>
      </f:DynamicPageHeader>
    </f:header>
    <f:content>
      <VBox>
        <t:Table id="fiscalTransactionsTable" selectionMode="MultiToggle" class="sapUiSizeCondensed"
          visibleRowCount="10" alternateRowColors="true" rows="{ path: 'fiscal>/transactions' }">
          <t:extension>
            <OverflowToolbar>
              <Title text="Romaneios ({= ${fiscal>/transactionCount} })"/>
              <ToolbarSpacer/>
              <Button text="Vincular" type="Emphasized" icon="sap-icon://chain-link" press=".onLink"/>
              <Button text="Exportar" icon="sap-icon://excel-attachment" press=".onExport"/>
            </OverflowToolbar>
          </t:extension>
          <t:columns>
            <t:Column width="3rem">
              <t:template><Button icon="sap-icon://detail-view" type="Transparent" tooltip="Vínculos" press=".onOpenLinks"/></t:template>
            </t:Column>
            <t:Column label="Romaneio" width="9rem" sortProperty="Code">
              <t:template><Text text="{fiscal>Code}" wrapping="false"/></t:template>
            </t:Column>
            <t:Column label="Data" width="7rem" sortProperty="TransactionDate">
              <t:template><Text wrapping="false" text="{ path: 'fiscal>TransactionDate', formatter: 'formatter.formatDate' }"/></t:template>
            </t:Column>
            <t:Column label="Tipo" width="9rem">
              <t:template><Text text="{fiscal>TransactionTypeName}" wrapping="false"/></t:template>
            </t:Column>
            <t:Column label="Contrato" width="10rem">
              <t:template><Text text="{fiscal>ContractCodesText}" wrapping="false" tooltip="{fiscal>ContractCodesText}"/></t:template>
            </t:Column>
            <t:Column label="Placa" width="7rem">
              <t:template><Text text="{fiscal>TruckCode}" wrapping="false"/></t:template>
            </t:Column>
            <t:Column label="Físico" hAlign="End" width="9rem" sortProperty="PhysicalQuantity">
              <t:template><ObjectNumber number="{ path: 'fiscal>PhysicalQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }" unit="KG"/></t:template>
            </t:Column>
            <t:Column label="Fiscal" hAlign="End" width="9rem" sortProperty="FiscalQuantity">
              <t:template><ObjectNumber number="{ path: 'fiscal>FiscalQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }" unit="KG"/></t:template>
            </t:Column>
            <t:Column label="Diferença" hAlign="End" width="9rem" sortProperty="Difference">
              <t:template><ObjectNumber state="{= ${fiscal>Difference} === 0 ? 'None' : 'Warning' }" number="{ path: 'fiscal>Difference', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }" unit="KG"/></t:template>
            </t:Column>
            <t:Column label="Situação" width="8rem" sortProperty="Status">
              <t:template>
                <ObjectStatus text="{fiscal>StatusName}" inverted="true"
                  state="{= ${fiscal>Status} === 'Reconciled' ? 'Success' : ${fiscal>Status} === 'Partial' ? 'Warning' : 'Error' }"/>
              </t:template>
            </t:Column>
            <t:Column label="NFs vinculadas" width="14rem">
              <t:template><Text text="{fiscal>InvoiceNumbersText}" wrapping="false" tooltip="{fiscal>InvoiceNumbersText}"/></t:template>
            </t:Column>
          </t:columns>
        </t:Table>

        <t:Table id="fiscalInvoiceLinesTable" selectionMode="MultiToggle" class="sapUiSizeCondensed sapUiSmallMarginTop"
          visibleRowCount="8" alternateRowColors="true" rows="{ path: 'fiscal>/invoiceLines' }">
          <t:extension>
            <OverflowToolbar>
              <Title text="Linhas de NF do fornecedor ({= ${fiscal>/invoiceLines}.length })"/>
            </OverflowToolbar>
          </t:extension>
          <t:columns>
            <t:Column label="NF/Série" width="9rem" sortProperty="TaxDocument">
              <t:template><Text text="{fiscal>TaxDocument}" wrapping="false"/></t:template>
            </t:Column>
            <t:Column label="Tipo" width="6rem">
              <t:template><Text text="{= ${fiscal>InvoiceType} === 'Return' ? 'Devolução' : 'Normal' }" wrapping="false"/></t:template>
            </t:Column>
            <t:Column label="Emitente" width="18rem">
              <t:template>
                <ObjectStatus text="{fiscal>CardCode} - {fiscal>CardName}"
                  icon="{= ${fiscal>IssuerMatchesSupplier} ? '' : 'sap-icon://alert' }"
                  state="{= ${fiscal>IssuerMatchesSupplier} ? 'None' : 'Warning' }"
                  tooltip="{= ${fiscal>IssuerMatchesSupplier} ? '' : 'Emitente com CPF/CNPJ diferente do fornecedor' }"/>
              </t:template>
            </t:Column>
            <t:Column label="Emissão" width="7rem" sortProperty="IssueDate">
              <t:template><Text wrapping="false" text="{ path: 'fiscal>IssueDate', formatter: 'formatter.formatDate' }"/></t:template>
            </t:Column>
            <t:Column label="Natureza" width="14rem">
              <t:template><Text text="{fiscal>UsageName}" wrapping="false" tooltip="{fiscal>UsageName}"/></t:template>
            </t:Column>
            <t:Column label="Contrato" width="9rem">
              <t:template><Text text="{fiscal>ContractCode}" wrapping="false"/></t:template>
            </t:Column>
            <t:Column label="Quantidade" hAlign="End" width="10rem">
              <t:template><ObjectNumber number="{ path: 'fiscal>Quantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }" unit="{fiscal>UnitOfMeasureCode}"/></t:template>
            </t:Column>
            <t:Column label="Vinculada" hAlign="End" width="9rem">
              <t:template><ObjectNumber number="{ path: 'fiscal>LinkedQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }" unit="KG"/></t:template>
            </t:Column>
            <t:Column label="Disponível" hAlign="End" width="10rem">
              <t:template>
                <ObjectNumber
                  number="{= ${fiscal>AvailableQuantity} === null ? 'UM sem fator' : ${ path: 'fiscal>AvailableQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } } }"
                  state="{= ${fiscal>AvailableQuantity} === null ? 'Error' : 'None' }" unit="{= ${fiscal>AvailableQuantity} === null ? '' : 'KG' }"/>
              </t:template>
            </t:Column>
          </t:columns>
        </t:Table>
      </VBox>
    </f:content>
  </f:DynamicPage>
</mvc:View>
```

- [ ] **Step 6: Controller (leitura)**

`webapp/controller/purchaseFiscalReconciliations/Main.controller.ts`:

```ts
import JSONModel from "sap/ui/model/json/JSONModel";
import MessageBox from "sap/m/MessageBox";
import Table from "sap/ui/table/Table";
import { Route$PatternMatchedEvent } from "sap/ui/core/routing/Route";
import CommonController from "../common/CommonController";
import { odataValue, sendJson } from "siagrob1/helpers/FetchHelpers";
import {
  FiscalReconciliationInvoiceLine, FiscalReconciliationTransaction,
} from "siagrob1/types/PurchaseFiscalReconciliation";

type FiscalFilter = {
  CardCode: string; CardName: string; ItemCode: string; ItemName: string; ContractCode: string;
  DateFrom: string; DateTo: string; Statuses: string[]; IssuerCardCode: string; IncludeFullyLinked: boolean;
};

/** Literal de string OData: aspas simples, ' dobrado, e codificado para a URL. */
const odataString = (value: string | null | undefined): string =>
  `'${encodeURIComponent((value ?? "").replace(/'/g, "''"))}'`;

/**
 * Conciliação Fiscal de Compras (spec 2026-10-13): NF do fornecedor × romaneio de compra (8/9/10/16/17), N:N com
 * quantidade. Só conciliação — não mexe em saldo. Fornecedor e produto vão ao servidor; Situação filtra no cliente.
 *
 * @namespace siagrob1.controller.purchaseFiscalReconciliations
 */
export default class Main extends CommonController {

  onInit(): void {
    this.createFilterModel(Main.defaultFilter());
    this.getView().setModel(new JSONModel({ transactions: [], allTransactions: [], invoiceLines: [], transactionCount: 0, busy: false }), "fiscal");

    this.getRouter().getRoute("purchaseFiscalReconciliations")
      .attachPatternMatched((ev: Route$PatternMatchedEvent) => this.routeMatched(ev));
  }

  private static defaultFilter(): FiscalFilter {
    return {
      CardCode: "", CardName: "", ItemCode: "", ItemName: "", ContractCode: "", DateFrom: "", DateTo: "",
      Statuses: [], IssuerCardCode: "", IncludeFullyLinked: false,
    };
  }

  /** A aba Entregas abre a tela já filtrada: ?cardCode=&itemCode=&contractCode=. */
  private routeMatched(ev: Route$PatternMatchedEvent): void {
    const query = (ev.getParameter("arguments") as { "?query"?: Record<string, string> })["?query"];
    if (!query?.cardCode) return;

    this.createFilterModel({
      ...Main.defaultFilter(),
      CardCode: query.cardCode, ItemCode: query.itemCode ?? "", ContractCode: query.contractCode ?? "",
    });
    void this.load();
  }

  onSearch(): void {
    void this.load();
  }

  onClearFilters(): void {
    this.createFilterModel(Main.defaultFilter());
    const model = this.getModel("fiscal") as JSONModel;
    model.setData({ transactions: [], allTransactions: [], invoiceLines: [], transactionCount: 0, busy: false });
  }

  protected filterData(): FiscalFilter {
    return (this.getModel("filter") as JSONModel).getData() as FiscalFilter;
  }

  async load(): Promise<void> {
    const f = this.filterData();
    const model = this.getModel("fiscal") as JSONModel;

    if (!f.CardCode || !f.ItemCode) {
      MessageBox.warning("Informe o fornecedor e o produto.");
      return;
    }

    const common = `CardCode=${odataString(f.CardCode)},ItemCode=${odataString(f.ItemCode)},` +
      `ContractCode=${odataString(f.ContractCode)},DateFrom=${odataString(f.DateFrom)},DateTo=${odataString(f.DateTo)}`;

    model.setProperty("/busy", true);
    const [transactions, lines] = await Promise.all([
      sendJson("GET", `${this.api.purchaseFiscalReconciliationTransactions}(${common})`),
      sendJson("GET", `${this.api.purchaseFiscalReconciliationInvoiceLines}(${common},` +
        `IssuerCardCode=${odataString(f.IssuerCardCode)},IncludeFullyLinked=${f.IncludeFullyLinked ? "true" : "false"})`),
    ]);
    model.setProperty("/busy", false);

    if (!transactions.ok || !lines.ok) {
      MessageBox.error((transactions.ok ? lines.message : transactions.message) ?? "Erro ao carregar a conciliação.");
      return;
    }

    model.setProperty("/allTransactions", odataValue<FiscalReconciliationTransaction[]>(transactions.data) ?? []);
    model.setProperty("/invoiceLines", odataValue<FiscalReconciliationInvoiceLine[]>(lines.data) ?? []);
    this.applyStatusFilter();
    (this.byId("fiscalTransactionsTable") as Table).clearSelection();
    (this.byId("fiscalInvoiceLinesTable") as Table).clearSelection();
  }

  private applyStatusFilter(): void {
    const model = this.getModel("fiscal") as JSONModel;
    const statuses = this.filterData().Statuses ?? [];
    const all = model.getProperty("/allTransactions") as FiscalReconciliationTransaction[];
    const rows = statuses.length ? all.filter(t => statuses.includes(t.Status)) : all;
    model.setProperty("/transactions", rows);
    model.setProperty("/transactionCount", rows.length);
  }

  selectedTransactions(): FiscalReconciliationTransaction[] {
    return this.selectedOf<FiscalReconciliationTransaction>("fiscalTransactionsTable");
  }

  selectedInvoiceLines(): FiscalReconciliationInvoiceLine[] {
    return this.selectedOf<FiscalReconciliationInvoiceLine>("fiscalInvoiceLinesTable");
  }

  private selectedOf<T>(tableId: string): T[] {
    const table = this.byId(tableId) as Table;
    return table.getSelectedIndices()
      .map(i => table.getContextByIndex(i)?.getObject() as T)
      .filter(Boolean);
  }
}
```

- [ ] **Step 7: Typecheck + lint**

Run: `yarn ts-typecheck` e `yarn lint`
Expected: sem erro. (`onLink`, `onExport`, `onOpenLinks` ainda não existem — a view os referencia, mas isso só falha em
tempo de execução; a Task 12 os cria. Se o `ui5lint` reclamar, rode-o só depois da Task 12.)

- [ ] **Step 8: Commit**

```bash
git add webapp/model/ServerRoutes.ts webapp/types/PurchaseFiscalReconciliation.ts webapp/manifest.json webapp/view/purchaseFiscalReconciliations webapp/controller/purchaseFiscalReconciliations
git commit -m "feat(invoice): tela de conciliação fiscal de compras" -m "Romaneios do fornecedor com físico, fiscal, diferença e situação, e as linhas de NF do fornecedor e das outras inscrições do mesmo CPF/CNPJ, com aviso quando o emitente é outro."
```

---

### Task 12: Frontend — vincular, vínculos do romaneio, confirmar, exportar

**Files:**
- Create: `webapp/view/purchaseFiscalReconciliations/fragments/LinkDialog.fragment.xml`
- Create: `webapp/view/purchaseFiscalReconciliations/fragments/LinksDialog.fragment.xml`
- Modify: `webapp/controller/purchaseFiscalReconciliations/Main.controller.ts`

**Interfaces:**
- Consumes: Task 11 (`load`, `selectedTransactions`, `selectedInvoiceLines`), rotas da Task 7.

- [ ] **Step 1: Diálogo de vincular**

`LinkDialog.fragment.xml`:

```xml
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core">
  <Dialog id="fiscalLinkDialog" title="Vincular NF × romaneio" contentWidth="52rem" busyIndicatorDelay="0" busy="{link>/busy}">
    <content>
      <MessageStrip class="sapUiSmallMargin" showIcon="true" type="Information"
        text="Distribuição sugerida: cada NF preenche o saldo dos romaneios por data; a sobra da NF fica no último. Ajuste as quantidades (KG) — zero desfaz o par."/>
      <Table items="{link>/pairs}">
        <columns>
          <Column><Text text="NF/Série"/></Column>
          <Column><Text text="Romaneio"/></Column>
          <Column hAlign="End"><Text text="Atual (KG)"/></Column>
          <Column hAlign="End" width="12rem"><Text text="Quantidade (KG)"/></Column>
        </columns>
        <items>
          <ColumnListItem>
            <Text text="{link>TaxDocument}"/>
            <Text text="{link>TransactionCode}"/>
            <ObjectNumber number="{ path: 'link>CurrentQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
            <Input textAlign="End" value="{ path: 'link>Quantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }, constraints: { minimum: 0 } }"/>
          </ColumnListItem>
        </items>
      </Table>
    </content>
    <beginButton><Button text="Gravar" type="Emphasized" press=".onSaveLinks"/></beginButton>
    <endButton><Button text="Cancelar" press=".onCloseLinkDialog"/></endButton>
  </Dialog>
</core:FragmentDefinition>
```

- [ ] **Step 2: Diálogo de vínculos do romaneio**

`LinksDialog.fragment.xml`:

```xml
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core">
  <Dialog id="fiscalLinksDialog" title="Romaneio {links>/transaction/Code} — NFs vinculadas" contentWidth="48rem" busyIndicatorDelay="0" busy="{links>/busy}">
    <content>
      <ObjectHeader
        title="{links>/transaction/StatusName}"
        number="{ path: 'links>/transaction/Difference', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"
        numberUnit="KG de diferença">
        <ObjectAttribute text="{= ${links>/transaction/FiscalReconciledBy} ? 'Conciliado manualmente por ' + ${links>/transaction/FiscalReconciledBy} : '' }"/>
      </ObjectHeader>
      <Table items="{links>/links}" noDataText="Nenhuma NF vinculada.">
        <columns>
          <Column><Text text="NF/Série"/></Column>
          <Column><Text text="Emitente"/></Column>
          <Column hAlign="End"><Text text="Quantidade (KG)"/></Column>
          <Column width="4rem"/>
        </columns>
        <items>
          <ColumnListItem>
            <Text text="{links>TaxDocument}"/>
            <Text text="{links>CardCode} - {links>CardName}"/>
            <ObjectNumber number="{ path: 'links>Quantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
            <Button icon="sap-icon://broken-link" type="Transparent" tooltip="Desvincular" press=".onRemoveLink"/>
          </ColumnListItem>
        </items>
      </Table>
    </content>
    <buttons>
      <Button text="Marcar como conciliado" type="Emphasized" visible="{= !${links>/transaction/FiscalReconciledAt} }" press=".onConfirmTransaction"/>
      <Button text="Desfazer conciliação" visible="{= !!${links>/transaction/FiscalReconciledAt} }" press=".onUnconfirmTransaction"/>
      <Button text="Fechar" press=".onCloseLinksDialog"/>
    </buttons>
  </Dialog>
</core:FragmentDefinition>
```

- [ ] **Step 3: Handlers no controller**

Acrescente imports e campos:

```ts
import Dialog from "sap/m/Dialog";
import Fragment from "sap/ui/core/Fragment";
import Event from "sap/ui/base/Event";
import Control from "sap/ui/core/Control";
import Spreadsheet from "sap/ui/export/Spreadsheet";
import { EdmType } from "sap/ui/export/library";
import { FiscalReconciliationLink, FiscalReconciliationPair } from "siagrob1/types/PurchaseFiscalReconciliation";
```

```ts
  private _linkDialog: Dialog;
  private _linksDialog: Dialog;
```

e os métodos (na classe):

```ts
  private async dialog(name: "LinkDialog" | "LinksDialog"): Promise<Dialog> {
    const view = this.getView();
    const dialog = await Fragment.load({
      name: `siagrob1.view.purchaseFiscalReconciliations.fragments.${name}`,
      controller: this,
      id: view.getId(),
    }) as Dialog;
    view.addDependent(dialog);
    return dialog;
  }

  async onLink(): Promise<void> {
    const lines = this.selectedInvoiceLines();
    const transactions = this.selectedTransactions();
    if (!lines.length || !transactions.length) {
      MessageBox.information("Selecione ao menos uma linha de NF e um romaneio.");
      return;
    }

    const result = await sendJson("POST", this.api.purchaseFiscalReconciliationSuggest, {
      InvoiceItemKeys: lines.map(l => l.PurchaseInvoiceItemKey),
      TransactionKeys: transactions.map(t => t.Key),
    });
    if (!result.ok) {
      MessageBox.error(result.message);
      return;
    }

    const pairs = odataValue<FiscalReconciliationPair[]>(result.data) ?? [];
    if (!pairs.length) {
      MessageBox.information("Nenhum par possível: confira o tipo da NF (normal × devolução) e dos romaneios.");
      return;
    }

    this._linkDialog ??= await this.dialog("LinkDialog");
    this.getView().setModel(new JSONModel({ pairs, busy: false }), "link");
    this._linkDialog.open();
  }

  async onSaveLinks(): Promise<void> {
    const model = this.getModel("link") as JSONModel;
    const pairs = model.getProperty("/pairs") as FiscalReconciliationPair[];

    model.setProperty("/busy", true);
    const result = await sendJson("POST", this.api.purchaseFiscalReconciliationSave, {
      InvoiceItemKeys: pairs.map(p => p.PurchaseInvoiceItemKey),
      TransactionKeys: pairs.map(p => p.StorageTransactionKey),
      Quantities: pairs.map(p => Number(p.Quantity) || 0),
    });
    model.setProperty("/busy", false);

    if (!result.ok) {
      MessageBox.error(result.message);
      return;
    }

    this._linkDialog.close();
    await this.load();
  }

  onCloseLinkDialog(): void {
    this._linkDialog.close();
  }

  async onOpenLinks(ev: Event): Promise<void> {
    const transaction = (ev.getSource() as Control).getBindingContext("fiscal").getObject() as FiscalReconciliationTransaction;
    this._linksDialog ??= await this.dialog("LinksDialog");
    this.getView().setModel(new JSONModel({ transaction, links: transaction.Links, busy: false }), "links");
    this._linksDialog.open();
  }

  async onRemoveLink(ev: Event): Promise<void> {
    const link = (ev.getSource() as Control).getBindingContext("links").getObject() as FiscalReconciliationLink;
    await this.runOnLinks(() => sendJson("POST", this.api.purchaseFiscalReconciliationRemove, { Key: link.Key }));
  }

  async onConfirmTransaction(): Promise<void> {
    const key = ((this.getModel("links") as JSONModel).getProperty("/transaction") as FiscalReconciliationTransaction).Key;
    await this.runOnLinks(() => sendJson("POST", this.api.purchaseFiscalReconciliationConfirm, { TransactionKey: key }));
  }

  async onUnconfirmTransaction(): Promise<void> {
    const key = ((this.getModel("links") as JSONModel).getProperty("/transaction") as FiscalReconciliationTransaction).Key;
    await this.runOnLinks(() => sendJson("POST", this.api.purchaseFiscalReconciliationUnconfirm, { TransactionKey: key }));
  }

  onCloseLinksDialog(): void {
    this._linksDialog.close();
  }

  /** Executa a ação, recarrega a tela e reabre o diálogo com o romaneio atualizado (ou fecha, se ele saiu do recorte). */
  private async runOnLinks(action: () => Promise<{ ok: boolean; message?: string }>): Promise<void> {
    const model = this.getModel("links") as JSONModel;
    const key = (model.getProperty("/transaction") as FiscalReconciliationTransaction).Key;

    model.setProperty("/busy", true);
    const result = await action();
    model.setProperty("/busy", false);
    if (!result.ok) {
      MessageBox.error(result.message);
      return;
    }

    await this.load();
    const all = (this.getModel("fiscal") as JSONModel).getProperty("/allTransactions") as FiscalReconciliationTransaction[];
    const fresh = all.find(t => t.Key === key);
    if (!fresh) {
      this._linksDialog.close();
      return;
    }
    model.setData({ transaction: fresh, links: fresh.Links, busy: false });
  }

  onExport(): void {
    const table = this.byId("fiscalTransactionsTable") as Table;
    const sheet = new Spreadsheet({
      dataSource: (this.getModel("fiscal") as JSONModel).getProperty("/transactions") as object[],
      fileName: "Conciliação Fiscal de Compras.xlsx",
      workbook: {
        columns: [
          { label: "Romaneio", property: "Code", type: EdmType.String },
          { label: "Data", property: "TransactionDate", type: EdmType.Date, inputFormat: "yyyy-MM-ddTHH:mm:ss", format: "dd/mm/yyyy" },
          { label: "Tipo", property: "TransactionTypeName", type: EdmType.String },
          { label: "Contrato", property: "ContractCodesText", type: EdmType.String },
          { label: "Placa", property: "TruckCode", type: EdmType.String },
          { label: "Físico (KG)", property: "PhysicalQuantity", type: EdmType.Number, scale: 3, delimiter: true },
          { label: "Fiscal (KG)", property: "FiscalQuantity", type: EdmType.Number, scale: 3, delimiter: true },
          { label: "Diferença (KG)", property: "Difference", type: EdmType.Number, scale: 3, delimiter: true },
          { label: "Situação", property: "StatusName", type: EdmType.String },
          { label: "NFs vinculadas", property: "InvoiceNumbersText", type: EdmType.String },
        ],
        context: { sheetName: "Conciliação" },
      },
    });
    void sheet.build().finally(() => sheet.destroy());
    void table;
  }
```

> `inputFormat` da data: confira no navegador (console, `await fetch(...)`) o formato real de `TransactionDate`
> (`2026-10-10T00:00:00` ou com fuso). Se vier com fuso, troque `inputFormat` por nada e `type` por `EdmType.DateTime`.

- [ ] **Step 4: Typecheck, lint, ui5lint**

Run: `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint`
Expected: sem erro novo. (Remova o `void table;` e a variável se o lint acusar não-uso — está ali só porque o
exemplo do house pattern pegava a tabela; aqui ela não é necessária.)

- [ ] **Step 5: Commit**

```bash
git add webapp/view/purchaseFiscalReconciliations webapp/controller/purchaseFiscalReconciliations
git commit -m "feat(invoice): vincular, desvincular e confirmar na conciliação fiscal" -m "O diálogo sugere a distribuição e deixa editar cada par; o detalhe do romaneio desvincula e marca ou desfaz a conciliação manual. Exporta a grade para Excel."
```

---

### Task 13: Frontend — aba Entregas do contrato de compra

**Files:**
- Modify: `webapp/view/purchaseContracts/fragments/PurchaseContractAllocations.fragment.xml`
- Modify: `webapp/controller/purchaseContracts/PurchaseContractsBaseController.ts` (handler `onOpenFiscalReconciliation`)

- [ ] **Step 1: Colunas e botão**

No `<OverflowToolbar>` do fragmento, antes do `<ToolbarSpacer />`:

```xml
          <Button text="Conciliar NFs" icon="sap-icon://chain-link" type="Transparent" press=".onOpenFiscalReconciliation" />
```

Depois da coluna "Nota Fiscal Venda":

```xml
      <t:Column label="NF do fornecedor" tooltip="NFs do fornecedor vinculadas ao romaneio" width="12rem">
        <t:template>
          <Text text="{allocationModel>SupplierInvoiceNumbers}" tooltip="{allocationModel>SupplierInvoiceNumbers}" wrapping="false" />
        </t:template>
      </t:Column>
      <t:Column label="Fiscal" tooltip="Quantidade das NFs vinculadas ao romaneio inteiro" hAlign="End">
        <t:template>
          <Text wrapping="false" text="{ path: 'allocationModel>FiscalQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }" />
        </t:template>
      </t:Column>
      <t:Column label="Diferença" tooltip="Fiscal − peso líquido do romaneio" hAlign="End">
        <t:template>
          <Text wrapping="false" text="{ path: 'allocationModel>FiscalDifference', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }" />
        </t:template>
      </t:Column>
      <t:Column label="Situação fiscal" width="9rem">
        <t:template>
          <ObjectStatus text="{allocationModel>FiscalStatus}"
            icon="{= ${allocationModel>IsSplitAcrossContracts} ? 'sap-icon://split' : '' }"
            tooltip="{= ${allocationModel>IsSplitAcrossContracts} ? 'Romaneio rateado em mais de um contrato: os números fiscais são do romaneio inteiro.' : '' }"
            state="{= ${allocationModel>FiscalStatus} === 'Conciliado' ? 'Success' : ${allocationModel>FiscalStatus} === 'Parcial' ? 'Warning' : 'None' }" />
        </t:template>
      </t:Column>
```

- [ ] **Step 2: Handler**

Em `PurchaseContractsBaseController.ts`, perto de `getAllocations`:

```ts
  /** Abre a Conciliação Fiscal de Compras já filtrada pelo fornecedor, produto e código deste contrato. */
  onOpenFiscalReconciliation(): void {
    const contract = this.getView().getBindingContext()?.getObject() as { CardCode?: string; ItemCode?: string; Code?: string } | undefined;
    if (!contract?.CardCode || !contract.ItemCode) {
      MessageBox.information("Contrato sem fornecedor ou produto.");
      return;
    }

    this.getRouter().navTo("purchaseFiscalReconciliations", {
      "?query": { cardCode: contract.CardCode, itemCode: contract.ItemCode, contractCode: contract.Code ?? "" },
    });
  }
```

> Confira de onde o Detail do contrato lê o contrato corrente (binding da view ou um JSONModel próprio — procure
> `CardCode` em `purchaseContracts/Detail.controller.ts`) e use a mesma fonte; a linha acima assume o binding da view.

- [ ] **Step 3: Typecheck + lint**

Run: `yarn ts-typecheck` e `yarn lint`
Expected: sem erro.

- [ ] **Step 4: Commit**

```bash
git add webapp/view/purchaseContracts/fragments/PurchaseContractAllocations.fragment.xml webapp/controller/purchaseContracts/PurchaseContractsBaseController.ts
git commit -m "feat(purchase-contract): situação fiscal e atalho de conciliação na aba Entregas" -m "NFs do fornecedor, fiscal, diferença e situação por romaneio, com indicativo de romaneio rateado; o botão abre a Conciliação Fiscal já filtrada pelo contrato."
```

---

### Task 14: Frontend — tela do relatório

**Files:**
- Create: `webapp/view/reports/purchaseFiscalReconciliation/Main.view.xml`
- Create: `webapp/controller/reports/purchaseFiscalReconciliation/Main.controller.ts`
- Modify: `webapp/manifest.json` (rota + target ao lado de `storageRemittancesReport`, ~linhas 1039 e 2292)

- [ ] **Step 1: Manifest**

```json
        {
          "pattern": "purchase-fiscal-reconciliation/report",
          "name": "purchaseFiscalReconciliationReport",
          "target": "purchaseFiscalReconciliationReport"
        },
```

```json
        "purchaseFiscalReconciliationReport": {
          "id": "purchaseFiscalReconciliationReport",
          "level": 1,
          "name": "siagrob1.view.reports.purchaseFiscalReconciliation.Main",
          "clearControlAggregation": true
        },
```

- [ ] **Step 2: Controller**

```ts
import InvoiceReportController from "../InvoiceReportController";
import ServerRoutes from "siagrob1/model/ServerRoutes";

/**
 * Entregas × NF do Fornecedor (spec 2026-10-13 §8): os mesmos filtros da Conciliação Fiscal de Compras. Situação:
 * 0 Sem NF, 1 Parcial, 2 Conciliado (vazio = todas).
 * @namespace siagrob1.controller.reports.purchaseFiscalReconciliation
 */
export default class Main extends InvoiceReportController {
	protected readonly routeName = "purchaseFiscalReconciliationReport";
	protected readonly formId = "purchaseFiscalReconciliationReportForm";
	protected readonly serverRoute = ServerRoutes.purchaseFiscalReconciliationReport;

	protected defaults() {
		return { CardCode: "", CardName: "", ItemCode: "", ItemName: "", ContractCode: "", FromDate: null, ToDate: null, FiscalStatuses: [] };
	}

	protected buildPayload() {
		const data = super.buildPayload();
		data.Statuses = ((data.FiscalStatuses as unknown[]) ?? []).map(Number);
		delete data.FiscalStatuses;
		delete data.NfeStatuses;
		delete data.CardName;
		delete data.ItemName;
		return data;
	}
}
```

- [ ] **Step 3: View**

```xml
<mvc:View
	controllerName="siagrob1.controller.reports.purchaseFiscalReconciliation.Main"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form"
	xmlns:l="sap.ui.layout">
	<Page title="Entregas × NF do Fornecedor">
		<f:SimpleForm id="purchaseFiscalReconciliationReportForm" editable="true" layout="ResponsiveGridLayout"
			labelSpanXL="4" labelSpanL="4" labelSpanM="4" labelSpanS="12" adjustLabelSpan="false"
			emptySpanXL="0" emptySpanL="0" emptySpanM="0" emptySpanS="0" columnsXL="2" columnsL="2" columnsM="2"
			singleContainerFullSize="false" busyIndicatorDelay="0">
			<f:content>
				<core:Title text="Fornecedor e produto" emphasized="true"/>
				<Label text="Fornecedor" required="true"/>
				<Input required="true" showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openSuppliersValueHelp" value="{params>/CardCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData><core:CustomData key="descriptionProperty" value="CardName"/></customData>
				</Input>
				<Input value="{params>/CardName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
				<Label text="Produto" required="true"/>
				<Input required="true" showValueHelp="true" valueHelpOnly="true" valueHelpRequest=".openItemValueHelp" value="{params>/ItemCode}">
					<layoutData><l:GridData span="XL3 L3 M3 S12"/></layoutData>
					<customData><core:CustomData key="descriptionProperty" value="ItemName"/></customData>
				</Input>
				<Input value="{params>/ItemName}" editable="false">
					<layoutData><l:GridData span="XL5 L5 M5 S12"/></layoutData>
				</Input>
				<Label text="Contrato de compra"/>
				<Input value="{params>/ContractCode}"/>
				<core:Title text="Período e situação" emphasized="true"/>
				<Label text="Data de"/>
				<DatePicker value="{ path: 'params>/FromDate', type: 'sap.ui.model.type.String' }" displayFormat="dd/MM/yyyy" valueFormat="yyyy-MM-dd"/>
				<Label text="Data até"/>
				<DatePicker value="{ path: 'params>/ToDate', type: 'sap.ui.model.type.String' }" displayFormat="dd/MM/yyyy" valueFormat="yyyy-MM-dd"/>
				<Label text="Situação"/>
				<MultiComboBox selectedKeys="{params>/FiscalStatuses}">
					<core:Item key="0" text="Sem NF"/>
					<core:Item key="1" text="Parcial"/>
					<core:Item key="2" text="Conciliado"/>
				</MultiComboBox>
			</f:content>
		</f:SimpleForm>
		<footer>
			<OverflowToolbar>
				<ToolbarSpacer/>
				<Button text="Imprimir" type="Emphasized" press=".onPrintReport"/>
			</OverflowToolbar>
		</footer>
	</Page>
</mvc:View>
```

> `InvoiceReportController.routeMatched` injeta `Statuses: ["0","1","3"]` (situação de documento) — o `buildPayload`
> acima sobrescreve `Statuses` com o filtro fiscal, então aquele default não vaza. Confira no Network do navegador o
> corpo do POST (Task 15).

- [ ] **Step 4: Typecheck + lint**

Run: `yarn ts-typecheck` e `yarn lint`
Expected: sem erro.

- [ ] **Step 5: Commit**

```bash
git add webapp/manifest.json webapp/view/reports/purchaseFiscalReconciliation webapp/controller/reports/purchaseFiscalReconciliation
git commit -m "feat(reports): tela do relatório entregas × NF do fornecedor"
```

---

### Task 15: Verificação de ponta a ponta (CEAGUI, navegador)

Siga `superpowers:verification-before-completion`. "Pronto" = alcançável pelo menu, do jeito que o usuário usa.

- [ ] **Step 1: Aplicar as migrations SÓ no CEAGUI local**

```bash
cd siagro-b1-backend
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Antes, confira em `SiagroB1.Web/appsettings.Ceagui-Development.json` que as connection strings apontam para
`localhost` (`CEAGUI_SIAGRO_DEV` / `CEAGUI_SIAGRO_COMMON_DEV`). Se o `update` começar a aplicar migrations antigas,
**pare** (Ctrl+C) e investigue — é banco errado.

- [ ] **Step 2: Subir a stack**

Web, Gateway e Reports com `--launch-profile ceagui`; frontend `yarn start:dev` (ou `yarn start`, conforme
`package.json`). Rode cada um em background e anote o PID — para derrubar, só `taskkill /PID <pid>` (nunca por nome).

- [ ] **Step 3: Montar dados de teste pela própria aplicação** (ou reaproveitar existentes do CEAGUI)

Fornecedor PF com CPF X e um segundo parceiro com o mesmo CPF (outra IE); contrato de compra de SOJA desse fornecedor;
dois romaneios de compra (29.820 e 29.900 kg) alocados no contrato; um Documento de Entrada de 60.000 kg do fornecedor;
um segundo Documento de Entrada de 500 kg emitido pelo segundo parceiro.

- [ ] **Step 4: Roteiro**

1. Menu Compras → "Conciliação Fiscal de Compras" abre. Filtrar fornecedor + produto → 2 romaneios "Sem NF"; 2 linhas de NF (a do outro parceiro **sem** aviso, mesmo CPF).
2. Selecionar a NF de 60.000 + os 2 romaneios → Vincular → sugestão 29.820 / 30.180 → Gravar. Romaneio 1 "Conciliado", romaneio 2 "Parcial" (+280).
3. Detalhe do romaneio 2 → "Marcar como conciliado" → vira Conciliado com "Conciliado manualmente por …".
4. Vincular a NF de 500 ao romaneio 2 → confirmação cai (volta a Parcial). Desvincular pelo detalhe.
5. Filtro Emitente = um terceiro parceiro com outro CPF, com NF → linha com ícone de alerta.
6. Abrir a NF de 60.000 no Documento de Entrada e reduzir a quantidade para 50.000 → recusa "desvincule…".
7. Cancelar a NF de 500 (se vinculada) → some dos vínculos.
8. Contrato de compra → aba Entregas: colunas NF do fornecedor/Fiscal/Diferença/Situação; "Conciliar NFs" abre a tela filtrada.
9. Relatórios → "Entregas × NF do Fornecedor" → PDF com o contrato, os 2 romaneios, as NFs abaixo e a seção de NFs livres; números iguais aos da tela.
10. Exportar Excel na tela → abre com as colunas.

Para cada passo, confira também no banco (`sqlcmd -S localhost -E -I -d CEAGUI_SIAGRO_DEV -Q "SELECT * FROM PURCHASE_FISCAL_RECONCILIATIONS"`).

- [ ] **Step 5: Gates finais**

Backend: `dotnet build SiagroB1.sln` e `dotnet test SiagroB1.Application.Tests` — verde.
Frontend: `yarn ts-typecheck`, `yarn lint`, `yarn ui5lint` — sem erro novo (`yarn test` tem gate de cobertura irreal
neste projeto; não é critério).

- [ ] **Step 6: Registrar o que ficou de fora**

Migrations aplicadas só no CEAGUI local; Yokotobi (SAPB1) não verificado; nenhum push. Atualize a memória
`purchase-fiscal-reconciliation-feature.md` com o estado final.
