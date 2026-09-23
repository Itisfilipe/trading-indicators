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

| Sinal | O que significa | Cor (tema claro) |
|-------|----------------|------------------|
| STRONG BUY | Compra a favor da tendencia maior, com volume e agressao confirmando | Azul |
| STRONG SELL | Venda a favor da tendencia maior | Vermelho |
| SCALP BUY | Compra contra tendencia maior, MACD menor confirmou a virada | Azul claro |
| SCALP SELL | Venda contra tendencia maior | Vermelho escuro |
| REJ BUY | Compra por rejeicao (agressao vendedora forte mas preco segurou + pavio grande) | Azul |
| REJ SELL | Venda por rejeicao | Vermelho |

**Cores de tendencia (quando nao ha gatilho):**

| Cor | Significado |
|-----|-------------|
| Verde | Ambos MACDs concordam para alta |
| Verde claro | Apenas um MACD para alta |
| Preto | Ambos MACDs concordam para baixa |
| Cinza | Apenas um MACD para baixa |

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
  - Barra acima da linha branca = agressao acima da media (sinal forte)
  - Barra cinza = agressao abaixo da media
- **Abaixo do zero** = volume de contratos
  - Azul claro = volume acima da media (mercado ativo)
  - Cinza = volume abaixo da media (mercado parado)
- **Linhas brancas** = referencia da media (valor 1.0)
- Valores normalizados: 1.0 = na media, 2.0 = o dobro da media

**Parametros:**
- `Tape_MA_Saldo(500)` — periodo da media de agressao
- `Tape_MA_Vol(300)` — periodo da media de volume

---

### MACD Histogram (Indicador)

**Arquivo:** `macd-histogram.ntsl`

Histograma do MACD com linha de contorno conectando os topos. Os periodos sao os mesmos do MACD menor do sistema de confluencia.

**Como ler:**
- Barras verdes = momentum positivo (compradores dominando)
- Barras vermelhas = momentum negativo (vendedores dominando)
- Histograma crescendo = momentum acelerando
- Histograma diminuindo = momentum desacelerando
- Cruzou o zero = possivel mudanca de tendencia
- Linha branca conecta os topos para facilitar ver divergencias

**Parametros:**
- `Periodo_EMA_Rapida(21)` — EMA rapida
- `Periodo_EMA_Lenta(89)` — EMA lenta
- `Periodo_Sinal(42)` — linha de sinal
- `Exibir_Linha_Contorno(true)` — mostrar linha branca

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

Linha horizontal no preco de abertura do dia. Reseta automaticamente a cada novo dia.

**Como usar:**
- Preco acima da linha = dia positivo ate agora
- Preco abaixo da linha = dia negativo ate agora
- Funciona como suporte/resistencia natural do dia

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
- `Apenas_RTH(false)`, `Horario_Em_NY(true)`, `Offset_NY_Para_BR(60)`, `RTH_Inicio(930)`, `RTH_Fim(1600)` — usar so o pregao regular
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
- O "dia" e a data do calendario: em ativos com sessao noturna, ligue
  `Apenas_RTH` para medir so o pregao regular

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

## TradingView (Pine Script)

Indicadores em Pine Script v6, na pasta `tradingview/`. Nao rodam no Profit Chart — sao para o TradingView.

**Como instalar:** abra o **Pine Editor** no TradingView, cole o conteudo do arquivo `.pine`, clique em **Add to chart**.

### Candle Countdown & Position Sizer

**Arquivo:** `tradingview/candle-countdown-position-sizer.pine`

Tabela sobre o grafico com relogio, contagem regressiva para o proximo candle e sugestao de tamanho de posicao por ATR. Voce informa quanto quer arriscar e a largura do stop (em multiplo de ATR); ele devolve o tamanho de lote que mantem a perda nesse risco caso o stop seja atingido.

**Linhas da tabela:**
- **Time** — relogio com segundos, no fuso escolhido
- **Countdowns** — tempo ate o proximo candle, em ate 3 timeframes selecionaveis (pode ligar/desligar cada um e o grupo todo)
- **Lot** — tamanho de lote sugerido (arredondado)
- **ATR** — valor bruto do ATR
- **Stop ATR (n)** — distancia do stop em pontos/preco (n = multiplo de ATR)
- **Stop Cash (x%)** — dinheiro em risco no lote sugerido; aparece so no modo % of Account

**Parametros principais:**
- `Risk Mode` — `Fixed $` (arrisca valor fixo) ou `% of Account` (arrisca % do saldo)
- `Risk Per Trade ($)` / `Account Size ($)` / `Risk Per Trade (%)` — conforme o Risk Mode
- `Stop Size (× ATR)` — distancia do stop como multiplo do ATR
- `ATR Length(5)` / `ATR Smoothing` — periodo e suavizacao (EMA ou RMA/Wilder)
- `Show Countdowns` + `Timeframe 1/2/3` — quais candles contar
- `Highlight Countdown Before New Candle` + `Alert Lead Time` — destaque antes do proximo candle

### Time-Based Price Levels

**Arquivo:** `tradingview/time-based-price-levels.pine`

Plota linhas horizontais em ate 10 niveis de preco. Cada linha e um horario intraday (HH:MM no fuso escolhido) ou a abertura de um periodo maior: Diario, Semanal, Mensal, Trimestral, Semestral ou Anual.

**Por linha voce escolhe:** liga/desliga, horario/periodo, tipo de preco (O/H/L/C) e cor. Largura e estilo das linhas sao globais para manter o painel compacto.

**Parametros principais:**
- `Time Zone` — fuso dos horarios intraday
- `Days of History to Show(0)` — dias anteriores a exibir (0 = so o dia atual)
- `Show Labels` / `Label Offset` / `Label Size` — labels automaticos (D.O, W.O, ... ou o horario)
- `Line Width` / `Line Style` — estilo global das linhas

## Documentacao

A pasta `profit-chart/docs/` contem a referencia completa da linguagem NTSL (27 capitulos, em portugues), convertida do manual oficial da Nelogica.

## Licenca

Este projeto e fornecido para uso pessoal e educacional.
