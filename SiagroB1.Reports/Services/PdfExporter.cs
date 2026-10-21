using FastReport;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Ponto único de exportação de relatório preparado para PDF. Usa <see cref="ReusableBitmapPdfExport"/>,
/// que reaproveita o Bitmap da página: o <c>PDFSimpleExport</c> cria um Bitmap de ~35 MB por página e o
/// libgdiplus do Linux nunca devolve essa memória (40 páginas: 1212 MB contra 160 MB, medido na VPS em
/// 21/10/2026). Toda exportação nova para PDF deve passar por aqui.
/// </summary>
public static class PdfExporter
{
    /// <summary>Exporta um relatório JÁ PREPARADO. <paramref name="title"/> nulo deixa o PDF sem título.</summary>
    public static byte[] Export(Report report, string? title)
    {
        using var export = new ReusableBitmapPdfExport { Title = title };
        using var stream = new MemoryStream();
        report.Export(export, stream);
        return stream.ToArray();
    }
}
