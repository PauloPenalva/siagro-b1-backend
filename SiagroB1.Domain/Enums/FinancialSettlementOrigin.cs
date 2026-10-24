namespace SiagroB1.Domain.Enums;

public enum FinancialSettlementOrigin
{
    Manual = 0,
    Reversal = 1,
    InvoiceOffset = 2,
    AdvanceApplication = 3,
    Netting = 4,

    /// <summary>
    /// Devolução do valor adiantado. Origem PRÓPRIA e não Reversal de propósito: o estorno diz
    /// que a baixa não deveria ter existido, a devolução diz que ela existiu e o dinheiro voltou.
    /// A conciliação bancária da Fase 4 precisa da distinção — no estorno não há movimento de
    /// caixa a conciliar, na devolução há.
    /// </summary>
    AdvanceRefund = 5,

    /// <summary>
    /// Dação: o operador baixa a sobra de um firme (NF acima do contrato) sem dinheiro, com motivo. Sem conta.
    /// Estornável pelo estorno genérico.
    /// </summary>
    Dation = 6,

    /// <summary>
    /// Transferência do crédito NÃO aplicado de um adiantamento para um adiantamento NOVO em outro contrato (spec
    /// 2026-10-14, regra 8: revínculo de adiantamento parcialmente aplicado). Grava-se em PAR, sempre com Amount positivo:
    /// a de ENTRADA fica no adiantamento novo (OriginType = FinancialDocument e OriginKey = RelatedDocumentKey da linha)
    /// e conta no PAGO; a de SAÍDA fica no original e conta no APLICADO. Só nasce pelo revínculo.
    /// </summary>
    AdvanceTransfer = 7,

    /// <summary>Baixa dos títulos de CT-e aglutinados na fatura de frete (a fatura passa a ser o título a receber).</summary>
    Aggregation = 8
}
