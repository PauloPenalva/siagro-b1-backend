using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.StorageAddresses;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsCompletedService(
    IUnitOfWork db,
    StorageTransactionsCreateService storageTransactionsCreateService,
    StorageAddressesGetService  storageAddressesGetService,
    ILogger<WeighingTicketsCompletedService> logger
    )
{
    public async Task ExecuteAsync(Guid key, WeighingTicket ticket, string userName)
    {
        await Validate(ticket);
        
        var existingTicket = await db.Context.WeighingTickets
            .Include(x => x.QualityInspections)                  
            .Where(x => x.Stage == WeighingTicketStage.ReadyForCompleting)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                     throw new NotFoundException("Weighing ticket not found.");

        var storageAddress = await storageAddressesGetService.GetByIdAsync(ticket.StorageAddressCode);
        if (storageAddress == null)
            throw new NotFoundException("Storage address not found.");
            
        try
        {
            await db.BeginTransactionAsync();
            db.Context.Entry(existingTicket).CurrentValues.SetValues(ticket);
            
            UpdateQualityInspections(existingTicket, ticket.QualityInspections);
            
            existingTicket.Status = WeighingTicketStatus.Complete;
            existingTicket.Stage = WeighingTicketStage.Completed;
            
            await db.SaveChangesAsync();
            
            var st = new StorageTransaction
            {
                StorageAddressCode = existingTicket.StorageAddressCode,
                TransactionDate = DateTime.Now.Date,
                TransactionTime = DateTime.Now.TimeOfDay.ToString(),
                TransactionType = existingTicket.Type == WeighingTicketType.Receipt
                    ? StorageTransactionType.Receipt
                    : StorageTransactionType.Shipment,
                TransactionStatus = StorageTransactionsStatus.Pending,
                GrossWeight = existingTicket.GrossWeight,
                TransactionOrigin = TransactionCode.WeighingTicket,
                TruckCode = existingTicket.TruckCode,
                TruckDriverCode = existingTicket.TruckDriverCode,
                WeighingTicketKey = existingTicket.Key,
                CardCode = existingTicket.CardCode,
                ItemCode = existingTicket.ItemCode,
                UnitOfMeasureCode = "KG",
                NetWeight = existingTicket.GrossWeight,
                WarehouseCode = storageAddress.WarehouseCode,
                BranchCode = existingTicket.BranchCode,
            };
           
            foreach (var inspection in ticket.QualityInspections)
            {
                st.QualityInspections.Add(new StorageTransactionQualityInspection
                {
                    Value = inspection.Value,
                    QualityAttribCode = inspection.QualityAttribCode,
                    StorageTransaction = st,
                });
            }
            
            await storageTransactionsCreateService.ExecuteAsync(st, userName, TransactionCode.WeighingTicket);
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e.Message, e);
            await db.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }
    
    private void UpdateQualityInspections(
        WeighingTicket existingTicket, 
        ICollection<QualityInspection> updatedInspections)
    {
        // Remover inspeções que não estão mais na lista atualizada
        var inspectionsToRemove = existingTicket.QualityInspections
            .Where(ei => !updatedInspections.Any(ui => ui.Key == ei.Key))
            .ToList();
    
        foreach (var inspection in inspectionsToRemove)
        {
            db.Context.QualityInspections.Remove(inspection);
        }
    
        // Atualizar/Adicionar inspeções
        foreach (var updatedInspection in updatedInspections)
        {
            var existingInspection = existingTicket.QualityInspections
                .FirstOrDefault(ei => ei.Key == updatedInspection.Key);
        
            if (existingInspection != null)
            {
                // Atualizar inspeção existente
                db.Context.Entry(existingInspection).CurrentValues.SetValues(updatedInspection);
            }
            else
            {
                // Adicionar nova inspeção
                updatedInspection.WeighingTicketKey = existingTicket.Key; // Se houver FK
                existingTicket.QualityInspections.Add(updatedInspection);
            }
        }
    }

    private async Task Validate(WeighingTicket ticket)
    {   
        if (ticket.StorageAddressCode == null)
            throw new ApplicationException("Storage address code not informed.");
        
        if (ticket.Stage != WeighingTicketStage.ReadyForCompleting)
        {
            throw new ApplicationException("Invalid ticket stage.");
        }
        
        if (ticket.Type == WeighingTicketType.Receipt)
        {
            if (ticket.SecondWeighValue > ticket.FirstWeighValue)
            {
                throw new ApplicationException("Invalid ticket second weigh value.");
            }
        }
        
        if (ticket.Type == WeighingTicketType.Shipment)
        {
            if (ticket.FirstWeighValue > ticket.SecondWeighValue)
            {
                throw new ApplicationException("Invalid ticket first weigh value.");
            }
        }
        
        var storageAddress = await db.Context.StorageAddresses
                                 .AsNoTracking()
                                 .FirstOrDefaultAsync(x => x.Code == ticket.StorageAddressCode) ??
                             throw new NotFoundException("Storage address not found.");
        
        if (storageAddress.CardCode != ticket.CardCode)
        {
            throw new ApplicationException("Invalid ticket business partner.");
        }
        
        
        if (storageAddress.ItemCode != ticket.ItemCode)
        {
            throw new ApplicationException("Invalid ticket product.");
        }
        
        if (ticket.Type == WeighingTicketType.Shipment && ticket.GrossWeight > storageAddress.Balance)
        {
            if (!IsWarehouseOwner(storageAddress))
                throw new ApplicationException("The total shipped exceeds the available balance in the storage address.");
        }
    }

    private async Task AssingQualityInspection(StorageTransaction st, WeighingTicket ticket)
    {
        var attribs = await db.Context.QualityAttribs
            .AsNoTracking()
            .Where(x => !x.Disabled)
            .OrderBy(x => x.Code)
            .ToListAsync();

        attribs.ForEach(attrib =>
        {
            st.QualityInspections.Add(new StorageTransactionQualityInspection
            {
                StorageTransactionKey = st.Key,
                QualityAttribCode = attrib.Code,
                Value = ticket.QualityInspections
                    .Where(x => x.QualityAttribCode == attrib.Code)
                    .Select(x => x.Value)
                    .FirstOrDefault(),
            });
        });
    }

    private bool IsWarehouseOwner(StorageAddress sa)
    {
        var warehouse = db.Context.WhareHouses
            .AsNoTracking()
            .Select(x => new {x.Code, x.Type})
            .FirstOrDefault(x => x.Code == sa.WarehouseCode) ??
                        throw new NotFoundException("Warehouse not found.");

        return warehouse.Type == WarehouseType.Owner;
    }
}