# Inutilização do número da NF-e rejeitada — desenho

Data: 2026-10-08 · Repos: `siagro-b1-backend` + `siagro-b1-frontend` · Branch: `feature/nfe-number-void`

## 1. Contexto e objetivo

Em 08/10/2026 a NF-e nº 233 (série 1) da CEAGUI foi rejeitada (929) e o documento de saída
DS000007 foi cancelado em seguida. O número nunca foi autorizado e ficou sem uso: pela legislação
ele precisa ser **inutilizado** na SEFAZ até o dia 10 do mês seguinte (aqui, até 10/11/2026).
Nem o SiagroB1 nem o EfisCloud fazem inutilização hoje.

Objetivo: uma ação **"Inutilizar numeração"** no documento (saída e entrada própria) que pede a
inutilização do número dele à SEFAZ e guarda o comprovante.

Decisões do usuário (brainstorming 08/10/2026):

- **No próprio documento**, não uma tela de faixa na Configuração da NF-e (fica fora do escopo).
- **Ação separada**: o cancelamento do documento não muda e não oferece a inutilização.
- **Saída e entrada própria.**
- Abordagem **1**: seguir o molde do cancelamento da NF-e (sem tabela nova).
- Respostas 256/563 viram "Inutilizada sem comprovante" (§3.3).

## 2. Quem pode ser inutilizado

A ação existe quando **todas** valem (mesma regra no backend e na tela):

1. `InvoiceStatus == Cancelled` (o documento já foi cancelado pelo caminho normal);
2. `NfeStatus == Rejected` — **Processing** fica de fora (pode ter sido autorizada sem o sistema
   saber: consulte antes), **Denied** também (o número já está registrado na SEFAZ);
3. o documento tem `TaxDocumentNumber` e `TaxDocumentSeries`;
4. entrada: só a **própria** (a de terceiro não é emitida pelo Siagro — `EnsureIssuer`, como no
   cancelamento).

A justificativa é digitada pelo usuário, 15 a 255 caracteres depois do `Trim` (mesmos limites e
mensagem do cancelamento). A trava é a da emissão (`NfeNumberReservationService.AcquireEmissionLockAsync`):
um segundo pedido simultâneo do mesmo documento é recusado com "em andamento".

## 3. Pedido à SEFAZ

### 3.1 Montagem

Serviço `NfeInutilizacao4`, faixa de um número só:

| Campo | Origem |
|---|---|
| `cUF` | UF da filial (a mesma da emissão) |
| `ano` | "AA" da chave da tentativa rejeitada (`ChaveNFe[2..4]`); sem chave, ano corrente de Brasília |
| `CNPJ` | CNPJ da filial |
| `mod` | 55 |
| `serie` | `TaxDocumentSeries` |
| `nNFIni` = `nNFFin` | `TaxDocumentNumber` |
| `xJust` | justificativa digitada |
| `tpAmb` | `invoice.NfeEnvironment` — o ambiente da EMISSÃO, não o atual da filial |

Certificado e configuração via `BranchNfeSettingsService.OpenAsync(branch, invoice.NfeEnvironment)`,
como no cancelamento.

### 3.2 Fiscal

- `NfeVoidNumberRequest(StateCode, Year, TaxId, Series, Number, Justification)`.
- `INfeSefazClient.VoidNumberAsync(request, settings)` → `NfeVoidNumberResult(StatusCode, Reason,
  Protocol, Xml)`; `Xml` é o `procInutNFe` (pedido assinado + retorno) quando há homologação.
- Implementação no `ZeusNfeSefazClient` com o `NfeInutilizacao` da Zeus.
- XSDs oficiais `inutNFe_v4.00`, `leiauteInutNFe_v4.00`, `retInutNFe_v4.00`, `procInutNFe_v4.00`
  (cópia dos do EfisCloud, PL vigente) em `SiagroB1.Fiscal/Schemas`, publicados com o resto.
- `NfeStatusCodes`: `NumberVoided = 102`, `IsNumberAlreadyVoided(code) => code is 256 or 563`.

### 3.3 Respostas

| Resposta | Efeito no documento | Mensagem |
|---|---|---|
| **102** (homologada) | `NfeStatus = Voided`, `NfeStatusCode/Reason` do retorno, `NfeProtocol` = protocolo da inutilização, XML guardado com `NfeXmlKind.NumberVoid` | sucesso |
| **256** (já inutilizada) / **563** (pedido repetido) | `NfeStatus = Voided`, código e motivo gravados, **sem XML** (a SEFAZ não devolve o protocolo antigo e não há consulta de inutilização) | aviso "inutilizada, comprovante não disponível" |
| outra recusa (ex.: 241 número já utilizado) | nada muda | "Inutilização recusada pela SEFAZ: código - motivo" |
| sem resposta / falha de comunicação / erro inesperado (certificado, TLS, XSD) | nada muda | "Sem resposta da SEFAZ na inutilização: tente de novo." (+ detalhe técnico no erro inesperado, como no cancelamento) — reenviar é seguro: no pior caso volta 256/563 |

A `ChaveNFe` da tentativa rejeitada fica no documento (histórico); o DANFE não existe para ela.

## 4. Backend

- **Domain:** `NfeStatus.Voided = 6` ("Inutilizada"); `NfeXmlKind.NumberVoid = 5` (procInutNFe).
  Enums gravados como inteiro, sem restrição no banco → **sem migration**.
- **Application** (`Services/Nfe/`):
  - `NfeVoidNumberServiceBase<TDocument>(store, settingsService, sefaz, reservation, logger)` —
    regras da §2, montagem da §3.1, tratamento da §3.3; grava pelo `INfeDocumentStore`
    (`AddXml` + `SaveChanges`). `EnsureIssuer` virtual, como no cancelamento.
  - `SalesInvoicesNfeVoidNumberService` e `PurchaseInvoicesNfeVoidNumberService` (esta recusa a
    entrada de terceiro).
  - `SalesInvoicesNfeVoidNumberXmlDownloadService` e `PurchaseInvoicesNfeVoidNumberXmlDownloadService`
    — `LatestXmlAsync(key, NumberVoid)`; sem XML → `NotFoundException`
    ("Esta inutilização não tem comprovante."). Arquivo `<cnpj><serie><numero>-procInutNFe.xml`.
  - Retorno da action: o `NfeIssueOutcomeDto` já usado (NfeStatus, StatusCode, Reason).
- **Web:** actions `SalesInvoicesVoidNfeNumber` e `PurchaseInvoicesVoidNfeNumber`
  (`Key: Guid`, `Justification: string`), funções de download
  `SalesInvoicesNfeVoidNumberXml(Key=…)` / `PurchaseInvoicesNfeVoidNumberXml(Key=…)`; registro
  em `ODataConfigurations` e `AddApplicationServices`. Recusa de regra → 400 com a mensagem;
  documento inexistente → 404.
- **Permissão:** a mesma do cancelamento da NF-e (sem papel novo).
- Onde o código hoje decide pelo `NfeStatus` (travas, `isEmittedNfeStatus`, estornos), `Voided` se
  comporta como **número queimado sem nota válida**: documento continua cancelado e travado; nada
  reabre. Conferir no plano cada `switch`/comparação sobre `NfeStatus`.

## 5. Frontend

- `NfeHelpers.canVoidNfeNumber(nfeStatus, invoiceStatus, issuerType?)` → `invoiceStatus ===
  "Cancelled" && nfeStatus === "Rejected" && (issuerType === undefined || issuerType === "Own")`.
- Documento de Saída e Documento de Entrada, **Detail** e **lista** (menu da linha, como o
  cancelamento):
  - botão **"Inutilizar numeração"** visível por `canVoidNfeNumber`; abre o diálogo de
    justificativa do cancelamento (mesmo contador 15–255, título próprio); resultado com
    `nfeOutcomeMessage` (102 = sucesso, 256/563 = aviso);
  - situação da NF-e "Inutilizada" (texto/estado no formatter que já existe);
  - botão **"XML da inutilização"** no Detail quando `NfeStatus === "Voided"` e há comprovante
    (o 404 sem comprovante vira a mensagem do backend).
- `ServerRoutes`: as quatro rotas novas. Textos pt-BR no i18n.

## 6. Testes

TDD, RED visto antes de cada implementação.

- **Fiscal.Tests:** montagem do pedido (ano da chave e fallback, série, número, CNPJ, ambiente);
  XML do pedido válido no XSD `inutNFe_v4.00`; leitura do retorno 102 / 256 / 563 / recusa.
- **Application.Tests** (InMemory, `INfeSefazClient` simulado), saída **e** entrada:
  cada regra da §2 recusa; 102 grava status, protocolo e XML; 256/563 grava Voided sem XML;
  recusa e falta de resposta não mudam o documento; download com e sem comprovante; entrada de
  terceiro recusada.
- **QUnit:** `canVoidNfeNumber`.
- **Homologação E2E** (CEAGUI_SIAGRO_DEV, série 9), pela tela: documento de saída rejeitado →
  cancelar → inutilizar = 102 + XML baixável; reenvio (status restaurado no banco) = 256/563;
  o mesmo com uma entrada própria. A forma de provocar a rejeição sai no plano.

## 7. Fora do escopo

- Inutilização de faixa pela Configuração da NF-e (opção B do brainstorming).
- Oferecer a inutilização no cancelamento.
- Inutilização da NF-e 233 em produção: feita pelo usuário, pela tela, depois do deploy
  (Web + Gateway com o frontend).
