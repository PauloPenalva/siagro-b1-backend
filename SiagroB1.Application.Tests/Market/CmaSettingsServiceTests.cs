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
    [Theory]
    [InlineData("http://feed.exemplo.com")]
    [InlineData("feed.exemplo.com")]
    [InlineData("https://feed.exemplo.com/execute")]
    [InlineData("https://feed.exemplo.com/?a=1")]
    [InlineData("https://feed.exemplo.com/#x")]
    public async Task CmaSettings_InvalidHost_Throws(string host)
    {
        var (service, _) = Create();

        await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(Input() with { Host = host }));
    }

    [Theory]
    [InlineData("https://feed.exemplo.com:443")]
    [InlineData("https://feed.exemplo.com/")]
    public async Task CmaSettings_ValidHost_Saved(string host)
    {
        var (service, _) = Create();

        var saved = await service.SaveAsync(Input() with { Host = host });

        Assert.Equal(host, saved.Host);
    }

    [Fact]
    public async Task CmaSettings_BlankHost_KeepsPrevious()
    {
        var (service, _) = Create();
        await service.SaveAsync(Input());

        var saved = await service.SaveAsync(Input("") with { Host = "  " });

        Assert.Equal("https://feed.exemplo.com:443", saved.Host);
    }

    [Fact]
    public async Task CmaSettings_HostChange_RequiresPassword()
    {
        var (service, _) = Create();
        await service.SaveAsync(Input());

        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            service.SaveAsync(Input("") with { Host = "https://outro.exemplo.com" }));
        Assert.Equal("Informe a senha novamente ao alterar o host", error.Message);

        var saved = await service.SaveAsync(Input("nova") with { Host = "https://outro.exemplo.com" });
        Assert.Equal("https://outro.exemplo.com", saved.Host);
        Assert.Equal("nova", (await service.GetCredentialsAsync())!.Password);
    }

    // --- Usuário da CMA SEM senha (caso real: a conta de streaming da CMA pode ter senha em branco) ---

    [Fact]
    public async Task CmaSettings_BlankPassword_ConnectorGetsEmptyPassword()
    {
        var (service, db) = Create();

        var view = await service.SaveAsync(Input(null) with { BlankPassword = true });

        Assert.True(view.HasPassword);
        Assert.True(view.BlankPassword);
        Assert.True((await service.GetAsync()).BlankPassword);
        var credentials = await service.GetCredentialsAsync();
        Assert.NotNull(credentials);
        Assert.Equal("", credentials!.Password);
        Assert.Equal("usuario", credentials.UserName);
        // Continua cifrado no banco (não é coluna vazia): "em branco" é uma escolha gravada, não ausência de senha.
        Assert.NotEmpty((await db.Context.CmaSettings.AsNoTracking().SingleAsync()).PasswordEncrypted!);
    }

    [Fact]
    public async Task CmaSettings_BlankPassword_ReplacesStoredPassword_AndTypedPasswordReplacesBlank()
    {
        var (service, _) = Create();
        await service.SaveAsync(Input("segredo"));
        Assert.False((await service.GetAsync()).BlankPassword);

        await service.SaveAsync(Input("ignorada") with { BlankPassword = true });
        Assert.Equal("", (await service.GetCredentialsAsync())!.Password);

        var view = await service.SaveAsync(Input("nova"));
        Assert.False(view.BlankPassword);
        Assert.Equal("nova", (await service.GetCredentialsAsync())!.Password);
    }

    [Fact]
    public async Task CmaSettings_BlankPassword_KeptWhenSavingWithoutTouchingThePassword()
    {
        var (service, _) = Create();
        await service.SaveAsync(Input(null) with { BlankPassword = true });

        // A tela reenvia o que mostrou: marcado "sem senha" e campo de senha vazio.
        var view = await service.SaveAsync(Input("") with { BlankPassword = true, UserName = "outro" });

        Assert.True(view.BlankPassword);
        Assert.Equal("", (await service.GetCredentialsAsync())!.Password);
    }

    [Fact]
    public async Task CmaSettings_NoPasswordAndNotBlank_ConnectorStaysOff()
    {
        var (service, _) = Create();

        var view = await service.SaveAsync(Input(null));

        Assert.False(view.HasPassword);
        Assert.False(view.BlankPassword);
        Assert.Null(await service.GetCredentialsAsync());
    }

    [Fact]
    public async Task CmaSettings_BlankPassword_WithoutKey_Throws()
    {
        var (service, _) = Create(key: "");

        await Assert.ThrowsAsync<BusinessException>(() => service.SaveAsync(Input(null) with { BlankPassword = true }));
    }

    [Fact]
    public async Task CmaSettings_HostChange_WithBlankPassword_DoesNotAskForThePasswordAgain()
    {
        var (service, _) = Create();
        await service.SaveAsync(Input("segredo"));

        var view = await service.SaveAsync(Input(null) with { Host = "https://outro.exemplo.com:443", BlankPassword = true });

        Assert.Equal("https://outro.exemplo.com:443", view.Host);
        Assert.Equal("", (await service.GetCredentialsAsync())!.Password);
    }
}
