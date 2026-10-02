using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;

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
}
