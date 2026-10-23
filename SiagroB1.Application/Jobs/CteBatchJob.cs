using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Transmite ou averba UM CT-e (enfileirado por <see cref="CteBatchService"/>). O primeiro argumento é a empresa
/// (lido pelo CompanyJobFilter). NUNCA relança: sem retry do Hangfire, o motivo da falha vai para o documento
/// (StatusReason na transmissão, EndorsementError na averbação) e o job termina com sucesso.
/// </summary>
[AutomaticRetry(Attempts = 0)]
public class CteBatchJob(
    TransportDocumentsTransmitService transmit,
    TransportDocumentsEndorseService endorse,
    IUnitOfWork db,
    ILogger<CteBatchJob> logger)
{
    private const int MaxReasonLength = 500;

    public async Task TransmitAsync(string companyCode, Guid key, string user)
    {
        try
        {
            // O serviço grava o resultado da SEFAZ (autorizado/rejeitado/processando) no documento.
            await transmit.TransmitAsync(key, user);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Transmissão em lote do CT-e {Key} falhou", key);
            await RecordAsync(key, user, e.Message, endorsement: false);
        }
    }

    public async Task EndorseAsync(string companyCode, Guid key, string user)
    {
        try
        {
            var result = await endorse.EndorseAsync(key, user);
            if (!result.Success)
                await RecordAsync(key, user, result.Message, endorsement: true);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Averbação em lote do CT-e {Key} falhou", key);
            await RecordAsync(key, user, e.Message, endorsement: true);
        }
    }

    /// <summary>Grava o motivo sem apagar um desfecho já gravado pelo serviço (autorizado, cancelado, averbado).</summary>
    private async Task RecordAsync(Guid key, string user, string? reason, bool endorsement)
    {
        try
        {
            // Estado sujo de uma falha no meio do serviço não pode ir junto.
            db.Context.ChangeTracker.Clear();
            var doc = await db.Context.TransportDocuments.FirstOrDefaultAsync(x => x.Key == key);
            if (doc is null)
                return;

            var text = Cut(reason);
            if (endorsement)
            {
                if (doc.Status != CteStatus.Authorized || doc.EndorsementStatus != CteEndorsementStatus.None)
                    return;
                doc.EndorsementError = text;
            }
            else
            {
                if (doc.Status is CteStatus.Authorized or CteStatus.Cancelled)
                    return;
                doc.StatusReason = text;
            }

            doc.UpdatedAt = DateTime.Now;
            doc.UpdatedBy = user;
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Não foi possível gravar o motivo da falha em lote do CT-e {Key}", key);
        }
    }

    private static string? Cut(string? text) =>
        text is { Length: > MaxReasonLength } ? text[..MaxReasonLength] : text;
}
