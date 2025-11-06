using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SiagroB1.Domain.Entities;

public class QualityInspectionConfig : IEntityTypeConfiguration<QualityInspection>
{
    public void Configure(EntityTypeBuilder<QualityInspection> builder)
    {
        builder.HasKey(b => new { b.BranchKey, b.WeighingTicketKey, b.QualityAttribKey });
    }
}