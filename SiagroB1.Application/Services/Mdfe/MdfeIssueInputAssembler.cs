using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// Traduz o <see cref="TransportManifest"/> gravado (já validado na confirmação) para o <see cref="MdfeIssueInput"/> do
/// Fiscal. Não decide cadastro: usa o snapshot de veículos, condutores, CT-e e pagamento gravados no MDF-e.
/// </summary>
public static class MdfeIssueInputAssembler
{
    /// <summary>tpComp "99" (outros): o XSD copiado não aceita "04" (frete).</summary>
    public const string OtherComponent = "99";

    /// <param name="b">Filial com <see cref="Branch.Municipality"/> carregado (cMun/UF do emitente).</param>
    /// <param name="t">Configuração de transporte da filial (RNTRC).</param>
    public static MdfeIssueInput Build(
        TransportManifest m, Branch b, BranchTransportSettings t, NfeTechnicalResponsible? technicalResponsible = null)
    {
        if (m.Number is not { } number || m.RandomCode is null || m.IssuedAt is not { } issuedAt)
            throw new DefaultException("Confirme o MDF-e antes de transmitir: ele ainda não tem número e chave.");

        var traction = m.Vehicles.FirstOrDefault(v => v.IsTraction)
                       ?? throw new DefaultException("Informe o veículo de tração.");

        return new MdfeIssueInput
        {
            Issuer = new MdfeIssuer
            {
                TaxId = Required(b.TaxId, string.Empty).Trim(),
                StateRegistration = Required(b.StateRegistration, string.Empty),
                LegalName = Required(b.LegalName, b.BranchName),
                TradeName = b.TradeName,
                Rntrc = t.CompanyRntrc ?? string.Empty,
                Address = new NfeAddress
                {
                    Street = b.Street ?? string.Empty,
                    Number = Required(b.StreetNumber, "S/N"),
                    Complement = b.Complement,
                    District = b.District ?? string.Empty,
                    MunicipalityCode = b.MunicipalityCode ?? string.Empty,
                    MunicipalityName = b.Municipality?.Name ?? string.Empty,
                    State = b.Municipality?.StateAbbreviation ?? b.StateCode ?? string.Empty,
                    ZipCode = b.ZipCode,
                    Phone = b.Phone,
                },
            },
            Environment = m.Environment ?? throw new DefaultException("O MDF-e está sem ambiente: confirme de novo."),
            Series = m.Series,
            Number = number,
            RandomCode = m.RandomCode,
            // dhEmi com o fuso de Brasília (como no CT-e).
            IssuedAt = new DateTimeOffset(issuedAt, NfeIssueInputAssembler.BrasiliaZone.GetUtcOffset(issuedAt)),
            UfStart = m.UfStart ?? string.Empty,
            UfEnd = m.UfEnd ?? string.Empty,
            RouteUfs = m.RouteUfs.OrderBy(u => u.LineNum).Select(u => u.Uf).ToList(),
            LoadingCities = m.LoadingCities.OrderBy(c => c.LineNum).Select(c => new MdfeLoadingCity(c.CityIbge, c.CityName)).ToList(),
            UnloadingCities = m.Ctes.OrderBy(c => c.LineNum).GroupBy(c => c.UnloadingCityIbge)
                .Select(g => new MdfeUnloadingCity(g.Key, g.First().UnloadingCityName, g.Select(c => c.CteAccessKey).ToList()))
                .ToList(),
            Traction = Vehicle(traction),
            Trailers = m.Vehicles.Where(v => !v.IsTraction).OrderBy(v => v.LineNum).Select(Vehicle).ToList(),
            Drivers = Drivers(m),
            Ciot = string.IsNullOrWhiteSpace(m.CiotNumber) ? null : new MdfeCiot(m.CiotNumber, m.CiotResponsibleTaxId ?? string.Empty),
            Contractors = m.Contractors.Select(c => new MdfeContractor(c.TaxId, c.Name)).ToList(),
            TollVoucher = string.IsNullOrWhiteSpace(m.TollSupplierCnpj) || string.IsNullOrWhiteSpace(m.TollVoucherNumber)
                ? null
                : new MdfeTollVoucher(m.TollSupplierCnpj, m.TollVoucherNumber, m.TollValue ?? 0m),
            Payment = m.Payments.FirstOrDefault() is { } p ? Payment(p) : null,
            Insurance = Insurance(m, b),
            Product = new MdfePredominantProduct(
                m.CargoType ?? "01", Required(m.ProductDescription, "CARGA"), m.Ncm,
                m.Ctes.Count == 1 ? m.LoadingZip : null, m.Ctes.Count == 1 ? m.UnloadingZip : null),
            Totals = new MdfeTotals(m.Ctes.Count, m.CargoValue, m.WeightUnit, m.GrossWeight),
            Seals = m.Seals.Select(s => s.Number).ToList(),
            AdditionalInfo = m.AdditionalInfo,
            InfRespTec = technicalResponsible,
        };
    }

    private static MdfeVehicle Vehicle(TransportManifestVehicle v) =>
        new(v.Plate, v.Renavam, v.TareKg, v.CapacityKg, v.CapacityM3, v.BodyType, v.WheelType, v.Uf);

    /// <summary>Condutor principal + adicionais (sem repetir CPF); os incluídos por evento não voltam ao XML.</summary>
    private static List<MdfeDriver> Drivers(TransportManifest m)
    {
        var list = new List<MdfeDriver>();
        if (!string.IsNullOrWhiteSpace(m.DriverCpf))
            list.Add(new MdfeDriver(m.DriverCpf, m.DriverName ?? string.Empty));
        list.AddRange(m.Drivers.Where(d => !d.IncludedByEvent).OrderBy(d => d.LineNum).Select(d => new MdfeDriver(d.Cpf, d.Name)));
        return list.DistinctBy(d => d.Cpf).ToList();
    }

    private static MdfePayment Payment(TransportManifestPayment p) => new()
    {
        Contractor = new MdfeContractor(p.ContractorTaxId, p.ContractorName),
        Components = p.Components.Select(c => c.Type is "01" or "02" or "03"
                ? new MdfePaymentComponent(c.Type, c.Value, c.Description)
                : new MdfePaymentComponent(OtherComponent, c.Value, Required(c.Description, "FRETE")))
            .ToList(),
        Total = p.Total,
        IndPag = p.IndPag,
        Installments = p.Installments.OrderBy(i => i.Number).Select(i => new MdfePaymentInstallment(i.Number, i.DueDate, i.Value)).ToList(),
        Pix = p.Pix,
        BankCode = p.BankCode,
        Agency = p.Agency,
    };

    /// <summary>
    /// Seguro só com seguradora informada. A entidade não guarda o CPF/CNPJ do responsável: emitente (1) = CNPJ da
    /// filial; contratante (2) = o do contratante do MDF-e (ou o do pagamento).
    /// </summary>
    private static MdfeInsurance? Insurance(TransportManifest m, Branch b)
    {
        if (string.IsNullOrWhiteSpace(m.InsurerTaxId) || string.IsNullOrWhiteSpace(m.InsurerName))
            return null;

        var responsible = m.InsuranceResponsible?.Trim() == "2" ? 2 : 1;
        var taxId = responsible == 2
            ? m.Contractors.FirstOrDefault()?.TaxId ?? m.Payments.FirstOrDefault()?.ContractorTaxId
            : b.TaxId?.Trim();
        var endorsements = m.Ctes.OrderBy(c => c.LineNum)
            .SelectMany(c => (c.Endorsements ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct().ToList();

        return new MdfeInsurance(responsible, taxId, m.InsurerName, m.InsurerTaxId, m.PolicyNumber ?? string.Empty, endorsements);
    }

    private static string Required(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
