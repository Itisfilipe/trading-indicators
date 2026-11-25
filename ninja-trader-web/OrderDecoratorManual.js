/**
 * Mede uma saída planejada usando preço médio e preço de saída marcados à mão.
 * Primeiro clique: referência de entrada. Segundo: STOP ou ALVO.
 * Informe o lado e a quantidade restante. O valor por ponto começa em US$ 2
 * para MNQ. Para NQ, use US$ 20. A ferramenta não lê a conta nem envia ordens.
 */
const predef = require("./tools/predef");
const { du } = require("./tools/graphics");
const spec = predef.paramSpecs;

module.exports = {
  name: "OrderDecoratorManual",
  description: "Order Decorator (manual plan)",
  tags: ["LRM"],
  minN: 2,
  maxN: 2,
  params: {
    positionSide: spec.enum({ long: "Long", short: "Short" }, "long"),
    exitType: spec.enum({ stop: "STOP", target: "ALVO" }, "stop"),
    remainingContracts: spec.period(1),
    dollarsPerPointPerContract: spec.number(2, 0.01, 0.01),
    tickSize: spec.number(0.25, 0.01, 0.00000001),
    showLabels: spec.bool(true),
    showPointsTimesContracts: spec.bool(true),
    showMoney: spec.bool(true),
    showTicks: spec.bool(false)
  },
  drawing: {
    render({ anchors, props }) {
      const entry = anchors[0],
        exit = anchors[1];
      if (!entry || !exit) return { items: [] };
      const tick = props.tickSize;
      const contracts = props.remainingContracts;
      const exitTicks = Math.round(exit.y.value / tick);
      const ticks =
        (exitTicks - Math.round(entry.y.value / tick)) *
        (props.positionSide === "short" ? -1 : 1);
      const contractPoints = ticks * tick * contracts;
      const fields = [
        "Plano",
        props.exitType === "stop" ? "STOP" : "ALVO",
        String(contracts) + " ct"
      ];
      if (props.showPointsTimesContracts)
        fields.push(Number(contractPoints.toFixed(8)) + " pt·ct");
      if (props.showMoney)
        fields.push(
          "US$ " +
            (contractPoints * props.dollarsPerPointPerContract).toFixed(2)
        );
      if (props.showTicks) fields.push(ticks + "t/ct");
      const color = ticks < 0 ? "#800000" : ticks > 0 ? "#006400" : "#5F6773";
      const y = du(Number((exitTicks * tick).toFixed(8)));
      const a = { x: entry.x, y },
        b = { x: exit.x, y };
      const items = [
        {
          tag: "LineSegments",
          key: "exit",
          lines: [{ tag: "Line", a, b }],
          lineStyle: { color, lineWidth: 2 }
        }
      ];
      if (props.showLabels)
        items.push({
          tag: "Text",
          key: "label",
          point: b,
          text: fields.join("  ·  "),
          textAlignment: "rightAbove",
          style: { fill: color, fontSize: 12 }
        });
      return { items };
    }
  }
};
