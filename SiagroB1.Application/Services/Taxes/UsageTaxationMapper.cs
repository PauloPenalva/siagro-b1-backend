using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Taxes;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// Cópia da identidade e da tributação do modelo da API para a entidade USAGES (STANDALONE).
/// Num lugar só para o create e o update não divergirem campo a campo.
/// </summary>
internal static class UsageTaxationMapper
{
    /// <summary>Mesma regra do catálogo fiscal — ver <see cref="FiscalCodes.Normalize"/>.</summary>
    internal static string? Normalize(string? value) => FiscalCodes.Normalize(value);

    internal static void CopyToEntity(UsageModel model, Usage usage)
    {
        var direction = model.Direction ?? UsageDirection.Outgoing;
        var incoming = direction == UsageDirection.Incoming;

        usage.Name = model.Name;
        usage.Description = model.Description;
        usage.Inactive = model.Inactive;
        usage.Direction = direction;

        // O tipo decide as colunas do CFOP; as do outro tipo são descartadas para não sobrar
        // CFOP de saída numa natureza de entrada (e vice-versa).
        usage.CfopOutgoingInState = incoming ? null : Normalize(model.CfopOutgoingInState);
        usage.CfopOutgoingOutState = incoming ? null : Normalize(model.CfopOutgoingOutState);
        usage.CfopIncomingInState = incoming ? Normalize(model.CfopIncomingInState) : null;
        usage.CfopIncomingOutState = incoming ? Normalize(model.CfopIncomingOutState) : null;

        usage.InvoiceOperationText = Normalize(model.InvoiceOperationText);
        usage.DefaultAdditionalInfo = Normalize(model.DefaultAdditionalInfo);
        usage.MovesFiscalInventory = model.MovesFiscalInventory;
        usage.CreatesFinancialDocument = model.CreatesFinancialDocument;

        usage.IcmsInStateCst = Normalize(model.IcmsInStateCst);
        usage.IcmsInStateCsosn = Normalize(model.IcmsInStateCsosn);
        usage.IcmsInStateRate = model.IcmsInStateRate;
        usage.IcmsInStateBaseReduction = model.IcmsInStateBaseReduction;
        usage.IcmsInStateDeferral = model.IcmsInStateDeferral;
        usage.IcmsInStateBenefitCode = Normalize(model.IcmsInStateBenefitCode);

        usage.IcmsOutStateCst = Normalize(model.IcmsOutStateCst);
        usage.IcmsOutStateCsosn = Normalize(model.IcmsOutStateCsosn);
        usage.IcmsOutStateBaseReduction = model.IcmsOutStateBaseReduction;
        usage.IcmsOutStateDeferral = model.IcmsOutStateDeferral;
        usage.IcmsOutStateBenefitCode = Normalize(model.IcmsOutStateBenefitCode);

        usage.PisCst = Normalize(model.PisCst);
        usage.PisRate = model.PisRate;
        usage.CofinsCst = Normalize(model.CofinsCst);
        usage.CofinsRate = model.CofinsRate;
        usage.ExcludeIcmsFromPisCofinsBase = model.ExcludeIcmsFromPisCofinsBase;

        usage.IbsCbsCst = Normalize(model.IbsCbsCst);
        usage.IbsCbsClassCode = Normalize(model.IbsCbsClassCode);
        usage.IbsRateReduction = model.IbsRateReduction;
        usage.CbsRateReduction = model.CbsRateReduction;

        // A natureza de devolução vale nos dois sentidos (venda → devolução de venda, compra → devolução
        // de compra); o sentido oposto é validado pelo UsageService antes de chegar aqui.
        usage.ReturnUsageCode = model.ReturnUsageCode;
    }
}
