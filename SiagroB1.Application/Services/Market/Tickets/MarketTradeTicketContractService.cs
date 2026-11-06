using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Tickets;

/// <summary>
/// Gera o contrato de compra de preço fixo da boleta e o leva até Aprovado, compondo os serviços existentes
/// (Create → SendApproval → Approval). NÃO abre transação: o Create abre a dele no mesmo contexto e transação
/// aninhada estoura no SQL Server. A chave do contrato é PRÉ-ATRIBUÍDA e já está gravada na boleta antes daqui
/// (no "pedido de vez" da aprovação): contrato inexistente com essa chave → cria com ela; existente → segue pelo
/// status atual. Repetir a geração (em qualquer processo) reencontra o mesmo contrato ou bate na chave primária —
/// nunca cria um segundo.
/// </summary>
public class MarketTradeTicketContractService(
    AppDbContext context,
    PurchaseContractsCreateService createService,
    PurchaseContractsSendApprovalService sendApprovalService,
    PurchaseContractsApprovalService approvalService)
{
    /// <summary>Única unidade aceita nesta versão: a conversão da boleta para o contrato é fixa em kg.</summary>
    public const string ContractUnitOfMeasure = "KG";

    public const string ContractUnitMessage = "Nesta versão o contrato gerado pela boleta é sempre em KG.";

    public async Task GenerateAsync(MarketTradeTicket ticket, string approvedBy, CancellationToken ct)
    {
        var key = ticket.PurchaseContractKey
                  ?? throw new InvalidOperationException("A chave do contrato deve ser gravada na boleta antes da geração.");

        var existing = await context.PurchaseContracts.AsNoTracking()
            .Where(c => c.Key == key).Select(c => new { c.Code }).FirstOrDefaultAsync(ct);
        if (existing is null)
        {
            var settings = await context.MarketTradingSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            var entity = BuildContract(ticket, settings);
            entity.Key = key;
            var contract = await createService.ExecuteAsync(entity, ticket.CreatedBy ?? approvedBy);
            ticket.PurchaseContractCode = contract.Code;
        }
        else
        {
            ticket.PurchaseContractCode = existing.Code;
        }

        // No máximo Draft → InApproval → Approved; o limite evita laço se algum serviço não mudar o status.
        for (var step = 0; step < 3; step++)
        {
            var status = await context.PurchaseContracts.AsNoTracking()
                .Where(c => c.Key == key).Select(c => c.Status).FirstOrDefaultAsync(ct);
            switch (status)
            {
                case ContractStatus.Approved:
                    return;
                case ContractStatus.Draft:
                    await sendApprovalService.ExecuteAsync(key, approvedBy);
                    break;
                case ContractStatus.InApproval:
                    await approvalService.ExecuteAsync(key, $"Aprovado pela boleta nº {ticket.Id}", approvedBy);
                    break;
                default:
                    throw new BusinessException(
                        $"O contrato {ticket.PurchaseContractCode} gerado pela boleta nº {ticket.Id} está " +
                        "cancelado, rejeitado ou encerrado; não pode ser usado.");
            }
        }

        throw new BusinessException($"O contrato {ticket.PurchaseContractCode} não chegou a Aprovado.");
    }

    private static PurchaseContract BuildContract(MarketTradeTicket t, MarketTradingSettings? settings) => new()
    {
        Type = ContractType.Fixed,
        MarketType = MarketType.Internal,
        CardCode = t.CardCode!,
        ItemCode = t.ItemCode!,
        UnitOfMeasureCode = string.Equals(settings?.ContractUnitOfMeasureCode?.Trim(), ContractUnitOfMeasure,
            StringComparison.OrdinalIgnoreCase)
            ? ContractUnitOfMeasure
            : throw new BusinessException(ContractUnitMessage),
        FreightUmCode = settings!.ContractFreightUmCode
                        ?? throw new BusinessException("Informe a unidade de medida do frete nos parâmetros do Mercado."),
        HarvestSeasonCode = t.HarvestSeasonCode,
        DeliveryLocationCode = t.WarehouseCode!,
        AgentCode = t.AgentCode,
        BranchCode = t.BranchCode,
        LogisticRegionCode = t.LogisticRegionCode,
        FreightTerms = FreightTerms.Fob,
        FreightCostStandard = 0,
        DeliveryStartDate = t.DeliveryStartDate!.Value,
        DeliveryEndDate = t.DeliveryEndDate!.Value,
        TotalVolume = t.VolumeKg,
        StandardPrice = Math.Round(t.NegotiatedPrice / 60m, 6, MidpointRounding.AwayFromZero),
        StandardCurrency = CurrencyType.Brl,
        StandardCashFlowDate = t.PaymentDate,
        Comments = $"Gerado pela boleta nº {t.Id}",
    };
}
