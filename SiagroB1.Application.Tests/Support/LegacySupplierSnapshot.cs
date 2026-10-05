using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Fotografia que a entrada de terceiro importada gravava ANTES da spec terceiro-chave: a tributação do det do
/// fornecedor, por nItem. Documentos confirmados nessa época continuam no banco assim, e os testes da devolução de
/// terceiro partem deles.
/// </summary>
public static class LegacySupplierSnapshot
{
    public static void Apply(PurchaseInvoice invoice)
    {
        var nfe = SupplierNfeXmlReader.Read(invoice.XmlData!);

        foreach (var item in invoice.Items)
        {
            if (item.NfeItemNumber is not { } number)
                continue;

            var det = nfe.Items.Single(d => d.ItemNumber == number);
            item.Cfop = det.Cfop;
            item.Ncm = det.Ncm;
            item.GoodsOrigin = det.GoodsOrigin;
            item.IcmsBenefitCode = det.BenefitCode;
            item.CstIcms = det.CstIcms;
            item.IcmsBase = det.IcmsBase;
            item.IcmsBaseReduction = det.IcmsBaseReduction;
            item.IcmsRate = det.IcmsRate;
            item.IcmsValue = det.IcmsValue;
            item.IcmsDeferral = det.IcmsDeferral;
            item.IcmsOperationValue = det.IcmsOperationValue;
            item.IcmsDeferredValue = det.IcmsDeferredValue;
            item.CstPis = det.CstPis;
            item.PisBase = det.PisBase;
            item.PisRate = det.PisRate;
            item.PisValue = det.PisValue;
            item.CstCofins = det.CstCofins;
            item.CofinsBase = det.CofinsBase;
            item.CofinsRate = det.CofinsRate;
            item.CofinsValue = det.CofinsValue;
            item.IbsCbsCst = det.IbsCbsCst;
            item.IbsCbsClassCode = det.IbsCbsClassCode;
            item.IbsCbsBase = det.IbsCbsBase;
            item.IbsStateRate = det.IbsStateRate;
            item.IbsMunicipalRate = det.IbsMunicipalRate;
            item.IbsRateReduction = det.IbsRateReduction;
            item.IbsStateValue = det.IbsStateValue;
            item.IbsMunicipalValue = det.IbsMunicipalValue;
            item.CbsRate = det.CbsRate;
            item.CbsRateReduction = det.CbsRateReduction;
            item.CbsValue = det.CbsValue;
        }
    }
}
