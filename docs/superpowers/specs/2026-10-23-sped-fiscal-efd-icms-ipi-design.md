# SPED Fiscal (EFD ICMS/IPI) — geração do arquivo

Data: 2026-10-23 · Status: desenho (usuário pulou as perguntas — "seguir"; decisões abaixo são minhas,
marcadas como **[minha decisão]**, salvo o que o usuário definiu).

## Intenção

1ª de 4 obrigações pedidas pelo usuário, nesta ordem: SPED Fiscal → EFD Contribuições → ECD/ECF →
GIA/DeSTDA. Hoje ninguém gera o SPED Fiscal da CEAGUI; a Conferência Fiscal (prévia C100/C170/C190/E110)
já existe e é a base. Objetivo: gerar, por filial e mês, o TXT da EFD ICMS/IPI pronto para importar
no PVA, sem digitação fora do sistema.

**Definido pelo usuário:** bloco D fica fora (CT-e não entra nesta entrega).

## Metas
- `POST /reports/SpedFiscal` (SiagroB1.Reports) devolve o arquivo `.txt` (ISO-8859-1, linhas `|...|` + CRLF).
- Tela Fiscal → "SPED Fiscal" (só STANDALONE): filial, ano/mês, finalidade (0 original / 1 retificadora),
  "incluir inventário (bloco H)" com data e motivo; antes de gerar, mostra as inconsistências da
  Conferência e os cadastros faltantes (bloqueantes x avisos).
- Cadastro "Parâmetros do SPED Fiscal" por filial (dados do contabilista, perfil, atividade, código de
  receita e dia de vencimento do ICMS).

## Fora do escopo (não-metas)
- Bloco D (decisão do usuário); E200 (ICMS-ST), E300 (DIFAL), E500 (IPI), bloco G (CIAP), bloco K
  (vai com K001 IND_MOV=1 e K010), bloco B (ISS-DF) → todos abertos com indicador "sem dados".
- Ajustes de apuração E111/E112/E113 e C197 por UF: **[minha decisão]** fica para uma 2ª etapa; na 1ª,
  E110 sai só com débitos/créditos das notas e saldo credor anterior informado na tela.
- Transmissão (o PVA faz). Gravação do arquivo no banco.

## Anti-metas
- Gerar arquivo que diverge da Conferência Fiscal do mesmo mês: os totais de C190/E110 TÊM de bater
  com a Conferência (mesma fonte `FiscalConferenceService.LoadLinesAsync`).
- Recalcular tributo: o arquivo lê o que está gravado na linha.

## Como (decidido por mim)
- Motor em `SiagroB1.Fiscal/Sped/` (escritor genérico de registros + contagem automática do bloco 9);
  montagem dos registros em `SiagroB1.Reports/Services/SpedFiscal/` reaproveitando
  `FiscalConferenceService` e `FiscalInventoryService`.
- Versão do leiaute: tabela por período (`COD_VER` 019 até 12/2025, 020 a partir de 01/2026).
- Registros: 0000, 0001, 0005, 0100, 0150, 0190, 0200, 0220, 0990; B001/B990; C001, C100, C170, C190,
  C990; D001(IND_MOV=1)/D990; E001, E100, E110, E116, E990; G001/G990; H001, H005, H010, H990;
  K001, K010, K990; 1001, 1010, 1990; 9001, 9900, 9990, 9999.
- C100: um por documento. COD_SIT pela situação: 00 regular, 02 cancelada, 04 denegada, 05 inutilizada
  (só chave/número), 06 complementar. Cancelada/denegada/inutilizada só de NF-e PRÓPRIA.
  IND_EMIT 0 própria / 1 terceiro; IND_OPER 0 entrada / 1 saída (pelo documento); IND_FRT por
  FreightTerms (Cif→0, Fob→1, Ter→2, None→9).
- C170 só em documento de TERCEIRO (Guia Prático: NF-e própria não informa C170).
- C190 por CST/CFOP/alíquota; VL_RED_BC = 0, ST e IPI = 0 (não há esses campos no sistema).
- E116: COD_OR "000", código de receita e vencimento dos parâmetros; só se saldo a recolher > 0.
- Bloco H: se marcado (obrigatório em fevereiro sobre 31/12), H010 com quantidade/valor do Inventário
  PEPS; IND_PROP 0; saldo negativo recusa a geração.
- 0200: TIPO_ITEM "00" (mercadoria para revenda) **[minha decisão]**; item de serviço de frete "09".
  0220 quando a unidade da linha difere da unidade do produto e há fator no complemento.
- Participante 0150: só os que aparecem em C100 de terceiro/própria; COD_PAIS 01058; CPF para produtor
  rural pessoa física (RuralProducerCpf), CNPJ no restante.

## Bloqueantes (não gera o arquivo)
Filial sem CNPJ, IE, UF ou município; parâmetros do SPED ausentes; participante sem município;
produto sem NCM em C170/H010; linha sem CFOP ou CST; inventário com saldo negativo.
Demais regras da Conferência viram aviso.

## Deixado ao implementador
Nomes de classes, layout exato da tela (padrão da Conferência Fiscal), testes.
