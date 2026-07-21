#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.DrawingTools
{
    /// <summary>
    /// Risk/reward drawing tool with up to three independently draggable targets.
    /// Two clicks place entry and stop; targets seed at 1R/2R/3R and then every
    /// anchor moves freely, with each target's R multiple recomputed live from
    /// the current entry/stop distance (the built-in RiskReward tool instead
    /// derives one side from the other through a fixed ratio).
    /// </summary>
    public class RiskRewardTargets : DrawingTool
    {
        private const int CursorSensitivity = 15;
        private ChartAnchor editingAnchor;

        [Display(Order = 1)]
        public ChartAnchor EntryAnchor { get; set; }
        [Display(Order = 2)]
        public ChartAnchor StopAnchor { get; set; }
        [Display(Order = 3)]
        public ChartAnchor Target1Anchor { get; set; }
        [Display(Order = 4)]
        public ChartAnchor Target2Anchor { get; set; }
        [Display(Order = 5)]
        public ChartAnchor Target3Anchor { get; set; }

        public override object Icon { get { return Icons.DrawRiskReward; } }

        [Range(1, 3)]
        [Display(Name = "Targets", GroupName = "General", Order = 1,
                 Description = "How many targets are shown and draggable (1 to 3).")]
        public int Targets { get; set; }

        [Display(Name = "Anchor line", GroupName = "Lines", Order = 1)]
        public Stroke AnchorLineStroke { get; set; }
        [Display(Name = "Entry line", GroupName = "Lines", Order = 2)]
        public Stroke EntryLineStroke { get; set; }
        [Display(Name = "Stop line", GroupName = "Lines", Order = 3)]
        public Stroke StopLineStroke { get; set; }
        [Display(Name = "Target line", GroupName = "Lines", Order = 4)]
        public Stroke TargetLineStroke { get; set; }

        [Display(Name = "Extend lines left", GroupName = "Lines", Order = 5)]
        public bool IsExtendedLinesLeft { get; set; }
        [Display(Name = "Extend lines right", GroupName = "Lines", Order = 6)]
        public bool IsExtendedLinesRight { get; set; }

        // All five anchors always serialize and move with the tool; a target beyond
        // the configured count is only skipped for rendering and hit testing, so
        // raising the count later brings it back where it last was.
        //
        // Never-placed anchors are withheld: with snap-to-object on, the platform
        // walks every object's Anchors looking for snap candidates, and an anchor
        // that was never given a time or slot (a build interrupted mid-placement,
        // preserved by a workspace save) sends its snap logic through a
        // NullReferenceException on every mouse move over the chart.
        public override IEnumerable<ChartAnchor> Anchors
        {
            get
            {
                ChartAnchor[] all = { EntryAnchor, StopAnchor, Target1Anchor, Target2Anchor, Target3Anchor };
                foreach (ChartAnchor anchor in all)
                    if (anchor != null && anchor.Time != default(DateTime))
                        yield return anchor;
            }
        }

        private IEnumerable<ChartAnchor> EnabledTargetAnchors()
        {
            yield return Target1Anchor;
            if (Targets >= 2)
                yield return Target2Anchor;
            if (Targets >= 3)
                yield return Target3Anchor;
        }

        private IEnumerable<ChartAnchor> EnabledAnchors()
        {
            yield return EntryAnchor;
            yield return StopAnchor;
            foreach (ChartAnchor target in EnabledTargetAnchors())
                yield return target;
        }

        // Placed means "seeded with real prices", which is only in question while
        // the tool is still being built -- outside Building (including a restored
        // tool, or dragging a target, which sets IsEditing again) targets are
        // always placed. Keying on IsEditing alone would hide every target while
        // one of them is being dragged.
        private bool TargetsPlaced
        {
            get { return DrawingState != DrawingState.Building || !Target1Anchor.IsEditing; }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Risk/reward with up to three freely draggable targets; each target shows its own R.";
                Name = "Risk Reward Targets";
                Targets = 3;
                AnchorLineStroke = new Stroke(Brushes.DarkGray, DashStyleHelper.Solid, 1f, 50);
                EntryLineStroke = new Stroke(Brushes.Goldenrod, DashStyleHelper.Solid, 2f);
                StopLineStroke = new Stroke(Brushes.Crimson, DashStyleHelper.Solid, 2f);
                TargetLineStroke = new Stroke(Brushes.SeaGreen, DashStyleHelper.Solid, 2f);
                EntryAnchor = new ChartAnchor { IsEditing = true, DrawingTool = this };
                StopAnchor = new ChartAnchor { IsEditing = true, DrawingTool = this };
                Target1Anchor = new ChartAnchor { IsEditing = true, DrawingTool = this };
                Target2Anchor = new ChartAnchor { IsEditing = true, DrawingTool = this };
                Target3Anchor = new ChartAnchor { IsEditing = true, DrawingTool = this };
                EntryAnchor.DisplayName = "Entry";
                StopAnchor.DisplayName = "Stop";
                Target1Anchor.DisplayName = "Target 1";
                Target2Anchor.DisplayName = "Target 2";
                Target3Anchor.DisplayName = "Target 3";
            }
            else if (State == State.Terminated)
                Dispose();
        }

        /// <summary>
        /// Seeds the targets at 1R/2R/3R on the profit side of the entry, aligned to
        /// the entry's bar. Only used while building; afterwards targets move freely.
        /// Never fails: tick rounding is the only thing the instrument was needed
        /// for, and rounding is cosmetic. A seed that could bail would strand the
        /// tool in its build state, and a half-built drawing left active grinds the
        /// platform's snap logic through nulls on every subsequent mouse event.
        /// </summary>
        private void SeedTargetsFromStop()
        {
            MasterInstrument master = AttachedTo?.Instrument?.MasterInstrument;
            double entryPrice = master != null ? master.RoundToTickSize(EntryAnchor.Price) : EntryAnchor.Price;
            double stopPrice = master != null ? master.RoundToTickSize(StopAnchor.Price) : StopAnchor.Price;
            double signedRisk = entryPrice - stopPrice;

            ChartAnchor[] targets = { Target1Anchor, Target2Anchor, Target3Anchor };
            for (int i = 0; i < targets.Length; i++)
            {
                double price = entryPrice + (i + 1) * signedRisk;
                targets[i].Price = master != null ? master.RoundToTickSize(price) : price;
                targets[i].Time = EntryAnchor.Time;
                targets[i].SlotIndex = EntryAnchor.SlotIndex;
                targets[i].IsEditing = false;
            }
        }

        public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            switch (DrawingState)
            {
                case DrawingState.Building:
                    if (EntryAnchor.IsEditing)
                    {
                        dataPoint.CopyDataValues(EntryAnchor);
                        dataPoint.CopyDataValues(StopAnchor);
                        EntryAnchor.IsEditing = false;
                    }
                    else if (StopAnchor.IsEditing)
                    {
                        dataPoint.CopyDataValues(StopAnchor);
                        StopAnchor.IsEditing = false;
                        SeedTargetsFromStop();
                    }
                    if (!EntryAnchor.IsEditing && !StopAnchor.IsEditing && TargetsPlaced)
                    {
                        DrawingState = DrawingState.Normal;
                        IsSelected = false;
                    }
                    break;
                case DrawingState.Normal:
                    Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
                    editingAnchor = GetClosestEnabledAnchor(chartControl, chartPanel, chartScale, point);
                    if (editingAnchor != null)
                    {
                        editingAnchor.IsEditing = true;
                        DrawingState = DrawingState.Editing;
                    }
                    else if (GetCursor(chartControl, chartPanel, chartScale, point) == null)
                        IsSelected = false; // missed both anchors and lines
                    else
                        DrawingState = DrawingState.Moving;
                    break;
            }
        }

        public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (IsLocked && DrawingState != DrawingState.Building || !IsVisible)
                return;

            if (DrawingState == DrawingState.Building)
            {
                if (EntryAnchor.IsEditing)
                    dataPoint.CopyDataValues(EntryAnchor);
                else if (StopAnchor.IsEditing)
                {
                    dataPoint.CopyDataValues(StopAnchor);
                    // Live-preview the targets while the stop is being placed; the
                    // second mouse-down still runs the completion path.
                    SeedTargetsFromStop();
                }
            }
            else if (DrawingState == DrawingState.Editing && editingAnchor != null)
                dataPoint.CopyDataValues(editingAnchor);
            else if (DrawingState == DrawingState.Moving)
                foreach (ChartAnchor anchor in Anchors)
                    anchor.MoveAnchor(InitialMouseDownAnchor, dataPoint, chartControl, chartPanel, chartScale, this);
        }

        public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (DrawingState == DrawingState.Building)
                return;

            if (DrawingState == DrawingState.Editing || DrawingState == DrawingState.Moving)
                DrawingState = DrawingState.Normal;
            if (editingAnchor != null)
                editingAnchor.IsEditing = false;
            editingAnchor = null;
        }

        private ChartAnchor GetClosestEnabledAnchor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
        {
            ChartAnchor closest = null;
            double closestDistance = double.MaxValue;
            foreach (ChartAnchor anchor in EnabledAnchors())
            {
                Point anchorPoint = anchor.GetPoint(chartControl, chartPanel, chartScale);
                double distance = (anchorPoint - point).Length;
                if (distance <= CursorSensitivity && distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = anchor;
                }
            }
            return closest;
        }

        private void GetLineBounds(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale,
            out double lineStartX, out double lineEndX)
        {
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            foreach (ChartAnchor anchor in EnabledAnchors())
            {
                // Un-seeded targets still carry default time/slot values; letting
                // them into the bounds would stretch the entry/stop lines to a
                // bogus X during the first phase of building.
                if (!TargetsPlaced && anchor != EntryAnchor && anchor != StopAnchor)
                    continue;
                double x = anchor.GetPoint(chartControl, chartPanel, chartScale).X;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
            }
            lineStartX = IsExtendedLinesLeft ? chartPanel.X : minX;
            lineEndX = IsExtendedLinesRight ? chartPanel.X + chartPanel.W : maxX;
        }

        public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
        {
            switch (DrawingState)
            {
                case DrawingState.Building: return Cursors.Pen;
                case DrawingState.Moving: return IsLocked ? Cursors.No : Cursors.SizeAll;
                case DrawingState.Editing: return IsLocked ? Cursors.No : Cursors.SizeNS;
                default:
                    ChartAnchor closest = GetClosestEnabledAnchor(chartControl, chartPanel, chartScale, point);
                    if (closest != null)
                        return IsLocked ? Cursors.Arrow : Cursors.SizeNS;

                    // Near any of the horizontal price lines counts as "on the tool"
                    // and drags the whole thing.
                    double lineStartX, lineEndX;
                    GetLineBounds(chartControl, chartPanel, chartScale, out lineStartX, out lineEndX);
                    if (point.X >= lineStartX - CursorSensitivity && point.X <= lineEndX + CursorSensitivity)
                        foreach (ChartAnchor anchor in EnabledAnchors())
                        {
                            double lineY = anchor.GetPoint(chartControl, chartPanel, chartScale).Y;
                            if (Math.Abs(point.Y - lineY) <= CursorSensitivity)
                                return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
                        }

                    // The diagonal connectors are part of the hit-test rendering, so
                    // they must also answer here -- otherwise clicking one deselects
                    // the tool instead of starting a move.
                    Point entryPixel = EntryAnchor.GetPoint(chartControl, chartPanel, chartScale);
                    Vector stopVector = StopAnchor.GetPoint(chartControl, chartPanel, chartScale) - entryPixel;
                    if (MathHelper.IsPointAlongVector(point, entryPixel, stopVector, CursorSensitivity))
                        return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
                    if (TargetsPlaced)
                        foreach (ChartAnchor target in EnabledTargetAnchors())
                        {
                            Vector targetVector = target.GetPoint(chartControl, chartPanel, chartScale) - entryPixel;
                            if (MathHelper.IsPointAlongVector(point, entryPixel, targetVector, CursorSensitivity))
                                return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
                        }
                    return null;
            }
        }

        public override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
        {
            ChartPanel chartPanel = chartControl.ChartPanels[chartScale.PanelIndex];
            List<Point> points = new List<Point>();
            foreach (ChartAnchor anchor in EnabledAnchors())
            {
                if (anchor.IsEditing && DrawingState == DrawingState.Building)
                    continue;
                points.Add(anchor.GetPoint(chartControl, chartPanel, chartScale));
            }
            return points.ToArray();
        }

        public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
        {
            if (DrawingState == DrawingState.Building)
                return true;

            // An extended line always reaches the panel edge, so the tool is on
            // screen no matter where its anchors sit.
            if (IsExtendedLinesLeft || IsExtendedLinesRight)
                return true;

            // Interval overlap rather than any-anchor-inside: anchors straddling
            // the viewport (one left of it, one right) still draw lines through it.
            DateTime minTime = DateTime.MaxValue;
            DateTime maxTime = DateTime.MinValue;
            foreach (ChartAnchor anchor in Anchors)
            {
                if (anchor.Time < minTime) minTime = anchor.Time;
                if (anchor.Time > maxTime) maxTime = anchor.Time;
            }
            return minTime <= lastTimeOnChart && maxTime >= firstTimeOnChart;
        }

        public override void OnCalculateMinMax()
        {
            MinValue = double.MaxValue;
            MaxValue = double.MinValue;

            if (!IsVisible)
                return;

            // Only un-seeded anchors during Building are skipped (a default price of
            // 0 would blow up the auto-scale range). An established anchor being
            // dragged keeps IsEditing set too, and the scale must keep following it.
            foreach (ChartAnchor anchor in EnabledAnchors())
            {
                if (DrawingState == DrawingState.Building && anchor.IsEditing)
                    continue;
                MinValue = Math.Min(anchor.Price, MinValue);
                MaxValue = Math.Max(anchor.Price, MaxValue);
            }
        }

        public override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (!IsVisible)
                return;
            if (Anchors.All(a => a.IsEditing))
                return;

            // The attachment empties while the tool is deleted or the workspace is
            // still loading; a render pass caught in that window draws nothing.
            MasterInstrument master = AttachedTo?.Instrument?.MasterInstrument;
            if (master == null)
                return;
            ChartPanel chartPanel = chartControl.ChartPanels[PanelIndex];
            double entryPrice = master.RoundToTickSize(EntryAnchor.Price);
            double stopPrice = master.RoundToTickSize(StopAnchor.Price);
            double signedRisk = entryPrice - stopPrice;

            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;

            AnchorLineStroke.RenderTarget = RenderTarget;
            EntryLineStroke.RenderTarget = RenderTarget;
            StopLineStroke.RenderTarget = RenderTarget;
            TargetLineStroke.RenderTarget = RenderTarget;

            Point entryPoint = EntryAnchor.GetPoint(chartControl, chartPanel, chartScale);
            Point stopPoint = StopAnchor.GetPoint(chartControl, chartPanel, chartScale);

            // Faint connector from entry to stop, like the built-in tool, so the
            // tool reads as one object when anchors sit on different bars.
            SharpDX.Direct2D1.Brush connectorBrush = IsInHitTest ? chartControl.SelectionBrush : AnchorLineStroke.BrushDX;
            RenderTarget.DrawLine(entryPoint.ToVector2(), stopPoint.ToVector2(), connectorBrush, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);

            double lineStartX, lineEndX;
            GetLineBounds(chartControl, chartPanel, chartScale, out lineStartX, out lineEndX);

            SimpleFont wpfFont = chartControl.Properties.LabelFont ?? new SimpleFont();
            SharpDX.DirectWrite.TextFormat textFormat = wpfFont.ToDirectWriteTextFormat();
            try
            {
                textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
                textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;

                DrawPriceLine(EntryLineStroke, entryPoint.Y, lineStartX, lineEndX, chartControl, textFormat,
                    master.FormatPrice(entryPrice));

                double riskTicks = Math.Abs(signedRisk) / master.TickSize;
                DrawPriceLine(StopLineStroke, stopPoint.Y, lineStartX, lineEndX, chartControl, textFormat,
                    string.Format("{0}  ({1:F0}t)", master.FormatPrice(stopPrice), riskTicks));

                if (TargetsPlaced)
                {
                    int targetNumber = 1;
                    foreach (ChartAnchor target in EnabledTargetAnchors())
                    {
                        Point targetPoint = target.GetPoint(chartControl, chartPanel, chartScale);
                        RenderTarget.DrawLine(entryPoint.ToVector2(), targetPoint.ToVector2(), connectorBrush, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);

                        double targetPrice = master.RoundToTickSize(target.Price);
                        double targetTicks = Math.Abs(targetPrice - entryPrice) / master.TickSize;
                        // R is reward over risk from the CURRENT anchors; works for
                        // long and short alike because both terms flip sign together.
                        string rText = signedRisk.ApproxCompare(0) == 0
                            ? "R n/a"
                            : string.Format("{0:0.0}R", (targetPrice - entryPrice) / signedRisk);
                        DrawPriceLine(TargetLineStroke, targetPoint.Y, lineStartX, lineEndX, chartControl, textFormat,
                            string.Format("T{0} {1}  ({2:F0}t, {3})", targetNumber, master.FormatPrice(targetPrice), targetTicks, rText));
                        targetNumber++;
                    }
                }
            }
            finally
            {
                textFormat.Dispose();
            }
        }

        private void DrawPriceLine(Stroke stroke, double y, double lineStartX, double lineEndX,
            ChartControl chartControl, SharpDX.DirectWrite.TextFormat textFormat, string label)
        {
            SharpDX.Direct2D1.Brush brush = IsInHitTest ? chartControl.SelectionBrush : stroke.BrushDX;
            RenderTarget.DrawLine(
                new SharpDX.Vector2((float)lineStartX, (float)y),
                new SharpDX.Vector2((float)lineEndX, (float)y),
                brush, stroke.Width, stroke.StrokeStyle);

            if (IsInHitTest)
                return;

            SharpDX.DirectWrite.TextLayout textLayout = new SharpDX.DirectWrite.TextLayout(
                Core.Globals.DirectWriteFactory, label, textFormat, 600, textFormat.FontSize);
            try
            {
                float textX = (float)(lineEndX - textLayout.Metrics.Width - 4);
                float textY = (float)(y - textFormat.FontSize - 4);
                RenderTarget.DrawTextLayout(new SharpDX.Vector2(textX, textY), textLayout, stroke.BrushDX,
                    SharpDX.Direct2D1.DrawTextOptions.NoSnap);
            }
            finally
            {
                textLayout.Dispose();
            }
        }
    }
}
