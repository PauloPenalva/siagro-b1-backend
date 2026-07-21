using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// Rede de proteção para <see cref="PurchaseContractTax.TotalTax"/>, que é calculado em
/// runtime a partir de <c>PurchaseContract.TotalPrice × Tax.Rate</c> e retorna 0
/// SILENCIOSAMENTE se as navegações não estiverem carregadas.
/// </summary>
public class PurchaseContractTaxIncludeTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<Guid> SeedAsync()
    {
        // Rate é DECIMAL(5,4): máximo 9,9999. 1,63% é a alíquota vigente do Funrural.
        var tax = new Tax { Code = "FUNRURAL", Name = "Funrural", Rate = 1.63m };

        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 100_000m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        _db.Context.Taxes.Add(tax);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = 10_000m,
            FixationPrice = 2m,
            Status = PriceFixationStatus.Confirmed,
        });
        _db.Context.PurchaseContractsTaxes.Add(new PurchaseContractTax
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            TaxCode = "FUNRURAL",
        });

        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        return contract.Key;
    }

    [Fact]
    public async Task TotalTax_WithNestedIncludes_ComputesFromConfirmedFixations()
    {
        var key = await SeedAsync();

        var contractTax = await _db.Context.PurchaseContractsTaxes
            .Include(x => x.Tax)
            .Include(x => x.PurchaseContract)
            .ThenInclude(c => c!.PriceFixations)
            .AsNoTracking()
            .SingleAsync(x => x.PurchaseContractKey == key);

        // TotalPrice = 10.000 × 2 = 20.000; 20.000 / 100 × 1,63 = 326
        Assert.Equal(326m, contractTax.TotalTax);
    }

    [Fact]
    public async Task TotalTax_InApprovalFixationOnly_IsZero()
    {
        // Uma fixação ainda não aprovada não pode gerar imposto:
        // é justamente o vazamento que a Task 2 fechou.
        var tax = new Tax { Code = "FUNRURAL", Name = "Funrural", Rate = 1.63m };
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-002",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 100_000m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        _db.Context.Taxes.Add(tax);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = 10_000m,
            FixationPrice = 2m,
            Status = PriceFixationStatus.InApproval,
        });
        _db.Context.PurchaseContractsTaxes.Add(new PurchaseContractTax
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            TaxCode = "FUNRURAL",
        });
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var contractTax = await _db.Context.PurchaseContractsTaxes
            .Include(x => x.Tax)
            .Include(x => x.PurchaseContract)
            .ThenInclude(c => c!.PriceFixations)
            .AsNoTracking()
            .SingleAsync(x => x.PurchaseContractKey == contract.Key);

        Assert.Equal(0m, contractTax.TotalTax);
    }

    /// <summary>
    /// Replica a query exata de <c>PurchaseContractsTotalsService.GetTotals</c>, que é o
    /// consumidor real de TotalTax exposto ao frontend. Depende do EF popular a navegação
    /// INVERSA (tax.PurchaseContract) sob AsNoTracking — se essa premissa mudar, o imposto
    /// vai a zero na tela sem nenhum erro.
    /// </summary>
    [Fact]
    public async Task TotalTax_ViaTotalsServiceQueryShape_IsPopulated()
    {
        var key = await SeedAsync();

        var ctr = await _db.Context.PurchaseContracts
            .Include(x => x.Taxes)
            .ThenInclude(x => x.Tax)
            .Include(x => x.PriceFixations)
            .AsNoTracking()
            .FirstAsync(x => x.Key == key);

        Assert.Equal(326m, ctr.TotalTax);
    }

    [Fact]
    public async Task TotalTax_WithoutNestedIncludes_SilentlyReturnsZero()
    {
        var key = await SeedAsync();

        var contractTax = await _db.Context.PurchaseContractsTaxes
            .AsNoTracking()
            .SingleAsync(x => x.PurchaseContractKey == key);

        // Documenta a armadilha: sem Include aninhado o imposto some sem erro.
        // Todo consumidor de TotalTax PRECISA incluir Tax + PurchaseContract.PriceFixations.
        Assert.Equal(0m, contractTax.TotalTax);
    }
}
