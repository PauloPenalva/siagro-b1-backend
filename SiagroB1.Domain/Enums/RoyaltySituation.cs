namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação de royalty (tecnologia Bayer/Monsanto) apurada na entrada da soja e lançada no ITS.
/// Declarada e Testada consomem crédito no ITS; Convencional e Armazém Participante só informam.
/// </summary>
public enum RoyaltySituation
{
    Declared = 0,
    Tested = 1,
    Conventional = 2,
    ParticipantWarehouse = 3,
}
