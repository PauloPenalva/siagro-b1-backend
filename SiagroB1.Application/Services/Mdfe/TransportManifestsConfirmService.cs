using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Mdfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Mdfe;

/// <summary>
/// Confirma o MDF-e (Rascunho → Confirmado). Na primeira confirmação reserva o número, sorteia o cMDF e monta a
/// chave de acesso (mês da confirmação); depois disso número, cMDF e chave nunca mudam — reabrir e confirmar de
/// novo só reaproveita. Para abandonar o número é preciso "Descartar número" (MDF-e Anulado).
/// </summary>
public class TransportManifestsConfirmService(
    IUnitOfWork db,
    IConfiguration configuration,
    MdfeNumberReservationService numbers,
    MdfeReadinessValidator validator)
{
    public const string NotDraft = "Só é possível confirmar MDF-e em rascunho.";

    public async Task ConfirmAsync(Guid key, string user)
    {
        TransportManifestsCreateFromCtesService.EnsureStandalone(configuration);

        // Trava por documento: duas confirmações simultâneas não reservam dois números.
        await using var emissionLock = await numbers.AcquireEmissionLockAsync(key);

        var m = await db.Context.TransportManifests.Include(x => x.Ctes).Include(x => x.Vehicles).Include(x => x.Drivers)
                    .Include(x => x.Payments).Include(x => x.Contractors).AsSplitQuery().FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new DefaultException(TransportManifestsSaveService.NotFound);
        if (m.Status != MdfeStatus.Draft) throw new DefaultException(NotDraft);

        MdfeInsuranceSnapshot.RefreshIfIncomplete(m,
            await db.Context.BranchTransportSettings.AsNoTracking().FirstOrDefaultAsync(x => x.BranchCode == m.BranchCode));
        var (errors, _) = await validator.ValidateAsync(m);
        if (errors.Count > 0) throw new DefaultException(string.Join(" ", errors));

        var settings = await db.Context.BranchMdfeSettings.AsNoTracking().FirstAsync(x => x.BranchCode == m.BranchCode);

        if (m.Number is null)
        {
            var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(x => x.Code == m.BranchCode)
                         ?? throw new DefaultException($"Filial {m.BranchCode} não encontrada.");
            var municipalityState = string.IsNullOrWhiteSpace(branch.MunicipalityCode)
                ? null
                : await db.Context.Municipalities.AsNoTracking().Where(x => x.Code == branch.MunicipalityCode)
                    .Select(x => x.StateAbbreviation).FirstOrDefaultAsync();
            var ufCode = TransportDocumentsConfirmService.UfCodes.GetValueOrDefault((municipalityState ?? branch.StateCode)?.Trim() ?? string.Empty)
                         ?? throw new DefaultException($"A filial {m.BranchCode} está sem UF válida no cadastro.");
            if (string.IsNullOrWhiteSpace(branch.TaxId))
                throw new DefaultException($"A filial {m.BranchCode} está sem CNPJ no cadastro.");

            // Conexão própria, fora da transação do EF: o número reservado não volta se o save falhar.
            var number = await numbers.ReserveAsync(m.BranchCode, m.Series);
            var now = DateTime.Now;
            m.Number = number;
            m.RandomCode = GenerateRandomCode(number);
            m.IssuedAt = now;
            m.AccessKey = MdfeAccessKey.Build(ufCode, now, branch.TaxId.Trim(), m.Series, number, 1, m.RandomCode);
        }

        m.Environment = settings.Environment;
        m.ApplyStatus(MdfeStatus.Confirmed);
        m.UpdatedAt = DateTime.Now;
        m.UpdatedBy = user;
        await db.SaveChangesAsync();
    }

    /// <summary>cMDF: 8 dígitos aleatórios, nunca iguais ao número com zeros à esquerda (regra da SEFAZ).</summary>
    internal static string GenerateRandomCode(int number)
    {
        var forbidden = number.ToString("00000000", CultureInfo.InvariantCulture);
        string code;
        do
        {
            code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("00000000", CultureInfo.InvariantCulture);
        } while (code == forbidden);
        return code;
    }
}
