<template>
  <el-card class="metric-trend-card" shadow="never">
    <template #header>
      <div class="metric-trend-heading">
        <div><strong>{{ metric.name }}</strong><small>{{ metric.category }}</small></div>
        <span>{{ rangeLabel }}</span>
      </div>
    </template>
    <div class="metric-chart" @mouseleave="hoverPoint = null">
      <div class="chart-plot">
        <svg viewBox="0 0 800 280" preserveAspectRatio="xMidYMid meet" role="img">
          <title>{{ metric.name }}趋势图</title>
          <defs><linearGradient :id="gradientId" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="#2a82bd" stop-opacity="0.22" /><stop offset="100%" stop-color="#2a82bd" stop-opacity="0.02" /></linearGradient></defs>
          <g><g v-for="tick in yTicks" :key="`y-${tick.y}`"><line x1="72" x2="790" :y1="tick.y" :y2="tick.y" class="chart-grid-line" /><text x="62" :y="tick.y + 4" text-anchor="end" class="chart-axis-label">{{ tick.label }}</text></g><line x1="72" x2="72" y1="20" y2="232" class="chart-axis-line" /></g>
          <g><line x1="72" x2="790" y1="232" y2="232" class="chart-axis-line" /><g v-for="tick in xTicks" :key="`x-${tick.index}`"><line :x1="tick.x" :x2="tick.x" y1="232" y2="239" class="chart-axis-tick" /><text :x="tick.x" y="260" :text-anchor="tick.anchor" class="chart-axis-label chart-time-label">{{ tick.label }}</text></g></g>
          <polygon v-if="points.length > 1" :points="areaPoints" class="chart-area" :style="{ fill: `url(#${gradientId})` }" />
          <g v-if="mode === 'history'"><g v-for="reference in referenceLines" :key="reference.key"><line x1="72" x2="790" :y1="reference.y" :y2="reference.y" class="chart-reference-line" :class="reference.key" /><text x="782" :y="Math.max(14, reference.y - 6)" text-anchor="end" class="chart-reference-label" :class="reference.key">{{ reference.label }}</text></g></g>
          <polyline v-if="points.length > 1" :points="linePoints" class="chart-line" />
          <g v-for="point in mode === 'recent' ? points : []" :key="point.key" class="chart-point-group" @mouseenter="hoverPoint = point" @mousemove="hoverPoint = point"><circle :cx="point.x" :cy="point.y" r="11" class="chart-point-hit" /><circle :cx="point.x" :cy="point.y" :r="hoverPoint?.key === point.key ? 3.8 : 2.4" class="chart-point" :class="{ active: hoverPoint?.key === point.key }" /></g>
        </svg>
        <div v-if="hoverPoint" class="chart-tooltip" :class="{ 'align-left': hoverPoint.x < 175, 'align-right': hoverPoint.x > 680, 'place-below': hoverPoint.y < 78 }" :style="tooltipStyle"><strong>{{ metric.name }}</strong><span>{{ formatValue(hoverPoint.value) }}</span><small>{{ formatDate(hoverPoint.timestamp) }}</small></div>
        <el-empty v-if="points.length < 2" description="暂无足够采样点" :image-size="54" />
      </div>
    </div>
  </el-card>
</template>

<script>
export default {
  props: { metric: { type: Object, required: true }, samples: { type: Array, default: () => [] }, mode: { type: String, default: 'recent' } },
  data: () => ({ hoverPoint: null }),
  computed: {
    gradientId () { return `metric-gradient-${String(this.metric.key).replace(/[^a-z0-9]/gi, '-')}` },
    values () { return this.samples.map(item => this.metricValue(item)).filter(value => value !== null) },
    scale () {
      if (!this.values.length) return { min: 0, max: this.metric.unit === 'percent' ? 100 : 1 }
      const observedMin = Math.min(...this.values); const observedMax = Math.max(...this.values)
      const minimumSpan = this.metric.unit === 'percent' ? 10 : Math.max(Math.abs(observedMax) * 0.1, 1)
      const paddedSpan = Math.max(observedMax - observedMin, minimumSpan)
      let min = Math.max(0, observedMin - paddedSpan * 0.18); let max = observedMax + paddedSpan * 0.18
      const roughStep = Math.max((max - min) / 4, Number.EPSILON); const magnitude = 10 ** Math.floor(Math.log10(roughStep)); const normalized = roughStep / magnitude
      const step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude
      min = Math.floor(min / step) * step; max = Math.ceil(max / step) * step
      if (this.metric.unit === 'percent') { max = Math.min(100, max); if (max - min < 10) { if (max >= 100) min = 90; else max = Math.min(100, min + 10) } }
      return { min, max: max <= min ? min + step : max }
    },
    points () {
      const rows = [...this.samples].sort((a, b) => new Date(a.collectedAtUtc) - new Date(b.collectedAtUtc)).map((sample, index) => ({ sample, index, value: this.metricValue(sample) })).filter(item => item.value !== null)
      const range = Math.max(1e-9, this.scale.max - this.scale.min)
      return rows.map((item, index) => ({ key: `${item.sample.collectedAtUtc}-${item.index}`, timestamp: item.sample.collectedAtUtc, value: item.value, x: rows.length === 1 ? 431 : 72 + index * 718 / (rows.length - 1), y: 232 - (item.value - this.scale.min) * 212 / range }))
    },
    linePoints () { return this.points.map(point => `${point.x},${point.y}`).join(' ') },
    areaPoints () { return this.points.length > 1 ? `${this.points[0].x},232 ${this.linePoints} ${this.points.at(-1).x},232` : '' },
    referenceLines () {
      if (this.mode !== 'history' || !this.points.length) return []
      const min = Math.min(...this.points.map(point => point.value)); const max = Math.max(...this.points.map(point => point.value)); const yFor = value => 232 - (value - this.scale.min) * 212 / Math.max(1e-9, this.scale.max - this.scale.min)
      if (Math.abs(max - min) < 1e-9) return [{ key: 'same', y: yFor(max), label: `最高/最低 ${this.formatValue(max)}` }]
      return [{ key: 'maximum', y: yFor(max), label: `最高 ${this.formatValue(max)}` }, { key: 'minimum', y: yFor(min), label: `最低 ${this.formatValue(min)}` }]
    },
    yTicks () { const range = this.scale.max - this.scale.min; return Array.from({ length: 5 }, (_, index) => ({ y: 20 + index * 53, label: this.formatValue(this.scale.max - range * index / 4) })) },
    xTicks () { if (!this.points.length) return []; const count = Math.min(5, this.points.length); const indices = [...new Set(Array.from({ length: count }, (_, index) => Math.round(index * (this.points.length - 1) / Math.max(1, count - 1))))]; return indices.map((pointIndex, index) => ({ index: pointIndex, x: this.points[pointIndex].x, label: this.formatAxisTime(this.points[pointIndex].timestamp), anchor: index === 0 ? 'start' : index === indices.length - 1 ? 'end' : 'middle' })) },
    rangeLabel () { return `${this.formatValue(this.scale.min)} – ${this.formatValue(this.scale.max)}` },
    tooltipStyle () { return this.hoverPoint ? { left: `${this.hoverPoint.x / 8}%`, top: `${this.hoverPoint.y / 2.8}%` } : {} }
  },
  methods: {
    metricValue (sample) { const value = sample?.metrics?.[this.metric.key]; return value === null || value === undefined || !Number.isFinite(Number(value)) ? null : Number(value) },
    formatValue (value) { if (value === null || value === undefined || !Number.isFinite(Number(value))) return '—'; if (this.metric.unit === 'percent') return `${Number(value).toFixed(1)}%`; if (this.metric.unit === 'bytes' || this.metric.unit === 'bytes_per_second') { const units = ['B', 'KB', 'MB', 'GB', 'TB']; let number = Number(value); let index = 0; while (Math.abs(number) >= 1024 && index < units.length - 1) { number /= 1024; index++ } return `${number.toFixed(index ? 1 : 0)} ${units[index]}${this.metric.unit === 'bytes_per_second' ? '/s' : ''}` } if (this.metric.unit === 'duration_seconds') { const seconds = Math.max(0, Math.floor(Number(value))); const days = Math.floor(seconds / 86400); const hours = Math.floor(seconds % 86400 / 3600); return days ? `${days}天 ${hours}小时` : `${hours}小时` } return Number(value).toLocaleString('zh-CN', { maximumFractionDigits: 1 }) },
    formatDate (value) { const date = new Date(value); return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString('zh-CN', { hour12: false }) },
    formatAxisTime (value) { const date = new Date(value); if (Number.isNaN(date.getTime())) return '—'; const duration = this.points.length > 1 ? new Date(this.points.at(-1).timestamp) - new Date(this.points[0].timestamp) : 0; const md = `${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`; const hm = `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`; return duration > 7 * 86400000 ? md : duration > 86400000 ? `${md} ${hm}` : `${hm}:${String(date.getSeconds()).padStart(2, '0')}` }
  }
}
</script>

<style scoped>
.metric-trend-card { min-width: 0; height: 100%; }
.metric-trend-heading { display:flex; align-items:center; justify-content:space-between; gap:12px; }
.metric-trend-heading > div { display:grid; gap:2px; }
.metric-trend-heading small, .metric-trend-heading > span { color:var(--brand-text-muted); font-size:12px; }
.metric-chart { position:relative; }
.chart-plot { position:relative; width:100%; aspect-ratio:20/7; }
.chart-plot svg { position:absolute; inset:0; width:100%; height:100%; overflow:visible; }
.chart-grid-line { stroke:#e3eaf2; stroke-width:1; stroke-dasharray:4 5; vector-effect:non-scaling-stroke; }
.chart-axis-line { stroke:#9cabbc; stroke-width:1.2; vector-effect:non-scaling-stroke; }
.chart-axis-tick { stroke:#9cabbc; stroke-width:1; vector-effect:non-scaling-stroke; }
.chart-axis-label { fill:#6f7e91; font-size:11px; font-variant-numeric:tabular-nums; }
.chart-time-label { font-size:10.5px; }
.chart-line { fill:none; stroke:#1769aa; stroke-width:2.5; stroke-linecap:round; stroke-linejoin:round; vector-effect:non-scaling-stroke; }
.chart-area { stroke:none; pointer-events:none; }
.chart-reference-line { stroke-width:1.2; stroke-dasharray:7 5; vector-effect:non-scaling-stroke; }
.chart-reference-line.maximum { stroke:#d56b45; }.chart-reference-line.minimum { stroke:#3b9270; }.chart-reference-line.same { stroke:#7b6eae; }
.chart-reference-label { font-size:10.5px; font-weight:600; paint-order:stroke; stroke:rgba(255,255,255,.92); stroke-width:3px; }.chart-reference-label.maximum { fill:#bd5838; }.chart-reference-label.minimum { fill:#267a59; }.chart-reference-label.same { fill:#695b9b; }
.chart-point-group { cursor:pointer; }.chart-point-hit { fill:transparent; stroke:transparent; pointer-events:all; }.chart-point { fill:#fff; stroke:#1769aa; stroke-width:1.5; }.chart-point.active { fill:#1769aa; stroke:#fff; stroke-width:2; }
.chart-tooltip { --tooltip-x:-50%; position:absolute; z-index:4; display:grid; gap:3px; min-width:154px; padding:10px 12px; color:#edf6ff; background:rgba(15,39,67,.96); border:1px solid rgba(125,190,235,.35); border-radius:8px; pointer-events:none; transform:translate(var(--tooltip-x),calc(-100% - 10px)); }.chart-tooltip.align-left { --tooltip-x:-8%; }.chart-tooltip.align-right { --tooltip-x:-92%; }.chart-tooltip.place-below { transform:translate(var(--tooltip-x),12px); }.chart-tooltip span { color:#fff; font-size:18px; font-weight:700; }.chart-tooltip small { color:#b9c9da; font-size:11px; }
.chart-plot > :deep(.el-empty) { position:absolute; inset:28px 0 36px 70px; z-index:2; padding:0; background:rgba(255,255,255,.72); }
:global(html.dark) .chart-grid-line { stroke:#26364e; }:global(html.dark) .chart-axis-line,:global(html.dark) .chart-axis-tick { stroke:#46607f; }:global(html.dark) .chart-axis-label { fill:#8ea0b8; }:global(html.dark) .chart-point { fill:#152033; }:global(html.dark) .chart-reference-label { stroke:rgba(14,23,37,.92); }:global(html.dark) .chart-plot > :deep(.el-empty) { background:rgba(14,23,37,.72); }
</style>
