using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Concluir cancelamento" do documento de saída.</summary>
public class SalesInvoicesNfeCompleteCancellationService(
    IUnitOfWork db, SalesInvoiceNfeCancellationHandler handler, NfeNumberReservationService reservation)
    : NfeCompleteCancellationServiceBase<SalesInvoice>(new SalesInvoiceNfeStore(db), handler, reservation);
