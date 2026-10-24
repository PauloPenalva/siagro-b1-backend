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

        if (await partners.GetByIdAsync(m.TakerCardCode) is null)
            throw new DefaultException("Tomador não encontrado.");
        if (await items.GetByIdAsync(m.ItemCode) is null)
            throw new DefaultException("Produto não encontrado.");

        if (m.Key is null) return await CreateAsync(m, user);

        var order = await db.Context.FreightOrders.FirstOrDefaultAsync(o => o.Key == m.Key)
            ?? throw new DefaultException("Ordem de frete não encontrada.");
        if (order.Status is FreightOrderStatus.Completed or FreightOrderStatus.Cancelled)
            throw new DefaultException("Ordem encerrada ou cancelada não pode ser alterada.");

        var hasAuthorized = await db.Context.TransportDocuments
            .AnyAsync(d => d.FreightOrderKey == order.Key && d.Status == CteStatus.Authorized);
        if (hasAuthorized && (order.TakerCardCode != m.TakerCardCode || order.ItemCode != m.ItemCode))
            throw new DefaultException("Ordem com CT-e autorizado: tomador e produto não podem ser alterados.");

        Apply(order, m);
        await db.SaveChangesAsync();
        // Mudar o volume contratado muda o saldo: a situação precisa acompanhar.
        await recalculate.RecalculateAsync(order.Key);
        return order;
    }

    private async Task<FreightOrder> CreateAsync(FreightOrderSaveModel m, string user)
    {
        // Número buscado antes de gravar (Dapper em conexão separada).
        var assignment = await docNumberSequence.AssignAsync(new DocNumberRequest(
            TransactionCode.FreightOrder, m.DocNumberKey, m.TypedCode, 20,
            c => db.Context.FreightOrders.AnyAsync(x => x.Code == c)));

        var order = new FreightOrder
        {
            BranchCode = m.BranchCode, Code = assignment.Code, DocNumberKey = assignment.DocNumberKey,
            TakerCardCode = m.TakerCardCode, StartMunicipalityCode = m.StartMunicipalityCode, StartState = m.StartState,
            EndMunicipalityCode = m.EndMunicipalityCode, EndState = m.EndState, ItemCode = m.ItemCode,
            ItemName = m.ItemName, CreatedBy = user, CreatedAt = DateTime.Now,
            Status = FreightOrderStatus.Open,
            OrderDate = m.OrderDate ?? DateTime.Today,
        };
        Apply(order, m);
        db.Context.FreightOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private static void Apply(FreightOrder o, FreightOrderSaveModel m)
    {
        o.TakerCardCode = m.TakerCardCode;
        o.SenderCardCode = m.SenderCardCode;
        o.RecipientCardCode = m.RecipientCardCode;
        o.StartMunicipalityCode = m.StartMunicipalityCode;
        o.StartState = m.StartState;
        o.EndMunicipalityCode = m.EndMunicipalityCode;
        o.EndState = m.EndState;
        o.ItemCode = m.ItemCode;
        o.ItemName = m.ItemName;
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
