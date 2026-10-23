using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// Descarta o número do MDF-e (Rascunho já numerado/Confirmado/Rejeitado → Anulado). O número fica no documento anulado e os CT-e
/// são liberados (vínculo inativo) para entrar em outro MDF-e. Um MDF-e autorizado não passa por aqui.
/// </summary>
public class TransportManifestsDiscardNumberService(IUnitOfWork db, IConfiguration configuration, MdfeNumberReservationService numbers)
{
    public async Task DiscardNumberAsync(Guid key)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);

        // Mesma trava da confirmação e da transmissão: não anula número que a SEFAZ possa estar autorizando.
        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var m = await db.Context.TransportManifests.Include(x => x.Ctes).FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new DefaultException(TransportManifestsSaveService.NotFound);
        if (m.Status is not (MdfeStatus.Draft or MdfeStatus.Confirmed or MdfeStatus.Rejected) || m.Number is null)
            throw new DefaultException("Só é possível descartar o número de um MDF-e em rascunho (já numerado), confirmado ou rejeitado.");

        m.ApplyStatus(MdfeStatus.Voided);
        m.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }
}
