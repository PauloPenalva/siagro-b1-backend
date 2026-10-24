using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.FreightOrders;

/// <summary>Inclui e altera a ordem de frete. Só STANDALONE.</summary>
public class FreightOrdersSaveService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    IBusinessPartnerService partners,
    IItemService items,
    IConfiguration configuration,
    FreightOrdersRecalculateService recalculate)
{
    public async Task<FreightOrder> SaveAsync(FreightOrderSaveModel m, string user)
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(BranchCteSettingsService.StandaloneOnly);

        if (m.ContractedWeightKg <= 0) throw new DefaultException("Informe o volume contratado.");
        if (m.UnitPrice <= 0) throw new DefaultException("Informe o preço unitário.");
        if (m.ValidTo is { } to && to.Date < m.ValidFrom.Date)
            throw new DefaultException("A vigência final não pode ser anterior à inicial.");

        var taker = await partners.GetByIdAsync(m.TakerCardCode)
            ?? throw new DefaultException("Tomador não encontrado.");
        var item = await items.GetByIdAsync(m.ItemCode)
            ?? throw new DefaultException("Produto não encontrado.");
        var names = new Lookups(
            taker.CardName,
            await NameOfAsync(m.SenderCardCode),
            await NameOfAsync(m.RecipientCardCode),
            // O nome do produto vem do cadastro, nunca do cliente (vira o produto predominante do CT-e).
            Truncate(item.ItemName ?? item.ItemCode, 60)!,
            await MunicipalityNameAsync(m.StartMunicipalityCode),
            await MunicipalityNameAsync(m.EndMunicipalityCode));

        if (m.Key is null) return await CreateAsync(m, user, names);

        var order = await db.Context.FreightOrders.FirstOrDefaultAsync(o => o.Key == m.Key)
            ?? throw new DefaultException("Ordem de frete não encontrada.");
        if (order.Status is FreightOrderStatus.Completed or FreightOrderStatus.Cancelled)
            throw new DefaultException("Ordem encerrada ou cancelada não pode ser alterada.");

        var hasAuthorized = await db.Context.TransportDocuments
            .AnyAsync(d => d.FreightOrderKey == order.Key && d.Status == CteStatus.Authorized);
        if (hasAuthorized && (order.TakerCardCode != m.TakerCardCode || order.ItemCode != m.ItemCode))
            throw new DefaultException("Ordem com CT-e autorizado: tomador e produto não podem ser alterados.");

        Apply(order, m, names);
        await db.SaveChangesAsync();
        // Mudar o volume contratado muda o saldo: a situação precisa acompanhar.
        await recalculate.RecalculateAsync(order.Key);
        return order;
    }

    private async Task<FreightOrder> CreateAsync(FreightOrderSaveModel m, string user, Lookups names)
    {
        // Número buscado antes de gravar (Dapper em conexão separada).
        DocNumberAssignment assignment;
        try
        {
            assignment = await docNumberSequence.AssignAsync(new DocNumberRequest(
                TransactionCode.FreightOrder, m.DocNumberKey, m.TypedCode, 20,
                c => db.Context.FreightOrders.AnyAsync(x => x.Code == c)));
        }
        catch (NotFoundException) when (m.DocNumberKey is null)
        {
            throw new DefaultException(
                "Não há série de numeração padrão para a Ordem de Frete. Cadastre uma em Numeração de Documentos.");
        }

        var order = new FreightOrder
        {
            BranchCode = m.BranchCode, Code = assignment.Code, DocNumberKey = assignment.DocNumberKey,
            TakerCardCode = m.TakerCardCode, StartMunicipalityCode = m.StartMunicipalityCode, StartState = m.StartState,
            EndMunicipalityCode = m.EndMunicipalityCode, EndState = m.EndState, ItemCode = m.ItemCode,
            ItemName = names.ItemName, CreatedBy = user, CreatedAt = DateTime.Now,
            Status = FreightOrderStatus.Open,
            OrderDate = m.OrderDate ?? DateTime.Today,
        };
        Apply(order, m, names);
        db.Context.FreightOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private sealed record Lookups(
        string TakerName, string? SenderName, string? RecipientName, string ItemName,
        string? StartMunicipalityName, string? EndMunicipalityName);

    private static string? Truncate(string? v, int max) => v is { } s && s.Length > max ? s[..max] : v;

    private async Task<string?> NameOfAsync(string? cardCode) =>
        string.IsNullOrWhiteSpace(cardCode) ? null : Truncate((await partners.GetByIdAsync(cardCode))?.CardName, 100);

    private async Task<string?> MunicipalityNameAsync(string code) =>
        await db.Context.Municipalities.Where(x => x.Code == code).Select(x => x.Name).FirstOrDefaultAsync();

    private static void Apply(FreightOrder o, FreightOrderSaveModel m, Lookups n)
    {
        o.TakerName = Truncate(n.TakerName, 100);
        o.SenderName = n.SenderName;
        o.RecipientName = n.RecipientName;
        o.StartMunicipalityName = n.StartMunicipalityName;
        o.EndMunicipalityName = n.EndMunicipalityName;
        o.TakerCardCode = m.TakerCardCode;
        o.SenderCardCode = m.SenderCardCode;
        o.RecipientCardCode = m.RecipientCardCode;
        o.StartMunicipalityCode = m.StartMunicipalityCode;
        o.StartState = m.StartState;
        o.EndMunicipalityCode = m.EndMunicipalityCode;
        o.EndState = m.EndState;
        o.ItemCode = m.ItemCode;
        o.ItemName = n.ItemName;
        o.ContractedWeightKg = m.ContractedWeightKg;
        o.PriceMethod = m.PriceMethod;
        o.UnitPrice = m.UnitPrice;
        o.ValidFrom = m.ValidFrom;
        o.ValidTo = m.ValidTo;
        o.PaymentConditionCode = m.PaymentConditionCode;
        o.UsageCode = m.UsageCode;
        o.Observation = m.Observation;
    }
}
