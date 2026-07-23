using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

public class ShipmentReleasesGetServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public async Task GetById_IncludesBranch_SoDetailCanShowFilial()
    {
        // Espelha o detalhe de venda: o GET-by-key materializa a entidade e o $expand do
        // OData não expande nav em objeto único, então o serviço precisa incluir a Branch
        // por Include — senão o campo "Filial" fica em branco na tela de detalhe.
        var contractKey = Guid.NewGuid();
        _db.Context.PurchaseContracts.Add(new PurchaseContract
        {
            Key = contractKey, Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
            TotalVolume = 1_000m, Status = ContractStatus.Approved,
        });
        _db.Context.Set<Branch>().Add(new Branch { Code = "3", BranchName = "Yokotobi - Pilar", ShortName = "Filial Pilar" });
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contractKey,
            DeliveryLocationCode = "01", BranchCode = "3",
        };
        _db.Context.ShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();

        // Limpa o tracker para o fixup do EF não mascarar a ausência do Include.
        _db.Context.ChangeTracker.Clear();

        var service = new ShipmentReleasesGetService(_db, NullLogger<ShipmentReleasesGetService>.Instance);
        var result = await service.GetByIdAsync(release.Key);

        Assert.NotNull(result);
        Assert.NotNull(result.Branch);
        Assert.Equal("Filial Pilar", result.Branch.ShortName);
    }
}
