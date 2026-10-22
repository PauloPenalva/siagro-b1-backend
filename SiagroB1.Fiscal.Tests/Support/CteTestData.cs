using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Support;

/// <summary>
/// Caso COSTA: frete interno em SP (Itaberá → São Paulo), tomador Destinatário, ICMS 00 a 12%,
/// um componente, uma NF-e, CFOP 5352.
/// </summary>
public static class CteTestData
{
    public static readonly DateTimeOffset IssuedAt = new(2026, 10, 22, 10, 0, 0, TimeSpan.FromHours(-3));

    public static CteParty Sender() => new()
    {
        TaxId = "11222333000181", Name = "COSTA PRODUTOR RURAL LTDA", StateRegistration = "110042490114",
        Address = NfeTestData.Itabera(),
    };

    public static CteParty Recipient() => new()
    {
        TaxId = "33444555000122", Name = "ARMAZEM DESTINO LTDA", StateRegistration = "111222333444",
        Address = NfeTestData.SaoPaulo(),
    };

    public static CteParty OtherTaker() => new()
    {
        TaxId = "44555666000177", Name = "TRADING PAGADORA SA", StateRegistration = "222333444555",
        Address = NfeTestData.SaoPaulo() with { Street = "RUA AUGUSTA", Number = "500" },
    };

    public static CteParty Receiver() => new()
    {
        TaxId = "55666777000133", Name = "RECEBEDOR CAMPINAS LTDA", StateRegistration = "244555666777",
        Address = NfeTestData.SaoPaulo() with { MunicipalityCode = "3509502", MunicipalityName = "Campinas", ZipCode = "13010000" },
    };

    public static CteIssueInput Input() => new()
    {
        Environment = NfeEnvironment.Homologation,
        Series = 1,
        Number = 123,
        RandomCode = "12345678",
        IssuedAt = IssuedAt,
        Cfop = "5352",
        OperationNature = "PRESTACAO DE SERVICO DE TRANSPORTE",
        Issuer = new CteIssuer
        {
            TaxId = "12345678000195", LegalName = "CEAGUI CEREAIS LTDA", StateRegistration = "387000000110",
            TaxRegime = TaxRegime.Normal, Rntrc = "12345678", Address = NfeTestData.Itabera(),
        },
        Sender = Sender(),
        Recipient = Recipient(),
        Taker = CteTaker.Recipient,
        TakerIndicator = 1,
        Route = new CteRoute("3521705", "Itaberá", "SP", "3550308", "São Paulo", "SP"),
        Components = [new CteComponent("FRETE PESO", 1000m)],
        ServiceValue = 1000m,
        ReceivableValue = 1000m,
        IcmsCst = "00",
        IcmsBase = 1000m,
        IcmsRate = 12m,
        IcmsValue = 120m,
        PredominantProduct = "SOJA EM GRAOS",
        CargoValue = 60000m,
        CargoQuantities = [new CteCargoQuantity("01", "PESO BRUTO", 30000m)],
        NfeKeys = [NfeTestData.SaleAccessKey],
        Remarks = "FRETE CONFORME CONTRATO",
        Observations = [new CteObservation("CONTRATO", "123")],
        TechnicalResponsible = new NfeTechnicalResponsible("09123456000100", "IDX Consultoria", "fiscal@idx.com.br", "1533334444"),
    };
}
