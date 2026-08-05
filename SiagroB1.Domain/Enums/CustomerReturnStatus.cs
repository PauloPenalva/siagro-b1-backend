namespace SiagroB1.Domain.Enums;

/// <summary>
/// Situação da devolução emitida pelo CLIENTE. Curto de propósito: o documento é de controle e
/// conciliação, não tem fluxo de aprovação nem efeito em saldo — ou está registrado, ou foi
/// cancelado.
/// </summary>
public enum CustomerReturnStatus
{
    Registered = 0,

    Cancelled = 1,
}
