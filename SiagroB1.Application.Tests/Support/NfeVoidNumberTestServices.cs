using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Inutilização da numeração sobre os seeds da emissão, com a SEFAZ simulada.</summary>
public static class NfeVoidNumberTestServices
{
    /// <summary>Chave da tentativa rejeitada: ano 26, CNPJ 12345678000195, série 001, número 000000233.</summary>
    public const string RejectedAccessKey = "35261012345678000195550010000002331481516230";

    /// <summary>
    /// Documento cancelado com a NF-e rejeitada. <paramref name="sentToSefaz"/> = false é a rejeição na
    /// validação local: número reservado, mas sem chave nem ambiente.
    /// </summary>
    public static async Task RejectAndCancelSaleAsync(UnitOfWork db, Guid key, bool sentToSefaz = true)
    {
        var invoice = await db.Context.SalesInvoices.SingleAsync(x => x.Key == key);
        invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        Reject(invoice, sentToSefaz);
        await db.SaveChangesAsync();
    }

    public static async Task RejectAndCancelPurchaseAsync(UnitOfWork db, Guid key, bool sentToSefaz = true)
    {
        var invoice = await db.Context.PurchaseInvoices.SingleAsync(x => x.Key == key);
        invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        invoice.IssuerType = DocumentIssuerType.Own;
        Reject(invoice, sentToSefaz);
        await db.SaveChangesAsync();
    }

    /// <summary>Campos da NF-e comuns aos dois documentos (o InvoiceStatus não tem setter na interface).</summary>
    private static void Reject(Domain.Interfaces.INfeDocument invoice, bool sentToSefaz)
    {
        invoice.NfeStatus = NfeStatus.Rejected;
        invoice.TaxDocumentNumber = "000000233";
        invoice.TaxDocumentSeries = "1";
        invoice.NfeRandomCode = "48151623";
        invoice.NfeStatusCode = sentToSefaz ? "929" : null;
        invoice.NfeStatusReason = sentToSefaz
            ? "Rejeição: Informado CST de diferimento sem as informações de diferimento [nItem: 1]"
            : "Rejeitada na validação local, nada foi enviado: XSD";
        invoice.ChaveNFe = sentToSefaz ? RejectedAccessKey : null;
        invoice.NfeEnvironment = sentToSefaz ? NfeEnvironment.Homologation : null;
    }

    public static BranchNfeSettingsService Settings(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, new NfeOptions(NfeTestSeed.Config()), sefaz);

    public static SalesInvoicesNfeVoidNumberService SalesVoid(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, Settings(db, sefaz), sefaz, new FakeNfeNumberReservationService(),
            NullLogger<SalesInvoicesNfeVoidNumberService>.Instance);
}
