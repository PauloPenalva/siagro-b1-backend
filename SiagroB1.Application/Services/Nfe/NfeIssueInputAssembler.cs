using System.Globalization;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Payments;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Documento + cadastro conferido → <see cref="NfeIssueInput"/>. Todo CNPJ/CPF, IE, CEP, telefone
/// e placa sai normalizado daqui (ver <see cref="NfeText"/>): o builder confia no que recebe.
/// </summary>
public static class NfeIssueInputAssembler
{
    private static readonly TimeZoneInfo Brasilia = FindBrasilia();

    /// <summary>IANA (Linux/ICU) primeiro; o id do Windows quando o ICU não está presente.</summary>
    private static TimeZoneInfo FindBrasilia()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        throw new TimeZoneNotFoundException("Fuso de Brasília não encontrado neste servidor.");
    }

    /// <summary><c>dhEmi</c> em America/Sao_Paulo (IANA: funciona em Linux e Windows com ICU).</summary>
    public static DateTimeOffset BrasiliaNow() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Brasilia);

    /// <summary>Fuso de Brasília (America/Sao_Paulo), o da data da NF-e.</summary>
    public static TimeZoneInfo BrasiliaZone => Brasilia;

    public static NfeIssueInput Build(
        SalesInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible)
    {
        var returnOrigin = invoice.IsNfeReturn
            ? context.ReturnOrigin ?? throw new DefaultException("A devolução está sem a venda de origem.")
            : null;

        // Complemento de ICMS (spec 2026-10-18 S7): SAÍDA com finalidade 2, a venda original no NFref, sem pagamento e
        // sem transporte (modFrete 9, sem vol) — mesmo que o cabeçalho tenha sido gravado com frete ou pesos por fora.
        var icmsComplement = returnOrigin is null && invoice.InvoiceType == SalesInvoiceType.IcmsComplement;

        IReadOnlyList<string> referencedKeys = returnOrigin is not null ? [returnOrigin.AccessKey]
            : icmsComplement ? context.ComplementReferencedKeys ?? throw new DefaultException("O complemento está sem a nota original.")
            : context.CustodyReferencedKey is { } custodyKey ? [custodyKey]
            : [];

        var purpose = returnOrigin is not null ? NfePurpose.Return
            : icmsComplement ? NfePurpose.Complement
            : NfePurpose.Normal;

        return Build(new NfeDocumentView(
            invoice.TaxDocumentNumber!, invoice.TaxDocumentSeries!, invoice.NfeRandomCode!,
            // Venda: saída normal. Devolução de venda: ENTRADA com finalidade 4.
            returnOrigin is null ? NfeDirection.Outgoing : NfeDirection.Incoming,
            purpose, returnOrigin, referencedKeys,
            NfeItemNumbering.Ordered(invoice.Items).Cast<INfeTaxedLine>().ToList(),
            line => ((SalesInvoiceItem)line).SalesInvoiceItemOriginKey,
            icmsComplement ? FreightTerms.None : invoice.FreightTerms,
            icmsComplement ? 0m : invoice.NetWeight, icmsComplement ? 0m : invoice.GrossWeight,
            icmsComplement
                ? null
                : new NfeVolume(invoice.VolumeQuantity, Trimmed(invoice.VolumeSpecies), Trimmed(invoice.VolumeBrand), Trimmed(invoice.VolumeNumbering)),
            invoice.TaxPayerComments, invoice.TaxComments, FunruralText: null, "VENDA",
            WithoutPayment: returnOrigin is not null || invoice.CustodyRole != StorageCustodyRole.None || icmsComplement),
            context, issuedAt, technicalResponsible);
    }

    /// <summary>
    /// Entrada própria: ENTRADA normal, destinatário = fornecedor; a contra-nota leva no NFref a chave da escrituração do
    /// produtor e o Funrural dela no infCpl (spec 2026-10-10 §6). Devolução de compra: SAÍDA com finalidade 4, o item da
    /// entrada no DFeReferenciado, sem pagamento. Complemento próprio (valor ou ICMS): ENTRADA com finalidade 2 e as
    /// notas originais no NFref do cabeçalho (spec 2026-10-14, regras 9–10); o de ICMS sem pagamento (spec 2026-10-17 D8).
    /// </summary>
    public static NfeIssueInput Build(
        PurchaseInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible)
    {
        var returnOrigin = invoice.IsNfeReturn
            ? context.ReturnOrigin ?? throw new DefaultException("A devolução está sem a entrada de origem.")
            : null;

        var complement = returnOrigin is null && PurchaseInvoiceComplements.IsComplement(invoice.InvoiceType);
        var producer = returnOrigin is null && !complement ? context.ProducerInvoice : null;

        IReadOnlyList<string> referencedKeys = returnOrigin is not null ? [returnOrigin.AccessKey]
            : complement ? context.ComplementReferencedKeys ?? throw new DefaultException("O complemento está sem as notas originais.")
            : producer is not null ? [producer.AccessKey]
            : [];

        var purpose = returnOrigin is not null ? NfePurpose.Return
            : complement ? NfePurpose.Complement
            : NfePurpose.Normal;

        return Build(new NfeDocumentView(
            invoice.TaxDocumentNumber!, invoice.TaxDocumentSeries!, invoice.NfeRandomCode!,
            returnOrigin is null ? NfeDirection.Incoming : NfeDirection.Outgoing,
            purpose, returnOrigin, referencedKeys,
            NfeItemNumbering.Ordered(invoice.Items).Cast<INfeTaxedLine>().ToList(),
            line => ((PurchaseInvoiceItem)line).PurchaseInvoiceItemOriginKey,
            invoice.FreightTerms, invoice.NetWeight, invoice.GrossWeight,
            new NfeVolume(invoice.VolumeQuantity, Trimmed(invoice.VolumeSpecies), Trimmed(invoice.VolumeBrand), Trimmed(invoice.VolumeNumbering)),
            invoice.TaxPayerComments, TaxComments: null,
            producer is not null ? producer.FunruralText : PurchaseInvoiceFunrural.Text(invoice),
            returnOrigin is null ? "COMPRA" : "DEVOLUCAO DE COMPRA",
            // Complemento de ICMS (spec 2026-10-17 D8): nenhum valor a pagar, tPag 90 como a devolução.
            WithoutPayment: returnOrigin is not null || invoice.CustodyRole != StorageCustodyRole.None ||
                            invoice.InvoiceType == PurchaseInvoiceType.IcmsComplement), context, issuedAt, technicalResponsible);
    }

    /// <summary>O que o montador precisa do documento, sem saber se é de saída ou de entrada.</summary>
    /// <param name="Lines">Já na ordem do <c>nItem</c> (<see cref="NfeItemNumbering.Ordered{TLine}"/>).</param>
    /// <param name="OriginItemKey">Na devolução, a chave do item da operação original que a linha devolve.</param>
    /// <param name="FunruralText">Trecho do Funrural no infCpl (só a entrada; spec 2026-10-09 §6).</param>
    /// <param name="DefaultOperationNature"><c>natOp</c> quando a natureza da primeira linha não tem texto nem nome.</param>
    private sealed record NfeDocumentView(
        string TaxDocumentNumber, string TaxDocumentSeries, string RandomCode,
        NfeDirection Direction, NfePurpose Purpose, NfeReturnOrigin? ReturnOrigin, IReadOnlyList<string> HeaderReferencedKeys,
        IReadOnlyList<INfeTaxedLine> Lines, Func<INfeTaxedLine, Guid?> OriginItemKey,
        FreightTerms FreightTerms, decimal NetWeight, decimal GrossWeight, NfeVolume? Volume,
        string? TaxPayerComments, string? TaxComments, string? FunruralText, string DefaultOperationNature,
        bool WithoutPayment);

    /// <summary>
    /// CFOP de exportação + produto com unidade tributável configurada → grupo tributável convertido
    /// (spec 2026-10-14 §4); senão o item sai como está (tributável = comercial).
    /// </summary>
    private static NfeItem WithTaxableUnit(NfeItem item, IReadOnlyDictionary<string, NfeTaxableUnitSetting>? taxableUnits)
    {
        if (!ExportCfops.Contains(item.Cfop) || taxableUnits?.GetValueOrDefault(item.ItemCode) is not { } setting)
            return item;

        var (quantity, unitPrice) = NfeTaxableUnit.Convert(item.Quantity, item.Total, setting.Factor);
        return item with { TaxableUnitOfMeasure = setting.UnitOfMeasure, TaxableQuantity = quantity, TaxableUnitPrice = unitPrice };
    }

    private static NfeIssueInput Build(
        NfeDocumentView view, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible)
    {
        var items = view.Lines;
        var returnOrigin = view.ReturnOrigin;
        // O total geral (itens + frete + seguro + outras despesas − desconto) é o vNF: é ele que se parcela.
        var total = items.Sum(i => i.GrandTotal);
        var firstUsage = items.Select(i => i.UsageCode).FirstOrDefault(c => c is not null) is { } code
                         && context.Usages.TryGetValue(code, out var usage)
            ? usage
            : null;

        return new NfeIssueInput
        {
            Environment = context.Settings.Environment,
            Series = int.Parse(view.TaxDocumentSeries, CultureInfo.InvariantCulture),
            Number = long.Parse(view.TaxDocumentNumber, CultureInfo.InvariantCulture),
            RandomCode = view.RandomCode,
            Direction = view.Direction,
            Purpose = view.Purpose,
            ReferencedKeys = view.HeaderReferencedKeys,
            IssuedAt = issuedAt,
            OperationNature = string.IsNullOrWhiteSpace(firstUsage?.InvoiceOperationText)
                ? firstUsage?.Name ?? view.DefaultOperationNature
                : firstUsage.InvoiceOperationText,
            ApplicationVersion = $"SiagroB1 {typeof(NfeIssueInputAssembler).Assembly.GetName().Version?.ToString(3)}",
            Issuer = new NfeIssuer
            {
                TaxId = NfeText.AlphaNumeric(context.Branch.TaxId),
                LegalName = context.Branch.LegalName!,
                TradeName = context.Branch.TradeName,
                StateRegistration = NfeText.AlphaNumeric(context.Branch.StateRegistration),
                TaxRegime = context.Branch.TaxRegime!.Value,
                Address = new NfeAddress
                {
                    Street = context.Branch.Street!, Number = context.Branch.StreetNumber!, Complement = context.Branch.Complement,
                    District = context.Branch.District!, MunicipalityCode = context.BranchMunicipality.Code,
                    MunicipalityName = context.BranchMunicipality.Name, State = context.BranchMunicipality.StateAbbreviation,
                    ZipCode = NfeText.Digits(context.Branch.ZipCode), Phone = NfeText.Digits(context.Branch.Phone),
                },
            },
            Recipient = new NfeRecipient
            {
                TaxId = NfeText.AlphaNumeric(context.Customer.TaxId),
                Name = context.Customer.CardName,
                Indicator = context.Customer.StateRegistrationIndicator!.Value,
                StateRegistration = NfeText.Digits(context.Customer.StateRegistration),
                Email = context.Customer.NfeEmails.OrderBy(e => e.LineNum).Select(e => e.Email).FirstOrDefault(),
                Address = ToAddress(context.CustomerAddress, context.CustomerMunicipality, context.Customer.Phone),
            },
            Delivery = context.DeliveryPartner is null
                ? null
                : new NfeDelivery
                {
                    TaxId = NfeText.AlphaNumeric(context.DeliveryPartner.TaxId),
                    Name = context.DeliveryPartner.CardName,
                    StateRegistration = NfeText.Digits(context.DeliveryPartner.StateRegistration),
                    Address = ToAddress(context.DeliveryAddress!, context.DeliveryMunicipality!, context.DeliveryPartner.Phone),
                },
            Items = items.Select((item, index) => WithTaxableUnit(ToItem(item, item.NfeItemNumber ?? index + 1) with
            {
                Cest = context.ItemCests.GetValueOrDefault(item.ItemCode!),
                // Pedido do cliente só existe na saída; a linha de compra devolve nulo no cast.
                OrderNumber = (item as SalesInvoiceItem)?.CustomerOrderNumber,
                OrderItem = (item as SalesInvoiceItem)?.CustomerOrderItem,
                // VC02-14: o número do item NA OPERAÇÃO ORIGINAL, não a posição na devolução.
                Reference = returnOrigin is null
                    ? null
                    : new NfeItemReference(returnOrigin.AccessKey, returnOrigin.ItemNumbers[view.OriginItemKey(item)!.Value]),
            }, context.TaxableUnits)).ToList(),
            FreightTerms = view.FreightTerms,
            Carrier = context.Carrier is null
                ? null
                : new NfeCarrier
                {
                    TaxId = string.IsNullOrWhiteSpace(context.Carrier.TaxId) ? null : NfeText.AlphaNumeric(context.Carrier.TaxId),
                    Name = context.Carrier.CardName,
                    StateRegistration = NfeText.Digits(context.Carrier.StateRegistration) is { Length: > 0 } ie ? ie : null,
                    FullAddress = context.CarrierAddress is null
                        ? null
                        : string.Join(", ", new[] { context.CarrierAddress.Street, context.CarrierAddress.StreetNumber }
                            .Where(s => !string.IsNullOrWhiteSpace(s))),
                    MunicipalityName = context.CarrierAddress?.City,
                    State = context.CarrierAddress?.State,
                },
            Vehicle = context.TruckPlate is not null && context.TruckState is not null
                ? new NfeVehicle(context.TruckPlate, context.TruckState)
                : null,
            NetWeight = view.NetWeight,
            GrossWeight = view.GrossWeight,
            Volume = view.Volume,
            // Devolução e nota de custódia: tPag 90 com vPag 0 (rejeição 871) e sem cobr.
            Payment = view.WithoutPayment
                ? new PaymentPlan(PaymentMeansCodes.NoPayment, null, 0m, [])
                : PaymentInstallmentCalculator.Calculate(
                    context.PaymentCondition!.Days, context.PaymentCondition.StartRule, context.PaymentCondition.PaymentMeans,
                    total, DateOnly.FromDateTime(issuedAt.Date)),
            BillingNumber = view.TaxDocumentNumber,
            AdditionalInfo = AdditionalInfo(items, context.Usages, view.FunruralText, view.TaxPayerComments, ReturnReference(returnOrigin)),
            FiscoInfo = view.TaxComments,
            TechnicalResponsible = technicalResponsible,
        };
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static NfeAddress ToAddress(Address address, Municipality municipality, string? phone) => new()
    {
        Street = address.Street!,
        Number = address.StreetNumber!,
        Complement = address.Complement,
        District = address.Block!,
        MunicipalityCode = municipality.Code,
        MunicipalityName = municipality.Name,
        State = municipality.StateAbbreviation,
        ZipCode = NfeText.Digits(address.ZipCode) is { Length: 8 } zip ? zip : null,
        Phone = NfeText.Digits(phone),
    };

    /// <remarks>Produto e unidade de medida já conferidos pela prontidão (<see cref="NfeReadinessValidator"/>).</remarks>
    private static NfeItem ToItem(INfeTaxedLine item, int number) => new()
    {
        Number = number,
        ItemCode = item.ItemCode!,
        Description = string.IsNullOrWhiteSpace(item.ItemName) ? item.ItemCode! : item.ItemName,
        Ncm = item.Ncm!,
        Cfop = item.Cfop!,
        UnitOfMeasure = item.UnitOfMeasureCode!,
        Quantity = item.Quantity,
        UnitPrice = item.UnitPrice,
        Total = item.Total,
        FreightValue = item.FreightValue,
        InsuranceValue = item.InsuranceValue,
        DiscountValue = item.DiscountValue,
        OtherExpensesValue = item.OtherExpensesValue,
        GoodsOrigin = item.GoodsOrigin ?? 0,
        BenefitCode = item.IcmsBenefitCode,
        IcmsCode = item.CstIcms!,
        IcmsBase = item.IcmsBase,
        IcmsRate = item.IcmsRate,
        IcmsValue = item.IcmsValue,
        IcmsBaseReduction = item.IcmsBaseReduction,
        IcmsDeferral = item.IcmsDeferral,
        IcmsOperationValue = item.IcmsOperationValue,
        IcmsDeferredValue = item.IcmsDeferredValue,
        PisCst = item.CstPis!,
        PisBase = item.PisBase,
        PisRate = item.PisRate,
        PisValue = item.PisValue,
        CofinsCst = item.CstCofins!,
        CofinsBase = item.CofinsBase,
        CofinsRate = item.CofinsRate,
        CofinsValue = item.CofinsValue,
        IbsCbsCst = item.IbsCbsCst,
        IbsCbsClassCode = item.IbsCbsClassCode,
        IbsCbsBase = item.IbsCbsBase,
        IbsStateRate = item.IbsStateRate,
        IbsMunicipalRate = item.IbsMunicipalRate,
        IbsRateReduction = item.IbsRateReduction,
        IbsStateValue = item.IbsStateValue,
        IbsMunicipalValue = item.IbsMunicipalValue,
        CbsRate = item.CbsRate,
        CbsRateReduction = item.CbsRateReduction,
        CbsValue = item.CbsValue,
    };

    /// <summary>
    /// <c>infCpl</c>: na devolução, a referência à operação original primeiro; depois os textos padrão
    /// distintos das naturezas, na ordem das linhas, o Funrural calculado e as informações do contribuinte.
    /// O texto da natureza é o da operação da linha: dentro do estado (CFOP 1xxx/5xxx) ou fora (os demais).
    /// </summary>
    private static string? AdditionalInfo(
        IEnumerable<INfeTaxedLine> items, IReadOnlyDictionary<int, Usage> usages, string? funruralText,
        string? taxPayerComments, string? returnReference = null)
    {
        var texts = items
            .Select(i => i.UsageCode is { } code && usages.TryGetValue(code, out var usage) ? UsageText(usage, i.Cfop) : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct()
            .ToList();

        if (returnReference is not null)
            texts.Insert(0, returnReference);

        if (!string.IsNullOrWhiteSpace(funruralText))
            texts.Add(funruralText);

        if (!string.IsNullOrWhiteSpace(taxPayerComments))
            texts.Add(taxPayerComments.Trim());

        return texts.Count == 0 ? null : string.Join(" | ", texts);
    }

    private static string? UsageText(Usage usage, string? cfop) =>
        cfop is { Length: > 0 } && cfop[0] is '1' or '5' ? usage.DefaultAdditionalInfo : usage.DefaultAdditionalInfoOutState;

    private static string? ReturnReference(NfeReturnOrigin? origin) =>
        origin is null
            ? null
            : $"Devolução da NF-e nº {NumberText(origin.Number)}, série {origin.Series}, " +
              $"de {origin.IssuedOn?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}, chave {origin.AccessKey}.";

    private static string NumberText(string number) =>
        long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : number;
}
