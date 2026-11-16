# GAC-1226 — Vincular NF do SAP à carga — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Em `Erp=SAPB1`, gerar o documento de saída da carga a partir de uma A/R Invoice do SAP escolhida pelo usuário, e refletir no Siagro o cancelamento feito no SAP.

**Architecture:** Leitura direta do SAP por EF (`SapErpDbContext`, novas entidades OINV/INV1/INV12) e do add-on fiscal por um provedor plugável (`ISapFiscalDocumentSource`, InvOne). O vínculo monta um `SalesInvoice` e reusa o orquestrador `ShipmentBillingCreateSalesInvoiceService` com um parâmetro `sapLink`, sem duplicar efeitos. Um job Hangfire cancela o documento quando o SAP cancela.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server), OData, Hangfire, xUnit + EF InMemory; OpenUI5 + TypeScript (OData v4).

**Spec:** `docs/superpowers/specs/2026-11-16-gac-1226-sap-invoice-link-design.md`

## Global Constraints

- Tudo novo só age com `Erp=SAPB1`; em STANDALONE o único efeito é o botão "Informar Nota Fiscal" oculto.
- Nada é gravado no SAP nem no `DBInvOne`. Consultas a esses bancos passam datas **como parâmetro**.
- Sem navigation property EF de entidade local para entidade SAP; sem join entre bancos.
- Identificadores em inglês; mensagens e rótulos em pt-BR, exatamente como no spec.
- Tolerância de quantidade: `0.001`. Número da NF com 9 dígitos (`D9`), série com 3.
- `NfeStatus` do documento vinculado permanece `None`.
- Branch `feature/gac-1226-sap-invoice-link` nos dois repositórios; conferir a branch antes de cada commit; `git add` em todo arquivo novo; nunca push. Commit no formato do `CLAUDE.md` de cada repo, com `Refs: GAC-1226` e `DB: AddSalesInvoiceSapLink` no commit da migration.
- Serviços novos são registrados à mão em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`; os que dependem do SAP, só no ramo SAPB1 de `Program.cs`.
- Rodar testes: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`.

## Review Focus

1. Duplo clique / duas abas vinculando a mesma NF → a segunda falha com a mensagem "já está vinculada", nunca com erro cru do índice único. (Task 6)
2. SAP ou `DBInvOne` fora do ar na listagem → mensagem de negócio clara, sem 500 genérico; no job → loga e segue para a próxima empresa. (Tasks 4, 8)
3. Documento vinculado estornado para Pendente e a invoice cancelada no SAP nesse meio-tempo → reconfirmar recusa; o job trata o Pendente. (Tasks 7, 8)
4. Frontend antigo em cache mandando `TaxDocumentKind = Nfe` no Faturar em SAPB1 → servidor grava `Other` + `WithoutTaxDocument`. (Task 5)
5. Invoice com `Serial` nulo, mais de uma linha, incoterm vazio ou filial sem correspondência → fora da lista; no vínculo direto, recusa com mensagem. (Tasks 4, 6)

---

### Task 0: Confirmar os fatos do SAP — CONCLUÍDA em 16/11/2026

Resultados na seção "Confirmado em produção" do spec. Valem para as tarefas seguintes: filial =
`BPLId.ToString() == Branch.Code`; `Process.CompanyId == BPLId`; autorizado = `StatusId == 4`;
cancelado no add-on = `StatusId == 10 || IsCanceled == true`; incoterm `0/1/2/9` →
`Cif/Fob/Ter/None`; `DeliveryCardCode` = `DeliveryLocationCode` da liberação; `ShipToCode` não é
mapeado; frete = `ShipmentLoad.FreightPrice`.

### Task 1: Modo SAPB1, campos do vínculo e migration

**Files:**
- Modify: `SiagroB1.Infra/ErpMode.cs`, `SiagroB1.Domain/Entities/SalesInvoice.cs`, `SiagroB1.Infra/Context/AppDbContext.cs`, `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs`
- Create: migration `AddSalesInvoiceSapLink` em `SiagroB1.Migrations`
- Test: `SiagroB1.Application.Tests/Infra/ErpModeTests.cs`, `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceSapLinkLockTests.cs`

**Interfaces:**
- Produces: `ErpMode.IsSapB1(IConfiguration?) : bool`; propriedades `SalesInvoice.SapDocEntry (int?)`, `SapDocNum (int?)`, `SapCancelPending (bool)`, `SapCancelPendingReason (string?, 500)`, `SapCancelDetectedAt (DateTime?)`; `SalesInvoice.IsSapLinked => SapDocEntry != null` (`[NotMapped]`, não entra no EDM).

- [ ] **Step 1: Testes que falham** — `IsSapB1` verdadeiro só para `"SAPB1"` (qualquer caixa/espaços), falso para nulo/`STANDALONE`; `ResetIssuanceFields` zera os cinco campos; `RestoreIssuanceFields` restaura os cinco a partir do valor original num PATCH.
- [ ] **Step 2:** Rodar e ver falhar.
- [ ] **Step 3:** Implementar. Índice: `HasIndex(SapDocEntry).IsUnique().HasFilter("[SapDocEntry] IS NOT NULL AND [InvoiceStatus] <> 2 AND [InvoiceStatus] <> 3")` (conferir no enum que 2 = Cancelled e 3 = Returned; usar os valores reais).
- [ ] **Step 4:** Gerar a migration (`dotnet ef migrations add AddSalesInvoiceSapLink --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`) e conferir que só contém as cinco colunas e o índice.
- [ ] **Step 5:** Testes verdes; `dotnet build SiagroB1.sln`.
- [ ] **Step 6:** Commit `feat(invoice): adiciona os campos de vínculo com a invoice do SAP` com `DB: AddSalesInvoiceSapLink`.

### Task 2: Entidades do SAP e provedor do add-on fiscal

**Files:**
- Create: `SiagroB1.Domain/Entities/SAP/SapInvoice.cs`, `SapInvoiceLine.cs`, `SapInvoiceTransport.cs`; `SiagroB1.Domain/Interfaces/ISapFiscalDocumentSource.cs`; `SiagroB1.Infra/Context/InvOneDbContext.cs`; `SiagroB1.Domain/Entities/SAP/InvOneProcess.cs`; `SiagroB1.Application/Services/SAP/InvOneFiscalDocumentSource.cs`, `NullSapFiscalDocumentSource.cs`
- Modify: `SiagroB1.Infra/Context/SapErpDbContext.cs`, `SiagroB1.Web/Program.cs` (ramo SAPB1), `SiagroB1.Web/appsettings.Yokotobi-*.json`
- Test: `SiagroB1.Application.Tests/Sap/InvOneFiscalDocumentSourceTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public record SapFiscalDocumentInfo(int DocEntry, string? AccessKey, bool Authorized, bool Cancelled, string? Protocol);
  public interface ISapFiscalDocumentSource
  {
      bool IsAvailable { get; }   // false no Null
      Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(IReadOnlyCollection<int> docEntries, CancellationToken ct = default);
  }
  ```
- `SapInvoice` (tabela `OINV`, chave `DocEntry`): `DocNum`, `DocDate`, `CardCode`, `CardName`, `BPLId`, `Canceled` (coluna `CANCELED`), `Serial`, `SeriesStr`, `Model`; coleção `Lines`; referência `Transport`. `SapInvoiceLine` (`INV1`, chave `DocEntry`+`LineNum`): `ItemCode`, `Quantity`, `Price`, `LineTotal`, `UnitMsr`, `Usage`, `CfopCode`. `SapInvoiceTransport` (`INV12`, chave `DocEntry`): `Carrier`, `Incoterms`. Tipos conforme a Task 0.
- `InvOneProcess` (`Process`): `DocType`, `DocEntry`, `CompanyId`, `KeyNfe`, `StatusId`, `IsCanceled`, `ProtocolNumber`. Sem chave declarada no banco → `HasNoKey()`.
- Config: `ConnectionStrings:SapFiscalAddonDB`, `SAPB1:FiscalAddon` (`InvOne` | ausente). `Authorized` = `StatusId == 4`.

- [ ] **Step 1: Testes que falham** (InMemory `InvOneDbContext`): devolve chave/autorizado/cancelado só de `DocType == 13`; ignora `DocEntry` fora da lista; `KeyNfe` normalizada para 44 dígitos (remove não-dígitos; se não tiver 44, `AccessKey = null`); lista com mais de 1.000 `DocEntry` é consultada em lotes; `NullSapFiscalDocumentSource` devolve dicionário vazio e `IsAvailable == false`.
- [ ] **Step 2–4:** Ver falhar, implementar, ver passar.
- [ ] **Step 5:** Registro em `Program.cs` (ramo SAPB1): `InvOneDbContext` + `InvOneFiscalDocumentSource` quando `SAPB1:FiscalAddon == "InvOne"` e a connection string existir; senão `NullSapFiscalDocumentSource`. Em STANDALONE, `Null`. Nos appsettings da Yokotobi, usar a mesma credencial do `SapDB` trocando o banco para `DBInvOne`.
- [ ] **Step 6:** Commit `feat(sap): mapeia a A/R Invoice do SAP e o provedor fiscal InvOne`.

### Task 3: Compartilhar a trava de duplicidade da NF e o cálculo do saldo da carga

Nenhuma trava é removida nem afrouxada: o código sai de dentro de um serviço para uma classe usada pelos dois caminhos (informar NF e vincular NF do SAP), com as mesmas mensagens. Os testes existentes dessas travas precisam continuar verdes sem alteração.

**Files:**
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceTaxDocumentDuplicateGuard.cs`
- Modify: `SalesInvoicesSetDocumentNumberService.cs`, `ShipmentLoads/ShipmentLoadsBillingGuardService.cs`, `ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceTaxDocumentDuplicateGuardTests.cs`, `ShipmentLoadsBillingGuardServiceTests.cs` (acrescentar)

**Interfaces:**
- Produces:
  ```csharp
  public class SalesInvoiceTaxDocumentDuplicateGuard(AppDbContext context)
  {
      Task EnsureUniqueAsync(string branchCode, string? number, string? series, string? accessKey, Guid? ignoreInvoiceKey);
  }
  // ShipmentLoadsBillingGuardService
  public Task<decimal> CalculateAvailableAsync(Guid loadKey);
  ```
  Mesmas mensagens e mesmas exceções de hoje (mover, não reescrever). `EnsureCanBillAsync` passa a usar `CalculateAvailableAsync`.

- [ ] **Step 1:** Testes do guard novo (duplicado na filial recusa; outra filial passa; Cancelled e Returned são ignorados; chave duplicada global recusa) e de `CalculateAvailableAsync` (igual ao saldo que `EnsureCanBillAsync` usa em um cenário com fatura parcial).
- [ ] **Step 2–4:** Ver falhar, extrair, ver passar — incluindo os testes já existentes de `SalesInvoicesSetDocumentNumberService` e do guard da carga.
- [ ] **Step 5:** Commit `refactor(invoice): extrai a trava de duplicidade da NF e o saldo da carga`.

### Task 4: Listagem das invoices vinculáveis

**Files:**
- Create: `SiagroB1.Application/Services/SAP/SapLinkableInvoicesService.cs`, `SiagroB1.Domain/DTOs/SapLinkableInvoiceDto.cs`, `SiagroB1.Web/Functions/ShipmentLoads/ShipmentLoadsGetLinkableSapInvoicesController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `Program.cs` (ramo SAPB1)
- Test: `SiagroB1.Application.Tests/Sap/SapLinkableInvoicesServiceTests.cs`

**Interfaces:**
- Consumes: `ShipmentLoadsBillingGuardService.CalculateAvailableAsync`, `ISapFiscalDocumentSource`.
- Produces:
  ```csharp
  public class SapLinkableInvoicesService(AppDbContext context, SapErpDbContext sap, ISapFiscalDocumentSource fiscal, ShipmentLoadsBillingGuardService loadGuard)
  {
      Task<List<SapLinkableInvoiceDto>> ListAsync(Guid loadKey, DateTime dateFrom, DateTime dateTo, CancellationToken ct = default);
      Task<SapLinkableInvoiceDto?> FindAsync(Guid loadKey, int docEntry, CancellationToken ct = default); // mesmas regras, um documento, sem filtro de período
  }
  ```
  `SapLinkableInvoiceDto`: `DocEntry`, `DocNum`, `DocDate`, `CardCode`, `CardName`, `TaxDocumentNumber` (D9), `TaxDocumentSeries` (3), `Quantity`, `UnitPrice`, `LineTotal`, `Cfop`, `CarrierCode`, `CarrierMismatch` (bool), `FreightTerms`, `DeliveryCardCode`, `AccessKey`.
- Função OData `ShipmentLoadsGetLinkableSapInvoices(Key, DateFrom, DateTo)` devolvendo coleção do DTO (padrão de `SalesShipmentReleasesGetAvailable`). Fora de SAPB1: 400 "Disponível somente com integração SAP Business One." Limite de 200 linhas, mais recentes primeiro.

- [ ] **Step 1: Testes que falham** — um por regra da seção "Listagem" do spec: cancelada fora; modelo não NF-e fora; `Serial` nulo fora; 2 linhas fora; item diferente fora; quantidade ±0,001 entra e ±0,002 sai; fora do período sai; filial diferente sai; não autorizada sai quando `fiscal.IsAvailable`, entra quando não; já vinculada a documento Confirmado/Pendente sai; vinculada só a Cancelled/Returned entra; `CarrierMismatch` verdadeiro quando `INV12.Carrier` difere da carga; incoterm vazio/não mapeável sai. Mais um teste `ToQueryString()` das consultas ao SAP com provider SqlServer (connection string fictícia) para pegar LINQ intraduzível.
- [ ] **Step 2–4:** Ver falhar, implementar (exclusão das vinculadas em dois passos, lotes ≤ 1.000), ver passar.
- [ ] **Step 5:** Controller + EDM + registro; teste de EDM no estilo de `UserEdmModelTests`. Falha de conexão com o SAP vira `BusinessException` "Não foi possível consultar as notas fiscais no SAP. Tente novamente em instantes."
- [ ] **Step 6:** Commit `feat(sap): lista as invoices do SAP vinculáveis a uma carga`.

### Task 5: Orquestrador — vínculo SAP e Faturar manual "Sem NF"

**Files:**
- Modify: `SiagroB1.Application/Services/ShipmentBilling/ShipmentBillingCreateSalesInvoiceService.cs`
- Create: `SiagroB1.Application/Services/ShipmentBilling/SapInvoiceLink.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadBillingServiceTests.cs` (acrescentar)

**Interfaces:**
- Produces:
  ```csharp
  public record SapInvoiceLink(int DocEntry, int DocNum, string TaxDocumentNumber, string TaxDocumentSeries, string? AccessKey, string? Cfop);
  // construtor ganha o último parâmetro opcional: IConfiguration? configuration = null
  public Task ExecuteAsync(SalesInvoice salesInvoice, string username, SapInvoiceLink? sapLink = null);
  ```
- Regras: em SAPB1 e `sapLink == null` → `TaxDocumentKind = Other`, `WithoutTaxDocument = true`, número/série/chave nulos. Com `sapLink` → `TaxDocumentKind = Nfe`, `WithoutTaxDocument = false`, e **depois** de `salesInvoicesCreateService.ExecuteAsync` (que zera os campos) aplica `SapDocEntry`, `SapDocNum`, número, série, chave e `Cfop` da linha. O controller público nunca passa `sapLink`.

- [ ] **Step 1: Testes que falham** — SAPB1 sem vínculo com payload `Nfe` → grava `Other` + `WithoutTaxDocument`; SAPB1 com vínculo → `Nfe`, campos SAP e CFOP gravados, `NfeStatus == None`, alocação/título/carga como no faturamento comum; STANDALONE com payload `Nfe` → inalterado.
- [ ] **Step 2–4:** Ver falhar, implementar, ver passar (toda a classe de testes existente continua verde).
- [ ] **Step 5:** Commit `feat(shipment): fatura manual em SAPB1 nasce sem NF e aceita vínculo SAP`.

### Task 6: Ação "Vincular NF do SAP"

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsLinkSapInvoiceService.cs`, `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsLinkSapInvoiceController.cs`
- Modify: `ODataConfigurations.cs`, `Program.cs` (ramo SAPB1)
- Test: `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadsLinkSapInvoiceServiceTests.cs`

**Interfaces:**
- Consumes: `SapLinkableInvoicesService.FindAsync`, `SalesInvoiceTaxDocumentDuplicateGuard.EnsureUniqueAsync`, `ShipmentBillingCreateSalesInvoiceService.ExecuteAsync(..., sapLink)`.
- Produces: `Task<SalesInvoice> ExecuteAsync(Guid loadKey, int sapDocEntry, Guid salesShipmentReleaseKey, string username)`; ação OData `ShipmentLoadsLinkSapInvoice(Key, SapDocEntry, SalesShipmentReleaseKey)` → `{ key, invoiceNumber }`.
- Montagem do `SalesInvoice`: tabela "Campo | Origem" do spec. `FreightCostStandard` = frete informado na carga.
- Mensagens (exatas, do spec): já vinculada; quantidade diferente; e "O cliente da NF do SAP ({cardCode}) é diferente do cliente do contrato da liberação." / "A NF do SAP não está mais disponível para vínculo." (quando `FindAsync` devolve nulo).

- [ ] **Step 1: Testes que falham** — caminho feliz (documento Confirmado, campos e todos os efeitos); quantidade diferente; já vinculada (inclusive quando o índice único dispara `DbUpdateException` → mesma mensagem); cancelada no SAP entre listar e vincular; cliente do contrato divergente; liberação inativa ou de outro item; NF duplicada na filial; sem chave (provedor nulo) vincula com chave nula; fora de SAPB1 recusa.
- [ ] **Step 2–4:** Ver falhar, implementar, ver passar.
- [ ] **Step 5:** Controller + EDM + teste de EDM.
- [ ] **Step 6:** Commit `feat(shipment): vincula a NF do SAP à carga`.

### Task 7: Regras do documento vinculado

**Files:**
- Modify: `SalesInvoiceNfeLock.cs` (`EnsureManualTaxDocument`, `EnsureItemEditable`/`EnsureLinesChangeable` ou o serviço de update de itens), `SalesInvoicesConfirmService.cs`
- Test: `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceSapLinkedRulesTests.cs`

**Interfaces:**
- Consumes: `SalesInvoice.IsSapLinked`; `SapErpDbContext` (opcional no `SalesInvoicesConfirmService`, como dependência nula em STANDALONE).

- [ ] **Step 1: Testes que falham** — `SalesInvoicesSetDocumentNumber` em documento vinculado recusa com "Número, série e chave deste documento vêm da nota fiscal do SAP."; alterar quantidade/preço/item ou incluir/excluir linha em vinculado recusa com "Documento vinculado à nota fiscal do SAP: quantidade, preço e item não podem ser alterados."; conferência de entrega (`DeliveredQuantity`, `QuantityLoss`) continua permitida; estornar confirmação funciona e mantém `SapDocEntry`; excluir o Pendente funciona e a NF volta a aparecer em `ListAsync`; cancelar funciona e libera a NF; reconfirmar Pendente vinculado com a invoice cancelada no SAP recusa com "A NF do SAP vinculada foi cancelada. Exclua este documento.".
- [ ] **Step 2–4:** Ver falhar, implementar, ver passar.
- [ ] **Step 5:** Commit `feat(invoice): protege o documento vinculado à NF do SAP`.

### Task 8: Job `sap-invoice-sync`

**Files:**
- Create: `SiagroB1.Application/Services/SAP/SapInvoiceSyncService.cs`, `SiagroB1.Application/Jobs/SapInvoiceSyncJob.cs`
- Create: `SiagroB1.Domain/Entities/SapIntegrationSettings.cs` (+ DbSet, entity set OData, serviço get/update no padrão de `EmailSettings`), ação `SapInvoiceSyncRunNow`; migration de menu "Integração SAP" (seguir a última migration de menu existente; lembrar o script de menu da Yokotobi no deploy)
- Modify: `SiagroB1.Application/Jobs/Companies/CompanyScopedJobRunner.cs` (nova chave `SapInvoiceSync`), `Program.cs` (registro `* * * * *` no ramo SAPB1 com `RunForAllCompaniesWithoutRetryAsync`; `RemoveIfExists` no else), `SalesInvoicesCancelService.cs` (limpa `SapCancelPending*` ao cancelar)
- Test: `SiagroB1.Application.Tests/Sap/SapInvoiceSyncServiceTests.cs`

**Interfaces:**
- Produces: `SapInvoiceSyncService.ExecuteAsync(CancellationToken)`; usuário do cancelamento: `"sap-sync"`.
- `SapIntegrationSettings`: `InvoiceSyncEnabled (bool, padrão true)`, `InvoiceSyncIntervalMinutes (int, padrão 15, 1..1440)`, `InvoiceSyncLastRunAt (DateTime?)`. A tabela entra na migration `AddSalesInvoiceSapLink` da Task 1 se ainda não commitada; senão, migration própria `AddSapIntegrationSettings` com trailer `DB:`.
- `ExecuteAsync(ct, bool force = false)`: sem `force`, sai sem fazer nada quando desligado ou quando `now - InvoiceSyncLastRunAt < InvoiceSyncIntervalMinutes`; grava `InvoiceSyncLastRunAt` ao terminar. "Sincronizar agora" chama com `force: true`. Relógio injetável (`TimeProvider`) para o teste.
- Cada documento é processado em escopo de DI próprio (`IServiceScopeFactory`): um cancelamento que falha deixa o contexto sujo.

- [ ] **Step 1: Testes que falham** — intervalo: desligado não executa; antes de vencer o intervalo não executa; vencido executa e grava a última execução; `force` executa sempre; intervalo fora de 1..1440 é recusado no update com "Informe um intervalo entre 1 e 1440 minutos."; sem linha de configuração usa os padrões. Sincronização: invoice `CANCELED='Y'` → documento Confirmado fica Cancelled e a NF fica livre; cancelada só no add-on (`Cancelled == true`) → idem; trava do cancelamento (ex.: título baixado) → documento intacto, `SapCancelPending = true`, motivo = mensagem da trava, `SapCancelDetectedAt` preenchido; rodada seguinte com a trava resolvida → cancela e limpa; documento vinculado Pendente com invoice cancelada → excluído/cancelado e NF livre; documento vinculado sem chave e provedor com chave → chave preenchida; exceção ao consultar o SAP → não propaga; cancelar à mão um documento com pendência limpa os campos.
- [ ] **Step 2–4:** Ver falhar, implementar, ver passar.
- [ ] **Step 5:** Commit `feat(sap): cancela o documento de saída quando a invoice é cancelada no SAP`.

### Task 9: Frontend — Faturar "Sem NF" em SAPB1 e botão oculto

Repositório `siagro-b1-frontend`, branch `feature/gac-1226-sap-invoice-link` (criar a partir de `main`).

**Files:**
- Modify: `webapp/helpers/ShipmentBillingDialog.ts`, `webapp/view/shipmentBilling/Billing.fragment.xml`, `webapp/view/salesInvoices/Main.view.xml`, `webapp/controller/salesInvoices/Main.controller.ts`
- Test: QUnit ao lado dos helpers existentes, se a lógica nova for extraída em função pura

- [ ] **Step 1:** `ShipmentBillingDialog.open()`: quando `SessionService.isSapMode()`, `billing>/TaxDocumentKind = "Other"`, `billing>/SapMode = true`; o Select de tipo fica oculto e aparece um `MessageStrip` informativo: "Com integração SAP, o Faturar gera documento sem NF-e. Para nota fiscal use Vincular NF do SAP."
- [ ] **Step 2:** Remover da view o botão "Informar Nota Fiscal" (`Main.view.xml:62`), o handler `onNotaFiscal`, os handlers do diálogo e o fragment `NotaFiscalDialog.fragment.xml` se não tiverem outro uso (conferir com Grep antes de apagar). Ajustar o `$select` que existia só para o diálogo.
- [ ] **Step 2b:** Tela "Integração SAP" (`webapp/view/sapIntegrationSettings`, controller, rota e target no `manifest.json`), copiando a estrutura de `emailSettings`: Switch "Sincronizar cancelamentos do SAP", StepInput "Intervalo (minutos)" 1–1440, texto "Última execução", botão "Sincronizar agora". Depende da Task 8.
- [ ] **Step 3:** Colunas "Doc. SAP" (`SapDocNum`) e `ObjectStatus` "Canc. SAP pendente" (estado Error, tooltip `SapCancelPendingReason`, visível com `${path:'SapCancelPending', targetType:'any'} === true`) na lista; visíveis só em SAPB1 por uma flag `ui>/sapMode` iniciada `false`.
- [ ] **Step 4:** `yarn ts-typecheck && yarn lint && yarn test`.
- [ ] **Step 5:** Commit (formato do `CLAUDE.md` do frontend, `Refs: GAC-1226`).

### Task 10: Frontend — diálogo "Vincular NF do SAP"

**Files:**
- Create: `webapp/helpers/SapInvoiceLinkDialog.ts`, `webapp/view/shipmentLoads/fragments/SapInvoiceLinkDialog.fragment.xml`
- Modify: `webapp/view/shipmentLoads/Detail.view.xml`, `webapp/controller/shipmentLoads/Detail.controller.ts`, `webapp/model/ServerRoutes.ts` (se as rotas forem REST) 

**Interfaces:**
- Consumes: função `ShipmentLoadsGetLinkableSapInvoices(Key, DateFrom, DateTo)`, ação `ShipmentLoadsLinkSapInvoice`, `SalesShipmentReleasesGetAvailable` (mesma chamada do diálogo de faturar).

- [ ] **Step 1:** Botão "Vincular NF do SAP" em `uxap:actions`, mesma expressão de `visible` do "Faturar" mais `${ui>/sapInvoiceLinkVisible} === true`; flag iniciada `false` no `onInit`/route-matched e ligada com `erp === "SAPB1"` (padrão de `applyPreContractPrintVisibility`).
- [ ] **Step 2:** Diálogo: período (padrão últimos 15 dias) + "Buscar"; tabela de seleção única com Data, NF/Série, Cliente, Quantidade, Preço, Total, Transportadora (com ícone de aviso quando `CarrierMismatch`); abaixo, a liberação — lista de `SalesShipmentReleasesGetAvailable` filtrada pelo `CardCode` da invoice escolhida, pré-selecionada quando houver só uma. Texto vazio: "Nenhuma nota fiscal do SAP com quantidade igual ao saldo da carga neste período."
- [ ] **Step 3:** "Vincular": trava de reentrância setada antes do primeiro `await` (padrão `_billingInFlight`), chama a ação, fecha, atualiza o binding `Invoices` e navega para `salesInvoicesDetail`. Erros do servidor em `MessageBox.error`.
- [ ] **Step 4:** Na tabela de documentos da carga: coluna "Doc. SAP" e o mesmo `ObjectStatus` de pendência; incluir os campos no `$select`.
- [ ] **Step 5:** `yarn ts-typecheck && yarn lint && yarn test`.
- [ ] **Step 6:** Commit.

### Task 11: Verificação ponta a ponta

- [ ] **Step 1:** Aplicar a migration numa **cópia** do banco Siagro da Yokotobi (nunca no banco compartilhado local) e subir a stack no perfil `yktb` apontando para a cópia; SAP e InvOne só são lidos.
- [ ] **Step 2:** Pelo navegador: numa carga de teste com saldo igual ao de uma NF real, abrir "Vincular NF do SAP", vincular, e conferir no documento de saída número/série/chave/CFOP/frete/local de entrega; conferir alocação do contrato, saldo da liberação, título firme e status da carga.
- [ ] **Step 3:** Estornar a confirmação, excluir, e vincular de novo (o caso "NF errada").
- [ ] **Step 4:** "Faturar" manual na mesma instalação → documento tipo Outros, "Sem NF".
- [ ] **Step 5:** Cancelamento: disparar o job pelo Hangfire contra um documento vinculado a uma invoice já cancelada no SAP (vínculo feito por semente na cópia, informando ao usuário o que foi semeado) → documento cancelado; repetir com um título baixado → pendência visível.
- [ ] **Step 6:** Perfil `ceagui` (STANDALONE): lista de documentos de saída sem "Informar Nota Fiscal"; faturamento e emissão de NF-e inalterados.
- [ ] **Step 7:** Suíte completa: `dotnet test SiagroB1.Application.Tests` e `yarn test`. Relatar ao usuário o que foi verificado e o que não foi; deixar as duas pastas principais de volta em `main` ao final.
