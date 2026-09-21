namespace SiagroB1.Domain.Enums;

/// <summary>
/// Papel do signatário. Espelha os 13 atos do D4Sign para nada se perder na migração da Tagui,
/// mas os valores são nossos — o código <c>act</c> do provedor é mapeado dentro do provider.
/// </summary>
public enum SignatoryRole
{
    Sign = 1,
    Approve = 2,
    Acknowledge = 3,
    SignAsParty = 4,
    SignAsWitness = 5,
    SignAsIntervening = 6,
    AcknowledgeReceipt = 7,
    SignAsIssuerEndorserGuarantor = 8,
    SignAsIssuerEndorserGuarantorSurety = 9,
    SignAsSurety = 10,
    SignAsPartyAndSurety = 11,
    SignAsJointDebtor = 12,
    SignAsPartyAndJointDebtor = 13,
}
