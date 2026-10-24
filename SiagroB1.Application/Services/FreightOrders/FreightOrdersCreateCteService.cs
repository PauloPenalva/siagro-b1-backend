using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.FreightOrders;

/// <summary>
/// Cria o rascunho de CT-e a partir da ordem de frete: copia partes, rota, produto, natureza e condição de pagamento.
/// Sem quantidades e sem valor: o operador informa o peso e a tela calcula o frete pelo método/preço da ordem.
/// </summary>
public class FreightOrdersCreateCteService(
    IUnitOfWork db,
    IConfiguration configuration,
    TransportDocumentDefaults defaults)
{
    public const string NotUsable = "Só é possível emitir CT-e para ordem Aberta ou Em execução.";
    public const string OutsideValidity = "Ordem de frete fora da vigência.";
    public const string OtherTaker = "A ordem de frete é de outro tomador.";

    public async Task<Guid> CreateDraftAsync(Guid orderKey, string user)
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(BranchCteSettingsService.StandaloneOnly);

        var order = await db.Context.FreightOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Key == orderKey)
            ?? throw new DefaultException("Ordem de frete não encontrada.");
        EnsureUsable(order);

        var parties = new List<TransportDocumentParty>();
        int takerRole;
        var taker = order.TakerCardCode.Trim();
        if (taker == order.SenderCardCode?.Trim()) takerRole = (int)CtePartyRole.Sender;
        else if (taker == order.RecipientCardCode?.Trim()) takerRole = (int)CtePartyRole.Recipient;
        else takerRole = (int)CtePartyRole.OtherTaker;

        if (order.SenderCardCode is { Length: > 0 } sender)
            parties.Add(await defaults.SnapshotPartyAsync(CtePartyRole.Sender, sender, null));
        if (order.RecipientCardCode is { Length: > 0 } recipient)
            parties.Add(await defaults.SnapshotPartyAsync(CtePartyRole.Recipient, recipient, null));
        if (takerRole == (int)CtePartyRole.OtherTaker)
            parties.Add(await defaults.SnapshotPartyAsync(CtePartyRole.OtherTaker, order.TakerCardCode, null));

        var doc = new TransportDocument
        {
            BranchCode = order.BranchCode,
            Series = await defaults.DefaultSeriesAsync(order.BranchCode),
            CreatedBy = user,
            FreightOrderKey = order.Key,
            TakerRole = takerRole,
            UsageCode = order.UsageCode,
            PaymentConditionCode = order.PaymentConditionCode,
            PredominantProduct = TransportDocumentDefaults.Cut(order.ItemName, 60),
            StartMunicipalityCode = order.StartMunicipalityCode,
            StartState = order.StartState,
            EndMunicipalityCode = order.EndMunicipalityCode,
            EndState = order.EndState,
        };
        foreach (var p in parties) doc.Parties.Add(p);

        db.Context.TransportDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc.Key;
    }

    /// <summary>Ordem Aberta ou Em execução e dentro da vigência (data de hoje; fim inclusivo, dia inteiro).</summary>
    internal static void EnsureUsable(FreightOrder order)
    {
        if (order.Status is not (FreightOrderStatus.Open or FreightOrderStatus.InExecution))
            throw new DefaultException(NotUsable);
        var today = DateTime.Today;
        if (today < order.ValidFrom.Date || (order.ValidTo is { } to && today > to.Date))
            throw new DefaultException(OutsideValidity);
    }
}
