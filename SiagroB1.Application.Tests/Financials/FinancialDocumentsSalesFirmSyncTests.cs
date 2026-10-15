using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15: o firme a receber por linha × contrato, derivado do ledger, e o que muda nele.</summary>
public class FinancialDocumentsSalesFirmSyncTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    // ---- Distribuição (modelo do spec) ----

    [Fact]
    public void Distribute_without_volume_puts_the_whole_value_on_the_line_contract()
    {
        var result = FinancialDocumentsSalesFirmSyncService.Distribute(500m, A, []);
        Assert.Equal(500m, Assert.Single(result).Value);
        Assert.Equal(A, result.Keys.Single());

        var zero = FinancialDocumentsSalesFirmSyncService.Distribute(500m, A, [(B, 0m)]);
        Assert.Equal(A, zero.Keys.Single());
    }

    [Fact]
    public void Distribute_follows_the_ledger_shares()
    {
        var result = FinancialDocumentsSalesFirmSyncService.Distribute(1_000m, A, [(A, 600m), (B, 400m)]);
        Assert.Equal(600m, result[A]);
        Assert.Equal(400m, result[B]);
    }

    [Fact]
    public void Distribute_puts_the_rounding_residue_on_the_largest_share()
    {
        var result = FinancialDocumentsSalesFirmSyncService.Distribute(100m, A, [(A, 1m), (B, 1m), (C, 1m)]);

        Assert.Equal(100m, result.Values.Sum());
        Assert.Equal(33.34m, result[A]);   // empate: o contrato da linha leva o resíduo
        Assert.Equal(33.33m, result[B]);
        Assert.Equal(33.33m, result[C]);
    }

    [Fact]
    public void Distribute_ignores_contracts_whose_net_volume_is_not_positive()
    {
        // Linha toda realocada para B: A fica com 0 líquido.
        var result = FinancialDocumentsSalesFirmSyncService.Distribute(1_000m, A, [(A, 0m), (B, 1_000m)]);
        Assert.Equal(B, result.Keys.Single());
    }

    [Fact]
    public void Distribute_of_a_return_uses_the_negative_volumes_as_shares()
    {
        var result = FinancialDocumentsSalesFirmSyncService.Distribute(300m, A, [(A, -200m), (B, -100m)]);
        Assert.Equal(200m, result[A]);
        Assert.Equal(100m, result[B]);
    }

    // ---- Nascimento (G1–G4) ----

    [Fact]
    public async Task Confirmed_line_with_contract_and_financial_usage_creates_one_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(line, seed.Contract, 1_000m);

        await seed.SyncAsync(line);

        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        Assert.Equal(FinancialDirection.Receivable, firm.Direction);
        Assert.Equal(FinancialDocumentNature.Firm, firm.Nature);
        Assert.Equal(FinancialDocumentOrigin.SalesInvoice, firm.OriginType);
        Assert.Equal(SalesFirmReceivableSeed.DefaultLineAmount, firm.NetAmount);
        Assert.Equal(seed.Contract.Key, firm.SalesContractKey);
        Assert.Null(firm.PurchaseContractKey);
        Assert.Equal("VC-1", firm.OriginDocNumber);
        Assert.Equal(invoice.Key, firm.SalesInvoiceKey);
        Assert.Equal("7001-1", firm.TaxDocumentNumber);
        Assert.Equal("C0001", firm.CardCode);
        Assert.Equal("CLIENTE TESTE", firm.CardName);
        Assert.Equal("01", firm.BranchCode);
        Assert.Equal(new DateTime(2026, 10, 15), firm.DueDate);
        Assert.Equal(CurrencyType.Brl, firm.Currency);
        Assert.Equal(FinancialDocumentStatus.Open, firm.Status);
        Assert.Equal("ST-0001", firm.Code);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("noContract")]
    [InlineData("nonFinancialUsage")]
    [InlineData("noUsage")]
    [InlineData("custody")]
    [InlineData("remittance")]
    public async Task Lines_that_fail_the_rules_create_nothing(string scenario)
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.AddInvoiceAsync(
            status: scenario == "pending" ? InvoiceStatus.Pending : InvoiceStatus.Confirmed,
            withContract: scenario != "noContract",
            usageCode: scenario == "nonFinancialUsage" ? seed.NonFinancialUsageCode : null,
            withUsage: scenario != "noUsage");
        if (scenario is "custody" or "remittance")
        {
            var tracked = await seed.Context.SalesInvoices.SingleAsync(i => i.Key == invoice.Key);
            if (scenario == "custody") tracked.CustodyRole = StorageCustodyRole.StorageEntry;
            else tracked.ShipmentReleaseKey = Guid.NewGuid();
            await seed.Context.SaveChangesAsync();
        }

        await seed.SyncAsync(line);

        Assert.Empty(await seed.TitlesOfAsync(line, includeCanceled: true));
    }

    [Fact]
    public async Task Sapb1_mode_creates_the_firm_even_when_the_usage_has_no_flag()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync(erp: "SAPB1");
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed, usageCode: seed.NonFinancialUsageCode);

        await seed.SyncAsync(line);

        Assert.Single(await seed.TitlesOfAsync(line));
    }

    [Fact]
    public async Task Syncing_twice_does_not_duplicate()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);

        await seed.SyncAsync(line);
        await seed.SyncAsync(line);

        Assert.Single(await seed.TitlesOfAsync(line, includeCanceled: true));
        Assert.Equal(1, seed.Numbers.NumberCalls);
    }

    [Fact]
    public async Task Two_syncs_before_saving_do_not_duplicate_the_pending_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);

        await seed.Sync().EnqueueSyncAsync([line.Key!.Value], "ana");
        await seed.Sync().EnqueueSyncAsync([line.Key!.Value], "ana");
        await seed.Db.SaveChangesAsync();

        Assert.Single(await seed.TitlesOfAsync(line, includeCanceled: true));
        Assert.Equal(1, seed.Numbers.NumberCalls);
    }

    // ---- Realocação (D7) ----

    [Fact]
    public async Task Ledger_split_between_two_contracts_creates_one_firm_per_contract()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var other = await seed.AddContractAsync("VC-2");
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(line, seed.Contract, 1_000m);
        await seed.AllocateAsync(line, seed.Contract, -400m, SalesContractAllocationOrigin.Reallocation);
        await seed.AllocateAsync(line, other, 400m, SalesContractAllocationOrigin.Reallocation);

        await seed.SyncAsync(line, SalesFirmSyncTrigger.Reallocation);

        var titles = await seed.TitlesOfAsync(line);
        Assert.Equal(54_000m, titles.Single(t => t.SalesContractKey == seed.Contract.Key).NetAmount);
        Assert.Equal(36_000m, titles.Single(t => t.SalesContractKey == other.Key).NetAmount);
        Assert.Equal("VC-2", titles.Single(t => t.SalesContractKey == other.Key).OriginDocNumber);
    }

    [Fact]
    public async Task Reallocation_recalculates_the_loose_firm_and_logs_the_old_value()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var other = await seed.AddContractAsync("VC-2");
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(line, seed.Contract, 1_000m);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));

        await seed.AllocateAsync(line, seed.Contract, -400m, SalesContractAllocationOrigin.Reallocation);
        await seed.AllocateAsync(line, other, 400m, SalesContractAllocationOrigin.Reallocation);
        await seed.SyncAsync(line, SalesFirmSyncTrigger.Reallocation);

        Assert.Equal(54_000m, (await seed.ReloadAsync(firm.Key)).NetAmount);
        var log = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking().SingleAsync(l => l.FinancialDocumentKey == firm.Key);
        Assert.Equal(FinancialDocumentChangeLogFields.NetAmount, log.Field);
        Assert.Equal("90.000,00", log.OldValue);
        Assert.Equal("54.000,00", log.NewValue);
    }

    [Fact]
    public async Task Whole_line_reallocated_cancels_the_loose_firm_with_the_reallocation_reason()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var other = await seed.AddContractAsync("VC-2");
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(line, seed.Contract, 1_000m);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));

        await seed.AllocateAsync(line, seed.Contract, -1_000m, SalesContractAllocationOrigin.Reallocation);
        await seed.AllocateAsync(line, other, 1_000m, SalesContractAllocationOrigin.Reallocation);
        await seed.SyncAsync(line, SalesFirmSyncTrigger.Reallocation);

        var cancelled = await seed.ReloadAsync(firm.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, cancelled.Status);
        Assert.Equal("Volume realocado para outro contrato", cancelled.CancellationReason);
        Assert.Equal(other.Key, Assert.Single(await seed.TitlesOfAsync(line)).SalesContractKey);
    }

    [Fact]
    public async Task Reallocation_of_a_composed_firm_is_refused_before_any_write()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var other = await seed.AddContractAsync("VC-2");
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(line, seed.Contract, 1_000m);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        await seed.ComposeAsync(firm);
        await seed.AllocateAsync(line, seed.Contract, -400m, SalesContractAllocationOrigin.Reallocation);
        await seed.AllocateAsync(line, other, 400m, SalesContractAllocationOrigin.Reallocation);

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            seed.Sync().EnqueueSyncAsync([line.Key!.Value], "ana", SalesFirmSyncTrigger.Reallocation));

        Assert.Equal(
            $"O título {firm.Code} da linha realocada já está composto no título FN-PROV-V1: desfaça a composição antes de realocar.",
            error.Message);
        Assert.DoesNotContain(seed.Context.ChangeTracker.Entries<FinancialDocument>(), e => e.State == EntityState.Added);
    }

    // ---- Estorno / cancelamento do documento ----

    [Fact]
    public async Task Document_leaving_confirmed_cancels_the_loose_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));

        await seed.SetStatusAsync(invoice, InvoiceStatus.Pending);
        await seed.SyncAsync(line);

        var cancelled = await seed.ReloadAsync(firm.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, cancelled.Status);
        Assert.Equal("Documento de saída cancelado ou estornado", cancelled.CancellationReason);
        Assert.Equal("ana", cancelled.CanceledBy);
    }

    [Fact]
    public async Task Document_leaving_confirmed_with_a_composed_firm_is_refused()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        await seed.ComposeAsync(firm);
        await seed.SetStatusAsync(invoice, InvoiceStatus.Pending);

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            seed.Sync().EnqueueSyncAsync([line.Key!.Value], "ana"));

        Assert.Equal(
            $"O título {firm.Code} do documento de saída DS000001 já está composto no título FN-PROV-V1: desfaça a composição antes de cancelar ou estornar o documento.",
            error.Message);
    }

    [Fact]
    public async Task Document_with_a_dation_on_the_firm_is_refused_on_undo()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        await new FinancialDocumentsDationService(seed.Db).ExecuteAsync(firm.Key, 100m, DateTime.Today, "acordo", "ana");

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            seed.Sync().EnsureCanUndoAsync(invoice));

        Assert.Equal(
            $"O título {firm.Code} do documento de saída DS000001 já tem baixa por dação ou encontro de contas: estorne-a antes de cancelar ou estornar o documento.",
            error.Message);
    }

    [Fact]
    public async Task Usage_that_stops_creating_financial_cancels_the_loose_firm_on_the_next_sync()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (_, line) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.SyncAsync(line);
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        var tracked = await seed.Context.SalesInvoicesItems.SingleAsync(i => i.Key == line.Key);
        tracked.UsageCode = seed.NonFinancialUsageCode;
        await seed.Context.SaveChangesAsync();

        await seed.SyncAsync(line);

        Assert.Equal("Natureza não gera financeiro", (await seed.ReloadAsync(firm.Key)).CancellationReason);
    }

    // ---- Devolução (D3) ----

    [Fact]
    public async Task Confirmed_return_creates_a_sales_return_that_nets_the_loose_origin_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (origin, originLine) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(originLine, seed.Contract, 1_000m);
        await seed.SyncAsync(originLine);
        var firm = Assert.Single(await seed.TitlesOfAsync(originLine));
        var (_, returnLine) = await seed.AddInvoiceAsync(quantity: 100m, status: InvoiceStatus.Confirmed,
            type: SalesInvoiceType.Return, originInvoiceKey: origin.Key, originItemKey: originLine.Key, number: "DS000002");
        await seed.AllocateAsync(returnLine, seed.Contract, -100m, SalesContractAllocationOrigin.Return);

        await seed.SyncAsync(returnLine);

        var refund = Assert.Single(await seed.TitlesOfAsync(returnLine));
        Assert.Equal(FinancialDocumentNature.SalesReturn, refund.Nature);
        Assert.Equal(9_000m, refund.NetAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, refund.Status);
        Assert.Equal(9_000m, (await seed.ReloadAsync(firm.Key)).SettledAmount);
    }

    [Fact]
    public async Task Return_against_a_composed_origin_stays_open()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (origin, originLine) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.AllocateAsync(originLine, seed.Contract, 1_000m);
        await seed.SyncAsync(originLine);
        await seed.ComposeAsync(Assert.Single(await seed.TitlesOfAsync(originLine)));
        var (_, returnLine) = await seed.AddInvoiceAsync(quantity: 100m, status: InvoiceStatus.Confirmed,
            type: SalesInvoiceType.Return, originInvoiceKey: origin.Key, originItemKey: originLine.Key, number: "DS000002");
        await seed.AllocateAsync(returnLine, seed.Contract, -100m, SalesContractAllocationOrigin.Return);

        await seed.SyncAsync(returnLine);

        var refund = Assert.Single(await seed.TitlesOfAsync(returnLine));
        Assert.Equal(0m, refund.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, refund.Status);
    }

    [Fact]
    public async Task Origin_marked_returned_keeps_its_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (origin, originLine) = await seed.AddInvoiceAsync(status: InvoiceStatus.Confirmed);
        await seed.SyncAsync(originLine);
        await seed.SetStatusAsync(origin, InvoiceStatus.Returned);

        await seed.SyncAsync(originLine);

        Assert.Single(await seed.TitlesOfAsync(originLine));
    }
}
