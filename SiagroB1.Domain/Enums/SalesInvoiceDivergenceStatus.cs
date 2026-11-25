namespace SiagroB1.Domain.Enums;

/// <summary>Situação do registro de divergência da NF do SAP.</summary>
public enum SalesInvoiceDivergenceStatus
{
    /// <summary>Vigente: conta no saldo da carga.</summary>
    Open = 0,

    /// <summary>Anulada: deixa de contar e libera um novo registro para o documento.</summary>
    Voided = 1,
}
