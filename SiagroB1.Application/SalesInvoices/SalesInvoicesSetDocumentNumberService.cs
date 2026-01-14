using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.SalesInvoices;

public class SalesInvoicesSetDocumentNumberService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string documentNumber, string documentSeries, string ChaveNFe, string username)
    {
        var invoice = await db.Context.SalesInvoices
            .FirstOrDefaultAsync(x => x.Key == key)
            ??  throw new NotFoundException("Sales invoice not found.");

        if (HasTaxDocument(documentNumber, documentSeries, invoice))
                throw new ApplicationException($"Tax document {documentNumber} has already been added.");
        
        if (HasChaveNFe(ChaveNFe))
            throw new ApplicationException($"Chave de acesso {ChaveNFe} has already been added.");
        
        try
        {
            invoice.TaxDocumentNumber = documentNumber;
            invoice.TaxDocumentSeries = documentSeries;
            invoice.ChaveNFe = ChaveNFe;
            invoice.UpdatedBy = username;
            invoice.UpdatedAt = DateTime.Now;

            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }

    private bool HasChaveNFe(string ChaveNFe)
    {
        return db.Context.SalesInvoices
            .AsNoTracking()
            .Any(x => x.ChaveNFe == ChaveNFe);
    }

    private bool HasTaxDocument(string documentNumber, string documentSeries, SalesInvoice invoice)
    {
        return db.Context.SalesInvoices
            .AsNoTracking()
            .Any(x =>
                x.BranchCode == invoice.BranchCode && 
                x.TaxDocumentNumber == documentNumber && 
                x.TaxDocumentSeries == documentSeries &&
                x.Key != invoice.Key &&
                x.InvoiceStatus != InvoiceStatus.Cancelled);

        
    }
}