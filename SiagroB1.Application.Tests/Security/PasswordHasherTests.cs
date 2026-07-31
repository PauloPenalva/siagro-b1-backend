using SiagroB1.Security.Shared;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// O hash guardado em USERS.PasswordHash é o único segredo que separa um estranho da conta do
/// usuário. Errar aqui não gera erro visível — o login continua funcionando e a senha é que fica
/// desprotegida.
/// </summary>
public class PasswordHasherTests
{
    [Fact]
    public void Hash_ProducesVerifiableHashInPbkdf2Format()
    {
        var hash = PasswordHasher.Hash("senha-do-usuario");

        Assert.StartsWith("PBKDF2$", hash);
        Assert.True(PasswordHasher.Verify(hash, "senha-do-usuario", out var needsUpgrade));
        Assert.False(needsUpgrade);
    }

    /// <summary>
    /// Salt aleatório: a mesma senha não pode gerar o mesmo hash duas vezes, senão uma rainbow
    /// table quebra o banco inteiro de uma vez.
    /// </summary>
    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        var first = PasswordHasher.Hash("senha-do-usuario");
        var second = PasswordHasher.Hash("senha-do-usuario");

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify(first, "senha-do-usuario", out _));
        Assert.True(PasswordHasher.Verify(second, "senha-do-usuario", out _));
    }

    /// <summary>
    /// A coluna é VARCHAR(256): um formato que não coubesse ali só falharia ao gravar, em produção.
    /// </summary>
    [Fact]
    public void Hash_FitsInThePasswordHashColumn()
    {
        Assert.True(PasswordHasher.Hash("senha-do-usuario").Length <= 256);
    }

    [Fact]
    public void Verify_WrongPassword_Fails()
    {
        var hash = PasswordHasher.Hash("senha-do-usuario");

        Assert.False(PasswordHasher.Verify(hash, "outra-senha", out _));
    }

    /// <summary>
    /// Todo mundo que já tem conta está com o hash antigo (SHA-256 sem salt). Se este caso falhar,
    /// ninguém consegue mais entrar depois do deploy.
    /// </summary>
    [Fact]
    public void Verify_LegacySha256Hash_SucceedsAndAsksForUpgrade()
    {
        var legacy = Utils.HashPassword("1234");

        Assert.True(PasswordHasher.Verify(legacy, "1234", out var needsUpgrade));
        Assert.True(needsUpgrade);
    }

    [Fact]
    public void Verify_LegacySha256Hash_WrongPassword_Fails()
    {
        var legacy = Utils.HashPassword("1234");

        Assert.False(PasswordHasher.Verify(legacy, "4321", out _));
    }

    /// <summary>
    /// Usuário criado pelo sync do OUSR nasce sem hash. Ele não pode logar com senha nenhuma —
    /// muito menos com string vazia.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_UserWithoutPassword_NeverAuthenticates(string? storedHash)
    {
        Assert.False(PasswordHasher.Verify(storedHash, "qualquer-senha", out _));
        Assert.False(PasswordHasher.Verify(storedHash, "", out _));
    }

    /// <summary>
    /// Um hash corrompido no banco (edição manual, truncamento) deve recusar o login em vez de
    /// estourar exceção e derrubar o endpoint.
    /// </summary>
    [Theory]
    [InlineData("PBKDF2$")]
    [InlineData("PBKDF2$abc$def")]
    [InlineData("PBKDF2$210000$nao-e-base64$nao-e-base64")]
    [InlineData("PBKDF2$210000$c2FsdA==$aGFzaA==$sobrando")]
    public void Verify_MalformedHash_ReturnsFalseInsteadOfThrowing(string storedHash)
    {
        Assert.False(PasswordHasher.Verify(storedHash, "senha-do-usuario", out _));
    }
}
