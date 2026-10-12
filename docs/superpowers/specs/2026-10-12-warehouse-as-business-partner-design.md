# Armazém como Parceiro de Negócio (STANDALONE)

Data: 12/10/2026 · Branch: `feature/warehouse-as-business-partner` (backend e frontend)

## 1. Problema

No modo STANDALONE o armazém vive na tabela local `WAREHOUSES` (código, nome, CNPJ). Esse
cadastro não serve para emitir NF-e: não tem IE, endereço, município, e-mail da NF-e. A remessa
para depósito em armazém de terceiro (`ShipmentReleasesCreateStorageRemittanceService`) já usa
o código do armazém da liberação (`DeliveryLocationCode`) como `CardCode` do destinatário da
NF-e — então hoje o armazém precisa existir **duas vezes**, em `WAREHOUSES` e em
`BUSINESS_PARTNERS`, com o mesmo código (foi o que o E2E da remessa fez com o `FTESTEPJ`).

O modo SAPB1 (Yokotobi) não tem o problema: lá o armazém é o parceiro do `OCRD` com
`QryGroup23 = 'Y'`. A CEAGUI é o primeiro cliente a emitir NF-e; passamos o STANDALONE a fazer
o mesmo que o SAPB1.

Hoje o STANDALONE é inconsistente: `QueryAll`/`GetByIdAsync`/`GetAllAsync` leem `WAREHOUSES`,
mas `LoadWarehousesAsync` (nomes, cidade/UF em saldos, recusa, transbordo, devolução) já lê
`BUSINESS_PARTNERS` com `QryGroup23 = 'Y'` — e como a inclusão de PN força `'N'`, essa parte
devolve vazio.

## 2. Objetivo e critérios de sucesso

- Em STANDALONE, **armazém = parceiro de negócio com `QryGroup23 = 'Y'`**. Um único cadastro,
  completo para NF-e.
- A remessa para depósito é emitida para um armazém cadastrado só como PN.
- Lotes, movimentos, contratos, liberações e cargas antigos continuam mostrando o armazém, sem
  regravar nenhum documento.
- SAPB1 não percebe mudança alguma, exceto a tela "Armazéns" (§5), onde os botões que já
  davam erro somem.

## 3. Origem do armazém (backend)

`SiagroB1.Application/Services/WarehouseService.cs` (STANDALONE) passa a espelhar
`Services/SAP/WarehouseService.cs`, lendo `BUSINESS_PARTNERS` (`AppDbContext`):

- `QueryAll`, `GetAllAsync`, `GetByIdAsync`: `Where(QryGroup23 == "Y")`, projetando
  `Code = CardCode`, `Name = CardName`, `FName = CardFName`, `TaxId`, `Notes`, e `City`/`State`
  do primeiro endereço de entrega (`AdresType == "S"`, ordenado por `AddressName`) — mesma
  regra do `LoadWarehousesAsync`.
- `CreateAsync`, `UpdateAsync`, `DeleteAsync`: lançam `DefaultException` com a mensagem pt-BR
  **"Cadastre o armazém no Parceiro de Negócio, marcando 'Armazém'."** — o
  `WarehousesController` já converte `DefaultException` em 400 com a mensagem.
- `LoadWarehousesAsync`: inalterado.

Os consumidores do `IWarehouseService` (contratos de compra, liberações, saldos, lotes,
movimentos, cargas, recusa, transbordo, conferência, devolução, remessa) não mudam.

`SiagroB1.Reports/Services/PrePurchaseContractReportService.cs` deixa de ler
`db.Context.Warehouses`: o CNPJ do armazém (`WarehouseTaxId`) passa a vir do parceiro do
`DeliveryLocationCode`, resolvido pelo `IPartnerSource` já usado no mesmo serviço para o
vendedor. Efeito colateral: o campo passa a sair preenchido também em SAPB1.

A entidade `Warehouse` e a tabela `WAREHOUSES` ficam sem uso (marcadas como obsoletas em
comentário). Apagar a tabela fica para uma etapa posterior.

## 4. Cadastro de Parceiro de Negócio

- `BusinessPartnerService` (STANDALONE): `CreateAsync` grava `QryGroup23` do modelo (`"Y"` ou
  `"N"`; nulo/outro vira `"N"`) em vez de forçar `"N"`; `UpdateAsync` passa a gravá-lo.
- Frontend `view/parceirosNegocio/fragments/Form.fragment.xml`: checkbox **"Armazém"**
  ligado a `QryGroup23` (`Y`/`N`), visível só com `ui>/standalone`. `Add.controller.ts` mantém
  o padrão `"N"`.
- Sem trava ao desmarcar (igual ao SAP): documentos antigos mantêm o código; o PN só deixa de
  aparecer no value help de armazém.

## 5. Tela "Armazéns" (ambos os modos)

- `view/armazem/Main.view.xml`: ficam **Atualizar** e **Complemento**; saem **Incluir**,
  **Editar** e **Deletar**. Colunas: Código, Nome, CNPJ, Cidade/UF.
- Lista vazia: texto "Nenhum armazém. Marque 'Armazém' no cadastro do Parceiro de Negócio."
- Removidos `controller/armazem/{Add,Edit}.controller.ts`, `view/armazem/{Add,Edit}.view.xml`
  e as rotas/targets correspondentes no `manifest.json` (e os fragments que só eles usavam).
- O Complemento (`WAREHOUSE_COMPLEMENTS`, chaveado pelo código) não muda.

## 6. Migração dos dados

Migration do contexto App, só SQL, idempotente. Para cada linha de `WAREHOUSES`:

1. existe PN com `CardCode = Code` → `UPDATE ... SET QryGroup23 = 'Y'` (nada mais);
2. senão → `INSERT` em `BUSINESS_PARTNERS` com `CardCode = Code`, `CardName = Name`,
   `CardType = 'S'`, `QryGroup23 = 'Y'`, `U_YKT_CNPJ_CPF = NULLIF(LTRIM(RTRIM(TAXID)), '')`.

`Down` não desfaz (no-op comentado). Em SAPB1 `WAREHOUSES` é vazia → sem efeito.

Levantamento feito em 12/10/2026 (só SELECT):

| Banco | Armazéns | Efeito | Referências que continuam válidas |
|---|---|---|---|
| `MHAGRO_SIAGRO_PRD` | `0001` "MH AGRO", sem CNPJ | cria PN `0001` | 40 lotes, 2.258 movimentos |
| `CEAGUI_SIAGRO` (produção) | `0000000001` "CEAGUI", sem CNPJ | cria PN `0000000001` | 3 contratos, 2 liberações, 10 cargas, 24 movimentos |
| `CEAGUI_SIAGRO_DEV` | `0000000001`, `FTESTEPJ` | cria `0000000001`; só marca `FTESTEPJ` | contratos, liberações, cargas, movimentos de teste |

Nenhum dos bancos tem PN com código de armazém que seja **outro** estabelecimento (colisão).

Riscos aceitos:
- O PN criado nasce sem endereço nem IE. Para ser destinatário de NF-e o usuário completa o
  cadastro; o `NfeReadinessValidator` já recusa a emissão enquanto faltar dado. Armazém próprio
  (o caso dos dois clientes) nunca é destinatário da remessa.
- O armazém aparece também como fornecedor na lista de PN e nos value helps de fornecedor.
- A MH Agro está com migrations paradas em 15/09; esta migration vai junto com as demais no
  próximo deploy de lá.

## 7. Testes e verificação

Backend (TDD, `SiagroB1.Application.Tests`):
- `WarehouseService` STANDALONE: lista só PN com a marca; projeta nome, fantasia, CNPJ,
  observação, cidade/UF do endereço de entrega; `GetByIdAsync` de PN sem marca devolve nulo;
  incluir/editar/excluir recusam com a mensagem.
- `BusinessPartnerService`: inclusão e edição gravam `QryGroup23`; valor inválido vira `"N"`.
- Relatório de pré-contrato: CNPJ do armazém vem do parceiro.

Migration: aplicar o SQL numa cópia do `CEAGUI_SIAGRO_DEV` e conferir os dois casos (insere e
só marca) e a reexecução sem efeito.

Frontend: `ts-typecheck` e `lint`. Pela tela no CEAGUI dev: marcar um PN como armazém; vê-lo
na tela Armazéns e no value help da liberação; emitir a remessa para ele sem cadastro
duplicado; lotes e movimentos antigos do `0000000001` continuam com nome.

## 8. Fora do escopo

- Apagar a tabela `WAREHOUSES` e a entidade `Warehouse`.
- Trava contra desmarcar "Armazém" em PN com movimentos.
- Qualquer mudança no modo SAPB1 além da tela Armazéns.
