# NF-e STANDALONE — NF-e de entrada própria e devolução de compra — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Numa filial que emite NF-e pelo Siagro, o Documento de Entrada de Emissão Própria passa a calcular os tributos pela natureza de entrada e a emitir a NF-e de entrada (compra, `finNFe 1`, `tpNF 0`); e a entrada própria autorizada ganha o "Devolver", que cria a devolução de compra emitida como NF-e de saída (`finNFe 4`, `tpNF 1`) referenciando cada item da entrada.

**Architecture:** (1) o `SiagroB1.Fiscal` separa sentido (`NfeDirection` → `tpNF`) de finalidade (`NfePurpose` → `finNFe`) e o motor passa a receber UF de origem/destino; (2) o Documento de Entrada ganha a mesma fotografia fiscal da linha de saída, estado da NF-e no cabeçalho e uma tabela de XMLs própria; (3) o pipeline do 2a é generalizado por duas interfaces de domínio (`INfeDocument`, `INfeTaxedLine`), um "store" por documento e classes base de emissão/consulta/retorno, mantendo nome, construtor e comportamento dos serviços do documento de saída; (4) serviços finos da entrada (cálculo, travas, emissão, devolução) e as telas espelhando as do documento de saída.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server), Microsoft.AspNetCore.OData 9.4.1, xUnit + EF InMemory, Zeus.Net.NFe.NFCe 2026.9.24.1416, FastReport.OpenSource; OpenUI5 1.141 + TypeScript (OData v4), QUnit.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-05-nfe-purchase-invoice-issuance-design.md` (commit 0b815b8 + ajuste do §10)

## Global Constraints

- Código (classes, tabelas, colunas) em inglês; texto que o usuário lê (labels, mensagens de negócio) em **pt-BR**; comentários em pt-BR.
- **Regra ativa** = `Erp == "STANDALONE"` **e** `Branch.IssuesNfe`, sempre perguntada ao `TaxCalculationGate.IsActiveAsync(branchCode)`. Sem ela (Yokotobi/SAPB1, MH Agro): comportamento idêntico ao de hoje.
- **Modo NF-e do Documento de Entrada** = regra ativa para a `BranchCode` do documento **e** `IssuerType == Own`. Documento de terceiro (`ThirdParty`) e a "Devolução do cliente" (Return de terceiro) **nunca** entram em cálculo, trava ou guarda nova.
- **Devolução de compra** = `PurchaseInvoice.InvoiceType == Return` **e** `IsNfeReturn == true` (e `IssuerType == Own`). Só `PurchaseInvoicesNfeReturnCreateService` grava `IsNfeReturn = true`; o corpo da API nunca.
- **Os testes do documento de saída não mudam de asserção.** Só mudam fábricas de teste (construtor) e renomeações mecânicas de identificador (`SalesInvoiceNfeXmlKind` → `NfeXmlKind`, `NfePurpose.Sale` → `NfePurpose.Normal`, `SalesInvoiceTaxSnapshot` → `TaxSnapshot`, `SalesInvoiceNfeItemNumbering` → `NfeItemNumbering`, `SalesInvoiceNfeReturnConference` → `NfeReturnConference`, `SalesInvoiceNfeResultHandler.Truncate` → `NfeStatusText.Truncate`). Exceção prevista: um teste que fixe `indFinal` da devolução de venda para não contribuinte (spec D15).
- Regras da SEFAZ: `finNFe 4` exige `tPag 90` com `vPag 0` (rejeição 871); referência num nível só — `NFref` **ou** `DFeReferenciado`, nunca os dois (rejeição 1010); VC02-14 exige `det/DFeReferenciado` em cada item da devolução; `indFinal = 1` só em saída para não contribuinte.
- Mensagens de guarda/negócio: `DefaultException` em pt-BR (o controller devolve 400).
- Quantidades em action OData: `Collection(Edm.Double)`, nunca `Decimal`.
- Migration única `AddPurchaseInvoiceNfe` (aditiva, `AppDbContext`). Gerar/aplicar a partir de `siagro-b1-backend/` com ambiente explícito `ASPNETCORE_ENVIRONMENT=Ceagui-Development` (banco `CEAGUI_SIAGRO_DEV`). **Ler a migration gerada antes de seguir.** Não aplicar em nenhum outro banco.
- Novo arquivo ⇒ `git add <arquivo>` imediato no repo dele.
- Branch nos dois repos: `feature/nfe-purchase-invoice-issuance` (conferir antes de cada commit). Commits por tarefa, padrão `tipo(escopo): descrição pt-BR`; escopos `invoice` (documentos e NF-e) e `master-data` (natureza); trailer `DB: AddPurchaseInvoiceNfe` no commit da migration. Mensagem termina com:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01BgJE3fiJjTbTPaTvZhCpnx
  ```
  **Sempre com pathspec explícito** (`git commit -F <msg> -- <arquivos>`): no backend os documentos do sub-projeto 1 (`docs/superpowers/{specs,plans}/2026-10-01-nfe-standalone-taxation*`) estão staged e **não** entram; no frontend, `.vscode/.advpl/*.tlpp` estão modificados e **não** entram. Nunca push.
- Commit negado pelo classificador do auto mode: grave a mensagem num arquivo, deixe o comando `git commit -F <arquivo> -- <pathspecs>` registrado no relatório e siga. Nunca contornar.
- Testes backend (a partir de `siagro-b1-backend/`): `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"` e `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~<Classe>"`; suíte inteira: `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests`. Antes de compilar, pare qualquer `SiagroB1.Web`/`Gateway`/`Reports` iniciado por você (travam as DLLs).
- Frontend (a partir de `siagro-b1-frontend/`): `yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (base **920** problemas — não pode subir), QUnit: `npx ui5 serve --port 8081` em segundo plano e `npx ui5-test-runner --url "http://localhost:8081/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests"`, depois pare o `ui5 serve` pelo PID (a porta 8080 é do dev server do usuário — não mexa nele). O `yarn test` nunca passa (gate de cobertura) — não use.
- Frontend: enum em expressão usa `${path: 'X', targetType: 'any'}`; filtro de enum vai como `$filter` estático; tipo de dado em XML via `core:require`; `--` dentro de comentário XML mata o fragmento; `setProperty` em contexto com update group diferido **não** leva `await`; texto de célula de tabela com `wrapping="false"`; booleano em expressão comparado com `=== true`.

## Review Focus

1. **Documento de Entrada antigo, de terceiro, sem filial** (o `BranchCode` nunca era gravado) — editar e salvar continua funcionando: nenhuma regra nova exige filial em documento de terceiro. Teste na Task 5.
2. **Entrada própria criada com a chave da filial desligada e emitida depois de ligá-la** — as linhas estão sem fotografia; a emissão recusa com "salve o documento para recalcular", e salvar recalcula. Teste na Task 8.
3. **Devolução de só o 2º item de uma entrada com vários itens** — o `DFeReferenciado.nItem` é o número do item **na entrada** (2), não a posição na devolução (1). Teste na Task 9.
4. **Duas devoluções Pendentes da mesma entrada que, somadas, passam do comprado** — a emissão da segunda recusa antes de reservar número. Teste na Task 9.
5. **Linha da entrada própria sem unidade de medida** (o campo é anulável na entrada) — a prontidão lista a lacuna em vez de estourar no montador. Teste na Task 8.

---

## File Structure

**Backend (`siagro-b1-backend/`)**

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs` (mod) | `NfeDirection`, `NfePurpose { Normal, Return }`, `Direction` |
| `SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs` (mod) | `tpNF` pelo sentido, `finNFe` pela finalidade, `indFinal` só na saída |
| `SiagroB1.Fiscal/Taxes/TaxCalculationModels.cs`, `TaxCalculator.cs` (mod) | `OriginState`/`DestinationState` |
| `SiagroB1.Domain/Interfaces/INfeDocument.cs` (novo) | cabeçalho que o pipeline da NF-e lê e escreve |
| `SiagroB1.Domain/Interfaces/INfeTaxedLine.cs` (novo) | linha com a fotografia fiscal |
| `SiagroB1.Domain/Enums/NfeXmlKind.cs` (renomeado de `SalesInvoiceNfeXmlKind.cs`) | tipo do XML guardado |
| `SiagroB1.Domain/Entities/PurchaseInvoice.cs`, `PurchaseInvoiceItem.cs` (mod) | colunas do spec §5.1/§5.2 + interfaces |
| `SiagroB1.Domain/Entities/SalesInvoice.cs`, `SalesInvoiceItem.cs` (mod) | só declaram as interfaces |
| `SiagroB1.Domain/Entities/PurchaseInvoiceNfeXml.cs` (novo) | XMLs da NF-e da entrada |
| `SiagroB1.Domain/Dtos/Nfe/NfeIssueOutcomeDto.cs` (mod) | `From(INfeDocument)` |
| `SiagroB1.Domain/Dtos/PurchaseInvoiceNfeReturnableItemDto.cs` (novo) | linha do diálogo "Devolver" da entrada |
| `SiagroB1.Infra/Context/AppDbContext.cs` (mod) | `PurchaseInvoiceNfeXmls` + índice |
| `SiagroB1.Infra/Nfe/PurchaseInvoiceNfeXmlQueries.cs` (novo) | procNFe autorizado mais recente da entrada |
| `SiagroB1.Application/Services/Taxes/TaxLineCalculator.cs` (novo) | natureza + produto + UFs → CFOP e tributos de uma linha |
| `SiagroB1.Application/Services/Taxes/TaxSnapshot.cs` (renomeado de `SalesInvoices/SalesInvoiceTaxSnapshot.cs`) | escrita/trava da fotografia, genérica |
| `SiagroB1.Application/Services/Taxes/NfeReturnConference.cs` (renomeado de `SalesInvoices/SalesInvoiceNfeReturnConference.cs`) | conferência devolução × origem, genérica |
| `SiagroB1.Application/Services/Nfe/NfeItemNumbering.cs` (renomeado de `SalesInvoiceNfeItemNumbering.cs`) | `nItem`, genérico |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs` (mod) | passa a usar `TaxLineCalculator` |
| `SiagroB1.Application/Services/UsageService.cs` (mod) | natureza de devolução nos dois sentidos; "em uso" olha a entrada |
| `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs` (novo) | cálculo da entrada própria e da devolução de compra |
| `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceNfeLock.cs` (novo) | travas da NF-e no Documento de Entrada |
| `SiagroB1.Application/Services/Nfe/NfeLockRules.cs` (novo) | comparação/restauração de campos e mensagens comuns às duas travas |
| `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceAccessKey.cs` (novo) | validação da NF-e referenciada |
| `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs`, `UpdateService.cs`, `ItemsCreateService.cs`, `ItemsUpdateService.cs`, `ItemsDeleteService.cs`, `ConfirmService.cs`, `CancelService.cs`, `DeleteService.cs`, `GetService.cs` (mod) | cálculo, campos novos, travas e guardas |
| `SiagroB1.Application/Services/Nfe/NfeStatusText.cs` (novo) | `Truncate` (500) |
| `SiagroB1.Application/Services/Nfe/INfeDocumentStore.cs`, `SalesInvoiceNfeStore.cs`, `PurchaseInvoiceNfeStore.cs` (novos) | acesso a documento e XMLs por tipo |
| `SiagroB1.Application/Services/Nfe/NfeIssueServiceBase.cs`, `NfeResultHandlerBase.cs`, `NfeConsultServiceBase.cs`, `NfeCompleteConfirmationServiceBase.cs`, `NfeXmlDownloadServiceBase.cs` (novos) | o fluxo do 2a, genérico |
| `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeIssueService.cs`, `SalesInvoiceNfeResultHandler.cs`, `SalesInvoicesNfeConsultService.cs`, `SalesInvoicesNfeCompleteConfirmationService.cs`, `SalesInvoicesNfeXmlDownloadService.cs` (mod) | subclasses finas, mesmos construtores |
| `SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs`, `NfeIssueInputAssembler.cs` (mod) | núcleo neutro + sobrecargas da entrada |
| `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs`, `PurchaseInvoiceNfeResultHandler.cs`, `PurchaseInvoicesNfeConsultService.cs`, `PurchaseInvoicesNfeCompleteConfirmationService.cs`, `PurchaseInvoicesNfeXmlDownloadService.cs` (novos) | emissão da entrada |
| `SiagroB1.Application/Services/Nfe/PurchaseInvoiceNfeReturnBalance.cs`, `PurchaseInvoicesNfeReturnCreateService.cs`, `PurchaseInvoicesNfeReturnableItemsService.cs` (novos) | devolução de compra |
| `SiagroB1.Web/Actions/Nfe/PurchaseInvoices*Controller.cs`, `SiagroB1.Web/Functions/Nfe/PurchaseInvoices*Controller.cs` (novos) | endpoints |
| `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (mod) | EDM e DI |
| `SiagroB1.Reports/Controllers/DanfeController.cs`, `Services/DanfeReportService.cs` (mod) | DANFE da entrada |
| `SiagroB1.Migrations/AppContext/<ts>_AddPurchaseInvoiceNfe.cs` (+ Designer, snapshot) (gerado) | colunas + tabela |

**Frontend (`siagro-b1-frontend/webapp/`)**

| Arquivo | Responsabilidade |
|---|---|
| `view/usages/fragments/Form.fragment.xml`, `controller/usages/BaseController.ts` (mod) | natureza de devolução nos dois sentidos |
| `model/ServerRoutes.ts` (mod) | rotas da entrada |
| `helpers/NfeReturnHelpers.ts` (mod) | tipos genéricos sobre as colunas comuns |
| `helpers/PurchaseInvoiceNfeHelpers.ts` (novo) | regras de tela puras da entrada (modo NF-e, chave) |
| `controller/purchaseInvoices/BaseController.ts`, `Add.controller.ts`, `Edit.controller.ts`, `Detail.controller.ts`, `Main.controller.ts` (mod) | modo NF-e, filial, natureza, emissão, devolução |
| `view/purchaseInvoices/fragments/Form.fragment.xml`, `Items.fragment.xml` (mod) | campos novos e travas |
| `view/purchaseInvoices/fragments/ItemFiscalDialog.fragment.xml`, `NfeReturnDialog.fragment.xml` (novos) | diálogo fiscal (leitura) e "Devolver" |
| `view/purchaseInvoices/Detail.view.xml`, `Main.view.xml` (mod) | painel NF-e, botões, coluna da lista |
| `test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts` (novo), `test/unit/unitTests.qunit.ts` (mod) | testes das regras puras |

---

### Task 1: Fiscal — sentido separado da finalidade e UFs de origem/destino no motor

Spec: D6, D14, D15, §8.1.

**Files:**
- Modify: `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs` (enum `NfePurpose` linhas ~72-77, propriedade `Purpose` linha ~148)
- Modify: `SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs:105-114` (`tpNF`, `finNFe`, `indFinal`)
- Modify: `SiagroB1.Fiscal/Taxes/TaxCalculationModels.cs:18-28`, `SiagroB1.Fiscal/Taxes/TaxCalculator.cs:66`
- Modify: `SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs:60` (`Purpose`/`Direction` da venda e da devolução de venda)
- Modify: `SiagroB1.Fiscal.Tests/Support/NfeTestData.cs` (`ReturnInput` ganha `Direction`; dois cenários novos)
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs`, `SiagroB1.Fiscal.Tests/Nfe/NfeSignerTests.cs`, `SiagroB1.Fiscal.Tests/Taxes/TaxCalculatorTests.cs`

**Interfaces:**
- Produces: `enum NfeDirection { Outgoing, Incoming }`; `enum NfePurpose { Normal, Return }` (era `{ Sale, Return }`); `NfeIssueInput.Direction` (padrão `Outgoing`); `TaxCalculationInput(decimal Amount, bool InState, string OriginState, string DestinationState, TaxRegime Regime, byte GoodsOrigin, IcmsRule Icms, PisCofinsRule PisCofins, IbsCbsRule? IbsCbs, IbsCbsRates? Rates)`; `NfeTestData.ProducerAccessKey`, `NfeTestData.EntryAccessKey`, `NfeTestData.PurchaseEntryInput()`, `NfeTestData.PurchaseReturnInput()`.

- [ ] **Step 1: Cenários de teste novos em `NfeTestData`**

Em `SiagroB1.Fiscal.Tests/Support/NfeTestData.cs`, na `ReturnInput()` acrescente `Direction = NfeDirection.Incoming,` logo antes de `Purpose = NfePurpose.Return,`. Depois acrescente, no fim da classe:

```csharp
    /// <summary>Chave da NF-e do produtor que a compra de teste referencia (nota de terceiro).</summary>
    public const string ProducerAccessKey = "35261011222333000181550010000004561123456780";

    /// <summary>Chave da NF-e de entrada própria que a devolução de compra de teste referencia.</summary>
    public const string EntryAccessKey = "35261012345678000195550010000001231481516234";

    private static NfeRecipient NonTaxpayerProducer() => new()
    {
        TaxId = "52998224725", Name = "PRODUTOR RURAL TESTE", Indicator = StateRegistrationIndicator.NonTaxpayer,
        Address = SaoPaulo(),
    };

    /// <summary>
    /// Compra de produtor de SP (operação interna) com NF-e própria de entrada: CFOP 1102, PIS/COFINS
    /// de entrada, cobrança pela condição e a NF-e do produtor referenciada no cabeçalho.
    /// </summary>
    public static NfeIssueInput PurchaseEntryInput() => Input(recipientAddress: SaoPaulo()) with
    {
        OperationNature = "COMPRA DE MERCADORIA",
        Direction = NfeDirection.Incoming,
        Purpose = NfePurpose.Normal,
        ReferencedKeys = [ProducerAccessKey],
        Recipient = NonTaxpayerProducer(),
        Items = [Item() with { Cfop = "1102", PisCst = "74", CofinsCst = "74" }],
    };

    /// <summary>
    /// Devolução de compra ao mesmo produtor (saída, finalidade 4): CFOP 5202, sem pagamento, com o
    /// item 2 da entrada referenciado no item (VC02-14) e nada no cabeçalho (rejeição 1010).
    /// </summary>
    public static NfeIssueInput PurchaseReturnInput() => Input(recipientAddress: SaoPaulo()) with
    {
        OperationNature = "DEVOLUCAO DE COMPRA",
        Direction = NfeDirection.Outgoing,
        Purpose = NfePurpose.Return,
        ReferencedKeys = [EntryAccessKey],
        Recipient = NonTaxpayerProducer(),
        Items = [Item() with { Cfop = "5202", PisCst = "49", CofinsCst = "49", Reference = new NfeItemReference(EntryAccessKey, 2) }],
        Payment = new PaymentPlan(PaymentMeansCodes.NoPayment, null, 0m, []),
    };
```

- [ ] **Step 2: Testes que falham**

Em `NfeXmlBuilderTests.cs`, no fim da classe:

```csharp
    [Fact]
    public void Purchase_entry_is_an_incoming_normal_note_with_the_producer_note_referenced()
    {
        var nfe = Build(NfeTestData.PurchaseEntryInput());

        Assert.Equal(TipoNFe.tnEntrada, nfe.infNFe.ide.tpNF);
        Assert.Equal(FinalidadeNFe.fnNormal, nfe.infNFe.ide.finNFe);
        Assert.Equal(NfeTestData.ProducerAccessKey, Assert.Single(nfe.infNFe.ide.NFref).refNFe);
        var det = Assert.Single(nfe.infNFe.det);
        Assert.Null(det.DFeReferenciado);
        Assert.Equal(1102, det.prod.CFOP);
    }

    [Fact]
    public void Purchase_entry_from_a_non_taxpayer_is_not_a_final_consumer_operation()
    {
        Assert.Equal(ConsumidorFinal.cfNao, Build(NfeTestData.PurchaseEntryInput()).infNFe.ide.indFinal);
    }

    [Fact]
    public void Purchase_entry_bills_by_the_payment_plan()
    {
        var nfe = Build(NfeTestData.PurchaseEntryInput());

        Assert.NotNull(nfe.infNFe.cobr);
        Assert.Equal(2, nfe.infNFe.cobr.dup.Count);
    }

    [Fact]
    public void Purchase_return_is_an_outgoing_return_referenced_only_at_item_level()
    {
        var nfe = Build(NfeTestData.PurchaseReturnInput());

        Assert.Equal(TipoNFe.tnSaida, nfe.infNFe.ide.tpNF);
        Assert.Equal(FinalidadeNFe.fnDevolucao, nfe.infNFe.ide.finNFe);
        Assert.Null(nfe.infNFe.ide.NFref);
        var det = Assert.Single(nfe.infNFe.det);
        Assert.Equal(NfeTestData.EntryAccessKey, det.DFeReferenciado.chaveAcesso);
        Assert.Equal(2, det.DFeReferenciado.nItem);
        Assert.Equal(5202, det.prod.CFOP);
    }

    [Fact]
    public void Purchase_return_to_a_non_taxpayer_is_a_final_consumer_operation()
    {
        Assert.Equal(ConsumidorFinal.cfConsumidorFinal, Build(NfeTestData.PurchaseReturnInput()).infNFe.ide.indFinal);
    }

    [Fact]
    public void Purchase_return_pays_nothing_and_has_no_billing()
    {
        var nfe = Build(NfeTestData.PurchaseReturnInput());

        var payment = Assert.Single(Assert.Single(nfe.infNFe.pag).detPag);
        Assert.Equal(90, (int)payment.tPag!);
        Assert.Equal(0m, payment.vPag);
        Assert.Null(nfe.infNFe.cobr);
    }

    [Fact]
    public void Sales_return_to_a_non_taxpayer_is_not_a_final_consumer_operation()
    {
        var input = NfeTestData.ReturnInput() with
        {
            Recipient = NfeTestData.ReturnInput().Recipient with
            {
                Indicator = StateRegistrationIndicator.NonTaxpayer, StateRegistration = null,
            },
        };

        Assert.Equal(ConsumidorFinal.cfNao, Build(input).infNFe.ide.indFinal);
    }
```

Em `NfeSignerTests.cs`, no fim da classe:

```csharp
    [Fact]
    public void Purchase_entry_validates_against_the_official_schema()
    {
        using var certificate = Certificate();

        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.PurchaseEntryInput(), Settings(certificate));

        Assert.Contains("<tpNF>0</tpNF>", signed.Xml);
        Assert.Contains("<finNFe>1</finNFe>", signed.Xml);
        Assert.Contains($"<NFref><refNFe>{NfeTestData.ProducerAccessKey}</refNFe></NFref>", signed.Xml);
        Assert.Contains("<CPF>52998224725</CPF>", signed.Xml);
    }

    [Fact]
    public void Purchase_return_validates_against_the_official_schema()
    {
        using var certificate = Certificate();

        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.PurchaseReturnInput(), Settings(certificate));

        Assert.Contains("<tpNF>1</tpNF>", signed.Xml);
        Assert.Contains("<finNFe>4</finNFe>", signed.Xml);
        Assert.DoesNotContain("<NFref>", signed.Xml);
        Assert.Contains($"<DFeReferenciado><chaveAcesso>{NfeTestData.EntryAccessKey}</chaveAcesso><nItem>2</nItem></DFeReferenciado>", signed.Xml);
        Assert.Contains("<tPag>90</tPag>", signed.Xml);
    }
```

Em `TaxCalculatorTests.cs`, no fim da classe (argumentos NOMEADOS de propósito — é o que prova o novo nome):

```csharp
    /// <summary>Compra interestadual: a alíquota segue a mercadoria (BA → SP = 12%), não a filial (SP → BA = 7%).</summary>
    [Fact]
    public void Interstate_entry_uses_the_supplier_state_as_origin()
    {
        var r = TaxCalculator.Calculate(new TaxCalculationInput(
            Amount: 60000m, InState: false, OriginState: "BA", DestinationState: "SP", Regime: TaxRegime.Normal,
            GoodsOrigin: 0, Icms: Cst("00"), PisCofins: PisCofins, IbsCbs: null, Rates: null));

        Assert.Equal(12m, r.IcmsRate);
    }
```

- [ ] **Step 3: Ver falhar**

Run: `dotnet test SiagroB1.Fiscal.Tests`
Expected: falha de compilação (`NfeDirection` não existe; `OriginState`/`DestinationState` não são parâmetros de `TaxCalculationInput`).

- [ ] **Step 4: Implementação no Fiscal**

Em `NfeIssueInput.cs`, troque o enum `NfePurpose` por:

```csharp
/// <summary>Sentido da NF-e (<c>ide/tpNF</c>): saída (1) ou entrada (0).</summary>
public enum NfeDirection
{
    Outgoing,
    Incoming,
}

/// <summary>
/// Finalidade da NF-e (<c>ide/finNFe</c>): normal (1) ou devolução (4). Não decide o sentido: a devolução
/// de venda é entrada e a devolução de compra é saída (<see cref="NfeDirection"/>).
/// </summary>
public enum NfePurpose
{
    Normal,
    Return,
}
```

e, em `NfeIssueInput`, troque `public NfePurpose Purpose { get; init; } = NfePurpose.Sale;` por:

```csharp
    public NfeDirection Direction { get; init; } = NfeDirection.Outgoing;

    public NfePurpose Purpose { get; init; } = NfePurpose.Normal;
```

Ajuste o `<summary>` de `ReferencedKeys` para: `<c>ide/NFref/refNFe</c> — na devolução, a chave da nota de origem; na entrada própria, a NF-e do produtor. Só vai ao cabeçalho quando nenhum item tem <see cref="NfeItem.Reference"/> (rejeição 1010).`

Em `NfeXmlBuilder.cs` (`BuildIde`):

```csharp
        tpNF = input.Direction == NfeDirection.Incoming ? TipoNFe.tnEntrada : TipoNFe.tnSaida,
```
```csharp
        finNFe = input.Purpose == NfePurpose.Return ? FinalidadeNFe.fnDevolucao : FinalidadeNFe.fnNormal,
        // Consumidor final só existe na SAÍDA (regra 696): na entrada o destinatário é quem vende.
        indFinal = input.Direction == NfeDirection.Outgoing &&
                   input.Recipient.Indicator == StateRegistrationIndicator.NonTaxpayer
            ? ConsumidorFinal.cfConsumidorFinal
            : ConsumidorFinal.cfNao,
```

Em `TaxCalculationModels.cs`, renomeie os dois parâmetros posicionais e documente:

```csharp
/// <param name="OriginState">UF de onde a mercadoria SAI: a filial na venda, o fornecedor na compra.</param>
/// <param name="DestinationState">UF para onde a mercadoria VAI: o cliente na venda, a filial na compra.</param>
public sealed record TaxCalculationInput(
    decimal Amount,
    bool InState,
    string OriginState,
    string DestinationState,
    TaxRegime Regime,
    byte GoodsOrigin,
    IcmsRule Icms,
    PisCofinsRule PisCofins,
    IbsCbsRule? IbsCbs,
    IbsCbsRates? Rates);
```

(mantenha o `<summary>` que já existe acima do record). Em `TaxCalculator.cs:66`: `InterstateIcmsRate.Resolve(input.OriginState, input.DestinationState, input.GoodsOrigin)`.

- [ ] **Step 5: Ajustar o montador da Application**

Em `NfeIssueInputAssembler.cs`, troque a linha do `Purpose` por:

```csharp
            // Venda: saída normal. Devolução de venda: ENTRADA com finalidade 4.
            Direction = returnOrigin is null ? NfeDirection.Outgoing : NfeDirection.Incoming,
            Purpose = returnOrigin is null ? NfePurpose.Normal : NfePurpose.Return,
```

Procure outros usos: `grep -rn "NfePurpose.Sale\|BranchState\|CustomerState" --include=*.cs SiagroB1.* | grep -v /bin/ | grep -v /obj/` deve voltar vazio depois das trocas (o `SalesInvoicesTaxApplyService` passa a `TaxCalculationInput` por posição — não muda).

- [ ] **Step 6: Ver passar**

Run: `dotnet test SiagroB1.Fiscal.Tests` — Expected: PASS (todos, inclusive os antigos de venda e devolução de venda).
Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Nfe|FullyQualifiedName~Tax"` — Expected: PASS.

- [ ] **Step 7: Commit (backend)**

```bash
git add SiagroB1.Fiscal/Nfe/NfeIssueInput.cs SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs SiagroB1.Fiscal/Taxes/TaxCalculationModels.cs SiagroB1.Fiscal/Taxes/TaxCalculator.cs SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs SiagroB1.Fiscal.Tests/Support/NfeTestData.cs SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs SiagroB1.Fiscal.Tests/Nfe/NfeSignerTests.cs SiagroB1.Fiscal.Tests/Taxes/TaxCalculatorTests.cs
git commit -F <msg> -- <os mesmos arquivos>
```
Mensagem: `feat(invoice): NF-e separa sentido da finalidade e o motor recebe UF de origem e destino` (corpo: por que a devolução de compra é saída com finalidade 4; `indFinal` só na saída; a entrada interestadual usa a UF do fornecedor).

---

### Task 2: Esquema — interfaces, colunas da entrada, tabela de XMLs, EDM e migration

Spec: §5, §12, D10, D13.

**Files:**
- Create: `SiagroB1.Domain/Interfaces/INfeDocument.cs`, `SiagroB1.Domain/Interfaces/INfeTaxedLine.cs`
- Rename: `SiagroB1.Domain/Enums/SalesInvoiceNfeXmlKind.cs` → `SiagroB1.Domain/Enums/NfeXmlKind.cs` (enum `NfeXmlKind`, mesmos valores) — use `git mv` e troque TODAS as referências (`grep -rln "SalesInvoiceNfeXmlKind" --include=*.cs . | grep -v /bin/ | grep -v /obj/`)
- Create: `SiagroB1.Domain/Entities/PurchaseInvoiceNfeXml.cs`, `SiagroB1.Infra/Nfe/PurchaseInvoiceNfeXmlQueries.cs`
- Modify: `SiagroB1.Domain/Entities/PurchaseInvoice.cs`, `PurchaseInvoiceItem.cs`, `SalesInvoice.cs`, `SalesInvoiceItem.cs`
- Modify: `SiagroB1.Domain/Dtos/Nfe/NfeIssueOutcomeDto.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSet + índice)
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (bloco do Documento de Entrada, ~linhas 259-273)
- Generate: `SiagroB1.Migrations/AppContext/<ts>_AddPurchaseInvoiceNfe.cs` (+ `.Designer.cs`, snapshot)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceNfeSchemaTests.cs` (novo)

**Interfaces:**
- Produces:
  - `INfeDocument` (`SiagroB1.Domain.Interfaces`): `Guid Key {get;}`, `string? BranchCode {get;}`, `string CardCode {get;}`, `InvoiceStatus? InvoiceStatus {get;}`, `string? TaxDocumentNumber/TaxDocumentSeries/ChaveNFe {get;set;}`, `NfeStatus NfeStatus {get;set;}`, `NfeEnvironment? NfeEnvironment {get;set;}`, `string? NfeRandomCode/NfeProtocol {get;set;}`, `DateTime? NfeAuthorizedAt {get;set;}`, `string? NfeStatusCode/NfeStatusReason/NfeConfirmationError {get;set;}`.
  - `INfeTaxedLine`: `Guid? Key`, `string? ItemCode/ItemName/UnitOfMeasureCode {get;}`, `decimal Quantity/UnitPrice/Total {get;}` e, com `get; set;`: `UsageCode`, `UsageName`, `Cfop`, `Ncm`, `GoodsOrigin`, `CstIcms`, `IcmsBase`, `IcmsRate`, `IcmsValue`, `IcmsBaseReduction`, `IcmsDeferral`, `IcmsOperationValue`, `IcmsDeferredValue`, `IcmsBenefitCode`, `CstPis`, `PisBase`, `PisRate`, `PisValue`, `CstCofins`, `CofinsBase`, `CofinsRate`, `CofinsValue`, `IbsCbsCst`, `IbsCbsClassCode`, `IbsCbsBase`, `CbsRate`, `CbsRateReduction`, `CbsValue`, `IbsStateRate`, `IbsMunicipalRate`, `IbsRateReduction`, `IbsStateValue`, `IbsMunicipalValue`, `MovesFiscalInventory`, `CreatesFinancialDocument`, `NfeItemNumber`.
  - `PurchaseInvoice`: `PaymentConditionCode`, `ReferencedAccessKey`, `IsNfeReturn`, `NfeStatus`, `NfeEnvironment`, `NfeRandomCode`, `NfeProtocol`, `NfeAuthorizedAt`, `NfeStatusCode`, `NfeStatusReason`, `NfeConfirmationError`, `[NotMapped] TotalInvoiceTaxes`, `[NotMapped] TotalInvoiceIbsCbs`.
  - `PurchaseInvoiceItem`: a fotografia acima + `[NotMapped] TotalTaxes`, `[NotMapped] TotalIbsCbs`.
  - `PurchaseInvoiceNfeXml` + `AppDbContext.PurchaseInvoiceNfeXmls`; `PurchaseInvoiceNfeXmlQueries.LatestAuthorizedXmlAsync(this IQueryable<PurchaseInvoiceNfeXml>, Guid)`.
  - `NfeXmlKind { Signed = 1, Authorized = 2, Denied = 3 }`; `NfeIssueOutcomeDto.From(INfeDocument)`.

- [ ] **Step 1: Teste que falha**

`SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceNfeSchemaTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Nfe;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Documento de entrada com a fotografia fiscal, o estado da NF-e e os XMLs (spec §5).</summary>
public class PurchaseInvoiceNfeSchemaTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Fact]
    public void Both_documents_share_the_nfe_interfaces()
    {
        Assert.IsAssignableFrom<INfeDocument>(new PurchaseInvoice { CardCode = "F1" });
        Assert.IsAssignableFrom<INfeDocument>(new SalesInvoice { CardCode = "C1" });
        Assert.IsAssignableFrom<INfeTaxedLine>(new PurchaseInvoiceItem());
        Assert.IsAssignableFrom<INfeTaxedLine>(new SalesInvoiceItem { ItemCode = "X", UnitOfMeasureCode = "KG" });
    }

    [Fact]
    public void Purchase_invoice_exposes_the_nfe_header_fields()
    {
        var type = Model().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == nameof(PurchaseInvoice));

        foreach (var name in new[]
                 {
                     "PaymentConditionCode", "ReferencedAccessKey", "IsNfeReturn", "NfeStatus", "NfeEnvironment",
                     "NfeProtocol", "NfeAuthorizedAt", "NfeStatusCode", "NfeStatusReason", "NfeConfirmationError",
                     "TotalInvoiceTaxes", "TotalInvoiceIbsCbs",
                 })
            Assert.NotNull(type.FindProperty(name));
    }

    [Fact]
    public void Purchase_invoice_item_exposes_the_tax_snapshot()
    {
        var type = Model().SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == nameof(PurchaseInvoiceItem));

        foreach (var name in new[]
                 {
                     "UsageCode", "UsageName", "Cfop", "Ncm", "CstIcms", "IcmsValue", "IbsCbsCst", "CbsValue",
                     "NfeItemNumber", "TotalTaxes", "TotalIbsCbs",
                 })
            Assert.NotNull(type.FindProperty(name));
    }

    [Fact]
    public async Task Latest_authorized_xml_of_a_purchase_invoice_is_found()
    {
        var db = TestDb.CreateUnitOfWork();
        var key = Guid.NewGuid();
        db.Context.PurchaseInvoiceNfeXmls.AddRange(
            new PurchaseInvoiceNfeXml { Key = Guid.NewGuid(), PurchaseInvoiceKey = key, Kind = NfeXmlKind.Signed, Xml = "<signed/>", CreatedAt = DateTime.Now },
            new PurchaseInvoiceNfeXml { Key = Guid.NewGuid(), PurchaseInvoiceKey = key, Kind = NfeXmlKind.Authorized, Xml = "<nfeProc/>", CreatedAt = DateTime.Now });
        await db.SaveChangesAsync();

        Assert.Equal("<nfeProc/>", await db.Context.PurchaseInvoiceNfeXmls.LatestAuthorizedXmlAsync(key));
    }

    [Fact]
    public void Line_totals_follow_the_snapshot()
    {
        var item = new PurchaseInvoiceItem { IcmsValue = 10m, PisValue = 1m, CofinsValue = 2m, CbsValue = 3m, IbsStateValue = 0.5m };
        var invoice = new PurchaseInvoice { CardCode = "F1" };
        invoice.AddItem(item);

        Assert.Equal(13m, item.TotalTaxes);
        Assert.Equal(3.5m, item.TotalIbsCbs);
        Assert.Equal(13m, invoice.TotalInvoiceTaxes);
        Assert.Equal(3.5m, invoice.TotalInvoiceIbsCbs);
    }
}
```

Observação: o InMemory não precisa de FK real, mas `PurchaseInvoiceNfeXml.PurchaseInvoiceKey` sem documento pode falhar se o EF exigir a navegação — se falhar por isso, crie o `PurchaseInvoice` antes (mesma chave) no teste.

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceNfeSchemaTests"`
Expected: falha de compilação (tipos inexistentes).

- [ ] **Step 2: Interfaces de domínio**

`SiagroB1.Domain/Interfaces/INfeDocument.cs`:

```csharp
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Cabeçalho de documento que emite NF-e pelo Siagro — o documento de saída e o de entrada. É o que o
/// pipeline da emissão (reserva, assinatura, transmissão, retorno, consulta) lê e escreve. As
/// propriedades já existem nas entidades com estes nomes: a interface não muda o mapeamento do EF nem
/// o EDM.
/// </summary>
public interface INfeDocument
{
    Guid Key { get; }
    string? BranchCode { get; }
    string CardCode { get; }
    InvoiceStatus? InvoiceStatus { get; }
    string? TaxDocumentNumber { get; set; }
    string? TaxDocumentSeries { get; set; }
    string? ChaveNFe { get; set; }
    NfeStatus NfeStatus { get; set; }
    NfeEnvironment? NfeEnvironment { get; set; }
    string? NfeRandomCode { get; set; }
    string? NfeProtocol { get; set; }
    DateTime? NfeAuthorizedAt { get; set; }
    string? NfeStatusCode { get; set; }
    string? NfeStatusReason { get; set; }
    string? NfeConfirmationError { get; set; }
}
```

`SiagroB1.Domain/Interfaces/INfeTaxedLine.cs`:

```csharp
namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Linha de documento fiscal com a fotografia dos tributos (SP1 §5.6) — a linha do documento de saída e
/// a do de entrada. O cálculo escreve aqui e o montador da NF-e lê daqui, sem recalcular.
/// </summary>
public interface INfeTaxedLine
{
    Guid? Key { get; }
    string? ItemCode { get; }
    string? ItemName { get; }
    string? UnitOfMeasureCode { get; }
    decimal Quantity { get; }
    decimal UnitPrice { get; }
    decimal Total { get; }

    int? UsageCode { get; set; }
    string? UsageName { get; set; }
    string? Cfop { get; set; }
    string? Ncm { get; set; }
    byte? GoodsOrigin { get; set; }

    string? CstIcms { get; set; }
    decimal IcmsBase { get; set; }
    decimal IcmsRate { get; set; }
    decimal IcmsValue { get; set; }
    decimal IcmsBaseReduction { get; set; }
    decimal IcmsDeferral { get; set; }
    decimal IcmsOperationValue { get; set; }
    decimal IcmsDeferredValue { get; set; }
    string? IcmsBenefitCode { get; set; }

    string? CstPis { get; set; }
    decimal PisBase { get; set; }
    decimal PisRate { get; set; }
    decimal PisValue { get; set; }
    string? CstCofins { get; set; }
    decimal CofinsBase { get; set; }
    decimal CofinsRate { get; set; }
    decimal CofinsValue { get; set; }

    string? IbsCbsCst { get; set; }
    string? IbsCbsClassCode { get; set; }
    decimal IbsCbsBase { get; set; }
    decimal CbsRate { get; set; }
    decimal CbsRateReduction { get; set; }
    decimal CbsValue { get; set; }
    decimal IbsStateRate { get; set; }
    decimal IbsMunicipalRate { get; set; }
    decimal IbsRateReduction { get; set; }
    decimal IbsStateValue { get; set; }
    decimal IbsMunicipalValue { get; set; }

    bool MovesFiscalInventory { get; set; }
    bool CreatesFinancialDocument { get; set; }

    /// <summary>O <c>det/@nItem</c> gravado na emissão (a devolução referencia o item por ele).</summary>
    int? NfeItemNumber { get; set; }
}
```

`SalesInvoice : DocumentEntity, INfeDocument` e `SalesInvoiceItem : INfeTaxedLine` — só a declaração (as propriedades já existem; `SalesInvoice.InvoiceStatus` já é `InvoiceStatus?`). Acrescente `using SiagroB1.Domain.Interfaces;`.

- [ ] **Step 3: Colunas novas da entrada**

`PurchaseInvoice : DocumentEntity, INfeDocument` e, depois de `FreightTerms`:

```csharp
    /// <summary>Condição de pagamento (NF-e STANDALONE): vira cobr/dup e pag na entrada própria. Sem FK, como na saída.</summary>
    public int? PaymentConditionCode { get; set; }

    /// <summary>
    /// NF-e referenciada pela entrada própria (normalmente a nota do produtor): vai no <c>ide/NFref/refNFe</c>.
    /// </summary>
    [Column(TypeName = "VARCHAR(44)")]
    public string? ReferencedAccessKey { get; set; }

    /// <summary>
    /// Devolução de compra criada pelo "Devolver" a partir de uma entrada própria autorizada: sai com NF-e
    /// PRÓPRIA de saída (finalidade 4). Só <c>PurchaseInvoicesNfeReturnCreateService</c> grava <c>true</c>.
    /// </summary>
    public bool IsNfeReturn { get; set; }

    // --- NF-e (mesmas colunas do documento de saída, spec 2a §9.1) ---
    public NfeStatus NfeStatus { get; set; } = NfeStatus.None;

    public NfeEnvironment? NfeEnvironment { get; set; }

    [Column(TypeName = "VARCHAR(8)")]
    public string? NfeRandomCode { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? NfeProtocol { get; set; }

    public DateTime? NfeAuthorizedAt { get; set; }

    [Column(TypeName = "VARCHAR(4)")]
    public string? NfeStatusCode { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? NfeStatusReason { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? NfeConfirmationError { get; set; }

    /// <summary>O status desta entidade não é anulável; a interface comum o lê como na saída.</summary>
    InvoiceStatus? INfeDocument.InvoiceStatus => InvoiceStatus;
```

e junto do `TotalInvoiceItems`:

```csharp
    [NotMapped]
    public decimal TotalInvoiceTaxes => Items.Sum(i => i.TotalTaxes);

    [NotMapped]
    public decimal TotalInvoiceIbsCbs => Items.Sum(i => i.TotalIbsCbs);
```

`PurchaseInvoiceItem : INfeTaxedLine` e, depois de `PurchaseInvoiceItemOriginKey`/navegação, a fotografia — **mesmos tipos e `Column` da `SalesInvoiceItem.cs:132-243`** (copie o bloco de `UsageCode` até `NfeItemNumber`, sem `CostCenterCode`/`LedgerAccountCode`), e os dois `[NotMapped]`:

```csharp
    /// <summary>ICMS + PIS + COFINS da fotografia (o IBS/CBS de 2026 é informativo e fica fora).</summary>
    [NotMapped]
    public decimal TotalTaxes => IcmsValue + PisValue + CofinsValue;

    [NotMapped]
    public decimal TotalIbsCbs => CbsValue + IbsStateValue + IbsMunicipalValue;
```

Atualize o `<summary>` da classe: os campos fiscais existem e só são preenchidos pelo cálculo da emissão própria (modo NF-e); documento de terceiro os deixa vazios.

- [ ] **Step 4: XMLs da entrada, consulta, enum e DTO**

`git mv SiagroB1.Domain/Enums/SalesInvoiceNfeXmlKind.cs SiagroB1.Domain/Enums/NfeXmlKind.cs`, renomeie o enum para `NfeXmlKind` (summary: "Tipo do XML da NF-e guardado com o documento (saída ou entrada).") e troque as referências em todo o repositório (código e testes).

`SiagroB1.Domain/Entities/PurchaseInvoiceNfeXml.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// XMLs da NF-e do documento de entrada — espelho de <see cref="SalesInvoiceNfeXml"/>. O assinado é gravado
/// ANTES do envio. Não é exposto no OData (download por função própria).
/// </summary>
[Table("PURCHASE_INVOICE_NFE_XMLS")]
public class PurchaseInvoiceNfeXml
{
    [Key]
    public Guid Key { get; set; }

    [ForeignKey(nameof(PurchaseInvoice))]
    public Guid PurchaseInvoiceKey { get; set; }

    public virtual PurchaseInvoice? PurchaseInvoice { get; set; }

    public NfeXmlKind Kind { get; set; }

    [Column(TypeName = "NVARCHAR(MAX)")]
    public required string Xml { get; set; }

    public DateTime CreatedAt { get; set; }
}
```

`AppDbContext`: `public DbSet<PurchaseInvoiceNfeXml> PurchaseInvoiceNfeXmls { get; set; }` (perto de `SalesInvoiceNfeXmls`) e, no `OnModelCreating`, junto das configurações de `PurchaseInvoice`:

```csharp
        modelBuilder.Entity<PurchaseInvoiceNfeXml>()
            .HasIndex(x => new { x.PurchaseInvoiceKey, x.Kind });
```

`SiagroB1.Infra/Nfe/PurchaseInvoiceNfeXmlQueries.cs` — espelho de `SalesInvoiceNfeXmlQueries` (mesma mensagem):

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Infra.Nfe;

/// <summary>Consulta compartilhada pelo download do XML (Application) e pelo DANFE (Reports), na entrada.</summary>
public static class PurchaseInvoiceNfeXmlQueries
{
    /// <summary>procNFe autorizado mais recente do documento; sem ele, <see cref="NotFoundException"/>.</summary>
    public static async Task<string> LatestAuthorizedXmlAsync(this IQueryable<PurchaseInvoiceNfeXml> xmls, Guid invoiceKey) =>
        await xmls.AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == invoiceKey && x.Kind == NfeXmlKind.Authorized)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Xml)
            .FirstOrDefaultAsync()
        ?? throw new NotFoundException("Este documento não tem NF-e autorizada.");
}
```

`NfeIssueOutcomeDto.From(SalesInvoice invoice)` → `From(INfeDocument invoice)` (corpo igual; `using SiagroB1.Domain.Interfaces;`).

- [ ] **Step 5: EDM**

Em `ODataConfigurations.cs`, logo depois dos `AddProperty` de `PurchaseInvoiceItem` (`Difference`):

```csharp
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoice))
            .AddProperty(typeof(PurchaseInvoice).GetProperty(nameof(PurchaseInvoice.TotalInvoiceTaxes)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoice))
            .AddProperty(typeof(PurchaseInvoice).GetProperty(nameof(PurchaseInvoice.TotalInvoiceIbsCbs)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoiceItem))
            .AddProperty(typeof(PurchaseInvoiceItem).GetProperty(nameof(PurchaseInvoiceItem.TotalTaxes)));
        modelBuilder.StructuralTypes.First(t => t.ClrType == typeof(PurchaseInvoiceItem))
            .AddProperty(typeof(PurchaseInvoiceItem).GetProperty(nameof(PurchaseInvoiceItem.TotalIbsCbs)));
```

- [ ] **Step 6: Ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceNfeSchemaTests|FullyQualifiedName~Nfe"` — Expected: PASS.

- [ ] **Step 7: Migration**

Pare Web/Gateway/Reports que você tenha iniciado. A partir de `siagro-b1-backend/`:

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddPurchaseInvoiceNfe --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```

**Leia a migration gerada:** só `AddColumn` em `PURCHASE_INVOICES` (11 colunas do spec §5.1, `IsNfeReturn`/`NfeStatus` com default 0) e em `PURCHASE_INVOICES_ITEMS` (fotografia + `NfeItemNumber`, defaults 0 nos decimais e bits), `CreateTable PURCHASE_INVOICE_NFE_XMLS` com FK cascade e o índice. Nada em outras tabelas (a renomeação do enum não toca o banco). Se aparecer qualquer outra coisa, pare e investigue (modelo fora de sincronia).

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --no-build
```

Expected: "No changes have been made to the model since the last migration." e a migration aplicada no `CEAGUI_SIAGRO_DEV`. Depois: `dotnet build SiagroB1.sln` sem erro e `dotnet test SiagroB1.Application.Tests` + `dotnet test SiagroB1.Fiscal.Tests` verdes.

- [ ] **Step 8: Commit (backend)**

`git add` dos arquivos novos (interfaces, entidade, consulta, teste, migration + Designer) e commit com pathspec de todos os arquivos tocados, incluindo o `git mv` e o snapshot. Mensagem: `feat(invoice): documento de entrada com fotografia fiscal, estado da NF-e e XMLs` + corpo + trailer `DB: AddPurchaseInvoiceNfe`.

---

### Task 3: Peças compartilhadas do cálculo — linha, fotografia, conferência e nItem

Spec: §6.1. **Comportamento do documento de saída inalterado** (a suíte dele é a rede).

**Files:**
- Create: `SiagroB1.Application/Services/Taxes/TaxLineCalculator.cs`
- Move/rename: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceTaxSnapshot.cs` → `SiagroB1.Application/Services/Taxes/TaxSnapshot.cs` (classe `TaxSnapshot`, namespace `SiagroB1.Application.Services.Taxes`)
- Move/rename: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeReturnConference.cs` → `SiagroB1.Application/Services/Taxes/NfeReturnConference.cs`
- Move/rename: `SiagroB1.Application/Services/Nfe/SalesInvoiceNfeItemNumbering.cs` → `SiagroB1.Application/Services/Nfe/NfeItemNumbering.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs` e todos os usos das três classes renomeadas (`grep -rn "SalesInvoiceTaxSnapshot\|SalesInvoiceNfeReturnConference\|SalesInvoiceNfeItemNumbering" --include=*.cs . | grep -v /bin/ | grep -v /obj/`)
- Test: `SiagroB1.Application.Tests/Taxes/TaxLineCalculatorTests.cs` (novo), `SiagroB1.Application.Tests/Taxes/NfeReturnConferenceTests.cs` (novo)

**Interfaces:**
- Consumes: `INfeTaxedLine` (Task 2), `TaxCalculationInput` com `OriginState`/`DestinationState` (Task 1).
- Produces:
  - `public sealed record TaxLineRequest(UsageModel Usage, string? ItemCode, decimal Amount, bool InState, string OriginState, string DestinationState, TaxRegime Regime, DateOnly RateDate, bool IncomingCfop);`
  - `public sealed record TaxLineResult(string Cfop, Item Product, TaxCalculationResult Taxes);`
  - `public class TaxLineCalculator(IUnitOfWork db, IbsCbsRatesService ibsCbsRatesService)` com `Task<TaxLineResult> CalculateAsync(TaxLineRequest request)`, `static void Apply(INfeTaxedLine line, UsageModel usage, TaxLineResult result)`, `Task<Branch> LoadBranchAsync(string? branchCode)` e `static Task<string> LoadPartnerStateAsync(IBusinessPartnerService partners, string cardCode)`.
  - `TaxSnapshot.LockedProperties`, `TaxSnapshot.Write(INfeTaxedLine line, TaxCalculationResult r)`, `TaxSnapshot.RestoreLocked(EntityEntry entry)`.
  - `NfeReturnConference.Ensure(INfeTaxedLine returned, INfeTaxedLine origin, string usageName, string operation)` — `operation` = `"venda"` ou `"compra"`.
  - `NfeItemNumbering.Ordered<TLine>(IEnumerable<TLine>) where TLine : INfeTaxedLine`, `Renumber<TLine>(...)`, `OriginNumber(INfeTaxedLine originItem, int originItemCount)`.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Taxes/TaxLineCalculatorTests.cs`:

```csharp
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Taxes;

/// <summary>A linha calculada pela natureza — peça comum ao documento de saída e ao de entrada.</summary>
public class TaxLineCalculatorTests
{
    private static async Task<UnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", Ncm = "12019000", GoodsOrigin = 0 });
        db.Context.IbsCbsRates.Add(new IbsCbsRate { StartDate = new DateOnly(2026, 1, 1), CbsRate = 0.9m, IbsStateRate = 0.1m, IbsMunicipalRate = 0m });
        await db.SaveChangesAsync();
        return db;
    }

    private static UsageModel Purchase() => new()
    {
        Code = 6, Name = "COMPRA DE MERCADORIA", Direction = UsageDirection.Incoming,
        CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
        IcmsInStateCst = "00", IcmsInStateRate = 18m, IcmsOutStateCst = "00",
        PisCst = "74", CofinsCst = "74",
    };

    private static TaxLineRequest Request(UsageModel usage, bool inState = true, string origin = "SP") =>
        new(usage, "SOJA", 1000m, inState, origin, "SP", TaxRegime.Normal, new DateOnly(2026, 10, 5), IncomingCfop: true);

    [Fact]
    public async Task Incoming_cfop_follows_the_state_comparison()
    {
        var db = await SeedAsync();
        var calculator = new TaxLineCalculator(db, new IbsCbsRatesService(db));

        Assert.Equal("1102", (await calculator.CalculateAsync(Request(Purchase()))).Cfop);
        Assert.Equal("2102", (await calculator.CalculateAsync(Request(Purchase(), inState: false, origin: "BA"))).Cfop);
    }

    [Fact]
    public async Task Interstate_entry_rate_comes_from_the_supplier_state()
    {
        var db = await SeedAsync();
        var calculator = new TaxLineCalculator(db, new IbsCbsRatesService(db));

        var result = await calculator.CalculateAsync(Request(Purchase(), inState: false, origin: "BA"));

        Assert.Equal(12m, result.Taxes.IcmsRate);
    }

    [Fact]
    public async Task Missing_incoming_cfop_names_the_kind()
    {
        var db = await SeedAsync();
        var usage = Purchase();
        usage.CfopIncomingInState = null;

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => new TaxLineCalculator(db, new IbsCbsRatesService(db)).CalculateAsync(Request(usage)));

        Assert.Equal("Natureza de operação COMPRA DE MERCADORIA está sem CFOP de entrada dentro do estado.", e.Message);
    }

    [Fact]
    public async Task Line_without_product_is_refused()
    {
        var db = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => new TaxLineCalculator(db, new IbsCbsRatesService(db)).CalculateAsync(Request(Purchase()) with { ItemCode = null }));

        Assert.Equal("Informe o produto do item.", e.Message);
    }

    [Fact]
    public async Task Apply_writes_usage_cfop_product_and_snapshot()
    {
        var db = await SeedAsync();
        var usage = Purchase();
        var result = await new TaxLineCalculator(db, new IbsCbsRatesService(db)).CalculateAsync(Request(usage));
        var line = new PurchaseInvoiceItem { ItemCode = "SOJA", Quantity = 500m, UnitPrice = 2m };

        TaxLineCalculator.Apply(line, usage, result);

        Assert.Equal(6, line.UsageCode);
        Assert.Equal("COMPRA DE MERCADORIA", line.UsageName);
        Assert.Equal("1102", line.Cfop);
        Assert.Equal("12019000", line.Ncm);
        Assert.Equal((byte)0, line.GoodsOrigin);
        Assert.Equal("00", line.CstIcms);
        Assert.Equal(180m, line.IcmsValue);
        Assert.Equal("74", line.CstPis);
    }
}
```

(Confira os nomes reais das entidades `IbsCbsRate`/`DbSet IbsCbsRates` e das propriedades de `UsageModel`/`Item` antes de rodar; se diferirem, ajuste o TESTE ao nome real — não renomeie entidade.)

`SiagroB1.Application.Tests/Taxes/NfeReturnConferenceTests.cs`:

```csharp
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Taxes;

public class NfeReturnConferenceTests
{
    [Fact]
    public void Purchase_return_names_the_purchase_in_the_message()
    {
        var bought = new PurchaseInvoiceItem { ItemCode = "SOJA", CstIcms = "51", IcmsRate = 18m };
        var returned = new PurchaseInvoiceItem { ItemCode = "SOJA", CstIcms = "00", IcmsRate = 18m };

        var e = Assert.Throws<DefaultException>(() => NfeReturnConference.Ensure(returned, bought, "DEVOLUCAO DE COMPRA", "compra"));

        Assert.Equal(
            "Item SOJA: a natureza de devolução DEVOLUCAO DE COMPRA não reproduz a tributação da compra — " +
            "CST do ICMS: compra 51, devolução 00.", e.Message);
    }

    [Fact]
    public void Matching_taxation_passes()
    {
        var bought = new PurchaseInvoiceItem { ItemCode = "SOJA", CstIcms = "51", IcmsRate = 18m, IcmsDeferral = 100m };
        var returned = new PurchaseInvoiceItem { ItemCode = "SOJA", CstIcms = "51", IcmsRate = 18m, IcmsDeferral = 100m };

        NfeReturnConference.Ensure(returned, bought, "DEVOLUCAO DE COMPRA", "compra");
    }
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~TaxLineCalculatorTests|FullyQualifiedName~NfeReturnConferenceTests"` — Expected: falha de compilação.

- [ ] **Step 2: `TaxLineCalculator`**

Mova de `SalesInvoicesTaxApplyService` (sem mudar mensagens) `LoadProductAsync`, `ResolveCfop`, `ResolveIcmsRule`, `ResolvePisCofinsRule` e `ResolveIbsCbsAsync` para `SiagroB1.Application/Services/Taxes/TaxLineCalculator.cs`. Mova também `LoadBranchAsync` (passa a `public Task<Branch> LoadBranchAsync(string? branchCode)`, mesmo corpo e mensagem) e `LoadCustomerStateAsync` (passa a `public static async Task<string> LoadPartnerStateAsync(IBusinessPartnerService partners, string cardCode)`, mesmo corpo e mensagem "Parceiro {cardCode} …"): o cálculo da entrada (Task 5) usa os dois.

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Taxes;

/// <param name="OriginState">UF de onde a mercadoria sai (filial na venda e na devolução de venda; fornecedor na compra e na devolução de compra).</param>
/// <param name="IncomingCfop">Lê os CFOPs de entrada da natureza (1xxx/2xxx) em vez dos de saída.</param>
public sealed record TaxLineRequest(
    UsageModel Usage, string? ItemCode, decimal Amount, bool InState, string OriginState, string DestinationState,
    TaxRegime Regime, DateOnly RateDate, bool IncomingCfop);

public sealed record TaxLineResult(string Cfop, Item Product, TaxCalculationResult Taxes);

/// <summary>
/// Uma linha calculada pela natureza (SP1 §6): CFOP pelo sentido e pela UF, regras de ICMS/PIS/COFINS/IBS-CBS
/// da natureza, produto com NCM e origem, alíquota IBS/CBS vigente. Comum ao documento de saída e ao de
/// entrada; quem decide QUAL natureza, quais UFs e qual data é o serviço de cada documento.
/// </summary>
public class TaxLineCalculator(IUnitOfWork db, IbsCbsRatesService ibsCbsRatesService)
{
    public async Task<TaxLineResult> CalculateAsync(TaxLineRequest request)
    {
        var product = await LoadProductAsync(request.ItemCode);
        var cfop = ResolveCfop(request.Usage, request.InState, request.IncomingCfop);
        var icms = ResolveIcmsRule(request.Usage, request.InState, request.Regime);
        var pisCofins = ResolvePisCofinsRule(request.Usage);
        var (ibsCbs, rates) = await ResolveIbsCbsAsync(request.Usage, request.RateDate);

        var taxes = TaxCalculator.Calculate(new TaxCalculationInput(
            request.Amount, request.InState, request.OriginState, request.DestinationState, request.Regime,
            product.GoodsOrigin!.Value, icms, pisCofins, ibsCbs, rates));

        return new TaxLineResult(cfop, product, taxes);
    }

    /// <summary>Grava na linha a natureza, o CFOP, NCM/origem do produto, as flags e a fotografia.</summary>
    public static void Apply(INfeTaxedLine line, UsageModel usage, TaxLineResult result)
    {
        line.UsageCode = usage.Code;
        line.UsageName = usage.Name;
        line.Cfop = result.Cfop;
        line.Ncm = result.Product.Ncm;
        line.GoodsOrigin = result.Product.GoodsOrigin;
        line.MovesFiscalInventory = usage.MovesFiscalInventory;
        line.CreatesFinancialDocument = usage.CreatesFinancialDocument;

        TaxSnapshot.Write(line, result.Taxes);
    }

    private async Task<Item> LoadProductAsync(string? itemCode)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
            throw new DefaultException("Informe o produto do item.");

        // (corpo atual de SalesInvoicesTaxApplyService.LoadProductAsync, sem mudança)
    }

    // LoadBranchAsync (público), LoadPartnerStateAsync (público, static), ResolveCfop, ResolveIcmsRule,
    // ResolvePisCofinsRule, ResolveIbsCbsAsync: movidos SEM mudança de corpo nem de mensagem.
}
```

(Substitua os dois comentários pelos corpos movidos — o código final não tem comentário de "movido".)

`SalesInvoicesTaxApplyService`: **mesmo construtor**; acrescente `private readonly TaxLineCalculator _lines = new(db, ibsCbsRatesService);` e troque o corpo do laço por:

```csharp
        foreach (var item in lines)
        {
            var usage = await ResolveUsageAsync(item, usages, ownReturn);
            // Venda e devolução de venda: a mercadoria sai da filial para o cliente (na devolução, a operação
            // é a da venda, que ela anula).
            var line = await _lines.CalculateAsync(new TaxLineRequest(
                usage, item.ItemCode, item.Total, inState, branch.StateCode!, customerState,
                branch.TaxRegime!.Value, rateDate, IncomingCfop: ownReturn));

            TaxLineCalculator.Apply(item, usage, line);

            if (ownReturn)
                NfeReturnConference.Ensure(item, OriginOf(item, origins), usage.Name, "venda");
        }
```

e, no começo do `ApplyAsync`, troque `LoadBranchAsync(invoice.BranchCode)` por `_lines.LoadBranchAsync(invoice.BranchCode)` e `LoadCustomerStateAsync(invoice.CardCode)` por `TaxLineCalculator.LoadPartnerStateAsync(businessPartnerService, invoice.CardCode)`. Apague os métodos movidos.

- [ ] **Step 3: Fotografia, conferência e numeração genéricas**

`TaxSnapshot` (renomeado, em `Services/Taxes`): `LockedProperties` com `nameof(INfeTaxedLine.Cfop)` etc. (mesma lista e mesma ordem de hoje); `Write(INfeTaxedLine item, TaxCalculationResult r)` (corpo igual); `RestoreLocked(EntityEntry entry)` (parâmetro passa a ser o `EntityEntry` não genérico — `EntityEntry<SalesInvoiceItem>` continua aceito). Summary: "comum à linha de saída e à de entrada".

`NfeReturnConference` (renomeado, em `Services/Taxes`): mesma lista de campos; assinatura `Ensure(INfeTaxedLine returned, INfeTaxedLine origin, string usageName, string operation)`; mensagem:

```csharp
        throw new DefaultException(
            $"Item {returned.ItemCode}: a natureza de devolução {usageName} não reproduz a tributação da {operation} — " +
            $"{field}: {operation} {originValue}, devolução {returnedValue}.");
```

Com `operation = "venda"` a mensagem fica idêntica à de hoje (os testes da devolução de venda não mudam).

`NfeItemNumbering` (renomeado, em `Services/Nfe`): métodos genéricos com `where TLine : INfeTaxedLine` (mesma ordenação: `NfeItemNumber ?? int.MaxValue`, depois `Key` em memória) e `OriginNumber(INfeTaxedLine originItem, int originItemCount)`.

Atualize todos os usos (assembler, emissão, devolução de venda, prontidão, itens devolvíveis, travas, testes). Os testes antigos só trocam o identificador.

- [ ] **Step 4: Ver passar**

Run: `dotnet test SiagroB1.Application.Tests` — Expected: PASS (suíte inteira; a do documento de saída sem mudança de asserção).

- [ ] **Step 5: Commit (backend)**

Mensagem: `refactor(invoice): cálculo de linha, fotografia, conferência e nItem comuns aos dois documentos`. Pathspec com os arquivos movidos (`git mv`), os novos e todos os modificados.

---

### Task 4: Natureza de devolução nos dois sentidos

Spec: D4, §9.1.

**Files:**
- Modify: `SiagroB1.Application/Services/UsageService.cs` (`ValidateReturnUsageAsync` ~235-254; trava de troca de tipo ~135-155; `DeleteAsync` ~191-197)
- Test: `SiagroB1.Application.Tests/Usages/UsageReturnUsageTests.cs`

**Interfaces:**
- Produces: natureza de Entrada pode ter `ReturnUsageCode` apontando natureza de Saída; mensagens novas: "A natureza de devolução {Name} precisa ser de saída." e "Natureza de operação {Name} já foi utilizada em documento de entrada. …".

- [ ] **Step 1: Testes que falham**

Em `UsageReturnUsageTests.cs`, o teste que hoje espera "Só natureza de saída tem natureza de devolução." (linha ~61) passa a provar o caminho novo — reescreva-o como:

```csharp
    [Fact]
    public async Task Incoming_usage_points_to_an_outgoing_return_usage()
    {
        // natureza de compra (Entrada) → devolução de compra (Saída): o vínculo da devolução de compra.
        var db = TestDb.CreateUnitOfWork();
        var returnCode = await CreateAsync(db, "Devolução de compra", UsageDirection.Outgoing);

        var created = await Service(db).CreateAsync(Model("Compra", UsageDirection.Incoming, returnCode));

        Assert.Equal(returnCode, created.ReturnUsageCode);
    }

    [Fact]
    public async Task Incoming_usage_cannot_point_to_another_incoming_usage()
    {
        var db = TestDb.CreateUnitOfWork();
        var returnCode = await CreateAsync(db, "Entrada 2", UsageDirection.Incoming);

        Assert.Equal("A natureza de devolução Entrada 2 precisa ser de saída.",
            await Rejects(() => Service(db).CreateAsync(Model("Compra", UsageDirection.Incoming, returnCode))));
    }
```

Use os helpers que o arquivo já tem (`Service`, `Rejects`; se não houver `CreateAsync`/`Model` com essa forma, crie-os no arquivo com a mesma lógica que os testes vizinhos usam para montar `UsageModel` e gravar). Acrescente também:

```csharp
    [Fact]
    public async Task Usage_used_by_a_purchase_invoice_line_cannot_change_direction()
    {
        var db = TestDb.CreateUnitOfWork();
        var code = await CreateAsync(db, "Compra", UsageDirection.Incoming);
        db.Context.PurchaseInvoicesItems.Add(new PurchaseInvoiceItem { Key = Guid.NewGuid(), ItemCode = "SOJA", UsageCode = code });
        await db.SaveChangesAsync();

        var model = Model("Compra", UsageDirection.Outgoing, null);

        Assert.Equal(
            "Natureza de operação Compra já foi utilizada em documento de entrada. O tipo não pode ser alterado.",
            await Rejects(() => Service(db).UpdateAsync(code, model)));
    }

    [Fact]
    public async Task Usage_used_by_a_purchase_invoice_line_cannot_be_deleted()
    {
        var db = TestDb.CreateUnitOfWork();
        var code = await CreateAsync(db, "Compra", UsageDirection.Incoming);
        db.Context.PurchaseInvoicesItems.Add(new PurchaseInvoiceItem { Key = Guid.NewGuid(), ItemCode = "SOJA", UsageCode = code });
        await db.SaveChangesAsync();

        Assert.Equal(
            "Natureza de operação Compra já foi utilizada em documento de entrada. Inative-a em vez de excluir.",
            await Rejects(() => Service(db).DeleteAsync(code)));
    }

    [Fact]
    public async Task Outgoing_return_usage_of_a_purchase_cannot_become_incoming()
    {
        var db = TestDb.CreateUnitOfWork();
        var returnCode = await CreateAsync(db, "Devolução de compra", UsageDirection.Outgoing);
        await Service(db).CreateAsync(Model("Compra", UsageDirection.Incoming, returnCode));

        Assert.Equal(
            "A natureza Devolução de compra é a natureza de devolução de outra natureza de entrada: o tipo não pode virar Entrada.",
            await Rejects(() => Service(db).UpdateAsync(returnCode, Model("Devolução de compra", UsageDirection.Incoming, null))));
    }
```

(O `Model(...)` de update precisa trazer os campos obrigatórios que o `UpdateAsync` exige — siga os testes de update existentes no arquivo.)

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~UsageReturnUsageTests"` — Expected: os novos FALHAM (e o antigo reescrito também).

- [ ] **Step 2: Implementação**

`ValidateReturnUsageAsync`:

```csharp
    /// <summary>
    /// A natureza de devolução tem o sentido OPOSTO: a de venda (Saída) aponta a de devolução de venda
    /// (Entrada); a de compra (Entrada) aponta a de devolução de compra (Saída).
    /// </summary>
    private async Task ValidateReturnUsageAsync(UsageModel model, int? key)
    {
        if (model.ReturnUsageCode is not { } returnCode)
            return;

        if (key == returnCode)
            throw new DefaultException("A natureza de devolução não pode ser a própria natureza.");

        var target = await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(x => x.Code == returnCode)
                     ?? throw new DefaultException($"Natureza de devolução {returnCode} não encontrada.");

        var expected = (model.Direction ?? UsageDirection.Outgoing) == UsageDirection.Outgoing
            ? UsageDirection.Incoming
            : UsageDirection.Outgoing;

        if (target.Direction != expected)
            throw new DefaultException(
                $"A natureza de devolução {target.Name} precisa ser de {(expected == UsageDirection.Incoming ? "entrada" : "saída")}.");

        if (target.Inactive)
            throw new DefaultException($"A natureza de devolução {target.Name} está inativa.");
    }
```

Na trava de troca de tipo do `UpdateAsync`, depois do teste com `SalesInvoicesItems` (mensagem de saída mantida):

```csharp
        if (newDirection != usage.Direction &&
            await db.Context.PurchaseInvoicesItems.AnyAsync(x => x.UsageCode == key))
        {
            throw new DefaultException(
                $"Natureza de operação {usage.Name} já foi utilizada em documento de entrada. " +
                "O tipo não pode ser alterado.");
        }
```

e, depois da trava existente "Entrada → Saída", a simétrica:

```csharp
        // Natureza de saída que é a devolução de uma natureza de compra viraria uma "devolução" de entrada:
        // a devolução de compra criada pelo Devolver nasceria com CFOP de entrada.
        if (newDirection == UsageDirection.Incoming && usage.Direction == UsageDirection.Outgoing &&
            await db.Context.Usages.AnyAsync(x => x.ReturnUsageCode == key))
        {
            throw new DefaultException(
                $"A natureza {usage.Name} é a natureza de devolução de outra natureza de entrada: " +
                "o tipo não pode virar Entrada.");
        }
```

No `DeleteAsync`, depois do teste de `SalesInvoicesItems`:

```csharp
        if (await db.Context.PurchaseInvoicesItems.AnyAsync(x => x.UsageCode == key))
        {
            throw new DefaultException(
                $"Natureza de operação {usage.Name} já foi utilizada em documento de entrada. " +
                "Inative-a em vez de excluir.");
        }
```

Atenção: a trava simétrica só pode disparar quando a natureza é devolução **de uma natureza de Entrada**; se hoje uma Saída fosse devolução de uma Saída (impossível pela validação), não importa. Mantenha a trava existente intacta.

- [ ] **Step 3: Ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Usage"` — Expected: PASS.

- [ ] **Step 4: Commit (backend)**

Mensagem: `feat(master-data): natureza de compra aponta a natureza de devolução de compra`.

---

### Task 5: Cálculo da entrada própria e da devolução de compra

Spec: §6.2, D5, D6, D7, D8, D10, §7 (NF-e referenciada e devolução manual).

**Files:**
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs`
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceAccessKey.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs`, `PurchaseInvoicesUpdateService.cs`, `PurchaseInvoicesItemsCreateService.cs`, `PurchaseInvoicesItemsUpdateService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (registrar `PurchaseInvoicesTaxApplyService` junto de `SalesInvoicesTaxApplyService`)
- Modify (fábricas de teste): `SiagroB1.Application.Tests/Support/TaxTestServices.cs` e os testes de `SiagroB1.Application.Tests/PurchaseInvoices/` que constroem os quatro serviços
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesTaxApplyTests.cs` (novo)

**Interfaces:**
- Consumes: `TaxLineCalculator`, `TaxLineRequest`, `TaxLineCalculator.Apply/LoadBranchAsync/LoadPartnerStateAsync`, `NfeReturnConference.Ensure(..., "compra")` (Task 3); colunas da Task 2.
- Produces:
  - `public class PurchaseInvoicesTaxApplyService(IUnitOfWork db, TaxCalculationGate gate, IUsage usageService, IBusinessPartnerService businessPartnerService, IbsCbsRatesService ibsCbsRatesService)` com `Task<bool> IsActiveForAsync(PurchaseInvoice)`, `Task<bool> IsBranchActiveAsync(string? branchCode)`, `static bool IsOwnNfeReturn(PurchaseInvoice)`, `Task ApplyAsync(PurchaseInvoice invoice, IEnumerable<PurchaseInvoiceItem> items)`.
  - `PurchaseInvoiceAccessKey.Normalize(string? value)` (só dígitos ou null) e `PurchaseInvoiceAccessKey.EnsureValidReference(PurchaseInvoice invoice)`.
  - `PurchaseInvoicesCreateService(IUnitOfWork db, IBusinessPartnerService businessPartnerService, IItemService itemService, PurchaseInvoicesTaxApplyService taxApply)`; `ExecuteAsync(PurchaseInvoice invoice, string userName, bool nfeReturn = false)` — `nfeReturn` só é usado pela Task 9 (por ora: `invoice.IsNfeReturn = nfeReturn && invoice.InvoiceType == PurchaseInvoiceType.Return`).
  - `PurchaseInvoicesUpdateService(IUnitOfWork db, IBusinessPartnerService businessPartnerService, IItemService itemService, PurchaseInvoicesTaxApplyService taxApply)`; `PurchaseInvoicesItemsCreateService(IUnitOfWork db, IItemService itemService, PurchaseInvoicesTaxApplyService taxApply)`; `PurchaseInvoicesItemsUpdateService(IUnitOfWork db, IItemService itemService, PurchaseInvoicesTaxApplyService taxApply)`.
  - `TaxTestServices.PurchaseApply(UnitOfWork db, IBusinessPartnerService partners, string? erp = "STANDALONE")` e `TaxTestServices.InactivePurchaseApply(UnitOfWork db)`.

- [ ] **Step 1: Fábricas de teste**

Em `TaxTestServices.cs`:

```csharp
    public static PurchaseInvoicesTaxApplyService PurchaseApply(
        UnitOfWork db, IBusinessPartnerService partners, string? erp = "STANDALONE") =>
        new(db, Gate(db, erp), new UsageService(db, NullLogger<UsageService>.Instance), partners,
            new IbsCbsRatesService(db));

    /// <summary>Regra sempre inativa (modo SAPB1) — para os testes antigos da entrada, que não exercitam a tributação.</summary>
    public static PurchaseInvoicesTaxApplyService InactivePurchaseApply(UnitOfWork db) =>
        PurchaseApply(db, new FakeBusinessPartnerService(), "SAPB1");
```

Nos testes existentes de `PurchaseInvoices/` que constroem `PurchaseInvoicesCreateService`, `UpdateService`, `ItemsCreateService`, `ItemsUpdateService`, acrescente o último argumento `TaxTestServices.InactivePurchaseApply(db)` (é mecânico; nenhuma asserção muda).

- [ ] **Step 2: Testes que falham**

`SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesTaxApplyTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Tributos da entrada própria e da devolução de compra pela natureza (spec §6.2).</summary>
public class PurchaseInvoicesTaxApplyTests
{
    private const string Supplier = "F-SP";
    private const string ValidKey = "35261011222333000181550010000004561123456780";

    private sealed record Seed(UnitOfWork Db, int PurchaseUsage, int ReturnUsage, FakeBusinessPartnerService Partners);

    private static async Task<Seed> SeedAsync(string supplierState = "SP", bool issuesNfe = true)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "CEAGUI", ShortName = "CEAGUI", TaxId = "12345678000195", StateCode = "SP",
            TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe,
        });
        db.Context.Items.Add(new Item { ItemCode = "TRIGO", ItemName = "TRIGO EM GRAOS", Ncm = "10019900", GoodsOrigin = 0 });
        var returnUsage = new Usage
        {
            Name = "DEVOLUCAO DE COMPRA", Direction = UsageDirection.Outgoing, CfopOutgoingInState = "5202",
            CfopOutgoingOutState = "6202", IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m,
            IcmsInStateBenefitCode = "SP053521", IcmsOutStateCst = "00", PisCst = "49", CofinsCst = "49",
        };
        db.Context.Usages.Add(returnUsage);
        await db.SaveChangesAsync();
        var purchaseUsage = new Usage
        {
            Name = "COMPRA DE MERCADORIA", Direction = UsageDirection.Incoming, CfopIncomingInState = "1102",
            CfopIncomingOutState = "2102", IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m,
            IcmsInStateBenefitCode = "SP053521", IcmsOutStateCst = "00", PisCst = "74", CofinsCst = "74",
            ReturnUsageCode = returnUsage.Code,
        };
        db.Context.Usages.Add(purchaseUsage);
        await db.SaveChangesAsync();

        var partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [Supplier] = "PRODUTOR TESTE" },
            states: new Dictionary<string, string> { [Supplier] = supplierState },
            paymentConditions: new Dictionary<string, int> { [Supplier] = 7 });

        return new Seed(db, purchaseUsage.Code, returnUsage.Code, partners);
    }

    private static PurchaseInvoice OwnEntry(int? usageCode, string? itemCode = "TRIGO")
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = Supplier, IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Normal, IssueDate = new DateTime(2026, 10, 5),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = itemCode, UnitOfMeasureCode = "KG", Quantity = 1000m, UnitPrice = 1.5m,
            UsageCode = usageCode,
        });
        return invoice;
    }

    private static PurchaseInvoicesCreateService Create(Seed seed, string erp = "STANDALONE") =>
        new(seed.Db, seed.Partners, new FakeItemService(names: new Dictionary<string, string> { ["TRIGO"] = "TRIGO EM GRAOS" }),
            TaxTestServices.PurchaseApply(seed.Db, seed.Partners, erp));

    [Fact]
    public async Task Own_entry_is_calculated_with_the_incoming_usage()
    {
        var seed = await SeedAsync();
        var invoice = OwnEntry(seed.PurchaseUsage);

        await Create(seed).ExecuteAsync(invoice, "tester");

        var line = await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal("1102", line.Cfop);
        Assert.Equal("10019900", line.Ncm);
        Assert.Equal("51", line.CstIcms);
        Assert.Equal(1500m, line.IcmsBase);
        Assert.Equal(270m, line.IcmsOperationValue);
        Assert.Equal(0m, line.IcmsValue);
        Assert.Equal("SP053521", line.IcmsBenefitCode);
        Assert.Equal("74", line.CstPis);
        Assert.Equal("COMPRA DE MERCADORIA", line.UsageName);
    }

    [Fact]
    public async Task Own_entry_from_another_state_uses_the_out_of_state_cfop()
    {
        var seed = await SeedAsync(supplierState: "PR");

        await Create(seed).ExecuteAsync(OwnEntry(seed.PurchaseUsage), "tester");

        var line = await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal("2102", line.Cfop);
        Assert.Equal(12m, line.IcmsRate); // PR → SP
    }

    [Fact]
    public async Task Own_entry_takes_the_supplier_payment_condition()
    {
        var seed = await SeedAsync();
        var invoice = OwnEntry(seed.PurchaseUsage);

        await Create(seed).ExecuteAsync(invoice, "tester");

        Assert.Equal(7, invoice.PaymentConditionCode);
    }

    [Fact]
    public async Task Own_entry_line_without_usage_is_refused()
    {
        var seed = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(seed).ExecuteAsync(OwnEntry(null), "tester"));

        Assert.Equal("O item TRIGO está sem natureza de operação.", e.Message);
    }

    [Fact]
    public async Task Own_entry_line_without_product_is_refused()
    {
        var seed = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Create(seed).ExecuteAsync(OwnEntry(seed.PurchaseUsage, itemCode: null), "tester"));

        Assert.Equal("Informe o produto do item 1.", e.Message);
    }

    [Fact]
    public async Task Outgoing_usage_is_refused_in_the_own_entry()
    {
        var seed = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(seed).ExecuteAsync(OwnEntry(seed.ReturnUsage), "tester"));

        Assert.Equal("A natureza de operação DEVOLUCAO DE COMPRA é de saída e não pode ser usada na entrada.", e.Message);
    }

    [Fact]
    public async Task Supplier_without_state_is_refused_at_save()
    {
        var seed = await SeedAsync();
        var partners = new FakeBusinessPartnerService(names: new Dictionary<string, string> { [Supplier] = "PRODUTOR TESTE" });
        var create = new PurchaseInvoicesCreateService(seed.Db, partners, new FakeItemService(),
            TaxTestServices.PurchaseApply(seed.Db, partners));

        var e = await Assert.ThrowsAsync<DefaultException>(() => create.ExecuteAsync(OwnEntry(seed.PurchaseUsage), "tester"));

        Assert.Equal($"Parceiro {Supplier} está sem UF no endereço de faturamento.", e.Message);
    }

    [Fact]
    public async Task Third_party_document_is_not_calculated()
    {
        var seed = await SeedAsync();
        var invoice = OwnEntry(null);
        invoice.IssuerType = DocumentIssuerType.ThirdParty;

        await Create(seed).ExecuteAsync(invoice, "tester");

        Assert.Null((await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync()).Cfop);
    }

    [Fact]
    public async Task Inactive_rule_keeps_the_own_entry_as_typed()
    {
        var seed = await SeedAsync(issuesNfe: false);

        await Create(seed).ExecuteAsync(OwnEntry(null), "tester");

        Assert.Null((await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync()).Cfop);
    }

    [Fact]
    public async Task Legacy_third_party_document_without_branch_still_saves()
    {
        // Review Focus 1: o BranchCode nunca era gravado na entrada; documento de terceiro não passa a exigi-lo.
        var seed = await SeedAsync();
        var invoice = OwnEntry(null);
        invoice.IssuerType = DocumentIssuerType.ThirdParty;
        invoice.BranchCode = null;
        await Create(seed, "SAPB1").ExecuteAsync(invoice, "tester");

        var update = new PurchaseInvoicesUpdateService(seed.Db, seed.Partners, new FakeItemService(),
            TaxTestServices.PurchaseApply(seed.Db, seed.Partners));
        var changed = await seed.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        changed.Comments = "conferido";

        await update.ExecuteAsync(changed.Key, changed, "tester");

        Assert.Equal("conferido", (await seed.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync()).Comments);
    }

    [Fact]
    public async Task Changing_the_supplier_recalculates_every_line()
    {
        var seed = await SeedAsync();
        await Create(seed).ExecuteAsync(OwnEntry(seed.PurchaseUsage), "tester");
        var partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [Supplier] = "PRODUTOR TESTE", ["F-PR"] = "PRODUTOR PR" },
            states: new Dictionary<string, string> { [Supplier] = "SP", ["F-PR"] = "PR" });
        var update = new PurchaseInvoicesUpdateService(seed.Db, partners, new FakeItemService(),
            TaxTestServices.PurchaseApply(seed.Db, partners));
        var changed = await seed.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        changed.CardCode = "F-PR";

        await update.ExecuteAsync(changed.Key, changed, "tester");

        Assert.Equal("2102", (await seed.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync()).Cfop);
    }

    [Fact]
    public async Task Referenced_key_with_a_wrong_check_digit_is_refused()
    {
        var seed = await SeedAsync();
        var invoice = OwnEntry(seed.PurchaseUsage);
        invoice.ReferencedAccessKey = ValidKey[..43] + "1";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(seed).ExecuteAsync(invoice, "tester"));

        Assert.Equal("A chave da NF-e referenciada é inválida.", e.Message);
    }

    [Fact]
    public async Task Referenced_key_is_kept_with_digits_only()
    {
        var seed = await SeedAsync();
        var invoice = OwnEntry(seed.PurchaseUsage);
        invoice.ReferencedAccessKey = " " + ValidKey + " ";

        await Create(seed).ExecuteAsync(invoice, "tester");

        Assert.Equal(ValidKey, invoice.ReferencedAccessKey);
    }

    [Fact]
    public async Task Manual_own_return_is_refused_in_a_branch_that_issues_nfe()
    {
        var seed = await SeedAsync();
        var invoice = OwnEntry(seed.ReturnUsage);
        invoice.InvoiceType = PurchaseInvoiceType.Return;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(seed).ExecuteAsync(invoice, "tester"));

        Assert.Equal(
            "Na filial que emite NF-e pelo Siagro, a devolução de compra é feita pelo botão Devolver, no detalhe do documento de entrada.",
            e.Message);
    }

    [Fact]
    public async Task Purchase_return_is_calculated_with_the_outgoing_usage_and_conferred()
    {
        var seed = await SeedAsync();
        var origin = OwnEntry(seed.PurchaseUsage);
        await Create(seed).ExecuteAsync(origin, "tester");
        var originItem = origin.Items.Single();

        var returned = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = Supplier, IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Return, IssueDate = new DateTime(2026, 10, 6),
            PurchaseInvoiceOriginKey = origin.Key,
        };
        returned.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 400m, UnitPrice = 1.5m,
            UsageCode = seed.ReturnUsage, PurchaseInvoiceItemOriginKey = originItem.Key,
        });

        await Create(seed).ExecuteAsync(returned, "tester", nfeReturn: true);

        var line = returned.Items.Single();
        Assert.Equal("5202", line.Cfop);
        Assert.Equal("51", line.CstIcms);
        Assert.Equal("49", line.CstPis);
        Assert.True(returned.IsNfeReturn);
        Assert.Null(returned.PaymentConditionCode);
    }

    [Fact]
    public async Task Purchase_return_that_does_not_reproduce_the_purchase_is_refused()
    {
        var seed = await SeedAsync();
        var origin = OwnEntry(seed.PurchaseUsage);
        await Create(seed).ExecuteAsync(origin, "tester");
        var usage = await seed.Db.Context.Usages.SingleAsync(u => u.Code == seed.ReturnUsage);
        usage.IcmsInStateBenefitCode = "SP070010";
        await seed.Db.SaveChangesAsync();

        var returned = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = Supplier, IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Return, IssueDate = new DateTime(2026, 10, 6), PurchaseInvoiceOriginKey = origin.Key,
        };
        returned.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 400m, UnitPrice = 1.5m,
            UsageCode = seed.ReturnUsage, PurchaseInvoiceItemOriginKey = origin.Items.Single().Key,
        });

        var e = await Assert.ThrowsAsync<DefaultException>(() => Create(seed).ExecuteAsync(returned, "tester", nfeReturn: true));

        Assert.Equal(
            "Item TRIGO: a natureza de devolução DEVOLUCAO DE COMPRA não reproduz a tributação da compra — cBenef: compra SP053521, devolução SP070010.",
            e.Message);
    }
}
```

(Confira o nome das propriedades de `Branch`/`Usage`/`Item` usadas no seed contra as entidades; ajuste o teste ao nome real se algum diferir. O `FakeItemService()` sem nomes devolve a descrição já vinda na linha.)

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesTaxApplyTests"` — Expected: falha de compilação.

- [ ] **Step 3: `PurchaseInvoiceAccessKey`**

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// NF-e referenciada pela entrada própria (spec D7): normalmente a nota do produtor, que vai no
/// <c>ide/NFref/refNFe</c>. Conferida aqui (44 dígitos e dígito verificador mod 11) porque a SEFAZ só a
/// recusaria depois de o número ter sido consumido.
/// </summary>
public static class PurchaseInvoiceAccessKey
{
    public static string? Normalize(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    public static void EnsureValidReference(PurchaseInvoice invoice)
    {
        // Só a entrada própria Normal referencia nota; nos outros casos o campo não tem uso.
        if (invoice.IssuerType != DocumentIssuerType.Own || invoice.InvoiceType != PurchaseInvoiceType.Normal)
        {
            invoice.ReferencedAccessKey = null;
            return;
        }

        invoice.ReferencedAccessKey = Normalize(invoice.ReferencedAccessKey);

        if (invoice.ReferencedAccessKey is null)
            return;

        if (!IsValid(invoice.ReferencedAccessKey) || invoice.ReferencedAccessKey == invoice.ChaveNFe)
            throw new DefaultException("A chave da NF-e referenciada é inválida.");
    }

    private static bool IsValid(string key)
    {
        if (key.Length != 44)
            return false;

        var sum = 0;
        var weight = 2;
        for (var i = 42; i >= 0; i--)
        {
            sum += (key[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var digit = 11 - (sum % 11);
        return (digit >= 10 ? 0 : digit) == key[43] - '0';
    }
}
```

- [ ] **Step 4: `PurchaseInvoicesTaxApplyService`**

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Tributos da linha do Documento de Entrada pela natureza (spec §6.2). Só age com a regra ativa, documento
/// Pendente e EMISSÃO PRÓPRIA — a entrada Normal (natureza de Entrada, CFOP 1xxx/2xxx) e a devolução de compra
/// criada pelo Devolver (natureza de Saída, CFOP 5xxx/6xxx, conferida contra a linha comprada). Documento de
/// terceiro e a devolução do cliente ficam exatamente como chegaram.
/// </summary>
public class PurchaseInvoicesTaxApplyService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    IUsage usageService,
    IBusinessPartnerService businessPartnerService,
    IbsCbsRatesService ibsCbsRatesService)
{
    private readonly TaxLineCalculator _lines = new(db, ibsCbsRatesService);

    public Task<bool> IsBranchActiveAsync(string? branchCode) => gate.IsActiveAsync(branchCode);

    public async Task<bool> IsActiveForAsync(PurchaseInvoice invoice) =>
        invoice.IssuerType == DocumentIssuerType.Own
        && (invoice.InvoiceType == PurchaseInvoiceType.Normal || IsOwnNfeReturn(invoice))
        && await gate.IsActiveAsync(invoice.BranchCode);

    /// <summary>Devolução de compra criada pelo "Devolver": sai com NF-e própria de saída.</summary>
    public static bool IsOwnNfeReturn(PurchaseInvoice invoice) =>
        invoice.InvoiceType == PurchaseInvoiceType.Return && invoice.IsNfeReturn;

    public async Task ApplyAsync(PurchaseInvoice invoice, IEnumerable<PurchaseInvoiceItem> items)
    {
        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            return;

        if (!await IsActiveForAsync(invoice))
            return;

        var lines = items.ToList();
        var ownReturn = IsOwnNfeReturn(invoice);
        var branch = await _lines.LoadBranchAsync(invoice.BranchCode);
        var supplierState = await TaxLineCalculator.LoadPartnerStateAsync(businessPartnerService, invoice.CardCode);
        var inState = string.Equals(branch.StateCode, supplierState, StringComparison.OrdinalIgnoreCase);

        // Devolução: IBS/CBS pelas alíquotas da DATA DA ENTRADA — ela anula o tributo daquela operação.
        var rateDate = ownReturn
            ? await OriginDateAsync(invoice)
            : DateOnly.FromDateTime(invoice.IssueDate ?? DateTime.Today);
        var origins = ownReturn ? await LoadOriginItemsAsync(lines) : new Dictionary<Guid, PurchaseInvoiceItem>();
        var usages = new Dictionary<int, UsageModel>();
        var number = 0;

        foreach (var item in lines)
        {
            number++;

            if (string.IsNullOrWhiteSpace(item.ItemCode))
                throw new DefaultException($"Informe o produto do item {number}.");

            var usage = await ResolveUsageAsync(item, usages, ownReturn);

            // A mercadoria vem do FORNECEDOR para a filial; a devolução repete a operação da compra, para
            // reproduzir a alíquota creditada (12% de BA→SP, e não 7% de SP→BA).
            var line = await _lines.CalculateAsync(new TaxLineRequest(
                usage, item.ItemCode, item.Total, inState, supplierState, branch.StateCode!,
                branch.TaxRegime!.Value, rateDate, IncomingCfop: !ownReturn));

            TaxLineCalculator.Apply(item, usage, line);

            if (ownReturn)
                NfeReturnConference.Ensure(item, OriginOf(item, origins), usage.Name, "compra");
        }
    }

    private async Task<UsageModel> ResolveUsageAsync(
        PurchaseInvoiceItem item, Dictionary<int, UsageModel> cache, bool ownReturn)
    {
        if (item.UsageCode is not { } code)
            throw new DefaultException(ownReturn
                ? $"O item {item.ItemCode} da devolução está sem natureza de devolução."
                : $"O item {item.ItemCode} está sem natureza de operação.");

        if (!cache.TryGetValue(code, out var usage))
        {
            usage = await usageService.GetByIdAsync(code)
                    ?? throw new DefaultException("Natureza de operação não encontrada.");
            cache[code] = usage;
        }

        if (!ownReturn && usage.Direction != UsageDirection.Incoming)
            throw new DefaultException(
                $"A natureza de operação {usage.Name} é de saída e não pode ser usada na entrada.");

        if (ownReturn && usage.Direction != UsageDirection.Outgoing)
            throw new DefaultException(
                $"A natureza de operação {usage.Name} é de entrada e não pode ser usada na devolução de compra.");

        if (usage.Inactive)
            throw new DefaultException($"Natureza de operação {usage.Name} está inativa.");

        return usage;
    }

    private async Task<DateOnly> OriginDateAsync(PurchaseInvoice invoice)
    {
        var date = await db.Context.PurchaseInvoices.AsNoTracking()
            .Where(i => i.Key == invoice.PurchaseInvoiceOriginKey)
            .Select(i => i.IssueDate)
            .FirstOrDefaultAsync();

        return DateOnly.FromDateTime(date ?? throw new DefaultException("A devolução está sem a entrada de origem."));
    }

    private async Task<Dictionary<Guid, PurchaseInvoiceItem>> LoadOriginItemsAsync(IEnumerable<PurchaseInvoiceItem> lines)
    {
        var keys = lines.Where(l => l.PurchaseInvoiceItemOriginKey != null)
            .Select(l => l.PurchaseInvoiceItemOriginKey!.Value).Distinct().ToList();

        return await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Where(i => i.Key != null && keys.Contains(i.Key.Value))
            .ToDictionaryAsync(i => i.Key!.Value);
    }

    private static PurchaseInvoiceItem OriginOf(PurchaseInvoiceItem item, Dictionary<Guid, PurchaseInvoiceItem> origins) =>
        item.PurchaseInvoiceItemOriginKey is { } key && origins.TryGetValue(key, out var origin)
            ? origin
            : throw new DefaultException($"O item {item.ItemCode} da devolução não aponta um item da entrada.");
}
```

(Se `IUsage.GetByIdAsync` tiver outro nome, use o mesmo método que o `SalesInvoicesTaxApplyService` usa.)

- [ ] **Step 5: Integração nos serviços**

`PurchaseInvoicesCreateService` — construtor ganha `PurchaseInvoicesTaxApplyService taxApply`; assinatura `ExecuteAsync(PurchaseInvoice invoice, string userName, bool nfeReturn = false)`; ordem do corpo:

```csharp
        if (invoice.Items.Count == 0)
            throw new DefaultException("Informe ao menos um item no documento de entrada.");

        // Só o "Devolver" (PurchaseInvoicesNfeReturnCreateService) marca a devolução de compra; o corpo da
        // API nunca — senão um POST tiraria a nota da confirmação pela emissão.
        invoice.IsNfeReturn = nfeReturn && invoice.InvoiceType == PurchaseInvoiceType.Return;

        if (invoice.IssuerType == DocumentIssuerType.Own && invoice.InvoiceType == PurchaseInvoiceType.Return &&
            !invoice.IsNfeReturn && await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            throw new DefaultException(
                "Na filial que emite NF-e pelo Siagro, a devolução de compra é feita pelo botão Devolver, no detalhe do documento de entrada.");

        PurchaseInvoiceAccessKey.EnsureValidReference(invoice);
        await EnsureChaveNFeIsFreeAsync(invoice.ChaveNFe, invoice.Key);

        invoice.CreatedAt = DateTime.Now;
        invoice.CreatedBy = userName;
        invoice.InvoiceStatus = InvoiceStatus.Pending;

        var partner = await businessPartnerService.GetByIdAsync(invoice.CardCode);

        if (string.IsNullOrWhiteSpace(invoice.CardName))
            invoice.CardName = partner?.CardName;

        // Condição padrão do fornecedor (NF-e STANDALONE), como no documento de saída. A devolução de
        // compra não tem pagamento (tPag 90).
        if (!invoice.IsNfeReturn)
            invoice.PaymentConditionCode ??= partner?.PaymentConditionCode;

        foreach (var item in invoice.Items) { /* laço atual: ResolveItemNameAsync + EnsureContractIsCompatibleAsync */ }

        // Tributos pela natureza, ANTES de gravar: as guardas recusam com mensagem de negócio.
        await taxApply.ApplyAsync(invoice, invoice.Items);

        await db.Context.PurchaseInvoices.AddAsync(invoice);
        await db.SaveChangesAsync();
```

(mantenha os comentários longos que já existem sobre `CardName`, adaptados ao `partner` carregado uma vez.)

`PurchaseInvoicesUpdateService` — construtor ganha `taxApply`; depois de `existing.FreightTerms = entity.FreightTerms;` acrescente:

```csharp
        existing.BranchCode = entity.BranchCode;
        existing.PaymentConditionCode = entity.PaymentConditionCode;
        existing.ReferencedAccessKey = entity.ReferencedAccessKey;
```

e, depois de `existing.UpdatedBy = userName;`, antes do `SyncItemsAsync`: `PurchaseInvoiceAccessKey.EnsureValidReference(existing);`. Depois do `await SyncItemsAsync(existing, entity);`:

```csharp
        // O PATCH do cabeçalho traz todas as linhas (o controller carrega com Include e aplica o Delta),
        // então toda alteração passa por aqui: recalcula o documento inteiro — data, fornecedor e filial
        // mudam o CFOP e a alíquota de todas as linhas. As linhas incluídas no SyncItems já estão na
        // coleção pelo fixup do EF.
        await taxApply.ApplyAsync(existing, existing.Items);
```

`PurchaseInvoicesItemsCreateService` — construtor ganha `taxApply`; antes do `AddAsync`:

```csharp
        var invoice = await db.Context.PurchaseInvoices.FirstAsync(x => x.Key == item.PurchaseInvoiceKey);
        await taxApply.ApplyAsync(invoice, [item]);
```

(reaproveite este `invoice` no lugar da consulta só do `CardCode` que já existe para o contrato.)

`PurchaseInvoicesItemsUpdateService` — construtor ganha `taxApply`; depois das atribuições, antes do `SaveChangesAsync`:

```csharp
        var invoice = await db.Context.PurchaseInvoices.FirstAsync(x => x.Key == existing.PurchaseInvoiceKey);
        await taxApply.ApplyAsync(invoice, [existing]);
```

DI: `services.AddScoped<PurchaseInvoicesTaxApplyService>();` ao lado de `SalesInvoicesTaxApplyService`.

- [ ] **Step 6: Ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoice"` — Expected: PASS (novos e antigos).

- [ ] **Step 7: Commit (backend)**

Mensagem: `feat(invoice): entrada própria calcula os tributos pela natureza de entrada` (corpo: CFOP 1xxx/2xxx, UFs fornecedor → filial, condição padrão do fornecedor, NF-e referenciada, devolução manual recusada, devolução de compra calculada e conferida).

---

### Task 6: Travas e guardas da NF-e no Documento de Entrada

Spec: §7, §9.3 (travas da devolução de compra, exceto o saldo — Task 9).

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/NfeLockRules.cs`
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceNfeLock.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs` (passa a usar `NfeLockRules`, mensagens idênticas)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs`, `UpdateService.cs`, `ItemsCreateService.cs`, `ItemsUpdateService.cs`, `ItemsDeleteService.cs`, `DeleteService.cs`, `CancelService.cs`, `ConfirmService.cs`
- Modify (fábricas de teste): `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoicesLifecycleTests.cs` (`new PurchaseInvoicesConfirmService(db, TaxTestServices.Gate(db, "SAPB1"))`)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceNfeLockTests.cs` (novo)

**Interfaces:**
- Produces:
  - `NfeLockRules.ProcessingMessage`, `NfeLockRules.AuthorizedMessage`, `NfeLockRules.AnyChanged(EntityEntry, IEnumerable<string>)`, `NfeLockRules.Restore(EntityEntry, IEnumerable<string>)`.
  - `PurchaseInvoiceNfeLock.ResetIssuanceFields(PurchaseInvoice)`, `RestoreIssuanceFields(EntityEntry<PurchaseInvoice>)`, `EnsureHeaderEditable(EntityEntry<PurchaseInvoice>)`, `RestoreReturnHeader(EntityEntry<PurchaseInvoice>)`, `EnsureItemEditable(NfeStatus, EntityEntry<PurchaseInvoiceItem>)`, `RestoreReturnLine(EntityEntry<PurchaseInvoiceItem>)`, `EnsureLinesChangeable(NfeStatus)`, `EnsureLineCanBeAdded(PurchaseInvoice)`, `EnsureDeletable(PurchaseInvoice)`, `EnsureCancellable(PurchaseInvoice)`.
  - `PurchaseInvoicesConfirmService(IUnitOfWork db, TaxCalculationGate gate)`.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceNfeLockTests.cs` — monte os documentos direto no banco (InMemory) e chame os serviços:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Travas da NF-e no Documento de Entrada (spec §7) — todas pelo NfeStatus, que fica None na Yokotobi e na MH Agro.</summary>
public class PurchaseInvoiceNfeLockTests
{
    private static async Task<(UnitOfWork Db, PurchaseInvoice Invoice)> SeedAsync(
        NfeStatus nfeStatus, InvoiceStatus status = InvoiceStatus.Pending, bool nfeReturn = false, bool issuesNfe = true)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "CEAGUI", ShortName = "CEAGUI", TaxId = "12345678000195", StateCode = "SP",
            TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe,
        });
        // A devolução de compra aponta uma entrada própria autorizada (o saldo da edição é conferido contra ela).
        PurchaseInvoice? origin = null;
        if (nfeReturn)
        {
            origin = new PurchaseInvoice
            {
                Key = Guid.NewGuid(), BranchCode = "01", CardCode = "F-SP", IssuerType = DocumentIssuerType.Own,
                InvoiceType = PurchaseInvoiceType.Normal, InvoiceStatus = InvoiceStatus.Confirmed, NfeStatus = NfeStatus.Authorized,
                ChaveNFe = "35261012345678000195550010000001231481516234", TaxDocumentNumber = "000000001", TaxDocumentSeries = "9",
            };
            origin.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1000m, UnitPrice = 1.5m, NfeItemNumber = 1,
            });
            db.Context.PurchaseInvoices.Add(origin);
        }

        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "F-SP", IssuerType = DocumentIssuerType.Own,
            InvoiceType = nfeReturn ? PurchaseInvoiceType.Return : PurchaseInvoiceType.Normal, IsNfeReturn = nfeReturn,
            InvoiceStatus = status, NfeStatus = nfeStatus, IssueDate = new DateTime(2026, 10, 5), NetWeight = 1000m,
            GrossWeight = 1000m, NfeRandomCode = nfeStatus == NfeStatus.None ? null : "12345678",
            TaxDocumentNumber = nfeStatus == NfeStatus.None ? null : "000000010", TaxDocumentSeries = "9",
            PurchaseInvoiceOriginKey = origin?.Key,
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1000m, UnitPrice = 1.5m,
            PurchaseInvoiceItemOriginKey = origin?.Items.Single().Key,
        });
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return (db, invoice);
    }

    private static PurchaseInvoicesUpdateService Update(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(), new FakeItemService(), TaxTestServices.InactivePurchaseApply(db));

    private static async Task<PurchaseInvoice> LoadAsync(UnitOfWork db, Guid key) =>
        await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == key);

    [Fact]
    public async Task Create_never_starts_emitted()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new PurchaseInvoice { CardCode = "F-SP", NfeStatus = NfeStatus.Authorized, NfeProtocol = "1", IsNfeReturn = true, InvoiceType = PurchaseInvoiceType.Return };
        invoice.AddItem(new PurchaseInvoiceItem { ItemCode = "TRIGO", Quantity = 1m, UnitPrice = 1m });

        await new PurchaseInvoicesCreateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
            TaxTestServices.InactivePurchaseApply(db)).ExecuteAsync(invoice, "tester");

        Assert.Equal(NfeStatus.None, invoice.NfeStatus);
        Assert.Null(invoice.NfeProtocol);
        Assert.False(invoice.IsNfeReturn);
    }

    [Fact]
    public async Task Processing_document_cannot_be_edited()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Processing);
        var changed = await LoadAsync(db, invoice.Key);
        changed.Comments = "x";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(db).ExecuteAsync(invoice.Key, changed, "tester"));

        Assert.Equal("A NF-e deste documento está em processamento na SEFAZ: aguarde e use Consultar situação.", e.Message);
    }

    [Fact]
    public async Task Authorized_document_keeps_the_fiscal_header()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        var changed = await LoadAsync(db, invoice.Key);
        changed.NetWeight = 999m;

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(db).ExecuteAsync(invoice.Key, changed, "tester"));

        Assert.Equal("A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.", e.Message);
    }

    [Fact]
    public async Task Api_never_writes_the_issuance_fields_nor_the_number_after_reservation()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Rejected);
        var changed = await LoadAsync(db, invoice.Key);
        changed.NfeStatus = NfeStatus.Authorized;
        changed.TaxDocumentNumber = "000000999";
        changed.IsNfeReturn = true;
        changed.Comments = "ok";

        await Update(db).ExecuteAsync(invoice.Key, changed, "tester");

        var saved = await LoadAsync(db, invoice.Key);
        Assert.Equal(NfeStatus.Rejected, saved.NfeStatus);
        Assert.Equal("000000010", saved.TaxDocumentNumber);
        Assert.False(saved.IsNfeReturn);
        Assert.Equal("ok", saved.Comments);
    }

    [Fact]
    public async Task Line_cannot_be_added_while_authorized()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(new PurchaseInvoiceItem { PurchaseInvoiceKey = invoice.Key, ItemCode = "TRIGO", Quantity = 1m }, "tester"));

        Assert.Equal("A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.", e.Message);
    }

    [Fact]
    public async Task Purchase_return_does_not_take_new_lines()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None, nfeReturn: true);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(new PurchaseInvoiceItem { PurchaseInvoiceKey = invoice.Key, ItemCode = "TRIGO", Quantity = 1m }, "tester"));

        Assert.Equal("A devolução de compra só tem os itens que vieram da entrada: não é possível incluir item.", e.Message);
    }

    [Fact]
    public async Task Purchase_return_line_only_changes_the_quantity()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None, nfeReturn: true);
        var line = invoice.Items.Single();
        var changed = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == line.Key);
        changed.Quantity = 400m;
        changed.UnitPrice = 9m;
        changed.ItemCode = "MILHO";

        await new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
            .ExecuteAsync(line.Key!.Value, changed, "tester");

        var saved = await db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == line.Key);
        Assert.Equal(400m, saved.Quantity);
        Assert.Equal(1.5m, saved.UnitPrice);
        Assert.Equal("TRIGO", saved.ItemCode);
    }

    [Fact]
    public async Task Purchase_return_header_keeps_supplier_branch_and_type()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None, nfeReturn: true);
        var changed = await LoadAsync(db, invoice.Key);
        changed.CardCode = "OUTRO";
        changed.BranchCode = "02";
        changed.InvoiceType = PurchaseInvoiceType.Normal;
        changed.Comments = "motivo revisto";

        await Update(db).ExecuteAsync(invoice.Key, changed, "tester");

        var saved = await LoadAsync(db, invoice.Key);
        Assert.Equal("F-SP", saved.CardCode);
        Assert.Equal("01", saved.BranchCode);
        Assert.Equal(PurchaseInvoiceType.Return, saved.InvoiceType);
        Assert.Equal("motivo revisto", saved.Comments);
    }

    [Theory]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Authorized)]
    [InlineData(NfeStatus.Denied)]
    public async Task Emitted_document_cannot_be_deleted(NfeStatus status)
    {
        var (db, invoice) = await SeedAsync(status);

        var e = await Assert.ThrowsAsync<DefaultException>(() => new PurchaseInvoicesDeleteService(db).ExecuteAsync(invoice.Key));

        Assert.Equal("Documento com NF-e em processamento, autorizada ou denegada não pode ser excluído.", e.Message);
    }

    [Fact]
    public async Task Authorized_document_cannot_be_cancelled_here()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);

        var e = await Assert.ThrowsAsync<DefaultException>(() => new PurchaseInvoicesCancelService(db).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal(
            "A NF-e deste documento está autorizada: o cancelamento precisa ser feito na SEFAZ, recurso da próxima etapa.",
            e.Message);
    }

    [Fact]
    public async Task Own_document_in_a_branch_that_issues_nfe_confirms_only_by_the_emission()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesConfirmService(db, TaxTestServices.Gate(db, "STANDALONE")).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.", e.Message);
    }

    [Fact]
    public async Task Authorized_own_document_confirms()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);

        await new PurchaseInvoicesConfirmService(db, TaxTestServices.Gate(db, "STANDALONE")).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, (await LoadAsync(db, invoice.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Third_party_document_confirms_as_before()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        var entity = await db.Context.PurchaseInvoices.SingleAsync(i => i.Key == invoice.Key);
        entity.IssuerType = DocumentIssuerType.ThirdParty;
        await db.SaveChangesAsync();

        await new PurchaseInvoicesConfirmService(db, TaxTestServices.Gate(db, "STANDALONE")).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, (await LoadAsync(db, invoice.Key)).InvoiceStatus);
    }
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceNfeLockTests"` — Expected: falha de compilação/asserções.

- [ ] **Step 2: `NfeLockRules` e o documento de saída usando-o**

```csharp
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Mecânica e mensagens comuns às travas da NF-e dos dois documentos (saída e entrada).</summary>
public static class NfeLockRules
{
    public const string ProcessingMessage =
        "A NF-e deste documento está em processamento na SEFAZ: aguarde e use Consultar situação.";

    public const string AuthorizedMessage =
        "A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.";

    public static bool AnyChanged(EntityEntry entry, IEnumerable<string> properties) =>
        properties.Any(p => !Equals(entry.OriginalValues[p], entry.CurrentValues[p]));

    /// <summary>Volta as propriedades ao valor gravado — o "sobrescreve, não recusa".</summary>
    public static void Restore(EntityEntry entry, IEnumerable<string> properties)
    {
        foreach (var property in properties)
            entry.Property(property).CurrentValue = entry.OriginalValues[property];
    }
}
```

Em `SalesInvoiceNfeLock`: troque as duas constantes privadas pelas de `NfeLockRules`, o `AnyChanged` privado por `NfeLockRules.AnyChanged` e o laço de restauração por `NfeLockRules.Restore`. Nenhuma mensagem muda.

- [ ] **Step 3: `PurchaseInvoiceNfeLock`**

```csharp
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Travas da NF-e no Documento de Entrada (spec §7), espelho de <c>SalesInvoiceNfeLock</c>. Todas agem pelo
/// <see cref="PurchaseInvoice.NfeStatus"/>, que fica <c>None</c> para sempre no documento de terceiro, na
/// Yokotobi (SAPB1) e na MH Agro (chave desligada).
/// </summary>
public static class PurchaseInvoiceNfeLock
{
    private static readonly string[] HeaderFiscalFields =
    [
        nameof(PurchaseInvoice.BranchCode), nameof(PurchaseInvoice.CardCode), nameof(PurchaseInvoice.IssueDate),
        nameof(PurchaseInvoice.InvoiceType), nameof(PurchaseInvoice.IssuerType), nameof(PurchaseInvoice.TruckingCompanyCode),
        nameof(PurchaseInvoice.TruckCode), nameof(PurchaseInvoice.FreightTerms), nameof(PurchaseInvoice.PaymentConditionCode),
        nameof(PurchaseInvoice.GrossWeight), nameof(PurchaseInvoice.NetWeight), nameof(PurchaseInvoice.TaxPayerComments),
        nameof(PurchaseInvoice.ReferencedAccessKey),
    ];

    private static readonly string[] ItemFiscalFields =
    [
        nameof(PurchaseInvoiceItem.ItemCode), nameof(PurchaseInvoiceItem.UnitOfMeasureCode), nameof(PurchaseInvoiceItem.Quantity),
        nameof(PurchaseInvoiceItem.UnitPrice), nameof(PurchaseInvoiceItem.UsageCode),
    ];

    private static readonly string[] IssuanceFields =
    [
        nameof(PurchaseInvoice.NfeStatus), nameof(PurchaseInvoice.NfeEnvironment), nameof(PurchaseInvoice.NfeRandomCode),
        nameof(PurchaseInvoice.NfeProtocol), nameof(PurchaseInvoice.NfeAuthorizedAt), nameof(PurchaseInvoice.NfeStatusCode),
        nameof(PurchaseInvoice.NfeStatusReason), nameof(PurchaseInvoice.NfeConfirmationError),
    ];

    /// <summary>Número, série, chave e o vNF: depois da primeira emissão, só a emissão escreve.</summary>
    private static readonly string[] TaxDocumentFields =
    [
        nameof(PurchaseInvoice.TaxDocumentNumber), nameof(PurchaseInvoice.TaxDocumentSeries), nameof(PurchaseInvoice.ChaveNFe),
        nameof(PurchaseInvoice.TotalDocumentValue),
    ];

    /// <summary>Na devolução de compra o cabeçalho que identifica a operação não muda (spec §9.3).</summary>
    private static readonly string[] ReturnHeaderFields =
    [
        nameof(PurchaseInvoice.CardCode), nameof(PurchaseInvoice.BranchCode), nameof(PurchaseInvoice.InvoiceType),
        nameof(PurchaseInvoice.IssuerType), nameof(PurchaseInvoice.PurchaseInvoiceOriginKey),
    ];

    /// <summary>Na linha da devolução de compra só a quantidade é editável.</summary>
    private static readonly string[] ReturnLineFields =
    [
        nameof(PurchaseInvoiceItem.ItemCode), nameof(PurchaseInvoiceItem.ItemName), nameof(PurchaseInvoiceItem.UnitOfMeasureCode),
        nameof(PurchaseInvoiceItem.UnitPrice), nameof(PurchaseInvoiceItem.UsageCode),
        nameof(PurchaseInvoiceItem.PurchaseInvoiceItemOriginKey), nameof(PurchaseInvoiceItem.PurchaseContractKey),
        nameof(PurchaseInvoiceItem.SalesInvoiceItemKey),
    ];

    public static void ResetIssuanceFields(PurchaseInvoice invoice)
    {
        invoice.NfeStatus = NfeStatus.None;
        invoice.NfeEnvironment = null;
        invoice.NfeRandomCode = null;
        invoice.NfeProtocol = null;
        invoice.NfeAuthorizedAt = null;
        invoice.NfeStatusCode = null;
        invoice.NfeStatusReason = null;
        invoice.NfeConfirmationError = null;
    }

    /// <summary>O PATCH/PUT não escreve situação, protocolo, retorno — nem número/série/chave depois de emitir.</summary>
    public static void RestoreIssuanceFields(EntityEntry<PurchaseInvoice> entry)
    {
        var emitted = (NfeStatus)entry.OriginalValues[nameof(PurchaseInvoice.NfeStatus)]! != NfeStatus.None
                      || entry.OriginalValues[nameof(PurchaseInvoice.NfeRandomCode)] is not null;

        NfeLockRules.Restore(entry, emitted ? IssuanceFields.Concat(TaxDocumentFields) : IssuanceFields);
        NfeLockRules.Restore(entry, [nameof(PurchaseInvoice.IsNfeReturn)]);
    }

    /// <summary>Chamado DEPOIS das atribuições do Update: compara o gravado com o que chegou.</summary>
    public static void EnsureHeaderEditable(EntityEntry<PurchaseInvoice> entry)
    {
        var status = (NfeStatus)entry.OriginalValues[nameof(PurchaseInvoice.NfeStatus)]!;

        if (status == NfeStatus.Processing)
            throw new DefaultException(NfeLockRules.ProcessingMessage);

        if (status == NfeStatus.Authorized && NfeLockRules.AnyChanged(entry, HeaderFiscalFields))
            throw new DefaultException(NfeLockRules.AuthorizedMessage);
    }

    public static void RestoreReturnHeader(EntityEntry<PurchaseInvoice> entry)
    {
        if ((bool)entry.OriginalValues[nameof(PurchaseInvoice.IsNfeReturn)]!)
            NfeLockRules.Restore(entry, ReturnHeaderFields);
    }

    public static void EnsureItemEditable(NfeStatus invoiceStatus, EntityEntry<PurchaseInvoiceItem> entry)
    {
        if (invoiceStatus == NfeStatus.Processing)
            throw new DefaultException(NfeLockRules.ProcessingMessage);

        if (invoiceStatus == NfeStatus.Authorized && NfeLockRules.AnyChanged(entry, ItemFiscalFields))
            throw new DefaultException(NfeLockRules.AuthorizedMessage);
    }

    public static void RestoreReturnLine(EntityEntry<PurchaseInvoiceItem> entry) =>
        NfeLockRules.Restore(entry, ReturnLineFields);

    public static void EnsureLinesChangeable(NfeStatus invoiceStatus)
    {
        if (invoiceStatus == NfeStatus.Processing)
            throw new DefaultException(NfeLockRules.ProcessingMessage);

        if (invoiceStatus == NfeStatus.Authorized)
            throw new DefaultException(NfeLockRules.AuthorizedMessage);
    }

    public static void EnsureLineCanBeAdded(PurchaseInvoice invoice)
    {
        EnsureLinesChangeable(invoice.NfeStatus);

        if (invoice.IsNfeReturn)
            throw new DefaultException(
                "A devolução de compra só tem os itens que vieram da entrada: não é possível incluir item.");
    }

    public static void EnsureDeletable(PurchaseInvoice invoice)
    {
        if (invoice.NfeStatus is NfeStatus.Processing or NfeStatus.Authorized or NfeStatus.Denied)
            throw new DefaultException(
                "Documento com NF-e em processamento, autorizada ou denegada não pode ser excluído.");
    }

    public static void EnsureCancellable(PurchaseInvoice invoice)
    {
        if (invoice.NfeStatus == NfeStatus.Processing)
            throw new DefaultException(NfeLockRules.ProcessingMessage);

        if (invoice.NfeStatus == NfeStatus.Authorized)
            throw new DefaultException(
                "A NF-e deste documento está autorizada: o cancelamento precisa ser feito na SEFAZ, recurso da próxima etapa.");
    }
}
```

- [ ] **Step 4: Ligar as travas nos serviços**

- `PurchaseInvoicesCreateService.ExecuteAsync`: primeira linha depois da checagem de itens: `PurchaseInvoiceNfeLock.ResetIssuanceFields(invoice);`.
- `PurchaseInvoicesUpdateService.ExecuteAsync`: depois de todas as atribuições do cabeçalho (antes de `EnsureValidReference` e do `SyncItemsAsync`):

```csharp
        var entry = db.Context.Entry(existing);
        PurchaseInvoiceNfeLock.RestoreIssuanceFields(entry);
        PurchaseInvoiceNfeLock.RestoreReturnHeader(entry);
        PurchaseInvoiceNfeLock.EnsureHeaderEditable(entry);
```

  No `SyncItemsAsync`: antes de remover linhas e antes de incluir linha nova, `PurchaseInvoiceNfeLock.EnsureLinesChangeable(existing.NfeStatus)` (só quando houver linha removida/incluída); para incluir, `PurchaseInvoiceNfeLock.EnsureLineCanBeAdded(existing)`; para linha existente, depois das atribuições: `var lineEntry = db.Context.Entry(current); if (existing.IsNfeReturn) PurchaseInvoiceNfeLock.RestoreReturnLine(lineEntry); PurchaseInvoiceNfeLock.EnsureItemEditable(existing.NfeStatus, lineEntry);` (a `ItemName` reconsultada também é restaurada na devolução).
- `PurchaseInvoicesItemsCreateService`: depois de carregar o `invoice` (Task 5): `PurchaseInvoiceNfeLock.EnsureLineCanBeAdded(invoice);` antes do cálculo.
- `PurchaseInvoicesItemsUpdateService`: depois das atribuições e de carregar o `invoice`: `var entry = db.Context.Entry(existing); if (invoice.IsNfeReturn) PurchaseInvoiceNfeLock.RestoreReturnLine(entry); PurchaseInvoiceNfeLock.EnsureItemEditable(invoice.NfeStatus, entry);` — antes do cálculo.
- `PurchaseInvoicesItemsDeleteService`: carregue o status da NF-e do pai e chame `PurchaseInvoiceNfeLock.EnsureLinesChangeable(status)` antes de remover.
- `PurchaseInvoicesDeleteService`: depois da checagem de Pendente, `PurchaseInvoiceNfeLock.EnsureDeletable(invoice);`.
- `PurchaseInvoicesCancelService`: antes de cancelar, `PurchaseInvoiceNfeLock.EnsureCancellable(invoice);`.
- `PurchaseInvoicesConfirmService(IUnitOfWork db, TaxCalculationGate gate)`: depois da checagem de Pendente:

```csharp
        // Emitir é o que confirma (spec D3): na filial com a regra ativa, o documento de emissão própria só
        // confirma com a NF-e autorizada — é o que a emissão chama. Documento de terceiro confirma como hoje.
        if (invoice.IssuerType == DocumentIssuerType.Own && invoice.NfeStatus != NfeStatus.Authorized &&
            await gate.IsActiveAsync(invoice.BranchCode))
            throw new DefaultException("Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.");
```

Atualize as construções de `PurchaseInvoicesConfirmService` nos testes antigos (`PurchaseInvoicesLifecycleTests`) para `new PurchaseInvoicesConfirmService(db, TaxTestServices.Gate(db, "SAPB1"))`.

- [ ] **Step 5: Ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoice|FullyQualifiedName~SalesInvoiceNfeLock|FullyQualifiedName~SalesInvoices"` — Expected: PASS.

- [ ] **Step 6: Commit (backend)**

Mensagem: `feat(invoice): travas da NF-e no documento de entrada e confirmação pela emissão`.

---

### Task 7: Pipeline da NF-e genérico (sem mudar o documento de saída)

Spec: D13, §10. **Toda a suíte do documento de saída passa sem mudar asserção** — é o critério de aceite desta tarefa.

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/NfeStatusText.cs`, `INfeDocumentStore.cs`, `SalesInvoiceNfeStore.cs`, `NfeResultHandlerBase.cs`, `NfeIssueServiceBase.cs`, `NfeConsultServiceBase.cs`, `NfeCompleteConfirmationServiceBase.cs`, `NfeXmlDownloadServiceBase.cs`
- Modify: `SiagroB1.Application/Services/Nfe/SalesInvoiceNfeResultHandler.cs`, `SalesInvoicesNfeIssueService.cs`, `SalesInvoicesNfeConsultService.cs`, `SalesInvoicesNfeCompleteConfirmationService.cs`, `SalesInvoicesNfeXmlDownloadService.cs`
- Modify: `SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs`, `NfeIssueInputAssembler.cs`, `NfeIssueContext.cs` (só documentação de `Customer` = destinatário)
- Modify: usos de `SalesInvoiceNfeResultHandler.Truncate` → `NfeStatusText.Truncate`
- Test: nenhum teste novo obrigatório; a rede é `SiagroB1.Application.Tests` inteira. Acrescente só `SiagroB1.Application.Tests/Nfe/NfeStatusTextTests.cs` (1 teste do corte em 500).

**Interfaces:**
- Consumes: `INfeDocument`, `INfeTaxedLine`, `NfeXmlKind` (Task 2); `NfeItemNumbering` (Task 3).
- Produces (usadas pelas Tasks 8 e 9):

```csharp
public static class NfeStatusText { public static string Truncate(string value); }

public interface INfeDocumentStore<TDocument> where TDocument : class, INfeDocument
{
    string NotFoundMessage { get; }
    Task<TDocument?> FindWithItemsAsync(Guid key);   // rastreado, com itens (emissão)
    Task<TDocument?> FindAsync(Guid key);            // rastreado, só cabeçalho (retorno, consulta)
    Task<TDocument?> FindReadOnlyAsync(Guid key);    // AsNoTracking (conclusão, download)
    void AddXml(TDocument document, NfeXmlKind kind, string xml);
    Task<IReadOnlyList<string>> SignedXmlsAsync(Guid key);   // o mais novo primeiro
    Task<string> LatestAuthorizedXmlAsync(Guid key);         // NotFoundException sem autorizado
}

public abstract class NfeResultHandlerBase<TDocument>(IUnitOfWork db, INfeDocumentStore<TDocument> store, ILogger logger)
    where TDocument : class, INfeDocument
{
    public Task<NfeIssueOutcomeDto> ApplyAuthorizationAsync(TDocument document, string signedXml, NfeSefazResult result, string userName);
    public Task<NfeIssueOutcomeDto> ApplyConsultAsync(TDocument document, IReadOnlyList<string> signedXmls, NfeSefazResult result, string userName);
    public Task<NfeIssueOutcomeDto> ConfirmAsync(Guid key, string userName);
    protected abstract Task ConfirmDocumentAsync(Guid key, string userName);
}

public abstract class NfeIssueServiceBase<TDocument>(
    IUnitOfWork db, TaxCalculationGate gate, INfeDocumentStore<TDocument> store, BranchNfeSettingsService settingsService,
    NfeNumberReservationService reservation, INfeSefazClient sefaz, NfeResultHandlerBase<TDocument> resultHandler,
    NfeOptions options, ILogger logger, Func<DateTimeOffset>? clock, TimeZoneInfo? storageZone)
    where TDocument : class, INfeDocument
{
    public Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName);
    protected abstract void EnsureIssuableType(TDocument document);
    protected abstract DateTime? DocumentDate(TDocument document);
    protected abstract IReadOnlyList<INfeTaxedLine> Lines(TDocument document);
    protected virtual Task EnsureBeforeReservationAsync(TDocument document) => Task.CompletedTask;
    protected abstract Task<NfeIssueContext> ValidateReadinessAsync(TDocument document);
    protected abstract NfeIssueInput BuildInput(TDocument document, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible);
    protected virtual void BeforeSigning(TDocument document) { }
}

public abstract class NfeConsultServiceBase<TDocument>(
    IUnitOfWork db, INfeDocumentStore<TDocument> store, BranchNfeSettingsService settingsService, INfeSefazClient sefaz,
    NfeResultHandlerBase<TDocument> resultHandler, NfeNumberReservationService reservation, ILogger logger)
    where TDocument : class, INfeDocument
{ public Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName); }

public abstract class NfeCompleteConfirmationServiceBase<TDocument>(INfeDocumentStore<TDocument> store, NfeResultHandlerBase<TDocument> resultHandler)
    where TDocument : class, INfeDocument
{ public Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName); }

public abstract class NfeXmlDownloadServiceBase<TDocument>(INfeDocumentStore<TDocument> store)
    where TDocument : class, INfeDocument
{ public Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid invoiceKey); }

public sealed record NfeReadinessRequest(
    string? BranchCode, string PartnerCode, string PartnerLabel, string? DeliveryCardCode, string? TruckingCompanyCode,
    string? TruckCode, decimal GrossWeight, decimal NetWeight, int? PaymentConditionCode, bool RequiresPayment,
    NfeDirection Direction, IReadOnlyList<INfeTaxedLine> Lines, bool RequiresProductAndUnit);
```

  e, no `NfeReadinessValidator`, `protected`/`private Task<NfeIssueContext> ValidateCoreAsync(NfeReadinessRequest request, Func<List<string>, Task<NfeReturnOrigin?>>? loadReturnOrigin)` usado pelas sobrecargas públicas.

- [ ] **Step 1: `NfeStatusText` (TDD curto)**

`SiagroB1.Application.Tests/Nfe/NfeStatusTextTests.cs`:

```csharp
using SiagroB1.Application.Services.Nfe;

namespace SiagroB1.Application.Tests.Nfe;

public class NfeStatusTextTests
{
    [Fact]
    public void Reason_is_cut_at_500_characters()
    {
        Assert.Equal(500, NfeStatusText.Truncate(new string('x', 600)).Length);
        Assert.Equal("curto", NfeStatusText.Truncate("curto"));
    }
}
```

Run (falha de compilação), crie:

```csharp
namespace SiagroB1.Application.Services.Nfe;

/// <summary>Texto de situação/retorno da NF-e gravado no documento (colunas de 500).</summary>
public static class NfeStatusText
{
    public static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
```

e troque todos os `SalesInvoiceNfeResultHandler.Truncate(` por `NfeStatusText.Truncate(` (remova o método antigo). Run → PASS.

- [ ] **Step 2: Store, retorno e subclasse da saída**

`INfeDocumentStore.cs` com a interface acima. `SalesInvoiceNfeStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Nfe;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Documento de saída e seus XMLs para o pipeline da NF-e.</summary>
public sealed class SalesInvoiceNfeStore(IUnitOfWork db) : INfeDocumentStore<SalesInvoice>
{
    public string NotFoundMessage => "Documento de saída não encontrado.";

    public Task<SalesInvoice?> FindWithItemsAsync(Guid key) =>
        db.Context.SalesInvoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Key == key);

    public Task<SalesInvoice?> FindAsync(Guid key) =>
        db.Context.SalesInvoices.FirstOrDefaultAsync(i => i.Key == key);

    public Task<SalesInvoice?> FindReadOnlyAsync(Guid key) =>
        db.Context.SalesInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Key == key);

    public void AddXml(SalesInvoice document, NfeXmlKind kind, string xml) =>
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = document.Key, Kind = kind, Xml = xml, CreatedAt = DateTime.Now,
        });

    public async Task<IReadOnlyList<string>> SignedXmlsAsync(Guid key) =>
        await db.Context.SalesInvoiceNfeXmls.AsNoTracking()
            .Where(x => x.SalesInvoiceKey == key && x.Kind == NfeXmlKind.Signed)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Xml)
            .ToListAsync();

    public Task<string> LatestAuthorizedXmlAsync(Guid key) => db.Context.SalesInvoiceNfeXmls.LatestAuthorizedXmlAsync(key);
}
```

`NfeResultHandlerBase<TDocument>`: mova o corpo inteiro de `SalesInvoiceNfeResultHandler` (summary incluído, adaptado para "o documento"), trocando:
- `db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml { … Kind = SalesInvoiceNfeXmlKind.Authorized, Xml = X … })` → `store.AddXml(invoice, NfeXmlKind.Authorized, X)` (idem `Denied`);
- `await confirm.ExecuteAsync(key, userName)` → `await ConfirmDocumentAsync(key, userName)`;
- `await db.Context.SalesInvoices.FirstAsync(i => i.Key == key)` → `await store.FindAsync(key) ?? throw new NotFoundException(store.NotFoundMessage)`;
- `logger.LogError(e, "Falha ao confirmar o documento {InvoiceKey} depois da NF-e autorizada.", key)` mantido.

`SalesInvoiceNfeResultHandler` fica:

```csharp
/// <summary>Retorno da SEFAZ no documento de saída; confirma pelo <see cref="SalesInvoicesConfirmService"/>.</summary>
public class SalesInvoiceNfeResultHandler(
    IUnitOfWork db, SalesInvoicesConfirmService confirm, ILogger<SalesInvoiceNfeResultHandler> logger)
    : NfeResultHandlerBase<SalesInvoice>(db, new SalesInvoiceNfeStore(db), logger)
{
    protected override Task ConfirmDocumentAsync(Guid key, string userName) => confirm.ExecuteAsync(key, userName);
}
```

- [ ] **Step 3: Emissão, consulta, conclusão e download genéricos**

`NfeIssueServiceBase<TDocument>`: mova o corpo de `SalesInvoicesNfeIssueService` (constantes, relógio, fuso, `ExecuteAsync`, `ReserveNumberAsync`, `NewRandomCode`) com estas trocas:
- carregar: `var invoice = await store.FindWithItemsAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);`
- `EnsurePreconditionsAsync(invoice)` passa a ser privado na base e fica:

```csharp
    private async Task EnsurePreconditionsAsync(TDocument invoice)
    {
        if (!await gate.IsActiveAsync(invoice.BranchCode))
            throw new DefaultException($"A filial {invoice.BranchCode} não emite NF-e pelo Siagro.");

        EnsureIssuableType(invoice);

        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException("Só documento Pendente pode ser emitido.");

        // (switch de NfeStatus — sem mudança)
        // (conferência do dia em Brasília com DocumentDate(invoice) no lugar de invoice.InvoiceDate — mesma mensagem)

        var lines = Lines(invoice);
        if (lines.Count == 0)
            throw new DefaultException("O documento não tem itens.");

        // (conferência de linha sem cálculo sobre `lines` — mesma mensagem)

        await EnsureBeforeReservationAsync(invoice);
    }
```

- a conversão do `ReturnOrigin.IssuedOn` para Brasília continua na base, igual;
- `NfeItemNumbering.Renumber(Lines(invoice));` e logo depois `BeforeSigning(invoice);` (no lugar do `Renumber` de hoje);
- `NfeIssueInputAssembler.Build(...)` → `BuildInput(invoice, context, _now(), options.TechnicalResponsible)`;
- XML assinado: `store.AddXml(invoice, NfeXmlKind.Signed, signed.Xml);`;
- XMLs da duplicidade: `var signedXmls = await store.SignedXmlsAsync(invoice.Key);`;
- `SalesInvoiceNfeResultHandler.Truncate` → `NfeStatusText.Truncate`; `logger` é `ILogger` (mensagem "Falha inesperada ao transmitir a NF-e do documento {InvoiceKey}." mantida).

`SalesInvoicesNfeIssueService` fica com **o mesmo construtor de hoje**:

```csharp
public class SalesInvoicesNfeIssueService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    NfeReadinessValidator readiness,
    BranchNfeSettingsService settingsService,
    NfeNumberReservationService reservation,
    INfeSefazClient sefaz,
    SalesInvoiceNfeResultHandler resultHandler,
    NfeOptions options,
    ILogger<SalesInvoicesNfeIssueService> logger,
    Func<DateTimeOffset>? clock = null,
    TimeZoneInfo? storageZone = null)
    : NfeIssueServiceBase<SalesInvoice>(
        db, gate, new SalesInvoiceNfeStore(db), settingsService, reservation, sefaz, resultHandler, options, logger, clock, storageZone)
{
    private readonly IUnitOfWork _db = db;

    protected override void EnsureIssuableType(SalesInvoice invoice)
    {
        if (invoice.InvoiceType != SalesInvoiceType.Normal && !SalesInvoicesTaxApplyService.IsOwnNfeReturn(invoice))
            throw new DefaultException(
                "Só o documento Normal e a devolução criada pelo Devolver são emitidos como NF-e por aqui.");
    }

    protected override DateTime? DocumentDate(SalesInvoice invoice) => invoice.InvoiceDate;

    protected override IReadOnlyList<INfeTaxedLine> Lines(SalesInvoice invoice) => invoice.Items.Cast<INfeTaxedLine>().ToList();

    // Devolução: peso e saldo da venda ANTES de reservar o número (a confirmação roda depois da autorização).
    protected override async Task EnsureBeforeReservationAsync(SalesInvoice invoice)
    {
        if (!invoice.IsNfeReturn)
            return;

        SalesInvoicesReturnWeightService.EnsureHeaderWeightMatchesItems(invoice);
        await SalesInvoiceNfeReturnBalance.EnsureWithinAsync(_db.Context, invoice, invoice.Items);
    }

    protected override Task<NfeIssueContext> ValidateReadinessAsync(SalesInvoice invoice) => readiness.ValidateAsync(invoice);

    protected override NfeIssueInput BuildInput(
        SalesInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible) =>
        NfeIssueInputAssembler.Build(invoice, context, issuedAt, technicalResponsible);
}
```

(mantenha o `<summary>` da classe; os comentários do fluxo vão para a base.)

`NfeConsultServiceBase<TDocument>`: corpo de `SalesInvoicesNfeConsultService` com `store.FindAsync`/`store.NotFoundMessage`/`store.SignedXmlsAsync` e `NfeStatusText.Truncate`. `SalesInvoicesNfeConsultService(IUnitOfWork db, BranchNfeSettingsService settingsService, INfeSefazClient sefaz, SalesInvoiceNfeResultHandler resultHandler, NfeNumberReservationService reservation, ILogger<SalesInvoicesNfeConsultService> logger) : NfeConsultServiceBase<SalesInvoice>(db, new SalesInvoiceNfeStore(db), settingsService, sefaz, resultHandler, reservation, logger);` — corpo vazio.

`NfeCompleteConfirmationServiceBase<TDocument>`:

```csharp
public abstract class NfeCompleteConfirmationServiceBase<TDocument>(
    INfeDocumentStore<TDocument> store, NfeResultHandlerBase<TDocument> resultHandler)
    where TDocument : class, INfeDocument
{
    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName)
    {
        var invoice = await store.FindReadOnlyAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);

        if (invoice.NfeStatus != NfeStatus.Authorized || invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException("Só documento com NF-e autorizada e ainda Pendente tem confirmação a concluir.");

        return await resultHandler.ConfirmAsync(key, userName);
    }
}
```

`SalesInvoicesNfeCompleteConfirmationService(IUnitOfWork db, SalesInvoiceNfeResultHandler resultHandler) : NfeCompleteConfirmationServiceBase<SalesInvoice>(new SalesInvoiceNfeStore(db), resultHandler);`

`NfeXmlDownloadServiceBase<TDocument>`:

```csharp
public abstract class NfeXmlDownloadServiceBase<TDocument>(INfeDocumentStore<TDocument> store)
    where TDocument : class, INfeDocument
{
    public async Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid invoiceKey)
    {
        var xml = await store.LatestAuthorizedXmlAsync(invoiceKey);
        var invoice = await store.FindReadOnlyAsync(invoiceKey);

        return (Encoding.UTF8.GetBytes(xml), $"{invoice?.ChaveNFe}-procNFe.xml");
    }
}
```

`SalesInvoicesNfeXmlDownloadService(IUnitOfWork db) : NfeXmlDownloadServiceBase<SalesInvoice>(new SalesInvoiceNfeStore(db));`

- [ ] **Step 4: Prontidão e montador com núcleo neutro**

`NfeReadinessValidator`: extraia o corpo de `ValidateAsync(SalesInvoice)` para `ValidateCoreAsync(NfeReadinessRequest request, Func<List<string>, Task<NfeReturnOrigin?>>? loadReturnOrigin)`, trocando:
- `invoice.BranchCode` → `request.BranchCode`; `invoice.CardCode` → `request.PartnerCode`;
- `"Cliente {invoice.CardCode} não encontrado"` → `$"{request.PartnerLabel} {request.PartnerCode} não encontrado"`; `$"Cliente {customer.CardCode}"` → `$"{request.PartnerLabel} {customer.CardCode}"`;
- prefixos do CFOP: `var inStatePrefix = request.Direction == NfeDirection.Incoming ? '1' : '5'; var outStatePrefix = request.Direction == NfeDirection.Incoming ? '2' : '6';`, laço sobre `request.Lines`;
- logo antes do laço do CFOP, quando `request.RequiresProductAndUnit`:

```csharp
        if (request.RequiresProductAndUnit)
        {
            var position = 0;
            foreach (var line in request.Lines)
            {
                position++;
                var gaps = new List<string>();
                if (string.IsNullOrWhiteSpace(line.ItemCode)) gaps.Add("produto");
                if (string.IsNullOrWhiteSpace(line.UnitOfMeasureCode)) gaps.Add("unidade de medida");
                Report(problems, $"Item {position}", gaps);
            }
        }
```

- entrega com `request.DeliveryCardCode`; transportadora com `request.TruckingCompanyCode`; pesos com `request.GrossWeight/NetWeight`; condição de pagamento só com `request.RequiresPayment` (usando `request.PaymentConditionCode`);
- origem: `var returnOrigin = loadReturnOrigin is null ? null : await loadReturnOrigin(problems);`;
- caminhão `request.TruckCode`; naturezas e CEST a partir de `request.Lines` (`ItemCode` não nulo: `.Where(c => c is not null).Select(c => c!)`).

`ValidateAsync(SalesInvoice invoice)` (assinatura pública inalterada) passa a ser:

```csharp
    public Task<NfeIssueContext> ValidateAsync(SalesInvoice invoice) =>
        ValidateCoreAsync(
            new NfeReadinessRequest(
                invoice.BranchCode, invoice.CardCode, "Cliente", invoice.DeliveryCardCode, invoice.TruckingCompanyCode,
                invoice.TruckCode, invoice.GrossWeight, invoice.NetWeight, invoice.PaymentConditionCode,
                RequiresPayment: !invoice.IsNfeReturn,
                Direction: invoice.IsNfeReturn ? NfeDirection.Incoming : NfeDirection.Outgoing,
                Lines: invoice.Items.Cast<INfeTaxedLine>().ToList(),
                RequiresProductAndUnit: false),
            invoice.IsNfeReturn ? problems => LoadReturnOriginAsync(invoice, problems) : null);
```

`NfeIssueInputAssembler`: extraia o corpo de `Build(SalesInvoice, …)` para um núcleo privado sobre uma visão neutra:

```csharp
    /// <summary>O que o montador precisa do documento, sem saber se é de saída ou de entrada.</summary>
    private sealed record NfeDocumentView(
        string TaxDocumentNumber, string TaxDocumentSeries, string RandomCode,
        NfeDirection Direction, NfeReturnOrigin? ReturnOrigin, IReadOnlyList<string> HeaderReferencedKeys,
        IReadOnlyList<INfeTaxedLine> Lines, Func<INfeTaxedLine, Guid?> OriginItemKey,
        FreightTerms FreightTerms, decimal NetWeight, decimal GrossWeight, NfeVolume? Volume,
        string? TaxPayerComments, string? TaxComments, string DefaultOperationNature);

    private static NfeIssueInput Build(NfeDocumentView view, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible)
```

No núcleo: `Direction = view.Direction`, `Purpose = view.ReturnOrigin is null ? NfePurpose.Normal : NfePurpose.Return`, `ReferencedKeys = view.HeaderReferencedKeys`, itens de `view.Lines` (já ordenadas), `Reference = view.ReturnOrigin is null ? null : new NfeItemReference(view.ReturnOrigin.AccessKey, view.ReturnOrigin.ItemNumbers[view.OriginItemKey(item)!.Value])`, `Payment` com `tPag 90` quando há `ReturnOrigin`, `OperationNature` com `view.DefaultOperationNature` no lugar de `"VENDA"`, `Volume = view.Volume`, `FiscoInfo = view.TaxComments`, `AdditionalInfo(view.Lines, …, view.TaxPayerComments, ReturnReference(view.ReturnOrigin))`. `ToItem` e `AdditionalInfo` passam a receber `INfeTaxedLine` (`ItemCode!`, `UnitOfMeasureCode!`; a prontidão já garantiu os dois).

`Build(SalesInvoice invoice, …)` (assinatura pública inalterada):

```csharp
    public static NfeIssueInput Build(
        SalesInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible)
    {
        var returnOrigin = invoice.IsNfeReturn
            ? context.ReturnOrigin ?? throw new DefaultException("A devolução está sem a venda de origem.")
            : null;

        return Build(new NfeDocumentView(
            invoice.TaxDocumentNumber!, invoice.TaxDocumentSeries!, invoice.NfeRandomCode!,
            // Venda: saída normal. Devolução de venda: ENTRADA com finalidade 4.
            returnOrigin is null ? NfeDirection.Outgoing : NfeDirection.Incoming,
            returnOrigin, returnOrigin is null ? [] : [returnOrigin.AccessKey],
            NfeItemNumbering.Ordered(invoice.Items).Cast<INfeTaxedLine>().ToList(),
            line => ((SalesInvoiceItem)line).SalesInvoiceItemOriginKey,
            invoice.FreightTerms, invoice.NetWeight, invoice.GrossWeight,
            new NfeVolume(invoice.VolumeQuantity, Trimmed(invoice.VolumeSpecies), Trimmed(invoice.VolumeBrand), Trimmed(invoice.VolumeNumbering)),
            invoice.TaxPayerComments, invoice.TaxComments, "VENDA"), context, issuedAt, technicalResponsible);
    }
```

- [ ] **Step 5: Rede verde**

Run: `dotnet build SiagroB1.sln` — Expected: sem erro.
Run: `dotnet test SiagroB1.Application.Tests` — Expected: PASS, sem nenhuma asserção alterada nos testes do documento de saída (confira com `git diff --stat SiagroB1.Application.Tests` — só identificadores renomeados/fábricas).
Run: `dotnet test SiagroB1.Fiscal.Tests` — Expected: PASS.

- [ ] **Step 6: Commit (backend)**

Mensagem: `refactor(invoice): pipeline da NF-e genérico sobre o documento, sem mudar a saída` (corpo: por que as duas cópias não podiam existir — 539, digest, "Em processamento" antes do envio).

---

### Task 8: Emissão da NF-e de entrada própria

Spec: §8.2, §8.3, D3, D8, D9.

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/PurchaseInvoiceNfeStore.cs`, `PurchaseInvoiceNfeResultHandler.cs`, `PurchaseInvoicesNfeIssueService.cs`, `PurchaseInvoicesNfeConsultService.cs`, `PurchaseInvoicesNfeCompleteConfirmationService.cs`, `PurchaseInvoicesNfeXmlDownloadService.cs`
- Modify: `SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs` (sobrecarga da entrada + origem da devolução de compra), `NfeIssueInputAssembler.cs` (sobrecarga da entrada, inclusive o ramo da devolução)
- Create: `SiagroB1.Web/Actions/Nfe/PurchaseInvoicesIssueNfeController.cs`, `PurchaseInvoicesConsultNfeController.cs`, `PurchaseInvoicesCompleteNfeConfirmationController.cs`; `SiagroB1.Web/Functions/Nfe/PurchaseInvoicesNfeXmlController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Create (teste): `SiagroB1.Application.Tests/Support/PurchaseNfeTestSeed.cs`
- Test: `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeIssueServiceTests.cs`, `SiagroB1.Application.Tests/Nfe/PurchaseInvoiceNfeEdmModelTests.cs`

**Interfaces:**
- Consumes: bases da Task 7; `PurchaseInvoicesConfirmService(IUnitOfWork, TaxCalculationGate)` (Task 6); `PurchaseInvoicesTaxApplyService.IsOwnNfeReturn` (Task 5).
- Produces:
  - `PurchaseInvoiceNfeStore(IUnitOfWork db) : INfeDocumentStore<PurchaseInvoice>`
  - `PurchaseInvoiceNfeResultHandler(IUnitOfWork db, PurchaseInvoicesConfirmService confirm, ILogger<PurchaseInvoiceNfeResultHandler> logger)`
  - `PurchaseInvoicesNfeIssueService(IUnitOfWork db, TaxCalculationGate gate, NfeReadinessValidator readiness, BranchNfeSettingsService settingsService, NfeNumberReservationService reservation, INfeSefazClient sefaz, PurchaseInvoiceNfeResultHandler resultHandler, NfeOptions options, ILogger<PurchaseInvoicesNfeIssueService> logger, Func<DateTimeOffset>? clock = null, TimeZoneInfo? storageZone = null)`
  - `PurchaseInvoicesNfeConsultService(IUnitOfWork db, BranchNfeSettingsService settingsService, INfeSefazClient sefaz, PurchaseInvoiceNfeResultHandler resultHandler, NfeNumberReservationService reservation, ILogger<PurchaseInvoicesNfeConsultService> logger)`
  - `PurchaseInvoicesNfeCompleteConfirmationService(IUnitOfWork db, PurchaseInvoiceNfeResultHandler resultHandler)`, `PurchaseInvoicesNfeXmlDownloadService(IUnitOfWork db)`
  - `NfeReadinessValidator.ValidateAsync(PurchaseInvoice invoice)`; `NfeIssueInputAssembler.Build(PurchaseInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible)`
  - OData: actions `PurchaseInvoicesIssueNfe(Key)`, `PurchaseInvoicesConsultNfe(Key)`, `PurchaseInvoicesCompleteNfeConfirmation(Key)` → `NfeIssueOutcomeDto`; função `PurchaseInvoicesNfeXml(Key=…)` → arquivo.
  - Teste: `PurchaseNfeTestSeed.SeedAsync(bool twoItems = false)` → `PurchaseNfeScenario(UnitOfWork Db, string DatabaseName, Guid InvoiceKey, Guid SaleKey, int ReturnUsage)`; `PurchaseNfeTestSeed.Supplier = "F-SP"`, `PurchaseNfeTestSeed.ProducerKey`.

- [ ] **Step 1: Cenário de teste**

`SiagroB1.Application.Tests/Support/PurchaseNfeTestSeed.cs` — reaproveita o cenário da venda (filial CEAGUI emitindo, configuração, certificado, municípios, transportadora, condição 30/60) e acrescenta o produtor, os produtos, as naturezas de compra/devolução de compra e uma entrada própria Pendente já calculada:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

public sealed record PurchaseNfeScenario(UnitOfWork Db, string DatabaseName, Guid InvoiceKey, Guid SaleKey, int ReturnUsage);

/// <summary>
/// Compra de trigo de produtor de Itaberá/SP (não contribuinte, CPF) com NF-e própria de entrada: CFOP 1102,
/// ICMS 51 diferido com cBenef, PIS/COFINS 74 — o formato das 23 compras reais da CEAGUI (spec §1).
/// </summary>
public static class PurchaseNfeTestSeed
{
    public const string Supplier = "F-SP";
    public const string ProducerKey = "35261011222333000181550010000004561123456780";

    public static async Task<PurchaseNfeScenario> SeedAsync(bool twoItems = false)
    {
        var sale = await NfeTestSeed.SeedAsync();
        var context = sale.Db.Context;
        var condition = await context.PaymentConditions.FirstAsync();

        context.Items.AddRange(
            new Item { ItemCode = "TRIGO", ItemName = "TRIGO EM GRAOS", Ncm = "10019900", GoodsOrigin = 0 },
            new Item { ItemCode = "MILHO", ItemName = "MILHO EM GRAOS", Ncm = "10059010", GoodsOrigin = 0 });

        var returnUsage = new Usage
        {
            Name = "DEVOLUCAO DE COMPRA", Direction = UsageDirection.Outgoing, InvoiceOperationText = "DEVOLUCAO DE COMPRA",
            CfopOutgoingInState = "5202", CfopOutgoingOutState = "6202", IcmsInStateCst = "51", IcmsInStateRate = 18m,
            IcmsInStateDeferral = 100m, IcmsInStateBenefitCode = "SP053521", IcmsOutStateCst = "00",
            PisCst = "49", CofinsCst = "49",
        };
        context.Usages.Add(returnUsage);
        await sale.Db.SaveChangesAsync();

        var purchaseUsage = new Usage
        {
            Name = "COMPRA DE MERCADORIA", Direction = UsageDirection.Incoming, InvoiceOperationText = "COMPRA DE MERCADORIA",
            DefaultAdditionalInfo = "Produtor optante pelo recolhimento pela folha de pagamento.",
            CfopIncomingInState = "1102", CfopIncomingOutState = "2102", IcmsInStateCst = "51", IcmsInStateRate = 18m,
            IcmsInStateDeferral = 100m, IcmsInStateBenefitCode = "SP053521", IcmsOutStateCst = "00",
            PisCst = "74", CofinsCst = "74", ReturnUsageCode = returnUsage.Code,
        };
        context.Usages.Add(purchaseUsage);

        context.BusinessPartners.Add(new BusinessPartner
        {
            CardCode = Supplier, CardName = "PRODUTOR RURAL TESTE", CardType = "S", TaxId = "52998224725",
            StateRegistrationIndicator = StateRegistrationIndicator.NonTaxpayer, PaymentConditionCode = condition.Code,
            Addresses =
            [
                new Address
                {
                    CardCode = Supplier, AddressName = "FATURAMENTO", AdresType = "B", Street = "ESTRADA MUNICIPAL",
                    StreetNumber = "S/N", Block = "ZONA RURAL", ZipCode = "18440000", City = "Itaberá", State = "SP",
                    Country = "BR", MunicipalityCode = "3521705",
                },
            ],
        });
        await sale.Db.SaveChangesAsync();

        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = Supplier, CardName = "PRODUTOR RURAL TESTE",
            IssuerType = DocumentIssuerType.Own, InvoiceType = PurchaseInvoiceType.Normal, InvoiceStatus = InvoiceStatus.Pending,
            IssueDate = new DateTime(2026, 10, 2), PostingDate = new DateTime(2026, 10, 2), GrossWeight = 1000m, NetWeight = 1000m,
            FreightTerms = FreightTerms.None, PaymentConditionCode = condition.Code, ReferencedAccessKey = ProducerKey,
            TaxPayerComments = "Contribuição Social denominada Funrural 1,5% = R$ 22,50",
        };
        invoice.AddItem(Line("TRIGO", "TRIGO EM GRAOS", "10019900", 1000m, purchaseUsage));
        if (twoItems)
            invoice.AddItem(Line("MILHO", "MILHO EM GRAOS", "10059010", 500m, purchaseUsage));
        if (twoItems)
        {
            invoice.GrossWeight = 1500m;
            invoice.NetWeight = 1500m;
        }

        context.PurchaseInvoices.Add(invoice);
        await sale.Db.SaveChangesAsync();

        return new PurchaseNfeScenario(sale.Db, sale.DatabaseName, invoice.Key, sale.InvoiceKey, returnUsage.Code);
    }

    /// <summary>Linha já calculada: base = total, ICMS 51 18% diferido 100% (vICMSOp = vICMSDif, vICMS 0).</summary>
    private static PurchaseInvoiceItem Line(string code, string name, string ncm, decimal quantity, Usage usage)
    {
        var total = decimal.Round(quantity * 1.5m, 2);
        var operation = decimal.Round(total * 0.18m, 2, MidpointRounding.AwayFromZero);

        return new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = code, ItemName = name, UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 1.5m,
            UsageCode = usage.Code, UsageName = usage.Name, Cfop = "1102", Ncm = ncm, GoodsOrigin = 0,
            CstIcms = "51", IcmsBase = total, IcmsRate = 18m, IcmsOperationValue = operation, IcmsDeferral = 100m,
            IcmsDeferredValue = operation, IcmsValue = 0m, IcmsBenefitCode = "SP053521", CstPis = "74", CstCofins = "74",
        };
    }
}
```

(Confira `Item`/`Usage`/`BusinessPartner`/`Address` contra as entidades; ajuste o seed ao nome real se divergir.)

- [ ] **Step 2: Testes que falham**

`SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeIssueServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Emitir NF-e" da entrada própria (spec §8.3), com a SEFAZ simulada e o XML assinado de verdade.</summary>
public class PurchaseInvoicesNfeIssueServiceTests
{
    internal static PurchaseInvoiceNfeResultHandler Handler(PurchaseNfeScenario scenario) =>
        new(scenario.Db, new PurchaseInvoicesConfirmService(scenario.Db, new TaxCalculationGate(scenario.Db, NfeTestSeed.Config())),
            NullLogger<PurchaseInvoiceNfeResultHandler>.Instance);

    internal static PurchaseInvoicesNfeIssueService Issue(
        PurchaseNfeScenario scenario, FakeNfeSefazClient sefaz, FakeNfeNumberReservationService? reservation = null)
    {
        var config = NfeTestSeed.Config();
        var options = new NfeOptions(config);

        return new PurchaseInvoicesNfeIssueService(
            scenario.Db, new TaxCalculationGate(scenario.Db, config), new NfeReadinessValidator(scenario.Db, options),
            new BranchNfeSettingsService(scenario.Db, options, sefaz), reservation ?? new FakeNfeNumberReservationService(),
            sefaz, Handler(scenario), options, NullLogger<PurchaseInvoicesNfeIssueService>.Instance,
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);
    }

    internal static Task<PurchaseInvoice> ReloadAsync(PurchaseNfeScenario scenario, Guid? key = null) =>
        TestDb.CreateUnitOfWork(scenario.DatabaseName).Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
            .SingleAsync(i => i.Key == (key ?? scenario.InvoiceKey));

    private static async Task ChangeAsync(PurchaseNfeScenario scenario, Action<PurchaseInvoice> change)
    {
        var context = TestDb.CreateUnitOfWork(scenario.DatabaseName).Context;
        var invoice = await context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == scenario.InvoiceKey);
        change(invoice);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Authorized_entry_is_saved_and_the_document_confirmed()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, invoice.InvoiceStatus);
        Assert.Equal("000000001", invoice.TaxDocumentNumber);
        Assert.Equal(44, invoice.ChaveNFe!.Length);
        Assert.Equal(1500m, invoice.TotalDocumentValue);
        Assert.Equal(1, invoice.Items.Single().NfeItemNumber);

        var signed = Assert.Single(sefaz.Sent).Xml;
        Assert.Contains("<tpNF>0</tpNF>", signed);
        Assert.Contains("<finNFe>1</finNFe>", signed);
        Assert.Contains($"<refNFe>{PurchaseNfeTestSeed.ProducerKey}</refNFe>", signed);
        Assert.Contains("<CFOP>1102</CFOP>", signed);
        Assert.Contains("<indFinal>0</indFinal>", signed);
        Assert.Contains("Funrural", signed);

        var xmls = await scenario.Db.Context.PurchaseInvoiceNfeXmls.AsNoTracking().ToListAsync();
        Assert.Contains(xmls, x => x.Kind == NfeXmlKind.Signed);
        Assert.Contains(xmls, x => x.Kind == NfeXmlKind.Authorized && x.Xml.Contains("<protNFe"));
    }

    [Fact]
    public async Task Entry_and_sale_share_the_branch_number_sequence()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var reservation = new FakeNfeNumberReservationService();
        var config = NfeTestSeed.Config();
        var options = new NfeOptions(config);
        var saleIssue = new SalesInvoicesNfeIssueService(
            scenario.Db, new TaxCalculationGate(scenario.Db, config), new NfeReadinessValidator(scenario.Db, options),
            new BranchNfeSettingsService(scenario.Db, options, sefaz), reservation, sefaz,
            new SalesInvoiceNfeResultHandler(scenario.Db, new RecordingConfirmService(scenario.Db), NullLogger<SalesInvoiceNfeResultHandler>.Instance),
            options, NullLogger<SalesInvoicesNfeIssueService>.Instance, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

        await saleIssue.ExecuteAsync(scenario.SaleKey, "tester");
        await Issue(scenario, sefaz, reservation).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal("000000002", (await ReloadAsync(scenario)).TaxDocumentNumber);
    }

    [Fact]
    public async Task Rejected_entry_stays_pending_with_the_reason()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => FakeNfeSefazClient.Rejected());

        var outcome = await Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Rejected, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);
        Assert.Equal("209", invoice.NfeStatusCode);
    }

    [Fact]
    public async Task Third_party_document_is_not_issued()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await ChangeAsync(scenario, i => i.IssuerType = DocumentIssuerType.ThirdParty);

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Issue(scenario, new FakeNfeSefazClient()).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("Só o documento de emissão própria é emitido como NF-e.", e.Message);
    }

    [Fact]
    public async Task Uncalculated_line_asks_to_save_again()
    {
        // Review Focus 2: entrada criada com a chave desligada e emitida depois de ligá-la.
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await ChangeAsync(scenario, i => i.Items.Single().Cfop = null);

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Issue(scenario, new FakeNfeSefazClient()).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("O item TRIGO está sem os tributos calculados. Salve o documento para recalcular antes de emitir.", e.Message);
    }

    [Fact]
    public async Task Readiness_lists_a_line_without_unit_and_a_missing_payment_condition()
    {
        // Review Focus 5: a unidade é anulável na entrada.
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await ChangeAsync(scenario, i =>
        {
            i.Items.Single().UnitOfMeasureCode = null;
            i.PaymentConditionCode = null;
        });
        var reservation = new FakeNfeNumberReservationService();

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Issue(scenario, new FakeNfeSefazClient(), reservation).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Contains("Item 1: unidade de medida", e.Message);
        Assert.Contains("Documento: condição de pagamento", e.Message);
        Assert.Equal(0, reservation.Calls);
    }

    [Fact]
    public async Task Readiness_names_the_supplier()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var context = TestDb.CreateUnitOfWork(scenario.DatabaseName).Context;
        var address = await context.Set<Address>().SingleAsync(a => a.CardCode == PurchaseNfeTestSeed.Supplier);
        address.StreetNumber = null;
        await context.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Issue(scenario, new FakeNfeSefazClient()).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Contains($"Fornecedor {PurchaseNfeTestSeed.Supplier}: número do endereço de faturamento", e.Message);
    }

    [Fact]
    public async Task Entry_from_another_day_is_refused()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await ChangeAsync(scenario, i => i.IssueDate = new DateTime(2026, 10, 1));

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Issue(scenario, new FakeNfeSefazClient()).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.StartsWith("A data do documento (01/10/2026) precisa ser a de hoje", e.Message);
    }

    [Fact]
    public async Task Processing_entry_is_consulted_and_confirmed()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(FakeNfeSefazClient.NoResponse);
        await Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await new PurchaseInvoicesNfeConsultService(
                scenario.Db, new BranchNfeSettingsService(scenario.Db, new NfeOptions(NfeTestSeed.Config()), sefaz), sefaz,
                Handler(scenario), new FakeNfeNumberReservationService(), NullLogger<PurchaseInvoicesNfeConsultService>.Instance)
            .ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(scenario)).InvoiceStatus);
    }

    [Fact]
    public async Task Authorized_entry_xml_is_downloaded()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");

        var (bytes, fileName) = await new PurchaseInvoicesNfeXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey);

        Assert.Equal($"{(await ReloadAsync(scenario)).ChaveNFe}-procNFe.xml", fileName);
        Assert.Contains("<nfeProc", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Confirmation_is_completed_for_an_authorized_pending_entry()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await ChangeAsync(scenario, i => i.NfeStatus = NfeStatus.Authorized);

        var outcome = await new PurchaseInvoicesNfeCompleteConfirmationService(scenario.Db, Handler(scenario))
            .ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, outcome.InvoiceStatus);
    }
}
```

(Se `context.Set<Address>()` não existir com esse tipo, use o `DbSet` de endereços que o `AppDbContext` expõe.)

`SiagroB1.Application.Tests/Nfe/PurchaseInvoiceNfeEdmModelTests.cs`:

```csharp
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Nfe;

public class PurchaseInvoiceNfeEdmModelTests
{
    private static IEdmModel Model()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("PurchaseInvoicesIssueNfe")]
    [InlineData("PurchaseInvoicesConsultNfe")]
    [InlineData("PurchaseInvoicesCompleteNfeConfirmation")]
    public void Nfe_actions_take_the_key_and_return_the_outcome(string name)
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == name);

        Assert.Equal("Edm.Guid", action.Parameters.Single(p => p.Name == "Key").Type.FullName());
        Assert.EndsWith("NfeIssueOutcomeDto", action.ReturnType.FullName());
    }

    [Fact]
    public void Xml_download_is_a_function_with_the_key()
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "PurchaseInvoicesNfeXml");

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
    }
}
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesNfeIssueServiceTests|FullyQualifiedName~PurchaseInvoiceNfeEdmModelTests"` — Expected: falha de compilação.

- [ ] **Step 3: Store, retorno e serviços da entrada**

`PurchaseInvoiceNfeStore` — espelho do `SalesInvoiceNfeStore` com `db.Context.PurchaseInvoices`, `db.Context.PurchaseInvoiceNfeXmls`, `PurchaseInvoiceKey`, `PurchaseInvoiceNfeXmlQueries.LatestAuthorizedXmlAsync` e `NotFoundMessage => "Documento de entrada não encontrado."`.

`PurchaseInvoiceNfeResultHandler`:

```csharp
/// <summary>Retorno da SEFAZ no documento de entrada; confirma pelo <see cref="PurchaseInvoicesConfirmService"/>.</summary>
public class PurchaseInvoiceNfeResultHandler(
    IUnitOfWork db, PurchaseInvoicesConfirmService confirm, ILogger<PurchaseInvoiceNfeResultHandler> logger)
    : NfeResultHandlerBase<PurchaseInvoice>(db, new PurchaseInvoiceNfeStore(db), logger)
{
    protected override Task ConfirmDocumentAsync(Guid key, string userName) => confirm.ExecuteAsync(key, userName);
}
```

`PurchaseInvoicesNfeIssueService`:

```csharp
/// <summary>
/// "Emitir NF-e" do Documento de Entrada de emissão própria (spec §8.3): a entrada Normal sai como NF-e de
/// ENTRADA (finalidade 1) e a devolução de compra como NF-e de SAÍDA (finalidade 4). Emitir é o que confirma.
/// </summary>
public class PurchaseInvoicesNfeIssueService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    NfeReadinessValidator readiness,
    BranchNfeSettingsService settingsService,
    NfeNumberReservationService reservation,
    INfeSefazClient sefaz,
    PurchaseInvoiceNfeResultHandler resultHandler,
    NfeOptions options,
    ILogger<PurchaseInvoicesNfeIssueService> logger,
    Func<DateTimeOffset>? clock = null,
    TimeZoneInfo? storageZone = null)
    : NfeIssueServiceBase<PurchaseInvoice>(
        db, gate, new PurchaseInvoiceNfeStore(db), settingsService, reservation, sefaz, resultHandler, options, logger, clock, storageZone)
{
    protected override void EnsureIssuableType(PurchaseInvoice invoice)
    {
        if (invoice.IssuerType != DocumentIssuerType.Own)
            throw new DefaultException("Só o documento de emissão própria é emitido como NF-e.");

        if (invoice.InvoiceType != PurchaseInvoiceType.Normal && !PurchaseInvoicesTaxApplyService.IsOwnNfeReturn(invoice))
            throw new DefaultException(
                "Só a entrada Normal e a devolução criada pelo Devolver são emitidas como NF-e por aqui.");
    }

    protected override DateTime? DocumentDate(PurchaseInvoice invoice) => invoice.IssueDate;

    protected override IReadOnlyList<INfeTaxedLine> Lines(PurchaseInvoice invoice) => invoice.Items.Cast<INfeTaxedLine>().ToList();

    protected override Task<NfeIssueContext> ValidateReadinessAsync(PurchaseInvoice invoice) => readiness.ValidateAsync(invoice);

    protected override NfeIssueInput BuildInput(
        PurchaseInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible) =>
        NfeIssueInputAssembler.Build(invoice, context, issuedAt, technicalResponsible);

    /// <summary>O total da nota própria é o vNF (soma dos itens): o "valor declarado" passa a ser o emitido.</summary>
    protected override void BeforeSigning(PurchaseInvoice invoice) =>
        invoice.TotalDocumentValue = invoice.Items.Sum(i => i.Total);
}
```

`PurchaseInvoicesNfeConsultService`, `PurchaseInvoicesNfeCompleteConfirmationService`, `PurchaseInvoicesNfeXmlDownloadService`: subclasses de corpo vazio das bases da Task 7 com `new PurchaseInvoiceNfeStore(db)` (mesma forma das da saída).

- [ ] **Step 4: Prontidão e montador da entrada**

`NfeReadinessValidator`:

```csharp
    /// <summary>
    /// Entrada própria (destinatário = fornecedor, CFOP 1xxx/2xxx, condição de pagamento) ou devolução de compra
    /// (CFOP 5xxx/6xxx, sem pagamento, a entrada de origem conferida). Produto e unidade em toda linha: na
    /// entrada os dois são anuláveis (nota de terceiro pode trazer código fora do cadastro).
    /// </summary>
    public Task<NfeIssueContext> ValidateAsync(PurchaseInvoice invoice) =>
        ValidateCoreAsync(
            new NfeReadinessRequest(
                invoice.BranchCode, invoice.CardCode, "Fornecedor", DeliveryCardCode: null, invoice.TruckingCompanyCode,
                invoice.TruckCode, invoice.GrossWeight, invoice.NetWeight, invoice.PaymentConditionCode,
                RequiresPayment: !invoice.IsNfeReturn,
                Direction: invoice.IsNfeReturn ? NfeDirection.Outgoing : NfeDirection.Incoming,
                Lines: invoice.Items.Cast<INfeTaxedLine>().ToList(),
                RequiresProductAndUnit: true),
            invoice.IsNfeReturn ? problems => LoadPurchaseReturnOriginAsync(invoice, problems) : null);

    /// <summary>A entrada da devolução de compra: própria, confirmada, com NF-e autorizada e o nItem de cada item devolvido.</summary>
    private async Task<NfeReturnOrigin?> LoadPurchaseReturnOriginAsync(PurchaseInvoice invoice, List<string> problems)
    {
        var origin = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Key == invoice.PurchaseInvoiceOriginKey);

        if (origin is null)
        {
            problems.Add("Documento: entrada de origem não encontrada");
            return null;
        }

        if (origin.NfeStatus != NfeStatus.Authorized || origin.ChaveNFe is not { Length: 44 })
        {
            problems.Add($"Entrada de origem {origin.TaxDocumentNumber}: NF-e não autorizada");
            return null;
        }

        if (origin.InvoiceStatus != InvoiceStatus.Confirmed)
        {
            problems.Add($"Entrada de origem {origin.TaxDocumentNumber}: não está confirmada");
            return null;
        }

        var numbers = new Dictionary<Guid, int>();
        foreach (var item in invoice.Items)
        {
            var bought = origin.Items.FirstOrDefault(o => o.Key == item.PurchaseInvoiceItemOriginKey);
            var number = bought is null ? null : NfeItemNumbering.OriginNumber(bought, origin.Items.Count);

            if (number is null)
                problems.Add($"Item {item.ItemCode}: sem o item correspondente da NF-e de entrada");
            else
                numbers[bought!.Key!.Value] = number.Value;
        }

        return new NfeReturnOrigin(origin.ChaveNFe, origin.TaxDocumentNumber!, origin.TaxDocumentSeries!, origin.IssueDate, numbers);
    }
```

`NfeIssueInputAssembler`:

```csharp
    /// <summary>
    /// Entrada própria: ENTRADA normal, destinatário = fornecedor, a NF-e do produtor no NFref. Devolução de
    /// compra: SAÍDA com finalidade 4, o item da entrada no DFeReferenciado, sem pagamento.
    /// </summary>
    public static NfeIssueInput Build(
        PurchaseInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible)
    {
        var returnOrigin = invoice.IsNfeReturn
            ? context.ReturnOrigin ?? throw new DefaultException("A devolução está sem a entrada de origem.")
            : null;

        IReadOnlyList<string> referencedKeys = returnOrigin is not null
            ? [returnOrigin.AccessKey]
            : invoice.ReferencedAccessKey is { Length: 44 } producerKey ? [producerKey] : [];

        return Build(new NfeDocumentView(
            invoice.TaxDocumentNumber!, invoice.TaxDocumentSeries!, invoice.NfeRandomCode!,
            returnOrigin is null ? NfeDirection.Incoming : NfeDirection.Outgoing,
            returnOrigin, referencedKeys,
            NfeItemNumbering.Ordered(invoice.Items).Cast<INfeTaxedLine>().ToList(),
            line => ((PurchaseInvoiceItem)line).PurchaseInvoiceItemOriginKey,
            invoice.FreightTerms, invoice.NetWeight, invoice.GrossWeight, Volume: null,
            invoice.TaxPayerComments, TaxComments: null,
            returnOrigin is null ? "COMPRA" : "DEVOLUCAO DE COMPRA"), context, issuedAt, technicalResponsible);
    }
```

- [ ] **Step 5: Endpoints, EDM e DI**

Controllers — espelhos de `SalesInvoicesIssueNfeController`, `SalesInvoicesConsultNfeController`, `SalesInvoicesCompleteNfeConfirmationController` (rotas `odata/PurchaseInvoicesIssueNfe`, `odata/PurchaseInvoicesConsultNfe`, `odata/PurchaseInvoicesCompleteNfeConfirmation`, mesmos tratamentos de `NotFoundException`/`DefaultException`) e de `SalesInvoicesNfeXmlController` (`odata/PurchaseInvoicesNfeXml(Key={key})`), cada um injetando o serviço da entrada correspondente.

EDM (logo depois do bloco `salesInvoicesCompleteNfeConfirmation`):

```csharp
        // NF-e de entrada própria (spec 2026-10-05).
        var purchaseInvoicesIssueNfe = modelBuilder.Action("PurchaseInvoicesIssueNfe");
        purchaseInvoicesIssueNfe.Parameter<Guid>("Key");
        purchaseInvoicesIssueNfe.Returns<NfeIssueOutcomeDto>();

        var purchaseInvoicesConsultNfe = modelBuilder.Action("PurchaseInvoicesConsultNfe");
        purchaseInvoicesConsultNfe.Parameter<Guid>("Key");
        purchaseInvoicesConsultNfe.Returns<NfeIssueOutcomeDto>();

        var purchaseInvoicesCompleteNfeConfirmation = modelBuilder.Action("PurchaseInvoicesCompleteNfeConfirmation");
        purchaseInvoicesCompleteNfeConfirmation.Parameter<Guid>("Key");
        purchaseInvoicesCompleteNfeConfirmation.Returns<NfeIssueOutcomeDto>();
```

e, junto de `salesInvoicesNfeXml` (~linha 1360):

```csharp
        var purchaseInvoicesNfeXml = modelBuilder.Function("PurchaseInvoicesNfeXml");
        purchaseInvoicesNfeXml.Parameter<Guid>("Key");
        purchaseInvoicesNfeXml.Returns<IActionResult>();
```

DI (bloco da NF-e): `PurchaseInvoiceNfeResultHandler`, `PurchaseInvoicesNfeIssueService`, `PurchaseInvoicesNfeConsultService`, `PurchaseInvoicesNfeXmlDownloadService`, `PurchaseInvoicesNfeCompleteConfirmationService` como `AddScoped`.

- [ ] **Step 6: Ver passar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Nfe|FullyQualifiedName~PurchaseInvoice"` — Expected: PASS. Depois `dotnet build SiagroB1.sln`.

- [ ] **Step 7: Commit (backend)**

Mensagem: `feat(invoice): emissão da NF-e de entrada própria pelo documento de entrada`.

---

### Task 9: Devolução de compra

Spec: §9, D2, D11, D12, §8.3 (saldo antes da reserva).

**Files:**
- Create: `SiagroB1.Domain/Dtos/PurchaseInvoiceNfeReturnableItemDto.cs`
- Create: `SiagroB1.Application/Services/Nfe/PurchaseInvoiceNfeReturnBalance.cs`, `PurchaseInvoicesNfeReturnCreateService.cs`, `PurchaseInvoicesNfeReturnableItemsService.cs`
- Modify: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs` (saldo antes da reserva)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesUpdateService.cs`, `PurchaseInvoicesItemsUpdateService.cs` (saldo ao editar a devolução)
- Create: `SiagroB1.Web/Actions/Nfe/NfeReturnActionParameters.cs`, `PurchaseInvoicesCreateNfeReturnController.cs`; `SiagroB1.Web/Functions/Nfe/PurchaseInvoicesNfeReturnableItemsController.cs`
- Modify: `SiagroB1.Web/Actions/Nfe/SalesInvoicesCreateNfeReturnController.cs` (usa `NfeReturnActionParameters.Quantities`)
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeReturnTests.cs`, acrescentar à `PurchaseInvoiceNfeEdmModelTests.cs`

**Interfaces:**
- Consumes: `PurchaseInvoicesCreateService.ExecuteAsync(invoice, userName, nfeReturn: true)` (Task 5); prontidão/montador da devolução (Task 8); `NfeItemNumbering.OriginNumber` (Task 3).
- Produces:
  - `public sealed record PurchaseInvoiceNfeReturnItem(Guid OriginItemKey, decimal Quantity);`
  - `public sealed record PurchaseInvoiceNfeReturnRequest(Guid PurchaseInvoiceKey, IReadOnlyList<PurchaseInvoiceNfeReturnItem> Items, string Reason);`
  - `PurchaseInvoicesNfeReturnCreateService(IUnitOfWork db, TaxCalculationGate gate, PurchaseInvoicesCreateService createService, Func<DateTimeOffset>? clock = null, TimeZoneInfo? storageZone = null)` → `Task<PurchaseInvoice> ExecuteAsync(PurchaseInvoiceNfeReturnRequest request, string userName)`.
  - `PurchaseInvoiceNfeReturnBalance.ReturnedByOriginItemAsync(AppDbContext, Guid originKey, Guid? excludingReturnKey)`, `EnsureWithinAsync(AppDbContext, PurchaseInvoice returnInvoice, IEnumerable<PurchaseInvoiceItem> lines)`.
  - `PurchaseInvoicesNfeReturnableItemsService(IUnitOfWork db)` → `Task<IReadOnlyList<PurchaseInvoiceNfeReturnableItemDto>> ExecuteAsync(Guid key)`.
  - DTO: `OriginItemKey`, `ItemCode`, `ItemName`, `UnitOfMeasureCode`, `PurchasedQuantity`, `ReturnedQuantity`, `Returnable` (double, `[JsonPropertyName]` PascalCase).
  - OData: `PurchaseInvoicesCreateNfeReturn(Key, OriginItemKeys: Collection(Edm.Guid), Quantities: Collection(Edm.Double), Reason)` → `Edm.Guid`; `PurchaseInvoicesNfeReturnableItems(Key=…)` → coleção do DTO.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Nfe/PurchaseInvoicesNfeReturnTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Devolver" a entrada própria autorizada e emitir a devolução de compra (spec §9).</summary>
public class PurchaseInvoicesNfeReturnTests
{
    private static FakeBusinessPartnerService Partners() => new(
        names: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "PRODUTOR RURAL TESTE" },
        states: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "SP" });

    private static PurchaseInvoicesCreateService CreateService(PurchaseNfeScenario scenario) =>
        new(scenario.Db, Partners(), new FakeItemService(), TaxTestServices.PurchaseApply(scenario.Db, Partners()));

    private static PurchaseInvoicesNfeReturnCreateService Returns(PurchaseNfeScenario scenario, string erp = "STANDALONE") =>
        new(scenario.Db, new TaxCalculationGate(scenario.Db, NfeTestSeed.Config(erp)), CreateService(scenario),
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

    /// <summary>A entrada do cenário autorizada e confirmada pela emissão (é o que dá chave e nItem à origem).</summary>
    private static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Origin)> AuthorizedOriginAsync(bool twoItems = false)
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync(twoItems);
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        scenario.Db.Context.ChangeTracker.Clear();

        return (scenario, await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario));
    }

    private static PurchaseInvoiceNfeReturnRequest Request(PurchaseInvoice origin, decimal quantity, string reason = "grão fora do padrão", int item = 0) =>
        new(origin.Key, [new PurchaseInvoiceNfeReturnItem(origin.Items.OrderBy(i => i.NfeItemNumber).ElementAt(item).Key!.Value, quantity)], reason);

    [Fact]
    public async Task Partial_return_is_born_pending_with_the_return_usage_and_proportional_weights()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key);
        Assert.Equal(PurchaseInvoiceType.Return, saved.InvoiceType);
        Assert.Equal(DocumentIssuerType.Own, saved.IssuerType);
        Assert.True(saved.IsNfeReturn);
        Assert.Equal(InvoiceStatus.Pending, saved.InvoiceStatus);
        Assert.Equal(origin.Key, saved.PurchaseInvoiceOriginKey);
        Assert.Null(saved.PaymentConditionCode);
        Assert.Null(saved.ReferencedAccessKey);
        Assert.Equal(400m, saved.NetWeight);
        Assert.Equal(400m, saved.GrossWeight);
        var line = saved.Items.Single();
        Assert.Equal(400m, line.Quantity);
        Assert.Equal(1.5m, line.UnitPrice);
        Assert.Equal(scenario.ReturnUsage, line.UsageCode);
        Assert.Equal("5202", line.Cfop);
        Assert.StartsWith($"Devolução da NF-e 1 série 1. Motivo: grão fora do padrão", saved.Comments);
    }

    [Fact]
    public async Task Origin_is_left_untouched()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();

        await Returns(scenario).ExecuteAsync(Request(origin, 1000m), "tester");

        var after = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario);
        Assert.Equal(InvoiceStatus.Confirmed, after.InvoiceStatus);
        Assert.Equal(1000m, after.Items.Single().Quantity);
    }

    [Fact]
    public async Task Quantity_above_the_returnable_balance_is_refused()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();
        await Returns(scenario).ExecuteAsync(Request(origin, 600m), "tester");

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, 400.001m), "tester"));

        Assert.Equal("Item TRIGO: a quantidade a devolver (400,001) passa do saldo devolvível (400,000).", e.Message);
    }

    [Theory]
    [InlineData("", "Informe o motivo da devolução.")]
    [InlineData("   ", "Informe o motivo da devolução.")]
    public async Task Reason_is_required(string reason, string message)
    {
        var (scenario, origin) = await AuthorizedOriginAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, 10m, reason), "tester"));

        Assert.Equal(message, e.Message);
    }

    [Fact]
    public async Task Zero_quantity_is_refused()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, 0m), "tester"));

        Assert.Equal("Informe a quantidade a devolver de ao menos um item.", e.Message);
    }

    [Fact]
    public async Task Branch_that_does_not_issue_nfe_is_refused()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario, "SAPB1").ExecuteAsync(Request(origin, 10m), "tester"));

        Assert.Equal("A filial 01 não emite NF-e pelo Siagro.", e.Message);
    }

    [Fact]
    public async Task Entry_without_authorized_nfe_is_refused()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var origin = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, 10m), "tester"));

        Assert.Equal(
            "O documento de entrada não tem NF-e própria autorizada pelo Siagro: a devolução de compra parte de uma entrada própria autorizada.",
            e.Message);
    }

    [Fact]
    public async Task Usage_without_return_usage_is_refused()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();
        var usage = await scenario.Db.Context.Usages.SingleAsync(u => u.Name == "COMPRA DE MERCADORIA");
        usage.ReturnUsageCode = null;
        await scenario.Db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, 10m), "tester"));

        Assert.Equal($"A natureza {usage.Code} COMPRA DE MERCADORIA da compra não tem natureza de devolução cadastrada.", e.Message);
    }

    [Fact]
    public async Task Returning_only_the_second_item_references_its_entry_item_number()
    {
        // Review Focus 3.
        var (scenario, origin) = await AuthorizedOriginAsync(twoItems: true);
        var created = await Returns(scenario).ExecuteAsync(Request(origin, 100m, item: 1), "tester");
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(created.Key, "tester");

        var xml = Assert.Single(sefaz.Sent).Xml;
        Assert.Contains("<tpNF>1</tpNF>", xml);
        Assert.Contains("<finNFe>4</finNFe>", xml);
        Assert.Contains($"<DFeReferenciado><chaveAcesso>{origin.ChaveNFe}</chaveAcesso><nItem>2</nItem></DFeReferenciado>", xml);
        Assert.DoesNotContain("<NFref>", xml);
        Assert.Contains("<tPag>90</tPag>", xml);
        Assert.Contains("Devolução da NF-e nº 1, série 1", xml);
    }

    [Fact]
    public async Task Authorized_return_is_confirmed()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();
        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(created.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Two_pending_returns_above_the_purchase_are_refused_before_the_number()
    {
        // Review Focus 4.
        var (scenario, origin) = await AuthorizedOriginAsync();
        await Returns(scenario).ExecuteAsync(Request(origin, 600m), "tester");
        var second = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");
        var context = TestDb.CreateUnitOfWork(scenario.DatabaseName).Context;
        var line = await context.PurchaseInvoicesItems.SingleAsync(i => i.PurchaseInvoiceKey == second.Key);
        line.Quantity = 500m; // gravado por fora, como um dado concorrente
        await context.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        var reservation = new FakeNfeNumberReservationService(10);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, new FakeNfeSefazClient(), reservation).ExecuteAsync(second.Key, "tester"));

        Assert.Equal("Item TRIGO: a devolução (500,000) passa do saldo devolvível da compra (400,000).", e.Message);
        Assert.Equal(0, reservation.Calls);
    }

    [Fact]
    public async Task Editing_the_return_above_the_balance_is_refused()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();
        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");
        var line = await scenario.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == created.Key);
        line.Quantity = 1000.5m;

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsUpdateService(scenario.Db, new FakeItemService(), TaxTestServices.PurchaseApply(scenario.Db, Partners()))
                .ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Equal("Item TRIGO: a devolução (1.000,500) passa do saldo devolvível da compra (1.000,000).", e.Message);
    }

    [Fact]
    public async Task Returnable_items_show_purchased_returned_and_balance()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();
        await Returns(scenario).ExecuteAsync(Request(origin, 250m), "tester");

        var row = Assert.Single(await new PurchaseInvoicesNfeReturnableItemsService(scenario.Db).ExecuteAsync(origin.Key));

        Assert.Equal("TRIGO", row.ItemCode);
        Assert.Equal(1000d, row.PurchasedQuantity);
        Assert.Equal(250d, row.ReturnedQuantity);
        Assert.Equal(750d, row.Returnable);
    }
}
```

Em `PurchaseInvoiceNfeEdmModelTests` acrescente:

```csharp
    [Fact]
    public void Create_nfe_return_is_an_action_with_parallel_double_quantities()
    {
        var action = Model().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == "PurchaseInvoicesCreateNfeReturn");

        Assert.Equal("Collection(Edm.Guid)", action.Parameters.Single(p => p.Name == "OriginItemKeys").Type.FullName());
        Assert.Equal("Collection(Edm.Double)", action.Parameters.Single(p => p.Name == "Quantities").Type.FullName());
        Assert.Equal("Edm.String", action.Parameters.Single(p => p.Name == "Reason").Type.FullName());
        Assert.Equal("Edm.Guid", action.ReturnType.FullName());
    }

    [Fact]
    public void Returnable_items_is_a_collection_function()
    {
        var function = Model().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "PurchaseInvoicesNfeReturnableItems");

        Assert.True(function.ReturnType.IsCollection());
    }
```

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoicesNfeReturnTests|FullyQualifiedName~PurchaseInvoiceNfeEdmModelTests"` — Expected: falha de compilação.

- [ ] **Step 2: Saldo, DTO e itens devolvíveis**

`PurchaseInvoiceNfeReturnBalance` — espelho de `SalesInvoiceNfeReturnBalance` sobre `PurchaseInvoicesItems`, contando só devoluções de compra (o `PurchaseInvoiceOriginKey` também serve à remessa de venda futura, que **não** é devolução):

```csharp
    public static Task<Dictionary<Guid, decimal>> ReturnedByOriginItemAsync(
        AppDbContext context, Guid originInvoiceKey, Guid? excludingReturnKey) =>
        context.PurchaseInvoicesItems.AsNoTracking()
            .Where(i => i.PurchaseInvoiceItemOriginKey != null
                        && i.PurchaseInvoice!.PurchaseInvoiceOriginKey == originInvoiceKey
                        && i.PurchaseInvoice.InvoiceType == PurchaseInvoiceType.Return
                        && i.PurchaseInvoice.IsNfeReturn
                        && i.PurchaseInvoice.InvoiceStatus != InvoiceStatus.Cancelled
                        && i.PurchaseInvoiceKey != excludingReturnKey)
            .GroupBy(i => i.PurchaseInvoiceItemOriginKey!.Value)
            .Select(g => new { g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity);
```

`EnsureWithinAsync(AppDbContext context, PurchaseInvoice returnInvoice, IEnumerable<PurchaseInvoiceItem> lines)` com a mesma lógica da venda e as mensagens "A devolução está sem a entrada de origem.", "O item {ItemCode} da devolução não aponta um item da entrada." e `$"Item {line.ItemCode}: a devolução ({line.Quantity.ToString("N3", PtBr)}) passa do saldo devolvível da compra ({available.ToString("N3", PtBr)})."`.

DTO: copie `SalesInvoiceNfeReturnableItemDto` para `PurchaseInvoiceNfeReturnableItemDto` trocando `SoldQuantity` por `PurchasedQuantity` (inclusive o `JsonPropertyName`), `ItemCode` passa a `string?` e o summary fala da entrada.

`PurchaseInvoicesNfeReturnableItemsService` — espelho de `SalesInvoicesNfeReturnableItemsService` sobre `PurchaseInvoices`/`PurchaseInvoiceNfeReturnBalance`, `NotFoundException("Documento de entrada não encontrado.")`, `PurchasedQuantity = (double)item.Quantity`.

- [ ] **Step 3: `PurchaseInvoicesNfeReturnCreateService`**

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

public sealed record PurchaseInvoiceNfeReturnItem(Guid OriginItemKey, decimal Quantity);

public sealed record PurchaseInvoiceNfeReturnRequest(
    Guid PurchaseInvoiceKey, IReadOnlyList<PurchaseInvoiceNfeReturnItem> Items, string Reason);

/// <summary>
/// "Devolver" do Documento de Entrada (spec §9.2): a partir de uma entrada própria autorizada pelo Siagro, cria a
/// devolução de compra Pendente que sai com NF-e PRÓPRIA de saída (finalidade 4). Não confirma (quem confirma é a
/// emissão) e não mexe na entrada de origem: o saldo devolvível é calculado das devoluções (D12).
/// </summary>
public class PurchaseInvoicesNfeReturnCreateService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    PurchaseInvoicesCreateService createService,
    Func<DateTimeOffset>? clock = null,
    TimeZoneInfo? storageZone = null)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Func<DateTimeOffset> _now = clock ?? NfeIssueInputAssembler.BrasiliaNow;

    // Mesmo fuso em que o OData grava as datas (o do servidor): a emissão o converte de volta para Brasília.
    private readonly TimeZoneInfo _storageZone = storageZone ?? TimeZoneInfo.Local;

    public async Task<PurchaseInvoice> ExecuteAsync(PurchaseInvoiceNfeReturnRequest request, string userName)
    {
        var origin = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
                         .FirstOrDefaultAsync(i => i.Key == request.PurchaseInvoiceKey)
                     ?? throw new NotFoundException("Documento de entrada não encontrado.");

        // TODA a validação antes de qualquer escrita.
        await ValidateOriginAsync(origin);

        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new DefaultException("Informe o motivo da devolução.");

        var quantities = await ResolveQuantitiesAsync(origin, request.Items);
        var returnUsages = await ResolveReturnUsagesAsync(origin, quantities.Keys);
        EnsureEntryItemNumbers(origin, quantities.Keys);

        var today = TimeZoneInfo.ConvertTime(_now(), _storageZone).DateTime;
        var share = quantities.Values.Sum() / origin.Items.Sum(i => i.Quantity);

        var returnInvoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(),
            BranchCode = origin.BranchCode,
            CardCode = origin.CardCode,
            CardName = origin.CardName,
            IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Return,
            IssueDate = today,
            PostingDate = today,
            PurchaseInvoiceOriginKey = origin.Key,
            // Pesos na proporção do que volta: a unidade da linha pode não ser quilo (editáveis enquanto Pendente).
            NetWeight = decimal.Round(origin.NetWeight * share, 3, MidpointRounding.AwayFromZero),
            GrossWeight = decimal.Round(origin.GrossWeight * share, 3, MidpointRounding.AwayFromZero),
            TruckCode = origin.TruckCode,
            TruckingCompanyCode = origin.TruckingCompanyCode,
            TruckingCompanyName = origin.TruckingCompanyName,
            FreightTerms = origin.FreightTerms,
            Comments = $"Devolução da NF-e {NumberText(origin.TaxDocumentNumber)} série {origin.TaxDocumentSeries}. " +
                       $"Motivo: {request.Reason.Trim()}",
        };

        foreach (var bought in NfeItemNumbering.Ordered(origin.Items).Where(i => quantities.ContainsKey(i.Key!.Value)))
        {
            returnInvoice.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(),
                ItemCode = bought.ItemCode,
                ItemName = bought.ItemName,
                UnitOfMeasureCode = bought.UnitOfMeasureCode,
                Quantity = quantities[bought.Key!.Value],
                UnitPrice = bought.UnitPrice,
                UsageCode = returnUsages[bought.Key!.Value],
                PurchaseInvoiceItemOriginKey = bought.Key,
            });
        }

        // O create calcula e confere os tributos (spec §6.2) e grava num SaveChanges só.
        await createService.ExecuteAsync(returnInvoice, userName, nfeReturn: true);

        return returnInvoice;
    }

    private async Task ValidateOriginAsync(PurchaseInvoice origin)
    {
        if (!await gate.IsActiveAsync(origin.BranchCode))
            throw new DefaultException($"A filial {origin.BranchCode} não emite NF-e pelo Siagro.");

        if (origin.IssuerType != DocumentIssuerType.Own || origin.InvoiceType != PurchaseInvoiceType.Normal ||
            origin.InvoiceStatus != InvoiceStatus.Confirmed || origin.NfeStatus != NfeStatus.Authorized ||
            origin.ChaveNFe is not { Length: 44 })
            throw new DefaultException(
                "O documento de entrada não tem NF-e própria autorizada pelo Siagro: " +
                "a devolução de compra parte de uma entrada própria autorizada.");
    }

    private async Task<Dictionary<Guid, decimal>> ResolveQuantitiesAsync(
        PurchaseInvoice origin, IReadOnlyList<PurchaseInvoiceNfeReturnItem> items)
    {
        if (items.Any(i => i.Quantity < 0))
            throw new DefaultException("A quantidade a devolver não pode ser negativa.");

        var returned = await PurchaseInvoiceNfeReturnBalance.ReturnedByOriginItemAsync(db.Context, origin.Key, null);
        var result = new Dictionary<Guid, decimal>();

        foreach (var requested in items.Where(i => i.Quantity > 0))
        {
            var bought = origin.Items.FirstOrDefault(i => i.Key == requested.OriginItemKey)
                         ?? throw new DefaultException("Item informado não pertence ao documento de entrada.");
            var available = bought.Quantity - returned.GetValueOrDefault(requested.OriginItemKey);

            if (requested.Quantity > available)
                throw new DefaultException(
                    $"Item {bought.ItemCode}: a quantidade a devolver ({requested.Quantity.ToString("N3", PtBr)}) " +
                    $"passa do saldo devolvível ({available.ToString("N3", PtBr)}).");

            result[requested.OriginItemKey] = requested.Quantity;
        }

        return result.Count == 0
            ? throw new DefaultException("Informe a quantidade a devolver de ao menos um item.")
            : result;
    }

    private async Task<Dictionary<Guid, int>> ResolveReturnUsagesAsync(PurchaseInvoice origin, IEnumerable<Guid> originItemKeys)
    {
        var result = new Dictionary<Guid, int>();

        foreach (var key in originItemKeys)
        {
            var bought = origin.Items.First(i => i.Key == key);
            var usage = bought.UsageCode is { } code
                ? await db.Context.Usages.AsNoTracking().FirstOrDefaultAsync(u => u.Code == code)
                : null;

            if (usage?.ReturnUsageCode is not { } returnCode)
                throw new DefaultException(usage is null
                    ? $"O item {bought.ItemCode} da compra está sem natureza de operação."
                    : $"A natureza {usage.Code} {usage.Name} da compra não tem natureza de devolução cadastrada.");

            result[key] = returnCode;
        }

        return result;
    }

    private static void EnsureEntryItemNumbers(PurchaseInvoice origin, IEnumerable<Guid> originItemKeys)
    {
        foreach (var key in originItemKeys)
        {
            if (NfeItemNumbering.OriginNumber(origin.Items.First(i => i.Key == key), origin.Items.Count) is null)
                throw new DefaultException(
                    "A NF-e de entrada foi emitida sem a numeração dos itens; a devolução com NF-e não está disponível para ela.");
        }
    }

    private static string? NumberText(string? number) =>
        long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : number;
}
```

- [ ] **Step 4: Saldo na emissão e na edição**

`PurchaseInvoicesNfeIssueService`:

```csharp
    // Devolução de compra: o saldo da entrada ANTES de reservar o número — a SEFAZ autorizaria uma devolução
    // maior que a compra.
    protected override async Task EnsureBeforeReservationAsync(PurchaseInvoice invoice)
    {
        if (invoice.IsNfeReturn)
            await PurchaseInvoiceNfeReturnBalance.EnsureWithinAsync(_db.Context, invoice, invoice.Items);
    }
```

(com `private readonly IUnitOfWork _db = db;` na classe.)

`PurchaseInvoicesItemsUpdateService`: depois das travas e antes do cálculo, `if (invoice.IsNfeReturn) await PurchaseInvoiceNfeReturnBalance.EnsureWithinAsync(db.Context, invoice, [existing]);`.
`PurchaseInvoicesUpdateService`: depois do `SyncItemsAsync`, antes do cálculo, `if (existing.IsNfeReturn) await PurchaseInvoiceNfeReturnBalance.EnsureWithinAsync(db.Context, existing, existing.Items);`.

- [ ] **Step 5: Endpoints, EDM e DI**

`SiagroB1.Web/Actions/Nfe/NfeReturnActionParameters.cs` — mova para cá o `Quantities(ODataActionParameters)` privado do `SalesInvoicesCreateNfeReturnController` (como `public static List<decimal> Quantities(ODataActionParameters parameters)`) e use-o nos dois controllers.

`PurchaseInvoicesCreateNfeReturnController` — espelho do da venda: rota `odata/PurchaseInvoicesCreateNfeReturn`, monta `PurchaseInvoiceNfeReturnRequest`, devolve `Ok(created.Key)`.
`PurchaseInvoicesNfeReturnableItemsController` — espelho de `SalesInvoicesNfeReturnableItemsController`: `odata/PurchaseInvoicesNfeReturnableItems(Key={key})`.

EDM (ao lado do bloco da devolução de venda):

```csharp
        // Devolução de compra (spec 2026-10-05): cria a devolução a partir da entrada própria autorizada.
        var purchaseInvoicesCreateNfeReturn = modelBuilder.Action("PurchaseInvoicesCreateNfeReturn");
        purchaseInvoicesCreateNfeReturn.Parameter<Guid>("Key");
        purchaseInvoicesCreateNfeReturn.CollectionParameter<Guid>("OriginItemKeys");
        purchaseInvoicesCreateNfeReturn.CollectionParameter<double>("Quantities");
        purchaseInvoicesCreateNfeReturn.Parameter<string>("Reason");
        purchaseInvoicesCreateNfeReturn.Returns<Guid>();

        var purchaseInvoicesNfeReturnableItems = modelBuilder.Function("PurchaseInvoicesNfeReturnableItems");
        purchaseInvoicesNfeReturnableItems.Parameter<Guid>("Key");
        purchaseInvoicesNfeReturnableItems.ReturnsCollection<PurchaseInvoiceNfeReturnableItemDto>();
```

DI: `PurchaseInvoicesNfeReturnCreateService`, `PurchaseInvoicesNfeReturnableItemsService`.

- [ ] **Step 6: Ver passar**

Run: `dotnet test SiagroB1.Application.Tests` — Expected: PASS (suíte inteira). `dotnet build SiagroB1.sln` sem erro.

- [ ] **Step 7: Commit (backend)**

Mensagem: `feat(invoice): devolução de compra com NF-e própria a partir da entrada autorizada`.

---

### Task 10: DANFE da NF-e de entrada no Reports

Spec: §8.3.

**Files:**
- Modify: `SiagroB1.Reports/Services/DanfeReportService.cs`, `SiagroB1.Reports/Controllers/DanfeController.cs`
- Test: `SiagroB1.Application.Tests/Reports/DanfeReportServiceTests.cs`

**Interfaces:**
- Produces: `DanfeReportService.GeneratePurchasePdfAsync(Guid invoiceKey)` → `(byte[] Pdf, string FileName)`; rota `POST /reports/Danfe/purchase-invoices/{key:guid}/print`.

- [ ] **Step 1: Teste que falha**

Em `DanfeReportServiceTests.cs`, acrescente (mesmo preparo do teste de saída, com a entrada própria do Fiscal):

```csharp
    [Fact]
    public async Task Authorized_purchase_nfe_renders_a_pdf()
    {
        var db = TestDb.CreateUnitOfWork();
        using var certificate = CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password);
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP", certificate, NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.PurchaseEntryInput(), settings);
        var invoiceKey = Guid.NewGuid();
        db.Context.PurchaseInvoiceNfeXmls.Add(new PurchaseInvoiceNfeXml
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = invoiceKey, Kind = NfeXmlKind.Authorized, CreatedAt = DateTime.Now,
            Xml = NfeProcComposer.Compose(signed.Xml, FakeNfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!),
        });
        await db.SaveChangesAsync();

        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance);

        var (pdf, fileName) = await new DanfeReportService(db, environment, header).GeneratePurchasePdfAsync(invoiceKey);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal($"{signed.AccessKey}-danfe.pdf", fileName);
    }
```

(se o `InMemory` exigir o documento pai para a FK do XML, grave antes um `PurchaseInvoice` com `Key = invoiceKey`.)

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~DanfeReportServiceTests"` — Expected: falha de compilação.

- [ ] **Step 2: Implementação**

`DanfeReportService`: extraia de `GeneratePdfAsync` um `private (byte[] Pdf, string FileName) Render(string xml)` (tudo depois da leitura do XML), mantenha `GeneratePdfAsync(Guid invoiceKey)` lendo `SalesInvoiceNfeXmls` e acrescente:

```csharp
    /// <summary>DANFE da NF-e do documento de entrada (entrada própria ou devolução de compra).</summary>
    public async Task<(byte[] Pdf, string FileName)> GeneratePurchasePdfAsync(Guid invoiceKey) =>
        Render(await db.Context.PurchaseInvoiceNfeXmls.LatestAuthorizedXmlAsync(invoiceKey));
```

`DanfeController`: segunda action, mesmo tratamento de erro:

```csharp
    [HttpPost("purchase-invoices/{key:guid}/print")]
    public async Task<IActionResult> PurchaseReport(Guid key)
```

(extraia o corpo comum num método privado que recebe a `Func<Task<(byte[], string)>>`, para as duas actions não duplicarem o try/catch.)

- [ ] **Step 3: Ver passar e commit**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~DanfeReportServiceTests"` — Expected: PASS.
Mensagem: `feat(reports): DANFE da NF-e do documento de entrada`.

---

### Task 11: Frontend — natureza de devolução nos dois sentidos

Spec: D4, §11 (Natureza — formulário).

**Files:**
- Modify: `siagro-b1-frontend/webapp/view/usages/fragments/Form.fragment.xml` (bloco "Natureza de devolução", ~linhas 105-126)
- Modify: `siagro-b1-frontend/webapp/controller/usages/BaseController.ts`

**Interfaces:**
- Consumes: backend da Task 4 (natureza de Entrada aceita devolução de Saída).

- [ ] **Step 1: Campo visível nos dois sentidos**

No fragmento, troque o comentário e as três condições `visible` do bloco por `visible="{ui>/identityEditable}"` (o campo vale para Saída e Entrada):

```xml
          <!-- Natureza de devolução, só STANDALONE: a de venda (Saída) aponta a de devolução de venda (Entrada)
               e a de compra (Entrada) aponta a de devolução de compra (Saída). O "Devolver" herda esta
               natureza em cada linha da devolução. -->
```

- [ ] **Step 2: Value help do sentido oposto e limpeza ao trocar o tipo**

Em `BaseController.ts`:

```ts
	/**
	 * Só natureza ativa do sentido OPOSTO ao desta natureza: Saída → Entrada, Entrada → Saída. O enum vai
	 * como $filter estático (o Filter do UI5 não formata enum). setProperty sem await: no update group
	 * diferido a Promise só resolve no submit.
	 */
	async onReturnUsageValueHelp(ev: Input$ValueHelpRequestEvent) {
		const oTarget = ev.getSource().getBindingContext() as Context;
		const direction = oTarget.getProperty("Direction") as string;
		const opposite = direction === "Incoming" ? "Outgoing" : "Incoming";

		const oSelected = await DialogHelper.openTableSelectDialog(
			this, "UsagesSelectDialog", ["Name", "Description"],
			[new Filter("Inactive", FilterOperator.EQ, false)], undefined, `Direction eq '${opposite}'`);

		if (!oSelected) {
			return;
		}

		void oTarget.setProperty("ReturnUsageCode", oSelected.getProperty("Code"));
		void oTarget.setProperty("ReturnUsageName", oSelected.getProperty("Name"));
	}
```

e `onDirectionChange` passa a limpar o vínculo em **qualquer** troca de tipo (a devolução precisa ser do sentido oposto, então a escolhida antes deixa de servir):

```ts
	/**
	 * A natureza de devolução é do sentido oposto: trocar o tipo invalida a escolhida antes, então o vínculo
	 * é limpo. Sem await: o contexto está num update group diferido.
	 */
	onDirectionChange(ev: Select$ChangeEvent) {
		const oTarget = ev.getSource().getBindingContext() as Context;
		void oTarget.setProperty("ReturnUsageCode", null);
		void oTarget.setProperty("ReturnUsageName", null);
	}
```

(Se `Select$ChangeEvent` deixar de ser usado de forma relevante, mantenha o parâmetro tipado — é o handler do `change`.)

- [ ] **Step 3: Gates e commit (frontend)**

Run (em `siagro-b1-frontend/`): `yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (≤ 920).
Mensagem: `feat(master-data): natureza de compra escolhe a natureza de devolução de compra`.

---

### Task 12: Frontend — regras puras, rotas e helpers compartilhados

Spec: §11 (`ServerRoutes`), D13 (reuso do diálogo de devolução).

**Files:**
- Create: `siagro-b1-frontend/webapp/helpers/PurchaseInvoiceNfeHelpers.ts`
- Modify: `siagro-b1-frontend/webapp/helpers/NfeReturnHelpers.ts` (tipos genéricos)
- Modify: `siagro-b1-frontend/webapp/model/ServerRoutes.ts`
- Modify: `siagro-b1-frontend/webapp/controller/common/CommonController.ts` (`isTaxCalculationActive`, `refreshAnyBranchIssuesNfe` movido do `salesInvoices/Main.controller.ts`)
- Modify: `siagro-b1-frontend/webapp/controller/salesInvoices/BaseController.ts` (`refreshTaxLock` usa `isTaxCalculationActive`), `controller/salesInvoices/Main.controller.ts` (usa o `refreshAnyBranchIssuesNfe` herdado)
- Test: `siagro-b1-frontend/webapp/test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts` (novo), `test/unit/unitTests.qunit.ts`

**Interfaces:**
- Produces:
  - `isPurchaseNfeMode(taxLocked: boolean, issuerType: string): boolean`
  - `canIssuePurchaseNfe(doc: PurchaseNfeState, nfeMode: boolean): boolean`
  - `canReturnPurchase(doc: PurchaseNfeState, nfeMode: boolean): boolean`
  - `isValidAccessKey(value: string): boolean` (44 dígitos depois de tirar o resto, DV mod 11)
  - `PURCHASE_ITEM_SELECT: string` (o `$select` das linhas no Detail/Edit)
  - `type PurchaseNfeState = { InvoiceStatus: string; InvoiceType: string; IssuerType: string; IsNfeReturn: boolean; NfeStatus: string }`
  - `NfeReturnHelpers`: `type ReturnableRow = { OriginItemKey: string; ItemCode: string; Returnable: number; ReturnQuantity: number | null }`; `prefillNfeReturnRows<T extends ReturnableRow>(rows: T[]): T[]`; `hasReturnableBalance(rows: ReturnableRow[])`; `buildNfeReturnPayload(rows: ReturnableRow[], reason: string)`; `NfeReturnRow` continua exportado (o da venda) e passa a estender `ReturnableRow`.
  - `ServerRoutes`: `purchaseInvoicesIssueNfe: '/odata/PurchaseInvoicesIssueNfe'`, `purchaseInvoicesConsultNfe: '/odata/PurchaseInvoicesConsultNfe'`, `purchaseInvoicesCompleteNfeConfirmation: '/odata/PurchaseInvoicesCompleteNfeConfirmation'`, `purchaseInvoicesNfeXml: '/odata/PurchaseInvoicesNfeXml'`, `purchaseInvoicesDanfeReport: '/reports/Danfe/purchase-invoices'`, `purchaseInvoicesCreateNfeReturn: '/PurchaseInvoicesCreateNfeReturn(...)'`, `purchaseInvoicesNfeReturnableItems: '/odata/PurchaseInvoicesNfeReturnableItems'`.
  - `CommonController.isTaxCalculationActive(branchCode: string): Promise<boolean>`, `CommonController.refreshAnyBranchIssuesNfe(): Promise<void>` (o mesmo de hoje da lista de saída).

- [ ] **Step 1: Teste que falha**

`test/unit/helpers/PurchaseInvoiceNfeHelpers.qunit.ts`:

```ts
import {
	canIssuePurchaseNfe, canReturnPurchase, isPurchaseNfeMode, isValidAccessKey, PurchaseNfeState
} from "siagrob1/helpers/PurchaseInvoiceNfeHelpers";

QUnit.module("PurchaseInvoiceNfeHelpers - NF-e do documento de entrada");

const pending: PurchaseNfeState = {
	InvoiceStatus: "Pending", InvoiceType: "Normal", IssuerType: "Own", IsNfeReturn: false, NfeStatus: "None",
};

QUnit.test("modo NF-e só com a regra ativa e emissão própria", function (assert) {
	assert.strictEqual(isPurchaseNfeMode(true, "Own"), true);
	assert.strictEqual(isPurchaseNfeMode(true, "ThirdParty"), false);
	assert.strictEqual(isPurchaseNfeMode(false, "Own"), false);
});

QUnit.test("emite a entrada própria pendente sem NF-e ou rejeitada", function (assert) {
	assert.strictEqual(canIssuePurchaseNfe(pending, true), true);
	assert.strictEqual(canIssuePurchaseNfe({ ...pending, NfeStatus: "Rejected" }, true), true);
	assert.strictEqual(canIssuePurchaseNfe({ ...pending, NfeStatus: "Processing" }, true), false);
	assert.strictEqual(canIssuePurchaseNfe({ ...pending, InvoiceStatus: "Confirmed" }, true), false);
	assert.strictEqual(canIssuePurchaseNfe(pending, false), false);
});

QUnit.test("emite a devolução de compra, não a devolução do cliente", function (assert) {
	assert.strictEqual(canIssuePurchaseNfe({ ...pending, InvoiceType: "Return", IsNfeReturn: true }, true), true);
	assert.strictEqual(canIssuePurchaseNfe({ ...pending, InvoiceType: "Return", IsNfeReturn: false }, true), false);
});

QUnit.test("devolve só a entrada própria Normal, confirmada e autorizada", function (assert) {
	const authorized = { ...pending, InvoiceStatus: "Confirmed", NfeStatus: "Authorized" };
	assert.strictEqual(canReturnPurchase(authorized, true), true);
	assert.strictEqual(canReturnPurchase({ ...authorized, InvoiceType: "Return", IsNfeReturn: true }, true), false);
	assert.strictEqual(canReturnPurchase({ ...authorized, NfeStatus: "None" }, true), false);
	assert.strictEqual(canReturnPurchase(authorized, false), false);
});

QUnit.test("chave de NF-e: 44 dígitos com dígito verificador mod 11", function (assert) {
	assert.strictEqual(isValidAccessKey("35261011222333000181550010000004561123456780"), true);
	assert.strictEqual(isValidAccessKey("3526 1011 2223 3300 0181 5500 1000 0004 5611 2345 6780"), true);
	assert.strictEqual(isValidAccessKey("35261011222333000181550010000004561123456781"), false);
	assert.strictEqual(isValidAccessKey("123"), false);
	assert.strictEqual(isValidAccessKey(""), false);
});
```

Registre em `test/unit/unitTests.qunit.ts`: `import "./helpers/PurchaseInvoiceNfeHelpers.qunit";`.

Run: `npx ui5 serve --port 8081` (segundo plano) + `npx ui5-test-runner --url "http://localhost:8081/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests"` — Expected: falha (módulo inexistente).

- [ ] **Step 2: `PurchaseInvoiceNfeHelpers.ts`**

```ts
/**
 * Regras de tela da NF-e do Documento de Entrada (spec 2026-10-05), puras para poderem ser testadas sem view.
 * Quem decide de verdade é o servidor; isto só esconde o botão que voltaria recusado.
 */
export type PurchaseNfeState = {
	InvoiceStatus: string;
	InvoiceType: string;
	IssuerType: string;
	IsNfeReturn: boolean;
	NfeStatus: string;
};

/** Modo NF-e: a filial emite pelo Siagro (regra ativa) e o documento é de emissão própria. */
export function isPurchaseNfeMode(taxLocked: boolean, issuerType: string): boolean {
	return taxLocked === true && issuerType === "Own";
}

/** Entrada própria Normal ou devolução de compra, Pendente, sem NF-e ou com a anterior rejeitada. */
export function canIssuePurchaseNfe(doc: PurchaseNfeState, nfeMode: boolean): boolean {
	const issuable = doc.InvoiceType === "Normal" || (doc.InvoiceType === "Return" && doc.IsNfeReturn === true);
	return nfeMode === true && issuable && doc.InvoiceStatus === "Pending" &&
		(doc.NfeStatus === "None" || doc.NfeStatus === "Rejected" || !doc.NfeStatus);
}

/** "Devolver": entrada própria Normal, confirmada, com NF-e autorizada. */
export function canReturnPurchase(doc: PurchaseNfeState, nfeMode: boolean): boolean {
	return nfeMode === true && doc.InvoiceType === "Normal" && doc.IsNfeReturn !== true &&
		doc.InvoiceStatus === "Confirmed" && doc.NfeStatus === "Authorized";
}

/** 44 dígitos (o resto é descartado) e dígito verificador mod 11, como o servidor confere. */
export function isValidAccessKey(value: string): boolean {
	const key = (value ?? "").replace(/\D/g, "");

	if (key.length !== 44) {
		return false;
	}

	let sum = 0;
	let weight = 2;
	for (let i = 42; i >= 0; i--) {
		sum += Number(key[i]) * weight;
		weight = weight === 9 ? 2 : weight + 1;
	}

	const digit = 11 - (sum % 11);
	return (digit >= 10 ? 0 : digit) === Number(key[43]);
}

/**
 * $select das linhas no Detail/Edit. Explícito porque as colunas fiscais só aparecem no modo NF-e — informação
 * que ainda não chegou quando o UI5 monta o $select automático — e o diálogo fiscal lê a fotografia inteira.
 */
export const PURCHASE_ITEM_SELECT =
	"Key,ItemCode,ItemName,UnitOfMeasureCode,Quantity,UnitPrice,Total,AssessedShortage,Difference," +
	"SalesInvoiceItemKey,PurchaseContractKey,PurchaseInvoiceItemOriginKey,UsageCode,UsageName,Cfop,Ncm,GoodsOrigin," +
	"CstIcms,IcmsBase,IcmsRate,IcmsValue,IcmsBaseReduction,IcmsDeferral,IcmsOperationValue,IcmsDeferredValue," +
	"IcmsBenefitCode,CstPis,PisBase,PisRate,PisValue,CstCofins,CofinsBase,CofinsRate,CofinsValue,IbsCbsCst," +
	"IbsCbsClassCode,IbsCbsBase,CbsRate,CbsRateReduction,CbsValue,IbsStateRate,IbsMunicipalRate,IbsRateReduction," +
	"IbsStateValue,IbsMunicipalValue,NfeItemNumber";
```

- [ ] **Step 3: `NfeReturnHelpers` genérico, rotas e controller comum**

`NfeReturnHelpers.ts`: introduza

```ts
/** O que o diálogo "Devolver" usa de cada linha — comum à venda e à compra. */
export type ReturnableRow = {
	OriginItemKey: string;
	ItemCode: string;
	Returnable: number;
	// eslint-disable-next-line @typescript-eslint/no-redundant-type-constituents -- strictNullChecks desligado; null é intencional
	ReturnQuantity: number | null;
};
```

faça `NfeReturnRow` = `ReturnableRow & { ItemName: string; SoldQuantity: number; ReturnedQuantity: number }` (mesmos campos de hoje), e troque as assinaturas para `prefillNfeReturnRows<T extends ReturnableRow>(rows: T[]): T[]`, `hasReturnableBalance(rows: ReturnableRow[])`, `buildNfeReturnPayload(rows: ReturnableRow[], reason: string)` — corpos iguais. Os testes de `NfeReturnHelpers.qunit.ts` não mudam.

`ServerRoutes.ts`: as sete rotas da seção Interfaces, perto das de NF-e da saída.

`CommonController.ts`: acrescente

```ts
  /**
   * A filial calcula tributos e emite NF-e pelo Siagro (`TaxCalculationGate` no servidor)? Falha = false: quem
   * recalcula e trava continua sendo o servidor.
   */
  protected async isTaxCalculationActive(branchCode: string): Promise<boolean> {
    if (!branchCode) {
      return false;
    }

    try {
      const oModel = this.getView().getModel() as ODataModel;
      const oFunction = oModel.bindContext(ServerRoutes.taxCalculationIsActive);
      oFunction.setParameter("BranchCode", branchCode);
      await oFunction.invoke();
      return oFunction.getBoundContext().getProperty("value") === true;
    } catch {
      return false;
    }
  }
```

e mova para ele, sem mudar o corpo, o `refreshAnyBranchIssuesNfe` de `salesInvoices/Main.controller.ts` (como `protected`). Em `salesInvoices/BaseController.ts`, o `refreshTaxLock` passa a `const locked = await this.isTaxCalculationActive(branchCode); uiModel.setProperty("/taxLocked", locked); return locked;` (mantendo o zerar antes e o `return false` sem filial). Imports que faltarem (`ODataModel`, `ServerRoutes`) entram no `CommonController`.

- [ ] **Step 4: Gates e commit (frontend)**

QUnit (Step 1) → PASS; `yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (≤ 920). Pare o `ui5 serve` da 8081 pelo PID.
Mensagem: `feat(invoice): regras de tela da NF-e do documento de entrada`.

---

### Task 13: Frontend — formulário e itens do Documento de Entrada no modo NF-e

Spec: §11 (formulário e itens), D7, D8, D10, §9.3.

**Files:**
- Modify: `siagro-b1-frontend/webapp/controller/purchaseInvoices/BaseController.ts`, `Add.controller.ts`, `Edit.controller.ts`
- Modify: `siagro-b1-frontend/webapp/view/purchaseInvoices/fragments/Form.fragment.xml`, `Items.fragment.xml`, `view/purchaseInvoices/Add.view.xml`, `Edit.view.xml`
- Create: `siagro-b1-frontend/webapp/view/purchaseInvoices/fragments/ItemFiscalDialog.fragment.xml`

**Interfaces:**
- Consumes: `isPurchaseNfeMode`, `PURCHASE_ITEM_SELECT` (Task 12); `CommonController.isTaxCalculationActive`, `openPaymentConditionsValueHelp`, `refreshPaymentConditionName`, `getBranchInfo` (já existem / Task 12).
- Produces: no `ui` model, `/taxLocked`, `/nfeMode`, `/nfeReturn`, `/paymentConditionName`; métodos `refreshNfeMode()`, `onBranchChange()`, `onIssuerTypeChange()`, `openPurchaseUsageValueHelp(ev)`, `onOpenItemFiscal()`, `onCloseItemFiscal()` no `purchaseInvoices/BaseController`.

- [ ] **Step 1: BaseController — modo NF-e, natureza e diálogo fiscal**

Em `controller/purchaseInvoices/BaseController.ts` acrescente:

```ts
  private itemFiscalDialog: Dialog;

  /**
   * Modo NF-e do documento ligado à view: filial que emite pelo Siagro (`/taxLocked`) e emissão própria
   * (`/nfeMode`); devolução de compra (`/nfeReturn`); nome da condição de pagamento. Zera antes de esperar:
   * um true velho não pode aparecer no documento de outra filial.
   */
  protected async refreshNfeMode(): Promise<void> {
    const uiModel = this.getModel("ui") as JSONModel;
    uiModel.setProperty("/taxLocked", false);
    uiModel.setProperty("/nfeMode", false);
    uiModel.setProperty("/nfeReturn", false);

    const oContext = this.getView().getBindingContext() as Context;
    if (!oContext) {
      return;
    }

    const branchCode = await oContext.requestProperty("BranchCode") as string;
    const issuerType = await oContext.requestProperty("IssuerType") as string;
    const isNfeReturn = await oContext.requestProperty("IsNfeReturn") === true;
    const paymentConditionCode = await oContext.requestProperty("PaymentConditionCode") as number;
    const taxLocked = await this.isTaxCalculationActive(branchCode);

    uiModel.setProperty("/taxLocked", taxLocked);
    uiModel.setProperty("/nfeMode", isPurchaseNfeMode(taxLocked, issuerType));
    uiModel.setProperty("/nfeReturn", isNfeReturn);
    await this.refreshPaymentConditionName(paymentConditionCode);
  }

  /** Trocar a filial ou a emissão muda o modo NF-e (a natureza e os tributos passam a valer, ou deixam). */
  onBranchChange() {
    void this.refreshNfeMode();
  }

  onIssuerTypeChange() {
    void this.refreshNfeMode();
  }

  /**
   * Natureza da LINHA da entrada própria: só naturezas de Entrada ativas ($filter estático do enum). O Input
   * mostra o nome (`UsageName`) e quem vale para o servidor é o `UsageCode`; os tributos aparecem depois de
   * salvar (o cálculo é do servidor). setProperty sem await: o grupo é diferido.
   */
  async openPurchaseUsageValueHelp(ev: Input$ValueHelpRequestEvent) {
    const oInput = ev.getSource();
    const oTarget = oInput.getBindingContext() as Context;

    const oSelected = await DialogHelper.openTableSelectDialog(
      this, "UsagesSelectDialog", ["Name", "Description"],
      [new Filter("Inactive", FilterOperator.EQ, false)], undefined, "Direction eq 'Incoming'");

    if (!oSelected) {
      return;
    }

    oInput.setValue(oSelected.getProperty("Name") as string);
    void oTarget.setProperty("UsageCode", oSelected.getProperty("Code"));
  }

  /** Tributos calculados do item selecionado (somente leitura). */
  async onOpenItemFiscal() {
    const oTable = this.byId("tablePurchaseInvoiceItems") as Table;
    const i = oTable.getSelectedIndex();

    if (i < 0) {
      MessageBox.alert("Selecione um item.");
      return;
    }

    const oItemContext = oTable.getContextByIndex(i) as Context;
    this.itemFiscalDialog ??= await DialogHelper.createDialog(
      this, "siagrob1.view.purchaseInvoices.fragments.ItemFiscalDialog", oItemContext);
    this.itemFiscalDialog.setBindingContext(oItemContext);
    this.itemFiscalDialog.open();
  }

  onCloseItemFiscal() {
    this.itemFiscalDialog?.close();
  }
```

(imports: `Dialog` de `sap/m/Dialog`, `isPurchaseNfeMode` de `siagrob1/helpers/PurchaseInvoiceNfeHelpers`; `DialogHelper.createDialog` com a mesma assinatura que a tela de saída usa — confira em `salesInvoices/BaseController.ts:162`.)

- [ ] **Step 2: Add e Edit**

`Add.controller.ts`:
- `newRouteMatched` passa a `async`: depois de `uiModel.setData({})`, `uiModel.setProperty("/paymentConditionName", "")`; `const branchInfo = await this.getBranchInfo();` e passe `branchInfo?.code` ao `createDraft`; depois de criar o contexto, `await this.refreshNfeMode();`. Mantenha o fluxo do `onImportXml` (o rascunho do XML também recebe a filial da sessão).
- `createDraft(draft?, xmlContent?, branchCode?)`: no `oBinding.create({...})` acrescente `BranchCode: branchCode ?? null`, `PaymentConditionCode: null`, `ReferencedAccessKey: null`; em cada linha (inclusive `onAddItem`), `UsageCode: null, UsageName: null` (sem eles a primeira escolha de natureza abre "Must not change a property before it has been read"). A interface `InvoiceItemPayload` ganha `UsageCode: number; UsageName: string;` (strictNullChecks off: aceitam null).
- `onSave`: com `ui>/nfeMode`, antes do aviso de contrato, recuse linha sem natureza:

```ts
    const nfeMode = (this.getModel("ui") as JSONModel).getProperty("/nfeMode") === true;
    if (nfeMode) {
      const withoutUsage = (oBinding?.getAllCurrentContexts() ?? []).filter(ctx => !ctx.getProperty("UsageCode"));
      if (withoutUsage.length > 0) {
        MessageBox.warning("Informe a natureza de operação de todos os itens: a NF-e de entrada é calculada por ela.");
        return;
      }
    }
```

`Edit.controller.ts`: no `bindElement`, troque a lista de `$select` das linhas por `` `Items($select=${PURCHASE_ITEM_SELECT};$expand=SalesInvoiceItem($expand=SalesInvoice),PurchaseContract($select=Key,Code))` `` e, depois do bind, `void this.refreshNfeMode();` (mantendo o `dataReceived` do total). `onAddItem` ganha `UsageCode: null, UsageName: null`. `onSave` ganha a mesma recusa de linha sem natureza do Add.

- [ ] **Step 3: Formulário**

Em `fragments/Form.fragment.xml`:
- **Emissão** (`Select` de `IssuerType`): acrescente `change=".onIssuerTypeChange"`.
- Depois do par de Selects, a **Filial**:

```xml
          <!-- Filial do documento: decide se a emissão própria sai com NF-e pelo Siagro (modo NF-e). Antes nunca
               era gravada na entrada. -->
          <Label text="Filial" />
          <Select
            selectedKey="{BranchCode}"
            editable="{= ${ui>/editable} === true &amp;&amp; ${IsNfeReturn} !== true }"
            forceSelection="false"
            change=".onBranchChange"
            items="{ path: '/Branchs', sorter: { path: 'Code' } }"
            width="100%">
            <core:Item key="{Code}" text="{Code} - {BranchName}" />
          </Select>
```

  (confira os nomes `Code`/`BranchName` no `Select` de filial do `salesInvoices/fragments/Form.fragment.xml` e use os mesmos.)
- **Emitente**: `editable` e `showValueHelp` passam a `{= ${ui>/editable} === true &amp;&amp; ${IsNfeReturn} !== true }`.
- **Número da NF**, **Série**, **Chave da NF-e**: `editable="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeMode} !== true }"` (no modo NF-e vêm da emissão).
- Depois de "Valor declarado", os campos do modo NF-e:

```xml
          <!-- Modo NF-e (filial que emite pelo Siagro + emissão própria). A condição vira cobr/dup e pag; padrão do
               fornecedor. A devolução de compra não tem pagamento (tPag 90). -->
          <Label text="Condição de pagamento"
                 visible="{= ${ui>/nfeMode} === true &amp;&amp; ${ui>/nfeReturn} !== true }" />
          <Input
            visible="{= ${ui>/nfeMode} === true &amp;&amp; ${ui>/nfeReturn} !== true }"
            editable="{ui>/editable}"
            showValueHelp="true"
            valueHelpOnly="true"
            valueHelpRequest=".openPaymentConditionsValueHelp"
            value="{PaymentConditionCode}" />
          <Input value="{ui>/paymentConditionName}" editable="false"
                 visible="{= ${ui>/nfeMode} === true &amp;&amp; ${ui>/nfeReturn} !== true }" />

          <!-- Nota do produtor (ou outra NF-e) que a entrada própria referencia no NFref. 44 dígitos; o servidor
               confere o dígito verificador. -->
          <Label text="NF-e referenciada"
                 visible="{= ${ui>/nfeMode} === true &amp;&amp; ${ui>/nfeReturn} !== true }" />
          <Input
            value="{ReferencedAccessKey}"
            maxLength="54"
            placeholder="Chave de 44 dígitos (ex.: NF-e do produtor)"
            editable="{ui>/editable}"
            visible="{= ${ui>/nfeMode} === true &amp;&amp; ${ui>/nfeReturn} !== true }" />
```

- O `TextArea` de `TaxPayerComments`: rótulo e edição pelo modo:

```xml
          <Label text="{= ${ui>/nfeMode} === true ? 'Informações complementares (vão na NF-e)' : 'Referências informadas pelo emitente' }" />
          <TextArea
              value="{TaxPayerComments}"
              editable="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeMode} === true }"
              rows="8"
              growing="true"
              width="100%" />
```

  (ajuste o comentário XML acima dele: na emissão própria é o `infCpl` que o operador escreve, ex.: o texto do Funrural.)

- [ ] **Step 4: Itens**

Em `fragments/Items.fragment.xml`:
- Toolbar (`t:extension`): antes do `ToolbarSpacer`, `<Button text="Tributos do item" icon="sap-icon://simulate" press=".onOpenItemFiscal" visible="{ui>/nfeMode}" />`.
- **Produto**, **UM** e **Preço Unitário**: `editable`/`showValueHelp` passam a `{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true }` (na devolução de compra só a quantidade muda).
- Depois de **Descrição**, duas colunas do modo NF-e:

```xml
      <!-- Natureza da linha (modo NF-e): só naturezas de Entrada. Na devolução de compra vem da natureza de
           devolução da linha comprada e não muda. -->
      <t:Column label="Natureza" width="14rem" visible="{ui>/nfeMode}">
        <t:template>
          <Input
            value="{UsageName}"
            editable="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true }"
            showValueHelp="{= ${ui>/editable} === true &amp;&amp; ${ui>/nfeReturn} !== true }"
            valueHelpOnly="true"
            valueHelpRequest=".openPurchaseUsageValueHelp" />
        </t:template>
      </t:Column>
      <t:Column label="CFOP" width="6rem" visible="{ui>/nfeMode}">
        <t:template>
          <Text text="{Cfop}" wrapping="false" />
        </t:template>
      </t:Column>
```

- **Contrato**: `visible` passa a `{= ${path: 'InvoiceType', targetType: 'any'} === 'Normal' }` (inalterado) — e as colunas da devolução do CLIENTE (**NF de Origem**, **Quebra Apurada**, **Diferença**) passam a `{= ${path: 'InvoiceType', targetType: 'any'} === 'Return' &amp;&amp; ${IsNfeReturn} !== true }` (a devolução de compra não tem essa amarração).
- Os `Text` de célula que existirem continuam com `wrapping="false"`.

`Add.view.xml` e `Edit.view.xml`: os botões "Incluir Item"/"Excluir Item" ganham `enabled="{= ${ui>/nfeReturn} !== true }"`.

- [ ] **Step 5: Diálogo fiscal (somente leitura)**

`view/purchaseInvoices/fragments/ItemFiscalDialog.fragment.xml`:

```xml
<core:FragmentDefinition
	xmlns="sap.m"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form">
	<Dialog
		core:require="{ Decimal: 'sap/ui/model/odata/type/Decimal' }"
		title="Tributos do item {ItemCode}"
		contentWidth="48rem"
		class="sapUiSizeCompact">
		<content>
			<!-- A fotografia do cálculo pela natureza: travada, quem grava é o servidor ao salvar. -->
			<f:SimpleForm editable="false" layout="ResponsiveGridLayout" labelSpanL="5" labelSpanM="5" columnsL="2" columnsM="2">
				<f:content>
					<core:Title text="Operação" />
					<Label text="Natureza" />
					<Text text="{UsageName}" />
					<Label text="CFOP" />
					<Text text="{Cfop}" />
					<Label text="NCM" />
					<Text text="{Ncm}" />
					<Label text="Origem" />
					<Text text="{GoodsOrigin}" />
					<Label text="Nº do item na NF-e" />
					<Text text="{NfeItemNumber}" />

					<core:Title text="ICMS" />
					<Label text="CST/CSOSN" />
					<Text text="{CstIcms}" />
					<Label text="Base" />
					<Text text="{ path: 'IcmsBase', type: 'Decimal', constraints: { precision: 18, scale: 2 } }" />
					<Label text="Alíquota %" />
					<Text text="{ path: 'IcmsRate', type: 'Decimal', constraints: { precision: 7, scale: 4 } }" />
					<Label text="Redução da base %" />
					<Text text="{ path: 'IcmsBaseReduction', type: 'Decimal', constraints: { precision: 7, scale: 4 } }" />
					<Label text="ICMS da operação" />
					<Text text="{ path: 'IcmsOperationValue', type: 'Decimal', constraints: { precision: 18, scale: 2 } }" />
					<Label text="Diferimento %" />
					<Text text="{ path: 'IcmsDeferral', type: 'Decimal', constraints: { precision: 7, scale: 4 } }" />
					<Label text="ICMS diferido" />
					<Text text="{ path: 'IcmsDeferredValue', type: 'Decimal', constraints: { precision: 18, scale: 2 } }" />
					<Label text="ICMS" />
					<Text text="{ path: 'IcmsValue', type: 'Decimal', constraints: { precision: 18, scale: 2 } }" />
					<Label text="cBenef" />
					<Text text="{IcmsBenefitCode}" />

					<core:Title text="PIS / COFINS" />
					<Label text="CST PIS" />
					<Text text="{CstPis}" />
					<Label text="PIS" />
					<Text text="{ path: 'PisValue', type: 'Decimal', constraints: { precision: 18, scale: 2 } }" />
					<Label text="CST COFINS" />
					<Text text="{CstCofins}" />
					<Label text="COFINS" />
					<Text text="{ path: 'CofinsValue', type: 'Decimal', constraints: { precision: 18, scale: 2 } }" />

					<core:Title text="IBS / CBS" />
					<Label text="CST / cClassTrib" />
					<Text text="{IbsCbsCst} {IbsCbsClassCode}" />
					<Label text="Base" />
					<Text text="{ path: 'IbsCbsBase', type: 'Decimal', constraints: { precision: 18, scale: 2 } }" />
					<Label text="CBS" />
					<Text text="{ path: 'CbsValue', type: 'Decimal', constraints: { precision: 18, scale: 2 } }" />
					<Label text="IBS estadual" />
					<Text text="{ path: 'IbsStateValue', type: 'Decimal', constraints: { precision: 18, scale: 2 } }" />
					<Label text="IBS municipal" />
					<Text text="{ path: 'IbsMunicipalValue', type: 'Decimal', constraints: { precision: 18, scale: 2 } }" />
				</f:content>
			</f:SimpleForm>
		</content>
		<endButton>
			<Button text="Fechar" press=".onCloseItemFiscal" />
		</endButton>
	</Dialog>
</core:FragmentDefinition>
```

⚠️ `{IbsCbsCst} {IbsCbsClassCode}` é binding composto: se o OData v4 recusar (memória: "um campo por propriedade"), troque por dois `Text` em `HBox`, como a lista de naturezas faz.

- [ ] **Step 6: Gates e commit (frontend)**

`yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (≤ 920), QUnit (como na Task 12). Mensagem: `feat(invoice): documento de entrada com filial, natureza e tributos no modo NF-e`.

---

### Task 14: Frontend — detalhe e lista: emitir, consultar, DANFE, XML e Devolver

Spec: §11 (detalhe e lista), §9.2.

**Files:**
- Modify: `siagro-b1-frontend/webapp/view/purchaseInvoices/Detail.view.xml`, `controller/purchaseInvoices/Detail.controller.ts`
- Create: `siagro-b1-frontend/webapp/view/purchaseInvoices/fragments/NfeReturnDialog.fragment.xml`
- Modify: `siagro-b1-frontend/webapp/view/purchaseInvoices/Main.view.xml`, `controller/purchaseInvoices/Main.controller.ts`

**Interfaces:**
- Consumes: `canIssuePurchaseNfe`, `canReturnPurchase`, `PURCHASE_ITEM_SELECT`, rotas da Task 12; `refreshNfeMode` (Task 13); `nfeOutcomeMessage`, `prefillNfeReturnRows`, `hasReturnableBalance`, `buildNfeReturnPayload`; `sendJson`, `odataValue`, `readErrorMessage` (`siagrob1/helpers/FetchHelpers`).
- Produces: handlers `onIssueNfe`, `onConsultNfe`, `onCompleteNfeConfirmation`, `onDanfe`, `onNfeXml`, `onNfeReturn`, `onCloseNfeReturn`, `onConfirmNfeReturn` no Detail da entrada; `ui>/canIssueNfe`, `ui>/canReturn`.

- [ ] **Step 1: Controller do detalhe**

Em `Detail.controller.ts`:
- `bindElement`: `$select` das linhas = `PURCHASE_ITEM_SELECT` (mesma expressão do Edit, Task 13).
- Depois do bind e a cada releitura: `void this.refreshDetailNfe();` com

```ts
  /** Modo NF-e e os dois botões que dependem do estado do documento (regras puras em PurchaseInvoiceNfeHelpers). */
  private async refreshDetailNfe() {
    await this.refreshNfeMode();
    const uiModel = this.getModel("ui") as JSONModel;
    const oContext = this.getView().getBindingContext() as Context;
    const state = oContext ? await oContext.requestObject() as PurchaseNfeState : undefined;
    const nfeMode = uiModel.getProperty("/nfeMode") === true;

    uiModel.setProperty("/canIssueNfe", !!state && canIssuePurchaseNfe(state, nfeMode));
    uiModel.setProperty("/canReturn", !!state && canReturnPurchase(state, nfeMode));
  }
```

  e, no `detailRouteMatched`, zere `/canIssueNfe` e `/canReturn` antes do bind.
- Ações de NF-e — copie de `salesInvoices/Detail.controller.ts` `onIssueNfe`, `onConsultNfe`, `onCompleteNfeConfirmation`, `runNfeAction`, `onDanfe`, `onNfeXml` trocando as rotas por `ServerRoutes.purchaseInvoicesIssueNfe`, `purchaseInvoicesConsultNfe`, `purchaseInvoicesCompleteNfeConfirmation`, `` `${ServerRoutes.purchaseInvoicesDanfeReport}/${key}/print` `` e `` `${ServerRoutes.purchaseInvoicesNfeXml}(Key=${key})` ``; no `finally` do `runNfeAction`, depois do `requestRefresh`, `void this.refreshDetailNfe();`.
- "Devolver" — copie `onNfeReturn`, `onCloseNfeReturn`, `onConfirmNfeReturn` da saída, trocando: a rota dos itens por `ServerRoutes.purchaseInvoicesNfeReturnableItems`, o tipo das linhas por `PurchaseReturnRow` (abaixo), a mensagem sem saldo por `"Esta entrada não tem saldo a devolver."`, o fragmento por `siagrob1.view.purchaseInvoices.fragments.NfeReturnDialog`, a action por `ServerRoutes.purchaseInvoicesCreateNfeReturn` e a navegação por `this.navTo("purchaseInvoicesDetail", { id: key })`. Tipo local:

```ts
/** Linha do "Devolver" da entrada: o comprado no lugar do vendido. */
type PurchaseReturnRow = ReturnableRow & { ItemName: string; PurchasedQuantity: number; ReturnedQuantity: number };
```

- [ ] **Step 2: Diálogo "Devolver" da entrada**

`fragments/NfeReturnDialog.fragment.xml` — cópia de `view/salesInvoices/fragments/NfeReturnDialog.fragment.xml` com: título `"Devolver Documento de Entrada"`, coluna `"Comprado"` lendo `nfeReturn>PurchasedQuantity`, placeholder do motivo `"Ex.: grão fora do padrão contratado"`. Mantenha o `core:require` do `Float` e o resto igual.

- [ ] **Step 3: View do detalhe**

Em `Detail.view.xml`:
- Nova seção, antes de "Itens":

```xml
      <uxap:ObjectPageSection titleUppercase="false" title="NF-e" visible="{ui>/nfeMode}">
        <uxap:subSections>
          <uxap:ObjectPageSubSection title=" " titleUppercase="false">
            <core:Fragment fragmentName="siagrob1.view.salesInvoices.fragments.NfePanel" type="XML" />
          </uxap:ObjectPageSubSection>
        </uxap:subSections>
      </uxap:ObjectPageSection>
```

  (o painel da saída lê os mesmos nomes de campo; reaproveitado de propósito.)
- Título: `text="{= ${IsNfeReturn} === true ? 'Devolução de Compra ' + (${TaxDocumentNumber} || '') : 'Documento de Entrada ' + (${TaxDocumentNumber} || '') }"` nos dois `Title`.
- Rodapé, antes de "Editar":

```xml
        <Button text="Emitir NF-e" type="Emphasized" icon="sap-icon://paper-plane" visible="{ui>/canIssueNfe}" press=".onIssueNfe" />
        <Button text="Consultar situação" icon="sap-icon://synchronize"
                visible="{= ${ui>/nfeMode} === true &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} === 'Processing' }"
                press=".onConsultNfe" />
        <Button text="Concluir confirmação"
                visible="{= ${ui>/nfeMode} === true &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} === 'Authorized' &amp;&amp; ${path: 'InvoiceStatus', targetType: 'any'} === 'Pending' }"
                press=".onCompleteNfeConfirmation" />
        <Button text="DANFE" icon="sap-icon://pdf-attachment"
                visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Authorized' }" press=".onDanfe" />
        <Button text="XML" icon="sap-icon://download"
                visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Authorized' }" press=".onNfeXml" />
        <Button text="Devolver" icon="sap-icon://undo" visible="{ui>/canReturn}" press=".onNfeReturn" />
```

- "Editar": `visible="{= ${path: 'InvoiceStatus', targetType: 'any'} === 'Pending' &amp;&amp; ${path: 'NfeStatus', targetType: 'any'} !== 'Processing' }"`.
- "Confirmar": `visible="{= ${path: 'InvoiceStatus', targetType: 'any'} === 'Pending' &amp;&amp; ${ui>/nfeMode} !== true }"` (no modo NF-e quem confirma é a emissão).

- [ ] **Step 4: Lista**

`Main.controller.ts` (no route matched): `void this.refreshStandaloneFlag().then(() => this.refreshAnyBranchIssuesNfe());` (herdado do `CommonController`, Task 12). `Main.view.xml`: coluna depois de "Situação":

```xml
          <t:Column label="Situação NF-e" width="9rem" visible="{= ${ui>/standalone} === true &amp;&amp; ${ui>/anyBranchIssuesNfe} === true }">
            <t:template>
              <ObjectStatus
                inverted="true"
                text="{ path: 'NfeStatus', targetType: 'any', formatter: '.formatter.formatNfeStatus' }"
                state="{ path: 'NfeStatus', targetType: 'any', formatter: '.formatter.stateNfeStatus' }" />
            </t:template>
          </t:Column>
```

  (se a lista usa `$select` explícito, acrescente `NfeStatus`, `IssuerType`, `IsNfeReturn`.)

- [ ] **Step 5: Gates e commit (frontend)**

`yarn ts-typecheck`, `yarn lint`, `npx ui5lint` (≤ 920), QUnit. Mensagem: `feat(invoice): emissão, consulta, DANFE e devolução de compra na tela do documento de entrada`.

---

### Task 15: Verificação ponta a ponta e memória (controlador, não subagente)

Spec: §13 (na tela).

- [ ] **Step 1: Subir a stack `ceagui`** — Web, Gateway e Reports (perfis `ceagui`) e `npx ui5 serve --port 8080` se o do usuário não estiver de pé; login admin/1234; conferir no SQL que a migration `AddPurchaseInvoiceNfe` está no `CEAGUI_SIAGRO_DEV`.
- [ ] **Step 2: Cadastros pela tela** — natureza "DEVOLUCAO DE COMPRA" (Saída, 5202/6202, ICMS 51 18% diferimento 100 cBenef SP053521, PIS/COFINS 49, IBS/CBS como a de compra) e natureza "COMPRA DE MERCADORIA" (Entrada, 1102/2102, ICMS 51 + cBenef, PIS/COFINS 74, IBS/CBS 200/200036) apontando a primeira em "Natureza de devolução"; produto 1 TRIGO com NCM e origem; fornecedor de teste FICTÍCIO (CPF válido gerado, não contribuinte, endereço em Itaberá/SP).
- [ ] **Step 3: Entrada própria** — Documentos de Entrada → Incluir, Emissão Própria, filial 01, fornecedor de teste, natureza na linha, condição de pagamento, NF-e referenciada (opcional), salvar; conferir CFOP 1102 e o diálogo "Tributos do item"; "Emitir NF-e" em homologação. Se o ambiente barrar a emissão, parar aqui, registrar e seguir com o XML validado no XSD pelos testes.
- [ ] **Step 4: DANFE e XML** da entrada autorizada; conferir `tpNF 0`, `finNFe 1`, CFOP 1102 no XML.
- [ ] **Step 5: Devolver** parcial no detalhe da entrada autorizada; conferir a devolução Pendente (5202, quantidade, pesos proporcionais); emitir em homologação; conferir `tpNF 1`, `finNFe 4`, `DFeReferenciado` com o `nItem` da entrada, `tPag 90`.
- [ ] **Step 6: Isolamento** — documento de terceiro continua como antes (sem natureza/CFOP, "Confirmar" visível).
- [ ] **Step 7: Memória** — criar `nfe-purchase-invoice-issuance-feature.md` no diretório de memória com estado, decisões não óbvias e pendências; índice em `MEMORY.md`.
