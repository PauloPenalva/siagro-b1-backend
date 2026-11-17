namespace SiagroB1.Application.Services.SAP;

/// <summary>
/// Formato em que número e série da NF-e do SAP são gravados em SALES_INVOICES: um só lugar, para a invoice de
/// venda e a NF de devolução saírem iguais.
/// </summary>
public static class SapTaxDocumentFormat
{
    /// <summary>Número da NF-e (OINV/ORIN.Serial) com 9 dígitos.</summary>
    public static string Number(int serial) => serial.ToString("D9");

    /// <summary>Série (SeriesStr) com 3 dígitos.</summary>
    public static string Series(string? seriesStr) => (seriesStr ?? string.Empty).Trim().PadLeft(3, '0');
}
