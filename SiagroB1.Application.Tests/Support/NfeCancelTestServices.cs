using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.SalesInvoices;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Monta o cancelamento da NF-e sobre os seeds da emissão, com a SEFAZ simulada.</summary>
public static class NfeCancelTestServices
{
    /// <summary>Chave da filial semeada (CNPJ 12345678000195 nas posições 7–20).</summary>
    public const string AccessKey = "35261012345678000195550010000000011481516230";

    public const string AuthorizationProtocol = "135260000000001";

    public static async Task AuthorizeSaleAsync(UnitOfWork db, Guid key, InvoiceStatus status = InvoiceStatus.Confirmed)
    {
        var invoice = await db.Context.SalesInvoices.SingleAsync(x => x.Key == key);
        invoice.InvoiceStatus = status;
        invoice.NfeStatus = NfeStatus.Authorized;
        invoice.NfeEnvironment = NfeEnvironment.Homologation;
        invoice.ChaveNFe = AccessKey;
        invoice.NfeProtocol = AuthorizationProtocol;
        invoice.NfeStatusCode = "100";
        invoice.NfeStatusReason = "Autorizado o uso da NF-e";
        await db.SaveChangesAsync();
    }

    public static async Task AuthorizePurchaseAsync(UnitOfWork db, Guid key)
    {
        var invoice = await db.Context.PurchaseInvoices.SingleAsync(x => x.Key == key);
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        invoice.IssuerType = DocumentIssuerType.Own;
        invoice.NfeStatus = NfeStatus.Authorized;
        invoice.NfeEnvironment = NfeEnvironment.Homologation;
        invoice.ChaveNFe = AccessKey;
        invoice.NfeProtocol = AuthorizationProtocol;
        await db.SaveChangesAsync();
    }

    private static BranchNfeSettingsService Settings(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, new NfeOptions(NfeTestSeed.Config()), sefaz);

    public static SalesInvoiceNfeCancellationHandler SalesHandler(UnitOfWork db, SalesInvoicesCancelService? cancel = null) =>
        new(db, cancel ?? SalesInvoicesCancelServiceTests.Service(db), NullLogger<SalesInvoiceNfeCancellationHandler>.Instance);

    public static PurchaseInvoiceNfeCancellationHandler PurchaseHandler(UnitOfWork db) =>
        new(db, new PurchaseInvoicesCancelService(db), NullLogger<PurchaseInvoiceNfeCancellationHandler>.Instance);

    public static SalesInvoicesNfeCancelService SalesCancel(
        UnitOfWork db, FakeNfeSefazClient sefaz, SalesInvoicesCancelService? cancel = null,
        FakeNfeNumberReservationService? reservation = null) =>
        new(db, Settings(db, sefaz), sefaz, SalesHandler(db, cancel), reservation ?? new FakeNfeNumberReservationService());

    public static SalesInvoicesNfeCompleteCancellationService SalesComplete(UnitOfWork db, SalesInvoicesCancelService? cancel = null) =>
        new(db, SalesHandler(db, cancel), new FakeNfeNumberReservationService());

    public static PurchaseInvoicesNfeCancelService PurchaseCancel(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, Settings(db, sefaz), sefaz, PurchaseHandler(db), new FakeNfeNumberReservationService());
}
