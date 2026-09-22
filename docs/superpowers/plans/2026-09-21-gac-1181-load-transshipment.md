# GAC-1181 — Transbordo na Carga — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** trazer o transbordo (descarregar e recarregar num armazém intermediário) para dentro de uma única carga, inclusive quando ele nasce de uma recusa.

**Architecture:** o transbordo vira uma tabela filha da carga (`SHIPMENT_LOAD_TRANSSHIPMENTS`, 0..N). A entrada no armazém de terceiro é um romaneio novo (`TransshipmentReceipt = 15`) que emite liberações de origem `Transshipment` — que não consomem o contrato de compra e são a porta de saída do grão. O papel de cada romaneio na carga é explícito (`StorageTransaction.ShipmentLoadTransshipmentKey`), e o saldo da carga ganha um quarto termo.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData v4 (Microsoft.AspNetCore.OData), xUnit + EF InMemory, OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-09-21-gac-1181-load-transshipment-design.md`

**Repos:**
- Backend: worktree `C:\Projetos\SiagroB1\siagro-b1-backend-gac-1181`, branch `feature/gac-1181-load-transshipment`.
- Frontend: `C:\Projetos\SiagroB1\siagro-b1-frontend`, branch `feature/gac-1181-load-transshipment`.

## Global Constraints

- **Nomes em inglês, texto de tela em pt-BR.** Classes, entidades, tabelas e colunas em inglês; rótulos, mensagens de negócio e comentários em pt-BR.
- **Valor de enum persistido entra SEMPRE no fim da numeração.** `StorageTransactionType.TransshipmentReceipt = 15`, `ReleaseOrigin.Transshipment = 3`, `ShipmentLoadStatus.InTransshipment = 7`, `RefusalDestination.Transshipment = 2`, `ShipmentLoadMovementType.TransshipmentStarted = 19 / TransshipmentEntered = 20 / TransshipmentReversed = 21`. Mudança de enum **não gera migration** e nada avisa.
- **FK de tabela filha é `NoAction`** e o serviço de delete do pai remove os filhos à mão.
- **Parâmetro de action OData:** quantidade é `Edm.Double` (nunca `Edm.Decimal` — o cliente serializa decimal como string e o 400 não nomeia o campo); data é `string` no formato `yyyy-MM-dd` lida com `TryParseExact`; enum viaja como `string`; tudo que pode faltar precisa de `.Optional()`, senão o `ODataParameterReader` recusa o payload inteiro.
- **Rota de navigation property só responde declarada à mão**, nas duas formas (`odata/ShipmentLoads({key:guid})/X` e `odata/ShipmentLoads/{key:guid}/X`).
- **Todo serviço novo é registrado à mão** em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (`AddApplicationServices`).
- **`UnitOfWork.CommitAsync` não é aninhável**: serviço interno chamado de dentro de transação alheia vai em `CommitMode.Deferred`.
- **Recalcular depois do `SaveChangesAsync`** que gravou as FKs — os recálculos consultam o banco e não enxergam o change tracker.
- **Commits:** `tipo(escopo): descrição em pt-BR`, escopo `shipment`, `Refs: GAC-1181`, e `DB: <NomeDaMigration>` obrigatório no commit que contém migration. Nunca `git push`.
- **Comandos** rodam do worktree do backend; testes: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj`.

---

### Task 1: Modelo — enums, entidade filha, colunas e migration

**Files:**
- Create: `SiagroB1.Domain/Enums/TransshipmentOrigin.cs`
- Create: `SiagroB1.Domain/Entities/ShipmentLoadTransshipment.cs`
- Modify: `SiagroB1.Domain/Enums/StorageTransactionType.cs` (acrescentar `TransshipmentReceipt = 15`)
- Modify: `SiagroB1.Domain/Enums/ReleaseOrigin.cs` (acrescentar `Transshipment = 3`)
- Modify: `SiagroB1.Domain/Enums/ShipmentLoadStatus.cs` (acrescentar `InTransshipment = 7`)
- Modify: `SiagroB1.Domain/Enums/ShipmentLoadMovementType.cs` (acrescentar 19, 20, 21)
- Modify: `SiagroB1.Domain/Entities/ShipmentLoad.cs` (`TransshippedQuantity`, coleção `Transshipments`, `CalculateAvailableQuantity`)
- Modify: `SiagroB1.Domain/Entities/StorageTransaction.cs` (`ShipmentLoadTransshipmentKey`)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSet + fluent)
- Create: migration `AddShipmentLoadTransshipments`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadTransshipmentModelTests.cs`

**Interfaces:**
- Produces: `ShipmentLoadTransshipment` (`Key`, `ShipmentLoadKey`, `Sequence`, `Origin`, `WarehouseCode`, `WarehouseName`, `TransshipmentDate`, `OutgoingQuantity`, `EntryQuantity`, `EntryStorageTransactionKey`, `Comments`, `ShrinkageQuantity`); `ShipmentLoad.TransshippedQuantity`; `ShipmentLoad.CalculateAvailableQuantity(decimal total, decimal invoiced, decimal returned, decimal transshipped)`; `StorageTransaction.ShipmentLoadTransshipmentKey`.

- [ ] **Step 1: Escrever o teste de modelo que falha**

Criar `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadTransshipmentModelTests.cs`, no molde de `ShipmentLoadDischargeModelTests.cs` (contexto só para materializar o modelo, sem abrir conexão):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ShipmentLoads;

public class ShipmentLoadTransshipmentModelTests
{
    private static AppDbContext ModelOnlyContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void Transshipment_MapsToItsOwnTable()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ShipmentLoadTransshipment))!;
        Assert.Equal("SHIPMENT_LOAD_TRANSSHIPMENTS", entity.GetTableName());
    }

    [Fact]
    public void AllForeignKeys_AreNoAction()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ShipmentLoadTransshipment))!;
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
    }

    [Fact]
    public void StorageTransaction_HasTransshipmentKey()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(StorageTransaction))!;
        Assert.NotNull(entity.FindProperty(nameof(StorageTransaction.ShipmentLoadTransshipmentKey)));
    }

    [Fact]
    public void ShipmentLoad_HasTransshippedQuantity()
    {
        using var context = ModelOnlyContext();
        var entity = context.Model.FindEntityType(typeof(ShipmentLoad))!;
        Assert.NotNull(entity.FindProperty(nameof(ShipmentLoad.TransshippedQuantity)));
    }

    [Theory]
    // total, invoiced, returned, transshipped, esperado
    [InlineData(30, 0, 0, 0, 30)]
    [InlineData(30, 30, 0, 0, 0)]
    [InlineData(30, 0, 0, 30, 0)]
    [InlineData(59.5, 29.5, 0, 30, 0)]
    public void CalculateAvailableQuantity_SubtractsTheFourTerms(
        decimal total, decimal invoiced, decimal returned, decimal transshipped, decimal expected)
    {
        Assert.Equal(expected, ShipmentLoad.CalculateAvailableQuantity(total, invoiced, returned, transshipped));
    }
}
```

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~ShipmentLoadTransshipmentModelTests`
Expected: erro de compilação — `ShipmentLoadTransshipment` não existe.

- [ ] **Step 3: Acrescentar os valores de enum**

Em `StorageTransactionType.cs`, **no fim**:

```csharp
    TransshipmentReceipt = 15,    // Transbordo - Entrada em armazém intermediário (GAC-1181)
```

Em `ReleaseOrigin.cs`, **no fim**, com o XML-doc:

```csharp
    /// <summary>
    /// Emitida pela ENTRADA de um transbordo (GAC-1181): o grão foi descarregado num armazém
    /// intermediário e esta liberação é a porta de saída dele. O físico já está no armazém,
    /// creditado pelo romaneio <see cref="StorageTransactionType.TransshipmentReceipt"/>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Não consome o contrato de compra.</b> O volume já foi debitado na saída da origem;
    /// contá-lo de novo duplicaria o liberado — mesma razão da <see cref="SalesReturn"/>.
    /// </remarks>
    Transshipment = 3,
```

Em `ShipmentLoadStatus.cs`, **no fim**:

```csharp
    InTransshipment = 7     // Descarregada em armazém intermediário, aguardando a saída (GAC-1181)
```

Em `ShipmentLoadMovementType.cs`, **no fim**:

```csharp
    TransshipmentStarted = 19,   // Transbordo iniciado — o saldo sai da carga para o armazém
    TransshipmentEntered = 20,   // Entrada no transbordo registrada — romaneio e liberações criados
    TransshipmentReversed = 21   // Transbordo estornado — o saldo volta para a carga
```

- [ ] **Step 4: Criar `TransshipmentOrigin`**

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>
/// De onde o transbordo nasceu (GAC-1181). Não muda o que o transbordo FAZ — muda apenas de que
/// passo do fluxo ele veio, e é isso que a tela mostra ao operador.
/// </summary>
public enum TransshipmentOrigin
{
    /// <summary>Planejado: a carga já sai da origem sabendo que vai ser mexida no meio.</summary>
    Planned = 0,

    /// <summary>
    /// Nasceu de uma recusa com destino Transbordo: a carga já foi faturada e devolvida, e a
    /// mercadoria vai ser padronizada antes de seguir para o mesmo cliente ou outro.
    /// </summary>
    Refusal = 1,
}
```

- [ ] **Step 5: Criar a entidade**

`SiagroB1.Domain/Entities/ShipmentLoadTransshipment.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Um transbordo da carga (GAC-1181): a mercadoria é descarregada num armazém intermediário e
/// recarregada dali para o cliente, sem que a viagem vire duas cargas.
/// </summary>
/// <remarks>
/// <b><see cref="OutgoingQuantity"/> é o saldo disponível INTEIRO da carga no momento</b>, porque
/// tudo é descarregado — não existe descarregar parte e seguir com o resto no caminhão. É ele o
/// quarto termo do saldo da carga.
/// <para>
/// <see cref="EntryQuantity"/> é o peso PESADO na entrada e pode ser menor: a diferença
/// (<see cref="ShrinkageQuantity"/>) é quebra de transporte, meramente informativa — não volta ao
/// contrato e ninguém a fatura.
/// </para>
/// <para>
/// Não herda <c>BaseEntity</c>, como <see cref="ShipmentLoadDischarge"/>: é registro filho de
/// documento, não documento.
/// </para>
/// </remarks>
[Table("SHIPMENT_LOAD_TRANSSHIPMENTS")]
[Index(nameof(ShipmentLoadKey))]
[Index(nameof(EntryStorageTransactionKey))]
public class ShipmentLoadTransshipment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    /// <summary>1, 2, 3… dentro da carga. É por ele que se acha o ÚLTIMO transbordo.</summary>
    public int Sequence { get; set; }

    public TransshipmentOrigin Origin { get; set; } = TransshipmentOrigin.Planned;

    [Column(TypeName = "VARCHAR(10)")]
    public required string WarehouseCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? WarehouseName { get; set; }

    public DateTime TransshipmentDate { get; set; } = DateTime.Now.Date;

    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal OutgoingQuantity { get; set; }

    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal EntryQuantity { get; set; }

    /// <summary>
    /// Romaneio de entrada: o <see cref="StorageTransactionType.TransshipmentReceipt"/> em armazém
    /// de terceiro, ou o <see cref="StorageTransactionType.Receipt"/> da Entrada em Armazenagem em
    /// armazém próprio. Nulo enquanto a entrada não foi registrada.
    /// </summary>
    public Guid? EntryStorageTransactionKey { get; set; }
    public virtual StorageTransaction? EntryStorageTransaction { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }

    /// <summary>Quebra de transporte: o que saiu da carga menos o que entrou no armazém.</summary>
    [NotMapped]
    public decimal ShrinkageQuantity => EntryQuantity <= decimal.Zero
        ? decimal.Zero
        : decimal.Round(OutgoingQuantity - EntryQuantity, 3, MidpointRounding.ToEven);
}
```

- [ ] **Step 6: Acrescentar as colunas em `ShipmentLoad` e `StorageTransaction`**

Em `ShipmentLoad.cs`, junto de `ReturnedToWarehouseQuantity`:

```csharp
    /// <summary>
    /// Persistido-derivado: Σ <c>OutgoingQuantity</c> dos transbordos da carga (GAC-1181) — o
    /// quarto termo do saldo. Escritor único: <c>ShipmentLoadsRecalculateInvoicedService</c>,
    /// pelo mesmo motivo do terceiro termo (o status depende dele).
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal TransshippedQuantity { get; set; }
```

e a coleção, junto de `Discharges`:

```csharp
    public virtual ICollection<ShipmentLoadTransshipment> Transshipments { get; } = [];
```

`AvailableQuantity` e `CalculateAvailableQuantity` passam a considerar o quarto termo. **A assinatura muda de propósito**, para o compilador apontar todos os chamadores:

```csharp
    [NotMapped]
    public decimal AvailableQuantity => Status == ShipmentLoadStatus.Cancelled
        ? decimal.Zero
        : CalculateAvailableQuantity(
            TotalQuantity, InvoicedQuantity, ReturnedToWarehouseQuantity, TransshippedQuantity);

    public static decimal CalculateAvailableQuantity(
        decimal totalQuantity,
        decimal invoicedQuantity,
        decimal returnedToWarehouseQuantity,
        decimal transshippedQuantity) =>
        decimal.Round(
            totalQuantity - invoicedQuantity - returnedToWarehouseQuantity - transshippedQuantity,
            3, MidpointRounding.ToEven);
```

Em `StorageTransaction.cs`, junto de `RefusedFromShipmentLoadKey`:

```csharp
    /// <summary>
    /// Transbordo (GAC-1181) a que este romaneio pertence: a ENTRADA no armazém intermediário ou
    /// uma SAÍDA dele para o cliente.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>O romaneio de ENTRADA não carrega <see cref="ShipmentLoadKey"/>.</b> O cancelamento da
    /// carga zera aquela chave em todos os romaneios dela, e a entrada ficaria órfã. A carga chega
    /// até ele por esta coluna. As SAÍDAS carregam as duas: elas são volume faturável da carga.
    /// </remarks>
    public Guid? ShipmentLoadTransshipmentKey { get; set; }
    public virtual ShipmentLoadTransshipment? ShipmentLoadTransshipment { get; set; }
```

- [ ] **Step 7: Configurar no `AppDbContext`**

DbSet junto dos demais da carga (`AppDbContext.cs:69-74`):

```csharp
    public DbSet<ShipmentLoadTransshipment> ShipmentLoadsTransshipments { get; set; }
```

Fluent, junto do bloco das descargas (`AppDbContext.cs:260-291`):

```csharp
        // GAC-1181: as duas FKs do transbordo são NoAction — ShipmentLoadsDeleteService remove os
        // filhos à mão, e o romaneio de entrada não pode arrastar o transbordo num delete.
        modelBuilder.Entity<ShipmentLoadTransshipment>()
            .HasOne(x => x.ShipmentLoad)
            .WithMany(x => x.Transshipments)
            .HasForeignKey(x => x.ShipmentLoadKey)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ShipmentLoadTransshipment>()
            .HasOne(x => x.EntryStorageTransaction)
            .WithMany()
            .HasForeignKey(x => x.EntryStorageTransactionKey)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<StorageTransaction>()
            .HasOne(x => x.ShipmentLoadTransshipment)
            .WithMany()
            .HasForeignKey(x => x.ShipmentLoadTransshipmentKey)
            .OnDelete(DeleteBehavior.NoAction);
```

- [ ] **Step 8: Corrigir os chamadores que o compilador apontar**

`dotnet build` vai apontar os usos de `CalculateAvailableQuantity` com 3 argumentos (pelo menos `ShipmentLoadsBillingGuardService`). Nesta task, passe `decimal.Zero` como quarto argumento **apenas** onde o valor ainda não existe; a Task 3 substitui pelo termo recalculado. Registre isso com um `// TODO GAC-1181 (Task 3)` — é a única exceção à regra de não deixar TODO, e ela sai nesta mesma feature.

- [ ] **Step 9: Rodar os testes de modelo**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~ShipmentLoadTransshipmentModelTests`
Expected: PASS (5 testes).

- [ ] **Step 10: Gerar a migration**

```bash
dotnet ef migrations add AddShipmentLoadTransshipments \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```

Conferir no arquivo gerado: `CreateTable SHIPMENT_LOAD_TRANSSHIPMENTS` sem `onDelete`, `AddColumn ShipmentLoadTransshipmentKey` em `STORAGE_TRANSACTIONS`, `AddColumn TransshippedQuantity` em `SHIPMENT_LOADS` com `defaultValue: 0m`, e os índices. **Não aplicar em banco nenhum ainda** — a aplicação é combinada com o usuário no fim.

- [ ] **Step 11: Rodar a suíte inteira**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj`
Expected: PASS, com a mesma contagem de antes + 5.

- [ ] **Step 12: Commit**

```bash
git add SiagroB1.Domain SiagroB1.Infra SiagroB1.Migrations SiagroB1.Application.Tests SiagroB1.Application
git commit -F - <<'EOF'
feat(shipment): criar o modelo do transbordo da carga

O transbordo passa a ser registro filho da carga, com o volume que saiu, o
peso que entrou no armazem intermediario e o romaneio de entrada. O saldo da
carga ganha o quarto termo e o romaneio ganha o vinculo de papel.

Atencao: CalculateAvailableQuantity mudou de assinatura de proposito, para o
compilador apontar todos os chamadores do saldo.

Refs: GAC-1181
DB: AddShipmentLoadTransshipments

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 2: O romaneio 15 no confirm e no saldo de armazém

**Files:**
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsConfirmedService.cs`
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsWarehouseBalanceService.cs:51-73`
- Modify: `SiagroB1.Domain/Enums/ReleaseOriginRules.cs`
- Test: `SiagroB1.Application.Tests/StorageTransactions/TransshipmentReceiptBalanceTests.cs`

**Interfaces:**
- Consumes: `StorageTransactionType.TransshipmentReceipt`, `ReleaseOrigin.Transshipment` (Task 1).
- Produces: confirmação do 15 (`Confirmed`, `NetWeight = GrossWeight`); o 15 creditando o saldo do armazém; `ReleaseOriginRules.ConsumesPurchaseContract(Transshipment) == false`.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Tests.StorageTransactions;

public class TransshipmentReceiptBalanceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private static StorageTransaction Entry(decimal grossWeight, StorageTransactionsStatus status) => new()
    {
        TransactionType = StorageTransactionType.TransshipmentReceipt,
        TransactionStatus = status,
        TransactionDate = DateTime.Today,
        BranchCode = "01",
        CardCode = "F001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        WarehouseCode = "ARM99",
        GrossWeight = grossWeight,
        NetWeight = grossWeight,
    };

    [Fact]
    public async Task ConfirmedTransshipmentReceipt_CreditsTheWarehouse()
    {
        _db.Context.StorageTransactions.Add(Entry(29_800, StorageTransactionsStatus.Confirmed));
        await _db.SaveChangesAsync();

        var balance = await new StorageTransactionsWarehouseBalanceService(_db.Context)
            .CalculateAsync("ARM99", "SOJA");

        Assert.Equal(29_800m, balance);
    }

    [Fact]
    public async Task CancelledTransshipmentReceipt_DoesNotCredit()
    {
        _db.Context.StorageTransactions.Add(Entry(29_800, StorageTransactionsStatus.Cancelled));
        await _db.SaveChangesAsync();

        var balance = await new StorageTransactionsWarehouseBalanceService(_db.Context)
            .CalculateAsync("ARM99", "SOJA");

        Assert.Equal(decimal.Zero, balance);
    }

    [Fact]
    public void TransshipmentRelease_DoesNotConsumeThePurchaseContract()
    {
        Assert.False(ReleaseOriginRules.ConsumesPurchaseContract(ReleaseOrigin.Transshipment));
        Assert.True(ReleaseOriginRules.ShipsWithoutPurchaseLeg(ReleaseOrigin.Transshipment));
        Assert.False(ReleaseOriginRules.RequiresStorageAddress(ReleaseOrigin.Transshipment));
    }
}
```

(Se a assinatura real de `CalculateAsync`/do construtor divergir, ajuste o teste ao serviço — não o contrário.)

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~TransshipmentReceiptBalanceTests`
Expected: FAIL — saldo 0 no primeiro teste e `ConsumesPurchaseContract` devolvendo `true`.

- [ ] **Step 3: `ReleaseOriginRules.ConsumesPurchaseContract`**

```csharp
    /// <summary>
    /// A liberação desconta volume do contrato de compra. Falso em
    /// <see cref="ReleaseOrigin.SalesReturn"/> e <see cref="ReleaseOrigin.Transshipment"/>: nas
    /// duas, aquele volume já foi debitado do contrato quando a mercadoria saiu pela primeira vez,
    /// e contá-lo de novo duplicaria o liberado.
    /// </summary>
    public static bool ConsumesPurchaseContract(ReleaseOrigin origin) =>
        origin is not (ReleaseOrigin.SalesReturn or ReleaseOrigin.Transshipment);
```

- [ ] **Step 4: Creditar o 15 no saldo de armazém**

Em `StorageTransactionsWarehouseBalanceService`, acrescentar o tipo ao filtro e ao sinal positivo:

```csharp
                 x.TransactionType == StorageTransactionType.SalesShipmentReturn ||
                 x.TransactionType == StorageTransactionType.TransshipmentReceipt ||   // GAC-1181
```

```csharp
            // GAC-1181: a entrada do transbordo credita o armazém intermediário, como a devolução.
            // Ela NÃO entra em nenhum saldo por LOTE — é entrada em nível de armazém.
            var isCredit = x.TransactionType is StorageTransactionType.Purchase
                or StorageTransactionType.SalesShipmentReturn
                or StorageTransactionType.TransshipmentReceipt
                or StorageTransactionType.WarehouseGain;
```

- [ ] **Step 5: Ramo de confirmação do 15**

Em `StorageTransactionsConfirmedService`, no `switch` (linhas 55-79):

```csharp
            case StorageTransactionType.TransshipmentReceipt:
                await ExecuteTransshipmentReceiptTransactionAsync(st, userName, commitMode);
                break;
```

e o método, no molde de `ExecuteSalesShipmentReturnTransactionAsync`:

```csharp
    /// <summary>
    /// Entrada do transbordo (GAC-1181): crédito em nível de ARMAZÉM, sem lote e sem descontos.
    /// </summary>
    /// <remarks>
    /// <c>NetWeight = GrossWeight</c> porque não há tabela de custos no transbordo: a quebra do
    /// trajeto já está registrada no próprio transbordo (saída menos entrada), e descontar de novo
    /// aqui a contaria duas vezes.
    /// </remarks>
    private async Task ExecuteTransshipmentReceiptTransactionAsync(
        StorageTransaction st, string userName, CommitMode commitMode)
    {
        if (st.TransactionStatus != StorageTransactionsStatus.Pending)
            throw new ApplicationException($"O romaneio {st.Code} não está pendente.");

        st.TransactionStatus = StorageTransactionsStatus.Confirmed;
        st.NetWeight = st.GrossWeight;
        st.AvaiableVolumeToAllocate = decimal.Zero;
        st.UpdatedAt = DateTime.Now;
        st.UpdatedBy = userName;

        if (commitMode == CommitMode.Auto)
            await unitOfWork.SaveChangesAsync();
    }
```

- [ ] **Step 6: Rodar os testes**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter FullyQualifiedName~TransshipmentReceiptBalanceTests`
Expected: PASS (3 testes).

- [ ] **Step 7: Rodar a suíte inteira** — nenhum teste de saldo de lote pode mudar de resultado.

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add SiagroB1.Application SiagroB1.Domain SiagroB1.Application.Tests
git commit -m "feat(storage): creditar a entrada de transbordo no saldo do armazem

O romaneio 15 confirma em nivel de armazem, sem lote e sem descontos, e a
liberacao de transbordo nao consome o contrato de compra.

Refs: GAC-1181

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: O quarto termo do saldo e a situação "Em transbordo"

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRecalculateTransshippedService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRecalculateInvoicedService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsBillingGuardService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadTransshipmentBalanceTests.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadResolveStatusTests.cs` (acrescentar casos)

**Interfaces:**
- Produces: `ShipmentLoadsRecalculateTransshippedService.CalculateTransshippedAsync(AppDbContext, Guid)` e `HasOpenTransshipmentAsync(AppDbContext, Guid)`; `ShipmentLoadsRecalculateInvoicedService.ResolveStatus(decimal total, decimal invoiced, decimal returned, decimal transshipped, bool hasOpenTransshipment)`.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
public class ShipmentLoadTransshipmentBalanceTests
{
    [Theory]
    // total, invoiced, returned, transshipped, hasOpen, esperado
    [InlineData(0, 0, 0, 0, false, ShipmentLoadStatus.Planned)]
    [InlineData(30, 0, 0, 0, false, ShipmentLoadStatus.Open)]
    [InlineData(30, 30, 0, 0, false, ShipmentLoadStatus.Invoiced)]
    [InlineData(30, 0, 0, 30, true, ShipmentLoadStatus.InTransshipment)]
    [InlineData(59.5, 0, 0, 30, false, ShipmentLoadStatus.PartiallyInvoiced)]
    [InlineData(59.5, 29.5, 0, 30, false, ShipmentLoadStatus.Invoiced)]
    [InlineData(40, 0, 40, 0, false, ShipmentLoadStatus.Returned)]
    public void ResolveStatus_ConsidersTheTransshipment(
        decimal total, decimal invoiced, decimal returned, decimal transshipped,
        bool hasOpen, ShipmentLoadStatus expected)
    {
        Assert.Equal(expected, ShipmentLoadsRecalculateInvoicedService.ResolveStatus(
            total, invoiced, returned, transshipped, hasOpen));
    }
}
```

Mais um teste de integração que percorre o **cenário do usuário** (SP → porto → recusa → parceiro → cliente 2) só na aritmética, montando as linhas direto no InMemory: carga com `TotalQuantity = 30_000`, um transbordo com `OutgoingQuantity = 30_000`, e depois uma Expedição de 29_500 com `ShipmentLoadKey` e `ShipmentLoadTransshipmentKey` preenchidos; assere `TotalQuantity = 59_500` depois do `RecalculateTotal`, `TransshippedQuantity = 30_000` e `AvailableQuantity = 29_500`.

- [ ] **Step 2: Rodar e ver falhar** — `ResolveStatus` ainda tem 3 parâmetros.

- [ ] **Step 3: Criar a fórmula do quarto termo**

```csharp
namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Fórmula de <see cref="ShipmentLoad.TransshippedQuantity"/> (GAC-1181) — o volume que saiu da
/// carga para um armazém intermediário.
/// </summary>
/// <remarks>
/// Só a fórmula, sem <c>SaveChanges</c> e sem estado, como
/// <see cref="ShipmentLoadsRecalculateReturnedService"/>. <b>Quem GRAVA é
/// <see cref="ShipmentLoadsRecalculateInvoicedService"/></b>, o escritor único do status.
/// <para>
/// O estorno do transbordo APAGA a linha, então não há status a filtrar aqui: linha existente é
/// transbordo vivo.
/// </para>
/// </remarks>
public static class ShipmentLoadsRecalculateTransshippedService
{
    public static async Task<decimal> CalculateTransshippedAsync(AppDbContext context, Guid shipmentLoadKey)
    {
        var total = await context.ShipmentLoadsTransshipments
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .SumAsync(x => (decimal?)x.OutgoingQuantity) ?? decimal.Zero;

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }

    /// <summary>
    /// O ÚLTIMO transbordo ainda não tem saída vinculada — a mercadoria está no armazém
    /// intermediário e a viagem não acabou.
    /// </summary>
    public static async Task<bool> HasOpenTransshipmentAsync(AppDbContext context, Guid shipmentLoadKey)
    {
        var last = await context.ShipmentLoadsTransshipments
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .OrderByDescending(x => x.Sequence)
            .FirstOrDefaultAsync();

        if (last?.Key is not { } transshipmentKey)
            return false;

        return !await context.StorageTransactions.AnyAsync(x =>
            x.ShipmentLoadTransshipmentKey == transshipmentKey &&
            x.TransactionType == StorageTransactionType.SalesShipment &&
            x.TransactionStatus != StorageTransactionsStatus.Cancelled);
    }
}
```

- [ ] **Step 4: Gravar o termo e resolver o status**

Em `ShipmentLoadsRecalculateInvoicedService.RecalculateAsync`, depois do termo `returned`:

```csharp
        var transshipped = await ShipmentLoadsRecalculateTransshippedService
            .CalculateTransshippedAsync(context, shipmentLoadKey);

        var hasOpenTransshipment = await ShipmentLoadsRecalculateTransshippedService
            .HasOpenTransshipmentAsync(context, shipmentLoadKey);

        load.InvoicedQuantity = invoiced;
        load.ReturnedToWarehouseQuantity = returned;
        load.TransshippedQuantity = transshipped;
        load.Status = ResolveStatus(load.TotalQuantity, invoiced, returned, transshipped, hasOpenTransshipment);
```

e `ResolveStatus`:

```csharp
    /// <remarks>
    /// <b>O ramo do transbordo vem PRIMEIRO</b> (GAC-1181). Depois que o volume sai da carga para o
    /// armazém intermediário o saldo é zero, e sem este ramo a carga leria "Faturada" e sumiria das
    /// telas de pendência com a mercadoria ainda no meio do caminho — a mesma armadilha que o ramo
    /// <c>Planned</c> corrigiu quando `ResolveStatus(0, 0)` devolvia `Open`.
    /// </remarks>
    public static ShipmentLoadStatus ResolveStatus(
        decimal totalQuantity,
        decimal invoicedQuantity,
        decimal returnedToWarehouseQuantity,
        decimal transshippedQuantity,
        bool hasOpenTransshipment)
    {
        if (hasOpenTransshipment)
            return ShipmentLoadStatus.InTransshipment;

        if (totalQuantity <= Tolerance)
            return ShipmentLoadStatus.Planned;

        var consumed = invoicedQuantity + returnedToWarehouseQuantity + transshippedQuantity;

        if (consumed <= decimal.Zero)
            return ShipmentLoadStatus.Open;

        if (consumed < totalQuantity - Tolerance)
            return ShipmentLoadStatus.PartiallyInvoiced;

        return returnedToWarehouseQuantity > Tolerance
            ? ShipmentLoadStatus.Returned
            : ShipmentLoadStatus.Invoiced;
    }
```

⚠️ A projeção de `TransactionStatus` nos romaneios (logo abaixo) precisa **pular** o romaneio de entrada: ele não carrega `ShipmentLoadKey`, então já está fora da consulta — confira e deixe um comentário dizendo por quê.

- [ ] **Step 5: Ajustar o guard de faturamento**

Em `ShipmentLoadsBillingGuardService.EnsureCanBillAsync`, trocar o `// TODO GAC-1181` da Task 1 pelo termo real e acrescentar a recusa por status, **antes** da comparação de saldo:

```csharp
        // GAC-1181: a mercadoria está no armazém intermediário e o saldo é zero. Recusar por
        // STATUS, e não pela comparação de saldo, pelo mesmo motivo do ramo Planned: a mensagem
        // de quantidade mandaria o usuário procurar um problema que não existe.
        if (load.Status == ShipmentLoadStatus.InTransshipment)
            throw new ApplicationException(
                $"A carga {load.Code} está em transbordo. Registre a entrada e vincule a Expedição " +
                "de saída do armazém de transbordo antes de faturá-la.");
```

```csharp
        var transshipped = await ShipmentLoadsRecalculateTransshippedService
            .CalculateTransshippedAsync(context, shipmentLoadKey);

        var available = ShipmentLoad.CalculateAvailableQuantity(
            load.TotalQuantity, invoiced, returned, transshipped);
```

- [ ] **Step 6: Rodar os testes de saldo e status**

Run: `dotnet test SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj --filter "FullyQualifiedName~ShipmentLoadTransshipmentBalanceTests|FullyQualifiedName~ShipmentLoadResolveStatusTests|FullyQualifiedName~ShipmentLoadsBillingGuardServiceTests"`
Expected: PASS.

- [ ] **Step 7: Registrar o serviço novo no DI** — `ServiceCollectionExtensions.AddApplicationServices` (o de recálculo é estático; registre apenas os que forem de instância).

- [ ] **Step 8: Rodar a suíte inteira e commitar**

```bash
git add SiagroB1.Application SiagroB1.Application.Tests SiagroB1.Web
git commit -m "feat(shipment): somar o transbordo no saldo e criar a situacao Em transbordo

O volume que sai para o armazem intermediario vira o quarto termo do saldo, e
a carga fica Em transbordo enquanto o ultimo transbordo nao tem saida.

Atencao: o ramo novo do ResolveStatus vem antes de todos; sem ele o saldo zero
faria a carga ler Faturada com a mercadoria ainda no meio do caminho.

Refs: GAC-1181

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Iniciar o transbordo

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadTransshipmentRules.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsTransshipmentStartService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsTransshipmentStartServiceTests.cs`

**Interfaces:**
- Produces: `ShipmentLoadsTransshipmentStartService.ExecuteAsync(Guid loadKey, string warehouseCode, DateTime transshipmentDate, string? comments, string userName) → ShipmentLoadTransshipment`; `ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment(ShipmentLoad)`, `.EnsureIsLastAsync(...)`.

- [ ] **Step 1: Escrever os testes que falham**

Casos, no molde de `ShipmentLoadsDetachTransactionsServiceTests` (fixture com `TestDb.CreateUnitOfWork()` e helpers de seed):

```csharp
[Fact] public async Task Start_TakesTheWholeAvailableBalance()          // Outgoing == AvailableQuantity da carga
[Fact] public async Task Start_LeavesTheLoadInTransshipment()            // Status == InTransshipment
[Fact] public async Task Start_NumbersTheSequence()                      // 1, depois 2
[Fact] public async Task Start_RefusesWhenThereIsNoBalance()             // disponível 0 → ApplicationException
[Fact] public async Task Start_RefusesCancelledOrReturnedOrRemovalLoad() // 3 casos
[Fact] public async Task Start_RefusesWhenTheLastTransshipmentIsOpen()   // não empilha transbordo sobre transbordo aberto
[Fact] public async Task Start_LogsTheMovement()                         // TransshipmentStarted com delta negativo
```

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Escrever as regras compartilhadas**

```csharp
namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Regras do transbordo (GAC-1181) compartilhadas por iniciar, registrar entrada e estornar.
/// Lançam <see cref="ApplicationException"/> com mensagem de negócio e são chamadas ANTES de abrir
/// transação — o catch dos serviços embrulharia a mensagem.
/// </summary>
public static class ShipmentLoadTransshipmentRules
{
    public const decimal Tolerance = 0.001m;

    public static void EnsureLoadAcceptsTransshipment(ShipmentLoad load)
    {
        if (load.LoadType == ShipmentLoadType.Removal)
            throw new ApplicationException(
                $"A carga {load.Code} é do tipo Remoção e não tem transbordo.");

        if (load.Status is ShipmentLoadStatus.Cancelled or ShipmentLoadStatus.Returned
            or ShipmentLoadStatus.Completed)
            throw new ApplicationException(
                $"A carga {load.Code} está encerrada e não aceita transbordo.");
    }

    public static void EnsureWarehouseInformed(string? warehouseCode)
    {
        if (string.IsNullOrWhiteSpace(warehouseCode))
            throw new ApplicationException("Informe o armazém do transbordo.");
    }
}
```

- [ ] **Step 4: Escrever o serviço**

Comportamento, em ordem: carrega a carga (`NotFoundException`) → `EnsureLoadAcceptsTransshipment` → `EnsureWarehouseInformed` → resolve o armazém por **`IWarehouseService`** (em modo SAPB1 a tabela local está vazia) → recalcula o saldo e exige `available > Tolerance` com mensagem nomeando a carga → exige que não haja transbordo aberto (`HasOpenTransshipmentAsync`) → em transação: grava a linha com `Sequence = último + 1`, `Origin = Planned`, `OutgoingQuantity = available`, `EntryQuantity = 0` → `SaveChangesAsync` → `ShipmentLoadsRecalculateInvoicedService.RecalculateAsync` (que grava o quarto termo e o status) → `movementLog.Register(..., TransshipmentStarted, -available, load.AvailableQuantity, "Transbordo N iniciado no armazém (X) Nome: 30.000,000.", userName)` → `SaveChangesAsync` → commit.

- [ ] **Step 5: Registrar no DI** e rodar os testes da task.

- [ ] **Step 6: Suíte inteira + commit** (`feat(shipment): iniciar o transbordo da carga`).

---

### Task 5: Registrar a entrada do transbordo

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsTransshipmentRegisterEntryService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentReleases/ShipmentReleasesFromReturnService.cs` (parametrizar a origem)
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRefuseService.cs` (chamada existente passa `ReleaseOrigin.SalesReturn`)
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesReturnService.cs` (idem, se chamar)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsTransshipmentRegisterEntryServiceTests.cs`

**Interfaces:**
- Produces: `ShipmentLoadsTransshipmentRegisterEntryService.ExecuteAsync(Guid transshipmentKey, decimal grossWeight, decimal? tareWeight, DateTime entryDate, Guid? receiptStorageTransactionKey, string userName)`.
- Consumes: `ShipmentReleasesFromReturnService.BuildAsync(entry, shares, warehouseCode, warehouseName, userName, ReleaseOrigin origin)`.

**Decisão desta task (ajuste ao spec):** em vez de um `ShipmentReleasesFromTransshipmentService` novo, **parametriza-se a origem** em `ShipmentReleasesFromReturnService`. O rastreio do contrato (cadeia curta pela `ShipmentReleaseKey`, cadeia longa por `SHIPPING_TRANSACTIONS`), o rateio por peso e o tratamento de volume órfão são idênticos, e duplicá-los criaria duas fontes da mesma regra. O parâmetro é **obrigatório**, para o compilador apontar os chamadores existentes.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
[Fact] public async Task RegisterEntry_CreatesTheReceiptWithTheWeighedQuantity()
[Fact] public async Task RegisterEntry_TheReceiptCarriesNeitherReleaseKeyNorLoadKey()   // regressão da armadilha
[Fact] public async Task RegisterEntry_EmitsOneReleasePerPurchaseContract()
[Fact] public async Task RegisterEntry_ReleasesDoNotConsumeThePurchaseContract()        // ConsumedQuantity == 0
[Fact] public async Task RegisterEntry_RecordsTheShrinkage()                            // Outgoing 30.000, Entry 29.800 → 200
[Fact] public async Task RegisterEntry_OwnWarehouse_LinksTheExistingReceipt()           // caso 1
[Fact] public async Task RegisterEntry_RefusesWhenAlreadyRegistered()
[Fact] public async Task RegisterEntry_RefusesQuantityGreaterThanOutgoing()
[Fact] public async Task RegisterEntry_UntraceableVolume_DoesNotFail()                  // vai para o Comments
```

O teste da armadilha é o mais importante:

```csharp
[Fact]
public async Task RegisterEntry_TheReceiptCarriesNeitherReleaseKeyNorLoadKey()
{
    var (load, transshipment) = await SeedStartedTransshipmentAsync(outgoing: 30_000);

    await Service().ExecuteAsync(transshipment.Key!.Value, 29_800, null, DateTime.Today, null, "tester");

    var entry = await _db.Context.StorageTransactions
        .AsNoTracking()
        .SingleAsync(x => x.TransactionType == StorageTransactionType.TransshipmentReceipt);

    Assert.Null(entry.ShipmentReleaseKey);   // o romaneio que ORIGINA a liberação nunca a consome
    Assert.Null(entry.ShipmentLoadKey);      // o cancelamento da carga o deixaria órfão
    Assert.Equal(transshipment.Key, entry.ShipmentLoadTransshipmentKey);
}
```

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Parametrizar a origem em `ShipmentReleasesFromReturnService`**

`BuildAsync` e o `Create` privado ganham `ReleaseOrigin origin`, gravado em `release.Origin`. Atualizar o XML-doc da classe: "Emite as liberações de uma entrada em armazém — devolução (`SalesReturn`) ou transbordo (`Transshipment`)". Os chamadores existentes passam `ReleaseOrigin.SalesReturn`.

- [ ] **Step 4: Escrever o serviço**

Comportamento, em ordem:

1. Carrega o transbordo + a carga; `EnsureLoadAcceptsTransshipment`; recusa se `EntryStorageTransactionKey != null` ("A entrada do transbordo N já foi registrada. Estorne-o para corrigir.").
2. Resolve o armazém por `IWarehouseService` e o complemento por `IWarehouseComplementService`.
3. **Armazém próprio (`IsOwn`)**: exige `receiptStorageTransactionKey`; valida que é `Receipt`, `Confirmed`, do mesmo armazém, produto, filial e unidade da carga, sem `ShipmentLoadKey` e sem `ShipmentLoadTransshipmentKey`; grava `ShipmentLoadTransshipmentKey` nele; `EntryQuantity = GrossWeight` dele; **não cria liberação** (o grão entrou no lote pela Entrada em Armazenagem).
4. **Armazém de terceiro**: exige peso > 0 e `<= OutgoingQuantity + Tolerance`; monta o romaneio 15 com `BranchCode/ItemCode/UnitOfMeasureCode/TruckCode/TruckDriverCode` da carga, `WarehouseCode` do transbordo, `CardCode` do **primeiro romaneio de saída da origem** da carga (a coluna é `NOT NULL`; com clientes/fornecedores distintos, todos ficam listados no `Comments`), `GrossWeight` informado (peso único da entrada; **não existe tara em `StorageTransaction`** — ver a decisão registrada no ledger da Task 5), `Comments` narrando carga e transbordo. Cria com `storageCreate.ExecuteAsync(entry, userName, TransactionCode.ShipmentLoad, CommitMode.Deferred)`, `SaveChangesAsync`, confirma com `storageConfirm.ExecuteAsync(entry, userName, CommitMode.Deferred)`, `SaveChangesAsync`.
5. Emite as liberações: `DistributeByWeight(origemExits, entryQuantity)` → `returnReleases.BuildAsync(entry, shares, warehouse.Code, warehouse.Name, userName, ReleaseOrigin.Transshipment)` → `AddRange` + `Comments` do volume órfão. **Falha aqui não derruba o registro** — o caminhão já descarregou.
6. Grava `EntryQuantity`, `EntryStorageTransactionKey`, `UpdatedAt/By`; `SaveChangesAsync`; `RecalculateInvoicedService.RecalculateAsync`; `movementLog.Register(..., TransshipmentEntered, 0, load.AvailableQuantity, "Entrada do transbordo N registrada no armazém (X): 29.800,000. Quebra: 200,000. Romaneio 00001234.", userName)`; `SaveChangesAsync`; commit.

Tudo dentro de **uma** transação, com todos os serviços internos em `CommitMode.Deferred`.

- [ ] **Step 5: Registrar no DI, rodar os testes da task, rodar a suíte inteira.**

- [ ] **Step 6: Commit** (`feat(shipment): registrar a entrada do transbordo e emitir as liberacoes`, com o `Atenção:` sobre as duas chaves proibidas).

---

### Task 6: Estornar o transbordo

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsTransshipmentReverseService.cs`
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsCancelService.cs`
- Modify: `SiagroB1.Application/Services/StorageTransactions/StorageTransactionsReverseService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsTransshipmentReverseServiceTests.cs`

- [ ] **Step 1: Escrever os testes que falham**

```csharp
[Fact] public async Task Reverse_ReturnsTheBalanceToTheLoad()                 // Transshipped volta a 0, saldo volta
[Fact] public async Task Reverse_CancelsTheEntryAndItsReleases()
[Fact] public async Task Reverse_OwnWarehouse_OnlyUnlinksTheReceipt()          // o Receipt não é cancelado
[Fact] public async Task Reverse_RefusesWhenTheReleaseHasBeenConsumed()        // ShippedQuantity > 0
[Fact] public async Task Reverse_RefusesWhenItIsNotTheLastOne()
[Fact] public async Task Reverse_RefusalOrigin_DoesNotUndoTheReturnInvoices()  // as devoluções continuam
[Fact] public async Task CancelStorageTransaction_RefusesTheTransshipmentEntry()
```

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Escrever o serviço** — carrega o transbordo e a carga; exige ser o de maior `Sequence` ("Estorne primeiro o transbordo N."); exige que nenhuma liberação gerada pela entrada (`GeneratedByStorageTransactionKey == EntryStorageTransactionKey`) tenha `ShippedQuantity > Tolerance` ("A liberação do transbordo já foi embarcada. Estorne a Expedição de saída antes."); exige que o transbordo não tenha saída vinculada; em transação: cancela o 15 (`TransactionStatus = Cancelled`, sem passar pelo serviço de cancelamento, que o barra) ou apenas desvincula o `Receipt` no caso próprio, cancela as liberações emitidas (`ReleaseStatus.Cancelled` + `CancellationReason`), remove a linha do transbordo, `SaveChangesAsync`, recalcula, registra `TransshipmentReversed` com delta positivo, `SaveChangesAsync`, commit.

- [ ] **Step 4: Barrar o 15 nos serviços de romaneio**

Em `StorageTransactionsCancelService` e `StorageTransactionsReverseService`, junto dos guards de `ShipmentLoadKey` / `RefusedFromShipmentLoadKey`:

```csharp
        // GAC-1181: o romaneio de entrada pertence ao transbordo. Cancelá-lo por aqui deixaria o
        // transbordo apontando um romaneio cancelado e as liberações vivas.
        if (transaction.ShipmentLoadTransshipmentKey != null)
            throw new ApplicationException(
                $"O romaneio {transaction.Code} é a entrada de um transbordo. Use Estornar " +
                "Transbordo na carga.");
```

- [ ] **Step 5: Registrar no DI, rodar os testes, rodar a suíte, commitar.**

---

### Task 7: Papel do romaneio — vincular, desvincular, ler e travar a composição

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsAttachTransactionsService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsDetachTransactionsService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsCompositionGuardService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsCancelService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsDeleteService.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsGetService.cs`
- Modify: `SiagroB1.Application/Services/ShippingTransactions/ShippingTransactionsChangeReleaseService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsAttachTransshipmentExitTests.cs`

**Interfaces:**
- Produces: `ShipmentLoadsAttachTransactionsService.ExecuteAsync(Guid shipmentLoadKey, ICollection<Guid> storageTransactionKeys, Guid? transshipmentKey, string userName)`.

- [ ] **Step 1: Escrever os testes que falham**

```csharp
[Fact] public async Task Attach_WithTransshipmentKey_StampsTheRole()
[Fact] public async Task Attach_WithTransshipmentKey_RefusesOtherWarehouse()
[Fact] public async Task Attach_WithTransshipmentKey_RefusesBeforeTheEntryIsRegistered()
[Fact] public async Task Attach_WithoutTransshipmentKey_KeepsTodaysBehaviour()      // regressão
[Fact] public async Task Detach_ClearsTheTransshipmentKey()
[Fact] public async Task Detach_RefusesTheOriginExitWhileThereIsATransshipment()
[Fact] public async Task Cancel_RefusesWhileThereIsATransshipment()
[Fact] public async Task Delete_RefusesWhileThereIsATransshipment()
[Fact] public async Task QueryTransactions_IncludesTheTransshipmentEntry()
```

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Vincular por papel** — `ExecuteAsync` ganha `Guid? transshipmentKey`. Com a chave: valida que o transbordo é da carga, que tem entrada registrada, que todo romaneio é `SalesShipment` e que o `WarehouseCode` de cada um é o do transbordo ("O romaneio {Code} é do armazém {X} e o transbordo é no armazém {Y}."); grava `ShipmentLoadKey` **e** `ShipmentLoadTransshipmentKey`. Sem a chave: comportamento de hoje, e recusa vincular romaneio cujo armazém seja o de um transbordo **com entrada registrada** ("Vincule-o como saída do transbordo N."). A homogeneidade (placa, produto, filial, unidade) continua valendo nos dois caminhos.

- [ ] **Step 4: Desvincular** — zera as duas chaves; antes disso, recusa quando o romaneio é **saída da origem** (`ShipmentLoadTransshipmentKey == null`) e a carga tem transbordo: "As liberações do transbordo derivam deste romaneio. Estorne o transbordo antes."

- [ ] **Step 5: Travar composição, cancelamento e exclusão**

`ShipmentLoadsCompositionGuardService.EnsureCanChangeCompositionAsync` ganha, depois do termo `returned`:

```csharp
        var transshipped = await ShipmentLoadsRecalculateTransshippedService
            .CalculateTransshippedAsync(context, load.Key);

        if (transshipped > Tolerance)
            throw new ApplicationException(
                $"A carga {load.Code} tem {transshipped:N3} em transbordo e sua composição não pode " +
                "ser alterada. Estorne o transbordo antes.");
```

`ShipmentLoadsCancelService` e `ShipmentLoadsDeleteService` recusam com a mesma mensagem; o `DeleteService` também remove `Transshipments` no `RemoveRange` (defesa em profundidade, como as descargas).

- [ ] **Step 6: Leitura** — `ShipmentLoadsGetService.QueryTransactions` passa a unir os romaneios cujo `ShipmentLoadTransshipmentKey` pertença a um transbordo da carga (subquery, no molde do `changeKeysForLoad` do GAC-1177), para a entrada aparecer no grid.

- [ ] **Step 7: Recusar a troca de liberação em saída de transbordo** — `ShippingTransactionsChangeReleaseService`: se o romaneio tem `ShipmentLoadTransshipmentKey`, recusa ("A troca de liberação não está disponível para a saída de um transbordo.").

- [ ] **Step 8: Rodar os testes, a suíte inteira e commitar.**

---

### Task 8: Recusa com destino Transbordo

**Files:**
- Modify: `SiagroB1.Domain/Enums/RefusalDestination.cs`
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRefuseService.cs`
- Modify: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsRefuseController.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsRefuseTransshipmentTests.cs`

- [ ] **Step 1: Escrever os testes que falham** — o cenário do usuário inteiro:

```csharp
[Fact] public async Task Refuse_ToTransshipment_ReturnsTheInvoicesAndOpensTheTransshipment()
[Fact] public async Task Refuse_ToTransshipment_LeavesTheLoadInTransshipment()       // não vai para Returned
[Fact] public async Task Refuse_ToTransshipment_DoesNotCreateTheType12()             // o ramo Warehouse fica intacto
[Fact] public async Task Refuse_ToWarehouse_StillBehavesLikeBefore()                 // regressão
[Fact] public async Task FullScenario_SpToPortRefusedThenPartnerThenSecondCustomer()  // a tabela numérica do spec
```

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: Enum + parsing** — `RefusalDestination.Transshipment = 2` com XML-doc; `ParseDestination` do controller aceita `"Transshipment"`; a validação de armazém obrigatório passa a valer para `Warehouse` **ou** `Transshipment`.

- [ ] **Step 4: Ramo novo no `ShipmentLoadsRefuseService`** — depois das devoluções (`ReturnAsync` por linha, que já devolvem o saldo), cria a linha do transbordo com `Origin = Refusal`, `WarehouseCode` do pedido, `TransshipmentDate = DateTime.Today`, `OutgoingQuantity = totalQuantity` (o total recusado) e `Sequence = último + 1`; `SaveChangesAsync`; `RecalculateInvoicedService.RecalculateAsync`; `movementLog.Register(..., TransshipmentStarted, -totalQuantity, load.AvailableQuantity, "Mercadoria recusada enviada para transbordo no armazém (X) Nome.", userName, movementContext: ...)`. Tudo na transação que já existe, em `CommitMode.Deferred`.

- [ ] **Step 5: Rodar os testes, a suíte inteira e commitar.**

---

### Task 9: Camada OData — actions, entity set e rotas

**Files:**
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`
- Create: `SiagroB1.Web/Controllers/ShipmentLoadsTransshipmentsController.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsTransshipmentStartController.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsTransshipmentRegisterEntryController.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsTransshipmentReverseController.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsTransshipmentsGetService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadTransshipmentEdmModelTests.cs`

- [ ] **Step 1: Escrever o teste de EDM que falha** — no molde de `ShipmentLoadDischargeEdmModelTests`: monta o EDM real (`new ODataConventionModelBuilder().ConfigureODataEntities().GetEdmModel()`) e assere: entity set `ShipmentLoadsTransshipments`; as três actions; `GrossWeight` como `Edm.Double`; `TransshipmentDate` como `Edm.String`; `GrossWeight`, `Comments` e `ReceiptStorageTransactionKey` como `IEdmOptionalParameter`; `ShipmentLoad` expondo `TransshippedQuantity` e `AvailableQuantity`.

- [ ] **Step 2: Rodar e ver falhar.**

- [ ] **Step 3: EDM**

```csharp
        modelBuilder.EntitySet<ShipmentLoadTransshipment>("ShipmentLoadsTransshipments");
```

```csharp
        // GAC-1181 — quantidade em Edm.Double e data em string, pelas mesmas razões das demais.
        var transshipmentStart = modelBuilder.Action("ShipmentLoadsTransshipmentStart");
        transshipmentStart.Parameter<Guid>("LoadKey");
        transshipmentStart.Parameter<string>("WarehouseCode");
        transshipmentStart.Parameter<string>("TransshipmentDate");
        transshipmentStart.Parameter<string>("Comments").Optional();

        var transshipmentEntry = modelBuilder.Action("ShipmentLoadsTransshipmentRegisterEntry");
        transshipmentEntry.Parameter<Guid>("Key");
        transshipmentEntry.Parameter<string>("EntryDate");
        transshipmentEntry.Parameter<double>("GrossWeight").Optional();
        transshipmentEntry.Parameter<Guid?>("ReceiptStorageTransactionKey").Optional();

        var transshipmentReverse = modelBuilder.Action("ShipmentLoadsTransshipmentReverse");
        transshipmentReverse.Parameter<Guid>("Key");
        transshipmentReverse.Parameter<string>("Reason").Optional();
```

E o parâmetro novo da vinculação: `ShipmentLoadsAttachTransactions` ganha `Parameter<Guid?>("TransshipmentKey").Optional()`.

- [ ] **Step 4: Controller de leitura**, com as **duas** rotas:

```csharp
    [HttpGet("odata/ShipmentLoads({key:guid})/Transshipments")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/Transshipments")]
    [EnableQuery]
    public IActionResult GetTransshipments(Guid key) => Ok(getService.QueryAll(key));
```

- [ ] **Step 5: Controllers de action**, no molde de `ShipmentLoadsCompleteController` (guard de `parameters == null`, `TryGetValue`, `ShipmentLoadActionParameters.TryParseDate`, mapeamento `NotFoundException → 404`, `ApplicationException → 400`).

- [ ] **Step 6: Registrar tudo no DI, rodar os testes de EDM, rodar a suíte, commitar.**

---

### Task 10: Frontend — seção Transbordos no detalhe da carga

**Files:**
- Create: `webapp/view/shipmentLoads/fragments/ShipmentLoadTransshipments.fragment.xml`
- Create: `webapp/view/shipmentLoads/fragments/ShipmentLoadTransshipmentDialog.fragment.xml`
- Modify: `webapp/view/shipmentLoads/Detail.view.xml`
- Modify: `webapp/controller/shipmentLoads/BaseController.ts`
- Modify: `webapp/controller/shipmentLoads/Detail.controller.ts` (`refreshAll`)
- Modify: `webapp/model/formatter.ts`

- [ ] **Step 1: Fragmento da tabela** — `t:Table id="loadTransshipmentsTable"`, `rows="{ path: 'Transshipments', parameters: { '$$ownRequest': true, '$select': 'Key,Sequence,Origin,WarehouseCode,WarehouseName,TransshipmentDate,OutgoingQuantity,EntryQuantity,ShrinkageQuantity,EntryStorageTransactionKey,Comments' }, sorter: { path: 'Sequence' } }"`. Colunas: Seq., Origem (`formatTransshipmentOrigin`), Armazém, Data, Saída, Entrada, Quebra, Situação (`formatTransshipmentStatus`: "Aguardando entrada" / "Aguardando saída" / "Concluído"). Toolbar com **Iniciar Transbordo**, **Registrar Entrada** e **Estornar**, com `visible` espelhando as regras do servidor.

- [ ] **Step 2: Fragmento do diálogo** — buffer JSON `viewModel>/transshipmentDialog/...` (nunca two-way no contexto OData), `DatePicker` com `valueFormat="yyyy-MM-dd"`, um **único** Input de peso de entrada com `sap.ui.model.type.Float` (3 casas, pt-BR), value help de armazém escrito **à mão** no model do diálogo, e, quando o armazém é próprio, um `Select` do `Receipt` da Entrada em Armazenagem.

- [ ] **Step 3: Handlers no `BaseController.ts`** — `onStartTransshipment`, `onRegisterTransshipmentEntry`, `onReverseTransshipment`, `refreshTransshipments`. Padrão: `Fragment.load({ id: this.getView().getId(), controller: this })` com `addDependent` dentro do `if`; trava `_transshipmentInFlight` avaliada **antes do primeiro await**; `bindContext("/Action(...)")` + `setParameter` + `await invoke()`; fechar o diálogo **depois** do resolve; `refresh` do contexto do elemento + tabelas.

- [ ] **Step 4: Seção no `Detail.view.xml`** (`ObjectPageSection` → `subSections` → `blocks` → `core:Fragment`), visível só em carga `Normal`, e `loadTransshipmentsTable` na lista de `refreshAll()`.

- [ ] **Step 5: Formatters** — `formatTransshipmentOrigin` (Planejado/Recusa), `formatTransshipmentStatus`, `InTransshipment` em `formatShipmentLoadStatus` ("Em transbordo") e `stateShipmentLoadStatus` (`Warning`), `TransshipmentReceipt` em `formatStorageTransactionType` ("Entrada em Transbordo"), e os três movimentos novos em `formatShipmentLoadMovementType`.

- [ ] **Step 6: `yarn ts-typecheck` e `yarn lint`, e commit** no repositório do frontend.

---

### Task 11: Frontend — recusa com transbordo, vínculo por papel e listas

**Files:**
- Modify: `webapp/view/shipmentLoads/fragments/Refusal.fragment.xml`
- Modify: `webapp/controller/shipmentLoads/Detail.controller.ts`
- Modify: `webapp/controller/shipmentLoads/Attach.controller.ts`
- Modify: `webapp/view/shipmentLoads/Attach.view.xml`
- Modify: `webapp/controller/shipmentLoads/Main.controller.ts`
- Modify: `webapp/controller/shipmentLoads/Panel.controller.ts`
- Modify: `webapp/view/shipmentLoads/Panel.view.xml`

- [ ] **Step 1: Terceiro destino na recusa** — `RadioButton` "Mercadoria segue para transbordo em outro armazém" (índice 2); o value help de armazém passa a aparecer para os índices 1 **e** 2; `onConfirmRefusal` manda `Destination = "Transshipment"` e mantém `DestinationWarehouseCode` **sempre definido** (nunca `undefined`: `JSON.stringify` omite a chave e o OData recusa o corpo).

- [ ] **Step 2: Vincular por papel** — `Attach.controller.ts` carrega os transbordos da carga (`bindList('/ShipmentLoads(<key>)/Transshipments')` + `requestContexts`), monta o `Select` "Vincular como" (Origem / Transbordo N — armazém X, só os com entrada registrada), ajusta o `$filter` **string crua** conforme o papel (o armazém entra no filtro quando o papel é um transbordo) e passa `TransshipmentKey` na action.

- [ ] **Step 3: Situação nova nas listas** — `SHIPMENT_LOAD_STATUSES` no `Main.controller.ts` ganha "Em transbordo"; `LANES` no `Panel.controller.ts` e o `Panel.view.xml` ganham a raia, posicionada entre "Carregada" e "Faturada Parcial".

- [ ] **Step 4: `yarn ts-typecheck` e `yarn lint`, e commit.**

---

### Task 12: Verificação ponta a ponta

- [ ] **Step 1: Gates** — no worktree do backend: `dotnet build` limpo e `dotnet test` inteiro verde; no frontend: `yarn ts-typecheck` e `yarn lint` limpos (`yarn test` **não** é gate: o projeto tem trava de cobertura de 50% contra ~2,4% reais).

- [ ] **Step 2: Aplicar a migration** — combinar com o usuário antes (o banco de desenvolvimento é compartilhado com outra sessão) e então:

```bash
ASPNETCORE_ENVIRONMENT=Yokotobi dotnet ef database update \
  --project SiagroB1.Migrations --startup-project SiagroB1.Web --context AppDbContext
```

⚠️ Sempre com o ambiente explícito: o profile `db-migration` aponta para produção.

- [ ] **Step 3: Subir a stack** — `SiagroB1.Web` e `SiagroB1.Gateway` no profile `yktb`, `yarn start:dev` no frontend, login `admin/1234`. **A porta é uma só**: combinar com a outra sessão antes de subir.

- [ ] **Step 4: Verificar no navegador, a partir da home** (nunca por `curl`):
  1. caso 2: carga planejada → Expedição da origem vinculada → Iniciar Transbordo → Registrar Entrada com quebra → conferir a liberação na Expedição de Grãos → Expedição de saída vinculada como transbordo → faturar → conferir os quatro números da carga;
  2. caso 3: faturar, Registrar Recusa com destino Transbordo, registrar entrada, sair para **outro cliente**, faturar de novo;
  3. caso 1: transbordo em armazém próprio, vinculando o `Receipt` da Entrada em Armazenagem;
  4. Estornar Transbordo e as travas (desvincular a origem, cancelar a carga, cancelar o 15 pela tela de romaneios, faturar em transbordo);
  5. regressão: uma carga Normal comum do início ao fim e uma recusa com destino **Armazém**.

- [ ] **Step 5: Registrar o resultado** — acrescentar ao spec a seção "Verificação executada", com o que foi exercido, os achados e os dados deixados no banco; commitar.
