# EFD Contribuições Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** gerar, por CNPJ raiz e mês, o TXT da EFD Contribuições (PIS/COFINS) importável no PVA, nos regimes cumulativo e não cumulativo.

**Architecture:** reaproveita o motor do SPED Fiscal: `SpedRecordWriter` (Fiscal), linhas de
`FiscalConferenceService.LoadLinesAsync`, participantes/itens/unidades do `SpedFiscalDataLoader` (extraídos
para um serviço comum), e o padrão loader → validator → file service → controller em
`SiagroB1.Reports/Services/EfdContributions`. Dois campos novos em `USAGES`; tela UI5 no menu Fiscal.

**Tech Stack:** .NET 10, EF Core (SQL Server/InMemory), xUnit, OpenUI5 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-23-efd-contributions-design.md`

Worktrees: backend `C:\Projetos\SiagroB1\siagro-b1-backend-efd-contributions`, frontend
`C:\Projetos\SiagroB1\siagro-b1-frontend-efd-contributions`, branch `feature/efd-contributions`.

## Global Constraints
- Só STANDALONE (`ErpMode.IsStandalone`; `"Disponível somente no modo STANDALONE."`).
- Arquivo ISO-8859-1, `|REG|...|` CRLF, decimais vírgula sem milhar, datas ddMMyyyy (via `SpedRecordWriter`).
- Um arquivo por CNPJ raiz (8 primeiros dígitos do `Branch.TaxId`), declarante = filial `/0001`; um 0140 e um C010 por filial.
- D001 `1` (CT-e fora); A001, F001, I001, P001 `1`.
- Tributo lido da linha (CstPis/PisBase/PisRate/PisValue e COFINS), nunca recalculado; valores de PIS/COFINS por linha = os da Conferência Fiscal.
- COD_INC_TRIB: só NonCumulative → "1"; só Cumulative → "2"; ambos → "3". IND_REG_CUM "9" quando houver cumulativo, senão vazio.
- M205 códigos de receita: não cumulativo PIS "6912" / COFINS "5856"; cumulativo PIS "8109" / COFINS "2172".
- Identificadores em inglês; telas/erros pt-BR. Commits `feat(reports): ...` / `feat(master-data): ...`; `DB:` trailer em commit com migration. Never push.

## Review Focus
1. Mês sem documentos → arquivo válido com C001 `1` e M200/M600 zerados (Task 4).
2. Duas filiais do mesmo CNPJ raiz → dois 0140 e dois C010, cada um só com os seus documentos (Task 3).
3. Filial com outro CNPJ raiz no banco → fica fora do arquivo (Task 3).
4. Venda CST 09 sem "Natureza da receita" na natureza → bloqueante (Task 4).
5. Crédito CST 50 maior que a contribuição no não cumulativo → M200 usa só até a contribuição e a sobra vai para 1100/1500 (Task 4).

---

### Task 1: Campos da natureza de operação (Domain/Web/Migration + tela)

**Files:**
- Modify: `SiagroB1.Domain/Entities/Usage.cs` — `string? PisCofinsRevenueNatureCode` (VARCHAR(3)), `string? PisCofinsCreditBaseCode` (VARCHAR(2)).
- Migration AppDbContext `AddUsagePisCofinsEfdCodes` (só as 2 colunas).
- Modify o serviço de validação de natureza (procurar `UsageService`/`UsagesCreate|Update`): código de natureza da receita = 3 dígitos, base de crédito = 2 dígitos `01`..`18`; mensagens `"Natureza da receita deve ter 3 dígitos."`, `"Base de cálculo do crédito deve estar entre 01 e 18."`.
- Frontend: ObjectPage/Detalhe da natureza (`webapp/view/usages*`), seção PIS/COFINS: "Natureza da receita (EFD Contribuições)" e "Base de cálculo do crédito (EFD Contribuições)".
- Test: `SiagroB1.Application.Tests/Usages/UsagePisCofinsEfdCodesTests.cs`.

- [ ] testes `Save_AcceptsValidCodes`, `Save_RejectsRevenueNatureNot3Digits`, `Save_RejectsCreditBaseOutOfRange` → FAIL → implementar → PASS; frontend `npm run ts-typecheck` + `npm run lint` limpos.
- [ ] commits: backend `feat(master-data): códigos da EFD Contribuições na natureza de operação` (`DB: AddUsagePisCofinsEfdCodes`); frontend idem.

### Task 2: Extrair participantes/itens/unidades comuns

**Files:**
- Create: `SiagroB1.Reports/Services/Sped/SpedMasterDataLoader.cs` — move de `SpedFiscalDataLoader` a montagem de `SpedParticipant`, `SpedItem`, `SpedUnit`, `SpedUnitConversion`, `MissingParticipantCodes`, `UnconvertibleUnits`.
- Modify: `SpedFiscalDataLoader` passa a usar o serviço; registrar no DI do Reports.

**Interfaces:**
- Produces: `SpedMasterDataLoader(IUnitOfWork db)` com
  `Task<SpedMasterData> LoadAsync(IReadOnlyCollection<FiscalDocumentLineDto> lines, IReadOnlyCollection<FiscalInventoryRowDto>? inventory, Func<FiscalDocumentLineDto,bool> writesItemLine)`
  → `SpedMasterData { Participants; Items; Units; Conversions; MissingParticipantCodes; UnconvertibleUnits }`.
  `writesItemLine` decide quais linhas contam para unidade/conversão (SPED Fiscal: só terceiro; Contribuições: todas).

- [ ] Refactor puro: `--filter "FullyQualifiedName~SpedFiscal"` continua 100% verde antes e depois; nenhum teste alterado em asserção.
- [ ] commit `refactor(reports): extrai cadastro comum do SPED`.

### Task 3: Carga do período por CNPJ raiz

**Files:**
- Create: `SiagroB1.Reports/Services/EfdContributions/EfdContributionsDataLoader.cs`, `SiagroB1.Reports/Dtos/EfdContributionsDtos.cs`.
- Test: `SiagroB1.Application.Tests/EfdContributions/EfdContributionsDataLoaderTests.cs`.

**Interfaces:**
- `EfdContributionsRequest { BranchCode; Year; Month; Purpose (0/1); string? PreviousReceiptNumber }`.
- `EfdContributionsData { Branch Declarant; SpedFiscalSettings? Settings; List<EfdEstablishment> Establishments; Dictionary<int,Usage> Usages }`;
  `EfdEstablishment { Branch Branch; List<FiscalDocumentLineDto> Lines; SpedMasterData MasterData }` (um por filial do CNPJ raiz, ordenado por CNPJ).
- `FiscalDocumentLineDto` precisa de `UsageCode` (adicionar no mapper da Conferência, aditivo).
- Consumes: `FiscalConferenceService.LoadLinesAsync` (uma chamada por filial), `SpedMasterDataLoader` (Task 2) com `writesItemLine = _ => true`.

- [ ] testes `Load_GroupsBranchesBySameCnpjRoot`, `Load_IgnoresBranchWithOtherCnpjRoot`, `Load_DeclarantIsHeadOffice0001`, `Load_NoHeadOfficeLeavesDeclarantNull` → FAIL → implementar → PASS; `~FiscalConference` verde.
- [ ] commit `feat(reports): carga dos dados da EFD Contribuições`.

### Task 4: Arquivo, validação e endpoint

**Files:**
- Create: `SiagroB1.Reports/Services/EfdContributions/{EfdContributionsFileService,EfdContributionsValidator}.cs`, `SiagroB1.Reports/Controllers/EfdContributionsController.cs`.
- Test: `SiagroB1.Application.Tests/EfdContributions/{EfdContributionsFileServiceTests,EfdContributionsValidatorTests,EfdContributionsControllerTests}.cs`.

**Interfaces:**
- `EfdContributionsFileService.CheckAsync(req) -> EfdContributionsCheckDto { Issues (Blocking, Message, Reference); Totals { PisDue, PisCredit, PisPayable, CofinsDue, CofinsCredit, CofinsPayable } }`;
  `GenerateAsync(req) -> (byte[] Content, string FileName)` (`EFD_CONTRIBUICOES_<CNPJ>_<AAAAMM>.txt`), `BusinessException` com os bloqueantes.
- Controller `POST /reports/EfdContributions/Check` e `POST /reports/EfdContributions`.

Regras (do spec; campos e ordem pelo Guia Prático da EFD-Contribuições, comentados acima de cada registro):
- Bloco 0/A/C/D/F/I/M/P/1/9 como no spec. C100 por documento sob o C010 da filial; C170 por linha com CST/base/alíquota/valor de PIS e COFINS da linha; IND_OPER pela direção do CFOP (mesma regra do SPED Fiscal).
- M (por contribuição): receitas tributadas = linhas de saída CST 01/02; contribuição = Σ valor gravado; M210 agrupado por alíquota (COD_CONT "01" não cumulativo alíquota básica, "51" cumulativo).
  Crédito (só filial não cumulativa): linhas de entrada CST 50-56 → M100 tipo "101", M105 por NAT_BC_CRED da natureza.
  Crédito descontado = min(crédito, contribuição); sobra → 1100 (PIS) / 1500 (COFINS) com período = mês, VL_CRED_APU = crédito, VL_CRED_DESC_EFD = descontado, saldo = sobra; 1001 `0` nesse caso.
  M205 quando a contribuição a recolher > 0, código pela Global Constraints, valor = a recolher.
  M400/M410 (M800/M810): saídas CST 04/06/07/08/09 agrupadas por CST e por natureza da receita (`PisCofinsRevenueNatureCode`), valor = Σ OperationValue.
- Bloqueantes do spec + `MissingParticipantCodes`/`UnconvertibleUnits` de cada estabelecimento; mensagens pt-BR, ex.: `"Natureza {código} sem Natureza da receita (EFD Contribuições) para CST {cst}."`, `"Natureza {código} sem Base de cálculo do crédito (EFD Contribuições)."`, `"Linha {doc}/{item} sem CST de PIS ou COFINS."`, `"Filiais do CNPJ {raiz} sem matriz (/0001) cadastrada."`.
- Avisos: os da Conferência Fiscal.

- [ ] testes: `Generate_EmptyMonthIsValid`, `Generate_CumulativeOnlyHasNoCreditRegisters`, `Generate_NonCumulativeCreditCappedAndCarriedTo1100`, `Generate_RevenueWithSuspensionGoesToM410ByNature`, `Generate_TwoEstablishmentsTwoC010`, `Generate_Block9CountsMatchLines`, `Generate_IncTribFromBranchRegimes` (1/2/3), `Validate_MissingRevenueNatureBlocks`, `Validate_MissingCreditBaseBlocks`, `Controller_NonStandaloneReturnsBadRequest` → FAIL → implementar → PASS; suítes completas sem regressão.
- [ ] commit `feat(reports): geração do arquivo da EFD Contribuições`.

### Task 5: Menu + tela

- Backend: migration CommonDbContext `AddEfdContributionsMenu` — item `efdContributions` "EFD Contribuições", ParentKey `fiscal`, ordem 11, StandaloneOnly, `sap-icon://document-text`, ROLE_MENUS ADMIN (GUID fixo). Commit com `DB: AddEfdContributionsMenu`.
- Frontend: rota `efdContributions` (padrão `webapp/controller/spedFiscal` + view), campos: filial, mês/ano, finalidade (+ "Nº do recibo anterior" visível só na retificadora), Verificar (tabela Bloqueante/Aviso + totais PIS e COFINS: devido, crédito, a recolher), Gerar arquivo (mesma trava por chave do pedido verificado do SPED Fiscal). Campos de valor: nunca `type="Number"` com Float pt-BR.
- [ ] `npm run ts-typecheck` + `npm run lint` limpos; commit `feat(reports): tela da EFD Contribuições`.

### Task 6: Verificação ponta a ponta
- [ ] Aplicar as migrations novas só nos bancos locais CEAGUI (0001, 0002 e Common), conferindo antes migrations alheias.
- [ ] Stack em portas alternativas se as normais estiverem ocupadas por outra sessão; navegação pelo menu; preencher natureza da receita nas naturezas de venda; Verificar e Gerar 10/2026 e 09/2026; conferir 0000/0110/0140/C010/M200/M600/M410/9900 e totais = soma dos PIS/COFINS da Conferência.
- [ ] Derrubar só os PIDs iniciados.
