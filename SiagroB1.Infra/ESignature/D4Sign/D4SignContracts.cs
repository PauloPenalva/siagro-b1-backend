using System.Text.Json.Serialization;

namespace SiagroB1.Infra.ESignature.D4Sign;

// O D4Sign devolve strings onde caberia número ("statusId":"4", "signed":"1") — por isso tudo é string.

internal record D4SignUploadResponse([property: JsonPropertyName("uuid")] string? Uuid);

internal record D4SignSigner(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("act")] string Act,
    [property: JsonPropertyName("foreign")] string Foreign,
    [property: JsonPropertyName("certificadoicpbr")] string CertificadoIcpBr,
    [property: JsonPropertyName("assinatura_presencial")] string AssinaturaPresencial,
    [property: JsonPropertyName("docauth")] string DocAuth,
    [property: JsonPropertyName("docauthandselfie")] string DocAuthAndSelfie,
    [property: JsonPropertyName("embed_methodauth")] string EmbedMethodAuth,
    [property: JsonPropertyName("upload_allow")] string UploadAllow);

internal record D4SignCreateListRequest([property: JsonPropertyName("signers")] IReadOnlyList<D4SignSigner> Signers);

internal record D4SignWebhookRequest([property: JsonPropertyName("url")] string Url);

internal record D4SignSendToSignerRequest(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("skip_email")] string SkipEmail,
    [property: JsonPropertyName("workflow")] string Workflow,
    [property: JsonPropertyName("tokenAPI")] string TokenApi);

internal record D4SignDocument(
    [property: JsonPropertyName("uuidDoc")] string? UuidDoc,
    [property: JsonPropertyName("statusId")] string? StatusId);

internal record D4SignSignerState(
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("signed")] string? Signed,
    [property: JsonPropertyName("signed_date")] string? SignedDate,
    [property: JsonPropertyName("message")] string? Message);

internal record D4SignListResponse([property: JsonPropertyName("list")] IReadOnlyList<D4SignSignerState>? List);

internal record D4SignDownloadRequest(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("language")] string Language);

internal record D4SignDownloadResponse([property: JsonPropertyName("url")] string? Url);
