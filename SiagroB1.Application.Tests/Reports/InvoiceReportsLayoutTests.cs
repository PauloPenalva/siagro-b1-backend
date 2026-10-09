using FastReport;
using FastReport.Utils;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Confere, sem renderizar, que cada campo numérico dos relatórios fiscais cabe no maior valor
/// previsto — mesma regra dos relatórios de logística: (5,2 px/char + 4 px de margem de reticência) a 7pt,
/// proporcional ao corpo, + 4 px de padding (medido no PDF: 14 caracteres cortam em 80 px) — que os
/// cabeçalhos não cortam e que nada passa da banda (1084 px) nem se sobrepõe a outro objeto da mesma banda.
/// </summary>
public class InvoiceReportsLayoutTests
{
    private const float CharWidth = 5.2f; // Consolas 7pt
    private const float TrimMargin = 4f;  // GDI+ ao aparar com reticências, a 7pt (medido)
    private const float Padding = 4f;
    private const float PageWidth = 1084f;

    public static TheoryData<string> Templates => new()
    {
        "SalesInvoicesByPeriod", "PurchaseInvoicesByPeriod", "SalesInvoiceItems",
        "PurchaseInvoiceItems", "SalesReturns",
    };

    /// <summary>Quantidade de caracteres do maior valor previsto, ou null se o objeto não é numérico.</summary>
    private static int? Budget(string name)
    {
        var isTotal = name.StartsWith("txtSum") || name.StartsWith("fiscalSum")
            || name.StartsWith("txtGrp") || name.StartsWith("fiscalGrp");
        if (isTotal)
        {
            if (name is "txtSumLabel" or "txtSumCount" or "txtGrpLabel" or "txtGrpCount") return null;
            return name.EndsWith("Quantity") || name.EndsWith("NetWeight") ? 15 : 14; // 999.999.999,999 / 999.999.999,99
        }

        return name switch
        {
            "txtIssueDate" or "txtPostingDate" => 10,                    // 31/12/2026
            "txtQuantity" or "txtNetWeight" => 14,                       // 99.999.999,999
            "txtProducts" or "txtDeclared" or "txtTotal" or "txtGrandTotal" or "txtValue" => 12, // 9.999.999,99
            "txtFreight" or "txtDiscount" or "fiscalTaxes" or "fiscalIcms" or "fiscalPis"
                or "fiscalCofins" or "fiscalIbsCbs" => 10,               // 999.999,99
            _ => null,
        };
    }

    private static float Required(int chars, float fontSize, float padding = Padding) =>
        (chars * CharWidth + TrimMargin) * fontSize / 7f + padding;

    private static Report Load(string template)
    {
        Config.WebMode = true;
        var report = new Report();
        report.Load(Path.Combine(AppContext.BaseDirectory, "ReportTemplates", template + ".frx"));
        return report;
    }

    private static List<TextObject> TextObjects(Report report) =>
        report.AllObjects.OfType<TextObject>().ToList();

    [Theory]
    [MemberData(nameof(Templates))]
    public void NumericObjects_FitTheirLargestValue(string template)
    {
        using var report = Load(template);
        var tooNarrow = new List<string>();
        var checkedCount = 0;

        foreach (var obj in TextObjects(report))
        {
            if (Budget(obj.Name) is not { } chars) continue;
            checkedCount++;
            var required = Required(chars, obj.Font.Size);
            if (required > obj.Width)
                tooNarrow.Add($"{obj.Name}: precisa {required:0.#}px, tem {obj.Width:0.#}px");
        }

        Assert.True(checkedCount >= 3, $"{template}: nenhum campo numérico encontrado.");
        Assert.True(tooNarrow.Count == 0, $"{template}: " + string.Join("; ", tooNarrow));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Headers_FitTheirText(string template)
    {
        using var report = Load(template);
        var tooNarrow = TextObjects(report)
            .Where(o => o.Name.StartsWith("hdr") || o.Name.StartsWith("fiscalHdr"))
            .Select(o => (Obj: o, Required: Required(o.Text.Length, o.Font.Size, o.Padding.Left + o.Padding.Right)))
            .Where(x => x.Required > x.Obj.Width)
            .Select(x => $"{x.Obj.Name}: precisa {x.Required:0.#}px, tem {x.Obj.Width:0.#}px");

        Assert.Empty(tooNarrow);
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Objects_StayInsideThePageAndDoNotOverlapInTheSameBand(string template)
    {
        using var report = Load(template);
        var problems = new List<string>();
        var objects = TextObjects(report);

        foreach (var obj in objects)
        {
            if (obj.Left + obj.Width > PageWidth + 0.01f)
                problems.Add($"{obj.Name} passa de {PageWidth}px ({obj.Left + obj.Width:0.#})");
        }

        foreach (var numeric in objects.Where(o => Budget(o.Name) is not null))
        {
            foreach (var other in objects)
            {
                if (ReferenceEquals(numeric, other) || other.Name == "txtEmpty") continue;
                if (!ReferenceEquals(numeric.Parent, other.Parent)) continue;
                var sameRow = numeric.Top < other.Top + other.Height && other.Top < numeric.Top + numeric.Height;
                var sameColumns = numeric.Left < other.Left + other.Width - 0.01f
                    && other.Left < numeric.Left + numeric.Width - 0.01f;
                if (sameRow && sameColumns && string.CompareOrdinal(numeric.Name, other.Name) < 0)
                    problems.Add($"{numeric.Name} sobrepõe {other.Name}");
            }
        }

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void HeaderAndDataCells_ShareLeftAndWidth(string template)
    {
        using var report = Load(template);
        var objects = TextObjects(report);
        var mismatches = new List<string>();

        foreach (var header in objects.Where(o => o.Name.StartsWith("hdr") || o.Name.StartsWith("fiscalHdr")))
        {
            var dataName = header.Name.StartsWith("fiscalHdr")
                ? "fiscal" + header.Name["fiscalHdr".Length..]
                : "txt" + header.Name["hdr".Length..];
            var data = objects.FirstOrDefault(o => o.Name == dataName);
            if (data is null) continue;
            if (Math.Abs(data.Left - header.Left) > 0.01f || Math.Abs(data.Width - header.Width) > 0.01f)
                mismatches.Add($"{header.Name} x {data.Name}");
        }

        Assert.True(mismatches.Count == 0, $"{template}: " + string.Join("; ", mismatches));
    }

    [Fact]
    public void PurchaseInvoicesByPeriod_DeclaredHeaderAndNfeStatusFitTheirText()
    {
        using var report = Load("PurchaseInvoicesByPeriod");
        var objects = TextObjects(report);
        // Cabeçalho "Vl. declarado" (13 caracteres) e situação da NF-e ("Autorizada"/"Inutilizada", 11 caracteres + padding 4/2)
        var checks = new (string Name, int Chars, float Pad)[]
        {
            ("hdrDeclared", 13, Padding), ("fiscalHdrNfeStatus", 9, 6f), ("fiscalNfeStatus", 11, 6f),
        };
        var tooNarrow = checks
            .Select(c => (c.Name, Required: Required(c.Chars, 7f, c.Pad), obj: objects.Single(o => o.Name == c.Name)))
            .Where(c => c.Required > c.obj.Width)
            .Select(c => $"{c.Name}: precisa {c.Required:0.#}px, tem {c.obj.Width:0.#}px");

        Assert.Empty(tooNarrow);
    }
}
