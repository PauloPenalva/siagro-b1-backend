using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.BusinessPartners;

/// <summary>
/// Parceiro STANDALONE criado pela tela com os endereços aninhados (deep insert). O endereço de
/// faturamento é de onde sai a UF do CFOP e do cálculo de tributos — parceiro que nasce sem
/// endereço trava o documento de saída.
/// </summary>
public class BusinessPartnerServiceAddressesTests
{
    private static BusinessPartnerService Service(UnitOfWork db) =>
        new(db, NullLogger<BusinessPartnerService>.Instance, new FakeStringLocalizer<Resource>());

    private static BusinessPartnerModel Partner(params AddressModel[] addresses) => new()
    {
        CardCode = "C90001", CardName = "CLIENTE TESTE", CardType = "C", TaxId = "12345678000199",
        Addresses = addresses.ToList(),
    };

    private static AddressModel Address(string name, string type, string state) => new()
    {
        AddressName = name, AdresType = type, Street = "RUA A", City = "CIDADE", State = state, Country = "BR",
    };

    [Fact]
    public async Task Create_persists_nested_addresses()
    {
        var db = TestDb.CreateUnitOfWork();

        await Service(db).CreateAsync(Partner(Address("FATURAMENTO", "B", "PR"), Address("ENTREGA", "S", "SP")));

        var stored = await db.Context.Set<Address>().AsNoTracking().Where(a => a.CardCode == "C90001").ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, a => a is { AdresType: "B", State: "PR" });
        Assert.Contains(stored, a => a is { AdresType: "S", State: "SP" });
    }

    /// <summary>
    /// O CardCode faz parte da chave do endereço no EDM (AddressName, AdresType, CardCode). A lista (QueryAll) o deixava
    /// nulo, e o value help de clientes — que expande só os endereços de entrega (S) — devolvia 500 assim que algum
    /// cliente tinha um: "The property 'CardCode[Nullable=False]' … has a null value".
    /// </summary>
    [Fact]
    public async Task QueryAll_fills_the_card_code_of_every_address()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Partner(Address("FATURAMENTO", "B", "PR"), Address("ENTREGA", "S", "SP")));

        var partner = await Service(db).QueryAll().SingleAsync(p => p.CardCode == "C90001");

        Assert.Equal(2, partner.Addresses.Count);
        Assert.All(partner.Addresses, a => Assert.Equal("C90001", a.CardCode));
    }

    [Fact]
    public async Task Create_without_addresses_still_works()
    {
        var db = TestDb.CreateUnitOfWork();

        await Service(db).CreateAsync(Partner());

        Assert.True(await db.Context.BusinessPartners.AnyAsync(b => b.CardCode == "C90001"));
        Assert.Empty(await db.Context.Set<Address>().ToListAsync());
    }

    [Fact]
    public async Task GetById_returns_the_created_addresses()
    {
        var db = TestDb.CreateUnitOfWork();
        await Service(db).CreateAsync(Partner(Address("FATURAMENTO", "B", "PR"), Address("ENTREGA", "S", "SP")));

        var read = await Service(db).GetByIdAsync("C90001");

        Assert.Equal(2, read!.Addresses.Count);
    }
}
