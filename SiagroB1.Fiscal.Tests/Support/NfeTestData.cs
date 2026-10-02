using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Payments;

namespace SiagroB1.Fiscal.Tests.Support;

/// <summary>
/// O documento do exemplo do sub-projeto 1: CEAGUI (Itaberá/SP) vende 30.000 kg de soja a
/// R$ 2,00 para um cliente da BA — ICMS 7%, PIS/COFINS sem ICMS na base, IBS/CBS de 2026.
/// </summary>
public static class NfeTestData
{
    public static readonly DateTimeOffset IssuedAt = new(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(-3));

    public static NfeAddress Itabera() => new()
    {
        Street = "RODOVIA SP 258", Number = "KM 290", District = "ZONA RURAL",
        MunicipalityCode = "3521705", MunicipalityName = "Itaberá", State = "SP", ZipCode = "18440000",
        Phone = "1535621234",
    };

    public static NfeAddress Salvador() => new()
    {
        Street = "AV SETE DE SETEMBRO", Number = "100", Complement = "SALA 2", District = "CENTRO",
        MunicipalityCode = "2927408", MunicipalityName = "Salvador", State = "BA", ZipCode = "40060000",
    };

    public static NfeAddress SaoPaulo() => new()
    {
        Street = "AV PAULISTA", Number = "1000", District = "BELA VISTA",
        MunicipalityCode = "3550308", MunicipalityName = "São Paulo", State = "SP", ZipCode = "01310100",
    };

    public static NfeItem Item(int number = 1) => new()
    {
        Number = number, ItemCode = "SOJA", Description = "SOJA EM GRAOS", Ncm = "12019000", Cfop = "6102",
        UnitOfMeasure = "KG", Quantity = 30000m, UnitPrice = 2m, Total = 60000m, GoodsOrigin = 0,
        IcmsCode = "00", IcmsBase = 60000m, IcmsRate = 7m, IcmsValue = 4200m,
        PisCst = "01", PisBase = 55800m, PisRate = 1.65m, PisValue = 920.70m,
        CofinsCst = "01", CofinsBase = 55800m, CofinsRate = 7.6m, CofinsValue = 4240.80m,
        IbsCbsCst = "000", IbsCbsClassCode = "000001", IbsCbsBase = 50638.50m,
        IbsStateRate = 0.1m, IbsMunicipalRate = 0m, IbsStateValue = 50.64m, IbsMunicipalValue = 0m,
        CbsRate = 0.9m, CbsValue = 455.75m,
    };

    public static NfeIssueInput Input(
        NfeEnvironment environment = NfeEnvironment.Homologation,
        NfeAddress? recipientAddress = null,
        string issuerTaxId = "12345678000195") => new()
    {
        Environment = environment,
        Series = 1,
        Number = 123,
        RandomCode = "48151623",
        IssuedAt = IssuedAt,
        OperationNature = "VENDA DE PRODUCAO DO ESTABELECIMENTO",
        ApplicationVersion = "SiagroB1 1.0.116",
        Issuer = new NfeIssuer
        {
            TaxId = issuerTaxId, LegalName = "CEAGUI CEREAIS LTDA", TradeName = "CEAGUI",
            StateRegistration = "371012345110", TaxRegime = TaxRegime.Normal, Address = Itabera(),
        },
        Recipient = new NfeRecipient
        {
            TaxId = "11222333000181", Name = "CLIENTE BA LTDA", Indicator = StateRegistrationIndicator.Taxpayer,
            StateRegistration = "123456789", Email = "nfe@cliente.com.br", Address = recipientAddress ?? Salvador(),
        },
        Items = [Item()],
        FreightTerms = FreightTerms.Cif,
        Carrier = new NfeCarrier
        {
            TaxId = "33444555000122", Name = "TRANSPORTADORA TESTE LTDA", FullAddress = "RUA B, 10",
            MunicipalityName = "Itapeva", State = "SP",
        },
        Vehicle = new NfeVehicle("ABC1D23", "SP"),
        NetWeight = 30000m,
        GrossWeight = 30500m,
        Payment = PaymentInstallmentCalculator.Calculate("30,60", PaymentStartRule.IssueDate, "15", 60000m,
            DateOnly.FromDateTime(IssuedAt.Date)),
        BillingNumber = "000000123",
        AdditionalInfo = "Documento emitido por ME ou EPP optante pelo Simples Nacional? Nao.",
        FiscoInfo = "Pedido 77",
        TechnicalResponsible = new NfeTechnicalResponsible("09123456000100", "IDX Consultoria", "fiscal@idx.com.br", "1533334444"),
    };
}
