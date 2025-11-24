using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Application.Dtos;

public class WeighingTicketsCompletedDto
{
    [Required(ErrorMessage = "Storage Address is required.")]
    public required Guid StorageAddressKey { get; set; }
}