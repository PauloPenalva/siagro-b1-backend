using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Linha do grid de anexos da carga — sem o binário.</summary>
public class ShipmentLoadAttachmentDto
{
    public Guid? Key { get; set; }
    public ShipmentLoadAttachmentType AttachmentType { get; set; }
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
}
