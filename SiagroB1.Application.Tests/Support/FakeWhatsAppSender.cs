using SiagroB1.Domain.Interfaces.Notifications;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// <see cref="IWhatsAppSender"/> de teste. Registra tudo que foi enviado e permite roteirizar
/// o resultado por telefone, que é como se testa o comportamento do job diante de falha
/// parcial (uns entregam, outros não).
/// </summary>
public sealed class FakeWhatsAppSender : IWhatsAppSender
{
    private readonly Dictionary<string, WhatsAppSendResult> _scripted = [];
    private readonly WhatsAppSendResult _default;

    public List<(string Phone, string Text)> Sent { get; } = [];

    public FakeWhatsAppSender(WhatsAppSendResult? defaultResult = null) =>
        _default = defaultResult ?? WhatsAppSendResult.Ok(200, "MSG-1");

    public FakeWhatsAppSender FailsPermanently(string phone, string error = "número inválido")
    {
        _scripted[phone] = WhatsAppSendResult.Permanent(400, error);
        return this;
    }

    public FakeWhatsAppSender FailsTransiently(string phone, string error = "provedor fora do ar")
    {
        _scripted[phone] = WhatsAppSendResult.Retryable(503, error);
        return this;
    }

    public Task<WhatsAppSendResult> SendTextAsync(string phoneE164, string text, CancellationToken ct = default)
    {
        Sent.Add((phoneE164, text));

        return Task.FromResult(_scripted.TryGetValue(phoneE164, out var scripted) ? scripted : _default);
    }
}
