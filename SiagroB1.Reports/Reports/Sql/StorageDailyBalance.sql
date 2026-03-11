WITH ChargeSummary AS
         (
             SELECT
                 C.StorageAddressCode,
                 C.PeriodEnd AS BalanceDate,
                 SUM(CASE WHEN C.ChargeType = 1 THEN C.TotalAmount ELSE 0 END) AS StorageCost
             FROM STORAGE_CHARGES C
             WHERE C.StorageAddressCode = @StorageAddressCode
               AND C.PeriodEnd BETWEEN @DateFrom AND @DateTo
             GROUP BY
                 C.StorageAddressCode,
                 C.PeriodEnd
         )
SELECT
    B.BalanceDate                              AS [Data],
    B.OpeningBalance                           AS [SaldoAnterior],
    B.ReceiptQty                               AS [Entradas],
    B.ShipmentQty                              AS [Saidas],
    B.TechnicalLossQty                         AS [QuebraTecnica],
    B.ClosingBalance                           AS [Saldo],
    ISNULL(S.StorageCost, 0)                   AS [CustoArmazenagem]
FROM STORAGE_DAILY_BALANCES B
         LEFT JOIN ChargeSummary S
                   ON S.StorageAddressCode = B.StorageAddressCode
                       AND S.BalanceDate = B.BalanceDate
WHERE B.StorageAddressCode = @StorageAddressCode
  AND B.BalanceDate BETWEEN @DateFrom AND @DateTo
ORDER BY B.BalanceDate;