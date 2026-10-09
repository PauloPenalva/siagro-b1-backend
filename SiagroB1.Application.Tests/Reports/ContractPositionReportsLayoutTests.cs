using FastReport;
using FastReport.Format;
using FastReport.Utils;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Geometria dos relatórios de posição de contratos, sem renderizar — mesmas regras de
/// <see cref="LogisticsReportsLayoutTests"/> (medidas no PDF em 08/10): (5,2 px/char + 4 px de
/// margem de reticência) a 7pt, proporcional ao corpo, + 4 px de padding; cabeçalho não corta;
/// campos curtos cabem no maior valor real; totais em 6pt numa linha só; nada passa de 1084 px nem
/// se sobrepõe; grupos não são reordenados pelo FastReport. Aqui os saldos podem ser negativos.
/// </summary>
public class ContractPositionReportsLayoutTests
{
    private const float CharWidth = 5.2f;  // Consolas 7pt
    private const float TrimMargin = 4f;   // GDI+ ao aparar com reticências, a 7pt (medido)
    private const float Padding = 4f;
    private const float PageWidth = 1084f;

    public static TheoryData<string> Templates => new()
    {
        "ContractPosition",
    };

    /// <summary>Maior valor realista de cada campo de texto curto, em caracteres.</summary>
    private static readonly Dictionary<string, Dictionary<string, int>> LongestText = new()
    {
        ["ContractPosition"] = new()
        {
            ["txtCode"] = 12,            // PC2026000123
            ["txtCreationDate"] = 10,    // 31/12/2026
            ["txtHarvestSeason"] = 10,   // VARCHAR(10): 2025/2026
            ["txtType"] = 3,             // FIX / PAF
            ["txtCashFlowDate"] = 10,    // 31/12/2026
            ["txtDeliveryEndDate"] = 10, // 31/12/2026
            ["txtStatus"] = 12,          // Em Aprovação
        },
    };

    private static Report Load(string template)
    {
        Config.WebMode = true;
        var report = new Report();
        report.Load(Path.Combine(AppContext.BaseDirectory, "ReportTemplates", template + ".frx"));
        return report;
    }

    private static List<TextObject> TextObjects(Report report) =>
        report.AllObjects.OfType<TextObject>().ToList();

    private static float Required(int chars, float fontSize) =>
        (chars * CharWidth + TrimMargin) * fontSize / 7f + Padding;

    private static bool IsTotal(TextObject obj) => obj.Parent is GroupFooterBand or ReportSummaryBand;

    [Theory]
    [MemberData(nameof(Templates))]
    public void NumericObjects_FitTheLargestRealisticValue(string template)
    {
        using var report = Load(template);
        var problems = new List<string>();
        var checkedCount = 0;

        foreach (var obj in TextObjects(report))
        {
            if (obj.Format is not NumberFormat number) continue;
            checkedCount++;
            // dado: 99.999.999,999 (14) / 9.999.999,99 (12); total: 999.999.999,999 (15) / 999.999.999,99 (14)
            var chars = IsTotal(obj)
                ? (number.DecimalDigits == 3 ? 15 : 14)
                : (number.DecimalDigits == 3 ? 14 : 12);
            var required = Required(chars, obj.Font.Size);
            if (required > obj.Width)
                problems.Add($"{obj.Name}: precisa {required:0.#}px, tem {obj.Width:0.#}px");
        }

        Assert.True(checkedCount >= 3, $"{template}: nenhum campo numérico encontrado.");
        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    /// <summary>
    /// Medido no PDF em 08/10: "-10.000.000,000" (15 car.) a 7pt saiu "-10.000.000,0…" com 86 px
    /// (a conta dá 86,0 — a regra fica no limite) e coube com 88.
    /// </summary>
    private const float NegativeBalanceAt7pt = 88f;

    /// <summary>
    /// Saldo pode ser negativo (sem clamp): "-10.000.000,000" tem 15 caracteres e precisa caber no
    /// dado, no subtotal da seção, no saldo geral do bloco e no total.
    /// </summary>
    [Fact]
    public void ContractPosition_BalanceObjectsFitANegative15CharValue()
    {
        using var report = Load("ContractPosition");
        var balances = TextObjects(report)
            .Where(o => o.Name.EndsWith("BalanceQuantity", StringComparison.Ordinal))
            .ToList();
        float Need(TextObject o) => Math.Abs(o.Font.Size - 7f) < 0.01f
            ? Math.Max(Required(15, 7f), NegativeBalanceAt7pt)
            : Required(15, o.Font.Size);
        var problems = balances
            .Where(o => Need(o) > o.Width)
            .Select(o => $"{o.Name}: precisa {Need(o):0.#}px, tem {o.Width:0.#}px")
            .ToList();

        Assert.True(balances.Count >= 5, "ContractPosition: faltam objetos de Saldo (cabeçalho, dado, seção, bloco e total).");
        Assert.True(problems.Count == 0, "ContractPosition: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Headers_FitTheirText(string template)
    {
        using var report = Load(template);
        var problems = TextObjects(report)
            .Where(o => o.Parent is ColumnHeaderBand)
            .Where(o => Required(o.Text.Length, o.Font.Size) > o.Width)
            .Select(o => $"{o.Name} (\"{o.Text}\"): precisa {Required(o.Text.Length, o.Font.Size):0.#}px, tem {o.Width:0.#}px")
            .ToList();

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void ShortTextCells_FitTheLongestRealisticValue(string template)
    {
        using var report = Load(template);
        var objects = TextObjects(report);
        var problems = new List<string>();

        foreach (var (name, chars) in LongestText[template])
        {
            var obj = objects.SingleOrDefault(o => o.Name == name);
            if (obj is null)
            {
                problems.Add($"{name} não existe");
                continue;
            }

            if (Required(chars, obj.Font.Size) > obj.Width)
                problems.Add($"{name}: precisa {Required(chars, obj.Font.Size):0.#}px, tem {obj.Width:0.#}px");
        }

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Objects_StayInsideThePage(string template)
    {
        using var report = Load(template);
        var problems = TextObjects(report)
            .Where(o => o.Left + o.Width > PageWidth + 0.01f)
            .Select(o => $"{o.Name} passa de {PageWidth}px ({o.Left + o.Width:0.#})")
            .ToList();

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void HeaderAndDataCells_ShareLeftAndWidth(string template)
    {
        using var report = Load(template);
        var objects = TextObjects(report);
        var problems = new List<string>();

        foreach (var header in objects.Where(o => o.Parent is ColumnHeaderBand))
        {
            var dataName = "txt" + header.Name["hdr".Length..];
            var data = objects.SingleOrDefault(o => o.Name == dataName && o.Parent is DataBand);
            if (data is null)
                problems.Add($"{header.Name} sem {dataName}");
            else if (Math.Abs(data.Left - header.Left) > 0.01f || Math.Abs(data.Width - header.Width) > 0.01f)
                problems.Add($"{header.Name} x {data.Name}");
        }

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void DataCells_DoNotOverlap(string template)
    {
        using var report = Load(template);
        var cells = TextObjects(report).Where(o => o.Parent is DataBand).OrderBy(o => o.Left).ToList();
        var problems = new List<string>();

        for (var i = 0; i + 1 < cells.Count; i++)
        {
            if (cells[i].Left + cells[i].Width > cells[i + 1].Left + 0.01f)
                problems.Add($"{cells[i].Name} sobrepõe {cells[i + 1].Name}");
        }

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Totals_UseSixPointInASingleRow(string template)
    {
        using var report = Load(template);
        var problems = new List<string>();

        foreach (var band in report.AllObjects.OfType<BandBase>().Where(b => b is GroupFooterBand or ReportSummaryBand))
        {
            var numbers = band.Objects.OfType<TextObject>().Where(o => o.Format is NumberFormat).ToList();
            problems.AddRange(numbers.Where(o => Math.Abs(o.Font.Size - 6f) > 0.01f).Select(o => $"{o.Name} não está em 6pt"));
            if (numbers.Select(o => o.Top).Distinct().Count() > 1)
                problems.Add($"{band.Name}: totais em mais de uma linha");
        }

        Assert.True(problems.Count == 0, $"{template}: " + string.Join("; ", problems));
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void GroupHeaders_KeepTheServiceOrder(string template)
    {
        using var report = Load(template);
        var groups = report.AllObjects.OfType<GroupHeaderBand>().ToList();

        Assert.NotEmpty(groups);
        Assert.All(groups, g => Assert.Equal(SortOrder.None, g.SortOrder));
    }

    /// <summary>Washout é só da compra: zero (venda, ou compra sem washout) sai em branco, não "0,000".</summary>
    [Fact]
    public void ContractPosition_WashoutHidesZeros()
    {
        using var report = Load("ContractPosition");
        var washouts = TextObjects(report)
            .Where(o => o.Name.EndsWith("WashedOutQuantity", StringComparison.Ordinal) && o.Format is NumberFormat)
            .ToList();

        Assert.Equal(2, washouts.Count);
        Assert.All(washouts, o => Assert.True(o.HideZeros, $"{o.Name} sem HideZeros"));
    }
}
