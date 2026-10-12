using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Infra.Context;
using SapWarehouseService = SiagroB1.Application.Services.SAP.WarehouseService;

namespace SiagroB1.Application.Tests.Warehouses;

/// <summary>
/// A lista de armazéns ganhou a coluna Cidade/UF nos dois modos. Em SAPB1 ela pegava o primeiro
/// endereço qualquer (<c>Addresses.ToList().FirstOrDefault()</c>) — podia ser o de cobrança, e a
/// expressão ia para o ORDER BY/WHERE ao ordenar ou filtrar a coluna. Agora segue a mesma regra do
/// STANDALONE e do LoadWarehousesAsync: primeiro endereço de ENTREGA pelo nome.
/// </summary>
public class WarehouseServiceSapTests
{
    private static async Task<SapErpDbContext> Seeded()
    {
        var sap = new SapErpDbContext(new DbContextOptionsBuilder<SapErpDbContext>()
            .UseInMemoryDatabase($"sap-{Guid.NewGuid()}").Options);
        sap.BusinessPartners.Add(new BusinessPartner { CardCode = "ARM001", CardName = "ARMAZEM GERAL", QryGroup23 = "Y" });
        sap.AddRange(
            new Address { CardCode = "ARM001", AddressName = "COBRANCA", AdresType = "B", City = "SAO PAULO", State = "SP" },
            new Address { CardCode = "ARM001", AddressName = "Z-ENTREGA", AdresType = "S", City = "CURITIBA", State = "PR" },
            new Address { CardCode = "ARM001", AddressName = "A-ENTREGA", AdresType = "S", City = "ITABERA", State = "SP" });
        await sap.SaveChangesAsync();
        return sap;
    }

    [Fact]
    public async Task QueryAll_takes_city_and_state_from_the_first_ship_to_address()
    {
        var sap = await Seeded();
        var service = new SapWarehouseService(sap, NullLogger<SapWarehouseService>.Instance, new ConfigurationBuilder().Build());

        var warehouse = await service.QueryAll().SingleAsync();

        Assert.Equal("ITABERA", warehouse.City);
        Assert.Equal("SP", warehouse.State);
    }
}
