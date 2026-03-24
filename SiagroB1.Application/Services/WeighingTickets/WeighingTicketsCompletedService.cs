using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WeighingTickets;

public class WeighingTicketsCompletedService(
    IUnitOfWork db,
    IBusinessPartnerService  businessPartnerService,
    IItemService itemService,
    StorageTransactionsCreateService stCreateService,
    StorageTransactionsConfirmedService  stConfirmedService,
    StorageAddressesGetService  storageAddressesGetService,
    IStringLocalizer<Resource> resource,
    ILogger<WeighingTicketsCompletedService> logger
    )
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        
        var existingTicket = await db.Context.WeighingTickets
            .Include(x => x.QualityInspections)                  
            .Where(x => x.Stage == WeighingTicketStage.ReadyForCompleting)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                     throw new NotFoundException("Ticket de pesagem não encontrado.");
        
        await Validate(existingTicket);

        if (existingTicket.StorageAddressCode == null)
            throw new ApplicationException("Lote de armazenagem não informado.");
            
        var storageAddress = await storageAddressesGetService.GetByIdAsync(existingTicket.StorageAddressCode);
        if (storageAddress == null)
            throw new NotFoundException("Lote de armazenagem não encontrado.");
            
        try
        {
            await db.BeginTransactionAsync();
            
            existingTicket.Status = WeighingTicketStatus.Complete;
            existingTicket.Stage = WeighingTicketStage.Completed;
            existingTicket.CardName = (await businessPartnerService.GetByIdAsync(existingTicket.CardCode))?.CardName;
            existingTicket.ItemName = (await itemService.GetByIdAsync(existingTicket.ItemCode))?.ItemName;
            
            await db.SaveChangesAsync();
            
            var st = new StorageTransaction
            {
                StorageAddressCode = existingTicket.StorageAddressCode,
                TransactionDate = existingTicket.Date,
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
                CardName = existingTicket.CardName,
                ItemCode = existingTicket.ItemCode,
                ItemName = existingTicket.ItemName,
                UnitOfMeasureCode = "KG",
                NetWeight = existingTicket.GrossWeight,
                WarehouseCode = storageAddress.WarehouseCode,
                BranchCode = existingTicket.BranchCode,
                ProcessingCostCode = storageAddress.ProcessingCostCode
            };
           
            foreach (var inspection in existingTicket.QualityInspections)
            {
                st.QualityInspections.Add(new StorageTransactionQualityInspection
                {
                    Value = inspection.Value,
                    QualityAttribCode = inspection.QualityAttribCode,
                    StorageTransaction = st,
                });
            }
            
            st = await stCreateService.ExecuteAsync(st, userName, TransactionCode.WeighingTicket);
            await stConfirmedService.ExecuteAsync(st.Key, userName);
            
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e.Message, e);
            await db.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }
   
    private async Task Validate(WeighingTicket ticket)
    {   
        if (ticket.Stage != WeighingTicketStage.ReadyForCompleting)
        {
            throw new ApplicationException("Ticket stage inválido.");
        }
        
        if (ticket.Type == WeighingTicketType.Receipt)
        {
            if (ticket.SecondWeighValue > ticket.FirstWeighValue)
            {
                //throw new ApplicationException("Invalid ticket second weigh value.");
            }
        }
        
        if (ticket.Type == WeighingTicketType.Shipment)
        {
            if (ticket.FirstWeighValue > ticket.SecondWeighValue)
            {
                //throw new ApplicationException("Invalid ticket first weigh value.");
            }
        }
        
        var storageAddress = await db.Context.StorageAddresses
                                 .Include(x => x.Transactions)
                                 .AsNoTracking()
                                 .FirstOrDefaultAsync(x => x.Code == ticket.StorageAddressCode) ??
                             throw new NotFoundException("Lote de armazenagem não encontrado.");
        
        if (storageAddress.CardCode != ticket.CardCode)
        {
            //Cliente informado no ticket é diferente do Cliente do lote de armazenagem
            throw new ApplicationException(resource["EXCEPTION_00001"]);
        }
        
        
        if (storageAddress.ItemCode != ticket.ItemCode)
        {
            //"Produto informado no ticket é diferente do produto informado no lote de armazenagem."
            throw new ApplicationException(resource["EXCEPTION_00002"]);
        }
        
        if (ticket.Type == WeighingTicketType.Shipment && ticket.GrossWeight > storageAddress.Balance)
        {
            if (!IsWarehouseOwner(storageAddress))
                //"O total embarcado excede o saldo disponivel no lote de armazenagem."
                throw new ApplicationException(resource["EXCEPTION_00003"]);
        }
    }
    
    private bool IsWarehouseOwner(StorageAddress sa)
    {
        return false;
    }
}