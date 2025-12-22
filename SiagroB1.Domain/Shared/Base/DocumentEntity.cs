using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Shared.Base;

public abstract class DocumentEntity : BaseEntity
{
    public Guid? DocNumberKey { get; set; }
    public virtual DocNumber? DocNumber { get; set; }
}