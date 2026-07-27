using SiagroB1.Application.Services.ShipmentBilling;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentBilling;

/// <summary>
/// Trava que impede faturar duas vezes o mesmo romaneio (chamado de duplicidade de
/// documento de saída: a segunda submissão re-apontava o romaneio para a nova invoice,
/// deixando a primeira órfã e consumindo o saldo do contrato duas vezes).
/// </summary>
public class ShipmentBillingTransactionGuardServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentBillingTransactionGuardService Service() => new(_db.Context);

    private async Task<StorageTransaction> SeedShipmentAsync(
        string code,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        Guid? salesInvoiceKey = null,
        string? invoiceNumber = null)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C0001",
            ItemCode = "I0001",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "W01",
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = status,
            NetWeight = 30_000m,
            SalesInvoiceKey = salesInvoiceKey,
            InvoiceNumber = invoiceNumber,
        };

        _db.Context.StorageTransactions.Add(transaction);
        await _db.Context.SaveChangesAsync();
        return transaction;
    }

    [Fact]
    public async Task EnsureCanBill_ConfirmedAndUnbilled_DoesNotThrow()
    {
        var transaction = await SeedShipmentAsync("ROM-001");

        await Service().EnsureCanBillAsync([transaction.Key]);
    }

    [Fact]
    public async Task EnsureCanBill_AlreadyLinkedToInvoice_Throws()
    {
        var transaction = await SeedShipmentAsync(
            "ROM-002",
            StorageTransactionsStatus.Invoiced,
            salesInvoiceKey: Guid.NewGuid(),
            invoiceNumber: "000001686");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().EnsureCanBillAsync([transaction.Key]));

        Assert.Contains("ROM-002", ex.Message);
        Assert.Contains("000001686", ex.Message);
    }

    [Fact]
    public async Task EnsureCanBill_NotConfirmed_Throws()
    {
        // Sem SalesInvoiceKey, mas fora de Confirmed (cancelado/retornado): não fatura.
        var transaction = await SeedShipmentAsync("ROM-003", StorageTransactionsStatus.Cancelled);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().EnsureCanBillAsync([transaction.Key]));

        Assert.Contains("ROM-003", ex.Message);
    }

    [Fact]
    public async Task EnsureCanBill_UnknownKey_Throws()
    {
        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().EnsureCanBillAsync([Guid.NewGuid()]));
    }

    [Fact]
    public async Task EnsureCanBill_MixedBatch_ThrowsForTheWholeBatch()
    {
        // Lote com um romaneio livre e outro já faturado: recusa tudo, não fatura em parte.
        var free = await SeedShipmentAsync("ROM-004");
        var billed = await SeedShipmentAsync(
            "ROM-005",
            StorageTransactionsStatus.Invoiced,
            salesInvoiceKey: Guid.NewGuid(),
            invoiceNumber: "000001687");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().EnsureCanBillAsync([free.Key, billed.Key]));

        Assert.Contains("ROM-005", ex.Message);
    }

    [Fact]
    public async Task EnsureCanBill_EmptyBatch_DoesNotThrow()
    {
        await Service().EnsureCanBillAsync([]);
    }
}
