using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Consultar situação" (spec §9.3). Consulta pela chave, no ambiente da EMISSÃO: em processamento
/// monta o procNFe do XML assinado; autorizada, reconhece o cancelamento feito na SEFAZ.
/// </summary>
public class PurchaseInvoicesNfeConsultService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    PurchaseInvoiceNfeResultHandler resultHandler,
    PurchaseInvoiceNfeCancellationHandler cancellationHandler,
    NfeNumberReservationService reservation,
    ILogger<PurchaseInvoicesNfeConsultService> logger)
    : NfeConsultServiceBase<PurchaseInvoice>(db, new PurchaseInvoiceNfeStore(db), settingsService, sefaz, resultHandler, cancellationHandler, reservation, logger);
