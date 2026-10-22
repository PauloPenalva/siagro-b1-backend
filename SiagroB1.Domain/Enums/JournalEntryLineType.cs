namespace SiagroB1.Domain.Enums;

/// <summary>Tipo da linha (CT2_DC): débito, crédito, partida dobrada ou continuação de histórico.</summary>
public enum JournalEntryLineType
{
    Debit = 1,
    Credit = 2,
    Double = 3,
    HistoryContinuation = 4
}
