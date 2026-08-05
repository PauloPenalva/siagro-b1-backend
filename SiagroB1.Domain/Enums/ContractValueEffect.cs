namespace SiagroB1.Domain.Enums;

/// <summary>
/// Efeito da natureza de operação sobre o VALOR apurado do contrato de venda.
/// Materializado na coluna PriceDifference do ledger SALES_CONTRACTS_ALLOCATIONS, que já
/// apura por contrato a diferença entre o preço faturado e o preço do contrato — o
/// documento avulso é o que finalmente a LIQUIDA.
/// </summary>
public enum ContractValueEffect
{
    /// <summary>Não mexe no valor apurado.</summary>
    None = 0,

    /// <summary>Soma ao apurado — PriceDifference positivo (complemento de preço).</summary>
    Add = 1,

    /// <summary>Subtrai do apurado — PriceDifference negativo (devolução, quebra).</summary>
    Subtract = 2,
}
