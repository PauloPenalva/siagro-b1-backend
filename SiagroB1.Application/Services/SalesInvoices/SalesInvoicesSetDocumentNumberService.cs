using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesSetDocumentNumberService(
    IUnitOfWork db,
    SalesInvoicesChangeLogService changeLog,
    SalesInvoiceTaxDocumentDuplicateGuard? taxDocumentDuplicateGuard = null)
{
    private readonly SalesInvoiceTaxDocumentDuplicateGuard duplicateGuard =
        taxDocumentDuplicateGuard ?? new SalesInvoiceTaxDocumentDuplicateGuard(db.Context);

    /// <summary>
    /// Número, série e chave chegam anuláveis: os três são parâmetros OData opcionais, e a
    /// chave de acesso é de preenchimento opcional no diálogo.
    ///
    /// <paramref name="withoutTaxDocument"/> marca a operação sem nota fiscal (GAC-1174). As duas
    /// situações se excluem, e o flag vence: número, série e chave são gravados nulos mesmo que
    /// cheguem preenchidos — e por isso também não passam pela trava de duplicidade.
    /// </summary>
    public async Task ExecuteAsync(
        Guid key, string? documentNumber, string? documentSeries, string? ChaveNFe, string username,
        bool withoutTaxDocument = false)
    {
        var invoice = await db.Context.SalesInvoices
            .FirstOrDefaultAsync(x => x.Key == key)
            ??  throw new NotFoundException("Sales invoice not found.");

        SalesInvoiceNfeLock.EnsureManualTaxDocument(invoice);

        // Branco vira null: a chave de acesso é opcional, e gravar "" faria todo documento
        // seguinte sem chave colidir com o anterior.
        var number = withoutTaxDocument ? null : Normalize(documentNumber);
        var series = withoutTaxDocument ? null : Normalize(documentSeries);
        var chave = withoutTaxDocument ? null : Normalize(ChaveNFe);

        await duplicateGuard.EnsureUniqueAsync(
            invoice.BranchCode, number, series, chave, invoice.Key);

        try
        {
            // Só a mudança do flag vai para o log: número/série/chave nunca foram logados, e
            // reconfirmar a NF sem mexer no flag não pode gerar linha.
            if (invoice.WithoutTaxDocument != withoutTaxDocument)
            {
                changeLog.Register(
                    invoice.Key,
                    ContractChangeLogFields.WithoutTaxDocument,
                    ContractChangeLogFields.DescribeYesNo(invoice.WithoutTaxDocument),
                    ContractChangeLogFields.DescribeYesNo(withoutTaxDocument),
                    username);
            }

            invoice.WithoutTaxDocument = withoutTaxDocument;
            invoice.TaxDocumentNumber = number;
            invoice.TaxDocumentSeries = series;
            invoice.ChaveNFe = chave;
            invoice.UpdatedBy = username;
            invoice.UpdatedAt = DateTime.Now;

            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
