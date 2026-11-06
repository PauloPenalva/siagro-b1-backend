using System.Globalization;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// GAC-18: a fatura de armazenagem imprime um resumo com o total de cada serviço,
/// para conferir sem somar as cobranças diárias à mão.
/// </summary>
public class StorageInvoiceServiceSummaryTests
{
    private static StorageInvoicePrintItemDto Item(string description, decimal qty, decimal total, decimal loss = 0) =>
        new() { ReferenceDate = new DateTime(2026, 4, 11), Description = description, Quantity = qty, TotalAmount = total, TotalQuantityLoss = loss };

    private static readonly List<StorageInvoicePrintItemDto> Items =
    [
        Item("Recepção - 000101", 19007m, 95.04m),
        Item("Recepção - 000102", 19185m, 95.93m),
        Item("Armazenagem 11/04/2026 a 11/04/2026", 19007m, 190.07m),
        Item("Armazenagem 12/04/2026 a 12/04/2026", 19007m, 190.07m),
        Item("Quebra técnica 11/04/2026 a 11/04/2026", 19007m, 0m, 2m),
        Item("Frete/Remoção - 000300", 1000m, 50m),
    ];

    [Fact]
    public void GroupsItemsByServiceNameInFirstAppearanceOrder()
    {
        var summary = StorageInvoicePrintDataService.SummarizeByService(Items);

        Assert.Equal(new[] { "Recepção", "Armazenagem", "Quebra técnica", "Frete/Remoção" },
            summary.Select(s => s.Service).ToArray());
        var storage = summary[1];
        Assert.Equal(2, storage.Lines);
        Assert.Equal(38014m, storage.Quantity);
        Assert.Equal(380.14m, storage.TotalAmount);
        Assert.Equal(2m, summary[2].TotalQuantityLoss);
    }

    [Fact]
    public void TemplatePrintsOneSummaryRowPerService()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            var dto = new StorageInvoicePrintDto { InvoiceNumber = "F1", TotalAmount = 621.11m, Items = Items };
            dto.ServiceSummary = StorageInvoicePrintDataService.SummarizeByService(Items);

            var root = Path.Combine(AppContext.BaseDirectory, "ReportTemplates", "StorageInvoice.frx");
            using var report = new global::FastReport.Report();
            report.Load(root);
            report.RegisterData(new List<StorageInvoicePrintDto> { dto }, "Invoice");
            report.RegisterData(dto.Items, "InvoiceItems");
            report.RegisterData(dto.ServiceSummary, "ServiceSummary");
            foreach (var n in new[] { "Invoice", "InvoiceItems", "ServiceSummary" }) report.GetDataSource(n).Enabled = true;
            Assert.True(report.Prepare());

            var texts = Enumerable.Range(0, report.PreparedPages.Count)
                .SelectMany(i => report.PreparedPages.GetPage(i).AllObjects.OfType<global::FastReport.TextObject>())
                .ToList();
            string[] Named(string name) => texts.Where(t => t.Name == name).Select(t => t.Text).ToArray();
            Assert.Equal(new[] { "Recepção", "Armazenagem", "Quebra técnica", "Frete/Remoção" }, Named("SumService"));
            Assert.Equal(new[] { "R$ 190,97", "R$ 380,14", "R$ 0,00", "R$ 50,00" }, Named("SumTotal"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
