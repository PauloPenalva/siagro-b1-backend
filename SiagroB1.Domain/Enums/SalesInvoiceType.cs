namespace SiagroB1.Domain.Enums;

public enum SalesInvoiceType
{
    Normal = 0,
    Return = 1, // sales return - devolução/recusa
    IcmsComplement = 2, // complemento de ICMS: nota só de valor, vinculada à linha da saída original
}