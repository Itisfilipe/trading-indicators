const predef = require("./tools/predef");
const { px } = require("./tools/graphics");
const spec = predef.paramSpecs;

class LRMCountdown {
    init() {
        this.time = null;
        this.atr = null;
        this.close = null;
    }

    map(d) {
        const time = d.timestamp().getTime();
        if (this.time !== null && time < this.time) this.init();
        if (this.time !== time) {
            this.previousAtr = this.atr;
            this.previousClose = this.close;
            this.time = time;
        }
        const p = this.props;
        const previous = this.previousClose == null ? d.close() : this.previousClose;
        const tr = Math.max(d.high() - d.low(), Math.abs(d.high() - previous), Math.abs(d.low() - previous));
        this.atr = this.previousAtr == null ? tr : this.previousAtr + 2 / (p.atrLength + 1) * (tr - this.previousAtr);
        this.close = d.close();
        if (!d.isLast()) return;

        const contract = this.contractInfo;
        const autoValue = { MNQ: 2, NQ: 20 }[contract.product];
        const pointValue = p.dollarsPerPointOverride || autoValue;
        const stop = Math.max(1, Math.round(p.stopAtrMultiple * this.atr / contract.tickSize)) * contract.tickSize;
        const chart = this.chartDescription;
        const timed = chart.underlyingType === "MinuteBar" && chart.elementSizeUnit === "UnderlyingUnits";
        const values = [];
        if (p.showCountdown && timed) {
            const duration = chart.elementSize * 60000;
            const now = Date.now();
            const live = time <= now && now < time + duration;
            const seconds = Math.max(0, Math.floor((time + duration - now) / 1000));
            const clock = live ? Math.floor(seconds / 60) + ":" + String(seconds % 60).padStart(2, "0") : "–";
            values.push([clock, live && seconds <= p.alertSeconds ? p.alertColor : p.panelColor]);
        }
        if (p.showContractCount) values.push([pointValue ? String(Math.floor(p.riskDollars / (stop * pointValue))) : "–", p.panelColor]);
        if (p.showAtr) values.push([this.atr.toFixed(2), p.panelColor]);
        const children = [];
        let offset = 8;
        for (let i = values.length - 1; i >= 0; i--) {
            const [value, color] = values[i];
            const width = Math.max(36, value.length * p.fontSize * 0.66 + 14);
            const height = p.fontSize + 14;
            const point = (x, y) => ({ x: px(x), y: px(y) });
            children.push({ tag: "Shapes", key: "cell" + i, fillStyle: { color },
                primitives: [{ tag: "Polygon", points: [point(offset, 8), point(offset + width, 8),
                    point(offset + width, 8 + height), point(offset, 8 + height)] }] });
            children.push({ tag: "Text", key: "value" + i, text: value, textAlignment: "centerMiddle",
                point: point(offset + width / 2, 8 + height / 2), style: { fill: p.textColor, fontSize: p.fontSize } });
            offset += width + 1;
        }
        return { graphics: { items: [{ tag: "Container", key: "lrmPanel", global: true,
            origin: { cs: "frame", h: "right", v: "top" }, children }] } };
    }
}

module.exports = {
    name: "LRMCountdown", description: "LRM Countdown", calculator: LRMCountdown,
    inputType: "bars", plots: {}, tags: ["LRM"],
    params: {
        showCountdown: spec.bool(true), showContractCount: spec.bool(true), showAtr: spec.bool(true),
        riskDollars: spec.number(100, 1, 0), stopAtrMultiple: spec.number(1.5, 0.1, 0.1),
        atrLength: spec.period(5), dollarsPerPointOverride: spec.number(0, 0.01, 0),
        fontSize: spec.period(20), alertSeconds: spec.number(5, 1, 0),
        panelColor: spec.color("#3B4352"), textColor: spec.color("#FFFFFF"), alertColor: spec.color("#F23645")
    }
};
