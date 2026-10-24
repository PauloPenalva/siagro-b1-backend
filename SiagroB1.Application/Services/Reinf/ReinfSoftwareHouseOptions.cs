namespace SiagroB1.Application.Services.Reinf;

/// <summary>Software house informada no R-1000 (seção <c>Reinf:SoftwareHouse</c> do appsettings).</summary>
public sealed record ReinfSoftwareHouseOptions
{
    public string? Cnpj { get; init; }
    public string? Name { get; init; }
    public string? Contact { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
}
