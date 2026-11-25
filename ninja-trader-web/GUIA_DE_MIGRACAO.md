# Do TradingView ao Classic Web Trader

## Reproduzir o layout MNQ

No TradingView, a referência é o layout **MNQ**, com **CME_MINI:MNQ1!** em candles de 1 minuto à esquerda e 15 minutos à direita. Na inspeção de 25/11/2026, o gráfico usava horário de Nova York, sessão eletrônica e ajuste de contratos ligado. No Classic, abra os dois gráficos no mesmo vencimento de MNQ. Um contrato individual pode divergir do contínuo ajustado, principalmente em níveis que atravessam a rolagem.

| Estudo | 1 minuto | 15 minutos |
| --- | --- | --- |
| LRM Liquidity | Visível | Visível |
| LRM Openings | Visível | Oculto |
| LRM Vertical Lines | Visível | Oculto |
| LRM Countdown | Visível | Visível |
| News° de toodegrees | Visível | Oculto |

**LRM Map** reúne os três primeiros estudos. **Use Liquidity Only On 15 Minute Chart** reproduz a diferença entre os painéis. **LRM Countdown** é um segundo indicador. O layout também contém **Psych Levels** no painel de 15m, com passo 50, destaque a cada 100 e nível principal a cada 1.000. Psych Levels e a tabela externa de notícias ficam fora destes arquivos.

## Instalar e aplicar

1. No Classic, abra o módulo **Code Editor**. No menu **File → New**, use o mesmo nome do arquivo local, incluindo `.js`, cole o código e salve com **⌘S**. Para atualizar um arquivo existente, abra-o na árvore antes de substituir o conteúdo.
2. Instale [LRMMap.js](LRMMap.js), [LRMCountdown.js](LRMCountdown.js), [LRMFibProjection.js](LRMFibProjection.js) e [LRMRectangle.js](LRMRectangle.js). O retângulo base também fornece o desenho usado pelas cinco cores.
3. Salve [Orange](LRMRectangleOrange.js), [Cyan](LRMRectangleCyan.js), [Red](LRMRectangleRed.js), [Purple](LRMRectanglePurple.js) e [Gray](LRMRectangleGray.js) na mesma pasta do retângulo base. Cada cor aparece como uma ferramenta **LRM Rectangle** com seu nome. Uma cor pode ser selecionada a cada novo desenho.
4. Em **Indicators**, adicione **LRM Map** e **LRM Countdown** aos gráficos de 1m e 15m. Em **Drawing Tools**, escolha uma das cinco cores e clique em dois cantos opostos. Para projetar a perna A–B a partir de C, selecione **LRM Fib Projection** e marque os três pivôs.
5. Para sincronizar desenhos no Classic, vincule os dois módulos pela mesma cor e ative **Drawing Tools → Sync Charts** em ambos. Confira com um retângulo de teste, movendo seu canto no 15m e observando o 1m. O TradingView estava com a replicação de desenhos no mesmo ticker e o magnetismo OHLC ligados. No Classic, **Crosshair → Snap** encaixa nos ticks de preço.

## Cores e divisões

No editor de elementos do gráfico, selecione o instrumento e ajuste as cores dos candles. Os seis valores abaixo foram lidos nos seletores do TradingView. A coloração por fechamento anterior estava desligada.

| Elemento | Alta | Baixa |
| --- | --- | --- |
| Corpo | `#5B9CF5` | `#9C9C9C` |
| Borda | `#0C3299` | `#000000` |
| Pavio | `#000000` | `#000000` |

Use fundo branco. A tabela seguinte registra a aparência **amostrada na captura** sobre branco. As transparências originais dos templates pessoais podem produzir a mesma aparência com outra combinação de cor e opacidade.

| Ferramenta | Borda | Linha de 50% | Preenchimento observado |
| --- | --- | --- | --- |
| Orange | `#F8D8A1` | `#F19D38` | `#FDF5E8` |
| Cyan | `#AAE2EC` | `#54B9D1` | `#E9F7FB` |
| Red | `#EFB2B6` | `#DF484C` | `#FBECED` |
| Purple | `#BFB1DE` | `#613CB0` | `#EFECF7` |
| Gray | `#CCCCCC` | `#000000` | `#E6E6E6` |

O retângulo genérico começa na cor cinza. Cada ferramenta nomeada começa na sua própria cor, com preenchimento a 10%. A configuração inicial desenha a linha de **50%**. **Divide Into Quarters** acrescenta os níveis de 25% e 75%. **Divide Into Eighths** mostra sete linhas internas em intervalos de 12,5% e inclui os quartos e o meio. Se ambas as opções estiverem ligadas, prevalecem os oitavos. Para uma zona vazia, desligue as duas opções e **Show Midline**.

## Configurações verificadas nos dois painéis

| Parâmetro | Valor do layout |
| --- | --- |
| Datas e histórico de sessões | DD/MM, cinco sessões anteriores por janela além da mais recente |
| Sessão em andamento e caixas | Ambas desligadas. Transparência das caixas em 90% |
| Extremos encerrados | 3 dias, 1 semana, 1 mês e 1 segunda-feira |
| Janelas de liquidez em NY | Ásia 20:00–00:00, Londres 02:00–05:00, NY AM 09:30–11:00, NY PM 13:30–16:00. Pré-mercado 08:00–09:30 desligado |
| Aberturas e marcadores habilitados | 00:00, 08:30, 09:30, 13:30. Aberturas e marcadores de 02:00 e 20:00 desligados |
| Histórico de aberturas | A versão instalada do Pine mostrava 1 ocorrência por horário. A versão nova usa 0 pregões anteriores para mostrar o pregão atual |
| Gaps | NWOG desligado, retenção 4. NDOG desligado, retenção 5 |
| Marcadores | Legenda inferior, três pregões anteriores, MOC desligado |
| Painel de risco | Contagem regressiva, lote e ATR ligados, texto grande, alerta nos últimos 5 s, risco US$ 100, stop 1,5 × ATR e ATR de 5 períodos |

## Conferir a migração

Compare primeiro um pregão com a mesma cobertura nas duas plataformas. No 1m, confira o preço da abertura de 09:30, um extremo conhecido de sessão e a barra do primeiro toque. No 15m, o mesmo nível de preço pode começar ou terminar em outra posição horizontal devido à agregação das barras. Carregue a sessão eletrônica inteira e um período anterior completo para conferir níveis de semana ou mês. O relógio e o ATR do painel dependem das atualizações do feed. Para conferir a geometria, desenhe uma zona de 80 pontos: os quartos ficam a cada 20 pontos e os oitavos a cada 10.
