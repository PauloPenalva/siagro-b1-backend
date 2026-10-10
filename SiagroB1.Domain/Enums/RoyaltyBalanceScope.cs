namespace SiagroB1.Domain.Enums;

/// <summary>Abrangência da lista/totais/PDF de royalties ITS, pelo saldo do romaneio.</summary>
public enum RoyaltyBalanceScope
{
    /// <summary>A baixar: saldo &gt; 0 (padrão).</summary>
    Pending = 0,
    /// <summary>Baixados: totalmente baixados (saldo &lt;= 0 com peso baixado).</summary>
    WrittenOff = 1,
    /// <summary>Todos, sem filtro de saldo.</summary>
    All = 2,
}
