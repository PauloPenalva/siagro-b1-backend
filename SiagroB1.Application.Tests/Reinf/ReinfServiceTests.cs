using System.Xml;
using SiagroB1.Application.Services.Reinf;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Reinf;
using SiagroB1.Infra;
using Xunit;

namespace SiagroB1.Application.Tests.Reinf;

public class ReinfServiceTests
{
    private const string Root = "12345678";
    private const string Period = "2026-09";
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly UnitOfWork _db;
    private readonly FakeReinfClient _client = new();
    private int _receipt;
    private int _partners;

    public ReinfServiceTests()
    {
        _db = TestDb.CreateUnitOfWork(_dbName);
        var c = _db.Context;
        c.Branchs.Add(new Branch { Code = "01", BranchName = "Matriz", TaxId = "12345678000195" });
        c.Usages.Add(new Usage { Code = 1, Name = "Compra", RuralContributionMode = RuralContributionMode.Full });
        c.ReinfSettings.Add(new ReinfSettings { TaxIdRoot = Root, BranchCode = "01", RegistrationStatus = ReinfRegistrationStatus.Accepted });
        c.SaveChanges();
    }

    private ReinfService Service(string erp = "STANDALONE") => Service(_db.Context, erp);

    private ReinfService Service(SiagroB1.Infra.Context.AppDbContext ctx, string erp = "STANDALONE") => new(
        ctx, TestConfiguration.Erp(erp), new ReinfAcquisitionCalculator(ctx), _client,
        new FakeReinfCertificateProvider(), new ReinfEventIdGenerator(), new ReinfSchemaValidator(), new ReinfAppInfo("1.0"),
        new FixedTime());

    private PurchaseInvoiceItem Invoice(string card, decimal gross)
    {
        if (!_db.Context.BusinessPartners.Any(p => p.CardCode == card))
            _db.Context.BusinessPartners.Add(new BusinessPartner
            {
                CardCode = card, CardName = "Produtor " + card, TaxId = (11100000000L + ++_partners).ToString(),
                IsRuralProducer = true, RuralProducerType = RuralProducerType.Individual,
            });
        var inv = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = card, BranchCode = "01", IssueDate = new DateTime(2026, 9, 5), InvoiceStatus = InvoiceStatus.Confirmed };
        var item = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = inv.Key, UsageCode = 1, FunruralBase = gross,
            FunruralInssValue = Math.Round(gross * 0.012m, 2), FunruralRatValue = Math.Round(gross * 0.001m, 2), FunruralSenarValue = Math.Round(gross * 0.002m, 2),
        };
        _db.Context.PurchaseInvoices.Add(inv);
        _db.Context.PurchaseInvoicesItems.Add(item);
        _db.Context.SaveChanges();
        return item;
    }

    private void Return(PurchaseInvoiceItem origin, decimal gross)
    {
        var inv = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "P1", BranchCode = "01", IssueDate = new DateTime(2026, 10, 2), InvoiceStatus = InvoiceStatus.Confirmed, InvoiceType = PurchaseInvoiceType.Return, IsNfeReturn = true };
        _db.Context.PurchaseInvoices.Add(inv);
        _db.Context.PurchaseInvoicesItems.Add(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = inv.Key, PurchaseInvoiceItemOriginKey = origin.Key, FunruralBase = gross,
            FunruralInssValue = Math.Round(gross * 0.012m, 2), FunruralRatValue = Math.Round(gross * 0.001m, 2), FunruralSenarValue = Math.Round(gross * 0.002m, 2),
        });
        _db.Context.SaveChanges();
    }

    /// <summary>Programa a consulta para aceitar todos os eventos do lote.</summary>
    private void AcceptAllOnQuery(Guid batchKey) => _client.QueryResults.Enqueue(() =>
        new ReinfBatchResult(2, "Processado", null, [],
            _db.Context.ReinfEvents.Where(e => e.BatchKey == batchKey).ToList()
                .Select(e => new ReinfEventResult(e.EventId, true, $"1-55-1234-2026-{++_receipt}", [], "")).ToList(), "<r/>"));

    private async Task TransmitAndAccept()
    {
        var batches = await Service().TransmitAsync(Root, Period, "u", default);
        foreach (var b in batches) { AcceptAllOnQuery(b); await Service().QueryBatchAsync(b, default); }
    }

    private static XmlDocument Doc(string xml) { var d = new XmlDocument(); d.LoadXml(xml); return d; }
    private static IEnumerable<XmlElement> ByName(XmlDocument d, string name) => d.GetElementsByTagName(name).OfType<XmlElement>();

    [Fact]
    public async Task New_transmission_sends_one_batch_with_two_original_events()
    {
        Invoice("P1", 1000m);
        Invoice("P2", 500m);

        var batches = await Service().TransmitAsync(Root, Period, "u", default);

        var key = Assert.Single(batches);
        var sent = Doc(Assert.Single(_client.SentXml));
        Assert.Equal(2, ByName(sent, "evtAqProd").Count());
        Assert.All(ByName(sent, "indRetif"), e => Assert.Equal("1", e.InnerText));
        var batch = _db.Context.ReinfBatches.Single();
        Assert.Equal(ReinfBatchStatus.Sent, batch.Status);
        Assert.Equal("PROT-1", batch.Protocol);
        Assert.All(_db.Context.ReinfEvents.ToList(), e => { Assert.Equal(ReinfEventStatus.Sent, e.Status); Assert.Equal(key, e.BatchKey); });
        Assert.All((await Service().GetOverviewAsync(Root, Period, default)).Rows, r => Assert.Equal("Enviado", r.Situation));
    }

    [Fact]
    public async Task Processed_query_marks_accepted_with_receipt()
    {
        Invoice("P1", 1000m);
        await TransmitAndAccept();

        var e = _db.Context.ReinfEvents.Single();
        Assert.Equal(ReinfEventStatus.Accepted, e.Status);
        Assert.Equal("1-55-1234-2026-1", e.Receipt);
        var b = _db.Context.ReinfBatches.Single();
        Assert.Equal(ReinfBatchStatus.Processed, b.Status);
        Assert.Equal(1, b.Attempts);
        var row = Assert.Single((await Service().GetOverviewAsync(Root, Period, default)).Rows);
        Assert.Equal("Aceito", row.Situation);
        Assert.Empty(await Service().TransmitAsync(Root, Period, "u", default));
    }

    [Fact]
    public async Task Return_after_acceptance_is_outdated_and_rectifies_with_receipt()
    {
        var origin = Invoice("P1", 1000m);
        await TransmitAndAccept();
        Return(origin, 200m);

        Assert.Equal("Desatualizado", Assert.Single((await Service().GetOverviewAsync(Root, Period, default)).Rows).Situation);
        await Service().TransmitAsync(Root, Period, "u", default);

        var sent = Doc(_client.SentXml[^1]);
        Assert.Equal("2", ByName(sent, "indRetif").Single().InnerText);
        Assert.Equal("1-55-1234-2026-1", ByName(sent, "nrRecibo").Single().InnerText);

        var batch = _db.Context.ReinfBatches.OrderByDescending(b => b.SentAt).ThenByDescending(b => b.Protocol).First(b => b.Protocol == "PROT-2");
        AcceptAllOnQuery(batch.Key);
        await Service().QueryBatchAsync(batch.Key, default);
        Assert.Equal(ReinfEventStatus.Outdated, _db.Context.ReinfEvents.Single(e => e.Receipt == "1-55-1234-2026-1").Status);
        Assert.Equal("Aceito", Assert.Single((await Service().GetOverviewAsync(Root, Period, default)).Rows).Situation);
    }

    [Fact]
    public async Task Total_return_sends_R9000_with_receipt_and_marks_excluded()
    {
        var origin = Invoice("P1", 1000m);
        await TransmitAndAccept();
        Return(origin, 1000m);
        Assert.Equal("Excluir", Assert.Single((await Service().GetOverviewAsync(Root, Period, default)).Rows).Situation);

        await TransmitAndAccept();

        var sent = Doc(_client.SentXml[^1]);
        Assert.Single(ByName(sent, "evtExclusao"));
        Assert.Equal("R-2055", ByName(sent, "tpEvento").Single().InnerText);
        Assert.Equal("1-55-1234-2026-1", ByName(sent, "nrRecEvt").Single().InnerText);
        Assert.Equal(ReinfEventStatus.Excluded, _db.Context.ReinfEvents.Single(e => e.Type == ReinfEventType.R2055).Status);
        Assert.Equal("Excluído", Assert.Single((await Service().GetOverviewAsync(Root, Period, default)).Rows).Situation);
    }

    [Fact]
    public async Task Fifty_one_producers_make_two_batches()
    {
        for (var i = 0; i < 51; i++) Invoice($"P{i:00}", 100m + i);

        var batches = await Service().TransmitAsync(Root, Period, "u", default);

        Assert.Equal(2, batches.Count);
        Assert.Equal(50, ByName(Doc(_client.SentXml[0]), "evtAqProd").Count());
        Assert.Single(ByName(Doc(_client.SentXml[1]), "evtAqProd"));
    }

    [Fact]
    public async Task Transport_failure_keeps_events_pending_and_retry_resends()
    {
        Invoice("P1", 1000m);
        _client.SendResults.Enqueue(() => throw new ReinfTransportException(503, "indisponível"));

        await Service().TransmitAsync(Root, Period, "u", default);

        var b = _db.Context.ReinfBatches.Single();
        Assert.Equal(ReinfBatchStatus.Failed, b.Status);
        Assert.Contains("503", b.Error);
        Assert.Equal(ReinfEventStatus.Pending, _db.Context.ReinfEvents.Single().Status);
        Assert.Equal("Novo", Assert.Single((await Service().GetOverviewAsync(Root, Period, default)).Rows).Situation);

        await Service().TransmitAsync(Root, Period, "u", default);
        Assert.Equal(2, _client.SentXml.Count);
        var e = _db.Context.ReinfEvents.Single();
        Assert.Equal(ReinfEventStatus.Sent, e.Status);
    }

    [Fact]
    public async Task Query_still_processing_changes_nothing()
    {
        Invoice("P1", 1000m);
        var key = Assert.Single(await Service().TransmitAsync(Root, Period, "u", default));
        _client.QueryResults.Enqueue(() => new ReinfBatchResult(1, "Em processamento", null, [], [], "<r/>"));

        await Service().QueryBatchAsync(key, default);

        Assert.Equal(ReinfBatchStatus.Processing, _db.Context.ReinfBatches.Single().Status);
        Assert.Equal(ReinfEventStatus.Sent, _db.Context.ReinfEvents.Single().Status);
    }

    [Fact]
    public async Task Rejected_event_is_resent_as_new()
    {
        Invoice("P1", 1000m);
        var key = Assert.Single(await Service().TransmitAsync(Root, Period, "u", default));
        _client.QueryResults.Enqueue(() => new ReinfBatchResult(2, "Processado", null, [],
            [new ReinfEventResult(_db.Context.ReinfEvents.Single().EventId, false, null, ["MS0030 - erro"], "")], "<r/>"));
        await Service().QueryBatchAsync(key, default);

        var row = Assert.Single((await Service().GetOverviewAsync(Root, Period, default)).Rows);
        Assert.Equal("Rejeitado", row.Situation);
        Assert.Contains("MS0030", row.Occurrences);

        await Service().TransmitAsync(Root, Period, "u", default);
        Assert.Equal("1", ByName(Doc(_client.SentXml[^1]), "indRetif").Single().InnerText);
    }

    [Fact]
    public async Task Refused_batch_returns_events_to_pending()
    {
        Invoice("P1", 1000m);
        _client.SendResults.Enqueue(() => new ReinfBatchResult(99, "Lote inválido", null, ["MS1 - x"], [], "<r/>"));
        await Service().TransmitAsync(Root, Period, "u", default);

        Assert.Equal(ReinfBatchStatus.Refused, _db.Context.ReinfBatches.Single().Status);
        var e = _db.Context.ReinfEvents.Single();
        Assert.Equal(ReinfEventStatus.Pending, e.Status);
        Assert.Contains("MS1", e.Occurrences);
    }

    [Fact]
    public async Task Requires_accepted_registration()
    {
        _db.Context.ReinfSettings.Single().RegistrationStatus = ReinfRegistrationStatus.Sent;
        _db.Context.SaveChanges();
        var ex = await Assert.ThrowsAsync<BusinessException>(() => Service().TransmitAsync(Root, Period, "u", default));
        Assert.Equal("Envie e tenha aceito o R-1000 antes.", ex.Message);
    }

    [Fact]
    public async Task Closed_period_is_refused()
    {
        _db.Context.ReinfPeriods.Add(new ReinfPeriod { TaxIdRoot = Root, Period = Period, Status = ReinfPeriodStatus.Closed });
        _db.Context.SaveChanges();
        var ex = await Assert.ThrowsAsync<BusinessException>(() => Service().TransmitAsync(Root, Period, "u", default));
        Assert.Equal("Período fechado: reabra (R-2098) antes de enviar.", ex.Message);
    }

    [Fact]
    public async Task Outside_standalone_is_refused()
    {
        var ex = await Assert.ThrowsAsync<BusinessException>(() => Service("SAPB1").TransmitAsync(Root, Period, "u", default));
        Assert.Equal(ReinfService.StandaloneOnlyMessage, ex.Message);
    }

    [Fact]
    public async Task Issues_block_transmission()
    {
        Invoice("P1", 1000m);
        Invoice("P2", 1000m);
        foreach (var p in _db.Context.BusinessPartners) p.TaxId = "123";
        _db.Context.SaveChanges();

        var ex = await Assert.ThrowsAsync<BusinessException>(() => Service().TransmitAsync(Root, Period, "u", default));
        Assert.Equal("Produtor P1 sem CPF/CNPJ válido. (+1 pendência(s))", ex.Message);
        Assert.Empty(_client.SentXml);
    }

    [Fact]
    public async Task Unexpected_send_error_keeps_batch_record_and_retry_does_not_duplicate()
    {
        Invoke();
        _client.SendResults.Enqueue(() => throw new InvalidOperationException("resposta ilegível"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().TransmitAsync(Root, Period, "u", default));

        var fresh = TestDb.CreateUnitOfWork(_dbName);
        {
            var b = fresh.Context.ReinfBatches.Single();
            Assert.Equal(ReinfBatchStatus.Failed, b.Status);
            Assert.Contains("ilegível", b.Error);
            var e = fresh.Context.ReinfEvents.Single();
            Assert.Equal(ReinfEventStatus.Pending, e.Status);
            Assert.Equal(b.Key, e.BatchKey);
        }

        await Service().TransmitAsync(Root, Period, "u", default);
        Assert.Equal(2, _client.SentXml.Count);
        Assert.Single(_db.Context.ReinfEvents);
        Assert.Equal(ReinfEventStatus.Sent, _db.Context.ReinfEvents.Single().Status);
    }

    [Fact]
    public async Task Concurrent_transmissions_send_each_producer_once()
    {
        Invoke();
        _client.SendDelay = TimeSpan.FromMilliseconds(300);
        var a = TestDb.CreateUnitOfWork(_dbName);
        var b = TestDb.CreateUnitOfWork(_dbName);

        var results = await Task.WhenAll(
            Task.Run(() => Service(a.Context).TransmitAsync(Root, Period, "u", default)),
            Task.Run(() => Service(b.Context).TransmitAsync(Root, Period, "u", default)));

        Assert.Single(_client.SentXml);
        Assert.Equal(1, results.Sum(r => r.Count));
    }

    [Fact]
    public async Task Event_missing_from_processed_response_is_rejected()
    {
        Invoke();
        var key = Assert.Single(await Service().TransmitAsync(Root, Period, "u", default));
        _client.QueryResults.Enqueue(() => new ReinfBatchResult(2, "Processado", null, [], [], "<r/>"));

        await Service().QueryBatchAsync(key, default);

        var e = _db.Context.ReinfEvents.Single();
        Assert.Equal(ReinfEventStatus.Rejected, e.Status);
        Assert.Equal("Evento ausente no retorno do lote.", e.Occurrences);
    }

    private void Invoke() => Invoice("P1", 1000m);

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 24, 13, 0, 0, TimeSpan.Zero);
    }
}
