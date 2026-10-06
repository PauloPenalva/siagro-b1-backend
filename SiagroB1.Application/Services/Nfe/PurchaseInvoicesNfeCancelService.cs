using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Cancelar NF-e" do documento de entrada.</summary>
public class PurchaseInvoicesNfeCancelService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    PurchaseInvoiceNfeCancellationHandler handler,
    NfeNumberReservationService reservation)
    : NfeCancelServiceBase<PurchaseInvoice>(new PurchaseInvoiceNfeStore(db), settingsService, sefaz, handler, reservation)
{
    protected override void EnsureIssuer(PurchaseInvoice document)
    {
        if (document.IssuerType != DocumentIssuerType.Own)
            throw new DefaultException("NF-e de terceiro não é cancelada pelo Siagro: use o Cancelar do documento.");
    }
}
