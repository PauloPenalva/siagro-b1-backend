using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.BusinessPartners;

/// <summary>
/// Leitura dos endereços do parceiro STANDALONE (<c>GET BusinessPartners('X')/Addresses</c>, que a
/// tela relê logo depois de criar o parceiro). <c>CardCode</c> faz parte da chave do endereço no
/// EDM e não aceita nulo: sem ele na projeção a serialização estourava no meio da resposta e a
/// conexão caía — a tela mostrava "500" mesmo com o parceiro gravado.
/// </summary>
public class BusinessPartnerAddressServiceTests
{
    private static async Task<BusinessPartnerAddressService> Seeded(SiagroB1.Infra.UnitOfWork db)
    {
        db.Context.Addresses.Add(new Address
        {
            CardCode = "C90002", AddressName = "FATURAMENTO", AdresType = "B", State = "BA",
        });
        await db.SaveChangesAsync();

        return new BusinessPartnerAddressService(
            db, NullLogger<BusinessPartnerAddressService>.Instance, new FakeStringLocalizer<Resource>());
    }

    [Fact]
    public async Task QueryAll_projects_the_card_code_of_the_key()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = await Seeded(db);

        var address = await service.QueryAll("C90002").SingleAsync();

        Assert.Equal("C90002", address.CardCode);
    }

    [Fact]
    public async Task GetById_projects_the_card_code_of_the_key()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = await Seeded(db);

        var address = await service.GetByIdAsync("C90002", "FATURAMENTO", "B");

        Assert.Equal("C90002", address!.CardCode);
    }

    private static AddressModel NewAddress(string type) => new()
    {
        AddressName = "ENTREGA", AdresType = type, Street = "RUA A", City = "CIDADE", State = "BA", Country = "BR",
    };

    private static async Task<BusinessPartnerAddressService> WithPartner(SiagroB1.Infra.UnitOfWork db)
    {
        db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = "C90002", CardName = "CLIENTE" });
        await db.SaveChangesAsync();

        return new BusinessPartnerAddressService(
            db, NullLogger<BusinessPartnerAddressService>.Instance, new FakeStringLocalizer<Resource>());
    }

    [Fact]
    public async Task Create_rejects_an_address_type_other_than_bill_to_or_ship_to()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = await WithPartner(db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.Create("C90002", NewAddress("X")));

        Assert.Equal("BP_ADDRESS_INVALID_TYPE", ex.Message);
        Assert.False(await db.Context.Addresses.AnyAsync());
    }

    [Theory]
    [InlineData("B")]
    [InlineData("S")]
    public async Task Create_accepts_bill_to_and_ship_to(string type)
    {
        var db = TestDb.CreateUnitOfWork();
        var service = await WithPartner(db);

        await service.Create("C90002", NewAddress(type));

        Assert.Equal(type, (await db.Context.Addresses.AsNoTracking().SingleAsync()).AdresType);
    }
}
