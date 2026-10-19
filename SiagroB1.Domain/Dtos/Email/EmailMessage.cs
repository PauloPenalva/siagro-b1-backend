namespace SiagroB1.Domain.Dtos.Email;

/// <summary>Configuração SMTP de uma empresa (a senha já vem decifrada; nunca é logada).</summary>
public record SmtpSettings(
    string Host,
    int Port,
    bool UseStartTls,
    string? UserName,
    string? Password,
    string FromAddress,
    string? FromName);

public record EmailAttachment(string FileName, byte[] Content, string ContentType);

public record EmailMessage(
    IReadOnlyList<string> To,
    string Subject,
    string HtmlBody,
    IReadOnlyList<EmailAttachment> Attachments);

public record EmailSendResult(bool Success, string? Error);
