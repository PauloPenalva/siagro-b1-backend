using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesShipmentReleases;

public class SalesShipmentReleasesGetServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public async Task GetById_IncludesBranch_SoDetailCanShowFilial()
    {
        // A tela de detalhe binda {Branch/ShortName}; o GET-by-key materializa a entidade
        // (o $expand do OData não expande nav em objeto único), então o serviço precisa
        // trazer a Branch por Include — senão o campo "Filial" fica em branco.
        var contractKey = Guid.NewGuid();
        _db.Context.SalesContracts.Add(new SalesContract
        {
            Key = contractKey, Code = "SC-001", CardCode = "C0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
            TotalVolume = 1_000m, Status = ContractStatus.Approved,
        });
        _db.Context.Set<Branch>().Add(new Branch { Code = "3", BranchName = "Yokotobi - Pilar", ShortName = "Filial Pilar" });
        var release = new SalesShipmentRelease
        {
            Key = Guid.NewGuid(), SalesContractKey = contractKey,
            DeliveryLocationCode = "C002255", BranchCode = "3",
        };
        _db.Context.SalesShipmentReleases.Add(release);
        await _db.Context.SaveChangesAsync();

        // Limpa o tracker para o fixup do EF não mascarar a ausência do Include.
        _db.Context.ChangeTracker.Clear();

        var service = new SalesShipmentReleasesGetService(_db, NullLogger<SalesShipmentReleasesGetService>.Instance);
        var result = await service.GetByIdAsync(release.Key);

        Assert.NotNull(result);
        Assert.NotNull(result.Branch);
        Assert.Equal("Filial Pilar", result.Branch.ShortName);
    }
}
