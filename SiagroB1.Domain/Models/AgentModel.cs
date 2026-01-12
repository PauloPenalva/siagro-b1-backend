using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Models;

public class AgentModel
{
    [Key]
    public int Code { get; set; }

    public required string Name { get; set; }

    public string? Inactive { get; set; }
}