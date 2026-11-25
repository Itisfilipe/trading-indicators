/**
 * Agenda de divulgações econômicas para o gráfico do Classic.
 * A tabela acompanha a última barra em horário de Nova York. Em gráficos de
 * minutos, cada losango marca a barra que contém uma ou mais divulgações.
 * Começa filtrada em USD. Rode tools/update_calendar.py e reimporte este
 * arquivo para atualizar os eventos da semana.
 */
const predef = require("./tools/predef");
const { px, du } = require("./tools/graphics");
const spec = predef.paramSpecs;
// BEGIN WEEKLY SNAPSHOT
const snapshotStart = "2026-11-22";
const snapshotEnd = "2026-11-28";
// prettier-ignore
const releases = [
  ["2026-11-22T16:45:00-05:00", "NZD", "Low", "Core Retail Sales q/q"],
  ["2026-11-22T16:45:00-05:00", "NZD", "Low", "Retail Sales q/q"],
  ["2026-11-22T17:00:00-05:00", "AUD", "Low", "Flash Manufacturing PMI"],
  ["2026-11-22T17:00:00-05:00", "AUD", "Low", "Flash Services PMI"],
  ["2026-11-22T19:00:00-05:00", "JPY", "Holiday", "Bank Holiday"],
  ["2026-11-23T03:15:00-05:00", "EUR", "Medium", "French Flash Manufacturing PMI"],
  ["2026-11-23T03:15:00-05:00", "EUR", "Medium", "French Flash Services PMI"],
  ["2026-11-23T03:30:00-05:00", "EUR", "Medium", "German Flash Manufacturing PMI"],
  ["2026-11-23T03:30:00-05:00", "EUR", "Medium", "German Flash Services PMI"],
  ["2026-11-23T04:00:00-05:00", "EUR", "Low", "Flash Manufacturing PMI"],
  ["2026-11-23T04:00:00-05:00", "EUR", "Low", "Flash Services PMI"],
  ["2026-11-23T04:30:00-05:00", "GBP", "Medium", "Flash Manufacturing PMI"],
  ["2026-11-23T04:30:00-05:00", "GBP", "Medium", "Flash Services PMI"],
  ["2026-11-23T08:00:00-05:00", "GBP", "Low", "MPC Member Pill Speaks"],
  ["2026-11-23T08:32:00-05:00", "CAD", "Low", "Corporate Profits q/q"],
  ["2026-11-23T09:45:00-05:00", "USD", "Medium", "Flash Manufacturing PMI"],
  ["2026-11-23T09:45:00-05:00", "USD", "Medium", "Flash Services PMI"],
  ["2026-11-23T16:00:00-05:00", "USD", "Low", "FOMC Member Williams Speaks"],
  ["2026-11-23T19:30:00-05:00", "JPY", "Low", "Flash Manufacturing PMI"],
  ["2026-11-24T04:00:00-05:00", "EUR", "Low", "German ifo Business Climate"],
  ["2026-11-24T06:00:00-05:00", "GBP", "Low", "CBI Realized Sales"],
  ["2026-11-24T07:30:00-05:00", "GBP", "Low", "MPC Member Greene Speaks"],
  ["2026-11-24T08:15:00-05:00", "USD", "Low", "ADP Weekly Employment Change"],
  ["2026-11-24T08:51:00-05:00", "CNY", "Low", "CB Leading Index m/m"],
  ["2026-11-24T09:00:00-05:00", "EUR", "Low", "Belgian NBB Business Climate"],
  ["2026-11-24T09:00:00-05:00", "USD", "Low", "HPI m/m"],
  ["2026-11-24T09:00:00-05:00", "USD", "Low", "S&P/CS Composite-20 HPI y/y"],
  ["2026-11-24T10:00:00-05:00", "USD", "Low", "Richmond Manufacturing Index"],
  ["2026-11-24T10:00:00-05:00", "USD", "Medium", "CB Consumer Confidence"],
  ["2026-11-24T12:25:00-05:00", "CAD", "Medium", "BOC Gov Macklem Speaks"],
  ["2026-11-24T16:30:00-05:00", "USD", "Low", "API Weekly Statistical Bulletin"],
  ["2026-11-24T18:50:00-05:00", "JPY", "Low", "SPPI y/y"],
  ["2026-11-24T19:30:00-05:00", "AUD", "High", "CPI m/m"],
  ["2026-11-24T19:30:00-05:00", "AUD", "High", "CPI y/y"],
  ["2026-11-24T19:30:00-05:00", "AUD", "High", "Trimmed Mean CPI m/m"],
  ["2026-11-24T19:30:00-05:00", "AUD", "Low", "Construction Work Done q/q"],
  ["2026-11-25T00:00:00-05:00", "JPY", "Low", "BOJ Core CPI y/y"],
  ["2026-11-25T02:00:00-05:00", "EUR", "Low", "German Final GDP q/q"],
  ["2026-11-25T04:00:00-05:00", "CHF", "Low", "UBS Economic Expectations"],
  ["2026-11-25T05:32:00-05:00", "EUR", "Low", "German 10-y Bond Auction"],
  ["2026-11-25T07:25:00-05:00", "USD", "Low", "FOMC Member Bowman Speaks"],
  ["2026-11-25T08:30:00-05:00", "USD", "High", "Core PCE Price Index m/m"],
  ["2026-11-25T08:30:00-05:00", "USD", "High", "Prelim GDP q/q"],
  ["2026-11-25T08:30:00-05:00", "USD", "Low", "Core Durable Goods Orders m/m"],
  ["2026-11-25T08:30:00-05:00", "USD", "Low", "Durable Goods Orders m/m"],
  ["2026-11-25T08:30:00-05:00", "USD", "Low", "Personal Income m/m"],
  ["2026-11-25T08:30:00-05:00", "USD", "Low", "Personal Spending m/m"],
  ["2026-11-25T08:30:00-05:00", "USD", "Low", "Unemployment Claims"],
  ["2026-11-25T08:30:00-05:00", "USD", "Medium", "Prelim GDP Price Index q/q"],
  ["2026-11-25T09:15:00-05:00", "GBP", "Medium", "Monetary Policy Report Hearings"],
  ["2026-11-25T10:00:00-05:00", "USD", "Low", "New Home Sales"],
  ["2026-11-25T10:30:00-05:00", "USD", "Low", "Crude Oil Inventories"],
  ["2026-11-25T12:00:00-05:00", "USD", "Low", "Natural Gas Storage"],
  ["2026-11-25T14:00:00-05:00", "USD", "Low", "Beige Book"],
  ["2026-11-25T19:30:00-05:00", "AUD", "Low", "Private Capital Expenditure q/q"],
  ["2026-11-25T19:30:00-05:00", "AUD", "Low", "RBA Bulletin"],
  ["2026-11-26T02:00:00-05:00", "EUR", "Low", "German GfK Consumer Climate"],
  ["2026-11-26T04:00:00-05:00", "EUR", "Low", "M3 Money Supply y/y"],
  ["2026-11-26T04:00:00-05:00", "EUR", "Low", "Private Loans y/y"],
  ["2026-11-26T05:30:00-05:00", "GBP", "Low", "BOE Financial Stability Report"],
  ["2026-11-26T05:30:00-05:00", "GBP", "Low", "FPC Meeting Minutes"],
  ["2026-11-26T05:30:00-05:00", "GBP", "Low", "FPC Statement"],
  ["2026-11-26T07:30:00-05:00", "EUR", "Low", "ECB Monetary Policy Meeting Accounts"],
  ["2026-11-26T08:00:00-05:00", "USD", "Holiday", "Bank Holiday"],
  ["2026-11-26T18:30:00-05:00", "JPY", "Medium", "Tokyo Core CPI y/y"],
  ["2026-11-27T02:45:00-05:00", "EUR", "Low", "French Consumer Spending m/m"],
  ["2026-11-27T02:45:00-05:00", "EUR", "Low", "French Final Private Payrolls q/q"],
  ["2026-11-27T02:45:00-05:00", "EUR", "Low", "French Prelim CPI m/m"],
  ["2026-11-27T02:45:00-05:00", "EUR", "Low", "French Prelim GDP q/q"],
  ["2026-11-27T03:00:00-05:00", "EUR", "Low", "Spanish Flash CPI y/y"],
  ["2026-11-27T03:55:00-05:00", "EUR", "Low", "German Unemployment Change"],
  ["2026-11-27T05:33:00-05:00", "EUR", "Low", "Italian 10-y Bond Auction"],
  ["2026-11-27T07:40:00-05:00", "EUR", "Low", "German Buba President Nagel Speaks"],
  ["2026-11-27T08:30:00-05:00", "CAD", "Low", "Current Account"],
  ["2026-11-27T08:30:00-05:00", "USD", "Low", "Goods Trade Balance"],
  ["2026-11-27T08:30:00-05:00", "USD", "Low", "Prelim Wholesale Inventories m/m"],
];
// END WEEKLY SNAPSHOT

const nyDay = new Intl.DateTimeFormat("sv-SE", {
  timeZone: "America/New_York",
  year: "numeric",
  month: "2-digit",
  day: "2-digit"
});
const nyClock = new Intl.DateTimeFormat("en-GB", {
  timeZone: "America/New_York",
  hour: "2-digit",
  minute: "2-digit",
  hour12: false
});
const colors = {
  High: "#D32F2F",
  Medium: "#E58800",
  Low: "#59687A",
  Holiday: "#776299"
};

const impactPriority = { High: 0, Medium: 1, Low: 2, Holiday: 3 };

function isVisible(event, settings) {
  return (
    (!settings.onlyUSD || event.currency === "USD") &&
    settings["show" + event.impact]
  );
}

function releaseMarker(bar, events, showNames) {
  const strongest = events.reduce((a, b) =>
    impactPriority[a.impact] <= impactPriority[b.impact] ? a : b
  );
  return {
    tag: "Text",
    key: "release",
    point: { x: du(bar.index()), y: du(bar.high()) },
    textAlignment: "centerAbove",
    text: showNames ? events.map((event) => event.title).join(" / ") : "◆",
    style: { fill: colors[strongest.impact], fontSize: 11 }
  };
}

function dailyTable(day, timestamp, events) {
  const rows = [
    {
      text: "Agenda · " + day + " · NY",
      color: "#FFFFFF",
      background: "#283C70"
    }
  ];
  for (const event of events) {
    rows.push({
      text: [
        nyClock.format(event.time),
        event.currency,
        event.impact,
        event.title
      ].join("  "),
      color: event.time >= timestamp ? colors[event.impact] : "#455A64",
      background: "#DEE1E9"
    });
  }
  if (!events.length) {
    const expired = day < snapshotStart || day > snapshotEnd;
    rows.push({
      text: expired ? "Atualize a semana da agenda" : "Sem eventos no filtro",
      color: "#455A64",
      background: "#DEE1E9"
    });
  }

  const point = (x, y) => ({ x: px(x), y: px(y) });
  const width = Math.max(...rows.map((row) => row.text.length)) * 7.5 + 24;
  const backgrounds = [],
    labels = [];
  rows.forEach((row, index) => {
    const y = 15 + (rows.length - index) * 17;
    const corners = [
      point(5, y - 8.5),
      point(width, y - 8.5),
      point(width, y + 8.5),
      point(5, y + 8.5)
    ];
    backgrounds.push({
      tag: "Shapes",
      key: "background" + index,
      fillStyle: { color: row.background },
      primitives: [{ tag: "Polygon", points: corners }]
    });
    labels.push({
      tag: "Text",
      key: "row" + index,
      text: row.text,
      point: point(12, y),
      textAlignment: "rightMiddle",
      style: { fill: row.color, fontSize: 12 }
    });
  });
  return {
    tag: "Container",
    key: "today",
    global: true,
    origin: { cs: "frame", h: "right", v: "bottom" },
    children: backgrounds.concat(labels)
  };
}

class EconomicCalendar {
  init() {
    this.events = releases.map(([isoTime, currency, impact, title]) => {
      const time = Date.parse(isoTime);
      return { time, day: nyDay.format(time), currency, impact, title };
    });
  }

  map(bar) {
    const start = bar.timestamp().getTime();
    const chart = this.chartDescription;
    const settings = this.props;
    const minuteBars =
      chart.underlyingType === "MinuteBar" &&
      chart.elementSizeUnit === "UnderlyingUnits";
    const items = [];
    if (settings.showReleaseMarkers && minuteBars) {
      const end = start + chart.elementSize * 60000;
      const hits = this.events.filter(
        (event) =>
          event.time >= start && event.time < end && isVisible(event, settings)
      );
      if (hits.length)
        items.push(releaseMarker(bar, hits, settings.showMarkerNames));
    }
    if (bar.isLast() && settings.showTodayTable) {
      const day = nyDay.format(start);
      const today = this.events.filter(
        (event) => event.day === day && isVisible(event, settings)
      );
      items.push(dailyTable(day, start, today));
    }
    return { graphics: { items } };
  }
}
module.exports = {
  name: "EconomicCalendar",
  description: "Economic Calendar (weekly snapshot)",
  calculator: EconomicCalendar,
  inputType: "bars",
  plots: {},
  tags: ["LRM"],
  params: {
    onlyUSD: spec.bool(true),
    showHigh: spec.bool(true),
    showMedium: spec.bool(true),
    showLow: spec.bool(true),
    showHoliday: spec.bool(true),
    showTodayTable: spec.bool(true),
    showReleaseMarkers: spec.bool(true),
    showMarkerNames: spec.bool(false)
  }
};
