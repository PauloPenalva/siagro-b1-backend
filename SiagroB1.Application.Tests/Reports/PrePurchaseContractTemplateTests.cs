using System.Globalization;
using FastReport;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// GAC-1200: o pré-contrato imprimia o preço com 2 casas e divergia do valor digitado no
/// contrato. Prepara o template real e lê o texto de cada campo na página gerada — o PDF
/// sai como imagem, então a página preparada é o último ponto onde o texto é legível.
/// </summary>
public class PrePurchaseContractTemplateTests
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    static PrePurchaseContractTemplateTests()
    {
        FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));
        FastReport.Utils.Config.WebMode = true;
    }

    /// <summary>
    /// Cada valor usa todas as casas que a coluna do banco guarda: se o layout arredondar,
    /// o texto impresso não volta ao valor original.
    /// </summary>
    [Theory]
    [InlineData("PrecoValor", "123.456789")]      // StandardPrice DECIMAL(18,6)
    [InlineData("FreteValor", "12.34567891")]     // FreightCostStandard DECIMAL(18,8)
    [InlineData("QtdValor", "1000.125")]          // TotalVolume DECIMAL(18,3)
    public void PrintedValue_KeepsEveryStoredDecimal(string objectName, string storedValue)
    {
        var expected = decimal.Parse(storedValue, CultureInfo.InvariantCulture);

        var printed = PrepareAndRead(objectName, new PrePurchaseContractPrintDto
        {
            StandardPrice = 123.456789m,
            FreightCostStandard = 12.34567891m,
            TotalVolume = 1000.125m,
            DeliveryStartDate = new DateTime(2026, 10, 1),
            DeliveryEndDate = new DateTime(2026, 10, 31),
        });

        Assert.Equal(expected, decimal.Parse(printed, NumberStyles.Number, PtBr));
    }

    private static string PrepareAndRead(string objectName, PrePurchaseContractPrintDto data)
    {
        // UseLocale="true" formata com a cultura corrente; no serviço ela é a pt-BR do
        // RequestLocalization, aqui fixamos para o teste não depender da máquina.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = PtBr;
        try
        {
            using var report = new Report();
            report.Load(Path.Combine(AppContext.BaseDirectory, "ReportTemplates", "PrePurchaseContract.frx"));

            report.RegisterData(new List<PrePurchaseContractPrintDto> { data }, "PreContract");
            report.RegisterData(data.QualityParameters, "QualityParameters");
            report.RegisterData(data.Taxes, "Taxes");
            report.GetDataSource("PreContract").Enabled = true;
            report.GetDataSource("QualityParameters").Enabled = true;
            report.GetDataSource("Taxes").Enabled = true;

            Assert.True(report.Prepare(), "PrePurchaseContract.frx failed to prepare.");

            var text = Enumerable.Range(0, report.PreparedPages.Count)
                .SelectMany(i => report.PreparedPages.GetPage(i).AllObjects.OfType<TextObject>())
                .FirstOrDefault(t => t.Name == objectName);

            Assert.NotNull(text);
            return text.Text;
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
