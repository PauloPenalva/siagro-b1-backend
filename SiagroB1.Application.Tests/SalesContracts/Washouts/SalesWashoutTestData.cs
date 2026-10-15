using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.SalesContracts.Washouts;

/// <summary>
/// Sementes dos testes do washout de venda. Contrato de 1.000 KG, cliente C0001, preço 100; a fixação padrão é a
/// automática do preço fixo (1.000 @ 100). Mercado padrão 90: contrato acima do mercado, o cliente paga a diferença.
/// </summary>
internal static class SalesWashoutTestData
{
    public static SalesContract Contract(
        ContractType type = ContractType.Fixed,
        ContractStatus status = ContractStatus.Approved,
        decimal totalVolume = 1_000m)
    {
        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume, status: status);
        contract.Code = "CV-001";
        contract.Type = type;
        contract.BranchCode = "01";
        contract.CardName = "CLIENTE TESTE";
        contract.StandardCurrency = CurrencyType.Brl;
        contract.StandardCashFlowDate = new DateTime(2026, 12, 31);
        contract.CreationDate = DateTime.Today;
        return contract;
    }

    public static SalesContractPriceFixation Fixation(
        SalesContract contract,
        decimal volume,
        decimal price = 100m,
        PriceFixationStatus status = PriceFixationStatus.Confirmed) => new()
    {
        Key = Guid.NewGuid(),
        SalesContractKey = contract.Key,
        FixationVolume = volume,
        FixationPrice = price,
        FinancialDueDate = new DateTime(2026, 12, 31),
        Status = status,
    };

    public static SalesContractWashout Washout(
        SalesContract contract,
        SalesContractPriceFixation? fixation = null,
        decimal fixedVolume = 0m,
        decimal unfixedVolume = 0m,
        SalesContractWashoutStatus status = SalesContractWashoutStatus.InApproval,
        int sequence = 1,
        decimal marketPrice = 90m,
        decimal penaltyAmount = 500m)
    {
        var contractPrice = fixation?.FixationPrice ?? 0m;

        return new SalesContractWashout
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            PriceFixationKey = fixedVolume > 0 ? fixation?.Key : null,
            Sequence = sequence,
            FixedVolume = fixedVolume,
            UnfixedVolume = unfixedVolume,
            ContractPrice = contractPrice,
            MarketPrice = marketPrice,
            PenaltyAmount = penaltyAmount,
            Amount = SalesContractWashout.CalculateAmount(marketPrice, contractPrice, fixedVolume, penaltyAmount),
            DueDate = new DateTime(2026, 10, 31),
            Reason = "Cliente desistiu",
            Status = status,
        };
    }

    /// <summary>Provisório a receber da fixação, no valor cheio.</summary>
    public static FinancialDocument Provisional(SalesContract contract, SalesContractPriceFixation fixation, string code = "FN-PROV-V1") => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CardCode = contract.CardCode,
        BranchCode = "01",
        Direction = FinancialDirection.Receivable,
        Nature = FinancialDocumentNature.Provisional,
        Status = FinancialDocumentStatus.Open,
        Currency = CurrencyType.Brl,
        DueDate = new DateTime(2026, 12, 31),
        NetAmount = decimal.Round(fixation.FixationVolume * fixation.FixationPrice, 2, MidpointRounding.ToEven),
        OriginType = FinancialDocumentOrigin.SalesContractPriceFixation,
        OriginKey = fixation.Key,
        OriginDocNumber = contract.Code,
        SalesContractKey = contract.Key,
    };

    /// <summary>Documento de saída confirmado de uma linha, com a alocação no ledger (o "faturado" da venda).</summary>
    public static (SalesInvoice Invoice, SalesContractAllocation Allocation) Billed(SalesContract contract, decimal volume)
    {
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Confirmed);
        var item = SalesContractsAllocationTestSupport.NewItem(invoice, contract.Key, releaseKey: null, volume);
        return (invoice, SalesContractsAllocationTestSupport.NewAllocation(contract.Key, item.Key!.Value, volume));
    }
}
