using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Tributação da NF-e do fornecedor na linha do Documento de Entrada de terceiro (spec terceiro D4/D5, §7). O servidor
/// lê o XML guardado (<see cref="PurchaseInvoice.XmlData"/>) e copia o det do mesmo nItem para a fotografia fiscal da
/// linha — é a régua da conferência da devolução. Nada disso é cálculo: é o registro da nota do fornecedor, e imposto
/// vindo da tela nunca vale. Entrada própria e devolução do cliente não passam por aqui.
/// </summary>
public static class PurchaseInvoiceSupplierTaxes
{
    public static void Apply(PurchaseInvoice invoice, IEnumerable<PurchaseInvoiceItem> items)
    {
        if (invoice.IssuerType != DocumentIssuerType.ThirdParty || invoice.InvoiceType != PurchaseInvoiceType.Normal)
            return;

        var nfe = invoice.XmlData is { Length: > 0 } ? SupplierNfeXmlReader.Read(invoice.XmlData) : null;

        foreach (var item in items)
        {
            if (nfe is null || item.NfeItemNumber is not { } number)
            {
                // Sem XML (documento digitado) ou linha sem nItem (incluída depois da importação): sem fotografia.
                // O nItem digitado no "Devolver" fica (D6).
                Clear(item);
                continue;
            }

            var det = nfe.Items.FirstOrDefault(d => d.ItemNumber == number)
                      ?? throw new DefaultException($"Item {item.ItemCode}: o item {number} não existe na NF-e do fornecedor.");

            Copy(det, item);
        }
    }

    public static void Clear(PurchaseInvoiceItem item)
    {
        item.Cfop = null;
        item.Ncm = null;
        item.GoodsOrigin = null;
        item.IcmsBenefitCode = null;
        item.CstIcms = null;
        item.IcmsBase = 0m;
        item.IcmsBaseReduction = 0m;
        item.IcmsRate = 0m;
        item.IcmsValue = 0m;
        item.IcmsDeferral = 0m;
        item.IcmsOperationValue = 0m;
        item.IcmsDeferredValue = 0m;
        item.CstPis = null;
        item.PisBase = 0m;
        item.PisRate = 0m;
        item.PisValue = 0m;
        item.CstCofins = null;
        item.CofinsBase = 0m;
        item.CofinsRate = 0m;
        item.CofinsValue = 0m;
        item.IbsCbsCst = null;
        item.IbsCbsClassCode = null;
        item.IbsCbsBase = 0m;
        item.IbsStateRate = 0m;
        item.IbsMunicipalRate = 0m;
        item.IbsRateReduction = 0m;
        item.IbsStateValue = 0m;
        item.IbsMunicipalValue = 0m;
        item.CbsRate = 0m;
        item.CbsRateReduction = 0m;
        item.CbsValue = 0m;
    }

    private static void Copy(SupplierNfeItem det, PurchaseInvoiceItem item)
    {
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
