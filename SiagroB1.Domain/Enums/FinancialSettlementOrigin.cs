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
    AdvanceRefund = 5
}
