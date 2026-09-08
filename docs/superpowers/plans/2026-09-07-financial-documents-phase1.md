# Contas a Pagar / Contas a Receber — Fase 1 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar ao SiagroB1 um razão auxiliar financeiro próprio, em que o contrato aprovado gera um documento provisório bloqueado para baixa, e o adiantamento a contrato pode ser criado e liquidado.

**Architecture:** Uma tabela de documentos financeiros (`FINANCIAL_DOCUMENTS`) com enum de direção e de natureza, um ledger de baixas assinado e insert-only (`FINANCIAL_SETTLEMENTS`) do qual o saldo é derivado-e-persistido, um cadastro de contas financeiras e um log de alterações campo a campo. A geração do provisório é **enqueue-only**: enfileira no `ChangeTracker` do serviço de aprovação de contrato e cai no mesmo `SaveChanges`, sem transação nova e sem converter nenhum serviço existente para `IUnitOfWork`.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server), OData v4 (Microsoft.AspNetCore.OData), Dapper (só na numeração), xUnit + EF Core InMemory, OpenUI5 1.141 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-09-07-financial-documents-design.md` — leia antes de começar qualquer tarefa. O plano argumenta a partir dele.

## Global Constraints

- **NUNCA commitar ou dar push.** Os commits deste projeto são feitos manualmente pelo usuário. A única operação de escrita no git permitida é `git add`, e **todo arquivo novo deve ser staged imediatamente após ser criado**. Onde a skill de planos pediria "Commit", este plano pede `git add`.
- **Identificadores em inglês; texto que o usuário lê em pt-BR.** Classes, propriedades, tabelas e colunas em inglês; rótulos de tela, títulos de menu e mensagens de erro de negócio em português.
- **Nenhuma FK para cadastro dual-mode** (`CardCode` de parceiro, `ItemCode`, `LedgerAccountCode`, `CostCenterCode`). Em `Erp=SAPB1` as tabelas locais estão vazias e a FK obrigatória vira INNER JOIN que zera a coleção inteira. Grave o código e o nome desnormalizado.
- **Valor monetário em parâmetro de action é `Edm.Double`, nunca `Edm.Decimal`.** O UI5 v4 serializa decimal como string e o backend devolve 400 sem nomear o campo. Data e enum em parâmetro de action vão como `string`.
- **Toda propriedade `[NotMapped]` precisa de `AddProperty` explícito** em `ODataConfigurations.cs`, senão `$select` devolve 400.
- **Toda rota de action/function é declarada à mão**, nas duas formas quando houver chave — a forma não declarada toma 404.
- **Serviço novo precisa de `AddScoped` manual** em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (`AddApplicationServices()`). Não há assembly scanning.
- **Não renumere enum existente.** Valor novo entra sempre no fim.
- **Migration nunca é aplicada pelo profile `db-migration`**, que aponta para produção. Aplique com env explícito: `ASPNETCORE_ENVIRONMENT=Yokotobi-Development`.
- Valores monetários são `DECIMAL(18,2)` e arredondam com `MidpointRounding.ToEven`.

---

## File Structure

**Domínio** — `SiagroB1.Domain/`
- `Enums/FinancialDirection.cs`, `FinancialDocumentNature.cs`, `FinancialDocumentStatus.cs`, `FinancialDocumentOrigin.cs`, `FinancialSettlementOrigin.cs`, `FinancialAccountType.cs` — um enum por arquivo, como o resto da pasta.
- `Enums/TransactionCode.cs` (modificar) — `FinancialDocument = 12`.
- `Entities/FinancialAccount.cs`, `FinancialDocument.cs`, `FinancialSettlement.cs`, `FinancialDocumentChangeLog.cs`.
- `Entities/FinancialDocumentChangeLogFields.cs` — constantes dos códigos de campo.
- `Dtos/FinancialDocumentTotalsDto.cs`, `FinancialDocumentByContractDto.cs`, `FinancialDocumentBacklogResultDto.cs`, `FinancialDocumentRecalcResultDto.cs`.

**Infra** — `SiagroB1.Infra/Context/AppDbContext.cs` (modificar): 4 `DbSet`, 2 FKs de contrato e 2 índices filtrados.

**Aplicação** — `SiagroB1.Application/Services/Financials/` (pasta nova), um arquivo por serviço.

**Web** — `SiagroB1.Web/Controllers/Financial*Controller.cs`, `SiagroB1.Web/Actions/Financials/*Controller.cs`, `SiagroB1.Web/Functions/Financials/*Controller.cs`, e `ODataConfig/ODataConfigurations.cs` + `Extensions/ServiceCollectionExtensions.cs` (modificar).

**Migrations** — `SiagroB1.Migrations/AppContext/` (schema + seed) e `SiagroB1.Migrations/CommonContext/` (menu).

**Testes** — `SiagroB1.Application.Tests/Financials/`.

---

## Task 1: Enums, entidades e schema

**Files:**
- Create: `SiagroB1.Domain/Enums/FinancialDirection.cs`, `FinancialDocumentNature.cs`, `FinancialDocumentStatus.cs`, `FinancialDocumentOrigin.cs`, `FinancialSettlementOrigin.cs`, `FinancialAccountType.cs`
- Create: `SiagroB1.Domain/Entities/FinancialAccount.cs`, `FinancialDocument.cs`, `FinancialSettlement.cs`, `FinancialDocumentChangeLog.cs`, `FinancialDocumentChangeLogFields.cs`
- Modify: `SiagroB1.Domain/Enums/TransactionCode.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`

**Interfaces:**
- Produces: as quatro entidades e os seis enums, consumidos por todas as tarefas seguintes. `FinancialDocument.OpenAmount`, `.IsBlockedForSettlement`, `.IsOverdue`, `.AvailableAdvanceAmount` são `[NotMapped]` e a Task 4 as registra no EDM.

- [ ] **Step 1: Criar os seis enums**

`SiagroB1.Domain/Enums/FinancialDirection.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

public enum FinancialDirection
{
    Receivable = 0,
    Payable = 1
}
```

`FinancialDocumentNature.cs` — a Fase 1 escreve só `Provisional` e `Advance`; os outros dois estão reservados porque renumerar enum é proibido neste repositório:

```csharp
namespace SiagroB1.Domain.Enums;

public enum FinancialDocumentNature
{
    Provisional = 0,
    Firm = 1,
    Advance = 2,
    TaxWithholding = 3
}
```

`FinancialDocumentStatus.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

public enum FinancialDocumentStatus
{
    Open = 0,
    PartiallySettled = 1,
    Settled = 2,
    Canceled = 3
}
```

`FinancialDocumentOrigin.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

public enum FinancialDocumentOrigin
{
    Manual = 0,
    PurchaseContractPriceFixation = 1,
    SalesContractPriceFixation = 2,
    PurchaseInvoice = 3,
    SalesInvoice = 4,
    FinancialDocument = 5
}
```

`FinancialSettlementOrigin.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

public enum FinancialSettlementOrigin
{
    Manual = 0,
    Reversal = 1,
    InvoiceOffset = 2,
    AdvanceApplication = 3,
    Netting = 4
}
```

`FinancialAccountType.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

public enum FinancialAccountType
{
    Cash = 0,
    Bank = 1
}
```

- [ ] **Step 2: Acrescentar o TransactionCode**

Em `SiagroB1.Domain/Enums/TransactionCode.cs`, adicionar a última linha, **sem renumerar nada**:

```csharp
    ShipmentLoad = 11,
    FinancialDocument = 12,
```

- [ ] **Step 3: Criar `FinancialAccount`**

Sem classe base, com `[Key] Code` — é o padrão de cadastro simples da casa (`TAXES`, `COST_CENTERS`), e não `MasterEntity`, cujo `Code` é `VARCHAR(50)` fixo.

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Conta financeira: caixa ou banco. É por ela que a baixa sai ou entra.
/// Não guarda saldo — a posição de caixa é derivada do ledger de baixas e só ganha tela na Fase 4.
/// </summary>
[Table("FINANCIAL_ACCOUNTS")]
[Index(nameof(BranchCode))]
public class FinancialAccount
{
    [Key]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    public FinancialAccountType Type { get; set; }

    /// <summary>Código FEBRABAN. Nulo em conta do tipo Caixa.</summary>
    [Column(TypeName = "VARCHAR(3)")]
    public string? BankCode { get; set; }

    /// <summary>
    /// Nome do banco, desnormalizado. Não existe cadastro de bancos e não vale criar um nesta
    /// fase: teria um leitor e nenhum escritor além do próprio usuário.
    /// </summary>
    [Column(TypeName = "VARCHAR(100)")]
    public string? BankName { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? BankBranch { get; set; }

    /// <summary>Conta com dígito. TEXTO, nunca numérico — zeros à esquerda são significativos.</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? BankAccountNumber { get; set; }

    /// <summary>
    /// A conta é monomoeda. O guard de baixa recusa liquidar documento em moeda diferente
    /// da conta.
    /// </summary>
    [Column(TypeName = "INT DEFAULT 1")]
    public CurrencyType Currency { get; set; } = CurrencyType.Brl;

    /// <summary>
    /// Filial dona da conta; nulo = conta corporativa. FK real porque BRANCHS é tabela LOCAL
    /// nos dois modos de ERP.
    /// </summary>
    [Column(TypeName = "VARCHAR(14)")]
    [ForeignKey(nameof(Branch))]
    public string? BranchCode { get; set; }

    public virtual Branch? Branch { get; set; }

    public bool Inactive { get; set; }
}
```

- [ ] **Step 4: Criar `FinancialDocument`**

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Documento financeiro: uma obrigação a pagar ou a receber.
///
/// Na Fase 1 nasce PROVISÓRIO, a partir de cada fixação de preço confirmada, e é bloqueado
/// para baixa — representa o compromisso do contrato, não uma dívida exigível. A Fase 2 o
/// converte em FIRME pela confirmação do documento fiscal, e a partir daí OpenAmount de um
/// provisório significa "saldo a faturar".
///
/// A natureza ADVANCE (adiantamento) é a exceção da Fase 1: nasce desbloqueada e é liquidada
/// normalmente.
/// </summary>
[Table("FINANCIAL_DOCUMENTS")]
[Index(nameof(Code), IsUnique = true)]
[Index(nameof(Direction), nameof(Status), nameof(DueDate))]
[Index(nameof(CardCode))]
[Index(nameof(PurchaseContractKey))]
[Index(nameof(SalesContractKey))]
public class FinancialDocument : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }

    public FinancialDirection Direction { get; set; }

    public FinancialDocumentNature Nature { get; set; }

    /// <summary>
    /// Persistido-derivado de <see cref="SettledAmount"/>. Escritor ÚNICO:
    /// FinancialDocumentsRecalculateBalanceService. A exceção é <see cref="FinancialDocumentStatus.Canceled"/>,
    /// que só o cancelamento grava. Persistido, e não [NotMapped], porque as telas filtram e
    /// ordenam por ele NO SERVIDOR.
    /// </summary>
    public FinancialDocumentStatus Status { get; set; } = FinancialDocumentStatus.Open;

    /// <summary>SAP ENTITY — sem FK: em modo SAPB1 BUSINESS_PARTNERS está vazia.</summary>
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    /// <summary>Emissão: data da aprovação do contrato ou da confirmação da fixação.</summary>
    public DateTime DocumentDate { get; set; } = DateTime.Now;

    /// <summary>
    /// Obrigatório. Resolvido por <c>fixation.FinancialDueDate ?? contract.StandardCashFlowDate</c>
    /// e validado no gerador ANTES de qualquer escrita — documento sem vencimento não entra em
    /// fluxo de caixa nenhum.
    /// </summary>
    public DateTime DueDate { get; set; }

    [Column(TypeName = "INT DEFAULT 1")]
    public CurrencyType Currency { get; set; } = CurrencyType.Brl;

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal NetAmount { get; set; }

    /// <summary>
    /// Persistido-derivado: soma ASSINADA de FINANCIAL_SETTLEMENTS.Amount. Recalculado sempre
    /// por soma do ledger, NUNCA incrementalmente, e protegido por <see cref="RowVersion"/>.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal SettledAmount { get; set; }

    public FinancialDocumentOrigin OriginType { get; set; }

    /// <summary>
    /// Chave da linha geradora — a FIXAÇÃO, na Fase 1. SEM FK e SEM navegação: a linha precisa
    /// sobreviver ao registro que ela narra, como ShipmentLoadMovement.SalesInvoiceKey.
    /// Junto com <see cref="OriginType"/> forma a chave de idempotência do índice único filtrado.
    /// </summary>
    public Guid? OriginKey { get; set; }

    /// <summary>Código do contrato, desnormalizado — a lista mostra a origem sem join.</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? OriginDocNumber { get; set; }

    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public Guid? SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    /// <summary>
    /// Cópia de <c>fixation.PaymentDetails ?? contract.PaymentTerms</c>. É o texto que o
    /// financeiro lê para saber onde pagar. O cadastro estruturado de condição de pagamento é
    /// Fase 2, e vai entrar AO LADO deste campo, não no lugar dele.
    /// </summary>
    [Column(TypeName = "VARCHAR(1000)")]
    public string? PaymentTermsText { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? CancellationReason { get; set; }

    /// <summary>
    /// Concorrência real: duas baixas simultâneas do mesmo documento passam pelos dois guards
    /// e só aqui a segunda falha.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    public ICollection<FinancialSettlement> Settlements { get; set; } = [];

    public ICollection<FinancialDocumentChangeLog> ChangeLogs { get; set; } = [];

    [NotMapped]
    public decimal OpenAmount =>
        decimal.Round(NetAmount - SettledAmount, 2, MidpointRounding.ToEven);

    /// <summary>
    /// Derivado da natureza, SEM coluna — uma coluna criaria uma segunda fonte de verdade para
    /// manter em sincronia.
    /// </summary>
    [NotMapped]
    public bool IsBlockedForSettlement => Nature == FinancialDocumentNature.Provisional;

    [NotMapped]
    public bool IsOverdue => DueDate.Date < DateTime.Now.Date && OpenAmount > 0;

    /// <summary>
    /// Crédito de adiantamento disponível. Na Fase 1 é o que já foi pago, porque não existe
    /// documento firme para amortizar. A Fase 2 acrescenta AppliedAmount e esta expressão vira
    /// <c>SettledAmount - AppliedAmount</c>; o backfill sai do próprio ledger, pelo Origin.
    /// </summary>
    [NotMapped]
    public decimal AvailableAdvanceAmount =>
        Nature == FinancialDocumentNature.Advance ? SettledAmount : 0m;
}
```

Adicionar `using System.ComponentModel.DataAnnotations;` no topo (é de onde vem `[Timestamp]`).

- [ ] **Step 5: Criar `FinancialSettlement`**

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Ledger de baixas: uma linha por evento financeiro, com valor ASSINADO e origem enumerada.
/// INSERT-ONLY — é dinheiro, nunca se apaga; estorno é linha negativa apontando para a baixa
/// original por <see cref="ReversedSettlementKey"/>.
///
/// É deste ledger que FinancialDocument.SettledAmount é derivado, sempre por soma.
/// </summary>
[Table("FINANCIAL_SETTLEMENTS")]
[Index(nameof(FinancialDocumentKey))]
[Index(nameof(SettlementDate))]
[Index(nameof(FinancialAccountCode))]
public class FinancialSettlement : BaseEntity
{
    [ForeignKey(nameof(FinancialDocument))]
    public Guid FinancialDocumentKey { get; set; }

    public virtual FinancialDocument? FinancialDocument { get; set; }

    /// <summary>
    /// Nulável para as origens não-caixa das Fases 2 e 3 (abatimento pelo documento fiscal,
    /// amortização de adiantamento, encontro de contas), que não passam por conta financeira.
    /// Na Fase 1 o guard a exige.
    /// </summary>
    [Column(TypeName = "VARCHAR(10)")]
    [ForeignKey(nameof(FinancialAccount))]
    public string? FinancialAccountCode { get; set; }

    public virtual FinancialAccount? FinancialAccount { get; set; }

    /// <summary>Data-caixa EFETIVA, distinta de CreatedAt (quando a linha foi digitada).</summary>
    public DateTime SettlementDate { get; set; }

    /// <summary>ASSINADO: baixa positiva, estorno negativo.</summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal Amount { get; set; }

    /// <remarks>
    /// Juros, multa e desconto NÃO entram em <see cref="Amount"/>: Amount é o que abate o
    /// documento; os três compõem o valor efetivamente pago ou recebido. Somá-los faria o
    /// documento liquidar por valor diferente do devido.
    /// </remarks>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal InterestAmount { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal FineAmount { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal DiscountAmount { get; set; }

    public FinancialSettlementOrigin Origin { get; set; }

    /// <summary>
    /// A baixa que esta linha estorna. SEM FK: auto-relação para a mesma tabela deixaria a
    /// convenção do EF ambígua. O índice único filtrado impede estornar a mesma baixa duas vezes.
    /// </summary>
    public Guid? ReversedSettlementKey { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? DocumentReference { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Notes { get; set; }
}
```

- [ ] **Step 6: Criar `FinancialDocumentChangeLog` e as constantes de campo**

`FinancialDocumentChangeLog.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Uma alteração pontual num documento financeiro — responde "quem mudou o quê, quando, e o
/// que estava lá antes". Mesma estrutura do log do documento de saída.
///
/// NÃO cobre o ciclo de vida (criado, cancelado): para isso já existem os carimbos
/// CreatedBy/At, UpdatedBy/At e CanceledBy/At da entidade base, mais CancellationReason. E não
/// cobre dinheiro: o ledger de baixas é insert-only e já é a trilha. Cobre alteração DE CAMPO —
/// na Fase 1, o vencimento e as observações.
/// </summary>
[Table("FINANCIAL_DOCUMENT_CHANGE_LOGS")]
public class FinancialDocumentChangeLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? FinancialDocumentKey { get; set; }

    public virtual FinancialDocument? FinancialDocument { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? ChangedBy { get; set; }

    /// <summary>
    /// CÓDIGO do campo alterado, não o rótulo traduzido: a tela resolve o rótulo por formatter,
    /// para não travar o i18n.
    /// </summary>
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string Field { get; set; }

    /// <summary>Valor anterior. Nulo quando a linha registra uma INCLUSÃO.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? OldValue { get; set; }

    /// <summary>Valor novo. Nulo quando a linha registra uma REMOÇÃO.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? NewValue { get; set; }
}
```

`FinancialDocumentChangeLogFields.cs`:

```csharp
namespace SiagroB1.Domain.Entities;

/// <summary>
/// Códigos dos campos registrados em FINANCIAL_DOCUMENT_CHANGE_LOGS. São CÓDIGOS, não rótulos:
/// a tela traduz por formatter. No molde de ContractChangeLogFields.
/// </summary>
public static class FinancialDocumentChangeLogFields
{
    public const string DueDate = "DueDate";
    public const string Comments = "Comments";
}
```

- [ ] **Step 7: Registrar no `AppDbContext`**

Em `SiagroB1.Infra/Context/AppDbContext.cs`, adicionar os quatro `DbSet` junto aos demais:

```csharp
    public DbSet<FinancialAccount> FinancialAccounts { get; set; }
    public DbSet<FinancialDocument> FinancialDocuments { get; set; }
    public DbSet<FinancialSettlement> FinancialSettlements { get; set; }
    public DbSet<FinancialDocumentChangeLog> FinancialDocumentChangeLogs { get; set; }
```

E, no fim do `OnModelCreating` (**depois** da varredura que põe todas as FKs em `NoAction`, senão ela sobrescreve o que você declarar):

```csharp
        // Duas navegações para contratos DIFERENTES, declaradas à mão porque a convenção
        // emparelha errado em silêncio. WithMany() SEM coleção inversa: uma coleção nova no
        // contrato entraria no EDM do OData sem ninguém pedir.
        modelBuilder.Entity<FinancialDocument>()
            .HasOne(x => x.PurchaseContract).WithMany()
            .HasForeignKey(x => x.PurchaseContractKey).OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<FinancialDocument>()
            .HasOne(x => x.SalesContract).WithMany()
            .HasForeignKey(x => x.SalesContractKey).OnDelete(DeleteBehavior.NoAction);

        // A TRAVA DE IDEMPOTÊNCIA. Dois cliques em "Aprovar" gerariam dois provisórios
        // idênticos, e ninguém perceberia até o mês fechar com o dobro. Filtrado por
        // Provisional porque dois ADIANTAMENTOS no mesmo contrato são legítimos, e por
        // <> Canceled porque cancelar precisa LIBERAR a origem para a reabertura regenerar —
        // mesmo desenho do índice de PurchaseInvoice.ChaveNFe.
        modelBuilder.Entity<FinancialDocument>()
            .HasIndex(x => new { x.OriginType, x.OriginKey }, "IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin")
            .IsUnique()
            .HasFilter($"[Nature] = {(int)FinancialDocumentNature.Provisional} " +
                       $"AND [Status] <> {(int)FinancialDocumentStatus.Canceled} " +
                       "AND [OriginKey] IS NOT NULL");

        // Impede estornar a mesma baixa duas vezes.
        modelBuilder.Entity<FinancialSettlement>()
            .HasIndex(x => x.ReversedSettlementKey)
            .IsUnique()
            .HasFilter("[ReversedSettlementKey] IS NOT NULL");
```

Adicionar `using SiagroB1.Domain.Enums;` no topo do arquivo se ainda não houver.

- [ ] **Step 8: Compilar**

Run: `dotnet build SiagroB1.sln`
Expected: build sem erros. Se aparecer `CS0246` em `Branch`, `PurchaseContract` ou `SalesContract`, falta `using SiagroB1.Domain.Entities;`.

- [ ] **Step 9: Gerar a migration de schema**

Run:

```bash
dotnet ef migrations add CreateFinancialModule \
  --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

- [ ] **Step 10: Ler a migration gerada antes de aceitar**

Abra `SiagroB1.Migrations/AppContext/<timestamp>_CreateFinancialModule.cs` e confira, item por item:

- as **quatro** tabelas são criadas;
- `FINANCIAL_DOCUMENTS.RowVersion` sai como `rowversion`;
- os dois índices filtrados aparecem com o `filter:` correto;
- **nenhuma** FK tem `onDelete: ReferentialAction.Cascade`;
- não há FK para `BUSINESS_PARTNERS`.

Se algo estiver errado, corrija a entidade e regenere (`dotnet ef migrations remove --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`) — **não** edite a migration para mascarar erro de modelo.

- [ ] **Step 11: Verificar que o modelo e o snapshot estão sincronizados**

Run:

```bash
dotnet ef migrations has-pending-model-changes \
  --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

Expected: "No changes have been made to the model since the last migration."

- [ ] **Step 12: Stage**

```bash
git add SiagroB1.Domain/Enums/Financial*.cs SiagroB1.Domain/Enums/TransactionCode.cs \
        SiagroB1.Domain/Entities/Financial*.cs \
        SiagroB1.Infra/Context/AppDbContext.cs \
        SiagroB1.Migrations/AppContext/
```

---

## Task 2: Numeração do documento financeiro

**Files:**
- Create: `SiagroB1.Migrations/AppContext/<timestamp>_SeedFinancialDocumentDocNumber.cs`

**Interfaces:**
- Consumes: `TransactionCode.FinancialDocument = 12` (Task 1).
- Produces: uma linha `DOC_NUMBERS` com `TransactionCode = 12` e `[Default] = 1`, que `DocNumberSequenceService.GetKeyByTransactionCode` encontra.

- [ ] **Step 1: Criar a migration vazia**

Run:

```bash
dotnet ef migrations add SeedFinancialDocumentDocNumber \
  --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

- [ ] **Step 2: Escrever o seed idempotente**

Substituir o corpo de `Up`/`Down` pelo SQL abaixo. **SQL bruto e não `InsertData`**: `DOC_NUMBERS` tem índice único em `(TransactionCode, Name)`, e se o usuário já tiver criado a numeração à mão o `InsertData` derrubaria o deploy inteiro.

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext;

/// <summary>
/// Semeia a numeração do documento financeiro (TransactionCode 12).
/// Idempotente de propósito: o índice único (TransactionCode, Name) faria um INSERT cego
/// derrubar o deploy em qualquer base onde a numeração já tenha sido criada pela tela.
/// </summary>
public partial class SeedFinancialDocumentDocNumber : Migration
{
    private const string SeedKey = "3F6C1B84-9A27-4D50-8E13-7C2A5B4E9D60";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql($"""
            IF NOT EXISTS (SELECT 1 FROM DOC_NUMBERS WHERE TransactionCode = 12)
            INSERT INTO DOC_NUMBERS ([Key], TransactionCode, Name, FirstNumber, LastNumber,
                                     NextNumber, [Default], Prefix, Suffix, BranchCode,
                                     Inactive, IsManual, NumberSize)
            VALUES ('{SeedKey}', 12, 'FINANCEIRO', 1, 0, 1, 1, 'FN', '', NULL, 0, 0, '6');
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql($"DELETE FROM DOC_NUMBERS WHERE [Key] = '{SeedKey}';");
}
```

- [ ] **Step 3: Aplicar as duas migrations no banco local**

Run (env **explícito** — o profile `db-migration` aponta para produção e não deve ser usado):

```bash
ASPNETCORE_ENVIRONMENT=Yokotobi-Development dotnet ef database update \
  --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

- [ ] **Step 4: Rodar de novo, para provar a idempotência**

Run: o mesmo comando do Step 3.
Expected: termina sem erro e sem duplicar. Confirme com
`SELECT COUNT(*) FROM DOC_NUMBERS WHERE TransactionCode = 12` → **1**.

- [ ] **Step 5: Stage**

```bash
git add SiagroB1.Migrations/AppContext/
```

---

## Task 3: Cadastro de conta financeira

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialAccountsCreateService.cs`, `FinancialAccountsUpdateService.cs`, `FinancialAccountsGetService.cs`, `FinancialAccountsDeleteService.cs`
- Create: `SiagroB1.Web/Controllers/FinancialAccountsController.cs`
- Create: `SiagroB1.Application.Tests/Financials/FinancialAccountsCreateServiceTests.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: `FinancialAccount`, `FinancialAccountType` (Task 1).
- Produces: `FinancialAccountsGetService.QueryAll() : IQueryable<FinancialAccount>` e `GetByIdAsync(string code) : Task<FinancialAccount?>`, usados pelo guard de baixa na Task 8. Entity set OData `FinancialAccounts`.

- [ ] **Step 1: Escrever o teste que falha**

`SiagroB1.Application.Tests/Financials/FinancialAccountsCreateServiceTests.cs`:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialAccountsCreateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAccountsCreateService Service() => new(_db);

    private static FinancialAccount Form(
        string code = "CX01",
        string name = "Caixa Geral",
        FinancialAccountType type = FinancialAccountType.Cash,
        string? bankAccountNumber = null) => new()
    {
        Code = code,
        Name = name,
        Type = type,
        BankAccountNumber = bankAccountNumber
    };

    [Fact]
    public async Task A_cash_account_is_created_with_the_default_currency()
    {
        var account = await Service().ExecuteAsync(Form(), "tester");

        Assert.Equal("CX01", account.Code);
        Assert.Equal(CurrencyType.Brl, account.Currency);
        Assert.False(account.Inactive);
    }

    [Fact]
    public async Task Refuses_a_bank_account_without_the_account_number()
    {
        var form = Form(code: "BB01", name: "Banco do Brasil", type: FinancialAccountType.Bank);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(form, "tester"));

        Assert.Contains("conta", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_duplicated_code()
    {
        await Service().ExecuteAsync(Form(), "tester");

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(Form(name: "Outro caixa"), "tester"));

        Assert.Contains("já existe", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Rodar o teste e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialAccountsCreateServiceTests`
Expected: falha de compilação — `FinancialAccountsCreateService` não existe.

- [ ] **Step 3: Implementar o serviço de criação**

`SiagroB1.Application/Services/Financials/FinancialAccountsCreateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialAccountsCreateService(IUnitOfWork db)
{
    public async Task<FinancialAccount> ExecuteAsync(FinancialAccount account, string userName)
    {
        await ValidateAsync(account);

        account.Code = account.Code.Trim().ToUpperInvariant();

        try
        {
            await db.BeginTransactionAsync();
            db.Context.FinancialAccounts.Add(account);
            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        return account;
    }

    private async Task ValidateAsync(FinancialAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.Code))
            throw new ApplicationException("Informe o código da conta financeira.");

        if (string.IsNullOrWhiteSpace(account.Name))
            throw new ApplicationException("Informe o nome da conta financeira.");

        if (account.Type == FinancialAccountType.Bank)
        {
            if (string.IsNullOrWhiteSpace(account.BankCode))
                throw new ApplicationException("Informe o código do banco.");

            if (string.IsNullOrWhiteSpace(account.BankAccountNumber))
                throw new ApplicationException("Informe o número da conta bancária.");
        }

        var code = account.Code.Trim().ToUpperInvariant();

        if (await db.Context.FinancialAccounts.AnyAsync(x => x.Code == code))
            throw new ApplicationException($"A conta financeira {code} já existe.");
    }
}
```

- [ ] **Step 4: Rodar o teste e confirmar que passa**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialAccountsCreateServiceTests`
Expected: 3 testes passando.

- [ ] **Step 5: Implementar leitura, alteração e exclusão**

`FinancialAccountsGetService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialAccountsGetService(IUnitOfWork db)
{
    public IQueryable<FinancialAccount> QueryAll() =>
        db.Context.FinancialAccounts.AsNoTracking();

    public Task<FinancialAccount?> GetByIdAsync(string code) =>
        db.Context.FinancialAccounts.FirstOrDefaultAsync(x => x.Code == code);
}
```

`FinancialAccountsUpdateService.cs` — recusa trocar a moeda depois que a conta já movimentou, porque isso reinterpretaria baixas passadas:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialAccountsUpdateService(IUnitOfWork db)
{
    public async Task<FinancialAccount> ExecuteAsync(string code, FinancialAccount entity)
    {
        var existing = await db.Context.FinancialAccounts.FirstOrDefaultAsync(x => x.Code == code)
                       ?? throw new NotFoundException($"Conta financeira {code} não encontrada.");

        if (existing.Currency != entity.Currency &&
            await db.Context.FinancialSettlements.AnyAsync(x => x.FinancialAccountCode == code))
            throw new ApplicationException(
                "A conta já possui baixas: não é possível alterar a moeda.");

        db.Context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Code = code;

        await db.SaveChangesAsync();
        return existing;
    }
}
```

`FinancialAccountsDeleteService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialAccountsDeleteService(IUnitOfWork db)
{
    public async Task ExecuteAsync(string code)
    {
        var existing = await db.Context.FinancialAccounts.FirstOrDefaultAsync(x => x.Code == code)
                       ?? throw new NotFoundException($"Conta financeira {code} não encontrada.");

        if (await db.Context.FinancialSettlements.AnyAsync(x => x.FinancialAccountCode == code))
            throw new ApplicationException(
                "A conta possui baixas e não pode ser excluída. Inative-a.");

        db.Context.FinancialAccounts.Remove(existing);
        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 6: Criar o controller**

`SiagroB1.Web/Controllers/FinancialAccountsController.cs` — CRUD direto no entity set, como `LogisticRegionsController`; cadastro simples não vira action:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Controllers;

public class FinancialAccountsController(
    FinancialAccountsCreateService createService,
    FinancialAccountsUpdateService updateService,
    FinancialAccountsDeleteService deleteService,
    FinancialAccountsGetService getService) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialAccount>> Get() => Ok(getService.QueryAll());

    [EnableQuery]
    public async Task<ActionResult<FinancialAccount>> Get([FromRoute] string key)
    {
        var entity = await getService.GetByIdAsync(key);
        return entity is null ? NotFound() : Ok(entity);
    }

    public async Task<IActionResult> Post([FromBody] FinancialAccount entity)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var created = await createService.ExecuteAsync(entity, User.Identity?.Name ?? "Unknown");
            return Created(created);
        }
        catch (Exception e) { return Map(e); }
    }

    public async Task<IActionResult> Put([FromRoute] string key, [FromBody] FinancialAccount entity)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            await updateService.ExecuteAsync(key, entity);
            return NoContent();
        }
        catch (Exception e) { return Map(e); }
    }

    public async Task<IActionResult> Delete([FromRoute] string key)
    {
        try
        {
            await deleteService.ExecuteAsync(key);
            return NoContent();
        }
        catch (Exception e) { return Map(e); }
    }

    private IActionResult Map(Exception e) => e switch
    {
        NotFoundException or KeyNotFoundException => NotFound(e.Message),
        DefaultException or ApplicationException => BadRequest(e.Message),
        _ => StatusCode(500, e.Message)
    };
}
```

- [ ] **Step 7: Registrar o entity set e o DI**

Em `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, junto aos demais entity sets:

```csharp
modelBuilder.EntitySet<FinancialAccount>("FinancialAccounts");
```

Em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, dentro de `AddApplicationServices()`, com `using SiagroB1.Application.Services.Financials;` no topo:

```csharp
        // financials
        services.AddScoped<FinancialAccountsCreateService>();
        services.AddScoped<FinancialAccountsUpdateService>();
        services.AddScoped<FinancialAccountsGetService>();
        services.AddScoped<FinancialAccountsDeleteService>();
```

- [ ] **Step 8: Rodar a suíte inteira**

Run: `dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests`
Expected: build limpo, suíte verde (os 3 testes novos incluídos).

- [ ] **Step 9: Stage**

```bash
git add SiagroB1.Application/Services/Financials/ \
        SiagroB1.Application.Tests/Financials/ \
        SiagroB1.Web/Controllers/FinancialAccountsController.cs \
        SiagroB1.Web/ODataConfig/ODataConfigurations.cs \
        SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs
```

---

## Task 4: Saldo derivado-e-persistido e leitura do documento

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsRecalculateBalanceService.cs`, `FinancialDocumentsGetService.cs`
- Create: `SiagroB1.Domain/Dtos/FinancialDocumentRecalcResultDto.cs`
- Create: `SiagroB1.Application.Tests/Financials/FinancialDocumentsRecalculateBalanceServiceTests.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Create: `SiagroB1.Web/Controllers/FinancialDocumentsController.cs`, `FinancialSettlementsController.cs`

**Interfaces:**
- Consumes: `FinancialDocument`, `FinancialSettlement` (Task 1).
- Produces:
  - `static Task FinancialDocumentsRecalculateBalanceService.RecalculateAsync(AppDbContext context, Guid documentKey)` — **não salva**; usada pelas Tasks 8 e 9 de dentro da transação delas.
  - `Task<FinancialDocumentRecalcResultDto> ExecuteAsync(Guid key)` — salva; usada pela action de recálculo.
  - `FinancialDocumentsGetService.QueryAll() : IQueryable<FinancialDocument>`, `GetByIdAsync(Guid key) : Task<FinancialDocument?>`.

- [ ] **Step 1: Escrever o teste que falha**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsRecalculateBalanceServiceTests.cs`:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsRecalculateBalanceServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<FinancialDocument> SeedDocumentAsync(decimal netAmount = 1000m)
    {
        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FN000001",
            CardCode = "F0001",
            Direction = FinancialDirection.Payable,
            Nature = FinancialDocumentNature.Advance,
            DueDate = DateTime.Today.AddDays(30),
            NetAmount = netAmount
        };

        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();
        return document;
    }

    private async Task AddSettlementAsync(Guid documentKey, decimal amount)
    {
        _db.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = documentKey,
            FinancialAccountCode = "CX01",
            SettlementDate = DateTime.Today,
            Amount = amount,
            Origin = amount >= 0
                ? FinancialSettlementOrigin.Manual
                : FinancialSettlementOrigin.Reversal
        });

        await _db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task An_untouched_document_is_open_with_the_full_amount_outstanding()
    {
        var document = await SeedDocumentAsync();

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(0m, document.SettledAmount);
        Assert.Equal(1000m, document.OpenAmount);
        Assert.Equal(FinancialDocumentStatus.Open, document.Status);
    }

    [Fact]
    public async Task A_partial_settlement_leaves_the_document_partially_settled()
    {
        var document = await SeedDocumentAsync();
        await AddSettlementAsync(document.Key, 400m);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(400m, document.SettledAmount);
        Assert.Equal(600m, document.OpenAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, document.Status);
    }

    [Fact]
    public async Task Settling_the_whole_amount_settles_the_document()
    {
        var document = await SeedDocumentAsync();
        await AddSettlementAsync(document.Key, 1000m);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(FinancialDocumentStatus.Settled, document.Status);
        Assert.Equal(0m, document.OpenAmount);
    }

    [Fact]
    public async Task A_reversal_is_a_negative_line_and_reopens_the_document()
    {
        var document = await SeedDocumentAsync();
        await AddSettlementAsync(document.Key, 1000m);
        await AddSettlementAsync(document.Key, -1000m);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(0m, document.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, document.Status);
    }

    [Fact]
    public async Task A_canceled_document_keeps_its_status_when_recalculated()
    {
        var document = await SeedDocumentAsync();
        document.Status = FinancialDocumentStatus.Canceled;
        await _db.Context.SaveChangesAsync();

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();

        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialDocumentsRecalculateBalanceServiceTests`
Expected: falha de compilação — o serviço não existe.

- [ ] **Step 3: Implementar o recalculador**

Par estático/instância, como `ShipmentLoadsRecalculateInvoicedService`: o estático compõe dentro de transação alheia e **não salva**; o de instância é o ponto de entrada da action.

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// ESCRITOR ÚNICO de <see cref="FinancialDocument.SettledAmount"/> e de
/// <see cref="FinancialDocument.Status"/>.
///
/// O saldo é sempre derivado da SOMA do ledger, nunca incremental: é a mesma regra de
/// SalesContractsRecalculateBalanceService, e é o que faz baixa, estorno e (nas fases
/// seguintes) abatimento e compensação conviverem sem divergir.
///
/// A única exceção ao "escritor único" é <see cref="FinancialDocumentStatus.Canceled"/>, que
/// só o cancelamento grava e que este serviço NUNCA sobrescreve.
/// </summary>
public class FinancialDocumentsRecalculateBalanceService(IUnitOfWork db)
{
    /// <summary>
    /// Recalcula e apenas ENFILEIRA a alteração — quem chama decide quando salvar. É esta
    /// sobrecarga que a baixa e o estorno usam, de dentro da transação deles.
    /// </summary>
    public static async Task RecalculateAsync(AppDbContext context, Guid documentKey)
    {
        var document = await context.FinancialDocuments
                           .FirstOrDefaultAsync(x => x.Key == documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        var settled = await context.FinancialSettlements
            .Where(x => x.FinancialDocumentKey == documentKey)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        document.SettledAmount = decimal.Round(settled, 2, MidpointRounding.ToEven);

        if (document.Status == FinancialDocumentStatus.Canceled)
            return;

        document.Status = document.SettledAmount switch
        {
            <= 0m => FinancialDocumentStatus.Open,
            var s when s >= document.NetAmount => FinancialDocumentStatus.Settled,
            _ => FinancialDocumentStatus.PartiallySettled
        };
    }

    public async Task<FinancialDocumentRecalcResultDto> ExecuteAsync(Guid key)
    {
        await RecalculateAsync(db.Context, key);
        await db.SaveChangesAsync();

        var document = await db.Context.FinancialDocuments.AsNoTracking()
                           .FirstAsync(x => x.Key == key);

        return new FinancialDocumentRecalcResultDto
        {
            Key = document.Key,
            Code = document.Code,
            NetAmount = document.NetAmount,
            SettledAmount = document.SettledAmount,
            OpenAmount = document.OpenAmount,
            Status = document.Status.ToString()
        };
    }
}
```

`SiagroB1.Domain/Dtos/FinancialDocumentRecalcResultDto.cs` — `[JsonPropertyName]` em PascalCase é obrigatório, senão o UI5 lê `undefined`:

```csharp
using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class FinancialDocumentRecalcResultDto
{
    [JsonPropertyName("Key")] public Guid Key { get; set; }
    [JsonPropertyName("Code")] public string? Code { get; set; }
    [JsonPropertyName("NetAmount")] public decimal NetAmount { get; set; }
    [JsonPropertyName("SettledAmount")] public decimal SettledAmount { get; set; }
    [JsonPropertyName("OpenAmount")] public decimal OpenAmount { get; set; }
    [JsonPropertyName("Status")] public string? Status { get; set; }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialDocumentsRecalculateBalanceServiceTests`
Expected: 5 testes passando.

- [ ] **Step 5: Implementar a leitura**

`FinancialDocumentsGetService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialDocumentsGetService(IUnitOfWork db)
{
    public IQueryable<FinancialDocument> QueryAll() =>
        db.Context.FinancialDocuments.AsNoTracking();

    public Task<FinancialDocument?> GetByIdAsync(Guid key) =>
        db.Context.FinancialDocuments
            .Include(x => x.Settlements)
            .Include(x => x.ChangeLogs)
            .FirstOrDefaultAsync(x => x.Key == key);
}
```

- [ ] **Step 6: Criar os controllers de leitura**

`FinancialDocuments` e `FinancialSettlements` são **somente leitura**: toda mutação passa por action, para que a mutação e o recálculo do saldo caiam no mesmo `SaveChanges` de um serviço único.

`SiagroB1.Web/Controllers/FinancialDocumentsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

public class FinancialDocumentsController(FinancialDocumentsGetService getService) : ODataController
{
    [EnableQuery(MaxExpansionDepth = 5)]
    public ActionResult<IEnumerable<FinancialDocument>> Get() => Ok(getService.QueryAll());

    // Rota literal nas DUAS formas: a não declarada toma 404 no UI5.
    [HttpGet("odata/FinancialDocuments({key:guid})")]
    [HttpGet("odata/FinancialDocuments/{key:guid}")]
    [EnableQuery(MaxExpansionDepth = 5)]
    public async Task<ActionResult<FinancialDocument>> Get([FromRoute] Guid key)
    {
        var entity = await getService.GetByIdAsync(key);
        return entity is null ? NotFound() : Ok(entity);
    }

    [HttpGet("odata/FinancialDocuments({key:guid})/Settlements")]
    [HttpGet("odata/FinancialDocuments/{key:guid}/Settlements")]
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialSettlement>> GetSettlements([FromRoute] Guid key) =>
        Ok(getService.QueryAll().Where(x => x.Key == key).SelectMany(x => x.Settlements));

    [HttpGet("odata/FinancialDocuments({key:guid})/ChangeLogs")]
    [HttpGet("odata/FinancialDocuments/{key:guid}/ChangeLogs")]
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialDocumentChangeLog>> GetChangeLogs([FromRoute] Guid key) =>
        Ok(getService.QueryAll().Where(x => x.Key == key).SelectMany(x => x.ChangeLogs));
}
```

`SiagroB1.Web/Controllers/FinancialSettlementsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Web.Controllers;

public class FinancialSettlementsController(FinancialDocumentsGetService getService) : ODataController
{
    [EnableQuery]
    public ActionResult<IEnumerable<FinancialSettlement>> Get() =>
        Ok(getService.QueryAll().SelectMany(x => x.Settlements));
}
```

- [ ] **Step 7: Registrar entity sets, `AddProperty` e DI**

Em `ODataConfigurations.cs`:

```csharp
modelBuilder.EntitySet<FinancialDocument>("FinancialDocuments");
modelBuilder.EntitySet<FinancialSettlement>("FinancialSettlements");
modelBuilder.EntitySet<FinancialDocumentChangeLog>("FinancialDocumentChangeLogs");

// [NotMapped] some do EDM: sem estes quatro AddProperty, $select=OpenAmount devolve 400 e a
// tela não consegue mostrar o saldo.
foreach (var property in new[]
         {
             nameof(FinancialDocument.OpenAmount),
             nameof(FinancialDocument.IsBlockedForSettlement),
             nameof(FinancialDocument.IsOverdue),
             nameof(FinancialDocument.AvailableAdvanceAmount)
         })
{
    modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(FinancialDocument))
        .AddProperty(typeof(FinancialDocument).GetProperty(property));
}
```

Em `ServiceCollectionExtensions.cs`, no bloco `// financials`:

```csharp
        services.AddScoped<FinancialDocumentsGetService>();
        services.AddScoped<FinancialDocumentsRecalculateBalanceService>();
```

- [ ] **Step 8: Escrever o teste do EDM**

`SiagroB1.Application.Tests/Financials/FinancialEdmModelTests.cs`:

```csharp
using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialEdmModelTests
{
    private static Microsoft.OData.Edm.IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("FinancialAccounts")]
    [InlineData("FinancialDocuments")]
    [InlineData("FinancialSettlements")]
    public void The_entity_sets_are_exposed(string entitySet)
    {
        Assert.NotNull(BuildModel().EntityContainer.FindEntitySet(entitySet));
    }

    [Theory]
    [InlineData(nameof(FinancialDocument.OpenAmount))]
    [InlineData(nameof(FinancialDocument.IsBlockedForSettlement))]
    [InlineData(nameof(FinancialDocument.IsOverdue))]
    [InlineData(nameof(FinancialDocument.AvailableAdvanceAmount))]
    public void The_computed_properties_survive_into_the_edm(string property)
    {
        var type = BuildModel().EntityContainer
            .FindEntitySet("FinancialDocuments").EntityType;

        Assert.NotNull(type.FindProperty(property));
    }
}
```

- [ ] **Step 9: Rodar a suíte**

Run: `dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests`
Expected: tudo verde. Se `The_computed_properties_survive_into_the_edm` falhar, faltou um `AddProperty`.

- [ ] **Step 10: Stage**

```bash
git add SiagroB1.Application/Services/Financials/ SiagroB1.Application.Tests/Financials/ \
        SiagroB1.Domain/Dtos/ SiagroB1.Web/
```

---

## Task 5: Gerador do provisório e cancelamento (ainda sem gancho)

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateService.cs`, `FinancialDocumentsCancelService.cs`, `FinancialDocumentChangeLogService.cs`
- Create: `SiagroB1.Application.Tests/Financials/FinancialDocumentsGenerateServiceTests.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: Task 1 (entidades/enums), `DocNumberSequenceService`, `IBusinessPartnerService`.
- Produces:
  - `Task FinancialDocumentsGenerateService.EnqueueForPurchaseFixationAsync(PurchaseContract contract, PurchaseContractPriceFixation fixation, string userName)`
  - `Task FinancialDocumentsGenerateService.EnqueueForSalesFixationAsync(SalesContract contract, SalesContractPriceFixation fixation, string userName)`
  - `Task FinancialDocumentsCancelService.EnqueueCancelByOriginAsync(FinancialDocumentOrigin originType, Guid originKey, string reason, string userName)`
  - `Task FinancialDocumentsCancelService.EnqueueCancelByContractAsync(Guid? purchaseContractKey, Guid? salesContractKey, string reason, string userName)`
  - `void FinancialDocumentChangeLogService.Register(Guid documentKey, string field, string? oldValue, string? newValue, string userName)`

  Todos **enqueue-only**: chamam `Add`/alteram entidade rastreada e **nunca** `SaveChangesAsync`.

- [ ] **Step 1: Escrever o teste que falha**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsGenerateServiceTests.cs`:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsGenerateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsGenerateService Service() => new(
        _db.Context,
        new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" }));

    private static PurchaseContract Contract(
        DateTime? cashFlowDate = null,
        decimal totalVolume = 1000m,
        decimal standardPrice = 100m) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC0001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        ItemName = "SOJA GRAO",
        BranchCode = "01",
        TotalVolume = totalVolume,
        StandardPrice = standardPrice,
        StandardCashFlowDate = cashFlowDate,
        StandardCurrency = CurrencyType.Brl,
        Type = ContractType.Fixed,
        Status = ContractStatus.Approved
    };

    private static PurchaseContractPriceFixation Fixation(
        PurchaseContract contract,
        DateTime? financialDueDate = null,
        decimal? volume = null,
        decimal? price = null) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contract.Key,
        FixationVolume = volume ?? contract.TotalVolume,
        FixationPrice = price ?? contract.StandardPrice,
        FinancialDueDate = financialDueDate,
        Status = PriceFixationStatus.Confirmed
    };

    [Fact]
    public async Task A_confirmed_fixation_produces_one_payable_provisional_document()
    {
        var contract = Contract(cashFlowDate: new DateTime(2026, 12, 31));
        var fixation = Fixation(contract);

        await Service().EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        var document = Assert.Single(_db.Context.FinancialDocuments);
        Assert.Equal(FinancialDirection.Payable, document.Direction);
        Assert.Equal(FinancialDocumentNature.Provisional, document.Nature);
        Assert.Equal(FinancialDocumentStatus.Open, document.Status);
        Assert.True(document.IsBlockedForSettlement);
        Assert.Equal(100_000m, document.NetAmount);
        Assert.Equal(new DateTime(2026, 12, 31), document.DueDate);
        Assert.Equal(fixation.Key, document.OriginKey);
        Assert.Equal(FinancialDocumentOrigin.PurchaseContractPriceFixation, document.OriginType);
        Assert.Equal("PC0001", document.OriginDocNumber);
        Assert.Equal("PRODUTOR TESTE", document.CardName);
        Assert.Equal(contract.Key, document.PurchaseContractKey);
    }

    [Fact]
    public async Task The_fixation_due_date_wins_over_the_contract_forecast()
    {
        var contract = Contract(cashFlowDate: new DateTime(2026, 12, 31));
        var fixation = Fixation(contract, financialDueDate: new DateTime(2026, 6, 15));

        await Service().EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(new DateTime(2026, 6, 15), _db.Context.FinancialDocuments.Single().DueDate);
    }

    [Fact]
    public async Task The_amount_is_rounded_to_two_decimals()
    {
        var contract = Contract(cashFlowDate: DateTime.Today);
        var fixation = Fixation(contract, volume: 333.333m, price: 100.005m);

        await Service().EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        // 333.333 * 100.005 = 33334.966665 -> 33334.97
        Assert.Equal(33_334.97m, _db.Context.FinancialDocuments.Single().NetAmount);
    }

    [Fact]
    public async Task Refuses_a_fixed_price_contract_without_the_payment_forecast()
    {
        var contract = Contract(cashFlowDate: null);
        var fixation = Fixation(contract);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().EnqueueForPurchaseFixationAsync(contract, fixation, "tester"));

        Assert.Contains("previsão de pagamento", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_to_be_determined_fixation_without_a_financial_due_date()
    {
        var contract = Contract(cashFlowDate: null);
        contract.Type = ContractType.ToBeDetermined;
        var fixation = Fixation(contract, financialDueDate: null);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().EnqueueForPurchaseFixationAsync(contract, fixation, "tester"));

        Assert.Contains("vencimento financeiro", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Generating_twice_for_the_same_fixation_does_not_duplicate()
    {
        var contract = Contract(cashFlowDate: DateTime.Today);
        var fixation = Fixation(contract);
        var service = Service();

        await service.EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();
        await service.EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Single(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task A_canceled_document_frees_the_origin_for_regeneration()
    {
        var contract = Contract(cashFlowDate: DateTime.Today);
        var fixation = Fixation(contract);
        var service = Service();

        await service.EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        _db.Context.FinancialDocuments.Single().Status = FinancialDocumentStatus.Canceled;
        await _db.Context.SaveChangesAsync();

        await service.EnqueueForPurchaseFixationAsync(contract, fixation, "tester");
        await _db.Context.SaveChangesAsync();

        Assert.Equal(2, _db.Context.FinancialDocuments.Count());
        Assert.Single(_db.Context.FinancialDocuments.Where(x => x.Status == FinancialDocumentStatus.Open));
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialDocumentsGenerateServiceTests`
Expected: falha de compilação — o serviço não existe.

- [ ] **Step 3: Implementar o log de alterações**

`FinancialDocumentChangeLogService.cs` — porta única de escrita, no molde de `PurchaseContractsChangeLogService`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Porta ÚNICA de escrita do log do documento financeiro. Só enfileira a linha no
/// ChangeTracker — quem salva é o serviço de mutação que chamou. É isso que faz o log nascer e
/// morrer junto com a alteração: operação revertida não deixa log, operação salva nunca o perde.
/// </summary>
public class FinancialDocumentChangeLogService(AppDbContext context)
{
    public void Register(Guid documentKey, string field, string? oldValue, string? newValue, string userName)
    {
        context.FinancialDocumentChangeLogs.Add(new FinancialDocumentChangeLog
        {
            FinancialDocumentKey = documentKey,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = DateTime.Now,
            ChangedBy = userName
        });
    }
}
```

- [ ] **Step 4: Implementar o gerador**

`FinancialDocumentsGenerateService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Gera o documento financeiro PROVISÓRIO de UMA fixação de preço confirmada.
///
/// ENQUEUE-ONLY: faz Add e nunca chama SaveChangesAsync. É o que permite engancharem-no nos
/// serviços de aprovação de contrato, que injetam AppDbContext direto e salvam uma vez só —
/// o documento e a mudança de status caem no MESMO SaveChanges, atômicos sem transação
/// explícita. Mesmo idioma de ContractNotificationOutboxService.Register.
///
/// Um caminho de código para os dois tipos de contrato: o de preço fixo já nasce com uma
/// fixação Confirmed (PurchaseContractsCreateService.CreatePriceFixation), então "iterar as
/// fixações confirmadas" produz 1 documento no FIX e 0 no PAF, sem nenhum if de tipo.
///
/// ⚠️ Chame ANTES de abrir transação: DocNumberSequenceService roda por Dapper numa conexão
/// separada, que não participa da transação do EF, e segura UPDLOCK em DOC_NUMBERS.
///
/// FASE 2: quando existir documento FIRME, o valor terá de descontar os firmes já emitidos
/// contra esta origem, senão a reabertura de contrato conta duas vezes. Hoje essa soma é
/// sempre zero e a consulta seria código morto.
/// </summary>
public class FinancialDocumentsGenerateService(
    AppDbContext context,
    DocNumberSequenceService docNumberSequence,
    IBusinessPartnerService businessPartnerService)
{
    public Task EnqueueForPurchaseFixationAsync(
        PurchaseContract contract, PurchaseContractPriceFixation fixation, string userName) =>
        EnqueueAsync(
            direction: FinancialDirection.Payable,
            originType: FinancialDocumentOrigin.PurchaseContractPriceFixation,
            originKey: fixation.Key,
            contractCode: contract.Code,
            cardCode: contract.CardCode,
            branchCode: contract.BranchCode,
            currency: contract.StandardCurrency ?? CurrencyType.Brl,
            contractType: contract.Type,
            volume: fixation.FixationVolume,
            price: fixation.FixationPrice,
            fixationDueDate: fixation.FinancialDueDate,
            contractCashFlowDate: contract.StandardCashFlowDate,
            paymentTermsText: fixation.PaymentDetails ?? contract.PaymentTerms,
            purchaseContractKey: contract.Key,
            salesContractKey: null,
            userName: userName);

    public Task EnqueueForSalesFixationAsync(
        SalesContract contract, SalesContractPriceFixation fixation, string userName) =>
        EnqueueAsync(
            direction: FinancialDirection.Receivable,
            originType: FinancialDocumentOrigin.SalesContractPriceFixation,
            originKey: fixation.Key,
            contractCode: contract.Code,
            cardCode: contract.CardCode,
            branchCode: contract.BranchCode,
            currency: contract.StandardCurrency ?? CurrencyType.Brl,
            contractType: contract.Type,
            volume: fixation.FixationVolume,
            price: fixation.FixationPrice,
            fixationDueDate: fixation.FinancialDueDate,
            contractCashFlowDate: contract.StandardCashFlowDate,
            paymentTermsText: fixation.PaymentDetails ?? contract.PaymentTerms,
            purchaseContractKey: null,
            salesContractKey: contract.Key,
            userName: userName);

    private async Task EnqueueAsync(
        FinancialDirection direction,
        FinancialDocumentOrigin originType,
        Guid originKey,
        string? contractCode,
        string cardCode,
        string? branchCode,
        CurrencyType currency,
        ContractType contractType,
        decimal volume,
        decimal price,
        DateTime? fixationDueDate,
        DateTime? contractCashFlowDate,
        string? paymentTermsText,
        Guid? purchaseContractKey,
        Guid? salesContractKey,
        string userName)
    {
        var dueDate = fixationDueDate ?? contractCashFlowDate
            ?? throw new ApplicationException(
                contractType == ContractType.ToBeDetermined
                    ? "Informe o vencimento financeiro da fixação de preço para gerar o título."
                    : "Informe a previsão de pagamento do contrato para gerar o título.");

        // Primeira camada de idempotência: mensagem amigável. A segunda é o índice único
        // filtrado IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin, que sobrevive a um caminho novo
        // que esqueça esta consulta.
        var alreadyGenerated = await context.FinancialDocuments.AnyAsync(x =>
            x.OriginType == originType &&
            x.OriginKey == originKey &&
            x.Nature == FinancialDocumentNature.Provisional &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (alreadyGenerated) return;

        var partner = await businessPartnerService.GetByIdAsync(cardCode);

        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);

        context.FinancialDocuments.Add(new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = await docNumberSequence.GetDocNumber(docNumberKey),
            DocNumberKey = docNumberKey,
            BranchCode = branchCode,
            Direction = direction,
            Nature = FinancialDocumentNature.Provisional,
            Status = FinancialDocumentStatus.Open,
            CardCode = cardCode,
            CardName = partner?.CardName,
            DocumentDate = DateTime.Now,
            DueDate = dueDate,
            Currency = currency,
            NetAmount = decimal.Round(volume * price, 2, MidpointRounding.ToEven),
            SettledAmount = 0m,
            OriginType = originType,
            OriginKey = originKey,
            OriginDocNumber = contractCode,
            PurchaseContractKey = purchaseContractKey,
            SalesContractKey = salesContractKey,
            PaymentTermsText = paymentTermsText,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        });
    }
}
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialDocumentsGenerateServiceTests`
Expected: 7 testes passando.

- [ ] **Step 6: Implementar o cancelamento**

`FinancialDocumentsCancelService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Cancela documento financeiro. CANCELA, NUNCA APAGA: apagar funcionaria e mataria o rastro —
/// é a tentação óbvia desta feature. Cancelar também é o que LIBERA a origem no índice único
/// filtrado, permitindo que a reabertura de contrato regenere o provisório.
/// </summary>
public class FinancialDocumentsCancelService(IUnitOfWork db)
{
    private AppDbContext Context => db.Context;

    /// <summary>Ponto de entrada da tela: cancela um documento e SALVA.</summary>
    public async Task ExecuteAsync(Guid key, string reason, string userName)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo do cancelamento.");

        var document = await Context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} já está cancelado.");

        if (document.SettledAmount != 0m)
            throw new ApplicationException(
                $"O documento {document.Code} possui baixas. Estorne-as antes de cancelar.");

        Cancel(document, reason, userName);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Enqueue-only, para os hooks de contrato: cancela o provisório de UMA origem e não salva.
    /// </summary>
    public async Task EnqueueCancelByOriginAsync(
        FinancialDocumentOrigin originType, Guid originKey, string reason, string userName)
    {
        var documents = await Context.FinancialDocuments
            .Where(x => x.OriginType == originType &&
                        x.OriginKey == originKey &&
                        x.Nature == FinancialDocumentNature.Provisional &&
                        x.Status != FinancialDocumentStatus.Canceled)
            .ToListAsync();

        foreach (var document in documents)
            Cancel(document, reason, userName);
    }

    /// <summary>
    /// Enqueue-only: cancela TODOS os provisórios abertos de um contrato. Adiantamento não é
    /// tocado — Nature é filtrado por Provisional — porque o dinheiro do adiantamento pode já
    /// ter saído.
    /// </summary>
    public async Task EnqueueCancelByContractAsync(
        Guid? purchaseContractKey, Guid? salesContractKey, string reason, string userName)
    {
        var documents = await Context.FinancialDocuments
            .Where(x => x.Nature == FinancialDocumentNature.Provisional &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        ((purchaseContractKey != null && x.PurchaseContractKey == purchaseContractKey) ||
                         (salesContractKey != null && x.SalesContractKey == salesContractKey)))
            .ToListAsync();

        foreach (var document in documents)
            Cancel(document, reason, userName);
    }

    private static void Cancel(Domain.Entities.FinancialDocument document, string reason, string userName)
    {
        document.Status = FinancialDocumentStatus.Canceled;
        document.CancellationReason = reason;
        document.CanceledAt = DateTime.Now;
        document.CanceledBy = userName;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;
    }
}
```

- [ ] **Step 7: Registrar no DI e rodar a suíte**

Em `ServiceCollectionExtensions.cs`, no bloco `// financials`:

```csharp
        services.AddScoped<FinancialDocumentsGenerateService>();
        services.AddScoped<FinancialDocumentsCancelService>();
        services.AddScoped<FinancialDocumentChangeLogService>();
```

Run: `dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests`
Expected: suíte verde.

- [ ] **Step 8: Stage**

```bash
git add SiagroB1.Application/Services/Financials/ SiagroB1.Application.Tests/Financials/ \
        SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs
```

---

## Task 6: Ganchos de geração nos quatro serviços de aprovação

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsApprovalService.cs`
- Modify: `SiagroB1.Application/Services/SalesContracts/SalesContractsApprovalService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsApprovalService.cs`
- Modify: `SiagroB1.Application/Services/SalesContracts/SalesContractsPriceFixationsApprovalService.cs`

**Interfaces:**
- Consumes: `FinancialDocumentsGenerateService.EnqueueForPurchaseFixationAsync` / `EnqueueForSalesFixationAsync` (Task 5).

- [ ] **Step 1: Confirmar que os quatro alvos ainda são estes**

Run:

```bash
grep -rn "ContractStatus.Approved;\|PriceFixationStatus.Confirmed;" SiagroB1.Application/Services/
```

Expected: exatamente os quatro arquivos listados acima. Se aparecer outro, ele também precisa do gancho.

- [ ] **Step 2: Enganchar a aprovação do contrato de compra**

Em `PurchaseContractsApprovalService.cs`: injetar o gerador, **acrescentar o `Include` das fixações** (hoje a query é `FirstOrDefaultAsync` puro e `PriceFixations` viria vazia) e enfileirar antes do `SaveChangesAsync`:

```csharp
public class PurchaseContractsApprovalService(
    AppDbContext context,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsGenerateService financialDocuments)
{
    public async Task ExecuteAsync(Guid key, string? comments, string approvedBy)
    {
        var contract = await context.PurchaseContracts
            .Include(x => x.PriceFixations)
            .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.InApproval)  ??
                       throw new NotFoundException($"Contract with key {key} not found or not draft.");

        contract.Status = ContractStatus.Approved;
        contract.ApprovalComments = comments;
        contract.ApprovedAt = DateTime.Now;
        contract.ApprovedBy = approvedBy;

        notificationOutbox.Register(contract, NotificationEventType.Approved, approvedBy);

        // Um documento financeiro provisório por fixação CONFIRMADA. Contrato de preço fixo já
        // nasce com uma; contrato a fixar não tem nenhuma aqui e recebe a sua quando cada
        // fixação for confirmada. Sem if de tipo, de propósito.
        // Enfileirado ANTES do SaveChanges: o documento e o status do contrato são atômicos.
        foreach (var fixation in contract.PriceFixations
                     .Where(f => f.Status == PriceFixationStatus.Confirmed))
        {
            await financialDocuments.EnqueueForPurchaseFixationAsync(contract, fixation, approvedBy);
        }

        await context.SaveChangesAsync();
    }
}
```

Adicionar `using SiagroB1.Application.Services.Financials;` no topo.

- [ ] **Step 3: Enganchar a aprovação do contrato de venda**

Mesma alteração em `SalesContractsApprovalService.cs`, com `EnqueueForSalesFixationAsync`:

```csharp
public class SalesContractsApprovalService(
    AppDbContext context,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsGenerateService financialDocuments)
{
    public async Task ExecuteAsync(Guid key, string? comments, string approvedBy)
    {
        var contract = await context.SalesContracts
            .Include(x => x.PriceFixations)
            .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.InApproval)  ??
                       throw new NotFoundException($"Contract with key {key} not found or not draft.");

        contract.Status = ContractStatus.Approved;
        contract.ApprovalComments = comments;
        contract.ApprovedAt = DateTime.Now;
        contract.ApprovedBy = approvedBy;

        notificationOutbox.Register(contract, NotificationEventType.Approved, approvedBy);

        foreach (var fixation in contract.PriceFixations
                     .Where(f => f.Status == PriceFixationStatus.Confirmed))
        {
            await financialDocuments.EnqueueForSalesFixationAsync(contract, fixation, approvedBy);
        }

        await context.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Enganchar a confirmação de fixação de compra**

Em `PurchaseContractsPriceFixationsApprovalService.cs`, injetar o gerador e enfileirar **depois** de `fixation.Status = Confirmed` e **antes** do primeiro `SaveChangesAsync`. A chamada tem de ficar **acima** do `BeginTransactionAsync`? Não: o `Enqueue` só toca o `ChangeTracker`, mas ele **busca o número por Dapper**, em conexão separada. Para não segurar `UPDLOCK` dentro da transação do EF, mova a linha `await using var transaction = ...` para **depois** do enqueue:

```csharp
        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException(
                "Contrato precisa estar aprovado para movimentar fixações. " +
                "Reabra o contrato antes de aprovar a fixação.");

        fixation.Status = PriceFixationStatus.Confirmed;

        // ANTES de abrir a transação: o gerador busca o número em DOC_NUMBERS por Dapper, numa
        // conexão que NÃO participa da transação do EF; feito lá dentro, o UPDLOCK ficaria
        // preso e serializaria a criação de documento no sistema inteiro.
        await financialDocuments.EnqueueForPurchaseFixationAsync(contract, fixation, approvedBy);

        await using var transaction = await context.Database.BeginTransactionAsync();

        var previous = ContractChangeLogFields.DescribePriceFixation(
            fixation.FixationVolume, fixation.FixationPrice, PriceFixationStatus.InApproval,
            contract.UnitOfMeasureCode);
```

O restante do método fica como está. **Cuidado:** `previous` era calculado antes de mudar o status; ao mover `fixation.Status = Confirmed` para cima, passe `PriceFixationStatus.InApproval` explicitamente ao `DescribePriceFixation` do valor anterior, como no trecho acima — senão o log passa a registrar "Confirmed → Confirmed".

- [ ] **Step 5: Enganchar a confirmação de fixação de venda**

Mesma alteração em `SalesContractsPriceFixationsApprovalService.cs`, com `EnqueueForSalesFixationAsync`.

- [ ] **Step 6: Compilar e rodar a suíte**

Run: `dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests`
Expected: build limpo, suíte verde. Se algum teste de aprovação de contrato quebrar por falta de argumento no construtor, passe `new FinancialDocumentsGenerateService(...)` com os mesmos fakes da Task 5.

- [ ] **Step 7: Verificar pelo navegador**

Subir a stack (`SiagroB1.Web` e `SiagroB1.Gateway` no profile `yktb`; frontend com `yarn start:dev`), login `admin` / `1234`:

1. Criar contrato de compra de preço fixo **sem** previsão de pagamento → enviar para aprovação → aprovar. **Esperado:** recusa com "Informe a previsão de pagamento do contrato para gerar o título."
2. Preencher a previsão, aprovar. Conferir no banco:
   `SELECT Code, Direction, Nature, Status, NetAmount, DueDate, OriginKey FROM FINANCIAL_DOCUMENTS`
   → 1 linha, `Direction = 1` (Payable), `Nature = 0`, `NetAmount = TotalVolume * StandardPrice`, `OriginKey` = chave da auto-fixação.
3. Clicar "Aprovar" duas vezes em sequência num segundo contrato → **1** documento, não 2.
4. Contrato a fixar: aprovar → **0** documentos; confirmar duas fixações → 2 documentos com os vencimentos das fixações.

- [ ] **Step 8: Stage**

```bash
git add SiagroB1.Application/Services/PurchaseContracts/ SiagroB1.Application/Services/SalesContracts/
```

---

## Task 7: Ganchos de desfazimento

**Files:**
- Modify: `PurchaseContractsPriceFixationsCancelService.cs`, `SalesContractsPriceFixationsCancelService.cs`
- Modify: `PurchaseContractsCancelService.cs`, `SalesContractsCancelService.cs`
- Modify: `PurchaseContractsCloseService.cs`, `SalesContractsCloseService.cs`
- Modify: `PurchaseContractsReopenService.cs`, `SalesContractsReopenService.cs`
- Modify: `SiagroB1.Infra/Interceptors/FinishedContractMutationGuardInterceptor.cs` (só um comentário)

**Interfaces:**
- Consumes: `FinancialDocumentsCancelService.EnqueueCancelByOriginAsync` / `EnqueueCancelByContractAsync` e `FinancialDocumentsGenerateService.EnqueueForPurchaseFixationAsync` / `EnqueueForSalesFixationAsync` (Task 5).

- [ ] **Step 1: Enganchar o estorno de fixação**

Em `PurchaseContractsPriceFixationsCancelService.cs`, injetar `FinancialDocumentsCancelService` e, junto da linha que faz `fixation.Status = PriceFixationStatus.InApproval`, antes do `SaveChanges`:

```csharp
        // O estorno devolve a fixação para InApproval; o compromisso financeiro que ela criou
        // deixa de existir. Reaprovar depois gera um documento NOVO, com o valor novo —
        // funciona porque cancelamos (Status = Canceled) em vez de apagar, e o índice único
        // filtrado ignora cancelados.
        await financialDocuments.EnqueueCancelByOriginAsync(
            FinancialDocumentOrigin.PurchaseContractPriceFixation,
            fixation.Key,
            "Fixação de preço estornada",
            canceledBy);
```

Repetir em `SalesContractsPriceFixationsCancelService.cs` com `SalesContractPriceFixation`.

**Não** engancha em `PriceFixationsRejectService`: ele exige `InApproval` e nunca vê uma fixação com documento. Enganchar ali é código morto que deixaria o provisório órfão.

- [ ] **Step 2: Enganchar o cancelamento de contrato**

Em `PurchaseContractsCancelService.cs`, antes do `SaveChanges`:

```csharp
        await financialDocuments.EnqueueCancelByContractAsync(
            purchaseContractKey: contract.Key,
            salesContractKey: null,
            reason: "Contrato cancelado",
            userName: canceledBy);
```

E o espelho em `SalesContractsCancelService.cs`, com `salesContractKey: contract.Key`.

- [ ] **Step 3: Enganchar o encerramento de contrato**

Em `PurchaseContractsCloseService.cs`, antes do `SaveChanges`:

```csharp
        // Encerrar significa que não haverá mais entrega — e o provisório significa "saldo a
        // faturar". O que sobra nunca será faturado; deixá-lo aberto empilharia na tela um
        // "a faturar" permanente que envenena o total.
        await financialDocuments.EnqueueCancelByContractAsync(
            purchaseContractKey: contract.Key,
            salesContractKey: null,
            reason: "Contrato encerrado",
            userName: closedBy);
```

E o espelho em `SalesContractsCloseService.cs`.

- [ ] **Step 4: Enganchar a reabertura**

Em `PurchaseContractsReopenService.cs`, depois de o contrato voltar a `Approved` e antes do `SaveChanges` — carregando as fixações se a query não as trouxer:

```csharp
        // Regenera pelo MESMO gerador idempotente, recomputando o valor do estado atual das
        // fixações. Simétrico com o encerramento, e sem estado novo para manter.
        foreach (var fixation in contract.PriceFixations
                     .Where(f => f.Status == PriceFixationStatus.Confirmed))
        {
            await financialDocuments.EnqueueForPurchaseFixationAsync(contract, fixation, reopenedBy);
        }
```

E o espelho em `SalesContractsReopenService.cs`. Se a query do serviço não tiver `.Include(x => x.PriceFixations)`, acrescente.

- [ ] **Step 5: Documentar o interceptor**

Em `SiagroB1.Infra/Interceptors/FinishedContractMutationGuardInterceptor.cs`, dentro de `CollectContractKeys`, acima do `switch`:

```csharp
        // ⚠️ NÃO acrescente FinancialDocument a este switch, por mais que pareça "seguir o
        // padrão". O encerramento do contrato cancela o provisório no MESMO SaveChanges em que
        // grava Finished; incluído aqui, o próprio encerramento passaria a lançar
        // "Contrato encerrado: não é possível alterar dados vinculados ao contrato.", sem saída
        // pela tela — e qualquer baixa de documento de contrato encerrado também.
```

- [ ] **Step 6: Compilar e rodar a suíte**

Run: `dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests`
Expected: verde.

- [ ] **Step 7: Verificar o ciclo completo pelo navegador**

Com a stack de pé, num contrato a fixar:

1. Confirmar uma fixação → documento nasce.
2. Estornar a fixação → documento fica `Canceled` (`Status = 3`).
3. Reaprovar a fixação → documento **novo**, `Status = 0`; o antigo continua cancelado.
4. Encerrar o contrato → provisórios abertos ficam `Canceled`, **sem** lançar "Contrato encerrado…".
5. Reabrir o contrato → provisórios regenerados.

- [ ] **Step 8: Stage**

```bash
git add SiagroB1.Application/Services/PurchaseContracts/ \
        SiagroB1.Application/Services/SalesContracts/ \
        SiagroB1.Infra/Interceptors/FinishedContractMutationGuardInterceptor.cs
```

---

## Task 8: Baixa e estorno

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsSettlementGuardService.cs`, `FinancialDocumentsSettleService.cs`, `FinancialDocumentsReverseSettlementService.cs`
- Create: `SiagroB1.Web/Actions/Financials/FinancialDocumentsSettleController.cs`, `FinancialDocumentsReverseSettlementController.cs`, `FinancialDocumentsCancelController.cs`, `FinancialDocumentsRecalculateBalanceController.cs`
- Create: `SiagroB1.Application.Tests/Financials/FinancialDocumentsSettleServiceTests.cs`
- Modify: `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: `FinancialDocumentsRecalculateBalanceService.RecalculateAsync` (Task 4), `FinancialAccountsGetService.GetByIdAsync` (Task 3).
- Produces:
  - `Task<FinancialSettlement> FinancialDocumentsSettleService.ExecuteAsync(Guid documentKey, string financialAccountCode, decimal amount, DateTime settlementDate, decimal interest, decimal fine, decimal discount, string? documentReference, string? notes, string userName, CommitMode commitMode = CommitMode.Auto)`
  - `Task FinancialDocumentsReverseSettlementService.ExecuteAsync(Guid settlementKey, string reason, string userName, CommitMode commitMode = CommitMode.Auto)`

- [ ] **Step 1: Escrever o teste que falha**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsSettleServiceTests.cs`:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsSettleServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsSettleService Service() => new(_db);
    private FinancialDocumentsReverseSettlementService Reverser() => new(_db);

    private async Task<FinancialDocument> SeedAsync(
        FinancialDocumentNature nature = FinancialDocumentNature.Advance,
        decimal netAmount = 1000m,
        CurrencyType currency = CurrencyType.Brl,
        FinancialDocumentStatus status = FinancialDocumentStatus.Open)
    {
        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl
        });

        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FN000001",
            CardCode = "F0001",
            Direction = FinancialDirection.Payable,
            Nature = nature,
            Status = status,
            DueDate = DateTime.Today.AddDays(30),
            NetAmount = netAmount,
            Currency = currency
        };

        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();
        return document;
    }

    [Fact]
    public async Task Settling_part_of_an_advance_leaves_it_partially_settled()
    {
        var document = await SeedAsync();

        await Service().ExecuteAsync(document.Key, "CX01", 400m, DateTime.Today,
            0m, 0m, 0m, "OP-1", null, "tester");

        Assert.Equal(400m, document.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, document.Status);
        Assert.Equal(400m, document.AvailableAdvanceAmount);
    }

    [Fact]
    public async Task Refuses_to_settle_a_provisional_document()
    {
        var document = await SeedAsync(nature: FinancialDocumentNature.Provisional);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", 100m, DateTime.Today,
                0m, 0m, 0m, null, null, "tester"));

        Assert.Contains("provisório", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_to_settle_more_than_the_outstanding_amount()
    {
        var document = await SeedAsync(netAmount: 1000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", 1500m, DateTime.Today,
                0m, 0m, 0m, null, null, "tester"));

        Assert.Contains("saldo", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_an_account_in_a_different_currency()
    {
        var document = await SeedAsync(currency: CurrencyType.Usd);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", 100m, DateTime.Today,
                0m, 0m, 0m, null, null, "tester"));

        Assert.Contains("moeda", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_to_settle_a_canceled_document()
    {
        var document = await SeedAsync(status: FinancialDocumentStatus.Canceled);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", 100m, DateTime.Today,
                0m, 0m, 0m, null, null, "tester"));

        Assert.Contains("cancelado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Interest_and_fine_do_not_change_what_the_document_owes()
    {
        var document = await SeedAsync(netAmount: 1000m);

        await Service().ExecuteAsync(document.Key, "CX01", 1000m, DateTime.Today,
            interest: 50m, fine: 20m, discount: 0m, "OP-2", null, "tester");

        Assert.Equal(1000m, document.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, document.Status);
    }

    [Fact]
    public async Task Reversing_a_settlement_writes_a_negative_line_and_reopens_the_document()
    {
        var document = await SeedAsync();
        var settlement = await Service().ExecuteAsync(document.Key, "CX01", 1000m, DateTime.Today,
            0m, 0m, 0m, null, null, "tester");

        await Reverser().ExecuteAsync(settlement.Key, "Pagamento indevido", "tester");

        Assert.Equal(0m, document.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, document.Status);
        Assert.Equal(2, _db.Context.FinancialSettlements.Count());
    }

    [Fact]
    public async Task The_same_settlement_cannot_be_reversed_twice()
    {
        var document = await SeedAsync();
        var settlement = await Service().ExecuteAsync(document.Key, "CX01", 1000m, DateTime.Today,
            0m, 0m, 0m, null, null, "tester");

        await Reverser().ExecuteAsync(settlement.Key, "Pagamento indevido", "tester");

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Reverser().ExecuteAsync(settlement.Key, "De novo", "tester"));

        Assert.Contains("estornada", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialDocumentsSettleServiceTests`
Expected: falha de compilação.

- [ ] **Step 3: Implementar o guard**

`FinancialDocumentsSettlementGuardService.cs` — estático e sem estado, como `SalesContractsPostApprovalGuard`, para que a regra de "pode baixar?" exista em **um** lugar:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Regra ÚNICA de "este documento pode ser baixado?". Estático e sem estado de propósito: a
/// baixa, o estorno e (na Fase 4) o pagamento em lote têm de recusar exatamente pelos mesmos
/// motivos.
/// </summary>
public static class FinancialDocumentsSettlementGuardService
{
    public static void EnsureCanSettle(
        FinancialDocument document, FinancialAccount? account, decimal amount)
    {
        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} está cancelado.");

        if (document.IsBlockedForSettlement)
            throw new ApplicationException(
                $"O documento {document.Code} é provisório e não pode ser baixado. " +
                "Ele será liberado quando o documento fiscal for confirmado.");

        if (account is null)
            throw new ApplicationException("Informe a conta financeira da baixa.");

        if (account.Inactive)
            throw new ApplicationException($"A conta financeira {account.Code} está inativa.");

        if (account.Currency != document.Currency)
            throw new ApplicationException(
                $"A conta {account.Code} é em {account.Currency} e o documento em " +
                $"{document.Currency}: a moeda precisa ser a mesma.");

        if (amount <= 0m)
            throw new ApplicationException("O valor da baixa deve ser maior que zero.");

        if (amount > document.OpenAmount)
            throw new ApplicationException(
                $"O valor da baixa ({amount:N2}) excede o saldo em aberto do documento " +
                $"({document.OpenAmount:N2}).");
    }
}
```

- [ ] **Step 4: Implementar a baixa**

`FinancialDocumentsSettleService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Grava UMA linha do ledger de baixas e recalcula o saldo do documento.
///
/// Aceita CommitMode porque é ponto de entrada de tela HOJE e será composto pelo pagamento em
/// lote da Fase 4: chamado em Auto de dentro de uma transação alheia, o CommitAsync do
/// UnitOfWork comitaria a transação DO CHAMADOR — ele comita e zera _transaction
/// incondicionalmente.
/// </summary>
public class FinancialDocumentsSettleService(IUnitOfWork db)
{
    public async Task<FinancialSettlement> ExecuteAsync(
        Guid documentKey,
        string financialAccountCode,
        decimal amount,
        DateTime settlementDate,
        decimal interest,
        decimal fine,
        decimal discount,
        string? documentReference,
        string? notes,
        string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        var document = await db.Context.FinancialDocuments
                           .FirstOrDefaultAsync(x => x.Key == documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        var account = await db.Context.FinancialAccounts
            .FirstOrDefaultAsync(x => x.Code == financialAccountCode);

        // Validação ANTES da transação.
        FinancialDocumentsSettlementGuardService.EnsureCanSettle(document, account, amount);

        var settlement = new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = documentKey,
            FinancialAccountCode = financialAccountCode,
            SettlementDate = settlementDate,
            Amount = decimal.Round(amount, 2, MidpointRounding.ToEven),
            InterestAmount = decimal.Round(interest, 2, MidpointRounding.ToEven),
            FineAmount = decimal.Round(fine, 2, MidpointRounding.ToEven),
            DiscountAmount = decimal.Round(discount, 2, MidpointRounding.ToEven),
            Origin = FinancialSettlementOrigin.Manual,
            DocumentReference = documentReference,
            Notes = notes,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialSettlements.Add(settlement);
            await db.Context.SaveChangesAsync();

            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, documentKey);
            await db.Context.SaveChangesAsync();

            if (commitMode == CommitMode.Auto) await db.CommitAsync();
        }
        catch
        {
            if (commitMode == CommitMode.Auto) await db.RollbackAsync();
            throw;
        }

        return settlement;
    }
}
```

- [ ] **Step 5: Implementar o estorno**

`FinancialDocumentsReverseSettlementService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Estorna uma baixa gravando a linha NEGATIVA espelho. O ledger é insert-only — é dinheiro,
/// nunca se apaga. O índice único filtrado em ReversedSettlementKey impede estornar a mesma
/// baixa duas vezes; a consulta abaixo é a camada amigável do mesmo impedimento.
/// </summary>
public class FinancialDocumentsReverseSettlementService(IUnitOfWork db)
{
    public async Task ExecuteAsync(
        Guid settlementKey, string reason, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo do estorno.");

        var settlement = await db.Context.FinancialSettlements
                             .FirstOrDefaultAsync(x => x.Key == settlementKey)
                         ?? throw new NotFoundException("Baixa não encontrada.");

        if (settlement.Origin == FinancialSettlementOrigin.Reversal)
            throw new ApplicationException("Não é possível estornar um estorno.");

        if (await db.Context.FinancialSettlements.AnyAsync(x => x.ReversedSettlementKey == settlementKey))
            throw new ApplicationException("Esta baixa já foi estornada.");

        var reversal = new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = settlement.FinancialDocumentKey,
            FinancialAccountCode = settlement.FinancialAccountCode,
            SettlementDate = DateTime.Today,
            Amount = -settlement.Amount,
            InterestAmount = -settlement.InterestAmount,
            FineAmount = -settlement.FineAmount,
            DiscountAmount = -settlement.DiscountAmount,
            Origin = FinancialSettlementOrigin.Reversal,
            ReversedSettlementKey = settlementKey,
            DocumentReference = settlement.DocumentReference,
            Notes = reason,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialSettlements.Add(reversal);
            await db.Context.SaveChangesAsync();

            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(
                db.Context, settlement.FinancialDocumentKey);
            await db.Context.SaveChangesAsync();

            if (commitMode == CommitMode.Auto) await db.CommitAsync();
        }
        catch
        {
            if (commitMode == CommitMode.Auto) await db.RollbackAsync();
            throw;
        }
    }
}
```

- [ ] **Step 6: Rodar e confirmar que passa**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialDocumentsSettleServiceTests`
Expected: 8 testes passando.

- [ ] **Step 7: Criar as actions**

`SiagroB1.Web/Actions/Financials/FinancialDocumentsSettleController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialDocumentsSettleController(FinancialDocumentsSettleService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsSettle")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // ODataActionParameters chega NULL quando o corpo não casa com nenhum parâmetro do EDM.
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o documento financeiro.");

            if (!parameters.TryGetValue("FinancialAccountCode", out var accountObj))
                return BadRequest("Informe a conta financeira.");

            if (!parameters.TryGetValue("Amount", out var amountObj) || amountObj is null)
                return BadRequest("Informe o valor da baixa.");

            if (!parameters.TryGetValue("SettlementDate", out var dateObj) || dateObj is null)
                return BadRequest("Informe a data da baixa.");

            await service.ExecuteAsync(
                documentKey: Guid.Parse(keyObj.ToString()!),
                financialAccountCode: accountObj?.ToString() ?? string.Empty,
                amount: Convert.ToDecimal(amountObj),
                settlementDate: DateTime.Parse(dateObj.ToString()!),
                interest: Money(parameters, "InterestAmount"),
                fine: Money(parameters, "FineAmount"),
                discount: Money(parameters, "DiscountAmount"),
                documentReference: Text(parameters, "DocumentReference"),
                notes: Text(parameters, "Notes"),
                userName: User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }

    // TryGetValue devolve TRUE com valor null em parâmetro opcional: sempre value?.ToString().
    private static string? Text(ODataActionParameters p, string name) =>
        p.TryGetValue(name, out var v) ? v?.ToString() : null;

    private static decimal Money(ODataActionParameters p, string name) =>
        p.TryGetValue(name, out var v) && v is not null ? Convert.ToDecimal(v) : 0m;
}
```

`FinancialDocumentsReverseSettlementController.cs` — repare que o parâmetro é a chave da **baixa**, não a do documento:

```csharp
// FinancialDocumentsReverseSettlementController
[HttpPost("odata/FinancialDocumentsReverseSettlement")]
public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
{
    if (!ModelState.IsValid) return BadRequest(ModelState);
    if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

    try
    {
        if (!parameters.TryGetValue("SettlementKey", out var keyObj) || keyObj is null)
            return BadRequest("Informe a baixa a estornar.");

        parameters.TryGetValue("Reason", out var reasonObj);

        await service.ExecuteAsync(
            Guid.Parse(keyObj.ToString()!),
            reasonObj?.ToString() ?? string.Empty,
            User.Identity?.Name ?? "Unknown");

        return Ok();
    }
    catch (Exception e)
    {
        if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
        return BadRequest(e.Message);
    }
}
```

`FinancialDocumentsCancelController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialDocumentsCancelController(FinancialDocumentsCancelService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsCancel")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o documento financeiro.");

            parameters.TryGetValue("Reason", out var reasonObj);

            await service.ExecuteAsync(
                Guid.Parse(keyObj.ToString()!),
                reasonObj?.ToString() ?? string.Empty,
                User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

`FinancialDocumentsRecalculateBalanceController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialDocumentsRecalculateBalanceController(
    FinancialDocumentsRecalculateBalanceService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsRecalculateBalance")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o documento financeiro.");

            return Ok(await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!)));
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

- [ ] **Step 8: Registrar as actions no EDM e o DI**

Em `ODataConfigurations.cs`:

```csharp
        // ⚠️ Edm.Double, NUNCA Edm.Decimal: o UI5 v4 serializa decimal como STRING e o backend
        // devolve 400 sem nomear o campo. Data e enum vão como string, pelo mesmo motivo.
        var financialSettle = modelBuilder.Action("FinancialDocumentsSettle");
        financialSettle.Parameter<Guid>("Key");
        financialSettle.Parameter<string>("FinancialAccountCode");
        financialSettle.Parameter<double>("Amount");
        financialSettle.Parameter<string>("SettlementDate");
        financialSettle.Parameter<double>("InterestAmount").Optional();
        financialSettle.Parameter<double>("FineAmount").Optional();
        financialSettle.Parameter<double>("DiscountAmount").Optional();
        financialSettle.Parameter<string>("DocumentReference").Optional();
        financialSettle.Parameter<string>("Notes").Optional();
        financialSettle.Returns<IActionResult>();

        var financialReverse = modelBuilder.Action("FinancialDocumentsReverseSettlement");
        financialReverse.Parameter<Guid>("SettlementKey");
        financialReverse.Parameter<string>("Reason");
        financialReverse.Returns<IActionResult>();

        var financialCancel = modelBuilder.Action("FinancialDocumentsCancel");
        financialCancel.Parameter<Guid>("Key");
        financialCancel.Parameter<string>("Reason");
        financialCancel.Returns<IActionResult>();

        var financialRecalc = modelBuilder.Action("FinancialDocumentsRecalculateBalance");
        financialRecalc.Parameter<Guid>("Key");
        financialRecalc.Returns<FinancialDocumentRecalcResultDto>();
```

Em `ServiceCollectionExtensions.cs`:

```csharp
        services.AddScoped<FinancialDocumentsSettleService>();
        services.AddScoped<FinancialDocumentsReverseSettlementService>();
```

- [ ] **Step 9: Acrescentar o teste de EDM das actions**

Em `FinancialEdmModelTests.cs`:

```csharp
    [Theory]
    [InlineData("FinancialDocumentsSettle", "Amount")]
    [InlineData("FinancialDocumentsSettle", "InterestAmount")]
    public void Money_parameters_are_double_never_decimal(string action, string parameter)
    {
        var operation = BuildModel().SchemaElements
            .OfType<Microsoft.OData.Edm.IEdmAction>()
            .Single(a => a.Name == action);

        var type = operation.Parameters.Single(p => p.Name == parameter).Type;

        Assert.Equal("Edm.Double", type.FullName());
    }
```

- [ ] **Step 10: Rodar tudo**

Run: `dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests`
Expected: verde.

- [ ] **Step 11: Stage**

```bash
git add SiagroB1.Application/Services/Financials/ SiagroB1.Application.Tests/Financials/ SiagroB1.Web/
```

---

## Task 9: Adiantamento a contrato

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialAdvancesCreateService.cs`
- Create: `SiagroB1.Web/Actions/Financials/FinancialAdvancesCreateController.cs`
- Create: `SiagroB1.Application.Tests/Financials/FinancialAdvancesCreateServiceTests.cs`
- Modify: `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: Task 1, `DocNumberSequenceService`, `IBusinessPartnerService`.
- Produces: `Task<FinancialDocument> FinancialAdvancesCreateService.ExecuteAsync(string contractType, Guid contractKey, decimal amount, DateTime dueDate, string? comments, string userName, CommitMode commitMode = CommitMode.Auto)`

- [ ] **Step 1: Escrever o teste que falha**

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialAdvancesCreateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAdvancesCreateService Service() => new(
        _db,
        new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" }));

    private async Task<PurchaseContract> SeedPurchaseContractAsync(
        ContractStatus status = ContractStatus.Approved)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC0001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            BranchCode = "01",
            TotalVolume = 1000m,
            StandardPrice = 100m,
            StandardCurrency = CurrencyType.Brl,
            Type = ContractType.Fixed,
            Status = status
        };

        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task An_advance_on_a_purchase_contract_is_payable_and_not_blocked()
    {
        var contract = await SeedPurchaseContractAsync();

        var advance = await Service().ExecuteAsync(
            "Purchase", contract.Key, 5000m, DateTime.Today.AddDays(10), null, "tester");

        Assert.Equal(FinancialDirection.Payable, advance.Direction);
        Assert.Equal(FinancialDocumentNature.Advance, advance.Nature);
        Assert.Equal(FinancialDocumentOrigin.Manual, advance.OriginType);
        Assert.False(advance.IsBlockedForSettlement);
        Assert.Equal(5000m, advance.NetAmount);
        Assert.Equal("PRODUTOR TESTE", advance.CardName);
        Assert.Equal(contract.Key, advance.PurchaseContractKey);
    }

    [Fact]
    public async Task Two_advances_on_the_same_contract_are_both_accepted()
    {
        var contract = await SeedPurchaseContractAsync();
        var service = Service();

        await service.ExecuteAsync("Purchase", contract.Key, 1000m, DateTime.Today, null, "tester");
        await service.ExecuteAsync("Purchase", contract.Key, 2000m, DateTime.Today, null, "tester");

        Assert.Equal(2, _db.Context.FinancialDocuments.Count());
    }

    [Fact]
    public async Task Refuses_an_advance_on_a_contract_that_is_not_approved()
    {
        var contract = await SeedPurchaseContractAsync(ContractStatus.Draft);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync("Purchase", contract.Key, 1000m, DateTime.Today, null, "tester"));

        Assert.Contains("aprovado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_non_positive_amount()
    {
        var contract = await SeedPurchaseContractAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync("Purchase", contract.Key, 0m, DateTime.Today, null, "tester"));

        Assert.Contains("valor", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialAdvancesCreateServiceTests`
Expected: falha de compilação.

- [ ] **Step 3: Implementar**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Adiantamento a contrato: o único documento LIQUIDÁVEL da Fase 1.
///
/// Direção segue o lado do contrato — adiantamos ao produtor (compra → a pagar) ou recebemos do
/// cliente (venda → a receber). Valor e vencimento são informados pelo usuário; parceiro, moeda
/// e filial vêm do contrato.
///
/// Dois adiantamentos no mesmo contrato são legítimos, e o índice único de idempotência não os
/// barra porque filtra por Nature = Provisional.
///
/// FASE 2: a amortização contra o documento firme entra como linha de ledger com origem
/// AdvanceApplication, e AvailableAdvanceAmount passa a descontar o que já foi aplicado.
/// </summary>
public class FinancialAdvancesCreateService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    IBusinessPartnerService businessPartnerService)
{
    public async Task<FinancialDocument> ExecuteAsync(
        string contractType,
        Guid contractKey,
        decimal amount,
        DateTime dueDate,
        string? comments,
        string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        if (amount <= 0m)
            throw new ApplicationException("O valor do adiantamento deve ser maior que zero.");

        var isPurchase = string.Equals(contractType, "Purchase", StringComparison.OrdinalIgnoreCase);
        var isSales = string.Equals(contractType, "Sales", StringComparison.OrdinalIgnoreCase);

        if (!isPurchase && !isSales)
            throw new ApplicationException("Tipo de contrato inválido. Informe Purchase ou Sales.");

        string cardCode, contractCode, branchCode, paymentTerms;
        CurrencyType currency;

        if (isPurchase)
        {
            var contract = await db.Context.PurchaseContracts.AsNoTracking()
                               .FirstOrDefaultAsync(x => x.Key == contractKey)
                           ?? throw new NotFoundException("Contrato de compra não encontrado.");

            if (contract.Status != ContractStatus.Approved)
                throw new ApplicationException(
                    "O contrato precisa estar aprovado para receber adiantamento.");

            cardCode = contract.CardCode;
            contractCode = contract.Code ?? string.Empty;
            branchCode = contract.BranchCode ?? string.Empty;
            currency = contract.StandardCurrency ?? CurrencyType.Brl;
            paymentTerms = contract.PaymentTerms ?? string.Empty;
        }
        else
        {
            var contract = await db.Context.SalesContracts.AsNoTracking()
                               .FirstOrDefaultAsync(x => x.Key == contractKey)
                           ?? throw new NotFoundException("Contrato de venda não encontrado.");

            if (contract.Status != ContractStatus.Approved)
                throw new ApplicationException(
                    "O contrato precisa estar aprovado para receber adiantamento.");

            cardCode = contract.CardCode;
            contractCode = contract.Code ?? string.Empty;
            branchCode = contract.BranchCode ?? string.Empty;
            currency = contract.StandardCurrency ?? CurrencyType.Brl;
            paymentTerms = contract.PaymentTerms ?? string.Empty;
        }

        var partner = await businessPartnerService.GetByIdAsync(cardCode);

        // Número buscado ANTES da transação: Dapper em conexão separada, segurando UPDLOCK.
        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);
        var code = await docNumberSequence.GetDocNumber(docNumberKey);

        var advance = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = code,
            DocNumberKey = docNumberKey,
            BranchCode = branchCode,
            Direction = isPurchase ? FinancialDirection.Payable : FinancialDirection.Receivable,
            Nature = FinancialDocumentNature.Advance,
            Status = FinancialDocumentStatus.Open,
            CardCode = cardCode,
            CardName = partner?.CardName,
            DocumentDate = DateTime.Now,
            DueDate = dueDate,
            Currency = currency,
            NetAmount = decimal.Round(amount, 2, MidpointRounding.ToEven),
            OriginType = FinancialDocumentOrigin.Manual,
            OriginDocNumber = contractCode,
            PurchaseContractKey = isPurchase ? contractKey : null,
            SalesContractKey = isSales ? contractKey : null,
            PaymentTermsText = paymentTerms,
            Comments = comments,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialDocuments.Add(advance);
            await db.Context.SaveChangesAsync();

            if (commitMode == CommitMode.Auto) await db.CommitAsync();
        }
        catch
        {
            if (commitMode == CommitMode.Auto) await db.RollbackAsync();
            throw;
        }

        return advance;
    }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialAdvancesCreateServiceTests`
Expected: 4 testes passando.

- [ ] **Step 5: Criar a action e registrar**

`SiagroB1.Web/Actions/Financials/FinancialAdvancesCreateController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialAdvancesCreateController(FinancialAdvancesCreateService service) : ODataController
{
    [HttpPost("odata/FinancialAdvancesCreate")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("ContractType", out var typeObj) || typeObj is null)
                return BadRequest("Informe o tipo de contrato.");

            if (!parameters.TryGetValue("ContractKey", out var contractObj) || contractObj is null)
                return BadRequest("Informe o contrato.");

            if (!parameters.TryGetValue("Amount", out var amountObj) || amountObj is null)
                return BadRequest("Informe o valor do adiantamento.");

            if (!parameters.TryGetValue("DueDate", out var dueObj) || dueObj is null)
                return BadRequest("Informe o vencimento do adiantamento.");

            parameters.TryGetValue("Comments", out var commentsObj);

            var advance = await service.ExecuteAsync(
                contractType: typeObj.ToString()!,
                contractKey: Guid.Parse(contractObj.ToString()!),
                amount: Convert.ToDecimal(amountObj),
                dueDate: DateTime.Parse(dueObj.ToString()!),
                comments: commentsObj?.ToString(),
                userName: User.Identity?.Name ?? "Unknown");

            return Ok(new { advance.Key, advance.Code });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

Em `ODataConfigurations.cs`:

```csharp
        var financialAdvance = modelBuilder.Action("FinancialAdvancesCreate");
        financialAdvance.Parameter<string>("ContractType");   // "Purchase" | "Sales" — string, nunca enum
        financialAdvance.Parameter<Guid>("ContractKey");
        financialAdvance.Parameter<double>("Amount");
        financialAdvance.Parameter<string>("DueDate");
        financialAdvance.Parameter<string>("Comments").Optional();
        financialAdvance.Returns<IActionResult>();
```

Em `ServiceCollectionExtensions.cs`: `services.AddScoped<FinancialAdvancesCreateService>();`

- [ ] **Step 6: Rodar tudo e stage**

Run: `dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests`

```bash
git add SiagroB1.Application/Services/Financials/ SiagroB1.Application.Tests/Financials/ SiagroB1.Web/
```

---

## Task 10: Totais, consulta por contrato, correção de vencimento e menu

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsGetTotalsService.cs`, `FinancialDocumentsSetDueDateService.cs`, `FinancialDocumentsGetByContractService.cs`
- Create: `SiagroB1.Domain/Dtos/FinancialDocumentTotalsDto.cs`, `FinancialDocumentByContractDto.cs`
- Create: `SiagroB1.Web/Functions/Financials/FinancialDocumentsGetTotalsController.cs`, `FinancialDocumentsGetByContractController.cs`
- Create: `SiagroB1.Web/Actions/Financials/FinancialDocumentsSetDueDateController.cs`
- Create: `SiagroB1.Migrations/CommonContext/<timestamp>_AddFinancialMenus.cs`
- Modify: `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: Tasks 1, 4, 5 (o `FinancialDocumentChangeLogService` é usado aqui).
- Produces: `FinancialDocumentTotalsDto { OpenAmount, OverdueAmount, DueAmount, DocumentCount }`, `FinancialDocumentByContractDto`.

- [ ] **Step 1: Criar os DTOs**

```csharp
using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class FinancialDocumentTotalsDto
{
    [JsonPropertyName("OpenAmount")] public decimal OpenAmount { get; set; }
    [JsonPropertyName("OverdueAmount")] public decimal OverdueAmount { get; set; }
    [JsonPropertyName("DueAmount")] public decimal DueAmount { get; set; }
    [JsonPropertyName("DocumentCount")] public int DocumentCount { get; set; }
}
```

```csharp
using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class FinancialDocumentByContractDto
{
    [JsonPropertyName("Key")] public Guid Key { get; set; }
    [JsonPropertyName("Code")] public string? Code { get; set; }
    [JsonPropertyName("Nature")] public string? Nature { get; set; }
    [JsonPropertyName("Status")] public string? Status { get; set; }
    [JsonPropertyName("DueDate")] public DateTime DueDate { get; set; }
    [JsonPropertyName("NetAmount")] public decimal NetAmount { get; set; }
    [JsonPropertyName("SettledAmount")] public decimal SettledAmount { get; set; }
    [JsonPropertyName("OpenAmount")] public decimal OpenAmount { get; set; }
}
```

- [ ] **Step 2: Implementar os totais**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialDocumentsGetTotalsService(IUnitOfWork db)
{
    public async Task<FinancialDocumentTotalsDto> ExecuteAsync(string direction, string? branchCode)
    {
        if (!Enum.TryParse<FinancialDirection>(direction, ignoreCase: true, out var parsed))
            throw new ApplicationException("Direção inválida. Informe Payable ou Receivable.");

        var today = DateTime.Today;

        var query = db.Context.FinancialDocuments.AsNoTracking()
            .Where(x => x.Direction == parsed && x.Status != FinancialDocumentStatus.Canceled);

        if (!string.IsNullOrWhiteSpace(branchCode))
            query = query.Where(x => x.BranchCode == branchCode);

        var rows = await query
            .Select(x => new { x.NetAmount, x.SettledAmount, x.DueDate })
            .ToListAsync();

        var open = rows.Select(r => new { Open = r.NetAmount - r.SettledAmount, r.DueDate })
                       .Where(r => r.Open > 0)
                       .ToList();

        return new FinancialDocumentTotalsDto
        {
            OpenAmount = decimal.Round(open.Sum(r => r.Open), 2, MidpointRounding.ToEven),
            OverdueAmount = decimal.Round(
                open.Where(r => r.DueDate.Date < today).Sum(r => r.Open), 2, MidpointRounding.ToEven),
            DueAmount = decimal.Round(
                open.Where(r => r.DueDate.Date >= today).Sum(r => r.Open), 2, MidpointRounding.ToEven),
            DocumentCount = open.Count
        };
    }
}
```

- [ ] **Step 3: Implementar a correção de vencimento, com log**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Corrige o vencimento sem reabrir o contrato. É o único campo mutável do documento na Fase 1,
/// e por isso é o primeiro consumidor do log de alterações.
/// </summary>
public class FinancialDocumentsSetDueDateService(
    IUnitOfWork db,
    FinancialDocumentChangeLogService changeLog)
{
    public async Task ExecuteAsync(Guid key, DateTime dueDate, string userName)
    {
        var document = await db.Context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} está cancelado.");

        if (document.DueDate.Date == dueDate.Date) return;

        // Registrado ANTES do SaveChanges, para nascer e morrer com a alteração.
        changeLog.Register(
            key,
            FinancialDocumentChangeLogFields.DueDate,
            document.DueDate.ToString("dd/MM/yyyy"),
            dueDate.ToString("dd/MM/yyyy"),
            userName);

        document.DueDate = dueDate;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 4: Escrever o teste do log**

Acrescentar em `SiagroB1.Application.Tests/Financials/FinancialDocumentsSetDueDateServiceTests.cs`:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsSetDueDateServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsSetDueDateService Service() =>
        new(_db, new FinancialDocumentChangeLogService(_db.Context));

    private async Task<FinancialDocument> SeedAsync()
    {
        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FN000001",
            CardCode = "F0001",
            Direction = FinancialDirection.Payable,
            Nature = FinancialDocumentNature.Provisional,
            DueDate = new DateTime(2026, 1, 31),
            NetAmount = 1000m
        };

        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();
        return document;
    }

    [Fact]
    public async Task Changing_the_due_date_writes_one_log_line_with_both_values()
    {
        var document = await SeedAsync();

        await Service().ExecuteAsync(document.Key, new DateTime(2026, 3, 15), "tester");

        var log = Assert.Single(_db.Context.FinancialDocumentChangeLogs);
        Assert.Equal(FinancialDocumentChangeLogFields.DueDate, log.Field);
        Assert.Equal("31/01/2026", log.OldValue);
        Assert.Equal("15/03/2026", log.NewValue);
        Assert.Equal("tester", log.ChangedBy);
        Assert.Equal(new DateTime(2026, 3, 15), document.DueDate);
    }

    [Fact]
    public async Task Setting_the_same_date_writes_no_log_line()
    {
        var document = await SeedAsync();

        await Service().ExecuteAsync(document.Key, new DateTime(2026, 1, 31), "tester");

        Assert.Empty(_db.Context.FinancialDocumentChangeLogs);
    }
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialDocumentsSetDueDateServiceTests`
Expected: 2 testes passando.

- [ ] **Step 5: Criar as functions e a action**

`FinancialDocumentsGetTotalsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.Financials;

public class FinancialDocumentsGetTotalsController(FinancialDocumentsGetTotalsService service)
    : ODataController
{
    // Rota literal declarada à mão: a forma não declarada toma 404.
    [EnableQuery]
    [HttpGet("odata/FinancialDocumentsGetTotals(Direction={direction},BranchCode={branchCode})")]
    public async Task<ActionResult<FinancialDocumentTotalsDto>> Get(
        [FromRoute] string direction, [FromRoute] string? branchCode) =>
        Ok(await service.ExecuteAsync(direction, branchCode));
}
```

`FinancialDocumentsGetByContractService.cs` — a aba Financeiro do contrato:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialDocumentsGetByContractService(IUnitOfWork db)
{
    public async Task<IEnumerable<FinancialDocumentByContractDto>> ExecuteAsync(
        string contractType, Guid contractKey)
    {
        var isPurchase = string.Equals(contractType, "Purchase", StringComparison.OrdinalIgnoreCase);
        var isSales = string.Equals(contractType, "Sales", StringComparison.OrdinalIgnoreCase);

        if (!isPurchase && !isSales)
            throw new ApplicationException("Tipo de contrato inválido. Informe Purchase ou Sales.");

        var documents = await db.Context.FinancialDocuments.AsNoTracking()
            .Where(x => isPurchase
                ? x.PurchaseContractKey == contractKey
                : x.SalesContractKey == contractKey)
            .OrderBy(x => x.DueDate)
            .ToListAsync();

        // Projetado em memória porque OpenAmount é [NotMapped] e o EF não a traduz.
        return documents.Select(x => new FinancialDocumentByContractDto
        {
            Key = x.Key,
            Code = x.Code,
            Nature = x.Nature.ToString(),
            Status = x.Status.ToString(),
            DueDate = x.DueDate,
            NetAmount = x.NetAmount,
            SettledAmount = x.SettledAmount,
            OpenAmount = x.OpenAmount
        }).ToList();
    }
}
```

`FinancialDocumentsGetByContractController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.Financials;

public class FinancialDocumentsGetByContractController(
    FinancialDocumentsGetByContractService service) : ODataController
{
    [EnableQuery]
    [HttpGet("odata/FinancialDocumentsGetByContract(ContractType={contractType},ContractKey={contractKey})")]
    public async Task<ActionResult<IEnumerable<FinancialDocumentByContractDto>>> Get(
        [FromRoute] string contractType, [FromRoute] Guid contractKey) =>
        Ok(await service.ExecuteAsync(contractType, contractKey));
}
```

`FinancialDocumentsSetDueDateController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialDocumentsSetDueDateController(
    FinancialDocumentsSetDueDateService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsSetDueDate")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o documento financeiro.");

            if (!parameters.TryGetValue("DueDate", out var dueObj) || dueObj is null)
                return BadRequest("Informe o novo vencimento.");

            await service.ExecuteAsync(
                Guid.Parse(keyObj.ToString()!),
                DateTime.Parse(dueObj.ToString()!),
                User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

Em `ODataConfigurations.cs`:

```csharp
        var financialSetDueDate = modelBuilder.Action("FinancialDocumentsSetDueDate");
        financialSetDueDate.Parameter<Guid>("Key");
        financialSetDueDate.Parameter<string>("DueDate");
        financialSetDueDate.Returns<IActionResult>();

        var financialTotals = modelBuilder.Function("FinancialDocumentsGetTotals");
        financialTotals.Parameter<string>("Direction");
        financialTotals.Parameter<string>("BranchCode").Optional();
        financialTotals.Returns<FinancialDocumentTotalsDto>();

        var financialByContract = modelBuilder.Function("FinancialDocumentsGetByContract");
        financialByContract.Parameter<string>("ContractType");
        financialByContract.Parameter<Guid>("ContractKey");
        financialByContract.ReturnsCollection<FinancialDocumentByContractDto>();
```

Em `ServiceCollectionExtensions.cs`:

```csharp
        services.AddScoped<FinancialDocumentsGetTotalsService>();
        services.AddScoped<FinancialDocumentsSetDueDateService>();
        services.AddScoped<FinancialDocumentsGetByContractService>();
```

- [ ] **Step 6: Criar a migration de menu**

Run:

```bash
dotnet ef migrations add AddFinancialMenus \
  --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web \
  --output-dir CommonContext
```

Substituir o corpo por:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext;

/// <summary>
/// Menus do módulo financeiro. O grupo "Financeiro" é RAIZ (ParentKey = null), Order 7 — os
/// raízes ocupados são main 0, admin 1, registers 2, storage 3, purchases 4, sales 5 e
/// reports 6; entra no fim para não renumerar nada.
///
/// A Key de cada item PRECISA ser igual ao name da rota no manifest.json do frontend:
/// App.controller.ts navega com navTo(item.getKey()).
///
/// Sem a linha em ROLE_MENUS o item não aparece para ninguém — inclusive o próprio grupo.
/// </summary>
public partial class AddFinancialMenus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.InsertData(
            table: "MENU_ITEMS",
            columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
            values: new object[,]
            {
                { "financial", "Financeiro", "sap-icon://money-bills", true, false, 7, null },
                { "financialAccounts", "Contas Financeiras", "sap-icon://folder-blank", true, false, 1, "financial" },
                { "accountsPayable", "Contas a Pagar", "sap-icon://folder-blank", true, false, 2, "financial" },
                { "accountsReceivable", "Contas a Receber", "sap-icon://folder-blank", true, false, 3, "financial" },
                { "financialAdvances", "Adiantamentos", "sap-icon://folder-blank", true, false, 4, "financial" }
            });

        migrationBuilder.InsertData(
            table: "ROLE_MENUS",
            columns: ["Id", "RoleCode", "MenuItemKey"],
            values: new object[,]
            {
                { "A1D4E7F0-2B36-4C58-9E71-0D3A6B8C5F21", "ADMIN", "financial" },
                { "B2E5F801-3C47-4D69-8F82-1E4B7C9D6032", "ADMIN", "financialAccounts" },
                { "C3F60912-4D58-4E7A-9083-2F5C8D0E7143", "ADMIN", "accountsPayable" },
                { "D4071A23-5E69-4F8B-A194-306D9E1F8254", "ADMIN", "accountsReceivable" },
                { "E5182B34-6F7A-409C-B2A5-417EAF209365", "ADMIN", "financialAdvances" }
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
        migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id",
            keyValues:
            [
                "A1D4E7F0-2B36-4C58-9E71-0D3A6B8C5F21", "B2E5F801-3C47-4D69-8F82-1E4B7C9D6032",
                "C3F60912-4D58-4E7A-9083-2F5C8D0E7143", "D4071A23-5E69-4F8B-A194-306D9E1F8254",
                "E5182B34-6F7A-409C-B2A5-417EAF209365"
            ]);

        // Filhos antes do pai, pela auto-relação ParentKey.
        migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key",
            keyValues: ["financialAccounts", "accountsPayable", "accountsReceivable", "financialAdvances"]);
        migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValues: ["financial"]);
    }
}
```

- [ ] **Step 7: Aplicar a migration de menu**

Run:

```bash
ASPNETCORE_ENVIRONMENT=Yokotobi-Development dotnet ef database update \
  --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

- [ ] **Step 8: Rodar tudo e stage**

Run: `dotnet build SiagroB1.sln && dotnet test SiagroB1.Application.Tests`

```bash
git add SiagroB1.Application/Services/Financials/ SiagroB1.Application.Tests/Financials/ \
        SiagroB1.Domain/Dtos/ SiagroB1.Web/ SiagroB1.Migrations/CommonContext/
```

---

## Task 11: Geração sob demanda para contratos já aprovados

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsGenerateBacklogService.cs`
- Create: `SiagroB1.Domain/Dtos/FinancialDocumentBacklogResultDto.cs`
- Create: `SiagroB1.Web/Actions/Financials/FinancialDocumentsGenerateBacklogController.cs`
- Modify: `ODataConfigurations.cs`, `ServiceCollectionExtensions.cs`

**Interfaces:**
- Consumes: `FinancialDocumentsGenerateService` (Task 5).
- Produces: `Task<FinancialDocumentBacklogResultDto> ExecuteAsync(string? branchCode, DateTime fromDate, DateTime toDate, bool dryRun, string userName)`

- [ ] **Step 1: Criar o DTO**

```csharp
using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

public class FinancialDocumentBacklogResultDto
{
    [JsonPropertyName("DryRun")] public bool DryRun { get; set; }
    [JsonPropertyName("EligibleFixations")] public int EligibleFixations { get; set; }
    [JsonPropertyName("Generated")] public int Generated { get; set; }
    [JsonPropertyName("SkippedAlreadyGenerated")] public int SkippedAlreadyGenerated { get; set; }
    [JsonPropertyName("SkippedWithoutDueDate")] public int SkippedWithoutDueDate { get; set; }
}
```

- [ ] **Step 2: Escrever o teste que falha**

O arquivo completo, `SiagroB1.Application.Tests/Financials/FinancialDocumentsGenerateBacklogServiceTests.cs`, com o helper de semente:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialDocumentsGenerateBacklogServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsGenerateBacklogService Service() => new(
        _db,
        new FinancialDocumentsGenerateService(
            _db.Context,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(
                new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" })));

    private async Task<PurchaseContract> SeedApprovedFixedContractAsync(DateTime? cashFlowDate)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC0001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            BranchCode = "01",
            CreationDate = DateTime.Today,
            TotalVolume = 1000m,
            StandardPrice = 100m,
            StandardCashFlowDate = cashFlowDate,
            StandardCurrency = CurrencyType.Brl,
            Type = ContractType.Fixed,
            Status = ContractStatus.Approved
        };

        contract.PriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = contract.TotalVolume,
            FixationPrice = contract.StandardPrice,
            Status = PriceFixationStatus.Confirmed
        });

        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task A_dry_run_reports_what_would_be_created_and_writes_nothing()
    {
        await SeedApprovedFixedContractAsync(cashFlowDate: DateTime.Today);

        var result = await Service().ExecuteAsync(
            branchCode: null,
            fromDate: DateTime.Today.AddYears(-1),
            toDate: DateTime.Today.AddDays(1),
            dryRun: true,
            userName: "tester");

        Assert.True(result.DryRun);
        Assert.Equal(1, result.EligibleFixations);
        Assert.Equal(0, result.Generated);
        Assert.Empty(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task Running_it_twice_generates_only_once()
    {
        await SeedApprovedFixedContractAsync(cashFlowDate: DateTime.Today);
        var service = Service();
        var from = DateTime.Today.AddYears(-1);
        var to = DateTime.Today.AddDays(1);

        await service.ExecuteAsync(null, from, to, dryRun: false, "tester");
        var second = await service.ExecuteAsync(null, from, to, dryRun: false, "tester");

        Assert.Equal(0, second.Generated);
        Assert.Equal(1, second.SkippedAlreadyGenerated);
        Assert.Single(_db.Context.FinancialDocuments);
    }

    [Fact]
    public async Task A_contract_without_a_due_date_is_reported_not_thrown()
    {
        await SeedApprovedFixedContractAsync(cashFlowDate: null);

        var result = await Service().ExecuteAsync(
            null, DateTime.Today.AddYears(-1), DateTime.Today.AddDays(1), dryRun: false, "tester");

        Assert.Equal(1, result.SkippedWithoutDueDate);
        Assert.Equal(0, result.Generated);
    }
}
```

- [ ] **Step 3: Rodar e confirmar que falha**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialDocumentsGenerateBacklogServiceTests`
Expected: falha de compilação.

- [ ] **Step 4: Implementar**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Geração deliberada de documentos provisórios para contratos JÁ aprovados.
///
/// Isto NÃO é migration de propósito. Uma migration roda no deploy e criaria em silêncio
/// milhares de documentos, muitos sem vencimento e muitos de contratos já entregues e pagos
/// fora do sistema — o módulo estrearia com um backlog falso e impagável, com risco real de
/// pagamento em duplicidade. E a numeração via Dapper num laço queimaria milhares de números
/// sob UPDLOCK. Aqui o financeiro roda por filial, confere o DryRun, e só então efetiva.
///
/// Contrato sem vencimento resolvível é RELATADO, nunca lançado: um contrato ruim no meio do
/// lote não pode derrubar o lote inteiro.
/// </summary>
public class FinancialDocumentsGenerateBacklogService(
    IUnitOfWork db,
    FinancialDocumentsGenerateService generateService)
{
    public async Task<FinancialDocumentBacklogResultDto> ExecuteAsync(
        string? branchCode, DateTime fromDate, DateTime toDate, bool dryRun, string userName)
    {
        var result = new FinancialDocumentBacklogResultDto { DryRun = dryRun };

        var contracts = await db.Context.PurchaseContracts
            .Include(x => x.PriceFixations)
            .Where(x => x.Status == ContractStatus.Approved &&
                        x.CreationDate >= fromDate && x.CreationDate <= toDate &&
                        (branchCode == null || x.BranchCode == branchCode))
            .ToListAsync();

        foreach (var contract in contracts)
        {
            foreach (var fixation in contract.PriceFixations
                         .Where(f => f.Status == PriceFixationStatus.Confirmed))
            {
                result.EligibleFixations++;

                var alreadyGenerated = await db.Context.FinancialDocuments.AnyAsync(x =>
                    x.OriginType == FinancialDocumentOrigin.PurchaseContractPriceFixation &&
                    x.OriginKey == fixation.Key &&
                    x.Nature == FinancialDocumentNature.Provisional &&
                    x.Status != FinancialDocumentStatus.Canceled);

                if (alreadyGenerated) { result.SkippedAlreadyGenerated++; continue; }

                if ((fixation.FinancialDueDate ?? contract.StandardCashFlowDate) is null)
                {
                    result.SkippedWithoutDueDate++;
                    continue;
                }

                if (dryRun) continue;

                await generateService.EnqueueForPurchaseFixationAsync(contract, fixation, userName);
                await db.SaveChangesAsync();
                result.Generated++;
            }
        }

        return result;
    }
}
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test SiagroB1.Application.Tests --filter FullyQualifiedName~FinancialDocumentsGenerateBacklogServiceTests`
Expected: 3 testes passando.

- [ ] **Step 6: Criar a action e registrar**

Em `ODataConfigurations.cs`:

```csharp
        var financialBacklog = modelBuilder.Action("FinancialDocumentsGenerateBacklog");
        financialBacklog.Parameter<string>("BranchCode").Optional();
        financialBacklog.Parameter<string>("FromDate");
        financialBacklog.Parameter<string>("ToDate");
        financialBacklog.Parameter<bool>("DryRun");
        financialBacklog.Returns<FinancialDocumentBacklogResultDto>();
```

Em `ServiceCollectionExtensions.cs`: `services.AddScoped<FinancialDocumentsGenerateBacklogService>();`

`SiagroB1.Web/Actions/Financials/FinancialDocumentsGenerateBacklogController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

public class FinancialDocumentsGenerateBacklogController(
    FinancialDocumentsGenerateBacklogService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsGenerateBacklog")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("FromDate", out var fromObj) || fromObj is null)
                return BadRequest("Informe a data inicial.");

            if (!parameters.TryGetValue("ToDate", out var toObj) || toObj is null)
                return BadRequest("Informe a data final.");

            // Sem DryRun explícito, simula: gerar por engano é o erro caro aqui.
            var dryRun = !parameters.TryGetValue("DryRun", out var dryObj)
                         || dryObj is null
                         || Convert.ToBoolean(dryObj);

            parameters.TryGetValue("BranchCode", out var branchObj);

            var result = await service.ExecuteAsync(
                branchCode: branchObj?.ToString(),
                fromDate: DateTime.Parse(fromObj.ToString()!),
                toDate: DateTime.Parse(toObj.ToString()!),
                dryRun: dryRun,
                userName: User.Identity?.Name ?? "Unknown");

            return Ok(result);
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

- [ ] **Step 7: Verificação final, pelo caminho do usuário**

Subir a stack e percorrer o roteiro inteiro da seção "Verificação" do spec, os 10 passos. Em especial:

- o menu **Financeiro** aparece com os quatro itens;
- baixar um provisório é recusado com "…é provisório e não pode ser baixado";
- `DryRun = true` em base com contratos aprovados devolve a contagem e **zero** linhas gravadas.

Ao terminar, **derrubar a stack matando por PID** nas portas 50000/5246/8080 — parar a task não basta, o file watcher já ressuscitou os três processos neste projeto.

- [ ] **Step 8: Stage**

```bash
git add SiagroB1.Application/Services/Financials/ SiagroB1.Application.Tests/Financials/ \
        SiagroB1.Domain/Dtos/ SiagroB1.Web/
```

---

## Fora do escopo deste plano

O **frontend** (telas de Contas a Pagar, Contas a Receber, Adiantamentos e Contas Financeiras, no repositório `siagro-b1-frontend`) é um plano próprio, escrito depois que o backend estiver verificado. O spec já descreve os padrões que ele terá de seguir; separá-lo evita um plano de duas dezenas de tarefas atravessando dois repositórios, que são **dois conjuntos de arquivos staged independentes**.
