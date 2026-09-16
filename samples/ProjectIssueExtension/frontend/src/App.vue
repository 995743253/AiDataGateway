<template>
  <div class="page">
    <header class="top">
      <div>
        <h1>项目问题单</h1>
        <div class="muted">集中管理 Q 单、个案、测试状态、变动状态与来源文档；日报归纳当天变动</div>
      </div>
      <div class="top-actions">
        <el-button @click="layout.summaryHidden = !layout.summaryHidden">{{ layout.summaryHidden ? '展开统计' : '收起统计' }}</el-button>
        <el-button @click="layout.filtersHidden = !layout.filtersHidden">{{ layout.filtersHidden ? '展开筛选' : '收起筛选' }}</el-button>
        <el-button @click="openReport">当天日报</el-button>
        <el-button @click="importVisible = true">导入表格</el-button>
        <el-button type="primary" @click="openEditor(null)">＋ 新建问题</el-button>
      </div>
    </header>

    <section v-show="!layout.filtersHidden" class="filters card">
      <div class="field" style="min-width: 210px">
        <label>项目</label>
        <el-button class="project-btn" @click="projectPickerVisible = true">
          <span class="project-btn-text">{{ projectLabel }}</span>
          <el-icon class="caret"><ArrowDown /></el-icon>
        </el-button>
      </div>
      <div class="field grow">
        <label>搜索单号、个案、程序、说明或解决描述</label>
        <el-input v-model="keyword" placeholder="输入关键词后按回车" clearable @keyup.enter="search" />
      </div>
      <div class="field">
        <label>测试状态</label>
        <el-select v-model="filterStatus" clearable placeholder="全部状态" style="width: 150px" @change="search">
          <el-option v-for="name in knownTestStatuses" :key="name" :label="name" :value="name" />
        </el-select>
      </div>
      <div class="field">
        <label>变动状态</label>
        <el-select v-model="filterWorkflow" clearable placeholder="全部" style="width: 150px" @change="search">
          <el-option v-for="name in WORKFLOW_OPTIONS" :key="name" :label="name" :value="name" />
        </el-select>
      </div>
      <el-button type="primary" @click="search">查询</el-button>
    </section>

    <section v-show="!layout.summaryHidden" class="summary">
      <article v-for="card in summaryCards" :key="card.label" class="card stat-card" :class="card.tone">
        <span class="muted">{{ card.label }}</span>
        <strong :title="String(card.value)">{{ card.value }}</strong>
      </article>
    </section>

    <section v-loading="loading" class="card table-card">
      <div class="table-toolbar">
        <div class="toolbar-hints">
          <span class="muted">问题列表</span>
          <span class="muted">{{ toolbarHints }}</span>
        </div>
        <el-popover placement="bottom-end" :width="200" trigger="click" title="显示列">
          <template #reference>
            <el-button>⚙ 显示列</el-button>
          </template>
          <el-checkbox-group v-model="visibleKeys">
            <div v-for="column in selectableColumns" :key="column.key" class="col-option">
              <el-checkbox :value="column.key" :label="column.label" />
            </div>
          </el-checkbox-group>
        </el-popover>
      </div>
      <div ref="tableWrap" class="table-wrap">
        <el-table :data="issues" :height="tableHeight" style="width: 100%" @sort-change="onSortChange" @filter-change="onFilterChange">
        <el-table-column
          v-for="column in visibleColumns"
          :key="column.key"
          :column-key="column.key"
          :prop="column.key === 'source' ? undefined : column.key"
          :label="column.label"
          :width="columnWidth(column.key)"
          :sortable="column.sortable ? 'custom' : false"
          :filters="column.filter === 'enum' ? enumFilters(column.key) : undefined"
        >
          <template v-if="column.key !== 'actions'" #header>
            <div class="th-wrap">
              <span class="th-label">{{ column.label }}</span>
              <span class="issue-resizer" @mousedown="startResize($event, column)"></span>
            </div>
          </template>
          <template #default="{ row }">
            <template v-if="column.key === 'ticketNumber'"><b :title="row.ticketNumber">{{ row.ticketNumber || '—' }}</b></template>

            <template v-else-if="column.key === 'testStatus'">
              <el-dropdown trigger="click" @command="value => applyInline(row, 'testStatus', value)">
                <el-tag class="cell-tag" :type="statusTagType(row.testStatus)" size="small">{{ row.testStatus || '未标记' }}</el-tag>
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item command="">清除</el-dropdown-item>
                    <el-dropdown-item v-for="option in editOptions('testStatus', row)" :key="option" :command="option" divided>{{ option }}</el-dropdown-item>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
            </template>

            <template v-else-if="column.key === 'workflowStatus'">
              <el-dropdown trigger="click" @command="value => applyInline(row, 'workflowStatus', value)">
                <el-tag class="cell-tag" :type="workflowTagType(row.workflowStatus)" size="small" :title="workflowTitle(row)">{{ row.workflowStatus || '未标记' }}</el-tag>
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item command="">清除</el-dropdown-item>
                    <el-dropdown-item v-for="option in editOptions('workflowStatus', row)" :key="option" :command="option" divided>{{ option }}</el-dropdown-item>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
            </template>

            <template v-else-if="column.key === 'resolved'">
              <el-dropdown trigger="click" @command="value => applyInline(row, 'resolved', value)">
                <el-tag class="cell-tag" :type="row.resolved === '是' ? 'success' : 'info'" size="small">{{ row.resolved || '未标记' }}</el-tag>
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item command="">清除</el-dropdown-item>
                    <el-dropdown-item v-for="option in editOptions('resolved', row)" :key="option" :command="option" divided>{{ option }}</el-dropdown-item>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
            </template>

            <template v-else-if="column.key === 'raisedDate' || column.key === 'completedDate'">
              <el-date-picker
                v-if="isEditing(row, column.key)"
                :model-value="row[column.key]"
                type="date"
                value-format="YYYY-MM-DD"
                size="small"
                style="width: 100%"
                @change="value => applyInline(row, column.key, value || '')"
                @blur="cancelEdit"
              />
              <span v-else class="cell-tag" @click="startEdit(row, column.key)">{{ row[column.key] || '—' }}</span>
            </template>

            <template v-else-if="column.key === 'source'">{{ row.sourceUrl ? '在线文档' : row.sourceSheet ? '导入' : '手工' }}</template>

            <template v-else-if="column.key === 'actions'">
              <el-button link type="primary" size="small" @click="detailIssue = row; detailVisible = true">详情</el-button>
              <el-button link type="primary" size="small" @click="openEditor(row)">编辑</el-button>
              <el-button link type="danger" size="small" @click="removeIssue(row)">删除</el-button>
            </template>

            <template v-else>{{ row[column.key] || '—' }}</template>
          </template>
        </el-table-column>
      </el-table>
      </div>
      <div class="pager">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="total"
          :page-sizes="[15, 30, 50, 100]"
          layout="total, sizes, prev, pager, next, jumper"
          background
          @current-change="load"
          @size-change="onSizeChange"
        />
      </div>
    </section>

    <!-- 选择项目 -->
    <el-dialog v-model="projectPickerVisible" title="选择项目" width="460">
      <el-input v-model="projectSearch" placeholder="搜索项目名称或编号" clearable />
      <div class="project-list" style="margin-top: 10px">
        <div
          v-for="project in filteredProjects"
          :key="project.code"
          class="proj-item"
          :class="{ current: project.code === currentProject }"
          @click="pickProject(project)"
        >
          <div>
            <div class="proj-name">{{ project.name }}</div>
            <div class="muted">{{ project.code }}</div>
          </div>
          <span v-if="project.code === currentProject" class="check">✓</span>
        </div>
        <div v-if="filteredProjects.length === 0" class="muted" style="padding: 20px; text-align: center">没有匹配的项目</div>
      </div>
      <template #footer>
        <el-button @click="projectPickerVisible = false">关闭</el-button>
      </template>
    </el-dialog>

    <!-- 新建/编辑 -->
    <el-dialog v-model="editorVisible" :title="editorIssue ? '编辑问题' : '新建问题'" width="820">
      <el-form label-position="top">
        <div class="form-grid">
          <el-form-item label="Q 单编号"><el-input v-model="editorForm.ticketNumber" /></el-form-item>
          <el-form-item label="个案书名"><el-input v-model="editorForm.caseName" /></el-form-item>
          <el-form-item label="程序名称"><el-input v-model="editorForm.programName" /></el-form-item>
          <el-form-item label="类别"><el-input v-model="editorForm.category" /></el-form-item>
          <el-form-item label="测试状态">
            <el-select v-model="editorForm.testStatus" filterable clearable placeholder="未标记" style="width: 100%">
              <el-option v-for="option in testStatusOptions" :key="option" :label="option" :value="option" />
            </el-select>
          </el-form-item>
          <el-form-item label="变动状态">
            <el-select v-model="editorForm.workflowStatus" clearable placeholder="未标记" style="width: 100%">
              <el-option v-for="name in WORKFLOW_OPTIONS" :key="name" :label="name" :value="name" />
            </el-select>
          </el-form-item>
          <el-form-item label="是否解决">
            <el-select v-model="editorForm.resolved" clearable placeholder="未标记" style="width: 100%">
              <el-option label="是" value="是" />
              <el-option label="否" value="否" />
            </el-select>
          </el-form-item>
          <el-form-item label="提单日"><el-date-picker v-model="editorForm.raisedDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" /></el-form-item>
          <el-form-item label="完成日"><el-date-picker v-model="editorForm.completedDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" /></el-form-item>
          <el-form-item label="负责顾问"><el-input v-model="editorForm.owner" /></el-form-item>
          <el-form-item label="开发人员"><el-input v-model="editorForm.developer" /></el-form-item>
          <el-form-item label="问题处理人"><el-input v-model="editorForm.handler" /></el-form-item>
          <el-form-item label="问题描述 / 开发说明" class="span2"><el-input v-model="editorForm.description" type="textarea" :rows="3" /></el-form-item>
          <el-form-item label="解决描述" class="span2"><el-input v-model="editorForm.solutionNote" type="textarea" :rows="3" /></el-form-item>
          <el-form-item label="开发备注" class="span2"><el-input v-model="editorForm.developmentNote" type="textarea" :rows="2" /></el-form-item>
          <el-form-item label="备注" class="span2"><el-input v-model="editorForm.remark" type="textarea" :rows="2" /></el-form-item>
          <el-form-item label="来源文档链接" class="span2"><el-input v-model="editorForm.sourceUrl" placeholder="https://" /></el-form-item>
        </div>
      </el-form>
      <template #footer>
        <el-button @click="editorVisible = false">取消</el-button>
        <el-button type="primary" @click="saveEditor">保存问题</el-button>
      </template>
    </el-dialog>

    <!-- 导入 -->
    <el-dialog v-model="importVisible" title="导入问题单" width="760">
      <p class="hint">
        支持 .xlsx、.csv、.tsv（单文件不超过 5 MB）；也可在金山文档等在线表格中选中表头和数据区域，复制后粘贴到下方。
        需要登录的在线文档请先由用户导出或复制，不会绕过访问权限。表格可包含“变动状态”“解决描述”“提单日期”“完成日期”列；未填写变动状态的新记录不设变动时间。
      </p>
      <el-form label-position="top">
        <el-form-item label="选择 Excel/CSV 文件"><input type="file" accept=".xlsx,.csv,.tsv,.txt" @change="onImportFileChange" /></el-form-item>
        <el-form-item label="或粘贴在线表格内容（含表头）">
          <el-input v-model="importText" type="textarea" :rows="6" placeholder="Q单编号	测试状态	变动状态	个案书名" />
        </el-form-item>
        <el-form-item label="原始在线文档链接（可选，供追溯）"><el-input v-model="importSourceUrl" placeholder="https://www.kdocs.cn/l/..." /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="importVisible = false">取消</el-button>
        <el-button type="primary" :loading="importing" @click="doImport">确认导入</el-button>
      </template>
    </el-dialog>

    <!-- 详情 -->
    <el-dialog v-model="detailVisible" title="问题详情" width="720">
      <el-descriptions :column="1" border size="small">
        <el-descriptions-item v-for="[label, value] in detailRows" :key="label" :label="label">{{ value }}</el-descriptions-item>
      </el-descriptions>
      <template #footer>
        <el-button @click="detailVisible = false">关闭</el-button>
      </template>
    </el-dialog>

    <!-- 日报 -->
    <el-dialog v-model="reportVisible" title="变动日报" width="760">
      <div class="report-bar">
        <span class="muted">{{ reportSummary }}</span>
        <div style="display: flex; align-items: center; gap: 8px">
          <el-checkbox v-model="reportAll" @change="buildReport">所有项目</el-checkbox>
          <el-date-picker v-model="reportDate" type="date" value-format="YYYY-MM-DD" style="width: 140px" @change="buildReport" />
          <el-button @click="buildReport">刷新</el-button>
          <el-button type="primary" @click="copyReport">复制内容</el-button>
        </div>
      </div>
      <el-input v-model="reportText" type="textarea" :rows="14" readonly />
      <template #footer>
        <el-button @click="reportVisible = false">关闭</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { ArrowDown } from '@element-plus/icons-vue'

const ROOT = '/api/custom-modules/project-issue-tracker/invoke/'
const WORKFLOW_OPTIONS = ['未处理', '处理中', '处理完成']
const TEST_STATUS_PRESETS = ['待开发处理', '开发完成', '待复测', '测试通过', '已完成', '已关闭']
const STORE_KEY = 'project_issue_columns_v1'
const LAYOUT_KEY = 'project_issue_layout_v1'

const COLUMNS = [
  { key: 'ticketNumber', label: 'Q 单编号', width: 150, sortable: true, filter: 'text' },
  { key: 'testStatus', label: '测试状态', width: 110, sortable: true, filter: 'enum', edit: 'select' },
  { key: 'workflowStatus', label: '变动状态', width: 110, sortable: true, filter: 'enum', edit: 'select' },
  { key: 'resolved', label: '是否解决', width: 92, sortable: true, filter: 'enum', edit: 'select' },
  { key: 'category', label: '类别', width: 90, sortable: true, filter: 'enum' },
  { key: 'caseName', label: '个案书名', width: 150, sortable: true, filter: 'text' },
  { key: 'programName', label: '程序名称', width: 150, sortable: true, filter: 'text' },
  { key: 'owner', label: '负责顾问', width: 96, sortable: true, filter: 'text' },
  { key: 'developer', label: '开发人员', width: 96, sortable: true, filter: 'text' },
  { key: 'raisedDate', label: '提单日', width: 104, sortable: true, edit: 'date' },
  { key: 'completedDate', label: '完成日', width: 104, sortable: true, edit: 'date' },
  { key: 'source', label: '来源', width: 80 },
  { key: 'actions', label: '操作', width: 168, locked: true }
]

const projects = ref([])
const currentProject = ref('')
const issues = ref([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(30)
const keyword = ref('')
const filterStatus = ref('')
const filterWorkflow = ref('')
const knownTestStatuses = ref([])
const summaryData = ref(null)
const loading = ref(false)
const sort = reactive({ key: '', dir: 'desc' })
const activeFilters = reactive({})
const importVisible = ref(false)
const importText = ref('')
const importSourceUrl = ref('')
const importing = ref(false)
let importFile = null
const detailVisible = ref(false)
const detailIssue = ref(null)
const editorVisible = ref(false)
const editorIssue = ref(null)
const editorForm = reactive({})
const reportVisible = ref(false)
const reportDate = ref('')
const reportAll = ref(true)
const reportText = ref('')
const reportSummary = ref('')
const projectPickerVisible = ref(false)
const projectSearch = ref('')
const layout = reactive({ filtersHidden: false, summaryHidden: false })

let columnState = {}
try { columnState = JSON.parse(localStorage.getItem(STORE_KEY) || '{}') } catch { columnState = {} }
try { Object.assign(layout, JSON.parse(localStorage.getItem(LAYOUT_KEY) || '{}')) } catch {}

const columnWidth = key => columnState[key]?.width || COLUMNS.find(c => c.key === key)?.width || 110
const columnVisible = key => key === 'actions' || columnState[key]?.visible !== false
const visibleColumns = computed(() => COLUMNS.filter(c => columnVisible(c.key)))
const selectableColumns = COLUMNS.filter(c => !c.locked)
const visibleKeys = ref(COLUMNS.filter(c => !c.locked && columnVisible(c.key)).map(c => c.key))
function saveColumnState() {
  const slim = {}
  for (const column of COLUMNS) if (columnState[column.key]) slim[column.key] = columnState[column.key]
  try { localStorage.setItem(STORE_KEY, JSON.stringify(slim)) } catch {}
}
function saveLayout() { try { localStorage.setItem(LAYOUT_KEY, JSON.stringify(layout)) } catch {} }

const projectLabel = computed(() => {
  const project = projects.value.find(item => item.code === currentProject.value)
  return project ? `${project.name}（${project.code}）` : '选择项目'
})
const filteredProjects = computed(() => {
  const key = projectSearch.value.trim().toLowerCase()
  if (!key) return projects.value
  return projects.value.filter(item => item.name.toLowerCase().includes(key) || item.code.toLowerCase().includes(key))
})
const projectCode = () => currentProject.value

const summaryCards = computed(() => {
  const data = summaryData.value
  if (!data) return []
  const topWorkflow = data.workflowStatuses[0]
  const unresolved = (data.resolution || []).find(item => item.name === '否')
  return [
    { tone: 'blue', label: '问题总数', value: data.total },
    { tone: 'green', label: '有 Q 单号', value: data.withTicket },
    { tone: 'amber', label: '最多变动状态', value: topWorkflow ? `${topWorkflow.name} · ${topWorkflow.count}` : '暂无' },
    { tone: 'red', label: '未解决', value: unresolved?.count || 0 }
  ]
})
const toolbarHints = computed(() => {
  const hints = []
  if (sort.key) hints.push(`排序：${COLUMNS.find(c => c.key === sort.key)?.label || sort.key} ${sort.dir === 'asc' ? '升序' : '降序'}`)
  const count = Object.keys(activeFilters).length
  if (count) hints.push(`筛选 ${count} 列`)
  return hints.join(' · ')
})
const testStatusOptions = computed(() => [...new Set([...TEST_STATUS_PRESETS, ...knownTestStatuses.value])])
const detailRows = computed(() => {
  const issue = detailIssue.value
  if (!issue) return []
  const labels = { ticketNumber: 'Q 单编号', testStatus: '测试状态', resolved: '是否解决', category: '类别', caseName: '个案书名', programName: '程序名称', owner: '负责顾问', developer: '开发人员', handler: '问题处理人', description: '问题描述', solutionNote: '解决描述', developmentNote: '开发说明', remark: '备注', raisedDate: '提单日', completedDate: '完成日', sourceFileName: '来源文件', sourceSheet: '来源工作表', sourceRow: '来源行号', updatedBy: '最后修改人', updatedAtUtc: '更新时间' }
  const rows = Object.entries(labels).map(([key, label]) => [label, issue[key] || '—'])
  rows.push(['变动状态', issue.workflowStatus || '未标记'])
  rows.push(['变动状态时间', issue.workflowStatusChangedAtUtc ? new Date(issue.workflowStatusChangedAtUtc).toLocaleString('zh-CN', { hour12: false }) : '—'])
  for (const [key, value] of Object.entries(issue.extraFields || {})) rows.push([key, value])
  return rows
})

function same(a, b) { return String(a ?? '').trim().toLowerCase() === String(b ?? '').trim().toLowerCase() }
async function call(name, args = {}) {
  const response = await fetch(ROOT + name, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(args) })
  let body = {}
  try { body = await response.json() } catch {}
  if (!response.ok) throw new Error(body.message || body.detail || `调用失败（${response.status}）`)
  return body
}
function statusTagType(value) {
  const text = (value || '').trim()
  if (/完成|通过|关闭|修复/.test(text)) return 'success'
  if (/处理中|进行|开发|待/.test(text)) return 'warning'
  return 'info'
}
function workflowTagType(value) { return statusTagType(value) }
function workflowTitle(row) {
  return row.workflowStatusChangedAtUtc ? `变动时间 ${new Date(row.workflowStatusChangedAtUtc).toLocaleString('zh-CN', { hour12: false })}` : '点击标记变动状态'
}

async function loadProjects() {
  const result = await call('list_projects')
  projects.value = result.items
  if (!projects.value.some(item => item.code === currentProject.value)) currentProject.value = projects.value[0]?.code || ''
  if (currentProject.value) await load()
}

async function load() {
  if (!currentProject.value) return
  loading.value = true
  try {
    const args = { projectCode: currentProject.value, keyword: keyword.value.trim(), status: filterStatus.value, workflowStatus: filterWorkflow.value, page: page.value, pageSize: pageSize.value }
    const filters = {}
    for (const [key, value] of Object.entries(activeFilters)) if (value.values?.length) filters[key] = value.values
    if (Object.keys(filters).length) args.filters = filters
    if (sort.key) { args.sortBy = sort.key; args.sortDir = sort.dir }
    const result = await call('list_issues', args)
    issues.value = result.items
    total.value = result.total
    await loadSummary()
  } catch (error) { ElMessage.error(error.message) } finally { loading.value = false }
}

async function loadSummary() {
  if (!currentProject.value) return
  summaryData.value = await call('summarize_issues', { projectCode: currentProject.value })
  knownTestStatuses.value = (summaryData.value.statuses || []).filter(item => item.name !== '未标记').map(item => item.name)
}

function search() { page.value = 1; load() }
function onSizeChange() { page.value = 1; load() }
function onSortChange({ prop, order }) {
  if (!order) { sort.key = ''; sort.dir = 'desc' } else { sort.key = prop; sort.dir = order === 'ascending' ? 'asc' : 'desc' }
  page.value = 1
  load()
}
function onFilterChange(payload) {
  for (const [key, values] of Object.entries(payload || {})) {
    if (Array.isArray(values) && values.length) activeFilters[key] = { values }
    else delete activeFilters[key]
  }
  page.value = 1
  load()
}

/* 列表内直接编辑状态与日期 */
function enumFilters(field) {
  if (field === 'testStatus') return [...new Set([...TEST_STATUS_PRESETS, ...knownTestStatuses.value])].map(value => ({ text: value, value }))
  if (field === 'workflowStatus') return WORKFLOW_OPTIONS.map(value => ({ text: value, value }))
  if (field === 'resolved') return [{ text: '是', value: '是' }, { text: '否', value: '否' }]
  if (field === 'category') return (summaryData.value?.categories || []).filter(item => item.name !== '未标记').map(item => ({ text: item.name, value: item.name }))
  return undefined
}
const editingCell = reactive({ id: '', field: '' })
function startEdit(row, field) { editingCell.id = row.id; editingCell.field = field }
function isEditing(row, field) { return editingCell.id === row.id && editingCell.field === field }
function cancelDateEdit() { editingCell.id = ''; editingCell.field = '' }
function editOptions(field, row) {
  const current = row[field] || ''
  let base = field === 'workflowStatus' ? WORKFLOW_OPTIONS : field === 'resolved' ? ['是', '否'] : field === 'testStatus' ? [...TEST_STATUS_PRESETS, ...knownTestStatuses.value] : []
  const values = [...new Set(base)]
  if (current && !values.some(value => value.toLowerCase() === current.toLowerCase())) values.unshift(current)
  return values
}
async function applyInline(row, field, value) {
  if (same(value, row[field])) return
  try {
    await call('save_issue', { projectCode: currentProject.value, issue: { ...row, [field]: value } })
    if (field === 'workflowStatus' && value) ElMessage.success(`${row.ticketNumber || '问题单'} 变动状态：${value}（变动时间已记录）`)
    await load()
  } catch (error) { ElMessage.error(error.message); await load() }
}

/* 列宽拖拽 */
function startResize(event, column) {
  event.preventDefault()
  event.stopPropagation()
  const startX = event.clientX
  const startWidth = columnWidth(column.key)
  const resizer = event.target
  resizer.classList?.add('dragging')
  const move = e => {
    columnState[column.key] = { ...columnState[column.key], width: Math.min(520, Math.max(56, startWidth + e.clientX - startX)) }
  }
  const up = () => {
    document.removeEventListener('mousemove', move)
    document.removeEventListener('mouseup', up)
    resizer.classList?.remove('dragging')
    saveColumnState()
  }
  document.addEventListener('mousemove', move)
  document.addEventListener('mouseup', up)
}

/* 编辑弹窗 */
function openEditor(issue) {
  editorIssue.value = issue
  const keys = ['ticketNumber', 'caseName', 'programName', 'category', 'testStatus', 'workflowStatus', 'resolved', 'raisedDate', 'completedDate', 'owner', 'developer', 'handler', 'description', 'solutionNote', 'developmentNote', 'remark', 'sourceUrl']
  for (const key of keys) editorForm[key] = issue?.[key] ?? ''
  editorVisible.value = true
}
async function saveEditor() {
  try {
    const result = await call('save_issue', { projectCode: currentProject.value, issue: { ...(editorIssue.value || {}), ...editorForm } })
    editorVisible.value = false
    ElMessage.success(`已保存 ${result.ticketNumber || result.caseName || '问题单'}${result.workflowStatusChangedAtUtc ? '（变动状态时间已记录）' : ''}`)
    await load()
  } catch (error) { ElMessage.error(error.message) }
}
async function removeIssue(issue) {
  try {
    await ElMessageBox.confirm(`确定删除 ${issue.ticketNumber || issue.caseName || '此问题单'}？`, '删除确认', { type: 'warning' })
  } catch { return }
  try {
    await call('delete_issue', { projectCode: currentProject.value, id: issue.id })
    ElMessage.success('已删除')
    await load()
  } catch (error) { ElMessage.error(error.message) }
}

/* 导入 */
function onImportFileChange(event) { importFile = event.target.files[0] }
async function doImport() {
  if (!importFile && !importText.value.trim()) { ElMessage.warning('请选择文件或粘贴表格内容'); return }
  if (importFile && importFile.size > 5 * 1024 * 1024) { ElMessage.warning('文件不能超过 5 MB'); return }
  try {
    const args = { projectCode: currentProject.value, sourceUrl: importSourceUrl.value.trim() }
    if (importFile) {
      args.fileName = importFile.name
      args.base64 = await new Promise((resolve, reject) => {
        const reader = new FileReader()
        reader.onload = () => resolve(String(reader.result).split(',')[1])
        reader.onerror = reject
        reader.readAsDataURL(importFile)
      })
    } else {
      args.fileName = '在线文档粘贴.tsv'
      args.text = importText.value
    }
    importing.value = true
    const result = await call('import_issues', args)
    importVisible.value = false
    ElMessage.success(`解析 ${result.parsed} 条，新增 ${result.created} 条，更新 ${result.updated} 条，跳过 ${result.skipped} 行`)
    page.value = 1
    await load()
  } catch (error) { ElMessage.error(error.message) } finally { importing.value = false }
}

/* 日报 */
async function buildReport() {
  const args = { date: reportDate.value }
  if (!reportAll.value) args.projectCode = currentProject.value
  const result = await call('daily_report', args)
  const statusLine = (result.byStatus || []).map(item => `${item.name} ${item.count} 条`).join('，')
  const projectLine = result.byProject.length > 1 ? result.byProject.map(item => `${item.name} ${item.count} 条`).join('，') : ''
  reportSummary.value = `${result.date} · ${result.scope} 共 ${result.total} 条变动${statusLine ? `（${statusLine}）` : ''}`
  const lines = [`【${result.date}】${result.scope}问题单变动日报（${result.total} 条）`]
  if (projectLine) lines.push(`项目分布：${projectLine}`)
  if (result.total === 0) lines.push('当天无变动状态变化的问题单。')
  result.items.forEach((item, index) => {
    const title = result.scope === '全部项目' ? `[${item.projectName}] ` : ''
    lines.push(`${index + 1}. ${title}${item.ticketNumber || '（无单号）'}｜${item.caseName || item.programName || ''}`)
    lines.push(`   变动状态：${item.workflowStatus || '（空）'}（${item.changedAtLocal}）`)
    if (item.solutionNote) lines.push(`   解决描述：${item.solutionNote}`)
  })
  reportText.value = lines.join('\n')
}
async function openReport() {
  reportDate.value = new Date().toLocaleDateString('sv-SE')
  try { await buildReport(); reportVisible.value = true } catch (error) { ElMessage.error(error.message) }
}
async function copyReport() {
  try { await navigator.clipboard.writeText(reportText.value); ElMessage.success('日报内容已复制') } catch { ElMessage.warning('复制失败，请手动选择文本复制') }
}

/* 项目弹窗 */
function pickProject(project) {
  currentProject.value = project.code
  projectPickerVisible.value = false
  page.value = 1
  load()
}

const tableWrap = ref(null)
const tableHeight = ref(360)
let resizeObserver = null
onMounted(async () => {
  resizeObserver = new ResizeObserver(entries => {
    for (const entry of entries) tableHeight.value = Math.max(120, Math.floor(entry.contentRect.height))
  })
  if (tableWrap.value) resizeObserver.observe(tableWrap.value)
  try { await loadProjects() } catch (error) { ElMessage.error(error.message) }
})
</script>

<style scoped>
.page { height: 100%; display: flex; flex-direction: column; gap: 12px; padding: 16px 24px 12px; }
.top { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex: 0 0 auto; }
.top h1 { font-size: 20px; margin: 0 0 3px; color: #172b4d; }
.top-actions { display: flex; align-items: center; gap: 8px; }
.muted { color: #778399; font-size: 12px; }
.card { background: #fff; border: 1px solid #e2e8f0; border-radius: 12px; box-shadow: 0 4px 18px rgba(15, 39, 69, .05); }
.filters { padding: 14px 18px; display: flex; gap: 12px; align-items: flex-end; flex-wrap: wrap; flex: 0 0 auto; }
.field { display: flex; flex-direction: column; gap: 6px; min-width: 150px; }
.field label { color: #627087; font-size: 12px; }
.grow { flex: 1; }
.summary { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px; flex: 0 0 auto; }
.summary .card { padding: 12px 16px; min-width: 0; border-left-width: 3px; border-left-style: solid; }
.summary strong { display: block; margin-top: 6px; font-size: 19px; color: #172b4d; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.summary .muted { display: block; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.table-card { flex: 1 1 auto; display: flex; flex-direction: column; min-height: 0; }
.table-wrap { flex: 1 1 auto; min-height: 120px; overflow: hidden; }
.table-toolbar { display: flex; justify-content: space-between; align-items: center; gap: 10px; padding: 8px 16px; border-bottom: 1px solid #e2e8f0; flex: 0 0 auto; }
.toolbar-hints { display: flex; align-items: center; gap: 10px; min-width: 0; overflow: hidden; }
.pager { display: flex; justify-content: flex-end; padding: 10px 16px 4px; flex: 0 0 auto; }
.form-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 16px; }
.span2 { grid-column: span 2; }
.project-btn { width: 100%; justify-content: space-between; }
.project-btn-text { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: #172b4d; }
.hint { font-size: 12px; line-height: 1.7; color: #627087; }
.report-bar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; gap: 8px; flex-wrap: wrap; }
.th-wrap { display: flex; align-items: center; min-width: 0; padding-right: 6px; }
.th-label { overflow: hidden; text-overflow: ellipsis; }
@media (max-width: 700px) {
  .page { padding: 12px; overflow: auto; }
  .top { align-items: flex-start; flex-direction: column; }
  .top-actions { flex-wrap: wrap; }
  .form-grid { grid-template-columns: 1fr; }
  .summary { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
</style>
