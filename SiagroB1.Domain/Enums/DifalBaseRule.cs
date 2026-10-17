namespace SiagroB1.Domain.Enums;

/// <summary>Regra da base de cálculo do DIFAL, configurada por UF.</summary>
public enum DifalBaseRule
{
    /// <summary>Base única: a base é o valor da operação.</summary>
    Single = 0,

    /// <summary>Base dupla: a base é "por dentro" (valor menos o ICMS de origem, dividido por 1 - alíquotas).</summary>
    Double = 1,
}
