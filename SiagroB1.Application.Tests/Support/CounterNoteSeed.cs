using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Escrituração da NF-e do produtor pronta para a contra-nota (spec 2026-10-10): a entrada de terceiro confirmada do
/// <see cref="ThirdPartyPurchaseSeed"/> com as linhas calculadas pela natureza "COMPRA DE MERCADORIA" (CFOP 1102), a
/// natureza marcada "Permite contra-nota", a condição de pagamento preenchida e Funrural (SENAR 0,2%) na linha do TRIGO.
/// </summary>
public static class CounterNoteSeed
{
    public static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Producer)> SeedAsync(bool allowsCounterNote = true)
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        var context = scenario.Db.Context;

        var usage = await context.Usages.SingleAsync(u => u.Name == "COMPRA DE MERCADORIA");
        usage.AllowsCounterNote = allowsCounterNote;

        var producer = await context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);
        producer.PaymentConditionCode = (await context.PaymentConditions.FirstAsync()).Code;

        foreach (var line in producer.Items)
        {
            line.UsageCode = usage.Code;
            line.UsageName = usage.Name;
            line.Cfop = "1102";

            if (line.ItemCode == "TRIGO")
            {
                line.FunruralBase = 1500m;
                line.FunruralSenarRate = 0.2m;
                line.FunruralSenarValue = 3m;
            }
        }

        await scenario.Db.SaveChangesAsync();
        context.ChangeTracker.Clear();

        return (scenario, await context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key));
    }
}
