using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SiagroB1.Infra.Context;

/// <summary>
/// Carimbos gravados em UTC voltam do SQL Server com Kind=Unspecified, e o OData os trata como hora local (a tela
/// soma 3 h). Estes conversores só marcam o Kind=Utc na leitura; não mudam o tipo da coluna (sem migration).
/// </summary>
public static class UtcDateTimeConverters
{
    public static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static readonly ValueConverter<DateTime, DateTime> Required = new(v => v, v => AsUtc(v));

    public static readonly ValueConverter<DateTime?, DateTime?> Optional =
        new(v => v, v => v.HasValue ? AsUtc(v.Value) : v);
}
