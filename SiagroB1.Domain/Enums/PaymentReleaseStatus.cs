namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação da liberação de pagamento do contrato de compra (GAC-1188). Nulo é valor legítimo
/// e equivale a <see cref="Pending"/> na tela: contratos anteriores à feature nascem nulos.
/// </summary>
public enum PaymentReleaseStatus
{
    Pending = 0,
    UnderReview = 1,
    IncompleteDocuments = 2,
    Approved = 3,
    PreApproved = 4,
}
