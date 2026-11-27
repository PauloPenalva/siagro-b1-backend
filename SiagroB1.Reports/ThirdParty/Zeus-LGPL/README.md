# DANFE da Zeus (DFe.NET) — LGPL-2.1

Arquivos copiados **sem alteração** de https://github.com/ZeusAutomacao/DFe.NET, commit `08743cd5f5b82769a26ad9ae0093b1b0ae60c74f`,
licença LGPL-2.1 (ver `LICENSE`). O layout `NFeRetrato.frx` e as classes do `NFe.Danfe.OpenFast`
não são publicados no NuGet, por isso estão aqui.

Os `.cs` compilam, sem alteração, em **`NFe.Danfe.Base.dll`** (projeto `NFe.Danfe.Base.csproj`, nesta
pasta). O nome do assembly é o que o `NFeRetrato.frx` referencia no script, e a DLL separada mantém o
código LGPL substituível pelo usuário (LGPL-2.1 §6). O `SiagroB1.Reports` referencia o projeto e não
compila estes arquivos. `LICENSE`, `README.md` e o `.frx` vão para a saída do build e do publish.

Regra: **não editar estes arquivos.** Qualquer alteração obriga a publicar o diff (LGPL). Para
atualizar, copie de novo do repositório e registre o novo commit acima.

## Arquivo modificado

`NFe.Danfe.Base/NFe/NFeRetratoRtc.frx` é obra **derivada** do `NFeRetrato.frx` (mesmo commit da Zeus acima),
modificada em 26/11/2026 para a NT 2026.010 v1.00 (DANFE com a Reforma Tributária). Continua sob LGPL-2.1 e é
entregue aberto, em texto, junto com a instalação. A regra "não editar" vale para todos os demais arquivos desta
pasta. O próprio `.frx` traz, num comentário XML logo após a declaração, o aviso de que é versão modificada
(LGPL-2.1 §2a); salvar o arquivo pelo designer do FastReport apaga esse comentário, que tem de ser reposto.

O `.gitattributes` da raiz marca os `.frx` desta pasta como `-text`: o Git não converte fim de linha, e o
SHA-256 do `NFeRetrato.frx` original (preso em teste) não muda num clone com `core.autocrlf=true`.

### Mudanças no `NFeRetratoRtc.frx`

- **Emitente**: nova linha com "CÓDIGO DO REGIME TRIBUTÁRIO" (`txtRtcCrt`, parâmetro `RtcCrt`) e
  "TIPO DE REGIME DE APURAÇÃO DO IBS E DA CBS" (`txtRtcRegimeApuracao`, sempre vazio: a tag sai em NT
  futura). A faixa passou de 173,86 para 200,32 de altura.
- **Totais em três blocos**, na ordem da NT, entre Fatura/Duplicatas e Transportador:
  - `TotalProdutosNota` — "TOTAL DOS PRODUTOS E TOTAL DA NOTA": produtos, frete, seguro, desconto,
    outras despesas e, na segunda linha, o valor total da nota (objetos e expressões que estavam na
    faixa `Imposto`).
  - `Imposto` — "TOTAL DO ICMS / IPI" (antes "CÁLCULO DO IMPOSTO"): BC ICMS, ICMS, BC ICMS ST, ICMS ST e
    IPI. Faixas filhas encadeadas `ImpostoFcp` (FCP, FCP retido por ST, DIFAL e FCP na UF de destino) e
    `ImpostoIcmsMono` (ICMS monofásico), visíveis só quando `RtcShowFcpLine` / `RtcShowIcmsMonoLine`.
  - `TotalIbsCbsIs` — "TOTAL DO IBS / CBS / IS": CBS, IBS UF, IBS município e IS. Faixa filha
    `TotalIbsCbsMono` (IBS/CBS monofásicos), visível só quando `RtcShowIbsCbsMonoLine`.
- **Removidos**: totais de PIS e COFINS e o quadro "Total dos Tributos (Fonte: IBPT)"; o parâmetro
  `ExibirTotalTributos` deixou de ter efeito. O script `Imposto_BeforePrint` agora só liga as faixas filhas.
- **Parâmetros `Rtc*`** preenchidos por `DanfeRtcData.ApplyTo`. Os escalares (`RtcCrt`, totais e as flags
  `RtcShow*`) estão declarados no dicionário; os seis arrays `string[]` da grade (`RtcItemClassTrib`,
  `RtcItemBases`, `RtcItemRatesLeft`, `RtcItemRatesRight`, `RtcItemValuesLeft`, `RtcItemValuesRight`) **não**
  estão: o script os lê com `Report.GetParameterValue`.
- **Quebra da primeira página**: `DadosProdutosHeader_BeforePrint` soma também `TotalProdutosNota`,
  `TotalIbsCbsIs` e as faixas filhas visíveis.
- **Grade de produtos** (faixas `DadosProdutosHeader` e `DadosProdutos`), colunas do modelo retrato da NT:
  DESCRIÇÃO DO PRODUTO / SERVIÇO (`txtRtcItemDescricao`: código - nome, informações adicionais do item e, na
  última linha, `[NCM …]` e `[cClassTrib …]`, este só quando o item tem `IBSCBS`), CST / CFOP
  (`txtRtcItemCstCfop`, origem+CST ou CSOSN sobre o CFOP), QTD / UN (`txtRtcItemQtdUn`; com as duas unidades,
  a tributável abaixo da comercial), VLR UNIT (`txtRtcItemVlrUnit`), VLR TOTAL (`txtRtcItemVlrTotal`, `vProd`),
  BASES DE CÁLCULO (`lblRtcItemBases` + `txtRtcItemBases`), ALÍQUOTAS (`…RatesLeft` ICMS/IBS UF/IBS MUN e
  `…RatesRight` CBS/IPI/IS) e VALOR DOS TRIBUTOS (`…ValuesLeft`/`…ValuesRight`, mesmos pares). Os valores vêm
  prontos dos parâmetros `string[]` `RtcItem*` (`DanfeRtcData`); ausente no XML sai em branco.
  - A tabela `prodTable` virou objetos de texto soltos que crescem juntos (`CanGrow` + `GrowToBottom`), todos
    com `LineHeight` 9 para as linhas dos rótulos (5pt) e dos valores (6pt) ficarem alinhadas. Linha mínima
    de 40 (4 linhas de texto).
  - Larguras (px): descrição 155,57; CST/CFOP 24; QTD 58; VLR UNIT 56; VLR TOTAL 56; bases 35 + 56; alíquotas
    35 + 41 | 20 + 41; valores 35 + 56 | 20 + 56 (cabeçalhos alinhados às células; a grade termina em 744,57).
    Valores até 9.999.999,99 (inclusive ICMS e CBS de 7 dígitos) e alíquotas como "0,0375%", "12,3456%" e
    "100,00%" cabem em fonte normal (6pt) com 4% de folga (teste `Large_amounts_fit_their_columns`).
  - Rede de segurança para a métrica mais larga das fontes do Linux: as células numéricas da grade (QTD, VLR UNIT,
    VLR TOTAL, bases, alíquotas e valores) têm `AutoShrink="FontSize"` com `AutoShrinkMinSize="5"`. O FastReport
    mede a linha mais longa do texto sem quebra e, se não couber, desce a fonte de 6pt para 5pt naquela célula; a
    altura da linha não muda (`LineHeight` fixo). Abaixo de 5pt não encolhe: o que ainda sobrar é cortado.
  - Saíram as colunas próprias de código, NCM, desconto, BC ST e valor ST, e com elas o IPI devolvido por
    item (`impostoDevol`): o IPI do item agora vem de `RtcItem*`. Os parâmetros `DuasLinhas`,
    `ImprimirDescPorc` e `ImprimirTotalLiquido` deixaram de ter efeito na grade.
  - Números da grade formatados com cultura pt-BR fixa (antes dependiam da cultura do servidor).
  - O cabeçalho da grade agora se repete em cada folha (`RepeatOnEveryPage`), como na folha de continuação da NT.
  - O índice do item no script sai da posição do objeto `det` corrente na lista do XML, e não de um contador:
    o motor também dispara o `BeforePrint` quando só mede a faixa.
  - Quebra da folha 1: `DadosProdutos_AfterPrint` passa a quebrar quando a próxima linha (altura mínima) não
    cabe mais, e a soma do cabeçalho conta todas as linhas de duplicatas (o contador da Zeus deixava de fora a
    última linha incompleta; ele e os eventos `Duplicatas_BeforePrint`/`DuplicatasHeader_BeforePrint` foram removidos).
