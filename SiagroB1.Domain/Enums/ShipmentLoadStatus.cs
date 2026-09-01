namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação da carga. Derivada do volume montado e do saldo faturado — o escritor único é
/// <c>ShipmentLoadsRecalculateInvoicedService.ResolveStatus</c>, com duas exceções:
/// <c>Cancelled</c>, que só o cancelamento grava, e <c>Planned</c> na criação, que é apenas o
/// mesmo valor que o recálculo devolveria para uma carga sem volume.
/// </summary>
/// <remarks>
/// ⚠️ Persistido como <c>int</c>. Valor novo entra SEMPRE no fim da numeração — por isso
/// <see cref="Planned"/> é 4 e não 0, apesar de ser o primeiro estado do ciclo de vida.
/// Renumerar reescreveria o significado de toda linha já gravada, e nada avisaria: mudança de
/// enum não gera migration.
/// </remarks>
public enum ShipmentLoadStatus
{
    Open = 0,               // Tem romaneio vinculado, nada faturado
    PartiallyInvoiced = 1,  // Faturada em parte, ainda com saldo
    Invoiced = 2,           // Totalmente faturada
    Cancelled = 3,
    Planned = 4             // Criada pela Logística, ainda sem romaneio vinculado
}
