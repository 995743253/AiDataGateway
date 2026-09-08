import io

path = 'src/AiDataGateway.Web/src/App.vue'
text = io.open(path, encoding='utf-8').read()

# 1. formatDate shows milliseconds everywhere
old = "formatDate (value) { return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—' },"
new = "formatDate (value) { return value ? new Date(value).toLocaleString('zh-CN', { hour12: false, fractionalSecondDigits: 3 }) : '—' },"
assert old in text
text = text.replace(old, new, 1)

# 2. expand/tree column for ALL sources (local NLog values are normalized too)
old = '              <el-table-column v-if="selectedApplicationLogSource?.type === 2" type="expand" width="48"><template #default="s"><div class="structured-log-expand"><div class="structured-log-expand-title">事件属性层级</div><StructuredValueTree :value="s.row.properties || {}" /></div></template></el-table-column>'
new = '              <el-table-column type="expand" width="48"><template #default="s"><div class="structured-log-expand"><div class="structured-log-expand-title">事件属性层级</div><StructuredValueTree :value="s.row.properties || {}" /></div></template></el-table-column>'
assert old in text
text = text.replace(old, new, 1)

old = '              <el-table-column v-if="selectedRealtimeLogSource?.type === 2" type="expand" width="48"><template #default="s"><div class="structured-log-expand"><div class="structured-log-expand-title">事件属性层级</div><StructuredValueTree :value="s.row.properties || {}" /></div></template></el-table-column>'
new = '              <el-table-column type="expand" width="48"><template #default="s"><div class="structured-log-expand"><div class="structured-log-expand-title">事件属性层级</div><StructuredValueTree :value="s.row.properties || {}" /></div></template></el-table-column>'
assert old in text
text = text.replace(old, new, 1)

# 3. wider time columns for millisecond display (the two log tables)
old = '              <el-table-column prop="timestampUtc" label="时间" width="190"><template #default="s">{{ formatDate(s.row.timestampUtc) }}</template></el-table-column>'
assert text.count(old) == 2
text = text.replace(old, '              <el-table-column prop="timestampUtc" label="时间" width="215"><template #default="s">{{ formatDate(s.row.timestampUtc) }}</template></el-table-column>')

# 4. object values render as indented JSON and can be opened in the viewer
old = """    formatCell (value) {
      if (value === null || value === undefined) return 'NULL'
      return typeof value === 'object' ? JSON.stringify(value) : String(value)
    },"""
new = """    formatCell (value) {
      if (value === null || value === undefined) return 'NULL'
      return typeof value === 'object' ? JSON.stringify(value, null, 2) : String(value)
    },"""
assert old in text
text = text.replace(old, new, 1)

# 5. detect/open handle object values (backend now normalizes JSON into objects)
old = """    detectStructuredValue (value) {
      if (typeof value !== 'string' || value.trim().length < 3) return null
      if (this.extractJsonPretty(value) !== null) return 'json'
"""
new = """    detectStructuredValue (value) {
      if (value !== null && typeof value === 'object') return 'json'
      if (typeof value !== 'string' || value.trim().length < 3) return null
      if (this.extractJsonPretty(value) !== null) return 'json'
"""
assert old in text
text = text.replace(old, new, 1)

old = """    openPropertyValueViewer (row) {
      const kind = this.detectStructuredValue(row.value)
      const text = this.formatCell(row.value)
      this.propertyViewer = { key: row.key, kind, text, pretty: kind === 'json' ? this.extractJsonPretty(row.value) || text : text }
      this.propertyViewerDialog = true
    },"""
new = """    openPropertyValueViewer (row) {
      const kind = this.detectStructuredValue(row.value)
      if (kind === 'json' && row.value !== null && typeof row.value === 'object') {
        this.propertyViewer = { key: row.key, kind, text: JSON.stringify(row.value), pretty: JSON.stringify(row.value, null, 2) }
        this.propertyViewerDialog = true
        return
      }
      const text = this.formatCell(row.value)
      this.propertyViewer = { key: row.key, kind, text, pretty: kind === 'json' ? this.extractJsonPretty(row.value) || text : text }
      this.propertyViewerDialog = true
    },"""
assert old in text
text = text.replace(old, new, 1)

io.open(path, 'w', encoding='utf-8', newline='').write(text)
print('frontend fixes applied')
PYEOF