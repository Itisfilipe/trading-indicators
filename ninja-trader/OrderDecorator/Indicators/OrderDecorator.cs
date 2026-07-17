#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    /// <summary>
    /// Annotates every working order on the chart's instrument with its distance from
    /// the position's average price (or from the market when flat): ticks, points,
    /// money value for the order's remaining quantity, and for profit-side orders the
    /// R multiple against the nearest stop. The platform draws the orders themselves;
    /// this only adds the numbers their markers never show.
    /// </summary>
    public class OrderDecorator : Indicator
    {
        // Snapshot of one working order taken under the account lock, so rendering
        // and label math never touch the live collection.
        private struct OrderInfo
        {
            public double Price;
            public int RemainingQuantity;
            public bool IsStopType;
            public string TypeLabel;
        }

        private SharpDX.Direct2D1.Brush profitBrushDx;
        private SharpDX.Direct2D1.Brush lossBrushDx;
        private SharpDX.Direct2D1.Brush neutralBrushDx;

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Show Points", Order = 0, GroupName = "Content")]
        public bool ShowPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Money Value", Order = 1, GroupName = "Content")]
        public bool ShowCurrency { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show R Multiple", Order = 2, GroupName = "Content",
                 Description = "On profit-side orders, append reward relative to the nearest stop (e.g. 1.5R).")]
        public bool ShowRMultiple { get; set; }

        [Range(0, 2000), NinjaScriptProperty]
        [Display(Name = "Right Margin (pixels)", Order = 3, GroupName = "Content",
                 Description = "Distance kept between the labels and the right edge, clearing the platform's own order markers.")]
        public int RightMarginPixels { get; set; }

        [XmlIgnore]
        [Display(Name = "Profit Side Color", Order = 4, GroupName = "Colors")]
        public System.Windows.Media.Brush ProfitColor { get; set; }

        [Browsable(false)]
        public string ProfitColorSerializable
        {
            get { return Serialize.BrushToString(ProfitColor); }
            set { ProfitColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Loss Side Color", Order = 5, GroupName = "Colors")]
        public System.Windows.Media.Brush LossColor { get; set; }

        [Browsable(false)]
        public string LossColorSerializable
        {
            get { return Serialize.BrushToString(LossColor); }
            set { LossColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Flat/Neutral Color", Order = 6, GroupName = "Colors")]
        public System.Windows.Media.Brush NeutralColor { get; set; }

        [Browsable(false)]
        public string NeutralColorSerializable
        {
            get { return Serialize.BrushToString(NeutralColor); }
            set { NeutralColor = Serialize.StringToBrush(value); }
        }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Labels every working order with its distance from the position average (ticks, points, money, R vs the nearest stop).";
                Name = "Order Decorator";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                ShowPoints = false;
                ShowCurrency = true;
                ShowRMultiple = true;
                RightMarginPixels = 220;
                ProfitColor = Brushes.SeaGreen;
                LossColor = Brushes.Crimson;
                NeutralColor = Brushes.DimGray;
            }
            else if (State == State.Terminated)
            {
                DisposeDxBrushes();
            }
        }

        // Render-only tool: all reads happen in OnRender against live account state.
        protected override void OnBarUpdate() { }

        public override void OnRenderTargetChanged()
        {
            DisposeDxBrushes();
            if (RenderTarget == null)
                return;
            profitBrushDx = ToDxBrush(ProfitColor);
            lossBrushDx = ToDxBrush(LossColor);
            neutralBrushDx = ToDxBrush(NeutralColor);
        }

        private void DisposeDxBrushes()
        {
            if (profitBrushDx != null) { profitBrushDx.Dispose(); profitBrushDx = null; }
            if (lossBrushDx != null) { lossBrushDx.Dispose(); lossBrushDx = null; }
            if (neutralBrushDx != null) { neutralBrushDx.Dispose(); neutralBrushDx = null; }
        }

        private SharpDX.Direct2D1.Brush ToDxBrush(System.Windows.Media.Brush wpfBrush)
        {
            // Fully qualified on both sides: System.Windows.Media and SharpDX.Direct2D1
            // each define SolidColorBrush, and the unqualified name is CS0104.
            System.Windows.Media.SolidColorBrush solid = wpfBrush as System.Windows.Media.SolidColorBrush;
            System.Windows.Media.Color c = solid != null ? solid.Color : Colors.Gray;
            return new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, new SharpDX.Color(c.R, c.G, c.B, c.A));
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartControl == null || RenderTarget == null || neutralBrushDx == null)
                return;

            Account account = ChartControl.OwnerChart?.ChartTrader?.Account;
            MasterInstrument master = Instrument?.MasterInstrument;
            if (account == null || master == null)
                return;

            // Snapshot working orders for this instrument under the collection lock;
            // everything after runs on copies.
            List<OrderInfo> workingOrders = new List<OrderInfo>();
            lock (account.Orders)
            {
                foreach (Order order in account.Orders)
                {
                    if (order.Instrument == null || order.Instrument.FullName != Instrument.FullName)
                        continue;
                    // PartFilled counts: the unfilled remainder is still resting, and
                    // the money value below is computed for exactly that remainder.
                    if (order.OrderState != OrderState.Working
                        && order.OrderState != OrderState.Accepted
                        && order.OrderState != OrderState.PartFilled)
                        continue;

                    bool isStopType = order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit;
                    // MIT rides the stop price field (settled in this repo's forum
                    // registry), so it prices from StopPrice without being risk-side.
                    bool pricedByStopField = isStopType || order.OrderType == OrderType.MIT;
                    double price = pricedByStopField ? order.StopPrice : order.LimitPrice;
                    if (price <= 0)
                        continue; // market orders and the like carry no resting price to label

                    string typeLabel;
                    if (order.OrderType == OrderType.StopMarket) typeLabel = "STP";
                    else if (order.OrderType == OrderType.StopLimit) typeLabel = "STP LMT";
                    else if (order.OrderType == OrderType.MIT) typeLabel = "MIT";
                    else typeLabel = "LMT";

                    workingOrders.Add(new OrderInfo
                    {
                        Price = price,
                        RemainingQuantity = Math.Max(0, order.Quantity - (int)order.Filled),
                        IsStopType = isStopType,
                        TypeLabel = typeLabel,
                    });
                }
            }
            // Scalars are copied inside the lock: the live Position object keeps
            // mutating after release, and mixing an old MarketPosition with a new
            // AveragePrice would label a frame inconsistently.
            bool hasPosition = false;
            bool positionIsLong = false;
            double positionAvgPrice = 0;
            int positionQuantity = 0;
            double pnlTicks = 0;
            double pnlPoints = 0;
            double pnlMoney = 0;
            lock (account.Positions)
            {
                foreach (Position candidate in account.Positions)
                {
                    if (candidate.Instrument != null
                        && candidate.Instrument.FullName == Instrument.FullName
                        && candidate.MarketPosition != MarketPosition.Flat)
                    {
                        hasPosition = true;
                        positionIsLong = candidate.MarketPosition == MarketPosition.Long;
                        positionAvgPrice = candidate.AveragePrice;
                        positionQuantity = candidate.Quantity;
                        // The platform's own P&L, not hand math against the last bar
                        // close: without a price argument NinjaTrader marks with last
                        // or bid/ask exactly as its P&L display settings dictate, so
                        // this label can never disagree in sign with ChartTrader.
                        pnlTicks = candidate.GetUnrealizedProfitLoss(PerformanceUnit.Ticks);
                        pnlPoints = candidate.GetUnrealizedProfitLoss(PerformanceUnit.Points);
                        pnlMoney = candidate.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                        break;
                    }
                }
            }

            // With a position, the average-price line gets its own label even when no
            // exit orders are working; flat with no working orders, nothing to draw.
            if (workingOrders.Count == 0 && !hasPosition)
                return;

            // In a position the reference is what execution actually cost; flat, the
            // market itself is the only meaningful reference -- and without bars
            // there is no market reference at all yet.
            if (!hasPosition && Bars.Count == 0)
                return;
            double referencePrice = hasPosition
                ? positionAvgPrice
                : Bars.GetClose(Bars.Count - 1);

            // Money per point: futures use PointValue alone; Forex additionally
            // scales by the account's lot size (the shipped RiskReward does the same).
            double pointMoney = master.PointValue;
            if (master.InstrumentType == InstrumentType.Forex)
                pointMoney *= account.ForexLotSize;

            // Risk basis for R multiples: the nearest working stop to the reference.
            double nearestStopDistance = 0;
            if (hasPosition)
                foreach (OrderInfo order in workingOrders)
                {
                    if (!order.IsStopType)
                        continue;
                    double distance = Math.Abs(order.Price - referencePrice);
                    if (distance > 0 && (nearestStopDistance == 0 || distance < nearestStopDistance))
                        nearestStopDistance = distance;
                }

            SharpDX.DirectWrite.TextFormat textFormat = chartControl.Properties.LabelFont.ToDirectWriteTextFormat();
            try
            {
                float panelRight = ChartPanel.X + ChartPanel.W;

                // The execution (average price) line: live unrealized P&L in ticks and
                // money, so the entry line answers "where am I" without the trader
                // toggling the platform's own P&L display.
                if (hasPosition)
                {
                    bool inProfit = pnlMoney > 0;

                    float avgY = chartScale.GetYByValue(positionAvgPrice);
                    if (avgY >= ChartPanel.Y && avgY <= ChartPanel.Y + ChartPanel.H)
                    {
                        // Signed custom formats: fractional values keep a decimal
                        // instead of collapsing to a signed zero that contradicts
                        // the money value and the color.
                        string positionLabel = string.Format("{0} {1}  {2}t",
                            positionIsLong ? "LONG" : "SHORT", positionQuantity,
                            pnlTicks.ToString("+0.#;-0.#;0"));
                        if (ShowPoints)
                            positionLabel += string.Format("  {0}pt", pnlPoints.ToString("+0.00##;-0.00##;0"));
                        if (ShowCurrency)
                            positionLabel += string.Format("  {0}{1}",
                                pnlMoney > 0 ? "+" : (pnlMoney < 0 ? "-" : ""),
                                Core.Globals.FormatCurrency(Math.Abs(pnlMoney)));

                        SharpDX.Direct2D1.Brush positionBrush = pnlMoney.ApproxCompare(0) == 0
                            ? neutralBrushDx : (inProfit ? profitBrushDx : lossBrushDx);
                        SharpDX.DirectWrite.TextLayout positionLayout = new SharpDX.DirectWrite.TextLayout(
                            Core.Globals.DirectWriteFactory, positionLabel, textFormat, 600, textFormat.FontSize);
                        try
                        {
                            // Below the line, where order labels never draw (they sit
                            // above it): a stop moved to breakeven rests exactly on the
                            // average price, and the two labels must stack, not overlap.
                            float px = panelRight - RightMarginPixels - positionLayout.Metrics.Width;
                            RenderTarget.DrawTextLayout(new SharpDX.Vector2(px, avgY + 3),
                                positionLayout, positionBrush);
                        }
                        finally
                        {
                            positionLayout.Dispose();
                        }
                    }
                }

                foreach (OrderInfo order in workingOrders)
                {
                    float y = chartScale.GetYByValue(order.Price);
                    if (y < ChartPanel.Y || y > ChartPanel.Y + ChartPanel.H)
                        continue;

                    double signedPoints = order.Price - referencePrice;
                    double ticks = Math.Abs(signedPoints) / master.TickSize;
                    double money = Math.Abs(signedPoints) * pointMoney * order.RemainingQuantity;

                    // Which side of the reference an order rests on decides win/lose:
                    // for a long, anything above average pays and anything below costs;
                    // shorts mirror. Flat has no side, so it stays neutral.
                    bool isProfitSide = false;
                    SharpDX.Direct2D1.Brush brush = neutralBrushDx;
                    if (hasPosition)
                    {
                        isProfitSide = positionIsLong ? signedPoints > 0 : signedPoints < 0;
                        brush = isProfitSide ? profitBrushDx : lossBrushDx;
                    }

                    string label = string.Format("{0} {1:F0}t", order.TypeLabel, ticks);
                    if (ShowPoints)
                        label += string.Format("  {0:0.00}pt", Math.Abs(signedPoints));
                    if (ShowCurrency)
                        label += string.Format("  {0}{1}", !hasPosition ? "" : (isProfitSide ? "+" : "-"),
                            Core.Globals.FormatCurrency(money));
                    if (ShowRMultiple && isProfitSide && nearestStopDistance > 0)
                        label += string.Format("  {0:0.0}R", Math.Abs(signedPoints) / nearestStopDistance);

                    SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(
                        Core.Globals.DirectWriteFactory, label, textFormat, 600, textFormat.FontSize);
                    try
                    {
                        float x = panelRight - RightMarginPixels - layout.Metrics.Width;
                        RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, y - textFormat.FontSize - 3), layout, brush);
                    }
                    finally
                    {
                        layout.Dispose();
                    }
                }
            }
            finally
            {
                textFormat.Dispose();
            }
        }
    }
}
