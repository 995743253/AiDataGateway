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
          <el-button native-type="submit" type="primary" class="full-button" :loading="saving">完成初始化</el-button>
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
          <el-button type="primary" class="full-button" :loading="saving" @click="login">登录</el-button>
        </el-form>
      </el-card>
    </div>

    <div v-else class="shell">
      <header class="header">
        <button class="brand-button" type="button" @click="goTo('overview')">
          <span class="brand">AiDataGateway</span><span class="subtitle">本地 AI 数据访问管控</span>
        </button>
        <div class="header-actions">
          <span class="live-state" :class="{ online: eventConnected }"><span class="live-dot" />{{ eventConnected ? '实时同步' : '正在连接' }}</span>
          <el-dropdown trigger="click" @command="handleUserCommand">
            <button class="user-menu-trigger" type="button">
              <el-avatar :size="36">{{ userInitials }}</el-avatar>
              <span class="user-summary"><strong>{{ user.displayName || user.userName }}</strong><small>{{ user.roles.join(' / ') }}</small></span>
              <span class="dropdown-caret">⌄</span>
            </button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item v-if="isAdmin" command="settings">系统设置</el-dropdown-item>
                <el-dropdown-item v-if="isAdmin" command="users">用户管理</el-dropdown-item>
                <el-dropdown-item v-if="isAdmin" command="clients">OAuth2 客户端</el-dropdown-item>
                <el-dropdown-item divided command="logout">退出登录</el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </header>

      <main class="content">
        <el-alert v-if="generatedClient" class="credential-alert" title="请立即保存 OAuth2 客户端凭据，关闭后无法再次查看 Secret" type="warning" show-icon closable @close="generatedClient=null">
          <div class="credential-panel">
            <div class="credential-item"><span class="credential-label">Client ID</span><code class="credential-value">{{ generatedClient.clientId }}</code></div>
            <div class="credential-item"><span class="credential-label">Client Secret</span><code class="credential-value">{{ generatedClient.clientSecret }}</code></div>
          </div>
        </el-alert>

        <el-tabs v-if="isMainPage" v-model="activeTab" class="main-tabs" @tab-change="loadActiveTab">
          <el-tab-pane label="概览" name="overview">
            <div class="metric-grid">
              <el-card class="metric metric-link" :class="{ 'metric-disabled': !canOperate }" shadow="hover" role="button" :tabindex="canOperate ? 0 : -1" @click="openDataSourcePage" @keyup.enter="openDataSourcePage"><span>数据源</span><strong>{{ canOperate ? dataSources.length : '—' }}</strong><small>{{ canOperate ? '进入数据源管理 →' : '无查看权限' }}</small></el-card>
              <el-card class="metric metric-link warning" :class="{ 'metric-disabled': !canApprove }" shadow="hover" role="button" :tabindex="canApprove ? 0 : -1" @click="openApprovalPage('Pending')" @keyup.enter="openApprovalPage('Pending')"><span>待审批</span><strong>{{ canApprove ? pendingApprovalTotal : '—' }}</strong><small>{{ canApprove ? '查看待审批工单 →' : '无查看权限' }}</small></el-card>
              <el-card class="metric metric-link" :class="{ 'metric-disabled': !canApprove }" shadow="hover" role="button" :tabindex="canApprove ? 0 : -1" @click="openApprovalPage('all')" @keyup.enter="openApprovalPage('all')"><span>审批记录</span><strong>{{ canApprove ? approvalAllTotal : '—' }}</strong><small>{{ canApprove ? '查询审批历史 →' : '无查看权限' }}</small></el-card>
              <el-card class="metric metric-link success" :class="{ 'metric-disabled': !canViewLogs }" shadow="hover" role="button" :tabindex="canViewLogs ? 0 : -1" @click="openLogPage" @keyup.enter="openLogPage"><span>运行日志</span><strong>{{ canViewLogs ? auditLogAllTotal : '—' }}</strong><small>{{ canViewLogs ? '查询调用明细 →' : '无查看权限' }}</small></el-card>
            </div>
            <el-card class="overview-card" shadow="never">
              <div class="overview-heading">
                <div><h3>本地网关运行状态</h3><p>管理页面通过服务端事件接收数据变更，不使用定时轮询。</p></div>
                <el-tag :type="eventConnected ? 'success' : 'warning'">{{ eventConnected ? 'SSE 已连接' : 'SSE 重连中' }}</el-tag>
              </div>
            </el-card>
          </el-tab-pane>

          <el-tab-pane v-if="canOperate" label="数据源" name="datasources">
            <div class="toolbar"><div><h3>多数据库连接</h3><p>管理 AI 可以访问的数据库及审批模式。</p></div><el-button type="primary" @click="openDataSource()">新增数据源</el-button></div>
            <el-table :data="dataSources" stripe>
              <el-table-column prop="name" label="名称" min-width="150" />
              <el-table-column prop="key" label="标识" min-width="140" />
              <el-table-column prop="provider" label="类型" width="110"><template #default="s">{{ providerName(s.row.provider) }}</template></el-table-column>
              <el-table-column label="地址" min-width="180"><template #default="s">{{ s.row.host }}:{{ s.row.port }}</template></el-table-column>
              <el-table-column prop="database" label="数据库" min-width="150" show-overflow-tooltip />
              <el-table-column prop="accessMode" label="模式" width="140"><template #default="s">{{ accessName(s.row.accessMode) }}</template></el-table-column>
              <el-table-column label="表黑名单" width="110"><template #default="s"><el-tag v-if="s.row.blockedTables?.length" type="danger" effect="plain">{{ s.row.blockedTables.length }} 张表</el-tag><span v-else>—</span></template></el-table-column>
              <el-table-column label="操作" width="220"><template #default="s"><el-button size="small" @click="testDataSource(s.row)">测试</el-button><el-button size="small" @click="openDataSource(s.row)">编辑</el-button><el-button size="small" type="danger" @click="deleteDataSource(s.row)">删除</el-button></template></el-table-column>
            </el-table>
          </el-tab-pane>

          <el-tab-pane v-if="canApprove" label="审批记录" name="approvals">
            <div class="toolbar">
              <div><h3>SQL 审批记录</h3><p>同时查看待审批、已通过、已拒绝和执行失败的完整历史。</p></div>
              <div class="toolbar-actions">
                <el-input v-model="approvalKeyword" class="log-search" clearable placeholder="搜索 SQL、发起者、审批者或意见" @keyup.enter="searchApprovals" />
                <el-select v-model="approvalFilter" class="status-filter" @change="searchApprovals"><el-option label="全部状态" value="all" /><el-option label="待审批" value="Pending" /><el-option label="执行成功" value="Succeeded" /><el-option label="已拒绝" value="Rejected" /><el-option label="执行失败" value="Failed" /><el-option label="已过期" value="Expired" /></el-select>
                <el-button type="primary" @click="searchApprovals">查询</el-button><el-button @click="resetApprovalSearch">重置</el-button>
              </div>
            </div>
            <el-table :data="approvals" stripe border max-height="calc(100vh - 360px)" class="paged-table" @row-dblclick="openApproval">
              <el-table-column prop="dataSourceName" label="数据源" min-width="150"><template #default="s">{{ s.row.dataSourceName || shortId(s.row.dataSourceId) }}</template></el-table-column>
              <el-table-column prop="requestedBy" label="发起者" min-width="150" />
              <el-table-column prop="status" label="状态" width="110"><template #default="s"><el-tag :type="approvalStatusType(s.row.status)">{{ approvalStatusName(s.row.status) }}</el-tag></template></el-table-column>
              <el-table-column prop="riskLevel" label="风险" width="90"><template #default="s"><el-tag :type="riskType(s.row.riskLevel)" effect="plain">{{ s.row.riskLevel }}</el-tag></template></el-table-column>
              <el-table-column prop="sql" label="SQL 摘要" min-width="300" show-overflow-tooltip />
              <el-table-column prop="createdAtUtc" label="提交时间" width="180"><template #default="s">{{ formatDate(s.row.createdAtUtc) }}</template></el-table-column>
              <el-table-column label="操作" width="110"><template #default="s"><el-button size="small" type="primary" plain @click="openApproval(s.row)">查看详情</el-button></template></el-table-column>
            </el-table>
            <el-empty v-if="approvals.length === 0" description="暂无审批记录" />
            <div class="pagination-panel">
              <span class="pagination-summary">第 {{ approvalTotal ? (approvalPage - 1) * approvalPageSize + 1 : 0 }}–{{ Math.min(approvalPage * approvalPageSize, approvalTotal) }} 条，共 {{ approvalTotal }} 条</span>
              <div class="pagination-controls">
                <span class="page-size-label">每页</span>
                <el-select v-model="approvalPageSize" class="page-size-select" @change="approvalSizeChanged"><el-option v-for="size in [10, 20, 50, 100]" :key="size" :label="`${size} 条`" :value="size" /></el-select>
                <el-button :disabled="approvalPage <= 1" @click="changeApprovalPage(approvalPage - 1)">上一页</el-button>
                <strong class="page-indicator">第 {{ approvalPage }} / {{ approvalPageCount }} 页</strong>
                <el-button type="primary" plain :disabled="approvalPage >= approvalPageCount" @click="changeApprovalPage(approvalPage + 1)">下一页</el-button>
              </div>
            </div>
          </el-tab-pane>

          <el-tab-pane v-if="canViewLogs" label="运行日志" name="logs">
            <div class="toolbar">
              <div><h3>调用与运行日志</h3><p>记录 AI 查询、变更提单、人工审批、数据源、用户及认证操作。</p></div>
              <div class="toolbar-actions"><el-input v-model="logKeyword" class="log-search" clearable placeholder="搜索人员、动作、SQL 或结果数据" @keyup.enter="searchAuditLogs" /><el-select v-model="logOutcome" class="status-filter" @change="searchAuditLogs"><el-option label="全部结果" value="" /><el-option label="成功" value="success" /><el-option label="失败" value="failure" /><el-option label="待处理" value="pending" /><el-option label="已拒绝" value="rejected" /></el-select><el-button type="primary" @click="searchAuditLogs">查询</el-button><el-button @click="resetLogSearch">重置</el-button></div>
            </div>
            <el-table :data="auditLogs" stripe border max-height="calc(100vh - 360px)" class="paged-table" @row-dblclick="openLog">
              <el-table-column prop="createdAtUtc" label="时间" width="180"><template #default="s">{{ formatDate(s.row.createdAtUtc) }}</template></el-table-column>
              <el-table-column prop="actor" label="调用者" min-width="150" />
              <el-table-column prop="action" label="动作" min-width="170"><template #default="s">{{ actionName(s.row.action) }}</template></el-table-column>
              <el-table-column prop="outcome" label="结果" width="100"><template #default="s"><el-tag :type="outcomeType(s.row.outcome)">{{ outcomeName(s.row.outcome) }}</el-tag></template></el-table-column>
              <el-table-column prop="dataSourceName" label="数据源" min-width="140"><template #default="s">{{ s.row.dataSourceName || (s.row.dataSourceId ? shortId(s.row.dataSourceId) : '—') }}</template></el-table-column>
              <el-table-column label="详情" min-width="320" show-overflow-tooltip><template #default="s">{{ logSummary(s.row) }}</template></el-table-column>
              <el-table-column label="操作" width="90"><template #default="s"><el-button size="small" link type="primary" @click="openLog(s.row)">完整数据</el-button></template></el-table-column>
            </el-table>
            <el-empty v-if="auditLogs.length === 0" description="暂无运行日志" />
            <div class="pagination-panel">
              <span class="pagination-summary">第 {{ auditLogTotal ? (auditLogPage - 1) * auditLogPageSize + 1 : 0 }}–{{ Math.min(auditLogPage * auditLogPageSize, auditLogTotal) }} 条，共 {{ auditLogTotal }} 条</span>
              <div class="pagination-controls">
                <span class="page-size-label">每页</span>
                <el-select v-model="auditLogPageSize" class="page-size-select" @change="auditLogSizeChanged"><el-option v-for="size in [10, 20, 50, 100]" :key="size" :label="`${size} 条`" :value="size" /></el-select>
                <el-button :disabled="auditLogPage <= 1" @click="changeAuditLogPage(auditLogPage - 1)">上一页</el-button>
                <strong class="page-indicator">第 {{ auditLogPage }} / {{ auditLogPageCount }} 页</strong>
                <el-button type="primary" plain :disabled="auditLogPage >= auditLogPageCount" @click="changeAuditLogPage(auditLogPage + 1)">下一页</el-button>
              </div>
            </div>
          </el-tab-pane>
        </el-tabs>

        <section v-else-if="activeTab === 'settings'" class="secondary-page">
          <div class="toolbar"><div><el-button link type="primary" @click="goTo('overview')">← 返回主页面</el-button><h2>系统设置</h2><p>配置网关自身数据的保存期限与自动清理计划。</p></div></div>
          <el-card class="settings-card" shadow="never">
            <template #header><div><strong>日志与记录清理</strong><p class="settings-subtitle">仅清理运行审计日志、审批历史和本地日志文件，不影响用户、数据源、OAuth2 客户端或业务数据库。</p></div></template>
            <el-form :model="maintenanceSettings" label-width="150px" class="settings-form">
              <el-form-item label="启用定期清理"><el-switch v-model="maintenanceSettings.cleanupEnabled" /><span class="inline-help">启用后每次软件启动立即清理一次，并在每日计划时间再次清理；关闭后仍可手动执行。</span></el-form-item>
              <el-form-item label="记录保留天数"><el-input-number v-model="maintenanceSettings.retentionDays" :min="1" :max="3650" /><span class="inline-help">默认保留最近 3 天，超过期限的数据将在下次任务中删除。</span></el-form-item>
              <el-form-item label="每日清理时间"><el-time-select v-model="maintenanceSettings.cleanupTimeLocal" start="00:00" step="00:30" end="23:30" placeholder="选择本地时间" :disabled="!maintenanceSettings.cleanupEnabled" /><span class="inline-help">使用当前 Windows 电脑的本地时间；错过该时间不影响下次启动时清理。</span></el-form-item>
              <el-divider />
              <el-descriptions :column="1" border>
                <el-descriptions-item label="上次清理时间">{{ formatDate(maintenanceSettings.lastCleanupAtUtc) }}</el-descriptions-item>
                <el-descriptions-item label="上次清理结果">{{ maintenanceSettings.lastCleanupSummary || '尚未执行' }}</el-descriptions-item>
              </el-descriptions>
              <div class="settings-actions"><el-button type="primary" :loading="saving" @click="saveMaintenanceSettings">保存设置</el-button><el-button :loading="saving" @click="cleanupNow">立即清理</el-button></div>
            </el-form>
          </el-card>
        </section>

        <section v-else-if="activeTab === 'users'" class="secondary-page">
          <div class="toolbar"><div><el-button link type="primary" @click="goTo('overview')">← 返回主页面</el-button><h2>用户管理</h2><p>管理本地后台账号、状态和角色；有历史记录的用户应禁用而不是删除。</p></div><el-button type="primary" @click="openUserDialog()">新增用户</el-button></div>
          <el-table :data="users" stripe>
            <el-table-column prop="userName" label="用户名" /><el-table-column prop="displayName" label="显示名称" /><el-table-column prop="email" label="邮箱" min-width="200" />
            <el-table-column label="角色" min-width="180"><template #default="s">{{ s.row.roles.join(', ') }}</template></el-table-column>
            <el-table-column label="状态" width="100"><template #default="s"><el-tag :type="s.row.isEnabled?'success':'danger'">{{ s.row.isEnabled?'启用':'禁用' }}</el-tag></template></el-table-column>
            <el-table-column label="操作" width="170"><template #default="s"><el-button size="small" @click="openUserDialog(s.row)">编辑</el-button><el-button size="small" type="danger" :disabled="s.row.id === user.id" @click="deleteUser(s.row)">删除</el-button></template></el-table-column>
          </el-table>
        </section>

        <section v-else-if="activeTab === 'clients'" class="secondary-page">
          <div class="toolbar"><div><el-button link type="primary" @click="goTo('overview')">← 返回主页面</el-button><h2>OAuth2 客户端</h2><p>管理本地 AI 使用的客户端身份和权限范围。</p></div><el-button type="primary" @click="createClient">创建客户端</el-button></div>
          <el-table :data="clients" stripe>
            <el-table-column prop="displayName" label="名称" min-width="180" /><el-table-column prop="clientId" label="Client ID" min-width="300" />
            <el-table-column label="权限" min-width="360"><template #default="s"><div class="permission-list"><el-tag v-for="permission in s.row.permissions" :key="permission" effect="plain">{{ permission }}</el-tag></div></template></el-table-column>
            <el-table-column label="操作" width="110"><template #default="s"><el-button size="small" type="danger" @click="deleteClient(s.row)">吊销删除</el-button></template></el-table-column>
          </el-table>
        </section>
      </main>

      <el-dialog v-model="dataSourceDialog" title="数据源" width="680px">
        <el-form :model="dataSourceForm" label-width="110px">
          <el-form-item label="标识"><el-input v-model="dataSourceForm.key" /></el-form-item><el-form-item label="名称"><el-input v-model="dataSourceForm.name" /></el-form-item>
          <el-form-item label="类型"><el-select v-model="dataSourceForm.provider" class="full-width"><el-option v-for="p in providers" :key="p.value" :label="p.label" :value="p.value" /></el-select></el-form-item>
          <el-form-item label="IP/主机"><el-input v-model="dataSourceForm.host" /></el-form-item><el-form-item label="端口"><el-input-number v-model="dataSourceForm.port" :min="1" :max="65535" /></el-form-item>
          <el-form-item label="数据库"><el-input v-model="dataSourceForm.database" /></el-form-item><el-form-item label="用户名"><el-input v-model="dataSourceForm.username" /></el-form-item>
          <el-form-item label="密码"><el-input v-model="dataSourceForm.password" type="password" show-password :placeholder="editingDataSource?'留空表示不修改':''" /></el-form-item>
          <el-form-item label="访问模式"><el-select v-model="dataSourceForm.accessMode" class="full-width"><el-option v-for="m in accessModes" :key="m.value" :label="m.label" :value="m.value" /></el-select></el-form-item>
          <el-form-item label="表黑名单"><div class="full-width"><el-input v-model="dataSourceForm.blockedTablesText" type="textarea" :rows="5" placeholder="每行一个表名，例如：&#10;AspNetUsers&#10;main.GatewayAuditEntries" /><p class="field-help">支持表名或 schema.table。命中黑名单的 FROM/JOIN 查询会在连接数据库前直接拦截。</p></div></el-form-item>
          <el-form-item label="最大返回行"><el-input-number v-model="dataSourceForm.maxRows" :min="1" :max="10000" /></el-form-item><el-form-item label="超时秒数"><el-input-number v-model="dataSourceForm.commandTimeoutSeconds" :min="1" :max="300" /></el-form-item>
        </el-form>
        <template #footer><el-button @click="dataSourceDialog=false">取消</el-button><el-button type="primary" :loading="saving" @click="saveDataSource">保存</el-button></template>
      </el-dialog>

      <el-dialog v-model="approvalDialog" title="SQL 审批详情" width="900px" class="detail-window approval-detail-window" draggable overflow destroy-on-close>
        <div v-if="selectedApproval" class="detail-dialog approval-detail-content" :class="{ 'has-review-form': selectedApproval.status === 'Pending' }">
          <el-descriptions :column="2" border>
            <el-descriptions-item label="工单 ID">{{ selectedApproval.id }}</el-descriptions-item><el-descriptions-item label="状态"><el-tag :type="approvalStatusType(selectedApproval.status)">{{ approvalStatusName(selectedApproval.status) }}</el-tag></el-descriptions-item>
            <el-descriptions-item label="数据源">{{ selectedApproval.dataSourceName || selectedApproval.dataSourceId }}</el-descriptions-item><el-descriptions-item label="风险等级">{{ selectedApproval.riskLevel }}</el-descriptions-item>
            <el-descriptions-item label="发起者">{{ selectedApproval.requestedBy }}</el-descriptions-item><el-descriptions-item label="审批者">{{ selectedApproval.reviewedBy || '—' }}</el-descriptions-item>
            <el-descriptions-item label="提交时间">{{ formatDate(selectedApproval.createdAtUtc) }}</el-descriptions-item><el-descriptions-item label="失效时间">{{ formatDate(selectedApproval.expiresAtUtc) }}</el-descriptions-item>
            <el-descriptions-item label="审批时间">{{ formatDate(selectedApproval.reviewedAtUtc) }}</el-descriptions-item><el-descriptions-item label="执行时间">{{ formatDate(selectedApproval.executedAtUtc) }}</el-descriptions-item>
            <el-descriptions-item label="审批意见" :span="2">{{ selectedApproval.reviewComment || '—' }}</el-descriptions-item>
            <el-descriptions-item v-if="selectedApproval.executionError" label="执行错误" :span="2"><span class="error-text">{{ selectedApproval.executionError }}</span></el-descriptions-item>
          </el-descriptions>
          <div class="detail-section approval-sql-section"><h4>完整 SQL</h4><pre class="sql-block">{{ selectedApproval.sql }}</pre></div>
          <el-form v-if="selectedApproval.status === 'Pending'" label-position="top" class="approval-review-form"><el-form-item label="审批意见（可选）" class="approval-review-item"><el-input v-model="reviewComment" type="textarea" maxlength="500" show-word-limit resize="none" class="approval-review-textarea" /></el-form-item></el-form>
        </div>
        <template #footer><el-button @click="approvalDialog=false">关闭</el-button><template v-if="selectedApproval?.status === 'Pending'"><el-button type="danger" :loading="saving" @click="reviewSelected(false)">拒绝</el-button><el-button type="success" :loading="saving" @click="reviewSelected(true)">批准并执行</el-button></template></template>
      </el-dialog>

      <el-dialog v-model="logDialog" title="运行日志完整数据" width="90%" class="detail-window log-dialog" draggable overflow>
        <div v-if="selectedLog" class="detail-dialog">
          <el-descriptions :column="2" border>
            <el-descriptions-item label="日志 ID">{{ selectedLog.id }}</el-descriptions-item><el-descriptions-item label="时间">{{ formatDate(selectedLog.createdAtUtc) }}</el-descriptions-item>
            <el-descriptions-item label="调用者">{{ selectedLog.actor }}</el-descriptions-item><el-descriptions-item label="动作">{{ actionName(selectedLog.action) }}（{{ selectedLog.action }}）</el-descriptions-item>
            <el-descriptions-item label="结果"><el-tag :type="outcomeType(selectedLog.outcome)">{{ outcomeName(selectedLog.outcome) }}</el-tag></el-descriptions-item><el-descriptions-item label="数据源">{{ selectedLog.dataSourceName || selectedLog.dataSourceId || '—' }}</el-descriptions-item>
            <el-descriptions-item v-if="selectedLogDetail.rowCount !== undefined" label="查询行数">{{ selectedLogDetail.rowCount }}{{ selectedLogDetail.truncated ? '（已达到返回上限）' : '' }}</el-descriptions-item>
            <el-descriptions-item v-if="selectedLogDetail.affectedRows !== undefined" label="影响行数">{{ selectedLogDetail.affectedRows }}</el-descriptions-item>
            <el-descriptions-item v-if="selectedLogDetail.error" label="错误信息" :span="2"><span class="error-text">{{ selectedLogDetail.error }}</span></el-descriptions-item>
          </el-descriptions>
          <div v-if="selectedLogDetail.sql" class="detail-section"><h4>入参 SQL</h4><pre class="sql-block">{{ selectedLogDetail.sql }}</pre></div>
          <div v-if="Array.isArray(selectedLogDetail.rows)" class="detail-section">
            <h4>查询结果（{{ selectedLogDetail.rows.length }} 行）</h4>
            <el-table :data="selectedLogDetail.rows" stripe border max-height="420" class="query-result-table">
              <el-table-column v-for="column in selectedLogColumns" :key="column" :label="column" min-width="150" show-overflow-tooltip><template #default="s">{{ formatCell(s.row[column]) }}</template></el-table-column>
            </el-table>
            <el-empty v-if="selectedLogDetail.rows.length === 0" description="查询结果为空" />
          </div>
          <div v-if="selectedLogDetail.raw" class="detail-section"><h4>历史日志详情</h4><pre class="log-detail">{{ selectedLogDetail.raw }}</pre></div>
        </div>
        <template #footer><el-button @click="logDialog=false">关闭</el-button></template>
      </el-dialog>

      <el-dialog v-model="userDialog" :title="editingUser ? '编辑用户' : '新增用户'" width="580px">
        <el-form :model="newUser" label-width="100px"><el-form-item label="用户名"><el-input v-model="newUser.userName" :disabled="!!editingUser" /></el-form-item><el-form-item label="显示名称"><el-input v-model="newUser.displayName" /></el-form-item><el-form-item label="邮箱"><el-input v-model="newUser.email" :disabled="!!editingUser" /></el-form-item><el-form-item v-if="!editingUser" label="密码"><el-input v-model="newUser.password" type="password" show-password /></el-form-item><el-form-item label="角色"><el-select v-model="newUser.roles" multiple class="full-width"><el-option v-for="r in roles" :key="r" :label="r" :value="r" /></el-select></el-form-item><el-form-item v-if="editingUser" label="账号状态"><el-switch v-model="newUser.enabled" active-text="启用" inactive-text="禁用" /></el-form-item></el-form>
        <template #footer><el-button @click="userDialog=false">取消</el-button><el-button type="primary" :loading="saving" @click="saveUser">{{ editingUser ? '保存' : '创建' }}</el-button></template>
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
    eventSource: null, eventConnected: false, eventRefreshTimer: null,
    setup: { userName: 'admin', email: '', displayName: '管理员', password: '', aiClientName: 'Local AI Client' },
    setupRules: {
      userName: [{ required: true, message: '请输入用户名', trigger: 'blur' }, { pattern: /^[a-zA-Z0-9._@+-]+$/, message: '用户名只能包含英文字母、数字及 . - _ @ +', trigger: 'blur' }],
      email: [{ required: true, message: '请输入邮箱地址', trigger: 'blur' }, { type: 'email', message: '请输入有效的邮箱地址', trigger: ['blur', 'change'] }],
      displayName: [{ required: true, message: '请输入显示名称', trigger: 'blur' }],
      password: [{ required: true, message: '请输入管理员密码', trigger: 'blur' }, { min: 10, message: '密码长度至少为 10 位', trigger: 'blur' }, { pattern: /[a-z]/, message: '密码必须包含小写字母', trigger: 'blur' }, { pattern: /[A-Z]/, message: '密码必须包含大写字母', trigger: 'blur' }, { pattern: /\d/, message: '密码必须包含数字', trigger: 'blur' }]
    },
    loginForm: { userName: 'admin', password: '', rememberMe: true },
    dataSources: [], approvals: [], auditLogs: [], users: [], clients: [], roles: [],
    approvalFilter: 'all', approvalKeyword: '', approvalPage: 1, approvalPageSize: 20, approvalTotal: 0, approvalAllTotal: 0, pendingApprovalTotal: 0,
    logKeyword: '', logOutcome: '', auditLogPage: 1, auditLogPageSize: 20, auditLogTotal: 0, auditLogAllTotal: 0,
    dataSourceDialog: false, editingDataSource: null, dataSourceForm: {},
    approvalDialog: false, selectedApproval: null, reviewComment: '', logDialog: false, selectedLog: null,
    userDialog: false, editingUser: null, newUser: { userName: '', email: '', displayName: '', password: '', roles: ['Developer'], enabled: true },
    maintenanceSettings: { cleanupEnabled: true, retentionDays: 3, cleanupTimeLocal: '03:00', lastCleanupAtUtc: null, lastCleanupSummary: null },
    providers: [{ value: 1, label: 'SQL Server', port: 1433 }, { value: 2, label: 'MySQL', port: 3306 }, { value: 3, label: 'PostgreSQL', port: 5432 }, { value: 4, label: 'SQLite', port: 1 }],
    accessModes: [{ value: 0, label: '禁用' }, { value: 1, label: '只读' }, { value: 2, label: '写入需审批' }, { value: 3, label: '开发模式' }]
  }),
  computed: {
    isAdmin () { return this.user?.roles?.includes('Administrator') },
    canOperate () { return this.isAdmin || this.user?.roles?.includes('Operator') },
    canApprove () { return this.isAdmin || this.user?.roles?.includes('Approver') },
    canViewLogs () { return this.isAdmin || this.user?.roles?.includes('Approver') || this.user?.roles?.includes('Operator') || this.user?.roles?.includes('Auditor') },
    isMainPage () { return ['overview', 'datasources', 'approvals', 'logs'].includes(this.activeTab) },
    approvalPageCount () { return Math.max(1, Math.ceil(this.approvalTotal / this.approvalPageSize)) },
    auditLogPageCount () { return Math.max(1, Math.ceil(this.auditLogTotal / this.auditLogPageSize)) },
    userInitials () { return (this.user?.displayName || this.user?.userName || 'U').trim().slice(0, 2).toUpperCase() },
    selectedLogDetail () { return this.parseLogDetail(this.selectedLog?.detail) },
    selectedLogColumns () {
      if (Array.isArray(this.selectedLogDetail.columns) && this.selectedLogDetail.columns.length) return this.selectedLogDetail.columns
      return Object.keys(this.selectedLogDetail.rows?.[0] || {})
    }
  },
  async created () { await this.bootstrap() },
  beforeUnmount () { this.disconnectEvents() },
  methods: {
    async bootstrap () {
      try {
        const setup = await axios.get('/api/setup/status')
        this.needsSetup = setup.data.needsSetup
        if (!this.needsSetup) {
          try { this.user = (await axios.get('/api/auth/me')).data; await this.loadOverview(); this.connectEvents() } catch (_) { this.user = null }
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
      try { this.user = (await axios.post('/api/auth/login', this.loginForm)).data; await this.loadOverview(); this.connectEvents() } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async logout () { this.disconnectEvents(); await axios.post('/api/auth/logout'); this.user = null; this.activeTab = 'overview' },
    async handleUserCommand (command) { if (command === 'logout') return this.logout(); await this.goTo(command) },
    async goTo (name) { this.activeTab = name; await this.loadActiveTab() },
    async loadOverview () {
      const jobs = []
      if (this.canOperate) jobs.push(this.loadDataSources())
      if (this.canApprove) jobs.push(this.loadApprovalMetrics())
      if (this.canViewLogs) jobs.push(this.loadAuditLogMetrics())
      await Promise.all(jobs)
    },
    async loadActiveTab () {
      if (this.activeTab === 'overview') await this.loadOverview()
      if (this.activeTab === 'datasources') await this.loadDataSources()
      if (this.activeTab === 'approvals') await this.loadApprovals()
      if (this.activeTab === 'logs') await this.loadAuditLogs()
      if (this.activeTab === 'settings') await this.loadMaintenanceSettings()
      if (this.activeTab === 'users') await this.loadUsers()
      if (this.activeTab === 'clients') await this.loadClients()
    },
    async loadDataSources () { this.dataSources = (await axios.get('/api/admin/datasources')).data },
    async loadApprovalMetrics () {
      const [all, pending] = await Promise.all([
        axios.get('/api/approvals', { params: { page: 1, pageSize: 1 } }),
        axios.get('/api/approvals', { params: { status: 'Pending', page: 1, pageSize: 1 } })
      ])
      this.approvalAllTotal = all.data.total
      this.pendingApprovalTotal = pending.data.total
    },
    async loadAuditLogMetrics () { this.auditLogAllTotal = (await axios.get('/api/audit/logs', { params: { page: 1, pageSize: 1 } })).data.total },
    async loadApprovals () {
      const response = await axios.get('/api/approvals', { params: { status: this.approvalFilter === 'all' ? undefined : this.approvalFilter, keyword: this.approvalKeyword.trim() || undefined, page: this.approvalPage, pageSize: this.approvalPageSize } })
      this.approvals = response.data.items
      this.approvalTotal = response.data.total
      this.approvalAllTotal = this.approvalFilter === 'all' && !this.approvalKeyword.trim() ? response.data.total : this.approvalAllTotal
    },
    async loadAuditLogs () {
      const response = await axios.get('/api/audit/logs', { params: { keyword: this.logKeyword.trim() || undefined, outcome: this.logOutcome || undefined, page: this.auditLogPage, pageSize: this.auditLogPageSize } })
      this.auditLogs = response.data.items
      this.auditLogTotal = response.data.total
      this.auditLogAllTotal = !this.logKeyword.trim() && !this.logOutcome ? response.data.total : this.auditLogAllTotal
    },
    async searchApprovals () { this.approvalPage = 1; await this.loadApprovals() },
    async resetApprovalSearch () { this.approvalFilter = 'all'; this.approvalKeyword = ''; this.approvalPage = 1; await this.loadApprovals() },
    async approvalSizeChanged () { this.approvalPage = 1; await this.loadApprovals() },
    async changeApprovalPage (page) { if (page < 1 || page > this.approvalPageCount || page === this.approvalPage) return; this.approvalPage = page; await this.loadApprovals() },
    async searchAuditLogs () { this.auditLogPage = 1; await this.loadAuditLogs() },
    async resetLogSearch () { this.logKeyword = ''; this.logOutcome = ''; this.auditLogPage = 1; await this.loadAuditLogs() },
    async auditLogSizeChanged () { this.auditLogPage = 1; await this.loadAuditLogs() },
    async changeAuditLogPage (page) { if (page < 1 || page > this.auditLogPageCount || page === this.auditLogPage) return; this.auditLogPage = page; await this.loadAuditLogs() },
    async openDataSourcePage () { if (this.canOperate) await this.goTo('datasources') },
    async openApprovalPage (status = 'all') { if (!this.canApprove) return; this.approvalFilter = status; this.approvalKeyword = ''; this.approvalPage = 1; await this.goTo('approvals') },
    async openLogPage () { if (!this.canViewLogs) return; this.logKeyword = ''; this.logOutcome = ''; this.auditLogPage = 1; await this.goTo('logs') },
    async loadUsers () { const [users, roles] = await Promise.all([axios.get('/api/admin/users'), axios.get('/api/admin/roles')]); this.users = users.data; this.roles = roles.data },
    async loadClients () { this.clients = (await axios.get('/api/admin/oauth-clients')).data },
    async loadMaintenanceSettings () { this.maintenanceSettings = (await axios.get('/api/settings/maintenance')).data },
    connectEvents () {
      this.disconnectEvents()
      const source = new EventSource('/api/events', { withCredentials: true })
      this.eventSource = source
      source.onopen = () => { this.eventConnected = true }
      source.onerror = () => { this.eventConnected = false }
      source.addEventListener('gateway-change', event => { try { this.scheduleEventRefresh(JSON.parse(event.data)) } catch (_) {} })
    },
    disconnectEvents () {
      if (this.eventSource) this.eventSource.close()
      this.eventSource = null; this.eventConnected = false
      if (this.eventRefreshTimer) clearTimeout(this.eventRefreshTimer)
      this.eventRefreshTimer = null
    },
    scheduleEventRefresh (gatewayEvent) {
      if (this.eventRefreshTimer) clearTimeout(this.eventRefreshTimer)
      this.eventRefreshTimer = setTimeout(() => this.refreshForEvent(gatewayEvent), 180)
    },
    async refreshForEvent (gatewayEvent) {
      const action = gatewayEvent?.action || ''
      const jobs = []
      if (this.canViewLogs) {
        jobs.push(this.loadAuditLogMetrics())
        if (this.activeTab === 'logs') jobs.push(this.loadAuditLogs())
      }
      if (this.canApprove && action.startsWith('change.')) {
        jobs.push(this.loadApprovalMetrics())
        if (this.activeTab === 'approvals') jobs.push(this.loadApprovals())
      }
      if (this.canOperate && action.startsWith('datasource.')) jobs.push(this.loadDataSources())
      if (this.isAdmin && action.startsWith('user.') && this.activeTab === 'users') jobs.push(this.loadUsers())
      if (this.isAdmin && action.startsWith('oauth-client.') && this.activeTab === 'clients') jobs.push(this.loadClients())
      if (this.isAdmin && (action.startsWith('settings.') || action.startsWith('maintenance.')) && this.activeTab === 'settings') jobs.push(this.loadMaintenanceSettings())
      await Promise.allSettled(jobs)
    },
    providerName (value) { return this.providers.find(item => item.value === value)?.label || value },
    accessName (value) { return this.accessModes.find(item => item.value === value)?.label || value },
    openDataSource (row) {
      this.editingDataSource = row || null
      this.dataSourceForm = row ? { ...row, password: '', blockedTablesText: (row.blockedTables || []).join('\n') } : { key: '', name: '', provider: 1, host: '127.0.0.1', port: 1433, database: '', username: '', password: '', accessMode: 1, maxRows: 1000, commandTimeoutSeconds: 30, enabled: true, blockedTablesText: '' }
      this.dataSourceDialog = true
    },
    async saveDataSource () {
      this.saving = true
      try { const payload = { ...this.dataSourceForm, blockedTables: this.dataSourceForm.blockedTablesText.split(/\r?\n/).map(item => item.trim()).filter(Boolean) }; delete payload.blockedTablesText; if (this.editingDataSource) await axios.put(`/api/admin/datasources/${this.editingDataSource.id}`, payload); else await axios.post('/api/admin/datasources', payload); this.dataSourceDialog = false; await this.loadDataSources(); ElMessage.success('已保存') } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async testDataSource (row) { try { const response = await axios.post(`/api/admin/datasources/${row.id}/test`); ElMessage({ type: response.data.success ? 'success' : 'error', message: response.data.message }) } catch (e) { this.error(e) } },
    async deleteDataSource (row) { try { await ElMessageBox.confirm(`确定删除 ${row.name}？`); await axios.delete(`/api/admin/datasources/${row.id}`); await this.loadDataSources() } catch (e) { if (!this.isCanceled(e)) this.error(e) } },
    async openApproval (row) { try { this.selectedApproval = (await axios.get(`/api/approvals/${row.id}`)).data; this.reviewComment = ''; this.approvalDialog = true } catch (e) { this.error(e) } },
    async reviewSelected (approved) {
      if (!this.selectedApproval) return
      this.saving = true
      try { await axios.post(`/api/approvals/${this.selectedApproval.id}/review`, { approved, comment: this.reviewComment }); this.approvalDialog = false; await this.loadApprovals(); ElMessage.success(approved ? '已批准并执行' : '已拒绝') } catch (e) { this.error(e) } finally { this.saving = false }
    },
    openLog (row) { this.selectedLog = row; this.logDialog = true },
    parseLogDetail (detail) {
      if (!detail) return {}
      try {
        const parsed = JSON.parse(detail)
        return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : { raw: detail }
      } catch (_) { return { raw: detail } }
    },
    logSummary (row) {
      const detail = this.parseLogDetail(row.detail)
      if (detail.sql) {
        const result = detail.rowCount !== undefined ? `；返回 ${detail.rowCount} 行${detail.truncated ? '（已截断）' : ''}` : detail.affectedRows !== undefined ? `；影响 ${detail.affectedRows} 行` : ''
        return `${detail.sql}${result}`
      }
      return detail.error || detail.raw || '—'
    },
    formatCell (value) {
      if (value === null || value === undefined) return 'NULL'
      return typeof value === 'object' ? JSON.stringify(value) : String(value)
    },
    openUserDialog (row) {
      this.editingUser = row || null
      this.newUser = row ? { userName: row.userName, email: row.email, displayName: row.displayName, password: '', roles: [...row.roles], enabled: row.isEnabled } : { userName: '', email: '', displayName: '', password: '', roles: ['Developer'], enabled: true }
      this.userDialog = true
    },
    async saveUser () {
      this.saving = true
      try { const wasEditing = !!this.editingUser; if (wasEditing) await axios.put(`/api/admin/users/${this.editingUser.id}`, { displayName: this.newUser.displayName, enabled: this.newUser.enabled, roles: this.newUser.roles }); else await axios.post('/api/admin/users', this.newUser); this.userDialog = false; this.editingUser = null; await this.loadUsers(); ElMessage.success(wasEditing ? '用户已保存' : '用户已创建') } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async deleteUser (row) { try { await ElMessageBox.confirm(`确定永久删除用户 ${row.userName}？已有审批或审计历史的用户将被拒绝删除，请改为禁用。`, '删除用户', { type: 'warning', confirmButtonText: '永久删除' }); await axios.delete(`/api/admin/users/${row.id}`); await this.loadUsers(); ElMessage.success('用户已删除') } catch (e) { if (!this.isCanceled(e)) this.error(e) } },
    async createClient () { try { const { value } = await ElMessageBox.prompt('客户端名称', '创建 OAuth2 客户端', { inputValue: 'Local AI Client' }); this.generatedClient = (await axios.post('/api/admin/oauth-clients', { displayName: value })).data; await this.loadClients() } catch (e) { if (!this.isCanceled(e)) this.error(e) } },
    async deleteClient (row) { try { await ElMessageBox.confirm(`吊销并删除 ${row.displayName || row.clientId}？该客户端将无法获取新 Token，已签发 Token 也会立即失效。`, '吊销 OAuth2 客户端', { type: 'warning', confirmButtonText: '吊销并删除' }); await axios.delete(`/api/admin/oauth-clients/${encodeURIComponent(row.clientId)}`); await this.loadClients(); ElMessage.success('OAuth2 客户端已吊销并删除') } catch (e) { if (!this.isCanceled(e)) this.error(e) } },
    async saveMaintenanceSettings () {
      this.saving = true
      try { this.maintenanceSettings = (await axios.put('/api/settings/maintenance', { cleanupEnabled: this.maintenanceSettings.cleanupEnabled, retentionDays: this.maintenanceSettings.retentionDays, cleanupTimeLocal: this.maintenanceSettings.cleanupTimeLocal })).data; ElMessage.success('清理设置已保存') } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async cleanupNow () {
      try { await ElMessageBox.confirm(`将删除 ${this.maintenanceSettings.retentionDays} 天以前的运行日志、审批记录和日志文件，是否继续？`, '立即清理', { type: 'warning' }) } catch (e) { if (!this.isCanceled(e)) this.error(e); return }
      this.saving = true
      try { const result = (await axios.post('/api/settings/maintenance/cleanup-now')).data; await this.loadMaintenanceSettings(); ElMessage.success(`清理完成：日志 ${result.auditLogsDeleted} 条，审批 ${result.approvalRecordsDeleted} 条，文件 ${result.logFilesDeleted} 个`) } catch (e) { this.error(e) } finally { this.saving = false }
    },
    formatDate (value) { return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—' },
    shortId (value) { return value ? String(value).slice(0, 8) : '—' },
    approvalStatusName (status) { return ({ Pending: '待审批', Approved: '已批准', Rejected: '已拒绝', Executing: '执行中', Succeeded: '执行成功', Failed: '执行失败', Expired: '已过期' })[status] || status },
    approvalStatusType (status) { return ({ Pending: 'warning', Succeeded: 'success', Approved: 'primary', Rejected: 'danger', Failed: 'danger', Expired: 'info' })[status] || 'info' },
    riskType (risk) { return ({ Low: 'success', Medium: 'warning', High: 'danger', Critical: 'danger' })[risk] || 'info' },
    outcomeName (outcome) { return ({ success: '成功', failure: '失败', pending: '待处理', rejected: '已拒绝' })[outcome] || outcome },
    outcomeType (outcome) { return ({ success: 'success', failure: 'danger', pending: 'warning', rejected: 'danger' })[outcome] || 'info' },
    actionName (action) { return ({ 'system.setup': '系统初始化', 'auth.login': '用户登录', 'auth.logout': '用户退出', 'query.execute': 'AI 只读查询', 'query.blocked': '黑名单拦截查询', 'change.submit': '提交 SQL 工单', 'change.review': '审核 SQL 工单', 'change.execute': '执行 SQL 变更', 'datasource.create': '创建数据源', 'datasource.update': '更新数据源', 'datasource.delete': '删除数据源', 'datasource.test': '测试数据源', 'settings.maintenance.update': '更新清理设置', 'maintenance.cleanup': '清理日志与记录', 'user.create': '创建用户', 'user.update': '更新用户', 'user.delete': '删除用户', 'oauth-client.create': '创建 OAuth2 客户端', 'oauth-client.delete': '吊销 OAuth2 客户端' })[action] || action },
    isCanceled (e) { return e === 'cancel' || e === 'close' || e?.message === 'cancel' || e?.message === 'close' }
  }
}
</script>
