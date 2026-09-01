using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Reports.PartnerSources;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Leitura das pessoas de contato do SAP (OCPR) usadas pelo pré-contrato de compra:
/// sócios administradores e contato para envio do contrato.
/// </summary>
public class SapPartnerContactMapperTests
{
    private static ContactPerson Contact(
        string? position,
        string? name = null,
        string? notes1 = null,
        string? email = null,
        string? mobile = null,
        string? phone = null) =>
        new()
        {
            CardCode = "F0001",
            Name = name,
            Position = position,
            Notes1 = notes1,
            Email = email,
            MobilePhone = mobile,
            Phone = phone
        };

    [Theory]
    [InlineData("Socio")]
    [InlineData("SOCIO")]
    [InlineData("Sócio")]
    [InlineData("  socio  ")]
    [InlineData("Sócios Administradores")]
    public void PositionMatches_IgnoresCaseAccentsAndSurroundingText(string position)
    {
        Assert.True(SapPartnerMapper.PositionMatches(position, "SOCIO"));
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("")]
    [InlineData(null)]
    public void PositionMatches_RejectsUnrelatedPositions(string? position)
    {
        Assert.False(SapPartnerMapper.PositionMatches(position, "SOCIO"));
    }

    [Fact]
    public void BuildManagingPartners_ReturnsNull_WhenThereIsNoMatchingContact()
    {
        var result = SapPartnerMapper.BuildManagingPartners(
            [Contact("Comprador", name: "CARLOS")]);

        Assert.Null(result);
    }

    [Fact]
    public void BuildManagingPartners_JoinsEveryPartnerWithItsTaxId()
    {
        var result = SapPartnerMapper.BuildManagingPartners([
            Contact("Socio", name: "JOÃO DA SILVA", notes1: "123.456.789-00"),
            Contact("Comprador", name: "CARLOS"),
            Contact("Sócio", name: "MARIA SOUZA", notes1: "987.654.321-00")
        ]);

        Assert.Equal("JOÃO DA SILVA (123.456.789-00); MARIA SOUZA (987.654.321-00)", result);
    }

    [Fact]
    public void BuildManagingPartners_OmitsParenthesis_WhenContactHasNoTaxId()
    {
        var result = SapPartnerMapper.BuildManagingPartners([Contact("Socio", name: "JOÃO")]);

        Assert.Equal("JOÃO", result);
    }

    [Fact]
    public void BuildManagingPartners_SkipsContactsWithoutName()
    {
        var result = SapPartnerMapper.BuildManagingPartners([
            Contact("Socio", name: "  ", notes1: "123"),
            Contact("Socio", name: "MARIA")
        ]);

        Assert.Equal("MARIA", result);
    }

    [Fact]
    public void BuildContractContact_ReturnsNull_WhenThereIsNoMatchingContact()
    {
        Assert.Null(SapPartnerMapper.BuildContractContact([Contact("Socio", name: "JOÃO")]));
    }

    [Fact]
    public void BuildContractContact_JoinsEmailAndMobilePhone()
    {
        var result = SapPartnerMapper.BuildContractContact([
            Contact("Contrato", email: "compras@fazenda.com.br", mobile: "(66) 99999-0000")
        ]);

        Assert.Equal("compras@fazenda.com.br / (66) 99999-0000", result);
    }

    [Fact]
    public void BuildContractContact_FallsBackToLandlinePhone_WhenThereIsNoMobile()
    {
        var result = SapPartnerMapper.BuildContractContact([
            Contact("Contrato", email: "compras@fazenda.com.br", phone: "(66) 3333-0000")
        ]);

        Assert.Equal("compras@fazenda.com.br / (66) 3333-0000", result);
    }

    [Fact]
    public void BuildContractContact_ReturnsOnlyWhatExists()
    {
        var result = SapPartnerMapper.BuildContractContact([Contact("Contrato", mobile: "66999990000")]);

        Assert.Equal("66999990000", result);
    }

    [Fact]
    public void BuildContractContact_ReturnsNull_WhenMatchingContactHasNoChannel()
    {
        Assert.Null(SapPartnerMapper.BuildContractContact([Contact("Contrato", name: "JOÃO")]));
    }
}
