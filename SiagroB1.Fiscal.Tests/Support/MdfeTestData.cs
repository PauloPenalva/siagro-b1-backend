using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Cte;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Support;

/// <summary>
/// Viagem interna em SP: carrega em Itaberá, descarrega em São Paulo e Campinas, 2 CT-e, tração +
/// 1 reboque, 1 condutor, peso em KG.
/// </summary>
public static class MdfeTestData
{
    public static readonly DateTimeOffset IssuedAt = new(2026, 10, 23, 10, 0, 0, TimeSpan.FromHours(-3));

    public static readonly string CteKey1 = CteAccessKey.Build("35", new DateTime(2026, 10, 22), "12345678000195", 231, 1, 1, "11111111");
    public static readonly string CteKey2 = CteAccessKey.Build("35", new DateTime(2026, 10, 22), "12345678000195", 231, 2, 1, "22222222");

    public static MdfeIssueInput Input() => new()
    {
        Issuer = new MdfeIssuer
        {
            TaxId = "12345678000195", StateRegistration = "387000000110", LegalName = "CEAGUI CEREAIS LTDA",
            Rntrc = "12345678", Address = NfeTestData.Itabera(),
        },
        Environment = NfeEnvironment.Homologation,
        Series = 231,
        Number = 1,
        RandomCode = "12345678",
        IssuedAt = IssuedAt,
        UfStart = "SP",
        UfEnd = "SP",
        LoadingCities = [new MdfeLoadingCity("3521705", "Itaberá")],
        UnloadingCities =
        [
            new MdfeUnloadingCity("3550308", "São Paulo", [CteKey1]),
            new MdfeUnloadingCity("3509502", "Campinas", [CteKey2]),
        ],
        Traction = new MdfeVehicle("ABC1D23", "12345678901", 8000, 30000, 60, "03", "03", "SP"),
        Trailers = [new MdfeVehicle("XYZ9A87", null, 7000, 35000, 70, "03", "06", "SP")],
        Drivers = [new MdfeDriver("12345678909", "JOAO MOTORISTA")],
        Product = new MdfePredominantProduct("01", "SOJA EM GRAOS", "12019000", "18440000", "01001000"),
        Totals = new MdfeTotals(2, 120000m, "01", 60000m),
        InfRespTec = new NfeTechnicalResponsible("09123456000100", "IDX Consultoria", "fiscal@idx.com.br", "1533334444"),
    };

    public static MdfeIssueInput SingleCte() => Input() with
    {
        UnloadingCities = [new MdfeUnloadingCity("3550308", "São Paulo", [CteKey1])],
        Totals = new MdfeTotals(1, 60000m, "01", 30000m),
    };
}
