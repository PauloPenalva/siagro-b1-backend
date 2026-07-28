namespace SiagroB1.Domain.Interfaces.Notifications;

/// <summary>
/// Envio de uma mensagem de texto por WhatsApp. Abstrai o provedor (hoje PlugZapi) para que a
/// troca não toque no job, no log nem nas telas.
/// </summary>
public interface IWhatsAppSender
{
    /// <summary>
    /// Envia <paramref name="text"/> para <paramref name="phoneE164"/>.
    ///
    /// NUNCA lança por falha do provedor — devolve o resultado. Quem decide retentar é o job,
    /// olhando <see cref="WhatsAppSendResult.Transient"/>; deixar a exceção subir daqui
    /// entregaria essa decisão ao Hangfire, que não sabe distinguir "instância desconectada"
    /// (inútil retentar) de "provedor fora do ar" (vale retentar).
    /// </summary>
    Task<WhatsAppSendResult> SendTextAsync(string phoneE164, string text, CancellationToken ct = default);
}

/// <param name="Success">Provedor aceitou a mensagem.</param>
/// <param name="HttpStatusCode">Status HTTP devolvido, quando houve resposta.</param>
/// <param name="ProviderMessageId">Identificador da mensagem no provedor, para conferência no painel dele.</param>
/// <param name="Error">
/// Resumo do erro. NUNCA contém a URL da requisição: o token da instância vai no path e este
/// texto é gravado no log, que a tela de administração exibe.
/// </param>
/// <param name="Transient">
/// Falha possivelmente passageira (timeout, 429, 5xx). Só neste caso vale retentar — um 4xx de
/// número inválido ou instância desconectada não melhora com repetição.
/// </param>
public record WhatsAppSendResult(
    bool Success,
    int? HttpStatusCode,
    string? ProviderMessageId,
    string? Error,
    bool Transient)
{
    public static WhatsAppSendResult Ok(int statusCode, string? providerMessageId) =>
        new(true, statusCode, providerMessageId, null, false);

    public static WhatsAppSendResult Permanent(int? statusCode, string error) =>
        new(false, statusCode, null, error, false);

    public static WhatsAppSendResult Retryable(int? statusCode, string error) =>
        new(false, statusCode, null, error, true);
}
