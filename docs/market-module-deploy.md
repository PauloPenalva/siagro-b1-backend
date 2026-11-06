# Módulo Mercado (cotações) — implantação

Checklist curto para ligar o módulo Mercado num ambiente. Nada aqui é aplicado pela publicação: cada
instalação (VPS, cliente) mantém os próprios `appsettings`, e o pacote publicado não leva nenhum.

## 1. Configuração

- **`Modules:Market:Enabled = true` no Web E no Gateway** do ambiente. O Web usa a chave para as rotas,
  o conector da CMA e o job de fechamento; o Gateway, para o menu. Só num dos dois o módulo fica pela metade.
- **Rota no Gateway**: `ReverseProxy:Routes:market-route` com `Match.Path = /market/{**catch-all}`,
  `ClusterId` = o cluster do Web (`backend`) e `AuthorizationPolicy = AuthenticatedOnly`. Sem ela a tela
  mostra "Serviço do módulo Mercado não encontrado (verifique a rota /market no Gateway)". Com a rota mas o
  módulo desligado no Web, a resposta é 404 com o cabeçalho `X-Market-Module: disabled` e a tela mostra
  "Módulo Mercado não habilitado neste ambiente".
- **`Nfe:CertificateKey`** (base64 de 32 bytes) precisa existir no Web **antes** de salvar a senha da CMA:
  ela cifra a senha. Chave vazia ou malformada → o salvamento devolve 400.
- **Reiniciar** Web e Gateway depois de mudar a chave: o serviço de streaming da CMA e o job
  `market-daily-close-job` leem a configuração na partida.

## 2. Banco

- `AppDbContext`, em **todo banco de empresa**: `AddMarketQuotes` e `AddMarketDailyCloseQuotedAt`.
- `CommonDbContext`, no banco comum: `AddMarketMenu` (o item "Mercado" entra por último no menu superior).
- O Web recusa subir com migration pendente (`PendingMigrationsGuard`).

## 3. Regras de operação

- **Nunca rode duas instâncias do Web com as mesmas credenciais da CMA** (ex.: produção e homologação
  apontando para o mesmo usuário): as sessões se derrubam uma à outra.
- Ao trocar o host da CMA na tela, a senha precisa ser digitada de novo (a guardada nunca vai a um host novo).
- O fechamento diário roda às 18h de Brasília (dias úteis) e só grava entre 18:00 e 23:59 locais; se o job
  falhar ou atrasar, lance o fechamento manualmente.

## 4. Primeiro teste real com a CMA

Premissas do protocolo que ainda não foram confirmadas contra o servidor real:

1. O canal persistente entrega objetos JSON concatenados, cada um com o campo `name`.
2. Um segundo `QuotesRequest` (reassinatura após mudar instrumentos) soma ou substitui a assinatura anterior.
3. `PingRequest` com `sessionId` vazio mantém a sessão viva.
4. O `arrQuotes` inicial da resposta do `QuotesRequest` tem o mesmo formato do `QuotesUpdateResponse`.
5. Expiração de sessão chega como `ErrorResponse` no canal.

Onde olhar:

- Status na tela **Integração CMA** (Conectado / Erro + mensagem).
- Log do Web, categoria `SiagroB1.Application.Services.Market.Cma.CmaCompanySession`: falhas de transporte
  trazem `HttpRequestError`, `Status`, `Socket` e a cadeia de tipos (nunca a URL — ela leva a senha);
  falhas de banco/cifra trazem a exceção completa. Repetições saem no máximo uma vez por minuto.
- `SiagroB1.Web.Market.CmaStreamingHostedService` (sessão que terminou) e `MarketQuotesStreamLoop` (SSE).
- Hangfire: job `market-daily-close-job` (sem retry automático; "fora da janela" aparece como warning).

## 5. Parte 2 — Preços, tabela de preços e boleta

1. **Migrations em TODOS os clientes** (com ou sem módulo ligado — o Web recusa subir com migration pendente):
   - em **todo banco de empresa** (`AppDbContext`): `AddMarketPricing` e `AddMarketTicketFxTrace`;
   - no **banco comum** (`CommonDbContext`): `AddMarketPricingMenuAndPermissions` e `AddMarketRoleMenus`.
2. **Papéis**: usuário não-admin precisa de `MARKET_TRADER` e/ou `MARKET_BOARD` num perfil. Sem isso, cotação
   manual, liberação, fechamento manual e cadastro de instrumentos passam a dar **403** (antes da Parte 2 eram livres).
3. **Parâmetros do Mercado** — salvar ANTES da primeira boleta: instrumento do dólar à vista (ativo, R$/US$),
   unidade do frete e unidade do contrato **KG** (única aceita nesta versão; a boleta grava o contrato em kg).
4. **Limiares de alçada** (volume em t, desvio em %) vazios = sem alçada da diretoria.
5. **Trava de aprovação** vale por processo: rode **um Web por cliente**. Entre processos, o que impede contrato
   duplicado é a chave do contrato pré-atribuída na boleta.

## 6. Parte 3 — Hedges (corretoras, operações de bolsa, hedge de moeda, EFP)

1. Migrations: `AddMarketHedges` e `AddMarketHedgesIndexes` no banco de **cada empresa** (`AppDbContext`) e
   `AddMarketHedgesMenu` no banco **comum** (`CommonDbContext`). A Web recusa subir com qualquer uma pendente.
   `AddMarketHedgesIndexes` cria o índice único filtrado "uma operação por EFP" (exige `QUOTED_IDENTIFIER ON` na
   sessão que grava — o padrão do SqlClient) e os índices de casamento e de EFP por fixação/contrato.
2. A premissa de **um único processo da Web** (seção 3) agora cobre também o casamento PEPS das operações de bolsa e o
   EFP: as travas são em memória. Com duas instâncias, o risco real é casamento **faltando ou defasado**: duas
   instâncias inserindo operações opostas num par (conta × instrumento) vazio commitam sem casar; a posição então
   esconde o par e não há resultado realizado até a próxima gravação nele (qualquer gravação refaz o casamento inteiro).
3. EFP interrompido fica **Em processamento**: use "Retomar" (continua sem duplicar operação nem fixação) ou
   "Cancelar". A fixação do EFP passa pela aprovação normal e gera o título provisório do fluxo atual.
4. A fixação gerada por um EFP só se altera pelo EFP: alterar, excluir, rejeitar ou estornar essa fixação pelas ações
   do contrato é recusado enquanto o EFP não estiver cancelado.
5. **Pendência antes de operar dólar futuro**: confirmar com uma nota de corretagem real a escala do preço. A B3 cota
   em R$ por US$ 1.000; o sistema assume R$ por US$ 1 com `ContractSize` 50.000.
