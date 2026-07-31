using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Com o faturamento podendo furar a liberação de entrega, uma liberação aberta pode ficar com
/// <c>AvailableQuantity</c> NEGATIVO. Somada crua, ela DIMINUI a reserva e portanto AUMENTA o
/// saldo físico liberável — o contrato aceitaria liberar volume que não existe. Por isso a
/// reserva soma cada liberação com piso zero, na entidade e no espelho em SQL, que precisam
/// continuar dando o mesmo resultado.
/// </summary>
public class SalesContractsPhysicalAvailableToReleaseTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesContractsGetShipmentReleasesAvailableService Service() =>
        new(_db, NullLogger<SalesContractsGetShipmentReleasesAvailableService>.Instance);

    private static SalesContract NewContract(decimal totalVolume, decimal allocatedVolume) => new()
    {
        Key = Guid.NewGuid(),
        Code = "SC-PHY",
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        TotalVolume = totalVolume,
        AllocatedVolume = allocatedVolume,
        Status = ContractStatus.Approved,
    };

    private async Task<SalesContract> SeedAsync(
        decimal totalVolume, decimal allocatedVolume,
        decimal released, decimal shipped, ReleaseStatus status = ReleaseStatus.Actived)
    {
        var sc = NewContract(totalVolume, allocatedVolume);
        sc.SalesShipmentReleases.Add(new SalesShipmentRelease
        {
            Key = Guid.NewGuid(), SalesContractKey = sc.Key, DeliveryLocationCode = "01",
            ReleasedQuantity = released, ShippedQuantity = shipped, Status = status,
        });
        _db.Context.SalesContracts.Add(sc);
        await _db.Context.SaveChangesAsync();
        return sc;
    }

    /// <summary>
    /// Contrato de 2.000 com 1.300 já faturados numa liberação de 1.000 (saldo −300).
    /// Sem o piso, a reserva de −300 elevaria o físico de 700 para 1.000.
    /// </summary>
    [Fact]
    public void NegativeRelease_DoesNotInflatePhysicalAvailable()
    {
        var sc = NewContract(totalVolume: 2000m, allocatedVolume: 1300m);
        sc.SalesShipmentReleases.Add(new SalesShipmentRelease
        {
            Key = Guid.NewGuid(), SalesContractKey = sc.Key, DeliveryLocationCode = "01",
            ReleasedQuantity = 1000m, ShippedQuantity = 1300m, Status = ReleaseStatus.Actived,
        });

        Assert.Equal(-300m, sc.SalesShipmentReleases.Single().AvailableQuantity);
        Assert.Equal(0m, sc.ReservedByOpenReleases);
        Assert.Equal(700m, sc.PhysicalAvailableToRelease);
    }

    /// <summary>
    /// Liberação positiva continua reservando normalmente — o piso não pode zerar a reserva real.
    /// </summary>
    [Fact]
    public void PositiveRelease_StillReservesFullAvailableQuantity()
    {
        var sc = NewContract(totalVolume: 2000m, allocatedVolume: 300m);
        sc.SalesShipmentReleases.Add(new SalesShipmentRelease
        {
            Key = Guid.NewGuid(), SalesContractKey = sc.Key, DeliveryLocationCode = "01",
            ReleasedQuantity = 1000m, ShippedQuantity = 300m, Status = ReleaseStatus.Actived,
        });

        Assert.Equal(700m, sc.ReservedByOpenReleases);
        Assert.Equal(1000m, sc.PhysicalAvailableToRelease);
    }

    /// <summary>
    /// Espelho em SQL: o contrato do cenário acima tem 700 físicos e deve continuar listado —
    /// mas pelo valor certo, não pelos 1.000 inflados.
    /// </summary>
    [Fact]
    public async Task Query_NegativeRelease_ListsContractByClampedBalance()
    {
        var sc = await SeedAsync(
            totalVolume: 2000m, allocatedVolume: 1300m, released: 1000m, shipped: 1300m);

        var listed = Assert.Single(await Service().Query().ToListAsync());
        Assert.Equal(sc.Key, listed.Key);
        Assert.Equal(700m, listed.PhysicalAvailableToRelease);
    }

    /// <summary>
    /// Contrato esgotado por sobre-faturamento não pode voltar à lista por causa do negativo:
    /// 1.000 contratados, 1.300 faturados numa liberação de 1.000 → físico −300.
    /// </summary>
    [Fact]
    public async Task Query_OverBilledContract_NotListed()
    {
        await SeedAsync(
            totalVolume: 1000m, allocatedVolume: 1300m, released: 1000m, shipped: 1300m);

        Assert.Empty(await Service().Query().ToListAsync());
    }
}
