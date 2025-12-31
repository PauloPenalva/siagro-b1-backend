namespace SiagroB1.Reports.Dtos;

public record StorageTransactionsReceiptsDto
{
    public string Filial { get; init; }
    public string Romaneio { get; init; }
    public string Documento { get; init; }
    public int Status { get; init; }
    public string ClienteNome { get; init; }
    public string ClienteCodigo { get; init; }
    public string LoteCodigo { get; init; }
    public string LoteDescricao { get; init; }
    public string ArmazemCodigo { get; init; }
    public string ProdutoNome { get; init; }
    public string Placa { get; init; }
    public DateTime Emissao { get; init; }
    public string Ticket { get; init; }
    public decimal PesoBruto { get; init; }
    public decimal Umidade { get; init; }
    public decimal Impurezas { get; init; }
    public decimal Avariados { get; init; }
    public decimal Ardidos { get; init; }
    public decimal PH { get; init; }
    public decimal FN { get; init; }
    public decimal Descontos { get; init; }
    public decimal PesoLiquido { get; init; }
}