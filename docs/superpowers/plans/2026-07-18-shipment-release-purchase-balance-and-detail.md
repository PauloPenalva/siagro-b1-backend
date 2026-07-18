# Saldo da liberação por romaneio de compra + tela de detalhe — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Corrigir `ShipmentRelease.ShippedQuantity` para derivar de romaneios `Purchase`/`PurchaseReturn` (e não dos tipos de venda), e criar a tela de detalhe da liberação exibindo motivo de cancelamento e os romaneios que compõem o saldo.

**Architecture:** A Parte A troca a regra de cálculo num único serviço e substitui a lista de tipos duplicada em 5 pontos por um predicado compartilhado, seguida de uma migration de backfill de dados. A Parte B adiciona uma detail page read-only nos moldes das 10 existentes (`ObjectPageLayout` + fragmentos `SimpleForm`), com a tabela de romaneios bindada diretamente em `/StorageTransactions` filtrado.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server), OData v4 (`Microsoft.AspNetCore.OData`), xUnit + EF InMemory; OpenUI5 1.141 + TypeScript.

**Spec:** [`../specs/2026-07-18-shipment-release-purchase-balance-and-detail-design.md`](../specs/2026-07-18-shipment-release-purchase-balance-and-detail-design.md)

## Global Constraints

- **Commits são manuais.** Paulo commita os dois repos ele mesmo. Nenhuma task termina em `git commit` — termina em verificação. Não rodar `git commit` nem `git push`.
- **Migrations escritas à mão.** O `AppDbContextModelSnapshot` está dessincronizado do banco; nunca rodar scaffolding que gere `AlterColumn` espúrio. Toda migration precisa de um `.Designer.cs` (convenção do repo: 167 migrations, todas com o seu).
- **Fórmula do saldo:** `ShippedQuantity = Σ Purchase.NetWeight − Σ PurchaseReturn.NetWeight`, sobre romaneios com `TransactionStatus != Cancelled`. `PurchaseQtyComplement` (10) e `PurchasePriceComplement` (11) **não** entram.
- **Valores do enum `StorageTransactionType`:** `SalesShipment = 7`, `Purchase = 8`, `PurchaseReturn = 9`, `SalesShipmentReturn = 12`. `StorageTransactionsStatus.Cancelled = 2`.
- **Frontend sem i18n:** todos os textos em pt-BR hardcoded, como em todas as views do app.
- **Frontend usa `SimpleForm`** com `editable="{ui>/editable}"`, seguindo as 10 detail pages existentes (decisão registrada no spec).
- **Não há harness de teste automatizado para as views/controllers do frontend.** Verificação das tasks B é `yarn ts-typecheck`, `npx eslint`, `npx ui5lint` e roteiro manual — não inventar testes QUnit/OPA5 para elas.
- **Build do backend:** os apps `SiagroB1.Web`/`Gateway`/`Reports` podem estar rodando e travando os `.dll`. Se `dotnet build SiagroB1.sln` falhar com `MSB3021`/`MSB3027`, é lock de arquivo, não erro de compilação — compile o projeto isolado com `-p:BaseOutputPath=<temp>/`.

## File Structure

**Backend (`siagro-b1-backend`)**

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateShippedService.cs` | dono da regra: consulta EF + predicado `AffectsShippedQuantity` |
| `SiagroB1.Application/Services/StorageTransactions/{Create,Confirmed,Cancel,Reverse}Service.cs` | consomem o predicado nos hooks |
| `SiagroB1.Migrations/AppContext/20260719090000_BackfillShippedQuantityFromPurchase.cs` (+ `.Designer.cs`) | backfill de dados, sem mudança de esquema |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` | expõe `ConsumedQuantity` no EDM |

**Frontend (`siagro-b1-frontend`)**

| Arquivo | Responsabilidade |
|---|---|
| `webapp/controller/shipmentReleases/Detail.controller.ts` | bind do elemento, filtro da tabela de romaneios |
| `webapp/view/shipmentReleases/Detail.view.xml` | casca `ObjectPageLayout` + seções |
| `webapp/view/shipmentReleases/fragments/Form.fragment.xml` | seção Dados |
| `webapp/view/shipmentReleases/fragments/Audit.fragment.xml` | seção Auditoria (motivo do cancelamento) |
| `webapp/view/shipmentReleases/fragments/Transactions.fragment.xml` | tabela de romaneios |
| `webapp/manifest.json` | rota + target |
| `webapp/{view,controller}/shipmentReleases/Main.*` | botão "Visualizar" + `onDetail()` |

---

# Parte A — Correção da regra de saldo

### Task A1: Regra `Purchase`/`PurchaseReturn` no serviço e nos 4 hooks

Serviço e hooks mudam juntos: separá-los deixaria a suíte vermelha no meio (o hook filtraria tipos que a regra não conta mais). Esta task realinha também os 4 arquivos de teste que dependiam da regra antiga.

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesRecalculateShippedService.cs`
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCreateService.cs:76-77`
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsConfirmedService.cs:74-76`
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCancelService.cs:47-48`
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsReverseService.cs:59-60`
- Test: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesRecalculateShippedServiceTests.cs`
- Test: `SiagroB1.Application.Tests/StorageTransactions/StorageTransactionsCancelHookTests.cs`
- Test: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesCancelationServiceTests.cs`
- Test: `SiagroB1.Application.Tests/ShipmentReleases/ShipmentReleasesRecalculateBalanceServiceTests.cs`

**Interfaces:**
- Produces: `ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(StorageTransactionType type) → bool` (static), consumido pelos 4 hooks.
- Produces: `CalculateShippedAsync(Guid) → Task<decimal>` e `RecalculateAsync(Guid) → Task` mantêm as assinaturas atuais; só a regra interna muda.

- [ ] **Step 1: Reescrever os testes da regra**

Substituir os 4 testes de `ShipmentReleasesRecalculateShippedServiceTests.cs` (helpers `SeedReleaseAsync`, `Tx`, `ShippedAsync` nas linhas 16-48 permanecem exatamente como estão):

```csharp
    [Fact]
    public async Task Recalc_PurchaseMinusPurchaseReturn_UsingNetWeight()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.Purchase, 400m),
            Tx(sr.Key, StorageTransactionType.PurchaseReturn, 150m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(250m, await ShippedAsync(sr.Key)); // 400 − 150
    }

    [Fact]
    public async Task Recalc_IgnoresCancelled_CountsPending()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.Purchase, 40m, StorageTransactionsStatus.Pending),
            Tx(sr.Key, StorageTransactionType.Purchase, 25m, StorageTransactionsStatus.Cancelled));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(40m, await ShippedAsync(sr.Key)); // pending conta, cancelled não
    }

    [Fact]
    public async Task Recalc_IgnoresSalesTypes()
    {
        // tipos de venda pertencem ao fluxo de shipmentBilling, não ao de liberação
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.SalesShipment, 500m),
            Tx(sr.Key, StorageTransactionType.SalesShipmentReturn, 200m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(0m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_IgnoresComplements()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        _db.Context.StorageTransactions.AddRange(
            Tx(sr.Key, StorageTransactionType.PurchaseQtyComplement, 50m),
            Tx(sr.Key, StorageTransactionType.PurchasePriceComplement, 70m),
            Tx(sr.Key, StorageTransactionType.Purchase, 10m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(10m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_IgnoresOtherReleases()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        var other = Guid.NewGuid();
        _db.Context.StorageTransactions.AddRange(
            Tx(other, StorageTransactionType.Purchase, 70m),
            Tx(sr.Key, StorageTransactionType.Purchase, 10m));
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(10m, await ShippedAsync(sr.Key));
    }

    [Fact]
    public async Task Recalc_NoTransactions_SetsZero()
    {
        var sr = await SeedReleaseAsync(released: 1000m);

        await Service().RecalculateAsync(sr.Key);

        Assert.Equal(0m, await ShippedAsync(sr.Key));
    }

    [Theory]
    [InlineData(StorageTransactionType.Purchase, true)]
    [InlineData(StorageTransactionType.PurchaseReturn, true)]
    [InlineData(StorageTransactionType.SalesShipment, false)]
    [InlineData(StorageTransactionType.SalesShipmentReturn, false)]
    [InlineData(StorageTransactionType.PurchaseQtyComplement, false)]
    [InlineData(StorageTransactionType.PurchasePriceComplement, false)]
    [InlineData(StorageTransactionType.Transfer, false)]
    public void AffectsShippedQuantity_MatchesRule(StorageTransactionType type, bool expected)
    {
        Assert.Equal(expected, ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(type));
    }
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --nologo`
Expected: FAIL — erro de compilação `CS0117: "ShipmentReleasesRecalculateShippedService" não contém uma definição para "AffectsShippedQuantity"`.

- [ ] **Step 3: Implementar a regra e o predicado**

Em `ShipmentReleasesRecalculateShippedService.cs`, substituir `CalculateShippedAsync` e acrescentar o predicado:

```csharp
    /// <summary>
    /// Tipos de romaneio que consomem o saldo da liberação. Fonte única da regra —
    /// os hooks em StorageTransactions consultam este predicado em vez de repetir
    /// a lista (a duplicação anterior deixou serviço e hooks divergirem em silêncio).
    /// </summary>
    public static bool AffectsShippedQuantity(StorageTransactionType type) =>
        type is StorageTransactionType.Purchase or StorageTransactionType.PurchaseReturn;

    /// <summary>
    /// Calcula o volume romaneado SEM persistir nada, para quem precisa decidir
    /// antes de gravar (ex.: cancelamento, que recusa quando não há saldo).
    /// </summary>
    public async Task<decimal> CalculateShippedAsync(Guid shipmentReleaseKey)
    {
        // usado = Σ(Purchase.Net) − Σ(PurchaseReturn.Net); Pending conta, Cancelled não.
        // Lista inline (e não AffectsShippedQuantity) porque o EF precisa traduzir para SQL.
        return await context.StorageTransactions
            .Where(t => t.ShipmentReleaseKey == shipmentReleaseKey
                        && t.TransactionStatus != StorageTransactionsStatus.Cancelled
                        && (t.TransactionType == StorageTransactionType.Purchase
                            || t.TransactionType == StorageTransactionType.PurchaseReturn))
            .SumAsync(t => t.TransactionType == StorageTransactionType.Purchase
                ? t.NetWeight
                : -t.NetWeight);
    }
```

`RecalculateAsync` fica inalterado.

- [ ] **Step 4: Trocar o filtro nos 4 hooks**

Em cada um dos quatro arquivos, substituir a condição de tipo pelo predicado.

`StorageTransactionsCreateService.cs:76-77`:
```csharp
                if (entity.ShipmentReleaseKey.HasValue &&
                    ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(entity.TransactionType))
```

`StorageTransactionsConfirmedService.cs:74-76`:
```csharp
        if (commitMode == CommitMode.Auto &&
            st.ShipmentReleaseKey.HasValue &&
            ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(st.TransactionType))
```

`StorageTransactionsCancelService.cs:47-48`:
```csharp
            if (doc.ShipmentReleaseKey.HasValue &&
                ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(doc.TransactionType))
```

`StorageTransactionsReverseService.cs:59-60`:
```csharp
            if (doc.ShipmentReleaseKey.HasValue &&
                ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(doc.TransactionType))
```

Os quatro já têm `using SiagroB1.Application.Services.ShipmentReleases;` (injetam `recalcShipped`); não é preciso acrescentar import.

- [ ] **Step 5: Realinhar o teste do hook de cancelamento**

Em `StorageTransactionsCancelHookTests.cs`, trocar o nome do teste e o tipo do romaneio (linhas 16 e 35), mantendo o resto idêntico:

```csharp
    public async Task Cancel_PurchaseLinkedToRelease_RecalculatesShippedQuantity()
```
```csharp
            TransactionType = StorageTransactionType.Purchase,
```

E acrescentar o caso negativo, que trava o comportamento do predicado ponta a ponta:

```csharp
    [Fact]
    public async Task Cancel_SalesShipmentLinkedToRelease_DoesNotTouchShippedQuantity()
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = 100m,
            ShippedQuantity = 80m,
            Status = ReleaseStatus.Actived,
        };
        var tx = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "ST",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionOrigin = TransactionCode.StorageTransaction,
            NetWeight = 80m,
            ShipmentReleaseKey = release.Key,
        };
        _db.Context.ShipmentReleases.Add(release);
        _db.Context.StorageTransactions.Add(tx);
        await _db.Context.SaveChangesAsync();

        var recalc = new ShipmentReleasesRecalculateShippedService(_db.Context);
        var service = new StorageTransactionsCancelService(_db, recalc);

        await service.ExecuteAsync(tx.Key, "tester");

        var reloaded = await _db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(80m, reloaded.ShippedQuantity); // hook não dispara para tipo de venda
    }
```

- [ ] **Step 6: Realinhar os testes de cancelamento da liberação**

Em `ShipmentReleasesCancelationServiceTests.cs`, trocar **todas** as ocorrências de `StorageTransactionType.SalesShipment` por `StorageTransactionType.Purchase` e de `StorageTransactionType.SalesShipmentReturn` por `StorageTransactionType.PurchaseReturn`. Os valores e asserções não mudam — a aritmética é a mesma (`+` para Purchase, `−` para PurchaseReturn).

Dois testes precisam de ajuste além da troca mecânica:

- `Cancel_WithPurchaseTransaction_Succeeds` e `Cancel_PurchaseTransactions_DoNotCountAsConsumed` foram escritos quando Purchase **não** contava. Agora contam. Substituir os dois por:

```csharp
    [Fact]
    public async Task Cancel_PurchaseTransactions_CountAsConsumed()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 300m, "ST-777");

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        var saved = await ReloadAsync(sr.Key);
        Assert.Equal(300m, saved.ShippedQuantity);
        Assert.Equal(300m, saved.ConsumedQuantity);
    }

    [Fact]
    public async Task Cancel_SalesShipmentLinked_DoesNotCountAsConsumed()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.SalesShipment, 300m, "ST-888");

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        var saved = await ReloadAsync(sr.Key);
        Assert.Equal(0m, saved.ShippedQuantity);
        Assert.Equal(0m, saved.ConsumedQuantity);
    }
```

- `Cancel_MixedPurchaseAndSalesShipment_ConsumesOnlyShipped` inverte: agora quem conta é o Purchase.

```csharp
    [Fact]
    public async Task Cancel_MixedPurchaseAndSales_ConsumesOnlyPurchase()
    {
        var sr = await SeedReleaseAsync(released: 1000m);
        await AddTransactionAsync(sr.Key, StorageTransactionType.Purchase, 300m, "ST-A");
        await AddTransactionAsync(sr.Key, StorageTransactionType.SalesShipment, 200m, "ST-B");

        await Service().ExecuteAsync(sr.Key, "maria", Reason);

        Assert.Equal(300m, (await ReloadAsync(sr.Key)).ConsumedQuantity);
    }
```

- [ ] **Step 7: Realinhar o teste de recálculo de saldo**

Em `ShipmentReleasesRecalculateBalanceServiceTests.cs`, trocar a única ocorrência de `StorageTransactionType.SalesShipment` por `StorageTransactionType.Purchase`. Asserções não mudam.

`ShipmentReleaseMovementGuardServiceTests.cs` **não muda**: o guard cobre venda e compra, e seus testes já exercitam os dois.

- [ ] **Step 8: Rodar a suíte inteira**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --nologo -v q`
Expected: `Aprovado!` com 0 falhas. Se algum teste ainda referenciar tipo de venda esperando que mova o saldo, corrigir o teste (a regra nova é a verdade).

---

### Task A2: Migration de backfill

**Files:**
- Create: `SiagroB1.Migrations/AppContext/20260719090000_BackfillShippedQuantityFromPurchase.cs`
- Create: `SiagroB1.Migrations/AppContext/20260719090000_BackfillShippedQuantityFromPurchase.Designer.cs`

**Interfaces:**
- Consumes: nada de A1 em tempo de compilação; depende de A1 apenas semanticamente (a migration alinha os dados à regra nova).
- Produces: nenhuma API. `AppDbContextModelSnapshot` fica **inalterado** — a migration não mexe em esquema.

- [ ] **Step 1: Escrever a migration**

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class BackfillShippedQuantityFromPurchase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Realinha ShippedQuantity à regra nova: Σ(Purchase.Net) − Σ(PurchaseReturn.Net).
            // Tipos: Purchase = 8, PurchaseReturn = 9. Status Cancelled = 2.
            migrationBuilder.Sql(@"
                UPDATE SR
                SET SR.ShippedQuantity = ISNULL((
                    SELECT SUM(CASE
                                 WHEN t.TransactionType = 8 THEN t.NetWeight
                                 WHEN t.TransactionType = 9 THEN -t.NetWeight
                                 ELSE 0 END)
                    FROM STORAGE_TRANSACTIONS t
                    WHERE t.ShipmentReleaseKey = SR.[Key]
                      AND t.TransactionStatus <> 2
                      AND t.TransactionType IN (8, 9)
                ), 0)
                FROM SHIPMENT_RELEASES SR;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaura a regra antiga (SalesShipment = 7, SalesShipmentReturn = 12).
            migrationBuilder.Sql(@"
                UPDATE SR
                SET SR.ShippedQuantity = ISNULL((
                    SELECT SUM(CASE
                                 WHEN t.TransactionType = 7  THEN t.NetWeight
                                 WHEN t.TransactionType = 12 THEN -t.NetWeight
                                 ELSE 0 END)
                    FROM STORAGE_TRANSACTIONS t
                    WHERE t.ShipmentReleaseKey = SR.[Key]
                      AND t.TransactionStatus <> 2
                      AND t.TransactionType IN (7, 12)
                ), 0)
                FROM SHIPMENT_RELEASES SR;");
        }
    }
}
```

- [ ] **Step 2: Gerar o `.Designer.cs`**

O designer é o snapshot do modelo no momento da migration. Como o modelo não muda, é uma cópia do `AppDbContextModelSnapshot` atual com o cabeçalho trocado. Gerar assim (bash, a partir de `siagro-b1-backend/SiagroB1.Migrations/AppContext`):

```bash
cat > /tmp/hdr.txt <<'EOF'
// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SiagroB1.Infra.Context;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260719090000_BackfillShippedQuantityFromPurchase")]
    partial class BackfillShippedQuantityFromPurchase
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
EOF
printf '\xEF\xBB\xBF' > 20260719090000_BackfillShippedQuantityFromPurchase.Designer.cs
cat /tmp/hdr.txt >> 20260719090000_BackfillShippedQuantityFromPurchase.Designer.cs
tail -n +17 AppDbContextModelSnapshot.cs >> 20260719090000_BackfillShippedQuantityFromPurchase.Designer.cs
```

(`tail -n +17` descarta as 16 linhas de cabeçalho do snapshot, que terminam em `protected override void BuildModel(ModelBuilder modelBuilder)`, e mantém o corpo a partir de `        {`.)

- [ ] **Step 3: Compilar e confirmar que o modelo segue sincronizado**

Run: `dotnet build SiagroB1.Migrations/SiagroB1.Migrations.csproj --nologo -v q`
Expected: 0 erros.

Run: `dotnet ef migrations has-pending-model-changes --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext --no-build`
Expected: `No changes have been made to the model since the last migration.`

- [ ] **Step 4: Medir o impacto antes de aplicar**

Antes de rodar a migration em qualquer banco com dados, conferir quantas linhas mudam:

```sql
SELECT TransactionType, COUNT(*) AS Qtd, SUM(NetWeight) AS Peso
FROM STORAGE_TRANSACTIONS
WHERE ShipmentReleaseKey IS NOT NULL
GROUP BY TransactionType;
```

Se aparecerem tipos 7 ou 12 com volume relevante, **parar e reportar a Paulo** antes de aplicar — significa que existe um fluxo de venda vinculado a liberações que a premissa do spec não previu.

- [ ] **Step 5: Aplicar**

> ⚠️ **NÃO usar o launch profile `db-migration`.** Ele define `ASPNETCORE_ENVIRONMENT=Migration`,
> para o qual **não existe** `appsettings.Migration.json`. Sem arquivo de override sobra apenas
> o `appsettings.json` base, cuja `ConnectionStrings:SiagroDB` aponta para **`IDX_SIAGRO_PRD`
> (produção)** — verificado em `Program.cs:56`, que não trata esse ambiente de forma especial.

Aplicar sempre com o ambiente explícito do banco alvo, conferindo antes para onde aponta:

```bash
# confirmar o alvo ANTES de qualquer escrita
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations list \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext

ASPNETCORE_ENVIRONMENT=Development dotnet ef database update \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```

Bancos por ambiente: `Development` → `MHAGRO_SIAGRO_HOM`; `Staging` → `IDX_SIAGRO_HOM`;
`Yokotobi` → `IDX_SIAGRO_DEV`; ambiente ausente/`Migration`/`Production` → `IDX_SIAGRO_PRD`.

Conferir depois que uma liberação com romaneio de compra deixou de ter `ShippedQuantity = 0`.

---

# Parte B — Tela de detalhe

### Task B1: Expor `ConsumedQuantity` no EDM

**Files:**
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs:53-55`

**Interfaces:**
- Produces: propriedade OData `ShipmentReleases/ConsumedQuantity` (`Edm.Decimal`), consumida pela view da Task B3.

- [ ] **Step 1: Registrar a propriedade**

`ConsumedQuantity` é `[NotMapped]`; com `autoExpandSelect: true` no manifest o modelo monta um `$select` que só inclui propriedades do EDM, então sem este registro o binding vem vazio. Acrescentar logo após o registro existente de `AvailableQuantity`:

```csharp
        modelBuilder.EntitySet<ShipmentRelease>("ShipmentReleases");
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(ShipmentRelease))
            .AddProperty(typeof(ShipmentRelease).GetProperty(nameof(ShipmentRelease.AvailableQuantity)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(ShipmentRelease))
            .AddProperty(typeof(ShipmentRelease).GetProperty(nameof(ShipmentRelease.ConsumedQuantity)));
```

- [ ] **Step 2: Compilar**

Run: `dotnet build SiagroB1.Web/SiagroB1.Web.csproj --nologo -v q -p:BaseOutputPath=/tmp/webbuild/`
Expected: 0 erros.

- [ ] **Step 3: Conferir no metadata**

Subir `SiagroB1.Web` e abrir `http://localhost:50000/odata/$metadata`.
Expected: dentro de `<EntityType Name="ShipmentRelease">` aparecem `<Property Name="AvailableQuantity" .../>` **e** `<Property Name="ConsumedQuantity" .../>`.

---

### Task B2: Rota, navegação e casca da tela

Entrega navegável de ponta a ponta: da lista abre a tela, que mostra o cabeçalho. As seções entram nas tasks seguintes.

**Files:**
- Modify: `webapp/manifest.json`
- Create: `webapp/controller/shipmentReleases/Detail.controller.ts`
- Create: `webapp/view/shipmentReleases/Detail.view.xml`
- Modify: `webapp/view/shipmentReleases/Main.view.xml`
- Modify: `webapp/controller/shipmentReleases/Main.controller.ts`

**Interfaces:**
- Produces: rota `shipmentReleasesDetail` com parâmetro `{id}`; controller `siagrob1.controller.shipmentReleases.Detail` com `setBusy`/`bindElement` herdados de `CommonController`.
- Consumes: `BaseController.navTo(name, params)` e `CommonController.bindElement(path)` já existentes.

- [ ] **Step 1: Acrescentar a rota no manifest**

Em `webapp/manifest.json`, no array `routes`, logo após a rota `shipmentReleases` (linhas 360-364):

```json
        {
          "pattern": "shipment-releases/{id}/detail",
          "name": "shipmentReleasesDetail",
          "target": "shipmentReleasesDetail"
        },
```

E no objeto `targets`, logo após o target `shipmentReleases` (linhas 1052-1057):

```json
        "shipmentReleasesDetail": {
					"id": "shipmentReleasesDetail",
          "level": 2,
					"name": "siagrob1.view.shipmentReleases.Detail",
          "clearControlAggregation": true
				},
```

Atenção ao formato: este repo usa `id` / `level` / `name` (caminho completo da view), **não** `viewId` / `viewLevel` / `viewName`. E `target` é string, não array.

- [ ] **Step 2: Criar o controller**

`webapp/controller/shipmentReleases/Detail.controller.ts`:

```ts
import { Route$MatchedEvent } from "sap/ui/core/routing/Route";
import JSONModel from "sap/ui/model/json/JSONModel";
import { BaseController } from "./BaseController";
import formatter from "siagrob1/model/formatter";

/**
 * @namespace siagrob1.controller.shipmentReleases
 */
export default class Detail extends BaseController {

  formatter = formatter;

  onInit(): void {
    this.getRouter().getRoute("shipmentReleasesDetail")
      .attachPatternMatched((ev) => this.detailRouteMatched(ev));
  }

  private detailRouteMatched(ev: Route$MatchedEvent) {
    const { id } = ev.getParameter("arguments") as { id: string };
    if (id == null) return;

    (this.getModel("ui") as JSONModel).setProperty("/editable", false);
    this.bindElement(`/ShipmentReleases(${id})`);
  }

  onNavBack(): void {
    this.navTo("shipmentReleases");
  }
}
```

`Route$MatchedEvent` é **import nomeado** (`{ ... }`) de `sap/ui/core/routing/Route`, não default — igual a `webapp/controller/storageInvoices/Detail.controller.ts:1`.

- [ ] **Step 3: Criar a casca da view**

`webapp/view/shipmentReleases/Detail.view.xml`:

```xml
<mvc:View
	controllerName="siagrob1.controller.shipmentReleases.Detail"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:uxap="sap.uxap"
	core:require="{
		formatter: 'siagrob1/model/formatter'
	}">
	<uxap:ObjectPageLayout
		busy="{ui>/busy}"
		busyIndicatorDelay="0"
		showTitleInHeaderContent="true"
		toggleHeaderOnTitleClick="true"
		preserveHeaderStateOnScroll="false">
		<uxap:headerTitle>
			<uxap:ObjectPageDynamicHeaderTitle>
				<uxap:expandedHeading>
					<Title text="Liberação de Entrega"/>
				</uxap:expandedHeading>
				<uxap:snappedHeading>
					<Title text="Liberação de Entrega"/>
				</uxap:snappedHeading>
				<uxap:actions>
					<Button text="Voltar" type="Transparent" press=".onNavBack"/>
				</uxap:actions>
			</uxap:ObjectPageDynamicHeaderTitle>
		</uxap:headerTitle>
		<uxap:headerContent>
			<HBox>
				<ObjectIdentifier title="{PurchaseContract/Code}" text="{PurchaseContract/CardName}"/>
				<ObjectStatus
					class="sapUiMediumMarginBegin"
					inverted="true"
					state="{
						path: 'Status',
						targetType: 'any',
						formatter: '.formatter.stateShipmentReleaseStatus'
					}"
					text="{
						path: 'Status',
						targetType: 'any',
						formatter: '.formatter.formatShipmentReleaseStatus'
					}"/>
				<ObjectNumber
					class="sapUiMediumMarginBegin"
					number="{
						path: 'AvailableQuantity',
						type: 'sap.ui.model.type.Float',
						formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
					}"
					unit="{PurchaseContract/UnitOfMeasureCode}"/>
			</HBox>
		</uxap:headerContent>
		<uxap:sections>
		</uxap:sections>
	</uxap:ObjectPageLayout>
</mvc:View>
```

- [ ] **Step 4: Acrescentar o botão "Visualizar" na lista**

Em `webapp/view/shipmentReleases/Main.view.xml`, no `OverflowToolbar` do `t:extension`, antes do botão "Ativar":

```xml
						<Button type="Transparent" text="Visualizar" press=".onDetail"/>
```

- [ ] **Step 5: Acrescentar o handler na lista**

Em `webapp/controller/shipmentReleases/Main.controller.ts`, junto dos demais handlers:

```ts
  onDetail(): void {
    const oTable = this.byId("tableShipmentReleases") as Table;
    const i = oTable.getSelectedIndex();

    if (i < 0) {
      MessageBox.warning("Selecione um registro.");
      return;
    }

    const oContext = oTable.getContextByIndex(i);
    if (oContext) {
      this.navTo("shipmentReleasesDetail", { id: oContext.getProperty("Key") as string });
    }
  }
```

- [ ] **Step 6: Verificar**

Run: `npx tsc --noEmit -p tsconfig.json`
Expected: nenhum erro nos arquivos tocados. **Nota:** o `tsconfig.json` do repo tem `"ignoreDeprecations": "6.0"`, inválido para o TS instalado, e falha com `TS5103` antes de checar qualquer arquivo. Para checar de verdade, copiar o tsconfig trocando por `"5.0"` e rodar contra a cópia, descartando-a depois. Erros pré-existentes em `webapp/types/ContractType.ts` e `webapp/test/unit/controller/Main.qunit.ts` não são regressão.

Run: `npx ui5lint webapp/view/shipmentReleases/Detail.view.xml webapp/controller/shipmentReleases/Detail.controller.ts`
Expected: nenhum achado novo.

Manual: `yarn start`, abrir a lista, selecionar uma liberação, clicar "Visualizar". A URL vira `#/shipment-releases/<guid>/detail` e o cabeçalho mostra contrato, fornecedor, status e saldo.

---

### Task B3: Seções Dados e Auditoria

**Files:**
- Create: `webapp/view/shipmentReleases/fragments/Form.fragment.xml`
- Modify: `webapp/view/shipmentReleases/Detail.view.xml` (aggregation `uxap:sections`)

**Interfaces:**
- Consumes: `ConsumedQuantity` exposto na Task B1; `ui>/editable` posto em `false` pelo controller da Task B2.

- [ ] **Step 1: Criar o fragmento**

`webapp/view/shipmentReleases/fragments/Form.fragment.xml`:

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form">
	<f:SimpleForm
		editable="true"
		layout="ResponsiveGridLayout"
		columnsXL="2" columnsL="2" columnsM="1"
		labelSpanXL="4" labelSpanL="4" labelSpanM="4">
		<f:content>
			<Label text="Contrato"/>
			<Input editable="false" value="{PurchaseContract/Code}"/>

			<Label text="Fornecedor"/>
			<Input editable="false" value="{PurchaseContract/CardName}"/>

			<Label text="Produto"/>
			<Input editable="false" value="({PurchaseContract/ItemCode}) {PurchaseContract/ItemName}"/>

			<Label text="Armazém"/>
			<Input editable="false" value="({DeliveryLocationCode}) {DeliveryLocationName}"/>

			<Label text="Dt.Liberação"/>
			<Input editable="false" value="{
				path: 'ReleaseDate',
				targetType: 'any',
				formatter: '.formatter.formatDate'
			}"/>

			<Label text="Filial"/>
			<Input editable="false" value="{Branch/ShortName}"/>

			<Label text="Liberado"/>
			<Input editable="false" value="{
				path: 'ReleasedQuantity',
				type: 'sap.ui.model.type.Float',
				formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
			}"/>

			<Label text="Romaneado"/>
			<Input editable="false" value="{
				path: 'ShippedQuantity',
				type: 'sap.ui.model.type.Float',
				formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
			}"/>

			<Label text="Saldo"/>
			<Input editable="false" value="{
				path: 'AvailableQuantity',
				type: 'sap.ui.model.type.Float',
				formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
			}"/>

			<Label text="Consumido do contrato"/>
			<Input editable="false" value="{
				path: 'ConsumedQuantity',
				type: 'sap.ui.model.type.Float',
				formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
			}"/>
		</f:content>
	</f:SimpleForm>
</core:FragmentDefinition>
```

- [ ] **Step 2: Criar o fragmento de auditoria**

`webapp/view/shipmentReleases/fragments/Audit.fragment.xml`:

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form">
	<f:SimpleForm
		editable="true"
		layout="ResponsiveGridLayout"
		columnsXL="2" columnsL="2" columnsM="1"
		labelSpanXL="4" labelSpanL="4" labelSpanM="4">
		<f:content>
			<Label text="Criado por"/>
			<Input editable="false" value="{CreatedBy}"/>

			<Label text="Criado em"/>
			<Input editable="false" value="{
				path: 'CreatedAt',
				type: 'sap.ui.model.odata.type.DateTimeOffset',
				formatOptions: { pattern: 'dd/MM/yyyy HH:mm' }
			}"/>

			<Label text="Aprovado por"/>
			<Input editable="false" value="{ApprovedBy}"/>

			<Label text="Aprovado em"/>
			<Input editable="false" value="{
				path: 'ApprovedAt',
				type: 'sap.ui.model.odata.type.DateTimeOffset',
				formatOptions: { pattern: 'dd/MM/yyyy HH:mm' }
			}"/>

			<Label text="Cancelado por" visible="{= !!${CanceledAt} }"/>
			<Input editable="false" value="{CanceledBy}" visible="{= !!${CanceledAt} }"/>

			<Label text="Cancelado em" visible="{= !!${CanceledAt} }"/>
			<Input editable="false" visible="{= !!${CanceledAt} }" value="{
				path: 'CanceledAt',
				type: 'sap.ui.model.odata.type.DateTimeOffset',
				formatOptions: { pattern: 'dd/MM/yyyy HH:mm' }
			}"/>

			<Label text="Motivo do cancelamento" visible="{= !!${CanceledAt} }"/>
			<TextArea
				editable="false"
				rows="3"
				width="100%"
				value="{CancellationReason}"
				visible="{= !!${CanceledAt} }"/>
		</f:content>
	</f:SimpleForm>
</core:FragmentDefinition>
```

- [ ] **Step 3: Ligar as seções na view**

Em `Detail.view.xml`, preencher `<uxap:sections>`:

```xml
		<uxap:sections>
			<uxap:ObjectPageSection titleUppercase="false" title="Dados">
				<uxap:subSections>
					<uxap:ObjectPageSubSection>
						<uxap:blocks>
							<core:Fragment fragmentName="siagrob1.view.shipmentReleases.fragments.Form" type="XML"/>
						</uxap:blocks>
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
			<uxap:ObjectPageSection titleUppercase="false" title="Auditoria">
				<uxap:subSections>
					<uxap:ObjectPageSubSection>
						<uxap:blocks>
							<core:Fragment fragmentName="siagrob1.view.shipmentReleases.fragments.Audit" type="XML"/>
						</uxap:blocks>
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
		</uxap:sections>
```

Formato conferido contra `webapp/view/storageInvoices/Detail.view.xml`: `titleUppercase="false"` e `subSections`/`blocks` explícitos.

- [ ] **Step 4: Verificar**

Run: `npx ui5lint webapp/view/shipmentReleases/Detail.view.xml webapp/view/shipmentReleases/fragments/Form.fragment.xml webapp/view/shipmentReleases/fragments/Audit.fragment.xml`
Expected: nenhum achado novo.

Manual: abrir o detalhe de uma liberação **cancelada** — a seção Auditoria mostra quem cancelou, quando e o motivo. Abrir uma **ativa** — os três campos de cancelamento somem, os de criação/aprovação continuam.

---

### Task B4: Seção Romaneios

**Files:**
- Create: `webapp/view/shipmentReleases/fragments/Transactions.fragment.xml`
- Modify: `webapp/view/shipmentReleases/Detail.view.xml` (mais uma seção)
- Modify: `webapp/controller/shipmentReleases/Detail.controller.ts` (filtro)

**Interfaces:**
- Consumes: `detailRouteMatched(ev)` da Task B2, onde o filtro é aplicado após o `bindElement`.

- [ ] **Step 1: Criar o fragmento da tabela**

`webapp/view/shipmentReleases/fragments/Transactions.fragment.xml`:

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:t="sap.ui.table">
	<t:Table
		id="tableReleaseTransactions"
		busyIndicatorDelay="0"
		selectionMode="None"
		class="sapUiSizeCondensed"
		visibleRowCountMode="Auto"
		minAutoRowCount="5"
		alternateRowColors="true"
		rows="{
			path: '/StorageTransactions',
			sorter: [{ path: 'RowId', descending: true }]
		}">
		<t:columns>
			<t:Column label="Código" width="10rem">
				<t:template><Text text="{Code}" wrapping="false"/></t:template>
			</t:Column>
			<t:Column label="Data" width="10rem">
				<t:template>
					<Text wrapping="false" text="{
						path: 'TransactionDate',
						targetType: 'any',
						formatter: '.formatter.formatDate'
					}"/>
				</t:template>
			</t:Column>
			<t:Column label="Tipo" width="12rem">
				<t:template>
					<Text wrapping="false" text="{
						path: 'TransactionType',
						targetType: 'any',
						formatter: '.formatter.formatStorageTransactionType'
					}"/>
				</t:template>
			</t:Column>
			<t:Column label="Armazém" width="10rem">
				<t:template><Text text="{WarehouseCode}" wrapping="false"/></t:template>
			</t:Column>
			<t:Column label="Peso Líquido" hAlign="End" width="10rem">
				<t:template>
					<ObjectNumber
						textAlign="End"
						number="{
							path: 'NetWeight',
							type: 'sap.ui.model.type.Float',
							formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
						}"
						unit="{UnitOfMeasureCode}"/>
				</t:template>
			</t:Column>
			<t:Column label="Status" hAlign="Center" width="8rem">
				<t:template>
					<ObjectStatus
						inverted="true"
						state="{
							path: 'TransactionStatus',
							targetType: 'any',
							formatter: '.formatter.stateStorageTransactionStatus'
						}"
						text="{
							path: 'TransactionStatus',
							targetType: 'any',
							formatter: '.formatter.formatStorageTransactionStatus'
						}"/>
				</t:template>
			</t:Column>
		</t:columns>
	</t:Table>
</core:FragmentDefinition>
```

Os três formatters usados já existem em `webapp/model/formatter.ts`: `formatStorageTransactionType` (linha 204, mapeia `Purchase → "Compra"` e `PurchaseReturn → "Dev.Compra"`), `formatStorageTransactionStatus` (219) e `stateStorageTransactionStatus` (229). Nenhum precisa ser criado.

- [ ] **Step 2: Ligar a seção na view**

Em `Detail.view.xml`, após a seção Auditoria:

```xml
			<uxap:ObjectPageSection title="Romaneios">
				<uxap:subSections>
					<uxap:ObjectPageSubSection>
						<uxap:blocks>
							<core:Fragment fragmentName="siagrob1.view.shipmentReleases.fragments.Transactions" type="XML"/>
						</uxap:blocks>
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
```

- [ ] **Step 3: Aplicar o filtro no controller**

A tabela é bindada em `/StorageTransactions` (coleção inteira) e filtrada por liberação + tipo. Em `Detail.controller.ts`, acrescentar os imports e a chamada no fim de `detailRouteMatched`:

```ts
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import Table from "sap/ui/table/Table";
```

```ts
  private detailRouteMatched(ev: Route$MatchedEvent) {
    const { id } = ev.getParameter("arguments") as { id: string };
    if (id == null) return;

    (this.getModel("ui") as JSONModel).setProperty("/editable", false);
    this.bindElement(`/ShipmentReleases(${id})`);
    this.filterTransactions(id);
  }

  /** Só romaneios de compra: são os que compõem ShippedQuantity. */
  private filterTransactions(releaseKey: string): void {
    const oBinding = (this.byId("tableReleaseTransactions") as Table)
      .getBinding("rows") as ODataListBinding;

    oBinding.filter([
      new Filter("ShipmentReleaseKey", FilterOperator.EQ, releaseKey),
      new Filter({
        filters: [
          new Filter("TransactionType", FilterOperator.EQ, "Purchase"),
          new Filter("TransactionType", FilterOperator.EQ, "PurchaseReturn"),
        ],
        and: false,
      }),
    ]);
  }
```

- [ ] **Step 4: Verificar**

Run: `npx ui5lint webapp/view/shipmentReleases/fragments/Transactions.fragment.xml webapp/controller/shipmentReleases/Detail.controller.ts`
Expected: nenhum achado novo.

Run: typecheck conforme a nota da Task B2 Step 6.
Expected: nenhum erro novo.

Manual, com a rede aberta no DevTools: abrir o detalhe de uma liberação com romaneio de compra. A requisição a `/odata/StorageTransactions` deve conter
`$filter=ShipmentReleaseKey eq <guid> and (TransactionType eq 'Purchase' or TransactionType eq 'PurchaseReturn')`.
Conferir a reconciliação: soma da coluna Peso Líquido (com `PurchaseReturn` subtraindo) == campo **Romaneado**, e **Saldo** == Liberado − Romaneado.

---

## Verificação final (roteiro manual ponta a ponta)

1. Contrato aprovado de 1.000; criar e aprovar liberação de 1.000 no armazém A.
2. Romanear 300 (`Purchase` confirmado). Na lista, coluna Saldo mostra **700** — antes da Parte A mostrava 1.000.
3. Abrir o detalhe: Liberado 1.000 / Romaneado 300 / Saldo 700 / Consumido 1.000 (ainda ativa), e a tabela lista o romaneio de 300.
4. Cancelar pela lista com motivo "troca de armazém". Contrato volta a ter **700** disponíveis para liberar.
5. Reabrir o detalhe: Consumido do contrato = **300**, e a seção Auditoria mostra quem cancelou, quando e o motivo.
6. Criar liberação de 700 no armazém B e aprovar — a validação de `ShipmentReleasesApprovationService` aceita.
7. Tentar romanear (`Purchase`) contra a liberação cancelada → bloqueado pelo guard.
8. Liberação com saldo zerado → botão "Cancelar" desabilitado na lista; forçar pela API devolve "Utilize a ação Finalizar".
