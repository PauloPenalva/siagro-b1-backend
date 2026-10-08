using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Inutilizar numeração" do documento de saída (venda e devolução própria).</summary>
public class SalesInvoicesNfeVoidNumberService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeNumberReservationService reservation,
    ILogger<SalesInvoicesNfeVoidNumberService> logger)
    : NfeVoidNumberServiceBase<SalesInvoice>(db, new SalesInvoiceNfeStore(db), settingsService, sefaz, reservation, logger);
