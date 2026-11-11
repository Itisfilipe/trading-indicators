#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators.FilipeAmaral
{
    public enum MacroTimeZone
    {
        Exchange,
        Utc,
        NewYork,
        Chicago,
        London,
        Berlin,
        Tokyo,
        Sydney
    }

    public enum MacroStripPosition
    {
        Bottom,
        Top
    }

    /// <summary>
    /// The ICT macro windows: each one bracketed by a vertical line at its start
    /// and end with a captioned band joining the pair, the caption counting down
    /// to the window and then counting out what is left of it. Windows are drawn
    /// for the whole day, the ones still ahead included.
    /// Extracted from the TradingView "Time-Based Vertical Lines" port so the
    /// macro concept stands on its own; TimeBasedVerticalLines keeps the plain
    /// line slots.
    /// </summary>
    public class ICTMacros : Indicator
    {
        // Distinct days kept in the log: comfortably more than the five the
        // history cutoff can ask for, so the oldest day still wanted is always
        // still in there.
        private const int MaxTrackedDays = 20;

        private class MacroSpan
        {
            public int StartMinutes;
            public int EndMinutes;
            public string Caption;
        }

        private readonly List<MacroSpan> spans = new List<MacroSpan>();

        // Mutated on the data thread, read on the render thread.
        private readonly object sync = new object();
        private readonly List<DateTime> tradingDays = new List<DateTime>();

        private TimeZoneInfo displayZone;
        private System.Windows.Threading.DispatcherTimer refreshTimer;

        #region Properties

        [Display(Name = "Time Zone", GroupName = "1. General", Order = 1,
                 Description = "Zone every window time below is read in. Exchange means the instrument's own trading-hours zone.")]
        public MacroTimeZone TimeZoneSelection { get; set; }

        [Range(0, 5)]
        [Display(Name = "Days of History to Show", GroupName = "1. General", Order = 2,
                 Description = "How far back to keep drawing windows, counted from the last day the chart has bars for. 0 = that day and today.")]
        public int HistoryDays { get; set; }

        [Display(Name = "Include Tomorrow", GroupName = "1. General", Order = 3,
                 Description = "Also draw tomorrow's windows. Useful late in the session, when everything for today has already happened.")]
        public bool IncludeTomorrow { get; set; }

        [Display(Name = "Show Captions", GroupName = "1. General", Order = 4)]
        public bool ShowLabels { get; set; }

        [Display(Name = "Countdown In Captions", GroupName = "1. General", Order = 5,
                 Description = "Count down to a window still ahead, and count out what is left of one already running.")]
        public bool ShowCountdown { get; set; }

        [Display(Name = "Style", GroupName = "2. Band", Order = 1,
                 Description = "Color, width and dash shared by every window's two lines and its band border.")]
        public Stroke MacroStroke { get; set; }

        [Display(Name = "Strip Position", GroupName = "2. Band", Order = 2,
                 Description = "Which edge of the viewport the bands ride. Bottom by default, leaving the top free for the Time-Based Vertical Lines captions.")]
        public MacroStripPosition StripPosition { get; set; }

        [Range(0.0, 40.0)]
        [Display(Name = "Edge Gap (%)", GroupName = "2. Band", Order = 3,
                 Description = "Clear space between the panel edge and the bands, as a share of the panel height.")]
        public double EdgeGapPercent { get; set; }

        [Range(0.0, 40.0)]
        [Display(Name = "Band Height (%)", GroupName = "2. Band", Order = 4,
                 Description = "Thickness of the band joining a window's two lines, as a share of the panel height. 0 draws no band.")]
        public double BandHeightPercent { get; set; }

        [Range(0, 100)]
        [Display(Name = "Band Opacity (%)", GroupName = "2. Band", Order = 5)]
        public int BandOpacity { get; set; }

        [Display(Name = "Macro 1", GroupName = "3. Asia Macros", Order = 10)]
        public bool Macro1Enabled { get; set; }
        [Display(Name = "Macro 1 Start", GroupName = "3. Asia Macros", Order = 11)]
        public string Macro1Start { get; set; }
        [Display(Name = "Macro 1 End", GroupName = "3. Asia Macros", Order = 12,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro1End { get; set; }
        [Display(Name = "Macro 1 Label", GroupName = "3. Asia Macros", Order = 13,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro1Label { get; set; }

        [Display(Name = "Macro 2", GroupName = "3. Asia Macros", Order = 20)]
        public bool Macro2Enabled { get; set; }
        [Display(Name = "Macro 2 Start", GroupName = "3. Asia Macros", Order = 21)]
        public string Macro2Start { get; set; }
        [Display(Name = "Macro 2 End", GroupName = "3. Asia Macros", Order = 22,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro2End { get; set; }
        [Display(Name = "Macro 2 Label", GroupName = "3. Asia Macros", Order = 23,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro2Label { get; set; }

        [Display(Name = "Macro 3", GroupName = "3. Asia Macros", Order = 30)]
        public bool Macro3Enabled { get; set; }
        [Display(Name = "Macro 3 Start", GroupName = "3. Asia Macros", Order = 31)]
        public string Macro3Start { get; set; }
        [Display(Name = "Macro 3 End", GroupName = "3. Asia Macros", Order = 32,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro3End { get; set; }
        [Display(Name = "Macro 3 Label", GroupName = "3. Asia Macros", Order = 33,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro3Label { get; set; }

        [Display(Name = "Macro 4", GroupName = "3. Asia Macros", Order = 40)]
        public bool Macro4Enabled { get; set; }
        [Display(Name = "Macro 4 Start", GroupName = "3. Asia Macros", Order = 41)]
        public string Macro4Start { get; set; }
        [Display(Name = "Macro 4 End", GroupName = "3. Asia Macros", Order = 42,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro4End { get; set; }
        [Display(Name = "Macro 4 Label", GroupName = "3. Asia Macros", Order = 43,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro4Label { get; set; }

        [Display(Name = "Macro 5", GroupName = "3. Asia Macros", Order = 50)]
        public bool Macro5Enabled { get; set; }
        [Display(Name = "Macro 5 Start", GroupName = "3. Asia Macros", Order = 51)]
        public string Macro5Start { get; set; }
        [Display(Name = "Macro 5 End", GroupName = "3. Asia Macros", Order = 52,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro5End { get; set; }
        [Display(Name = "Macro 5 Label", GroupName = "3. Asia Macros", Order = 53,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro5Label { get; set; }

        [Display(Name = "Macro 6", GroupName = "3. Asia Macros", Order = 60)]
        public bool Macro6Enabled { get; set; }
        [Display(Name = "Macro 6 Start", GroupName = "3. Asia Macros", Order = 61)]
        public string Macro6Start { get; set; }
        [Display(Name = "Macro 6 End", GroupName = "3. Asia Macros", Order = 62,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro6End { get; set; }
        [Display(Name = "Macro 6 Label", GroupName = "3. Asia Macros", Order = 63,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro6Label { get; set; }

        [Display(Name = "Macro 7", GroupName = "3. Asia Macros", Order = 70)]
        public bool Macro7Enabled { get; set; }
        [Display(Name = "Macro 7 Start", GroupName = "3. Asia Macros", Order = 71)]
        public string Macro7Start { get; set; }
        [Display(Name = "Macro 7 End", GroupName = "3. Asia Macros", Order = 72,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro7End { get; set; }
        [Display(Name = "Macro 7 Label", GroupName = "3. Asia Macros", Order = 73,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro7Label { get; set; }

        [Display(Name = "Macro 8", GroupName = "4. London Macros", Order = 80)]
        public bool Macro8Enabled { get; set; }
        [Display(Name = "Macro 8 Start", GroupName = "4. London Macros", Order = 81)]
        public string Macro8Start { get; set; }
        [Display(Name = "Macro 8 End", GroupName = "4. London Macros", Order = 82,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro8End { get; set; }
        [Display(Name = "Macro 8 Label", GroupName = "4. London Macros", Order = 83,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro8Label { get; set; }

        [Display(Name = "Macro 9", GroupName = "4. London Macros", Order = 90)]
        public bool Macro9Enabled { get; set; }
        [Display(Name = "Macro 9 Start", GroupName = "4. London Macros", Order = 91)]
        public string Macro9Start { get; set; }
        [Display(Name = "Macro 9 End", GroupName = "4. London Macros", Order = 92,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro9End { get; set; }
        [Display(Name = "Macro 9 Label", GroupName = "4. London Macros", Order = 93,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro9Label { get; set; }

        [Display(Name = "Macro 10", GroupName = "4. London Macros", Order = 100)]
        public bool Macro10Enabled { get; set; }
        [Display(Name = "Macro 10 Start", GroupName = "4. London Macros", Order = 101)]
        public string Macro10Start { get; set; }
        [Display(Name = "Macro 10 End", GroupName = "4. London Macros", Order = 102,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro10End { get; set; }
        [Display(Name = "Macro 10 Label", GroupName = "4. London Macros", Order = 103,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro10Label { get; set; }

        [Display(Name = "Macro 11", GroupName = "4. London Macros", Order = 110)]
        public bool Macro11Enabled { get; set; }
        [Display(Name = "Macro 11 Start", GroupName = "4. London Macros", Order = 111)]
        public string Macro11Start { get; set; }
        [Display(Name = "Macro 11 End", GroupName = "4. London Macros", Order = 112,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro11End { get; set; }
        [Display(Name = "Macro 11 Label", GroupName = "4. London Macros", Order = 113,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro11Label { get; set; }

        [Display(Name = "Macro 12", GroupName = "5. NY Pre-Market Macros", Order = 120)]
        public bool Macro12Enabled { get; set; }
        [Display(Name = "Macro 12 Start", GroupName = "5. NY Pre-Market Macros", Order = 121)]
        public string Macro12Start { get; set; }
        [Display(Name = "Macro 12 End", GroupName = "5. NY Pre-Market Macros", Order = 122,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro12End { get; set; }
        [Display(Name = "Macro 12 Label", GroupName = "5. NY Pre-Market Macros", Order = 123,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro12Label { get; set; }

        [Display(Name = "Macro 13", GroupName = "5. NY Pre-Market Macros", Order = 130)]
        public bool Macro13Enabled { get; set; }
        [Display(Name = "Macro 13 Start", GroupName = "5. NY Pre-Market Macros", Order = 131)]
        public string Macro13Start { get; set; }
        [Display(Name = "Macro 13 End", GroupName = "5. NY Pre-Market Macros", Order = 132,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro13End { get; set; }
        [Display(Name = "Macro 13 Label", GroupName = "5. NY Pre-Market Macros", Order = 133,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro13Label { get; set; }

        [Display(Name = "Macro 14", GroupName = "5. NY Pre-Market Macros", Order = 140)]
        public bool Macro14Enabled { get; set; }
        [Display(Name = "Macro 14 Start", GroupName = "5. NY Pre-Market Macros", Order = 141)]
        public string Macro14Start { get; set; }
        [Display(Name = "Macro 14 End", GroupName = "5. NY Pre-Market Macros", Order = 142,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro14End { get; set; }
        [Display(Name = "Macro 14 Label", GroupName = "5. NY Pre-Market Macros", Order = 143,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro14Label { get; set; }

        [Display(Name = "Macro 15", GroupName = "5. NY Pre-Market Macros", Order = 150)]
        public bool Macro15Enabled { get; set; }
        [Display(Name = "Macro 15 Start", GroupName = "5. NY Pre-Market Macros", Order = 151)]
        public string Macro15Start { get; set; }
        [Display(Name = "Macro 15 End", GroupName = "5. NY Pre-Market Macros", Order = 152,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro15End { get; set; }
        [Display(Name = "Macro 15 Label", GroupName = "5. NY Pre-Market Macros", Order = 153,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro15Label { get; set; }

        [Display(Name = "Macro 16", GroupName = "6. NY AM Macros", Order = 160)]
        public bool Macro16Enabled { get; set; }
        [Display(Name = "Macro 16 Start", GroupName = "6. NY AM Macros", Order = 161)]
        public string Macro16Start { get; set; }
        [Display(Name = "Macro 16 End", GroupName = "6. NY AM Macros", Order = 162,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro16End { get; set; }
        [Display(Name = "Macro 16 Label", GroupName = "6. NY AM Macros", Order = 163,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro16Label { get; set; }

        [Display(Name = "Macro 17", GroupName = "6. NY AM Macros", Order = 170)]
        public bool Macro17Enabled { get; set; }
        [Display(Name = "Macro 17 Start", GroupName = "6. NY AM Macros", Order = 171)]
        public string Macro17Start { get; set; }
        [Display(Name = "Macro 17 End", GroupName = "6. NY AM Macros", Order = 172,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro17End { get; set; }
        [Display(Name = "Macro 17 Label", GroupName = "6. NY AM Macros", Order = 173,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro17Label { get; set; }

        [Display(Name = "Macro 18", GroupName = "6. NY AM Macros", Order = 180)]
        public bool Macro18Enabled { get; set; }
        [Display(Name = "Macro 18 Start", GroupName = "6. NY AM Macros", Order = 181)]
        public string Macro18Start { get; set; }
        [Display(Name = "Macro 18 End", GroupName = "6. NY AM Macros", Order = 182,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro18End { get; set; }
        [Display(Name = "Macro 18 Label", GroupName = "6. NY AM Macros", Order = 183,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro18Label { get; set; }

        [Display(Name = "Macro 19", GroupName = "7. NY Lunch Macros", Order = 190)]
        public bool Macro19Enabled { get; set; }
        [Display(Name = "Macro 19 Start", GroupName = "7. NY Lunch Macros", Order = 191)]
        public string Macro19Start { get; set; }
        [Display(Name = "Macro 19 End", GroupName = "7. NY Lunch Macros", Order = 192,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro19End { get; set; }
        [Display(Name = "Macro 19 Label", GroupName = "7. NY Lunch Macros", Order = 193,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro19Label { get; set; }

        [Display(Name = "Macro 20", GroupName = "8. NY PM Macros", Order = 200)]
        public bool Macro20Enabled { get; set; }
        [Display(Name = "Macro 20 Start", GroupName = "8. NY PM Macros", Order = 201)]
        public string Macro20Start { get; set; }
        [Display(Name = "Macro 20 End", GroupName = "8. NY PM Macros", Order = 202,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro20End { get; set; }
        [Display(Name = "Macro 20 Label", GroupName = "8. NY PM Macros", Order = 203,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro20Label { get; set; }

        [Display(Name = "Macro 21", GroupName = "8. NY PM Macros", Order = 210)]
        public bool Macro21Enabled { get; set; }
        [Display(Name = "Macro 21 Start", GroupName = "8. NY PM Macros", Order = 211)]
        public string Macro21Start { get; set; }
        [Display(Name = "Macro 21 End", GroupName = "8. NY PM Macros", Order = 212,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro21End { get; set; }
        [Display(Name = "Macro 21 Label", GroupName = "8. NY PM Macros", Order = 213,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro21Label { get; set; }

        [Display(Name = "Macro 22", GroupName = "9. Final Hour Macros", Order = 220)]
        public bool Macro22Enabled { get; set; }
        [Display(Name = "Macro 22 Start", GroupName = "9. Final Hour Macros", Order = 221)]
        public string Macro22Start { get; set; }
        [Display(Name = "Macro 22 End", GroupName = "9. Final Hour Macros", Order = 222,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro22End { get; set; }
        [Display(Name = "Macro 22 Label", GroupName = "9. Final Hour Macros", Order = 223,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro22Label { get; set; }

        [Display(Name = "Macro 23", GroupName = "9. Final Hour Macros", Order = 230)]
        public bool Macro23Enabled { get; set; }
        [Display(Name = "Macro 23 Start", GroupName = "9. Final Hour Macros", Order = 231)]
        public string Macro23Start { get; set; }
        [Display(Name = "Macro 23 End", GroupName = "9. Final Hour Macros", Order = 232,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro23End { get; set; }
        [Display(Name = "Macro 23 Label", GroupName = "9. Final Hour Macros", Order = 233,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro23Label { get; set; }

        [Display(Name = "Macro 24", GroupName = "9. Final Hour Macros", Order = 240)]
        public bool Macro24Enabled { get; set; }
        [Display(Name = "Macro 24 Start", GroupName = "9. Final Hour Macros", Order = 241)]
        public string Macro24Start { get; set; }
        [Display(Name = "Macro 24 End", GroupName = "9. Final Hour Macros", Order = 242,
                 Description = "An end at or before the start runs into the next day.")]
        public string Macro24End { get; set; }
        [Display(Name = "Macro 24 Label", GroupName = "9. Final Hour Macros", Order = 243,
                 Description = "Caption on the band. Empty uses the window's times.")]
        public string Macro24Label { get; set; }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "The ICT macro windows, each bracketed by a line at both ends with a captioned band joining the pair, countdowns included.";
                Name = "ICT Macros";
                Calculate = Calculate.OnPriceChange;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;

                TimeZoneSelection = MacroTimeZone.NewYork;
                HistoryDays = 1;
                IncludeTomorrow = false;
                ShowLabels = true;
                ShowCountdown = true;

                MacroStroke = new Stroke(Rgb(0xF5, 0x7C, 0x00), DashStyleHelper.Dot, 1f);
                StripPosition = MacroStripPosition.Bottom;
                EdgeGapPercent = 2.0;
                BandHeightPercent = 4.0;
                BandOpacity = 15;

                // The macro rule is :50 to :10 around every top of the hour, with
                // the last RTH hour split into the Final Hour and Market On Close
                // windows instead. The playbook trades the six RTH windows plus
                // those two, so exactly that set is on; every other session's
                // windows are carried for convenience but start off.
                Macro1Enabled = false; Macro1Start = "18:50"; Macro1End = "19:10"; Macro1Label = "";
                Macro2Enabled = false; Macro2Start = "19:50"; Macro2End = "20:10"; Macro2Label = "";
                Macro3Enabled = false; Macro3Start = "20:50"; Macro3End = "21:10"; Macro3Label = "";
                Macro4Enabled = false; Macro4Start = "21:50"; Macro4End = "22:10"; Macro4Label = "";
                Macro5Enabled = false; Macro5Start = "22:50"; Macro5End = "23:10"; Macro5Label = "";
                Macro6Enabled = false; Macro6Start = "23:50"; Macro6End = "00:10"; Macro6Label = "";
                Macro7Enabled = false; Macro7Start = "00:50"; Macro7End = "01:10"; Macro7Label = "";
                Macro8Enabled = false; Macro8Start = "01:50"; Macro8End = "02:10"; Macro8Label = "";
                Macro9Enabled = false; Macro9Start = "02:50"; Macro9End = "03:10"; Macro9Label = "";
                Macro10Enabled = false; Macro10Start = "03:50"; Macro10End = "04:10"; Macro10Label = "";
                Macro11Enabled = false; Macro11Start = "04:50"; Macro11End = "05:10"; Macro11Label = "";
                Macro12Enabled = false; Macro12Start = "05:50"; Macro12End = "06:10"; Macro12Label = "";
                Macro13Enabled = false; Macro13Start = "06:50"; Macro13End = "07:10"; Macro13Label = "";
                Macro14Enabled = false; Macro14Start = "07:50"; Macro14End = "08:10"; Macro14Label = "";
                Macro15Enabled = false; Macro15Start = "08:50"; Macro15End = "09:10"; Macro15Label = "";
                Macro16Enabled = true; Macro16Start = "09:50"; Macro16End = "10:10"; Macro16Label = "";
                Macro17Enabled = true; Macro17Start = "10:50"; Macro17End = "11:10"; Macro17Label = "";
                Macro18Enabled = true; Macro18Start = "11:50"; Macro18End = "12:10"; Macro18Label = "";
                Macro19Enabled = true; Macro19Start = "12:50"; Macro19End = "13:10"; Macro19Label = "";
                Macro20Enabled = true; Macro20Start = "13:50"; Macro20End = "14:10"; Macro20Label = "";
                Macro21Enabled = true; Macro21Start = "14:50"; Macro21End = "15:10"; Macro21Label = "";
                Macro22Enabled = true; Macro22Start = "15:15"; Macro22End = "15:45"; Macro22Label = "Final Hour";
                Macro23Enabled = true; Macro23Start = "15:45"; Macro23End = "16:00"; Macro23Label = "MOC";
                Macro24Enabled = false; Macro24Start = "15:50"; Macro24End = "16:00"; Macro24Label = "Settlement";
            }
            else if (State == State.DataLoaded)
            {
                displayZone = ResolveDisplayZone();
                spans.Clear();
                AddMacro(Macro1Enabled, Macro1Start, Macro1End, Macro1Label);
                AddMacro(Macro2Enabled, Macro2Start, Macro2End, Macro2Label);
                AddMacro(Macro3Enabled, Macro3Start, Macro3End, Macro3Label);
                AddMacro(Macro4Enabled, Macro4Start, Macro4End, Macro4Label);
                AddMacro(Macro5Enabled, Macro5Start, Macro5End, Macro5Label);
                AddMacro(Macro6Enabled, Macro6Start, Macro6End, Macro6Label);
                AddMacro(Macro7Enabled, Macro7Start, Macro7End, Macro7Label);
                AddMacro(Macro8Enabled, Macro8Start, Macro8End, Macro8Label);
                AddMacro(Macro9Enabled, Macro9Start, Macro9End, Macro9Label);
                AddMacro(Macro10Enabled, Macro10Start, Macro10End, Macro10Label);
                AddMacro(Macro11Enabled, Macro11Start, Macro11End, Macro11Label);
                AddMacro(Macro12Enabled, Macro12Start, Macro12End, Macro12Label);
                AddMacro(Macro13Enabled, Macro13Start, Macro13End, Macro13Label);
                AddMacro(Macro14Enabled, Macro14Start, Macro14End, Macro14Label);
                AddMacro(Macro15Enabled, Macro15Start, Macro15End, Macro15Label);
                AddMacro(Macro16Enabled, Macro16Start, Macro16End, Macro16Label);
                AddMacro(Macro17Enabled, Macro17Start, Macro17End, Macro17Label);
                AddMacro(Macro18Enabled, Macro18Start, Macro18End, Macro18Label);
                AddMacro(Macro19Enabled, Macro19Start, Macro19End, Macro19Label);
                AddMacro(Macro20Enabled, Macro20Start, Macro20End, Macro20Label);
                AddMacro(Macro21Enabled, Macro21Start, Macro21End, Macro21Label);
                AddMacro(Macro22Enabled, Macro22Start, Macro22End, Macro22Label);
                AddMacro(Macro23Enabled, Macro23Start, Macro23End, Macro23Label);
                AddMacro(Macro24Enabled, Macro24Start, Macro24End, Macro24Label);
            }
            else if (State == State.Realtime)
            {
                // Countdowns move with wall time, not with ticks, so a
                // once-a-second repaint keeps them honest through quiet tape.
                if (refreshTimer == null && ChartControl != null && ShowCountdown)
                {
                    ChartControl.Dispatcher.InvokeAsync(() =>
                    {
                        // The indicator can be removed while this callback is
                        // still queued; a timer born after teardown would tick
                        // for a dead indicator until the chart closes.
                        if (State >= State.Terminated || refreshTimer != null)
                            return;
                        refreshTimer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(1),
                            IsEnabled = true
                        };
                        refreshTimer.Tick += (o, e) => ForceRefresh();
                    });
                }
            }
            else if (State == State.Terminated)
            {
                if (refreshTimer != null)
                {
                    refreshTimer.IsEnabled = false;
                    refreshTimer = null;
                }
            }
        }

        // Frozen because the render thread reads the brush's color; an unfrozen
        // WPF brush is bound to its creating thread.
        private static System.Windows.Media.Brush Rgb(byte r, byte g, byte b)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        private TimeZoneInfo ResolveDisplayZone()
        {
            try
            {
                switch (TimeZoneSelection)
                {
                    case MacroTimeZone.Utc: return TimeZoneInfo.Utc;
                    case MacroTimeZone.NewYork: return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    case MacroTimeZone.Chicago: return TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
                    case MacroTimeZone.London: return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
                    case MacroTimeZone.Berlin: return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
                    case MacroTimeZone.Tokyo: return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
                    case MacroTimeZone.Sydney: return TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
                    default: return Bars.TradingHours.TimeZoneInfo;
                }
            }
            catch (TimeZoneNotFoundException)
            {
                return Core.Globals.GeneralOptions.TimeZoneInfo;
            }
        }

        // Reads HH:MM into minutes since midnight, or -1 when the entry is not a
        // valid clock time. Guarding here keeps a typo in one slot from taking
        // the whole indicator down.
        private static int ParseClockMinutes(string timeText)
        {
            string[] parts = (timeText ?? string.Empty).Trim().Split(':');
            int hour, minute;
            if (parts.Length != 2 || !int.TryParse(parts[0], out hour) || !int.TryParse(parts[1], out minute))
                return -1;
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59)
                return -1;
            return hour * 60 + minute;
        }

        private void AddMacro(bool enabled, string startText, string endText, string label)
        {
            if (!enabled)
                return;
            int startMinutes = ParseClockMinutes(startText);
            int endMinutes = ParseClockMinutes(endText);
            if (startMinutes < 0 || endMinutes < 0)
            {
                Log("ICT Macros: skipping macro \"" + startText + "\" to \"" + endText + "\", expected times as HH:MM.", LogLevel.Warning);
                return;
            }
            if (endMinutes == startMinutes)
            {
                Log("ICT Macros: skipping macro \"" + startText + "\" to \"" + endText + "\", a window cannot end the minute it starts.", LogLevel.Warning);
                return;
            }
            // An end before the start rolls into the next day, which is what
            // lets the 23:50 window close at 00:10.
            if (endMinutes < startMinutes)
                endMinutes += 24 * 60;
            // A named window keeps its name; the unnamed ones caption with
            // their times, which is how the :50-:10 rule windows read best.
            string trimmedLabel = (label ?? string.Empty).Trim();
            spans.Add(new MacroSpan
            {
                StartMinutes = startMinutes,
                EndMinutes = endMinutes,
                Caption = trimmedLabel.Length > 0
                    ? trimmedLabel
                    : "Macro " + ClockText(startMinutes) + "-" + ClockText(endMinutes % (24 * 60))
            });
        }

        private static string ClockText(int minutesOfDay)
        {
            return string.Format("{0:00}:{1:00}", minutesOfDay / 60, minutesOfDay % 60);
        }

        // mm:ss while under an hour, then "Xh MMm" -- the seconds stop being
        // useful once the window is that far out.
        private static string CountdownText(TimeSpan left)
        {
            if (left < TimeSpan.Zero)
                left = TimeSpan.Zero;
            return left.TotalHours >= 1
                ? string.Format("{0}h {1:00}m", (int)left.TotalHours, left.Minutes)
                : string.Format("{0:00}:{1:00}", left.Minutes, left.Seconds);
        }

        // Days are logged as the bars pass through them rather than counted back
        // from today, so a weekend or a holiday never collects a set of windows
        // nothing traded under.
        protected override void OnBarUpdate()
        {
            if (displayZone == null)
                return;
            DateTime tzDate = TimeZoneInfo.ConvertTime(Time[0], Core.Globals.GeneralOptions.TimeZoneInfo, displayZone).Date;
            lock (sync)
            {
                if (tradingDays.Count == 0 || tradingDays[tradingDays.Count - 1] != tzDate)
                {
                    tradingDays.Add(tzDate);
                    if (tradingDays.Count > MaxTrackedDays)
                        tradingDays.RemoveAt(0);
                }
            }
        }

        private DateTime PlatformNow()
        {
            return Connection.PlaybackConnection != null ? Connection.PlaybackConnection.Now : Core.Globals.Now;
        }

        // A clock time that does not exist on a daylight-saving switch day is
        // nudged forward an hour instead of throwing inside ConvertTime.
        private DateTime ToChartZone(DateTime tzUnspecified)
        {
            if (displayZone.IsInvalidTime(tzUnspecified))
                tzUnspecified = tzUnspecified.AddHours(1);
            return TimeZoneInfo.ConvertTime(tzUnspecified, displayZone, Core.Globals.GeneralOptions.TimeZoneInfo);
        }

        // X for any chart-zone time, including times past the last bar, where the
        // bar-to-pixel mapping runs out and the recent bars' own pace extends it.
        // On non-time bars (Renko, tick) that pace is an estimate, but so is any
        // other way of placing a future time on such a chart.
        // Median spacing of the last bars: a session gap inside the window would
        // otherwise shrink the pace and pile future times next to the last bar.
        private double TypicalBarMilliseconds()
        {
            List<double> gaps = new List<double>();
            for (int i = Math.Max(1, Bars.Count - 20); i < Bars.Count; i++)
                gaps.Add((Bars.GetTime(i) - Bars.GetTime(i - 1)).TotalMilliseconds);
            if (gaps.Count == 0)
                return 0;
            gaps.Sort();
            return gaps[gaps.Count / 2];
        }

        private double XForTime(ChartControl chartControl, DateTime chartZoneTime, DateTime lastBarTime, double lastBarX, double pixelsPerMs)
        {
            if (chartZoneTime <= lastBarTime)
                return chartControl.GetXByTime(chartZoneTime);
            if (pixelsPerMs <= 0)
                return double.MaxValue; // no pace to extend with; lands off-panel and is skipped
            return lastBarX + (chartZoneTime - lastBarTime).TotalMilliseconds * pixelsPerMs;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (Bars == null || ChartControl == null || ChartPanel == null || RenderTarget == null || displayZone == null)
                return;
            if (Bars.Count == 0 || spans.Count == 0)
                return;

            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

            DateTime lastBarTime = Bars.GetTime(Bars.Count - 1);
            double lastBarX = chartControl.GetXByTime(lastBarTime);

            // Pixels per millisecond from the last stretch of bars, for placing
            // times that lie past the last bar.
            double barMs = TypicalBarMilliseconds();
            double pixelsPerMs = barMs > 0 ? chartControl.Properties.BarDistance / barMs : 0;

            DateTime now = PlatformNow();
            DateTime todayTzDate = TimeZoneInfo.ConvertTime(now, Core.Globals.GeneralOptions.TimeZoneInfo, displayZone).Date;

            // History counts back from the newest day the chart has bars for, not
            // from today: counting from today would, over a weekend, push the last
            // day that actually traded out of the window.
            List<DateTime> daysToDraw = new List<DateTime>();
            lock (sync)
            {
                if (tradingDays.Count > 0)
                {
                    DateTime cutoff = tradingDays[tradingDays.Count - 1].AddDays(-HistoryDays);
                    foreach (DateTime trackedDay in tradingDays)
                        if (trackedDay >= cutoff && trackedDay < todayTzDate)
                            daysToDraw.Add(trackedDay);
                }
            }
            // A window crossing midnight belongs to the day it starts on, so
            // yesterday's crossing windows stay in while they may still be running,
            // even when history would otherwise drop yesterday.
            DateTime yesterday = todayTzDate.AddDays(-1);
            bool yesterdayCrossingOnly = !daysToDraw.Contains(yesterday);
            if (yesterdayCrossingOnly)
                daysToDraw.Add(yesterday);
            // Today and tomorrow go in whether or not they have bars yet: their
            // windows are the ones still ahead, which is the point of drawing them.
            daysToDraw.Add(todayTzDate);
            if (IncludeTomorrow)
                daysToDraw.Add(todayTzDate.AddDays(1));

            float panelLeft = ChartPanel.X;
            float panelRight = ChartPanel.X + ChartPanel.W;
            float panelTop = ChartPanel.Y;
            float panelBottom = ChartPanel.Y + ChartPanel.H;

            // The band strip is pinned to the viewport, a gap in from the chosen
            // edge, and rides there through scroll and zoom -- it costs no chart
            // scale, unlike the TradingView original, which had to give up price
            // range for the room.
            float edgeGap = (float)(ChartPanel.H * EdgeGapPercent / 100);
            float bandHeight = (float)(ChartPanel.H * BandHeightPercent / 100);
            float bandTop = StripPosition == MacroStripPosition.Top
                ? panelTop + edgeGap
                : panelBottom - edgeGap - bandHeight;

            SimpleFont wpfFont = chartControl.Properties.LabelFont ?? new SimpleFont();
            SharpDX.DirectWrite.TextFormat textFormat = wpfFont.ToDirectWriteTextFormat();
            SharpDX.Direct2D1.SolidColorBrush bandFillBrush = null;
            try
            {
                textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;

                System.Windows.Media.SolidColorBrush macroSolid = MacroStroke.Brush as System.Windows.Media.SolidColorBrush;
                System.Windows.Media.Color macroColor = macroSolid != null ? macroSolid.Color : Colors.Orange;
                bandFillBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                    new SharpDX.Color(macroColor.R, macroColor.G, macroColor.B, (byte)(255 * BandOpacity / 100)));
                MacroStroke.RenderTarget = RenderTarget;

                foreach (DateTime dayStart in daysToDraw)
                    foreach (MacroSpan span in spans)
                    {
                        if (yesterdayCrossingOnly && dayStart == yesterday && span.EndMinutes <= 24 * 60)
                            continue;
                        DateTime spanStart = ToChartZone(dayStart.AddMinutes(span.StartMinutes));
                        DateTime spanEnd = ToChartZone(dayStart.AddMinutes(span.EndMinutes));
                        double startX = XForTime(chartControl, spanStart, lastBarTime, lastBarX, pixelsPerMs);
                        double endX = XForTime(chartControl, spanEnd, lastBarTime, lastBarX, pixelsPerMs);
                        if (endX < panelLeft || startX > panelRight)
                            continue;

                        // Both edges, always: a macro is a span, and one line can
                        // only say when it opens. The band between them carries
                        // the name, which is what makes the pair read as one
                        // window instead of two loose lines.
                        if (startX >= panelLeft)
                            RenderTarget.DrawLine(
                                new SharpDX.Vector2((float)startX, panelTop),
                                new SharpDX.Vector2((float)startX, panelBottom),
                                MacroStroke.BrushDX, MacroStroke.Width, MacroStroke.StrokeStyle);
                        if (endX <= panelRight)
                            RenderTarget.DrawLine(
                                new SharpDX.Vector2((float)endX, panelTop),
                                new SharpDX.Vector2((float)endX, panelBottom),
                                MacroStroke.BrushDX, MacroStroke.Width, MacroStroke.StrokeStyle);

                        // Pixels drawn in the hit-test pass take the click and would block click-and-drag pan.
                        if (bandHeight > 0 && !IsInHitTest)
                        {
                            double bandLeft = Math.Max(startX, panelLeft);
                            double bandRight = Math.Min(endX, panelRight);
                            SharpDX.RectangleF band = new SharpDX.RectangleF(
                                (float)bandLeft, bandTop, (float)(bandRight - bandLeft), bandHeight);
                            RenderTarget.FillRectangle(band, bandFillBrush);
                            RenderTarget.DrawRectangle(band, MacroStroke.BrushDX, MacroStroke.Width, MacroStroke.StrokeStyle);
                        }

                        if (!ShowLabels)
                            continue;
                        // One caption for the whole window, the countdown on its
                        // own line under the name, changing tense with the
                        // window: counting down to the open while it is ahead,
                        // counting out what is left of it once inside.
                        string caption = span.Caption;
                        if (ShowCountdown)
                        {
                            if (now < spanStart)
                                caption += "\nin " + CountdownText(spanStart - now);
                            else if (now < spanEnd)
                                caption += "\n" + CountdownText(spanEnd - now) + " left";
                        }
                        DrawCaption(caption, (float)((startX + endX) / 2), bandTop + bandHeight / 2, textFormat);
                    }
            }
            finally
            {
                if (bandFillBrush != null)
                    bandFillBrush.Dispose();
                textFormat.Dispose();
            }
        }

        // The layout is a fixed-width box centered on the band's middle with
        // centered text alignment, so the name line and the (shorter)
        // countdown line under it each center themselves rather than sharing
        // the block's left edge.
        private void DrawCaption(string caption, float centreX, float centreY, SharpDX.DirectWrite.TextFormat textFormat)
        {
            const float LayoutWidth = 600f;
            SharpDX.DirectWrite.TextLayout layout = new SharpDX.DirectWrite.TextLayout(
                Core.Globals.DirectWriteFactory, caption, textFormat, LayoutWidth, textFormat.FontSize);
            try
            {
                layout.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
                RenderTarget.DrawTextLayout(
                    new SharpDX.Vector2(centreX - LayoutWidth / 2, centreY - layout.Metrics.Height / 2),
                    layout, MacroStroke.BrushDX, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
            }
            finally
            {
                layout.Dispose();
            }
        }
    }
}
