using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Dtos;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsCompletedService(
    AppDbContext db,
    StorageTransactionsCreateService storageTransactionsCreateService
    )
{
    public async Task ExecuteAsync(Guid key, WeighingTicketsCompletedDto dto, string userName)
    {
        var ticket = await db.WeighingTickets
            .Where(x => x.Stage == WeighingTicketStage.ReadyForCompleting)
            .Include(x => x.QualityInspections)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                     throw new NotFoundException("Weighing ticket not found.");

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
        
        var storageAddress = await db.StorageAddresses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == dto.StorageAddressKey) ??
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
        
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            ticket.Status = WeighingTicketStatus.Complete;
            ticket.Stage = WeighingTicketStage.Completed;
           
            var st = new StorageTransaction
            {
                StorageAddressKey = dto.StorageAddressKey,
                TransactionDate = DateTime.Now.Date,
                TransactionTime = DateTime.Now.TimeOfDay.ToString(),
                TransactionType = ticket.Type == WeighingTicketType.Receipt
                    ? StorageTransactionType.Receipt
                    : StorageTransactionType.Shipment,
                TransactionStatus = StorageTransactionsStatus.Pending,
                GrossWeight = ticket.GrossWeight,
                TransactionOrigin = TransactionCode.WeighingTicket,
                TruckCode = ticket.TruckCode,
                TruckDriverCode = ticket.TruckDriverCode,
                WeighingTicketKey = ticket.Key
            };
                
            await storageTransactionsCreateService.ExecuteAsync(st, userName, TransactionCode.WeighingTicket);

            await AssingQualityInspection(st, ticket);
            
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            await transaction.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }

    private async Task AssingQualityInspection(StorageTransaction st, WeighingTicket ticket)
    {
        var attribs = await db.QualityAttribs
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
        var warehouse = db.WhareHouses
            .AsNoTracking()
            .Select(x => new {x.Code, x.Type})
            .FirstOrDefault(x => x.Code == sa.WarehouseCode) ??
                        throw new NotFoundException("Warehouse not found.");

        return warehouse.Type == WarehouseType.Owner;
    }
}