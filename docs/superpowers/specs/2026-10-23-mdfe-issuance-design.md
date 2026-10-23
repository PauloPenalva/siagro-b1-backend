# Emissão de MDF-e (sub-projeto 4) — desenho

Data: 23/10/2026. Base: `feature/cte-issuance` (CT-e entregas A e B prontas, não mergeadas).
Cliente: COSTA E COSTA TRANSPORTES LTDA (filial de teste 02 no CEAGUI local, homologação).

## Por que

A COSTA emite ~750 CT-e/mês de combustível, todos dentro de SP, com frota própria. O MDF-e
(Manifesto Eletrônico de Documentos Fiscais, modelo 58, versão 3.00) é obrigatório para a viagem
e é onde vai o **CIOT** (código de identificação da operação de transporte), que motivou o pedido.
No EfisCloud a COSTA nunca transmitiu um MDF-e (só 2 rascunhos, série 1, último número 32): para
eles o MDF-e é novo, então não há legado a migrar nem hábito a copiar.

## Objetivo

Emitir, encerrar e cancelar MDF-e rodoviário a partir dos CT-e autorizados, com CIOT, no modo
**STANDALONE** — no mesmo padrão do CT-e (documento + ciclo SEFAZ + documento auxiliar em PDF).

## Fora do escopo (não-objetivos)

- SAPB1 (como o CT-e, o MDF-e só existe no STANDALONE).
- MDF-e de emitente que não é transportador (tpEmit 2 "carga própria" e 3 "CT-e globalizado").
- Modais que não sejam rodoviário.
- Evento de inclusão de DF-e, evento de pagamento da operação (`evPagtoOperMDFe`) e alteração de
  pagamento — ficam para depois; o CIOT e as informações de pagamento entram no próprio MDF-e.
- Averbação do MDF-e na AT&M (a averbação já é feita pelo CT-e).
- Geração do CIOT: o CIOT vem de uma administradora (IPEF/PEF) fora do sistema; aqui ele é só
  informado.
- Importar os rascunhos do EfisCloud.

## Anti-objetivos (falha mesmo que "funcione")

- Um MDF-e autorizado que nunca é encerrado: a SEFAZ bloqueia novo MDF-e para a mesma placa/UF
  enquanto houver um não encerrado. O encerramento tem que ser fácil e a lista tem que mostrar os
  não encerrados com destaque.
- O mesmo CT-e em dois MDF-e abertos ao mesmo tempo.
- Perder o número ou o XML assinado se a resposta da SEFAZ cair (mesma lição do CT-e: XML assinado
  gravado antes de enviar; retransmitir consulta primeiro).

## Como funciona (decisões)

### Documento

- Entidade `TransportManifest` (MDF-e), por filial, série própria do MDF-e por filial
  (`BranchMdfeSeries`, como as séries do CT-e; numeração independente da do CT-e). Configuração por
  filial na mesma tela do CT-e (aba MDF-e): habilitado, ambiente, série padrão.
- Ciclo igual ao do CT-e: Rascunho → Confirmado (número e chave fixos) → Em processamento →
  Autorizado → Encerrado / Cancelado; Rejeitado/Denegado como no CT-e; descartar número.
- Emitente: tpEmit = 1 (prestador de serviço de transporte), tpTransp vazio (frota própria/ETC).
- Origem: o usuário escolhe CT-e **autorizados** da filial que não estejam em outro MDF-e ativo
  (Rascunho, Confirmado, Em processamento, Autorizado não encerrado). Cancelar o MDF-e ou ele ser
  rejeitado libera os CT-e.
- A partir dos CT-e o sistema preenche: UF de início e fim, municípios de carregamento (início da
  prestação) e de descarregamento (fim da prestação), com a chave de cada CT-e agrupada pelo
  município de descarga; veículo de tração, reboques e motorista (do conjunto do CT-e); RNTRC;
  seguro (responsável, seguradora, apólice da configuração da filial) e os números de averbação
  dos CT-e averbados; produto predominante e tipo de carga.
- O usuário pode ajustar: UFs de percurso (obrigatórias quando a viagem passa por outra UF; a
  COSTA opera só em SP, então em geral vazio), condutores adicionais, lacres, CEP de carregamento e
  descarregamento (exigidos pela SEFAZ quando é carga lotação — um único CT-e/NF-e), observações.
- Totais: quantidade de CT-e, valor total da carga e **peso bruto em KG ou TON**. **Decisão minha:**
  o MDF-e só aceita KG ou TON, mas o CT-e da COSTA é em M3 (combustível). O campo de peso fica
  editável e obrigatório; ele vem preenchido só quando os CT-e estão em KG/TON. Para M3 o usuário
  digita o peso (o EfisCloud gravava 43.881 "M3", o que a SEFAZ não aceita). Se o cliente quiser,
  depois dá para converter por densidade cadastrada no produto.
- Tipo de carga (`tpCarga`): padrão "Granel líquido" (02) configurável por filial; produto
  predominante herdado do CT-e.

### CIOT, contratante e pagamento (grupo `infANTT`)

- **CIOT**: número (12 dígitos) + CPF/CNPJ do responsável pela geração, informados à mão. Opcional
  (frota própria sem subcontratação pode não ter CIOT), mas a tela avisa quando a filial está
  marcada como "exige CIOT" (configuração por filial, padrão desligado).
- **Contratante**: preenchido com os tomadores distintos dos CT-e (CNPJ/CPF e nome).
- **Vale-pedágio**: opcional, informado à mão (CNPJ da fornecedora, número do comprovante, valor).
- **Informações do pagamento do frete** (`infPag`): opcional, informado à mão (contratante,
  componentes, valor total, à vista/a prazo com parcelas, PIX ou banco/agência). **Decisão minha:**
  ele é exigido só quando houver CIOT; o validador recusa a confirmação com CIOT e sem pagamento.

### SEFAZ

- Biblioteca `Zeus.Net.MDFe` 2026.9.24.1416 (mesma versão do Zeus CT-e; ainda não instalada). XSDs
  do MDF-e 3.00 copiados de `C:\Projetos\EfisCloud\backend\schemas` (como foi feito com o CT-e).
- Autorização **síncrona** (`MDFeRecepcaoSinc`), consulta por chave, eventos:
  - **Encerramento** (obrigatório; data, UF e município de encerramento — padrão: último município
    de descarga; pode ser em lote na lista);
  - **Cancelamento** (só antes do encerramento e dentro do prazo da SEFAZ; justificativa ≥ 15);
  - **Inclusão de condutor** (troca de motorista durante a viagem);
  - **Consulta de MDF-e não encerrados** por CNPJ (botão na lista, para achar o que trava a placa).
- Lições do CT-e aplicadas: texto livre limpo (sem espaço duplo), nomes trocados em homologação pela
  frase oficial, XML assinado gravado antes do envio, retransmissão consulta antes, duplicidade
  tratada como no CT-e, IBS/CBS não se aplica (o MDF-e não tem tributo).

### DAMDFE

- PDF próprio em FastReport (`Damdfe.frx`) no serviço de Reports, com QR Code, como o DACTE.
  Download individual e ZIP de XML/DAMDFE na lista.

### Telas e menu

- **Módulo novo "TMS" no menu superior** (pedido do usuário 23/10), ícone `sap-icon://folder-blank`
  como os demais módulos, reunindo as funcionalidades de transporte: Conhecimentos de Transporte
  (CT-e), Manifestos (MDF-e) e os cadastros de transporte (veículos, motoristas, conjuntos).
  Migration na COMMON que cria o módulo, inclui o MDF-e e **move** para ele o item do CT-e (hoje em
  Vendas) e os cadastros de transporte (hoje em Cadastros), preservando as permissões já dadas aos
  perfis. **Decisão minha:** mover os cadastros de transporte também, para o módulo ficar completo;
  a configuração da transportadora continua na tela da filial. Só STANDALONE, como as telas.
- Lista com filtros (emissão, situação, placa, motorista, "não encerrados"), ações: Incluir,
  Encerrar selecionados, Baixar XML, Baixar DAMDFE, Consultar não encerrados.
- Inclusão: escolher CT-e (diálogo com os CT-e livres da filial, filtráveis por placa e data);
  depois o formulário pré-preenchido. Detalhe com Confirmar, Transmitir, Consultar, Encerrar,
  Cancelar, Incluir condutor, DAMDFE.
- No detalhe do CT-e: mostrar em qual MDF-e ele está.

## Restrições

- Identificadores em inglês, texto de tela em pt-BR. Só STANDALONE (`ErpMode.IsStandalone`).
- Migrations só nos bancos locais (CEAGUI_SIAGRO_DEV, LMCEREAIS_SIAGRO_DEV, CEAGUI_SIAGRO_COMMON_DEV)
  com `ASPNETCORE_ENVIRONMENT=Ceagui-Development` e `--connection` explícito.
- Transmissão em homologação só pela tela, filial 02 COSTA, com o certificado já cadastrado. Série de
  homologação do MDF-e: **231** (mesma convenção do CT-e).
- Nunca push; merge é decisão do usuário.

## Deixado para quem constrói

Nome exato das tabelas e serviços, divisão em tarefas, layout do DAMDFE dentro do padrão oficial,
como reaproveitar o código do CT-e (chave, assinatura, cliente SEFAZ, lote/ZIP) sem duplicar.
