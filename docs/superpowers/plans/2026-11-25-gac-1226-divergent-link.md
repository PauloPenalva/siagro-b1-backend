# GAC-1226 — Vínculo de NF do SAP com divergência — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** o vínculo de NF do SAP na carga passa a aceitar quantidade e valor diferentes do esperado, avisa, grava um
registro de divergência por NF e fecha a carga por um sexto termo no saldo; o vínculo 1:1 da NF de devolução sai.

**Architecture:** o documento de saída continua espelhando a NF (contrato baixado pela quantidade da NF). A diferença
QE − QNF vira `SalesInvoiceDivergence` e entra no saldo da carga como `ShipmentLoad.InvoiceDivergenceQuantity`,
persistido-derivado, escrito só por `ShipmentLoadsRecalculateInvoicedService`. A classificação (tipo 0/1/2) vive num
classificador puro no servidor; a tela pede uma prévia ao servidor em vez de repetir a regra.

**Tech Stack:** .NET 10, EF Core SQL Server, OData, xUnit + EF InMemory; OpenUI5 + TypeScript, QUnit.

**Spec:** `docs/superpowers/specs/2026-11-24-gac-1226-divergent-link-design.md` (leia inteira, inclusive "Ajustes da
revisão de 24/11" e "Decisões do usuário de 25/11", que valem sobre o texto acima delas).

## Global Constraints

- Só `Erp = SAPB1`. Nada muda em STANDALONE; nada é gravado no SAP nem no InvOne.
- Identificadores em inglês; texto ao usuário e comentários em pt-BR.
- Branch `feature/gac-1226-divergent-link` nos dois worktrees
  (`C:\Projetos\SiagroB1\worktrees\gac-1226-credit-memo\{backend,frontend}`). Conferir o branch antes de cada commit.
- Commit por tarefa: `tipo(escopo): descrição` + `Refs: GAC-1226` + `DB: <Migration>` quando houver migration.
  Nunca `git push`. Nunca `git add -A` no frontend depois de `yarn test` (gera `report/`, `coverage/`,
  `.nyc_output/`): stage por caminho.
- Todo arquivo novo recebe `git add` na hora.
- Tolerâncias: 0,001 na quantidade; R$ 0,01 no valor.
- Textos fixos (copiar exatamente):
  - tipo 1: `Valor(es) da(s) NF(s) diferente(s) do contrato, deseja finalizar o faturamento?`
  - tipo 2: `Valor(es) e qtde(s) da(s) NF(s) diferente(s) do contrato, deseja finalizar o faturamento?`
  - `Informe o cliente da carga para vincular a NF do SAP.` / `Informe a transportadora da carga para vincular a NF
    do SAP.` / `Informe a placa da carga para vincular a NF do SAP.`
  - `Desfaça a descarga e a conferência antes de desvincular as notas fiscais.`
  - `Estorne a recusa antes de desvincular.`
- DTO devolvido por function/action leva `[JsonPropertyName]` em PascalCase em toda propriedade.
- Serviço novo de modo SAP é registrado à mão no ramo SAPB1 de `SiagroB1.Web/Program.cs` (~linha 133).
- Testes do backend: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"`.
- Processo só se mata pelo PID (`taskkill //PID <pid> //F`).

## Decisão de desenho tomada no plano (difere de uma frase da spec)

A spec diz que a função das liberações devolve o preço para a tela classificar igual ao servidor. Em vez disso há
uma function de **prévia** (`ShipmentLoadsGetSapLinkPreview`) que devolve QE, QNF, VE, VNF, tipo e mensagem já
calculados. Mesmo efeito para o usuário, uma regra só.

## Review Focus

1. NF **maior** que a carga (30.150 sobre 30.000): divergência −150, saldo 0, carga Faturada — teste na Tarefa 3 e 7.
2. Rateio da QE entre várias NFs: as partes somam exatamente a QE, a última leva o resto — teste na Tarefa 6.
3. Contrato em dólar ou sem preço confirmado: classifica só por quantidade, valores nulos, sem exceção — Tarefa 6 e 7.
4. XML do add-on ilegível, em UTF-16 ou sem `finNFe`: a NF some da lista, sem erro 500 — Tarefas 4 e 5.
5. Placa da carga com hífen, espaço ou minúscula (`abc-1d23`) contra `ABC1D23` do SAP: casa — Tarefa 5.

---

## Backend

### Task 1: remover o vínculo 1:1 da NF de devolução (ORIN)

**Files:**
- Delete: `SiagroB1.Application/Services/SAP/SapLinkableCreditMemosService.cs`,
  `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsLinkSapCreditMemoService.cs`,
  `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsUnlinkSapCreditMemoService.cs`,
  `SiagroB1.Domain/Dtos/SapLinkableCreditMemoDto.cs`,
  `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsLinkSapCreditMemoController.cs`,
  `SiagroB1.Web/Functions/ShipmentLoads/ShipmentLoadsGetLinkableSapCreditMemosController.cs`,
  testes `SalesInvoices/SalesInvoiceSapCreditMemoLockTests.cs`, `Sap/SapCreditMemoLinkEdmTests.cs`,
  `Sap/SapLinkableCreditMemosServiceTests.cs`, `ShipmentLoads/ShipmentLoadsSapCreditMemoLinkTests.cs`
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs:199-214` (saem `SapCreditMemoDocEntry`/`SapCreditMemoDocNum`;
  `IsSapLinked => SapDocEntry != null`), `SiagroB1.Infra/Context/AppDbContext.cs:575-580` (índice),
  `SalesInvoices/SalesInvoiceNfeLock.cs:53-63,178-179`, `SalesInvoicesItemsDeleteService.cs:51,66`,
  `SalesInvoicesItemsUpdateService.cs:137,143`, `SalesInvoicesReverseConfirmService.cs:44,67-68`,
  `SAP/SapInvoiceSyncService.cs` (constante `CreditMemoCancelledReason`, linhas 117, 168-195, 222-291: o segundo
  passo inteiro), `SiagroB1.Web/ODataConfig/ODataConfigurations.cs:1678-1687,1906-1911`,
  `SiagroB1.Web/Program.cs:137-139`, `Sap/SapInvoiceSyncServiceTests.cs` (testes do Credit Memo)
- Create: migration `RemoveSalesInvoiceSapCreditMemoLink`
- Test: `SiagroB1.Application.Tests/Sap/SapInvoiceLinkEdmTests.cs` (novo)

**Fica** (a frente 6 reaproveita): `SapCreditMemo`, `SapCreditMemoLine` e os DbSets, `GetForCreditMemosAsync` e
`ArCreditMemoDocType` no InvOne (trocar o comentário "HIPÓTESE" por "confirmado na produção da Yokotobi em
24/11/2026"), `SapTaxDocumentFormat`, `ApplySapFiscalFields`.

**Interfaces:**
- Produces: `SalesInvoice.IsSapLinked` olha só `SapDocEntry`.

- [ ] **Passo 1: teste que falha.** `SapInvoiceLinkEdmTests`, copiando a montagem do modelo de
  `SapCreditMemoLinkEdmTests` antes de apagá-lo:

```csharp
[Theory]
[InlineData("ShipmentLoadsLinkSapCreditMemo")]
[InlineData("ShipmentLoadsUnlinkSapCreditMemo")]
[InlineData("ShipmentLoadsGetLinkableSapCreditMemos")]
public void Credit_memo_link_routes_are_gone(string name) =>
    Assert.DoesNotContain(Model.SchemaElements.OfType<IEdmOperation>(), o => o.Name == name);

[Fact]
public void Sales_invoice_no_longer_exposes_the_credit_memo_link() =>
    Assert.Null(typeof(SalesInvoice).GetProperty("SapCreditMemoDocEntry"));
```

- [ ] **Passo 2:** rodar `--filter "FullyQualifiedName~SapInvoiceLinkEdmTests"` → FALHA (rotas e propriedade existem).
- [ ] **Passo 3:** apagar e editar os arquivos acima. Na sincronização, o laço de venda fica intacto.
- [ ] **Passo 4:** `dotnet ef migrations add RemoveSalesInvoiceSapCreditMemoLink --context AppDbContext --project
  SiagroB1.Migrations --startup-project SiagroB1.Web`. Conferir que o `Up` só derruba o índice e as duas colunas.
- [ ] **Passo 5:** `dotnet build SiagroB1.sln` sem erro; rodar `~SapInvoiceLinkEdmTests`, `~SapInvoiceSync`,
  `~SalesInvoiceNfeLock`, `~SalesInvoiceSapLink`, `~AppDbContextModel` → PASSA.
- [ ] **Passo 6: commit** `refactor(sap): remova o vínculo da NF de devolução na carga` com
  `DB: RemoveSalesInvoiceSapCreditMemoLink`. O commit apaga arquivos: se o classificador barrar, usar pathspecs
  explícitos (memória `commit-with-deletions-blocked-by-classifier`).

### Task 2: modelo da divergência e migration

**Files:**
- Create: `SiagroB1.Domain/Enums/SapLinkType.cs`, `SiagroB1.Domain/Enums/SalesInvoiceDivergenceStatus.cs`,
  `SiagroB1.Domain/Entities/SalesInvoiceDivergence.cs`,
  `SiagroB1.Web/Controllers/SalesInvoiceDivergencesController.cs` (só `Get` com `[EnableQuery]`, no padrão dos
  controllers de leitura vizinhos), migration `AddSalesInvoiceDivergences`
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs`, `SiagroB1.Domain/Entities/ShipmentLoad.cs` (propriedade,
  navegação, `AvailableQuantity`, `CalculateAvailableQuantity`, comentário de `CardCode`),
  `SiagroB1.Infra/Context/AppDbContext.cs`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`,
  chamadores de `CalculateAvailableQuantity`: `ShipmentLoadsBillingGuardService.cs:164`,
  `ShipmentLoadsTransshipmentStartService.cs:67` (por ora passam `0m`/o persistido; a Tarefa 3 liga o cálculo)
- Test: `SiagroB1.Application.Tests/Infra/AppDbContextModelTests.cs`,
  `SiagroB1.Application.Tests/ShipmentLoads/ShipmentLoadAvailableQuantityTests.cs` (novo)

**Interfaces:**
- Produces:

```csharp
public enum SapLinkType { Normal = 0, ValueDivergent = 1, QuantityAndValueDivergent = 2 }
public enum SalesInvoiceDivergenceStatus { Open = 0, Voided = 1 }

[Table("SALES_INVOICE_DIVERGENCES")]
public class SalesInvoiceDivergence
{
    public Guid Key { get; set; }
    public Guid SalesInvoiceKey { get; set; }
    public SalesInvoice? SalesInvoice { get; set; }
    public Guid ShipmentLoadKey { get; set; }
    public SapLinkType LinkType { get; set; }
    public decimal EffectiveQuantity { get; set; }   // DECIMAL(18,3)
    public decimal InvoiceQuantity { get; set; }     // DECIMAL(18,3)
    public decimal QuantityDifference { get; set; }  // QE − QNF, com sinal
    public decimal? EffectiveValue { get; set; }     // DECIMAL(18,2); nulo sem preço em R$
    public decimal InvoiceValue { get; set; }        // DECIMAL(18,2)
    public decimal? ValueDifference { get; set; }    // VE − VNF
    public SalesInvoiceDivergenceStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }
    public string? VoidReason { get; set; }          // NVARCHAR(200)
}

// SalesInvoice
public SapLinkType? SapLinkType { get; set; }
// ShipmentLoad
public decimal InvoiceDivergenceQuantity { get; set; }   // DECIMAL(18,3), padrão 0
public virtual ICollection<SalesInvoiceDivergence> InvoiceDivergences { get; } = [];
public static decimal CalculateAvailableQuantity(decimal totalQuantity, decimal invoicedQuantity,
    decimal returnedToWarehouseQuantity, decimal transshippedQuantity, decimal transitLossQuantity,
    decimal invoiceDivergenceQuantity);   // sexto parâmetro OBRIGATÓRIO: nenhum chamador esquece
```

- Mapeamento: FK `SalesInvoiceKey` com `OnDelete(Cascade)` (documento Pendente excluído leva o registro junto e o
  serviço de exclusão não precisa conhecer a tabela); FK `ShipmentLoadKey` com `NoAction`; índice em
  `ShipmentLoadKey`; índice único em `SalesInvoiceKey` filtrado por `[Status] = 0`.
- EDM: entity set `SalesInvoiceDivergences`; navegação `ShipmentLoad.InvoiceDivergences`.
- Comentário de `ShipmentLoad.CardCode`: deixa de ser "meramente informativo" — desde 24/11/2026 filtra as NFs do
  SAP e as liberações do vínculo; continua sem efeito em saldo, romaneio e documento.

- [ ] **Passo 1: testes que falham.**

```csharp
[Theory]
[InlineData(30000, 29800, 200, 0)]      // NF menor: a divergência fecha a carga
[InlineData(30000, 30150, -150, 0)]     // NF maior: divergência negativa
[InlineData(30000, 29800, 0, 200)]      // sem divergência registrada sobra saldo
public void Divergence_is_the_sixth_term(decimal total, decimal invoiced, decimal divergence, decimal expected) =>
    Assert.Equal(expected, ShipmentLoad.CalculateAvailableQuantity(total, invoiced, 0, 0, 0, divergence));
```

  Em `AppDbContextModelTests`: a entidade mapeia `SALES_INVOICE_DIVERGENCES`; a FK para `SalesInvoice` é `Cascade`;
  existe índice único em `SalesInvoiceKey` com filtro `[Status] = 0`.
- [ ] **Passo 2:** rodar `~ShipmentLoadAvailableQuantityTests` e `~AppDbContextModelTests` → FALHA (não compila).
- [ ] **Passo 3:** implementar entidades, mapeamento, EDM e controller; ajustar os chamadores.
- [ ] **Passo 4:** `dotnet ef migrations add AddSalesInvoiceDivergences --context AppDbContext --project
  SiagroB1.Migrations --startup-project SiagroB1.Web`. Conferir: tabela, `SALES_INVOICES.SapLinkType` anulável,
  `SHIPMENT_LOADS.InvoiceDivergenceQuantity` com padrão 0.
- [ ] **Passo 5:** build + os dois filtros → PASSA.
- [ ] **Passo 6: commit** `feat(shipment): crie o registro de divergência da NF do SAP` com
  `DB: AddSalesInvoiceDivergences`.

### Task 3: sexto termo no saldo e na situação da carga

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsRecalculateDivergenceService.cs`
- Modify: `ShipmentLoadsRecalculateInvoicedService.cs:122-160,287-338`, `ShipmentLoadsBillingGuardService.cs:150-167`,
  `ShipmentLoadsTransshipmentStartService.cs:57-68`. `SiagroB1.Reports/Services/DashboardService.cs:89` fica como
  está: soma `TotalQuantity - InvoicedQuantity` só de cargas Open/PartiallyInvoiced, e o vínculo com divergência
  sempre fecha a carga
- Test: `ShipmentLoads/ShipmentLoadsRecalculateDivergenceServiceTests.cs` (novo),
  `ShipmentLoads/ShipmentLoadsRecalculateInvoicedServiceTests.cs`

**Interfaces:**
- Consumes: `SalesInvoiceDivergence`, `CalculateAvailableQuantity` de seis termos (Tarefa 2).
- Produces:

```csharp
public static class ShipmentLoadsRecalculateDivergenceService
{
    /// Σ QuantityDifference dos registros Open da carga cujo documento está vivo
    /// (Pending, Confirmed ou Returned) e NÃO totalmente devolvido
    /// (Σ itens − Σ itens das devoluções Confirmed com origem nele > 0,001).
    public static Task<decimal> CalculateAsync(
        AppDbContext context, Guid shipmentLoadKey, ICollection<Guid>? excludedInvoiceKeys);
}

// ResolveStatus ganha o último parâmetro, opcional:
public static ShipmentLoadStatus ResolveStatus(..., bool endsAtFinalTransshipment = false,
    decimal invoiceDivergenceQuantity = 0m);
```

- `RecalculateAsync` grava `load.InvoiceDivergenceQuantity` junto com os outros quatro termos; `available` de
  `ResolveStatus` subtrai o termo. O rótulo (Open × PartiallyInvoiced) não muda.
- `excludedInvoiceKeys` tem a mesma função que em `CalculateInvoicedAsync`: documento removido na mesma transação.

- [ ] **Passo 1: testes que falham** (carga de 30.000 em todos):

```csharp
// Divergência aberta de 200 com documento Confirmed de 29.800 → CalculateAsync = 200
// Registro Voided → 0
// Documento Cancelled com registro ainda Open → 0
// Devolução Confirmed de 29.800 sobre o documento (totalmente devolvido) → 0
// Devolução Confirmed de 10.000 (parcial) → 200
// A devolução acima cancelada → volta a 200 sem mexer no registro
// documento em excludedInvoiceKeys → 0
[Fact] public async Task Load_with_a_smaller_invoice_and_its_divergence_is_invoiced()
// Recalculate: InvoicedQuantity 29.800, InvoiceDivergenceQuantity 200, AvailableQuantity 0, Status Invoiced
[Fact] public async Task Load_with_a_larger_invoice_is_invoiced_with_a_negative_divergence()
// NF 30.150: InvoicedQuantity 30.150, InvoiceDivergenceQuantity -150, AvailableQuantity 0, Status Invoiced
[Fact] public async Task Billing_guard_available_subtracts_the_divergence()
// CalculateAvailableAsync = 0 com NF 29.800 + divergência 200
```

- [ ] **Passo 2:** rodar `~ShipmentLoadsRecalculateDivergence` e `~ShipmentLoadsRecalculateInvoiced` → FALHA.
- [ ] **Passo 3:** implementar. Filtro de enum anulável: lista do mesmo tipo (`InvoiceStatus?[]`), como em
  `CalculateInvoicedAsync`.
- [ ] **Passo 4:** os dois filtros + `~ShipmentLoadsBillingGuard` + `~Transshipment` → PASSA.
- [ ] **Passo 5: commit** `feat(shipment): abata a divergência de NF do saldo da carga`.

### Task 4: leitura da placa, da moeda e da finalidade (finNFe)

**Files:**
- Modify: `SiagroB1.Domain/Entities/SAP/SapInvoiceTransport.cs` (`Vehicle`, coluna `Vehicle`, `NVARCHAR(10)`),
  `SiagroB1.Domain/Entities/SAP/SapInvoice.cs` (`DocCur`, coluna `DocCur`, `NVARCHAR(3)`),
  `SiagroB1.Domain/Entities/SAP/InvOneProcess.cs` (`byte[]? XmlEnvAssinado`),
  `SiagroB1.Domain/Interfaces/ISapFiscalDocumentSource.cs`, `SiagroB1.Application/Services/SAP/InvOneFiscalDocumentSource.cs`
- Test: `SiagroB1.Application.Tests/Sap/InvOneFiscalDocumentSourceTests.cs`

**Interfaces:**
- Produces:

```csharp
// ISapFiscalDocumentSource — implementação padrão devolve dicionário vazio
/// finNFe de cada invoice AUTORIZADA, lido do XML; a invoice sem XML legível ou sem a tag não entra no resultado.
Task<IReadOnlyDictionary<int, string>> GetInvoicePurposesAsync(
    IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default);

// InvOneFiscalDocumentSource
internal static string? ReadPurpose(byte[]? compressedXml);
```

- `ReadPurpose`: `DeflateStream` (deflate sem cabeçalho); se o segundo byte descomprimido for 0, decodifica como
  UTF-16LE, senão UTF-8; extrai `<finNFe>(\d)</finNFe>`; qualquer falha devolve `null`, nunca lança.
- `GetInvoicePurposesAsync` lê só `DocType = 13`, `StatusId = 4`, pelos mesmos lotes e filtro de filial de
  `GetAsync`, projetando `DocEntry`, `CompanyId`, `XmlEnvAssinado`. A sincronização não chama este método.

- [ ] **Passo 1: testes que falham.**

```csharp
static byte[] Deflate(string xml, Encoding e) { /* DeflateStream sobre e.GetBytes(xml) */ }
const string Xml = "<NFe><infNFe><ide><tpNF>1</tpNF><finNFe>1</finNFe></ide></infNFe></NFe>";

[Fact] public void Reads_the_purpose_from_raw_deflate_utf8() =>
    Assert.Equal("1", InvOneFiscalDocumentSource.ReadPurpose(Deflate(Xml, Encoding.UTF8)));
[Fact] public void Reads_the_purpose_from_utf16() =>
    Assert.Equal("1", InvOneFiscalDocumentSource.ReadPurpose(Deflate(Xml, Encoding.Unicode)));
[Fact] public void Complement_invoice_reads_two() =>
    Assert.Equal("2", InvOneFiscalDocumentSource.ReadPurpose(Deflate(Xml.Replace("<finNFe>1", "<finNFe>2"), Encoding.UTF8)));
[Theory] [InlineData(null)] [InlineData(new byte[0])] [InlineData(new byte[] { 1, 2, 3, 4 })]
public void Unreadable_xml_has_no_purpose(byte[]? raw) => Assert.Null(InvOneFiscalDocumentSource.ReadPurpose(raw));
[Fact] public void Xml_without_the_tag_has_no_purpose() =>
    Assert.Null(InvOneFiscalDocumentSource.ReadPurpose(Deflate("<NFe/>", Encoding.UTF8)));
[Fact] public async Task Purposes_ignore_credit_memos_and_unauthorized_rows()
// Process: (13, autorizada, fin 1), (13, StatusId 2, fin 1), (14, autorizada, fin 4), mesma filial
// → só o DocEntry da primeira, com "1"
```

- [ ] **Passo 2:** rodar `~InvOneFiscalDocumentSource` → FALHA.
- [ ] **Passo 3:** implementar.
- [ ] **Passo 4:** rodar o filtro → PASSA.
- [ ] **Passo 5: commit** `feat(sap): leia a finalidade da NF-e no XML do add-on fiscal`.

### Task 5: filtros novos da lista de NFs

**Files:**
- Modify: `SiagroB1.Application/Services/SAP/SapLinkableInvoicesService.cs`, `SiagroB1.Domain/Dtos/SapLinkableInvoiceDto.cs`
  (sai `CarrierMismatch`)
- Test: `SiagroB1.Application.Tests/Sap/SapLinkableInvoicesServiceTests.cs`

**Interfaces:**
- Consumes: `GetInvoicePurposesAsync`, `Vehicle`, `DocCur` (Tarefa 4).
- Produces:

```csharp
public static string NormalizePlate(string? plate);   // maiúsculas, sem hífen e sem espaço; "" quando vazia
public static IQueryable<SapInvoiceProjection> BuildSapQuery(
    SapErpDbContext sap, string itemCode, int branchId, string cardCode, string carrierCode, string plate,
    DateTime? from, DateTime? toExclusive, int? docEntry);
```

- Filtros novos no SQL: `i.CardCode == cardCode`; `i.Transport.Carrier == carrierCode`;
  `i.Transport.Vehicle.Replace("-", "").Replace(" ", "").ToUpper() == plate`; `i.DocCur == "R$"`;
  `i.Lines.Any(l => l.UnitMsr == "KG")`.
- Depois da exclusão das já vinculadas e do filtro de autorizada: com `fiscal.IsAvailable`, só ficam as de
  finalidade `"1"`. `DbException`/`TimeoutException` do add-on vira a mesma `BusinessException` que já existe.
- Carga sem cliente, transportadora ou placa: `BusinessException` com o texto de Global Constraints, nessa ordem de
  verificação, antes de consultar o SAP.

- [ ] **Passo 1: testes que falham** (reaproveitar a montagem existente do arquivo):

```csharp
[Fact] public async Task Lists_only_invoices_of_the_load_customer()
[Fact] public async Task Lists_only_invoices_of_the_load_carrier()
[Theory] [InlineData("abc-1d23")] [InlineData("ABC 1D23")] [InlineData("ABC1D23")]
public async Task Plate_matches_ignoring_case_hyphen_and_space(string loadPlate)   // INV12.Vehicle = "ABC1D23"
[Fact] public async Task Invoice_in_another_unit_or_currency_is_not_listed()        // unitMsr "TON"; DocCur "US$"
[Fact] public async Task Complement_invoice_is_not_listed()                          // finalidade "2"
[Fact] public async Task Invoice_without_readable_purpose_is_not_listed()            // fora do dicionário
[Fact] public async Task Without_fiscal_addon_the_purpose_filter_does_not_apply()    // IsAvailable = false
[Theory]
[InlineData(null, "T01", "ABC1D23", "Informe o cliente da carga para vincular a NF do SAP.")]
[InlineData("C01", null, "ABC1D23", "Informe a transportadora da carga para vincular a NF do SAP.")]
[InlineData("C01", "T01", " ", "Informe a placa da carga para vincular a NF do SAP.")]
public async Task Load_missing_data_is_refused(string? card, string? carrier, string? plate, string message)
[Fact] public void Sap_query_translates_the_new_filters()
// ToQueryString no provider SqlServer contém "[Vehicle]", "[DocCur]" e "[unitMsr]"
```

- [ ] **Passo 2:** rodar `~SapLinkableInvoicesService` → FALHA.
- [ ] **Passo 3:** implementar; corrigir os testes antigos que dependiam de `CarrierMismatch` ou de carga sem
  cliente/placa.
- [ ] **Passo 4:** rodar o filtro → PASSA.
- [ ] **Passo 5: commit** `feat(sap): filtre as NFs do vínculo por cliente, transportadora, placa e finalidade`.

### Task 6: classificador do vínculo

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/SapLinkClassifier.cs`,
  `SiagroB1.Application/Services/ShipmentLoads/SapLinkReferencePrice.cs`
- Test: `SiagroB1.Application.Tests/ShipmentLoads/SapLinkClassifierTests.cs`,
  `SiagroB1.Application.Tests/ShipmentLoads/SapLinkReferencePriceTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record SapLinkInvoice(int DocEntry, decimal Quantity, decimal LineTotal);
public sealed record SapLinkShare(int DocEntry, decimal EffectiveQuantity, decimal InvoiceQuantity,
    decimal? EffectiveValue, decimal InvoiceValue)
{
    public decimal QuantityDifference => EffectiveQuantity - InvoiceQuantity;
    public decimal? ValueDifference => EffectiveValue - InvoiceValue;
}
public sealed record SapLinkAssessment(SapLinkType Type, decimal EffectiveQuantity, decimal InvoiceQuantity,
    decimal? EffectiveValue, decimal InvoiceValue, IReadOnlyList<SapLinkShare> Shares)
{
    public decimal QuantityDifference { get; }   // QE − QNF
    public decimal? ValueDifference { get; }     // VE − VNF
    public string? Message { get; }              // texto fixo do tipo; nulo no tipo 0
}
public static class SapLinkClassifier
{
    public const decimal QuantityTolerance = 0.001m;
    public const decimal ValueTolerance = 0.01m;
    public static SapLinkAssessment Assess(decimal effectiveQuantity, decimal? referencePrice,
        IReadOnlyList<SapLinkInvoice> invoices);
}
public static class SapLinkReferencePrice
{
    /// Preço em R$/unidade que a alocação usaria; nulo quando o contrato não é em R$ ou o preço confirmado é ≤ 0.
    public static Task<decimal?> ResolveAsync(SalesContractsFixedVolumeService fixedVolume, SalesContract contract);
}
```

- Regras: tipo 2 se |QE − QNF| > 0,001; senão tipo 0 se o preço é nulo; senão tipo 1 se |VE − VNF| > 0,01; senão 0.
  VE = `Round(QE × preço, 2)`. Rateio: parte de cada NF = `Round(QE × qtde / QNF, 3)`, a última leva `QE − Σ`
  anteriores; VE da parte = `Round(parteQE × preço, 2)`. Ordem das partes = ordem de `invoices`.
- `ResolveAsync`: `StandardCurrency` nulo conta como real; preço =
  `fixedVolume.ConfirmedUnitPriceAsync(contract.Key, contract.Price)`.

- [ ] **Passo 1: testes que falham.**

```csharp
[Fact] public void Equal_quantity_and_value_is_normal()
// Assess(30000, 2.00m, [(1, 30000, 60000.00)]) → Normal, Message null, VE 60000.00
[Fact] public void Value_within_one_cent_is_normal()      // LineTotal 60000.01 → Normal
[Fact] public void Value_beyond_one_cent_is_type_one()    // LineTotal 60000.02 → ValueDivergent + texto do tipo 1
[Fact] public void Smaller_invoice_is_type_two()
// Assess(30000, 2.00m, [(1, 29800, 59600.00)]) → tipo 2, QuantityDifference 200, ValueDifference 400.00, texto do tipo 2
[Fact] public void Larger_invoice_is_type_two_with_negative_difference()   // 30150 → -150
[Fact] public void Quantity_differs_but_value_matches_is_still_type_two()  // (29800, 60000.00)
[Fact] public void Without_reference_price_only_quantity_decides()
// Assess(30000, null, [(1, 30000, 1.00)]) → Normal; EffectiveValue e ValueDifference nulos
// Assess(30000, null, [(1, 29800, 1.00)]) → tipo 2; EffectiveValue nulo
[Fact] public void Effective_quantity_is_prorated_and_the_last_share_takes_the_remainder()
// Assess(30000, 2.00m, [(1, 10000, 20000), (2, 19800, 39600)])
// → partes 10067.114 e 19932.886; soma exatamente 30000; VE das partes 20134.23 e 39865.77
[Fact] public async Task Dollar_contract_has_no_reference_price()
[Fact] public async Task Contract_without_confirmed_price_has_no_reference_price()   // Price 0, sem fixação
[Fact] public async Task Fixed_contract_uses_its_price()
[Fact] public async Task Confirmed_fixations_give_the_weighted_price()
```

- [ ] **Passo 2:** rodar `~SapLinkClassifier` e `~SapLinkReferencePrice` → FALHA.
- [ ] **Passo 3:** implementar.
- [ ] **Passo 4:** rodar → PASSA.
- [ ] **Passo 5: commit** `feat(shipment): classifique o vínculo de NF do SAP por quantidade e valor`.

### Task 7: vínculo permissivo, registro e prévia

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/SapLinkDivergenceException.cs`,
  `SiagroB1.Domain/Dtos/SapLinkPreviewDto.cs`,
  `SiagroB1.Web/Functions/ShipmentLoads/ShipmentLoadsGetSapLinkPreviewController.cs`
- Modify: `ShipmentLoads/ShipmentLoadsLinkSapInvoiceService.cs`, `ShipmentBilling/SapInvoiceLink.cs`,
  `ShipmentBilling/ShipmentBillingCreateSalesInvoiceService.cs:68-105,191-200`,
  `ShipmentLoads/ShipmentLoadsBillingGuardService.cs:45-143`,
  `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsLinkSapInvoiceController.cs`,
  `SiagroB1.Web/ODataConfig/ODataConfigurations.cs:1671-1675,1900`, `SiagroB1.Web/Program.cs`
- Test: `ShipmentLoads/ShipmentLoadsLinkSapInvoiceServiceTests.cs`, `ShipmentLoads/ShipmentLoadsBillingGuardServiceTests.cs`,
  `Sap/SapInvoiceLinkEdmTests.cs`

**Interfaces:**
- Consumes: `SapLinkClassifier.Assess`, `SapLinkReferencePrice.ResolveAsync`, `SalesInvoiceDivergence`,
  `ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(context, key, null)`.
- Produces:

```csharp
public class SapLinkDivergenceException(SapLinkAssessment assessment) : ApplicationException(assessment.Message)
{ public SapLinkAssessment Assessment { get; } = assessment; }

// ShipmentLoadsLinkSapInvoiceService (construtor ganha SalesContractsFixedVolumeService)
public Task<IReadOnlyList<SalesInvoice>> ExecuteAsync(Guid loadKey, IReadOnlyList<int> sapDocEntries,
    Guid salesShipmentReleaseKey, string username, bool confirmDivergence = false);
public Task<SapLinkAssessment> PreviewAsync(Guid loadKey, IReadOnlyList<int> sapDocEntries,
    Guid salesShipmentReleaseKey);

// SapInvoiceLink ganha o último campo: SapLinkType LinkType = SapLinkType.Normal  → ApplySapLink grava
//   SalesInvoice.SapLinkType
// ShipmentBillingCreateSalesInvoiceService.ExecuteAsync(..., CommitMode commitMode = CommitMode.Auto,
//   bool allowLoadExcess = false)
// ShipmentLoadsBillingGuardService.EnsureCanBillAsync(Guid key, decimal quantity, string? carrier,
//   bool allowExcess = false)   → pula SÓ o teste "quantity > available"; as travas de situação e de
//   transportadora continuam

public class SapLinkPreviewDto   // [JsonPropertyName] PascalCase em todas
{
    public int LinkType { get; set; }
    public string? Message { get; set; }
    public decimal EffectiveQuantity { get; set; }
    public decimal InvoiceQuantity { get; set; }
    public decimal QuantityDifference { get; set; }
    public decimal? EffectiveValue { get; set; }
    public decimal InvoiceValue { get; set; }
    public decimal? ValueDifference { get; set; }
}
```

- EDM: `ShipmentLoadsLinkSapInvoice` ganha `ConfirmDivergence` (Boolean **opcional**: ausente = falso — o
  controller lê com `TryGetValue`). Function `ShipmentLoadsGetSapLinkPreview(Key: Guid, SalesShipmentReleaseKey:
  Guid, SapDocEntries: string)` — os DocEntry separados por vírgula — devolve `SapLinkPreviewDto`. Fora do modo SAP
  responde 400, como o controller do vínculo.
- Fluxo do `ExecuteAsync`: some `SumMatchesAvailable` e a mensagem "A soma das notas fiscais...". Validações de
  NF, cliente e liberação como hoje. **Dentro da transação**: reler a QE
  (`new ShipmentLoadsBillingGuardService(db.Context).CalculateAvailableAsync`); QE ≤ 0,001 →
  `A carga {Code} não tem saldo a faturar.`; preço de referência do contrato da liberação; `Assess`; tipo ≠ 0 sem
  `confirmDivergence` → `SapLinkDivergenceException` (rollback, nada gravado); faturar cada NF com
  `allowLoadExcess: true` e o tipo no `SapInvoiceLink`; nos tipos 1 e 2, um `SalesInvoiceDivergence` `Open` por
  documento com os números da parte (`SapLinkShare`) dele; por fim `RecalculateAsync` da carga + `SaveChanges`
  (os registros nascem depois do recálculo que o faturamento já fez) e commit.
- `PreviewAsync`: mesmas validações e mesma conta, sem transação e sem escrita.
- Controller do vínculo: `SapLinkDivergenceException` cai no ramo `ApplicationException` → 400 com o texto do alerta.

- [ ] **Passo 1: testes que falham** (carga de 30.000, liberação de contrato fixo a R$ 2,00/kg, salvo indicação):

```csharp
[Fact] public async Task Matching_invoice_links_as_normal_without_a_divergence_record()
// NF 30000 / 60000.00 → 1 documento, SapLinkType Normal, nenhum registro, carga Invoiced
[Fact] public async Task Smaller_invoice_without_confirmation_is_refused_and_writes_nothing()
// NF 29800 → SapLinkDivergenceException com o texto do tipo 2; nenhum SalesInvoice, nenhum registro, carga Open
[Fact] public async Task Smaller_invoice_confirmed_links_and_closes_the_load()
// confirmDivergence: documento com Quantity 29800 e SapLinkType 2; registro Open QE 30000, QNF 29800, diferença 200,
// VE 60000.00, VNF 59600.00, diferença 400.00; carga InvoicedQuantity 29800, InvoiceDivergenceQuantity 200,
// AvailableQuantity 0, Status Invoiced; alocação do contrato = 29800
[Fact] public async Task Larger_invoice_confirmed_links_with_a_negative_divergence()   // 30150 → -150, Invoiced
[Fact] public async Task Value_divergence_confirmed_records_type_one_with_zero_quantity_difference()
// NF 30000 / 60300.00 → tipo 1, registro com QuantityDifference 0, ValueDifference -300.00
[Fact] public async Task Two_invoices_get_one_record_each_and_the_shares_sum_the_effective_quantity()
// NFs 10000 e 19800 → 2 documentos, 2 registros, Σ EffectiveQuantity = 30000, carga Invoiced
[Fact] public async Task Dollar_contract_classifies_by_quantity_only()
// NF 30000 → Normal sem registro; NF 29800 confirmada → tipo 2 com EffectiveValue e ValueDifference nulos
[Fact] public async Task Load_without_balance_is_refused()
[Fact] public async Task Preview_returns_the_assessment_and_writes_nothing()
[Fact] public async Task Guard_with_allow_excess_still_refuses_a_planned_load_and_a_different_carrier()
// EDM: ShipmentLoadsLinkSapInvoice tem o parâmetro ConfirmDivergence; existe ShipmentLoadsGetSapLinkPreview
```

  Reescrever o teste antigo da soma diferente do saldo (agora é o segundo acima).
- [ ] **Passo 2:** rodar `~ShipmentLoadsLinkSapInvoiceService`, `~ShipmentLoadsBillingGuard`,
  `~SapInvoiceLinkEdm` → FALHA.
- [ ] **Passo 3:** implementar.
- [ ] **Passo 4:** rodar os três filtros + `~ShipmentBilling` → PASSA.
- [ ] **Passo 5: commit** `feat(shipment): vincule NF do SAP com divergência de quantidade e valor`.

### Task 8: registro acompanha cancelamento, exclusão e sincronização

**Files:**
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceDivergenceRules.cs`
- Modify: `SalesInvoices/SalesInvoicesCancelService.cs:36-62,150-158`
- Test: `SalesInvoices/SalesInvoicesCancelServiceTests.cs`, `Sap/SapInvoiceSyncServiceTests.cs`,
  `SalesInvoices/SalesInvoicesDeleteServiceTests.cs` (criar se não existir, no padrão do de cancelamento)

**Interfaces:**
- Produces:

```csharp
public static class SalesInvoiceDivergenceRules
{
    public const string CancelledReason = "Documento de saída cancelado.";
    public const string UnlinkedReason = "Notas fiscais do SAP desvinculadas da carga.";
    /// Marca Voided os registros Open do documento (rastreado, sem SaveChanges).
    public static Task VoidForInvoiceAsync(AppDbContext context, Guid salesInvoiceKey, string userName, string reason);
}

// SalesInvoicesCancelService
/// Cancelamento pelo "Desvincular NFs do SAP", na transação de quem chama.
public Task CancelForUnlinkAsync(Guid key, string userName);
// = CancelAsync(key, userName, afterNfe: false, CommitMode.Deferred) com o motivo UnlinkedReason
```

- Em `CancelAsync`, logo depois de `InvoiceStatus = Cancelled` (antes do `SaveChanges` da linha 177 e, portanto,
  antes de `loadHook.ApplyAsync`): `VoidForInvoiceAsync` com o motivo do caminho. Vale para o cancelamento manual,
  o pós-NF-e, a recusa e a sincronização.
- Exclusão de documento Pendente: o cascade apaga o registro; nada a escrever no serviço. O teste prova que o saldo
  da carga volta inteiro.

- [ ] **Passo 1: testes que falham** (carga 30.000, documento vinculado de 29.800 com registro de 200):

```csharp
[Fact] public async Task Cancelling_a_divergent_document_voids_the_record_and_reopens_the_load()
// registro Voided, VoidReason = CancelledReason, VoidedBy = usuário; carga InvoiceDivergenceQuantity 0,
// InvoicedQuantity 0, AvailableQuantity 30000, Status Open
[Fact] public async Task Cancel_for_unlink_uses_the_unlink_reason_and_does_not_commit()
[Fact] public async Task Deleting_a_pending_divergent_document_removes_the_record_and_restores_the_balance()
[Fact] public async Task Sap_cancellation_voids_the_divergence_and_reopens_the_load()
// SapInvoiceSyncService com OINV.CANCELED = 'Y' → documento Cancelled, registro Voided, carga Open
```

- [ ] **Passo 2:** rodar `~SalesInvoicesCancelService`, `~SalesInvoicesDeleteService`, `~SapInvoiceSync` → FALHA.
- [ ] **Passo 3:** implementar.
- [ ] **Passo 4:** rodar → PASSA.
- [ ] **Passo 5: commit** `feat(invoice): anule a divergência quando o documento é cancelado`.

### Task 9: desvincular as NFs do SAP da carga

**Files:**
- Create: `SiagroB1.Application/Services/ShipmentLoads/ShipmentLoadsUnlinkSapInvoicesService.cs`,
  `SiagroB1.Web/Actions/ShipmentLoads/ShipmentLoadsUnlinkSapInvoicesController.cs`
- Modify: `SiagroB1.Web/ODataConfig/ODataConfigurations.cs` (junto de 1671), `SiagroB1.Web/Program.cs` (~134)
- Test: `ShipmentLoads/ShipmentLoadsUnlinkSapInvoicesServiceTests.cs` (novo), `Sap/SapInvoiceLinkEdmTests.cs`

**Interfaces:**
- Consumes: `SalesInvoicesCancelService.CancelForUnlinkAsync` (Tarefa 8).
- Produces:

```csharp
public class ShipmentLoadsUnlinkSapInvoicesService(IUnitOfWork db, SalesInvoicesCancelService cancel,
    IConfiguration? configuration = null, ILogger<ShipmentLoadsUnlinkSapInvoicesService>? logger = null)
{
    /// Devolve quantos documentos foram cancelados.
    public Task<int> ExecuteAsync(Guid loadKey, string userName);
}
// Action OData ShipmentLoadsUnlinkSapInvoices(Key: Guid) → { count }
```

- Regras, nesta ordem: modo SAP (`Disponível somente com integração SAP Business One.`); carga existe;
  `Discharged`/`Completed` → `Desfaça a descarga e a conferência antes de desvincular as notas fiscais.`; qualquer
  situação que não `Invoiced`/`PartiallyInvoiced` → `A carga {Code} não está faturada: não há notas fiscais do SAP
  para desvincular.`; documentos alvo = Normais da carga com `SapDocEntry` e situação Pending, Confirmed ou
  Returned; nenhum → `A carga {Code} não tem nota fiscal do SAP vinculada.`; algum com devolução não cancelada
  (`SalesInvoiceOriginKey` apontando para ele) → `Estorne a recusa antes de desvincular.`; algum Pending →
  `O documento {InvoiceNumber} está pendente. Confirme-o ou exclua-o antes de desvincular.`
- Uma transação: `CancelForUnlinkAsync` para cada um; qualquer recusa das travas do cancelamento (título baixado,
  mês fechado, quebra de descarga, complemento de ICMS) desfaz tudo e sobe com a mensagem dela.
- Controller no molde de `ShipmentLoadsLinkSapInvoiceController` (serviço resolvido sob demanda, 400 fora do SAP,
  `DbUpdateConcurrencyException` → "A carga foi alterada por outro usuário. Atualize e tente novamente.").

- [ ] **Passo 1: testes que falham.**

```csharp
[Fact] public async Task Unlinks_every_sap_document_of_an_invoiced_load()
// 2 documentos vinculados (um com divergência) → devolve 2; ambos Cancelled; registro Voided com UnlinkedReason;
// carga Open com AvailableQuantity = TotalQuantity; alocações do contrato removidas
[Theory] [InlineData(ShipmentLoadStatus.Discharged)] [InlineData(ShipmentLoadStatus.Completed)]
public async Task Discharged_or_completed_load_is_refused(ShipmentLoadStatus status)
[Fact] public async Task Open_load_is_refused()
[Fact] public async Task Load_with_a_refusal_return_is_refused()            // "Estorne a recusa antes de desvincular."
[Fact] public async Task A_cancel_lock_on_one_document_rolls_back_all()     // 2º documento com título baixado
[Fact] public async Task Manual_documents_of_the_load_are_left_alone()      // sem SapDocEntry: não é cancelado
// EDM: existe a action ShipmentLoadsUnlinkSapInvoices com o parâmetro Key
```

- [ ] **Passo 2:** rodar `~ShipmentLoadsUnlinkSapInvoices` e `~SapInvoiceLinkEdm` → FALHA.
- [ ] **Passo 3:** implementar.
- [ ] **Passo 4:** rodar → PASSA.
- [ ] **Passo 5: commit** `feat(shipment): desvincule as NFs do SAP da carga faturada`.

### Task 10: recusa total movimenta a quantidade efetiva

**Files:**
- Modify: `SalesInvoices/SalesInvoiceDivergenceRules.cs`, `ShipmentLoads/ShipmentLoadsRefuseService.cs:100-156`,
  `ShipmentLoads/ShipmentLoadRefusalCompleteService.cs:40-82`
- Test: `ShipmentLoads/ShipmentLoadsRefuseServiceTests.cs`, `ShipmentLoads/ShipmentLoadsRefuseTransshipmentTests.cs`,
  `ShipmentLoads/ShipmentLoadsRefuseTransitLossTests.cs`, `ShipmentLoads/ShipmentLoadRefusalCompleteTests.cs`

**Interfaces:**
- Produces:

```csharp
/// Σ QuantityDifference dos registros Open dos documentos que esta recusa devolve POR INTEIRO
/// (o que resta do documento depois dela ≤ 0,001). Zero para os demais.
public static Task<decimal> PhysicalAdjustmentAsync(AppDbContext context,
    IReadOnlyCollection<(Guid SalesInvoiceKey, decimal RemainingAfterRefusal)> refusedDocuments);
```

- Nos dois serviços, o `totalQuantity` entregue a `RefusalEffectsInput` passa a ser a soma devolvida **mais** o
  ajuste, só quando o destino é `Warehouse`, `Transshipment` ou `TransitLoss`. O documento de devolução e o
  `ReturnedQuantity` da origem continuam com a quantidade da NF. Antes de editar, ler nos dois serviços como
  `totalQuantity` e o resto de cada documento são calculados e usar o mesmo resto que decide marcar a origem como
  devolvida.

- [ ] **Passo 1: testes que falham** (carga 30.000, documento vinculado de 29.800, registro Open de 200):

```csharp
[Fact] public async Task Full_refusal_to_warehouse_moves_the_effective_quantity()
// devolução de 29800; romaneio de entrada NetWeight 30000; carga ReturnedToWarehouseQuantity 30000,
// InvoicedQuantity 0, InvoiceDivergenceQuantity 0, AvailableQuantity 0, Status Returned
[Fact] public async Task Partial_refusal_to_warehouse_moves_what_was_informed_and_keeps_the_divergence()
// recusa de 10000 → romaneio 10000; InvoiceDivergenceQuantity 200; AvailableQuantity 0
[Fact] public async Task Full_refusal_to_transshipment_sends_the_effective_quantity()   // OutgoingQuantity 30000
[Fact] public async Task Full_refusal_as_transit_loss_loses_the_effective_quantity()     // TransitLossQuantity 30000
[Fact] public async Task Full_refusal_that_follows_the_trip_frees_the_effective_quantity()
// destino "segue viagem": AvailableQuantity 30000, InvoiceDivergenceQuantity 0, sem romaneio
[Fact] public async Task Two_step_refusal_completion_moves_the_effective_quantity()      // RefusalCompleteService
[Fact] public async Task Negative_divergence_reduces_the_physical_quantity()
// NF 30150, registro -150, recusa total para armazém → romaneio 30000
```

- [ ] **Passo 2:** rodar `~ShipmentLoadsRefuse` e `~ShipmentLoadRefusalComplete` → FALHA.
- [ ] **Passo 3:** implementar.
- [ ] **Passo 4:** rodar os filtros + `~ShipmentLoadsCancelRefusal` + `~ShipmentLoadRefusalReopen` → PASSA.
- [ ] **Passo 5: commit** `feat(shipment): movimente a quantidade efetiva na recusa total com divergência`.

### Task 11: suíte inteira do backend

- [ ] `dotnet build SiagroB1.sln` → 0 erros.
- [ ] `dotnet test SiagroB1.Application.Tests` e `dotnet test SiagroB1.Fiscal.Tests` → tudo verde; falha
  pré-existente só conta como tal se também falhar em `main`.
- [ ] Corrigir o que quebrar e commitar na tarefa dona do código.

---

## Frontend (`worktrees/gac-1226-credit-memo/frontend`)

### Task 12: tirar a NF de devolução da tela

**Files:**
- Delete: `webapp/helpers/SapCreditMemoLinkDialog.ts`, `webapp/helpers/SapCreditMemoLinkHelpers.ts`,
  `webapp/test/unit/helpers/SapCreditMemoLinkHelpers.qunit.ts`,
  `webapp/view/shipmentLoads/fragments/SapCreditMemoLinkDialog.fragment.xml`
- Modify: `webapp/controller/shipmentLoads/Detail.controller.ts:20-23,178,226-320`,
  `webapp/view/shipmentLoads/Detail.view.xml:411,430,458,645-695` (tabela "Documentos de devolução", selo
  "Aguardando NF do SAP", `$select` e formatters), `webapp/model/ServerRoutes.ts:~480-484`,
  `webapp/view/salesInvoices/Main.view.xml:48,123`, `webapp/view/salesInvoices/fragments/Form.fragment.xml:95,113`,
  `webapp/test/unit/unitTests.qunit.ts:51`

- [ ] **Passo 1:** apagar e editar. A coluna "Doc. SAP" volta a mostrar só `SapDocNum`. Nenhum `$select` pode
  continuar pedindo `SapCreditMemoDocEntry`/`SapCreditMemoDocNum` (o backend novo responde 400).
- [ ] **Passo 2:** `grep -rn "CreditMemo" webapp` → nenhuma ocorrência.
- [ ] **Passo 3:** `yarn ts-typecheck` e `yarn lint` → sem erro.
- [ ] **Passo 4: commit** `refactor(sap): remova o vínculo da NF de devolução da carga` (stage por caminho).

### Task 13: diálogo de vínculo com prévia e confirmação

**Files:**
- Modify: `webapp/helpers/SapInvoiceLinkHelpers.ts`, `webapp/helpers/SapInvoiceLinkDialog.ts`,
  `webapp/view/shipmentLoads/fragments/SapInvoiceLinkDialog.fragment.xml`,
  `webapp/controller/shipmentLoads/Detail.controller.ts:541-552` (`SapLinkLoad` ganha `CardCode`),
  `webapp/model/ServerRoutes.ts`
- Test: `webapp/test/unit/helpers/SapInvoiceLinkHelpers.qunit.ts`

**Interfaces:**
- Consumes: function `ShipmentLoadsGetSapLinkPreview(Key, SalesShipmentReleaseKey, SapDocEntries)` e o parâmetro
  `ConfirmDivergence` da action (Tarefa 7).
- Produces (em `SapInvoiceLinkHelpers.ts`):

```ts
export type SapLinkPreview = {
  LinkType: number, Message: string, EffectiveQuantity: number, InvoiceQuantity: number,
  QuantityDifference: number, EffectiveValue: number | null, InvoiceValue: number,
  ValueDifference: number | null,
};
export function normalizeLinkPreview(data: unknown): SapLinkPreview | undefined;
/** Só as liberações do cliente da carga; com showOthers, todas, as do cliente da carga primeiro e as demais com OtherCustomer. */
export function releasesForLoad<T extends CustomerRelease>(
  releases: T[], loadCardCode: string, showOthers: boolean): Array<T & { OtherCustomer: boolean }>;
export function canLink(invoices: QuantityRow[], hasRelease: boolean): boolean;   // ≥ 1 NF e liberação escolhida
export function formatMoney(value: number | null): string;                          // "R$ 59.600,00"; "—" quando nulo
```

- Saem de helpers e do diálogo: `CarrierMismatch` (tipo, normalização, coluna e aviso), `matchesAvailable`,
  `exceedsAvailable`, `selectionSummaryText`, `releasesForInvoices`, `shouldKeepRelease`, `hasMixedCustomers` e os
  estados `/SumMatches`, `/SumExceeds`, `/SumBelow`, `/MixedCustomers`.
- Diálogo:
  - `Switch` "Mostrar liberações de outros clientes" acima da tabela de liberações, desligado a cada abertura;
    a lista não depende mais das NFs marcadas.
  - Rodapé: a cada mudança de NFs marcadas ou de liberação, com as duas escolhidas, chama a prévia (descartar
    resposta atrasada com `isStaleRequest`) e mostra `Efetivo`, `NF`, `Diferença` em kg e em R$; valores nulos
    mostram "—". Com `LinkType` 1 ou 2, uma faixa `MessageStrip` de aviso: "Vínculo com divergência de valor" /
    "Vínculo com divergência de quantidade e valor".
  - "Vincular": se a prévia tem `LinkType` 1 ou 2, `MessageBox.confirm(preview.Message, …)` com ações "Sim" e
    "Não"; no "Sim" (ou direto no tipo 0) envia a action com `ConfirmDivergence = LinkType !== 0`. Erro do
    servidor mantém o diálogo aberto com a mensagem dele, como hoje.

- [ ] **Passo 1: testes que falham** (QUnit):

```ts
QUnit.test("releasesForLoad mostra só as do cliente da carga por padrão", …);          // 3 liberações, 1 do cliente
QUnit.test("releasesForLoad com showOthers traz todas, as do cliente primeiro, marcadas", …);
QUnit.test("releasesForLoad sem cliente na carga devolve vazio", …);
QUnit.test("canLink exige NF e liberação e não olha o saldo", …);                       // soma ≠ saldo → true
QUnit.test("normalizeLinkPreview converte Edm.Decimal em número e mantém nulos", …);    // "30000.000" → 30000
QUnit.test("normalizeLinkPreview recusa resposta sem LinkType", …);                     // → undefined
QUnit.test("formatMoney formata em pt-BR e mostra travessão no nulo", …);
```

- [ ] **Passo 2:** `yarn test` → os novos FALHAM.
- [ ] **Passo 3:** implementar helpers, diálogo e fragmento. Sem `--` dentro de comentário XML.
- [ ] **Passo 4:** `yarn ts-typecheck`, `yarn lint`, `yarn test` → verde (a falha de `PaymentReleaseExportHelpers`
  é pré-existente). Matar pelo PID o servidor que o `yarn test` deixar na 8080. Apagar `report/`, `coverage/`,
  `.nyc_output/` se aparecerem.
- [ ] **Passo 5: commit** `feat(shipment): avise e confirme a divergência ao vincular NF do SAP` (stage por caminho).

### Task 14: desvincular e divergências no detalhe da carga

**Files:**
- Modify: `webapp/view/shipmentLoads/Detail.view.xml`, `webapp/controller/shipmentLoads/Detail.controller.ts`,
  `webapp/model/ServerRoutes.ts` (`shipmentLoadsUnlinkSapInvoices: '/odata/ShipmentLoadsUnlinkSapInvoices'`),
  `webapp/helpers/SapInvoiceLinkHelpers.ts`
- Test: `webapp/test/unit/helpers/SapInvoiceLinkHelpers.qunit.ts`

**Interfaces:**
- Produces:

```ts
export function canUnlinkSapInvoices(status: string, sapMode: boolean): boolean;   // Invoiced | PartiallyInvoiced
export function linkTypeText(linkType: number | string): string;
// 0 "Normal", 1 "Valor divergente", 2 "Quantidade e valor divergentes" (aceita o nome do enum que o OData devolve)
export function divergenceStatusText(status: string): string;                      // "Aberta" | "Anulada"
```

- Tela:
  - Botão "Desvincular NFs do SAP" ao lado de "Vincular NF do SAP" (`Detail.view.xml:~42`), visível por
    `canUnlinkSapInvoices`. `onUnlinkSapInvoices`: `MessageBox.confirm("Desvincular as notas fiscais do SAP desta
    carga? Os documentos de saída vinculados serão cancelados.")`; no "Sim", POST com `{ Key }`, toast
    "Notas fiscais do SAP desvinculadas." e `refreshAll`; erro mostra a mensagem do servidor.
  - Cabeçalho: `ObjectStatus` "Divergência de NF" com `InvoiceDivergenceQuantity` em kg, estado `Warning`, visível
    quando diferente de zero (`targetType: 'any'` na expressão).
  - Na seção "Documentos de Saída", tabela "Divergências" (`sap.ui.table.Table`, no padrão das vizinhas, com
    `registerTableLayouts`), `rows` em `InvoiceDivergences` com `$$ownRequest`, `$expand` de
    `SalesInvoice($select=InvoiceNumber,TaxDocumentNumber)` e `$select` explícito; colunas: Documento, NF, Tipo,
    Efetivo (kg), NF (kg), Diferença (kg), Valor efetivo, Valor da NF, Diferença (R$), Situação. Visível só em modo
    SAP. Entra no `refreshAll`.

- [ ] **Passo 1: testes que falham** (QUnit) para os três helpers: `canUnlinkSapInvoices("Invoiced", true)` e
  `("PartiallyInvoiced", true)` verdadeiros; `("Discharged", true)` e `("Invoiced", false)` falsos; os três textos
  de tipo por número e por nome (`"QuantityAndValueDivergent"`); as duas situações.
- [ ] **Passo 2:** `yarn test` → FALHAM.
- [ ] **Passo 3:** implementar.
- [ ] **Passo 4:** `yarn ts-typecheck`, `yarn lint`, `yarn test` → verde; limpar o que o teste gerar.
- [ ] **Passo 5: commit** `feat(shipment): desvincule as NFs do SAP e mostre as divergências na carga`.

---

## Task 15: verificação no navegador

Ambiente: SAP e InvOne de mentira locais, como na verificação anterior do GAC-1226 (memória
`gac-1226-sap-invoice-link`), em **cópias** dos bancos (`IDX_SIAGRO_DEV_GAC1226` + `IDX_SIAGRO_COMMON_GAC1226`),
`Erp = SAPB1`. Antes: acrescentar ao SAP de mentira `INV12.Vehicle`, `OINV.DocCur`, `INV1.unitMsr` e, ao InvOne de
mentira, `Process.XmlEnvAssinado` (deflate cru de um XML com `finNFe`). Aplicar as duas migrations na cópia.

- [ ] 1. Lista: carga com cliente, transportadora e placa mostra só as NFs que casam; NF de outro cliente, de outra
  placa, em TON e a de `finNFe = 2` não aparecem. Carga sem placa → mensagem "Informe a placa…".
- [ ] 2. Liberações: só as do cliente da carga; a chave traz as outras, marcadas.
- [ ] 3. NF igual à carga → rodapé sem diferença, vincula sem pergunta, carga Faturada, tabela de divergências vazia.
- [ ] 4. Desvincular → carga Em aberto, documentos cancelados, NF volta à lista.
- [ ] 5. NF 200 kg menor → rodapé com as diferenças, pergunta do tipo 2; "Não" não grava nada; "Sim" → carga
  Faturada com "Divergência de NF 200", linha na tabela; contrato baixado por 29.800 (conferir por SQL).
- [ ] 6. NF de mesmo peso e valor diferente → pergunta do tipo 1; registro com diferença de valor.
- [ ] 7. NF maior que a carga → vincula com divergência negativa, carga Faturada.
- [ ] 8. Liberação de contrato em dólar → rodapé com "—" no valor; classifica só por quantidade.
- [ ] 9. Recusa total para armazém da carga do item 5 → romaneio de entrada de 30.000, carga Devolvida, saldo 0.
- [ ] 10. `CANCELED = 'Y'` no SAP de mentira + "Sincronizar agora" → documento cancelado, registro Anulado, carga
  reaberta.
- [ ] 11. Carga Descarregada → botão de desvincular some; a action responde 400 com o texto da spec.
- [ ] 12. Console sem 404 de late property e sem erro de binding; `Erp = STANDALONE`: sem botões, as rotas novas
  respondem 400.
- [ ] Derrubar a stack pelo PID e confirmar que as pastas principais continuam em `main`.

## Deploy (não faz parte da execução; registrar na memória ao final)

Migrations `RemoveSalesInvoiceSapCreditMemoLink` e `AddSalesInvoiceDivergences` em todo banco de empresa → backend →
frontend. Sem mudança de appsettings. Na Yokotobi elas rodam depois das três do GAC-1226 ainda não implantadas.
