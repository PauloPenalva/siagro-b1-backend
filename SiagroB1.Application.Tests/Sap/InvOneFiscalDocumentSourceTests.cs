using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Sap;

public class InvOneFiscalDocumentSourceTests
{
    private const string Key = "35261112345678000199550010000012341000012345";

    private static InvOneDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<InvOneDbContext>()
            .UseInMemoryDatabase($"invone-{Guid.NewGuid()}").Options);

    private static InvOneProcess P(int docEntry, int status = 4, string? key = Key, int docType = 13,
        bool? canceled = null, string? protocol = "135") =>
        new() { CompanyId = 1, DocType = docType, DocEntry = docEntry, KeyNfe = key, StatusId = status,
            IsCanceled = canceled, ProtocolNumber = protocol };

    [Fact]
    public async Task Returns_key_authorized_and_protocol_only_for_ar_invoices()
    {
        using var db = CreateDb();
        db.Processes.AddRange(P(1), P(2, docType: 18), P(3));
        await db.SaveChangesAsync();

        var result = await new InvOneFiscalDocumentSource(db).GetForInvoicesAsync([1, 2]);

        Assert.Single(result);
        Assert.Equal(Key, result[1].AccessKey);
        Assert.True(result[1].Authorized);
        Assert.False(result[1].Cancelled);
        Assert.Equal("135", result[1].Protocol);
        Assert.False(result.ContainsKey(3)); // fora da lista
    }

    [Fact]
    public async Task Marks_cancelled_by_status_or_flag()
    {
        using var db = CreateDb();
        db.Processes.AddRange(P(1, status: 10), P(2, status: 5, canceled: true), P(3, status: 5, canceled: false));
        await db.SaveChangesAsync();

        var result = await new InvOneFiscalDocumentSource(db).GetForInvoicesAsync([1, 2, 3]);

        Assert.True(result[1].Cancelled);
        Assert.True(result[2].Cancelled);
        Assert.False(result[3].Cancelled);
        Assert.False(result[3].Authorized);
    }

    [Theory]
    [InlineData("3526 1112-3456.78000199550010000012341000012345", Key)]
    [InlineData("12345", null)]
    [InlineData(null, null)]
    public async Task Normalizes_access_key_to_44_digits_or_null(string? raw, string? expected)
    {
        using var db = CreateDb();
        db.Processes.Add(P(1, key: raw));
        await db.SaveChangesAsync();

        var result = await new InvOneFiscalDocumentSource(db).GetForInvoicesAsync([1]);

        Assert.Equal(expected, result[1].AccessKey);
    }

    [Fact]
    public async Task Resend_rows_prefer_authorized_then_cancelled()
    {
        using var db = CreateDb();
        db.Processes.AddRange(
            P(1, status: 5, protocol: "a"), P(1, status: 4, protocol: "b"),
            P(2, status: 5, protocol: "c"), P(2, status: 10, protocol: "d"));
        await db.SaveChangesAsync();

        var result = await new InvOneFiscalDocumentSource(db).GetForInvoicesAsync([1, 2]);

        Assert.Equal("b", result[1].Protocol);
        Assert.True(result[1].Authorized);
        Assert.Equal("d", result[2].Protocol);
        Assert.True(result[2].Cancelled);
    }

    [Fact]
    public async Task Handles_more_than_one_thousand_doc_entries()
    {
        using var db = CreateDb();
        for (var i = 1; i <= 2500; i++) db.Processes.Add(P(i));
        await db.SaveChangesAsync();

        var result = await new InvOneFiscalDocumentSource(db)
            .GetForInvoicesAsync(Enumerable.Range(1, 2500).ToList());

        Assert.Equal(2500, result.Count);
    }

    [Fact]
    public async Task Null_source_is_unavailable_and_empty()
    {
        var source = new NullSapFiscalDocumentSource();

        Assert.False(source.IsAvailable);
        Assert.Empty(await source.GetForInvoicesAsync([1, 2]));
    }
}
