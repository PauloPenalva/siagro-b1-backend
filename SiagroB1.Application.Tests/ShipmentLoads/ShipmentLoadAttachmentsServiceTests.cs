using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Anexos da carga: gravação com tipo, listagem sem o binário e a trava de exclusão de anexo
/// ainda referenciado por um ticket.
/// </summary>
public class ShipmentLoadAttachmentsServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadAttachmentsCreateService CreateService() => new(
        _db.Context, NullLogger<ShipmentLoadAttachmentsCreateService>.Instance);

    private ShipmentLoadAttachmentsGetService GetService() => new(_db.Context);

    private ShipmentLoadAttachmentsDeleteService DeleteService() => new(
        _db.Context, NullLogger<ShipmentLoadAttachmentsDeleteService>.Instance);

    private async Task<ShipmentLoad> SeedLoadAsync()
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            Status = ShipmentLoadStatus.Invoiced,
        };

        _db.Context.ShipmentLoads.Add(load);
        await _db.Context.SaveChangesAsync();
        return load;
    }

    private static ShipmentLoadAttachment NewAttachment(
        ShipmentLoadAttachmentType type = ShipmentLoadAttachmentType.DischargeTicket) => new()
    {
        AttachmentType = type,
        Description = "Ticket de descarga 1234",
        FileName = "ticket.pdf",
        ContentType = "application/pdf",
        FileData = [1, 2, 3, 4],
        CreatedAt = DateTime.Now,
        CreatedBy = "paulo",
    };

    [Fact]
    public async Task Save_stores_the_file_with_its_type()
    {
        var load = await SeedLoadAsync();

        var saved = await CreateService().SaveAsync(load.Key, NewAttachment());

        Assert.NotNull(saved.Key);
        Assert.Equal(load.Key, saved.ShipmentLoadKey);
        Assert.Equal(ShipmentLoadAttachmentType.DischargeTicket, saved.AttachmentType);
        Assert.Equal(4, saved.FileData.Length);
    }

    [Fact]
    public async Task Save_refuses_an_unknown_load()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateService().SaveAsync(Guid.NewGuid(), NewAttachment()));
    }

    [Fact]
    public async Task List_projects_without_the_binary()
    {
        var load = await SeedLoadAsync();
        await CreateService().SaveAsync(load.Key, NewAttachment());

        var rows = GetService().ListByLoad(load.Key).ToList();

        var row = Assert.Single(rows);
        Assert.Equal("ticket.pdf", row.FileName);
        Assert.Equal(ShipmentLoadAttachmentType.DischargeTicket, row.AttachmentType);
    }

    [Fact]
    public async Task Delete_removes_a_free_attachment()
    {
        var load = await SeedLoadAsync();
        var saved = await CreateService().SaveAsync(load.Key, NewAttachment());

        await DeleteService().ExecuteAsync(saved.Key!.Value, "paulo");

        Assert.Empty(_db.Context.ShipmentLoadsAttachments);
    }

    [Fact]
    public async Task Delete_refuses_an_attachment_still_linked_to_a_ticket()
    {
        // Sem essa trava a FK estoura com erro 547 e o usuário vê um 500 sem explicação.
        var load = await SeedLoadAsync();
        var saved = await CreateService().SaveAsync(load.Key, NewAttachment());

        _db.Context.ShipmentLoadsDischarges.Add(new ShipmentLoadDischarge
        {
            Key = Guid.NewGuid(),
            ShipmentLoadKey = load.Key,
            TicketNumber = "T-1",
            DischargedQuantity = 100m,
            AttachmentKey = saved.Key,
        });
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<DefaultException>(
            () => DeleteService().ExecuteAsync(saved.Key!.Value, "paulo"));

        Assert.Contains("descarga", error.Message);
        Assert.Single(_db.Context.ShipmentLoadsAttachments);
    }
}
