using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Porta única de escrita do log da carga. Ela apenas ENFILEIRA: quem chama salva, para que o
/// log e a alteração que ele descreve entrem no mesmo SaveChanges.
/// </summary>
public class ShipmentLoadsChangeLogServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsChangeLogService Service() => new(_db.Context);

    [Fact]
    public void Register_only_enqueues_and_does_not_save()
    {
        var loadKey = Guid.NewGuid();

        Service().Register(
            loadKey, ShipmentLoadChangeLogFields.TruckCode, "ABC1D23", "XYZ4E56", "joao");

        // Nada no banco ate o SaveChanges de quem chamou.
        Assert.Empty(_db.Context.ShipmentLoadsChangeLogs.ToList());

        _db.Context.SaveChanges();

        var log = Assert.Single(_db.Context.ShipmentLoadsChangeLogs.ToList());
        Assert.Equal(loadKey, log.ShipmentLoadKey);
        Assert.Equal(ShipmentLoadChangeLogFields.TruckCode, log.Field);
        Assert.Equal("ABC1D23", log.OldValue);
        Assert.Equal("XYZ4E56", log.NewValue);
        Assert.Equal("joao", log.ChangedBy);
    }

    [Fact]
    public void Register_truncates_values_at_the_column_width()
    {
        // As colunas sao VARCHAR(500): um texto longo nao pode derrubar a gravacao da alteracao
        // que o log so acompanha.
        Service().Register(
            Guid.NewGuid(),
            ShipmentLoadChangeLogFields.Comments,
            new string('a', 600),
            new string('b', 600),
            "joao");

        _db.Context.SaveChanges();

        var log = Assert.Single(_db.Context.ShipmentLoadsChangeLogs.ToList());
        Assert.Equal(500, log.OldValue!.Length);
        Assert.Equal(500, log.NewValue!.Length);
    }
}
