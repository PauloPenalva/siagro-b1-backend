using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// A devolução ao armazém gerada pelo RETORNO de um documento de saída legado não pode ser
/// cancelada nem estornada pela tela de Romaneios.
/// </summary>
/// <remarks>
/// Os dois guards existentes barram por <c>ShipmentLoadKey</c> e <c>RefusedFromShipmentLoadKey</c>,
/// e a devolução do fluxo legado tem os dois NULOS — não há carga nenhuma envolvida. Sem uma
/// condição sobre <c>GeneratedByReturnInvoiceKey</c> ela escapa dos dois, e cancelá-la por lá
/// derrubaria em silêncio o crédito do armazém, deixando o grão sem lugar nenhum: fora da nota,
/// que está devolvida, e fora do estoque.
/// </remarks>
public class StorageTransactionsSalesInvoiceReturnGuardTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransactionsCancelService Cancel() =>
        new(_db, new ShipmentReleasesRecalculateShippedService(_db.Context));

    /// <summary>
    /// <c>balanceService</c> vai nulo de propósito: ele só é usado em <c>ValidateBalance</c>, que
    /// roda DEPOIS do guard sob teste. Se algum dia o guard for movido para baixo, este null vira
    /// NRE — e é justamente o alarme que se quer.
    /// </summary>
    private StorageTransactionsReverseService Reverse() =>
        new(_db,
            null!,
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new FakeStringLocalizer<Resource>());

    private async Task<StorageTransaction> SeedInvoiceReturnAsync()
    {
        // O vínculo é com o documento de RETORNO, e não com a nota de origem: uma nota pode ser
        // retornada em parcelas, cada uma com sua devolução ao armazém.
        var returnInvoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C0001",
            BranchCode = "01",
            InvoiceNumber = "000123",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Return,
        };

        var entry = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "RM000888",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM99",
            BranchCode = "01",
            GrossWeight = 20_000m,
            NetWeight = 20_000m,
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            TransactionOrigin = TransactionCode.StorageTransaction,
            // Os dois nulos DE PROPÓSITO: não há carga, e é o que faz os guards antigos não
            // pegarem esta transação.
            ShipmentLoadKey = null,
            RefusedFromShipmentLoadKey = null,
            GeneratedByReturnInvoiceKey = returnInvoice.Key,
        };

        _db.Context.SalesInvoices.Add(returnInvoice);
        _db.Context.StorageTransactions.Add(entry);
        await _db.SaveChangesAsync();

        return entry;
    }

    [Fact]
    public async Task An_invoice_return_cannot_be_cancelled_from_the_shipments_screen()
    {
        var entry = await SeedInvoiceReturnAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Cancel().ExecuteAsync(entry.Key, "tester", TransactionCode.StorageTransaction));

        Assert.Contains("devolução gerada pelo retorno", error.Message);
        Assert.Contains("000123", error.Message);
    }

    [Fact]
    public async Task An_invoice_return_cannot_be_reversed_from_the_shipments_screen()
    {
        var entry = await SeedInvoiceReturnAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Reverse().ExecuteAsync(entry.Key, "tester", TransactionCode.StorageTransaction));

        Assert.Contains("devolução gerada pelo retorno", error.Message);
        Assert.Contains("000123", error.Message);
    }
}
