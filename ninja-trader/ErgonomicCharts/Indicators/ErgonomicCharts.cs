#region Using declarations
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using System.ComponentModel.DataAnnotations;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Natural chart interaction: scroll wheel zooms without holding Ctrl, and
    /// optionally left-drag pans without holding Ctrl.
    /// </summary>
    /// <remarks>
    /// Zoom drives NinjaTrader's own bar-spacing hotkey handlers.
    ///
    /// Panning has no such route. NinjaTrader exposes no API to set a chart's scroll
    /// position -- its own indicators only read the visible range -- so pan works by
    /// holding a synthetic Ctrl down for the duration of the drag and letting the
    /// platform's native Ctrl+drag do the work. That key is pressed into the Windows
    /// session, not into this chart, so a drag that never sees its mouse-up leaves Ctrl
    /// down for every application. Every reachable release path is covered below, but a
    /// crash mid-drag cannot be, which is why the feature is off unless asked for.
    /// </remarks>
    public class ErgonomicCharts : Indicator
    {
        #region Win32 interop
        private const int VK_CONTROL = 0x11;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const ushort KEY_DOWN_FLAGS = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        /// <summary>
        /// Must carry every member of the native union, not just the keyboard one:
        /// SendInput rejects the call outright if the size it is handed does not match
        /// the real INPUT, and the mouse member is what sets that size.
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        #endregion

        #region Synthetic Ctrl lease
        // The Ctrl key belongs to the whole session, so ownership of it cannot live on a
        // single indicator instance. Charts and panels each get their own instance, and
        // without a shared lease one instance's release would cancel another's drag.
        private static readonly object leaseGate = new object();
        private static readonly HashSet<object> leaseOwners = new HashSet<object>();

        private static bool IsPhysicalCtrlDown()
        {
            return (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        }

        private static bool SendCtrl(bool up)
        {
            INPUT[] input = new INPUT[1];
            input[0].type = INPUT_KEYBOARD;
            input[0].u.ki.wVk = VK_CONTROL;
            input[0].u.ki.dwFlags = up ? KEYEVENTF_KEYUP : KEY_DOWN_FLAGS;
            return SendInput(1, input, Marshal.SizeOf(typeof(INPUT))) == 1;
        }

        /// <summary>
        /// Presses Ctrl for the first owner only, and declines while the user is
        /// physically holding it so that releasing later cannot clear their own keypress.
        /// </summary>
        private static bool AcquireCtrl(object owner)
        {
            lock (leaseGate)
            {
                if (leaseOwners.Contains(owner))
                    return true;

                if (leaseOwners.Count == 0)
                {
                    if (IsPhysicalCtrlDown() || !SendCtrl(up: false))
                        return false;
                }

                leaseOwners.Add(owner);
                return true;
            }
        }

        private static void ReleaseCtrl(object owner)
        {
            lock (leaseGate)
            {
                if (leaseOwners.Remove(owner) && leaseOwners.Count == 0)
                    SendCtrl(up: true);
            }
        }
        #endregion

        #region Fields
        private ChartControl chartControl;
        private Window chartWindow;
        private bool eventHandlersAttached;
        #endregion

        #region Properties
        /// <summary>
        /// Whether left-drag pans the chart without holding Ctrl.
        /// </summary>
        /// <remarks>
        /// Off by default: see the class remarks for why this cannot be made safe.
        /// </remarks>
        [Display(Name = "Enable drag-to-pan (experimental)", Order = 1, GroupName = "Parameters",
                 Description = "Left-drag pans without holding Ctrl. Works by holding a synthetic Ctrl " +
                               "key down for the whole session while dragging, so if NinjaTrader crashes " +
                               "mid-drag, Ctrl can stay stuck down in Windows until you tap it. Zoom does " +
                               "not use this and is unaffected.")]
        public bool EnableDragToPan { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Scroll to zoom without holding Ctrl. Optionally drag to pan.";
                Name = "Ergonomic Charts";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = false;
                DrawVerticalGridLines = false;
                PaintPriceMarkers = false;
                ScaleJustification = ScaleJustification.Right;
                EnableDragToPan = false;
            }
            else if (State == State.Historical)
            {
                // Historical is the first state where the chart is reliably present, and
                // its UI must be touched on its own thread rather than this one.
                ChartControl owner = ChartControl;
                if (owner != null && !eventHandlersAttached)
                {
                    owner.Dispatcher.InvokeAsync(() =>
                    {
                        if (State < State.Terminated)
                            AttachEventHandlers(owner);
                    });
                }
            }
            else if (State == State.Terminated)
            {
                // Drop the key first and without waiting on the dispatcher: releasing it
                // matters more than tidy unsubscription, and the queue may never drain.
                ReleaseCtrl(this);

                ChartControl owner = chartControl;
                if (owner != null)
                    owner.Dispatcher.InvokeAsync(() => DetachEventHandlers(owner));

                chartControl = null;
                chartWindow = null;
            }
        }

        #region Event wiring
        private void AttachEventHandlers(ChartControl owner)
        {
            if (owner == null || eventHandlersAttached)
                return;

            try
            {
                owner.PreviewMouseLeftButtonDown += OnMouseDown;
                owner.PreviewMouseLeftButtonUp += OnMouseUp;
                owner.MouseLeave += OnMouseLeave;
                owner.LostMouseCapture += OnLostMouseCapture;
                owner.PreviewMouseWheel += OnMouseWheel;

                // A drag interrupted by alt-tab never delivers its mouse-up, so the
                // window losing focus has to count as the end of the gesture.
                chartWindow = Window.GetWindow(owner);
                if (chartWindow != null)
                    chartWindow.Deactivated += OnWindowDeactivated;

                chartControl = owner;
                eventHandlersAttached = true;
            }
            catch (Exception ex)
            {
                // Roll back rather than leave a half-wired chart behind.
                DetachEventHandlers(owner);
                Log("ErgonomicCharts: failed to attach handlers - " + ex, LogLevel.Error);
            }
        }

        private void DetachEventHandlers(ChartControl owner)
        {
            if (owner == null)
                return;

            try
            {
                owner.PreviewMouseLeftButtonDown -= OnMouseDown;
                owner.PreviewMouseLeftButtonUp -= OnMouseUp;
                owner.MouseLeave -= OnMouseLeave;
                owner.LostMouseCapture -= OnLostMouseCapture;
                owner.PreviewMouseWheel -= OnMouseWheel;

                if (chartWindow != null)
                    chartWindow.Deactivated -= OnWindowDeactivated;
            }
            catch (Exception ex)
            {
                Log("ErgonomicCharts: failed to detach handlers - " + ex, LogLevel.Error);
            }

            eventHandlersAttached = false;
        }
        #endregion

        #region Pan
        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!EnableDragToPan)
                return;

            // A modifier already held means the user is asking for something else.
            if (Keyboard.Modifiers != ModifierKeys.None)
                return;

            if (!IsInsideOwningPanel(e))
                return;

            AcquireCtrl(this);
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            ReleaseCtrl(this);
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            ReleaseCtrl(this);
        }

        private void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            ReleaseCtrl(this);
        }

        private void OnWindowDeactivated(object sender, EventArgs e)
        {
            ReleaseCtrl(this);
        }
        #endregion

        #region Zoom
        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Ctrl-wheel is NinjaTrader's own zoom, and Shift/Alt belong to whatever
            // else is listening.
            if (Keyboard.Modifiers != ModifierKeys.None)
                return;

            if (chartControl == null || e.Delta == 0 || !IsInsideOwningPanel(e))
                return;

            try
            {
                // NinjaTrader's own bar-spacing hotkey handlers. Driving them keeps the
                // user's spacing ratio and the platform's own limits, which hand-rolled
                // BarWidth arithmetic does not: the old factors here were 1.1 and 0.9,
                // so a wheel up and back down left the chart at 0.99 of where it began
                // and bars shrank as the user scrolled around.
                if (e.Delta > 0)
                    chartControl.OnBarSpacingPlusHotKey(null, null);
                else
                    chartControl.OnBarSpacingMinusHotKey(null, null);

                // Only claim the event once it has actually been acted on, so a failure
                // still leaves the chart's own wheel handling intact.
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Log("ErgonomicCharts: zoom error - " + ex, LogLevel.Error);
            }
        }
        #endregion

        /// <summary>
        /// Whether the pointer is over the chart itself.
        /// </summary>
        /// <remarks>
        /// Everything here stays in the chart control's own WPF units. ChartPanel's
        /// geometry and ChartControl's canvas bounds are both in device pixels, and
        /// mouse positions are not, so comparing across the two silently misjudges the
        /// edges at any display scaling other than 100%.
        ///
        /// The trade-off is that this does not carve the axes out of the gesture the way
        /// the previous fixed 50/30 unit margins tried to: those numbers were guesses
        /// that already broke on a left-hand scale or a multi-panel layout.
        /// </remarks>
        private bool IsInsideOwningPanel(MouseEventArgs e)
        {
            if (chartControl == null)
                return false;

            Point pos = e.GetPosition(chartControl);
            return pos.X >= 0 && pos.Y >= 0
                && pos.X <= chartControl.ActualWidth && pos.Y <= chartControl.ActualHeight;
        }

        protected override void OnBarUpdate()
        {
            // Chart interaction only; nothing to calculate per bar.
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
