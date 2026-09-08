<template>
  <div class="structured-value-tree" :class="{ 'tree-root': depth === 0 }">
    <div v-for="node in nodes" :key="node.key" class="structured-tree-node">
      <button v-if="node.children" type="button" class="structured-tree-row branch" @click="toggle(node.key)">
        <span class="tree-chevron" :class="{ expanded: isExpanded(node.key) }">›</span>
        <span class="tree-key">{{ node.label }}</span>
        <span class="tree-kind">{{ node.kind }} · {{ node.count }}</span>
      </button>
      <div v-else class="structured-tree-row leaf">
        <span class="tree-spacer" />
        <span class="tree-key">{{ node.label }}</span>
        <span class="tree-value" :class="`value-${node.kind}`">{{ node.display }}</span>
      </div>
      <StructuredValueTree
        v-if="node.children && isExpanded(node.key)"
        :value="node.value"
        :depth="depth + 1"
        :max-depth="maxDepth"
      />
    </div>
    <div v-if="truncated" class="structured-tree-more">仅显示前 {{ maxChildren }} 项</div>
  </div>
</template>

<script>
export default {
  name: 'StructuredValueTree',
  props: {
    value: { type: [Object, Array], required: true },
    depth: { type: Number, default: 0 },
    maxDepth: { type: Number, default: 8 },
    maxChildren: { type: Number, default: 200 }
  },
  data: () => ({ collapsed: new Set() }),
  computed: {
    entries () {
      if (Array.isArray(this.value)) return this.value.map((item, index) => [`[${index}]`, item])
      return Object.entries(this.value || {})
    },
    truncated () { return this.entries.length > this.maxChildren },
    nodes () {
      return this.entries.slice(0, this.maxChildren).map(([label, rawValue], index) => {
        const value = this.normalize(rawValue)
        const children = this.depth < this.maxDepth && value !== null && typeof value === 'object'
        const count = children ? (Array.isArray(value) ? value.length : Object.keys(value).length) : 0
        return { key: `${this.depth}-${index}-${label}`, label, value, children, count, kind: this.kindOf(value), display: this.displayValue(value) }
      })
    }
  },
  methods: {
    normalize (value) {
      if (typeof value !== 'string') return value
      const text = value.trim()
      if (text.length < 2 || !((text.startsWith('{') && text.endsWith('}')) || (text.startsWith('[') && text.endsWith(']')))) return value
      try { return JSON.parse(text) } catch (_) { return value }
    },
    kindOf (value) {
      if (value === null) return 'null'
      if (Array.isArray(value)) return '数组'
      if (typeof value === 'object') return '对象'
      return typeof value
    },
    displayValue (value) {
      if (value === null) return 'null'
      if (value === '') return '""'
      return String(value)
    },
    isExpanded (key) { return !this.collapsed.has(key) },
    toggle (key) {
      const next = new Set(this.collapsed)
      if (next.has(key)) next.delete(key); else next.add(key)
      this.collapsed = next
    }
  }
}
</script>
