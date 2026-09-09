# Devolução e revínculo de adiantamento — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fechar a pendência da Fase 1 do módulo financeiro — um adiantamento pago não pode mais sobreviver ao cancelamento do contrato: ou a baixa é estornada, ou o valor é devolvido, ou o adiantamento é vinculado a outro contrato.

**Architecture:** Dois serviços de aplicação novos (`FinancialAdvancesRefundService`, `FinancialAdvancesRelinkContractService`) e um serviço de guard (`FinancialDocumentsContractCancellationGuardService`) chamado pelos dois serviços de cancelamento de contrato. A devolução é uma linha NEGATIVA no ledger insert-only com origem própria (`AdvanceRefund`), seguida do cancelamento do título; o revínculo troca a FK do contrato e grava uma linha no log de alterações. **Nenhuma migration** — só um valor novo de enum (coluna já é `int`) e uma constante de código. Duas actions OData e dois diálogos no detalhe do título já existente.

**Tech Stack:** .NET 10, EF Core, ASP.NET Core OData v8, xUnit + EF InMemory (backend, repo `siagro-b1-backend`); OpenUI5 1.141 + TypeScript, OData v4 (frontend, repo `siagro-b1-frontend`).

**Spec:** `docs/superpowers/specs/2026-09-08-advance-refund-and-relink-design.md`

## Global Constraints

- **Dois repositórios independentes.** Tasks 1–4 são em `C:\Projetos\SiagroB1\siagro-b1-backend`; Tasks 5–6 em `C:\Projetos\SiagroB1\siagro-b1-frontend`. Uma mudança que atravessa os dois é **dois conjuntos de arquivos em dois repos**, nunca um. Sempre `git -C "<caminho do repo>"`.
- **NUNCA commitar ou dar push.** O passo final de cada task é `git add` dos arquivos criados/alterados, e só isso. Os commits são feitos manualmente pelo usuário. Isto SUBSTITUI o passo "Commit" do template de plano.
- **Todo arquivo novo tem de ser staged** com `git add` logo depois de criado, para que `git status`/`git diff` mostrem o conjunto completo da mudança.
- **Identificadores de código em inglês; texto que o usuário lê em pt-BR.** Classes, serviços, propriedades e parâmetros em inglês; rótulos de tela, títulos de diálogo e mensagens de erro de negócio em português. Comentários em pt-BR.
- **Nenhuma migration nesta entrega.** Ao final da Task 3, `dotnet ef migrations has-pending-model-changes` tem de responder que **não** há mudanças pendentes. Se aparecer migration pendente, alguma coisa saiu do desenho — pare e reporte.
- **Parâmetro OData de dinheiro é `double`, nunca `decimal`**; data e enum vão como `string`. O UI5 v4 serializa `Edm.Decimal` como STRING e o backend devolve 400 sem nomear o campo. *(Não há parâmetro de dinheiro nestas duas actions — o valor da devolução é sempre lido no servidor — mas a regra vale para as datas.)*
- **`ODataActionParameters` chega NULO** quando o corpo não casa com nenhum parâmetro do EDM, e `TryGetValue` devolve `true` com valor `null` para string ausente. Repetir as duas proteções que as controllers existentes já fazem.
- **`--` dentro de comentário XML mata o fragmento inteiro.** XML proíbe `--` em comentário; `Fragment.load` devolve nulls em silêncio e nenhum gate (ts-typecheck, eslint, ui5lint) acusa. Nunca escrever `--` na prosa de um comentário de fragmento.
- **`core:require` da view NÃO chega ao `<core:Fragment>`.** Cada fragmento é parseado como documento próprio. Dentro de fragmento, use `.formatter.x` COM ponto inicial (resolve contra o controller) ou declare `core:require` no próprio `FragmentDefinition`.
- **Data `Edm.DateTimeOffset` na tela** usa `type: 'sap.ui.model.odata.type.DateTimeOffset'` + `constraints: { precision: 7 }` + `formatOptions: { pattern: 'dd/MM/yyyy' }`. Nunca `sap.ui.model.odata.type.Date`.
- **Filtro sobre propriedade de ENUM nunca é `sap.ui.model.Filter`** — estoura "Unsupported type" no UI5. Filtro de enum mora no `$filter` estático do fragmento. Filtro sobre string (ex.: `CardCode`) como `Filter` é seguro.
- **Verificação é no navegador, não em gate verde.** A Fase 1 mostrou um value help inteiro morto por um `--` em comentário, com todos os gates passando.

---

### Task 1: `FinancialAdvancesRefundService` — devolver o valor adiantado

Registra que o dinheiro **saiu e voltou**. É deliberadamente diferente do estorno: o estorno diz que a baixa não deveria ter existido; a devolução diz que ela existiu e foi desfeita por fora. Reusar `Reversal` faria o razão mentir.

**Files:**
- Modify: `SiagroB1.Domain/Enums/FinancialSettlementOrigin.cs`
- Create: `SiagroB1.Application/Services/Financials/FinancialAdvancesRefundService.cs`
- Test: `SiagroB1.Application.Tests/Financials/FinancialAdvancesRefundServiceTests.cs`

**Interfaces:**
- Consumes: `FinancialDocumentsRecalculateBalanceService.RecalculateAsync(AppDbContext, Guid)` (estático, já existe); `IUnitOfWork` com `BeginTransactionAsync/CommitAsync/RollbackAsync`; `CommitMode` de `SiagroB1.Infra.Enums`.
- Produces: `FinancialAdvancesRefundService.ExecuteAsync(Guid documentKey, string financialAccountCode, DateTime refundDate, string? documentReference, string reason, string userName, CommitMode commitMode = CommitMode.Auto) : Task` — consumido pela Task 4 (controller). E `FinancialSettlementOrigin.AdvanceRefund = 5`.

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/Financials/FinancialAdvancesRefundServiceTests.cs`:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Devolução do adiantamento: linha NEGATIVA com origem própria e o título cancelado.
/// A origem separada de Reversal é o ponto — o estorno diz que a baixa não deveria ter
/// existido, a devolução diz que ela existiu e o dinheiro voltou.
/// </summary>
public class FinancialAdvancesRefundServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAdvancesRefundService Service() => new(_db);
    private FinancialDocumentsSettleService Settler() => new(_db);

    private async Task<FinancialDocument> SeedAsync(
        FinancialDocumentNature nature = FinancialDocumentNature.Advance,
        decimal netAmount = 1000m,
        decimal settle = 1000m,
        FinancialDocumentStatus status = FinancialDocumentStatus.Open)
    {
        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl
        });

        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FN000217",
            CardCode = "F0001",
            Direction = FinancialDirection.Payable,
            Nature = nature,
            Status = status,
            DueDate = DateTime.Today.AddDays(30),
            NetAmount = netAmount,
            Currency = CurrencyType.Brl
        };

        _db.Context.FinancialDocuments.Add(document);
        await _db.Context.SaveChangesAsync();

        if (settle > 0m && nature == FinancialDocumentNature.Advance)
            await Settler().ExecuteAsync(document.Key, "CX01", settle, DateTime.Today,
                0m, 0m, 0m, "OP-1", null, "tester");

        return document;
    }

    [Fact]
    public async Task Refunding_writes_a_negative_line_zeroes_the_balance_and_cancels_the_document()
    {
        var document = await SeedAsync();

        await Service().ExecuteAsync(document.Key, "CX01", new DateTime(2026, 9, 8),
            "TED-99", "produtor desistiu", "tester");

        var refund = _db.Context.FinancialSettlements
            .Single(x => x.Origin == FinancialSettlementOrigin.AdvanceRefund);

        Assert.Equal(-1000m, refund.Amount);
        Assert.Equal(0m, refund.InterestAmount);
        Assert.Equal(0m, refund.FineAmount);
        Assert.Equal(0m, refund.DiscountAmount);
        Assert.Equal("CX01", refund.FinancialAccountCode);
        Assert.Equal(new DateTime(2026, 9, 8), refund.SettlementDate);
        Assert.Equal("TED-99", refund.DocumentReference);
        Assert.Null(refund.ReversedSettlementKey);
        Assert.Equal("produtor desistiu", refund.Notes);

        Assert.Equal(0m, document.SettledAmount);
        Assert.Equal(0m, document.AvailableAdvanceAmount);
        Assert.Equal(FinancialDocumentStatus.Canceled, document.Status);
        Assert.Contains("produtor desistiu", document.CancellationReason);
        Assert.Equal("tester", document.CanceledBy);
    }

    [Fact]
    public async Task Refusing_a_second_refund_of_the_same_advance()
    {
        var document = await SeedAsync();

        await Service().ExecuteAsync(document.Key, "CX01", DateTime.Today, null, "primeira", "tester");

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", DateTime.Today, null, "segunda", "tester"));

        Assert.Contains("cancelado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_an_advance_with_nothing_settled()
    {
        var document = await SeedAsync(settle: 0m);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", DateTime.Today, null, "motivo", "tester"));

        Assert.Contains("não há", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_document_that_is_not_an_advance()
    {
        var document = await SeedAsync(nature: FinancialDocumentNature.Provisional, settle: 0m);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", DateTime.Today, null, "motivo", "tester"));

        Assert.Contains("adiantamento", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_refund_without_a_reason()
    {
        var document = await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX01", DateTime.Today, null, "  ", "tester"));

        Assert.Contains("motivo", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_an_unknown_financial_account()
    {
        var document = await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "NAOEXISTE", DateTime.Today, null, "motivo", "tester"));

        Assert.Contains("conta financeira", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_an_inactive_financial_account()
    {
        var document = await SeedAsync();

        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX02", Name = "Caixa velho", Type = FinancialAccountType.Cash,
            Currency = CurrencyType.Brl, Inactive = true
        });
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(document.Key, "CX02", DateTime.Today, null, "motivo", "tester"));

        Assert.Contains("inativa", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_document_that_does_not_exist()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<SiagroB1.Domain.Exceptions.NotFoundException>(
            () => Service().ExecuteAsync(Guid.NewGuid(), "CX01", DateTime.Today, null, "motivo", "tester"));
    }
}
```

- [ ] **Step 2: Rodar o teste e ver falhar**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend"
dotnet test --filter FullyQualifiedName~FinancialAdvancesRefundServiceTests
```

Esperado: FALHA de COMPILAÇÃO — `FinancialAdvancesRefundService` não existe e `FinancialSettlementOrigin.AdvanceRefund` não existe.

- [ ] **Step 3: Acrescentar o valor ao enum**

Em `SiagroB1.Domain/Enums/FinancialSettlementOrigin.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

public enum FinancialSettlementOrigin
{
    Manual = 0,
    Reversal = 1,
    InvoiceOffset = 2,
    AdvanceApplication = 3,
    Netting = 4,

    /// <summary>
    /// Devolução do valor adiantado. Origem PRÓPRIA e não Reversal de propósito: o estorno diz
    /// que a baixa não deveria ter existido, a devolução diz que ela existiu e o dinheiro voltou.
    /// A conciliação bancária da Fase 4 precisa da distinção — no estorno não há movimento de
    /// caixa a conciliar, na devolução há.
    /// </summary>
    AdvanceRefund = 5
}
```

- [ ] **Step 4: Escrever o serviço**

Criar `SiagroB1.Application/Services/Financials/FinancialAdvancesRefundService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Devolução do valor adiantado: o dinheiro saiu e voltou.
///
/// Uma das três saídas para um adiantamento pago cujo contrato alguém quer cancelar (as outras
/// são estornar a baixa e vincular o adiantamento a outro contrato). Sempre pelo valor INTEIRO:
/// devolução parcial exigiria partir o título em dois e encosta na amortização da Fase 2.
///
/// AvailableAdvanceAmount é [NotMapped] derivado de SettledAmount, então o crédito desaparece
/// sozinho — não há segunda fonte de verdade para sincronizar.
/// </summary>
public class FinancialAdvancesRefundService(IUnitOfWork db)
{
    public async Task ExecuteAsync(
        Guid documentKey,
        string financialAccountCode,
        DateTime refundDate,
        string? documentReference,
        string reason,
        string userName,
        CommitMode commitMode = CommitMode.Auto)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo da devolução.");

        var document = await db.Context.FinancialDocuments
                           .FirstOrDefaultAsync(x => x.Key == documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Nature != FinancialDocumentNature.Advance)
            throw new ApplicationException(
                $"O documento {document.Code} não é um adiantamento. Só adiantamento se devolve.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} já está cancelado.");

        if (document.SettledAmount == 0m)
            throw new ApplicationException(
                $"O adiantamento {document.Code} não tem valor pago: não há o que devolver.");

        // Obrigatória: o dinheiro volta para algum lugar concreto. FinancialAccountCode ser
        // anulável no modelo existe para as origens não-caixa da Fase 2/3, não para esta.
        var account = await db.Context.FinancialAccounts
            .FirstOrDefaultAsync(x => x.Code == financialAccountCode);

        if (account is null)
            throw new ApplicationException("Informe a conta financeira que recebeu a devolução.");

        if (account.Inactive)
            throw new ApplicationException($"A conta financeira {account.Code} está inativa.");

        var refund = new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = documentKey,
            FinancialAccountCode = financialAccountCode,
            SettlementDate = refundDate,
            Amount = -document.SettledAmount,
            InterestAmount = 0m,
            FineAmount = 0m,
            DiscountAmount = 0m,
            Origin = FinancialSettlementOrigin.AdvanceRefund,
            ReversedSettlementKey = null,
            DocumentReference = documentReference,
            Notes = reason,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName
        };

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialSettlements.Add(refund);
            await db.Context.SaveChangesAsync();

            // SumAsync não enxerga entidade rastreada ainda não salva: recalcular só DEPOIS do save.
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, documentKey);
            await db.Context.SaveChangesAsync();

            // O recálculo escreve Status; o cancelamento vem DEPOIS para prevalecer sobre ele.
            document.Status = FinancialDocumentStatus.Canceled;
            document.CancellationReason = $"Adiantamento devolvido: {reason}";
            document.CanceledAt = DateTime.Now;
            document.CanceledBy = userName;
            document.UpdatedAt = DateTime.Now;
            document.UpdatedBy = userName;
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

- [ ] **Step 5: Rodar o teste e ver passar**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend"
dotnet test --filter FullyQualifiedName~FinancialAdvancesRefundServiceTests
```

Esperado: PASS, 8 testes.

Se `Refunding_writes_a_negative_line...` falhar em `document.Status`, confira que o bloco de cancelamento vem **depois** do `RecalculateAsync` — o recálculo grava `Status` a partir do saldo e sobrescreveria `Canceled` se rodasse por último.

- [ ] **Step 6: Stage (NÃO commitar)**

```bash
git -C "C:/Projetos/SiagroB1/siagro-b1-backend" add \
  SiagroB1.Domain/Enums/FinancialSettlementOrigin.cs \
  SiagroB1.Application/Services/Financials/FinancialAdvancesRefundService.cs \
  SiagroB1.Application.Tests/Financials/FinancialAdvancesRefundServiceTests.cs
```

---

### Task 2: `FinancialAdvancesRelinkContractService` — vincular o adiantamento a outro contrato

O vínculo entre adiantamento e contrato, que hoje nasce em `FinancialAdvancesCreateService` e nunca mais muda, passa a ser mutável sob regras. Sempre o adiantamento INTEIRO.

**Files:**
- Modify: `SiagroB1.Domain/Entities/FinancialDocumentChangeLogFields.cs`
- Create: `SiagroB1.Application/Services/Financials/FinancialAdvancesRelinkContractService.cs`
- Test: `SiagroB1.Application.Tests/Financials/FinancialAdvancesRelinkContractServiceTests.cs`

**Interfaces:**
- Consumes: `FinancialDocumentChangeLogService.Register(Guid documentKey, string field, string? oldValue, string? newValue, string userName) : void` (só enfileira no ChangeTracker; quem salva é este serviço); `IUnitOfWork`.
- Produces: `FinancialAdvancesRelinkContractService.ExecuteAsync(Guid documentKey, string targetContractType, Guid targetContractKey, string userName) : Task` — consumido pela Task 4. E `FinancialDocumentChangeLogFields.Contract = "Contract"`, consumido pela Task 6 (formatter da tela).

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/Financials/FinancialAdvancesRelinkContractServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Revínculo do adiantamento. O guard que importa é o de PARCEIRO: sem ele, mover crédito de um
/// produtor para o contrato de outro é um erro caro e silencioso.
/// </summary>
public class FinancialAdvancesRelinkContractServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialAdvancesRelinkContractService Service() =>
        new(_db, new FinancialDocumentChangeLogService(_db.Context));

    private static PurchaseContract NewPurchaseContract(
        string code, string cardCode = "F0001", string branchCode = "03",
        ContractStatus status = ContractStatus.Approved,
        CurrencyType currency = CurrencyType.Brl) => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CardCode = cardCode,
        BranchCode = branchCode,
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = 1_000m,
        Type = ContractType.Fixed,
        StandardCashFlowDate = new DateTime(2026, 12, 31),
        StandardCurrency = currency,
        PaymentTerms = "Depósito no Banco X",
        Status = status,
    };

    private static SalesContract NewSalesContract(string code, string cardCode = "C0001") => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CardCode = cardCode,
        BranchCode = "03",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        TotalVolume = 1_000m,
        StandardCashFlowDate = new DateTime(2026, 12, 31),
        StandardCurrency = CurrencyType.Brl,
        Status = ContractStatus.Approved,
    };

    private async Task<(FinancialDocument advance, PurchaseContract origin)> SeedAsync(
        FinancialDirection direction = FinancialDirection.Payable,
        FinancialDocumentNature nature = FinancialDocumentNature.Advance)
    {
        var origin = NewPurchaseContract("PC-0001");
        _db.Context.PurchaseContracts.Add(origin);

        var advance = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = "FN000217",
            CardCode = "F0001",
            BranchCode = "03",
            Direction = direction,
            Nature = nature,
            Status = FinancialDocumentStatus.Settled,
            DueDate = new DateTime(2026, 12, 31),
            NetAmount = 50_000m,
            SettledAmount = 50_000m,
            Currency = CurrencyType.Brl,
            OriginType = FinancialDocumentOrigin.Manual,
            OriginDocNumber = "PC-0001",
            PurchaseContractKey = origin.Key,
            PaymentTermsText = "Depósito no Banco X",
        };

        _db.Context.FinancialDocuments.Add(advance);
        await _db.Context.SaveChangesAsync();
        return (advance, origin);
    }

    [Fact]
    public async Task Relinking_moves_the_key_the_origin_doc_number_and_logs_one_line()
    {
        var (advance, _) = await SeedAsync();
        var target = NewPurchaseContract("PC-0002");
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester");

        Assert.Equal(target.Key, advance.PurchaseContractKey);
        Assert.Null(advance.SalesContractKey);
        Assert.Equal("PC-0002", advance.OriginDocNumber);

        var log = await _db.Context.FinancialDocumentChangeLogs
            .SingleAsync(x => x.FinancialDocumentKey == advance.Key);

        Assert.Equal(FinancialDocumentChangeLogFields.Contract, log.Field);
        Assert.Equal("PC-0001", log.OldValue);
        Assert.Equal("PC-0002", log.NewValue);
        Assert.Equal("tester", log.ChangedBy);
    }

    /// <summary>
    /// PaymentTermsText é a cópia de "onde pagar" segundo o contrato de ORIGEM, e para um
    /// adiantamento já pago foi por ali que o dinheiro saiu. Reescrevê-lo apagaria histórico.
    /// </summary>
    [Fact]
    public async Task Relinking_does_not_touch_the_payment_terms_text()
    {
        var (advance, _) = await SeedAsync();
        var target = NewPurchaseContract("PC-0002");
        target.PaymentTerms = "Outro banco totalmente diferente";
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester");

        Assert.Equal("Depósito no Banco X", advance.PaymentTermsText);
    }

    [Fact]
    public async Task Refuses_a_target_contract_of_a_different_partner()
    {
        var (advance, _) = await SeedAsync();
        var target = NewPurchaseContract("PC-0002", cardCode: "F0002");
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester"));

        Assert.Contains("parceiro", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_target_contract_that_is_not_approved()
    {
        var (advance, _) = await SeedAsync();
        var target = NewPurchaseContract("PC-0002", status: ContractStatus.InApproval);
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester"));

        Assert.Contains("aprovado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Direção cruzada é erro de negócio, não de digitação: a pagar só migra para compra.</summary>
    [Fact]
    public async Task Refuses_a_sales_contract_for_a_payable_advance()
    {
        var (advance, _) = await SeedAsync();
        var target = NewSalesContract("SC-0002", cardCode: "F0001");
        _db.Context.SalesContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Sales", target.Key, "tester"));

        Assert.Contains("a pagar", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_target_contract_in_a_different_branch()
    {
        var (advance, _) = await SeedAsync();
        var target = NewPurchaseContract("PC-0002", branchCode: "01");
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester"));

        Assert.Contains("filial", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_target_contract_in_a_different_currency()
    {
        var (advance, _) = await SeedAsync();
        var target = NewPurchaseContract("PC-0002", currency: CurrencyType.Usd);
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester"));

        Assert.Contains("moeda", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_relinking_to_the_contract_it_is_already_linked_to()
    {
        var (advance, origin) = await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Purchase", origin.Key, "tester"));

        Assert.Contains("já está vinculado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_document_that_is_not_an_advance()
    {
        var (advance, _) = await SeedAsync(nature: FinancialDocumentNature.Provisional);
        var target = NewPurchaseContract("PC-0002");
        _db.Context.PurchaseContracts.Add(target);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Purchase", target.Key, "tester"));

        Assert.Contains("adiantamento", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_an_invalid_contract_type()
    {
        var (advance, _) = await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(advance.Key, "Barter", Guid.NewGuid(), "tester"));

        Assert.Contains("tipo de contrato", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Rodar o teste e ver falhar**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend"
dotnet test --filter FullyQualifiedName~FinancialAdvancesRelinkContractServiceTests
```

Esperado: FALHA de COMPILAÇÃO — `FinancialAdvancesRelinkContractService` e `FinancialDocumentChangeLogFields.Contract` não existem.

- [ ] **Step 3: Acrescentar a constante do log**

Em `SiagroB1.Domain/Entities/FinancialDocumentChangeLogFields.cs`:

```csharp
public static class FinancialDocumentChangeLogFields
{
    public const string DueDate = "DueDate";
    public const string Comments = "Comments";

    /// <summary>Revínculo do adiantamento: os valores gravados são os CÓDIGOS dos contratos.</summary>
    public const string Contract = "Contract";
}
```

- [ ] **Step 4: Escrever o serviço**

Criar `SiagroB1.Application/Services/Financials/FinancialAdvancesRelinkContractService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Vincula um adiantamento a OUTRO contrato. Sempre o adiantamento inteiro — parcial exigiria
/// partir o título em dois e encosta na amortização da Fase 2.
///
/// Depois da troca o contrato antigo fica sem adiantamento e o cancelamento passa; o contrato
/// novo herda o adiantamento e passa a ser protegido pelo mesmo guard.
///
/// O índice único filtrado IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin cobre apenas
/// Nature = Provisional, então adiantamento migrando de contrato nunca colide com ele.
/// </summary>
public class FinancialAdvancesRelinkContractService(
    IUnitOfWork db,
    FinancialDocumentChangeLogService changeLog)
{
    public async Task ExecuteAsync(
        Guid documentKey, string targetContractType, Guid targetContractKey, string userName)
    {
        var document = await db.Context.FinancialDocuments
                           .FirstOrDefaultAsync(x => x.Key == documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Nature != FinancialDocumentNature.Advance)
            throw new ApplicationException(
                $"O documento {document.Code} não é um adiantamento. Só adiantamento troca de contrato.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} está cancelado.");

        var isPurchase = string.Equals(targetContractType, "Purchase", StringComparison.OrdinalIgnoreCase);
        var isSales = string.Equals(targetContractType, "Sales", StringComparison.OrdinalIgnoreCase);

        if (!isPurchase && !isSales)
            throw new ApplicationException("Tipo de contrato inválido. Informe Purchase ou Sales.");

        // Direção cruzada é erro de NEGÓCIO: título a pagar é do produtor, a receber é do cliente.
        if (isPurchase && document.Direction != FinancialDirection.Payable)
            throw new ApplicationException(
                "Um título a receber só pode ser vinculado a um contrato de venda.");

        if (isSales && document.Direction != FinancialDirection.Receivable)
            throw new ApplicationException(
                "Um título a pagar só pode ser vinculado a um contrato de compra.");

        string targetCode, targetCardCode, targetBranchCode;
        CurrencyType targetCurrency;
        ContractStatus targetStatus;

        if (isPurchase)
        {
            var contract = await db.Context.PurchaseContracts.AsNoTracking()
                               .FirstOrDefaultAsync(x => x.Key == targetContractKey)
                           ?? throw new NotFoundException("Contrato de compra não encontrado.");

            targetCode = contract.Code ?? string.Empty;
            targetCardCode = contract.CardCode;
            targetBranchCode = contract.BranchCode ?? string.Empty;
            targetCurrency = contract.StandardCurrency ?? CurrencyType.Brl;
            targetStatus = contract.Status;
        }
        else
        {
            var contract = await db.Context.SalesContracts.AsNoTracking()
                               .FirstOrDefaultAsync(x => x.Key == targetContractKey)
                           ?? throw new NotFoundException("Contrato de venda não encontrado.");

            targetCode = contract.Code ?? string.Empty;
            targetCardCode = contract.CardCode;
            targetBranchCode = contract.BranchCode ?? string.Empty;
            targetCurrency = contract.StandardCurrency ?? CurrencyType.Brl;
            targetStatus = contract.Status;
        }

        var currentKey = isPurchase ? document.PurchaseContractKey : document.SalesContractKey;

        if (currentKey == targetContractKey)
            throw new ApplicationException(
                $"O adiantamento {document.Code} já está vinculado ao contrato {targetCode}.");

        if (targetStatus != ContractStatus.Approved)
            throw new ApplicationException(
                $"O contrato {targetCode} precisa estar aprovado para receber o adiantamento.");

        // O dinheiro é DAQUELE parceiro: é este guard que impede mover crédito de um produtor
        // para o contrato de outro.
        if (!string.Equals(targetCardCode, document.CardCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O contrato {targetCode} é de outro parceiro ({targetCardCode}). O adiantamento " +
                $"é de {document.CardCode} e só pode migrar entre contratos do mesmo parceiro.");

        if (!string.Equals(targetBranchCode, document.BranchCode ?? string.Empty,
                StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException(
                $"O contrato {targetCode} é da filial {targetBranchCode} e o adiantamento da " +
                $"filial {document.BranchCode}: a filial precisa ser a mesma.");

        if (targetCurrency != document.Currency)
            throw new ApplicationException(
                $"O contrato {targetCode} é em {targetCurrency} e o adiantamento em " +
                $"{document.Currency}: a moeda precisa ser a mesma.");

        var oldCode = document.OriginDocNumber;

        document.PurchaseContractKey = isPurchase ? targetContractKey : null;
        document.SalesContractKey = isSales ? targetContractKey : null;
        document.OriginDocNumber = targetCode;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;

        // PaymentTermsText NÃO muda: é a cópia de "onde pagar" do contrato de origem, e para um
        // adiantamento já pago foi por ali que o dinheiro saiu.

        changeLog.Register(documentKey, FinancialDocumentChangeLogFields.Contract,
            oldCode, targetCode, userName);

        await db.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Rodar o teste e ver passar**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend"
dotnet test --filter FullyQualifiedName~FinancialAdvancesRelinkContractServiceTests
```

Esperado: PASS, 10 testes.

- [ ] **Step 6: Stage (NÃO commitar)**

```bash
git -C "C:/Projetos/SiagroB1/siagro-b1-backend" add \
  SiagroB1.Domain/Entities/FinancialDocumentChangeLogFields.cs \
  SiagroB1.Application/Services/Financials/FinancialAdvancesRelinkContractService.cs \
  SiagroB1.Application.Tests/Financials/FinancialAdvancesRelinkContractServiceTests.cs
```

---

### Task 3: Guard de cancelamento de contrato

A regra é financeira, então mora no financeiro e é chamada pelos dois serviços de cancelamento, ao lado do guard de movimento físico que já existe. Nesta task o comportamento visível muda: **cancelar contrato com adiantamento PAGO passa a falhar**, e adiantamento NÃO pago passa a ser cancelado junto.

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsContractCancellationGuardService.cs`
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsCancelService.cs` (parâmetro `includeUnpaidAdvances` + XML-doc)
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCancelService.cs`
- Modify: `SiagroB1.Application/Services/SalesContracts/SalesContractsCancelService.cs`
- Modify: `SiagroB1.Application.Tests/Support/FinancialDocumentTestServices.cs` (fábrica do guard)
- Modify: `SiagroB1.Application.Tests/Financials/FinancialDocumentUndoHooksTests.cs` (3 call sites + reescrever 1 teste)
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsContractCancellationGuardServiceTests.cs`

**Interfaces:**
- Consumes: `FinancialAdvancesRefundService.ExecuteAsync(...)` (Task 1) e `FinancialAdvancesRelinkContractService.ExecuteAsync(...)` (Task 2) nos testes de integração do guard; `FinancialDocumentsReverseSettlementService.ExecuteAsync(Guid settlementKey, string reason, string userName, CommitMode)`; `FinancialDocumentsSettleService.ExecuteAsync(...)` que **retorna** o `FinancialSettlement` gravado.
- Produces:
  - `FinancialDocumentsContractCancellationGuardService.EnsureCanCancelAsync(Guid? purchaseContractKey, Guid? salesContractKey) : Task`
  - `FinancialDocumentsCancelService.EnqueueCancelByContractAsync(Guid? purchaseContractKey, Guid? salesContractKey, string reason, string userName, bool includeUnpaidAdvances = false) : Task`
  - Construtores novos: `PurchaseContractsCancelService(IUnitOfWork, ContractNotificationOutboxService, FinancialDocumentsCancelService, FinancialDocumentsContractCancellationGuardService)` e o espelho de venda. **Consumido pela Task 4 (DI).**
  - `FinancialDocumentTestServices.CancellationGuard(AppDbContext) : FinancialDocumentsContractCancellationGuardService`

- [ ] **Step 1: Escrever o teste que falha**

Criar `SiagroB1.Application.Tests/Financials/FinancialDocumentsContractCancellationGuardServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// O guard que faltava na Fase 1: dinheiro passa a ser tratado com o mesmo cuidado que grão.
/// Cancelar contrato com adiantamento PAGO falha e NÃO mexe no status do contrato; as três
/// saídas (estornar, devolver, revincular) destravam o cancelamento.
///
/// Cada fase (semear, agir, conferir) usa um AppDbContext próprio sobre o mesmo banco InMemory,
/// no molde de FinancialDocumentUndoHooksTests — assim o change tracker não "conserta" sozinho
/// o que a query de produção não incluiu.
/// </summary>
public class FinancialDocumentsContractCancellationGuardServiceTests
{
    private static AppDbContext NewContext(string dbName) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static PurchaseContract NewContract(string code = "PC-GUARD-001") => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CardCode = "F0001",
        BranchCode = "03",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = 1_000m,
        Type = ContractType.Fixed,
        StandardCashFlowDate = new DateTime(2026, 12, 31),
        StandardCurrency = CurrencyType.Brl,
        Status = ContractStatus.Approved,
    };

    private static FinancialDocument NewAdvance(Guid contractKey, decimal settled) => new()
    {
        Key = Guid.NewGuid(),
        Code = "FN000217",
        CardCode = "F0001",
        BranchCode = "03",
        Direction = FinancialDirection.Payable,
        Nature = FinancialDocumentNature.Advance,
        Status = settled > 0m ? FinancialDocumentStatus.Settled : FinancialDocumentStatus.Open,
        DueDate = new DateTime(2026, 12, 31),
        NetAmount = 50_000m,
        SettledAmount = settled,
        Currency = CurrencyType.Brl,
        OriginType = FinancialDocumentOrigin.Manual,
        OriginDocNumber = "PC-GUARD-001",
        PurchaseContractKey = contractKey,
    };

    private static Task CancelContractAsync(AppDbContext context, Guid contractKey) =>
        new PurchaseContractsCancelService(
                new UnitOfWork(context),
                TestNotificationOutbox.For(context),
                FinancialDocumentTestServices.Cancel(context),
                FinancialDocumentTestServices.CancellationGuard(context))
            .ExecuteAsync(contractKey, "washout", "tester");

    [Fact]
    public async Task Canceling_a_contract_with_a_paid_advance_fails_and_leaves_the_contract_approved()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);
            seed.FinancialDocuments.Add(NewAdvance(contractKey, 50_000m));
            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(dbName))
        {
            var error = await Assert.ThrowsAsync<ApplicationException>(
                () => CancelContractAsync(act, contractKey));

            Assert.Contains("FN000217", error.Message);
            Assert.Contains("adiantamento pago", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("devolução", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("outro contrato", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        await using var assert = NewContext(dbName);
        Assert.Equal(ContractStatus.Approved,
            (await assert.PurchaseContracts.SingleAsync(x => x.Key == contractKey)).Status);
    }

    /// <summary>Adiantamento não pago é só uma promessa: cancela junto com o contrato.</summary>
    [Fact]
    public async Task Canceling_a_contract_cancels_an_unpaid_advance_along_with_the_provisionals()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var advanceKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            var advance = NewAdvance(contractKey, 0m);
            advance.Key = advanceKey;
            seed.FinancialDocuments.Add(advance);
            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(dbName))
            await CancelContractAsync(act, contractKey);

        await using var assert = NewContext(dbName);

        Assert.Equal(ContractStatus.Canceled,
            (await assert.PurchaseContracts.SingleAsync(x => x.Key == contractKey)).Status);

        var advanceAfter = await assert.FinancialDocuments.SingleAsync(x => x.Key == advanceKey);
        Assert.Equal(FinancialDocumentStatus.Canceled, advanceAfter.Status);
        Assert.Equal("Contrato cancelado", advanceAfter.CancellationReason);
    }

    [Fact]
    public async Task Refunding_the_advance_unblocks_the_contract_cancellation()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var advanceKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            seed.FinancialAccounts.Add(new FinancialAccount
            {
                Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl
            });

            var advance = NewAdvance(contractKey, 0m);
            advance.Key = advanceKey;
            seed.FinancialDocuments.Add(advance);
            await seed.SaveChangesAsync();

            // A baixa entra pelo serviço, para o ledger existir de verdade.
            await new FinancialDocumentsSettleService(new UnitOfWork(seed)).ExecuteAsync(
                advanceKey, "CX01", 50_000m, DateTime.Today, 0m, 0m, 0m, "OP-1", null, "tester");
        }

        await using (var refund = NewContext(dbName))
            await new FinancialAdvancesRefundService(new UnitOfWork(refund)).ExecuteAsync(
                advanceKey, "CX01", DateTime.Today, "TED-99", "produtor desistiu", "tester");

        await using (var act = NewContext(dbName))
            await CancelContractAsync(act, contractKey);

        await using var assert = NewContext(dbName);
        Assert.Equal(ContractStatus.Canceled,
            (await assert.PurchaseContracts.SingleAsync(x => x.Key == contractKey)).Status);
    }

    [Fact]
    public async Task Reversing_the_settlement_unblocks_the_contract_cancellation()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var advanceKey = Guid.NewGuid();
        Guid settlementKey;

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            seed.FinancialAccounts.Add(new FinancialAccount
            {
                Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl
            });

            var advance = NewAdvance(contractKey, 0m);
            advance.Key = advanceKey;
            seed.FinancialDocuments.Add(advance);
            await seed.SaveChangesAsync();

            var settlement = await new FinancialDocumentsSettleService(new UnitOfWork(seed)).ExecuteAsync(
                advanceKey, "CX01", 50_000m, DateTime.Today, 0m, 0m, 0m, "OP-1", null, "tester");

            settlementKey = settlement.Key;
        }

        await using (var reverse = NewContext(dbName))
            await new FinancialDocumentsReverseSettlementService(new UnitOfWork(reverse))
                .ExecuteAsync(settlementKey, "baixa errada", "tester");

        await using (var act = NewContext(dbName))
            await CancelContractAsync(act, contractKey);

        await using var assert = NewContext(dbName);
        Assert.Equal(ContractStatus.Canceled,
            (await assert.PurchaseContracts.SingleAsync(x => x.Key == contractKey)).Status);
    }

    /// <summary>
    /// Depois do revínculo, o contrato ANTIGO cancela e o contrato NOVO passa a ser protegido —
    /// as duas metades da mesma regra.
    /// </summary>
    [Fact]
    public async Task After_relinking_the_old_contract_cancels_and_the_new_one_blocks()
    {
        var dbName = Guid.NewGuid().ToString();
        var oldKey = Guid.NewGuid();
        var newKey = Guid.NewGuid();
        var advanceKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var oldContract = NewContract("PC-GUARD-001");
            oldContract.Key = oldKey;
            seed.PurchaseContracts.Add(oldContract);

            var newContract = NewContract("PC-GUARD-002");
            newContract.Key = newKey;
            seed.PurchaseContracts.Add(newContract);

            var advance = NewAdvance(oldKey, 50_000m);
            advance.Key = advanceKey;
            seed.FinancialDocuments.Add(advance);
            await seed.SaveChangesAsync();
        }

        await using (var relink = NewContext(dbName))
            await new FinancialAdvancesRelinkContractService(
                    new UnitOfWork(relink), new FinancialDocumentChangeLogService(relink))
                .ExecuteAsync(advanceKey, "Purchase", newKey, "tester");

        await using (var act = NewContext(dbName))
            await CancelContractAsync(act, oldKey);

        await using (var act = NewContext(dbName))
        {
            var error = await Assert.ThrowsAsync<ApplicationException>(
                () => CancelContractAsync(act, newKey));

            Assert.Contains("FN000217", error.Message);
        }

        await using var assert = NewContext(dbName);
        Assert.Equal(ContractStatus.Canceled,
            (await assert.PurchaseContracts.SingleAsync(x => x.Key == oldKey)).Status);
        Assert.Equal(ContractStatus.Approved,
            (await assert.PurchaseContracts.SingleAsync(x => x.Key == newKey)).Status);
    }

    /// <summary>
    /// Já cancelado não bloqueia: o guard filtra por Status != Canceled, senão um adiantamento
    /// devolvido (que fica Canceled com o saldo zerado) travaria o contrato para sempre.
    /// </summary>
    [Fact]
    public async Task A_canceled_advance_does_not_block()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            var advance = NewAdvance(contractKey, 50_000m);
            advance.Status = FinancialDocumentStatus.Canceled;
            seed.FinancialDocuments.Add(advance);
            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(dbName))
            await CancelContractAsync(act, contractKey);

        await using var assert = NewContext(dbName);
        Assert.Equal(ContractStatus.Canceled,
            (await assert.PurchaseContracts.SingleAsync(x => x.Key == contractKey)).Status);
    }
}
```

- [ ] **Step 2: Rodar o teste e ver falhar**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend"
dotnet test --filter FullyQualifiedName~FinancialDocumentsContractCancellationGuardServiceTests
```

Esperado: FALHA de COMPILAÇÃO — o guard, a fábrica `FinancialDocumentTestServices.CancellationGuard` e o 4º parâmetro de `PurchaseContractsCancelService` não existem.

- [ ] **Step 3: Escrever o guard**

Criar `SiagroB1.Application/Services/Financials/FinancialDocumentsContractCancellationGuardService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// "Este contrato pode ser cancelado, do ponto de vista do financeiro?"
///
/// A regra é financeira, então mora no financeiro; os dois serviços de cancelamento de contrato
/// a chamam ao lado do guard de movimento físico que já existe. Fecha a assimetria da Fase 1,
/// em que movimento de grão bloqueava o cancelamento e dinheiro pago não bloqueava nada.
///
/// NÃO vale para ENCERRAMENTO de contrato: encerrar é outro ato — o contrato foi cumprido, e
/// ali o adiantamento é matéria da amortização (Fase 2), não coisa a bloquear.
/// </summary>
public class FinancialDocumentsContractCancellationGuardService(IUnitOfWork db)
{
    public async Task EnsureCanCancelAsync(Guid? purchaseContractKey, Guid? salesContractKey)
    {
        var blocking = await db.Context.FinancialDocuments
            .AsNoTracking()
            .Where(x => x.Nature == FinancialDocumentNature.Advance &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        x.SettledAmount != 0m &&
                        ((purchaseContractKey != null && x.PurchaseContractKey == purchaseContractKey) ||
                         (salesContractKey != null && x.SalesContractKey == salesContractKey)))
            .Select(x => new { x.Code, x.SettledAmount })
            .ToListAsync();

        if (blocking.Count == 0) return;

        var titles = string.Join(", ", blocking.Select(x => $"{x.Code} (R$ {x.SettledAmount:N2})"));

        throw new ApplicationException(
            $"O contrato possui adiantamento pago: {titles}. Estorne a baixa, registre a " +
            "devolução do valor adiantado ou vincule o adiantamento a outro contrato antes de cancelar.");
    }
}
```

- [ ] **Step 4: Acrescentar `includeUnpaidAdvances` ao cancelamento em lote**

Em `SiagroB1.Application/Services/Financials/FinancialDocumentsCancelService.cs`, substituir o método `EnqueueCancelByContractAsync` **inteiro, junto com o XML-doc** (a prosa atual ficou errada: passa a ser o adiantamento PAGO que fica de fora, e ele é barrado pelo guard):

```csharp
    /// <summary>
    /// Enqueue-only: cancela TODOS os provisórios abertos de um contrato, e — quando
    /// <paramref name="includeUnpaidAdvances"/> é true — também os adiantamentos SEM baixa.
    ///
    /// Adiantamento não pago é só uma promessa e morre junto com o contrato. Adiantamento PAGO
    /// nunca é alcançado aqui: ele é barrado antes, por
    /// <see cref="FinancialDocumentsContractCancellationGuardService"/>, que exige estorno,
    /// devolução ou revínculo.
    ///
    /// Só os dois serviços de CANCELAMENTO passam true. Os de ENCERRAMENTO chamam este mesmo
    /// método e ficam com o padrão false: contrato encerrado foi cumprido, e ali o adiantamento
    /// é matéria da amortização da Fase 2.
    /// </summary>
    public async Task EnqueueCancelByContractAsync(
        Guid? purchaseContractKey, Guid? salesContractKey, string reason, string userName,
        bool includeUnpaidAdvances = false)
    {
        var documents = await Context.FinancialDocuments
            .Where(x => (x.Nature == FinancialDocumentNature.Provisional ||
                         (includeUnpaidAdvances &&
                          x.Nature == FinancialDocumentNature.Advance &&
                          x.SettledAmount == 0m)) &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        ((purchaseContractKey != null && x.PurchaseContractKey == purchaseContractKey) ||
                         (salesContractKey != null && x.SalesContractKey == salesContractKey)))
            .ToListAsync();

        foreach (var document in documents)
            Cancel(document, reason, userName);
    }
```

- [ ] **Step 5: Ligar o guard nos dois serviços de cancelamento de contrato**

Em `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCancelService.cs`. O guard roda **antes** de `contract.Status = Canceled`, junto do guard de `Allocations`: o serviço salva tudo num `SaveChangesAsync` só, então uma exceção depois da atribuição deixaria o contrato marcado em memória.

```csharp
public class PurchaseContractsCancelService(
    IUnitOfWork db,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsCancelService financialDocuments,
    FinancialDocumentsContractCancellationGuardService cancellationGuard)
{
    public async Task ExecuteAsync(Guid key, string? comments, string userName)
    {
        var contract = await db.Context.PurchaseContracts
                            .Include(x => x.Allocations)
                            .ThenInclude(a => a.StorageTransaction)
                            .FirstOrDefaultAsync(x => x.Key == key
                                                      && x.Status == ContractStatus.Approved) ?? 
                       throw new NotFoundException($"Contrato com a chave {key} não encontrado ou não está aprovado.");

        if (contract.Allocations.Any(x => 
                x.StorageTransaction?.TransactionStatus != StorageTransactionsStatus.Cancelled))
        {
            throw new ApplicationException("Contrato possui movimentos. Não é possivel cancelar, considere fazer washout.");
        }

        // Dinheiro tem o mesmo peso que grão: adiantamento pago barra o cancelamento. ANTES de
        // qualquer atribuição, para a operação inteira falhar sem efeito colateral.
        await cancellationGuard.EnsureCanCancelAsync(
            purchaseContractKey: contract.Key, salesContractKey: null);

        contract.Status = ContractStatus.Canceled;
        contract.ApprovalComments = comments;
        contract.CanceledAt = DateTime.Now;
        contract.CanceledBy = userName;

        notificationOutbox.Register(contract, NotificationEventType.Canceled, userName);

        await financialDocuments.EnqueueCancelByContractAsync(
            purchaseContractKey: contract.Key,
            salesContractKey: null,
            reason: "Contrato cancelado",
            userName: userName,
            includeUnpaidAdvances: true);

        await db.SaveChangesAsync();
    }
}
```

Fazer o espelho em `SiagroB1.Application/Services/SalesContracts/SalesContractsCancelService.cs` — mesma dependência no construtor, guard depois da checagem de `SalesInvoiceItems` e antes de `contract.Status = ContractStatus.Canceled`, e `includeUnpaidAdvances: true` na chamada existente:

```csharp
        await cancellationGuard.EnsureCanCancelAsync(
            purchaseContractKey: null, salesContractKey: contract.Key);
```

- [ ] **Step 6: Acrescentar a fábrica do guard aos helpers de teste**

Em `SiagroB1.Application.Tests/Support/FinancialDocumentTestServices.cs`, acrescentar dentro da classe:

```csharp
    public static FinancialDocumentsContractCancellationGuardService CancellationGuard(
        AppDbContext context) => new(new UnitOfWork(context));
```

- [ ] **Step 7: Atualizar os 3 call sites e o teste que a regra inverteu**

Em `SiagroB1.Application.Tests/Financials/FinancialDocumentUndoHooksTests.cs`, acrescentar o 4º argumento nos três construtores (linhas ~258, ~340 e ~445):

```csharp
            await new PurchaseContractsCancelService(db, TestNotificationOutbox.For(act),
                    FinancialDocumentTestServices.Cancel(act),
                    FinancialDocumentTestServices.CancellationGuard(act))
                .ExecuteAsync(contractKey, "washout", "tester");
```

```csharp
            await new SalesContractsCancelService(db, TestNotificationOutbox.For(act),
                    FinancialDocumentTestServices.Cancel(act),
                    FinancialDocumentTestServices.CancellationGuard(act))
                .ExecuteAsync(contractKey, "washout", "tester");
```

E **substituir inteiro** o teste `CancelingAContract_DoesNotTouchAnAdvanceLinkedToIt` — a regra que ele prendia foi revogada em 08/09/2026. Ele semeia um adiantamento com `SettledAmount = 1_000m` e afirma que o cancelamento passa e o adiantamento sobrevive; agora o cancelamento **falha**. Trocar o corpo por:

```csharp
    /// <summary>
    /// A regra ANTIGA (o adiantamento pago sobrevivia ao cancelamento) foi revogada em
    /// 08/09/2026: o adiantamento pago agora BARRA o cancelamento, e a saída é estornar,
    /// devolver ou revincular. O caso completo, com as três saídas, está em
    /// <see cref="FinancialDocumentsContractCancellationGuardServiceTests"/> — aqui fica só a
    /// âncora de que o gancho não cancela um adiantamento pago por baixo do guard.
    /// </summary>
    [Fact]
    public async Task CancelingAContract_IsBlockedByAPaidAdvanceAndLeavesItUntouched()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var advanceKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            var seededAdvance = NewProvisional(contractKey, Guid.NewGuid());
            seededAdvance.Key = advanceKey;
            seededAdvance.Nature = FinancialDocumentNature.Advance;
            seededAdvance.SettledAmount = 1_000m; // já pago
            seed.FinancialDocuments.Add(seededAdvance);

            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(dbName))
        {
            var db = new UnitOfWork(act);
            await Assert.ThrowsAsync<ApplicationException>(
                () => new PurchaseContractsCancelService(db, TestNotificationOutbox.For(act),
                        FinancialDocumentTestServices.Cancel(act),
                        FinancialDocumentTestServices.CancellationGuard(act))
                    .ExecuteAsync(contractKey, "washout", "tester"));
        }

        await using var assert = NewContext(dbName);

        Assert.Equal(ContractStatus.Approved,
            (await assert.PurchaseContracts.SingleAsync(x => x.Key == contractKey)).Status);

        var advance = await assert.FinancialDocuments.SingleAsync(x => x.Key == advanceKey);
        Assert.Equal(FinancialDocumentStatus.Open, advance.Status);
        Assert.Null(advance.CancellationReason);
    }
```

- [ ] **Step 8: Rodar a suíte inteira e ver passar**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend"
dotnet test
```

Esperado: PASS, com +24 testes novos das Tasks 1–3 sobre a contagem anterior. **Zero testes falhando** — se qualquer teste de ENCERRAMENTO (`Closing...`) falhar, o `includeUnpaidAdvances: true` foi parar num serviço de `Close`, o que o spec exclui explicitamente.

- [ ] **Step 9: Confirmar que o snapshot do EF segue limpo**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend"
dotnet ef migrations has-pending-model-changes --project SiagroB1.Infra --startup-project SiagroB1.Web
```

Esperado: "No changes have been made to the model since the last migration." Acrescentar valor a um enum cuja coluna já é `int` não gera migration; **se aparecer migration pendente, pare e reporte** — alguma coisa saiu do desenho.

- [ ] **Step 10: Stage (NÃO commitar)**

```bash
git -C "C:/Projetos/SiagroB1/siagro-b1-backend" add \
  SiagroB1.Application/Services/Financials/FinancialDocumentsContractCancellationGuardService.cs \
  SiagroB1.Application/Services/Financials/FinancialDocumentsCancelService.cs \
  SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCancelService.cs \
  SiagroB1.Application/Services/SalesContracts/SalesContractsCancelService.cs \
  SiagroB1.Application.Tests/Support/FinancialDocumentTestServices.cs \
  SiagroB1.Application.Tests/Financials/FinancialDocumentUndoHooksTests.cs \
  SiagroB1.Application.Tests/Financials/FinancialDocumentsContractCancellationGuardServiceTests.cs
```

---

### Task 4: Superfície OData — duas actions, duas controllers e o DI

**Files:**
- Create: `SiagroB1.Web/Actions/Financials/FinancialAdvancesRefundController.cs`
- Create: `SiagroB1.Web/Actions/Financials/FinancialAdvancesRelinkContractController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (bloco financeiro, depois de `financialAdvance`, linha ~954)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (bloco `// financials`, linha ~504)
- Test: `SiagroB1.Application.Tests/Financials/FinancialEdmModelTests.cs`

**Interfaces:**
- Consumes: `FinancialAdvancesRefundService.ExecuteAsync(...)` (Task 1), `FinancialAdvancesRelinkContractService.ExecuteAsync(...)` (Task 2), `FinancialDocumentsContractCancellationGuardService` (Task 3 — precisa de registro no DI porque é injetado nos dois serviços de cancelamento de contrato).
- Produces: as rotas `POST /odata/FinancialAdvancesRefund` e `POST /odata/FinancialAdvancesRelinkContract` — consumidas pelas Tasks 5 e 6.

- [ ] **Step 1: Escrever o teste que falha**

Abrir `SiagroB1.Application.Tests/Financials/FinancialEdmModelTests.cs` e acrescentar os dois testes abaixo dentro da classe. O helper `BuildModel()` já existe no arquivo (monta um `ODataConventionModelBuilder` com `Namespace = "SIAGROB1"`, chama `ConfigureODataEntities()` e devolve `GetEdmModel()`) e os `using` de `Microsoft.OData.Edm` também — nada novo a importar:

```csharp
    [Fact]
    public void The_advance_refund_action_declares_its_parameters()
    {
        var model = BuildModel();

        var action = model.SchemaElements.OfType<IEdmAction>()
            .Single(x => x.Name == "FinancialAdvancesRefund");

        Assert.Contains(action.Parameters, p => p.Name == "DocumentKey");
        Assert.Contains(action.Parameters, p => p.Name == "FinancialAccountCode");
        Assert.Contains(action.Parameters, p => p.Name == "RefundDate");
        Assert.Contains(action.Parameters, p => p.Name == "DocumentReference");
        Assert.Contains(action.Parameters, p => p.Name == "Reason");
    }

    [Fact]
    public void The_advance_relink_action_declares_its_parameters()
    {
        var model = BuildModel();

        var action = model.SchemaElements.OfType<IEdmAction>()
            .Single(x => x.Name == "FinancialAdvancesRelinkContract");

        Assert.Contains(action.Parameters, p => p.Name == "DocumentKey");
        Assert.Contains(action.Parameters, p => p.Name == "ContractType");
        Assert.Contains(action.Parameters, p => p.Name == "ContractKey");
    }
```

- [ ] **Step 2: Rodar o teste e ver falhar**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend"
dotnet test --filter FullyQualifiedName~FinancialEdmModelTests
```

Esperado: FALHA — `Single(...)` não encontra as duas actions.

- [ ] **Step 3: Declarar as actions no EDM**

Em `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, logo depois de `financialAdvance.Returns<IActionResult>();`:

```csharp
        // As três saídas de um adiantamento pago cujo contrato alguém quer cancelar: estornar a
        // baixa (FinancialDocumentsReverseSettlement, acima), devolver e revincular.
        // Nenhuma das duas recebe DINHEIRO como parâmetro: o valor devolvido é sempre o saldo
        // baixado, lido no servidor.
        var financialAdvanceRefund = modelBuilder.Action("FinancialAdvancesRefund");
        financialAdvanceRefund.Parameter<Guid>("DocumentKey");
        financialAdvanceRefund.Parameter<string>("FinancialAccountCode");
        financialAdvanceRefund.Parameter<string>("RefundDate");
        financialAdvanceRefund.Parameter<string>("DocumentReference").Optional();
        financialAdvanceRefund.Parameter<string>("Reason");
        financialAdvanceRefund.Returns<IActionResult>();

        var financialAdvanceRelink = modelBuilder.Action("FinancialAdvancesRelinkContract");
        financialAdvanceRelink.Parameter<Guid>("DocumentKey");
        financialAdvanceRelink.Parameter<string>("ContractType");   // "Purchase" | "Sales"
        financialAdvanceRelink.Parameter<Guid>("ContractKey");
        financialAdvanceRelink.Returns<IActionResult>();
```

- [ ] **Step 4: Escrever as duas controllers**

Criar `SiagroB1.Web/Actions/Financials/FinancialAdvancesRefundController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>
/// Devolução do valor adiantado. O VALOR não vem da tela: é sempre o saldo baixado, lido no
/// servidor.
/// </summary>
public class FinancialAdvancesRefundController(FinancialAdvancesRefundService service) : ODataController
{
    [HttpPost("odata/FinancialAdvancesRefund")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // ODataActionParameters chega NULL quando o corpo não casa com nenhum parâmetro do EDM.
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("DocumentKey", out var keyObj) || keyObj is null)
                return BadRequest("Informe o adiantamento a devolver.");

            if (!parameters.TryGetValue("FinancialAccountCode", out var accountObj) || accountObj is null)
                return BadRequest("Informe a conta financeira que recebeu a devolução.");

            if (!parameters.TryGetValue("RefundDate", out var dateObj) || dateObj is null)
                return BadRequest("Informe a data da devolução.");

            // TryGetValue devolve TRUE com valor null em parâmetro opcional.
            parameters.TryGetValue("DocumentReference", out var referenceObj);
            parameters.TryGetValue("Reason", out var reasonObj);

            await service.ExecuteAsync(
                documentKey: Guid.Parse(keyObj.ToString()!),
                financialAccountCode: accountObj.ToString()!,
                refundDate: DateTime.Parse(dateObj.ToString()!),
                documentReference: referenceObj?.ToString(),
                reason: reasonObj?.ToString() ?? string.Empty,
                userName: User.Identity?.Name ?? "Unknown");

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

Criar `SiagroB1.Web/Actions/Financials/FinancialAdvancesRelinkContractController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>Vincula o adiantamento a outro contrato do MESMO parceiro.</summary>
public class FinancialAdvancesRelinkContractController(
    FinancialAdvancesRelinkContractService service) : ODataController
{
    [HttpPost("odata/FinancialAdvancesRelinkContract")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("DocumentKey", out var keyObj) || keyObj is null)
                return BadRequest("Informe o adiantamento.");

            if (!parameters.TryGetValue("ContractType", out var typeObj) || typeObj is null)
                return BadRequest("Informe o tipo do contrato de destino.");

            if (!parameters.TryGetValue("ContractKey", out var contractObj) || contractObj is null)
                return BadRequest("Informe o contrato de destino.");

            await service.ExecuteAsync(
                documentKey: Guid.Parse(keyObj.ToString()!),
                targetContractType: typeObj.ToString()!,
                targetContractKey: Guid.Parse(contractObj.ToString()!),
                userName: User.Identity?.Name ?? "Unknown");

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

- [ ] **Step 5: Registrar os TRÊS serviços no DI**

Em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, no fim do bloco `// financials` (depois de `FinancialDocumentsGenerateBacklogService`):

```csharp
        services.AddScoped<FinancialAdvancesRefundService>();
        services.AddScoped<FinancialAdvancesRelinkContractService>();
        // O guard é injetado nos dois serviços de cancelamento de CONTRATO, então precisa de
        // registro — ao contrário de FinancialDocumentsSettlementGuardService, que é estático e
        // deliberadamente não registrado.
        services.AddScoped<FinancialDocumentsContractCancellationGuardService>();
```

- [ ] **Step 6: Rodar build + suíte inteira**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend"
dotnet build SiagroB1.sln
dotnet test
```

Esperado: build sem erro e todos os testes passando, incluindo os dois novos do `FinancialEdmModelTests`.

- [ ] **Step 7: Conferir que o DI resolve de verdade**

Subir a Web e ler o `$metadata` — é o gate barato que pega tanto o registro faltando quanto a action não declarada:

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend"
dotnet run --project SiagroB1.Web --launch-profile yktb
```

Em outro terminal:

```bash
curl -s "http://localhost:50000/odata/\$metadata" | grep -o "FinancialAdvancesRe[a-zA-Z]*"
```

Esperado: `FinancialAdvancesRefund` e `FinancialAdvancesRelinkContract` aparecem. Derrubar a Web em seguida.

- [ ] **Step 8: Stage (NÃO commitar)**

```bash
git -C "C:/Projetos/SiagroB1/siagro-b1-backend" add \
  SiagroB1.Web/Actions/Financials/FinancialAdvancesRefundController.cs \
  SiagroB1.Web/Actions/Financials/FinancialAdvancesRelinkContractController.cs \
  SiagroB1.Web/ODataConfig/ODataConfigurations.cs \
  SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs \
  SiagroB1.Application.Tests/Financials/FinancialEdmModelTests.cs
```

---

### Task 5: Tela — devolver adiantamento

**Repo: `C:\Projetos\SiagroB1\siagro-b1-frontend`.** Nenhuma tela nova, nenhuma rota nova, nenhuma migration de menu: tudo no detalhe do título que já existe.

**Files:**
- Create: `webapp/view/financialDocuments/fragments/RefundAdvanceDialog.fragment.xml`
- Modify: `webapp/view/financialDocuments/Detail.view.xml` (rodapé)
- Modify: `webapp/controller/financialDocuments/Detail.controller.ts`
- Modify: `webapp/model/ServerRoutes.ts`

**Interfaces:**
- Consumes: `POST /odata/FinancialAdvancesRefund` com `DocumentKey`, `FinancialAccountCode`, `RefundDate`, `DocumentReference`, `Reason` (Task 4); `DialogHelper.createDialog(controller, fragmentName) : Promise<Dialog>`; o handler `openFinancialAccountsValueHelp` que já existe neste controller.
- Produces: `api.financialAdvancesRefund` e `api.financialAdvancesRelinkContract` (a segunda é usada pela Task 6); handlers `onRefundAdvance`, `onCloseRefundDialog`, `onConfirmRefund`; helper privado `lastSettlementAccount()`.

- [ ] **Step 1: Acrescentar as rotas das actions**

Em `webapp/model/ServerRoutes.ts`, no bloco `// financeiro`, depois de `financialAdvancesCreate`:

```ts
  financialAdvancesRefund: '/FinancialAdvancesRefund(...)',
  financialAdvancesRelinkContract: '/FinancialAdvancesRelinkContract(...)',
```

As duas entram juntas — a segunda é consumida pela Task 6.

- [ ] **Step 2: Escrever o fragmento do diálogo**

Criar `webapp/view/financialDocuments/fragments/RefundAdvanceDialog.fragment.xml`. **Nenhum `--` na prosa dos comentários**: XML proíbe, e `Fragment.load` devolveria nulls em silêncio.

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:f="sap.ui.layout.form"
    xmlns:core="sap.ui.core"
>
<Dialog id="financialRefundAdvanceDialog" title="Devolver adiantamento">
  <content>
    <VBox class="sapUiSmallMargin" width="560px">
      <MessageStrip
        text="A devolução é sempre pelo valor INTEIRO já pago. O título fica cancelado e o crédito do adiantamento deixa de existir."
        type="Warning"
        showIcon="true"
        class="sapUiSmallMarginBottom"/>
      <f:Form editable="true">
        <f:layout>
          <f:ColumnLayout columnsM="2" columnsL="2" columnsXL="2"/>
        </f:layout>
        <f:formContainers>
          <f:FormContainer>
            <f:formElements>
              <f:FormElement label="Conta financeira">
                <f:fields>
                  <Input
                    value="{viewModel>/refundDialog/financialAccountCode}"
                    required="true"
                    showValueHelp="true"
                    valueHelpOnly="true"
                    valueHelpRequest=".openFinancialAccountsValueHelp"/>
                </f:fields>
              </f:FormElement>
              <f:FormElement label="Data da devolução">
                <f:fields>
                  <DatePicker
                    value="{viewModel>/refundDialog/refundDate}"
                    required="true"
                    displayFormat="dd/MM/yyyy"
                    valueFormat="yyyy-MM-dd"/>
                </f:fields>
              </f:FormElement>
              <f:FormElement label="Valor a devolver">
                <f:fields>
                  <Text text="{viewModel>/refundDialog/amountText}"/>
                </f:fields>
              </f:FormElement>
              <f:FormElement label="Documento">
                <f:fields>
                  <Input value="{viewModel>/refundDialog/documentReference}" maxLength="50"/>
                </f:fields>
              </f:FormElement>
              <f:FormElement label="Motivo">
                <f:fields>
                  <TextArea
                    value="{viewModel>/refundDialog/reason}"
                    required="true"
                    rows="3" width="100%" maxLength="500"/>
                </f:fields>
              </f:FormElement>
            </f:formElements>
          </f:FormContainer>
        </f:formContainers>
      </f:Form>
    </VBox>
  </content>
  <footer>
    <OverflowToolbar>
      <ToolbarSpacer />
      <Button text="Devolver" type="Emphasized" press=".onConfirmRefund"/>
      <Button text="Fechar" press=".onCloseRefundDialog"/>
    </OverflowToolbar>
  </footer>
</Dialog>
</core:FragmentDefinition>
```

O valor é `Text`, não `Input`: quem decide o valor é o servidor.

- [ ] **Step 3: Acrescentar o botão ao rodapé**

Em `webapp/view/financialDocuments/Detail.view.xml`, dentro de `<uxap:footer><OverflowToolbar>`, antes do botão "Cancelar título":

```xml
        <Button
          text="Devolver adiantamento"
          icon="sap-icon://undo"
          visible="{= ${path: 'Nature', targetType: 'any'} === 'Advance'
                    &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Canceled'
                    &amp;&amp; ${path: 'SettledAmount', targetType: 'any'} > 0 }"
          press=".onRefundAdvance" />
```

`targetType: 'any'` é obrigatório em cada parte: sem ele o enum chega formatado e a comparação nunca casa. `&&` em XML tem de ser `&amp;&amp;`.

- [ ] **Step 4: Escrever os handlers**

Em `webapp/controller/financialDocuments/Detail.controller.ts`, acrescentar o import que falta junto dos existentes:

```ts
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
```

E os handlers, antes do fechamento da classe (`Table`, `Dialog`, `JSONModel`, `MessageBox`, `MessageToast`, `ODataModel`, `DialogHelper` e `formatter` já estão importados):

```ts
  private _refundDialog: Dialog;

  /**
   * Conta financeira sugerida: a da última baixa do título. É quase sempre a conta de onde o
   * dinheiro saiu, então poupa um value help no caso comum sem impedir a troca.
   */
  private lastSettlementAccount(): string {
    const table = this.byId("financialSettlementsTable") as Table;
    const contexts = (table?.getBinding("rows") as ODataListBinding | undefined)
      ?.getCurrentContexts() ?? [];

    for (let i = contexts.length - 1; i >= 0; i--) {
      const code = contexts[i].getProperty("FinancialAccountCode") as string;
      if (code) return code;
    }

    return "";
  }

  async onRefundAdvance(): Promise<void> {
    const ctx = this.documentContext();
    if (!ctx) {
      MessageBox.error("Título não carregado.");
      return;
    }

    const settled = Number(ctx.getProperty("SettledAmount") ?? 0);

    if (!(settled > 0)) {
      MessageBox.warning("O adiantamento não tem valor pago: não há o que devolver.");
      return;
    }

    (this.getModel("viewModel") as JSONModel).setProperty("/refundDialog", {
      financialAccountCode: this.lastSettlementAccount(),
      refundDate: new Date().toISOString().substring(0, 10),
      amountText: formatter.formatDecimal(settled, 2),
      documentReference: "",
      reason: "",
    });

    this._refundDialog ??= await DialogHelper.createDialog(
      this, "siagrob1.view.financialDocuments.fragments.RefundAdvanceDialog");
    this._refundDialog.open();
  }

  onCloseRefundDialog(): void {
    this._refundDialog?.close();
  }

  async onConfirmRefund(): Promise<void> {
    const ctx = this.documentContext();
    if (!ctx) return;

    const form = (this.getModel("viewModel") as JSONModel)
      .getProperty("/refundDialog") as {
        financialAccountCode: string; refundDate: string;
        documentReference: string; reason: string;
      };

    if (!form.financialAccountCode) {
      MessageBox.alert("Informe a conta financeira que recebeu a devolução.");
      return;
    }

    if (!form.refundDate) {
      MessageBox.alert("Informe a data da devolução.");
      return;
    }

    if (!form.reason?.trim()) {
      MessageBox.alert("Informe o motivo da devolução.");
      return;
    }

    this.onCloseRefundDialog();

    try {
      this.setBusy(true);
      const action = (this.getModel() as ODataModel).bindContext(this.api.financialAdvancesRefund);
      action.setParameter("DocumentKey", ctx.getProperty("Key"));
      action.setParameter("FinancialAccountCode", form.financialAccountCode);
      action.setParameter("RefundDate", form.refundDate);
      action.setParameter("DocumentReference", form.documentReference ?? "");
      action.setParameter("Reason", form.reason);
      await action.invoke();

      MessageToast.show("Devolução registrada. O título foi cancelado.");
      this.refreshDocument();
    } catch (err) {
      MessageBox.error((err as Error).message || "Erro ao registrar a devolução.");
    } finally {
      this.setBusy(false);
    }
  }
```

- [ ] **Step 5: Rodar os gates do frontend**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-frontend"
yarn ts-typecheck
yarn lint
npx ui5lint
```

Esperado: os três limpos, sem regra nova. **Nenhum destes gates prova que o fragmento carrega** — isso é a Task 7.

Não rode `yarn test`: o gate de cobertura de 50% falha contra os ~2,4% reais do projeto e não diz nada sobre esta mudança.

- [ ] **Step 6: Stage (NÃO commitar)**

```bash
git -C "C:/Projetos/SiagroB1/siagro-b1-frontend" add \
  webapp/view/financialDocuments/fragments/RefundAdvanceDialog.fragment.xml \
  webapp/view/financialDocuments/Detail.view.xml \
  webapp/controller/financialDocuments/Detail.controller.ts \
  webapp/model/ServerRoutes.ts
```

---

### Task 6: Tela — vincular o adiantamento a outro contrato

**Repo: `C:\Projetos\SiagroB1\siagro-b1-frontend`.**

**Files:**
- Create: `webapp/view/financialDocuments/fragments/RelinkContractDialog.fragment.xml`
- Modify: `webapp/view/financialDocuments/Detail.view.xml` (rodapé)
- Modify: `webapp/controller/financialDocuments/Detail.controller.ts`
- Modify: `webapp/model/formatter.ts` (`formatFinancialChangeLogField`, linha ~1004)

**Interfaces:**
- Consumes: `POST /odata/FinancialAdvancesRelinkContract` com `DocumentKey`, `ContractType`, `ContractKey` (Task 4); `api.financialAdvancesRelinkContract` (declarado na Task 5, Step 1); `DialogHelper.openTableSelectDialog(controller, name, filters, defaultFilters?, elementPath?) : Promise<Context | undefined>` — resolve `undefined` no cancelar; o campo `Field` do log vem com o código `"Contract"` (Task 2).
- Produces: handlers `onRelinkContract`, `onCloseRelinkDialog`, `openRelinkContractValueHelp`, `onConfirmRelink`.

- [ ] **Step 1: Traduzir o campo novo do log**

Em `webapp/model/formatter.ts`, dentro de `formatFinancialChangeLogField`:

```ts
  formatFinancialChangeLogField: (value: string) => {
    const m = new Map<string, string>();
    m.set("DueDate", "Vencimento");
    m.set("Comments", "Comentários");
    m.set("Contract", "Contrato");

    return m.get(value) ?? value;
  },
```

- [ ] **Step 2: Escrever o fragmento do diálogo**

Criar `webapp/view/financialDocuments/fragments/RelinkContractDialog.fragment.xml`. O tipo de contrato **não é editável**: a direção do título já o determina.

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:f="sap.ui.layout.form"
    xmlns:core="sap.ui.core"
>
<Dialog id="financialRelinkContractDialog" title="Vincular a outro contrato">
  <content>
    <VBox class="sapUiSmallMargin" width="520px">
      <MessageStrip
        text="O adiantamento migra INTEIRO. O contrato de destino precisa ser do mesmo parceiro, da mesma filial, na mesma moeda e estar aprovado."
        type="Information"
        showIcon="true"
        class="sapUiSmallMarginBottom"/>
      <f:Form editable="true">
        <f:layout>
          <f:ColumnLayout columnsM="1" columnsL="1" columnsXL="1"/>
        </f:layout>
        <f:formContainers>
          <f:FormContainer>
            <f:formElements>
              <f:FormElement label="Contrato atual">
                <f:fields>
                  <Text text="{viewModel>/relinkDialog/currentContractCode}"/>
                </f:fields>
              </f:FormElement>
              <f:FormElement label="Novo contrato">
                <f:fields>
                  <Input
                    value="{viewModel>/relinkDialog/contractCode}"
                    required="true"
                    showValueHelp="true"
                    valueHelpOnly="true"
                    valueHelpRequest=".openRelinkContractValueHelp"/>
                </f:fields>
              </f:FormElement>
            </f:formElements>
          </f:FormContainer>
        </f:formContainers>
      </f:Form>
    </VBox>
  </content>
  <footer>
    <OverflowToolbar>
      <ToolbarSpacer />
      <Button text="Vincular" type="Emphasized" press=".onConfirmRelink"/>
      <Button text="Fechar" press=".onCloseRelinkDialog"/>
    </OverflowToolbar>
  </footer>
</Dialog>
</core:FragmentDefinition>
```

- [ ] **Step 3: Acrescentar o botão ao rodapé**

Em `webapp/view/financialDocuments/Detail.view.xml`, logo depois do botão "Devolver adiantamento". Este fica visível enquanto não cancelado, mesmo sem baixa — a troca está disponível sempre no detalhe do adiantamento, não só durante o cancelamento:

```xml
        <Button
          text="Vincular a outro contrato"
          icon="sap-icon://chain-link"
          visible="{= ${path: 'Nature', targetType: 'any'} === 'Advance'
                    &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Canceled' }"
          press=".onRelinkContract" />
```

- [ ] **Step 4: Escrever os handlers**

Em `webapp/controller/financialDocuments/Detail.controller.ts`, antes do fechamento da classe (`Filter`, `FilterOperator`, `DialogHelper` e `Context` já estão importados):

```ts
  private _relinkDialog: Dialog;

  async onRelinkContract(): Promise<void> {
    const ctx = this.documentContext();
    if (!ctx) {
      MessageBox.error("Título não carregado.");
      return;
    }

    (this.getModel("viewModel") as JSONModel).setProperty("/relinkDialog", {
      currentContractCode: (ctx.getProperty("OriginDocNumber") as string) || "(sem contrato)",
      contractCode: "",
      contractKey: null,
    });

    this._relinkDialog ??= await DialogHelper.createDialog(
      this, "siagrob1.view.financialDocuments.fragments.RelinkContractDialog");
    this._relinkDialog.open();
  }

  onCloseRelinkDialog(): void {
    this._relinkDialog?.close();
  }

  /**
   * Value help do contrato de destino.
   *
   * O diálogo é escolhido pela DIREÇÃO do título, não por um campo de tela: a pagar migra para
   * contrato de compra, a receber para contrato de venda, e a action recusa o cruzado.
   *
   * O filtro de parceiro vai como defaultFilters porque muda a cada abertura. CardCode é STRING,
   * então Filter é seguro aqui; o status Approved continua no $filter estático de cada
   * fragmento, porque filtro de enum montado como objeto estoura "Unsupported type" no UI5.
   */
  async openRelinkContractValueHelp(): Promise<void> {
    const ctx = this.documentContext();
    if (!ctx) return;

    const isPayable = ctx.getProperty("Direction") === "Payable";
    const dialogName = isPayable
      ? "PurchaseContractsApprovedSelectDialog"
      : "SalesContractsSelectDialog";

    const cardCode = ctx.getProperty("CardCode") as string;

    const oSelected = await DialogHelper.openTableSelectDialog(
      this, dialogName, ["Code", "CardName"],
      [new Filter("CardCode", FilterOperator.EQ, cardCode)]);

    if (!oSelected) return;

    const viewModel = this.getModel("viewModel") as JSONModel;
    viewModel.setProperty("/relinkDialog/contractCode", oSelected.getProperty("Code") as string);
    viewModel.setProperty("/relinkDialog/contractKey", oSelected.getProperty("Key") as string);
  }

  async onConfirmRelink(): Promise<void> {
    const ctx = this.documentContext();
    if (!ctx) return;

    const form = (this.getModel("viewModel") as JSONModel)
      .getProperty("/relinkDialog") as { contractCode: string; contractKey: string };

    if (!form.contractKey) {
      MessageBox.alert("Selecione o contrato de destino.");
      return;
    }

    this.onCloseRelinkDialog();

    try {
      this.setBusy(true);
      const action = (this.getModel() as ODataModel)
        .bindContext(this.api.financialAdvancesRelinkContract);
      action.setParameter("DocumentKey", ctx.getProperty("Key"));
      action.setParameter("ContractType",
        ctx.getProperty("Direction") === "Payable" ? "Purchase" : "Sales");
      action.setParameter("ContractKey", form.contractKey);
      await action.invoke();

      MessageToast.show(`Adiantamento vinculado ao contrato ${form.contractCode}.`);
      this.refreshDocument();
    } catch (err) {
      MessageBox.error((err as Error).message || "Erro ao vincular o adiantamento.");
    } finally {
      this.setBusy(false);
    }
  }
```

- [ ] **Step 5: Rodar os gates do frontend**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-frontend"
yarn ts-typecheck
yarn lint
npx ui5lint
```

Esperado: os três limpos.

- [ ] **Step 6: Stage (NÃO commitar)**

```bash
git -C "C:/Projetos/SiagroB1/siagro-b1-frontend" add \
  webapp/view/financialDocuments/fragments/RelinkContractDialog.fragment.xml \
  webapp/view/financialDocuments/Detail.view.xml \
  webapp/controller/financialDocuments/Detail.controller.ts \
  webapp/model/formatter.ts
```

---

### Task 7: Verificação de ponta a ponta no navegador

Gate verde não prova que a tela funciona: na Fase 1 um `--` em comentário XML matou um value help inteiro com ts-typecheck, eslint e ui5lint todos passando. **A entrega só está pronta depois desta task.**

**Files:** nenhum, a menos que a verificação ache defeito — aí a correção volta para a task correspondente.

- [ ] **Step 1: Subir a stack**

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-backend"
dotnet run --project SiagroB1.Web --launch-profile yktb
```

Em outro terminal:

```bash
cd "C:/Projetos/SiagroB1/siagro-b1-frontend"
yarn start:dev
```

O login é feito pelo usuário (`admin`). Derrubar a stack ao fim da verificação.

- [ ] **Step 2: Preparar um adiantamento pago**

Ir em **Financeiro → Adiantamentos**, incluir um adiantamento sobre um contrato de compra APROVADO da filial 3, e dar baixa nele pelo detalhe do título (conta `CX01` ou outra ativa). Anotar o código do título (`FN…`) e o do contrato. Repetir para ter um segundo adiantamento pago (o do revínculo).

Se a lista de adiantamentos estiver vazia e nenhum contrato aparecer no value help, rode antes a action `FinancialDocumentsGenerateBacklog` — na Fase 1 as tabelas nasceram vazias e sem esse passo nada é testável.

- [ ] **Step 3: Percorrer os 9 itens**

1. No detalhe do adiantamento pago, os botões **"Devolver adiantamento"** e **"Vincular a outro contrato"** aparecem no rodapé.
2. Abrir um título PROVISÓRIO (natureza Compromisso): **nenhum dos dois botões aparece**.
3. "Devolver adiantamento" abre o diálogo com a conta da última baixa já preenchida, a data de hoje, e o valor a devolver igual ao pago. **Se o diálogo não abrir e o console acusar `null`, procure `--` em comentário XML do fragmento.**
4. Confirmar sem motivo: alerta "Informe o motivo da devolução." e nada é enviado.
5. Confirmar com motivo: toast, e ao voltar o título está **Cancelado**, com o saldo baixado zerado, e a aba **Baixas** mostra a linha negativa com a origem da devolução.
6. Depois da devolução, os dois botões somem (o título está cancelado) e o "Cancelar título" também, pela regra que já existia.
7. No segundo adiantamento pago, "Vincular a outro contrato": o value help abre **filtrado pelo parceiro do título** — só contratos daquele produtor, e só aprovados.
8. Confirmar o vínculo: toast com o código do novo contrato; ao recarregar, o campo de origem mostra o contrato novo e a aba **Log de Alterações** tem uma linha com campo **"Contrato"** (traduzido, não `Contract`), valor anterior e valor novo, e a coluna "Quando" preenchida.
9. Tentar **cancelar o contrato ANTIGO** (Contratos de Compra → cancelar): passa. Tentar cancelar o contrato **NOVO**: MessageBox com a mensagem do guard, nomeando o código do título e as três saídas; o contrato continua Aprovado.

- [ ] **Step 4: Registrar o resultado**

Anotar cada achado com tela, sintoma e causa. Achado de frontend volta para a Task 5 ou 6; achado de backend volta para a Task 1, 2 ou 3, **com teste antes da correção**. Re-verificar na tela depois de corrigir — não basta o teste voltar a passar.

- [ ] **Step 5: Stage do que a verificação corrigiu (NÃO commitar)**

```bash
git -C "C:/Projetos/SiagroB1/siagro-b1-backend" status --short
git -C "C:/Projetos/SiagroB1/siagro-b1-frontend" status --short
```

Conferir que **nada** aparece como untracked (`??`) nos dois repos, e reportar ao usuário o conjunto staged para que ele faça os dois commits — um por repo.

---

## Fora de escopo (decidido no spec, não esquecido)

- **Devolução parcial e troca parcial** — devolver é devolver tudo; o vínculo migra o adiantamento inteiro.
- **Guard no ENCERRAMENTO de contrato** — `PurchaseContractsCloseService` e `SalesContractsCloseService` não mudam de comportamento.
- **Amortização do adiantamento contra o documento firme** — continua Fase 2.
