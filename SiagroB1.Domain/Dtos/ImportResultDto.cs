namespace SiagroB1.Domain.Dtos;

public sealed class ImportResultDto
{
    public string ProcessingCostCode { get; set; } = string.Empty;
    public int ImportedCount { get; set; }
    public List<string> Errors { get; set; } = [];
}