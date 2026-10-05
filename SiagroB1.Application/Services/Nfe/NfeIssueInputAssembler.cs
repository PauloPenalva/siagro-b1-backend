using System.Globalization;
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

        return Build(new NfeDocumentView(
            invoice.TaxDocumentNumber!, invoice.TaxDocumentSeries!, invoice.NfeRandomCode!,
            // Venda: saída normal. Devolução de venda: ENTRADA com finalidade 4.
            returnOrigin is null ? NfeDirection.Outgoing : NfeDirection.Incoming,
            returnOrigin, returnOrigin is null ? [] : [returnOrigin.AccessKey],
            NfeItemNumbering.Ordered(invoice.Items).Cast<INfeTaxedLine>().ToList(),
            line => ((SalesInvoiceItem)line).SalesInvoiceItemOriginKey,
            invoice.FreightTerms, invoice.NetWeight, invoice.GrossWeight,
            new NfeVolume(invoice.VolumeQuantity, Trimmed(invoice.VolumeSpecies), Trimmed(invoice.VolumeBrand), Trimmed(invoice.VolumeNumbering)),
            invoice.TaxPayerComments, invoice.TaxComments, "VENDA"), context, issuedAt, technicalResponsible);
    }

    /// <summary>
    /// Entrada própria: ENTRADA normal, destinatário = fornecedor, sem NF-e referenciada (o NFref é 1:N no
    /// leiaute e o documento não tem onde guardá-las). Devolução de compra: SAÍDA com finalidade 4, o item da
    /// entrada no DFeReferenciado, sem pagamento.
    /// </summary>
    public static NfeIssueInput Build(
        PurchaseInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible)
    {
        var returnOrigin = invoice.IsNfeReturn
            ? context.ReturnOrigin ?? throw new DefaultException("A devolução está sem a entrada de origem.")
            : null;

        IReadOnlyList<string> referencedKeys = returnOrigin is not null ? [returnOrigin.AccessKey] : [];

        return Build(new NfeDocumentView(
            invoice.TaxDocumentNumber!, invoice.TaxDocumentSeries!, invoice.NfeRandomCode!,
            returnOrigin is null ? NfeDirection.Incoming : NfeDirection.Outgoing,
            returnOrigin, referencedKeys,
            NfeItemNumbering.Ordered(invoice.Items).Cast<INfeTaxedLine>().ToList(),
            line => ((PurchaseInvoiceItem)line).PurchaseInvoiceItemOriginKey,
            invoice.FreightTerms, invoice.NetWeight, invoice.GrossWeight,
            new NfeVolume(invoice.VolumeQuantity, Trimmed(invoice.VolumeSpecies), Trimmed(invoice.VolumeBrand), Trimmed(invoice.VolumeNumbering)),
            invoice.TaxPayerComments, TaxComments: null,
            returnOrigin is null ? "COMPRA" : "DEVOLUCAO DE COMPRA"), context, issuedAt, technicalResponsible);
    }

    /// <summary>O que o montador precisa do documento, sem saber se é de saída ou de entrada.</summary>
    /// <param name="Lines">Já na ordem do <c>nItem</c> (<see cref="NfeItemNumbering.Ordered{TLine}"/>).</param>
    /// <param name="OriginItemKey">Na devolução, a chave do item da operação original que a linha devolve.</param>
    /// <param name="DefaultOperationNature"><c>natOp</c> quando a natureza da primeira linha não tem texto nem nome.</param>
    private sealed record NfeDocumentView(
        string TaxDocumentNumber, string TaxDocumentSeries, string RandomCode,
        NfeDirection Direction, NfeReturnOrigin? ReturnOrigin, IReadOnlyList<string> HeaderReferencedKeys,
        IReadOnlyList<INfeTaxedLine> Lines, Func<INfeTaxedLine, Guid?> OriginItemKey,
        FreightTerms FreightTerms, decimal NetWeight, decimal GrossWeight, NfeVolume? Volume,
        string? TaxPayerComments, string? TaxComments, string DefaultOperationNature);

    private static NfeIssueInput Build(
        NfeDocumentView view, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible)
    {
        var items = view.Lines;
        var returnOrigin = view.ReturnOrigin;
        var total = items.Sum(i => i.Total);
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
            Purpose = returnOrigin is null ? NfePurpose.Normal : NfePurpose.Return,
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
                Email = context.Customer.NfeEmail,
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
            Items = items.Select((item, index) => ToItem(item, item.NfeItemNumber ?? index + 1) with
            {
                Cest = context.ItemCests.GetValueOrDefault(item.ItemCode!),
                // VC02-14: o número do item NA OPERAÇÃO ORIGINAL, não a posição na devolução.
                Reference = returnOrigin is null
                    ? null
                    : new NfeItemReference(returnOrigin.AccessKey, returnOrigin.ItemNumbers[view.OriginItemKey(item)!.Value]),
            }).ToList(),
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
            // Devolução: tPag 90 com vPag 0 (rejeição 871) e sem cobr.
            Payment = returnOrigin is not null
                ? new PaymentPlan(PaymentMeansCodes.NoPayment, null, 0m, [])
                : PaymentInstallmentCalculator.Calculate(
                    context.PaymentCondition!.Days, context.PaymentCondition.StartRule, context.PaymentCondition.PaymentMeans,
                    total, DateOnly.FromDateTime(issuedAt.Date)),
            BillingNumber = view.TaxDocumentNumber,
            AdditionalInfo = AdditionalInfo(items, context.Usages, view.TaxPayerComments, ReturnReference(returnOrigin)),
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
    /// distintos das naturezas, na ordem das linhas, e as informações do contribuinte.
    /// </summary>
    private static string? AdditionalInfo(
        IEnumerable<INfeTaxedLine> items, IReadOnlyDictionary<int, Usage> usages, string? taxPayerComments,
        string? returnReference = null)
    {
        var texts = items
            .Select(i => i.UsageCode is { } code && usages.TryGetValue(code, out var usage) ? usage.DefaultAdditionalInfo : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct()
            .ToList();

        if (returnReference is not null)
            texts.Insert(0, returnReference);

        if (!string.IsNullOrWhiteSpace(taxPayerComments))
            texts.Add(taxPayerComments.Trim());

        return texts.Count == 0 ? null : string.Join(" | ", texts);
    }

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
