using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.BusinessPartners;

/// <summary>
/// Campos que a NF-e exige do destinatário e da transportadora (só STANDALONE — o serviço local
/// só existe nesse modo). A cidade e a UF do endereço passam a vir do município: é a UF do
/// endereço de faturamento que decide CFOP e tributos.
/// </summary>
public class BusinessPartnerNfeFieldsTests
{
    private static BusinessPartnerService Partners(UnitOfWork db) =>
        new(db, NullLogger<BusinessPartnerService>.Instance, new FakeStringLocalizer<Resource>());

    private static BusinessPartnerAddressService Addresses(UnitOfWork db) =>
        new(db, NullLogger<BusinessPartnerAddressService>.Instance, new FakeStringLocalizer<Resource>());

    private static async Task<UnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Municipalities.Add(new Municipality { Code = "2927408", Name = "Salvador", StateAbbreviation = "BA" });
        await db.SaveChangesAsync();
        return db;
    }

    private static BusinessPartnerModel Customer(StateRegistrationIndicator? indicator = StateRegistrationIndicator.Taxpayer,
        string? ie = "123456789") => new()
    {
        CardCode = "C001", CardName = "CLIENTE BA LTDA", CardType = "C", TaxId = "11222333000181",
        StateRegistration = ie, StateRegistrationIndicator = indicator, NfeEmail = "nfe@cliente.com.br",
        Phone = "71999990000", PaymentConditionCode = 3,
        Addresses =
        [
            new AddressModel
            {
                AddressName = "FATURAMENTO", AdresType = "B", Street = "AV SETE", StreetNumber = "100",
                Complement = "SALA 2", Block = "CENTRO", ZipCode = "40000000", MunicipalityCode = "2927408",
                City = "digitado errado", State = "XX", Country = "BR",
            },
        ],
    };

    [Fact]
    public async Task Partner_nfe_fields_round_trip()
    {
        var db = await SeedAsync();

        await Partners(db).CreateAsync(Customer());
        var read = await Partners(db).GetByIdAsync("C001");

        Assert.Equal("123456789", read!.StateRegistration);
        Assert.Equal(StateRegistrationIndicator.Taxpayer, read.StateRegistrationIndicator);
        Assert.Equal("nfe@cliente.com.br", read.NfeEmail);
        Assert.Equal("71999990000", read.Phone);
        Assert.Equal(3, read.PaymentConditionCode);

        var address = Assert.Single(read.Addresses);
        Assert.Equal("100", address.StreetNumber);
        Assert.Equal("SALA 2", address.Complement);
        Assert.Equal("2927408", address.MunicipalityCode);
    }

    [Fact]
    public async Task City_and_state_come_from_the_municipality_on_create()
    {
        var db = await SeedAsync();

        await Partners(db).CreateAsync(Customer());

        var saved = await db.Context.Set<Address>().AsNoTracking().SingleAsync();
        Assert.Equal("Salvador", saved.City);
        Assert.Equal("BA", saved.State);
    }

    [Fact]
    public async Task Taxpayer_requires_state_registration()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Partners(db).CreateAsync(Customer(ie: null)));

        Assert.Contains("inscrição estadual", ex.Message);
    }

    [Fact]
    public async Task Taxpayer_state_registration_must_be_digits()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Partners(db).CreateAsync(Customer(ie: "ISENTO")));

        Assert.Contains("só dígitos", ex.Message);
    }

    [Fact]
    public async Task Non_taxpayer_without_state_registration_is_valid()
    {
        var db = await SeedAsync();

        await Partners(db).CreateAsync(Customer(StateRegistrationIndicator.NonTaxpayer, ie: null));

        Assert.Equal(1, await db.Context.BusinessPartners.CountAsync());
    }

    [Fact]
    public async Task Update_persists_partner_nfe_fields()
    {
        var db = await SeedAsync();
        await Partners(db).CreateAsync(Customer());

        var changed = Customer(StateRegistrationIndicator.Exempt, ie: null);
        changed.NfeEmail = "fiscal@cliente.com.br";
        await Partners(db).UpdateAsync("C001", changed);

        var saved = await db.Context.BusinessPartners.AsNoTracking().SingleAsync();
        Assert.Equal(StateRegistrationIndicator.Exempt, saved.StateRegistrationIndicator);
        Assert.Equal("fiscal@cliente.com.br", saved.NfeEmail);
    }

    [Fact]
    public async Task Address_service_derives_city_and_state_and_keeps_number()
    {
        var db = await SeedAsync();
        db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = "C002", CardName = "OUTRO" });
        await db.SaveChangesAsync();

        await Addresses(db).Create("C002", new AddressModel
        {
            AddressName = "ENTREGA", AdresType = "S", Street = "RUA A", StreetNumber = "S/N",
            MunicipalityCode = "2927408",
        });

        var saved = await db.Context.Set<Address>().AsNoTracking().SingleAsync(a => a.CardCode == "C002");
        Assert.Equal("S/N", saved.StreetNumber);
        Assert.Equal("Salvador", saved.City);
        Assert.Equal("BA", saved.State);
    }

    [Fact]
    public async Task Unknown_municipality_is_rejected()
    {
        var db = await SeedAsync();
        var customer = Customer();
        customer.Addresses.Single().MunicipalityCode = "0000000";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Partners(db).CreateAsync(customer));

        Assert.Contains("0000000", ex.Message);
    }
}
