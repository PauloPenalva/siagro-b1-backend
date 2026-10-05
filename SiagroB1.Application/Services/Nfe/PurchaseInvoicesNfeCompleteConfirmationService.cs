using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Concluir confirmação": refaz a confirmação de um documento de entrada com NF-e já autorizada.</summary>
public class PurchaseInvoicesNfeCompleteConfirmationService(IUnitOfWork db, PurchaseInvoiceNfeResultHandler resultHandler)
    : NfeCompleteConfirmationServiceBase<PurchaseInvoice>(new PurchaseInvoiceNfeStore(db), resultHandler);
