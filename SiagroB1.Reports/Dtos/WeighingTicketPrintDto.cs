namespace SiagroB1.Reports.Dtos;

public class WeighingTicketPrintDto
{
    public string? CompanyName { get; set; }
    public string? Title { get; set; } = "TICKET DE PESAGEM";

    public string? Code { get; set; }
    public DateTime? Date { get; set; }

    public string? Type { get; set; }
    public string? Status { get; set; }
    public string? Stage { get; set; }

    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }

    public string? CardCode { get; set; }
    public string? CardName { get; set; }

    public string? TruckCode { get; set; }
    public string? TruckPlate { get; set; }

    public string? TruckDriverCode { get; set; }
    public string? TruckDriverName { get; set; }

    public int FirstWeighValue { get; set; }
    public DateTimeOffset? FirstWeighDateTime { get; set; }

    public int SecondWeighValue { get; set; }
    public DateTimeOffset? SecondWeighDateTime { get; set; }

    public int GrossWeight { get; set; }

    public string? StorageAddressCode { get; set; }
    public string? ProcessingCostCode { get; set; }

    public string? Comments { get; set; }
    
    public string? FirstWeighUsername {  get; set; }
    
    public string? SecondWeighUsername { get; set; }

    public List<WeighingTicketQualityPrintDto> QualityInspections { get; set; } = [];
}