using System.Globalization;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Aplica o cancelamento registrado na SEFAZ (spec 2026-10-05 §7.3), vindo do envio do evento ou da consulta.
/// Fase 1: grava a NF-e cancelada e o procEventoNFe e SALVA. Fase 2: cancela o documento com os estornos
/// do cancelamento comum; se falhar, a NF-e (já gravada) é a verdade e o erro vai para
/// <c>NfeCancellationError</c> — "Concluir cancelamento" refaz só a fase 2.
/// </summary>
public abstract class NfeCancellationHandlerBase<TDocument>(IUnitOfWork db, INfeDocumentStore<TDocument> store, ILogger logger)
    where TDocument : class, INfeDocument
{
    /// <summary>Justificativa gravada quando a nota foi cancelada fora do Siagro e a consulta não trouxe o evento.</summary>
    public const string UnknownReason = "Cancelada fora do Siagro";

    public async Task<NfeIssueOutcomeDto> ApplyRegisteredAsync(
        TDocument document, NfeEventResult result, string? justification, string userName)
    {
        if (result.ProcEventXml is not null)
            store.AddXml(document, NfeXmlKind.CancellationEvent, result.ProcEventXml);

        document.NfeStatus = NfeStatus.Cancelled;
        document.NfeCancellationProtocol = result.Protocol;
        document.NfeCancelledAt = result.RegisteredAt?.DateTime;
        document.NfeCancellationReason = justification ?? result.Justification ?? UnknownReason;
        document.NfeStatusCode = result.StatusCode.ToString(CultureInfo.InvariantCulture);
        document.NfeStatusReason = NfeStatusText.Truncate(result.Reason);
        document.NfeCancellationError = null;

        // A NF-e cancelada vai para o banco ANTES do cancelamento do documento.
        await db.SaveChangesAsync();

        return await CompleteLocalAsync(document.Key, userName);
    }

    public async Task<NfeIssueOutcomeDto> CompleteLocalAsync(Guid key, string userName)
    {
        try
        {
            await CancelDocumentAsync(key, userName);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Falha ao cancelar o documento {InvoiceKey} depois da NF-e cancelada.", key);

            // ⚠️ Mesmo cuidado do ConfirmAsync: o rollback não desfaz o rastreador. Descarta tudo e
            // grava só o erro, relendo o documento.
            db.Context.ChangeTracker.Clear();

            var failed = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);
            failed.NfeCancellationError = NfeStatusText.Truncate(e.Message);
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(failed);
        }

        var cancelled = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);
        if (cancelled.NfeCancellationError is not null)
        {
            cancelled.NfeCancellationError = null;
            await db.SaveChangesAsync();
        }

        return NfeIssueOutcomeDto.From(cancelled);
    }

    /// <summary>Ensaio das regras de negócio do cancelamento, antes de falar com a SEFAZ.</summary>
    public abstract Task EnsureCanCancelAsync(Guid key);

    /// <summary>O cancelamento do documento (estornos) — o de cada tipo de documento.</summary>
    protected abstract Task CancelDocumentAsync(Guid key, string userName);
}
