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

    /// <summary>Chave da venda que a devolução de teste referencia.</summary>
    public const string SaleAccessKey = "35261012345678000195550010000000981481516230";

    /// <summary>
    /// Devolução da venda de teste pelo cliente de SP (operação interna): entrada, CFOP 1202, PIS/COFINS
    /// de entrada, sem pagamento, com a venda referenciada no cabeçalho e no item.
    /// </summary>
    public static NfeIssueInput ReturnInput() => Input(recipientAddress: SaoPaulo()) with
    {
        OperationNature = "DEVOLUCAO DE VENDA",
        Direction = NfeDirection.Incoming,
        Purpose = NfePurpose.Return,
        ReferencedKeys = [SaleAccessKey],
        Items = [Item() with { Cfop = "1202", PisCst = "72", CofinsCst = "72", Reference = new NfeItemReference(SaleAccessKey, 1) }],
        Payment = new PaymentPlan(PaymentMeansCodes.NoPayment, null, 0m, []),
    };

    /// <summary>Chave da NF-e do produtor que a compra de teste referencia (nota de terceiro).</summary>
    public const string ProducerAccessKey = "35261011222333000181550010000004561123456780";

    /// <summary>Chave da NF-e de entrada própria que a devolução de compra de teste referencia.</summary>
    public const string EntryAccessKey = "35261012345678000195550010000001231481516234";

    private static NfeRecipient NonTaxpayerProducer() => new()
    {
        TaxId = "52998224725", Name = "PRODUTOR RURAL TESTE", Indicator = StateRegistrationIndicator.NonTaxpayer,
        Address = SaoPaulo(),
    };

    /// <summary>
    /// Compra de produtor de SP (operação interna) com NF-e própria de entrada: CFOP 1102, PIS/COFINS
    /// de entrada, cobrança pela condição e a NF-e do produtor referenciada no cabeçalho.
    /// </summary>
    public static NfeIssueInput PurchaseEntryInput() => Input(recipientAddress: SaoPaulo()) with
    {
        OperationNature = "COMPRA DE MERCADORIA",
        Direction = NfeDirection.Incoming,
        Purpose = NfePurpose.Normal,
        ReferencedKeys = [ProducerAccessKey],
        Recipient = NonTaxpayerProducer(),
        Items = [Item() with { Cfop = "1102", PisCst = "74", CofinsCst = "74" }],
    };

    /// <summary>
    /// Devolução de compra ao mesmo produtor (saída, finalidade 4): CFOP 5202, sem pagamento, com o
    /// item 2 da entrada referenciado no item (VC02-14) e nada no cabeçalho (rejeição 1010).
    /// </summary>
    public static NfeIssueInput PurchaseReturnInput() => Input(recipientAddress: SaoPaulo()) with
    {
        OperationNature = "DEVOLUCAO DE COMPRA",
        Direction = NfeDirection.Outgoing,
        Purpose = NfePurpose.Return,
        ReferencedKeys = [EntryAccessKey],
        Recipient = NonTaxpayerProducer(),
        Items = [Item() with { Cfop = "5202", PisCst = "49", CofinsCst = "49", Reference = new NfeItemReference(EntryAccessKey, 2) }],
        Payment = new PaymentPlan(PaymentMeansCodes.NoPayment, null, 0m, []),
    };
}
