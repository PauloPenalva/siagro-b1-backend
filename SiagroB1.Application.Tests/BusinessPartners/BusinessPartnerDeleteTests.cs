using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.BusinessPartners;

/// <summary>
/// Excluir PN STANDALONE que tem endereço dava 500: BUSINESS_PARTNERS_ADDRESSES tem FK para o parceiro
/// com NoAction (o AppDbContext força NoAction em toda relação), então o DELETE do pai batia no erro 547.
/// O endereço e os signatários são parte do cadastro do parceiro e saem junto com ele.
/// Limitação: o InMemory NÃO impõe FK, então este teste não reproduz o 547 — ele garante que os
/// endereços são removidos junto e impede que alguém tire essa remoção depois.
/// </summary>
public class BusinessPartnerDeleteTests
{
    private static BusinessPartnerService Service(UnitOfWork db) =>
        new(db, NullLogger<BusinessPartnerService>.Instance, new FakeStringLocalizer<Resource>());

    [Fact]
    public async Task Delete_removes_the_partner_addresses_with_it()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.BusinessPartners.AddRange(
            new BusinessPartner { CardCode = "ARM900", CardName = "ARMAZEM TESTE" },
            new BusinessPartner { CardCode = "C900", CardName = "OUTRO" });
        db.Context.Addresses.AddRange(
            new Address { CardCode = "ARM900", AddressName = "ENTREGA", AdresType = "S" },
            new Address { CardCode = "ARM900", AddressName = "FATURAMENTO", AdresType = "B" },
            new Address { CardCode = "C900", AddressName = "ENTREGA", AdresType = "S" });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var deleted = await Service(db).DeleteAsync("ARM900");

        Assert.True(deleted);
        Assert.False(await db.Context.BusinessPartners.AnyAsync(b => b.CardCode == "ARM900"));
        Assert.False(await db.Context.Addresses.AnyAsync(a => a.CardCode == "ARM900"));
        Assert.Equal(1, await db.Context.Addresses.CountAsync(a => a.CardCode == "C900"));
    }

    /// <summary>
    /// Signatários não têm FK para o parceiro (por desenho: em SAPB1 o parceiro vive no SAP), então não
    /// bloqueavam a exclusão — ficavam órfãos e reapareciam se o código fosse recriado.
    /// </summary>
    [Fact]
    public async Task Delete_removes_the_partner_signatories_with_it()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.BusinessPartners.AddRange(
            new BusinessPartner { CardCode = "ARM900", CardName = "ARMAZEM TESTE" },
            new BusinessPartner { CardCode = "C900", CardName = "OUTRO" });
        db.Context.BusinessPartnerSignatories.AddRange(
            new BusinessPartnerSignatory { Key = Guid.NewGuid(), CardCode = "ARM900", Name = "A", TaxId = "11122233344", Email = "a@x.com" },
            new BusinessPartnerSignatory { Key = Guid.NewGuid(), CardCode = "ARM900", Name = "B", TaxId = "22233344455", Email = "b@x.com" },
            new BusinessPartnerSignatory { Key = Guid.NewGuid(), CardCode = "C900", Name = "C", TaxId = "33344455566", Email = "c@x.com" });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        await Service(db).DeleteAsync("ARM900");

        Assert.False(await db.Context.BusinessPartnerSignatories.AnyAsync(s => s.CardCode == "ARM900"));
        Assert.Equal(1, await db.Context.BusinessPartnerSignatories.CountAsync(s => s.CardCode == "C900"));
    }

    [Fact]
    public async Task Delete_of_unknown_partner_returns_false()
    {
        var db = TestDb.CreateUnitOfWork();

        Assert.False(await Service(db).DeleteAsync("NAOEXISTE"));
    }
}
