# NF-e STANDALONE — sub-projeto 2a: emissão da NF-e de saída (autorização, consulta, DANFE)

**Data:** 2026-10-02
**Branch:** `feature/nfe-standalone-issuance` (backend e frontend), criado a partir de
`feature/nfe-standalone-taxation` (sub-projeto 1, ainda sem merge).
**Depende de:** `2026-10-01-nfe-standalone-taxation-design.md` (sub-projeto 1) — a regra de ativação, a
natureza fiscal e a fotografia dos tributos na linha vêm de lá.

## 1. O pedido

O usuário quer emitir NF-e em **homologação** para validar o sub-projeto 1, e depois em produção, para a
CEAGUI (cerealista LTDA, Itaberá/SP). Requisito explícito: em homologação a razão social do destinatário
tem de ser o literal da SEFAZ.

**Verificado:** a biblioteca Zeus **não** troca o nome sozinha (o texto não existe em nenhuma DLL do
pacote e a documentação não mostra tratamento). O literal oficial (NT 2011/002, rejeição 598) é **sem
acento**: `NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL`.

## 2. Decisões do usuário (fechadas no brainstorming)

| # | Decisão |
|---|---|
| D1 | Sub-projeto 2 dividido: **2a** = emitir e consultar (este spec); **2b** = cancelamento, CC-e, inutilização, e-mail. |
| D2 | **Emitir é o que confirma.** Na filial com a regra ativa, "Confirmar" vira "Emitir NF-e": envia a partir do documento Pendente; autorizada ⇒ confirma; rejeitada ⇒ continua Pendente e editável. |
| D3 | Cadastro de **parceiro** com todos os campos que a NF-e exige (destinatário e transportadora). Todos os campos novos deste sub-projeto **só fazem sentido em STANDALONE** — ocultos e não validados em SAPB1. |
| D4 | **Certificado A1 por upload na tela, por filial**; o .pfx no banco e a senha cifrada (AES-GCM) com chave que só existe no `appsettings` do servidor. |
| D5 | **Cadastro de condição de pagamento** inspirado no SE4 do Protheus — só o tipo **"Dias"** (parcelas iguais), com meio de pagamento e início da contagem. Percentuais e "informada no documento" ficam para depois. |
| D6 | Abordagem: **emissão própria e síncrona** com a Zeus, com estado "Em processamento" + "Consultar situação" para timeout. |
| D7 | Parte fiscal num **projeto próprio da solução, `SiagroB1.Fiscal`** (biblioteca pura, sem EF/banco); o motor do sub-projeto 1 migra para lá sem mudar comportamento. |
| D8 | **DANFE pelo layout FastReport da Zeus** (`NFeRetrato.frx`, LGPL-2.1), trazido para o `SiagroB1.Reports` **sem alteração**, numa pasta identificada. |
| D9 | **Atualizar a Zeus** de `2026.7.3.2002` para `2026.9.24.1416` (schemas 010d v1.03 — CNPJ alfanumérico, NT 2026.004 — e correções). |

## 3. Escopo

**Entra:**
- Projeto `SiagroB1.Fiscal` (+ `SiagroB1.Fiscal.Tests`).
- Cadastros: emitente (filial), destinatário/transportadora (parceiro + endereço), municípios IBGE,
  condição de pagamento (cadastro + padrão no cliente + campo no documento).
- Configuração da NF-e por filial: certificado, ambiente, série, próximo número, "Testar comunicação".
- "Emitir NF-e", "Consultar situação", "Concluir confirmação", DANFE (PDF) e download do XML autorizado.

**Não entra:** tudo do 2b (cancelamento, CC-e, inutilização, e-mail); contingência; NF-e de devolução e de
complemento (`finNFe` ≠ 1 — devolução é o sub-projeto 3); exterior (país fixo Brasil); percentuais e
condição "informada no documento"; IPI/ST/DIFAL (fora desde o sub-projeto 1); CEST/GTIN ("SEM GTIN").

## 4. Isolamento (a mesma regra do sub-projeto 1)

Regra ativa = `Erp == STANDALONE` **e** `Branch.IssuesNfe` (`TaxCalculationGate`).

| Onde | Comportamento |
|---|---|
| Documento de saída, regra ativa | Botão "Emitir NF-e" no lugar de "Confirmar"; "Informar Nota Fiscal" oculto (número, série e chave vêm da emissão). |
| `SalesInvoicesConfirmService`, regra ativa | Documento **Normal** só confirma com `NfeStatus = Authorized` (é o que a emissão chama); a confirmação direta é recusada: "Na filial que emite NF-e pelo Siagro, confirme emitindo a NF-e." Devolução (`InvoiceType = Return`) segue como hoje. |
| Telas novas (Condições de Pagamento, Configuração da NF-e) | No menu, com a marca nova **`MENU_ITEMS.StandaloneOnly`**: o Gateway (que monta o menu e conhece o `Erp`) esconde esses itens fora do STANDALONE. Os controllers também recusam fora do STANDALONE. |
| Campos novos de filial/parceiro/documento | Ocultos em SAPB1 (`ui>/standalone`, positivo); validações de servidor só em STANDALONE. |
| MH Agro (STANDALONE, chave desligada) | Vê os cadastros novos; documento igual ao de hoje (sem emissão, "Confirmar" e "Informar Nota Fiscal" como antes). |

## 5. Projeto `SiagroB1.Fiscal`

Biblioteca de classe .NET 10 **sem EF e sem banco**: recebe registros de entrada, devolve resultados.
Referenciada por `Application` (orquestração) e `Reports` (DANFE). A dependência
`Zeus.Net.NFe.NFCe` sai do `SiagroB1.Infra` e passa a ser só do `Fiscal`.

| Pasta / componente | Responsabilidade |
|---|---|
| `Taxes/` | Motor do sub-projeto 1, movido de `Application/Services/Taxes`: `TaxCalculator`, `InterstateIcmsRate`, `FiscalCodes`, `TaxCalculationModels`, `UsageTaxationValidator`/`Mapper` (o que não depender de EF). `ErpMode` e `TaxCalculationGate` ficam na `Application` (leem configuração/banco). **Mover sem mudar comportamento: os mesmos testes passam antes e depois.** |
| `Payments/PaymentInstallmentCalculator` | Parcelas da condição "Dias" (§7.4). |
| `Nfe/NfeIssueInput` (records) | Tudo o que o XML precisa, já resolvido: emitente, destinatário, entrega, itens com a fotografia, transporte, parcelas, textos, responsável técnico, ambiente, série/número/cNF/dhEmi. |
| `Nfe/NfeAccessKey` | Monta a chave (cUF, AAMM, CNPJ, mod, série, nNF, tpEmis, cNF) e o `cDV`. **Aceita CNPJ alfanumérico** (NT 2026.004): nada de "só dígitos" no CNPJ. |
| `Nfe/NfeXmlBuilder` | `NfeIssueInput` → objeto `NFe` da Zeus (mapeamento da §8). |
| `Nfe/INfeSefazClient` + `ZeusNfeSefazClient` | `AuthorizeAsync(nfe, settings)` (síncrono, `indSinc=1`), `ConsultProtocolAsync(chave, settings)`, `ServiceStatusAsync(settings)`. Assina e valida no XSD antes de enviar. A interface é o ponto de simulação nos testes. |
| `Certificates/CertificatePasswordCipher` | AES-GCM com a chave `Nfe:CertificateKey` (base64, 32 bytes) — nonce + tag + texto cifrado. |
| `Certificates/CertificateInspector` | Abre .pfx + senha; devolve titular, CNPJ (OID ICP-Brasil `2.16.76.1.3.3`), validade, se tem chave privada. |
| `Schemas/` | XSDs oficiais do pacote **010d v1.03** (copiados do repositório da Zeus), publicados junto com o Web (`CopyToOutputDirectory`) e apontados em `ConfiguracaoServico`. |

## 6. Cadastros (todos só STANDALONE)

### 6.1 Filial — emitente (`BRANCHS`)

Já tem `TaxId` (CNPJ), `StateCode` (UF), `TaxRegime` (CRT), `IssuesNfe`. Colunas novas:

| Coluna | Tipo | XML |
|---|---|---|
| `LegalName` | VARCHAR(60) | `emit/xNome` (o `BranchName` atual é rótulo interno) |
| `TradeName` | VARCHAR(60) NULL | `xFant` |
| `StateRegistration` | VARCHAR(14) | `IE` |
| `Street`, `StreetNumber`, `Complement`, `District` | VARCHAR(60) (complemento NULL) | `xLgr`, `nro`, `xCpl`, `xBairro` |
| `MunicipalityCode` | VARCHAR(7) NULL, FK `MUNICIPALITIES` | `cMun`/`xMun`; `cUF` = 2 primeiros dígitos |
| `ZipCode` | VARCHAR(8) | `CEP` |
| `Phone` | VARCHAR(14) NULL | `fone` |

Validação de servidor só em STANDALONE: município escolhido ⇒ UF do município == `StateCode`.

### 6.2 Parceiro — destinatário e transportadora (`BUSINESS_PARTNERS` + `BUSINESS_PARTNERS_ADDRESSES`)

Já tem `TaxId` (CPF ou CNPJ), `CardName`, `CardFName`. Colunas novas no parceiro:

| Coluna | Tipo | XML |
|---|---|---|
| `StateRegistration` | VARCHAR(14) NULL | `dest/IE` (só com indicador 1) |
| `StateRegistrationIndicator` | INT NULL — enum `{ Taxpayer = 1, Exempt = 2, NonTaxpayer = 9 }` | `indIEDest`; `NonTaxpayer` ⇒ `ide/indFinal = 1` |
| `NfeEmail` | VARCHAR(250) NULL | `dest/email` |
| `Phone` | VARCHAR(14) NULL | `fone` |
| `PaymentConditionCode` | INT NULL (sem FK, padrão do documento) | — |

No endereço (já tem `Street`, `Block` = bairro, `ZipCode`, `City`, `State`, `Country`):

| Coluna | Tipo | XML |
|---|---|---|
| `StreetNumber` | VARCHAR(60) NULL | `nro` |
| `Complement` | VARCHAR(60) NULL | `xCpl` |
| `MunicipalityCode` | VARCHAR(7) NULL, FK `MUNICIPALITIES` | `cMun`/`xMun`; ao escolher, a tela preenche `City` e `State` |

A NF-e usa o endereço de **faturamento** (mesma escolha de `SalesInvoicesCfopResolveService.ResolvePartnerState`).
País fixo: `cPais = 1058`, `xPais = BRASIL`. CPF × CNPJ pelo tamanho do `TaxId` (11 = CPF).

### 6.3 Municípios (`MUNICIPALITIES`)

`Code` VARCHAR(7) PK (IBGE), `Name` VARCHAR(100), `StateAbbreviation` VARCHAR(2). Semente com os
**5.574** municípios oficiais a partir de `EfisCloud/backend/src/main/resources/ibge/cidades.txt`
(`código|nome|data`) + `uf.txt` (código IBGE da UF → sigla). Somente leitura (OData `Municipalities`,
para a pesquisa nas telas).

### 6.4 Condição de pagamento (`PAYMENT_CONDITIONS`)

| Coluna | Tipo | Significado |
|---|---|---|
| `Code` | INT IDENTITY PK | |
| `Name` | VARCHAR(100) | "30/60/90 boleto" |
| `Days` | VARCHAR(100) | dias separados por vírgula: `0`, `30`, `30,60,90` (inteiros ≥ 0, crescentes) |
| `StartRule` | INT — enum `{ IssueDate = 1, NextMonth = 2 }` | início da contagem: data de emissão ou "fora o mês" (1º dia do mês seguinte) |
| `PaymentMeans` | VARCHAR(2) | `tPag` da SEFAZ: `01` dinheiro, `03` cartão de crédito, `04` cartão de débito, `15` boleto, `16` depósito, `17` PIX, `18` transferência, `90` sem pagamento, `99` outros |
| `Inactive` | BIT | |

Documento de saída ganha `PaymentConditionCode` (INT NULL, sem FK). Na criação (inclusive faturamento de
romaneio), se vier vazio, recebe o padrão do cliente. Editável enquanto Pendente. **Obrigatório na
emissão.** Tela "Condições de Pagamento" (menu só STANDALONE), com prévia das parcelas para um valor e uma
data de exemplo.

### 6.5 Produto e veículo

Produto: nada novo (NCM e origem vêm do sub-projeto 1; `cEAN`/`cEANTrib` = "SEM GTIN"; `uTrib` = `uCom`).
Veículo: placa (`Truck.Code`) e UF (`Truck.StateKey` → `STATES.Abbreviation`) já existem.

## 7. Configuração da NF-e, certificado, numeração

### 7.1 `BRANCH_NFE_SETTINGS` (1:1 com a filial, só STANDALONE)

| Coluna | Tipo |
|---|---|
| `BranchCode` | VARCHAR(14) PK, FK `BRANCHS` |
| `Environment` | INT — enum `NfeEnvironment { Production = 1, Homologation = 2 }` |
| `Series` | INT (0–999) |
| `NextNumber` | INT (≥ 1) |
| `CertificatePfx` | VARBINARY(MAX) NULL |
| `CertificatePasswordCipher` | VARBINARY(512) NULL |
| `CertificateSubject` | VARCHAR(250) NULL |
| `CertificateTaxId` | VARCHAR(14) NULL |
| `CertificateValidUntil` | DATETIME2 NULL |
| `UpdatedAt`, `UpdatedBy` | auditoria |

Tabela própria para o blob não viajar em cada leitura de `BRANCHS` (que o SAPB1 também faz).

### 7.2 Tela "Configuração da NF-e" (menu só STANDALONE)

Escolha da filial; ambiente, série, próximo número; quadro do certificado (titular, CNPJ, validade, dias
para vencer; alerta abaixo de 30 dias); **"Enviar certificado"** (.pfx + senha; upload `multipart` por
`fetch` cru, como o anexo do contrato); **"Testar comunicação"** (status do serviço na SEFAZ); faixa fixa
"Ambiente de homologação — notas sem valor fiscal" quando for homologação.

### 7.3 Envio do certificado

Recusa com mensagem pt-BR: não é PFX / senha errada; sem chave privada; vencido; raiz do CNPJ (8
caracteres) diferente da raiz do `TaxId` da filial (permite certificado da matriz). Sem
`Nfe:CertificateKey` no servidor, envio e emissão recusam dizendo o que configurar. A senha nunca é
devolvida pela API.

### 7.4 Parcelas (`PaymentInstallmentCalculator`)

- Base = data de emissão (`IssueDate`) ou 1º dia do mês seguinte (`NextMonth`).
- Vencimento de cada parcela = base + dias (`0` = a própria base).
- Valor = `Round(total / n, 2)` em cada parcela; a **última** recebe `total − soma das anteriores`.
- `indPag` = 0 (à vista) se houver **uma** parcela vencendo até a data de emissão + 1 dia; senão 1 (a
  prazo). Meio `90` ⇒ sem parcelas, `vPag = 0`, sem `indPag`.

### 7.5 Numeração

- Na primeira tentativa de emissão, se o documento não tem número fiscal, reserva `NextNumber` da filial
  de forma **atômica** (`UPDATE … WITH (UPDLOCK, HOLDLOCK) OUTPUT`, padrão de
  `DocNumberSequenceService`) e grava em `TaxDocumentNumber` (9, zeros à esquerda) e `TaxDocumentSeries`.
  Serviço próprio (`NfeNumberReservationService`) com *fake* nos testes (o InMemory não roda SQL cru).
- Retentativa após rejeição **reaproveita** o número. `cNF` (8 dígitos aleatórios) gerado uma vez e
  guardado no documento; a chave é remontada a cada tentativa (o AAMM do `dhEmi` faz parte dela).
- Documento excluído com número reservado deixa lacuna — inutilização é 2b.

### 7.6 Ambiente e responsável técnico

- Homologação também confirma o documento (ciclo idêntico, para a validação ser fiel) ⇒ homologação roda
  em banco de teste (ex.: demo da CEAGUI). Trocar para produção exige ajustar série/próximo número.
- `infRespTec` do `appsettings`, `Nfe:TechnicalResponsible` (`Cnpj`, `Contact`, `Email`, `Phone`) — os
  dados da IDX, iguais para todos os clientes.

## 8. O XML (`NfeXmlBuilder`)

| Grupo | Origem / regra |
|---|---|
| `ide` | `cUF` (do município do emitente), `cNF`, `natOp` = `InvoiceOperationText` da natureza da **primeira linha** (vazio ⇒ `Name`; máx. 60), `mod` 55, `serie`, `nNF`, `dhEmi` = agora em America/Sao_Paulo, `dhSaiEnt` = `dhEmi`, `tpNF` 1, `idDest` 1 (mesma UF) / 2 (outra UF), `cMunFG` = município do emitente, `tpImp` 1, `tpEmis` 1, `cDV`, `tpAmb`, `finNFe` 1, `indFinal` (1 se destinatário `NonTaxpayer`), `indPres` 9, `indIntermed` 0, `procEmi` 0, `verProc` "SiagroB1 <versão>". |
| `emit` | Filial (§6.1); `CRT` de `TaxRegime`. |
| `dest` | Parceiro + endereço de faturamento. **Em homologação, `xNome` = `NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL`.** `IE` só com indicador 1; `email` se houver. |
| `entrega` | Quando `DeliveryCardCode` ≠ `CardCode`: CNPJ/CPF, nome, IE e endereço do parceiro do local de entrega. |
| `det/prod` | `cProd` ItemCode, `cEAN` SEM GTIN, `xProd`, `NCM`, `cBenef` (se houver), `CFOP`, `uCom` UoM, `qCom` Quantity, `vUnCom` UnitPrice, `vProd` Total, `cEANTrib` SEM GTIN, `uTrib`/`qTrib`/`vUnTrib` = comerciais, `indTot` 1. |
| `det/imposto/ICMS` | Pelo código gravado: `00` ICMS00, `20` ICMS20 (`pRedBC`), `40/41/50` ICMS40, `51` ICMS51 (`pRedBC`, `vICMSOp`, `pDif`, `vICMSDif`, `vICMS`), `90` ICMS90; CSOSN `102/103/300/400` ICMSSN102, `900` ICMSSN900. `orig` da fotografia; `modBC` 3 (valor da operação). |
| `det/imposto/PIS`, `COFINS` | CST `01/02` ⇒ `PISAliq`/`COFINSAliq`; `04–09` ⇒ `PISNT`/`COFINSNT`; `49` (e CSTs de entrada) ⇒ `PISOutr`/`COFINSOutr`. Sem grupo de IPI. |
| `det/imposto/IBSCBS` | Quando a linha tem `IbsCbsCst`: `CST`, `cClassTrib` e, para `000`/`200`, `gIBSCBS` (`vBC`, `gIBSUF`, `gIBSMun`, `vIBS`, `gCBS`, com `gRed`/`pAliqEfet` quando houver redução). `400`/`410` sem valores. Regras finas de preenchimento conferidas contra a NT 2025.002 vigente na implementação. |
| `total` | `ICMSTot` somado das linhas (`vNF` = `vProd`); `IBSCBSTot` quando houver IBS/CBS. |
| `transp` | `modFrete`: `Cif` 0, `Fob` 1, `Ter` 2, `None` 9. `transporta` da transportadora (CNPJ/CPF, nome, IE, endereço, município, UF). `veicTransp` (placa + UF) **só com `idDest = 1`**. `vol` com `pesoL` = `NetWeight` e `pesoB` = `GrossWeight`. |
| `cobr` | Se meio ≠ 90: `fat` (`nFat` = número, `vOrig` = `vLiq` = `vNF`, `vDesc` 0) + `dup` (`001`, `002`…, `dVenc`, `vDup`) de §7.4. |
| `pag` | `detPag`: `indPag` (§7.4), `tPag` = meio, `vPag` = `vNF` (meio 90: `vPag` 0). |
| `infAdic` | `infCpl` = `DefaultAdditionalInfo` distintos das naturezas do documento + `TaxPayerComments`; `infAdFisco` = `TaxComments`. |
| `infRespTec` | §7.6. |

## 9. Emissão e estados

### 9.1 Documento de saída — colunas novas

| Coluna | Tipo |
|---|---|
| `PaymentConditionCode` | INT NULL |
| `NfeStatus` | INT — enum `NfeStatus { None = 0, Processing = 1, Authorized = 2, Rejected = 3, Denied = 4 }` |
| `NfeEnvironment` | INT NULL |
| `NfeRandomCode` | VARCHAR(8) NULL (`cNF`) |
| `NfeProtocol` | VARCHAR(20) NULL |
| `NfeAuthorizedAt` | DATETIME2 NULL |
| `NfeStatusCode` | VARCHAR(4) NULL (último `cStat`) |
| `NfeStatusReason` | VARCHAR(500) NULL (último `xMotivo` ou mensagem local) |
| `NfeConfirmationError` | VARCHAR(500) NULL (autorizada mas confirmação falhou) |

`SALES_INVOICE_NFE_XMLS`: `Key` GUID PK, `SalesInvoiceKey` FK, `Kind` INT — enum `{ Signed = 1, Authorized = 2 }`
(2b acrescenta eventos), `Xml` NVARCHAR(MAX), `CreatedAt`. O assinado é gravado **antes** do envio (sem
ele não dá para montar o `procNFe` se a resposta se perder).

### 9.2 "Emitir NF-e" (`SalesInvoicesNfeIssueService`, action `SalesInvoicesIssueNfe(Key)`)

1. **Pré-condições:** regra ativa; `InvoiceType = Normal`; `InvoiceStatus = Pending`; `NfeStatus` ∈
   {`None`, `Rejected`}; toda linha calculada (fotografia presente: `Cfop`, `Ncm`, `CstIcms`, `CstPis`,
   `CstCofins`). Documento de antes da chave ⇒ recusa com "salve o documento para recalcular" (não
   recalcula na emissão).
2. **Prontidão do cadastro** (`NfeReadinessValidator`): **uma** mensagem listando tudo o que falta
   (emitente, configuração, certificado/validade, chave de cifra, destinatário, endereço/município,
   condição de pagamento, transportadora).
3. Reserva número/`cNF` se faltarem (§7.5); monta `NfeIssueInput` (`NfeIssueInputAssembler`).
4. Monta o `NFe`, **assina e valida no XSD**; erro ⇒ `Rejected` com a mensagem local, nada enviado.
5. **Grava antes de enviar:** `NfeStatus = Processing`, `ChaveNFe`, `NfeEnvironment`, XML assinado.
6. Envia (síncrono) e trata o retorno:

| Retorno | Ação |
|---|---|
| `100`/`150` autorizada | Grava XML autorizado (`procNFe`), protocolo, data; `NfeStatus = Authorized`; **salva**; depois chama `SalesInvoicesConfirmService` e salva. |
| Rejeição | `NfeStatus = Rejected`, `cStat`/`xMotivo`; documento segue Pendente e editável; número guardado. |
| Denegada (`110`, `301`, `302`, `303`) | `NfeStatus = Denied`; emissão bloqueada; número queimado. |
| Duplicidade (`204`, `539`) | Consulta pela chave e segue o resultado. |
| Timeout / comunicação | Fica `Processing`: "Sem resposta da SEFAZ — use Consultar situação." |

7. **Autorizada e confirmação falhou:** a NF-e (já gravada) é a verdade; `NfeConfirmationError` recebe a
   mensagem; botão **"Concluir confirmação"** (`SalesInvoicesCompleteNfeConfirmation(Key)`) tenta de novo.

### 9.3 "Consultar situação" (`SalesInvoicesConsultNfe(Key)`)

Consulta o protocolo pela chave: autorizada ⇒ monta `procNFe` (assinado guardado + `protNFe`) e segue o
caminho da autorização (inclusive a confirmação); "não consta na base" (`217`) ⇒ `Rejected` (pode
reenviar); outros ⇒ grava código/motivo e mostra.

### 9.4 Travas

- `Processing`: documento e linhas não editáveis; exclusão recusada.
- `Authorized`/`Denied`: exclusão recusada.
- Confirmação direta com a regra ativa: só com `Authorized` (§4).

## 10. DANFE e XML

- **DANFE:** endpoint no `SiagroB1.Reports` que lê o `procNFe` do banco e gera o PDF com o
  `NFeRetrato.frx` da Zeus (pasta `Reports/ThirdParty/Zeus-LGPL/`, arquivos **sem alteração**, com o
  aviso de licença), com o logo que o Reports já usa (`CompanyLogoPath`). Homologação sai com a marca
  "sem valor fiscal" do próprio layout.
- **XML:** download pelo Web, `<chave>-procNFe.xml`.

## 11. Telas (frontend)

| Tela | Mudança |
|---|---|
| Documento de saída — detalhe | Regra ativa: "Emitir NF-e" (no lugar de "Confirmar"), "Consultar situação" (Processing), "Concluir confirmação" (`NfeConfirmationError`), "DANFE" e "XML" (Authorized); bloco "NF-e" com situação, chave, protocolo, último retorno. "Informar Nota Fiscal" oculto. |
| Documento de saída — formulário | "Condição de pagamento" no cabeçalho (STANDALONE), com value help. |
| Documento de saída — lista | Coluna "Situação NF-e" (STANDALONE). |
| Configuração da NF-e (nova) | §7.2. |
| Condições de Pagamento (nova) | Lista + formulário + prévia das parcelas. |
| Filial | Campos da §6.1 (município por pesquisa). |
| Parceiro | Campos da §6.2; no grid de endereços, número, complemento e município (pesquisa que preenche cidade/UF); condição de pagamento padrão. |

## 12. Migrations

**App:** colunas de §6.1, §6.2, §6.4 (documento) e §9.1; tabelas `MUNICIPALITIES` (+ semente),
`PAYMENT_CONDITIONS`, `BRANCH_NFE_SETTINGS`, `SALES_INVOICE_NFE_XMLS`. Tudo anulável ou com default.
**Common:** `MENU_ITEMS.StandaloneOnly` (BIT default 0) + itens "Configuração da NF-e" e "Condições de
Pagamento" (marcados, concedidos ao perfil ADMIN). Filtro no serviço de menu do Gateway.

## 13. Testes e verificação

**`SiagroB1.Fiscal.Tests`:** `NfeXmlBuilder` (homologação troca `xNome`; `idDest`; `indFinal`; cada grupo
de ICMS/PIS/COFINS; IBS/CBS; totais; `veicTransp` só interno; `cobr`/`pag`; `entrega`); **XML assinado com
certificado de teste gerado no teste e validado no XSD oficial**; `PaymentInstallmentCalculator`;
`NfeAccessKey` (incl. CNPJ alfanumérico); `CertificatePasswordCipher` (ida e volta, chave errada falha);
`CertificateInspector`. Os testes do motor do sub-projeto 1 migram junto e continuam verdes.

**`SiagroB1.Application.Tests`** (SEFAZ por `INfeSefazClient` simulado): cada retorno da §9.2; `Processing`
gravado antes do envio; confirmação após autorização; `NfeConfirmationError` + "Concluir confirmação";
número reaproveitado; confirmação direta recusada com a regra ativa e aceita sem ela; travas da §9.4;
prontidão (mensagem única); certificado (cada recusa da §7.3); condição de pagamento padrão do cliente;
campos novos validados só em STANDALONE; filtro de menu `StandaloneOnly`.

**Verificação:** (1) sem certificado — testes acima, incluindo o documento do sub-projeto 1 montado e
validado no XSD; (2) com certificado A1 + credenciamento — em homologação, na cópia local ou na demo:
"Testar comunicação", emitir (autorizada, DANFE, XML), forçar rejeição (ex.: IE inválida), corrigir e
reenviar, "Consultar situação"; (3) SAPB1 — nenhum menu nem campo novo.

## 14. Riscos

- **Credenciamento e certificado** são pré-requisitos externos: sem eles, a verificação para no XSD.
- **Grupo IBS/CBS** ainda muda por NT; conferir a versão da NT 2025.002 no momento da implementação.
- **`veicTransp` só interno**: regra adotada para evitar rejeição em operação interestadual — confirmar com
  o contador se a CEAGUI precisa do veículo no XML interestadual.
- **Fuso:** `dhEmi` em America/Sao_Paulo (IANA, funciona em Linux e Windows com ICU).
- **LGPL:** os arquivos do DANFE ficam intactos e separados; alteração futura exige publicar o diff.
