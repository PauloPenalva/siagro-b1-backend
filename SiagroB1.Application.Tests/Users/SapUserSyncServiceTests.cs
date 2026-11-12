using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Users;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Users;

/// <summary>
/// Em modo SAPB1 o cadastro de usuários é mantido no SAP e o SiagroB1 apenas o espelha. Os riscos
/// aqui não aparecem em tela: desativar o admin tranca todo mundo para fora, um e-mail repetido
/// derruba a sincronização inteira pelo índice único, e sobrescrever o hash de senha apagaria a
/// credencial de quem já usava o sistema.
/// </summary>
public class SapUserSyncServiceTests
{
    private static (CommonDbContext Common, SapErpDbContext Sap) CreateDbs()
    {
        var name = Guid.NewGuid().ToString();

        return (
            new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>()
                .UseInMemoryDatabase($"common-{name}").Options),
            new SapErpDbContext(new DbContextOptionsBuilder<SapErpDbContext>()
                .UseInMemoryDatabase($"sap-{name}").Options));
    }

    private static SapUserSyncService CreateService(
        CommonDbContext common, SapErpDbContext sap, string[]? protectedUsernames = null,
        short[]? ignoredGroups = null)
    {
        var settings = new Dictionary<string, string?>();

        if (ignoredGroups is not null)
        {
            for (var i = 0; i < ignoredGroups.Length; i++)
            {
                settings[$"SapUserSync:IgnoredGroups:{i}"] = ignoredGroups[i].ToString();
            }
        }

        if (protectedUsernames is not null)
        {
            for (var i = 0; i < protectedUsernames.Length; i++)
            {
                settings[$"SapUserSync:ProtectedUsernames:{i}"] = protectedUsernames[i];
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new SapUserSyncService(common, sap, configuration, new TestLogger<SapUserSyncService>());
    }

    private static void AddSapUser(
        SapErpDbContext sap, short id, string code, string? name, string? email, string locked = "N",
        short? group = null)
    {
        sap.SapUsers.Add(new SapUser
        {
            Id = id,
            UserCode = code,
            UserName = name,
            Email = email,
            Locked = locked,
            Group = group
        });
        sap.SaveChanges();
    }

    [Fact]
    public async Task ExecuteAsync_CreatesUserFromOusr()
    {
        var (common, sap) = CreateDbs();
        AddSapUser(sap, 1, "jsilva", "João da Silva", "joao@empresa.com");

        var result = await CreateService(common, sap).ExecuteAsync();

        Assert.Equal(1, result.Created);
        var user = Assert.Single(common.Users);
        Assert.Equal("jsilva", user.Username);
        Assert.Equal("João da Silva", user.FullName);
        Assert.Equal("joao@empresa.com", user.Email);
        Assert.True(user.IsActive);

        // Nasce sem senha: o primeiro acesso é pelo "esqueci minha senha".
        Assert.Null(user.PasswordHash);
        Assert.False(user.IsAdmin);
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesNameAndEmailFromOusr()
    {
        var (common, sap) = CreateDbs();
        common.Users.Add(new User
        {
            Username = "jsilva", FullName = "Nome Antigo", Email = "antigo@empresa.com", IsActive = true
        });
        common.SaveChanges();
        AddSapUser(sap, 1, "jsilva", "João da Silva", "joao@empresa.com");

        var result = await CreateService(common, sap).ExecuteAsync();

        Assert.Equal(1, result.Updated);
        var user = common.Users.Single();
        Assert.Equal("João da Silva", user.FullName);
        Assert.Equal("joao@empresa.com", user.Email);
    }

    /// <summary>
    /// Senha, perfil de acesso, tema e foto são do SiagroB1. O SAP manda apenas em identificação
    /// e situação — sobrescrever o resto apagaria a credencial e as permissões de quem já usava.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PreservesLocalOnlyFields()
    {
        var (common, sap) = CreateDbs();
        var hash = PasswordHasher.Hash("senhaDoUsuario1");
        common.Users.Add(new User
        {
            Username = "jsilva",
            FullName = "João",
            Email = "joao@empresa.com",
            PasswordHash = hash,
            IsAdmin = true,
            Theme = "sap_horizon_dark",
            PhotoContent = [1, 2, 3],
            PhotoContentType = "image/png",
            IsActive = true
        });
        common.SaveChanges();
        AddSapUser(sap, 1, "jsilva", "João da Silva", "joao@empresa.com");

        await CreateService(common, sap).ExecuteAsync();

        var user = common.Users.Single();
        Assert.Equal(hash, user.PasswordHash);
        Assert.True(user.IsAdmin);
        Assert.Equal("sap_horizon_dark", user.Theme);
        Assert.Equal([1, 2, 3], user.PhotoContent);
    }

    [Fact]
    public async Task ExecuteAsync_LockedInSap_DeactivatesLocally()
    {
        var (common, sap) = CreateDbs();
        AddSapUser(sap, 1, "jsilva", "João", "joao@empresa.com", locked: "Y");

        await CreateService(common, sap).ExecuteAsync();

        Assert.False(common.Users.Single().IsActive);
    }

    [Fact]
    public async Task ExecuteAsync_UnlockedInSap_ReactivatesLocally()
    {
        var (common, sap) = CreateDbs();
        common.Users.Add(new User { Username = "jsilva", FullName = "João", IsActive = false });
        common.SaveChanges();
        AddSapUser(sap, 1, "jsilva", "João", null);

        await CreateService(common, sap).ExecuteAsync();

        Assert.True(common.Users.Single().IsActive);
    }

    /// <summary>Sumiu do SAP: fica inativo, mas continua existindo - autoria e histórico apontam para ele.</summary>
    [Fact]
    public async Task ExecuteAsync_UserMissingFromOusr_IsDeactivatedButNeverDeleted()
    {
        var (common, sap) = CreateDbs();
        common.Users.Add(new User { Username = "demitido", FullName = "Ex-Funcionário", IsActive = true });
        common.SaveChanges();
        AddSapUser(sap, 1, "jsilva", "João", "joao@empresa.com");

        var result = await CreateService(common, sap).ExecuteAsync();

        Assert.Equal(1, result.Deactivated);
        var demitido = common.Users.Single(u => u.Username == "demitido");
        Assert.False(demitido.IsActive);
        Assert.Equal(2, common.Users.Count());
    }

    /// <summary>
    /// O admin é local e nunca existiu no SAP. Desativá-lo na primeira execução trancaria todo
    /// mundo para fora do sistema — inclusive quem faria a correção.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NeverDeactivatesTheLocalAdmin()
    {
        var (common, sap) = CreateDbs();
        common.Users.Add(new User { Username = "admin", FullName = "Administrator", IsAdmin = true, IsActive = true });
        common.SaveChanges();
        AddSapUser(sap, 1, "jsilva", "João", "joao@empresa.com");

        var result = await CreateService(common, sap).ExecuteAsync();

        Assert.True(common.Users.Single(u => u.Username == "admin").IsActive);
        Assert.Equal(0, result.Deactivated);
    }

    [Fact]
    public async Task ExecuteAsync_HonorsConfiguredProtectedUsernames()
    {
        var (common, sap) = CreateDbs();
        common.Users.Add(new User { Username = "integracao", FullName = "Serviço", IsActive = true });
        common.Users.Add(new User { Username = "admin", FullName = "Administrator", IsActive = true });
        common.SaveChanges();
        AddSapUser(sap, 1, "jsilva", "João", null);

        await CreateService(common, sap, ["integracao"]).ExecuteAsync();

        Assert.True(common.Users.Single(u => u.Username == "integracao").IsActive);

        // Lista configurada substitui o padrão: o admin deixa de estar protegido.
        Assert.False(common.Users.Single(u => u.Username == "admin").IsActive);
    }

    /// <summary>
    /// USERS.Email tem índice único. Dois usuários do SAP com o mesmo endereço fariam o
    /// SaveChanges estourar e nenhum dos dois seria gravado.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_DuplicatedEmailInOusr_KeepsOnlyTheFirstAndDiscardsTheOther()
    {
        var (common, sap) = CreateDbs();
        AddSapUser(sap, 1, "jsilva", "João", "compartilhado@empresa.com");
        AddSapUser(sap, 2, "msouza", "Maria", "compartilhado@empresa.com");

        var result = await CreateService(common, sap).ExecuteAsync();

        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.EmailsDiscarded);
        Assert.Single(common.Users, u => u.Email == "compartilhado@empresa.com");
        Assert.Single(common.Users, u => u.Email == null);
    }

    /// <summary>
    /// O dono do e-mail aqui é um usuário local protegido (não vem do SAP e continua ativo): o
    /// endereço é dele e o usuário do SAP fica sem.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_EmailAlreadyUsedByProtectedLocalUser_IsDiscarded()
    {
        var (common, sap) = CreateDbs();
        common.Users.Add(new User { Username = "admin", FullName = "Administrator", Email = "joao@empresa.com", IsActive = true });
        common.SaveChanges();
        AddSapUser(sap, 1, "jsilva", "João", "joao@empresa.com");

        var result = await CreateService(common, sap).ExecuteAsync();

        Assert.Equal(1, result.EmailsDiscarded);
        Assert.Null(common.Users.Single(u => u.Username == "jsilva").Email);
        Assert.Equal("joao@empresa.com", common.Users.Single(u => u.Username == "admin").Email);
    }

    /// <summary>
    /// Quem não existe mais no SAP é desativado nesta mesma rodada - não pode continuar segurando
    /// o e-mail de um usuário válido, que ficaria sem conseguir recuperar a senha.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_EmailHeldByUserMissingFromOusr_IsHandedToTheSapUser()
    {
        var (common, sap) = CreateDbs();
        common.Users.Add(new User { Username = "outro", FullName = "Outro", Email = "joao@empresa.com", IsActive = true });
        common.SaveChanges();
        AddSapUser(sap, 1, "jsilva", "João", "joao@empresa.com");

        var result = await CreateService(common, sap).ExecuteAsync();

        Assert.Equal(0, result.EmailsDiscarded);
        Assert.Equal("joao@empresa.com", common.Users.Single(u => u.Username == "jsilva").Email);
        var outro = common.Users.Single(u => u.Username == "outro");
        Assert.Null(outro.Email);
        Assert.False(outro.IsActive);
    }

    /// <summary>
    /// GAC-1220: o OUSR guarda usuários de uma instalação antiga do SAP (GROUPS = 99), invisíveis
    /// na aplicação do SAP. Eles não são usuários do sistema e não podem ser espelhados.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_UserFromIgnoredGroup_IsNotCreated()
    {
        var (common, sap) = CreateDbs();
        AddSapUser(sap, 1, "it001", "Yuji", "yuji@empresa.com", group: 99);
        AddSapUser(sap, 2, "Yuji", "Yuji", null, group: 1);

        var result = await CreateService(common, sap).ExecuteAsync();

        Assert.Equal(1, result.Created);
        Assert.Equal("Yuji", Assert.Single(common.Users).Username);
    }

    /// <summary>
    /// O caso do chamado: "it001" (antigo) já foi espelhado numa rodada anterior e segura o e-mail
    /// do "Yuji". Ao passar a ser ignorado, ele é desativado e o e-mail vai para o usuário válido.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AlreadyMirroredUserFromIgnoredGroup_IsDeactivatedAndReleasesTheEmail()
    {
        var (common, sap) = CreateDbs();
        common.Users.Add(new User { Username = "it001", FullName = "Yuji", Email = "yuji@empresa.com", IsActive = true });
        common.Users.Add(new User { Username = "Yuji", FullName = "Yuji", Email = null, IsActive = true });
        common.SaveChanges();
        AddSapUser(sap, 1, "it001", "Yuji", "yuji@empresa.com", group: 99);
        AddSapUser(sap, 2, "Yuji", "Yuji", "yuji@empresa.com", group: 1);

        var result = await CreateService(common, sap).ExecuteAsync();

        Assert.Equal(1, result.Deactivated);
        Assert.Equal(0, result.EmailsDiscarded);
        var legacy = common.Users.Single(u => u.Username == "it001");
        Assert.False(legacy.IsActive);
        Assert.Null(legacy.Email);
        Assert.Equal("yuji@empresa.com", common.Users.Single(u => u.Username == "Yuji").Email);
    }

    [Fact]
    public async Task ExecuteAsync_HonorsConfiguredIgnoredGroups()
    {
        var (common, sap) = CreateDbs();
        AddSapUser(sap, 1, "legado", "Legado", null, group: 50);
        AddSapUser(sap, 2, "atual", "Atual", null, group: 99);

        await CreateService(common, sap, ignoredGroups: [50]).ExecuteAsync();

        // Lista configurada substitui o padrão: o grupo 99 deixa de ser ignorado.
        Assert.Equal("atual", Assert.Single(common.Users).Username);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyEmailInOusr_BecomesNullInsteadOfEmptyString()
    {
        var (common, sap) = CreateDbs();
        AddSapUser(sap, 1, "jsilva", "João", "   ");
        AddSapUser(sap, 2, "msouza", "Maria", null);

        await CreateService(common, sap).ExecuteAsync();

        // String vazia repetida colidiria no índice único tanto quanto um e-mail repetido.
        Assert.All(common.Users, u => Assert.Null(u.Email));
    }

    /// <summary>USERS.FullName é VARCHAR(100) e OUSR.U_NAME vai até 155: truncar evita erro de gravação.</summary>
    [Fact]
    public async Task ExecuteAsync_LongNameIsTruncatedToTheColumnSize()
    {
        var (common, sap) = CreateDbs();
        AddSapUser(sap, 1, "jsilva", new string('A', 155), null);

        await CreateService(common, sap).ExecuteAsync();

        Assert.Equal(100, common.Users.Single().FullName.Length);
    }

    [Fact]
    public async Task ExecuteAsync_UserWithoutNameInOusr_FallsBackToTheUserCode()
    {
        var (common, sap) = CreateDbs();
        AddSapUser(sap, 1, "jsilva", null, null);

        await CreateService(common, sap).ExecuteAsync();

        Assert.Equal("jsilva", common.Users.Single().FullName);
    }

    /// <summary>
    /// USERS.Username tem índice único com collation Latin1_General_100_CI_AI - que ignora
    /// acentos. O OUSR do cliente tem "Joao" e "João" como usuários distintos; gravar os dois
    /// viola o índice e aborta a sincronização inteira, deixando o cadastro sem NENHUMA
    /// atualização. Este caso não aparece em nenhuma tela: só contra o banco real.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_NamesDifferingOnlyByAccent_KeepsTheFirstAndSkipsTheOther()
    {
        var (common, sap) = CreateDbs();
        AddSapUser(sap, 76, "Joao", "Joao da Silva", "joao@empresa.com");
        AddSapUser(sap, 165, "João", "João Pereira", "joao.pereira@empresa.com");

        var result = await CreateService(common, sap).ExecuteAsync();

        Assert.Equal(1, result.Created);
        Assert.Equal(1, result.Skipped);
        var user = Assert.Single(common.Users);
        Assert.Equal("Joao", user.Username);
    }

    [Fact]
    public async Task ExecuteAsync_ExistingUserDifferingByAccentOrCase_IsUpdatedNotDuplicated()
    {
        var (common, sap) = CreateDbs();
        common.Users.Add(new User { Username = "JOÃO", FullName = "Nome Antigo", IsActive = true });
        common.SaveChanges();
        AddSapUser(sap, 76, "joao", "João da Silva", null);

        var result = await CreateService(common, sap).ExecuteAsync();

        Assert.Equal(0, result.Created);
        Assert.Equal(0, result.Deactivated);
        var user = Assert.Single(common.Users);
        Assert.Equal("João da Silva", user.FullName);
        // O nome de login local não é reescrito: é a chave que sessões e auditoria já usam.
        Assert.Equal("JOÃO", user.Username);
    }

    /// <summary>E-mail também tem índice CI_AI: "José@x.com" e "jose@x.com" colidem no banco.</summary>
    [Fact]
    public async Task ExecuteAsync_EmailsDifferingOnlyByAccent_KeepOnlyOne()
    {
        var (common, sap) = CreateDbs();
        AddSapUser(sap, 1, "jsilva", "José", "josé@empresa.com");
        AddSapUser(sap, 2, "msouza", "Maria", "jose@empresa.com");

        var result = await CreateService(common, sap).ExecuteAsync();

        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.EmailsDiscarded);
        Assert.Single(common.Users, u => u.Email == null);
    }

    [Fact]
    public async Task ExecuteAsync_RunningTwice_DoesNotDuplicateOrReportChanges()
    {
        var (common, sap) = CreateDbs();
        AddSapUser(sap, 1, "jsilva", "João da Silva", "joao@empresa.com");
        var service = CreateService(common, sap);

        await service.ExecuteAsync();
        var second = await service.ExecuteAsync();

        Assert.Single(common.Users);
        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Updated);
        Assert.Equal(0, second.Deactivated);
    }
}
