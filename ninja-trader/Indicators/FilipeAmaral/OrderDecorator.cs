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
    /// Annotates every working order on the chart's instrument with a box centered on
    /// its line: STOP or ALVO with the quantity, points times quantity and the money
    /// value against the position's average price (or the market when flat), red for
    /// exit stops, green for exit targets, neutral for entries. The platform draws
    /// the orders themselves; this only adds the numbers their markers never show.
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
            public bool IsBuy;
        }

        private struct PendingLabel
        {
            public SharpDX.DirectWrite.TextLayout Layout;
            public SharpDX.Direct2D1.Brush Brush;
            public float Top;
            public float ShiftLeft;
        }

        private SharpDX.Direct2D1.Brush profitBrushDx;
        private SharpDX.Direct2D1.Brush lossBrushDx;
        private SharpDX.Direct2D1.Brush neutralBrushDx;
        private SharpDX.Direct2D1.Brush borderBrushDx;
        private SharpDX.Direct2D1.Brush textBrushDx;
        private SharpDX.Direct2D1.Brush shadowBrushDx;
        private const float BoxPaddingX = 4f;
        private const float BoxPaddingY = 2f;
        private const string FieldSeparator = "  :  ";
        private static readonly System.Globalization.CultureInfo LabelCulture = new System.Globalization.CultureInfo("pt-BR");

        #region Properties

        [NinjaScriptProperty]
        [Display(Name = "Show Ticks", Order = 0, GroupName = "1. Content")]
        public bool ShowTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Points", Order = 1, GroupName = "1. Content")]
        public bool ShowPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Money Value", Order = 2, GroupName = "1. Content")]
        public bool ShowCurrency { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show R Multiple", Order = 3, GroupName = "1. Content",
                 Description = "On profit-side orders, append reward relative to the nearest stop (e.g. 1.5R).")]
        public bool ShowRMultiple { get; set; }

        [Range(0, 2000), NinjaScriptProperty]
        [Display(Name = "Right Margin (pixels)", Order = 4, GroupName = "1. Content",
                 Description = "Distance kept between the labels and the right edge, clearing the platform's own order markers.")]
        public int RightMarginPixels { get; set; }

        [Display(Name = "Label the Average Price", Order = 5, GroupName = "1. Content",
                 Description = "Add a box on the position's average-price line with side, quantity and unrealized P&L.")]
        public bool ShowPositionLabel { get; set; }

        [Display(Name = "Keep Labels Apart", Order = 6, GroupName = "1. Content",
                 Description = "Push labels of nearby orders apart instead of letting them overlap on their own lines.")]
        public bool AvoidOverlap { get; set; }

        [Range(0, 20)]
        [Display(Name = "Corner Radius", Order = 1, GroupName = "3. Box")]
        public int CornerRadius { get; set; }

        [Range(0.0, 1.0)]
        [Display(Name = "Box Opacity", Order = 2, GroupName = "3. Box")]
        public double BoxOpacity { get; set; }

        [Display(Name = "Show Shadow", Order = 3, GroupName = "3. Box")]
        public bool ShowShadow { get; set; }

        [Range(0, 20)]
        [Display(Name = "Shadow Offset", Order = 4, GroupName = "3. Box")]
        public int ShadowOffset { get; set; }

        [Range(0.0, 1.0)]
        [Display(Name = "Shadow Opacity", Order = 5, GroupName = "3. Box")]
        public double ShadowOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Border Color", Order = 7, GroupName = "2. Colors")]
        public System.Windows.Media.Brush BorderColor { get; set; }

        [Browsable(false)]
        public string BorderColorSerializable
        {
            get { return Serialize.BrushToString(BorderColor); }
            set { BorderColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Text Color", Order = 8, GroupName = "2. Colors")]
        public System.Windows.Media.Brush TextColor { get; set; }

        [Browsable(false)]
        public string TextColorSerializable
        {
            get { return Serialize.BrushToString(TextColor); }
            set { TextColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Target / Profit Side Background", Order = 4, GroupName = "2. Colors")]
        public System.Windows.Media.Brush ProfitColor { get; set; }

        [Browsable(false)]
        public string ProfitColorSerializable
        {
            get { return Serialize.BrushToString(ProfitColor); }
            set { ProfitColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Stop / Loss Side Background", Order = 5, GroupName = "2. Colors")]
        public System.Windows.Media.Brush LossColor { get; set; }

        [Browsable(false)]
        public string LossColorSerializable
        {
            get { return Serialize.BrushToString(LossColor); }
            set { LossColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Flat/Neutral Background", Order = 6, GroupName = "2. Colors")]
        public System.Windows.Media.Brush NeutralColor { get; set; }

        [Browsable(false)]
        public string NeutralColorSerializable
        {
            get { return Serialize.BrushToString(NeutralColor); }
            set { NeutralColor = Serialize.StringToBrush(value); }
        }

        #endregion

        private static System.Windows.Media.Brush Rgb(byte r, byte g, byte b)
        {
            System.Windows.Media.SolidColorBrush brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Labels every working order with STOP or ALVO, its quantity, points times quantity and money value against the position average.";
                Name = "Order Decorator";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                ShowTicks = false;
                ShowPoints = true;
                ShowCurrency = true;
                ShowRMultiple = false;
                ShowPositionLabel = false;
                AvoidOverlap = false;
                RightMarginPixels = 105;
                CornerRadius = 4;
                BoxOpacity = 0.85;
                ShowShadow = true;
                ShadowOffset = 2;
                ShadowOpacity = 0.35;
                ProfitColor = Rgb(0x00, 0x64, 0x00);
                LossColor = Rgb(0x80, 0x00, 0x00);
                NeutralColor = Rgb(0x5F, 0x67, 0x73);
                BorderColor = Rgb(0xF0, 0xF8, 0xFF);
                TextColor = Brushes.White;
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
            profitBrushDx.Opacity = (float)BoxOpacity;
            lossBrushDx.Opacity = (float)BoxOpacity;
            neutralBrushDx.Opacity = (float)BoxOpacity;
            borderBrushDx = ToDxBrush(BorderColor);
            textBrushDx = ToDxBrush(TextColor);
            shadowBrushDx = ToDxBrush(Brushes.Black);
            shadowBrushDx.Opacity = (float)ShadowOpacity;
        }

        private void DisposeDxBrushes()
        {
            if (profitBrushDx != null) { profitBrushDx.Dispose(); profitBrushDx = null; }
            if (lossBrushDx != null) { lossBrushDx.Dispose(); lossBrushDx = null; }
            if (neutralBrushDx != null) { neutralBrushDx.Dispose(); neutralBrushDx = null; }
            if (borderBrushDx != null) { borderBrushDx.Dispose(); borderBrushDx = null; }
            if (textBrushDx != null) { textBrushDx.Dispose(); textBrushDx = null; }
            if (shadowBrushDx != null) { shadowBrushDx.Dispose(); shadowBrushDx = null; }
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

            if (Bars == null || ChartControl == null || ChartPanel == null
                || RenderTarget == null || neutralBrushDx == null || IsInHitTest)
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

                    workingOrders.Add(new OrderInfo
                    {
                        Price = price,
                        RemainingQuantity = Math.Max(0, order.Quantity - (int)order.Filled),
                        IsStopType = isStopType,
                        IsBuy = order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.BuyToCover,
                    });
                }
            }
            // Orders resting on the same price render as one label with the summed
            // quantity: the platform stacks their markers into a single row, and
            // stacked label text is unreadable (typical case: several bracket stops
            // parked together at breakeven).
            Dictionary<long, OrderInfo> mergedByPrice = new Dictionary<long, OrderInfo>();
            List<long> mergeOrder = new List<long>();
            foreach (OrderInfo order in workingOrders)
            {
                // Same price, side and type only, so an exit never takes another order's role.
                long priceKey = (long)Math.Round(order.Price / master.TickSize) * 4
                    + (order.IsBuy ? 2 : 0) + (order.IsStopType ? 1 : 0);
                OrderInfo existing;
                if (mergedByPrice.TryGetValue(priceKey, out existing))
                {
                    existing.RemainingQuantity += order.RemainingQuantity;
                    mergedByPrice[priceKey] = existing;
                }
                else
                {
                    mergedByPrice[priceKey] = order;
                    mergeOrder.Add(priceKey);
                }
            }
            workingOrders.Clear();
            foreach (long priceKey in mergeOrder)
                workingOrders.Add(mergedByPrice[priceKey]);

            // Scalars are copied inside the lock: the live Position object keeps
            // mutating after release, and mixing an old MarketPosition with a new
            // AveragePrice would label a frame inconsistently.
            bool hasPosition = false;
            bool positionIsLong = false;
            double positionAvgPrice = 0;
            int positionQuantity = 0;
            bool pnlKnown = false;
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
                        // Which mark that is depends on those settings and the feed,
                        // so no null pre-check can cover every combination; between
                        // a connect and the first quotes the call derefs the missing
                        // mark, and the label carries side and quantity only.
                        try
                        {
                            pnlTicks = candidate.GetUnrealizedProfitLoss(PerformanceUnit.Ticks);
                            pnlPoints = candidate.GetUnrealizedProfitLoss(PerformanceUnit.Points);
                            pnlMoney = candidate.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                            pnlKnown = true;
                        }
                        catch (NullReferenceException)
                        {
                        }
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

            // Risk basis for R multiples: the nearest working stop on the loss side of
            // the reference; a stop already trailed into profit carries no risk.
            double nearestStopDistance = 0;
            if (hasPosition)
                foreach (OrderInfo order in workingOrders)
                {
                    if (!order.IsStopType)
                        continue;
                    bool isLossSide = positionIsLong ? order.Price < referencePrice : order.Price > referencePrice;
                    if (!isLossSide)
                        continue;
                    double distance = Math.Abs(order.Price - referencePrice);
                    if (distance > 0 && (nearestStopDistance == 0 || distance < nearestStopDistance))
                        nearestStopDistance = distance;
                }

            Gui.Tools.SimpleFont boldFont = (Gui.Tools.SimpleFont)chartControl.Properties.LabelFont.Clone();
            boldFont.Bold = true;
            SharpDX.DirectWrite.TextFormat textFormat = boldFont.ToDirectWriteTextFormat();
            List<PendingLabel> labels = new List<PendingLabel>();
            try
            {
                // The execution (average price) line: live unrealized P&L in ticks and
                // money, so the entry line answers "where am I" without the trader
                // toggling the platform's own P&L display.
                if (hasPosition && ShowPositionLabel)
                {
                    bool inProfit = pnlMoney > 0;

                    float avgY = chartScale.GetYByValue(positionAvgPrice);
                    if (avgY >= ChartPanel.Y && avgY <= ChartPanel.Y + ChartPanel.H)
                    {
                        // Signed custom formats: fractional values keep a decimal
                        // instead of collapsing to a signed zero that contradicts
                        // the money value and the color.
                        string positionLabel = string.Format("{0} {1}",
                            positionIsLong ? "LONG" : "SHORT", positionQuantity);
                        if (pnlKnown)
                        {
                            if (ShowTicks)
                                positionLabel += string.Format("  {0}t", pnlTicks.ToString("+0.#;-0.#;0"));
                            if (ShowPoints)
                                positionLabel += string.Format("  {0}pt", pnlPoints.ToString("+0.00##;-0.00##;0"));
                            if (ShowCurrency)
                                positionLabel += string.Format("  {0}{1}",
                                    pnlMoney > 0 ? "+" : (pnlMoney < 0 ? "-" : ""),
                                    Core.Globals.FormatCurrency(Math.Abs(pnlMoney)));
                        }

                        SharpDX.Direct2D1.Brush positionBrush = !pnlKnown || pnlMoney.ApproxCompare(0) == 0
                            ? neutralBrushDx : (inProfit ? profitBrushDx : lossBrushDx);
                        // Below the half box an order centered on the same line would cover:
                        // a stop moved to breakeven rests exactly on the average price.
                        SharpDX.DirectWrite.TextLayout positionLayout = new SharpDX.DirectWrite.TextLayout(
                            Core.Globals.DirectWriteFactory, positionLabel, textFormat, 600, textFormat.FontSize);
                        labels.Add(new PendingLabel
                        {
                            Layout = positionLayout,
                            Brush = positionBrush,
                            Top = avgY + BoxHeight(positionLayout) / 2 + 3,
                        });
                    }
                }

                // Different roles at one price get one label each, laid side by side.
                Dictionary<float, float> usedWidthAtY = new Dictionary<float, float>();
                foreach (OrderInfo order in workingOrders)
                {
                    float y = chartScale.GetYByValue(order.Price);
                    if (y < ChartPanel.Y || y > ChartPanel.Y + ChartPanel.H)
                        continue;

                    double signedPoints = order.Price - referencePrice;
                    double ticks = Math.Abs(signedPoints) / master.TickSize;
                    bool isProfitSide = hasPosition && (positionIsLong ? signedPoints > 0 : signedPoints < 0);

                    // Colored by role, not by side: a stop trailed into profit stays red.
                    bool isExit = hasPosition && (positionIsLong ? !order.IsBuy : order.IsBuy);
                    SharpDX.Direct2D1.Brush brush = neutralBrushDx;
                    if (isExit)
                        brush = order.IsStopType ? lossBrushDx : profitBrushDx;

                    string label = OrderLabel(order.IsStopType, order.RemainingQuantity,
                        Math.Abs(signedPoints) * order.RemainingQuantity, isExit, isExit && !isProfitSide, pointMoney);
                    if (ShowTicks)
                        label += FieldSeparator + string.Format("{0:F0}t", ticks * order.RemainingQuantity);
                    if (ShowRMultiple && isExit && isProfitSide && nearestStopDistance > 0)
                        label += FieldSeparator + string.Format("{0:0.0}R", Math.Abs(signedPoints) / nearestStopDistance);

                    SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(
                        Core.Globals.DirectWriteFactory, label, textFormat, 600, textFormat.FontSize);
                    float usedWidth;
                    usedWidthAtY.TryGetValue(y, out usedWidth);
                    usedWidthAtY[y] = usedWidth + layout.Metrics.Width + 2 * BoxPaddingX + 4;
                    labels.Add(new PendingLabel
                    {
                        Layout = layout,
                        Brush = brush,
                        Top = y - BoxHeight(layout) / 2,
                        ShiftLeft = AvoidOverlap ? 0 : usedWidth,
                    });
                }

                DrawLabels(labels);
            }
            finally
            {
                foreach (PendingLabel label in labels)
                    label.Layout.Dispose();
                textFormat.Dispose();
            }
        }

        // With Keep Labels Apart, orders a few ticks apart get their boxes pushed
        // off each other: top to bottom each box goes just below the previous, a
        // bottom-up pass pulls back any pushed off the panel's bottom, and a final
        // top-down pass keeps them below its top; price order never changes.
        private void DrawLabels(List<PendingLabel> labels)
        {
            const float gap = 1f;
            labels.Sort((a, b) => a.Top.CompareTo(b.Top));

            float[] tops = new float[labels.Count];
            for (int i = 0; i < labels.Count; i++)
                tops[i] = labels[i].Top;

            if (AvoidOverlap)
            {
                float previousBottom = float.MinValue;
                for (int i = 0; i < labels.Count; i++)
                {
                    tops[i] = Math.Max(tops[i], previousBottom + gap);
                    previousBottom = tops[i] + BoxHeight(labels[i].Layout);
                }

                float nextTop = ChartPanel.Y + ChartPanel.H + gap;
                for (int i = labels.Count - 1; i >= 0; i--)
                {
                    tops[i] = Math.Min(tops[i], nextTop - gap - BoxHeight(labels[i].Layout));
                    nextTop = tops[i];
                }

                previousBottom = ChartPanel.Y - gap;
                for (int i = 0; i < labels.Count; i++)
                {
                    tops[i] = Math.Max(tops[i], previousBottom + gap);
                    previousBottom = tops[i] + BoxHeight(labels[i].Layout);
                }
            }

            float panelRight = ChartPanel.X + ChartPanel.W;
            for (int i = 0; i < labels.Count; i++)
            {
                float width = labels[i].Layout.Metrics.Width + 2 * BoxPaddingX;
                float height = BoxHeight(labels[i].Layout);
                float x = Math.Max(ChartPanel.X, panelRight - RightMarginPixels - width - labels[i].ShiftLeft);
                SharpDX.Direct2D1.RoundedRectangle box = new SharpDX.Direct2D1.RoundedRectangle
                {
                    Rect = new SharpDX.RectangleF(x, tops[i], width, height),
                    RadiusX = CornerRadius,
                    RadiusY = CornerRadius
                };
                if (ShowShadow)
                {
                    SharpDX.Direct2D1.RoundedRectangle shadow = new SharpDX.Direct2D1.RoundedRectangle
                    {
                        Rect = new SharpDX.RectangleF(x + ShadowOffset, tops[i] + ShadowOffset, width, height),
                        RadiusX = CornerRadius,
                        RadiusY = CornerRadius
                    };
                    RenderTarget.FillRoundedRectangle(ref shadow, shadowBrushDx);
                }
                RenderTarget.FillRoundedRectangle(ref box, labels[i].Brush);
                RenderTarget.DrawRoundedRectangle(ref box, borderBrushDx, 1f, null);
                RenderTarget.DrawTextLayout(new SharpDX.Vector2(x + BoxPaddingX, tops[i] + BoxPaddingY), labels[i].Layout, textBrushDx);
            }
        }

        /// <summary>
        /// "STOP (4)  :  -230 P  :  $ -4.600,00" / "ALVO (2)  :  31 P  :  $ 620,00".
        /// Points are the distance times the quantity; stops carry a sign (+ on the
        /// profit side), targets do not; money shows only a minus.
        /// </summary>
        private string OrderLabel(bool isStop, int quantity, double points, bool isExit, bool lossSide, double pointMoney)
        {
            string role = "ENTRADA";
            if (isExit)
                role = isStop ? "STOP" : "ALVO";
            string label = string.Format("{0} ({1})", role, quantity);
            if (ShowPoints)
            {
                string sign = "";
                if (isStop && isExit && points != 0)
                    sign = lossSide ? "-" : "+";
                label += FieldSeparator + sign + points.ToString("#,0.##", LabelCulture) + " P";
            }
            if (ShowCurrency)
            {
                double money = points * pointMoney;
                label += FieldSeparator + "$ " + (lossSide && money != 0 ? "-" : "") + money.ToString("N2", LabelCulture);
            }
            return label;
        }

        private static float BoxHeight(SharpDX.DirectWrite.TextLayout layout)
        {
            return layout.Metrics.Height + 2 * BoxPaddingY;
        }
    }
}
