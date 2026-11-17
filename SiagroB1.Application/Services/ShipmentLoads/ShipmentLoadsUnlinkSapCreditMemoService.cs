using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// "Desvincular NF de devolução" (GAC-1226): tira do documento de devolução a NF de devolução do SAP (A/R Credit
/// Memo). Serve ao usuário que escolheu a nota errada e à sincronização, quando a nota é cancelada no SAP.
/// </summary>
/// <remarks>
/// A devolução continua Confirmada e volta a "Aguardando NF do SAP": o físico dela (saldo, armazém, contrato) não
/// depende da nota e não é tocado. CFOP e natureza das linhas ficam como estavam. Não consulta o SAP.
/// </remarks>
public class ShipmentLoadsUnlinkSapCreditMemoService(
    IUnitOfWork db,
    SalesInvoicesChangeLogService changeLog,
    IConfiguration? configuration = null)
{
    public const string NotLinkedMessage = "Esta devolução não está vinculada a uma NF de devolução do SAP.";

    /// <param name="loadKey">Carga do documento; nula quando quem chama é a sincronização, que parte do documento.</param>
    /// <param name="reason">Motivo gravado no log quando o desvínculo não é uma escolha do usuário.</param>
    public async Task<SalesInvoice> ExecuteAsync(
        Guid? loadKey, Guid returnInvoiceKey, string username, string? reason = null)
    {
        if (!ErpMode.IsSapB1(configuration))
            throw new ApplicationException(ShipmentLoadsLinkSapCreditMemoService.SapOnlyMessage);

        var invoice = await db.Context.SalesInvoices.FirstOrDefaultAsync(x => x.Key == returnInvoiceKey);

        if (invoice is null || (loadKey is not null && invoice.ShipmentLoadKey != loadKey))
            throw new NotFoundException($"Sales invoice not found key {returnInvoiceKey}");

        if (invoice.SapCreditMemoDocEntry is null)
            throw new ApplicationException(NotLinkedMessage);

        var described = ShipmentLoadsLinkSapCreditMemoService.Describe(
            invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.SapCreditMemoDocNum);

        invoice.SapCreditMemoDocEntry = null;
        invoice.SapCreditMemoDocNum = null;
        invoice.TaxDocumentNumber = null;
        invoice.TaxDocumentSeries = null;
        invoice.ChaveNFe = null;

        changeLog.Register(
            invoice.Key, ContractChangeLogFields.SapReturnInvoice, described,
            string.IsNullOrWhiteSpace(reason) ? null : reason, username);

        await db.SaveChangesAsync();

        return invoice;
    }
}
