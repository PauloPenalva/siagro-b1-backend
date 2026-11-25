using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Sap;

public class InvOneFiscalDocumentSourceTests
{
    private const string Key = "35261112345678000199550010000012341000012345";

    private static InvOneDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<InvOneDbContext>()
            .UseInMemoryDatabase($"invone-{Guid.NewGuid()}").Options);

    private static SapInvoiceRef[] Refs(IEnumerable<int> docEntries, int? branch = 1) =>
        docEntries.Select(d => new SapInvoiceRef(d, branch)).ToArray();

    private static InvOneProcess P(int docEntry, int status = 4, string? key = Key, int docType = 13,
        bool? canceled = null, string? protocol = "135", int company = 1) =>
        new() { CompanyId = company, DocType = docType, DocEntry = docEntry, KeyNfe = key, StatusId = status,
            IsCanceled = canceled, ProtocolNumber = protocol };

    [Fact]
    public async Task Returns_key_authorized_and_protocol_only_for_ar_invoices()
    {
        using var db = CreateDb();
        db.Processes.AddRange(P(1), P(2, docType: 18), P(3));
        await db.SaveChangesAsync();

        var result = await new InvOneFiscalDocumentSource(db).GetForInvoicesAsync(Refs([1, 2]));

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

        var result = await new InvOneFiscalDocumentSource(db).GetForInvoicesAsync(Refs([1, 2, 3]));

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

        var result = await new InvOneFiscalDocumentSource(db).GetForInvoicesAsync(Refs([1]));

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

        var result = await new InvOneFiscalDocumentSource(db).GetForInvoicesAsync(Refs([1, 2]));

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
            .GetForInvoicesAsync(Refs(Enumerable.Range(1, 2500)));

        Assert.Equal(2500, result.Count);
    }

    [Fact]
    public async Task Null_source_is_unavailable_and_empty()
    {
        var source = new NullSapFiscalDocumentSource();

        Assert.False(source.IsAvailable);
        Assert.Empty(await source.GetForInvoicesAsync(Refs([1, 2])));
    }

    [Fact]
    public async Task Same_doc_entry_under_another_company_is_ignored()
    {
        using var db = CreateDb();
        db.Processes.AddRange(P(1, company: 3, protocol: "outra-base"), P(2, company: 1, protocol: "certa"));
        await db.SaveChangesAsync();

        var result = await new InvOneFiscalDocumentSource(db).GetForInvoicesAsync(Refs([1, 2], branch: 1));

        Assert.False(result.ContainsKey(1));
        Assert.Equal("certa", result[2].Protocol);
    }

    [Fact]
    public async Task Picks_the_row_of_the_requested_branch_when_companies_collide()
    {
        using var db = CreateDb();
        db.Processes.AddRange(P(1, company: 3, protocol: "base3"), P(1, company: 4, protocol: "base4"));
        await db.SaveChangesAsync();

        var result = await new InvOneFiscalDocumentSource(db)
            .GetForInvoicesAsync([new SapInvoiceRef(1, 4)]);

        Assert.Equal("base4", result[1].Protocol);
    }

    [Fact]
    public async Task Null_branch_matches_nothing()
    {
        using var db = CreateDb();
        db.Processes.Add(P(1));
        await db.SaveChangesAsync();

        var result = await new InvOneFiscalDocumentSource(db)
            .GetForInvoicesAsync([new SapInvoiceRef(1, null)]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Duplicate_rows_of_same_status_are_picked_deterministically()
    {
        // A ordem de inserção é invertida entre os dois bancos: o resultado tem de ser o mesmo.
        string?[] Run(bool reversed)
        {
            using var db = CreateDb();
            var rows = new[]
            {
                P(1, status: 4, protocol: "100", key: Key), P(1, status: 4, protocol: "200", key: Key, canceled: false),
                P(2, status: 10, protocol: "300"), P(2, status: 10, protocol: "400", canceled: true),
                P(3, status: 5, protocol: "500"), P(3, status: 6, protocol: "600"),
            };
            if (reversed) Array.Reverse(rows);
            db.Processes.AddRange(rows);
            db.SaveChanges();
            var r = new InvOneFiscalDocumentSource(db).GetForInvoicesAsync(Refs([1, 2, 3])).GetAwaiter().GetResult();
            return [r[1].Protocol, r[2].Protocol, r[3].Protocol];
        }

        var forward = Run(false);
        var backward = Run(true);

        Assert.Equal(forward, backward);
    }

    [Fact]
    public async Task Null_protocol_and_null_key_rows_are_tolerated()
    {
        using var db = CreateDb();
        db.Processes.AddRange(
            P(1, status: 2, key: null, protocol: null),
            P(1, status: 3, key: null, protocol: null),
            P(2, status: 4, key: null, protocol: null));
        await db.SaveChangesAsync();

        var result = await new InvOneFiscalDocumentSource(db).GetForInvoicesAsync(Refs([1, 2]));

        Assert.Null(result[1].AccessKey);
        Assert.Null(result[1].Protocol);
        Assert.True(result[2].Authorized);
        Assert.Null(result[2].AccessKey);
    }

    [Fact]
    public async Task Row_with_null_company_or_doc_entry_is_ignored()
    {
        using var db = CreateDb();
        db.Add(new InvOneProcess { CompanyId = null, DocType = 13, DocEntry = 1, StatusId = 4 });
        db.Add(new InvOneProcess { CompanyId = 1, DocType = null, DocEntry = 1, StatusId = 4 });
        db.Add(new InvOneProcess { CompanyId = 1, DocType = 13, DocEntry = null, StatusId = 4 });
        db.Add(new InvOneProcess { CompanyId = 1, DocType = 13, DocEntry = 1, StatusId = null });
        await db.SaveChangesAsync();

        var result = await new InvOneFiscalDocumentSource(db).GetForInvoicesAsync(Refs([1]));

        // Só a linha com StatusId nulo tem identidade completa; sem status ela não é autorizada.
        Assert.False(result[1].Authorized);
        Assert.Single(result);
    }

    [Fact]
    public async Task Credit_memos_are_read_by_their_own_doc_type()
    {
        // DocEntry 1 existe como invoice (13) e como Credit Memo (14): são documentos diferentes.
        using var db = CreateDb();
        db.Processes.AddRange(P(1, status: 10, key: null), P(1, docType: InvOneFiscalDocumentSource.ArCreditMemoDocType));
        await db.SaveChangesAsync();
        var source = new InvOneFiscalDocumentSource(db);

        var creditMemos = await source.GetForCreditMemosAsync(Refs([1]));
        var invoices = await source.GetForInvoicesAsync(Refs([1]));

        Assert.True(creditMemos[1].Authorized);
        Assert.False(creditMemos[1].Cancelled);
        Assert.Equal(Key, creditMemos[1].AccessKey);
        Assert.True(invoices[1].Cancelled);
        Assert.Null(invoices[1].AccessKey);
    }

    [Fact]
    public async Task Invoice_rows_never_answer_for_a_credit_memo()
    {
        using var db = CreateDb();
        db.Processes.Add(P(1));
        await db.SaveChangesAsync();

        Assert.Empty(await new InvOneFiscalDocumentSource(db).GetForCreditMemosAsync(Refs([1])));
    }

    [Fact]
    public async Task Null_source_knows_no_credit_memo()
    {
        ISapFiscalDocumentSource source = new NullSapFiscalDocumentSource();

        Assert.Empty(await source.GetForCreditMemosAsync(Refs([1])));
    }

    [Fact]
    public void Credit_memo_query_translates_with_its_doc_type()
    {
        using var db = new InvOneDbContext(new DbContextOptionsBuilder<InvOneDbContext>()
            .UseSqlServer("Server=localhost;Database=DBInvOne;User Id=x;Password=x;TrustServerCertificate=True;")
            .Options);

        var sql = InvOneFiscalDocumentSource.BuildCreditMemoQuery(db, [1, 2], [1]).ToQueryString();

        Assert.Contains("[DocType] = 14", sql);
    }

    [Fact]
    public void Real_sqlserver_model_is_keyless_and_query_translates()
    {
        using var db = new InvOneDbContext(new DbContextOptionsBuilder<InvOneDbContext>()
            .UseSqlServer("Server=localhost;Database=DBInvOne;User Id=x;Password=x;TrustServerCertificate=True;")
            .Options);

        var entity = db.Model.FindEntityType(typeof(InvOneProcess))!;
        Assert.Null(entity.FindPrimaryKey());

        var sql = InvOneFiscalDocumentSource.BuildQuery(db, [1, 2], [1]).ToQueryString();
        Assert.Contains("[Process]", sql);
        Assert.Contains("[DocType] = 13", sql);
    }

    // ---- finalidade da NF-e (finNFe) lida do XML assinado ----

    private const string PurposeXml = "<NFe><infNFe><ide><tpNF>1</tpNF><finNFe>1</finNFe></ide></infNFe></NFe>";

    private static byte[] Deflate(string xml, Encoding encoding)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = encoding.GetBytes(xml);
            deflate.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    [Fact]
    public void Reads_the_purpose_from_raw_deflate_utf8() =>
        Assert.Equal("1", InvOneFiscalDocumentSource.ReadPurpose(Deflate(PurposeXml, Encoding.UTF8)));

    [Fact]
    public void Reads_the_purpose_from_utf16() =>
        Assert.Equal("1", InvOneFiscalDocumentSource.ReadPurpose(Deflate(PurposeXml, Encoding.Unicode)));

    [Fact]
    public void Reads_the_purpose_from_utf16_with_bom() =>
        Assert.Equal("1", InvOneFiscalDocumentSource.ReadPurpose(DeflateWithBom(PurposeXml, Encoding.Unicode)));

    [Fact]
    public void Reads_the_purpose_from_utf8_with_bom() =>
        Assert.Equal("1", InvOneFiscalDocumentSource.ReadPurpose(DeflateWithBom(PurposeXml, new UTF8Encoding(true))));

    private static byte[] DeflateWithBom(string xml, Encoding encoding)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(xml)).ToArray();
            deflate.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    [Fact]
    public void Complement_invoice_reads_two() =>
        Assert.Equal("2", InvOneFiscalDocumentSource.ReadPurpose(
            Deflate(PurposeXml.Replace("<finNFe>1", "<finNFe>2"), Encoding.UTF8)));

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    public void Unreadable_xml_has_no_purpose(byte[]? raw) =>
        Assert.Null(InvOneFiscalDocumentSource.ReadPurpose(raw));

    [Fact]
    public void Xml_without_the_tag_has_no_purpose() =>
        Assert.Null(InvOneFiscalDocumentSource.ReadPurpose(Deflate("<NFe/>", Encoding.UTF8)));

    [Fact]
    public async Task Purposes_ignore_credit_memos_and_unauthorized_rows()
    {
        using var db = CreateDb();
        var authorized = P(1);
        authorized.XmlEnvAssinado = Deflate(PurposeXml, Encoding.UTF8);
        var unauthorized = P(2, status: 2);
        unauthorized.XmlEnvAssinado = Deflate(PurposeXml, Encoding.UTF8);
        var creditMemo = P(3, docType: 14);
        creditMemo.XmlEnvAssinado = Deflate(PurposeXml.Replace("<finNFe>1", "<finNFe>4"), Encoding.UTF8);
        db.Processes.AddRange(authorized, unauthorized, creditMemo);
        await db.SaveChangesAsync();

        var result = await new InvOneFiscalDocumentSource(db).GetInvoicePurposesAsync(Refs([1, 2, 3]));

        var only = Assert.Single(result);
        Assert.Equal(1, only.Key);
        Assert.Equal("1", only.Value);
    }
}
