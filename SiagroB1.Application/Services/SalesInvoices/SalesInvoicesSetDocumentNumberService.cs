using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesSetDocumentNumberService(IUnitOfWork db, SalesInvoicesChangeLogService changeLog)
{
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

        // Branco vira null: a chave de acesso é opcional, e gravar "" faria todo documento
        // seguinte sem chave colidir com o anterior.
        var number = withoutTaxDocument ? null : Normalize(documentNumber);
        var series = withoutTaxDocument ? null : Normalize(documentSeries);
        var chave = withoutTaxDocument ? null : Normalize(ChaveNFe);

        var taxDocumentConflict = await FindTaxDocumentConflictAsync(number, series, invoice);
        if (taxDocumentConflict is not null)
            throw new ApplicationException(
                $"Nota fiscal {number}, série {series} já informada no documento de saída {taxDocumentConflict.InvoiceNumber}.");

        var chaveConflict = await FindChaveNFeConflictAsync(chave, invoice);
        if (chaveConflict is not null)
            throw new ApplicationException(
                $"Chave de acesso {chave} já informada no documento de saída {chaveConflict.InvoiceNumber}.");

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

    /// <summary>
    /// A chave da NF-e é única nacionalmente por construção, então a busca é global — sem
    /// escopo de filial, ao contrário do número/série.
    /// </summary>
    private async Task<SalesInvoice?> FindChaveNFeConflictAsync(string? chaveNFe, SalesInvoice invoice)
    {
        if (chaveNFe is null)
            return null;

        return await db.Context.SalesInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.ChaveNFe == chaveNFe &&
                x.Key != invoice.Key &&
                x.InvoiceStatus != InvoiceStatus.Cancelled);
    }

    private async Task<SalesInvoice?> FindTaxDocumentConflictAsync(
        string? documentNumber, string? documentSeries, SalesInvoice invoice)
    {
        if (documentNumber is null || documentSeries is null)
            return null;

        return await db.Context.SalesInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.BranchCode == invoice.BranchCode &&
                x.TaxDocumentNumber == documentNumber &&
                x.TaxDocumentSeries == documentSeries &&
                x.Key != invoice.Key &&
                x.InvoiceStatus != InvoiceStatus.Cancelled);
    }
}
