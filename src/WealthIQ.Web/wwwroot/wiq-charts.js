// Thin wrapper around TradingView Lightweight Charts (v4) for Blazor interop.
// Charts are keyed by a string id supplied from C#. The library is loaded as a
// classic script in App.razor, exposing the global `LightweightCharts`.
window.wiqCharts = {
    _charts: {},

    create: function (id, kind, theme) {
        var container = document.getElementById(id);
        if (!container || !window.LightweightCharts) return;
        this.dispose(id);

        var chart = LightweightCharts.createChart(container, {
            autoSize: true,
            layout: { background: { color: 'transparent' }, textColor: theme.textColor },
            grid: { vertLines: { color: theme.gridColor }, horzLines: { color: theme.gridColor } },
            rightPriceScale: { borderColor: theme.gridColor },
            timeScale: { borderColor: theme.gridColor, timeVisible: false },
            crosshair: { mode: 0 }
        });

        var series = kind === 'line'
            ? chart.addLineSeries({ color: theme.lineColor, lineWidth: 2 })
            : chart.addCandlestickSeries({
                upColor: theme.upColor, downColor: theme.downColor,
                borderUpColor: theme.upColor, borderDownColor: theme.downColor,
                wickUpColor: theme.upColor, wickDownColor: theme.downColor
            });

        this._charts[id] = { chart: chart, series: series, kind: kind };
    },

    setData: function (id, data, initialRangeDays) {
        var entry = this._charts[id];
        if (!entry) return;
        var points = data || [];
        entry.series.setData(points);

        // When an initial window is requested and there is enough data, show only the last
        // `initialRangeDays` days; otherwise fit everything. Times are "yyyy-MM-dd" strings.
        if (initialRangeDays && points.length > 0) {
            var lastTime = points[points.length - 1].time;
            var last = new Date(lastTime + 'T00:00:00Z');
            var firstAvailable = new Date(points[0].time + 'T00:00:00Z');
            var from = new Date(last);
            from.setUTCDate(from.getUTCDate() - initialRangeDays);
            if (from <= firstAvailable) {
                entry.chart.timeScale().fitContent();
            } else {
                var iso = function (d) { return d.toISOString().slice(0, 10); };
                entry.chart.timeScale().setVisibleRange({ from: iso(from), to: lastTime });
            }
        } else {
            entry.chart.timeScale().fitContent();
        }
    },

    applyTheme: function (id, theme) {
        var entry = this._charts[id];
        if (!entry) return;
        entry.chart.applyOptions({
            layout: { textColor: theme.textColor },
            grid: { vertLines: { color: theme.gridColor }, horzLines: { color: theme.gridColor } },
            rightPriceScale: { borderColor: theme.gridColor },
            timeScale: { borderColor: theme.gridColor }
        });
        if (entry.kind === 'line') {
            entry.series.applyOptions({ color: theme.lineColor });
        } else {
            entry.series.applyOptions({
                upColor: theme.upColor, downColor: theme.downColor,
                borderUpColor: theme.upColor, borderDownColor: theme.downColor,
                wickUpColor: theme.upColor, wickDownColor: theme.downColor
            });
        }
    },

    dispose: function (id) {
        var entry = this._charts[id];
        if (entry) { try { entry.chart.remove(); } catch (e) { } delete this._charts[id]; }
    }
};
