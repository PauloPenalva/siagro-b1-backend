using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

public class WarehouseReconciliationAttachmentsServiceTests
{
    private readonly WarehouseReconciliationsTestContext _ctx = new();

    private static WarehouseReconciliationAttachment NewAttachment() => new()
    {
        Description = "Extrato do armazém",
        FileName = "extrato.pdf",
        ContentType = "application/pdf",
        FileData = [1, 2, 3],
        CreatedBy = "tester",
    };

    private WarehouseReconciliationAttachmentsCreateService Create() =>
        new(_ctx.Db, _ctx.Resource);

    private WarehouseReconciliationAttachmentsDeleteService Delete() =>
        new(_ctx.Db, _ctx.Resource);

    [Fact]
    public async Task Save_list_download_and_delete()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Draft, DateTime.Today);
        var create = Create();
        var get = new WarehouseReconciliationAttachmentsGetService(_ctx.Db);
        var delete = Delete();

        await create.SaveAsync(r.Key, NewAttachment());

        var listed = Assert.Single(get.ListByReconciliation(r.Key));
        Assert.Equal("extrato.pdf", listed.FileName);
        var file = await get.GetByKey(listed.Key!.Value);
        Assert.Equal(new byte[] { 1, 2, 3 }, file!.FileData);

        await delete.Delete(listed.Key!.Value);
        Assert.Empty(get.ListByReconciliation(r.Key));
    }

    [Fact]
    public async Task Save_refuses_unknown_reconciliation()
    {
        var create = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => create.SaveAsync(Guid.NewGuid(), NewAttachment()));
    }

    [Fact]
    public async Task Upload_is_refused_once_the_reconciliation_is_no_longer_draft_or_in_approval()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Approved, DateTime.Today);

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Create().SaveAsync(r.Key, NewAttachment()));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ATTACHMENTS_LOCKED", ex.Message);
        Assert.Empty(new WarehouseReconciliationAttachmentsGetService(_ctx.Db).ListByReconciliation(r.Key));
    }

    [Fact]
    public async Task Delete_is_refused_once_the_reconciliation_is_no_longer_draft_or_in_approval()
    {
        var r = await _ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Draft, DateTime.Today);
        var get = new WarehouseReconciliationAttachmentsGetService(_ctx.Db);
        await Create().SaveAsync(r.Key, NewAttachment());
        var attachmentKey = Assert.Single(get.ListByReconciliation(r.Key)).Key!.Value;

        // Simula a decisão chegando DEPOIS do upload: o anexo já existe quando a conferência sai
        // de rascunho/em aprovação, e não pode mais ser removido por aqui.
        var reconciliation = await _ctx.Db.Context.WarehouseReconciliations.SingleAsync(x => x.Key == r.Key);
        reconciliation.Status = WarehouseReconciliationStatus.Approved;
        await _ctx.Db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Delete().Delete(attachmentKey));

        Assert.Equal("WAREHOUSE_RECONCILIATION_ATTACHMENTS_LOCKED", ex.Message);
        Assert.Single(get.ListByReconciliation(r.Key));
    }
}
