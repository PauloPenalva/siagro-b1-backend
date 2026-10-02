namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// As contas do spec §6, puras. Ordem fixa: ICMS → PIS/COFINS → IBS/CBS, cada base e cada valor
/// arredondado a 2 casas (meio para cima) antes do passo seguinte — PIS usa o ICMS já
/// arredondado e a base do IBS/CBS usa os três já arredondados, que é o que vai no XML e o que a
/// SEFAZ confere. Quem garante que os códigos são conhecidos é o cadastro
/// (<see cref="UsageTaxationValidator"/>) e a guarda do documento.
/// </summary>
public static class TaxCalculator
{
    public static TaxCalculationResult Calculate(TaxCalculationInput input)
    {
        var icms = CalculateIcms(input);

        var pisBase = 0m; var pisRate = 0m; var pisValue = 0m;
        var cofinsBase = 0m; var cofinsRate = 0m; var cofinsValue = 0m;
        var pisCofinsBase = Round(input.Amount - (input.PisCofins.ExcludeIcmsFromBase ? icms.Value : 0m));

        if (!FiscalCodes.PisCofinsCstNoTax.Contains(input.PisCofins.PisCst))
        {
            pisBase = pisCofinsBase;
            pisRate = input.PisCofins.PisRate ?? 0m;
            pisValue = Round(pisBase * pisRate / 100m);
        }

        if (!FiscalCodes.PisCofinsCstNoTax.Contains(input.PisCofins.CofinsCst))
        {
            cofinsBase = pisCofinsBase;
            cofinsRate = input.PisCofins.CofinsRate ?? 0m;
            cofinsValue = Round(cofinsBase * cofinsRate / 100m);
        }

        var ibsCbs = CalculateIbsCbs(input, icms.Value, pisValue, cofinsValue);

        return new TaxCalculationResult(
            icms.Code, icms.Base, icms.Rate, icms.Reduction, icms.OperationValue, icms.Deferral,
            icms.DeferredValue, icms.Value, input.Icms.BenefitCode,
            input.PisCofins.PisCst, pisBase, pisRate, pisValue,
            input.PisCofins.CofinsCst, cofinsBase, cofinsRate, cofinsValue,
            input.IbsCbs?.Cst, input.IbsCbs?.ClassCode, ibsCbs.Base, ibsCbs.CbsRate, ibsCbs.CbsReduction,
            ibsCbs.CbsValue, ibsCbs.IbsStateRate, ibsCbs.IbsMunicipalRate, ibsCbs.IbsReduction,
            ibsCbs.IbsStateValue, ibsCbs.IbsMunicipalValue);
    }

    private sealed record IcmsPart(
        string Code, decimal Base, decimal Rate, decimal Reduction, decimal OperationValue,
        decimal Deferral, decimal DeferredValue, decimal Value);

    private sealed record IbsCbsPart(
        decimal Base, decimal CbsRate, decimal CbsReduction, decimal CbsValue,
        decimal IbsStateRate, decimal IbsMunicipalRate, decimal IbsReduction,
        decimal IbsStateValue, decimal IbsMunicipalValue);

    private static IcmsPart CalculateIcms(TaxCalculationInput input)
    {
        var code = (FiscalCodes.UsesCsosn(input.Regime) ? input.Icms.Csosn : input.Icms.Cst)
                   ?? throw new InvalidOperationException("Código de ICMS ausente — a guarda deveria ter barrado.");

        var noTax = FiscalCodes.IcmsCstNoTax.Contains(code) || FiscalCodes.IcmsCsosnNoTax.Contains(code);
        if (noTax)
            return new IcmsPart(code, 0m, 0m, 0m, 0m, 0m, 0m, 0m);

        var rate = input.InState
            ? input.Icms.Rate ?? 0m
            : InterstateIcmsRate.Resolve(input.BranchState, input.CustomerState, input.GoodsOrigin);

        var reduction = FiscalCodes.IcmsCodesWithReduction.Contains(code) ? input.Icms.BaseReduction ?? 0m : 0m;
        var icmsBase = Round(input.Amount * (1m - reduction / 100m));

        if (code == "51")
        {
            var deferral = input.Icms.Deferral ?? 0m;
            var operation = Round(icmsBase * rate / 100m);
            var deferred = Round(operation * deferral / 100m);
            return new IcmsPart(code, icmsBase, rate, reduction, operation, deferral, deferred, operation - deferred);
        }

        return new IcmsPart(code, icmsBase, rate, reduction, 0m, 0m, 0m, Round(icmsBase * rate / 100m));
    }

    private static IbsCbsPart CalculateIbsCbs(TaxCalculationInput input, decimal icms, decimal pis, decimal cofins)
    {
        if (input.IbsCbs is null || FiscalCodes.IbsCbsCstNoTax.Contains(input.IbsCbs.Cst))
            return new IbsCbsPart(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);

        var rates = input.Rates
                    ?? throw new InvalidOperationException("Alíquotas de IBS/CBS ausentes — a guarda deveria ter barrado.");

        var ibsReduction = input.IbsCbs.IbsReduction ?? 0m;
        var cbsReduction = input.IbsCbs.CbsReduction ?? 0m;
        var taxBase = Round(input.Amount - icms - pis - cofins);

        decimal Tax(decimal rate, decimal reduction) => Round(taxBase * rate / 100m * (1m - reduction / 100m));

        return new IbsCbsPart(
            taxBase,
            rates.Cbs, cbsReduction, Tax(rates.Cbs, cbsReduction),
            rates.IbsState, rates.IbsMunicipal, ibsReduction,
            Tax(rates.IbsState, ibsReduction), Tax(rates.IbsMunicipal, ibsReduction));
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
