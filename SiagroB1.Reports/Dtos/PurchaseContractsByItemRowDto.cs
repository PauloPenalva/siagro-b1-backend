namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Uma linha do relatório, já achatada e formatada para o template FastReport.
/// Só Quantity e Price são numéricos (são totalizados no .frx); o resto é texto
/// pronto, para o template não declarar colunas de data e quebrar ao renderizar
/// sem dados.
/// </summary>
public class PurchaseContractsByItemRowDto
{
    public string ItemCode { get; set; } = "";

    public string ItemName { get; set; } = "";

    /// <summary>Cabeçalho do grupo, ex.: "SOJA EM GRÃOS (10001)".</summary>
    public string Product { get; set; } = "";

    public string ContractCode { get; set; } = "";

    /// <summary>Nome da filial, sem o código. Ex.: "MATRIZ".</summary>
    public string Branch { get; set; } = "";

    /// <summary>Nome do local de entrega, sem o código. Ex.: "SILO 1".</summary>
    public string DeliveryLocation { get; set; } = "";

    public string Supplier { get; set; } = "";

    public decimal Quantity { get; set; }

    /// <summary>
    /// Unidade de medida da quantidade, impressa numa coluna própria logo depois dela.
    /// Fica separada de <see cref="Quantity"/> para o total continuar somando número.
    /// </summary>
    public string UnitOfMeasure { get; set; } = "";

    public decimal Price { get; set; }

    public string Funrural { get; set; } = "";

    /// <summary>Previsão de pagamento no formato dd/MM/yyyy, ou vazio.</summary>
    public string PaymentForecast { get; set; } = "";

    /// <summary>Corretores concatenados, ex.: "João Silva - 2,00 TN; Maria Souza - 1,50 TN".</summary>
    public string Commission { get; set; } = "";

    /// <summary>Ex.: "CIF - 45,00" ou "Sem frete".</summary>
    public string Freight { get; set; } = "";

    public string Buyer { get; set; } = "";
}
