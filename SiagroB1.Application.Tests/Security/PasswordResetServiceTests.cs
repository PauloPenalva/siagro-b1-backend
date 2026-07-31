using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Services;
using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// A recuperação de senha é a única porta que um anônimo tem para dentro de uma conta. Cada caso
/// aqui fecha uma fresta: token que sobrevive ao uso, token de outro usuário, enumeração de
/// usuários pela resposta, sessão antiga que continuaria valendo depois da troca.
/// </summary>
public class PasswordResetServiceTests
{
    private static CommonDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (PasswordResetService Service, FakeEmailSender Email, FakeSapUserProvisioner Sap)
        CreateService(CommonDbContext db, int minimumLength = 4, int? maxRequests = null,
            TestLogger<PasswordResetService>? logger = null)
    {
        var email = new FakeEmailSender();
        var sap = new FakeSapUserProvisioner();
        var settings = new Dictionary<string, string?>
        {
            ["Security:AppBaseUrl"] = "http://localhost:5246",
            ["Security:PasswordPolicy:MinimumLength"] = minimumLength.ToString()
        };

        if (maxRequests is not null)
        {
            settings["Security:PasswordReset:MaxRequestsPerWindow"] = maxRequests.Value.ToString();
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return (
            new PasswordResetService(
                db, email, sap, new PasswordPolicy(configuration), configuration,
                logger ?? new TestLogger<PasswordResetService>()),
            email,
            sap);
    }

    private static User AddUser(CommonDbContext db, string username = "joao", string? email = "joao@empresa.com")
    {
        var user = new User
        {
            Username = username,
            FullName = "João da Silva",
            Email = email,
            PasswordHash = PasswordHasher.Hash("senhaAntiga1"),
            IsActive = true
        };

        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    /// <summary>Extrai o token do link que foi para o e-mail - é o único lugar onde ele existe em claro.</summary>
    private static string TokenFromEmail(FakeEmailSender email) => TokenFrom(email.Sent.Single().Body);

    private static string TokenFrom(string body)
    {
        var start = body.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var end = body.IndexOfAny(['"', '\'', '<', ' '], start);
        return body[start..(end < 0 ? body.Length : end)];
    }

    [Fact]
    public async Task RequestAsync_SendsLinkAndStoresOnlyTheHash()
    {
        var db = CreateDb();
        var user = AddUser(db);
        var (service, email, _) = CreateService(db);

        await service.RequestAsync("joao", "10.0.0.1");

        var sent = Assert.Single(email.Sent);
        Assert.Equal("joao@empresa.com", sent.To);

        var stored = Assert.Single(db.PasswordResetTokens);
        Assert.Equal(user.Id, stored.UserId);
        Assert.Equal("10.0.0.1", stored.RequestIp);
        Assert.Null(stored.UsedAt);

        // O token em claro não pode estar gravado em lugar nenhum.
        var token = TokenFromEmail(email);
        Assert.NotEqual(token, stored.TokenHash);
        Assert.DoesNotContain(token, stored.TokenHash);
    }

    [Fact]
    public async Task RequestAsync_AcceptsEmailInsteadOfUsername()
    {
        var db = CreateDb();
        AddUser(db);
        var (service, email, _) = CreateService(db);

        await service.RequestAsync("JOAO@EMPRESA.COM", null);

        Assert.Single(email.Sent);
    }

    /// <summary>
    /// Usuário inexistente não pode se distinguir de um existente: nem por exceção, nem pela
    /// ausência de resposta. O endpoint responde igual nos dois casos.
    /// </summary>
    [Fact]
    public async Task RequestAsync_UnknownUser_DoesNothingAndDoesNotThrow()
    {
        var db = CreateDb();
        AddUser(db);
        var (service, email, _) = CreateService(db);

        await service.RequestAsync("nao-existe", null);

        Assert.Empty(email.Sent);
        Assert.Empty(db.PasswordResetTokens);
    }

    [Fact]
    public async Task RequestAsync_InactiveUser_DoesNotSend()
    {
        var db = CreateDb();
        var user = AddUser(db);
        user.IsActive = false;
        db.SaveChanges();
        var (service, email, _) = CreateService(db);

        await service.RequestAsync("joao", null);

        Assert.Empty(email.Sent);
    }

    /// <summary>Sem e-mail cadastrado não há para onde mandar - e não adianta gerar token.</summary>
    [Fact]
    public async Task RequestAsync_UserWithoutEmail_DoesNotCreateToken()
    {
        var db = CreateDb();
        AddUser(db, email: null);
        var (service, emailSender, _) = CreateService(db);

        await service.RequestAsync("joao", null);

        Assert.Empty(emailSender.Sent);
        Assert.Empty(db.PasswordResetTokens);
    }

    /// <summary>
    /// Em modo SAPB1 o usuário pode existir só no OUSR. O provisionamento roda ANTES da busca,
    /// senão quem acabou de ser cadastrado no SAP não conseguiria definir a primeira senha.
    /// </summary>
    [Fact]
    public async Task RequestAsync_ProvisionsFromSapBeforeLookingUpTheUser()
    {
        var db = CreateDb();
        var (service, email, sap) = CreateService(db);
        sap.OnEnsure = _ =>
        {
            AddUser(db, "maria", "maria@empresa.com");
            return Task.CompletedTask;
        };

        await service.RequestAsync("maria", null);

        Assert.Equal("maria", Assert.Single(sap.Calls));
        Assert.Equal("maria@empresa.com", Assert.Single(email.Sent).To);
    }

    [Fact]
    public async Task RequestAsync_MoreThanThreeTimesInTheWindow_StopsSending()
    {
        var db = CreateDb();
        AddUser(db);
        var (service, email, _) = CreateService(db);

        for (var i = 0; i < 5; i++)
        {
            await service.RequestAsync("joao", null);
        }

        Assert.Equal(3, email.Sent.Count);
        Assert.Equal(3, db.PasswordResetTokens.Count());
    }

    [Fact]
    public async Task RequestAsync_HonorsAConfiguredRequestLimit()
    {
        var db = CreateDb();
        AddUser(db);
        var (service, email, _) = CreateService(db, maxRequests: 5);

        for (var i = 0; i < 7; i++)
        {
            await service.RequestAsync("joao", null);
        }

        Assert.Equal(5, email.Sent.Count);
    }

    /// <summary>
    /// Desistir por usuário inexistente, por falta de e-mail ou por excesso de pedidos produz na
    /// tela exatamente a mesma resposta de um envio bem-sucedido - é o preço de não deixar o
    /// endpoint público virar verificador de contas. Só o log distingue os casos, então cada
    /// desfecho precisa sair em Warning e com o mesmo prefixo, senão a falha é indiagnosticável.
    /// </summary>
    [Fact]
    public async Task RequestAsync_EverySilentOutcome_IsLoggedAsWarningWithTheSamePrefix()
    {
        var db = CreateDb();
        AddUser(db);
        AddUser(db, "semEmail", email: null);
        var logger = new TestLogger<PasswordResetService>();
        var (service, _, _) = CreateService(db, maxRequests: 1, logger: logger);

        await service.RequestAsync("nao-existe", null);   // usuário desconhecido
        await service.RequestAsync("semEmail", null);     // sem e-mail cadastrado
        await service.RequestAsync("joao", null);         // envia
        await service.RequestAsync("joao", null);         // excede o limite

        var warnings = logger.Entries
            .Where(e => e.Level == LogLevel.Warning && e.Message.Contains("RECUPERACAO-SENHA"))
            .ToList();

        Assert.Equal(4, warnings.Count);
        Assert.Contains(warnings, w => w.Message.Contains("nenhum usuário ATIVO"));
        Assert.Contains(warnings, w => w.Message.Contains("não tem e-mail"));
        Assert.Contains(warnings, w => w.Message.Contains("link gerado"));
        Assert.Contains(warnings, w => w.Message.Contains("excedeu o limite"));
    }

    [Fact]
    public async Task ResetAsync_ChangesThePasswordAndConsumesTheToken()
    {
        var db = CreateDb();
        var user = AddUser(db);
        var (service, email, _) = CreateService(db);
        await service.RequestAsync("joao", null);
        var token = TokenFromEmail(email);

        var result = await service.ResetAsync(token, "novaSenha1");

        Assert.True(result.Success);
        Assert.True(PasswordHasher.Verify(db.Users.Single().PasswordHash, "novaSenha1", out _));
        Assert.NotNull(db.PasswordResetTokens.Single().UsedAt);
        Assert.Equal(user.Id, db.PasswordResetTokens.Single().UserId);
    }

    /// <summary>
    /// O link fica no e-mail para sempre. Se o token continuasse valendo depois do uso, qualquer
    /// um com acesso à caixa de entrada retomaria a conta meses depois.
    /// </summary>
    [Fact]
    public async Task ResetAsync_TokenUsedTwice_IsRefusedOnTheSecondTime()
    {
        var db = CreateDb();
        AddUser(db);
        var (service, email, _) = CreateService(db);
        await service.RequestAsync("joao", null);
        var token = TokenFromEmail(email);

        Assert.True((await service.ResetAsync(token, "novaSenha1")).Success);
        var second = await service.ResetAsync(token, "outraSenha2");

        Assert.False(second.Success);
        Assert.True(PasswordHasher.Verify(db.Users.Single().PasswordHash, "novaSenha1", out _));
    }

    [Fact]
    public async Task ResetAsync_ExpiredToken_IsRefused()
    {
        var db = CreateDb();
        AddUser(db);
        var (service, email, _) = CreateService(db);
        await service.RequestAsync("joao", null);
        var token = TokenFromEmail(email);

        var stored = db.PasswordResetTokens.Single();
        stored.ExpiresAt = DateTime.Now.AddMinutes(-1);
        db.SaveChanges();

        var result = await service.ResetAsync(token, "novaSenha1");

        Assert.False(result.Success);
        Assert.True(PasswordHasher.Verify(db.Users.Single().PasswordHash, "senhaAntiga1", out _));
    }

    [Fact]
    public async Task ResetAsync_UnknownToken_IsRefused()
    {
        var db = CreateDb();
        AddUser(db);
        var (service, _, _) = CreateService(db);

        var result = await service.ResetAsync("token-que-nunca-existiu", "novaSenha1");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ResetAsync_PasswordBelowTheMinimum_IsRefusedAndKeepsTheTokenUsable()
    {
        var db = CreateDb();
        AddUser(db);
        var (service, email, _) = CreateService(db, minimumLength: 6);
        await service.RequestAsync("joao", null);
        var token = TokenFromEmail(email);

        Assert.False((await service.ResetAsync(token, "123")).Success);

        // Recusar por senha curta não pode queimar o token: o usuário tenta de novo na mesma tela.
        Assert.True((await service.ResetAsync(token, "novaSenha1")).Success);
    }

    /// <summary>
    /// A política acompanha o padrão do SAP, onde as senhas em uso são curtas e só de dígitos.
    /// Exigir mais aqui do que o SAP exige lá deixaria parte dos usuários sem conseguir repetir a
    /// senha que já usam.
    /// </summary>
    [Theory]
    [InlineData("250825")]
    [InlineData("1234")]
    public async Task ResetAsync_ShortNumericPassword_IsAcceptedByDefault(string password)
    {
        var db = CreateDb();
        AddUser(db);
        var (service, email, _) = CreateService(db);
        await service.RequestAsync("joao", null);
        var token = TokenFromEmail(email);

        Assert.True((await service.ResetAsync(token, password)).Success);
        Assert.True(PasswordHasher.Verify(db.Users.Single().PasswordHash, password, out _));
    }

    /// <summary>
    /// Quem redefine a senha normalmente é quem perdeu o controle da conta. Deixar as sessões
    /// abertas manteria o invasor logado apesar da troca.
    /// </summary>
    [Fact]
    public async Task ResetAsync_KillsActiveSessionsAndOtherPendingTokens()
    {
        var db = CreateDb();
        var user = AddUser(db);
        db.UserSessions.Add(new UserSession
        {
            SessionId = "sessao-aberta",
            UserId = user.Id,
            ExpiresAt = DateTime.Now.AddHours(8),
            IsActive = true
        });
        db.SaveChanges();

        var (service, email, _) = CreateService(db);
        await service.RequestAsync("joao", null);
        await service.RequestAsync("joao", null);
        var token = TokenFrom(email.Sent[0].Body);

        Assert.True((await service.ResetAsync(token, "novaSenha1")).Success);

        Assert.False(db.UserSessions.Single().IsActive);
        Assert.All(db.PasswordResetTokens, t => Assert.NotNull(t.UsedAt));
    }

    /// <summary>Token de um usuário não pode ser aceito enquanto outro usuário responde pela conta.</summary>
    [Fact]
    public async Task ResetAsync_DoesNotTouchOtherUsers()
    {
        var db = CreateDb();
        AddUser(db);
        AddUser(db, "maria", "maria@empresa.com");
        var (service, email, _) = CreateService(db);
        await service.RequestAsync("joao", null);
        var token = TokenFromEmail(email);

        Assert.True((await service.ResetAsync(token, "novaSenha1")).Success);

        var maria = db.Users.Single(u => u.Username == "maria");
        Assert.True(PasswordHasher.Verify(maria.PasswordHash, "senhaAntiga1", out _));
    }

    [Fact]
    public async Task ValidateAsync_ReflectsTheTokenState()
    {
        var db = CreateDb();
        AddUser(db);
        var (service, email, _) = CreateService(db);
        await service.RequestAsync("joao", null);
        var token = TokenFromEmail(email);

        Assert.True(await service.ValidateAsync(token));
        Assert.False(await service.ValidateAsync("outro-token"));

        await service.ResetAsync(token, "novaSenha1");
        Assert.False(await service.ValidateAsync(token));
    }
}
