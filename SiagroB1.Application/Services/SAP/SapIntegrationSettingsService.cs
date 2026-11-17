using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SAP;

/// <summary>
/// Configuração da integração SAP da empresa (linha única). Sem linha, vale o padrão: a tela abre
/// e o job roda sem exigir semeadura.
/// </summary>
public class SapIntegrationSettingsService(IUnitOfWork db)
{
    private const int SingleKey = 1;

    public async Task<SapIntegrationSettingsModel> GetAsync()
    {
        var row = await db.Context.SapIntegrationSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SingleKey);
        return ToModel(row ?? new SapIntegrationSettings());
    }

    /// <summary>Grava liga/desliga e intervalo. A última execução nunca vem do cliente.</summary>
    public async Task<SapIntegrationSettingsModel> SaveAsync(SapIntegrationSettingsModel model)
    {
        if (model.InvoiceSyncIntervalMinutes is < SapIntegrationSettings.MinIntervalMinutes or > SapIntegrationSettings.MaxIntervalMinutes)
            throw new DefaultException("Informe um intervalo entre 1 e 1440 minutos.");

        var row = await db.Context.SapIntegrationSettings.FirstOrDefaultAsync(x => x.Key == SingleKey);

        if (row is null)
        {
            row = new SapIntegrationSettings { Key = SingleKey };
            db.Context.SapIntegrationSettings.Add(row);
        }

        row.InvoiceSyncEnabled = model.InvoiceSyncEnabled;
        row.InvoiceSyncIntervalMinutes = model.InvoiceSyncIntervalMinutes;

        await db.SaveChangesAsync();

        return ToModel(row);
    }

    public static SapIntegrationSettingsModel ToModel(SapIntegrationSettings row) => new()
    {
        InvoiceSyncEnabled = row.InvoiceSyncEnabled,
        InvoiceSyncIntervalMinutes = row.InvoiceSyncIntervalMinutes,
        InvoiceSyncLastRunAt = row.InvoiceSyncLastRunAt,
    };
}
