# Carta de Correção Eletrônica (CC-e) — Documento de Saída e Documento de Entrada própria (NF-e STANDALONE) — design

Data: 06/10/2026. Branch `feature/nfe-correction-letter` nos dois repos, criado de `main` (que já contém a cadeia
da NF-e STANDALONE e o cancelamento, `feature/nfe-cancellation`).
Base: spec `2026-10-02-nfe-standalone-issuance-design.md` (D1 reservou CC-e para o sub-projeto **2b**) e spec
`2026-10-05-nfe-cancellation-design.md`, cujo molde (serviço genérico, trava `nfe:{key}`, consulta reconciliando o
evento) este spec segue. Cobre **só a CC-e**; inutilização e e-mail continuam fora.

## 1. O pedido

"Implementar carta de correção eletrônica para doc de saída e entrada própria." A CC-e é o evento **110110** da
SEFAZ, enviado sobre uma NF-e autorizada, com um texto livre de correção (`xCorrecao`) e o texto fixo de condição
de uso (`xCondUso`, preenchido pela Zeus).

Regras do leiaute (NT 2011.003 e MOC), que valem para o desenho:

- `xCorrecao` com 15 a 1000 caracteres.
- `nSeqEvento` de 1 a 20 por NF-e. **Cada CC-e substitui a anterior**: a nova deve repetir todas as correções
  ainda válidas.
- Não corrige: valores e impostos (base, alíquota, diferença de preço, quantidade, valor), dados cadastrais que
  mudem o remetente ou o destinatário, data de emissão ou de saída. A SEFAZ não valida o conteúdo do texto: a
  responsabilidade é do emitente, e o aviso vai na tela.

Sucesso: na filial que emite NF-e, uma saída ou entrada própria autorizada (inclusive devoluções) recebe uma ou mais
CC-e pela tela; cada carta registrada fica no histórico do documento com sequência, texto, protocolo, data, usuário
e o XML do evento; cada carta pode ser baixada em XML e impressa em PDF.

## 2. Decisões (do usuário, em 06/10)

| # | Decisão | Motivo |
|---|---|---|
| D1 | **A CC-e só registra.** Texto livre enviado à SEFAZ e guardado no histórico; nenhum campo do documento muda; status, saldos e travas da NF-e autorizada continuam iguais. | Padrão de mercado (é o que o EfisCloud faz). Alterar campos exigiria destravar seletivamente uma NF-e autorizada. |
| D2 | **Imprimir a CC-e** em PDF pelo layout da Zeus (`NFeEvento.frx` + `DanfeFrEvento.cs`, LGPL, sem alteração), copiado para `ThirdParty/Zeus-LGPL` como o DANFE. | O destinatário e o transportador recebem a carta impressa. |
| D3 | **Abordagem A:** tabela filha por documento (`SALES_INVOICE_NFE_CORRECTIONS`, `PURCHASE_INVOICE_NFE_CORRECTIONS`), uma linha por carta registrada, com o `procEventoNFe` na própria linha. | Histórico consultável sem ler XML; cada carta com seu XML e seu PDF; pronto para a escrituração futura. |

Decisões técnicas propostas e aceitas junto com o desenho:

| # | Decisão |
|---|---|
| T1 | Sem regra local de prazo nem de conteúdo: a SEFAZ recusa (prazo, NF-e cancelada etc.) e a mensagem dela vai para a tela, como no cancelamento. |
| T2 | Só cartas **registradas** viram linha. Recusa ou falha de comunicação não grava nada; a sequência não é consumida. |
| T3 | Sequência = maior sequência registrada do documento + 1. Limite local de 20 ("Limite de 20 cartas de correção atingido para esta NF-e."). |
| T4 | 573 (evento duplicado: a sequência já foi registrada, ex.: resposta perdida) → consulta a NF-e e importa as CC-e que faltam. |
| T5 | "Consultar situação" de NF-e autorizada importa as CC-e registradas na SEFAZ e ausentes no banco (cartas perdidas por timeout ou emitidas fora do Siagro). Usuário das importadas: "Consulta SEFAZ". |
| T6 | Entrada **de terceiro** fica fora: a NF-e é do fornecedor ("NF-e de terceiro não recebe carta de correção pelo Siagro."). |
| T7 | Sem trava de perfil nova — segue a emissão e o cancelamento; "perfil nas ações" continua pendência antes da VPS. |
| T8 | Texto só com trim (como a justificativa do cancelamento); quebras de linha viram espaço (o `xCorrecao` não aceita CR/LF no padrão da SEFAZ). |
| T9 | O diálogo abre **pré-preenchido com o texto da última carta**, para o usuário acrescentar em vez de redigir tudo de novo. |

## 3. Escopo

**Dentro:** evento 110110 no cliente Fiscal e os XSD dele; extração das CC-e na consulta; tabelas, entidades e
serviço genérico com subclasses de saída e entrada própria; importação na consulta; actions e leitura do histórico
no OData; download do XML; PDF no Reports; botão, diálogo e tabela "Cartas de correção" no detalhe da saída e da
entrada.

**Fora:** coluna ou filtro de CC-e nas listas; e-mail da carta; escrituração (SPED, Reinf); CC-e de NF-e de
terceiro; alterar dados do documento (D1).

## 4. Isolamento

Só documento com `NfeStatus = Authorized` e ativo enxerga a ação — isso só existe na filial que emite NF-e pelo
Siagro (CEAGUI). Yokotobi (SAPB1) e MH Agro não têm documento autorizado: o botão não aparece e as tabelas ficam
vazias.

## 5. Modelo de dados

Duas entidades com o mesmo formato (interface `INfeCorrection` no Domain):

| Coluna | Tipo | Conteúdo |
|---|---|---|
| `Key` | UNIQUEIDENTIFIER PK | |
| `SalesInvoiceKey` / `PurchaseInvoiceKey` | UNIQUEIDENTIFIER FK NOT NULL | documento (Restrict, como os XMLs) |
| `Sequence` | INT NOT NULL | `nSeqEvento` (1–20) |
| `Text` | NVARCHAR(1000) NOT NULL | `xCorrecao` enviado/registrado |
| `Protocol` | VARCHAR(20) NOT NULL | `nProt` do evento |
| `RegisteredAt` | DATETIME2 NOT NULL | `dhRegEvento` em hora de Brasília (mesmo tratamento de `NfeCancelledAt`) |
| `StatusCode` | INT NOT NULL | cStat do `retEvento` (135) |
| `Reason` | VARCHAR(255) NOT NULL | `xMotivo` |
| `ProcEventXml` | NVARCHAR(MAX) NOT NULL | `procEventoNFe` (evento assinado + `retEvento`) |
| `CreatedAt` / `CreatedBy` | | quem registrou ("Consulta SEFAZ" na importação) |

Índice único (documento, `Sequence`). Exposição no OData somente leitura (GET); POST/PATCH/DELETE recusados.

`INfeDocumentStore<T>` ganha o acesso às cartas: `CorrectionsAsync(key)` (sequências registradas) e
`AddCorrection(document, correction)`.

## 6. Fiscal (`SiagroB1.Fiscal/Nfe`)

- `INfeSefazClient.SendCorrectionAsync(NfeCorrectionRequest, NfeServiceSettings, CancellationToken)`.
  `NfeCorrectionRequest(AccessKey, Sequence, Text, IssuerDocument, EventAt)` — `EventAt` em Brasília (−03:00),
  convertido do relógio do servidor como no cancelamento.
- `ZeusNfeSefazClient` chama `ServicosNFe.RecepcaoEventoCartaCorrecao(idLote 1, sequência, chave, texto, cnpj,
  dhEvento)` pelo `RunAsync` existente; mapeia com `NfeSefazResponseMapper.FromEvent`. O `NfeEventResult` ganha
  dois campos opcionais no fim, `Sequence` (`nSeqEvento`) e `CorrectionText` (`detEvento.xCorrecao`); os usos do
  cancelamento não mudam.
- `FromConsult` passa a devolver também `Corrections`: os `procEventoNFe` com `tpEvento 110110` e `retEvento`
  registrado (135/155), com sequência, texto, protocolo, data e XML. Vale para qualquer cStat da consulta
  (100 autorizada, 101 cancelada).
- `NfeStatusCodes`: `IsEventRegistered(code) => code is 135 or 155` (o `IsCancellationRegistered` atual passa a
  usá-lo).
- XSD: copiar de `C:\Projetos\EfisCloud\backend\schemas` para `SiagroB1.Fiscal/Schemas` os arquivos da CC-e
  (`envCCe_v1.00.xsd`, `leiauteCCe_v1.00.xsd`, `CCe_v1.00.xsd`, `procCCeNFe_v1.00.xsd`, `retEnvCCe_v1.00.xsd` e
  dependências que ainda faltarem). ⚠️ Lição do cancelamento: sem eles a Zeus lança `FileNotFoundException` antes
  do envio, e os testes com SEFAZ simulada não pegam — conferir qual arquivo a Zeus abre e cobrir com teste que
  valide o evento real contra o XSD.

## 7. Application

### 7.1 `NfeCorrectionServiceBase<T>` (+ `SalesInvoicesNfeCorrectionService`, `PurchaseInvoicesNfeCorrectionService`)

1. Texto: CR/LF → espaço, trim; 15–1000 caracteres ("O texto da correção deve ter entre 15 e 1000 caracteres.").
2. Trava `sp_getapplock` `nfe:{key}` (a mesma da emissão, consulta e cancelamento).
3. Pré-condições: documento existe (404); `NfeStatus = Authorized` ("Só NF-e autorizada recebe carta de
   correção."); chave de 44 dígitos; documento não cancelado; entrada com emissão Própria (T6); menos de 20 cartas
   (T3).
4. Sequência = máx. registrada + 1; abre o certificado com `BranchNfeSettingsService.OpenAsync(branch,
   doc.NfeEnvironment)` e chama `SendCorrectionAsync`.
5. Resultado:
   - **135/155** → grava a linha (`AddCorrection`) e `SaveChanges`; mensagem "Carta de correção nº {seq}
     registrada na SEFAZ.".
   - **573** → consulta (§7.2); se a sequência enviada aparecer registrada, sucesso com ela importada; senão,
     recusa com a mensagem da SEFAZ.
   - **Outro** → nada gravado; 400 "Carta de correção recusada pela SEFAZ: {cStat} - {motivo}".
   - **`NfeCommunicationException`** → nada gravado; 400 "Sem resposta da SEFAZ na carta de correção: use Consultar
     situação antes de tentar de novo.".
6. Retorno: `NfeIssueOutcomeDto` (o frontend usa `nfeOutcomeMessage`).

O campo `NfeStatusCode/NfeStatusReason` do documento **não** é sobrescrito pela CC-e (continua mostrando o retorno
da autorização ou do cancelamento).

### 7.2 Importação na consulta

`NfeConsultServiceBase`, no caminho de NF-e autorizada (e no de 101 cancelada), importa as `Corrections` da resposta
cujas sequências não existam no documento (T5). Idempotente: consultar duas vezes não duplica (índice único +
filtro prévio). A importação não muda nenhum outro campo e não interfere no reconhecimento do cancelamento.

### 7.3 Leitura

- XML: `CorrectionXmlAsync(key, sequence)` → `ProcEventXml`; sem a carta, 404.
- O histórico vem pelo OData (§8).

## 8. Web (OData) e Reports

Ações no padrão de `Web/Actions/Nfe/` (`DefaultException` → 400, `NotFoundException` → 404):

- `SalesInvoicesSendNfeCorrection(Key, Text)` / `PurchaseInvoicesSendNfeCorrection(Key, Text)` → `NfeIssueOutcomeDto`.
- Functions `SalesInvoicesNfeCorrectionXml(Key, Sequence)` / `PurchaseInvoicesNfeCorrectionXml(Key, Sequence)` →
  arquivo `{chave}-cce-{seq}-procEventoNFe.xml`.
- Entity sets somente leitura `SalesInvoiceNfeCorrections` / `PurchaseInvoiceNfeCorrections` (filtrados pelo
  documento na tela). ⚠️ Rota de navegação OData precisa ser declarada à mão — por isso entity set próprio com
  `$filter` em vez de navegação.

Reports: `DanfeController` ganha `GET` do PDF da carta (saída e entrada, por documento + sequência);
`DanfeReportService` monta `DanfeFrEvento(nfeProc, procEventoNFe, ConfiguracaoDanfeNfe(logo), desenvolvedor,
caminho absoluto do NFeEvento.frx)`. Os arquivos `NFeEvento.frx` (em `NFe.Danfe.Base/NFe/`) e `DanfeFrEvento.cs`
(em `NFe.Danfe.OpenFast/NFe/`) são copiados sem alteração do commit `08743cd5f5b82769a26ad9ae0093b1b0ae60c74f` já
registrado no README da pasta; o `.frx` vai para a saída do build/publish como o `NFeRetrato.frx`. Arquivo
`{chave}-cce-{seq}.pdf`.

## 9. Telas (frontend)

- **`webapp/dialogs/NfeCorrectionDialog.ts`** (compartilhado): `TextArea` com contador (15–1000), pré-preenchido
  com o texto da última carta (T9); botão "Enviar carta" habilitado a partir de 15; aviso: "A nova carta substitui
  as anteriores: repita todas as correções. Não corrige valores, impostos, quantidades, emitente/destinatário nem
  datas. O envio à SEFAZ não pode ser desfeito."
- **Detalhe da saída e da entrada:**
  - Botão "Carta de Correção": visível com `NfeStatus = Authorized` e documento ativo (entrada: emissão Própria).
  - Tabela "Cartas de correção" na área da NF-e (visível quando houver cartas): Seq., Registrada em, Protocolo,
    Texto, Usuário; ações por linha "XML" e "Imprimir". Recarrega após o envio e após "Consultar situação".
- `ServerRoutes`: rotas novas (action, functions, PDF).

## 10. Migration

Uma migration aditiva no App (`AddNfeCorrectionLetters`): as duas tabelas. Aplicar no `CEAGUI_SIAGRO_DEV`; entra na
fila de migrations do deploy do `main`.

## 11. Testes e verificação

TDD com RED visto. Gates: build 0 erros, `SiagroB1.Application.Tests`, `SiagroB1.Fiscal.Tests`, testes do Reports,
`tsc`, `lint`, ui5lint sem regressão.

- **Fiscal:** mapeamento do retorno (135 com procEvento, recusa, status do `retEvento` e não do lote); `FromConsult`
  extraindo CC-e (com e sem cartas, junto com cancelamento); evento CC-e validando contra os XSD copiados.
- **Application** (xUnit + InMemory, `INfeSefazClient` falso), saída e entrada: texto curto/longo/com quebra de
  linha; pré-condições (não autorizada, cancelada, terceiro, 20 cartas) **sem chamar a SEFAZ**; sequência 1, depois
  2; 135 grava a linha com XML; recusa e falha de comunicação sem gravar; 573 → consulta importa e devolve sucesso;
  consulta de autorizada importa as cartas ausentes sem duplicar; status da NF-e do documento inalterado.
- **Reports:** PDF da carta gerado a partir de procNFe + procEventoNFe de teste.
- **E2E em homologação** (`CEAGUI_SIAGRO_DEV`, Playwright, pelo caminho do usuário): uma saída e uma entrada própria
  autorizadas recebem CC-e nº 1 e nº 2 (pré-preenchimento visto); XML e PDF baixados e conferidos; "Consultar
  situação" não duplica. Cada envio à SEFAZ de homologação pede confirmação do usuário antes.

## 12. Riscos

- **Timeout com o evento registrado:** a carta fica fora do banco até a consulta ou o próximo envio (573 → consulta).
  A mensagem orienta consultar.
- **Consulta sem os eventos:** se a SEFAZ-SP não devolver os `procEventoNFe` no `retConsSitNFe`, o 573 não se
  resolve sozinho; a carta continua válida na SEFAZ, e o XML pode ser obtido depois por distribuição DF-e (fora
  daqui). Conferir no E2E.
- **Conteúdo proibido:** a SEFAZ não valida o texto; o aviso no diálogo é a única barreira (responsabilidade do
  emitente).
- **Layout da Zeus:** o `NFeEvento.frx` pode depender de script com tipos barrados pelos stubs do FastReport — os
  stubs já estão desligados no startup (`DisableScriptStubs`); conferir no teste do Reports.

## 13. Próximas etapas (fora daqui)

Inutilização de numeração; e-mail da NF-e, do cancelamento e da CC-e; escrituração fiscal; coluna/filtro de CC-e nas
listas; perfil nas ações de NF-e.
