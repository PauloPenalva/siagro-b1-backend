using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesShipmentReleases;

public class SalesShipmentReleasesCreateServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    // No escopo de VENDA o local de entrega é um cliente (business partner), não um armazém.
    // Este fake resolve o CardName por CardCode; códigos desconhecidos devolvem null.
    private sealed class FakeBusinessPartnerService(Dictionary<string, string>? names = null) : IBusinessPartnerService
    {
        private readonly Dictionary<string, string> _names = names ?? new();
        public Task<BusinessPartnerModel?> GetByIdAsync(string code) => Task.FromResult(
            _names.TryGetValue(code, out var name)
                ? new BusinessPartnerModel { CardCode = code, CardName = name }
                : null);
        public Task<IEnumerable<BusinessPartnerModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<BusinessPartnerModel> CreateAsync(BusinessPartnerModel entity) => throw new NotImplementedException();
        public Task<BusinessPartnerModel?> UpdateAsync(string code, BusinessPartnerModel entity) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
        public IQueryable<BusinessPartnerModel> QueryAll() => throw new NotImplementedException();
        public Task<bool> DeleteAsyncWithTransaction(string code, Func<BusinessPartnerModel, Task>? preDeleteAction = null) => throw new NotImplementedException();
        public Task<Dictionary<string, SupplierInfo>> LoadSuppliersAsync(IReadOnlyCollection<string> cardCodes) => throw new NotImplementedException();
    }

    private SalesShipmentReleasesCreateService Service(IBusinessPartnerService? businessPartners = null) => new(
        _db, businessPartners ?? new FakeBusinessPartnerService(), NullLogger<SalesShipmentReleasesCreateService>.Instance);

    /// <summary>
    /// A liberação só sai para um dos locais de entrega do contrato, então todo contrato
    /// de teste nasce com os códigos usados pelos cenários (<see cref="NewRelease"/> e o
    /// teste de resolução de nome). Passe um array vazio em
    /// <paramref name="deliveryLocations"/> para exercitar o contrato sem locais cadastrados.
    /// </summary>
    private async Task<SalesContract> SeedContractAsync(
        decimal totalVolume,
        decimal invoicedQuantity,
        string[]? deliveryLocations = null)
    {
        var sc = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC-001", CardCode = "C0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
            TotalVolume = totalVolume, Status = ContractStatus.Approved,
        };
        _db.Context.SalesContracts.Add(sc);

        foreach (var cardCode in deliveryLocations ?? ["01", "C002255"])
        {
            _db.Context.SalesContractsDeliveryLocations.Add(new SalesContractDeliveryLocation
            {
                Key = Guid.NewGuid(), SalesContractKey = sc.Key, CardCode = cardCode,
                CardName = $"LOCAL {cardCode}",
            });
        }

        if (invoicedQuantity > 0)
        {
            var inv = new SalesInvoice
            {
                Key = Guid.NewGuid(), CardCode = "C0001",
                InvoiceStatus = InvoiceStatus.Confirmed, InvoiceType = SalesInvoiceType.Normal,
            };
            _db.Context.SalesInvoices.Add(inv);
            var item = new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), SalesInvoiceKey = inv.Key, SalesContractKey = sc.Key,
                ItemCode = "SOJA", UnitOfMeasureCode = "KG",
                Quantity = invoicedQuantity, DeliveryStatus = SalesInvoiceDeliveryStatus.Open,
            };
            _db.Context.SalesInvoicesItems.Add(item);

            // O consumo do contrato agora vem do ledger de alocações (persistido no
            // AllocatedVolume pelo faturamento).
            _db.Context.SalesContractsAllocations.Add(new SalesContractAllocation
            {
                Key = Guid.NewGuid(), SalesContractKey = sc.Key,
                SalesInvoiceItemKey = item.Key!.Value, Volume = invoicedQuantity,
                Origin = SalesContractAllocationOrigin.Billing,
            });
            sc.AllocatedVolume = invoicedQuantity;
        }

        await _db.Context.SaveChangesAsync();
        return sc;
    }

    private SalesShipmentRelease NewRelease(Guid contractKey, decimal released) => new()
    {
        SalesContractKey = contractKey, DeliveryLocationCode = "01", ReleasedQuantity = released,
    };

    [Fact]
    public async Task Create_FullyInvoicedContract_Throws()
    {
        // 600k de contrato, 600k já faturado → saldo físico 0.
        var sc = await SeedContractAsync(totalVolume: 600_000m, invoicedQuantity: 600_000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(NewRelease(sc.Key, 100_000m), "tester"));

        Assert.Contains("físico", ex.Message);
        Assert.Empty(_db.Context.SalesShipmentReleases);
    }

    [Fact]
    public async Task Create_WithinPhysicalBalance_Succeeds()
    {
        // 1000k de contrato, 600k faturado → saldo físico 400k.
        var sc = await SeedContractAsync(totalVolume: 1_000_000m, invoicedQuantity: 600_000m);

        var created = await Service().ExecuteAsync(NewRelease(sc.Key, 300_000m), "tester");

        Assert.Equal(ReleaseStatus.Pending, created.Status);
        Assert.Single(_db.Context.SalesShipmentReleases);
    }

    [Fact]
    public async Task Create_ResolvesDeliveryLocationName_FromBusinessPartner()
    {
        // No escopo de venda, o local de entrega é um cliente: o nome deve vir do
        // cadastro de parceiros (CardName), não do armazém.
        var sc = await SeedContractAsync(totalVolume: 1_000_000m, invoicedQuantity: 0m);
        var businessPartners = new FakeBusinessPartnerService(new()
        {
            ["C002255"] = "4R SILVICULTURA LTDA - EPP",
        });
        var release = new SalesShipmentRelease
        {
            SalesContractKey = sc.Key, DeliveryLocationCode = "C002255", ReleasedQuantity = 100_000m,
        };

        var created = await Service(businessPartners).ExecuteAsync(release, "tester");

        Assert.Equal("4R SILVICULTURA LTDA - EPP", created.DeliveryLocationName);
    }

    [Fact]
    public async Task Create_ExceedingPhysicalBalance_Throws()
    {
        var sc = await SeedContractAsync(totalVolume: 1_000_000m, invoicedQuantity: 600_000m);

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(NewRelease(sc.Key, 500_000m), "tester"));
    }

    [Fact]
    public async Task Create_SecondReleaseConsumesRemainingPhysical_Throws()
    {
        // físico 400k; 1ª liberação de 300k reserva; a 2ª de 200k não cabe (resta 100k).
        var sc = await SeedContractAsync(totalVolume: 1_000_000m, invoicedQuantity: 600_000m);
        await Service().ExecuteAsync(NewRelease(sc.Key, 300_000m), "tester");

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(NewRelease(sc.Key, 200_000m), "tester"));

        // a de 100k ainda cabe
        var ok = await Service().ExecuteAsync(NewRelease(sc.Key, 100_000m), "tester");
        Assert.Equal(ReleaseStatus.Pending, ok.Status);
    }

    [Fact]
    public async Task Create_ContractWithoutDeliveryLocations_Throws()
    {
        // Sem locais cadastrados não há para onde liberar: o usuário precisa informá-los
        // no contrato antes de solicitar a liberação.
        var sc = await SeedContractAsync(totalVolume: 1_000_000m, invoicedQuantity: 0m, deliveryLocations: []);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(NewRelease(sc.Key, 100_000m), "tester"));

        Assert.Contains("local de entrega", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_db.Context.SalesShipmentReleases);
    }

    [Fact]
    public async Task Create_DeliveryLocationNotInContract_Throws()
    {
        // O local precisa ser um dos do contrato — não qualquer cliente da base.
        var sc = await SeedContractAsync(totalVolume: 1_000_000m, invoicedQuantity: 0m, deliveryLocations: ["01"]);
        var release = new SalesShipmentRelease
        {
            SalesContractKey = sc.Key, DeliveryLocationCode = "C009999", ReleasedQuantity = 100_000m,
        };

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(release, "tester"));

        Assert.Contains("C009999", ex.Message);
        Assert.Empty(_db.Context.SalesShipmentReleases);
    }

    [Fact]
    public async Task Create_FinishedContract_Throws()
    {
        var sc = await SeedContractAsync(totalVolume: 1_000_000m, invoicedQuantity: 0m);
        sc.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(NewRelease(sc.Key, 100_000m), "tester"));
    }
}
