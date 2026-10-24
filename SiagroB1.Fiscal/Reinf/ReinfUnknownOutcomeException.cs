namespace SiagroB1.Fiscal.Reinf;

/// <summary>
/// O pedido saiu, mas não há retorno confiável (tempo esgotado ou resposta 2xx ilegível):
/// a Receita pode ter recebido o lote, então não reenviar automaticamente.
/// </summary>
public sealed class ReinfUnknownOutcomeException(string message, Exception? inner = null) : Exception(message, inner);
