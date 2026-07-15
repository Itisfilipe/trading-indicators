#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    /// <summary>
    /// ATR Renko Size Calculator - Calculates ATR with EMA smoothing, gap handling, and displays values in points and ticks for Renko sizing.
    /// </summary>
    public class ATRRenkoSizeCalculator : Indicator
    {
        private EMA emaATR;
        private Series<double> trSeries;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description               = "ATR Renko Size Calculator - Calculates ATR with EMA smoothing, gap handling, and displays values in points and ticks for Renko sizing.";
                Name                      = "ATR Renko Size Calculator";
                IsSuspendedWhileInactive  = true;
                IsOverlay                 = false;

                Period                    = 14;
                IgnoreGaps                = true;
                ShowHalfATR               = true;
                ShowTable                 = true;
                DecimalPlaces             = 1;

                AddPlot(System.Windows.Media.Brushes.Blue, "ATR");
                AddPlot(System.Windows.Media.Brushes.Green, "HalfATR");
            }
            else if (State == State.DataLoaded)
            {
                trSeries = new Series<double>(this);
                emaATR = EMA(trSeries, Period);
            }
        }

        protected override void OnBarUpdate()
        {
            double high0 = High[0];
            double low0  = Low[0];

            // Calculate True Range with optional gap handling
            double trueRange;

            if (CurrentBar == 0)
            {
                // First bar ever: only High - Low
                trueRange = high0 - low0;
            }
            else if (IgnoreGaps && Bars.IsFirstBarOfSession)
            {
                // First bar of new session with gap handling: only High - Low
                trueRange = high0 - low0;
            }
            else
            {
                // Standard True Range: max of (H-L, |H-PC|, |L-PC|)
                trueRange = Math.Max(high0 - low0, Math.Max(Math.Abs(high0 - Close[1]), Math.Abs(low0 - Close[1])));
            }

            // Store True Range in series - EMA will automatically process this
            trSeries[0] = trueRange;

            // Wait for minimum bars before using EMA
            if (CurrentBar < Period)
            {
                Value[0] = 0;
                Values[1][0] = 0;
                return;
            }

            // Read the EMA-smoothed ATR value
            double atr = emaATR[0];

            // Set ATR value
            Value[0] = atr;

            // Calculate and set Half ATR
            double halfATR = atr / 2.0;
            Values[1][0] = ShowHalfATR ? halfATR : double.NaN;

            // Calculate Half ATR in ticks
            int halfATRTicks = (int)Math.Round(halfATR / TickSize);

            // Display values as text if enabled
            if (ShowTable)
            {
                // Format the values with bold formatting and padding
                string atrText = "  ATR: " + FormatValue(atr) + "  ";
                string halfATRText = "  Half ATR: " + FormatValue(halfATR) + "  ";
                string ticksText = "  Renko Size: " + halfATRTicks.ToString() + "  ";

                // Combine all values into one display with spacing
                string displayText = "\n" + atrText + "\n" + halfATRText + "\n" + ticksText + "\n";

                // Draw text in upper right corner of indicator panel with bold font
                Draw.TextFixed(this, "ATRValues", displayText, TextPosition.TopRight,
                    Brushes.White, new SimpleFont("Arial", 14) { Bold = true }, Brushes.Transparent,
                    Brushes.DarkSlateGray, 100);
            }
        }

        private string FormatValue(double value)
        {
            if (DecimalPlaces == 0)
                return Math.Round(value).ToString();
            else
                return value.ToString("F" + DecimalPlaces);
        }

        #region Properties
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "ATR Length", GroupName = "Parameters", Order = 1)]
        public int Period { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ignore Gaps", GroupName = "Parameters", Order = 2)]
        public bool IgnoreGaps { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Half ATR", GroupName = "Parameters", Order = 3)]
        public bool ShowHalfATR { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Values Table", GroupName = "Parameters", Order = 4)]
        public bool ShowTable { get; set; }

        [Range(0, 8), NinjaScriptProperty]
        [Display(Name = "Decimal Places", GroupName = "Parameters", Order = 5)]
        public int DecimalPlaces { get; set; }

        [Browsable(false)]
        [XmlIgnore]
        public Series<double> HalfATR
        {
            get { return Values[1]; }
        }
        #endregion
    }
}

