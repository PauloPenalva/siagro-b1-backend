using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// As liberações por departamento (GAC-1188) são filhas do contrato. O InMemory não aplica o
/// cascade do banco, então o teste confere que o serviço de exclusão remove as linhas de forma
/// explícita — no SQL Server o cascade apagaria de qualquer jeito, mas o serviço não depende dele.
/// </summary>
public class PurchaseContractPaymentReleaseDeleteTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsDeleteService Delete() =>
        new(_db.Context, new FakeStringLocalizer<Resource>(), FinancialDocumentTestServices.Cancel(_db.Context),
            NullLogger<PurchaseContractsDeleteService>.Instance);

    [Fact]
    public async Task DeletingContract_WithPaymentReleases_RemovesThem()
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TotalVolume = 100_000m,
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            StandardPrice = 2m,
            Type = ContractType.Fixed,
            Status = ContractStatus.Draft,
        };
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPaymentReleases.AddRange(
            new PurchaseContractPaymentRelease
            {
                PurchaseContractKey = contract.Key,
                Department = PaymentReleaseDepartment.Stock,
                Released = true,
            },
            new PurchaseContractPaymentRelease
            {
                PurchaseContractKey = contract.Key,
                Department = PaymentReleaseDepartment.Legal,
            });
        _db.Context.PurchaseContractsPaymentReleaseHistory.Add(new PurchaseContractPaymentReleaseHistory
        {
            PurchaseContractKey = contract.Key,
            Department = PaymentReleaseDepartment.Stock,
            Released = true,
            Reason = "Conferido",
            ChangedAt = DateTime.Now,
            ChangedBy = "maria",
        });
        await _db.Context.SaveChangesAsync();

        var deleted = await Delete().ExecuteAsync(contract.Key, "tester");

        Assert.True(deleted);
        Assert.Empty(await _db.Context.PurchaseContractsPaymentReleases.ToListAsync());
        Assert.Empty(await _db.Context.PurchaseContractsPaymentReleaseHistory.ToListAsync());
    }
}
