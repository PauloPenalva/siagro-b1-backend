using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Companies;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Companies;

public class CompanyLogoServiceTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private readonly CommonDbContext _db = new(new DbContextOptionsBuilder<CommonDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public CompanyLogoServiceTests()
    {
        _db.Companies.Add(new Company { Code = "0001", CompanyName = "RODOSIL", ConnectionString = "x" });
        _db.SaveChanges();
    }

    private CompanyLogoService Create() => new(_db);

    [Fact]
    public async Task Set_then_get_returns_the_image()
    {
        await Create().SetAsync("0001", "image/png", Convert.ToBase64String(Png));

        var logo = await Create().GetAsync("0001");

        Assert.NotNull(logo);
        Assert.Equal("image/png", logo!.Value.ContentType);
        Assert.Equal(Png, logo.Value.Content);
        Assert.True(await Create().ExistsAsync("0001"));
    }

    [Fact]
    public async Task Set_twice_replaces_the_image()
    {
        await Create().SetAsync("0001", "image/png", Convert.ToBase64String(Png));
        await Create().SetAsync("0001", "image/jpeg", Convert.ToBase64String([1, 2]));

        Assert.Single(_db.CompanyLogos);
        Assert.Equal("image/jpeg", (await Create().GetAsync("0001"))!.Value.ContentType);
    }

    [Fact]
    public async Task Get_without_logo_returns_null()
    {
        Assert.Null(await Create().GetAsync("0001"));
        Assert.False(await Create().ExistsAsync("0001"));
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Unsupported_type_is_refused(string? contentType)
    {
        await Assert.ThrowsAsync<DefaultException>(() => Create().SetAsync("0001", contentType, Convert.ToBase64String(Png)));
    }

    [Fact]
    public async Task Image_over_one_megabyte_is_refused()
    {
        var big = Convert.ToBase64String(new byte[CompanyLogoService.MaxBytes + 1]);

        await Assert.ThrowsAsync<DefaultException>(() => Create().SetAsync("0001", "image/png", big));
    }

    [Theory]
    [InlineData("@@nao-e-base64@@")]
    [InlineData("")]
    public async Task Invalid_or_empty_content_is_refused(string base64)
    {
        await Assert.ThrowsAsync<DefaultException>(() => Create().SetAsync("0001", "image/png", base64));
    }

    [Fact]
    public async Task Unknown_company_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Create().SetAsync("9999", "image/png", Convert.ToBase64String(Png)));
    }

    [Fact]
    public async Task Remove_deletes_the_logo_and_is_harmless_without_one()
    {
        await Create().SetAsync("0001", "image/png", Convert.ToBase64String(Png));

        await Create().RemoveAsync("0001");
        await Create().RemoveAsync("0001");

        Assert.Null(await Create().GetAsync("0001"));
    }
}
