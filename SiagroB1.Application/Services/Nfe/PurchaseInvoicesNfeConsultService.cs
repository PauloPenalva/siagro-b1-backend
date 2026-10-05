using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Consultar situação" (spec §9.3). Consulta pela chave, no ambiente da EMISSÃO, e monta o
/// procNFe a partir do XML assinado gravado antes do envio.
/// </summary>
public class PurchaseInvoicesNfeConsultService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    PurchaseInvoiceNfeResultHandler resultHandler,
    NfeNumberReservationService reservation,
    ILogger<PurchaseInvoicesNfeConsultService> logger)
    : NfeConsultServiceBase<PurchaseInvoice>(db, new PurchaseInvoiceNfeStore(db), settingsService, sefaz, resultHandler, reservation, logger);
