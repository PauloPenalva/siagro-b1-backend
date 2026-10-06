using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Monta o envio da CC-e sobre os seeds da emissão, com a SEFAZ simulada.</summary>
public static class NfeCorrectionTestServices
{
    private static BranchNfeSettingsService Settings(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, new NfeOptions(NfeTestSeed.Config()), sefaz);

    public static SalesInvoicesNfeCorrectionService SalesCorrection(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, Settings(db, sefaz), sefaz, new FakeNfeNumberReservationService(),
            NullLogger<SalesInvoicesNfeCorrectionService>.Instance);

    public static PurchaseInvoicesNfeCorrectionService PurchaseCorrection(UnitOfWork db, FakeNfeSefazClient sefaz) =>
        new(db, Settings(db, sefaz), sefaz, new FakeNfeNumberReservationService(),
            NullLogger<PurchaseInvoicesNfeCorrectionService>.Instance);
}
