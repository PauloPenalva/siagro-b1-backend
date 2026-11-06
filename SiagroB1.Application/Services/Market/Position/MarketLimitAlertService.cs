using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Position;

/// <summary>
/// Alerta de estouro de limite de exposição: um aviso por limite por dia. Grava o <see cref="MarketLimitBreach"/> e a
/// mensagem da outbox (WhatsApp) no MESMO SaveChanges; o índice único (LimitId, Date) é a guarda real — quem perde a
/// corrida contra outra execução desfaz os dois e segue. Limite que voltou ao normal não gera nada nem apaga avisos.
/// </summary>
public class MarketLimitAlertService(
    AppDbContext context, MarketPositionService position, TimeProvider time, ILogger<MarketLimitAlertService> logger)
{
    private const string SystemUser = "sistema";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Confere os limites em <paramref name="today"/> (data de São Paulo); devolve quantos avisos novos gravou.</summary>
    public async Task<int> CheckAsync(DateOnly today, CancellationToken ct)
    {
        var snapshot = await position.GetSnapshotAsync(today, ct, bypassCache: true);
        if (snapshot.Alerts.Count == 0) return 0;

        var date = today.ToDateTime(TimeOnly.MinValue);
        var already = (await context.MarketLimitBreaches.AsNoTracking()
            .Where(b => b.Date == date).Select(b => b.LimitId).ToListAsync(ct)).ToHashSet();

        var written = 0;
        foreach (var alert in snapshot.Alerts.GroupBy(a => a.LimitId).Select(g => g.First()))
        {
            if (already.Contains(alert.LimitId)) continue;

            var shortBreach = alert.State == "shortExceeded";
            var breach = new MarketLimitBreach
            {
                LimitId = alert.LimitId,
                Date = date,
                Value = Math.Round(alert.Value, 2, MidpointRounding.AwayFromZero),
                LimitValue = shortBreach ? -alert.LimitValue : alert.LimitValue,
                DetectedAt = time.GetUtcNow().UtcDateTime,
            };
            var message = new NotificationOutboxMessage
            {
                DocumentType = NotificationDocumentType.MarketPosition,
                EventType = NotificationEventType.ExposureLimitExceeded,
                DocumentKey = SyntheticKey(alert.LimitId, today),
                DocumentCode = $"LIMITE-{alert.LimitId}",
                OccurredAt = DateTime.Now,
                TriggeredBy = SystemUser,
                PayloadJson = JsonSerializer.Serialize(new { text = BuildMessage(alert) }),
                Status = NotificationOutboxStatus.Pending,
                CreatedBy = SystemUser,
            };
            context.MarketLimitBreaches.Add(breach);
            context.NotificationOutboxMessages.Add(message);
            try
            {
                await context.SaveChangesAsync(ct);
                written++;
            }
            catch (DbUpdateException ex)
            {
                // Outra execução gravou o aviso deste limite hoje (índice único): descarta os dois e segue com os demais.
                context.Entry(breach).State = EntityState.Detached;
                context.Entry(message).State = EntityState.Detached;
                logger.LogInformation(ex, "Estouro do limite {LimitId} em {Date} já registrado por outra execução.", alert.LimitId, today);
            }
        }
        return written;
    }

    /// <summary>Texto pt-BR pronto para o WhatsApp.</summary>
    public static string BuildMessage(MarketLimitAlert alert)
    {
        var side = alert.Value < 0 ? "vendida" : "comprada";
        var usd = alert.Scope == "usd";
        string Format(decimal v) => usd ? $"US$ {Math.Abs(v).ToString("N2", PtBr)}" : $"{Math.Abs(v).ToString("N3", PtBr)} t";
        return $"Limite de exposição excedido — {alert.Description}: posição {side} de {Format(alert.Value)} acima do limite de {Format(alert.LimitValue)}.";
    }

    /// <summary>Chave estável por limite e dia (a outbox exige Guid, sem FK).</summary>
    private static Guid SyntheticKey(int limitId, DateOnly date) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes($"market-limit:{limitId}:{date:yyyy-MM-dd}")));
}
