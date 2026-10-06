using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

public sealed record NfeScenario(UnitOfWork Db, string DatabaseName, Guid InvoiceKey);

/// <summary>
/// Cenário completo da CEAGUI para a emissão: filial que emite (Itaberá/SP), configuração com
/// certificado de teste, cliente da BA contribuinte, transportadora, veículo, natureza, condição
/// 30/60 boleto e um documento Pendente com a linha já calculada (exemplo do sub-projeto 1).
/// </summary>
public static class NfeTestSeed
{
    public const string CardCode = "C-BA";

    /// <summary>Relógio fixo dos testes: o documento semeado tem a data deste dia (a emissão exige "hoje").</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(-3));

    public static DateTimeOffset Clock() => Now;

    public static readonly string CertificateKey =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    public static IConfiguration Config(string? erp = "STANDALONE", bool withKey = true) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Erp"] = erp,
            ["Nfe:CertificateKey"] = withKey ? CertificateKey : null,
            ["Nfe:TechnicalResponsible:Cnpj"] = "09123456000100",
            ["Nfe:TechnicalResponsible:Contact"] = "IDX Consultoria",
            ["Nfe:TechnicalResponsible:Email"] = "fiscal@idx.com.br",
            ["Nfe:TechnicalResponsible:Phone"] = "1533334444",
        }).Build();

    /// <param name="status">
    /// Situação do documento de saída. O documento Normal é transmitido já Confirmado (spec 2026-10-06 D1); o padrão
    /// Pendente fica para quem testa o que vem antes da confirmação (cancelamento, conclusão de confirmação, travas).
    /// </param>
    public static async Task<NfeScenario> SeedAsync(InvoiceStatus status = InvoiceStatus.Pending)
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var context = db.Context;

        context.Municipalities.AddRange(
            new Municipality { Code = "3521705", Name = "Itaberá", StateAbbreviation = "SP" },
            new Municipality { Code = "3522406", Name = "Itapeva", StateAbbreviation = "SP" },
            new Municipality { Code = "2927408", Name = "Salvador", StateAbbreviation = "BA" });

        context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "CEAGUI", ShortName = "CEAGUI", TaxId = "12345678000195", StateCode = "SP",
            TaxRegime = TaxRegime.Normal, IssuesNfe = true, LegalName = "CEAGUI CEREAIS LTDA", TradeName = "CEAGUI",
            StateRegistration = "371012345110", Street = "RODOVIA SP 258", StreetNumber = "KM 290",
            District = "ZONA RURAL", MunicipalityCode = "3521705", ZipCode = "18440000", Phone = "1535621234",
        });

        context.BranchNfeSettings.Add(new BranchNfeSettings
        {
            BranchCode = "01", Environment = NfeEnvironment.Homologation, Series = 1, NextNumber = 1,
            CertificatePfx = TestCertificates.CreatePfx("12345678000195"),
            CertificatePasswordCipher = CertificatePasswordCipher.FromBase64(CertificateKey).Encrypt(TestCertificates.Password),
            CertificateSubject = "CN=CEAGUI CEREAIS LTDA:12345678000195", CertificateTaxId = "12345678000195",
            CertificateValidUntil = DateTime.Now.AddYears(1),
        });

        var usage = new Usage
        {
            Name = "Venda de grãos", InvoiceOperationText = "VENDA DE PRODUCAO DO ESTABELECIMENTO",
            DefaultAdditionalInfo = "Produto agrícola in natura.", CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
        };
        context.Usages.Add(usage);

        var condition = new PaymentCondition { Name = "30/60 boleto", Days = "30,60", PaymentMeans = "15" };
        context.PaymentConditions.Add(condition);
        await db.SaveChangesAsync();

        context.BusinessPartners.AddRange(
            new BusinessPartner
            {
                CardCode = CardCode, CardName = "CLIENTE BA LTDA", CardType = "C", TaxId = "11222333000181",
                StateRegistrationIndicator = StateRegistrationIndicator.Taxpayer, StateRegistration = "123456789",
                NfeEmail = "nfe@cliente.com.br", PaymentConditionCode = condition.Code,
                Addresses =
                [
                    new Address
                    {
                        CardCode = CardCode, AddressName = "FATURAMENTO", AdresType = "B", Street = "AV SETE DE SETEMBRO",
                        StreetNumber = "100", Block = "CENTRO", ZipCode = "40060000", City = "Salvador", State = "BA",
                        Country = "BR", MunicipalityCode = "2927408",
                    },
                ],
            },
            new BusinessPartner
            {
                CardCode = "T-001", CardName = "TRANSPORTADORA TESTE LTDA", CardType = "S", TaxId = "33444555000122",
                Addresses =
                [
                    new Address
                    {
                        CardCode = "T-001", AddressName = "FATURAMENTO", AdresType = "B", Street = "RUA B",
                        StreetNumber = "10", Block = "CENTRO", City = "Itapeva", State = "SP", MunicipalityCode = "3522406",
                    },
                ],
            });

        context.States.Add(new State { Code = "35", Name = "São Paulo", Abbreviation = "SP" });
        context.Trucks.Add(new Truck { Code = "ABC-1D23", StateKey = "35" });

        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = CardCode, CardName = "CLIENTE BA LTDA",
            InvoiceType = SalesInvoiceType.Normal, InvoiceStatus = status, InvoiceNumber = "000002388",
            InvoiceDate = new DateTime(2026, 10, 2), GrossWeight = 30500m, NetWeight = 30000m,
            TruckingCompanyCode = "T-001", TruckCode = "ABC-1D23", FreightTerms = FreightTerms.Cif,
            PaymentConditionCode = condition.Code, TaxPayerComments = "Pedido do cliente 77",
            Items =
            [
                new SalesInvoiceItem
                {
                    Key = Guid.NewGuid(), ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
                    Quantity = 30000m, UnitPrice = 2m, UsageCode = usage.Code, UsageName = usage.Name, Cfop = "6102",
                    Ncm = "12019000", GoodsOrigin = 0,
                    CstIcms = "00", IcmsBase = 60000m, IcmsRate = 7m, IcmsValue = 4200m,
                    CstPis = "01", PisBase = 55800m, PisRate = 1.65m, PisValue = 920.70m,
                    CstCofins = "01", CofinsBase = 55800m, CofinsRate = 7.6m, CofinsValue = 4240.80m,
                    IbsCbsCst = "000", IbsCbsClassCode = "000001", IbsCbsBase = 50638.50m,
                    CbsRate = 0.9m, CbsValue = 455.75m, IbsStateRate = 0.1m, IbsStateValue = 50.64m,
                },
            ],
        };
        context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return new NfeScenario(db, name, invoice.Key);
    }
}
