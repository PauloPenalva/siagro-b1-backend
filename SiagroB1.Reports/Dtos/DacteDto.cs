namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Dados do DACTE, já formatados para impressão, lidos do <c>cteProc</c> autorizado (nunca da entidade):
/// o DACTE mostra o que a SEFAZ autorizou. Listas (componentes, quantidades, chaves, observações)
/// vão como texto de várias linhas — o layout é de página única, como o retrato do manual.
/// </summary>
public class DacteDto
{
    public string AccessKey { get; set; } = "";
    public string FormattedAccessKey { get; set; } = "";
    public string Protocol { get; set; } = "";
    public string QrCode { get; set; } = "";
    public bool Homologation { get; set; }
    public bool Cancelled { get; set; }

    public string IssuerName { get; set; } = "";
    public string IssuerDetails { get; set; } = "";

    public string Model { get; set; } = "57";
    public string Series { get; set; } = "";
    public string Number { get; set; } = "";
    public string IssuedAt { get; set; } = "";
    public string Modal { get; set; } = "";
    public string CteType { get; set; } = "";
    public string ServiceType { get; set; } = "";
    public string Taker { get; set; } = "";
    public string Cfop { get; set; } = "";
    public string OperationNature { get; set; } = "";
    public string Start { get; set; } = "";
    public string End { get; set; } = "";

    public string Sender { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string Shipper { get; set; } = "";
    public string Receiver { get; set; } = "";
    public string TakerParty { get; set; } = "";

    public string PredominantProduct { get; set; } = "";
    public string OtherCharacteristics { get; set; } = "";
    public string CargoValue { get; set; } = "";
    public string Quantities { get; set; } = "";

    public string Components { get; set; } = "";
    public string ServiceTotal { get; set; } = "";
    public string AmountToReceive { get; set; } = "";

    public string IcmsSituation { get; set; } = "";
    public string IcmsBase { get; set; } = "";
    public string IcmsRate { get; set; } = "";
    public string IcmsValue { get; set; } = "";
    public string IcmsReduction { get; set; } = "";

    public string Documents { get; set; } = "";
    public string Remarks { get; set; } = "";
    public string Rntrc { get; set; } = "";
}
