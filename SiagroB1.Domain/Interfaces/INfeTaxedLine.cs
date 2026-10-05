namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Linha de documento fiscal com a fotografia dos tributos (SP1 §5.6) — a linha do documento de saída e
/// a do de entrada. O cálculo escreve aqui e o montador da NF-e lê daqui, sem recalcular.
/// </summary>
public interface INfeTaxedLine
{
    Guid? Key { get; }
    string? ItemCode { get; }
    string? ItemName { get; }
    string? UnitOfMeasureCode { get; }
    decimal Quantity { get; }
    decimal UnitPrice { get; }
    decimal Total { get; }

    int? UsageCode { get; set; }
    string? UsageName { get; set; }
    string? Cfop { get; set; }
    string? Ncm { get; set; }
    byte? GoodsOrigin { get; set; }

    string? CstIcms { get; set; }
    decimal IcmsBase { get; set; }
    decimal IcmsRate { get; set; }
    decimal IcmsValue { get; set; }
    decimal IcmsBaseReduction { get; set; }
    decimal IcmsDeferral { get; set; }
    decimal IcmsOperationValue { get; set; }
    decimal IcmsDeferredValue { get; set; }
    string? IcmsBenefitCode { get; set; }

    string? CstPis { get; set; }
    decimal PisBase { get; set; }
    decimal PisRate { get; set; }
    decimal PisValue { get; set; }
    string? CstCofins { get; set; }
    decimal CofinsBase { get; set; }
    decimal CofinsRate { get; set; }
    decimal CofinsValue { get; set; }

    string? IbsCbsCst { get; set; }
    string? IbsCbsClassCode { get; set; }
    decimal IbsCbsBase { get; set; }
    decimal CbsRate { get; set; }
    decimal CbsRateReduction { get; set; }
    decimal CbsValue { get; set; }
    decimal IbsStateRate { get; set; }
    decimal IbsMunicipalRate { get; set; }
    decimal IbsRateReduction { get; set; }
    decimal IbsStateValue { get; set; }
    decimal IbsMunicipalValue { get; set; }

    bool MovesFiscalInventory { get; set; }
    bool CreatesFinancialDocument { get; set; }

    /// <summary>O <c>det/@nItem</c> gravado na emissão (a devolução referencia o item por ele).</summary>
    int? NfeItemNumber { get; set; }
}
