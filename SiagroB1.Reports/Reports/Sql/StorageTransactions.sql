SELECT
    B.ShortName          AS Filial,
    ST.Code              AS Romaneio,
    ST.InvoiceNumber     AS Documento,
    CASE
        WHEN ST.TransactionStatus = 0 THEN 'Pendente'
        WHEN ST.TransactionStatus = 1 THEN 'Confirmado'
        WHEN ST.TransactionStatus = 3 THEN 'Faturado'
    END AS Status,
    ST.CardName          AS ClienteNome,
    ST.CardCode          AS ClienteCodigo,
    SA.Code              AS LoteCodigo,
    SA.Description       AS LoteDescricao,
    ST.WarehouseCode     AS ArmazemCodigo,
    ST.ItemName          AS ProdutoNome,
    ST.TruckCode         AS Placa,
    ST.TransactionDate   AS Emissao,
    T.Code               AS Ticket,
    ST.GrossWeight       AS PesoBruto,

    COALESCE(Q.Umidade, 0)    AS Umidade,
    COALESCE(Q.Impurezas, 0)  AS Impurezas,
    COALESCE(Q.Avariados, 0)  AS Avariados,
    COALESCE(Q.Ardidos, 0)    AS Ardidos,
    COALESCE(Q.PH, 0)         AS PH,
    COALESCE(Q.FN, 0)         AS FN,

    ST.OthersDicount + ST.DryingDiscount + ST.CleaningDiscount AS Descontos,
    ST.NetWeight AS PesoLiquido,
    ST.DryingServicePrice + ST.CleaningServicePrice + ST.ShipmentPrice + ST.ReceiptServicePrice AS ValorServicos

FROM STORAGE_TRANSACTIONS ST
         LEFT JOIN STORAGE_ADDRESSES SA
                   ON SA.BranchCode = ST.BranchCode
                       AND SA.Code = ST.StorageAddressCode
         LEFT JOIN BRANCHS B
                   ON B.Code = ST.BranchCode
         LEFT JOIN WEIGHING_TICKETS T
                   ON T.[Key] = ST.WeighingTicketKey
    LEFT JOIN (
    SELECT
    StorageTransactionKey,
    MAX(CASE WHEN QualityAttribCode = '001' THEN Value END) AS Umidade,
    MAX(CASE WHEN QualityAttribCode = '002' THEN Value END) AS Impurezas,
    MAX(CASE WHEN QualityAttribCode = '003' THEN Value END) AS Avariados,
    MAX(CASE WHEN QualityAttribCode = '004' THEN Value END) AS Ardidos,
    MAX(CASE WHEN QualityAttribCode = '005' THEN Value END) AS PH,
    MAX(CASE WHEN QualityAttribCode = '006' THEN Value END) AS FN
    FROM STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS
    GROUP BY StorageTransactionKey
    ) Q
ON Q.StorageTransactionKey = ST.[Key]
WHERE
    ST.TransactionType = @TransactionType
  AND ST.TransactionStatus <> 2
  AND ST.TransactionDate >= @TransactionDateFrom
  AND ST.TransactionDate < DATEADD(DAY, 1, @TransactionDateTo)
  AND (@BranchCodeFrom IS NULL OR ISNULL(ST.BranchCode,'') >= @BranchCodeFrom)
  AND (@BranchCodeTo IS NULL OR ISNULL(ST.BranchCode,'') <= @BranchCodeTo)
  AND (@CardCodeFrom IS NULL OR ISNULL(ST.CardCode,'') >= @CardCodeFrom)
  AND (@CardCodeTo IS NULL OR ISNULL(ST.CardCode,'') <= @CardCodeTo)
  AND (@StorageAddressCodeFrom IS NULL OR ISNULL(ST.StorageAddressCode,'') >= @StorageAddressCodeFrom)
  AND (@StorageAddressCodeTo IS NULL OR ISNULL(ST.StorageAddressCode,'') <= @StorageAddressCodeTo)
  AND (@ItemCodeFrom IS NULL OR ISNULL(ST.ItemCode,'') >= @ItemCodeFrom)
  AND (@ItemCodeTo IS NULL OR ISNULL(ST.ItemCode,'') <= @ItemCodeTo)
  AND (@WarehouseCodeFrom IS NULL OR ISNULL(ST.WarehouseCode,'') >= @WarehouseCodeFrom)
  AND (@WarehouseCodeTo IS NULL OR ISNULL(ST.WarehouseCode,'') <= @WarehouseCodeTo)
  AND (@TruckCodeFrom IS NULL OR ISNULL(ST.TruckCode,'') >= @TruckCodeFrom)
  AND (@TruckCodeTo IS NULL OR ISNULL(ST.TruckCode,'') <= @TruckCodeTo)
ORDER BY ST.BranchCode, ST.RowId;