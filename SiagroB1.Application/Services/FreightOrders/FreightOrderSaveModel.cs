using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.FreightOrders;

/// <summary>Campos editáveis da ordem de frete. <see cref="Key"/> nulo = inclusão.</summary>
public class FreightOrderSaveModel
{
    public Guid? Key { get; set; }
    public Guid? DocNumberKey { get; set; }
    public string? TypedCode { get; set; }

    public required string BranchCode { get; set; }
    public DateTime? OrderDate { get; set; }
    public required string TakerCardCode { get; set; }
    public string? SenderCardCode { get; set; }
    public string? RecipientCardCode { get; set; }
    public required string StartMunicipalityCode { get; set; }
    public required string StartState { get; set; }
    public required string EndMunicipalityCode { get; set; }
    public required string EndState { get; set; }
    public required string ItemCode { get; set; }
    public required string ItemName { get; set; }
    public decimal ContractedWeightKg { get; set; }
    public FreightCalculationMethod PriceMethod { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public int? PaymentConditionCode { get; set; }
    public int? UsageCode { get; set; }
    public string? Observation { get; set; }
}
