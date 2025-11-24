using System.Text.Json.Serialization;

namespace SiagroB1.Application.Dtos;

public class WeighingTicketsWeighDto
{
    [JsonPropertyName("WeighValue")]
    public int WeighValue { get; set; }
}