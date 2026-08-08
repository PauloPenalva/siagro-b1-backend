using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Commons.Scales;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WeighingTickets;

/// <summary>De onde veio o peso: qual balança e se foi capturado ou digitado.</summary>
public sealed record WeighingWeightOrigin(string? ScaleCode, bool Captured);

/// <summary>
/// Regras comuns às duas pesagens: quem pode digitar, o comprovante de captura e a tara.
///
/// O comprovante é o que impede burlar a restrição por fora da tela: sem a permissão, o peso
/// precisa ter nascido no servidor, e o comprovante é de uso único.
/// </summary>
public class WeighingCaptureValidator(
    IUnitOfWork db,
    IUserPermissions permissions,
    CaptureStore captures)
{
    public async Task<WeighingWeightOrigin> ResolveAsync(
        string username,
        int weigh,
        Guid? captureId,
        WeighingScalePurpose purpose,
        string truckCode)
    {
        var canType = await permissions.HasAsync(username, PermissionCodes.WeighingManualEntry);

        string? scaleCode = null;
        var captured = false;

        if (captureId.HasValue)
        {
            var capture = captures.Consume(captureId.Value, username, DateTime.Now)
                ?? throw new ApplicationException(
                    "A captura do peso expirou ou já foi utilizada. Capture o peso novamente.");

            if (capture.Weight != weigh)
                throw new ApplicationException(
                    "O peso informado não confere com o peso capturado na balança.");

            scaleCode = capture.ScaleCode;
            captured = true;
        }
        else if (!canType)
        {
            throw new ApplicationException(
                "O peso deve ser capturado da balança. Este usuário não pode digitar o peso.");
        }

        scaleCode ??= await GetConfiguredScaleCodeAsync(username, purpose);

        await ValidateTareAsync(scaleCode, truckCode, weigh);

        return new WeighingWeightOrigin(scaleCode, captured);
    }

    private async Task<string?> GetConfiguredScaleCodeAsync(string username, WeighingScalePurpose purpose) =>
        await db.Context.UserTruckScales
            .AsNoTracking()
            .Where(x => x.Username == username && x.Purpose == purpose)
            .Select(x => x.TruckScaleCode)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Vale para as duas pesagens: nenhum peso lido pode ser menor que a tara cadastrada menos a
    /// tolerância da balança. Sem balança determinável, a validação não incide.
    /// </summary>
    private async Task ValidateTareAsync(string? scaleCode, string truckCode, int weigh)
    {
        if (scaleCode == null)
            return;

        var scale = await db.Context.TruckScales
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == scaleCode);

        if (scale is not { ValidateTare: true })
            return;

        var tare = await db.Context.Trucks
            .AsNoTracking()
            .Where(x => x.Code == truckCode)
            .Select(x => x.TareWeight)
            .FirstOrDefaultAsync();

        if (tare == null)
            throw new ApplicationException(
                "Caminhão sem tara cadastrada. Informe a tara no cadastro do veículo antes de pesar.");

        var minimum = tare.Value - scale.TareToleranceKg;

        if (weigh < minimum)
            throw new ApplicationException(
                $"Peso de {weigh:N0} kg é menor que a tara cadastrada de {tare.Value:N0} kg " +
                $"menos a tolerância de {scale.TareToleranceKg:N0} kg.");
    }
}
