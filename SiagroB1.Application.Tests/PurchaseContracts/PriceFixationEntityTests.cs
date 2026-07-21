using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PriceFixationEntityTests
{
    [Fact]
    public void PriceFixation_InheritsBaseEntity_ExposingAuditFields()
    {
        var fixation = new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            ApprovedBy = "diretoria",
            ApprovedAt = new DateTime(2026, 7, 20),
            ApprovalComments = "ok",
        };

        Assert.IsAssignableFrom<BaseEntity>(fixation);
        Assert.Equal("diretoria", fixation.ApprovedBy);
        Assert.Equal("ok", fixation.ApprovalComments);
    }

    [Fact]
    public void PriceFixationStatus_HasRejected()
    {
        Assert.Equal(3, (int) PriceFixationStatus.Rejected);
    }
}
