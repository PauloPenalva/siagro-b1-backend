using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Transport;

public class BranchTransportSettingsServiceTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeAverbaClient _averba = new();

    private BranchTransportSettingsService Service(string erp = "STANDALONE") =>
        new(_db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Erp"] = erp, ["Nfe:CertificateKey"] = Key,
        }).Build(), _averba);

    private static BranchTransportSettingsModel Model(string? password = "s3nha") => new()
    {
        CompanyRntrc = "11382119",
        InsuranceResponsible = InsuranceResponsible.Issuer,
        InsurerTaxId = "61.573.796/0001-66",
        InsurerName = "ALLIANZ",
        PolicyNumber = "2026550000194",
        AverbaEnabled = true,
        AverbaUser = "COSTA",
        AverbaPassword = password,
        AverbaCode = "11371106",
        AverbaEnvironment = NfeEnvironment.Production,
    };

    [Fact]
    public async Task Get_returns_empty_settings_when_missing()
    {
        var got = await Service().GetAsync("01");

        Assert.Equal("01", got.BranchCode);
        Assert.Null(got.CompanyRntrc);
        Assert.False(got.AverbaEnabled);
        Assert.False(got.HasAverbaPassword);
    }

    [Fact]
    public async Task Save_encrypts_password_and_never_returns_it()
    {
        var saved = await Service().SaveAsync("01", Model(), "tester");

        Assert.True(saved.HasAverbaPassword);
        Assert.Null(saved.AverbaPassword);
        Assert.Equal("61573796000166", saved.InsurerTaxId);
        var row = await _db.Context.BranchTransportSettings.SingleAsync();
        Assert.NotNull(row.AverbaPasswordCipher);
        Assert.DoesNotContain("s3nha", System.Text.Encoding.UTF8.GetString(row.AverbaPasswordCipher!));
        Assert.Equal("tester", row.UpdatedBy);
    }

    [Fact]
    public async Task Save_with_empty_password_keeps_stored_cipher()
    {
        await Service().SaveAsync("01", Model(), "tester");
        var before = (await _db.Context.BranchTransportSettings.AsNoTracking().SingleAsync()).AverbaPasswordCipher;

        var model = Model(password: "");
        model.PolicyNumber = "999";
        await Service().SaveAsync("01", model, "tester");

        var row = await _db.Context.BranchTransportSettings.AsNoTracking().SingleAsync();
        Assert.Equal(before, row.AverbaPasswordCipher);
        Assert.Equal("999", row.PolicyNumber);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1138211A")]
    public async Task Invalid_rntrc_is_rejected(string rntrc)
    {
        var model = Model();
        model.CompanyRntrc = rntrc;

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync("01", model, "t"));

        Assert.Equal("O RNTRC tem 8 dígitos.", ex.Message);
    }

    [Fact]
    public async Task Invalid_insurer_taxid_is_rejected()
    {
        var model = Model();
        model.InsurerTaxId = "11111111000111";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync("01", model, "t"));

        Assert.Equal("CNPJ da seguradora inválido.", ex.Message);
    }

    [Fact]
    public async Task Enabled_without_credentials_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service().SaveAsync("01", Model(password: null), "t"));

        Assert.Equal("Para averbar, informe usuário, senha e código da AT&M.", ex.Message);
    }

    [Fact]
    public async Task Disabled_without_credentials_is_accepted()
    {
        var model = Model(password: null);
        model.AverbaEnabled = false;
        model.AverbaUser = null;

        var saved = await Service().SaveAsync("01", model, "t");

        Assert.False(saved.AverbaEnabled);
    }

    [Fact]
    public async Task Test_uses_stored_password_when_none_typed()
    {
        await Service().SaveAsync("01", Model(), "tester");

        var result = await Service().TestAverbaAsync("01", "COSTA", null, "11371106", NfeEnvironment.Production);

        Assert.True(result.Success);
        Assert.Equal("Conexão com a AT&M OK.", result.Message);
        Assert.Equal("s3nha", _averba.LastPassword);
    }

    [Fact]
    public async Task Test_uses_typed_password_and_reports_failure()
    {
        _averba.Error = "Usuário ou senha inválidos";

        var result = await Service().TestAverbaAsync("01", "COSTA", "outra", "11371106", NfeEnvironment.Homologation);

        Assert.False(result.Success);
        Assert.Equal("Usuário ou senha inválidos", result.Message);
        Assert.Equal("outra", _averba.LastPassword);
        Assert.Equal(NfeEnvironment.Homologation, _averba.LastEnvironment);
        Assert.Empty(_db.Context.BranchTransportSettings);
    }

    [Fact]
    public async Task Test_without_any_password_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().TestAverbaAsync("01", "COSTA", null, "11371106", NfeEnvironment.Production));

        Assert.Equal("Informe usuário, senha e código da AT&M para testar.", ex.Message);
    }

    [Fact]
    public async Task Rejected_in_sapb1()
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service("SAPB1").GetAsync("01"));

        Assert.Equal("A configuração de transporte só existe no modo STANDALONE.", ex.Message);
    }

    private sealed class FakeAverbaClient : IAverbaClient
    {
        public string? Error { get; set; }
        public string? LastPassword { get; private set; }
        public NfeEnvironment? LastEnvironment { get; private set; }

        public Task<AverbaLoginResult> LoginAsync(NfeEnvironment environment, string user, string password, string code, CancellationToken ct)
        {
            LastPassword = password;
            LastEnvironment = environment;
            return Task.FromResult(Error is null ? new AverbaLoginResult(true, "tok", null) : new AverbaLoginResult(false, null, Error));
        }
    }
}
