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
/// aninhada estoura no SQL Server. Por isso é retomável: a chave do contrato vai para a boleta logo após o Create,
/// e cada chamada segue o status ATUAL do contrato no banco.
/// </summary>
public class MarketTradeTicketContractService(
    AppDbContext context,
    PurchaseContractsCreateService createService,
    PurchaseContractsSendApprovalService sendApprovalService,
    PurchaseContractsApprovalService approvalService)
{
    public async Task GenerateAsync(MarketTradeTicket ticket, string approvedBy, CancellationToken ct)
    {
        if (ticket.PurchaseContractKey is null)
        {
            var settings = await context.MarketTradingSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            var contract = await createService.ExecuteAsync(BuildContract(ticket, settings), ticket.CreatedBy ?? approvedBy);
            ticket.PurchaseContractKey = contract.Key;
            ticket.PurchaseContractCode = contract.Code;
            // Grava já: se um passo seguinte falhar, a próxima tentativa continua deste contrato, sem criar outro.
            try
            {
                await context.SaveChangesAsync(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new BusinessException(
                    $"O contrato {contract.Code} foi criado, mas não foi possível vinculá-lo à boleta nº {ticket.Id} ({e.Message}). " +
                    "Exclua ou cancele esse contrato antes de aprovar a boleta de novo.");
            }
        }

        var key = ticket.PurchaseContractKey.Value;
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
                        $"O contrato {ticket.PurchaseContractCode} gerado pela boleta nº {ticket.Id} não existe mais ou está " +
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
        UnitOfMeasureCode = settings?.ContractUnitOfMeasureCode
                            ?? throw new BusinessException("Informe a unidade de medida do contrato nos parâmetros do Mercado."),
        FreightUmCode = settings.ContractFreightUmCode
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
