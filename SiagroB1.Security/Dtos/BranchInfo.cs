using Newtonsoft.Json;

namespace SiagroB1.Security.Dtos;

public class BranchInfo
{
    [JsonProperty(nameof(Code))]
    public string? Code { get; set; }

    [JsonProperty(nameof(BranchName))]
    public string? BranchName { get; set; }
        
    [JsonProperty(nameof(ShortName))]
    public string? ShortName { get; set; }
        
    [JsonProperty(nameof(TaxId))]
    public string? TaxId { get; set; }
}