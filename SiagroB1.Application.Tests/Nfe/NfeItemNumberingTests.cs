using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary><c>det/@nItem</c> fixo (spec §8.2): gravado na emissão, 1..n sem buracos, ordem estável.</summary>
public class NfeItemNumberingTests
{
    private static SalesInvoiceItem Line(string key, int? number = null) => new()
    {
        Key = Guid.Parse(key), ItemCode = "SOJA", UnitOfMeasureCode = "KG", NfeItemNumber = number,
    };

    [Fact]
    public void New_lines_are_numbered_in_key_order()
    {
        var b = Line("00000000-0000-0000-0000-000000000002");
        var a = Line("00000000-0000-0000-0000-000000000001");

        NfeItemNumbering.Renumber([b, a]);

        Assert.Equal(1, a.NfeItemNumber);
        Assert.Equal(2, b.NfeItemNumber);
    }

    [Fact]
    public void Renumbering_keeps_the_previous_order_and_closes_gaps()
    {
        var third = Line("00000000-0000-0000-0000-000000000001", 3);
        var first = Line("00000000-0000-0000-0000-000000000009", 1);
        var added = Line("00000000-0000-0000-0000-000000000005");

        NfeItemNumbering.Renumber([third, first, added]);

        Assert.Equal(1, first.NfeItemNumber);
        Assert.Equal(2, third.NfeItemNumber);
        Assert.Equal(3, added.NfeItemNumber);
    }

    [Fact]
    public void Legacy_sale_with_a_single_line_counts_as_item_1()
    {
        Assert.Equal(1, NfeItemNumbering.OriginNumber(Line("00000000-0000-0000-0000-000000000001"), 1));
    }

    [Fact]
    public void Legacy_sale_with_several_lines_has_no_known_number()
    {
        Assert.Null(NfeItemNumbering.OriginNumber(Line("00000000-0000-0000-0000-000000000001"), 2));
    }

    [Fact]
    public void Numbered_sale_line_returns_its_number()
    {
        Assert.Equal(2, NfeItemNumbering.OriginNumber(Line("00000000-0000-0000-0000-000000000001", 2), 3));
    }
}
