<template>
  <div>
    <div v-if="loading" class="auth-wrap"><div class="loading-spinner" aria-label="加载中" /></div>

    <div v-else-if="needsSetup" class="auth-wrap">
      <el-card class="auth-card" shadow="always">
        <h2 class="auth-title">初始化 AiDataGateway</h2>
        <p class="muted">创建首个管理员，并生成一组仅显示一次的本地 AI OAuth2 客户端凭据。</p>
        <el-form ref="setupForm" :model="setup" :rules="setupRules" label-position="top" @submit.prevent="completeSetup">
          <el-form-item label="用户名" prop="userName"><el-input v-model="setup.userName" autocomplete="username" /></el-form-item>
          <el-form-item label="邮箱" prop="email"><el-input v-model="setup.email" autocomplete="email" /></el-form-item>
          <el-form-item label="显示名称" prop="displayName"><el-input v-model="setup.displayName" /></el-form-item>
          <el-form-item label="管理员密码" prop="password"><el-input v-model="setup.password" type="password" autocomplete="new-password" show-password /></el-form-item>
          <p class="form-hint">至少 10 位，并同时包含大写字母、小写字母和数字。</p>
          <el-button native-type="submit" type="primary" style="width:100%" :loading="saving">完成初始化</el-button>
        </el-form>
      </el-card>
    </div>

    <div v-else-if="!user" class="auth-wrap">
      <el-card class="auth-card" shadow="always">
        <h2 class="auth-title">登录</h2>
        <p class="muted">用户会话用于管理和人工审批；AI 使用独立 OAuth2 Client Credentials。</p>
        <el-form label-position="top" @keyup.enter="login">
          <el-form-item label="用户名"><el-input v-model="loginForm.userName" /></el-form-item>
          <el-form-item label="密码"><el-input v-model="loginForm.password" type="password" show-password /></el-form-item>
          <el-button type="primary" style="width:100%" :loading="saving" @click="login">登录</el-button>
        </el-form>
      </el-card>
    </div>

    <div v-else class="shell">
      <header class="header">
        <div><span class="brand">AiDataGateway</span><span class="subtitle">本地 AI 数据访问管控</span></div>
        <div>{{ user.displayName || user.userName }} · {{ user.roles.join(' / ') }} <el-button size="small" plain @click="logout">退出</el-button></div>
      </header>
      <main class="content">
        <el-alert v-if="generatedClient" class="credential-alert" title="请立即保存 OAuth2 客户端凭据，关闭后无法再次查看 Secret" type="warning" show-icon closable @close="generatedClient=null">
          <div class="credential-panel">
            <div class="credential-item">
              <span class="credential-label">Client ID</span>
              <code class="credential-value">{{ generatedClient.clientId }}</code>
            </div>
            <div class="credential-item">
              <span class="credential-label">Client Secret</span>
              <code class="credential-value">{{ generatedClient.clientSecret }}</code>
            </div>
          </div>
        </el-alert>

        <el-tabs v-model="activeTab" @tab-click="loadActiveTab">
          <el-tab-pane label="概览" name="overview">
            <div class="metric-grid">
              <el-card class="metric"><span>数据源</span><strong>{{ dataSources.length }}</strong></el-card>
              <el-card class="metric"><span>待审批</span><strong>{{ approvals.length }}</strong></el-card>
              <el-card class="metric"><span>用户</span><strong>{{ users.length }}</strong></el-card>
              <el-card class="metric"><span>OAuth 客户端</span><strong>{{ clients.length }}</strong></el-card>
            </div>
          </el-tab-pane>

          <el-tab-pane v-if="canOperate" label="数据源" name="datasources">
            <div class="toolbar"><h3>多数据库连接</h3><el-button type="primary" @click="openDataSource()">新增数据源</el-button></div>
            <el-table :data="dataSources" stripe>
              <el-table-column prop="name" label="名称" min-width="150" />
              <el-table-column prop="key" label="标识" min-width="140" />
              <el-table-column prop="provider" label="类型" width="100"><template #default="s">{{ providerName(s.row.provider) }}</template></el-table-column>
              <el-table-column label="地址" min-width="180"><template #default="s">{{ s.row.host }}:{{ s.row.port }}</template></el-table-column>
              <el-table-column prop="database" label="数据库" min-width="130" />
              <el-table-column prop="accessMode" label="模式" width="140"><template #default="s">{{ accessName(s.row.accessMode) }}</template></el-table-column>
              <el-table-column label="操作" width="220">
                <template #default="s">
                  <el-button size="small" @click="testDataSource(s.row)">测试</el-button>
                  <el-button size="small" @click="openDataSource(s.row)">编辑</el-button>
                  <el-button size="small" type="danger" @click="deleteDataSource(s.row)">删除</el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-tab-pane>

          <el-tab-pane v-if="canApprove" label="审批" name="approvals">
            <el-table :data="approvals" stripe>
              <el-table-column prop="requestedBy" label="发起者" width="150" />
              <el-table-column prop="sql" label="SQL" min-width="360" show-overflow-tooltip />
              <el-table-column prop="riskLevel" label="风险" width="100" />
              <el-table-column prop="createdAtUtc" label="时间" width="190" />
              <el-table-column label="操作" width="160"><template #default="s"><el-button size="small" type="success" @click="review(s.row,true)">批准</el-button><el-button size="small" type="danger" @click="review(s.row,false)">拒绝</el-button></template></el-table-column>
            </el-table>
          </el-tab-pane>

          <el-tab-pane v-if="isAdmin" label="用户" name="users">
            <div class="toolbar"><h3>用户与角色</h3><el-button type="primary" @click="userDialog=true">新增用户</el-button></div>
            <el-table :data="users" stripe>
              <el-table-column prop="userName" label="用户名" />
              <el-table-column prop="displayName" label="显示名称" />
              <el-table-column prop="email" label="邮箱" />
              <el-table-column label="角色"><template #default="s">{{ s.row.roles.join(', ') }}</template></el-table-column>
              <el-table-column label="状态" width="100"><template #default="s"><el-tag :type="s.row.isEnabled?'success':'danger'">{{ s.row.isEnabled?'启用':'禁用' }}</el-tag></template></el-table-column>
            </el-table>
          </el-tab-pane>

          <el-tab-pane v-if="isAdmin" label="OAuth 客户端" name="clients">
            <div class="toolbar"><h3>AI 客户端凭据</h3><el-button type="primary" @click="createClient">创建客户端</el-button></div>
            <el-table :data="clients" stripe>
              <el-table-column prop="displayName" label="名称" />
              <el-table-column prop="clientId" label="Client ID" min-width="280" />
              <el-table-column label="权限" min-width="320"><template #default="s">{{ s.row.permissions.join(', ') }}</template></el-table-column>
            </el-table>
          </el-tab-pane>
        </el-tabs>
      </main>

      <el-dialog v-model="dataSourceDialog" title="数据源" width="620px">
        <el-form :model="dataSourceForm" label-width="110px">
          <el-form-item label="标识"><el-input v-model="dataSourceForm.key" /></el-form-item>
          <el-form-item label="名称"><el-input v-model="dataSourceForm.name" /></el-form-item>
          <el-form-item label="类型"><el-select v-model="dataSourceForm.provider" style="width:100%"><el-option v-for="p in providers" :key="p.value" :label="p.label" :value="p.value" /></el-select></el-form-item>
          <el-form-item label="IP/主机"><el-input v-model="dataSourceForm.host" /></el-form-item>
          <el-form-item label="端口"><el-input-number v-model="dataSourceForm.port" :min="1" :max="65535" /></el-form-item>
          <el-form-item label="数据库"><el-input v-model="dataSourceForm.database" /></el-form-item>
          <el-form-item label="用户名"><el-input v-model="dataSourceForm.username" /></el-form-item>
          <el-form-item label="密码"><el-input v-model="dataSourceForm.password" type="password" show-password :placeholder="editingDataSource?'留空表示不修改':''" /></el-form-item>
          <el-form-item label="访问模式"><el-select v-model="dataSourceForm.accessMode" style="width:100%"><el-option v-for="m in accessModes" :key="m.value" :label="m.label" :value="m.value" /></el-select></el-form-item>
          <el-form-item label="最大返回行"><el-input-number v-model="dataSourceForm.maxRows" :min="1" :max="10000" /></el-form-item>
          <el-form-item label="超时秒数"><el-input-number v-model="dataSourceForm.commandTimeoutSeconds" :min="1" :max="300" /></el-form-item>
        </el-form>
        <template #footer><span><el-button @click="dataSourceDialog=false">取消</el-button><el-button type="primary" :loading="saving" @click="saveDataSource">保存</el-button></span></template>
      </el-dialog>

      <el-dialog v-model="userDialog" title="新增用户" width="560px">
        <el-form :model="newUser" label-width="100px">
          <el-form-item label="用户名"><el-input v-model="newUser.userName" /></el-form-item>
          <el-form-item label="显示名称"><el-input v-model="newUser.displayName" /></el-form-item>
          <el-form-item label="邮箱"><el-input v-model="newUser.email" /></el-form-item>
          <el-form-item label="密码"><el-input v-model="newUser.password" type="password" show-password /></el-form-item>
          <el-form-item label="角色"><el-select v-model="newUser.roles" multiple style="width:100%"><el-option v-for="r in roles" :key="r" :label="r" :value="r" /></el-select></el-form-item>
        </el-form>
        <template #footer><span><el-button @click="userDialog=false">取消</el-button><el-button type="primary" @click="createUser">创建</el-button></span></template>
      </el-dialog>
    </div>
  </div>
</template>

<script>
import axios from 'axios'
import { ElMessage, ElMessageBox } from 'element-plus'

axios.defaults.withCredentials = true

export default {
  data: () => ({
    loading: true, saving: false, needsSetup: false, user: null, activeTab: 'overview', generatedClient: null,
    setup: { userName: 'admin', email: '', displayName: '管理员', password: '', aiClientName: 'Local AI Client' },
    setupRules: {
      userName: [
        { required: true, message: '请输入用户名', trigger: 'blur' },
        { pattern: /^[a-zA-Z0-9._@+-]+$/, message: '用户名只能包含英文字母、数字及 . - _ @ +', trigger: 'blur' }
      ],
      email: [
        { required: true, message: '请输入邮箱地址', trigger: 'blur' },
        { type: 'email', message: '请输入有效的邮箱地址', trigger: ['blur', 'change'] }
      ],
      displayName: [{ required: true, message: '请输入显示名称', trigger: 'blur' }],
      password: [
        { required: true, message: '请输入管理员密码', trigger: 'blur' },
        { min: 10, message: '密码长度至少为 10 位', trigger: 'blur' },
        { pattern: /[a-z]/, message: '密码必须包含小写字母', trigger: 'blur' },
        { pattern: /[A-Z]/, message: '密码必须包含大写字母', trigger: 'blur' },
        { pattern: /\d/, message: '密码必须包含数字', trigger: 'blur' }
      ]
    },
    loginForm: { userName: 'admin', password: '', rememberMe: true },
    dataSources: [], approvals: [], users: [], clients: [], roles: [],
    dataSourceDialog: false, editingDataSource: null,
    dataSourceForm: {}, userDialog: false,
    newUser: { userName: '', email: '', displayName: '', password: '', roles: ['Developer'] },
    providers: [{ value: 1, label: 'SQL Server', port: 1433 }, { value: 2, label: 'MySQL', port: 3306 }, { value: 3, label: 'PostgreSQL', port: 5432 }, { value: 4, label: 'SQLite', port: 1 }],
    accessModes: [{ value: 0, label: '禁用' }, { value: 1, label: '只读' }, { value: 2, label: '写入需审批' }, { value: 3, label: '开发模式' }]
  }),
  computed: {
    isAdmin () { return this.user?.roles?.includes('Administrator') },
    canOperate () { return this.isAdmin || this.user?.roles?.includes('Operator') },
    canApprove () { return this.isAdmin || this.user?.roles?.includes('Approver') }
  },
  async created () { await this.bootstrap() },
  methods: {
    async bootstrap () {
      try {
        const setup = await axios.get('/api/setup/status')
        this.needsSetup = setup.data.needsSetup
        if (!this.needsSetup) {
          try { this.user = (await axios.get('/api/auth/me')).data; await this.loadOverview() } catch (_) { this.user = null }
        }
      } finally { this.loading = false }
    },
    error (e) {
      const data = e.response?.data
      const validationMessages = data?.errors ? Object.values(data.errors).flat().filter(Boolean) : []
      ElMessage.error(data?.message || validationMessages.join('；') || data?.detail || e.message || '操作失败')
    },
    async completeSetup () {
      try { await this.$refs.setupForm.validate() } catch (_) { return }
      this.saving = true
      try { this.generatedClient = (await axios.post('/api/setup', this.setup)).data; this.needsSetup = false; ElMessage.success('初始化完成，请登录') } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async login () {
      this.saving = true
      try { this.user = (await axios.post('/api/auth/login', this.loginForm)).data; await this.loadOverview() } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async logout () { await axios.post('/api/auth/logout'); this.user = null },
    async loadOverview () {
      const jobs = []
      if (this.canOperate) jobs.push(this.loadDataSources())
      if (this.canApprove) jobs.push(this.loadApprovals())
      if (this.isAdmin) jobs.push(this.loadUsers(), this.loadClients())
      await Promise.all(jobs)
    },
    async loadActiveTab () {
      if (this.activeTab === 'datasources') await this.loadDataSources()
      if (this.activeTab === 'approvals') await this.loadApprovals()
      if (this.activeTab === 'users') await this.loadUsers()
      if (this.activeTab === 'clients') await this.loadClients()
    },
    async loadDataSources () { this.dataSources = (await axios.get('/api/admin/datasources')).data },
    async loadApprovals () { this.approvals = (await axios.get('/api/approvals/pending')).data },
    async loadUsers () { const [u, r] = await Promise.all([axios.get('/api/admin/users'), axios.get('/api/admin/roles')]); this.users = u.data; this.roles = r.data },
    async loadClients () { this.clients = (await axios.get('/api/admin/oauth-clients')).data },
    providerName (value) { return this.providers.find(p => p.value === value)?.label || value },
    accessName (value) { return this.accessModes.find(m => m.value === value)?.label || value },
    openDataSource (row) {
      this.editingDataSource = row || null
      this.dataSourceForm = row ? { ...row, password: '' } : { key: '', name: '', provider: 1, host: '127.0.0.1', port: 1433, database: '', username: '', password: '', accessMode: 1, maxRows: 1000, commandTimeoutSeconds: 30, enabled: true }
      this.dataSourceDialog = true
    },
    async saveDataSource () {
      this.saving = true
      try {
        if (this.editingDataSource) await axios.put(`/api/admin/datasources/${this.editingDataSource.id}`, this.dataSourceForm)
        else await axios.post('/api/admin/datasources', this.dataSourceForm)
        this.dataSourceDialog = false; await this.loadDataSources(); ElMessage.success('已保存')
      } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async testDataSource (row) { try { const r = await axios.post(`/api/admin/datasources/${row.id}/test`); ElMessage({ type: r.data.success ? 'success' : 'error', message: r.data.message }) } catch (e) { this.error(e) } },
    async deleteDataSource (row) { try { await ElMessageBox.confirm(`确定删除 ${row.name}？`); await axios.delete(`/api/admin/datasources/${row.id}`); await this.loadDataSources() } catch (e) { if (!this.isCanceled(e)) this.error(e) } },
    async review (row, approved) { try { await axios.post(`/api/approvals/${row.id}/review`, { approved, comment: '' }); await this.loadApprovals(); ElMessage.success(approved ? '已批准并执行' : '已拒绝') } catch (e) { this.error(e) } },
    async createUser () { try { await axios.post('/api/admin/users', this.newUser); this.userDialog = false; await this.loadUsers(); ElMessage.success('用户已创建') } catch (e) { this.error(e) } },
    async createClient () {
      try {
        const { value } = await ElMessageBox.prompt('客户端名称', '创建 OAuth2 客户端', { inputValue: 'Local AI Client' })
        this.generatedClient = (await axios.post('/api/admin/oauth-clients', { displayName: value })).data
        await this.loadClients()
      } catch (e) { if (!this.isCanceled(e)) this.error(e) }
    },
    isCanceled (e) { return e === 'cancel' || e === 'close' || e?.message === 'cancel' || e?.message === 'close' }
  }
}
</script>
