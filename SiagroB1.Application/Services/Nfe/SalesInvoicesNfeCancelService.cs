using SiagroB1.Domain.Entities;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Cancelar NF-e" do documento de saída (venda e devolução própria).</summary>
public class SalesInvoicesNfeCancelService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    SalesInvoiceNfeCancellationHandler handler,
    NfeNumberReservationService reservation)
    : NfeCancelServiceBase<SalesInvoice>(new SalesInvoiceNfeStore(db), settingsService, sefaz, handler, reservation);
