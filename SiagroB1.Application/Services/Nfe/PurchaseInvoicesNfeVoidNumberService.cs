using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Inutilizar numeração" do documento de entrada própria.</summary>
public class PurchaseInvoicesNfeVoidNumberService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeNumberReservationService reservation,
    ILogger<PurchaseInvoicesNfeVoidNumberService> logger)
    : NfeVoidNumberServiceBase<PurchaseInvoice>(db, new PurchaseInvoiceNfeStore(db), settingsService, sefaz, reservation, logger)
{
    public const string ThirdPartyMessage = "NF-e de terceiro não tem numeração do Siagro para inutilizar.";

    protected override void EnsureIssuer(PurchaseInvoice document)
    {
        if (document.IssuerType != DocumentIssuerType.Own)
            throw new DefaultException(ThirdPartyMessage);
    }
}
