SELECT
    B.ShortName          AS Filial,
    ST.Code              AS Romaneio,
    ST.InvoiceNumber     AS Documento,
    ST.TransactionStatus AS Status,
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

    COALESCE(MAX(CASE WHEN Q.QualityAttribCode = '001' THEN Q.Value END), 0) AS Umidade,
    COALESCE(MAX(CASE WHEN Q.QualityAttribCode = '002' THEN Q.Value END), 0) AS Impurezas,
    COALESCE(MAX(CASE WHEN Q.QualityAttribCode = '003' THEN Q.Value END), 0) AS Avariados,
    COALESCE(MAX(CASE WHEN Q.QualityAttribCode = '004' THEN Q.Value END), 0) AS Ardidos,
    COALESCE(MAX(CASE WHEN Q.QualityAttribCode = '005' THEN Q.Value END), 0) AS PH,
    COALESCE(MAX(CASE WHEN Q.QualityAttribCode = '006' THEN Q.Value END), 0) AS FN,

    ST.OthersDicount   AS Descontos,
    ST.NetWeight       AS PesoLiquido
FROM STORAGE_TRANSACTIONS ST
         LEFT JOIN STORAGE_ADDRESSES SA
                   ON SA.BranchCode = ST.BranchCode
                       AND SA.Code = ST.StorageAddressCode
         LEFT JOIN BRANCHS B
                   ON B.Code = ST.BranchCode
         LEFT JOIN WEIGHING_TICKETS T
                   ON T.[Key] = ST.WeighingTicketKey
    LEFT JOIN STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS Q
ON Q.StorageTransactionKey = ST.[Key]
WHERE ST.WeighingTicketKey IS NOT NULL
  AND ST.TransactionType = 0
  AND ST.TransactionStatus <> 2
GROUP BY
    ST.RowId,
    ST.BranchCode,
    B.ShortName,
    ST.Code,
    ST.InvoiceNumber,
    ST.TransactionStatus,
    ST.CardName,
    ST.CardCode,
    SA.Code,
    SA.Description,
    ST.WarehouseCode,
    ST.ItemName,
    ST.TruckCode,
    ST.TransactionDate,
    T.Code,
    ST.GrossWeight,
    ST.OthersDicount,
    ST.NetWeight
ORDER BY ST.BranchCode, ST.RowId;
