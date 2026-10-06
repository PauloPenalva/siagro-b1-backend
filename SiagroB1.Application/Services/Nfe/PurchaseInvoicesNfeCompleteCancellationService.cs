using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Concluir cancelamento" do documento de entrada.</summary>
public class PurchaseInvoicesNfeCompleteCancellationService(
    IUnitOfWork db, PurchaseInvoiceNfeCancellationHandler handler, NfeNumberReservationService reservation)
    : NfeCompleteCancellationServiceBase<PurchaseInvoice>(new PurchaseInvoiceNfeStore(db), handler, reservation);
