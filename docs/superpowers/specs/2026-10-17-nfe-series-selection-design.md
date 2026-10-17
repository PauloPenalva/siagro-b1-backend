# Escolha da série na emissão própria de NF-e — design (2026-10-17)

> Combinado com o usuário em 17/10/2026. Escopo: só STANDALONE (a emissão própria só existe nele).

## Contexto e porquê

Algumas empresas usam mais de uma série de NF-e por convenção com o contador. Exemplo:

- **1**: NF-e de saída de venda;
- **100**: NF-e de entrada (contra-nota, devolução);
- **300**: remessa para armazenagem em terceiros.

O usuário que emite **sabe qual série usar pela convenção da empresa**. O sistema não precisa deduzir a série; precisa
deixar escolher entre as séries que a empresa usa.

Hoje a `BRANCH_NFE_SETTINGS` guarda **uma** série (`Series`) e **um** contador (`NextNumber`) por filial. Toda emissão
própria usa essa série, seja saída, entrada, contra-nota ou devolução. A reserva do número acontece na transmissão
(`NfeIssueServiceBase.ReserveNumberAsync` → `NfeNumberReservationService.ReserveAsync`) e é gravada no documento
(`TaxDocumentSeries`, `TaxDocumentNumber`).

## Objetivos

1. A filial cadastra **várias séries**, cada uma com seu **próprio contador** de nNF.
2. Em **todas as telas que emitem NF-e própria**, o usuário escolhe a série **na hora de transmitir**, entre as séries
   cadastradas da filial, com a **padrão** já preenchida.
3. Quem usa uma série só não percebe diferença, a não ser o diálogo de confirmação com a série já escolhida.

## Não objetivos

- Deduzir a série pela natureza de operação, pelo tipo de documento ou por regra. A escolha é do usuário.
- Série digitada livremente na emissão.
- Padrão separada para saída e para entrada. O usuário confirmou: **uma padrão por filial**.
- Contador separado por ambiente (homologação × produção). Continua como hoje.
- Modo SAPB1.
- Mudanças em cancelamento, CC-e, inutilização e consulta. Elas já usam a série gravada no documento.

## Decisões

### Cadastro: séries da filial

Nova tabela filha da configuração (nome sugerido `BRANCH_NFE_SERIES`), chave `(BranchCode, Series)`:

| Campo | Regra |
|---|---|
| `BranchCode` | FK para `BRANCH_NFE_SETTINGS.BranchCode` |
| `Series` | inteiro de 0 a 999, único na filial |
| `Description` | texto curto obrigatório, pt-BR, por exemplo "Venda" ou "Remessa para armazém de terceiro" |
| `NextNumber` | de 1 a 999.999.999 |
| `IsDefault` | **exatamente uma** série padrão por filial que tenha séries |

- O `Environment` e o certificado continuam em `BRANCH_NFE_SETTINGS`.
- As colunas `Series` e `NextNumber` saem de `BRANCH_NFE_SETTINGS` depois da migração dos dados (abaixo).
- A regra de hoje, "o próximo número não volta", passa a valer **por série**: com o mesmo ambiente, `NextNumber` de uma
  série não pode diminuir. Trocar o ambiente continua liberando a numeração, como hoje.
- **Ambiente e séries são gravados juntos, numa chamada só** (decisão minha). A gravação recebe o ambiente e a lista
  inteira de séries, e a regra acima compara o ambiente **gravado** com o **recebido**. Se o ambiente mudou na mesma
  gravação, qualquer contador pode ser ajustado, inclusive voltar a 1 na passagem de homologação para produção. Se não
  mudou, nenhum contador existente pode diminuir. Gravar ambiente e séries em chamadas separadas quebraria essa regra:
  depois de trocar o ambiente, não daria mais para zerar os contadores.
- **Não se exclui uma série já usada** (decisão minha sobre o que é "usada"): uma série que aparece em
  `TaxDocumentSeries` de **qualquer** documento de emissão própria, seja de saída ou de entrada com `IssuerType` próprio,
  **em qualquer situação e em qualquer ambiente**. Isso inclui nota só reservada, rejeitada ou emitida em homologação.
  O motivo é que o contador não é separado por ambiente: excluir e recriar a série recomeçaria de um número menor, com
  risco de rejeição 539. Uma série sem uso pode ser excluída, desde que não seja a padrão.
- O número da série não muda depois de criada. Para usar outra série, cadastra-se uma nova.

### Migração dos dados

Para cada linha existente de `BRANCH_NFE_SETTINGS`, cria-se uma série com o `Series` e o `NextNumber` atuais,
`IsDefault = 1` e a descrição "Padrão". Assim, nenhuma filial fica sem série e a numeração continua de onde parou.
(Lembrete do projeto: o Web recusa subir com migration pendente.)

### Emissão: escolha da série

- O "Emitir NF-e" passa a receber a **série escolhida**. A action OData ganha um parâmetro `Series` (inteiro),
  obrigatório.
- O backend recusa, com mensagem pt-BR, uma série que não está cadastrada na filial do documento.
- A reserva do número passa a ser **por série**: o `UPDATE … OUTPUT deleted.NextNumber` atua sobre a linha
  `(BranchCode, Series)` de `BRANCH_NFE_SERIES`, com a mesma trava (`UPDLOCK, HOLDLOCK`) e na mesma conexão própria
  fora da transação.
- A regra de retentativa existente continua: se o documento já tem reserva (`NfeRandomCode`) e a série escolhida ou o
  ambiente for diferente da reserva, a reserva é descartada e outro número é reservado na série escolhida. A
  comparação passa a usar a série escolhida em vez de `settings.Series`.
- A prontidão (`NfeReadinessValidator`) passa a acusar "nenhuma série cadastrada" quando a filial não tem séries.

### Diálogo na interface

Ao clicar em emitir, abre um diálogo pequeno com um **Select** das séries da filial do documento, exibidas como
"300 - Remessa para armazém de terceiro". Ao confirmar, a emissão segue como hoje.

Pré-seleção:
- **primeira tentativa:** a série padrão;
- **retentativa** (documento com `TaxDocumentSeries` já gravada): a série dessa tentativa, se ainda estiver cadastrada,
  senão a padrão.

Os pontos que transmitem hoje (todos passam por `SalesInvoicesIssueNfe` ou `PurchaseInvoicesIssueNfe`):
1. **Documento de Saída**: `salesInvoices/Detail.controller.ts` `onIssueNfe`;
2. **Documento de Entrada**: `purchaseInvoices/Detail.controller.ts` `onIssueNfe` (entrada própria, contra-nota,
   devolução);
3. **Montagem de Carga**: `shipmentLoads/Detail.controller.ts`, na emissão da NF-e de entrada da recusa
   (`runRefusalNfeAction`).

O diálogo deve ser um único componente reaproveitado nos três pontos. Qualquer outro ponto que chame essas actions
também precisa mandar a série, já que o parâmetro é obrigatório.

### Tela de Configuração da NF-e

Em `nfeSettings/Main.view.xml`, os campos "Série" e "Próximo número" viram uma **tabela de séries** com Série,
Descrição, Próximo número e Padrão, e as ações incluir, editar e excluir. A gravação valida as regras do cadastro
acima. O ambiente e o certificado ficam como estão.

## Restrições

- Nomes de código, tabela e colunas em inglês; textos para o usuário em pt-BR (regra do projeto).
- A reserva continua atômica e fora da transação do EF. Nos testes, o InMemory não roda SQL cru, então o fake da
  reserva precisa passar a receber a série.
- A emissão continua um documento por vez. Não existe emissão em lote hoje.

## Deixado para quem constrói

- O nome exato da tabela, do modelo OData e das actions de gravação das séries (uma action por operação ou gravação da
  lista inteira).
- Se a série padrão é marcada por coluna `IsDefault` ou por uma `DefaultSeries` em `BRANCH_NFE_SETTINGS`. O que importa
  é garantir sempre exatamente uma.
- O layout do diálogo e da tabela, seguindo o padrão UI5 do projeto.
