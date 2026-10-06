using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Carta de Correção" (spec 2026-10-06 §7.1), comum aos documentos: valida, envia o evento 110110 no ambiente da
/// EMISSÃO com a próxima sequência e grava a carta registrada. Recusa e falta de resposta não gravam nada. A CC-e
/// não muda nenhum campo do documento.
/// </summary>
public abstract class NfeCorrectionServiceBase<TDocument>(
    IUnitOfWork db,
    INfeDocumentStore<TDocument> store,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeNumberReservationService reservation,
    ILogger logger)
    where TDocument : class, INfeDocument
{
    public const int MaxCorrections = 20;

    public const string NoAnswerMessage =
        "Sem resposta da SEFAZ na carta de correção: use Consultar situação antes de tentar de novo.";

    public const string NotSavedMessage =
        "A SEFAZ pode ter registrado a carta de correção, mas ela não foi gravada: use Consultar situação.";

    public async Task<NfeCorrectionOutcomeDto> ExecuteAsync(Guid key, string? text, string userName)
    {
        var correction = NfeCorrectionText.Normalize(text);
        if (correction.Length is < NfeCorrectionText.MinLength or > NfeCorrectionText.MaxLength)
            throw new DefaultException("O texto da correção deve ter entre 15 e 1000 caracteres.");

        var invalid = NfeCorrectionText.InvalidCharacters(correction);
        if (invalid.Count > 0)
            throw new DefaultException($"O texto da correção tem caracteres que a SEFAZ não aceita: {string.Join(' ', invalid)}");

        // A mesma trava da emissão, da consulta e do cancelamento.
        await using var emissionLock = await reservation.AcquireEmissionLockAsync(key);

        var invoice = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);

        EnsureIssuer(invoice);

        if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento cancelado não recebe carta de correção.");

        if (invoice.NfeStatus != NfeStatus.Authorized)
            throw new DefaultException("Só NF-e autorizada recebe carta de correção.");

        if (invoice.ChaveNFe is not { Length: 44 } accessKey)
            throw new DefaultException("A NF-e deste documento está sem chave de acesso.");

        var sequences = await store.CorrectionSequencesAsync(key);
        var sequence = (sequences.Count == 0 ? 0 : sequences.Max()) + 1;
        if (sequence > MaxCorrections)
            throw new DefaultException("Limite de 20 cartas de correção atingido para esta NF-e.");

        using var service = await settingsService.OpenAsync(invoice.BranchCode!, invoice.NfeEnvironment);

        var request = new NfeCorrectionRequest(
            accessKey, sequence, correction, accessKey.Substring(6, 14),
            TimeZoneInfo.ConvertTime(DateTimeOffset.Now, NfeIssueInputAssembler.BrasiliaZone));

        NfeEventResult result;
        try
        {
            result = await sefaz.SendCorrectionAsync(request, service.Settings);
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException(NoAnswerMessage);
        }
        catch (Exception e) when (e is not DefaultException)
        {
            // Certificado, TLS, XSD ausente: a situação real na SEFAZ é desconhecida, como no "sem resposta".
            logger.LogError(e, "Falha inesperada ao enviar a carta de correção da NF-e do documento {InvoiceKey}.", key);
            throw new DefaultException(NfeStatusText.Truncate($"{NoAnswerMessage} (detalhe técnico: {e.Message})"));
        }

        if (NfeStatusCodes.IsEventRegistered(result.StatusCode))
        {
            store.AddCorrection(invoice, sequence, correction, result, userName);
            await SaveAsync(key);
            return Outcome(sequence, result);
        }

        if (result.StatusCode == NfeStatusCodes.DuplicateEvent)
            return await RecoverDuplicateAsync(invoice, accessKey, sequence, correction, service.Settings, userName);

        throw new DefaultException($"Carta de correção recusada pela SEFAZ: {result.StatusCode} - {result.Reason}");
    }

    /// <summary>
    /// 573: a sequência já está na SEFAZ (envio anterior sem resposta). A consulta traz as cartas; se a desta
    /// sequência tem o MESMO texto, é sucesso; com outro texto, ela é importada e o usuário reenvia o texto novo.
    /// </summary>
    private async Task<NfeCorrectionOutcomeDto> RecoverDuplicateAsync(
        TDocument invoice, string accessKey, int sequence, string correction, NfeServiceSettings settings, string userName)
    {
        NfeSefazResult consult;
        try
        {
            consult = await sefaz.ConsultProtocolAsync(accessKey, settings);
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException(NoAnswerMessage);
        }

        var registered = consult.Corrections?.FirstOrDefault(c => c.Sequence == sequence);
        if (registered is null)
            throw new DefaultException(
                $"A SEFAZ informou evento duplicado, mas a consulta não trouxe a carta nº {sequence}: {consult.StatusCode} - {consult.Reason}");

        await NfeCorrectionImporter.ImportAsync(store, invoice, consult.Corrections, userName);
        await SaveAsync(invoice.Key);

        if (registered.CorrectionText != correction)
            throw new DefaultException(
                $"A carta de correção nº {sequence} já estava registrada na SEFAZ com outro envio e foi importada. " +
                "O texto enviado agora não foi registrado: envie de novo.");

        return Outcome(sequence, registered);
    }

    private async Task SaveAsync(Guid key)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Falha ao gravar a carta de correção registrada na SEFAZ do documento {InvoiceKey}.", key);
            db.Context.ChangeTracker.Clear();
            throw new DefaultException(NotSavedMessage);
        }
    }

    private static NfeCorrectionOutcomeDto Outcome(int sequence, NfeEventResult result) => new()
    {
        Sequence = sequence, Protocol = result.Protocol, RegisteredAt = result.RegisteredAt?.DateTime,
    };

    /// <summary>Quem emitiu a nota: a entrada de terceiro não recebe CC-e pelo Siagro.</summary>
    protected virtual void EnsureIssuer(TDocument document) { }
}
