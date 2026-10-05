using NFe.Classes;
using NFe.Danfe.Base.NFe;
using NFe.Danfe.OpenFast.NFe;
using SiagroB1.Infra;
using SiagroB1.Infra.Nfe;

namespace SiagroB1.Reports.Services;

/// <summary>
/// DANFE da NF-e autorizada, a partir do procNFe gravado, com o layout FastReport da Zeus
/// (ThirdParty/Zeus-LGPL, DLL própria NFe.Danfe.Base, sem alteração). Homologação sai com a marca
/// "sem valor fiscal" do próprio layout. ⚠️ O caminho padrão do .frx na Zeus usa barra invertida e
/// falha no Linux: o caminho vai absoluto. ⚠️ O script do .frx usa <c>Environment.NewLine</c>: exige
/// desligar os stubs de script do FastReport (ver <see cref="DisableScriptStubs"/>).
/// </summary>
public class DanfeReportService(IUnitOfWork db, IWebHostEnvironment env, ReportHeaderService header)
{
    /// <summary>
    /// ⚠️ Desliga os stubs de segurança do FastReport, e com eles toda a validação de script: no
    /// FastReport.OpenSource 2026.1.3 a StopList/RegexStopList nunca é lida pelo núcleo, então nada mais
    /// barra o script. Os stubs declaram um <c>System.Environment</c> vazio e o script do NFeRetrato.frx
    /// usa <c>Environment.NewLine</c> (CS0117 "don't use the method 'NewLine'"). Aceito porque todo .frx
    /// é arquivo versionado no repositório (nenhum modelo vem do usuário ou do banco). Armadilha: nunca
    /// carregue um modelo de fora do repositório sem restaurar a validação. Chamado uma vez no startup
    /// do Reports e pelo módulo de inicialização dos testes (que não passam pelo Program.cs).
    /// </summary>
    public static void DisableScriptStubs()
    {
        // ScriptSecurityProps só nasce quando EnableScriptSecurity é definido explicitamente.
        FastReport.Utils.Config.WebMode = true;
        FastReport.Utils.Config.EnableScriptSecurity = true;
        FastReport.Utils.Config.ScriptSecurityProps.AddStubClasses = false;
    }

    public async Task<(byte[] Pdf, string FileName)> GeneratePdfAsync(Guid invoiceKey)
    {
        var xml = await db.Context.SalesInvoiceNfeXmls.LatestAuthorizedXmlAsync(invoiceKey);
        return Render(xml);
    }

    /// <summary>DANFE da NF-e do documento de entrada (entrada própria ou devolução de compra).</summary>
    public async Task<(byte[] Pdf, string FileName)> GeneratePurchasePdfAsync(Guid invoiceKey)
    {
        var xml = await db.Context.PurchaseInvoiceNfeXmls.LatestAuthorizedXmlAsync(invoiceKey);
        return Render(xml);
    }

    private (byte[] Pdf, string FileName) Render(string xml)
    {
        var proc = new nfeProc().CarregarDeXmlString(xml);
        var template = Path.Combine(env.ContentRootPath, "ThirdParty", "Zeus-LGPL", "NFe.Danfe.Base", "NFe", "NFeRetrato.frx");

        FastReport.Utils.Config.WebMode = true;

        var danfe = new DanfeFrNfe(proc, new ConfiguracaoDanfeNfe(header.LogoBytes()),
            desenvolvedor: "IDX Consultoria e Sistemas", arquivoRelatorio: template);
        using var report = danfe.Relatorio;

        return (danfe.ExportarPdf(), $"{proc.protNFe.infProt.chNFe}-danfe.pdf");
    }
}
