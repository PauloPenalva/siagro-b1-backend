using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ContractDrafts;

/// <summary>Montagem comum dos testes de minuta: banco InMemory, fakes e semente mínima.</summary>
public sealed class ContractDraftsTestContext
{
    /// <summary>Nome da base InMemory — compartilhado com os escopos de DI de <see cref="Scopes"/>.</summary>
    public string DatabaseName { get; } = Guid.NewGuid().ToString();

    public UnitOfWork Db { get; }
    public FakeHtmlToPdfRenderer Pdf { get; } = new();
    public FakeESignatureProvider Signature { get; } = new();

    public ContractDraftsTestContext() => Db = TestDb.CreateUnitOfWork(DatabaseName);

    /// <summary>Configuração do serviço. Enabled=true por padrão; o teste de "desligado" sobrescreve.</summary>
    public IConfiguration Configuration { get; set; } = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Signature:Enabled"] = "true",
            ["Signature:D4Sign:WebhookBaseUrl"] = "https://gw.example.com",
            ["Signature:D4Sign:WebhookSecret"] = "SEGREDO",
        }).Build();

    public FakeBusinessPartnerService Partners { get; } = new(
        names: new() { ["F0001"] = "FAZENDA BOA VISTA LTDA", ["C0001"] = "TRADING SUL SA" },
        taxIds: new() { ["F0001"] = "12345678000190" });

    public ContractDraftPlaceholderResolver Resolver() => new(Db.Context, Partners);
    public ContractDraftsLoader Loader() => new(Db.Context);
    public PurchaseContractsChangeLogService PurchaseLog() => new(Db.Context);
    public SalesContractsChangeLogService SalesLog() => new(Db.Context);
    public PurchaseContractsSetSignatureStatusService PurchaseSignatureStatus() => new(Db.Context, PurchaseLog());
    public SalesContractsSetSignatureStatusService SalesSignatureStatus() => new(Db.Context, SalesLog());

    public ContractDraftsCreateService Create() => new(Db.Context, Resolver(), PurchaseLog(), SalesLog(),
        NullLogger<ContractDraftsCreateService>.Instance);
    public ContractDraftsUpdateService Update() => new(Db.Context, Loader());
    public ContractDraftsDeleteService Delete() => new(Db.Context, Loader());
    public ContractDraftsGetService Get() => new(Db.Context);
    public ContractDraftsGetPdfService GetPdf() => new(Db.Context, Loader(), Pdf,
        new PurchaseContractsAttachmentsGetService(Db, NullLogger<PurchaseContractsAttachmentsGetService>.Instance),
        new SalesContractsAttachmentsGetService(Db, NullLogger<SalesContractsAttachmentsGetService>.Instance));

    public ContractDraftsSendToSignatureService SendToSignature() => new(
        Db.Context, Loader(), GetPdf(), Signature, Configuration,
        PurchaseLog(), SalesLog(), PurchaseSignatureStatus(), SalesSignatureStatus(),
        NullLogger<ContractDraftsSendToSignatureService>.Instance);

    public ContractDraftsApplyProviderStateService ApplyState() => new(
        Db.Context, Loader(), Signature,
        new PurchaseContractsAttachmentsCreateService(Db, NullLogger<PurchaseContractsAttachmentsCreateService>.Instance),
        new SalesContractsAttachmentsCreateService(Db, SalesLog(), NullLogger<SalesContractsAttachmentsCreateService>.Instance),
        PurchaseLog(), SalesLog(), PurchaseSignatureStatus(), SalesSignatureStatus(),
        NullLogger<ContractDraftsApplyProviderStateService>.Instance);

    public ContractDraftsCancelService Cancel() => new(
        Db.Context, Loader(), Signature, Configuration, PurchaseLog(), SalesLog(),
        NullLogger<ContractDraftsCancelService>.Instance);

    public ContractDraftsRefreshStateService RefreshState() => new(Loader(), Signature, Configuration, ApplyState());

    /// <summary>
    /// Fábrica de escopos de DI de verdade sobre a MESMA base InMemory: cada escopo ganha um
    /// <see cref="AppDbContext"/> novo — logo, um change tracker novo — exatamente como em
    /// produção. É o que o job de reconciliação usa para isolar uma minuta da seguinte; um
    /// dublê que devolvesse sempre o mesmo contexto não provaria isolamento nenhum.
    /// </summary>
    public IServiceScopeFactory Scopes()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    /// <summary>
    /// Registra banco, fakes e os serviços de minuta num contêiner qualquer — usado tanto pelos
    /// escopos de <see cref="Scopes"/> quanto pelo host de teste do webhook.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton(Configuration);
        services.AddSingleton<IESignatureProvider>(Signature);
        services.AddDbContext<AppDbContext>(o => o
            .UseInMemoryDatabase(DatabaseName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<ContractDraftsLoader>();
        services.AddScoped<PurchaseContractsChangeLogService>();
        services.AddScoped<SalesContractsChangeLogService>();
        services.AddScoped<PurchaseContractsSetSignatureStatusService>();
        services.AddScoped<SalesContractsSetSignatureStatusService>();
        services.AddScoped<PurchaseContractsAttachmentsCreateService>();
        services.AddScoped<SalesContractsAttachmentsCreateService>();
        services.AddScoped<ContractDraftsApplyProviderStateService>();
        services.AddScoped<ContractDraftsRefreshStateService>();
    }

    public async Task<ContractTemplate> SeedTemplateAsync(
        string body = "<p>Contrato {{numero}} com {{fornecedor_razao_social}}</p>{{assinaturas_empresa}}",
        ContractTemplateScope scope = ContractTemplateScope.Purchase, bool active = true)
    {
        var t = new ContractTemplate { Name = $"Modelo {Guid.NewGuid():N}", Title = "Contrato de Compra", ContractType = scope, BodyHtml = body, Active = active };
        Db.Context.ContractTemplates.Add(t);
        await Db.Context.SaveChangesAsync();
        return t;
    }

    public async Task<PurchaseContract> SeedPurchaseAsync(ContractStatus status = ContractStatus.Approved)
    {
        Db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "Matriz", ShortName = "MTZ", TaxId = "98765432000110" });
        var c = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-000123", BranchCode = "01", CardCode = "F0001", ItemCode = "SOJA",
            ItemName = "SOJA EM GRÃOS", UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", TotalVolume = 1000m,
            StandardPrice = 2m, DeliveryLocationCode = "01", Type = ContractType.Fixed, Status = status,
            DeliveryStartDate = new DateTime(2026, 10, 1), DeliveryEndDate = new DateTime(2026, 10, 31),
        };
        Db.Context.PurchaseContracts.Add(c);
        await Db.Context.SaveChangesAsync();
        return c;
    }

    public async Task<SalesContract> SeedSalesAsync(ContractStatus status = ContractStatus.Approved)
    {
        var c = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC-000045", BranchCode = "01", CardCode = "C0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", Volume = 500m, Price = 3m, Type = ContractType.Fixed,
            Status = status, DeliveryStartDate = new DateTime(2026, 10, 1), DeliveryEndDate = new DateTime(2026, 10, 31),
        };
        Db.Context.SalesContracts.Add(c);
        await Db.Context.SaveChangesAsync();
        return c;
    }

    /// <summary>Um signatário de cada lado, o mínimo que o envio exige.</summary>
    public async Task SeedSignatoriesAsync(string cardCode = "F0001", string branchCode = "01")
    {
        Db.Context.CompanySignatories.Add(new CompanySignatory
        {
            Name = "Diretor Tagui", TaxId = "11122233344", Email = "diretor@tagui.com",
            Role = SignatoryRole.SignAsParty, Order = 1, BranchCode = branchCode, Active = true,
        });
        Db.Context.BusinessPartnerSignatories.Add(new BusinessPartnerSignatory
        {
            CardCode = cardCode, Name = "Produtor", TaxId = "55566677788", Email = "produtor@x.com",
            Role = SignatoryRole.SignAsParty, Order = 1, Active = true,
        });
        await Db.Context.SaveChangesAsync();
    }
}
