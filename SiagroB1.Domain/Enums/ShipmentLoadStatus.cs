namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação da carga montada. Derivada do saldo — nenhum serviço escreve este campo
/// diretamente, exceto o cancelamento; ver <c>ShipmentLoadsRecalculateInvoicedService</c>.
/// </summary>
public enum ShipmentLoadStatus
{
    Open = 0,               // Montada, nada faturado
    PartiallyInvoiced = 1,  // Faturada em parte, ainda com saldo
    Invoiced = 2,           // Totalmente faturada
    Cancelled = 3
}
