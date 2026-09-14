using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

/// <summary>
/// Monta os serviços da Conferência de Saldo de Armazém sobre um banco InMemory. Usa o
/// <c>WarehouseComplementService</c> REAL (lê a mesma WAREHOUSE_COMPLEMENTS semeada aqui), para
/// que o gate "armazém de terceiros" seja exercitado de ponta a ponta.
/// </summary>
internal sealed partial class WarehouseReconciliationsTestContext
{
    public const string ThirdPartyWarehouse = "ARM-T";
    public const string OwnWarehouse = "ARM-P";
    public const string Item = "SOJA";
    public const string Branch = "01";

    public UnitOfWork Db { get; } = TestDb.CreateUnitOfWork();
    public FakeStringLocalizer<Resource> Resource { get; } = new();

    private int _seq;

    public async Task<WarehouseReconciliationReason> SeedReasonAsync(bool active = true, string? code = null)
    {
        var reason = new WarehouseReconciliationReason
        {
            Key = Guid.NewGuid(),
            Code = code ?? $"R{++_seq}",
            Description = "Quebra técnica",
            Active = active,
        };
        Db.Context.WarehouseReconciliationReasons.Add(reason);
        await Db.Context.SaveChangesAsync();
        return reason;
    }

    public async Task SeedOwnWarehouseAsync()
    {
        Db.Context.WarehouseComplements.Add(new WarehouseComplement { WarehouseCode = OwnWarehouse, IsOwn = true });
        await Db.Context.SaveChangesAsync();
    }

    public async Task<StorageTransaction> SeedStockAsync(
        StorageTransactionType type,
        decimal quantity,
        DateTime? date,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        string warehouse = ThirdPartyWarehouse)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = $"SEED-{++_seq:0000}",
            CardCode = "C0001",
            ItemCode = Item,
            UnitOfMeasureCode = "KG",
            WarehouseCode = warehouse,
            BranchCode = Branch,
            TransactionType = type,
            TransactionStatus = status,
            TransactionDate = date,
            GrossWeight = quantity,
            NetWeight = quantity,
        };
        Db.Context.StorageTransactions.Add(transaction);
        await Db.Context.SaveChangesAsync();
        return transaction;
    }

    public static WarehouseReconciliation NewReconciliation(
        Guid reasonKey,
        decimal reportedBalance,
        DateTime? referenceDate = null,
        string warehouse = ThirdPartyWarehouse) => new()
        {
            WarehouseCode = warehouse,
            ItemCode = Item,
            UnitOfMeasureCode = "KG",
            BranchCode = Branch,
            ReferenceDate = referenceDate ?? DateTime.Today,
            ReportedBalance = reportedBalance,
            ReasonKey = reasonKey,
        };

    public WarehouseReconciliationReasonsCreateService ReasonsCreate() => new(Db, Resource);
    public WarehouseReconciliationReasonsUpdateService ReasonsUpdate() => new(Db, Resource);
    public WarehouseReconciliationReasonsDeleteService ReasonsDelete() => new(Db, Resource);
}
