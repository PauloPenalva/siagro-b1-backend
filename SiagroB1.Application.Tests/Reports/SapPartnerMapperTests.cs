using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Reports.PartnerSources;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Regras de leitura fiscal do parceiro no SAP (CRD7/CRD1/OCNT) usadas pelo
/// pré-contrato de compra.
/// </summary>
public class SapPartnerMapperTests
{
    private static AddressTaxExtension Tax(
        string name, string? cnpj = null, string? cpf = null, string? ie = null) =>
        new()
        {
            CardCode = "F0001",
            AddressName = name,
            AddressType = "S",
            Cnpj = cnpj,
            StateRegistration = ie,
            Cpf = cpf
        };

    [Fact]
    public void SelectFiscalAddress_ReturnsNull_WhenPartnerHasNoAddress()
    {
        var result = SapPartnerMapper.SelectFiscalAddress([], "FATURAMENTO");

        Assert.Null(result);
    }

    [Fact]
    public void SelectFiscalAddress_PrefersDefaultAddressWithDocument()
    {
        var result = SapPartnerMapper.SelectFiscalAddress(
            [Tax("OUTRO", cnpj: "11111111111111"), Tax("PADRAO", cnpj: "22222222222222")],
            "PADRAO");

        Assert.Equal("PADRAO", result?.AddressName);
    }

    [Fact]
    public void SelectFiscalAddress_FallsBackToAnyAddressWithDocument_WhenDefaultHasNone()
    {
        var result = SapPartnerMapper.SelectFiscalAddress(
            [Tax("PADRAO"), Tax("OUTRO", cpf: "12345678901")],
            "PADRAO");

        Assert.Equal("OUTRO", result?.AddressName);
    }

    [Fact]
    public void SelectFiscalAddress_FallsBackToDefaultAddress_WhenNoneHasDocument()
    {
        var result = SapPartnerMapper.SelectFiscalAddress(
            [Tax("OUTRO"), Tax("PADRAO")],
            "PADRAO");

        Assert.Equal("PADRAO", result?.AddressName);
    }

    [Fact]
    public void SelectFiscalAddress_FallsBackToFirstByName_WhenThereIsNoDefaultAddress()
    {
        var result = SapPartnerMapper.SelectFiscalAddress(
            [Tax("ZULU"), Tax("ALFA")],
            shipToDef: null);

        Assert.Equal("ALFA", result?.AddressName);
    }

    [Fact]
    public void SelectFiscalAddress_IgnoresBlankDocuments()
    {
        var result = SapPartnerMapper.SelectFiscalAddress(
            [Tax("PADRAO", cnpj: "   "), Tax("OUTRO", cnpj: "22222222222222")],
            "PADRAO");

        Assert.Equal("OUTRO", result?.AddressName);
    }

    [Fact]
    public void BuildFullAddress_ReturnsNull_WhenThereIsNoAddress()
    {
        Assert.Null(SapPartnerMapper.BuildFullAddress(null, null));
    }

    [Fact]
    public void BuildFullAddress_JoinsEveryPartInUpperCase()
    {
        var address = new Address
        {
            CardCode = "F0001",
            AddressName = "PADRAO",
            AdresType = "S",
            StreetType = "Rua",
            Street = "das Flores",
            StreetNo = "123",
            Block = "Centro",
            City = "Cidade Livre",
            State = "MT",
            ZipCode = "78000-000",
            County = "7"
        };
        var county = new County { AbsId = 7, Name = "Sorriso", State = "MT" };

        var result = SapPartnerMapper.BuildFullAddress(address, county);

        Assert.Equal(
            "RUA DAS FLORES, 123 - BAIRRO: CENTRO - MUNICÍPIO: SORRISO - UF: MT - CEP: 78000-000",
            result);
    }

    [Fact]
    public void BuildFullAddress_OmitsEmptyParts()
    {
        var address = new Address
        {
            CardCode = "F0001",
            AddressName = "PADRAO",
            AdresType = "S",
            Street = "AV BRASIL",
            City = "SORRISO"
        };

        var result = SapPartnerMapper.BuildFullAddress(address, null);

        Assert.Equal("AV BRASIL - MUNICÍPIO: SORRISO", result);
    }

    [Fact]
    public void BuildFullAddress_FallsBackToAddressCity_WhenCountyIsMissing()
    {
        var address = new Address
        {
            CardCode = "F0001",
            AddressName = "PADRAO",
            AdresType = "S",
            Street = "AV BRASIL",
            City = "NOVA MUTUM",
            State = "MT"
        };

        var result = SapPartnerMapper.BuildFullAddress(address, null);

        Assert.Equal("AV BRASIL - MUNICÍPIO: NOVA MUTUM - UF: MT", result);
    }

    [Fact]
    public void BuildFullAddress_FallsBackToCountyState_WhenAddressHasNoState()
    {
        var address = new Address
        {
            CardCode = "F0001",
            AddressName = "PADRAO",
            AdresType = "S",
            Street = "AV BRASIL",
            County = "7"
        };
        var county = new County { AbsId = 7, Name = "SORRISO", State = "MT" };

        var result = SapPartnerMapper.BuildFullAddress(address, county);

        Assert.Equal("AV BRASIL - MUNICÍPIO: SORRISO - UF: MT", result);
    }
}
