# SPED Fiscal (EFD ICMS/IPI) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** gerar, por filial e mês, o TXT da EFD ICMS/IPI importável no PVA, a partir das notas já escrituradas.

**Architecture:** escritor genérico de registros SPED em `SiagroB1.Fiscal/Sped` (formatação + bloco 9);
montagem dos blocos em `SiagroB1.Reports/Services/SpedFiscal`, lendo as mesmas linhas da Conferência
Fiscal e o Inventário PEPS; parâmetros por filial em tabela nova do `AppDbContext`, editados por OData
no Web; duas telas UI5 no menu Fiscal.

**Tech Stack:** .NET 10, EF Core (SQL Server / InMemory nos testes), xUnit, OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-23-sped-fiscal-efd-icms-ipi-design.md`

Worktrees: backend `C:\Projetos\SiagroB1\siagro-b1-backend-sped-fiscal`, frontend
`C:\Projetos\SiagroB1\siagro-b1-frontend-sped-fiscal`, branch `feature/sped-fiscal` nos dois.

## Global Constraints
- Só modo STANDALONE (`ErpMode.IsStandalone`; mensagem `"Disponível somente no modo STANDALONE."`).
- Bloco D fora: `D001|1|` + `D990|2|`.
- Arquivo ISO-8859-1 (`Encoding.Latin1`), cada linha `|REG|campo|...|` terminada em CRLF.
- Decimais com vírgula, sem separador de milhar (`0,00`); datas `ddMMyyyy`; campo vazio = `||`.
- Caractere `|` dentro de texto é removido; texto é `Trim()`.
- `COD_VER`: período < 2026-01 → `"019"`; ≥ 2026-01 → `"020"`.
- Totais de C190 e E110 = os da `FiscalConferenceService.BuildAsync` do mesmo pedido (mesma fonte `LoadLinesAsync`).
- Identificadores em inglês; textos de tela/erro em pt-BR. Commits `feat(reports): ...`; `DB:` trailer em commits com migration. Never push.

## Review Focus
1. Mês sem nenhum documento → arquivo válido com C001 `IND_MOV=1` e E110 zerado (test em Task 4).
2. NF-e própria cancelada no mês → C100 COD_SIT 02 só com chave/número, fora de C190/E110 (Task 3).
3. Produtor rural PF sem CNPJ → 0150 com CPF (`RuralProducerCpf`) e CNPJ vazio (Task 3).
4. Mesmo participante/produto em vários documentos → um único 0150/0200 (Task 3).
5. Saldo credor anterior maior que débitos → E110 VL_SLD_CREDOR_TRANSPORTAR > 0 e sem E116 (Task 4).

---

### Task 1: Escritor SPED (Fiscal)

**Files:**
- Create: `SiagroB1.Fiscal/Sped/SpedRecordWriter.cs`
- Test: `SiagroB1.Fiscal.Tests/Sped/SpedRecordWriterTests.cs`

**Interfaces:**
- Produces: `public sealed class SpedRecordWriter` com
  `void Add(string register, params object?[] fields)`,
  `int CountOf(string register)`, `int CountOfBlock(char block)`,
  `byte[] Finish()` (fecha o bloco 9: 9001, 9900 por registro inclusive os do próprio bloco 9, 9990, 9999; devolve Latin1).
  Formatação: `decimal` → 2 casas vírgula; `SpedDecimal(value, places)` struct para outras casas;
  `DateTime` → ddMMyyyy; `null` → vazio; `string` → sem `|`, trim.
  Também `static string ClosingCount(...)` não é necessário: o chamador adiciona `X990` com `CountOfBlock(X) + 1`.

- [ ] **Step 1: testes** — `Finish_WritesBlock9WithCountsIncludingItself`: Add 0000, 0001, 0990 → linhas
  terminam com `|9001|0|`, `|9900|0000|1|`, …, `|9900|9900|N|`, `|9990|K|`, `|9999|T|` onde T = total de linhas;
  `Add_FormatsDecimalDateAndNull`: `Add("C190", 1234.5m, new DateTime(2026,1,31), null, " a|b ")` → `|C190|1234,50|31012026||ab|`;
  `Finish_UsesLatin1AndCrlf`: "Ç" vira byte 0xC7; linhas separadas por `\r\n`.
- [ ] **Step 2:** `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~SpedRecordWriter"` → FAIL (tipo inexistente).
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** mesmo comando → PASS.
- [ ] **Step 5:** commit `feat(reports): escritor de registros do SPED`.

### Task 2: Parâmetros do SPED Fiscal por filial (Domain/Infra/Web/Migrations)

**Files:**
- Create: `SiagroB1.Domain/Entities/SpedFiscalSettings.cs` (tabela `SPED_FISCAL_SETTINGS`, PK `BranchCode`)
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs` (`DbSet<SpedFiscalSettings> SpedFiscalSettings`)
- Create: `SiagroB1.Application/Services/SpedFiscalSettings/SpedFiscalSettingsService.cs` (+ interface `ISpedFiscalSettingsService`), registrar em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Create: `SiagroB1.Web/Controllers/SpedFiscalSettingsController.cs` — `GET odata/SpedFiscalSettingsGet(branchCode)`, `POST odata/SpedFiscalSettingsSave` (mesmo padrão de `AccountingSettingsController`)
- Create: migrations `AddSpedFiscalSettings` (AppDbContext) e `AddSpedFiscalMenus` (CommonDbContext: itens `spedFiscal` "SPED Fiscal" ordem 9 e `spedFiscalSettings` "Parâmetros do SPED Fiscal" ordem 10, ParentKey `fiscal`, StandaloneOnly, ícone `sap-icon://document-text`, ROLE_MENUS ADMIN com GUIDs fixos)
- Test: `SiagroB1.Application.Tests/SpedFiscal/SpedFiscalSettingsServiceTests.cs`

**Interfaces:**
- Produces entidade: `BranchCode` (string, chave), `Profile` (string "A"/"B"/"C", default "A"),
  `ActivityType` (int 0 industrial / 1 outros, default 1), `AccountantName`, `AccountantCpf`, `AccountantCrc`,
  `AccountantCnpj?`, `AccountantZipCode?`, `AccountantStreet?`, `AccountantNumber?`, `AccountantComplement?`,
  `AccountantDistrict?`, `AccountantPhone?`, `AccountantEmail?`, `AccountantMunicipalityCode?`,
  `IcmsRevenueCode` (string), `IcmsDueDay` (int 1..31, default 10), `UpdatedAt`, `UpdatedBy`.
- `Task<SpedFiscalSettings?> GetAsync(string branchCode)`; `Task<SpedFiscalSettings> SaveAsync(SpedFiscalSettings s, string user)` — upsert.

- [ ] **Step 1: testes** — `Save_CreatesThenUpdates`; `Save_RejectsInvalidCpf` (`BusinessException` "CPF do contabilista inválido."); `Save_RejectsDueDayOutOfRange` ("Dia de vencimento deve estar entre 1 e 31."); `Save_RejectsProfileOtherThanABC` ("Perfil deve ser A, B ou C.").
- [ ] **Step 2:** `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SpedFiscalSettings"` → FAIL.
- [ ] **Step 3:** implementar entidade, serviço, controller, DI; gerar migrations (`dotnet ef migrations add ... --context AppDbContext|CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`, com `ASPNETCORE_ENVIRONMENT` explícito de dev) e conferir o SQL gerado.
- [ ] **Step 4:** testes PASS; `dotnet build SiagroB1.sln` sem erros.
- [ ] **Step 5:** commit `feat(reports): parâmetros do SPED Fiscal por filial` com `DB: AddSpedFiscalSettings, AddSpedFiscalMenus`.

### Task 3: Carga dos dados do período (Reports)

**Files:**
- Create: `SiagroB1.Reports/Services/SpedFiscal/SpedFiscalDataLoader.cs`, `SiagroB1.Reports/Dtos/SpedFiscalDtos.cs`
- Modify: `SiagroB1.Reports/Services/FiscalConferenceService.cs` — expor em `FiscalDocumentLineDto` (Dtos) os campos que faltam: `IssuerType` ("0" própria/"1" terceiro), `CardCode`, `Quantity`, `UnitOfMeasureCode`, `UnitPrice`, `DiscountValue`, `FreightValue`, `InsuranceValue`, `OtherExpensesValue`, `PisBase/Rate`, `CofinsBase/Rate`, `NfeItemNumber`, `FreightTerms`, `IssueDate`, `IsComplement` (item com `ComplementedItemKey`/tipo complementar). Sem mudar o que a Conferência já retorna.
- Test: `SiagroB1.Application.Tests/SpedFiscal/SpedFiscalDataLoaderTests.cs` (seed via `InvoiceReportSeed`)

**Interfaces:**
- Consumes: `FiscalConferenceService.LoadLinesAsync(FiscalConferenceRequest)`.
- Produces: `SpedFiscalDataLoader(IUnitOfWork db, FiscalConferenceService conference)`,
  `Task<SpedFiscalData> LoadAsync(SpedFiscalRequest request)`.
  `SpedFiscalRequest { BranchCode; Year; Month; Purpose (0/1); bool IncludeInventory; DateTime? InventoryDate; string InventoryReason ("01".."06", default "01"); decimal PreviousCreditBalance }`.
  `SpedFiscalData { Branch; Settings?; List<FiscalDocumentLineDto> Lines; List<SpedVoidDocument> VoidDocuments; List<SpedParticipant> Participants; List<SpedItem> Items; List<SpedUnit> Units; List<SpedUnitConversion> Conversions; FiscalInventoryData? Inventory }`.
  `SpedVoidDocument { Direction "0"/"1"; Situation "02"|"04"|"05"; Model; Series; Number; AccessKey }` — só NF-e própria (saída, e entrada com `IssuerType==Own`) com `NfeStatus` Cancelled/Denied/Voided e data no mês.
  `SpedParticipant { Code; Name; CountryCode "01058"; Cnpj; Cpf; StateRegistration; MunicipalityCode; Street; Number; Complement; District }` — endereço: 1º `Address` com MunicipalityCode.
  `SpedItem { Code; Description; UnitCode; ItemType "00" ou "09" (código = `Branch.FreightServiceItemCode`); Ncm; Cest }`.

- [ ] **Step 1: testes** — `Load_OwnCancelledNfeBecomesVoidDocumentWithSituation02` (e não entra em `Lines`);
  `Load_RuralProducerIndividualUsesCpf`; `Load_ParticipantsAndItemsAreDistinct`;
  `Load_ConversionWhenLineUnitDiffersFromItemUnit` (fator do `ItemComplement`);
  `Load_ThirdPartyCancelledIsIgnored`.
- [ ] **Step 2:** `--filter "FullyQualifiedName~SpedFiscalDataLoader"` → FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS, e `--filter "FullyQualifiedName~FiscalConference"` continua PASS.
- [ ] **Step 5:** commit `feat(reports): carga dos dados do SPED Fiscal`.

### Task 4: Montagem do arquivo + validações + endpoint

**Files:**
- Create: `SiagroB1.Reports/Services/SpedFiscal/SpedFiscalFileService.cs`, `SpedFiscalValidator.cs`
- Create: `SiagroB1.Reports/Controllers/SpedFiscalController.cs`
- Test: `SiagroB1.Application.Tests/SpedFiscal/SpedFiscalFileServiceTests.cs`, `SpedFiscalValidatorTests.cs`, `SpedFiscalControllerTests.cs`

**Interfaces:**
- Consumes: Task 1 `SpedRecordWriter`, Task 3 `SpedFiscalDataLoader`, `FiscalInventoryService.BuildAsync`.
- Produces: `SpedFiscalValidator.Validate(SpedFiscalData) -> List<SpedFiscalIssueDto>` (`Blocking` bool, `Message`, `Reference`);
  `SpedFiscalFileService.CheckAsync(SpedFiscalRequest) -> SpedFiscalCheckDto { Issues; Totals (Debits, Credits, Balance) }`;
  `SpedFiscalFileService.GenerateAsync(SpedFiscalRequest) -> (byte[] Content, string FileName)` lança `BusinessException` com as mensagens bloqueantes juntas.
  Controller: `POST /reports/SpedFiscal/Check` (JSON) e `POST /reports/SpedFiscal` (`text/plain; charset=iso-8859-1`, nome `SPED_FISCAL_<CNPJ>_<AAAAMM>.txt`).

Regras de registro (fixas):
- 0000: COD_VER pela regra global, COD_FIN = Purpose, DT_INI/DT_FIN do mês, NOME = LegalName, CNPJ, CPF vazio, UF, IE, COD_MUN, IM vazio, SUFRAMA vazio, IND_PERFIL = Profile, IND_ATIV = ActivityType.
- 0001 `0`; 0005 (TradeName, CEP, endereço, número, complemento, bairro, fone, fax vazio, e-mail vazio); 0100 do contabilista; 0150/0190/0200/0220 dos dados carregados; 0990.
- B001 `1`/B990 `2`.
- C001 `0` se houver C100, senão `1`. C100 regular: IND_OPER (0 entrada / 1 saída pelo `DocumentType`), IND_EMIT, COD_PART, COD_MOD, COD_SIT ("06" se complementar, senão "00"), SER, NUM_DOC, CHV_NFE, DT_DOC, DT_E_S, VL_DOC (soma OperationValue), IND_PGTO "2"(outros), VL_DESC, VL_ABAT_NT 0, VL_MERC (Σ qtd×preço), IND_FRT (Cif→0, Fob→1, Ter→2, None→9), VL_FRT, VL_SEG, VL_OUT_DA, VL_BC_ICMS, VL_ICMS, BC/ICMS-ST 0, VL_IPI 0, VL_PIS, VL_COFINS, PIS/COFINS-ST 0. C100 de `VoidDocuments`: só IND_OPER, IND_EMIT, COD_MOD, COD_SIT, SER, NUM_DOC, CHV_NFE.
- C170 só para IND_EMIT=1. C190 por (CST com origem, CFOP, alíquota) do documento.
- E001 `0`, E100 (período), E110: VL_TOT_DEBITOS = Σ ICMS das linhas "S", VL_TOT_CREDITOS = Σ "E", VL_SLD_CREDOR_ANT = PreviousCreditBalance, saldo = débitos − créditos − anterior; positivo → VL_ICMS_RECOLHER e E116 (COD_OR "000", VL_OR, DT_VCTO = IcmsDueDay do mês seguinte limitado ao último dia, COD_REC = IcmsRevenueCode, MES_REF `MMyyyy`); negativo → VL_SLD_CREDOR_TRANSPORTAR.
- G001 `1`/G990. H: se IncludeInventory, H001 `0`, H005 (DT_INV, VL_INV, MOT_INV), H010 por linha (COD_ITEM, UNID, QTD 3 casas, VL_UNIT 6 casas, VL_ITEM, IND_PROP "0"); senão H001 `1`. K001 `1` + K990 `2`. 1001 `0`, 1010 com todos os indicadores "N". 9 pelo writer.
- Bloqueantes: mensagens do spec, em pt-BR, ex. `"Filial sem CNPJ."`, `"Parâmetros do SPED Fiscal não cadastrados para a filial."`, `"Participante {code} sem município."`, `"Produto {code} sem NCM."`, `"Item {doc}/{item} sem CFOP."`, `"Inventário com saldo negativo: {code}."`. Avisos = demais `FiscalConferenceService` Issues.

- [ ] **Step 1: testes** — `Generate_EmptyMonthHasC001IndMov1AndZeroE110`;
  `Generate_C190AndE110MatchConference` (mesmo seed, compara com `FiscalConferenceService.BuildAsync`);
  `Generate_OwnSaleHasNoC170_ThirdPartyPurchaseHas`;
  `Generate_PreviousCreditLargerThanDebitsCarriesForwardWithoutE116`;
  `Generate_PositiveBalanceWritesE116WithDueDate` (período 01/2026 com IcmsDueDay 31 → DT_VCTO `28022026`; período 02/2026 com dia 10 → `10032026`);
  `Generate_InventoryBlockH`; `Generate_Block9CountsMatchLines`;
  `Validate_MissingSettingsIsBlocking`; `Controller_NonStandaloneReturnsBadRequest`.
- [ ] **Step 2:** `--filter "FullyQualifiedName~SpedFiscal"` → FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS; suíte completa das duas test projects sem regressões novas.
- [ ] **Step 5:** commit `feat(reports): geração do arquivo do SPED Fiscal`.

### Task 5: Telas no frontend

**Files (frontend worktree):**
- Create: `webapp/view/SpedFiscal.view.xml`, `webapp/controller/spedFiscal/Main.controller.ts`
- Create: `webapp/view/SpedFiscalSettings.view.xml`, `webapp/controller/spedFiscalSettings/Main.controller.ts`
- Modify: `webapp/manifest.json` (rotas `spedFiscal`, `spedFiscalSettings` — mesmo name das Keys de menu), `webapp/model/ServerRoutes.ts`, `webapp/i18n/i18n.properties`

Padrões: copiar estrutura de `fiscalConference` (filtros filial/mês) e de `accountingSettings` (formulário `Form` + `ColumnLayout`).
SPED Fiscal: filial, mês/ano, finalidade (Original/Retificadora), "Incluir inventário (bloco H)" + data + motivo, "Saldo credor do período anterior"; botão "Verificar" (lista inconsistências: Bloqueante/Aviso, e totais débito/crédito/saldo) e "Gerar arquivo" (download do blob, desabilitado com bloqueante).
Parâmetros: filial + campos da Task 2, rótulos pt-BR.

- [ ] **Step 1:** `npm run ts-typecheck` e `npm run lint` limpos.
- [ ] **Step 2:** commit `feat(reports): telas do SPED Fiscal e dos parâmetros`.

### Task 6: Verificação ponta a ponta

- [ ] Subir stack local CEAGUI (profile `ceagui`, `yarn start:dev`), aplicar as migrations nos bancos locais CEAGUI (conferir antes `shared-local-db` para migrations alheias).
- [ ] Pela home → Fiscal → Parâmetros do SPED Fiscal: cadastrar filial; → SPED Fiscal: Verificar e Gerar um mês com notas.
- [ ] Conferir: totais E110 = Conferência Fiscal do mês; bloco 9 consistente; abrir o TXT e checar 0000/C100/C190/E110 por amostragem. (PVA não roda aqui — dizer isso ao usuário.)
- [ ] Derrubar a stack pelo PID.
