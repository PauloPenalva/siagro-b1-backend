namespace SiagroB1.Domain.Enums;

/// <summary>
/// Efeito da natureza de operação sobre o SALDO FÍSICO do contrato de venda.
/// Materializado como o SINAL do Volume na linha do ledger SALES_CONTRACTS_ALLOCATIONS —
/// não existe mecanismo novo nem coluna nova no contrato.
/// </summary>
public enum ContractBalanceEffect
{
    /// <summary>Não move saldo: nenhuma linha de volume é gravada (ex.: complemento de preço).</summary>
    None = 0,

    /// <summary>Consome saldo — Volume positivo, como o faturamento de romaneio grava hoje.</summary>
    Consume = 1,

    /// <summary>Devolve saldo — Volume negativo (devolução, ajuste de quebra).</summary>
    Restore = 2,
}
