using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// "Desvincular NFs do SAP" (GAC-1226): desfaz o vínculo cancelando, numa transação só, todos os documentos de
/// saída ligados a NFs do SAP de uma carga que ainda está só Faturada (sem descarga nem conferência).
/// </summary>
/// <remarks>
/// O cancelamento de cada documento é o <see cref="SalesInvoicesCancelService.CancelForUnlinkAsync"/>: roda na
/// transação daqui, mantém todas as travas do cancelamento (título baixado, mês fechado, quebra de descarga,
/// complemento de ICMS), anula o registro de divergência e recalcula a carga. Qualquer recusa desfaz tudo.
/// </remarks>
public class ShipmentLoadsUnlinkSapInvoicesService(
    IUnitOfWork db,
    SalesInvoicesCancelService cancel,
    IConfiguration? configuration = null,
    ILogger<ShipmentLoadsUnlinkSapInvoicesService>? logger = null)
{
    private const string PersistenceFailure =
        "Não foi possível desvincular as notas fiscais do SAP. Nenhuma alteração foi gravada. Tente novamente.";

    /// <summary>Devolve quantos documentos foram cancelados.</summary>
    public async Task<int> ExecuteAsync(Guid loadKey, string userName)
    {
        if (!ErpMode.IsSapB1(configuration))
            throw new ApplicationException("Disponível somente com integração SAP Business One.");

        var load = await db.Context.ShipmentLoads.AsNoTracking().FirstOrDefaultAsync(x => x.Key == loadKey)
                   ?? throw new NotFoundException($"Shipment load not found key {loadKey}");

        EnsureUnlinkableStatus(load.Status, load.Code);
        await LoadTargetsAsync(load.Key, load.Code);

        await db.BeginTransactionAsync();
        try
        {
            // TRAVA da carga — primeira escrita da transação (mesma razão do vínculo): serializa dois
            // desvínculos/vínculos da mesma carga; o concorrente espera aqui e relê o estado já alterado.
            var lockedLoad = await db.Context.ShipmentLoads.FirstAsync(x => x.Key == loadKey);
            lockedLoad.UpdatedAt = DateTime.Now;
            lockedLoad.UpdatedBy = userName;
            await db.SaveChangesAsync();

            // Relido sob a trava: o que a validação acima viu pode ter mudado.
            EnsureUnlinkableStatus(lockedLoad.Status, lockedLoad.Code);
            var targets = await LoadTargetsAsync(lockedLoad.Key, lockedLoad.Code);

            foreach (var target in targets)
                await cancel.CancelForUnlinkAsync(target.Key, userName);

            await db.CommitAsync();
            return targets.Count;
        }
        catch (Exception e)
        {
            await db.RollbackAsync();

            // Recusa das travas do cancelamento e erros de negócio seguem com a própria mensagem.
            if (HasDbUpdateException(e))
            {
                logger?.LogError(e, "Falha ao desvincular as NFs do SAP da carga {LoadKey}", loadKey);
                throw new ApplicationException(PersistenceFailure, e);
            }

            throw;
        }
    }

    private static void EnsureUnlinkableStatus(ShipmentLoadStatus status, string? code)
    {
        if (status is ShipmentLoadStatus.Discharged or ShipmentLoadStatus.Completed)
            throw new ApplicationException(
                "Desfaça a descarga e a conferência antes de desvincular as notas fiscais.");

        if (status is not (ShipmentLoadStatus.Invoiced or ShipmentLoadStatus.PartiallyInvoiced))
            throw new ApplicationException(
                $"A carga {code} não está faturada: não há notas fiscais do SAP para desvincular.");
    }

    /// <summary>Documentos a cancelar, já validados (sem devolução viva e sem pendentes), em ordem estável.</summary>
    private async Task<List<SalesInvoice>> LoadTargetsAsync(Guid loadKey, string? loadCode)
    {
        var targets = await db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.ShipmentLoadKey == loadKey
                        && i.InvoiceType == SalesInvoiceType.Normal
                        && i.SapDocEntry != null
                        && (i.InvoiceStatus == InvoiceStatus.Pending
                            || i.InvoiceStatus == InvoiceStatus.Confirmed
                            || i.InvoiceStatus == InvoiceStatus.Returned))
            .OrderBy(i => i.InvoiceNumber)
            .ToListAsync();

        if (targets.Count == 0)
            throw new ApplicationException($"A carga {loadCode} não tem nota fiscal do SAP vinculada.");

        var keys = targets.Select(t => t.Key).ToList();
        var hasLiveReturn = await db.Context.SalesInvoices.AsNoTracking().AnyAsync(i =>
            i.InvoiceType == SalesInvoiceType.Return
            && i.SalesInvoiceOriginKey != null && keys.Contains(i.SalesInvoiceOriginKey.Value)
            && i.InvoiceStatus != InvoiceStatus.Cancelled);
        if (hasLiveReturn)
            throw new ApplicationException("Estorne a recusa antes de desvincular.");

        var pending = targets.FirstOrDefault(t => t.InvoiceStatus == InvoiceStatus.Pending);
        if (pending is not null)
            throw new ApplicationException(
                $"O documento {pending.InvoiceNumber} está pendente. Confirme-o ou exclua-o antes de desvincular.");

        return targets;
    }

    private static bool HasDbUpdateException(Exception e)
    {
        for (var inner = e; inner != null; inner = inner.InnerException)
            if (inner is DbUpdateException)
                return true;
        return false;
    }
}
