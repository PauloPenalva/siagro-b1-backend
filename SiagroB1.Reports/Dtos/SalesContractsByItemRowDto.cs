namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Uma linha do relatório de contratos de VENDA, já achatada e formatada para o
/// template FastReport. Só Quantity e Price são numéricos (são totalizados no .frx);
/// o resto é texto pronto, para o template não declarar colunas de data e quebrar
/// ao renderizar sem dados.
/// </summary>
public class SalesContractsByItemRowDto
{
    public string ItemCode { get; set; } = "";

    public string ItemName { get; set; } = "";

    /// <summary>Cabeçalho do grupo, ex.: "SOJA EM GRÃOS (10001)".</summary>
    public string Product { get; set; } = "";

    public string ContractCode { get; set; } = "";

    /// <summary>Nome da filial, sem o código.</summary>
    public string Branch { get; set; } = "";

    /// <summary>
    /// Nome da região logística, sem o código. Ocupa o lugar do local de entrega do
    /// relatório de compra: na venda os locais são 1:N e não cabem numa célula.
    /// </summary>
    public string LogisticRegion { get; set; } = "";

    public string Customer { get; set; } = "";

    public decimal Quantity { get; set; }

    /// <summary>
    /// Unidade de medida da quantidade, impressa numa coluna própria logo depois dela.
    /// Fica separada de <see cref="Quantity"/> para o total continuar somando número.
    /// </summary>
    public string UnitOfMeasure { get; set; } = "";

    public decimal Price { get; set; }

    /// <summary>Tipo de mercado: "Interno" ou "Exportação".</summary>
    public string Market { get; set; } = "";

    /// <summary>Previsão de pagamento no formato dd/MM/yyyy, ou vazio.</summary>
    public string PaymentForecast { get; set; } = "";

    /// <summary>"CIF", "FOB" ou "Sem frete" — na venda não há custo de frete.</summary>
    public string Freight { get; set; } = "";

    public string Seller { get; set; } = "";
}
