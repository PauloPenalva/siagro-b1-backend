using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>Reabre o MDF-e confirmado para edição (Confirmado → Rascunho). Mantém número, cMDF e chave.</summary>
public class TransportManifestsReopenService(IUnitOfWork db, IConfiguration configuration, MdfeNumberReservationService numbers)
{
    public async Task ReopenAsync(Guid key)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);

        // Mesma trava da confirmação e da transmissão: não volta a rascunho um MDF-e que a SEFAZ possa estar autorizando.
        // A situação é lida (e conferida) já dentro da trava.
        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var m = await db.Context.TransportManifests.Include(x => x.Ctes).FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new DefaultException(TransportManifestsSaveService.NotFound);
        if (m.Status != MdfeStatus.Confirmed)
            throw new DefaultException("Só é possível reabrir MDF-e confirmado.");

        m.ApplyStatus(MdfeStatus.Draft);
        m.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }
}
