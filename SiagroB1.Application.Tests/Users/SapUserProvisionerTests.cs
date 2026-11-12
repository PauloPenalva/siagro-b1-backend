using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Services.SapUsers;

namespace SiagroB1.Application.Tests.Users;

/// <summary>
/// O provisionamento pontual (login e "esqueci minha senha") precisa ignorar os mesmos usuários
/// que a varredura ignora. Do contrário, quem digita o e-mail na recuperação de senha recria o
/// usuário da instalação antiga do SAP - que tem o mesmo endereço do usuário atual (GAC-1220).
/// </summary>
public class SapUserProvisionerTests
{
    private static (CommonDbContext Common, SapErpDbContext Sap, SapUserProvisioner Provisioner) Create()
    {
        var name = Guid.NewGuid().ToString();
        var common = new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase($"common-{name}").Options);
        var sap = new SapErpDbContext(new DbContextOptionsBuilder<SapErpDbContext>()
            .UseInMemoryDatabase($"sap-{name}").Options);
        var configuration = new ConfigurationBuilder().Build();

        return (common, sap,
            new SapUserProvisioner(common, sap, configuration, new TestLogger<SapUserProvisioner>()));
    }

    [Fact]
    public async Task EnsureAsync_ByEmailSharedWithLegacyUser_ProvisionsOnlyTheCurrentUser()
    {
        var (common, sap, provisioner) = Create();
        sap.SapUsers.Add(new SapUser { Id = 1, UserCode = "it001", UserName = "Yuji", Email = "yuji@empresa.com", Group = 99 });
        sap.SapUsers.Add(new SapUser { Id = 2, UserCode = "Yuji", UserName = "Yuji", Email = "yuji@empresa.com", Group = 1 });
        sap.SaveChanges();

        await provisioner.EnsureAsync("yuji@empresa.com");

        var user = Assert.Single(common.Users);
        Assert.Equal("Yuji", user.Username);
        Assert.Equal("yuji@empresa.com", user.Email);
    }

    [Fact]
    public async Task EnsureAsync_ByUserCodeOfLegacyUser_CreatesNothing()
    {
        var (common, sap, provisioner) = Create();
        sap.SapUsers.Add(new SapUser { Id = 1, UserCode = "it001", UserName = "Yuji", Email = "yuji@empresa.com", Group = 99 });
        sap.SaveChanges();

        await provisioner.EnsureAsync("it001");

        Assert.Empty(common.Users);
    }
}
