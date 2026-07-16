#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;
#endregion

// ChartTrading -- click-to-trade indicator for NinjaTrader 8.
//
// This file is milestone M1: the LIVE BRACKET PREVIEW only. It places NO orders.
//
// Hold the buy modifier (default Shift) or the sell modifier (default Alt) and move
// the mouse over the chart: the indicator draws the bracket a click would place at
// the pointer, styled like NinjaTrader's own resting-order markers -- a dashed line
// per level with a tag reading like the order itself ("1 Buy LMT 14924.25", with the
// stop and targets labelled as the closing orders they would become). Entry LMT vs
// STP is inferred from the pointer being below or above the last traded price. This
// is "Option A": the indicator owns the bracket geometry, so what you see is exactly
// what a later milestone will submit, to the tick.
//
// Nothing here touches an account or the network; the only ChartTrader read is the
// order quantity, for the tag text. Order submission, OCO, and grid entry are later
// milestones layered on top.

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Which keyboard modifier arms a side of the click-to-trade preview.
    /// </summary>
    public enum ChartTradingModifier
    {
        Shift,
        Alt,
        Control,
    }

    public class ChartTrading : Indicator
    {
        #region Preview state
        private enum Side { None, Buy, Sell }

        // Written by the WPF input handlers, read by OnRender. Both run on the chart UI
        // thread, so plain fields are enough; no order or account state lives here.
        private Side previewSide = Side.None;
        private int pointerDeviceX;
        private int pointerDeviceY;
        private bool pointerOverPanel;

        private ChartPanel hookedPanel;
        private bool handlersAttached;

        // Quantity shown in the tags, read from ChartTrader on mouse events (UI thread).
        private int previewQuantity = 1;

        private SharpDX.Direct2D1.Brush tagTextBrushDx;
        #endregion

        #region Parameters
        [Display(Name = "Buy modifier", Order = 1, GroupName = "Gesture",
                 Description = "Hold this key and move over the chart to preview a buy bracket.")]
        public ChartTradingModifier BuyModifier { get; set; }

        [Display(Name = "Sell modifier", Order = 2, GroupName = "Gesture",
                 Description = "Hold this key and move over the chart to preview a sell bracket.")]
        public ChartTradingModifier SellModifier { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Stop loss (ticks)", Order = 1, GroupName = "Bracket",
                 Description = "Distance from entry to the stop, in ticks.")]
        public int StopLossTicks { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Target 1 (ticks)", Order = 2, GroupName = "Bracket",
                 Description = "Distance from entry to the first profit target, in ticks.")]
        public int Target1Ticks { get; set; }

        [Range(0, int.MaxValue)]
        [Display(Name = "Target 2 (ticks)", Order = 3, GroupName = "Bracket",
                 Description = "Distance from entry to the second target, in ticks. 0 hides it.")]
        public int Target2Ticks { get; set; }

        [Range(0, int.MaxValue)]
        [Display(Name = "Target 3 (ticks)", Order = 4, GroupName = "Bracket",
                 Description = "Distance from entry to the third target, in ticks. 0 hides it.")]
        public int Target3Ticks { get; set; }

        // Strokes rather than plain brushes so each level carries its own color, width,
        // and dash style, and binds to the render target the way the platform's own
        // price-line indicator does. NinjaTrader persists Stroke properties natively.
        [Display(Name = "Entry line", Order = 1, GroupName = "Colors")]
        public Stroke EntryStroke { get; set; }

        [Display(Name = "Stop line", Order = 2, GroupName = "Colors")]
        public Stroke StopStroke { get; set; }

        [Display(Name = "Target line", Order = 3, GroupName = "Colors")]
        public Stroke TargetStroke { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Preview a click-to-trade bracket by holding a modifier key. Places no orders.";
                Name = "ChartTrading";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;

                BuyModifier = ChartTradingModifier.Shift;
                SellModifier = ChartTradingModifier.Alt;
                StopLossTicks = 20;
                Target1Ticks = 20;
                Target2Ticks = 40;
                Target3Ticks = 0;

                // Dashed, like the platform draws a working order that is not yet filled.
                EntryStroke = new Stroke(Brushes.DodgerBlue, DashStyleHelper.Dash, 2f);
                StopStroke = new Stroke(Brushes.Crimson, DashStyleHelper.Dash, 2f);
                TargetStroke = new Stroke(Brushes.LimeGreen, DashStyleHelper.Dash, 2f);
            }
            else if (State == State.Historical)
            {
                // First state where the chart is reliably present, and its input events
                // must be wired on its own thread. Mirrors the hardened ErgonomicCharts
                // lifecycle: capture the owner, attach through the dispatcher, and guard
                // against attaching after teardown has begun.
                ChartControl owner = ChartControl;
                if (owner != null && !handlersAttached)
                {
                    owner.Dispatcher.InvokeAsync(() =>
                    {
                        if (State < State.Terminated)
                            AttachHandlers();
                    });
                }
            }
            else if (State == State.Terminated)
            {
                ChartControl owner = ChartControl;
                if (owner != null)
                    owner.Dispatcher.InvokeAsync(DetachHandlers);
                else
                    DetachHandlers();
            }
        }

        #region Input wiring
        private void AttachHandlers()
        {
            if (handlersAttached || ChartPanel == null)
                return;

            try
            {
                hookedPanel = ChartPanel;
                hookedPanel.MouseMove += OnMouseMove;
                hookedPanel.MouseLeave += OnMouseLeave;
                hookedPanel.PreviewKeyDown += OnKeyChanged;
                hookedPanel.PreviewKeyUp += OnKeyChanged;
                handlersAttached = true;
            }
            catch (Exception ex)
            {
                DetachHandlers();
                Log("ChartTrading: failed to attach handlers - " + ex, NinjaTrader.Cbi.LogLevel.Error);
            }
        }

        private void DetachHandlers()
        {
            if (hookedPanel != null)
            {
                try
                {
                    hookedPanel.MouseMove -= OnMouseMove;
                    hookedPanel.MouseLeave -= OnMouseLeave;
                    hookedPanel.PreviewKeyDown -= OnKeyChanged;
                    hookedPanel.PreviewKeyUp -= OnKeyChanged;
                }
                catch (Exception ex)
                {
                    Log("ChartTrading: failed to detach handlers - " + ex, NinjaTrader.Cbi.LogLevel.Error);
                }
                hookedPanel = null;
            }

            previewSide = Side.None;
            pointerOverPanel = false;
            handlersAttached = false;
        }
        #endregion

        #region Gesture
        private ModifierKeys ToModifierKeys(ChartTradingModifier modifier)
        {
            switch (modifier)
            {
                case ChartTradingModifier.Alt: return ModifierKeys.Alt;
                case ChartTradingModifier.Control: return ModifierKeys.Control;
                default: return ModifierKeys.Shift;
            }
        }

        /// <summary>
        /// Resolve which side, if any, the currently held modifiers arm.
        /// </summary>
        /// <remarks>
        /// Exact equality, not a flag test, so an accidental Shift+Ctrl does not read as
        /// a plain Shift buy.
        /// </remarks>
        private Side ResolveSide()
        {
            ModifierKeys held = Keyboard.Modifiers;
            if (held == ToModifierKeys(BuyModifier))
                return Side.Buy;
            if (held == ToModifierKeys(SellModifier))
                return Side.Sell;
            return Side.None;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (ChartControl == null || ChartPanel == null)
                return;

            System.Windows.Point pos = e.GetPosition(ChartControl as IInputElement);
            pointerDeviceX = ChartingExtensions.ConvertToHorizontalPixels(pos.X, ChartControl.PresentationSource);
            pointerDeviceY = ChartingExtensions.ConvertToVerticalPixels(pos.Y, ChartControl.PresentationSource);

            pointerOverPanel =
                pointerDeviceX >= ChartPanel.X && pointerDeviceX <= ChartPanel.X + ChartPanel.W &&
                pointerDeviceY >= ChartPanel.Y && pointerDeviceY <= ChartPanel.Y + ChartPanel.H;

            // The tags show the quantity ChartTrader would trade. Mouse events run on
            // the chart UI thread, so reading the control here is thread-consistent;
            // fall back to the last known value if ChartTrader is unavailable.
            try
            {
                int quantity = ChartControl.OwnerChart.ChartTrader.Quantity;
                if (quantity > 0)
                    previewQuantity = quantity;
            }
            catch (Exception) { }

            UpdatePreview(ResolveSide());
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            pointerOverPanel = false;
            UpdatePreview(Side.None);
        }

        // Catches holding or releasing the modifier without moving the mouse. Best-effort:
        // key events only arrive when the panel has focus, and the mouse handler is the
        // reliable driver.
        private void OnKeyChanged(object sender, KeyEventArgs e)
        {
            UpdatePreview(pointerOverPanel ? ResolveSide() : Side.None);
        }

        private void UpdatePreview(Side side)
        {
            bool sideChanged = side != previewSide;
            previewSide = side;

            // Repaint on every pointer move while a side is armed, so the bracket tracks
            // the mouse in real time -- not only when the side changes. Also repaint on
            // the transition to None so the preview clears. OnRender itself is cheap
            // (a few lines from cached state), so per-move refreshes are fine.
            if (side != Side.None || sideChanged)
                ForceRefresh();
        }
        #endregion

        #region Rendering
        public override void OnRenderTargetChanged()
        {
            // Strokes bind to the render target the way the platform's own price-line
            // indicator binds its ask/bid/last strokes.
            if (EntryStroke != null) EntryStroke.RenderTarget = RenderTarget;
            if (StopStroke != null) StopStroke.RenderTarget = RenderTarget;
            if (TargetStroke != null) TargetStroke.RenderTarget = RenderTarget;

            tagTextBrushDx?.Dispose();
            tagTextBrushDx = null;
            if (RenderTarget != null)
                tagTextBrushDx = Brushes.White.ToDxBrush(RenderTarget);
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (previewSide == Side.None || !pointerOverPanel || RenderTarget == null)
                return;

            MasterInstrument master = Instrument?.MasterInstrument;
            if (master == null || EntryStroke?.BrushDX == null)
                return;

            double entryPrice = master.RoundToTickSize(chartScale.GetValueByY(pointerDeviceY));
            double tick = master.TickSize;
            bool isBuy = previewSide == Side.Buy;

            // Buy: stop below entry, targets above. Sell: mirror. sign is +1 in the
            // direction the trade profits.
            int profitSign = isBuy ? 1 : -1;

            string enter = isBuy ? "Buy" : "Sell";
            string exit = isBuy ? "Sell" : "Buy";

            // The entry becomes a limit when it is on the favorable side of the last
            // traded price and a stop-market beyond it -- the label previews the order
            // type a click would actually submit.
            string entryType = "MKT";
            if (Bars != null && Bars.Count > 0)
            {
                double last = Bars.GetClose(Bars.Count - 1);
                bool favorable = isBuy ? entryPrice < last : entryPrice > last;
                if (entryPrice.ApproxCompare(last) != 0)
                    entryType = favorable ? "LMT" : "STP";
            }

            SharpDX.DirectWrite.TextFormat textFormat = chartControl.Properties.LabelFont.ToDirectWriteTextFormat();
            try
            {
                DrawOrderLine(chartScale, entryPrice, EntryStroke,
                    $"{previewQuantity} {enter} {entryType} {master.FormatPrice(entryPrice)}", textFormat);

                double stopPrice = master.RoundToTickSize(entryPrice - profitSign * StopLossTicks * tick);
                DrawOrderLine(chartScale, stopPrice, StopStroke,
                    $"{previewQuantity} {exit} STP {master.FormatPrice(stopPrice)}", textFormat);

                int targetNumber = 0;
                foreach (int targetTicks in new[] { Target1Ticks, Target2Ticks, Target3Ticks })
                {
                    targetNumber++;
                    if (targetTicks <= 0)
                        continue;
                    double targetPrice = master.RoundToTickSize(entryPrice + profitSign * targetTicks * tick);
                    DrawOrderLine(chartScale, targetPrice, TargetStroke,
                        $"{previewQuantity} {exit} LMT {master.FormatPrice(targetPrice)} (T{targetNumber})", textFormat);
                }
            }
            finally
            {
                textFormat.Dispose();
            }
        }

        /// <summary>
        /// Draws one preview level the way the platform draws a working order: a dashed
        /// line across the panel with a filled tag at the left edge naming the order.
        /// </summary>
        private void DrawOrderLine(ChartScale chartScale, double price, Stroke stroke,
            string label, SharpDX.DirectWrite.TextFormat textFormat)
        {
            if (stroke?.BrushDX == null)
                return;

            float y = chartScale.GetYByValue(price);
            if (y < ChartPanel.Y || y > ChartPanel.Y + ChartPanel.H)
                return;

            RenderTarget.DrawLine(
                new Vector2(ChartPanel.X, y),
                new Vector2(ChartPanel.X + ChartPanel.W, y),
                stroke.BrushDX, stroke.Width, stroke.StrokeStyle);

            if (tagTextBrushDx == null)
                return;

            using (var layout = new SharpDX.DirectWrite.TextLayout(
                Core.Globals.DirectWriteFactory, label, textFormat, ChartPanel.W, textFormat.FontSize))
            {
                float padX = 5f, padY = 2f;
                float boxWidth = layout.Metrics.Width + 2f * padX;
                float boxHeight = layout.Metrics.Height + 2f * padY;
                var box = new RectangleF(ChartPanel.X + 4f, y - boxHeight / 2f, boxWidth, boxHeight);

                RenderTarget.FillRectangle(box, stroke.BrushDX);
                RenderTarget.DrawTextLayout(new Vector2(box.X + padX, box.Y + padY), layout, tagTextBrushDx);
            }
        }
        #endregion

        protected override void OnBarUpdate()
        {
            // Chart interaction only; nothing to calculate per bar.
        }
    }
}
