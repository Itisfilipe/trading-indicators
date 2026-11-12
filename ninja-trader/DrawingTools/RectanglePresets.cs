#region Using declarations
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.DrawingTools
{
    /// <summary>
    /// Rectangle Midline in three fixed colors, each its own entry in the
    /// drawing-tools menu so a zone of the right color is one click away.
    /// Every property stays editable and each tool keeps its own templates.
    /// </summary>
    internal static class RectanglePreset
    {
        public static System.Windows.Media.SolidColorBrush Rgb(byte r, byte g, byte b)
        {
            System.Windows.Media.SolidColorBrush brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public static void ApplyColor(RectangleMidline tool, System.Windows.Media.Brush color)
        {
            tool.OutlineStroke = new Stroke(color, DashStyleHelper.Solid, 1f);
            tool.AreaBrush = color;
            tool.MidlineStroke = new Stroke(color, DashStyleHelper.Dash, 1f);
            tool.QuarterStroke = new Stroke(color, DashStyleHelper.Dot, 1f);
        }

        public static object Icon(System.Windows.Media.SolidColorBrush color)
        {
            System.Windows.Media.SolidColorBrush fill = new System.Windows.Media.SolidColorBrush(color.Color) { Opacity = 0.35 };
            return new System.Windows.Shapes.Rectangle
            {
                Width = 14,
                Height = 10,
                Fill = fill,
                Stroke = color,
                StrokeThickness = 1.5,
            };
        }
    }

    public class RectangleBlue : RectangleMidline
    {
        private static readonly System.Windows.Media.SolidColorBrush PresetColor = RectanglePreset.Rgb(0x29, 0x62, 0xFF);

        public override object Icon => RectanglePreset.Icon(PresetColor);

        protected override void OnStateChange()
        {
            base.OnStateChange();
            if (State == State.SetDefaults)
            {
                Name = "Rectangle Blue";
                RectanglePreset.ApplyColor(this, PresetColor);
            }
        }
    }

    public class RectangleGray : RectangleMidline
    {
        private static readonly System.Windows.Media.SolidColorBrush PresetColor = RectanglePreset.Rgb(0x78, 0x7B, 0x86);

        public override object Icon => RectanglePreset.Icon(PresetColor);

        protected override void OnStateChange()
        {
            base.OnStateChange();
            if (State == State.SetDefaults)
            {
                Name = "Rectangle Gray";
                RectanglePreset.ApplyColor(this, PresetColor);
            }
        }
    }

    public class RectangleAmber : RectangleMidline
    {
        private static readonly System.Windows.Media.SolidColorBrush PresetColor = RectanglePreset.Rgb(0xB8, 0x86, 0x2B);

        public override object Icon => RectanglePreset.Icon(PresetColor);

        protected override void OnStateChange()
        {
            base.OnStateChange();
            if (State == State.SetDefaults)
            {
                Name = "Rectangle Amber";
                RectanglePreset.ApplyColor(this, PresetColor);
            }
        }
    }
}
