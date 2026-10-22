# Emissão de CT-e (modelo 57, versão 4.00) — sub-projeto 3

Sub-projeto 3 de `2026-10-21-transport-and-managerial-accounting-design.md`. Ele depende dos
cadastros de transporte (sub-projeto 2, branch `feature/transport-master-data`, ainda não
mergeado). Esta branch (`feature/cte-issuance`) parte daquela.

## Por que existe

A **COSTA E COSTA TRANSPORTES LTDA** emite hoje cerca de 750 CT-e por mês no EfisCloud (17.836 no
total, 6.986 em 2026) e quer migrar para o SiagroB1. Os cadastros (veículo, conjunto, motorista,
seguro, AT&M) já existem; falta o documento.

**Sucesso:**
- a equipe emite, transmite, averba, cancela e corrige o CT-e no SiagroB1 sem voltar ao EfisCloud;
- o CT-e autorizado gera o título a receber contra o tomador.

## O que os dados reais mostram (400 CT-e detalhados + lista completa, lidos em 22/10/2026)

- **Sempre o mesmo tipo de CT-e:**
  - tomador = **destinatário**;
  - tipo de serviço Normal, finalidade Normal, emissão Normal;
  - ICMS **CST 00 a 12%**, sem redução, base = valor da prestação;
  - **um** componente de frete;
  - **1 NF-e** por CT-e (2 NF-e em 4%).
- **Expedidor e recebedor:** o expedidor é sempre igual ao remetente. O **recebedor difere do
  destinatário em 30%**, e é ele que define o município de fim da prestação.
- **Quantidade:** sempre unidade "00" (M3 no leiaute) com valores como 5.000. Provavelmente deveria
  ser KG — ver pendências.
- **Situação dos CT-e:** 97% autorizados, 2% cancelados (353) e 89 rejeitados. As causas de rejeição:
  - 843 / 652: NF-e inválida ou cancelada;
  - 539: duplicidade;
  - certificado.
- **Averbação na AT&M:** 92% averbados, com seguradora Allianz e ramo 55.
- **Série e número:** série 2, último número 82.393.
- **Não aparecem:** anulação, substituição, complemento, subcontratação, redespacho ou multimodal.

### Erros do EfisCloud que não podem se repetir

- **Numeração:** o número sai de `ultimoNumero + 1` sem trava, o que gerou 19 rejeições 539.
- **Chave instável:** o `cCT` é aleatório **a cada tentativa**, e um reenvio depois de timeout gera
  outra chave.
- **Consulta:** só imprime no console e não recupera autorização perdida.
- **Cancelamento:**
  - a justificativa é fixa ("DOCUMENTO EMITIDO INDEVIDAMENTE");
  - não cancela a averbação na AT&M.
- **CC-e:** só corrige `xObs`.
- **Valores fixos no código:**
  - `indIEToma` sempre 1;
  - `infRespTec` fixo com os dados da IDX;
  - tomador "outros" (toma4) não existe.
- **Senhas:** AT&M e SMTP em texto puro.

## Objetivos

1. **Documento "Conhecimento de Transporte"** (CT-e de saída), com:
   - participantes: remetente, expedidor, recebedor, destinatário e tomador, com tomador "outros";
   - município de início e de fim;
   - NF-e transportadas, carga, componentes, ICMS e IBS/CBS;
   - modal rodoviário: conjunto ou tração, motorista e RNTRC.
2. **Ciclo SEFAZ completo:**
   - confirmar;
   - transmitir;
   - consultar e recuperar (timeout, 539);
   - cancelar;
   - CC-e;
   - inutilizar faixa de numeração.
3. **DACTE** em PDF e **e-mail** automático (XML + DACTE).
4. **Averbação manual** na AT&M (botão "Averbar", decisão do usuário: nem todo CT-e é averbado) e
   **cancelamento da averbação** automático ao cancelar um CT-e averbado.
5. **Título a receber** contra o tomador ao autorizar, com as dimensões contábeis; cancelado junto
   com o CT-e, se não tiver baixa.
6. **Importar o XML da NF-e transportada** para preencher o CT-e: remetente, destinatário, valor,
   produto, peso e chave. É o caminho do dia a dia: o cliente manda a nota, e o CT-e nasce dela.

## Fora de escopo

- CT-e de **anulação, substituição e complemento**; **subcontratação, redespacho e multimodal**;
  **CT-e OS** (modelo 67); **contingência** (FS-DA/EPEC).
- **MDF-e** (sub-projeto 4). O CT-e só guarda o que o MDF-e vai precisar: conjunto, motorista e
  municípios.
- **Importar o histórico do EfisCloud.**
- **Averbação automática.**
- **Outros documentos transportados** (NF modelo 1, "outros"): o cliente só usa NF-e.

## Anti-objetivos (falha mesmo que funcione)

- **Número queimado** sem documento ou repetido (539) por falta de trava.
- **Duas chaves diferentes** para o mesmo CT-e por causa de reenvio.
- **CT-e autorizado na SEFAZ que o sistema não sabe que existe.** Timeout sem reconciliação.
- **Cancelar o CT-e e deixar a carga averbada** na seguradora.
- **Quebrar a NF-e:** a emissão de NF-e (CEAGUI em produção) tem de continuar idêntica. O CT-e
  reaproveita peças da NF-e, mas não as altera de forma que mude o XML ou o fluxo da NF-e.
- **Qualquer tela, menu ou coluna nova em SAPB1.**

## Restrições

- **Somente STANDALONE**, com gate positivo `ErpMode.IsStandalone`.
- **Emissão ligada por filial:** chave "Emite CT-e", como a "Emite NF-e".
- **Homologação** na SEFAZ autorizadora do CT-e de SP (SVSP) antes de produção.
- **Migrations:**
  - aditivas e aplicadas em todas as empresas da COMMON;
  - commit com trailer `DB:`.
- **Idioma:** identificadores em inglês, telas e mensagens em pt-BR.
- **Biblioteca:** Zeus DFe.NET (informação do usuário: cobre CT-e e MDF-e).
  - O pacote NuGet de CT-e **não** está no projeto hoje: só `Zeus.Net.NFe.NFCe`, sem DLL de CT-e.
  - **Primeira tarefa do plano:** achar o pacote certo, na mesma linha de versão do NF-e
    (`2026.9.24.1416`).
  - **Se não houver pacote publicado:** montar o XML com os XSDs oficiais do CT-e 4.00 em
    `SiagroB1.Fiscal/Schemas` e reaproveitar só a assinatura e o certificado.

## Desenho (o que está decidido)

### Documento — `TransportDocuments` (+ filhos)

- **Cabeçalho:**
  - filial, série, número, chave, `cCT` **gerado uma vez e guardado**;
  - data/hora de emissão;
  - natureza de operação (`USAGES`) e CFOP resolvido;
  - situação.
- **Participantes:** cinco papéis, cada um um parceiro (`CardCode`) com **cópia** de nome,
  CNPJ/CPF, IE e endereço no documento. É a mesma ideia do snapshot das notas: mudar o cadastro
  não muda documento emitido.
  - O **tomador** é um de remetente, expedidor, recebedor, destinatário ou outro (toma4, com
    parceiro próprio).
  - O `indIEToma` vem do cadastro do tomador: contribuinte, isento ou não contribuinte. Nunca é fixo.
- **Rota:** município de início e de fim (IBGE) e UF.
  - Sugeridos pela regra do EfisCloud: início = expedidor, senão remetente; fim = recebedor, senão
    destinatário.
  - **Editáveis.**
- **NF-e transportadas:** lista de chaves, com dígito verificador validado e sem chave repetida no
  mesmo CT-e.
  - A chave pode vir do XML importado, ser digitada ou ser escolhida entre as NF-e de saída
    autorizadas do próprio SiagroB1.
  - **Aviso**, não trava, se a mesma chave já estiver em outro CT-e não cancelado.
- **Carga:**
  - produto predominante (texto, sugerido do produto do XML ou do cadastro);
  - valor da carga;
  - quantidades: lista com unidade e valor. A unidade usa os códigos do leiaute: 00 M3, 01 KG,
    02 TON, 03 UNIDADE, 04 LITROS, 05 MMBTU.
- **Prestação:**
  - componentes, uma lista com nome e valor;
  - valor total da prestação = soma dos componentes;
  - valor a receber = total.
- **Tributos:**
  - **ICMS pela natureza de operação**, espelho do CT-e de entrada:
    - CFOP pela UF de início × UF da filial (dentro/fora do estado);
    - alíquota pela UF de início × fim;
    - CST, base, redução e valor gravados no documento;
    - nunca digitados.
  - **IBS/CBS** pelo mesmo serviço de alíquotas da NF-e (`IbsCbsRatesService`), com `cClassTrib` da
    natureza.
- **Modal rodoviário:**
  - RNTRC da filial (configuração da Transportadora);
  - conjunto **ou** tração, mais o motorista. O documento guarda **cópia** das placas, RENAVAM, tara,
    capacidade, tipo de rodado/carroceria e do motorista (nome e CPF);
  - o conjunto e o motorista sugeridos vêm do motorista padrão do conjunto.
- **Seguro:**
  - responsável, seguradora e apólice sugeridos da configuração da filial (editáveis);
  - depois da averbação: protocolo, número da averbação, data/hora, valor e ramo, que vêm da AT&M,
    **todos os seguros** que ela devolver.
- **Observações:**
  - texto livre (`xObs`);
  - os mesmos `ObsCont` que o EfisCloud gera hoje: PLACA, CPF/nome do motorista, RASTREADO,
    TranspProprio. As flags rastreado e transporte próprio ficam no documento, com padrão SIM como
    hoje, porque os parceiros do cliente podem ler esses campos.
- **Dimensões contábeis:** centro de custo, item contábil ou rateio no documento, como nas notas
  (sub-projeto 1). O título herda essas dimensões.
- **XMLs guardados:** autorizado (`cteProc`), eventos de cancelamento e CC-e.

### Ciclo de vida

```
Rascunho ──Confirmar──▶ Confirmado ──Transmitir──▶ Autorizado ──Cancelar──▶ Cancelado
   ▲                        │            │
   └──── Reabrir ───────────┘            └─ Rejeitado (corrige e transmite de novo, mesmo número/chave)
```

- **Confirmar:**
  - valida tudo o que a SEFAZ vai cobrar: participantes com endereço/IBGE, chaves, RNTRC, veículo
    com tara/capacidade/rodado, motorista com CPF, certificado;
  - **reserva o número com trava**: reaproveita o padrão `NfeNumberReservationService` e a série
    ganha o modelo, porque o CT-e tem numeração própria;
  - fixa `cCT` e chave.
- **Transmitir:** síncrono, como a NF-e. **Timeout ou 539 dispara a consulta pela chave**, que
  recupera o protocolo se a SEFAZ já tinha autorizado.
- **Rejeitado:** corrige e retransmite com **o mesmo número e a mesma chave**. Se precisar mudar
  algo que muda a chave, o número volta para a fila de inutilização.
- **Cancelar:**
  - justificativa digitada (mínimo 15 caracteres) e prazo legal;
  - bloqueado se o título tiver baixa;
  - em seguida cancela o título e, se o CT-e foi averbado, **envia o cancelamento à AT&M** com o
    XML do evento de cancelamento. Falha na AT&M não desfaz o cancelamento na SEFAZ: fica pendente
    com botão "Reenviar cancelamento da averbação".
- **CC-e:** estruturada (grupo, campo e valor) a partir de uma lista fechada de campos (ver
  decisões abaixo), com sequência e XML guardados.
- **Inutilizar:** faixa de números da série do CT-e, com justificativa.
- **Averbar** (só autorizado): envia o `cteProc` para `/rest/CTE` da AT&M, usando o `AverbaClient`
  e as credenciais do sub-projeto 2. Guarda o retorno. Não averba duas vezes.
- **E-mail automático** na autorização e no cancelamento: tomador, remetente e destinatário, pela
  fila de e-mail que a NF-e já usa.

### DACTE

- PDF em FastReport, no serviço de Relatórios, seguindo o manual oficial (retrato).
- **Preferência:** o modelo de DACTE do próprio Zeus, se existir no pacote (como o DANFE vendorizado
  em `ThirdParty/Zeus-LGPL`); senão, um `.frx` próprio.

### Título a receber

Ao autorizar:
- um título a receber contra o **tomador**, com o valor a receber;
- vencimento pela condição de pagamento do tomador;
- dimensões do documento.

A natureza decide se gera título (`CreatesFinancialDocument`), como nas notas. O cancelamento do
CT-e cancela o título.

### Telas

- **Lista "Conhecimentos de Transporte"** com FilterBar: período, situação, tomador, remetente,
  destinatário, placa, averbado sim/não. Ações em massa:
  - transmitir os confirmados;
  - averbar os selecionados;
  - baixar XML e DACTE em lote.
- **Inclusão e edição** em página. Botão **"Importar XML da NF-e"**, que aceita várias NF-e: preenche
  remetente e destinatário, chaves, valor da carga, produto e peso, e sugere a rota.
- **Detalhe em ObjectPage**, com os botões do ciclo de vida e a seção de averbação.
- **Configuração:**
  - série, próximo número e ambiente do CT-e na tela de configuração da NF-e, que vira "Configuração
    de DF-e" ou ganha uma aba CT-e;
  - "Emite CT-e" na filial.
- **Menu:** novo item em Vendas ou módulo "Transporte". O builder escolhe, sempre com
  `sap-icon://folder-blank` se for módulo novo.

## Decisões tomadas na revisão do desenho (minhas, 22/10)

1. **Número, chave e edição:**
   - O número e a chave nascem no **Confirmar** e não mudam mais.
   - **Reabrir** (Confirmado → Rascunho) e **Rejeitado** permitem editar tudo, menos filial, série e
     número.
   - A data/hora de emissão é renovada a cada transmissão. Se ela cair num **mês** diferente do da
     chave, a chave muda (`AAMM`). Nesse caso o sistema recusa transmitir e oferece "Descartar
     número": o número vai para a lista de inutilização e o documento volta a Rascunho, sem número.
   - Excluir um documento com número reservado faz o mesmo.
   - A **lista de números a inutilizar** fica numa tabela própria, e o usuário inutiliza a partir
     dela.
2. **Título a receber:**
   - Reaproveita o caminho dos títulos firmes de venda, com **novo tipo de origem** "Conhecimento de
     Transporte".
   - A condição de pagamento vem do **cadastro do tomador**, editável no documento.
   - Sem chave de data de corte, porque não há histórico de CT-e no sistema.
   - As travas de cancelamento são as mesmas dos títulos de venda: não cancela título com baixa.
3. **Contabilização automática:** **fora** deste sub-projeto. Entra no sub-projeto 6, como no
   roadmap. O CT-e só carrega as dimensões.
4. **Base da branch:** esta branch parte de `feature/transport-master-data`. O sub-projeto 1
   (CC/Item/rateio) **já está em `main`**, e portanto na base.
5. **CC-e:**
   - A da NF-e é texto livre; a do CT-e é estruturada (`infCorrecao`: grupo, campo e valor) e
     precisa ser construída.
   - A tela oferece uma **lista fechada de campos corrigíveis**, começando por `compl/xObs`,
     endereço e nome do recebedor/destinatário e produto predominante. Nunca valores, tributos,
     participantes por CNPJ ou datas.
   - Várias correções por evento.
6. **Transmissão em lote:** **job do Hangfire**, um CT-e por vez. O resultado aparece na lista e na
   barra de mensagens. Clicar num CT-e só continua síncrono.
7. **Cancelamento da averbação:**
   - Envia o `procEventoCTe` de cancelamento para o mesmo `/rest/CTE` da AT&M, como o leiaute dela
     prevê.
   - O `AverbaClient` ganha esse envio e o de averbação.
   - Validar contra o ambiente de homologação da AT&M antes de produção.
8. **Configuração própria do CT-e por filial:**
   - ambiente (homologação/produção) e séries **separados da NF-e**, para homologar o CT-e com a
     NF-e da CEAGUI em produção;
   - o **certificado é o mesmo** da NF-e da filial;
   - a tela é uma aba "CT-e" na configuração da NF-e.

## O que fica para quem constrói

- Nomes de classes, divisão em tarefas, leiaute exato das telas.
- Se a série ganha coluna de modelo na tabela de séries da NF-e ou vira tabela própria. O requisito
  é numeração independente por modelo e série, com a mesma trava.
- Como reaproveitar as bases genéricas da NF-e (`Nfe*ServiceBase`) sem mudar o comportamento da
  NF-e.
- `infRespTec`: preencher com os dados do responsável técnico vindos de configuração do servidor,
  nunca fixos no código. A NF-e hoje sai sem, e não muda aqui.

## Pendências que dependem do cliente

1. ~~Unidade da quantidade~~ **Respondido (22/10):**
   - A COSTA E COSTA transporta **combustível**, e a unidade é **M3** mesmo.
   - Outros clientes podem usar outra medida. Por isso a unidade padrão fica **configurável por
     filial** (aba CT-e, padrão M3) e editável em cada linha de quantidade, e o XML da NF-e
     importado não troca a unidade.
2. ~~Operação interestadual~~ **Respondido (22/10):**
   - A COSTA E COSTA opera **só dentro de SP**: início e fim em SP, CFOP de prestação interna.
   - Clientes futuros podem operar fora de SP. A regra de CFOP/alíquota por UF de início × fim
     continua genérica, testada para os dois casos, mas só o caso interno será homologado agora.
3. **Responsável técnico** (`infRespTec`): dados de quem assina pelo sistema.
