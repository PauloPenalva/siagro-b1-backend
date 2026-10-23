# Task 12 fix report

Bug 1 (backend): new SiagroB1.Fiscal/Cte/CteText.cs (Clean/CleanOrNull) applied to every free-text field in CteXmlBuilder (names, addresses, xObs, ObsCont, proPred, natOp, components, tpMed, emails, resp. tecnico); phone reduced to digits (6-14) or omitted; optional empties omitted. Test Build_cleans_free_text_... (red first, then green). Commit fd8bf405. Build 0 errors; Fiscal.Tests 352 pass; Application.Tests 6672 pass.

Bug 2 (frontend): readErrorMessage now reads error.message, message, value (OData string envelope), else raw body. 3 QUnit tests added (not run in browser). ts-typecheck and eslint clean.
Backend controller already returns BadRequest(e.Message) consistently (RunSefazAsync etc.); the envelope is added by the OData formatter, so no backend change.

## Fix rejeição 646 (xNome em homologação)
- HomologationName agora "CTE EMITIDO EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"; aplicado a rem/exped/receb/dest/toma4 via Named() em CteIssueInputAssembler; produção mantém nomes reais.
- Testes: Homologation_replaces_every_party_name (vermelho antes, verde depois) e Production_keeps_real_party_names.
- Build sln OK; Fiscal.Tests 352/352; Application.Tests 6673/6673. Commit abe54405.
- Obs: matei por PID dois processos soltos deste worktree (Reports 17644, Gateway 29468) que travavam o build.

## Fix: número queimado na SEFAZ (539/204 → consulta 216/217)
- Duplicidade + consulta 216, ou duplicidade + 217, e consulta 216 em Processamento (transmit ou ConsultService) → Rejected, StatusCode = último código (216, ou 539 no caso 217), StatusReason "Número já usado na SEFAZ por outro CT-e (chave diferente). Descarte o número e confirme de novo." Sem reenvio.
- Timeout + 217 continua em Processamento.
- Warning no ILogger com chave, código e mensagem para falha de comunicação, duplicidade, número queimado e consulta inesperada.
- Testes: 5 novos + descarte a partir de Rejected; asserção de log via TestLogger. Suíte: 6679/6679.
