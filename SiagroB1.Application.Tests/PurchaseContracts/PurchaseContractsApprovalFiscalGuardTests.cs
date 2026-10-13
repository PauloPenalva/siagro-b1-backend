using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractsApprovalFiscalGuardTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ContractNotificationOutboxService Outbox()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:AppBaseUrl"] = "https://siagro.teste" })
            .Build();

        return new ContractNotificationOutboxService(
            _db.Context, new ContractNotificationPayloadBuilder(_db.Context, configuration));
    }

    private FinancialDocumentsGenerateService FinancialDocuments() => new(
        _db.Context, new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" }));

    private PurchaseContractsApprovalService Service(string mode) =>
        new(_db.Context, Outbox(), FinancialDocuments(), TestConfiguration.Erp(mode));

    /// <summary>Contrato a fixar em aprovação: sem fixação confirmada, a aprovação não gera título nem exige vencimento.</summary>
    private async Task<Guid> SeedInApprovalAsync()
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
            Type = ContractType.ToBeDetermined, Status = ContractStatus.InApproval,
        };
        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        return contract.Key;
    }

    [Fact]
    public async Task Standalone_without_complement_is_refused_and_nothing_changes()
    {
        var key = await SeedInApprovalAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service("STANDALONE").ExecuteAsync(key, null, "diretoria"));

        Assert.Equal("Informe a natureza de operação do contrato (Complemento Fiscal) antes de aprovar.", e.Message);
        var status = await _db.Context.PurchaseContracts.AsNoTracking().Where(x => x.Key == key).Select(x => x.Status).SingleAsync();
        Assert.Equal(ContractStatus.InApproval, status);
    }

    [Fact]
    public async Task Standalone_with_empty_complement_is_refused()
    {
        var key = await SeedInApprovalAsync();
        _db.Context.PurchaseContractFiscalComplements.Add(new PurchaseContractFiscalComplement { PurchaseContractKey = key });
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => Service("STANDALONE").ExecuteAsync(key, null, "diretoria"));
    }

    [Fact]
    public async Task Standalone_with_usage_is_approved()
    {
        var key = await SeedInApprovalAsync();
        _db.Context.PurchaseContractFiscalComplements.Add(new PurchaseContractFiscalComplement { PurchaseContractKey = key, UsageCode = 7 });
        await _db.Context.SaveChangesAsync();

        await Service("STANDALONE").ExecuteAsync(key, null, "diretoria");

        var status = await _db.Context.PurchaseContracts.AsNoTracking().Where(x => x.Key == key).Select(x => x.Status).SingleAsync();
        Assert.Equal(ContractStatus.Approved, status);
    }

    [Fact]
    public async Task Sapb1_does_not_require_the_usage()
    {
        var key = await SeedInApprovalAsync();

        await Service("SAPB1").ExecuteAsync(key, null, "diretoria");

        var status = await _db.Context.PurchaseContracts.AsNoTracking().Where(x => x.Key == key).Select(x => x.Status).SingleAsync();
        Assert.Equal(ContractStatus.Approved, status);
    }
}
