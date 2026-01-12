using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.SalesInvoices.Factories;

public static class SalesInvoiceCopyFactory
{
    public static SalesInvoice CreateFrom(SalesInvoice original, string username)
    {
        if (original == null)
            throw new ArgumentNullException(nameof(original));

        var clone = new SalesInvoice
        {
            DocNumberKey = original.DocNumberKey,
            BranchCode =  original.BranchCode,
            InvoiceStatus = InvoiceStatus.Pending,
            InvoiceType = original.InvoiceType,
            CardCode =  original.CardCode,
            CardName =  original.CardName,
            GrossWeight = original.GrossWeight,
            NetWeight = original.NetWeight,
            DeliveryCardCode =  original.DeliveryCardCode,
            DeliveryCardName = original.DeliveryCardName,
            TruckingCompanyCode  = original.TruckingCompanyCode,
            TruckingCompanyName = original.TruckingCompanyName,
            TruckCode = original.TruckCode,
            FreightTerms =  original.FreightTerms,
            FreightCostStandard =  original.FreightCostStandard,
            Comments =  original.Comments,
            TaxPayerComments =  original.TaxPayerComments,
            TaxComments =  original.TaxComments, 
            DeliveryStatus = SalesInvoiceDeliveryStatus.Open,
            CreatedBy = username,
            CreatedAt = DateTime.Now
        };

        foreach (var item in original.Items)
        {
            clone.AddItem(new SalesInvoiceItem
            {
                ItemCode = item.ItemCode,
                ItemName = item.ItemName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                UnitOfMeasureCode  = item.UnitOfMeasureCode,
                SalesContractKey =  item.SalesContractKey,
            });    
        }
        
        return clone;
    }
    
   
}