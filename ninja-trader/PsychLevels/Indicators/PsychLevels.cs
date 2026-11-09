#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

// PsychLevels -- horizontal lines at round price levels, in three tiers.
//
// Every multiple of the step size gets a line. Multiples of "Highlight every"
// use the highlighted style, and multiples of "Major every" the major style,
// so with the defaults 29350 is regular, 29300 highlighted and 29000 major.
// Levels are recomputed on every render from the visible price range, so they
// follow scrolling and zooming without touching chart objects.

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    public enum PsychLabelPosition
    {
        Right,
        Left
    }

    public class PsychLevels : Indicator
    {
        [NinjaScriptProperty]
        [Range(0.00000001, double.MaxValue)]
        [Display(Name = "Step size", GroupName = "1. Levels", Order = 1,
                 Description = "Distance between regular levels, in price units.")]
        public double StepSize { get; set; }

        [NinjaScriptProperty]
        [Range(0.00000001, double.MaxValue)]
        [Display(Name = "Highlight every", GroupName = "1. Levels", Order = 2,
                 Description = "Levels that are multiples of this value use the highlighted style.")]
        public double HighlightEvery { get; set; }

        [NinjaScriptProperty]
        [Range(0.00000001, double.MaxValue)]
        [Display(Name = "Major every", GroupName = "1. Levels", Order = 3,
                 Description = "Levels that are multiples of this value use the major style, over the highlighted one.")]
        public double MajorEvery { get; set; }

        [Range(1, 2000)]
        [Display(Name = "Maximum lines", GroupName = "1. Levels", Order = 4,
                 Description = "Upper bound on lines drawn at once, centered on the visible range.")]
        public int MaxLines { get; set; }

        [Display(Name = "Regular prices", GroupName = "3. Price labels", Order = 1,
                 Description = "Print the price on each regular line, in the line's color.")]
        public bool ShowRegularPrices { get; set; }

        [Display(Name = "Highlighted prices", GroupName = "3. Price labels", Order = 2)]
        public bool ShowHighlightPrices { get; set; }

        [Display(Name = "Major prices", GroupName = "3. Price labels", Order = 3)]
        public bool ShowMajorPrices { get; set; }

        [Display(Name = "Position", GroupName = "3. Price labels", Order = 4,
                 Description = "Which end of the line the price sits on.")]
        public PsychLabelPosition LabelPosition { get; set; }

        [Display(Name = "Regular", GroupName = "2. Style", Order = 1)]
        public Stroke RegularStroke { get; set; }

        [Display(Name = "Highlighted", GroupName = "2. Style", Order = 2)]
        public Stroke HighlightStroke { get; set; }

        [Display(Name = "Major", GroupName = "2. Style", Order = 3)]
        public Stroke MajorStroke { get; set; }

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
                Description = "Horizontal lines at round price levels, with separate styles for regular, highlighted and major levels.";
                Name = "Psych Levels";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                StepSize = 50;
                HighlightEvery = 100;
                MajorEvery = 1000;
                MaxLines = 250;
                ShowRegularPrices = false;
                ShowHighlightPrices = false;
                ShowMajorPrices = false;
                LabelPosition = PsychLabelPosition.Right;

                RegularStroke = new Stroke(Rgb(0x8F, 0x95, 0x9E), DashStyleHelper.Dash, 1f);
                HighlightStroke = new Stroke(Rgb(0x5F, 0x67, 0x73), DashStyleHelper.Solid, 1f);
                MajorStroke = new Stroke(Rgb(0x37, 0x41, 0x51), DashStyleHelper.Solid, 2f);
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (ChartPanel == null || RenderTarget == null)
                return;

            long firstIndex = (long)Math.Ceiling(chartScale.MinValue / StepSize);
            long lastIndex = (long)Math.Floor(chartScale.MaxValue / StepSize);
            long totalLevels = lastIndex - firstIndex + 1;
            if (totalLevels <= 0)
                return;

            long linesToDraw = Math.Min(totalLevels, MaxLines);
            long startIndex = firstIndex + (totalLevels - linesToDraw) / 2;

            RegularStroke.RenderTarget = RenderTarget;
            HighlightStroke.RenderTarget = RenderTarget;
            MajorStroke.RenderTarget = RenderTarget;

            float left = ChartPanel.X;
            float right = ChartPanel.X + ChartPanel.W;

            SharpDX.DirectWrite.TextFormat labelFormat = ShowRegularPrices || ShowHighlightPrices || ShowMajorPrices
                ? chartControl.Properties.LabelFont.ToDirectWriteTextFormat()
                : null;
            try
            {
                for (long index = startIndex; index < startIndex + linesToDraw; index++)
                {
                    double price = index * StepSize;
                    Stroke stroke = RegularStroke;
                    bool showPrice = ShowRegularPrices;
                    if (IsMultipleOf(price, MajorEvery))
                    {
                        stroke = MajorStroke;
                        showPrice = ShowMajorPrices;
                    }
                    else if (IsMultipleOf(price, HighlightEvery))
                    {
                        stroke = HighlightStroke;
                        showPrice = ShowHighlightPrices;
                    }

                    float y = chartScale.GetYByValue(price);
                    RenderTarget.DrawLine(
                        new SharpDX.Vector2(left, y),
                        new SharpDX.Vector2(right, y),
                        stroke.BrushDX, stroke.Width, stroke.StrokeStyle);

                    if (showPrice && labelFormat != null)
                        DrawPriceLabel(price, left, right, y, labelFormat, stroke.BrushDX);
                }
            }
            finally
            {
                if (labelFormat != null)
                    labelFormat.Dispose();
            }
        }

        private void DrawPriceLabel(double price, float left, float right, float y,
            SharpDX.DirectWrite.TextFormat labelFormat, SharpDX.Direct2D1.Brush brush)
        {
            string text = Instrument.MasterInstrument.FormatPrice(price);
            using (SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(
                Core.Globals.DirectWriteFactory, text, labelFormat, 300, labelFormat.FontSize))
            {
                float x = LabelPosition == PsychLabelPosition.Left ? left + 6 : right - layout.Metrics.Width - 6;
                RenderTarget.DrawTextLayout(
                    new SharpDX.Vector2(x, y - layout.Metrics.Height - 1),
                    layout, brush, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
            }
        }

        private static bool IsMultipleOf(double price, double every)
        {
            double nearest = Math.Round(price / every) * every;
            double tolerance = Math.Max(Math.Abs(price), every) * 1e-10;
            return Math.Abs(price - nearest) <= tolerance;
        }
    }
}
