using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageAddresses;

public class StorageAddressesCreateService(
    IUnitOfWork db,
    DocNumberSequenceService numberSequenceService,
    IBusinessPartnerService  businessPartnerService,
    IItemService itemService,
    IWarehouseService warehouseService,
    IWarehouseComplementService warehouseComplementService,
    ILogger<StorageAddressesCreateService> logger)
{
    public async Task<StorageAddress> ExecuteAsync(StorageAddress entity, string userName)
    {
        // Natureza Transbordo só existe em armazém próprio (GAC-1181 fase 2): sem porta de saída
        // documentada, o lote ficaria com mercadoria que nenhum fluxo de terceiro sabe liberar.
        // Validação antes de qualquer escrita: este serviço não abre transação.
        if (entity.Nature == StorageAddressNature.Transshipment)
        {
            var complement = await warehouseComplementService.GetAsync(entity.WarehouseCode);

            if (complement?.IsOwn != true)
            {
                throw new ApplicationException(
                    $"O armazém {entity.WarehouseCode} não é armazém próprio. " +
                    "Escolha um armazém próprio para o lote de Transbordo, ou deixe a natureza Regular.");
            }
        }

        entity.DocNumberKey ??= await numberSequenceService.GetKeyByTransactionCode(TransactionCode.StorageAddress);
        
        try
        {
            entity.Code = await numberSequenceService.GetDocNumber((Guid) entity.DocNumberKey);
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.WarehouseName = (await warehouseService.GetByIdAsync(entity.WarehouseCode))?.Name;
            entity.TransactionOrigin = TransactionCode.StorageAddress;
            
            await db.Context.StorageAddresses.AddAsync(entity);
            await db.SaveChangesAsync();
            
            return entity;
        }
        catch (Exception ex)
        {
            // Sem RollbackAsync aqui: não há BeginTransactionAsync, então a chamada era
            // no-op e só passava a falsa impressão de fluxo transacional. O único
            // SaveChangesAsync acima já é atômico por si. Se um dia este método ganhar
            // um segundo SaveChanges, abrir transação de verdade (Begin/Commit), como em
            // SalesInvoicesConfirmService.
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }  
}