namespace SiagroB1.Domain.Enums;

/// <summary>Início da contagem dos dias da condição de pagamento (E4_DDD do Protheus, reduzido).</summary>
public enum PaymentStartRule
{
    /// <summary>A partir da data de emissão.</summary>
    IssueDate = 1,

    /// <summary>"Fora o mês": a partir do 1º dia do mês seguinte à emissão.</summary>
    NextMonth = 2,
}
