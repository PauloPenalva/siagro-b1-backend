using System.Linq.Expressions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.StorageAddresses;

/// <summary>
/// Quais lotes entram no cálculo diário (saldo, armazenagem e quebra técnica) de uma data.
/// Lote aberto sempre entra; lote encerrado entra só até a data de encerramento, inclusive — assim o
/// reprocessamento de um período anterior ao encerramento continua recalculando o lote.
/// </summary>
public static class StorageAddressCalculationScope
{
    public static Expression<Func<StorageAddress, bool>> CalculatedOn(DateTime processingDate)
    {
        var date = processingDate.Date;

        return x => x.ClosingDate != null
            ? x.ClosingDate >= date
            : x.Status == StorageAddressStatus.Open;
    }
}
