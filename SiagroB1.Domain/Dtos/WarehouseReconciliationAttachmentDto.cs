namespace SiagroB1.Domain.Dtos;

public class WarehouseReconciliationAttachmentDto
{
    public Guid? Key { get; set; }
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
}
