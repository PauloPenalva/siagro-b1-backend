namespace SiagroB1.Reports.Dtos;

/// <summary>Situação de retorno da remessa: Em aberto = A retornar &gt; 0; Totalmente retornada = A retornar ≤ 0.</summary>
public enum StorageRemittanceSituation
{
    All = 0,
    Open = 1,
    Returned = 2,
}

/// <summary>
/// Filtros de "Remessas para Depósito" (período sobre a data da remessa, <c>SalesInvoice.InvoiceDate</c>).
/// Armazém = parceiro da remessa; produto = alguma linha da remessa.
/// </summary>
public class StorageRemittancesByPeriodRequest : LogisticsReportRequest
{
    public string? CardCode { get; set; }

    public StorageRemittanceSituation ReturnSituation { get; set; } = StorageRemittanceSituation.All;

    /// <summary>Acrescenta, abaixo de cada remessa, os retornos e os romaneios de carregamento.</summary>
    public bool Detailed { get; set; }
}
