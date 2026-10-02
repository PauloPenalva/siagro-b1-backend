namespace SiagroB1.Domain.Enums;

/// <summary>
/// Indicador da IE do destinatário (<c>indIEDest</c>). Os valores são os da SEFAZ.
/// <see cref="NonTaxpayer"/> também liga <c>ide/indFinal = 1</c> (consumidor final).
/// </summary>
public enum StateRegistrationIndicator
{
    Taxpayer = 1,
    Exempt = 2,
    NonTaxpayer = 9,
}
