namespace SiagroB1.Domain.Enums;

/// <summary>
/// Origem de uma linha de alocação de contrato de venda (SALES_CONTRACTS_ALLOCATIONS).
/// </summary>
public enum SalesContractAllocationOrigin
{
    /// <summary>Alocação padrão criada na confirmação do faturamento (item → contrato original).</summary>
    Billing = 0,

    /// <summary>Par −/+ criado por realocação manual entre contratos (amarrado por ReallocationGroupKey).</summary>
    Reallocation = 1,

    /// <summary>Linha negativa criada pela confirmação de uma nota de devolução.</summary>
    Return = 2,

    /// <summary>Linha criada pela migração de backfill a partir das invoices pré-existentes.</summary>
    Backfill = 3,

    /// <summary>
    /// Par −/+ criado por ajuste manual de CONCILIAÇÃO: move volume entre contratos SEM
    /// exigir liberação de entrega no destino, para corrigir a distribuição de contratos
    /// legados (faturados por fora da liberação) cujo saldo ficou negativo. Diferente de
    /// <see cref="Reallocation"/>, permite que o contrato de destino fique com saldo
    /// negativo — é o que destrava a troca cruzada, em que cada movimento dependeria do
    /// outro ter acontecido antes. Exige motivo e é restrita a ADMIN.
    /// </summary>
    Reconciliation = 4,
}
