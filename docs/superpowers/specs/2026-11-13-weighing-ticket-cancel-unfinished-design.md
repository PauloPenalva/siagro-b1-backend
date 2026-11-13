# Cancelar ticket de pesagem não concluído (somente ADMIN) — design

Data: 2026-11-13 · Aprovado pelo usuário em 13/11/2026.

## Intenção

Um ticket de pesagem em andamento (aguardando 1ª pesagem, 2ª pesagem ou a criação do romaneio)
precisa poder ser cancelado, mas só por quem tem o papel `ADMIN` atribuído pelo perfil.

## Como está hoje

- `WeighingTicketsCancelService` exige um romaneio (`StorageTransaction`) pendente ligado ao
  ticket. O romaneio só nasce em `WeighingTicketsCompletedService`, então cancelar um ticket não
  concluído falha com `STORAGE_TRANSACTION_NOT_FOUND`.
- O botão "Cancelar Ticket" já aparece em `FirstWeighing`, `SecondWeighing` e `Confirm` para
  qualquer usuário, e a ação `WeighingTicketsCancel` não checa permissão.
- A mensagem de sucesso do cancelamento na tela diz "Romaneio criado com sucesso."

## Objetivos

- Ticket com `Status` `Waiting` ou `Processing` pode ser cancelado pela ação
  `WeighingTicketsCancel`, desde que o usuário tenha o papel `ADMIN` (`HasRoleAsync`, sem o
  bypass do flag legado `USERS.IsAdmin`).
- O ticket vira `Status = Cancelled`, `Stage = Completed`, com `UpdatedBy`/`UpdatedAt`
  preenchidos. Não é apagado.
- Na tela, o botão "Cancelar Ticket" das três telas de andamento só aparece para ADMIN.
- Mensagem de sucesso corrigida para "Ticket cancelado com sucesso."

## Fora do escopo

- Ticket concluído com romaneio pendente: o cancelamento continua como hoje, sem exigir ADMIN.
- Motivo do cancelamento: não é pedido (decisão minha, aceita pelo usuário).
- `DELETE` de ticket na API: não muda.
- Sem migration.

## Anti-objetivo

Usuário sem o papel ADMIN não pode cancelar ticket em andamento nem chamando a API direto: a
regra vale no servidor, a tela só a espelha.

## Abordagens

1. **Estender `WeighingTicketsCancelService`** (escolhida): o mesmo serviço e a mesma ação OData
   decidem pelo status do ticket. Ticket não concluído → checa ADMIN e cancela sem tocar em
   romaneio; ticket concluído → caminho atual. Uma regra só, telas já apontam para a ação.
2. Ação/serviço novo só para o ticket em andamento: duplicaria a regra de cancelamento e
   obrigaria a tela a escolher a ação pelo status.
3. Reusar o `DELETE`: apagaria o histórico do ticket, contra o pedido de "cancelar".

## Desenho

- `WeighingTicketsCancelService` recebe `IUserPermissions`. Em `ExecuteAsync`:
  - ticket já `Cancelled` → `BusinessException` "Ticket de pesagem já está cancelado.";
  - `Waiting`/`Processing` → sem papel ADMIN: `BusinessException` "Somente usuário com o papel
    ADMIN pode cancelar um ticket de pesagem não concluído."; com papel: marca cancelado e salva;
  - `Complete` → caminho atual (romaneio pendente), inalterado.
- Frontend: `ui>/canCancelTicket = SessionService.hasRole("ADMIN")` no `GenericController`,
  ligado ao `visible` do botão nas três views; texto do toast corrigido nos dois `onCancel`.

## Testes

`SiagroB1.Application.Tests/WeighingTickets/WeighingTicketCancelUnfinishedTests.cs`: ADMIN cancela
ticket `Waiting` e `Processing`; não ADMIN é barrado e o ticket não muda; ticket já cancelado é
recusado.
