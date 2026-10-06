using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>O DANFE sai do procNFe gravado, pelo layout da Zeus copiado sem alteração.</summary>
public class DanfeReportServiceTests
{
    [Fact]
    public async Task Authorized_nfe_renders_a_pdf()
    {
        var db = TestDb.CreateUnitOfWork();
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), settings);
        var invoiceKey = Guid.NewGuid();
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoiceKey, Kind = NfeXmlKind.Authorized, CreatedAt = DateTime.Now,
            Xml = NfeProcComposer.Compose(signed.Xml, FakeNfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!),
        });
        await db.SaveChangesAsync();

        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance);

        var (pdf, fileName) = await new DanfeReportService(db, environment, header).GeneratePdfAsync(invoiceKey);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal($"{signed.AccessKey}-danfe.pdf", fileName);
    }

    [Fact]
    public async Task Without_authorized_nfe_it_is_not_found()
    {
        var db = TestDb.CreateUnitOfWork();
        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, new ConfigurationBuilder().Build(), NullLogger<ReportHeaderService>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new DanfeReportService(db, environment, header).GeneratePdfAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Authorized_purchase_nfe_renders_a_pdf()
    {
        var db = TestDb.CreateUnitOfWork();
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.PurchaseEntryInput(), settings);
        var invoiceKey = Guid.NewGuid();
        db.Context.PurchaseInvoiceNfeXmls.Add(new PurchaseInvoiceNfeXml
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoiceKey, Kind = NfeXmlKind.Authorized, CreatedAt = DateTime.Now,
            Xml = NfeProcComposer.Compose(signed.Xml, FakeNfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!),
        });
        await db.SaveChangesAsync();

        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance);

        var (pdf, fileName) = await new DanfeReportService(db, environment, header).GeneratePurchasePdfAsync(invoiceKey);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal($"{signed.AccessKey}-danfe.pdf", fileName);
    }

    [Fact]
    public async Task Cancelled_nfe_danfe_is_generated()
    {
        var db = TestDb.CreateUnitOfWork();
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), settings);
        var invoiceKey = Guid.NewGuid();
        db.Context.SalesInvoices.Add(new SalesInvoice { Key = invoiceKey, CardCode = "C1", NfeStatus = NfeStatus.Cancelled });
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoiceKey, Kind = NfeXmlKind.Authorized, CreatedAt = DateTime.Now,
            Xml = NfeProcComposer.Compose(signed.Xml, FakeNfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!),
        });
        await db.SaveChangesAsync();

        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance);

        var (pdf, _) = await new DanfeReportService(db, environment, header).GeneratePdfAsync(invoiceKey);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Registered_correction_renders_a_pdf()
    {
        var db = TestDb.CreateUnitOfWork();
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), settings);
        var invoiceKey = Guid.NewGuid();
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoiceKey, Kind = NfeXmlKind.Authorized, CreatedAt = DateTime.Now,
            Xml = NfeProcComposer.Compose(signed.Xml, FakeNfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!),
        });
        db.Context.SalesInvoiceNfeCorrections.Add(new SalesInvoiceNfeCorrection
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoiceKey, Sequence = 1, Text = "Placa correta XYZ9K87",
            StatusCode = 135, Reason = "Evento registrado e vinculado a NF-e", CreatedAt = DateTime.Now,
            ProcEventXml = CorrectionProcEvent(signed.AccessKey),
        });
        await db.SaveChangesAsync();

        var (pdf, fileName) = await Service(db).GenerateCorrectionPdfAsync(invoiceKey, 1);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal($"{signed.AccessKey}-cce-1.pdf", fileName);
    }

    [Fact]
    public async Task Missing_correction_is_not_found()
    {
        var db = TestDb.CreateUnitOfWork();

        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).GenerateCorrectionPdfAsync(Guid.NewGuid(), 1));
    }

    private static DanfeReportService Service(UnitOfWork db)
    {
        var environment = new TestWebHostEnvironment(Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        return new DanfeReportService(db, environment,
            new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance));
    }

    /// <summary>procEventoNFe real de CC-e (serializado pela Zeus), como o envio grava.</summary>
    private static string CorrectionProcEvent(string accessKey) => DFe.Utils.FuncoesXml.ClasseParaXmlString(
        new NFe.Classes.Servicos.Consulta.procEventoNFe
        {
            versao = "1.00",
            evento = new NFe.Classes.Servicos.Evento.evento
            {
                versao = "1.00",
                infEvento = new NFe.Classes.Servicos.Evento.infEventoEnv
                {
                    Id = $"ID110110{accessKey}01", cOrgao = DFe.Classes.Entidades.Estado.SP,
                    tpAmb = DFe.Classes.Flags.TipoAmbiente.Homologacao, CNPJ = accessKey.Substring(6, 14), chNFe = accessKey,
                    dhEvento = new DateTimeOffset(2026, 10, 6, 9, 15, 0, TimeSpan.FromHours(-3)),
                    tpEvento = NFe.Classes.Servicos.Tipos.NFeTipoEvento.TeNfeCartaCorrecao, nSeqEvento = 1, verEvento = "1.00",
                    detEvento = new NFe.Classes.Servicos.Evento.detEvento
                    {
                        versao = "1.00", descEvento = "Carta de Correcao", xCorrecao = "Placa correta XYZ9K87",
                        xCondUso = "A Carta de Correcao e disciplinada pelo paragrafo 1o-A do art. 7o do Convenio S/N",
                    },
                },
            },
            retEvento = new NFe.Classes.Servicos.Evento.retEvento
            {
                versao = "1.00",
                infEvento = new NFe.Classes.Servicos.Evento.infEventoRet
                {
                    tpAmb = DFe.Classes.Flags.TipoAmbiente.Homologacao, cOrgao = DFe.Classes.Entidades.Estado.SP,
                    cStat = 135, xMotivo = "Evento registrado e vinculado a NF-e", chNFe = accessKey,
                    tpEvento = NFe.Classes.Servicos.Tipos.NFeTipoEvento.TeNfeCartaCorrecao, nSeqEvento = 1,
                    dhRegEvento = new DateTime(2026, 10, 6, 9, 15, 5), nProt = "135260000000201",
                },
            },
        });
}
