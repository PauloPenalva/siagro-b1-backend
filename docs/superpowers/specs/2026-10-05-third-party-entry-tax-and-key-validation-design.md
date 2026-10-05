# Documento de Entrada de terceiro: natureza, cálculo e validação da chave na SEFAZ (CEAGUI) — design

Data: 05/10/2026. Branch `feature/third-party-entry-tax-and-key` nos dois repos, criado de `main` (que já tem a
emissão STANDALONE, a entrada própria, a devolução de compra e a devolução de entrada de terceiro).
Base: specs `2026-10-05-nfe-purchase-invoice-issuance-design.md` e `2026-10-05-nfe-third-party-purchase-return-design.md`.

## 1. O pedido

"Estou fazendo um documento de entrada normal de terceiros, não aparece o campo para eu selecionar a natureza de
operação. O documento de terceiros deve ter um comportamento semelhante ao próprio. Inclusive, o ambiente NF-e sendo
produção, validar se a chave de acesso está autorizada na SEFAZ."

Hoje, na filial que emite NF-e pelo Siagro, só a entrada de **emissão própria** mostra natureza, CFOP e tributos
("modo NF-e" = regra ativa E emissão própria). A de terceiro não calcula nada: quando importada do XML, copia a
tributação do fornecedor para a linha; quando digitada, fica sem tributação. A chave de acesso não é validada em
lugar nenhum além da unicidade.

Sucesso: na filial com a regra ativa, a entrada de terceiro Normal escolhe a natureza em cada linha e tem a
tributação calculada como a própria; o documento eletrônico (NF-e) exige uma chave coerente e, com a filial em
Produção, só confirma com a NF-e do fornecedor autorizada na SEFAZ.

## 2. Decisões (do usuário, em 05/10)

| # | Decisão | Motivo |
|---|---|---|
| D1 | **Natureza no terceiro Normal: igual à própria — o motor calcula tudo.** Natureza obrigatória (de Entrada) em cada linha; o motor calcula CFOP e todos os tributos por ela, inclusive no documento importado do XML (os valores do fornecedor deixam de ser copiados). | "O documento de terceiros deve ter um comportamento semelhante ao próprio." Um modelo de imposto só. |
| D2 | **Dois tipos de documento.** (1) **Eletrônico** ("tipo SPED", NF-e) e (2) **outro** (nota de serviço, nota de papel/talão…). | O fluxo de chave só faz sentido para o eletrônico. |
| D3 | **Eletrônico: chave obrigatória nos dois ambientes** (homologação e produção). | "Exigir em ambos." |
| D4 | **Eletrônico: consultar a SEFAZ só com a filial em Produção, no "Confirmar".** Nota cancelada, denegada ou inexistente não confirma. Salvar não consulta. | "Confirmar a chave apenas NF-e produção." O confirmar é quando o documento passa a valer. |
| D5 | **SEFAZ sem resposta: recusa e pede para tentar de novo.** | Nenhum documento eletrônico confirma sem a chave validada em Produção. |

**Efeito sobre a spec anterior.** A D1 da devolução de entrada de terceiro ("conferência pelo XML") fica
substituída: a devolução continua conferindo contra a tributação da linha de origem, mas essa tributação passa a
ser a calculada pelo motor na entrada. "Os tributos da devolução espelham a entrada" continua valendo — a entrada
agora é calculada pelo Siagro.

Decisões tomadas no desenho (aprovado pelo usuário em 05/10):

| # | Decisão | Motivo |
|---|---|---|
| R1 | O campo **"Tipo de documento"** (`TaxDocumentKind`: `Nfe` / `Other`) fica no cabeçalho, só para terceiro; importar XML grava `Nfe`; inclusão manual começa em `Nfe`. | Um campo explícito é mais claro que deduzir pela chave em branco. |
| R2 | A chave do eletrônico é conferida **localmente** ao salvar e ao confirmar: 44 dígitos, dígito verificador, modelo 55, CNPJ/CPF do emitente = o do fornecedor, número e série = os do documento (preenchidos a partir da chave quando em branco). | Pega erro de digitação sem depender da SEFAZ, nos dois ambientes (D3). |
| R3 | A consulta usa o **Consulta Situação da NF-e** (`consSitNFe`, `INfeSefazClient.ConsultProtocolAsync`, já usado para as nossas NF-e), dirigido à **SEFAZ autorizadora da UF da chave** (2 primeiros dígitos), com o certificado da filial. | Cliente existente; a UF do emitente pode ser outra. A Distribuição DF-e (nacional) fica para depois (§13). |
| R4 | A autorização confirmada é guardada: **protocolo** e **data/hora da consulta**, mostrados no Detail. | Rastro de que a chave foi validada. |

## 3. Escopo

Dentro:
- Natureza, CFOP, tributos e "Tributos do item" na entrada de terceiro **Normal**, como na própria (§6).
- Campo "Tipo de documento" (§5, §9) e regras da chave do eletrônico (§7).
- Consulta à SEFAZ no "Confirmar" com a filial em Produção (§8).
- A devolução de entrada de terceiro passa a conferir contra a tributação calculada (§6.3).

Fora:
- Devolução do cliente (terceiro + Devolução) — continua como está, sem natureza nem validação de chave.
- Tipo "Outro" (serviço, papel) — sem chave e sem consulta; natureza e cálculo valem para ele também (D1 é por
  documento de terceiro Normal, não por tipo).
- Conferir se a NF-e foi emitida **para a filial** (destinatário): o `consSitNFe` não devolve o destinatário (§12).
- Manifestação do destinatário e download do XML pela Distribuição DF-e (§13).

## 4. Isolamento

Tudo vale só na filial com a regra de tributação ativa (`TaxCalculationGate.IsActiveAsync(branch)`: STANDALONE +
`Branch.IssuesNfe`). Em SAPB1 (Yokotobi) e na filial que não emite pelo Siagro (MH Agro) o comportamento fica
idêntico ao de hoje: terceiro sem natureza, sem cálculo, sem exigência de chave, sem consulta.

## 5. Modelo de dados

### 5.1 `PURCHASE_INVOICES` — colunas novas

| Coluna | Tipo | Regra |
|---|---|---|
| `TaxDocumentKind` | `int NOT NULL`, default `0` | Enum `TaxDocumentKind { Nfe = 0, Other = 1 }`. Editável enquanto Pendente. Emissão própria é sempre `Nfe`. |
| `SupplierNfeProtocol` | `varchar(20) NULL` | Protocolo de autorização devolvido pela SEFAZ na consulta (§8). Só o servidor grava. |
| `SupplierNfeCheckedAt` | `datetime2 NULL` | Quando a consulta autorizou. Só o servidor grava. |

O serviço de atualização não copia `SupplierNfeProtocol`/`SupplierNfeCheckedAt` do que o cliente manda.

### 5.2 Migration `AddPurchaseInvoiceTaxDocumentKind`

Cria as três colunas e preenche `TaxDocumentKind` dos documentos existentes: emissão própria → `Nfe`; terceiro com
`ChaveNFe` de 44 caracteres → `Nfe`; demais → `Other`. Assim nenhum documento antigo pendente passa a exigir chave
que não tinha. Aplicada no `CEAGUI_SIAGRO_DEV` durante a verificação; no deploy, junto das demais (o Web recusa
subir com migration pendente).

### 5.3 Contratos

- EDM: a enum `TaxDocumentKind` e as três propriedades em `PurchaseInvoices`.
- Rascunho da importação (`PurchaseInvoiceDraftDto`): `TaxDocumentKind = Nfe`.

## 6. Natureza e cálculo no terceiro Normal

### 6.1 Cálculo

`PurchaseInvoicesTaxApplyService.Applies` passa a valer também para **terceiro + Normal** (hoje: só emissão própria
Normal e devolução própria). `ResolveUsageAsync` exige a natureza nas linhas dele com as mesmas regras da própria:
obrigatória, de Entrada (`UsageDirection.Incoming`), ativa. O motor calcula CFOP e tributos na direção
fornecedor → filial, como na entrada própria. Continua valendo o resto de `ApplyAsync`: só Pendente, nada depois de
NF-e autorizada/em processamento (que o terceiro nunca tem).

### 6.2 Importação e `nItem`

`PurchaseInvoiceSupplierTaxes` deixa de copiar a tributação do XML para a linha (o motor a sobrescreveria). Fica só
o que a devolução precisa: o `nItem` da linha, que continua vindo do rascunho da importação e sendo conferido contra
o XML guardado ao salvar ("Item X: o item N não existe na NF-e do fornecedor."). Linha incluída depois da
importação continua sem `nItem` (informado no "Devolver").

### 6.3 Devolução de entrada de terceiro

O código da devolução não muda. A conferência (`NfeReturnConference`) compara a devolução com a tributação da linha
de origem; essa tributação agora é a calculada pela natureza de entrada. Efeito: a origem **digitada**, que antes
saía sem conferência (não tinha tributação), passa a ser conferida também. Documentos confirmados antes desta
mudança ficam com o que já gravaram (importado: tributação do fornecedor; digitado: nenhuma).

## 7. Chave de acesso do documento eletrônico — conferência local

Vale para terceiro + Normal + `TaxDocumentKind.Nfe`, filial com a regra ativa, ao salvar (inclusão, alteração) e ao
confirmar, nos dois ambientes. Mensagens em pt-BR, nesta ordem:

| # | Regra | Mensagem |
|---|---|---|
| 1 | Chave informada | "Informe a chave de acesso da NF-e do fornecedor." |
| 2 | 44 dígitos (só algarismos) | "A chave de acesso tem 44 dígitos." |
| 3 | Dígito verificador (módulo 11, pesos 2–9 da direita sobre os 43 primeiros; resto 0/1 → 0) | "Chave de acesso inválida: o dígito verificador não confere." |
| 4 | Modelo (posições 21–22) = `55` | "A chave não é de NF-e (modelo 55)." |
| 5 | CNPJ/CPF do emitente (posições 7–20; CPF vem com três zeros à esquerda) = documento do fornecedor | "A chave é de outro emitente: o CNPJ/CPF não é o do fornecedor." |
| 6 | Série (23–25) e número (26–34) = os do documento, comparados como números; em branco no documento, são preenchidos a partir da chave, sem zeros à esquerda (como a importação grava `nNF`/`serie`) | "O número/série da chave não conferem com os do documento." |
| 7 | Chave livre (não usada por outro documento não cancelado) — hoje só na inclusão, passa a valer também na alteração | a mensagem atual de `EnsureChaveNFeIsFreeAsync` |

O documento do fornecedor vem do cadastro do parceiro (`IBusinessPartnerService`, o mesmo da importação).

## 8. Consulta à SEFAZ no "Confirmar"

### 8.1 Quando

No `PurchaseInvoicesConfirmService`, para terceiro + Normal + `TaxDocumentKind.Nfe`, filial com a regra ativa e
**ambiente NF-e da filial = Produção** (`BranchNfeSettings.Environment`). Antes, reaplica a conferência local (§7).
Em homologação só a conferência local roda.

### 8.2 Como

Serviço novo `SupplierNfeAuthorizationService` (`SiagroB1.Application/Services/Nfe`):
`BranchNfeSettingsService.OpenAsync(branchCode)` → configuração com o certificado da filial; troca a UF da
configuração pela UF da chave (código IBGE das posições 1–2 → sigla) — `NfeServiceSettings` é um record, basta
`with { IssuerState = uf }`; chama `INfeSefazClient.ConsultProtocolAsync(chave, settings)`.

### 8.3 Resultado

| `cStat` | Resultado |
|---|---|
| 100, 150 (autorizada) | Confirma; grava `SupplierNfeProtocol` (nProt) e `SupplierNfeCheckedAt`. |
| 101, 151, 155 (cancelada) | Recusa: "A NF-e do fornecedor está cancelada na SEFAZ." |
| 110, 301, 302, 303 (denegada) | Recusa: "A NF-e do fornecedor teve o uso denegado na SEFAZ." |
| 217 (não consta) | Recusa: "A chave de acesso não consta na SEFAZ." |
| qualquer outro, falha de rede, timeout | Recusa: "A SEFAZ não confirmou a NF-e do fornecedor ({cStat} – {motivo}). Tente novamente." (sem cStat quando não houve resposta: "A SEFAZ não respondeu. Tente novamente.") |

A consulta é feita antes de qualquer gravação: a recusa deixa o documento Pendente, como estava.

## 9. Telas (frontend)

- **Formulário:** campo "Tipo de documento" (Select: "NF-e (eletrônica)" / "Outro (serviço, papel/talão…)"),
  visível só em documento de terceiro, editável enquanto Pendente. Com "NF-e", a chave fica obrigatória
  (marcador e validação de formulário). Número, Série e Chave seguem editáveis no terceiro.
- **Itens:** natureza, CFOP e "Tributos do item" aparecem quando a filial calcula tributos e o documento é de
  emissão própria **ou** de terceiro Normal. Hoje a mesma flag (`nfeMode`) controla também o que só a emissão
  própria tem (travas de Número/Série/Chave, emissão); as duas coisas se separam: `nfeMode` (emite) continua como
  está e uma regra nova (calcula tributos), pura e testada, controla natureza/CFOP/tributos e a exigência da
  natureza ao salvar.
- **Importar XML:** o rascunho vem com "NF-e".
- **Detail:** com a autorização guardada, a seção da NF-e mostra "Autorizada na SEFAZ — protocolo {nProt} em
  {data/hora}" para o documento de terceiro.
- Erros do confirmar continuam no tratamento atual (mensagem do servidor).

## 10. Testes e verificação

Backend (xUnit, `SiagroB1.Application.Tests`):
- Terceiro Normal na filial ativa: natureza obrigatória e de Entrada; CFOP e tributos calculados; importado do XML
  com tributação do fornecedor diferente → fica a calculada; `nItem` preservado e conferido contra o XML.
- Filial sem a regra (SAPB1/IssuesNfe falso): terceiro igual a hoje (sem cálculo, sem exigência).
- Devolução do cliente (terceiro + Devolução): inalterada.
- Devolução de entrada de terceiro: origem digitada agora conferida; origem importada conferida contra o calculado.
- Chave (§7): cada regra com o seu teste (sem chave, 43 dígitos, letra, DV errado, modelo 65, CNPJ de outro, CPF com
  zeros, série/número divergentes, preenchimento a partir da chave, chave repetida na alteração); tipo "Outro" sem
  chave salva e confirma.
- Consulta (§8) com `INfeSefazClient` falso: 100 grava protocolo e data; 150; 101/151/155; 110/301; 217; outro cStat;
  exceção de rede; homologação não consulta; UF da configuração = UF da chave (ex.: chave 41… → PR).
- Migration: preenchimento do `TaxDocumentKind` dos três casos.

Frontend (QUnit): a regra "calcula tributos" (própria, terceiro Normal, devolução do cliente, filial inativa) e a
obrigatoriedade da chave por tipo.

Navegador (CEAGUI, homologação):
1. Importar o XML fictício do fornecedor → natureza por linha, CFOP e tributos calculados; salvar, confirmar
   (homologação: sem consulta, só a conferência local).
2. Digitar um terceiro "NF-e" com chave de DV errado → recusa; com "Outro" sem chave → salva e confirma.
3. "Devolver" de uma entrada de terceiro nova → conferência contra o calculado passa.
4. Consulta real em homologação de uma chave inexistente de outra UF (ex.: PR) com o certificado da CEAGUI: espera
   217 (não consta), que prova o roteamento pela UF da chave (226 = UF divergente seria roteamento errado).
5. Produção: só com o cliente falso (não há NF-e de fornecedor real de teste em produção).

## 11. Riscos

- **Roteamento por UF da chave.** O Zeus escolhe a URL pelo `cUF` da configuração; UFs atendidas pela SVRS/SVAN
  precisam cair no autorizador certo. Coberto pelo teste 4 do navegador; se falhar para alguma UF, a recusa mostra o
  cStat (ex.: 226) e nada é confirmado.
- **Destinatário não conferido.** Uma chave autorizada de NF-e emitida para outra empresa passaria na consulta.
  Mitigado em parte pela regra 5 (emitente = fornecedor); conferir o destinatário exige a Distribuição DF-e (§13).
- **SEFAZ fora trava confirmações** em Produção (D5, aceito pelo usuário).
- **Documentos importados antes desta mudança** guardam a tributação do fornecedor; se forem editados enquanto
  Pendentes, o motor recalcula e ela é substituída (é o comportamento pedido em D1).

## 12. Não-objetivos explícitos

Não conferir destinatário, valores ou itens da NF-e do fornecedor contra a SEFAZ; não baixar o XML da SEFAZ; não
registrar eventos (ciência/confirmação da operação).

## 13. Próximas etapas (fora daqui)

- Distribuição DF-e (`NFeDistribuicaoDFe`, ambiente nacional): consultar por chave como destinatário, baixar o XML
  autorizado e conferir o destinatário; abre caminho para a manifestação do destinatário.
- Validação de chave também na devolução do cliente.
