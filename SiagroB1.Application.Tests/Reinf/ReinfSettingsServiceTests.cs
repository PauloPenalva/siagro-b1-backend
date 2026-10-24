using SiagroB1.Application.Services.Reinf;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Reinf.Events;
using Xunit;

namespace SiagroB1.Application.Tests.Reinf;

public class ReinfSettingsServiceTests
{
    private readonly SiagroB1.Infra.UnitOfWork _db = TestDb.CreateUnitOfWork(Guid.NewGuid().ToString());
    private ReinfSettingsService Service() => new(_db.Context, TimeProvider.System);

    private static ReinfSettings Valid() => new()
    {
        TaxIdRoot = "12.345.678/0001-95", BranchCode = "01", TaxClassification = "99", ValidFrom = "2026-01",
        ContactName = "Fulano", ContactCpf = "123.456.789-09",
    };

    [Fact]
    public async Task Saves_and_reads_by_root()
    {
        await Service().SaveAsync(Valid(), "u");
        var s = await Service().GetAsync("12345678");
        Assert.Equal("12345678", s.TaxIdRoot);
        Assert.Equal("12345678909", s.ContactCpf);
        Assert.Equal(ReinfRegistrationStatus.NotSent, s.RegistrationStatus);
        Assert.Equal(R1000Operation.Include, ReinfService.RegistrationOperation(s));
    }

    [Theory]
    [InlineData("cpf", "CPF do contato deve ter 11 dígitos.")]
    [InlineData("class", "Classificação tributária deve ter 2 dígitos.")]
    [InlineData("valid", "Início da validade deve estar no formato AAAA-MM.")]
    public async Task Validates(string field, string message)
    {
        var input = Valid();
        if (field == "cpf") input.ContactCpf = "123";
        if (field == "class") input.TaxClassification = "9";
        if (field == "valid") input.ValidFrom = "2026-13";
        var ex = await Assert.ThrowsAsync<BusinessException>(() => Service().SaveAsync(input, "u"));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task Changing_accepted_settings_keeps_status_and_next_send_is_update()
    {
        await Service().SaveAsync(Valid(), "u");
        var s = _db.Context.ReinfSettings.Single();
        s.RegistrationStatus = ReinfRegistrationStatus.Accepted;
        s.RegistrationReceipt = "1-00-1";
        _db.Context.SaveChanges();

        var input = Valid();
        input.ContactName = "Outro";
        input.RegistrationStatus = ReinfRegistrationStatus.NotSent; // ignorado
        var saved = await Service().SaveAsync(input, "u");

        Assert.Equal("Outro", saved.ContactName);
        Assert.Equal(ReinfRegistrationStatus.Accepted, saved.RegistrationStatus);
        Assert.Equal(R1000Operation.Update, ReinfService.RegistrationOperation(saved));
    }
}
