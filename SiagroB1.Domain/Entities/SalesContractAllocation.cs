using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Ledger de consumo dos contratos de venda por item de invoice — fonte de verdade do
/// volume faturado/realocado/devolvido de cada contrato (espelho de
/// <see cref="PurchaseContractAllocation"/> no lado de compra).
/// Linhas são imutáveis (insert/delete-only): faturamento grava volume positivo,
/// devolução grava negativo e realocação grava um par −/+ amarrado por
/// <see cref="ReallocationGroupKey"/>. Invariante: Σ Volume por item = consumo nominal
/// do item (uma realocação soma zero por item, apenas move volume entre contratos e
/// liberações).
///
/// A quebra de entrega NÃO entra no <see cref="Volume"/>: ela é descontada no recálculo do
/// saldo, inteira, da linha marcada com <see cref="OwnsDeliveryDifference"/> — nos DOIS
/// eixos derivados deste ledger, o contrato e a liberação de entrega.
/// </summary>
[Table("SALES_CONTRACTS_ALLOCATIONS")]
[Index(nameof(SalesContractKey))]
[Index(nameof(SalesInvoiceItemKey))]
[Index(nameof(SalesShipmentReleaseKey))]
[Index(nameof(ReallocationGroupKey))]
[Index(nameof(CounterpartySalesContractKey))]
public class SalesContractAllocation : BaseEntity
{
    public Guid SalesContractKey { get; set; }
    public virtual SalesContract? SalesContract { get; set; }

    public Guid SalesInvoiceItemKey { get; set; }
    public virtual SalesInvoiceItem? SalesInvoiceItem { get; set; }

    /// <summary>
    /// Liberação de entrega consumida por esta linha (null para itens do fluxo legado,
    /// faturados antes das liberações existirem). Na realocação, a linha negativa
    /// aponta para a liberação de origem (devolve saldo) e a positiva para a do destino.
    /// </summary>
    public Guid? SalesShipmentReleaseKey { get; set; }
    public virtual SalesShipmentRelease? SalesShipmentRelease { get; set; }

    /// <summary>
    /// Volume nominal ASSINADO: faturamento +, devolução −, realocação par −/+.
    /// Quebra de entrega não altera a linha — ela é descontada no recálculo do contrato E da
    /// liberação de entrega, e SÓ da linha marcada com <see cref="OwnsDeliveryDifference"/>
    /// (ver SalesContractsRecalculateBalanceService e
    /// SalesShipmentReleasesRecalculateShippedService).
    /// </summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Volume { get; set; }

    /// <summary>
    /// Marca a ÚNICA linha do item que carrega a diferença de entrega INTEIRA. Quando um
    /// item é dividido entre vários contratos, a quebra apurada na conferência não é
    /// rateada: ela sai toda do contrato desta linha, e as demais consomem o volume nominal.
    ///
    /// Nasce na linha do faturamento (a mais antiga do item) e ACOMPANHA O VOLUME quando uma
    /// realocação zera o líquido do contrato dono — a titularidade é gravada, nunca inferida
    /// na leitura. Quem mantém a regra é
    /// <c>SalesContractsDeliveryDifferenceOwnerService</c>, chamado por todo caminho que
    /// mexe no ledger; o estorno se auto-corrige por ela, sem bookkeeping.
    ///
    /// Invariante "um dono por item" travada no banco por índice único filtrado
    /// (<c>WHERE OwnsDeliveryDifference = 1</c>).
    /// </summary>
    public bool OwnsDeliveryDifference { get; set; }

    /// <summary>Snapshot de <see cref="SalesInvoiceItem.UnitPrice"/> no momento da gravação.</summary>
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal InvoiceUnitPrice { get; set; }

    /// <summary>Snapshot de <see cref="SalesContract.Price"/> do contrato DESTA linha no momento da gravação.</summary>
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal ContractPrice { get; set; }

    /// <summary>
    /// Diferença financeira apurada = Round(Volume × (InvoiceUnitPrice − ContractPrice), 2).
    /// Convenção: preço da NF MAIOR que o do contrato → positivo (sobra); menor → negativo
    /// (falta). Somada por contrato, apura a diferença líquida entre o preço faturado e o
    /// preço do contrato onde o volume está alocado — base para NF complementar / desconto
    /// financeiro decididos manualmente pelo usuário.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal PriceDifference { get; set; }

    public SalesContractAllocationOrigin Origin { get; set; }

    /// <summary>Amarra o par −/+ de uma realocação para permitir estorno atômico do grupo.</summary>
    public Guid? ReallocationGroupKey { get; set; }

    /// <summary>
    /// Contrato do OUTRO lado de uma realocação (rastreabilidade): na linha negativa da
    /// origem aponta para o contrato de DESTINO; na positiva do destino aponta para o de
    /// ORIGEM. Null em faturamento/devolução/backfill. Combinado com o sinal do
    /// <see cref="Volume"/> permite ver de onde veio / para onde foi o volume realocado.
    /// Sem propriedade de navegação (evita segunda FK para SALES_CONTRACTS, que tornaria
    /// ambíguo o relacionamento <see cref="SalesContract.Allocations"/>); o código do
    /// contrato é resolvido por JOIN no relatório de entregas por contrato.
    /// </summary>
    public Guid? CounterpartySalesContractKey { get; set; }

    /// <summary>
    /// Justificativa do ajuste manual, gravada nas duas pontas do par para o motivo
    /// aparecer de qualquer lado. Obrigatória quando <see cref="Origin"/> é
    /// <see cref="SalesContractAllocationOrigin.Reconciliation"/> (é o que dá rastro ao
    /// ajuste que fura o saldo do destino); opcional nas demais origens.
    /// </summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? ReconciliationReason { get; set; }
}
