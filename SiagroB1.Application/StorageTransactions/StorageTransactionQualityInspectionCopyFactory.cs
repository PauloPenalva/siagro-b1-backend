using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.StorageTransactions;

public static class StorageTransactionQualityInspectionCopyFactory
{
    public static StorageTransactionQualityInspection CreateFrom(
        StorageTransactionQualityInspection original,
        StorageTransaction parent)
    {
        return new StorageTransactionQualityInspection
        {
            StorageTransaction = parent,
            QualityAttribCode = original.QualityAttribCode,
            Value = original.Value,
            LossValue =  original.LossValue
        };
    }
}
