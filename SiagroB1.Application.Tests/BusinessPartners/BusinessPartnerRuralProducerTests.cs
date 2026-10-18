using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.BusinessPartners;

public class BusinessPartnerRuralProducerTests
{
    private sealed class PassthroughLocalizer : IStringLocalizer<Resource>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private static BusinessPartnerService NewService() =>
        new(Support.TestDb.CreateUnitOfWork(), NullLogger<BusinessPartnerService>.Instance, new PassthroughLocalizer());

    private static BusinessPartnerModel Model(string taxId, bool rural, RuralProducerType? type = null,
        string? cpf = null, bool payroll = false) => new()
    {
        CardCode = "F0001",
        CardName = "PRODUTOR",
        CardType = "S",
        TaxId = taxId,
        IsRuralProducer = rural,
        RuralProducerType = type,
        RuralProducerCpf = cpf,
        RuralPayrollOption = payroll,
    };

    [Fact]
    public async Task Rural_producer_requires_a_type()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NewService().CreateAsync(Model("52998224725", true)));
        Assert.Equal("Informe o tipo de produtor rural.", ex.Message);
    }

    [Fact]
    public async Task Individual_producer_with_cnpj_requires_a_valid_cpf()
    {
        var svc = NewService();
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            svc.CreateAsync(Model("11222333000181", true, RuralProducerType.Individual)));
        Assert.Equal("Informe o CPF do produtor rural.", ex.Message);

        ex = await Assert.ThrowsAsync<DefaultException>(() =>
            svc.CreateAsync(Model("11222333000181", true, RuralProducerType.Individual, "12345678900")));
        Assert.Equal("CPF do produtor rural inválido.", ex.Message);
    }

    [Fact]
    public async Task Individual_producer_with_cpf_tax_id_does_not_need_a_second_cpf()
    {
        var svc = NewService();
        await svc.CreateAsync(Model("52998224725", true, RuralProducerType.Individual));
        var read = await svc.GetByIdAsync("F0001");
        Assert.True(read!.IsRuralProducer);
        Assert.Null(read.RuralProducerCpf);
    }

    [Fact]
    public async Task Legal_entity_producer_drops_cpf_and_payroll_option()
    {
        var svc = NewService();
        await svc.CreateAsync(Model("11222333000181", true, RuralProducerType.LegalEntity, "52998224725", true));
        var read = await svc.GetByIdAsync("F0001");
        Assert.Equal(RuralProducerType.LegalEntity, read!.RuralProducerType);
        Assert.Null(read.RuralProducerCpf);
        Assert.False(read.RuralPayrollOption);
    }

    [Fact]
    public async Task Clearing_rural_producer_clears_type_cpf_and_payroll()
    {
        var svc = NewService();
        await svc.CreateAsync(Model("11222333000181", true, RuralProducerType.Individual, "529.982.247-25", true));
        var created = await svc.GetByIdAsync("F0001");
        Assert.Equal("52998224725", created!.RuralProducerCpf);
        Assert.True(created.RuralPayrollOption);

        await svc.UpdateAsync("F0001", Model("11222333000181", false, RuralProducerType.Individual, "52998224725", true));
        var read = await svc.GetByIdAsync("F0001");
        Assert.False(read!.IsRuralProducer);
        Assert.Null(read.RuralProducerType);
        Assert.Null(read.RuralProducerCpf);
        Assert.False(read.RuralPayrollOption);
    }

    [Fact]
    public async Task Rural_fields_round_trip_through_get_and_query()
    {
        var svc = NewService();
        await svc.CreateAsync(Model("11222333000181", true, RuralProducerType.SpecialInsured, "52998224725", true));

        var byId = await svc.GetByIdAsync("F0001");
        var queried = await svc.QueryAll().SingleAsync(x => x.CardCode == "F0001");
        foreach (var m in new[] { byId!, queried })
        {
            Assert.True(m.IsRuralProducer);
            Assert.Equal(RuralProducerType.SpecialInsured, m.RuralProducerType);
            Assert.Equal("52998224725", m.RuralProducerCpf);
            Assert.True(m.RuralPayrollOption);
        }
    }

    [Fact]
    public async Task Cnpj_typed_as_producer_cpf_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NewService().CreateAsync(Model("11222333000181", true, RuralProducerType.Individual, "11222333000181")));
        Assert.Equal("CPF do produtor rural inválido.", ex.Message);
    }

    [Fact]
    public async Task Informed_cpf_is_validated_even_when_tax_id_is_a_cpf()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NewService().CreateAsync(Model("52998224725", true, RuralProducerType.Individual, "123")));
        Assert.Equal("CPF do produtor rural inválido.", ex.Message);
    }
}
