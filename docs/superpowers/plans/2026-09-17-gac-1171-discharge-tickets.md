# GAC-1171 — Registro de pesos de descarga (tickets) na carga e grid de anexos

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** dar à carga um registro de descargas (ticket, data, peso, nota/item de destino, anexo) e um grid de anexos tipados, e exibir na Conferência de Entregas o confronto entre o peso do ticket e o peso conferido do relatório da trading.

**Architecture:** tabela filha `SHIPMENT_LOAD_DISCHARGES` da carga, apontando para uma linha de documento de saída. Um escritor único soma os tickets em duas colunas persistido-derivadas — `SalesInvoiceItem.TicketDeliveredQuantity` e `ShipmentLoad.DischargedQuantity` — e **nunca** escreve em `DeliveredQuantity`/`QuantityLoss`/`DeliveryStatus`. Anexos em tabela própria com tipo de lista fechada, no molde da Conferência de Armazém. Frontend: duas abas novas na página de detalhe da carga e duas colunas novas na Conferência de Entregas.

**Tech Stack:** .NET 10, EF Core (SQL Server), ASP.NET Core OData v4, xUnit + EF InMemory; OpenUI5 + TypeScript (`sap.ui.table`, OData V4 model).

**Spec:** `docs/superpowers/specs/2026-09-17-gac-1171-discharge-tickets-design.md` (leia antes da Task 1; o plano argumenta a partir dela)

## Global Constraints

- **Identificadores de código em inglês; texto que o usuário lê em pt-BR.** Vale para classes, tabelas e colunas. Rótulos de tela, mensagens de negócio e comentários explicativos em pt-BR.
- **Todo arquivo novo é staged imediatamente** com `git add <caminho>` no sub-repo a que pertence.
- **Nunca `git push`.** Commits só depois da verificação e da revisão do usuário.
- **Mensagem de commit:** `tipo(escopo): descrição em pt-BR, imperativo, minúscula, sem ponto final`. Types: `feat fix refactor perf chore docs test`. Scope deste trabalho: `shipment`. Rodapé `Refs: GAC-1171` sempre. **Rodapé `DB: <NomeDaMigration>` obrigatório em todo commit que contenha migration.**
- **Uma mudança que atravessa os dois repos são dois commits**, nunca um.
- **Invariante central, cobrada por teste em toda task de backend:** nenhum caminho de descarga escreve em `SalesInvoiceItem.DeliveredQuantity`, `QuantityLoss` ou `DeliveryStatus`, e nenhum dispara `SalesContractsRecalculateBalanceService` ou `SalesShipmentReleasesRecalculateShippedService`.
- **Valor novo de enum entra sempre no fim da numeração** — enums são persistidos como int e renumerar reescreve linhas já gravadas sem gerar migration.
- **Ambiente explícito em todo comando de banco:** `$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'` antes de `dotnet ef database update`. O profile `db-migration` aponta para produção.
- **UI5, campo decimal editável:** `sap.ui.model.odata.type.Double`, nunca `Decimal` — `Decimal` serializa como string e o leitor OData devolve 400 sem nomear o campo.
- **UI5, coleção filha em `ObjectPage`:** sempre `$$ownRequest`, senão vira `$expand` do pai e `refresh()` não funciona.
- **UI5, formatter sobre enum, `Edm.Decimal` ou `Edm.Guid`:** cada `part` leva `targetType: 'any'`, senão chega string no formatter.
- **Controller de action OData:** `ODataActionParameters` chega **nulo** quando nenhum parâmetro do EDM é enviado; parâmetro string é anulável (`TryGetValue` devolve `true` com `null`).

---

## Ordem e dependências

Backend 1 → 2 → 3 → 4 → 5 → 6, depois frontend 7 → 8 → 9. A Task 2 depende só da Task 1; as Tasks 3 e 4 dependem da 2; a 5 depende da 3 e da 4; a 6 é independente da 5. O frontend depende da 5 estar de pé.

---

### Task 1: Modelo — entidades, enum, DbSets e migrations

**Files:**
- Create: `SiagroB1.Domain/Entities/ShipmentLoadDischarge.cs`
- Create: `SiagroB1.Domain/Entities/ShipmentLoadAttachment.cs`
- Create: `SiagroB1.Domain/Enums/ShipmentLoadAttachmentType.cs`
- Modify: `SiagroB1.Domain/Entities/ShipmentLoad.cs` (coleções + `DischargedQuantity`)
- Modify: `SiagroB1.Domain/Entities/SalesInvoiceItem.cs` (`TicketDeliveredQuantity`)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (2 `DbSet` na região das linhas 67–72)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargeModelTests.cs`
- Create: 3 migrations em `SiagroB1.Migrations/AppContext/`

**Interfaces:**
- Consumes: nada.
- Produces: `ShipmentLoadDischarge` (`Key`, `ShipmentLoadKey`, `SalesInvoiceKey`, `SalesInvoiceItemKey`, `TicketNumber`, `DischargeDate`, `DischargedQuantity`, `Comments`, `AttachmentKey`, `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`); `ShipmentLoadAttachment` (`Key`, `ShipmentLoadKey`, `AttachmentType`, `Description`, `FileName`, `FileData`, `ContentType`, `CreatedAt`, `CreatedBy`); enum `ShipmentLoadAttachmentType`; `ShipmentLoad.Discharges`, `ShipmentLoad.Attachments`, `ShipmentLoad.DischargedQuantity`; `SalesInvoiceItem.TicketDeliveredQuantity`; `AppDbContext.ShipmentLoadsDischarges`, `AppDbContext.ShipmentLoadsAttachments`.

- [ ] **Step 1: Escrever o teste de modelo que falha**

Create `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargeModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Mapeamento relacional do registro de descarga e do anexo da carga. Usa o provider SqlServer
/// só para materializar o modelo — nenhuma conexão é aberta.
/// </summary>
public class ShipmentLoadDischargeModelTests
{
    private static AppDbContext ModelOnlyContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void ShipmentLoadDischarge_IsMappedToExpectedTableAndColumns()
    {
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(ShipmentLoadDischarge));
        Assert.NotNull(entityType);
        Assert.Equal("SHIPMENT_LOAD_DISCHARGES", entityType!.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains(nameof(ShipmentLoadDischarge.ShipmentLoadKey), props);
        Assert.Contains(nameof(ShipmentLoadDischarge.SalesInvoiceKey), props);
        Assert.Contains(nameof(ShipmentLoadDischarge.SalesInvoiceItemKey), props);
        Assert.Contains(nameof(ShipmentLoadDischarge.TicketNumber), props);
        Assert.Contains(nameof(ShipmentLoadDischarge.DischargeDate), props);
        Assert.Contains(nameof(ShipmentLoadDischarge.DischargedQuantity), props);
        Assert.Contains(nameof(ShipmentLoadDischarge.AttachmentKey), props);
    }

    [Fact]
    public void ShipmentLoadAttachment_IsMappedWithTypeColumn()
    {
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(ShipmentLoadAttachment));
        Assert.NotNull(entityType);
        Assert.Equal("SHIPMENT_LOAD_ATTACHMENTS", entityType!.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains(nameof(ShipmentLoadAttachment.AttachmentType), props);
        Assert.Contains(nameof(ShipmentLoadAttachment.FileData), props);
    }

    [Fact]
    public void Derived_ticket_quantities_are_real_columns()
    {
        // Persistido-derivadas, não [NotMapped]: entram no EDM sem AddProperty e a tela pode
        // colocá-las no $select e no $orderby.
        using var context = ModelOnlyContext();

        var loadProps = context.Model.FindEntityType(typeof(ShipmentLoad))!
            .GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains(nameof(ShipmentLoad.DischargedQuantity), loadProps);

        var itemProps = context.Model.FindEntityType(typeof(SalesInvoiceItem))!
            .GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains(nameof(SalesInvoiceItem.TicketDeliveredQuantity), itemProps);
    }

    [Fact]
    public void Discharge_foreign_keys_never_cascade()
    {
        // A exclusão da carga apaga as filhas À MÃO, em ShipmentLoadsDeleteService. Cascade aqui
        // faria a nota apagar tickets em silêncio, e o histórico do frete iria com ela.
        using var context = ModelOnlyContext();

        var foreignKeys = context.Model.FindEntityType(typeof(ShipmentLoadDischarge))!
            .GetForeignKeys().ToList();

        Assert.NotEmpty(foreignKeys);
        Assert.All(foreignKeys, fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
    }
}
```

- [ ] **Step 2: Rodar o teste e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadDischargeModelTests`
Expected: FAIL na compilação — `ShipmentLoadDischarge` e `ShipmentLoadAttachment` não existem.

- [ ] **Step 3: Criar o enum de tipo de anexo**

Create `SiagroB1.Domain/Enums/ShipmentLoadAttachmentType.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Natureza do documento anexado à carga (GAC-1171). Lista fechada, porque o objetivo é poder
/// perguntar "falta o ticket de descarga?" — o que uma descrição livre não responde.
/// </summary>
/// <remarks>
/// ⚠️ Persistido como <c>int</c>. Valor novo entra SEMPRE no fim da numeração: renumerar
/// reescreveria o significado de toda linha já gravada, e mudança de enum não gera migration.
/// </remarks>
public enum ShipmentLoadAttachmentType
{
    LoadingTicket = 0,    // Ticket de Carga
    DischargeTicket = 1,  // Ticket de Descarga
    TaxDocument = 2,      // Nota Fiscal
    FreightDocument = 3,  // Conhecimento de Frete
    Other = 4,            // Outro
}
```

- [ ] **Step 4: Criar a entidade de anexo**

Create `SiagroB1.Domain/Entities/ShipmentLoadAttachment.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Documento anexado à carga: tickets de carga e de descarga, nota fiscal, conhecimento de frete
/// ou outro (GAC-1171). Molde de <c>WarehouseReconciliationAttachment</c>, mais o tipo.
/// </summary>
[Table("SHIPMENT_LOAD_ATTACHMENTS")]
[Index(nameof(ShipmentLoadKey))]
public class ShipmentLoadAttachment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    public ShipmentLoadAttachmentType AttachmentType { get; set; } = ShipmentLoadAttachmentType.Other;

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string FileName { get; set; }

    [Column(TypeName = "VARBINARY(MAX)")]
    public required byte[] FileData { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string ContentType { get; set; }

    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }
}
```

- [ ] **Step 5: Criar a entidade de descarga**

Create `SiagroB1.Domain/Entities/ShipmentLoadDischarge.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Ticket de descarga da carga (GAC-1171): o que a balança do DESTINO pesou, segundo o documento
/// que o transportador entregou.
/// </summary>
/// <remarks>
/// Serve a duas coisas: liberar o pagamento do frete e confrontar o relatório de descarga do
/// cliente/trading. As duas fontes podem estar erradas por motivos diferentes — o ticket pode ter
/// sido adulterado pelo transportador, e o relatório pode ter sido alimentado com informação
/// errada.
/// <para>
/// ⚠️ Por isso o ticket NÃO é fonte da conferência. Ele soma em
/// <see cref="SalesInvoiceItem.TicketDeliveredQuantity"/> e nunca toca
/// <see cref="SalesInvoiceItem.DeliveredQuantity"/>, que é digitado pelo conferente a partir do
/// relatório da trading. Fazer o ticket preencher o peso conferido transformaria o número
/// possivelmente adulterado no padrão da conferência.
/// </para>
/// <para>
/// Guarda a nota E a linha da nota. A linha é o alvo do peso; a nota existe para o grid chegar ao
/// número dela com um <c>$expand</c> de um nível. O par é validado no servidor, porque nada impede
/// a tela de mandar uma combinação inconsistente.
/// </para>
/// </remarks>
[Table("SHIPMENT_LOAD_DISCHARGES")]
[Index(nameof(ShipmentLoadKey))]
[Index(nameof(SalesInvoiceItemKey))]
public class ShipmentLoadDischarge
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    public Guid? SalesInvoiceKey { get; set; }
    public virtual SalesInvoice? SalesInvoice { get; set; }

    public Guid? SalesInvoiceItemKey { get; set; }
    public virtual SalesInvoiceItem? SalesInvoiceItem { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string TicketNumber { get; set; }

    public DateTime DischargeDate { get; set; } = DateTime.Now.Date;

    /// <summary>Peso do ticket. Mesma escala de <c>StorageTransaction.GrossWeight</c>.</summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal DischargedQuantity { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    /// <summary>
    /// Arquivo do ticket, quando anexado no próprio diálogo de registro. Anulável: o ticket pode
    /// ser lançado antes de o arquivo chegar.
    /// </summary>
    public Guid? AttachmentKey { get; set; }
    public virtual ShipmentLoadAttachment? Attachment { get; set; }

    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }
}
```

- [ ] **Step 6: Adicionar coleções e a quantidade derivada em `ShipmentLoad`**

Modify `SiagroB1.Domain/Entities/ShipmentLoad.cs`. Depois de `ReturnedToWarehouseQuantity`, inserir:

```csharp
    /// <summary>
    /// Persistido-derivado: soma do peso dos tickets de descarga da carga (GAC-1171). Escritor
    /// único: <c>ShipmentLoadDischargesRecalculateService</c>. Existe para o cabeçalho confrontar
    /// descarregado × embarcado; NÃO entra em saldo, faturamento nem status.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal DischargedQuantity { get; set; }
```

E junto das outras coleções, depois de `ChangeLogs`:

```csharp
    /// <summary>Tickets de descarga da carga (GAC-1171).</summary>
    public virtual ICollection<ShipmentLoadDischarge> Discharges { get; } = [];

    /// <summary>Documentos anexados à carga (GAC-1171).</summary>
    public virtual ICollection<ShipmentLoadAttachment> Attachments { get; } = [];
```

- [ ] **Step 7: Adicionar a quantidade do ticket em `SalesInvoiceItem`**

Modify `SiagroB1.Domain/Entities/SalesInvoiceItem.cs`. Depois de `QuantityLoss`, inserir:

```csharp
    /// <summary>
    /// Peso descarregado segundo os TICKETS (GAC-1171): soma de
    /// <c>ShipmentLoadDischarge.DischargedQuantity</c> dos tickets que apontam para esta linha.
    /// Persistido-derivado, escritor único <c>ShipmentLoadDischargesRecalculateService</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Vive ao lado de <see cref="DeliveredQuantity"/> e NÃO se confunde com ele: este é o
    /// ticket do transportador, aquele é o relatório da trading digitado pelo conferente. Nenhum
    /// dos dois manda no outro — é justamente a divergência entre eles que o chamado quer expor,
    /// porque o ticket pode ter sido adulterado e o relatório pode ter vindo errado.
    /// NÃO participa de nenhum fator efetivo: saldo de contrato e de liberação continuam lendo só
    /// <see cref="DeliveredQuantity"/> e <see cref="QuantityLoss"/> de item encerrado.
    /// </remarks>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal TicketDeliveredQuantity { get; set; }
```

- [ ] **Step 8: Declarar os `DbSet` e as FKs sem cascade**

Modify `SiagroB1.Infra/Context/AppDbContext.cs`. Junto dos `DbSet` da carga (região das linhas 69–72):

```csharp
    public DbSet<ShipmentLoadDischarge> ShipmentLoadsDischarges { get; set; }
    public DbSet<ShipmentLoadAttachment> ShipmentLoadsAttachments { get; set; }
```

Em `OnModelCreating`, junto das outras configurações de carga:

```csharp
        // As quatro FKs do ticket de descarga são NoAction. A carga apaga as filhas à mão
        // (ShipmentLoadsDeleteService); e cascade a partir da NOTA faria cancelar/excluir nota
        // levar embora, em silêncio, a evidência física que libera o pagamento do frete.
        modelBuilder.Entity<ShipmentLoadDischarge>()
            .HasOne(x => x.ShipmentLoad)
            .WithMany(x => x.Discharges)
            .HasForeignKey(x => x.ShipmentLoadKey)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ShipmentLoadDischarge>()
            .HasOne(x => x.SalesInvoice)
            .WithMany()
            .HasForeignKey(x => x.SalesInvoiceKey)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ShipmentLoadDischarge>()
            .HasOne(x => x.SalesInvoiceItem)
            .WithMany()
            .HasForeignKey(x => x.SalesInvoiceItemKey)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ShipmentLoadDischarge>()
            .HasOne(x => x.Attachment)
            .WithMany()
            .HasForeignKey(x => x.AttachmentKey)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<ShipmentLoadAttachment>()
            .HasOne(x => x.ShipmentLoad)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.ShipmentLoadKey)
            .OnDelete(DeleteBehavior.NoAction);
```

- [ ] **Step 9: Rodar o teste e confirmar que passa**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadDischargeModelTests`
Expected: PASS, 4 testes.

- [ ] **Step 10: Gerar as três migrations**

Run, na raiz de `siagro-b1-backend`:

```bash
dotnet ef migrations add AddShipmentLoadAttachments --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir AppContext
dotnet ef migrations add AddShipmentLoadDischarges --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir AppContext
dotnet ef migrations add AddTicketDeliveredQuantity --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --output-dir AppContext
```

Anexos primeiro: a descarga tem FK para o anexo, e a ordem errada gera uma migration que não aplica em banco novo. Conferir cada arquivo gerado: `CreateTable` sem `onDelete`, e as duas `AddColumn` com `defaultValue: 0m`.

⚠️ Não use `dotnet ef migrations remove --no-build` se precisar desfazer: sem build ele apaga a migration ERRADA. Rode `dotnet ef migrations remove --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` com build.

- [ ] **Step 11: Aplicar no banco local e conferir**

```bash
$env:ASPNETCORE_ENVIRONMENT = 'Yokotobi-Development'
dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Expected: as três aplicam sem erro. Se `database update` reclamar de migration já aplicada, pare e reporte — é drift de ambiente, não erro do plano.

- [ ] **Step 12: Commit**

```bash
git add SiagroB1.Domain/Entities/ShipmentLoadDischarge.cs SiagroB1.Domain/Entities/ShipmentLoadAttachment.cs SiagroB1.Domain/Enums/ShipmentLoadAttachmentType.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargeModelTests.cs
git add SiagroB1.Domain/Entities/ShipmentLoad.cs SiagroB1.Domain/Entities/SalesInvoiceItem.cs SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Migrations/AppContext/
git commit -m "feat(shipment): criar modelo de ticket de descarga e anexo da carga

O ticket de descarga serve a duas coisas: liberar o pagamento do frete e
confrontar o relatorio de descarga do cliente/trading. Como as duas fontes
podem estar erradas por motivos diferentes (ticket adulterado pelo
transportador, relatorio alimentado com informacao errada), o peso do ticket
mora em coluna propria e nunca se mistura com o peso conferido.

Atencao: as FKs sao todas NoAction. Cascade a partir da nota faria
cancelar/excluir nota levar embora a evidencia fisica que libera o frete.

Refs: GAC-1171
DB: AddShipmentLoadAttachments, AddShipmentLoadDischarges, AddTicketDeliveredQuantity"
```

---

### Task 2: Escritor único das quantidades derivadas

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargesRecalculateService.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargesRecalculateServiceTests.cs`

**Interfaces:**
- Consumes: entidades da Task 1.
- Produces: `ShipmentLoadDischargesRecalculateService` com `Task RecalculateAsync(Guid shipmentLoadKey, IEnumerable<Guid> salesInvoiceItemKeys)` — enfileira as escritas no contexto e **não** chama `SaveChangesAsync` (quem chama decide a transação, como `ShipmentLoadsChangeLogService` já faz).

- [ ] **Step 1: Escrever os testes que falham**

Create `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargesRecalculateServiceTests.cs`:

```csharp
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Escritor único das quantidades derivadas do ticket. O teste que mais importa aqui é o que
/// prova o que o serviço NÃO faz: encostar na conferência de entrega.
/// </summary>
public class ShipmentLoadDischargesRecalculateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadDischargesRecalculateService Service() => new(_db.Context);

    private async Task<(ShipmentLoad Load, SalesInvoice Invoice, SalesInvoiceItem Item)> SeedAsync(
        SalesInvoiceDeliveryStatus deliveryStatus = SalesInvoiceDeliveryStatus.Open,
        decimal deliveredQuantity = 0m)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = ShipmentLoadStatus.Invoiced,
            TotalQuantity = 40000m,
        };

        var invoice = new SalesInvoice { Key = Guid.NewGuid(), ShipmentLoadKey = load.Key };

        var item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 40000m,
            DeliveredQuantity = deliveredQuantity,
            DeliveryStatus = deliveryStatus,
        };

        _db.Context.ShipmentLoads.Add(load);
        _db.Context.SalesInvoices.Add(invoice);
        _db.Context.SalesInvoicesItems.Add(item);
        await _db.Context.SaveChangesAsync();

        return (load, invoice, item);
    }

    private void AddTicket(ShipmentLoad load, SalesInvoice invoice, SalesInvoiceItem item, decimal weight)
    {
        _db.Context.ShipmentLoadsDischarges.Add(new ShipmentLoadDischarge
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = load.Key,
            SalesInvoiceKey = invoice.Key,
            SalesInvoiceItemKey = item.Key,
            TicketNumber = "T1",
            DischargeDate = DateTime.Now.Date,
            DischargedQuantity = weight,
        });
    }

    [Fact]
    public async Task Sums_every_ticket_of_the_item_and_of_the_load()
    {
        var (load, invoice, item) = await SeedAsync();
        AddTicket(load, invoice, item, 25000m);
        AddTicket(load, invoice, item, 14500m);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key!.Value, [item.Key!.Value]);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(39500m, item.TicketDeliveredQuantity);
        Assert.Equal(39500m, load.DischargedQuantity);
    }

    [Theory]
    [InlineData(SalesInvoiceDeliveryStatus.Open)]
    [InlineData(SalesInvoiceDeliveryStatus.Closed)]
    public async Task Never_touches_the_delivery_reconciliation(SalesInvoiceDeliveryStatus status)
    {
        // A regra central do GAC-1171: o ticket alimenta o campo dele e só. Vale nos DOIS
        // estados da entrega — a conferência é mandatória e soberana.
        var (load, invoice, item) = await SeedAsync(status, deliveredQuantity: 38000m);
        item.QuantityLoss = 120m;
        await _db.Context.SaveChangesAsync();

        AddTicket(load, invoice, item, 39500m);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key!.Value, [item.Key!.Value]);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(39500m, item.TicketDeliveredQuantity);
        Assert.Equal(38000m, item.DeliveredQuantity);
        Assert.Equal(120m, item.QuantityLoss);
        Assert.Equal(status, item.DeliveryStatus);
    }

    [Fact]
    public async Task Zeroes_the_item_when_the_last_ticket_is_gone()
    {
        var (load, invoice, item) = await SeedAsync(deliveredQuantity: 38000m);
        AddTicket(load, invoice, item, 39500m);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key!.Value, [item.Key!.Value]);
        await _db.Context.SaveChangesAsync();

        _db.Context.ShipmentLoadsDischarges.RemoveRange(_db.Context.ShipmentLoadsDischarges);
        await _db.Context.SaveChangesAsync();

        await Service().RecalculateAsync(load.Key!.Value, [item.Key!.Value]);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(0m, item.TicketDeliveredQuantity);
        Assert.Equal(0m, load.DischargedQuantity);
        // O conferido continua de pé: excluir ticket não apaga a conferência.
        Assert.Equal(38000m, item.DeliveredQuantity);
    }

    [Fact]
    public async Task Ignores_unknown_item_keys_without_throwing()
    {
        var (load, _, _) = await SeedAsync();

        await Service().RecalculateAsync(load.Key!.Value, [Guid.NewGuid()]);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(0m, load.DischargedQuantity);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadDischargesRecalculateServiceTests`
Expected: FAIL na compilação — `ShipmentLoadDischargesRecalculateService` não existe.

- [ ] **Step 3: Implementar o serviço**

Create `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargesRecalculateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Escritor ÚNICO das duas quantidades derivadas do ticket de descarga (GAC-1171):
/// <c>SalesInvoiceItem.TicketDeliveredQuantity</c> e <c>ShipmentLoad.DischargedQuantity</c>.
///
/// Apenas enfileira as escritas no contexto — quem chama decide quando salvar, para que a soma e
/// o ticket que a causou entrem no mesmo <c>SaveChanges</c>.
/// </summary>
/// <remarks>
/// ⚠️ Este serviço NÃO escreve em <c>DeliveredQuantity</c>, <c>QuantityLoss</c> ou
/// <c>DeliveryStatus</c>, e NÃO chama <c>SalesContractsRecalculateBalanceService</c> nem
/// <c>SalesShipmentReleasesRecalculateShippedService</c>. Essa ausência é a regra de negócio, não
/// um esquecimento: a conferência de entrega é mandatória e soberana, e o peso do ticket — que
/// pode ter sido adulterado pelo transportador — não pode virar o número que move saldo. Quem move
/// saldo continua sendo um único ato: o encerramento da conferência.
/// </remarks>
public class ShipmentLoadDischargesRecalculateService(AppDbContext context)
{
    public async Task RecalculateAsync(Guid shipmentLoadKey, IEnumerable<Guid> salesInvoiceItemKeys)
    {
        foreach (var itemKey in salesInvoiceItemKeys.Distinct())
        {
            var item = await context.SalesInvoicesItems
                .FirstOrDefaultAsync(x => x.Key == itemKey);

            // Chave desconhecida não é erro: a exclusão em lote pode citar item já removido.
            if (item is null) continue;

            item.TicketDeliveredQuantity = await SumByItemAsync(itemKey);
        }

        var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == shipmentLoadKey);

        if (load is not null)
            load.DischargedQuantity = await SumByLoadAsync(shipmentLoadKey);
    }

    private Task<decimal> SumByItemAsync(Guid itemKey) =>
        SumAsync(
            context.ShipmentLoadsDischarges.Where(x => x.SalesInvoiceItemKey == itemKey),
            x => x.SalesInvoiceItemKey == itemKey);

    private Task<decimal> SumByLoadAsync(Guid loadKey) =>
        SumAsync(
            context.ShipmentLoadsDischarges.Where(x => x.ShipmentLoadKey == loadKey),
            x => x.ShipmentLoadKey == loadKey);

    /// <summary>
    /// Soma o que está no banco MAIS o que está no rastreador, em vez de usar <c>SumAsync</c> do
    /// EF: a agregação no servidor não vê a entidade recém-adicionada e ainda não gravada, e foi
    /// exatamente isso que já fez a diferença de entrega ser calculada com o valor anterior.
    /// </summary>
    /// <remarks>
    /// A consulta entra FILTRADA por chave (<paramref name="persistedQuery"/>): somar em memória a
    /// tabela inteira funcionaria hoje e degradaria em silêncio conforme os tickets acumulam.
    /// O predicado repete o mesmo filtro para as entidades do rastreador, que o SQL não alcança.
    /// </remarks>
    private async Task<decimal> SumAsync(
        IQueryable<Domain.Entities.ShipmentLoadDischarge> persistedQuery,
        Func<Domain.Entities.ShipmentLoadDischarge, bool> predicate)
    {
        var persisted = await persistedQuery.AsNoTracking().ToListAsync();

        var tracked = context.ChangeTracker
            .Entries<Domain.Entities.ShipmentLoadDischarge>()
            .Where(e => e.State != EntityState.Deleted)
            .Select(e => e.Entity)
            .ToList();

        var trackedKeys = tracked.Select(x => x.Key).ToHashSet();

        return persisted
            .Where(x => !trackedKeys.Contains(x.Key))
            .Concat(tracked)
            .Where(predicate)
            .Sum(x => x.DischargedQuantity);
    }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadDischargesRecalculateServiceTests`
Expected: PASS, 5 casos (o `Theory` conta dois).

- [ ] **Step 5: Registrar na DI**

Modify `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, junto dos serviços de carga (região das linhas 391–413):

```csharp
        services.AddScoped<ShipmentLoadDischargesRecalculateService>();
```

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargesRecalculateService.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargesRecalculateServiceTests.cs
git add SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(shipment): somar peso dos tickets de descarga em escritor unico

Uma porta unica escreve TicketDeliveredQuantity do item e DischargedQuantity
da carga, para o numero ser sempre reconstruivel a partir dos tickets.

Atencao: o servico nao encosta em DeliveredQuantity, QuantityLoss nem
DeliveryStatus, e nao dispara recalculo de contrato ou liberacao. Essa
ausencia E a regra de negocio: a conferencia de entrega e soberana e o peso
do ticket, que pode ter sido adulterado, nao pode mover saldo.

Refs: GAC-1171"
```

---

### Task 3: Serviços de descarga (criar, alterar, excluir, ler) e guards

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargesCreateService.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargesUpdateService.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargesDeleteService.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargesGetService.cs`
- Modify: `SiagroB1.Domain/Entities/ShipmentLoadChangeLogFields.cs` (código `Discharge`)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargesServiceTests.cs`

**Interfaces:**
- Consumes: `ShipmentLoadDischargesRecalculateService.RecalculateAsync`, `ShipmentLoadsChangeLogService.Register`.
- Produces:
  - `ShipmentLoadDischargesCreateService.ExecuteAsync(Guid loadKey, Guid salesInvoiceKey, Guid salesInvoiceItemKey, string? ticketNumber, DateTime dischargeDate, decimal quantity, string? comments, Guid? attachmentKey, string userName) → Task<ShipmentLoadDischarge>`
  - `ShipmentLoadDischargesUpdateService.ExecuteAsync(Guid dischargeKey, string? ticketNumber, DateTime dischargeDate, decimal quantity, string? comments, string userName) → Task`
  - `ShipmentLoadDischargesDeleteService.ExecuteAsync(Guid dischargeKey, string userName) → Task`
  - `ShipmentLoadDischargesGetService.QueryAll(Guid shipmentLoadKey) → IQueryable<ShipmentLoadDischarge>`
  - `ShipmentLoadChangeLogFields.Discharge` (`"Discharge"`) e `ShipmentLoadChangeLogFields.DescribeDischarge(string ticketNumber, decimal quantity) → string`

- [ ] **Step 1: Escrever os testes que falham**

Create `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargesServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Registro de descarga: gravação, guards e as linhas que cada operação deixa no log da carga.
/// </summary>
public class ShipmentLoadDischargesServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadDischargesRecalculateService Recalculate() => new(_db.Context);
    private ShipmentLoadsChangeLogService ChangeLog() => new(_db.Context);

    private ShipmentLoadDischargesCreateService CreateService() => new(
        _db.Context, Recalculate(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesCreateService>.Instance);

    private ShipmentLoadDischargesUpdateService UpdateService() => new(
        _db.Context, Recalculate(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesUpdateService>.Instance);

    private ShipmentLoadDischargesDeleteService DeleteService() => new(
        _db.Context, Recalculate(), ChangeLog(),
        NullLogger<ShipmentLoadDischargesDeleteService>.Instance);

    private ShipmentLoad _load = null!;
    private SalesInvoice _invoice = null!;
    private SalesInvoiceItem _item = null!;

    private async Task SeedAsync(ShipmentLoadStatus status = ShipmentLoadStatus.Invoiced,
        InvoiceStatus invoiceStatus = InvoiceStatus.Confirmed)
    {
        _load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = status,
            TotalQuantity = 40000m,
        };

        _invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = _load.Key,
            InvoiceStatus = invoiceStatus,
        };

        _item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = _invoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 40000m,
        };

        _db.Context.ShipmentLoads.Add(_load);
        _db.Context.SalesInvoices.Add(_invoice);
        _db.Context.SalesInvoicesItems.Add(_item);
        await _db.Context.SaveChangesAsync();
    }

    private Task<ShipmentLoadDischarge> CreateAsync(decimal quantity = 39500m, string ticket = "T-1") =>
        CreateService().ExecuteAsync(
            _load.Key!.Value, _invoice.Key!.Value, _item.Key!.Value,
            ticket, new DateTime(2026, 9, 17), quantity, "descarga na trading", null, "paulo");

    private List<ShipmentLoadChangeLog> LogsOf() =>
        _db.Context.ShipmentLoadsChangeLogs.Where(l => l.ShipmentLoadKey == _load.Key).ToList();

    [Fact]
    public async Task Create_stores_the_ticket_sums_it_and_logs_it()
    {
        await SeedAsync();

        var discharge = await CreateAsync();

        Assert.Equal("T-1", discharge.TicketNumber);
        Assert.Equal("paulo", discharge.CreatedBy);
        Assert.NotNull(discharge.CreatedAt);
        Assert.Equal(39500m, _item.TicketDeliveredQuantity);
        Assert.Equal(39500m, _load.DischargedQuantity);

        var log = Assert.Single(LogsOf());
        Assert.Equal(ShipmentLoadChangeLogFields.Discharge, log.Field);
        Assert.Null(log.OldValue);
        Assert.Contains("T-1", log.NewValue);
    }

    [Fact]
    public async Task Create_leaves_the_delivery_reconciliation_untouched()
    {
        await SeedAsync();
        _item.DeliveredQuantity = 38000m;
        _item.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;
        await _db.Context.SaveChangesAsync();

        await CreateAsync();

        Assert.Equal(38000m, _item.DeliveredQuantity);
        Assert.Equal(SalesInvoiceDeliveryStatus.Closed, _item.DeliveryStatus);
        Assert.Equal(39500m, _item.TicketDeliveredQuantity);
    }

    [Fact]
    public async Task Update_changes_the_weight_and_resums()
    {
        await SeedAsync();
        var discharge = await CreateAsync();

        await UpdateService().ExecuteAsync(
            discharge.Key!.Value, "T-1A", new DateTime(2026, 9, 18), 40100m, "corrigido", "paulo");

        Assert.Equal("T-1A", discharge.TicketNumber);
        Assert.Equal(40100m, discharge.DischargedQuantity);
        Assert.Equal("paulo", discharge.UpdatedBy);
        Assert.Equal(40100m, _item.TicketDeliveredQuantity);
        Assert.Equal(2, LogsOf().Count);
    }

    [Fact]
    public async Task Delete_removes_the_ticket_and_resums()
    {
        await SeedAsync();
        var discharge = await CreateAsync();

        await DeleteService().ExecuteAsync(discharge.Key!.Value, "paulo");

        Assert.Empty(_db.Context.ShipmentLoadsDischarges);
        Assert.Equal(0m, _item.TicketDeliveredQuantity);
        Assert.Equal(0m, _load.DischargedQuantity);

        var deletionLog = LogsOf().Last();
        Assert.Contains("T-1", deletionLog.OldValue);
        Assert.Null(deletionLog.NewValue);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Cancelled)]
    [InlineData(ShipmentLoadStatus.Returned)]
    public async Task Frozen_load_refuses_the_three_operations(ShipmentLoadStatus status)
    {
        await SeedAsync(ShipmentLoadStatus.Invoiced);
        var discharge = await CreateAsync();

        _load.Status = status;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(1000m, "T-2"));
        await Assert.ThrowsAsync<DefaultException>(() => UpdateService().ExecuteAsync(
            discharge.Key!.Value, "T-1", new DateTime(2026, 9, 17), 100m, null, "paulo"));
        await Assert.ThrowsAsync<DefaultException>(() =>
            DeleteService().ExecuteAsync(discharge.Key!.Value, "paulo"));
    }

    [Fact]
    public async Task Cancelled_invoice_refuses_a_new_ticket()
    {
        await SeedAsync(invoiceStatus: InvoiceStatus.Cancelled);

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync());
    }

    [Fact]
    public async Task Item_from_another_invoice_is_refused()
    {
        await SeedAsync();

        var strangerItem = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = Guid.NewGuid(),
            ItemCode = "MILHO",
            UnitOfMeasureCode = "KG",
            Quantity = 1000m,
        };
        _db.Context.SalesInvoicesItems.Add(strangerItem);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateService().ExecuteAsync(
            _load.Key!.Value, _invoice.Key!.Value, strangerItem.Key!.Value,
            "T-9", DateTime.Now.Date, 100m, null, null, "paulo"));
    }

    [Fact]
    public async Task Invoice_from_another_load_is_refused()
    {
        await SeedAsync();

        var otherInvoice = new SalesInvoice { Key = Guid.NewGuid(), ShipmentLoadKey = Guid.NewGuid() };
        var otherItem = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = otherInvoice.Key,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 1000m,
        };
        _db.Context.SalesInvoices.Add(otherInvoice);
        _db.Context.SalesInvoicesItems.Add(otherItem);
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateService().ExecuteAsync(
            _load.Key!.Value, otherInvoice.Key!.Value, otherItem.Key!.Value,
            "T-9", DateTime.Now.Date, 100m, null, null, "paulo"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Non_positive_weight_is_refused(decimal quantity)
    {
        await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(quantity, "T-3"));
    }

    [Fact]
    public async Task Blank_ticket_number_is_refused()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => CreateAsync(100m, "   "));
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadDischargesServiceTests`
Expected: FAIL na compilação — os três serviços não existem.

- [ ] **Step 3: Adicionar o código de log e o descritor**

Modify `SiagroB1.Domain/Entities/ShipmentLoadChangeLogFields.cs`. Junto das outras constantes:

```csharp
    /// <summary>
    /// Ticket de descarga da carga (coleção <c>Discharges</c>, GAC-1171). Inclusão, alteração e
    /// exclusão passam por aqui: é dado DIGITADO, e o log da carga é o lugar dele. A Movimentação
    /// não recebe nada, porque o saldo da carga não muda.
    /// </summary>
    public const string Discharge = "Discharge";
```

E, junto dos outros `Describe*`:

```csharp
    /// <summary>
    /// Ticket como ele aparece no log: número e peso juntos, porque o log é texto livre e uma
    /// linha só com o peso não diria de qual ticket ele é.
    /// </summary>
    public static string DescribeDischarge(string ticketNumber, decimal quantity) =>
        $"{ticketNumber} — {quantity.ToString("N3", PtBr)}";
```

- [ ] **Step 4: Implementar o guard compartilhado e o serviço de criação**

Create `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargesCreateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Registra um ticket de descarga na carga (GAC-1171), contra uma linha de documento de saída.
/// </summary>
public class ShipmentLoadDischargesCreateService(
    AppDbContext context,
    ShipmentLoadDischargesRecalculateService recalculate,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadDischargesCreateService> logger)
{
    public async Task<ShipmentLoadDischarge> ExecuteAsync(
        Guid loadKey,
        Guid salesInvoiceKey,
        Guid salesInvoiceItemKey,
        string? ticketNumber,
        DateTime dischargeDate,
        decimal quantity,
        string? comments,
        Guid? attachmentKey,
        string userName)
    {
        try
        {
            var ticket = ShipmentLoadDischargeRules.NormalizeTicketNumber(ticketNumber);
            ShipmentLoadDischargeRules.EnsurePositiveQuantity(quantity);

            var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == loadKey)
                ?? throw new NotFoundException("Carga não encontrada.");

            ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(load);

            var invoice = await context.SalesInvoices
                .FirstOrDefaultAsync(x => x.Key == salesInvoiceKey)
                ?? throw new NotFoundException("Documento de saída não encontrado.");

            if (invoice.ShipmentLoadKey != loadKey)
                throw new DefaultException(
                    "O documento de saída informado não pertence a esta carga.");

            if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
                throw new DefaultException(
                    "Não é possível registrar descarga em documento de saída cancelado.");

            var item = await context.SalesInvoicesItems
                .FirstOrDefaultAsync(x => x.Key == salesInvoiceItemKey)
                ?? throw new NotFoundException("Item do documento de saída não encontrado.");

            if (item.SalesInvoiceKey != salesInvoiceKey)
                throw new DefaultException(
                    "O item informado não pertence ao documento de saída informado.");

            var discharge = new ShipmentLoadDischarge
            {
                ShipmentLoadKey = loadKey,
                SalesInvoiceKey = salesInvoiceKey,
                SalesInvoiceItemKey = salesInvoiceItemKey,
                TicketNumber = ticket,
                DischargeDate = dischargeDate.Date,
                DischargedQuantity = quantity,
                Comments = comments,
                AttachmentKey = attachmentKey,
                CreatedAt = DateTime.Now,
                CreatedBy = userName,
            };

            await context.AddAsync(discharge);

            changeLog.Register(
                loadKey,
                ShipmentLoadChangeLogFields.Discharge,
                null,
                ShipmentLoadChangeLogFields.DescribeDischarge(ticket, quantity),
                userName);

            await recalculate.RecalculateAsync(loadKey, [salesInvoiceItemKey]);

            await context.SaveChangesAsync();

            return discharge;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
```

Create, no mesmo arquivo de pasta, `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargeRules.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Regras compartilhadas pelos três serviços de escrita do ticket de descarga (GAC-1171).
/// </summary>
/// <remarks>
/// ⚠️ NÃO existe aqui guard de "entrega encerrada", e a ausência é deliberada: o ticket é aceito
/// em qualquer estado da conferência, porque ele não escreve no número conferido. Barrar deixaria
/// a Logística sem onde lançar o documento de que o financeiro precisa para liberar o frete.
/// </remarks>
public static class ShipmentLoadDischargeRules
{
    public static string NormalizeTicketNumber(string? ticketNumber)
    {
        var text = (ticketNumber ?? string.Empty).Trim();

        if (text.Length == 0)
            throw new DefaultException("Informe o número do ticket de descarga.");

        return text.Length > 50 ? text[..50] : text;
    }

    public static void EnsurePositiveQuantity(decimal quantity)
    {
        if (quantity <= decimal.Zero)
            throw new DefaultException("O peso descarregado deve ser maior que zero.");
    }

    /// <summary>
    /// Carga cancelada ou devolvida está congelada: as três operações mexem em quantidade.
    /// Diferente do comentário da carga, que vale a qualquer tempo porque não move número nenhum.
    /// </summary>
    public static void EnsureLoadAcceptsChanges(ShipmentLoad load)
    {
        if (load.Status is ShipmentLoadStatus.Cancelled or ShipmentLoadStatus.Returned)
            throw new DefaultException(
                "Carga cancelada ou devolvida não aceita registro de descarga.");
    }
}
```

- [ ] **Step 5: Implementar alteração e exclusão**

Create `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargesUpdateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Altera um ticket de descarga já registrado (GAC-1171). Nota e item não mudam: apontar o ticket
/// para outra linha é excluir e registrar de novo, senão a soma da linha antiga fica órfã.
/// </summary>
public class ShipmentLoadDischargesUpdateService(
    AppDbContext context,
    ShipmentLoadDischargesRecalculateService recalculate,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadDischargesUpdateService> logger)
{
    public async Task ExecuteAsync(
        Guid dischargeKey,
        string? ticketNumber,
        DateTime dischargeDate,
        decimal quantity,
        string? comments,
        string userName)
    {
        try
        {
            var ticket = ShipmentLoadDischargeRules.NormalizeTicketNumber(ticketNumber);
            ShipmentLoadDischargeRules.EnsurePositiveQuantity(quantity);

            var discharge = await context.ShipmentLoadsDischarges
                .FirstOrDefaultAsync(x => x.Key == dischargeKey)
                ?? throw new NotFoundException("Registro de descarga não encontrado.");

            var load = await context.ShipmentLoads
                .FirstOrDefaultAsync(x => x.Key == discharge.ShipmentLoadKey)
                ?? throw new NotFoundException("Carga não encontrada.");

            ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(load);

            var before = ShipmentLoadChangeLogFields.DescribeDischarge(
                discharge.TicketNumber, discharge.DischargedQuantity);

            discharge.TicketNumber = ticket;
            discharge.DischargeDate = dischargeDate.Date;
            discharge.DischargedQuantity = quantity;
            discharge.Comments = comments;
            discharge.UpdatedAt = DateTime.Now;
            discharge.UpdatedBy = userName;

            changeLog.Register(
                load.Key!.Value,
                ShipmentLoadChangeLogFields.Discharge,
                before,
                ShipmentLoadChangeLogFields.DescribeDischarge(ticket, quantity),
                userName);

            if (discharge.SalesInvoiceItemKey.HasValue)
                await recalculate.RecalculateAsync(
                    load.Key!.Value, [discharge.SalesInvoiceItemKey.Value]);

            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
```

Create `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargesDeleteService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Exclui um ticket de descarga (GAC-1171). O ticket excluído fica no log — é o que permite
/// reconstituir o peso que já foi lançado e depois retirado.
/// </summary>
public class ShipmentLoadDischargesDeleteService(
    AppDbContext context,
    ShipmentLoadDischargesRecalculateService recalculate,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadDischargesDeleteService> logger)
{
    public async Task ExecuteAsync(Guid dischargeKey, string userName)
    {
        try
        {
            var discharge = await context.ShipmentLoadsDischarges
                .FirstOrDefaultAsync(x => x.Key == dischargeKey)
                ?? throw new NotFoundException("Registro de descarga não encontrado.");

            var load = await context.ShipmentLoads
                .FirstOrDefaultAsync(x => x.Key == discharge.ShipmentLoadKey)
                ?? throw new NotFoundException("Carga não encontrada.");

            ShipmentLoadDischargeRules.EnsureLoadAcceptsChanges(load);

            var itemKey = discharge.SalesInvoiceItemKey;

            context.ShipmentLoadsDischarges.Remove(discharge);

            changeLog.Register(
                load.Key!.Value,
                ShipmentLoadChangeLogFields.Discharge,
                ShipmentLoadChangeLogFields.DescribeDischarge(
                    discharge.TicketNumber, discharge.DischargedQuantity),
                null,
                userName);

            if (itemKey.HasValue)
                await recalculate.RecalculateAsync(load.Key!.Value, [itemKey.Value]);

            await context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
```

- [ ] **Step 6: Implementar a leitura**

Create `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischargesGetService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Leitura dos tickets de descarga de uma carga, mais recente primeiro. Devolve
/// <see cref="IQueryable{T}"/> para o OData ainda poder aplicar $filter/$orderby/$top/$expand.
/// </summary>
public class ShipmentLoadDischargesGetService(AppDbContext context)
{
    public IQueryable<ShipmentLoadDischarge> QueryAll(Guid shipmentLoadKey) =>
        context.ShipmentLoadsDischarges
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey)
            .OrderByDescending(x => x.DischargeDate)
            .ThenByDescending(x => x.CreatedAt);
}
```

- [ ] **Step 7: Rodar e confirmar que passa**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadDischargesServiceTests`
Expected: PASS, 13 casos.

- [ ] **Step 8: Registrar na DI**

Modify `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`:

```csharp
        services.AddScoped<ShipmentLoadDischargesCreateService>();
        services.AddScoped<ShipmentLoadDischargesUpdateService>();
        services.AddScoped<ShipmentLoadDischargesDeleteService>();
        services.AddScoped<ShipmentLoadDischargesGetService>();
```

- [ ] **Step 9: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadDischarge*.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargesServiceTests.cs
git add SiagroB1.Domain/Entities/ShipmentLoadChangeLogFields.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(shipment): registrar, alterar e excluir ticket de descarga da carga

O ticket aponta obrigatoriamente para uma linha de documento de saida da
propria carga, e o par nota/item e validado no servidor: a tabela guarda as
duas chaves e nada impede a tela de mandar combinacao inconsistente.

Atencao: nao existe guard de entrega encerrada, e a ausencia e deliberada.
O ticket entra em qualquer estado da conferencia porque nao escreve no numero
conferido; barrar deixaria a Logistica sem onde lancar o documento que libera
o pagamento do frete.

Refs: GAC-1171"
```

---

### Task 4: Anexos da carga

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadAttachmentsCreateService.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadAttachmentsGetService.cs`
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadAttachmentsDeleteService.cs`
- Create: `SiagroB1.Domain/Dtos/ShipmentLoadAttachmentDto.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadAttachmentsServiceTests.cs`

**Interfaces:**
- Consumes: entidades da Task 1.
- Produces:
  - `ShipmentLoadAttachmentsCreateService.SaveAsync(Guid loadKey, ShipmentLoadAttachment attachment) → Task<ShipmentLoadAttachment>` (devolve a entidade já com `Key`, para o diálogo de descarga ligar o ticket ao arquivo)
  - `ShipmentLoadAttachmentsGetService.ListByLoad(Guid loadKey) → IEnumerable<ShipmentLoadAttachmentDto>` e `GetByKey(Guid key) → Task<ShipmentLoadAttachment?>`
  - `ShipmentLoadAttachmentsDeleteService.ExecuteAsync(Guid attachmentKey, string userName) → Task`
  - `ShipmentLoadAttachmentDto` (`Key`, `AttachmentType`, `Description`, `FileName`, `CreatedBy`, `CreatedAt`)

- [ ] **Step 1: Escrever os testes que falham**

Create `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadAttachmentsServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Anexos da carga: gravação com tipo, listagem sem o binário e a trava de exclusão de anexo
/// ainda referenciado por um ticket.
/// </summary>
public class ShipmentLoadAttachmentsServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadAttachmentsCreateService CreateService() => new(
        _db.Context, NullLogger<ShipmentLoadAttachmentsCreateService>.Instance);

    private ShipmentLoadAttachmentsGetService GetService() => new(_db.Context);

    private ShipmentLoadAttachmentsDeleteService DeleteService() => new(
        _db.Context, NullLogger<ShipmentLoadAttachmentsDeleteService>.Instance);

    private async Task<ShipmentLoad> SeedLoadAsync()
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = ShipmentLoadStatus.Invoiced,
        };

        _db.Context.ShipmentLoads.Add(load);
        await _db.Context.SaveChangesAsync();
        return load;
    }

    private static ShipmentLoadAttachment NewAttachment(
        ShipmentLoadAttachmentType type = ShipmentLoadAttachmentType.DischargeTicket) => new()
    {
        AttachmentType = type,
        Description = "Ticket de descarga 1234",
        FileName = "ticket.pdf",
        ContentType = "application/pdf",
        FileData = [1, 2, 3, 4],
        CreatedAt = DateTime.Now,
        CreatedBy = "paulo",
    };

    [Fact]
    public async Task Save_stores_the_file_with_its_type()
    {
        var load = await SeedLoadAsync();

        var saved = await CreateService().SaveAsync(load.Key!.Value, NewAttachment());

        Assert.NotNull(saved.Key);
        Assert.Equal(load.Key, saved.ShipmentLoadKey);
        Assert.Equal(ShipmentLoadAttachmentType.DischargeTicket, saved.AttachmentType);
        Assert.Equal(4, saved.FileData.Length);
    }

    [Fact]
    public async Task Save_refuses_an_unknown_load()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService().SaveAsync(Guid.NewGuid(), NewAttachment()));
    }

    [Fact]
    public async Task List_projects_without_the_binary()
    {
        var load = await SeedLoadAsync();
        await CreateService().SaveAsync(load.Key!.Value, NewAttachment());

        var rows = GetService().ListByLoad(load.Key!.Value).ToList();

        var row = Assert.Single(rows);
        Assert.Equal("ticket.pdf", row.FileName);
        Assert.Equal(ShipmentLoadAttachmentType.DischargeTicket, row.AttachmentType);
    }

    [Fact]
    public async Task Delete_removes_a_free_attachment()
    {
        var load = await SeedLoadAsync();
        var saved = await CreateService().SaveAsync(load.Key!.Value, NewAttachment());

        await DeleteService().ExecuteAsync(saved.Key!.Value, "paulo");

        Assert.Empty(_db.Context.ShipmentLoadsAttachments);
    }

    [Fact]
    public async Task Delete_refuses_an_attachment_still_linked_to_a_ticket()
    {
        // Sem essa trava a FK estoura com erro 547 e o usuário vê um 500 sem explicação.
        var load = await SeedLoadAsync();
        var saved = await CreateService().SaveAsync(load.Key!.Value, NewAttachment());

        _db.Context.ShipmentLoadsDischarges.Add(new ShipmentLoadDischarge
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = load.Key,
            TicketNumber = "T-1",
            DischargedQuantity = 100m,
            AttachmentKey = saved.Key,
        });
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<DefaultException>(
            () => DeleteService().ExecuteAsync(saved.Key!.Value, "paulo"));

        Assert.Contains("descarga", error.Message);
        Assert.Single(_db.Context.ShipmentLoadsAttachments);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadAttachmentsServiceTests`
Expected: FAIL na compilação — os serviços e o DTO não existem.

- [ ] **Step 3: Criar o DTO**

Create `SiagroB1.Domain/Dtos/ShipmentLoadAttachmentDto.cs`:

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Linha do grid de anexos da carga — sem o binário.</summary>
public class ShipmentLoadAttachmentDto
{
    public Guid? Key { get; set; }
    public ShipmentLoadAttachmentType AttachmentType { get; set; }
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
}
```

- [ ] **Step 4: Implementar os três serviços**

Create `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadAttachmentsCreateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Anexa um documento à carga (GAC-1171). Devolve a entidade gravada porque o diálogo de registro
/// de descarga precisa da chave do anexo para ligar o ticket ao arquivo na mesma operação.
/// </summary>
public class ShipmentLoadAttachmentsCreateService(
    AppDbContext context,
    ILogger<ShipmentLoadAttachmentsCreateService> logger)
{
    public async Task<ShipmentLoadAttachment> SaveAsync(
        Guid loadKey, ShipmentLoadAttachment attachment)
    {
        try
        {
            if (!await context.ShipmentLoads.AnyAsync(x => x.Key == loadKey))
                throw new NotFoundException("Carga não encontrada.");

            attachment.ShipmentLoadKey = loadKey;
            attachment.CreatedAt ??= DateTime.Now;

            await context.AddAsync(attachment);
            await context.SaveChangesAsync();

            return attachment;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
```

Create `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadAttachmentsGetService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

public class ShipmentLoadAttachmentsGetService(AppDbContext context)
{
    // Projeção sem FileData: a lista não arrasta o binário.
    public IEnumerable<ShipmentLoadAttachmentDto> ListByLoad(Guid loadKey) =>
        context.ShipmentLoadsAttachments
            .AsNoTracking()
            .Where(x => x.ShipmentLoadKey == loadKey)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new ShipmentLoadAttachmentDto
            {
                Key = x.Key,
                AttachmentType = x.AttachmentType,
                Description = x.Description,
                FileName = x.FileName,
                CreatedBy = x.CreatedBy,
                CreatedAt = x.CreatedAt,
            })
            .ToList();

    public Task<ShipmentLoadAttachment?> GetByKey(Guid key) =>
        context.ShipmentLoadsAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
}
```

Create `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadAttachmentsDeleteService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Exclui um anexo da carga (GAC-1171), desde que nenhum ticket de descarga aponte para ele.
/// </summary>
public class ShipmentLoadAttachmentsDeleteService(
    AppDbContext context,
    ILogger<ShipmentLoadAttachmentsDeleteService> logger)
{
    public async Task ExecuteAsync(Guid attachmentKey, string userName)
    {
        try
        {
            var attachment = await context.ShipmentLoadsAttachments
                .FirstOrDefaultAsync(x => x.Key == attachmentKey)
                ?? throw new NotFoundException("Anexo não encontrado.");

            // Guard antes da FK: sem ele o banco devolve erro 547 e o usuário vê um 500 de corpo
            // vazio, sem saber que o problema é o vínculo com o ticket.
            if (await context.ShipmentLoadsDischarges.AnyAsync(x => x.AttachmentKey == attachmentKey))
                throw new DefaultException(
                    "Este anexo está vinculado a um registro de descarga. " +
                    "Exclua o registro de descarga antes de excluir o anexo.");

            context.ShipmentLoadsAttachments.Remove(attachment);
            await context.SaveChangesAsync();

            logger.LogInformation(
                "Anexo {Key} da carga excluído por {User}.", attachmentKey, userName);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadAttachmentsServiceTests`
Expected: PASS, 5 testes.

- [ ] **Step 6: Registrar na DI**

Modify `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`:

```csharp
        services.AddScoped<ShipmentLoadAttachmentsCreateService>();
        services.AddScoped<ShipmentLoadAttachmentsGetService>();
        services.AddScoped<ShipmentLoadAttachmentsDeleteService>();
```

- [ ] **Step 7: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadAttachments*.cs SiagroB1.Domain/Dtos/ShipmentLoadAttachmentDto.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadAttachmentsServiceTests.cs
git add SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs
git commit -m "feat(shipment): anexar documentos tipados a carga

Tickets de carga e de descarga, nota fiscal e conhecimento de frete passam a
ter onde ficar. O tipo e lista fechada porque o objetivo e poder perguntar
'falta o ticket de descarga?', o que descricao livre nao responde.

Atencao: excluir anexo ainda referenciado por um ticket e barrado no servico.
Deixar a FK barrar devolveria erro 547 como 500 de corpo vazio.

Refs: GAC-1171"
```

---

### Task 5: Superfície OData — EDM, controllers e rota de navegação

**Files:**
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (EntitySet na região da linha 200; actions/functions na região da linha 648)
- Create: `SiagroB1.Web/Controllers/ShipmentLoadsDischargesController.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsDischargeCreateController.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsDischargeUpdateController.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsDischargeDeleteController.cs`
- Create: `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsAttachmentUploadController.cs`
- Create: `SiagroB1.Web/Functions/ShipmentLoads/ShipmentLoadsAttachmentsListController.cs`
- Create: `SiagroB1.Web/Functions/ShipmentLoads/ShipmentLoadsAttachmentsDownloadController.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargeEdmModelTests.cs`

**Interfaces:**
- Consumes: serviços das Tasks 3 e 4.
- Produces, para o frontend consumir na Task 7 e 8:
  - `GET odata/ShipmentLoads({key})/Discharges`
  - `POST odata/ShipmentLoadsDischargeCreate` — `LoadKey`, `SalesInvoiceKey`, `SalesInvoiceItemKey` (Guid), `TicketNumber` (string), `DischargeDate` (string `yyyy-MM-dd`), `Quantity` (double), `Comments` (string), `FileName`/`ContentType`/`File` (string, base64, opcionais)
  - `POST odata/ShipmentLoadsDischargeUpdate` — `Key`, `TicketNumber`, `DischargeDate`, `Quantity`, `Comments`
  - `POST odata/ShipmentLoadsDischargeDelete` — `Key`
  - `POST odata/ShipmentLoadsAttachmentUpload` — `LoadKey`, `AttachmentType` (string), `Description`, `File`, `FileName`, `ContentType`
  - `GET odata/ShipmentLoadsAttachmentsList(LoadKey={key})`
  - `GET odata/ShipmentLoadsAttachmentsDownload(Key={key})`

`DischargeDate` viaja como **string** e `Quantity` como **double**: `Edm.Date`/`Edm.Decimal` em parâmetro de action já custaram 400 sem nome de campo neste projeto, e `double` tem precedente provado em `ShipmentLoadsRefuse`.

- [ ] **Step 1: Escrever o teste de EDM que falha**

Create `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargeEdmModelTests.cs`, no molde de `ShipmentLoadEdmModelTests`:

```csharp
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// O EDM é contrato com a tela: action ou propriedade que não está aqui devolve 404/400 no
/// navegador, e nenhum teste de serviço acusa.
/// </summary>
public class ShipmentLoadDischargeEdmModelTests
{
    // Mesmo helper de ShipmentLoadEdmModelTests: o EDM real, montado pelo
    // ConfigureODataEntities que o Program usa.
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();

        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("ShipmentLoadsDischargeCreate")]
    [InlineData("ShipmentLoadsDischargeUpdate")]
    [InlineData("ShipmentLoadsDischargeDelete")]
    [InlineData("ShipmentLoadsAttachmentUpload")]
    [InlineData("ShipmentLoadsAttachmentDelete")]
    public void Declares_the_write_actions(string name)
    {
        Assert.NotEmpty(Model().SchemaElements.OfType<IEdmAction>().Where(a => a.Name == name));
    }

    [Theory]
    [InlineData("ShipmentLoadsAttachmentsList")]
    [InlineData("ShipmentLoadsAttachmentsDownload")]
    public void Declares_the_read_functions(string name)
    {
        Assert.NotEmpty(Model().SchemaElements.OfType<IEdmFunction>().Where(f => f.Name == name));
    }

    [Fact]
    public void Declares_the_discharges_entity_set()
    {
        Assert.NotNull(Model().EntityContainer.FindEntitySet("ShipmentLoadsDischarges"));
    }

    [Fact]
    public void Exposes_the_derived_ticket_quantities()
    {
        var load = (IEdmStructuredType)Model().FindDeclaredType(typeof(ShipmentLoad).FullName);
        Assert.NotNull(load.FindProperty(nameof(ShipmentLoad.DischargedQuantity)));

        var item = (IEdmStructuredType)Model().FindDeclaredType(typeof(SalesInvoiceItem).FullName);
        Assert.NotNull(item.FindProperty(nameof(SalesInvoiceItem.TicketDeliveredQuantity)));
    }

    [Fact]
    public void Create_action_takes_the_ticket_parameters()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>()
            .Single(a => a.Name == "ShipmentLoadsDischargeCreate");

        var names = action.Parameters.Select(p => p.Name).ToHashSet();
        Assert.Contains("LoadKey", names);
        Assert.Contains("SalesInvoiceKey", names);
        Assert.Contains("SalesInvoiceItemKey", names);
        Assert.Contains("TicketNumber", names);
        Assert.Contains("DischargeDate", names);
        Assert.Contains("Quantity", names);
    }
}
```

Se `ODataConfigurations.GetEdmModel()` tiver outra assinatura, ajuste a chamada conforme `ShipmentLoadEdmModelTests` já faz — leia esse arquivo antes.

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadDischargeEdmModelTests`
Expected: FAIL — as actions e o EntitySet não estão no modelo.

- [ ] **Step 3: Declarar no EDM**

Modify `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`. Junto dos EntitySets da carga (região da linha 200):

```csharp
        modelBuilder.EntitySet<ShipmentLoadDischarge>("ShipmentLoadsDischarges");
```

Junto das actions da carga (região da linha 648):

```csharp
        // DischargeDate como string e Quantity como double de propósito: Edm.Date e Edm.Decimal
        // em parâmetro de action já devolveram 400 sem nomear o campo. double tem precedente
        // PROVADO em ShipmentLoadsRefuse.
        var shipmentLoadsDischargeCreate = modelBuilder.Action("ShipmentLoadsDischargeCreate");
        shipmentLoadsDischargeCreate.Parameter<Guid>("LoadKey");
        shipmentLoadsDischargeCreate.Parameter<Guid>("SalesInvoiceKey");
        shipmentLoadsDischargeCreate.Parameter<Guid>("SalesInvoiceItemKey");
        shipmentLoadsDischargeCreate.Parameter<string>("TicketNumber");
        shipmentLoadsDischargeCreate.Parameter<string>("DischargeDate");
        shipmentLoadsDischargeCreate.Parameter<double>("Quantity");
        shipmentLoadsDischargeCreate.Parameter<string>("Comments");
        shipmentLoadsDischargeCreate.Parameter<string>("File");
        shipmentLoadsDischargeCreate.Parameter<string>("FileName");
        shipmentLoadsDischargeCreate.Parameter<string>("ContentType");
        shipmentLoadsDischargeCreate.Returns<IActionResult>();

        var shipmentLoadsDischargeUpdate = modelBuilder.Action("ShipmentLoadsDischargeUpdate");
        shipmentLoadsDischargeUpdate.Parameter<Guid>("Key");
        shipmentLoadsDischargeUpdate.Parameter<string>("TicketNumber");
        shipmentLoadsDischargeUpdate.Parameter<string>("DischargeDate");
        shipmentLoadsDischargeUpdate.Parameter<double>("Quantity");
        shipmentLoadsDischargeUpdate.Parameter<string>("Comments");
        shipmentLoadsDischargeUpdate.Returns<IActionResult>();

        var shipmentLoadsDischargeDelete = modelBuilder.Action("ShipmentLoadsDischargeDelete");
        shipmentLoadsDischargeDelete.Parameter<Guid>("Key");
        shipmentLoadsDischargeDelete.Returns<IActionResult>();

        var shipmentLoadsAttachmentUpload = modelBuilder.Action("ShipmentLoadsAttachmentUpload");
        shipmentLoadsAttachmentUpload.Parameter<Guid>("LoadKey");
        shipmentLoadsAttachmentUpload.Parameter<string>("AttachmentType");
        shipmentLoadsAttachmentUpload.Parameter<string>("Description");
        shipmentLoadsAttachmentUpload.Parameter<string>("File");
        shipmentLoadsAttachmentUpload.Parameter<string>("FileName");
        shipmentLoadsAttachmentUpload.Parameter<string>("ContentType");
        shipmentLoadsAttachmentUpload.Returns<IActionResult>();

        var shipmentLoadsAttachmentDelete = modelBuilder.Action("ShipmentLoadsAttachmentDelete");
        shipmentLoadsAttachmentDelete.Parameter<Guid>("Key");
        shipmentLoadsAttachmentDelete.Returns<IActionResult>();

        var shipmentLoadsAttachmentsList = modelBuilder.Function("ShipmentLoadsAttachmentsList");
        shipmentLoadsAttachmentsList.Parameter<Guid>("LoadKey");
        shipmentLoadsAttachmentsList.Returns<IActionResult>();

        var shipmentLoadsAttachmentsDownload = modelBuilder.Function("ShipmentLoadsAttachmentsDownload");
        shipmentLoadsAttachmentsDownload.Parameter<Guid>("Key");
        shipmentLoadsAttachmentsDownload.Returns<IActionResult>();
```

- [ ] **Step 4: Criar o controller de leitura com a rota de navegação**

Create `SiagroB1.Web/Controllers/ShipmentLoadsDischargesController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Tickets de descarga da carga: somente leitura aqui. A escrita passa pelas actions
/// ShipmentLoadsDischarge{Create,Update,Delete}, que carimbam o autor, recalculam as somas e
/// gravam o log na mesma transação.
/// </summary>
/// <remarks>
/// ⚠️ As duas rotas são declaradas à mão. Navegação e late property só respondem onde a rota foi
/// escrita: sem isso o grid recebe 404 e a aba aparece vazia sem erro visível.
/// </remarks>
public class ShipmentLoadsDischargesController(
    ShipmentLoadDischargesGetService getService) : ODataController
{
    [HttpGet("odata/ShipmentLoads({key:guid})/Discharges")]
    [HttpGet("odata/ShipmentLoads/{key:guid}/Discharges")]
    [EnableQuery]
    public ActionResult<IEnumerable<ShipmentLoadDischarge>> Get([FromRoute] Guid key)
    {
        return Ok(getService.QueryAll(key));
    }
}
```

- [ ] **Step 5: Criar os três controllers de escrita da descarga**

Create `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsDischargeCreateController.cs`:

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsDischargeCreateController(
    ShipmentLoadDischargesCreateService service,
    ShipmentLoadAttachmentsCreateService attachments) : ODataController
{
    [HttpPost("odata/ShipmentLoadsDischargeCreate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            // parameters chega NULO quando nenhum parametro do EDM e enviado.
            if (parameters is null ||
                !parameters.TryGetValue("LoadKey", out var loadKeyObj) || loadKeyObj is null ||
                !parameters.TryGetValue("SalesInvoiceKey", out var invoiceKeyObj) || invoiceKeyObj is null ||
                !parameters.TryGetValue("SalesInvoiceItemKey", out var itemKeyObj) || itemKeyObj is null)
                return BadRequest("Carga, documento de saída e item são obrigatórios.");

            parameters.TryGetValue("TicketNumber", out var ticketObj);
            parameters.TryGetValue("DischargeDate", out var dateObj);
            parameters.TryGetValue("Quantity", out var quantityObj);
            parameters.TryGetValue("Comments", out var commentsObj);
            parameters.TryGetValue("File", out var fileObj);
            parameters.TryGetValue("FileName", out var fileNameObj);
            parameters.TryGetValue("ContentType", out var contentTypeObj);

            var loadKey = (Guid) loadKeyObj;
            var userName = User.Identity?.Name ?? "Unknown";

            // Anexo primeiro: o ticket guarda a chave dele, e gravar na ordem inversa deixaria o
            // vínculo para um segundo SaveChanges que pode não acontecer.
            Guid? attachmentKey = null;

            if (fileObj is string base64 && base64.Length > 0)
            {
                var saved = await attachments.SaveAsync(loadKey, new ShipmentLoadAttachment
                {
                    AttachmentType = ShipmentLoadAttachmentType.DischargeTicket,
                    Description = $"Ticket de descarga {ticketObj as string ?? string.Empty}".Trim(),
                    FileName = fileNameObj?.ToString() ?? "ticket",
                    ContentType = contentTypeObj?.ToString() ?? "application/octet-stream",
                    FileData = Convert.FromBase64String(base64),
                    CreatedAt = DateTime.Now,
                    CreatedBy = userName,
                });

                attachmentKey = saved.Key;
            }

            await service.ExecuteAsync(
                loadKey,
                (Guid) invoiceKeyObj,
                (Guid) itemKeyObj,
                ticketObj as string,
                ParseDate(dateObj),
                Convert.ToDecimal(quantityObj ?? 0d, CultureInfo.InvariantCulture),
                commentsObj as string,
                attachmentKey,
                userName);

            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }

    /// <summary>
    /// A data viaja como string "yyyy-MM-dd". Parâmetro string do EDM é anulável, então o nulo
    /// cai no dia de hoje em vez de estourar.
    /// </summary>
    private static DateTime ParseDate(object? value) =>
        value is string text && DateTime.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : DateTime.Now.Date;
}
```

Create `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsDischargeUpdateController.cs`:

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsDischargeUpdateController(
    ShipmentLoadDischargesUpdateService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsDischargeUpdate")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null ||
                !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Registro de descarga não informado.");

            parameters.TryGetValue("TicketNumber", out var ticketObj);
            parameters.TryGetValue("DischargeDate", out var dateObj);
            parameters.TryGetValue("Quantity", out var quantityObj);
            parameters.TryGetValue("Comments", out var commentsObj);

            await service.ExecuteAsync(
                (Guid) keyObj,
                ticketObj as string,
                dateObj is string text && DateTime.TryParse(
                    text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed.Date
                    : DateTime.Now.Date,
                Convert.ToDecimal(quantityObj ?? 0d, CultureInfo.InvariantCulture),
                commentsObj as string,
                User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
```

Create `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsDischargeDeleteController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsDischargeDeleteController(
    ShipmentLoadDischargesDeleteService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsDischargeDelete")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null ||
                !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Registro de descarga não informado.");

            await service.ExecuteAsync((Guid) keyObj, User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
```

- [ ] **Step 6: Criar os controllers de anexo**

Create `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsAttachmentUploadController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsAttachmentUploadController(
    ShipmentLoadAttachmentsCreateService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsAttachmentUpload")]
    public async Task<ActionResult> Upload([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null ||
            !parameters.TryGetValue("LoadKey", out var keyObj) || keyObj is null ||
            !parameters.TryGetValue("File", out var fileObj) || fileObj is null ||
            !parameters.TryGetValue("Description", out var descriptionObj) || descriptionObj is null)
            return BadRequest("LoadKey, File e Description são obrigatórios.");

        parameters.TryGetValue("AttachmentType", out var typeObj);
        parameters.TryGetValue("FileName", out var fileNameObj);
        parameters.TryGetValue("ContentType", out var contentTypeObj);

        try
        {
            await service.SaveAsync((Guid) keyObj, new ShipmentLoadAttachment
            {
                AttachmentType = Enum.TryParse<ShipmentLoadAttachmentType>(
                    typeObj as string, out var parsedType)
                    ? parsedType
                    : ShipmentLoadAttachmentType.Other,
                Description = descriptionObj.ToString()!,
                FileName = fileNameObj?.ToString() ?? "anexo",
                ContentType = contentTypeObj?.ToString() ?? "application/octet-stream",
                FileData = Convert.FromBase64String(fileObj.ToString()!),
                CreatedAt = DateTime.Now,
                CreatedBy = User.Identity?.Name ?? "unknown",
            });

            return Ok();
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

Create `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsAttachmentDeleteController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

public class ShipmentLoadsAttachmentDeleteController(
    ShipmentLoadAttachmentsDeleteService service) : ODataController
{
    [HttpPost("odata/ShipmentLoadsAttachmentDelete")]
    public async Task<IActionResult> Post(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            if (parameters is null ||
                !parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Anexo não informado.");

            await service.ExecuteAsync((Guid) keyObj, User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is NotFoundException or KeyNotFoundException)
                return NotFound(e.Message);

            if (e is DefaultException or BusinessException or ApplicationException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }
}
```

Create `SiagroB1.Web/Functions/ShipmentLoads/ShipmentLoadsAttachmentsListController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;

namespace SiagroB1.Web.Functions.ShipmentLoads;

public class ShipmentLoadsAttachmentsListController(
    ShipmentLoadAttachmentsGetService service) : ODataController
{
    [HttpGet("odata/ShipmentLoadsAttachmentsList(LoadKey={key})")]
    public ActionResult List([FromRoute] Guid key) => Ok(service.ListByLoad(key));
}
```

Create `SiagroB1.Web/Functions/ShipmentLoads/ShipmentLoadsAttachmentsDownloadController.cs`, espelhando `WarehouseReconciliationsAttachmentsDownloadController` (leia-o antes e reproduza o mesmo tipo de retorno de arquivo):

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;

namespace SiagroB1.Web.Functions.ShipmentLoads;

public class ShipmentLoadsAttachmentsDownloadController(
    ShipmentLoadAttachmentsGetService service) : ODataController
{
    [HttpGet("odata/ShipmentLoadsAttachmentsDownload(Key={key})")]
    public async Task<ActionResult> Download([FromRoute] Guid key)
    {
        var attachment = await service.GetByKey(key);

        if (attachment is null)
            return NotFound("Anexo não encontrado.");

        return File(attachment.FileData, attachment.ContentType, attachment.FileName);
    }
}
```

- [ ] **Step 7: Rodar o teste de EDM e a suíte inteira**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadDischargeEdmModelTests`
Expected: PASS, 9 casos.

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS, sem regressão nas suítes de carga, contrato e documento de saída.

- [ ] **Step 8: Subir o Web e provar as rotas**

Run: `dotnet build SiagroB1.sln` e suba `SiagroB1.Web` no profile `yktb`. Confira em `https://localhost:50000/odata/$metadata` que os seis nomes novos aparecem. Isso é smoke test de rota, não a verificação da feature — essa é a Task 9.

- [ ] **Step 9: Commit**

```bash
git add SiagroB1.Web/Controllers/ShipmentLoadsDischargesController.cs SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsDischarge*.cs SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsAttachmentUploadController.cs SiagroB1.Web/Functions/ShipmentLoads/
git add SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadDischargeEdmModelTests.cs
git commit -m "feat(shipment): expor descargas e anexos da carga no OData

Leitura da colecao por rota de navegacao declarada a mao, escrita por actions
e anexos no molde da Conferencia de Armazem, com o binario fora da leitura
normal.

Atencao: DischargeDate viaja como string e Quantity como double. Edm.Date e
Edm.Decimal em parametro de action devolvem 400 sem nomear o campo, e
ODataActionParameters chega nulo quando nenhum parametro e enviado.

Refs: GAC-1171"
```

---

### Task 6: Exclusão da carga apaga as filhas novas

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsDeleteService.cs:67-89`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsDeleteServiceTests.cs` (adicionar caso)

**Interfaces:**
- Consumes: `AppDbContext.ShipmentLoadsDischarges`, `AppDbContext.ShipmentLoadsAttachments`.
- Produces: nada novo.

- [ ] **Step 1: Escrever o teste que falha**

Modify `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsDeleteServiceTests.cs`, adicionando (leia o arquivo e reuse os helpers de seed que ele já tem, em vez de criar outros):

```csharp
    [Fact]
    public async Task Delete_removes_discharges_and_attachments()
    {
        // ⚠️ O InMemory NÃO reproduz o erro 547: este teste garante a INTENÇÃO, e a prova real é
        // excluir uma carga com ticket e anexo no banco SQL Server, na verificação.
        var load = await SeedLoadAsync(ShipmentLoadStatus.Planned);

        var attachment = new ShipmentLoadAttachment
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = load.Key,
            AttachmentType = ShipmentLoadAttachmentType.DischargeTicket,
            Description = "Ticket",
            FileName = "t.pdf",
            ContentType = "application/pdf",
            FileData = [1],
            CreatedAt = DateTime.Now,
        };

        _db.Context.ShipmentLoadsAttachments.Add(attachment);
        _db.Context.ShipmentLoadsDischarges.Add(new ShipmentLoadDischarge
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = load.Key,
            TicketNumber = "T-1",
            DischargedQuantity = 100m,
            AttachmentKey = attachment.Key,
        });
        await _db.Context.SaveChangesAsync();

        await DeleteService().ExecuteAsync(load.Key!.Value, "paulo");

        Assert.Empty(_db.Context.ShipmentLoadsDischarges);
        Assert.Empty(_db.Context.ShipmentLoadsAttachments);
        Assert.Empty(_db.Context.ShipmentLoads);
    }
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadsDeleteServiceTests`
Expected: FAIL — as filhas novas sobrevivem à exclusão da carga.

- [ ] **Step 3: Incluir as filhas na exclusão, na ordem certa**

Modify `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsDeleteService.cs`. Junto das consultas das linhas 67–75:

```csharp
        var discharges = await db.Context.ShipmentLoadsDischarges
            .Where(x => x.ShipmentLoadKey == key)
            .ToListAsync();

        var attachments = await db.Context.ShipmentLoadsAttachments
            .Where(x => x.ShipmentLoadKey == key)
            .ToListAsync();
```

E no bloco de remoção (linhas 86–89), **antes** de `db.Context.ShipmentLoads.Remove(load)`:

```csharp
            // Ordem obrigatória: a descarga referencia o anexo, então o anexo sai depois. O
            // inverso dispara erro 547 e o InMemory dos testes não acusa.
            db.Context.ShipmentLoadsDischarges.RemoveRange(discharges);
            db.Context.ShipmentLoadsAttachments.RemoveRange(attachments);
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~ShipmentLoadsDeleteServiceTests`
Expected: PASS.

- [ ] **Step 5: Provar contra o SQL Server**

Suba `SiagroB1.Web` no profile `yktb`, crie uma carga planejada, anexe um documento, registre um ticket e exclua a carga pela tela. Expected: exclusão sem erro 547. Sem este passo o caso fica apenas declarado, não provado.

- [ ] **Step 6: Commit**

```bash
git add SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsDeleteService.cs SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsDeleteServiceTests.cs
git commit -m "fix(shipment): apagar descargas e anexos ao excluir a carga

Tabela filha nova sem tratamento no delete do pai quebra com erro 547, e o
teste InMemory passa verde mesmo assim.

Atencao: a ordem importa. A descarga referencia o anexo, entao o anexo sai
depois; o inverso volta a estourar a FK.

Refs: GAC-1171"
```

---

### Task 7: Aba Descargas na carga

**Files:**
- Create: `webapp/view/shipmentLoads/fragments/ShipmentLoadDischarges.fragment.xml`
- Create: `webapp/view/shipmentLoads/fragments/ShipmentLoadDischargeDialog.fragment.xml`
- Modify: `webapp/view/shipmentLoads/Detail.view.xml` (nova `ObjectPageSection` depois de "Documentos de Saída", que termina na linha 373)
- Modify: `webapp/view/shipmentLoads/fragments/LoadCard.fragment.xml` (peso descarregado no cabeçalho)
- Modify: `webapp/controller/shipmentLoads/BaseController.ts` (handlers)
- Modify: `webapp/controller/shipmentLoads/Detail.controller.ts` (`refreshAll` ganha as tabelas novas)
- Modify: `webapp/model/formatter.ts` (rótulo do tipo de anexo, usado também na Task 8)

**Interfaces:**
- Consumes: as actions e a rota de navegação da Task 5.
- Produces, para a Task 8 reusar: `BaseController.loadAttachmentBase64(uploader: FileUploader): Promise<{ File: string; FileName: string; ContentType: string } | null>` e `BaseController.currentLoadKey(): string`.

> **Por que os handlers vão no `BaseController`:** `Detail.controller.ts` já tem 759 linhas. `shipmentLoads/BaseController.ts` é declarado no próprio arquivo como "ponto de extensão" da Montagem de Carga e hoje está vazio; é onde `warehouseReconciliations/BaseController.ts` guarda exatamente esse tipo de lógica. Nada existente é movido.

- [ ] **Step 1: Criar o fragmento do grid**

Create `webapp/view/shipmentLoads/fragments/ShipmentLoadDischarges.fragment.xml`:

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:t="sap.ui.table">
	<!-- $$ownRequest: sem ele a coleção vira $expand do pai e o refresh() depois de gravar não
	     funciona. $expand de um nível na nota, porque o número dela é atribuído depois e
	     denormalizar aqui daria um snapshot velho. -->
	<t:Table
		id="loadDischargesTable"
		selectionMode="Single"
		selectionBehavior="Row"
		class="sapUiSizeCondensed"
		visibleRowCount="6"
		rows="{ path: 'Discharges', parameters: { '$$ownRequest': true, '$expand': 'SalesInvoice($select=Key,InvoiceNumber),SalesInvoiceItem($select=Key,ItemCode,ItemName)' } }">
		<t:extension>
			<OverflowToolbar>
				<Title text="Descargas"/>
				<ToolbarSpacer/>
				<Button
					type="Transparent"
					text="Registrar Descarga"
					icon="sap-icon://add"
					visible="{= ${path: 'Status', targetType: 'any'} !== 'Cancelled' &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Returned' }"
					press=".onAddDischarge"/>
				<Button
					type="Transparent"
					text="Editar"
					icon="sap-icon://edit"
					visible="{= ${path: 'Status', targetType: 'any'} !== 'Cancelled' &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Returned' }"
					press=".onEditDischarge"/>
				<Button
					type="Transparent"
					text="Excluir"
					icon="sap-icon://delete"
					visible="{= ${path: 'Status', targetType: 'any'} !== 'Cancelled' &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Returned' }"
					press=".onRemoveDischarge"/>
			</OverflowToolbar>
		</t:extension>
		<t:columns>
			<t:Column label="Ticket" width="10rem">
				<t:template><Text text="{TicketNumber}" wrapping="false"/></t:template>
			</t:Column>
			<t:Column label="Data da Descarga" width="9rem">
				<t:template>
					<Text wrapping="false" text="{ path: 'DischargeDate', targetType: 'any', formatter: '.formatter.formatDate' }"/>
				</t:template>
			</t:Column>
			<t:Column label="Documento de Saída" width="11rem">
				<t:template><Text text="{SalesInvoice/InvoiceNumber}" wrapping="false"/></t:template>
			</t:Column>
			<t:Column label="Produto" width="18rem">
				<t:template><Text text="({SalesInvoiceItem/ItemCode}) {SalesInvoiceItem/ItemName}" wrapping="false"/></t:template>
			</t:Column>
			<t:Column label="Peso Descarregado" hAlign="End" width="10rem">
				<t:template>
					<ObjectNumber
						textAlign="End"
						number="{ path: 'DischargedQuantity', type: 'sap.ui.model.type.Float', formatOptions: { decimals: 3, decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' } }"/>
				</t:template>
			</t:Column>
			<t:Column label="Anexo" hAlign="Center" width="6rem">
				<t:template>
					<Button
						type="Transparent"
						icon="sap-icon://attachment"
						tooltip="Baixar o ticket anexado"
						visible="{= !!${path: 'AttachmentKey', targetType: 'any'} }"
						press=".onDownloadDischargeAttachment"/>
				</t:template>
			</t:Column>
			<t:Column label="Observação" width="20rem">
				<t:template><Text text="{Comments}" wrapping="false"/></t:template>
			</t:Column>
			<t:Column label="Registrado por" width="12rem">
				<t:template><Text text="{CreatedBy}" wrapping="false"/></t:template>
			</t:Column>
		</t:columns>
	</t:Table>
</core:FragmentDefinition>
```

O número da nota é `SalesInvoice.InvoiceNumber` — conferido no código, não é `DocumentNumber` nem `TaxDocumentNumber` (esse é o da NF-e). Coluna sem `$select`/`$expand` correspondente vem vazia sem erro.

- [ ] **Step 2: Criar o fragmento do diálogo**

Create `webapp/view/shipmentLoads/fragments/ShipmentLoadDischargeDialog.fragment.xml`:

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form"
	xmlns:u="sap.ui.unified">
	<Dialog
		id="shipmentLoadDischargeDialog"
		title="{viewModel>/dischargeDialog/title}"
		contentWidth="34rem">
		<content>
			<f:Form editable="true">
				<f:layout>
					<f:ColumnLayout columnsM="1" columnsL="1" columnsXL="1"/>
				</f:layout>
				<f:formContainers>
					<f:FormContainer>
						<f:formElements>
							<f:FormElement label="Nº do Ticket">
								<f:fields>
									<Input value="{viewModel>/dischargeDialog/ticketNumber}" maxLength="50"/>
								</f:fields>
							</f:FormElement>
							<f:FormElement label="Data da Descarga">
								<f:fields>
									<DatePicker
										value="{viewModel>/dischargeDialog/dischargeDate}"
										valueFormat="yyyy-MM-dd"
										displayFormat="dd/MM/yyyy"/>
								</f:fields>
							</f:FormElement>
							<f:FormElement label="Documento de Saída">
								<f:fields>
									<!-- Itens vindos de JSONModel estático, carregado ANTES de abrir: Select com
									     selectedKey sobre coleção OData renderiza vazio com o estado interno certo. -->
									<Select
										forceSelection="false"
										selectedKey="{viewModel>/dischargeDialog/salesInvoiceKey}"
										items="{viewModel>/dischargeDialog/invoices}"
										change=".onDischargeInvoiceChange"
										enabled="{= !${viewModel>/dischargeDialog/key} }">
										<core:ListItem key="{viewModel>Key}" text="{viewModel>Text}"/>
									</Select>
								</f:fields>
							</f:FormElement>
							<f:FormElement label="Item">
								<f:fields>
									<Select
										forceSelection="false"
										selectedKey="{viewModel>/dischargeDialog/salesInvoiceItemKey}"
										items="{viewModel>/dischargeDialog/items}"
										enabled="{= !${viewModel>/dischargeDialog/key} }">
										<core:ListItem key="{viewModel>Key}" text="{viewModel>Text}"/>
									</Select>
								</f:fields>
							</f:FormElement>
							<f:FormElement label="Peso Descarregado">
								<f:fields>
									<!-- Sem type="Number": <input type="number"> apaga valor com separador pt-BR. -->
									<Input value="{viewModel>/dischargeDialog/quantity}" textAlign="End"/>
								</f:fields>
							</f:FormElement>
							<f:FormElement label="Observação">
								<f:fields>
									<TextArea value="{viewModel>/dischargeDialog/comments}" rows="3" maxLength="500"/>
								</f:fields>
							</f:FormElement>
							<f:FormElement label="Ticket digitalizado" visible="{= !${viewModel>/dischargeDialog/key} }">
								<f:fields>
									<u:FileUploader
										id="dischargeFileUploader"
										placeholder="Selecione o arquivo do ticket"
										buttonText="Procurar"
										uploadOnChange="false"/>
								</f:fields>
							</f:FormElement>
						</f:formElements>
					</f:FormContainer>
				</f:formContainers>
			</f:Form>
		</content>
		<beginButton>
			<Button text="Gravar" type="Emphasized" press=".onConfirmDischarge"/>
		</beginButton>
		<endButton>
			<Button text="Cancelar" press=".onCloseDischargeDialog"/>
		</endButton>
	</Dialog>
</core:FragmentDefinition>
```

Nota e item ficam desabilitados na edição, espelhando o backend: mudar o destino de um ticket é excluir e registrar de novo.

⚠️ Não use `--` dentro de comentário XML: `Fragment.load` devolve nulls e nenhum gate acusa.

- [ ] **Step 3: Pendurar a seção na página de detalhe**

Modify `webapp/view/shipmentLoads/Detail.view.xml`, depois da seção "Documentos de Saída" (que fecha na linha 373):

```xml
			<uxap:ObjectPageSection titleUppercase="false" title="Descargas"
				visible="{= ${path: 'LoadType', targetType: 'any'} !== 'Removal' }">
				<uxap:subSections>
					<uxap:ObjectPageSubSection>
						<uxap:blocks>
							<core:Fragment fragmentName="siagrob1.view.shipmentLoads.fragments.ShipmentLoadDischarges" type="XML"/>
						</uxap:blocks>
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
```

Carga de Remoção não fatura, então não tem nota para o ticket apontar — a aba não aparece nela. Confirme que `core` já está declarado no `mvc:View` do arquivo; se não, adicione `xmlns:core="sap.ui.core"`.

- [ ] **Step 4: Mostrar o descarregado no cabeçalho**

Modify `webapp/view/shipmentLoads/fragments/LoadCard.fragment.xml`, ao lado do peso embarcado (leia o arquivo e siga o controle que ele já usa para `TotalQuantity`):

```xml
	<ObjectAttribute
		title="Descarregado (tickets)"
		text="{ parts: [ { path: 'DischargedQuantity', targetType: 'any' }, { path: 'TotalQuantity', targetType: 'any' } ], formatter: '.formatter.formatDischargedAgainstLoaded' }"/>
```

- [ ] **Step 5: Adicionar os formatters**

Modify `webapp/model/formatter.ts`, junto de `formatDeliveryDifference` (linha 215):

```ts
  /**
   * "39.500,000 de 40.000,000 (-500,000)" — descarregado, embarcado e a diferença. Vazio
   * enquanto nenhum ticket foi registrado: zero aqui pareceria "chegou nada".
   */
  formatDischargedAgainstLoaded: (discharged: number, loaded: number): string => {
    const dischargedValue = Number(discharged ?? 0);

    if (dischargedValue <= 0) return "";

    const loadedValue = Number(loaded ?? 0);
    const difference = dischargedValue - loadedValue;

    return `${formatter.formatDecimal3(dischargedValue)} de ${formatter.formatDecimal3(loadedValue)} (${difference > 0 ? "+" : ""}${formatter.formatDecimal3(difference)})`;
  },

  /** Rótulo pt-BR do tipo de anexo da carga. O código fica em inglês, no enum do backend. */
  formatShipmentLoadAttachmentType: (type: string): string => {
    switch (type) {
      case "LoadingTicket": return "Ticket de Carga";
      case "DischargeTicket": return "Ticket de Descarga";
      case "TaxDocument": return "Nota Fiscal";
      case "FreightDocument": return "Conhecimento de Frete";
      case "Other": return "Outro";
      default: return type ?? "";
    }
  },

  /** Número com 3 decimais em pt-BR, usado pelos formatters de peso desta tela. */
  formatDecimal3: (value: number): string =>
    Number(value ?? 0).toLocaleString("pt-BR", {
      minimumFractionDigits: 3,
      maximumFractionDigits: 3,
    }),
```

Se `formatter.ts` já tiver um helper de 3 decimais, use o existente e não crie `formatDecimal3`.

- [ ] **Step 6: Implementar os handlers no `BaseController`**

Modify `webapp/controller/shipmentLoads/BaseController.ts`:

```ts
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import Fragment from "sap/ui/core/Fragment";
import JSONModel from "sap/ui/model/json/JSONModel";
import Context from "sap/ui/model/odata/v4/Context";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import Table from "sap/ui/table/Table";
import FileUploader from "sap/ui/unified/FileUploader";
import DialogHelper from "siagrob1/dialogs/DialogHelper";
import CommonController from "siagrob1/controller/common/CommonController";

/** Linha das listas do diálogo de descarga, em JSONModel estático. */
type DischargeOption = { Key: string; Text: string; InvoiceKey?: string };

/**
 * Ponto de extensão da Montagem de Carga. Guarda as descargas (GAC-1171) e os anexos, para não
 * engordar mais o `Detail.controller`.
 */
export abstract class BaseController extends CommonController {
  private _dischargeDialog?: Dialog;

  /** Chave da carga aberta, lida do contexto do elemento da página. */
  protected currentLoadKey(): string {
    const context = this.getView().getBindingContext() as Context;
    return context?.getProperty("Key") as string;
  }

  private dischargesTable(): Table {
    return this.byId("loadDischargesTable") as Table;
  }

  private viewModel(): JSONModel {
    return this.getModel("viewModel") as JSONModel;
  }

  /**
   * Carrega notas e itens da carga para um JSONModel ANTES de abrir o diálogo. Select com
   * selectedKey sobre coleção OData renderiza vazio mesmo com o estado interno correto.
   */
  private async loadInvoiceOptionsAsync(): Promise<{ invoices: DischargeOption[]; items: DischargeOption[] }> {
    const model = this.getModel() as ODataModel;

    const binding = model.bindList(
      `/ShipmentLoads(${this.currentLoadKey()})/Invoices`,
      undefined,
      undefined,
      undefined,
      { $select: "Key,InvoiceNumber,InvoiceStatus", $expand: "Items($select=Key,ItemCode,ItemName,Quantity)" }
    );

    const contexts = await binding.requestContexts(0, 200);

    const invoices: DischargeOption[] = [];
    const items: DischargeOption[] = [];

    contexts.forEach(context => {
      const invoice = context.getObject() as {
        Key: string;
        InvoiceNumber?: string;
        InvoiceStatus?: string;
        Items?: { Key: string; ItemCode?: string; ItemName?: string; Quantity?: number }[];
      };

      if (invoice.InvoiceStatus === "Cancelled") return;

      invoices.push({ Key: invoice.Key, Text: invoice.InvoiceNumber ?? "(sem número)" });

      (invoice.Items ?? []).forEach(item => {
        items.push({
          Key: item.Key,
          InvoiceKey: invoice.Key,
          Text: `(${item.ItemCode}) ${item.ItemName ?? ""}`.trim(),
        });
      });
    });

    return { invoices, items };
  }

  async onAddDischarge(): Promise<void> {
    this.setBusy(true);

    try {
      const options = await this.loadInvoiceOptionsAsync();

      if (options.invoices.length === 0) {
        MessageBox.alert(
          "Esta carga ainda não tem documento de saída. O ticket de descarga é registrado " +
          "contra uma nota da carga."
        );
        return;
      }

      const firstInvoice = options.invoices[0].Key;
      const itemsOfFirst = options.items.filter(i => i.InvoiceKey === firstInvoice);

      this.viewModel().setProperty("/dischargeDialog", {
        title: "Registrar Descarga",
        key: null,
        ticketNumber: "",
        dischargeDate: new Date().toISOString().slice(0, 10),
        quantity: "",
        comments: "",
        salesInvoiceKey: firstInvoice,
        salesInvoiceItemKey: itemsOfFirst.length === 1 ? itemsOfFirst[0].Key : null,
        invoices: options.invoices,
        allItems: options.items,
        items: itemsOfFirst,
      });

      await this.openDischargeDialog();
    } catch (e) {
      MessageBox.error((e as Error).message || "Erro ao preparar o registro de descarga.");
    } finally {
      this.setBusy(false);
    }
  }

  async onEditDischarge(): Promise<void> {
    const context = this.selectedDischargeContext();

    if (!context) return;

    const options = await this.loadInvoiceOptionsAsync();
    const invoiceKey = context.getProperty("SalesInvoiceKey") as string;

    this.viewModel().setProperty("/dischargeDialog", {
      title: "Editar Descarga",
      key: context.getProperty("Key") as string,
      ticketNumber: context.getProperty("TicketNumber") as string,
      dischargeDate: (context.getProperty("DischargeDate") as string ?? "").slice(0, 10),
      quantity: String(context.getProperty("DischargedQuantity") ?? ""),
      comments: (context.getProperty("Comments") as string) ?? "",
      salesInvoiceKey: invoiceKey,
      salesInvoiceItemKey: context.getProperty("SalesInvoiceItemKey") as string,
      invoices: options.invoices,
      allItems: options.items,
      items: options.items.filter(i => i.InvoiceKey === invoiceKey),
    });

    await this.openDischargeDialog();
  }

  /** Troca de nota refiltra os itens em memória — nada volta ao servidor. */
  onDischargeInvoiceChange(): void {
    const viewModel = this.viewModel();
    const invoiceKey = viewModel.getProperty("/dischargeDialog/salesInvoiceKey") as string;
    const all = (viewModel.getProperty("/dischargeDialog/allItems") as DischargeOption[]) ?? [];
    const items = all.filter(i => i.InvoiceKey === invoiceKey);

    viewModel.setProperty("/dischargeDialog/items", items);
    viewModel.setProperty(
      "/dischargeDialog/salesInvoiceItemKey", items.length === 1 ? items[0].Key : null);
  }

  private async openDischargeDialog(): Promise<void> {
    this._dischargeDialog ??= await Fragment.load({
      id: this.getView().getId(),
      name: "siagrob1.view.shipmentLoads.fragments.ShipmentLoadDischargeDialog",
      controller: this,
    }) as Dialog;

    this.getView().addDependent(this._dischargeDialog);
    this._dischargeDialog.open();
  }

  onCloseDischargeDialog(): void {
    this._dischargeDialog?.close();
  }

  async onConfirmDischarge(): Promise<void> {
    const form = this.viewModel().getProperty("/dischargeDialog") as {
      key?: string;
      ticketNumber?: string;
      dischargeDate?: string;
      quantity?: string;
      comments?: string;
      salesInvoiceKey?: string;
      salesInvoiceItemKey?: string;
    };

    const ticketNumber = (form.ticketNumber ?? "").trim();
    const quantity = Number(String(form.quantity ?? "").replace(/\./g, "").replace(",", "."));

    if (ticketNumber === "") {
      MessageBox.alert("Informe o número do ticket.");
      return;
    }

    if (!(quantity > 0)) {
      MessageBox.alert("Informe o peso descarregado.");
      return;
    }

    if (!form.key && (!form.salesInvoiceKey || !form.salesInvoiceItemKey)) {
      MessageBox.alert("Selecione o documento de saída e o item.");
      return;
    }

    this.setBusy(true);

    try {
      const model = this.getModel() as ODataModel;

      if (form.key) {
        const action = model.bindContext("/ShipmentLoadsDischargeUpdate(...)");
        action.setParameter("Key", form.key);
        action.setParameter("TicketNumber", ticketNumber);
        action.setParameter("DischargeDate", form.dischargeDate);
        action.setParameter("Quantity", quantity);
        action.setParameter("Comments", form.comments ?? "");
        await action.invoke();
        MessageToast.show("Descarga alterada.");
      } else {
        const file = await this.loadAttachmentBase64(
          this.byId("dischargeFileUploader") as FileUploader);

        const action = model.bindContext("/ShipmentLoadsDischargeCreate(...)");
        action.setParameter("LoadKey", this.currentLoadKey());
        action.setParameter("SalesInvoiceKey", form.salesInvoiceKey);
        action.setParameter("SalesInvoiceItemKey", form.salesInvoiceItemKey);
        action.setParameter("TicketNumber", ticketNumber);
        action.setParameter("DischargeDate", form.dischargeDate);
        action.setParameter("Quantity", quantity);
        action.setParameter("Comments", form.comments ?? "");

        if (file) {
          action.setParameter("File", file.File);
          action.setParameter("FileName", file.FileName);
          action.setParameter("ContentType", file.ContentType);
        }

        await action.invoke();
        MessageToast.show("Descarga registrada.");
      }

      this.onCloseDischargeDialog();
      this.refreshDischarges();
    } catch (e) {
      MessageBox.error((e as Error).message || "Erro ao gravar a descarga.");
    } finally {
      this.setBusy(false);
    }
  }

  async onRemoveDischarge(): Promise<void> {
    const context = this.selectedDischargeContext();

    if (!context) return;

    if (!await DialogHelper.confirmDialog("Excluir o registro de descarga selecionado ?")) return;

    this.setBusy(true);

    try {
      const action = (this.getModel() as ODataModel)
        .bindContext("/ShipmentLoadsDischargeDelete(...)");
      action.setParameter("Key", context.getProperty("Key") as string);
      await action.invoke();

      MessageToast.show("Descarga excluída.");
      this.refreshDischarges();
    } catch (e) {
      MessageBox.error((e as Error).message || "Erro ao excluir a descarga.");
    } finally {
      this.setBusy(false);
    }
  }

  onDownloadDischargeAttachment(event: { getSource(): { getBindingContext(): Context } }): void {
    const key = event.getSource().getBindingContext().getProperty("AttachmentKey") as string;
    window.open(`/odata/ShipmentLoadsAttachmentsDownload(Key=${key})`, "_blank");
  }

  /** Lê o arquivo do FileUploader como base64 sem o prefixo data:. */
  protected loadAttachmentBase64(
    uploader?: FileUploader
  ): Promise<{ File: string; FileName: string; ContentType: string } | null> {
    const file = (uploader?.oFileUpload as HTMLInputElement | undefined)?.files?.[0];

    if (!file) return Promise.resolve(null);

    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve({
        File: String(reader.result).split(",")[1] ?? "",
        FileName: file.name,
        ContentType: file.type || "application/octet-stream",
      });
      reader.onerror = () => reject(new Error("Não foi possível ler o arquivo selecionado."));
      reader.readAsDataURL(file);
    });
  }

  private selectedDischargeContext(): Context | null {
    const table = this.dischargesTable();
    const index = table?.getSelectedIndex?.() ?? -1;

    if (index < 0) {
      MessageBox.alert("Selecione um registro de descarga.");
      return null;
    }

    return table.getContextByIndex(index) as Context;
  }

  /** O cabeçalho também muda: DischargedQuantity da carga é recalculado junto. */
  protected refreshDischarges(): void {
    (this.getView().getBindingContext() as Context)?.refresh();
    ((this.dischargesTable())?.getBinding("rows") as ODataListBinding)?.refresh();
  }
}
```

⚠️ Confirme a assinatura real de `DialogHelper.confirmDialog` e de `setBusy` antes de codar — `Detail.controller.ts` usa as duas e é a referência. Confirme também se o `viewModel` da página de detalhe é criado no `onInit`; se não existir, crie-o ali com `new JSONModel({})`, e **não** com `setData({})` depois, que já causou o bug do aviso de atualização aparecendo sempre.

- [ ] **Step 7: Incluir a tabela nova no refresh geral**

Modify `webapp/controller/shipmentLoads/Detail.controller.ts`, no array de `refreshAll`:

```ts
      "loadDischargesTable",
      "loadAttachmentsTable",
```

`loadAttachmentsTable` só existe depois da Task 8; `binding?.refresh()` com `?.` já tolera o id ausente, então a ordem não quebra nada.

- [ ] **Step 8: Verificar tipos e lint**

Run, em `siagro-b1-frontend`: `yarn ts-typecheck` e `yarn lint`
Expected: sem erro. ⚠️ Não rode `yarn test`: o gate de cobertura de 50% contra ~2,4% reais reprova o repo inteiro, independentemente desta mudança.

- [ ] **Step 9: Provar no navegador**

Suba Web + Gateway no profile `yktb` e `yarn start:dev`. Abra uma carga faturada, registre uma descarga com anexo, confira a linha no grid, o ícone de anexo baixando o arquivo, o cabeçalho mostrando descarregado × embarcado, e a edição e exclusão.

- [ ] **Step 10: Commit**

```bash
git add webapp/view/shipmentLoads/fragments/ShipmentLoadDischarges.fragment.xml webapp/view/shipmentLoads/fragments/ShipmentLoadDischargeDialog.fragment.xml
git add webapp/view/shipmentLoads/Detail.view.xml webapp/view/shipmentLoads/fragments/LoadCard.fragment.xml webapp/controller/shipmentLoads/BaseController.ts webapp/controller/shipmentLoads/Detail.controller.ts webapp/model/formatter.ts
git commit -m "feat(shipment): registrar descargas da carga na tela de detalhe

Aba nova com o grid de tickets e o dialogo de registro, que aponta para uma
linha de documento de saida da carga e aceita o arquivo do ticket junto.

Atencao: as listas de nota e item vem de JSONModel estatico carregado antes
de abrir. Select com selectedKey sobre colecao OData renderiza vazio com o
estado interno correto.

Refs: GAC-1171"
```

---

### Task 8: Aba Anexos na carga

**Files:**
- Create: `webapp/view/shipmentLoads/fragments/ShipmentLoadAttachments.fragment.xml`
- Create: `webapp/view/shipmentLoads/fragments/ShipmentLoadAttachmentDialog.fragment.xml`
- Modify: `webapp/view/shipmentLoads/Detail.view.xml` (seção depois de "Descargas")
- Modify: `webapp/controller/shipmentLoads/BaseController.ts` (handlers de anexo)
- Modify: `webapp/model/ServerRoutes.ts` (3 rotas)

**Interfaces:**
- Consumes: `BaseController.loadAttachmentBase64`, `BaseController.currentLoadKey` (Task 7); actions/functions de anexo (Task 5).
- Produces: nada para tasks seguintes.

- [ ] **Step 1: Declarar as rotas**

Modify `webapp/model/ServerRoutes.ts`, junto das rotas de anexo já existentes (região da linha 196):

```ts
  shipmentLoadsAttachmentsList: '/odata/ShipmentLoadsAttachmentsList',
  shipmentLoadsAttachmentUpload: '/odata/ShipmentLoadsAttachmentUpload',
  shipmentLoadsAttachmentsDownload: '/odata/ShipmentLoadsAttachmentsDownload',
```

- [ ] **Step 2: Criar o fragmento do grid de anexos**

Create `webapp/view/shipmentLoads/fragments/ShipmentLoadAttachments.fragment.xml`:

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:t="sap.ui.table">
	<!-- Lido pela Function, não pelo modelo OData: a lista não pode arrastar o binário. -->
	<t:Table
		id="loadAttachmentsTable"
		selectionMode="Single"
		selectionBehavior="Row"
		class="sapUiSizeCondensed"
		visibleRowCount="5"
		rows="{viewModel>/attachments}">
		<t:extension>
			<OverflowToolbar>
				<Title text="Anexos"/>
				<ToolbarSpacer/>
				<Button type="Transparent" text="Anexar" icon="sap-icon://add" press=".onAddAttachment"/>
				<Button type="Transparent" text="Baixar" icon="sap-icon://download" press=".onDownloadAttachment"/>
				<Button type="Transparent" text="Excluir" icon="sap-icon://delete" press=".onRemoveAttachment"/>
			</OverflowToolbar>
		</t:extension>
		<t:columns>
			<t:Column label="Tipo" width="12rem">
				<t:template>
					<Text wrapping="false" text="{ path: 'viewModel>AttachmentType', formatter: '.formatter.formatShipmentLoadAttachmentType' }"/>
				</t:template>
			</t:Column>
			<t:Column label="Descrição" width="22rem">
				<t:template><Text text="{viewModel>Description}" wrapping="false"/></t:template>
			</t:Column>
			<t:Column label="Arquivo" width="18rem">
				<t:template><Text text="{viewModel>FileName}" wrapping="false"/></t:template>
			</t:Column>
			<t:Column label="Data" width="11rem">
				<t:template>
					<Text wrapping="false" text="{ path: 'viewModel>CreatedAt', formatter: '.formatter.formatDateTime' }"/>
				</t:template>
			</t:Column>
			<t:Column label="Usuário" width="12rem">
				<t:template><Text text="{viewModel>CreatedBy}" wrapping="false"/></t:template>
			</t:Column>
		</t:columns>
	</t:Table>
</core:FragmentDefinition>
```

- [ ] **Step 3: Criar o diálogo de anexo**

Create `webapp/view/shipmentLoads/fragments/ShipmentLoadAttachmentDialog.fragment.xml`:

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form"
	xmlns:u="sap.ui.unified">
	<Dialog id="shipmentLoadAttachmentDialog" title="Anexar Documento" contentWidth="32rem">
		<content>
			<f:Form editable="true">
				<f:layout>
					<f:ColumnLayout columnsM="1" columnsL="1" columnsXL="1"/>
				</f:layout>
				<f:formContainers>
					<f:FormContainer>
						<f:formElements>
							<f:FormElement label="Tipo">
								<f:fields>
									<Select forceSelection="false" selectedKey="{viewModel>/attachmentDialog/attachmentType}">
										<core:ListItem key="LoadingTicket" text="Ticket de Carga"/>
										<core:ListItem key="DischargeTicket" text="Ticket de Descarga"/>
										<core:ListItem key="TaxDocument" text="Nota Fiscal"/>
										<core:ListItem key="FreightDocument" text="Conhecimento de Frete"/>
										<core:ListItem key="Other" text="Outro"/>
									</Select>
								</f:fields>
							</f:FormElement>
							<f:FormElement label="Descrição">
								<f:fields>
									<Input value="{viewModel>/attachmentDialog/description}" maxLength="100"/>
								</f:fields>
							</f:FormElement>
							<f:FormElement label="Arquivo">
								<f:fields>
									<u:FileUploader
										id="attachmentFileUploader"
										placeholder="Selecione o arquivo"
										buttonText="Procurar"
										uploadOnChange="false"/>
								</f:fields>
							</f:FormElement>
						</f:formElements>
					</f:FormContainer>
				</f:formContainers>
			</f:Form>
		</content>
		<beginButton>
			<Button text="Anexar" type="Emphasized" press=".onConfirmAttachment"/>
		</beginButton>
		<endButton>
			<Button text="Cancelar" press=".onCloseAttachmentDialog"/>
		</endButton>
	</Dialog>
</core:FragmentDefinition>
```

- [ ] **Step 4: Pendurar a seção**

Modify `webapp/view/shipmentLoads/Detail.view.xml`, depois da seção "Descargas":

```xml
			<uxap:ObjectPageSection titleUppercase="false" title="Anexos">
				<uxap:subSections>
					<uxap:ObjectPageSubSection>
						<uxap:blocks>
							<core:Fragment fragmentName="siagrob1.view.shipmentLoads.fragments.ShipmentLoadAttachments" type="XML"/>
						</uxap:blocks>
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
```

Sem `visible` condicional: anexo vale em qualquer carga, inclusive de Remoção e cancelada.

- [ ] **Step 5: Implementar os handlers**

Modify `webapp/controller/shipmentLoads/BaseController.ts`, adicionando à classe:

```ts
  private _attachmentDialog?: Dialog;

  /** Carrega o grid de anexos pela Function. Chamar no onInit da página e depois de gravar. */
  protected async refreshAttachments(): Promise<void> {
    const response = await fetch(
      `${ServerRoutes.shipmentLoadsAttachmentsList}(LoadKey=${this.currentLoadKey()})`);

    if (!response.ok) {
      this.viewModel().setProperty("/attachments", []);
      return;
    }

    const payload = await response.json() as { value?: unknown[] } | unknown[];
    this.viewModel().setProperty(
      "/attachments", Array.isArray(payload) ? payload : payload.value ?? []);
  }

  async onAddAttachment(): Promise<void> {
    this.viewModel().setProperty("/attachmentDialog", {
      attachmentType: "DischargeTicket",
      description: "",
    });

    this._attachmentDialog ??= await Fragment.load({
      id: this.getView().getId(),
      name: "siagrob1.view.shipmentLoads.fragments.ShipmentLoadAttachmentDialog",
      controller: this,
    }) as Dialog;

    this.getView().addDependent(this._attachmentDialog);
    this._attachmentDialog.open();
  }

  onCloseAttachmentDialog(): void {
    this._attachmentDialog?.close();
  }

  async onConfirmAttachment(): Promise<void> {
    const form = this.viewModel().getProperty("/attachmentDialog") as {
      attachmentType?: string;
      description?: string;
    };

    const description = (form.description ?? "").trim();

    if (description === "") {
      MessageBox.alert("Informe a descrição do anexo.");
      return;
    }

    const file = await this.loadAttachmentBase64(
      this.byId("attachmentFileUploader") as FileUploader);

    if (!file) {
      MessageBox.alert("Selecione o arquivo.");
      return;
    }

    this.setBusy(true);

    try {
      const action = (this.getModel() as ODataModel)
        .bindContext("/ShipmentLoadsAttachmentUpload(...)");
      action.setParameter("LoadKey", this.currentLoadKey());
      action.setParameter("AttachmentType", form.attachmentType ?? "Other");
      action.setParameter("Description", description);
      action.setParameter("File", file.File);
      action.setParameter("FileName", file.FileName);
      action.setParameter("ContentType", file.ContentType);
      await action.invoke();

      MessageToast.show("Documento anexado.");
      this.onCloseAttachmentDialog();
      await this.refreshAttachments();
    } catch (e) {
      MessageBox.error((e as Error).message || "Erro ao anexar o documento.");
    } finally {
      this.setBusy(false);
    }
  }

  onDownloadAttachment(): void {
    const row = this.selectedAttachmentRow();

    if (!row) return;

    window.open(`${ServerRoutes.shipmentLoadsAttachmentsDownload}(Key=${row.Key})`, "_blank");
  }

  async onRemoveAttachment(): Promise<void> {
    const row = this.selectedAttachmentRow();

    if (!row) return;

    if (!await DialogHelper.confirmDialog("Excluir o anexo selecionado ?")) return;

    this.setBusy(true);

    try {
      const action = (this.getModel() as ODataModel)
        .bindContext("/ShipmentLoadsAttachmentDelete(...)");
      action.setParameter("Key", row.Key);
      await action.invoke();

      MessageToast.show("Anexo excluído.");
      await this.refreshAttachments();
    } catch (e) {
      MessageBox.error((e as Error).message || "Erro ao excluir o anexo.");
    } finally {
      this.setBusy(false);
    }
  }

  private selectedAttachmentRow(): { Key: string } | null {
    const table = this.byId("loadAttachmentsTable") as Table;
    const index = table?.getSelectedIndex?.() ?? -1;

    if (index < 0) {
      MessageBox.alert("Selecione um anexo.");
      return null;
    }

    return table.getContextByIndex(index)?.getObject() as { Key: string };
  }
```

Adicione `import ServerRoutes from "siagrob1/model/ServerRoutes";` no topo, conferindo antes a forma exata do export em `ServerRoutes.ts` (default ou nomeado).

`onRemoveAttachment` chama `ShipmentLoadsAttachmentDelete`, criada na Task 5 — esta task não toca no backend. Se a action não existir, pare: a Task 5 não foi concluída.

- [ ] **Step 6: Carregar os anexos ao abrir a página**

Modify `webapp/controller/shipmentLoads/Detail.controller.ts`, no handler de rota casada (onde `_loadKey` é definido), depois do bind do elemento:

```ts
    void this.refreshAttachments();
```

- [ ] **Step 7: Verificar tipos e lint**

Run: `yarn ts-typecheck` e `yarn lint`
Expected: sem erro.

- [ ] **Step 8: Provar no navegador**

Anexe um documento de cada tipo, baixe, exclua. Tente excluir o anexo que a Task 7 vinculou a um ticket: deve aparecer a mensagem pedindo para excluir o registro de descarga antes, e não um erro genérico.

- [ ] **Step 9: Commit**

```bash
git add webapp/view/shipmentLoads/fragments/ShipmentLoadAttachments.fragment.xml webapp/view/shipmentLoads/fragments/ShipmentLoadAttachmentDialog.fragment.xml
git add webapp/view/shipmentLoads/Detail.view.xml webapp/controller/shipmentLoads/BaseController.ts webapp/controller/shipmentLoads/Detail.controller.ts webapp/model/ServerRoutes.ts
git commit -m "feat(shipment): anexar documentos na tela da carga

Grid de anexos com tipo de lista fechada, para o usuario guardar ticket de
carga, de descarga, nota fiscal e conhecimento de frete no documento a que
eles pertencem.

Refs: GAC-1171"
```

---

### Task 9: Confronto ticket × relatório na Conferência de Entregas

**Files:**
- Modify: `webapp/view/salesInvoices/reconciliation/Main.view.xml` (duas colunas, depois de "Qtd.Entregue" — região das linhas 185–214)
- Modify: `webapp/controller/salesInvoices/reconciliation/Main.controller.ts` (`$select` do binding)
- Modify: `webapp/model/formatter.ts` (formatter da diferença)

**Interfaces:**
- Consumes: `SalesInvoiceItem.TicketDeliveredQuantity` (Task 1, exposta no EDM pela Task 5).
- Produces: nada.

- [ ] **Step 1: Adicionar o formatter da diferença**

Modify `webapp/model/formatter.ts`, junto de `formatDeliveryDifference`:

```ts
  /**
   * Diferença entre o peso dos TICKETS e o peso conferido do relatório da trading, exibida
   * SOMENTE quando os dois são maiores que zero (GAC-1171).
   *
   * Vazio e zero dizem coisas diferentes: sem os dois lados não há confronto, e um "0,000" ali
   * afirmaria que os números batem. Por isso a ausência devolve string vazia.
   */
  formatTicketDifference: (ticketQuantity: number, deliveredQuantity: number): string => {
    const ticket = Number(ticketQuantity ?? 0);
    const delivered = Number(deliveredQuantity ?? 0);

    if (!(ticket > 0) || !(delivered > 0)) return "";

    const difference = ticket - delivered;

    return `${difference > 0 ? "+" : ""}${formatter.formatDecimal3(difference)}`;
  },

  /** Vermelho quando ticket e relatório divergem; neutro quando batem ou falta um lado. */
  stateTicketDifference: (ticketQuantity: number, deliveredQuantity: number): string => {
    const ticket = Number(ticketQuantity ?? 0);
    const delivered = Number(deliveredQuantity ?? 0);

    if (!(ticket > 0) || !(delivered > 0)) return "None";

    return ticket === delivered ? "Success" : "Error";
  },
```

- [ ] **Step 2: Adicionar as duas colunas**

Modify `webapp/view/salesInvoices/reconciliation/Main.view.xml`, depois da coluna "Qtd.Entregue" (que fecha na linha 214):

```xml
          <t:Column
						label="Qtd. Ticket"
						hAlign="Center"
            width="7rem"
						>
						<t:template>
							<!--
								Peso dos TICKETS de descarga, somado pelo backend. Somente leitura: o numero
								vem do documento do transportador e nao se confunde com o relatorio da trading
								ao lado. Wrapping desligado porque em sap.ui.table texto sem ele e CORTADO.
							-->
							<Text
								wrapping="false"
								text="{ path: 'TicketDeliveredQuantity', targetType: 'any', formatter: '.formatter.formatDecimal3' }"/>
						</t:template>
					</t:Column>
          <t:Column
						label="Diferença Ticket"
						hAlign="Center"
            width="8rem"
						>
						<t:template>
							<!--
								Ticket menos conferido, so quando os DOIS sao maiores que zero. Vazio significa
								"ainda nao ha confronto"; zero significaria "confere", que e outra afirmacao.
								targetType 'any' nas duas partes: sao Edm.Decimal e sem isso chega string,
								fazendo a comparacao com zero mentir.
							-->
							<ObjectStatus
								inverted="true"
								state="{
                  parts: [
                    { path: 'TicketDeliveredQuantity', targetType: 'any' },
                    { path: 'DeliveredQuantity', targetType: 'any' }
                  ],
                  formatter: '.formatter.stateTicketDifference'
                }"
								text="{
                  parts: [
                    { path: 'TicketDeliveredQuantity', targetType: 'any' },
                    { path: 'DeliveredQuantity', targetType: 'any' }
                  ],
                  formatter: '.formatter.formatTicketDifference'
                }"/>
						</t:template>
					</t:Column>
```

- [ ] **Step 3: Incluir a propriedade no `$select`**

Modify `webapp/controller/salesInvoices/reconciliation/Main.controller.ts`. Localize o `$select` do binding dos itens e acrescente `TicketDeliveredQuantity`. Se o binding estiver declarado no XML em vez do controller, acrescente lá. Propriedade fora do `$select` chega `undefined` e a coluna fica vazia sem erro.

- [ ] **Step 4: Verificar tipos e lint**

Run: `yarn ts-typecheck` e `yarn lint`
Expected: sem erro.

- [ ] **Step 5: Provar o ciclo inteiro no navegador**

Com Web + Gateway no `yktb` e `yarn start:dev`:

1. Carga faturada → registrar descarga com anexo.
2. Conferência de Entregas: "Qtd. Ticket" preenchida, "Diferença Ticket" **vazia** (só um lado).
3. Digitar o peso do relatório da trading em "Qtd.Entregue" → a diferença **aparece**, destacada quando divergir.
4. Encerrar a entrega → confirmar que **só então** o saldo do contrato se move.
5. Registrar um **segundo ticket com a entrega já encerrada** → "Qtd.Entregue", desconto e status ficam **intactos**, e a diferença se atualiza.

O passo 5 é o que prova a regra central do chamado. Se ele falhar, pare e reporte antes de commitar.

- [ ] **Step 6: Commit**

```bash
git add webapp/view/salesInvoices/reconciliation/Main.view.xml webapp/controller/salesInvoices/reconciliation/Main.controller.ts webapp/model/formatter.ts
git commit -m "feat(invoice): confrontar peso do ticket com o conferido na entrega

O ticket pode ter sido adulterado pelo transportador e o relatorio da trading
pode ter vindo errado. Expor os dois lado a lado, com a diferenca destacada,
e o que permite apurar a inconsistencia.

Atencao: a diferenca so aparece quando os DOIS pesos sao maiores que zero.
Vazio diz 'ainda nao ha confronto'; zero diria 'confere'.

Refs: GAC-1171"
```

---

## Autorrevisão (executada)

**Cobertura da spec:** as 8 decisões e as 5 subseções do Desenho têm task. Decisão 1 → Tasks 1 e 2; decisão 2 → Task 1; decisões 3 e 4 → Task 3 (guards) e Task 7 (diálogo); decisão 5 → Task 9; decisão 6 → Tasks 2 e 3 (ausência de guard, cobrada por teste); decisão 7 → Tasks 7 e 8; decisão 8 → nenhum índice criado na Task 1, como pedido. Integrações obrigatórias → Task 6 (delete) e Task 3 (nota cancelada). Log → Task 3. OData → Task 5. Fora de escopo permanece fora: nenhuma task cria relatório de inconsistências nem ato de liberação de frete.

**Lacuna encontrada e corrigida inline:** a Task 8 usa `ShipmentLoadsAttachmentDelete`, que a Task 5 não cria. O Step 5 da Task 8 agora manda criar a action e o controller no backend antes, em commit separado.

**Consistência de tipos:** `TicketDeliveredQuantity` (item) e `DischargedQuantity` (carga e ticket) usados com o mesmo nome em todas as tasks; `RecalculateAsync(Guid, IEnumerable<Guid>)` com a mesma assinatura nas Tasks 2, 3 e 6; `loadAttachmentBase64`/`currentLoadKey` declarados na Task 7 e consumidos na Task 8 com a mesma forma; `formatDecimal3` definido na Task 7 e reusado na Task 9.

**Pontos que o executor precisa confirmar no código antes de codar** (estão marcados nos steps, e existem porque o plano não deve inventar assinatura): assinaturas de `DialogHelper.confirmDialog` e `setBusy`; forma do export de `ServerRoutes`; existência de um helper de 3 decimais em `formatter.ts`; onde o `$select` dos itens da Conferência é declarado.
