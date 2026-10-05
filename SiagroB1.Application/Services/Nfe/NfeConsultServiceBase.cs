using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Consultar situação" (spec §9.3), comum aos documentos. Consulta pela chave, no ambiente da
/// EMISSÃO, e monta o procNFe a partir do XML assinado gravado antes do envio.
/// </summary>
public abstract class NfeConsultServiceBase<TDocument>(
    IUnitOfWork db,
    INfeDocumentStore<TDocument> store,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    NfeResultHandlerBase<TDocument> resultHandler,
    NfeNumberReservationService reservation,
    ILogger logger)
    where TDocument : class, INfeDocument
{
    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName)
    {
        // A mesma trava da emissão: consultar no meio de uma emissão em curso gravaria por cima.
        await using var emissionLock = await reservation.AcquireEmissionLockAsync(key);

        var invoice = await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);

        if (invoice.NfeStatus != NfeStatus.Processing)
            throw new DefaultException("Só a NF-e em processamento é consultada.");

        var signedXmls = await store.SignedXmlsAsync(key);

        if (signedXmls.Count == 0)
            throw new DefaultException("O XML assinado deste documento não foi encontrado.");

        using var service = await settingsService.OpenAsync(invoice.BranchCode!, invoice.NfeEnvironment);

        NfeSefazResult result;
        try
        {
            result = await sefaz.ConsultProtocolAsync(invoice.ChaveNFe!, service.Settings);
        }
        catch (NfeCommunicationException)
        {
            invoice.NfeStatusCode = null;
            invoice.NfeStatusReason = "Sem resposta da SEFAZ na consulta — tente de novo em instantes.";
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(invoice);
        }
        catch (Exception e)
        {
            // Qualquer outra falha da chamada (certificado, TLS, XML de retorno ilegível) também
            // deixa o documento em processamento: o detalhe técnico vai para o motivo.
            logger.LogError(e, "Falha inesperada ao consultar a NF-e do documento {InvoiceKey}.", key);
            invoice.NfeStatusCode = null;
            invoice.NfeStatusReason = NfeStatusText.Truncate(
                $"Sem resposta da SEFAZ na consulta — tente de novo em instantes. (detalhe técnico: {e.Message})");
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(invoice);
        }

        return await resultHandler.ApplyConsultAsync(invoice, signedXmls, result, userName);
    }
}
