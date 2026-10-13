using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.StorageCustody;

/// <summary>Etapas esperadas por romaneio (spec 2026-10-12 §3.4) e montagem da seção.</summary>
public class StorageCustodyRulesTests
{
    private static StorageCustodyTransactionRow Row(
        StorageTransactionType type = StorageTransactionType.Receipt, bool processing = true, bool transfer = false,
        bool transferCancelled = false, StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        StorageOwnershipType ownership = StorageOwnershipType.ThirdParty,
        StorageAddressNature nature = StorageAddressNature.Regular) => new()
    {
        Key = Guid.NewGuid(), Code = "RM000001", TransactionType = type, TransactionStatus = status,
        GrossWeight = 30_000m, NetWeight = 28_000m, IsOwnershipTransfer = transfer, TransferCancelled = transferCancelled,
        OwnershipType = ownership, Nature = nature, RequiresProcessing = processing,
        StorageAddressCode = "L0001", CardCode = "P0001", ItemCode = "SOJA", UoM = "KG",
    };

    [Fact]
    public void Receipt_with_processing_expects_the_three_entry_steps() =>
        Assert.Equal(
            [StorageCustodyRole.ProcessingEntry, StorageCustodyRole.ProcessingSymbolicReturn, StorageCustodyRole.StorageEntry],
            StorageCustodyRules.ExpectedRoles(Row()));

    [Fact]
    public void Receipt_without_processing_expects_only_the_storage_entry() =>
        Assert.Equal([StorageCustodyRole.StorageEntry], StorageCustodyRules.ExpectedRoles(Row(processing: false)));

    [Fact]
    public void Transfer_receipt_expects_only_the_storage_entry_even_with_processing() =>
        Assert.Equal([StorageCustodyRole.StorageEntry], StorageCustodyRules.ExpectedRoles(Row(transfer: true)));

    [Fact]
    public void Shipment_expects_the_return() =>
        Assert.Equal([StorageCustodyRole.StorageReturn], StorageCustodyRules.ExpectedRoles(Row(StorageTransactionType.Shipment)));

    [Fact]
    public void Transfer_shipment_expects_the_symbolic_return() =>
        Assert.Equal([StorageCustodyRole.StorageSymbolicReturn],
            StorageCustodyRules.ExpectedRoles(Row(StorageTransactionType.Shipment, transfer: true)));

    [Fact]
    public void Romaneios_of_a_cancelled_transfer_expect_nothing()
    {
        Assert.Empty(StorageCustodyRules.ExpectedRoles(Row(transfer: true, transferCancelled: true)));
        Assert.Empty(StorageCustodyRules.ExpectedRoles(Row(StorageTransactionType.Shipment, transfer: true, transferCancelled: true)));
    }

    [Theory]
    [InlineData(StorageTransactionsStatus.Pending)]
    [InlineData(StorageTransactionsStatus.Cancelled)]
    public void Unconfirmed_romaneio_expects_nothing(StorageTransactionsStatus status) =>
        Assert.Empty(StorageCustodyRules.ExpectedRoles(Row(status: status)));

    [Fact]
    public void Invoiced_romaneio_counts_as_confirmed() =>
        Assert.NotEmpty(StorageCustodyRules.ExpectedRoles(Row(status: StorageTransactionsStatus.Invoiced)));

    [Fact]
    public void Own_lot_and_transshipment_lot_expect_nothing()
    {
        Assert.Empty(StorageCustodyRules.ExpectedRoles(Row(ownership: StorageOwnershipType.OwnedInOurCustody)));
        Assert.Empty(StorageCustodyRules.ExpectedRoles(Row(nature: StorageAddressNature.Transshipment)));
    }

    [Fact]
    public void Other_transaction_types_expect_nothing() =>
        Assert.Empty(StorageCustodyRules.ExpectedRoles(Row(StorageTransactionType.TechnicalLoss)));

    [Fact]
    public void Quantities_follow_gross_gross_net()
    {
        var row = Row();

        Assert.Equal(30_000m, StorageCustodyRules.ExpectedQuantity(StorageCustodyRole.ProcessingEntry, row, null));
        Assert.Equal(29_500m, StorageCustodyRules.ExpectedQuantity(StorageCustodyRole.ProcessingSymbolicReturn, row, 29_500m));
        Assert.Null(StorageCustodyRules.ExpectedQuantity(StorageCustodyRole.ProcessingSymbolicReturn, row, null));
        Assert.Equal(28_000m, StorageCustodyRules.ExpectedQuantity(StorageCustodyRole.StorageEntry, row, null));
        Assert.Equal(28_000m, StorageCustodyRules.ExpectedQuantity(StorageCustodyRole.StorageReturn, row, null));
        Assert.Equal(28_000m, StorageCustodyRules.ExpectedQuantity(StorageCustodyRole.StorageSymbolicReturn, row, null));
    }

    [Fact]
    public void Steps_show_situation_quantity_and_difference()
    {
        var row = Row();
        var entry = new StorageCustodyDocumentRow
        {
            TransactionKey = row.Key, Role = StorageCustodyRole.ProcessingEntry, DocumentKey = Guid.NewGuid(),
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceStatus = InvoiceStatus.Confirmed, Quantity = 29_500m,
            InvoiceNumber = "DE000001", TaxDocumentNumber = "000000123",
        };
        var symbolic = new StorageCustodyDocumentRow
        {
            TransactionKey = row.Key, Role = StorageCustodyRole.ProcessingSymbolicReturn, DocumentKey = Guid.NewGuid(),
            IsSalesInvoice = true, IssuerType = DocumentIssuerType.Own, InvoiceStatus = InvoiceStatus.Confirmed,
            NfeStatus = NfeStatus.Authorized, Quantity = 29_500m,
        };
        var otherRomaneio = new StorageCustodyDocumentRow { TransactionKey = Guid.NewGuid(), Role = StorageCustodyRole.StorageEntry };

        var steps = StorageCustodyRules.BuildSteps(row, [entry, symbolic, otherRomaneio]);

        Assert.Equal(3, steps.Count);
        Assert.Equal(StorageCustodyStepSituation.Recorded, steps[0].Situation);
        Assert.Equal(-500m, steps[0].Difference);
        Assert.Equal("000000123", steps[0].Number);
        Assert.Equal(StorageCustodyStepSituation.Authorized, steps[1].Situation);
        Assert.Equal(0m, steps[1].Difference);
        Assert.Equal(StorageCustodyStepSituation.Missing, steps[2].Situation);
        Assert.Null(steps[2].DocumentKey);
        Assert.Equal(28_000m, steps[2].ExpectedQuantity);
        Assert.Equal(StorageCustodyRole.StorageEntry, StorageCustodyRules.NextRole(steps));
    }

    [Theory]
    [InlineData(DocumentIssuerType.Own, InvoiceStatus.Pending, NfeStatus.None, StorageCustodyStepSituation.Generated)]
    [InlineData(DocumentIssuerType.Own, InvoiceStatus.Confirmed, NfeStatus.Authorized, StorageCustodyStepSituation.Authorized)]
    [InlineData(DocumentIssuerType.Own, InvoiceStatus.Pending, NfeStatus.Rejected, StorageCustodyStepSituation.Generated)]
    [InlineData(DocumentIssuerType.ThirdParty, InvoiceStatus.Pending, NfeStatus.None, StorageCustodyStepSituation.Generated)]
    [InlineData(DocumentIssuerType.ThirdParty, InvoiceStatus.Confirmed, NfeStatus.None, StorageCustodyStepSituation.Recorded)]
    public void Situation_follows_issuer_status_and_nfe(
        DocumentIssuerType issuer, InvoiceStatus status, NfeStatus nfe, StorageCustodyStepSituation expected) =>
        Assert.Equal(expected, StorageCustodyRules.Situation(new StorageCustodyDocumentRow
        {
            IssuerType = issuer, InvoiceStatus = status, NfeStatus = nfe,
        }));
}
