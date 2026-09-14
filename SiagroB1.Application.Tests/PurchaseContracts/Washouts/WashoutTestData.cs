using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseContracts.Washouts;

/// <summary>Sementes compartilhadas pelos testes de washout (serviços e financeiro).</summary>
internal static class WashoutTestData
{
    public static PurchaseContract Contract(
        ContractType type = ContractType.Fixed,
        ContractStatus status = ContractStatus.Approved,
        decimal totalVolume = 100_000m) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        ItemName = "SOJA GRAO",
        BranchCode = "01",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = totalVolume,
        StandardPrice = 2.5m,
        StandardCurrency = CurrencyType.Brl,
        StandardCashFlowDate = new DateTime(2026, 12, 31),
        Type = type,
        Status = status,
    };

    public static PurchaseContractPriceFixation Fixation(
        PurchaseContract contract,
        decimal volume,
        decimal price = 2.5m,
        PriceFixationStatus status = PriceFixationStatus.Confirmed) => new()
    {
        Key = Guid.NewGuid(),
        PurchaseContractKey = contract.Key,
        FixationVolume = volume,
        FixationPrice = price,
        FinancialDueDate = new DateTime(2026, 12, 31),
        Status = status,
    };

    public static PurchaseContractWashout Washout(
        PurchaseContract contract,
        PurchaseContractPriceFixation? fixation = null,
        decimal fixedVolume = 0m,
        decimal unfixedVolume = 0m,
        PurchaseContractWashoutStatus status = PurchaseContractWashoutStatus.InApproval,
        int sequence = 1,
        decimal marketPrice = 2.75m,
        decimal penaltyAmount = 1_000m)
    {
        var contractPrice = fixation?.FixationPrice ?? 0m;

        return new PurchaseContractWashout
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            PriceFixationKey = fixedVolume > 0 ? fixation?.Key : null,
            Sequence = sequence,
            FixedVolume = fixedVolume,
            UnfixedVolume = unfixedVolume,
            ContractPrice = contractPrice,
            MarketPrice = marketPrice,
            PenaltyAmount = penaltyAmount,
            Amount = PurchaseContractWashout.CalculateAmount(marketPrice, contractPrice, fixedVolume, penaltyAmount),
            DueDate = new DateTime(2026, 10, 31),
            Reason = "Produtor sem produto",
            Status = status,
        };
    }

    public static FinancialDocument Provisional(PurchaseContract contract, PurchaseContractPriceFixation fixation) => new()
    {
        Key = Guid.NewGuid(),
        Code = "FD-0001",
        CardCode = contract.CardCode,
        Direction = FinancialDirection.Payable,
        Nature = FinancialDocumentNature.Provisional,
        Status = FinancialDocumentStatus.Open,
        DueDate = new DateTime(2026, 12, 31),
        NetAmount = decimal.Round(fixation.FixationVolume * fixation.FixationPrice, 2, MidpointRounding.ToEven),
        OriginType = FinancialDocumentOrigin.PurchaseContractPriceFixation,
        OriginKey = fixation.Key,
        OriginDocNumber = contract.Code,
        PurchaseContractKey = contract.Key,
    };
}
