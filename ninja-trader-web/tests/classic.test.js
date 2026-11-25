// Run with node --test. Exercise Classic's per-bar and repeated-live-bar callbacks.
const { test } = require("node:test");
const assert = require("node:assert/strict");
const vm = require("node:vm");
const fs = require("node:fs");
const path = require("node:path");

const du = value => ({ value, unit: "du" });
const px = value => ({ value, unit: "px" });
function load(name) {
    const source = fs.readFileSync(path.join(__dirname, "..", name + ".js"), "utf8");
    const mod = { exports: {} };
    const identity = v => v;
    const spec = { bool: identity, number: identity, color: identity, period: identity,
        percent: identity, enum: (_, value) => value };
    vm.runInNewContext(source, { module: mod, require: id => {
        if (id === "./tools/predef") return { paramSpecs: spec };
        if (id === "./tools/graphics") return { du, px };
        if (id === "./LRMRectangle") return load("LRMRectangle");
        throw Error(id);
    } });
    return mod.exports;
}
function instance(name, settings, step = 1) {
    const plugin = load(name), calc = new plugin.calculator();
    calc.props = { ...plugin.params, ...settings };
    calc.contractInfo = { product: "MNQ", tickSize: 0.25 };
    calc.chartDescription = { underlyingType: "MinuteBar", elementSizeUnit: "UnderlyingUnits", elementSize: step };
    calc.init();
    return calc;
}
function bar(x, iso, o, h, l, c, last = true) {
    return { index: () => x, timestamp: () => new Date(iso), open: () => o,
        high: () => h, low: () => l, close: () => c, isLast: () => last };
}
const items = (calc, d) => calc.map(d).graphics.items[0].children;
const labels = list => list.filter(item => item.tag === "Text").map(item => item.text).join(" | ");

test("DST, overnight liquidity, live update and first post-session touch", () => {
    for (const [evening, morning] of [["2026-11-22T", "2026-11-23T"], ["2026-06-07T", "2026-06-08T"]]) {
        const winter = evening.includes("11-22");
        const calc = instance("LRMMap", { showCurrentDayHighLow: true, showAsiaOpen: true, showWeeklyOpeningGaps: true });
        items(calc, bar(0, evening + (winter ? "23:00Z" : "22:00Z"), 95, 96, 94, 95));
        const asia = bar(1, morning + (winter ? "01:00Z" : "00:00Z"), 100, 102, 98, 101);
        items(calc, asia);
        items(calc, asia);
        const after = items(calc, bar(2, morning + (winter ? "05:00Z" : "04:00Z"), 100, 101, 99, 100));
        assert.match(labels(after), /Asia High/);
        assert.match(labels(after), /Current DH/);
        items(calc, bar(3, morning + (winter ? "14:30Z" : "13:30Z"), 100, 103, 99, 102));
        const last = items(calc, bar(4, morning + (winter ? "14:31Z" : "13:31Z"), 102, 104, 101, 103));
        const line = last.find(i => i.key === "Asia1H").lines[0];
        assert.equal(line.a.x.value, 1);
        assert.equal(line.b.x.value, 3);
        assert.equal(calc.days[0].opens.filter(o => o.label === "20:00").length, 1);
        assert.match(labels(last), /09:30/);
    }
});

test("first partial period is omitted, Sunday gap uses Friday close, zero history drops Friday", () => {
    const calc = instance("LRMMap", { showCurrentDayHighLow: true, showAsiaOpen: true, showWeeklyOpeningGaps: true });
    const friday = items(calc, bar(0, "2026-11-20T21:59Z", 99, 101, 98, 100));
    assert.doesNotMatch(labels(friday), /Current DH/);
    const sunday = items(calc, bar(1, "2026-11-22T23:00Z", 105, 110, 102, 108));
    assert.match(labels(sunday), /NWOG O 22\/11/);
    assert.match(labels(sunday), /NWOG C 20\/11/);
    assert.equal(sunday.find(i => i.key === "NWOG1CE").lines[0].a.y.value, 102.5);
    assert.doesNotMatch(labels(sunday), /PDH/);
});

test("15m fractional MOC marker and independent label controls", () => {
    const calc = instance("LRMMap", { useLiquidityOnlyOn15MinuteChart: false, showMocMarkers: true, showMarkerLabels: false, showOpeningLabels: false, showLiquidityLabels: false }, 15);
    const result = items(calc, bar(10, "2026-11-23T20:45Z", 100, 101, 99, 100));
    assert.equal(labels(result), "");
    const marker = result.find(i => i.key.startsWith("markerMOC"));
    assert.equal(marker.lines[0].a.x.value, 10 + 5 / 15);
});

test("ATR uses prior closed bar on repeated ticks; MNQ and NQ point values differ", () => {
    for (const [product, expected] of [["MNQ", 16], ["NQ", 1]]) {
        const calc = instance("LRMCountdown", { showCountdown: false });
        calc.contractInfo.product = product;
        items(calc, bar(0, "2026-11-23T14:30Z", 100, 101, 99, 100));
        const next = bar(1, "2026-11-23T14:31Z", 100, 101, 99, 101);
        items(calc, next);
        const result = items(calc, next);
        assert.equal(result.find(i => i.key === "value0").text, String(expected));
        assert.equal(result.find(i => i.key === "value1").text, "2.00");
        items(calc, bar(1, "2026-11-23T14:31Z", 100, 104, 99, 103));
        assert.equal(calc.atr, 3);
    }
});

test("three-point projection follows the signed impulse; rectangles follow both corners", () => {
    const fib = load("LRMFibProjection"), rect = load("LRMRectangle");
    for (const sign of [1, -1]) {
        const anchors = [0, 100, 40].map((v, i) => ({ x: du(i), y: du(200 + sign * v) }));
        const result = fib.drawing.render({ anchors, props: fib.params }).items;
        assert.equal(result.find(i => i.key === "target0").lines[0].a.y.value, 200 + sign * 116.4);
        assert.equal(result.find(i => i.key === "target1").lines[0].a.y.value, 200 + sign * 140);
        const box = rect.drawing.render({ anchors, props: { ...rect.params, colorPreset: "orangeZone" } }).items;
        assert.equal(box[2].lines[0].a.y.value, 200 + sign * 50);
        assert.equal(box[2].lineStyle.color, "#F19D38");
    }
});

test("1m and 15m overnight, daily, weekly and monthly extremes match in the same loaded sessions", () => {
    const settings = { showCurrentDayHighLow: true, showCurrentWeekHighLow: true,
        showCurrentMonthHighLow: true, showCurrentMondayHighLow: true };
    const minute = instance("LRMMap", settings), quarter = instance("LRMMap", settings, 15);
    const start = Date.parse("2026-11-01T23:00:00Z");
    for (const calc of [minute, quarter])
        calc.map(bar(-1, "2026-10-30T20:45:00Z", 100, 101, 99, 100, false));
    let m = 0, q = 0, a, b;
    for (let day = 0; day < 2; day++) {
        for (let offset = 0; offset < 23 * 60; offset += 15) {
            const block = Array.from({length: 15}, (_, n) => {
                const index = day * 1380 + offset + n;
                const price = 200 + (index * 17 % 97);
                return { time: start + day * 86400000 + (offset + n) * 60000,
                    open: price, high: price + 3, low: price - 1, close: price + 1 };
            });
            const last = block[14];
            for (const v of block) {
                const r = minute.map(bar(m++, v.time, v.open, v.high, v.low, v.close, offset === 1365 && v === last));
                if (r) a = r.graphics.items[0].children;
            }
            const r = quarter.map(bar(q++, block[0].time, block[0].open, Math.max(...block.map(v => v.high)),
                Math.min(...block.map(v => v.low)), last.close, offset === 1365));
            if (r) b = r.graphics.items[0].children;
        }
    }
    const levels = list => list.filter(i => i.tag === "LineSegments" && /^(Day|Week|Month|Monday|Asia|London|NyAm|NyPm)\d+[HL]$/.test(i.key))
        .map(i => i.key.replace(/\d+/, "") + "-" + i.lines[0].a.y.value).sort().join("/");
    assert.ok(levels(a).includes("DayH"));
    assert.ok(levels(a).includes("WeekH"));
    assert.ok(levels(a).includes("MonthH"));
    assert.equal(levels(a), levels(b));
    assert.equal(labels(a).includes("PDH"), true);
});

test("five named rectangles retain every fractional line when dragged in either direction", () => {
    for (const name of ["Orange", "Cyan", "Red", "Purple", "Gray"]) {
        const tool = load("LRMRectangle" + name);
        assert.equal(tool.name, "LRMRectangle" + name);
        const colorPreset = tool.params.colorPreset.def;
        assert.equal(colorPreset, name.toLowerCase() + "Zone");
        for (const [a,b] of [[100,180],[180,100]]) {
            const anchors = [{ x: du(1), y: du(a) }, { x: du(5), y: du(b) }];
            for (const divisions of [1,2,4,8]) {
                const props = { ...tool.params, colorPreset, showMidline: divisions >= 2,
                    divideIntoQuarters: divisions >= 4, divideIntoEighths: divisions === 8 };
                const lines = tool.drawing.render({anchors, props}).items.slice(2);
                assert.equal(lines.length, divisions - 1);
                lines.forEach((item, index) => {
                    const line = item.lines[0];
                    assert.equal(line.a.y.value, a + (b-a) * (index+1) / divisions);
                    assert.equal(line.b.y.value, line.a.y.value);
                    assert.equal(line.a.x.value, 1);
                    assert.equal(line.b.x.value, 5);
                });
            }
        }
    }
});
