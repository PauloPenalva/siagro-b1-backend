using SiagroB1.Application.Services.Companies;

namespace SiagroB1.Application.Tests.Companies;

/// <summary>A string de conexão sai do servidor com a senha mascarada e volta assim na edição; a senha real só é trocada quando o admin digita outra.</summary>
public class CompanyConnectionStringsTests
{
    private const string Real = "Server=db;Database=LM;User Id=app;Password=s3cr3t;TrustServerCertificate=True";

    [Fact]
    public void MaskPassword_replaces_the_password_and_keeps_the_rest()
    {
        var masked = CompanyConnectionStrings.MaskPassword(Real);

        Assert.DoesNotContain("s3cr3t", masked);
        Assert.Contains("*****", masked);
        Assert.Contains("Initial Catalog=LM", masked);
        Assert.True(CompanyConnectionStrings.IsMasked(masked));
        Assert.False(CompanyConnectionStrings.IsMasked(Real));
    }

    [Fact]
    public void MaskPassword_without_password_or_unparseable_returns_input()
    {
        const string integrated = "Server=db;Database=LM;Integrated Security=True";
        Assert.Equal(integrated, CompanyConnectionStrings.MaskPassword(integrated));
        Assert.Equal("isso não é uma string", CompanyConnectionStrings.MaskPassword("isso não é uma string"));
        Assert.Equal("", CompanyConnectionStrings.MaskPassword(null));
    }

    [Fact]
    public void MergeMaskedPassword_restores_the_stored_password_only_when_masked()
    {
        var masked = CompanyConnectionStrings.MaskPassword(Real).Replace("Initial Catalog=LM", "Initial Catalog=LM2");

        var merged = CompanyConnectionStrings.MergeMaskedPassword(masked, Real);

        Assert.Contains("Password=s3cr3t", merged);
        Assert.Contains("Initial Catalog=LM2", merged);
        Assert.Equal("Server=x;Password=nova", CompanyConnectionStrings.MergeMaskedPassword("Server=x;Password=nova", Real));
    }
}
