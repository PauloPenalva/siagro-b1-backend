using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1171 (melhorias): o status Descarregada e a marca manual que o produz.
/// </summary>
/// <remarks>
/// Os valores numéricos são o contrato com o banco: enum persistido como int, e renumerar
/// reescreveria o significado de toda linha gravada sem nenhuma migration avisar.
/// </remarks>
public class ShipmentLoadDischargedModelTests
{
    [Fact]
    public void Discharged_status_is_persisted_as_8()
    {
        Assert.Equal(8, (int)ShipmentLoadStatus.Discharged);
        Assert.Equal(7, (int)ShipmentLoadStatus.InTransshipment);
        Assert.Equal(6, (int)ShipmentLoadStatus.Completed);
    }

    [Fact]
    public void Discharge_movements_are_appended_at_the_end()
    {
        Assert.Equal(23, (int)ShipmentLoadMovementType.Discharged);
        Assert.Equal(24, (int)ShipmentLoadMovementType.DischargeUndone);
    }

    [Theory]
    [InlineData(ShipmentLoadStatus.Discharged, "Descarregada")]
    [InlineData(ShipmentLoadStatus.InTransshipment, "Em Transbordo")]
    [InlineData(ShipmentLoadStatus.Completed, "Concluída")]
    public void Change_log_describes_the_status_in_portuguese(ShipmentLoadStatus status, string label)
    {
        Assert.Equal(label, ShipmentLoadChangeLogFields.DescribeStatus(status));
    }

    [Fact]
    public async Task A_new_load_is_not_discharged()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
        });
        await db.Context.SaveChangesAsync();

        Assert.False((await db.Context.ShipmentLoads.AsNoTracking().SingleAsync()).IsDischarged);
    }
}
