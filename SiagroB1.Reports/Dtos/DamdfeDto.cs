namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Dados do DAMDFE, já formatados para impressão, lidos do <c>mdfeProc</c> autorizado (nunca da entidade):
/// o DAMDFE mostra o que a SEFAZ autorizou. Listas (veículos, condutores, CIOT, vale-pedágio, documentos)
/// vão como texto de várias linhas — leiaute de página única, como o retrato do manual.
/// </summary>
public class DamdfeDto
{
    public string AccessKey { get; set; } = "";
    public string FormattedAccessKey { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string QrCode { get; set; } = "";
    public bool Homologation { get; set; }
    public bool Cancelled { get; set; }
    public bool Closed { get; set; }

    public string IssuerName { get; set; } = "";
    public string IssuerDetails { get; set; } = "";

    public string Modal { get; set; } = "";
    public string Model { get; set; } = "58";
    public string Series { get; set; } = "";
    public string Number { get; set; } = "";
    public string IssuedAt { get; set; } = "";
    public string UfStart { get; set; } = "";
    public string UfEnd { get; set; } = "";
    public string Route { get; set; } = "";
    public string LoadingCities { get; set; } = "";

    public string CteCount { get; set; } = "";
    public string NfeCount { get; set; } = "";
    public string CargoValue { get; set; } = "";
    public string WeightUnit { get; set; } = "";
    public string GrossWeight { get; set; } = "";
    public string Product { get; set; } = "";

    public string Rntrc { get; set; } = "";
    public string Vehicles { get; set; } = "";
    public string Drivers { get; set; } = "";
    public string Ciot { get; set; } = "";
    public string TollVouchers { get; set; } = "";

    public string Documents { get; set; } = "";
    public string Remarks { get; set; } = "";
}
