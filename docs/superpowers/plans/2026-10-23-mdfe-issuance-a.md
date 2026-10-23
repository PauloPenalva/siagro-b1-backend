# Emissão de MDF-e — entrega A (módulo TMS + documento + autorização/consulta + encerramento + DAMDFE + telas) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Emitir MDF-e modelo 58 v3.00 rodoviário no SiagroB1 STANDALONE a partir de CT-e autorizados, com CIOT/infANTT, autorização síncrona, consulta, encerramento individual e DAMDFE, tudo sob o novo módulo de menu "TMS".

**Architecture:**
- **Menu:** módulo superior novo `tms` ("TMS") na COMMON; CT-e sai de Vendas, cadastros de transporte saem de Cadastros, MDF-e entra nele.
- **Fiscal:** módulo `SiagroB1.Fiscal/Mdfe` espelhando `Fiscal/Cte`, com o pacote `Zeus.Net.MDFe` (mesma família/versão do `Zeus.Net.CTe`; compartilham `DFe.Classes`/`DFe.Utils`/`DFe.Wsdl`). Chave de acesso e limpeza de texto extraídas para `SiagroB1.Fiscal/Dfe` sem mudar o comportamento do CT-e.
- **Documento e configuração:** entidade `TransportManifest` com filhos; `BranchMdfeSettings`/`BranchMdfeSeries` (numeração independente do CT-e).
- **Serviços:** `Application/Services/Mdfe`, um por operação, molde `Services/Cte`.
- **Front:** telas `transportManifests` + aba MDF-e na Configuração da NF-e + "MDF-e" no detalhe do CT-e.
- **Entrega B** (`2026-10-23-mdfe-issuance-b.md`): cancelamento, inclusão de condutor, consulta de não encerrados, encerramento em lote, ZIP de XML/DAMDFE.

O encerramento individual fica na entrega A de propósito: o anti-objetivo "MDF-e autorizado que nunca é encerrado" não pode esperar a entrega B.

**Tech Stack:** .NET 10, EF Core/SQL Server, OData v4, `Zeus.Net.MDFe` 2026.9.24.1416, FastReport.OpenSource, xUnit; OpenUI5 1.141 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-23-mdfe-issuance-design.md` (binding). Prior art: `docs/superpowers/specs/2026-10-22-cte-issuance-design.md`, `docs/superpowers/plans/2026-10-22-cte-issuance-a.md`/`-b.md`.

**Worktrees** (já criados, branch `feature/mdfe-issuance`, base `feature/cte-issuance`):
- Backend: `C:\Projetos\SiagroB1\siagro-b1-backend-mdfe`
- Frontend: `C:\Projetos\SiagroB1\siagro-b1-frontend-mdfe` (`yarn install` próprio; nada de junction de node_modules)
- Copiar `SiagroB1.Reports/wwwroot` do principal para os testes de relatório.

## Global Constraints

- Somente STANDALONE (`ErpMode.IsStandalone`); SAPB1 recusa com `"O MDF-e só existe no modo STANDALONE."`.
- Emissão ligada por filial (`BranchMdfeSettings.Enabled`, "Emite MDF-e"); exige certificado (`BranchNfeSettings`) e RNTRC (`BranchTransportSettings`).
- **Modelo 58, versão 3.00**, só modal rodoviário, `tpEmit = 1`, `tpTransp` ausente. `tpAmb` vem de `BranchMdfeSettings.Environment`.
- **O CT-e não muda de comportamento:** extrações para `Fiscal/Dfe` só com `dotnet test SiagroB1.Fiscal.Tests` e `SiagroB1.Application.Tests` (filtro `Cte`) idênticos e verdes.
- **Zeus:** `Zeus.Net.MDFe` **2026.9.24.1416**, a mesma versão do `Zeus.Net.CTe` (os `DFe.*` compartilhados não podem divergir). Se o MDF-e tiver configuração estática global (como `CteZeusConfiguration.PinGlobalLayout`), fixá-la **no início de cada operação** (montar, assinar, enviar), e o CT-e continua fixando a dele.
- **Número e chave:** nascem no Confirmar e nunca mudam; `cMDF` aleatório de 8 dígitos ≠ número. Série de homologação: **231**.
- **CT-e elegível:** `CteStatus.Authorized`, mesma filial, sem vínculo **ativo** com outro MDF-e. Vínculo ativo = MDF-e em Draft, Confirmed, Processing, Rejected ou Authorized. Ele deixa de ser ativo quando o MDF-e vai para Voided (descarte), Cancelled ou Closed, ou quando o rascunho é excluído.
- **Peso bruto:** só unidade `01` KG ou `02` TON; obrigatório e > 0. Vem preenchido só se todos os CT-e estão em KG/TON (se misturados, soma em KG); M3 deixa vazio.
- **`tpCarga`:** padrão `BranchMdfeSettings.DefaultCargoType = "02"` (Granel líquido).
- **CIOT:** 12 dígitos + CPF/CNPJ do responsável; opcional. Com CIOT, `infPag` é obrigatório: `"Com CIOT informado, preencha as informações de pagamento do frete."`. Filial com `RequiresCiot` e sem CIOT → aviso no gravar, não bloqueio.
- **CEP de carregamento/descarregamento** (`infLotacao`): exigido quando o MDF-e tem um único CT-e: `"Carga lotação: informe o CEP de carregamento e de descarregamento."`.
- **Texto livre:** sempre por `DfeText.Clean` (sem espaço duplo, inicial ou final; o XSD rejeita).
- **Homologação:** o E2E verifica se a SEFAZ exige troca de `xNome` no MDF-e. Por padrão **não** trocar o nome do emitente (o MDF-e não tem destinatário). Se a SEFAZ rejeitar, aplicar `"MDF-E EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"` no assembler, com teste, e registrar no spec.
- **SEFAZ:** XML assinado gravado (`MdfeXmlKind.Signed`) **antes** do envio; retransmissão consulta pela chave primeiro; duplicidade (204/539/609) → consulta; `dhEmi` só é renovado depois de uma rejeição processada.
- **Encerramento:** data (hoje por padrão), UF e município (padrão = último município de descarga); evento 110112; 135 → `Closed`.
- **Menu:** módulo `tms`, título `"TMS"`, ícone `sap-icon://folder-blank`, `ParentKey = null`, `StandaloneOnly = true`. Permissões (`ROLE_MENUS.MenuItemKey`) seguem a **chave** do item, não o grupo: mover = `UpdateData` de `ParentKey`/`Order`, sem tocar em `ROLE_MENUS` (molde `MoveFinancialReportsToReportsMenu`).
- **Textos** pt-BR; identificadores em inglês.
- **Migrations:** aditivas; só CEAGUI_SIAGRO_DEV, LMCEREAIS_SIAGRO_DEV (App) e CEAGUI_SIAGRO_COMMON_DEV (Common), com `ASPNETCORE_ENVIRONMENT=Ceagui-Development` e `--connection` explícito; conferir `__EFMigrationsHistory` antes.
- **Git:** só em `feature/mdfe-issuance` (conferir antes de cada commit); `git add` de todo arquivo novo; `feat(invoice): ...` (MDF-e e menu TMS), `feat(reports): ...` (DAMDFE); trailer `DB: <Migration>` quando houver migration; nunca push.
- **UI5:** enum e data com formatter usam `targetType: 'any'`; `$expand` com `autoExpandSelect` exige `$select` explícito; toda tela chama `refreshStandaloneFlag()`; nada de `==`/`!=` em expression binding; filtro de enum como `$filter` cru.

## Decisões sobre ambiguidades da spec

- **Rejeitado segura os CT-e.** A spec diz que "ser rejeitado libera os CT-e". Mas um Rejected é retransmitido com o mesmo número; se perder os CT-e para outro MDF-e, dois manifestos teriam o mesmo CT-e (anti-objetivo). Ruling: os CT-e são liberados ao **descartar o número** ou excluir o rascunho.
- **CT-e com UF de início ou placa de tração diferentes não entram no mesmo MDF-e**: a spec manda herdar UF e veículo "dos CT-e", o que só é unívoco se forem iguais.
- **Cancelamento: prazo de 24 h** (regra nacional do MDF-e). A spec diz "dentro do prazo da SEFAZ" (entrega B).
- **Chaves de menu dos cadastros de transporte:** só `truckCombinations` aparece em migration; as chaves de Veículos e Motoristas são lidas de `MENU_ITEMS` na Tarefa 1 antes de escrever a migration.

## Review Focus

1. **CT-e já em outro MDF-e ativo**, escolhido por duas abas ao mesmo tempo: o segundo gravar recusa com `"O CT-e {n} já está no MDF-e {n}."`, checado dentro da transação e garantido pelo índice filtrado. Teste na Tarefa 7.
2. **Timeout na transmissão:** a consulta pela chave recupera a autorização sem reenviar; o XML assinado já está gravado. Teste na Tarefa 9.
3. **CT-e em M3:** peso vazio; a confirmação recusa até o usuário digitar KG/TON. Teste nas Tarefas 7 e 8.
4. **Viagem SP→SP com CT-e de dois municípios de descarga:** um `infMunDescarga` por município com as chaves agrupadas; o encerramento sugere o último. Teste nas Tarefas 3 e 10.
5. **Encerrar duas vezes / encerrar com timeout:** encerrar um Closed recusa; erro de comunicação mantém Authorized e manda tentar de novo; 631 (duplicidade de evento) marca Closed. Teste na Tarefa 10.
6. **Perfil que já via o CT-e** continua vendo depois da mudança para TMS (permissão pela chave). Teste na Tarefa 1.

---

### Task 1: Módulo "TMS" no menu (Common)

**Files:**
- Migration (Common): `SiagroB1.Migrations/CommonContext/<ts>_AddTmsMenuModule.cs`
- Test: `SiagroB1.Application.Tests/Mdfe/TmsMenuMigrationTests.cs`

**Interfaces:**
- Produces: `MENU_ITEMS` `tms` (módulo); `transportManifests` ("Manifestos (MDF-e)", `sap-icon://shipping-status`, `ParentKey "tms"`, `StandaloneOnly true`, rota que a Tarefa 13 cria); `transportDocuments`, os itens de Veículos, Motoristas e `truckCombinations` movidos para `tms`. Ordem dentro de TMS: 1 Conhecimentos de Transporte, 2 Manifestos (MDF-e), 3 Veículos, 4 Motoristas, 5 Conjuntos de Veículos. `ROLE_MENUS` ADMIN com GUID fixo para `tms` e `transportManifests`.

- [ ] **Step 1:** ler em `CEAGUI_SIAGRO_COMMON_DEV` (`sqlcmd -I`) as linhas de `MENU_ITEMS` com `ParentKey` em `registers`, `sales` e `NULL` (chaves de Veículos/Motoristas, `Order` atual e o maior `Order` dos módulos) e a definição do `ROLE_MENUS`. Confirmar que a permissão referencia `MenuItemKey` e que o frontend mostra o módulo quando o perfil tem algum filho (ver `Shell`/`MenuService`). Se o módulo-pai também precisa de `ROLE_MENUS` para os perfis não-ADMIN aparecerem, a migration insere `tms` para todo `RoleCode` que já tem `transportDocuments` ou um cadastro de transporte (`INSERT ... SELECT DISTINCT`).
- [ ] **Step 2: Teste que falha** (`TmsMenuMigrationTests`, lendo as operações da migration como os testes de migration existentes, ou texto da classe se não houver molde):
  - `Creates_tms_module_with_folder_blank_icon` (`ParentKey` null, `StandaloneOnly` true);
  - `Moves_cte_and_transport_master_data_to_tms_without_touching_role_menus` (só `UpdateData` em `MENU_ITEMS` para os 4 itens; nenhum `DeleteData` em `ROLE_MENUS`);
  - `Down_restores_previous_parents_and_orders` (`transportDocuments` → `sales`/16, `truckCombinations` → `registers`/4, e os valores lidos no Step 1).
- [ ] **Step 3:** FAIL → escrever a migration → PASS.
- [ ] **Step 4:** aplicar em `CEAGUI_SIAGRO_COMMON_DEV`; conferir no SQL que um perfil não-ADMIN com `transportDocuments` continua com o item.
- [ ] **Step 5:** commit `feat(invoice): módulo TMS no menu com CT-e, MDF-e e cadastros de transporte` + `DB: AddTmsMenuModule`.

### Task 2: Pacote Zeus MDF-e + XSDs + spike que fixa a API usada

**Files:**
- Modify: `SiagroB1.Fiscal/SiagroB1.Fiscal.csproj` (`<PackageReference Include="Zeus.Net.MDFe" Version="2026.9.24.1416" />`; XSDs com `CopyToOutputDirectory`)
- Create: `SiagroB1.Fiscal/Schemas/MDFe/*.xsd`, copiados de `C:\Projetos\EfisCloud\backend\schemas`. Só os do MDF-e 3.00:
  - documento: `mdfe_v3.00`, `MDFeModalRodoviario_v3.00`, `procMDFe`;
  - serviços: `enviMDFe`, `consSitMDFe`, `consStatServMDFe`, `consMDFeNaoEnc`;
  - eventos: `eventoMDFe`, `evEncMDFe`, `evCancMDFe`, `evIncCondutorMDFe`;
  - os tipos básicos e `xmldsig` de que esses dependem.
- Create: `SiagroB1.Fiscal/Mdfe/MdfeZeusConfiguration.cs`
- Test: `SiagroB1.Fiscal.Tests/Mdfe/ZeusMdfeApiSpikeTests.cs`

**Interfaces:**
- Produces:
  - `sealed record MdfeServiceSettings(NfeEnvironment Environment, string IssuerState, X509Certificate2 Certificate, string SchemasDirectory, int TimeoutMilliseconds = 60_000)`;
  - `static class MdfeZeusConfiguration { static string DefaultSchemasDirectory; static void PinGlobalLayout(); static <ConfigType> Create(MdfeServiceSettings s); }`, onde `<ConfigType>` é o tipo real de configuração que o spike achar.

- [ ] **Step 1:** `dotnet add SiagroB1.Fiscal package Zeus.Net.MDFe --version 2026.9.24.1416`. `dotnet build` deve sair sem conflito de versão de `DFe.*` (warning MSB3277 = falha).
- [ ] **Step 2:** inspecionar `~/.nuget/packages/zeus.net.mdfe/2026.9.24.1416` e anotar no topo do teste os namespaces/tipos usados:
  - a classe do MDF-e e o `infModal` rodoviário;
  - `infANTT`, `infCIOT` e `infPag`;
  - os serviços: recepção síncrona, consulta, eventos (encerramento, cancelamento, inclusão de condutor) e não encerrados;
  - a configuração global (p.ex. um `...Configuracao.Instancia`).
- [ ] **Step 3: Testes de spike** (passam só com a API real):
  - `Mdfe_types_used_by_builder_exist`: instancia o MDF-e rodoviário com `infANTT.infCIOT` e `infPag` e serializa sem exceção.
  - `Cte_and_mdfe_global_configs_coexist`: chamar o `PinGlobalLayout()` do CT-e, depois o do MDF-e, depois o do CT-e de novo, conferindo cada versão (CT-e `ve400`, MDF-e 3.00).
    - Se as duas configurações forem o mesmo singleton `DFe`, o teste documenta isso.
    - Nesse caso, fixar a versão no início de toda operação vira obrigatório nos dois módulos; registrar a conclusão no XML doc de `MdfeZeusConfiguration`.
  - `Mdfe_xsd_directory_is_copied`: `Schemas/MDFe/mdfe_v3.00.xsd` existe no output.
- [ ] **Step 4:** `dotnet test SiagroB1.Fiscal.Tests` inteiro verde (CT-e e NF-e intactos).
- [ ] **Step 5:** commit `feat(invoice): pacote Zeus MDF-e, XSDs 3.00 e spike da API`.

### Task 3: Chave, XML e assinatura do MDF-e (com extração de `Fiscal/Dfe`)

**Files:**
- Create: `SiagroB1.Fiscal/Dfe/DfeAccessKey.cs`, `Dfe/DfeText.cs`
- Modify: `SiagroB1.Fiscal/Cte/CteAccessKey.cs`, `Cte/CteText.cs` (viram fachadas que delegam; assinatura pública inalterada)
- Create: `SiagroB1.Fiscal/Mdfe/MdfeAccessKey.cs`, `Mdfe/MdfeIssueInput.cs`, `Mdfe/MdfeXmlBuilder.cs`, `Mdfe/MdfeSigner.cs`
- Test: `SiagroB1.Fiscal.Tests/Dfe/DfeAccessKeyTests.cs`, `Mdfe/MdfeAccessKeyTests.cs`, `Mdfe/MdfeXmlBuilderTests.cs`

**Interfaces:**
- Produces:
  - `static class DfeAccessKey { static string Build(string ufCode, DateTime issuedAt, string issuerCnpj, string model, int series, int number, int emissionType, string randomCode); static bool IsValid(string key, string? model = null); }`
  - `static class DfeText { static string Clean(string? v, int max = int.MaxValue); static string? CleanOrNull(string? v, int max = int.MaxValue); }`
  - `MdfeAccessKey.Build(...)` = `DfeAccessKey.Build(..., "58", ...)`; `MdfeAccessKey.IsValidCteKey(string)` confere modelo 57.
  - `sealed record MdfeIssueInput`, com os grupos:
    - **Identificação:** emitente (CNPJ, IE, nome, endereço, RNTRC); ambiente, série, número, `cMDF`, `dhEmi`, `UfStart`, `UfEnd`; `IReadOnlyList<string> RouteUfs`.
    - **Municípios:** `IReadOnlyList<MdfeLoadingCity(IbgeCode, Name)>`; `IReadOnlyList<MdfeUnloadingCity(IbgeCode, Name, IReadOnlyList<string> CteKeys)>`.
    - **Veículos e condutores:** `MdfeVehicle Traction(Plate, Renavam?, TareKg, CapacityKg, CapacityM3, BodyType, WheelType, Uf)`; `IReadOnlyList<MdfeVehicle> Trailers`; `IReadOnlyList<MdfeDriver(Cpf, Name)> Drivers`.
    - **infANTT:** `MdfeCiot?(Number, ResponsibleTaxId)`; `IReadOnlyList<MdfeContractor(TaxId, Name)>`; `MdfeTollVoucher?(SupplierCnpj, Number, Value)`; `MdfePayment?` com:
      - contratante;
      - componentes `(Type, Value)` e total;
      - `IndPag` 0/1 e parcelas `(Number, DueDate, Value)`;
      - `Pix`, ou `(BankCode, Agency)`.
    - **Seguro:** `(Responsible, ResponsibleTaxId, InsurerName, InsurerCnpj, PolicyNumber, IReadOnlyList<string> Endorsements)`.
    - **Produto predominante:** `(CargoType, Description, Ncm?, LoadingZip?, UnloadingZip?)`.
    - **Totais:** `(CteCount, CargoValue, WeightUnit "01"/"02", GrossWeight)`.
    - **Demais:** `IReadOnlyList<string> Seals`; `AdditionalInfo`; `InfRespTec?`.
  - `static class MdfeXmlBuilder { static <ZeusMdfe> Build(MdfeIssueInput input); }`
  - `static class MdfeSigner { static SignedMdfe SignAndValidate(<ZeusMdfe> mdfe, MdfeServiceSettings s); }` e `sealed record SignedMdfe(string Xml, string AccessKey)`. Erro de XSD lança `NfeValidationException`.

- [ ] **Step 1: Testes que falham.**
  - **`DfeAccessKeyTests`:**
    - `Build_matches_cte_key_for_model_57`: mesmo caso do `CteAccessKeyTests`, valor idêntico;
    - `IsValid_checks_dv_and_model`.
  - **`MdfeAccessKeyTests.Build_uses_model_58`:** UF "35", 2026-10-23, CNPJ do teste da COSTA, série 231, nº 1, cMDF "12345678". Posições 21–22 = `"58"`; DV módulo 11 conferido por função de referência do teste.
  - **`MdfeXmlBuilderTests`:**
    - `Build_sp_internal_trip_validates_against_xsd`. Caso: 2 CT-e, 1 município de carregamento, 2 de descarga, tração + 1 reboque, 1 condutor, KG. Esperado:
      - `SignAndValidate` não lança;
      - `<mod>58</mod>`, `<tpEmit>1</tpEmit>`, sem `<tpTransp>`, `<modal>1</modal>`;
      - dois `<infMunDescarga>` com as chaves certas;
      - `<qCTe>2</qCTe>` e `<cUnid>01</cUnid>`.
    - `Build_with_ciot_and_payment_emits_infANTT`: `<infCIOT><CIOT>123456789012</CIOT>` e `<infPag>` com `<indPag>`.
    - `Build_single_cte_emits_infLotacao_with_zips`.
    - `Build_cleans_double_spaces_in_free_text`: `"A  B "` vira `<infCpl>A B</infCpl>`.
    - `Build_route_ufs_emits_infPercurso_in_order`.
- [ ] **Step 2:** `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~Mdfe|FullyQualifiedName~Dfe"`. Esperado: FAIL (não compila).
- [ ] **Step 3:** implementar; `MdfeZeusConfiguration.PinGlobalLayout()` no início de `Build` e de `SignAndValidate`.
- [ ] **Step 4:** PASS, e `dotnet test SiagroB1.Fiscal.Tests` inteiro verde.
- [ ] **Step 5:** commit `feat(invoice): chave, XML e assinatura do MDF-e; chave e texto comuns em Fiscal/Dfe`.

### Task 4: Cliente SEFAZ do MDF-e

**Files:**
- Create: `SiagroB1.Fiscal/Mdfe/MdfeSefaz.cs` (interface, requests, `MdfeSefazResponseMapper`), `Mdfe/ZeusMdfeSefazClient.cs`
- Create: `SiagroB1.Application.Tests/Support/FakeMdfeSefazClient.cs`
- Test: `SiagroB1.Fiscal.Tests/Mdfe/MdfeSefazResponseMapperTests.cs`

**Interfaces:**
- `interface IMdfeSefazClient`. Os resultados reaproveitam `NfeSefazResult`, `NfeEventResult` e `NfeStatusCodes`; rede e timeout lançam `NfeCommunicationException`.
  - **Autorização e consulta:**
    - `AuthorizeAsync(SignedMdfe m, MdfeServiceSettings s, CancellationToken ct = default) → NfeSefazResult` (`MDFeRecepcaoSinc`);
    - `ConsultProtocolAsync(string accessKey, MdfeServiceSettings s, ...) → NfeSefazResult`;
    - `ServiceStatusAsync(MdfeServiceSettings s, ...) → NfeSefazResult`.
  - **Eventos:**
    - `CloseAsync(MdfeCloseRequest r, ...) → NfeEventResult`, com `MdfeCloseRequest(AccessKey, Protocol, IssuerCnpj, DateOnly ClosedOn, string UfCode, string CityIbge, DateTimeOffset EventAt)`;
    - `CancelAsync(MdfeCancelRequest r, ...) → NfeEventResult`, com `(AccessKey, Protocol, Justification, IssuerCnpj, EventAt)` (usado na entrega B);
    - `IncludeDriverAsync(MdfeIncludeDriverRequest r, ...) → NfeEventResult`, com `(AccessKey, IssuerCnpj, int Sequence, string Cpf, string Name, EventAt)` (entrega B).
  - **Não encerrados:** `ListNotClosedAsync(string issuerCnpj, MdfeServiceSettings s, ...) → IReadOnlyList<MdfeNotClosed(string AccessKey, string Protocol)>` (entrega B).
- Todos chamam `MdfeZeusConfiguration.PinGlobalLayout()` primeiro.

- [ ] **Step 1: Testes que falham**, com XMLs de retorno em string:
  - `Authorized_100_maps_protocol_and_mdfeProc`;
  - `Rejection_maps_code_and_reason`;
  - `Close_135_maps_event_protocol`;
  - `Cancel_135_maps_event_protocol`;
  - `NotClosed_list_maps_keys_and_protocols`;
  - `NotClosed_112_none_found_maps_empty`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar o mapper, o cliente Zeus e o fake. O fake roteiriza respostas, lança timeout e conta chamadas.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): cliente SEFAZ do MDF-e (autorização síncrona, consulta, eventos)`.

### Task 5: Configuração do MDF-e por filial + séries + reserva de número

**Files:**
- Create: `SiagroB1.Domain/Entities/BranchMdfeSettings.cs`, `BranchMdfeSeries.cs`, `Domain/Models/BranchMdfeSettingsModel.cs`
- Create: `SiagroB1.Application/Services/Mdfe/BranchMdfeSettingsService.cs`, `Services/Mdfe/MdfeNumberReservationService.cs`, `Services/Mdfe/MdfeServiceContextFactory.cs`
- Create: `SiagroB1.Web/Controllers/BranchMdfeSettingsController.cs` (molde `BranchCteSettingsController`: `BranchMdfeSettingsGet(BranchCode)`, `BranchMdfeSettingsSave`)
- Modify: `AppDbContext`, `ODataConfigurations`, `ServiceCollectionExtensions`
- Migration (App): `AddMdfeSettings`
- Test: `SiagroB1.Application.Tests/Mdfe/BranchMdfeSettingsServiceTests.cs`, `Mdfe/MdfeNumberReservationSqlTests.cs`

**Interfaces:**
- **`BranchMdfeSettings`** (`BRANCH_MDFE_SETTINGS`, PK `BranchCode`):
  - `Enabled`;
  - `NfeEnvironment Environment` (padrão Homologation);
  - `int DefaultSeries`;
  - `string DefaultCargoType` VARCHAR(2), padrão "02";
  - `bool RequiresCiot` (padrão false);
  - `UpdatedAt`, `UpdatedBy` e `SeriesList`.
- **`BranchMdfeSeries`** (`BRANCH_MDFE_SERIES`, PK `(BranchCode, Series)`): `Description` e `NextNumber`.
- **`BranchMdfeSettingsService.GetAsync/SaveAsync`:** as mesmas regras de série do `BranchCteSettingsService`. Habilitar sem certificado ou sem RNTRC → `"Para emitir MDF-e, cadastre o certificado (Configuração da NF-e) e o RNTRC (Filial → Transportadora)."`.
- **`MdfeNumberReservationService`:**
  - `ReserveAsync(branchCode, series) → Task<int>`, com o mesmo SQL `UPDLOCK, HOLDLOCK ... OUTPUT deleted.NextNumber` em `BRANCH_MDFE_SERIES`;
  - `AcquireEmissionLockAsync(Guid)`, com prefixo `"MDFE-"`.
- **`MdfeServiceContextFactory.OpenAsync(branchCode, environment) → MdfeServiceContext(MdfeServiceSettings)`:** certificado da `BranchNfeSettings`.

- [ ] **Step 1: Testes que falham:**
  - `Save_rejects_number_going_back_in_same_environment`;
  - `Save_allows_restart_when_environment_changes`;
  - `Enabling_requires_certificate_and_rntrc`;
  - `Defaults_are_bulk_liquid_and_ciot_not_required`;
  - `Rejected_in_sapb1`;
  - `Reservation_sql_locks_branch_mdfe_series`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** gerar `AddMdfeSettings` e aplicar em CEAGUI_SIAGRO_DEV e LMCEREAIS_SIAGRO_DEV (ambiente e `--connection` explícitos).
- [ ] **Step 6:** commit `feat(invoice): configuração e séries do MDF-e por filial` + `DB: AddMdfeSettings`.

### Task 6: Documento `TransportManifest` (entidades + migration)

**Files:**
- Create: `SiagroB1.Domain/Entities/TransportManifest.cs`, `TransportManifestCte.cs`, `TransportManifestLoadingCity.cs`, `TransportManifestRouteUf.cs`, `TransportManifestVehicle.cs`, `TransportManifestDriver.cs`, `TransportManifestContractor.cs`, `TransportManifestPayment.cs`, `TransportManifestPaymentComponent.cs`, `TransportManifestPaymentInstallment.cs`, `TransportManifestSeal.cs`, `TransportManifestXml.cs`, `TransportManifestEvent.cs`
- Create: `SiagroB1.Domain/Enums/MdfeStatus.cs`
- Modify: `AppDbContext`
- Migration (App): `AddTransportManifests`
- Test: `SiagroB1.Application.Tests/Mdfe/TransportManifestModelTests.cs`

**Interfaces:**
- **Enums:**
  - `MdfeStatus { Draft=0, Confirmed=1, Processing=2, Authorized=3, Rejected=4, Denied=5, Cancelled=6, Voided=7, Closed=8 }`;
  - `MdfeXmlKind { Authorized=0, Cancellation=1, Closing=2, DriverInclusion=3, Signed=4 }`;
  - `MdfeEventType { Closing=0, Cancellation=1, DriverInclusion=2 }`.
- **`TransportManifest`** (`TRANSPORT_MANIFESTS`, PK `Key` Guid):
  - **Identificação:** `BranchCode`, `MdfeStatus Status`, `Series`, `Number?`, `RandomCode?`, `AccessKey?` VARCHAR(44), `IssuedAt?`, `Environment`.
  - **Viagem:** `UfStart`, `UfEnd`, `TractionPlate`, `DriverCpf`/`DriverName` (condutor principal).
  - **Carga:** `CargoType`, `ProductDescription`, `Ncm?`, `LoadingZip?`, `UnloadingZip?`, `CteCount`, `CargoValue` DECIMAL(15,2), `WeightUnit` ("01"/"02"), `GrossWeight` DECIMAL(15,4).
  - **infANTT:** `CiotNumber?` VARCHAR(12), `CiotResponsibleTaxId?`, `TollSupplierCnpj?`, `TollVoucherNumber?`, `TollValue?`.
  - **Seguro:** os mesmos campos do `TransportDocument`.
  - **Observações:** `AdditionalInfo?`.
  - **SEFAZ:** `ProtocolNumber?`, `AuthorizedAt?`, `RejectionCode?`, `RejectionReason?`.
  - **Encerramento e cancelamento:** `ClosedAt?`, `ClosedOn?`, `ClosingUf?`, `ClosingCityIbge?`, `CancelledAt?`.
  - **Auditoria:** `CreatedAt/By`, `UpdatedAt/By`, e as coleções.
- **`TransportManifestCte`** (`TRANSPORT_MANIFEST_CTES`):
  - `ManifestKey`, `TransportDocumentKey` (FK Restrict), `CteAccessKey`, `UnloadingCityIbge`, `UnloadingCityName`, `Endorsements?`, `bool IsActive`;
  - **índice único filtrado** em `TransportDocumentKey` `WHERE [IsActive] = 1`;
  - `IsActive` é mantido pelos serviços (false em Voided/Cancelled/Closed);
  - memória: índice filtrado exige `QUOTED_IDENTIFIER`, e nada de `NOT IN` no filtro.
- Demais filhos: dados planos que o assembler copia para `MdfeIssueInput`.

- [ ] **Step 1: Testes que falham:**
  - `Filtered_unique_index_on_active_cte_link_is_configured`: lê o modelo EF e confere o índice em `TransportDocumentKey`, `IsUnique` e o filtro `[IsActive] = 1`;
  - `Status_enum_values_are_stable`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** gerar `AddTransportManifests`, revisar o SQL (só CREATE) e aplicar nos 2 bancos App.
- [ ] **Step 6:** commit `feat(invoice): documento do MDF-e` + `DB: AddTransportManifests`.

### Task 7: Criar a partir dos CT-e + gravar + validação

**Files:**
- Create: `SiagroB1.Application/Services/Mdfe/MdfeEligibleCtesQuery.cs`, `Services/Mdfe/TransportManifestsCreateFromCtesService.cs`, `Services/Mdfe/TransportManifestsSaveService.cs`, `Services/Mdfe/MdfeReadinessValidator.cs`, `Domain/Models/TransportManifestSaveModel.cs`
- Create: `SiagroB1.Web/Controllers/TransportManifestsController.cs`, com:
  - o entity set `TransportManifests`, só leitura via OData;
  - as ações `TransportManifestsCreateFromCtes`, `TransportManifestsSave` e `TransportManifestsDelete`;
  - a função `MdfeEligibleCtes(BranchCode)`.
- Test: `SiagroB1.Application.Tests/Mdfe/TransportManifestsCreateFromCtesServiceTests.cs`, `Mdfe/TransportManifestsSaveServiceTests.cs`, `Mdfe/MdfeReadinessValidatorTests.cs`

**Interfaces:**
- **`MdfeEligibleCtesQuery.ListAsync(branchCode) → IReadOnlyList<TransportDocument>`:** CT-e Authorized da mesma filial, sem `TransportManifestCte.IsActive`.
- **`CreateFromCtesAsync(string branchCode, IReadOnlyList<Guid> cteKeys, string user) → Guid`** cria um Draft com:
  - **UF de início e fim, e município de carregamento:** do início da prestação do 1º CT-e. UFs de início diferentes → recusa `"Os CT-e escolhidos começam em UFs diferentes."`.
  - **Descarga:** chaves agrupadas pelo município fim de cada CT-e.
  - **Veículos e condutor:** tração, reboques e motorista do CT-e. Placas de tração diferentes → recusa `"Os CT-e escolhidos são de veículos diferentes."`.
  - **Seguro:** da `BranchTransportSettings`, mais as averbações dos CT-e averbados.
  - **Produto:** predominante do 1º CT-e; `CargoType` da configuração da filial.
  - **Contratantes:** os tomadores distintos.
  - **Totais:** `CargoValue` = soma; peso conforme as Global Constraints.
- **`SaveAsync(Guid key, TransportManifestSaveModel m, string user) → IReadOnlyList<string> warnings`:** só Draft.
  - Campos editáveis: UFs de percurso, condutores adicionais, reboques, lacres, CEPs, peso e unidade, CIOT, contratantes, vale-pedágio, pagamento e observações.
  - Reconfere a elegibilidade dos CT-e **dentro da transação**; o índice filtrado dá a última palavra. Violação → `"O CT-e {n} já está no MDF-e {n}."`.
- **`MdfeReadinessValidator.Validate(TransportManifest m, BranchMdfeSettings s) → (IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)`:**
  - erros: as regras das Global Constraints (peso, unidade, CIOT com 12 dígitos, CIOT exige pagamento, lotação exige CEPs), ao menos 1 condutor e RNTRC;
  - aviso: `RequiresCiot` sem CIOT.

- [ ] **Step 1: Testes que falham:**
  - **Criação:**
    - `Create_groups_cte_keys_by_unloading_city`;
    - `Create_refuses_cte_already_in_active_manifest`;
    - `Create_refuses_different_traction_plates`;
    - `Create_prefills_weight_only_for_kg_or_ton`: M3 → `GrossWeight` null; KG + TON → soma em KG;
    - `Create_contractors_are_distinct_takers`.
  - **Gravação:** `Save_rechecks_cte_eligibility_inside_transaction`.
  - **Validação:**
    - `Validator_requires_kg_or_ton_weight`;
    - `Validator_ciot_requires_payment`;
    - `Validator_single_cte_requires_zips`;
    - `Validator_warns_when_branch_requires_ciot`.
  - **Modo:** `Rejected_in_sapb1`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): inclusão do MDF-e a partir dos CT-e autorizados`.

### Task 8: Confirmar / Reabrir / Descartar número

**Files:**
- Create: `SiagroB1.Application/Services/Mdfe/TransportManifestsConfirmService.cs`, `TransportManifestsReopenService.cs`, `TransportManifestsDiscardNumberService.cs`
- Modify: controller (ações `TransportManifestsConfirm`, `TransportManifestsReopen`, `TransportManifestsDiscardNumber`)
- Test: `SiagroB1.Application.Tests/Mdfe/TransportManifestsConfirmServiceTests.cs`

**Interfaces** (molde dos serviços `TransportDocumentsConfirm`/`Reopen`/`DiscardNumber`):
- **Confirmar:** valida com o `MdfeReadinessValidator` (erros → `BusinessException` com as mensagens unidas); reserva o número; gera `cMDF` ≠ número e a chave; grava `IssuedAt`.
- **Reabrir:** Confirmed → Draft, mantendo número e chave.
- **Descartar:** Confirmed/Rejected → Voided; `IsActive=false` nos vínculos (libera os CT-e).

- [ ] **Step 1: Testes que falham:**
  - `Confirm_reserves_number_and_builds_model_58_key`;
  - `Confirm_refuses_m3_without_weight`;
  - `Reopen_keeps_number_and_key`;
  - `Discard_voids_and_frees_ctes`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): confirmar, reabrir e descartar número do MDF-e`.

### Task 9: Transmitir + consultar (com recuperação)

**Files:**
- Create: `SiagroB1.Application/Services/Mdfe/MdfeIssueInputAssembler.cs`, `TransportManifestsTransmitService.cs`, `TransportManifestsConsultService.cs`
- Modify: controller (ações `TransportManifestsTransmit`, `TransportManifestsConsult`; função `TransportManifestsXml(Key)`)
- Test: `SiagroB1.Application.Tests/Mdfe/TransportManifestsTransmitServiceTests.cs`

**Interfaces:**
- **`MdfeIssueInputAssembler.Build(TransportManifest m, Branch b, BranchTransportSettings t) → MdfeIssueInput`.**
- **`TransmitAsync(Guid key, string user) → NfeSefazResult`:** só Confirmed, Processing ou Rejected.
  1. Pega a trava de emissão.
  2. Se já existe XML `Signed` e o status é Processing (falha de comunicação anterior), **consulta pela chave antes**. Autorizado → Authorized, sem reenviar.
  3. `dhEmi` é renovado **só** se o status atual é Rejected com rejeição processada. Se o mês difere do AAMM da chave → `"A chave foi gerada em {mm/aaaa} e a emissão cairia em outro mês. Descarte o número e confirme de novo."`.
  4. Assina, grava `Signed`, muda para Processing e faz `SaveChanges`; só então envia.
  5. Resposta 100 → Authorized e guarda o `mdfeProc`.
  6. Rejeição → Rejected com código e motivo.
  7. `NfeCommunicationException` ou 204/539/609 → `ConsultProtocolAsync`. Autorizado → Authorized; senão fica Processing com `"Sem resposta da SEFAZ: consulte depois."`.
- **`ConsultAsync(key)`:** atualiza a partir do protocolo, inclusive encerrado ou cancelado na SEFAZ → Closed/Cancelled.

- [ ] **Step 1: Testes que falham** (`FakeMdfeSefazClient`):
  - `Authorized_stores_protocol_and_xml`;
  - `Signed_xml_is_persisted_before_send`;
  - `Timeout_recovers_by_consult_without_resending`;
  - `Retransmit_after_timeout_consults_first`;
  - `Duplicate_539_recovers_by_consult`;
  - `Rejected_then_retransmit_keeps_number_and_key`;
  - `Issue_date_renewed_only_after_processed_rejection`;
  - `Different_month_is_refused_with_discard_hint`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): transmissão do MDF-e com recuperação por consulta`.

### Task 10: Encerramento individual

**Files:**
- Create: `SiagroB1.Application/Services/Mdfe/TransportManifestsCloseService.cs`
- Modify: controller (ação `TransportManifestsClose(Key, ClosedOn, UfCode, CityIbge)`)
- Test: `SiagroB1.Application.Tests/Mdfe/TransportManifestsCloseServiceTests.cs`

**Interfaces:**
- **`CloseAsync(Guid key, DateOnly? closedOn, string? ufCode, string? cityIbge, string user) → NfeEventResult`:** só Authorized.
  - Padrões: hoje, a UF fim e o último município de descarga.
  - Data futura → `"A data de encerramento não pode ser futura."`.
  - 135 → Closed; grava `TransportManifestEvent` e o XML `Closing`; `IsActive=false` nos vínculos.
  - 631 (duplicidade de evento) → consulta e marca Closed.
  - Erro de rede → mantém Authorized, com `"Sem resposta da SEFAZ: tente encerrar de novo."`.
- **`CloseCoreAsync`:** a mesma lógica, reutilizável pelo lote da entrega B.

- [ ] **Step 1: Testes que falham:**
  - `Close_defaults_to_last_unloading_city`;
  - `Close_135_marks_closed_and_frees_ctes`;
  - `Close_closed_manifest_is_refused`;
  - `Close_future_date_is_refused`;
  - `Close_timeout_keeps_authorized`;
  - `Close_duplicate_event_marks_closed`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): encerramento do MDF-e`.

### Task 11: DAMDFE (Relatórios)

**Files:**
- Create: `SiagroB1.Reports/Services/DamdfeXmlReader.cs`, `Services/DamdfeReportService.cs`, `Dtos/DamdfeDto.cs`, `Reports/Templates/Damdfe.frx`
- Create: `Controllers/DamdfeController.cs` (`GET /reports/Damdfe/{key}`) e `Controllers/InternalDamdfeController.cs` (molde `InternalDacteController`, para o ZIP da entrega B)
- Test: `SiagroB1.Application.Tests/Reports/DamdfeReportTests.cs`

**Interfaces:** `DamdfeReportService.RenderAsync(Guid key) → byte[]`, montado a partir do `mdfeProc` (não da entidade). Conteúdo:
- **Cabeçalho:** emitente, modal rodoviário, chave em CODE-128C, protocolo e QR Code (`infMDFeSupl/qrCodMDFe`).
- **Viagem e carga:** UF início/fim, quantidade de CT-e, valor, peso e unidade.
- **Veículos e condutores:** placas, RNTRC e condutores.
- **infANTT:** CIOT e vale-pedágio.
- **Documentos:** chaves dos CT-e por município de descarga; observações.
- **Tarjas:** "SEM VALOR FISCAL" (homologação), "CANCELADO" e "ENCERRADO".
- **Logo:** `DanfeLogo`.

- [ ] **Step 1: Testes que falham:**
  - `Damdfe_renders_pdf_from_authorized_xml`: começa com `%PDF` e tem a chave formatada no texto;
  - `Damdfe_closed_shows_closed_stamp`;
  - `Damdfe_for_unauthorized_is_refused`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar, com fontes métricas.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** renderizar localmente e conferir contra o leiaute do manual do DAMDFE.
- [ ] **Step 6:** commit `feat(reports): DAMDFE do MDF-e`.

### Task 12: EDM final + vínculo MDF-e no CT-e (backend)

**Files:**
- Modify: `TransportDocumentsController` + EDM, com a função `TransportDocumentsManifest(Key) → {ManifestKey, Number, Series, Status}?` (o MDF-e ativo ou o último do CT-e)
- Test: `SiagroB1.Application.Tests/Mdfe/MdfeEdmModelTests.cs`

- [ ] **Step 1: Testes de EDM:**
  - `TransportManifests` com as navegações;
  - as ações e funções das Tarefas 5 e 7–10, com os tipos de parâmetro;
  - a função do vínculo no CT-e.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** `dotnet test SiagroB1.Application.Tests` e `SiagroB1.Fiscal.Tests` completos e verdes.
- [ ] **Step 6:** commit `feat(invoice): EDM do MDF-e e vínculo no detalhe do CT-e`.

### Task 13: Frontend — telas do MDF-e, aba de configuração, vínculo no CT-e

**Files** (`siagro-b1-frontend-mdfe`):
- Create: `webapp/view/transportManifests/{Main,Form,Detail}.view.xml` (+ fragments `SelectCtesDialog`, `CloseDialog`), `webapp/controller/transportManifests/{Main,Form,Detail}.controller.ts`, `webapp/helpers/MdfeHelpers.ts`
- Modify:
  - `webapp/view/nfeSettings/*` + controller (aba "MDF-e");
  - `webapp/view/transportDocuments/Detail.view.xml` + controller (campo "MDF-e" com link);
  - `webapp/manifest.json`: rotas `transportManifests` (a mesma chave do item de menu da Tarefa 1), `transportManifestsNew`, `transportManifestsEdit` e `transportManifestsDetail`; padrões `transport-manifests`, `.../new`, `.../{id}/edit` e `.../{id}`;
  - `webapp/model/ServerRoutes.ts`.
- Test: `webapp/test/unit/helpers/MdfeHelpers.qunit.ts`

**Interfaces:** helpers testáveis:
- `mdfeStatusText`/`mdfeStatusState`: Closed = Success; Authorized não encerrado = Warning "Autorizado — não encerrado";
- `weightUnitText`: "01" KG, "02" TON;
- `cargoTypeText`: 01–11 da tabela do manual;
- `formatAccessKey`: reuso do `CteHelpers`;
- `ciotError(text)`: 12 dígitos;
- `buildTransportManifestSavePayload(form)`.

**O que muda:**
- **Lista:**
  - FilterBar: emissão, situação (multi), placa, motorista e o switch "Só não encerrados", que aplica o `$filter` cru `Status eq SiagroB1.Domain.Enums.MdfeStatus'Authorized'`;
  - colunas: número/série, emissão, situação (`ObjectStatus`; não encerrados em Warning), UF início→fim, placa, motorista, quantidade de CT-e, peso e chave;
  - ações: Incluir e Baixar DAMDFE (individual).
- **Incluir:** o `SelectCtesDialog` lista `MdfeEligibleCtes`, filtra por placa e data e aceita multisseleção. Chama `TransportManifestsCreateFromCtes` e navega ao formulário.
- **Formulário (página)**, seções:
  - CT-e (só leitura, agrupados por município de descarga);
  - percurso (UFs);
  - veículos, condutores e lacres;
  - carga: tipo, produto, CEPs, peso e unidade (obrigatórios);
  - CIOT, contratantes, vale-pedágio e pagamento;
  - seguro;
  - observações.
  - Os avisos do `SaveAsync` aparecem em `MessageBox.warning`.
- **Detalhe (ObjectPage)**, botões: Confirmar, Reabrir, Transmitir, Consultar, Encerrar (`CloseDialog` com data, UF e município pré-preenchidos), DAMDFE e Descartar número. Os botões da entrega B entram depois.
- **Aba MDF-e na Configuração da NF-e:** "Emite MDF-e", ambiente, série padrão, tipo de carga padrão, "Exige CIOT" e a tabela de séries.
- **Detalhe do CT-e:** "MDF-e: nº/série (situação)", com link para o detalhe.

- [ ] **Step 1:** QUnit dos helpers (RED por typecheck):
  - `ciotError`: 11 dígitos dá erro; 12 dá null;
  - `mdfeStatusText` para todos os valores;
  - `buildTransportManifestSavePayload` converte o peso para Number.
- [ ] **Step 2:** implementar helpers e telas.
- [ ] **Step 3:** `yarn ts-typecheck`, `yarn lint` e QUnit (`unitTests` no navegador) verdes.
- [ ] **Step 4:** commits por tela:
  - `feat(invoice): lista e inclusão do MDF-e`;
  - `feat(invoice): detalhe e encerramento do MDF-e`;
  - `feat(invoice): aba MDF-e na configuração e vínculo no CT-e`.

### Task 14: Verificação ponta a ponta (homologação SEFAZ)

> Transmissões à SEFAZ de homologação **só pela tela**, filial 02 COSTA, com o certificado já cadastrado. O usuário já autorizou os testes de homologação deste projeto; ainda assim, anunciar cada transmissão antes de clicar.

- [ ] **Step 1:** subir a stack `ceagui` dos worktrees `-mdfe` (memória "Subir a stack local"); migrations em dia nos 3 bancos locais.
- [ ] **Step 2:** pelo caminho do usuário, a partir da home:
  - o módulo **TMS** aparece no menu superior com Conhecimentos de Transporte, Manifestos (MDF-e), Veículos, Motoristas e Conjuntos;
  - Vendas e Cadastros não têm mais esses itens;
  - na aba MDF-e da filial 02: homologação, série **231**, tipo de carga 02.
- [ ] **Step 3:** incluir um MDF-e com os CT-e autorizados **1, 5 e 6** da série 231.
  - O diálogo também lista o CT-e 4 (autorizado) e **não** lista o 3 (cancelado).
  - Digitar o peso em KG (os CT-e estão em M3) e um CIOT de teste com pagamento; conferir os avisos.
- [ ] **Step 4:** confirmar, transmitir, consultar e abrir o DAMDFE. Se a SEFAZ rejeitar por nome em homologação, aplicar a regra das Global Constraints e registrar.
- [ ] **Step 5:** o diálogo de inclusão de um novo MDF-e não lista mais o CT-e 1; o detalhe do CT-e 1 mostra o MDF-e.
- [ ] **Step 6:** encerrar pela tela, com o município sugerido. Conferir Closed, a tarja "ENCERRADO" e o CT-e 1 elegível de novo.
- [ ] **Step 7:** conferir o XML autorizado: `mod 58`, `tpEmit 1`, `infMunDescarga` com as chaves, `infCIOT`, `infPag` e `cUnid 01`.
- [ ] **Step 8:** derrubar a stack pelos PIDs; superpowers:verification-before-completion; revisão final do branch.
