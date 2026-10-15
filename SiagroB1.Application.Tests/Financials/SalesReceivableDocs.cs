using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Títulos de venda gravados diretamente (sem a sincronização da Task 3): contrato VC-1 do cliente C0001, provisório a
/// receber FN-PROV-V1 de 10.000,00 e a conta CX01. Para exercitar os serviços do financeiro generalizados.
/// </summary>
internal sealed class SalesReceivableDocs
{
    public UnitOfWork Db { get; } = TestDb.CreateUnitOfWork();
    public AppDbContext Context => Db.Context;
    public SalesContract Contract { get; private set; } = null!;
    public FinancialDocument Provisional { get; private set; } = null!;
    private int _next = 1;

    public static async Task<SalesReceivableDocs> CreateAsync(decimal provisionalAmount = 10_000m)
    {
        var docs = new SalesReceivableDocs();
        docs.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl,
        });
        docs.Contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000_000m);
        docs.Contract.Code = "VC-1";
        docs.Context.SalesContracts.Add(docs.Contract);
        await docs.Context.SaveChangesAsync();
        docs.Provisional = await docs.AddAsync(FinancialDocumentNature.Provisional,
            FinancialDocumentOrigin.SalesContractPriceFixation, provisionalAmount, code: "FN-PROV-V1");
        return docs;
    }

    public async Task<FinancialDocument> AddAsync(
        FinancialDocumentNature nature, FinancialDocumentOrigin origin, decimal amount,
        Guid? salesContractKey = null, Guid? purchaseContractKey = null, string? code = null, Guid? originKey = null,
        FinancialDirection direction = FinancialDirection.Receivable)
    {
        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = code ?? $"FN-V{_next++:000}", CardCode = "C0001", BranchCode = "01",
            Direction = direction, Nature = nature, Status = FinancialDocumentStatus.Open,
            DueDate = new DateTime(2026, 12, 31), NetAmount = amount, Currency = CurrencyType.Brl,
            OriginType = origin, OriginKey = originKey ?? Guid.NewGuid(),
            SalesContractKey = purchaseContractKey is null ? salesContractKey ?? Contract.Key : null,
            PurchaseContractKey = purchaseContractKey,
            CreatedAt = DateTime.Now.AddMinutes(_next),
        };
        Context.FinancialDocuments.Add(document);
        await Context.SaveChangesAsync();
        return document;
    }

    public Task<FinancialDocument> FirmAsync(decimal amount, Guid? contractKey = null, Guid? originKey = null) =>
        AddAsync(FinancialDocumentNature.Firm, FinancialDocumentOrigin.SalesInvoice, amount, contractKey, originKey: originKey);

    public Task<FinancialDocument> ReturnAsync(decimal amount, Guid? contractKey = null) =>
        AddAsync(FinancialDocumentNature.SalesReturn, FinancialDocumentOrigin.SalesInvoice, amount, contractKey);

    public async Task<FinancialDocument> ReloadAsync(Guid key) =>
        await Context.FinancialDocuments.AsNoTracking().SingleAsync(d => d.Key == key);
}
