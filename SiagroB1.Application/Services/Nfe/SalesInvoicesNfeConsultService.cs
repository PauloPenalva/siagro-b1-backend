using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Consultar situação" (spec §9.3). Consulta pela chave, no ambiente da EMISSÃO, e monta o
/// procNFe a partir do XML assinado gravado antes do envio.
/// </summary>
public class SalesInvoicesNfeConsultService(
    IUnitOfWork db,
    BranchNfeSettingsService settingsService,
    INfeSefazClient sefaz,
    SalesInvoiceNfeResultHandler resultHandler,
    NfeNumberReservationService reservation,
    ILogger<SalesInvoicesNfeConsultService> logger)
{
    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName)
    {
        // A mesma trava da emissão: consultar no meio de uma emissão em curso gravaria por cima.
        await using var emissionLock = await reservation.AcquireEmissionLockAsync(key);

        var invoice = await db.Context.SalesInvoices.FirstOrDefaultAsync(i => i.Key == key)
                      ?? throw new NotFoundException("Documento de saída não encontrado.");

        if (invoice.NfeStatus != NfeStatus.Processing)
            throw new DefaultException("Só a NF-e em processamento é consultada.");

        var signedXmls = await db.Context.SalesInvoiceNfeXmls.AsNoTracking()
            .Where(x => x.SalesInvoiceKey == key && x.Kind == SalesInvoiceNfeXmlKind.Signed)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Xml)
            .ToListAsync();

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
            invoice.NfeStatusReason = SalesInvoiceNfeResultHandler.Truncate(
                $"Sem resposta da SEFAZ na consulta — tente de novo em instantes. (detalhe técnico: {e.Message})");
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(invoice);
        }

        return await resultHandler.ApplyConsultAsync(invoice, signedXmls, result, userName);
    }
}
