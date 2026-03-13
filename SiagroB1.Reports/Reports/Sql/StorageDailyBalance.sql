WITH ChargeSummary AS
         (
             SELECT
                 C.StorageAddressCode,
                 CAST(C.PeriodEnd AS date) AS BalanceDate,
                 SUM(CASE WHEN C.ChargeType = 1 THEN C.TotalAmount ELSE 0 END) AS StorageCost
             FROM STORAGE_CHARGES C
             WHERE C.StorageAddressCode = @StorageAddressCode
               AND C.PeriodEnd >= CAST(@DateFrom AS date)
               AND C.PeriodEnd < DATEADD(day, 1, CAST(@DateTo AS date))
             GROUP BY
                 C.StorageAddressCode,
                 CAST(C.PeriodEnd AS date)
         )
SELECT
    CAST(B.BalanceDate AS date)                     AS [Data],
    B.OpeningBalance                                AS [SaldoAnterior],
    B.ReceiptQty                                    AS [Entradas],
    B.ShipmentQty                                   AS [Saidas],
    B.OpeningBalance + B.ReceiptQty - B.ShipmentQty AS [Base],
    B.TechnicalLossQty                              AS [QuebraTecnica],
    B.ClosingBalance                                AS [Saldo],
    ISNULL(S.StorageCost, 0)                        AS [CustoArmazenagem]
FROM STORAGE_DAILY_BALANCES B
         LEFT JOIN ChargeSummary S
                   ON S.StorageAddressCode = B.StorageAddressCode
                       AND S.BalanceDate = CAST(B.BalanceDate AS date)
WHERE B.StorageAddressCode = @StorageAddressCode
  AND B.BalanceDate >= CAST(@DateFrom AS date)
  AND B.BalanceDate < DATEADD(day, 1, CAST(@DateTo AS date))
ORDER BY B.BalanceDate;