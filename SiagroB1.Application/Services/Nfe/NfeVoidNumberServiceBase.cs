using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Inutilizar numeração" (spec 2026-10-08), comum aos documentos: a NF-e rejeitada de um documento já
/// cancelado nunca foi autorizada, e o número dela precisa ser inutilizado na SEFAZ (NfeInutilizacao4).
/// 102 grava o procInutNFe; 256/563 (já inutilizada) marcam sem comprovante; recusa e falta de resposta
/// não mudam o documento — reenviar é seguro.
/// </summary>
public abstract class NfeVoidNumberServiceBase<TDocument>(
    IUnitOfWork db,
    INfeDocumentStore<TDocument> store,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeNumberReservationService reservation,
    ILogger logger)
    where TDocument : class, INfeDocument
{
    public const string NoAnswerMessage = "Sem resposta da SEFAZ na inutilização: tente de novo.";
    public const string NotCancelledMessage = "Só o documento cancelado tem a numeração inutilizada: cancele o documento antes.";
    public const string NotRejectedMessage = "Só a NF-e rejeitada tem a numeração inutilizada.";
    public const string AlreadyVoidedMessage = "A numeração da NF-e deste documento já foi inutilizada.";
    public const string NoNumberMessage = "Este documento não tem número e série de NF-e para inutilizar.";

    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string? justification, string userName)
    {
        var reason = (justification ?? string.Empty).Trim();
        if (reason.Length is < NfeCancelServiceBase<TDocument>.MinJustificationLength
            or > NfeCancelServiceBase<TDocument>.MaxJustificationLength)
            throw new DefaultException("A justificativa deve ter entre 15 e 255 caracteres.");

        // A mesma trava da emissão, da consulta e do cancelamento.
        await using var emissionLock = await reservation.AcquireEmissionLockAsync(key);

        var invoice = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);

        EnsureIssuer(invoice);

        if (invoice.NfeStatus == NfeStatus.Voided)
            throw new DefaultException(AlreadyVoidedMessage);

        if (invoice.InvoiceStatus != InvoiceStatus.Cancelled)
            throw new DefaultException(NotCancelledMessage);

        if (invoice.NfeStatus != NfeStatus.Rejected)
            throw new DefaultException(NotRejectedMessage);

        if (!int.TryParse(invoice.TaxDocumentNumber, out var number) || !int.TryParse(invoice.TaxDocumentSeries, out var series))
            throw new DefaultException(NoNumberMessage);

        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == invoice.BranchCode)
                     ?? throw new DefaultException($"A filial {invoice.BranchCode} do documento não existe.");

        // Ano da tentativa que foi à SEFAZ (AA da chave); a rejeição local não tem chave: ano corrente de Brasília.
        var year = invoice.ChaveNFe is { Length: 44 } accessKey
            ? int.Parse(accessKey.Substring(2, 2), CultureInfo.InvariantCulture)
            : TimeZoneInfo.ConvertTime(DateTimeOffset.Now, NfeIssueInputAssembler.BrasiliaZone).Year % 100;

        // Ambiente da EMISSÃO; nulo (rejeição local) = o atual da filial.
        using var service = await settingsService.OpenAsync(invoice.BranchCode!, invoice.NfeEnvironment);

        var request = new NfeVoidNumberRequest(year, NfeText.AlphaNumeric(branch.TaxId), series, number, reason);

        NfeVoidNumberResult result;
        try
        {
            result = await sefaz.VoidNumberAsync(request, service.Settings);
        }
        catch (NfeCommunicationException)
        {
            throw new DefaultException(NoAnswerMessage);
        }
        catch (Exception e) when (e is not DefaultException)
        {
            logger.LogError(e, "Falha inesperada ao inutilizar a numeração da NF-e do documento {InvoiceKey}.", key);
            throw new DefaultException(NfeStatusText.Truncate($"{NoAnswerMessage} (detalhe técnico: {e.Message})"));
        }

        var homologated = result.StatusCode == NfeStatusCodes.NumberVoided;
        if (!homologated && !NfeStatusCodes.IsNumberAlreadyVoided(result.StatusCode))
            throw new DefaultException($"Inutilização recusada pela SEFAZ: {result.StatusCode} - {result.Reason}");

        // Antes de gravar: se o save falhar, o protocolo da SEFAZ continua no log.
        logger.LogInformation(
            "SEFAZ respondeu à inutilização {Series}/{Number}: status {StatusCode}, protocolo {Protocol}.",
            series, number, result.StatusCode, result.Protocol);

        if (homologated && result.ProcXml is not null)
            store.AddXml(invoice, NfeXmlKind.NumberVoid, result.ProcXml);

        invoice.NfeStatus = NfeStatus.Voided;
        invoice.NfeStatusCode = result.StatusCode.ToString(CultureInfo.InvariantCulture);
        invoice.NfeStatusReason = NfeStatusText.Truncate(result.Reason);
        if (result.Protocol is not null)
            invoice.NfeProtocol = result.Protocol;

        await db.SaveChangesAsync();

        logger.LogInformation("Numeração {Series}/{Number} inutilizada ({StatusCode}) por {User}.",
            series, number, result.StatusCode, userName);

        return NfeIssueOutcomeDto.From(invoice);
    }

    /// <summary>Quem emitiu a nota: a entrada de terceiro não tem numeração do Siagro.</summary>
    protected virtual void EnsureIssuer(TDocument document) { }
}
