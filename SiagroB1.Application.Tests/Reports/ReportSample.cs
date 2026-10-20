namespace SiagroB1.Application.Tests.Reports;

/// <summary>Grava o PDF de amostra em %TEMP%/&lt;pasta&gt; apenas se SIAGRO_WRITE_SAMPLE_PDF estiver definida.</summary>
internal static class ReportSample
{
    public static void WriteIfRequested(string folderName, string fileName, byte[] pdf)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SIAGRO_WRITE_SAMPLE_PDF"))) return;
        var folder = Path.Combine(Path.GetTempPath(), folderName);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, fileName), pdf);
    }
}
