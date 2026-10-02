using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// A fotografia dos tributos na linha: o que o cálculo grava e o que fica TRAVADO com a regra
/// ativa. A lista de campos travados mora aqui para a trava e a escrita não divergirem.
/// </summary>
public static class SalesInvoiceTaxSnapshot
{
    public static readonly IReadOnlyList<string> LockedProperties =
    [
        nameof(SalesInvoiceItem.Cfop), nameof(SalesInvoiceItem.Ncm), nameof(SalesInvoiceItem.GoodsOrigin),
        nameof(SalesInvoiceItem.CstIcms), nameof(SalesInvoiceItem.IcmsBase), nameof(SalesInvoiceItem.IcmsRate),
        nameof(SalesInvoiceItem.IcmsValue), nameof(SalesInvoiceItem.IcmsBaseReduction),
        nameof(SalesInvoiceItem.IcmsDeferral), nameof(SalesInvoiceItem.IcmsOperationValue),
        nameof(SalesInvoiceItem.IcmsDeferredValue), nameof(SalesInvoiceItem.IcmsBenefitCode),
        nameof(SalesInvoiceItem.CstPis), nameof(SalesInvoiceItem.PisBase), nameof(SalesInvoiceItem.PisRate),
        nameof(SalesInvoiceItem.PisValue), nameof(SalesInvoiceItem.CstCofins), nameof(SalesInvoiceItem.CofinsBase),
        nameof(SalesInvoiceItem.CofinsRate), nameof(SalesInvoiceItem.CofinsValue),
        nameof(SalesInvoiceItem.IbsCbsCst), nameof(SalesInvoiceItem.IbsCbsClassCode),
        nameof(SalesInvoiceItem.IbsCbsBase), nameof(SalesInvoiceItem.CbsRate),
        nameof(SalesInvoiceItem.CbsRateReduction), nameof(SalesInvoiceItem.CbsValue),
        nameof(SalesInvoiceItem.IbsStateRate), nameof(SalesInvoiceItem.IbsMunicipalRate),
        nameof(SalesInvoiceItem.IbsRateReduction), nameof(SalesInvoiceItem.IbsStateValue),
        nameof(SalesInvoiceItem.IbsMunicipalValue), nameof(SalesInvoiceItem.MovesFiscalInventory),
        nameof(SalesInvoiceItem.CreatesFinancialDocument),
    ];

    public static void Write(SalesInvoiceItem item, TaxCalculationResult r)
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
    public static void RestoreLocked(EntityEntry<SalesInvoiceItem> entry)
    {
        foreach (var name in LockedProperties)
        {
            var property = entry.Property(name);
            property.CurrentValue = property.OriginalValue;
        }
    }
}
