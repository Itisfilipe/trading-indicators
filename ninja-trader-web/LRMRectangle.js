const predef = require("./tools/predef");
const { du } = require("./tools/graphics");

const colors = { blue: "#2962FF", gray: "#787B86", amber: "#B8862B" };

const rectangle = {
    render({ anchors, props }) {
        const [a, b] = anchors;
        if (!a || !b) return { items: [] };

        const color = colors[props.colorPreset] || colors.blue;
        const corners = [a, { x: b.x, y: a.y }, b, { x: a.x, y: b.y }];
        const shape = { tag: "Polygon", points: corners };
        const items = [
            {
                tag: "Shapes", key: "fill", primitives: [shape],
                fillStyle: { color, opacity: props.fillOpacityPercent / 100 }
            },
            {
                tag: "ContourShapes", key: "border", primitives: [shape],
                lineStyle: { color, lineWidth: 1 }
            }
        ];

        if (props.showMidline) {
            const y = du((a.y.value + b.y.value) / 2);
            items.push({
                tag: "LineSegments", key: "midline",
                lines: [{ tag: "Line", a: { x: a.x, y }, b: { x: b.x, y } }],
                lineStyle: { color, lineWidth: 1, lineStyle: 3 }
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
        colorPreset: predef.paramSpecs.enum({ blue: "Blue", gray: "Gray", amber: "Amber" }, "blue"),
        showMidline: predef.paramSpecs.bool(true),
        fillOpacityPercent: predef.paramSpecs.percent(15, 1, 0, 100)
    }
};
