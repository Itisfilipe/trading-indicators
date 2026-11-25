const predef = require("./tools/predef");
const { du } = require("./tools/graphics");
const spec = predef.paramSpecs;

const projection = {
    render({ anchors, props }) {
        const [a, b, c] = anchors;
        if (!a || !b) return { items: [] };
        const legs = [{ tag: "Line", a, b }];
        if (c) legs.push({ tag: "Line", a: b, b: c });
        const items = props.showAnchorLegs ? [{ tag: "LineSegments", key: "legs", lines: legs,
            lineStyle: { color: props.legColor, lineWidth: 1, lineStyle: 3 } }] : [];
        if (!c) return { items };
        const move = b.y.value - a.y.value;
        const start = c.x.value, end = start + props.projectionWidthBars;
        const price = percent => du(c.y.value + move * percent / 100);
        const near = price(props.firstTargetPercent), far = price(props.secondTargetPercent);
        const corner = (x, y) => ({ x: du(x), y });
        const color = props.targetColor;
        items.push({ tag: "Shapes", key: "targetZone", fillStyle: { color, opacity: props.fillOpacityPercent },
            primitives: [{ tag: "Polygon", points: [corner(start, near), corner(end, near), corner(end, far), corner(start, far)] }] });
        for (const [index, percent] of [props.firstTargetPercent, props.secondTargetPercent].entries()) {
            const y = price(percent);
            items.push({ tag: "LineSegments", key: "target" + index, lineStyle: { color, lineWidth: 1 },
                lines: [{ tag: "Line", a: corner(start, y), b: corner(end, y) }] });
            if (props.showLabels) items.push({ tag: "Text", key: "label" + index, point: corner(end, y),
                text: percent + "%", textAlignment: "rightAbove", style: { fill: color, fontSize: 11 } });
        }
        return { items };
    }
};

module.exports = {
    name: "LRMFibProjection", description: "LRM Fib Projection", drawing: projection, minN: 3, maxN: 3, tags: ["LRM"],
    params: {
        firstTargetPercent: spec.number(76.4, 0.1, 0), secondTargetPercent: spec.number(100, 0.1, 0),
        projectionWidthBars: spec.period(30), showAnchorLegs: spec.bool(true), showLabels: spec.bool(true),
        legColor: spec.color("#616161"), targetColor: spec.color("#DF484C"), fillOpacityPercent: spec.percent(10, 1, 0, 100)
    }
};
