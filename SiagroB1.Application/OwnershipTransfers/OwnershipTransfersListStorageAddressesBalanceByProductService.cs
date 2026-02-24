using System.Data;
using Dapper;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Application.OwnershipTransfers;

public class OwnershipTransfersListStorageAddressesBalanceByProductService(
        IDbConnection conn
    )
{
    public async Task<ICollection<StorageAddressBalanceDto>> ExecuteAsync(string itemCode, string? ignoreCode)
    {
        var sql = """
                    SELECT 
                            SA.BranchCode,
                            B.BranchName,
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
            var balance = GetBalance(item.Code);
            storageAddresses.Add(new StorageAddressBalanceDto
            {
                BranchCode = item.BranchCode,
                BranchName = item.BranchName,
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

    private decimal GetBalance(string storageAddressCode)
    {
        var sql = """
                  SELECT 
                       TotalIncoming - TotalOutgoing AS Balance
                  FROM (SELECT SUM(Incoming) AS TotalIncoming,
                               SUM(Outgoing) AS TotalOutgoing
                        FROM (SELECT CASE
                                         WHEN (ST.TransactionType = 0 OR ST.TransactionType = 6)
                                             AND (ST.TransactionStatus = 1 OR ST.TransactionStatus = 3)
                                             THEN ST.NetWeight
                                         ELSE 0
                                         END AS Incoming,
                                     CASE
                                         WHEN (ST.TransactionType = 1 OR ST.TransactionType = 7 OR ST.TransactionType = 4)
                                             AND (ST.TransactionStatus = 1 OR ST.TransactionStatus = 3)
                                             THEN ST.NetWeight
                                         ELSE 0
                                         END AS Outgoing
                              FROM STORAGE_TRANSACTIONS ST
                              WHERE ST.StorageAddressCode = @StorageAddressCode) AS CTE) AS TOTALS
                  """;

        return conn.QueryFirstOrDefault<decimal>(sql, new
        {
            StorageAddressCode = storageAddressCode
        });
    }
}