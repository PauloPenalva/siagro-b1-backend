namespace SiagroB1.Domain.Enums;

/// <summary>
/// <para>
/// Situação da carga. Derivada do volume montado e do saldo faturado — o escritor único é
/// <c>ShipmentLoadsRecalculateInvoicedService.ResolveStatus</c>, com duas exceções:
/// <c>Cancelled</c>, que só o cancelamento grava, e <c>Planned</c> na criação, que é apenas o
/// mesmo valor que o recálculo devolveria para uma carga sem volume.
/// </para>
/// <para>
/// GAC-1171 (melhorias): <c>Discharged</c> e o <c>Completed</c> da carga Normal também são
/// derivados. Eles refinam o <c>Invoiced</c> pela marca manual <c>ShipmentLoad.IsDischarged</c> e
/// pela Conferência de Entregas; ver <c>ShipmentLoadsRecalculateInvoicedService.ResolveClosure</c>.
/// </para>
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
    Planned = 4,            // Criada pela Logística, ainda sem romaneio vinculado
    Returned = 5,           // Mercadoria recusada e devolvida a armazém — encerrada, sem saldo
    Completed = 6,          // Remoção: encerrada à mão. Normal: toda a conferência de entrega encerrada (GAC-1171)
    InTransshipment = 7,    // Descarregada em armazém intermediário, aguardando a saída (GAC-1181)
    Discharged = 8          // Faturada e marcada à mão como descarregada no destino (GAC-1171)
}
