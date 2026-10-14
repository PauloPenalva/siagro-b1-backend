using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Cenário dos títulos firmes de compra: contrato aprovado do fornecedor F0001 com um PROVISÓRIO (FN-PROV-1), conta
/// CX01, duas naturezas de entrada (uma gera financeiro, outra não — D10), e conciliações feitas pelo serviço REAL
/// (o firme nasce do sync). Linha padrão: 30.000 KG × R$ 2,00 = 60.000,00 com Funrural 815,00 → vínculo de
/// 10.000 KG = 10.000 × 59.185,00 ÷ 30.000 = 19.728,33.
/// </summary>
internal sealed class FirmPayableSeed
{
    public const decimal DefaultFirmAmount = 19_728.33m;

    public FiscalReconciliationSeed Fiscal { get; }
    public UnitOfWork Db => Fiscal.Db;
    public AppDbContext Context => Db.Context;
    public FakeDocNumberSequenceService Numbers { get; } = new();
    public PurchaseContract Contract { get; private set; } = null!;
    public FinancialDocument Provisional { get; private set; } = null!;
    public string Erp { get; private set; } = "STANDALONE";

    /// <summary>Natureza de entrada com "Gera financeiro" (D10).</summary>
    public int FinancialUsageCode { get; private set; }

    /// <summary>Natureza de entrada SEM "Gera financeiro".</summary>
    public int NonFinancialUsageCode { get; private set; }

    private FirmPayableSeed(FiscalReconciliationSeed fiscal) => Fiscal = fiscal;

    public static async Task<FirmPayableSeed> CreateAsync(
        decimal provisionalAmount = 100_000m, IInterceptor? interceptor = null, string erp = "STANDALONE")
    {
        var db = interceptor is null ? TestDb.CreateUnitOfWork() : TestDb.CreateUnitOfWork(interceptor);
        var seed = new FirmPayableSeed(await FiscalReconciliationSeed.CreateAsync(db)) { Erp = erp };
        seed.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl,
        });
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        seed.FinancialUsageCode = (await usages.CreateAsync(Usage("Compra para comercialização", createsFinancial: true))).Code;
        seed.NonFinancialUsageCode = (await usages.CreateAsync(Usage("Entrada sem financeiro", createsFinancial: false))).Code;
        seed.Contract = await seed.Fiscal.AddContractAsync();
        seed.Provisional = await seed.AddProvisionalAsync(seed.Contract, provisionalAmount, "FN-PROV-1");
        return seed;
    }

    private static UsageModel Usage(string name, bool createsFinancial) => new()
    {
        Name = name, Direction = UsageDirection.Incoming, CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
        CreatesFinancialDocument = createsFinancial,
    };

    public async Task<FinancialDocument> AddProvisionalAsync(PurchaseContract contract, decimal amount, string code)
    {
        var provisional = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = code, CardCode = FiscalReconciliationSeed.Supplier,
            Direction = FinancialDirection.Payable, Nature = FinancialDocumentNature.Provisional,
            Status = FinancialDocumentStatus.Open, DueDate = new DateTime(2026, 12, 31), NetAmount = amount,
            OriginType = FinancialDocumentOrigin.PurchaseContractPriceFixation, OriginKey = Guid.NewGuid(),
            OriginDocNumber = contract.Code, PurchaseContractKey = contract.Key, BranchCode = "01", Currency = CurrencyType.Brl,
        };
        Context.FinancialDocuments.Add(provisional);
        await Context.SaveChangesAsync();
        return provisional;
    }

    public PurchaseFiscalReconciliationService Reconciliation() => FiscalReconciliationSeed.NewService(Db, Numbers, Erp);

    /// <summary>Romaneio alocado + NF de uma linha, vinculados pelo serviço real. Firme = o ativo do vínculo (ou null).</summary>
    public async Task<Conciliation> ConciliateAsync(
        decimal kg = 10_000m, decimal lineKg = 30_000m, decimal unitPrice = 2m, decimal funrural = 815m,
        PurchaseInvoiceType invoiceType = PurchaseInvoiceType.Normal,
        StorageTransactionType transactionType = StorageTransactionType.Purchase,
        bool createsFinancialDocument = true, bool lineWithContract = true, Guid? originLineKey = null,
        PurchaseContract? contract = null, string taxNumber = "1001", bool withUsage = true)
    {
        var target = contract ?? Contract;
        var transaction = await Fiscal.AddTransactionAsync(kg, transactionType, target);
        var (invoice, line) = await Fiscal.AddInvoiceAsync(
            lineKg, type: invoiceType, contract: lineWithContract ? target : null, taxNumber: taxNumber);
        line.UnitPrice = unitPrice;
        line.FunruralInssValue = funrural;
        // D10: decide a NATUREZA da linha; a cópia gravada na linha fica false de propósito, para provar que não é lida.
        line.UsageCode = withUsage ? (createsFinancialDocument ? FinancialUsageCode : NonFinancialUsageCode) : null;
        line.CreatesFinancialDocument = false;
        line.PurchaseInvoiceItemOriginKey = originLineKey;
        await Db.SaveChangesAsync();

        await Reconciliation().SaveAsync(
            [new FiscalReconciliationPairDto { PurchaseInvoiceItemKey = line.Key!.Value, StorageTransactionKey = transaction.Key, Quantity = kg }],
            "ana");

        var link = await Context.PurchaseFiscalReconciliations.SingleAsync(l => l.StorageTransactionKey == transaction.Key);
        var firm = await Context.FinancialDocuments.SingleOrDefaultAsync(d =>
            d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation && d.OriginKey == link.Key &&
            d.Status != FinancialDocumentStatus.Canceled);
        return new Conciliation(transaction, invoice, line, link, firm);
    }

    /// <summary>Regrava o par da conciliação com outra quantidade (0 remove), pelo serviço real, como "bia".</summary>
    public Task SaveLinkAsync(Conciliation c, decimal kg) =>
        Reconciliation().SaveAsync(
            [new FiscalReconciliationPairDto { PurchaseInvoiceItemKey = c.Line.Key!.Value, StorageTransactionKey = c.Transaction.Key, Quantity = kg }],
            "bia");

    public async Task<FinancialDocument> ReloadAsync(Guid key) =>
        await Context.FinancialDocuments.AsNoTracking().SingleAsync(d => d.Key == key);
}

internal sealed record Conciliation(
    StorageTransaction Transaction, PurchaseInvoice Invoice, PurchaseInvoiceItem Line, PurchaseFiscalReconciliation Link,
    FinancialDocument? Firm);
