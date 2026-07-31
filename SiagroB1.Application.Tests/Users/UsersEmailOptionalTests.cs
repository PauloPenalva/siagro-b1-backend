using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Users;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Users;

/// <summary>
/// O e-mail é opcional, como no OUSR do SAP - mais da metade dos usuários espelhados de lá não
/// tem endereço.
///
/// O risco não é a ausência em si: `IX_USERS_Email` é um índice único FILTRADO
/// (<c>WHERE [Email] IS NOT NULL</c>), então vários nulos convivem sem problema. O risco é a
/// **string vazia**, que a tela envia quando o campo fica em branco: ela não é nula, entra no
/// índice, e o segundo usuário sem e-mail seria recusado por chave duplicada.
/// </summary>
public class UsersEmailOptionalTests
{
    private static CommonDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static UsersCreateService CreateService(CommonDbContext db) =>
        new(db, new FakeStringLocalizer<Resource>(), new TestLogger<UsersCreateService>());

    private static UsersUpdateService UpdateService(CommonDbContext db) =>
        new(db, new FakeStringLocalizer<Resource>(), new TestLogger<UsersUpdateService>());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutEmail_StoresNullInsteadOfEmptyString(string? email)
    {
        var db = CreateDb();

        await CreateService(db).ExecuteAsync(new User
        {
            Username = "jsilva",
            FullName = "João da Silva",
            Email = email,
            Password = "1234"
        });

        Assert.Null(db.Users.Single().Email);
    }

    [Fact]
    public async Task Create_TwoUsersWithoutEmail_BothAreStored()
    {
        var db = CreateDb();
        var service = CreateService(db);

        await service.ExecuteAsync(new User { Username = "jsilva", FullName = "João", Email = "", Password = "1234" });
        await service.ExecuteAsync(new User { Username = "msouza", FullName = "Maria", Email = "", Password = "1234" });

        Assert.Equal(2, db.Users.Count());
        Assert.All(db.Users, u => Assert.Null(u.Email));
    }

    [Fact]
    public async Task Create_TrimsTheEmail()
    {
        var db = CreateDb();

        await CreateService(db).ExecuteAsync(new User
        {
            Username = "jsilva", FullName = "João", Email = "  joao@empresa.com  ", Password = "1234"
        });

        Assert.Equal("joao@empresa.com", db.Users.Single().Email);
    }

    /// <summary>Apagar o e-mail de quem já tinha um deve gravar nulo, não string vazia.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_ClearingTheEmail_StoresNull(string email)
    {
        var db = CreateDb();
        var user = new User { Username = "jsilva", FullName = "João", Email = "joao@empresa.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await UpdateService(db).ExecuteAsync(user.Id, new User
        {
            Username = "jsilva", FullName = "João da Silva", Email = email, IsActive = true
        });

        Assert.Null(db.Users.Single().Email);
        Assert.Equal("João da Silva", db.Users.Single().FullName);
    }

    [Fact]
    public async Task Update_KeepsAValidEmail()
    {
        var db = CreateDb();
        var user = new User { Username = "jsilva", FullName = "João", Email = null };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await UpdateService(db).ExecuteAsync(user.Id, new User
        {
            Username = "jsilva", FullName = "João", Email = " joao@empresa.com ", IsActive = true
        });

        Assert.Equal("joao@empresa.com", db.Users.Single().Email);
    }
}
