# Emissão de CT-e — entrega A (documento + ciclo SEFAZ + DACTE) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Emitir CT-e modelo 57 v4.00 de saída no SiagroB1 STANDALONE: o documento (com
importação do XML da NF-e), confirmação com número travado, transmissão com recuperação,
consulta, cancelamento, CC-e estruturada, inutilização e DACTE.

**Architecture:**
- **Fiscal:** módulo `SiagroB1.Fiscal/Cte` espelhando `Fiscal/Nfe`, com o pacote `Zeus.Net.CTe`.
- **Documento e configuração:** entidade nova `TransportDocument` com filhos; configuração e séries
  próprias do CT-e (`BranchCteSettings`/`BranchCteSeries`), sem tocar nas tabelas da NF-e.
- **Serviços:** em `Application/Services/Cte`, um por operação, como o resto do app.
- **Front:** telas OpenUI5 novas e aba CT-e na Configuração da NF-e.
- **Entrega B** (plano separado, depois): averbação, e-mail, título a receber e transmissão em lote.

**Tech Stack:** .NET 10, EF Core/SQL Server, OData v4, `Zeus.Net.CTe` 2026.9.24.1416, FastReport.OpenSource, xUnit; OpenUI5 1.141 + TypeScript.

**Spec:** `docs/superpowers/specs/2026-10-22-cte-issuance-design.md` (inclui a seção "Decisões tomadas na revisão do desenho").

**Worktrees:**
- **Backend:** `C:\Projetos\SiagroB1\siagro-b1-backend-cte-issuance` (`feature/cte-issuance`, base
  `feature/transport-master-data`), já criado.
- **Frontend:** criar `C:\Projetos\SiagroB1\siagro-b1-frontend-cte-issuance` a partir de
  `feature/transport-master-data` do frontend, com `yarn install` próprio (sem junction de
  node_modules).
- Copiar `SiagroB1.Reports/wwwroot` do principal para os testes de relatório.

## Global Constraints

- Somente STANDALONE (`ErpMode.IsStandalone`); SAPB1 recusa com `"O CT-e só existe no modo STANDALONE."`.
- Emissão ligada por filial (`BranchCteSettings.Enabled`, "Emite CT-e").
- **A NF-e não muda:** nenhum arquivo em `Fiscal/Nfe` ou `Services/Nfe` tem comportamento
  alterado. Extrair código comum só se os testes da NF-e continuarem idênticos e verdes.
- **Modelo 57, versão 4.00:** `tpAmb` vem de `BranchCteSettings.Environment`, separado da NF-e.
  O certificado é o de `BranchNfeSettings` da filial.
- **Unidade padrão da quantidade:** `BranchCteSettings.DefaultQuantityUnit`, padrão **"00" (M3)**.
  Códigos: 00 M3, 01 KG, 02 TON, 03 UNIDADE, 04 LITROS, 05 MMBTU.
- **Tomador:** 0 Remetente, 1 Expedidor, 2 Recebedor, 3 Destinatário, 4 Outros (toma4).
- **`indIEToma`:** 1 contribuinte (tem IE), 2 isento ("ISENTO"), 9 não contribuinte. Vem do
  cadastro do tomador, nunca fixo.
- **Rota sugerida:** início = expedidor, senão remetente; fim = recebedor, senão destinatário.
  Editável.
- **Número e chave:** nascem no Confirmar e nunca mudam. `cCT` aleatório de 8 dígitos, gerado uma
  vez e diferente do número.
- **Cancelamento:** justificativa de 15 a 255 caracteres; prazo de **168 horas** da autorização.
- **ICMS e IBS/CBS:** sempre calculados pela natureza (`USAGES`), nunca digitados.
- **Textos:** mensagens pt-BR; identificadores em inglês.
- **Migrations:** aditivas, em todas as empresas da COMMON, com trailer `DB:`.
- **Git:** `git add` de todo arquivo novo; commits `feat(invoice): ...` (escopo de documento
  fiscal); nunca push.

## Review Focus

1. **Transmitir de novo um CT-e rejeitado** usa o mesmo número e a mesma chave (o `cCT` não muda).
   Teste na Tarefa 7.
2. **Timeout na transmissão:** a consulta pela chave recupera o protocolo e marca Autorizado, sem
   novo envio. Teste na Tarefa 7.
3. **Transmitir num mês diferente do da chave:** recusa com mensagem e oferece "Descartar número".
   Teste na Tarefa 7.
4. **Duas confirmações simultâneas na mesma série** recebem números diferentes (trava SQL, como a
   NF-e). Teste na Tarefa 3, no nível do SQL da reserva.
5. **NF-e importada com remetente ou destinatário sem cadastro:** a importação diz quais
   CNPJ/CPF faltam e não cria o documento pela metade. Teste na Tarefa 5.

---

### Task 1: Pacote Zeus CT-e + chave de acesso + XML/assinatura no Fiscal

**Files:**
- Modify: `SiagroB1.Fiscal/SiagroB1.Fiscal.csproj` (`<PackageReference Include="Zeus.Net.CTe" Version="2026.9.24.1416" />`)
- Create: `SiagroB1.Fiscal/Cte/CteAccessKey.cs`, `Cte/CteIssueInput.cs`, `Cte/CteXmlBuilder.cs`, `Cte/CteSigner.cs`, `Cte/CteZeusConfiguration.cs`
- Create: `SiagroB1.Fiscal/Schemas/CTe/*.xsd` (XSDs oficiais do CT-e 4.00 e dos eventos, copiados do pacote Zeus se vierem nele; senão do Portal CT-e)
- Test: `SiagroB1.Fiscal.Tests/Cte/CteAccessKeyTests.cs`, `Cte/CteXmlBuilderTests.cs`

**Interfaces:**
- Produces:
  - `static class CteAccessKey { static string Build(string ufCode, DateTime issuedAt, string issuerCnpj, int series, int number, int emissionType, string randomCode); static bool IsValidNfeKey(string key); }`
    — 44 dígitos, modelo **57**, DV módulo 11. `IsValidNfeKey` confere o DV e o modelo **55**.
  - `sealed record CteIssueInput(...)` — tudo o que o XML precisa, sem EF:
    - emitente (com RNTRC);
    - os 5 papéis com endereço e IBGE, tomador e `IndIEToma`;
    - rota;
    - CFOP e natOp;
    - série, número, cCT, dhEmi e ambiente;
    - componentes;
    - ICMS (CST/vBC/pICMS/vICMS/pRedBC) e IBS/CBS;
    - carga (proPred, vCarga, quantidades);
    - chaves NF-e;
    - `ObsCont` e xObs;
    - autXML opcional;
    - `infRespTec` opcional.
  - `static class CteXmlBuilder { static CTe Build(CteIssueInput input); }` (tipo `CTe` do Zeus).
  - `static class CteSigner { static SignedCte SignAndValidate(CTe cte, CteServiceSettings settings); }`
    - `SignedCte(string Xml, string AccessKey)`;
    - lança `NfeValidationException` com as mensagens do XSD (a mesma exceção da NF-e, para reaproveitar tratamento).
  - `sealed record CteServiceSettings(NfeEnvironment Environment, string IssuerState, X509Certificate2 Certificate, string SchemasDirectory, int TimeoutMilliseconds = 60_000)`.
  - `static class CteZeusConfiguration { static ConfiguracaoServico Create(CteServiceSettings s); }`
    — o tipo de configuração do Zeus **CTe**, com `ModeloDocumento` 57 e `VersaoLayout` 4.00.

- [ ] **Step 1: Testes que falham**
  - **`CteAccessKeyTests`:**
    - `Build_composes_44_digits_with_model_57_and_valid_dv`: para UF "35", 2026-10-22, CNPJ
      "09109848000128", série 2, nº 82394, tpEmis 1, cCT "12345678", a chave começa com
      `"352610091098480001285700200008239411234567"` (43 dígitos) e o 44º é o DV módulo 11
      calculado no teste por uma função de referência independente.
    - `IsValidNfeKey_rejects_wrong_dv_and_model`: com uma chave NF-e válida de
      `SiagroB1.Fiscal.Tests/Support` → true; trocar um dígito → false; trocar o modelo para 57 →
      false.
  - **`CteXmlBuilderTests`:**
    - `Build_internal_sp_freight_validates_against_xsd`: input do caso COSTA (tomador
      Destinatário, CST 00, 12%, 1 componente, 1 NF-e, unidade "00", rota SP→SP, CFOP 5352). Usar o
      certificado de teste que `Fiscal.Tests/Support` já tem. Esperado: `SignAndValidate` não lança;
      o XML tem `<mod>57</mod>`, `<toma3><toma>3</toma></toma3>`, `<CST>00</CST>`, `<pICMS>12.00</pICMS>`
      e a `<chave>` da NF-e.
    - `Build_taker_other_uses_toma4`: tomador 4 → `<toma4>` com CNPJ e endereço do tomador.
    - `Build_receiver_present_emits_receb_and_end_municipality`: recebedor ≠ destinatário → `<receb>`
      presente e `cMunFim` = o fornecido na rota.
    - `Build_indIEToma_follows_input`: 9 → `<indIEToma>9</indIEToma>`.
- [ ] **Step 2:** `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~Cte"`. Esperado:
  FAIL (não compila).
- [ ] **Step 3: Implementar.**
  - Ler a API do pacote no `~/.nuget/packages/zeus.net.cte/2026.9.24.1416` (namespaces `CTe.Classes`,
    `CTe.Servicos`, `CTe.Utils`).
  - Copiar os XSDs para `Schemas/CTe` com `CopyToOutputDirectory`, como os da NF-e.
- [ ] **Step 4:** PASS, e `dotnet test SiagroB1.Fiscal.Tests` inteiro verde (a NF-e intacta).
- [ ] **Step 5:** commit `feat(invoice): XML, assinatura e chave do CT-e no Fiscal (Zeus CT-e)`.

### Task 2: Cliente SEFAZ do CT-e no Fiscal

**Files:**
- Create: `SiagroB1.Fiscal/Cte/CteSefaz.cs` (interface + records), `Cte/ZeusCteSefazClient.cs`
- Create: `SiagroB1.Application.Tests/Support/FakeCteSefazClient.cs`
- Test: `SiagroB1.Fiscal.Tests/Cte/CteSefazResponseMapperTests.cs`

**Interfaces:**
- Produces:
  - `interface ICteSefazClient`:
    - `Task<NfeSefazResult> AuthorizeAsync(SignedCte cte, CteServiceSettings s, CancellationToken ct = default)`
      — envio **síncrono** (`CTeRecepcaoSincV4`);
    - `Task<NfeSefazResult> ConsultProtocolAsync(string accessKey, CteServiceSettings s, CancellationToken ct = default)`;
    - `Task<NfeSefazResult> ServiceStatusAsync(CteServiceSettings s, CancellationToken ct = default)`;
    - `Task<NfeEventResult> CancelAsync(CteCancelRequest r, CteServiceSettings s, CancellationToken ct = default)`;
    - `Task<NfeEventResult> SendCorrectionAsync(CteCorrectionRequest r, CteServiceSettings s, CancellationToken ct = default)`;
    - `Task<NfeVoidNumberResult> VoidNumberAsync(CteVoidNumberRequest r, CteServiceSettings s, CancellationToken ct = default)`.
  - Reaproveita os records de resultado da NF-e (`NfeSefazResult`, `NfeEventResult`,
    `NfeVoidNumberResult`) e `NfeStatusCodes`.
    - Autorizado = 100.
    - Cancelamento homologado = 135.
    - Duplicidade = 204/539 (incluir no `NfeStatusCodes` só se ainda não estiverem; se mudar a
      classe, os testes da NF-e precisam continuar verdes).
  - Requests:
    - `CteCancelRequest(string AccessKey, string Protocol, string Justification, string IssuerCnpj, DateTimeOffset EventAt)`;
    - `CteCorrectionRequest(string AccessKey, string IssuerCnpj, int Sequence, IReadOnlyList<CteCorrectionItem> Items, DateTimeOffset EventAt)`;
    - `CteCorrectionItem(string Group, string Field, string Value, int? GroupNumber)`;
    - `CteVoidNumberRequest(string IssuerCnpj, string StateCode, int Year, int Series, int From, int To, string Justification)`.
  - `CommunicationException` reaproveitada: `NfeCommunicationException` para timeout/rede.

- [ ] **Step 1: Testes que falham** — `CteSefazResponseMapperTests` com XMLs de retorno de exemplo
  em string:
  - `Authorized_100_maps_protocol_and_xml`;
  - `Rejection_maps_code_and_reason`;
  - `Cancel_135_maps_event_protocol`;
  - `Correction_135_maps_sequence`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar o mapper e o `ZeusCteSefazClient`. O cliente real não é testado
  contra a SEFAZ aqui; o E2E de homologação (Tarefa 12) cobre. O `FakeCteSefazClient` roteiriza
  respostas e exceção de timeout para os testes de Application.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): cliente SEFAZ do CT-e (autorização síncrona, consulta, eventos, inutilização)`.

### Task 3: Configuração do CT-e por filial + séries + reserva de número

**Files:**
- Create: `SiagroB1.Domain/Entities/BranchCteSettings.cs`, `BranchCteSeries.cs`; `Domain/Models/BranchCteSettingsModel.cs`
- Create: `SiagroB1.Application/Services/Cte/BranchCteSettingsService.cs`, `Services/Cte/CteNumberReservationService.cs`
- Create: `SiagroB1.Web/Controllers/BranchCteSettingsController.cs` (função `BranchCteSettingsGet(BranchCode)` + ação `BranchCteSettingsSave`, molde `BranchTransportSettingsController`)
- Modify: `AppDbContext`, `ODataConfigurations`, `ServiceCollectionExtensions`
- Migration (App): `AddCteSettings`
- Test: `SiagroB1.Application.Tests/Cte/BranchCteSettingsServiceTests.cs`, `Cte/CteNumberReservationSqlTests.cs`

**Interfaces:**
- Produces:
  - **`BranchCteSettings`** (`BRANCH_CTE_SETTINGS`, PK `BranchCode`):
    - `bool Enabled`;
    - `NfeEnvironment Environment` (padrão Homologation);
    - `int DefaultSeries`;
    - `string DefaultQuantityUnit` (VARCHAR(2), padrão "00");
    - `UpdatedAt` e `UpdatedBy`;
    - `ICollection<BranchCteSeries> SeriesList`.
  - **`BranchCteSeries`** (`BRANCH_CTE_SERIES`, PK `(BranchCode, Series)`): `Description` e `NextNumber`.
  - **`BranchCteSettingsService`:**
    - `GetAsync(branchCode)`;
    - `SaveAsync(branchCode, model, user)`.
    - Mesmas regras de série do `BranchNfeSettingsService`: série 0–999, número 1–999.999.999, não
      volta número no mesmo ambiente, não exclui série usada.
    - `Enabled` exige certificado na `BranchNfeSettings` da filial e RNTRC na `BranchTransportSettings`.
      Mensagem: `"Para emitir CT-e, cadastre o certificado (Configuração da NF-e) e o RNTRC (Filial → Transportadora)."`.
  - **`CteNumberReservationService`:**
    - `virtual Task<int> ReserveAsync(string branchCode, int series)`, com o mesmo SQL
      `UPDATE ... WITH (UPDLOCK, HOLDLOCK) ... OUTPUT deleted.NextNumber` em `BRANCH_CTE_SERIES`;
    - `AcquireEmissionLockAsync(Guid documentKey)`: copie o padrão do
      `NfeNumberReservationService`, com o recurso do applock prefixado `"CTE-"`.

- [ ] **Step 1: Testes que falham.**
  - `Save_rejects_number_going_back_in_same_environment`;
  - `Save_allows_restart_when_environment_changes`;
  - `Enabling_requires_certificate_and_rntrc`;
  - `Default_quantity_unit_is_m3`;
  - `Rejected_in_sapb1`.
  - **`CteNumberReservationSqlTests`:** confere que o SQL usa `BRANCH_CTE_SERIES` com
    `UPDLOCK, HOLDLOCK` e `OUTPUT deleted.NextNumber`. Teste de texto, como o equivalente da NF-e,
    se existir; senão, teste de integração marcado `[Trait("Category","Sql")]`, pulado sem banco.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar entidades, serviço, controller, EDM e DI.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** gerar `AddCteSettings` e aplicar em CEAGUI_SIAGRO_DEV e LMCEREAIS_SIAGRO_DEV com
  `ASPNETCORE_ENVIRONMENT=Ceagui-Development` e `--connection` explícito. Antes, conferir
  `__EFMigrationsHistory`.
- [ ] **Step 6:** commit `feat(invoice): configuração e séries do CT-e por filial` + `DB: AddCteSettings`.

### Task 4: Documento `TransportDocument` (entidades + migration + cálculo)

**Files:**
- Create: `SiagroB1.Domain/Entities/TransportDocument.cs`, `TransportDocumentParty.cs`, `TransportDocumentNfeKey.cs`, `TransportDocumentQuantity.cs`, `TransportDocumentComponent.cs`, `TransportDocumentXml.cs`, `TransportDocumentCorrection.cs`, `CteNumberToVoid.cs`
- Create: `SiagroB1.Domain/Enums/CteStatus.cs`, `CtePartyRole.cs`
- Create: `SiagroB1.Application/Services/Cte/TransportDocumentCalculator.cs`
- Modify: `AppDbContext`
- Migration (App): `AddTransportDocuments`
- Test: `SiagroB1.Application.Tests/Cte/TransportDocumentCalculatorTests.cs`

**Interfaces:**
- Produces:
  - `enum CteStatus { Draft = 0, Confirmed = 1, Processing = 2, Authorized = 3, Rejected = 4, Denied = 5, Cancelled = 6, Voided = 7 }`.
  - `enum CtePartyRole { Sender = 0, Shipper = 1, Receiver = 2, Recipient = 3, OtherTaker = 4 }`.
    Shipper = expedidor; Receiver = recebedor; Recipient = destinatário.
  - **`TransportDocument`** (`TRANSPORT_DOCUMENTS`, PK `Guid Key`):
    - identificação: `BranchCode`, `Series`, `int? Number`, `string? AccessKey` (44), `string? RandomCode` (8);
    - `DateTime? IssuedAt`, `CteStatus Status`, `NfeEnvironment? Environment`;
    - natureza: `UsageCode`, `Cfop`, `NatureText`;
    - `int TakerRole` (0–4);
    - rota: `StartMunicipalityCode`/`StartState`, `EndMunicipalityCode`/`EndState`;
    - carga: `PredominantProduct` (60), `CargoValue`;
    - prestação: `ServiceTotal`, `AmountToReceive`;
    - ICMS: `IcmsCst`, `IcmsBase`, `IcmsRate`, `IcmsReduction`, `IcmsValue`, e o snapshot de IBS/CBS
      no mesmo formato do `TaxSnapshot` das notas;
    - modal: `Rntrc`, `TruckCombinationCode`, `TractionPlate`, `TrailerPlates` (texto "PLACA1,PLACA2"),
      `VehicleSnapshotJson` (renavam/tara/capacidade/rodado/carroceria/UF de cada veículo),
      `DriverCpf`, `DriverName`;
    - seguro: `InsuranceResponsible`, `InsurerTaxId`, `InsurerName`, `PolicyNumber`;
    - observações: `Tracked` (padrão true), `OwnTransport` (padrão true), `Observation` (2000);
    - dimensões: `CostCenterCode`, `AccountingItemCode`, `DistributionRuleCode`;
    - SEFAZ: `Protocol`, `AuthorizedAt`, `StatusCode`, `StatusReason`, `CancellationProtocol`,
      `CancelledAt`, `CancellationReason`;
    - `CreatedAt/By`, `UpdatedAt/By`.
  - **Filhos:**
    - `TransportDocumentParty`: `Role`, `CardCode`, nome, CNPJ/CPF, IE, `IndIE`, endereço completo,
      `MunicipalityCode`, UF, CEP, fone, e-mail. Snapshot.
    - `TransportDocumentNfeKey`: `AccessKey`, `Order`.
    - `TransportDocumentQuantity`: `UnitCode` (2), `MeasureType` (20, ex. "VOLUME"), `Quantity` (decimal 15,4).
    - `TransportDocumentComponent`: `Name` (15), `Value`.
    - `TransportDocumentXml`: `Kind` (Authorized/Cancellation/Correction), `Xml`.
    - `TransportDocumentCorrection`: `Sequence`, `ItemsJson`, `Protocol`, `SentAt`.
  - **`CteNumberToVoid`** (`CTE_NUMBERS_TO_VOID`): `BranchCode`, `Series`, `Number`, `Reason`,
    `CreatedAt`, `VoidedAt?`, `VoidProtocol?`.
  - **`TransportDocumentCalculator`:** `static void Recalculate(TransportDocument doc)`.
    - `ServiceTotal` = soma dos componentes;
    - `AmountToReceive` = `ServiceTotal`;
    - `IcmsBase` = `ServiceTotal × (1 − IcmsReduction/100)`;
    - `IcmsValue` = arredonda 2 casas (base × alíquota / 100).

- [ ] **Step 1: Testes que falham.**
  - `Totals_sum_components`: 650 + 50 → 700.
  - `Icms_12_percent_on_650_is_78`: o caso real dos dados — base 650, valor 78.
  - `Reduction_applies_to_base`.
  - `New_document_is_draft_tracked_and_own_transport`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar. Configuração EF: filhos em cascata a partir do documento; índice
  único `(BranchCode, Series, Number)` filtrado `Number IS NOT NULL`. O índice filtrado exige
  `QUOTED_IDENTIFIER`; ver memória.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** migration `AddTransportDocuments`, aplicada como na Tarefa 3.
- [ ] **Step 6:** commit `feat(invoice): documento de CT-e (conhecimento de transporte)` + `DB: AddTransportDocuments`.

### Task 5: Inclusão/edição do documento + importação do XML da NF-e

**Files:**
- Create: `SiagroB1.Application/Services/Cte/TransportDocumentsSaveService.cs`, `Services/Cte/TransportDocumentDefaults.cs`, `Services/Cte/NfeXmlForCteReader.cs`, `Services/Cte/TransportDocumentsImportNfeService.cs`, `Services/Cte/TransportDocumentsTaxApplyService.cs`
- Create: `SiagroB1.Web/Controllers/TransportDocumentsController.cs` (entity set `TransportDocuments` com `$expand` dos filhos; ações `TransportDocumentsSave`, `TransportDocumentsImportNfe`, `TransportDocumentsDelete`)
- Test: `SiagroB1.Application.Tests/Cte/TransportDocumentsSaveServiceTests.cs`, `Cte/TransportDocumentsImportNfeServiceTests.cs`, `Cte/TransportDocumentsTaxApplyServiceTests.cs`

**Interfaces:**
- Consumes: Tarefas 3 e 4; `TaxLineCalculator.CalculateAsync`; `BranchTransportSettings`;
  `TruckCombination`/`Truck`/`TruckDriver`; `BusinessPartner` + `Address.MunicipalityCode`;
  `IbsCbsRatesService`.
- Produces:
  - **`TransportDocumentInput`** (record):
    - `Guid? Key`, `BranchCode`, `int Series`, `UsageCode`, `int TakerRole`;
    - `IReadOnlyList<PartyInput>` (`Role`, `CardCode`, `AddressName?`);
    - rota (códigos IBGE, opcionais: vazio = sugerir);
    - `IReadOnlyList<string> NfeKeys`;
    - carga: `PredominantProduct`, `CargoValue`, `IReadOnlyList<QuantityInput>`;
    - `IReadOnlyList<ComponentInput>`;
    - modal: `TruckCombinationCode?`, `TractionPlate?`, `DriverCpf?`;
    - seguro: quatro campos opcionais (vazio = padrão da filial);
    - observações: `Tracked`, `OwnTransport`, `Observation`;
    - dimensões: três campos.
  - **`TransportDocumentsSaveService.SaveAsync(TransportDocumentInput input, string user) → Guid`**:
    - grava só em Draft, Confirmed (volta a Draft? **não**: editar Confirmed exige Reabrir antes)
      ou Rejected;
    - em Rejected, recusa mudar filial, série e número.
  - **`TransportDocumentDefaults`:**
    - `SuggestRoute(parties)`: regra da Global Constraints;
    - `IndIE(partner)`: 1, 2 ou 9;
    - seguro e unidade padrão da filial.
  - **`TransportDocumentsTaxApplyService.ApplyAsync(TransportDocument doc)`**: espelho do ramo CT-e
    de `PurchaseInvoicesTaxApplyService` (linhas ~146–161):
    - `inState` = início == fim;
    - origem = início, destino = fim;
    - `cfopInState` = início == UF da filial;
    - **`GoodsOrigin` = 0** (serviço).
    - Grava CST/alíquota/redução/CFOP/natureza e IBS/CBS, depois `TransportDocumentCalculator.Recalculate`.
  - **`NfeXmlForCteReader.Read(string xml) → NfeForCte`**:
    - `AccessKey`;
    - emitente e destinatário (CNPJ/CPF, nome, IBGE);
    - `vNF`;
    - produto predominante = `xProd` do item de maior valor;
    - peso bruto (`transp/vol/pesoB`).
    - Recusa XML que não é `nfeProc`/`NFe` modelo 55.
  - **`TransportDocumentsImportNfeService.ImportAsync(Guid? key, string branchCode, IReadOnlyList<string> xmls, string user) → Guid`**:
    - cria ou complementa o rascunho com remetente = emitente da NF-e, destinatário = destinatário
      da NF-e, chaves, soma de `vNF` em `CargoValue`, produto predominante e uma quantidade;
    - a quantidade usa a unidade **padrão da filial**, com o valor do peso só se a unidade padrão
      for KG ("01"); senão a quantidade fica 0 para o usuário preencher (a NF-e não traz M3);
    - recusa NF-e cujo emitente ou destinatário não tenha cadastro.

**Regras do `SaveAsync`** (mensagens exatas):
- **Chaves:**
  - chave NF-e com DV inválido → `"Chave de NF-e inválida: {chave}."`;
  - repetida no documento → `"A chave {chave} aparece mais de uma vez."`;
  - **sem** chave → `"Informe ao menos uma NF-e transportada."`.
- **Participantes:**
  - sem remetente ou destinatário → `"Informe remetente e destinatário."`;
  - tomador 4 sem participante `OtherTaker` → `"Informe o tomador do serviço."`;
  - participante sem IBGE no endereço → `"O endereço de {nome} está sem município (IBGE)."`.
- **Componentes:** total 0 → `"Informe o valor da prestação."`.
- **Aviso, não erro:** chave já usada em outro CT-e não cancelado. O retorno traz `Warnings`, que o
  front mostra.
- **Snapshots:** participantes, veículos e motorista copiados do cadastro a cada gravação.
  - Conjunto informado → placas da tração e reboques.
  - Sem conjunto, só a tração.
  - Motorista vazio → motorista padrão do conjunto.

- [ ] **Step 1: Testes que falham.**
  - **`TransportDocumentsSaveServiceTests`:**
    - `Saves_costa_case_with_route_suggested_from_receiver`: recebedor ≠ destinatário → fim = município
      do recebedor.
    - `Taker_ie_indicator_comes_from_partner`: IE "ISENTO" → 2; sem IE → 9; com IE → 1.
    - `Invalid_nfe_key_is_rejected`.
    - `Missing_nfe_key_is_rejected`.
    - `Duplicate_key_in_other_document_returns_warning`.
    - `Combination_snapshot_copies_plates_and_default_driver`.
    - `Insurance_defaults_from_branch_transport_settings`.
    - `Rejected_document_cannot_change_series`.
    - `Confirmed_document_requires_reopen_to_edit`.
    - `Rejected_in_sapb1`.
  - **`TransportDocumentsTaxApplyServiceTests`:**
    - `Internal_sp_uses_in_state_cfop_and_12_percent`: natureza com CFOP 5352 dentro e 6352 fora,
      ICMS interno 12% configurado → CFOP 5352, alíquota 12, ICMS 78 sobre 650.
    - `Interstate_uses_out_of_state_cfop_and_interstate_rate`: SP→PR → CFOP 6352, 12%.
    - `Interstate_from_south_to_north_uses_7_percent`: PR→BA → 7%.
  - **`TransportDocumentsImportNfeServiceTests`** (XML de `Fiscal.Tests/Support`):
    - `Import_fills_parties_key_value_and_product`.
    - `Import_with_unknown_partner_lists_missing_tax_ids_and_creates_nothing`: mensagem
      `"Cadastre os parceiros antes de importar: {cnpj1}, {cnpj2}."` e nenhum documento novo.
    - `Import_two_nfes_sums_cargo_value_and_keeps_two_keys`.
    - `Import_uses_branch_default_unit_m3_with_zero_quantity`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar serviços, controller, EDM e DI. Ações com `ODataActionParameters`,
  molde `TruckCombinationsController.SaveAsync`.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): inclusão do CT-e com importação do XML da NF-e e ICMS pela natureza`.

### Task 6: Confirmar / Reabrir / Descartar número

**Files:**
- Create: `SiagroB1.Application/Services/Cte/TransportDocumentsConfirmService.cs`, `Services/Cte/CteReadinessValidator.cs`, `Services/Cte/TransportDocumentsReopenService.cs`, `Services/Cte/TransportDocumentsDiscardNumberService.cs`
- Modify: `TransportDocumentsController` (ações `TransportDocumentsConfirm`, `TransportDocumentsReopen`, `TransportDocumentsDiscardNumber`)
- Test: `SiagroB1.Application.Tests/Cte/TransportDocumentsConfirmServiceTests.cs`

**Interfaces:**
- Consumes: Tarefas 1, 3, 4 e 5.
- Produces:
  - `ConfirmAsync(Guid key, string user)`.
    - Valida `CteReadinessValidator`:
      - filial habilitada;
      - certificado válido na data;
      - RNTRC;
      - tração com tara, capacidade e rodado;
      - reboques com tara e capacidade;
      - motorista com CPF válido;
      - natureza resolvida.
    - Na primeira confirmação: reserva número, gera `RandomCode` e monta `AccessKey` com o mês de
      `DateTime.Now`.
  - `ReopenAsync(key)`: Confirmed → Draft, **mantém** número e chave.
  - `DiscardNumberAsync(key, reason)`: Confirmed/Rejected/Draft com número → grava
    `CteNumberToVoid` e zera `Number/AccessKey/RandomCode`, status Draft.

- [ ] **Step 1: Testes que falham.**
  - `Confirm_reserves_number_and_builds_key`.
  - `Confirm_twice_keeps_same_number_and_key` (reabrir e confirmar de novo).
  - `Confirm_without_tare_lists_vehicle` (mensagem `"Veículo {placa} sem tara/capacidade: complete o cadastro."`).
  - `Confirm_with_invalid_driver_cpf_is_rejected`.
  - `Discard_number_moves_it_to_void_list`.
  - `Confirm_disabled_branch_is_rejected`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar (`CteNumberReservationService` substituível no teste por fake, como o
  `FakeNfeNumberReservationService`).
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): confirmação do CT-e com número travado e chave estável`.

### Task 7: Transmitir + consultar (com recuperação)

**Files:**
- Create: `SiagroB1.Application/Services/Cte/CteIssueInputAssembler.cs`, `Services/Cte/TransportDocumentsTransmitService.cs`, `Services/Cte/TransportDocumentsConsultService.cs`, `Services/Cte/CteServiceContextFactory.cs`
- Modify: controller (ações `TransportDocumentsTransmit`, `TransportDocumentsConsult`, função `TransportDocumentsXml(Key)`)
- Test: `SiagroB1.Application.Tests/Cte/TransportDocumentsTransmitServiceTests.cs`

**Interfaces:**
- Consumes: Tarefas 1, 2 e 6; `BranchNfeSettings` (certificado) e `BranchCteSettings` (ambiente).
- Produces:
  - **`CteIssueInputAssembler.Build(TransportDocument doc, Branch branch, BranchCteSettings settings) → CteIssueInput`**:
    - `ObsCont`: `PLACA`, `CPFMOTORISTA`, `NOMEMOTORISTA`, `RASTREADO` (SIM/NAO),
      `TranspProprio` (SIM/NAO);
    - xObs: `"Motorista: {nome} | CPF: {cpf} | Placa: {placas} | {Observation}"`;
    - homologação: `xNome` do destinatário = `"CT-E EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"`.
  - **`TransmitAsync(Guid key, string user) → NfeSefazResult`**: só Confirmed ou Rejected.
    1. Pega a trava de emissão.
    2. Renova `IssuedAt`. Se o mês difere do `AAMM` da chave → `"A chave foi gerada em {mm/aaaa} e a emissão cairia em outro mês. Descarte o número e confirme de novo."`.
    3. Assina e envia.
    4. 100 → Authorized, guarda o `cteProc` em `TransportDocumentXml` (Kind Authorized).
    5. Rejeição → Rejected com código e motivo.
    6. **`NfeCommunicationException` ou 204/539 → `ConsultProtocolAsync(chave)`**: autorizado → trata
       como 100; não encontrado → Rejected com `"Sem resposta da SEFAZ: consulte depois."`.
  - **`ConsultAsync(key)`**: atualiza a partir do protocolo; recupera um Processing ou Rejected que a
    SEFAZ já autorizou.

- [ ] **Step 1: Testes que falham** (`FakeCteSefazClient`):
  - `Authorized_stores_protocol_and_xml`.
  - `Rejected_then_retransmit_keeps_number_and_key`.
  - `Timeout_recovers_authorization_by_consult_without_resending`: o fake lança na 1ª autorização e
    responde 100 na consulta → Authorized, `AuthorizeAsync` chamado 1 vez.
  - `Duplicate_539_recovers_by_consult`.
  - `Different_month_is_refused_with_discard_hint`.
  - `Xml_carries_obscont_plate_and_driver`.
  - `Homologation_replaces_recipient_name`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): transmissão do CT-e com recuperação por consulta`.

### Task 8: Cancelar, CC-e estruturada e inutilização

**Files:**
- Create: `SiagroB1.Application/Services/Cte/TransportDocumentsCancelService.cs`, `Services/Cte/TransportDocumentsCorrectionService.cs`, `Services/Cte/CteCorrectableFields.cs`, `Services/Cte/CteVoidNumbersService.cs`
- Modify: controller (ações `TransportDocumentsCancel`, `TransportDocumentsSendCorrection`, `CteNumbersVoid`; entity set `CteNumbersToVoid` só leitura)
- Test: `SiagroB1.Application.Tests/Cte/TransportDocumentsCancelServiceTests.cs`, `Cte/TransportDocumentsCorrectionServiceTests.cs`, `Cte/CteVoidNumbersServiceTests.cs`

**Interfaces:**
- Produces:
  - **`CancelAsync(key, justification, user)`**:
    - só Authorized;
    - justificativa 15–255 → senão `"A justificativa do cancelamento tem de 15 a 255 caracteres."`;
    - prazo de 168 h desde `AuthorizedAt` → senão `"O prazo de cancelamento do CT-e (168 horas) acabou."`;
    - 135 → Cancelled, guarda o XML do evento.
    - **Gancho para a entrega B:** interface `ICteCancellationHook { Task OnCancelledAsync(TransportDocument doc); }`,
      registrada como no-op agora. A entrega B pluga título e averbação.
  - **`CteCorrectableFields`:** lista fechada, cada item com grupo, campo, rótulo pt-BR e tamanho máx.:
    - `compl/xObs` (2000);
    - `receb/xNome`, `receb/enderReceb/xLgr`, `receb/enderReceb/nro`, `receb/enderReceb/xBairro`;
    - `dest/xNome`, `dest/enderDest/xLgr`, `dest/enderDest/nro`, `dest/enderDest/xBairro`;
    - `infCarga/proPred` (60).
  - **`SendCorrectionAsync(key, IReadOnlyList<(string Group, string Field, string Value)>, user)`**:
    - só Authorized;
    - campo fora da lista → `"O campo {grupo}/{campo} não pode ser corrigido por CC-e."`;
    - sequência = última + 1 (máx. 20);
    - grava `TransportDocumentCorrection` e o XML.
  - **`CteVoidNumbersService.VoidAsync(branchCode, series, from, to, justification)`**:
    - recusa número usado por CT-e Authorized/Cancelled;
    - marca `CteNumberToVoid.VoidedAt` e o protocolo.

- [ ] **Step 1: Testes que falham.**
  - `Cancel_short_justification_is_rejected`.
  - `Cancel_after_168h_is_rejected`.
  - `Cancel_135_marks_cancelled_and_calls_hook`.
  - `Correction_outside_whitelist_is_rejected`.
  - `Correction_increments_sequence`.
  - `Void_range_with_authorized_number_is_rejected`.
  - `Void_marks_pending_numbers`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** commit `feat(invoice): cancelamento, CC-e e inutilização do CT-e`.

### Task 9: DACTE (Relatórios)

**Files:**
- Create: `SiagroB1.Reports/Services/DacteReportService.cs`, `SiagroB1.Reports/Templates/Dacte.frx` (ou pasta equivalente dos `.frx`), `SiagroB1.Reports/Dtos/DacteDto.cs`, `SiagroB1.Reports/Controllers/DacteController.cs` (`GET /reports/Dacte/{key}`)
- Test: `SiagroB1.Application.Tests/Reports/DacteReportTests.cs` (+ `ReportTemplateRenderSmokeTests` passa a listar `Dacte.frx` automaticamente, se for por pasta)

**Interfaces:**
- Produces: `DacteReportService.RenderAsync(Guid key) → byte[]` (PDF), lendo o `cteProc` autorizado.
  - Monta o DTO **do XML**, não da entidade, para o DACTE refletir o que a SEFAZ autorizou.
  - Leiaute retrato do manual: cabeçalho com emitente, modal, chave em código de barras CODE-128C e
    protocolo; tomador, remetente, destinatário, expedidor e recebedor; produto predominante,
    quantidades e valor da carga; componentes; ICMS; documentos originários (chaves); observações;
    dados do modal rodoviário (RNTRC); QR Code (`infCTeSupl/qrCodCTe`).
  - Homologação: tarja "SEM VALOR FISCAL". Cancelado: tarja "CANCELADO".
  - Logo: reaproveitar `DanfeLogo`.

- [ ] **Step 1: Testes que falham.**
  - `Dacte_renders_pdf_from_authorized_xml` (PDF não vazio, começa com `%PDF`, contém a chave
    formatada no texto extraído, como os testes de relatório existentes fazem).
  - `Dacte_for_cancelled_shows_cancelled_stamp`.
  - `Dacte_for_unauthorized_document_is_refused`.
- [ ] **Step 2:** FAIL.
- [ ] **Step 3:** implementar. Fontes métricas (ver memória "Relatório ilegível no Linux").
- [ ] **Step 4:** PASS.
- [ ] **Step 5:** abrir o PDF gerado localmente (memória "Ver um .frx renderizado localmente") e
  conferir o leiaute contra o manual.
- [ ] **Step 6:** commit `feat(reports): DACTE do CT-e`.

### Task 10: Menu + EDM final (backend)

**Files:**
- Migration (Common): `AddCteMenus`: `MENU_ITEMS` `transportDocuments`, título `"Conhecimentos de Transporte"`, `ParentKey` = o módulo de Vendas (consultar `MENU_ITEMS`), `StandaloneOnly = true`, mais `ROLE_MENUS` ADMIN com GUID fixo.
- Test: `SiagroB1.Application.Tests/Cte/CteEdmModelTests.cs`

- [ ] **Step 1:** testes de EDM: entity set `TransportDocuments` com navegações para os filhos;
  ações e funções das tarefas 3 e 5–8 com os tipos de parâmetro esperados.
- [ ] **Step 2:** FAIL, depois implementar o que faltar. PASS.
- [ ] **Step 3:** migration de menu, aplicada em `CEAGUI_SIAGRO_COMMON_DEV`.
- [ ] **Step 4:** `dotnet test SiagroB1.Application.Tests` e `SiagroB1.Fiscal.Tests` completos, verdes.
- [ ] **Step 5:** commit `feat(invoice): menu e EDM do CT-e` + `DB: AddCteMenus`.

### Task 11: Frontend — telas do CT-e e aba de configuração

**Files** (worktree `siagro-b1-frontend-cte-issuance`):
- Create: `webapp/view/transportDocuments/{Main,Form,Detail}.view.xml` (+ fragments), `webapp/controller/transportDocuments/*.ts`, `webapp/helpers/CteHelpers.ts`
- Modify: `webapp/view/nfeSettings/*` + controller (aba "CT-e"), `webapp/manifest.json` (rotas `transportDocuments`, `transportDocumentsNew`, `transportDocumentsEdit`, `transportDocumentsDetail`; padrões `transport-documents`, `.../new`, `.../{id}/edit`, `.../{id}`), `webapp/model/ServerRoutes.ts`
- Test: `webapp/test/unit/helpers/CteHelpers.qunit.ts`

**Interfaces:**
- Consumes: ações e funções das tarefas 3 e 5–8; `/reports/Dacte/{key}`.
- Produces: helpers testáveis:
  - `cteStatusText`/`cteStatusState`;
  - `takerRoleText`;
  - `quantityUnitText`;
  - `formatAccessKey` (grupos de 4);
  - `buildTransportDocumentSavePayload(form)`;
  - `cancellationJustificationError(text)`: mesma regra 15–255.

**O que muda:**
- **Lista** com FilterBar: período, situação, tomador, remetente, destinatário, placa. Colunas:
  - número/série, emissão, situação (`ObjectStatus`);
  - tomador, remetente, destinatário;
  - placa, valor da prestação, chave.
- **Formulário (página)**, seções:
  - natureza e tomador;
  - participantes, com value help de parceiros;
  - rota, com município sugerido e editável;
  - NF-e transportadas: lista de chaves e botão **"Importar XML da NF-e"**, que aceita vários
    arquivos e lê com `FileReader`, como o upload de certificado;
  - carga;
  - componentes;
  - tributos (só leitura, recalculados ao gravar);
  - modal: conjunto (value help de conjuntos) ou tração (`openTractionTrucksValueHelp`) e motorista;
  - seguro;
  - observações, com as flags rastreado e transporte próprio;
  - dimensões.
  - Os avisos do `SaveAsync` aparecem em `MessageBox.warning`.
- **Detalhe (ObjectPage)** com os botões conforme a situação:
  - Confirmar e Reabrir;
  - Transmitir e Consultar;
  - DACTE;
  - Cancelar (diálogo com justificativa);
  - CC-e (diálogo: campo da lista fechada + valor, várias linhas);
  - Descartar número.
- **Aba CT-e na Configuração da NF-e:**
  - "Emite CT-e", ambiente, série padrão e unidade padrão;
  - séries (tabela como a da NF-e);
  - números a inutilizar, com botão "Inutilizar".
- **Lições do sub-projeto 2:**
  - enum e data do OData v4 com formatter ou Select usam `targetType: 'any'`;
  - `$expand` com `$select` explícito dos filhos;
  - toda tela chama `refreshStandaloneFlag()` ao abrir.

- [ ] **Step 1:** testes QUnit dos helpers (RED por typecheck).
- [ ] **Step 2:** implementar helpers e telas.
- [ ] **Step 3:** `yarn ts-typecheck`, `yarn lint` e QUnit no navegador (`unitTests`) verdes.
- [ ] **Step 4:** commits por tela: `feat(invoice): lista e formulário do CT-e`,
  `feat(invoice): detalhe e eventos do CT-e`, `feat(invoice): aba CT-e na configuração`.

### Task 12: Verificação ponta a ponta (homologação SEFAZ)

- [ ] **Step 1:** stack `ceagui` dos worktrees novos. Migrations em dia nas empresas locais.
- [ ] **Step 2:** pelo caminho do usuário, a partir da home:
  1. configurar a aba CT-e (homologação, série 1);
  2. RNTRC na filial;
  3. veículo com tara/capacidade/rodado, conjunto e motorista;
  4. natureza de prestação de serviço de transporte (CFOP 5352/6352, CST 00, 12%).
- [ ] **Step 3:** criar CT-e importando o XML de uma NF-e de teste (de
  `SiagroB1.Fiscal.Tests/Support` ou das NF-e de homologação já emitidas pela CEAGUI local), com
  tomador destinatário e recebedor diferente.
- [ ] **Step 4:** confirmar, transmitir **em homologação** e consultar; abrir o DACTE; mandar uma
  CC-e de `xObs`; cancelar com justificativa; inutilizar um número descartado.
  - ⚠️ A transmissão à SEFAZ, mesmo em homologação, é ação externa. Pedir confirmação ao usuário
    antes, fazer **pela tela** e com dados de teste. Ver memória "Dados reais da CEAGUI do
    EfisCloud": transmitir com dado real pela API foi bloqueado.
- [ ] **Step 5:** conferir o XML autorizado:
  - `toma3=3`;
  - `indIEToma`;
  - `<receb>` presente;
  - `ObsCont` (PLACA/CPFMOTORISTA/RASTREADO);
  - unidade "00";
  - CFOP 5352.
- [ ] **Step 6:** SAPB1: menu e telas ausentes (sem navegador se o banco local continuar
  defasado; nesse caso, via testes `Rejected_in_sapb1`).
- [ ] **Step 7:** derrubar a stack pelos PIDs; superpowers:verification-before-completion; revisão
  final do branch.
