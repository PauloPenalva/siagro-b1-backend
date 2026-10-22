using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// ICMS e IBS/CBS do CT-e pela natureza (USAGES), espelho do ramo CT-e de
/// <c>PurchaseInvoicesTaxApplyService</c>: a tributação segue as UFs da prestação (início → fim) e a origem da
/// "mercadoria" é 0 (serviço). Sem natureza o rascunho grava com tributos zerados; a confirmação a exige.
/// </summary>
public class TransportDocumentsTaxApplyService(IUnitOfWork db, IUsage usages, IbsCbsRatesService ibsCbsRatesService)
{
    private readonly TaxLineCalculator _lines = new(db, ibsCbsRatesService);

    public async Task ApplyAsync(TransportDocument doc)
    {
        TransportDocumentCalculator.Recalculate(doc);

        if (doc.UsageCode is not { } usageCode)
        {
            ClearTaxes(doc);
            TransportDocumentCalculator.Recalculate(doc);
            return;
        }

        var usage = await usages.GetByIdAsync(usageCode)
                    ?? throw new DefaultException("Natureza de operação não encontrada.");
        if (usage.Direction != UsageDirection.Outgoing)
            throw new DefaultException($"A natureza de operação {usage.Name} é de entrada e não pode ser usada no CT-e.");
        if (usage.Inactive)
            throw new DefaultException($"Natureza de operação {usage.Name} está inativa.");

        var start = (doc.StartState ?? string.Empty).Trim().ToUpperInvariant();
        var end = (doc.EndState ?? string.Empty).Trim().ToUpperInvariant();
        if (start.Length == 0 || end.Length == 0)
            throw new DefaultException("Informe o município de início e o de fim da prestação.");

        var branch = await _lines.LoadBranchAsync(doc.BranchCode);
        var inState = start == end;
        // CFOP 5xxx só para prestação que começa e termina na UF da filial; interestadual (ou iniciada em outra UF) é 6xxx.
        var cfopInState = inState && string.Equals(start, branch.StateCode, StringComparison.OrdinalIgnoreCase);
        var rateDate = DateOnly.FromDateTime(doc.IssuedAt ?? DateTime.Today);

        var (cfop, taxes) = await _lines.CalculateServiceAsync(new TaxLineRequest(
            usage, null, doc.ServiceTotal, inState, start, end, branch.TaxRegime!.Value, rateDate,
            IncomingCfop: false, CfopInState: cfopInState), goodsOrigin: 0);

        doc.Cfop = cfop;
        doc.NatureText = usage.Name.Length > 60 ? usage.Name[..60] : usage.Name;
        doc.IcmsCst = taxes.IcmsCode;
        doc.IcmsRate = taxes.IcmsRate;
        doc.IcmsReduction = taxes.IcmsBaseReduction;
        doc.IbsCbsCst = taxes.IbsCbsCst;
        doc.IbsCbsClassCode = taxes.IbsCbsClassCode;
        doc.IbsCbsBase = taxes.IbsCbsBase;
        doc.CbsRate = taxes.CbsRate;
        doc.CbsRateReduction = taxes.CbsRateReduction;
        doc.CbsValue = taxes.CbsValue;
        doc.IbsStateRate = taxes.IbsStateRate;
        doc.IbsMunicipalRate = taxes.IbsMunicipalRate;
        doc.IbsRateReduction = taxes.IbsRateReduction;
        doc.IbsStateValue = taxes.IbsStateValue;
        doc.IbsMunicipalValue = taxes.IbsMunicipalValue;

        TransportDocumentCalculator.Recalculate(doc);
    }

    private static void ClearTaxes(TransportDocument doc)
    {
        doc.Cfop = null;
        doc.NatureText = null;
        doc.IcmsCst = null;
        doc.IcmsRate = 0;
        doc.IcmsReduction = 0;
        doc.IbsCbsCst = null;
        doc.IbsCbsClassCode = null;
        doc.IbsCbsBase = 0;
        doc.CbsRate = 0;
        doc.CbsRateReduction = 0;
        doc.CbsValue = 0;
        doc.IbsStateRate = 0;
        doc.IbsMunicipalRate = 0;
        doc.IbsRateReduction = 0;
        doc.IbsStateValue = 0;
        doc.IbsMunicipalValue = 0;
    }
}
