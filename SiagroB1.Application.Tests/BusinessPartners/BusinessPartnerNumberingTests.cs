using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.BusinessPartners;

/// <summary>
/// Numeração do cadastro de parceiro em STANDALONE: o usuário escolhe a série (Automático/Manual)
/// do Cliente ou do Fornecedor. CardCode é UMA tabela só — cliente e fornecedor disputam os códigos.
/// </summary>
public class BusinessPartnerNumberingTests
{
    private static readonly Guid SeriesKey = Guid.NewGuid();

    private static BusinessPartnerService Service(UnitOfWork db, FakeDocNumberSequenceService fake) =>
        new(db, NullLogger<BusinessPartnerService>.Instance, new FakeStringLocalizer<Resource>(), fake);

    private static BusinessPartnerModel Partner(string? code, string? type, Guid? key = null) => new()
    {
        CardCode = code!, CardName = "PARCEIRO", CardType = type, DocNumberKey = key,
    };

    private static async Task Seed(UnitOfWork db, string code, string type)
    {
        db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = code, CardName = "EXISTENTE", CardType = type });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Supplier_Automatic_GetsSupplierSeriesCode()
    {
        var db = TestDb.CreateUnitOfWork();
        var fake = new FakeDocNumberSequenceService();
        await Seed(db, "F000045", "S");

        var model = Partner(null, "S", SeriesKey);
        await Service(db, fake).CreateAsync(model);

        await DocNumberRequestAsserts.AssertRequestAsync(fake, TransactionCode.Supplier, SeriesKey, null, 15, "F000045");
        Assert.Equal("ST-0001", model.CardCode);
        Assert.True(await db.Context.BusinessPartners.AnyAsync(b => b.CardCode == "ST-0001"));
    }

    [Fact]
    public async Task Customer_Automatic_UsesCustomerSeries()
    {
        var db = TestDb.CreateUnitOfWork();
        var fake = new FakeDocNumberSequenceService();

        await Service(db, fake).CreateAsync(Partner("", "C", SeriesKey));

        var request = Assert.Single(fake.Requests);
        Assert.Equal(TransactionCode.Customer, request.TransactionCode);
        Assert.Equal("", request.TypedCode);
    }

    [Fact]
    public async Task Supplier_Manual_AcceptsF002448_WhenCustomerC002448Exists()
    {
        var db = TestDb.CreateUnitOfWork();
        var fake = new FakeDocNumberSequenceService();
        await Seed(db, "C002448", "C");

        var model = Partner(" F002448 ", "S", SeriesKey);
        await Service(db, fake).CreateAsync(model);

        var request = Assert.Single(fake.Requests);
        Assert.True(await request.ExistsAsync("C002448"));
        Assert.False(await request.ExistsAsync("F999999"));
        Assert.Equal("F002448", model.CardCode);
        Assert.True(await db.Context.BusinessPartners.AnyAsync(b => b.CardCode == "F002448"));
    }

    [Fact]
    public async Task Supplier_Manual_RejectsCodeOfExistingCustomer()
    {
        var db = TestDb.CreateUnitOfWork();
        var fake = new FakeDocNumberSequenceService();
        await Seed(db, "C002448", "C");

        // O duplo aceita qualquer código; o predicado entregue ao serviço real é que recusa.
        var ex = await Record.ExceptionAsync(() => Service(db, fake).CreateAsync(Partner("C002448", "S", SeriesKey)));

        var request = Assert.Single(fake.Requests);
        Assert.Equal(TransactionCode.Supplier, request.TransactionCode);
        Assert.True(await request.ExistsAsync("C002448"));
        Assert.NotNull(ex); // o duplo não recusou, então o INSERT bateu na chave: nada novo foi gravado
        Assert.Equal(1, await db.Context.BusinessPartners.AsNoTracking().CountAsync(b => b.CardCode == "C002448"));
    }

    [Fact]
    public async Task UnknownCardType_KeepsTypedCodeAndSkipsSeries()
    {
        var db = TestDb.CreateUnitOfWork();
        var fake = new FakeDocNumberSequenceService();

        await Service(db, fake).CreateAsync(Partner("L0001", "L"));

        Assert.Empty(fake.Requests);
        Assert.True(await db.Context.BusinessPartners.AnyAsync(b => b.CardCode == "L0001"));
    }
}
