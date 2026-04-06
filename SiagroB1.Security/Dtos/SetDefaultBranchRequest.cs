using Newtonsoft.Json;

namespace SiagroB1.Security.Dtos;

public class SetDefaultBranchRequest
{
    [JsonProperty(nameof(BranchCode))]
    public required string BranchCode { get; set; }
}