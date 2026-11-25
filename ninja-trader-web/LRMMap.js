// NQ/MNQ intraday map. Dates are New York calendar dates; the CME trading day rolls at 18:00.
const predef = require("./tools/predef");
const { du, px } = require("./tools/graphics");
const spec = predef.paramSpecs;
const MINUTE = 60000;
const DAY = 86400000;
const ny = new Intl.DateTimeFormat("en-US", {
    timeZone: "America/New_York", year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", minute: "2-digit", hourCycle: "h23"
});

function clock(date) {
    const parts = {};
    for (const part of ny.formatToParts(date)) parts[part.type] = Number(part.value);
    const minute = (parts.hour % 24) * 60 + parts.minute;
    const calendar = Date.UTC(parts.year, parts.month - 1, parts.day) / DAY;
    return { minute, calendar, day: calendar + Number(minute >= 1080) };
}

const periods = [
    ["Day", "D", 3], ["Week", "W", 1], ["Month", "M", 1], ["Monday", "Mon", 1]
];
const windows = [
    ["Asia", 2000, 0, "#2962ff"], ["London", 200, 500, "#9c27b0"],
    ["Pre", 800, 930, "#ff9800"], ["NyAm", 930, 1100, "#089981"],
    ["NyPm", 1330, 1600, "#f23645"]
];
const events = [
    ["Midnight", 0, "Midnight", "#000000"], ["London", 200, "London", "#9c27b0"],
    ["News", 830, "8:30 Open", "#ff9800"], ["Equity", 930, "Equity Open", "#089981"],
    ["Pm", 1330, "NY PM", "#f23645"], ["Asia", 2000, "Asia", "#2962ff"]
];
function minutes(hhmm) {
    const h = Math.floor(hhmm / 100), m = hhmm % 100;
    return h < 24 && m < 60 ? h * 60 + m : NaN;
}
function hhmm(m) {
    return String(Math.floor(m / 60)).padStart(2, "0") + ":" + String(m % 60).padStart(2, "0");
}
function dayLabel(day, monthFirst) {
    const date = new Date(day * DAY);
    const d = String(date.getUTCDate()).padStart(2, "0"), m = String(date.getUTCMonth() + 1).padStart(2, "0");
    return monthFirst ? m + "/" + d : d + "/" + m;
}
function point(x, y) { return { x: du(x), y: du(y) }; }
function segment(key, a, b, color, style, vertical) {
    return { tag: "LineSegments", key, lineStyle: { color, lineWidth: 1, lineStyle: style },
        lines: [{ tag: "Line", a, b, infiniteStart: !!vertical, infiniteEnd: !!vertical }] };
}
function text(key, x, y, label, color, alignment) {
    return { tag: "Text", key, point: point(x, y), text: label, textAlignment: alignment,
        style: { fill: color, fontSize: 11 } };
}

function bucket() { return { active: null, finished: [] }; }
function track(group, id, startKnown, bar, keep) {
    let r = group.active;
    if (r && r.id !== id) {
        if (r.complete) group.finished.push(r);
        while (group.finished.length > keep) group.finished.shift();
        group.active = null;
    }
    for (const old of group.finished) {
        if (old.hitH == null && bar.h >= old.h) old.hitH = bar.x;
        if (old.hitL == null && bar.l <= old.l) old.hitL = bar.x;
    }
    if (id == null) return;
    if (!group.active) {
        group.active = { id, complete: startKnown, first: bar.x, firstDay: bar.day,
            h: bar.h, hx: bar.x, l: bar.l, lx: bar.x };
    }
    r = group.active;
    if (bar.h > r.h) { r.h = bar.h; r.hx = bar.x; }
    if (bar.l < r.l) { r.l = bar.l; r.lx = bar.x; }
    r.last = bar.x;
    r.lastDay = bar.day;
}

class LRMMap {
    init() {
        this.groups = {};
        for (const [name] of periods.concat(windows)) this.groups[name] = bucket();
        this.days = [];
        this.gaps = [];
        this.previous = null;
        this.lastTime = null;
    }

    map(d) {
        const time = d.timestamp().getTime();
        if (this.lastTime != null && time < this.lastTime) this.init();
        const fresh = time !== this.lastTime;
        if (fresh) this.now = clock(d.timestamp());
        const { day, minute, calendar } = this.now;
        const x = d.index();
        const bar = { x, day, h: d.high(), l: d.low() };
        const weekday = new Date(day * DAY).getUTCDay();
        const week = day - (weekday + 6) % 7;
        const month = new Date(day * DAY).toISOString().slice(0, 7);
        const p = this.props;
        if (fresh) {
            this.previous = this.lastBar;
            this.lastTime = time;
        }
        const prev = this.previous;
        const startOfDay = minute === 1080;
        const ids = [day, week, month, weekday === 1 ? day : null];
        const starts = [startOfDay, startOfDay && weekday === 1,
            startOfDay && new Date(day * DAY).getUTCDate() === 1, startOfDay];
        periods.forEach(([name], i) => {
            if (p["show" + name + "Liquidity"]) {
                track(this.groups[name], ids[i], !!prev || starts[i], bar, p["previous" + name + "Periods"]);
            }
        });
        for (const [name] of windows) {
            if (!p["show" + name + "Session"]) continue;
            const start = minutes(p[name + "StartNY"]), end = minutes(p[name + "EndNY"]);
            const inside = Number.isFinite(start) && Number.isFinite(end) &&
                (start < end ? minute >= start && minute < end : minute >= start || minute < end);
            const id = inside ? calendar - Number(minute < start) : null;
            track(this.groups[name], id, minute === start, bar, p.previousSessionsPerWindow + 1);
        }
        let today = this.days[this.days.length - 1];
        if (!today || today.day !== day) {
            today = { day, opens: [], markers: [], last: x };
            this.days.push(today);
            const retain = Math.max(p.pastTradingDaysForOpens, p.pastTradingDaysForMarkers,
                p.limitLiquidityHistory ? p.pastTradingDaysForLiquidity : 0) + 1;
            if (this.days.length > retain) this.days.shift();
        }
        today.last = x;
        const step = this.chartDescription.elementSize;
        const minuteChart = this.chartDescription.underlyingType === "MinuteBar" &&
            this.chartDescription.elementSizeUnit === "UnderlyingUnits";
        if (fresh) {
            const recordMarker = (m, label, color) => {
                if (minute === m || (minuteChart && minute < m && minute + step > m)) {
                    today.markers.push({ x: x + (m - minute) / step, label, color });
                }
            };
            for (const [name, , label] of events) {
                const m = minutes(p[name + "TimeNY"]);
                if (p["show" + name + "Open"] && minute === m)
                    today.opens.push({ x, price: d.open(), label: hhmm(m) });
                if (p["show" + name + "Marker"]) recordMarker(m, label, p[name + "MarkerColor"]);
            }
            if (p.showMocMarkers) {
                recordMarker(950, "MOC", p.mocColor);
                recordMarker(960, "Equity Close", p.mocColor);
            }
            // Only use a close from the preceding session's final loaded minute bar.
            if (startOfDay && prev && minuteChart && prev.minute + step === 1020 &&
                (prev.calendar === calendar || (weekday === 1 && calendar - prev.calendar === 2)) &&
                d.open() !== prev.close) {
                const weekly = weekday === 1;
                const kind = weekly ? "NWOG" : "NDOG";
                this.gaps.push({ x, kind, openDay: calendar, closeDay: prev.calendar, open: d.open(), close: prev.close });
                const keep = weekly ? p.weeklyGapsToShow : p.dailyGapsToShow;
                const same = this.gaps.filter(g => g.kind === kind);
                if (same.length > keep) this.gaps.splice(this.gaps.indexOf(same[0]), 1);
            }
        }
        this.lastBar = { minute, calendar, close: d.close() };
        if (!d.isLast()) return;
        return this.draw(bar, step, minuteChart);
    }

    draw(bar, step, minuteChart) {
        const p = this.props, items = [], captions = new Map();
        const cutoffIndex = Math.max(0, this.days.length - 1 - p.pastTradingDaysForLiquidity);
        const cutoff = p.limitLiquidityHistory ? this.days[cutoffIndex].day : -Infinity;
        const level = (key, start, end, price, label, color, high, showLabel, style = 1) => {
            items.push(segment(key, point(start, price), point(Math.max(start + 0.15, end), price), color, style));
            if (showLabel) {
                const id = [end, price, high].join("/");
                if (captions.has(id)) captions.get(id).text += " / " + label;
                else captions.set(id, text(key + "-label", end, price, label, color, high ? "rightAbove" : "rightBelow"));
            }
        };
        const range = (name, r, prefix, suffix, color, session) => {
            const id = name + r.first;
            level(id + "H", r.hx, r.hitH == null ? bar.x : r.hitH, r.h,
                prefix + (session ? " High" : "H") + suffix, color, true, p.showLiquidityLabels);
            level(id + "L", r.lx, r.hitL == null ? bar.x : r.hitL, r.l,
                prefix + (session ? " Low" : "L") + suffix, color, false, p.showLiquidityLabels);
        };
        for (const [name, prefix] of periods) {
            const g = this.groups[name], color = p[name + "LevelColor"];
            g.finished.forEach((r, i) => {
                if (r.lastDay < cutoff) return;
                const latest = i === g.finished.length - 1;
                range(name, r, latest && name !== "Monday" ? "P" + prefix : prefix,
                    latest ? "" : " " + dayLabel(r.firstDay, p.monthFirstDates), color, false);
            });
            if (p["showCurrent" + name + "HighLow"] && g.active && g.active.complete)
                range(name, g.active, "Current " + prefix, "", color, false);
        }
        for (const [name] of windows) {
            const g = this.groups[name], color = p[name + "SessionColor"];
            const all = g.finished.concat(g.active ? [g.active] : []).slice(-(p.previousSessionsPerWindow + 1));
            for (const r of all) {
                if (!r.complete || r.lastDay < cutoff || (r === g.active && !p.showCurrentSessionHighLow)) continue;
                const label = { NyAm: "NY AM", NyPm: "NY PM" }[name] || name;
                if (p.drawSessionBoxes) {
                    const points = [point(r.first, r.h), point(r.last + 1, r.h), point(r.last + 1, r.l), point(r.first, r.l)];
                    items.push({ tag: "Shapes", key: "box" + name + r.first, primitives: [{tag:"Polygon", points}],
                        fillStyle: { color, opacity: p.sessionBoxOpacityPercent / 100 } });
                }
                range(name, r, label, "", color, true);
            }
        }
        for (const day of this.days.slice(-(p.pastTradingDaysForOpens + 1))) {
            const remaining = (1020 - this.now.minute + 1440) % 1440;
            const end = day.day === bar.day && minuteChart ? bar.x + Math.min(15, remaining / step) : day.last;
            day.opens.forEach((o, i) => level("open" + day.day + "-" + i, o.x, end, o.price,
                o.label, p.openLineColor, true, p.showOpeningLabels, 3));
        }
        for (const g of this.gaps) {
            if (g.kind === "NWOG" ? !p.showWeeklyOpeningGaps : !p.showDailyOpeningGaps) continue;
            const color = g.kind === "NWOG" ? p.weeklyGapColor : p.dailyGapColor;
            const end = bar.x + 15;
            for (const [tag, price] of [["O", g.open], ["C", g.close], ["CE", (g.open + g.close) / 2]])
                level(g.kind + g.x + tag, g.x, end, price, g.kind + " " + tag + " " +
                    dayLabel(tag === "C" ? g.closeDay : g.openDay, p.monthFirstDates), color, true, p.showOpeningLabels, tag === "CE" ? 3 : 1);
        }
        const drawMarker = (m, future) => {
            items.push(segment("marker" + m.label + m.x, point(m.x, bar.l), point(m.x, bar.h + this.contractInfo.tickSize), m.color, 3, true));
            if (p.showMarkerLabels) {
                const item = text("markerLabel" + m.label + m.x, m.x, bar.l, m.label + (future || ""), m.color, "leftAbove");
                item.point.y = px(24);
                item.origin = { cs: "grid", h: "left", v: "bottom" };
                items.push(item);
            }
        };
        for (const day of this.days.slice(-(p.pastTradingDaysForMarkers + 1)))
            day.markers.forEach(m => drawMarker(m, ""));
        if (minuteChart && p.showUpcomingMarkers) {
            const now = (this.now.minute + 360) % 1440;
            const upcoming = (m, label, color) => {
                const delta = (m + 360) % 1440 - now;
                if (delta <= 0 || delta < step) return;
                drawMarker({ x: bar.x + delta / step, label, color }, " (" + delta + "m)");
            };
            for (const [name, , label] of events)
                if (p["show" + name + "Marker"]) upcoming(minutes(p[name + "TimeNY"]), label, p[name + "MarkerColor"]);
            if (p.showMocMarkers) { upcoming(950, "MOC", p.mocColor); upcoming(960, "Equity Close", p.mocColor); }
        }
        items.push(...captions.values());
        return { graphics: { items: [{ tag: "Container", key: "lrmMap", global: true, children: items }] } };
    }
}

const params = {
    showLiquidityLabels: spec.bool(true), showOpeningLabels: spec.bool(true), showMarkerLabels: spec.bool(true),
    monthFirstDates: spec.bool(false), limitLiquidityHistory: spec.bool(false),
    pastTradingDaysForLiquidity: spec.number(3, 1, 0), previousSessionsPerWindow: spec.number(3, 1, 0),
    showCurrentSessionHighLow: spec.bool(true), drawSessionBoxes: spec.bool(false),
    sessionBoxOpacityPercent: spec.percent(10, 1, 0, 100),
    pastTradingDaysForOpens: spec.number(0, 1, 0), openLineColor: spec.color("#000000"),
    showWeeklyOpeningGaps: spec.bool(true), weeklyGapsToShow: spec.number(4, 1, 0), weeklyGapColor: spec.color("#000000"),
    showDailyOpeningGaps: spec.bool(false), dailyGapsToShow: spec.number(5, 1, 0), dailyGapColor: spec.color("#787b86"),
    pastTradingDaysForMarkers: spec.number(3, 1, 0), showUpcomingMarkers: spec.bool(true),
    showMocMarkers: spec.bool(true), mocColor: spec.color("#f23645")
};
for (const [name, , count] of periods) {
    params["show" + name + "Liquidity"] = spec.bool(true);
    params["showCurrent" + name + "HighLow"] = spec.bool(false);
    params["previous" + name + "Periods"] = spec.number(count, 1, 0);
    params[name + "LevelColor"] = spec.color("#455a64");
}
for (const [name, start, end, color] of windows) {
    params["show" + name + "Session"] = spec.bool(name !== "Pre");
    params[name + "StartNY"] = spec.number(start, 1, 0);
    params[name + "EndNY"] = spec.number(end, 1, 0);
    params[name + "SessionColor"] = spec.color(color);
}
for (const [name, time, , color] of events) {
    params["show" + name + "Open"] = spec.bool(name !== "London");
    params["show" + name + "Marker"] = spec.bool(name !== "London");
    params[name + "TimeNY"] = spec.number(time, 1, 0);
    params[name + "MarkerColor"] = spec.color(color);
}
module.exports = { name: "LRMMap", description: "LRM Map", calculator: LRMMap, params, tags: ["LRM"], inputType: "bars", plots: {} };
