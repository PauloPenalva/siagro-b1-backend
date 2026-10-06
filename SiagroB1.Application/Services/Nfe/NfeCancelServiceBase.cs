using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Cancelar NF-e" (spec 2026-10-05 §7.2), comum aos documentos: valida, faz o ensaio das regras locais,
/// envia o evento 110111 no ambiente da EMISSÃO e entrega o registro ao handler (fase 1 + fase 2).
/// Recusa da SEFAZ ou falta de resposta não mudam o documento.
/// </summary>
public abstract class NfeCancelServiceBase<TDocument>(
    INfeDocumentStore<TDocument> store,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeCancellationHandlerBase<TDocument> handler,
    NfeNumberReservationService reservation)
    where TDocument : class, INfeDocument
{
    public const int MinJustificationLength = 15;
    public const int MaxJustificationLength = 255;

    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string? justification, string userName)
    {
        var reason = (justification ?? string.Empty).Trim();
        if (reason.Length is < MinJustificationLength or > MaxJustificationLength)
            throw new DefaultException("A justificativa deve ter entre 15 e 255 caracteres.");

        // A mesma trava da emissão e da consulta.
        await using var emissionLock = await reservation.AcquireEmissionLockAsync(key);

        var invoice = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);

        EnsureIssuer(invoice);

        if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento já está cancelado.");

        if (invoice.NfeStatus == NfeStatus.Cancelled)
            throw new DefaultException("A NF-e deste documento já foi cancelada: use Concluir cancelamento.");

        if (invoice.NfeStatus != NfeStatus.Authorized)
            throw new DefaultException("Só NF-e autorizada pode ser cancelada.");

        if (invoice.ChaveNFe is not { Length: 44 } accessKey || string.IsNullOrWhiteSpace(invoice.NfeProtocol))
            throw new DefaultException("A NF-e deste documento está sem chave ou protocolo de autorização.");

        // Ensaio: uma regra local que recusaria o cancelamento recusa ANTES do evento.
        try
        {
            await handler.EnsureCanCancelAsync(key);
        }
        catch (ApplicationException e)
        {
            throw new DefaultException(e.Message);
        }

        using var service = await settingsService.OpenAsync(invoice.BranchCode!, invoice.NfeEnvironment);

        var request = new NfeCancelRequest(
            accessKey, invoice.NfeProtocol, reason, accessKey.Substring(6, 14),
            TimeZoneInfo.ConvertTime(DateTimeOffset.Now, NfeIssueInputAssembler.BrasiliaZone));

        NfeEventResult result;
        try
        {
            result = await sefaz.CancelAsync(request, service.Settings);
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException("Sem resposta da SEFAZ no cancelamento: use Consultar situação antes de tentar de novo.");
        }

        if (NfeStatusCodes.IsCancellationRegistered(result.StatusCode))
            return await handler.ApplyRegisteredAsync(invoice, result, reason, userName);

        if (result.StatusCode == NfeStatusCodes.DuplicateEvent)
        {
            // O evento já está na SEFAZ (envio anterior sem resposta): a consulta traz o registrado.
            NfeSefazResult consult;
            try
            {
                consult = await sefaz.ConsultProtocolAsync(accessKey, service.Settings);
            }
            catch (NfeCommunicationException)
            {
                throw new DefaultException("Sem resposta da SEFAZ no cancelamento: use Consultar situação antes de tentar de novo.");
            }

            if (consult.StatusCode == NfeStatusCodes.Cancelled)
                return await handler.ApplyRegisteredAsync(
                    invoice, consult.CancellationEvent ?? new NfeEventResult(consult.StatusCode, consult.Reason), null, userName);

            throw new DefaultException(
                $"A SEFAZ informou evento duplicado, mas a consulta não confirmou o cancelamento: {consult.StatusCode} - {consult.Reason}");
        }

        throw new DefaultException($"Cancelamento recusado pela SEFAZ: {result.StatusCode} - {result.Reason}");
    }

    /// <summary>Quem emitiu a nota: a entrada de terceiro não é cancelada pelo Siagro.</summary>
    protected virtual void EnsureIssuer(TDocument document) { }
}
