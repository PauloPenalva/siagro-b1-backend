using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Mdfe;

/// <summary>Modelo EF do MDF-e: um CT-e só pode estar em um MDF-e ATIVO por vez.</summary>
public class TransportManifestModelTests
{
    [Fact]
    public void Filtered_unique_index_on_active_cte_link_is_configured()
    {
        var db = TestDb.CreateUnitOfWork();
        var entity = db.Context.Model.FindEntityType(typeof(TransportManifestCte))!;

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Count == 1 && i.Properties[0].Name == nameof(TransportManifestCte.TransportDocumentKey));

        Assert.True(index.IsUnique);
        Assert.Equal("[IsActive] = 1", index.GetFilter());
        var fk = entity.GetForeignKeys().Single(f => f.PrincipalEntityType.ClrType == typeof(TransportDocument));
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public void Status_enum_values_are_stable()
    {
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8], Enum.GetValues<MdfeStatus>().Select(x => (int)x));
        Assert.Equal(new[] { "Draft", "Confirmed", "Processing", "Authorized", "Rejected", "Denied", "Cancelled", "Voided", "Closed" },
            Enum.GetNames<MdfeStatus>());
        Assert.Equal(new[] { "Authorized", "Cancellation", "Closing", "DriverInclusion", "Signed" }, Enum.GetNames<MdfeXmlKind>());
        Assert.Equal(4, (int)MdfeXmlKind.Signed);
        Assert.Equal(new[] { "Closing", "Cancellation", "DriverInclusion" }, Enum.GetNames<MdfeEventType>());
        Assert.Equal(2, (int)MdfeEventType.DriverInclusion);
    }

    [Theory]
    [InlineData(MdfeStatus.Draft, true)]
    [InlineData(MdfeStatus.Confirmed, true)]
    [InlineData(MdfeStatus.Processing, true)]
    [InlineData(MdfeStatus.Rejected, true)]
    [InlineData(MdfeStatus.Authorized, true)]
    [InlineData(MdfeStatus.Denied, false)]
    [InlineData(MdfeStatus.Cancelled, false)]
    [InlineData(MdfeStatus.Voided, false)]
    [InlineData(MdfeStatus.Closed, false)]
    public void ApplyStatus_syncs_every_cte_link(MdfeStatus status, bool active)
    {
        var manifest = new TransportManifest { BranchCode = "01", Status = MdfeStatus.Closed };
        for (var i = 0; i < 2; i++)
            manifest.Ctes.Add(new TransportManifestCte
            {
                CteAccessKey = new string('1', 44), UnloadingCityIbge = "3550308", UnloadingCityName = "São Paulo", IsActive = !active,
            });

        manifest.ApplyStatus(status);

        Assert.Equal(status, manifest.Status);
        Assert.All(manifest.Ctes, c => Assert.Equal(active, c.IsActive));
        Assert.Equal(active, MdfeStatuses.Active.Contains(status));
    }

    [Fact]
    public void Theory_covers_every_status() =>
        Assert.Equal(9, Enum.GetValues<MdfeStatus>().Length);

    [Fact]
    public void ApplyStatus_throws_when_ctes_were_not_loaded()
    {
        var manifest = new TransportManifest { BranchCode = "01", CteCount = 1 };

        var ex = Assert.Throws<InvalidOperationException>(() => manifest.ApplyStatus(MdfeStatus.Cancelled));

        Assert.Equal("Carregue os CT-e do MDF-e antes de mudar a situação.", ex.Message);
        Assert.Equal(MdfeStatus.Draft, manifest.Status);
    }
}
