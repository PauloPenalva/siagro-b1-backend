namespace SiagroB1.Domain.Enums;

/// <summary>Classe de centro de custo / item contábil (CTT_CLASSE / CTD_CLASSE do Protheus).</summary>
public enum AccountingEntityClass
{
    /// <summary>Sintético: só agrupa, não recebe documento nem rateio.</summary>
    Synthetic = 1,

    /// <summary>Analítico: recebe documento e rateio.</summary>
    Analytic = 2,
}
