using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Certificates;

namespace SiagroB1.Application.Tests.Cte;

/// <summary>Ganchos reais do CT-e: título na autorização; recusa do cancelamento com baixa; título e averbação no cancelamento.</summary>
public class CteLifecycleHooksTests : CteEventsTestBase
{
    private const string Reason = "Cancelamento por erro de digitacao";

    private sealed class FakeAverba : IAverbaClient
    {
        public int SendCalls { get; private set; }

        public Task<AverbaLoginResult> LoginAsync(NfeEnvironment environment, string user, string password, string code, CancellationToken ct) =>
            Task.FromResult(new AverbaLoginResult(true, "tok", null));

        public Task<AverbaCteResult> SendCteAsync(NfeEnvironment environment, string bearer, string xml, CancellationToken ct)
        {
            SendCalls++;
            return Task.FromResult(new AverbaCteResult(true, "CANC-1", null, [], null));
        }
    }

    private readonly FakeAverba _averba = new();
    private readonly FakeDocNumberSequenceService _docNumbers = new();

    private FinancialDocumentsTransportService Financial() => FinancialDocumentTestServices.Transport(Db, _docNumbers);

    private CteLifecycleHooks Hooks() => new(
        Db, NfeTestSeed.Config(), Financial(),
        new CteEndorsementCancellationService(Db, NfeTestSeed.Config(), Numbers, _averba,
            NullLogger<CteEndorsementCancellationService>.Instance),
        NullLogger<CteLifecycleHooks>.Instance);

    private TransportDocumentsCancelService Cancel() =>
        new(Db, NfeTestSeed.Config(), Numbers, Contexts(), Sefaz, Hooks(), Financial(), () => Clock);

    private TransportDocumentsGenerateTitleService Generate() =>
        new(Db, NfeTestSeed.Config(), Numbers, Financial());

    private TransportDocumentsConsultService Consult() =>
        new(Db, NfeTestSeed.Config(), Numbers, Contexts(), Sefaz, authorizationHook: Hooks());

    private async Task<Guid> SeedWithTitleAsync()
    {
        var key = await SeedDocAsync();
        await Hooks().OnAuthorizedAsync(await Db.Context.TransportDocuments.SingleAsync(x => x.Key == key));
        return key;
    }

    /// <summary>CT-e autorizado, tomador = remetente (REM), natureza que gera financeiro, averbado; SEM título.</summary>
    private async Task<Guid> SeedDocAsync(CteStatus status = CteStatus.Authorized)
    {
        var key = await SeedAsync(status);
        var usage = (await new UsageService(Db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Frete", Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5352", CfopOutgoingOutState = "6352",
            ContractBalanceEffect = ContractBalanceEffect.None, RequiresContract = false, CreatesFinancialDocument = true,
        })).Code;
        var doc = await Db.Context.TransportDocuments.Include(x => x.Parties).SingleAsync(x => x.Key == key);
        doc.UsageCode = usage;
        doc.TakerRole = 0;
        doc.AmountToReceive = 800m;
        doc.EndorsementStatus = CteEndorsementStatus.Endorsed;
        // Guid já preenchido: sem o Add explícito o EF trataria a parte como existente (UPDATE).
        Db.Context.TransportDocumentParties.Add(new TransportDocumentParty
        {
            TransportDocumentKey = key, Role = CtePartyRole.Sender, CardCode = "REM", Name = "REMETENTE LTDA",
        });
        Db.Context.BranchTransportSettings.Add(new BranchTransportSettings
        {
            BranchCode = "01", AverbaEnabled = true, AverbaUser = "u", AverbaCode = "c",
            AverbaEnvironment = NfeEnvironment.Homologation,
            AverbaPasswordCipher = CertificatePasswordCipher.FromBase64(NfeTestSeed.CertificateKey).Encrypt("s"),
        });
        await Db.SaveChangesAsync();
        return key;
    }

    private Task<List<FinancialDocument>> TitlesAsync(Guid key) =>
        Db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.TransportDocument && d.OriginKey == key).ToListAsync();

    [Fact]
    public async Task Authorization_hook_creates_title_for_taker()
    {
        var key = await SeedWithTitleAsync();

        var title = Assert.Single(await TitlesAsync(key));
        Assert.Equal("REM", title.CardCode);
        Assert.Equal(800m, title.NetAmount);
    }

    [Fact]
    public async Task Cancel_with_settled_title_is_refused_before_sefaz()
    {
        var key = await SeedWithTitleAsync();
        var title = await Db.Context.FinancialDocuments.SingleAsync(d => d.OriginKey == key);
        title.SettledAmount = 100m;
        await Db.SaveChangesAsync();
        Sefaz.CancelResponses.Enqueue(r => FakeCteSefazClient.CancellationRegistered(r));

        var e = await Assert.ThrowsAsync<DefaultException>(() => Cancel().CancelAsync(key, Reason, "tester"));

        Assert.Contains("já tem baixa", e.Message);
        Assert.Equal(0, Sefaz.CancelCalls);
        Assert.Equal(CteStatus.Authorized, (await LoadAsync(key)).Status);
        Assert.Equal(FinancialDocumentStatus.Open, (await TitlesAsync(key)).Single().Status);
    }

    [Fact]
    public async Task Cancel_cancels_title_and_calls_endorsement_cancellation()
    {
        var key = await SeedWithTitleAsync();
        Sefaz.CancelResponses.Enqueue(r => FakeCteSefazClient.CancellationRegistered(r));

        await Cancel().CancelAsync(key, Reason, "tester");

        Assert.Equal(CteStatus.Cancelled, (await LoadAsync(key)).Status);
        Assert.Equal(FinancialDocumentStatus.Canceled, (await TitlesAsync(key)).Single().Status);
        Assert.Equal(1, _averba.SendCalls);
        Assert.Equal(CteEndorsementStatus.Cancelled, (await LoadAsync(key)).EndorsementStatus);
    }

    [Fact]
    public async Task Hooks_do_nothing_in_sapb1()
    {
        var key = await SeedAsync();
        var doc = await Db.Context.TransportDocuments.SingleAsync(x => x.Key == key);
        var hooks = new CteLifecycleHooks(Db, NfeTestSeed.Config("SAPB1"), Financial(),
            new CteEndorsementCancellationService(Db, NfeTestSeed.Config("SAPB1"), Numbers, _averba,
                NullLogger<CteEndorsementCancellationService>.Instance),
            NullLogger<CteLifecycleHooks>.Instance);

        await hooks.OnAuthorizedAsync(doc);
        await hooks.OnCancelledAsync(doc);

        Assert.Empty(await TitlesAsync(key));
        Assert.Equal(0, _averba.SendCalls);
    }

    [Fact]
    public async Task Authorized_without_title_consult_creates_title()
    {
        var key = await SeedDocAsync();
        Sefaz.ConsultResponses.Enqueue(k => FakeCteSefazClient.Authorized(k));

        await Consult().ConsultAsync(key);
        Sefaz.ConsultResponses.Enqueue(k => FakeCteSefazClient.Authorized(k));
        await Consult().ConsultAsync(key);

        var title = Assert.Single(await TitlesAsync(key));
        Assert.Equal("REM", title.CardCode);
    }

    [Fact]
    public async Task Generate_title_action_is_idempotent()
    {
        var key = await SeedDocAsync();

        var first = await Generate().GenerateAsync(key, "ana");
        var second = await Generate().GenerateAsync(key, "ana");

        var title = Assert.Single(await TitlesAsync(key));
        Assert.Contains(title.Code, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Cancel_hook_with_title_settled_meanwhile_records_financial_error_and_still_cancels_endorsement()
    {
        // Baixa feita no Financeiro durante a ida à SEFAZ: a trava do serviço já passou, o CT-e está cancelado.
        var key = await SeedWithTitleAsync();
        var title = await Db.Context.FinancialDocuments.SingleAsync(d => d.OriginKey == key);
        title.SettledAmount = 100m;
        var doc = await Db.Context.TransportDocuments.SingleAsync(x => x.Key == key);
        doc.Status = CteStatus.Cancelled;
        Db.Context.TransportDocumentXmls.Add(new TransportDocumentXml
        {
            TransportDocumentKey = key, Kind = CteXmlKind.Cancellation, Xml = "<procEventoCTe/>",
        });
        await Db.SaveChangesAsync();

        await Hooks().OnCancelledAsync(doc);

        var saved = await LoadAsync(key);
        Assert.Contains("não cancelado", saved.FinancialError);
        Assert.Contains(title.Code, saved.FinancialError);
        Assert.Equal(FinancialDocumentStatus.Open, (await TitlesAsync(key)).Single().Status);
        Assert.Equal(CteEndorsementStatus.Cancelled, saved.EndorsementStatus);
    }

    [Fact]
    public async Task Hook_failure_records_financial_error_and_generate_clears_it()
    {
        var key = await SeedDocAsync();
        var doc = await Db.Context.TransportDocuments.SingleAsync(x => x.Key == key);
        doc.TakerRole = 3; // destinatário sem parte: o título não tem contra quem nascer
        await Db.SaveChangesAsync();

        await Hooks().OnAuthorizedAsync(doc);

        Assert.Empty(await TitlesAsync(key));
        Assert.Contains("tomador", (await LoadAsync(key)).FinancialError);

        doc.TakerRole = 0;
        await Db.SaveChangesAsync();
        await Generate().GenerateAsync(key, "ana");

        Assert.Single(await TitlesAsync(key));
        Assert.Null((await LoadAsync(key)).FinancialError);
    }

    [Fact]
    public async Task Generate_title_not_authorized_is_refused()
    {
        var key = await SeedDocAsync(CteStatus.Confirmed);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Generate().GenerateAsync(key, "ana"));

        Assert.Equal("Só CT-e autorizado gera título.", e.Message);
        Assert.Empty(await TitlesAsync(key));
    }

    [Fact]
    public async Task Generate_title_without_financial_usage_says_so()
    {
        var key = await SeedDocAsync();
        var doc = await Db.Context.TransportDocuments.SingleAsync(x => x.Key == key);
        doc.UsageCode = null;
        await Db.SaveChangesAsync();

        Assert.Equal("O CT-e não gera título: a natureza não cria financeiro.", await Generate().GenerateAsync(key, "ana"));
    }
}
