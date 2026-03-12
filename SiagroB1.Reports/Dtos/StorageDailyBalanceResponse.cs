namespace SiagroB1.Reports.Dtos;

public class StorageDailyBalanceResponse
{
    public DateTime? Data { get; set; }
    
    public decimal? SaldoAnterior { get; set; }
    
    public decimal? Entradas { get; set; }
    
    public decimal? Saidas { get; set; }
    
    public decimal? Base { get; set; }
    
    public decimal? QuebraTecnica { get; set; }
    
    public decimal? Saldo { get; set; }
    
    public decimal? CustoArmazenagem { get; set; }
}