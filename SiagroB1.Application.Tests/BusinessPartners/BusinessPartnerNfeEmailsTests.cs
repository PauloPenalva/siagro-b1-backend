using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.BusinessPartners;

/// <summary>Lista de e-mails de NF-e do parceiro (substitui o campo único NfeEmail).</summary>
public class BusinessPartnerNfeEmailsTests
{
    private static BusinessPartnerService Partners(UnitOfWork db) =>
        new(db, NullLogger<BusinessPartnerService>.Instance, new FakeStringLocalizer<Resource>(), new FakeDocNumberSequenceService());

    private static BusinessPartnerModel Partner(params string[] emails) => new()
    {
        CardCode = "C001", CardName = "CLIENTE LTDA", CardType = "C", TaxId = "11222333000181",
        NfeEmails = emails.ToList(),
    };

    private static Task<List<string>> Saved(UnitOfWork db) =>
        db.Context.BusinessPartnerNfeEmails.AsNoTracking().Where(e => e.CardCode == "C001")
            .OrderBy(e => e.LineNum).Select(e => e.Email).ToListAsync();

    [Fact]
    public async Task Create_saves_emails_in_order()
    {
        var db = TestDb.CreateUnitOfWork();

        await Partners(db).CreateAsync(Partner(" b@x.com ", "a@x.com"));

        Assert.Equal(["b@x.com", "a@x.com"], await Saved(db));
        var read = await Partners(db).GetByIdAsync("C001");
        Assert.Equal(["b@x.com", "a@x.com"], read!.NfeEmails);
    }

    [Fact]
    public async Task Update_replaces_list()
    {
        var db = TestDb.CreateUnitOfWork();
        await Partners(db).CreateAsync(Partner("a@x.com", "b@x.com"));

        await Partners(db).UpdateAsync("C001", Partner("c@x.com", "a@x.com"));

        Assert.Equal(["c@x.com", "a@x.com"], await Saved(db));
    }

    [Fact]
    public async Task Rejects_invalid_email()
    {
        var db = TestDb.CreateUnitOfWork();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Partners(db).CreateAsync(Partner("sem-arroba")));

        Assert.Equal("E-mail de NF-e inválido: sem-arroba", ex.Message);
    }

    [Fact]
    public async Task Rejects_duplicate_email()
    {
        var db = TestDb.CreateUnitOfWork();

        await Assert.ThrowsAsync<DefaultException>(() => Partners(db).CreateAsync(Partner("a@x.com", "A@X.com")));
    }

    [Fact]
    public async Task Rejects_more_than_10()
    {
        var db = TestDb.CreateUnitOfWork();
        var emails = Enumerable.Range(1, 11).Select(i => $"u{i}@x.com").ToArray();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Partners(db).CreateAsync(Partner(emails)));

        Assert.Equal("Informe no máximo 10 e-mails de NF-e.", ex.Message);
    }

    [Fact]
    public async Task Delete_partner_with_emails_succeeds()
    {
        var db = TestDb.CreateUnitOfWork();
        await Partners(db).CreateAsync(Partner("a@x.com", "b@x.com"));

        Assert.True(await Partners(db).DeleteAsync("C001"));

        Assert.Empty(await db.Context.BusinessPartnerNfeEmails.ToListAsync());
    }

    [Fact]
    public async Task Resolver_returns_emails_by_linenum()
    {
        var db = TestDb.CreateUnitOfWork();
        await Partners(db).CreateAsync(Partner("z@x.com", "m@x.com", "a@x.com"));

        var result = await new NfeEmailRecipientResolver(db).ResolveAsync("C001");

        Assert.Equal(["z@x.com", "m@x.com", "a@x.com"], result);
        Assert.Empty(await new NfeEmailRecipientResolver(db).ResolveAsync("OUTRO"));
    }
}
