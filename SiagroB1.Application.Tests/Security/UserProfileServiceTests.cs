using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Services;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// A tela "Meu Perfil" é a única onde um usuário logado altera a própria conta. O que precisa
/// segurar aqui: a troca de senha exigir a senha atual, e nada aceitar valores que a UI não
/// consiga renderizar depois (tema inexistente, arquivo que não é imagem).
/// </summary>
public class UserProfileServiceTests
{
    private static CommonDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static UserProfileService CreateService(CommonDbContext db, int minimumLength = 4)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:PasswordPolicy:MinimumLength"] = minimumLength.ToString()
            })
            .Build();

        return new UserProfileService(db, new PasswordPolicy(configuration), new TestLogger<UserProfileService>());
    }

    private static void AddUser(CommonDbContext db, string password = "senhaAtual1")
    {
        db.Users.Add(new User
        {
            Username = "joao",
            FullName = "João da Silva",
            Email = "joao@empresa.com",
            PasswordHash = PasswordHasher.Hash(password),
            IsActive = true
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task ChangePasswordAsync_WithCorrectCurrentPassword_Changes()
    {
        var db = CreateDb();
        AddUser(db);

        var result = await CreateService(db).ChangePasswordAsync("joao", "senhaAtual1", "senhaNova1");

        Assert.True(result.Success);
        Assert.True(PasswordHasher.Verify(db.Users.Single().PasswordHash, "senhaNova1", out _));
    }

    /// <summary>
    /// Sem exigir a senha atual, um computador deixado desbloqueado permitiria a qualquer um
    /// assumir a conta em definitivo.
    /// </summary>
    [Fact]
    public async Task ChangePasswordAsync_WithWrongCurrentPassword_IsRefused()
    {
        var db = CreateDb();
        AddUser(db);

        var result = await CreateService(db).ChangePasswordAsync("joao", "chutei", "senhaNova1");

        Assert.False(result.Success);
        Assert.True(PasswordHasher.Verify(db.Users.Single().PasswordHash, "senhaAtual1", out _));
    }

    /// <summary>Usuário vindo do OUSR não tem hash: a troca com senha atual não pode virar porta de entrada.</summary>
    [Fact]
    public async Task ChangePasswordAsync_UserWithoutPassword_IsRefused()
    {
        var db = CreateDb();
        db.Users.Add(new User { Username = "joao", FullName = "João", PasswordHash = null });
        db.SaveChanges();

        var result = await CreateService(db).ChangePasswordAsync("joao", "", "senhaNova1");

        Assert.False(result.Success);
        Assert.Null(db.Users.Single().PasswordHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    public async Task ChangePasswordAsync_NewPasswordBelowTheMinimum_IsRefused(string newPassword)
    {
        var db = CreateDb();
        AddUser(db);

        var result = await CreateService(db).ChangePasswordAsync("joao", "senhaAtual1", newPassword);

        Assert.False(result.Success);
        Assert.True(PasswordHasher.Verify(db.Users.Single().PasswordHash, "senhaAtual1", out _));
    }

    /// <summary>
    /// A política padrão acompanha o SAP: senha curta e só de dígitos é aceita. Não é descuido -
    /// é a regra pedida, para o usuário poder repetir aqui a senha que já usa lá.
    /// </summary>
    [Theory]
    [InlineData("250825")]
    [InlineData("1234")]
    public async Task ChangePasswordAsync_ShortNumericPassword_IsAcceptedByDefault(string newPassword)
    {
        var db = CreateDb();
        AddUser(db);

        var result = await CreateService(db).ChangePasswordAsync("joao", "senhaAtual1", newPassword);

        Assert.True(result.Success);
        Assert.True(PasswordHasher.Verify(db.Users.Single().PasswordHash, newPassword, out _));
    }

    /// <summary>A regra é configurável: quem quiser apertar sobe o mínimo por ambiente.</summary>
    [Fact]
    public async Task ChangePasswordAsync_HonorsAConfiguredHigherMinimum()
    {
        var db = CreateDb();
        AddUser(db);

        var result = await CreateService(db, minimumLength: 8)
            .ChangePasswordAsync("joao", "senhaAtual1", "250825");

        Assert.False(result.Success);
        Assert.Contains("8", result.Message);
    }

    [Fact]
    public async Task GetProfileAsync_ReportsThePasswordRuleInForce()
    {
        var db = CreateDb();
        AddUser(db);

        var profile = await CreateService(db, minimumLength: 6).GetProfileAsync("joao");

        // A tela mostra este texto em vez de um aviso fixo, que divergiria da regra aplicada.
        Assert.Contains("6", profile!.PasswordRequirements);
    }

    [Fact]
    public async Task SetThemeAsync_AcceptsOnlyKnownThemes()
    {
        var db = CreateDb();
        AddUser(db);
        var service = CreateService(db);

        Assert.True((await service.SetThemeAsync("joao", "sap_horizon_dark")).Success);
        Assert.Equal("sap_horizon_dark", db.Users.Single().Theme);

        // Um tema inexistente deixaria a UI sem CSS - recusar é melhor do que gravar.
        Assert.False((await service.SetThemeAsync("joao", "tema_inventado")).Success);
        Assert.Equal("sap_horizon_dark", db.Users.Single().Theme);
    }

    [Fact]
    public async Task SetPhotoAsync_StoresContentAndContentType()
    {
        var db = CreateDb();
        AddUser(db);
        var content = new byte[] { 1, 2, 3, 4 };

        var result = await CreateService(db)
            .SetPhotoAsync("joao", "image/png", Convert.ToBase64String(content));

        Assert.True(result.Success);
        Assert.Equal(content, db.Users.Single().PhotoContent);
        Assert.Equal("image/png", db.Users.Single().PhotoContentType);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/html")]
    [InlineData(null)]
    public async Task SetPhotoAsync_NonImageContentType_IsRefused(string? contentType)
    {
        var db = CreateDb();
        AddUser(db);

        var result = await CreateService(db)
            .SetPhotoAsync("joao", contentType, Convert.ToBase64String([1, 2, 3]));

        Assert.False(result.Success);
        Assert.Null(db.Users.Single().PhotoContent);
    }

    [Fact]
    public async Task SetPhotoAsync_InvalidBase64_IsRefusedInsteadOfThrowing()
    {
        var db = CreateDb();
        AddUser(db);

        var result = await CreateService(db).SetPhotoAsync("joao", "image/png", "isto-nao-e-base64!!");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task SetPhotoAsync_AboveTheSizeLimit_IsRefused()
    {
        var db = CreateDb();
        AddUser(db);

        var result = await CreateService(db)
            .SetPhotoAsync("joao", "image/png", Convert.ToBase64String(new byte[2 * 1024 * 1024 + 1]));

        Assert.False(result.Success);
        Assert.Null(db.Users.Single().PhotoContent);
    }

    [Fact]
    public async Task RemovePhotoAsync_ClearsContentAndContentType()
    {
        var db = CreateDb();
        AddUser(db);
        await CreateService(db).SetPhotoAsync("joao", "image/png", Convert.ToBase64String([1, 2, 3]));

        Assert.True((await CreateService(db).RemovePhotoAsync("joao")).Success);

        Assert.Null(db.Users.Single().PhotoContent);
        Assert.Null(db.Users.Single().PhotoContentType);
    }

    /// <summary>O blob nunca vai no perfil - a shell pede a imagem no endpoint próprio.</summary>
    [Fact]
    public async Task GetProfileAsync_ReportsHasPhotoWithoutReturningTheBlob()
    {
        var db = CreateDb();
        AddUser(db);
        var service = CreateService(db);

        Assert.False((await service.GetProfileAsync("joao"))!.HasPhoto);

        await service.SetPhotoAsync("joao", "image/png", Convert.ToBase64String([1, 2, 3]));

        var profile = await service.GetProfileAsync("joao");
        Assert.True(profile!.HasPhoto);
        Assert.Equal("joao@empresa.com", profile.Email);
        Assert.Equal("João da Silva", profile.FullName);
    }

    [Fact]
    public async Task GetPhotoAsync_WithoutPhoto_ReturnsNull()
    {
        var db = CreateDb();
        AddUser(db);

        Assert.Null(await CreateService(db).GetPhotoAsync("joao"));
    }

    /// <summary>Um usuário não pode alcançar o perfil de outro nem por engano de resolução de nome.</summary>
    [Fact]
    public async Task Operations_OnUnknownUser_Fail()
    {
        var db = CreateDb();
        AddUser(db);
        var service = CreateService(db);

        Assert.Null(await service.GetProfileAsync("maria"));
        Assert.False((await service.SetThemeAsync("maria", "sap_horizon")).Success);
        Assert.False((await service.ChangePasswordAsync("maria", "senhaAtual1", "senhaNova1")).Success);
        Assert.True(PasswordHasher.Verify(db.Users.Single().PasswordHash, "senhaAtual1", out _));
    }
}
