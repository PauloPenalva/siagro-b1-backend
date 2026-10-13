using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class PurchaseFiscalReconciliationReportServiceTests
{
    private static PurchaseFiscalReconciliationReportService Service(FiscalReconciliationSeed seed) =>
        new(seed.Db, new RecordingFastReportService());

    private static PurchaseFiscalReconciliationReportRequest Request() =>
        new() { CardCode = FiscalReconciliationSeed.Supplier, ItemCode = FiscalReconciliationSeed.Product };

    [Fact]
    public async Task Groups_by_contract_with_links_below_and_unlinked_invoices_at_the_end()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var contract = await seed.AddContractAsync("PC-9");
        var tx = await seed.AddTransactionAsync(29_820m, contract: contract, code: "RM000010");
        var (_, line) = await seed.AddInvoiceAsync(30_000m, taxNumber: "000555");
        await seed.LinkAsync(line, tx, 30_000m);
        await seed.AddInvoiceAsync(1_000m, taxNumber: "000556");

        var rows = await Service(seed).BuildRowsAsync(Request());

        Assert.Equal(
            [("Contrato PC-9", "Romaneio", "RM000010"), ("Contrato PC-9", "NF", "000555-1"),
             (PurchaseFiscalReconciliationReportService.UnlinkedGroup, "NF livre", "000556-1")],
            rows.Select(r => (r.Group, r.Kind, r.Document)));
        var head = rows[0];
        Assert.Equal(("29.820,000", "30.000,000", "180,000", "Parcial"), (head.PhysicalText, head.FiscalText, head.DifferenceText, head.Status));
        Assert.Equal(1, head.TransactionCount);
    }

    [Fact]
    public async Task Split_transaction_appears_in_each_contract_but_counts_once_in_the_summary()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var c1 = await seed.AddContractAsync("PC-1");
        var c2 = await seed.AddContractAsync("PC-2");
        var tx = await seed.AddTransactionAsync(100m, contract: c1);
        seed.Db.Context.PurchaseContractsAllocations.Add(new PurchaseContractAllocation
            { Key = Guid.NewGuid(), PurchaseContractKey = c2.Key, StorageTransactionKey = tx.Key, Volume = 0m });
        await seed.Db.SaveChangesAsync();

        var rows = (await Service(seed).BuildRowsAsync(Request())).Where(r => r.Kind == "Romaneio").ToList();

        Assert.Equal(["Contrato PC-1", "Contrato PC-2"], rows.Select(r => r.Group));
        Assert.Equal([100m, 100m], rows.Select(r => r.GroupPhysical));
        Assert.Equal(100m, rows.Sum(r => r.SumPhysical));
        Assert.Equal(1, rows.Sum(r => r.TransactionCount));
    }

    [Fact]
    public async Task Status_filter_and_transaction_without_contract()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        await seed.AddTransactionAsync(100m, code: "RM-SEM");
        var request = Request();
        request.Statuses = [(int)FiscalReconciliationStatus.Reconciled];

        Assert.DoesNotContain(await Service(seed).BuildRowsAsync(request), r => r.Kind == "Romaneio");
        Assert.Equal("Sem contrato", Assert.Single(await Service(seed).BuildRowsAsync(Request())).Group);
    }
}
