using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.SalesInvoices;

public class SalesInvoiceTaxSnapshotTests
{
    private static readonly TaxCalculationResult Result = new(
        "51", 1000m, 18m, 10m, 162m, 100m, 162m, 0m, "SP800001",
        "01", 1000m, 1.65m, 16.5m, "01", 1000m, 7.6m, 76m,
        "000", "000001", 907.5m, 0.9m, 0m, 8.17m, 0.1m, 0m, 0m, 0.91m, 0m);

    private static SalesInvoiceItem Item() => new()
    {
        Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 100m, UnitPrice = 10m,
    };

    [Fact]
    public void Write_copies_every_value()
    {
        var item = Item();
        SalesInvoiceTaxSnapshot.Write(item, Result);

        Assert.Equal("51", item.CstIcms);
        Assert.Equal(1000m, item.IcmsBase);
        Assert.Equal(18m, item.IcmsRate);
        Assert.Equal(10m, item.IcmsBaseReduction);
        Assert.Equal(162m, item.IcmsOperationValue);
        Assert.Equal(100m, item.IcmsDeferral);
        Assert.Equal(162m, item.IcmsDeferredValue);
        Assert.Equal(0m, item.IcmsValue);
        Assert.Equal("SP800001", item.IcmsBenefitCode);
        Assert.Equal("01", item.CstPis);
        Assert.Equal(16.5m, item.PisValue);
        Assert.Equal(76m, item.CofinsValue);
        Assert.Equal("000", item.IbsCbsCst);
        Assert.Equal("000001", item.IbsCbsClassCode);
        Assert.Equal(907.5m, item.IbsCbsBase);
        Assert.Equal(8.17m, item.CbsValue);
        Assert.Equal(0.91m, item.IbsStateValue);
        Assert.Equal(9.08m, item.TotalIbsCbs);
        // IBS/CBS informativo em 2026: fora do total de impostos de hoje.
        Assert.Equal(92.5m, item.TotalTaxes);
    }

    [Fact]
    public async Task Restore_locked_puts_back_the_stored_values()
    {
        var db = TestDb.CreateUnitOfWork();
        var item = Item();
        SalesInvoiceTaxSnapshot.Write(item, Result);
        db.Context.SalesInvoicesItems.Add(item);
        await db.SaveChangesAsync();

        item.IcmsValue = 999m;
        item.Cfop = "9999";
        item.CostCenterCode = "CC01";

        SalesInvoiceTaxSnapshot.RestoreLocked(db.Context.Entry(item));

        Assert.Equal(0m, item.IcmsValue);
        Assert.Null(item.Cfop);
        // Centro de custo não é travado.
        Assert.Equal("CC01", item.CostCenterCode);
    }

    [Fact]
    public void Invoice_total_ibs_cbs_sums_the_lines()
    {
        var a = Item(); SalesInvoiceTaxSnapshot.Write(a, Result);
        var b = Item(); SalesInvoiceTaxSnapshot.Write(b, Result);
        var invoice = new SalesInvoice { CardCode = "C1", Items = [a, b] };

        Assert.Equal(18.16m, invoice.TotalInvoiceIbsCbs);
    }
}
