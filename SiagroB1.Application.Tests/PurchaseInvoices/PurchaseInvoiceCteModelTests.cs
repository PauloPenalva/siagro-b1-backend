using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Modelo de dados do CT-e de frete no Documento de Entrada.</summary>
public class PurchaseInvoiceCteModelTests
{
    [Fact]
    public async Task Transported_documents_roundtrip_with_cte_header()
    {
        var db = TestDb.CreateUnitOfWork();
        var cteKey = Guid.NewGuid();
        const string accessKey = "35261012345678000195550010000001231481516234";
        db.Context.PurchaseInvoices.Add(new PurchaseInvoice
        {
            Key = cteKey, BranchCode = "01", CardCode = "T-SP", IssuerType = DocumentIssuerType.ThirdParty,
            TaxDocumentKind = TaxDocumentKind.Cte, FreightStartState = "SP", FreightEndState = "MT",
            TransportedDocuments = [new PurchaseInvoiceTransportedDocument { Key = Guid.NewGuid(), AccessKey = accessKey }],
        });
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var read = await db.Context.PurchaseInvoices.Include(x => x.TransportedDocuments).SingleAsync(x => x.Key == cteKey);

        Assert.Equal(TaxDocumentKind.Cte, read.TaxDocumentKind);
        Assert.Equal("SP", read.FreightStartState);
        Assert.Equal("MT", read.FreightEndState);
        Assert.Equal(accessKey, read.TransportedDocuments.Single().AccessKey);
    }
}
