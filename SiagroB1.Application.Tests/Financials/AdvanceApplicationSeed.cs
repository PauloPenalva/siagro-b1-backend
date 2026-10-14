using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Cenário da compensação de adiantamento (spec 2026-10-14): o <see cref="FirmPayableSeed"/> (contrato PC-1 do F0001,
/// provisório FN-PROV-1, conta CX01) com o firme ST-0001 de 19.728,33 COMPOSTO no provisório — disponível para baixa =
/// 19.728,33 — e o adiantamento FN-ADT-1 do mesmo contrato, pago pela baixa REAL (linha Manual na CX01).
/// </summary>
internal sealed class AdvanceApplicationSeed
{
    public FirmPayableSeed Firm { get; }
    public UnitOfWork Db => Firm.Db;
    public AppDbContext Context => Firm.Context;
    public PurchaseContract Contract => Firm.Contract;
    public FinancialDocument Provisional => Firm.Provisional;
    public FinancialDocument ComposedFirm { get; private set; } = null!;
    public FinancialDocument Advance { get; private set; } = null!;

    private AdvanceApplicationSeed(FirmPayableSeed firm) => Firm = firm;

    public static async Task<AdvanceApplicationSeed> CreateAsync(
        decimal provisionalAmount = 100_000m, decimal advanceAmount = 10_000m, decimal advancePaid = 10_000m)
    {
        var firm = await FirmPayableSeed.CreateAsync(provisionalAmount);
        var seed = new AdvanceApplicationSeed(firm);
        var conciliation = await firm.ConciliateAsync();
        seed.ComposedFirm = conciliation.Firm!;
        await seed.ComposeAsync(firm.Provisional, conciliation.Firm!);
        seed.Advance = await seed.AddAdvanceAsync(firm.Contract, advanceAmount, advancePaid, "FN-ADT-1");
        return seed;
    }

    public Task ComposeAsync(FinancialDocument provisional, FinancialDocument firm) =>
        new FinancialDocumentsComposeService(Db, new FinancialDocumentChangeLogService(Context))
            .ExecuteAsync(provisional.Key, [firm.Key], "ana");

    public async Task<FinancialDocument> AddAdvanceAsync(
        PurchaseContract contract, decimal amount, decimal paid, string code, CurrencyType currency = CurrencyType.Brl)
    {
        var advance = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = code, CardCode = FiscalReconciliationSeed.Supplier, CardName = "PRODUTOR TESTE",
            Direction = FinancialDirection.Payable, Nature = FinancialDocumentNature.Advance,
            Status = FinancialDocumentStatus.Open, DueDate = new DateTime(2026, 12, 31), NetAmount = amount,
            OriginType = FinancialDocumentOrigin.Manual, OriginDocNumber = contract.Code,
            PurchaseContractKey = contract.Key, BranchCode = contract.BranchCode, Currency = currency,
            PaymentTermsText = "Depósito no Banco X",
        };
        Context.FinancialDocuments.Add(advance);
        await Context.SaveChangesAsync();

        if (paid > 0m)
            await PayAsync(advance, paid);

        return advance;
    }

    public Task PayAsync(FinancialDocument advance, decimal amount) =>
        new FinancialDocumentsSettleService(Db).ExecuteAsync(
            advance.Key, "CX01", amount, new DateTime(2026, 10, 1), 0m, 0m, 0m, "TED-1", null, "ana");

    public FinancialDocumentsApplyAdvanceService Apply() => new(Db, new FinancialDocumentChangeLogService(Context));

    public Task ApplyAsync(decimal amount, FinancialDocument? advance = null, FinancialDocument? provisional = null) =>
        Apply().ExecuteAsync((provisional ?? Provisional).Key, (advance ?? Advance).Key, amount,
            new DateTime(2026, 10, 14), "ana");

    public FinancialDocumentsReverseSettlementService Reverse() => new(Db);

    public Task<FinancialSettlement> LineAsync(Guid documentKey, FinancialSettlementOrigin origin) =>
        Context.FinancialSettlements.AsNoTracking()
            .SingleAsync(s => s.FinancialDocumentKey == documentKey && s.Origin == origin);

    public Task<FinancialDocument> ReloadAsync(Guid key) => Firm.ReloadAsync(key);
}
