using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesContracts;

/// <summary>
/// Edição pontual do contrato de venda depois de aprovado (observação, locais de entrega,
/// anexos) e o log campo a campo que ela produz.
/// </summary>
public class SalesContractsPostApprovalEditTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private sealed class FakeBusinessPartnerService(string? cardName = null) : IBusinessPartnerService
    {
        public Task<BusinessPartnerModel?> GetByIdAsync(string code) => Task.FromResult(
            (BusinessPartnerModel?)new BusinessPartnerModel
            {
                CardCode = code, CardName = cardName ?? $"PARCEIRO {code}",
            });
        public Task<IEnumerable<BusinessPartnerModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<BusinessPartnerModel> CreateAsync(BusinessPartnerModel entity) => throw new NotImplementedException();
        public Task<BusinessPartnerModel?> UpdateAsync(string code, BusinessPartnerModel entity) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
        public IQueryable<BusinessPartnerModel> QueryAll() => throw new NotImplementedException();
        public Task<bool> DeleteAsyncWithTransaction(string code, Func<BusinessPartnerModel, Task>? preDeleteAction = null) => throw new NotImplementedException();
        public Task<Dictionary<string, SupplierInfo>> LoadSuppliersAsync(IReadOnlyCollection<string> cardCodes) => throw new NotImplementedException();
    }

    private SalesContractsChangeLogService ChangeLog() => new(_db.Context);

    private SalesContractsDeliveryLocationsCreateService LocationCreateService(string? cardName = null) => new(
        _db.Context, new FakeBusinessPartnerService(cardName), ChangeLog(),
        NullLogger<SalesContractsDeliveryLocationsCreateService>.Instance);

    private SalesContractsDeliveryLocationsDeleteService LocationDeleteService() => new(
        _db.Context, ChangeLog(),
        NullLogger<SalesContractsDeliveryLocationsDeleteService>.Instance);

    private SalesContractsAttachmentsCreateService AttachmentCreateService() => new(
        _db, ChangeLog(),
        NullLogger<SalesContractsAttachmentsCreateService>.Instance);

    private SalesContractsAttachmentsDeleteService AttachmentDeleteService() => new(
        _db, ChangeLog(),
        NullLogger<SalesContractsAttachmentsDeleteService>.Instance);

    private static SalesContractAttachment NewAttachment(string fileName = "contrato.pdf") => new()
    {
        Description = "Aditivo", FileName = fileName, ContentType = "application/pdf",
        FileData = [1, 2, 3], CreatedAt = new DateTime(2026, 8, 19), CreatedBy = "joao",
    };

    private async Task<SalesContract> SeedContractAsync(
        ContractStatus status = ContractStatus.Approved)
    {
        var sc = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC-001", CardCode = "C0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
            TotalVolume = 1_000_000m, Status = status,
        };
        _db.Context.SalesContracts.Add(sc);
        await _db.Context.SaveChangesAsync();
        return sc;
    }

    private List<SalesContractChangeLog> LogsOf(Guid contractKey) =>
        _db.Context.SalesContractsChangeLogs.Where(l => l.SalesContractKey == contractKey).ToList();

    // ---------- locais de entrega ----------

    [Fact]
    public async Task ValueLongerThanColumn_IsTruncatedInLog()
    {
        // A coluna do log é VARCHAR(500): o log não pode derrubar a alteração que acompanha.
        var sc = await SeedContractAsync();

        await LocationCreateService(new string('x', 800)).ExecuteAsync(
            sc.Key, new SalesContractDeliveryLocation { CardCode = "C000001" }, "maria");

        var log = Assert.Single(LogsOf(sc.Key));
        Assert.Equal(500, log.NewValue!.Length);
    }

    [Fact]
    public async Task DeliveryLocation_AddedOnApprovedContract_IsLoggedAsInclusion()
    {
        var sc = await SeedContractAsync();

        await LocationCreateService().ExecuteAsync(
            sc.Key, new SalesContractDeliveryLocation { CardCode = "C000001" }, "joao");

        var log = Assert.Single(LogsOf(sc.Key));
        Assert.Equal(ContractChangeLogFields.DeliveryLocation, log.Field);
        Assert.Null(log.OldValue);
        Assert.Contains("C000001", log.NewValue);
    }

    [Fact]
    public async Task DeliveryLocation_RemovedOnApprovedContract_IsLoggedAsRemoval()
    {
        var sc = await SeedContractAsync();
        var created = await LocationCreateService().ExecuteAsync(
            sc.Key, new SalesContractDeliveryLocation { CardCode = "C000001" }, "joao");

        await LocationDeleteService().ExecuteAsync(created.Key!.Value, "joao");

        var removal = Assert.Single(LogsOf(sc.Key).Where(l => l.NewValue is null));
        Assert.Equal(ContractChangeLogFields.DeliveryLocation, removal.Field);
        Assert.Contains("C000001", removal.OldValue);
        Assert.Empty(_db.Context.SalesContractsDeliveryLocations);
    }

    [Fact]
    public async Task DeliveryLocation_AddedOnFinishedContract_Throws()
    {
        // Buraco que existia antes da feature: o create de local não olhava o status.
        var sc = await SeedContractAsync(ContractStatus.Finished);

        await Assert.ThrowsAsync<DefaultException>(
            () => LocationCreateService().ExecuteAsync(
                sc.Key, new SalesContractDeliveryLocation { CardCode = "C000001" }, "joao"));

        Assert.Empty(_db.Context.SalesContractsDeliveryLocations);
        Assert.Empty(LogsOf(sc.Key));
    }

    [Fact]
    public async Task DeliveryLocation_RemovedOnFinishedContract_Throws()
    {
        var sc = await SeedContractAsync();
        var created = await LocationCreateService().ExecuteAsync(
            sc.Key, new SalesContractDeliveryLocation { CardCode = "C000001" }, "joao");

        sc.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => LocationDeleteService().ExecuteAsync(created.Key!.Value, "joao"));

        Assert.Single(_db.Context.SalesContractsDeliveryLocations);
    }

    // ---------- anexos ----------

    [Fact]
    public async Task Attachment_AddedOnFinishedContract_IsSavedAndLogged()
    {
        // Anexo é documentação, não movimento: precisa ser aceito em qualquer status,
        // inclusive no contrato encerrado, onde o guard pós-aprovação barrava.
        var sc = await SeedContractAsync(ContractStatus.Finished);

        await AttachmentCreateService().SaveAsync(sc.Key, NewAttachment(), "joao");

        var attachment = Assert.Single(_db.Context.SalesContractAttachments);
        Assert.Equal(sc.Key, attachment.SalesContractKey);

        var log = Assert.Single(LogsOf(sc.Key));
        Assert.Equal(ContractChangeLogFields.Attachment, log.Field);
        Assert.Null(log.OldValue);
        Assert.Equal("contrato.pdf", log.NewValue);
    }

    [Fact]
    public async Task Attachment_RemovedOnFinishedContract_IsDeletedAndLogged()
    {
        var sc = await SeedContractAsync();
        await AttachmentCreateService().SaveAsync(sc.Key, NewAttachment(), "joao");
        var key = _db.Context.SalesContractAttachments.Single().Key!.Value;

        sc.Status = ContractStatus.Finished;
        await _db.Context.SaveChangesAsync();

        await AttachmentDeleteService().Delete(key, "joao");

        Assert.Empty(_db.Context.SalesContractAttachments);
        var removal = Assert.Single(LogsOf(sc.Key).Where(l => l.NewValue is null));
        Assert.Equal(ContractChangeLogFields.Attachment, removal.Field);
        Assert.Equal("contrato.pdf", removal.OldValue);
    }
}
