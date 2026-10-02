using SiagroB1.Domain.Enums;

namespace SiagroB1.Fiscal.Taxes;

/// <summary>Bloco de ICMS da natureza já escolhido (dentro ou fora do estado).</summary>
public sealed record IcmsRule(
    string? Cst, string? Csosn, decimal? Rate, decimal? BaseReduction, decimal? Deferral, string? BenefitCode);

public sealed record PisCofinsRule(
    string PisCst, decimal? PisRate, string CofinsCst, decimal? CofinsRate, bool ExcludeIcmsFromBase);

public sealed record IbsCbsRule(string Cst, string ClassCode, decimal? IbsReduction, decimal? CbsReduction);

/// <summary>Alíquotas de IBS/CBS vigentes na data de emissão, em percentual.</summary>
public sealed record IbsCbsRates(decimal Cbs, decimal IbsState, decimal IbsMunicipal);

/// <summary>Tudo de que a conta precisa — sem banco, sem configuração.</summary>
public sealed record TaxCalculationInput(
    decimal Amount,
    bool InState,
    string BranchState,
    string CustomerState,
    TaxRegime Regime,
    byte GoodsOrigin,
    IcmsRule Icms,
    PisCofinsRule PisCofins,
    IbsCbsRule? IbsCbs,
    IbsCbsRates? Rates);

/// <summary>A fotografia que vai para a linha do documento.</summary>
public sealed record TaxCalculationResult(
    string IcmsCode,
    decimal IcmsBase,
    decimal IcmsRate,
    decimal IcmsBaseReduction,
    decimal IcmsOperationValue,
    decimal IcmsDeferral,
    decimal IcmsDeferredValue,
    decimal IcmsValue,
    string? IcmsBenefitCode,
    string PisCst,
    decimal PisBase,
    decimal PisRate,
    decimal PisValue,
    string CofinsCst,
    decimal CofinsBase,
    decimal CofinsRate,
    decimal CofinsValue,
    string? IbsCbsCst,
    string? IbsCbsClassCode,
    decimal IbsCbsBase,
    decimal CbsRate,
    decimal CbsRateReduction,
    decimal CbsValue,
    decimal IbsStateRate,
    decimal IbsMunicipalRate,
    decimal IbsRateReduction,
    decimal IbsStateValue,
    decimal IbsMunicipalValue);
