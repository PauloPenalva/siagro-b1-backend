using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Concluir confirmação": refaz a confirmação de um documento de saída com NF-e já autorizada.</summary>
public class SalesInvoicesNfeCompleteConfirmationService(IUnitOfWork db, SalesInvoiceNfeResultHandler resultHandler)
    : NfeCompleteConfirmationServiceBase<SalesInvoice>(new SalesInvoiceNfeStore(db), resultHandler);
