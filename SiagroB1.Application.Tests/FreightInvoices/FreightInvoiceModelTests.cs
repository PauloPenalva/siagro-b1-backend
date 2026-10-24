using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.FreightInvoices;

/// <summary>Modelo EF da fatura de frete e dos itens (CT-e).</summary>
public class FreightInvoiceModelTests
{
    [Fact]
    public void Status_enum_values_are_stable()
        => Assert.Equal(new[] { "Draft", "Issued", "Settled", "Cancelled" }, Enum.GetNames<FreightInvoiceStatus>());

    [Fact]
    public void Origins_have_expected_values()
    {
        Assert.Equal(12, (int)FinancialDocumentOrigin.FreightInvoice);
        Assert.Equal(8, (int)FinancialSettlementOrigin.Aggregation);
    }

    [Fact]
    public void Model_has_unique_indexes_and_restrict_fk()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = db.Context.Model.FindEntityType(typeof(FreightInvoice))!;
        Assert.Contains(invoice.GetIndexes(), i => i.IsUnique
            && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "BranchCode", "Code" }));

        var item = db.Context.Model.FindEntityType(typeof(FreightInvoiceItem))!;
        var idx = item.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "TransportDocumentKey" }));
        Assert.True(idx.IsUnique);
        Assert.Equal("[IsActive] = 1", idx.GetFilter());
        var fk = item.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(TransportDocument));
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        var parent = item.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(FreightInvoice));
        Assert.Equal(DeleteBehavior.Cascade, parent.DeleteBehavior);
    }

    [Fact]
    public async Task FreightInvoice_RoundTrips_WithTwoItems()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var cte1 = new TransportDocument { BranchCode = "12345678000190" };
        var cte2 = new TransportDocument { BranchCode = "12345678000190" };
        db.Context.Set<TransportDocument>().AddRange(cte1, cte2);
        var invoice = new FreightInvoice
        {
            BranchCode = "12345678000190", Code = "FF-1", DocNumberKey = Guid.NewGuid(), TakerCardCode = "C1",
            TakerName = "Tomador", IssueDate = new DateTime(2026, 10, 24), CreatedBy = "tester", TotalAmount = 300m,
            Items =
            {
                new FreightInvoiceItem { TransportDocumentKey = cte1.Key, LineNum = 1, Amount = 100m },
                new FreightInvoiceItem { TransportDocumentKey = cte2.Key, LineNum = 2, Amount = 200m },
            },
        };
        db.Context.Set<FreightInvoice>().Add(invoice);
        await db.Context.SaveChangesAsync();

        var db2 = TestDb.CreateUnitOfWork(name);
        var loaded = await db2.Context.Set<FreightInvoice>().Include(x => x.Items).SingleAsync(x => x.Key == invoice.Key);
        Assert.Equal(FreightInvoiceStatus.Draft, loaded.Status);
        Assert.Equal(2, loaded.Items.Count);
        Assert.All(loaded.Items, i => Assert.True(i.IsActive));
        Assert.Equal(300m, loaded.Items.Sum(i => i.Amount));
        Assert.Equal("Tomador", loaded.TakerName);
    }
}
