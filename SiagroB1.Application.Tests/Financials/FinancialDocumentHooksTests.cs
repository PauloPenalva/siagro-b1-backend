using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Os quatro ganchos de geração de documento financeiro provisório (Task 6): aprovação de
/// contrato de compra, aprovação de contrato de venda, confirmação de fixação de preço de
/// compra e confirmação de fixação de preço de venda.
///
/// Cobre só o COMPORTAMENTO DO GANCHO (quando ele dispara, quantos documentos produz, para
/// qual origem) — as regras de cálculo do documento em si (valor, vencimento, idempotência)
/// já são de <see cref="FinancialDocumentsGenerateServiceTests"/>.
///
/// Cada teste semeia com um <see cref="AppDbContext"/> e executa o serviço com OUTRO, os dois
/// apontando para o mesmo banco InMemory. Reaproveitar a mesma instância de contexto faria o
/// change tracker "consertar" sozinho a navegação <c>PriceFixations</c> por fixup, mesmo sem
/// <c>Include</c> na query de aprovação — mascarando exatamente a armadilha que estes testes
/// existem para pegar (contrato aprovado sem <c>Include(x =&gt; x.PriceFixations)</c> gera
/// zero documentos, silenciosamente).
/// </summary>
public class FinancialDocumentHooksTests
{
    private static AppDbContext NewContext(string dbName) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static ContractNotificationOutboxService Outbox(AppDbContext context)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:AppBaseUrl"] = "https://siagro.teste" })
            .Build();

        return new ContractNotificationOutboxService(
            context, new ContractNotificationPayloadBuilder(context, configuration));
    }

    private static FinancialDocumentsGenerateService FinancialDocuments(AppDbContext context) => new(
        context, new FakeDocNumberSequenceService(),
        new FakeBusinessPartnerService(new Dictionary<string, string>
        {
            ["F0001"] = "PRODUTOR TESTE",
            ["C0001"] = "CLIENTE TESTE",
        }));

    [Fact]
    public async Task ApprovingFixedPricePurchaseContract_GeneratesOnePayableDocument_FromTheAutoFixation()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var fixationKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.PurchaseContracts.Add(new PurchaseContract
            {
                Key = contractKey,
                Code = "PC-FIX-001",
                CardCode = "F0001",
                ItemCode = "SOJA",
                UnitOfMeasureCode = "KG",
                HarvestSeasonCode = "24/25",
                DeliveryLocationCode = "01",
                TotalVolume = 1_000m,
                StandardPrice = 100m,
                StandardCashFlowDate = new DateTime(2026, 12, 31),
                StandardCurrency = CurrencyType.Brl,
                Type = ContractType.Fixed,
                Status = ContractStatus.InApproval,
            });

            // Espelha o que PurchaseContractsCreateService.CreatePriceFixation grava na
            // criação: uma fixação Confirmed que acompanha o preço já acordado do contrato.
            seed.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
            {
                Key = fixationKey,
                PurchaseContractKey = contractKey,
                FixationVolume = 1_000m,
                FixationPrice = 100m,
                Status = PriceFixationStatus.Confirmed,
            });

            await seed.SaveChangesAsync();
        }

        await using var act = NewContext(dbName);
        await new PurchaseContractsApprovalService(act, Outbox(act), FinancialDocuments(act))
            .ExecuteAsync(contractKey, null, "diretoria");

        var document = Assert.Single(act.FinancialDocuments);
        Assert.Equal(FinancialDirection.Payable, document.Direction);
        Assert.Equal(FinancialDocumentNature.Provisional, document.Nature);
        Assert.Equal(fixationKey, document.OriginKey);
        Assert.Equal(FinancialDocumentOrigin.PurchaseContractPriceFixation, document.OriginType);
    }

    [Fact]
    public async Task ApprovingToBeDeterminedPurchaseContract_WithoutConfirmedFixations_GeneratesNoDocuments()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.PurchaseContracts.Add(new PurchaseContract
            {
                Key = contractKey,
                Code = "PC-PAF-001",
                CardCode = "F0001",
                ItemCode = "SOJA",
                UnitOfMeasureCode = "KG",
                HarvestSeasonCode = "24/25",
                DeliveryLocationCode = "01",
                TotalVolume = 1_000m,
                Type = ContractType.ToBeDetermined,
                Status = ContractStatus.InApproval,
            });

            await seed.SaveChangesAsync();
        }

        await using var act = NewContext(dbName);
        await new PurchaseContractsApprovalService(act, Outbox(act), FinancialDocuments(act))
            .ExecuteAsync(contractKey, null, "diretoria");

        Assert.Empty(act.FinancialDocuments);
    }

    [Fact]
    public async Task ApprovingFixedPriceSalesContract_GeneratesOneReceivableDocument()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var fixationKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.SalesContracts.Add(new SalesContract
            {
                Key = contractKey,
                Code = "SC-FIX-001",
                CardCode = "C0001",
                ItemCode = "SOJA",
                UnitOfMeasureCode = "KG",
                HarvestSeasonCode = "24/25",
                TotalVolume = 1_000m,
                Price = 120m,
                StandardCashFlowDate = new DateTime(2026, 12, 31),
                StandardCurrency = CurrencyType.Brl,
                Type = ContractType.Fixed,
                Status = ContractStatus.InApproval,
            });

            // Espelha SalesContractsCreateService.BuildAutoFixation: fixação Confirmed que
            // acompanha o preço acordado.
            seed.SalesContractsPriceFixations.Add(new SalesContractPriceFixation
            {
                Key = fixationKey,
                SalesContractKey = contractKey,
                FixationVolume = 1_000m,
                FixationPrice = 120m,
                Status = PriceFixationStatus.Confirmed,
            });

            await seed.SaveChangesAsync();
        }

        await using var act = NewContext(dbName);
        await new SalesContractsApprovalService(act, Outbox(act), FinancialDocuments(act))
            .ExecuteAsync(contractKey, null, "diretoria");

        var document = Assert.Single(act.FinancialDocuments);
        Assert.Equal(FinancialDirection.Receivable, document.Direction);
        Assert.Equal(FinancialDocumentNature.Provisional, document.Nature);
        Assert.Equal(fixationKey, document.OriginKey);
        Assert.Equal(FinancialDocumentOrigin.SalesContractPriceFixation, document.OriginType);
    }

    [Fact]
    public async Task ConfirmingPriceFixation_OnApprovedToBeDeterminedPurchaseContract_GeneratesOnePayableDocument()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var fixationKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            seed.PurchaseContracts.Add(new PurchaseContract
            {
                Key = contractKey,
                Code = "PC-PAF-002",
                CardCode = "F0001",
                ItemCode = "SOJA",
                UnitOfMeasureCode = "KG",
                HarvestSeasonCode = "24/25",
                DeliveryLocationCode = "01",
                TotalVolume = 100_000m,
                FixedVolume = 30_000m,
                Type = ContractType.ToBeDetermined,
                Status = ContractStatus.Approved,
            });

            seed.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
            {
                Key = fixationKey,
                PurchaseContractKey = contractKey,
                FixationVolume = 30_000m,
                FixationPrice = 2.5m,
                // Contrato a fixar exige o vencimento financeiro NA fixação, não no contrato.
                FinancialDueDate = new DateTime(2026, 12, 31),
                Status = PriceFixationStatus.InApproval,
            });

            await seed.SaveChangesAsync();
        }

        await using var act = NewContext(dbName);
        await new PurchaseContractsPriceFixationsApprovalService(
                act,
                new PurchaseContractsFixedVolumeService(act),
                new PurchaseContractsChangeLogService(act),
                TestNotificationOutbox.For(act),
                FinancialDocuments(act))
            .ExecuteAsync(fixationKey, "ok", "diretoria");

        var document = Assert.Single(act.FinancialDocuments);
        Assert.Equal(FinancialDirection.Payable, document.Direction);
        Assert.Equal(FinancialDocumentNature.Provisional, document.Nature);
        Assert.Equal(fixationKey, document.OriginKey);
        Assert.Equal(FinancialDocumentOrigin.PurchaseContractPriceFixation, document.OriginType);
    }
}
