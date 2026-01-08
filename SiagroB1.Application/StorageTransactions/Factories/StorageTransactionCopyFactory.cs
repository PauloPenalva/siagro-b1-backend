using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.StorageTransactions.Factories;

public static class StorageTransactionCopyFactory
{
    public static StorageTransaction CreateFrom(
        StorageTransaction original,
        string userName)
    {
        if (original == null)
            throw new ArgumentNullException(nameof(original));

        var clone = new StorageTransaction
        {
            // ===== Identidade / auditoria =====
            CreatedAt = DateTime.Now.Date,
            CreatedBy = userName,

            // ===== Estado do negócio =====
            TransactionDate = DateTime.Now.Date,
            TransactionTime = DateTime.Now.TimeOfDay.ToString(),
            TransactionStatus = StorageTransactionsStatus.Pending,

            // ===== Dados de negócio =====
            Code = null,
            BranchCode = original.BranchCode,
            DocNumberKey = original.DocNumberKey,
            StorageAddressCode = original.StorageAddressCode,
            TransactionType = original.TransactionType,
            TransactionOrigin = original.TransactionOrigin,

            CardCode = original.CardCode,
            CardName = original.CardName,

            ItemCode = original.ItemCode,
            ItemName = original.ItemName,

            UnitOfMeasureCode = original.UnitOfMeasureCode,

            GrossWeight = original.GrossWeight,
            DryingDiscount = original.DryingDiscount,
            CleaningDiscount = original.CleaningDiscount,
            OthersDicount = original.OthersDicount,
            NetWeight = original.NetWeight,

            WarehouseCode = original.WarehouseCode,

            ShipmentReleaseKey = original.ShipmentReleaseKey,
            ShippingOrderKey = original.ShippingOrderKey,

            TruckDriverCode = original.TruckDriverCode,
            TruckCode = original.TruckCode,

            WeighingTicketKey = original.WeighingTicketKey,

            ProcessingCostCode = original.ProcessingCostCode,

            InvoiceNumber = original.InvoiceNumber,
            InvoiceSerie = original.InvoiceSerie,
            InvoiceQty = original.InvoiceQty,
            ChaveNFe = original.ChaveNFe,

            AvaiableVolumeToAllocate = original.AvaiableVolumeToAllocate,
            Comments = original.Comments,

            SalesInvoiceKey = null // regra clara: não herdar
        };

        // ===== Copiar apenas entidades filhas do aggregate =====
        foreach (var inspection in original.QualityInspections)
        {
            clone.QualityInspections.Add(
                StorageTransactionQualityInspectionCopyFactory.CreateFrom(
                    inspection, clone));
        }

        return clone;
    }
}
