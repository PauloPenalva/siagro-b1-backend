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
                      COALESCE(SUM(
                          CASE
                              WHEN (ST.TransactionType IN (0, 6))
                                   AND (ST.TransactionStatus IN (1, 3))
                              THEN ST.NetWeight
                              ELSE 0
                          END
                      ), 0)
                      -
                      COALESCE(SUM(
                          CASE
                              WHEN (ST.TransactionType IN (1, 7, 4))
                                   AND (ST.TransactionStatus IN (1, 3))
                              THEN ST.NetWeight
                              ELSE 0
                          END
                      ), 0) AS Balance
                  FROM STORAGE_TRANSACTIONS ST
                  WHERE ST.StorageAddressCode = @StorageAddressCode
                  """;

        return conn.QueryFirstOrDefault<decimal>(sql, new
        {
            StorageAddressCode = storageAddressCode
        });
    }
}