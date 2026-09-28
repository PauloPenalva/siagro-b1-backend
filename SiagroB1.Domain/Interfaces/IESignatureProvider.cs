using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Assinatura eletrônica de um documento. Abstrai o provedor (hoje D4Sign) para que a troca não
/// toque nos serviços da minuta, no webhook nem no job de reconciliação.
///
/// NENHUM método lança por falha do provedor — todos devolvem resultado. Quem decide o que fazer
/// é o serviço, olhando <c>Transient</c>: um 4xx de credencial errada não melhora com repetição,
/// um 5xx ou timeout melhora.
/// </summary>
public interface IESignatureProvider
{
    /// <summary>Nome gravado em <c>ContractDraft.Provider</c>. Ex.: "D4Sign".</summary>
    string Name { get; }

    Task<ESignatureSendResult> SendAsync(ESignatureSendRequest request, CancellationToken ct = default);
    Task<ESignatureResult> CancelAsync(string externalDocumentId, CancellationToken ct = default);
    Task<ESignatureDocumentState> GetStateAsync(string externalDocumentId, CancellationToken ct = default);

    /// <summary>PDF assinado, com certificado do provedor. Null quando ainda não há.</summary>
    Task<byte[]?> DownloadSignedAsync(string externalDocumentId, CancellationToken ct = default);
}

/// <param name="Title">Título do documento no cofre do provedor.</param>
/// <param name="FileName">Nome do arquivo enviado, com extensão.</param>
/// <param name="PdfBytes">Conteúdo do PDF renderizado da minuta.</param>
/// <param name="Signers">Signatários na ordem de envio (empresa primeiro, depois parceiro).</param>
/// <param name="WebhookUrl">URL que o provedor chama a cada evento. Vazia ⇒ não registra webhook.</param>
/// <param name="Message">Texto do e-mail de convite.</param>
public record ESignatureSendRequest(
    string Title,
    string FileName,
    byte[] PdfBytes,
    IReadOnlyList<ESignatureSigner> Signers,
    string WebhookUrl,
    string Message);

public record ESignatureSigner(string Name, string Email, SignatoryRole Role, int Order);

/// <param name="Succeeded">Provedor aceitou o documento e o envio.</param>
/// <param name="ExternalDocumentId">Identificador do documento no provedor (uuid do D4Sign).</param>
/// <param name="ErrorMessage">
/// Resumo do erro, truncado em 300 caracteres. NUNCA contém token, chave ou URL com credencial:
/// este texto é gravado em <c>ContractDraft.LastError</c> e exibido na tela.
/// </param>
/// <param name="Transient">Falha possivelmente passageira (timeout, 408, 429, 5xx).</param>
public record ESignatureSendResult(
    bool Succeeded,
    string? ExternalDocumentId,
    string? ErrorMessage,
    bool Transient)
{
    public static ESignatureSendResult Ok(string externalDocumentId) => new(true, externalDocumentId, null, false);
    public static ESignatureSendResult Fail(string message, bool transient) => new(false, null, message, transient);
}

public record ESignatureResult(bool Succeeded, string? ErrorMessage, bool Transient)
{
    public static readonly ESignatureResult Ok = new(true, null, false);
    public static ESignatureResult Fail(string message, bool transient) => new(false, message, transient);
}

/// <summary>Situação do documento no provedor, já traduzida — o serviço não conhece códigos do D4Sign.</summary>
public enum ESignatureDocumentStatus
{
    /// <summary>Não foi possível ler a situação (falha de rede, documento inacessível).</summary>
    Unknown = 0,
    Pending = 1,
    Finished = 2,
    Canceled = 3,
}

/// <param name="Status">Situação do documento.</param>
/// <param name="Signers">Um item por signatário conhecido pelo provedor, correlacionado por e-mail.</param>
/// <param name="ErrorMessage">Preenchido quando <paramref name="Status"/> é <c>Unknown</c>.</param>
public record ESignatureDocumentState(
    ESignatureDocumentStatus Status,
    IReadOnlyList<ESignatureSignerState> Signers,
    string? ErrorMessage = null)
{
    public static ESignatureDocumentState Unknown(string message) =>
        new(ESignatureDocumentStatus.Unknown, [], message);
}

/// <param name="Email">Chave de correlação com <c>CONTRACT_DRAFT_SIGNERS.Email</c>.</param>
/// <param name="Signed">Já assinou.</param>
/// <param name="SignedAt">Quando assinou, se o provedor informar.</param>
/// <param name="Message">Mensagem do provedor sobre este signatário (ex.: falha de e-mail).</param>
public record ESignatureSignerState(string Email, bool Signed, DateTime? SignedAt, string? Message);
