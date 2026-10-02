using System.Security.Cryptography;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;

namespace SiagroB1.Fiscal.Tests.Certificates;

/// <summary>A senha do .pfx vai cifrada para o banco; a chave só existe no appsettings do servidor.</summary>
public class CertificatePasswordCipherTests
{
    private static readonly string KeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Round_trip_returns_the_password()
    {
        var cipher = CertificatePasswordCipher.FromBase64(KeyBase64);

        Assert.Equal("s3nh@ç", cipher.Decrypt(cipher.Encrypt("s3nh@ç")));
    }

    [Fact]
    public void Each_encryption_uses_a_new_nonce()
    {
        var cipher = CertificatePasswordCipher.FromBase64(KeyBase64);

        Assert.NotEqual(cipher.Encrypt("abc"), cipher.Encrypt("abc"));
    }

    [Fact]
    public void Another_key_cannot_decrypt()
    {
        var encrypted = CertificatePasswordCipher.FromBase64(KeyBase64).Encrypt("abc");
        var other = CertificatePasswordCipher.FromBase64(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        var ex = Assert.Throws<DefaultException>(() => other.Decrypt(encrypted));

        Assert.Contains("Envie o certificado de novo", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("não é base64")]
    [InlineData("AAAA")]
    public void Missing_or_invalid_key_names_the_setting(string? key)
    {
        var ex = Assert.Throws<DefaultException>(() => CertificatePasswordCipher.FromBase64(key));

        Assert.Contains("Nfe:CertificateKey", ex.Message);
    }
}
