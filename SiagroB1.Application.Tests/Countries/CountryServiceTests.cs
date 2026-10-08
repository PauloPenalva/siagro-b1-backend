using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.Countries;

/// <summary>Países: tabela de referência só leitura que alimenta o value help do endereço.</summary>
public class CountryServiceTests
{
    [Fact]
    public async Task Lists_and_finds_countries_by_iso_code()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Countries.Add(new Country { Code = "BR", BacenCode = "1058", Name = "BRASIL" });
        db.Context.Countries.Add(new Country { Code = "AR", BacenCode = "0639", Name = "ARGENTINA" });
        await db.SaveChangesAsync();
        var service = new CountryService(db);

        Assert.Equal(2, await service.QueryAll().CountAsync());
        Assert.Equal("1058", (await service.GetByIdAsync("BR"))!.BacenCode);
        Assert.Null(await service.GetByIdAsync("XX"));
    }
}
