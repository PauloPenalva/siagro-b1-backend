using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsCompletedService(
    IUnitOfWork db,
    StorageTransactionsCreateService storageTransactionsCreateService
    )
{
    public async Task ExecuteAsync(Guid key, WeighingTicket ticket, string userName)
    {
        var existingTicket = await db.Context.WeighingTickets
            .Where(x => x.Stage == WeighingTicketStage.ReadyForCompleting)
            .Include(x => x.StorageAddress)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                     throw new NotFoundException("Weighing ticket not found.");

        await Validate(ticket, existingTicket);
        
        try
        {
            await db.BeginTransactionAsync();
            db.Context.Entry(existingTicket).CurrentValues.SetValues(ticket);
            db.Context.Entry(existingTicket).Property("RowId").IsModified = false;
            db.Context.Entry(existingTicket).Property("Key").IsModified = false;

            existingTicket.Status = WeighingTicketStatus.Complete;
            existingTicket.Stage = WeighingTicketStage.Completed;

            UpdateQualityInspections(existingTicket, ticket.QualityInspections);
            
            await db.SaveChangesAsync();
            
            var st = new StorageTransaction
            {
                StorageAddressCode = ticket.StorageAddressCode,
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
                WarehouseCode = ticket.StorageAddress?.WarehouseCode,
            };
            
            foreach (var inspection in existingTicket.QualityInspections)
            {
                st.QualityInspections.Add(new StorageTransactionQualityInspection
                {
                    Key = Guid.NewGuid(),
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
                db.Context.Entry(existingInspection).Property("RowId").IsModified = false;
                db.Context.Entry(existingInspection).Property("Key").IsModified = false;
            }
            else
            {
                // Adicionar nova inspeção
                updatedInspection.WeighingTicketKey = existingTicket.Key; // Se houver FK
                existingTicket.QualityInspections.Add(updatedInspection);
            }
        }
    }

    private async Task Validate(WeighingTicket weighingTicket, WeighingTicket ticket)
    {
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
                                 .FirstOrDefaultAsync(x => x.Code == weighingTicket.StorageAddressCode) ??
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