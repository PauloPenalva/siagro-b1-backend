using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Web.Hooks;

namespace SiagroB1.Application.Tests.ContractDrafts;

/// <summary>
/// Testes do ENDPOINT do webhook, não do <c>SecretMatches</c> (esse tem os seus). O que quebra no
/// primeiro deploy é a fiação: o template da rota, o <c>AllowAnonymous</c>, de onde vem a situação
/// aplicada. Esta é a única superfície pública não autenticada da fase.
///
/// O host é montado aqui, com só o <c>MapD4SignWebhook()</c> real: o <c>Program.cs</c> de verdade
/// não sobe num teste unitário — entre o <c>Build()</c> e o <c>Run()</c> ele conecta ao SQL Server
/// pelo PendingMigrationsGuard e ao storage do Hangfire para registrar os jobs recorrentes.
/// </summary>
public class D4SignWebhookEndpointTests
{
    private const string Secret = "SEGREDO";

    private readonly ContractDraftsTestContext _ctx = new();

    /// <summary>Host mínimo com o endpoint real mapeado; o segredo configurado é parametrizável.</summary>
    private async Task<(WebApplication App, HttpClient Client)> StartAsync(string? configuredSecret = Secret)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = "Development",
        });

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        _ctx.ConfigureServices(builder.Services);

        builder.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Signature:Enabled"] = "true",
                [D4SignWebhookEndpoint.SecretKey] = configuredSecret,
            }).Build());

        // Toda rota exige usuário autenticado, MENOS quem se declara anônima. Sem isto o
        // .AllowAnonymous() do endpoint passaria despercebido: o webhook responderia igual com
        // ele e sem ele, e o D4Sign — que não manda credencial nenhuma — só descobriria o
        // problema em produção.
        builder.Services.AddAuthentication(DenyAllHandler.Scheme)
            .AddScheme<AuthenticationSchemeOptions, DenyAllHandler>(DenyAllHandler.Scheme, null);
        builder.Services.AddAuthorization(o =>
            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapD4SignWebhook();

        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    /// <summary>Minuta já enviada, pronta para receber eventos do provedor.</summary>
    private async Task<ContractDraft> SentDraftAsync(string uuid)
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "m", "t", default);
        await _ctx.SeedSignatoriesAsync();
        _ctx.Signature.SucceedsWith(uuid);
        await _ctx.SendToSignature().ExecuteAsync(draft.Key, "tester", default);
        return draft;
    }

    [Fact]
    public async Task Rejects_a_wrong_secret()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var response = await client.PostAsync("/hooks/d4sign/ERRADO", Form(("uuid", "uuid-1")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Fail-closed: sem segredo configurado, o endpoint recusa TUDO. O contrário — liberar quando
    /// não há segredo — deixaria a instalação recém-configurada aceitando post de qualquer um.
    /// </summary>
    [Fact]
    public async Task Rejects_everything_when_no_secret_is_configured()
    {
        var (app, client) = await StartAsync(configuredSecret: null);
        await using var _ = app;

        var response = await client.PostAsync($"/hooks/d4sign/{Secret}", Form(("uuid", "uuid-1")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// uuid desconhecido devolve 200 — pode ser documento criado à mão no cofre, e um erro faria
    /// o D4Sign gastar as sete tentativas dele à toa. É também o teste que prova o
    /// <c>AllowAnonymous</c>: com a política padrão exigindo autenticação, sem ele este 200 seria
    /// 401.
    /// </summary>
    [Fact]
    public async Task Answers_200_for_an_unknown_uuid()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var response = await client.PostAsync($"/hooks/d4sign/{Secret}", Form(("uuid", "nao-existe")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, _ctx.Signature.StateCalls);
    }

    /// <summary>
    /// O D4Sign não assina o webhook: quem descobrir a URL pode postar "assinado". Por isso do
    /// form só se usa o uuid — a situação vem sempre de <c>GetStateAsync</c>. Aqui o form grita
    /// que o documento foi finalizado e assinado; o provedor diz que foi CANCELADO, e é o
    /// provedor que vale.
    /// </summary>
    [Fact]
    public async Task Takes_the_state_from_the_provider_not_from_the_form()
    {
        var draft = await SentDraftAsync("uuid-1");
        _ctx.Signature.StateIs(new ESignatureDocumentState(ESignatureDocumentStatus.Canceled, []));

        var (app, client) = await StartAsync();
        await using var _ = app;

        var response = await client.PostAsync($"/hooks/d4sign/{Secret}",
            Form(("uuid", "uuid-1"), ("status", "finished"), ("signed", "1"), ("type_post", "4")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _ctx.Signature.StateCalls);

        var saved = await _ctx.Db.Context.ContractDrafts.AsNoTracking().SingleAsync(d => d.Key == draft.Key);
        Assert.Equal(ContractDraftStatus.Canceled, saved.Status);
        Assert.Null(saved.SignedAttachmentKey);
        Assert.Equal("d4sign-webhook", saved.CanceledBy);
    }

    /// <summary>Esquema que nunca autentica ninguém: qualquer rota não anônima vira 401.</summary>
    private sealed class DenyAllHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
    {
        public const string Scheme = "DenyAll";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
