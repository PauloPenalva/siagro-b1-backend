# Cadastros de transporte — sub-projeto 2

Sub-projeto 2 de `2026-10-21-transport-and-managerial-accounting-design.md`. O sub-projeto 1
(centro de custo, item contábil e rateio) já está em `main`.

## Por que existe

A **COSTA E COSTA TRANSPORTES LTDA** (CNPJ 09.109.848/0001-28) emite CT-e no EfisCloud e quer
migrar para o SiagroB1. Os sub-projetos 3 (CT-e) e 4 (MDF-e) precisam de veículo, conjunto,
motorista e seguro já cadastrados, para que o documento só **escolha** essas informações em vez
de digitá-las. Este sub-projeto entrega esses cadastros. Ele **não emite** nenhum documento fiscal.

### O que a operação real mostra (EfisCloud, empresa 3, lido em 22/10/2026)

- **CT-e:**
  - 17.836 CT-e no total, cerca de 30 por dia útil e 6.986 em 2026;
  - em produção na série 2, com o último número em 82.393;
  - todos em SP (UF 35); ICMS CST 00 a 12%;
  - tomador típico é o destinatário.
- **Averbação:** 92% dos CT-e foram averbados na AT&M (averba.com.br), com a apólice da Allianz,
  ramo 55 e responsável pelo seguro = emitente.
- **MDF-e:** só 2 rascunhos de 2025, nunca transmitidos. O cliente vai precisar do manifesto por
  causa do **CIOT**, mas ainda não deu sequência (palavra do usuário).
- **Veículos (74):**
  - 36 cavalos, 4 trucks e 1 toco;
  - 33 cadastrados como "OUTROS", que são, na prática, as carretas (20 delas com carroceria baú);
  - nenhum tem tara ou capacidade;
  - RNTRC preenchido em 42.
- **Motoristas (75):**
  - todos têm CPF, e quase todos têm categoria e validade da CNH;
  - nenhum tem número da CNH;
  - endereço quase vazio.
- **Ligação no CT-e:** cada CT-e guarda **um único** veículo. A carreta nunca é ligada a ele.

## Objetivos

1. Ampliar o **veículo** (`Truck`, `TRUCKS`) com os dados que o CT-e e o MDF-e exigem.
2. Criar o **conjunto** (`TruckCombination`): uma tração com até 3 reboques.
3. Ampliar o **motorista** (`TruckDriver`, `TRUCK_DRIVERS`) com CPF e CNH.
4. Criar a **configuração de transporte da filial**: RNTRC da empresa, seguro padrão e as
   credenciais da AT&M para a averbação.

## Fora de escopo

- **Importar os 74 veículos e os 75 motoristas do EfisCloud** (decisão do usuário: não é
  necessário agora).
- **Vínculo do motorista** (funcionário, agregado ou terceiro): o usuário decidiu que ele não deve
  pesar no desenho. O motorista é só quem dirige.
- **A averbação em si** e os campos de averbação no documento ficam no sub-projeto 3. A averbação
  **continua manual** (decisão do usuário: nem todo CT-e é averbado), com botão "Averbar".
- **Folha de pagamento, manutenção e custo do veículo** (o veículo vira centro de custo pelo
  sub-projeto 1, na amarração da conta).
- **CIOT, vale-pedágio e pagamento de frete:** são dados do MDF-e (sub-projeto 4), não deste
  cadastro.

## Anti-objetivos (falha mesmo que funcione)

- Quebrar a **balança, o romaneio, a carga e a ordem de expedição**, que já usam `TRUCKS` e
  `TRUCK_DRIVERS` (`WeighingTicket`, `StorageTransaction`, `ShipmentLoad`, `ShippingOrder`,
  `SalesInvoice`, `PurchaseInvoice`), inclusive em modo SAPB1 (Yokotobi).
- Tornar obrigatório um campo novo e travar a gravação dos veículos e motoristas já cadastrados.
  Os dados de transporte só são **exigidos na emissão** do CT-e e do MDF-e (sub-projetos 3 e 4),
  como `TareWeight` já faz com a pesagem.
- Copiar os erros do EfisCloud:
  - **seguro com nome e CNPJ da seguradora trocados:** `MdfeSeguradora` mapeia
    `nome_responsavel_seguro` → `cnpjSeguradora` e `cnpj_seguradora` → `nomeSeguradora`;
  - **senha da AT&M em texto puro** na configuração;
  - **reboque sem lugar próprio**, cadastrado como "OUTROS";
  - **CT-e com um só veículo.**

## Restrições

- Veículo e motorista existem nos dois modos (STANDALONE e SAPB1), porque a balança usa ambos.
  Os campos novos também aparecem em SAPB1 (são cadastro local, não vêm do SAP).
- A **configuração de transporte da filial** e o **conjunto** só existem em STANDALONE
  (`ErpMode.IsStandalone`), como a configuração de NF-e.
- Migrations **aditivas**, com colunas novas anuláveis. Elas rodam em **todas as empresas** da
  COMMON, porque o Web recusa subir com migration pendente.
- Identificadores em inglês e textos de tela em pt-BR.

## Desenho (o que foi decidido)

### Veículo — `Truck` ampliado

O código continua sendo a placa (`Code`, VARCHAR(10)). Campos novos, todos anuláveis:

| Campo | Tipo | Observação |
|---|---|---|
| `Renavam` | VARCHAR(11) | |
| `Kind` | enum `TruckKind`: `Traction` / `Trailer` | Padrão `Traction` para os existentes |
| `WheelType` | enum (tpRod do MDF-e/CT-e): 01 Truck, 02 Toco, 03 Cavalo mecânico, 04 VAN, 05 Utilitário, 06 Outros | Só tração; reboque não tem tpRod |
| `BodyType` | enum (tpCar): 00 Não aplicável, 01 Aberta, 02 Fechada/Baú, 03 Graneleira, 04 Porta-container, 05 Sider | |
| `TareWeight` | já existe (kg) | Exigido na emissão do MDF-e |
| `CapacityKg` | int | |
| `CapacityM3` | int | |
| `StateKey` | já existe | UF de licenciamento |
| `OwnerType` | enum `TruckOwnerType`: `Own` / `ThirdParty` | Padrão `Own` |
| `OwnerCardCode` | parceiro (`BusinessPartner`) | Obrigatório quando `ThirdParty`. Em SAPB1 não há navegação para o parceiro do SAP |
| `OwnerRntrc` | VARCHAR(8) | RNTRC do proprietário terceiro |
| `OwnerKind` | enum (tpProp): 0 TAC Agregado, 1 TAC Independente, 2 Outros | Só quando `ThirdParty` |
| `Inactive` | bit | Some dos value help de emissão; histórico intacto |

As tabelas de rodado e carroceria seguem os códigos do manual do MDF-e 3.00 e do CT-e 4.00, que
são os mesmos nos dois documentos.

### Conjunto — `TruckCombination` (novo, só STANDALONE)

- **Cabeçalho:** código, descrição, tração (`TruckCode`, só `Kind = Traction`), motorista padrão
  (opcional) e inativo.
- **Linhas:** até **3 reboques** (limite do MDF-e), com ordem, cada um um `Truck` com
  `Kind = Trailer`.
- O mesmo reboque pode estar em mais de um conjunto, porque a carreta troca de cavalo. Isso fica
  **sem trava**, por decisão minha.
- O CT-e e o MDF-e escolhem o **conjunto** (ou só uma tração sem reboque). O documento grava uma
  **cópia** das placas usadas, para que mudar o conjunto depois não altere documento emitido.

### Motorista — `TruckDriver` ampliado

- **O CPF é o próprio `Code`** (VARCHAR(11), decisão do usuário), que a balança e a carga já usam.
  O CT-e e o MDF-e leem o CPF do `Code`. O dígito é validado só na **inclusão** de motorista novo,
  e os códigos antigos fora do padrão não travam a edição. O CT-e e o MDF-e recusam o motorista
  com CPF inválido.
- `Document` continua **texto livre** (CNH ou RG), sem validação.
- Campos novos, anuláveis:

| Campo | Tipo |
|---|---|
| `LicenseNumber` | VARCHAR(11) |
| `LicenseCategory` | VARCHAR(2), ex.: E, AE, AD |
| `LicenseExpiresOn` | date |
| `Inactive` | bit |

- **Aviso, não trava:** a lista e o value help mostram a CNH vencida, em vermelho, ou a vencer em
  30 dias. O bloqueio na emissão é decisão do sub-projeto 3.

### Configuração de transporte da filial — `BranchTransportSettings` (novo, só STANDALONE)

Uma tabela por filial, separada de `BranchNfeSettings`:

- **Empresa transportadora:** `CompanyRntrc` (RNTRC da filial, exigido no CT-e e no MDF-e).
- **Seguro padrão:**
  - responsável (`InsuranceResponsible`: 1 Emitente, 2 Contratante);
  - CNPJ e nome da seguradora em **campos separados e nomeados corretamente**;
  - número da apólice.

  Esses dados preenchem o CT-e e o MDF-e, que podem alterá-los.
- **Averbação AT&M:**
  - `AverbaEnabled`;
  - `AverbaUser`;
  - `AverbaPasswordCipher` (VARBINARY), cifrada com o mesmo mecanismo da senha do certificado
    (AES-GCM, chave no appsettings) e **nunca devolvida pela API**;
  - `AverbaCode` (código da empresa na AT&M);
  - `AverbaEnvironment` (produção / homologação).
- **Botão "Testar conexão"**, que faz só o login na AT&M (`POST /rest/Auth`) e mostra o resultado.
  Nenhum CT-e é enviado.

### Telas

Seguem o padrão atual: lista com FilterBar, ObjectPage no detalhe.

- **Veículos** e **Motoristas:** substituem as telas simples atuais (`view/veiculo`,
  `view/motorista`). Mantêm-se no menu onde estão hoje, e as seções novas aparecem nos dois modos.
- **Conjuntos:** tela nova, só STANDALONE.
- **Transporte da filial:** seção ou aba na tela da filial, onde já estão as configurações de
  NF-e, só STANDALONE.
- **Menu:** migration de menu na COMMON, para os itens novos.

## Reboque nas telas atuais (decisão do usuário, 22/10)

Os value help de veículo da **balança, do romaneio, da carga, da ordem de expedição e das notas
de entrada e saída** mostram **só tração** (`Kind = Traction`) e escondem os inativos, nos dois
modos. A carreta só aparece no cadastro de conjunto (e, no futuro, no CT-e e no MDF-e). Os
documentos já gravados com uma placa qualquer continuam abrindo normalmente.

## Decisões do revisor de desenho (minhas, 22/10)

- **`Kind` e `OwnerType`:** colunas NOT NULL com default (`Traction` / `Own`), preenchidas pela
  migration nos registros existentes.
- **Proprietário terceiro** (`OwnerType`, `OwnerCardCode`, `OwnerRntrc`, `OwnerKind`): só aparece
  em STANDALONE. Em SAPB1 fica oculto e não há versão SAP do value help do parceiro.
- **Senha da AT&M:** cifrada pelo mesmo `CertificatePasswordCipher` (`Nfe:CertificateKey`) que já
  protege a senha do e-mail em `EmailSettingsService`. Não há configuração nova no deploy.
  - Salvar com o campo vazio mantém a senha gravada.
  - O "Testar conexão" usa a senha digitada; se o campo estiver vazio, usa a gravada.
  - As URLs da AT&M (produção e homologação) ficam no appsettings.
- **Transporte da filial:** seção nova em `view/branchs/fragments/Form.fragment.xml`, onde já fica
  a configuração da NF-e. Não precisa de item de menu. A migration de menu cobre só o
  **Conjuntos**.

## O que fica para quem constrói

- Leiaute exato das telas, nome das classes e ordem das tarefas.
- Forma do enum no banco (int ou string), seguindo o padrão dos enums já usados no `Truck`/NF-e.
- Se o "Testar conexão" fica num serviço `AverbaClient` já preparado para o sub-projeto 3 (sugerido).

## Para o sub-projeto 4 (registro, não muda este)

O CIOT, até aqui fora do MDF-e, passa a ser o motivo do manifesto para este cliente. Ele deve
entrar no escopo do sub-projeto 4.
