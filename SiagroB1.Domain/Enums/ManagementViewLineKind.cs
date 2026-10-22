namespace SiagroB1.Domain.Enums;

/// <summary>Tipo da linha da visão gerencial (CTS_CLASSE / CTS_FORMUL).</summary>
public enum ManagementViewLineKind
{
    /// <summary>Soma das linhas filhas.</summary>
    Total = 1,

    /// <summary>Saldo das contas analíticas da faixa.</summary>
    Accounts = 2,

    /// <summary>Soma/subtração de outras linhas da visão.</summary>
    Formula = 3,
}
