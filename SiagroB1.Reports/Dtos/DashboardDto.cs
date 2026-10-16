namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Painel da home (spec 2026-10-16-home-dashboard-design): tudo o que a tela mostra numa única
/// chamada. Só STANDALONE — em SAPB1 estoque e títulos moram no SAP e o endpoint responde 404.
/// </summary>
public class DashboardDto
{
    public DateTime GeneratedAt { get; set; }
    public DashboardFinancialDto Financial { get; set; } = new();
    public DashboardOverdueContractsDto OverdueContracts { get; set; } = new();
    public DashboardLoadsInProgressDto LoadsInProgress { get; set; } = new();
    public List<DashboardDayLoadsDto> RecentLoads { get; set; } = [];
    public List<DashboardStockRowDto> Stock { get; set; } = [];
    public List<ContractMonthlyPositionRowDto> MonthlyPosition { get; set; } = [];
    public DashboardContractQueueDto SalesToDeliver { get; set; } = new();
    public DashboardContractQueueDto PurchasesToReceive { get; set; } = new();
}

public class DashboardMoneyDto
{
    /// <summary>Σ (NetAmount − SettledAmount), só em BRL. Títulos em outra moeda contam em Count mas não aqui.</summary>
    public decimal Amount { get; set; }
    public int Count { get; set; }
}

public class DashboardFinancialDto
{
    public DashboardMoneyDto PayablesDueToday { get; set; } = new();
    public DashboardMoneyDto ReceivablesDueToday { get; set; } = new();
    public DashboardMoneyDto PayablesOverdue { get; set; } = new();
    public DashboardMoneyDto ReceivablesOverdue { get; set; } = new();
}

public class DashboardQuantityDto
{
    public string UnitOfMeasure { get; set; } = "";
    public decimal Quantity { get; set; }
}

public class DashboardOverdueSideDto
{
    public int Count { get; set; }
    public List<DashboardQuantityDto> Quantities { get; set; } = [];
}

public class DashboardOverdueContractsDto
{
    public DashboardOverdueSideDto Purchase { get; set; } = new();
    public DashboardOverdueSideDto Sales { get; set; } = new();
}

public class DashboardLoadsInProgressDto
{
    public int Total { get; set; }
    public int Planned { get; set; }
    /// <summary>Open + PartiallyInvoiced.</summary>
    public int Loading { get; set; }
    public int InTransshipment { get; set; }
    /// <summary>Invoiced: faturada e ainda não descarregada.</summary>
    public int InTransit { get; set; }
    public int RefusalPending { get; set; }
}

public class DashboardDayLoadsDto
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public List<DashboardQuantityDto> Quantities { get; set; } = [];
}

public class DashboardStockRowDto
{
    public string ItemCode { get; set; } = "";
    public string? ItemName { get; set; }
    public string UnitOfMeasure { get; set; } = "";
    /// <summary>Nulo quando o item só aparece em contrato numa UM sem estoque.</summary>
    public decimal? Physical { get; set; }
    /// <summary>Embarcado e ainda não faturado: já saiu do físico, a venda ainda o conta.</summary>
    public decimal InLoad { get; set; }
    public decimal PurchaseToDeliver { get; set; }
    public decimal SalesToDeliver { get; set; }
    /// <summary>Physical + InLoad + PurchaseToDeliver − SalesToDeliver; nulo quando Physical é nulo.</summary>
    public decimal? Net { get; set; }
}

public class DashboardContractQueueItemDto
{
    public Guid Key { get; set; }
    public string Code { get; set; } = "";
    public string CardCode { get; set; } = "";
    public string? CardName { get; set; }
    public string ItemCode { get; set; } = "";
    public string? ItemName { get; set; }
    public string HarvestSeasonCode { get; set; } = "";
    public decimal ToDeliver { get; set; }
    public string UnitOfMeasure { get; set; } = "";
    public DateTime? CashFlowDate { get; set; }
    /// <summary>Nulo quando o contrato não tem prazo (sentinela anterior a 1901).</summary>
    public DateTime? DeliveryEndDate { get; set; }
    public bool IsOverdue { get; set; }
}

public class DashboardContractQueueDto
{
    public int Total { get; set; }
    public List<DashboardContractQueueItemDto> Items { get; set; } = [];
}
