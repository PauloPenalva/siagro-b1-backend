using System.Globalization;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
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
        var items = invoice.Items.ToList();
        var total = items.Sum(i => i.Total);
        var firstUsage = items.Select(i => i.UsageCode).FirstOrDefault(c => c is not null) is { } code
                         && context.Usages.TryGetValue(code, out var usage)
            ? usage
            : null;

        return new NfeIssueInput
        {
            Environment = context.Settings.Environment,
            Series = int.Parse(invoice.TaxDocumentSeries!, CultureInfo.InvariantCulture),
            Number = long.Parse(invoice.TaxDocumentNumber!, CultureInfo.InvariantCulture),
            RandomCode = invoice.NfeRandomCode!,
            IssuedAt = issuedAt,
            OperationNature = string.IsNullOrWhiteSpace(firstUsage?.InvoiceOperationText)
                ? firstUsage?.Name ?? "VENDA"
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
            Items = items.Select((item, index) => ToItem(item, index + 1) with
            {
                Cest = context.ItemCests.GetValueOrDefault(item.ItemCode),
            }).ToList(),
            FreightTerms = invoice.FreightTerms,
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
            NetWeight = invoice.NetWeight,
            GrossWeight = invoice.GrossWeight,
            Volume = new NfeVolume(
                invoice.VolumeQuantity, Trimmed(invoice.VolumeSpecies), Trimmed(invoice.VolumeBrand), Trimmed(invoice.VolumeNumbering)),
            Payment = PaymentInstallmentCalculator.Calculate(
                context.PaymentCondition.Days, context.PaymentCondition.StartRule, context.PaymentCondition.PaymentMeans,
                total, DateOnly.FromDateTime(issuedAt.Date)),
            BillingNumber = invoice.TaxDocumentNumber!,
            AdditionalInfo = AdditionalInfo(items, context.Usages, invoice.TaxPayerComments),
            FiscoInfo = invoice.TaxComments,
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

    private static NfeItem ToItem(SalesInvoiceItem item, int number) => new()
    {
        Number = number,
        ItemCode = item.ItemCode,
        Description = string.IsNullOrWhiteSpace(item.ItemName) ? item.ItemCode : item.ItemName,
        Ncm = item.Ncm!,
        Cfop = item.Cfop!,
        UnitOfMeasure = item.UnitOfMeasureCode,
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

    /// <summary><c>infCpl</c>: textos padrão distintos das naturezas, na ordem das linhas, + informações do contribuinte.</summary>
    private static string? AdditionalInfo(
        IEnumerable<SalesInvoiceItem> items, IReadOnlyDictionary<int, Usage> usages, string? taxPayerComments)
    {
        var texts = items
            .Select(i => i.UsageCode is { } code && usages.TryGetValue(code, out var usage) ? usage.DefaultAdditionalInfo : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct()
            .ToList();

        if (!string.IsNullOrWhiteSpace(taxPayerComments))
            texts.Add(taxPayerComments.Trim());

        return texts.Count == 0 ? null : string.Join(" | ", texts);
    }
}
