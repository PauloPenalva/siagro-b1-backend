using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Carta de Correção" do documento de entrada (entrada própria e devolução de compra).</summary>
public class PurchaseInvoicesNfeCorrectionService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeNumberReservationService reservation,
    ILogger<PurchaseInvoicesNfeCorrectionService> logger)
    : NfeCorrectionServiceBase<PurchaseInvoice>(db, new PurchaseInvoiceNfeStore(db), settingsService, sefaz, reservation, logger)
{
    protected override void EnsureIssuer(PurchaseInvoice document)
    {
        if (document.IssuerType != DocumentIssuerType.Own)
            throw new DefaultException("NF-e de terceiro não recebe carta de correção pelo Siagro.");
    }
}
