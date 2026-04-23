using System.Data;
using Dapper;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Application.Services.OwnershipTransfers;

public class OwnershipTransfersListStorageAddressesBalanceByProductService(
        IDbConnection conn,
        StorageAddressesGetBalanceService getBalanceService
    )
{
    public async Task<ICollection<StorageAddressBalanceDto>> ExecuteAsync(string itemCode, string? ignoreCode)
    {
        var sql = """
                    SELECT 
                            SA.BranchCode,
                            B.ShortName,
                            SA.Code,
                            SA.CreationDate,
                            SA.Description,
                            SA.CardCode,
                            SA.CardName,
                            SA.WarehouseCode,
                            SA.WarehouseName,
                            SA.ItemCode,
                            SA.ItemName,
                            SA.UoM
                       FROM STORAGE_ADDRESSES SA
                  LEFT JOIN BRANCHS B
                         ON B.Code = SA.BranchCode
                      WHERE SA.ItemCode = @ItemCode
                        AND SA.Status = 0
                        AND (@IgnoreCode IS NULL OR SA.Code <> @IgnoreCode)
                  """;

        var results = await conn.QueryAsync<dynamic>(sql, new
        {
            ItemCode = itemCode,
            IgnoreCode = string.IsNullOrEmpty(ignoreCode) ? null : ignoreCode
        });

        var list = results.ToList();
        var storageAddresses = new List<StorageAddressBalanceDto>();

        foreach (var item in list)
        {
            var balance = getBalanceService.GetBalance(item.Code);
            storageAddresses.Add(new StorageAddressBalanceDto
            {
                BranchCode = item.BranchCode,
                BranchName = item.ShortName,
                Code = item.Code,
                CreationDate = item.CreationDate,
                Description = item.Description,
                CardCode = item.CardCode,
                CardName = item.CardName,
                WarehouseCode = item.WarehouseCode,
                WarehouseName = item.WarehouseName,
                ItemCode = item.ItemCode,
                ItemName = item.ItemName,
                Balance = balance,
                UoM =  item.UoM
            });
        }
        
        return storageAddresses;
    }
}