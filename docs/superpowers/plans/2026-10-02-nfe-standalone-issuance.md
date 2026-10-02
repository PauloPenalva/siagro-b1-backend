# NF-e STANDALONE — sub-projeto 2a: emissão da NF-e de saída — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Na filial com a regra ativa (`Erp=STANDALONE` + "Emite NF-e pelo Siagro"), o documento de saída Pendente é emitido como NF-e modelo 55 na SEFAZ (homologação ou produção), autorizado ⇒ confirmado, com consulta de situação, DANFE em PDF e download do XML; cadastros de emitente, destinatário, municípios, condição de pagamento e certificado A1 completam o que o XML exige.

**Architecture:** Um projeto novo, `SiagroB1.Fiscal` (biblioteca sem banco), concentra o motor de tributos do sub-projeto 1, as parcelas da condição de pagamento, o certificado, a montagem/assinatura/validação do XML com a Zeus e o cliente da SEFAZ atrás de `INfeSefazClient`. A `Application` orquestra: carrega e valida o cadastro (`NfeReadinessValidator`), monta a entrada (`NfeIssueInputAssembler`), reserva o número, grava "Em processamento" com o XML assinado **antes** de enviar e trata o retorno (`SalesInvoiceNfeResultHandler`), chamando a confirmação existente quando autorizada. O `Reports` gera o DANFE com o layout FastReport da Zeus (LGPL, copiado sem alteração).

**Tech Stack:** .NET 10, EF Core 10 (SQL Server), Microsoft.AspNetCore.OData 9.4.1, Dapper, xUnit + EF InMemory, Zeus.Net.NFe.NFCe 2026.9.24.1416, FastReport.OpenSource 2026.1.3; OpenUI5 1.141 + TypeScript (OData v4).

**Spec:** `siagro-b1-backend/docs/superpowers/specs/2026-10-02-nfe-standalone-issuance-design.md`

## Global Constraints

- Código (classes, tabelas, colunas) em inglês; texto que o usuário lê (labels, menus, mensagens de negócio) em **pt-BR**; comentários em pt-BR.
- **Regra ativa** = `Erp == "STANDALONE"` (teste positivo; chave ausente vale STANDALONE) **e** `Branch.IssuesNfe` — sempre perguntada ao `TaxCalculationGate`. Emissão, consulta, travas novas e a recusa da confirmação direta só valem com ela. SAPB1 e STANDALONE com a chave desligada (MH Agro): comportamento idêntico ao de hoje.
- Campos novos de filial, parceiro, endereço e documento: ocultos em SAPB1 (`ui>/standalone`) e validados no servidor só em STANDALONE. Telas novas: menu com `MENU_ITEMS.StandaloneOnly` e controllers que recusam fora do STANDALONE.
- **Homologação:** `dest/xNome` = `NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL` (literal oficial, **sem acento**). A Zeus não faz essa troca.
- `Zeus.Net.NFe.NFCe` **2026.9.24.1416**, referenciada **só** por `SiagroB1.Fiscal` (sai do `SiagroB1.Infra`).
- `SiagroB1.Fiscal` não tem `DbContext`, `IConfiguration` nem acesso a banco; referencia só `SiagroB1.Domain` (enums, modelos, `DefaultException`).
- Certificado A1: `.pfx` no banco; senha cifrada com AES-GCM usando `Nfe:CertificateKey` (base64 de 32 bytes) do appsettings do servidor; a API **nunca** devolve senha nem `.pfx`.
- Mensagens de guarda/negócio: `DefaultException` em pt-BR (o controller devolve 400).
- Valores monetários `DECIMAL(18,2)`; percentuais como percentual (`18.0000` = 18%).
- Migrations: `dotnet ef migrations add <Nome> --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web` (ou `--context CommonDbContext`) a partir de `siagro-b1-backend/`. **Ler a migration gerada antes de seguir.** Nunca aplicar em banco sem `ASPNETCORE_ENVIRONMENT` explícito.
- Novo arquivo ⇒ `git add <arquivo>` imediato no repo dele.
- Commits por tarefa no padrão `tipo(escopo): descrição pt-BR` — escopos deste plano: `platform` (projeto/solução), `master-data` (municípios, filial), `partner`, `financial` (condição de pagamento), `security` (menu), `invoice` (NF-e), `reports` (DANFE). Trailer `DB: <Migration>` quando houver migration. Mensagem termina com:
  ```
  Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6
  ```
  **Sempre com pathspec explícito** (`git commit -m ... -- <arquivos>`): o spec e o plano do sub-projeto 1 estão staged no backend e não entram nestes commits. Nunca push.
- Commit negado pelo classificador do auto mode (já aconteceu, de forma não determinística, com commit que remove ou renomeia arquivo — Tasks 1 e 7): grave a mensagem num arquivo e deixe para o usuário o comando `git commit -F <arquivo> -- <pathspecs>`; siga para a próxima tarefa. Nunca contornar (estreitar o commit até passar, desligar sandbox, executar no lugar de um subagente o que foi negado a ele).
- Branch: backend já em `feature/nfe-standalone-issuance`; frontend nasce na Task 17 de `feature/nfe-standalone-taxation`. Conferir o branch antes de cada commit.
- Frontend: decimal **editável** usa `sap.ui.model.odata.type.Double`; enum em expressão usa `${path: 'X', targetType: 'any'}`; filtro de enum vai como `$filter` estático; `ui>/standalone` é o teste de modo; rotas novas `paymentConditions` e `nfeSettings` (iguais às `MENU_ITEMS.Key`).
- Testes backend (a partir de `siagro-b1-backend/`): `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~<Classe>"` e `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~<Classe>"`. Antes de rodar testes, pare qualquer `SiagroB1.Web` em execução (trava as DLLs).

## Decisões deste plano (desvios conscientes do spec, com o motivo)

| # | Decisão | Motivo |
|---|---|---|
| P1 | `SiagroB1.Fiscal` referencia `SiagroB1.Domain` | O motor usa `TaxRegime`, `UsageModel` e `DefaultException`; duplicá-los criaria dois catálogos. Continua sem banco e sem configuração. |
| P2 | Sem `NfeAccessKey` próprio | `ExtNFe.Assina` já monta `infNFe.Id` e `cDV` via `DFe.Utils.ChaveFiscal`, que aceita CNPJ alfanumérico (ASCII − 48). O plano **testa** a chave com CNPJ alfanumérico em vez de reimplementá-la. |
| P3 | Certificado enviado em **base64 por action OData** | É o padrão real do "anexo do contrato" (`PurchaseContractsAttachmentUpload`); multipart não atravessa o Gateway (só `/odata`, `/security`, `/reports` chegam ao backend). |
| P4 | Assinar e enviar são passos separados (`NfeSigner` + `INfeSefazClient.AuthorizeAsync(SignedNfe)`) | O XML assinado precisa ser gravado **antes** do envio (spec §9.2 passo 5). |
| P5 | `procNFe` composto por texto (XML assinado gravado + `protNFe`) | A parte assinada fica byte a byte igual à transmitida, inclusive na "Consultar situação". |
| P6 | Prévia das parcelas por função do servidor (`PaymentConditionsPreview`) | Um só arredondamento (o do `PaymentInstallmentCalculator`). |
| P7 | Cidade/UF do endereço derivadas do município **no servidor** | A tela grava descrição com group `null` (fora do PATCH); a UF do endereço decide CFOP e tributos e não pode depender da tela. |
| P8 | `ErpMode` desce de `Application/Services/Taxes` para `SiagroB1.Infra` | O menu (projeto `Security`, que não vê a `Application`) precisa do mesmo teste de modo. |
| P9 | `FreightTerms.None` vale **3** no código | Mapeado explicitamente para `mfSemFrete` (9). |
| P10 | `SalesInvoicesConfirmService` ganha `TaxCalculationGate? gate = null` como **último** parâmetro e `ExecuteAsync` vira `virtual` | Os 11 arquivos de teste que o constroem seguem compilando (null = regra inativa); `virtual` permite simular falha da confirmação. |
| P11 | Travas além do §9.4 | NF-e autorizada tranca os dados que foram ao XML; cancelamento recusado com NF-e em processamento/autorizada (cancelar na SEFAZ é 2b); "Informar Nota Fiscal" recusado com `NfeStatus ≠ None`. |
| P12 | "Consultar situação" só com `NfeStatus = Processing` | É o único estado em que o botão aparece. |
| P13 | As actions de emissão/consulta devolvem **200 com o resultado** (situação, código, motivo) para rejeição, denegação e falta de resposta; 400 só para pré-condição e prontidão | Rejeição é desfecho normal, não erro de requisição; a tela mostra o motivo. |
| P14 | Certificado carregado com `MachineKeySet` no Windows e `EphemeralKeySet` nos demais | SChannel não usa chave efêmera em TLS cliente. |
| P15 | `Nfe:ValidateSefazCertificate` (padrão `true`) | A Zeus aceita qualquer certificado de servidor quando a flag é falsa; deixar configurável para o host sem a cadeia ICP-Brasil. |
| P16 | `tPag = 99` leva `xPag = "Outros"`; `cNF ≠ nNF` | Exigências de schema/regra da SEFAZ que o spec não cita. |

## Review Focus

1. **CNPJ/CPF gravado com máscara** (`12.345.678/0001-95`) no parceiro ou na filial — o setter `CNPJ` da Zeus guarda só o primeiro trecho alfanumérico ("12"); esperado: o XML sai só com `[0-9A-Z]` e a prontidão aceita o dado mascarado. Teste na Task 13 (`NfeIssueInputAssemblerTests`).
2. **SEFAZ sem resposta depois do envio** — esperado: o documento fica `Processing` com o XML assinado gravado, e "Consultar situação" autoriza e monta o `procNFe` a partir do XML gravado. Testes nas Tasks 14 e 15.
3. **Confirmação falha depois da autorização** — esperado: NF-e `Authorized` gravada, documento continua Pendente **sem nenhuma alteração parcial da confirmação**, `NfeConfirmationError` preenchido; "Concluir confirmação" resolve. Teste na Task 14.
4. **Documento autorizado, confirmação estornada, depois editado** — esperado: cliente, filial, data, transporte, condição de pagamento, textos e linhas (produto, quantidade, preço, natureza) travados. Teste na Task 15.
5. **Cancelar documento com NF-e autorizada** — esperado: recusa com mensagem (o cancelamento na SEFAZ é o sub-projeto 2b). Teste na Task 15.

---

## Mapa de arquivos

**Backend (`siagro-b1-backend/`)**

| Arquivo | Responsabilidade |
|---|---|
| `SiagroB1.Fiscal/SiagroB1.Fiscal.csproj` (novo) | biblioteca fiscal (Zeus, schemas) |
| `SiagroB1.Fiscal/Taxes/*` (movidos) | motor do sub-projeto 1 |
| `SiagroB1.Fiscal/Payments/PaymentInstallmentCalculator.cs`, `PaymentMeansCodes.cs` (novos) | parcelas e meios de pagamento |
| `SiagroB1.Fiscal/Certificates/CertificatePasswordCipher.cs`, `CertificateLoader.cs`, `CertificateInspector.cs` (novos) | cifra da senha, carga e inspeção do .pfx |
| `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs`, `NfeXmlBuilder.cs`, `NfeSigner.cs`, `NfeZeusConfiguration.cs`, `NfeSefaz.cs`, `ZeusNfeSefazClient.cs`, `NfeProcComposer.cs` (novos) | XML, assinatura, XSD, SEFAZ, procNFe |
| `SiagroB1.Fiscal/Schemas/*.xsd` (novos) | schemas oficiais 010d v1.03 |
| `SiagroB1.Fiscal.Tests/*` (novo) | testes da biblioteca |
| `SiagroB1.Infra/ErpMode.cs` (movido) | teste único do modo |
| `SiagroB1.Domain/Enums/StateRegistrationIndicator.cs`, `PaymentStartRule.cs`, `NfeEnvironment.cs`, `NfeStatus.cs`, `SalesInvoiceNfeXmlKind.cs` (novos) | enums |
| `SiagroB1.Domain/Entities/Municipality.cs`, `PaymentCondition.cs`, `BranchNfeSettings.cs`, `SalesInvoiceNfeXml.cs` (novos) | tabelas novas |
| `SiagroB1.Domain/Entities/Branch.cs`, `BusinessPartner.cs`, `Address.cs`, `SalesInvoice.cs`, `Common/MenuItem.cs` | colunas novas |
| `SiagroB1.Domain/Models/BusinessPartnerModel.cs`, `AddressModel.cs`, `BranchNfeSettingsModel.cs` (novo), `Dtos/Nfe/*` (novos) | API |
| `SiagroB1.Infra/Context/AppDbContext.cs` | DbSets |
| `SiagroB1.Migrations/Seeds/municipalities.txt`, `Seeds/MunicipalitySeed.cs` (novos) | semente IBGE |
| `SiagroB1.Application/Services/MunicipalityService.cs`, `PaymentConditions/PaymentConditionsService.cs` (novos) | cadastros |
| `SiagroB1.Application/Services/Nfe/*` (novos) | configuração, certificado, numeração, prontidão, montagem, emissão, consulta, conclusão, XML |
| `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs` (novo) + serviços existentes do documento | travas |
| `SiagroB1.Application/Services/BranchService.cs`, `BusinessPartnerService.cs`, `BusinessPartnerAddressService.cs` | campos e validação |
| `SiagroB1.Security/Services/MenuService.cs` | filtro `StandaloneOnly` |
| `SiagroB1.Web/Controllers/*`, `Actions/Nfe/*`, `Functions/Nfe/*`, `ODataConfig/ODataConfigurations.cs`, `Extensions/ServiceCollectionExtensions.cs`, `appsettings*.json` | API, EDM, DI, configuração |
| `SiagroB1.Reports/ThirdParty/Zeus-LGPL/*` (novos, sem alteração), `Services/DanfeReportService.cs`, `Controllers/DanfeController.cs` (novos) | DANFE |

**Frontend (`siagro-b1-frontend/webapp/`)**

| Arquivo | Responsabilidade |
|---|---|
| `view/branchs/fragments/Form.fragment.xml` | emitente |
| `view/parceirosNegocio/fragments/Form.fragment.xml`, `Addresses.fragment.xml` | destinatário/transportadora |
| `dialogs/fragments/MunicipalitiesSelectDialog.fragment.xml`, `PaymentConditionsSelectDialog.fragment.xml` (novos), `controller/common/CommonController.ts` | value helps |
| `view/paymentConditions/*`, `controller/paymentConditions/Main.controller.ts` (novos) | condições de pagamento |
| `view/nfeSettings/*`, `controller/nfeSettings/Main.controller.ts` (novos) | configuração da NF-e |
| `helpers/NfeHelpers.ts` (novo), `test/unit/helpers/NfeHelpers.qunit.ts` (novo) | regras puras de tela |
| `view/salesInvoices/Detail.view.xml`, `Main.view.xml`, `fragments/Form.fragment.xml`, `fragments/NfePanel.fragment.xml` (novo), `controller/salesInvoices/*.ts` | emissão no documento |
| `model/ServerRoutes.ts`, `model/formatter.ts`, `manifest.json` | rotas e formatação |

---

### Task 1: Projeto `SiagroB1.Fiscal` — motor de tributos movido e Zeus atualizada

**Files:**
- Create: `SiagroB1.Fiscal/SiagroB1.Fiscal.csproj`, `SiagroB1.Fiscal.Tests/SiagroB1.Fiscal.Tests.csproj`
- Move (git mv): `SiagroB1.Application/Services/Taxes/{TaxCalculator,TaxCalculationModels,FiscalCodes,InterstateIcmsRate,UsageTaxationValidator}.cs` → `SiagroB1.Fiscal/Taxes/`
- Move (git mv): `SiagroB1.Application.Tests/Taxes/{TaxCalculatorTests,InterstateIcmsRateTests}.cs` e `SiagroB1.Application.Tests/Usages/UsageTaxationValidatorTests.cs` → `SiagroB1.Fiscal.Tests/Taxes/`
- Modify: `SiagroB1.sln`, `SiagroB1.Application/SiagroB1.Application.csproj`, `SiagroB1.Infra/SiagroB1.Infra.csproj`, `SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs` e os chamadores listados no Step 5

**Interfaces:**
- Consumes: nada.
- Produces: namespace `SiagroB1.Fiscal.Taxes` com `TaxCalculator`, `TaxCalculationInput/Result`, `IcmsRule`, `PisCofinsRule`, `IbsCbsRule`, `IbsCbsRates`, `FiscalCodes` (+ `public static string? Normalize(string?)`), `InterstateIcmsRate`, `UsageTaxationValidator`; projetos `SiagroB1.Fiscal` e `SiagroB1.Fiscal.Tests` na solução; Zeus 2026.9.24.1416 disponível para quem referencia o `Fiscal`.

Esta tarefa é refatoração pura: **nenhum teste novo**, e a soma dos testes dos dois projetos tem de ser igual à contagem de antes.

- [ ] **Step 1: Registrar a contagem de referência**

Run: `dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Passed!` com o total (2625 no fim do sub-projeto 1). Anote o número: é o alvo do Step 9.

- [ ] **Step 2: Criar os dois projetos**

```bash
dotnet new classlib -n SiagroB1.Fiscal -o SiagroB1.Fiscal -f net10.0
dotnet new xunit -n SiagroB1.Fiscal.Tests -o SiagroB1.Fiscal.Tests -f net10.0
rm SiagroB1.Fiscal/Class1.cs SiagroB1.Fiscal.Tests/UnitTest1.cs
dotnet sln SiagroB1.sln add SiagroB1.Fiscal/SiagroB1.Fiscal.csproj SiagroB1.Fiscal.Tests/SiagroB1.Fiscal.Tests.csproj
```

Substitua o conteúdo de `SiagroB1.Fiscal/SiagroB1.Fiscal.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <LangVersion>14</LangVersion>
        <FileVersion>1.0.116</FileVersion>
        <Company>IDX Consultoria e Sistemas</Company>
        <Product>Siagro B1 Fiscal</Product>
        <Authors>IDX Consultoria e Sistemas</Authors>
        <Copyright>Copyright © 2026 IDX Consultoria e Sistemas</Copyright>
    </PropertyGroup>

    <ItemGroup>
      <ProjectReference Include="..\SiagroB1.Domain\SiagroB1.Domain.csproj" />
    </ItemGroup>

    <ItemGroup>
      <PackageReference Include="Zeus.Net.NFe.NFCe" Version="2026.9.24.1416" />
    </ItemGroup>

</Project>
```

Substitua o conteúdo de `SiagroB1.Fiscal.Tests/SiagroB1.Fiscal.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <IsPackable>false</IsPackable>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="coverlet.collector" Version="6.0.4" />
        <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
        <PackageReference Include="xunit" Version="2.9.3" />
        <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    </ItemGroup>

    <ItemGroup>
        <Using Include="Xunit" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\SiagroB1.Fiscal\SiagroB1.Fiscal.csproj" />
    </ItemGroup>

</Project>
```

```bash
git add SiagroB1.Fiscal/SiagroB1.Fiscal.csproj SiagroB1.Fiscal.Tests/SiagroB1.Fiscal.Tests.csproj
```

- [ ] **Step 3: Zeus sai do Infra; Application passa a referenciar o Fiscal**

Em `SiagroB1.Infra/SiagroB1.Infra.csproj`, apague a linha:

```xml
    <PackageReference Include="Zeus.Net.NFe.NFCe" Version="2026.7.3.2002" />
```

Em `SiagroB1.Application/SiagroB1.Application.csproj`, no `ItemGroup` dos `ProjectReference`, acrescente:

```xml
      <ProjectReference Include="..\SiagroB1.Fiscal\SiagroB1.Fiscal.csproj" />
```

(Nenhum `.cs` usa a Zeus hoje — `PurchaseInvoicesImportXmlService` lê o XML com `XDocument` de propósito.)

- [ ] **Step 4: Mover o motor e trocar o namespace**

```bash
mkdir -p SiagroB1.Fiscal/Taxes
for f in TaxCalculator TaxCalculationModels FiscalCodes InterstateIcmsRate UsageTaxationValidator; do
  git mv SiagroB1.Application/Services/Taxes/$f.cs SiagroB1.Fiscal/Taxes/$f.cs
  sed -i 's/^namespace SiagroB1\.Application\.Services\.Taxes;/namespace SiagroB1.Fiscal.Taxes;/' SiagroB1.Fiscal/Taxes/$f.cs
done
```

O validador usa `UsageTaxationMapper.Normalize`, que é `internal` na `Application` e fica lá (o mapper copia para a entidade). Leve a regra para o catálogo: em `SiagroB1.Fiscal/Taxes/FiscalCodes.cs`, antes do `UsesCsosn`, acrescente:

```csharp
    /// <summary>Select vazio da tela chega como "": no banco o "não informado" é nulo.</summary>
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
```

Em `SiagroB1.Fiscal/Taxes/UsageTaxationValidator.cs`, troque toda ocorrência de `UsageTaxationMapper.Normalize(` por `FiscalCodes.Normalize(`:

```bash
sed -i 's/UsageTaxationMapper\.Normalize(/FiscalCodes.Normalize(/g' SiagroB1.Fiscal/Taxes/UsageTaxationValidator.cs
```

Em `SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs`, o corpo do `Normalize` passa a delegar (as outras chamadas não mudam) e o arquivo ganha `using SiagroB1.Fiscal.Taxes;`:

```csharp
    /// <summary>Mesma regra do catálogo fiscal — ver <see cref="FiscalCodes.Normalize"/>.</summary>
    internal static string? Normalize(string? value) => FiscalCodes.Normalize(value);
```

- [ ] **Step 5: Ajustar os chamadores**

Acrescente `using SiagroB1.Fiscal.Taxes;` (mantendo o `using SiagroB1.Application.Services.Taxes;` onde ele ainda for usado por `TaxCalculationGate`, `ErpMode` ou `IbsCbsRatesService`) em:

- `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesTaxApplyService.cs`
- `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceTaxSnapshot.cs`
- `SiagroB1.Application/Services/UsageService.cs`
- `SiagroB1.Application/Services/Taxes/UsageTaxationMapper.cs`
- `SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceTaxSnapshotTests.cs`

Run: `dotnet build SiagroB1.sln 2>&1 | grep -E "error|Build succeeded" | sort -u | head -30`
Expected: `Build succeeded`. Um `CS0246`/`CS0103` restante aponta o arquivo que ainda precisa do `using SiagroB1.Fiscal.Taxes;` — acrescente e rode de novo.

- [ ] **Step 6: Mover os testes puros**

```bash
mkdir -p SiagroB1.Fiscal.Tests/Taxes
git mv SiagroB1.Application.Tests/Taxes/TaxCalculatorTests.cs SiagroB1.Fiscal.Tests/Taxes/TaxCalculatorTests.cs
git mv SiagroB1.Application.Tests/Taxes/InterstateIcmsRateTests.cs SiagroB1.Fiscal.Tests/Taxes/InterstateIcmsRateTests.cs
git mv SiagroB1.Application.Tests/Usages/UsageTaxationValidatorTests.cs SiagroB1.Fiscal.Tests/Taxes/UsageTaxationValidatorTests.cs
for f in TaxCalculatorTests InterstateIcmsRateTests UsageTaxationValidatorTests; do
  sed -i -e 's/^namespace SiagroB1\.Application\.Tests\.\(Taxes\|Usages\);/namespace SiagroB1.Fiscal.Tests.Taxes;/' \
         -e 's/^using SiagroB1\.Application\.Services\.Taxes;/using SiagroB1.Fiscal.Taxes;/' \
         SiagroB1.Fiscal.Tests/Taxes/$f.cs
done
```

Os três só usam o motor, `SiagroB1.Domain.Enums`, `SiagroB1.Domain.Models` e `SiagroB1.Domain.Exceptions` — nenhum toca banco.

- [ ] **Step 7: Build da solução**

Run: `dotnet build SiagroB1.sln 2>&1 | grep -E " error |Build succeeded" | sort -u | head`
Expected: `Build succeeded`.

Se a remoção da Zeus do Infra derrubar algum projeto (dependência transitiva que alguém usava sem declarar), o erro nomeia o tipo; acrescente o pacote dele no projeto que o usa, nunca a Zeus de volta no Infra.

- [ ] **Step 8: Rodar as duas suítes**

Run: `dotnet test SiagroB1.Fiscal.Tests 2>&1 | tail -3`
Expected: `Passed!`, total = testes de `TaxCalculatorTests` + `InterstateIcmsRateTests` + `UsageTaxationValidatorTests`.

Run: `dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Passed!`.

- [ ] **Step 9: Conferir que nenhum teste sumiu**

A soma dos dois totais do Step 8 tem de ser **igual** ao número anotado no Step 1. Diferença ⇒ algum teste deixou de ser descoberto (namespace ou arquivo fora do projeto); corrija antes de commitar.

- [ ] **Step 10: Commit**

```bash
git add SiagroB1.sln SiagroB1.Fiscal SiagroB1.Fiscal.Tests SiagroB1.Application SiagroB1.Application.Tests SiagroB1.Infra/SiagroB1.Infra.csproj
git commit -m "refactor(platform): mover o motor de tributos para o projeto SiagroB1.Fiscal" -m "A emissão da NF-e (sub-projeto 2a) concentra a parte fiscal numa biblioteca sem banco. O motor do sub-projeto 1 muda de casa sem mudar de comportamento: os mesmos testes passam, agora no SiagroB1.Fiscal.Tests. A Zeus sai do Infra, onde nunca foi usada, e entra no Fiscal já na 2026.9.24.1416 (schemas 010d v1.03, CNPJ alfanumérico).

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.sln SiagroB1.Fiscal SiagroB1.Fiscal.Tests SiagroB1.Application SiagroB1.Application.Tests SiagroB1.Infra/SiagroB1.Infra.csproj
```

---

### Task 2: Municípios do IBGE (`MUNICIPALITIES`)

**Files:**
- Create: `SiagroB1.Domain/Entities/Municipality.cs`
- Create: `SiagroB1.Migrations/Seeds/municipalities.txt` (gerado), `SiagroB1.Migrations/Seeds/MunicipalitySeed.cs`
- Create: `SiagroB1.Application/Services/MunicipalityService.cs`, `SiagroB1.Web/Controllers/MunicipalitiesController.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`, `SiagroB1.Migrations/SiagroB1.Migrations.csproj`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Create (gerada + editada): migration `CreateMunicipalities`
- Test: `SiagroB1.Application.Tests/Municipalities/MunicipalitySeedTests.cs`

**Interfaces:**
- Produces: entidade `Municipality { string Code (PK, VARCHAR(7)); string Name (VARCHAR(100)); string StateAbbreviation (VARCHAR(2)) }`; `DbSet<Municipality> Municipalities`; `MunicipalitySeed.Read() : IReadOnlyList<(string Code, string Name, string State)>` e `MunicipalitySeed.InsertBatches(int batchSize = 500) : IEnumerable<string>`; OData `Municipalities` (só leitura).

- [ ] **Step 1: Gerar o arquivo da semente**

```bash
mkdir -p SiagroB1.Migrations/Seeds
IBGE=/c/Projetos/EfisCloud/backend/src/main/resources/ibge
awk -F'|' 'NR==FNR { sub(/\r$/, ""); uf[$1]=$2; next } { sub(/\r$/, ""); print $1 "|" $2 "|" uf[substr($1,1,2)] }' \
  "$IBGE/uf.txt" "$IBGE/cidades.txt" > SiagroB1.Migrations/Seeds/municipalities.txt
wc -l SiagroB1.Migrations/Seeds/municipalities.txt
awk -F'|' '$3=="" || length($1)!=7' SiagroB1.Migrations/Seeds/municipalities.txt | wc -l
grep -c "^3550308|São Paulo|SP$" SiagroB1.Migrations/Seeds/municipalities.txt
git add SiagroB1.Migrations/Seeds/municipalities.txt
```

Expected: `5574`, `0` e `1`.

- [ ] **Step 2: Write the failing test**

```csharp
// SiagroB1.Application.Tests/Municipalities/MunicipalitySeedTests.cs
using SiagroB1.Migrations.Seeds;

namespace SiagroB1.Application.Tests.Municipalities;

/// <summary>
/// A semente dos municípios vem do arquivo do IBGE que o EfisCloud já usa em produção. O código
/// do município é o <c>cMun</c> da NF-e e os 2 primeiros dígitos são o <c>cUF</c> — um código
/// errado aqui é rejeição na SEFAZ.
/// </summary>
public class MunicipalitySeedTests
{
    [Fact]
    public void Seed_has_all_5574_municipalities_with_unique_seven_digit_codes()
    {
        var rows = MunicipalitySeed.Read();

        Assert.Equal(5574, rows.Count);
        Assert.All(rows, r => Assert.Matches("^[0-9]{7}$", r.Code));
        Assert.Equal(rows.Count, rows.Select(r => r.Code).Distinct().Count());
    }

    [Fact]
    public void Every_municipality_has_the_state_of_its_ibge_prefix()
    {
        var rows = MunicipalitySeed.Read();

        Assert.Equal(27, rows.Select(r => r.State).Distinct().Count());
        Assert.Contains(rows, r => r is { Code: "3550308", Name: "São Paulo", State: "SP" });
        Assert.Contains(rows, r => r is { Code: "5300108", Name: "Brasília", State: "DF" });
        Assert.Contains(rows, r => r is { Code: "3522406", State: "SP" }); // Itapeva
    }

    [Fact]
    public void Insert_batches_escape_apostrophes_and_cover_every_row()
    {
        var batches = MunicipalitySeed.InsertBatches(500).ToList();

        Assert.Equal(12, batches.Count); // 5574 / 500 = 11,1
        Assert.Contains(batches, b => b.Contains("N'Alta Floresta D''Oeste'"));
        Assert.All(batches, b => Assert.StartsWith("INSERT INTO MUNICIPALITIES (Code, Name, StateAbbreviation) VALUES", b));
    }
}
```

```bash
git add SiagroB1.Application.Tests/Municipalities/MunicipalitySeedTests.cs
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~MunicipalitySeedTests"`
Expected: FAIL de compilação — `SiagroB1.Migrations.Seeds` não existe.

- [ ] **Step 4: Implementar a semente**

Em `SiagroB1.Migrations/SiagroB1.Migrations.csproj`, acrescente:

```xml
    <ItemGroup>
      <EmbeddedResource Include="Seeds\municipalities.txt" LogicalName="SiagroB1.Migrations.Seeds.municipalities.txt" />
    </ItemGroup>
```

```csharp
// SiagroB1.Migrations/Seeds/MunicipalitySeed.cs
using System.Text;

namespace SiagroB1.Migrations.Seeds;

/// <summary>
/// Municípios do IBGE (<c>código|nome|UF</c>), embarcados no assembly das migrations. O arquivo
/// nasceu do <c>cidades.txt</c> + <c>uf.txt</c> do EfisCloud. Lido pela migration
/// <c>CreateMunicipalities</c>: 5.574 <c>InsertData</c> dentro do .cs da migration seriam ilegíveis.
/// </summary>
public static class MunicipalitySeed
{
    private const string ResourceName = "SiagroB1.Migrations.Seeds.municipalities.txt";

    public static IReadOnlyList<(string Code, string Name, string State)> Read()
    {
        using var stream = typeof(MunicipalitySeed).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"Recurso {ResourceName} não encontrado.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var rows = new List<(string Code, string Name, string State)>();

        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split('|');
            rows.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim()));
        }

        return rows;
    }

    /// <summary>
    /// INSERTs de até <paramref name="batchSize"/> linhas (o SQL Server aceita até 1000 por
    /// <c>VALUES</c>). <c>N'...'</c> preserva os acentos; o apóstrofo é dobrado.
    /// </summary>
    public static IEnumerable<string> InsertBatches(int batchSize = 500) =>
        Read()
            .Chunk(batchSize)
            .Select(chunk =>
                "INSERT INTO MUNICIPALITIES (Code, Name, StateAbbreviation) VALUES " +
                string.Join(", ", chunk.Select(r => $"('{r.Code}', N'{r.Name.Replace("'", "''")}', '{r.State}')")) +
                ";");
}
```

```bash
git add SiagroB1.Migrations/Seeds/MunicipalitySeed.cs
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~MunicipalitySeedTests"`
Expected: PASS (3 testes).

- [ ] **Step 6: Entidade, DbSet, serviço e controller**

```csharp
// SiagroB1.Domain/Entities/Municipality.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Município do IBGE — <c>cMun</c>/<c>xMun</c> da NF-e. Tabela de referência, somente leitura,
/// semeada pela migration. Os 2 primeiros dígitos do código são o <c>cUF</c>.
/// </summary>
[Table("MUNICIPALITIES")]
public class Municipality
{
    [Key]
    [Column(TypeName = "VARCHAR(7)")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(2) NOT NULL")]
    public required string StateAbbreviation { get; set; }
}
```

Em `SiagroB1.Infra/Context/AppDbContext.cs`, junto dos outros `DbSet`:

```csharp
    public DbSet<Municipality> Municipalities { get; set; }
```

```csharp
// SiagroB1.Application/Services/MunicipalityService.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

/// <summary>Municípios do IBGE, só leitura (pesquisa das telas de filial e parceiro).</summary>
public class MunicipalityService(IUnitOfWork db)
{
    public IQueryable<Municipality> QueryAll() => db.Context.Municipalities.AsNoTracking();

    public Task<Municipality?> GetByIdAsync(string code) =>
        db.Context.Municipalities.AsNoTracking().FirstOrDefaultAsync(m => m.Code == code);
}
```

```csharp
// SiagroB1.Web/Controllers/MunicipalitiesController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services;

namespace SiagroB1.Web.Controllers;

/// <summary>Municípios do IBGE — somente leitura, nos dois modos (é dado de referência).</summary>
public class MunicipalitiesController(MunicipalityService service) : ODataController
{
    [EnableQuery(PageSize = 200)]
    public IActionResult Get() => Ok(service.QueryAll());

    [EnableQuery]
    public async Task<IActionResult> Get([FromRoute] string key)
    {
        var municipality = await service.GetByIdAsync(key.Trim('\''));

        return municipality is null ? NotFound() : Ok(municipality);
    }
}
```

Em `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, ao lado de `modelBuilder.EntitySet<IbsCbsRate>("IbsCbsRates");`:

```csharp
        modelBuilder.EntitySet<Municipality>("Municipalities");
```

Em `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, ao lado de `services.AddScoped<IbsCbsRatesService>();`:

```csharp
        services.AddScoped<MunicipalityService>();
```

```bash
git add SiagroB1.Domain/Entities/Municipality.cs SiagroB1.Application/Services/MunicipalityService.cs SiagroB1.Web/Controllers/MunicipalitiesController.cs
```

- [ ] **Step 7: Migration com a semente**

Run: `dotnet ef migrations add CreateMunicipalities --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`

Leia o arquivo gerado: só pode criar a tabela `MUNICIPALITIES` (3 colunas, PK `Code`). Depois do `CreateTable` no `Up`, acrescente:

```csharp
            // 5.574 municípios do IBGE, a partir do recurso embarcado (ver MunicipalitySeed).
            foreach (var sql in SiagroB1.Migrations.Seeds.MunicipalitySeed.InsertBatches())
            {
                migrationBuilder.Sql(sql);
            }
```

O `Down` (`DropTable`) já leva os dados junto.

Run: `dotnet ef migrations script --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web --idempotent -o "$TEMP/create-municipalities.sql" && grep -c "INSERT INTO MUNICIPALITIES" "$TEMP/create-municipalities.sql"`
Expected: `12`.

```bash
git add SiagroB1.Migrations/AppContext/*_CreateMunicipalities*.cs SiagroB1.Migrations/AppContext/AppDbContextModelSnapshot.cs
```

- [ ] **Step 8: Build e suíte**

Run: `dotnet build SiagroB1.sln 2>&1 | grep -E " error |Build succeeded" | sort -u` e `dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Build succeeded` e `Passed!`.

- [ ] **Step 9: Commit**

```bash
git commit -m "feat(master-data): cadastrar os municípios do IBGE para a NF-e" -m "O cMun/xMun do emitente, do destinatário e do local de entrega precisam do código oficial do município. A tabela nasce semeada com os 5.574 municípios do arquivo que o EfisCloud usa em produção, embarcado no assembly das migrations.

DB: CreateMunicipalities

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Domain/Entities/Municipality.cs SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Migrations SiagroB1.Application/Services/MunicipalityService.cs SiagroB1.Web/Controllers/MunicipalitiesController.cs SiagroB1.Web/ODataConfig/ODataConfigurations.cs SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Application.Tests/Municipalities
```

---

### Task 3: Filial — dados do emitente

**Files:**
- Modify: `SiagroB1.Domain/Entities/Branch.cs`, `SiagroB1.Application/Services/BranchService.cs`
- Create (gerada): migration `AddBranchIssuerFields`
- Test: `SiagroB1.Application.Tests/Branches/BranchIssuerFieldsTests.cs`

**Interfaces:**
- Consumes: `Municipality` (Task 2).
- Produces: `Branch.LegalName`, `TradeName`, `StateRegistration`, `Street`, `StreetNumber`, `Complement`, `District`, `MunicipalityCode`, `ZipCode`, `Phone` (todas `string?`) e navegação `Branch.Municipality`.

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Application.Tests/Branches/BranchIssuerFieldsTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Branches;

/// <summary>
/// Dados do emitente da NF-e na filial (só STANDALONE). O município manda no cUF e no cMun do
/// XML, então precisa ser da mesma UF da filial — senão a SEFAZ rejeita.
/// </summary>
public class BranchIssuerFieldsTests
{
    private static BranchService Service(UnitOfWork db, string erp) => new(db.Context, TaxTestServices.Config(erp));

    private static async Task<UnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Municipalities.AddRange(
            new Municipality { Code = "3522406", Name = "Itapeva", StateAbbreviation = "SP" },
            new Municipality { Code = "4106902", Name = "Curitiba", StateAbbreviation = "PR" });
        await db.SaveChangesAsync();
        return db;
    }

    private static Branch NewBranch(string? municipality = "3522406", string state = "SP") => new()
    {
        Code = "01", BranchName = "MATRIZ", ShortName = "MTZ", TaxId = "68583898000101", StateCode = state,
        LegalName = "CEAGUI CEREAIS LTDA", TradeName = "CEAGUI", StateRegistration = "371012345110",
        Street = "RODOVIA SP 258", StreetNumber = "KM 290", District = "ZONA RURAL",
        MunicipalityCode = municipality, ZipCode = "18400000", Phone = "1535261234",
    };

    [Fact]
    public async Task Issuer_fields_are_persisted()
    {
        var db = await SeedAsync();

        await Service(db, "STANDALONE").CreateAsync(NewBranch());

        var saved = await db.Context.Branchs.AsNoTracking().SingleAsync();
        Assert.Equal("CEAGUI CEREAIS LTDA", saved.LegalName);
        Assert.Equal("371012345110", saved.StateRegistration);
        Assert.Equal("3522406", saved.MunicipalityCode);
        Assert.Equal("18400000", saved.ZipCode);
    }

    [Fact]
    public async Task Standalone_rejects_municipality_from_another_state()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, "STANDALONE").CreateAsync(NewBranch(municipality: "4106902")));

        Assert.Contains("Curitiba", ex.Message);
        Assert.Contains("PR", ex.Message);
    }

    [Fact]
    public async Task Standalone_rejects_unknown_municipality()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, "STANDALONE").CreateAsync(NewBranch(municipality: "9999999")));

        Assert.Contains("9999999", ex.Message);
    }

    [Fact]
    public async Task Standalone_rejects_zip_code_without_eight_digits()
    {
        var db = await SeedAsync();
        var branch = NewBranch();
        branch.ZipCode = "18400-00";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db, "STANDALONE").CreateAsync(branch));

        Assert.Contains("CEP", ex.Message);
    }

    [Fact]
    public async Task Sapb1_does_not_validate_issuer_fields()
    {
        var db = await SeedAsync();

        await Service(db, "SAPB1").CreateAsync(NewBranch(municipality: "4106902"));

        Assert.Equal(1, await db.Context.Branchs.CountAsync());
    }
}
```

```bash
git add SiagroB1.Application.Tests/Branches/BranchIssuerFieldsTests.cs
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BranchIssuerFieldsTests"`
Expected: FAIL de compilação — `Branch` não tem `LegalName`.

- [ ] **Step 3: Colunas na entidade**

Em `SiagroB1.Domain/Entities/Branch.cs`, depois de `IssuesNfe`:

```csharp
        // --- Emitente da NF-e STANDALONE (ocultos e não validados em SAPB1). ---

        /// <summary>Razão social do emitente (<c>emit/xNome</c>). O <see cref="BranchName"/> é rótulo interno.</summary>
        [Column(TypeName = "VARCHAR(60)")]
        public string? LegalName { get; set; }

        /// <summary>Nome fantasia (<c>xFant</c>).</summary>
        [Column(TypeName = "VARCHAR(60)")]
        public string? TradeName { get; set; }

        /// <summary>Inscrição estadual (<c>IE</c>).</summary>
        [Column(TypeName = "VARCHAR(14)")]
        public string? StateRegistration { get; set; }

        [Column(TypeName = "VARCHAR(60)")]
        public string? Street { get; set; }

        [Column(TypeName = "VARCHAR(60)")]
        public string? StreetNumber { get; set; }

        [Column(TypeName = "VARCHAR(60)")]
        public string? Complement { get; set; }

        /// <summary>Bairro (<c>xBairro</c>).</summary>
        [Column(TypeName = "VARCHAR(60)")]
        public string? District { get; set; }

        /// <summary>Município do IBGE: dá o <c>cMun</c>/<c>xMun</c> e, nos 2 primeiros dígitos, o <c>cUF</c>.</summary>
        [Column(TypeName = "VARCHAR(7)")]
        [ForeignKey(nameof(Municipality))]
        public string? MunicipalityCode { get; set; }

        public virtual Municipality? Municipality { get; set; }

        /// <summary>CEP, 8 dígitos sem máscara.</summary>
        [Column(TypeName = "VARCHAR(8)")]
        public string? ZipCode { get; set; }

        [Column(TypeName = "VARCHAR(14)")]
        public string? Phone { get; set; }
```

- [ ] **Step 4: Validação no serviço**

Em `SiagroB1.Application/Services/BranchService.cs`, nos dois métodos que já chamam `ValidateNfeIssuance(entity);` (`CreateAsync` e `UpdateAsync`), acrescente logo depois:

```csharp
        await ValidateIssuerFieldsAsync(entity);
```

E o método novo, ao lado de `ValidateNfeIssuance`:

```csharp
    /// <summary>
    /// Coerência dos dados do emitente (só STANDALONE): o município dá o cUF/cMun do XML e tem de
    /// ser da UF da filial; o CEP vai sem máscara. Nada aqui é obrigatório — quem exige o cadastro
    /// completo é a prontidão da emissão.
    /// </summary>
    private async Task ValidateIssuerFieldsAsync(Branch entity)
    {
        if (!ErpMode.IsStandalone(configuration))
            return;

        if (!string.IsNullOrWhiteSpace(entity.ZipCode) && !System.Text.RegularExpressions.Regex.IsMatch(entity.ZipCode, "^[0-9]{8}$"))
            throw new DefaultException("O CEP da filial deve ter 8 dígitos, sem traço.");

        if (string.IsNullOrWhiteSpace(entity.MunicipalityCode))
            return;

        var municipality = await context.Municipalities.AsNoTracking()
                               .FirstOrDefaultAsync(m => m.Code == entity.MunicipalityCode)
                           ?? throw new DefaultException($"Município {entity.MunicipalityCode} não encontrado.");

        if (!string.Equals(municipality.StateAbbreviation, entity.StateCode, StringComparison.OrdinalIgnoreCase))
            throw new DefaultException(
                $"O município {municipality.Name} é de {municipality.StateAbbreviation}, mas a UF da filial é {entity.StateCode}.");
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~Branch"`
Expected: PASS (`BranchIssuerFieldsTests` e os antigos `BranchServiceIssuesNfeTests`).

- [ ] **Step 6: Migration**

Run: `dotnet ef migrations add AddBranchIssuerFields --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`

Leia: só `AddColumn` anuláveis em `BRANCHS` (10 colunas), a FK `FK_BRANCHS_MUNICIPALITIES_MunicipalityCode` e o índice dela. Nada de `AlterColumn` em coluna existente.

```bash
git add SiagroB1.Migrations/AppContext/*_AddBranchIssuerFields*.cs
```

- [ ] **Step 7: Suíte**

Run: `dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Passed!`.

- [ ] **Step 8: Commit**

```bash
git commit -m "feat(master-data): guardar na filial os dados do emitente da NF-e" -m "Razão social, IE, endereço e município do emitente entram no XML. Só STANDALONE valida: o município tem de ser da UF da filial (é dele que saem o cUF e o cMun) e o CEP vai sem máscara. A obrigatoriedade fica para a prontidão da emissão.

DB: AddBranchIssuerFields

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Domain/Entities/Branch.cs SiagroB1.Application/Services/BranchService.cs SiagroB1.Migrations SiagroB1.Application.Tests/Branches/BranchIssuerFieldsTests.cs
```

---

### Task 4: Parceiro e endereço — destinatário e transportadora

**Files:**
- Create: `SiagroB1.Domain/Enums/StateRegistrationIndicator.cs`, `SiagroB1.Application/Services/AddressMunicipalityResolver.cs`
- Modify: `SiagroB1.Domain/Entities/BusinessPartner.cs`, `Entities/Address.cs`, `Models/BusinessPartnerModel.cs`, `Models/AddressModel.cs`, `SiagroB1.Application/Services/BusinessPartnerService.cs`, `BusinessPartnerAddressService.cs`
- Create (gerada): migration `AddBusinessPartnerNfeFields`
- Test: `SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerNfeFieldsTests.cs`

**Interfaces:**
- Consumes: `Municipality` (Task 2).
- Produces: `enum StateRegistrationIndicator { Taxpayer = 1, Exempt = 2, NonTaxpayer = 9 }`; em `BusinessPartner` e `BusinessPartnerModel`: `StateRegistration`, `StateRegistrationIndicator?`, `NfeEmail`, `Phone`, `int? PaymentConditionCode`; em `Address` e `AddressModel`: `StreetNumber`, `Complement`, `MunicipalityCode`; `AddressMunicipalityResolver.ApplyAsync(AppDbContext, Address)`.

O `BusinessPartnerService` (local) só é registrado no modo STANDALONE (`AddStandAloneServices`); em SAPB1 o parceiro vem do `Services/SAP/BusinessPartnerService`, cujas projeções são inicializadores de objeto — as propriedades novas ficam nulas sem tocar nele.

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerNfeFieldsTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.BusinessPartners;

/// <summary>
/// Campos que a NF-e exige do destinatário e da transportadora (só STANDALONE — o serviço local
/// só existe nesse modo). A cidade e a UF do endereço passam a vir do município: é a UF do
/// endereço de faturamento que decide CFOP e tributos.
/// </summary>
public class BusinessPartnerNfeFieldsTests
{
    private static BusinessPartnerService Partners(UnitOfWork db) =>
        new(db, NullLogger<BusinessPartnerService>.Instance, new FakeStringLocalizer<Resource>());

    private static BusinessPartnerAddressService Addresses(UnitOfWork db) =>
        new(db, NullLogger<BusinessPartnerAddressService>.Instance, new FakeStringLocalizer<Resource>());

    private static async Task<UnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Municipalities.Add(new Municipality { Code = "2927408", Name = "Salvador", StateAbbreviation = "BA" });
        await db.SaveChangesAsync();
        return db;
    }

    private static BusinessPartnerModel Customer(StateRegistrationIndicator? indicator = StateRegistrationIndicator.Taxpayer,
        string? ie = "123456789") => new()
    {
        CardCode = "C001", CardName = "CLIENTE BA LTDA", CardType = "C", TaxId = "11222333000181",
        StateRegistration = ie, StateRegistrationIndicator = indicator, NfeEmail = "nfe@cliente.com.br",
        Phone = "71999990000", PaymentConditionCode = 3,
        Addresses =
        [
            new AddressModel
            {
                AddressName = "FATURAMENTO", AdresType = "B", Street = "AV SETE", StreetNumber = "100",
                Complement = "SALA 2", Block = "CENTRO", ZipCode = "40000000", MunicipalityCode = "2927408",
                City = "digitado errado", State = "XX", Country = "BR",
            },
        ],
    };

    [Fact]
    public async Task Partner_nfe_fields_round_trip()
    {
        var db = await SeedAsync();

        await Partners(db).CreateAsync(Customer());
        var read = await Partners(db).GetByIdAsync("C001");

        Assert.Equal("123456789", read!.StateRegistration);
        Assert.Equal(StateRegistrationIndicator.Taxpayer, read.StateRegistrationIndicator);
        Assert.Equal("nfe@cliente.com.br", read.NfeEmail);
        Assert.Equal("71999990000", read.Phone);
        Assert.Equal(3, read.PaymentConditionCode);

        var address = Assert.Single(read.Addresses);
        Assert.Equal("100", address.StreetNumber);
        Assert.Equal("SALA 2", address.Complement);
        Assert.Equal("2927408", address.MunicipalityCode);
    }

    [Fact]
    public async Task City_and_state_come_from_the_municipality_on_create()
    {
        var db = await SeedAsync();

        await Partners(db).CreateAsync(Customer());

        var saved = await db.Context.Set<Address>().AsNoTracking().SingleAsync();
        Assert.Equal("Salvador", saved.City);
        Assert.Equal("BA", saved.State);
    }

    [Fact]
    public async Task Taxpayer_requires_state_registration()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Partners(db).CreateAsync(Customer(ie: null)));

        Assert.Contains("inscrição estadual", ex.Message);
    }

    [Fact]
    public async Task Taxpayer_state_registration_must_be_digits()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Partners(db).CreateAsync(Customer(ie: "ISENTO")));

        Assert.Contains("só dígitos", ex.Message);
    }

    [Fact]
    public async Task Non_taxpayer_without_state_registration_is_valid()
    {
        var db = await SeedAsync();

        await Partners(db).CreateAsync(Customer(StateRegistrationIndicator.NonTaxpayer, ie: null));

        Assert.Equal(1, await db.Context.BusinessPartners.CountAsync());
    }

    [Fact]
    public async Task Update_persists_partner_nfe_fields()
    {
        var db = await SeedAsync();
        await Partners(db).CreateAsync(Customer());

        var changed = Customer(StateRegistrationIndicator.Exempt, ie: null);
        changed.NfeEmail = "fiscal@cliente.com.br";
        await Partners(db).UpdateAsync("C001", changed);

        var saved = await db.Context.BusinessPartners.AsNoTracking().SingleAsync();
        Assert.Equal(StateRegistrationIndicator.Exempt, saved.StateRegistrationIndicator);
        Assert.Equal("fiscal@cliente.com.br", saved.NfeEmail);
    }

    [Fact]
    public async Task Address_service_derives_city_and_state_and_keeps_number()
    {
        var db = await SeedAsync();
        db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = "C002", CardName = "OUTRO" });
        await db.SaveChangesAsync();

        await Addresses(db).Create("C002", new AddressModel
        {
            AddressName = "ENTREGA", AdresType = "S", Street = "RUA A", StreetNumber = "S/N",
            MunicipalityCode = "2927408",
        });

        var saved = await db.Context.Set<Address>().AsNoTracking().SingleAsync(a => a.CardCode == "C002");
        Assert.Equal("S/N", saved.StreetNumber);
        Assert.Equal("Salvador", saved.City);
        Assert.Equal("BA", saved.State);
    }

    [Fact]
    public async Task Unknown_municipality_is_rejected()
    {
        var db = await SeedAsync();
        var customer = Customer();
        customer.Addresses.Single().MunicipalityCode = "0000000";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Partners(db).CreateAsync(customer));

        Assert.Contains("0000000", ex.Message);
    }
}
```

```bash
git add SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerNfeFieldsTests.cs
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BusinessPartnerNfeFieldsTests"`
Expected: FAIL de compilação — `StateRegistrationIndicator` não existe.

- [ ] **Step 3: Enum, colunas e modelos**

```csharp
// SiagroB1.Domain/Enums/StateRegistrationIndicator.cs
namespace SiagroB1.Domain.Enums;

/// <summary>
/// Indicador da IE do destinatário (<c>indIEDest</c>). Os valores são os da SEFAZ.
/// <see cref="NonTaxpayer"/> também liga <c>ide/indFinal = 1</c> (consumidor final).
/// </summary>
public enum StateRegistrationIndicator
{
    Taxpayer = 1,
    Exempt = 2,
    NonTaxpayer = 9,
}
```

Em `SiagroB1.Domain/Entities/BusinessPartner.cs`, depois de `Notes` (acrescente `using SiagroB1.Domain.Enums;`):

```csharp
    // --- Destinatário/transportadora da NF-e STANDALONE (nulos em SAPB1). ---

    [Column(TypeName = "VARCHAR(14)")]
    public string? StateRegistration { get; set; }

    public StateRegistrationIndicator? StateRegistrationIndicator { get; set; }

    [Column(TypeName = "VARCHAR(250)")]
    public string? NfeEmail { get; set; }

    [Column(TypeName = "VARCHAR(14)")]
    public string? Phone { get; set; }

    /// <summary>Condição de pagamento padrão do documento de saída. Sem FK, como o spec pede.</summary>
    public int? PaymentConditionCode { get; set; }
```

Em `SiagroB1.Domain/Entities/Address.cs`, depois de `Country`:

```csharp
    [Column(TypeName = "VARCHAR(60)")]
    public string? StreetNumber { get; set; }

    [Column(TypeName = "VARCHAR(60)")]
    public string? Complement { get; set; }

    /// <summary>Município do IBGE. Quando preenchido, <see cref="City"/> e <see cref="State"/> vêm dele.</summary>
    [Column(TypeName = "VARCHAR(7)")]
    [ForeignKey(nameof(Municipality))]
    public string? MunicipalityCode { get; set; }

    public virtual Municipality? Municipality { get; set; }
```

Em `SiagroB1.Domain/Models/BusinessPartnerModel.cs`, depois de `Notes` (acrescente `using SiagroB1.Domain.Enums;`):

```csharp
    public string? StateRegistration { get; set; }

    public StateRegistrationIndicator? StateRegistrationIndicator { get; set; }

    public string? NfeEmail { get; set; }

    public string? Phone { get; set; }

    public int? PaymentConditionCode { get; set; }
```

Em `SiagroB1.Domain/Models/AddressModel.cs`, depois de `Country`:

```csharp
    public string? StreetNumber { get; set; }

    public string? Complement { get; set; }

    public string? MunicipalityCode { get; set; }
```

- [ ] **Step 4: Resolvedor do município**

```csharp
// SiagroB1.Application/Services/AddressMunicipalityResolver.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

/// <summary>
/// Com o município escolhido, cidade e UF do endereço vêm dele — no servidor, porque a tela grava
/// a descrição fora do PATCH e a UF do endereço de faturamento decide CFOP e tributos. Sem
/// município, o endereço fica como foi digitado (é o caminho de todo cadastro anterior).
/// </summary>
public static class AddressMunicipalityResolver
{
    public static async Task ApplyAsync(AppDbContext context, Address address)
    {
        if (string.IsNullOrWhiteSpace(address.MunicipalityCode))
        {
            address.MunicipalityCode = null;
            return;
        }

        var municipality = await context.Municipalities.AsNoTracking()
                               .FirstOrDefaultAsync(m => m.Code == address.MunicipalityCode)
                           ?? throw new DefaultException($"Município {address.MunicipalityCode} não encontrado.");

        address.City = municipality.Name;
        address.State = municipality.StateAbbreviation;
    }
}
```

```bash
git add SiagroB1.Domain/Enums/StateRegistrationIndicator.cs SiagroB1.Application/Services/AddressMunicipalityResolver.cs
```

- [ ] **Step 5: Mapeamento e validação no `BusinessPartnerService`**

Em `SiagroB1.Application/Services/BusinessPartnerService.cs`:

1. Nas **duas** projeções `new BusinessPartnerModel()` (`GetByIdAsync` e `QueryAll`), acrescente:

```csharp
                    StateRegistration = x.StateRegistration,
                    StateRegistrationIndicator = x.StateRegistrationIndicator,
                    NfeEmail = x.NfeEmail,
                    Phone = x.Phone,
                    PaymentConditionCode = x.PaymentConditionCode,
```

2. Nas **duas** projeções `new AddressModel()` dessas consultas, acrescente:

```csharp
                            StreetNumber = a.StreetNumber,
                            Complement = a.Complement,
                            MunicipalityCode = a.MunicipalityCode,
```

3. No `CreateAsync`, antes do `var entity = new BusinessPartner()`, chame `ValidateNfeFields(model);`; no inicializador da entidade acrescente os cinco campos do parceiro (`StateRegistration = model.StateRegistration`, `StateRegistrationIndicator = model.StateRegistrationIndicator`, `NfeEmail = model.NfeEmail`, `Phone = model.Phone`, `PaymentConditionCode = model.PaymentConditionCode`); no `new Address` do laço acrescente `StreetNumber = address.StreetNumber`, `Complement = address.Complement`, `MunicipalityCode = address.MunicipalityCode`; e troque o laço para resolver o município de cada endereço:

```csharp
        foreach (var address in model.Addresses)
        {
            var entityAddress = new Address
            {
                CardCode = model.CardCode,
                AddressName = address.AddressName,
                AdresType = address.AdresType,
                Street = address.Street,
                StreetNumber = address.StreetNumber,
                Complement = address.Complement,
                Block = address.Block,
                ZipCode = address.ZipCode,
                City = address.City,
                State = address.State,
                Country = address.Country,
                MunicipalityCode = address.MunicipalityCode,
            };

            await AddressMunicipalityResolver.ApplyAsync(db.Context, entityAddress);
            entity.Addresses.Add(entityAddress);
        }
```

4. No `UpdateAsync`, chame `ValidateNfeFields(model);` antes de carregar a entidade e acrescente depois de `entity.TaxId = model.TaxId;`:

```csharp
        entity.StateRegistration = model.StateRegistration;
        entity.StateRegistrationIndicator = model.StateRegistrationIndicator;
        entity.NfeEmail = model.NfeEmail;
        entity.Phone = model.Phone;
        entity.PaymentConditionCode = model.PaymentConditionCode;
```

5. Método novo (antes de `EntityExists`):

```csharp
    /// <summary>
    /// Coerência dos campos da NF-e. O serviço local só existe em STANDALONE, então a regra já
    /// nasce restrita ao modo. Contribuinte (indicador 1) exige IE só com dígitos — é o tipo do
    /// schema para <c>dest/IE</c>; isento e não contribuinte não levam IE no XML.
    /// </summary>
    private static void ValidateNfeFields(BusinessPartnerModel model)
    {
        if (model.StateRegistrationIndicator == StateRegistrationIndicator.Taxpayer)
        {
            if (string.IsNullOrWhiteSpace(model.StateRegistration))
                throw new DefaultException("Parceiro contribuinte do ICMS precisa da inscrição estadual.");

            if (!System.Text.RegularExpressions.Regex.IsMatch(model.StateRegistration.Trim(), "^[0-9]{2,14}$"))
                throw new DefaultException("A inscrição estadual do contribuinte deve ter só dígitos (2 a 14).");
        }

        if (!string.IsNullOrWhiteSpace(model.NfeEmail) &&
            (!model.NfeEmail.Contains('@') || model.NfeEmail.Contains(' ')))
            throw new DefaultException($"E-mail da NF-e inválido: {model.NfeEmail}.");
    }
```

Acrescente `using SiagroB1.Domain.Enums;` no arquivo.

- [ ] **Step 6: Mapeamento no `BusinessPartnerAddressService`**

Em `SiagroB1.Application/Services/BusinessPartnerAddressService.cs`:

1. Nas projeções de `QueryAll` e `GetByIdAsync`, acrescente `StreetNumber`, `Complement` e `MunicipalityCode` (mesmo padrão do Step 5.2).
2. No `Create`, acrescente ao inicializador `var address = new Address() { ... }` os campos `StreetNumber = addressModel.StreetNumber`, `Complement = addressModel.Complement` e `MunicipalityCode = addressModel.MunicipalityCode`; logo depois do inicializador (antes do `try`), chame:

```csharp
        await AddressMunicipalityResolver.ApplyAsync(db.Context, address);
```
3. No `Update`, depois de `entity.ZipCode = addressModel.ZipCode;`:

```csharp
        entity.StreetNumber = addressModel.StreetNumber;
        entity.Complement = addressModel.Complement;
        entity.MunicipalityCode = addressModel.MunicipalityCode;
        await AddressMunicipalityResolver.ApplyAsync(db.Context, entity);
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BusinessPartner"`
Expected: PASS (testes novos e os antigos de endereço).

- [ ] **Step 8: Migration**

Run: `dotnet ef migrations add AddBusinessPartnerNfeFields --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`

Leia: 5 colunas anuláveis em `BUSINESS_PARTNERS`, 3 em `BUSINESS_PARTNERS_ADDRESSES`, FK para `MUNICIPALITIES` + índice. Nada mais.

```bash
git add SiagroB1.Migrations/AppContext/*_AddBusinessPartnerNfeFields*.cs
```

- [ ] **Step 9: Suíte**

Run: `dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Passed!`.

- [ ] **Step 10: Commit**

```bash
git commit -m "feat(partner): guardar no parceiro os dados do destinatário da NF-e" -m "IE, indicador da IE, e-mail, telefone, condição de pagamento padrão e, no endereço, número, complemento e município. Com o município escolhido, cidade e UF passam a vir dele no servidor: a UF do endereço de faturamento decide CFOP e tributos e não pode depender do que a tela mandou. Contribuinte exige IE só com dígitos.

Atenção: o serviço local de parceiro só existe em STANDALONE; em SAPB1 as propriedades novas ficam nulas.

DB: AddBusinessPartnerNfeFields

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Domain SiagroB1.Application/Services/AddressMunicipalityResolver.cs SiagroB1.Application/Services/BusinessPartnerService.cs SiagroB1.Application/Services/BusinessPartnerAddressService.cs SiagroB1.Migrations SiagroB1.Application.Tests/BusinessPartners/BusinessPartnerNfeFieldsTests.cs
```

---
### Task 5: Parcelas da condição de pagamento (`PaymentInstallmentCalculator`)

**Files:**
- Create: `SiagroB1.Domain/Enums/PaymentStartRule.cs`
- Create: `SiagroB1.Fiscal/Payments/PaymentInstallmentCalculator.cs`
- Test: `SiagroB1.Fiscal.Tests/Payments/PaymentInstallmentCalculatorTests.cs`

**Interfaces:**
- Produces: `enum PaymentStartRule { IssueDate = 1, NextMonth = 2 }`; `record PaymentInstallment(int Number, DateOnly DueDate, decimal Amount)`; `record PaymentPlan(string PaymentMeans, int? PaymentIndicator, decimal PaidAmount, IReadOnlyList<PaymentInstallment> Installments)`; `PaymentMeansCodes.All` (código → descrição), `PaymentMeansCodes.NoPayment = "90"`, `PaymentMeansCodes.Other = "99"`; `PaymentInstallmentCalculator.ParseDays(string?) : IReadOnlyList<int>` e `PaymentInstallmentCalculator.Calculate(string days, PaymentStartRule startRule, string paymentMeans, decimal total, DateOnly issueDate) : PaymentPlan`.

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Fiscal.Tests/Payments/PaymentInstallmentCalculatorTests.cs
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Payments;

namespace SiagroB1.Fiscal.Tests.Payments;

/// <summary>
/// Condição de pagamento do tipo "Dias" (SE4 tipo 1 do Protheus): parcelas iguais, o resto na
/// última, contagem a partir da emissão ou do 1º dia do mês seguinte. Alimenta o cobr/dup e o
/// pag/detPag da NF-e.
/// </summary>
public class PaymentInstallmentCalculatorTests
{
    private static readonly DateOnly Issue = new(2026, 10, 2);

    [Fact]
    public void Zero_days_is_one_cash_installment_on_the_issue_date()
    {
        var plan = PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.IssueDate, "15", 1000m, Issue);

        var installment = Assert.Single(plan.Installments);
        Assert.Equal(new PaymentInstallment(1, Issue, 1000m), installment);
        Assert.Equal(0, plan.PaymentIndicator);
        Assert.Equal(1000m, plan.PaidAmount);
    }

    [Fact]
    public void One_day_is_still_cash()
    {
        var plan = PaymentInstallmentCalculator.Calculate("1", PaymentStartRule.IssueDate, "17", 500m, Issue);

        Assert.Equal(new DateOnly(2026, 10, 3), plan.Installments.Single().DueDate);
        Assert.Equal(0, plan.PaymentIndicator);
    }

    [Fact]
    public void Thirty_days_is_term()
    {
        var plan = PaymentInstallmentCalculator.Calculate("30", PaymentStartRule.IssueDate, "15", 500m, Issue);

        Assert.Equal(new DateOnly(2026, 11, 1), plan.Installments.Single().DueDate);
        Assert.Equal(1, plan.PaymentIndicator);
    }

    [Fact]
    public void Remainder_goes_to_the_last_installment()
    {
        var plan = PaymentInstallmentCalculator.Calculate("30,60,90", PaymentStartRule.IssueDate, "15", 1000m, Issue);

        Assert.Equal(
            [
                new PaymentInstallment(1, new DateOnly(2026, 11, 1), 333.33m),
                new PaymentInstallment(2, new DateOnly(2026, 12, 1), 333.33m),
                new PaymentInstallment(3, new DateOnly(2026, 12, 31), 333.34m),
            ],
            plan.Installments);
        Assert.Equal(1, plan.PaymentIndicator);
    }

    [Fact]
    public void Next_month_counts_from_the_first_day_of_the_following_month()
    {
        var plan = PaymentInstallmentCalculator.Calculate("0,30", PaymentStartRule.NextMonth, "15", 100m,
            new DateOnly(2026, 10, 15));

        Assert.Equal([new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1)],
            plan.Installments.Select(i => i.DueDate));
        Assert.Equal([50m, 50m], plan.Installments.Select(i => i.Amount));
    }

    [Fact]
    public void Next_month_single_installment_is_term()
    {
        var plan = PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.NextMonth, "15", 100m,
            new DateOnly(2026, 10, 15));

        Assert.Equal(1, plan.PaymentIndicator);
    }

    [Fact]
    public void No_payment_means_has_no_installments_and_zero_paid()
    {
        var plan = PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.IssueDate, "90", 1000m, Issue);

        Assert.Empty(plan.Installments);
        Assert.Null(plan.PaymentIndicator);
        Assert.Equal(0m, plan.PaidAmount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("30,abc")]
    [InlineData("60,30")]
    [InlineData("30,30")]
    [InlineData("-1")]
    [InlineData("30;60")]
    [InlineData("1.5")]
    public void Invalid_days_are_rejected(string days)
    {
        Assert.Throws<DefaultException>(() =>
            PaymentInstallmentCalculator.Calculate(days, PaymentStartRule.IssueDate, "15", 100m, Issue));
    }

    [Fact]
    public void Parse_days_accepts_spaces_around_commas()
    {
        Assert.Equal([30, 60, 90], PaymentInstallmentCalculator.ParseDays(" 30, 60 ,90 "));
    }

    [Fact]
    public void Unsupported_payment_means_is_rejected()
    {
        var ex = Assert.Throws<DefaultException>(() =>
            PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.IssueDate, "05", 100m, Issue));

        Assert.Contains("05", ex.Message);
    }
}
```

```bash
git add SiagroB1.Fiscal.Tests/Payments/PaymentInstallmentCalculatorTests.cs
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~PaymentInstallmentCalculatorTests"`
Expected: FAIL de compilação — `SiagroB1.Fiscal.Payments` não existe.

- [ ] **Step 3: Implementação**

```csharp
// SiagroB1.Domain/Enums/PaymentStartRule.cs
namespace SiagroB1.Domain.Enums;

/// <summary>Início da contagem dos dias da condição de pagamento (E4_DDD do Protheus, reduzido).</summary>
public enum PaymentStartRule
{
    /// <summary>A partir da data de emissão.</summary>
    IssueDate = 1,

    /// <summary>"Fora o mês": a partir do 1º dia do mês seguinte à emissão.</summary>
    NextMonth = 2,
}
```

```csharp
// SiagroB1.Fiscal/Payments/PaymentInstallmentCalculator.cs
using System.Globalization;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Fiscal.Payments;

public sealed record PaymentInstallment(int Number, DateOnly DueDate, decimal Amount);

/// <param name="PaymentIndicator"><c>indPag</c>: 0 à vista, 1 a prazo, nulo sem pagamento.</param>
/// <param name="PaidAmount"><c>vPag</c>: o total, ou 0 com o meio "sem pagamento".</param>
public sealed record PaymentPlan(
    string PaymentMeans,
    int? PaymentIndicator,
    decimal PaidAmount,
    IReadOnlyList<PaymentInstallment> Installments);

/// <summary>Meios de pagamento (<c>tPag</c>) que o cadastro aceita — os da SEFAZ que fazem sentido aqui.</summary>
public static class PaymentMeansCodes
{
    public const string NoPayment = "90";
    public const string Other = "99";

    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["01"] = "Dinheiro",
        ["03"] = "Cartão de crédito",
        ["04"] = "Cartão de débito",
        ["15"] = "Boleto bancário",
        ["16"] = "Depósito bancário",
        ["17"] = "PIX",
        ["18"] = "Transferência bancária",
        ["90"] = "Sem pagamento",
        ["99"] = "Outros",
    };
}

/// <summary>
/// Parcelas da condição "Dias" (SE4 tipo 1 do Protheus): valor igual em cada parcela,
/// arredondado em 2 casas, e o resto na última — a soma fecha sempre no total.
/// </summary>
public static class PaymentInstallmentCalculator
{
    /// <summary>O grupo <c>dup</c> da NF-e aceita até 120 ocorrências.</summary>
    public const int MaxInstallments = 120;

    public static IReadOnlyList<int> ParseDays(string? days)
    {
        if (string.IsNullOrWhiteSpace(days))
            throw new DefaultException("Informe os dias da condição de pagamento (ex.: 0 ou 30,60,90).");

        var result = new List<int>();

        foreach (var part in days.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var day))
                throw new DefaultException(
                    $"Dia inválido na condição de pagamento: \"{part}\". Use números inteiros separados por vírgula.");

            if (result.Count > 0 && day <= result[^1])
                throw new DefaultException("Os dias da condição de pagamento devem ser crescentes (ex.: 30,60,90).");

            result.Add(day);
        }

        if (result.Count > MaxInstallments)
            throw new DefaultException($"A condição de pagamento aceita no máximo {MaxInstallments} parcelas.");

        return result;
    }

    public static PaymentPlan Calculate(
        string days, PaymentStartRule startRule, string paymentMeans, decimal total, DateOnly issueDate)
    {
        if (!PaymentMeansCodes.All.ContainsKey(paymentMeans))
            throw new DefaultException($"Meio de pagamento {paymentMeans} não é aceito pela condição de pagamento.");

        if (paymentMeans == PaymentMeansCodes.NoPayment)
            return new PaymentPlan(paymentMeans, null, 0m, []);

        var dayList = ParseDays(days);
        var start = startRule == PaymentStartRule.NextMonth
            ? new DateOnly(issueDate.Year, issueDate.Month, 1).AddMonths(1)
            : issueDate;

        var count = dayList.Count;
        var share = decimal.Round(total / count, 2, MidpointRounding.AwayFromZero);
        var installments = new List<PaymentInstallment>(count);

        for (var i = 0; i < count; i++)
        {
            var amount = i == count - 1 ? total - share * (count - 1) : share;
            installments.Add(new PaymentInstallment(i + 1, start.AddDays(dayList[i]), amount));
        }

        // À vista: uma parcela vencendo até o dia seguinte à emissão.
        var cash = count == 1 && installments[0].DueDate <= issueDate.AddDays(1);

        return new PaymentPlan(paymentMeans, cash ? 0 : 1, total, installments);
    }
}
```

```bash
git add SiagroB1.Domain/Enums/PaymentStartRule.cs SiagroB1.Fiscal/Payments/PaymentInstallmentCalculator.cs
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~PaymentInstallmentCalculatorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(financial): calcular as parcelas da condição de pagamento" -m "Condição do tipo Dias, inspirada no SE4 do Protheus: parcelas iguais com o resto na última, contagem a partir da emissão ou do 1º dia do mês seguinte, indicador à vista/a prazo e o meio 90 sem parcelas. É o que vai para o cobr/dup e o pag da NF-e.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Domain/Enums/PaymentStartRule.cs SiagroB1.Fiscal/Payments SiagroB1.Fiscal.Tests/Payments
```

---

### Task 6: Cadastro de condição de pagamento, campo no documento e padrão do cliente

**Files:**
- Create: `SiagroB1.Domain/Entities/PaymentCondition.cs`, `SiagroB1.Domain/Dtos/Nfe/PaymentInstallmentPreviewDto.cs`
- Create: `SiagroB1.Application/Services/PaymentConditions/PaymentConditionsService.cs`
- Create: `SiagroB1.Web/Controllers/PaymentConditionsController.cs`, `SiagroB1.Web/Functions/PaymentConditions/PaymentConditionsPreviewController.cs`
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs`, `SiagroB1.Infra/Context/AppDbContext.cs`, `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, `SiagroB1.Application.Tests/Support/FakeBusinessPartnerService.cs`
- Create (gerada): migration `CreatePaymentConditions`
- Test: `SiagroB1.Application.Tests/PaymentConditions/PaymentConditionsServiceTests.cs`, `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesPaymentConditionDefaultTests.cs`

**Interfaces:**
- Consumes: `PaymentStartRule`, `PaymentInstallmentCalculator`, `PaymentMeansCodes` (Task 5); `BusinessPartnerModel.PaymentConditionCode` (Task 4).
- Produces: entidade `PaymentCondition { int Code; string Name; string Days; PaymentStartRule StartRule; string PaymentMeans; bool Inactive }` (`PAYMENT_CONDITIONS`); `DbSet<PaymentCondition> PaymentConditions`; `SalesInvoice.PaymentConditionCode : int?`; `PaymentConditionsService` (`QueryAll`, `GetByIdAsync(int)`, `CreateAsync`, `UpdateAsync`, `DeleteAsync(int)`); OData `PaymentConditions` e função `PaymentConditionsPreview(Days,StartRule,PaymentMeans,Total,IssueDate)` → coleção de `PaymentInstallmentPreviewDto { int Number; DateOnly DueDate; decimal Amount }`; `FakeBusinessPartnerService(..., paymentConditions:)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// SiagroB1.Application.Tests/PaymentConditions/PaymentConditionsServiceTests.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PaymentConditions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.PaymentConditions;

/// <summary>Cadastro de condição de pagamento (só STANDALONE): dias, início da contagem e meio.</summary>
public class PaymentConditionsServiceTests
{
    private static PaymentCondition Condition(string days = "30, 60 ,90", string means = "15") => new()
    {
        Name = "30/60/90 boleto", Days = days, StartRule = PaymentStartRule.IssueDate, PaymentMeans = means,
    };

    [Fact]
    public async Task Create_normalizes_the_days()
    {
        var db = TestDb.CreateUnitOfWork();

        await new PaymentConditionsService(db).CreateAsync(Condition());

        Assert.Equal("30,60,90", (await db.Context.PaymentConditions.SingleAsync()).Days);
    }

    [Theory]
    [InlineData("60,30", "15", "crescentes")]
    [InlineData("30", "05", "05")]
    public async Task Create_rejects_invalid_conditions(string days, string means, string expected)
    {
        var db = TestDb.CreateUnitOfWork();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            new PaymentConditionsService(db).CreateAsync(Condition(days, means)));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Create_requires_a_name()
    {
        var db = TestDb.CreateUnitOfWork();
        var condition = Condition();
        condition.Name = " ";

        await Assert.ThrowsAsync<DefaultException>(() => new PaymentConditionsService(db).CreateAsync(condition));
    }

    [Fact]
    public async Task Delete_is_refused_when_a_document_uses_the_condition()
    {
        var db = TestDb.CreateUnitOfWork();
        var service = new PaymentConditionsService(db);
        var condition = await service.CreateAsync(Condition());
        db.Context.SalesInvoices.Add(new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", PaymentConditionCode = condition.Code });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.DeleteAsync(condition.Code));

        Assert.Contains("inative", ex.Message);
    }

    [Fact]
    public void Controller_refuses_outside_standalone()
    {
        var db = TestDb.CreateUnitOfWork();
        var controller = new PaymentConditionsController(new PaymentConditionsService(db), TaxTestServices.Config("SAPB1"));

        Assert.IsType<BadRequestObjectResult>(controller.Get());
    }
}
```

```csharp
// SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesPaymentConditionDefaultTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// O documento de saída nasce com a condição de pagamento padrão do cliente quando chega sem ela
/// (inclusive pelo faturamento de romaneio). Em SAPB1 o parceiro não tem o campo e nada muda.
/// </summary>
public class SalesInvoicesPaymentConditionDefaultTests
{
    private static async Task<UnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "SP" });
        await db.SaveChangesAsync();

        await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Venda", CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
            RequiresQuantity = true, IsDefault = true,
        });

        return db;
    }

    private static SalesInvoicesCreateService Create(UnitOfWork db, int? customerDefault)
    {
        var partners = new FakeBusinessPartnerService(
            names: new() { ["C1"] = "CLIENTE" },
            states: new() { ["C1"] = "SP" },
            paymentConditions: customerDefault is { } code ? new() { ["C1"] = code } : null);
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);

        return new SalesInvoicesCreateService(
            db, partners,
            new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
            new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(db, usages, partners),
            TaxTestServices.InactiveApply(db),
            NullLogger<SalesInvoicesCreateService>.Instance);
    }

    private static SalesInvoice Invoice(int? paymentCondition = null) => new()
    {
        Key = Guid.NewGuid(), BranchCode = "01", CardCode = "C1", InvoiceDate = new DateTime(2026, 10, 2),
        PaymentConditionCode = paymentCondition,
        Items =
        [
            new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m,
                SalesContractKey = Guid.NewGuid(),
            },
        ],
    };

    [Fact]
    public async Task Empty_condition_receives_the_customer_default()
    {
        var db = await SeedAsync();
        var invoice = Invoice();

        await Create(db, customerDefault: 7).ExecuteAsync(invoice, "tester");

        Assert.Equal(7, invoice.PaymentConditionCode);
    }

    [Fact]
    public async Task Informed_condition_is_kept()
    {
        var db = await SeedAsync();
        var invoice = Invoice(paymentCondition: 5);

        await Create(db, customerDefault: 7).ExecuteAsync(invoice, "tester");

        Assert.Equal(5, invoice.PaymentConditionCode);
    }

    [Fact]
    public async Task Customer_without_default_keeps_it_empty()
    {
        var db = await SeedAsync();
        var invoice = Invoice();

        await Create(db, customerDefault: null).ExecuteAsync(invoice, "tester");

        Assert.Null(invoice.PaymentConditionCode);
    }
}
```

```bash
git add SiagroB1.Application.Tests/PaymentConditions/PaymentConditionsServiceTests.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesPaymentConditionDefaultTests.cs
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PaymentCondition"`
Expected: FAIL de compilação — `PaymentCondition` não existe.

- [ ] **Step 3: Entidade, coluna no documento e DbSet**

```csharp
// SiagroB1.Domain/Entities/PaymentCondition.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Condição de pagamento do tipo "Dias" (inspirada no SE4 do Protheus). Só STANDALONE. Os
/// percentuais e a condição "informada no documento" ficaram fora do sub-projeto 2a.
/// </summary>
[Table("PAYMENT_CONDITIONS")]
public class PaymentCondition
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>Dias separados por vírgula, crescentes: <c>0</c>, <c>30</c>, <c>30,60,90</c>.</summary>
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Days { get; set; }

    public PaymentStartRule StartRule { get; set; } = PaymentStartRule.IssueDate;

    /// <summary>Meio de pagamento da SEFAZ (<c>tPag</c>): 15 boleto, 17 PIX, 90 sem pagamento...</summary>
    [Column(TypeName = "VARCHAR(2) NOT NULL")]
    public required string PaymentMeans { get; set; }

    public bool Inactive { get; set; }
}
```

```csharp
// SiagroB1.Domain/Dtos/Nfe/PaymentInstallmentPreviewDto.cs
namespace SiagroB1.Domain.Dtos.Nfe;

/// <summary>Uma parcela da prévia da tela de condições de pagamento.</summary>
public class PaymentInstallmentPreviewDto
{
    public int Number { get; set; }

    public DateOnly DueDate { get; set; }

    public decimal Amount { get; set; }
}
```

Em `SiagroB1.Domain/Entities/SalesInvoice.cs`, depois de `TaxComments`:

```csharp
    /// <summary>
    /// Condição de pagamento (NF-e STANDALONE): cobr/dup e pag do XML. Sem FK. Na criação, vazia,
    /// recebe o padrão do cliente; obrigatória só na emissão.
    /// </summary>
    public int? PaymentConditionCode { get; set; }
```

Em `SiagroB1.Infra/Context/AppDbContext.cs`:

```csharp
    public DbSet<PaymentCondition> PaymentConditions { get; set; }
```

```bash
git add SiagroB1.Domain/Entities/PaymentCondition.cs SiagroB1.Domain/Dtos/Nfe/PaymentInstallmentPreviewDto.cs
```

- [ ] **Step 4: Serviço**

```csharp
// SiagroB1.Application/Services/PaymentConditions/PaymentConditionsService.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Payments;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PaymentConditions;

/// <summary>Cadastro das condições de pagamento (só STANDALONE; o controller recusa nos demais modos).</summary>
public class PaymentConditionsService(IUnitOfWork db)
{
    public IQueryable<PaymentCondition> QueryAll() => db.Context.PaymentConditions.AsNoTracking();

    public async Task<PaymentCondition?> GetByIdAsync(int code) => await db.Context.PaymentConditions.FindAsync(code);

    public async Task<PaymentCondition> CreateAsync(PaymentCondition entity)
    {
        Normalize(entity);

        db.Context.PaymentConditions.Add(entity);
        await db.SaveChangesAsync();

        return entity;
    }

    /// <summary>A entidade chega rastreada (o controller a carrega e aplica o Delta).</summary>
    public async Task<PaymentCondition> UpdateAsync(PaymentCondition entity)
    {
        Normalize(entity);

        await db.SaveChangesAsync();

        return entity;
    }

    public async Task<bool> DeleteAsync(int code)
    {
        var entity = await db.Context.PaymentConditions.FindAsync(code);

        if (entity is null)
            return false;

        var inUse =
            await db.Context.SalesInvoices.AnyAsync(i => i.PaymentConditionCode == code) ||
            await db.Context.BusinessPartners.AnyAsync(p => p.PaymentConditionCode == code);

        if (inUse)
            throw new DefaultException(
                $"A condição de pagamento {entity.Name} está em uso em documento ou parceiro; inative-a em vez de excluir.");

        db.Context.PaymentConditions.Remove(entity);
        await db.SaveChangesAsync();

        return true;
    }

    private static void Normalize(PaymentCondition entity)
    {
        if (string.IsNullOrWhiteSpace(entity.Name))
            throw new DefaultException("Informe o nome da condição de pagamento.");

        entity.Name = entity.Name.Trim();
        entity.PaymentMeans = (entity.PaymentMeans ?? string.Empty).Trim();

        if (!PaymentMeansCodes.All.ContainsKey(entity.PaymentMeans))
            throw new DefaultException($"Meio de pagamento {entity.PaymentMeans} não é aceito pela condição de pagamento.");

        if (!Enum.IsDefined(entity.StartRule))
            throw new DefaultException("Escolha o início da contagem: data de emissão ou fora o mês.");

        entity.Days = string.Join(",", PaymentInstallmentCalculator.ParseDays(entity.Days));
    }
}
```

```bash
git add SiagroB1.Application/Services/PaymentConditions/PaymentConditionsService.cs
```

- [ ] **Step 5: Controller e função de prévia**

```csharp
// SiagroB1.Web/Controllers/PaymentConditionsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PaymentConditions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Condições de pagamento — cadastro só do modo STANDALONE. O item de menu é escondido fora dele
/// (MENU_ITEMS.StandaloneOnly); recusar aqui é a defesa de quem chega pela API.
/// </summary>
public class PaymentConditionsController(PaymentConditionsService service, IConfiguration configuration) : ODataController
{
    private const string StandaloneOnly = "As condições de pagamento só existem no modo STANDALONE.";

    private bool IsStandalone => ErpMode.IsStandalone(configuration);

    [EnableQuery]
    public ActionResult Get()
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        return Ok(service.QueryAll());
    }

    [EnableQuery]
    public async Task<ActionResult> Get([FromRoute] int key)
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        var condition = await service.GetByIdAsync(key);

        return condition is null ? NotFound() : Ok(condition);
    }

    public async Task<IActionResult> Post([FromBody] PaymentCondition entity)
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            await service.CreateAsync(entity);

            return Created(entity);
        }
        catch (Exception ex)
        {
            return ex is DefaultException ? BadRequest(ex.Message) : StatusCode(500, ex.Message);
        }
    }

    [AcceptVerbs("PATCH", "MERGE")]
    public async Task<IActionResult> Patch([FromODataUri] int key, [FromBody] Delta<PaymentCondition> patch)
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        if (!ModelState.IsValid) return BadRequest(ModelState);

        var condition = await service.GetByIdAsync(key);

        if (condition is null) return NotFound();

        try
        {
            patch.Patch(condition);
            await service.UpdateAsync(condition);
        }
        catch (Exception ex)
        {
            return ex is DefaultException ? BadRequest(ex.Message) : StatusCode(500, ex.Message);
        }

        return NoContent();
    }

    public async Task<IActionResult> Delete([FromODataUri] int key)
    {
        if (!IsStandalone) return BadRequest(StandaloneOnly);

        try
        {
            return await service.DeleteAsync(key) ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            return ex is DefaultException ? BadRequest(ex.Message) : StatusCode(500, ex.Message);
        }
    }
}
```

`ErpMode` ainda está em `SiagroB1.Application.Services.Taxes` até a Task 7; enquanto isso use `using SiagroB1.Application.Services.Taxes;` no lugar de `using SiagroB1.Infra;` neste controller e no da prévia (a Task 7 troca o `using`).

```csharp
// SiagroB1.Web/Functions/PaymentConditions/PaymentConditionsPreviewController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Payments;
using SiagroB1.Infra;

namespace SiagroB1.Web.Functions.PaymentConditions;

/// <summary>
/// Prévia das parcelas para a tela, com o MESMO cálculo da emissão — a condição ainda não
/// precisa estar gravada (os parâmetros são os do formulário).
/// </summary>
public class PaymentConditionsPreviewController(IConfiguration configuration) : ODataController
{
    [HttpGet("odata/PaymentConditionsPreview(Days={days},StartRule={startRule},PaymentMeans={paymentMeans},Total={total},IssueDate={issueDate})")]
    public IActionResult Get(
        [FromRoute] string days, [FromRoute] int startRule, [FromRoute] string paymentMeans,
        [FromRoute] decimal total, [FromRoute] DateOnly issueDate)
    {
        if (!ErpMode.IsStandalone(configuration))
            return BadRequest("As condições de pagamento só existem no modo STANDALONE.");

        try
        {
            // Rotas por atributo entregam o segmento COM as aspas simples do OData.
            var plan = PaymentInstallmentCalculator.Calculate(
                days.Trim('\''), (PaymentStartRule)startRule, paymentMeans.Trim('\''), total, issueDate);

            return Ok(plan.Installments.Select(i => new PaymentInstallmentPreviewDto
            {
                Number = i.Number, DueDate = i.DueDate, Amount = i.Amount,
            }));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

Em `ODataConfigurations.cs`, depois de `modelBuilder.EntitySet<Municipality>("Municipalities");`:

```csharp
        modelBuilder.EntitySet<PaymentCondition>("PaymentConditions");

        // Prévia das parcelas da condição de pagamento (mesmo cálculo da emissão da NF-e).
        var paymentConditionsPreview = modelBuilder.Function("PaymentConditionsPreview");
        paymentConditionsPreview.Parameter<string>("Days");
        paymentConditionsPreview.Parameter<int>("StartRule");
        paymentConditionsPreview.Parameter<string>("PaymentMeans");
        paymentConditionsPreview.Parameter<decimal>("Total");
        paymentConditionsPreview.Parameter<DateOnly>("IssueDate");
        paymentConditionsPreview.ReturnsCollection<PaymentInstallmentPreviewDto>();
```

Em `ServiceCollectionExtensions.cs`, junto de `MunicipalityService`:

```csharp
        services.AddScoped<PaymentConditionsService>();
```

```bash
git add SiagroB1.Web/Controllers/PaymentConditionsController.cs SiagroB1.Web/Functions/PaymentConditions/PaymentConditionsPreviewController.cs
```

- [ ] **Step 6: Padrão do cliente na criação do documento**

Em `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs`, dentro do `try`, troque:

```csharp
            salesInvoice.CardName = (await businessPartnerService.GetByIdAsync(salesInvoice.CardCode))?.CardName;
```

por:

```csharp
            var customer = await businessPartnerService.GetByIdAsync(salesInvoice.CardCode);
            salesInvoice.CardName = customer?.CardName;

            // Condição de pagamento padrão do cliente quando o documento chega sem ela — inclusive
            // no faturamento de romaneio. Em SAPB1 o parceiro não tem o campo: segue nulo, como hoje.
            salesInvoice.PaymentConditionCode ??= customer?.PaymentConditionCode;
```

Em `SiagroB1.Application.Tests/Support/FakeBusinessPartnerService.cs`, acrescente o parâmetro **no fim** do construtor primário:

```csharp
    bool failOnLoadSuppliers = false,
    Dictionary<string, int>? paymentConditions = null) : IBusinessPartnerService
```

um campo:

```csharp
    /// <summary>Condição de pagamento padrão por CardCode — o padrão do documento de saída.</summary>
    private readonly Dictionary<string, int> _paymentConditions = paymentConditions ?? new();
```

e, no `new BusinessPartnerModel { ... }` do `GetByIdAsync`:

```csharp
                PaymentConditionCode = _paymentConditions.TryGetValue(code, out var paymentCondition) ? paymentCondition : null,
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~PaymentCondition"`
Expected: PASS (8 testes).

- [ ] **Step 8: Migration**

Run: `dotnet ef migrations add CreatePaymentConditions --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`

Leia: `CreateTable PAYMENT_CONDITIONS` (Code identity) e `AddColumn PaymentConditionCode` (int, nulo) em `SALES_INVOICES`. Nada mais.

```bash
git add SiagroB1.Migrations/AppContext/*_CreatePaymentConditions*.cs
```

- [ ] **Step 9: Suíte**

Run: `dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Passed!`.

- [ ] **Step 10: Commit**

```bash
git commit -m "feat(financial): cadastrar condições de pagamento e levá-las ao documento de saída" -m "Cadastro só STANDALONE (o controller recusa nos demais modos), com prévia das parcelas pelo mesmo cálculo da emissão. O documento ganha a condição de pagamento e, vazio na criação, recebe o padrão do cliente — inclusive no faturamento de romaneio. Condição em uso não se exclui: inativa-se.

DB: CreatePaymentConditions

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Domain SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Application/Services/PaymentConditions SiagroB1.Application/Services/SalesInvoices/SalesInvoicesCreateService.cs SiagroB1.Web SiagroB1.Migrations SiagroB1.Application.Tests
```

---

### Task 7: Menu só STANDALONE (`MENU_ITEMS.StandaloneOnly`)

**Files:**
- Move (git mv): `SiagroB1.Application/Services/Taxes/ErpMode.cs` → `SiagroB1.Infra/ErpMode.cs`
- Modify: `SiagroB1.Domain/Entities/Common/MenuItem.cs`, `SiagroB1.Security/Services/MenuService.cs`, `SiagroB1.Application/Services/BranchService.cs`, `SiagroB1.Application/Services/Taxes/TaxCalculationGate.cs`, `SiagroB1.Web/Controllers/IbsCbsRatesController.cs`, `SiagroB1.Web/Controllers/PaymentConditionsController.cs`, `SiagroB1.Web/Functions/PaymentConditions/PaymentConditionsPreviewController.cs`
- Create (gerada + editada): migration `AddMenuItemStandaloneOnly` (CommonContext)
- Test: `SiagroB1.Application.Tests/Security/MenuServiceStandaloneOnlyTests.cs`

**Interfaces:**
- Produces: `SiagroB1.Infra.ErpMode.IsStandalone(IConfiguration)`; `MenuItem.StandaloneOnly : bool`; `MenuService(CommonDbContext context, IConfiguration configuration)`; itens de menu `paymentConditions` ("Condições de Pagamento") e `nfeSettings` ("Configuração da NF-e") sob `registers`, marcados e concedidos ao `ADMIN`.

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Application.Tests/Security/MenuServiceStandaloneOnlyTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;
using SiagroB1.Security.Services;

namespace SiagroB1.Application.Tests.Security;

/// <summary>
/// Telas que só existem no STANDALONE (condições de pagamento, configuração da NF-e) somem do
/// menu nos outros modos. Quem monta o menu é o Gateway, que conhece o Erp.
/// </summary>
public class MenuServiceStandaloneOnlyTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static async Task<CommonDbContext> SeedAsync()
    {
        var db = new CommonDbContext(new DbContextOptionsBuilder<CommonDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        db.UserProfiles.Add(new UserProfile { UserId = UserId, ProfileCode = "P1" });
        db.ProfileRoles.Add(new ProfileRole { ProfileCode = "P1", RoleCode = "ADMIN" });
        db.MenuItems.AddRange(
            new MenuItem { Key = "registers", Title = "Cadastros", Order = 2 },
            new MenuItem { Key = "usages", Title = "Naturezas", Order = 13, ParentKey = "registers" },
            new MenuItem { Key = "paymentConditions", Title = "Condições de Pagamento", Order = 16, ParentKey = "registers", StandaloneOnly = true });
        db.RolesMenus.AddRange(
            new RoleMenu { RoleCode = "ADMIN", MenuItemKey = "registers" },
            new RoleMenu { RoleCode = "ADMIN", MenuItemKey = "usages" },
            new RoleMenu { RoleCode = "ADMIN", MenuItemKey = "paymentConditions" });
        await db.SaveChangesAsync();

        return db;
    }

    private static async Task<List<string?>> ChildKeysAsync(string? erp)
    {
        var db = await SeedAsync();
        var menu = await new MenuService(db, TaxTestServices.Config(erp)).GetMenuAsync(UserId);

        return Assert.Single(menu.Navigation).Items!.Select(i => i.Key).Order().ToList();
    }

    [Fact]
    public async Task Standalone_shows_standalone_only_items() =>
        Assert.Equal(["paymentConditions", "usages"], await ChildKeysAsync("STANDALONE"));

    [Fact]
    public async Task Missing_erp_key_counts_as_standalone() =>
        Assert.Contains("paymentConditions", await ChildKeysAsync(null));

    [Fact]
    public async Task Sapb1_hides_standalone_only_items() =>
        Assert.Equal(["usages"], await ChildKeysAsync("SAPB1"));
}
```

```bash
git add SiagroB1.Application.Tests/Security/MenuServiceStandaloneOnlyTests.cs
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~MenuServiceStandaloneOnlyTests"`
Expected: FAIL de compilação — `MenuItem.StandaloneOnly` não existe e `MenuService` não aceita configuração.

- [ ] **Step 3: `ErpMode` desce para o Infra**

```bash
git mv SiagroB1.Application/Services/Taxes/ErpMode.cs SiagroB1.Infra/ErpMode.cs
sed -i 's/^namespace SiagroB1\.Application\.Services\.Taxes;/namespace SiagroB1.Infra;/' SiagroB1.Infra/ErpMode.cs
```

Acrescente no comentário do `ErpMode` a linha: `/// Mora no Infra porque o menu (projeto Security) também pergunta o modo.`

Ajuste os `using` (todos já referenciam o Infra):
- `SiagroB1.Application/Services/BranchService.cs`: troque `using SiagroB1.Application.Services.Taxes;` por `using SiagroB1.Infra;`.
- `SiagroB1.Application/Services/Taxes/TaxCalculationGate.cs`: já tem `using SiagroB1.Infra;` — nada a fazer.
- `SiagroB1.Web/Controllers/IbsCbsRatesController.cs`: acrescente `using SiagroB1.Infra;` (mantenha o de `Taxes`, que traz o `IbsCbsRatesService`).
- `SiagroB1.Web/Controllers/PaymentConditionsController.cs` e `SiagroB1.Web/Functions/PaymentConditions/PaymentConditionsPreviewController.cs`: troque `using SiagroB1.Application.Services.Taxes;` por `using SiagroB1.Infra;`.

Run: `grep -rn "Services.Taxes" --include=*.cs SiagroB1.Application SiagroB1.Web | grep -i erpmode`
Expected: nenhuma linha.

- [ ] **Step 4: Coluna e filtro**

Em `SiagroB1.Domain/Entities/Common/MenuItem.cs`, depois de `Order`:

```csharp
    /// <summary>
    /// Item que só existe no modo STANDALONE (ex.: Condições de Pagamento, Configuração da NF-e).
    /// O serviço de menu do Gateway o esconde nos demais modos.
    /// </summary>
    public bool StandaloneOnly { get; set; }
```

Em `SiagroB1.Security/Services/MenuService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Dtos.Common;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Security.Services;

public class MenuService(CommonDbContext context, IConfiguration configuration)
```

e troque o início da montagem de `menus`:

```csharp
        // Telas só do STANDALONE somem nos demais modos — mesmo teste positivo de ErpMode.
        var standalone = ErpMode.IsStandalone(configuration);

        var menus = data
            .Where(x => standalone || !x.Menu.StandaloneOnly)
            .Select(x => new MenuNode
```

(o restante do `Select`/`DistinctBy`/`ToList` fica igual).

Run: `grep -rn "new MenuService(" --include=*.cs . | grep -v /obj/`
Expected: só o teste novo (o Gateway resolve `MenuService` pelo DI, que injeta `IConfiguration`).

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~MenuServiceStandaloneOnlyTests"`
Expected: PASS (3 testes).

- [ ] **Step 6: Migration (CommonContext) com os itens novos**

Run: `dotnet ef migrations add AddMenuItemStandaloneOnly --context CommonDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`

Leia: só o `AddColumn StandaloneOnly` (bit, not null, default false) em `MENU_ITEMS`. Depois dele, no `Up`, acrescente:

```csharp
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "paymentConditions", "Condições de Pagamento", "sap-icon://payment-approval", true, false, 16, "registers", true },
                    { "nfeSettings", "Configuração da NF-e", "sap-icon://action-settings", true, false, 17, "registers", true },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "8E5B2C71-4A39-4F0D-9C61-2D7A3B5E9F14", "ADMIN", "paymentConditions" },
                    { "3F9D6A20-7C84-4E1B-A5D2-6B0E8C47F9A3", "ADMIN", "nfeSettings" },
                });
```

E no `Down`, **antes** do `DropColumn`:

```csharp
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues: ["8E5B2C71-4A39-4F0D-9C61-2D7A3B5E9F14", "3F9D6A20-7C84-4E1B-A5D2-6B0E8C47F9A3"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["paymentConditions", "nfeSettings"]);
```

```bash
git add SiagroB1.Migrations/CommonContext/*_AddMenuItemStandaloneOnly*.cs SiagroB1.Migrations/CommonContext/CommonDbContextModelSnapshot.cs
```

- [ ] **Step 7: Build e suíte**

Run: `dotnet build SiagroB1.sln 2>&1 | grep -E " error |Build succeeded" | sort -u` e `dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Build succeeded` e `Passed!`.

- [ ] **Step 8: Commit**

```bash
git commit -m "feat(security): esconder fora do STANDALONE os menus que só existem nele" -m "MENU_ITEMS ganha StandaloneOnly e o serviço de menu do Gateway, que conhece o Erp, filtra esses itens nos demais modos. Nascem marcados os menus Condições de Pagamento e Configuração da NF-e, concedidos ao ADMIN. O ErpMode desce para o Infra porque a Security não vê a Application.

DB: AddMenuItemStandaloneOnly

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Infra/ErpMode.cs SiagroB1.Application/Services/Taxes SiagroB1.Application/Services/BranchService.cs SiagroB1.Domain/Entities/Common/MenuItem.cs SiagroB1.Security/Services/MenuService.cs SiagroB1.Web SiagroB1.Migrations SiagroB1.Application.Tests/Security/MenuServiceStandaloneOnlyTests.cs
```

---

### Task 8: Certificado A1 — cifra da senha, carga e inspeção

**Files:**
- Create: `SiagroB1.Fiscal/Certificates/CertificatePasswordCipher.cs`, `CertificateLoader.cs`, `CertificateInspector.cs`
- Create: `SiagroB1.Fiscal.Tests/Support/TestCertificates.cs`
- Test: `SiagroB1.Fiscal.Tests/Certificates/CertificatePasswordCipherTests.cs`, `CertificateInspectorTests.cs`

**Interfaces:**
- Produces:
  - `CertificatePasswordCipher(byte[] key)`, `CertificatePasswordCipher.FromBase64(string?)`, `byte[] Encrypt(string)`, `string Decrypt(byte[])`, constantes `ConfigurationKey = "Nfe:CertificateKey"`, `MissingKeyMessage`.
  - `CertificateLoader.Load(byte[] pfx, string password) : X509Certificate2` (recusa com `DefaultException`).
  - `record CertificateInfo(string Subject, string? TaxId, DateTime ValidFrom, DateTime ValidUntil, bool HasPrivateKey)`; `CertificateInspector.Inspect(byte[] pfx, string password)` e `Inspect(X509Certificate2)`.
  - `TestCertificates.CreatePfx(string? icpBrasilCnpj = "12345678000195", string commonName = ..., DateTimeOffset? notAfter = null, bool withPrivateKey = true, string password = TestCertificates.Password) : byte[]` — usado também pelas Tasks 10, 12, 13 e 16.

- [ ] **Step 1: Helper de teste — certificado autoassinado com o OID da ICP-Brasil**

```csharp
// SiagroB1.Fiscal.Tests/Support/TestCertificates.cs
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SiagroB1.Fiscal.Tests.Support;

/// <summary>
/// Certificado A1 de mentira, gerado no teste: RSA 2048 autoassinado, com o CNPJ no
/// SubjectAlternativeName (otherName OID 2.16.76.1.3.3, como na ICP-Brasil) e no fim do CN
/// ("RAZAO:CNPJ"). Serve para assinar o XML e validar no XSD sem certificado real.
/// Compartilhado com o SiagroB1.Application.Tests por link no .csproj.
/// </summary>
public static class TestCertificates
{
    public const string Password = "senha-teste";

    public static byte[] CreatePfx(
        string? icpBrasilCnpj = "12345678000195",
        string commonName = "CEAGUI CEREAIS LTDA:12345678000195",
        DateTimeOffset? notAfter = null,
        bool withPrivateKey = true,
        string password = Password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        if (icpBrasilCnpj is not null)
            request.CertificateExtensions.Add(IcpBrasilSubjectAlternativeName(icpBrasilCnpj));

        var end = notAfter ?? DateTimeOffset.UtcNow.AddYears(1);
        using var certificate = request.CreateSelfSigned(end.AddYears(-2), end);

        if (withPrivateKey)
            return certificate.Export(X509ContentType.Pfx, password);

        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return publicOnly.Export(X509ContentType.Pfx, password);
    }

    private static X509Extension IcpBrasilSubjectAlternativeName(string cnpj)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);

        using (writer.PushSequence())
        {
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            {
                writer.WriteObjectIdentifier("2.16.76.1.3.3");

                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                {
                    writer.WriteOctetString(Encoding.ASCII.GetBytes(cnpj));
                }
            }
        }

        return new X509Extension("2.5.29.17", writer.Encode(), critical: false);
    }
}
```

```bash
git add SiagroB1.Fiscal.Tests/Support/TestCertificates.cs
```

- [ ] **Step 2: Write the failing tests**

```csharp
// SiagroB1.Fiscal.Tests/Certificates/CertificatePasswordCipherTests.cs
using System.Security.Cryptography;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;

namespace SiagroB1.Fiscal.Tests.Certificates;

/// <summary>A senha do .pfx vai cifrada para o banco; a chave só existe no appsettings do servidor.</summary>
public class CertificatePasswordCipherTests
{
    private static readonly string KeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Round_trip_returns_the_password()
    {
        var cipher = CertificatePasswordCipher.FromBase64(KeyBase64);

        Assert.Equal("s3nh@ç", cipher.Decrypt(cipher.Encrypt("s3nh@ç")));
    }

    [Fact]
    public void Each_encryption_uses_a_new_nonce()
    {
        var cipher = CertificatePasswordCipher.FromBase64(KeyBase64);

        Assert.NotEqual(cipher.Encrypt("abc"), cipher.Encrypt("abc"));
    }

    [Fact]
    public void Another_key_cannot_decrypt()
    {
        var encrypted = CertificatePasswordCipher.FromBase64(KeyBase64).Encrypt("abc");
        var other = CertificatePasswordCipher.FromBase64(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

        var ex = Assert.Throws<DefaultException>(() => other.Decrypt(encrypted));

        Assert.Contains("Envie o certificado de novo", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("não é base64")]
    [InlineData("AAAA")]
    public void Missing_or_invalid_key_names_the_setting(string? key)
    {
        var ex = Assert.Throws<DefaultException>(() => CertificatePasswordCipher.FromBase64(key));

        Assert.Contains("Nfe:CertificateKey", ex.Message);
    }
}
```

```csharp
// SiagroB1.Fiscal.Tests/Certificates/CertificateInspectorTests.cs
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Certificates;

/// <summary>Titular, CNPJ (OID ICP-Brasil), validade e chave privada do .pfx enviado.</summary>
public class CertificateInspectorTests
{
    [Fact]
    public void Reads_the_cnpj_from_the_icp_brasil_oid()
    {
        var pfx = TestCertificates.CreatePfx(icpBrasilCnpj: "68583898000101", commonName: "OUTRA RAZAO");

        var info = CertificateInspector.Inspect(pfx, TestCertificates.Password);

        Assert.Equal("68583898000101", info.TaxId);
        Assert.True(info.HasPrivateKey);
        Assert.Contains("OUTRA RAZAO", info.Subject);
    }

    [Fact]
    public void Falls_back_to_the_cnpj_at_the_end_of_the_common_name()
    {
        var pfx = TestCertificates.CreatePfx(icpBrasilCnpj: null, commonName: "CEAGUI LTDA:12345678000195");

        Assert.Equal("12345678000195", CertificateInspector.Inspect(pfx, TestCertificates.Password).TaxId);
    }

    [Fact]
    public void Accepts_an_alphanumeric_cnpj()
    {
        var pfx = TestCertificates.CreatePfx(icpBrasilCnpj: "12ABC34501DE35", commonName: "ALFA LTDA");

        Assert.Equal("12ABC34501DE35", CertificateInspector.Inspect(pfx, TestCertificates.Password).TaxId);
    }

    [Fact]
    public void Wrong_password_is_a_business_error()
    {
        var pfx = TestCertificates.CreatePfx();

        var ex = Assert.Throws<DefaultException>(() => CertificateInspector.Inspect(pfx, "errada"));

        Assert.Contains("senha", ex.Message);
    }

    [Fact]
    public void Not_a_pfx_is_a_business_error()
    {
        Assert.Throws<DefaultException>(() => CertificateInspector.Inspect([1, 2, 3], "x"));
    }

    [Fact]
    public void Reports_a_pfx_without_private_key()
    {
        var pfx = TestCertificates.CreatePfx(withPrivateKey: false);

        Assert.False(CertificateInspector.Inspect(pfx, TestCertificates.Password).HasPrivateKey);
    }

    [Fact]
    public void Reports_the_validity()
    {
        var notAfter = new DateTimeOffset(2027, 3, 10, 12, 0, 0, TimeSpan.Zero);
        var pfx = TestCertificates.CreatePfx(notAfter: notAfter);

        var info = CertificateInspector.Inspect(pfx, TestCertificates.Password);

        Assert.Equal(notAfter.UtcDateTime.Date, info.ValidUntil.ToUniversalTime().Date);
    }
}
```

```bash
git add SiagroB1.Fiscal.Tests/Certificates
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~Certificate"`
Expected: FAIL de compilação — `SiagroB1.Fiscal.Certificates` não existe.

- [ ] **Step 4: Implementação**

```csharp
// SiagroB1.Fiscal/Certificates/CertificatePasswordCipher.cs
using System.Security.Cryptography;
using System.Text;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Fiscal.Certificates;

/// <summary>
/// AES-GCM da senha do certificado A1. Formato gravado: nonce (12) + tag (16) + texto cifrado.
/// A chave (32 bytes, base64) fica só em <c>Nfe:CertificateKey</c> do appsettings do servidor:
/// quem lê o banco não abre o .pfx sozinho.
/// </summary>
public sealed class CertificatePasswordCipher
{
    public const string ConfigurationKey = "Nfe:CertificateKey";

    public const string MissingKeyMessage =
        "Configure no servidor a chave Nfe:CertificateKey (base64 de 32 bytes) antes de enviar o certificado ou emitir NF-e.";

    private const string DecryptFailedMessage =
        "Não foi possível abrir a senha do certificado: a chave Nfe:CertificateKey do servidor mudou. Envie o certificado de novo.";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public CertificatePasswordCipher(byte[] key)
    {
        if (key.Length != 32)
            throw new DefaultException(MissingKeyMessage);

        _key = key;
    }

    public static CertificatePasswordCipher FromBase64(string? keyBase64)
    {
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new DefaultException(MissingKeyMessage);

        try
        {
            return new CertificatePasswordCipher(Convert.FromBase64String(keyBase64));
        }
        catch (FormatException)
        {
            throw new DefaultException(MissingKeyMessage);
        }
    }

    public byte[] Encrypt(string password)
    {
        var plain = Encoding.UTF8.GetBytes(password);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        return [.. nonce, .. tag, .. cipher];
    }

    public string Decrypt(byte[] payload)
    {
        if (payload.Length < NonceSize + TagSize)
            throw new DefaultException(DecryptFailedMessage);

        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var cipher = payload[(NonceSize + TagSize)..];
        var plain = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            throw new DefaultException(DecryptFailedMessage);
        }

        return Encoding.UTF8.GetString(plain);
    }
}
```

```csharp
// SiagroB1.Fiscal/Certificates/CertificateLoader.cs
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Fiscal.Certificates;

/// <summary>
/// Abre o .pfx a partir dos bytes (nunca do repositório do Windows). No Windows a chave vai para
/// um contêiner de máquina temporário: o SChannel não usa chave efêmera na autenticação TLS de
/// cliente que a SEFAZ exige. Nos demais sistemas, chave efêmera.
/// </summary>
public static class CertificateLoader
{
    public static X509Certificate2 Load(byte[] pfx, string password)
    {
        var flags = OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.MachineKeySet
            : X509KeyStorageFlags.EphemeralKeySet;

        try
        {
            return X509CertificateLoader.LoadPkcs12(pfx, password, flags);
        }
        catch (CryptographicException)
        {
            throw new DefaultException("O arquivo não é um certificado A1 (.pfx) válido ou a senha está errada.");
        }
    }
}
```

```csharp
// SiagroB1.Fiscal/Certificates/CertificateInspector.cs
using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace SiagroB1.Fiscal.Certificates;

public sealed record CertificateInfo(
    string Subject, string? TaxId, DateTime ValidFrom, DateTime ValidUntil, bool HasPrivateKey);

/// <summary>
/// Lê do certificado o que a tela mostra e o envio confere: titular, CNPJ, validade e se há chave
/// privada. O CNPJ vem do SubjectAlternativeName (otherName OID 2.16.76.1.3.3 da ICP-Brasil); na
/// falta dele, do fim do CN ("RAZAO SOCIAL:CNPJ"), que é o formato usual das AC brasileiras.
/// </summary>
public static class CertificateInspector
{
    public const string IcpBrasilCnpjOid = "2.16.76.1.3.3";

    private static readonly Regex TaxIdPattern = new("^[0-9A-Z]{14}$");

    public static CertificateInfo Inspect(byte[] pfx, string password)
    {
        using var certificate = CertificateLoader.Load(pfx, password);
        return Inspect(certificate);
    }

    public static CertificateInfo Inspect(X509Certificate2 certificate) =>
        new(certificate.Subject,
            ReadIcpBrasilCnpj(certificate) ?? ReadFromCommonName(certificate),
            certificate.NotBefore,
            certificate.NotAfter,
            certificate.HasPrivateKey);

    private static string? ReadIcpBrasilCnpj(X509Certificate2 certificate)
    {
        var extension = certificate.Extensions["2.5.29.17"];

        if (extension is null)
            return null;

        try
        {
            var names = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
            var otherNameTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);

            while (names.HasData)
            {
                if (!names.PeekTag().HasSameClassAndValue(otherNameTag))
                {
                    names.ReadEncodedValue();
                    continue;
                }

                var otherName = names.ReadSequence(otherNameTag);
                var oid = otherName.ReadObjectIdentifier();
                var value = otherName.ReadSequence(otherNameTag);

                if (oid != IcpBrasilCnpjOid)
                    continue;

                var text = value.PeekTag().TagValue switch
                {
                    (int)UniversalTagNumber.OctetString => Encoding.ASCII.GetString(value.ReadOctetString()),
                    (int)UniversalTagNumber.UTF8String => value.ReadCharacterString(UniversalTagNumber.UTF8String),
                    (int)UniversalTagNumber.PrintableString => value.ReadCharacterString(UniversalTagNumber.PrintableString),
                    (int)UniversalTagNumber.IA5String => value.ReadCharacterString(UniversalTagNumber.IA5String),
                    _ => null,
                };

                return Clean(text);
            }
        }
        catch (AsnContentException)
        {
            // SAN fora do padrão: tenta o CN.
        }

        return null;
    }

    private static string? ReadFromCommonName(X509Certificate2 certificate)
    {
        var commonName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        var separator = commonName.LastIndexOf(':');

        return separator < 0 ? null : Clean(commonName[(separator + 1)..]);
    }

    private static string? Clean(string? value)
    {
        if (value is null)
            return null;

        var cleaned = new string(value.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

        return TaxIdPattern.IsMatch(cleaned) ? cleaned : null;
    }
}
```

```bash
git add SiagroB1.Fiscal/Certificates
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~Certificate"`
Expected: PASS (11 testes).

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(invoice): abrir e inspecionar o certificado A1 e cifrar a senha dele" -m "A senha do .pfx vai para o banco cifrada com AES-GCM; a chave fica só em Nfe:CertificateKey no appsettings do servidor. O inspetor lê titular, CNPJ (OID 2.16.76.1.3.3 da ICP-Brasil, com o fim do CN como reserva, aceitando CNPJ alfanumérico), validade e chave privada — o que o envio do certificado vai conferir.

Atenção: no Windows a chave é carregada em contêiner de máquina; o SChannel não usa chave efêmera no TLS de cliente que a SEFAZ exige.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Fiscal/Certificates SiagroB1.Fiscal.Tests/Support SiagroB1.Fiscal.Tests/Certificates
```

---
### Task 9: XML da NF-e — entrada (`NfeIssueInput`) e montagem (`NfeXmlBuilder`)

**Files:**
- Create: `SiagroB1.Fiscal/Nfe/NfeIssueInput.cs`, `SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs`
- Create: `SiagroB1.Fiscal.Tests/Support/NfeTestData.cs`
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs`

**Interfaces:**
- Consumes: `PaymentPlan`, `PaymentMeansCodes` (Task 5); `TaxRegime`, `StateRegistrationIndicator` (Task 4), `NfeEnvironment` (criado aqui), `FreightTerms` (existente: `Cif = 0, Fob = 1, Ter = 2, None = 3`).
- Produces:
  - `enum NfeEnvironment { Production = 1, Homologation = 2 }` (Domain).
  - Records `NfeAddress`, `NfeIssuer`, `NfeRecipient`, `NfeDelivery`, `NfeCarrier`, `NfeVehicle`, `NfeTechnicalResponsible`, `NfeItem`, `NfeIssueInput` (propriedades abaixo, `required`/`init`).
  - `NfeXmlBuilder.Build(NfeIssueInput) : NFe.Classes.NFe`; `NfeXmlBuilder.HomologationRecipientName`.
  - `NfeTestData.Input(...)` e `NfeTestData.Item(...)` — usados também nas Tasks 10 e 16.

Mapeamento = spec §8. Os namespaces da Zeus abaixo foram conferidos nos metadados do pacote.

- [ ] **Step 1: Records de entrada e o enum de ambiente**

```csharp
// SiagroB1.Domain/Enums/NfeEnvironment.cs
namespace SiagroB1.Domain.Enums;

/// <summary>Ambiente da SEFAZ (<c>tpAmb</c>); os valores são os oficiais.</summary>
public enum NfeEnvironment
{
    Production = 1,
    Homologation = 2,
}
```

```csharp
// SiagroB1.Fiscal/Nfe/NfeIssueInput.cs
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Payments;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>Endereço já resolvido (município do IBGE, CEP e telefone só com dígitos).</summary>
public sealed record NfeAddress
{
    public required string Street { get; init; }
    public required string Number { get; init; }
    public string? Complement { get; init; }
    public required string District { get; init; }
    public required string MunicipalityCode { get; init; }
    public required string MunicipalityName { get; init; }
    public required string State { get; init; }
    public string? ZipCode { get; init; }
    public string? Phone { get; init; }
}

/// <summary>Emitente. <see cref="TaxId"/> só com [0-9A-Z]: 14 = CNPJ, 11 = CPF.</summary>
public sealed record NfeIssuer
{
    public required string TaxId { get; init; }
    public required string LegalName { get; init; }
    public string? TradeName { get; init; }
    public required string StateRegistration { get; init; }
    public required TaxRegime TaxRegime { get; init; }
    public required NfeAddress Address { get; init; }
}

public sealed record NfeRecipient
{
    public required string TaxId { get; init; }
    public required string Name { get; init; }
    public required StateRegistrationIndicator Indicator { get; init; }
    public string? StateRegistration { get; init; }
    public string? Email { get; init; }
    public required NfeAddress Address { get; init; }
}

/// <summary>Local de entrega diferente do destinatário (grupo <c>entrega</c>).</summary>
public sealed record NfeDelivery
{
    public required string TaxId { get; init; }
    public required string Name { get; init; }
    public string? StateRegistration { get; init; }
    public required NfeAddress Address { get; init; }
}

public sealed record NfeCarrier
{
    public string? TaxId { get; init; }
    public required string Name { get; init; }
    public string? StateRegistration { get; init; }
    public string? FullAddress { get; init; }
    public string? MunicipalityName { get; init; }
    public string? State { get; init; }
}

/// <summary>Placa (sem traço, maiúscula) e UF do veículo.</summary>
public sealed record NfeVehicle(string Plate, string State);

/// <summary><c>infRespTec</c> — os dados da IDX, de <c>Nfe:TechnicalResponsible</c>.</summary>
public sealed record NfeTechnicalResponsible(string Cnpj, string Contact, string Email, string Phone);

/// <summary>Linha do documento com a fotografia dos tributos do sub-projeto 1, já gravada.</summary>
public sealed record NfeItem
{
    public required int Number { get; init; }
    public required string ItemCode { get; init; }
    public required string Description { get; init; }
    public required string Ncm { get; init; }
    public required string Cfop { get; init; }
    public required string UnitOfMeasure { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal Total { get; init; }
    public required byte GoodsOrigin { get; init; }
    public string? BenefitCode { get; init; }

    /// <summary>CST (2 dígitos) ou CSOSN (3 dígitos) do ICMS.</summary>
    public required string IcmsCode { get; init; }
    public decimal IcmsBase { get; init; }
    public decimal IcmsRate { get; init; }
    public decimal IcmsValue { get; init; }
    public decimal IcmsBaseReduction { get; init; }
    public decimal IcmsDeferral { get; init; }
    public decimal IcmsOperationValue { get; init; }
    public decimal IcmsDeferredValue { get; init; }

    public required string PisCst { get; init; }
    public decimal PisBase { get; init; }
    public decimal PisRate { get; init; }
    public decimal PisValue { get; init; }
    public required string CofinsCst { get; init; }
    public decimal CofinsBase { get; init; }
    public decimal CofinsRate { get; init; }
    public decimal CofinsValue { get; init; }

    public string? IbsCbsCst { get; init; }
    public string? IbsCbsClassCode { get; init; }
    public decimal IbsCbsBase { get; init; }
    public decimal IbsStateRate { get; init; }
    public decimal IbsMunicipalRate { get; init; }
    public decimal IbsRateReduction { get; init; }
    public decimal IbsStateValue { get; init; }
    public decimal IbsMunicipalValue { get; init; }
    public decimal CbsRate { get; init; }
    public decimal CbsRateReduction { get; init; }
    public decimal CbsValue { get; init; }
}

/// <summary>
/// Tudo o que o XML precisa, já resolvido pela Application (<c>NfeIssueInputAssembler</c>). O
/// builder não decide nada de cadastro: só traduz para o modelo da Zeus.
/// </summary>
public sealed record NfeIssueInput
{
    public required NfeEnvironment Environment { get; init; }
    public required int Series { get; init; }
    public required long Number { get; init; }

    /// <summary><c>cNF</c>: 8 dígitos, gerado uma vez e guardado no documento.</summary>
    public required string RandomCode { get; init; }

    /// <summary><c>dhEmi</c>, já em America/Sao_Paulo.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    public required string OperationNature { get; init; }
    public required string ApplicationVersion { get; init; }
    public required NfeIssuer Issuer { get; init; }
    public required NfeRecipient Recipient { get; init; }
    public NfeDelivery? Delivery { get; init; }
    public required IReadOnlyList<NfeItem> Items { get; init; }
    public required FreightTerms FreightTerms { get; init; }
    public NfeCarrier? Carrier { get; init; }
    public NfeVehicle? Vehicle { get; init; }
    public decimal NetWeight { get; init; }
    public decimal GrossWeight { get; init; }
    public required PaymentPlan Payment { get; init; }

    /// <summary><c>cobr/fat/nFat</c> — o número fiscal do documento.</summary>
    public required string BillingNumber { get; init; }

    public string? AdditionalInfo { get; init; }
    public string? FiscoInfo { get; init; }
    public NfeTechnicalResponsible? TechnicalResponsible { get; init; }
}
```

```bash
git add SiagroB1.Domain/Enums/NfeEnvironment.cs SiagroB1.Fiscal/Nfe/NfeIssueInput.cs
```

- [ ] **Step 2: Dados de teste — o exemplo do spec do sub-projeto 1**

```csharp
// SiagroB1.Fiscal.Tests/Support/NfeTestData.cs
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Payments;

namespace SiagroB1.Fiscal.Tests.Support;

/// <summary>
/// O documento do exemplo do sub-projeto 1: CEAGUI (Itaberá/SP) vende 30.000 kg de soja a
/// R$ 2,00 para um cliente da BA — ICMS 7%, PIS/COFINS sem ICMS na base, IBS/CBS de 2026.
/// </summary>
public static class NfeTestData
{
    public static readonly DateTimeOffset IssuedAt = new(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(-3));

    public static NfeAddress Itabera() => new()
    {
        Street = "RODOVIA SP 258", Number = "KM 290", District = "ZONA RURAL",
        MunicipalityCode = "3521705", MunicipalityName = "Itaberá", State = "SP", ZipCode = "18440000",
        Phone = "1535621234",
    };

    public static NfeAddress Salvador() => new()
    {
        Street = "AV SETE DE SETEMBRO", Number = "100", Complement = "SALA 2", District = "CENTRO",
        MunicipalityCode = "2927408", MunicipalityName = "Salvador", State = "BA", ZipCode = "40060000",
    };

    public static NfeAddress SaoPaulo() => new()
    {
        Street = "AV PAULISTA", Number = "1000", District = "BELA VISTA",
        MunicipalityCode = "3550308", MunicipalityName = "São Paulo", State = "SP", ZipCode = "01310100",
    };

    public static NfeItem Item(int number = 1) => new()
    {
        Number = number, ItemCode = "SOJA", Description = "SOJA EM GRAOS", Ncm = "12019000", Cfop = "6102",
        UnitOfMeasure = "KG", Quantity = 30000m, UnitPrice = 2m, Total = 60000m, GoodsOrigin = 0,
        IcmsCode = "00", IcmsBase = 60000m, IcmsRate = 7m, IcmsValue = 4200m,
        PisCst = "01", PisBase = 55800m, PisRate = 1.65m, PisValue = 920.70m,
        CofinsCst = "01", CofinsBase = 55800m, CofinsRate = 7.6m, CofinsValue = 4240.80m,
        IbsCbsCst = "000", IbsCbsClassCode = "000001", IbsCbsBase = 50638.50m,
        IbsStateRate = 0.1m, IbsMunicipalRate = 0m, IbsStateValue = 50.64m, IbsMunicipalValue = 0m,
        CbsRate = 0.9m, CbsValue = 455.75m,
    };

    public static NfeIssueInput Input(
        NfeEnvironment environment = NfeEnvironment.Homologation,
        NfeAddress? recipientAddress = null,
        string issuerTaxId = "12345678000195") => new()
    {
        Environment = environment,
        Series = 1,
        Number = 123,
        RandomCode = "48151623",
        IssuedAt = IssuedAt,
        OperationNature = "VENDA DE PRODUCAO DO ESTABELECIMENTO",
        ApplicationVersion = "SiagroB1 1.0.116",
        Issuer = new NfeIssuer
        {
            TaxId = issuerTaxId, LegalName = "CEAGUI CEREAIS LTDA", TradeName = "CEAGUI",
            StateRegistration = "371012345110", TaxRegime = TaxRegime.Normal, Address = Itabera(),
        },
        Recipient = new NfeRecipient
        {
            TaxId = "11222333000181", Name = "CLIENTE BA LTDA", Indicator = StateRegistrationIndicator.Taxpayer,
            StateRegistration = "123456789", Email = "nfe@cliente.com.br", Address = recipientAddress ?? Salvador(),
        },
        Items = [Item()],
        FreightTerms = FreightTerms.Cif,
        Carrier = new NfeCarrier
        {
            TaxId = "33444555000122", Name = "TRANSPORTADORA TESTE LTDA", FullAddress = "RUA B, 10",
            MunicipalityName = "Itapeva", State = "SP",
        },
        Vehicle = new NfeVehicle("ABC1D23", "SP"),
        NetWeight = 30000m,
        GrossWeight = 30500m,
        Payment = PaymentInstallmentCalculator.Calculate("30,60", PaymentStartRule.IssueDate, "15", 60000m,
            DateOnly.FromDateTime(IssuedAt.Date)),
        BillingNumber = "000000123",
        AdditionalInfo = "Documento emitido por ME ou EPP optante pelo Simples Nacional? Nao.",
        FiscoInfo = "Pedido 77",
        TechnicalResponsible = new NfeTechnicalResponsible("09123456000100", "IDX Consultoria", "fiscal@idx.com.br", "1533334444"),
    };
}
```

```bash
git add SiagroB1.Fiscal.Tests/Support/NfeTestData.cs
```

- [ ] **Step 3: Write the failing test**

```csharp
// SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs
using NFe.Classes.Informacoes.Destinatario;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Classes.Informacoes.Pagamento;
using NFe.Classes.Informacoes.Transporte;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Payments;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>Tradução da entrada para o modelo da Zeus — spec §8, grupo a grupo.</summary>
public class NfeXmlBuilderTests
{
    private static NFe.Classes.NFe Build(NfeIssueInput input) => NfeXmlBuilder.Build(input);

    [Fact]
    public void Homologation_replaces_the_recipient_name_with_the_official_literal()
    {
        var nfe = Build(NfeTestData.Input(NfeEnvironment.Homologation));

        Assert.Equal("NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL", nfe.infNFe.dest.xNome);
        Assert.Equal(TipoAmbiente.Homologacao, nfe.infNFe.ide.tpAmb);
    }

    [Fact]
    public void Production_keeps_the_recipient_name()
    {
        var nfe = Build(NfeTestData.Input(NfeEnvironment.Production));

        Assert.Equal("CLIENTE BA LTDA", nfe.infNFe.dest.xNome);
        Assert.Equal(TipoAmbiente.Producao, nfe.infNFe.ide.tpAmb);
    }

    [Fact]
    public void Ide_comes_from_the_input_and_the_issuer_municipality()
    {
        var ide = Build(NfeTestData.Input()).infNFe.ide;

        Assert.Equal(Estado.SP, ide.cUF);
        Assert.Equal(3521705L, ide.cMunFG);
        Assert.Equal("48151623", ide.cNF);
        Assert.Equal(1, ide.serie);
        Assert.Equal(123L, ide.nNF);
        Assert.Equal(ModeloDocumento.NFe, ide.mod);
        Assert.Equal(NfeTestData.IssuedAt, ide.dhEmi);
        Assert.Equal(TipoNFe.tnSaida, ide.tpNF);
        Assert.Equal(FinalidadeNFe.fnNormal, ide.finNFe);
        Assert.Equal(PresencaComprador.pcOutros, ide.indPres);
        Assert.Equal(IndicadorIntermediador.iiSemIntermediador, ide.indIntermed);
        Assert.Equal("VENDA DE PRODUCAO DO ESTABELECIMENTO", ide.natOp);
    }

    [Fact]
    public void Operation_nature_is_cut_at_60_characters()
    {
        var input = NfeTestData.Input() with { OperationNature = new string('X', 80) };

        Assert.Equal(60, Build(input).infNFe.ide.natOp.Length);
    }

    [Fact]
    public void Interstate_recipient_sets_idDest_2_and_omits_the_vehicle()
    {
        var nfe = Build(NfeTestData.Input(recipientAddress: NfeTestData.Salvador()));

        Assert.Equal(DestinoOperacao.doInterestadual, nfe.infNFe.ide.idDest);
        Assert.Null(nfe.infNFe.transp.veicTransp);
    }

    [Fact]
    public void In_state_recipient_sets_idDest_1_and_sends_the_vehicle()
    {
        var nfe = Build(NfeTestData.Input(recipientAddress: NfeTestData.SaoPaulo()));

        Assert.Equal(DestinoOperacao.doInterna, nfe.infNFe.ide.idDest);
        Assert.Equal("ABC1D23", nfe.infNFe.transp.veicTransp.placa);
    }

    [Fact]
    public void Non_taxpayer_is_final_consumer_without_state_registration()
    {
        var input = NfeTestData.Input();
        input = input with
        {
            Recipient = input.Recipient with
            {
                TaxId = "12345678909", Indicator = StateRegistrationIndicator.NonTaxpayer, StateRegistration = "123",
            },
        };

        var nfe = Build(input);

        Assert.Equal(ConsumidorFinal.cfConsumidorFinal, nfe.infNFe.ide.indFinal);
        Assert.Equal(indIEDest.NaoContribuinte, nfe.infNFe.dest.indIEDest);
        Assert.Null(nfe.infNFe.dest.IE);
        Assert.Equal("12345678909", nfe.infNFe.dest.CPF);
        Assert.Null(nfe.infNFe.dest.CNPJ);
    }

    [Fact]
    public void Taxpayer_sends_the_state_registration()
    {
        var nfe = Build(NfeTestData.Input());

        Assert.Equal(ConsumidorFinal.cfNao, nfe.infNFe.ide.indFinal);
        Assert.Equal(indIEDest.ContribuinteICMS, nfe.infNFe.dest.indIEDest);
        Assert.Equal("123456789", nfe.infNFe.dest.IE);
        Assert.Equal("11222333000181", nfe.infNFe.dest.CNPJ);
    }

    [Theory]
    [InlineData("00", typeof(ICMS00))]
    [InlineData("20", typeof(ICMS20))]
    [InlineData("40", typeof(ICMS40))]
    [InlineData("41", typeof(ICMS40))]
    [InlineData("50", typeof(ICMS40))]
    [InlineData("51", typeof(ICMS51))]
    [InlineData("90", typeof(ICMS90))]
    [InlineData("102", typeof(ICMSSN102))]
    [InlineData("300", typeof(ICMSSN102))]
    [InlineData("900", typeof(ICMSSN900))]
    public void Icms_group_follows_the_recorded_code(string code, Type expected)
    {
        var input = NfeTestData.Input() with { Items = [NfeTestData.Item() with { IcmsCode = code }] };

        Assert.IsType(expected, Build(input).infNFe.det[0].imposto.ICMS.TipoICMS);
    }

    [Fact]
    public void Icms51_carries_the_deferral()
    {
        var item = NfeTestData.Item() with
        {
            IcmsCode = "51", IcmsBase = 60000m, IcmsRate = 18m, IcmsOperationValue = 10800m, IcmsDeferral = 100m,
            IcmsDeferredValue = 10800m, IcmsValue = 0m,
        };

        var icms = Assert.IsType<ICMS51>(Build(NfeTestData.Input() with { Items = [item] }).infNFe.det[0].imposto.ICMS.TipoICMS);

        Assert.Equal(10800m, icms.vICMSOp);
        Assert.Equal(100m, icms.pDif);
        Assert.Equal(10800m, icms.vICMSDif);
        Assert.Equal(0m, icms.vICMS);
        Assert.Null(icms.pRedBC);
    }

    [Theory]
    [InlineData("01", typeof(PISAliq))]
    [InlineData("02", typeof(PISAliq))]
    [InlineData("06", typeof(PISNT))]
    [InlineData("09", typeof(PISNT))]
    [InlineData("49", typeof(PISOutr))]
    public void Pis_group_follows_the_cst(string cst, Type expected)
    {
        var input = NfeTestData.Input() with { Items = [NfeTestData.Item() with { PisCst = cst, CofinsCst = cst }] };
        var imposto = Build(input).infNFe.det[0].imposto;

        Assert.IsType(expected, imposto.PIS.TipoPIS);
        Assert.Equal(expected.Name.Replace("PIS", "COFINS"), imposto.COFINS.TipoCOFINS.GetType().Name);
    }

    [Fact]
    public void Ibs_cbs_group_carries_rates_and_values()
    {
        var group = Build(NfeTestData.Input()).infNFe.det[0].imposto.IBSCBS;

        Assert.Equal("000001", group.cClassTrib);
        Assert.Equal(50638.50m, group.gIBSCBS.vBC);
        Assert.Equal(0.1m, group.gIBSCBS.gIBSUF.pIBSUF);
        Assert.Equal(50.64m, group.gIBSCBS.gIBSUF.vIBSUF);
        Assert.Equal(50.64m, group.gIBSCBS.vIBS);
        Assert.Equal(455.75m, group.gIBSCBS.gCBS.vCBS);
        Assert.Null(group.gIBSCBS.gCBS.gRed);
    }

    [Fact]
    public void Ibs_cbs_reduction_fills_the_effective_rate()
    {
        var item = NfeTestData.Item() with { IbsCbsCst = "200", CbsRateReduction = 60m, IbsRateReduction = 60m };

        var group = Build(NfeTestData.Input() with { Items = [item] }).infNFe.det[0].imposto.IBSCBS;

        Assert.Equal(60m, group.gIBSCBS.gCBS.gRed.pRedAliq);
        Assert.Equal(0.36m, group.gIBSCBS.gCBS.gRed.pAliqEfet);
        Assert.Equal(0.04m, group.gIBSCBS.gIBSUF.gRed.pAliqEfet);
    }

    [Fact]
    public void Ibs_cbs_without_values_has_no_detail_group()
    {
        var item = NfeTestData.Item() with { IbsCbsCst = "410" };

        var group = Build(NfeTestData.Input() with { Items = [item] }).infNFe.det[0].imposto.IBSCBS;

        Assert.Null(group.gIBSCBS);
    }

    [Fact]
    public void Line_without_ibs_cbs_has_no_group_and_no_total()
    {
        var item = NfeTestData.Item() with { IbsCbsCst = null, IbsCbsClassCode = null };

        var nfe = Build(NfeTestData.Input() with { Items = [item] });

        Assert.Null(nfe.infNFe.det[0].imposto.IBSCBS);
        Assert.Null(nfe.infNFe.total.IBSCBSTot);
    }

    [Fact]
    public void Totals_add_up_the_lines()
    {
        var input = NfeTestData.Input() with { Items = [NfeTestData.Item(1), NfeTestData.Item(2) with { IcmsCode = "41" }] };

        var total = Build(input).infNFe.total;

        Assert.Equal(120000m, total.ICMSTot.vProd);
        Assert.Equal(120000m, total.ICMSTot.vNF);
        Assert.Equal(60000m, total.ICMSTot.vBC);      // o item 41 não leva base
        Assert.Equal(4200m, total.ICMSTot.vICMS);
        Assert.Equal(1841.40m, total.ICMSTot.vPIS);
        Assert.Equal(8481.60m, total.ICMSTot.vCOFINS);
        Assert.Equal(101277.00m, total.IBSCBSTot.vBCIBSCBS);
        Assert.Equal(911.50m, total.IBSCBSTot.gCBS.vCBS);
        Assert.Equal(101.28m, total.IBSCBSTot.gIBS.vIBS);
    }

    [Theory]
    [InlineData(FreightTerms.Cif, ModalidadeFrete.mfContaEmitenteOumfContaRemetente)]
    [InlineData(FreightTerms.Fob, ModalidadeFrete.mfContaDestinatario)]
    [InlineData(FreightTerms.Ter, ModalidadeFrete.mfContaTerceiros)]
    [InlineData(FreightTerms.None, ModalidadeFrete.mfSemFrete)]
    public void Freight_terms_map_to_modFrete(FreightTerms terms, ModalidadeFrete expected)
    {
        Assert.Equal(expected, Build(NfeTestData.Input() with { FreightTerms = terms }).infNFe.transp.modFrete);
    }

    [Fact]
    public void Carrier_and_weights_go_to_transp()
    {
        var transp = Build(NfeTestData.Input()).infNFe.transp;

        Assert.Equal("33444555000122", transp.transporta.CNPJ);
        Assert.Equal("TRANSPORTADORA TESTE LTDA", transp.transporta.xNome);
        Assert.Equal(30000m, transp.vol.Single().pesoL);
        Assert.Equal(30500m, transp.vol.Single().pesoB);
    }

    [Fact]
    public void Billing_and_payment_follow_the_plan()
    {
        var nfe = Build(NfeTestData.Input());

        Assert.Equal("000000123", nfe.infNFe.cobr.fat.nFat);
        Assert.Equal(60000m, nfe.infNFe.cobr.fat.vLiq);
        Assert.Equal(["001", "002"], nfe.infNFe.cobr.dup.Select(d => d.nDup));
        Assert.Equal([30000m, 30000m], nfe.infNFe.cobr.dup.Select(d => d.vDup));

        var detPag = nfe.infNFe.pag.Single().detPag.Single();
        Assert.Equal(IndicadorPagamentoDetalhePagamento.ipDetPgPrazo, detPag.indPag);
        Assert.Equal(15, (int)detPag.tPag);
        Assert.Equal(60000m, detPag.vPag);
    }

    [Fact]
    public void No_payment_means_omits_billing_and_pays_zero()
    {
        var input = NfeTestData.Input() with
        {
            Payment = PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.IssueDate, "90", 60000m, new DateOnly(2026, 10, 2)),
        };

        var nfe = Build(input);
        var detPag = nfe.infNFe.pag.Single().detPag.Single();

        Assert.Null(nfe.infNFe.cobr);
        Assert.Equal(FormaPagamento.fpSemPagamento, detPag.tPag);
        Assert.Equal(0m, detPag.vPag);
        Assert.Null(detPag.indPag);
    }

    [Fact]
    public void Other_payment_means_describes_itself()
    {
        var input = NfeTestData.Input() with
        {
            Payment = PaymentInstallmentCalculator.Calculate("0", PaymentStartRule.IssueDate, "99", 60000m, new DateOnly(2026, 10, 2)),
        };

        Assert.Equal("Outros", Build(input).infNFe.pag.Single().detPag.Single().xPag);
    }

    [Fact]
    public void Delivery_group_only_when_informed()
    {
        Assert.Null(Build(NfeTestData.Input()).infNFe.entrega);

        var input = NfeTestData.Input() with
        {
            Delivery = new NfeDelivery { TaxId = "44555666000177", Name = "ARMAZEM DESTINO", Address = NfeTestData.SaoPaulo() },
        };

        var entrega = Build(input).infNFe.entrega;
        Assert.Equal("44555666000177", entrega.CNPJ);
        Assert.Equal(3550308L, entrega.cMun);
        Assert.Equal("SP", entrega.UF);
    }

    [Fact]
    public void Texts_and_technical_responsible_are_sent()
    {
        var nfe = Build(NfeTestData.Input());

        Assert.Contains("Simples Nacional", nfe.infNFe.infAdic.infCpl);
        Assert.Equal("Pedido 77", nfe.infNFe.infAdic.infAdFisco);
        Assert.Equal("09123456000100", nfe.infNFe.infRespTec.CNPJ);
    }

    [Fact]
    public void Issuer_uses_crt_and_municipality()
    {
        var emit = Build(NfeTestData.Input()).infNFe.emit;

        Assert.Equal("12345678000195", emit.CNPJ);
        Assert.Equal(NFe.Classes.Informacoes.Emitente.CRT.RegimeNormal, emit.CRT);
        Assert.Equal(Estado.SP, emit.enderEmit.UF);
        Assert.Equal(3521705L, emit.enderEmit.cMun);
        Assert.Equal(1535621234L, emit.enderEmit.fone);
    }
}
```

Conta do teste de totais: dois itens iguais ao exemplo; o segundo com ICMS 41 (sem base nem valor de ICMS), mas a fotografia mantém os valores de PIS/COFINS/IBS/CBS: vPIS = 2 × 920,70; vCOFINS = 2 × 4.240,80; base IBS/CBS = 2 × 50.638,50; CBS = 2 × 455,75; IBS = 2 × 50,64.

```bash
git add SiagroB1.Fiscal.Tests/Nfe/NfeXmlBuilderTests.cs
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeXmlBuilderTests"`
Expected: FAIL de compilação — `NfeXmlBuilder` não existe.

- [ ] **Step 5: Implementar o builder**

```csharp
// SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs
using System.Globalization;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using NFe.Classes.Informacoes;
using NFe.Classes.Informacoes.Cobranca;
using NFe.Classes.Informacoes.Destinatario;
using NFe.Classes.Informacoes.Detalhe;
using NFe.Classes.Informacoes.Detalhe.Tributacao;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs.InformacoesCbs;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.InformacoesIbsCbs.InformacoesIbs;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Estadual.Tipos;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal;
using NFe.Classes.Informacoes.Detalhe.Tributacao.Federal.Tipos;
using NFe.Classes.Informacoes.Emitente;
using NFe.Classes.Informacoes.Identificacao;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Classes.Informacoes.Observacoes;
using NFe.Classes.Informacoes.Pagamento;
using NFe.Classes.Informacoes.Total;
using NFe.Classes.Informacoes.Total.IbsCbs;
using NFe.Classes.Informacoes.Total.IbsCbs.Cbs;
using NFe.Classes.Informacoes.Total.IbsCbs.Ibs;
using NFe.Classes.Informacoes.Transporte;
using Shared.NFe.Classes.Informacoes.InfRespTec;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Payments;
using IbsCbsCst = NFe.Classes.Informacoes.Detalhe.Tributacao.Compartilhado.Tipos.CST;
using ZeusNFe = NFe.Classes.NFe;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// <see cref="NfeIssueInput"/> → modelo da Zeus (spec §8). Não assina nem valida: isso é do
/// <c>NfeSigner</c>. As regras de "leva valor ou não" de cada grupo moram num lugar só
/// (<see cref="IcmsCarriesValues"/>, <see cref="PisCofinsCarriesValues"/>,
/// <see cref="IbsCbsCarriesValues"/>) e valem para a linha e para os totais — vBC/vICMS do total
/// diferente da soma dos itens é rejeição.
/// </summary>
public static class NfeXmlBuilder
{
    /// <summary>Literal oficial (NT 2011/002, rejeição 598) — sem acento. A Zeus não faz a troca.</summary>
    public const string HomologationRecipientName = "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

    private const string WithoutGtin = "SEM GTIN";
    private const int Brazil = 1058;
    private const string BrazilName = "BRASIL";

    private static readonly HashSet<string> IcmsCodesWithValues = ["00", "20", "51", "90", "900"];
    private static readonly HashSet<string> PisCofinsNotTaxed = ["04", "05", "06", "07", "08", "09"];
    private static readonly HashSet<string> IbsCbsCodesWithValues = ["000", "200"];

    public static ZeusNFe Build(NfeIssueInput input)
    {
        var interstate = !string.Equals(
            input.Issuer.Address.State, input.Recipient.Address.State, StringComparison.OrdinalIgnoreCase);

        return new ZeusNFe
        {
            infNFe = new infNFe
            {
                versao = "4.00",
                ide = BuildIde(input, interstate),
                emit = BuildIssuer(input.Issuer),
                dest = BuildRecipient(input),
                entrega = input.Delivery is null ? null : BuildDelivery(input.Delivery),
                det = input.Items.Select(BuildItem).ToList(),
                total = BuildTotal(input.Items),
                transp = BuildTransport(input, interstate),
                cobr = BuildBilling(input),
                pag = BuildPayment(input.Payment),
                infAdic = BuildAdditionalInfo(input),
                infRespTec = input.TechnicalResponsible is { } tech
                    ? new infRespTec { CNPJ = tech.Cnpj, xContato = tech.Contact, email = tech.Email, fone = tech.Phone }
                    : null,
            },
        };
    }

    internal static bool IcmsCarriesValues(string code) => IcmsCodesWithValues.Contains(code);

    internal static bool PisCofinsCarriesValues(string cst) => !PisCofinsNotTaxed.Contains(cst);

    internal static bool IbsCbsCarriesValues(string? cst) => cst is not null && IbsCbsCodesWithValues.Contains(cst);

    private static ide BuildIde(NfeIssueInput input, bool interstate) => new()
    {
        cUF = (Estado)int.Parse(input.Issuer.Address.MunicipalityCode[..2], CultureInfo.InvariantCulture),
        cNF = input.RandomCode,
        natOp = Truncate(input.OperationNature, 60),
        mod = ModeloDocumento.NFe,
        serie = input.Series,
        nNF = input.Number,
        dhEmi = input.IssuedAt,
        dhSaiEnt = input.IssuedAt,
        tpNF = TipoNFe.tnSaida,
        idDest = interstate ? DestinoOperacao.doInterestadual : DestinoOperacao.doInterna,
        cMunFG = long.Parse(input.Issuer.Address.MunicipalityCode, CultureInfo.InvariantCulture),
        tpImp = TipoImpressao.tiRetrato,
        tpEmis = TipoEmissao.teNormal,
        tpAmb = Environment(input.Environment),
        finNFe = FinalidadeNFe.fnNormal,
        indFinal = input.Recipient.Indicator == StateRegistrationIndicator.NonTaxpayer
            ? ConsumidorFinal.cfConsumidorFinal
            : ConsumidorFinal.cfNao,
        indPres = PresencaComprador.pcOutros,
        indIntermed = IndicadorIntermediador.iiSemIntermediador,
        procEmi = ProcessoEmissao.peAplicativoContribuinte,
        verProc = Truncate(input.ApplicationVersion, 20),
    };

    internal static TipoAmbiente Environment(NfeEnvironment environment) =>
        environment == NfeEnvironment.Production ? TipoAmbiente.Producao : TipoAmbiente.Homologacao;

    private static emit BuildIssuer(NfeIssuer issuer)
    {
        var address = issuer.Address;
        var result = new emit
        {
            xNome = Truncate(issuer.LegalName, 60),
            xFant = Blank(issuer.TradeName),
            IE = issuer.StateRegistration,
            CRT = issuer.TaxRegime switch
            {
                TaxRegime.SimplesNacional => CRT.SimplesNacional,
                TaxRegime.SimplesNacionalExcess => CRT.SimplesNacionalExcessoSublimite,
                TaxRegime.Mei => CRT.SimplesNacionalMei,
                _ => CRT.RegimeNormal,
            },
            enderEmit = new enderEmit
            {
                xLgr = address.Street,
                nro = address.Number,
                xCpl = Blank(address.Complement),
                xBairro = address.District,
                cMun = long.Parse(address.MunicipalityCode, CultureInfo.InvariantCulture),
                xMun = address.MunicipalityName,
                UF = Enum.Parse<Estado>(address.State, ignoreCase: true),
                CEP = address.ZipCode,
                cPais = Brazil,
                xPais = BrazilName,
                fone = Phone(address.Phone),
            },
        };

        if (issuer.TaxId.Length == 11)
            result.CPF = issuer.TaxId;
        else
            result.CNPJ = issuer.TaxId;

        return result;
    }

    private static dest BuildRecipient(NfeIssueInput input)
    {
        var recipient = input.Recipient;
        var address = recipient.Address;

        var result = new dest(VersaoServico.Versao400)
        {
            xNome = input.Environment == NfeEnvironment.Homologation
                ? HomologationRecipientName
                : Truncate(recipient.Name, 60),
            indIEDest = recipient.Indicator switch
            {
                StateRegistrationIndicator.Taxpayer => indIEDest.ContribuinteICMS,
                StateRegistrationIndicator.Exempt => indIEDest.Isento,
                _ => indIEDest.NaoContribuinte,
            },
            // Só o contribuinte leva IE: isento e não contribuinte não informam (schema e NT).
            IE = recipient.Indicator == StateRegistrationIndicator.Taxpayer ? recipient.StateRegistration : null,
            email = Blank(recipient.Email),
            enderDest = new enderDest
            {
                xLgr = address.Street,
                nro = address.Number,
                xCpl = Blank(address.Complement),
                xBairro = address.District,
                cMun = long.Parse(address.MunicipalityCode, CultureInfo.InvariantCulture),
                xMun = address.MunicipalityName,
                UF = address.State,
                CEP = address.ZipCode,
                cPais = Brazil,
                xPais = BrazilName,
                fone = Phone(address.Phone),
            },
        };

        if (recipient.TaxId.Length == 11)
            result.CPF = recipient.TaxId;
        else
            result.CNPJ = recipient.TaxId;

        return result;
    }

    private static entrega BuildDelivery(NfeDelivery delivery)
    {
        var address = delivery.Address;
        var result = new entrega
        {
            xNome = Truncate(delivery.Name, 60),
            xLgr = address.Street,
            nro = address.Number,
            xCpl = Blank(address.Complement),
            xBairro = address.District,
            cMun = long.Parse(address.MunicipalityCode, CultureInfo.InvariantCulture),
            xMun = address.MunicipalityName,
            UF = address.State,
            cPais = Brazil,
            xPais = BrazilName,
            IE = Blank(delivery.StateRegistration),
        };

        if (!string.IsNullOrWhiteSpace(address.ZipCode))
            result.CEP = long.Parse(address.ZipCode, CultureInfo.InvariantCulture);

        if (delivery.TaxId.Length == 11)
            result.CPF = delivery.TaxId;
        else
            result.CNPJ = delivery.TaxId;

        return result;
    }

    private static det BuildItem(NfeItem item) => new()
    {
        nItem = item.Number,
        prod = new prod
        {
            cProd = item.ItemCode,
            cEAN = WithoutGtin,
            xProd = Truncate(item.Description, 120),
            NCM = item.Ncm,
            cBenef = Blank(item.BenefitCode),
            CFOP = int.Parse(item.Cfop, CultureInfo.InvariantCulture),
            uCom = Truncate(item.UnitOfMeasure, 6),
            qCom = item.Quantity,
            vUnCom = item.UnitPrice,
            vProd = item.Total,
            cEANTrib = WithoutGtin,
            uTrib = Truncate(item.UnitOfMeasure, 6),
            qTrib = item.Quantity,
            vUnTrib = item.UnitPrice,
            indTot = IndicadorTotal.ValorDoItemCompoeTotalNF,
        },
        imposto = new imposto
        {
            ICMS = new ICMS { TipoICMS = BuildIcms(item) },
            PIS = new PIS { TipoPIS = BuildPis(item) },
            COFINS = new COFINS { TipoCOFINS = BuildCofins(item) },
            IBSCBS = BuildIbsCbs(item),
        },
    };

    private static ICMSBasico BuildIcms(NfeItem item)
    {
        var origin = (OrigemMercadoria)item.GoodsOrigin;
        decimal? reduction = item.IcmsBaseReduction > 0 ? item.IcmsBaseReduction : null;

        return item.IcmsCode switch
        {
            "00" => new ICMS00
            {
                orig = origin, CST = Csticms.Cst00, modBC = DeterminacaoBaseIcms.DbiValorOperacao,
                vBC = item.IcmsBase, pICMS = item.IcmsRate, vICMS = item.IcmsValue,
            },
            "20" => new ICMS20
            {
                orig = origin, CST = Csticms.Cst20, modBC = DeterminacaoBaseIcms.DbiValorOperacao,
                pRedBC = item.IcmsBaseReduction, vBC = item.IcmsBase, pICMS = item.IcmsRate, vICMS = item.IcmsValue,
            },
            "40" => new ICMS40 { orig = origin, CST = Csticms.Cst40 },
            "41" => new ICMS40 { orig = origin, CST = Csticms.Cst41 },
            "50" => new ICMS40 { orig = origin, CST = Csticms.Cst50 },
            "51" => new ICMS51
            {
                orig = origin, CST = Csticms.Cst51, modBC = DeterminacaoBaseIcms.DbiValorOperacao, pRedBC = reduction,
                vBC = item.IcmsBase, pICMS = item.IcmsRate, vICMSOp = item.IcmsOperationValue,
                pDif = item.IcmsDeferral, vICMSDif = item.IcmsDeferredValue, vICMS = item.IcmsValue,
            },
            "90" => new ICMS90
            {
                orig = origin, CST = Csticms.Cst90, modBC = DeterminacaoBaseIcms.DbiValorOperacao, pRedBC = reduction,
                vBC = item.IcmsBase, pICMS = item.IcmsRate, vICMS = item.IcmsValue,
            },
            "102" or "103" or "300" or "400" => new ICMSSN102
            {
                orig = origin, CSOSN = Enum.Parse<Csosnicms>("Csosn" + item.IcmsCode),
            },
            "900" => new ICMSSN900
            {
                orig = origin, CSOSN = Csosnicms.Csosn900, modBC = DeterminacaoBaseIcms.DbiValorOperacao, pRedBC = reduction,
                vBC = item.IcmsBase, pICMS = item.IcmsRate, vICMS = item.IcmsValue,
            },
            _ => throw new DefaultException(
                $"O item {item.ItemCode} tem o código de ICMS {item.IcmsCode}, que este sistema não emite."),
        };
    }

    private static PISBasico BuildPis(NfeItem item)
    {
        var cst = Enum.Parse<CSTPIS>("pis" + item.PisCst);

        if (item.PisCst is "01" or "02")
            return new PISAliq { CST = cst, vBC = item.PisBase, pPIS = item.PisRate, vPIS = item.PisValue };

        if (!PisCofinsCarriesValues(item.PisCst))
            return new PISNT { CST = cst };

        return new PISOutr { CST = cst, vBC = item.PisBase, pPIS = item.PisRate, vPIS = item.PisValue };
    }

    private static COFINSBasico BuildCofins(NfeItem item)
    {
        var cst = Enum.Parse<CSTCOFINS>("cofins" + item.CofinsCst);

        if (item.CofinsCst is "01" or "02")
            return new COFINSAliq { CST = cst, vBC = item.CofinsBase, pCOFINS = item.CofinsRate, vCOFINS = item.CofinsValue };

        if (!PisCofinsCarriesValues(item.CofinsCst))
            return new COFINSNT { CST = cst };

        return new COFINSOutr { CST = cst, vBC = item.CofinsBase, pCOFINS = item.CofinsRate, vCOFINS = item.CofinsValue };
    }

    private static IBSCBS? BuildIbsCbs(NfeItem item)
    {
        if (item.IbsCbsCst is null)
            return null;

        var group = new IBSCBS
        {
            CST = Enum.Parse<IbsCbsCst>("Cst" + item.IbsCbsCst),
            cClassTrib = item.IbsCbsClassCode,
        };

        if (!IbsCbsCarriesValues(item.IbsCbsCst))
            return group;

        group.gIBSCBS = new gIBSCBS
        {
            vBC = item.IbsCbsBase,
            gIBSUF = new gIBSUF
            {
                pIBSUF = item.IbsStateRate,
                gRed = Reduction(item.IbsStateRate, item.IbsRateReduction),
                vIBSUF = item.IbsStateValue,
            },
            gIBSMun = new gIBSMun
            {
                pIBSMun = item.IbsMunicipalRate,
                gRed = Reduction(item.IbsMunicipalRate, item.IbsRateReduction),
                vIBSMun = item.IbsMunicipalValue,
            },
            vIBS = item.IbsStateValue + item.IbsMunicipalValue,
            gCBS = new gCBS
            {
                pCBS = item.CbsRate,
                gRed = Reduction(item.CbsRate, item.CbsRateReduction),
                vCBS = item.CbsValue,
            },
        };

        return group;
    }

    /// <summary>Redução de alíquota: <c>pAliqEfet</c> = alíquota × (1 − redução/100), 4 casas.</summary>
    private static gRed? Reduction(decimal rate, decimal reduction) =>
        reduction <= 0
            ? null
            : new gRed
            {
                pRedAliq = reduction,
                pAliqEfet = decimal.Round(rate * (1 - reduction / 100m), 4, MidpointRounding.AwayFromZero),
            };

    private static total BuildTotal(IReadOnlyList<NfeItem> items)
    {
        var products = items.Sum(i => i.Total);
        var withIcms = items.Where(i => IcmsCarriesValues(i.IcmsCode)).ToList();
        var withIbsCbs = items.Where(i => IbsCbsCarriesValues(i.IbsCbsCst)).ToList();

        return new total
        {
            ICMSTot = new ICMSTot
            {
                vBC = withIcms.Sum(i => i.IcmsBase),
                vICMS = withIcms.Sum(i => i.IcmsValue),
                vICMSDeson = 0,
                vFCP = 0,
                vBCST = 0,
                vST = 0,
                vFCPST = 0,
                vFCPSTRet = 0,
                vProd = products,
                vFrete = 0,
                vSeg = 0,
                vDesc = 0,
                vII = 0,
                vIPI = 0,
                vIPIDevol = 0,
                vPIS = items.Where(i => PisCofinsCarriesValues(i.PisCst)).Sum(i => i.PisValue),
                vCOFINS = items.Where(i => PisCofinsCarriesValues(i.CofinsCst)).Sum(i => i.CofinsValue),
                vOutro = 0,
                vNF = products,
            },
            IBSCBSTot = items.Any(i => i.IbsCbsCst is not null)
                ? new IBSCBSTot
                {
                    vBCIBSCBS = withIbsCbs.Sum(i => i.IbsCbsBase),
                    gIBS = new gIBS
                    {
                        gIBSUF = new gIBSUFTotal { vDif = 0, vDevTrib = 0, vIBSUF = withIbsCbs.Sum(i => i.IbsStateValue) },
                        gIBSMun = new gIBSMunTotal { vDif = 0, vDevTrib = 0, vIBSMun = withIbsCbs.Sum(i => i.IbsMunicipalValue) },
                        vIBS = withIbsCbs.Sum(i => i.IbsStateValue + i.IbsMunicipalValue),
                        vCredPres = 0,
                        vCredPresCondSus = 0,
                    },
                    gCBS = new gCBSTotal
                    {
                        vDif = 0, vDevTrib = 0, vCBS = withIbsCbs.Sum(i => i.CbsValue), vCredPres = 0, vCredPresCondSus = 0,
                    },
                }
                : null,
        };
    }

    private static transp BuildTransport(NfeIssueInput input, bool interstate)
    {
        var result = new transp
        {
            modFrete = input.FreightTerms switch
            {
                FreightTerms.Cif => ModalidadeFrete.mfContaEmitenteOumfContaRemetente,
                FreightTerms.Fob => ModalidadeFrete.mfContaDestinatario,
                FreightTerms.Ter => ModalidadeFrete.mfContaTerceiros,
                _ => ModalidadeFrete.mfSemFrete,
            },
        };

        if (input.Carrier is { } carrier)
        {
            result.transporta = new transporta
            {
                xNome = Truncate(carrier.Name, 60),
                IE = Blank(carrier.StateRegistration),
                xEnder = carrier.FullAddress is null ? null : Truncate(carrier.FullAddress, 60),
                xMun = Blank(carrier.MunicipalityName),
                UF = Blank(carrier.State),
            };

            if (carrier.TaxId is { Length: 11 })
                result.transporta.CPF = carrier.TaxId;
            else if (!string.IsNullOrWhiteSpace(carrier.TaxId))
                result.transporta.CNPJ = carrier.TaxId;
        }

        // Veículo só em operação interna (spec §8 e risco §14): na interestadual a SEFAZ rejeita.
        if (!interstate && input.Vehicle is { } vehicle)
            result.veicTransp = new veicTransp { placa = vehicle.Plate, UF = vehicle.State };

        if (input.NetWeight > 0 || input.GrossWeight > 0)
            result.vol = [new vol { pesoL = input.NetWeight, pesoB = input.GrossWeight }];

        return result;
    }

    private static cobr? BuildBilling(NfeIssueInput input)
    {
        if (input.Payment.Installments.Count == 0)
            return null;

        var total = input.Items.Sum(i => i.Total);

        return new cobr
        {
            fat = new fat { nFat = input.BillingNumber, vOrig = total, vDesc = 0, vLiq = total },
            dup = input.Payment.Installments
                .Select(i => new dup
                {
                    nDup = i.Number.ToString("000", CultureInfo.InvariantCulture),
                    dVenc = i.DueDate.ToDateTime(TimeOnly.MinValue),
                    vDup = i.Amount,
                })
                .ToList(),
        };
    }

    private static List<pag> BuildPayment(PaymentPlan plan) =>
    [
        new pag
        {
            detPag =
            [
                new detPag
                {
                    indPag = plan.PaymentIndicator switch
                    {
                        0 => IndicadorPagamentoDetalhePagamento.ipDetPgVista,
                        1 => IndicadorPagamentoDetalhePagamento.ipDetPgPrazo,
                        _ => null,
                    },
                    tPag = (FormaPagamento)int.Parse(plan.PaymentMeans, CultureInfo.InvariantCulture),
                    // tPag 99 exige a descrição (NT 2020.006).
                    xPag = plan.PaymentMeans == PaymentMeansCodes.Other ? "Outros" : null,
                    vPag = plan.PaidAmount,
                },
            ],
        },
    ];

    private static infAdic? BuildAdditionalInfo(NfeIssueInput input)
    {
        var complementary = Blank(input.AdditionalInfo);
        var fisco = Blank(input.FiscoInfo);

        if (complementary is null && fisco is null)
            return null;

        return new infAdic
        {
            infCpl = complementary is null ? null : Truncate(complementary, 5000),
            infAdFisco = fisco is null ? null : Truncate(fisco, 2000),
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int length)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= length ? trimmed : trimmed[..length];
    }

    private static long? Phone(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        return digits.Length is >= 6 and <= 14 ? long.Parse(digits, CultureInfo.InvariantCulture) : null;
    }
}
```

Se o compilador acusar o tipo de alguma propriedade (ex.: `veicTransp.UF` como `Estado` em vez de `string`, ou `detPag.indPag` não anulável), ajuste só a conversão daquela atribuição — os nomes de classe, propriedade e enum acima foram conferidos nos metadados do pacote.

```bash
git add SiagroB1.Fiscal/Nfe/NfeXmlBuilder.cs
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeXmlBuilderTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git commit -m "feat(invoice): montar o XML da NF-e a partir do documento calculado" -m "Entrada já resolvida (NfeIssueInput) → modelo da Zeus, grupo a grupo do §8 do spec: homologação troca o nome do destinatário pelo literal oficial sem acento, veículo só em operação interna, ICMS/PIS/COFINS pelo código gravado na linha, IBS/CBS com redução, totais somados pela mesma regra dos itens, cobrança e pagamento pela condição.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Domain/Enums/NfeEnvironment.cs SiagroB1.Fiscal/Nfe SiagroB1.Fiscal.Tests/Support/NfeTestData.cs SiagroB1.Fiscal.Tests/Nfe
```

---

### Task 10: Assinatura, schemas oficiais e `procNFe`

**Files:**
- Create: `SiagroB1.Fiscal/Schemas/*.xsd` (baixados do repositório da Zeus), `SiagroB1.Fiscal/Nfe/NfeServiceSettings.cs`, `NfeZeusConfiguration.cs`, `NfeSigner.cs`, `NfeProcComposer.cs`
- Modify: `SiagroB1.Fiscal/SiagroB1.Fiscal.csproj`
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeSignerTests.cs`, `NfeProcComposerTests.cs`

**Interfaces:**
- Consumes: `NfeXmlBuilder`, `NfeIssueInput` (Task 9); `CertificateLoader` (Task 8); `TestCertificates`, `NfeTestData`.
- Produces:
  - `record NfeServiceSettings(NfeEnvironment Environment, string IssuerState, X509Certificate2 Certificate, string SchemasDirectory, int TimeoutMilliseconds = 60_000, bool ValidateServerCertificate = true)` + `NfeServiceSettings.DefaultSchemasDirectory`.
  - `NfeZeusConfiguration.Create(NfeServiceSettings) : ConfiguracaoServico` (internal).
  - `class SignedNfe { string Xml; string AccessKey; internal NFe.Classes.NFe Document }`; `class NfeValidationException : Exception`.
  - `NfeSigner.BuildSignAndValidate(NfeIssueInput, NfeServiceSettings) : SignedNfe`; `NfeSigner.SignAndValidate(NFe.Classes.NFe, NfeServiceSettings) : SignedNfe`.
  - `NfeProcComposer.Compose(string signedNfeXml, string protocolXml) : string`.
  - Pasta `Schemas` copiada para a saída de todo projeto que referencia o `Fiscal` (Web, Reports, testes).

- [ ] **Step 1: Baixar os schemas 010d v1.03 e fechar a lista de includes**

```bash
BASE=https://raw.githubusercontent.com/ZeusAutomacao/DFe.NET/master/NFe.AppTeste/Schemas
DIR=SiagroB1.Fiscal/Schemas
mkdir -p "$DIR"
for f in nfe_v4.00.xsd enviNFe_v4.00.xsd consSitNFe_v4.00.xsd consStatServ_v4.00.xsd procNFe_v4.00.xsd; do
  curl -fsSL "$BASE/$f" -o "$DIR/$f"
done
# Baixa todo arquivo citado em schemaLocation que ainda falte, até a lista fechar.
while :; do
  missing=$(grep -ho 'schemaLocation="[^"]*"' "$DIR"/*.xsd | sed 's/schemaLocation="\(.*\)"/\1/' | sort -u | while read -r s; do [ -f "$DIR/$s" ] || echo "$s"; done)
  [ -z "$missing" ] && break
  for s in $missing; do curl -fsSL "$BASE/$s" -o "$DIR/$s"; done
done
ls "$DIR"
grep -c "\[0-9A-Z\]{12}\[0-9\]{2}" "$DIR"/tiposBasico_v4.00.xsd "$DIR"/DFeTiposBasicos_v1.00.xsd
git add "$DIR"
```

Expected: a lista inclui no mínimo `nfe_v4.00.xsd`, `leiauteNFe_v4.00.xsd`, `tiposBasico_v4.00.xsd`, `xmldsig-core-schema_v1.01.xsd`, `DFeTiposBasicos_v1.00.xsd`, `enviNFe_v4.00.xsd`, `consSitNFe_v4.00.xsd`, `leiauteConsSitNFe_v4.00.xsd`, `consStatServ_v4.00.xsd`, `leiauteConsStatServ_v4.00.xsd`; e o `grep` acha o padrão do CNPJ alfanumérico (`[0-9A-Z]{12}[0-9]{2}`) em pelo menos um dos dois — é a prova de que veio o pacote 010d v1.03. Nomes de arquivo diferenciam maiúsculas no Linux: não renomeie.

No `SiagroB1.Fiscal/SiagroB1.Fiscal.csproj`, acrescente:

```xml
    <ItemGroup>
      <None Include="Schemas\**\*.xsd" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
    </ItemGroup>

    <ItemGroup>
      <InternalsVisibleTo Include="SiagroB1.Fiscal.Tests" />
    </ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

```csharp
// SiagroB1.Fiscal.Tests/Nfe/NfeSignerTests.cs
using DFe.Utils;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>
/// Assinatura com certificado de teste e validação no XSD oficial (010d v1.03) — a verificação
/// possível sem certificado real nem credenciamento na SEFAZ.
/// </summary>
public class NfeSignerTests
{
    private static NfeServiceSettings Settings(string cnpj = "12345678000195") => new(
        NfeEnvironment.Homologation, "SP",
        CertificateLoader.Load(TestCertificates.CreatePfx(cnpj), TestCertificates.Password),
        NfeServiceSettings.DefaultSchemasDirectory);

    [Fact]
    public void Signed_xml_validates_against_the_official_schema()
    {
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), Settings());

        Assert.Contains("<Signature", signed.Xml);
        Assert.Contains("NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL", signed.Xml);
    }

    [Fact]
    public void Access_key_is_valid_and_reflects_the_document()
    {
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), Settings());

        Assert.Equal(44, signed.AccessKey.Length);
        Assert.True(ChaveFiscal.ChaveValida(signed.AccessKey));
        Assert.StartsWith("352610", signed.AccessKey);                 // cUF 35 + AAMM 2610
        Assert.Equal("12345678000195", signed.AccessKey.Substring(6, 14));
        Assert.Equal("55", signed.AccessKey.Substring(20, 2));
        Assert.Equal("001", signed.AccessKey.Substring(22, 3));
        Assert.Equal("000000123", signed.AccessKey.Substring(25, 9));
        Assert.Equal("48151623", signed.AccessKey.Substring(35, 8));
        Assert.Contains($"Id=\"NFe{signed.AccessKey}\"", signed.Xml);
    }

    [Fact]
    public void Alphanumeric_issuer_cnpj_produces_a_valid_key()
    {
        var signed = NfeSigner.BuildSignAndValidate(
            NfeTestData.Input(issuerTaxId: "12ABC34501DE35"), Settings("12ABC34501DE35"));

        Assert.Equal("12ABC34501DE35", signed.AccessKey.Substring(6, 14));
        Assert.True(ChaveFiscal.ChaveValida(signed.AccessKey));
    }

    [Fact]
    public void Schema_violation_is_reported_as_validation_error()
    {
        var input = NfeTestData.Input() with { Items = [NfeTestData.Item() with { Ncm = "1201" }] };

        var ex = Assert.Throws<NfeValidationException>(() => NfeSigner.BuildSignAndValidate(input, Settings()));

        Assert.Contains("NCM", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

```csharp
// SiagroB1.Fiscal.Tests/Nfe/NfeProcComposerTests.cs
using DFe.Utils;
using NFe.Classes;
using NFe.Classes.Protocolo;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>
/// O procNFe é montado por texto: o XML assinado gravado entra intacto, e o protNFe da SEFAZ ao
/// lado. É o que permite montar o autorizado também na "Consultar situação".
/// </summary>
public class NfeProcComposerTests
{
    [Fact]
    public void Composed_proc_keeps_the_signed_part_and_parses_back()
    {
        var settings = new NfeServiceSettings(
            NfeEnvironment.Homologation, "SP",
            CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password),
            NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), settings);
        var protocol = new protNFe
        {
            versao = "4.00",
            infProt = new infProt
            {
                Id = "ID135260000000001", chNFe = signed.AccessKey, nProt = "135260000000001", cStat = 100,
                xMotivo = "Autorizado o uso da NF-e", dhRecbto = NfeTestData.IssuedAt, digVal = "abc=", verAplic = "SP_NFE",
            },
        };

        var proc = NfeProcComposer.Compose(signed.Xml, FuncoesXml.ClasseParaXmlString(protocol));

        var signedBody = signed.Xml[(signed.Xml.IndexOf("<NFe", StringComparison.Ordinal))..];
        Assert.Contains(signedBody.Trim(), proc);
        Assert.StartsWith("<?xml", proc);

        var parsed = new nfeProc().CarregarDeXmlString(proc);
        Assert.Equal("135260000000001", parsed.protNFe.infProt.nProt);
        Assert.Equal($"NFe{signed.AccessKey}", parsed.NFe.infNFe.Id);
    }
}
```

```bash
git add SiagroB1.Fiscal.Tests/Nfe/NfeSignerTests.cs SiagroB1.Fiscal.Tests/Nfe/NfeProcComposerTests.cs
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeSignerTests|FullyQualifiedName~NfeProcComposerTests"`
Expected: FAIL de compilação — `NfeSigner` não existe.

- [ ] **Step 4: Implementação**

```csharp
// SiagroB1.Fiscal/Nfe/NfeServiceSettings.cs
using System.Security.Cryptography.X509Certificates;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// O que a Zeus precisa para assinar, validar e falar com a SEFAZ de uma filial. O certificado é
/// do chamador (abre e descarta). <paramref name="IssuerState"/> é a sigla da UF do emitente.
/// </summary>
public sealed record NfeServiceSettings(
    NfeEnvironment Environment,
    string IssuerState,
    X509Certificate2 Certificate,
    string SchemasDirectory,
    int TimeoutMilliseconds = 60_000,
    bool ValidateServerCertificate = true)
{
    /// <summary>A pasta <c>Schemas</c> que o <c>SiagroB1.Fiscal</c> copia para a saída de quem o referencia.</summary>
    public static string DefaultSchemasDirectory => Path.Combine(AppContext.BaseDirectory, "Schemas");
}
```

```csharp
// SiagroB1.Fiscal/Nfe/NfeZeusConfiguration.cs
using System.Net;
using DFe.Classes.Entidades;
using DFe.Classes.Flags;
using NFe.Classes.Informacoes.Identificacao.Tipos;
using NFe.Utils;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// <c>ConfiguracaoServico</c> por chamada — nunca o singleton <c>Instancia</c>, que é global e
/// misturaria filiais. ⚠️ As cinco propriedades que resolvem versões e endereços (ambiente, UF,
/// modelo, tipo de emissão, versão do layout) valem 0 por padrão e não acham endpoint nenhum:
/// todas precisam ser definidas, com a versão do layout por último.
/// </summary>
internal static class NfeZeusConfiguration
{
    public static ConfiguracaoServico Create(NfeServiceSettings settings) => new()
    {
        tpAmb = NfeXmlBuilder.Environment(settings.Environment),
        cUF = Enum.Parse<Estado>(settings.IssuerState, ignoreCase: true),
        ModeloDocumento = ModeloDocumento.NFe,
        tpEmis = TipoEmissao.teNormal,
        VersaoLayout = VersaoServico.Versao400,
        TimeOut = settings.TimeoutMilliseconds,
        ProtocoloDeSeguranca = SecurityProtocolType.Tls12,
        ValidarSchemas = true,
        ValidarCertificadoDoServidor = settings.ValidateServerCertificate,
        DiretorioSchemas = settings.SchemasDirectory,
        SalvarXmlServicos = false,
    };
}
```

```csharp
// SiagroB1.Fiscal/Nfe/NfeSigner.cs
using NFe.Utils.Excecoes;
using NFe.Utils.NFe;
using ZeusNFe = NFe.Classes.NFe;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>NF-e assinada e validada, pronta para gravar (antes do envio) e transmitir.</summary>
public sealed class SignedNfe
{
    internal SignedNfe(ZeusNFe document, string xml, string accessKey)
    {
        Document = document;
        Xml = xml;
        AccessKey = accessKey;
    }

    public string Xml { get; }

    /// <summary>Chave de 44 posições (o <c>Id</c> sem o prefixo "NFe").</summary>
    public string AccessKey { get; }

    internal ZeusNFe Document { get; }
}

/// <summary>Falha de schema na validação local: nada foi enviado à SEFAZ.</summary>
public sealed class NfeValidationException(string message) : Exception(message);

/// <summary>
/// Assina e valida no XSD. A Zeus monta a chave e o <c>cDV</c> na assinatura (inclusive com CNPJ
/// alfanumérico) — por isso o sistema não tem montador de chave próprio.
/// </summary>
public static class NfeSigner
{
    public static SignedNfe BuildSignAndValidate(NfeIssueInput input, NfeServiceSettings settings) =>
        SignAndValidate(NfeXmlBuilder.Build(input), settings);

    public static SignedNfe SignAndValidate(ZeusNFe nfe, NfeServiceSettings settings)
    {
        var configuration = NfeZeusConfiguration.Create(settings);

        try
        {
            nfe.Assina(configuration, settings.Certificate);
            nfe.Valida(configuration);
        }
        catch (ValidacaoSchemaException e)
        {
            throw new NfeValidationException(e.Message);
        }

        return new SignedNfe(nfe, nfe.ObterXmlString(), nfe.infNFe.Id[3..]);
    }
}
```

```csharp
// SiagroB1.Fiscal/Nfe/NfeProcComposer.cs
namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// <c>nfeProc</c> = NF-e assinada + <c>protNFe</c>, montado por texto para a parte assinada ficar
/// byte a byte igual à transmitida (reserializar o objeto poderia mexer em espaços e prefixos).
/// </summary>
public static class NfeProcComposer
{
    public static string Compose(string signedNfeXml, string protocolXml) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<nfeProc versao=\"4.00\" xmlns=\"http://www.portalfiscal.inf.br/nfe\">" +
        Body(signedNfeXml) +
        Body(protocolXml) +
        "</nfeProc>";

    private static string Body(string xml)
    {
        var text = xml.Trim();

        if (text.StartsWith("<?xml", StringComparison.Ordinal))
            text = text[(text.IndexOf("?>", StringComparison.Ordinal) + 2)..].Trim();

        return text;
    }
}
```

```bash
git add SiagroB1.Fiscal/Nfe/NfeServiceSettings.cs SiagroB1.Fiscal/Nfe/NfeZeusConfiguration.cs SiagroB1.Fiscal/Nfe/NfeSigner.cs SiagroB1.Fiscal/Nfe/NfeProcComposer.cs
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeSignerTests|FullyQualifiedName~NfeProcComposerTests"`
Expected: PASS (5 testes).

Se `Signed_xml_validates_against_the_official_schema` falhar com mensagem de schema, a mensagem nomeia o elemento: corrija o **builder** (Task 9) para o que o XSD exige naquele elemento e acrescente o caso em `NfeXmlBuilderTests`. Não afrouxe a validação.

- [ ] **Step 6: Suíte do Fiscal**

Run: `dotnet test SiagroB1.Fiscal.Tests 2>&1 | tail -3`
Expected: `Passed!`.

- [ ] **Step 7: Commit**

```bash
git commit -m "feat(invoice): assinar a NF-e, validar no XSD oficial e montar o procNFe" -m "Schemas do pacote 010d v1.03 (CNPJ alfanumérico) copiados do repositório da Zeus e publicados com quem referencia o Fiscal. A assinatura usa uma ConfiguracaoServico por chamada, nunca o singleton global. A chave sai da própria Zeus, testada também com CNPJ alfanumérico. O procNFe é composto por texto para a parte assinada ficar idêntica à transmitida.

Atenção: as cinco propriedades da ConfiguracaoServico que resolvem os endpoints valem 0 por padrão; faltar uma deixa a Zeus sem versão nem URL.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Fiscal SiagroB1.Fiscal.Tests/Nfe
```

---

### Task 11: Cliente da SEFAZ (`INfeSefazClient` + Zeus)

**Files:**
- Create: `SiagroB1.Fiscal/Nfe/NfeSefaz.cs`, `SiagroB1.Fiscal/Nfe/ZeusNfeSefazClient.cs`
- Test: `SiagroB1.Fiscal.Tests/Nfe/NfeSefazResponseMapperTests.cs`

**Interfaces:**
- Consumes: `SignedNfe`, `NfeServiceSettings`, `NfeZeusConfiguration` (Task 10).
- Produces:
  - `record NfeSefazResult(int StatusCode, string Reason, string? Protocol = null, DateTimeOffset? ReceivedAt = null, string? ProtocolXml = null)`.
  - `class NfeCommunicationException : Exception` (sem resposta da SEFAZ).
  - `NfeStatusCodes.IsAuthorized(int)`, `IsDenied(int)`, `IsDuplicate(int)`, `NotFound = 217`, `InOperation = 107`.
  - `interface INfeSefazClient { Task<NfeSefazResult> AuthorizeAsync(SignedNfe, NfeServiceSettings, CancellationToken = default); Task<NfeSefazResult> ConsultProtocolAsync(string accessKey, NfeServiceSettings, CancellationToken = default); Task<NfeSefazResult> ServiceStatusAsync(NfeServiceSettings, CancellationToken = default); }`.
  - `ZeusNfeSefazClient : INfeSefazClient`.

O cliente real só roda contra a SEFAZ (verificação da Task 21); aqui se testa a tradução das respostas, que é onde mora a regra.

- [ ] **Step 1: Write the failing test**

```csharp
// SiagroB1.Fiscal.Tests/Nfe/NfeSefazResponseMapperTests.cs
using NFe.Classes.Protocolo;
using NFe.Classes.Servicos.Consulta;
using NFe.Classes.Servicos.Recepcao;
using NFe.Classes.Servicos.Status;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Fiscal.Tests.Nfe;

/// <summary>Retornos da SEFAZ → resultado neutro que a Application trata (sem tipos da Zeus).</summary>
public class NfeSefazResponseMapperTests
{
    private const string Key = "35261012345678000195550010000001231481516230";

    private static protNFe Protocol(int status, string reason) => new()
    {
        versao = "4.00",
        infProt = new infProt
        {
            chNFe = Key, cStat = status, xMotivo = reason, nProt = "135260000000001",
            dhRecbto = new DateTimeOffset(2026, 10, 2, 10, 0, 5, TimeSpan.FromHours(-3)),
        },
    };

    [Fact]
    public void Processed_batch_with_authorization_returns_the_protocol()
    {
        var result = NfeSefazResponseMapper.FromAuthorization(new retEnviNFe
        {
            cStat = 104, xMotivo = "Lote processado", protNFe = Protocol(100, "Autorizado o uso da NF-e"),
        });

        Assert.Equal(100, result.StatusCode);
        Assert.Equal("135260000000001", result.Protocol);
        Assert.Contains("<protNFe", result.ProtocolXml);
        Assert.True(NfeStatusCodes.IsAuthorized(result.StatusCode));
    }

    [Fact]
    public void Processed_batch_with_rejection_returns_the_note_status()
    {
        var result = NfeSefazResponseMapper.FromAuthorization(new retEnviNFe
        {
            cStat = 104, xMotivo = "Lote processado", protNFe = Protocol(209, "Rejeição: IE do emitente inválida"),
        });

        Assert.Equal(209, result.StatusCode);
        Assert.Contains("IE do emitente", result.Reason);
    }

    [Fact]
    public void Batch_level_rejection_has_no_protocol()
    {
        var result = NfeSefazResponseMapper.FromAuthorization(new retEnviNFe
        {
            cStat = 225, xMotivo = "Rejeição: Falha no Schema XML",
        });

        Assert.Equal(225, result.StatusCode);
        Assert.Null(result.ProtocolXml);
    }

    [Fact]
    public void Consult_of_authorized_note_returns_the_protocol()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 100, xMotivo = "Autorizado o uso da NF-e", protNFe = Protocol(100, "Autorizado o uso da NF-e"),
        });

        Assert.Equal(100, result.StatusCode);
        Assert.NotNull(result.ProtocolXml);
    }

    [Fact]
    public void Consult_of_unknown_note_is_217()
    {
        var result = NfeSefazResponseMapper.FromConsult(new retConsSitNFe
        {
            cStat = 217, xMotivo = "Rejeição: NF-e não consta na base de dados da SEFAZ",
        });

        Assert.Equal(NfeStatusCodes.NotFound, result.StatusCode);
        Assert.Null(result.ProtocolXml);
    }

    [Fact]
    public void Service_status_maps_code_and_reason()
    {
        var result = NfeSefazResponseMapper.FromStatus(new retConsStatServ { cStat = 107, xMotivo = "Serviço em Operação" });

        Assert.Equal(NfeStatusCodes.InOperation, result.StatusCode);
    }

    [Theory]
    [InlineData(100, true, false, false)]
    [InlineData(150, true, false, false)]
    [InlineData(110, false, true, false)]
    [InlineData(301, false, true, false)]
    [InlineData(302, false, true, false)]
    [InlineData(303, false, true, false)]
    [InlineData(204, false, false, true)]
    [InlineData(539, false, false, true)]
    [InlineData(209, false, false, false)]
    public void Status_codes_are_classified(int code, bool authorized, bool denied, bool duplicate)
    {
        Assert.Equal(authorized, NfeStatusCodes.IsAuthorized(code));
        Assert.Equal(denied, NfeStatusCodes.IsDenied(code));
        Assert.Equal(duplicate, NfeStatusCodes.IsDuplicate(code));
    }
}
```

```bash
git add SiagroB1.Fiscal.Tests/Nfe/NfeSefazResponseMapperTests.cs
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeSefazResponseMapperTests"`
Expected: FAIL de compilação — `NfeSefazResponseMapper` não existe.

- [ ] **Step 3: Contrato, códigos e tradução**

```csharp
// SiagroB1.Fiscal/Nfe/NfeSefaz.cs
using DFe.Utils;
using NFe.Classes.Protocolo;
using NFe.Classes.Servicos.Consulta;
using NFe.Classes.Servicos.Recepcao;
using NFe.Classes.Servicos.Status;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// Resposta da SEFAZ sem tipos da Zeus. <see cref="ProtocolXml"/> é o <c>protNFe</c> serializado
/// (entra no procNFe); só vem quando há protocolo.
/// </summary>
public sealed record NfeSefazResult(
    int StatusCode,
    string Reason,
    string? Protocol = null,
    DateTimeOffset? ReceivedAt = null,
    string? ProtocolXml = null);

/// <summary>Sem resposta da SEFAZ (rede, tempo esgotado): a situação real é desconhecida.</summary>
public sealed class NfeCommunicationException(string message, Exception? inner = null) : Exception(message, inner);

public static class NfeStatusCodes
{
    public const int BatchProcessed = 104;
    public const int InOperation = 107;
    public const int NotFound = 217;

    public static bool IsAuthorized(int code) => code is 100 or 150;

    public static bool IsDenied(int code) => code is 110 or 301 or 302 or 303;

    public static bool IsDuplicate(int code) => code is 204 or 539;
}

/// <summary>Ponto de simulação nos testes da Application. Implementação real: <see cref="ZeusNfeSefazClient"/>.</summary>
public interface INfeSefazClient
{
    Task<NfeSefazResult> AuthorizeAsync(SignedNfe nfe, NfeServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeSefazResult> ConsultProtocolAsync(string accessKey, NfeServiceSettings settings, CancellationToken cancellationToken = default);

    Task<NfeSefazResult> ServiceStatusAsync(NfeServiceSettings settings, CancellationToken cancellationToken = default);
}

internal static class NfeSefazResponseMapper
{
    /// <summary>Autorização síncrona: com o lote processado (104) vale o protocolo da nota.</summary>
    public static NfeSefazResult FromAuthorization(retEnviNFe response) =>
        response.cStat == NfeStatusCodes.BatchProcessed && response.protNFe?.infProt is not null
            ? FromProtocol(response.protNFe)
            : new NfeSefazResult(response.cStat, response.xMotivo);

    public static NfeSefazResult FromConsult(retConsSitNFe response) =>
        response.protNFe?.infProt is not null
            ? FromProtocol(response.protNFe)
            : new NfeSefazResult(response.cStat, response.xMotivo);

    public static NfeSefazResult FromStatus(retConsStatServ response) =>
        new(response.cStat, response.xMotivo);

    private static NfeSefazResult FromProtocol(protNFe protocol)
    {
        var info = protocol.infProt;

        return new NfeSefazResult(
            info.cStat, info.xMotivo, info.nProt, info.dhRecbto, FuncoesXml.ClasseParaXmlString(protocol));
    }
}
```

```csharp
// SiagroB1.Fiscal/Nfe/ZeusNfeSefazClient.cs
using System.Net;
using System.Net.Sockets;
using DFe.Wsdl.Common;
using NFe.Classes.Servicos.Tipos;
using NFe.Servicos;
using NFe.Utils.Excecoes;

namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// SEFAZ pela Zeus. As chamadas da biblioteca são síncronas (HttpWebRequest): rodam fora da
/// thread da requisição. Falha de transporte vira <see cref="NfeCommunicationException"/> — quem
/// chama trata como "sem resposta" e mantém o documento em processamento.
/// </summary>
public sealed class ZeusNfeSefazClient : INfeSefazClient
{
    public Task<NfeSefazResult> AuthorizeAsync(
        SignedNfe nfe, NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, services =>
        {
            // O lote leva uma NF-e só; o número da nota (9 dígitos) cabe no idLote.
            var batchId = int.Parse(nfe.AccessKey.Substring(25, 9));
            var response = services.NFeAutorizacao(batchId, IndicadorSincronizacao.Sincrono, [nfe.Document]);

            return NfeSefazResponseMapper.FromAuthorization(response.Retorno);
        }, cancellationToken);

    public Task<NfeSefazResult> ConsultProtocolAsync(
        string accessKey, NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, services => NfeSefazResponseMapper.FromConsult(services.NfeConsultaProtocolo(accessKey).Retorno),
            cancellationToken);

    public Task<NfeSefazResult> ServiceStatusAsync(
        NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        RunAsync(settings, services => NfeSefazResponseMapper.FromStatus(services.NfeStatusServico().Retorno),
            cancellationToken);

    private static Task<NfeSefazResult> RunAsync(
        NfeServiceSettings settings, Func<ServicosNFe, NfeSefazResult> call, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            // Estático da biblioteca (caminho .NET Core): mesma escolha da configuração.
            ConfiguracaoServicoWSDL.ValidarCertificadoDoServidorNetCore = settings.ValidateServerCertificate;

            try
            {
                using var services = new ServicosNFe(NfeZeusConfiguration.Create(settings), settings.Certificate);
                return call(services);
            }
            catch (Exception e) when (e is ComunicacaoException or WebException or HttpRequestException
                                          or TimeoutException or IOException or SocketException
                                          or TaskCanceledException)
            {
                throw new NfeCommunicationException(e.Message, e);
            }
        }, cancellationToken);
}
```

```bash
git add SiagroB1.Fiscal/Nfe/NfeSefaz.cs SiagroB1.Fiscal/Nfe/ZeusNfeSefazClient.cs
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test SiagroB1.Fiscal.Tests --filter "FullyQualifiedName~NfeSefazResponseMapperTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(invoice): falar com a SEFAZ pela Zeus atrás de uma interface" -m "INfeSefazClient (autorizar síncrono, consultar protocolo, status do serviço) devolve um resultado sem tipos da Zeus, e é o ponto de simulação dos testes da Application. Com o lote processado vale o protocolo da nota; falha de transporte vira NfeCommunicationException, que o emissor trata como sem resposta.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Fiscal/Nfe SiagroB1.Fiscal.Tests/Nfe
```

---
### Task 12: Configuração da NF-e por filial — tabela, envio do certificado, "Testar comunicação"

**Files:**
- Create: `SiagroB1.Domain/Entities/BranchNfeSettings.cs`, `SiagroB1.Domain/Models/BranchNfeSettingsModel.cs`, `SiagroB1.Domain/Dtos/Nfe/NfeServiceStatusDto.cs`
- Create: `SiagroB1.Fiscal/Nfe/NfeText.cs`
- Create: `SiagroB1.Application/Services/Nfe/NfeOptions.cs`, `BranchNfeSettingsService.cs`
- Create: `SiagroB1.Web/Functions/Nfe/BranchNfeSettingsGetController.cs`, `SiagroB1.Web/Actions/Nfe/BranchNfeSettingsSaveController.cs`, `BranchNfeSettingsUploadCertificateController.cs`, `BranchNfeSettingsTestConnectionController.cs`
- Create: `SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs`
- Modify: `SiagroB1.Infra/Context/AppDbContext.cs`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`, `SiagroB1.Web/appsettings.json`, `SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj`
- Create (gerada): migration `CreateBranchNfeSettings`
- Test: `SiagroB1.Application.Tests/Nfe/BranchNfeSettingsServiceTests.cs`

**Interfaces:**
- Consumes: `CertificatePasswordCipher`, `CertificateInspector`, `CertificateLoader` (Task 8); `NfeServiceSettings` (Task 10); `INfeSefazClient`, `NfeSefazResult`, `ZeusNfeSefazClient` (Task 11); `NfeEnvironment` (Task 9); `ErpMode` (Task 7).
- Produces:
  - Entidade `BranchNfeSettings` (`BRANCH_NFE_SETTINGS`): `BranchCode` (PK/FK), `Environment`, `Series`, `NextNumber`, `CertificatePfx`, `CertificatePasswordCipher`, `CertificateSubject`, `CertificateTaxId`, `CertificateValidUntil`, `UpdatedAt`, `UpdatedBy`; `DbSet<BranchNfeSettings> BranchNfeSettings`.
  - `BranchNfeSettingsModel` (sem senha nem .pfx; `HasCertificate`, `ServerKeyConfigured`); `NfeServiceStatusDto { int StatusCode; string Reason }`.
  - `NfeText.AlphaNumeric(string?)`, `NfeText.Digits(string?)`.
  - `NfeOptions(IConfiguration)`: `IsStandalone`, `HasCertificateKey`, `Cipher()`, `TimeoutMilliseconds`, `ValidateSefazCertificate`, `TechnicalResponsible`.
  - `BranchNfeSettingsService(IUnitOfWork, NfeOptions, INfeSefazClient)`: `GetAsync(string)`, `SaveAsync(string, NfeEnvironment, int, int, string)`, `UploadCertificateAsync(string, byte[], string, string)`, `TestConnectionAsync(string)`, `OpenAsync(string branchCode, NfeEnvironment? environment = null) : NfeServiceContext`.
  - `NfeServiceContext : IDisposable` com `Settings` (`NfeServiceSettings`) e `BranchSettings` (`BranchNfeSettings`).
  - OData: função `BranchNfeSettingsGet(BranchCode)`; actions `BranchNfeSettingsSave(BranchCode, Environment, Series, NextNumber)`, `BranchNfeSettingsUploadCertificate(BranchCode, Pfx, Password)`, `BranchNfeSettingsTestConnection(BranchCode)`.
  - Testes: `FakeNfeSefazClient` e `TestCertificates` (link) disponíveis no `SiagroB1.Application.Tests`.

- [ ] **Step 1: Infra de teste — certificado e SEFAZ simulada**

No `SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj`, acrescente:

```xml
    <ItemGroup>
      <Compile Include="..\SiagroB1.Fiscal.Tests\Support\TestCertificates.cs" Link="Support\TestCertificates.cs" />
    </ItemGroup>
```

```csharp
// SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs
using DFe.Utils;
using NFe.Classes.Protocolo;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// SEFAZ simulada: cada chamada consome a próxima resposta da fila (recebe a chave de acesso).
/// <see cref="BeforeAuthorize"/> deixa o teste olhar o banco no instante do envio.
/// </summary>
public sealed class FakeNfeSefazClient : INfeSefazClient
{
    public Queue<Func<string, NfeSefazResult>> AuthorizeResponses { get; } = new();
    public Queue<Func<string, NfeSefazResult>> ConsultResponses { get; } = new();
    public NfeSefazResult StatusResponse { get; set; } = new(107, "Serviço em Operação");
    public List<SignedNfe> Sent { get; } = [];
    public List<string> Consulted { get; } = [];
    public Func<Task>? BeforeAuthorize { get; set; }

    public async Task<NfeSefazResult> AuthorizeAsync(
        SignedNfe nfe, NfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        Sent.Add(nfe);

        if (BeforeAuthorize is not null)
            await BeforeAuthorize();

        return AuthorizeResponses.Dequeue()(nfe.AccessKey);
    }

    public Task<NfeSefazResult> ConsultProtocolAsync(
        string accessKey, NfeServiceSettings settings, CancellationToken cancellationToken = default)
    {
        Consulted.Add(accessKey);
        return Task.FromResult(ConsultResponses.Dequeue()(accessKey));
    }

    public Task<NfeSefazResult> ServiceStatusAsync(NfeServiceSettings settings, CancellationToken cancellationToken = default) =>
        Task.FromResult(StatusResponse);

    public static NfeSefazResult Authorized(string accessKey, int status = 100) => new(
        status, "Autorizado o uso da NF-e", "135260000000001", new DateTimeOffset(2026, 10, 2, 10, 0, 5, TimeSpan.FromHours(-3)),
        FuncoesXml.ClasseParaXmlString(new protNFe
        {
            versao = "4.00",
            infProt = new infProt
            {
                Id = "ID135260000000001", chNFe = accessKey, cStat = status, xMotivo = "Autorizado o uso da NF-e",
                nProt = "135260000000001", dhRecbto = new DateTimeOffset(2026, 10, 2, 10, 0, 5, TimeSpan.FromHours(-3)),
                digVal = "abc=", verAplic = "TESTE",
            },
        }));

    public static NfeSefazResult Rejected(int status = 209, string reason = "Rejeição: IE do emitente inválida") => new(status, reason);

    public static NfeSefazResult NoResponse(string _) => throw new NfeCommunicationException("Tempo esgotado.");
}
```

```bash
git add SiagroB1.Application.Tests/Support/FakeNfeSefazClient.cs
```

- [ ] **Step 2: Write the failing test**

```csharp
// SiagroB1.Application.Tests/Nfe/BranchNfeSettingsServiceTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Configuração da NF-e por filial e envio do certificado A1 (spec §7.1–§7.3).</summary>
public class BranchNfeSettingsServiceTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static IConfiguration Config(string erp = "STANDALONE", bool withKey = true) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Erp"] = erp,
            ["Nfe:CertificateKey"] = withKey ? Key : null,
        }).Build();

    private static async Task<UnitOfWork> SeedAsync(string taxId = "12345678000195")
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "CEAGUI", ShortName = "CEAGUI", TaxId = taxId, StateCode = "SP", IssuesNfe = true,
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static BranchNfeSettingsService Service(UnitOfWork db, IConfiguration? config = null, FakeNfeSefazClient? sefaz = null) =>
        new(db, new NfeOptions(config ?? Config()), sefaz ?? new FakeNfeSefazClient());

    [Fact]
    public async Task Get_returns_defaults_when_not_configured()
    {
        var db = await SeedAsync();

        var model = await Service(db).GetAsync("01");

        Assert.Equal(NfeEnvironment.Homologation, model.Environment);
        Assert.Equal(1, model.Series);
        Assert.Equal(1, model.NextNumber);
        Assert.False(model.HasCertificate);
        Assert.True(model.ServerKeyConfigured);
    }

    [Fact]
    public async Task Save_creates_and_then_updates()
    {
        var db = await SeedAsync();
        var service = Service(db);

        await service.SaveAsync("01", NfeEnvironment.Homologation, 1, 10, "tester");
        await service.SaveAsync("01", NfeEnvironment.Production, 2, 500, "tester");

        var saved = await db.Context.BranchNfeSettings.AsNoTracking().SingleAsync();
        Assert.Equal(NfeEnvironment.Production, saved.Environment);
        Assert.Equal(2, saved.Series);
        Assert.Equal(500, saved.NextNumber);
        Assert.Equal("tester", saved.UpdatedBy);
    }

    [Theory]
    [InlineData(-1, 1, "série")]
    [InlineData(1000, 1, "série")]
    [InlineData(1, 0, "próximo número")]
    public async Task Save_validates_series_and_next_number(int series, int next, string expected)
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).SaveAsync("01", NfeEnvironment.Homologation, series, next, "tester"));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Upload_stores_the_certificate_and_never_returns_the_password()
    {
        var db = await SeedAsync();

        var model = await Service(db).UploadCertificateAsync("01", TestCertificates.CreatePfx(), TestCertificates.Password, "tester");

        Assert.True(model.HasCertificate);
        Assert.Equal("12345678000195", model.CertificateTaxId);
        var saved = await db.Context.BranchNfeSettings.AsNoTracking().SingleAsync();
        Assert.NotNull(saved.CertificatePfx);
        Assert.Equal(TestCertificates.Password, CertificatePasswordCipher.FromBase64(Key).Decrypt(saved.CertificatePasswordCipher!));
        Assert.DoesNotContain(typeof(Domain.Models.BranchNfeSettingsModel).GetProperties(), p => p.Name.Contains("Password") || p.Name.Contains("Pfx"));
    }

    [Fact]
    public async Task Upload_accepts_a_certificate_of_the_head_office()
    {
        var db = await SeedAsync(taxId: "12345678000276"); // filial 0002 da mesma raiz

        var model = await Service(db).UploadCertificateAsync("01", TestCertificates.CreatePfx("12345678000195"), TestCertificates.Password, "tester");

        Assert.True(model.HasCertificate);
    }

    [Fact]
    public async Task Upload_refuses_wrong_password()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).UploadCertificateAsync("01", TestCertificates.CreatePfx(), "errada", "tester"));

        Assert.Contains("senha", ex.Message);
    }

    [Fact]
    public async Task Upload_refuses_certificate_without_private_key()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).UploadCertificateAsync(
            "01", TestCertificates.CreatePfx(withPrivateKey: false), TestCertificates.Password, "tester"));

        Assert.Contains("chave privada", ex.Message);
    }

    [Fact]
    public async Task Upload_refuses_expired_certificate()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).UploadCertificateAsync(
            "01", TestCertificates.CreatePfx(notAfter: DateTimeOffset.UtcNow.AddDays(-1)), TestCertificates.Password, "tester"));

        Assert.Contains("venceu", ex.Message);
    }

    [Fact]
    public async Task Upload_refuses_certificate_of_another_company()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).UploadCertificateAsync(
            "01", TestCertificates.CreatePfx("99888777000166"), TestCertificates.Password, "tester"));

        Assert.Contains("99888777000166", ex.Message);
    }

    [Fact]
    public async Task Upload_without_server_key_says_what_to_configure()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db, Config(withKey: false))
            .UploadCertificateAsync("01", TestCertificates.CreatePfx(), TestCertificates.Password, "tester"));

        Assert.Contains("Nfe:CertificateKey", ex.Message);
    }

    [Fact]
    public async Task Everything_is_refused_outside_standalone()
    {
        var db = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db, Config("SAPB1")).GetAsync("01"));

        Assert.Contains("STANDALONE", ex.Message);
    }

    [Fact]
    public async Task Test_connection_returns_the_sefaz_status()
    {
        var db = await SeedAsync();
        var service = Service(db);
        await service.UploadCertificateAsync("01", TestCertificates.CreatePfx(), TestCertificates.Password, "tester");

        var status = await service.TestConnectionAsync("01");

        Assert.Equal(107, status.StatusCode);
        Assert.Equal("Serviço em Operação", status.Reason);
    }

    [Fact]
    public async Task Test_connection_without_certificate_is_refused()
    {
        var db = await SeedAsync();
        await Service(db).SaveAsync("01", NfeEnvironment.Homologation, 1, 1, "tester");

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Service(db).TestConnectionAsync("01"));

        Assert.Contains("certificado", ex.Message);
    }
}
```

```bash
git add SiagroB1.Application.Tests/Nfe/BranchNfeSettingsServiceTests.cs
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BranchNfeSettingsServiceTests"`
Expected: FAIL de compilação — `BranchNfeSettingsService` não existe.

- [ ] **Step 4: Entidade, modelo, DTO e utilitário de texto**

```csharp
// SiagroB1.Domain/Entities/BranchNfeSettings.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Configuração da NF-e da filial (só STANDALONE). Tabela própria para o .pfx não viajar em cada
/// leitura de BRANCHS — que o SAPB1 também faz. A senha fica cifrada (AES-GCM, chave no
/// appsettings do servidor) e nunca sai pela API.
/// </summary>
[Table("BRANCH_NFE_SETTINGS")]
public class BranchNfeSettings
{
    [Key]
    [Column(TypeName = "VARCHAR(14)")]
    [ForeignKey(nameof(Branch))]
    public required string BranchCode { get; set; }

    public virtual Branch? Branch { get; set; }

    public NfeEnvironment Environment { get; set; } = NfeEnvironment.Homologation;

    public int Series { get; set; } = 1;

    /// <summary>Próximo número a reservar — ver <c>NfeNumberReservationService</c>.</summary>
    public int NextNumber { get; set; } = 1;

    [Column(TypeName = "VARBINARY(MAX)")]
    public byte[]? CertificatePfx { get; set; }

    [Column(TypeName = "VARBINARY(512)")]
    public byte[]? CertificatePasswordCipher { get; set; }

    [Column(TypeName = "VARCHAR(250)")]
    public string? CertificateSubject { get; set; }

    [Column(TypeName = "VARCHAR(14)")]
    public string? CertificateTaxId { get; set; }

    public DateTime? CertificateValidUntil { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }
}
```

```csharp
// SiagroB1.Domain/Models/BranchNfeSettingsModel.cs
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Models;

/// <summary>O que a tela "Configuração da NF-e" vê. Sem senha e sem o .pfx, de propósito.</summary>
public class BranchNfeSettingsModel
{
    public string BranchCode { get; set; } = string.Empty;
    public NfeEnvironment Environment { get; set; }
    public int Series { get; set; }
    public int NextNumber { get; set; }
    public bool HasCertificate { get; set; }
    public string? CertificateSubject { get; set; }
    public string? CertificateTaxId { get; set; }
    public DateTime? CertificateValidUntil { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>O servidor tem <c>Nfe:CertificateKey</c>? Sem ela não há envio de certificado nem emissão.</summary>
    public bool ServerKeyConfigured { get; set; }
}
```

```csharp
// SiagroB1.Domain/Dtos/Nfe/NfeServiceStatusDto.cs
namespace SiagroB1.Domain.Dtos.Nfe;

/// <summary>Resposta do "Testar comunicação" (status do serviço na SEFAZ; 107 = em operação).</summary>
public class NfeServiceStatusDto
{
    public int StatusCode { get; set; }
    public string Reason { get; set; } = string.Empty;
}
```

```csharp
// SiagroB1.Fiscal/Nfe/NfeText.cs
namespace SiagroB1.Fiscal.Nfe;

/// <summary>
/// Normalização dos textos que vão ao XML. ⚠️ O setter <c>CNPJ</c> da Zeus guarda só o PRIMEIRO
/// trecho alfanumérico: "12.345.678/0001-95" viraria "12". Todo CNPJ/CPF passa por aqui antes.
/// </summary>
public static class NfeText
{
    public static string AlphaNumeric(string? value) =>
        new string((value ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

    public static string Digits(string? value) =>
        new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
}
```

Em `AppDbContext.cs`:

```csharp
    public DbSet<BranchNfeSettings> BranchNfeSettings { get; set; }
```

```bash
git add SiagroB1.Domain/Entities/BranchNfeSettings.cs SiagroB1.Domain/Models/BranchNfeSettingsModel.cs SiagroB1.Domain/Dtos/Nfe/NfeServiceStatusDto.cs SiagroB1.Fiscal/Nfe/NfeText.cs
```

- [ ] **Step 5: Opções e serviço**

```csharp
// SiagroB1.Application/Services/Nfe/NfeOptions.cs
using System.Globalization;
using Microsoft.Extensions.Configuration;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Leitura única da seção <c>Nfe</c> do appsettings (e do modo de integração).</summary>
public class NfeOptions(IConfiguration configuration)
{
    public bool IsStandalone => ErpMode.IsStandalone(configuration);

    public bool HasCertificateKey => !string.IsNullOrWhiteSpace(configuration[CertificatePasswordCipher.ConfigurationKey]);

    public CertificatePasswordCipher Cipher() =>
        CertificatePasswordCipher.FromBase64(configuration[CertificatePasswordCipher.ConfigurationKey]);

    public int TimeoutMilliseconds =>
        (int.TryParse(configuration["Nfe:TimeoutSeconds"], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : 60) * 1000;

    public bool ValidateSefazCertificate =>
        !bool.TryParse(configuration["Nfe:ValidateSefazCertificate"], out var validate) || validate;

    public NfeTechnicalResponsible? TechnicalResponsible
    {
        get
        {
            var cnpj = configuration["Nfe:TechnicalResponsible:Cnpj"];

            return string.IsNullOrWhiteSpace(cnpj)
                ? null
                : new NfeTechnicalResponsible(
                    NfeText.AlphaNumeric(cnpj),
                    configuration["Nfe:TechnicalResponsible:Contact"] ?? string.Empty,
                    configuration["Nfe:TechnicalResponsible:Email"] ?? string.Empty,
                    NfeText.Digits(configuration["Nfe:TechnicalResponsible:Phone"]));
        }
    }
}
```

```csharp
// SiagroB1.Application/Services/Nfe/BranchNfeSettingsService.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Certificado aberto + configuração da filial, para uma chamada. Descarta o certificado no fim.</summary>
public sealed class NfeServiceContext(BranchNfeSettings branchSettings, NfeServiceSettings settings) : IDisposable
{
    public BranchNfeSettings BranchSettings { get; } = branchSettings;

    public NfeServiceSettings Settings { get; } = settings;

    public void Dispose() => Settings.Certificate.Dispose();
}

/// <summary>Configuração da NF-e por filial (spec §7). Só STANDALONE: recusa nos demais modos.</summary>
public class BranchNfeSettingsService(IUnitOfWork db, NfeOptions options, INfeSefazClient sefaz)
{
    private const string StandaloneOnly = "A configuração da NF-e só existe no modo STANDALONE.";

    public async Task<BranchNfeSettingsModel> GetAsync(string branchCode)
    {
        EnsureStandalone();

        var settings = await db.Context.BranchNfeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == branchCode);

        return ToModel(settings ?? new BranchNfeSettings { BranchCode = branchCode });
    }

    public async Task<BranchNfeSettingsModel> SaveAsync(
        string branchCode, NfeEnvironment environment, int series, int nextNumber, string userName)
    {
        EnsureStandalone();

        if (!Enum.IsDefined(environment))
            throw new DefaultException("Escolha o ambiente: produção ou homologação.");

        if (series is < 0 or > 999)
            throw new DefaultException("A série da NF-e vai de 0 a 999.");

        if (nextNumber is < 1 or > 999_999_999)
            throw new DefaultException("O próximo número da NF-e vai de 1 a 999.999.999.");

        var settings = await LoadOrCreateAsync(branchCode);
        settings.Environment = environment;
        settings.Series = series;
        settings.NextNumber = nextNumber;
        Stamp(settings, userName);

        await db.SaveChangesAsync();

        return ToModel(settings);
    }

    public async Task<BranchNfeSettingsModel> UploadCertificateAsync(
        string branchCode, byte[] pfx, string password, string userName)
    {
        EnsureStandalone();

        // Primeiro a chave do servidor: sem ela nada do que vem depois serve.
        var cipher = options.Cipher();

        var branch = await db.Context.Branchs.AsNoTracking().FirstOrDefaultAsync(b => b.Code == branchCode)
                     ?? throw new DefaultException($"Filial {branchCode} não encontrada.");

        var info = CertificateInspector.Inspect(pfx, password);

        if (!info.HasPrivateKey)
            throw new DefaultException("O certificado não tem a chave privada: exporte o .pfx com a chave.");

        if (info.ValidUntil < DateTime.Now)
            throw new DefaultException($"O certificado venceu em {info.ValidUntil:dd/MM/yyyy}.");

        if (info.TaxId is null)
            throw new DefaultException("Não foi possível ler o CNPJ do certificado.");

        // A raiz (8 primeiros caracteres) basta: o certificado da matriz assina pelas filiais.
        var branchTaxId = NfeText.AlphaNumeric(branch.TaxId);
        if (branchTaxId.Length < 8 || !info.TaxId.StartsWith(branchTaxId[..8], StringComparison.Ordinal))
            throw new DefaultException(
                $"O certificado é do CNPJ {info.TaxId}, que não é da raiz do CNPJ da filial ({branch.TaxId}).");

        var settings = await LoadOrCreateAsync(branchCode);
        settings.CertificatePfx = pfx;
        settings.CertificatePasswordCipher = cipher.Encrypt(password);
        settings.CertificateSubject = info.Subject.Length <= 250 ? info.Subject : info.Subject[..250];
        settings.CertificateTaxId = info.TaxId;
        settings.CertificateValidUntil = info.ValidUntil;
        Stamp(settings, userName);

        await db.SaveChangesAsync();

        return ToModel(settings);
    }

    public async Task<NfeServiceStatusDto> TestConnectionAsync(string branchCode)
    {
        using var context = await OpenAsync(branchCode);

        var result = await sefaz.ServiceStatusAsync(context.Settings);

        return new NfeServiceStatusDto { StatusCode = result.StatusCode, Reason = result.Reason };
    }

    /// <summary>
    /// Abre o certificado da filial para uma chamada à SEFAZ. <paramref name="environment"/> força
    /// o ambiente — a consulta usa o da emissão, mesmo que a configuração tenha mudado depois.
    /// </summary>
    public async Task<NfeServiceContext> OpenAsync(string branchCode, NfeEnvironment? environment = null)
    {
        EnsureStandalone();

        var settings = await db.Context.BranchNfeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == branchCode)
                       ?? throw new DefaultException($"A filial {branchCode} não tem a Configuração da NF-e.");

        if (settings.CertificatePfx is null || settings.CertificatePasswordCipher is null)
            throw new DefaultException("Envie o certificado digital da filial na Configuração da NF-e.");

        var branch = await db.Context.Branchs.AsNoTracking().FirstAsync(b => b.Code == branchCode);
        var password = options.Cipher().Decrypt(settings.CertificatePasswordCipher);
        var certificate = CertificateLoader.Load(settings.CertificatePfx, password);

        return new NfeServiceContext(settings, new NfeServiceSettings(
            environment ?? settings.Environment,
            branch.StateCode ?? throw new DefaultException($"A filial {branchCode} está sem UF."),
            certificate,
            NfeServiceSettings.DefaultSchemasDirectory,
            options.TimeoutMilliseconds,
            options.ValidateSefazCertificate));
    }

    private void EnsureStandalone()
    {
        if (!options.IsStandalone)
            throw new DefaultException(StandaloneOnly);
    }

    private async Task<BranchNfeSettings> LoadOrCreateAsync(string branchCode)
    {
        var settings = await db.Context.BranchNfeSettings.FirstOrDefaultAsync(s => s.BranchCode == branchCode);

        if (settings is not null)
            return settings;

        if (!await db.Context.Branchs.AnyAsync(b => b.Code == branchCode))
            throw new DefaultException($"Filial {branchCode} não encontrada.");

        settings = new BranchNfeSettings { BranchCode = branchCode };
        db.Context.BranchNfeSettings.Add(settings);

        return settings;
    }

    private static void Stamp(BranchNfeSettings settings, string userName)
    {
        settings.UpdatedAt = DateTime.Now;
        settings.UpdatedBy = userName;
    }

    private BranchNfeSettingsModel ToModel(BranchNfeSettings settings) => new()
    {
        BranchCode = settings.BranchCode,
        Environment = settings.Environment,
        Series = settings.Series,
        NextNumber = settings.NextNumber,
        HasCertificate = settings.CertificatePfx is not null,
        CertificateSubject = settings.CertificateSubject,
        CertificateTaxId = settings.CertificateTaxId,
        CertificateValidUntil = settings.CertificateValidUntil,
        UpdatedAt = settings.UpdatedAt,
        UpdatedBy = settings.UpdatedBy,
        ServerKeyConfigured = options.HasCertificateKey,
    };
}
```

```bash
git add SiagroB1.Application/Services/Nfe/NfeOptions.cs SiagroB1.Application/Services/Nfe/BranchNfeSettingsService.cs
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~BranchNfeSettingsServiceTests"`
Expected: PASS.

- [ ] **Step 7: API, EDM, DI e appsettings**

```csharp
// SiagroB1.Web/Functions/Nfe/BranchNfeSettingsGetController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Nfe;

public class BranchNfeSettingsGetController(BranchNfeSettingsService service) : ODataController
{
    [HttpGet("odata/BranchNfeSettingsGet(BranchCode={branchCode})")]
    public async Task<IActionResult> GetAsync([FromRoute] string branchCode)
    {
        try
        {
            // Rotas por atributo entregam o segmento COM as aspas simples do OData.
            return Ok(await service.GetAsync(branchCode.Trim('\'')));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

```csharp
// SiagroB1.Web/Actions/Nfe/BranchNfeSettingsSaveController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

public class BranchNfeSettingsSaveController(BranchNfeSettingsService service) : ODataController
{
    [HttpPost("odata/BranchNfeSettingsSave")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        // ODataActionParameters chega NULO quando o corpo não casa com o EDM.
        if (parameters is null || !parameters.ContainsKey("BranchCode"))
            return BadRequest("Parâmetros obrigatórios: BranchCode, Environment, Series, NextNumber.");

        try
        {
            return Ok(await service.SaveAsync(
                parameters["BranchCode"]?.ToString() ?? string.Empty,
                (NfeEnvironment)Convert.ToInt32(parameters["Environment"]),
                Convert.ToInt32(parameters["Series"]),
                Convert.ToInt32(parameters["NextNumber"]),
                User.Identity?.Name ?? "Unknown"));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

```csharp
// SiagroB1.Web/Actions/Nfe/BranchNfeSettingsUploadCertificateController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

/// <summary>
/// Certificado A1 em base64 por action OData — o mesmo caminho do anexo do contrato: multipart
/// não atravessa o Gateway (só /odata, /security e /reports chegam ao backend).
/// </summary>
public class BranchNfeSettingsUploadCertificateController(BranchNfeSettingsService service) : ODataController
{
    [HttpPost("odata/BranchNfeSettingsUploadCertificate")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.ContainsKey("BranchCode") || !parameters.ContainsKey("Pfx"))
            return BadRequest("Parâmetros obrigatórios: BranchCode, Pfx, Password.");

        byte[] pfx;
        try
        {
            pfx = Convert.FromBase64String(parameters["Pfx"]?.ToString() ?? string.Empty);
        }
        catch (FormatException)
        {
            return BadRequest("O arquivo do certificado chegou corrompido.");
        }

        try
        {
            return Ok(await service.UploadCertificateAsync(
                parameters["BranchCode"]?.ToString() ?? string.Empty,
                pfx,
                parameters.TryGetValue("Password", out var password) ? password?.ToString() ?? string.Empty : string.Empty,
                User.Identity?.Name ?? "Unknown"));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

```csharp
// SiagroB1.Web/Actions/Nfe/BranchNfeSettingsTestConnectionController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Web.Actions.Nfe;

public class BranchNfeSettingsTestConnectionController(BranchNfeSettingsService service) : ODataController
{
    [HttpPost("odata/BranchNfeSettingsTestConnection")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.ContainsKey("BranchCode"))
            return BadRequest("Parâmetro obrigatório: BranchCode.");

        try
        {
            return Ok(await service.TestConnectionAsync(parameters["BranchCode"]?.ToString() ?? string.Empty));
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
        catch (NfeCommunicationException e)
        {
            return BadRequest($"Sem resposta da SEFAZ: {e.Message}");
        }
    }
}
```

Em `ODataConfigurations.cs`, depois da função `PaymentConditionsPreview`:

```csharp
        // Configuração da NF-e por filial (NF-e STANDALONE). Senha e .pfx nunca saem.
        var branchNfeSettingsGet = modelBuilder.Function("BranchNfeSettingsGet");
        branchNfeSettingsGet.Parameter<string>("BranchCode");
        branchNfeSettingsGet.Returns<BranchNfeSettingsModel>();

        var branchNfeSettingsSave = modelBuilder.Action("BranchNfeSettingsSave");
        branchNfeSettingsSave.Parameter<string>("BranchCode");
        branchNfeSettingsSave.Parameter<int>("Environment");
        branchNfeSettingsSave.Parameter<int>("Series");
        branchNfeSettingsSave.Parameter<int>("NextNumber");
        branchNfeSettingsSave.Returns<BranchNfeSettingsModel>();

        var branchNfeSettingsUploadCertificate = modelBuilder.Action("BranchNfeSettingsUploadCertificate");
        branchNfeSettingsUploadCertificate.Parameter<string>("BranchCode");
        branchNfeSettingsUploadCertificate.Parameter<string>("Pfx");
        branchNfeSettingsUploadCertificate.Parameter<string>("Password");
        branchNfeSettingsUploadCertificate.Returns<BranchNfeSettingsModel>();

        var branchNfeSettingsTestConnection = modelBuilder.Action("BranchNfeSettingsTestConnection");
        branchNfeSettingsTestConnection.Parameter<string>("BranchCode");
        branchNfeSettingsTestConnection.Returns<NfeServiceStatusDto>();
```

Em `ServiceCollectionExtensions.cs`, junto de `PaymentConditionsService`:

```csharp
        services.AddScoped<NfeOptions>();
        services.AddScoped<BranchNfeSettingsService>();
        // Sem estado: uma instância serve a todos (a configuração da Zeus é criada por chamada).
        services.AddSingleton<INfeSefazClient, ZeusNfeSefazClient>();
```

Em `SiagroB1.Web/appsettings.json`, acrescente a seção (sem valores secretos — a chave real vai por variável de ambiente `Nfe__CertificateKey` ou no appsettings do servidor):

```json
  "Nfe": {
    "CertificateKey": "",
    "TimeoutSeconds": 60,
    "ValidateSefazCertificate": true,
    "TechnicalResponsible": {
      "Cnpj": "",
      "Contact": "",
      "Email": "",
      "Phone": ""
    }
  }
```

```bash
git add SiagroB1.Web/Functions/Nfe/BranchNfeSettingsGetController.cs SiagroB1.Web/Actions/Nfe/BranchNfeSettingsSaveController.cs SiagroB1.Web/Actions/Nfe/BranchNfeSettingsUploadCertificateController.cs SiagroB1.Web/Actions/Nfe/BranchNfeSettingsTestConnectionController.cs
```

- [ ] **Step 8: Migration**

Run: `dotnet ef migrations add CreateBranchNfeSettings --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`

Leia: só `CreateTable BRANCH_NFE_SETTINGS` (PK e FK `BranchCode` → `BRANCHS`). Nada em outras tabelas.

```bash
git add SiagroB1.Migrations/AppContext/*_CreateBranchNfeSettings*.cs
```

- [ ] **Step 9: Build e suíte**

Run: `dotnet build SiagroB1.sln 2>&1 | grep -E " error |Build succeeded" | sort -u` e `dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Build succeeded` e `Passed!`.

- [ ] **Step 10: Commit**

```bash
git commit -m "feat(invoice): configurar a NF-e por filial e receber o certificado A1" -m "Tabela própria para o .pfx não viajar com BRANCHS. O envio recusa com mensagem o arquivo que não é PFX ou com senha errada, sem chave privada, vencido ou de outra raiz de CNPJ (o da matriz vale para as filiais). A senha vai cifrada e nunca volta pela API. Testar comunicação consulta o status do serviço na SEFAZ. Tudo só STANDALONE.

Atenção: sem Nfe:CertificateKey no servidor, envio e emissão recusam dizendo o que configurar; trocar a chave obriga a reenviar o certificado.

DB: CreateBranchNfeSettings

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Domain SiagroB1.Fiscal/Nfe/NfeText.cs SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Application/Services/Nfe SiagroB1.Web SiagroB1.Migrations SiagroB1.Application.Tests
```

---

### Task 13: Documento — colunas da NF-e, XMLs, numeração, prontidão e montagem da entrada

**Files:**
- Create: `SiagroB1.Domain/Enums/NfeStatus.cs`, `SalesInvoiceNfeXmlKind.cs`, `SiagroB1.Domain/Entities/SalesInvoiceNfeXml.cs`
- Create: `SiagroB1.Application/Services/Nfe/NfeNumberReservationService.cs`, `NfeIssueContext.cs`, `NfeReadinessValidator.cs`, `NfeIssueInputAssembler.cs`
- Create: `SiagroB1.Application.Tests/Support/FakeNfeNumberReservationService.cs`, `SiagroB1.Application.Tests/Support/NfeTestSeed.cs`
- Modify: `SiagroB1.Domain/Entities/SalesInvoice.cs`, `SiagroB1.Infra/Context/AppDbContext.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Create (gerada): migration `AddSalesInvoiceNfeIssuance`
- Test: `SiagroB1.Application.Tests/Nfe/NfeReadinessValidatorTests.cs`, `NfeIssueInputAssemblerTests.cs`

**Interfaces:**
- Consumes: entidades e campos das Tasks 2–6 e 12; `NfeIssueInput` e records (Task 9); `PaymentInstallmentCalculator` (Task 5); `NfeText`, `NfeOptions` (Task 12).
- Produces:
  - `enum NfeStatus { None = 0, Processing = 1, Authorized = 2, Rejected = 3, Denied = 4 }`; `enum SalesInvoiceNfeXmlKind { Signed = 1, Authorized = 2 }`.
  - `SalesInvoice`: `NfeStatus`, `NfeEnvironment?`, `NfeRandomCode`, `NfeProtocol`, `NfeAuthorizedAt`, `NfeStatusCode`, `NfeStatusReason`, `NfeConfirmationError`.
  - Entidade `SalesInvoiceNfeXml` (`SALES_INVOICE_NFE_XMLS`) + `DbSet<SalesInvoiceNfeXml> SalesInvoiceNfeXmls`.
  - `NfeNumberReservationService(IDbConnection)` com `virtual Task<int> ReserveAsync(string branchCode)`.
  - `record NfeIssueContext(...)` (abaixo); `NfeReadinessValidator(IUnitOfWork, NfeOptions).ValidateAsync(SalesInvoice) : Task<NfeIssueContext>`.
  - `NfeIssueInputAssembler.Build(SalesInvoice, NfeIssueContext, DateTimeOffset issuedAt, NfeTechnicalResponsible?) : NfeIssueInput`; `NfeIssueInputAssembler.BrasiliaNow() : DateTimeOffset`.
  - Testes: `FakeNfeNumberReservationService`, `NfeTestSeed.SeedAsync() : Task<NfeScenario>`, `NfeTestSeed.Config(string? erp = "STANDALONE", bool withKey = true)`, `record NfeScenario(UnitOfWork Db, string DatabaseName, Guid InvoiceKey)`.

- [ ] **Step 1: Enums, entidade dos XMLs e colunas do documento**

```csharp
// SiagroB1.Domain/Enums/NfeStatus.cs
namespace SiagroB1.Domain.Enums;

/// <summary>Situação da NF-e do documento de saída (NF-e STANDALONE). None = nunca emitida.</summary>
public enum NfeStatus
{
    None = 0,
    Processing = 1,
    Authorized = 2,
    Rejected = 3,
    Denied = 4,
}
```

```csharp
// SiagroB1.Domain/Enums/SalesInvoiceNfeXmlKind.cs
namespace SiagroB1.Domain.Enums;

/// <summary>Tipo do XML guardado. O 2b acrescenta os eventos (cancelamento, CC-e).</summary>
public enum SalesInvoiceNfeXmlKind
{
    Signed = 1,
    Authorized = 2,
}
```

```csharp
// SiagroB1.Domain/Entities/SalesInvoiceNfeXml.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// XMLs da NF-e do documento. O assinado é gravado ANTES do envio: sem ele não há como montar o
/// procNFe se a resposta da SEFAZ se perder. Não é exposto no OData (download por função própria).
/// </summary>
[Table("SALES_INVOICE_NFE_XMLS")]
public class SalesInvoiceNfeXml
{
    [Key]
    public Guid Key { get; set; }

    [ForeignKey(nameof(SalesInvoice))]
    public Guid SalesInvoiceKey { get; set; }

    public virtual SalesInvoice? SalesInvoice { get; set; }

    public SalesInvoiceNfeXmlKind Kind { get; set; }

    [Column(TypeName = "NVARCHAR(MAX)")]
    public required string Xml { get; set; }

    public DateTime CreatedAt { get; set; }
}
```

Em `SiagroB1.Domain/Entities/SalesInvoice.cs`, depois de `PaymentConditionCode`:

```csharp
    // --- Emissão da NF-e STANDALONE. Só a emissão escreve aqui (create e PATCH ignoram). ---

    public NfeStatus NfeStatus { get; set; } = NfeStatus.None;

    public NfeEnvironment? NfeEnvironment { get; set; }

    /// <summary><c>cNF</c>: 8 dígitos, gerado na primeira tentativa e mantido nas seguintes.</summary>
    [Column(TypeName = "VARCHAR(8)")]
    public string? NfeRandomCode { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? NfeProtocol { get; set; }

    public DateTime? NfeAuthorizedAt { get; set; }

    /// <summary>Último <c>cStat</c> da SEFAZ.</summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? NfeStatusCode { get; set; }

    /// <summary>Último <c>xMotivo</c> da SEFAZ ou mensagem local.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? NfeStatusReason { get; set; }

    /// <summary>NF-e autorizada, mas a confirmação do documento falhou — "Concluir confirmação" refaz.</summary>
    [Column(TypeName = "VARCHAR(500)")]
    public string? NfeConfirmationError { get; set; }
```

Em `AppDbContext.cs`:

```csharp
    public DbSet<SalesInvoiceNfeXml> SalesInvoiceNfeXmls { get; set; }
```

```bash
git add SiagroB1.Domain/Enums/NfeStatus.cs SiagroB1.Domain/Enums/SalesInvoiceNfeXmlKind.cs SiagroB1.Domain/Entities/SalesInvoiceNfeXml.cs
```

- [ ] **Step 2: Reserva atômica do número**

```csharp
// SiagroB1.Application/Services/Nfe/NfeNumberReservationService.cs
using System.Data;
using Dapper;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Reserva o próximo número da NF-e da filial, atômica (mesmo padrão do DocNumberSequenceService).
/// ⚠️ Roda numa conexão Dapper própria, fora da transação do EF: chame antes de abrir transação.
/// O InMemory dos testes não roda SQL cru — use o fake.
/// </summary>
public class NfeNumberReservationService(IDbConnection connection)
{
    public virtual async Task<int> ReserveAsync(string branchCode)
    {
        const string sql = """
            UPDATE BRANCH_NFE_SETTINGS WITH (UPDLOCK, HOLDLOCK)
            SET NextNumber = NextNumber + 1
            OUTPUT deleted.NextNumber
            WHERE BranchCode = @BranchCode;
            """;

        return await connection.ExecuteScalarAsync<int?>(sql, new { BranchCode = branchCode })
               ?? throw new DefaultException($"A filial {branchCode} não tem a Configuração da NF-e.");
    }
}
```

```csharp
// SiagroB1.Application.Tests/Support/FakeNfeNumberReservationService.cs
using SiagroB1.Application.Services.Nfe;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Numeração em memória (o InMemory não roda o UPDATE ... OUTPUT).</summary>
public sealed class FakeNfeNumberReservationService(int next = 1) : NfeNumberReservationService(null!)
{
    private int _next = next;

    public int Calls { get; private set; }

    public override Task<int> ReserveAsync(string branchCode)
    {
        Calls++;
        return Task.FromResult(_next++);
    }
}
```

```bash
git add SiagroB1.Application/Services/Nfe/NfeNumberReservationService.cs SiagroB1.Application.Tests/Support/FakeNfeNumberReservationService.cs
```

- [ ] **Step 3: Cenário de teste compartilhado**

```csharp
// SiagroB1.Application.Tests/Support/NfeTestSeed.cs
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Support;

public sealed record NfeScenario(UnitOfWork Db, string DatabaseName, Guid InvoiceKey);

/// <summary>
/// Cenário completo da CEAGUI para a emissão: filial que emite (Itaberá/SP), configuração com
/// certificado de teste, cliente da BA contribuinte, transportadora, veículo, natureza, condição
/// 30/60 boleto e um documento Pendente com a linha já calculada (exemplo do sub-projeto 1).
/// </summary>
public static class NfeTestSeed
{
    public const string CardCode = "C-BA";

    public static readonly string CertificateKey =
        Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    public static IConfiguration Config(string? erp = "STANDALONE", bool withKey = true) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Erp"] = erp,
            ["Nfe:CertificateKey"] = withKey ? CertificateKey : null,
            ["Nfe:TechnicalResponsible:Cnpj"] = "09123456000100",
            ["Nfe:TechnicalResponsible:Contact"] = "IDX Consultoria",
            ["Nfe:TechnicalResponsible:Email"] = "fiscal@idx.com.br",
            ["Nfe:TechnicalResponsible:Phone"] = "1533334444",
        }).Build();

    public static async Task<NfeScenario> SeedAsync()
    {
        var name = Guid.NewGuid().ToString();
        var db = TestDb.CreateUnitOfWork(name);
        var context = db.Context;

        context.Municipalities.AddRange(
            new Municipality { Code = "3521705", Name = "Itaberá", StateAbbreviation = "SP" },
            new Municipality { Code = "3522406", Name = "Itapeva", StateAbbreviation = "SP" },
            new Municipality { Code = "2927408", Name = "Salvador", StateAbbreviation = "BA" });

        context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "CEAGUI", ShortName = "CEAGUI", TaxId = "12345678000195", StateCode = "SP",
            TaxRegime = TaxRegime.Normal, IssuesNfe = true, LegalName = "CEAGUI CEREAIS LTDA", TradeName = "CEAGUI",
            StateRegistration = "371012345110", Street = "RODOVIA SP 258", StreetNumber = "KM 290",
            District = "ZONA RURAL", MunicipalityCode = "3521705", ZipCode = "18440000", Phone = "1535621234",
        });

        context.BranchNfeSettings.Add(new BranchNfeSettings
        {
            BranchCode = "01", Environment = NfeEnvironment.Homologation, Series = 1, NextNumber = 1,
            CertificatePfx = TestCertificates.CreatePfx("12345678000195"),
            CertificatePasswordCipher = CertificatePasswordCipher.FromBase64(CertificateKey).Encrypt(TestCertificates.Password),
            CertificateSubject = "CN=CEAGUI CEREAIS LTDA:12345678000195", CertificateTaxId = "12345678000195",
            CertificateValidUntil = DateTime.Now.AddYears(1),
        });

        var usage = new Usage
        {
            Name = "Venda de grãos", InvoiceOperationText = "VENDA DE PRODUCAO DO ESTABELECIMENTO",
            DefaultAdditionalInfo = "Produto agrícola in natura.", CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
        };
        context.Usages.Add(usage);

        var condition = new PaymentCondition { Name = "30/60 boleto", Days = "30,60", PaymentMeans = "15" };
        context.PaymentConditions.Add(condition);
        await db.SaveChangesAsync();

        context.BusinessPartners.AddRange(
            new BusinessPartner
            {
                CardCode = CardCode, CardName = "CLIENTE BA LTDA", CardType = "C", TaxId = "11222333000181",
                StateRegistrationIndicator = StateRegistrationIndicator.Taxpayer, StateRegistration = "123456789",
                NfeEmail = "nfe@cliente.com.br", PaymentConditionCode = condition.Code,
                Addresses =
                [
                    new Address
                    {
                        CardCode = CardCode, AddressName = "FATURAMENTO", AdresType = "B", Street = "AV SETE DE SETEMBRO",
                        StreetNumber = "100", Block = "CENTRO", ZipCode = "40060000", City = "Salvador", State = "BA",
                        Country = "BR", MunicipalityCode = "2927408",
                    },
                ],
            },
            new BusinessPartner
            {
                CardCode = "T-001", CardName = "TRANSPORTADORA TESTE LTDA", CardType = "S", TaxId = "33444555000122",
                Addresses =
                [
                    new Address
                    {
                        CardCode = "T-001", AddressName = "FATURAMENTO", AdresType = "B", Street = "RUA B",
                        StreetNumber = "10", Block = "CENTRO", City = "Itapeva", State = "SP", MunicipalityCode = "3522406",
                    },
                ],
            });

        context.States.Add(new State { Code = "35", Name = "São Paulo", Abbreviation = "SP" });
        context.Trucks.Add(new Truck { Code = "ABC-1D23", StateKey = "35" });

        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = CardCode, CardName = "CLIENTE BA LTDA",
            InvoiceType = SalesInvoiceType.Normal, InvoiceStatus = InvoiceStatus.Pending, InvoiceNumber = "000002388",
            InvoiceDate = new DateTime(2026, 10, 2), GrossWeight = 30500m, NetWeight = 30000m,
            TruckingCompanyCode = "T-001", TruckCode = "ABC-1D23", FreightTerms = FreightTerms.Cif,
            PaymentConditionCode = condition.Code, TaxPayerComments = "Pedido do cliente 77",
            Items =
            [
                new SalesInvoiceItem
                {
                    Key = Guid.NewGuid(), ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
                    Quantity = 30000m, UnitPrice = 2m, UsageCode = usage.Code, UsageName = usage.Name, Cfop = "6102",
                    Ncm = "12019000", GoodsOrigin = 0,
                    CstIcms = "00", IcmsBase = 60000m, IcmsRate = 7m, IcmsValue = 4200m,
                    CstPis = "01", PisBase = 55800m, PisRate = 1.65m, PisValue = 920.70m,
                    CstCofins = "01", CofinsBase = 55800m, CofinsRate = 7.6m, CofinsValue = 4240.80m,
                    IbsCbsCst = "000", IbsCbsClassCode = "000001", IbsCbsBase = 50638.50m,
                    CbsRate = 0.9m, CbsValue = 455.75m, IbsStateRate = 0.1m, IbsStateValue = 50.64m,
                },
            ],
        };
        context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return new NfeScenario(db, name, invoice.Key);
    }
}
```

```bash
git add SiagroB1.Application.Tests/Support/NfeTestSeed.cs
```

- [ ] **Step 4: Write the failing tests**

```csharp
// SiagroB1.Application.Tests/Nfe/NfeReadinessValidatorTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>
/// Prontidão do cadastro antes de reservar número (spec §9.2 passo 2): UMA mensagem com tudo o
/// que falta, para o usuário corrigir de uma vez.
/// </summary>
public class NfeReadinessValidatorTests
{
    private static async Task<NfeIssueContext> ValidateAsync(NfeScenario scenario, bool withKey = true)
    {
        var invoice = await scenario.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == scenario.InvoiceKey);
        return await new NfeReadinessValidator(scenario.Db, new NfeOptions(NfeTestSeed.Config(withKey: withKey))).ValidateAsync(invoice);
    }

    [Fact]
    public async Task Complete_scenario_returns_the_loaded_context()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var context = await ValidateAsync(scenario);

        Assert.Equal("01", context.Branch.Code);
        Assert.Equal("Salvador", context.CustomerMunicipality.Name);
        Assert.Equal("T-001", context.Carrier!.CardCode);
        Assert.Equal("ABC1D23", context.TruckPlate);
        Assert.Equal("SP", context.TruckState);
        Assert.Equal("30/60 boleto", context.PaymentCondition.Name);
    }

    [Fact]
    public async Task Missing_data_is_listed_in_a_single_message()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var branch = await scenario.Db.Context.Branchs.SingleAsync();
        branch.StateRegistration = null;
        var address = await scenario.Db.Context.Addresses.SingleAsync(a => a.CardCode == NfeTestSeed.CardCode);
        address.MunicipalityCode = null;
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync();
        invoice.PaymentConditionCode = null;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario));

        Assert.StartsWith("Faltam dados para emitir a NF-e:", ex.Message);
        Assert.Contains("inscrição estadual", ex.Message);
        Assert.Contains("município do endereço de faturamento", ex.Message);
        Assert.Contains("condição de pagamento", ex.Message);
    }

    [Fact]
    public async Task Expired_certificate_is_reported()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var settings = await scenario.Db.Context.BranchNfeSettings.SingleAsync();
        settings.CertificateValidUntil = new DateTime(2026, 1, 31);
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario));

        Assert.Contains("vencido em 31/01/2026", ex.Message);
    }

    [Fact]
    public async Task Missing_server_key_is_reported()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario, withKey: false));

        Assert.Contains("Nfe:CertificateKey", ex.Message);
    }

    [Fact]
    public async Task Masked_tax_id_is_accepted()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var customer = await scenario.Db.Context.BusinessPartners.SingleAsync(p => p.CardCode == NfeTestSeed.CardCode);
        customer.TaxId = "11.222.333/0001-81";
        await scenario.Db.SaveChangesAsync();

        var context = await ValidateAsync(scenario);

        Assert.Equal(NfeTestSeed.CardCode, context.Customer.CardCode);
    }

    [Fact]
    public async Task Inactive_payment_condition_is_reported()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.PaymentConditions.SingleAsync()).Inactive = true;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ValidateAsync(scenario));

        Assert.Contains("inativa", ex.Message);
    }
}
```

```csharp
// SiagroB1.Application.Tests/Nfe/NfeIssueInputAssemblerTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Documento + cadastro carregado → entrada do XML, normalizada.</summary>
public class NfeIssueInputAssemblerTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(-3));

    private static async Task<NfeIssueInput> BuildAsync(NfeScenario scenario, Action<SalesInvoice>? change = null)
    {
        var invoice = await scenario.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == scenario.InvoiceKey);
        invoice.TaxDocumentNumber = "000000001";
        invoice.TaxDocumentSeries = "1";
        invoice.NfeRandomCode = "48151623";
        change?.Invoke(invoice);
        await scenario.Db.SaveChangesAsync();

        var options = new NfeOptions(NfeTestSeed.Config());
        var context = await new NfeReadinessValidator(scenario.Db, options).ValidateAsync(invoice);

        return NfeIssueInputAssembler.Build(invoice, context, IssuedAt, options.TechnicalResponsible);
    }

    [Fact]
    public async Task Masked_tax_ids_are_sent_only_with_alphanumerics()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.Branchs.SingleAsync()).TaxId = "12.345.678/0001-95";
        (await scenario.Db.Context.BusinessPartners.SingleAsync(p => p.CardCode == NfeTestSeed.CardCode)).TaxId = "11.222.333/0001-81";
        await scenario.Db.SaveChangesAsync();

        var input = await BuildAsync(scenario);

        Assert.Equal("12345678000195", input.Issuer.TaxId);
        Assert.Equal("11222333000181", input.Recipient.TaxId);
    }

    [Fact]
    public async Task Identification_comes_from_the_document_and_the_settings()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var input = await BuildAsync(scenario);

        Assert.Equal(NfeEnvironment.Homologation, input.Environment);
        Assert.Equal(1, input.Series);
        Assert.Equal(1L, input.Number);
        Assert.Equal("48151623", input.RandomCode);
        Assert.Equal("000000001", input.BillingNumber);
        Assert.Equal(IssuedAt, input.IssuedAt);
        Assert.Equal("3521705", input.Issuer.Address.MunicipalityCode);
        Assert.Equal("2927408", input.Recipient.Address.MunicipalityCode);
        Assert.Equal("09123456000100", input.TechnicalResponsible!.Cnpj);
    }

    [Fact]
    public async Task Operation_nature_comes_from_the_first_line_usage()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        Assert.Equal("VENDA DE PRODUCAO DO ESTABELECIMENTO", (await BuildAsync(scenario)).OperationNature);
    }

    [Fact]
    public async Task Operation_nature_falls_back_to_the_usage_name()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.Usages.SingleAsync()).InvoiceOperationText = null;
        await scenario.Db.SaveChangesAsync();

        Assert.Equal("Venda de grãos", (await BuildAsync(scenario)).OperationNature);
    }

    [Fact]
    public async Task Additional_info_joins_usage_text_and_taxpayer_comments()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var input = await BuildAsync(scenario);

        Assert.Equal("Produto agrícola in natura. | Pedido do cliente 77", input.AdditionalInfo);
    }

    [Fact]
    public async Task Line_carries_the_recorded_taxes()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var item = Assert.Single((await BuildAsync(scenario)).Items);

        Assert.Equal(1, item.Number);
        Assert.Equal("6102", item.Cfop);
        Assert.Equal("00", item.IcmsCode);
        Assert.Equal(4200m, item.IcmsValue);
        Assert.Equal(60000m, item.Total);
        Assert.Equal(455.75m, item.CbsValue);
    }

    [Fact]
    public async Task Payment_follows_the_condition_and_the_document_total()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var payment = (await BuildAsync(scenario)).Payment;

        Assert.Equal("15", payment.PaymentMeans);
        Assert.Equal([30000m, 30000m], payment.Installments.Select(i => i.Amount));
        Assert.Equal(new DateOnly(2026, 11, 1), payment.Installments[0].DueDate);
    }

    [Fact]
    public async Task Vehicle_plate_is_normalized_and_state_comes_from_the_truck()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var input = await BuildAsync(scenario);

        Assert.Equal(new NfeVehicle("ABC1D23", "SP"), input.Vehicle);
        Assert.Equal("TRANSPORTADORA TESTE LTDA", input.Carrier!.Name);
        Assert.Equal("RUA B, 10", input.Carrier.FullAddress);
    }

    [Fact]
    public async Task Delivery_only_when_another_partner_receives()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        Assert.Null((await BuildAsync(scenario)).Delivery);
        Assert.Null((await BuildAsync(scenario, i => i.DeliveryCardCode = NfeTestSeed.CardCode)).Delivery);
    }
}
```

```bash
git add SiagroB1.Application.Tests/Nfe/NfeReadinessValidatorTests.cs SiagroB1.Application.Tests/Nfe/NfeIssueInputAssemblerTests.cs
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeReadinessValidatorTests|FullyQualifiedName~NfeIssueInputAssemblerTests"`
Expected: FAIL de compilação — `NfeReadinessValidator` não existe.

- [ ] **Step 6: Contexto e prontidão**

```csharp
// SiagroB1.Application/Services/Nfe/NfeIssueContext.cs
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Cadastro já carregado e conferido pela prontidão — tudo o que a montagem da entrada usa.</summary>
public sealed record NfeIssueContext(
    Branch Branch,
    Municipality BranchMunicipality,
    BranchNfeSettings Settings,
    BusinessPartner Customer,
    Address CustomerAddress,
    Municipality CustomerMunicipality,
    BusinessPartner? DeliveryPartner,
    Address? DeliveryAddress,
    Municipality? DeliveryMunicipality,
    BusinessPartner? Carrier,
    Address? CarrierAddress,
    string? TruckPlate,
    string? TruckState,
    PaymentCondition PaymentCondition,
    IReadOnlyDictionary<int, Usage> Usages);
```

```csharp
// SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Confere o cadastro antes de reservar número (spec §9.2 passo 2). Junta TODAS as lacunas numa
/// mensagem só — emitente, configuração, certificado, chave do servidor, destinatário, entrega,
/// transportadora e condição de pagamento — e devolve o cadastro carregado para a montagem.
/// </summary>
public class NfeReadinessValidator(IUnitOfWork db, NfeOptions options)
{
    public async Task<NfeIssueContext> ValidateAsync(SalesInvoice invoice)
    {
        var problems = new List<string>();

        var branch = await db.Context.Branchs.AsNoTracking().Include(b => b.Municipality)
                         .FirstOrDefaultAsync(b => b.Code == invoice.BranchCode)
                     ?? throw new DefaultException($"Filial {invoice.BranchCode} não encontrada.");

        var branchGaps = Gaps(
            ("razão social", branch.LegalName), ("inscrição estadual", branch.StateRegistration),
            ("logradouro", branch.Street), ("número", branch.StreetNumber), ("bairro", branch.District),
            ("município", branch.Municipality?.Code), ("CEP", branch.ZipCode));
        if (!IsTaxId(branch.TaxId)) branchGaps.Insert(0, "CNPJ");
        if (branch.TaxRegime is null) branchGaps.Add("regime tributário (CRT)");
        Report(problems, $"Filial {branch.Code}", branchGaps);

        var settings = await db.Context.BranchNfeSettings.AsNoTracking().FirstOrDefaultAsync(s => s.BranchCode == branch.Code);
        if (settings is null)
            problems.Add("Configuração da NF-e da filial não cadastrada");
        else if (settings.CertificatePfx is null)
            problems.Add("Certificado digital não enviado (Configuração da NF-e)");
        else if (settings.CertificateValidUntil < DateTime.Now)
            problems.Add($"Certificado digital vencido em {settings.CertificateValidUntil:dd/MM/yyyy}");

        if (!options.HasCertificateKey)
            problems.Add("Chave Nfe:CertificateKey não configurada no servidor");

        var customer = await LoadPartnerAsync(invoice.CardCode);
        var customerAddress = customer is null ? null : BillingAddress(customer);
        if (customer is null)
        {
            problems.Add($"Cliente {invoice.CardCode} não encontrado");
        }
        else
        {
            var gaps = new List<string>();
            if (!IsTaxId(customer.TaxId)) gaps.Add("CNPJ/CPF");
            if (customer.StateRegistrationIndicator is null) gaps.Add("indicador da inscrição estadual");
            else if (customer.StateRegistrationIndicator == StateRegistrationIndicator.Taxpayer &&
                     string.IsNullOrWhiteSpace(customer.StateRegistration)) gaps.Add("inscrição estadual");
            AddressGaps(gaps, customerAddress, "endereço de faturamento");
            Report(problems, $"Cliente {customer.CardCode}", gaps);
        }

        BusinessPartner? deliveryPartner = null;
        Address? deliveryAddress = null;
        if (!string.IsNullOrWhiteSpace(invoice.DeliveryCardCode) && invoice.DeliveryCardCode != invoice.CardCode)
        {
            deliveryPartner = await LoadPartnerAsync(invoice.DeliveryCardCode);
            deliveryAddress = deliveryPartner is null ? null : DeliveryAddress(deliveryPartner);

            if (deliveryPartner is null)
            {
                problems.Add($"Local de entrega {invoice.DeliveryCardCode} não encontrado");
            }
            else
            {
                var gaps = new List<string>();
                if (!IsTaxId(deliveryPartner.TaxId)) gaps.Add("CNPJ/CPF");
                AddressGaps(gaps, deliveryAddress, "endereço");
                Report(problems, $"Local de entrega {deliveryPartner.CardCode}", gaps);
            }
        }

        BusinessPartner? carrier = null;
        if (!string.IsNullOrWhiteSpace(invoice.TruckingCompanyCode))
        {
            carrier = await LoadPartnerAsync(invoice.TruckingCompanyCode);
            if (carrier is null)
                problems.Add($"Transportadora {invoice.TruckingCompanyCode} não encontrada");
            else if (!string.IsNullOrWhiteSpace(carrier.TaxId) && !IsTaxId(carrier.TaxId))
                problems.Add($"Transportadora {carrier.CardCode}: CNPJ/CPF");
        }

        PaymentCondition? condition = null;
        if (invoice.PaymentConditionCode is null)
        {
            problems.Add("Documento: condição de pagamento");
        }
        else
        {
            condition = await db.Context.PaymentConditions.AsNoTracking().FirstOrDefaultAsync(c => c.Code == invoice.PaymentConditionCode);
            if (condition is null)
                problems.Add($"Documento: condição de pagamento {invoice.PaymentConditionCode} não encontrada");
            else if (condition.Inactive)
                problems.Add($"Documento: a condição de pagamento {condition.Name} está inativa");
        }

        if (problems.Count > 0)
            throw new DefaultException("Faltam dados para emitir a NF-e:\n- " + string.Join("\n- ", problems));

        var (plate, truckState) = await LoadTruckAsync(invoice.TruckCode);
        var usageCodes = invoice.Items.Where(i => i.UsageCode is not null).Select(i => i.UsageCode!.Value).Distinct().ToList();
        var usages = await db.Context.Usages.AsNoTracking().Where(u => usageCodes.Contains(u.Code)).ToDictionaryAsync(u => u.Code);

        return new NfeIssueContext(
            branch, branch.Municipality!, settings!, customer!, customerAddress!, customerAddress!.Municipality!,
            deliveryPartner, deliveryAddress, deliveryAddress?.Municipality,
            carrier, carrier is null ? null : BillingAddress(carrier),
            plate, truckState, condition!, usages);
    }

    private Task<BusinessPartner?> LoadPartnerAsync(string cardCode) =>
        db.Context.BusinessPartners.AsNoTracking()
            .Include(p => p.Addresses).ThenInclude(a => a.Municipality)
            .FirstOrDefaultAsync(p => p.CardCode == cardCode);

    /// <summary>Mesma escolha de <c>SalesInvoicesCfopResolveService.ResolvePartnerState</c>: faturamento primeiro.</summary>
    private static Address? BillingAddress(BusinessPartner partner) =>
        partner.Addresses.FirstOrDefault(a =>
            string.Equals(a.AdresType, "B", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a.AddressName, "FATURAMENTO", StringComparison.OrdinalIgnoreCase))
        ?? partner.Addresses.FirstOrDefault();

    /// <summary>Local de entrega: endereço de entrega com município; senão o de faturamento.</summary>
    private static Address? DeliveryAddress(BusinessPartner partner) =>
        partner.Addresses.FirstOrDefault(a =>
            string.Equals(a.AdresType, "S", StringComparison.OrdinalIgnoreCase) && a.MunicipalityCode is not null)
        ?? BillingAddress(partner);

    private async Task<(string? Plate, string? State)> LoadTruckAsync(string? truckCode)
    {
        if (string.IsNullOrWhiteSpace(truckCode))
            return (null, null);

        var truck = await db.Context.Trucks.AsNoTracking().Include(t => t.State).FirstOrDefaultAsync(t => t.Code == truckCode);

        return (NfeText.AlphaNumeric(truckCode), truck?.State?.Abbreviation);
    }

    private static bool IsTaxId(string? value) => NfeText.AlphaNumeric(value).Length is 11 or 14;

    private static void AddressGaps(List<string> gaps, Address? address, string label)
    {
        if (address is null)
        {
            gaps.Add(label);
            return;
        }

        gaps.AddRange(Gaps(
            ($"logradouro do {label}", address.Street), ($"número do {label}", address.StreetNumber),
            ($"bairro do {label}", address.Block), ($"município do {label}", address.Municipality?.Code)));
    }

    private static List<string> Gaps(params (string Label, string? Value)[] fields) =>
        fields.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Label).ToList();

    private static void Report(List<string> problems, string owner, List<string> gaps)
    {
        if (gaps.Count > 0)
            problems.Add($"{owner}: {string.Join(", ", gaps)}");
    }
}
```

- [ ] **Step 7: Montagem da entrada**

```csharp
// SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs
using System.Globalization;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Payments;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Documento + cadastro conferido → <see cref="NfeIssueInput"/>. Todo CNPJ/CPF, IE, CEP, telefone
/// e placa sai normalizado daqui (ver <see cref="NfeText"/>): o builder confia no que recebe.
/// </summary>
public static class NfeIssueInputAssembler
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary><c>dhEmi</c> em America/Sao_Paulo (IANA: funciona em Linux e Windows com ICU).</summary>
    public static DateTimeOffset BrasiliaNow() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Brasilia);

    public static NfeIssueInput Build(
        SalesInvoice invoice, NfeIssueContext context, DateTimeOffset issuedAt, NfeTechnicalResponsible? technicalResponsible)
    {
        var items = invoice.Items.ToList();
        var total = items.Sum(i => i.Total);
        var firstUsage = items.Select(i => i.UsageCode).FirstOrDefault(c => c is not null) is { } code
                         && context.Usages.TryGetValue(code, out var usage)
            ? usage
            : null;

        return new NfeIssueInput
        {
            Environment = context.Settings.Environment,
            Series = int.Parse(invoice.TaxDocumentSeries!, CultureInfo.InvariantCulture),
            Number = long.Parse(invoice.TaxDocumentNumber!, CultureInfo.InvariantCulture),
            RandomCode = invoice.NfeRandomCode!,
            IssuedAt = issuedAt,
            OperationNature = string.IsNullOrWhiteSpace(firstUsage?.InvoiceOperationText)
                ? firstUsage?.Name ?? "VENDA"
                : firstUsage.InvoiceOperationText,
            ApplicationVersion = $"SiagroB1 {typeof(NfeIssueInputAssembler).Assembly.GetName().Version?.ToString(3)}",
            Issuer = new NfeIssuer
            {
                TaxId = NfeText.AlphaNumeric(context.Branch.TaxId),
                LegalName = context.Branch.LegalName!,
                TradeName = context.Branch.TradeName,
                StateRegistration = NfeText.AlphaNumeric(context.Branch.StateRegistration),
                TaxRegime = context.Branch.TaxRegime!.Value,
                Address = new NfeAddress
                {
                    Street = context.Branch.Street!, Number = context.Branch.StreetNumber!, Complement = context.Branch.Complement,
                    District = context.Branch.District!, MunicipalityCode = context.BranchMunicipality.Code,
                    MunicipalityName = context.BranchMunicipality.Name, State = context.BranchMunicipality.StateAbbreviation,
                    ZipCode = NfeText.Digits(context.Branch.ZipCode), Phone = NfeText.Digits(context.Branch.Phone),
                },
            },
            Recipient = new NfeRecipient
            {
                TaxId = NfeText.AlphaNumeric(context.Customer.TaxId),
                Name = context.Customer.CardName,
                Indicator = context.Customer.StateRegistrationIndicator!.Value,
                StateRegistration = NfeText.Digits(context.Customer.StateRegistration),
                Email = context.Customer.NfeEmail,
                Address = ToAddress(context.CustomerAddress, context.CustomerMunicipality, context.Customer.Phone),
            },
            Delivery = context.DeliveryPartner is null
                ? null
                : new NfeDelivery
                {
                    TaxId = NfeText.AlphaNumeric(context.DeliveryPartner.TaxId),
                    Name = context.DeliveryPartner.CardName,
                    StateRegistration = NfeText.Digits(context.DeliveryPartner.StateRegistration),
                    Address = ToAddress(context.DeliveryAddress!, context.DeliveryMunicipality!, context.DeliveryPartner.Phone),
                },
            Items = items.Select((item, index) => ToItem(item, index + 1)).ToList(),
            FreightTerms = invoice.FreightTerms,
            Carrier = context.Carrier is null
                ? null
                : new NfeCarrier
                {
                    TaxId = string.IsNullOrWhiteSpace(context.Carrier.TaxId) ? null : NfeText.AlphaNumeric(context.Carrier.TaxId),
                    Name = context.Carrier.CardName,
                    StateRegistration = NfeText.Digits(context.Carrier.StateRegistration) is { Length: > 0 } ie ? ie : null,
                    FullAddress = context.CarrierAddress is null
                        ? null
                        : string.Join(", ", new[] { context.CarrierAddress.Street, context.CarrierAddress.StreetNumber }
                            .Where(s => !string.IsNullOrWhiteSpace(s))),
                    MunicipalityName = context.CarrierAddress?.City,
                    State = context.CarrierAddress?.State,
                },
            Vehicle = context.TruckPlate is not null && context.TruckState is not null
                ? new NfeVehicle(context.TruckPlate, context.TruckState)
                : null,
            NetWeight = invoice.NetWeight,
            GrossWeight = invoice.GrossWeight,
            Payment = PaymentInstallmentCalculator.Calculate(
                context.PaymentCondition.Days, context.PaymentCondition.StartRule, context.PaymentCondition.PaymentMeans,
                total, DateOnly.FromDateTime(issuedAt.Date)),
            BillingNumber = invoice.TaxDocumentNumber!,
            AdditionalInfo = AdditionalInfo(items, context.Usages, invoice.TaxPayerComments),
            FiscoInfo = invoice.TaxComments,
            TechnicalResponsible = technicalResponsible,
        };
    }

    private static NfeAddress ToAddress(Address address, Municipality municipality, string? phone) => new()
    {
        Street = address.Street!,
        Number = address.StreetNumber!,
        Complement = address.Complement,
        District = address.Block!,
        MunicipalityCode = municipality.Code,
        MunicipalityName = municipality.Name,
        State = municipality.StateAbbreviation,
        ZipCode = NfeText.Digits(address.ZipCode) is { Length: 8 } zip ? zip : null,
        Phone = NfeText.Digits(phone),
    };

    private static NfeItem ToItem(SalesInvoiceItem item, int number) => new()
    {
        Number = number,
        ItemCode = item.ItemCode,
        Description = string.IsNullOrWhiteSpace(item.ItemName) ? item.ItemCode : item.ItemName,
        Ncm = item.Ncm!,
        Cfop = item.Cfop!,
        UnitOfMeasure = item.UnitOfMeasureCode,
        Quantity = item.Quantity,
        UnitPrice = item.UnitPrice,
        Total = item.Total,
        GoodsOrigin = item.GoodsOrigin ?? 0,
        BenefitCode = item.IcmsBenefitCode,
        IcmsCode = item.CstIcms!,
        IcmsBase = item.IcmsBase,
        IcmsRate = item.IcmsRate,
        IcmsValue = item.IcmsValue,
        IcmsBaseReduction = item.IcmsBaseReduction,
        IcmsDeferral = item.IcmsDeferral,
        IcmsOperationValue = item.IcmsOperationValue,
        IcmsDeferredValue = item.IcmsDeferredValue,
        PisCst = item.CstPis!,
        PisBase = item.PisBase,
        PisRate = item.PisRate,
        PisValue = item.PisValue,
        CofinsCst = item.CstCofins!,
        CofinsBase = item.CofinsBase,
        CofinsRate = item.CofinsRate,
        CofinsValue = item.CofinsValue,
        IbsCbsCst = item.IbsCbsCst,
        IbsCbsClassCode = item.IbsCbsClassCode,
        IbsCbsBase = item.IbsCbsBase,
        IbsStateRate = item.IbsStateRate,
        IbsMunicipalRate = item.IbsMunicipalRate,
        IbsRateReduction = item.IbsRateReduction,
        IbsStateValue = item.IbsStateValue,
        IbsMunicipalValue = item.IbsMunicipalValue,
        CbsRate = item.CbsRate,
        CbsRateReduction = item.CbsRateReduction,
        CbsValue = item.CbsValue,
    };

    /// <summary><c>infCpl</c>: textos padrão distintos das naturezas, na ordem das linhas, + informações do contribuinte.</summary>
    private static string? AdditionalInfo(
        IEnumerable<SalesInvoiceItem> items, IReadOnlyDictionary<int, Usage> usages, string? taxPayerComments)
    {
        var texts = items
            .Select(i => i.UsageCode is { } code && usages.TryGetValue(code, out var usage) ? usage.DefaultAdditionalInfo : null)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct()
            .ToList();

        if (!string.IsNullOrWhiteSpace(taxPayerComments))
            texts.Add(taxPayerComments.Trim());

        return texts.Count == 0 ? null : string.Join(" | ", texts);
    }
}
```

DI em `ServiceCollectionExtensions.cs` (junto de `BranchNfeSettingsService`):

```csharp
        services.AddScoped<NfeNumberReservationService>();
        services.AddScoped<NfeReadinessValidator>();
```

```bash
git add SiagroB1.Application/Services/Nfe/NfeIssueContext.cs SiagroB1.Application/Services/Nfe/NfeReadinessValidator.cs SiagroB1.Application/Services/Nfe/NfeIssueInputAssembler.cs
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeReadinessValidatorTests|FullyQualifiedName~NfeIssueInputAssemblerTests"`
Expected: PASS.

- [ ] **Step 9: Migration**

Run: `dotnet ef migrations add AddSalesInvoiceNfeIssuance --context AppDbContext --project SiagroB1.Migrations --startup-project SiagroB1.Web`

Leia: 8 colunas novas em `SALES_INVOICES` (`NfeStatus` INT NOT NULL **DEFAULT 0**, as demais anuláveis) e `CreateTable SALES_INVOICE_NFE_XMLS` com FK em cascata para `SALES_INVOICES`. Se o `AddColumn` de `NfeStatus` não tiver `defaultValue: 0`, acrescente — documento existente nasce `None`.

```bash
git add SiagroB1.Migrations/AppContext/*_AddSalesInvoiceNfeIssuance*.cs
```

- [ ] **Step 10: Suíte**

Run: `dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Passed!`.

- [ ] **Step 11: Commit**

```bash
git commit -m "feat(invoice): preparar o documento de saída para a emissão da NF-e" -m "Situação, protocolo, último retorno e erro de confirmação no documento; tabela dos XMLs (o assinado é gravado antes do envio). Numeração atômica por filial. A prontidão junta numa mensagem só tudo o que falta no cadastro, e a montagem normaliza CNPJ/CPF, IE, CEP e placa — a Zeus guardaria só o primeiro trecho de um CNPJ com máscara.

DB: AddSalesInvoiceNfeIssuance

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Domain SiagroB1.Infra/Context/AppDbContext.cs SiagroB1.Application/Services/Nfe SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs SiagroB1.Migrations SiagroB1.Application.Tests
```

---

### Task 14: Emitir NF-e

**Files:**
- Create: `SiagroB1.Domain/Dtos/Nfe/NfeIssueOutcomeDto.cs`
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoiceNfeResultHandler.cs`, `SalesInvoicesNfeIssueService.cs`
- Create: `SiagroB1.Web/Actions/Nfe/SalesInvoicesIssueNfeController.cs`
- Create: `SiagroB1.Application.Tests/Support/RecordingConfirmService.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs` (só `virtual`), `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeIssueServiceTests.cs`

**Interfaces:**
- Consumes: tudo das Tasks 10–13; `TaxCalculationGate`; `SalesInvoicesConfirmService`.
- Produces:
  - `NfeIssueOutcomeDto { NfeStatus NfeStatus; InvoiceStatus? InvoiceStatus; string? StatusCode; string? Reason; string? AccessKey; string? ConfirmationError; static From(SalesInvoice) }`.
  - `SalesInvoiceNfeResultHandler(IUnitOfWork, SalesInvoicesConfirmService)`: `ApplyAuthorizationAsync(SalesInvoice, string signedXml, NfeSefazResult, string userName)`, `ApplyConsultAsync(...)` (mesmos parâmetros), `ConfirmAsync(Guid key, string userName)` — todos `Task<NfeIssueOutcomeDto>`.
  - `SalesInvoicesNfeIssueService.ExecuteAsync(Guid key, string userName) : Task<NfeIssueOutcomeDto>`.
  - Action `SalesInvoicesIssueNfe(Key)` → `NfeIssueOutcomeDto`.
  - `SalesInvoicesConfirmService.ExecuteAsync` passa a ser `virtual`.

- [ ] **Step 1: Confirmação simulável**

Em `SalesInvoicesConfirmService.cs`, troque `public async Task ExecuteAsync(` por `public virtual async Task ExecuteAsync(` (nada mais nesta tarefa).

```csharp
// SiagroB1.Application.Tests/Support/RecordingConfirmService.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Confirmação simulada: muda o rastreador como a real (status Confirmado) e, com
/// <paramref name="failWith"/>, estoura DEPOIS de mexer — é o que prova que o tratamento da
/// falha não grava confirmação pela metade.
/// </summary>
public sealed class RecordingConfirmService(UnitOfWork db, Exception? failWith = null)
    : SalesInvoicesConfirmService(null!, null!, null!, null!, null!, null!, null!, null!, null!)
{
    public int Calls { get; private set; }

    public override async Task ExecuteAsync(
        Guid key, string userName, CommitMode commitMode = CommitMode.Auto,
        IReadOnlyDictionary<Guid, StorageTransactionsStatus>? shipmentOutcomes = null)
    {
        Calls++;

        var invoice = await db.Context.SalesInvoices.FirstAsync(i => i.Key == key);
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;

        if (failWith is not null)
            throw failWith;

        invoice.ApprovedBy = userName;
        await db.SaveChangesAsync();
    }
}
```

(O nome do enum `StorageTransactionsStatus` e o namespace de `CommitMode` são os da assinatura real em `SalesInvoicesConfirmService.cs` — copie de lá se divergirem.)

```bash
git add SiagroB1.Application.Tests/Support/RecordingConfirmService.cs
```

- [ ] **Step 2: Write the failing test**

```csharp
// SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeIssueServiceTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Emitir NF-e" (spec §9.2), com a SEFAZ simulada e o XML assinado de verdade.</summary>
public class SalesInvoicesNfeIssueServiceTests
{
    private static SalesInvoicesNfeIssueService Issue(
        NfeScenario scenario, FakeNfeSefazClient sefaz, SalesInvoicesConfirmService confirm,
        string erp = "STANDALONE", FakeNfeNumberReservationService? reservation = null)
    {
        var config = NfeTestSeed.Config(erp);
        var options = new NfeOptions(config);

        return new SalesInvoicesNfeIssueService(
            scenario.Db,
            new TaxCalculationGate(scenario.Db, config),
            new NfeReadinessValidator(scenario.Db, options),
            new BranchNfeSettingsService(scenario.Db, options, sefaz),
            reservation ?? new FakeNfeNumberReservationService(),
            sefaz,
            new SalesInvoiceNfeResultHandler(scenario.Db, confirm),
            options);
    }

    private static Task<Domain.Entities.SalesInvoice> ReloadAsync(NfeScenario scenario) =>
        TestDb.CreateUnitOfWork(scenario.DatabaseName).Context.SalesInvoices.AsNoTracking()
            .SingleAsync(i => i.Key == scenario.InvoiceKey);

    [Fact]
    public async Task Authorized_nfe_is_saved_and_the_document_confirmed()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var confirm = new RecordingConfirmService(scenario.Db);

        var outcome = await Issue(scenario, sefaz, confirm).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Equal(NfeStatus.Authorized, invoice.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, invoice.InvoiceStatus);
        Assert.Equal("000000001", invoice.TaxDocumentNumber);
        Assert.Equal("1", invoice.TaxDocumentSeries);
        Assert.Equal(44, invoice.ChaveNFe!.Length);
        Assert.Equal("135260000000001", invoice.NfeProtocol);
        Assert.Equal(NfeEnvironment.Homologation, invoice.NfeEnvironment);
        Assert.Equal(1, confirm.Calls);

        var xmls = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().ToListAsync();
        Assert.Contains(xmls, x => x.Kind == SalesInvoiceNfeXmlKind.Signed && x.Xml.Contains("NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"));
        Assert.Contains(xmls, x => x.Kind == SalesInvoiceNfeXmlKind.Authorized && x.Xml.Contains("<nfeProc") && x.Xml.Contains("<protNFe"));
    }

    [Fact]
    public async Task Processing_and_signed_xml_are_saved_before_sending()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        NfeStatus? statusAtSend = null;
        var signedAtSend = 0;
        sefaz.BeforeAuthorize = async () =>
        {
            var other = TestDb.CreateUnitOfWork(scenario.DatabaseName).Context;
            statusAtSend = (await other.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus;
            signedAtSend = await other.SalesInvoiceNfeXmls.CountAsync(x => x.Kind == SalesInvoiceNfeXmlKind.Signed);
        };
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Processing, statusAtSend);
        Assert.Equal(1, signedAtSend);
    }

    [Fact]
    public async Task Rejection_keeps_the_document_pending_with_the_number()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => FakeNfeSefazClient.Rejected());
        var confirm = new RecordingConfirmService(scenario.Db);

        var outcome = await Issue(scenario, sefaz, confirm).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Rejected, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);
        Assert.Equal("209", invoice.NfeStatusCode);
        Assert.Contains("IE do emitente", invoice.NfeStatusReason);
        Assert.Equal("000000001", invoice.TaxDocumentNumber);
        Assert.Equal(0, confirm.Calls);
    }

    [Fact]
    public async Task Retry_after_rejection_reuses_the_number_and_the_random_code()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => FakeNfeSefazClient.Rejected());
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var reservation = new FakeNfeNumberReservationService();
        var service = Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db), reservation: reservation);

        await service.ExecuteAsync(scenario.InvoiceKey, "tester");
        var randomCode = (await ReloadAsync(scenario)).NfeRandomCode;
        await service.ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Authorized, invoice.NfeStatus);
        Assert.Equal("000000001", invoice.TaxDocumentNumber);
        Assert.Equal(randomCode, invoice.NfeRandomCode);
        Assert.Equal(1, reservation.Calls);
        Assert.Equal(sefaz.Sent[0].AccessKey[..34], sefaz.Sent[1].AccessKey[..34]); // mesma UF/AAMM/CNPJ/modelo/série/número
    }

    [Fact]
    public async Task Denied_nfe_blocks_new_attempts()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => new Fiscal.Nfe.NfeSefazResult(302, "Uso Denegado: Irregularidade fiscal do destinatário", "135260000000002"));
        var service = Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db));

        var outcome = await service.ExecuteAsync(scenario.InvoiceKey, "tester");
        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal(NfeStatus.Denied, outcome.NfeStatus);
        Assert.Contains("denegada", ex.Message);
    }

    [Fact]
    public async Task Duplicate_is_consulted_and_followed()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => new Fiscal.Nfe.NfeSefazResult(204, "Rejeição: Duplicidade de NF-e"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var confirm = new RecordingConfirmService(scenario.Db);

        var outcome = await Issue(scenario, sefaz, confirm).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Single(sefaz.Consulted);
        Assert.Equal(1, confirm.Calls);
    }

    [Fact]
    public async Task No_response_keeps_the_document_processing()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(FakeNfeSefazClient.NoResponse);

        var outcome = await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Processing, outcome.NfeStatus);
        Assert.Equal(NfeStatus.Processing, invoice.NfeStatus);
        Assert.Contains("Consultar situação", invoice.NfeStatusReason);
    }

    [Fact]
    public async Task Local_schema_failure_is_rejected_without_sending()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.SalesInvoicesItems.SingleAsync()).Ncm = "1201";
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var outcome = await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Rejected, outcome.NfeStatus);
        Assert.Contains("validação local", outcome.Reason);
        Assert.Empty(sefaz.Sent);
        Assert.Empty(await scenario.Db.Context.SalesInvoiceNfeXmls.ToListAsync());
    }

    [Fact]
    public async Task Confirmation_failure_keeps_the_nfe_and_records_the_error_without_partial_confirmation()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var confirm = new RecordingConfirmService(scenario.Db, failWith: new DefaultException("Liberação de entrega sem saldo."));

        var outcome = await Issue(scenario, sefaz, confirm).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Authorized, invoice.NfeStatus);
        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);      // a mudança no rastreador foi descartada
        Assert.Contains("sem saldo", invoice.NfeConfirmationError);
        Assert.Contains("sem saldo", outcome.ConfirmationError);
    }

    [Fact]
    public async Task Rule_inactive_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(scenario, new FakeNfeSefazClient(), new RecordingConfirmService(scenario.Db), erp: "SAPB1")
                .ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Contains("não emite NF-e", ex.Message);
    }

    [Fact]
    public async Task Line_without_calculated_taxes_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.SalesInvoicesItems.SingleAsync()).CstIcms = null;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(scenario, new FakeNfeSefazClient(), new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Contains("Salve o documento", ex.Message);
    }

    [Fact]
    public async Task Return_document_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).InvoiceType = SalesInvoiceType.Return;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(scenario, new FakeNfeSefazClient(), new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Contains("Normal", ex.Message);
    }
}
```

```bash
git add SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeIssueServiceTests.cs
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesNfeIssueServiceTests"`
Expected: FAIL de compilação — `SalesInvoicesNfeIssueService` não existe.

- [ ] **Step 4: Resultado da emissão, tratamento do retorno e o serviço**

```csharp
// SiagroB1.Domain/Dtos/Nfe/NfeIssueOutcomeDto.cs
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos.Nfe;

/// <summary>
/// Desfecho de emitir/consultar/concluir: a tela mostra a situação e o motivo. Rejeição e falta
/// de resposta são desfechos (200), não erro de requisição.
/// </summary>
public class NfeIssueOutcomeDto
{
    public NfeStatus NfeStatus { get; set; }
    public InvoiceStatus? InvoiceStatus { get; set; }
    public string? StatusCode { get; set; }
    public string? Reason { get; set; }
    public string? AccessKey { get; set; }
    public string? ConfirmationError { get; set; }

    public static NfeIssueOutcomeDto From(SalesInvoice invoice) => new()
    {
        NfeStatus = invoice.NfeStatus,
        InvoiceStatus = invoice.InvoiceStatus,
        StatusCode = invoice.NfeStatusCode,
        Reason = invoice.NfeStatusReason,
        AccessKey = invoice.ChaveNFe,
        ConfirmationError = invoice.NfeConfirmationError,
    };
}
```

```csharp
// SiagroB1.Application/Services/Nfe/SalesInvoiceNfeResultHandler.cs
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Aplica no documento o retorno da SEFAZ (spec §9.2 passo 6 e §9.3) — o mesmo para emissão e
/// consulta. Autorizada: grava o procNFe e a situação, SALVA, e só então confirma o documento.
/// </summary>
public class SalesInvoiceNfeResultHandler(IUnitOfWork db, SalesInvoicesConfirmService confirm)
{
    public Task<NfeIssueOutcomeDto> ApplyAuthorizationAsync(
        SalesInvoice invoice, string signedXml, NfeSefazResult result, string userName) =>
        ApplyAsync(invoice, signedXml, result, userName, fromConsult: false);

    public Task<NfeIssueOutcomeDto> ApplyConsultAsync(
        SalesInvoice invoice, string signedXml, NfeSefazResult result, string userName) =>
        ApplyAsync(invoice, signedXml, result, userName, fromConsult: true);

    /// <summary>
    /// Confirma o documento da NF-e autorizada. Falhou: a NF-e (já gravada) é a verdade; o erro
    /// vai para <c>NfeConfirmationError</c> e "Concluir confirmação" tenta de novo.
    /// </summary>
    public async Task<NfeIssueOutcomeDto> ConfirmAsync(Guid key, string userName)
    {
        try
        {
            await confirm.ExecuteAsync(key, userName);
        }
        catch (Exception e)
        {
            // ⚠️ A confirmação mexe em entidades rastreadas antes de falhar, e o rollback da
            // transação dela não desfaz o rastreador: salvar por cima gravaria a confirmação pela
            // metade. Descarta tudo e grava só o erro, relendo o documento.
            db.Context.ChangeTracker.Clear();

            var failed = await db.Context.SalesInvoices.FirstAsync(i => i.Key == key);
            failed.NfeConfirmationError = Truncate(e.Message);
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(failed);
        }

        var confirmed = await db.Context.SalesInvoices.FirstAsync(i => i.Key == key);
        if (confirmed.NfeConfirmationError is not null)
        {
            confirmed.NfeConfirmationError = null;
            await db.SaveChangesAsync();
        }

        return NfeIssueOutcomeDto.From(confirmed);
    }

    private async Task<NfeIssueOutcomeDto> ApplyAsync(
        SalesInvoice invoice, string signedXml, NfeSefazResult result, string userName, bool fromConsult)
    {
        invoice.NfeStatusCode = result.StatusCode.ToString(CultureInfo.InvariantCulture);
        invoice.NfeStatusReason = Truncate(result.Reason);

        if (NfeStatusCodes.IsAuthorized(result.StatusCode) && result.ProtocolXml is not null)
        {
            db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
            {
                Key = Guid.NewGuid(),
                SalesInvoiceKey = invoice.Key,
                Kind = SalesInvoiceNfeXmlKind.Authorized,
                Xml = NfeProcComposer.Compose(signedXml, result.ProtocolXml),
                CreatedAt = DateTime.Now,
            });

            invoice.NfeStatus = NfeStatus.Authorized;
            invoice.NfeProtocol = result.Protocol;
            invoice.NfeAuthorizedAt = (result.ReceivedAt ?? DateTimeOffset.Now).DateTime;
            invoice.NfeConfirmationError = null;

            // A NF-e autorizada vai para o banco ANTES da confirmação: se ela falhar, a nota
            // continua registrada como autorizada.
            await db.SaveChangesAsync();

            return await ConfirmAsync(invoice.Key, userName);
        }

        if (NfeStatusCodes.IsDenied(result.StatusCode))
        {
            invoice.NfeStatus = NfeStatus.Denied;
            invoice.NfeProtocol = result.Protocol;
        }
        else if (!fromConsult || result.StatusCode == NfeStatusCodes.NotFound)
        {
            // Rejeitada (ou "não consta na base" na consulta): pode corrigir e reenviar.
            invoice.NfeStatus = NfeStatus.Rejected;
        }

        // Consulta com outro retorno: segue em processamento, com o código e o motivo gravados.
        await db.SaveChangesAsync();

        return NfeIssueOutcomeDto.From(invoice);
    }

    internal static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
```

```csharp
// SiagroB1.Application/Services/Nfe/SalesInvoicesNfeIssueService.cs
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Emitir NF-e" (spec §9.2). Na filial com a regra ativa, emitir é o que confirma o documento.
/// Ordem que não pode mudar: reservar o número e SALVAR; assinar e validar; gravar
/// "Em processamento" + XML assinado e SALVAR; só então enviar.
/// </summary>
public class SalesInvoicesNfeIssueService(
    IUnitOfWork db,
    TaxCalculationGate gate,
    NfeReadinessValidator readiness,
    BranchNfeSettingsService settingsService,
    NfeNumberReservationService reservation,
    INfeSefazClient sefaz,
    SalesInvoiceNfeResultHandler resultHandler,
    NfeOptions options)
{
    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.SalesInvoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Key == key)
                      ?? throw new NotFoundException("Documento de saída não encontrado.");

        await EnsurePreconditionsAsync(invoice);

        var context = await readiness.ValidateAsync(invoice);

        using var service = await settingsService.OpenAsync(invoice.BranchCode!);

        await ReserveNumberAsync(invoice, context.Settings);

        var input = NfeIssueInputAssembler.Build(
            invoice, context, NfeIssueInputAssembler.BrasiliaNow(), options.TechnicalResponsible);

        SignedNfe signed;
        try
        {
            signed = NfeSigner.BuildSignAndValidate(input, service.Settings);
        }
        catch (NfeValidationException e)
        {
            invoice.NfeStatus = NfeStatus.Rejected;
            invoice.NfeStatusCode = null;
            invoice.NfeStatusReason = SalesInvoiceNfeResultHandler.Truncate(
                $"Rejeitada na validação local, nada foi enviado: {e.Message}");
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(invoice);
        }

        invoice.NfeStatus = NfeStatus.Processing;
        invoice.ChaveNFe = signed.AccessKey;
        invoice.NfeEnvironment = context.Settings.Environment;
        invoice.NfeStatusCode = null;
        invoice.NfeStatusReason = "Enviada à SEFAZ, aguardando o retorno.";
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(),
            SalesInvoiceKey = invoice.Key,
            Kind = SalesInvoiceNfeXmlKind.Signed,
            Xml = signed.Xml,
            CreatedAt = DateTime.Now,
        });
        await db.SaveChangesAsync();

        NfeSefazResult result;
        try
        {
            result = await sefaz.AuthorizeAsync(signed, service.Settings);

            if (NfeStatusCodes.IsDuplicate(result.StatusCode))
            {
                var consulted = await sefaz.ConsultProtocolAsync(signed.AccessKey, service.Settings);
                return await resultHandler.ApplyConsultAsync(invoice, signed.Xml, consulted, userName);
            }
        }
        catch (NfeCommunicationException)
        {
            invoice.NfeStatusReason = "Sem resposta da SEFAZ — use Consultar situação.";
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(invoice);
        }

        return await resultHandler.ApplyAuthorizationAsync(invoice, signed.Xml, result, userName);
    }

    private async Task EnsurePreconditionsAsync(SalesInvoice invoice)
    {
        if (!await gate.IsActiveAsync(invoice.BranchCode))
            throw new DefaultException($"A filial {invoice.BranchCode} não emite NF-e pelo Siagro.");

        if (invoice.InvoiceType != SalesInvoiceType.Normal)
            throw new DefaultException("Só o documento Normal é emitido como NF-e por aqui; a devolução fica para a próxima etapa.");

        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException("Só documento Pendente pode ser emitido.");

        switch (invoice.NfeStatus)
        {
            case NfeStatus.Processing:
                throw new DefaultException("A NF-e está em processamento na SEFAZ: use Consultar situação.");
            case NfeStatus.Authorized:
                throw new DefaultException("A NF-e deste documento já foi autorizada.");
            case NfeStatus.Denied:
                throw new DefaultException("A NF-e deste documento foi denegada: o número não pode ser reutilizado.");
        }

        if (invoice.Items.Count == 0)
            throw new DefaultException("O documento não tem itens.");

        // Documento de antes da chave, ou que perdeu o cálculo: a emissão não recalcula.
        var uncalculated = invoice.Items.FirstOrDefault(i =>
            string.IsNullOrWhiteSpace(i.Cfop) || string.IsNullOrWhiteSpace(i.Ncm) || string.IsNullOrWhiteSpace(i.CstIcms) ||
            string.IsNullOrWhiteSpace(i.CstPis) || string.IsNullOrWhiteSpace(i.CstCofins));

        if (uncalculated is not null)
            throw new DefaultException(
                $"O item {uncalculated.ItemCode} está sem os tributos calculados. Salve o documento para recalcular antes de emitir.");
    }

    /// <summary>
    /// Primeira tentativa: reserva o número (conexão própria, fora de transação) e gera o cNF. As
    /// seguintes reaproveitam os dois. Salva já: a retentativa depois de uma queda acha o número.
    /// </summary>
    private async Task ReserveNumberAsync(SalesInvoice invoice, BranchNfeSettings settings)
    {
        if (string.IsNullOrWhiteSpace(invoice.TaxDocumentNumber))
        {
            var number = await reservation.ReserveAsync(invoice.BranchCode!);
            invoice.TaxDocumentNumber = number.ToString("D9", CultureInfo.InvariantCulture);
            invoice.TaxDocumentSeries = settings.Series.ToString(CultureInfo.InvariantCulture);
        }

        invoice.NfeRandomCode ??= NewRandomCode(invoice.TaxDocumentNumber!);

        await db.SaveChangesAsync();
    }

    /// <summary>8 dígitos aleatórios, diferentes do número da nota (regra da SEFAZ).</summary>
    private static string NewRandomCode(string documentNumber)
    {
        var number = documentNumber.PadLeft(8, '0')[^8..];
        string code;

        do
        {
            code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", CultureInfo.InvariantCulture);
        } while (code == number);

        return code;
    }
}
```

```bash
git add SiagroB1.Domain/Dtos/Nfe/NfeIssueOutcomeDto.cs SiagroB1.Application/Services/Nfe/SalesInvoiceNfeResultHandler.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeIssueService.cs
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~SalesInvoicesNfeIssueServiceTests"`
Expected: PASS (12 testes).

- [ ] **Step 6: Action, EDM e DI**

```csharp
// SiagroB1.Web/Actions/Nfe/SalesInvoicesIssueNfeController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

/// <summary>
/// "Emitir NF-e". Rejeição, denegação e falta de resposta voltam 200 com o desfecho; pré-condição
/// e prontidão voltam 400 com a mensagem.
/// </summary>
public class SalesInvoicesIssueNfeController(SalesInvoicesNfeIssueService service) : ODataController
{
    [HttpPost("odata/SalesInvoicesIssueNfe")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        try
        {
            return Ok(await service.ExecuteAsync(key, User.Identity?.Name ?? "Unknown"));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

Em `ODataConfigurations.cs`, depois das actions da configuração da NF-e:

```csharp
        var salesInvoicesIssueNfe = modelBuilder.Action("SalesInvoicesIssueNfe");
        salesInvoicesIssueNfe.Parameter<Guid>("Key");
        salesInvoicesIssueNfe.Returns<NfeIssueOutcomeDto>();
```

DI (junto dos serviços da NF-e):

```csharp
        services.AddScoped<SalesInvoiceNfeResultHandler>();
        services.AddScoped<SalesInvoicesNfeIssueService>();
```

```bash
git add SiagroB1.Web/Actions/Nfe/SalesInvoicesIssueNfeController.cs
```

- [ ] **Step 7: Build e suíte**

Run: `dotnet build SiagroB1.sln 2>&1 | grep -E " error |Build succeeded" | sort -u` e `dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Build succeeded` e `Passed!`.

- [ ] **Step 8: Commit**

```bash
git commit -m "feat(invoice): emitir a NF-e do documento de saída e confirmar quando autorizada" -m "Na filial com a regra ativa, emitir é o que confirma. A ordem garante que nada se perde: número reservado e salvo, XML assinado e validado no XSD, Em processamento + XML assinado gravados antes do envio. Autorizada: procNFe e protocolo gravados, depois a confirmação. Rejeitada: segue Pendente com o número. Denegada: número queimado. Duplicidade: consulta. Sem resposta: fica em processamento.

Atenção: se a confirmação falhar depois da autorização, o rastreador é limpo antes de gravar o erro — a confirmação mexe em entidades antes de estourar e o rollback dela não desfaz o rastreador.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Domain/Dtos/Nfe SiagroB1.Application SiagroB1.Web SiagroB1.Application.Tests
```

---

### Task 15: Consultar situação, concluir confirmação, trava da confirmação direta e travas de edição

**Files:**
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeConsultService.cs`, `SalesInvoicesNfeCompleteConfirmationService.cs`
- Create: `SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs`
- Create: `SiagroB1.Web/Actions/Nfe/SalesInvoicesConsultNfeController.cs`, `SalesInvoicesCompleteNfeConfirmationController.cs`
- Modify: `SiagroB1.Application/Services/SalesInvoices/SalesInvoicesConfirmService.cs`, `SalesInvoicesCreateService.cs`, `SalesInvoicesUpdateService.cs`, `SalesInvoicesItemsCreateService.cs`, `SalesInvoicesItemsUpdateService.cs`, `SalesInvoicesItemsDeleteService.cs`, `SalesInvoicesDeleteService.cs`, `SalesInvoicesCancelService.cs`, `SalesInvoicesSetDocumentNumberService.cs`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeConsultServiceTests.cs`, `SalesInvoicesNfeCompleteConfirmationServiceTests.cs`, `SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesConfirmNfeGuardTests.cs`, `SalesInvoiceNfeLockTests.cs`

**Interfaces:**
- Consumes: `SalesInvoiceNfeResultHandler`, `BranchNfeSettingsService.OpenAsync(string, NfeEnvironment?)`, `INfeSefazClient`, `NfeIssueOutcomeDto` (Tasks 12–14).
- Produces:
  - `SalesInvoicesNfeConsultService.ExecuteAsync(Guid, string) : Task<NfeIssueOutcomeDto>`; `SalesInvoicesNfeCompleteConfirmationService.ExecuteAsync(Guid, string) : Task<NfeIssueOutcomeDto>`.
  - Actions `SalesInvoicesConsultNfe(Key)` e `SalesInvoicesCompleteNfeConfirmation(Key)` → `NfeIssueOutcomeDto`.
  - `SalesInvoiceNfeLock`: `EnsureHeaderEditable(EntityEntry<SalesInvoice>)`, `RestoreIssuanceFields(EntityEntry<SalesInvoice>)`, `ResetIssuanceFields(SalesInvoice)`, `EnsureItemEditable(NfeStatus, EntityEntry<SalesInvoiceItem>)`, `EnsureLinesChangeable(NfeStatus)`, `EnsureDeletable(SalesInvoice)`, `EnsureCancellable(SalesInvoice)`, `EnsureManualTaxDocument(SalesInvoice)`.
  - `SalesInvoicesConfirmService(..., IStringLocalizer<Resource> resource, TaxCalculationGate? gate = null)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeConsultServiceTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Consultar situação" (spec §9.3): o caminho de saída do "sem resposta".</summary>
public class SalesInvoicesNfeConsultServiceTests
{
    private static (SalesInvoicesNfeIssueService Issue, SalesInvoicesNfeConsultService Consult) Services(
        NfeScenario scenario, FakeNfeSefazClient sefaz, SalesInvoicesConfirmService confirm)
    {
        var config = NfeTestSeed.Config();
        var options = new NfeOptions(config);
        var settings = new BranchNfeSettingsService(scenario.Db, options, sefaz);
        var handler = new SalesInvoiceNfeResultHandler(scenario.Db, confirm);

        return (
            new SalesInvoicesNfeIssueService(scenario.Db, new TaxCalculationGate(scenario.Db, config),
                new NfeReadinessValidator(scenario.Db, options), settings, new FakeNfeNumberReservationService(), sefaz, handler, options),
            new SalesInvoicesNfeConsultService(scenario.Db, settings, sefaz, handler));
    }

    /// <summary>Emite sem resposta: o documento fica em processamento com o XML assinado gravado.</summary>
    private static async Task<(FakeNfeSefazClient Sefaz, RecordingConfirmService Confirm, SalesInvoicesNfeConsultService Consult)> ProcessingAsync(NfeScenario scenario)
    {
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(FakeNfeSefazClient.NoResponse);
        var confirm = new RecordingConfirmService(scenario.Db);
        var (issue, consult) = Services(scenario, sefaz, confirm);
        await issue.ExecuteAsync(scenario.InvoiceKey, "tester");
        return (sefaz, confirm, consult);
    }

    [Fact]
    public async Task Consult_after_no_response_authorizes_from_the_saved_signed_xml()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var (sefaz, confirm, consult) = await ProcessingAsync(scenario);
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await consult.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Equal(1, confirm.Calls);
        var signed = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().SingleAsync(x => x.Kind == SalesInvoiceNfeXmlKind.Signed);
        var proc = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().SingleAsync(x => x.Kind == SalesInvoiceNfeXmlKind.Authorized);
        Assert.Contains(signed.Xml[signed.Xml.IndexOf("<NFe", StringComparison.Ordinal)..].Trim(), proc.Xml);
    }

    [Fact]
    public async Task Not_found_at_sefaz_becomes_rejected_and_can_be_resent()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var (sefaz, _, consult) = await ProcessingAsync(scenario);
        sefaz.ConsultResponses.Enqueue(_ => new NfeSefazResult(217, "Rejeição: NF-e não consta na base de dados da SEFAZ"));

        var outcome = await consult.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Rejected, outcome.NfeStatus);
    }

    [Fact]
    public async Task Other_answers_keep_processing_with_the_reason()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var (sefaz, _, consult) = await ProcessingAsync(scenario);
        sefaz.ConsultResponses.Enqueue(_ => new NfeSefazResult(656, "Rejeição: Consumo Indevido"));

        var outcome = await consult.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Processing, outcome.NfeStatus);
        Assert.Equal("656", outcome.StatusCode);
    }

    [Fact]
    public async Task Only_processing_documents_are_consulted()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var (_, consult) = Services(scenario, new FakeNfeSefazClient(), new RecordingConfirmService(scenario.Db));

        await Assert.ThrowsAsync<DefaultException>(() => consult.ExecuteAsync(scenario.InvoiceKey, "tester"));
    }
}
```

```csharp
// SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeCompleteConfirmationServiceTests.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Concluir confirmação": NF-e autorizada cuja confirmação falhou (spec §9.2 passo 7).</summary>
public class SalesInvoicesNfeCompleteConfirmationServiceTests
{
    private static async Task<NfeScenario> AuthorizedWithErrorAsync()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync();
        invoice.NfeStatus = NfeStatus.Authorized;
        invoice.NfeConfirmationError = "Liberação de entrega sem saldo.";
        await scenario.Db.SaveChangesAsync();
        return scenario;
    }

    [Fact]
    public async Task Success_confirms_and_clears_the_error()
    {
        var scenario = await AuthorizedWithErrorAsync();
        var confirm = new RecordingConfirmService(scenario.Db);

        var outcome = await new SalesInvoicesNfeCompleteConfirmationService(
            scenario.Db, new SalesInvoiceNfeResultHandler(scenario.Db, confirm)).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, outcome.InvoiceStatus);
        Assert.Null(outcome.ConfirmationError);
    }

    [Fact]
    public async Task New_failure_replaces_the_error()
    {
        var scenario = await AuthorizedWithErrorAsync();
        var confirm = new RecordingConfirmService(scenario.Db, failWith: new DefaultException("Contrato encerrado."));

        var outcome = await new SalesInvoicesNfeCompleteConfirmationService(
            scenario.Db, new SalesInvoiceNfeResultHandler(scenario.Db, confirm)).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal("Contrato encerrado.", outcome.ConfirmationError);
        Assert.Equal(InvoiceStatus.Pending, outcome.InvoiceStatus);
    }

    [Fact]
    public async Task Without_authorized_nfe_it_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(() => new SalesInvoicesNfeCompleteConfirmationService(
                scenario.Db, new SalesInvoiceNfeResultHandler(scenario.Db, new RecordingConfirmService(scenario.Db)))
            .ExecuteAsync(scenario.InvoiceKey, "tester"));
    }
}
```

```csharp
// SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesConfirmNfeGuardTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Com a regra ativa, o documento Normal só confirma com a NF-e autorizada (é a emissão que chama
/// a confirmação). SAPB1 e STANDALONE sem a chave confirmam como sempre.
/// </summary>
public class SalesInvoicesConfirmNfeGuardTests
{
    private static SalesInvoicesConfirmService Confirm(UnitOfWork db, string erp)
    {
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);

        return new SalesInvoicesConfirmService(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationCreateService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesContractsAllocationCreateForReturnService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesInvoicesUsageGuardService(usages),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(db, new SalesContractsFixedVolumeService(db.Context)),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>(),
            TaxTestServices.Gate(db, erp));
    }

    /// <summary>Documento AVULSO de uma linha, natureza sem efeito no contrato, filial com a chave.</summary>
    private static async Task<(UnitOfWork Db, SalesInvoice Invoice)> SeedAsync(bool issuesNfe = true, NfeStatus nfe = NfeStatus.None)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "CEAGUI", StateCode = "SP", TaxRegime = TaxRegime.Normal, IssuesNfe = issuesNfe });
        await db.SaveChangesAsync();

        var usage = await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Venda", CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102", RequiresQuantity = false,
        });

        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000m);
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Pending);
        invoice.BranchCode = "01";
        invoice.NfeStatus = nfe;
        SalesContractsAllocationTestSupport.NewItem(invoice, contract.Key, releaseKey: null, 100m).UsageCode = usage.Code;

        db.Context.SalesContracts.Add(contract);
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return (db, invoice);
    }

    private static async Task<InvoiceStatus?> StatusAsync(UnitOfWork db, Guid key) =>
        (await db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == key)).InvoiceStatus;

    [Fact]
    public async Task Rule_active_refuses_direct_confirmation()
    {
        var (db, invoice) = await SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.", ex.Message);
    }

    [Fact]
    public async Task Rule_active_confirms_with_authorized_nfe()
    {
        var (db, invoice) = await SeedAsync(nfe: NfeStatus.Authorized);

        await Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Sapb1_with_the_flag_in_the_database_confirms_as_today()
    {
        var (db, invoice) = await SeedAsync();

        await Confirm(db, "SAPB1").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }

    [Fact]
    public async Task Standalone_without_the_flag_confirms_as_today()
    {
        var (db, invoice) = await SeedAsync(issuesNfe: false);

        await Confirm(db, "STANDALONE").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, await StatusAsync(db, invoice.Key));
    }
}
```

```csharp
// SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceNfeLockTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Travas do documento com NF-e (spec §9.4 + decisão P11 do plano). Em processamento nada muda; com
/// a NF-e autorizada, o que foi ao XML não muda (inclusive depois de estornar a confirmação), e o
/// cancelamento espera o cancelamento na SEFAZ (2b). Os campos da NF-e só a emissão escreve.
/// </summary>
public class SalesInvoiceNfeLockTests
{
    private static async Task<(UnitOfWork Db, SalesInvoice Invoice)> SeedAsync(
        NfeStatus nfe, InvoiceStatus status = InvoiceStatus.Pending)
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", BranchCode = "01", InvoiceStatus = status, NfeStatus = nfe,
            InvoiceDate = new DateTime(2026, 10, 2), Comments = "antes",
            Items =
            [
                new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m },
            ],
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (db, invoice);
    }

    private static SalesInvoicesUpdateService HeaderUpdate(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(names: new() { ["C1"] = "CLIENTE", ["C2"] = "OUTRO" }),
            TaxTestServices.InactiveApply(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    private static SalesInvoicesItemsUpdateService ItemUpdate(UnitOfWork db) =>
        new(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            TaxTestServices.InactiveApply(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    [Fact]
    public async Task Processing_document_header_cannot_be_edited()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Processing);
        invoice.Comments = "depois";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Contains("em processamento", ex.Message);
    }

    [Fact]
    public async Task Authorized_document_cannot_change_the_customer()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        invoice.CardCode = "C2";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Contains("autorizada", ex.Message);
    }

    [Fact]
    public async Task Authorized_document_can_change_internal_comments()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        invoice.Comments = "depois";

        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        Assert.Equal("depois", (await db.Context.SalesInvoices.AsNoTracking().SingleAsync()).Comments);
    }

    [Fact]
    public async Task Patch_cannot_write_the_issuance_fields()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        invoice.NfeStatus = NfeStatus.Authorized;
        invoice.NfeProtocol = "123";

        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        var saved = await db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.None, saved.NfeStatus);
        Assert.Null(saved.NfeProtocol);
    }

    [Fact]
    public void Create_ignores_issuance_fields_in_the_body()
    {
        var invoice = new SalesInvoice
        {
            CardCode = "C1", NfeStatus = NfeStatus.Authorized, NfeProtocol = "1", NfeRandomCode = "12345678",
            NfeConfirmationError = "x",
        };

        SalesInvoiceNfeLock.ResetIssuanceFields(invoice);

        Assert.Equal(NfeStatus.None, invoice.NfeStatus);
        Assert.Null(invoice.NfeProtocol);
        Assert.Null(invoice.NfeRandomCode);
        Assert.Null(invoice.NfeConfirmationError);
    }

    [Fact]
    public async Task Authorized_line_cannot_change_quantity()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.Quantity = 20m;

        await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate(db).ExecuteAsync(item.Key!.Value, item, "tester"));
    }

    [Fact]
    public async Task Authorized_line_accepts_the_delivery_reconciliation()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.DeliveredQuantity = 9m;

        await ItemUpdate(db).ExecuteAsync(item.Key!.Value, item, "tester");

        Assert.Equal(9m, (await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync()).DeliveredQuantity);
    }

    [Fact]
    public async Task Line_cannot_be_added_to_an_authorized_document()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);

        await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesItemsCreateService(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
                    TaxTestServices.InactiveApply(db), NullLogger<SalesInvoicesItemsCreateService>.Instance)
                .ExecuteAsync(new SalesInvoiceItem
                {
                    Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m,
                }, "tester"));
    }

    [Fact]
    public async Task Line_cannot_be_removed_while_processing()
    {
        var (db, _) = await SeedAsync(NfeStatus.Processing);
        var item = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesItemsDeleteService(db, NullLogger<SalesInvoicesItemsDeleteService>.Instance).ExecuteAsync(item.Key!.Value));
    }

    [Theory]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Authorized)]
    [InlineData(NfeStatus.Denied)]
    public async Task Document_with_nfe_cannot_be_deleted(NfeStatus nfe)
    {
        var (db, invoice) = await SeedAsync(nfe);

        await Assert.ThrowsAsync<DefaultException>(() => new SalesInvoicesDeleteService(db,
                new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
                NullLogger<SalesInvoicesDeleteService>.Instance)
            .ExecuteAsync(invoice.Key, "tester"));
    }

    [Fact]
    public async Task Rejected_document_can_be_deleted()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Rejected);

        var deleted = await new SalesInvoicesDeleteService(db,
                new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
                NullLogger<SalesInvoicesDeleteService>.Instance)
            .ExecuteAsync(invoice.Key, "tester");

        Assert.True(deleted);
    }

    [Fact]
    public async Task Document_with_authorized_nfe_cannot_be_cancelled()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => new SalesInvoicesCancelService(db,
                new SalesShipmentReleasesRecalculateShippedService(db.Context),
                new SalesContractsAllocationDeleteForInvoiceService(db),
                new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
                NullLogger<SalesInvoicesCancelService>.Instance)
            .ExecuteAsync(invoice.Key, "tester"));

        Assert.Contains("SEFAZ", ex.Message);
    }

    [Fact]
    public async Task Manual_tax_document_is_refused_after_issuance()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Rejected);

        await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesSetDocumentNumberService(db, new SalesInvoicesChangeLogService(db.Context))
                .ExecuteAsync(invoice.Key, "000000010", "1", null, "tester"));
    }
}
```

```bash
git add SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeConsultServiceTests.cs SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeCompleteConfirmationServiceTests.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoicesConfirmNfeGuardTests.cs SiagroB1.Application.Tests/SalesInvoices/SalesInvoiceNfeLockTests.cs
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeConsult|FullyQualifiedName~NfeCompleteConfirmation|FullyQualifiedName~ConfirmNfeGuard|FullyQualifiedName~SalesInvoiceNfeLock"`
Expected: FAIL de compilação — `SalesInvoicesNfeConsultService`, `SalesInvoiceNfeLock` e o parâmetro `gate` não existem.

- [ ] **Step 3: Consulta e conclusão**

```csharp
// SiagroB1.Application/Services/Nfe/SalesInvoicesNfeConsultService.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// "Consultar situação" (spec §9.3). Consulta pela chave, no ambiente da EMISSÃO, e monta o
/// procNFe a partir do XML assinado gravado antes do envio.
/// </summary>
public class SalesInvoicesNfeConsultService(
    IUnitOfWork db, BranchNfeSettingsService settingsService, INfeSefazClient sefaz, SalesInvoiceNfeResultHandler resultHandler)
{
    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.SalesInvoices.FirstOrDefaultAsync(i => i.Key == key)
                      ?? throw new NotFoundException("Documento de saída não encontrado.");

        if (invoice.NfeStatus != NfeStatus.Processing)
            throw new DefaultException("Só a NF-e em processamento é consultada.");

        var signedXml = await db.Context.SalesInvoiceNfeXmls.AsNoTracking()
                            .Where(x => x.SalesInvoiceKey == key && x.Kind == SalesInvoiceNfeXmlKind.Signed)
                            .OrderByDescending(x => x.CreatedAt)
                            .Select(x => x.Xml)
                            .FirstOrDefaultAsync()
                        ?? throw new DefaultException("O XML assinado deste documento não foi encontrado.");

        using var service = await settingsService.OpenAsync(invoice.BranchCode!, invoice.NfeEnvironment);

        NfeSefazResult result;
        try
        {
            result = await sefaz.ConsultProtocolAsync(invoice.ChaveNFe!, service.Settings);
        }
        catch (NfeCommunicationException)
        {
            invoice.NfeStatusReason = "Sem resposta da SEFAZ na consulta — tente de novo em instantes.";
            await db.SaveChangesAsync();

            return NfeIssueOutcomeDto.From(invoice);
        }

        return await resultHandler.ApplyConsultAsync(invoice, signedXml, result, userName);
    }
}
```

```csharp
// SiagroB1.Application/Services/Nfe/SalesInvoicesNfeCompleteConfirmationService.cs
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Concluir confirmação": refaz a confirmação de um documento com NF-e já autorizada.</summary>
public class SalesInvoicesNfeCompleteConfirmationService(IUnitOfWork db, SalesInvoiceNfeResultHandler resultHandler)
{
    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.SalesInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Key == key)
                      ?? throw new NotFoundException("Documento de saída não encontrado.");

        if (invoice.NfeStatus != NfeStatus.Authorized || invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException("Só documento com NF-e autorizada e ainda Pendente tem confirmação a concluir.");

        return await resultHandler.ConfirmAsync(key, userName);
    }
}
```

```bash
git add SiagroB1.Application/Services/Nfe/SalesInvoicesNfeConsultService.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeCompleteConfirmationService.cs
```

- [ ] **Step 4: As travas**

```csharp
// SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Travas do documento de saída com NF-e STANDALONE (spec §9.4 + decisão P11). Todas agem pelo
/// <see cref="SalesInvoice.NfeStatus"/>, que fica <c>None</c> para sempre na Yokotobi (SAPB1) e na
/// MH Agro (chave desligada) — por isso nenhuma delas muda o comportamento dessas bases.
/// </summary>
public static class SalesInvoiceNfeLock
{
    private const string ProcessingMessage =
        "A NF-e deste documento está em processamento na SEFAZ: aguarde e use Consultar situação.";

    private const string AuthorizedMessage =
        "A NF-e deste documento já foi autorizada: os dados que foram para a nota não podem mudar.";

    /// <summary>Campos do cabeçalho que vão para o XML.</summary>
    private static readonly string[] HeaderFiscalFields =
    [
        nameof(SalesInvoice.BranchCode), nameof(SalesInvoice.CardCode), nameof(SalesInvoice.InvoiceDate),
        nameof(SalesInvoice.InvoiceType), nameof(SalesInvoice.DeliveryCardCode), nameof(SalesInvoice.TruckingCompanyCode),
        nameof(SalesInvoice.TruckCode), nameof(SalesInvoice.FreightTerms), nameof(SalesInvoice.PaymentConditionCode),
        nameof(SalesInvoice.GrossWeight), nameof(SalesInvoice.NetWeight), nameof(SalesInvoice.TaxPayerComments),
        nameof(SalesInvoice.TaxComments),
    ];

    /// <summary>Campos da linha que vão para o XML (a Conferência de entregas mexe em outros).</summary>
    private static readonly string[] ItemFiscalFields =
    [
        nameof(SalesInvoiceItem.ItemCode), nameof(SalesInvoiceItem.UnitOfMeasureCode), nameof(SalesInvoiceItem.Quantity),
        nameof(SalesInvoiceItem.UnitPrice), nameof(SalesInvoiceItem.UsageCode),
    ];

    /// <summary>Campos que só a emissão escreve.</summary>
    private static readonly string[] IssuanceFields =
    [
        nameof(SalesInvoice.NfeStatus), nameof(SalesInvoice.NfeEnvironment), nameof(SalesInvoice.NfeRandomCode),
        nameof(SalesInvoice.NfeProtocol), nameof(SalesInvoice.NfeAuthorizedAt), nameof(SalesInvoice.NfeStatusCode),
        nameof(SalesInvoice.NfeStatusReason), nameof(SalesInvoice.NfeConfirmationError),
    ];

    /// <summary>Número, série e chave: depois da primeira emissão, também só a emissão escreve.</summary>
    private static readonly string[] TaxDocumentFields =
    [
        nameof(SalesInvoice.TaxDocumentNumber), nameof(SalesInvoice.TaxDocumentSeries), nameof(SalesInvoice.ChaveNFe),
    ];

    /// <summary>Chamado DEPOIS do SetValues: compara o gravado com o que chegou.</summary>
    public static void EnsureHeaderEditable(EntityEntry<SalesInvoice> entry)
    {
        var status = (NfeStatus)entry.OriginalValues[nameof(SalesInvoice.NfeStatus)]!;

        if (status == NfeStatus.Processing)
            throw new DefaultException(ProcessingMessage);

        if (status == NfeStatus.Authorized && AnyChanged(entry, HeaderFiscalFields))
            throw new DefaultException(AuthorizedMessage);
    }

    /// <summary>O PATCH/PUT não escreve situação, protocolo, retorno — nem número/série/chave depois de emitir.</summary>
    public static void RestoreIssuanceFields(EntityEntry<SalesInvoice> entry)
    {
        var emitted = (NfeStatus)entry.OriginalValues[nameof(SalesInvoice.NfeStatus)]! != NfeStatus.None;
        var fields = emitted ? IssuanceFields.Concat(TaxDocumentFields) : IssuanceFields;

        foreach (var field in fields)
            entry.Property(field).CurrentValue = entry.OriginalValues[field];
    }

    /// <summary>A criação nunca nasce emitida, venha o que vier no corpo.</summary>
    public static void ResetIssuanceFields(SalesInvoice invoice)
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

    /// <summary>Chamado DEPOIS do SetValues da linha.</summary>
    public static void EnsureItemEditable(NfeStatus invoiceStatus, EntityEntry<SalesInvoiceItem> entry)
    {
        if (invoiceStatus == NfeStatus.Processing)
            throw new DefaultException(ProcessingMessage);

        if (invoiceStatus == NfeStatus.Authorized && AnyChanged(entry, ItemFiscalFields))
            throw new DefaultException(AuthorizedMessage);
    }

    /// <summary>Incluir ou excluir linha.</summary>
    public static void EnsureLinesChangeable(NfeStatus invoiceStatus)
    {
        if (invoiceStatus == NfeStatus.Processing)
            throw new DefaultException(ProcessingMessage);

        if (invoiceStatus == NfeStatus.Authorized)
            throw new DefaultException(AuthorizedMessage);
    }

    public static void EnsureDeletable(SalesInvoice invoice)
    {
        if (invoice.NfeStatus is NfeStatus.Processing or NfeStatus.Authorized or NfeStatus.Denied)
            throw new DefaultException(
                "Documento com NF-e em processamento, autorizada ou denegada não pode ser excluído.");
    }

    public static void EnsureCancellable(SalesInvoice invoice)
    {
        if (invoice.NfeStatus == NfeStatus.Processing)
            throw new DefaultException(ProcessingMessage);

        if (invoice.NfeStatus == NfeStatus.Authorized)
            throw new DefaultException(
                "A NF-e deste documento está autorizada: o cancelamento precisa ser feito na SEFAZ, recurso da próxima etapa.");
    }

    public static void EnsureManualTaxDocument(SalesInvoice invoice)
    {
        if (invoice.NfeStatus != NfeStatus.None)
            throw new DefaultException("Número, série e chave deste documento vêm da emissão da NF-e pelo Siagro.");
    }

    private static bool AnyChanged(EntityEntry entry, IEnumerable<string> properties) =>
        properties.Any(p => !Equals(entry.OriginalValues[p], entry.CurrentValues[p]));
}
```

```bash
git add SiagroB1.Application/Services/SalesInvoices/SalesInvoiceNfeLock.cs
```

- [ ] **Step 5: Ligar as travas e a guarda nos serviços existentes**

1. `SalesInvoicesConfirmService.cs` — último parâmetro do construtor primário e a guarda:

```csharp
    IStringLocalizer<Resource> resource,
    TaxCalculationGate? gate = null)
```

(acrescente `using SiagroB1.Application.Services.Taxes;`). Logo depois do `if (invoice.InvoiceStatus != InvoiceStatus.Pending) { ... }`:

```csharp
        // NF-e STANDALONE: na filial com a regra ativa, o documento Normal só confirma com a NF-e
        // autorizada — é a emissão que chama esta confirmação. Devolução segue como sempre. Sem o
        // gate (os testes antigos constroem o serviço sem ele) a regra fica inativa.
        if (gate is not null &&
            invoice.InvoiceType == SalesInvoiceType.Normal &&
            invoice.NfeStatus != NfeStatus.Authorized &&
            await gate.IsActiveAsync(invoice.BranchCode))
        {
            throw new DefaultException("Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e.");
        }
```

2. `SalesInvoicesCreateService.cs` — no começo do `ExecuteAsync`, depois do `if (salesInvoice.Items.Count == 0) ...`:

```csharp
        // Os campos da NF-e só a emissão escreve: um corpo com "Autorizada" passaria pela guarda
        // da confirmação direta.
        SalesInvoiceNfeLock.ResetIssuanceFields(salesInvoice);
```

3. `SalesInvoicesUpdateService.cs` — troque a linha `db.Context.Entry(existingEntity).CurrentValues.SetValues(entity);` por:

```csharp
            var entry = db.Context.Entry(existingEntity);
            entry.CurrentValues.SetValues(entity);

            SalesInvoiceNfeLock.EnsureHeaderEditable(entry);
            SalesInvoiceNfeLock.RestoreIssuanceFields(entry);
```

4. `SalesInvoicesItemsCreateService.cs` — dentro do `if (invoice is not null) {`, antes do `ApplyAsync`:

```csharp
            SalesInvoiceNfeLock.EnsureLinesChangeable(invoice.NfeStatus);
```

5. `SalesInvoicesItemsUpdateService.cs` — logo depois de `db.Context.Entry(existingEntity).CurrentValues.SetValues(entity);`:

```csharp
            var invoiceNfeStatus = await db.Context.SalesInvoices.AsNoTracking()
                .Where(i => i.Key == existingEntity.SalesInvoiceKey)
                .Select(i => i.NfeStatus)
                .FirstOrDefaultAsync();
            SalesInvoiceNfeLock.EnsureItemEditable(invoiceNfeStatus, db.Context.Entry(existingEntity));
```

6. `SalesInvoicesItemsDeleteService.cs` — depois de `var salesInvoiceKey = entity.SalesInvoiceKey;`:

```csharp
            SalesInvoiceNfeLock.EnsureLinesChangeable(await db.Context.SalesInvoices.AsNoTracking()
                .Where(i => i.Key == salesInvoiceKey)
                .Select(i => i.NfeStatus)
                .FirstOrDefaultAsync());
```

7. `SalesInvoicesDeleteService.cs` — no `preDeleteAction`, depois do teste de `Pending`:

```csharp
            SalesInvoiceNfeLock.EnsureDeletable(entity);
```

8. `SalesInvoicesCancelService.cs` — depois do teste de "já está cancelado":

```csharp
        SalesInvoiceNfeLock.EnsureCancellable(existingInvoice);
```

9. `SalesInvoicesSetDocumentNumberService.cs` — logo depois de carregar o `invoice`:

```csharp
        SalesInvoiceNfeLock.EnsureManualTaxDocument(invoice);
```

Acrescente `using SiagroB1.Domain.Enums;`/`using SiagroB1.Domain.Exceptions;` onde faltar.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~NfeConsult|FullyQualifiedName~NfeCompleteConfirmation|FullyQualifiedName~ConfirmNfeGuard|FullyQualifiedName~SalesInvoiceNfeLock"`
Expected: PASS.

- [ ] **Step 7: Actions, EDM e DI**

```csharp
// SiagroB1.Web/Actions/Nfe/SalesInvoicesConsultNfeController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

public class SalesInvoicesConsultNfeController(SalesInvoicesNfeConsultService service) : ODataController
{
    [HttpPost("odata/SalesInvoicesConsultNfe")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        try
        {
            return Ok(await service.ExecuteAsync(key, User.Identity?.Name ?? "Unknown"));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

```csharp
// SiagroB1.Web/Actions/Nfe/SalesInvoicesCompleteNfeConfirmationController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.Nfe;

public class SalesInvoicesCompleteNfeConfirmationController(SalesInvoicesNfeCompleteConfirmationService service) : ODataController
{
    [HttpPost("odata/SalesInvoicesCompleteNfeConfirmation")]
    public async Task<IActionResult> PostAsync([FromBody] ODataActionParameters parameters)
    {
        if (parameters is null || !parameters.TryGetValue("Key", out var keyObj) || !Guid.TryParse(keyObj?.ToString(), out var key))
            return BadRequest("Parâmetro obrigatório: Key.");

        try
        {
            return Ok(await service.ExecuteAsync(key, User.Identity?.Name ?? "Unknown"));
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

`ODataConfigurations.cs`:

```csharp
        var salesInvoicesConsultNfe = modelBuilder.Action("SalesInvoicesConsultNfe");
        salesInvoicesConsultNfe.Parameter<Guid>("Key");
        salesInvoicesConsultNfe.Returns<NfeIssueOutcomeDto>();

        var salesInvoicesCompleteNfeConfirmation = modelBuilder.Action("SalesInvoicesCompleteNfeConfirmation");
        salesInvoicesCompleteNfeConfirmation.Parameter<Guid>("Key");
        salesInvoicesCompleteNfeConfirmation.Returns<NfeIssueOutcomeDto>();
```

DI:

```csharp
        services.AddScoped<SalesInvoicesNfeConsultService>();
        services.AddScoped<SalesInvoicesNfeCompleteConfirmationService>();
```

O `SalesInvoicesConfirmService` continua registrado como está: o contêiner injeta o `TaxCalculationGate` (já registrado) no parâmetro opcional.

```bash
git add SiagroB1.Web/Actions/Nfe/SalesInvoicesConsultNfeController.cs SiagroB1.Web/Actions/Nfe/SalesInvoicesCompleteNfeConfirmationController.cs
```

- [ ] **Step 8: Suíte completa**

Run: `dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Passed!` — inclusive os testes antigos de confirmação, edição, exclusão, cancelamento e "Informar Nota Fiscal" (nos documentos deles `NfeStatus` é `None`).

- [ ] **Step 9: Commit**

```bash
git commit -m "feat(invoice): consultar a NF-e, concluir a confirmação e travar o documento emitido" -m "Consultar situação resolve o sem resposta a partir do XML assinado gravado, no ambiente da emissão. Concluir confirmação refaz a confirmação de uma NF-e já autorizada. Com a regra ativa, a confirmação direta de documento Normal é recusada. Em processamento nada se edita; autorizada, o que foi ao XML não muda (nem depois de estornar a confirmação), o documento não se exclui nem se cancela sem a SEFAZ, e número/série/chave não se informam à mão. Os campos da NF-e só a emissão escreve.

Atenção: toda trava age pelo NfeStatus, que é None para sempre na Yokotobi e na MH Agro.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Application SiagroB1.Web SiagroB1.Application.Tests
```

---

### Task 16: DANFE no Reports e download do XML

**Files:**
- Create: `SiagroB1.Reports/ThirdParty/Zeus-LGPL/**` (arquivos da Zeus, sem alteração) + `SiagroB1.Reports/ThirdParty/Zeus-LGPL/README.md`
- Create: `SiagroB1.Reports/Services/DanfeReportService.cs`, `SiagroB1.Reports/Controllers/DanfeController.cs`
- Create: `SiagroB1.Application/Services/Nfe/SalesInvoicesNfeXmlDownloadService.cs`, `SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeXmlController.cs`
- Modify: `SiagroB1.Reports/SiagroB1.Reports.csproj`, `SiagroB1.Reports/Services/ReportHeaderService.cs`, `SiagroB1.Application.Tests/SiagroB1.Application.Tests.csproj`, `SiagroB1.Web/ODataConfig/ODataConfigurations.cs`, `SiagroB1.Web/Extensions/ServiceCollectionExtensions.cs`
- Test: `SiagroB1.Application.Tests/Reports/DanfeReportServiceTests.cs`, `SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeXmlDownloadServiceTests.cs`

**Interfaces:**
- Consumes: `SalesInvoiceNfeXml` (Task 13); `NfeSigner`, `NfeProcComposer` (Task 10); `NfeTestData` (link), `TestCertificates` (link), `FakeNfeSefazClient.Authorized`.
- Produces: `POST /reports/Danfe/{key}/print` → PDF; função `SalesInvoicesNfeXml(Key)` → `<chave>-procNFe.xml`; `ReportHeaderService.LogoBytes()`.

- [ ] **Step 1: Trazer os arquivos do DANFE (LGPL) sem alteração**

```bash
SHA=$(curl -fsSL https://api.github.com/repos/ZeusAutomacao/DFe.NET/commits/master | grep -m1 '"sha"' | sed 's/.*"sha": "\([0-9a-f]*\)".*/\1/')
echo "$SHA"
RAW="https://raw.githubusercontent.com/ZeusAutomacao/DFe.NET/$SHA"
DEST=SiagroB1.Reports/ThirdParty/Zeus-LGPL
for f in \
  NFe.Danfe.OpenFast/DanfeOpenFastBase.cs \
  NFe.Danfe.OpenFast/NFe/DanfeFrNfe.cs \
  Shared.NFe.Danfe/DanfeSharedHelper.cs \
  NFe.Danfe.Base/ConfiguracaoDanfe.cs \
  NFe.Danfe.Base/Enumns.cs \
  NFe.Danfe.Base/NFe/ConfiguracaoDanfeNfe.cs \
  NFe.Danfe.Base/NFe/ConfiguracaoDanfeNfeSimplificadoTipo2.cs \
  NFe.Danfe.Base/NFCe/ConfiguracaoDanfeNfce.cs \
  NFe.Danfe.Base/NFe/NFeRetrato.frx; do
  mkdir -p "$DEST/$(dirname "$f")"
  curl -fsSL "$RAW/$f" -o "$DEST/$f"
done
curl -fsSL "$RAW/LICENSE" -o "$DEST/LICENSE" || curl -fsSL "$RAW/LICENSE.txt" -o "$DEST/LICENSE"
find "$DEST" -type f | sort
```

Crie `SiagroB1.Reports/ThirdParty/Zeus-LGPL/README.md` (troque `<SHA>` pelo valor impresso acima):

```markdown
# DANFE da Zeus (DFe.NET) — LGPL-2.1

Arquivos copiados **sem alteração** de https://github.com/ZeusAutomacao/DFe.NET, commit `<SHA>`,
licença LGPL-2.1 (ver `LICENSE`). O layout `NFeRetrato.frx` e as classes do `NFe.Danfe.OpenFast`
não são publicados no NuGet, por isso estão aqui.

Regra: **não editar estes arquivos.** Qualquer alteração obriga a publicar o diff (LGPL). Para
atualizar, copie de novo do repositório e registre o novo commit acima.
```

Em `SiagroB1.Reports/SiagroB1.Reports.csproj`:

```xml
    <PropertyGroup>
        <!-- Liga os blocos #if do DANFE da Zeus (ThirdParty/Zeus-LGPL). -->
        <DefineConstants>$(DefineConstants);openfastreport</DefineConstants>
    </PropertyGroup>

    <ItemGroup>
      <ProjectReference Include="..\SiagroB1.Fiscal\SiagroB1.Fiscal.csproj" />
    </ItemGroup>

    <ItemGroup>
      <None Update="ThirdParty\Zeus-LGPL\**\*.frx">
        <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
      </None>
    </ItemGroup>
```

Run: `dotnet build SiagroB1.Reports 2>&1 | grep -E " error |Build succeeded" | sort -u`
Expected: `Build succeeded`. Se faltar um tipo do DANFE (`CS0246`), o arquivo dele está em `NFe.Danfe.Base/` ou `Shared.NFe.Danfe/` no mesmo commit: baixe-o para o caminho equivalente sob `ThirdParty/Zeus-LGPL/`, sem editar, e acrescente-o à lista do README.

```bash
git add SiagroB1.Reports/ThirdParty SiagroB1.Reports/SiagroB1.Reports.csproj
```

- [ ] **Step 2: Write the failing tests**

No `SiagroB1.Application.Tests.csproj`, acrescente:

```xml
    <ItemGroup>
      <Compile Include="..\SiagroB1.Fiscal.Tests\Support\NfeTestData.cs" Link="Support\NfeTestData.cs" />
      <None Include="..\SiagroB1.Reports\ThirdParty\Zeus-LGPL\NFe.Danfe.Base\NFe\NFeRetrato.frx"
            Link="ReportsContentRoot\ThirdParty\Zeus-LGPL\NFe.Danfe.Base\NFe\NFeRetrato.frx"
            CopyToOutputDirectory="PreserveNewest" />
    </ItemGroup>
```

```csharp
// SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeXmlDownloadServiceTests.cs
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

public class SalesInvoicesNfeXmlDownloadServiceTests
{
    [Fact]
    public async Task Returns_the_authorized_proc_named_by_the_access_key()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var invoice = scenario.Db.Context.SalesInvoices.Single();
        invoice.ChaveNFe = "35261012345678000195550010000000011481516230";
        scenario.Db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, Kind = SalesInvoiceNfeXmlKind.Authorized,
            Xml = "<nfeProc/>", CreatedAt = DateTime.Now,
        });
        await scenario.Db.SaveChangesAsync();

        var (bytes, fileName) = await new SalesInvoicesNfeXmlDownloadService(scenario.Db).ExecuteAsync(invoice.Key);

        Assert.Equal("35261012345678000195550010000000011481516230-procNFe.xml", fileName);
        Assert.Equal("<nfeProc/>", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Without_authorized_nfe_it_is_not_found()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new SalesInvoicesNfeXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey));
    }
}
```

```csharp
// SiagroB1.Application.Tests/Reports/DanfeReportServiceTests.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Tests.Support;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>O DANFE sai do procNFe gravado, pelo layout da Zeus copiado sem alteração.</summary>
public class DanfeReportServiceTests
{
    [Fact]
    public async Task Authorized_nfe_renders_a_pdf()
    {
        var db = TestDb.CreateUnitOfWork();
        var settings = new NfeServiceSettings(NfeEnvironment.Homologation, "SP",
            CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password), NfeServiceSettings.DefaultSchemasDirectory);
        var signed = NfeSigner.BuildSignAndValidate(NfeTestData.Input(), settings);
        var invoiceKey = Guid.NewGuid();
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoiceKey, Kind = SalesInvoiceNfeXmlKind.Authorized, CreatedAt = DateTime.Now,
            Xml = NfeProcComposer.Compose(signed.Xml, FakeNfeSefazClient.Authorized(signed.AccessKey).ProtocolXml!),
        });
        await db.SaveChangesAsync();

        var contentRoot = Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CompanyLogoPath"] = "wwwroot/images/logo.png" })
            .Build();
        var environment = new TestWebHostEnvironment(contentRoot);
        var header = new ReportHeaderService(environment, configuration, NullLogger<ReportHeaderService>.Instance);

        var (pdf, fileName) = await new DanfeReportService(db, environment, header).GeneratePdfAsync(invoiceKey);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal($"{signed.AccessKey}-danfe.pdf", fileName);
    }
}
```

```bash
git add SiagroB1.Application.Tests/Nfe/SalesInvoicesNfeXmlDownloadServiceTests.cs SiagroB1.Application.Tests/Reports/DanfeReportServiceTests.cs
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~DanfeReportServiceTests|FullyQualifiedName~NfeXmlDownload"`
Expected: FAIL de compilação — `DanfeReportService` e `SalesInvoicesNfeXmlDownloadService` não existem.

- [ ] **Step 4: Implementação**

Em `SiagroB1.Reports/Services/ReportHeaderService.cs`, acrescente o método público (reaproveita o cache do logo):

```csharp
    /// <summary>Bytes do logo configurado (ou nulo) — para layouts que recebem a imagem crua, como o DANFE.</summary>
    public byte[]? LogoBytes() => LoadLogo();
```

```csharp
// SiagroB1.Reports/Services/DanfeReportService.cs
using Microsoft.EntityFrameworkCore;
using NFe.Classes;
using NFe.Danfe.Base.NFe;
using NFe.Danfe.OpenFast.NFe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Reports.Services;

/// <summary>
/// DANFE da NF-e autorizada, a partir do procNFe gravado, com o layout FastReport da Zeus
/// (ThirdParty/Zeus-LGPL, sem alteração). Homologação sai com a marca "sem valor fiscal" do próprio
/// layout. ⚠️ O caminho padrão do .frx na Zeus usa barra invertida e falha no Linux: o caminho vai
/// absoluto.
/// </summary>
public class DanfeReportService(IUnitOfWork db, IWebHostEnvironment env, ReportHeaderService header)
{
    public async Task<(byte[] Pdf, string FileName)> GeneratePdfAsync(Guid invoiceKey)
    {
        var xml = await db.Context.SalesInvoiceNfeXmls.AsNoTracking()
                      .Where(x => x.SalesInvoiceKey == invoiceKey && x.Kind == SalesInvoiceNfeXmlKind.Authorized)
                      .OrderByDescending(x => x.CreatedAt)
                      .Select(x => x.Xml)
                      .FirstOrDefaultAsync()
                  ?? throw new NotFoundException("Este documento não tem NF-e autorizada.");

        var proc = new nfeProc().CarregarDeXmlString(xml);
        var template = Path.Combine(env.ContentRootPath, "ThirdParty", "Zeus-LGPL", "NFe.Danfe.Base", "NFe", "NFeRetrato.frx");

        FastReport.Utils.Config.WebMode = true;

        var danfe = new DanfeFrNfe(proc, new ConfiguracaoDanfeNfe(header.LogoBytes()),
            desenvolvedor: "IDX Consultoria e Sistemas", arquivoRelatorio: template);

        return (danfe.ExportarPdf(), $"{proc.protNFe.infProt.chNFe}-danfe.pdf");
    }
}
```

```csharp
// SiagroB1.Reports/Controllers/DanfeController.cs
using Microsoft.AspNetCore.Mvc;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Reports.Services;

namespace SiagroB1.Reports.Controllers;

[ApiController]
[Route("/reports/Danfe")]
public class DanfeController(DanfeReportService service) : ControllerBase
{
    [HttpPost("{key:guid}/print")]
    public async Task<IActionResult> Report(Guid key)
    {
        try
        {
            var (pdf, fileName) = await service.GeneratePdfAsync(key);

            Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
            return File(pdf, "application/pdf");
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
        catch (DefaultException e)
        {
            return BadRequest(e.Message);
        }
    }
}
```

(O `DanfeReportService` é registrado pela varredura do Scrutor do Reports — classes terminadas em `Service`.)

```csharp
// SiagroB1.Application/Services/Nfe/SalesInvoicesNfeXmlDownloadService.cs
using System.Text;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>XML autorizado (procNFe) para download: <c>&lt;chave&gt;-procNFe.xml</c>.</summary>
public class SalesInvoicesNfeXmlDownloadService(IUnitOfWork db)
{
    public async Task<(byte[] Bytes, string FileName)> ExecuteAsync(Guid invoiceKey)
    {
        var accessKey = await db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.Key == invoiceKey)
            .Select(i => i.ChaveNFe)
            .FirstOrDefaultAsync();

        var xml = await db.Context.SalesInvoiceNfeXmls.AsNoTracking()
                      .Where(x => x.SalesInvoiceKey == invoiceKey && x.Kind == SalesInvoiceNfeXmlKind.Authorized)
                      .OrderByDescending(x => x.CreatedAt)
                      .Select(x => x.Xml)
                      .FirstOrDefaultAsync()
                  ?? throw new NotFoundException("Este documento não tem NF-e autorizada.");

        return (Encoding.UTF8.GetBytes(xml), $"{accessKey}-procNFe.xml");
    }
}
```

```csharp
// SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeXmlController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.Nfe;

public class SalesInvoicesNfeXmlController(SalesInvoicesNfeXmlDownloadService service) : ODataController
{
    [HttpGet("odata/SalesInvoicesNfeXml(Key={key})")]
    public async Task<ActionResult> Download([FromRoute] Guid key)
    {
        try
        {
            var (bytes, fileName) = await service.ExecuteAsync(key);
            return File(bytes, "application/xml", fileName);
        }
        catch (NotFoundException e)
        {
            return NotFound(e.Message);
        }
    }
}
```

`ODataConfigurations.cs`:

```csharp
        var salesInvoicesNfeXml = modelBuilder.Function("SalesInvoicesNfeXml");
        salesInvoicesNfeXml.Parameter<Guid>("Key");
        salesInvoicesNfeXml.Returns<IActionResult>();
```

DI:

```csharp
        services.AddScoped<SalesInvoicesNfeXmlDownloadService>();
```

```bash
git add SiagroB1.Reports/Services/DanfeReportService.cs SiagroB1.Reports/Controllers/DanfeController.cs SiagroB1.Application/Services/Nfe/SalesInvoicesNfeXmlDownloadService.cs SiagroB1.Web/Functions/Nfe/SalesInvoicesNfeXmlController.cs
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test SiagroB1.Application.Tests --filter "FullyQualifiedName~DanfeReportServiceTests|FullyQualifiedName~NfeXmlDownload"`
Expected: PASS. Se o DANFE falhar ao preparar o relatório, a exceção do FastReport nomeia o campo do `.frx`: o problema está no procNFe (confira com `nfeProc().CarregarDeXmlString`), nunca no layout — que não se edita.

- [ ] **Step 6: Build e as duas suítes**

Run: `dotnet build SiagroB1.sln 2>&1 | grep -E " error |Build succeeded" | sort -u`, `dotnet test SiagroB1.Fiscal.Tests 2>&1 | tail -3`, `dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Build succeeded` e `Passed!` nas duas.

- [ ] **Step 7: Commit**

```bash
git commit -m "feat(reports): gerar o DANFE e baixar o XML da NF-e autorizada" -m "DANFE pelo layout FastReport da Zeus, trazido sem alteração para ThirdParty/Zeus-LGPL com o aviso da LGPL (não está no NuGet), a partir do procNFe gravado e com o logo que o Reports já usa. O XML autorizado baixa como <chave>-procNFe.xml.

Atenção: o caminho padrão do .frx na Zeus usa barra invertida e não resolve no Linux; o serviço passa o caminho absoluto. Editar os arquivos da Zeus obriga a publicar o diff (LGPL).

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- SiagroB1.Reports SiagroB1.Application SiagroB1.Web SiagroB1.Application.Tests
```

---
### Task 17: Frontend — filial, parceiro e pesquisa de municípios

**Repo:** `siagro-b1-frontend/` (todos os caminhos abaixo relativos a ele).

**Files:**
- Create: `webapp/dialogs/fragments/MunicipalitiesSelectDialog.fragment.xml`
- Modify: `webapp/controller/common/CommonController.ts`, `webapp/view/branchs/fragments/Form.fragment.xml`, `webapp/view/parceirosNegocio/fragments/Form.fragment.xml`, `webapp/view/parceirosNegocio/fragments/Addresses.fragment.xml`, `webapp/controller/parceirosNegocio/Add.controller.ts`, `webapp/controller/parceirosNegocio/Edit.controller.ts`

**Interfaces:**
- Consumes: OData `Municipalities`; colunas das Tasks 3 e 4 (`Branch.*`, `BusinessPartner.StateRegistration`, `StateRegistrationIndicator`, `NfeEmail`, `Phone`, `Address.StreetNumber`, `Complement`, `MunicipalityCode`); navegação `Branch/Municipality`.
- Produces: `CommonController.openMunicipalitiesValueHelp(ev)`; diálogo `MunicipalitiesSelectDialog`.

View XML não tem teste unitário aqui: a conferência desta tarefa é `ts-typecheck` + `lint` + `ui5lint`, e o roteiro no navegador da Task 21.

- [ ] **Step 1: Branch do frontend**

```bash
git -C siagro-b1-frontend checkout feature/nfe-standalone-taxation
git -C siagro-b1-frontend checkout -b feature/nfe-standalone-issuance
git -C siagro-b1-frontend branch --show-current
```

Expected: `feature/nfe-standalone-issuance`.

- [ ] **Step 2: Pesquisa de municípios**

```xml
<!-- webapp/dialogs/fragments/MunicipalitiesSelectDialog.fragment.xml -->
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:core="sap.ui.core"
>
<TableSelectDialog
  class="sapUiSizeCompact"
  growing="true"
  growingThreshold="30"
  items="{
    path: '/Municipalities',
    sorter: { path: 'Name', descending: false }
  }"
  title="Municípios (IBGE)"
>
<columns>
  <Column width="7rem"><header><Text text="Código" /></header></Column>
  <Column><header><Text text="Município" /></header></Column>
  <Column width="4rem"><header><Text text="UF" /></header></Column>
</columns>
<ColumnListItem>
  <cells>
    <Text text="{Code}" />
    <Text text="{Name}" />
    <Text text="{StateAbbreviation}" />
  </cells>
</ColumnListItem>
</TableSelectDialog>
</core:FragmentDefinition>
```

Em `webapp/controller/common/CommonController.ts`, ao lado de `openStatesAbbreviationValueHelp`:

```ts
  /**
   * Município do IBGE (NF-e STANDALONE). Grava o código; a descrição vai pelo
   * `descriptionProperty` do Input. Na filial e no endereço quem manda em cidade/UF é o
   * servidor, que as deriva do município.
   */
  openMunicipalitiesValueHelp(ev: Input$ValueHelpRequestEvent) {
    void this.applyValueHelp(ev, "MunicipalitiesSelectDialog", ["Code", "Name", "StateAbbreviation"], "Code");
  }
```

```bash
git -C siagro-b1-frontend add webapp/dialogs/fragments/MunicipalitiesSelectDialog.fragment.xml
```

- [ ] **Step 3: Filial — emitente**

Em `webapp/view/branchs/fragments/Form.fragment.xml`, depois do `CheckBox` de `IssuesNfe`, acrescente (o `xmlns:l="sap.ui.layout"` já existe no fragmento; se não existir, acrescente-o na raiz):

```xml
          <!-- Emitente da NF-e STANDALONE. Ocultos em SAPB1. Obrigatórios só na hora de emitir:
               a emissão lista o que faltar. -->
          <core:Title text="Emitente da NF-e" visible="{ui>/standalone}" />
          <Label text="Razão social" visible="{ui>/standalone}" />
          <Input value="{LegalName}" maxLength="60" visible="{ui>/standalone}" />
          <Label text="Nome fantasia" visible="{ui>/standalone}" />
          <Input value="{TradeName}" maxLength="60" visible="{ui>/standalone}" />
          <Label text="Inscrição estadual" visible="{ui>/standalone}" />
          <Input value="{StateRegistration}" maxLength="14" visible="{ui>/standalone}" />
          <Label text="Logradouro" visible="{ui>/standalone}" />
          <Input value="{Street}" maxLength="60" visible="{ui>/standalone}" />
          <Label text="Número / Complemento" visible="{ui>/standalone}" />
          <Input value="{StreetNumber}" maxLength="60" visible="{ui>/standalone}" />
          <Input value="{Complement}" maxLength="60" visible="{ui>/standalone}" />
          <Label text="Bairro" visible="{ui>/standalone}" />
          <Input value="{District}" maxLength="60" visible="{ui>/standalone}" />
          <Label text="Município" visible="{ui>/standalone}" />
          <Input
              value="{MunicipalityCode}"
              visible="{ui>/standalone}"
              showValueHelp="true"
              valueHelpOnly="true"
              valueHelpRequest=".openMunicipalitiesValueHelp"
              maxLength="7">
            <customData>
              <core:CustomData key="descriptionProperty" value="Municipality/Name:Name" />
            </customData>
          </Input>
          <Input value="{Municipality/Name}" editable="false" visible="{ui>/standalone}" />
          <Label text="CEP / Telefone" visible="{ui>/standalone}" />
          <Input value="{ZipCode}" maxLength="8" placeholder="Só dígitos" visible="{ui>/standalone}" />
          <Input value="{Phone}" maxLength="14" placeholder="Só dígitos" visible="{ui>/standalone}" />
```

- [ ] **Step 4: Parceiro — destinatário**

Em `webapp/view/parceirosNegocio/fragments/Form.fragment.xml`, depois do Input de `TaxId`:

```xml
          <!-- Destinatário/transportadora da NF-e STANDALONE (ocultos em SAPB1). -->
          <Label text="Indicador da IE" visible="{ui>/standalone}" />
          <Select
              visible="{ui>/standalone}"
              selectedKey="{ path: 'StateRegistrationIndicator', targetType: 'any' }"
              forceSelection="false">
            <core:ListItem key="Taxpayer" text="1 - Contribuinte do ICMS" />
            <core:ListItem key="Exempt" text="2 - Contribuinte isento" />
            <core:ListItem key="NonTaxpayer" text="9 - Não contribuinte" />
          </Select>
          <Label text="Inscrição estadual" visible="{ui>/standalone}" />
          <Input value="{StateRegistration}" maxLength="14" placeholder="Só dígitos" visible="{ui>/standalone}" />
          <Label text="E-mail da NF-e" visible="{ui>/standalone}" />
          <Input value="{NfeEmail}" maxLength="250" type="Email" visible="{ui>/standalone}" />
          <Label text="Telefone" visible="{ui>/standalone}" />
          <Input value="{Phone}" maxLength="14" placeholder="Só dígitos" visible="{ui>/standalone}" />
```

Em `webapp/view/parceirosNegocio/fragments/Addresses.fragment.xml`:

1. A coluna hoje rotulada "Complemento" é o `Block`, que é o **bairro** (é assim no SAP e no XML). Troque o `label="Complemento"` dessa coluna por:

```xml
      <t:Column>
        <t:label>
          <Label text="{= ${ui>/standalone} ? 'Bairro' : 'Complemento' }" />
        </t:label>
```

(fechando com o mesmo `<t:template>` que já existe). Em SAPB1 o rótulo continua o de hoje.

2. Depois da coluna "Logradouro", acrescente:

```xml
      <t:Column label="Número" width="6rem" visible="{ui>/standalone}">
        <t:template>
          <Input editable="{ui>/editable}" value="{StreetNumber}" maxLength="60" />
        </t:template>
      </t:Column>
      <t:Column label="Complemento" visible="{ui>/standalone}">
        <t:template>
          <Input editable="{ui>/editable}" value="{Complement}" maxLength="60" />
        </t:template>
      </t:Column>
```

3. Antes da coluna "Cidade", acrescente:

```xml
      <t:Column label="Município (IBGE)" width="9rem" visible="{ui>/standalone}">
        <t:template>
          <Input
            editable="{ui>/editable}"
            value="{MunicipalityCode}"
            showValueHelp="true"
            valueHelpOnly="true"
            valueHelpRequest=".openMunicipalitiesValueHelp">
            <customData>
              <core:CustomData key="descriptionProperty" value="City:Name,State:StateAbbreviation" />
            </customData>
          </Input>
        </t:template>
      </t:Column>
      <t:Column label="CEP" width="7rem" visible="{ui>/standalone}">
        <t:template>
          <Input editable="{ui>/editable}" value="{ZipCode}" maxLength="8" />
        </t:template>
      </t:Column>
```

A cidade e a UF escritas pelo `descriptionProperty` só atualizam a tela (grupo `null`); o servidor as grava a partir do município (Task 4).

Em `webapp/controller/parceirosNegocio/Add.controller.ts` e `Edit.controller.ts`, no handler de rota (o mesmo método que hoje faz o `bindList`/`bindElement`), acrescente no início:

```ts
    void this.refreshStandaloneFlag();
```

(`refreshStandaloneFlag` é do `BaseController` raiz, herdado via `CommonController`.)

- [ ] **Step 5: Conferências estáticas**

Run (em `siagro-b1-frontend/`): `yarn ts-typecheck && yarn lint && yarn ui5lint`
Expected: sem erro novo (compare com `git stash; yarn ui5lint; git stash pop` se aparecer aviso que pareça antigo).

- [ ] **Step 6: Commit**

```bash
git -C siagro-b1-frontend commit -m "feat(partner): campos da NF-e na filial e no parceiro, com pesquisa de município" -m "Emitente na filial e destinatário no parceiro, tudo oculto em SAPB1. No grid de endereços entram número, complemento, município do IBGE e CEP; a coluna Block passa a se chamar Bairro no STANDALONE, que é o que ela é. Cidade e UF vêm do município no servidor.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- webapp/dialogs/fragments/MunicipalitiesSelectDialog.fragment.xml webapp/controller/common/CommonController.ts webapp/view/branchs webapp/view/parceirosNegocio webapp/controller/parceirosNegocio
```

---

### Task 18: Frontend — condições de pagamento (tela, campo no documento e no parceiro)

**Repo:** `siagro-b1-frontend/`.

**Files:**
- Create: `webapp/helpers/NfeHelpers.ts`, `webapp/test/unit/helpers/NfeHelpers.qunit.ts`
- Create: `webapp/dialogs/fragments/PaymentConditionsSelectDialog.fragment.xml`
- Create: `webapp/view/paymentConditions/Main.view.xml`, `webapp/view/paymentConditions/fragments/ConditionDialog.fragment.xml`, `webapp/controller/paymentConditions/Main.controller.ts`
- Modify: `webapp/manifest.json`, `webapp/model/ServerRoutes.ts`, `webapp/controller/common/CommonController.ts`, `webapp/test/unit/unitTests.qunit.ts`, `webapp/view/salesInvoices/fragments/Form.fragment.xml`, `webapp/controller/salesInvoices/BaseController.ts`, `webapp/controller/salesInvoices/Add.controller.ts`, `Edit.controller.ts`, `Detail.controller.ts`, `webapp/view/parceirosNegocio/fragments/Form.fragment.xml`, `webapp/controller/parceirosNegocio/Edit.controller.ts`

**Interfaces:**
- Consumes: OData `PaymentConditions`, função `PaymentConditionsPreview` (Task 6); `sendJson`, `odataCollection` (`helpers/FetchHelpers.ts`).
- Produces: rota `paymentConditions` (= `MENU_ITEMS.Key` da Task 7); `NfeHelpers.PAYMENT_MEANS`, `PAYMENT_START_RULES`, `paymentPreviewUrl(...)`; `CommonController.openPaymentConditionsValueHelp(ev)` e `refreshPaymentConditionName(code)` → `ui>/paymentConditionName`.

- [ ] **Step 1: Write the failing test**

```ts
// webapp/test/unit/helpers/NfeHelpers.qunit.ts
import { PAYMENT_MEANS, PAYMENT_START_RULES, paymentPreviewUrl } from "siagrob1/helpers/NfeHelpers";

QUnit.module("NfeHelpers - condição de pagamento");

QUnit.test("meios de pagamento são os que o servidor aceita", function (assert) {
	assert.deepEqual(PAYMENT_MEANS.map((m) => m.key), ["01", "03", "04", "15", "16", "17", "18", "90", "99"]);
	assert.strictEqual(PAYMENT_MEANS.find((m) => m.key === "15")?.text, "15 - Boleto bancário");
});

QUnit.test("início da contagem usa os nomes do enum", function (assert) {
	assert.deepEqual(PAYMENT_START_RULES.map((r) => r.key), ["IssueDate", "NextMonth"]);
});

QUnit.test("URL da prévia leva os literais do OData, com os dias entre aspas e codificados", function (assert) {
	assert.strictEqual(
		paymentPreviewUrl("30,60", "NextMonth", "15", 1000.5, "2026-10-02"),
		"/odata/PaymentConditionsPreview(Days='30%2C60',StartRule=2,PaymentMeans='15',Total=1000.5,IssueDate=2026-10-02)"
	);
});

QUnit.test("total com vírgula ou vazio não quebra a URL", function (assert) {
	assert.ok(paymentPreviewUrl("0", "IssueDate", "17", Number("abc"), "2026-10-02").includes("Total=0,"));
});
```

Em `webapp/test/unit/unitTests.qunit.ts`, acrescente `import "./helpers/NfeHelpers.qunit";`.

```bash
git -C siagro-b1-frontend add webapp/test/unit/helpers/NfeHelpers.qunit.ts
```

- [ ] **Step 2: Run test to verify it fails**

Run: `yarn start` e abra `http://localhost:8080/test/Test.qunit.html?testsuite=test-resources/siagrob1/testsuite.qunit&test=unit/unitTests` filtrando por "NfeHelpers".
Expected: FAIL — módulo `siagrob1/helpers/NfeHelpers` não encontrado.

- [ ] **Step 3: Helper**

```ts
// webapp/helpers/NfeHelpers.ts
/**
 * Regras de tela da NF-e STANDALONE, puras para poderem ser testadas sem view.
 * As listas são as mesmas que o servidor aceita (`PaymentMeansCodes`, `PaymentStartRule`).
 */
export type Option = { key: string; text: string };

export const PAYMENT_MEANS: Option[] = [
  { key: "01", text: "01 - Dinheiro" },
  { key: "03", text: "03 - Cartão de crédito" },
  { key: "04", text: "04 - Cartão de débito" },
  { key: "15", text: "15 - Boleto bancário" },
  { key: "16", text: "16 - Depósito bancário" },
  { key: "17", text: "17 - PIX" },
  { key: "18", text: "18 - Transferência bancária" },
  { key: "90", text: "90 - Sem pagamento" },
  { key: "99", text: "99 - Outros" },
];

export const PAYMENT_START_RULES: Option[] = [
  { key: "IssueDate", text: "Data de emissão" },
  { key: "NextMonth", text: "Fora o mês (1º dia do mês seguinte)" },
];

const START_RULE_CODES: Record<string, number> = { IssueDate: 1, NextMonth: 2 };

/**
 * URL da função de prévia. O enum vai pelo número (o parâmetro é Edm.Int32) e o total com ponto
 * decimal; os dias vão entre aspas e codificados (a vírgula faz parte do valor).
 */
export function paymentPreviewUrl(
  days: string, startRule: string, paymentMeans: string, total: number, issueDate: string
): string {
  const safeTotal = Number.isFinite(total) ? total : 0;
  return "/odata/PaymentConditionsPreview(" +
    `Days='${encodeURIComponent(days ?? "")}',` +
    `StartRule=${START_RULE_CODES[startRule] ?? 1},` +
    `PaymentMeans='${encodeURIComponent(paymentMeans ?? "")}',` +
    `Total=${safeTotal},` +
    `IssueDate=${issueDate})`;
}
```

```bash
git -C siagro-b1-frontend add webapp/helpers/NfeHelpers.ts
```

- [ ] **Step 4: Run test to verify it passes**

Recarregue a página do QUnit filtrando por "NfeHelpers".
Expected: PASS (4 testes).

- [ ] **Step 5: Tela de condições de pagamento**

```xml
<!-- webapp/view/paymentConditions/Main.view.xml -->
<mvc:View
	controllerName="siagrob1.controller.paymentConditions.Main"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:t="sap.ui.table"
	core:require="{ formatter: 'siagrob1/model/formatter' }">

	<Page title="Condições de Pagamento">
		<content>
			<MessageStrip
				text="Condição do tipo Dias: parcelas iguais, o resto na última. Vai para a cobrança (duplicatas) e o pagamento da NF-e."
				type="Information"
				showIcon="true"
				class="sapUiSmallMargin" />
			<t:Table
				id="paymentConditionsTable"
				busyIndicatorDelay="0"
				selectionMode="Single"
				selectionBehavior="Row"
				class="sapUiSizeCondensed"
				visibleRowCountMode="Auto"
				enableBusyIndicator="true"
				alternateRowColors="true"
				rows="{ path: '/PaymentConditions', sorter: { path: 'Name' } }">
				<t:columns>
					<t:Column label="Código" width="6rem"><t:template><Text text="{Code}" /></t:template></t:Column>
					<t:Column label="Nome"><t:template><Text text="{Name}" /></t:template></t:Column>
					<t:Column label="Dias" width="10rem"><t:template><Text text="{Days}" /></t:template></t:Column>
					<t:Column label="Início da contagem" width="12rem">
						<t:template>
							<Text text="{ path: 'StartRule', targetType: 'any', formatter: '.formatStartRule' }" />
						</t:template>
					</t:Column>
					<t:Column label="Meio" width="14rem">
						<t:template>
							<Text text="{ path: 'PaymentMeans', formatter: '.formatPaymentMeans' }" />
						</t:template>
					</t:Column>
					<t:Column label="Inativa" width="6rem" hAlign="Center">
						<t:template><CheckBox selected="{Inactive}" editable="false" /></t:template>
					</t:Column>
				</t:columns>
			</t:Table>
		</content>
		<footer>
			<OverflowToolbar>
				<Button text="Atualizar" icon="sap-icon://refresh" press=".onRefresh" />
				<ToolbarSpacer />
				<Button text="Incluir" icon="sap-icon://add" type="Emphasized" press=".onCreate" />
				<Button text="Editar" icon="sap-icon://edit" press=".onEdit" />
				<Button text="Excluir" icon="sap-icon://delete" press=".onDelete" />
			</OverflowToolbar>
		</footer>
	</Page>
</mvc:View>
```

```xml
<!-- webapp/view/paymentConditions/fragments/ConditionDialog.fragment.xml -->
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:f="sap.ui.layout.form"
    xmlns:core="sap.ui.core"
>
  <Dialog title="Condição de pagamento" contentWidth="36rem" draggable="true">
    <content>
      <f:SimpleForm editable="true" layout="ResponsiveGridLayout" labelSpanM="4" labelSpanS="12">
        <f:content>
          <Label text="Nome" required="true" />
          <Input value="{condition>/name}" maxLength="100" />
          <Label text="Dias" required="true" />
          <Input value="{condition>/days}" placeholder="0 ou 30,60,90" maxLength="100" />
          <Label text="Início da contagem" />
          <Select selectedKey="{condition>/startRule}" items="{condition>/startRules}">
            <core:Item key="{condition>key}" text="{condition>text}" />
          </Select>
          <Label text="Meio de pagamento" required="true" />
          <Select selectedKey="{condition>/paymentMeans}" items="{condition>/paymentMeansList}">
            <core:Item key="{condition>key}" text="{condition>text}" />
          </Select>
          <Label text="Inativa" />
          <CheckBox selected="{condition>/inactive}" />

          <core:Title text="Prévia das parcelas" />
          <Label text="Valor / Emissão" />
          <Input value="{condition>/previewTotal}" type="Number" />
          <DatePicker value="{condition>/previewDate}" valueFormat="yyyy-MM-dd" displayFormat="dd/MM/yyyy" />
          <Label text="" />
          <Button text="Calcular prévia" icon="sap-icon://simulate" press=".onPreview" />
        </f:content>
      </f:SimpleForm>
      <Table items="{condition>/preview}" noDataText="Calcule a prévia para ver as parcelas." class="sapUiSmallMarginBeginEnd">
        <columns>
          <Column width="5rem"><Text text="Parcela" /></Column>
          <Column><Text text="Vencimento" /></Column>
          <Column hAlign="End"><Text text="Valor" /></Column>
        </columns>
        <items>
          <ColumnListItem>
            <cells>
              <Text text="{condition>Number}" />
              <Text text="{ path: 'condition>DueDate', formatter: '.formatIsoDate' }" />
              <Text text="{ path: 'condition>Amount', formatter: '.formatAmount' }" />
            </cells>
          </ColumnListItem>
        </items>
      </Table>
    </content>
    <beginButton>
      <Button text="Confirmar" type="Emphasized" press=".onConfirmCondition" />
    </beginButton>
    <endButton>
      <Button text="Cancelar" press=".onCancelCondition" />
    </endButton>
  </Dialog>
</core:FragmentDefinition>
```

```ts
// webapp/controller/paymentConditions/Main.controller.ts
import BaseController from "../BaseController";
import JSONModel from "sap/ui/model/json/JSONModel";
import Dialog from "sap/m/Dialog";
import Table from "sap/ui/table/Table";
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import Context from "sap/ui/model/odata/v4/Context";
import DialogHelper from "siagrob1/dialogs/DialogHelper";
import { confirmDialog } from "siagrob1/helpers/DialogHelpers";
import { odataCollection, sendJson } from "siagrob1/helpers/FetchHelpers";
import { PAYMENT_MEANS, PAYMENT_START_RULES, paymentPreviewUrl } from "siagrob1/helpers/NfeHelpers";

type ConditionForm = {
  code?: number;
  name: string;
  days: string;
  startRule: string;
  paymentMeans: string;
  inactive: boolean;
  previewTotal: number | string;
  previewDate: string;
  preview: unknown[];
  startRules: unknown[];
  paymentMeansList: unknown[];
};

/**
 * Condições de pagamento (só STANDALONE — o menu some nos outros modos e o servidor recusa).
 * Mesmo desenho da tela de alíquotas IBS/CBS: lista OData + diálogo com JSONModel.
 *
 * @namespace siagrob1.controller.paymentConditions
 */
export default class Main extends BaseController {
  private conditionDialog: Dialog;

  onInit(): void {
    this.getView().setModel(new JSONModel({}), "condition");
    this.getRouter().getRoute("paymentConditions").attachPatternMatched(() => this.onRefresh());
  }

  formatStartRule(value: string): string {
    return PAYMENT_START_RULES.find((r) => r.key === value)?.text ?? value;
  }

  formatPaymentMeans(value: string): string {
    return PAYMENT_MEANS.find((m) => m.key === value)?.text ?? value;
  }

  formatIsoDate(value: string): string {
    return value ? value.split("-").reverse().join("/") : "";
  }

  formatAmount(value: number | string): string {
    return Number(value ?? 0).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  private table(): Table {
    return this.byId("paymentConditionsTable") as Table;
  }

  onRefresh(): void {
    (this.table().getBinding("rows") as ODataListBinding)?.refresh();
  }

  private selected(): Context | undefined {
    const i = this.table().getSelectedIndex();
    return i < 0 ? undefined : (this.table().getContextByIndex(i) as Context);
  }

  async onCreate() {
    await this.openDialog({ name: "", days: "0", startRule: "IssueDate", paymentMeans: "15", inactive: false });
  }

  async onEdit() {
    const ctx = this.selected();
    if (!ctx) {
      MessageBox.alert("Selecione uma condição para editar.");
      return;
    }

    await this.openDialog({
      code: ctx.getProperty("Code") as number,
      name: ctx.getProperty("Name") as string,
      days: ctx.getProperty("Days") as string,
      startRule: ctx.getProperty("StartRule") as string,
      paymentMeans: ctx.getProperty("PaymentMeans") as string,
      inactive: ctx.getProperty("Inactive") as boolean,
    });
  }

  private async openDialog(data: Partial<ConditionForm>) {
    (this.getView().getModel("condition") as JSONModel).setData({
      ...data,
      previewTotal: 1000,
      previewDate: new Date().toISOString().slice(0, 10),
      preview: [],
      startRules: PAYMENT_START_RULES,
      paymentMeansList: PAYMENT_MEANS,
    });
    this.conditionDialog ??= await DialogHelper.createDialog(this, "siagrob1.view.paymentConditions.fragments.ConditionDialog");
    this.conditionDialog.open();
  }

  onCancelCondition() {
    this.conditionDialog?.close();
  }

  async onPreview() {
    const model = this.getView().getModel("condition") as JSONModel;
    const data = model.getData() as ConditionForm;
    const result = await sendJson("GET", paymentPreviewUrl(
      data.days, data.startRule, data.paymentMeans, Number(data.previewTotal), data.previewDate));

    if (!result.ok) {
      MessageBox.error(result.message);
      return;
    }

    model.setProperty("/preview", odataCollection(result.data));
  }

  async onConfirmCondition() {
    const data = (this.getView().getModel("condition") as JSONModel).getData() as ConditionForm;

    if (!data.name?.trim() || !data.days?.trim()) {
      MessageBox.warning("Informe o nome e os dias da condição.");
      return;
    }

    const model = this.getView().getModel() as ODataModel;
    const groupId = model.getUpdateGroupId();
    const payload: Record<string, string | boolean> = {
      Name: data.name.trim(),
      Days: data.days.trim(),
      StartRule: data.startRule,
      PaymentMeans: data.paymentMeans,
      Inactive: data.inactive,
    };

    try {
      this.setBusy(true);

      if (data.code == null) {
        (this.table().getBinding("rows") as ODataListBinding).create(payload, true);
      } else {
        const ctx = this.selected();
        // Sem await: com o grupo diferido a promise só resolve depois do submitBatch.
        Object.entries(payload).forEach(([name, value]) => void ctx.setProperty(name, value));
      }

      await model.submitBatch(groupId);

      if (model.hasPendingChanges(groupId)) {
        // A mensagem do servidor já foi mostrada pelo handler global; desfaz para não reenviar.
        model.resetChanges(groupId);
        return;
      }

      this.conditionDialog.close();
      MessageToast.show("Condição de pagamento gravada.");
      this.onRefresh();
    } finally {
      this.setBusy(false);
    }
  }

  async onDelete() {
    const ctx = this.selected();
    if (!ctx) {
      MessageBox.alert("Selecione uma condição para excluir.");
      return;
    }

    if (!(await confirmDialog("Deseja realmente excluir a condição selecionada ?", "Excluir condição ?"))) {
      return;
    }

    const model = this.getView().getModel() as ODataModel;

    try {
      this.setBusy(true);
      await ctx.delete("$auto");
      await model.submitBatch(model.getUpdateGroupId());
      MessageToast.show("Condição excluída.");
    } finally {
      this.setBusy(false);
    }
  }
}
```

Em `webapp/manifest.json`, junto da rota `usages`:

```json
        {
          "pattern": "payment-conditions",
          "name": "paymentConditions",
          "target": "paymentConditions"
        },
```

e nos `targets`, junto de `usages`:

```json
        "paymentConditions": {
          "id": "paymentConditions",
          "level": 1,
          "name": "siagrob1.view.paymentConditions.Main",
          "clearControlAggregation": true
        },
```

```bash
git -C siagro-b1-frontend add webapp/view/paymentConditions webapp/controller/paymentConditions
```

- [ ] **Step 6: Value help e nome da condição**

```xml
<!-- webapp/dialogs/fragments/PaymentConditionsSelectDialog.fragment.xml -->
<core:FragmentDefinition xmlns="sap.m" xmlns:core="sap.ui.core">
<TableSelectDialog
  class="sapUiSizeCompact"
  growing="true"
  growingThreshold="20"
  items="{ path: '/PaymentConditions', sorter: { path: 'Name' } }"
  title="Condições de Pagamento"
>
<columns>
  <Column width="6rem"><header><Text text="Código" /></header></Column>
  <Column><header><Text text="Nome" /></header></Column>
  <Column width="9rem"><header><Text text="Dias" /></header></Column>
</columns>
<ColumnListItem>
  <cells>
    <Text text="{Code}" />
    <Text text="{Name}" />
    <Text text="{Days}" />
  </cells>
</ColumnListItem>
</TableSelectDialog>
</core:FragmentDefinition>
```

Em `webapp/model/ServerRoutes.ts`, antes do `}` final:

```ts
  // NF-e STANDALONE (sub-projeto 2a).
  paymentConditions: '/odata/PaymentConditions',
```

Em `CommonController.ts` (acrescente `import { sendJson } from "siagrob1/helpers/FetchHelpers";` e `import ServerRoutes from "siagrob1/model/ServerRoutes";` se faltarem):

```ts
  /**
   * Condição de pagamento (NF-e STANDALONE). O documento e o parceiro guardam só o código, sem
   * navegação: o nome vai para `ui>/paymentConditionName`. Só condições ativas aparecem.
   */
  async openPaymentConditionsValueHelp(ev: Input$ValueHelpRequestEvent) {
    const oInput = ev.getSource();
    const oContext = await DialogHelper.openTableSelectDialog(
      this, "PaymentConditionsSelectDialog", ["Name", "Days"], [], undefined, "Inactive eq false");

    if (!oContext) {
      return;
    }

    oInput.setValue(String(oContext.getProperty("Code")));
    (this.getModel("ui") as JSONModel).setProperty("/paymentConditionName", oContext.getProperty("Name"));
  }

  /** Nome da condição gravada (o campo guarda só o código). */
  protected async refreshPaymentConditionName(code: number | string | undefined): Promise<void> {
    const uiModel = this.getModel("ui") as JSONModel;
    uiModel.setProperty("/paymentConditionName", "");

    if (code == null || code === "") {
      return;
    }

    const result = await sendJson("GET", `${ServerRoutes.paymentConditions}(${code})`);
    uiModel.setProperty("/paymentConditionName", result.ok ? (result.data as { Name?: string })?.Name ?? "" : "");
  }
```

- [ ] **Step 7: Campo no documento de saída e no parceiro**

Em `webapp/view/salesInvoices/fragments/Form.fragment.xml`, depois do bloco de "Tipo do Frete" e antes de `<core:Title text="Dados da NF-e"/>`:

```xml
            <!-- NF-e STANDALONE: vira cobr/dup e pag do XML. Padrão vem do cliente na criação. -->
            <Label text="Condição de pagamento" visible="{ui>/standalone}" />
            <Input
              visible="{ui>/standalone}"
              editable="{ui>/editable}"
              showValueHelp="true"
              valueHelpOnly="true"
              valueHelpRequest=".openPaymentConditionsValueHelp"
              value="{PaymentConditionCode}">
              <layoutData>
                <l:GridData span="XL3 L3 M3 S8" />
              </layoutData>
            </Input>
            <Input value="{ui>/paymentConditionName}" editable="false" visible="{ui>/standalone}" />
```

Em `webapp/controller/salesInvoices/BaseController.ts`, acrescente:

```ts
    /** Modo (campos da NF-e) e nome da condição de pagamento do documento ligado à view. */
    protected async refreshNfeHeaderFromContext() {
      void this.refreshStandaloneFlag();
      const oContext = this.getView().getBindingContext() as Context;
      const code = oContext ? await oContext.requestProperty("PaymentConditionCode") as number : undefined;
      await this.refreshPaymentConditionName(code);
    }
```

E chame `void this.refreshNfeHeaderFromContext();` nos handlers de rota de `Detail.controller.ts` e `Edit.controller.ts` (logo depois de `void this.refreshTaxLockFromContext();`); em `Add.controller.ts`, no handler de rota, chame `void this.refreshStandaloneFlag();` e `(this.getModel("ui") as JSONModel).setProperty("/paymentConditionName", "");`.

No parceiro (`webapp/view/parceirosNegocio/fragments/Form.fragment.xml`), depois do telefone (Task 17):

```xml
          <Label text="Condição de pagamento padrão" visible="{ui>/standalone}" />
          <Input
              visible="{ui>/standalone}"
              showValueHelp="true"
              valueHelpOnly="true"
              valueHelpRequest=".openPaymentConditionsValueHelp"
              value="{PaymentConditionCode}" />
          <Input value="{ui>/paymentConditionName}" editable="false" visible="{ui>/standalone}" />
```

Em `webapp/controller/parceirosNegocio/Edit.controller.ts`, depois do `bindElement`, carregue o nome:

```ts
    void (async () => {
      const oContext = this.getView().getBindingContext() as Context;
      const code = oContext ? await oContext.requestProperty("PaymentConditionCode") as number : undefined;
      await this.refreshPaymentConditionName(code);
    })();
```

```bash
git -C siagro-b1-frontend add webapp/dialogs/fragments/PaymentConditionsSelectDialog.fragment.xml
```

- [ ] **Step 8: Conferências estáticas e QUnit**

Run: `yarn ts-typecheck && yarn lint && yarn ui5lint`
Expected: sem erro novo. Recarregue o QUnit inteiro: todos verdes (135 + 4).

- [ ] **Step 9: Commit**

```bash
git -C siagro-b1-frontend commit -m "feat(financial): tela de condições de pagamento e o campo no documento de saída" -m "Cadastro com prévia das parcelas calculada no servidor (o mesmo cálculo da emissão). O documento de saída e o parceiro ganham a condição de pagamento, só no STANDALONE; o nome aparece ao lado do código, que é o que eles guardam.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- webapp
```

---

### Task 19: Frontend — Configuração da NF-e

**Repo:** `siagro-b1-frontend/`.

**Files:**
- Create: `webapp/view/nfeSettings/Main.view.xml`, `webapp/view/nfeSettings/fragments/CertificateDialog.fragment.xml`, `webapp/controller/nfeSettings/Main.controller.ts`
- Modify: `webapp/helpers/NfeHelpers.ts`, `webapp/test/unit/helpers/NfeHelpers.qunit.ts`, `webapp/model/ServerRoutes.ts`, `webapp/manifest.json`

**Interfaces:**
- Consumes: `BranchNfeSettingsGet`, `BranchNfeSettingsSave`, `BranchNfeSettingsUploadCertificate`, `BranchNfeSettingsTestConnection` (Task 12); `openBranchsValueHelp` (existente).
- Produces: rota `nfeSettings` (= `MENU_ITEMS.Key`); `NfeHelpers.certificateDaysToExpire(validUntil, today)`, `certificateState(days)`, `environmentCode(name)`, `readFileAsBase64(file)`.

- [ ] **Step 1: Write the failing test**

Acrescente em `webapp/test/unit/helpers/NfeHelpers.qunit.ts` (e o import dos nomes novos no topo):

```ts
QUnit.module("NfeHelpers - certificado e ambiente");

QUnit.test("dias para vencer contam a partir de hoje", function (assert) {
	const today = new Date(2026, 9, 2);
	assert.strictEqual(certificateDaysToExpire("2026-10-12T15:00:00-03:00", today), 10);
	assert.strictEqual(certificateDaysToExpire("2026-09-30T00:00:00-03:00", today), -2);
	assert.strictEqual(certificateDaysToExpire(undefined, today), undefined);
});

QUnit.test("situação do certificado: vencido, perto de vencer, ok", function (assert) {
	assert.strictEqual(certificateState(-1), "Error");
	assert.strictEqual(certificateState(29), "Warning");
	assert.strictEqual(certificateState(30), "Success");
	assert.strictEqual(certificateState(undefined), "None");
});

QUnit.test("ambiente vai ao servidor pelo número do enum", function (assert) {
	assert.strictEqual(environmentCode("Production"), 1);
	assert.strictEqual(environmentCode("Homologation"), 2);
	assert.strictEqual(environmentCode(undefined), 2);
});
```

- [ ] **Step 2: Run test to verify it fails**

QUnit filtrando por "NfeHelpers". Expected: FAIL — funções não exportadas.

- [ ] **Step 3: Helpers**

Acrescente em `webapp/helpers/NfeHelpers.ts`:

```ts
const DAY = 24 * 60 * 60 * 1000;

/** Dias inteiros até a validade (negativo = vencido), contados por data, sem hora. */
export function certificateDaysToExpire(validUntil: string | undefined, today: Date): number | undefined {
  if (!validUntil) {
    return undefined;
  }

  const end = new Date(validUntil);
  const endDay = Date.UTC(end.getFullYear(), end.getMonth(), end.getDate());
  const todayDay = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate());

  return Math.round((endDay - todayDay) / DAY);
}

/** ObjectStatus do certificado: alerta abaixo de 30 dias (spec §7.2). */
export function certificateState(days: number | undefined): "None" | "Error" | "Warning" | "Success" {
  if (days === undefined) {
    return "None";
  }

  if (days < 0) {
    return "Error";
  }

  return days < 30 ? "Warning" : "Success";
}

/** O parâmetro `Environment` da action é Edm.Int32: Production = 1, Homologation = 2. */
export function environmentCode(name: string | undefined): number {
  return name === "Production" ? 1 : 2;
}

/** Conteúdo do arquivo em base64, sem o prefixo `data:...;base64,` (mesmo padrão do anexo do contrato). */
export function readFileAsBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result as string;
      resolve(result.includes(",") ? result.split(",")[1] : result);
    };
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(file);
  });
}
```

- [ ] **Step 4: Run test to verify it passes**

QUnit filtrando por "NfeHelpers". Expected: PASS.

- [ ] **Step 5: Tela**

Em `webapp/model/ServerRoutes.ts`:

```ts
  branchNfeSettingsGet: '/odata/BranchNfeSettingsGet',
  branchNfeSettingsSave: '/odata/BranchNfeSettingsSave',
  branchNfeSettingsUploadCertificate: '/odata/BranchNfeSettingsUploadCertificate',
  branchNfeSettingsTestConnection: '/odata/BranchNfeSettingsTestConnection',
```

```xml
<!-- webapp/view/nfeSettings/Main.view.xml -->
<mvc:View
	controllerName="siagrob1.controller.nfeSettings.Main"
	displayBlock="true"
	xmlns="sap.m"
	xmlns:mvc="sap.ui.core.mvc"
	xmlns:core="sap.ui.core"
	xmlns:f="sap.ui.layout.form">

	<Page title="Configuração da NF-e" busy="{ui>/busy}" busyIndicatorDelay="0">
		<content>
			<MessageStrip
				text="O servidor não tem a chave Nfe:CertificateKey: o certificado não pode ser enviado nem a NF-e emitida."
				type="Error" showIcon="true" class="sapUiSmallMargin"
				visible="{= ${nfe>/loaded} === true &amp;&amp; ${nfe>/ServerKeyConfigured} === false }" />
			<MessageStrip
				text="Ambiente de homologação — notas sem valor fiscal."
				type="Warning" showIcon="true" class="sapUiSmallMargin"
				visible="{= ${nfe>/loaded} === true &amp;&amp; ${nfe>/Environment} === 'Homologation' }" />

			<f:SimpleForm editable="true" layout="ResponsiveGridLayout" labelSpanL="3" labelSpanM="4" columnsL="2" columnsM="1">
				<f:content>
					<core:Title text="Filial" />
					<Label text="Filial" required="true" />
					<Input
						id="nfeSettingsBranch"
						value="{nfe>/BranchCode}"
						showValueHelp="true"
						valueHelpOnly="true"
						valueHelpRequest=".onPickBranch" />

					<Label text="Ambiente" />
					<Select selectedKey="{nfe>/Environment}" enabled="{= ${nfe>/loaded} === true }">
						<core:Item key="Homologation" text="Homologação" />
						<core:Item key="Production" text="Produção" />
					</Select>
					<Label text="Série" />
					<Input value="{nfe>/Series}" type="Number" enabled="{= ${nfe>/loaded} === true }" />
					<Label text="Próximo número" />
					<Input value="{nfe>/NextNumber}" type="Number" enabled="{= ${nfe>/loaded} === true }" />

					<core:Title text="Certificado digital (A1)" />
					<Label text="Titular" />
					<Text text="{nfe>/CertificateSubject}" />
					<Label text="CNPJ" />
					<Text text="{nfe>/CertificateTaxId}" />
					<Label text="Validade" />
					<ObjectStatus text="{nfe>/certificateText}" state="{nfe>/certificateState}" />
				</f:content>
			</f:SimpleForm>
		</content>
		<footer>
			<OverflowToolbar>
				<Button text="Testar comunicação" icon="sap-icon://connected" press=".onTestConnection"
					enabled="{= ${nfe>/HasCertificate} === true }" />
				<ToolbarSpacer />
				<Button text="Enviar certificado" icon="sap-icon://upload" press=".onOpenCertificate"
					enabled="{= ${nfe>/loaded} === true }" />
				<Button text="Salvar" type="Emphasized" icon="sap-icon://save" press=".onSave"
					enabled="{= ${nfe>/loaded} === true }" />
			</OverflowToolbar>
		</footer>
	</Page>
</mvc:View>
```

```xml
<!-- webapp/view/nfeSettings/fragments/CertificateDialog.fragment.xml -->
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:f="sap.ui.layout.form"
    xmlns:u="sap.ui.unified"
    xmlns:core="sap.ui.core"
>
  <Dialog title="Enviar certificado A1" contentWidth="30rem" draggable="true">
    <content>
      <f:SimpleForm editable="true" layout="ResponsiveGridLayout" labelSpanM="4" labelSpanS="12">
        <f:content>
          <Label text="Arquivo (.pfx)" required="true" />
          <u:FileUploader id="nfeCertificateFile" fileType="pfx,p12" sendXHR="false" change=".onCertificateFileChange" />
          <Label text="Senha" required="true" />
          <Input value="{nfe>/certificatePassword}" type="Password" />
        </f:content>
      </f:SimpleForm>
    </content>
    <beginButton>
      <Button text="Enviar" type="Emphasized" press=".onUploadCertificate" />
    </beginButton>
    <endButton>
      <Button text="Cancelar" press=".onCloseCertificate" />
    </endButton>
  </Dialog>
</core:FragmentDefinition>
```

```ts
// webapp/controller/nfeSettings/Main.controller.ts
import CommonController from "../common/CommonController";
import JSONModel from "sap/ui/model/json/JSONModel";
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import MessageToast from "sap/m/MessageToast";
import { Input$ValueHelpRequestEvent } from "sap/m/Input";
import { FileUploader$ChangeEvent } from "sap/ui/unified/FileUploader";
import DialogHelper from "siagrob1/dialogs/DialogHelper";
import ServerRoutes from "siagrob1/model/ServerRoutes";
import { odataValue, sendJson } from "siagrob1/helpers/FetchHelpers";
import {
  certificateDaysToExpire, certificateState, environmentCode, readFileAsBase64,
} from "siagrob1/helpers/NfeHelpers";

type Settings = {
  BranchCode: string;
  Environment: string;
  Series: number | string;
  NextNumber: number | string;
  HasCertificate: boolean;
  CertificateSubject?: string;
  CertificateTaxId?: string;
  CertificateValidUntil?: string;
  ServerKeyConfigured: boolean;
};

/**
 * Configuração da NF-e por filial (spec §7.2) — só STANDALONE (menu StandaloneOnly; o servidor
 * recusa nos demais modos). Tudo por actions/functions lidas com fetch: o certificado vai em
 * base64, como o anexo do contrato.
 *
 * @namespace siagrob1.controller.nfeSettings
 */
export default class Main extends CommonController {
  private certificateDialog: Dialog;
  private certificateFile: File;

  onInit(): void {
    this.getView().setModel(new JSONModel({ loaded: false }), "nfe");
    this.getRouter().getRoute("nfeSettings").attachPatternMatched(() => this.reset());
  }

  private model(): JSONModel {
    return this.getView().getModel("nfe") as JSONModel;
  }

  private reset() {
    this.model().setData({ loaded: false });
  }

  async onPickBranch(ev: Input$ValueHelpRequestEvent) {
    const oContext = await DialogHelper.openTableSelectDialog(
      this, "BranchsSelectDialog", ["Code", "BranchName", "ShortName", "TaxId"]);

    if (!oContext) {
      return;
    }

    ev.getSource().setValue(oContext.getProperty("Code") as string);
    await this.load(oContext.getProperty("Code") as string);
  }

  private async load(branchCode: string) {
    this.setBusy(true);
    try {
      const result = await sendJson("GET", `${ServerRoutes.branchNfeSettingsGet}(BranchCode='${encodeURIComponent(branchCode)}')`);
      if (!result.ok) {
        MessageBox.error(result.message);
        return;
      }

      this.show(odataValue<Settings>(result.data));
    } finally {
      this.setBusy(false);
    }
  }

  private show(settings: Settings) {
    const days = certificateDaysToExpire(settings.CertificateValidUntil, new Date());
    const until = settings.CertificateValidUntil ? new Date(settings.CertificateValidUntil).toLocaleDateString("pt-BR") : "";

    this.model().setData({
      ...settings,
      loaded: true,
      certificateState: certificateState(days),
      certificateText: !settings.HasCertificate
        ? "Nenhum certificado enviado"
        : days < 0 ? `Vencido em ${until}` : `${until} (${days} dias para vencer)`,
    });
  }

  async onSave() {
    const data = this.model().getData() as Settings;
    this.setBusy(true);
    try {
      const result = await sendJson("POST", ServerRoutes.branchNfeSettingsSave, {
        BranchCode: data.BranchCode,
        Environment: environmentCode(data.Environment),
        Series: Number(data.Series),
        NextNumber: Number(data.NextNumber),
      });

      if (!result.ok) {
        MessageBox.error(result.message);
        return;
      }

      this.show(odataValue<Settings>(result.data));
      MessageToast.show("Configuração da NF-e gravada.");
    } finally {
      this.setBusy(false);
    }
  }

  async onOpenCertificate() {
    this.model().setProperty("/certificatePassword", "");
    this.certificateFile = undefined;
    this.certificateDialog ??= await DialogHelper.createDialog(this, "siagrob1.view.nfeSettings.fragments.CertificateDialog");
    this.certificateDialog.open();
  }

  onCertificateFileChange(ev: FileUploader$ChangeEvent) {
    const files = ev.getParameter("files") as unknown as File[];
    this.certificateFile = files?.length > 0 ? files[0] : undefined;
  }

  onCloseCertificate() {
    this.certificateDialog?.close();
  }

  async onUploadCertificate() {
    const data = this.model().getData() as Settings & { certificatePassword: string };

    if (!this.certificateFile || !data.certificatePassword) {
      MessageBox.warning("Escolha o arquivo .pfx e informe a senha.");
      return;
    }

    this.setBusy(true);
    try {
      const result = await sendJson("POST", ServerRoutes.branchNfeSettingsUploadCertificate, {
        BranchCode: data.BranchCode,
        Pfx: await readFileAsBase64(this.certificateFile),
        Password: data.certificatePassword,
      });

      if (!result.ok) {
        MessageBox.error(result.message);
        return;
      }

      this.certificateDialog.close();
      this.show(odataValue<Settings>(result.data));
      MessageToast.show("Certificado enviado.");
    } finally {
      this.model().setProperty("/certificatePassword", "");
      this.setBusy(false);
    }
  }

  async onTestConnection() {
    const data = this.model().getData() as Settings;
    this.setBusy(true);
    try {
      const result = await sendJson("POST", ServerRoutes.branchNfeSettingsTestConnection, { BranchCode: data.BranchCode });

      if (!result.ok) {
        MessageBox.error(result.message);
        return;
      }

      const status = odataValue<{ StatusCode: number; Reason: string }>(result.data);
      const text = `${status.StatusCode} - ${status.Reason}`;
      if (status.StatusCode === 107) {
        MessageBox.success(text);
      } else {
        MessageBox.warning(text);
      }
    } finally {
      this.setBusy(false);
    }
  }
}
```

Em `webapp/manifest.json`, rota e target (junto de `paymentConditions`):

```json
        {
          "pattern": "nfe-settings",
          "name": "nfeSettings",
          "target": "nfeSettings"
        },
```

```json
        "nfeSettings": {
          "id": "nfeSettings",
          "level": 1,
          "name": "siagrob1.view.nfeSettings.Main",
          "clearControlAggregation": true
        },
```

(`setBusy` vem do `BaseController` raiz; o `FileUploader` de `sap.ui.unified` já é usado pelo upload do anexo do contrato — a biblioteca é dependência do `sap.m`.)

```bash
git -C siagro-b1-frontend add webapp/view/nfeSettings webapp/controller/nfeSettings
```

- [ ] **Step 6: Conferências estáticas e QUnit**

Run: `yarn ts-typecheck && yarn lint && yarn ui5lint`
Expected: sem erro novo. QUnit inteiro verde.

- [ ] **Step 7: Commit**

```bash
git -C siagro-b1-frontend commit -m "feat(invoice): tela de configuração da NF-e por filial" -m "Ambiente, série e próximo número; quadro do certificado com alerta abaixo de 30 dias; envio do .pfx em base64 com a senha (nunca devolvida); Testar comunicação; faixa fixa de homologação sem valor fiscal e aviso quando o servidor não tem a chave de cifra.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- webapp
```

---

### Task 20: Frontend — emissão no documento de saída

**Repo:** `siagro-b1-frontend/`.

**Files:**
- Create: `webapp/view/salesInvoices/fragments/NfePanel.fragment.xml`
- Modify: `webapp/helpers/NfeHelpers.ts`, `webapp/test/unit/helpers/NfeHelpers.qunit.ts`, `webapp/model/formatter.ts`, `webapp/test/unit/model/formatter.qunit.ts`, `webapp/model/ServerRoutes.ts`, `webapp/view/salesInvoices/Detail.view.xml`, `webapp/controller/salesInvoices/Detail.controller.ts`, `webapp/view/salesInvoices/Main.view.xml`, `webapp/controller/salesInvoices/Main.controller.ts`

**Interfaces:**
- Consumes: actions `SalesInvoicesIssueNfe`, `SalesInvoicesConsultNfe`, `SalesInvoicesCompleteNfeConfirmation` (Tasks 14–15), função `SalesInvoicesNfeXml` e `POST /reports/Danfe/{key}/print` (Task 16), propriedades `NfeStatus`, `NfeStatusCode`, `NfeStatusReason`, `NfeProtocol`, `NfeAuthorizedAt`, `NfeConfirmationError`, `ChaveNFe`; `ui>/taxLocked` (sub-projeto 1).
- Produces: `formatter.formatNfeStatus`, `formatter.stateNfeStatus`; `NfeHelpers.nfeOutcomeMessage(outcome)`.

- [ ] **Step 1: Write the failing tests**

Em `webapp/test/unit/model/formatter.qunit.ts`:

```ts
QUnit.module("formatter - situação da NF-e (STANDALONE)");

QUnit.test("situação vira texto em português", function (assert) {
	assert.strictEqual(formatter.formatNfeStatus("None"), "");
	assert.strictEqual(formatter.formatNfeStatus("Processing"), "Em processamento");
	assert.strictEqual(formatter.formatNfeStatus("Authorized"), "Autorizada");
	assert.strictEqual(formatter.formatNfeStatus("Rejected"), "Rejeitada");
	assert.strictEqual(formatter.formatNfeStatus("Denied"), "Denegada");
});

QUnit.test("cor da situação", function (assert) {
	assert.strictEqual(formatter.stateNfeStatus("Authorized"), "Success");
	assert.strictEqual(formatter.stateNfeStatus("Rejected"), "Error");
	assert.strictEqual(formatter.stateNfeStatus("Denied"), "Error");
	assert.strictEqual(formatter.stateNfeStatus("Processing"), "Warning");
	assert.strictEqual(formatter.stateNfeStatus("None"), "None");
});
```

Em `webapp/test/unit/helpers/NfeHelpers.qunit.ts` (import de `nfeOutcomeMessage`):

```ts
QUnit.module("NfeHelpers - desfecho da emissão");

QUnit.test("autorizada e confirmada é sucesso", function (assert) {
	assert.deepEqual(nfeOutcomeMessage({ NfeStatus: "Authorized", InvoiceStatus: "Confirmed" }),
		{ type: "success", text: "NF-e autorizada e documento confirmado." });
});

QUnit.test("autorizada com confirmação falha pede Concluir confirmação", function (assert) {
	const message = nfeOutcomeMessage({ NfeStatus: "Authorized", ConfirmationError: "Liberação sem saldo." });
	assert.strictEqual(message.type, "warning");
	assert.ok(message.text.includes("Liberação sem saldo."));
	assert.ok(message.text.includes("Concluir confirmação"));
});

QUnit.test("rejeitada mostra código e motivo", function (assert) {
	assert.deepEqual(nfeOutcomeMessage({ NfeStatus: "Rejected", StatusCode: "209", Reason: "IE do emitente inválida" }),
		{ type: "error", text: "NF-e rejeitada: 209 - IE do emitente inválida" });
});

QUnit.test("rejeição local, sem código, mostra só o motivo", function (assert) {
	assert.strictEqual(nfeOutcomeMessage({ NfeStatus: "Rejected", Reason: "Rejeitada na validação local" }).text,
		"NF-e rejeitada: Rejeitada na validação local");
});

QUnit.test("em processamento é aviso com o motivo", function (assert) {
	assert.deepEqual(nfeOutcomeMessage({ NfeStatus: "Processing", Reason: "Sem resposta da SEFAZ — use Consultar situação." }),
		{ type: "warning", text: "Sem resposta da SEFAZ — use Consultar situação." });
});

QUnit.test("denegada é erro", function (assert) {
	assert.strictEqual(nfeOutcomeMessage({ NfeStatus: "Denied", StatusCode: "302", Reason: "Uso Denegado" }).type, "error");
});
```

- [ ] **Step 2: Run tests to verify they fail**

QUnit filtrando por "situação da NF-e" e "desfecho". Expected: FAIL.

- [ ] **Step 3: Formatter e helper**

Em `webapp/model/formatter.ts`, ao lado de `stateSalesInvoiceStatus`:

```ts
  /** Situação da NF-e STANDALONE. "None" (nunca emitida) fica em branco. */
  formatNfeStatus: (value: string): string => {
    const m = new Map<string, string>([
      ["Processing", "Em processamento"],
      ["Authorized", "Autorizada"],
      ["Rejected", "Rejeitada"],
      ["Denied", "Denegada"],
    ]);

    return m.get(value) ?? "";
  },

  stateNfeStatus: (value: string): string => {
    const m = new Map<string, string>([
      ["Processing", "Warning"],
      ["Authorized", "Success"],
      ["Rejected", "Error"],
      ["Denied", "Error"],
    ]);

    return m.get(value) ?? "None";
  },
```

Em `webapp/helpers/NfeHelpers.ts`:

```ts
export type NfeOutcome = {
  NfeStatus: string;
  InvoiceStatus?: string;
  StatusCode?: string;
  Reason?: string;
  AccessKey?: string;
  ConfirmationError?: string;
};

/** Mensagem do desfecho de emitir/consultar/concluir (o servidor devolve 200 com o desfecho). */
export function nfeOutcomeMessage(outcome: NfeOutcome): { type: "success" | "warning" | "error"; text: string } {
  const codeAndReason = [outcome.StatusCode, outcome.Reason].filter(Boolean).join(" - ");

  switch (outcome.NfeStatus) {
    case "Authorized":
      return outcome.ConfirmationError
        ? {
          type: "warning",
          text: `NF-e autorizada, mas a confirmação do documento falhou: ${outcome.ConfirmationError} ` +
            "Corrija e use Concluir confirmação.",
        }
        : { type: "success", text: "NF-e autorizada e documento confirmado." };
    case "Rejected":
      return { type: "error", text: `NF-e rejeitada: ${codeAndReason}` };
    case "Denied":
      return { type: "error", text: `NF-e denegada: ${codeAndReason}` };
    default:
      return { type: "warning", text: outcome.Reason ?? "NF-e em processamento." };
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

QUnit filtrando pelos dois módulos. Expected: PASS.

- [ ] **Step 5: Detalhe — botões e quadro da NF-e**

Em `webapp/model/ServerRoutes.ts`:

```ts
  salesInvoicesIssueNfe: '/odata/SalesInvoicesIssueNfe',
  salesInvoicesConsultNfe: '/odata/SalesInvoicesConsultNfe',
  salesInvoicesCompleteNfeConfirmation: '/odata/SalesInvoicesCompleteNfeConfirmation',
  salesInvoicesNfeXml: '/odata/SalesInvoicesNfeXml',
  danfeReport: '/reports/Danfe',
```

Em `webapp/view/salesInvoices/Detail.view.xml`, nas `<uxap:actions>`, troque o botão "Confirmar" e acrescente os da NF-e:

```xml
          <!-- Na filial com a regra ativa (ui>/taxLocked), emitir é o que confirma o documento Normal. -->
          <Button text="Confirmar" type="Success" press=".onConfirm"
            visible="{= ${ui>/taxLocked} !== true || ${path: 'InvoiceType', targetType: 'any'} !== 'Normal' }"
            enabled="{= ${path: 'InvoiceStatus', targetType: 'any'}==='Pending' ? true : false }" />
          <Button text="Emitir NF-e" type="Success" icon="sap-icon://paper-plane" press=".onIssueNfe"
            visible="{= ${ui>/taxLocked} === true &amp;&amp; ${path: 'InvoiceType', targetType: 'any'} === 'Normal' }"
            enabled="{= ${path: 'InvoiceStatus', targetType: 'any'} === 'Pending' &amp;&amp; (${path: 'NfeStatus', targetType: 'any'} === 'None' || ${path: 'NfeStatus', targetType: 'any'} === 'Rejected') }" />
          <Button text="Consultar situação" icon="sap-icon://synchronize" press=".onConsultNfe"
            visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Processing' }" />
          <Button text="Concluir confirmação" type="Attention" press=".onCompleteNfeConfirmation"
            visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Authorized' &amp;&amp; ${path: 'InvoiceStatus', targetType: 'any'} === 'Pending' }" />
          <Button text="DANFE" icon="sap-icon://pdf-attachment" press=".onDanfe"
            visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Authorized' }" />
          <Button text="XML" icon="sap-icon://download" press=".onNfeXml"
            visible="{= ${path: 'NfeStatus', targetType: 'any'} === 'Authorized' }" />
```

(Os botões "Editar", "Estornar" e "Cancelar" ficam como estão — o servidor recusa o que a NF-e trava, com mensagem.)

Depois da seção "Dados do Documento de Saída", acrescente:

```xml
      <uxap:ObjectPageSection titleUppercase="false" title="NF-e"
        visible="{= ${path: 'NfeStatus', targetType: 'any'} !== 'None' }">
				<uxap:subSections>
					<uxap:ObjectPageSubSection title=" " titleUppercase="false">
						<core:Fragment fragmentName="siagrob1.view.salesInvoices.fragments.NfePanel" type="XML" />
					</uxap:ObjectPageSubSection>
				</uxap:subSections>
			</uxap:ObjectPageSection>
```

```xml
<!-- webapp/view/salesInvoices/fragments/NfePanel.fragment.xml -->
<core:FragmentDefinition
    xmlns="sap.m"
    xmlns:f="sap.ui.layout.form"
    xmlns:core="sap.ui.core"
>
  <f:SimpleForm editable="false" layout="ResponsiveGridLayout" labelSpanL="3" labelSpanM="4" columnsL="2" columnsM="1">
    <f:content>
      <Label text="Situação" />
      <ObjectStatus
        inverted="true"
        text="{ path: 'NfeStatus', targetType: 'any', formatter: '.formatter.formatNfeStatus' }"
        state="{ path: 'NfeStatus', targetType: 'any', formatter: '.formatter.stateNfeStatus' }" />
      <Label text="Chave de acesso" />
      <Text text="{ChaveNFe}" />
      <Label text="Número / Série" />
      <Text text="{TaxDocumentNumber} / {TaxDocumentSeries}" />
      <Label text="Protocolo" />
      <Text text="{NfeProtocol}" />
      <Label text="Autorizada em" />
      <Text text="{ path: 'NfeAuthorizedAt', type: 'sap.ui.model.odata.type.DateTimeOffset', formatOptions: { pattern: 'dd/MM/yyyy HH:mm' } }" />
      <Label text="Último retorno" />
      <Text text="{= ${NfeStatusCode} ? ${NfeStatusCode} + ' - ' + ${NfeStatusReason} : ${NfeStatusReason} }" />
      <Label text="Confirmação" visible="{= !!${NfeConfirmationError} }" />
      <MessageStrip type="Warning" showIcon="true" text="{NfeConfirmationError}" visible="{= !!${NfeConfirmationError} }" />
    </f:content>
  </f:SimpleForm>
</core:FragmentDefinition>
```

Em `webapp/controller/salesInvoices/Detail.controller.ts` (imports: `ServerRoutes`, `sendJson`, `odataValue` de `FetchHelpers`, `nfeOutcomeMessage`/`NfeOutcome` de `NfeHelpers`):

```ts
  async onIssueNfe() {
    const ctx = this.getView().getBindingContext() as Context;
    if (!ctx || !(await confirmDialog("Emitir a NF-e deste documento ?", "Emitir NF-e ?"))) {
      return;
    }

    await this.runNfeAction(ServerRoutes.salesInvoicesIssueNfe, ctx);
  }

  async onConsultNfe() {
    const ctx = this.getView().getBindingContext() as Context;
    if (ctx) {
      await this.runNfeAction(ServerRoutes.salesInvoicesConsultNfe, ctx);
    }
  }

  async onCompleteNfeConfirmation() {
    const ctx = this.getView().getBindingContext() as Context;
    if (ctx) {
      await this.runNfeAction(ServerRoutes.salesInvoicesCompleteNfeConfirmation, ctx);
    }
  }

  /**
   * Emitir/consultar/concluir: 400 traz a mensagem de pré-condição/prontidão; 200 traz o desfecho
   * (autorizada, rejeitada, denegada, em processamento). O documento é relido nos dois casos.
   */
  private async runNfeAction(url: string, ctx: Context) {
    this.setBusy(true);
    try {
      const result = await sendJson("POST", url, { Key: ctx.getProperty("Key") as string });

      if (!result.ok) {
        MessageBox.error(result.message);
        return;
      }

      const message = nfeOutcomeMessage(odataValue<NfeOutcome>(result.data));
      if (message.type === "success") {
        MessageToast.show(message.text);
      } else if (message.type === "warning") {
        MessageBox.warning(message.text);
      } else {
        MessageBox.error(message.text);
      }
    } finally {
      ctx.refresh();
      this.setBusy(false);
    }
  }

  async onDanfe() {
    const ctx = this.getView().getBindingContext() as Context;
    const response = await fetch(`${ServerRoutes.danfeReport}/${ctx.getProperty("Key") as string}/print`, { method: "POST" });

    if (!response.ok) {
      MessageBox.error(await response.text() || "Falha ao gerar o DANFE.");
      return;
    }

    const fileURL = URL.createObjectURL(await response.blob());
    window.open(fileURL, "_blank");
    setTimeout(() => URL.revokeObjectURL(fileURL), 60000);
  }

  async onNfeXml() {
    const ctx = this.getView().getBindingContext() as Context;
    const response = await fetch(`${ServerRoutes.salesInvoicesNfeXml}(Key=${ctx.getProperty("Key") as string})`);

    if (!response.ok) {
      MessageBox.error(await response.text() || "Falha ao baixar o XML.");
      return;
    }

    const link = document.createElement("a");
    link.href = URL.createObjectURL(await response.blob());
    link.download = `${ctx.getProperty("ChaveNFe") as string}-procNFe.xml`;
    link.click();
    setTimeout(() => URL.revokeObjectURL(link.href), 60000);
  }
```

- [ ] **Step 6: Lista — coluna e "Informar Nota Fiscal"**

Em `webapp/view/salesInvoices/Main.view.xml`, depois da coluna "Chave NF-e":

```xml
            <t:Column label="Situação NF-e" width="9rem" visible="{ui>/standalone}">
              <t:template>
                <ObjectStatus
                  text="{ path: 'NfeStatus', targetType: 'any', formatter: '.formatter.formatNfeStatus' }"
                  state="{ path: 'NfeStatus', targetType: 'any', formatter: '.formatter.stateNfeStatus' }" />
              </t:template>
            </t:Column>
```

Em `webapp/controller/salesInvoices/Main.controller.ts`:

1. No `onInit`, troque o callback do `attachPatternMatched` por `() => { void this.refreshStandaloneFlag(); this.applyFilters(); }`.
2. Em `onNotaFiscal()` (já é `async`), logo depois do bloco que exige um registro selecionado, mova para cima a linha `const ctx = table.getContextByIndex(selectedInvoice[0]);` (apagando-a de onde está hoje, depois do `createDialog`) e acrescente, antes do `createDialog`:

```ts
    // Na filial que emite NF-e pelo Siagro, número/série/chave vêm da emissão (o servidor também
    // recusa). Pergunta a mesma regra que trava a linha, pela filial da linha escolhida.
    if ((ctx.getProperty("NfeStatus") as string) !== "None") {
      MessageBox.information("Número, série e chave deste documento vêm da emissão da NF-e pelo Siagro.");
      return;
    }

    await this.refreshTaxLock(ctx.getProperty("BranchCode") as string);
    if ((this.getModel("ui") as JSONModel).getProperty("/taxLocked") === true) {
      MessageBox.information("Na filial que emite NF-e pelo Siagro, número, série e chave vêm da emissão.");
      return;
    }
```

3. Em `webapp/view/salesInvoices/Main.view.xml`, o `NfeStatus` e o `BranchCode` são lidos no código: acrescente-os ao `$select` da tabela → `$select: 'ShipmentLoadKey,WithoutTaxDocument,NfeStatus,BranchCode'`.

- [ ] **Step 7: Conferências estáticas e QUnit**

Run: `yarn ts-typecheck && yarn lint && yarn ui5lint`
Expected: sem erro novo. QUnit inteiro verde.

- [ ] **Step 8: Commit**

```bash
git -C siagro-b1-frontend commit -m "feat(invoice): emitir, consultar e baixar a NF-e pelo documento de saída" -m "Na filial com a regra ativa, Emitir NF-e toma o lugar de Confirmar no documento Normal; aparecem Consultar situação (em processamento), Concluir confirmação (autorizada com a confirmação pendente), DANFE e XML (autorizada), e um quadro com situação, chave, protocolo e último retorno. A lista ganha a coluna Situação NF-e e o Informar Nota Fiscal avisa quando a filial emite pelo Siagro.

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01A5s7KYMUmPkURDz2JzgMv6" -- webapp
```

---

### Task 21: Verificação final

**Files:** nenhum arquivo novo de produção. Ajustes que a verificação revelar entram com teste e commit próprios, seguindo o padrão das tarefas anteriores.

- [ ] **Step 1: Backend inteiro**

Run (em `siagro-b1-backend/`, sem `SiagroB1.Web` rodando):
`dotnet build SiagroB1.sln 2>&1 | grep -E " error |Build succeeded" | sort -u`
`dotnet test SiagroB1.Fiscal.Tests 2>&1 | tail -3`
`dotnet test SiagroB1.Application.Tests 2>&1 | tail -3`
Expected: `Build succeeded`; as duas suítes `Passed!`. Anote os totais para o relatório.

- [ ] **Step 2: Frontend inteiro**

Run (em `siagro-b1-frontend/`): `yarn ts-typecheck && yarn lint && yarn ui5lint`, e o QUnit inteiro na página do navegador.
Expected: sem erro; QUnit todo verde (o `yarn test` tem o gate de cobertura irreal já conhecido — não use como critério).

- [ ] **Step 3: Migrations num banco de verificação**

As migrations novas: `CreateMunicipalities`, `AddBranchIssuerFields`, `AddBusinessPartnerNfeFields`, `CreatePaymentConditions`, `CreateBranchNfeSettings`, `AddSalesInvoiceNfeIssuance` (AppDbContext) e `AddMenuItemStandaloneOnly` (CommonDbContext).

- Aplique as do `AppDbContext` só na cópia de verificação (`IDX_SIAGRO_DEV_GAC1171`, a do sub-projeto 1), com `ASPNETCORE_ENVIRONMENT` explícito e a connection string sobrescrita por variável de ambiente (`ConnectionStrings__SiagroDB`).
- A do `CommonDbContext` muda o banco **comum** (menus), que é compartilhado: **pergunte ao usuário antes de aplicar**. Ela só acrescenta coluna com default e dois itens marcados `StandaloneOnly`.
- Não aplique nada no `IDX_SIAGRO_DEV` sem autorização explícita.

- [ ] **Step 4: Roteiro STANDALONE sem certificado real (na cópia)**

Suba Web (com `Erp=STANDALONE`, `ConnectionStrings__SiagroDB` da cópia e `Nfe__CertificateKey` com uma chave de teste: `[Convert]::ToBase64String((1..32 | % { [byte](Get-Random -Max 256) }))`), Gateway (`Erp=STANDALONE`), Reports e `yarn start:dev`; login conforme a memória do projeto. Confira, pelo caminho do usuário (menu → tela):

1. Menu "Cadastros" mostra "Condições de Pagamento" e "Configuração da NF-e".
2. Condição "30/60 boleto": prévia de R$ 1.000,00 dá 500,00 + 500,00 com os vencimentos certos; dias "60,30" recusados com mensagem.
3. Filial 3 (a do sub-projeto 1): razão social, IE, endereço, município por pesquisa (UF diferente da filial é recusada), CEP.
4. Parceiro C90002: indicador 1 sem IE é recusado; com IE grava; endereço de faturamento com município — cidade e UF passam a ser as do município depois de salvar.
5. Documento Pendente da filial 3: "Emitir NF-e" no lugar de "Confirmar"; "Informar Nota Fiscal" na lista avisa. Sem configuração/certificado, a emissão mostra UMA mensagem listando o que falta.
6. Configuração da NF-e: enviar um .pfx de teste com CNPJ de outra raiz é recusado com a mensagem; sem `Nfe__CertificateKey`, o envio diz o que configurar.
7. Confirmação direta pela API (`POST /odata/SalesInvoicesConfirm`) do documento Normal da filial 3: recusada com "Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e."

- [ ] **Step 5: Roteiro com certificado A1 real (quando o usuário fornecer o .pfx e o credenciamento em homologação)**

1. Configuração da NF-e: ambiente Homologação, série/número de teste, enviar o certificado, "Testar comunicação" → `107 - Serviço em Operação`. Se falhar com erro de TLS por cadeia do servidor, registre e teste `Nfe__ValidateSefazCertificate=false` (decisão P15).
2. Emitir o documento: autorizada; o destinatário no XML é `NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL`; documento Confirmado; DANFE abre em PDF com a marca de homologação; XML baixa como `<chave>-procNFe.xml`.
3. Forçar rejeição (IE do destinatário inválida): documento segue Pendente com o motivo; corrigir e reenviar reaproveita o número.
4. "Consultar situação": simule a falta de resposta derrubando a rede depois do envio, ou use um documento que ficou em processamento; a consulta autoriza e confirma.

Sem o certificado real, registre no relatório que a verificação parou no XSD (risco §14 do spec).

- [ ] **Step 6: Roteiro SAPB1 (Web e Gateway com `Erp=SAPB1`)**

Nenhum menu novo; filial, parceiro e documento sem campos novos; o documento mostra "Confirmar" e confirma como hoje (inclusive com `IssuesNfe = 1` gravado na filial); `GET /odata/PaymentConditions` responde 400 "só existem no modo STANDALONE".

- [ ] **Step 7: Relatório e memória**

Escreva o relatório da execução (o que foi entregue, migrations aplicadas e onde, decisões tomadas, o que não foi verificado) e atualize a memória `nfe-standalone-emission-feature.md` com o estado do sub-projeto 2a (branches e topo de cada um, migrations, pendências). Não faça merge nem push.
