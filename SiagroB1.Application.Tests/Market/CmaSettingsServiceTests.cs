using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Market;

public class CmaSettingsServiceTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static (CmaSettingsService Service, SiagroB1.Infra.UnitOfWork Db) Create(string? key = null)
    {
        var db = TestDb.CreateUnitOfWork();
        var options = new NfeOptions(TestConfiguration.With(("Nfe:CertificateKey", key ?? Key)));
        return (new CmaSettingsService(db.Context, options), db);
    }

    private static CmaSettingsInput Input(string? password = "segredo") =>
        new(true, "https://feed.exemplo.com:443", "usuario", password);

    [Fact]
    public async Task CmaSettings_Save_EncryptsPassword_AndGetNeverReturnsIt()
    {
        var (service, db) = Create();

        await service.SaveAsync(Input());
        var got = await service.GetAsync();

        Assert.True(got.Enabled);
        Assert.True(got.HasPassword);
        Assert.Equal("usuario", got.UserName);
        var row = await db.Context.CmaSettings.AsNoTracking().SingleAsync();
        Assert.NotNull(row.PasswordEncrypted);
        Assert.DoesNotContain("segredo", System.Text.Encoding.UTF8.GetString(row.PasswordEncrypted!));
        var credentials = await service.GetCredentialsAsync();
        Assert.Equal("segredo", credentials!.Password);
        Assert.Equal("usuario", credentials.UserName);
    }

    [Fact]
    public async Task CmaSettings_SaveWithEmptyPassword_KeepsPrevious()
    {
        var (service, db) = Create();
        await service.SaveAsync(Input("primeira"));
        var before = (await db.Context.CmaSettings.AsNoTracking().SingleAsync()).PasswordEncrypted;

        await service.SaveAsync(Input("") with { UserName = "outro" });

        var row = await db.Context.CmaSettings.AsNoTracking().SingleAsync();
        Assert.Equal(before, row.PasswordEncrypted);
        Assert.Equal("outro", row.UserName);
        Assert.Equal("primeira", (await service.GetCredentialsAsync())!.Password);
    }

    [Fact]
    public async Task CmaSettings_SaveWithPassword_WithoutKey_Throws()
    {
        var (service, _) = Create(key: "");

        var error = await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(Input()));

        Assert.Contains("Nfe:CertificateKey", error.Message);
    }

    [Fact]
    public async Task GetCredentials_WhenDisabled_ReturnsNull()
    {
        var (service, _) = Create();
        await service.SaveAsync(Input() with { Enabled = false });

        Assert.Null(await service.GetCredentialsAsync());
    }
}
