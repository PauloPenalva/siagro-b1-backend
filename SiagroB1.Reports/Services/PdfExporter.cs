using System.Diagnostics;
using FastReport;
using FastReport.Export.PdfSimple;

namespace SiagroB1.Reports.Services;

/// <summary>
/// Ponto único de exportação de relatório preparado para PDF.
///
/// Por que existe: o <see cref="PDFSimpleExport"/> do FastReport OpenSource rasteriza cada página num
/// Bitmap de 300 dpi (A4 ≈ 35 MB) e o libgdiplus do Linux não devolve esse buffer no Dispose — o processo
/// cresce ~35 MB por página para sempre (provado em 14/10/2026 com System.Drawing puro). No Linux a
/// rasterização roda num processo filho de vida curta (o próprio executável do Reports com
/// <see cref="ChildFlag"/>): o relatório já preparado vai para um .fpx temporário, o filho gera o PDF e
/// termina, e o sistema operacional recupera toda a memória. O script do .frx não é recompilado no filho
/// (páginas preparadas não têm script).
///
/// No Windows o GDI+ libera a memória, então o padrão é exportar no próprio processo.
/// Configuração: <c>Reports:IsolatePdfExport</c> (true/false) sobrepõe o padrão por sistema operacional.
/// </summary>
public static class PdfExporter
{
    public const string ChildFlag = "--render-pdf";
    private static readonly TimeSpan ChildTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Padrão do processo; definido no startup a partir da configuração.</summary>
    public static bool IsolateByDefault { get; set; } = OperatingSystem.IsLinux();

    /// <summary>Exporta um relatório JÁ PREPARADO. <paramref name="title"/> nulo mantém o título padrão.</summary>
    public static byte[] Export(Report report, string? title, bool? isolate = null, string? hostDirectory = null)
    {
        if (!(isolate ?? IsolateByDefault))
            return ExportInProcess(report, title);

        var basePath = Path.Combine(Path.GetTempPath(), $"siagro-pdf-{Guid.NewGuid():N}");
        var preparedPath = basePath + ".fpx";
        var pdfPath = basePath + ".pdf";
        try
        {
            report.SavePrepared(preparedPath);
            RunChild(preparedPath, pdfPath, title, hostDirectory ?? AppContext.BaseDirectory);
            return File.ReadAllBytes(pdfPath);
        }
        finally
        {
            TryDelete(preparedPath);
            TryDelete(pdfPath);
        }
    }

    private static byte[] ExportInProcess(Report report, string? title)
    {
        using var export = new PDFSimpleExport { ShowProgress = false };
        if (title != null) export.Title = title;
        using var stream = new MemoryStream();
        report.Export(export, stream);
        return stream.ToArray();
    }

    private static void RunChild(string preparedPath, string pdfPath, string? title, string hostDirectory)
    {
        var startInfo = ChildStartInfo(hostDirectory);
        startInfo.ArgumentList.Add(ChildFlag);
        startInfo.ArgumentList.Add(preparedPath);
        startInfo.ArgumentList.Add(pdfPath);
        if (title != null) startInfo.ArgumentList.Add(title);
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o processo de geração do PDF.");
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();

        if (!process.WaitForExit(ChildTimeout))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* já terminou */ }
            throw new TimeoutException("A geração do PDF excedeu o tempo limite.");
        }

        if (process.ExitCode != 0 || !File.Exists(pdfPath))
            throw new InvalidOperationException(
                $"Falha na geração do PDF (código {process.ExitCode}): {stderr.Result}{stdout.Result}".Trim());
    }

    /// <summary>
    /// Executável do Reports na pasta indicada: o apphost (publicação normal) ou <c>dotnet SiagroB1.Reports.dll</c>.
    /// </summary>
    private static ProcessStartInfo ChildStartInfo(string hostDirectory)
    {
        const string name = "SiagroB1.Reports";
        var appHost = Path.Combine(hostDirectory, OperatingSystem.IsWindows() ? name + ".exe" : name);
        if (File.Exists(appHost))
            return new ProcessStartInfo(appHost) { WorkingDirectory = hostDirectory };

        var dll = Path.Combine(hostDirectory, name + ".dll");
        if (!File.Exists(dll))
            throw new FileNotFoundException($"Executável do Reports não encontrado em {hostDirectory}.", dll);

        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = hostDirectory };
        start.ArgumentList.Add(dll);
        return start;
    }

    /// <summary>Entrada do processo filho: <c>--render-pdf &lt;entrada.fpx&gt; &lt;saida.pdf&gt; [título]</c>.</summary>
    public static int RunAsChild(string[] args)
    {
        try
        {
            if (args.Length < 3 || args[0] != ChildFlag)
            {
                Console.Error.WriteLine($"Uso: {ChildFlag} <entrada.fpx> <saida.pdf> [titulo]");
                return 2;
            }

            FastReport.Utils.Config.WebMode = true;
            ReportFonts.RegisterSubstitutes();

            using var report = new Report();
            report.LoadPrepared(args[1]);
            File.WriteAllBytes(args[2], ExportInProcess(report, args.Length > 3 ? args[3] : null));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* temporário; o SO limpa */ }
    }
}
