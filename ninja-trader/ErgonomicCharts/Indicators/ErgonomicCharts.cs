#region Using declarations
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ErgonomicCharts : Indicator
    {
        #region Win32 API
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_CONTROL = 0x11;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        #endregion

        private ChartControl chartControl;
        private bool ctrlSimulated = false;
        private bool eventHandlersAttached = false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Enables natural chart interaction - drag to pan, scroll to zoom";
                Name = "Ergonomic Charts";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Right;
            }
            else if (State == State.DataLoaded)
            {
                if (ChartControl != null && !eventHandlersAttached)
                {
                    chartControl = ChartControl;
                    AttachEventHandlers();
                }
            }
            else if (State == State.Historical)
            {
                if (chartControl == null && ChartControl != null && !eventHandlersAttached)
                {
                    chartControl = ChartControl;
                    AttachEventHandlers();
                }
            }
            else if (State == State.Terminated)
            {
                if (ctrlSimulated)
                {
                    keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    ctrlSimulated = false;
                }
                DetachEventHandlers();
                chartControl = null;
                eventHandlersAttached = false;
            }
        }

        private void AttachEventHandlers()
        {
            if (chartControl == null || eventHandlersAttached) return;

            try
            {
                chartControl.PreviewMouseLeftButtonDown += OnMouseDown;
                chartControl.PreviewMouseLeftButtonUp += OnMouseUp;
                chartControl.MouseLeave += OnMouseLeave;
                chartControl.PreviewMouseWheel += OnMouseWheel;
                eventHandlersAttached = true;
            }
            catch (Exception ex)
            {
                Log("ErgonomicCharts: Failed to attach handlers - " + ex.Message, LogLevel.Error);
            }
        }

        private void DetachEventHandlers()
        {
            if (chartControl == null) return;

            try
            {
                chartControl.PreviewMouseLeftButtonDown -= OnMouseDown;
                chartControl.PreviewMouseLeftButtonUp -= OnMouseUp;
                chartControl.MouseLeave -= OnMouseLeave;
                chartControl.PreviewMouseWheel -= OnMouseWheel;
            }
            catch { }
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Don't interfere if modifier keys are already pressed
            if (Keyboard.Modifiers != ModifierKeys.None)
                return;

            // Don't interfere with double-clicks
            if (e.ClickCount == 2)
                return;

            // Get click position
            Point pos = e.GetPosition(chartControl);

            // Simple check - avoid edges where UI elements typically are
            if (pos.X < 50 || pos.X > chartControl.ActualWidth - 30 ||
                pos.Y < 30 || pos.Y > chartControl.ActualHeight - 30)
                return;

            // Simulate Ctrl for panning
            if (!ctrlSimulated)
            {
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                ctrlSimulated = true;
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            ReleaseCtrl();
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            ReleaseCtrl();
        }

        private void ReleaseCtrl()
        {
            if (ctrlSimulated)
            {
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                ctrlSimulated = false;
            }
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Let normal zoom work if Ctrl is pressed
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                return;

            if (chartControl == null || ChartBars == null)
                return;

            try
            {
                // Natural zoom factor - smaller steps feel more natural
                double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;

                double currentBarWidth = chartControl.BarWidth;
                double newBarWidth = currentBarWidth * zoomFactor;

                // Natural limits that work well
                newBarWidth = Math.Max(0.5, Math.Min(80, newBarWidth));

                // Only update if there's a meaningful change
                if (Math.Abs(newBarWidth - currentBarWidth) > 0.01)
                {
                    chartControl.BarWidth = newBarWidth;

                    // Maintain a good visual ratio (Dean's fix)
                    if (chartControl.Properties != null)
                    {
                        chartControl.Properties.BarDistance = (float)(newBarWidth * 4);
                    }

                    // Refresh the chart
                    chartControl.InvalidateVisual();
                }

                // Always mark as handled to prevent default zoom
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Log("ErgonomicCharts: Zoom error - " + ex.Message, LogLevel.Error);
            }
        }

        protected override void OnBarUpdate()
        {
            // No bar processing needed
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ErgonomicCharts[] cacheErgonomicCharts;
		public ErgonomicCharts ErgonomicCharts()
		{
			return ErgonomicCharts(Input);
		}

		public ErgonomicCharts ErgonomicCharts(ISeries<double> input)
		{
			if (cacheErgonomicCharts != null)
				for (int idx = 0; idx < cacheErgonomicCharts.Length; idx++)
					if (cacheErgonomicCharts[idx] != null &&  cacheErgonomicCharts[idx].EqualsInput(input))
						return cacheErgonomicCharts[idx];
			return CacheIndicator<ErgonomicCharts>(new ErgonomicCharts(), input, ref cacheErgonomicCharts);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ErgonomicCharts ErgonomicCharts()
		{
			return indicator.ErgonomicCharts(Input);
		}

		public Indicators.ErgonomicCharts ErgonomicCharts(ISeries<double> input )
		{
			return indicator.ErgonomicCharts(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ErgonomicCharts ErgonomicCharts()
		{
			return indicator.ErgonomicCharts(Input);
		}

		public Indicators.ErgonomicCharts ErgonomicCharts(ISeries<double> input )
		{
			return indicator.ErgonomicCharts(input);
		}
	}
}

#endregion