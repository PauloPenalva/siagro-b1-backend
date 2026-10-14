# Títulos firmes de compra (conciliação, composição no provisório e dação) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O título a pagar FIRME de compra nasce da Conciliação Fiscal (NF × romaneio) — ou da confirmação de um documento de entrada "Complemento de valor" (D11) —, com o preço e o Funrural da linha do documento de entrada; o operador o compõe no provisório da fixação (único título que recebe baixa em dinheiro, e só até o composto), baixa a sobra por dação, e a devolução compensa o firme por encontro de contas — com travas no estorno de romaneio, de fixação, no washout, no cancelamento e no encerramento. O sub-projeto 1 (natureza no contrato) é desfeito.

**Architecture:** Três peças de dados novas — `FINANCIAL_DOCUMENT_COMPOSITIONS` (composição com valor assinado), colunas de origem fiscal em `FINANCIAL_DOCUMENTS` e `RelatedDocumentKey`/`CompositionKey` no ledger. O recalculador continua escritor único de `SettledAmount`/`Status`, passa a escrever `ComposedAmount` e aprende a enxergar linhas ainda não salvas (Added/Deleted no ChangeTracker) — isso deixa composição, desfazer, encontro de contas e o sync do firme todos *enqueue-only*, num `SaveChanges` só. A regra pura "este vínculo pode sumir? cancele o firme solto" fica num estático em `SiagroB1.Infra/Queries` (o interceptor da conciliação não enxerga a Application); criação/recálculo do firme ficam em `FinancialDocumentsFirmSyncService`, chamado por `PurchaseFiscalReconciliationService`.

**Tech Stack:** .NET 10, EF Core (SQL Server; InMemory nos testes), ASP.NET Core OData v4, xUnit; OpenUI5 1.141 + TypeScript, QUnit.

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-13-purchase-firm-payables-design.md` (autoridade; regras numeradas 1–30, "Reversão do sub-projeto 1" e "Decisões tomadas na revisão do plano" D10–D11, commit `4d85962`).

## Global Constraints

- Dois repositórios: `C:\Projetos\SiagroB1\siagro-b1-backend` (Tasks 1, 3–14) no branch **`feature/purchase-firm-payables`** (já existe) e `C:\Projetos\SiagroB1\siagro-b1-frontend` (Tasks 2, 15–21) no branch **`feature/purchase-firm-payables`**, criado a partir de `main` na Task 2. `git branch --show-current` antes de cada `git add`. **Nunca push. Nunca merge.**
- Todo arquivo novo: `git add <path>` logo depois de criado, no repo dono. O último passo de cada task é **stage** (`git add`) — o **controlador** commita com a mensagem dada (o `git commit` de subagente é bloqueado nesta sessão).
- Mensagem de commit: `tipo(escopo): descrição pt-BR, imperativo, minúscula, sem ponto`; escopo `financial` (ou `purchase-contract` na reversão); corpo com o PORQUÊ; trailer `DB: <Migration>[, <Migration>]` **sempre** que o commit tiver migration; terminar com
  `Co-Authored-By: {MODELO} <noreply@anthropic.com>` e
  `Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT`,
  onde `{MODELO}` é o nome exato do modelo que de fato escreveu o código da task (ex.: `Claude Opus 5.5 (1M context)`).
- Identificadores em inglês; texto ao usuário (mensagens, rótulos) em pt-BR; comentários em pt-BR.
- Enums: valores novos **no fim**, nunca renumerar — `FinancialDocumentNature.PurchaseReturn = 4`, `FinancialDocumentOrigin.PurchaseFiscalReconciliation = 7`, `FinancialSettlementOrigin.Dation = 6`, `PurchaseInvoiceType.ValueComplement = 2` (D11). `FinancialDocumentOrigin.PurchaseInvoice = 3` já existe e passa a ser escrito (firme do complemento de valor).
- D10: "gera financeiro" é lido da **natureza** da linha (`IUsage.GetByIdAsync(line.UsageCode)` → `CreatesFinancialDocument`) no momento em que o firme nasce; em SAPB1 (`ErpMode.IsStandalone == false`; o OUSG não tem a flag) conta como **gera**; linha sem `UsageCode` não gera. `PurchaseInvoiceItem.CreatesFinancialDocument` **não** é lido.
- Dinheiro: `decimal.Round(x, 2, MidpointRounding.ToEven)`. Valores em mensagens: `ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))`.
- "Firme de compra" (`[NotMapped] IsPurchaseFirmPayable`, Task 3) = `Nature ∈ {Firm, PurchaseReturn}` com `OriginType == PurchaseFiscalReconciliation` (nasce do vínculo) **ou** `Nature == Firm` com `OriginType == PurchaseInvoice` (nasce da confirmação do documento "Complemento de valor", D11). O título a receber do washout também é `Nature = Firm` (origem `PurchaseContractWashout`) e **continua** baixável em dinheiro — nenhuma regra deste plano o alcança.
- Migrations: **só** `dotnet ef migrations add` (não toca banco). **Nunca** `dotnet ef database update` sem `ASPNETCORE_ENVIRONMENT=Ceagui-Development` explícito e conferido (CEAGUI dev: `CEAGUI_SIAGRO_DEV` / `CEAGUI_SIAGRO_COMMON_DEV`); nunca o profile `db-migration`. Migrations antigas já aplicadas **não** são reescritas.
- Índice filtrado novo: aplicar só por `dotnet ef` (por `sqlcmd` exige `-I`, senão erro 1934 de QUOTED_IDENTIFIER).
- Processos (Web/Gateway/Reports/dev server): matar **só por PID** (`Get-NetTCPConnection -LocalPort <p> -State Listen` → `Stop-Process -Id <pid> -Force`). Nunca `taskkill /IM node.exe`/`dotnet.exe`.
- Armadilhas conhecidas (valem para todas as tasks):
  - `--` dentro de comentário XML mata o fragmento inteiro sem erro em gate nenhum — não escrever `--` em `<!-- -->`.
  - Fragmento usa `core:require` **no próprio fragmento** para o `formatter` (não herda o da view).
  - Enum no binding: `targetType: 'any'`; filtro de enum: `$filter` cru em string (`sap.ui.model.Filter` sobre enum estoura "Unsupported type").
  - `Edm.Decimal` chega como **string** no modelo V4 e no JSON das functions: sempre `Number(x)` antes de comparar/somar.
  - Parâmetro de dinheiro em action: `Edm.Double` (`Parameter<double>`), nunca `Edm.Decimal`; data como string ISO `yyyy-MM-dd`.
  - Handler de erro do frontend aceita as três formas (`string`, `.message`, `.error.message`) — usar `sendJson` de `helpers/FetchHelpers.ts` (ele já usa `readErrorMessage`).
  - `ODataActionParameters` chega **nulo** quando o corpo não casa com o EDM: guardar `parameters is null` antes de tudo; parâmetro opcional presente pode vir `null` (`value?.ToString()`).
  - DTO de function precisa de `[JsonPropertyName("PascalCase")]` em toda propriedade (sem isso sai camelCase).
  - InMemory **não** faz cascade de entidade não rastreada e não impõe FK/índice único: remoções são explícitas e os testes conferem o efeito, não só a ausência de exceção.
  - `DocNumberSequenceService` usa conexão Dapper própria com `UPDLOCK`: buscar número **antes** do `SaveChanges`, **fora** de transação longa (nos testes: `FakeDocNumberSequenceService`).
- Mensagens EXATAS (copiar literalmente; `{x}`/`{y}` = valor `N2` pt-BR; códigos = `Code` do título/romaneio):
  - **Do spec:**
    - S1 `O título {código} do vínculo já está composto no título {provisório}: desfaça a composição antes de alterar a conciliação.`
    - S2 (motivo de cancelamento) `Vínculo fiscal desfeito`
    - S3 `O título {provisório} já está integralmente composto.`
    - S4 `Estorne a baixa do título {provisório} antes de desfazer a composição.`
    - S5 `Abatimento de composição se desfaz pela composição.`
    - S6 `Valor acima do disponível para baixa ({x}): componha mais títulos firmes antes.`
    - S7 `Título firme não recebe baixa direta: componha no provisório ou baixe por dação.`
    - S8 `O romaneio {código} compõe o título {provisório}, que já tem baixa ou compensação: estorne a baixa antes.`
    - S9 `Desfaça a composição do título {provisório} antes de estornar a fixação.`
    - S10 `Há títulos firmes com saldo neste contrato: componha, compense ou baixe por dação antes de encerrar.`
    - S11 `O título {provisório} não está quitado: o contrato só encerra com todos os títulos quitados. Entrega a menos se resolve por washout; faturamento abaixo, por documento de complemento de valor.`
  - **Decididas neste plano (o spec não deu texto):**
    - P1 `O título {código} do vínculo já tem baixa por dação ou encontro de contas: estorne-a antes de alterar a conciliação.`
    - P2 `A devolução {código} deixaria o composto do título {provisório} abaixo do valor já baixado: estorne a baixa antes.`
    - P3 `O título {código} não é provisório.` · `O título {código} está cancelado.` · `Selecione ao menos um título firme ou de devolução.` · `O título {código} não é firme nem devolução da conciliação fiscal.` · `O título {código} é de outro contrato.` · `O título {código} não tem saldo a compor.`
    - P4 `Dação só se aplica a título firme da conciliação fiscal.` · `Informe o motivo da dação.` · `O valor da dação deve ser maior que zero.` · `O valor da dação ({x}) excede o saldo em aberto do título ({y}).`
    - P5 `Compensar com firme só vale para título de devolução.` · `O título {código} não é um título firme da conciliação fiscal.` · `O título {código} não tem saldo a compensar.` · `O título {código} está composto no título {provisório}: só firme solto compensa a devolução.` (mais `O título {código} é de outro contrato.` e `O título {código} está cancelado.` de P3)
    - P6 `Lançamento par do encontro de contas não encontrado.`
    - P7 `O washout reduziria o título {código} para {x}, abaixo do valor já composto ({y}): desfaça a composição antes.` · `O washout consumiria toda a fixação, mas o título {código} tem composição ou baixa: desfaça a composição e estorne as baixas antes.`
    - P8 `O título {código} tem composição ou baixa: desfaça a composição e estorne as baixas antes de cancelar o contrato ou retirar a aprovação.`
    - P9 `Título firme nasce e morre com o vínculo da Conciliação Fiscal de Compras: desfaça o vínculo para cancelá-lo.` · `O documento {código} tem composição. Desfaça a composição antes de cancelar.`
    - P10 `Composição não encontrada.` · `Abatimento da composição não encontrado.`
    - P11 (D11, linha do documento) `Item {produto}: valor de complemento só existe no documento do tipo Complemento de valor.` · `Complemento de valor é só escrituração por enquanto: lance-o como documento de terceiro. A emissão de NF-e de complemento vem depois.` · `Item {produto}: no complemento de valor a linha não tem quantidade nem preço unitário; informe só o valor do complemento.` · `Item {produto}: informe o valor do complemento.` · `Item {produto}: o complemento de valor exige o contrato de compra na linha.` · `Item {produto}: o complemento de valor exige a natureza de operação.` · `Item {produto}: a natureza {código} não gera financeiro; o complemento de valor precisa de uma natureza que gere.`
    - P12 (D11, cancelar/estornar o documento) `O título {código} do complemento de valor já está composto no título {provisório}: desfaça a composição antes de cancelar ou estornar o documento.` · `O título {código} do complemento de valor já tem baixa por dação ou encontro de contas: estorne-a antes de cancelar ou estornar o documento.`
    - P13 (D11, conciliação e NF-e) `O documento {NF} é complemento de valor: não se concilia com romaneio; o título nasce na confirmação.` · `Complemento de valor não é emitido como NF-e por aqui (fica para o sub-projeto 2b): lance-o como documento de terceiro.`
    - Nota automática no ledger ao desfazer composição: `Composição desfeita`.
- Comandos de teste (raiz do backend): `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`. Frontend (raiz, dev server **parado**): `yarn ts-typecheck && yarn lint`; QUnit pelo navegador com `yarn start` em `http://localhost:8080/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests` (o `yarn test` inteiro falha no gate de cobertura — não é regressão).

## Review Focus

1. **"Cancelar título" manual num firme da conciliação** — espera-se que o botão nem apareça e que o backend recuse: cancelar à mão deixaria o vínculo vivo sem título, e a próxima gravação do vínculo criaria outro. Recusa P9 em `FinancialDocumentsCancelService.ExecuteAsync`, teste `Manual_cancel_of_a_conciliation_firm_is_refused` (Task 5); botão oculto pelo formatter (Task 15).
2. **"Cancelar título" manual num provisório com composição e sem baixa** — o guard antigo só olha `SettledAmount`; cancelar deixaria linhas de composição órfãs abatendo firmes. Recusa P9 (2ª frase), teste `Manual_cancel_of_a_provisional_with_composition_is_refused` (Task 10).
3. **Duplo clique em "Compor" / mesmo título duas vezes na lista** — espera-se uma composição só. Backend aplica `Distinct()` às chaves (teste `Repeated_key_is_composed_once`, Task 6); a tela trava reentrância com `_actionRunning` (Task 16).
4. **Regravar o mesmo vínculo sem mudar a quantidade** (o diálogo de conciliação reenvia pares inalterados) — não pode duplicar firme nem logar `NetAmount`, e não pode ser recusado mesmo com o firme composto. Testes `Saving_the_same_link_again_keeps_one_firm_and_no_log` (Task 5) e `Resaving_a_composed_link_with_same_quantity_is_accepted` (Task 6).
5. **Cancelar o romaneio ou a NF por fora (sem passar pelo estorno da alocação) com o firme composto** — o interceptor tem de recusar com S1, não apagar o vínculo deixando abatimento órfão. Teste `Cancelling_the_transaction_of_a_composed_firm_is_refused_by_the_interceptor` (Task 6).

## Decisões do plano (lacunas do spec)

- **D10 (decisão do usuário):** "gera financeiro" vem da natureza da linha, lida no nascimento do firme; SAPB1 = gera (decidido pelo modo ERP, não pelo valor: o `UsageModel` do OUSG traz `false`, não nulo). Linha sem natureza não gera.
- **D11 (decisão do usuário):** documento de entrada "Complemento de valor" (`PurchaseInvoiceType.ValueComplement = 2`). Linha só-valor = coluna nova `PurchaseInvoiceItem.ComplementAmount DECIMAL(18,2) DEFAULT 0`, com `Quantity = UnitPrice = 0`; `Total = Round(Quantity × UnitPrice, 2) + ComplementAmount` (o vProd da linha), então `GrandTotal`, tributos, Funrural, totais e relatórios a enxergam sem caso especial. Só documento de **terceiro**; o firme nasce na **confirmação**, um por linha (`OriginType = PurchaseInvoice`, `OriginKey` = linha); cancelamento **e estorno da confirmação** do documento cancelam o firme solto ou recusam (P12) — pelo `PurchaseFiscalReconciliationCleanupInterceptor`, para valer em todos os caminhos (cancelamento comum, pós-NF-e, estorno). Reconfirmar gera firme novo (o índice `FirmOrigin` já cobre `OriginType`+`OriginKey`, sem mudança).
- No terceiro, "Complemento de valor" segue as mesmas regras do Normal onde fazem sentido: cálculo de tributos/Funrural pela natureza, valor declarado = soma das linhas e conferência da chave da NF-e do fornecedor; relatório de Funrural retido passa a incluí-lo (filtro `!= Return`).
- Regras de firme valem só para firme da conciliação ou do complemento de valor (`IsPurchaseFirmPayable`): o título a receber do washout também é `Firm` e segue baixável e fora das travas.
- Mensagens sem texto no spec: P1–P10 (Global Constraints). Cancelamento em cascata pelo interceptor grava `CanceledBy = "Sistema"`.
- Recalculador passa a somar linhas pendentes do ChangeTracker, para compor/desfazer/compensar/sync num `SaveChanges` só (sem salvar no meio de fluxos `Deferred`).
- Regra 24: estorno de alocação de romaneio 9/16 não toca vínculo nem título. Regra 23: só vínculos cujas linhas são do contrato da alocação.
- Cancelar título à mão: recusado para firme da conciliação e para provisório com composição (spec silente; ver Review Focus 1–2).
- "Rotas nas duas formas": nenhuma operação nova tem parâmetro opcional, então uma rota literal por operação (o par de rotas do projeto existe para parâmetro opcional omitido).
- Compensação automática só enxerga firmes de origem já gravados (origem e devolução conciliadas no mesmo pedido → o operador compensa à mão).
- Encerrar exige provisório `Settled` exato (regra 28): entrega a menos por washout, faturamento abaixo (inclusive centavos) por complemento de valor (D11) — S11 cita os dois.
- Washout passa a recalcular o status do provisório depois de ajustar o valor (necessário para D8 liberar o encerramento).

---

## File Structure

Backend (`siagro-b1-backend`):

| Arquivo | Responsabilidade |
|---|---|
| Delete (Task 1) `PurchaseContractFiscalComplement*`, controllers, DTO, 5 testes; Restore 14 arquivos de `ab1e691^1` | reversão do sub-projeto 1 |
| Create migrations `AppContext/*_DropPurchaseContractFiscalComplement`, `CommonContext/*_RemovePurchaseContractFiscalEditPermission`, `AppContext/*_AddFinancialDocumentComposition` | banco |
| Modify `SiagroB1.Domain/Enums/FinancialDocumentNature.cs`, `FinancialDocumentOrigin.cs`, `FinancialSettlementOrigin.cs` | valores novos |
| Modify `SiagroB1.Domain/Entities/FinancialDocument.cs`, `FinancialSettlement.cs`, `FinancialDocumentChangeLogFields.cs` | colunas e derivados |
| Create `SiagroB1.Domain/Entities/FinancialDocumentComposition.cs` | linha de composição |
| Create `SiagroB1.Domain/Dtos/FinancialDocumentCompositionDto.cs` | DTO da function |
| Modify `SiagroB1.Domain/Dtos/FinancialDocumentByContractDto.cs`, `FiscalReconciliationDtos.cs` | campos novos |
| Modify `SiagroB1.Infra/Context/AppDbContext.cs` (~131, ~476, ~553) | DbSet, FKs, índice FirmOrigin |
| Create `SiagroB1.Infra/Queries/PurchaseFirmPayableRules.cs` | regra pura do firme (Infra) |
| Modify `SiagroB1.Infra/Interceptors/PurchaseFiscalReconciliationCleanupInterceptor.cs`, `SiagroB1.Infra/Queries/PurchaseFiscalReconciliationQuery.cs` | cancelamento/recusa; coluna do firme |
| Create `SiagroB1.Application/Services/Financials/FinancialDocumentsFirmSyncService.cs`, `…ComposeService.cs`, `…UncomposeService.cs`, `…DationService.cs`, `…NettingService.cs`, `…GetCompositionService.cs`, `FinancialDocumentsFirmUnwind.cs` | regras 1–23 |
| Modify `…RecalculateBalanceService.cs`, `…SettlementGuardService.cs`, `…ReverseSettlementService.cs`, `…CancelService.cs`, `…AdjustProvisionalService.cs`, `…GetByContractService.cs` | regras 13–16, 21, 26, 27 |
| Modify `PurchaseFiscalReconciliationService.cs`, `PurchaseContractsAllocationDeleteService.cs`, `PurchaseContractsPriceFixationsCancelService.cs`, `PurchaseContractsCloseService.cs` | ganchos |
| Create `SiagroB1.Web/Actions/Financials/FinancialDocuments{Compose,Uncompose,Dation,Netting}Controller.cs`, `SiagroB1.Web/Functions/Financials/FinancialDocumentsGetCompositionController.cs` | endpoints |
| Modify `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (~1559, ~1661), `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (~666), `SiagroB1.Web/Actions/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationActionsController.cs` | EDM, DI, usuário no Remove |
| Modify (D11, Tasks 13–14) `SiagroB1.Domain/Enums/PurchaseInvoiceType.cs`, `SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs`; Create `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceValueComplement.cs`; Modify os 4 serviços de gravação, `PurchaseInvoicesTaxApplyService`, `PurchaseInvoiceDeclaredTotal`, `SupplierNfeKeyGuard`, `PurchaseInvoicesConfirmService`, `PurchaseInvoicesNfeIssueService`, 2 relatórios; migration `AddPurchaseInvoiceItemComplementAmount` | complemento de valor |
| Tests `SiagroB1.Application.Tests/Financials/FirmPayableSeed.cs` + 10 classes novas; `PurchaseInvoices/PurchaseInvoiceValueComplementTests.cs`; `Support/FinancialDocumentTestServices.cs`, `Support/TaxTestServices.cs`; `PurchaseFiscalReconciliations/FiscalReconciliationSeed.cs` | |

Frontend (`siagro-b1-frontend/webapp`):

| Arquivo | Responsabilidade |
|---|---|
| Delete/Restore (Task 2) fragmentos e helper do sub-projeto 1 | reversão |
| Modify `model/ServerRoutes.ts`, `model/formatter.ts`, `test/unit/model/formatter.qunit.ts` | rotas e rótulos |
| Create `types/FinancialComposition.ts`, `types/FinancialDocumentByContract.ts` | tipos dos DTOs |
| Create `helpers/FinancialDocumentFilterHelpers.ts` + `test/unit/helpers/FinancialDocumentFilterHelpers.qunit.ts` | filtro "Com saldo a resolver" |
| Modify `controller/financialDocuments/Detail.controller.ts`, `view/financialDocuments/Detail.view.xml`, `fragments/Form.fragment.xml`, `fragments/Settlements.fragment.xml` | Detail do título |
| Create `view/financialDocuments/fragments/Composition.fragment.xml`, `ComposeDialog.fragment.xml`, `DationDialog.fragment.xml`, `NettingDialog.fragment.xml` | composição, dação, compensação |
| Modify `controller/financialDocuments/FinancialDocumentsBaseController.ts`, `fragments/Filterbar.fragment.xml`, `view/accountsPayable/Main.view.xml` | lista Contas a Pagar |
| Create `view/purchaseContracts/fragments/PurchaseContractFinancialDocuments.fragment.xml`; Modify `controller/purchaseContracts/Detail.controller.ts`, `view/purchaseContracts/Detail.view.xml` | seção Financeiro |
| Modify `types/PurchaseFiscalReconciliation.ts`, `view/purchaseFiscalReconciliations/fragments/LinksDialog.fragment.xml`, `controller/purchaseFiscalReconciliations/Main.controller.ts` | coluna do firme na conciliação |
| Modify (D11, Task 21) `helpers/InvoiceChargeTotalsHelpers.ts`, `helpers/PurchaseInvoiceDraftHelpers.ts`, `controller/purchaseInvoices/{BaseController,Main.controller}.ts`, `view/purchaseInvoices/fragments/{Form,Filterbar,Items}.fragment.xml` | Entrada do tipo Complemento de valor |

---

### Task 1: Reversão do sub-projeto 1 no backend + migrations de remoção

Repositório: `C:\Projetos\SiagroB1\siagro-b1-backend`, branch `feature/purchase-firm-payables`.

**Files:**
- Restore (do commit `ab1e691^1`, pai do merge do sub-projeto 1): `SiagroB1.Application/Interfaces/IUserPermissions.cs`, `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsApprovalService.cs`, `…/PurchaseContractsDeleteService.cs`, `…/PurchaseContractsTotalsService.cs`, `SiagroB1.Domain/Dtos/PurchaseContractTotalsResponseDto.cs`, `SiagroB1.Domain/Entities/ContractChangeLogFields.cs`, `SiagroB1.Domain/Entities/PurchaseContract.cs`, `SiagroB1.Infra/Context/AppDbContext.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Application.Tests/Financials/FinancialDocumentHooksTests.cs`, `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractNotificationEventTests.cs`, `…/PurchaseContractsTotalsServiceTests.cs`, `…/Washouts/WashoutBalanceConsumersTests.cs`
- Delete: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractFiscalComplementService.cs`, `SiagroB1.Domain/Dtos/PurchaseContractFiscalComplementDto.cs`, `SiagroB1.Domain/Entities/PurchaseContractFiscalComplement.cs`, `SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsSetFiscalComplementController.cs`, `SiagroB1.Web/Functions/PurchaseContracts/PurchaseContractsGetFiscalComplementController.cs`, `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplement{EdmModel,Lifecycle,Model,Service}Tests.cs`, `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsApprovalFiscalGuardTests.cs`
- Modify: `SiagroB1.Domain/Entities/PurchaseContract.cs` (comentário do `FunruralType`)
- Create: migrations `DropPurchaseContractFiscalComplement` (AppDbContext) e `RemovePurchaseContractFiscalEditPermission` (CommonDbContext)
- Test: `SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementRemovalTests.cs`

**Interfaces:**
- Produces: `PurchaseContractsApprovalService(AppDbContext, ContractNotificationOutboxService, FinancialDocumentsGenerateService)` (3 args de novo); `PurchaseContractsTotalsService(AppDbContext)`; `PurchaseContractTotalsResponseDto` sem os 9 campos de Funrural; `AppDbContext` sem `PurchaseContractFiscalComplements`; `PermissionCodes` sem `PurchaseContractFiscalEdit`; `ContractChangeLogFields` sem `Usage`. Nenhuma task posterior consome nada do sub-projeto 1.

- [ ] **Step 1: Conferir que os arquivos não mudaram depois do merge**

Run:
```bash
git diff --stat ab1e691 HEAD -- SiagroB1.Application SiagroB1.Domain SiagroB1.Infra SiagroB1.Web SiagroB1.Application.Tests SiagroB1.Migrations
```
Expected: saída vazia (depois do merge só entraram documentos). Se aparecer arquivo, **parar** e reportar: o restore abaixo sobrescreveria trabalho alheio.

- [ ] **Step 2: Teste que falha**

`SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementRemovalTests.cs`:

```csharp
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// Spec 2026-10-13 (títulos firmes), "Reversão do sub-projeto 1": a natureza no contrato saiu — o Funrural e a flag
/// "gera financeiro" vêm da linha do documento de entrada conciliado. A grade de impostos digitada NÃO volta a ser
/// editável em STANDALONE, mas o TotalTax volta a ser a soma dela (é o que o SAPB1 sempre usou).
/// </summary>
public class PurchaseContractFiscalComplementRemovalTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    [Theory]
    [InlineData("PurchaseContractsGetFiscalComplement")]
    [InlineData("PurchaseContractsSetFiscalComplement")]
    public void The_fiscal_complement_operations_left_the_edm(string operation)
    {
        Assert.DoesNotContain(BuildModel().SchemaElements.OfType<IEdmOperation>(), o => o.Name == operation);
    }

    [Fact]
    public void The_fiscal_complement_table_left_the_context()
    {
        Assert.Null(typeof(AppDbContext).GetProperty("PurchaseContractFiscalComplements"));
    }

    [Fact]
    public async Task Total_tax_is_again_the_sum_of_the_typed_grid()
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-001", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01", TotalVolume = 1234.567m,
        };
        pc.PriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(), FixationVolume = 1234.567m, FixationPrice = 2.345678m, Status = PriceFixationStatus.Confirmed,
        });
        _db.Context.Taxes.Add(new Tax { Code = "ICMS", Name = "ICMS", Rate = 12m });
        pc.Taxes.Add(new PurchaseContractTax { TaxCode = "ICMS" });
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();

        var totals = await new PurchaseContractsTotalsService(_db.Context).GetTotals(pc.Key);

        Assert.Equal(2895.90m, totals.TotalPrice);
        Assert.Equal(decimal.Round(2895.90m / 100 * 12m, 2, MidpointRounding.ToEven), totals.TotalTax);
    }
}
```

`git add SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementRemovalTests.cs`

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractFiscalComplementRemovalTests"`
Expected: erro de compilação — `PurchaseContractsTotalsService` não tem construtor de 1 argumento.

- [ ] **Step 4: Restaurar os 14 arquivos e apagar os do sub-projeto 1**

```bash
git checkout ab1e691^1 -- \
  SiagroB1.Application/Interfaces/IUserPermissions.cs \
  SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsApprovalService.cs \
  SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsDeleteService.cs \
  SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsTotalsService.cs \
  SiagroB1.Domain/Dtos/PurchaseContractTotalsResponseDto.cs \
  SiagroB1.Domain/Entities/ContractChangeLogFields.cs \
  SiagroB1.Domain/Entities/PurchaseContract.cs \
  SiagroB1.Infra/Context/AppDbContext.cs \
  SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs \
  SiagroB1.Web/ODataConfig/ODataConfigurations.cs \
  SiagroB1.Application.Tests/Financials/FinancialDocumentHooksTests.cs \
  SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractNotificationEventTests.cs \
  SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsTotalsServiceTests.cs \
  SiagroB1.Application.Tests/PurchaseContracts/Washouts/WashoutBalanceConsumersTests.cs

git rm \
  SiagroB1.Application/Services/PurchaseContracts/PurchaseContractFiscalComplementService.cs \
  SiagroB1.Domain/Dtos/PurchaseContractFiscalComplementDto.cs \
  SiagroB1.Domain/Entities/PurchaseContractFiscalComplement.cs \
  SiagroB1.Web/Actions/PurchaseContracts/PurchaseContractsSetFiscalComplementController.cs \
  SiagroB1.Web/Functions/PurchaseContracts/PurchaseContractsGetFiscalComplementController.cs \
  SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementEdmModelTests.cs \
  SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementLifecycleTests.cs \
  SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementModelTests.cs \
  SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractFiscalComplementServiceTests.cs \
  SiagroB1.Application.Tests/PurchaseContracts/PurchaseContractsApprovalFiscalGuardTests.cs
```

**Não** restaurar nada em `SiagroB1.Migrations` (as migrations antigas ficam; o snapshot é atualizado pelas migrations novas).

- [ ] **Step 5: Comentário do `FunruralType`**

Em `SiagroB1.Domain/Entities/PurchaseContract.cs`, logo acima de `public FunruralType? FunruralType { get; set; } = Enums.FunruralType.Bruto;`:

```csharp
    /// <summary>
    /// Só COMERCIAL: diz ao produtor como o preço foi negociado e sai na impressão. Não entra em cálculo nenhum —
    /// o Funrural do pagamento vem da LINHA do documento de entrada conciliado (<c>PurchaseInvoiceItem.Funrural*</c>),
    /// pela natureza de operação daquela linha (spec 2026-10-13, títulos firmes). Não "conecte" este campo a nada.
    /// </summary>
```

- [ ] **Step 6: Rodar o teste novo e as classes que os arquivos restaurados tocam**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseContractFiscalComplementRemovalTests|FullyQualifiedName~PurchaseContractsTotalsServiceTests|FullyQualifiedName~WashoutBalanceConsumersTests|FullyQualifiedName~FinancialDocumentHooksTests|FullyQualifiedName~PurchaseContractNotificationEventTests"`
Expected: todos PASS. (Se `2895.90m` não bater com o `TotalPrice` real, trocar a constante pelo valor que o assert mostrar — a regra testada é "TotalTax = 12% da base", não a constante.)

- [ ] **Step 7: Migration que derruba a tabela**

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add DropPurchaseContractFiscalComplement --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Conferir no `Up` gerado: **só** `DropTable(name: "PURCHASE_CONTRACT_FISCAL_COMPLEMENTS")`; no `Down`, o `CreateTable` com a FK cascade para `PURCHASE_CONTRACTS`. Qualquer outra operação = snapshot sujo: parar e investigar. `git add` dos 3 arquivos (migration, Designer, snapshot).

- [ ] **Step 8: Migration que remove a permissão**

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add RemovePurchaseContractFiscalEditPermission --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Substituir `Up`/`Down` do arquivo gerado por:

```csharp
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente: DELETE de linha inexistente não falha (a permissão pode nunca ter sido semeada aqui).
            migrationBuilder.Sql("DELETE FROM ROLE_PERMISSIONS WHERE PermissionCode = 'PURCHASE_CONTRACT_FISCAL_EDIT';");
            migrationBuilder.Sql("DELETE FROM PERMISSIONS WHERE Code = 'PURCHASE_CONTRACT_FISCAL_EDIT';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PURCHASE_CONTRACT_FISCAL_EDIT')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('PURCHASE_CONTRACT_FISCAL_EDIT', 'Editar o complemento fiscal do contrato de compra');
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                   AND NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'ADMIN' AND PermissionCode = 'PURCHASE_CONTRACT_FISCAL_EDIT')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode)
                    VALUES (NEWID(), 'ADMIN', 'PURCHASE_CONTRACT_FISCAL_EDIT');
                """);
        }
```
O snapshot do `CommonDbContext` **não** pode mudar. `git add` da migration e do Designer.

- [ ] **Step 9: Gates**

```bash
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef migrations has-pending-model-changes --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
grep -rn "PurchaseContractFiscalComplement\|PurchaseContractFiscalEdit\|NetToProducer\|ContractChangeLogFields.Usage" --include=*.cs SiagroB1.Application SiagroB1.Domain SiagroB1.Infra SiagroB1.Web SiagroB1.Application.Tests
```
Expected: build OK; todos os testes verdes (anotar a contagem); os dois "No changes have been made to the model since the last migration."; o `grep` só encontra `PurchaseContractFiscalComplementRemovalTests` (nome da classe nova) e nada mais.

- [ ] **Step 10: Stage**

```bash
git add -A SiagroB1.Application SiagroB1.Domain SiagroB1.Infra SiagroB1.Web SiagroB1.Application.Tests SiagroB1.Migrations
git status --short
```
O controlador commita com:

```
refactor(purchase-contract): desfaz a natureza de operação no contrato de compra

O Funrural e a flag "gera financeiro" passam a vir da natureza da linha do documento de entrada
conciliado (spec 2026-10-13, títulos firmes): a natureza no contrato perdeu a razão de existir.
Saem a tabela de complemento, a permissão, a trava na aprovação e o totalizador; TotalTax volta a
ser a soma da grade digitada. As migrations antigas ficam (já aplicadas na CEAGUI dev).

DB: DropPurchaseContractFiscalComplement, RemovePurchaseContractFiscalEditPermission
Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 2: Reversão do sub-projeto 1 no frontend

Repositório: `C:\Projetos\SiagroB1\siagro-b1-frontend`. Antes de tudo: `git checkout main && git checkout -b feature/purchase-firm-payables`.

**Files:**
- Restore (do commit `663c950`, anterior ao sub-projeto 1): `webapp/model/ServerRoutes.ts`, `webapp/model/formatter.ts`, `webapp/types/PurchaseContractsTotal.ts`, `webapp/test/unit/unitTests.qunit.ts`
- Delete: `webapp/helpers/PurchaseContractFunruralHelpers.ts`, `webapp/test/unit/helpers/PurchaseContractFunruralHelpers.qunit.ts`, `webapp/view/purchaseContracts/fragments/PurchaseContractFiscalComplement.fragment.xml`, `…/PurchaseContractFiscalComplementDialog.fragment.xml`, `…/PurchaseContractFunruralTotals.fragment.xml`
- Modify: `webapp/controller/purchaseContracts/PurchaseContractsBaseController.ts`, `Detail.controller.ts`, `approval/Detail.controller.ts`, `webapp/view/purchaseContracts/Detail.view.xml`, `approval/Detail.view.xml`
- Keep (spec "fica"): `getApprovalErrorMessage` da Aprovação, a porta única `applyContractTotals`, `refreshContractTotals` `protected`, o `visible` da seção "Impostos" em Add/Edit e as chamadas `refreshStandaloneFlag()` de Add/Edit.

**Interfaces:**
- Produces: `applyContractTotals(viewModel, data)` grava só os 10 campos de volume/preço/impostos (sem `/NetToProducer` nem `/funrural`). Detail e Aprovação chamam `refreshStandaloneFlag()` direto (antes vinha por `startFiscalComplement`).

- [ ] **Step 1: Conferir a base**

Run: `git log --oneline 663c950..main`
Expected: só `02fa89c`, `ad30e56`, `deb0fe8`, `5e66324` (o sub-projeto 1). Qualquer outro commit: parar e reportar.

- [ ] **Step 2: Restaurar e apagar**

```bash
git checkout 663c950 -- webapp/model/ServerRoutes.ts webapp/model/formatter.ts webapp/types/PurchaseContractsTotal.ts webapp/test/unit/unitTests.qunit.ts
git rm webapp/helpers/PurchaseContractFunruralHelpers.ts \
  webapp/test/unit/helpers/PurchaseContractFunruralHelpers.qunit.ts \
  webapp/view/purchaseContracts/fragments/PurchaseContractFiscalComplement.fragment.xml \
  webapp/view/purchaseContracts/fragments/PurchaseContractFiscalComplementDialog.fragment.xml \
  webapp/view/purchaseContracts/fragments/PurchaseContractFunruralTotals.fragment.xml
```

- [ ] **Step 3: `PurchaseContractsBaseController.ts`**

1. Apagar os imports `import SessionService from "siagrob1/services/SessionService";` e `import { summarizeContractFunrural } from "siagrob1/helpers/PurchaseContractFunruralHelpers";` (manter `import { PurchaseContractsTotals } from "siagrob1/types/PurchaseContractsTotal";`).
2. Apagar o bloco `/** Complemento fiscal do contrato de compra ... */ export type PurchaseFiscalComplement = { ... }` inteiro.
3. Apagar o campo `/** Contrato cujo complemento fiscal a tela mostra; ... */ protected _fiscalContractKey: string;`.
4. Em `applyContractTotals`, apagar as duas últimas linhas:
   ```ts
       viewModel.setProperty("/NetToProducer", data.NetToProducer ?? 0);
       viewModel.setProperty("/funrural", summarizeContractFunrural(data));
   ```
5. Apagar os métodos `initFiscalComplementModel`, `startFiscalComplement` e `loadFiscalComplement` (com seus comentários `/** ... */`), que ficam entre `applyContractTotals` e o comentário de `refreshContractTotals`.

- [ ] **Step 4: `purchaseContracts/Detail.controller.ts`**

1. Linha 2 vira `import PurchaseContractsBaseController from "./PurchaseContractsBaseController";`
2. Apagar os imports de `ODataModel`, `Dialog`, `Filter`, `FilterOperator` e `DialogHelper` (só o complemento fiscal os usava).
3. Apagar os campos `private _fiscalComplementDialog: Dialog;` e `private _fiscalSaving = false;`.
4. `onInit` fica:
   ```ts
	onInit(): void  {	
		this.initContractDrafts();
		this.getRouter().getRoute("purchaseContractsDetail").attachPatternMatched((ev) => this.detailRouteMatched(ev));
	}
   ```
5. Em `detailRouteMatched`, trocar `void this.startFiscalComplement(id, true);` por `void this.refreshStandaloneFlag();`.
6. Apagar os métodos `onOpenFiscalComplementDialog`, `onCloseFiscalComplementDialog`, `openFiscalComplementUsageValueHelp`, `onClearFiscalComplementUsage` e `onSaveFiscalComplement` inteiros.
7. Conferir: `grep -n "ODataModel\|Dialog\b\|Filter\|DialogHelper" webapp/controller/purchaseContracts/Detail.controller.ts` não encontra uso.

- [ ] **Step 5: `purchaseContracts/approval/Detail.controller.ts`**

1. `onInit`: apagar `this.initFiscalComplementModel();`.
2. `detailRouteMatched`: trocar `void this.startFiscalComplement(id, false);` por `void this.refreshStandaloneFlag();`.
3. Apagar o comentário `// Aprovação é somente leitura: o fragmento reutilizado referencia este handler no botão oculto.` e `onOpenFiscalComplementDialog(): void { /* sem ação */ }`.
4. **Manter** `getApprovalErrorMessage` e os três `error:` que o usam.

- [ ] **Step 6: Views `Detail.view.xml` e `approval/Detail.view.xml` (as duas, mesmas edições)**

1. Apagar o `<HBox class="sapUiTinyMarginBottom" visible="{= ${ui>/standalone} === true }">` de "Líquido ao produtor:" inteiro (até o `</HBox>` dele).
2. O `HBox` de "Total Impostos:" ganha `visible` — trocar
   ```xml
          <HBox class="sapUiTinyMarginBottom">
            <Text text="Total Impostos:" class="sapUiMediumMarginEnd" />
   ```
   por
   ```xml
          <HBox class="sapUiTinyMarginBottom" visible="{= ${ui>/standalone} !== true }">
            <Text text="Total Impostos:" class="sapUiMediumMarginEnd" />
   ```
3. Apagar a `uxap:ObjectPageSection` `title="Complemento Fiscal"` inteira.
4. Apagar a `uxap:ObjectPageSection` `title="Impostos" visible="{= ${ui>/standalone} === true }"` (a do `PurchaseContractFunruralTotals`) inteira.
5. **Manter** a seção "Impostos" da grade (`PurchaseContractTaxes`) com `visible="{= ${ui>/standalone} !== true }"`.

- [ ] **Step 7: Gates**

```bash
grep -rn "FiscalComplement\|NetToProducer\|summarizeContractFunrural\|PurchaseContractFunrural\|PURCHASE_CONTRACT_FISCAL_EDIT\|fiscalComplement" webapp
yarn ts-typecheck && yarn lint
```
Expected: o `grep` não encontra nada; typecheck e lint sem erro (dev server parado).

- [ ] **Step 8: Stage**

```bash
git add -A webapp
git status --short
```
O controlador commita com:

```
refactor(purchase-contract): desfaz a seção de natureza e o quadro de Funrural do contrato

O Funrural passa a vir da linha do documento de entrada conciliado (spec 2026-10-13, títulos
firmes). Saem a seção Complemento Fiscal, o quadro de Funrural e o Líquido ao produtor; em
STANDALONE a grade de impostos e o Total Impostos ficam ocultos. Fica o tratamento de erro da
Aprovação (recusa de negócio aparece na tela) e a porta única applyContractTotals.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 3: Modelo — enums, colunas, tabela de composição, índice FirmOrigin e migration

Repositório backend. A partir daqui, todas as tasks de backend no branch `feature/purchase-firm-payables`.

**Files:**
- Modify: `SiagroB1.Domain/Enums/FinancialDocumentNature.cs`, `SiagroB1.Domain/Enums/FinancialDocumentOrigin.cs`, `SiagroB1.Domain/Enums/FinancialSettlementOrigin.cs`
- Modify: `SiagroB1.Domain/Entities/FinancialDocument.cs`, `SiagroB1.Domain/Entities/FinancialSettlement.cs`, `SiagroB1.Domain/Entities/FinancialDocumentChangeLogFields.cs`
- Create: `SiagroB1.Domain/Entities/FinancialDocumentComposition.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (DbSet ~131; FKs logo depois das FKs de `FinancialDocument` ~476; índice logo depois de `IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin` ~553)
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (lista de `AddProperty` do `FinancialDocument`, ~1551)
- Create: migration `AddFinancialDocumentComposition` (AppDbContext)
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentCompositionModelTests.cs`

**Interfaces:**
- Produces:
  - `FinancialDocumentNature.PurchaseReturn = 4`; `FinancialDocumentOrigin.PurchaseFiscalReconciliation = 7`; `FinancialSettlementOrigin.Dation = 6`.
  - `FinancialDocument`: `Guid? PurchaseInvoiceKey`, `Guid? StorageTransactionKey`, `string? StorageTransactionCode`, `string? TaxDocumentNumber`, `decimal ComposedAmount`; `[NotMapped] decimal AvailableToSettleAmount`; `[NotMapped] bool IsPurchaseFirmPayable`; `IsBlockedForSettlement` novo.
  - `FinancialSettlement`: `Guid? RelatedDocumentKey`, `Guid? CompositionKey`.
  - `FinancialDocumentComposition : BaseEntity { Guid ProvisionalDocumentKey; Guid ComposedDocumentKey; decimal Amount }`; `AppDbContext.FinancialDocumentCompositions`.
  - `FinancialDocumentChangeLogFields.Composition = "Composition"`.

- [ ] **Step 1: Teste que falha**

`SiagroB1.Application.Tests/Financials/FinancialDocumentCompositionModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Modelo dos títulos firmes de compra (spec 2026-10-13): composição, disponível para baixa e bloqueio.</summary>
public class FinancialDocumentCompositionModelTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FinancialDocument Document(
        FinancialDocumentNature nature, FinancialDocumentOrigin origin, decimal net, decimal settled = 0m, decimal composed = 0m) => new()
    {
        Key = Guid.NewGuid(), Code = "FN000001", CardCode = "F0001", Direction = FinancialDirection.Payable,
        Nature = nature, OriginType = origin, NetAmount = net, SettledAmount = settled, ComposedAmount = composed,
        DueDate = new DateTime(2026, 12, 31),
    };

    [Fact]
    public void New_enum_values_are_appended_with_fixed_numbers()
    {
        Assert.Equal(4, (int)FinancialDocumentNature.PurchaseReturn);
        Assert.Equal(7, (int)FinancialDocumentOrigin.PurchaseFiscalReconciliation);
        Assert.Equal(6, (int)FinancialSettlementOrigin.Dation);
    }

    [Fact]
    public async Task Composition_row_keeps_the_signed_amount()
    {
        var provisional = Document(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.PurchaseContractPriceFixation, 1000m);
        var refund = Document(FinancialDocumentNature.PurchaseReturn, FinancialDocumentOrigin.PurchaseFiscalReconciliation, 120.55m);
        _db.Context.FinancialDocuments.AddRange(provisional, refund);
        _db.Context.FinancialDocumentCompositions.Add(new FinancialDocumentComposition
        {
            Key = Guid.NewGuid(), ProvisionalDocumentKey = provisional.Key, ComposedDocumentKey = refund.Key, Amount = -120.55m,
        });
        await _db.Context.SaveChangesAsync();

        var row = await _db.Context.FinancialDocumentCompositions.AsNoTracking().SingleAsync();
        Assert.Equal(-120.55m, row.Amount);
        Assert.Equal(provisional.Key, row.ProvisionalDocumentKey);
        Assert.Equal(refund.Key, row.ComposedDocumentKey);
    }

    [Theory]
    [InlineData(1000.0, 400.0, 300.0, 100.0)]
    [InlineData(1000.0, 400.0, 400.0, 0.0)]
    [InlineData(1000.0, 0.0, 0.0, 0.0)]
    public void Provisional_available_is_composed_minus_settled(double net, double composed, double settled, double expected)
    {
        var provisional = Document(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.PurchaseContractPriceFixation,
            (decimal)net, (decimal)settled, (decimal)composed);

        Assert.Equal((decimal)expected, provisional.AvailableToSettleAmount);
        Assert.Equal(expected <= 0, provisional.IsBlockedForSettlement);
    }

    [Fact]
    public void Conciliation_firm_available_is_the_open_amount_and_it_is_always_blocked()
    {
        var firm = Document(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseFiscalReconciliation, 500m, settled: 120m);

        Assert.Equal(380m, firm.AvailableToSettleAmount);
        Assert.True(firm.IsPurchaseFirmPayable);
        Assert.True(firm.IsBlockedForSettlement);
        Assert.True(Document(FinancialDocumentNature.PurchaseReturn, FinancialDocumentOrigin.PurchaseFiscalReconciliation, 50m)
            .IsBlockedForSettlement);
        // D11: o firme do complemento de valor (origem PurchaseInvoice) é firme de compra igual.
        var complement = Document(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseInvoice, 80m);
        Assert.True(complement.IsPurchaseFirmPayable);
        Assert.True(complement.IsBlockedForSettlement);
    }

    [Fact]
    public void Washout_receivable_and_advance_stay_free_to_settle()
    {
        var receivable = Document(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseContractWashout, 500m);
        receivable.Direction = FinancialDirection.Receivable;

        Assert.False(receivable.IsPurchaseFirmPayable);
        Assert.False(receivable.IsBlockedForSettlement);
        Assert.False(Document(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, 500m).IsBlockedForSettlement);
    }

    [Fact]
    public void Edm_exposes_the_new_amounts_but_not_the_composition_set()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();
        var document = model.EntityContainer.FindEntitySet("FinancialDocuments").EntityType;
        var settlement = model.EntityContainer.FindEntitySet("FinancialSettlements").EntityType;

        Assert.NotNull(document.FindProperty(nameof(FinancialDocument.AvailableToSettleAmount)));
        Assert.NotNull(document.FindProperty(nameof(FinancialDocument.ComposedAmount)));
        Assert.NotNull(document.FindProperty(nameof(FinancialDocument.TaxDocumentNumber)));
        Assert.NotNull(document.FindProperty(nameof(FinancialDocument.StorageTransactionCode)));
        Assert.Null(document.FindProperty(nameof(FinancialDocument.IsPurchaseFirmPayable)));
        Assert.NotNull(settlement.FindProperty(nameof(FinancialSettlement.RelatedDocumentKey)));
        Assert.NotNull(settlement.FindProperty(nameof(FinancialSettlement.CompositionKey)));
        Assert.Null(model.EntityContainer.FindEntitySet("FinancialDocumentCompositions"));
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentCompositionModelTests"`
Expected: erro de compilação (`PurchaseReturn`, `ComposedAmount`, `FinancialDocumentComposition` não existem).

- [ ] **Step 3: Enums**

`FinancialDocumentNature.cs`:

```csharp
namespace SiagroB1.Domain.Enums;

public enum FinancialDocumentNature
{
    Provisional = 0,
    Firm = 1,
    Advance = 2,
    TaxWithholding = 3,

    /// <summary>
    /// Título de DEVOLUÇÃO de compra (spec 2026-10-13, títulos firmes): nasce do vínculo de uma NF de devolução com o
    /// romaneio 9/16. Compensa o firme normal (encontro de contas) ou entra NEGATIVO na composição do provisório.
    /// </summary>
    PurchaseReturn = 4
}
```

`FinancialDocumentOrigin.cs` — depois de `PurchaseContractWashout = 6` (vírgula nele):

```csharp
    PurchaseContractWashout = 6,

    /// <summary>Firme/devolução nascido de um vínculo PURCHASE_FISCAL_RECONCILIATIONS (OriginKey = chave do vínculo).</summary>
    PurchaseFiscalReconciliation = 7
```

`FinancialSettlementOrigin.cs` — depois de `AdvanceRefund = 5` (vírgula nele):

```csharp
    AdvanceRefund = 5,

    /// <summary>
    /// Dação: o operador baixa a sobra de um firme (NF acima do contrato) sem dinheiro, com motivo. Sem conta.
    /// Estornável pelo estorno genérico.
    /// </summary>
    Dation = 6
```

- [ ] **Step 4: Entidades**

`FinancialDocument.cs` — logo depois da propriedade `OriginDocNumber`:

```csharp

    /// <summary>
    /// Documento de entrada do FIRME (spec 2026-10-13, títulos firmes). Sem FK, como <see cref="OriginKey"/>: o
    /// título sobrevive ao registro que narra.
    /// </summary>
    public Guid? PurchaseInvoiceKey { get; set; }

    /// <summary>Romaneio do vínculo que gerou o firme. Sem FK.</summary>
    public Guid? StorageTransactionKey { get; set; }

    /// <summary>Código do romaneio, desnormalizado para a lista.</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? StorageTransactionCode { get; set; }

    /// <summary>"número-série" da NF, ou o número interno do documento sem NF (PurchaseFiscalReconciliationRules.TaxDocument).</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? TaxDocumentNumber { get; set; }

    /// <summary>
    /// Só no PROVISÓRIO: persistido-derivado, Σ FINANCIAL_DOCUMENT_COMPOSITIONS.Amount (assinado: firme +, devolução −).
    /// Escritor ÚNICO: FinancialDocumentsRecalculateBalanceService.
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal ComposedAmount { get; set; }
```

Trocar o bloco inteiro de `IsBlockedForSettlement` (o `/// <summary>` "Derivado da natureza, SEM coluna…" e a propriedade) por:

```csharp
    /// <summary>
    /// Derivado, SEM coluna — uma coluna criaria uma segunda fonte de verdade. Provisório: bloqueado enquanto não houver
    /// composto a pagar (paga-se só o que está coberto por firme). Firme e devolução da conciliação: sempre — saem só
    /// por composição, dação ou encontro de contas. Demais (adiantamento, título a receber do washout): livres.
    /// </summary>
    [NotMapped]
    public bool IsBlockedForSettlement => Nature switch
    {
        FinancialDocumentNature.Provisional => AvailableToSettleAmount <= 0m,
        _ => IsPurchaseFirmPayable,
    };

    /// <summary>Provisório: composto − baixado (mínimo 0). Demais naturezas: o saldo em aberto.</summary>
    [NotMapped]
    public decimal AvailableToSettleAmount => Nature == FinancialDocumentNature.Provisional
        ? Math.Max(0m, decimal.Round(ComposedAmount - SettledAmount, 2, MidpointRounding.ToEven))
        : OpenAmount;

    /// <summary>
    /// Firme ou devolução nascidos da Conciliação Fiscal de Compras, ou firme nascido da confirmação do documento de
    /// entrada "Complemento de valor" (spec D11, origem PurchaseInvoice). O título a receber do washout também é Firm,
    /// mas de outra origem, e continua baixável em dinheiro. Só em memória (o EF não traduz) e fora do EDM.
    /// </summary>
    [NotMapped]
    public bool IsPurchaseFirmPayable =>
        (OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation &&
         Nature is FinancialDocumentNature.Firm or FinancialDocumentNature.PurchaseReturn) ||
        (OriginType == FinancialDocumentOrigin.PurchaseInvoice && Nature == FinancialDocumentNature.Firm);
```

`FinancialSettlement.cs` — logo depois de `ReversedSettlementKey`:

```csharp

    /// <summary>
    /// Abatimento (InvoiceOffset): o PROVISÓRIO em que o título foi composto. Encontro de contas (Netting): o título do
    /// outro lado. Sem FK.
    /// </summary>
    public Guid? RelatedDocumentKey { get; set; }

    /// <summary>
    /// Abatimento: a linha de composição que o gerou — é como "desfazer" acha a linha a estornar. Sem FK: a composição é
    /// removida ao desfazer; o ledger, nunca.
    /// </summary>
    public Guid? CompositionKey { get; set; }
```

`FinancialDocumentChangeLogFields.cs` — depois de `NetAmount`:

```csharp

    /// <summary>
    /// Composição no provisório, registrada nos DOIS títulos: valores "código do outro título: valor N2 pt-BR"
    /// (assinado). OldValue nulo = composto; NewValue nulo = desfeito.
    /// </summary>
    public const string Composition = "Composition";
```

`SiagroB1.Domain/Entities/FinancialDocumentComposition.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Um título firme (ou de devolução) aglutinado no PROVISÓRIO de uma fixação (spec 2026-10-13, títulos firmes, regras
/// 9–13). <see cref="Amount"/> é ASSINADO: firme positivo, devolução negativa. A linha é REMOVIDA ao desfazer; o rastro
/// fica no ledger (abatimento + estorno) e no log dos dois títulos.
/// <para>As duas FKs para FINANCIAL_DOCUMENTS são declaradas à mão no AppDbContext, sem navegação e sem coleção
/// inversa no título (entraria no EDM).</para>
/// </summary>
[Table("FINANCIAL_DOCUMENT_COMPOSITIONS")]
public class FinancialDocumentComposition : BaseEntity
{
    public Guid ProvisionalDocumentKey { get; set; }

    public Guid ComposedDocumentKey { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal Amount { get; set; }
}
```

`git add SiagroB1.Domain/Entities/FinancialDocumentComposition.cs`

- [ ] **Step 5: AppDbContext**

Depois de `public DbSet<FinancialDocumentChangeLog> FinancialDocumentChangeLogs { get; set; }`:

```csharp
    public DbSet<FinancialDocumentComposition> FinancialDocumentCompositions { get; set; }
```

Logo depois do bloco `modelBuilder.Entity<FinancialDocument>().HasOne(x => x.SalesContract)…OnDelete(DeleteBehavior.NoAction);`:

```csharp

        // Composição (spec 2026-10-13, títulos firmes): duas FKs para a MESMA tabela, declaradas à mão para a convenção
        // não emparelhar errado; WithMany() SEM coleção inversa (entraria no EDM). NoAction: título financeiro não some.
        // Os índices das duas colunas vêm da convenção de FK.
        modelBuilder.Entity<FinancialDocumentComposition>()
            .HasOne<FinancialDocument>().WithMany()
            .HasForeignKey(x => x.ProvisionalDocumentKey).OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<FinancialDocumentComposition>()
            .HasOne<FinancialDocument>().WithMany()
            .HasForeignKey(x => x.ComposedDocumentKey).OnDelete(DeleteBehavior.NoAction);
```

Logo depois do índice `IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin`:

```csharp

        // Par do índice acima para o firme e a devolução: um título ativo por vínculo de conciliação. Também cobre o
        // título a receber do washout (Firm, origem própria), que já é um por washout.
        modelBuilder.Entity<FinancialDocument>()
            .HasIndex(x => new { x.OriginType, x.OriginKey }, "IX_FINANCIAL_DOCUMENTS_FirmOrigin")
            .IsUnique()
            .HasFilter($"[Nature] IN ({(int)FinancialDocumentNature.Firm}, {(int)FinancialDocumentNature.PurchaseReturn}) " +
                       $"AND [Status] <> {(int)FinancialDocumentStatus.Canceled} " +
                       "AND [OriginKey] IS NOT NULL");
```

- [ ] **Step 6: EDM**

`ODataConfigurations.cs`, no array do `foreach` de `AddProperty` do `FinancialDocument`, acrescentar depois de `nameof(FinancialDocument.AvailableAdvanceAmount)`:

```csharp
                     nameof(FinancialDocument.AvailableAdvanceAmount),
                     nameof(FinancialDocument.AvailableToSettleAmount)
```

(e trocar o comentário "sem estes quatro AddProperty" por "sem estes AddProperty"). **Não** adicionar `IsPurchaseFirmPayable`.

- [ ] **Step 7: Rodar o teste**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentCompositionModelTests|FullyQualifiedName~FinancialEdmModelTests|FullyQualifiedName~FinancialDocumentsWashoutTests|FullyQualifiedName~FinancialDocumentsGenerateServiceTests|FullyQualifiedName~FinancialAdvancesCreateServiceTests"`
Expected: todos PASS (os três últimos provam que o adiantamento e o título a receber do washout continuam desbloqueados e o provisório novo continua bloqueado).

- [ ] **Step 8: Migration**

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddFinancialDocumentComposition --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Conferir no `Up`, e **só** isto: `CreateTable FINANCIAL_DOCUMENT_COMPOSITIONS` (colunas da `BaseEntity` + `ProvisionalDocumentKey`, `ComposedDocumentKey`, `Amount DECIMAL(18,2)`, duas FKs `NoAction` para `FINANCIAL_DOCUMENTS`); `AddColumn` em `FINANCIAL_DOCUMENTS` (`PurchaseInvoiceKey`, `StorageTransactionKey`, `StorageTransactionCode VARCHAR(50)`, `TaxDocumentNumber VARCHAR(50)`, `ComposedAmount DECIMAL(18,2) DEFAULT 0`); `AddColumn` em `FINANCIAL_SETTLEMENTS` (`RelatedDocumentKey`, `CompositionKey`); `CreateIndex` dos dois índices de FK e de `IX_FINANCIAL_DOCUMENTS_FirmOrigin` com `filter: "[Nature] IN (1, 4) AND [Status] <> 3 AND [OriginKey] IS NOT NULL"` e `unique: true`. Se `ComposedAmount` sair `nullable: true` ou sem `defaultValueSql`, conferir o `[Column]` — tem de ser `DECIMAL(18,2) DEFAULT 0` como `SettledAmount`. `git add` dos 3 arquivos.

- [ ] **Step 9: Gates**

```bash
dotnet build SiagroB1.sln
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Expected: build OK; "No changes have been made to the model since the last migration."

- [ ] **Step 10: Stage**

```bash
git add SiagroB1.Domain SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Migrations SiagroB1.Application.Tests/Financials/FinancialDocumentCompositionModelTests.cs
```
O controlador commita com:

```
feat(financial): modelo da composição de títulos firmes no provisório

Firme e devolução de compra nascem da conciliação fiscal e são aglutinados à mão no provisório
da fixação, que só recebe baixa até o composto. O provisório ganha ComposedAmount e o disponível
para baixa; firme e devolução da conciliação ficam sempre bloqueados para baixa em dinheiro. O
título a receber do washout (também Firm) continua livre. Sem backfill: provisório existente
fica com composto 0, igual ao bloqueio de antes.

DB: AddFinancialDocumentComposition
Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 4: Recalculador (`ComposedAmount`, linhas pendentes) e guard da baixa (regras 15–16)

**Files:**
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsRecalculateBalanceService.cs`
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsSettlementGuardService.cs`
- Modify: `SiagroB1.Application.Tests/Financials/FinancialDocumentsSettleServiceTests.cs` (assert do teste `Refuses_to_settle_a_provisional_document`)
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsRecalculateComposedTests.cs`, `SiagroB1.Application.Tests/Financials/FinancialDocumentsSettlementGuardFirmTests.cs`

**Interfaces:**
- Consumes: Task 3.
- Produces: `FinancialDocumentsRecalculateBalanceService.RecalculateAsync(AppDbContext, Guid)` (mesma assinatura) agora (a) acha título ainda não salvo (`FindAsync`), (b) soma linhas **pendentes** do ChangeTracker (Added soma, Deleted subtrai) de `FINANCIAL_SETTLEMENTS` e `FINANCIAL_DOCUMENT_COMPOSITIONS`, (c) escreve `ComposedAmount` do provisório. Todas as tasks seguintes chamam este método **antes** do `SaveChanges` (enqueue-only).

- [ ] **Step 1: Testes que falham — recalculador**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsRecalculateComposedTests.cs`:

```csharp
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// O recalculador continua escritor único (SettledAmount, Status) e passa a escrever ComposedAmount do provisório.
/// Enxerga linhas ainda não salvas: é o que deixa composição, desfazer e encontro de contas num SaveChanges só.
/// </summary>
public class FinancialDocumentsRecalculateComposedTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FinancialDocument Provisional(decimal net = 1000m) => new()
    {
        Key = Guid.NewGuid(), Code = "FN-PROV", CardCode = "F0001", Direction = FinancialDirection.Payable,
        Nature = FinancialDocumentNature.Provisional, NetAmount = net, DueDate = new DateTime(2026, 12, 31),
        OriginType = FinancialDocumentOrigin.PurchaseContractPriceFixation, OriginKey = Guid.NewGuid(),
    };

    private static FinancialDocumentComposition Row(Guid provisionalKey, decimal amount) => new()
    {
        Key = Guid.NewGuid(), ProvisionalDocumentKey = provisionalKey, ComposedDocumentKey = Guid.NewGuid(), Amount = amount,
    };

    private static FinancialSettlement Settlement(Guid documentKey, decimal amount) => new()
    {
        Key = Guid.NewGuid(), FinancialDocumentKey = documentKey, Amount = amount, SettlementDate = DateTime.Today,
        Origin = FinancialSettlementOrigin.Manual,
    };

    [Fact]
    public async Task Composed_amount_is_the_signed_sum_of_the_composition_rows()
    {
        var provisional = Provisional();
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.FinancialDocumentCompositions.AddRange(Row(provisional.Key, 300m), Row(provisional.Key, -50m));
        await _db.Context.SaveChangesAsync();

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, provisional.Key);

        Assert.Equal(250m, provisional.ComposedAmount);
        Assert.Equal(250m, provisional.AvailableToSettleAmount);
    }

    [Fact]
    public async Task Pending_rows_are_counted_before_save()
    {
        var provisional = Provisional();
        var persisted = Row(provisional.Key, 400m);
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.FinancialDocumentCompositions.Add(persisted);
        await _db.Context.SaveChangesAsync();

        _db.Context.FinancialDocumentCompositions.Remove(persisted);                 // Deleted: ainda no banco
        _db.Context.FinancialDocumentCompositions.Add(Row(provisional.Key, 100m));   // Added: ainda fora dele
        _db.Context.FinancialSettlements.Add(Settlement(provisional.Key, 60m));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, provisional.Key);

        Assert.Equal(100m, provisional.ComposedAmount);
        Assert.Equal(60m, provisional.SettledAmount);
        Assert.Equal(40m, provisional.AvailableToSettleAmount);
    }

    [Fact]
    public async Task Unsaved_document_is_found_and_recalculated()
    {
        var provisional = Provisional(500m);
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.FinancialSettlements.Add(Settlement(provisional.Key, 200m));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, provisional.Key);

        Assert.Equal(200m, provisional.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, provisional.Status);
    }

    [Fact]
    public async Task Provisional_is_settled_only_when_the_contracted_value_is_paid()
    {
        var provisional = Provisional(1000m);
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.FinancialDocumentCompositions.Add(Row(provisional.Key, 1000m));
        _db.Context.FinancialSettlements.Add(Settlement(provisional.Key, 600m));
        await _db.Context.SaveChangesAsync();

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, provisional.Key);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, provisional.Status);

        _db.Context.FinancialSettlements.Add(Settlement(provisional.Key, 400m));
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Settled, provisional.Status);
    }
}
```

- [ ] **Step 2: Testes que falham — guard**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsSettlementGuardFirmTests.cs`:

```csharp
using System.Globalization;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Regra 15: o provisório baixa só o composto; firme e devolução da conciliação não recebem baixa em dinheiro.</summary>
public class FinancialDocumentsSettlementGuardFirmTests
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private FinancialDocumentsSettleService Settle() => new(_db);

    private async Task<FinancialDocument> SeedAsync(
        FinancialDocumentNature nature, FinancialDocumentOrigin origin, decimal net, decimal composed = 0m,
        FinancialDirection direction = FinancialDirection.Payable)
    {
        _db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl,
        });
        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = "FN000001", CardCode = "F0001", Direction = direction, Nature = nature,
            OriginType = origin, OriginKey = Guid.NewGuid(), NetAmount = net, DueDate = new DateTime(2026, 12, 31),
            Currency = CurrencyType.Brl,
        };
        _db.Context.FinancialDocuments.Add(document);
        if (composed != 0m)
            _db.Context.FinancialDocumentCompositions.Add(new FinancialDocumentComposition
            {
                Key = Guid.NewGuid(), ProvisionalDocumentKey = document.Key, ComposedDocumentKey = Guid.NewGuid(), Amount = composed,
            });
        await _db.Context.SaveChangesAsync();
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, document.Key);
        await _db.Context.SaveChangesAsync();
        return document;
    }

    private static string AboveAvailable(decimal available) =>
        $"Valor acima do disponível para baixa ({available.ToString("N2", PtBr)}): componha mais títulos firmes antes.";

    [Fact]
    public async Task Provisional_without_composition_has_nothing_available()
    {
        var provisional = await SeedAsync(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.PurchaseContractPriceFixation, 1000m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Settle().ExecuteAsync(provisional.Key, "CX01", 100m, DateTime.Today, 0m, 0m, 0m, null, null, "tester"));

        Assert.Equal(AboveAvailable(0m), error.Message);
    }

    [Fact]
    public async Task Provisional_settles_up_to_the_composed_amount()
    {
        var provisional = await SeedAsync(
            FinancialDocumentNature.Provisional, FinancialDocumentOrigin.PurchaseContractPriceFixation, 1000m, composed: 400m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Settle().ExecuteAsync(provisional.Key, "CX01", 400.01m, DateTime.Today, 0m, 0m, 0m, null, null, "tester"));
        Assert.Equal(AboveAvailable(400m), error.Message);

        await Settle().ExecuteAsync(provisional.Key, "CX01", 400m, DateTime.Today, 0m, 0m, 0m, null, null, "tester");

        Assert.Equal(400m, provisional.SettledAmount);
        Assert.Equal(0m, provisional.AvailableToSettleAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, provisional.Status);
    }

    [Theory]
    [InlineData(FinancialDocumentNature.Firm)]
    [InlineData(FinancialDocumentNature.PurchaseReturn)]
    public async Task Conciliation_firm_refuses_cash_settlement(FinancialDocumentNature nature)
    {
        var firm = await SeedAsync(nature, FinancialDocumentOrigin.PurchaseFiscalReconciliation, 500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Settle().ExecuteAsync(firm.Key, "CX01", 100m, DateTime.Today, 0m, 0m, 0m, null, null, "tester"));

        Assert.Equal("Título firme não recebe baixa direta: componha no provisório ou baixe por dação.", error.Message);
    }

    [Fact]
    public async Task Washout_receivable_still_settles_in_cash()
    {
        var receivable = await SeedAsync(
            FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseContractWashout, 500m,
            direction: FinancialDirection.Receivable);

        await Settle().ExecuteAsync(receivable.Key, "CX01", 500m, DateTime.Today, 0m, 0m, 0m, null, null, "tester");

        Assert.Equal(FinancialDocumentStatus.Settled, receivable.Status);
    }
}
```

`git add` os dois arquivos de teste.

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsRecalculateComposedTests|FullyQualifiedName~FinancialDocumentsSettlementGuardFirmTests"`
Expected: FAIL — `ComposedAmount` fica 0, linha pendente não é contada, `RecalculateAsync` lança `NotFoundException` para o título não salvo, e o guard devolve a mensagem antiga ("…é provisório e não pode ser baixado…").

- [ ] **Step 4: Recalculador**

`FinancialDocumentsRecalculateBalanceService.cs` — usings a acrescentar: `Microsoft.EntityFrameworkCore.ChangeTracking`. Trocar o método estático `RecalculateAsync` inteiro por:

```csharp
    /// <summary>
    /// Recalcula e apenas ENFILEIRA a alteração — quem chama decide quando salvar.
    ///
    /// Soma o ledger do BANCO mais as linhas ainda PENDENTES no ChangeTracker (Added soma; Deleted ainda está no banco,
    /// então subtrai). É o que deixa composição, desfazer, encontro de contas e o firme recém-nascido num SaveChanges
    /// só, sem salvar no meio. Ledger e composição não têm valor alterado em linha existente (insert-only/remove), então
    /// Modified não entra. Depois do SaveChanges não há pendência e o resultado é o mesmo de sempre.
    /// </summary>
    public static async Task RecalculateAsync(AppDbContext context, Guid documentKey)
    {
        // FindAsync, e não uma consulta: enxerga também o título ainda não salvo.
        var document = await context.FinancialDocuments.FindAsync(documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        var settled = (await context.FinancialSettlements
                          .Where(x => x.FinancialDocumentKey == documentKey)
                          .SumAsync(x => (decimal?)x.Amount) ?? 0m)
                      + PendingDelta(context.ChangeTracker.Entries<FinancialSettlement>()
                          .Where(e => e.Entity.FinancialDocumentKey == documentKey), x => x.Amount);

        document.SettledAmount = decimal.Round(settled, 2, MidpointRounding.ToEven);

        // Spec 2026-10-13 (títulos firmes): só o provisório tem composição.
        if (document.Nature == FinancialDocumentNature.Provisional)
        {
            var composed = (await context.FinancialDocumentCompositions
                               .Where(x => x.ProvisionalDocumentKey == documentKey)
                               .SumAsync(x => (decimal?)x.Amount) ?? 0m)
                           + PendingDelta(context.ChangeTracker.Entries<FinancialDocumentComposition>()
                               .Where(e => e.Entity.ProvisionalDocumentKey == documentKey), x => x.Amount);

            document.ComposedAmount = decimal.Round(composed, 2, MidpointRounding.ToEven);
        }

        if (document.Status == FinancialDocumentStatus.Canceled)
            return;

        // Documento sem valor nao tem o que liquidar: nasce e permanece quitado. Sem isto, o
        // braco `<= 0` da expressao abaixo o classificaria como Aberto para sempre, e ele ficaria
        // entulhando a tela de Contas a Pagar com saldo zero.
        // Regra 16: o provisório só fica Settled quando a baixa alcança o valor CONTRATADO (NetAmount).
        document.Status = document.NetAmount <= 0m
            ? FinancialDocumentStatus.Settled
            : document.SettledAmount switch
            {
                <= 0m => FinancialDocumentStatus.Open,
                var s when s >= document.NetAmount => FinancialDocumentStatus.Settled,
                _ => FinancialDocumentStatus.PartiallySettled
            };
    }

    private static decimal PendingDelta<T>(IEnumerable<EntityEntry<T>> entries, Func<T, decimal> amount) where T : class =>
        entries.Sum(e => e.State switch
        {
            EntityState.Added => amount(e.Entity),
            EntityState.Deleted => -amount(e.Entity),
            _ => 0m,
        });
```

(`FinancialDocumentComposition` vem de `SiagroB1.Domain.Entities`, já importado.)

- [ ] **Step 5: Guard**

`FinancialDocumentsSettlementGuardService.cs` completo:

```csharp
using System.Globalization;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Regra ÚNICA de "este documento pode ser baixado?". Estático e sem estado de propósito: a
/// baixa, o estorno e (na Fase 4) o pagamento em lote têm de recusar exatamente pelos mesmos
/// motivos.
/// </summary>
public static class FinancialDocumentsSettlementGuardService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static void EnsureCanSettle(
        FinancialDocument document, FinancialAccount? account, decimal amount)
    {
        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O documento {document.Code} está cancelado.");

        // Regra 15 (spec 2026-10-13, títulos firmes): firme e devolução da conciliação saem só por composição no
        // provisório, dação ou encontro de contas — nunca por baixa em dinheiro.
        if (document.IsPurchaseFirmPayable)
            throw new ApplicationException(
                "Título firme não recebe baixa direta: componha no provisório ou baixe por dação.");

        if (account is null)
            throw new ApplicationException("Informe a conta financeira da baixa.");

        if (account.Inactive)
            throw new ApplicationException($"A conta financeira {account.Code} está inativa.");

        if (account.Currency != document.Currency)
            throw new ApplicationException(
                $"A conta {account.Code} é em {account.Currency} e o documento em " +
                $"{document.Currency}: a moeda precisa ser a mesma.");

        if (amount <= 0m)
            throw new ApplicationException("O valor da baixa deve ser maior que zero.");

        // Regra 15: o provisório paga só o que está coberto por firme composto (trava rígida).
        if (document.Nature == FinancialDocumentNature.Provisional && amount > document.AvailableToSettleAmount)
            throw new ApplicationException(
                $"Valor acima do disponível para baixa ({document.AvailableToSettleAmount.ToString("N2", PtBr)}): " +
                "componha mais títulos firmes antes.");

        if (amount > document.OpenAmount)
            throw new ApplicationException(
                $"O valor da baixa ({amount:N2}) excede o saldo em aberto do documento " +
                $"({document.OpenAmount:N2}).");
    }
}
```

- [ ] **Step 6: Ajustar o teste antigo**

Em `FinancialDocumentsSettleServiceTests.Refuses_to_settle_a_provisional_document`, trocar
`Assert.Contains("provisório", error.Message, StringComparison.OrdinalIgnoreCase);` por
`Assert.StartsWith("Valor acima do disponível para baixa", error.Message);`.

- [ ] **Step 7: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Financials"`
Expected: todos PASS.

- [ ] **Step 8: Stage**

```bash
git add SiagroB1.Application/Services/Financials/FinancialDocumentsRecalculateBalanceService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsSettlementGuardService.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsSettleServiceTests.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsRecalculateComposedTests.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsSettlementGuardFirmTests.cs
```
O controlador commita com:

```
feat(financial): provisório baixa só o composto e firme não recebe baixa em dinheiro

O recalculador passa a escrever o composto do provisório e a enxergar linhas ainda não salvas,
o que deixa composição, desfazer e encontro de contas num SaveChanges só. A baixa do provisório
fica limitada ao disponível (composto − baixado) e o firme da conciliação só sai por composição,
dação ou encontro de contas. O provisório continua Quitado só ao alcançar o valor contratado.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 5: Nascimento, ajuste e cancelamento do firme pela conciliação (regras 1–7) + interceptor

**Files:**
- Create: `SiagroB1.Infra/Queries/PurchaseFirmPayableRules.cs`
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsFirmSyncService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationService.cs` (construtor, `SaveAsync`, `RemoveAsync`)
- Modify: `SiagroB1.Web/Actions/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationActionsController.cs` (Remove passa o usuário)
- Modify: `SiagroB1.Infra/Interceptors/PurchaseFiscalReconciliationCleanupInterceptor.cs`
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsCancelService.cs` (`ExecuteAsync` recusa o firme da conciliação)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs` (~666)
- Modify (testes de suporte): `SiagroB1.Application.Tests/Support/FinancialDocumentTestServices.cs`, `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/FiscalReconciliationSeed.cs`, call sites em `SiagroB1.Application.Tests/PurchaseFiscalReconciliations/*.cs`
- Create (suporte): `SiagroB1.Application.Tests/Financials/FirmPayableSeed.cs`
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsFirmSyncTests.cs`

**Interfaces:**
- Consumes: Task 3 (colunas, enums), Task 4 (`RecalculateAsync` enxerga título não salvo).
- Produces:
  - `PurchaseFirmPayableRules` (Infra, estático): `const string LinkUndoneReason = "Vínculo fiscal desfeito"`, `const string SystemUserName = "Sistema"`, `decimal Amount(decimal linkedKg, decimal lineKg, decimal lineGrandTotal, decimal lineFunrural)`, `string ComposedRefusal(string? firmCode, string? provisionalCode)`, `string SettledRefusal(string? firmCode)`, `string SituationName(FinancialDocumentStatus, bool composed)`, `List<FinancialDocument> ActiveFirmsOf(DbContext, IReadOnlyCollection<Guid> linkKeys)`, `Guid? ProvisionalOf(DbContext, Guid composedKey)`, `void EnsureLoose(DbContext, FinancialDocument)`, `void CancelLooseFirms(DbContext, IReadOnlyCollection<Guid> linkKeys, string userName)`.
  - `FinancialDocumentsFirmSyncService(IUnitOfWork db, DocNumberSequenceService docNumberSequence, FinancialDocumentChangeLogService changeLog, IUsage usageService, IConfiguration configuration)`: `Task EnqueueSyncAsync(PurchaseFiscalReconciliation link, string userName)`, `void EnqueueRemoval(Guid linkKey, string userName)`, `Task<bool> CreatesFinancialDocumentAsync(int? usageCode)` (D10) — enqueue-only. (A Task 8 acrescenta o 6º parâmetro `FinancialDocumentsNettingService netting`; a Task 14, o método do complemento de valor.)
  - `PurchaseFiscalReconciliationService(IUnitOfWork db, FinancialDocumentsFirmSyncService firmSync)`; `RemoveAsync(Guid linkKey, string userName)`.
  - Testes: `FinancialDocumentTestServices.FirmSync(IUnitOfWork db, FakeDocNumberSequenceService? numbers = null, string erp = "STANDALONE")`; `FiscalReconciliationSeed.NewService(UnitOfWork db, FakeDocNumberSequenceService? numbers = null, string erp = "STANDALONE")`; `FirmPayableSeed` (`CreateAsync(decimal provisionalAmount = 100_000m, IInterceptor? interceptor = null, string erp = "STANDALONE")`, `ConciliateAsync(...)` → `Conciliation(Transaction, Invoice, Line, Link, Firm)`, `SaveLinkAsync(Conciliation, decimal kg)`, `AddProvisionalAsync(PurchaseContract, decimal, string)`, `ReloadAsync(Guid)`, `Reconciliation()`, `Context`, `Db`, `Numbers`, `Contract`, `Provisional`, `Erp`, `FinancialUsageCode`, `NonFinancialUsageCode`).

- [ ] **Step 1: Suporte de teste — fábricas e seed**

1. Trocar os construtores diretos da conciliação nos testes **antes** de criar o `NewService` (senão o sed o pegaria):

```bash
sed -i 's/new PurchaseFiscalReconciliationService(/FiscalReconciliationSeed.NewService(/g' SiagroB1.Application.Tests/PurchaseFiscalReconciliations/*.cs
sed -i 's/RemoveAsync(link.Key)/RemoveAsync(link.Key, "ana")/g' SiagroB1.Application.Tests/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationServiceTests.cs
grep -rn "new PurchaseFiscalReconciliationService(\|RemoveAsync(link.Key)" SiagroB1.Application.Tests
```
Expected do `grep`: nada.

2. `FiscalReconciliationSeed.cs` — usings `SiagroB1.Application.Services.PurchaseFiscalReconciliations`; método novo dentro da classe:

```csharp
    /// <summary>Serviço real da conciliação, com o sync do firme (spec 2026-10-13, títulos firmes).</summary>
    public static PurchaseFiscalReconciliationService NewService(
        UnitOfWork db, FakeDocNumberSequenceService? numbers = null, string erp = "STANDALONE") =>
        new(db, FinancialDocumentTestServices.FirmSync(db, numbers, erp));
```

3. `Support/FinancialDocumentTestServices.cs` — usings `Microsoft.Extensions.Logging.Abstractions` e `SiagroB1.Application.Services`; método novo:

```csharp
    /// <summary>
    /// Sync do firme da conciliação (spec 2026-10-13, títulos firmes), com numeração em memória. A natureza é lida pelo
    /// UsageService local (D10); em "SAPB1" o modo decide e a natureza nem é consultada.
    /// </summary>
    public static FinancialDocumentsFirmSyncService FirmSync(
        IUnitOfWork db, FakeDocNumberSequenceService? numbers = null, string erp = "STANDALONE") =>
        new(db, numbers ?? new FakeDocNumberSequenceService(), new FinancialDocumentChangeLogService(db.Context),
            new UsageService(db, NullLogger<UsageService>.Instance), TestConfiguration.Erp(erp));
```

4. `SiagroB1.Application.Tests/Financials/FirmPayableSeed.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Cenário dos títulos firmes de compra: contrato aprovado do fornecedor F0001 com um PROVISÓRIO (FN-PROV-1), conta
/// CX01, duas naturezas de entrada (uma gera financeiro, outra não — D10), e conciliações feitas pelo serviço REAL
/// (o firme nasce do sync). Linha padrão: 30.000 KG × R$ 2,00 = 60.000,00 com Funrural 815,00 → vínculo de
/// 10.000 KG = 10.000 × 59.185,00 ÷ 30.000 = 19.728,33.
/// </summary>
internal sealed class FirmPayableSeed
{
    public const decimal DefaultFirmAmount = 19_728.33m;

    public FiscalReconciliationSeed Fiscal { get; }
    public UnitOfWork Db => Fiscal.Db;
    public AppDbContext Context => Db.Context;
    public FakeDocNumberSequenceService Numbers { get; } = new();
    public PurchaseContract Contract { get; private set; } = null!;
    public FinancialDocument Provisional { get; private set; } = null!;
    public string Erp { get; private set; } = "STANDALONE";

    /// <summary>Natureza de entrada com "Gera financeiro" (D10).</summary>
    public int FinancialUsageCode { get; private set; }

    /// <summary>Natureza de entrada SEM "Gera financeiro".</summary>
    public int NonFinancialUsageCode { get; private set; }

    private FirmPayableSeed(FiscalReconciliationSeed fiscal) => Fiscal = fiscal;

    public static async Task<FirmPayableSeed> CreateAsync(
        decimal provisionalAmount = 100_000m, IInterceptor? interceptor = null, string erp = "STANDALONE")
    {
        var db = interceptor is null ? TestDb.CreateUnitOfWork() : TestDb.CreateUnitOfWork(interceptor);
        var seed = new FirmPayableSeed(await FiscalReconciliationSeed.CreateAsync(db)) { Erp = erp };
        seed.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl,
        });
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        seed.FinancialUsageCode = (await usages.CreateAsync(Usage("Compra para comercialização", createsFinancial: true))).Code;
        seed.NonFinancialUsageCode = (await usages.CreateAsync(Usage("Entrada sem financeiro", createsFinancial: false))).Code;
        seed.Contract = await seed.Fiscal.AddContractAsync();
        seed.Provisional = await seed.AddProvisionalAsync(seed.Contract, provisionalAmount, "FN-PROV-1");
        return seed;
    }

    private static UsageModel Usage(string name, bool createsFinancial) => new()
    {
        Name = name, Direction = UsageDirection.Incoming, CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
        CreatesFinancialDocument = createsFinancial,
    };

    public async Task<FinancialDocument> AddProvisionalAsync(PurchaseContract contract, decimal amount, string code)
    {
        var provisional = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = code, CardCode = FiscalReconciliationSeed.Supplier,
            Direction = FinancialDirection.Payable, Nature = FinancialDocumentNature.Provisional,
            Status = FinancialDocumentStatus.Open, DueDate = new DateTime(2026, 12, 31), NetAmount = amount,
            OriginType = FinancialDocumentOrigin.PurchaseContractPriceFixation, OriginKey = Guid.NewGuid(),
            OriginDocNumber = contract.Code, PurchaseContractKey = contract.Key, BranchCode = "01", Currency = CurrencyType.Brl,
        };
        Context.FinancialDocuments.Add(provisional);
        await Context.SaveChangesAsync();
        return provisional;
    }

    public PurchaseFiscalReconciliationService Reconciliation() => FiscalReconciliationSeed.NewService(Db, Numbers, Erp);

    /// <summary>Romaneio alocado + NF de uma linha, vinculados pelo serviço real. Firme = o ativo do vínculo (ou null).</summary>
    public async Task<Conciliation> ConciliateAsync(
        decimal kg = 10_000m, decimal lineKg = 30_000m, decimal unitPrice = 2m, decimal funrural = 815m,
        PurchaseInvoiceType invoiceType = PurchaseInvoiceType.Normal,
        StorageTransactionType transactionType = StorageTransactionType.Purchase,
        bool createsFinancialDocument = true, bool lineWithContract = true, Guid? originLineKey = null,
        PurchaseContract? contract = null, string taxNumber = "1001", bool withUsage = true)
    {
        var target = contract ?? Contract;
        var transaction = await Fiscal.AddTransactionAsync(kg, transactionType, target);
        var (invoice, line) = await Fiscal.AddInvoiceAsync(
            lineKg, type: invoiceType, contract: lineWithContract ? target : null, taxNumber: taxNumber);
        line.UnitPrice = unitPrice;
        line.FunruralInssValue = funrural;
        // D10: decide a NATUREZA da linha; a cópia gravada na linha fica false de propósito, para provar que não é lida.
        line.UsageCode = withUsage ? (createsFinancialDocument ? FinancialUsageCode : NonFinancialUsageCode) : null;
        line.CreatesFinancialDocument = false;
        line.PurchaseInvoiceItemOriginKey = originLineKey;
        await Db.SaveChangesAsync();

        await Reconciliation().SaveAsync(
            [new FiscalReconciliationPairDto { PurchaseInvoiceItemKey = line.Key!.Value, StorageTransactionKey = transaction.Key, Quantity = kg }],
            "ana");

        var link = await Context.PurchaseFiscalReconciliations.SingleAsync(l => l.StorageTransactionKey == transaction.Key);
        var firm = await Context.FinancialDocuments.SingleOrDefaultAsync(d =>
            d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation && d.OriginKey == link.Key &&
            d.Status != FinancialDocumentStatus.Canceled);
        return new Conciliation(transaction, invoice, line, link, firm);
    }

    /// <summary>Regrava o par da conciliação com outra quantidade (0 remove), pelo serviço real, como "bia".</summary>
    public Task SaveLinkAsync(Conciliation c, decimal kg) =>
        Reconciliation().SaveAsync(
            [new FiscalReconciliationPairDto { PurchaseInvoiceItemKey = c.Line.Key!.Value, StorageTransactionKey = c.Transaction.Key, Quantity = kg }],
            "bia");

    public async Task<FinancialDocument> ReloadAsync(Guid key) =>
        await Context.FinancialDocuments.AsNoTracking().SingleAsync(d => d.Key == key);
}

internal sealed record Conciliation(
    StorageTransaction Transaction, PurchaseInvoice Invoice, PurchaseInvoiceItem Line, PurchaseFiscalReconciliation Link,
    FinancialDocument? Firm);
```

`git add SiagroB1.Application.Tests/Financials/FirmPayableSeed.cs`

- [ ] **Step 2: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsFirmSyncTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Interceptors;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-13 (títulos firmes), regras 1–7: o firme nasce, muda e morre com o vínculo da conciliação.</summary>
public class FinancialDocumentsFirmSyncTests
{
    private const string Composed =
        "O título ST-0001 do vínculo já está composto no título FN-PROV-1: desfaça a composição antes de alterar a conciliação.";

    private static IQueryable<FinancialDocument> ActiveFirms(FirmPayableSeed seed) =>
        seed.Context.FinancialDocuments.AsNoTracking().Where(d =>
            d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation && d.Status != FinancialDocumentStatus.Canceled);

    [Fact]
    public async Task Firm_is_born_from_the_link_with_the_line_price_net_of_funrural()
    {
        var seed = await FirmPayableSeed.CreateAsync();

        var c = await seed.ConciliateAsync();

        var firm = Assert.IsType<FinancialDocument>(c.Firm);
        Assert.Equal(FirmPayableSeed.DefaultFirmAmount, firm.NetAmount);
        Assert.Equal(FinancialDocumentNature.Firm, firm.Nature);
        Assert.Equal(FinancialDirection.Payable, firm.Direction);
        Assert.Equal(FinancialDocumentStatus.Open, firm.Status);
        Assert.Equal(c.Link.Key, firm.OriginKey);
        Assert.Equal(FiscalReconciliationSeed.Supplier, firm.CardCode);
        Assert.Equal(seed.Contract.Key, firm.PurchaseContractKey);
        Assert.Equal("PC-1", firm.OriginDocNumber);
        Assert.Equal("01", firm.BranchCode);
        Assert.Equal(CurrencyType.Brl, firm.Currency);
        Assert.Equal(new DateTime(2026, 10, 9), firm.DocumentDate);
        Assert.Equal(new DateTime(2026, 10, 9), firm.DueDate);
        Assert.Equal(c.Invoice.Key, firm.PurchaseInvoiceKey);
        Assert.Equal(c.Transaction.Key, firm.StorageTransactionKey);
        Assert.Equal(c.Transaction.Code, firm.StorageTransactionCode);
        Assert.Equal("1001-1", firm.TaxDocumentNumber);
        Assert.Equal("ST-0001", firm.Code);
        Assert.Equal(new[] { TransactionCode.FinancialDocument }, seed.Numbers.RequestedCodes);
    }

    [Fact]
    public async Task Sapb1_creates_the_firm_at_gross_even_with_the_nature_flag_off()
    {
        // D10: em SAPB1 a natureza é o OUSG, sem a flag — conta como "gera". O Funrural da linha é zero: firme pelo bruto.
        var seed = await FirmPayableSeed.CreateAsync(erp: "SAPB1");

        var c = await seed.ConciliateAsync(funrural: 0m, createsFinancialDocument: false);

        Assert.Equal(20_000m, c.Firm!.NetAmount);
    }

    [Fact]
    public async Task Nature_without_the_creates_financial_flag_does_not_create_a_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync();

        var c = await seed.ConciliateAsync(createsFinancialDocument: false);

        Assert.Null(c.Firm);
        Assert.Empty(ActiveFirms(seed));
    }

    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task Line_without_nature_does_not_create_a_firm(string erp)
    {
        var seed = await FirmPayableSeed.CreateAsync(erp: erp);

        var c = await seed.ConciliateAsync(withUsage: false);

        Assert.Null(c.Firm);
    }

    [Fact]
    public async Task Nature_flag_turned_off_later_cancels_the_loose_firm_on_the_next_change()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        var usage = await seed.Context.Usages.SingleAsync(u => u.Code == seed.FinancialUsageCode);
        usage.CreatesFinancialDocument = false;
        await seed.Context.SaveChangesAsync();

        await seed.SaveLinkAsync(c, 15_000m);

        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(c.Firm!.Key)).Status);
    }

    [Fact]
    public async Task Line_without_contract_does_not_create_a_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync();

        var c = await seed.ConciliateAsync(lineWithContract: false);

        Assert.Null(c.Firm);
    }

    [Fact]
    public async Task Return_invoice_creates_a_purchase_return_title()
    {
        var seed = await FirmPayableSeed.CreateAsync();

        var c = await seed.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return,
            transactionType: StorageTransactionType.PurchaseReturn);

        Assert.Equal(FinancialDocumentNature.PurchaseReturn, c.Firm!.Nature);
        Assert.Equal(2_959.25m, c.Firm.NetAmount); // 1.500 × 59.185,00 ÷ 30.000
    }

    [Fact]
    public async Task Saving_the_same_link_again_keeps_one_firm_and_no_log()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await seed.SaveLinkAsync(c, 10_000m);

        Assert.Single(ActiveFirms(seed));
        Assert.Empty(seed.Context.FinancialDocumentChangeLogs.AsNoTracking());
    }

    [Fact]
    public async Task Changing_the_quantity_recalculates_the_loose_firm_and_logs_it()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await seed.SaveLinkAsync(c, 15_000m);

        var firm = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(29_592.50m, firm.NetAmount); // 15.000 × 59.185,00 ÷ 30.000
        var log = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking().SingleAsync();
        Assert.Equal(FinancialDocumentChangeLogFields.NetAmount, log.Field);
        Assert.Equal("19.728,33", log.OldValue);
        Assert.Equal("29.592,50", log.NewValue);
        Assert.Equal("bia", log.ChangedBy);
    }

    [Fact]
    public async Task Zero_quantity_cancels_the_loose_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await seed.SaveLinkAsync(c, 0m);

        var firm = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, firm.Status);
        Assert.Equal("Vínculo fiscal desfeito", firm.CancellationReason);
        Assert.Equal("bia", firm.CanceledBy);
    }

    [Fact]
    public async Task Removing_the_link_cancels_the_loose_firm_and_relinking_creates_a_new_one()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await seed.Reconciliation().RemoveAsync(c.Link.Key, "carla");

        var old = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, old.Status);
        Assert.Equal("carla", old.CanceledBy);

        await seed.SaveLinkAsync(c, 10_000m);

        var renewed = Assert.Single(ActiveFirms(seed));
        Assert.NotEqual(old.Key, renewed.Key);
        Assert.Equal(FirmPayableSeed.DefaultFirmAmount, renewed.NetAmount);
    }

    [Fact]
    public async Task Composed_firm_refuses_quantity_change_and_removal()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        seed.Context.FinancialDocumentCompositions.Add(new FinancialDocumentComposition
        {
            Key = Guid.NewGuid(), ProvisionalDocumentKey = seed.Provisional.Key, ComposedDocumentKey = c.Firm!.Key,
            Amount = c.Firm.NetAmount,
        });
        await seed.Context.SaveChangesAsync();

        var change = await Assert.ThrowsAsync<DefaultException>(() => seed.SaveLinkAsync(c, 15_000m));
        Assert.Equal(Composed, change.Message);

        seed.Context.ChangeTracker.Clear();
        var removal = await Assert.ThrowsAsync<DefaultException>(() => seed.Reconciliation().RemoveAsync(c.Link.Key, "carla"));
        Assert.Equal(Composed, removal.Message);

        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(c.Firm.Key)).Status);
    }

    [Fact]
    public async Task Firm_with_dation_or_netting_counts_as_not_loose()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        seed.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = c.Firm!.Key, Amount = 100m, SettlementDate = DateTime.Today,
            Origin = FinancialSettlementOrigin.Dation, Notes = "sobra",
        });
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(seed.Context, c.Firm.Key);
        await seed.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.SaveLinkAsync(c, 0m));

        Assert.Equal(
            "O título ST-0001 do vínculo já tem baixa por dação ou encontro de contas: estorne-a antes de alterar a conciliação.",
            error.Message);
    }

    [Fact]
    public async Task Cancelling_the_invoice_cancels_the_loose_firm_through_the_interceptor()
    {
        var seed = await FirmPayableSeed.CreateAsync(interceptor: new PurchaseFiscalReconciliationCleanupInterceptor());
        var c = await seed.ConciliateAsync();

        c.Invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        await seed.Db.SaveChangesAsync();

        var firm = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, firm.Status);
        Assert.Equal("Vínculo fiscal desfeito", firm.CancellationReason);
        Assert.Equal("Sistema", firm.CanceledBy);
    }

    [Fact]
    public async Task Cancelling_the_transaction_cancels_the_loose_firm_through_the_interceptor()
    {
        var seed = await FirmPayableSeed.CreateAsync(interceptor: new PurchaseFiscalReconciliationCleanupInterceptor());
        var c = await seed.ConciliateAsync();

        c.Transaction.TransactionStatus = StorageTransactionsStatus.Cancelled;
        await seed.Db.SaveChangesAsync();

        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(c.Firm!.Key)).Status);
        Assert.Empty(seed.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }

    [Fact]
    public async Task Manual_cancel_of_a_conciliation_firm_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsCancelService(seed.Db).ExecuteAsync(c.Firm!.Key, "teste", "ana"));

        Assert.Equal(
            "Título firme nasce e morre com o vínculo da Conciliação Fiscal de Compras: desfaça o vínculo para cancelá-lo.",
            error.Message);
    }
}
```

`git add` o arquivo.

- [ ] **Step 3: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsFirmSyncTests"`
Expected: erro de compilação (`FinancialDocumentsFirmSyncService`, `FiscalReconciliationSeed.NewService`, `RemoveAsync(Guid, string)` não existem).

- [ ] **Step 4: Regras puras no Infra**

`SiagroB1.Infra/Queries/PurchaseFirmPayableRules.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Regras puras do título firme de compra (spec 2026-10-13, títulos firmes, regras 4 e 6–7). Moram no Infra porque o
/// PurchaseFiscalReconciliationCleanupInterceptor — que não enxerga a Application — precisa cancelar ou recusar o firme
/// quando a NF ou o romaneio somem; os serviços da Application usam as mesmas. Síncronas de propósito: o interceptor
/// tem caminho síncrono, e as consultas são poucas e por chave.
/// </summary>
public static class PurchaseFirmPayableRules
{
    public const string LinkUndoneReason = "Vínculo fiscal desfeito";

    /// <summary>Usuário do cancelamento em cascata (NF/romaneio cancelado): o interceptor não sabe quem é.</summary>
    public const string SystemUserName = "Sistema";

    /// <summary>Regra 4: a parte da linha que cabe ao vínculo, sem o Funrural retido. Multiplica antes de dividir.</summary>
    public static decimal Amount(decimal linkedKg, decimal lineKg, decimal lineGrandTotal, decimal lineFunrural) =>
        lineKg <= 0m ? 0m : decimal.Round(linkedKg * (lineGrandTotal - lineFunrural) / lineKg, 2, MidpointRounding.ToEven);

    public static string ComposedRefusal(string? firmCode, string? provisionalCode) =>
        $"O título {firmCode} do vínculo já está composto no título {provisionalCode}: desfaça a composição antes de alterar a conciliação.";

    public static string SettledRefusal(string? firmCode) =>
        $"O título {firmCode} do vínculo já tem baixa por dação ou encontro de contas: estorne-a antes de alterar a conciliação.";

    /// <summary>Situação do firme na tela da conciliação.</summary>
    public static string SituationName(FinancialDocumentStatus status, bool composed) =>
        status == FinancialDocumentStatus.Canceled ? "Cancelado" : composed ? "Composto" : "Solto";

    /// <summary>
    /// Firmes/devoluções ATIVOS dos vínculos, rastreados. Confere o status de novo em memória: o mesmo SaveChanges pode
    /// já tê-los cancelado (estorno da alocação seguido do cancelamento do romaneio).
    /// </summary>
    public static List<FinancialDocument> ActiveFirmsOf(DbContext context, IReadOnlyCollection<Guid> linkKeys)
    {
        if (linkKeys.Count == 0) return [];

        return context.Set<FinancialDocument>()
            .Where(d => d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation &&
                        d.OriginKey != null && linkKeys.Contains(d.OriginKey.Value) &&
                        d.Status != FinancialDocumentStatus.Canceled)
            .ToList()
            .Where(d => d.Status != FinancialDocumentStatus.Canceled)
            .ToList();
    }

    /// <summary>Provisório em que o título está composto, contando linhas pendentes (Added conta, Deleted não). Null = solto.</summary>
    public static Guid? ProvisionalOf(DbContext context, Guid composedKey)
    {
        var tracked = context.ChangeTracker.Entries<FinancialDocumentComposition>()
            .Where(e => e.Entity.ComposedDocumentKey == composedKey)
            .ToList();

        var added = tracked.FirstOrDefault(e => e.State == EntityState.Added);
        if (added is not null) return added.Entity.ProvisionalDocumentKey;

        var deleted = tracked.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Key).ToHashSet();

        return context.Set<FinancialDocumentComposition>().AsNoTracking()
            .Where(c => c.ComposedDocumentKey == composedKey)
            .Select(c => new { c.Key, c.ProvisionalDocumentKey })
            .AsEnumerable()
            .Where(c => !deleted.Contains(c.Key))
            .Select(c => (Guid?)c.ProvisionalDocumentKey)
            .FirstOrDefault();
    }

    /// <summary>Regras 6–7: "solto" = não composto e sem baixa (dação e encontro de contas contam como baixa).</summary>
    public static void EnsureLoose(DbContext context, FinancialDocument firm)
    {
        if (ProvisionalOf(context, firm.Key) is { } provisionalKey)
        {
            var provisionalCode = context.Set<FinancialDocument>().AsNoTracking()
                .Where(d => d.Key == provisionalKey)
                .Select(d => d.Code)
                .FirstOrDefault();

            throw new DefaultException(ComposedRefusal(firm.Code, provisionalCode));
        }

        if (firm.SettledAmount != 0m)
            throw new DefaultException(SettledRefusal(firm.Code));
    }

    /// <summary>
    /// Regra 7: vínculo desfeito → o firme solto é CANCELADO (nunca apagado); um não-solto recusa a operação inteira,
    /// antes de qualquer mudança. Enqueue-only. Grava os carimbos à mão: o Infra não enxerga
    /// FinancialDocumentsCancelService.Cancel.
    /// </summary>
    public static void CancelLooseFirms(DbContext context, IReadOnlyCollection<Guid> linkKeys, string userName)
    {
        var firms = ActiveFirmsOf(context, linkKeys);
        foreach (var firm in firms)
            EnsureLoose(context, firm);

        var now = DateTime.Now;
        foreach (var firm in firms)
        {
            firm.Status = FinancialDocumentStatus.Canceled;
            firm.CancellationReason = LinkUndoneReason;
            firm.CanceledAt = now;
            firm.CanceledBy = userName;
            firm.UpdatedAt = now;
            firm.UpdatedBy = userName;
        }
    }
}
```

`git add SiagroB1.Infra/Queries/PurchaseFirmPayableRules.cs`

- [ ] **Step 5: Serviço de sync**

`SiagroB1.Application/Services/Financials/FinancialDocumentsFirmSyncService.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using R = SiagroB1.Infra.Queries.PurchaseFiscalReconciliationRules;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// O título FIRME acompanha o vínculo da Conciliação Fiscal de Compras (spec 2026-10-13, títulos firmes, regras 1–7):
/// nasce na gravação do vínculo (não na confirmação "Conciliado"), é recalculado se a quantidade mudar e é cancelado
/// se o vínculo sumir — composto, com dação ou com encontro de contas, recusa.
///
/// ENQUEUE-ONLY: o chamador (PurchaseFiscalReconciliationService) grava vínculo e título no MESMO SaveChanges.
/// ⚠️ Chame FORA de transação: DocNumberSequenceService roda por Dapper numa conexão própria, com UPDLOCK.
/// </summary>
public class FinancialDocumentsFirmSyncService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    FinancialDocumentChangeLogService changeLog,
    IUsage usageService,
    IConfiguration configuration)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Regra 2 / D10: "gera financeiro" da NATUREZA da linha, lida agora — não a cópia gravada na linha, que só o
    /// cálculo de tributos da NF-e preenche. Em SAPB1 a natureza é o OUSG, sem a flag: conta como gera, decidido pelo
    /// MODO (o UsageModel do SAP traz false, não nulo). Linha sem natureza não gera.
    /// </summary>
    public async Task<bool> CreatesFinancialDocumentAsync(int? usageCode)
    {
        if (usageCode is not { } code) return false;
        if (!ErpMode.IsStandalone(configuration)) return true;
        return (await usageService.GetByIdAsync(code))?.CreatesFinancialDocument == true;
    }

    /// <summary>Cria (um por vínculo), recalcula ou cancela o firme do vínculo gravado/alterado.</summary>
    public async Task EnqueueSyncAsync(PurchaseFiscalReconciliation link, string userName)
    {
        var context = db.Context;

        var line = await context.PurchaseInvoicesItems.AsNoTracking()
                       .Include(l => l.PurchaseInvoice)
                       .FirstOrDefaultAsync(l => l.Key == link.PurchaseInvoiceItemKey)
                   ?? throw new NotFoundException("Linha de documento de entrada não encontrada.");
        var invoice = line.PurchaseInvoice
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        var firm = PurchaseFirmPayableRules.ActiveFirmsOf(context, [link.Key]).FirstOrDefault();

        // Regra 2 / D10: só com "gera financeiro" na natureza da linha e contrato na linha. Sem isso (ou NF cancelada),
        // um firme que exista de antes segue o vínculo desfeito.
        if (line.PurchaseContractKey is null || invoice.InvoiceStatus == InvoiceStatus.Cancelled ||
            !await CreatesFinancialDocumentAsync(line.UsageCode))
        {
            if (firm is not null)
                PurchaseFirmPayableRules.CancelLooseFirms(context, [link.Key], userName);
            return;
        }

        var taxDocument = R.TaxDocument(invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.InvoiceNumber);
        var complement = line.ItemCode is null
            ? null
            : await context.ItemComplements.AsNoTracking().FirstOrDefaultAsync(c => c.ItemCode == line.ItemCode);
        var factor = R.KgFactor(line.UnitOfMeasureCode, complement)
                     ?? throw new DefaultException(
                         $"Cadastre o fator da unidade {line.UnitOfMeasureCode} no Complemento do Produto {line.ItemCode} para vincular a NF {taxDocument}.");

        // Regra 4. Em SAPB1 o Funrural da linha é zero: o firme sai pelo bruto.
        var amount = PurchaseFirmPayableRules.Amount(link.Quantity, line.Quantity * factor, line.GrandTotal, line.FunruralValue);

        if (firm is not null)
        {
            // Regravar sem mudar o valor não toca no título — nem recusa o composto.
            if (firm.NetAmount == amount) return;

            // Regra 6: só o firme solto é recalculado.
            PurchaseFirmPayableRules.EnsureLoose(context, firm);

            changeLog.Register(firm.Key, FinancialDocumentChangeLogFields.NetAmount,
                firm.NetAmount.ToString("N2", PtBr), amount.ToString("N2", PtBr), userName);
            firm.NetAmount = amount;
            firm.UpdatedAt = DateTime.Now;
            firm.UpdatedBy = userName;
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, firm.Key);
            return;
        }

        var contract = await context.PurchaseContracts.AsNoTracking()
                           .Where(c => c.Key == line.PurchaseContractKey)
                           .Select(c => new { c.Code, c.StandardCurrency })
                           .FirstOrDefaultAsync()
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");
        var transactionCode = await context.StorageTransactions.AsNoTracking()
            .Where(t => t.Key == link.StorageTransactionKey)
            .Select(t => t.Code)
            .FirstOrDefaultAsync();

        // Regra 5: número ANTES do SaveChanges (conexão Dapper própria, fora de transação).
        var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);
        var code = await docNumberSequence.GetDocNumber(docNumberKey);
        var issueDate = (invoice.IssueDate ?? DateTime.Today).Date;
        var now = DateTime.Now;

        var document = new FinancialDocument
        {
            Key = Guid.NewGuid(),
            Code = code,
            DocNumberKey = docNumberKey,
            BranchCode = invoice.BranchCode,
            Direction = FinancialDirection.Payable,
            // Regra 3: pelo sentido do documento — a conciliação já garantiu que casa com o romaneio (8/10/17 ou 9/16).
            Nature = invoice.InvoiceType == PurchaseInvoiceType.Return
                ? FinancialDocumentNature.PurchaseReturn
                : FinancialDocumentNature.Firm,
            Status = FinancialDocumentStatus.Open,
            CardCode = invoice.CardCode,
            CardName = invoice.CardName,
            // Firme não é pago direto: o vencimento que vale é o do provisório.
            DocumentDate = issueDate,
            DueDate = issueDate,
            Currency = contract.StandardCurrency ?? CurrencyType.Brl,
            NetAmount = amount,
            OriginType = FinancialDocumentOrigin.PurchaseFiscalReconciliation,
            OriginKey = link.Key,
            OriginDocNumber = contract.Code,
            PurchaseContractKey = line.PurchaseContractKey,
            PurchaseInvoiceKey = invoice.Key,
            StorageTransactionKey = link.StorageTransactionKey,
            StorageTransactionCode = transactionCode,
            TaxDocumentNumber = taxDocument,
            CreatedAt = now,
            CreatedBy = userName,
            UpdatedAt = now,
            UpdatedBy = userName,
        };

        context.FinancialDocuments.Add(document);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);
    }

    /// <summary>Regra 7: vínculo removido → cancela o firme solto ou recusa (S1/P1).</summary>
    public void EnqueueRemoval(Guid linkKey, string userName) =>
        PurchaseFirmPayableRules.CancelLooseFirms(db.Context, [linkKey], userName);
}
```

`git add` o arquivo. DI em `ServiceCollectionExtensions.cs`, depois de `services.AddScoped<FinancialAdvancesRelinkContractService>();`:

```csharp
        services.AddScoped<FinancialDocumentsFirmSyncService>();
```

- [ ] **Step 6: Conciliação chama o sync**

`PurchaseFiscalReconciliationService.cs`:

1. Using `SiagroB1.Application.Services.Financials`; construtor:

```csharp
public class PurchaseFiscalReconciliationService(IUnitOfWork db, FinancialDocumentsFirmSyncService firmSync)
```

2. Em `SaveAsync`, trocar o bloco que começa em `foreach (var pair in pairs)` (o do upsert, depois do teto da NF) até `await db.SaveChangesAsync();` por:

```csharp
        var removedLinks = new List<Guid>();
        var syncedLinks = new List<PurchaseFiscalReconciliation>();

        foreach (var pair in pairs)
        {
            var link = existing.FirstOrDefault(e =>
                e.PurchaseInvoiceItemKey == pair.PurchaseInvoiceItemKey && e.StorageTransactionKey == pair.StorageTransactionKey);

            if (pair.Quantity == 0)
            {
                if (link != null)
                {
                    db.Context.PurchaseFiscalReconciliations.Remove(link);
                    removedLinks.Add(link.Key);
                }
                continue;
            }

            if (link != null)
            {
                link.Quantity = pair.Quantity;
                syncedLinks.Add(link);
            }
            else
            {
                // Chave explícita: o firme aponta para ela (OriginKey) antes do SaveChanges.
                var created = new PurchaseFiscalReconciliation
                {
                    Key = Guid.NewGuid(),
                    PurchaseInvoiceItemKey = pair.PurchaseInvoiceItemKey, StorageTransactionKey = pair.StorageTransactionKey,
                    Quantity = pair.Quantity, CreatedAt = DateTime.Now, CreatedBy = userName,
                };
                db.Context.PurchaseFiscalReconciliations.Add(created);
                syncedLinks.Add(created);
            }
        }

        // Spec 2026-10-13 (títulos firmes, regras 1–7): o firme acompanha o vínculo, no MESMO SaveChanges. Recusa
        // (firme composto ou com baixa) derruba a gravação inteira.
        foreach (var key in removedLinks)
            firmSync.EnqueueRemoval(key, userName);
        foreach (var link in syncedLinks)
            await firmSync.EnqueueSyncAsync(link, userName);

        await ClearConfirmationAsync(transactionKeys);
        await db.SaveChangesAsync();
```

3. `RemoveAsync` completo:

```csharp
    public async Task RemoveAsync(Guid linkKey, string userName)
    {
        var link = await db.Context.PurchaseFiscalReconciliations.FirstOrDefaultAsync(l => l.Key == linkKey)
                   ?? throw new NotFoundException("Vínculo não encontrado.");

        db.Context.PurchaseFiscalReconciliations.Remove(link);
        firmSync.EnqueueRemoval(link.Key, userName);
        await ClearConfirmationAsync([link.StorageTransactionKey]);
        await db.SaveChangesAsync();
    }
```

4. `PurchaseFiscalReconciliationActionsController.cs`, ação `Remove`: trocar `await service.RemoveAsync(key);` por `await service.RemoveAsync(key, User.Identity?.Name ?? "Unknown");`.

- [ ] **Step 7: Interceptor**

`PurchaseFiscalReconciliationCleanupInterceptor.cs` — using `SiagroB1.Infra.Queries`; logo depois de

```csharp
        if (doomed.Count == 0)
            return;
```

inserir:

```csharp

        // Spec 2026-10-13 (títulos firmes, regra 7): o firme solto do vínculo é cancelado; o composto (ou com dação ou
        // encontro de contas) recusa a operação inteira — antes de remover qualquer vínculo.
        PurchaseFirmPayableRules.CancelLooseFirms(
            context, doomed.Select(l => l.Key).ToList(), PurchaseFirmPayableRules.SystemUserName);
```

- [ ] **Step 8: Cancelamento manual recusa o firme da conciliação**

`FinancialDocumentsCancelService.ExecuteAsync`, logo depois do `if (document.Status == FinancialDocumentStatus.Canceled) throw …já está cancelado`:

```csharp

        // Spec 2026-10-13 (títulos firmes): cancelar à mão deixaria o vínculo vivo sem título — e a próxima gravação
        // dele criaria outro. O caminho é desfazer o vínculo na Conciliação Fiscal de Compras.
        if (document.IsPurchaseFirmPayable)
            throw new ApplicationException(
                "Título firme nasce e morre com o vínculo da Conciliação Fiscal de Compras: desfaça o vínculo para cancelá-lo.");
```

- [ ] **Step 9: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsFirmSyncTests|FullyQualifiedName~PurchaseFiscalReconciliations|FullyQualifiedName~FinancialDocumentsCancelServiceTests"`
Expected: todos PASS (as linhas das sementes antigas da conciliação não têm natureza — `UsageCode` nulo —, então nenhum teste antigo passa a gerar firme).

- [ ] **Step 10: Stage**

```bash
dotnet build SiagroB1.sln
git add SiagroB1.Infra/Queries/PurchaseFirmPayableRules.cs SiagroB1.Infra/Interceptors/PurchaseFiscalReconciliationCleanupInterceptor.cs SiagroB1.Application/Services/Financials/FinancialDocumentsFirmSyncService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsCancelService.cs SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationService.cs SiagroB1.Web/Actions/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationActionsController.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests
```
O controlador commita com:

```
feat(financial): título firme nasce da conciliação fiscal de compras

Um firme por vínculo NF × romaneio, gravado junto com o vínculo: preço e Funrural da linha do
documento de entrada (não do contrato), só com "gera financeiro" na natureza da linha e contrato
na linha. A
quantidade alterada recalcula o firme solto; vínculo desfeito (inclusive NF ou romaneio
cancelado) cancela o firme solto. Firme composto, ou com dação ou encontro de contas, recusa a
alteração. Cancelar o firme à mão passa a ser recusado.

O "gera financeiro" vem da natureza da linha, lida na conciliação (D10); em SAPB1 conta como gera.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 6: Compor e desfazer a composição (regras 9–14)

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsComposeService.cs`
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsUncomposeService.cs`
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsReverseSettlementService.cs` (recusa o abatimento — regra 14)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsCompositionTests.cs`

**Interfaces:**
- Consumes: Task 3 (entidade de composição, `FinancialDocumentChangeLogFields.Composition`, `IsPurchaseFirmPayable`), Task 4 (`RecalculateAsync` com pendentes), Task 5 (`FirmPayableSeed`, `PurchaseFirmPayableRules`).
- Produces:
  - `FinancialDocumentsComposeService(IUnitOfWork db, FinancialDocumentChangeLogService changeLog)`: `Task ExecuteAsync(Guid provisionalKey, IReadOnlyList<Guid> composedKeys, string userName, CommitMode commitMode = CommitMode.Auto)`; `internal static string Describe(string? code, decimal amount)` (texto do log: `"{código}: {valor N2 pt-BR}"`).
  - `FinancialDocumentsUncomposeService(IUnitOfWork db)`: `Task ExecuteAsync(Guid compositionKey, string userName)`; `static Task EnqueueAsync(AppDbContext context, FinancialDocumentComposition composition, string userName)` (sem a checagem da regra 13 — usada pelo estorno de romaneio, Task 9, que já garantiu provisório sem baixa).

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsCompositionTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Interceptors;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-13 (títulos firmes), regras 9–14: composição manual no provisório e o desfazer.</summary>
public class FinancialDocumentsCompositionTests
{
    private static FinancialDocumentsComposeService Compose(FirmPayableSeed s) => new(s.Db, new FinancialDocumentChangeLogService(s.Context));
    private static FinancialDocumentsUncomposeService Uncompose(FirmPayableSeed s) => new(s.Db);
    private static FinancialDocumentsSettleService Settle(FirmPayableSeed s) => new(s.Db);

    private static Task SettleProvisionalAsync(FirmPayableSeed s, decimal amount) =>
        Settle(s).ExecuteAsync(s.Provisional.Key, "CX01", amount, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

    private static Task<Conciliation> ReturnAsync(FirmPayableSeed s, string taxNumber = "2001") =>
        s.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return,
            transactionType: StorageTransactionType.PurchaseReturn, taxNumber: taxNumber); // 2.959,25, sem origem

    [Fact]
    public async Task Composing_a_firm_writes_the_row_the_offset_and_both_logs()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        var row = await seed.Context.FinancialDocumentCompositions.AsNoTracking().SingleAsync();
        Assert.Equal(seed.Provisional.Key, row.ProvisionalDocumentKey);
        Assert.Equal(c.Firm.Key, row.ComposedDocumentKey);
        Assert.Equal(19_728.33m, row.Amount);

        var offset = await seed.Context.FinancialSettlements.AsNoTracking().SingleAsync(s => s.FinancialDocumentKey == c.Firm.Key);
        Assert.Equal(FinancialSettlementOrigin.InvoiceOffset, offset.Origin);
        Assert.Equal(19_728.33m, offset.Amount);
        Assert.Null(offset.FinancialAccountCode);
        Assert.Equal(seed.Provisional.Key, offset.RelatedDocumentKey);
        Assert.Equal(row.Key, offset.CompositionKey);
        Assert.Equal("FN-PROV-1", offset.DocumentReference);

        var provisional = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(19_728.33m, provisional.ComposedAmount);
        Assert.Equal(19_728.33m, provisional.AvailableToSettleAmount);
        Assert.Equal(0m, provisional.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, provisional.Status);

        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(0m, firm.OpenAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, firm.Status);

        var logs = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking().ToListAsync();
        Assert.All(logs, l => Assert.Equal(FinancialDocumentChangeLogFields.Composition, l.Field));
        Assert.Equal("ST-0001: 19.728,33", logs.Single(l => l.FinancialDocumentKey == seed.Provisional.Key).NewValue);
        Assert.Equal("FN-PROV-1: 19.728,33", logs.Single(l => l.FinancialDocumentKey == c.Firm.Key).NewValue);
        Assert.All(logs, l => Assert.Null(l.OldValue));
    }

    [Fact]
    public async Task Firm_that_does_not_fit_enters_partially()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 15_000m);
        var c = await seed.ConciliateAsync();

        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        Assert.Equal(15_000m, (await seed.Context.FinancialDocumentCompositions.AsNoTracking().SingleAsync()).Amount);
        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(4_728.33m, firm.OpenAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, firm.Status);
        Assert.Equal(15_000m, (await seed.ReloadAsync(seed.Provisional.Key)).ComposedAmount);
    }

    [Fact]
    public async Task Listed_order_decides_who_enters_first()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 25_000m);
        var c1 = await seed.ConciliateAsync(taxNumber: "1001");
        var c2 = await seed.ConciliateAsync(taxNumber: "1002");

        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c2.Firm!.Key, c1.Firm!.Key], "ana");

        var rows = await seed.Context.FinancialDocumentCompositions.AsNoTracking().ToListAsync();
        Assert.Equal(19_728.33m, rows.Single(r => r.ComposedDocumentKey == c2.Firm.Key).Amount);
        Assert.Equal(5_271.67m, rows.Single(r => r.ComposedDocumentKey == c1.Firm.Key).Amount);
    }

    [Fact]
    public async Task Fully_composed_provisional_refuses_another_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: FirmPayableSeed.DefaultFirmAmount);
        var c1 = await seed.ConciliateAsync(taxNumber: "1001");
        var c2 = await seed.ConciliateAsync(taxNumber: "1002");
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c1.Firm!.Key], "ana");

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Compose(seed).ExecuteAsync(seed.Provisional.Key, [c2.Firm!.Key], "ana"));

        Assert.Equal("O título FN-PROV-1 já está integralmente composto.", error.Message);
        seed.Context.ChangeTracker.Clear();
        Assert.Single(seed.Context.FinancialDocumentCompositions.AsNoTracking());
    }

    [Fact]
    public async Task Repeated_key_is_composed_once()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key, c.Firm.Key], "ana");

        Assert.Single(seed.Context.FinancialDocumentCompositions.AsNoTracking());
    }

    [Fact]
    public async Task Return_enters_negative_and_frees_space()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        var r = await ReturnAsync(seed);

        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key, r.Firm!.Key], "ana");

        Assert.Equal(-2_959.25m, (await seed.Context.FinancialDocumentCompositions.AsNoTracking()
            .SingleAsync(x => x.ComposedDocumentKey == r.Firm.Key)).Amount);
        Assert.Equal(16_769.08m, (await seed.ReloadAsync(seed.Provisional.Key)).ComposedAmount);
        var refund = await seed.ReloadAsync(r.Firm.Key);
        Assert.Equal(FinancialDocumentStatus.Settled, refund.Status);
        Assert.Equal(2_959.25m, (await seed.Context.FinancialSettlements.AsNoTracking()
            .SingleAsync(s => s.FinancialDocumentKey == r.Firm.Key)).Amount);
    }

    [Fact]
    public async Task Return_cannot_take_the_composed_below_the_settled()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");
        await SettleProvisionalAsync(seed, 19_000m);
        var r = await ReturnAsync(seed);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Compose(seed).ExecuteAsync(seed.Provisional.Key, [r.Firm!.Key], "ana"));

        Assert.Equal(
            "A devolução ST-0002 deixaria o composto do título FN-PROV-1 abaixo do valor já baixado: estorne a baixa antes.",
            error.Message);
    }

    [Fact]
    public async Task Firm_of_another_contract_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var other = await seed.Fiscal.AddContractAsync("PC-2");
        var c = await seed.ConciliateAsync(contract: other);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana"));

        Assert.Equal("O título ST-0001 é de outro contrato.", error.Message);
    }

    [Fact]
    public async Task Uncompose_mirrors_the_offset_and_frees_the_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");
        var row = await seed.Context.FinancialDocumentCompositions.AsNoTracking().SingleAsync();
        var offset = await seed.Context.FinancialSettlements.AsNoTracking().SingleAsync();

        await Uncompose(seed).ExecuteAsync(row.Key, "bia");

        Assert.Empty(seed.Context.FinancialDocumentCompositions.AsNoTracking());
        var reversal = await seed.Context.FinancialSettlements.AsNoTracking()
            .SingleAsync(s => s.Origin == FinancialSettlementOrigin.Reversal);
        Assert.Equal(c.Firm.Key, reversal.FinancialDocumentKey);
        Assert.Equal(-19_728.33m, reversal.Amount);
        Assert.Equal(offset.Key, reversal.ReversedSettlementKey);
        Assert.Equal("Composição desfeita", reversal.Notes);

        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(0m, firm.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, firm.Status);
        Assert.Equal(0m, (await seed.ReloadAsync(seed.Provisional.Key)).ComposedAmount);

        var undoLogs = await seed.Context.FinancialDocumentChangeLogs.AsNoTracking().Where(l => l.NewValue == null).ToListAsync();
        Assert.Equal(2, undoLogs.Count);
        Assert.Contains(undoLogs, l => l.FinancialDocumentKey == seed.Provisional.Key && l.OldValue == "ST-0001: 19.728,33");
    }

    [Fact]
    public async Task Uncompose_is_refused_while_the_settlement_needs_that_composition()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");
        await SettleProvisionalAsync(seed, 10_000m);
        var row = await seed.Context.FinancialDocumentCompositions.AsNoTracking().SingleAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Uncompose(seed).ExecuteAsync(row.Key, "bia"));

        Assert.Equal("Estorne a baixa do título FN-PROV-1 antes de desfazer a composição.", error.Message);
    }

    [Fact]
    public async Task Generic_reversal_of_an_offset_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");
        var offset = await seed.Context.FinancialSettlements.AsNoTracking().SingleAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsReverseSettlementService(seed.Db).ExecuteAsync(offset.Key, "errado", "ana"));

        Assert.Equal("Abatimento de composição se desfaz pela composição.", error.Message);
    }

    [Fact]
    public async Task Resaving_a_composed_link_with_same_quantity_is_accepted()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        await seed.SaveLinkAsync(c, 10_000m);

        Assert.Single(seed.Context.FinancialDocumentCompositions.AsNoTracking());
        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(c.Firm.Key)).Status);
    }

    [Fact]
    public async Task Cancelling_the_transaction_of_a_composed_firm_is_refused_by_the_interceptor()
    {
        var seed = await FirmPayableSeed.CreateAsync(interceptor: new PurchaseFiscalReconciliationCleanupInterceptor());
        var c = await seed.ConciliateAsync();
        await Compose(seed).ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        c.Transaction.TransactionStatus = StorageTransactionsStatus.Cancelled;
        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync());

        Assert.Equal(
            "O título ST-0001 do vínculo já está composto no título FN-PROV-1: desfaça a composição antes de alterar a conciliação.",
            error.Message);
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsCompositionTests"`
Expected: erro de compilação (serviços de compor/desfazer não existem).

- [ ] **Step 3: Compor**

`SiagroB1.Application/Services/Financials/FinancialDocumentsComposeService.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Compõe (aglutina) títulos firmes e de devolução no PROVISÓRIO de uma fixação (spec 2026-10-13, títulos firmes,
/// regras 9–12). Manual: quem decide em qual provisório cada firme entra é o operador, na ordem em que listou.
///
/// Firme entra por min(saldo do firme, espaço), espaço = valor do provisório − composto; sem espaço nenhum, recusa.
/// Devolução entra NEGATIVA pelo seu saldo (libera espaço), desde que o composto não fique abaixo do já baixado.
/// Tudo num SaveChanges: linha de composição, abatimento (InvoiceOffset) no título composto, recálculo e log nos dois.
/// </summary>
public class FinancialDocumentsComposeService(IUnitOfWork db, FinancialDocumentChangeLogService changeLog)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task ExecuteAsync(
        Guid provisionalKey, IReadOnlyList<Guid> composedKeys, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        var context = db.Context;

        var provisional = await context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == provisionalKey)
                          ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (provisional.Nature != FinancialDocumentNature.Provisional)
            throw new ApplicationException($"O título {provisional.Code} não é provisório.");

        if (provisional.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O título {provisional.Code} está cancelado.");

        // Distinct mantém a ordem da primeira ocorrência: duplo clique ou chave repetida compõe uma vez só.
        var keys = composedKeys.Distinct().ToList();
        if (keys.Count == 0)
            throw new ApplicationException("Selecione ao menos um título firme ou de devolução.");

        var documents = await context.FinancialDocuments.Where(x => keys.Contains(x.Key)).ToListAsync();
        if (documents.Count != keys.Count)
            throw new NotFoundException("Documento financeiro não encontrado.");

        var composed = provisional.ComposedAmount;

        foreach (var key in keys)
        {
            var document = documents.Single(x => x.Key == key);
            EnsureComposable(provisional, document);

            decimal amount;
            if (document.Nature == FinancialDocumentNature.Firm)
            {
                var space = decimal.Round(provisional.NetAmount - composed, 2, MidpointRounding.ToEven);
                if (space <= 0m)
                    throw new ApplicationException($"O título {provisional.Code} já está integralmente composto.");

                amount = Math.Min(document.OpenAmount, space);
            }
            else
            {
                amount = -document.OpenAmount;
                if (composed + amount < provisional.SettledAmount)
                    throw new ApplicationException(
                        $"A devolução {document.Code} deixaria o composto do título {provisional.Code} abaixo do valor já baixado: estorne a baixa antes.");
            }

            composed += amount;
            Enqueue(provisional, document, amount, userName);
        }

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, provisional.Key);
        foreach (var document in documents)
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);

        if (commitMode == CommitMode.Auto)
            await db.SaveChangesAsync();
    }

    /// <summary>Texto do log de composição nos dois títulos: "código do outro: valor assinado N2 pt-BR".</summary>
    internal static string Describe(string? code, decimal amount) => $"{code}: {amount.ToString("N2", PtBr)}";

    /// <summary>Regra 9: mesmo CONTRATO (não o mesmo CardCode — produtor com várias IEs é vários parceiros).</summary>
    private static void EnsureComposable(FinancialDocument provisional, FinancialDocument document)
    {
        if (!document.IsPurchaseFirmPayable)
            throw new ApplicationException($"O título {document.Code} não é firme nem devolução da conciliação fiscal.");

        if (document.PurchaseContractKey != provisional.PurchaseContractKey)
            throw new ApplicationException($"O título {document.Code} é de outro contrato.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O título {document.Code} está cancelado.");

        if (document.OpenAmount <= 0m)
            throw new ApplicationException($"O título {document.Code} não tem saldo a compor.");
    }

    private void Enqueue(FinancialDocument provisional, FinancialDocument document, decimal amount, string userName)
    {
        var now = DateTime.Now;

        var composition = new FinancialDocumentComposition
        {
            Key = Guid.NewGuid(),
            ProvisionalDocumentKey = provisional.Key,
            ComposedDocumentKey = document.Key,
            Amount = amount,
            CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
        };
        db.Context.FinancialDocumentCompositions.Add(composition);

        // Regra 12: o abatimento no TÍTULO COMPOSTO, sem conta, sempre positivo (devolução abate o próprio saldo).
        db.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = document.Key,
            FinancialAccountCode = null,
            SettlementDate = now.Date,
            Amount = Math.Abs(amount),
            Origin = FinancialSettlementOrigin.InvoiceOffset,
            RelatedDocumentKey = provisional.Key,
            CompositionKey = composition.Key,
            DocumentReference = provisional.Code,
            CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
        });

        changeLog.Register(provisional.Key, FinancialDocumentChangeLogFields.Composition, null, Describe(document.Code, amount), userName);
        changeLog.Register(document.Key, FinancialDocumentChangeLogFields.Composition, null, Describe(provisional.Code, amount), userName);
    }
}
```

`git add` o arquivo.

- [ ] **Step 4: Desfazer**

`SiagroB1.Application/Services/Financials/FinancialDocumentsUncomposeService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Desfaz UMA linha de composição (spec 2026-10-13, títulos firmes, regra 13): grava no título composto o estorno
/// espelho do abatimento, REMOVE a linha de composição, recalcula os dois e loga nos dois. O estorno genérico recusa
/// abatimento (regra 14) — este é o único caminho.
/// </summary>
public class FinancialDocumentsUncomposeService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid compositionKey, string userName)
    {
        var composition = await db.Context.FinancialDocumentCompositions.FirstOrDefaultAsync(x => x.Key == compositionKey)
                          ?? throw new NotFoundException("Composição não encontrada.");

        var provisional = await db.Context.FinancialDocuments.FirstAsync(x => x.Key == composition.ProvisionalDocumentKey);

        // Sem a linha, o composto não pode ficar abaixo do que já foi pago no provisório.
        if (provisional.ComposedAmount - composition.Amount < provisional.SettledAmount)
            throw new ApplicationException($"Estorne a baixa do título {provisional.Code} antes de desfazer a composição.");

        await EnqueueAsync(db.Context, composition, userName);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Enqueue-only e SEM a checagem da regra 13: o estorno de romaneio (FinancialDocumentsFirmUnwind) só chega aqui
    /// depois de garantir que o provisório não tem baixa.
    /// </summary>
    public static async Task EnqueueAsync(AppDbContext context, FinancialDocumentComposition composition, string userName)
    {
        var offset = await context.FinancialSettlements.FirstOrDefaultAsync(x =>
                         x.CompositionKey == composition.Key && x.Origin == FinancialSettlementOrigin.InvoiceOffset)
                     ?? throw new ApplicationException("Abatimento da composição não encontrado.");

        var provisional = await context.FinancialDocuments.FindAsync(composition.ProvisionalDocumentKey)
                          ?? throw new NotFoundException("Documento financeiro não encontrado.");
        var composed = await context.FinancialDocuments.FindAsync(composition.ComposedDocumentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        var now = DateTime.Now;
        context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = offset.FinancialDocumentKey,
            FinancialAccountCode = null,
            SettlementDate = now.Date,
            Amount = -offset.Amount,
            Origin = FinancialSettlementOrigin.Reversal,
            ReversedSettlementKey = offset.Key,
            RelatedDocumentKey = composition.ProvisionalDocumentKey,
            CompositionKey = composition.Key,
            DocumentReference = offset.DocumentReference,
            Notes = "Composição desfeita",
            CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
        });

        context.FinancialDocumentCompositions.Remove(composition);

        var changeLog = new FinancialDocumentChangeLogService(context);
        changeLog.Register(provisional.Key, FinancialDocumentChangeLogFields.Composition,
            FinancialDocumentsComposeService.Describe(composed.Code, composition.Amount), null, userName);
        changeLog.Register(composed.Key, FinancialDocumentChangeLogFields.Composition,
            FinancialDocumentsComposeService.Describe(provisional.Code, composition.Amount), null, userName);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, provisional.Key);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, composed.Key);
    }
}
```

`git add` o arquivo.

- [ ] **Step 5: Estorno genérico recusa abatimento**

`FinancialDocumentsReverseSettlementService.ExecuteAsync`, logo depois de
`if (settlement.Origin == FinancialSettlementOrigin.Reversal) throw new ApplicationException("Não é possível estornar um estorno.");`:

```csharp

        // Regra 14 (spec 2026-10-13, títulos firmes): o abatimento nasce da composição e só ela o desfaz (remove a
        // linha de composição junto); estornar por aqui deixaria a composição órfã.
        if (settlement.Origin == FinancialSettlementOrigin.InvoiceOffset)
            throw new ApplicationException("Abatimento de composição se desfaz pela composição.");
```

- [ ] **Step 6: DI**

`ServiceCollectionExtensions.cs`, depois de `services.AddScoped<FinancialDocumentsFirmSyncService>();`:

```csharp
        services.AddScoped<FinancialDocumentsComposeService>();
        services.AddScoped<FinancialDocumentsUncomposeService>();
```

- [ ] **Step 7: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsCompositionTests|FullyQualifiedName~FinancialDocumentsSettleServiceTests|FullyQualifiedName~FinancialDocumentsContractCancellationGuardServiceTests"`
Expected: todos PASS.

- [ ] **Step 8: Stage**

```bash
git add SiagroB1.Application/Services/Financials/FinancialDocumentsComposeService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsUncomposeService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsReverseSettlementService.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsCompositionTests.cs
```
O controlador commita com:

```
feat(financial): compor títulos firmes no provisório e desfazer a composição

O operador aglutina firmes e devoluções do mesmo contrato no provisório da fixação, na ordem
que escolher: o firme entra até o espaço que resta (parcial se não couber), a devolução entra
negativa. Cada composição grava a linha, o abatimento no título composto e o log nos dois.
Desfazer grava o estorno espelho e é recusado se o provisório já pagou o que ela cobre; o
estorno genérico não desfaz abatimento.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 7: Dação (regra 17)

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsDationService.cs`
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsDationTests.cs`

**Interfaces:**
- Consumes: Tasks 3–6.
- Produces: `FinancialDocumentsDationService(IUnitOfWork db)`: `Task<FinancialSettlement> ExecuteAsync(Guid documentKey, decimal amount, DateTime settlementDate, string? notes, string userName)`.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsDationTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Regra 17: a sobra de um firme (NF acima do contrato) sai por dação, pelo operador, com motivo.</summary>
public class FinancialDocumentsDationTests
{
    private static FinancialDocumentsDationService Dation(FirmPayableSeed s) => new(s.Db);

    [Fact]
    public async Task Dation_settles_part_of_a_firm_without_account()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        var line = await Dation(seed).ExecuteAsync(c.Firm!.Key, 1_000m, new DateTime(2026, 10, 20), "Sobra faturada acima do contrato", "ana");

        Assert.Equal(FinancialSettlementOrigin.Dation, line.Origin);
        Assert.Null(line.FinancialAccountCode);
        Assert.Equal(1_000m, line.Amount);
        Assert.Equal(new DateTime(2026, 10, 20), line.SettlementDate);
        Assert.Equal("Sobra faturada acima do contrato", line.Notes);
        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(18_728.33m, firm.OpenAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, firm.Status);
    }

    [Fact]
    public async Task Partial_composition_plus_dation_closes_the_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 15_000m);
        var c = await seed.ConciliateAsync();
        await new FinancialDocumentsComposeService(seed.Db, new FinancialDocumentChangeLogService(seed.Context))
            .ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        await Dation(seed).ExecuteAsync(c.Firm.Key, 4_728.33m, DateTime.Today, "Faturado acima do contrato", "ana");

        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(c.Firm.Key)).Status);
    }

    [Fact]
    public async Task Dation_is_reversed_by_the_generic_reversal()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        var line = await Dation(seed).ExecuteAsync(c.Firm!.Key, 1_000m, DateTime.Today, "Sobra", "ana");

        await new FinancialDocumentsReverseSettlementService(seed.Db).ExecuteAsync(line.Key, "lançada errada", "ana");

        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(0m, firm.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, firm.Status);
    }

    [Fact]
    public async Task Dation_is_refused_on_provisional_and_on_return()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var r = await seed.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return,
            transactionType: StorageTransactionType.PurchaseReturn);

        foreach (var key in new[] { seed.Provisional.Key, r.Firm!.Key })
        {
            var error = await Assert.ThrowsAsync<ApplicationException>(() =>
                Dation(seed).ExecuteAsync(key, 10m, DateTime.Today, "Sobra", "ana"));
            Assert.Equal("Dação só se aplica a título firme da conciliação fiscal.", error.Message);
        }
    }

    [Fact]
    public async Task Dation_requires_a_reason_and_a_positive_value_within_the_open_amount()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        Assert.Equal("Informe o motivo da dação.", (await Assert.ThrowsAsync<ApplicationException>(() =>
            Dation(seed).ExecuteAsync(c.Firm!.Key, 10m, DateTime.Today, "   ", "ana"))).Message);
        Assert.Equal("O valor da dação deve ser maior que zero.", (await Assert.ThrowsAsync<ApplicationException>(() =>
            Dation(seed).ExecuteAsync(c.Firm!.Key, 0m, DateTime.Today, "Sobra", "ana"))).Message);
        Assert.Equal("O valor da dação (20.000,00) excede o saldo em aberto do título (19.728,33).",
            (await Assert.ThrowsAsync<ApplicationException>(() =>
                Dation(seed).ExecuteAsync(c.Firm!.Key, 20_000m, DateTime.Today, "Sobra", "ana"))).Message);
        Assert.Empty(seed.Context.FinancialSettlements.AsNoTracking());
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsDationTests"`
Expected: erro de compilação (`FinancialDocumentsDationService` não existe).

- [ ] **Step 3: Serviço**

`SiagroB1.Application/Services/Financials/FinancialDocumentsDationService.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Dação (spec 2026-10-13, títulos firmes, regra 17 e D4): a composição para no valor do provisório; a sobra de um
/// firme faturado acima do contrato é baixada pelo OPERADOR, com motivo, sem conta e sem dinheiro. Só em firme da
/// conciliação (não em devolução nem provisório). Estornável pelo estorno genérico.
/// </summary>
public class FinancialDocumentsDationService(IUnitOfWork db)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<FinancialSettlement> ExecuteAsync(
        Guid documentKey, decimal amount, DateTime settlementDate, string? notes, string userName)
    {
        var document = await db.Context.FinancialDocuments.FirstOrDefaultAsync(x => x.Key == documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (!document.IsPurchaseFirmPayable || document.Nature != FinancialDocumentNature.Firm)
            throw new ApplicationException("Dação só se aplica a título firme da conciliação fiscal.");

        if (document.Status == FinancialDocumentStatus.Canceled)
            throw new ApplicationException($"O título {document.Code} está cancelado.");

        if (string.IsNullOrWhiteSpace(notes))
            throw new ApplicationException("Informe o motivo da dação.");

        var value = decimal.Round(amount, 2, MidpointRounding.ToEven);
        if (value <= 0m)
            throw new ApplicationException("O valor da dação deve ser maior que zero.");

        if (value > document.OpenAmount)
            throw new ApplicationException(
                $"O valor da dação ({value.ToString("N2", PtBr)}) excede o saldo em aberto do título ({document.OpenAmount.ToString("N2", PtBr)}).");

        var now = DateTime.Now;
        var settlement = new FinancialSettlement
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = document.Key,
            FinancialAccountCode = null,
            SettlementDate = settlementDate.Date,
            Amount = value,
            Origin = FinancialSettlementOrigin.Dation,
            Notes = notes.Trim(),
            CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
        };

        db.Context.FinancialSettlements.Add(settlement);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, document.Key);
        await db.SaveChangesAsync();

        return settlement;
    }
}
```

`git add` o arquivo. DI, depois de `services.AddScoped<FinancialDocumentsUncomposeService>();`:

```csharp
        services.AddScoped<FinancialDocumentsDationService>();
```

- [ ] **Step 4: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsDationTests"`
Expected: todos PASS.

- [ ] **Step 5: Stage**

```bash
git add SiagroB1.Application/Services/Financials/FinancialDocumentsDationService.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsDationTests.cs
```
O controlador commita com:

```
feat(financial): dação da sobra do título firme

A composição para no valor do provisório; o que o produtor faturou acima do contrato fica no
firme e o operador o baixa por dação, com motivo e sem conta. Só vale para firme da conciliação
e é estornável pelo estorno comum.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 8: Devolução — encontro de contas automático, manual e estorno do par (regras 8, 18–21)

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsNettingService.cs`
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsFirmSyncService.cs` (4º parâmetro e chamada da compensação automática)
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsReverseSettlementService.cs` (arquivo completo abaixo)
- Modify: `SiagroB1.Application.Tests/Support/FinancialDocumentTestServices.cs` (`FirmSync` passa o netting)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsNettingTests.cs`

**Interfaces:**
- Consumes: Tasks 3–6; `PurchaseFirmPayableRules.ProvisionalOf`.
- Produces:
  - `FinancialDocumentsNettingService(IUnitOfWork db)`: `Task EnqueueAutoAsync(FinancialDocument returnDocument, Guid? originLineKey, string userName)` (enqueue-only), `Task ExecuteAsync(Guid returnKey, Guid firmKey, string userName)` (salva).
  - `FinancialDocumentsFirmSyncService(IUnitOfWork db, DocNumberSequenceService docNumberSequence, FinancialDocumentChangeLogService changeLog, IUsage usageService, IConfiguration configuration, FinancialDocumentsNettingService netting)`.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsNettingTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Regras 18–21: a devolução compensa o firme de origem solto; senão fica em aberto para o operador.</summary>
public class FinancialDocumentsNettingTests
{
    private static FinancialDocumentsNettingService Netting(FirmPayableSeed s) => new(s.Db);

    private static Task<Conciliation> ReturnAsync(FirmPayableSeed s, Guid? originLineKey, decimal kg = 1_500m) =>
        s.ConciliateAsync(kg: kg, invoiceType: PurchaseInvoiceType.Return, transactionType: StorageTransactionType.PurchaseReturn,
            originLineKey: originLineKey, taxNumber: "2001");

    [Fact]
    public async Task Return_born_with_a_loose_origin_is_netted_automatically()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        var r = await ReturnAsync(seed, c.Line.Key);

        var refund = await seed.ReloadAsync(r.Firm!.Key);
        Assert.Equal(2_959.25m, refund.NetAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, refund.Status);
        var pair = await seed.Context.FinancialSettlements.AsNoTracking()
            .Where(s => s.Origin == FinancialSettlementOrigin.Netting).ToListAsync();
        Assert.Equal(2, pair.Count);
        Assert.All(pair, s => Assert.Equal(2_959.25m, s.Amount));
        Assert.All(pair, s => Assert.Null(s.FinancialAccountCode));
        Assert.Equal(c.Firm!.Key, pair.Single(s => s.FinancialDocumentKey == refund.Key).RelatedDocumentKey);
        Assert.Equal(refund.Key, pair.Single(s => s.FinancialDocumentKey == c.Firm.Key).RelatedDocumentKey);
        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(16_769.08m, firm.OpenAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, firm.Status);
    }

    [Fact]
    public async Task Composed_origin_leaves_the_return_open()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 15_000m); // origem composta, mas ainda com saldo
        var c = await seed.ConciliateAsync();
        await new FinancialDocumentsComposeService(seed.Db, new FinancialDocumentChangeLogService(seed.Context))
            .ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        var r = await ReturnAsync(seed, c.Line.Key);

        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(r.Firm!.Key)).Status);
        Assert.Empty(seed.Context.FinancialSettlements.AsNoTracking().Where(s => s.Origin == FinancialSettlementOrigin.Netting));
    }

    [Fact]
    public async Task Return_without_origin_stays_open()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        await seed.ConciliateAsync();

        var r = await ReturnAsync(seed, originLineKey: null);

        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(r.Firm!.Key)).Status);
    }

    [Fact]
    public async Task Oldest_origin_is_netted_first()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c1 = await seed.ConciliateAsync();
        var tx2 = await seed.Fiscal.AddTransactionAsync(10_000m, contract: seed.Contract);
        await seed.Reconciliation().SaveAsync(
            [new FiscalReconciliationPairDto { PurchaseInvoiceItemKey = c1.Line.Key!.Value, StorageTransactionKey = tx2.Key, Quantity = 10_000m }],
            "ana");
        var older = await seed.Context.FinancialDocuments.SingleAsync(d => d.StorageTransactionKey == tx2.Key);
        older.CreatedAt = new DateTime(2026, 10, 1);
        c1.Firm!.CreatedAt = new DateTime(2026, 10, 2);
        await seed.Context.SaveChangesAsync();

        var r = await ReturnAsync(seed, c1.Line.Key, kg: 15_000m); // 29.592,50

        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(older.Key)).Status);       // 19.728,33 inteiro
        Assert.Equal(9_864.16m, (await seed.ReloadAsync(c1.Firm.Key)).OpenAmount);                      // 19.728,33 − 9.864,17
        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(r.Firm!.Key)).Status);
    }

    [Fact]
    public async Task Manual_netting_compensates_a_loose_firm_of_the_same_contract()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        var r = await ReturnAsync(seed, originLineKey: null);

        await Netting(seed).ExecuteAsync(r.Firm!.Key, c.Firm!.Key, "ana");

        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(r.Firm.Key)).Status);
        Assert.Equal(16_769.08m, (await seed.ReloadAsync(c.Firm.Key)).OpenAmount);
    }

    [Fact]
    public async Task Manual_netting_refusals()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 15_000m); // firme composto, ainda com saldo 4.728,33
        var c = await seed.ConciliateAsync();
        await new FinancialDocumentsComposeService(seed.Db, new FinancialDocumentChangeLogService(seed.Context))
            .ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");
        var r = await ReturnAsync(seed, originLineKey: null);

        Assert.Equal("Compensar com firme só vale para título de devolução.",
            (await Assert.ThrowsAsync<ApplicationException>(() => Netting(seed).ExecuteAsync(c.Firm.Key, r.Firm!.Key, "ana"))).Message);
        Assert.Equal("O título FN-PROV-1 não é um título firme da conciliação fiscal.",
            (await Assert.ThrowsAsync<ApplicationException>(() => Netting(seed).ExecuteAsync(r.Firm!.Key, seed.Provisional.Key, "ana"))).Message);
        Assert.Equal("O título ST-0001 está composto no título FN-PROV-1: só firme solto compensa a devolução.",
            (await Assert.ThrowsAsync<ApplicationException>(() => Netting(seed).ExecuteAsync(r.Firm!.Key, c.Firm.Key, "ana"))).Message);
    }

    [Fact]
    public async Task Reversing_one_netting_line_reverses_the_pair()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        var r = await ReturnAsync(seed, c.Line.Key);
        var line = await seed.Context.FinancialSettlements.AsNoTracking()
            .SingleAsync(s => s.Origin == FinancialSettlementOrigin.Netting && s.FinancialDocumentKey == r.Firm!.Key);

        await new FinancialDocumentsReverseSettlementService(seed.Db).ExecuteAsync(line.Key, "devolução errada", "ana");

        var reversals = await seed.Context.FinancialSettlements.AsNoTracking()
            .Where(s => s.Origin == FinancialSettlementOrigin.Reversal).ToListAsync();
        Assert.Equal(2, reversals.Count);
        Assert.All(reversals, s => Assert.Equal(-2_959.25m, s.Amount));
        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(r.Firm!.Key)).Status);
        var firm = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(0m, firm.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, firm.Status);
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsNettingTests"`
Expected: erro de compilação (`FinancialDocumentsNettingService` não existe).

- [ ] **Step 3: Serviço de encontro de contas**

`SiagroB1.Application/Services/Financials/FinancialDocumentsNettingService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Encontro de contas entre a DEVOLUÇÃO e o firme normal (spec 2026-10-13, títulos firmes, regras 18–21): uma baixa
/// <see cref="FinancialSettlementOrigin.Netting"/> em CADA título, sem conta, pelo menor dos dois saldos, cada uma com
/// <c>RelatedDocumentKey</c> apontando a outra ponta. Estornar uma das duas estorna o par
/// (FinancialDocumentsReverseSettlementService).
/// </summary>
public class FinancialDocumentsNettingService(IUnitOfWork db)
{
    /// <summary>
    /// Regras 18–19, ENQUEUE-ONLY (a devolução acabou de nascer no mesmo SaveChanges): compensa os firmes nascidos de
    /// vínculos da linha de origem, SOLTOS e com saldo, do mais antigo (CreatedAt, depois RowId). Sem origem, ou origem
    /// composta/quitada, a devolução fica em aberto para o operador.
    /// </summary>
    public async Task EnqueueAutoAsync(FinancialDocument returnDocument, Guid? originLineKey, string userName)
    {
        if (originLineKey is null || returnDocument.Nature != FinancialDocumentNature.PurchaseReturn)
            return;

        var context = db.Context;
        var originLinkKeys = await context.PurchaseFiscalReconciliations.AsNoTracking()
            .Where(l => l.PurchaseInvoiceItemKey == originLineKey.Value)
            .Select(l => l.Key)
            .ToListAsync();
        if (originLinkKeys.Count == 0)
            return;

        var origins = await context.FinancialDocuments
            .Where(d => d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation &&
                        d.Nature == FinancialDocumentNature.Firm &&
                        d.Status != FinancialDocumentStatus.Canceled &&
                        d.OriginKey != null && originLinkKeys.Contains(d.OriginKey.Value))
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.RowId)
            .ToListAsync();

        foreach (var firm in origins)
        {
            if (returnDocument.OpenAmount <= 0m)
                break;

            if (firm.OpenAmount <= 0m || PurchaseFirmPayableRules.ProvisionalOf(context, firm.Key) is not null)
                continue;

            await EnqueuePairAsync(returnDocument, firm, Math.Min(returnDocument.OpenAmount, firm.OpenAmount), userName);
        }
    }

    /// <summary>Regra 20: "Compensar com firme" — a devolução em aberto contra um firme solto do mesmo contrato.</summary>
    public async Task ExecuteAsync(Guid returnKey, Guid firmKey, string userName)
    {
        var context = db.Context;

        var refund = await context.FinancialDocuments.FirstOrDefaultAsync(d => d.Key == returnKey)
                     ?? throw new NotFoundException("Documento financeiro não encontrado.");
        if (!refund.IsPurchaseFirmPayable || refund.Nature != FinancialDocumentNature.PurchaseReturn)
            throw new ApplicationException("Compensar com firme só vale para título de devolução.");

        var firm = await context.FinancialDocuments.FirstOrDefaultAsync(d => d.Key == firmKey)
                   ?? throw new NotFoundException("Documento financeiro não encontrado.");
        if (!firm.IsPurchaseFirmPayable || firm.Nature != FinancialDocumentNature.Firm)
            throw new ApplicationException($"O título {firm.Code} não é um título firme da conciliação fiscal.");

        if (firm.PurchaseContractKey != refund.PurchaseContractKey)
            throw new ApplicationException($"O título {firm.Code} é de outro contrato.");

        foreach (var document in new[] { refund, firm })
        {
            if (document.Status == FinancialDocumentStatus.Canceled)
                throw new ApplicationException($"O título {document.Code} está cancelado.");

            if (document.OpenAmount <= 0m)
                throw new ApplicationException($"O título {document.Code} não tem saldo a compensar.");
        }

        if (PurchaseFirmPayableRules.ProvisionalOf(context, firm.Key) is { } provisionalKey)
        {
            var provisionalCode = await context.FinancialDocuments.AsNoTracking()
                .Where(d => d.Key == provisionalKey).Select(d => d.Code).FirstOrDefaultAsync();
            throw new ApplicationException(
                $"O título {firm.Code} está composto no título {provisionalCode}: só firme solto compensa a devolução.");
        }

        await EnqueuePairAsync(refund, firm, Math.Min(refund.OpenAmount, firm.OpenAmount), userName);
        await db.SaveChangesAsync();
    }

    private async Task EnqueuePairAsync(FinancialDocument refund, FinancialDocument firm, decimal amount, string userName)
    {
        var now = DateTime.Now;
        db.Context.FinancialSettlements.AddRange(
            Line(refund, firm, amount, now, userName),
            Line(firm, refund, amount, now, userName));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, refund.Key);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, firm.Key);
    }

    private static FinancialSettlement Line(
        FinancialDocument document, FinancialDocument other, decimal amount, DateTime now, string userName) => new()
    {
        Key = Guid.NewGuid(),
        FinancialDocumentKey = document.Key,
        FinancialAccountCode = null,
        SettlementDate = now.Date,
        Amount = amount,
        Origin = FinancialSettlementOrigin.Netting,
        RelatedDocumentKey = other.Key,
        DocumentReference = other.Code,
        CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
    };
}
```

`git add` o arquivo.

- [ ] **Step 4: O sync dispara a compensação automática**

`FinancialDocumentsFirmSyncService.cs`:

1. Construtor:

```csharp
public class FinancialDocumentsFirmSyncService(
    IUnitOfWork db,
    DocNumberSequenceService docNumberSequence,
    FinancialDocumentChangeLogService changeLog,
    IUsage usageService,
    IConfiguration configuration,
    FinancialDocumentsNettingService netting)
```

2. No fim de `EnqueueSyncAsync`, depois de `context.FinancialDocuments.Add(document); await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);`:

```csharp

        // Regra 8: devolução nasce → compensação automática com o firme de origem solto (regras 18–19).
        if (document.Nature == FinancialDocumentNature.PurchaseReturn)
            await netting.EnqueueAutoAsync(document, line.PurchaseInvoiceItemOriginKey, userName);
```

3. `Support/FinancialDocumentTestServices.cs`, `FirmSync` passa a ser:

```csharp
    public static FinancialDocumentsFirmSyncService FirmSync(
        IUnitOfWork db, FakeDocNumberSequenceService? numbers = null, string erp = "STANDALONE") =>
        new(db, numbers ?? new FakeDocNumberSequenceService(), new FinancialDocumentChangeLogService(db.Context),
            new UsageService(db, NullLogger<UsageService>.Instance), TestConfiguration.Erp(erp),
            new FinancialDocumentsNettingService(db));
```

4. DI, depois de `services.AddScoped<FinancialDocumentsDationService>();`:

```csharp
        services.AddScoped<FinancialDocumentsNettingService>();
```

- [ ] **Step 5: Estorno do par**

`FinancialDocumentsReverseSettlementService.cs` completo:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Estorna uma baixa gravando a linha NEGATIVA espelho. O ledger é insert-only — é dinheiro,
/// nunca se apaga. O índice único filtrado em ReversedSettlementKey impede estornar a mesma
/// baixa duas vezes; a consulta abaixo é a camada amigável do mesmo impedimento.
///
/// Encontro de contas (spec 2026-10-13, títulos firmes, regra 21): estornar uma ponta estorna o PAR no mesmo ato.
/// Abatimento de composição (regra 14): recusado — só o desfazer da composição o estorna.
/// </summary>
public class FinancialDocumentsReverseSettlementService(IUnitOfWork db)
{
    public async Task ExecuteAsync(
        Guid settlementKey, string reason, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo do estorno.");

        var settlement = await db.Context.FinancialSettlements
                             .FirstOrDefaultAsync(x => x.Key == settlementKey)
                         ?? throw new NotFoundException("Baixa não encontrada.");

        if (settlement.Origin == FinancialSettlementOrigin.Reversal)
            throw new ApplicationException("Não é possível estornar um estorno.");

        // Regra 14 (spec 2026-10-13, títulos firmes): o abatimento nasce da composição e só ela o desfaz (remove a
        // linha de composição junto); estornar por aqui deixaria a composição órfã.
        if (settlement.Origin == FinancialSettlementOrigin.InvoiceOffset)
            throw new ApplicationException("Abatimento de composição se desfaz pela composição.");

        // A devolução é o registro de que o dinheiro voltou por fora — não é uma baixa errada,
        // então não há o que "desfazer" nela.
        if (settlement.Origin == FinancialSettlementOrigin.AdvanceRefund)
            throw new ApplicationException(
                "Não é possível estornar uma devolução: ela é o registro de que o dinheiro voltou.");

        var settlements = new List<FinancialSettlement> { settlement };

        // Regra 21: o par do encontro de contas. Qualquer ponta ainda não estornada entre os mesmos dois títulos e
        // com o mesmo valor serve — financeiramente são idênticas.
        if (settlement.Origin == FinancialSettlementOrigin.Netting)
        {
            var counterpart = await db.Context.FinancialSettlements
                                  .Where(x => x.FinancialDocumentKey == settlement.RelatedDocumentKey &&
                                              x.RelatedDocumentKey == settlement.FinancialDocumentKey &&
                                              x.Origin == FinancialSettlementOrigin.Netting &&
                                              x.Amount == settlement.Amount &&
                                              !db.Context.FinancialSettlements.Any(r => r.ReversedSettlementKey == x.Key))
                                  .OrderBy(x => x.CreatedAt)
                                  .FirstOrDefaultAsync()
                              ?? throw new ApplicationException("Lançamento par do encontro de contas não encontrado.");
            settlements.Add(counterpart);
        }

        foreach (var line in settlements)
        {
            var document = await db.Context.FinancialDocuments
                               .FirstOrDefaultAsync(x => x.Key == line.FinancialDocumentKey)
                           ?? throw new NotFoundException("Documento financeiro não encontrado.");

            // A devolução de adiantamento é o primeiro caminho do sistema que produz um documento
            // Canceled com um ledger de baixas ainda vivo (a baixa original + a linha negativa da
            // devolução). Sem este guard, estornar a baixa original reabriria o saldo enquanto o
            // Status continua Canceled — e nem o recálculo (que não mexe em documento cancelado) nem
            // o guard de cancelamento de contrato (que já filtra Status != Canceled) percebem. É o
            // buraco que esta feature existe para fechar reabrindo sozinho, então NÃO apagar por
            // parecer redundante com o guard de Origin acima.
            if (document.Status == FinancialDocumentStatus.Canceled)
                throw new ApplicationException("Não é possível estornar a baixa de um título cancelado.");

            if (await db.Context.FinancialSettlements.AnyAsync(x => x.ReversedSettlementKey == line.Key))
                throw new ApplicationException("Esta baixa já foi estornada.");
        }

        var reversals = settlements.Select(line => Mirror(line, reason, userName)).ToList();

        try
        {
            if (commitMode == CommitMode.Auto) await db.BeginTransactionAsync();

            db.Context.FinancialSettlements.AddRange(reversals);
            await db.Context.SaveChangesAsync();

            foreach (var line in settlements)
                await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(db.Context, line.FinancialDocumentKey);
            await db.Context.SaveChangesAsync();

            if (commitMode == CommitMode.Auto) await db.CommitAsync();
        }
        catch
        {
            if (commitMode == CommitMode.Auto) await db.RollbackAsync();
            throw;
        }
    }

    private static FinancialSettlement Mirror(FinancialSettlement settlement, string reason, string userName) => new()
    {
        Key = Guid.NewGuid(),
        FinancialDocumentKey = settlement.FinancialDocumentKey,
        FinancialAccountCode = settlement.FinancialAccountCode,
        SettlementDate = DateTime.Today,
        Amount = -settlement.Amount,
        InterestAmount = -settlement.InterestAmount,
        FineAmount = -settlement.FineAmount,
        DiscountAmount = -settlement.DiscountAmount,
        Origin = FinancialSettlementOrigin.Reversal,
        ReversedSettlementKey = settlement.Key,
        RelatedDocumentKey = settlement.RelatedDocumentKey,
        CompositionKey = settlement.CompositionKey,
        DocumentReference = settlement.DocumentReference,
        Notes = reason,
        CreatedAt = DateTime.Now,
        CreatedBy = userName,
        UpdatedAt = DateTime.Now,
        UpdatedBy = userName
    };
}
```

- [ ] **Step 6: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Financials|FullyQualifiedName~PurchaseFiscalReconciliations"`
Expected: todos PASS.

- [ ] **Step 7: Stage**

```bash
git add SiagroB1.Application/Services/Financials/FinancialDocumentsNettingService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsFirmSyncService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsReverseSettlementService.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/Support/FinancialDocumentTestServices.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsNettingTests.cs
```
O controlador commita com:

```
feat(financial): devolução compensa o firme por encontro de contas

A devolução de compra conciliada compensa sozinha o firme de origem solto (do mais antigo), uma
baixa de encontro de contas em cada título; com a origem composta ou quitada ela fica em aberto,
e o operador compõe negativa no provisório ou compensa à mão com outro firme solto do contrato.
Estornar uma ponta do encontro estorna o par.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 9: Estorno de romaneio desfaz composição, vínculo e firme — ou recusa (regras 23–24)

**Files:**
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsFirmUnwind.cs`
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationDeleteService.cs` (`ExecuteAsync`)
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsFirmUnwindTests.cs`

**Interfaces:**
- Consumes: `FinancialDocumentsUncomposeService.EnqueueAsync(AppDbContext, FinancialDocumentComposition, string)` (Task 6), `PurchaseFirmPayableRules.ActiveFirmsOf/CancelLooseFirms` (Task 5).
- Produces: `static class FinancialDocumentsFirmUnwind` com `Task EnqueueForAllocationAsync(AppDbContext context, StorageTransaction transaction, Guid purchaseContractKey, string userName)` — estático de propósito: o construtor de `PurchaseContractsAllocationDeleteService` (11 call sites em testes) não muda.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsFirmUnwindTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Regras 23–24 (spec 2026-10-13, títulos firmes): estornar romaneio = excluir a alocação. Com o provisório já pago,
/// recusa; sem baixa, desfaz a composição, remove os vínculos do contrato e cancela os firmes. Devolução e quebra: nada.
/// </summary>
public class FinancialDocumentsFirmUnwindTests
{
    private static PurchaseContractsAllocationDeleteService AllocationDelete(FirmPayableSeed s) =>
        new(s.Db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance);

    private static async Task<Guid> AllocationKeyAsync(FirmPayableSeed s, StorageTransaction tx) =>
        (await s.Context.PurchaseContractsAllocations.AsNoTracking().SingleAsync(a => a.StorageTransactionKey == tx.Key)).Key;

    private static Task ComposeAsync(FirmPayableSeed s, Guid firmKey) =>
        new FinancialDocumentsComposeService(s.Db, new FinancialDocumentChangeLogService(s.Context))
            .ExecuteAsync(s.Provisional.Key, [firmKey], "ana");

    [Fact]
    public async Task Reversing_a_transaction_whose_provisional_has_settlement_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);
        await new FinancialDocumentsSettleService(seed.Db)
            .ExecuteAsync(seed.Provisional.Key, "CX01", 1_000m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");
        var allocationKey = await AllocationKeyAsync(seed, c.Transaction);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            AllocationDelete(seed).ExecuteWithTransactionAsync(allocationKey, "ana"));

        Assert.Equal(
            $"O romaneio {c.Transaction.Code} compõe o título FN-PROV-1, que já tem baixa ou compensação: estorne a baixa antes.",
            error.Message);
        seed.Context.ChangeTracker.Clear();
        Assert.True(await seed.Context.PurchaseContractsAllocations.AnyAsync(a => a.Key == allocationKey));
        Assert.Single(seed.Context.PurchaseFiscalReconciliations.AsNoTracking());
        Assert.Single(seed.Context.FinancialDocumentCompositions.AsNoTracking());
    }

    [Fact]
    public async Task Reversing_a_transaction_without_settlement_uncomposes_unlinks_and_cancels_the_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);
        c.Transaction.FiscalReconciledAt = DateTime.Now;
        c.Transaction.FiscalReconciledBy = "ana";
        await seed.Db.SaveChangesAsync();

        await AllocationDelete(seed).ExecuteWithTransactionAsync(await AllocationKeyAsync(seed, c.Transaction), "bia");

        Assert.Empty(seed.Context.PurchaseContractsAllocations.AsNoTracking());
        Assert.Empty(seed.Context.PurchaseFiscalReconciliations.AsNoTracking());
        Assert.Empty(seed.Context.FinancialDocumentCompositions.AsNoTracking());
        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, firm.Status);
        Assert.Equal("Vínculo fiscal desfeito", firm.CancellationReason);
        Assert.Equal("bia", firm.CanceledBy);
        Assert.Equal(0m, firm.SettledAmount);
        Assert.Equal(0m, (await seed.ReloadAsync(seed.Provisional.Key)).ComposedAmount);
        Assert.Null((await seed.Context.StorageTransactions.AsNoTracking().SingleAsync(t => t.Key == c.Transaction.Key)).FiscalReconciledAt);
    }

    [Fact]
    public async Task Reversing_a_transaction_with_a_loose_firm_unlinks_and_cancels_it()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await AllocationDelete(seed).ExecuteWithTransactionAsync(await AllocationKeyAsync(seed, c.Transaction), "bia");

        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(c.Firm!.Key)).Status);
        Assert.Empty(seed.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }

    [Theory]
    [InlineData(StorageTransactionType.PurchaseReturn)]
    [InlineData(StorageTransactionType.PurchaseDischargeLoss)]
    public async Task Return_and_discharge_loss_allocations_do_not_touch_the_return_title(StorageTransactionType type)
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var r = await seed.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return, transactionType: type);

        // Caminho em cascata (ExecuteAsync): a tela recusa quebra/sobra (16/17) antes.
        await AllocationDelete(seed).ExecuteAsync(await AllocationKeyAsync(seed, r.Transaction), "bia");

        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(r.Firm!.Key)).Status);
        Assert.Single(seed.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsFirmUnwindTests"`
Expected: os dois primeiros testes FAIL (a alocação some sem recusa / o vínculo e o firme ficam); o terceiro FAIL (firme continua Open); o da teoria PASSA (regressão da regra 24).

- [ ] **Step 3: O desfazer**

`SiagroB1.Application/Services/Financials/FinancialDocumentsFirmUnwind.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Estorno de romaneio de compra diante dos títulos firmes (spec 2026-10-13, títulos firmes, regras 23–24 e D7).
/// Chamado por PurchaseContractsAllocationDeleteService.ExecuteAsync — o único caminho de todos os estornos — ANTES de
/// qualquer outra mudança. Estático para não mudar o construtor daquele serviço. ENQUEUE-ONLY.
/// </summary>
public static class FinancialDocumentsFirmUnwind
{
    public static async Task EnqueueForAllocationAsync(
        AppDbContext context, StorageTransaction transaction, Guid purchaseContractKey, string userName)
    {
        // Regra 24: devolução (9) e quebra (16) não travam nem desfazem nada — o efeito vem do título de devolução.
        if (transaction.TransactionType is StorageTransactionType.PurchaseReturn or StorageTransactionType.PurchaseDischargeLoss)
            return;

        // Só os vínculos do romaneio com documentos DESTE contrato.
        var links = await context.PurchaseFiscalReconciliations
            .Where(l => l.StorageTransactionKey == transaction.Key &&
                        l.PurchaseInvoiceItem!.PurchaseContractKey == purchaseContractKey)
            .ToListAsync();
        if (links.Count == 0)
            return;

        var linkKeys = links.Select(l => l.Key).ToList();
        var firmKeys = PurchaseFirmPayableRules.ActiveFirmsOf(context, linkKeys).Select(f => f.Key).ToList();
        var compositions = await context.FinancialDocumentCompositions
            .Where(c => firmKeys.Contains(c.ComposedDocumentKey))
            .ToListAsync();

        // D7: o título em que o romaneio compõe já tem baixa ou compensação → recusa.
        var provisionalKeys = compositions.Select(c => c.ProvisionalDocumentKey).Distinct().ToList();
        var paid = await context.FinancialDocuments.AsNoTracking()
            .Where(d => provisionalKeys.Contains(d.Key) && d.SettledAmount != 0m)
            .OrderBy(d => d.Code)
            .Select(d => d.Code)
            .FirstOrDefaultAsync();
        if (paid is not null)
            throw new ApplicationException(
                $"O romaneio {transaction.Code} compõe o título {paid}, que já tem baixa ou compensação: estorne a baixa antes.");

        // Regra 13 sem a checagem (o provisório não tem baixa), remoção dos vínculos e regra 7.
        foreach (var composition in compositions)
            await FinancialDocumentsUncomposeService.EnqueueAsync(context, composition, userName);

        context.PurchaseFiscalReconciliations.RemoveRange(links);
        PurchaseFirmPayableRules.CancelLooseFirms(context, linkKeys, userName);

        // O romaneio volta a "Sem NF".
        transaction.FiscalReconciledAt = null;
        transaction.FiscalReconciledBy = null;
    }
}
```

`git add` o arquivo.

- [ ] **Step 4: Gancho na exclusão da alocação**

`PurchaseContractsAllocationDeleteService.ExecuteAsync` — using `SiagroB1.Application.Services.Financials`; logo depois de

```csharp
        if (storageTransaction.TransactionStatus == StorageTransactionsStatus.Invoiced)
            throw new ApplicationException("Cannot delete an allocation of an invoiced storage transaction.");
```

inserir:

```csharp

        // Spec 2026-10-13 (títulos firmes, regras 23–24): o estorno do romaneio desfaz a composição dos firmes dele,
        // remove os vínculos fiscais com documentos deste contrato e cancela os firmes — ou recusa, se o provisório já
        // tem baixa. ANTES de qualquer outra mudança; tudo enfileirado no mesmo SaveChanges do estorno.
        await FinancialDocumentsFirmUnwind.EnqueueForAllocationAsync(
            db.Context, storageTransaction, alloc.PurchaseContractKey, userName);
```

- [ ] **Step 5: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsFirmUnwindTests|FullyQualifiedName~PurchaseDischarge|FullyQualifiedName~ShippingTransactionsReverseServiceTests|FullyQualifiedName~StorageEntryTransactionsCancelServiceTests|FullyQualifiedName~OwnershipTransfers|FullyQualifiedName~WarehouseReconciliations|FullyQualifiedName~SalesInvoices"`
Expected: todos PASS (os demais caminhos que excluem alocação não têm vínculo fiscal e não mudam).

- [ ] **Step 6: Stage**

```bash
git add SiagroB1.Application/Services/Financials/FinancialDocumentsFirmUnwind.cs SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsAllocationDeleteService.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsFirmUnwindTests.cs
```
O controlador commita com:

```
feat(financial): estorno de romaneio desfaz a composição e cancela o firme

Estornar romaneio passa pela exclusão da alocação: se o provisório em que o firme dele compõe
já tem baixa ou compensação, recusa; senão desfaz a composição, remove os vínculos fiscais do
romaneio com documentos daquele contrato e cancela os firmes, e o romaneio volta a Sem NF.
Devolução e quebra de descarga não travam: o efeito vem do título de devolução.

Atenção: o gancho é estático para não mudar o construtor do serviço de alocação (11 call sites).

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 10: Travas de fixação, washout, cancelamento, retirada de aprovação, encerramento e reabertura (regras 25–30)

**Files:**
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsCancelService.cs`
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsAdjustProvisionalService.cs` (arquivo completo abaixo)
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsCancelService.cs` (`ExecuteAsync`, `EnqueueCancelByContractAsync`)
- Modify: `SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCloseService.cs` (arquivo completo abaixo)
- Modify: `SiagroB1.Application.Tests/Financials/FinancialDocumentUndoHooksTests.cs` (construtor do encerramento e dois testes reescritos)
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentFirmLocksTests.cs`

**Interfaces:**
- Consumes: Tasks 3–6.
- Produces: `PurchaseContractsCloseService(AppDbContext context, PurchaseContractsFixedVolumeService fixedVolumeService, ContractNotificationOutboxService notificationOutbox)` (sem o `FinancialDocumentsCancelService`). Demais assinaturas inalteradas.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentFirmLocksTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Interceptors;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-13 (títulos firmes), regras 25–30 e D6/D8/D9.</summary>
public class FinancialDocumentFirmLocksTests
{
    private static Task ComposeAsync(FirmPayableSeed s, Guid firmKey) =>
        new FinancialDocumentsComposeService(s.Db, new FinancialDocumentChangeLogService(s.Context))
            .ExecuteAsync(s.Provisional.Key, [firmKey], "ana");

    private static Task SettleProvisionalAsync(FirmPayableSeed s, decimal amount) =>
        new FinancialDocumentsSettleService(s.Db)
            .ExecuteAsync(s.Provisional.Key, "CX01", amount, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

    /// <summary>Fixação confirmada do provisório do seed: 50.000 × 2,00 = 100.000,00.</summary>
    private static async Task<PurchaseContractPriceFixation> AddFixationAsync(FirmPayableSeed s, decimal volume = 50_000m)
    {
        var fixation = new PurchaseContractPriceFixation
        {
            Key = s.Provisional.OriginKey!.Value, PurchaseContractKey = s.Contract.Key, FixationVolume = volume,
            FixationPrice = 2m, Status = PriceFixationStatus.Confirmed, FinancialDueDate = new DateTime(2026, 12, 31),
        };
        s.Context.PurchaseContractsPriceFixations.Add(fixation);
        await s.Context.SaveChangesAsync();
        return fixation;
    }

    /// <summary>Composição gravada direto (sem conciliação): o cancelamento do contrato exige contrato sem alocação.</summary>
    private static async Task SeedCompositionAsync(FirmPayableSeed s, decimal amount)
    {
        s.Context.FinancialDocumentCompositions.Add(new FinancialDocumentComposition
        {
            Key = Guid.NewGuid(), ProvisionalDocumentKey = s.Provisional.Key, ComposedDocumentKey = Guid.NewGuid(), Amount = amount,
        });
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(s.Context, s.Provisional.Key);
        await s.Context.SaveChangesAsync();
    }

    private static PurchaseContractsCloseService Close(FirmPayableSeed s) =>
        new(s.Context, new PurchaseContractsFixedVolumeService(s.Context), TestNotificationOutbox.For(s.Context));

    [Fact]
    public async Task Reversing_a_fixation_whose_provisional_has_composition_is_refused_until_uncomposed()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var fixation = await AddFixationAsync(seed);
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);
        var service = new PurchaseContractsPriceFixationsCancelService(
            seed.Context, new PurchaseContractsFixedVolumeService(seed.Context), new PurchaseContractsChangeLogService(seed.Context),
            TestNotificationOutbox.For(seed.Context), FinancialDocumentTestServices.Cancel(seed.Context));

        var error = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(fixation.Key, "ana"));
        Assert.Equal("Desfaça a composição do título FN-PROV-1 antes de estornar a fixação.", error.Message);

        var row = await seed.Context.FinancialDocumentCompositions.AsNoTracking().SingleAsync();
        await new FinancialDocumentsUncomposeService(seed.Db).ExecuteAsync(row.Key, "ana");
        await service.ExecuteAsync(fixation.Key, "ana");

        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(seed.Provisional.Key)).Status);
        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(c.Firm.Key)).Status); // D6: firme volta solto
    }

    [Fact]
    public async Task Washout_cannot_take_the_provisional_below_the_composed_nor_cancel_it()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var fixation = await AddFixationAsync(seed);
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);
        var adjust = new FinancialDocumentsAdjustProvisionalService(
            seed.Context, FinancialDocumentTestServices.Generate(seed.Context), new FinancialDocumentChangeLogService(seed.Context));

        var below = await Assert.ThrowsAsync<ApplicationException>(() =>
            adjust.EnqueueForPurchaseFixationAsync(seed.Contract, fixation, 5_000m, "ana"));
        Assert.Equal(
            "O washout reduziria o título FN-PROV-1 para 10.000,00, abaixo do valor já composto (19.728,33): desfaça a composição antes.",
            below.Message);

        var cancel = await Assert.ThrowsAsync<ApplicationException>(() =>
            adjust.EnqueueForPurchaseFixationAsync(seed.Contract, fixation, 0m, "ana"));
        Assert.Equal(
            "O washout consumiria toda a fixação, mas o título FN-PROV-1 tem composição ou baixa: desfaça a composição e estorne as baixas antes.",
            cancel.Message);
    }

    [Fact]
    public async Task Washout_down_to_the_paid_amount_settles_the_provisional()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var fixation = await AddFixationAsync(seed);
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);
        await SettleProvisionalAsync(seed, FirmPayableSeed.DefaultFirmAmount);
        var adjust = new FinancialDocumentsAdjustProvisionalService(
            seed.Context, FinancialDocumentTestServices.Generate(seed.Context), new FinancialDocumentChangeLogService(seed.Context));

        await adjust.EnqueueForPurchaseFixationAsync(seed.Contract, fixation, 9_864.165m, "ana"); // × 2,00 = 19.728,33
        await seed.Context.SaveChangesAsync();

        var provisional = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(FirmPayableSeed.DefaultFirmAmount, provisional.NetAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, provisional.Status);
    }

    [Fact]
    public async Task Cancelling_the_contract_or_withdrawing_approval_with_composition_is_refused()
    {
        const string expected =
            "O título FN-PROV-1 tem composição ou baixa: desfaça a composição e estorne as baixas antes de cancelar o contrato ou retirar a aprovação.";
        var seed = await FirmPayableSeed.CreateAsync();
        await SeedCompositionAsync(seed, 500m);

        var cancel = await Assert.ThrowsAsync<ApplicationException>(() =>
            new PurchaseContractsCancelService(seed.Db, TestNotificationOutbox.For(seed.Context),
                    FinancialDocumentTestServices.Cancel(seed.Context), FinancialDocumentTestServices.CancellationGuard(seed.Context))
                .ExecuteAsync(seed.Contract.Key, "teste", "ana"));
        Assert.Equal(expected, cancel.Message);

        seed.Context.ChangeTracker.Clear();
        var withdraw = await Assert.ThrowsAsync<ApplicationException>(() =>
            new PurchaseContractsWithdrawApprovalService(seed.Db, new FakeStringLocalizer<Resource>(),
                    TestNotificationOutbox.For(seed.Context), FinancialDocumentTestServices.Cancel(seed.Context))
                .ExecuteAsync(seed.Contract.Key, "ana"));
        Assert.Equal(expected, withdraw.Message);

        Assert.Equal(ContractStatus.Approved,
            (await seed.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == seed.Contract.Key)).Status);
    }

    [Fact]
    public async Task Manual_cancel_of_a_provisional_with_composition_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        await SeedCompositionAsync(seed, 500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            new FinancialDocumentsCancelService(seed.Db).ExecuteAsync(seed.Provisional.Key, "teste", "ana"));

        Assert.Equal("O documento FN-PROV-1 tem composição. Desfaça a composição antes de cancelar.", error.Message);
    }

    [Fact]
    public async Task Closing_with_a_firm_with_balance_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        await seed.ConciliateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal("Há títulos firmes com saldo neste contrato: componha, compense ou baixe por dação antes de encerrar.", error.Message);
    }

    [Fact]
    public async Task Closing_with_an_unsettled_provisional_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(() => Close(seed).ExecuteAsync(seed.Contract.Key, "ana"));

        Assert.Equal(
            "O título FN-PROV-1 não está quitado: o contrato só encerra com todos os títulos quitados. Entrega a menos se resolve por washout; faturamento abaixo, por documento de complemento de valor.",
            error.Message);
    }

    [Fact]
    public async Task Closing_and_reopening_a_fully_settled_contract_keep_every_value()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: FirmPayableSeed.DefaultFirmAmount);
        await AddFixationAsync(seed, volume: 9_864.165m);
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);
        await SettleProvisionalAsync(seed, FirmPayableSeed.DefaultFirmAmount);

        await Close(seed).ExecuteAsync(seed.Contract.Key, "ana");

        var closed = await seed.ReloadAsync(seed.Provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Settled, closed.Status);
        Assert.Equal(FirmPayableSeed.DefaultFirmAmount, closed.NetAmount);

        await new PurchaseContractsReopenService(seed.Context, TestNotificationOutbox.For(seed.Context),
                FinancialDocumentTestServices.Generate(seed.Context))
            .ExecuteAsync(seed.Contract.Key, "ana");

        var provisionals = await seed.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.PurchaseContractKey == seed.Contract.Key && d.Nature == FinancialDocumentNature.Provisional)
            .ToListAsync();
        var reopened = Assert.Single(provisionals);
        Assert.Equal(FinancialDocumentStatus.Settled, reopened.Status);
        Assert.Equal(FirmPayableSeed.DefaultFirmAmount, reopened.NetAmount);
        Assert.Equal(ContractStatus.Approved,
            (await seed.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == seed.Contract.Key)).Status);
    }

    [Fact]
    public async Task Finished_contract_guard_does_not_block_financial_changes()
    {
        var db = TestDb.CreateUnitOfWork(new FinishedContractMutationGuardInterceptor());
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-9", CardCode = "F0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "25/26", DeliveryLocationCode = "01", TotalVolume = 1_000m, Status = ContractStatus.Finished,
        };
        var provisional = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = "FN-9", CardCode = "F0001", Direction = FinancialDirection.Payable,
            Nature = FinancialDocumentNature.Provisional, NetAmount = 1_000m, DueDate = new DateTime(2026, 12, 31),
            PurchaseContractKey = contract.Key,
        };
        db.Context.PurchaseContracts.Add(contract);
        db.Context.FinancialDocuments.Add(provisional);
        await db.Context.SaveChangesAsync();

        db.Context.FinancialDocumentCompositions.Add(new FinancialDocumentComposition
        {
            Key = Guid.NewGuid(), ProvisionalDocumentKey = provisional.Key, ComposedDocumentKey = Guid.NewGuid(), Amount = 10m,
        });
        provisional.NetAmount = 900m;
        await db.Context.SaveChangesAsync(); // regra 30: o interceptor não ganha FinancialDocument nem a composição

        Assert.Equal(900m, (await db.Context.FinancialDocuments.AsNoTracking().SingleAsync()).NetAmount);
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentFirmLocksTests"`
Expected: erro de compilação (construtor de 3 argumentos de `PurchaseContractsCloseService` não existe). Depois do Step 6 compilar, cada teste falha pela trava que falta, exceto `Finished_contract_guard_does_not_block_financial_changes` (regressão — já passa).

- [ ] **Step 3: Estorno de fixação (regra 25)**

`PurchaseContractsPriceFixationsCancelService.ExecuteAsync`, logo depois do bloco `if (hasActiveWashout) throw …`:

```csharp

        // Regra 25 (spec 2026-10-13, títulos firmes, D6): o provisório desta fixação com composição ou baixa barra o
        // estorno. Desfaz-se a composição primeiro — os firmes NÃO são apagados, voltam a ficar soltos.
        var provisional = await context.FinancialDocuments.AsNoTracking().FirstOrDefaultAsync(x =>
            x.OriginType == FinancialDocumentOrigin.PurchaseContractPriceFixation &&
            x.OriginKey == fixation.Key &&
            x.Nature == FinancialDocumentNature.Provisional &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (provisional is not null && (provisional.ComposedAmount != 0m || provisional.SettledAmount != 0m))
            throw new ApplicationException(
                $"Desfaça a composição do título {provisional.Code} antes de estornar a fixação.");
```

- [ ] **Step 4: Washout (regra 26)**

`FinancialDocumentsAdjustProvisionalService.cs` completo:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Ajusta NO LUGAR o provisório a pagar de uma fixação cujo volume mudou por washout.
///
/// No lugar, e não cancelar-e-gerar: cancelar-e-gerar exigiria gravar o cancelamento antes do insert (índice único
/// filtrado IX_FINANCIAL_DOCUMENTS_ProvisionalOrigin) e buscar número por Dapper no meio da transação.
///
/// Spec 2026-10-13 (títulos firmes, regra 26): o valor não desce abaixo do já COMPOSTO, e o provisório com composição
/// ou baixa não é cancelado. Depois de ajustar, recalcula o status — o washout que cobre a entrega a menos deixa o
/// provisório pago Quitado (D8), e é o que libera o encerramento.
///
/// ENQUEUE-ONLY. Chame ANTES de abrir transação: sem provisório aberto, delega ao gerador, que
/// busca número em DOC_NUMBERS.
/// </summary>
public class FinancialDocumentsAdjustProvisionalService(
    AppDbContext context,
    FinancialDocumentsGenerateService generateService,
    FinancialDocumentChangeLogService changeLog)
{
    public const string WashedOutCancellationReason = "Fixação totalmente consumida por washout";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <param name="remainingVolume">Volume da fixação que continua a faturar, já descontados os washouts aprovados.</param>
    public async Task EnqueueForPurchaseFixationAsync(
        PurchaseContract contract, PurchaseContractPriceFixation fixation, decimal remainingVolume, string userName)
    {
        var document = await context.FinancialDocuments.FirstOrDefaultAsync(x =>
            x.OriginType == FinancialDocumentOrigin.PurchaseContractPriceFixation &&
            x.OriginKey == fixation.Key &&
            x.Nature == FinancialDocumentNature.Provisional &&
            x.Status != FinancialDocumentStatus.Canceled);

        if (document is null)
        {
            // Cancelado por volume zero (e agora estornado) ou dado antigo sem provisório.
            if (remainingVolume > 0)
                await generateService.EnqueueForPurchaseFixationAsync(contract, fixation, userName, remainingVolume);
            return;
        }

        if (remainingVolume <= 0)
        {
            if (document.ComposedAmount != 0m || document.SettledAmount != 0m)
                throw new ApplicationException(
                    $"O washout consumiria toda a fixação, mas o título {document.Code} tem composição ou baixa: desfaça a composição e estorne as baixas antes.");

            FinancialDocumentsCancelService.Cancel(document, WashedOutCancellationReason, userName);
            return;
        }

        var newAmount = decimal.Round(remainingVolume * fixation.FixationPrice, 2, MidpointRounding.ToEven);
        if (newAmount == document.NetAmount) return;

        if (newAmount < document.ComposedAmount)
            throw new ApplicationException(
                $"O washout reduziria o título {document.Code} para {newAmount.ToString("N2", PtBr)}, abaixo do valor já composto ({document.ComposedAmount.ToString("N2", PtBr)}): desfaça a composição antes.");

        changeLog.Register(
            document.Key,
            FinancialDocumentChangeLogFields.NetAmount,
            document.NetAmount.ToString("N2", PtBr),
            newAmount.ToString("N2", PtBr),
            userName);

        document.NetAmount = newAmount;
        document.UpdatedAt = DateTime.Now;
        document.UpdatedBy = userName;

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);
    }
}
```

- [ ] **Step 5: Cancelamento (regra 27 e cancelamento manual do provisório composto)**

`FinancialDocumentsCancelService.cs`:

1. Em `ExecuteAsync`, logo **antes** de `if (document.SettledAmount != 0m) throw …possui baixas…`:

```csharp
        // Spec 2026-10-13 (títulos firmes): cancelar o provisório composto deixaria linhas de composição órfãs
        // abatendo os firmes.
        if (document.ComposedAmount != 0m)
            throw new ApplicationException(
                $"O documento {document.Code} tem composição. Desfaça a composição antes de cancelar.");

```

2. Em `EnqueueCancelByContractAsync`, entre o `.ToListAsync();` e o `foreach (var document in documents) Cancel(...)`:

```csharp

        // Regra 27 (spec 2026-10-13, títulos firmes): cancelar contrato e retirar aprovação recusam provisório com
        // composição ou baixa. O encerramento não chega mais aqui (regra 28).
        var blocked = documents.FirstOrDefault(x =>
            x.Nature == FinancialDocumentNature.Provisional && (x.ComposedAmount != 0m || x.SettledAmount != 0m));
        if (blocked is not null)
            throw new ApplicationException(
                $"O título {blocked.Code} tem composição ou baixa: desfaça a composição e estorne as baixas antes de cancelar o contrato ou retirar a aprovação.");
```

3. No `<summary>` de `EnqueueCancelByContractAsync`, trocar a frase "Os de ENCERRAMENTO chamam este mesmo método e ficam com o padrão false: contrato encerrado foi cumprido, e ali o adiantamento é matéria da amortização da Fase 2." por "O ENCERRAMENTO de contrato de compra não chama mais este método (spec 2026-10-13, regra 28); o de venda continua chamando com o padrão false."

- [ ] **Step 6: Encerramento (regra 28)**

`PurchaseContractsCloseService.cs` completo:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsCloseService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await context.PurchaseContracts
                           .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.Approved)
                       ?? throw new NotFoundException("Contrato não encontrado ou não está aprovado.");

        // ANTES da guarda de fixação: ela desconta o washout lavado ASSUMINDO que só sobrou
        // Approved — se um InApproval ainda pudesse passar, a conta contaria volume que talvez
        // seja rejeitado e volte ao saldo.
        await GuardPendingWashoutsAsync(contract);

        if (contract.Type == ContractType.ToBeDetermined)
            await GuardPriceFixationAsync(contract);

        await GuardNegativeBalanceAsync(contract);

        await GuardFinancialDocumentsAsync(contract);

        contract.Status = ContractStatus.Finished;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        notificationOutbox.Register(contract, NotificationEventType.Closed, userName);

        // Spec 2026-10-13 (títulos firmes, regra 28 e D8): o encerramento NÃO cancela mais o provisório e não altera
        // valor nenhum — ele só passa com tudo quitado (a entrega a menos se resolve por washout antes).
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Regra 28: nenhum firme/devolução do contrato com saldo, e todo provisório não cancelado QUITADO
    /// (baixa + compensação = valor contratado).
    /// </summary>
    private async Task GuardFinancialDocumentsAsync(PurchaseContract contract)
    {
        // Firme/devolução da conciliação e firme do complemento de valor (D11); o título a receber do washout não entra.
        var hasOpenFirm = await context.FinancialDocuments.AnyAsync(x =>
            x.PurchaseContractKey == contract.Key &&
            (x.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation ||
             x.OriginType == FinancialDocumentOrigin.PurchaseInvoice) &&
            (x.Nature == FinancialDocumentNature.Firm || x.Nature == FinancialDocumentNature.PurchaseReturn) &&
            x.Status != FinancialDocumentStatus.Canceled &&
            x.NetAmount > x.SettledAmount);

        if (hasOpenFirm)
            throw new ApplicationException(
                "Há títulos firmes com saldo neste contrato: componha, compense ou baixe por dação antes de encerrar.");

        var unsettled = await context.FinancialDocuments
            .Where(x => x.PurchaseContractKey == contract.Key &&
                        x.Nature == FinancialDocumentNature.Provisional &&
                        x.Status != FinancialDocumentStatus.Canceled &&
                        x.Status != FinancialDocumentStatus.Settled)
            .OrderBy(x => x.Code)
            .Select(x => x.Code)
            .FirstOrDefaultAsync();

        if (unsettled is not null)
            throw new ApplicationException(
                $"O título {unsettled} não está quitado: o contrato só encerra com todos os títulos quitados. Entrega a menos se resolve por washout; faturamento abaixo, por documento de complemento de valor.");
    }

    /// <summary>
    /// Um contrato a fixar não pode ser encerrado devendo preço de mercadoria já entregue.
    /// Usa o volume CONFIRMADO — volume apenas em aprovação não define preço — descontado do
    /// volume lavado por washout: aquele volume fixado saiu do contrato e não precisa de preço.
    /// <see cref="GuardPendingWashoutsAsync"/> já rodou antes desta guarda, então os washouts
    /// ATIVOS aqui são só os APROVADOS.
    /// </summary>
    private async Task GuardPriceFixationAsync(PurchaseContract contract)
    {
        var pendingCount = await context.PurchaseContractsPriceFixations
            .CountAsync(f => f.PurchaseContractKey == contract.Key
                             && f.Status == PriceFixationStatus.InApproval);

        if (pendingCount > 0)
            throw new ApplicationException(
                $"Contrato possui {pendingCount} fixação(ões) pendente(s) de aprovação. " +
                "Aprove ou rejeite antes de encerrar.");

        var confirmedVolume = await fixedVolumeService.ConfirmedVolumeAsync(contract.Key);
        var deliveredVolume = await fixedVolumeService.DeliveredVolumeAsync(contract.Key);

        var (washedOutTotal, washedOutUnfixed) = await PurchaseContractsWashedOutVolumeService
            .ActiveVolumesAsync(context, contract.Key);
        var approvedWashedFixed = washedOutTotal - washedOutUnfixed;

        var pricedConfirmed = confirmedVolume - approvedWashedFixed;

        if (pricedConfirmed < deliveredVolume)
            throw new ApplicationException(
                $"Volume entregue sem preço fixado. Entregue: {deliveredVolume:N3}, " +
                $"fixado e confirmado (descontado o washout): {pricedConfirmed:N3}. " +
                "Fixe o preço do volume entregue antes de encerrar o contrato.");
    }

    /// <summary>
    /// Washout em aprovação reserva volume mas ainda pode ser rejeitado. Encerrar com ele pendente
    /// congelaria o contrato com um volume "lavado" que ninguém decidiu.
    /// </summary>
    private async Task GuardPendingWashoutsAsync(PurchaseContract contract)
    {
        var pendingCount = await context.PurchaseContractsWashouts
            .CountAsync(w => w.PurchaseContractKey == contract.Key
                             && w.Status == PurchaseContractWashoutStatus.InApproval);

        if (pendingCount > 0)
            throw new ApplicationException(
                $"Contrato possui {pendingCount} washout(s) pendente(s) de aprovação. " +
                "Aprove ou rejeite antes de encerrar.");
    }

    /// <summary>
    /// Espelha <c>SalesContractsCloseService</c>: contrato consumido ALÉM do volume contratado
    /// não pode ser congelado. Decide sobre o saldo RECALCULADO do ledger e dos washouts ativos,
    /// não sobre os agregados persistidos, que podem estar defasados. Não persiste o recálculo.
    /// </summary>
    private async Task GuardNegativeBalanceAsync(PurchaseContract contract)
    {
        var allocated = await PurchaseContractsRecalculateBalanceService
            .CalculateAllocatedAsync(context, contract.Key);
        var (washedOut, _) = await PurchaseContractsWashedOutVolumeService
            .ActiveVolumesAsync(context, contract.Key);
        var balance = decimal.Round(contract.TotalVolume - allocated - washedOut, 2, MidpointRounding.ToEven);

        if (balance < 0)
            throw new ApplicationException(
                $"Contrato faturado além do volume contratado. Contratado: {contract.TotalVolume:N2}, " +
                $"alocado: {allocated:N2}, washout: {washedOut:N2}, saldo: {balance:N2}. " +
                "Ajuste as alocações antes de encerrar.");
    }
}
```

(A reabertura — regra 29 — não muda: `PurchaseContractsReopenService` já usa o gerador idempotente, que pula o provisório existente. O teste `Closing_and_reopening_a_fully_settled_contract_keep_every_value` prende isso.)

- [ ] **Step 7: Ajustar `FinancialDocumentUndoHooksTests`**

1. Nos três `new PurchaseContractsCloseService(...)`, apagar o 4º argumento `FinancialDocumentTestServices.Cancel(act)` / `FinancialDocumentTestServices.Cancel(close)` (e a vírgula antes dele).
2. Trocar o teste `ClosingAContract_CancelsTheRemainingOpenProvisionals` inteiro (do `[Fact]` ao `}` final dele) por:

```csharp
    /// <summary>
    /// Spec 2026-10-13 (títulos firmes, regra 28 e D8): encerrar NÃO cancela mais o provisório — exige que ele esteja
    /// quitado. A entrega a menos se resolve por washout antes.
    /// </summary>
    [Fact]
    public async Task ClosingAContract_WithAnOpenProvisional_IsRefusedAndLeavesItOpen()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var documentKey = Guid.NewGuid();
        string? documentCode;

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            var seededDocument = NewProvisional(contractKey, Guid.NewGuid());
            seededDocument.Key = documentKey;
            documentCode = seededDocument.Code;
            seed.FinancialDocuments.Add(seededDocument);

            await seed.SaveChangesAsync();
        }

        await using (var act = NewContext(dbName))
        {
            var error = await Assert.ThrowsAsync<ApplicationException>(() =>
                new PurchaseContractsCloseService(
                        act,
                        new PurchaseContractsFixedVolumeService(act),
                        TestNotificationOutbox.For(act))
                    .ExecuteAsync(contractKey, "tester"));

            Assert.Equal(
                $"O título {documentCode} não está quitado: o contrato só encerra com todos os títulos quitados. Entrega a menos se resolve por washout; faturamento abaixo, por documento de complemento de valor.",
                error.Message);
        }

        await using var assert = NewContext(dbName);

        Assert.Equal(ContractStatus.Approved, (await assert.PurchaseContracts.SingleAsync(x => x.Key == contractKey)).Status);
        Assert.Equal(FinancialDocumentStatus.Open, (await assert.FinancialDocuments.SingleAsync(x => x.Key == documentKey)).Status);
    }
```

3. Trocar o teste `ReopeningAClosedContract_RegeneratesTheProvisional_ThroughTheRealCloseAndReopenServices` inteiro por:

```csharp
    /// <summary>
    /// Spec 2026-10-13 (títulos firmes, regras 28–29 e D9): o provisório quitado sobrevive ao encerramento, e a
    /// reabertura não altera valor nenhum — o gerador idempotente pula o provisório existente.
    /// </summary>
    [Fact]
    public async Task ReopeningAClosedContract_KeepsTheSettledProvisional_ThroughTheRealCloseAndReopenServices()
    {
        var dbName = Guid.NewGuid().ToString();
        var contractKey = Guid.NewGuid();
        var fixationKey = Guid.NewGuid();
        var originalDocumentKey = Guid.NewGuid();

        await using (var seed = NewContext(dbName))
        {
            var contract = NewContract();
            contract.Key = contractKey;
            seed.PurchaseContracts.Add(contract);

            seed.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
            {
                Key = fixationKey,
                PurchaseContractKey = contractKey,
                FixationVolume = 1_000m,
                FixationPrice = 2m,
                Status = PriceFixationStatus.Confirmed,
                ApprovedBy = "diretoria",
                ApprovedAt = DateTime.Now,
            });

            var seededDocument = NewProvisional(contractKey, fixationKey, FinancialDocumentStatus.Settled);
            seededDocument.Key = originalDocumentKey;
            seededDocument.SettledAmount = seededDocument.NetAmount;
            seed.FinancialDocuments.Add(seededDocument);

            await seed.SaveChangesAsync();
        }

        await using (var close = NewContext(dbName))
        {
            await new PurchaseContractsCloseService(
                    close,
                    new PurchaseContractsFixedVolumeService(close),
                    TestNotificationOutbox.For(close))
                .ExecuteAsync(contractKey, "tester");
        }

        await using (var reopen = NewContext(dbName))
        {
            await new PurchaseContractsReopenService(
                    reopen,
                    TestNotificationOutbox.For(reopen),
                    FinancialDocumentTestServices.Generate(reopen))
                .ExecuteAsync(contractKey, "tester");
        }

        await using var assert = NewContext(dbName);

        Assert.Equal(ContractStatus.Approved, (await assert.PurchaseContracts.SingleAsync(x => x.Key == contractKey)).Status);

        var document = Assert.Single(await assert.FinancialDocuments
            .Where(x => x.OriginType == FinancialDocumentOrigin.PurchaseContractPriceFixation && x.OriginKey == fixationKey)
            .ToListAsync());
        Assert.Equal(originalDocumentKey, document.Key);
        Assert.Equal(FinancialDocumentStatus.Settled, document.Status);
        Assert.Equal(1_000m, document.NetAmount);
    }
```

4. Conferir: `grep -rn "new PurchaseContractsCloseService" SiagroB1.Application.Tests SiagroB1.Application SiagroB1.Web` — nenhum call site com 4 argumentos.

- [ ] **Step 8: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentFirmLocksTests|FullyQualifiedName~FinancialDocumentUndoHooksTests|FullyQualifiedName~Washout|FullyQualifiedName~FinancialDocumentContractWithdrawAndDeleteTests|FullyQualifiedName~FinancialDocumentsContractCancellationGuardServiceTests|FullyQualifiedName~FinancialDocumentsCancelServiceTests|FullyQualifiedName~FinishedContractMutationGuardInterceptorTests"`
Expected: todos PASS.

- [ ] **Step 9: Stage**

```bash
git add SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsPriceFixationsCancelService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsAdjustProvisionalService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsCancelService.cs SiagroB1.Application/Services/PurchaseContracts/PurchaseContractsCloseService.cs SiagroB1.Application.Tests/Financials/FinancialDocumentUndoHooksTests.cs SiagroB1.Application.Tests/Financials/FinancialDocumentFirmLocksTests.cs
```
O controlador commita com:

```
feat(financial): travas do contrato de compra diante da composição

Estornar fixação, cancelar contrato e retirar aprovação são recusados com provisório composto
ou baixado; o washout não leva o provisório abaixo do composto nem o cancela, e recalcula a
situação — o washout que cobre a entrega a menos deixa o provisório pago quitado. Encerrar
passa a exigir firmes sem saldo e todo provisório quitado, e não cancela nem altera valor;
reabrir não muda nada.

Atenção: contratos antigos com provisório aberto e sem firme só encerram depois de washout.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 11: Leituras — composição de um título, financeiro do contrato e firme na conciliação

**Files:**
- Create: `SiagroB1.Domain/Dtos/FinancialDocumentCompositionDto.cs`
- Create: `SiagroB1.Application/Services/Financials/FinancialDocumentsGetCompositionService.cs`
- Modify: `SiagroB1.Domain/Dtos/FinancialDocumentByContractDto.cs`, `SiagroB1.Application/Services/Financials/FinancialDocumentsGetByContractService.cs`
- Modify: `SiagroB1.Domain/Dtos/FiscalReconciliationDtos.cs` (`FiscalReconciliationLinkDto`), `SiagroB1.Infra/Queries/PurchaseFiscalReconciliationQuery.cs` (`LoadLinksAsync`)
- Modify: `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsReadModelTests.cs`

**Interfaces:**
- Consumes: Tasks 3–8.
- Produces:
  - `FinancialDocumentsGetCompositionService(IUnitOfWork db)`: `Task<FinancialDocumentCompositionDto> ExecuteAsync(Guid key)`. Para PROVISÓRIO: `Lines` = suas composições, `Candidates` = firmes/devoluções do mesmo contrato, não cancelados, com saldo (`SuggestedAmount` = firme: `min(saldo, espaço)`; devolução: `−saldo`). Para FIRME: `Lines` = onde ele está composto (com `ProvisionalCode`), `Candidates` vazio. Para DEVOLUÇÃO da conciliação: `Lines` = onde está composta, `Candidates` = firmes SOLTOS do contrato com saldo (`SuggestedAmount = min(saldo do firme, saldo da devolução)`).
  - DTOs (PascalCase): `FinancialDocumentCompositionDto { List<FinancialDocumentCompositionLineDto> Lines; List<FinancialDocumentCompositionCandidateDto> Candidates }`; `FinancialDocumentCompositionLineDto { Guid CompositionKey; Guid ProvisionalDocumentKey; string? ProvisionalCode; Guid DocumentKey; string? DocumentCode; string? Nature; string? StorageTransactionCode; string? TaxDocumentNumber; string? CardCode; string? CardName; decimal Amount; DateTime? CreatedAt; string? CreatedBy }`; `FinancialDocumentCompositionCandidateDto { Guid DocumentKey; string? Code; string? Nature; string? StorageTransactionCode; string? TaxDocumentNumber; string? CardCode; string? CardName; DateTime DocumentDate; decimal OpenAmount; decimal SuggestedAmount }`.
  - `FinancialDocumentByContractDto` += `decimal ComposedAmount`, `string? StorageTransactionCode`, `string? TaxDocumentNumber`.
  - `FiscalReconciliationLinkDto` += `Guid? FirmDocumentKey`, `string? FirmDocumentCode`, `string? FirmSituation` ("Solto" | "Composto" | "Cancelado").

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsReadModelTests.cs`:

```csharp
using System.Text.Json;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Leituras dos títulos firmes (spec 2026-10-13): Detail do título, seção Financeiro do contrato e conciliação.</summary>
public class FinancialDocumentsReadModelTests
{
    private static FinancialDocumentsGetCompositionService Composition(FirmPayableSeed s) => new(s.Db);

    private static Task ComposeAsync(FirmPayableSeed s, Guid firmKey) =>
        new FinancialDocumentsComposeService(s.Db, new FinancialDocumentChangeLogService(s.Context))
            .ExecuteAsync(s.Provisional.Key, [firmKey], "ana");

    [Fact]
    public async Task Provisional_shows_its_lines_and_the_composable_candidates()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 25_000m);
        var c1 = await seed.ConciliateAsync(taxNumber: "1001");
        var c2 = await seed.ConciliateAsync(taxNumber: "1002");
        var r = await seed.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return,
            transactionType: StorageTransactionType.PurchaseReturn, taxNumber: "2001");
        var other = await seed.Fiscal.AddContractAsync("PC-2");
        await seed.ConciliateAsync(contract: other, taxNumber: "3001");
        await ComposeAsync(seed, c1.Firm!.Key);

        var dto = await Composition(seed).ExecuteAsync(seed.Provisional.Key);

        var line = Assert.Single(dto.Lines);
        Assert.Equal(c1.Firm.Key, line.DocumentKey);
        Assert.Equal("ST-0001", line.DocumentCode);
        Assert.Equal("FN-PROV-1", line.ProvisionalCode);
        Assert.Equal(19_728.33m, line.Amount);
        Assert.Equal("1001-1", line.TaxDocumentNumber);
        Assert.Equal(c1.Transaction.Code, line.StorageTransactionCode);
        Assert.Equal("Firm", line.Nature);

        Assert.Equal(2, dto.Candidates.Count);
        Assert.Equal(5_271.67m, dto.Candidates.Single(x => x.DocumentKey == c2.Firm!.Key).SuggestedAmount);
        var refund = dto.Candidates.Single(x => x.DocumentKey == r.Firm!.Key);
        Assert.Equal("PurchaseReturn", refund.Nature);
        Assert.Equal(-2_959.25m, refund.SuggestedAmount);
    }

    [Fact]
    public async Task Firm_shows_where_it_is_composed()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);

        var dto = await Composition(seed).ExecuteAsync(c.Firm.Key);

        Assert.Equal(seed.Provisional.Key, Assert.Single(dto.Lines).ProvisionalDocumentKey);
        Assert.Equal("FN-PROV-1", dto.Lines[0].ProvisionalCode);
        Assert.Empty(dto.Candidates);
    }

    [Fact]
    public async Task Return_lists_only_loose_firms_as_netting_candidates()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 15_000m);
        var composed = await seed.ConciliateAsync(taxNumber: "1001");
        var loose = await seed.ConciliateAsync(taxNumber: "1002");
        await ComposeAsync(seed, composed.Firm!.Key);
        var r = await seed.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return,
            transactionType: StorageTransactionType.PurchaseReturn, taxNumber: "2001");

        var dto = await Composition(seed).ExecuteAsync(r.Firm!.Key);

        var candidate = Assert.Single(dto.Candidates);
        Assert.Equal(loose.Firm!.Key, candidate.DocumentKey);
        Assert.Equal(2_959.25m, candidate.SuggestedAmount);
    }

    [Fact]
    public async Task Contract_financial_section_brings_composed_invoice_and_transaction()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);

        var rows = (await new FinancialDocumentsGetByContractService(seed.Db).ExecuteAsync("Purchase", seed.Contract.Key)).ToList();

        Assert.Equal(19_728.33m, rows.Single(x => x.Key == seed.Provisional.Key).ComposedAmount);
        var firm = rows.Single(x => x.Key == c.Firm.Key);
        Assert.Equal("Firm", firm.Nature);
        Assert.Equal("1001-1", firm.TaxDocumentNumber);
        Assert.Equal(c.Transaction.Code, firm.StorageTransactionCode);
    }

    [Fact]
    public async Task Conciliation_link_shows_the_firm_and_its_situation()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        var loose = Assert.Single(await seed.Reconciliation().ListLinksAsync(c.Transaction.Key));
        Assert.Equal(c.Firm!.Key, loose.FirmDocumentKey);
        Assert.Equal("ST-0001", loose.FirmDocumentCode);
        Assert.Equal("Solto", loose.FirmSituation);

        await ComposeAsync(seed, c.Firm.Key);
        Assert.Equal("Composto", Assert.Single(await seed.Reconciliation().ListLinksAsync(c.Transaction.Key)).FirmSituation);

        var row = Assert.Single(seed.Context.FinancialDocumentCompositions);
        await new FinancialDocumentsUncomposeService(seed.Db).ExecuteAsync(row.Key, "ana");
        c.Line.UsageCode = seed.NonFinancialUsageCode;       // natureza trocada por uma que não gera financeiro (D10)
        await seed.Db.SaveChangesAsync();
        await seed.SaveLinkAsync(c, 10_000m);
        Assert.Equal("Cancelado", Assert.Single(await seed.Reconciliation().ListLinksAsync(c.Transaction.Key)).FirmSituation);
    }

    [Fact]
    public void New_dtos_keep_pascal_case_names()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        object[] dtos =
        [
            new FinancialDocumentCompositionDto { Lines = [new()], Candidates = [new()] },
            new FinancialDocumentCompositionLineDto(),
            new FinancialDocumentCompositionCandidateDto(),
            new FinancialDocumentByContractDto(),
        ];

        foreach (var dto in dtos)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(dto, dto.GetType(), web));
            Assert.Equal(dto.GetType().GetProperties().Select(p => p.Name).Order().ToList(),
                doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToList());
        }
    }
}
```

`git add` o arquivo. (O `FiscalReconciliationDtoSerializationTests` existente já cobre o PascalCase dos campos novos do `FiscalReconciliationLinkDto`.)

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsReadModelTests"`
Expected: erro de compilação (DTOs e serviço novos não existem).

- [ ] **Step 3: DTOs**

`SiagroB1.Domain/Dtos/FinancialDocumentCompositionDto.cs`:

```csharp
using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Function FinancialDocumentsGetComposition (spec 2026-10-13, títulos firmes). PascalCase explícito: sem os
/// atributos o ASP.NET serializa camelCase e a tela lê undefined.
/// </summary>
public class FinancialDocumentCompositionDto
{
    [JsonPropertyName("Lines")]
    public List<FinancialDocumentCompositionLineDto> Lines { get; set; } = [];

    [JsonPropertyName("Candidates")]
    public List<FinancialDocumentCompositionCandidateDto> Candidates { get; set; } = [];
}

/// <summary>Uma linha de composição: o título composto (firme ou devolução, valor assinado) num provisório.</summary>
public class FinancialDocumentCompositionLineDto
{
    [JsonPropertyName("CompositionKey")] public Guid CompositionKey { get; set; }
    [JsonPropertyName("ProvisionalDocumentKey")] public Guid ProvisionalDocumentKey { get; set; }
    [JsonPropertyName("ProvisionalCode")] public string? ProvisionalCode { get; set; }
    [JsonPropertyName("DocumentKey")] public Guid DocumentKey { get; set; }
    [JsonPropertyName("DocumentCode")] public string? DocumentCode { get; set; }
    [JsonPropertyName("Nature")] public string? Nature { get; set; }
    [JsonPropertyName("StorageTransactionCode")] public string? StorageTransactionCode { get; set; }
    [JsonPropertyName("TaxDocumentNumber")] public string? TaxDocumentNumber { get; set; }
    [JsonPropertyName("CardCode")] public string? CardCode { get; set; }
    [JsonPropertyName("CardName")] public string? CardName { get; set; }
    [JsonPropertyName("Amount")] public decimal Amount { get; set; }
    [JsonPropertyName("CreatedAt")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("CreatedBy")] public string? CreatedBy { get; set; }
}

/// <summary>Título que pode entrar no provisório (composição) ou compensar a devolução (encontro de contas).</summary>
public class FinancialDocumentCompositionCandidateDto
{
    [JsonPropertyName("DocumentKey")] public Guid DocumentKey { get; set; }
    [JsonPropertyName("Code")] public string? Code { get; set; }
    [JsonPropertyName("Nature")] public string? Nature { get; set; }
    [JsonPropertyName("StorageTransactionCode")] public string? StorageTransactionCode { get; set; }
    [JsonPropertyName("TaxDocumentNumber")] public string? TaxDocumentNumber { get; set; }
    [JsonPropertyName("CardCode")] public string? CardCode { get; set; }
    [JsonPropertyName("CardName")] public string? CardName { get; set; }
    [JsonPropertyName("DocumentDate")] public DateTime DocumentDate { get; set; }
    [JsonPropertyName("OpenAmount")] public decimal OpenAmount { get; set; }

    /// <summary>Firme: min(saldo, espaço no provisório). Devolução: −saldo. Netting: min dos dois saldos.</summary>
    [JsonPropertyName("SuggestedAmount")] public decimal SuggestedAmount { get; set; }
}
```

`git add` o arquivo.

`FinancialDocumentByContractDto.cs` — acrescentar no fim da classe:

```csharp
    [JsonPropertyName("ComposedAmount")] public decimal ComposedAmount { get; set; }
    [JsonPropertyName("StorageTransactionCode")] public string? StorageTransactionCode { get; set; }
    [JsonPropertyName("TaxDocumentNumber")] public string? TaxDocumentNumber { get; set; }
```

`FiscalReconciliationDtos.cs`, em `FiscalReconciliationLinkDto`, depois de `CreatedBy`:

```csharp
    /// <summary>Título firme/devolução do vínculo (spec 2026-10-13, títulos firmes); o ativo, ou o último cancelado.</summary>
    [JsonPropertyName("FirmDocumentKey")]
    public Guid? FirmDocumentKey { get; set; }
    [JsonPropertyName("FirmDocumentCode")]
    public string? FirmDocumentCode { get; set; }
    /// <summary>"Solto", "Composto" ou "Cancelado" (PurchaseFirmPayableRules.SituationName).</summary>
    [JsonPropertyName("FirmSituation")]
    public string? FirmSituation { get; set; }
```

- [ ] **Step 4: Serviço de leitura da composição**

`SiagroB1.Application/Services/Financials/FinancialDocumentsGetCompositionService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Leitura da composição de um título (spec 2026-10-13, títulos firmes) — a function basta; não existe entity set de
/// composição. Projeção em memória porque OpenAmount é [NotMapped].
/// </summary>
public class FinancialDocumentsGetCompositionService(IUnitOfWork db)
{
    public async Task<FinancialDocumentCompositionDto> ExecuteAsync(Guid key)
    {
        var context = db.Context;
        var document = await context.FinancialDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        var rows = await context.FinancialDocumentCompositions.AsNoTracking()
            .Where(c => c.ProvisionalDocumentKey == key || c.ComposedDocumentKey == key)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        var documentKeys = rows.SelectMany(r => new[] { r.ProvisionalDocumentKey, r.ComposedDocumentKey }).Distinct().ToList();
        var documents = await context.FinancialDocuments.AsNoTracking()
            .Where(d => documentKeys.Contains(d.Key))
            .ToDictionaryAsync(d => d.Key);

        var lines = rows.Select(r =>
        {
            var composed = documents[r.ComposedDocumentKey];
            return new FinancialDocumentCompositionLineDto
            {
                CompositionKey = r.Key,
                ProvisionalDocumentKey = r.ProvisionalDocumentKey,
                ProvisionalCode = documents[r.ProvisionalDocumentKey].Code,
                DocumentKey = composed.Key,
                DocumentCode = composed.Code,
                Nature = composed.Nature.ToString(),
                StorageTransactionCode = composed.StorageTransactionCode,
                TaxDocumentNumber = composed.TaxDocumentNumber,
                CardCode = composed.CardCode,
                CardName = composed.CardName,
                Amount = r.Amount,
                CreatedAt = r.CreatedAt,
                CreatedBy = r.CreatedBy,
            };
        }).ToList();

        List<FinancialDocumentCompositionCandidateDto> candidates = document.Status == FinancialDocumentStatus.Canceled
            ? []
            : document.Nature switch
            {
                FinancialDocumentNature.Provisional => await ComposeCandidatesAsync(document),
                FinancialDocumentNature.PurchaseReturn when document.IsPurchaseFirmPayable => await NettingCandidatesAsync(document),
                _ => [],
            };

        return new FinancialDocumentCompositionDto { Lines = lines, Candidates = candidates };
    }

    private async Task<List<FinancialDocumentCompositionCandidateDto>> ComposeCandidatesAsync(FinancialDocument provisional)
    {
        var open = await OpenFirmPayablesAsync(provisional.PurchaseContractKey);
        var space = Math.Max(0m, provisional.NetAmount - provisional.ComposedAmount);

        return open
            .Select(d => Candidate(d, d.Nature == FinancialDocumentNature.Firm ? Math.Min(d.OpenAmount, space) : -d.OpenAmount))
            .ToList();
    }

    private async Task<List<FinancialDocumentCompositionCandidateDto>> NettingCandidatesAsync(FinancialDocument refund)
    {
        var firms = (await OpenFirmPayablesAsync(refund.PurchaseContractKey))
            .Where(d => d.Nature == FinancialDocumentNature.Firm)
            .ToList();
        var firmKeys = firms.Select(f => f.Key).ToList();
        var composed = (await db.Context.FinancialDocumentCompositions.AsNoTracking()
                .Where(c => firmKeys.Contains(c.ComposedDocumentKey))
                .Select(c => c.ComposedDocumentKey)
                .ToListAsync())
            .ToHashSet();

        return firms
            .Where(f => !composed.Contains(f.Key))
            .Select(f => Candidate(f, Math.Min(f.OpenAmount, refund.OpenAmount)))
            .ToList();
    }

    /// <summary>Firmes e devoluções de compra (conciliação e complemento de valor, D11), do mesmo contrato, não cancelados e com saldo.</summary>
    private async Task<List<FinancialDocument>> OpenFirmPayablesAsync(Guid? contractKey)
    {
        // Sem contrato não há candidato: comparar com chave nula casaria títulos sem contrato.
        if (contractKey is null) return [];

        return await db.Context.FinancialDocuments.AsNoTracking()
            .Where(d => d.PurchaseContractKey == contractKey &&
                        (d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation ||
                         d.OriginType == FinancialDocumentOrigin.PurchaseInvoice) &&
                        (d.Nature == FinancialDocumentNature.Firm || d.Nature == FinancialDocumentNature.PurchaseReturn) &&
                        d.Status != FinancialDocumentStatus.Canceled &&
                        d.NetAmount > d.SettledAmount)
            .OrderBy(d => d.DocumentDate).ThenBy(d => d.Code)
            .ToListAsync();
    }

    private static FinancialDocumentCompositionCandidateDto Candidate(FinancialDocument d, decimal suggested) => new()
    {
        DocumentKey = d.Key,
        Code = d.Code,
        Nature = d.Nature.ToString(),
        StorageTransactionCode = d.StorageTransactionCode,
        TaxDocumentNumber = d.TaxDocumentNumber,
        CardCode = d.CardCode,
        CardName = d.CardName,
        DocumentDate = d.DocumentDate,
        OpenAmount = d.OpenAmount,
        SuggestedAmount = suggested,
    };
}
```

`git add` o arquivo. DI, depois de `services.AddScoped<FinancialDocumentsNettingService>();`:

```csharp
        services.AddScoped<FinancialDocumentsGetCompositionService>();
```

- [ ] **Step 5: Financeiro do contrato**

`FinancialDocumentsGetByContractService.ExecuteAsync` — no `Select` da projeção, depois de `OpenAmount = x.OpenAmount`:

```csharp
            OpenAmount = x.OpenAmount,
            ComposedAmount = x.ComposedAmount,
            StorageTransactionCode = x.StorageTransactionCode,
            TaxDocumentNumber = x.TaxDocumentNumber
```

- [ ] **Step 6: Firme na conciliação**

`PurchaseFiscalReconciliationQuery.LoadLinksAsync` completo (using `SiagroB1.Domain.Enums` se faltar):

```csharp
    public static async Task<List<FiscalReconciliationLinkDto>> LoadLinksAsync(AppDbContext ctx, IReadOnlyCollection<Guid> transactionKeys)
    {
        var rows = await LinksQuery(ctx, transactionKeys).ToListAsync();

        // Spec 2026-10-13 (títulos firmes): o firme de cada vínculo — o ativo, ou o último cancelado — e a situação.
        var linkKeys = rows.Select(r => r.Key).ToList();
        var firms = await ctx.FinancialDocuments.AsNoTracking()
            .Where(d => d.OriginType == FinancialDocumentOrigin.PurchaseFiscalReconciliation &&
                        d.OriginKey != null && linkKeys.Contains(d.OriginKey.Value))
            .Select(d => new { d.Key, d.Code, OriginKey = d.OriginKey!.Value, d.Status, d.CreatedAt })
            .ToListAsync();
        var firmKeys = firms.Select(f => f.Key).ToList();
        var composed = (await ctx.FinancialDocumentCompositions.AsNoTracking()
                .Where(c => firmKeys.Contains(c.ComposedDocumentKey))
                .Select(c => c.ComposedDocumentKey)
                .ToListAsync())
            .ToHashSet();
        var firmByLink = firms
            .GroupBy(f => f.OriginKey)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(f => f.Status == FinancialDocumentStatus.Canceled ? 1 : 0)
                .ThenByDescending(f => f.CreatedAt)
                .First());

        return rows
            .Select(r =>
            {
                var firm = firmByLink.GetValueOrDefault(r.Key);
                return new FiscalReconciliationLinkDto
                {
                    Key = r.Key, PurchaseInvoiceItemKey = r.PurchaseInvoiceItemKey, PurchaseInvoiceKey = r.PurchaseInvoiceKey,
                    StorageTransactionKey = r.StorageTransactionKey,
                    TaxDocument = R.TaxDocument(r.TaxDocumentNumber, r.TaxDocumentSeries, r.InvoiceNumber),
                    CardCode = r.CardCode, CardName = r.CardName, IssueDate = r.IssueDate, Quantity = r.Quantity,
                    CreatedAt = r.CreatedAt, CreatedBy = r.CreatedBy,
                    FirmDocumentKey = firm?.Key,
                    FirmDocumentCode = firm?.Code,
                    FirmSituation = firm is null ? null : PurchaseFirmPayableRules.SituationName(firm.Status, composed.Contains(firm.Key)),
                };
            })
            .OrderBy(l => l.IssueDate).ThenBy(l => l.TaxDocument, StringComparer.Ordinal)
            .ToList();
    }
```

- [ ] **Step 7: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsReadModelTests|FullyQualifiedName~PurchaseFiscalReconciliations|FullyQualifiedName~Reports"`
Expected: todos PASS (o relatório da conciliação usa a mesma consulta no projeto Reports; o build da solução confirma).

- [ ] **Step 8: Stage**

```bash
dotnet build SiagroB1.sln
git add SiagroB1.Domain/Dtos SiagroB1.Application/Services/Financials/FinancialDocumentsGetCompositionService.cs SiagroB1.Application/Services/Financials/FinancialDocumentsGetByContractService.cs SiagroB1.Infra/Queries/PurchaseFiscalReconciliationQuery.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsReadModelTests.cs
```
O controlador commita com:

```
feat(financial): leituras da composição, do financeiro do contrato e do firme na conciliação

O Detail do título lê a composição (onde o firme está composto e o que cabe no provisório, com
o valor sugerido), a seção Financeiro do contrato ganha composto, NF e romaneio, e cada vínculo
da Conciliação Fiscal mostra o título firme e se ele está solto, composto ou cancelado.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 12: Endpoints OData (actions, function) e testes de EDM

**Files:**
- Create: `SiagroB1.Web/Actions/Financials/FinancialDocumentsComposeController.cs`, `FinancialDocumentsUncomposeController.cs`, `FinancialDocumentsDationController.cs`, `FinancialDocumentsNettingController.cs`
- Create: `SiagroB1.Web/Functions/Financials/FinancialDocumentsGetCompositionController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (logo depois de `financialByContract.ReturnsCollection<FinancialDocumentByContractDto>();`)
- Test: `SiagroB1.Application.Tests/Financials/FinancialFirmPayablesEdmModelTests.cs`

**Interfaces:**
- Consumes: serviços das Tasks 6–8 e 11.
- Produces (o frontend chama por `sendJson`):
  - `POST odata/FinancialDocumentsCompose` `{ Key: Guid, ComposedKeys: Guid[] }`
  - `POST odata/FinancialDocumentsUncompose` `{ CompositionKey: Guid }`
  - `POST odata/FinancialDocumentsDation` `{ Key: Guid, Amount: number, SettlementDate: "yyyy-MM-dd", Notes: string }`
  - `POST odata/FinancialDocumentsNetting` `{ Key: Guid (devolução), CounterpartKey: Guid (firme) }`
  - `GET odata/FinancialDocumentsGetComposition(Key={guid})` → `FinancialDocumentCompositionDto`
  - Erro de negócio: `400` com o texto da mensagem (`BadRequest(string)`); não encontrado: `404`.

- [ ] **Step 1: Teste de EDM que falha**

`SiagroB1.Application.Tests/Financials/FinancialFirmPayablesEdmModelTests.cs`:

```csharp
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>EDM dos títulos firmes (spec 2026-10-13): chaves Guid, dinheiro Edm.Double, function com Key.</summary>
public class FinancialFirmPayablesEdmModelTests
{
    private static IEdmModel BuildModel()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        return builder.GetEdmModel();
    }

    private static string ParameterType(string action, string parameter) =>
        BuildModel().SchemaElements.OfType<IEdmAction>().Single(a => a.Name == action)
            .Parameters.Single(p => p.Name == parameter).Type.FullName();

    [Theory]
    [InlineData("FinancialDocumentsCompose", "Key", "Edm.Guid")]
    [InlineData("FinancialDocumentsCompose", "ComposedKeys", "Collection(Edm.Guid)")]
    [InlineData("FinancialDocumentsUncompose", "CompositionKey", "Edm.Guid")]
    [InlineData("FinancialDocumentsDation", "Key", "Edm.Guid")]
    [InlineData("FinancialDocumentsDation", "Amount", "Edm.Double")]
    [InlineData("FinancialDocumentsDation", "SettlementDate", "Edm.String")]
    [InlineData("FinancialDocumentsDation", "Notes", "Edm.String")]
    [InlineData("FinancialDocumentsNetting", "Key", "Edm.Guid")]
    [InlineData("FinancialDocumentsNetting", "CounterpartKey", "Edm.Guid")]
    public void Action_parameters_have_the_expected_types(string action, string parameter, string type)
    {
        Assert.Equal(type, ParameterType(action, parameter));
    }

    [Fact]
    public void The_composition_function_takes_the_key()
    {
        var function = BuildModel().SchemaElements.OfType<IEdmFunction>().Single(f => f.Name == "FinancialDocumentsGetComposition");

        Assert.Equal("Edm.Guid", function.Parameters.Single(p => p.Name == "Key").Type.FullName());
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialFirmPayablesEdmModelTests"`
Expected: FAIL — `Sequence contains no matching element`.

- [ ] **Step 3: EDM**

`ODataConfigurations.cs`, logo depois de `financialByContract.ReturnsCollection<FinancialDocumentByContractDto>();`:

```csharp

        // Títulos firmes de compra (spec 2026-10-13): composição no provisório, dação e encontro de contas.
        // ⚠️ Dinheiro em Edm.Double e data em string, como na baixa. Nenhum parâmetro é opcional: uma rota só por operação.
        var financialCompose = modelBuilder.Action("FinancialDocumentsCompose");
        financialCompose.Parameter<Guid>("Key");
        financialCompose.CollectionParameter<Guid>("ComposedKeys");
        financialCompose.Returns<IActionResult>();

        var financialUncompose = modelBuilder.Action("FinancialDocumentsUncompose");
        financialUncompose.Parameter<Guid>("CompositionKey");
        financialUncompose.Returns<IActionResult>();

        var financialDation = modelBuilder.Action("FinancialDocumentsDation");
        financialDation.Parameter<Guid>("Key");
        financialDation.Parameter<double>("Amount");
        financialDation.Parameter<string>("SettlementDate");
        financialDation.Parameter<string>("Notes");
        financialDation.Returns<IActionResult>();

        var financialNetting = modelBuilder.Action("FinancialDocumentsNetting");
        financialNetting.Parameter<Guid>("Key");
        financialNetting.Parameter<Guid>("CounterpartKey");
        financialNetting.Returns<IActionResult>();

        var financialComposition = modelBuilder.Function("FinancialDocumentsGetComposition");
        financialComposition.Parameter<Guid>("Key");
        financialComposition.Returns<FinancialDocumentCompositionDto>();
```

- [ ] **Step 4: Controllers**

`SiagroB1.Web/Actions/Financials/FinancialDocumentsComposeController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>Compor firmes/devoluções no provisório (spec 2026-10-13, regras 9–12). ComposedKeys na ordem do operador.</summary>
public class FinancialDocumentsComposeController(FinancialDocumentsComposeService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsCompose")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // ODataActionParameters chega NULL quando o corpo não casa com nenhum parâmetro do EDM.
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o título provisório.");

            List<Guid> composedKeys = parameters.TryGetValue("ComposedKeys", out var keysObj) && keysObj is IEnumerable<Guid> keys
                ? keys.ToList()
                : [];

            await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!), composedKeys, User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

`SiagroB1.Web/Actions/Financials/FinancialDocumentsUncomposeController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>Desfaz UMA linha de composição (spec 2026-10-13, regra 13). O parâmetro é a chave da LINHA de composição.</summary>
public class FinancialDocumentsUncomposeController(FinancialDocumentsUncomposeService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsUncompose")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("CompositionKey", out var keyObj) || keyObj is null)
                return BadRequest("Informe a composição a desfazer.");

            await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!), User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

`SiagroB1.Web/Actions/Financials/FinancialDocumentsDationController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>Dação da sobra do firme (spec 2026-10-13, regra 17). Amount é Edm.Double; SettlementDate, string ISO.</summary>
public class FinancialDocumentsDationController(FinancialDocumentsDationService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsDation")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o título firme.");

            if (!parameters.TryGetValue("Amount", out var amountObj) || amountObj is null)
                return BadRequest("Informe o valor da dação.");

            if (!parameters.TryGetValue("SettlementDate", out var dateObj) || dateObj is null)
                return BadRequest("Informe a data da dação.");

            // TryGetValue devolve TRUE com valor null em parâmetro presente: sempre value?.ToString().
            parameters.TryGetValue("Notes", out var notesObj);

            await service.ExecuteAsync(
                Guid.Parse(keyObj.ToString()!),
                Convert.ToDecimal(amountObj),
                DateTime.Parse(dateObj.ToString()!),
                notesObj?.ToString(),
                User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

`SiagroB1.Web/Actions/Financials/FinancialDocumentsNettingController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Financials;

/// <summary>"Compensar com firme" (spec 2026-10-13, regra 20): Key = a devolução; CounterpartKey = o firme solto.</summary>
public class FinancialDocumentsNettingController(FinancialDocumentsNettingService service) : ODataController
{
    [HttpPost("odata/FinancialDocumentsNetting")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (parameters is null) return BadRequest("Corpo da requisição ausente ou inválido.");

        try
        {
            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj is null)
                return BadRequest("Informe o título de devolução.");

            if (!parameters.TryGetValue("CounterpartKey", out var counterpartObj) || counterpartObj is null)
                return BadRequest("Informe o título firme a compensar.");

            await service.ExecuteAsync(
                Guid.Parse(keyObj.ToString()!), Guid.Parse(counterpartObj.ToString()!), User.Identity?.Name ?? "Unknown");

            return Ok();
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

`SiagroB1.Web/Functions/Financials/FinancialDocumentsGetCompositionController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Financials;

/// <summary>Composição de um título (spec 2026-10-13): Lines e Candidates, DTO PascalCase.</summary>
public class FinancialDocumentsGetCompositionController(FinancialDocumentsGetCompositionService service) : ODataController
{
    [HttpGet("odata/FinancialDocumentsGetComposition(Key={key})")]
    public async Task<IActionResult> Get([FromRoute] Guid key)
    {
        try
        {
            return Ok(await service.ExecuteAsync(key));
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException) return NotFound(e.Message);
            return BadRequest(e.Message);
        }
    }
}
```

`git add` os cinco arquivos.

- [ ] **Step 5: Rodar e gates**

```bash
dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialFirmPayablesEdmModelTests|FullyQualifiedName~FinancialEdmModelTests"
dotnet build SiagroB1.sln
dotnet test SiagroB1.Application.Tests
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Expected: EDM PASS; build OK; suíte inteira verde (anotar a contagem); sem mudança de modelo pendente.

- [ ] **Step 6: Stage**

```bash
git add SiagroB1.Web/Actions/Financials SiagroB1.Web/Functions/Financials SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Application.Tests/Financials/FinancialFirmPayablesEdmModelTests.cs
```
O controlador commita com:

```
feat(financial): endpoints de composição, dação e encontro de contas

Actions para compor e desfazer no provisório, baixar por dação e compensar a devolução com
firme, e a function que lê a composição de um título. Chaves Guid, valor em Edm.Double e
recusas de negócio como 400 com a mensagem.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 13: Complemento de valor (D11) — tipo, valor da linha e validação do documento de entrada

**Files:**
- Modify: `SiagroB1.Domain/Enums/PurchaseInvoiceType.cs`
- Modify: `SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs` (`ComplementAmount`, `Total`)
- Create: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceValueComplement.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesTaxApplyService.cs` (`EnsureValueComplementAsync`, `IsActiveForAsync`)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesCreateService.cs`, `PurchaseInvoicesUpdateService.cs` (cópia no `SyncItemsAsync` + validação), `PurchaseInvoicesItemsCreateService.cs`, `PurchaseInvoicesItemsUpdateService.cs`
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceDeclaredTotal.cs`, `SupplierNfeKeyGuard.cs` (`AppliesTo`)
- Modify: `SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs` (`EnsureIssuableType`)
- Modify: `SiagroB1.Reports/Services/PurchaseInvoicesByPeriodReportService.cs` (`DescribeType`), `SiagroB1.Reports/Services/FunruralWithholdingReportService.cs` (filtro do tipo)
- Create: migration `AddPurchaseInvoiceItemComplementAmount` (AppDbContext)
- Test: `SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceValueComplementTests.cs`

**Interfaces:**
- Consumes: Task 5 (`FiscalReconciliationSeed`), suporte existente (`TaxTestServices.PurchaseApply`, `FakeItemService`, `FakeBusinessPartnerService`, `FakeDocNumberSequenceService`).
- Produces:
  - `PurchaseInvoiceType.ValueComplement = 2`.
  - `PurchaseInvoiceItem.ComplementAmount` (`DECIMAL(18,2) DEFAULT 0`); `Total = Round(Quantity × UnitPrice, 2) + ComplementAmount` (`GrandTotal`, tributos, Funrural e totais passam a enxergá-lo sem caso especial).
  - `static class PurchaseInvoiceValueComplement`: `Task EnsureAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, IUsage usageService, bool standalone)` (mensagens P11). A Task 14 reusa na confirmação.
  - `PurchaseInvoicesTaxApplyService.EnsureValueComplementAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines)` — porta usada pelos quatro serviços de gravação (sem mudar construtor nenhum: o serviço de tributos já é injetado em todos).

Definição de "linha só-valor" (o spec diz "sem quantidade, só valor"): `Quantity = 0`, `UnitPrice = 0`, valor em `ComplementAmount > 0`. É o desenho da NF-e de complemento (qCom 0, vProd = valor) e mantém frete/seguro/desconto/outras e o Funrural funcionando pelo `GrandTotal`. Nos demais tipos `ComplementAmount` é sempre 0.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceValueComplementTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Spec 2026-10-13 D11: documento de entrada "Complemento de valor" — linhas só com valor, contrato e natureza.</summary>
public class PurchaseInvoiceValueComplementTests
{
    private sealed record Scenario(UnitOfWork Db, PurchaseContract Contract, int Financial, int NonFinancial);

    private static async Task<Scenario> SeedAsync()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var contract = await seed.AddContractAsync();
        var usages = new UsageService(seed.Db, NullLogger<UsageService>.Instance);
        var financial = (await usages.CreateAsync(Usage("Complemento de preço", true))).Code;
        var nonFinancial = (await usages.CreateAsync(Usage("Entrada sem financeiro", false))).Code;
        return new Scenario(seed.Db, contract, financial, nonFinancial);
    }

    private static UsageModel Usage(string name, bool createsFinancial) => new()
    {
        Name = name, Direction = UsageDirection.Incoming, CfopIncomingInState = "1102", CfopIncomingOutState = "2102",
        CreatesFinancialDocument = createsFinancial,
    };

    private static PurchaseInvoice Document(
        Scenario s, int? usageCode, decimal amount = 1_500m, decimal quantity = 0m,
        PurchaseInvoiceType type = PurchaseInvoiceType.ValueComplement,
        DocumentIssuerType issuer = DocumentIssuerType.ThirdParty, bool withContract = true)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = FiscalReconciliationSeed.Supplier, InvoiceType = type,
            IssuerType = issuer, TaxDocumentNumber = "5001", TaxDocumentSeries = "1", IssueDate = new DateTime(2026, 10, 12),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = FiscalReconciliationSeed.Product, ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
            Quantity = quantity, UnitPrice = quantity > 0 ? 2m : 0m, ComplementAmount = amount, UsageCode = usageCode,
            PurchaseContractKey = withContract ? s.Contract.Key : null,
        });
        return invoice;
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db, string erp) =>
        new(db, new FakeBusinessPartnerService(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, new FakeBusinessPartnerService(), erp), new FakeDocNumberSequenceService());

    private static async Task<string> RefusalAsync(Scenario s, PurchaseInvoice invoice, string erp = "STANDALONE") =>
        (await Assert.ThrowsAsync<DefaultException>(() => Create(s.Db, erp).ExecuteAsync(invoice, "ana"))).Message;

    [Fact]
    public void Complement_line_total_is_its_amount()
    {
        var line = new PurchaseInvoiceItem { ComplementAmount = 1_500m, FreightValue = 10m };

        Assert.Equal(1_500m, line.Total);
        Assert.Equal(1_510m, line.GrandTotal);
    }

    [Fact]
    public async Task Valid_value_complement_is_saved()
    {
        var s = await SeedAsync();

        await Create(s.Db, "STANDALONE").ExecuteAsync(Document(s, s.Financial), "ana");

        var line = await s.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal(1_500m, line.ComplementAmount);
        Assert.Equal(PurchaseInvoiceType.ValueComplement,
            (await s.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync()).InvoiceType);
    }

    [Fact]
    public async Task Sapb1_accepts_a_nature_without_the_flag()
    {
        var s = await SeedAsync();

        await Create(s.Db, "SAPB1").ExecuteAsync(Document(s, s.NonFinancial), "ana");

        Assert.Single(s.Db.Context.PurchaseInvoices.AsNoTracking());
    }

    [Fact]
    public async Task Value_complement_refusals()
    {
        var s = await SeedAsync();

        Assert.Equal("Item SOJA: no complemento de valor a linha não tem quantidade nem preço unitário; informe só o valor do complemento.",
            await RefusalAsync(s, Document(s, s.Financial, quantity: 10m)));
        Assert.Equal("Item SOJA: informe o valor do complemento.",
            await RefusalAsync(s, Document(s, s.Financial, amount: 0m)));
        Assert.Equal("Item SOJA: o complemento de valor exige o contrato de compra na linha.",
            await RefusalAsync(s, Document(s, s.Financial, withContract: false)));
        Assert.Equal("Item SOJA: o complemento de valor exige a natureza de operação.",
            await RefusalAsync(s, Document(s, null)));
        Assert.Equal($"Item SOJA: a natureza {s.NonFinancial} não gera financeiro; o complemento de valor precisa de uma natureza que gere.",
            await RefusalAsync(s, Document(s, s.NonFinancial)));
        Assert.Equal("Complemento de valor é só escrituração por enquanto: lance-o como documento de terceiro. A emissão de NF-e de complemento vem depois.",
            await RefusalAsync(s, Document(s, s.Financial, issuer: DocumentIssuerType.Own)));
        Assert.Empty(s.Db.Context.PurchaseInvoices.AsNoTracking());
    }

    [Fact]
    public async Task Other_types_cannot_carry_a_complement_amount()
    {
        var s = await SeedAsync();

        Assert.Equal("Item SOJA: valor de complemento só existe no documento do tipo Complemento de valor.",
            await RefusalAsync(s, Document(s, s.Financial, quantity: 1_000m, type: PurchaseInvoiceType.Normal)));
    }

    [Fact]
    public async Task Line_create_and_update_apply_the_same_rules()
    {
        var s = await SeedAsync();
        var invoice = Document(s, s.Financial);
        await Create(s.Db, "STANDALONE").ExecuteAsync(invoice, "ana");
        var apply = TaxTestServices.PurchaseApply(s.Db, new FakeBusinessPartnerService(), "STANDALONE");

        var extra = new PurchaseInvoiceItem
        {
            PurchaseInvoiceKey = invoice.Key, ItemCode = FiscalReconciliationSeed.Product, ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG", Quantity = 5m, UnitPrice = 2m, UsageCode = s.Financial, PurchaseContractKey = s.Contract.Key,
        };
        var created = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(s.Db, new FakeItemService(), apply).ExecuteAsync(extra, "ana"));
        Assert.StartsWith("Item SOJA: no complemento de valor a linha não tem quantidade", created.Message);

        s.Db.Context.ChangeTracker.Clear();
        var line = await s.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync();
        line.PurchaseContractKey = null;
        var updated = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsUpdateService(s.Db, new FakeItemService(), apply).ExecuteAsync(line.Key!.Value, line, "ana"));
        Assert.Equal("Item SOJA: o complemento de valor exige o contrato de compra na linha.", updated.Message);
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceValueComplementTests"`
Expected: erro de compilação (`ValueComplement`, `ComplementAmount` não existem).

- [ ] **Step 3: Enum e linha**

`PurchaseInvoiceType.cs` — trocar o `<summary>` "Só dois, simétrico a…" por "Normal e Devolução, simétricos a <see cref=\"SalesInvoiceType\"/>, e o Complemento de valor (spec 2026-10-13 D11)." e o enum por:

```csharp
public enum PurchaseInvoiceType
{
    Normal = 0,
    Return = 1,

    /// <summary>
    /// Complemento de VALOR (spec 2026-10-13 D11): o produtor faturou abaixo do contrato e emite uma NF só de valor.
    /// Linhas sem quantidade (PurchaseInvoiceItem.ComplementAmount), contrato e natureza obrigatórios; não se concilia
    /// com romaneio; o título firme nasce na confirmação. Só escrituração (terceiro) — a emissão própria é o sub-projeto 2b.
    /// </summary>
    ValueComplement = 2,
}
```

`PurchaseInvoiceItem.cs` — logo antes de `/// <summary>INSS + RAT + SENAR retidos na linha.</summary>`:

```csharp
    /// <summary>
    /// Valor da linha do documento "Complemento de valor" (spec 2026-10-13 D11), que não tem quantidade nem preço
    /// unitário (Quantity = UnitPrice = 0). Soma no <see cref="Total"/> — é o vProd da NF de complemento —, então
    /// GrandTotal, tributos, Funrural e totais o enxergam sem caso especial. Zero em todo outro tipo (validado).
    /// </summary>
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal ComplementAmount { get; set; }

```

e trocar `public decimal Total => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.ToEven);` por

```csharp
    public decimal Total => decimal.Round(Quantity * UnitPrice, 2, MidpointRounding.ToEven) + ComplementAmount;
```

- [ ] **Step 4: Regras do complemento**

`SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoiceValueComplement.cs`:

```csharp
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Documento de entrada "Complemento de valor" (spec 2026-10-13 D11): o faturado ABAIXO do contrato. Regra única
/// das linhas, chamada pelos quatro caminhos que gravam linha (deep-insert, PATCH do cabeçalho, POST e PATCH de
/// linha — ver PurchaseInvoiceLineGuard) e de novo na confirmação, que cria os títulos firmes.
/// </summary>
public static class PurchaseInvoiceValueComplement
{
    /// <param name="standalone">
    /// STANDALONE exige natureza com "Gera financeiro"; em SAPB1 a natureza é o OUSG, sem a flag (D10), e basta existir.
    /// </param>
    public static async Task EnsureAsync(
        PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, IUsage usageService, bool standalone)
    {
        if (invoice.InvoiceType != PurchaseInvoiceType.ValueComplement)
        {
            var carrying = lines.FirstOrDefault(l => l.ComplementAmount != 0m);
            if (carrying is not null)
                throw new DefaultException(
                    $"Item {carrying.ItemCode}: valor de complemento só existe no documento do tipo Complemento de valor.");
            return;
        }

        if (invoice.IssuerType == DocumentIssuerType.Own)
            throw new DefaultException(
                "Complemento de valor é só escrituração por enquanto: lance-o como documento de terceiro. A emissão de NF-e de complemento vem depois.");

        foreach (var line in lines)
        {
            if (line.Quantity != 0m || line.UnitPrice != 0m)
                throw new DefaultException(
                    $"Item {line.ItemCode}: no complemento de valor a linha não tem quantidade nem preço unitário; informe só o valor do complemento.");

            if (line.ComplementAmount <= 0m)
                throw new DefaultException($"Item {line.ItemCode}: informe o valor do complemento.");

            if (line.PurchaseContractKey is null)
                throw new DefaultException($"Item {line.ItemCode}: o complemento de valor exige o contrato de compra na linha.");

            if (line.UsageCode is not { } usageCode)
                throw new DefaultException($"Item {line.ItemCode}: o complemento de valor exige a natureza de operação.");

            if (standalone && (await usageService.GetByIdAsync(usageCode))?.CreatesFinancialDocument != true)
                throw new DefaultException(
                    $"Item {line.ItemCode}: a natureza {usageCode} não gera financeiro; o complemento de valor precisa de uma natureza que gere.");
        }
    }
}
```

`git add` o arquivo.

- [ ] **Step 5: Porta no serviço de tributos e o terceiro "Complemento de valor" segue o Normal**

`PurchaseInvoicesTaxApplyService.cs`:

1. Em `IsActiveForAsync`, trocar
`|| invoice.IssuerType == DocumentIssuerType.ThirdParty && invoice.InvoiceType == PurchaseInvoiceType.Normal)`
por
`|| invoice.IssuerType == DocumentIssuerType.ThirdParty && invoice.InvoiceType is PurchaseInvoiceType.Normal or PurchaseInvoiceType.ValueComplement)`
(o terceiro complemento calcula tributos e Funrural pela natureza, como o Normal — a base é o `GrandTotal`, que já inclui o complemento).

2. Método novo, depois de `IsOwnNfeReturn`:

```csharp
    /// <summary>Spec 2026-10-13 D11: regras do documento "Complemento de valor" (e do valor de complemento nos demais tipos).</summary>
    public Task EnsureValueComplementAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines) =>
        PurchaseInvoiceValueComplement.EnsureAsync(invoice, lines, usageService, gate.IsStandalone);
```

`PurchaseInvoiceDeclaredTotal.AppliesTo` e `SupplierNfeKeyGuard.AppliesTo`: trocar `invoice.InvoiceType == PurchaseInvoiceType.Normal` por `invoice.InvoiceType is PurchaseInvoiceType.Normal or PurchaseInvoiceType.ValueComplement` (valor declarado = soma das linhas e conferência da chave da NF-e do fornecedor valem também para a NF de complemento).

- [ ] **Step 6: Os quatro caminhos de gravação**

1. `PurchaseInvoicesCreateService.ExecuteAsync`, logo depois do `foreach` que resolve descrição e contrato das linhas:

```csharp

        // Spec 2026-10-13 D11: complemento de valor (linhas só-valor, contrato e natureza) — e valor de complemento
        // recusado nos demais tipos.
        await taxApply.EnsureValueComplementAsync(invoice, invoice.Items.ToList());
```

2. `PurchaseInvoicesUpdateService`:
   - em `SyncItemsAsync`, na criação da linha nova (o objeto que recebe `Quantity = line.Quantity,`) acrescentar `ComplementAmount = line.ComplementAmount,`; e na atualização da linha existente, depois de `current.OtherExpensesValue = line.OtherExpensesValue;`, acrescentar `current.ComplementAmount = line.ComplementAmount;`;
   - em `ExecuteAsync`, logo depois de `await SyncItemsAsync(existing, entity);`:

```csharp

        // Spec 2026-10-13 D11 (o tipo é editável enquanto Pendente: a regra vale sobre a lista final).
        await taxApply.EnsureValueComplementAsync(existing, existing.Items.ToList());
```

3. `PurchaseInvoicesItemsCreateService.ExecuteAsync`, logo depois da linha `var others = await db.Context.PurchaseInvoicesItems…ToListAsync();`:

```csharp
        await taxApply.EnsureValueComplementAsync(invoice, [.. others, item]);
```

4. `PurchaseInvoicesItemsUpdateService.ExecuteAsync`: depois de `existing.OtherExpensesValue = entity.OtherExpensesValue;` acrescentar `existing.ComplementAmount = entity.ComplementAmount;`; e logo depois da linha `var others = await db.Context.PurchaseInvoicesItems…ToListAsync();`:

```csharp
        await taxApply.EnsureValueComplementAsync(invoice, [.. others, existing]);
```

- [ ] **Step 7: NF-e própria e relatórios**

1. `PurchaseInvoicesNfeIssueService.EnsureIssuableType`, logo no início:

```csharp
        // Spec 2026-10-13 D11: a NF-e de complemento (finNFe=2, NFref) é o sub-projeto 2b.
        if (invoice.InvoiceType == PurchaseInvoiceType.ValueComplement)
            throw new DefaultException(
                "Complemento de valor não é emitido como NF-e por aqui (fica para o sub-projeto 2b): lance-o como documento de terceiro.");
```

2. `PurchaseInvoicesByPeriodReportService.DescribeType`:

```csharp
    internal static string DescribeType(PurchaseInvoiceType type) => type switch
    {
        PurchaseInvoiceType.Return => "Devolução",
        PurchaseInvoiceType.ValueComplement => "Complemento de valor",
        _ => "Normal",
    };
```

3. `FunruralWithholdingReportService`: trocar `.Where(i => i.PurchaseInvoice!.InvoiceType == PurchaseInvoiceType.Normal)` por `.Where(i => i.PurchaseInvoice!.InvoiceType != PurchaseInvoiceType.Return)` (o Funrural retido no complemento de valor também é retenção a recolher) e acrescentar ao comentário acima: "O complemento de valor (D11) entra."

4. Conferir os demais usos do enum — `grep -rn "PurchaseInvoiceType\." --include=*.cs SiagroB1.Application SiagroB1.Infra SiagroB1.Reports`: custódia (`StorageCustodyGuard`, "precisa ser do tipo Normal"), contra-nota e "Devolver" exigem `Normal` e recusam o complemento como já recusam a devolução — **correto, não mudar**; `PurchaseInvoiceNfeReturnBalance`/`SalesReturnsReportService` filtram `Return` — inalterados. A conciliação (`SenseMatches`, `EnsurePair`, lista de linhas) é da Task 14.

- [ ] **Step 8: Migration**

```bash
ASPNETCORE_ENVIRONMENT=Ceagui-Development dotnet ef migrations add AddPurchaseInvoiceItemComplementAmount --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Conferir no `Up`: **só** `AddColumn ComplementAmount` em `PURCHASE_INVOICES_ITEMS`, `DECIMAL(18,2)`, não nulo, `defaultValueSql`/default 0. `git add` dos 3 arquivos.

- [ ] **Step 9: Rodar e gates**

```bash
dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PurchaseInvoiceValueComplementTests|FullyQualifiedName~PurchaseInvoices|FullyQualifiedName~InvoiceLineChargeRulesTests|FullyQualifiedName~Nfe|FullyQualifiedName~Reports"
dotnet build SiagroB1.sln
dotnet ef migrations has-pending-model-changes --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Expected: todos PASS; build OK; sem mudança pendente.

- [ ] **Step 10: Stage**

```bash
git add SiagroB1.Domain/Enums/PurchaseInvoiceType.cs SiagroB1.Domain/Entities/PurchaseInvoiceItem.cs SiagroB1.Application/Services/PurchaseInvoices SiagroB1.Application/Services/Nfe/PurchaseInvoicesNfeIssueService.cs SiagroB1.Reports/Services SiagroB1.Migrations SiagroB1.Application.Tests/PurchaseInvoices/PurchaseInvoiceValueComplementTests.cs
```
O controlador commita com:

```
feat(invoice): documento de entrada do tipo complemento de valor

Faturado abaixo do contrato se resolve com uma NF só de valor (spec 2026-10-13 D11): linhas sem
quantidade nem preço, com o valor do complemento, contrato e natureza obrigatórios — em
STANDALONE, natureza que gere financeiro. Só escrituração de terceiro: a NF-e própria de
complemento é o sub-projeto 2b. O valor entra no total da linha, então tributos, Funrural e
relatórios o enxergam.

DB: AddPurchaseInvoiceItemComplementAmount
Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 14: Complemento de valor (D11) — firme na confirmação, cancelamento/estorno e fora da conciliação

**Files:**
- Modify: `SiagroB1.Application/Services/Financials/FinancialDocumentsFirmSyncService.cs` (`EnqueueForValueComplementAsync`)
- Modify: `SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesConfirmService.cs`; `SiagroB1.Application.Tests/Support/TaxTestServices.cs` (`PurchaseConfirm`)
- Modify: `SiagroB1.Infra/Queries/PurchaseFirmPayableRules.cs` (métodos do complemento), `SiagroB1.Infra/Interceptors/PurchaseFiscalReconciliationCleanupInterceptor.cs` (`Apply` completo abaixo)
- Modify: `SiagroB1.Infra/Queries/PurchaseFiscalReconciliationRules.cs` (`SenseMatches`), `SiagroB1.Infra/Queries/PurchaseFiscalReconciliationQuery.cs` (`InvoiceLinesBase`), `SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationService.cs` (`EnsurePair`)
- Test: `SiagroB1.Application.Tests/Financials/FinancialDocumentsValueComplementTests.cs`

**Interfaces:**
- Consumes: Task 13 (`PurchaseInvoiceValueComplement.EnsureAsync`, `ComplementAmount`), Task 5 (`FirmPayableSeed`, `PurchaseFirmPayableRules`), Task 6 (compor).
- Produces:
  - `FinancialDocumentsFirmSyncService.EnqueueForValueComplementAsync(PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, string userName)` — enqueue-only, idempotente por linha.
  - `PurchaseInvoicesConfirmService(IUnitOfWork db, TaxCalculationGate gate, IBusinessPartnerService businessPartnerService, SupplierNfeAuthorizationService supplierNfe, FinancialDocumentsFirmSyncService firmSync)`.
  - `PurchaseFirmPayableRules`: `const string ValueComplementUndoneReason = "Documento de complemento de valor cancelado ou estornado"`, `List<FinancialDocument> ActiveValueComplementFirmsOf(DbContext, IReadOnlyCollection<Guid> lineKeys)`, `void CancelValueComplementFirms(DbContext, IReadOnlyCollection<Guid> lineKeys, string userName)` (mensagens P12).
  - `TaxTestServices.PurchaseConfirm(UnitOfWork db, string? erp = "STANDALONE", IBusinessPartnerService? partners = null, FakeNfeSefazClient? sefaz = null, FakeDocNumberSequenceService? firmNumbers = null)`.

- [ ] **Step 1: Testes que falham**

`SiagroB1.Application.Tests/Financials/FinancialDocumentsValueComplementTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.PurchaseFiscalReconciliations;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Interceptors;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-13 D11: o firme do complemento de valor nasce na confirmação, um por linha, e morre com o documento.</summary>
public class FinancialDocumentsValueComplementTests
{
    private static async Task<PurchaseInvoice> ConfirmedComplementAsync(FirmPayableSeed seed, params decimal[] amounts)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = FiscalReconciliationSeed.Supplier,
            InvoiceType = PurchaseInvoiceType.ValueComplement, IssuerType = DocumentIssuerType.ThirdParty,
            TaxDocumentNumber = "5001", TaxDocumentSeries = "1", IssueDate = new DateTime(2026, 10, 12),
        };
        foreach (var amount in amounts)
            invoice.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = FiscalReconciliationSeed.Product, ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
                ComplementAmount = amount, UsageCode = seed.FinancialUsageCode, PurchaseContractKey = seed.Contract.Key,
            });

        await new PurchaseInvoicesCreateService(seed.Db, new FakeBusinessPartnerService(), new FakeItemService(),
                TaxTestServices.PurchaseApply(seed.Db, new FakeBusinessPartnerService(), seed.Erp), new FakeDocNumberSequenceService())
            .ExecuteAsync(invoice, "ana");
        await TaxTestServices.PurchaseConfirm(seed.Db, seed.Erp, firmNumbers: seed.Numbers).ExecuteAsync(invoice.Key, "ana");
        return invoice;
    }

    private static IQueryable<FinancialDocument> ComplementFirms(FirmPayableSeed seed) =>
        seed.Context.FinancialDocuments.AsNoTracking().Where(d => d.OriginType == FinancialDocumentOrigin.PurchaseInvoice);

    [Fact]
    public async Task Confirming_creates_one_firm_per_line()
    {
        var seed = await FirmPayableSeed.CreateAsync();

        var invoice = await ConfirmedComplementAsync(seed, 1_000m, 500m);

        var firms = await ComplementFirms(seed).OrderByDescending(d => d.NetAmount).ToListAsync();
        Assert.Equal(new[] { 1_000m, 500m }, firms.Select(f => f.NetAmount));
        var lineKeys = invoice.Items.Select(i => i.Key!.Value).ToHashSet();
        Assert.All(firms, f =>
        {
            Assert.Equal(FinancialDocumentNature.Firm, f.Nature);
            Assert.Contains(f.OriginKey!.Value, lineKeys);
            Assert.Equal(invoice.Key, f.PurchaseInvoiceKey);
            Assert.Null(f.StorageTransactionKey);
            Assert.Equal("5001-1", f.TaxDocumentNumber);
            Assert.Equal(seed.Contract.Key, f.PurchaseContractKey);
            Assert.Equal("PC-1", f.OriginDocNumber);
            Assert.Equal(new DateTime(2026, 10, 12), f.DueDate);
            Assert.True(f.IsPurchaseFirmPayable);
        });
    }

    [Fact]
    public async Task Underbilling_is_closed_by_composing_the_complement()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 20_000m);
        var c = await seed.ConciliateAsync();                         // 19.728,33
        await ConfirmedComplementAsync(seed, 271.67m);
        var complement = await ComplementFirms(seed).SingleAsync();

        await new FinancialDocumentsComposeService(seed.Db, new FinancialDocumentChangeLogService(seed.Context))
            .ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key, complement.Key], "ana");
        await new FinancialDocumentsSettleService(seed.Db)
            .ExecuteAsync(seed.Provisional.Key, "CX01", 20_000m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(seed.Provisional.Key)).Status);
    }

    [Fact]
    public async Task Cancelling_the_document_cancels_the_loose_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync(interceptor: new PurchaseFiscalReconciliationCleanupInterceptor());
        var invoice = await ConfirmedComplementAsync(seed, 1_000m);

        await new PurchaseInvoicesCancelService(seed.Db).ExecuteAsync(invoice.Key, "bia");

        var firm = await ComplementFirms(seed).SingleAsync();
        Assert.Equal(FinancialDocumentStatus.Canceled, firm.Status);
        Assert.Equal("Documento de complemento de valor cancelado ou estornado", firm.CancellationReason);
        Assert.Equal("bia", firm.CanceledBy);
    }

    [Fact]
    public async Task Cancelling_the_document_with_a_composed_firm_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync(interceptor: new PurchaseFiscalReconciliationCleanupInterceptor());
        var invoice = await ConfirmedComplementAsync(seed, 1_000m);
        var firm = await ComplementFirms(seed).SingleAsync();
        await new FinancialDocumentsComposeService(seed.Db, new FinancialDocumentChangeLogService(seed.Context))
            .ExecuteAsync(seed.Provisional.Key, [firm.Key], "ana");

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesCancelService(seed.Db).ExecuteAsync(invoice.Key, "bia"));

        Assert.Equal(
            $"O título {firm.Code} do complemento de valor já está composto no título FN-PROV-1: desfaça a composição antes de cancelar ou estornar o documento.",
            error.Message);
    }

    [Fact]
    public async Task Reversing_the_confirmation_cancels_the_loose_firm_and_reconfirming_creates_a_new_one()
    {
        var seed = await FirmPayableSeed.CreateAsync(interceptor: new PurchaseFiscalReconciliationCleanupInterceptor());
        var invoice = await ConfirmedComplementAsync(seed, 1_000m);
        var first = await ComplementFirms(seed).SingleAsync();

        await new PurchaseInvoicesReverseConfirmService(seed.Db).ExecuteAsync(invoice.Key, "bia");
        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(first.Key)).Status);

        await TaxTestServices.PurchaseConfirm(seed.Db, seed.Erp, firmNumbers: seed.Numbers).ExecuteAsync(invoice.Key, "ana");

        var active = await ComplementFirms(seed).Where(d => d.Status != FinancialDocumentStatus.Canceled).SingleAsync();
        Assert.NotEqual(first.Key, active.Key);
    }

    [Fact]
    public async Task Value_complement_is_not_conciliated()
    {
        var fiscal = await FiscalReconciliationSeed.CreateAsync();
        var tx = await fiscal.AddTransactionAsync(10_000m);
        var (_, line) = await fiscal.AddInvoiceAsync(0m, type: PurchaseInvoiceType.ValueComplement, taxNumber: "5001");

        var error = await Assert.ThrowsAsync<DefaultException>(() => FiscalReconciliationSeed.NewService(fiscal.Db).SaveAsync(
            [new FiscalReconciliationPairDto { PurchaseInvoiceItemKey = line.Key!.Value, StorageTransactionKey = tx.Key, Quantity = 10m }], "ana"));

        Assert.Equal("O documento 5001-1 é complemento de valor: não se concilia com romaneio; o título nasce na confirmação.", error.Message);
        Assert.Empty(await FiscalReconciliationSeed.NewService(fiscal.Db).ListInvoiceLinesAsync(
            new FiscalReconciliationFilter { CardCode = FiscalReconciliationSeed.Supplier, ItemCode = FiscalReconciliationSeed.Product }));
    }
}
```

`git add` o arquivo.

- [ ] **Step 2: Rodar e ver falhar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsValueComplementTests"`
Expected: erro de compilação (`PurchaseConfirm(..., firmNumbers:)` não existe). Depois do Step 3, falham por falta de firme / recusa.

- [ ] **Step 3: Firme na confirmação**

1. `FinancialDocumentsFirmSyncService.cs` — using `SiagroB1.Application.Services.PurchaseInvoices`; método novo depois de `EnqueueRemoval`:

```csharp
    /// <summary>
    /// Spec 2026-10-13 D11: confirmar o documento "Complemento de valor" cria um firme por linha (origem PurchaseInvoice,
    /// OriginKey = linha), pelo valor da linha menos o Funrural, sem romaneio. Revalida as regras do documento (a
    /// natureza ou o contrato podem ter mudado desde a gravação). ENQUEUE-ONLY e fora de transação (numeração por Dapper).
    /// Idempotente por linha: o índice IX_FINANCIAL_DOCUMENTS_FirmOrigin (OriginType + OriginKey) é a segunda camada.
    /// </summary>
    public async Task EnqueueForValueComplementAsync(
        PurchaseInvoice invoice, IReadOnlyList<PurchaseInvoiceItem> lines, string userName)
    {
        if (invoice.InvoiceType != PurchaseInvoiceType.ValueComplement)
            return;

        await PurchaseInvoiceValueComplement.EnsureAsync(invoice, lines, usageService, ErpMode.IsStandalone(configuration));

        var context = db.Context;
        var taxDocument = R.TaxDocument(invoice.TaxDocumentNumber, invoice.TaxDocumentSeries, invoice.InvoiceNumber);
        var issueDate = (invoice.IssueDate ?? DateTime.Today).Date;
        var contractKeys = lines.Select(l => l.PurchaseContractKey!.Value).Distinct().ToList();
        var contracts = await context.PurchaseContracts.AsNoTracking()
            .Where(c => contractKeys.Contains(c.Key))
            .Select(c => new { c.Key, c.Code, c.StandardCurrency })
            .ToDictionaryAsync(c => c.Key);

        foreach (var line in lines)
        {
            var lineKey = line.Key!.Value;
            if (PurchaseFirmPayableRules.ActiveValueComplementFirmsOf(context, [lineKey]).Count > 0)
                continue;

            var contract = contracts[line.PurchaseContractKey!.Value];
            var docNumberKey = await docNumberSequence.GetKeyByTransactionCode(TransactionCode.FinancialDocument);
            var code = await docNumberSequence.GetDocNumber(docNumberKey);
            var now = DateTime.Now;

            var document = new FinancialDocument
            {
                Key = Guid.NewGuid(),
                Code = code,
                DocNumberKey = docNumberKey,
                BranchCode = invoice.BranchCode,
                Direction = FinancialDirection.Payable,
                Nature = FinancialDocumentNature.Firm,
                Status = FinancialDocumentStatus.Open,
                CardCode = invoice.CardCode,
                CardName = invoice.CardName,
                DocumentDate = issueDate,
                DueDate = issueDate,
                Currency = contract.StandardCurrency ?? CurrencyType.Brl,
                NetAmount = decimal.Round(line.GrandTotal - line.FunruralValue, 2, MidpointRounding.ToEven),
                OriginType = FinancialDocumentOrigin.PurchaseInvoice,
                OriginKey = lineKey,
                OriginDocNumber = contract.Code,
                PurchaseContractKey = line.PurchaseContractKey,
                PurchaseInvoiceKey = invoice.Key,
                TaxDocumentNumber = taxDocument,
                CreatedAt = now,
                CreatedBy = userName,
                UpdatedAt = now,
                UpdatedBy = userName,
            };

            context.FinancialDocuments.Add(document);
            await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, document.Key);
        }
    }
```

2. `PurchaseInvoicesConfirmService.cs` — using `SiagroB1.Application.Services.Financials`; construtor ganha o 5º parâmetro `FinancialDocumentsFirmSyncService firmSync`; logo depois de `await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, invoice, lines);`:

```csharp

        // Spec 2026-10-13 D11: o complemento de valor cria os títulos firmes aqui, no MESMO SaveChanges da confirmação.
        await firmSync.EnqueueForValueComplementAsync(invoice, lines, userName);
```

3. `Support/TaxTestServices.cs` — `PurchaseConfirm` passa a ser:

```csharp
    public static PurchaseInvoicesConfirmService PurchaseConfirm(
        UnitOfWork db, string? erp = "STANDALONE", IBusinessPartnerService? partners = null, FakeNfeSefazClient? sefaz = null,
        FakeDocNumberSequenceService? firmNumbers = null)
    {
        sefaz ??= new FakeNfeSefazClient();
        var config = NfeTestSeed.Config(erp);

        return new PurchaseInvoicesConfirmService(db, new TaxCalculationGate(db, config), partners ?? new FakeBusinessPartnerService(),
            new SupplierNfeAuthorizationService(db, new BranchNfeSettingsService(db, new NfeOptions(config), sefaz), sefaz),
            FinancialDocumentTestServices.FirmSync(db, firmNumbers, erp ?? "STANDALONE"));
    }
```

- [ ] **Step 4: Regras de cancelamento no Infra**

`PurchaseFirmPayableRules.cs` — acrescentar ao fim da classe:

```csharp

    // --- Complemento de valor (spec 2026-10-13 D11): o firme nasce da LINHA do documento (OriginType PurchaseInvoice). ---

    public const string ValueComplementUndoneReason = "Documento de complemento de valor cancelado ou estornado";

    /// <summary>Firmes ATIVOS das linhas do complemento de valor, rastreados (status reconferido em memória).</summary>
    public static List<FinancialDocument> ActiveValueComplementFirmsOf(DbContext context, IReadOnlyCollection<Guid> lineKeys)
    {
        if (lineKeys.Count == 0) return [];

        return context.Set<FinancialDocument>()
            .Where(d => d.OriginType == FinancialDocumentOrigin.PurchaseInvoice &&
                        d.Nature == FinancialDocumentNature.Firm &&
                        d.OriginKey != null && lineKeys.Contains(d.OriginKey.Value) &&
                        d.Status != FinancialDocumentStatus.Canceled)
            .ToList()
            .Where(d => d.Status != FinancialDocumentStatus.Canceled)
            .ToList();
    }

    /// <summary>
    /// Documento cancelado ou confirmação estornada: o firme solto é cancelado; composto, ou com dação ou encontro de
    /// contas, recusa a operação inteira (mesma regra 7 do vínculo, com o texto do documento).
    /// </summary>
    public static void CancelValueComplementFirms(DbContext context, IReadOnlyCollection<Guid> lineKeys, string userName)
    {
        var firms = ActiveValueComplementFirmsOf(context, lineKeys);
        foreach (var firm in firms)
        {
            if (ProvisionalOf(context, firm.Key) is { } provisionalKey)
            {
                var provisionalCode = context.Set<FinancialDocument>().AsNoTracking()
                    .Where(d => d.Key == provisionalKey).Select(d => d.Code).FirstOrDefault();
                throw new DefaultException(
                    $"O título {firm.Code} do complemento de valor já está composto no título {provisionalCode}: desfaça a composição antes de cancelar ou estornar o documento.");
            }

            if (firm.SettledAmount != 0m)
                throw new DefaultException(
                    $"O título {firm.Code} do complemento de valor já tem baixa por dação ou encontro de contas: estorne-a antes de cancelar ou estornar o documento.");
        }

        var now = DateTime.Now;
        foreach (var firm in firms)
        {
            firm.Status = FinancialDocumentStatus.Canceled;
            firm.CancellationReason = ValueComplementUndoneReason;
            firm.CanceledAt = now;
            firm.CanceledBy = userName;
            firm.UpdatedAt = now;
            firm.UpdatedBy = userName;
        }
    }
```

- [ ] **Step 5: Interceptor**

`PurchaseFiscalReconciliationCleanupInterceptor.cs` — trocar o método `Apply` inteiro por (o comentário de classe ganha o item "documento de complemento de valor cancelado, excluído ou com a confirmação estornada → firmes das linhas cancelados ou recusa"):

```csharp
    private static void Apply(DbContext context)
    {
        var cancelledTransactions = new HashSet<Guid>();
        var cancelledInvoices = new HashSet<Guid>();
        var deletedLines = new HashSet<Guid>();
        var changedLines = new List<EntityEntry<PurchaseInvoiceItem>>();
        // Spec 2026-10-13 D11: documento cujo firme de complemento de valor tem de morrer, com quem fez a operação.
        var undoneInvoices = new Dictionary<Guid, string>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case StorageTransaction t when entry.State == EntityState.Deleted ||
                                               BecameCancelled(entry, nameof(StorageTransaction.TransactionStatus), StorageTransactionsStatus.Cancelled):
                    cancelledTransactions.Add(t.Key);
                    break;
                case PurchaseInvoice i when entry.State == EntityState.Deleted ||
                                            BecameCancelled(entry, nameof(PurchaseInvoice.InvoiceStatus), InvoiceStatus.Cancelled):
                    cancelledInvoices.Add(i.Key);
                    undoneInvoices[i.Key] = i.CanceledBy ?? PurchaseFirmPayableRules.SystemUserName;
                    break;
                case PurchaseInvoice i when ConfirmationReversed(entry):
                    undoneInvoices[i.Key] = i.UpdatedBy ?? PurchaseFirmPayableRules.SystemUserName;
                    break;
                case PurchaseInvoiceItem { Key: { } key } when entry.State == EntityState.Deleted:
                    deletedLines.Add(key);
                    break;
                case PurchaseInvoiceItem { Key: not null } item when entry.State == EntityState.Modified:
                    changedLines.Add(context.Entry(item));
                    break;
            }
        }

        if (cancelledTransactions.Count + cancelledInvoices.Count + deletedLines.Count + changedLines.Count + undoneInvoices.Count == 0)
            return;

        var links = context.Set<PurchaseFiscalReconciliation>();

        foreach (var line in changedLines)
            EnsureLineEditKeepsLinks(context, links, line);

        // D11: o firme do complemento de valor segue o documento (cancelado, excluído ou com a confirmação estornada).
        if (undoneInvoices.Count > 0)
        {
            var invoiceKeys = undoneInvoices.Keys.ToList();
            var linesByInvoice = context.Set<PurchaseInvoiceItem>().AsNoTracking()
                .Where(l => l.PurchaseInvoiceKey != null && invoiceKeys.Contains(l.PurchaseInvoiceKey.Value))
                .Select(l => new { InvoiceKey = l.PurchaseInvoiceKey!.Value, LineKey = l.Key!.Value })
                .ToList()
                .GroupBy(l => l.InvoiceKey);

            foreach (var group in linesByInvoice)
                PurchaseFirmPayableRules.CancelValueComplementFirms(
                    context, group.Select(l => l.LineKey).ToList(), undoneInvoices[group.Key]);
        }

        if (cancelledTransactions.Count + cancelledInvoices.Count + deletedLines.Count == 0)
            return;

        if (cancelledInvoices.Count > 0)
            deletedLines.UnionWith(context.Set<PurchaseInvoiceItem>().AsNoTracking()
                .Where(l => l.PurchaseInvoiceKey != null && cancelledInvoices.Contains(l.PurchaseInvoiceKey.Value))
                .Select(l => l.Key!.Value)
                .ToList());

        var doomed = links
            .Where(l => cancelledTransactions.Contains(l.StorageTransactionKey) || deletedLines.Contains(l.PurchaseInvoiceItemKey))
            .ToList();
        if (doomed.Count == 0)
            return;

        // Spec 2026-10-13 (títulos firmes, regra 7): o firme solto do vínculo é cancelado; o composto (ou com dação ou
        // encontro de contas) recusa a operação inteira — antes de remover qualquer vínculo.
        PurchaseFirmPayableRules.CancelLooseFirms(
            context, doomed.Select(l => l.Key).ToList(), PurchaseFirmPayableRules.SystemUserName);

        links.RemoveRange(doomed);

        var touched = doomed.Select(l => l.StorageTransactionKey).Where(k => !cancelledTransactions.Contains(k)).Distinct().ToList();
        foreach (var tx in context.Set<StorageTransaction>().Where(t => touched.Contains(t.Key) && t.FiscalReconciledAt != null).ToList())
        {
            tx.FiscalReconciledAt = null;
            tx.FiscalReconciledBy = null;
        }
    }

    /// <summary>Estorno da confirmação: Confirmed → Pending (PurchaseInvoicesReverseConfirmService).</summary>
    private static bool ConfirmationReversed(EntityEntry entry)
    {
        if (entry.State != EntityState.Modified) return false;
        var p = entry.Property(nameof(PurchaseInvoice.InvoiceStatus));
        return p.IsModified && Equals(p.OriginalValue, InvoiceStatus.Confirmed) && Equals(p.CurrentValue, InvoiceStatus.Pending);
    }
```

- [ ] **Step 6: Fora da conciliação**

1. `PurchaseFiscalReconciliationRules.SenseMatches` completo:

```csharp
    /// <summary>
    /// NF de devolução cobre o que SAI do contrato (9, 16); a normal, o que ENTRA (8, 10, 17). O complemento de valor
    /// (D11) não tem quantidade: não casa com romaneio nenhum.
    /// </summary>
    public static bool SenseMatches(PurchaseInvoiceType invoiceType, StorageTransactionType type) => invoiceType switch
    {
        PurchaseInvoiceType.Return => type is StorageTransactionType.PurchaseReturn or StorageTransactionType.PurchaseDischargeLoss,
        PurchaseInvoiceType.Normal => type is StorageTransactionType.Purchase or StorageTransactionType.PurchaseQtyComplement
            or StorageTransactionType.PurchaseDischargeGain,
        _ => false,
    };
```

2. `PurchaseFiscalReconciliationQuery.InvoiceLinesBase`, no `Where` inicial, depois de `l.PurchaseInvoice.CustodyStorageTransactionKey == null && // nota de custódia não é NF de venda`:

```csharp
                        l.PurchaseInvoice.InvoiceType != PurchaseInvoiceType.ValueComplement && // D11: título nasce na confirmação
```

3. `PurchaseFiscalReconciliationService.EnsurePair`, logo depois da recusa de documento cancelado:

```csharp

        if (line.InvoiceType == PurchaseInvoiceType.ValueComplement)
            throw new DefaultException(
                $"O documento {line.TaxDocument} é complemento de valor: não se concilia com romaneio; o título nasce na confirmação.");
```

- [ ] **Step 7: Rodar**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~FinancialDocumentsValueComplementTests|FullyQualifiedName~Financials|FullyQualifiedName~PurchaseFiscalReconciliations|FullyQualifiedName~PurchaseInvoices|FullyQualifiedName~Nfe"`
Expected: todos PASS.

- [ ] **Step 8: Stage**

```bash
dotnet build SiagroB1.sln
git add SiagroB1.Application/Services/Financials/FinancialDocumentsFirmSyncService.cs SiagroB1.Application/Services/PurchaseInvoices/PurchaseInvoicesConfirmService.cs SiagroB1.Application/Services/PurchaseFiscalReconciliations/PurchaseFiscalReconciliationService.cs SiagroB1.Infra SiagroB1.Application.Tests/Support/TaxTestServices.cs SiagroB1.Application.Tests/Financials/FinancialDocumentsValueComplementTests.cs
```
O controlador commita com:

```
feat(financial): título firme do complemento de valor nasce na confirmação

Confirmar o documento de complemento de valor cria um título firme por linha, pelo valor menos
o Funrural, que se compõe no provisório como qualquer firme — é o que cobre o faturado abaixo do
contrato e deixa o provisório quitar. Cancelar o documento ou estornar a confirmação cancela o
firme solto e é recusado com o firme composto ou com dação/encontro de contas. O complemento
não entra na Conciliação Fiscal de Compras.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 15: Frontend — rótulos, rotas, origem da baixa e dados fiscais no Detail do título

Repositório: `C:\Projetos\SiagroB1\siagro-b1-frontend`, branch `feature/purchase-firm-payables` (criado na Task 2). A partir daqui o backend das Tasks 1–14 precisa estar no branch do backend para o roteiro de navegador; os gates de frontend (`ts-typecheck`, `lint`, QUnit) não dependem dele.

**Files:**
- Modify: `webapp/model/formatter.ts`, `webapp/test/unit/model/formatter.qunit.ts`
- Modify: `webapp/model/ServerRoutes.ts`
- Create: `webapp/types/FinancialComposition.ts`
- Modify: `webapp/view/financialDocuments/Detail.view.xml` (botão "Cancelar título"), `webapp/view/financialDocuments/fragments/Form.fragment.xml`, `webapp/view/financialDocuments/fragments/Settlements.fragment.xml`
- Modify: `webapp/controller/financialDocuments/Detail.controller.ts` (`onSettle`, `onConfirmSettle`, `onOpenPurchaseInvoice`)

**Interfaces:**
- Consumes: EDM da Task 3 (`ComposedAmount`, `AvailableToSettleAmount`, `PurchaseInvoiceKey`, `StorageTransactionCode`, `TaxDocumentNumber`, `Origin` da baixa) e rotas da Task 12.
- Produces:
  - `formatter.formatFinancialSettlementOrigin(value: string): string`, `formatter.isPurchaseFirmPayable(nature?: string, originType?: string): boolean`, `formatter.formatDecimal2(value: number | string): string`; `formatFinancialDocumentCancelable(status, settledAmount, nature?, originType?, composedAmount?)`.
  - `ServerRoutes`: `financialDocumentsCompose`, `financialDocumentsUncompose`, `financialDocumentsDation`, `financialDocumentsNetting`, `financialDocumentsGetComposition`, `financialDocumentsGetByContract`.
  - Tipos `FinancialCompositionLine`, `FinancialCompositionCandidate`, `FinancialComposition` em `types/FinancialComposition.ts`.

- [ ] **Step 1: Testes QUnit que falham**

Acrescentar ao fim de `webapp/test/unit/model/formatter.qunit.ts`:

```ts
QUnit.module("formatter - títulos firmes de compra (spec 2026-10-13)");

QUnit.test("natureza Devolução, origem da conciliação e campo Composição do log", function (assert) {
	assert.strictEqual(formatter.formatFinancialNature("PurchaseReturn"), "Devolução");
	assert.strictEqual(formatter.formatFinancialOrigin("PurchaseFiscalReconciliation"), "Conciliação fiscal de compras");
	assert.strictEqual(formatter.formatFinancialChangeLogField("Composition"), "Composição");
});

QUnit.test("origem da baixa traduzida", function (assert) {
	assert.strictEqual(formatter.formatFinancialSettlementOrigin("Manual"), "Manual");
	assert.strictEqual(formatter.formatFinancialSettlementOrigin("Reversal"), "Estorno");
	assert.strictEqual(formatter.formatFinancialSettlementOrigin("InvoiceOffset"), "Abatimento");
	assert.strictEqual(formatter.formatFinancialSettlementOrigin("Netting"), "Encontro de contas");
	assert.strictEqual(formatter.formatFinancialSettlementOrigin("Dation"), "Dação");
	assert.strictEqual(formatter.formatFinancialSettlementOrigin("AdvanceRefund"), "Devolução de adiantamento");
	assert.strictEqual(formatter.formatFinancialSettlementOrigin("AdvanceApplication"), "Amortização de adiantamento");
});

QUnit.test("Cancelar título some no firme da conciliação e no provisório composto", function (assert) {
	assert.strictEqual(formatter.formatFinancialDocumentCancelable("Open", "0", "Firm", "PurchaseFiscalReconciliation", "0"), false);
	assert.strictEqual(formatter.formatFinancialDocumentCancelable("Open", "0", "PurchaseReturn", "PurchaseFiscalReconciliation", "0"), false);
	assert.strictEqual(formatter.formatFinancialDocumentCancelable("Open", "0", "Provisional", "PurchaseContractPriceFixation", "500.00"), false);
	assert.strictEqual(formatter.formatFinancialDocumentCancelable("Open", "0", "Provisional", "PurchaseContractPriceFixation", "0"), true);
	assert.strictEqual(formatter.formatFinancialDocumentCancelable("Open", "0", "Firm", "PurchaseInvoice", "0"), false);
	assert.strictEqual(formatter.formatFinancialDocumentCancelable("Open", "0", "Firm", "PurchaseContractWashout", "0"), true);
	assert.strictEqual(formatter.formatFinancialDocumentCancelable("Open", "0"), true);
});

QUnit.test("formatDecimal2 aceita string do Edm.Decimal e não lança em vazio", function (assert) {
	assert.strictEqual(formatter.formatDecimal2("19728.33"), "19.728,33");
	assert.strictEqual(formatter.formatDecimal2(-2959.25), "-2.959,25");
	assert.strictEqual(formatter.formatDecimal2("abc"), "");
});
```

- [ ] **Step 2: Rodar o QUnit e ver falhar**

`yarn start`, abrir `http://localhost:8080/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests` e filtrar o módulo "títulos firmes". Expected: FAIL (`formatFinancialSettlementOrigin`/`formatDecimal2` não existem; `PurchaseReturn` vazio). Parar o dev server **pelo PID** da porta 8080.

- [ ] **Step 3: Formatter**

`webapp/model/formatter.ts`:

1. Em `formatFinancialNature`, depois de `m.set("TaxWithholding", "Retenção");`: `m.set("PurchaseReturn", "Devolução");`
2. Em `formatFinancialOrigin`, depois de `m.set("PurchaseContractWashout", "Washout de contrato de compra");`: `m.set("PurchaseFiscalReconciliation", "Conciliação fiscal de compras");`
3. Em `formatFinancialChangeLogField`, depois de `m.set("NetAmount", "Valor");`: `m.set("Composition", "Composição");`
4. Trocar `formatFinancialDocumentCancelable` (a função e o comentário acima dela) por:

```ts
  /**
   * O botão "Cancelar título" só deve aparecer enquanto o cancelamento é possível.
   * FinancialDocumentsCancelService recusa: já cancelado; SettledAmount != 0 ("possui baixas"); firme ou devolução da
   * Conciliação Fiscal (nasce e morre com o vínculo); provisório com composição (spec 2026-10-13, títulos firmes).
   * `!!status` segura o piscar antes dos dados: binding indefinido avalia como verdadeiro.
   * OData v4 serializa Edm.Decimal como string — coagir antes de comparar.
   */
  formatFinancialDocumentCancelable: (
    status: string, settledAmount: number | string, nature?: string, originType?: string, composedAmount?: number | string,
  ) =>
    !!status && status !== "Canceled" && !Number(settledAmount)
    && !formatter.isPurchaseFirmPayable(nature, originType) && !Number(composedAmount ?? 0),

  /**
   * Firme ou devolução da Conciliação Fiscal de Compras, ou firme do documento "Complemento de valor" (origem
   * PurchaseInvoice, spec D11): sai só por composição, dação ou encontro de contas.
   */
  isPurchaseFirmPayable: (nature?: string, originType?: string) =>
    ((nature === "Firm" || nature === "PurchaseReturn") && originType === "PurchaseFiscalReconciliation")
    || (nature === "Firm" && originType === "PurchaseInvoice"),
```

5. Logo depois de `formatFinancialOrigin`:

```ts
  /** Origem da linha do ledger de baixas (enum FinancialSettlementOrigin). Código novo cai para ele mesmo. */
  formatFinancialSettlementOrigin: (value: string) => {
    const m = new Map<string, string>();
    m.set("Manual", "Manual");
    m.set("Reversal", "Estorno");
    m.set("InvoiceOffset", "Abatimento");
    m.set("AdvanceApplication", "Amortização de adiantamento");
    m.set("Netting", "Encontro de contas");
    m.set("AdvanceRefund", "Devolução de adiantamento");
    m.set("Dation", "Dação");

    return m.get(value) ?? value ?? "";
  },
```

6. Logo depois de `formatDecimal3`:

```ts
  /** Valor monetário pt-BR com 2 casas para JSONModel (Edm.Decimal chega string). Como formatDecimal3, não lança. */
  formatDecimal2: (value: number | string): string => {
    const num = Number(value ?? 0);
    return Number.isFinite(num) ? formatter.formatDecimal(num, 2) : "";
  },
```

- [ ] **Step 4: Rotas e tipos**

`webapp/model/ServerRoutes.ts`, depois de `financialAdvancesRelinkContract: '/FinancialAdvancesRelinkContract(...)',`:

```ts
  // Títulos firmes de compra (spec 2026-10-13): chamados por sendJson (erro de negócio nas três formas).
  financialDocumentsCompose: '/odata/FinancialDocumentsCompose',
  financialDocumentsUncompose: '/odata/FinancialDocumentsUncompose',
  financialDocumentsDation: '/odata/FinancialDocumentsDation',
  financialDocumentsNetting: '/odata/FinancialDocumentsNetting',
  financialDocumentsGetComposition: '/odata/FinancialDocumentsGetComposition',
  financialDocumentsGetByContract: '/odata/FinancialDocumentsGetByContract',
```

`webapp/types/FinancialComposition.ts`:

```ts
/**
 * Function FinancialDocumentsGetComposition (spec 2026-10-13, títulos firmes). PascalCase; valores Edm.Decimal podem
 * chegar como string — sempre Number() antes de comparar.
 */
export type FinancialCompositionLine = {
  CompositionKey: string;
  ProvisionalDocumentKey: string;
  ProvisionalCode: string;
  DocumentKey: string;
  DocumentCode: string;
  Nature: string;
  StorageTransactionCode: string;
  TaxDocumentNumber: string;
  CardCode: string;
  CardName: string;
  Amount: number | string;
  CreatedAt: string;
  CreatedBy: string;
};

export type FinancialCompositionCandidate = {
  DocumentKey: string;
  Code: string;
  Nature: string;
  StorageTransactionCode: string;
  TaxDocumentNumber: string;
  CardCode: string;
  CardName: string;
  DocumentDate: string;
  OpenAmount: number | string;
  SuggestedAmount: number | string;
  /** Só na tela: marcado no diálogo Compor. */
  selected?: boolean;
};

export type FinancialComposition = {
  Lines: FinancialCompositionLine[];
  Candidates: FinancialCompositionCandidate[];
};
```

`git add webapp/types/FinancialComposition.ts`

- [ ] **Step 5: Form do título**

`webapp/view/financialDocuments/fragments/Form.fragment.xml`, no container "Valores":

1. O `FormElement label="Valor"` vira `label="{= ${path: 'Nature', targetType: 'any'} === 'Provisional' ? 'Valor contratado' : 'Valor' }"`.
2. Logo depois do `FormElement label="Baixado"`:

```xml
          <f:FormElement label="Composto" visible="{= ${path: 'Nature', targetType: 'any'} === 'Provisional' }">
            <f:fields>
              <Text text="{
                path: 'ComposedAmount',
                type: 'sap.ui.model.odata.type.Decimal',
                constraints: { precision: 18, scale: 2 },
                formatOptions: { decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
              }"/>
            </f:fields>
          </f:FormElement>
          <f:FormElement label="Disponível para baixa" visible="{= ${path: 'Nature', targetType: 'any'} === 'Provisional' }">
            <f:fields>
              <ObjectNumber
                number="{
                  path: 'AvailableToSettleAmount',
                  type: 'sap.ui.model.odata.type.Decimal',
                  constraints: { precision: 18, scale: 2 },
                  formatOptions: { decimalSeparator: ',', groupingEnabled: true, groupingSeparator: '.' }
                }"
                state="Success"/>
            </f:fields>
          </f:FormElement>
```

3. Novo container, entre "Valores" e "Observações":

```xml
      <f:FormContainer title="Documento fiscal"
        visible="{= ${path: 'OriginType', targetType: 'any'} === 'PurchaseFiscalReconciliation' || ${path: 'OriginType', targetType: 'any'} === 'PurchaseInvoice' }">
        <f:formElements>
          <f:FormElement label="Romaneio">
            <f:fields><Text text="{StorageTransactionCode}"/></f:fields>
          </f:FormElement>
          <f:FormElement label="NF">
            <f:fields><Text text="{TaxDocumentNumber}"/></f:fields>
          </f:FormElement>
          <f:FormElement label="Documento de entrada">
            <f:fields>
              <Link text="Abrir documento de entrada" enabled="{= !!${PurchaseInvoiceKey} }" press=".onOpenPurchaseInvoice"/>
            </f:fields>
          </f:FormElement>
        </f:formElements>
      </f:FormContainer>
```

(Os campos ficam no `$select` automático mesmo com o container oculto: o binding do controle existe. Não trocar por `requestProperty`, que pediria `FinancialDocuments(key)/Campo` e tomaria 404.)

- [ ] **Step 6: Origem na tabela de baixas**

`webapp/view/financialDocuments/fragments/Settlements.fragment.xml`:

1. A `core:FragmentDefinition` ganha o `core:require` (fragmento não herda o da view):

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:core="sap.ui.core"
    xmlns:t="sap.ui.table"
    core:require="{
      formatter: 'siagrob1/model/formatter'
    }"
>
```

2. Logo depois da coluna "Data":

```xml
      <t:Column label="Origem" width="12rem">
        <t:template>
          <Text text="{path: 'Origin', targetType: 'any', formatter: 'formatter.formatFinancialSettlementOrigin'}" wrapping="false"/>
        </t:template>
      </t:Column>
```

- [ ] **Step 7: Botão "Cancelar título"**

`webapp/view/financialDocuments/Detail.view.xml`, no `visible` do botão "Cancelar título", trocar as `parts` por:

```xml
            parts: [
              { path: 'Status', targetType: 'any' },
              { path: 'SettledAmount', targetType: 'any' },
              { path: 'Nature', targetType: 'any' },
              { path: 'OriginType', targetType: 'any' },
              { path: 'ComposedAmount', targetType: 'any' }
            ],
```

- [ ] **Step 8: Baixa pelo disponível e link do documento de entrada**

`webapp/controller/financialDocuments/Detail.controller.ts`:

1. Em `onSettle`, trocar o bloco desde `if (ctx.getProperty("IsBlockedForSettlement")) {` até o `if (openAmount <= 0) { … }` (inclusive) por:

```ts
    if (ctx.getProperty("IsBlockedForSettlement")) {
      MessageBox.warning(ctx.getProperty("Nature") === "Provisional"
        ? "Nada disponível para baixa: componha títulos firmes no provisório antes."
        : "Título firme não recebe baixa direta: componha no provisório ou baixe por dação.");
      return;
    }

    // Provisório baixa só o composto (spec 2026-10-13); nas demais naturezas o disponível é o saldo em aberto.
    const openAmount = Number(ctx.getProperty("AvailableToSettleAmount") ?? ctx.getProperty("OpenAmount") ?? 0);

    if (openAmount <= 0) {
      MessageBox.warning("O título não tem saldo disponível para baixa.");
      return;
    }
```

2. Em `onConfirmSettle`, trocar
```ts
    const openAmount = Number(ctx.getProperty("OpenAmount") ?? 0);

    if (amount > openAmount) {
      MessageBox.warning(
        `O valor da baixa (${formatter.formatDecimal(amount, 2)}) excede o saldo em aberto ` +
        `(${formatter.formatDecimal(openAmount, 2)}).`);
      return;
    }
```
por
```ts
    const openAmount = Number(ctx.getProperty("AvailableToSettleAmount") ?? ctx.getProperty("OpenAmount") ?? 0);

    if (amount > openAmount) {
      MessageBox.warning(
        `O valor da baixa (${formatter.formatDecimal(amount, 2)}) excede o disponível para baixa ` +
        `(${formatter.formatDecimal(openAmount, 2)}).`);
      return;
    }
```

3. Método novo, depois de `onBack`:

```ts
  /** Documento de entrada do firme (spec 2026-10-13): a NF que deu o preço. */
  onOpenPurchaseInvoice(): void {
    const key = this.documentContext()?.getProperty("PurchaseInvoiceKey") as string;
    if (key) {
      this.navTo("purchaseInvoicesDetail", { id: key });
    }
  }
```

- [ ] **Step 9: Gates**

`yarn ts-typecheck && yarn lint` (dev server parado) — sem erro. QUnit do Step 2 de novo: os 4 testes novos PASS e nenhum antigo quebra.

- [ ] **Step 10: Stage**

```bash
git add webapp/model/formatter.ts webapp/test/unit/model/formatter.qunit.ts webapp/model/ServerRoutes.ts webapp/types/FinancialComposition.ts webapp/view/financialDocuments webapp/controller/financialDocuments/Detail.controller.ts
```
O controlador commita com:

```
feat(financial): título firme no Detail do título e origem das baixas

O Detail mostra romaneio, NF e o documento de entrada do firme; no provisório, o valor
contratado, o composto e o disponível para baixa, que passa a ser o teto da baixa. A tabela de
baixas ganha a coluna Origem (manual, estorno, abatimento, encontro de contas, dação). Cancelar
título some no firme da conciliação e no provisório composto, como o backend recusa.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 16: Frontend — seção "Composição" (Compor, Desfazer, "composto em")

**Files:**
- Create: `webapp/view/financialDocuments/fragments/Composition.fragment.xml`, `webapp/view/financialDocuments/fragments/ComposeDialog.fragment.xml`
- Modify: `webapp/view/financialDocuments/Detail.view.xml` (seção nova), `webapp/view/financialDocuments/fragments/Form.fragment.xml` ("Composto em")
- Modify: `webapp/controller/financialDocuments/Detail.controller.ts`

**Interfaces:**
- Consumes: Task 15 (rotas, tipos, `formatDecimal2`), backend Tasks 11–12.
- Produces: JSONModel `composition` = `{ lines: FinancialCompositionLine[], candidates: FinancialCompositionCandidate[], composedInKey: string | null, composedInCode: string }`; no controller: `_compositionKey: string`, `_actionRunning: boolean`, `loadComposition(documentKey: string): Promise<void>`, `runDocumentAction(path: string, payload: object, success: string): Promise<boolean>` (a Task 17 reusa os dois).

- [ ] **Step 1: Fragmento da seção**

`webapp/view/financialDocuments/fragments/Composition.fragment.xml` (atenção: nenhum `--` dentro de comentário XML):

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:core="sap.ui.core"
    core:require="{
      formatter: 'siagrob1/model/formatter'
    }"
>
  <!--
    Composição do provisório (spec 2026-10-13, títulos firmes): firmes e devoluções aglutinados pelo operador.
    Dados da function FinancialDocumentsGetComposition no JSONModel "composition". Devolução entra negativa.
  -->
  <Table
    class="sapUiSizeCondensed"
    items="{composition>/lines}"
    noDataText="Nenhum título composto. Use Compor para trazer os títulos firmes deste contrato.">
    <headerToolbar>
      <OverflowToolbar>
        <Title text="Composição" />
        <ToolbarSpacer />
        <Button
          text="Compor"
          type="Emphasized"
          icon="sap-icon://add"
          visible="{= ${path: 'Status', targetType: 'any'} !== 'Canceled' }"
          press=".onOpenComposeDialog" />
      </OverflowToolbar>
    </headerToolbar>
    <columns>
      <Column width="9rem"><Text text="Título" /></Column>
      <Column width="9rem"><Text text="Romaneio" /></Column>
      <Column width="9rem"><Text text="NF" /></Column>
      <Column><Text text="Produtor" /></Column>
      <Column width="8rem"><Text text="Tipo" /></Column>
      <Column width="10rem" hAlign="End"><Text text="Valor" /></Column>
      <Column width="4rem" />
    </columns>
    <items>
      <ColumnListItem>
        <cells>
          <Link text="{composition>DocumentCode}" press=".onOpenCompositionDocument" />
          <Text text="{composition>StorageTransactionCode}" />
          <Text text="{composition>TaxDocumentNumber}" />
          <Text text="({composition>CardCode}) {composition>CardName}" />
          <Text text="{path: 'composition>Nature', formatter: 'formatter.formatFinancialNature'}" />
          <ObjectNumber
            number="{path: 'composition>Amount', formatter: 'formatter.formatDecimal2'}"
            state="{= Number(${composition>Amount}) &lt; 0 ? 'Error' : 'None' }" />
          <Button icon="sap-icon://undo" type="Transparent" tooltip="Desfazer a composição" press=".onUncompose" />
        </cells>
      </ColumnListItem>
    </items>
  </Table>
</core:FragmentDefinition>
```

`git add` o arquivo.

- [ ] **Step 2: Diálogo Compor**

`webapp/view/financialDocuments/fragments/ComposeDialog.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:core="sap.ui.core"
    core:require="{
      formatter: 'siagrob1/model/formatter'
    }"
>
  <!--
    Candidatos à composição: firmes e devoluções do mesmo contrato, com saldo. A seleção fica no próprio JSONModel
    (composition>selected), na ordem da lista, que é a ordem em que entram no provisório.
  -->
  <Dialog title="Compor títulos no provisório" contentWidth="64rem">
    <content>
      <MessageStrip
        type="Information"
        showIcon="true"
        class="sapUiSmallMargin"
        text="Os títulos entram na ordem da lista. O firme que não couber entra parcial; a sobra pode ser baixada por dação. A devolução entra negativa." />
      <Table
        class="sapUiSizeCondensed"
        items="{composition>/candidates}"
        noDataText="Nenhum título firme ou de devolução com saldo neste contrato.">
        <columns>
          <Column width="3rem" />
          <Column width="9rem"><Text text="Título" /></Column>
          <Column width="8rem"><Text text="Tipo" /></Column>
          <Column width="9rem"><Text text="Romaneio" /></Column>
          <Column width="9rem"><Text text="NF" /></Column>
          <Column><Text text="Produtor" /></Column>
          <Column width="9rem" hAlign="End"><Text text="Em aberto" /></Column>
          <Column width="9rem" hAlign="End"><Text text="Entra com" /></Column>
        </columns>
        <items>
          <ColumnListItem>
            <cells>
              <CheckBox selected="{composition>selected}" />
              <Text text="{composition>Code}" />
              <Text text="{path: 'composition>Nature', formatter: 'formatter.formatFinancialNature'}" />
              <Text text="{composition>StorageTransactionCode}" />
              <Text text="{composition>TaxDocumentNumber}" />
              <Text text="({composition>CardCode}) {composition>CardName}" />
              <Text text="{path: 'composition>OpenAmount', formatter: 'formatter.formatDecimal2'}" />
              <Text text="{path: 'composition>SuggestedAmount', formatter: 'formatter.formatDecimal2'}" />
            </cells>
          </ColumnListItem>
        </items>
      </Table>
    </content>
    <footer>
      <OverflowToolbar>
        <ToolbarSpacer />
        <Button text="Compor" type="Emphasized" press=".onConfirmCompose" />
        <Button text="Fechar" press=".onCloseComposeDialog" />
      </OverflowToolbar>
    </footer>
  </Dialog>
</core:FragmentDefinition>
```

`git add` o arquivo.

- [ ] **Step 3: Seção e "Composto em"**

`Detail.view.xml`, entre a seção "Dados do Título" e a seção "Baixas":

```xml
      <uxap:ObjectPageSection titleUppercase="false" title="Composição"
        visible="{= ${path: 'Nature', targetType: 'any'} === 'Provisional' }">
        <uxap:subSections>
          <uxap:ObjectPageSubSection title=" " titleUppercase="false">
            <core:Fragment fragmentName="siagrob1.view.financialDocuments.fragments.Composition" type="XML" />
          </uxap:ObjectPageSubSection>
        </uxap:subSections>
      </uxap:ObjectPageSection>
```

`Form.fragment.xml`, no container "Documento fiscal" (Task 15), depois de "Documento de entrada":

```xml
          <f:FormElement label="Composto em" visible="{= !!${composition>/composedInKey} }">
            <f:fields>
              <Link text="{composition>/composedInCode}" press=".onOpenComposedIn"/>
            </f:fields>
          </f:FormElement>
```

- [ ] **Step 4: Controller**

`webapp/controller/financialDocuments/Detail.controller.ts`:

Imports a acrescentar:

```ts
import { Button$PressEvent } from "sap/m/Button";
import { Link$PressEvent } from "sap/m/Link";
import { odataValue, sendJson } from "siagrob1/helpers/FetchHelpers";
import { FinancialComposition, FinancialCompositionCandidate, FinancialCompositionLine } from "siagrob1/types/FinancialComposition";
```

Campos e `onInit` (trocar o `onInit` atual):

```ts
  private _composeDialog: Dialog;
  /** Título cujo quadro de composição a tela mostra; descarta resposta atrasada de outro título. */
  private _compositionKey: string;
  /** Trava de duplo clique das ações de composição, dação e compensação. */
  private _actionRunning = false;

  onInit(): void {
    this.getView().setModel(new JSONModel(Detail.emptyComposition()), "composition");
    this.getRouter().getRoute("financialDocumentsDetail")
      .attachPatternMatched((ev) => this.routeMatched(ev));
  }

  private static emptyComposition() {
    return {
      lines: [] as FinancialCompositionLine[],
      candidates: [] as FinancialCompositionCandidate[],
      composedInKey: null as string | null,
      composedInCode: "",
    };
  }
```

Em `routeMatched`, depois do `this.bindElement(...)`:

```ts
    void this.loadComposition(id);
```

Métodos novos (depois de `refreshDocument`):

```ts
  /**
   * Quadro de composição (provisório), "composto em" (firme/devolução) e candidatos (compor ou compensar), pela function
   * FinancialDocumentsGetComposition (spec 2026-10-13, títulos firmes).
   */
  private async loadComposition(documentKey: string): Promise<void> {
    this._compositionKey = documentKey;
    const model = this.getModel("composition") as JSONModel;
    model.setData(Detail.emptyComposition());

    const result = await sendJson("GET", `${this.api.financialDocumentsGetComposition}(Key=${documentKey})`);
    // Resposta atrasada de outro título (troca rápida de rota) não pode sobrescrever a tela.
    if (documentKey !== this._compositionKey) return;

    if (!result.ok) {
      MessageBox.error(result.message);
      return;
    }

    const data = odataValue<FinancialComposition>(result.data) ?? { Lines: [], Candidates: [] };
    const lines = data.Lines ?? [];
    const composedIn = lines.find((l) => l.DocumentKey === documentKey);

    model.setData({
      lines,
      candidates: data.Candidates ?? [],
      composedInKey: composedIn?.ProvisionalDocumentKey ?? null,
      composedInCode: composedIn?.ProvisionalCode ?? "",
    });
  }

  /** Executa uma action uma vez só (trava de duplo clique); recarrega o título e o quadro. Recusa aparece na tela. */
  private async runDocumentAction(path: string, payload: object, success: string): Promise<boolean> {
    if (this._actionRunning) return false;
    this._actionRunning = true;
    this.setBusy(true);

    try {
      const result = await sendJson("POST", path, payload);
      if (!result.ok) {
        MessageBox.error(result.message);
        return false;
      }

      MessageToast.show(success);
      this.refreshDocument();
      await this.loadComposition(this._compositionKey);
      return true;
    } finally {
      this.setBusy(false);
      this._actionRunning = false;
    }
  }

  async onOpenComposeDialog(): Promise<void> {
    const model = this.getModel("composition") as JSONModel;
    const candidates = (model.getProperty("/candidates") as FinancialCompositionCandidate[]) ?? [];

    if (!candidates.length) {
      MessageBox.information("Nenhum título firme ou de devolução com saldo neste contrato.");
      return;
    }

    model.setProperty("/candidates", candidates.map((c) => ({ ...c, selected: false })));

    this._composeDialog ??= await DialogHelper.createDialog(
      this, "siagrob1.view.financialDocuments.fragments.ComposeDialog");
    this._composeDialog.open();
  }

  onCloseComposeDialog(): void {
    this._composeDialog?.close();
  }

  async onConfirmCompose(): Promise<void> {
    const candidates = (this.getModel("composition") as JSONModel).getProperty("/candidates") as FinancialCompositionCandidate[];
    // Na ordem da lista: é a ordem em que entram no provisório (regra 10).
    const keys = candidates.filter((c) => c.selected).map((c) => c.DocumentKey);

    if (!keys.length) {
      MessageBox.alert("Selecione ao menos um título firme ou de devolução.");
      return;
    }

    this.onCloseComposeDialog();
    await this.runDocumentAction(this.api.financialDocumentsCompose,
      { Key: this._compositionKey, ComposedKeys: keys }, "Títulos compostos no provisório.");
  }

  async onUncompose(ev: Button$PressEvent): Promise<void> {
    const line = ev.getSource().getBindingContext("composition").getObject() as FinancialCompositionLine;

    if (!(await DialogHelper.confirmDialog(`Desfazer a composição do título ${line.DocumentCode}?`))) return;

    await this.runDocumentAction(this.api.financialDocumentsUncompose,
      { CompositionKey: line.CompositionKey }, "Composição desfeita.");
  }

  onOpenCompositionDocument(ev: Link$PressEvent): void {
    const line = ev.getSource().getBindingContext("composition").getObject() as FinancialCompositionLine;
    this.navTo("financialDocumentsDetail", { id: line.DocumentKey });
  }

  onOpenComposedIn(): void {
    const key = (this.getModel("composition") as JSONModel).getProperty("/composedInKey") as string;
    if (key) {
      this.navTo("financialDocumentsDetail", { id: key });
    }
  }
```

- [ ] **Step 5: Gates**

`yarn ts-typecheck && yarn lint` (dev server parado). Expected: sem erro. Se o lint reclamar de `no-unsafe-*` em `getObject()`, manter o `as Tipo` já escrito (é o padrão do `purchaseFiscalReconciliations/Main.controller.ts`).

- [ ] **Step 6: Stage**

```bash
git add webapp/view/financialDocuments webapp/controller/financialDocuments/Detail.controller.ts
```
O controlador commita com:

```
feat(financial): composição dos títulos firmes no Detail do provisório

O provisório ganha a seção Composição: o operador escolhe os firmes e devoluções do contrato
(com o valor que cada um entra, na ordem da lista), desfaz linha a linha e navega para o título
composto; o firme mostra em qual provisório está composto. Toda recusa do servidor aparece na
tela e o duplo clique não dispara a ação duas vezes.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 17: Frontend — "Baixar por dação" e "Compensar com firme"

**Files:**
- Create: `webapp/view/financialDocuments/fragments/DationDialog.fragment.xml`, `webapp/view/financialDocuments/fragments/NettingDialog.fragment.xml`
- Modify: `webapp/view/financialDocuments/fragments/Settlements.fragment.xml` (botões)
- Modify: `webapp/controller/financialDocuments/Detail.controller.ts`

**Interfaces:**
- Consumes: Task 16 (`runDocumentAction`, `_compositionKey`, modelo `composition` — para a devolução os `candidates` são os firmes soltos do contrato), Task 15 (rotas).
- Produces: `viewModel>/dationDialog` = `{ amount: number, settlementDate: string, notes: string }`; `viewModel>/nettingDialog` = `{ counterpartKey: string }`.

- [ ] **Step 1: Diálogo de dação**

`webapp/view/financialDocuments/fragments/DationDialog.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:f="sap.ui.layout.form"
    xmlns:core="sap.ui.core"
>
  <!-- Dação (spec 2026-10-13, regra 17): baixa sem conta e sem dinheiro da sobra do firme, com motivo obrigatório. -->
  <Dialog title="Baixar por dação">
    <content>
      <VBox class="sapUiSmallMargin" width="480px">
        <f:Form editable="true">
          <f:layout>
            <f:ColumnLayout columnsM="1" columnsL="1" columnsXL="1"/>
          </f:layout>
          <f:formContainers>
            <f:FormContainer>
              <f:formElements>
                <f:FormElement label="Valor">
                  <f:fields>
                    <Input value="{path: 'viewModel>/dationDialog/amount', type: 'sap.ui.model.odata.type.Double'}" required="true"/>
                  </f:fields>
                </f:FormElement>
                <f:FormElement label="Data">
                  <f:fields>
                    <DatePicker
                      value="{viewModel>/dationDialog/settlementDate}"
                      required="true"
                      displayFormat="dd/MM/yyyy"
                      valueFormat="yyyy-MM-dd"/>
                  </f:fields>
                </f:FormElement>
                <f:FormElement label="Motivo">
                  <f:fields>
                    <TextArea value="{viewModel>/dationDialog/notes}" rows="3" width="100%" maxLength="500" required="true"/>
                  </f:fields>
                </f:FormElement>
              </f:formElements>
            </f:FormContainer>
          </f:formContainers>
        </f:Form>
      </VBox>
    </content>
    <footer>
      <OverflowToolbar>
        <ToolbarSpacer />
        <Button text="Baixar por dação" type="Emphasized" press=".onConfirmDation"/>
        <Button text="Fechar" press=".onCloseDationDialog"/>
      </OverflowToolbar>
    </footer>
  </Dialog>
</core:FragmentDefinition>
```

`git add` o arquivo.

- [ ] **Step 2: Diálogo de compensação**

`webapp/view/financialDocuments/fragments/NettingDialog.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:core="sap.ui.core"
>
  <!--
    Compensar a devolução com um firme SOLTO do mesmo contrato (spec 2026-10-13, regra 20), pelo menor dos dois saldos.
    Itens: composition>/candidates, já carregados quando o título é aberto (o Select precisa dos itens antes do selectedKey).
    O valor compensado é o menor dos dois saldos e aparece no Detail depois de compensar.
  -->
  <Dialog title="Compensar com firme">
    <content>
      <VBox class="sapUiSmallMargin" width="520px">
        <Label text="Título firme" labelFor="financialNettingSelect"/>
        <Select
          id="financialNettingSelect"
          width="100%"
          forceSelection="false"
          selectedKey="{viewModel>/nettingDialog/counterpartKey}"
          items="{composition>/candidates}">
          <core:Item
            key="{composition>DocumentKey}"
            text="{composition>Code} · NF {composition>TaxDocumentNumber} · romaneio {composition>StorageTransactionCode}"/>
        </Select>
      </VBox>
    </content>
    <footer>
      <OverflowToolbar>
        <ToolbarSpacer />
        <Button text="Compensar" type="Emphasized" press=".onConfirmNetting"/>
        <Button text="Fechar" press=".onCloseNettingDialog"/>
      </OverflowToolbar>
    </footer>
  </Dialog>
</core:FragmentDefinition>
```

`git add` o arquivo.

- [ ] **Step 3: Botões na tabela de baixas**

`Settlements.fragment.xml`, na `OverflowToolbar`, depois do botão "Baixar":

```xml
        <Button type="Transparent" text="Baixar por dação" press=".onOpenDationDialog"
                visible="{= ${path: 'Nature', targetType: 'any'} === 'Firm'
                          &amp;&amp; (${path: 'OriginType', targetType: 'any'} === 'PurchaseFiscalReconciliation'
                              || ${path: 'OriginType', targetType: 'any'} === 'PurchaseInvoice')
                          &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Canceled'
                          &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Settled' }"/>
        <Button type="Transparent" text="Compensar com firme" press=".onOpenNettingDialog"
                visible="{= ${path: 'Nature', targetType: 'any'} === 'PurchaseReturn'
                          &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Canceled'
                          &amp;&amp; ${path: 'Status', targetType: 'any'} !== 'Settled' }"/>
```

- [ ] **Step 4: Controller**

`Detail.controller.ts` — campos e métodos novos (depois de `onOpenComposedIn`):

```ts
  private _dationDialog: Dialog;
  private _nettingDialog: Dialog;

  async onOpenDationDialog(): Promise<void> {
    const ctx = this.documentContext();
    if (!ctx) return;

    (this.getModel("viewModel") as JSONModel).setProperty("/dationDialog", {
      amount: Number(ctx.getProperty("OpenAmount") ?? 0),
      settlementDate: localIsoDate(new Date()),
      notes: "",
    });

    this._dationDialog ??= await DialogHelper.createDialog(
      this, "siagrob1.view.financialDocuments.fragments.DationDialog");
    this._dationDialog.open();
  }

  onCloseDationDialog(): void {
    this._dationDialog?.close();
  }

  async onConfirmDation(): Promise<void> {
    const form = (this.getModel("viewModel") as JSONModel).getProperty("/dationDialog") as {
      amount: number; settlementDate: string; notes: string;
    };

    if (!(Number(form.amount) > 0)) {
      MessageBox.alert("O valor da dação deve ser maior que zero.");
      return;
    }

    if (!form.settlementDate) {
      MessageBox.alert("Informe a data da dação.");
      return;
    }

    if (!form.notes?.trim()) {
      MessageBox.alert("Informe o motivo da dação.");
      return;
    }

    this.onCloseDationDialog();
    await this.runDocumentAction(this.api.financialDocumentsDation, {
      Key: this._compositionKey,
      Amount: Number(form.amount),
      SettlementDate: form.settlementDate,
      Notes: form.notes,
    }, "Dação registrada.");
  }

  async onOpenNettingDialog(): Promise<void> {
    const candidates = ((this.getModel("composition") as JSONModel)
      .getProperty("/candidates") as FinancialCompositionCandidate[]) ?? [];

    if (!candidates.length) {
      MessageBox.information("Nenhum título firme solto com saldo neste contrato para compensar.");
      return;
    }

    (this.getModel("viewModel") as JSONModel).setProperty("/nettingDialog", {
      counterpartKey: candidates[0].DocumentKey,
    });

    this._nettingDialog ??= await DialogHelper.createDialog(
      this, "siagrob1.view.financialDocuments.fragments.NettingDialog");
    this._nettingDialog.open();
  }

  onCloseNettingDialog(): void {
    this._nettingDialog?.close();
  }

  async onConfirmNetting(): Promise<void> {
    const counterpartKey = (this.getModel("viewModel") as JSONModel)
      .getProperty("/nettingDialog/counterpartKey") as string;

    if (!counterpartKey) {
      MessageBox.alert("Selecione o título firme.");
      return;
    }

    this.onCloseNettingDialog();
    await this.runDocumentAction(this.api.financialDocumentsNetting,
      { Key: this._compositionKey, CounterpartKey: counterpartKey }, "Devolução compensada.");
  }
```

(`localIsoDate`, `Dialog`, `DialogHelper`, `MessageBox`, `JSONModel` já são importados no controller.)

- [ ] **Step 5: Gates**

`yarn ts-typecheck && yarn lint` (dev server parado). Expected: sem erro.

- [ ] **Step 6: Stage**

```bash
git add webapp/view/financialDocuments webapp/controller/financialDocuments/Detail.controller.ts
```
O controlador commita com:

```
feat(financial): dação e compensação da devolução no Detail do título

O firme da conciliação ganha "Baixar por dação" (valor, data e motivo) para a sobra faturada
acima do contrato, e a devolução em aberto ganha "Compensar com firme", que escolhe um firme
solto do mesmo contrato e compensa pelo menor saldo.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 18: Frontend — Contas a Pagar: filtros Natureza e "Com saldo a resolver", colunas NF e romaneio

**Files:**
- Create: `webapp/helpers/FinancialDocumentFilterHelpers.ts`, `webapp/test/unit/helpers/FinancialDocumentFilterHelpers.qunit.ts`
- Modify: `webapp/test/unit/unitTests.qunit.ts`
- Modify: `webapp/controller/financialDocuments/FinancialDocumentsBaseController.ts` (`applyFilters`, `createColumnConfig`)
- Modify: `webapp/view/financialDocuments/fragments/Filterbar.fragment.xml`
- Modify: `webapp/view/accountsPayable/Main.view.xml`

**Interfaces:**
- Consumes: EDM da Task 3 (`OriginType`, `Nature`, `TaxDocumentNumber`, `StorageTransactionCode`).
- Produces: `pendingResolutionFilter(): string`; chave `filter>/PendingResolution` (boolean) na barra de filtros.

- [ ] **Step 1: Teste QUnit que falha**

`webapp/test/unit/helpers/FinancialDocumentFilterHelpers.qunit.ts`:

```ts
import { pendingResolutionFilter } from "siagrob1/helpers/FinancialDocumentFilterHelpers";

QUnit.module("FinancialDocumentFilterHelpers - Com saldo a resolver");

QUnit.test("firme e devolução da conciliação, não cancelados, com saldo pelas colunas reais", function (assert) {
	assert.strictEqual(
		pendingResolutionFilter(),
		"(OriginType eq 'PurchaseFiscalReconciliation' or OriginType eq 'PurchaseInvoice')"
		+ " and (Nature eq 'Firm' or Nature eq 'PurchaseReturn')"
		+ " and Status ne 'Canceled' and NetAmount gt SettledAmount");
});
```

`webapp/test/unit/unitTests.qunit.ts` — acrescentar no fim: `import "./helpers/FinancialDocumentFilterHelpers.qunit";`

`git add` o arquivo de teste.

- [ ] **Step 2: Rodar e ver falhar**

QUnit no navegador (`yarn start`, URL das Global Constraints), módulo "Com saldo a resolver". Expected: FAIL (módulo não encontrado). Parar o dev server pelo PID.

- [ ] **Step 3: Helper**

`webapp/helpers/FinancialDocumentFilterHelpers.ts`:

```ts
/**
 * Filtros crus ($filter em string) da lista de títulos: `sap.ui.model.Filter` sobre enum estoura "Unsupported type".
 */

/**
 * "Com saldo a resolver" (spec 2026-10-13, títulos firmes): firme ou devolução da Conciliação Fiscal ou firme do
 * complemento de valor (D11, origem PurchaseInvoice), não cancelado,
 * que ainda não foi todo composto, compensado ou baixado por dação. OpenAmount é [NotMapped] e não vai ao SQL — a
 * comparação é pelas colunas reais. A origem exclui o título a receber do washout (também Firm).
 */
export function pendingResolutionFilter(): string {
	return "(OriginType eq 'PurchaseFiscalReconciliation' or OriginType eq 'PurchaseInvoice')"
		+ " and (Nature eq 'Firm' or Nature eq 'PurchaseReturn')"
		+ " and Status ne 'Canceled' and NetAmount gt SettledAmount";
}
```

`git add` o arquivo.

- [ ] **Step 4: Base das listas**

`FinancialDocumentsBaseController.ts`:

1. Import: `import { pendingResolutionFilter } from "siagrob1/helpers/FinancialDocumentFilterHelpers";`
2. Em `applyFilters`, trocar a linha
```ts
      } else if (key === "BranchCode" || key === "CardCode") {
```
por
```ts
      } else if (key === "PendingResolution") {
        // "Com saldo a resolver" (spec 2026-10-13, títulos firmes).
        filters.push(`(${pendingResolutionFilter()})`);
      } else if (key === "BranchCode" || key === "CardCode") {
```
(fica entre o ramo `OverdueOnly` e o de `BranchCode`/`CardCode`, antes do `contains(...)` genérico; o `false` do CheckBox desmarcado já é ignorado no topo do laço.)

3. Em `createColumnConfig`, no `valueMap` de "Natureza", acrescentar `"PurchaseReturn": "Devolução",`; e depois da coluna "Contrato":

```ts
    aCols.push({ label: "NF", property: "TaxDocumentNumber", type: EdmType.String });
    aCols.push({ label: "Romaneio", property: "StorageTransactionCode", type: EdmType.String });
```

- [ ] **Step 5: Barra de filtros**

`Filterbar.fragment.xml`:

1. O `FilterGroupItem name="nature"` passa a `visibleInFilterBar="true"` e o `Select` dele fica:

```xml
        <Select forceSelection="false" selectedKey="{filter>/Nature}">
          <core:ListItem key="" text="Todas"/>
          <core:ListItem key="Provisional" text="Provisório"/>
          <core:ListItem key="Firm" text="Firme"/>
          <core:ListItem key="PurchaseReturn" text="Devolução"/>
          <core:ListItem key="Advance" text="Adiantamento"/>
        </Select>
```

2. Depois do `FilterGroupItem name="overdueOnly"`:

```xml
    <fb:FilterGroupItem name="pendingResolution" label="Com saldo a resolver" groupName="GroupPendingResolution" visibleInFilterBar="true">
      <fb:control>
        <CheckBox selected="{filter>/PendingResolution}"/>
      </fb:control>
    </fb:FilterGroupItem>
```

- [ ] **Step 6: Colunas da lista**

`webapp/view/accountsPayable/Main.view.xml`, logo depois da coluna "Contrato":

```xml
					<t:Column label="NF" width="10rem" sortProperty="TaxDocumentNumber">
						<t:template>
							<Text text="{TaxDocumentNumber}" wrapping="false"/>
						</t:template>
					</t:Column>
					<t:Column label="Romaneio" width="10rem" sortProperty="StorageTransactionCode">
						<t:template>
							<Text text="{StorageTransactionCode}" wrapping="false"/>
						</t:template>
					</t:Column>
```

- [ ] **Step 7: Gates**

`yarn ts-typecheck && yarn lint` (dev server parado); QUnit do Step 2: PASS.

- [ ] **Step 8: Stage**

```bash
git add webapp/helpers/FinancialDocumentFilterHelpers.ts webapp/test/unit/helpers/FinancialDocumentFilterHelpers.qunit.ts webapp/test/unit/unitTests.qunit.ts webapp/controller/financialDocuments/FinancialDocumentsBaseController.ts webapp/view/financialDocuments/fragments/Filterbar.fragment.xml webapp/view/accountsPayable/Main.view.xml
```
O controlador commita com:

```
feat(financial): Contas a Pagar filtra natureza e títulos firmes com saldo a resolver

A lista ganha o filtro Natureza (provisório, firme, devolução) e "Com saldo a resolver" — firme
ou devolução da conciliação que ainda não foi todo composto, compensado ou baixado por dação —
e as colunas NF e romaneio, também no Excel.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 19: Frontend — seção "Financeiro" no Detail do contrato de compra

**Files:**
- Create: `webapp/types/FinancialDocumentByContract.ts`
- Create: `webapp/view/purchaseContracts/fragments/PurchaseContractFinancialDocuments.fragment.xml`
- Modify: `webapp/controller/purchaseContracts/Detail.controller.ts`, `webapp/view/purchaseContracts/Detail.view.xml`

**Interfaces:**
- Consumes: function `FinancialDocumentsGetByContract` (já existente; DTO estendido na Task 11), rota `financialDocumentsGetByContract` (Task 15), `formatter.formatDecimal2` (Task 15).
- Produces: JSONModel `contractFinancial` = `{ rows: FinancialDocumentByContract[] }`; `loadContractFinancial(contractKey: string)`.

- [ ] **Step 1: Tipo**

`webapp/types/FinancialDocumentByContract.ts`:

```ts
/** Linha da function FinancialDocumentsGetByContract (PascalCase; Edm.Decimal pode chegar string). */
export type FinancialDocumentByContract = {
  Key: string;
  Code: string;
  Nature: string;
  Status: string;
  DueDate: string;
  NetAmount: number | string;
  SettledAmount: number | string;
  OpenAmount: number | string;
  ComposedAmount: number | string;
  StorageTransactionCode: string;
  TaxDocumentNumber: string;
};
```

`git add` o arquivo.

- [ ] **Step 2: Fragmento**

`webapp/view/purchaseContracts/fragments/PurchaseContractFinancialDocuments.fragment.xml`:

```xml
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:core="sap.ui.core"
    core:require="{
      formatter: 'siagrob1/model/formatter'
    }"
>
  <!--
    Seção Financeiro do contrato de compra (spec 2026-10-13, títulos firmes): provisórios, firmes e devoluções,
    pela function FinancialDocumentsGetByContract, no JSONModel "contractFinancial". Somente leitura. Composto só
    existe no provisório (nos demais vem 0,00).
  -->
  <Table
    class="sapUiSizeCondensed"
    items="{contractFinancial>/rows}"
    noDataText="Nenhum título financeiro neste contrato.">
    <columns>
      <Column width="9rem"><Text text="Título" /></Column>
      <Column width="8rem"><Text text="Natureza" /></Column>
      <Column width="9rem"><Text text="Situação" /></Column>
      <Column width="9rem"><Text text="Romaneio" /></Column>
      <Column width="9rem"><Text text="NF" /></Column>
      <Column hAlign="End"><Text text="Valor" /></Column>
      <Column hAlign="End"><Text text="Composto" /></Column>
      <Column hAlign="End"><Text text="Baixado" /></Column>
      <Column hAlign="End"><Text text="Em aberto" /></Column>
    </columns>
    <items>
      <ColumnListItem>
        <cells>
          <Link text="{contractFinancial>Code}" press=".onOpenContractFinancialDocument" />
          <Text text="{path: 'contractFinancial>Nature', formatter: 'formatter.formatFinancialNature'}" />
          <ObjectStatus
            text="{path: 'contractFinancial>Status', formatter: 'formatter.formatFinancialStatus'}"
            state="{path: 'contractFinancial>Status', formatter: 'formatter.stateFinancialStatus'}" />
          <Text text="{contractFinancial>StorageTransactionCode}" />
          <Text text="{contractFinancial>TaxDocumentNumber}" />
          <Text text="{path: 'contractFinancial>NetAmount', formatter: 'formatter.formatDecimal2'}" />
          <Text text="{path: 'contractFinancial>ComposedAmount', formatter: 'formatter.formatDecimal2'}" />
          <Text text="{path: 'contractFinancial>SettledAmount', formatter: 'formatter.formatDecimal2'}" />
          <Text text="{path: 'contractFinancial>OpenAmount', formatter: 'formatter.formatDecimal2'}" />
        </cells>
      </ColumnListItem>
    </items>
  </Table>
</core:FragmentDefinition>
```

`git add` o arquivo.

- [ ] **Step 3: Seção no Detail**

`webapp/view/purchaseContracts/Detail.view.xml`, logo depois da seção "Washouts":

```xml
      <uxap:ObjectPageSection titleUppercase="false" title="Financeiro">
				<uxap:subSections>
					<uxap:ObjectPageSubSection titleUppercase="false">
						 <core:Fragment fragmentName="siagrob1.view.purchaseContracts.fragments.PurchaseContractFinancialDocuments" type="XML" />
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
```

- [ ] **Step 4: Controller**

`webapp/controller/purchaseContracts/Detail.controller.ts`:

Imports:

```ts
import { Link$PressEvent } from "sap/m/Link";
import { odataCollection, sendJson } from "siagrob1/helpers/FetchHelpers";
import { FinancialDocumentByContract } from "siagrob1/types/FinancialDocumentByContract";
```

Campo, `onInit` e chamada na rota:

```ts
  /** Contrato cuja seção Financeiro a tela mostra; descarta resposta atrasada de outro contrato. */
  private _financialContractKey: string;

	onInit(): void  {	
		this.getView().setModel(new JSONModel({ rows: [] }), "contractFinancial");
		this.initContractDrafts();
		this.getRouter().getRoute("purchaseContractsDetail").attachPatternMatched((ev) => this.detailRouteMatched(ev));
	}
```

Em `detailRouteMatched`, depois de `void this.refreshStandaloneFlag();` (Task 2):

```ts
      void this.loadContractFinancial(id);
```

Métodos novos (depois de `detailRouteMatched`):

```ts
  /** Seção "Financeiro" (spec 2026-10-13, títulos firmes): provisórios, firmes e devoluções do contrato. */
  private async loadContractFinancial(contractKey: string): Promise<void> {
    this._financialContractKey = contractKey;
    const model = this.getModel("contractFinancial") as JSONModel;
    model.setData({ rows: [] });

    const result = await sendJson("GET",
      `${this.api.financialDocumentsGetByContract}(ContractType='Purchase',ContractKey=${contractKey})`);
    if (contractKey !== this._financialContractKey) return;

    if (!result.ok) {
      MessageBox.error(result.message);
      return;
    }

    model.setData({ rows: odataCollection<FinancialDocumentByContract>(result.data) });
  }

  onOpenContractFinancialDocument(ev: Link$PressEvent): void {
    const row = ev.getSource().getBindingContext("contractFinancial").getObject() as FinancialDocumentByContract;
    this.navTo("financialDocumentsDetail", { id: row.Key });
  }
```

- [ ] **Step 5: Gates**

`yarn ts-typecheck && yarn lint` (dev server parado). Expected: sem erro.

- [ ] **Step 6: Stage**

```bash
git add webapp/types/FinancialDocumentByContract.ts webapp/view/purchaseContracts/fragments/PurchaseContractFinancialDocuments.fragment.xml webapp/view/purchaseContracts/Detail.view.xml webapp/controller/purchaseContracts/Detail.controller.ts
```
O controlador commita com:

```
feat(financial): seção Financeiro no contrato de compra

O Detail do contrato lista os provisórios, firmes e devoluções do contrato com valor, composto,
baixado e em aberto, NF e romaneio do firme, e abre o título com um clique.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 20: Frontend — título firme na Conciliação Fiscal de Compras

**Files:**
- Modify: `webapp/types/PurchaseFiscalReconciliation.ts` (`FiscalReconciliationLink`)
- Modify: `webapp/view/purchaseFiscalReconciliations/fragments/LinksDialog.fragment.xml`
- Modify: `webapp/controller/purchaseFiscalReconciliations/Main.controller.ts`

**Interfaces:**
- Consumes: `FiscalReconciliationLinkDto.FirmDocumentKey/FirmDocumentCode/FirmSituation` (Task 11).
- Produces: `onOpenFirmDocument(ev: Link$PressEvent)`.

- [ ] **Step 1: Tipo**

`FiscalReconciliationLink` em `types/PurchaseFiscalReconciliation.ts`, depois de `CreatedBy: string;`:

```ts
  /** Título firme/devolução do vínculo (spec 2026-10-13, títulos firmes); null quando a linha não gera financeiro. */
  FirmDocumentKey: string;
  FirmDocumentCode: string;
  /** "Solto" | "Composto" | "Cancelado". */
  FirmSituation: string;
```

- [ ] **Step 2: Diálogo de vínculos**

`LinksDialog.fragment.xml`:

1. Em `<columns>`, entre a coluna "Quantidade (KG)" e a coluna vazia de 4rem:

```xml
          <Column><Text text="Título"/></Column>
          <Column><Text text="Situação do título"/></Column>
```

2. Nas células, entre o `ObjectNumber` da quantidade e o botão "Desvincular":

```xml
            <Link text="{links>FirmDocumentCode}" visible="{= !!${links>FirmDocumentKey} }" press=".onOpenFirmDocument"/>
            <ObjectStatus
              text="{links>FirmSituation}"
              state="{= ${links>FirmSituation} === 'Composto' ? 'Success' : ${links>FirmSituation} === 'Cancelado' ? 'Error' : 'Warning' }"/>
```

- [ ] **Step 3: Controller**

`purchaseFiscalReconciliations/Main.controller.ts` — import `import { Link$PressEvent } from "sap/m/Link";`; método novo depois de `onRemoveLink`:

```ts
  /** Abre o título firme do vínculo (spec 2026-10-13, títulos firmes). */
  onOpenFirmDocument(ev: Link$PressEvent): void {
    const link = ev.getSource().getBindingContext("links").getObject() as FiscalReconciliationLink;
    if (!link.FirmDocumentKey) return;

    this._linksDialog.close();
    this.navTo("financialDocumentsDetail", { id: link.FirmDocumentKey });
  }
```

- [ ] **Step 4: Gates**

`yarn ts-typecheck && yarn lint` (dev server parado). Expected: sem erro.

- [ ] **Step 5: Stage**

```bash
git add webapp/types/PurchaseFiscalReconciliation.ts webapp/view/purchaseFiscalReconciliations/fragments/LinksDialog.fragment.xml webapp/controller/purchaseFiscalReconciliations/Main.controller.ts
```
O controlador commita com:

```
feat(financial): título firme de cada vínculo na Conciliação Fiscal de Compras

O diálogo de NFs vinculadas do romaneio mostra o título firme que o vínculo gerou, com link,
e se ele está solto, composto ou cancelado — a recusa ao desvincular firme composto passa a ter
contexto na própria tela.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 21: Frontend — Documento de Entrada do tipo "Complemento de valor" (D11)

**Files:**
- Modify: `webapp/model/formatter.ts` (`formatPurchaseInvoiceType`), `webapp/test/unit/model/formatter.qunit.ts`
- Modify: `webapp/helpers/InvoiceChargeTotalsHelpers.ts`, `webapp/test/unit/helpers/InvoiceChargeTotalsHelpers.qunit.ts`
- Modify: `webapp/helpers/PurchaseInvoiceDraftHelpers.ts` (`InvoiceItemPayload`, `blankItemRow`)
- Modify: `webapp/controller/purchaseInvoices/BaseController.ts` (totais da grade), `webapp/controller/purchaseInvoices/Main.controller.ts` (Excel)
- Modify: `webapp/view/purchaseInvoices/fragments/Form.fragment.xml` (Tipo), `Filterbar.fragment.xml`, `Items.fragment.xml`

**Interfaces:**
- Consumes: backend Task 13 (`PurchaseInvoiceType.ValueComplement`, `PurchaseInvoiceItem.ComplementAmount` no EDM — `Edm.Decimal`, escrito com `sap.ui.model.odata.type.Double` como frete/seguro).
- Produces: `purchaseChargeLineOf(context)` (linha de cobrança da ENTRADA, com `ComplementAmount`); `lineItemsTotal(quantity, unitPrice, complement?)`, `lineGrandTotal(..., otherExpenses, complement?)`, `formatLineGrandTotal(..., otherExpenses, complement?)` — o 7º argumento é opcional, então a grade da SAÍDA (seis partes) e `chargeLineOf` não mudam (a linha de saída não tem `ComplementAmount`; ler a propriedade lá dispararia late property).

- [ ] **Step 1: Testes QUnit que falham**

Acrescentar ao fim de `webapp/test/unit/model/formatter.qunit.ts`:

```ts
QUnit.test("tipo Complemento de valor do documento de entrada (spec D11)", function (assert) {
	assert.strictEqual(formatter.formatPurchaseInvoiceType("ValueComplement"), "Complemento de valor");
	assert.strictEqual(formatter.formatPurchaseInvoiceType("Return"), "Devolução");
});
```

Acrescentar ao fim de `webapp/test/unit/helpers/InvoiceChargeTotalsHelpers.qunit.ts`:

```ts
QUnit.module("InvoiceChargeTotalsHelpers - complemento de valor (spec D11)");

QUnit.test("o valor do complemento é o total dos itens da linha, sem quantidade", function (assert) {
	assert.strictEqual(lineItemsTotal(0, 0, "1500.00"), 1500);
	assert.strictEqual(formatLineGrandTotal("0", "0", "10", "0", "0", "0", "1500"), "1.510,00");
	assert.strictEqual(formatLineGrandTotal("30000", "2", "1000", "100", "500", "400"), "61.000,00");
});

QUnit.test("os totais do documento somam o complemento", function (assert) {
	const totals = summarizeInvoiceCharges([{
		Quantity: 0, UnitPrice: 0, FreightValue: 0, InsuranceValue: 0, DiscountValue: 0, OtherExpensesValue: 0,
		ComplementAmount: "271.67",
	}]);
	assert.strictEqual(totals.items, 271.67);
	assert.strictEqual(totals.grandTotal, 271.67);
});
```

(acrescentar `lineItemsTotal` ao `import` do topo do arquivo de teste, se ainda não estiver.)

- [ ] **Step 2: Rodar e ver falhar**

QUnit no navegador (`yarn start`, URL das Global Constraints). Expected: os três testes novos FAIL. Parar o dev server pelo PID.

- [ ] **Step 3: Helpers**

`InvoiceChargeTotalsHelpers.ts`:

1. `ChargeLine` ganha `/** Valor do complemento (só a linha da entrada do tipo Complemento de valor, spec D11). */ ComplementAmount?: Amount;`
2. `lineItemsTotal`:

```ts
/** Total dos itens da linha (o vProd): quantidade × preço, mais o valor do complemento (D11), em centavos — como o `Total` do servidor. */
export function lineItemsTotal(quantity: unknown, unitPrice: unknown, complement?: unknown): number {
	return cents(toNumber(quantity) * toNumber(unitPrice) + toNumber(complement));
}
```

3. `lineGrandTotal` e `formatLineGrandTotal` ganham o 7º parâmetro `complement?: unknown`, repassado a `lineItemsTotal(quantity, unitPrice, complement)` (e o comentário de `formatLineGrandTotal` passa a "seis partes na saída, sete na entrada").
4. Em `summarizeInvoiceCharges`, `const items = sum((line) => lineItemsTotal(line.Quantity, line.UnitPrice, line.ComplementAmount));`
5. Função nova, logo depois de `chargeLineOf`:

```ts
/** `chargeLineOf` da ENTRADA: acrescenta o valor do complemento (D11). A saída não tem a propriedade e usa `chargeLineOf`. */
export function purchaseChargeLineOf(context: { getProperty(path: string): unknown }): ChargeLine {
	return { ...chargeLineOf(context), ComplementAmount: context.getProperty("ComplementAmount") as Amount };
}
```

`PurchaseInvoiceDraftHelpers.ts`: `InvoiceItemPayload` ganha `ComplementAmount: number;` e `blankItemRow()` ganha `ComplementAmount: 0,` (toda propriedade editável entra no `create()`, senão "Must not change a property before it has been read").

`controller/purchaseInvoices/BaseController.ts`: no import, trocar `chargeLineOf` por `purchaseChargeLineOf`, e na soma dos totais `contexts.map((ctx) => purchaseChargeLineOf(ctx))`.

- [ ] **Step 4: Tipo na tela**

1. `formatter.ts`, em `formatPurchaseInvoiceType`: `m.set("ValueComplement", "Complemento de valor");`
2. `Form.fragment.xml`, no `Select` do Tipo, depois de `<core:Item key="Return" text="Devolução" />`: `<core:Item key="ValueComplement" text="Complemento de valor" />`
3. `Filterbar.fragment.xml` (lista), depois de `<core:ListItem key="Return" text="Devolução"/>`: `<core:ListItem key="ValueComplement" text="Complemento de valor"/>`
4. `Main.controller.ts`, no `valueMap` da coluna "Tipo" do Excel: `"ValueComplement": "Complemento de valor",`

- [ ] **Step 5: Grade de itens**

`Items.fragment.xml` (as condições ficam nas COLUNAS, cujo contexto é o cabeçalho — a célula tem o contexto da linha e não enxerga `InvoiceType`):

1. Coluna "Natureza": `visible="{= ${ui>/taxMode} === true || ${path: 'InvoiceType', targetType: 'any'} === 'ValueComplement' }"` (no complemento a natureza é obrigatória em toda filial).
2. Colunas "Quantidade" e "Preço Unitário": acrescentar `visible="{= ${path: 'InvoiceType', targetType: 'any'} !== 'ValueComplement' }"`.
3. Coluna nova, logo depois da coluna "Preço Unitário":

```xml
      <!-- Complemento de valor (spec 2026-10-13 D11): a linha não tem quantidade nem preço, só o valor. Escrita de decimal
           em Double, como frete e seguro. -->
      <t:Column label="Valor do complemento" width="11rem" hAlign="End"
                visible="{= ${path: 'InvoiceType', targetType: 'any'} === 'ValueComplement' }">
        <t:template>
          <Input
            editable="{= ${ui>/editable} === true }"
            change=".onItemAmountChange"
            value="{
              path: 'ComplementAmount',
              type: 'sap.ui.model.odata.type.Double',
              formatOptions: {
                decimals: 2,
                decimalSeparator: ',',
                groupingEnabled: true,
                groupingSeparator: '.'
              }
            }" />
        </t:template>
      </t:Column>
```

4. Coluna "Total": nas `parts`, depois de `{ path: 'OtherExpensesValue', targetType: 'any' }`, acrescentar `, { path: 'ComplementAmount', targetType: 'any' }` (sétima parte).
5. Coluna "Contrato": `visible="{= ${path: 'InvoiceType', targetType: 'any'} === 'Normal' || ${path: 'InvoiceType', targetType: 'any'} === 'ValueComplement' }"` e `label="{= ${path: 'InvoiceType', targetType: 'any'} === 'ValueComplement' ? 'Contrato (obrigatório)' : 'Contrato' }"` (a recusa do servidor diz qual linha está sem contrato).

- [ ] **Step 6: Gates**

`yarn ts-typecheck && yarn lint` (dev server parado); QUnit do Step 2: PASS, e os testes antigos de `InvoiceChargeTotalsHelpers` continuam verdes.

- [ ] **Step 7: Stage**

```bash
git add webapp/model/formatter.ts webapp/test/unit/model/formatter.qunit.ts webapp/helpers/InvoiceChargeTotalsHelpers.ts webapp/test/unit/helpers/InvoiceChargeTotalsHelpers.qunit.ts webapp/helpers/PurchaseInvoiceDraftHelpers.ts webapp/controller/purchaseInvoices webapp/view/purchaseInvoices
```
O controlador commita com:

```
feat(invoice): documento de entrada do tipo complemento de valor na tela

A Entrada passa a oferecer o tipo Complemento de valor: as linhas trocam quantidade e preço pelo
valor do complemento, mostram natureza e contrato (obrigatório) e somam o valor no total; a
lista filtra e exporta o tipo.

Co-Authored-By: {MODELO} <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E232CybKHVd48fk27eGKkT
```

---

### Task 22: Verificação ponta a ponta (CEAGUI)

**Files:** nenhum código novo — achado vira correção na task dona, com commit próprio (`fix(financial): …`).

- [ ] **Step 1: Gates completos**

Backend (raiz): `dotnet build SiagroB1.sln`, `dotnet test SiagroB1.Application.Tests` (anotar a contagem), `has-pending-model-changes` dos dois contextos ("No changes…"). Frontend (raiz, dev server parado): `yarn ts-typecheck && yarn lint` e `yarn ui5lint` sem regra nova; QUnit unit (URL das Global Constraints) com os módulos novos verdes. `git status --short` limpo nos dois repos depois dos commits do controlador; `git log --oneline main..` nos dois mostra só os commits deste plano.

- [ ] **Step 2: Aplicar as migrations só no CEAGUI dev**

Ler antes as memórias `run-stack-locally` e `db-migration-profile-targets-production`. No PowerShell, na raiz do backend:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Ceagui-Development"
(Get-Content SiagroB1.Web\appsettings.Ceagui-Development.json -Raw | ConvertFrom-Json).ConnectionStrings.SiagroDB -split ';' | Where-Object { $_ -match '^(Server|Database)=' }
```
Expected: `Database=CEAGUI_SIAGRO_DEV` em servidor local. Qualquer outro banco: **parar**.

Antes do `AddFinancialDocumentComposition`, conferir que o índice único novo não vai colidir (título a receber de washout duplicado):

```sql
SELECT OriginType, OriginKey, COUNT(*) FROM FINANCIAL_DOCUMENTS
WHERE Nature IN (1, 4) AND Status <> 3 AND OriginKey IS NOT NULL
GROUP BY OriginType, OriginKey HAVING COUNT(*) > 1;
```
Expected: nenhuma linha (senão parar e levar ao usuário). Então:

```powershell
dotnet ef database update --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
dotnet ef database update --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web
```
Conferir na saída o banco-alvo e as quatro migrations (`DropPurchaseContractFiscalComplement`, `AddFinancialDocumentComposition`, `AddPurchaseInvoiceItemComplementAmount`, `RemovePurchaseContractFiscalEditPermission`). Nunca produção, nunca o profile `db-migration`.

- [ ] **Step 3: Subir a stack (profile `ceagui`)**

Gateway primeiro (`dotnet run --project SiagroB1.Gateway --launch-profile ceagui`, esperar "Loading proxy data from config"), depois Web (`--launch-profile ceagui`); frontend `yarn start:dev`. Login `admin`/`1234`. Navegar pelo **menu** (não por `goto` de hash).

- [ ] **Step 4: Roteiro (spec, "Verificação no navegador")**

Pré-requisito de dado: a natureza de **entrada** usada na NF do produtor e a de **devolução** usada no "Devolver" com "Gera financeiro" marcado (D10: lida da natureza na conciliação — sem ela não nasce firme). Desmarcar a flag de uma terceira natureza e conferir que a NF com ela, conciliada, não gera firme.

1. Contrato de compra **fixo** aprovado (provisório nasce — Contas a Pagar mostra "Provisório"); romaneio de compra (8) alocado nele.
2. Documento de entrada do produtor com a linha no contrato; Conciliação Fiscal de Compras → vincular NF × romaneio. No diálogo "NFs vinculadas", coluna Título com o código e "Solto". Contas a Pagar → "Com saldo a resolver" → o firme aparece com NF e romaneio, Natureza "Firme".
3. Abrir o provisório → seção Composição → Compor (marcar o firme; "Entra com" = valor do firme) → "Disponível para baixa" = valor do firme → Baixar (o diálogo sugere o disponível). Tentar baixar acima → recusa S6 na tela.
4. Contrato → Entregas → estornar a entrega desse romaneio → recusa S8 **visível na tela**. Se nenhuma mensagem aparecer (só o busy some), o handler global do OData não cobriu: em `webapp/controller/purchaseContracts/allocation/Main.controller.ts` (`onDelete`), acrescentar `.catch((err: Error) => MessageBox.error(err.message || "Erro ao estornar a entrega."))` antes do `.finally(...)`, `yarn ts-typecheck && yarn lint`, e o controlador commita `fix(financial): recusa do estorno de entrega aparece na tela`.
5. No documento de entrada do produtor, "Devolver" parte da quantidade; confirmar a devolução; romaneio 9 (Dev.Compra) alocado; conciliar a NF de devolução com ele → no Detail do firme de origem, tabela de Baixas com "Encontro de contas" (Origem) e a devolução Quitada; se a origem já estiver composta (passo 3), a devolução fica em aberto → "Compensar com firme" com outro firme solto, ou compor negativa no provisório.
6. Outra NF faturada acima do contrato (preço maior) → Compor: entra parcial ("Entra com" < "Em aberto") → no firme, "Baixar por dação" da sobra com motivo → firme Quitado; tabela de Baixas com "Abatimento" e "Dação".
7. Encerrar o contrato com o provisório não quitado → recusa S11 na tela (com "faturamento abaixo, por documento de complemento de valor"). Washout cobrindo a entrega a menos e aprovar; se sobrar diferença de valor (NF abaixo do contrato, inclusive centavos), lançar Documento de Entrada **Complemento de valor** de terceiro (Tipo "Complemento de valor": sem Quantidade/Preço, com "Valor do complemento", natureza que gera financeiro e contrato na linha; tentar sem contrato → recusa P11 na tela) → Confirmar → firme do complemento em Contas a Pagar ("Com saldo a resolver") → Compor no provisório → baixar o restante → provisório Quitado → Encerrar → passa; o provisório continua Quitado, sem cancelamento (spec D8/D11/regra 28). Reabrir → nenhum valor muda (regra 29). Cancelar o documento de complemento com o firme composto → recusa P12 na tela; a Conciliação Fiscal não lista a NF de complemento.
8. Contrato → seção Financeiro (provisório e firmes, valor, composto, baixado, em aberto; o link abre o título); Detail do título → coluna Origem nas Baixas; firme → "Composto em" leva ao provisório; "Cancelar título" não aparece no firme.
9. Derrubar a stack **por PID** nas portas 50000, 5246 e 8080 (`Get-NetTCPConnection -LocalPort <p> -State Listen` → `Stop-Process -Id <pid> -Force`) e conferir as portas de novo no fim (o file watcher ressuscita processo enquanto houver edição).

- [ ] **Step 5: Fechamento**

Anotar contagem de testes, achados e correções. Atualizar a memória do projeto (entrada nova para o sub-projeto 2: migrations aplicadas só no CEAGUI dev, a decisão pendente da flag em SAPB1/filial sem NF-e, e que o sub-projeto 1 foi desfeito). Merge em `main` é decisão do usuário; **nunca push**.

---

## Cobertura do spec

| Item do spec | Onde |
|---|---|
| Reversão do sub-projeto 1 — backend (tabela, permissão, serviço, function/action, controllers, trava da aprovação e `IConfiguration`, totalizador e 9 campos do DTO, `ContractChangeLogFields.Usage`, comentário do `FunruralType`) + migrations `DropPurchaseContractFiscalComplement` e `RemovePurchaseContractFiscalEditPermission` | Task 1 |
| Reversão do sub-projeto 1 — frontend (seções Complemento Fiscal e Funrural, KPI Líquido ao produtor, helper, rótulo `Usage`, grade e Total Impostos ocultos em STANDALONE; fica o erro da Aprovação e `applyContractTotals`) | Task 2 |
| Enums `PurchaseReturn = 4`, `PurchaseFiscalReconciliation = 7`, `Dation = 6`; colunas de `FINANCIAL_DOCUMENTS` e `FINANCIAL_SETTLEMENTS`; `FINANCIAL_DOCUMENT_COMPOSITIONS`; `AvailableToSettleAmount`; `IsBlockedForSettlement`; `IX_FINANCIAL_DOCUMENTS_FirmOrigin`; migration `AddFinancialDocumentComposition` (sem backfill) | Task 3 |
| Regra 1 (nasce na gravação do vínculo, um por vínculo, origem = vínculo) | Task 5 |
| Regra 2 ("gera financeiro" + contrato na linha) e **D10** (flag lida da natureza; SAPB1 = gera; sem natureza não gera) | Task 5 |
| Regra 3 (natureza pelo sentido) | Task 5 |
| Regra 4 (valor proporcional sem Funrural; SAPB1 bruto) | Task 5 |
| Regra 5 (campos; número antes do SaveChanges) | Task 5 |
| Regra 6 (quantidade alterada: recalcula solto, recusa composto) | Task 5 (+ regravação idêntica na Task 6) |
| Regra 7 (vínculo removido/NF ou romaneio cancelado: cancela solto, recusa composto ou com dação/encontro) | Task 5 (interceptor: Tasks 5 e 6) |
| Regra 8 (devolução nasce → compensação automática) | Task 8 |
| Regras 9–12 (compor: pré-condições, parcial, sem espaço, devolução negativa, abatimento, log) | Task 6 |
| Regra 13 (desfazer; recusa com baixa) | Task 6 |
| Regra 14 (estorno genérico recusa abatimento) | Task 6 (arquivo final na Task 8) |
| Regra 15 (guard: disponível do provisório; firme sem baixa manual) | Task 4 |
| Regra 16 (status do provisório pelo valor contratado) | Task 4 |
| Regra 17 (dação) | Task 7 |
| Regras 18–19 (origem da devolução; compensação automática do mais antigo) | Task 8 |
| Regra 20 (compor negativa ou "Compensar com firme") | Tasks 6 e 8 (tela: Tasks 16 e 17) |
| Regra 21 (estorno do par de encontro de contas) | Task 8 |
| Regra 22 (recebimento em dinheiro da devolução: fora) | não implementado de propósito — o guard (Task 4) recusa baixa em dinheiro da devolução |
| Regra 23 (estorno de romaneio: recusa com baixa; senão desfaz composição, vínculos e firmes) | Task 9 |
| Regra 24 (devolução e quebra sem trava) | Task 9 |
| Regra 25 (estorno de fixação com composição) | Task 10 |
| Regra 26 (washout abaixo do composto; cancelar provisório com composição) | Task 10 |
| Regra 27 (cancelar contrato/retirar aprovação com composição ou baixa) | Task 10 |
| Regra 28 (encerrar: firme com saldo, provisório não quitado; não cancela nem altera valor; S11 cita washout e complemento) | Task 10 |
| Regra 29 (reabrir não altera valor) | Task 10 |
| Regra 30 (interceptor de contrato encerrado não ganha título nem composição) | Task 10 |
| "Onde a lógica mora" (estático no Infra + sync na Application) | Tasks 5 e 9 |
| Serviços e API (actions com `ODataActionParameters` nulo guardado, `Edm.Double`, chaves Guid; function com DTO PascalCase; sem entity set de composição; `BadRequest(string)`) | Tasks 11 e 12 |
| Frontend — Detail do título (Composição, Compor, Desfazer, cabeçalho de valores; firme: romaneio, NF, documento de entrada, composto em, dação; devolução: compensar; Origem nas baixas) | Tasks 15, 16, 17 |
| Frontend — Contas a Pagar (Natureza, "Com saldo a resolver", colunas NF e romaneio) | Task 18 |
| Frontend — contrato: seção Financeiro | Tasks 11 e 19 |
| Frontend — Conciliação: firme e situação no vínculo | Tasks 11 e 20 |
| Frontend — recusas na tela (três formas) e armadilhas | Global Constraints; `sendJson` nas Tasks 16–19 |
| Testes (`SiagroB1.Application.Tests`) listados no spec | Tasks 1, 3–14 |
| **D11** — tipo `ValueComplement = 2`, linha só-valor (`ComplementAmount`), contrato e natureza obrigatórios (STANDALONE: que gere financeiro), só terceiro; NF-e própria recusada; tipo novo nos relatórios e guardas | Task 13 |
| **D11** — firme por linha na confirmação (`OriginType = PurchaseInvoice`, valor − Funrural, sem romaneio), compõe como firme, cancelamento/estorno do documento cancela o solto ou recusa, fora da conciliação (`EnsurePair`, `SenseMatches`, lista de linhas); índice `FirmOrigin` sem mudança | Task 14 (e `IsPurchaseFirmPayable` na Task 3, travas nas Tasks 10–11) |
| **D11** — tela da Entrada (tipo, valor do complemento, natureza e contrato, total, lista e Excel) | Task 21 |
| Verificação no navegador (CEAGUI), incluindo D10 e D11 | Task 22 |
