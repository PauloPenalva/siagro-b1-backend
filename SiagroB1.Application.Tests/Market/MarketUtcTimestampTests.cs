using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Market;

public class MarketUtcTimestampTests
{
    [Fact]
    public void Converter_MarksUtcOnRead_KeepsInstant()
    {
        var raw = new DateTime(2026, 11, 6, 4, 51, 57, DateTimeKind.Unspecified);
        var read = (DateTime)UtcDateTimeConverters.Required.ConvertFromProvider(raw)!;
        Assert.Equal(DateTimeKind.Utc, read.Kind);
        Assert.Equal(raw.Ticks, read.Ticks);

        var optional = (DateTime?)UtcDateTimeConverters.Optional.ConvertFromProvider(raw);
        Assert.Equal(DateTimeKind.Utc, optional!.Value.Kind);
        Assert.Null(UtcDateTimeConverters.Optional.ConvertFromProvider(null));
        Assert.Equal(raw, UtcDateTimeConverters.Required.ConvertToProvider(raw));
    }

    [Theory]
    [InlineData(typeof(MarketFreightRate), nameof(MarketFreightRate.CreatedAt))]
    [InlineData(typeof(MarketPriceTable), nameof(MarketPriceTable.UpdatedAt))]
    [InlineData(typeof(MarketTradeTicket), nameof(MarketTradeTicket.CreatedAt))]
    [InlineData(typeof(MarketTradeTicket), nameof(MarketTradeTicket.ApprovedAt))]
    [InlineData(typeof(MarketTradeTicket), nameof(MarketTradeTicket.BoardApprovedAt))]
    [InlineData(typeof(MarketTradeTicket), nameof(MarketTradeTicket.RejectedAt))]
    [InlineData(typeof(MarketTradeTicket), nameof(MarketTradeTicket.CanceledAt))]
    [InlineData(typeof(MarketTradeTicket), nameof(MarketTradeTicket.QuotedAt))]
    [InlineData(typeof(MarketFuturesTrade), nameof(MarketFuturesTrade.TradedAt))]
    [InlineData(typeof(MarketFuturesTrade), nameof(MarketFuturesTrade.CreatedAt))]
    [InlineData(typeof(MarketFuturesTrade), nameof(MarketFuturesTrade.UpdatedAt))]
    [InlineData(typeof(MarketFuturesTrade), nameof(MarketFuturesTrade.ConfirmedAt))]
    [InlineData(typeof(MarketFxHedge), nameof(MarketFxHedge.TradedAt))]
    [InlineData(typeof(MarketFxHedge), nameof(MarketFxHedge.CreatedAt))]
    [InlineData(typeof(MarketFxHedge), nameof(MarketFxHedge.UpdatedAt))]
    [InlineData(typeof(MarketFxHedge), nameof(MarketFxHedge.ConfirmedAt))]
    [InlineData(typeof(MarketFxHedge), nameof(MarketFxHedge.SettledAt))]
    [InlineData(typeof(MarketEfp), nameof(MarketEfp.CreatedAt))]
    [InlineData(typeof(MarketEfp), nameof(MarketEfp.CanceledAt))]
    [InlineData(typeof(MarketBrokerStatement), nameof(MarketBrokerStatement.CreatedAt))]
    public void Model_UtcStamps_HaveConverter(Type entity, string property)
    {
        var db = TestDb.CreateUnitOfWork();
        var converter = db.Context.Model.FindEntityType(entity)!.FindProperty(property)!.GetValueConverter();
        Assert.True(converter == UtcDateTimeConverters.Required || converter == UtcDateTimeConverters.Optional);
    }

    [Theory]
    [InlineData(typeof(MarketTradeTicket), nameof(MarketTradeTicket.PaymentDate))]
    [InlineData(typeof(MarketPriceTableLine), nameof(MarketPriceTableLine.PaymentDate))]
    [InlineData(typeof(MarketFreightRate), nameof(MarketFreightRate.ValidFrom))]
    [InlineData(typeof(MarketFuturesTrade), nameof(MarketFuturesTrade.TradeDate))]
    [InlineData(typeof(MarketFxHedge), nameof(MarketFxHedge.SettlementDate))]
    [InlineData(typeof(MarketBrokerStatement), nameof(MarketBrokerStatement.Date))]
    public void Model_BusinessDates_Untouched(Type entity, string property)
    {
        var db = TestDb.CreateUnitOfWork();
        Assert.Null(db.Context.Model.FindEntityType(entity)!.FindProperty(property)!.GetValueConverter());
    }
}
