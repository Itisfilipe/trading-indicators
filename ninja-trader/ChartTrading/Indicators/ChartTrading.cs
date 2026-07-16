#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
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
// the pointer -- the entry line at the pointer price, the stop below (buy) or above
// (sell) it, and one line per profit target -- all at the tick offsets configured
// below. This is "Option A": the indicator owns the bracket geometry, so what you see
// is exactly what a later milestone will submit, to the tick.
//
// Nothing here touches an account, ChartTrader, or the network. Order submission,
// order-type inference, OCO, and grid entry are later milestones layered on top.

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

        private SharpDX.Direct2D1.Brush entryBrushDx;
        private SharpDX.Direct2D1.Brush stopBrushDx;
        private SharpDX.Direct2D1.Brush targetBrushDx;
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

        [Range(1, 10)]
        [Display(Name = "Line width", Order = 5, GroupName = "Bracket")]
        public int LineWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "Entry line", Order = 1, GroupName = "Colors")]
        public System.Windows.Media.Brush EntryLineBrush { get; set; }

        [Browsable(false)]
        public string EntryLineBrushSerialize
        {
            get { return Serialize.BrushToString(EntryLineBrush); }
            set { EntryLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Stop line", Order = 2, GroupName = "Colors")]
        public System.Windows.Media.Brush StopLineBrush { get; set; }

        [Browsable(false)]
        public string StopLineBrushSerialize
        {
            get { return Serialize.BrushToString(StopLineBrush); }
            set { StopLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Target line", Order = 3, GroupName = "Colors")]
        public System.Windows.Media.Brush TargetLineBrush { get; set; }

        [Browsable(false)]
        public string TargetLineBrushSerialize
        {
            get { return Serialize.BrushToString(TargetLineBrush); }
            set { TargetLineBrush = Serialize.StringToBrush(value); }
        }
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
                LineWidth = 2;

                EntryLineBrush = Brushes.DodgerBlue;
                StopLineBrush = Brushes.Crimson;
                TargetLineBrush = Brushes.LimeGreen;
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
            DisposeBrushes();

            if (RenderTarget == null)
                return;

            entryBrushDx = EntryLineBrush.ToDxBrush(RenderTarget);
            stopBrushDx = StopLineBrush.ToDxBrush(RenderTarget);
            targetBrushDx = TargetLineBrush.ToDxBrush(RenderTarget);
        }

        private void DisposeBrushes()
        {
            entryBrushDx?.Dispose();
            stopBrushDx?.Dispose();
            targetBrushDx?.Dispose();
            entryBrushDx = null;
            stopBrushDx = null;
            targetBrushDx = null;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (previewSide == Side.None || !pointerOverPanel || RenderTarget == null
                || entryBrushDx == null || stopBrushDx == null || targetBrushDx == null)
                return;

            MasterInstrument master = Instrument?.MasterInstrument;
            if (master == null)
                return;

            double entryPrice = master.RoundToTickSize(chartScale.GetValueByY(pointerDeviceY));
            double tick = master.TickSize;

            // Buy: stop below entry, targets above. Sell: mirror. sign is +1 in the
            // direction the trade profits.
            int profitSign = previewSide == Side.Buy ? 1 : -1;

            DrawPriceLine(chartScale, master.RoundToTickSize(entryPrice), entryBrushDx);
            DrawPriceLine(chartScale, master.RoundToTickSize(entryPrice - profitSign * StopLossTicks * tick), stopBrushDx);

            foreach (int targetTicks in new[] { Target1Ticks, Target2Ticks, Target3Ticks })
            {
                if (targetTicks <= 0)
                    continue;
                DrawPriceLine(chartScale, master.RoundToTickSize(entryPrice + profitSign * targetTicks * tick), targetBrushDx);
            }
        }

        private void DrawPriceLine(ChartScale chartScale, double price, SharpDX.Direct2D1.Brush brush)
        {
            float y = chartScale.GetYByValue(price);
            if (y < ChartPanel.Y || y > ChartPanel.Y + ChartPanel.H)
                return;

            float left = ChartPanel.X;
            float right = ChartPanel.X + ChartPanel.W;
            RenderTarget.DrawLine(new Vector2(left, y), new Vector2(right, y), brush, LineWidth);
        }
        #endregion

        protected override void OnBarUpdate()
        {
            // Chart interaction only; nothing to calculate per bar.
        }
    }
}
