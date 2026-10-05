using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// A fotografia dos tributos na linha: o que o cálculo grava e o que fica TRAVADO com a regra
/// ativa — comum à linha de saída e à de entrada. A lista de campos travados mora aqui para a trava e a
/// escrita não divergirem.
/// </summary>
public static class TaxSnapshot
{
    public static readonly IReadOnlyList<string> LockedProperties =
    [
        nameof(INfeTaxedLine.Cfop), nameof(INfeTaxedLine.Ncm), nameof(INfeTaxedLine.GoodsOrigin),
        nameof(INfeTaxedLine.CstIcms), nameof(INfeTaxedLine.IcmsBase), nameof(INfeTaxedLine.IcmsRate),
        nameof(INfeTaxedLine.IcmsValue), nameof(INfeTaxedLine.IcmsBaseReduction),
        nameof(INfeTaxedLine.IcmsDeferral), nameof(INfeTaxedLine.IcmsOperationValue),
        nameof(INfeTaxedLine.IcmsDeferredValue), nameof(INfeTaxedLine.IcmsBenefitCode),
        nameof(INfeTaxedLine.CstPis), nameof(INfeTaxedLine.PisBase), nameof(INfeTaxedLine.PisRate),
        nameof(INfeTaxedLine.PisValue), nameof(INfeTaxedLine.CstCofins), nameof(INfeTaxedLine.CofinsBase),
        nameof(INfeTaxedLine.CofinsRate), nameof(INfeTaxedLine.CofinsValue),
        nameof(INfeTaxedLine.IbsCbsCst), nameof(INfeTaxedLine.IbsCbsClassCode),
        nameof(INfeTaxedLine.IbsCbsBase), nameof(INfeTaxedLine.CbsRate),
        nameof(INfeTaxedLine.CbsRateReduction), nameof(INfeTaxedLine.CbsValue),
        nameof(INfeTaxedLine.IbsStateRate), nameof(INfeTaxedLine.IbsMunicipalRate),
        nameof(INfeTaxedLine.IbsRateReduction), nameof(INfeTaxedLine.IbsStateValue),
        nameof(INfeTaxedLine.IbsMunicipalValue), nameof(INfeTaxedLine.MovesFiscalInventory),
        nameof(INfeTaxedLine.CreatesFinancialDocument),
    ];

    public static void Write(INfeTaxedLine item, TaxCalculationResult r)
    {
        item.CstIcms = r.IcmsCode;
        item.IcmsBase = r.IcmsBase;
        item.IcmsRate = r.IcmsRate;
        item.IcmsBaseReduction = r.IcmsBaseReduction;
        item.IcmsOperationValue = r.IcmsOperationValue;
        item.IcmsDeferral = r.IcmsDeferral;
        item.IcmsDeferredValue = r.IcmsDeferredValue;
        item.IcmsValue = r.IcmsValue;
        item.IcmsBenefitCode = r.IcmsBenefitCode;

        item.CstPis = r.PisCst;
        item.PisBase = r.PisBase;
        item.PisRate = r.PisRate;
        item.PisValue = r.PisValue;
        item.CstCofins = r.CofinsCst;
        item.CofinsBase = r.CofinsBase;
        item.CofinsRate = r.CofinsRate;
        item.CofinsValue = r.CofinsValue;

        item.IbsCbsCst = r.IbsCbsCst;
        item.IbsCbsClassCode = r.IbsCbsClassCode;
        item.IbsCbsBase = r.IbsCbsBase;
        item.CbsRate = r.CbsRate;
        item.CbsRateReduction = r.CbsRateReduction;
        item.CbsValue = r.CbsValue;
        item.IbsStateRate = r.IbsStateRate;
        item.IbsMunicipalRate = r.IbsMunicipalRate;
        item.IbsRateReduction = r.IbsRateReduction;
        item.IbsStateValue = r.IbsStateValue;
        item.IbsMunicipalValue = r.IbsMunicipalValue;
    }

    /// <summary>
    /// Documento que não está mais Pendente (ex.: a Conferência de entregas editando um item
    /// Confirmado): os campos travados voltam ao que está gravado, venha o que vier no corpo.
    /// </summary>
    public static void RestoreLocked(EntityEntry entry)
    {
        foreach (var name in LockedProperties)
        {
            var property = entry.Property(name);
            property.CurrentValue = property.OriginalValue;
        }
    }
}
