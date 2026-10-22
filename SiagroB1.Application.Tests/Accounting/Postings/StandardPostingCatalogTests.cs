using SiagroB1.Application.Services.Accounting.Postings;

namespace SiagroB1.Application.Tests.Accounting.Postings;

public class StandardPostingCatalogTests
{
    [Fact]
    public void Codes_are_unique()
    {
        var codes = StandardPostingCatalog.All.Select(d => d.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.Contains("650", codes);
        Assert.Contains("670", codes);
    }

    [Fact]
    public void Every_reversal_points_to_existing_code_in_same_context()
    {
        foreach (var d in StandardPostingCatalog.All.Where(d => d.Reverses is not null))
        {
            var original = StandardPostingCatalog.Find(d.Reverses!);
            Assert.NotNull(original);
            Assert.Equal(original!.Context, d.Context);
            Assert.Equal(original.PerLine, d.PerLine);
        }
    }

    [Fact]
    public void Account_sources_end_with_fixed()
    {
        Assert.All(StandardPostingCatalog.All, d => Assert.Equal("Fixed", d.AccountSources[^1]));
        Assert.All(StandardPostingCatalog.All, d => Assert.NotEmpty(d.ValueSources));
    }

    [Fact]
    public void Find_returns_null_for_unknown()
    {
        Assert.Null(StandardPostingCatalog.Find("999"));
        Assert.Equal(PostingContextKind.PurchaseInvoice, StandardPostingCatalog.Find("650")!.Context);
        Assert.True(StandardPostingCatalog.Find("650")!.PerLine);
        Assert.False(StandardPostingCatalog.Find("660")!.PerLine);
        Assert.Equal("008810", StandardPostingCatalog.Find("650")!.Batch);
        Assert.Equal("650", StandardPostingCatalog.Find("655")!.Reverses);
    }

    [Fact]
    public void Purchase_line_exposes_fiscal_stock_cost()
    {
        Assert.Contains("Line.FiscalStockCost", StandardPostingCatalog.Find("650")!.ValueSources);
        Assert.Contains("Item.CostOfSales", StandardPostingCatalog.Find("670")!.ValueSources);
    }
}
