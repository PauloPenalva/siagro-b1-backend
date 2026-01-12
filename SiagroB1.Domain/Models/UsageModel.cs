using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Models;

public class UsageModel
{
    [Key]
    public int Code { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }
    
    public string? CfopIncomingInState { get; set; }
    
    public string? CfopIncomingOutState { get; set; }
    
    public string? CfopIncomingImport { get; set; }
    
    public string? CfopOutgoingInState { get; set; }
    
    public string? CfopOutgoingOutState { get; set; }
    
    public string? CfopOutgoingExport { get; set; }
}