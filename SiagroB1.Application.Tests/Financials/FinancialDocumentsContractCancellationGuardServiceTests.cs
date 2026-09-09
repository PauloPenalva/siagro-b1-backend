using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
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

    private static SalesContract NewSalesContract(string code = "SC-GUARD-001") => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        TotalVolume = 1_000m,
        Price = 3m,
        Type = ContractType.Fixed,
        StandardCashFlowDate = new DateTime(2026, 12, 31),
        StandardCurrency = CurrencyType.Brl,
        Status = ContractStatus.Approved,
    };

    private static FinancialDocument NewSalesAdvance(Guid contractKey, decimal settled) => new()
    {
        Key = Guid.NewGuid(),
        Code = "FN000218",
        CardCode = "C0001",
        Direction = FinancialDirection.Receivable,
        Nature = FinancialDocumentNature.Advance,
        Status = settled > 0m ? FinancialDocumentStatus.Settled : FinancialDocumentStatus.Open,
        DueDate = new DateTime(2026, 12, 31),
        NetAmount = 50_000m,
        SettledAmount = settled,
        Currency = CurrencyType.Brl,
        OriginType = FinancialDocumentOrigin.Manual,
        OriginDocNumber = "SC-GUARD-001",
        SalesContractKey = contractKey,
    };

    private static Task CancelSalesContractAsync(AppDbContext context, Guid contractKey) =>
        new SalesContractsCancelService(
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

    /// <summary>
    /// Espelho do teste de compra acima, do lado VENDA — prende o leg <c>salesContractKey</c> do
    /// LINQ do guard e o <c>includeUnpaidAdvances: true</c> de <see cref="SalesContractsCancelService"/>,
    /// que nenhum outro teste deste arquivo exercita.
    /// </summary>
    [Fact]
    public async Task Canceling_a_sales_contract_with_a_paid_advance_fails_and_leaves_the_contract_approved()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewSalesContract();
            contract.Key = contractKey;
            seed.SalesContracts.Add(contract);
            seed.FinancialDocuments.Add(NewSalesAdvance(contractKey, 50_000m));
            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(dbName))
        {
            var error = await Assert.ThrowsAsync<ApplicationException>(
                () => CancelSalesContractAsync(act, contractKey));

            Assert.Contains("FN000218", error.Message);
        }

        await using var assert = NewContext(dbName);
        Assert.Equal(ContractStatus.Approved,
            (await assert.SalesContracts.SingleAsync(x => x.Key == contractKey)).Status);
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
