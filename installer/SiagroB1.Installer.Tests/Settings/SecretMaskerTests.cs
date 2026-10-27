using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Tests.Settings;

public class SecretMaskerTests
{
    [Fact]
    public void MaskConnectionString_HidesPassword()
    {
        var masked = SecretMasker.MaskConnectionString("Server=x;Database=y;User Id=idx;Password=s3gr3do;TrustServerCertificate=True;");

        Assert.Contains("Password=***", masked);
        Assert.DoesNotContain("s3gr3do", masked);
        Assert.Contains("Database=y", masked);
    }

    [Theory]
    [InlineData("Email:Password")]
    [InlineData("Nfe:CertificateKey")]
    [InlineData("Notifications:WhatsApp:Token")]
    [InlineData("Signature:D4Sign:WebhookSecret")]
    [InlineData("Reports:InternalApiKey")]
    public void MaskValue_ByKeyName(string key)
    {
        Assert.Equal("***", SecretMasker.MaskValue(key, "valor"));
    }

    [Fact]
    public void MaskValue_KeepsNonSecret()
    {
        Assert.Equal("smtp.x", SecretMasker.MaskValue("Email:Host", "smtp.x"));
        Assert.Equal("4", SecretMasker.MaskValue("Security:PasswordPolicy:MinimumLength", "4"));
    }

    [Fact]
    public void MaskValue_ConnectionString()
    {
        Assert.DoesNotContain("abc", SecretMasker.MaskValue("ConnectionStrings:SiagroDB", "Server=x;Password=abc"));
    }

    [Fact]
    public void MaskText_HidesPasswordsInFreeText()
    {
        var masked = SecretMasker.MaskText("efbundle --connection \"Server=x;User Id=sa;Password=abc;\" falhou; pwd=def");

        Assert.DoesNotContain("abc", masked);
        Assert.DoesNotContain("def", masked);
    }
}
