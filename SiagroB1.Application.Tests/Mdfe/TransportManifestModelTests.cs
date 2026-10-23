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
}
