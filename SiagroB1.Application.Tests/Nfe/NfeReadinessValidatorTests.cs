using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>
/// Prontidão do cadastro antes de reservar número (spec §9.2 passo 2): UMA mensagem com tudo o
/// que falta, para o usuário corrigir de uma vez.
/// </summary>
public class NfeReadinessValidatorTests
{
    private static async Task<NfeIssueContext> ValidateAsync(NfeScenario scenario, bool withKey = true)
    {
        var invoice = await scenario.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == scenario.InvoiceKey);
        return await new NfeReadinessValidator(scenario.Db, new NfeOptions(NfeTestSeed.Config(withKey: withKey))).ValidateAsync(invoice);
    }

    [Fact]
    public async Task Complete_scenario_returns_the_loaded_context()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var context = await ValidateAsync(scenario);

        Assert.Equal("01", context.Branch.Code);
        Assert.Equal("Salvador", context.CustomerMunicipality.Name);
        Assert.Equal("T-001", context.Carrier!.CardCode);
        Assert.Equal("ABC1D23", context.TruckPlate);
        Assert.Equal("SP", context.TruckState);
        Assert.Equal("30/60 boleto", context.PaymentCondition.Name);
    }

    [Fact]
    public async Task Missing_data_is_listed_in_a_single_message()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var branch = await scenario.Db.Context.Branchs.SingleAsync();
        branch.StateRegistration = null;
        var address = await scenario.Db.Context.Addresses.SingleAsync(a => a.CardCode == NfeTestSeed.CardCode);
        address.MunicipalityCode = null;
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync();
        invoice.PaymentConditionCode = null;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario));

        Assert.StartsWith("Faltam dados para emitir a NF-e:", ex.Message);
        Assert.Contains("inscrição estadual", ex.Message);
        Assert.Contains("município do endereço de faturamento", ex.Message);
        Assert.Contains("condição de pagamento", ex.Message);
    }

    [Fact]
    public async Task Expired_certificate_is_reported()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var settings = await scenario.Db.Context.BranchNfeSettings.SingleAsync();
        settings.CertificateValidUntil = new DateTime(2026, 1, 31);
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario));

        Assert.Contains("vencido em 31/01/2026", ex.Message);
    }

    [Fact]
    public async Task Missing_server_key_is_reported()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario, withKey: false));

        Assert.Contains("Nfe:CertificateKey", ex.Message);
    }

    [Fact]
    public async Task Masked_tax_id_is_accepted()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var customer = await scenario.Db.Context.BusinessPartners.SingleAsync(p => p.CardCode == NfeTestSeed.CardCode);
        customer.TaxId = "11.222.333/0001-81";
        await scenario.Db.SaveChangesAsync();

        var context = await ValidateAsync(scenario);

        Assert.Equal(NfeTestSeed.CardCode, context.Customer.CardCode);
    }

    [Fact]
    public async Task Inactive_payment_condition_is_reported()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.PaymentConditions.SingleAsync()).Inactive = true;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario));

        Assert.Contains("inativa", ex.Message);
    }

    [Fact]
    public async Task Taxpayer_with_non_numeric_state_registration_is_reported()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.BusinessPartners.SingleAsync(p => p.CardCode == NfeTestSeed.CardCode)).StateRegistration = "ISENTO";
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario));

        Assert.Contains("inscrição estadual", ex.Message);
    }

    [Fact]
    public async Task Branch_zip_code_without_eight_digits_is_reported()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.Branchs.SingleAsync()).ZipCode = "1844";
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario));

        Assert.Contains("Filial 01", ex.Message);
        Assert.Contains("CEP", ex.Message);
    }

    [Fact]
    public async Task Delivery_partner_gaps_are_reported()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        scenario.Db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = "E-SP", CardName = "ARMAZEM SEM DADOS", CardType = "C" });
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).DeliveryCardCode = "E-SP";
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario));

        Assert.Contains("Local de entrega", ex.Message);
        Assert.Contains("CNPJ/CPF", ex.Message);
    }

    [Fact]
    public async Task Interstate_cfop_for_a_same_state_customer_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var address = await scenario.Db.Context.Addresses.SingleAsync(a => a.CardCode == NfeTestSeed.CardCode);
        address.MunicipalityCode = "3522406"; // Itapeva/SP: mesma UF da filial, e o item tem CFOP 6102
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario));

        Assert.Contains("Item 1: CFOP 6102 não confere com o destino (SP) — salve o item de novo para recalcular.", ex.Message);
    }

    [Fact]
    public async Task In_state_cfop_for_an_out_of_state_customer_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.SalesInvoicesItems.SingleAsync()).Cfop = "5102";
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario));

        Assert.Contains("Item 1: CFOP 5102 não confere com o destino (BA)", ex.Message);
    }
}
