using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractFiscalComplementLifecycleTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<PurchaseContract> SeedDraftWithComplementAsync()
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
            Status = ContractStatus.Draft, DocNumberKey = Guid.NewGuid(), AgentCode = 1,
        };
        _db.Context.PurchaseContracts.Add(pc);
        _db.Context.PurchaseContractFiscalComplements.Add(new PurchaseContractFiscalComplement { PurchaseContractKey = pc.Key, UsageCode = 7 });
        await _db.Context.SaveChangesAsync();
        // Como em produção: o complemento não está carregado no contexto da requisição.
        _db.Context.ChangeTracker.Clear();
        return pc;
    }

    private PurchaseContractsDeleteService DeleteService() => new(
        _db.Context, new FakeStringLocalizer<Resource>(),
        FinancialDocumentTestServices.Cancel(_db.Context),
        new TestLogger<PurchaseContractsDeleteService>());

    private async Task<Guid> CopyAndReturnNewKey(Guid originalKey)
    {
        var create = new PurchaseContractsCreateService(
            _db.Context, new FakeBusinessPartnerService(), new FakeItemService(),
            new FakeAgentService(), new FakeWarehouseService(), new FakeDocNumberSequenceService(),
            TestNotificationOutbox.For(_db.Context), new FakeStringLocalizer<Resource>(),
            new TestLogger<PurchaseContractsCreateService>());

        await new PurchaseContractsCopyService(_db.Context, create).ExecuteAsync(originalKey, "comercial");

        return (await _db.Context.PurchaseContracts.AsNoTracking()
            .SingleAsync(x => x.Key != originalKey)).Key;
    }

    [Fact]
    public async Task Deleting_the_contract_removes_the_complement()
    {
        var pc = await SeedDraftWithComplementAsync();

        await DeleteService().ExecuteAsync(pc.Key, "comercial");

        Assert.False(await _db.Context.PurchaseContractFiscalComplements.AnyAsync(x => x.PurchaseContractKey == pc.Key));
    }

    [Fact]
    public async Task Copying_the_contract_does_not_copy_the_complement()
    {
        var pc = await SeedDraftWithComplementAsync();

        var copyKey = await CopyAndReturnNewKey(pc.Key);

        Assert.NotEqual(pc.Key, copyKey);
        Assert.False(await _db.Context.PurchaseContractFiscalComplements.AnyAsync(x => x.PurchaseContractKey == copyKey));
    }
}
