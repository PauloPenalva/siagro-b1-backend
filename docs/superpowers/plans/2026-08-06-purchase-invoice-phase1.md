# Documento de Entrada — Fase 1 Implementation Plan

> **STATUS FINAL (06/08/2026): Fase 1 ENCERRADA E VERIFICADA NO NAVEGADOR.** 15/15 tarefas.
> Backend: **945** testes verdes, build 0 erros, migrations aplicadas só no localhost.
> Frontend: `ts-typecheck` e `lint` limpos. Nada commitado (commits são do usuário).
> Fases 2 (camada fiscal) e 3 (efeito e conciliação) não foram iniciadas.
>
> A verificação end-to-end achou **7 defeitos visíveis ao usuário — 11 correções ao todo**, todos
> meus e nenhum deles detectável pelos gates. Detalhe na Task 15. O resumo é que build verde, 941
> testes verdes e lint limpo conviviam com uma tela de detalhe que nascia COMPLETAMENTE EM BRANCO.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Substituir a rotina de Devolução de Cliente por um Documento de Entrada único
(`PURCHASE_INVOICES`), com tipos `Normal`/`Return`, migrando os dados existentes sem perda.

**Architecture:** Espelho direto de `SalesInvoice` — mesma entidade base (`DocumentEntity`), mesmo
enum de status, mesma separação cabeçalho/item/comentário/log, uma classe de serviço por operação.
A devolução vira `InvoiceType = Return` na tabela nova; `CUSTOMER_RETURNS` é copiada e dropada dentro
da mesma migration.

**Tech Stack:** .NET 10, EF Core (SQL Server), OData v4 (`Microsoft.AspNetCore.OData`), xUnit +
EF InMemory, OpenUI5 1.141 + TypeScript.

## Global Constraints

- **NUNCA `git commit` ou `git push`.** Os commits são feitos manualmente pelo usuário. A única
  operação de escrita no git permitida é `git add`. Todo passo "Stage" deste plano é `git add` puro.
- **Todo arquivo novo é staged imediatamente** com `git add <path>`, no sub-repo correto
  (`siagro-b1-backend/` ou `siagro-b1-frontend/`).
- **Identificadores em inglês; texto de usuário em pt-BR.** Exceção única e deliberada:
  `ChaveNFe`, para casar com `SalesInvoice` — decidida pelo usuário e registrada no spec.
- **Nada de FK para cadastro dual-mode.** `CardCode`, `UsageCode`, `CostCenterCode`,
  `LedgerAccountCode` gravam sem FK: em modo SAPB1 a tabela local fica vazia e FK obrigatória vira
  INNER JOIN que zera a coleção inteira. Validação é no serviço.
- **DI é manual.** Todo serviço novo entra à mão em `AddApplicationServices()`
  (`SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`). Não há assembly scanning.
- **Valores de enum usados no SQL da migration:** `InvoiceStatus` = `Pending 0`, `Confirmed 1`,
  `Cancelled 2`, `Returned 3`. `CustomerReturnStatus` (o que sai) = `Registered 0`, `Cancelled 1`.
  `PurchaseInvoiceType` = `Normal 0`, `Return 1`. `DocumentIssuerType` = `ThirdParty 0`, `Own 1`.
- **`dotnet ef` sempre com `ASPNETCORE_ENVIRONMENT` explícito** — o perfil `db-migration` aponta
  para produção no fallback.
- Spec de referência: `docs/superpowers/specs/2026-08-06-purchase-invoice-design.md`.

---

## File Structure

**Cria — `SiagroB1.Domain`**
- `Enums/PurchaseInvoiceType.cs`, `Enums/DocumentIssuerType.cs`
- `Entities/PurchaseInvoice.cs`, `PurchaseInvoiceItem.cs`, `PurchaseInvoiceComment.cs`, `PurchaseInvoiceChangeLog.cs`
- `Dtos/PurchaseInvoiceDraftDto.cs`, `Dtos/PurchaseInvoiceOriginItemDto.cs`

**Cria — `SiagroB1.Application/Services/PurchaseInvoices/`** (uma classe por operação)
- `PurchaseInvoicesGetService.cs`, `CreateService`, `UpdateService`, `DeleteService`,
  `ConfirmService`, `ReverseConfirmService`, `CancelService`, `ImportXmlService`,
  `GetOriginItemsService`
- `PurchaseInvoicesItems{Create,Update,Delete,Get}Service.cs`
- `PurchaseInvoicesChangeLogService.cs`, `ChangeLogsGetService.cs`
- `PurchaseInvoicesComment{Create,Update,Delete}Service.cs`, `CommentsGetService.cs`

**Cria — `SiagroB1.Web`**
- `Controllers/PurchaseInvoicesController.cs`, `PurchaseInvoicesItemsController.cs`,
  `PurchaseInvoicesCommentsController.cs`, `PurchaseInvoicesChangeLogsController.cs`
- `Actions/PurchaseInvoices/{Confirm,ReverseConfirm,Cancel,ImportXml,CommentCreate,CommentUpdate,CommentDelete}Controller.cs`

**Modifica**
- `SiagroB1.Infra/Context/AppDbContext.cs` — DbSets + índice único, remove os de `CustomerReturn`
- `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` — EntitySets/actions novos, remove os antigos
- `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` — registros novos, remove os antigos

**Remove** — tudo de `CustomerReturn` (Task 7), frontend em Task 12.

**Frontend cria** — `webapp/view/purchaseInvoices/` (+ `fragments/`),
`webapp/controller/purchaseInvoices/`, rotas no `manifest.json`, formatters.

---

### Task 1: Enums e entidades de domínio — ✅ CONCLUÍDA (06/08/2026)

> 7 testes passando, `dotnet build SiagroB1.sln` com 0 erros, tudo staged.
> Um teste a mais do que o previsto: `Document_is_born_normal_third_party_and_pending`, que trava
> os defaults do cabeçalho (Normal / ThirdParty / Pending).

**Files:**
- Create: `SiagroB1.Domain/Enums/PurchaseInvoiceType.cs`
- Create: `SiagroB1.Domain/Enums/DocumentIssuerType.cs`
- Create: `SiagroB1.Domain/Entities/PurchaseInvoice.cs`
- Create: `SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs`
- Create: `SiagroB1.Domain/Entities/PurchaseInvoiceComment.cs`
- Create: `SiagroB1.Domain/Entities/PurchaseInvoiceChangeLog.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSets + índice único)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceItemCalculationTests.cs`

**Interfaces:**
- Consumes: `BaseEntity`, `DocumentEntity` (`SiagroB1.Domain.Shared.Base`), `InvoiceStatus`,
  `FreightTerms`, `SalesInvoiceItem.AssessedShortage`.
- Produces: `PurchaseInvoice` (props abaixo), `PurchaseInvoiceItem` com `Total`,
  `AssessedShortage`, `Difference`; `AppDbContext.PurchaseInvoices`, `.PurchaseInvoicesItems`,
  `.PurchaseInvoicesComments`, `.PurchaseInvoicesChangeLogs`.

- [ ] **Step 1: Write the failing test**

Create `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceItemCalculationTests.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Propriedades calculadas da linha do documento de entrada. A quebra apurada e a diferença
/// migraram de CustomerReturnItem sem mudar de fórmula — estes testes são a prova.
/// </summary>
public class PurchaseInvoiceItemCalculationTests
{
    private static SalesInvoiceItem ClosedOrigin(
        decimal quantity = 1000m, decimal delivered = 980m, decimal loss = 0m) =>
        new()
        {
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
            DeliveredQuantity = delivered,
            QuantityLoss = loss,
            DeliveryStatus = SalesInvoiceDeliveryStatus.Closed,
        };

    [Fact]
    public void Total_is_quantity_times_unit_price_rounded_to_two_places()
    {
        var line = new PurchaseInvoiceItem { Quantity = 3m, UnitPrice = 1.005m };

        Assert.Equal(3.02m, line.Total);
    }

    [Fact]
    public void Assessed_shortage_comes_from_the_linked_sales_invoice_item()
    {
        var line = new PurchaseInvoiceItem { Quantity = 20m, SalesInvoiceItem = ClosedOrigin() };

        Assert.Equal(20m, line.AssessedShortage);
    }

    [Fact]
    public void Assessed_shortage_is_zero_when_no_origin_is_linked()
    {
        // Linha de entrada NORMAL não tem origem de saída — e isso não é divergência.
        var line = new PurchaseInvoiceItem { Quantity = 20m };

        Assert.Equal(0m, line.AssessedShortage);
    }

    [Fact]
    public void Difference_is_zero_when_the_return_matches_the_shortage()
    {
        var line = new PurchaseInvoiceItem { Quantity = 20m, SalesInvoiceItem = ClosedOrigin() };

        Assert.Equal(0m, line.Difference);
    }

    [Fact]
    public void Difference_is_negative_when_less_was_returned_than_assessed()
    {
        var line = new PurchaseInvoiceItem { Quantity = 15m, SalesInvoiceItem = ClosedOrigin() };

        Assert.Equal(-5m, line.Difference);
    }

    [Fact]
    public void Document_total_sums_the_lines_and_is_independent_of_the_declared_total()
    {
        var invoice = new PurchaseInvoice { CardCode = "F0001", TotalDocumentValue = 1_000m };
        invoice.AddItem(new PurchaseInvoiceItem { Quantity = 2m, UnitPrice = 10m });
        invoice.AddItem(new PurchaseInvoiceItem { Quantity = 3m, UnitPrice = 10m });

        // Divergir do declarado pelo emitente é INFORMAÇÃO de conciliação, não erro.
        Assert.Equal(50m, invoice.TotalInvoiceItems);
        Assert.Equal(1_000m, invoice.TotalDocumentValue);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoiceItemCalculationTests`
Expected: FAIL na compilação — `PurchaseInvoice`/`PurchaseInvoiceItem` não existem.

- [ ] **Step 3: Create the enums**

`SiagroB1.Domain/Enums/PurchaseInvoiceType.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Tipo do documento de entrada. Só dois, simétrico a <see cref="SalesInvoiceType"/>.
///
/// Compra de mercadoria, venda futura do produtor e remessa por carregamento NÃO são tipos:
/// são NATUREZAS DE OPERAÇÃO (<see cref="Usage"/> + <see cref="UsageEffect"/>), configuradas em
/// cadastro. É o que faz fluxo fiscal novo não exigir enum nem migration.
/// </summary>
public enum PurchaseInvoiceType
{
    Normal = 0,
    Return = 1,
}
```

`SiagroB1.Domain/Enums/DocumentIssuerType.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Quem emitiu o documento fiscal.
///
/// <c>ThirdParty</c> é o caso normal da entrada: fornecedor, produtor rural ou cliente
/// devolvendo — o número vem do emitente. <c>Own</c> é a emissão própria, em que o número é
/// digitado pelo operador nesta fase e passa a ser numerado automaticamente na Fase 3.
/// </summary>
public enum DocumentIssuerType
{
    ThirdParty = 0,
    Own = 1,
}
```

- [ ] **Step 4: Create `PurchaseInvoice`**

`SiagroB1.Domain/Entities/PurchaseInvoice.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Documento fiscal de ENTRADA: NF do fornecedor, compra de mercadoria para comercialização,
/// faturamento antecipado de venda futura do produtor rural e suas remessas, insumo, serviço,
/// e a devolução emitida pelo cliente (<see cref="PurchaseInvoiceType.Return"/>).
///
/// Espelha <see cref="SalesInvoice"/> de propósito: as duas são lidas lado a lado, e manter a
/// mesma forma é o que evita a divergência que a antiga <c>CustomerReturn</c> criou.
///
/// Nesta fase é documento de CONTROLE E CONCILIAÇÃO: não move saldo de contrato, não grava
/// ledger e não toca em romaneio. O efeito de negócio chega na Fase 3, pelo
/// <see cref="UsageEffect"/> da natureza de operação de cada linha.
/// </summary>
[Table("PURCHASE_INVOICES")]
public class PurchaseInvoice : DocumentEntity
{
    public PurchaseInvoiceType InvoiceType { get; set; } = PurchaseInvoiceType.Normal;

    public DocumentIssuerType IssuerType { get; set; } = DocumentIssuerType.ThirdParty;

    public InvoiceStatus InvoiceStatus { get; set; } = InvoiceStatus.Pending;

    /// <summary>
    /// Número INTERNO do documento. Nulo em documento de terceiro nesta fase; digitado à mão na
    /// emissão própria. A Fase 3 o preenche pelo numerador <c>DocNumbers</c>.
    /// </summary>
    [Column(TypeName = "VARCHAR(9)")]
    public string? InvoiceNumber { get; set; }

    /// <summary>Emitente: fornecedor, produtor ou cliente devolvendo. Sem FK — cadastro dual-mode.</summary>
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    /// <summary>Número da nota fiscal, como emitida.</summary>
    [Column(TypeName = "VARCHAR(9)")]
    public string? TaxDocumentNumber { get; set; }

    [Column(TypeName = "VARCHAR(3)")]
    public string? TaxDocumentSeries { get; set; }

    /// <summary>
    /// Chave da NF-e. Única entre as não canceladas.
    ///
    /// O nome fica em português para casar com <see cref="SalesInvoice.ChaveNFe"/> — exceção
    /// consciente à regra de identificadores em inglês, porque as duas entidades são irmãs e
    /// são lidas juntas. Ver o spec.
    /// </summary>
    [Column(TypeName = "VARCHAR(44)")]
    public string? ChaveNFe { get; set; }

    /// <summary>Emissão, como declarada pelo emitente.</summary>
    public DateTime? IssueDate { get; set; } = DateTime.Now.Date;

    /// <summary>Entrada/lançamento na empresa. Pode ser posterior à emissão.</summary>
    public DateTime? PostingDate { get; set; } = DateTime.Now.Date;

    /// <summary>
    /// Total DECLARADO pelo emitente (<c>ICMSTot/vNF</c> do XML).
    ///
    /// Coexiste com <see cref="TotalInvoiceItems"/>, que é a soma das linhas, e divergirem é
    /// INFORMAÇÃO de conciliação — não erro. Frete e impostos entram no declarado e não nas
    /// linhas. É por isso que este campo não pode virar derivado.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal TotalDocumentValue { get; set; }

    /// <summary>
    /// Informações complementares do contribuinte (<c>infAdic/infCpl</c> do XML), guardadas cruas.
    ///
    /// É AQUI que o emitente escreve em texto livre as referências que o layout não estrutura por
    /// linha. Exibido na tela: é a cola do operador para fazer a amarração.
    /// </summary>
    [Column(TypeName = "VARCHAR(MAX)")]
    public string? TaxPayerComments { get; set; }

    /// <summary>Observação do cabeçalho. Não confundir com <see cref="CommentEntries"/>.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal GrossWeight { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal NetWeight { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? TruckCode { get; set; }

    [Column(TypeName = "VARCHAR(15)")]
    public string? TruckingCompanyCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? TruckingCompanyName { get; set; }

    public FreightTerms FreightTerms { get; set; }

    /// <summary>
    /// Documento de origem. É como a NF de REMESSA aponta a NF de venda futura que a antecipou —
    /// mesmo mecanismo de <see cref="SalesInvoice.SalesInvoiceOriginKey"/>.
    /// </summary>
    public Guid? PurchaseInvoiceOriginKey { get; set; }
    public virtual PurchaseInvoice? PurchaseInvoiceOrigin { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? XmlFileName { get; set; }

    /// <summary>XML original: prova documental, e permite reprocessar se a leitura mudar.</summary>
    [Column(TypeName = "VARBINARY(MAX)")]
    public byte[]? XmlData { get; set; }

    public ICollection<PurchaseInvoiceItem> Items { get; set; } = [];

    /// <summary>
    /// Chama-se <c>CommentEntries</c>, e não <c>Comments</c>, porque <see cref="Comments"/> já é o
    /// escalar de "Observações" do cabeçalho.
    /// </summary>
    public ICollection<PurchaseInvoiceComment> CommentEntries { get; set; } = [];

    public ICollection<PurchaseInvoiceChangeLog> ChangeLogs { get; set; } = [];

    [NotMapped]
    public decimal TotalInvoiceItems => Items.Sum(i => i.Total);

    public void AddItem(PurchaseInvoiceItem item)
    {
        item.PurchaseInvoice = this;
        Items.Add(item);
    }
}
```

- [ ] **Step 5: Create `PurchaseInvoiceItem`**

`SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Linha do documento de entrada.
///
/// <see cref="ItemCode"/> e <see cref="UnitOfMeasureCode"/> são NULÁVEIS aqui, ao contrário de
/// <see cref="SalesInvoiceItem"/>: o código vem do emitente e pode não existir no cadastro local.
///
/// Os campos fiscais (natureza de operação, CFOP, NCM, CST, impostos) chegam na Fase 2, e as
/// amarrações a contrato de compra e a romaneio na Fase 3 — junto com o value help e a coluna de
/// divergência que as consomem.
/// </summary>
[Table("PURCHASE_INVOICES_ITEMS")]
public class PurchaseInvoiceItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? PurchaseInvoiceKey { get; set; }
    public virtual PurchaseInvoice? PurchaseInvoice { get; set; }

    [Column(TypeName = "VARCHAR(30)")]
    public string? ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "VARCHAR(4)")]
    public string? UnitOfMeasureCode { get; set; }

    /// <summary>
    /// A AMARRAÇÃO da devolução, feita à mão pelo operador: a linha do documento de SAÍDA que
    /// esta devolução espelha. Nulável porque a linha nasce da importação do XML sem origem — o
    /// layout da NF-e não carrega esse vínculo — e porque a entrada NORMAL não tem origem alguma.
    /// </summary>
    public Guid? SalesInvoiceItemKey { get; set; }
    public virtual SalesInvoiceItem? SalesInvoiceItem { get; set; }

    /// <summary>Linha de remessa apontando a linha da NF de venda futura que a antecipou.</summary>
    public Guid? PurchaseInvoiceItemOriginKey { get; set; }
    public virtual PurchaseInvoiceItem? PurchaseInvoiceItemOrigin { get; set; }

    [NotMapped]
    public decimal Total => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.ToEven);

    /// <summary>
    /// Quebra apurada da linha de ORIGEM — o número que o fiscal deveria espelhar.
    /// </summary>
    /// <remarks>
    /// Depende de <see cref="SalesInvoiceItem"/> CARREGADO. Sem o Include a navegação vem null e
    /// isto devolve 0 em silêncio, fazendo toda linha parecer divergente. Quem carrega é o
    /// <c>PurchaseInvoicesGetService</c>.
    /// </remarks>
    [NotMapped]
    public decimal AssessedShortage => SalesInvoiceItem?.AssessedShortage ?? 0m;

    /// <summary>
    /// Devolvido − quebra apurada. Zero é fiscal e físico batendo; diferente de zero a tela avisa
    /// mas NÃO impede gravar — arredondamento e devolução parcial são legítimos.
    /// </summary>
    [NotMapped]
    public decimal Difference =>
        decimal.Round(Quantity - AssessedShortage, 3, MidpointRounding.ToEven);
}
```

- [ ] **Step 6: Create `PurchaseInvoiceComment` and `PurchaseInvoiceChangeLog`**

`SiagroB1.Domain/Entities/PurchaseInvoiceComment.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Anotação livre do documento de entrada, com data, hora e autor, editável a qualquer tempo —
/// inclusive em documento confirmado ou cancelado, porque comentário não altera valor nem saldo.
///
/// Toda inclusão, edição e exclusão gera linha em <see cref="PurchaseInvoiceChangeLog"/> com o
/// código <see cref="ContractChangeLogFields.Comment"/>.
/// </summary>
[Table("PURCHASE_INVOICES_COMMENTS")]
public class PurchaseInvoiceComment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? PurchaseInvoiceKey { get; set; }
    public virtual PurchaseInvoice? PurchaseInvoice { get; set; }

    public DateTime CommentedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? CommentedBy { get; set; }

    /// <summary>500 para casar com <see cref="PurchaseInvoiceChangeLog.NewValue"/>: nada trunca.</summary>
    [Column(TypeName = "VARCHAR(500) NOT NULL")]
    public required string CommentText { get; set; }
}
```

`SiagroB1.Domain/Entities/PurchaseInvoiceChangeLog.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Alteração pontual no documento de entrada, campo a campo. Hoje só recebe linhas de comentário
/// (<see cref="ContractChangeLogFields.Comment"/>); a estrutura é a mesma do log do contrato para
/// receber outros campos depois sem migração de dados.
/// </summary>
[Table("PURCHASE_INVOICES_CHANGE_LOGS")]
public class PurchaseInvoiceChangeLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? PurchaseInvoiceKey { get; set; }
    public virtual PurchaseInvoice? PurchaseInvoice { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? ChangedBy { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string Field { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? OldValue { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? NewValue { get; set; }
}
```

- [ ] **Step 7: Add the DbSets and the unique index**

In `SiagroB1.Infra/Context/AppDbContext.cs`, next to the `SalesInvoice` DbSets, add:

```csharp
public DbSet<PurchaseInvoice> PurchaseInvoices { get; set; }
public DbSet<PurchaseInvoiceItem> PurchaseInvoicesItems { get; set; }
public DbSet<PurchaseInvoiceComment> PurchaseInvoicesComments { get; set; }
public DbSet<PurchaseInvoiceChangeLog> PurchaseInvoicesChangeLogs { get; set; }
```

In `OnModelCreating`, next to the existing `CustomerReturn` index (which Task 7 removes), add:

```csharp
// Uma chave de NF-e, um documento de entrada registrado. Filtrado porque cancelar precisa
// LIBERAR a chave para o relançamento — mesma trava já usada no documento de saída.
modelBuilder.Entity<PurchaseInvoice>()
    .HasIndex(x => x.ChaveNFe)
    .IsUnique()
    .HasFilter($"[ChaveNFe] IS NOT NULL AND [InvoiceStatus] <> {(int)InvoiceStatus.Cancelled}");

// Auto-relação: a remessa aponta a NF de venda futura. Restrict, não Cascade — apagar a nota
// futura não pode levar as remessas junto.
modelBuilder.Entity<PurchaseInvoice>()
    .HasOne(x => x.PurchaseInvoiceOrigin)
    .WithMany()
    .HasForeignKey(x => x.PurchaseInvoiceOriginKey)
    .OnDelete(DeleteBehavior.Restrict);

modelBuilder.Entity<PurchaseInvoiceItem>()
    .HasOne(x => x.PurchaseInvoiceItemOrigin)
    .WithMany()
    .HasForeignKey(x => x.PurchaseInvoiceItemOriginKey)
    .OnDelete(DeleteBehavior.Restrict);

// SalesInvoiceItem é FK OPCIONAL de propósito: entrada NORMAL não tem origem de saída, e uma
// FK obrigatória viraria INNER JOIN zerando a coleção.
modelBuilder.Entity<PurchaseInvoiceItem>()
    .HasOne(x => x.SalesInvoiceItem)
    .WithMany()
    .HasForeignKey(x => x.SalesInvoiceItemKey)
    .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoiceItemCalculationTests`
Expected: PASS, 6 testes.

- [ ] **Step 9: Build the whole solution**

Run: `dotnet build SiagroB1.sln`
Expected: sucesso. `CustomerReturn` ainda existe e coexiste — é esperado até a Task 7.

- [ ] **Step 10: Stage** (não commitar)

```bash
git -C siagro-b1-backend add \
  SiagroB1.Domain/Enums/PurchaseInvoiceType.cs \
  SiagroB1.Domain/Enums/DocumentIssuerType.cs \
  SiagroB1.Domain/Entities/PurchaseInvoice.cs \
  SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs \
  SiagroB1.Domain/Entities/PurchaseInvoiceComment.cs \
  SiagroB1.Domain/Entities/PurchaseInvoiceChangeLog.cs \
  SiagroB1.Infra/Context/AppDbContext.cs \
  SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceItemCalculationTests.cs
```

---

### Task 2: DTOs de rascunho e de origem — ✅ CONCLUÍDA (06/08/2026)

> Build limpo e **suíte inteira em 889/889** — confirmando que as mudanças de modelo da Task 1 não
> quebraram nenhuma outra área.

**Files:**
- Create: `SiagroB1.Domain/Dtos/PurchaseInvoiceDraftDto.cs`
- Create: `SiagroB1.Domain/Dtos/PurchaseInvoiceOriginItemDto.cs`

**Interfaces:**
- Produces: `PurchaseInvoiceDraftDto` (+ `PurchaseInvoiceDraftItemDto`) consumido pela action
  `PurchaseInvoicesImportXml`; `PurchaseInvoiceOriginItemDto` com `[Key] SalesInvoiceItemKey`,
  consumido pela function `PurchaseInvoicesOriginItems`.

- [ ] **Step 1: Create the draft DTO**

`SiagroB1.Domain/Dtos/PurchaseInvoiceDraftDto.cs`:

```csharp
namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Rascunho lido do XML — ainda NÃO gravado. DTO explícito, e não a entidade, porque é isto que
/// faz a resposta da action sair em PascalCase pelo EDM.
/// </summary>
public class PurchaseInvoiceDraftDto
{
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public string? TaxDocumentNumber { get; set; }
    public string? TaxDocumentSeries { get; set; }
    public string? ChaveNFe { get; set; }
    public DateTime? IssueDate { get; set; }
    public decimal TotalDocumentValue { get; set; }
    public string? TaxPayerComments { get; set; }
    public string? XmlFileName { get; set; }
    public List<PurchaseInvoiceDraftItemDto> Items { get; set; } = [];
}

public class PurchaseInvoiceDraftItemDto
{
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? UnitOfMeasureCode { get; set; }
}
```

- [ ] **Step 2: Create the origin DTO**

`SiagroB1.Domain/Dtos/PurchaseInvoiceOriginItemDto.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Linha de saída elegível como ORIGEM de uma devolução. Alimenta o value help da amarração.
/// </summary>
public class PurchaseInvoiceOriginItemDto
{
    /// <summary>Chave do EDM: sem ela a coleção não é endereçável.</summary>
    [Key]
    public Guid SalesInvoiceItemKey { get; set; }

    public string? InvoiceNumber { get; set; }
    public string? TaxDocumentNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public string? UnitOfMeasureCode { get; set; }
    public decimal Quantity { get; set; }
    public decimal DeliveredQuantity { get; set; }
    public decimal QuantityLoss { get; set; }
    public decimal AssessedShortage { get; set; }
    public string? SalesContractCode { get; set; }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build SiagroB1.sln`
Expected: sucesso.

- [ ] **Step 4: Stage**

```bash
git -C siagro-b1-backend add \
  SiagroB1.Domain/Dtos/PurchaseInvoiceDraftDto.cs \
  SiagroB1.Domain/Dtos/PurchaseInvoiceOriginItemDto.cs
```

---

### Task 3: Leitura — Get e origens elegíveis — ✅ CONCLUÍDA (06/08/2026)

> 4 testes (2 a mais que o previsto: `QueryAll` traz as linhas, e `GetByIdAsync` de chave
> inexistente lança `NotFoundException`). Suíte inteira 893/893, build 0 erros.
> Tropeço: faltou `using Microsoft.EntityFrameworkCore;` no serviço de origens — `AsNoTracking`
> é método de extensão e o erro só aparece na compilação.

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesGetService.cs`
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesGetOriginItemsService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesOriginItemsTests.cs`

**Interfaces:**
- Consumes: `IUnitOfWork` (`SiagroB1.Infra`), `NotFoundException` (`SiagroB1.Domain.Exceptions`),
  `PurchaseInvoiceOriginItemDto`.
- Produces: `PurchaseInvoicesGetService.QueryAll() : IQueryable<PurchaseInvoice>` e
  `.GetByIdAsync(Guid) : Task<PurchaseInvoice>`;
  `PurchaseInvoicesGetOriginItemsService.QueryByCardCode(string) : IQueryable<PurchaseInvoiceOriginItemDto>`.

- [ ] **Step 1: Write the failing test**

Create `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesOriginItemsTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Origens elegíveis para amarrar uma devolução — migrado de CustomerReturnsReconciliationTests
/// sem mudança de regra.
/// </summary>
public class PurchaseInvoicesOriginItemsTests
{
    private static SalesInvoiceItem ClosedItem(
        UnitOfWork db, SalesContract contract,
        decimal quantity = 1000m, decimal delivered = 980m, decimal loss = 0m,
        InvoiceStatus status = InvoiceStatus.Confirmed, string cardCode = "C0001")
    {
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(status, cardCode: cardCode);
        var item = SalesContractsAllocationTestSupport.NewItem(
            invoice, contract.Key, releaseKey: null, quantity);

        item.DeliveredQuantity = delivered;
        item.QuantityLoss = loss;
        item.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

        db.Context.SalesInvoices.Add(invoice);

        return item;
    }

    [Fact]
    public async Task Eligible_origins_only_bring_closed_deliveries_with_shortage()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContractsAllocationTestSupport.NewContract(10_000m);
        db.Context.SalesContracts.Add(contract);

        var withShortage = ClosedItem(db, contract, quantity: 1000m, delivered: 980m);
        ClosedItem(db, contract, quantity: 1000m, delivered: 1000m);           // sem quebra
        ClosedItem(db, contract, quantity: 500m, delivered: 400m,
            status: InvoiceStatus.Cancelled);                                   // cancelado
        ClosedItem(db, contract, quantity: 700m, delivered: 600m,
            cardCode: "C0002");                                                 // outro cliente

        var openDelivery = SalesContractsAllocationTestSupport.NewInvoice();
        var openItem = SalesContractsAllocationTestSupport.NewItem(
            openDelivery, contract.Key, null, 900m);
        openItem.DeliveredQuantity = 800m;
        openItem.DeliveryStatus = SalesInvoiceDeliveryStatus.Open;              // entrega aberta
        db.Context.SalesInvoices.Add(openDelivery);

        await db.SaveChangesAsync();

        var origins = await new PurchaseInvoicesGetOriginItemsService(db)
            .QueryByCardCode("C0001")
            .ToListAsync();

        var only = Assert.Single(origins);
        Assert.Equal(withShortage.Key, only.SalesInvoiceItemKey);
        Assert.Equal(20m, only.AssessedShortage);
    }

    [Fact]
    public async Task Get_by_id_loads_the_origin_so_the_shortage_is_not_silently_zero()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContractsAllocationTestSupport.NewContract(10_000m);
        db.Context.SalesContracts.Add(contract);

        var origin = ClosedItem(db, contract, quantity: 1000m, delivered: 980m);
        await db.SaveChangesAsync();

        var invoice = new PurchaseInvoice { CardCode = "C0001", InvoiceType = PurchaseInvoiceType.Return };
        invoice.AddItem(new PurchaseInvoiceItem { Quantity = 20m, SalesInvoiceItemKey = origin.Key });
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var loaded = await new PurchaseInvoicesGetService(db).GetByIdAsync(invoice.Key);

        // Sem o ThenInclude isto voltaria 0 e TODA linha pareceria divergente.
        Assert.Equal(20m, loaded.Items.Single().AssessedShortage);
        Assert.Equal(0m, loaded.Items.Single().Difference);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoicesOriginItemsTests`
Expected: FAIL na compilação — os serviços não existem.

- [ ] **Step 3: Create `PurchaseInvoicesGetService`**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Leitura do documento de entrada.
///
/// O <c>ThenInclude(SalesInvoiceItem)</c> NÃO é decorativo: a quebra apurada e a diferença da
/// linha vêm da linha de origem, e sem ele voltam ZERO EM SILÊNCIO — toda linha de devolução
/// pareceria divergente.
/// </summary>
public class PurchaseInvoicesGetService(IUnitOfWork db)
{
    public IQueryable<PurchaseInvoice> QueryAll()
    {
        return db.Context.PurchaseInvoices
            .Include(x => x.Items)
            .ThenInclude(i => i.SalesInvoiceItem)
            .AsNoTracking();
    }

    public async Task<PurchaseInvoice> GetByIdAsync(Guid key)
    {
        return await db.Context.PurchaseInvoices
                   .Include(x => x.Items)
                   .ThenInclude(i => i.SalesInvoiceItem)
                   .FirstOrDefaultAsync(x => x.Key == key)
               ?? throw new NotFoundException("Documento de entrada não encontrado.");
    }
}
```

- [ ] **Step 4: Create `PurchaseInvoicesGetOriginItemsService`**

```csharp
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Linhas de saída elegíveis como ORIGEM de uma devolução.
///
/// Elegível é a linha que pertence ao mesmo cliente, está em documento confirmado, teve a entrega
/// CONFERIDA e fechada, e apurou quebra maior que zero. Sem entrega fechada não há quebra: o
/// fator efetivo ainda vale 1 e não há nada que o fiscal precise espelhar.
///
/// O filtro de quebra é a FÓRMULA em SQL, e não a propriedade <c>AssessedShortage</c>:
/// [NotMapped] não vira SQL.
/// </summary>
public class PurchaseInvoicesGetOriginItemsService(IUnitOfWork db)
{
    public IQueryable<PurchaseInvoiceOriginItemDto> QueryByCardCode(string cardCode)
    {
        return db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Where(i =>
                i.SalesInvoice!.CardCode == cardCode &&
                i.SalesInvoice.InvoiceStatus == InvoiceStatus.Confirmed &&
                i.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed &&
                i.Quantity - (i.DeliveredQuantity - i.QuantityLoss) > 0)
            .Select(i => new PurchaseInvoiceOriginItemDto
            {
                SalesInvoiceItemKey = i.Key!.Value,
                InvoiceNumber = i.SalesInvoice!.InvoiceNumber,
                TaxDocumentNumber = i.SalesInvoice.TaxDocumentNumber,
                InvoiceDate = i.SalesInvoice.InvoiceDate,
                ItemCode = i.ItemCode,
                ItemName = i.ItemName,
                UnitOfMeasureCode = i.UnitOfMeasureCode,
                Quantity = i.Quantity,
                DeliveredQuantity = i.DeliveredQuantity,
                QuantityLoss = i.QuantityLoss,
                AssessedShortage = i.Quantity - (i.DeliveredQuantity - i.QuantityLoss),
                SalesContractCode = i.SalesContract!.Code,
            });
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoicesOriginItemsTests`
Expected: PASS, 2 testes.

- [ ] **Step 6: Stage**

```bash
git -C siagro-b1-backend add \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesGetService.cs \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesGetOriginItemsService.cs \
  SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesOriginItemsTests.cs
```

---

### Task 4: Create e a trava de chave duplicada — ✅ CONCLUÍDA (06/08/2026)

> 8 testes (3 a mais que o previsto: `CardName` denormalizado do cadastro, `CardName` vindo do XML
> NÃO sobrescrito, e chave em BRANCO não colidindo). Suíte 901/901, build 0 erros.
> O Step 2 do plano (criar o fake) não foi necessário: `Support/FakeBusinessPartnerService.cs` já
> existia e já suportava `taxIds`, que é o que a Task 7 vai precisar.

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesCreateTests.cs`

**Interfaces:**
- Consumes: `IUnitOfWork`, `IBusinessPartnerService` (`SiagroB1.Domain.Interfaces`),
  `DefaultException`.
- Produces: `PurchaseInvoicesCreateService.ExecuteAsync(PurchaseInvoice, string userName) : Task`.

- [ ] **Step 1: Write the failing test**

Create `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesCreateTests.cs`:

```csharp
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

public class PurchaseInvoicesCreateTests
{
    private static PurchaseInvoice NewInvoice(string? chave = "3526080000000000000000000000000000000000001")
    {
        var invoice = new PurchaseInvoice { CardCode = "F0001", ChaveNFe = chave };
        invoice.AddItem(new PurchaseInvoiceItem { ItemCode = "SOJA", Quantity = 10m, UnitPrice = 1m });
        return invoice;
    }

    [Fact]
    public async Task Document_without_items_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new PurchaseInvoicesCreateService(db, new FakeBusinessPartnerService());

        var empty = new PurchaseInvoice { CardCode = "F0001" };

        await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(empty, "tester"));
    }

    [Fact]
    public async Task Document_is_born_pending()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new PurchaseInvoicesCreateService(db, new FakeBusinessPartnerService());

        var invoice = NewInvoice();
        await service.ExecuteAsync(invoice, "tester");

        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);
        Assert.Equal("tester", invoice.CreatedBy);
    }

    [Fact]
    public async Task Duplicated_access_key_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new PurchaseInvoicesCreateService(db, new FakeBusinessPartnerService());

        await service.ExecuteAsync(NewInvoice(), "tester");

        await Assert.ThrowsAsync<DefaultException>(
            () => service.ExecuteAsync(NewInvoice(), "tester"));
    }

    [Fact]
    public async Task Cancelled_document_releases_the_access_key()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new PurchaseInvoicesCreateService(db, new FakeBusinessPartnerService());

        var first = NewInvoice();
        await service.ExecuteAsync(first, "tester");

        first.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();

        // Relançar a mesma NF depois de cancelar é caminho legítimo.
        await service.ExecuteAsync(NewInvoice(), "tester");
    }

    [Fact]
    public async Task Documents_without_access_key_do_not_collide()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new PurchaseInvoicesCreateService(db, new FakeBusinessPartnerService());

        await service.ExecuteAsync(NewInvoice(chave: null), "tester");
        await service.ExecuteAsync(NewInvoice(chave: null), "tester");
    }
}
```

- [ ] **Step 2: Create the fake partner service used by the test**

Create `SiagroB1.Application.Tests/Support/FakeBusinessPartnerService.cs` **only if it does not
already exist** — check first with
`ls SiagroB1.Application.Tests/Support/`. If absent, implement `IBusinessPartnerService` returning
an empty `IQueryable<BusinessPartner>` from `QueryAll()` and `null` from `GetByIdAsync`, throwing
`NotImplementedException` on every write member. Mirror the interface exactly as declared in
`SiagroB1.Domain/Interfaces/IBusinessPartnerService.cs` — read that file before writing the fake.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoicesCreateTests`
Expected: FAIL — `PurchaseInvoicesCreateService` não existe.

- [ ] **Step 4: Create the service**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Registra o documento de entrada.
///
/// Nesta fase é documento de CONTROLE: não grava ledger, não recalcula contrato e não toca em
/// romaneio. O efeito de negócio chega na Fase 3, pelo UsageEffect da natureza de cada linha.
/// </summary>
public class PurchaseInvoicesCreateService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService)
{
    public async Task ExecuteAsync(PurchaseInvoice invoice, string userName)
    {
        if (invoice.Items.Count == 0)
            throw new DefaultException("Informe ao menos um item no documento de entrada.");

        await EnsureChaveNFeIsFreeAsync(invoice.ChaveNFe, invoice.Key);

        invoice.CreatedAt = DateTime.Now;
        invoice.CreatedBy = userName;
        invoice.InvoiceStatus = InvoiceStatus.Pending;
        invoice.CardName ??=
            (await businessPartnerService.GetByIdAsync(invoice.CardCode))?.CardName;

        await db.Context.PurchaseInvoices.AddAsync(invoice);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Uma chave de NF-e, um documento registrado. O índice único no banco é a rede de segurança;
    /// esta checagem existe para a mensagem sair legível em pt-BR. Documento cancelado NÃO segura
    /// a chave — relançar é caminho legítimo.
    /// </summary>
    private async Task EnsureChaveNFeIsFreeAsync(string? chaveNFe, Guid key)
    {
        if (string.IsNullOrWhiteSpace(chaveNFe))
            return;

        var duplicated = await db.Context.PurchaseInvoices
            .AnyAsync(x => x.ChaveNFe == chaveNFe &&
                           x.InvoiceStatus != InvoiceStatus.Cancelled &&
                           x.Key != key);

        if (duplicated)
            throw new DefaultException(
                $"Já existe documento de entrada com a chave de NF-e {chaveNFe}.");
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoicesCreateTests`
Expected: PASS, 5 testes.

- [ ] **Step 6: Stage**

```bash
git -C siagro-b1-backend add \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs \
  SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesCreateTests.cs \
  SiagroB1.Application.Tests/Support/
```

---

### Task 5: Update que PERSISTE as linhas — ✅ CONCLUÍDA (06/08/2026)

> 8 testes, suíte 909/909, build 0 erros. **Duas correções sobre o código do plano**, ambas
> apanhadas por teste:
>
> 1. **Remoção:** tirar da coleção não apaga — `PurchaseInvoiceKey` é nulável, a relação é
>    opcional e o EF deixaria a linha ÓRFÃ com FK nula, invisível pelo `Include` e presente na
>    tabela. Tem de ser `db.Context.PurchaseInvoicesItems.Remove(line)`.
> 2. **Inserção:** adicionar pela NAVEGAÇÃO um item com `Key` preenchido faz o EF lê-lo como
>    registro existente sendo reanexado (a chave é `ValueGeneratedOnAdd`) e emitir UPDATE de linha
>    inexistente — `DbUpdateConcurrencyException`. Tem de ser `Add` no DbSet.
>
> **Aplicar as duas ao espelhar este serviço em qualquer coleção-filha das tarefas seguintes.**

Esta é a correção do bug que a devolução tem hoje: `CustomerReturnsUpdateService` atualiza só o
cabeçalho e nunca toca `existing.Items`, e por isso a amarração só existe no momento da criação.

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesUpdateTests.cs`

**Interfaces:**
- Produces: `PurchaseInvoicesUpdateService.ExecuteAsync(Guid key, PurchaseInvoice entity, string userName) : Task`.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// A regressão que motivou a rotina nova: a devolução antiga só atualizava o cabeçalho, e por
/// isso amarrar uma linha depois de gravar era impossível por qualquer caminho.
/// </summary>
public class PurchaseInvoicesUpdateTests
{
    private static async Task<(UnitOfWork db, PurchaseInvoice saved)> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new PurchaseInvoice { CardCode = "F0001", TaxDocumentNumber = "1" };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", Quantity = 10m, UnitPrice = 1m,
        });
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        return (db, invoice);
    }

    [Fact]
    public async Task Header_fields_are_updated()
    {
        var (db, saved) = await SeedAsync();

        var incoming = new PurchaseInvoice { CardCode = "F0001", TaxDocumentNumber = "999" };
        incoming.AddItem(new PurchaseInvoiceItem
        {
            Key = saved.Items.First().Key, ItemCode = "SOJA", Quantity = 10m, UnitPrice = 1m,
        });

        await new PurchaseInvoicesUpdateService(db).ExecuteAsync(saved.Key, incoming, "tester");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking()
            .FirstAsync(x => x.Key == saved.Key);

        Assert.Equal("999", reloaded.TaxDocumentNumber);
    }

    [Fact]
    public async Task Line_binding_is_persisted()
    {
        var (db, saved) = await SeedAsync();
        var originKey = Guid.NewGuid();

        var incoming = new PurchaseInvoice { CardCode = "F0001" };
        incoming.AddItem(new PurchaseInvoiceItem
        {
            Key = saved.Items.First().Key,
            ItemCode = "SOJA", Quantity = 10m, UnitPrice = 1m,
            SalesInvoiceItemKey = originKey,
        });

        await new PurchaseInvoicesUpdateService(db).ExecuteAsync(saved.Key, incoming, "tester");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking()
            .Include(x => x.Items).FirstAsync(x => x.Key == saved.Key);

        Assert.Equal(originKey, reloaded.Items.Single().SalesInvoiceItemKey);
    }

    [Fact]
    public async Task New_line_is_inserted_and_removed_line_is_deleted()
    {
        var (db, saved) = await SeedAsync();

        var incoming = new PurchaseInvoice { CardCode = "F0001" };
        // A linha original NÃO vem: deve ser removida. Uma nova entra no lugar.
        incoming.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "MILHO", Quantity = 5m, UnitPrice = 2m,
        });

        await new PurchaseInvoicesUpdateService(db).ExecuteAsync(saved.Key, incoming, "tester");

        var reloaded = await db.Context.PurchaseInvoices.AsNoTracking()
            .Include(x => x.Items).FirstAsync(x => x.Key == saved.Key);

        Assert.Equal("MILHO", reloaded.Items.Single().ItemCode);
    }

    [Fact]
    public async Task Cancelled_document_cannot_be_updated()
    {
        var (db, saved) = await SeedAsync();

        var tracked = await db.Context.PurchaseInvoices.FirstAsync(x => x.Key == saved.Key);
        tracked.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var incoming = new PurchaseInvoice { CardCode = "F0001" };
        incoming.AddItem(new PurchaseInvoiceItem { ItemCode = "SOJA", Quantity = 1m });

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesUpdateService(db).ExecuteAsync(saved.Key, incoming, "tester"));
    }

    [Fact]
    public async Task Confirmed_document_cannot_be_updated()
    {
        var (db, saved) = await SeedAsync();

        var tracked = await db.Context.PurchaseInvoices.FirstAsync(x => x.Key == saved.Key);
        tracked.InvoiceStatus = InvoiceStatus.Confirmed;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var incoming = new PurchaseInvoice { CardCode = "F0001" };
        incoming.AddItem(new PurchaseInvoiceItem { ItemCode = "SOJA", Quantity = 1m });

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesUpdateService(db).ExecuteAsync(saved.Key, incoming, "tester"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoicesUpdateTests`
Expected: FAIL — serviço não existe.

- [ ] **Step 3: Create the service**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Atualiza o documento de entrada — cabeçalho E LINHAS.
///
/// As linhas são o ponto: a AMARRAÇÃO com a nota de origem é manual e normalmente feita depois de
/// importar o XML. O serviço equivalente da devolução antiga nunca tocava a coleção, e por isso
/// amarrar depois de gravar não era possível por caminho nenhum.
///
/// Só documento PENDENTE é alterável: confirmado tem efeito de negócio pendurado (Fase 3) e
/// precisa passar pelo estorno.
/// </summary>
public class PurchaseInvoicesUpdateService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, PurchaseInvoice entity, string userName)
    {
        var existing = await db.Context.PurchaseInvoices
                           .Include(x => x.Items)
                           .FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (existing.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException(
                "Somente documento pendente pode ser alterado. Estorne a confirmação antes.");

        existing.InvoiceType = entity.InvoiceType;
        existing.IssuerType = entity.IssuerType;
        existing.InvoiceNumber = entity.InvoiceNumber;
        existing.TaxDocumentNumber = entity.TaxDocumentNumber;
        existing.TaxDocumentSeries = entity.TaxDocumentSeries;
        existing.ChaveNFe = entity.ChaveNFe;
        existing.IssueDate = entity.IssueDate;
        existing.PostingDate = entity.PostingDate;
        existing.TotalDocumentValue = entity.TotalDocumentValue;
        existing.TaxPayerComments = entity.TaxPayerComments;
        existing.Comments = entity.Comments;
        existing.GrossWeight = entity.GrossWeight;
        existing.NetWeight = entity.NetWeight;
        existing.TruckCode = entity.TruckCode;
        existing.TruckingCompanyCode = entity.TruckingCompanyCode;
        existing.TruckingCompanyName = entity.TruckingCompanyName;
        existing.FreightTerms = entity.FreightTerms;
        existing.PurchaseInvoiceOriginKey = entity.PurchaseInvoiceOriginKey;
        existing.UpdatedAt = DateTime.Now;
        existing.UpdatedBy = userName;

        SyncItems(existing, entity);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Reconcilia a coleção pela chave da linha: casa atualiza, sobra no entrante insere, sobra no
    /// existente remove. Reconciliar em vez de limpar-e-recriar preserva o RowId e a identidade
    /// das linhas que só mudaram de amarração.
    /// </summary>
    private static void SyncItems(PurchaseInvoice existing, PurchaseInvoice entity)
    {
        var incoming = entity.Items.ToList();

        var removed = existing.Items
            .Where(current => incoming.All(i => i.Key != current.Key))
            .ToList();

        foreach (var line in removed)
            existing.Items.Remove(line);

        foreach (var line in incoming)
        {
            var current = existing.Items.FirstOrDefault(x => x.Key == line.Key && line.Key != null);

            if (current is null)
            {
                existing.Items.Add(new PurchaseInvoiceItem
                {
                    Key = line.Key ?? Guid.NewGuid(),
                    PurchaseInvoiceKey = existing.Key,
                    ItemCode = line.ItemCode,
                    ItemName = line.ItemName,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    UnitOfMeasureCode = line.UnitOfMeasureCode,
                    SalesInvoiceItemKey = line.SalesInvoiceItemKey,
                    PurchaseInvoiceItemOriginKey = line.PurchaseInvoiceItemOriginKey,
                });

                continue;
            }

            current.ItemCode = line.ItemCode;
            current.ItemName = line.ItemName;
            current.Quantity = line.Quantity;
            current.UnitPrice = line.UnitPrice;
            current.UnitOfMeasureCode = line.UnitOfMeasureCode;
            current.SalesInvoiceItemKey = line.SalesInvoiceItemKey;
            current.PurchaseInvoiceItemOriginKey = line.PurchaseInvoiceItemOriginKey;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoicesUpdateTests`
Expected: PASS, 5 testes.

- [ ] **Step 5: Stage**

```bash
git -C siagro-b1-backend add \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs \
  SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesUpdateTests.cs
```

---

### Task 6: Ciclo de vida — Delete, Confirm, ReverseConfirm, Cancel — ✅ CONCLUÍDA (06/08/2026)

> 12 testes, suíte 921/921, build 0 erros.
>
> **Desvio de assinatura:** `PurchaseInvoicesDeleteService.ExecuteAsync(Guid key)` — SEM
> `userName`. O registro deixa de existir, não há o que carimbar, e parâmetro não usado é pior
> que a quebra de uniformidade. Os outros três mantêm `(Guid, string)`.
>
> **Não espelhar `SalesInvoicesDeleteService`:** ele retorna `bool` e lança `ApplicationException`,
> que o controller converte em 500. Aqui é `DefaultException`/`NotFoundException`, que viram
> 400/404. Também não há transação explícita: tudo é um único `SaveChanges`, então já é atômico —
> e `RollbackAsync` sem `BeginTransaction` é no-op silencioso neste repo.

**Files:**
- Create: `.../PurchaseInvoicesDeleteService.cs`, `PurchaseInvoicesConfirmService.cs`,
  `PurchaseInvoicesReverseConfirmService.cs`, `PurchaseInvoicesCancelService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesLifecycleTests.cs`

**Interfaces:**
- Produces: cada serviço expõe `ExecuteAsync(Guid key, string userName) : Task`.

- [ ] **Step 1: Write the failing test**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

public class PurchaseInvoicesLifecycleTests
{
    private static async Task<(UnitOfWork db, PurchaseInvoice invoice)> SeedAsync(
        InvoiceStatus status = InvoiceStatus.Pending)
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new PurchaseInvoice { CardCode = "F0001", InvoiceStatus = status };
        invoice.AddItem(new PurchaseInvoiceItem { ItemCode = "SOJA", Quantity = 10m, UnitPrice = 1m });
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return (db, invoice);
    }

    [Fact]
    public async Task Confirm_moves_pending_to_confirmed()
    {
        var (db, invoice) = await SeedAsync();

        await new PurchaseInvoicesConfirmService(db).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, invoice.InvoiceStatus);
        Assert.Equal("tester", invoice.ApprovedBy);
    }

    [Fact]
    public async Task Confirm_refuses_a_document_that_is_not_pending()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Confirmed);

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesConfirmService(db).ExecuteAsync(invoice.Key, "tester"));
    }

    [Fact]
    public async Task Reverse_confirm_moves_confirmed_back_to_pending()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Confirmed);

        await new PurchaseInvoicesReverseConfirmService(db).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);
    }

    [Fact]
    public async Task Reverse_confirm_refuses_a_cancelled_document()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Cancelled);

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesReverseConfirmService(db).ExecuteAsync(invoice.Key, "tester"));
    }

    [Fact]
    public async Task Cancel_marks_the_document_and_records_the_author()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Confirmed);

        await new PurchaseInvoicesCancelService(db).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, invoice.InvoiceStatus);
        Assert.Equal("tester", invoice.CanceledBy);
        Assert.NotNull(invoice.CanceledAt);
    }

    [Fact]
    public async Task Cancel_refuses_an_already_cancelled_document()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Cancelled);

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesCancelService(db).ExecuteAsync(invoice.Key, "tester"));
    }

    [Fact]
    public async Task Delete_removes_a_pending_document_with_its_lines()
    {
        var (db, invoice) = await SeedAsync();

        await new PurchaseInvoicesDeleteService(db).ExecuteAsync(invoice.Key, "tester");

        Assert.False(await db.Context.PurchaseInvoices.AnyAsync(x => x.Key == invoice.Key));
        Assert.False(await db.Context.PurchaseInvoicesItems
            .AnyAsync(x => x.PurchaseInvoiceKey == invoice.Key));
    }

    [Fact]
    public async Task Delete_refuses_a_confirmed_document()
    {
        var (db, invoice) = await SeedAsync(InvoiceStatus.Confirmed);

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesDeleteService(db).ExecuteAsync(invoice.Key, "tester"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoicesLifecycleTests`
Expected: FAIL — os 4 serviços não existem.

- [ ] **Step 3: Create `PurchaseInvoicesConfirmService`**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Confirma o documento de entrada: fecha para edição e o torna definitivo para a conciliação.
///
/// Nesta fase a confirmação SÓ transiciona o status. A Fase 3 pendura aqui o efeito da natureza
/// de operação sobre o contrato de compra, sem mexer nesta máquina de estados.
/// </summary>
public class PurchaseInvoicesConfirmService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.PurchaseInvoices
                          .FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException("Somente documento pendente pode ser confirmado.");

        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        invoice.ApprovedAt = DateTime.Now;
        invoice.ApprovedBy = userName;

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Create `PurchaseInvoicesReverseConfirmService`**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Estorna a confirmação, devolvendo o documento a pendente para poder ser corrigido.
/// Nesta fase não há efeito a desfazer — ver <see cref="PurchaseInvoicesConfirmService"/>.
/// </summary>
public class PurchaseInvoicesReverseConfirmService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.PurchaseInvoices
                          .FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (invoice.InvoiceStatus != InvoiceStatus.Confirmed)
            throw new DefaultException("Somente documento confirmado pode ser estornado.");

        invoice.InvoiceStatus = InvoiceStatus.Pending;
        invoice.ApprovedAt = null;
        invoice.ApprovedBy = null;
        invoice.UpdatedAt = DateTime.Now;
        invoice.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Create `PurchaseInvoicesCancelService`**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Cancela o documento de entrada.
///
/// Nesta fase não há nada para estornar — o documento nunca moveu saldo, ledger ou romaneio.
/// Cancelar tira o registro da conciliação e LIBERA a chave de NF-e para relançamento, sem
/// apagar o documento (o índice único é filtrado por status).
/// </summary>
public class PurchaseInvoicesCancelService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.PurchaseInvoices
                          .FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento de entrada já está cancelado.");

        invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        invoice.CanceledAt = DateTime.Now;
        invoice.CanceledBy = userName;

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 6: Create `PurchaseInvoicesDeleteService`**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Exclui o documento de entrada e suas linhas. Só documento PENDENTE — depois de confirmado o
/// caminho é estornar e cancelar, que preserva o rastro. Mesma regra de
/// <c>SalesInvoicesDeleteService</c>.
/// </summary>
public class PurchaseInvoicesDeleteService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.PurchaseInvoices
                          .Include(x => x.Items)
                          .FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException(
                "Somente documento pendente pode ser excluído. Cancele o documento.");

        db.Context.PurchaseInvoicesItems.RemoveRange(invoice.Items);
        db.Context.PurchaseInvoices.Remove(invoice);

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoicesLifecycleTests`
Expected: PASS, 8 testes.

- [ ] **Step 8: Stage**

```bash
git -C siagro-b1-backend add \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesConfirmService.cs \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesReverseConfirmService.cs \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCancelService.cs \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesDeleteService.cs \
  SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesLifecycleTests.cs
```

---

### Task 7: Importação do XML da NF-e — ✅ CONCLUÍDA (06/08/2026)

> 10 testes (o plano previa 5), suíte 931/931, build 0 erros e **sem CS1998**.
>
> **Correção sobre o código do plano:** ele tinha `await Task.CompletedTask;` para calar o aviso de
> async-sem-await. Trocado por método NÃO-async devolvendo `Task.FromResult` — a leitura é toda em
> memória. Quando a Fase 2 consultar o cadastro de naturezas, o `async` volta com await de verdade.
>
> O `FakeBusinessPartnerService` de `Support/` já servia: `names` + `taxIds` com a mesma chave.

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesImportXmlService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesImportXmlServiceTests.cs`

**Interfaces:**
- Consumes: `IBusinessPartnerService`, `PurchaseInvoiceDraftDto`.
- Produces: `PurchaseInvoicesImportXmlService.ExecuteAsync(byte[] xmlData, string fileName) : Task<PurchaseInvoiceDraftDto>`.

> Muda em relação ao serviço da devolução: devolve o **DTO de rascunho**, não a entidade, e grava
> em `ChaveNFe`/`TaxDocumentNumber`/`TaxDocumentSeries`/`TotalDocumentValue`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

public class PurchaseInvoicesImportXmlServiceTests
{
    private const string Nfe = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<nfeProc xmlns=""http://www.portalfiscal.inf.br/nfe"">
  <NFe>
    <infNFe Id=""NFe35260800000000000000550010000000011000000017"">
      <ide><nNF>1</nNF><serie>1</serie><dhEmi>2026-08-05T10:00:00-03:00</dhEmi></ide>
      <emit><CNPJ>12345678000199</CNPJ><xNome>PRODUTOR TESTE</xNome></emit>
      <det nItem=""1"">
        <prod><cProd>SOJA</cProd><xProd>SOJA EM GRAOS</xProd><uCom>KG</uCom>
        <qCom>1000.000</qCom><vUnCom>1.5000</vUnCom></prod>
      </det>
      <total><ICMSTot><vNF>1500.00</vNF></ICMSTot></total>
      <infAdic><infCpl>Ref. NF 123 serie 1</infCpl></infAdic>
    </infNFe>
  </NFe>
</nfeProc>";

    private static PurchaseInvoicesImportXmlService Service() =>
        new(new FakeBusinessPartnerService(cardCode: "F0001", taxId: "12.345.678/0001-99"));

    [Fact]
    public async Task Header_is_read_from_the_xml()
    {
        var draft = await Service().ExecuteAsync(Encoding.UTF8.GetBytes(Nfe), "nfe.xml");

        Assert.Equal("35260800000000000000550010000000011000000017", draft.ChaveNFe);
        Assert.Equal("1", draft.TaxDocumentNumber);
        Assert.Equal("1", draft.TaxDocumentSeries);
        Assert.Equal(1500.00m, draft.TotalDocumentValue);
        Assert.Equal("Ref. NF 123 serie 1", draft.TaxPayerComments);
        Assert.Equal("F0001", draft.CardCode);
        Assert.Equal("nfe.xml", draft.XmlFileName);
    }

    [Fact]
    public async Task Lines_are_read_from_det()
    {
        var draft = await Service().ExecuteAsync(Encoding.UTF8.GetBytes(Nfe), "nfe.xml");

        var line = Assert.Single(draft.Items);
        Assert.Equal("SOJA", line.ItemCode);
        Assert.Equal("SOJA EM GRAOS", line.ItemName);
        Assert.Equal("KG", line.UnitOfMeasureCode);
        Assert.Equal(1000m, line.Quantity);
        Assert.Equal(1.5m, line.UnitPrice);
    }

    [Fact]
    public async Task Empty_file_is_refused()
    {
        await Assert.ThrowsAsync<DefaultException>(() => Service().ExecuteAsync([], "x.xml"));
    }

    [Fact]
    public async Task Non_xml_content_is_refused()
    {
        await Assert.ThrowsAsync<DefaultException>(
            () => Service().ExecuteAsync(Encoding.UTF8.GetBytes("não é xml"), "x.xml"));
    }

    [Fact]
    public async Task Unknown_issuer_is_refused()
    {
        var service = new PurchaseInvoicesImportXmlService(
            new FakeBusinessPartnerService(cardCode: "F0002", taxId: "99.999.999/0001-99"));

        await Assert.ThrowsAsync<DefaultException>(
            () => service.ExecuteAsync(Encoding.UTF8.GetBytes(Nfe), "nfe.xml"));
    }
}
```

> `FakeBusinessPartnerService` precisa aceitar `cardCode`/`taxId` opcionais e devolvê-los em
> `QueryAll()`. Estender o fake criado na Task 4 com esse construtor.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoicesImportXmlServiceTests`
Expected: FAIL — serviço não existe.

- [ ] **Step 3: Create the service**

```csharp
using System.Globalization;
using System.Xml.Linq;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Lê o XML da NF-e de entrada e monta o rascunho do cabeçalho e das linhas, para o operador não
/// redigitar. SÓ LÊ — quem grava é o POST do documento.
///
/// NÃO tenta adivinhar a amarração linha → NF de origem. O layout guarda as referências em
/// <c>ide/NFref</c>, que é do CABEÇALHO: o XML não diz qual linha veio de qual origem. Os
/// emitentes escrevem isso em texto livre no <c>infAdic/infCpl</c>, preservado e exibido na tela.
/// Casar por quantidade erraria em silêncio, que aqui é o pior tipo de erro.
///
/// Lido com <see cref="XDocument"/> e não com a Zeus.Net.NFe: a biblioteca está referenciada no
/// Infra mas nunca foi exercitada neste projeto, e o valor dela é na EMISSÃO. A leitura de
/// <c>ide/NFref</c> e dos campos fiscais entra na Fase 2.
/// </summary>
public class PurchaseInvoicesImportXmlService(IBusinessPartnerService businessPartnerService)
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    public async Task<PurchaseInvoiceDraftDto> ExecuteAsync(byte[] xmlData, string fileName)
    {
        if (xmlData is null || xmlData.Length == 0)
            throw new DefaultException("Arquivo XML vazio.");

        XDocument document;

        try
        {
            document = XDocument.Parse(System.Text.Encoding.UTF8.GetString(xmlData));
        }
        catch (Exception)
        {
            throw new DefaultException("Arquivo não é um XML válido.");
        }

        // Pode vir como <nfeProc> (com protocolo) ou <NFe> puro.
        var infNfe = document.Descendants(Nfe + "infNFe").FirstOrDefault()
                     ?? throw new DefaultException(
                         "XML não parece uma NF-e: elemento infNFe não encontrado.");

        var ide = infNfe.Element(Nfe + "ide");
        var emit = infNfe.Element(Nfe + "emit");
        var cnpj = Value(emit, "CNPJ") ?? Value(emit, "CPF");

        var draft = new PurchaseInvoiceDraftDto
        {
            // A chave vem no atributo Id como "NFe" + 44 dígitos.
            ChaveNFe = (infNfe.Attribute("Id")?.Value ?? string.Empty)
                .Replace("NFe", string.Empty, StringComparison.OrdinalIgnoreCase),
            TaxDocumentNumber = Value(ide, "nNF"),
            TaxDocumentSeries = Value(ide, "serie"),
            IssueDate = ParseDate(Value(ide, "dhEmi") ?? Value(ide, "dEmi")),
            TotalDocumentValue = ParseDecimal(
                infNfe.Descendants(Nfe + "ICMSTot").FirstOrDefault(), "vNF"),
            TaxPayerComments = Value(infNfe.Element(Nfe + "infAdic"), "infCpl"),
            CardName = Value(emit, "xNome"),
            CardCode = await ResolveCardCodeAsync(cnpj),
            XmlFileName = fileName,
        };

        foreach (var det in infNfe.Elements(Nfe + "det"))
        {
            var prod = det.Element(Nfe + "prod");

            draft.Items.Add(new PurchaseInvoiceDraftItemDto
            {
                ItemCode = Value(prod, "cProd"),
                ItemName = Value(prod, "xProd"),
                UnitOfMeasureCode = Value(prod, "uCom"),
                Quantity = ParseDecimal(prod, "qCom"),
                UnitPrice = ParseDecimal(prod, "vUnCom"),
            });
        }

        if (draft.Items.Count == 0)
            throw new DefaultException("XML sem itens (det).");

        return draft;
    }

    /// <summary>
    /// Resolve o emitente pelo CNPJ. Não encontrar é erro de negócio explícito: sem parceiro não
    /// há como listar as notas de origem, e deixar em branco só adiaria a descoberta.
    /// </summary>
    private async Task<string> ResolveCardCodeAsync(string? cnpj)
    {
        if (string.IsNullOrWhiteSpace(cnpj))
            throw new DefaultException("XML sem CNPJ/CPF do emitente.");

        var digits = new string(cnpj.Where(char.IsDigit).ToArray());

        var partner = businessPartnerService.QueryAll()
            .FirstOrDefault(p => p.TaxId != null && p.TaxId.Replace(".", "")
                .Replace("/", "").Replace("-", "") == digits);

        await Task.CompletedTask;

        return partner?.CardCode
               ?? throw new DefaultException(
                   $"Nenhum parceiro cadastrado com o CNPJ/CPF {cnpj} do emitente do XML.");
    }

    private static string? Value(XElement? parent, string name) =>
        parent?.Element(Nfe + name)?.Value;

    private static decimal ParseDecimal(XElement? parent, string name) =>
        decimal.TryParse(Value(parent, name), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoicesImportXmlServiceTests`
Expected: PASS, 5 testes.

- [ ] **Step 5: Stage**

```bash
git -C siagro-b1-backend add \
  SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesImportXmlService.cs \
  SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesImportXmlServiceTests.cs \
  SiagroB1.Application.Tests/Support/FakeBusinessPartnerService.cs
```

---

### Task 8: Serviços de item, comentário e log — ✅ CONCLUÍDA (06/08/2026)

> 19 testes (3 previstos), suíte 950/950, build 0 erros. 12 serviços + `PurchaseInvoiceLineGuard`.
>
> **Correções sobre o que o plano supunha:** os serviços de comentário/log recebem `AppDbContext`
> direto (não `IUnitOfWork`); o método do log chama-se **`Register`**, não `Enqueue`; e
> Update/Delete de comentário recebem **`bool isAdmin`** além de `userName`. Confirmado lendo os
> moldes de venda, como o Step 1 mandava.
>
> **Guarda nova, sem par no lado de venda:** `PurchaseInvoiceLineGuard.EnsureParentIsPendingAsync`
> nos 3 serviços de escrita de linha. A grade grava por POST/PATCH/DELETE direto em
> `/PurchaseInvoicesItems`, que NÃO passa pelo Update do cabeçalho — sem isso, mexer na linha era
> a porta dos fundos para alterar documento confirmado.
>
> **NÃO espelhar os `ItemsDelete`/`Delete` de venda:** eles embrulham uma transação cujo
> `RollbackAsync` é no-op e devolvem `bool` em vez de lançar.

**Files:**
- Create: `.../PurchaseInvoicesItems{Get,Create,Update,Delete}Service.cs`
- Create: `.../PurchaseInvoicesChangeLogService.cs`, `.../PurchaseInvoicesChangeLogsGetService.cs`
- Create: `.../PurchaseInvoicesComment{Create,Update,Delete}Service.cs`, `.../PurchaseInvoicesCommentsGetService.cs`
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesCommentTests.cs`

**Interfaces:**
- Consumes: `ContractCommentRules.EnsureCanModify` e `ContractChangeLogFields.Comment`
  (`SiagroB1.Domain.Entities`), `IUnitOfWork`.
- Produces: `PurchaseInvoicesItemsGetService.QueryAll()`;
  `PurchaseInvoicesChangeLogService.Enqueue(Guid invoiceKey, string field, string? oldValue, string? newValue, string userName)`
  (**só enfileira, não chama SaveChanges** — porta única de escrita do log);
  `PurchaseInvoicesCommentCreateService.ExecuteAsync(Guid invoiceKey, string text, string userName)`,
  `...CommentUpdateService.ExecuteAsync(Guid commentKey, string text, string userName)`,
  `...CommentDeleteService.ExecuteAsync(Guid commentKey, string userName)`.

- [ ] **Step 1: Read the sales-side originals to mirror exactly**

Read, in order — cada um é o molde do seu par:
`SiagroB1.Application/Services/SalesInvoices/SalesInvoicesItemsGetService.cs`,
`SalesInvoicesItemsCreateService.cs`, `SalesInvoicesItemsUpdateService.cs`,
`SalesInvoicesItemsDeleteService.cs`, `SalesInvoicesChangeLogService.cs`,
`SalesInvoicesChangeLogsGetService.cs`, `SalesInvoicesCommentCreateService.cs`,
`SalesInvoicesCommentUpdateService.cs`, `SalesInvoicesCommentDeleteService.cs`,
`SalesInvoicesCommentsGetService.cs`.

**Não copiar do de saída:** o `ItemsUpdateService` de venda dispara
`SalesContractsRecalculateBalanceService` ao detectar mudança de entrega. O de entrada **não tem
esse gancho nesta fase** — nenhum efeito em contrato existe ainda.

- [ ] **Step 2: Write the failing test for comments**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

public class PurchaseInvoicesCommentTests
{
    private static async Task<(UnitOfWork db, PurchaseInvoice invoice)> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new PurchaseInvoice { CardCode = "F0001", InvoiceStatus = InvoiceStatus.Confirmed };
        invoice.AddItem(new PurchaseInvoiceItem { ItemCode = "SOJA", Quantity = 1m });
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return (db, invoice);
    }

    [Fact]
    public async Task Comment_can_be_created_on_a_confirmed_document()
    {
        var (db, invoice) = await SeedAsync();

        // Comentário não altera valor, peso nem saldo: vale em qualquer status.
        await new PurchaseInvoicesCommentCreateService(db, new PurchaseInvoicesChangeLogService(db))
            .ExecuteAsync(invoice.Key, "conferido com o motorista", "ana");

        var comment = await db.Context.PurchaseInvoicesComments
            .SingleAsync(c => c.PurchaseInvoiceKey == invoice.Key);

        Assert.Equal("conferido com o motorista", comment.CommentText);
        Assert.Equal("ana", comment.CommentedBy);
    }

    [Fact]
    public async Task Creating_a_comment_writes_a_change_log_line()
    {
        var (db, invoice) = await SeedAsync();

        await new PurchaseInvoicesCommentCreateService(db, new PurchaseInvoicesChangeLogService(db))
            .ExecuteAsync(invoice.Key, "primeiro", "ana");

        var log = await db.Context.PurchaseInvoicesChangeLogs
            .SingleAsync(l => l.PurchaseInvoiceKey == invoice.Key);

        Assert.Equal(ContractChangeLogFields.Comment, log.Field);
        Assert.Null(log.OldValue);
        Assert.Equal("primeiro", log.NewValue);
        Assert.Equal("ana", log.ChangedBy);
    }

    [Fact]
    public async Task Another_user_cannot_edit_someone_elses_comment()
    {
        var (db, invoice) = await SeedAsync();
        var logService = new PurchaseInvoicesChangeLogService(db);

        await new PurchaseInvoicesCommentCreateService(db, logService)
            .ExecuteAsync(invoice.Key, "da ana", "ana");

        var comment = await db.Context.PurchaseInvoicesComments.SingleAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => new PurchaseInvoicesCommentUpdateService(db, logService)
                .ExecuteAsync(comment.Key!.Value, "editado", "bruno"));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoicesCommentTests`
Expected: FAIL — serviços não existem.

- [ ] **Step 4: Implement the twelve services**

Escrever cada um espelhando o molde lido no Step 1, trocando `SalesInvoice`→`PurchaseInvoice`,
`SalesInvoiceKey`→`PurchaseInvoiceKey`, `SalesInvoices*`→`PurchaseInvoices*` nos DbSets, e as
mensagens de erro de "documento de saída" para "documento de entrada". Manter:

- `PurchaseInvoicesChangeLogService` **apenas enfileira** (`db.Context.PurchaseInvoicesChangeLogs.Add`)
  e **não chama `SaveChangesAsync`** — quem salva é o serviço chamador, para log e comentário
  entrarem na mesma transação.
- `PurchaseInvoicesCommentsGetService` e `ChangeLogsGetService` ordenam **descendente** por
  `CommentedAt` / `ChangedAt`.
- Autoria por `ContractCommentRules.EnsureCanModify` (autor ou admin).
- Comentário **sem guarda de status**.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~PurchaseInvoicesCommentTests`
Expected: PASS, 3 testes.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS. Os testes de `CustomerReturns` ainda existem e passam — some na Task 10.

- [ ] **Step 7: Stage**

```bash
git -C siagro-b1-backend add SiagroB1.Application/Services/PurchaseInvoices/ SiagroB1.Application.Tests/PurchaseInvoices/
```

---

### Task 9: DI, EDM, controllers e actions — ✅ CONCLUÍDA (06/08/2026)

> 4 controllers + 7 actions + DI + EDM. Build 0 erros, suíte 951/951.
>
> **`$metadata` conferido em runtime** (Web no profile `dev`): os 4 EntitySets, as 7 actions, a
> function e as 4 propriedades calculadas (`TotalInvoiceItems`, `Total`, `AssessedShortage`,
> `Difference`) estão no EDM.
>
> **DI validado exercitando endpoint**, não só pelo build: `PurchaseInvoicesOriginItems` respondeu
> **200**. `/PurchaseInvoices` e `/PurchaseInvoicesItems` devolveram **SQL 208 "Invalid object
> name"** — esperado nesta altura: as tabelas só nascem na Task 11. O 208 é a prova de que o
> controller foi construído e o serviço rodou; se o DI estivesse errado a falha seria na
> resolução, antes do SQL.
>
> **Correção antes dos controllers:** `PurchaseInvoicesGetService.GetByIdAsync` passou a usar
> `AsNoTracking()`. O PATCH do OData chama esse método, aplica o `Delta` sobre o que volta e
> entrega ao Update — com entidade rastreada, o Update carregaria O MESMO objeto como "existente"
> e a reconciliação das linhas viraria no-op SILENCIOSO. Coberto por
> `Patch_flow_through_get_by_id_actually_applies_the_changes`.
>
> Faltava `using SiagroB1.Application.Services.PurchaseInvoices;` em
> `ServiceCollectionExtensions.cs` (19 erros de compilação).

**Files:**
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`
- Create: `SiagroB1.Web/Controllers/PurchaseInvoicesController.cs`,
  `PurchaseInvoicesItemsController.cs`, `PurchaseInvoicesCommentsController.cs`,
  `PurchaseInvoicesChangeLogsController.cs`
- Create: `SiagroB1.Web/Actions/PurchaseInvoices/PurchaseInvoicesConfirmController.cs`,
  `...ReverseConfirmController.cs`, `...CancelController.cs`, `...ImportXmlController.cs`,
  `...CommentCreateController.cs`, `...CommentUpdateController.cs`, `...CommentDeleteController.cs`

**Interfaces:**
- Consumes: todos os serviços das Tasks 3-8.
- Produces: endpoints OData `/odata/PurchaseInvoices`, `/odata/PurchaseInvoicesItems`,
  `/odata/PurchaseInvoicesComments`, `/odata/PurchaseInvoicesChangeLogs`; actions
  `/odata/PurchaseInvoices{Confirm,ReverseConfirm,Cancel,ImportXml,CommentCreate,CommentUpdate,CommentDelete}`;
  function `/odata/PurchaseInvoicesOriginItems(CardCode={cardCode})`.

- [ ] **Step 1: Register the services in DI**

Em `AddApplicationServices()`, logo depois do bloco `// sales invoices`, inserir:

```csharp
// documento de entrada (NF de terceiro e emissão própria)
services.AddScoped<PurchaseInvoicesGetService>();
services.AddScoped<PurchaseInvoicesGetOriginItemsService>();
services.AddScoped<PurchaseInvoicesCreateService>();
services.AddScoped<PurchaseInvoicesUpdateService>();
services.AddScoped<PurchaseInvoicesDeleteService>();
services.AddScoped<PurchaseInvoicesConfirmService>();
services.AddScoped<PurchaseInvoicesReverseConfirmService>();
services.AddScoped<PurchaseInvoicesCancelService>();
services.AddScoped<PurchaseInvoicesImportXmlService>();
services.AddScoped<PurchaseInvoicesItemsGetService>();
services.AddScoped<PurchaseInvoicesItemsCreateService>();
services.AddScoped<PurchaseInvoicesItemsUpdateService>();
services.AddScoped<PurchaseInvoicesItemsDeleteService>();
services.AddScoped<PurchaseInvoicesChangeLogService>();
services.AddScoped<PurchaseInvoicesChangeLogsGetService>();
services.AddScoped<PurchaseInvoicesCommentCreateService>();
services.AddScoped<PurchaseInvoicesCommentUpdateService>();
services.AddScoped<PurchaseInvoicesCommentDeleteService>();
services.AddScoped<PurchaseInvoicesCommentsGetService>();
```

- [ ] **Step 2: Declare the EDM**

Em `ODataConfigurations.cs`, junto do bloco de `SalesInvoice`:

```csharp
// Documento de entrada: NF de fornecedor, venda futura, remessa e devolução de cliente.
modelBuilder.EntitySet<PurchaseInvoice>("PurchaseInvoices");
modelBuilder.EntitySet<PurchaseInvoiceItem>("PurchaseInvoicesItems");
modelBuilder.EntitySet<PurchaseInvoiceComment>("PurchaseInvoicesComments");
modelBuilder.EntitySet<PurchaseInvoiceChangeLog>("PurchaseInvoicesChangeLogs");

// Calculadas: a convenção do ODataConventionModelBuilder não as inclui sozinha, e sem estas
// linhas o $select devolve 400.
modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoice))
    .AddProperty(typeof(PurchaseInvoice).GetProperty(nameof(PurchaseInvoice.TotalInvoiceItems)));
modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoiceItem))
    .AddProperty(typeof(PurchaseInvoiceItem).GetProperty(nameof(PurchaseInvoiceItem.Total)));
modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoiceItem))
    .AddProperty(typeof(PurchaseInvoiceItem).GetProperty(nameof(PurchaseInvoiceItem.AssessedShortage)));
modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoiceItem))
    .AddProperty(typeof(PurchaseInvoiceItem).GetProperty(nameof(PurchaseInvoiceItem.Difference)));

var purchaseInvoicesImportXml = modelBuilder.Action("PurchaseInvoicesImportXml");
purchaseInvoicesImportXml.Parameter<string>("XmlContent");
purchaseInvoicesImportXml.Parameter<string>("FileName");
// Tipado, e não IActionResult: é o que faz a resposta sair em PascalCase pelo EDM.
purchaseInvoicesImportXml.Returns<PurchaseInvoiceDraftDto>();

var purchaseInvoicesConfirm = modelBuilder.Action("PurchaseInvoicesConfirm");
purchaseInvoicesConfirm.Parameter<Guid>("Key");
purchaseInvoicesConfirm.Returns<IActionResult>();

var purchaseInvoicesReverseConfirm = modelBuilder.Action("PurchaseInvoicesReverseConfirm");
purchaseInvoicesReverseConfirm.Parameter<Guid>("Key");
purchaseInvoicesReverseConfirm.Returns<IActionResult>();

var purchaseInvoicesCancel = modelBuilder.Action("PurchaseInvoicesCancel");
purchaseInvoicesCancel.Parameter<Guid>("Key");
purchaseInvoicesCancel.Returns<IActionResult>();

var purchaseInvoicesCommentCreate = modelBuilder.Action("PurchaseInvoicesCommentCreate");
purchaseInvoicesCommentCreate.Parameter<Guid>("InvoiceKey");
purchaseInvoicesCommentCreate.Parameter<string>("Text");
purchaseInvoicesCommentCreate.Returns<IActionResult>();

var purchaseInvoicesCommentUpdate = modelBuilder.Action("PurchaseInvoicesCommentUpdate");
purchaseInvoicesCommentUpdate.Parameter<Guid>("Key");
purchaseInvoicesCommentUpdate.Parameter<string>("Text");
purchaseInvoicesCommentUpdate.Returns<IActionResult>();

var purchaseInvoicesCommentDelete = modelBuilder.Action("PurchaseInvoicesCommentDelete");
purchaseInvoicesCommentDelete.Parameter<Guid>("Key");
purchaseInvoicesCommentDelete.Returns<IActionResult>();

var purchaseInvoicesOriginItems = modelBuilder.Function("PurchaseInvoicesOriginItems");
purchaseInvoicesOriginItems.Parameter<string>("CardCode");
purchaseInvoicesOriginItems.ReturnsCollection<PurchaseInvoiceOriginItemDto>();
```

- [ ] **Step 3: Create `PurchaseInvoicesController`**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class PurchaseInvoicesController(
    PurchaseInvoicesGetService getService,
    PurchaseInvoicesCreateService createService,
    PurchaseInvoicesUpdateService updateService,
    PurchaseInvoicesDeleteService deleteService,
    PurchaseInvoicesGetOriginItemsService originItemsService)
    : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<PurchaseInvoice>> Get() => Ok(getService.QueryAll());

    [EnableQuery]
    public async Task<ActionResult<PurchaseInvoice>> Get([FromRoute] Guid key)
    {
        try
        {
            return Ok(await getService.GetByIdAsync(key));
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Linhas de saída elegíveis como origem — alimenta o value help da amarração.</summary>
    [HttpGet("odata/PurchaseInvoicesOriginItems(CardCode={cardCode})")]
    [EnableQuery]
    public ActionResult GetOriginItems([FromRoute] string cardCode)
    {
        // Rota por atributo entrega o segmento COM as aspas simples do OData.
        return Ok(originItemsService.QueryByCardCode(cardCode?.Trim('\'') ?? string.Empty));
    }

    public async Task<IActionResult> Post([FromBody] PurchaseInvoice entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await createService.ExecuteAsync(entity, User.Identity?.Name ?? "Unknown");
            return Created(entity);
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    public async Task<IActionResult> Put([FromRoute] Guid key, [FromBody] PurchaseInvoice entity)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            await updateService.ExecuteAsync(key, entity, User.Identity?.Name ?? "Unknown");
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }

        return NoContent();
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch(
        [FromRoute] Guid key, [FromBody] Delta<PurchaseInvoice> patch)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var entity = await getService.GetByIdAsync(key);
            patch.Patch(entity);

            await updateService.ExecuteAsync(key, entity, User.Identity?.Name ?? "Unknown");
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }

        return NoContent();
    }

    public async Task<IActionResult> Delete([FromRoute] Guid key)
    {
        try
        {
            await deleteService.ExecuteAsync(key, User.Identity?.Name ?? "Unknown");
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }

        return NoContent();
    }
}
```

- [ ] **Step 4: Create the remaining entity controllers**

`PurchaseInvoicesItemsController` — CRUD completo sobre `PurchaseInvoicesItems`, espelhando
`SalesInvoicesItemsController.cs` (ler antes). `PurchaseInvoicesCommentsController` e
`PurchaseInvoicesChangeLogsController` — **só GET**, por rota de navegação:

```csharp
[HttpGet("odata/PurchaseInvoices({key})/CommentEntries")]
[HttpGet("odata/PurchaseInvoices({key})/ChangeLogs")]
```

espelhando `SalesInvoicesCommentsController.cs` / `SalesInvoicesChangeLogsController.cs`.

- [ ] **Step 5: Create the action controllers**

Sete arquivos em `SiagroB1.Web/Actions/PurchaseInvoices/`, todos
`[HttpPost("odata/<Nome>")]`. Padrão de leitura do parâmetro — **atenção à armadilha**:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.PurchaseInvoices;

public class PurchaseInvoicesImportXmlController(
    PurchaseInvoicesImportXmlService service) : ODataController
{
    /// <summary>
    /// Action OData, e não upload multipart em /api: o dev server e o Gateway só encaminham
    /// /odata, /security e /reports — um endpoint em /api não chegaria ao backend.
    /// </summary>
    [HttpPost("odata/PurchaseInvoicesImportXml")]
    public async Task<IActionResult> Post([FromBody] ODataActionParameters parameters)
    {
        // TryGetValue devolve TRUE com valor null: parâmetro string de action OData é anulável,
        // e um .ToString() direto estoura em NullReferenceException.
        var xmlContent = parameters.TryGetValue("XmlContent", out var raw) && raw is string s
            ? s
            : null;

        if (string.IsNullOrWhiteSpace(xmlContent))
            return BadRequest("Conteúdo do XML não informado.");

        var fileName = parameters.TryGetValue("FileName", out var rawName) && rawName is string n
            ? n
            : "nfe.xml";

        try
        {
            return Ok(await service.ExecuteAsync(
                System.Text.Encoding.UTF8.GetBytes(xmlContent), fileName));
        }
        catch (DefaultException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
```

Os outros seis seguem o mesmo formato, lendo `Key`/`InvoiceKey` como `Guid` e `Text` como string
anulável, e chamando o serviço correspondente com `User.Identity?.Name ?? "Unknown"`.

- [ ] **Step 6: Build and smoke-test the metadata**

Run: `dotnet build SiagroB1.sln`
Expected: sucesso.

Run: `dotnet run --project SiagroB1.Web --launch-profile dev` e, noutro terminal,
`curl http://localhost:50000/odata/$metadata | grep -i PurchaseInvoice`
Expected: os 4 EntitySets e as 7 actions aparecem no EDM. Encerrar o processo em seguida.

- [ ] **Step 7: Stage**

```bash
git -C siagro-b1-backend add \
  SiagroB1.Web/Controllers/PurchaseInvoices*.cs \
  SiagroB1.Web/Actions/PurchaseInvoices/ \
  SiagroB1.Web/ODataConfig/ODataConfigurations.cs \
  SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs
```

---

### Task 10: Remoção do CustomerReturn no backend — ✅ CONCLUÍDA (06/08/2026)

> 16 arquivos removidos, build 0 erros, suíte **941/941**.
>
> **A contagem CAIU de 951 para 941 e isso está certo** — o Step 3 do plano pedia para conferir que
> não caísse, mas a leitura correta é outra: os 10 fatos de `CustomerReturns` foram apagados e
> substituídos por **69** em `PurchaseInvoices`. Cobertura subiu 7×, não desceu.
>
> **Migrations e specs de 04/08 FICAM.** Migration aplicada é histórico: apagá-la quebraria a
> cadeia. O `AppDbContextModelSnapshot` é regenerado pela Task 11.
>
> **Curiosidade do git:** só 12 dos 16 arquivos apareceram como `D` no status. Os outros 4 nunca
> tinham sido commitados — estavam no índice como `A`, e apagar + `git add` os remove do índice
> inteiramente, sem deixar rastro. Conferido com `git ls-files` + `Test-Path`: disco e índice
> limpos.

Só agora, com o substituto completo e testado. Removida antes, a build quebraria; removida depois
da migration, a migration não geraria o DROP.

**Files:**
- Delete: `SiagroB1.Domain/Entities/CustomerReturn.cs`, `CustomerReturnItem.cs`
- Delete: `SiagroB1.Domain/Enums/CustomerReturnStatus.cs`
- Delete: `SiagroB1.Domain/Dtos/CustomerReturnDraftDto.cs`, `CustomerReturnOriginItemDto.cs`
- Delete: `SiagroB1.Application/Services/CustomerReturns/` (6 arquivos)
- Delete: `SiagroB1.Web/Controllers/CustomerReturnsController.cs`
- Delete: `SiagroB1.Web/Actions/CustomerReturns/` (2 arquivos)
- Delete: `SiagroB1.Application.Tests/CustomerReturns/` (2 arquivos)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`, `ODataConfigurations.cs`,
  `ServiceCollectionExtensions.cs`

- [ ] **Step 1: Delete the files**

```bash
cd siagro-b1-backend
rm SiagroB1.Domain/Entities/CustomerReturn.cs SiagroB1.Domain/Entities/CustomerReturnItem.cs
rm SiagroB1.Domain/Enums/CustomerReturnStatus.cs
rm SiagroB1.Domain/Dtos/CustomerReturnDraftDto.cs SiagroB1.Domain/Dtos/CustomerReturnOriginItemDto.cs
rm -r SiagroB1.Application/Services/CustomerReturns
rm SiagroB1.Web/Controllers/CustomerReturnsController.cs
rm -r SiagroB1.Web/Actions/CustomerReturns
rm -r SiagroB1.Application.Tests/CustomerReturns
```

- [ ] **Step 2: Remove the references**

- `AppDbContext.cs`: apagar os DbSets `CustomerReturns`/`CustomerReturnsItems` e o bloco do índice
  único em `AccessKey`.
- `ODataConfigurations.cs`: apagar os 2 EntitySets, os 3 `AddProperty` de `CustomerReturnItem`, e
  as actions/function `CustomerReturnsImportXml`, `CustomerReturnsCancel`,
  `CustomerReturnsOriginItems`. **Manter** o `AddProperty` de
  `SalesInvoiceItem.AssessedShortage` — quem o consome agora é o documento de entrada.
- `ServiceCollectionExtensions.cs`: apagar os 6 `AddScoped` do bloco
  `// devolução do cliente (controle e conciliação)`.

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build SiagroB1.sln`
Expected: sucesso, zero referência residual.

Run: `dotnet test SiagroB1.Application.Tests`
Expected: PASS. Confirmar que o total de testes NÃO caiu: os 10 fatos de `CustomerReturns` foram
substituídos pelos das Tasks 1-8.

- [ ] **Step 4: Stage the deletions**

```bash
git -C siagro-b1-backend add -A SiagroB1.Domain SiagroB1.Application SiagroB1.Web SiagroB1.Infra SiagroB1.Application.Tests
```

---

### Task 11: Migrations — tabelas, migração de dados e menu — ✅ CONCLUÍDA (06/08/2026)

> `20260806173630_CreatePurchaseInvoices` (AppContext) e `20260806173849_AddPurchaseInvoiceMenu`
> (CommonContext). Aplicadas **só no localhost** (env `Yokotobi`, `IDX_SIAGRO_DEV` +
> `IDX_SIAGRO_COMMON`). Build 0 erros.
>
> **O scaffold pôs os DropTable NO TOPO do `Up()`** — apagaria CUSTOMER_RETURNS antes de haver
> para onde copiar. Movidos para o fim, com o `INSERT...SELECT` antes deles.
>
> **A cópia foi EXERCITADA de verdade:** o banco local tinha 0 devoluções, então semeei uma
> sintética (amarrada a uma linha real de `SALES_INVOICES_ITEMS`), apliquei, conferi o resultado e
> apaguei. Sem isso o bloco de SQL teria passado sem executar nada. Verificado: `Registered→
> Confirmed`, `Return`/`ThirdParty`, `DocumentNumber→TaxDocumentNumber`, `AccessKey→ChaveNFe`,
> `TotalValue→TotalDocumentValue`, **amarração `SalesInvoiceItemKey` intacta**, `CUSTOMER_RETURNS`
> dropada.
>
> **Duas decisões contra o que o plano dizia:**
> 1. `BranchCode` só é preenchido quando a base tem UMA filial; com várias fica NULL. Filial em
>    branco é visível e pede correção — filial ERRADA num documento fiscal passa despercebida.
>    (Local tem 2 filiais, então a linha migrada saiu com NULL, como esperado.)
> 2. `ApprovedAt/By` NÃO são carimbados. As linhas viram `Confirmed` porque nasceram sob um modelo
>    sem etapa de aprovação; inventar aprovador seria fabricar auditoria.
>
> **`Down()` mantido como o EF gerou** (recria as tabelas antigas VAZIAS), com XML-doc avisando que
> reverte esquema e não dados.
>
> Correção de detalhe: a coluna do menu é `Expanded`, não `Blank`.
>
> ⚠️ **Ambientes NÃO tocados:** `Development` (129.121.53.204 / `MHAGRO_SIAGRO_HOM` — outro
> cliente), o `appsettings.json` base (env `Migration` → `IDX_SIAGRO_DEV` remoto, com `SapDB`
> apontando para `SBO_YOKOTOBI_PRD`), `Staging` e `Yokotobi-Production`.

**Files:**
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_CreatePurchaseInvoices.cs`
- Create: `SiagroB1.Migrations/CommonContext/<timestamp>_AddPurchaseInvoiceMenu.cs`

- [ ] **Step 1: Generate the AppContext migration**

```bash
cd siagro-b1-backend
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add CreatePurchaseInvoices \
  --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Expected: cria as 4 tabelas e **dropa** `CUSTOMER_RETURNS`/`CUSTOMER_RETURNS_ITEMS` (a Task 10 já
tirou as entidades do modelo).

> ⚠️ `dotnet ef migrations remove --no-build` apaga a migration ERRADA neste repo. Se precisar
> desfazer, apague o arquivo à mão e reverta o snapshot.

- [ ] **Step 2: Hand-edit the migration to copy the data before the drop**

Abrir o arquivo gerado. **Mover** os `DropTable` para o fim do `Up()` e inserir, imediatamente
antes deles, o `migrationBuilder.Sql` abaixo. `BranchCode` recebe a primeira filial cadastrada —
`CUSTOMER_RETURNS` não tinha filial.

```csharp
// A devolução de cliente vira documento de entrada tipo Return. Guardado sob OBJECT_ID porque
// CUSTOMER_RETURNS não está commitada e pode não existir em todos os ambientes — a migration
// precisa rodar tanto em base que a tem quanto em base que nunca a viu.
migrationBuilder.Sql(@"
IF OBJECT_ID('CUSTOMER_RETURNS', 'U') IS NOT NULL
BEGIN
    DECLARE @BranchCode VARCHAR(14) = (SELECT TOP 1 BranchCode FROM BRANCHES ORDER BY BranchCode);

    INSERT INTO PURCHASE_INVOICES
        ([Key], BranchCode, InvoiceType, IssuerType, InvoiceStatus,
         CardCode, CardName, TaxDocumentNumber, TaxDocumentSeries, ChaveNFe,
         IssueDate, PostingDate, TotalDocumentValue, TaxPayerComments,
         GrossWeight, NetWeight, FreightTerms,
         XmlFileName, XmlData,
         CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, CanceledAt, CanceledBy)
    SELECT
        cr.[Key], @BranchCode,
        1,                              -- PurchaseInvoiceType.Return
        0,                              -- DocumentIssuerType.ThirdParty
        CASE cr.[Status]
            WHEN 1 THEN 2               -- CustomerReturnStatus.Cancelled -> InvoiceStatus.Cancelled
            ELSE 1                      -- Registered                     -> Confirmed
        END,
        cr.CardCode, cr.CardName, cr.DocumentNumber, cr.DocumentSeries, cr.AccessKey,
        cr.IssueDate, cr.IssueDate, cr.TotalValue, cr.TaxPayerComments,
        0, 0, 0,
        cr.XmlFileName, cr.XmlData,
        cr.CreatedAt, cr.CreatedBy, cr.UpdatedAt, cr.UpdatedBy, cr.CanceledAt, cr.CanceledBy
    FROM CUSTOMER_RETURNS cr;

    INSERT INTO PURCHASE_INVOICES_ITEMS
        ([Key], PurchaseInvoiceKey, ItemCode, ItemName, Quantity, UnitPrice, SalesInvoiceItemKey)
    SELECT
        cri.[Key], cri.CustomerReturnKey, cri.ItemCode, cri.ItemName,
        cri.Quantity, cri.UnitPrice, cri.SalesInvoiceItemKey
    FROM CUSTOMER_RETURNS_ITEMS cri
    INNER JOIN CUSTOMER_RETURNS cr ON cr.[Key] = cri.CustomerReturnKey;
END
");
```

No `Down()`, acrescentar o comentário:

```csharp
// Down() NÃO reconstrói CUSTOMER_RETURNS: a entidade deixou de existir no código, e recriar a
// tabela produziria um esquema órfão que nenhum serviço lê. A volta desta migration é
// destrutiva por desenho.
```

- [ ] **Step 3: Apply and verify the data landed**

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update \
  --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

> Ler a connection string ANTES de rodar. O perfil `db-migration` aponta para produção no
> fallback e o alvo muda conforme o ambiente.

Conferir no banco:

```sql
SELECT COUNT(*) FROM PURCHASE_INVOICES WHERE InvoiceType = 1;
SELECT COUNT(*) FROM PURCHASE_INVOICES_ITEMS WHERE SalesInvoiceItemKey IS NOT NULL;
SELECT OBJECT_ID('CUSTOMER_RETURNS', 'U');  -- deve voltar NULL
```
Expected: as duas primeiras contagens batem com o que havia em `CUSTOMER_RETURNS`; a terceira é
`NULL`.

- [ ] **Step 4: Create the menu migration**

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add AddPurchaseInvoiceMenu \
  --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Preencher o `Up()` — espelhar o formato de
`SiagroB1.Migrations/CommonContext/20260720131842_AddStorageEntryTransactionMenu.cs`:

```csharp
// A Key TEM de ser igual ao nome da rota no manifest: App.controller.ts faz navTo(item.getKey()).
migrationBuilder.InsertData(
    table: "MENU_ITEMS",
    columns: ["Key", "Title", "Icon", "Enabled", "Blank", "Order", "ParentKey"],
    values: new object[] {
        "purchaseInvoices", "Documentos de Entrada",
        "sap-icon://journey-arrive", true, false, 9, "purchases"
    });

migrationBuilder.InsertData(
    table: "ROLE_MENUS",
    columns: ["Id", "RoleCode", "MenuItemKey"],
    values: new object[] {
        "3F7A1C08-9B24-4E6D-8A15-2C90D4B7E631", "ADMIN", "purchaseInvoices"
    });

// A devolução some do menu de Vendas — junto vai a colisão de Order 8 que ela tinha com
// salesContractsShipmentRelease, que deixava a ordenação entre as duas indefinida.
migrationBuilder.DeleteData(
    table: "ROLE_MENUS", keyColumn: "MenuItemKey", keyValue: "customerReturns");
migrationBuilder.DeleteData(
    table: "MENU_ITEMS", keyColumn: "Key", keyValue: "customerReturns");
```

`Order = 9` porque 1 a 8 estão ocupados em `purchases` (7 é `storageEntryTransaction`, 8 é a
aprovação de fixação de preço).

- [ ] **Step 5: Apply and verify**

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update \
  --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

```sql
SELECT [Key], Title, [Order], ParentKey FROM MENU_ITEMS WHERE [Key] IN ('purchaseInvoices','customerReturns');
```
Expected: uma linha, `purchaseInvoices` / Compras / 9.

- [ ] **Step 6: Stage**

```bash
git -C siagro-b1-backend add SiagroB1.Migrations/
```

---

### Tasks 12–14: Frontend completo — ✅ CONCLUÍDAS (06/08/2026)

> `yarn ts-typecheck` e `yarn lint` **limpos**. Frontend de `customerReturns` removido; grep por
> "customerReturn" no `webapp/` não retorna nada.
>
> Entregue: `Main` (lista com filtro de Tipo), `Add` (importar XML **ou digitar do zero**),
> `Detail` (4 seções + ciclo de vida) e `Edit` (a tela que a devolução nunca teve), mais os
> fragmentos `Form`, `Items`, `Comments`, `ChangeLogs`, `CommentDialog` e o diálogo de origem.
>
> **Decisões:**
> * `openBusinessPartnersValueHelp` (já existia no `CommonController`) para o emitente — serve
>   fornecedor E cliente, que é o que um documento de entrada precisa. `CustomData
>   descriptionProperty=CardName` preenche o nome sozinho.
> * Inclusão manual do zero, não só por XML: a NF sem arquivo e a emissão própria precisam disso.
> * Colunas de devolução (NF de Origem, Quebra Apurada, Diferença) com
>   `visible="{= ${InvoiceType} === 'Return' }"` — somem no documento Normal.
> * `maxLength="44"` na Chave NF-e. A tela da devolução tinha **9** para a mesma coluna
>   `VARCHAR(44)`: a chave não cabia.
> * Botões do Detail com expression binding explícito sobre o status — `visible` com binding
>   indefinido avalia como TRUE.
>
> ⚠️ **`yarn ui5lint` acusa erros nos arquivos novos**, todos das mesmas classes já presentes nos
> 690 erros do projeto (`getSelectedIndex`, `valueHelpOnly`, `visibleRowCountMode`, tipos OData
> como globais em XML). São o padrão da casa, copiado das telas existentes, e `ui5lint` NÃO está
> no `yarn test`. Não é regressão desta mudança.

### Task 12 (original): Frontend — lista, formatters e rotas

**Files:**
- Create: `webapp/view/purchaseInvoices/Main.view.xml`,
  `webapp/view/purchaseInvoices/fragments/Filterbar.fragment.xml`
- Create: `webapp/controller/purchaseInvoices/BaseController.ts`, `Main.controller.ts`
- Modify: `webapp/model/formatter.ts`, `webapp/manifest.json`
- Delete: `webapp/view/customerReturns/`, `webapp/controller/customerReturns/`,
  `webapp/dialogs/fragments/CustomerReturnOriginItemsSelectDialog.fragment.xml`

**Interfaces:**
- Consumes: EntitySet `/PurchaseInvoices`, actions `PurchaseInvoicesCancel`.
- Produces: rotas `purchaseInvoices`, `purchaseInvoicesAdd`, `purchaseInvoicesDetail`,
  `purchaseInvoicesEdit`; formatters `formatPurchaseInvoiceType`,
  `formatPurchaseInvoiceStatus`, `statePurchaseInvoiceStatus`, `stateInvoiceDifference`.

- [ ] **Step 1: Add the formatters**

Em `webapp/model/formatter.ts`, substituindo `formatCustomerReturnStatus` /
`stateCustomerReturnStatus` / `stateReturnDifference`:

```ts
formatPurchaseInvoiceType(type: string): string {
  switch (type) {
    case "Normal": return "Normal";
    case "Return": return "Devolução";
    default: return "";
  }
},

formatPurchaseInvoiceStatus(status: string): string {
  switch (status) {
    case "Pending": return "Pendente";
    case "Confirmed": return "Confirmado";
    case "Cancelled": return "Cancelado";
    case "Returned": return "Devolvido";
    default: return "";
  }
},

statePurchaseInvoiceStatus(status: string): string {
  switch (status) {
    case "Confirmed": return "Success";
    case "Cancelled": return "Error";
    case "Returned": return "Warning";
    default: return "None";
  }
},

/** Zero é fiscal e físico batendo; qualquer diferença é aviso, nunca erro. */
stateInvoiceDifference(difference: number): string {
  return Number(difference) === 0 ? "Success" : "Warning";
},
```

- [ ] **Step 2: Add the routes to `manifest.json`**

Remover as 3 rotas/targets de `customerReturns`. Acrescentar, no formato das rotas de
`salesInvoices`:

```json
{ "pattern": "purchase-invoices",              "name": "purchaseInvoices",       "target": "purchaseInvoices" },
{ "pattern": "purchase-invoices/add",          "name": "purchaseInvoicesAdd",    "target": "purchaseInvoicesAdd" },
{ "pattern": "purchase-invoices/{id}/detail",  "name": "purchaseInvoicesDetail", "target": "purchaseInvoicesDetail" },
{ "pattern": "purchase-invoices/{id}/edit",    "name": "purchaseInvoicesEdit",   "target": "purchaseInvoicesEdit" }
```

com os targets apontando para `view.purchaseInvoices.Main` / `.Add` / `.Detail` / `.Edit`,
`clearControlAggregation: true`, níveis 1/2/2/2.

- [ ] **Step 3: Create the list view and Filterbar**

`Main.view.xml` — `sap.ui.table.Table` com `rows="{path: '/PurchaseInvoices', parameters: {$orderby: 'RowId desc'}}"`,
footer com Atualizar / Incluir / Visualizar / Editar / Cancelar. Colunas: Filial, **Tipo**
(`formatPurchaseInvoiceType`), Emissão, Entrada, Situação (`ObjectStatus` com
`statePurchaseInvoiceStatus`), Emitente, Nota Fiscal, Série, Chave NF-e, Valor Declarado,
Observações.

> ⚠️ **Enum em binding precisa de `targetType: 'any'`** nas `parts`, senão o formatter recebe o
> objeto e não a string — passa nos gates e quebra só no navegador:
>
> ```xml
> <ObjectStatus
>   text="{path: 'InvoiceStatus', targetType: 'any', formatter: '.formatter.formatPurchaseInvoiceStatus'}"
>   state="{path: 'InvoiceStatus', targetType: 'any', formatter: '.formatter.statePurchaseInvoiceStatus'}" />
> ```

`fragments/Filterbar.fragment.xml` — BranchCode, TaxDocumentNumber, TaxDocumentSeries, ChaveNFe,
CardCode, DateFrom/DateTo, **InvoiceType** (Select fixo Normal/Devolução), InvoiceStatus (Select
fixo).

- [ ] **Step 4: Create the controllers**

`Main.controller.ts` — espelhar `controller/salesInvoices/Main.controller.ts`, incluindo o
`$filter` montado por concatenação de string a partir do JSONModel `filter`, e acrescentando o
ramo do tipo:

```ts
if (filterKey == "InvoiceStatus" || filterKey == "InvoiceType") {
  filters.push(`${filterKey} eq '${value}'`);
} else if (filterKey == "DateFrom") {
  filters.push(`IssueDate ge ${value}`);
} else if (filterKey == "DateTo") {
  filters.push(`IssueDate le ${value}`);
} else {
  filters.push(`contains(${filterKey},'${value}')`);
}
```

`BaseController.ts` — `openOriginItemValueHelp()` (portado de
`controller/customerReturns/BaseController.ts`, gravando `SalesInvoiceItemKey`) e
`refreshDocumentTotal()` somando **no cliente** via `getAllCurrentContexts()`, porque
`TotalInvoiceItems` é `[NotMapped]`.

- [ ] **Step 5: Delete the customerReturns frontend**

```bash
cd siagro-b1-frontend
rm -r webapp/view/customerReturns webapp/controller/customerReturns
rm webapp/dialogs/fragments/CustomerReturnOriginItemsSelectDialog.fragment.xml
```

Criar `webapp/dialogs/fragments/PurchaseInvoiceOriginItemsSelectDialog.fragment.xml` no lugar,
mantendo o vínculo em **`/SalesInvoicesItems`** com o `$filter` explícito:

```
SalesInvoice/InvoiceStatus eq 'Confirmed' and DeliveryStatus eq 'Closed'
and Quantity gt DeliveredQuantity sub QuantityLoss
```

> Não ligar na function `PurchaseInvoicesOriginItems`: função que devolve coleção não se liga por
> `elementPath` e o diálogo abre vazio. Foi por isso que a devolução já fazia assim.

- [ ] **Step 6: Run the gates**

```bash
cd siagro-b1-frontend
yarn ts-typecheck
yarn lint
```
Expected: PASS nos dois. Não rodar `yarn test` — o gate de cobertura é 50% contra ~2,4% reais e
nunca passa neste repo; não é regressão desta mudança.

- [ ] **Step 7: Stage**

```bash
git -C siagro-b1-frontend add -A webapp/
```

---

### Task 13: Frontend — Add com importação de XML

**Files:**
- Create: `webapp/view/purchaseInvoices/Add.view.xml`,
  `fragments/Form.fragment.xml`, `fragments/Items.fragment.xml`
- Create: `webapp/controller/purchaseInvoices/Add.controller.ts`

- [ ] **Step 1: Create the form and items fragments**

`Form.fragment.xml` — campos do cabeçalho, com **Tipo e Emissão selecionáveis** (`InvoiceType`,
`IssuerType`), Emitente com value help, Nota/Série/**Chave NF-e**, Emissão, Entrada, Valor
Declarado, Observações, e o `TaxPayerComments` num `TextArea` **read-only** ao lado da grade — é a
cola do operador para amarrar.

> ⚠️ O Input da Chave NF-e precisa de `maxLength="44"`. A tela da devolução tinha `maxLength="9"`
> para a mesma coluna `VARCHAR(44)`, e a chave não cabia.

`Items.fragment.xml` — `id="tablePurchaseInvoiceItems"`, colunas Produto, Descrição, UM,
Quantidade, Preço Unitário, Total, **NF de Origem** (Input `valueHelpOnly`), **Quebra Apurada**,
**Diferença** (`ObjectStatus` com `stateInvoiceDifference`).

> ⚠️ `precision: 7` nos bindings de quantidade (`DECIMAL(18,3)`), senão a terceira casa some.

- [ ] **Step 2: Create `Add.controller.ts`**

Portar de `controller/customerReturns/Add.controller.ts`, com as trocas de nome e **estas
diferenças**:

```ts
/** Rascunho devolvido pela leitura do XML — não é gravado ainda. */
interface ImportedInvoice {
  CardCode: string;
  CardName: string;
  TaxDocumentNumber: string;
  TaxDocumentSeries: string;
  ChaveNFe: string;
  IssueDate: string;
  TotalDocumentValue: number;
  TaxPayerComments: string;
  XmlFileName: string;
  Items: {
    ItemCode: string;
    ItemName: string;
    UnitOfMeasureCode: string;
    Quantity: number;
    UnitPrice: number;
  }[];
}
```

e no `createFromDraft`:

```ts
const oContext = oBinding.create({
  InvoiceType: "Normal",
  IssuerType: "ThirdParty",
  CardCode: draft.CardCode,
  CardName: draft.CardName,
  TaxDocumentNumber: draft.TaxDocumentNumber,
  TaxDocumentSeries: draft.TaxDocumentSeries,
  ChaveNFe: draft.ChaveNFe,
  IssueDate: draft.IssueDate,
  PostingDate: new Date().toISOString().slice(0, 10),
  TotalDocumentValue: draft.TotalDocumentValue,
  TaxPayerComments: draft.TaxPayerComments,
  Comments: null,
  XmlFileName: draft.XmlFileName,
  // O XML volta ao servidor no POST: é a prova documental guardada com o documento.
  XmlData: btoa(unescape(encodeURIComponent(xmlContent))),
  Items: (draft.Items ?? []).map(item => ({
    ItemCode: item.ItemCode,
    ItemName: item.ItemName,
    UnitOfMeasureCode: item.UnitOfMeasureCode,
    Quantity: item.Quantity,
    UnitPrice: item.UnitPrice,
    // Nasce sem amarração: o XML não carrega o vínculo. Precisa EXISTIR no payload inicial,
    // senão a primeira escolha no value help abre
    // "Must not change a property before it has been read".
    SalesInvoiceItemKey: null,
  })),
}, false, false, false);
```

> ⚠️ **Toda propriedade que a tela edita entra no `create()` inicial**, nem que seja como `null`.
> É a armadilha que já mordeu este projeto várias vezes.

O documento de entrada também pode ser criado **sem XML**: o botão Incluir abre o formulário vazio
com `InvoiceType: "Normal"` e uma linha em branco. A action de importação apenas preenche.

- [ ] **Step 3: Run the gates**

```bash
cd siagro-b1-frontend && yarn ts-typecheck && yarn lint
```
Expected: PASS.

- [ ] **Step 4: Stage**

```bash
git -C siagro-b1-frontend add -A webapp/
```

---

### Task 14: Frontend — Detail e Edit

**Files:**
- Create: `webapp/view/purchaseInvoices/Detail.view.xml`, `Edit.view.xml`
- Create: `webapp/view/purchaseInvoices/fragments/PurchaseInvoiceComments.fragment.xml`,
  `PurchaseInvoiceChangeLogs.fragment.xml`, `PurchaseInvoiceCommentDialog.fragment.xml`,
  `NotaFiscalDialog.fragment.xml`
- Create: `webapp/controller/purchaseInvoices/Detail.controller.ts`, `Edit.controller.ts`

- [ ] **Step 1: Create `Detail.view.xml`**

`ObjectPageLayout` com 4 seções — Form (read-only), Itens, Comentários, Log de Alterações —
espelhando `view/salesInvoices/Detail.view.xml`. Footer: Confirmar, Estornar, Cancelar, Editar.

> ⚠️ `visible` com binding `undefined` avalia como **`true`**. Os botões de ação precisam de
> expression binding explícito sobre o status, ex.:
> `visible="{= ${InvoiceStatus} === 'Pending' }"`.

- [ ] **Step 2: Create `Edit.view.xml`**

Reusa `fragments/Form.fragment.xml` e `fragments/Items.fragment.xml` com `ui>/editable` true.

> ⚠️ O fragmento compartilhado entre Add e Edit/Detail precisa de `$$ownRequest` no binding da
> lista de itens para carregar no Detail — e isso **não** quebra o deep-insert do Add.

- [ ] **Step 3: Create `Detail.controller.ts`**

Bind com `$expand` explícito, senão a quebra apurada volta zero:

```ts
this.getView().bindElement({
  path: `/PurchaseInvoices(${sKey})`,
  parameters: {
    $expand: "Items($expand=SalesInvoiceItem($expand=SalesInvoice))",
  },
});
```

Ações via `bindContext` (não `callFunction`):

```ts
const action = oModel.bindContext("/PurchaseInvoicesConfirm(...)");
action.setParameter("Key", sKey);
await action.invoke();
```

Comentários e log em bindings próprios com `$$ownRequest` — o `GetService` não os inclui.

- [ ] **Step 4: Run the gates**

```bash
cd siagro-b1-frontend && yarn ts-typecheck && yarn lint
```
Expected: PASS.

- [ ] **Step 5: Stage**

```bash
git -C siagro-b1-frontend add -A webapp/
```

---

### Task 15: Verificação end-to-end — ⚠️ PARCIAL (06/08/2026)

> **Verificado no navegador (stack `yktb` + `yarn start:dev`, login admin):** menu, lista com todas
> as colunas e filtro de Tipo, tela de inclusão renderizando, value help de parceiro preenchendo
> CardCode **e** CardName, digitação de cabeçalho e de item, colunas de devolução ocultas no tipo
> Normal, e a migração de dados (Task 11).
>
> **DOIS BUGS MEUS, achados só aqui e corrigidos:**
> 1. `FormatException: 'sap.ui.model.odata.type.Raw' does not support formatting` — falta de
>    `targetType: 'any'` em TODA propriedade de enum usada em binding: `selectedKey` dos 2 Selects,
>    `editable` do número interno e `visible` de 3 colunas + 4 botões do Detail.
> 2. Diálogo de erro `Must not change a property before it has been read` na cara do usuário:
>    `sap.m.Select` sobre entidade TRANSIENTE precisa de **`forceSelection="false"`**. No padrão
>    (`true`) ele auto-seleciona um item na inicialização e ESCREVE a chave de volta.
>
> **BLOQUEIO NÃO RESOLVIDO — pré-existente e fora do escopo:** gravar pela tela devolve
> **400 "The entity field is required"**. Causa isolada por bisecção: o backend recusa
> `Edm.Decimal` em STRING, que é exatamente o que o UI5 v4 envia (`IEEE754Compatible=true`).
> * `{"Quantity":1}` → 201; `{"Quantity":"1"}` → 400.
> * Reproduzido IDÊNTICO em `/odata/SalesInvoices`, que é anterior a este trabalho.
> * Vale para POST **e** PATCH, com e sem `IEEE754Compatible` no Content-Type.
>
> **RESOLVIDO em 06/08/2026, e a conclusão acima estava ERRADA na implicação prática.** Não era
> bloqueio de backend sem saída: o projeto já tem convenção para isso e eu a violei. Campo decimal
> EDITÁVEL usa `sap.ui.model.odata.type.Double` (parse para número), não `Decimal` (parse para
> string) — é o que `salesInvoices/fragments/Items.fragment.xml` já fazia, e por isso aquela tela
> grava. Corrigido em `Items.fragment.xml` (Quantidade, Preço Unitário) e `Form.fragment.xml`
> (Valor declarado). POST com o payload real da tela → **201**, persistindo `Quantity=1500.500` e
> `UnitPrice=2.50000000`. O `Program.cs` NÃO foi alterado e não precisa ser.
> Ver `docs/superpowers/specs/2026-08-06-odata-decimal-string-blocker.md`.
>
> **VERIFICAÇÃO CONCLUÍDA NO NAVEGADOR (06/08/2026, stack `yktb` + `yarn start:dev`, admin).**
>
> Percorrido e confirmado na tela: menu → lista (colunas, filtro de Tipo, busca) → Incluir com
> value help de emitente → Salvar → Detail → Editar linhas e regravar → Confirmar → Estornar →
> Cancelar → cancelar LIBERA a chave (relançamento da mesma NF-e funciona) → trava de duplicidade
> com pendente ativo → comentários (incluir e editar) com o Log de Alterações recebendo as duas
> linhas → amarração da devolução pelo value help de NF de origem, com Quebra Apurada (70,000) e
> Diferença (230,000) calculadas pelo servidor sobre dados REAIS do Yokotobi.
>
> **7 sintomas achados aqui — 11 correções —, todos corrigidos e reverificados na tela:**
>
> 1. **404 depois de gravar.** `PurchaseInvoicesItemsController` não declarava a navegação
>    `PurchaseInvoices({key})/Items` — os dois controllers irmãos (Comments, ChangeLogs) declaravam
>    o par de rotas e este não. O documento gravava e o usuário levava um erro na cara.
> 2. **Detail 100% em branco.** `[EnableQuery]` puro limita `$expand` a 2 níveis e o Detail precisa
>    de 3 (`Items > SalesInvoiceItem > SalesInvoice`). `PurchaseContractsController` já usava
>    `MaxExpansionDepth = 5` — o padrão existia e eu não segui.
> 3. **Nome do emitente gravava vazio.** `CardName ??=` só cobre null, e a TELA manda `""`: o value
>    help copia a descrição com group ID null de propósito (quem manda no campo desnormalizado é o
>    servidor) e o `create()` do UI5 precisa declarar a propriedade. Ver
>    [[value-help-description-binding]].
> 4. **Trocar o emitente era descartado em silêncio.** O `UpdateService` copiava todo o cabeçalho
>    menos `CardCode`/`CardName`. Gravava sem erro e voltava com o emitente antigo.
> 5. **"Total dos itens" travado em 0,00** no Detail e no Edit: `refreshDocumentTotal()` rodava
>    junto do `bindElement`, que é assíncrono, somando uma lista ainda vazia. Passou para o
>    `dataReceived`.
> 6. **Filtro de Tipo estourava** `Unsupported type: SIAGROB1.PurchaseInvoiceType`. Enum não vira
>    literal por `sap.ui.model.Filter`; foi para `$filter` estático via `changeParameters`, que o
>    UI5 combina com os filtros dinâmicos usando AND (doc oficial). De quebra, trocar o tipo
>    apagava a busca que continuava escrita na caixa.
> 7. **Escolher a NF de origem estourava** `Cannot set properties of null (setting 'SalesInvoice')`:
>    o Input exibe uma NAVEGAÇÃO e o `setValue` do value help tentava escrevê-la numa linha ainda
>    não amarrada. Resolvido com `mode: 'OneWay'`.
>
> Dois deles são a MESMA armadilha em roupas diferentes — o backend não serve
> `Entity(key)/Property` nem `Entity(key)/Navegação` sem rota declarada, e o UI5 pede exatamente
> isso quando a propriedade não veio no request principal. Foi o caso também de
> `AssessedShortage`/`Difference`, cujas colunas só ficam visíveis no tipo Devolução e por isso
> ficavam de fora do `$select` automático: resolvido com `$select` explícito no `$expand`.
>
> **Dados de teste:** 3 dos 4 documentos criados foram removidos. Restou **1 documento CANCELADO**
> (NF 778899), porque `PurchaseInvoicesDeleteService` só exclui documento pendente — regra
> deliberada, e apagá-lo exigiria SQL direto contornando a guarda. Fica a critério do usuário.

### Task 15 (original): Verificação end-to-end pelo caminho do usuário

Nenhuma parte desta fase conta como pronta até este passo. O histórico do projeto é claro: os bugs
que os gates não pegam (binding de enum, `$expand` faltando, propriedade fora do `create()`)
aparecem só aqui.

- [ ] **Step 1: Run every gate**

```bash
cd siagro-b1-backend && dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests
cd ../siagro-b1-frontend && yarn ts-typecheck && yarn lint
```
Expected: todos PASS. (`yarn test` não passa neste repo — cobertura 50% contra ~2,4% reais.)

- [ ] **Step 2: Start the stack**

Backend: `SiagroB1.Web` e `SiagroB1.Gateway` no profile `yktb` (ambiente Yokotobi).
Frontend: `yarn start:dev`. Login `admin` / `1234`.

- [ ] **Step 3: Walk the user path and confirm each item**

1. **Compras → Documentos de Entrada** aparece no menu e a lista abre
2. Incluir → **Importar XML** de uma NF-e real → cabeçalho e itens preenchem, chave com 44 dígitos
   visível inteira
3. Gravar → reabrir → **Editar e regravar as linhas**, incluindo trocar a amarração
   (é o que a devolução não fazia)
4. Filtrar por **Tipo = Devolução** → as linhas migradas de `CUSTOMER_RETURNS` aparecem, com a NF
   de origem amarrada e a **Quebra Apurada** preservada
5. Confirmar → Estornar → Cancelar. Cancelar **libera a chave**: reimportar o mesmo XML funciona
6. Comentar, editar o comentário, e ver a linha correspondente no **Log de Alterações**
7. Abrir um contrato de compra envolvido antes e depois de tudo: **o saldo não mudou** em passo
   algum

- [ ] **Step 4: Report honestly**

Registrar o que foi verificado no navegador e o que não foi. Se algum passo falhou, ele é um bug
desta fase — corrigir antes de declarar concluído. Se o ambiente impediu algum passo (o value help
do SAP dá 500 e as telas de Liberações estouram 30s no Yokotobi — nenhum dos dois é bug desta
mudança), dizer explicitamente qual passo ficou pendente.

- [ ] **Step 5: Final staging check**

```bash
git -C siagro-b1-backend status --short
git -C siagro-b1-frontend status --short
```
Expected: nenhum arquivo em `??` (untracked). **Não commitar** — o commit é do usuário.

---

## Self-Review

**Cobertura do spec** — cada seção tem tarefa: modelo de dados → Tasks 1-2; `IssuerType`
funcional → Tasks 1, 13; ciclo de status → Task 6; migração de `CUSTOMER_RETURNS` → Task 11;
regras preservadas da devolução → Tasks 3, 7; as duas regras corrigidas (`Update` persiste linhas,
Detail deixa de ser read-only) → Tasks 5 e 14; serviços e API → Tasks 3-9; frontend → Tasks 12-14;
remoções → Tasks 10 e 12; testes → distribuídos; verificação → Task 15.

**Consistência de nomes** — `ChaveNFe` (não `AccessKey`) em entidade, DTO, EDM, SQL da migração e
frontend; `TotalDocumentValue` (não `TotalValue`); `PurchaseInvoiceKey` como FK das três filhas;
`ExecuteAsync(Guid, string)` uniforme nos quatro serviços de ciclo de vida.

**Fora de escopo, deliberadamente** — campos fiscais e natureza de operação (Fase 2); amarração a
contrato de compra e romaneio, coluna de divergência, efeito no ledger e numerador automático
(Fase 3).
