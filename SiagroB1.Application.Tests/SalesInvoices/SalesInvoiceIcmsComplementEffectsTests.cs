using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Spec 2026-10-18 S8: o complemento de ICMS de saída só tem efeito FISCAL — sem título a receber, sem alocação/razão
/// de contrato, sem efeito em carga, liberação nem custódia, e sem "Devolver".
/// </summary>
/// <remarks>
/// Os documentos são montados direto (sem o <see cref="SalesInvoicesCreateIcmsComplementService"/>) e com os vínculos
/// que o complemento real nunca leva — contrato na linha com natureza que gera financeiro, carga, liberação: é o pior
/// caso (Review Focus 1), e prova que os serviços de efeito recusam pelo TIPO, não pela ausência do vínculo.
/// </remarks>
public class SalesInvoiceIcmsComplementEffectsTests
{
    private static async Task<(SalesFirmReceivableSeed Seed, SalesInvoice Invoice, SalesInvoiceItem Line)> ComplementAsync(
        InvoiceStatus status = InvoiceStatus.Pending)
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();

        // Contrato forçado na linha, natureza que gera financeiro e valor diferente de zero.
        var (invoice, line) = await seed.AddInvoiceAsync(
            quantity: 1_000m, unitPrice: 90m, status: status, type: SalesInvoiceType.IcmsComplement, number: "DS000009");

        var tracked = await seed.Context.SalesInvoices.SingleAsync(i => i.Key == invoice.Key);
        tracked.NfeStatus = NfeStatus.Authorized;
        await seed.Context.SaveChangesAsync();
        seed.Context.ChangeTracker.Clear();

        return (seed, invoice, line);
    }

    /// <summary>Carga Faturada com saldo GRAVADO que o recálculo mudaria: qualquer recálculo deixa rastro.</summary>
    private static async Task<ShipmentLoad> AddLoadAsync(SalesFirmReceivableSeed seed)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000077",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalQuantity = 100_000,
            InvoicedQuantity = 100_000,
            Status = ShipmentLoadStatus.Invoiced,
        };
        seed.Context.ShipmentLoads.Add(load);
        await seed.Context.SaveChangesAsync();
        return load;
    }

    /// <summary>Liberação com embarcado GRAVADO que o recálculo (sem alocação nenhuma) zeraria.</summary>
    private static async Task<SalesShipmentRelease> AddReleaseAsync(SalesFirmReceivableSeed seed)
    {
        var release = SalesContractsAllocationTestSupport.NewRelease(seed.Contract.Key, released: 5_000m, shipped: 777m);
        seed.Context.SalesShipmentReleases.Add(release);
        await seed.Context.SaveChangesAsync();
        return release;
    }

    private static async Task LinkAsync(SalesFirmReceivableSeed seed, SalesInvoice invoice, SalesInvoiceItem line,
        Guid loadKey, Guid releaseKey)
    {
        var trackedInvoice = await seed.Context.SalesInvoices.SingleAsync(i => i.Key == invoice.Key);
        trackedInvoice.ShipmentLoadKey = loadKey;
        var trackedLine = await seed.Context.SalesInvoicesItems.SingleAsync(i => i.Key == line.Key);
        trackedLine.SalesShipmentReleaseKey = releaseKey;
        await seed.Context.SaveChangesAsync();
        seed.Context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Authorized_complement_confirms_without_receivable_or_allocation()
    {
        var (seed, invoice, line) = await ComplementAsync();

        await seed.Confirm().ExecuteAsync(invoice.Key, "ana");

        var saved = await seed.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Empty(await seed.TitlesOfAsync(line, includeCanceled: true));
        Assert.Empty(await seed.Context.SalesContractsAllocations.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// S7: como a devolução própria, o complemento só confirma pela NF-e autorizada — mesmo sem o gate (o serviço do seed
    /// é construído sem ele). Confirmado sem NF-e, não seria mais transmitido e travaria o cancelamento da original.
    /// </summary>
    [Theory]
    [InlineData(NfeStatus.None)]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Rejected)]
    [InlineData(NfeStatus.Denied)]
    [InlineData(NfeStatus.Cancelled)]
    public async Task Manual_confirm_of_a_complement_without_authorized_nfe_is_refused(NfeStatus nfeStatus)
    {
        var (seed, invoice, _) = await ComplementAsync();
        var tracked = await seed.Context.SalesInvoices.SingleAsync(i => i.Key == invoice.Key);
        tracked.NfeStatus = nfeStatus;
        await seed.Context.SaveChangesAsync();
        seed.Context.ChangeTracker.Clear();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => seed.Confirm().ExecuteAsync(invoice.Key, "ana"));

        Assert.Equal("Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.", ex.Message);
        var saved = await seed.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key);
        Assert.Equal(InvoiceStatus.Pending, saved.InvoiceStatus);
    }

    [Fact]
    public async Task Receivable_sync_ignores_a_confirmed_complement_with_contract_and_financial_nature()
    {
        var (seed, _, line) = await ComplementAsync(InvoiceStatus.Confirmed);

        await seed.SyncAsync(line);

        Assert.Empty(await seed.TitlesOfAsync(line, includeCanceled: true));
    }

    [Fact]
    public async Task Complement_does_not_touch_shipment_load_or_release()
    {
        var (seed, invoice, line) = await ComplementAsync();
        var load = await AddLoadAsync(seed);
        var release = await AddReleaseAsync(seed);
        await LinkAsync(seed, invoice, line, load.Key, release.Key);

        await seed.Confirm().ExecuteAsync(invoice.Key, "ana");

        var savedLoad = await seed.Context.ShipmentLoads.AsNoTracking().SingleAsync(l => l.Key == load.Key);
        Assert.Equal(ShipmentLoadStatus.Invoiced, savedLoad.Status);
        Assert.Equal(100_000m, savedLoad.InvoicedQuantity);
        Assert.Empty(await seed.Context.ShipmentLoadsChangeLogs.AsNoTracking().ToListAsync());
        Assert.Empty(await seed.Context.ShipmentLoadMovements.AsNoTracking().ToListAsync());

        var savedRelease = await seed.Context.SalesShipmentReleases.AsNoTracking().SingleAsync(r => r.Key == release.Key);
        Assert.Equal(777m, savedRelease.ShippedQuantity);
    }

    [Fact]
    public async Task Complement_of_a_custody_nature_confirms_without_the_custody_guard()
    {
        // A natureza é a da original: no complemento de uma nota de custódia ela tem papel de custódia, e o complemento
        // não tem (nem pode ter) romaneio — a guarda recusaria a confirmação com a NF-e já autorizada.
        var (seed, invoice, _) = await ComplementAsync();
        var effect = await seed.Context.UsageEffects.SingleAsync(e => e.UsageCode == seed.FinancialUsageCode);
        effect.StorageCustodyRole = StorageCustodyRole.StorageReturn;
        await seed.Context.SaveChangesAsync();
        seed.Context.ChangeTracker.Clear();

        await seed.Confirm().ExecuteAsync(invoice.Key, "ana");

        var saved = await seed.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal(StorageCustodyRole.None, saved.CustodyRole);
    }

    [Fact]
    public async Task Cancelling_complement_runs_no_load_hook()
    {
        var (seed, invoice, line) = await ComplementAsync(InvoiceStatus.Confirmed);
        var load = await AddLoadAsync(seed);
        var release = await AddReleaseAsync(seed);
        await LinkAsync(seed, invoice, line, load.Key, release.Key);
        var tracked = await seed.Context.SalesInvoices.SingleAsync(i => i.Key == invoice.Key);
        tracked.NfeStatus = NfeStatus.Cancelled; // fase 2 do cancelamento: a SEFAZ já cancelou
        await seed.Context.SaveChangesAsync();
        seed.Context.ChangeTracker.Clear();

        await seed.Cancel().CancelAfterNfeAsync(invoice.Key, "ana");

        var saved = await seed.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key);
        Assert.Equal(InvoiceStatus.Cancelled, saved.InvoiceStatus);

        var savedLoad = await seed.Context.ShipmentLoads.AsNoTracking().SingleAsync(l => l.Key == load.Key);
        Assert.Equal(ShipmentLoadStatus.Invoiced, savedLoad.Status);
        Assert.Equal(100_000m, savedLoad.InvoicedQuantity);
        Assert.Empty(await seed.Context.ShipmentLoadMovements.AsNoTracking().ToListAsync());
        Assert.Empty(await seed.Context.ShipmentLoadsChangeLogs.AsNoTracking().ToListAsync());
        Assert.Empty(await seed.TitlesOfAsync(line, includeCanceled: true));

        var savedRelease = await seed.Context.SalesShipmentReleases.AsNoTracking().SingleAsync(r => r.Key == release.Key);
        Assert.Equal(777m, savedRelease.ShippedQuantity);
    }

    [Fact]
    public async Task Complement_has_no_return()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var sale = await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        sale.InvoiceType = SalesInvoiceType.IcmsComplement;
        await s.Sale.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => NfeReturnTestSeed.CreateReturnAsync(s, 30000m, "Carga recusada"));

        Assert.Equal("Só um documento de venda (Normal) pode ser devolvido por aqui.", ex.Message);
    }
}
