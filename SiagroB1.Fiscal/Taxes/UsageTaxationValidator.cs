using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Fiscal.Taxes;

/// <summary>
/// Coerência do cadastro tributário da natureza (STANDALONE). Cada regra só olha campo
/// PREENCHIDO: a natureza sem tributação é válida, porque quem exige a tributação é o
/// documento, e só quando a regra de ativação está ligada.
/// </summary>
public static class UsageTaxationValidator
{
    public static void Validate(UsageModel model)
    {
        var incoming = model.Direction == UsageDirection.Incoming;

        if (incoming)
        {
            ValidateCfop(model.CfopIncomingInState, '1', "CFOP de entrada dentro do estado");
            ValidateCfop(model.CfopIncomingOutState, '2', "CFOP de entrada fora do estado");
        }
        else
        {
            ValidateCfop(model.CfopOutgoingInState, '5', "CFOP de saída dentro do estado");
            ValidateCfop(model.CfopOutgoingOutState, '6', "CFOP de saída fora do estado");
        }

        ValidateIcmsBlock("dentro do estado", model.IcmsInStateCst, model.IcmsInStateCsosn,
            model.IcmsInStateRate, model.IcmsInStateBaseReduction, model.IcmsInStateDeferral, rateRequired: true);

        ValidateIcmsBlock("fora do estado", model.IcmsOutStateCst, model.IcmsOutStateCsosn,
            rate: null, model.IcmsOutStateBaseReduction, model.IcmsOutStateDeferral, rateRequired: false);

        var allowedPisCofins = incoming ? FiscalCodes.PisCofinsCstIncoming : FiscalCodes.PisCofinsCstOutgoing;
        ValidateCode(model.PisCst, allowedPisCofins, "CST de PIS", incoming);
        ValidateCode(model.CofinsCst, allowedPisCofins, "CST de COFINS", incoming);
        ValidatePercent(model.PisRate, "Alíquota de PIS");
        ValidatePercent(model.CofinsRate, "Alíquota de COFINS");
        ValidateRateForTaxedCst(model.PisCst, model.PisRate, "PIS");
        ValidateRateForTaxedCst(model.CofinsCst, model.CofinsRate, "COFINS");

        ValidateIbsCbs(model);
    }

    private static void ValidateCfop(string? cfop, char prefix, string label)
    {
        var value = FiscalCodes.Normalize(cfop);
        if (value is null) return;

        if (value.Length != 4 || !value.All(char.IsDigit) || value[0] != prefix)
        {
            throw new DefaultException($"{label} deve ter 4 dígitos e começar com {prefix}.");
        }
    }

    private static void ValidateIcmsBlock(
        string block, string? cstRaw, string? csosnRaw,
        decimal? rate, decimal? reduction, decimal? deferral, bool rateRequired)
    {
        var cst = FiscalCodes.Normalize(cstRaw);
        var csosn = FiscalCodes.Normalize(csosnRaw);

        if (cst is not null && !FiscalCodes.IcmsCst.Contains(cst))
            throw new DefaultException($"CST de ICMS {cst} ({block}) não é suportado.");

        if (csosn is not null && !FiscalCodes.IcmsCsosn.Contains(csosn))
            throw new DefaultException($"CSOSN {csosn} ({block}) não é suportado.");

        ValidatePercent(rate, $"Alíquota de ICMS ({block})");
        ValidatePercent(reduction, $"Redução de base do ICMS ({block})");
        ValidatePercent(deferral, $"Diferimento do ICMS ({block})");

        var anyTaxed = (cst is not null && FiscalCodes.IcmsCstTaxed.Contains(cst))
                       || (csosn is not null && FiscalCodes.IcmsCsosnTaxed.Contains(csosn));

        if (rateRequired && anyTaxed && (rate ?? 0) <= 0)
            throw new DefaultException($"Informe a alíquota de ICMS ({block}) para o código escolhido.");

        if (cst == "20" && (reduction ?? 0) <= 0)
            throw new DefaultException($"CST 20 ({block}) exige redução de base maior que zero.");

        if (cst == "51" && (deferral ?? 0) <= 0)
            throw new DefaultException($"CST 51 ({block}) exige percentual de diferimento.");

        if ((deferral ?? 0) > 0 && cst != "51")
            throw new DefaultException($"O diferimento ({block}) só vale com o CST 51.");

        var noTaxCodes = (cst is not null && FiscalCodes.IcmsCstNoTax.Contains(cst))
                         || (csosn is not null && FiscalCodes.IcmsCsosnNoTax.Contains(csosn));

        // Um bloco pode ter CST tributado e CSOSN sem imposto (cada código serve a um regime):
        // a recusa só vale quando NENHUM dos dois códigos do bloco destaca imposto.
        if (noTaxCodes && !anyTaxed && ((rate ?? 0) > 0 || (reduction ?? 0) > 0 || (deferral ?? 0) > 0))
            throw new DefaultException(
                $"O código de ICMS ({block}) sem imposto não aceita alíquota, redução nem diferimento.");

        var acceptsReduction = (cst is not null && FiscalCodes.IcmsCodesWithReduction.Contains(cst))
                               || (csosn is not null && FiscalCodes.IcmsCodesWithReduction.Contains(csosn));

        if ((reduction ?? 0) > 0 && anyTaxed && !acceptsReduction)
            throw new DefaultException($"O CST de ICMS ({block}) escolhido não aceita redução de base.");
    }

    private static void ValidateCode(string? raw, IReadOnlySet<string> allowed, string label, bool incoming)
    {
        var code = FiscalCodes.Normalize(raw);
        if (code is null) return;

        if (!allowed.Contains(code))
            throw new DefaultException(
                $"{label} {code} não é válido para natureza de {(incoming ? "entrada" : "saída")}.");
    }

    private static void ValidateRateForTaxedCst(string? rawCst, decimal? rate, string tax)
    {
        var cst = FiscalCodes.Normalize(rawCst);

        if (cst is not null && FiscalCodes.PisCofinsCstRequiringRate.Contains(cst) && (rate ?? 0) <= 0)
            throw new DefaultException($"Informe a alíquota de {tax} para o CST {cst}.");
    }

    private static void ValidatePercent(decimal? value, string label)
    {
        if (value is < 0 or > 100)
            throw new DefaultException($"{label} deve estar entre 0 e 100.");
    }

    private static void ValidateIbsCbs(UsageModel model)
    {
        var cst = FiscalCodes.Normalize(model.IbsCbsCst);
        var classCode = FiscalCodes.Normalize(model.IbsCbsClassCode);

        ValidatePercent(model.IbsRateReduction, "Redução do IBS");
        ValidatePercent(model.CbsRateReduction, "Redução da CBS");

        if (cst is null) return;

        if (!FiscalCodes.IbsCbsCst.Contains(cst))
            throw new DefaultException($"CST de IBS/CBS {cst} não é suportado.");

        if (classCode is null || classCode.Length != 6 || !classCode.All(char.IsDigit))
            throw new DefaultException("Informe o cClassTrib com 6 dígitos para o CST de IBS/CBS.");

        var anyReduction = (model.IbsRateReduction ?? 0) > 0 || (model.CbsRateReduction ?? 0) > 0;

        if (cst == "200" && !anyReduction)
            throw new DefaultException("CST 200 de IBS/CBS exige redução de alíquota.");

        if (FiscalCodes.IbsCbsCstNoTax.Contains(cst) && anyReduction)
            throw new DefaultException($"CST {cst} de IBS/CBS não aceita redução.");
    }
}
