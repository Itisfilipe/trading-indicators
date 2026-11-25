const predef = require("./tools/predef");
const { du } = require("./tools/graphics");

const colors = {
    blue: "#2962FF", london: "#9C27B0", premarket: "#FF9800",
    nyAm: "#089981", nyPm: "#F23645", periodLiquidity: "#455A64",
    openings: "#000000", gray: "#787B86", amber: "#B8862B"
};

const presetNames = {
    blue: "Asia (Blue)", london: "London (Purple)", premarket: "Pre-market (Orange)",
    nyAm: "NY AM (Teal)", nyPm: "NY PM / MOC (Red)",
    periodLiquidity: "Period liquidity (Blue-gray)", openings: "Openings / NWOG (Black)",
    gray: "NDOG (Gray)", amber: "Amber", custom: "Custom color"
};

// Screenshot presets use distinct edge and midpoint colors on a white chart.
const chartZones = {
    redZone: { edge: "#EFB2B6", middle: "#DF484C", fill: "#D7414B" },
    orangeZone: { edge: "#F8D8A1", middle: "#F19D38", fill: "#EB990F" },
    cyanZone: { edge: "#AAE2EC", middle: "#54B9D1", fill: "#23AFD7" },
    purpleZone: { edge: "#BFB1DE", middle: "#613CB0", fill: "#5F41AF" },
    grayZone: { edge: "#CCCCCC", middle: "#000000", fill: "#000000" }
};
Object.assign(presetNames, { redZone: "Chart zone (Red)", orangeZone: "Chart zone (Orange)", cyanZone: "Chart zone (Cyan)", purpleZone: "Chart zone (Purple)", grayZone: "Chart zone (Gray)" });

const rectangle = {
    render({ anchors, props }) {
        const [a, b] = anchors;
        if (!a || !b) return { items: [] };

        const color = props.colorPreset === "custom"
            ? props.customColor
            : colors[props.colorPreset] || colors.blue;
        const zone = chartZones[props.colorPreset];
        const corners = [a, { x: b.x, y: a.y }, b, { x: a.x, y: b.y }];
        const shape = { tag: "Polygon", points: corners };
        const items = [
            {
                tag: "Shapes", key: "fill", primitives: [shape],
                fillStyle: { color: zone ? zone.fill : color, opacity: zone ? (props.chartZoneOpacityPercent ?? 10) : (props.fillOpacityPercent ?? 15) }
            },
            {
                tag: "ContourShapes", key: "border", primitives: [shape],
                lineStyle: { color: zone ? zone.edge : color, lineWidth: 1 }
            }
        ];

        const divisions = props.divideIntoEighths ? 8 : props.divideIntoQuarters ? 4 : props.showMidline ? 2 : 1;
        for (let i = 1; i < divisions; i++) {
            const fraction = i / divisions;
            const y = du(a.y.value + (b.y.value - a.y.value) * fraction);
            const middle = fraction === 0.5;
            items.push({
                tag: "LineSegments", key: middle ? "midline" : "level" + fraction,
                lines: [{ tag: "Line", a: { x: a.x, y }, b: { x: b.x, y } }],
                lineStyle: { color: zone ? zone.middle : color, lineWidth: 1,
                    lineStyle: middle && zone ? 1 : 3, opacity: middle ? 100 : 65 }
            });
        }
        return { items };
    }
};

module.exports = {
    name: "LRMRectangle",
    description: "LRM Rectangle",
    tags: ["LRM"],
    drawing: rectangle,
    minN: 2,
    maxN: 2,
    params: {
        colorPreset: predef.paramSpecs.enum(presetNames, "blue"),
        customColor: predef.paramSpecs.color(colors.blue),
        showMidline: predef.paramSpecs.bool(true),
        divideIntoQuarters: predef.paramSpecs.bool(false),
        divideIntoEighths: predef.paramSpecs.bool(false),
        chartZoneOpacityPercent: predef.paramSpecs.percent(10, 1, 0, 100),
        fillOpacityPercent: predef.paramSpecs.percent(15, 1, 0, 100)
    }
};
