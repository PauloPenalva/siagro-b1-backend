# GAC-1185 (rodada 3) — nome do armazém e Região Logística na seleção de compra

## Intenção

No diálogo "Incluir contrato de compra planejado" da Carga (lista de liberações de embarque), a
Yokotobi pediu a **descrição/nome do armazém (AliasName)** e a **Região Logística**. Hoje a lista só
mostra o código do armazém, e quem planeja precisa reconhecer o armazém pelo nome e separar as
liberações por região.

## Objetivos

- Coluna "Nome Armazém": nome fantasia do armazém (`OCRD.AliasName`), lido do cadastro vivo; sem
  fantasia, cai para a razão social do cadastro e, por fim, para o nome gravado na liberação.
- Coluna "Região Logística": região do **contrato de compra**, no formato "(código) nome"
  (mesmo formato do GAC-1192); vazia quando o contrato não tem região.
- As duas colunas ordenam, filtram e entram na busca digitada do diálogo.

## Fora do escopo

- Diálogo de venda, grid da aba Contratos Planejados e a Seleção de Liberação da Expedição
  (recebem os campos novos na resposta, mas não mudam de tela).
- Nenhuma migration.

## Abordagens

1. **Enriquecer a function `ShipmentReleasesGetPurchaseContracts` (escolhida).** O serviço já monta
   a linha no servidor e já consulta o cadastro de parceiros em lote; soma-se o
   `IWarehouseService.LoadWarehousesAsync()` (roteia SAPB1 x STANDALONE) e a região do contrato na
   projeção. Mudança aditiva: as outras telas que consomem a function ignoram os campos novos.
2. Buscar armazém e região pelo frontend, linha a linha — descartada: N chamadas por abertura do
   diálogo e, em modo SAPB1, não existe navegação OData para o cadastro do SAP.
3. Gravar o AliasName na liberação — descartada: snapshot envelhece (mesmo problema do `CardFName`
   dos contratos) e exigiria migration + backfill.

## Desenho

- DTO `ShipmentRelesesPurchaseContractsResponseDto` ganha `WarehouseFName`, `LogisticRegionCode`,
  `LogisticRegionName` (todos `string?`).
- `ShipmentReleasesPurchaseContractsService` recebe `IWarehouseService`; a projeção inclui
  `PurchaseContract.LogisticRegionCode` e `PurchaseContract.LogisticRegion.Name`.
- Frontend: duas colunas no fragment `ShipmentLoadPlannedPurchaseReleaseDialog`, campos novos no
  tipo `PlannedPurchaseRelease` e na lista de campos buscados.
- Deploy: backend antes do frontend (com o frontend novo e o backend velho as colunas ficam vazias,
  sem erro).
