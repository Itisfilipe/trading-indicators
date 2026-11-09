# Profit Chart (NTSL)

Conjunto de indicadores e regras de coloracao para day trading no Profit Chart (Nelogica), escritos em NTSL.

> O manual NTSL da Nelogica nao faz parte deste repositorio (conteudo da Nelogica).
> Para gerar uma copia local em markdown a partir do seu proprio PDF do manual, use
> `convert_to_md.py`.

## Como instalar

1. Abra o **Editor de Estrategias** no Profit Chart
2. Crie uma nova estrategia do tipo correto:
   - **Indicador** para arquivos de indicador
   - **Coloracao** para regras de coloracao (arquivos com "coloring" no nome)
3. Copie e cole o conteudo do arquivo `.ntsl` desejado
4. Compile (F5 ou botao "Compilar")
5. Aplique no grafico:
   - Indicadores: clique direito no grafico -> "Inserir Indicador"
   - Coloracao: clique direito no grafico -> "Inserir Regra de Coloracao"

### Tema escuro ou claro

Todos os indicadores possuem o parametro `Tema_Escuro`. Ao aplicar cada indicador, configure:
- `Tema_Escuro = true` para fundos escuros (padrao; cores casam com candles verde #3DE295 e vermelho #E22B3D em fundo preto)
- `Tema_Escuro = false` para fundos claros

---

## Indicadores

### Confluence Coloring (Regra de Coloracao)

**Arquivo:** `confluence-coloring.ntsl`

O indicador principal do sistema. Pinta cada candle/brick com base na confluencia de multiplas regras, identificando visualmente os melhores momentos para entrar em uma operacao.

**Tipos de sinal:**

| Sinal | O que significa | Tema escuro (padrao) | Tema claro |
|-------|----------------|----------------------|------------|
| STRONG BUY | Compra a favor da tendencia maior, com volume e agressao confirmando | Azul | Azul |
| STRONG SELL | Venda a favor da tendencia maior | Fucsia | Vermelho |
| SCALP BUY | Compra contra tendencia maior, MACD menor confirmou a virada | Azul escuro | Azul claro |
| SCALP SELL | Venda contra tendencia maior | Fucsia escuro | Vermelho escuro |
| REJ BUY | Compra por rejeicao (agressao vendedora forte mas preco segurou + pavio grande) | Azul | Azul |
| REJ SELL | Venda por rejeicao | Fucsia | Vermelho |

**Cores de tendencia (quando nao ha gatilho):**

| Significado | Tema escuro (padrao) | Tema claro |
|-------------|----------------------|------------|
| Ambos MACDs concordam para alta | Verde do candle | Verde |
| Apenas um MACD para alta | Verde escuro | Verde claro |
| Ambos MACDs concordam para baixa | Vermelho do candle | Preto |
| Apenas um MACD para baixa | Vermelho escuro | Cinza |

**Regras que precisam concordar:**
1. **MACD maior** (200/800/50) — define a tendencia principal. Sinais a favor = strong, contra = scalp
2. **MACD menor** (72/200/34) — habilita scalps quando vira contra a tendencia maior
3. **Tape reading** — volume e/ou agressao acima da media de N periodos
4. **Pullback na EMA** — brick tocando a zona entre as EMAs 21/42 apos pullback (lookback: 6 bricks, max 2 inteiramente alem da EMA lenta)
5. **Rejeicao** — agressao contra a direcao do sinal + pavio >= 1.5x o corpo (cor do candle irrelevante)

**Parametros principais:**
- `Major_MACD_Fast/Slow/Signal` — periodos do MACD maior
- `Minor_MACD_Fast/Slow/Signal` — periodos do MACD menor
- `Tape_MA_Saldo(500)` — periodo da media de agressao
- `Tape_MA_Vol(300)` — periodo da media de volume
- `EMA_Fast(21)` / `EMA_Slow(42)` — EMAs do filtro de tendencia
- `Rej_Wick_Ratio(1.5)` — razao minima pavio/corpo para rejeicao
- `EMA_Zone_Tolerance(2)` — tolerancia em ticks para considerar candle na zona EMA
- `Ignore_Pullback(false)` — ignorar regra de pullback (so exige direcao + zona EMA)
- `Enable_Scalp(true)` — ativar/desativar sinais de scalp
- `Trend_Follow_Major(true)` — cor da tendencia segue o MACD maior

---

### Confluence Labels (Indicador)

**Arquivo:** `confluence-labels.ntsl`

Complemento do Confluence Coloring. Plota labels de texto acima ou abaixo dos bricks que disparam gatilhos.

**Labels:**
- `B` = Buy, `S` = Sell
- `BS` = Buy Scalp, `SS` = Sell Scalp
- `BR` = Buy Rejeicao, `SR` = Sell Rejeicao
- `BSR` = Buy Scalp Rejeicao, `SSR` = Sell Scalp Rejeicao

**Parametros adicionais:**
- `Label_Size(8)` — tamanho da fonte
- `Label_Offset_Ticks(8)` — distancia do label ao High/Low em ticks

> Manter os parametros sincronizados com o Confluence Coloring!

---

### Confluence Letreiro (Indicador)

**Arquivo:** `confluence-letreiro.ntsl`

Letreiro visual em sub-janela separada. Quando um gatilho dispara, o letreiro pisca com uma barra colorida e o nome do sinal (BUY, SELL, BSCALP, etc).

**Como usar:**
1. Aplique como indicador em uma sub-janela separada
2. O letreiro fica vazio quando nao ha gatilho
3. Quando um sinal dispara, a barra aparece com a cor e o texto do sinal
4. Para usar como "tela cheia": remova o grafico e deixe so o indicador, zoom no nivel do candle

**Parametros adicionais:**
- `Label_Size(38)` — tamanho da fonte do texto

---

### Tape Reading (Indicador)

**Arquivo:** `tape-reading.ntsl`

Mostra a forca dos compradores vs vendedores e o volume de contratos em histogramas normalizados.

**Como ler:**
- **Acima do zero** = agressao (quem esta empurrando o preco)
  - Verde = compradores dominando
  - Vermelho = vendedores dominando
  - Barra acima da linha de referencia = agressao acima da media (sinal forte)
  - Barra cinza = agressao abaixo da media
- **Abaixo do zero** = volume de contratos
  - Azul claro = volume acima da media (mercado ativo)
  - Cinza = volume abaixo da media (mercado parado)
- **Linhas de referencia** = referencia da media (valor 1.0)
- Valores normalizados: 1.0 = na media, 2.0 = o dobro da media

**Parametros:**
- `Tape_MA_Saldo(500)` — periodo da media de agressao
- `Tape_MA_Vol(300)` — periodo da media de volume

---

### MACD Histogram (Indicador)

**Arquivo:** `macd-histogram.ntsl`

Histograma do MACD com linha de contorno conectando os topos.

**Como ler:**
- Barras verdes = momentum positivo (compradores dominando)
- Barras vermelhas = momentum negativo (vendedores dominando)
- Histograma crescendo = momentum acelerando
- Histograma diminuindo = momentum desacelerando
- Cruzou o zero = possivel mudanca de tendencia
- Linha de contorno conecta os topos para facilitar ver divergencias

**Parametros:**
- `Periodo_EMA_Rapida(21)` — EMA rapida
- `Periodo_EMA_Lenta(89)` — EMA lenta
- `Periodo_Sinal(42)` — linha de sinal
- `Exibir_Linha_Contorno(true)` — mostrar linha de contorno

---

### MA Cloud (Indicador)

**Arquivo:** `ma-cloud.ntsl`

Duas EMAs que formam uma "nuvem" entre elas. Mostra visualmente a direcao e forca da tendencia.

**Como ler:**
- Verde = tendencia de alta (EMA rapida acima da lenta)
- Vermelho = tendencia de baixa
- Cinza = preco contradiz a tendencia (cautela!)

**Como ativar o preenchimento:**
1. Clique direito em uma das linhas no grafico
2. "Propriedades" -> aba "Preenchimento"
3. Marque "Preencher entre linhas" com Plot 1 e Plot 2

**Parametros:**
- `Periodo_EMA_Rapida(17)` — EMA rapida
- `Periodo_EMA_Lenta(34)` — EMA lenta
- `Exibir_Neutro(true)` — cinza quando o preco contradiz a tendencia; desligado, fica sempre na cor da tendencia

---

### MA Cloud Coloring (Regra de Coloracao)

**Arquivo:** `ma-cloud-coloring.ntsl`

Versao como regra de coloracao da MA Cloud. Pinta os candles em vez de desenhar linhas.

Mesmos parametros e logica da MA Cloud.

---

### Bias Coloring (Regra de Coloracao)

**Arquivo:** `bias-coloring.ntsl`

Mostra o "bias" (tendencia geral) baseado na posicao do preco em relacao a uma EMA.

**Como funciona:**
- Brick inteiro acima da EMA = bias de compra (verde)
- Brick inteiro abaixo da EMA = bias de venda (escuro/vermelho)
- Brick cruzando a EMA = mantem o bias anterior

**Parametros:**
- `EMA_Period(55)` — periodo da EMA

---

### Day Open (Indicador)

**Arquivo:** `day-open.ntsl`

Linha horizontal no preco de abertura de cada dia: a de hoje e as dos dias anteriores, ate `Max_Dias_Passados`.

**Como usar:**
- Preco acima da linha = dia positivo ate agora
- Preco abaixo da linha = dia negativo ate agora
- Funciona como suporte/resistencia natural do dia

**Parametros:**
- `Tema_Escuro(true)` — cores do tema
- `Max_Dias_Passados(5)` — dias anteriores com linha (maximo 5)
- `Estilo_Linha(2)` — 0 = solido, 1 = traco longo, 2 = traco curto
- `Espessura_Linha(2)` — espessura da linha

---

### SMC Openings (Indicador)

**Arquivo:** `smc_openings.ntsl`

Linhas horizontais nos precos de abertura chave: Midnight Open (00:00 NY), Weekly Open (domingo 18:00 NY), 8:30, 9:30 e 13:30 NY. Cada tipo guarda ate 5 dias passados.

**Parametros:**
- `Horario_Em_NY(true)` — horarios digitados em hora de NY; desligado, em hora do Brasil
- `Offset_NY_Para_BR(60)` — minutos somados ao horario de NY: 60 do 2o domingo de marco ao 1o domingo de novembro (horario de verao dos EUA), 120 no resto do ano
- `Ativar_*`, `*_Hora`, `*_Label` — liga, horario e texto de cada abertura
- `Hora_Fim_Linha(1800)` — onde as linhas intraday terminam (18:00 NY)
- `Max_Sessoes_Passadas(5)` — dias anteriores mostrados (maximo 5)
- `Estilo_Linha(2)` / `Espessura_Linha(2)` — estilo das linhas

---

### SMC Sessions (Indicador)

**Arquivo:** `smc_sessions.ntsl`

Caixas em volta das killzones, cada uma com sua cor: Asia (20:00–00:00 NY), Londres (02:00–05:00), NY AM (09:30–11:00), NY Lunch (12:00–13:00) e NY PM (13:30–16:00). A maxima e a minima da sessao mais recente se estendem para a direita como niveis de liquidez.

**Parametros:**
- `Horario_Em_NY(true)` / `Offset_NY_Para_BR(60)` — mesmo esquema do SMC Openings
- `Ativar_*`, `*_Inicio`, `*_Fim` — liga e horarios de cada sessao
- `Mostrar_Niveis_Sessao(false)` — mostra os niveis de maxima/minima da sessao
- `Espessura_Borda_Sessao(2)` — espessura da borda das caixas
- `Max_Sessoes_Passadas(5)` — sessoes anteriores mostradas por tipo (maximo 5)

---

### Psych Levels (Indicador)

**Arquivo:** `psych-levels.ntsl`

Linhas horizontais nos niveis redondos de preco, em tres estilos: regular a cada `Passo` (ex.: 29350), destaque a cada `Destaque_A_Cada` (ex.: 29300) e maior a cada `Maior_A_Cada` (ex.: 29000). Cobre `Niveis_Acima_Abaixo` niveis em volta do preco e acrescenta novos quando o preco anda.

**Parametros:**
- `Tema_Escuro(true)` — cores do tema
- `Passo(50)` / `Destaque_A_Cada(100)` / `Maior_A_Cada(1000)` — intervalos em pontos
- `Niveis_Acima_Abaixo(40)` — niveis desenhados acima e abaixo do preco
- `Precos_Regular(false)` / `Precos_Destaque(false)` / `Precos_Maior(false)` — escreve o preco nas linhas de cada tipo
- `Precos_A_Esquerda(false)` — preco na ponta esquerda em vez da direita
- `Casas_Decimais(0)` — casas decimais do preco
- `Estilo_*` (0 = solido, 1 = traco longo, 2 = traco curto) e `Espessura_*` — estilo de cada nivel

---

### Renko Size Calculator (Indicador)

**Arquivo:** `renko-size-calculator.ntsl`

Tamanho sugerido de box Renko para o tempo grafico em que ele esta aplicado.

**Como usar:**
1. Aplique no grafico de tempo que voce quer medir (1min, 5min, 15min...)
   e escolha **nova janela** ao inserir; no grafico de preco a escala em
   ticks esmaga os candles
2. Por padrao aparece uma linha: a **mediana em ticks**, que ja e a metade
   da mediana das medias diarias do True Range dos ultimos dias. Esse e o
   box sugerido. Ela so muda na virada do dia e pode repetir o mesmo valor
   por alguns dias; e o que a torna estavel
3. O rotulo no ultimo candle mostra o tempo grafico e o valor de cada linha
   ligada, com o nome. Ex.: `5 min | mediana (5d): 101 ticks`
4. Para comparar tempos graficos, abra um grafico de cada tempo

**Parametros:**
- `Periodo_Dias(5)` — dias usados no ATR e na mediana (maximo 60)
- `Ignorar_Gaps(true)` — ignorar o gap de abertura
- Linhas (cada uma entra no rotulo quando ligada):
  - `Exibir_Mediana_Ticks(true)` — box sugerido (metade da mediana, em ticks)
  - `Exibir_Meio_ATR_Ticks(false)` — metade do ATR atual, em ticks
  - `Exibir_Meio_ATR(false)` — metade do ATR atual, em pontos
  - `Exibir_ATR(false)` — ATR atual (media exponencial), em pontos
- `Exibir_Rotulo(true)`, `Casas_Decimais(1)`, `Tamanho_Fonte(9)`, `Tema_Escuro(true)`

**Observacoes:**
- Linhas em pontos e em ticks dividem a mesma escala; ligue uma unidade por vez
  para ver as duas bem
- A mediana aparece a partir do segundo dia completo carregado no grafico;
  ate la o rotulo mostra "aguardando"
- O "dia" e a data do calendario, com todos os candles do grafico

---

## Setup recomendado

Para day trading com Renko, recomendamos usar:

1. **Confluence Coloring** — regra de coloracao principal (mostra tendencia + gatilhos)
2. **Confluence Labels** — labels de texto nos gatilhos
3. **Confluence Letreiro** — alerta visual em sub-janela
4. **MA Cloud** — nuvem de EMAs para visualizar a zona de entrada
5. **Tape Reading** — sub-janela para acompanhar volume e agressao
6. **MACD Histogram** — sub-janela para acompanhar momentum

Opcional:
- **Day Open** — referencia de abertura do dia
- **Renko Size Calculator** — calcular tamanho do box (aplicar em grafico de tempo)
- **Bias Coloring** — usar em grafico separado para bias de longo prazo

---

## Documentacao

A referencia da linguagem NTSL nao e versionada (conteudo da Nelogica). Coloque o `ManualNTSL.pdf` em `profit-chart/` e rode `python3 convert_to_md.py` (precisa do `pdftotext`, do poppler) para gerar `profit-chart/docs/` localmente.

## Licenca

Este projeto e fornecido para uso pessoal e educacional.
