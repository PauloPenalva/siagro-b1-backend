# ECF (Escrituração Contábil Fiscal) — Lucro Real

Data: 2026-10-23 · Status: desenho. O usuário pulou as perguntas ("seguir"); o que não foi dito por ele
está marcado **[minha decisão]**. Parte da ECD (`feature/ecd`, não mergeada) — este branch nasce dela.

## Intenção

Fecha o par ECD/ECF da 3ª obrigação. Gerar, por empresa (CNPJ raiz) e ano-calendário, o TXT da ECF
(leiaute vigente para o ano) importável no PVA da ECF, com a apuração do **IRPJ e da CSLL pelo Lucro
Real** (CEAGUI passou a Lucro Real em 23/10), a partir da contabilidade do Siagro e de ajustes do
LALUR/LACS informados pelo usuário.

## Metas
- `POST /reports/Ecf/Check` (JSON) e `POST /reports/Ecf` (TXT), só STANDALONE; tela Contabilidade →
  "ECF" com Verificar/Gerar (mesmo padrão da ECD), mostrando por período: lucro antes do IR, adições,
  exclusões, compensação de prejuízo, lucro real, IRPJ (15% + adicional 10%), base e CSLL (9%).
- Tela "Parâmetros da ECF" por empresa (matriz) **[minha decisão]**: forma de apuração (anual com
  estimativas mensais por balancete de suspensão/redução, ou trimestral), CNAE principal, qualificação
  da PJ, indicadores do 0010/0020 que o sistema não deduz (todos com padrão "N"/"0"), quadro societário
  (nome, CPF/CNPJ, qualificação, % do capital votante e total, país 105) e o hash da ECF anterior
  quando retificadora. Signatários: os mesmos da ECD (Parâmetros da ECD + contabilista).
- Tela "Ajustes do LALUR/LACS" **[minha decisão]**: lançamentos por empresa/período com tributo (IRPJ,
  CSLL ou ambos), tipo (adição, exclusão, compensação de prejuízo/base negativa), código da tabela
  dinâmica (M300/M350 — informado pelo usuário), descrição, valor, e opcionalmente a conta contábil e a
  conta da parte B (M010) de controle. Sem isso a ECF sai com lucro real = lucro contábil.

## Registros [minha decisão — Manual da ECF do leiaute vigente]
- 0000, 0001, 0010 (forma de tributação "1" Lucro Real, apuração A/T, FORMA_TRIB_PER, MES_BAL_RED,
  TIP_ESC_PRE, TIP_ENT…), 0020 (parâmetros complementares, padrão "N"), 0030 (dados cadastrais,
  CNAE), 0930 (signatários), 0990.
- C001 `1` e E001 `1` (recuperados pelo PVA).
- J001, J050 (plano de contas), J051 (mapeamento referencial), J100 (centros de custo), J990.
- K001, K030 (período), K155/K156 (saldos patrimoniais e mapeamento), K355/K356 (resultado antes do
  encerramento), K990 — mesmos números do I155/I355 da ECD (reaproveitar o loader e os calculadores).
- L001, L030, L100 (balanço por conta referencial) e L300 (DRE por conta referencial), só com as contas
  referenciais analíticas usadas e o valor somado das contas mapeadas **[minha decisão: o PVA totaliza
  as sintéticas; conferir no PVA]**, L990.
- M001, M010 (contas da parte B usadas), M030 (período), M300 (demonstração do lucro real: lucro antes
  do IR do L300/K355, adições, exclusões, compensação), M350 (LACS), M410/M500 (lançamentos e controle
  da parte B só das contas usadas), M990.
- N001, N030, N500/N620 (base e IRPJ mensal por estimativa — anual) e N630 (IRPJ do período), N650/
  N660/N670 (CSLL), N990. Alíquotas fixas: IRPJ 15%, adicional 10% sobre o que exceder R$ 20.000,00
  por mês do período; CSLL 9% **[minha decisão: alíquota geral; instituições financeiras fora]**.
- P001/T001/U001/Q001/V001/W001/X001 `1`; Y001, Y540 (receita por CNAE = receita bruta do L300),
  Y600 (quadro societário), Y990. 9 pelo `SpedRecordWriter`.

## Fora do escopo (não-metas)
- Lucro Presumido/Arbitrado, imunes/isentas, instituições financeiras e seguradoras, atividade rural
  com benefício, incentivos (SUDENE/SUDAM, PAT, doações), IR retido/compensado além do campo de
  dedução informado, JCP calculado, preços de transferência, DEREX, país a país, bloco X.
- Cálculo automático de adições/exclusões (o usuário informa os ajustes).
- Controle de prejuízo fiscal entre anos além do saldo inicial informado na conta da parte B.

## Anti-metas
- Divergir da ECD do mesmo ano nos blocos J/K (mesma fonte, mesmos números).
- Calcular IRPJ/CSLL diferente da conta manual (lucro real × alíquotas) — os totais da tela de
  Verificar têm de bater com o arquivo.

## Bloqueantes
Todos os da ECD que se aplicam (contabilidade, meses fechados, referencial, visões); parâmetros da ECF
ausentes; CNAE inválido (7 dígitos); quadro societário vazio ou soma de % ≠ 100; ajuste sem código da
tabela; compensação de prejuízo acima de 30% do lucro real antes da compensação; forma anual sem as 12
estimativas calculáveis.

## Deixado ao implementador
Nomes de classes, telas no padrão ECD/Parâmetros da ECD, testes, extração de código comum com a ECD.
