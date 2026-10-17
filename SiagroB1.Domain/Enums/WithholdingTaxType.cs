namespace SiagroB1.Domain.Enums;

/// <summary>Tributo retido na fonte numa compra de serviço (spec 2026-10-17 §2). Os valores são gravados: não reordene.</summary>
public enum WithholdingTaxType
{
    Iss = 0,
    Irrf = 1,
    Inss = 2,
    Pis = 3,
    Cofins = 4,
    Csll = 5,
}
