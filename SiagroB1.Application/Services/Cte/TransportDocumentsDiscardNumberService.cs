using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Descarta o número do CT-e (Confirmado/Rejeitado/Rascunho com número): o número vai para a lista de inutilização
/// e o documento volta a Rascunho sem número, cCT e chave. Um documento já autorizado não passa por aqui.
/// </summary>
public class TransportDocumentsDiscardNumberService(IUnitOfWork db, IConfiguration configuration, CteNumberReservationService numbers)
{
    public async Task DiscardNumberAsync(Guid key, string? reason)
    {
        TransportDocumentsSaveService.EnsureStandalone(configuration);

        // Mesma trava da confirmação e da transmissão: não inutiliza número que a SEFAZ possa estar autorizando.
        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var doc = await db.Context.TransportDocuments.FirstOrDefaultAsync(x => x.Key == key)
                  ?? throw new DefaultException("CT-e não encontrado.");
        if (doc.Status is not (CteStatus.Draft or CteStatus.Confirmed or CteStatus.Rejected) || doc.Number is not { } number)
            throw new DefaultException("Só é possível descartar o número de um CT-e em rascunho, confirmado ou rejeitado que já tenha número.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DefaultException("Informe o motivo do descarte do número.");

        var text = reason.Trim();
        db.Context.CteNumbersToVoid.Add(new CteNumberToVoid
        {
            BranchCode = doc.BranchCode,
            Series = doc.Series,
            Number = number,
            Reason = text.Length > 255 ? text[..255] : text,
        });

        doc.Number = null;
        doc.AccessKey = null;
        doc.RandomCode = null;
        doc.IssuedAt = null;
        doc.StatusCode = null;
        doc.StatusReason = null;
        doc.Status = CteStatus.Draft;
        doc.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }
}
