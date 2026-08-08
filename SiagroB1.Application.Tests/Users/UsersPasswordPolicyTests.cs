using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Users;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Users;

/// <summary>
/// A senha definida pelo administrador na criação do usuário segue a mesma regra do reset por
/// e-mail e da troca no perfil. O XML doc da <see cref="PasswordPolicy"/> sempre afirmou isso -
/// mas a criação era o único dos três caminhos que não a consultava, e aceitava qualquer senha.
///
/// Senha em branco continua permitida: é o usuário que ainda não tem acesso e vai entrar pelo
/// "esqueci minha senha". O que a regra cobre é a senha FRACA, não a ausência dela.
/// </summary>
public class UsersPasswordPolicyTests
{
    private static CommonDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static UsersCreateService CreateService(CommonDbContext db, int minimumLength = 4)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:PasswordPolicy:MinimumLength"] = minimumLength.ToString()
            })
            .Build();

        return new UsersCreateService(
            db,
            new FakeStringLocalizer<Resource>(),
            new PasswordPolicy(configuration),
            new TestLogger<UsersCreateService>());
    }

    private static User NewUser(string? password) =>
        new() { Username = "jsilva", FullName = "João da Silva", Password = password };

    [Fact]
    public async Task Create_WithAPasswordShorterThanTheMinimum_IsRejected()
    {
        var db = CreateDb();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => CreateService(db, minimumLength: 6).ExecuteAsync(NewUser("123")));

        Assert.Contains("6", error.Message);
        Assert.Empty(db.Users);
    }

    [Fact]
    public async Task Create_WithAPasswordThatMeetsTheMinimum_IsStoredHashed()
    {
        var db = CreateDb();

        await CreateService(db, minimumLength: 6).ExecuteAsync(NewUser("senha123"));

        var hash = db.Users.Single().PasswordHash;

        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.DoesNotContain("senha123", hash);
    }

    /// <summary>Sem senha o usuário nasce sem hash - e a regra de tamanho não incide.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutAPassword_IsAllowedAndLeavesNoHash(string? password)
    {
        var db = CreateDb();

        await CreateService(db, minimumLength: 6).ExecuteAsync(NewUser(password));

        Assert.Null(db.Users.Single().PasswordHash);
    }
}
