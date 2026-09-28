using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Provedor de assinatura roteirizável. Começa devolvendo sucesso com um uuid fixo e documento
/// pendente sem signatários — cada teste ajusta só o que lhe interessa.
/// </summary>
public sealed class FakeESignatureProvider : IESignatureProvider
{
    private ESignatureSendResult _send = ESignatureSendResult.Ok("uuid-fake");
    private ESignatureResult _cancel = ESignatureResult.Ok;
    private ESignatureDocumentState _state = new(ESignatureDocumentStatus.Pending, []);
    private byte[]? _signedPdf = [0x25, 0x50, 0x44, 0x46];
    private Func<Task>? _onDownload;

    public string Name => "D4SignFake";

    public ESignatureSendRequest? LastSendRequest { get; private set; }
    public string? LastExternalDocumentId { get; private set; }
    public int SendCalls { get; private set; }
    public int CancelCalls { get; private set; }
    public int StateCalls { get; private set; }
    public int DownloadCalls { get; private set; }

    public FakeESignatureProvider SucceedsWith(string uuid) { _send = ESignatureSendResult.Ok(uuid); return this; }
    public FakeESignatureProvider FailsTransiently(string message = "provedor fora do ar") { _send = ESignatureSendResult.Fail(message, true); return this; }
    public FakeESignatureProvider FailsPermanently(string message = "credencial inválida") { _send = ESignatureSendResult.Fail(message, false); return this; }
    public FakeESignatureProvider CancelFails(string message = "não foi possível cancelar") { _cancel = ESignatureResult.Fail(message, false); return this; }
    public FakeESignatureProvider StateIs(ESignatureDocumentState state) { _state = state; return this; }
    public FakeESignatureProvider SignedPdfIs(byte[]? pdf) { _signedPdf = pdf; return this; }

    /// <summary>
    /// Roda antes de devolver o PDF em <see cref="DownloadSignedAsync"/> — o único ponto de
    /// <c>await</c> entre a leitura do estado atual e a abertura da transação em
    /// <c>ContractDraftsApplyProviderStateService</c>. Usado para simular, nos testes, uma escrita
    /// concorrente que "vence a corrida" enquanto este download está em voo.
    /// </summary>
    public FakeESignatureProvider OnDownload(Func<Task> callback) { _onDownload = callback; return this; }

    public Task<ESignatureSendResult> SendAsync(ESignatureSendRequest request, CancellationToken ct = default)
    {
        SendCalls++;
        LastSendRequest = request;
        return Task.FromResult(_send);
    }

    public Task<ESignatureResult> CancelAsync(string externalDocumentId, CancellationToken ct = default)
    {
        CancelCalls++;
        LastExternalDocumentId = externalDocumentId;
        return Task.FromResult(_cancel);
    }

    public Task<ESignatureDocumentState> GetStateAsync(string externalDocumentId, CancellationToken ct = default)
    {
        StateCalls++;
        LastExternalDocumentId = externalDocumentId;
        return Task.FromResult(_state);
    }

    public async Task<byte[]?> DownloadSignedAsync(string externalDocumentId, CancellationToken ct = default)
    {
        DownloadCalls++;
        LastExternalDocumentId = externalDocumentId;
        if (_onDownload is not null)
            await _onDownload();
        return _signedPdf;
    }
}
