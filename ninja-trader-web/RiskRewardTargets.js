/**
 * Régua de risco com alvos ligados em 1R, 2R e 3R.
 * Primeiro clique: entrada. Segundo clique: stop por padrão, ou o alvo
 * selecionado em Second Anchor Controls. Arraste uma alça para recalcular
 * todas as linhas. O tick padrão é 0,25 (NQ/MNQ).
 */
const predef = require("./tools/predef");
const { du } = require("./tools/graphics");
const spec = predef.paramSpecs;

const drawing = {
  render({ anchors, props }) {
    const [entry, control] = anchors;
    if (!entry || !control) return { items: [] };
    const items = [];
    const tick = props.tickSize;
    const ticks = (point) => Math.round(point.y.value / tick);
    const entryTicks = ticks(entry);
    const factor = { stop: -1, t1: 1, t2: 2, t3: 3 }[
      props.secondAnchorControls
    ];
    const rawRisk = (ticks(control) - entryTicks) / factor;
    const negative = (control.y.value - entry.y.value) / factor < 0;
    const risk =
      (negative ? -1 : 1) * Math.max(1, Math.round(Math.abs(rawRisk)));
    const left = Math.min(entry.x.value, control.x.value);
    const right = Math.max(
      left + props.minimumWidthBars,
      entry.x.value,
      control.x.value
    );
    function level(key, value, color, suffix) {
      const price = Number((value * tick).toFixed(8));
      const a = { x: du(left), y: du(price) },
        b = { x: du(right), y: du(price) };
      items.push({
        tag: "LineSegments",
        key,
        lines: [{ tag: "Line", a, b }],
        lineStyle: { color, lineWidth: 2 }
      });
      if (props.showLabels)
        items.push({
          tag: "Text",
          key: key + "Label",
          point: b,
          text: key + " " + price + suffix,
          textAlignment: "rightAbove",
          style: { fill: color, fontSize: 12 }
        });
    }
    level("Entrada", entryTicks, props.entryColor, "");
    level(
      "Stop",
      entryTicks - risk,
      props.stopColor,
      " (" + Math.abs(risk) + "t)"
    );
    for (let i = 1; i <= props.targetsToShow; i++) {
      const value = entryTicks + i * risk;
      const delta = value - entryTicks;
      const multiple = i + "R";
      level(
        "T" + i,
        value,
        props.targetColor,
        " (" + Math.abs(delta) + "t, " + multiple + ")"
      );
    }
    return { items };
  }
};

module.exports = {
  name: "RiskRewardTargets",
  description: "Risk Reward Targets (1R / 2R / 3R)",
  drawing,
  minN: 2,
  maxN: 2,
  tags: ["LRM"],
  params: {
    secondAnchorControls: spec.enum(
      { stop: "Stop", t1: "Target 1", t2: "Target 2", t3: "Target 3" },
      "stop"
    ),
    targetsToShow: spec.number(3, 1, 1, 3),
    tickSize: spec.number(0.25, 0.01, 0.00000001),
    minimumWidthBars: spec.period(8),
    showLabels: spec.bool(true),
    entryColor: spec.color("#DAA520"),
    stopColor: spec.color("#DC143C"),
    targetColor: spec.color("#2E8B57")
  }
};
