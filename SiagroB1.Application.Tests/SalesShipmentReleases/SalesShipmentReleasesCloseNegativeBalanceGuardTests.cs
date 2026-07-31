using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesShipmentReleases;

/// <summary>
/// Liberação faturada ALÉM do liberado não pode ser finalizada: finalizada, ela sai do
/// faturamento e do recálculo, e o excedente fica órfão. O guard decide sobre o saldo
/// RECALCULADO do ledger — <c>ShippedQuantity</c> é persistido-derivado e pode estar defasado.
/// Espelha <c>SalesContractsCloseNegativeBalanceGuardTests</c>.
/// </summary>
public class SalesShipmentReleasesCloseNegativeBalanceGuardTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesShipmentReleasesCloseService CloseService() =>
        new(_db.Context, new SalesShipmentReleasesRecalculateShippedService(_db.Context));

    /// <param name="persistedShipped">
    /// Valor gravado em <c>ShippedQuantity</c>. Quando difere de <paramref name="ledgerVolume"/>,
    /// reproduz o drift do agregado persistido.
    /// </param>
    private async Task<SalesShipmentRelease> SeedAsync(
        decimal released, decimal ledgerVolume, decimal? persistedShipped = null,
        ReleaseStatus status = ReleaseStatus.Actived)
    {
        var sr = new SalesShipmentRelease
        {
            Key = Guid.NewGuid(),
            SalesContractKey = Guid.NewGuid(),
            DeliveryLocationCode = "01",
            ReleasedQuantity = released,
            ShippedQuantity = persistedShipped ?? ledgerVolume,
            Status = status,
        };
        _db.Context.SalesShipmentReleases.Add(sr);

        if (ledgerVolume != decimal.Zero)
        {
            _db.Context.SalesContractsAllocations.Add(new SalesContractAllocation
            {
                Key = Guid.NewGuid(),
                SalesContractKey = sr.SalesContractKey,
                SalesInvoiceItemKey = Guid.NewGuid(),
                SalesShipmentReleaseKey = sr.Key,
                Volume = ledgerVolume,
                Origin = SalesContractAllocationOrigin.Billing,
            });
        }

        await _db.Context.SaveChangesAsync();
        return sr;
    }

    private Task<SalesShipmentRelease> ReloadAsync(Guid key) =>
        _db.Context.SalesShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key);

    [Theory]
    [InlineData(ReleaseStatus.Actived)]
    [InlineData(ReleaseStatus.Paused)]
    public async Task Close_LedgerBalanceNegative_Throws(ReleaseStatus status)
    {
        var sr = await SeedAsync(released: 1000m, ledgerVolume: 1300m, status: status);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => CloseService().ExecuteAsync(sr.Key, "joao"));

        Assert.Contains("além", ex.Message);
        Assert.Equal(status, (await ReloadAsync(sr.Key)).Status);
    }

    /// <summary>
    /// Saldo positivo encerra normalmente: finalizar é abrir mão do volume não embarcado.
    /// </summary>
    [Fact]
    public async Task Close_LedgerBalancePositive_Succeeds()
    {
        var sr = await SeedAsync(released: 1000m, ledgerVolume: 300m);

        await CloseService().ExecuteAsync(sr.Key, "joao");

        Assert.Equal(ReleaseStatus.Completed, (await ReloadAsync(sr.Key)).Status);
    }

    [Fact]
    public async Task Close_LedgerBalanceZero_Succeeds()
    {
        var sr = await SeedAsync(released: 1000m, ledgerVolume: 1000m);

        await CloseService().ExecuteAsync(sr.Key, "joao");

        Assert.Equal(ReleaseStatus.Completed, (await ReloadAsync(sr.Key)).Status);
    }

    /// <summary>
    /// Trava a decisão de design: o guard lê o ledger, não o agregado. Uma liberação correta
    /// cujo <c>ShippedQuantity</c> dessincronizou seria barrada por engano se lesse o persistido.
    /// </summary>
    [Fact]
    public async Task Close_PersistedShippedNegativeButLedgerHealthy_Succeeds()
    {
        var sr = await SeedAsync(released: 1000m, ledgerVolume: 300m, persistedShipped: 1300m);

        await CloseService().ExecuteAsync(sr.Key, "joao");

        Assert.Equal(ReleaseStatus.Completed, (await ReloadAsync(sr.Key)).Status);
    }

    /// <summary>
    /// Finalizar não tem efeito colateral de saldo: o guard calcula sem gravar, mesmo quando
    /// o ledger contradiz o persistido (para ressincronizar existe o Recalcular Saldo).
    /// </summary>
    [Fact]
    public async Task Close_DoesNotPersistRecalculatedShipped()
    {
        var sr = await SeedAsync(released: 1000m, ledgerVolume: 300m, persistedShipped: 250m);

        await CloseService().ExecuteAsync(sr.Key, "joao");

        Assert.Equal(250m, (await ReloadAsync(sr.Key)).ShippedQuantity);
    }
}
