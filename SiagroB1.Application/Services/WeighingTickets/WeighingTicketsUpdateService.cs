using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Royalties;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WeighingTickets;

public class WeighingTicketsUpdateService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService,
    IItemService itemService,
    ILogger<WeighingTicketsUpdateService> logger)
{
    public async Task<WeighingTicket?> ExecuteAsync(Guid key, WeighingTicket entity, string userName)
    {
        var existingEntity = await db.Context.WeighingTickets
                                 .FirstOrDefaultAsync(tc => tc.Key == key) ?? 
                             throw new KeyNotFoundException("Entity not found.");

        // O controller aplica o Delta na entidade rastreada antes de chamar aqui, então o Status
        // "atual" já é o do PATCH: o que estava gravado só existe no OriginalValue. Sem estas duas
        // travas o PATCH cancelaria (ou reabriria) o ticket por fora da ação, sem a checagem do
        // papel ADMIN do WeighingTicketsCancelService.
        var storedStatus = db.Context.Entry(existingEntity).Property(x => x.Status).OriginalValue;

        if (storedStatus == WeighingTicketStatus.Cancelled)
            throw new ApplicationException("Ticket de pesagem cancelado não pode ser alterado.");

        if (entity.Status == WeighingTicketStatus.Cancelled)
            throw new ApplicationException(
                "O ticket de pesagem só pode ser cancelado pela ação Cancelar Ticket.");

        if (existingEntity.Status == WeighingTicketStatus.Complete)
        {
            throw new ApplicationException("Weighing ticket is in complete status.");
        }
        
        try
        {
            db.Context.Entry(existingEntity).CurrentValues.SetValues(entity);

            // Depois do SetValues e em `existingEntity`: as colunas desnormalizadas
            // precisam acompanhar a troca do código, senão a tela mostra o nome antigo
            // ao lado do código novo na próxima leitura.
            existingEntity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            existingEntity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            existingEntity.RoyaltyTestCode =
                RoyaltyRules.NormalizeTestCode(existingEntity.RoyaltySituation, existingEntity.RoyaltyTestCode);
            // O value help grava a descrição só na tela (grupo null); o nome vem sempre do código.
            existingEntity.RoyaltyOriginName = string.IsNullOrWhiteSpace(existingEntity.RoyaltyOriginCode)
                ? null
                : (await businessPartnerService.GetByIdAsync(existingEntity.RoyaltyOriginCode))?.CardName;
            existingEntity.RecalculateFreightPrice();

            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new ApplicationException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
}