using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Um contrato (de compra ou de venda) com o entregue e o saldo recalculados do razão no momento
/// da emissão — a mesma conta dos serviços de recálculo de saldo, nunca o AllocatedVolume
/// persistido. É a matéria-prima comum dos dois relatórios de posição.
/// </summary>
public class ContractPosition
{
    public ContractPositionSide Side { get; init; }
    public Guid Key { get; init; }
    public string Code { get; init; } = "";
    public DateTime? CreationDate { get; init; }
    public string CardCode { get; init; } = "";
    public string? CardName { get; init; }
    public string ItemCode { get; init; } = "";
    public string? ItemName { get; init; }
    public string UnitOfMeasureCode { get; init; } = "";
    public string HarvestSeasonCode { get; init; } = "";
    public ContractType Type { get; init; }
    public ContractStatus? Status { get; init; }
    public DateTime? StandardCashFlowDate { get; init; }
    public DateTime DeliveryEndDate { get; init; }
    public decimal Price { get; init; }

    /// <summary>Volume contratado (TotalVolume).</summary>
    public decimal Contracted { get; init; }

    /// <summary>Entregue pelo razão: compra = Σ alocações com sinal; venda = Σ alocações − quebra apurada.</summary>
    public decimal Delivered { get; init; }

    /// <summary>Washout ativo (em aprovação + aprovado). Sempre 0 na venda.</summary>
    public decimal WashedOut { get; init; }

    /// <summary>Saldo = o AvaiableVolume do domínio depois de um recálculo. Pode ser negativo.</summary>
    public decimal Balance { get; init; }

    /// <summary>
    /// Saldo que ainda vai ser entregue: o próprio saldo, ou 0 para contrato Finalizado (encerrar é
    /// abrir mão do não entregue), Cancelado ou Rejeitado. Saldo negativo (entregue além do
    /// contratado) também vira 0: não há o que entregar, e o excesso não abate outros contratos.
    /// </summary>
    public decimal ToDeliver =>
        Status is ContractStatus.Finished or ContractStatus.Canceled or ContractStatus.Rejected
            ? 0m
            : Math.Max(Balance, 0m);
}
