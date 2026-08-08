using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class TruckScaleCaptureModelTests
{
    /// <summary>
    /// Usa o provider SqlServer só para materializar o modelo relacional; sem conexão. Com o
    /// provider InMemory não há metadado relacional e GetTableName() devolve o nome do tipo.
    /// </summary>
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void UserTruckScales_maps_to_its_table_with_a_unique_index_per_purpose()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(UserTruckScale))!;

        Assert.Equal("USER_TRUCK_SCALES", entity.GetTableName());

        var unique = entity.GetIndexes().Single(i => i.IsUnique);

        Assert.Equal(
            ["Username", "Purpose"],
            unique.Properties.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void UserTruckScales_has_no_navigation_to_users()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(UserTruckScale))!;

        // USERS vive no banco COMMON: uma FK aqui seria entre bancos diferentes.
        Assert.DoesNotContain(entity.GetNavigations(), n => n.Name == "User");
        Assert.Contains(entity.GetNavigations(), n => n.Name == "TruckScale");
    }

    [Fact]
    public void TruckScale_carries_the_connection_and_tare_configuration()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(TruckScale))!;

        foreach (var property in new[]
                 {
                     "IpAddress", "Port", "Protocol", "ValidateTare", "TareToleranceKg", "LogRawFrames"
                 })
        {
            Assert.NotNull(entity.FindProperty(property));
        }
    }

    [Fact]
    public void Truck_tare_is_optional()
    {
        using var context = CreateContext();

        var property = context.Model.FindEntityType(typeof(Truck))!.FindProperty("TareWeight")!;

        // Nulo de propósito: obrigatório travaria a gravação dos caminhões legados sem tara.
        Assert.True(property.IsNullable);
    }

    [Fact]
    public void WeighingTicket_records_the_origin_of_each_weight()
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(typeof(WeighingTicket))!;

        Assert.True(entity.FindProperty("FirstWeighScaleCode")!.IsNullable);
        Assert.True(entity.FindProperty("SecondWeighScaleCode")!.IsNullable);
        Assert.False(entity.FindProperty("FirstWeighCaptured")!.IsNullable);
        Assert.False(entity.FindProperty("SecondWeighCaptured")!.IsNullable);
    }

    [Fact]
    public void Purpose_has_exactly_two_values()
    {
        Assert.Equal(
            [WeighingScalePurpose.Opening, WeighingScalePurpose.Closing],
            Enum.GetValues<WeighingScalePurpose>());
    }
}
