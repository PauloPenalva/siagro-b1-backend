namespace SiagroB1.Domain.Enums;

/// <summary>
/// Documento que originou a notificação. É por este valor que o grupo assina o que quer
/// receber (ver <c>NOTIFICATION_GROUP_SUBSCRIPTIONS</c>).
/// </summary>
public enum NotificationDocumentType
{
    PurchaseContract = 0,
    SalesContract = 1,
}
