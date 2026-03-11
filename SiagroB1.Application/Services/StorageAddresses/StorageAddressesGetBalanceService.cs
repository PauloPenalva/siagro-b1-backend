using System.Data;
using Dapper;

namespace SiagroB1.Application.Services.StorageAddresses;

public class StorageAddressesGetBalanceService(
        IDbConnection conn
    )
{
    public decimal GetBalance(string storageAddressCode)
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