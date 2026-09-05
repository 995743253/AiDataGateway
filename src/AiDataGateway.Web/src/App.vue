<template>
  <el-config-provider :locale="zhCn">
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
          <p class="form-hint">至少 6 位，可使用中文、英文、数字或符号。</p>
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
          <el-checkbox v-model="loginForm.rememberMe">记住密码（本机保持登录 30 天）</el-checkbox>
          <el-button type="primary" class="full-button" :loading="saving" @click="login">登录</el-button>
          <p class="form-hint">不会在页面保存明文密码；使用受保护的登录 Cookie 记住登录状态。</p>
          <div class="auth-assist"><el-button link type="primary" @click="openAdminReset">忘记管理员密码？</el-button></div>
        </el-form>
      </el-card>
      <el-dialog v-model="adminResetDialog" title="重置管理员登录密码" width="520px" append-to-body destroy-on-close>
        <el-alert title="仅可重置已启用的 Administrator 账号。初始重置口令为 admin，建议登录后立即在系统设置中修改。" type="warning" :closable="false" show-icon />
        <el-form :model="adminResetForm" label-position="top" class="admin-reset-form">
          <el-form-item label="管理员用户名"><el-input v-model="adminResetForm.userName" autocomplete="username" /></el-form-item>
          <el-form-item label="重置口令"><el-input v-model="adminResetForm.resetPassword" type="password" show-password autocomplete="off" /></el-form-item>
          <el-form-item label="新的登录密码"><el-input v-model="adminResetForm.newPassword" type="password" show-password autocomplete="new-password" /></el-form-item>
          <el-form-item label="确认新的登录密码"><el-input v-model="adminResetForm.confirmPassword" type="password" show-password autocomplete="new-password" @keyup.enter="resetAdminPassword" /></el-form-item>
          <span class="field-help">新登录密码至少 6 位，可使用中文、英文、数字或符号。</span>
        </el-form>
        <template #footer><el-button @click="adminResetDialog=false">取消</el-button><el-button type="primary" :loading="saving" @click="resetAdminPassword">确认重置</el-button></template>
      </el-dialog>
    </div>

    <div v-else class="shell">
      <header class="header">
        <button class="brand-button" type="button" @click="goTo('overview')">
          <span class="brand">AiDataGateway</span><span class="subtitle">本地 AI 数据访问管控</span>
        </button>
        <div class="header-actions">
          <span class="live-state" :class="{ online: eventConnected }"><span class="live-dot" />{{ eventConnected ? '实时同步' : '正在连接' }}</span>
          <button class="theme-toggle" type="button" :title="uiTheme === 'dark' ? '切换到亮色模式' : '切换到暗色模式'" @click="toggleUiTheme">{{ uiTheme === 'dark' ? '☀️' : '🌙' }}</button>
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

      <div class="workspace-shell" :class="{ 'sidebar-is-collapsed': sidebarCollapsed }">
        <aside class="sidebar">
          <div class="sidebar-control">
            <button type="button" class="sidebar-toggle" :title="sidebarCollapsed ? '展开菜单' : '收起菜单'" @click="toggleSidebar">
              <span class="collapse-glyph">{{ sidebarCollapsed ? '»' : '«' }}</span>
              <span v-if="!sidebarCollapsed">收起菜单</span>
            </button>
          </div>
          <el-menu class="side-menu" :default-active="activeTab" :collapse="sidebarCollapsed" :collapse-transition="false" unique-opened @select="goTo">
            <el-menu-item index="overview"><span class="menu-glyph"><el-icon><Odometer /></el-icon></span><template #title>工作概览</template></el-menu-item>
            <el-sub-menu v-if="canOperate" index="resources">
              <template #title><span class="menu-glyph"><el-icon><Folder /></el-icon></span><span>资源管理</span></template>
              <el-menu-item index="projects">项目管理</el-menu-item>
              <el-menu-item index="datasources">数据源</el-menu-item>
              <el-menu-item index="logsources">日志源</el-menu-item>
            </el-sub-menu>
            <el-sub-menu v-if="canViewLogs || canViewMetrics" index="observability">
              <template #title><span class="menu-glyph"><el-icon><DataLine /></el-icon></span><span>观测中心</span></template>
              <el-menu-item v-if="canViewLogs" index="applicationlogs">应用日志</el-menu-item>
              <el-menu-item v-if="canViewLogs" index="realtimelogs">实时日志</el-menu-item>
              <el-menu-item v-if="canViewMetrics" index="monitoring">服务器监控</el-menu-item>
              <el-menu-item v-if="canViewLogs" index="logs">网关审计</el-menu-item>
            </el-sub-menu>
            <el-sub-menu v-if="canApprove" index="security">
              <template #title><span class="menu-glyph"><el-icon><Lock /></el-icon></span><span>安全审批</span></template>
              <el-menu-item index="approvals">审批记录</el-menu-item>
            </el-sub-menu>
            <el-sub-menu v-if="isAdmin" index="system">
              <template #title><span class="menu-glyph"><el-icon><Setting /></el-icon></span><span>系统管理</span></template>
              <el-menu-item index="settings">系统设置</el-menu-item>
              <el-menu-item index="users">用户管理</el-menu-item>
              <el-menu-item index="clients">OAuth2 客户端</el-menu-item>
            </el-sub-menu>
            <el-sub-menu index="toolbox">
              <template #title><span class="menu-glyph"><el-icon><Suitcase /></el-icon></span><span>工具箱</span></template>
              <el-menu-item index="toolboxwebhooks">WebHook 调试</el-menu-item>
              <el-menu-item index="toolboxtools">格式化与编码</el-menu-item>
            </el-sub-menu>
            <el-sub-menu index="customization">
              <template #title><span class="menu-glyph"><el-icon><Grid /></el-icon></span><span>定制化模块</span></template>
              <el-menu-item index="custommodules">模块中心</el-menu-item>
              <el-menu-item v-for="module in customModulePages" :key="module.id" :index="customModuleTab(module)">{{ module.pageTitle || module.name }}</el-menu-item>
            </el-sub-menu>
          </el-menu>
          <div v-if="!sidebarCollapsed" class="sidebar-foot"><span class="sidebar-live-dot" :class="{ online: eventConnected }" /><span>{{ eventConnected ? '数据实时同步' : '正在连接服务' }}</span></div>
        </aside>

        <main class="content">
        <el-alert v-if="generatedClient" class="credential-alert" title="请立即保存 OAuth2 客户端凭据，关闭后无法再次查看 Secret" type="warning" show-icon closable @close="generatedClient=null">
          <div class="credential-panel">
            <div class="credential-item"><span class="credential-label">Client ID</span><code class="credential-value">{{ generatedClient.clientId }}</code></div>
            <div class="credential-item"><span class="credential-label">Client Secret</span><code class="credential-value">{{ generatedClient.clientSecret }}</code></div>
          </div>
        </el-alert>

        <el-tabs v-model="activeTab" type="card" closable class="workspace-tabs" @tab-change="loadActiveTab" @tab-remove="closePage">
          <el-tab-pane v-if="isPageOpen('overview')" label="概览" name="overview" :closable="false">
            <div class="metric-grid">
              <el-card class="metric metric-link project-metric" :class="{ 'metric-disabled': !canOperate }" shadow="hover" role="button" :tabindex="canOperate ? 0 : -1" @click="openProjectPage" @keyup.enter="openProjectPage"><span>项目</span><strong>{{ canOperate ? projects.length : '—' }}</strong><small>{{ canOperate ? '查看项目资源聚合 →' : '无查看权限' }}</small></el-card>
              <el-card class="metric metric-link" :class="{ 'metric-disabled': !canOperate }" shadow="hover" role="button" :tabindex="canOperate ? 0 : -1" @click="openDataSourcePage" @keyup.enter="openDataSourcePage"><span>数据源</span><strong>{{ canOperate ? dataSources.length : '—' }}</strong><small>{{ canOperate ? '进入数据源管理 →' : '无查看权限' }}</small></el-card>
              <el-card class="metric metric-link log-source-metric" :class="{ 'metric-disabled': !canOperate }" shadow="hover" role="button" :tabindex="canOperate ? 0 : -1" @click="openLogSourcePage" @keyup.enter="openLogSourcePage"><span>日志源</span><strong>{{ canOperate ? logSources.length : '—' }}</strong><small>{{ canOperate ? '配置 NLog / Seq →' : '无查看权限' }}</small></el-card>
              <el-card class="metric metric-link warning" :class="{ 'metric-disabled': !canApprove }" shadow="hover" role="button" :tabindex="canApprove ? 0 : -1" @click="openApprovalPage('Pending')" @keyup.enter="openApprovalPage('Pending')"><span>待审批</span><strong>{{ canApprove ? pendingApprovalTotal : '—' }}</strong><small>{{ canApprove ? '查看待审批工单 →' : '无查看权限' }}</small></el-card>
              <el-card class="metric metric-link" :class="{ 'metric-disabled': !canApprove }" shadow="hover" role="button" :tabindex="canApprove ? 0 : -1" @click="openApprovalPage('all')" @keyup.enter="openApprovalPage('all')"><span>审批记录</span><strong>{{ canApprove ? approvalAllTotal : '—' }}</strong><small>{{ canApprove ? '查询审批历史 →' : '无查看权限' }}</small></el-card>
              <el-card class="metric metric-link success" :class="{ 'metric-disabled': !canViewLogs }" shadow="hover" role="button" :tabindex="canViewLogs ? 0 : -1" @click="openApplicationLogPage" @keyup.enter="openApplicationLogPage"><span>应用日志</span><strong>{{ canViewLogs ? 'NLog / Seq' : '—' }}</strong><small>{{ canViewLogs ? '查询项目业务日志 →' : '无查看权限' }}</small></el-card>
              <el-card class="metric metric-link monitor-metric" :class="{ 'metric-disabled': !canViewMetrics }" shadow="hover" role="button" :tabindex="canViewMetrics ? 0 : -1" @click="openMonitoringPage" @keyup.enter="openMonitoringPage"><span>服务器监控</span><strong>{{ canViewMetrics ? onlineMonitorCount : '—' }}</strong><small>{{ canViewMetrics ? `在线 ${onlineMonitorCount} / ${monitorTargets.length} →` : '无查看权限' }}</small></el-card>
            </div>
            <el-card class="overview-card" shadow="never">
              <div class="overview-heading">
                <div><h3>本地网关运行状态</h3><p>管理页面通过服务端事件接收数据变更，不使用定时轮询。</p></div>
                <el-tag :type="eventConnected ? 'success' : 'warning'">{{ eventConnected ? 'SSE 已连接' : 'SSE 重连中' }}</el-tag>
              </div>
            </el-card>
          </el-tab-pane>

          <el-tab-pane v-if="canOperate && isPageOpen('projects')" label="项目管理" name="projects">
            <div class="toolbar"><div><h3>项目资源聚合</h3><p>通过唯一项目编号聚合数据库、日志源和服务器监控节点，供页面及 AI 统一解析。</p></div><el-button type="primary" @click="openProject()">新增项目</el-button></div>
            <el-table :data="pagedProjects" stripe border height="100%" class="page-table">
              <el-table-column prop="code" label="项目编号" min-width="140" />
              <el-table-column prop="name" label="项目名称" min-width="160" />
              <el-table-column label="说明" min-width="220"><template #default="s"><div class="cell-with-action"><span class="cell-ellipsis">{{ s.row.description || '—' }}</span><el-button v-if="isLongCell(s.row.description)" link type="primary" @click="openTextViewer('项目说明', s.row.description)">查看</el-button></div></template></el-table-column>
              <el-table-column label="数据库" min-width="180"><template #default="s"><div class="tag-list"><el-tag v-for="item in s.row.dataSources" :key="item.id" effect="plain">{{ item.key }}</el-tag><span v-if="!s.row.dataSources.length">—</span></div></template></el-table-column>
              <el-table-column label="日志源" min-width="180"><template #default="s"><div class="tag-list"><el-tag v-for="item in s.row.logSources" :key="item.id" type="success" effect="plain">{{ item.key }}</el-tag><span v-if="!s.row.logSources.length">—</span></div></template></el-table-column>
              <el-table-column label="监控节点" min-width="180"><template #default="s"><div class="tag-list"><el-tag v-for="item in s.row.monitorTargets" :key="item.id" type="warning" effect="plain">{{ item.key }}</el-tag><span v-if="!s.row.monitorTargets?.length">—</span></div></template></el-table-column>
              <el-table-column label="状态" width="90"><template #default="s"><el-tag :type="s.row.enabled ? 'success' : 'info'">{{ s.row.enabled ? '启用' : '禁用' }}</el-tag></template></el-table-column>
              <el-table-column label="操作" width="170"><template #default="s"><el-button size="small" @click="openProject(s.row)">编辑</el-button><el-button size="small" type="danger" @click="deleteProject(s.row)">删除</el-button></template></el-table-column>
            </el-table>
            <el-pagination class="pagination-panel element-pagination" v-model:current-page="projectPage" v-model:page-size="projectPageSize" :page-sizes="pageSizeOptions" :total="projects.length" layout="total, sizes, prev, pager, next, jumper" background />
          </el-tab-pane>

          <el-tab-pane v-if="canOperate && isPageOpen('datasources')" label="数据源" name="datasources">
            <div class="toolbar"><div><h3>多数据库连接</h3><p>管理 AI 可以访问的数据库及审批模式。</p></div><el-button type="primary" @click="openDataSource()">新增数据源</el-button></div>
            <el-table :data="pagedDataSources" stripe height="100%" class="page-table">
              <el-table-column prop="name" label="名称" min-width="150" />
              <el-table-column prop="key" label="标识" min-width="140" />
              <el-table-column prop="provider" label="类型" width="110"><template #default="s">{{ providerName(s.row.provider) }}</template></el-table-column>
              <el-table-column label="地址" min-width="180"><template #default="s">{{ s.row.host }}:{{ s.row.port }}</template></el-table-column>
              <el-table-column label="数据库" min-width="150"><template #default="s"><div class="cell-with-action"><span class="cell-ellipsis">{{ s.row.database }}</span><el-button v-if="isLongCell(s.row.database)" link type="primary" @click="openTextViewer('数据库', s.row.database)">查看</el-button></div></template></el-table-column>
              <el-table-column prop="accessMode" label="模式" width="140"><template #default="s">{{ accessName(s.row.accessMode) }}</template></el-table-column>
              <el-table-column label="表黑名单" width="110"><template #default="s"><el-tag v-if="s.row.blockedTables?.length" type="danger" effect="plain">{{ s.row.blockedTables.length }} 张表</el-tag><span v-else>—</span></template></el-table-column>
              <el-table-column label="操作" width="290"><template #default="s"><el-button size="small" @click="testDataSource(s.row)">测试</el-button><el-button size="small" @click="openDataSource(s.row)">编辑</el-button><el-button size="small" type="primary" plain @click="openDataSourceApprovals(s.row)">审批记录</el-button><el-button size="small" type="danger" @click="deleteDataSource(s.row)">删除</el-button></template></el-table-column>
            </el-table>
            <el-pagination class="pagination-panel element-pagination" v-model:current-page="dataSourcePage" v-model:page-size="dataSourcePageSize" :page-sizes="pageSizeOptions" :total="dataSources.length" layout="total, sizes, prev, pager, next, jumper" background />
          </el-tab-pane>

          <el-tab-pane v-if="canOperate && isPageOpen('logsources')" label="日志源" name="logsources">
            <div class="toolbar"><div><h3>日志采集源</h3><p>支持网关本机 NLog、Seq API，以及安装在远端服务器上的采集 Agent。</p></div><el-button type="primary" @click="openLogSource()">新增日志源</el-button></div>
            <el-table :data="pagedLogSources" stripe border height="100%" class="page-table">
              <el-table-column prop="name" label="名称" min-width="160" />
              <el-table-column prop="key" label="日志标识" min-width="140" />
              <el-table-column label="类型" width="120"><template #default="s"><el-tag :type="s.row.type === 2 ? 'warning' : 'primary'">{{ logSourceTypeName(s.row.type) }}</el-tag></template></el-table-column>
              <el-table-column label="采集位置" min-width="300"><template #default="s"><div class="log-source-location cell-with-action"><div><strong>{{ s.row.type === 2 ? 'Seq HTTP API' : s.row.type === 3 ? '远程 Agent' : '本地文件夹' }}</strong><span class="cell-ellipsis">{{ s.row.endpoint || '从 NLog 配置读取文件位置' }}</span></div><el-button v-if="isLongCell(s.row.endpoint)" link type="primary" @click="openTextViewer('采集位置', s.row.endpoint)">查看</el-button></div></template></el-table-column>
              <el-table-column label="关联项目" min-width="180"><template #default="s"><div class="tag-list"><el-tag v-for="item in s.row.projects" :key="item.id" effect="plain">{{ item.code }}</el-tag><span v-if="!s.row.projects.length">—</span></div></template></el-table-column>
              <el-table-column label="状态" width="90"><template #default="s"><el-tag :type="s.row.enabled ? 'success' : 'info'">{{ s.row.enabled ? '启用' : '禁用' }}</el-tag></template></el-table-column>
              <el-table-column label="操作" width="290"><template #default="s"><el-button size="small" @click="testLogSource(s.row)">测试</el-button><el-button size="small" @click="openLogSource(s.row)">编辑</el-button><el-button size="small" type="primary" plain @click="openLogSourceLogs(s.row)">应用日志</el-button><el-button size="small" type="danger" @click="deleteLogSource(s.row)">删除</el-button></template></el-table-column>
            </el-table>
            <el-pagination class="pagination-panel element-pagination" v-model:current-page="logSourcePage" v-model:page-size="logSourcePageSize" :page-sizes="pageSizeOptions" :total="logSources.length" layout="total, sizes, prev, pager, next, jumper" background />
          </el-tab-pane>

          <el-tab-pane v-if="canViewLogs && isPageOpen('applicationlogs')" label="应用日志" name="applicationlogs">
            <div class="toolbar app-log-toolbar">
              <div><h3>项目应用日志</h3><p>统一读取本地 NLog 文件或 Seq，保留多行、空字段和未闭合末尾记录。</p></div>
              <div class="live-log-actions"><el-button type="primary" plain @click="openRealtimeLogs">实时日志 →</el-button></div>
            </div>
            <div class="app-log-filter-panel">
              <div class="app-log-filter-row">
                <el-select v-model="applicationLogSourceId" class="log-source-select" filterable placeholder="选择日志源"><el-option v-for="item in logSources.filter(x => x.enabled)" :key="item.id" :label="`[${item.type === 2 ? 'Seq API' : item.type === 3 ? '远程 Agent' : '本地文件'}] ${item.name}（${item.key}）`" :value="item.id" /></el-select>
                <el-date-picker v-model="applicationLogRange" class="log-date-range" type="datetimerange" range-separator="至" start-placeholder="开始日期" end-placeholder="结束日期" :shortcuts="logDateShortcuts" />
                <el-select v-model="applicationLogLevel" class="status-filter" clearable placeholder="全部级别"><el-option v-for="level in logLevels" :key="level" :label="level" :value="level" /></el-select>
              </div>
              <div v-if="selectedApplicationLogSource?.type === 2" class="seq-query-mode"><el-radio-group v-model="applicationLogQueryMode" size="small"><el-radio-button value="simple">简单查询</el-radio-button><el-radio-button value="advanced">高级表达式</el-radio-button></el-radio-group><span>{{ applicationLogQueryMode === 'simple' ? '不会写表达式也能查：填写关键词，或按属性精确筛选。' : '适合熟悉 Seq Filter 语法的用户。' }}</span></div>
              <div class="app-log-filter-row">
                <template v-if="selectedApplicationLogSource?.type !== 2 || applicationLogQueryMode === 'simple'">
                  <el-input v-model="applicationLogSearchText" class="log-search" clearable placeholder="关键词，例如：订单失败" @keyup.enter="searchApplicationLogs" />
                  <template v-if="selectedApplicationLogSource?.type === 2"><el-input v-model="applicationLogTopic" class="log-property" clearable placeholder="Topic（可选）" @keyup.enter="searchApplicationLogs" /><el-input v-model="applicationLogPropertyName" class="log-property" clearable placeholder="属性名（可选）" /><el-input v-model="applicationLogPropertyValue" class="log-property" clearable placeholder="属性值（可选）" /></template>
                </template>
                <el-input v-else v-model="applicationLogQuery" class="log-advanced-search" clearable placeholder="例如：RequestPath like '/api/%' and StatusCode >= 500" @keyup.enter="searchApplicationLogs" />
                <div class="filter-buttons"><el-button type="primary" :loading="applicationLogsLoading" @click="searchApplicationLogs">查询</el-button><el-button @click="resetApplicationLogs">重置</el-button></div>
              </div>
            </div>
            <div v-if="selectedApplicationLogSource" class="log-source-mode-bar">
              <el-tag :type="selectedApplicationLogSource.type === 2 ? 'warning' : selectedApplicationLogSource.type === 3 ? 'success' : 'primary'" effect="dark">{{ selectedApplicationLogSource.type === 2 ? 'Seq API 采集' : selectedApplicationLogSource.type === 3 ? '远程 Agent 采集' : '本地文件夹采集' }}</el-tag>
              <span>{{ selectedApplicationLogSource.type === 2 ? '网关使用 Seq HTTP API 和独立 API Key 查询；日期条件会直接发送给 Seq，避免先下载全部日志。' : selectedApplicationLogSource.type === 3 ? '网关按日期向远端 Agent 查询服务器本地日志，文件内容不会预先全量上传。' : '网关只读取所选日期可能涉及的文件，并限制单次读取量，避免日志过多占用大量内存。' }}</span>
            </div>
            <el-alert v-if="applicationLogWarning" :title="applicationLogWarning" type="warning" show-icon :closable="false" class="log-warning" />
            <el-table :data="applicationLogs" stripe border height="100%" class="paged-table page-table" @row-dblclick="openApplicationLog">
              <el-table-column prop="timestampUtc" label="时间" width="190"><template #default="s">{{ formatDate(s.row.timestampUtc) }}</template></el-table-column>
              <el-table-column prop="level" label="级别" width="110"><template #default="s"><el-tag :type="logLevelType(s.row.level)">{{ s.row.level || '未知' }}</el-tag></template></el-table-column>
              <el-table-column v-if="selectedApplicationLogSource?.type === 2" label="Topic" width="130"><template #default="s"><el-tag v-if="topicOf(s.row)" effect="plain" size="small">{{ topicOf(s.row) }}</el-tag><span v-else>—</span></template></el-table-column>
              <el-table-column label="消息" min-width="360"><template #default="s"><span class="cell-ellipsis">{{ s.row.message || '—' }}</span></template></el-table-column>
              <el-table-column label="解析" width="100"><template #default="s"><el-tag v-if="s.row.incomplete" type="warning">不完整</el-tag><el-tag v-else type="success" effect="plain">结构化</el-tag></template></el-table-column>
              <el-table-column label="操作" width="150"><template #default="s"><el-button v-if="extractLogSql(s.row)" size="small" link type="warning" @click="openApplicationLog(s.row, 'sql')">查看 SQL</el-button><el-button size="small" link type="primary" @click="openApplicationLog(s.row)">完整数据</el-button></template></el-table-column>
            </el-table>
            <el-empty v-if="!applicationLogsLoading && applicationLogs.length === 0" description="请选择日志源并查询" />
            <el-pagination class="pagination-panel element-pagination" v-model:current-page="applicationLogPage" v-model:page-size="applicationLogPageSize" :page-sizes="[20, 50, 100, 200]" :total="applicationLogPaginationTotal" layout="total, sizes, prev, pager, next, jumper" background @current-change="loadApplicationLogs" @size-change="applicationLogSizeChanged" />
          </el-tab-pane>

          <el-tab-pane v-if="canViewLogs && isPageOpen('realtimelogs')" label="实时日志" name="realtimelogs">
            <div class="toolbar realtime-log-toolbar"><div><h3>实时日志控制台</h3><p>独立查看新增日志，不影响历史日志查询；页面最多保留最近 500 条。</p></div><div class="live-log-actions"><el-tag :type="realtimeLogConnected ? 'success' : realtimeLogConnecting ? 'warning' : 'info'" effect="dark"><span class="live-dot" />{{ realtimeLogConnected ? '实时接收中' : realtimeLogConnecting ? '连接中' : '未连接' }}</el-tag></div></div>
            <div class="app-log-filter-panel realtime-filter-panel">
              <div class="app-log-filter-row">
                <el-select v-model="realtimeLogSourceId" class="log-source-select" filterable placeholder="选择日志源" @change="stopRealtimeLogStream"><el-option v-for="item in logSources.filter(x => x.enabled)" :key="item.id" :label="`[${item.type === 2 ? 'Seq API' : item.type === 3 ? '远程 Agent' : '本地文件'}] ${item.name}（${item.key}）`" :value="item.id" /></el-select>
                <el-input v-model="realtimeLogSearchText" class="log-search" clearable placeholder="只看包含此关键词的新日志（可选）" />
                <el-select v-model="realtimeLogLevel" class="status-filter" clearable placeholder="全部级别"><el-option v-for="level in logLevels" :key="level" :label="level" :value="level" /></el-select>
                <template v-if="selectedRealtimeLogSource?.type === 2"><el-input v-model="realtimeLogTopic" class="log-property" clearable placeholder="Topic（可选）" /><el-input v-model="realtimeLogPropertyName" class="log-property" clearable placeholder="属性名（可选）" /><el-input v-model="realtimeLogPropertyValue" class="log-property" clearable placeholder="属性值（可选）" /></template>
                <div class="filter-buttons"><el-button v-if="!realtimeLogConnected && !realtimeLogConnecting" type="success" @click="startRealtimeLogStream">开始接收</el-button><el-button v-else type="danger" @click="stopRealtimeLogStream">{{ realtimeLogConnecting ? '取消连接' : '停止接收' }}</el-button><el-button @click="clearRealtimeLogs">清空屏幕</el-button></div>
              </div>
            </div>
            <el-alert v-if="realtimeLogError" :title="realtimeLogError" type="warning" show-icon :closable="false" class="log-warning" />
            <el-table :data="pagedRealtimeLogs" stripe border height="100%" class="paged-table page-table realtime-log-table" @row-dblclick="openApplicationLog">
              <el-table-column prop="timestampUtc" label="时间" width="190"><template #default="s">{{ formatDate(s.row.timestampUtc) }}</template></el-table-column>
              <el-table-column prop="level" label="级别" width="110"><template #default="s"><el-tag :type="logLevelType(s.row.level)">{{ s.row.level || '未知' }}</el-tag></template></el-table-column>
              <el-table-column v-if="selectedRealtimeLogSource?.type === 2" label="Topic" width="130"><template #default="s"><el-tag v-if="topicOf(s.row)" effect="plain" size="small">{{ topicOf(s.row) }}</el-tag><span v-else>—</span></template></el-table-column>
              <el-table-column label="消息" min-width="420"><template #default="s"><span class="cell-ellipsis">{{ s.row.message || '—' }}</span></template></el-table-column>
              <el-table-column label="操作" width="150"><template #default="s"><el-button v-if="extractLogSql(s.row)" size="small" link type="warning" @click="openApplicationLog(s.row, 'sql')">查看 SQL</el-button><el-button size="small" link type="primary" @click="openApplicationLog(s.row)">完整数据</el-button></template></el-table-column>
            </el-table>
            <el-empty v-if="!realtimeLogs.length" :description="realtimeLogConnected ? '连接正常，正在等待新日志…' : realtimeLogConnecting ? '正在建立实时连接…' : '请选择日志源并点击“开始接收”'" />
            <el-pagination v-if="realtimeLogs.length" class="pagination-panel element-pagination" v-model:current-page="realtimeLogPage" v-model:page-size="realtimeLogPageSize" :page-sizes="[20, 50, 100]" :total="realtimeLogs.length" layout="total, sizes, prev, pager, next" background />
          </el-tab-pane>

          <el-tab-pane v-if="canViewMetrics && isPageOpen('monitoring')" label="服务器监控" name="monitoring">
            <div class="toolbar monitoring-toolbar">
              <div><h3>服务器状态指标</h3><p>本机由网关直接采集；远端服务器运行轻量 Agent 后使用独立密钥上报，无需轮询页面。</p></div>
              <el-button v-if="canOperate" type="primary" @click="openMonitorTarget()">新增远端节点</el-button>
            </div>
            <div class="monitor-layout">
              <el-card class="monitor-target-panel" shadow="never">
                <template #header><div class="panel-heading"><strong>监控节点</strong><el-tag effect="plain">{{ onlineMonitorCount }} 在线</el-tag></div></template>
                <button v-for="target in pagedMonitorTargets" :key="target.id" type="button" class="monitor-target-item" :class="{ active: selectedMonitorTargetId === target.id }" @click="selectMonitorTarget(target)">
                  <span class="target-status" :class="{ online: target.online }" />
                  <span class="target-copy"><strong>{{ target.name }}</strong><small>{{ target.hostName || target.key }} · {{ monitorTargetTypeName(target.type) }}</small></span>
                  <span class="target-cpu">{{ target.latest ? `${target.latest.cpuPercent.toFixed(1)}%` : '—' }}</span>
                </button>
                <el-empty v-if="monitorTargets.length === 0" description="暂无监控节点" :image-size="72" />
                <el-pagination v-if="monitorTargets.length > monitorTargetPageSize" class="monitor-target-pagination" v-model:current-page="monitorTargetPage" :page-size="monitorTargetPageSize" :total="monitorTargets.length" layout="prev, pager, next" small background />
              </el-card>

              <div class="monitor-detail">
                <el-tabs v-if="selectedMonitorTarget" v-model="monitorSection" class="monitor-subtabs">
                  <el-tab-pane label="实时概览" name="overview">
                  <el-card class="monitor-summary-card" shadow="never">
                    <div class="monitor-title-row">
                      <div><h3>{{ selectedMonitorTarget.name }}</h3><p>{{ selectedMonitorTarget.hostName || '等待首次采集' }} · {{ selectedMonitorTarget.osDescription || selectedMonitorTarget.key }}</p></div>
                      <div class="monitor-title-actions"><el-tag :type="selectedMonitorTarget.online ? 'success' : 'danger'">{{ selectedMonitorTarget.online ? '在线' : '离线' }}</el-tag><el-button v-if="canOperate" size="small" @click="openMonitorTarget(selectedMonitorTarget)">配置</el-button><el-button v-if="canOperate && selectedMonitorTarget.type === 2" size="small" @click="rotateMonitorSecret(selectedMonitorTarget)">重置密钥</el-button><el-button v-if="canOperate && selectedMonitorTarget.type === 2" size="small" type="danger" @click="deleteMonitorTarget(selectedMonitorTarget)">删除</el-button></div>
                    </div>
                    <div class="server-metric-grid">
                      <div class="server-metric"><span>CPU</span><strong>{{ formatPercent(selectedMonitorTarget.latest?.cpuPercent) }}</strong><el-progress :percentage="selectedMonitorTarget.latest?.cpuPercent || 0" :show-text="false" /></div>
                      <div class="server-metric"><span>内存</span><strong>{{ formatPercent(selectedMonitorTarget.latest?.memoryPercent) }}</strong><small>{{ formatBytes(selectedMonitorTarget.latest?.memoryUsedBytes) }} / {{ formatBytes(selectedMonitorTarget.latest?.memoryTotalBytes) }}</small><el-progress :percentage="selectedMonitorTarget.latest?.memoryPercent || 0" :show-text="false" status="warning" /></div>
                      <div class="server-metric"><span>磁盘</span><strong>{{ formatPercent(selectedMonitorTarget.latest?.diskPercent) }}</strong><small>{{ formatBytes(selectedMonitorTarget.latest?.diskUsedBytes) }} / {{ formatBytes(selectedMonitorTarget.latest?.diskTotalBytes) }}</small><el-progress :percentage="selectedMonitorTarget.latest?.diskPercent || 0" :show-text="false" status="success" /></div>
                      <div class="server-metric"><span>最近上报</span><strong class="metric-time">{{ formatDate(selectedMonitorTarget.lastSeenAtUtc) }}</strong><small>运行 {{ formatDuration(selectedMonitorTarget.latest?.systemUptimeSeconds) }}</small></div>
                    </div>
                    <div v-if="selectedMetricCards.length" class="extended-metric-grid">
                      <div v-for="metric in selectedMetricCards" :key="metric.key" class="extended-metric-card" :title="metric.description">
                        <span>{{ metric.name }}</span><strong>{{ formatMetricValue(metricValue(selectedMonitorTarget.latest, metric.key), metric.unit) }}</strong><small>{{ metric.category }}</small>
                      </div>
                    </div>
                  </el-card>
                  </el-tab-pane>

                  <el-tab-pane label="趋势分析" name="trends">
                    <div class="trend-workspace">
                      <div class="trend-controls">
                        <el-button :type="metricTrendMode === 'recent' ? 'primary' : 'default'" plain @click="setMetricTrendMode('recent')">近期趋势</el-button>
                        <el-button :type="metricTrendMode === 'history' ? 'primary' : 'default'" plain @click="setMetricTrendMode('history')">历史趋势</el-button>
                        <el-date-picker v-if="metricTrendMode === 'history'" v-model="metricHistoryRange" type="datetimerange" range-separator="至" start-placeholder="开始时间" end-placeholder="结束时间" :clearable="false" />
                        <el-select v-model="metricTrendKeys" class="trend-metric-select" multiple collapse-tags collapse-tags-tooltip :max-collapse-tags="2" filterable placeholder="选择最多 4 个指标" @change="trendMetricSelectionChanged"><el-option v-for="metric in selectableTrendMetrics" :key="metric.key" :label="`${metric.category} / ${metric.name}`" :value="metric.key" /></el-select>
                        <el-button :loading="monitorLoading" @click="metricTrendMode === 'history' ? loadHistoricalTrend() : loadMetricSamples()">刷新</el-button>
                        <span class="trend-summary">{{ trendSummary }}</span>
                      </div>
                      <div class="trend-chart-grid" :class="`chart-count-${selectedTrendMetrics.length}`"><metric-trend-chart v-for="metric in selectedTrendMetrics" :key="metric.key" :metric="metric" :samples="metricTrendSamples" :mode="metricTrendMode" /></div>
                      <el-empty v-if="!selectedTrendMetrics.length" description="请选择需要显示的趋势指标" />
                    </div>
                  </el-tab-pane>

                  <el-tab-pane label="采样明细" name="samples">
                  <div class="monitor-samples fixed-list-page">
                  <el-table :data="metricSamples" stripe border height="100%" class="page-table">
                    <el-table-column prop="collectedAtUtc" label="采集时间" width="190"><template #default="s">{{ formatDate(s.row.collectedAtUtc) }}</template></el-table-column>
                    <el-table-column prop="cpuPercent" label="CPU" width="100"><template #default="s">{{ formatPercent(s.row.cpuPercent) }}</template></el-table-column>
                    <el-table-column prop="memoryPercent" label="内存" width="100"><template #default="s">{{ formatPercent(s.row.memoryPercent) }}</template></el-table-column>
                    <el-table-column prop="diskPercent" label="磁盘" width="100"><template #default="s">{{ formatPercent(s.row.diskPercent) }}</template></el-table-column>
                    <el-table-column label="网关/Agent 内存" min-width="150"><template #default="s">{{ formatBytes(s.row.processWorkingSetBytes) }}</template></el-table-column>
                    <el-table-column label="网络累计接收 / 发送" min-width="220"><template #default="s">{{ formatBytes(s.row.networkReceivedBytes) }} / {{ formatBytes(s.row.networkSentBytes) }}</template></el-table-column>
                    <el-table-column label="系统运行时间" min-width="140"><template #default="s">{{ formatDuration(s.row.systemUptimeSeconds) }}</template></el-table-column>
                  </el-table>
                  <el-pagination class="pagination-panel element-pagination" v-model:current-page="metricSamplePage" v-model:page-size="metricSamplePageSize" :page-sizes="pageSizeOptions" :total="metricSampleTotal" layout="total, sizes, prev, pager, next, jumper" background @current-change="loadMetricSamples" @size-change="metricSampleSizeChanged" />
                  </div>
                  </el-tab-pane>
                </el-tabs>
                <el-empty v-else description="请选择监控节点" />
              </div>
            </div>
          </el-tab-pane>

          <el-tab-pane v-if="canApprove && isPageOpen('approvals')" label="审批记录" name="approvals">
            <div class="toolbar">
              <div><h3>SQL 审批记录</h3><p>同时查看待审批、已通过、已拒绝和执行失败的完整历史。</p></div>
              <div class="toolbar-actions">
                <el-tag v-if="approvalDataSourceFilter" closable type="warning" effect="plain" @close="clearApprovalDataSourceFilter">数据源：{{ approvalDataSourceFilter.name }}</el-tag>
                <el-input v-model="approvalKeyword" class="log-search" clearable placeholder="搜索 SQL、发起者、审批者或意见" @keyup.enter="searchApprovals" />
                <el-select v-model="approvalFilter" class="status-filter" @change="searchApprovals"><el-option label="全部状态" value="all" /><el-option label="待审批" value="Pending" /><el-option label="执行成功" value="Succeeded" /><el-option label="已拒绝" value="Rejected" /><el-option label="执行失败" value="Failed" /><el-option label="已过期" value="Expired" /></el-select>
                <el-button type="primary" @click="searchApprovals">查询</el-button><el-button @click="resetApprovalSearch">重置</el-button>
              </div>
            </div>
            <el-table :data="approvals" stripe border height="100%" class="paged-table page-table" @row-dblclick="openApproval">
              <el-table-column prop="dataSourceName" label="数据源" min-width="150"><template #default="s">{{ s.row.dataSourceName || shortId(s.row.dataSourceId) }}</template></el-table-column>
              <el-table-column prop="requestedBy" label="发起者" min-width="150" />
              <el-table-column prop="status" label="状态" width="110"><template #default="s"><el-tag :type="approvalStatusType(s.row.status)">{{ approvalStatusName(s.row.status) }}</el-tag></template></el-table-column>
              <el-table-column prop="riskLevel" label="风险" width="90"><template #default="s"><el-tag :type="riskType(s.row.riskLevel)" effect="plain">{{ s.row.riskLevel }}</el-tag></template></el-table-column>
              <el-table-column label="SQL 摘要" min-width="300"><template #default="s"><span class="cell-ellipsis">{{ s.row.sql }}</span></template></el-table-column>
              <el-table-column prop="createdAtUtc" label="提交时间" width="180"><template #default="s">{{ formatDate(s.row.createdAtUtc) }}</template></el-table-column>
              <el-table-column label="操作" width="110"><template #default="s"><el-button size="small" type="primary" plain @click="openApproval(s.row)">查看详情</el-button></template></el-table-column>
            </el-table>
            <el-empty v-if="approvals.length === 0" description="暂无审批记录" />
            <el-pagination class="pagination-panel element-pagination" v-model:current-page="approvalPage" v-model:page-size="approvalPageSize" :page-sizes="pageSizeOptions" :total="approvalTotal" layout="total, sizes, prev, pager, next, jumper" background @current-change="loadApprovals" @size-change="approvalSizeChanged" />
          </el-tab-pane>

          <el-tab-pane v-if="canViewLogs && isPageOpen('logs')" label="网关审计" name="logs">
            <div class="toolbar">
              <div><h3>网关调用与审计日志</h3><p>记录 AI 查询、日志读取、变更提单、人工审批、数据源、用户及认证操作。</p></div>
              <div class="toolbar-actions"><el-input v-model="logKeyword" class="log-search" clearable placeholder="搜索人员、动作、SQL 或结果数据" @keyup.enter="searchAuditLogs" /><el-select v-model="logOutcome" class="status-filter" @change="searchAuditLogs"><el-option label="全部结果" value="" /><el-option label="成功" value="success" /><el-option label="失败" value="failure" /><el-option label="待处理" value="pending" /><el-option label="已拒绝" value="rejected" /></el-select><el-button type="primary" @click="searchAuditLogs">查询</el-button><el-button @click="resetLogSearch">重置</el-button></div>
            </div>
            <el-table :data="auditLogs" stripe border height="100%" class="paged-table page-table" @row-dblclick="openLog">
              <el-table-column prop="createdAtUtc" label="时间" width="180"><template #default="s">{{ formatDate(s.row.createdAtUtc) }}</template></el-table-column>
              <el-table-column prop="actor" label="调用者" min-width="150" />
              <el-table-column prop="action" label="动作" min-width="170"><template #default="s">{{ actionName(s.row.action) }}</template></el-table-column>
              <el-table-column prop="outcome" label="结果" width="100"><template #default="s"><el-tag :type="outcomeType(s.row.outcome)">{{ outcomeName(s.row.outcome) }}</el-tag></template></el-table-column>
              <el-table-column prop="dataSourceName" label="数据源" min-width="140"><template #default="s">{{ s.row.dataSourceName || (s.row.dataSourceId ? shortId(s.row.dataSourceId) : '—') }}</template></el-table-column>
              <el-table-column label="详情" min-width="320"><template #default="s"><span class="cell-ellipsis">{{ logSummary(s.row) }}</span></template></el-table-column>
              <el-table-column label="操作" width="90"><template #default="s"><el-button size="small" link type="primary" @click="openLog(s.row)">完整数据</el-button></template></el-table-column>
            </el-table>
            <el-empty v-if="auditLogs.length === 0" description="暂无运行日志" />
            <el-pagination class="pagination-panel element-pagination" v-model:current-page="auditLogPage" v-model:page-size="auditLogPageSize" :page-sizes="pageSizeOptions" :total="auditLogTotal" layout="total, sizes, prev, pager, next, jumper" background @current-change="loadAuditLogs" @size-change="auditLogSizeChanged" />
          </el-tab-pane>
          <el-tab-pane v-if="isAdmin && isPageOpen('settings')" label="系统设置" name="settings">
          <section class="secondary-page">
          <div class="toolbar"><div><h2>系统设置</h2><p>配置桌面辅助功能、审批有效期、记录保存期限与自动清理计划。</p></div></div>
          <el-card class="settings-card desktop-feature-card" shadow="never">
            <template #header><div><strong>Windows 桌面辅助</strong><p class="settings-subtitle">悬浮球由 Windows 客户端直接显示，不经过 Web API，也不会向外部浏览器公开控制能力。</p></div></template>
            <el-form label-width="150px" class="settings-form">
              <el-form-item label="内存使用悬浮球">
                <el-switch v-model="desktopSettings.memoryOverlayEnabled" :disabled="!desktopSettings.available" @change="setMemoryOverlayEnabled" />
                <span class="inline-help">开启后在桌面置顶显示当前内存使用率；可拖动并记住位置，双击打开控制台，右键可关闭。</span>
              </el-form-item>
              <el-alert v-if="!desktopSettings.available" title="当前页面不在 AiDataGateway Windows 客户端中，桌面悬浮球不可用。" type="info" :closable="false" show-icon />
              <el-alert v-else :title="desktopSettings.memoryOverlayEnabled ? '内存悬浮球已显示在桌面。' : '内存悬浮球当前已关闭。'" :type="desktopSettings.memoryOverlayEnabled ? 'success' : 'info'" :closable="false" show-icon />
            </el-form>
          </el-card>
          <el-card class="settings-card storage-migration-card" shadow="never">
            <template #header><div><strong>软件数据库目录</strong><p class="settings-subtitle">迁移内置 SQLite 数据库、加密密钥和业务日志。迁移时会短暂关闭本地服务并自动重启软件。</p></div></template>
            <el-form label-width="150px" class="settings-form">
              <el-form-item label="当前目录"><el-input :model-value="desktopSettings.storagePath || '仅 Windows 客户端可查看'" readonly /></el-form-item>
              <el-form-item label="迁移到">
                <div class="storage-path-picker"><el-input v-model="storageMigrationTarget" placeholder="请选择新的数据库目录" readonly /><el-button :disabled="!desktopSettings.storageMigrationAvailable || storageMigrationBusy" @click="chooseStorageDirectory">选择目录</el-button></div>
                <span class="inline-help">请选择或新建一个空目录。成功后原目录会保留为备份，不会自动删除。</span>
              </el-form-item>
              <el-alert v-if="desktopSettings.storagePathManagedByEnvironment" title="当前路径由 AI_GATEWAY_STORAGE_PATH 环境变量管理，不能在软件内迁移。" type="warning" :closable="false" show-icon />
              <el-alert v-else-if="!desktopSettings.available" title="数据库迁移仅能从 AiDataGateway Windows 客户端执行。" type="info" :closable="false" show-icon />
              <div class="settings-actions"><el-button type="primary" :loading="storageMigrationBusy" :disabled="!desktopSettings.storageMigrationAvailable || !storageMigrationTarget" @click="migrateStorage">迁移并重启</el-button></div>
            </el-form>
          </el-card>
          <el-card class="settings-card admin-recovery-card" shadow="never">
            <template #header><div><strong>管理员密码恢复</strong><p class="settings-subtitle">登录页可使用独立重置口令恢复 Administrator 账号的登录密码。</p></div></template>
            <el-form label-width="150px" class="settings-form">
              <el-alert v-if="adminRecovery.usesDefaultPassword" title="当前仍使用默认重置口令 admin。为了避免他人重置管理员密码，请立即修改。" type="warning" :closable="false" show-icon />
              <el-alert v-else title="已设置自定义管理员重置口令，系统不会显示或返回原口令。" type="success" :closable="false" show-icon />
              <el-form-item label="新的重置口令"><el-input v-model="adminRecovery.newResetPassword" type="password" show-password maxlength="128" placeholder="4–128 位；保存后立即生效" autocomplete="new-password" /></el-form-item>
              <div class="settings-actions"><el-button type="primary" :loading="saving" :disabled="adminRecovery.newResetPassword.length < 4" @click="saveAdminRecoveryPassword">修改重置口令</el-button></div>
            </el-form>
          </el-card>
          <el-card class="settings-card" shadow="never">
            <template #header><div><strong>审批与记录维护</strong><p class="settings-subtitle">审批有效期只影响新提交工单；自动清理会删除过期审计、审批历史、服务器指标和本地日志文件。</p></div></template>
            <el-form :model="maintenanceSettings" label-width="150px" class="settings-form">
              <el-form-item label="审批有效期"><el-input-number v-model="maintenanceSettings.approvalExpirationMinutes" :min="1" :max="10080" /><span class="inline-help">单位：分钟，默认 15 分钟，最长 7 天；过期工单无法再批准或执行。</span></el-form-item>
              <el-divider />
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
          </el-tab-pane>

          <el-tab-pane v-if="isAdmin && isPageOpen('users')" label="用户管理" name="users">
          <section class="secondary-page">
          <div class="toolbar"><div><h2>用户管理</h2><p>管理本地后台账号、状态和角色；有历史记录的用户应禁用而不是删除。</p></div><el-button type="primary" @click="openUserDialog()">新增用户</el-button></div>
          <el-table :data="pagedUsers" stripe height="100%" class="page-table">
            <el-table-column prop="userName" label="用户名" /><el-table-column prop="displayName" label="显示名称" /><el-table-column prop="email" label="邮箱" min-width="200" />
            <el-table-column label="角色" min-width="180"><template #default="s">{{ s.row.roles.join(', ') }}</template></el-table-column>
            <el-table-column label="状态" width="100"><template #default="s"><el-tag :type="s.row.isEnabled?'success':'danger'">{{ s.row.isEnabled?'启用':'禁用' }}</el-tag></template></el-table-column>
            <el-table-column label="操作" width="170"><template #default="s"><el-button size="small" @click="openUserDialog(s.row)">编辑</el-button><el-button size="small" type="danger" :disabled="s.row.id === user.id" @click="deleteUser(s.row)">删除</el-button></template></el-table-column>
          </el-table>
          <el-pagination class="pagination-panel element-pagination" v-model:current-page="userPage" v-model:page-size="userPageSize" :page-sizes="pageSizeOptions" :total="users.length" layout="total, sizes, prev, pager, next, jumper" background />
          </section>
          </el-tab-pane>

          <el-tab-pane v-if="isAdmin && isPageOpen('clients')" label="OAuth2 客户端" name="clients">
          <section class="secondary-page">
          <div class="toolbar"><div><h2>OAuth2 客户端</h2><p>客户端名称和权限可随时调整；修改权限后已签发 Token 会立即撤销。</p></div><el-button type="primary" @click="openClientDialog()">创建客户端</el-button></div>
          <el-table :data="pagedClients" stripe height="100%" class="page-table">
            <el-table-column prop="displayName" label="名称" min-width="180" /><el-table-column label="Client ID" min-width="300"><template #default="s"><div class="cell-with-action"><span class="cell-ellipsis">{{ s.row.clientId }}</span><el-button v-if="isLongCell(s.row.clientId)" link type="primary" @click="openTextViewer('Client ID', s.row.clientId)">查看</el-button></div></template></el-table-column>
            <el-table-column label="权限" min-width="360"><template #default="s"><div class="permission-list"><el-tag v-for="scope in s.row.scopes" :key="scope" effect="plain">{{ oauthScopeName(scope) }}</el-tag><span v-if="!s.row.scopes?.length">无业务权限</span></div></template></el-table-column>
            <el-table-column label="操作" width="180"><template #default="s"><el-button size="small" @click="openClientDialog(s.row)">编辑权限</el-button><el-button size="small" type="danger" @click="deleteClient(s.row)">吊销删除</el-button></template></el-table-column>
          </el-table>
          <el-pagination class="pagination-panel element-pagination" v-model:current-page="clientPage" v-model:page-size="clientPageSize" :page-sizes="pageSizeOptions" :total="clients.length" layout="total, sizes, prev, pager, next, jumper" background />
          </section>
          </el-tab-pane>
          <el-tab-pane v-if="isPageOpen('toolboxwebhooks')" label="WebHook 调试" name="toolboxwebhooks">
            <div class="toolbar"><div><h3>WebHook 调试</h3><p>创建接收地址，让外部系统推送请求到此调试报文；报文仅手动清理，删除 WebHook 会级联删除其全部报文。</p></div><el-button type="primary" @click="openToolboxHook()">新增 WebHook</el-button></div>
            <div v-if="toolboxHooks.length" class="fixed-list-page"><el-table :data="toolboxHooks" stripe border height="100%" class="page-table">
              <el-table-column prop="name" label="名称" min-width="180"><template #default="s"><div class="hook-name-cell"><strong>{{ s.row.name }}</strong><small v-if="s.row.description">{{ s.row.description }}</small></div></template></el-table-column>
              <el-table-column label="调用地址" min-width="380"><template #default="s"><div class="hook-url-row"><span class="cell-ellipsis monospace">{{ toolboxHookUrl(s.row) }}</span><el-button link type="primary" size="small" @click="copyToolboxUrl(s.row)">复制</el-button></div></template></el-table-column>
              <el-table-column label="启用" width="90"><template #default="s"><el-switch :model-value="s.row.enabled" @change="toggleToolboxHook(s.row, $event)" /></template></el-table-column>
              <el-table-column label="报文数" width="90"><template #default="s"><el-tag effect="plain">{{ s.row.deliveryCount }}</el-tag></template></el-table-column>
              <el-table-column label="创建时间" width="180"><template #default="s">{{ formatDate(s.row.createdAtUtc) }}</template></el-table-column>
              <el-table-column label="操作" width="240"><template #default="s"><el-button size="small" type="primary" plain @click="viewToolboxDeliveries(s.row)">查看报文</el-button><el-button size="small" @click="clearToolboxDeliveries(s.row)">清空报文</el-button><el-button size="small" type="danger" @click="deleteToolboxHook(s.row)">删除</el-button></template></el-table-column>
            </el-table></div>
            <el-empty v-else description="还没有 WebHook，点击右上角新增" />
          </el-tab-pane>

          <el-tab-pane v-if="isPageOpen('toolboxtools')" label="格式化与编码" name="toolboxtools">
            <div class="toolbar"><div><h3>格式化与编码工具</h3><p>纯本地处理，输入内容不会离开这台电脑。</p></div></div>
            <el-tabs type="border-card" class="toolbox-tool-tabs">
              <el-tab-pane label="XML 格式化">
                <div class="tool-grid">
                  <el-input v-model="xmlTool.input" type="textarea" :rows="10" placeholder="粘贴需要格式化的 XML" />
                  <div class="tool-actions"><el-button type="primary" @click="formatXmlTool">格式化</el-button><el-button @click="copyToolOutput('xmlTool.output')">复制结果</el-button><el-button @click="clearTool('xml')">清空</el-button></div>
                  <el-alert v-if="xmlTool.error" :title="xmlTool.error" type="error" :closable="false" show-icon />
                  <pre v-if="xmlTool.output" class="beautify-viewer">{{ xmlTool.output }}</pre>
                </div>
              </el-tab-pane>
              <el-tab-pane label="JSON 格式化">
                <div class="tool-grid">
                  <el-input v-model="jsonTool.input" type="textarea" :rows="10" placeholder="粘贴需要格式化的 JSON" />
                  <div class="tool-actions"><el-button type="primary" @click="formatJsonTool">格式化</el-button><el-button @click="copyToolOutput('jsonTool.output')">复制结果</el-button><el-button @click="clearTool('json')">清空</el-button></div>
                  <el-alert v-if="jsonTool.error" :title="jsonTool.error" type="error" :closable="false" show-icon />
                  <pre v-if="jsonTool.output" class="beautify-viewer">{{ jsonTool.output }}</pre>
                </div>
              </el-tab-pane>
              <el-tab-pane label="大小写转换">
                <div class="tool-grid">
                  <el-input v-model="caseTool.input" type="textarea" :rows="8" placeholder="输入文本或标识符" />
                  <div class="tool-actions">
                    <el-button @click="convertCase('upper')">全部大写</el-button>
                    <el-button @click="convertCase('lower')">全部小写</el-button>
                    <el-button @click="convertCase('camel')">下划线转小驼峰</el-button>
                    <el-button @click="convertCase('snake')">驼峰转下划线</el-button>
                  </div>
                  <el-input v-model="caseTool.output" type="textarea" :rows="6" readonly placeholder="转换结果" />
                  <div class="tool-actions"><el-button type="primary" @click="copyToolOutput('caseTool.output')">复制结果</el-button><el-button @click="clearTool('case')">清空</el-button></div>
                </div>
              </el-tab-pane>
              <el-tab-pane label="Base64 编解码">
                <div class="tool-grid">
                  <el-input v-model="base64Tool.input" type="textarea" :rows="8" placeholder="编码：输入明文；解码：输入 Base64" />
                  <div class="tool-actions"><el-button type="primary" @click="encodeBase64">编码</el-button><el-button type="primary" plain @click="decodeBase64">解码</el-button><el-button @click="copyToolOutput('base64Tool.output')">复制结果</el-button><el-button @click="clearTool('base64')">清空</el-button></div>
                  <el-alert v-if="base64Tool.error" :title="base64Tool.error" type="error" :closable="false" show-icon />
                  <el-input v-model="base64Tool.output" type="textarea" :rows="6" readonly placeholder="结果" />
                </div>
              </el-tab-pane>
            </el-tabs>
          </el-tab-pane>

          <el-tab-pane v-if="isPageOpen('custommodules')" label="定制化模块" name="custommodules">
            <div class="toolbar">
              <div><h3>定制化模块中心</h3><p>安装企业或私人扩展包，为管理端增加页面，并向 AI 动态注册 MCP 工具。</p></div>
              <div class="toolbar-actions">
                <input ref="customModuleFile" class="custom-module-file" type="file" accept=".zip,application/zip" @change="installCustomModule" />
                <el-button v-if="isAdmin" type="primary" :loading="customModuleInstalling" @click="$refs.customModuleFile?.click()">安装扩展包</el-button>
              </div>
            </div>
            <el-alert v-if="isAdmin" class="custom-module-warning" type="warning" :closable="false" show-icon title="扩展 DLL 是进程内受信任代码，拥有与网关相同的系统权限；请只安装来源可信、经过审核的扩展包。" />
            <el-table :data="pagedCustomModules" stripe border height="100%" class="page-table">
              <el-table-column label="模块" min-width="220"><template #default="s"><div class="custom-module-name"><strong>{{ s.row.name }}</strong><small>{{ s.row.id }} · v{{ s.row.version }}</small></div></template></el-table-column>
              <el-table-column label="说明" min-width="280"><template #default="s"><div class="cell-with-action"><span class="cell-ellipsis">{{ s.row.description || '—' }}</span><el-button v-if="isLongCell(s.row.description)" link type="primary" @click="openTextViewer('模块说明', s.row.description)">查看</el-button></div></template></el-table-column>
              <el-table-column label="MCP 工具" min-width="220"><template #default="s"><div class="tag-list"><el-tag v-for="tool in s.row.tools" :key="tool.publicName" effect="plain">{{ tool.publicName }}</el-tag><span v-if="!s.row.tools?.length">—</span></div></template></el-table-column>
              <el-table-column label="状态" width="120"><template #default="s"><el-tag :type="s.row.loaded ? 'success' : s.row.enabled ? 'danger' : 'info'">{{ s.row.loaded ? '已加载' : s.row.enabled ? '加载失败' : '已停用' }}</el-tag></template></el-table-column>
              <el-table-column label="安装时间" width="180"><template #default="s">{{ formatDate(s.row.installedAtUtc) }}</template></el-table-column>
              <el-table-column label="操作" width="260"><template #default="s">
                <el-button v-if="s.row.pageUrl && s.row.loaded" size="small" type="primary" plain @click="openCustomModule(s.row)">打开页面</el-button>
                <el-switch v-if="isAdmin" class="custom-module-switch" :model-value="s.row.enabled" active-text="启用" inactive-text="停用" @change="setCustomModuleEnabled(s.row, $event)" />
                <el-button v-if="isAdmin" size="small" type="danger" link @click="deleteCustomModule(s.row)">删除</el-button>
              </template></el-table-column>
              <template #empty><el-empty description="暂无定制化模块，管理员可上传 .zip 扩展包" /></template>
            </el-table>
            <el-alert v-for="module in customModules.filter(item => item.loadError)" :key="module.id" class="custom-module-error" type="error" :closable="false" :title="`${module.name} 加载失败：${module.loadError}`" />
            <el-pagination class="pagination-panel element-pagination" v-model:current-page="customModulePage" v-model:page-size="customModulePageSize" :page-sizes="pageSizeOptions" :total="customModules.length" layout="total, sizes, prev, pager, next, jumper" background />
          </el-tab-pane>

          <el-tab-pane v-for="module in customModulePages.filter(item => isPageOpen(customModuleTab(item)))" :key="customModuleTab(module)" :label="module.pageTitle || module.name" :name="customModuleTab(module)">
            <iframe class="custom-module-frame" :src="module.pageUrl" :title="module.pageTitle || module.name" />
          </el-tab-pane>

        </el-tabs>
        </main>
      </div>

      <el-dialog v-model="dataSourceDialog" title="数据源" width="680px">
        <el-form :model="dataSourceForm" label-width="110px">
          <el-form-item label="标识"><el-input v-model="dataSourceForm.key" /></el-form-item><el-form-item label="名称"><el-input v-model="dataSourceForm.name" /></el-form-item>
          <el-form-item label="类型"><el-select v-model="dataSourceForm.provider" class="full-width" @change="providerChanged"><el-option v-for="p in providers" :key="p.value" :label="p.label" :value="p.value" /></el-select></el-form-item>
          <el-form-item label="IP/主机"><el-input v-model="dataSourceForm.host" /></el-form-item><el-form-item label="端口"><el-input-number v-model="dataSourceForm.port" :min="1" :max="65535" /></el-form-item>
          <el-form-item label="数据库"><el-input v-model="dataSourceForm.database" /></el-form-item><el-form-item label="用户名"><el-input v-model="dataSourceForm.username" /></el-form-item>
          <el-form-item label="密码"><el-input v-model="dataSourceForm.password" type="password" show-password :placeholder="editingDataSource?'留空表示不修改':''" /></el-form-item>
          <el-form-item label="访问模式"><el-select v-model="dataSourceForm.accessMode" class="full-width"><el-option v-for="m in accessModes" :key="m.value" :label="m.label" :value="m.value" /></el-select></el-form-item>
          <el-form-item label="表黑名单"><div class="full-width"><el-input v-model="dataSourceForm.blockedTablesText" type="textarea" :rows="5" placeholder="每行一个表名，例如：&#10;AspNetUsers&#10;main.GatewayAuditEntries" /><p class="field-help">支持表名或 schema.table。命中黑名单的 FROM/JOIN 查询会在连接数据库前直接拦截。</p></div></el-form-item>
          <el-form-item label="最大返回行"><el-input-number v-model="dataSourceForm.maxRows" :min="1" :max="10000" /></el-form-item><el-form-item label="超时秒数"><el-input-number v-model="dataSourceForm.commandTimeoutSeconds" :min="1" :max="300" /></el-form-item>
        </el-form>
        <template #footer><el-button @click="dataSourceDialog=false">取消</el-button><el-button type="primary" :loading="saving" @click="saveDataSource">保存</el-button></template>
      </el-dialog>

      <el-dialog v-model="projectDialog" :title="editingProject ? '编辑项目' : '新增项目'" width="720px" destroy-on-close>
        <el-form :model="projectForm" label-width="110px">
          <el-form-item label="项目编号"><el-input v-model="projectForm.code" placeholder="例如：order-center" /><span class="field-help">AI 使用此编号查找数据库、日志和监控标识，保存后建议不要随意修改。</span></el-form-item>
          <el-form-item label="项目名称"><el-input v-model="projectForm.name" /></el-form-item>
          <el-form-item label="项目说明"><el-input v-model="projectForm.description" type="textarea" :rows="3" /></el-form-item>
          <el-form-item label="关联数据库"><el-select v-model="projectForm.dataSourceIds" multiple filterable class="full-width" placeholder="可关联多个数据库"><el-option v-for="item in dataSources" :key="item.id" :label="`${item.name}（${item.key}）`" :value="item.id" /></el-select></el-form-item>
          <el-form-item label="关联日志源"><el-select v-model="projectForm.logSourceIds" multiple filterable class="full-width" placeholder="可关联多个 NLog / Seq 日志源"><el-option v-for="item in logSources" :key="item.id" :label="`${item.name}（${item.key}）`" :value="item.id" /></el-select></el-form-item>
          <el-form-item label="关联监控节点"><el-select v-model="projectForm.monitorTargetIds" multiple filterable class="full-width" placeholder="可关联本机或多个远端服务器"><el-option v-for="item in monitorTargets" :key="item.id" :label="`${item.name}（${item.key}）`" :value="item.id" /></el-select></el-form-item>
          <el-form-item label="启用"><el-switch v-model="projectForm.enabled" /></el-form-item>
        </el-form>
        <template #footer><el-button @click="projectDialog=false">取消</el-button><el-button type="primary" :loading="saving" @click="saveProject">保存</el-button></template>
      </el-dialog>

      <el-dialog v-model="logSourceDialog" :title="editingLogSource ? '编辑日志源' : '新增日志源'" width="780px" class="log-source-dialog" top="4vh" destroy-on-close>
        <el-form :model="logSourceForm" label-width="130px">
          <el-form-item label="日志标识"><el-input v-model="logSourceForm.key" placeholder="例如：order-api-log" /></el-form-item>
          <el-form-item label="名称"><el-input v-model="logSourceForm.name" /></el-form-item>
          <el-form-item label="采集方式"><div class="log-source-type-picker"><div class="log-source-type-buttons"><el-button :type="logSourceForm.type === 1 ? 'primary' : 'default'" :plain="logSourceForm.type !== 1" @click="selectLogSourceType(1)">本机 NLog</el-button><el-button :type="logSourceForm.type === 2 ? 'warning' : 'default'" :plain="logSourceForm.type !== 2" @click="selectLogSourceType(2)">Seq API</el-button><el-button :type="logSourceForm.type === 3 ? 'success' : 'default'" :plain="logSourceForm.type !== 3" @click="selectLogSourceType(3)">远程 Agent</el-button></div><p>{{ logSourceForm.type === 1 ? '读取网关所在电脑的 NLog 文件。' : logSourceForm.type === 2 ? '调用 Seq API，每条日志源使用独立 API Key。' : '通过单独部署的采集 Agent 追溯远程服务器本地日志。' }}</p></div></el-form-item>
          <template v-if="logSourceForm.type === 1">
            <el-alert title="本地文件夹采集不会连接 Seq。只需填写日志文件夹；默认读取其中最近的 *.log 文件。也可以直接填写完整文件名或通配符。" type="info" show-icon :closable="false" class="source-mode-alert" />
            <el-form-item label="日志文件夹"><el-input v-model="logSourceForm.endpoint" placeholder="例如 D:\Logs\OrderApi，或 D:\Logs\OrderApi\*.log" /><span class="field-help">支持绝对文件夹、完整文件名和通配符；文件夹模式默认匹配 *.log。也支持相对 NLog 配置的路径以及 ${basedir}、${shortdate} 等常见变量。</span></el-form-item>
            <el-form-item label="NLog 配置（可选）"><el-input v-model="logSourceForm.nLogConfiguration" type="textarea" :rows="6" placeholder="仅在需要自动读取 fileName/layout 时填写：可粘贴 NLog XML，或填写 nlog.config 的绝对路径" /></el-form-item>
            <el-form-item label="目标名称"><el-input v-model="logSourceForm.nLogTargetName" placeholder="配置包含多个 File target 时填写 target name" /></el-form-item>
            <el-form-item label="Layout 覆盖"><el-input v-model="logSourceForm.nLogLayout" type="textarea" :rows="3" placeholder="可留空并从 File target 提取，例如 ${longdate}|${level}|${message}|${exception}" /></el-form-item>
          </template>
          <template v-else-if="logSourceForm.type === 2">
            <el-alert title="Seq 模式不会读取本地文件。网关通过 Seq HTTP API 查询事件，API Key 仅加密保存在内置数据库中。" type="warning" show-icon :closable="false" class="source-mode-alert" />
            <el-form-item label="Seq API 地址"><el-input v-model="logSourceForm.endpoint" placeholder="http://127.0.0.1:5341" /><span class="field-help">填写 Seq 服务根地址，系统会调用 GET /api/events。</span></el-form-item>
            <el-form-item label="Seq API Key"><el-input v-model="logSourceForm.apiKey" type="password" show-password :placeholder="editingLogSource && editingLogSource.hasApiKey ? '已加密保存；留空表示不修改' : '建议使用仅具备 Read 权限的 API Key'" /></el-form-item>
          </template>
          <template v-else>
            <el-alert title="请先在远程服务器运行 Monitor Agent 并启用日志服务。网关只通过内网 HTTP 查询，不会访问远程共享文件夹。" type="success" show-icon :closable="false" class="source-mode-alert" />
            <el-form-item label="Agent 地址"><el-input v-model="logSourceForm.endpoint" placeholder="http://192.168.1.20:5188" /><span class="field-help">填写远程 Agent 的监听地址，请仅在可信内网开放该端口。</span></el-form-item>
            <el-form-item label="访问密钥"><el-input v-model="logSourceForm.apiKey" type="password" show-password :placeholder="editingLogSource && editingLogSource.hasApiKey ? '已加密保存；留空表示不修改' : '填写远端 Agent 使用的 --secret'" /></el-form-item>
          </template>
          <el-form-item label="关联项目"><el-select v-model="logSourceForm.projectIds" multiple filterable class="full-width" placeholder="同一日志源可以供多个项目使用"><el-option v-for="item in projects" :key="item.id" :label="`${item.name}（${item.code}）`" :value="item.id" /></el-select></el-form-item>
          <el-form-item label="启用"><el-switch v-model="logSourceForm.enabled" /></el-form-item>
        </el-form>
        <template #footer><el-button @click="logSourceDialog=false">取消</el-button><el-button type="primary" :loading="saving" @click="saveLogSource">保存</el-button></template>
      </el-dialog>

      <el-dialog v-model="monitorTargetDialog" :title="editingMonitorTarget ? '配置监控节点' : '新增远端监控节点'" width="680px" class="monitor-target-dialog" top="4vh" destroy-on-close>
        <el-form :model="monitorTargetForm" label-width="120px">
          <el-form-item label="节点标识"><el-input v-model="monitorTargetForm.key" :disabled="editingMonitorTarget?.type === 1" placeholder="例如：order-prod-01" /><span class="field-help">远端 Agent 使用该标识上报；同一网关内必须唯一。</span></el-form-item>
          <el-form-item label="显示名称"><el-input v-model="monitorTargetForm.name" placeholder="例如：订单生产服务器 01" /></el-form-item>
          <el-form-item label="节点类型"><el-tag :type="editingMonitorTarget?.type === 1 ? 'primary' : 'warning'">{{ editingMonitorTarget?.type === 1 ? '本机内置采集' : '远端 Agent 采集' }}</el-tag></el-form-item>
          <el-form-item label="关联项目"><el-select v-model="monitorTargetForm.projectIds" multiple filterable class="full-width" placeholder="同一节点可关联多个项目"><el-option v-for="item in projects" :key="item.id" :label="`${item.name}（${item.code}）`" :value="item.id" /></el-select></el-form-item>
          <el-form-item label="采集指标">
            <div class="metric-selector full-width">
              <div class="metric-selector-summary"><span>已选择 {{ monitorTargetForm.metricKeys?.length || 0 }} / {{ metricCatalog.length }} 项</span><el-button size="small" link type="primary" @click="selectDefaultMetrics">恢复推荐</el-button></div>
              <div v-for="group in metricCatalogGroups" :key="group.name" class="metric-selector-group">
                <strong>{{ group.name }}</strong>
                <el-checkbox-group v-model="monitorTargetForm.metricKeys">
                  <el-checkbox v-for="metric in group.items" :key="metric.key" :value="metric.key" :disabled="metric.required"><span>{{ metric.name }}</span><small>{{ metric.description }}</small></el-checkbox>
                </el-checkbox-group>
              </div>
              <p class="field-help">基础心跳指标不可取消；远端 Agent 大约每 5 分钟同步一次最新配置。未勾选的扩展指标不会入库。</p>
            </div>
          </el-form-item>
          <el-form-item label="启用采集"><el-switch v-model="monitorTargetForm.enabled" /></el-form-item>
        </el-form>
        <template #footer><el-button @click="monitorTargetDialog=false">取消</el-button><el-button type="primary" :loading="saving" @click="saveMonitorTarget">保存</el-button></template>
      </el-dialog>

      <el-dialog v-model="monitorCredentialDialog" title="请保存远端监控节点凭据" width="760px" :close-on-click-modal="false" destroy-on-close>
        <el-alert title="上报密钥只显示这一次；关闭后只能重置，无法再次查看。" type="warning" show-icon :closable="false" />
        <div v-if="monitorCredential" class="monitor-credential-panel">
          <div><span>网关地址</span><code>{{ gatewayBaseUrl }}</code></div>
          <div><span>节点标识</span><code>{{ monitorCredential.targetKey }}</code></div>
          <div><span>上报密钥</span><code>{{ monitorCredential.ingestSecret }}</code></div>
          <div><span>远端启动命令</span><pre>AiDataGateway.MonitorAgent.exe --gateway {{ gatewayBaseUrl }} --target {{ monitorCredential.targetKey }} --secret "{{ monitorCredential.ingestSecret }}"</pre></div>
        </div>
        <template #footer><el-button type="primary" @click="monitorCredentialDialog=false">我已保存</el-button></template>
      </el-dialog>

      <el-dialog v-model="applicationLogDialog" :fullscreen="logDetailMaximized" :draggable="!logDetailMaximized" width="90%" class="detail-window log-dialog" overflow>
        <template #header><div class="dialog-header-row"><span class="dialog-header-title">应用日志完整数据</span><el-button class="dialog-max-button" link type="primary" @click="toggleDetailMax('logDetailMaximized')"><el-icon><FullScreen /></el-icon>{{ logDetailMaximized ? ' 还原' : ' 全屏' }}</el-button></div></template>
        <el-tabs v-if="selectedApplicationLog" v-model="applicationLogDetailTab" class="log-detail-tabs">
          <el-tab-pane label="概况与消息" name="overview">
            <div class="detail-tab-page">
              <el-descriptions :column="2" border>
                <el-descriptions-item label="事件 ID">{{ selectedApplicationLog.id }}</el-descriptions-item><el-descriptions-item label="时间">{{ formatDate(selectedApplicationLog.timestampUtc) }}</el-descriptions-item>
                <el-descriptions-item label="级别"><el-tag :type="logLevelType(selectedApplicationLog.level)">{{ selectedApplicationLog.level || '未知' }}</el-tag></el-descriptions-item><el-descriptions-item label="解析状态"><el-tag :type="selectedApplicationLog.incomplete ? 'warning' : 'success'">{{ selectedApplicationLog.incomplete ? '记录不完整' : '结构化成功' }}</el-tag></el-descriptions-item>
                <el-descriptions-item v-if="selectedApplicationLog.parseWarning" label="解析提示" :span="2"><span class="warning-text">{{ selectedApplicationLog.parseWarning }}</span></el-descriptions-item>
                <el-descriptions-item v-if="selectedApplicationLog.exception" label="异常" :span="2"><pre class="inline-exception">{{ selectedApplicationLog.exception }}</pre></el-descriptions-item>
              </el-descriptions>
              <div class="detail-section"><h4>消息</h4><pre class="log-detail">{{ selectedApplicationLog.message || '—' }}</pre></div>
            </div>
          </el-tab-pane>
          <el-tab-pane label="结构化属性" name="properties">
            <div class="detail-tab-page fixed-list-page"><el-table :data="pagedApplicationLogProperties" stripe border height="100%" class="page-table"><el-table-column prop="key" label="字段" min-width="180" /><el-table-column label="值" min-width="500"><template #default="s"><div class="cell-with-action"><span class="cell-ellipsis monospace">{{ formatCell(s.row.value) }}</span><el-button v-if="isLongCell(s.row.value) || detectStructuredValue(s.row.value)" link type="primary" size="small" @click="openPropertyValueViewer(s.row)">查看</el-button></div></template></el-table-column></el-table><el-pagination v-if="applicationLogProperties.length > detailPageSize" class="pagination-panel element-pagination compact-pagination" v-model:current-page="applicationLogPropertyPage" :page-size="detailPageSize" :total="applicationLogProperties.length" layout="total, prev, pager, next" background /></div>
          </el-tab-pane>
          <el-tab-pane v-if="selectedApplicationLogSql" label="SQL 分析" name="sql">
            <div class="detail-tab-page sql-trace-page">
              <div class="sql-trace-toolbar"><el-select v-model="logSqlProjectId" placeholder="选择关联项目" filterable @change="logSqlProjectChanged"><el-option v-for="project in logSqlProjects" :key="project.id" :value="project.id" :label="`${project.code} / ${project.name}`" /></el-select><el-select v-model="logSqlDataSourceId" placeholder="选择项目数据源" filterable><el-option v-for="source in logSqlDataSources" :key="source.id" :value="source.id" :label="`${source.key} / ${source.name}（${source.provider}）`" /></el-select><el-tag :type="selectedApplicationLogSqlIsReadOnly ? 'success' : 'warning'" effect="plain">{{ selectedApplicationLogSqlIsReadOnly ? '只读 SQL' : '仅格式化预览' }}</el-tag><el-button type="primary" :disabled="!canExecuteSelectedLogSql" :loading="logSqlLoading" @click="executeSelectedLogSql">尝试只读执行</el-button></div>
              <el-alert title="只允许执行 SELECT 等只读语句；仍会经过网关的 SQL 安全检查、表黑名单和最大返回行数限制。数据源仅来自该日志源关联项目。" type="info" :closable="false" show-icon />
              <pre class="beautify-viewer sql-preview" v-html="selectedApplicationLogSqlHtml"></pre>
              <div v-if="logSqlResult" class="sql-result fixed-list-page"><el-table :data="pagedLogSqlRows" stripe border height="100%" class="page-table"><el-table-column v-for="column in logSqlResult.columns" :key="column" :label="column" min-width="150"><template #default="s"><div class="cell-with-action"><span class="cell-ellipsis monospace">{{ formatCell(s.row[column]) }}</span><el-button v-if="isLongCell(s.row[column])" link type="primary" @click="openTextViewer(column, s.row[column])">查看</el-button></div></template></el-table-column></el-table><el-pagination class="pagination-panel element-pagination compact-pagination" v-model:current-page="logSqlResultPage" :page-size="detailPageSize" :total="logSqlResult.rows.length" layout="total, prev, pager, next" background /></div>
            </div>
          </el-tab-pane>
          <el-tab-pane label="原始记录" name="raw"><div class="detail-tab-page"><pre class="log-detail raw-log-detail">{{ selectedApplicationLog.rawText || '—' }}</pre></div></el-tab-pane>
        </el-tabs>
        <template #footer><el-button @click="applicationLogDialog=false">关闭</el-button></template>
      </el-dialog>

      <el-dialog v-model="approvalDialog" :fullscreen="approvalMaximized" :draggable="!approvalMaximized" width="900px" class="detail-window approval-detail-window" overflow destroy-on-close>
        <template #header><div class="dialog-header-row"><span class="dialog-header-title">SQL 审批详情</span><el-button class="dialog-max-button" link type="primary" @click="toggleDetailMax('approvalMaximized')"><el-icon><FullScreen /></el-icon>{{ approvalMaximized ? ' 还原' : ' 全屏' }}</el-button></div></template>
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

      <el-dialog v-model="logDialog" :fullscreen="auditLogMaximized" :draggable="!auditLogMaximized" width="90%" class="detail-window log-dialog" overflow>
        <template #header><div class="dialog-header-row"><span class="dialog-header-title">运行日志完整数据</span><el-button class="dialog-max-button" link type="primary" @click="toggleDetailMax('auditLogMaximized')"><el-icon><FullScreen /></el-icon>{{ auditLogMaximized ? ' 还原' : ' 全屏' }}</el-button></div></template>
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
            <el-table :data="pagedSelectedLogRows" stripe border max-height="420" class="query-result-table">
              <el-table-column v-for="column in selectedLogColumns" :key="column" :label="column" min-width="150"><template #default="s"><div class="cell-with-action"><span class="cell-ellipsis">{{ formatCell(s.row[column]) }}</span><el-button v-if="isLongCell(s.row[column])" link type="primary" @click="openTextViewer(column, s.row[column])">查看</el-button></div></template></el-table-column>
            </el-table>
            <el-empty v-if="selectedLogDetail.rows.length === 0" description="查询结果为空" />
            <el-pagination v-if="selectedLogDetail.rows.length > detailPageSize" class="pagination-panel element-pagination compact-pagination" v-model:current-page="selectedLogRowPage" :page-size="detailPageSize" :total="selectedLogDetail.rows.length" layout="total, prev, pager, next" background />
          </div>
          <div v-if="selectedLogDetail.raw" class="detail-section"><h4>历史日志详情</h4><pre class="log-detail">{{ selectedLogDetail.raw }}</pre></div>
        </div>
        <template #footer><el-button @click="logDialog=false">关闭</el-button></template>
      </el-dialog>

      <el-dialog v-model="propertyViewerDialog" width="840px" top="6vh" class="property-viewer-dialog" destroy-on-close>
        <template #header><span class="dialog-header-title">{{ propertyViewer.key }} · {{ propertyViewer.kind === 'json' ? 'JSON 格式化' : propertyViewer.kind === 'sql' ? 'SQL 格式化' : '完整内容' }}</span></template>
        <pre class="beautify-viewer" v-html="propertyViewerHtml"></pre>
        <template #footer>
          <el-button @click="copyPropertyValue">复制</el-button>
          <el-button type="primary" @click="propertyViewerDialog = false">关闭</el-button>
        </template>
      </el-dialog>
      <el-dialog v-model="toolboxHookDialog" :title="editingToolboxHook ? '编辑 WebHook' : '新增 WebHook'" width="560px" destroy-on-close>
        <el-form :model="toolboxHookForm" label-width="90px">
          <el-form-item label="名称"><el-input v-model="toolboxHookForm.name" maxlength="100" placeholder="例如：订单回调调试" /></el-form-item>
          <el-form-item label="描述"><el-input v-model="toolboxHookForm.description" maxlength="200" placeholder="可选" /></el-form-item>
        </el-form>
        <template #footer><el-button @click="toolboxHookDialog=false">取消</el-button><el-button type="primary" :loading="saving" @click="saveToolboxHook">{{ editingToolboxHook ? '保存' : '创建' }}</el-button></template>
      </el-dialog>

      <el-dialog v-model="toolboxDeliveryDialog" width="90%" class="detail-window" :fullscreen="toolboxDeliveryMaximized" :draggable="!toolboxDeliveryMaximized" destroy-on-close>
        <template #header><div class="dialog-header-row"><span class="dialog-header-title">报文记录 · {{ toolboxDeliveryHook?.name || '' }}</span><div class="dialog-header-actions"><el-button size="small" type="warning" plain @click="clearToolboxDeliveries(toolboxDeliveryHook)">清空全部报文</el-button><el-button class="dialog-max-button" link type="primary" @click="toggleDetailMax('toolboxDeliveryMaximized')"><el-icon><FullScreen /></el-icon>{{ toolboxDeliveryMaximized ? ' 还原' : ' 全屏' }}</el-button></div></div></template>
        <div class="fixed-list-page"><el-table :data="toolboxDeliveries" stripe border height="100%" class="page-table">
          <el-table-column prop="receivedAtUtc" label="接收时间" width="190"><template #default="s">{{ formatDate(s.row.receivedAtUtc) }}</template></el-table-column>
          <el-table-column prop="method" label="方法" width="90" />
          <el-table-column label="Content-Type" min-width="160"><template #default="s">{{ s.row.contentType || '—' }}</template></el-table-column>
          <el-table-column label="查询参数" min-width="160"><template #default="s">{{ s.row.queryString || '—' }}</template></el-table-column>
          <el-table-column label="报文" min-width="440"><template #default="s"><div class="cell-with-action"><span class="cell-ellipsis monospace">{{ s.row.bodyTruncated ? s.row.body + ' …（已截断）' : s.row.body }}</span><el-button v-if="s.row.body" link type="primary" size="small" @click="openTextViewer('报文', s.row.body)">查看</el-button></div></template></el-table-column>
        </el-table></div>
        <template #footer><el-button @click="toolboxDeliveryDialog=false">关闭</el-button></template>
      </el-dialog>

      <el-dialog v-model="userDialog" :title="editingUser ? '编辑用户' : '新增用户'" width="580px">
        <el-form :model="newUser" label-width="100px"><el-form-item label="用户名"><el-input v-model="newUser.userName" :disabled="!!editingUser" /></el-form-item><el-form-item label="显示名称"><el-input v-model="newUser.displayName" /></el-form-item><el-form-item label="邮箱"><el-input v-model="newUser.email" :disabled="!!editingUser" /></el-form-item><el-form-item v-if="!editingUser" label="密码"><el-input v-model="newUser.password" type="password" show-password /></el-form-item><el-form-item label="角色"><el-select v-model="newUser.roles" multiple class="full-width"><el-option v-for="r in roles" :key="r" :label="r" :value="r" /></el-select></el-form-item><el-form-item v-if="editingUser" label="账号状态"><el-switch v-model="newUser.enabled" active-text="启用" inactive-text="禁用" /></el-form-item></el-form>
        <template #footer><el-button @click="userDialog=false">取消</el-button><el-button type="primary" :loading="saving" @click="saveUser">{{ editingUser ? '保存' : '创建' }}</el-button></template>
      </el-dialog>
      <el-dialog v-model="clientDialog" :title="editingClient ? '编辑 OAuth2 客户端' : '创建 OAuth2 客户端'" width="680px" destroy-on-close>
        <el-form :model="clientForm" label-width="100px">
          <el-form-item label="客户端名称"><el-input v-model="clientForm.displayName" placeholder="例如：本地开发 AI" /></el-form-item>
          <el-form-item v-if="editingClient" label="Client ID"><el-input :model-value="editingClient.clientId" disabled /></el-form-item>
          <el-form-item label="权限范围"><el-checkbox-group v-model="clientForm.scopes" class="oauth-scope-list"><el-checkbox v-for="item in oauthScopeOptions" :key="item.value" :value="item.value"><span>{{ item.label }}</span><small>{{ item.description }}</small></el-checkbox></el-checkbox-group></el-form-item>
          <el-alert v-if="editingClient" title="保存后该客户端已签发的 Token 会被撤销，AI 需要重新获取 Token；Client Secret 不会改变。" type="warning" show-icon :closable="false" />
        </el-form>
        <template #footer><el-button @click="clientDialog=false">取消</el-button><el-button type="primary" :loading="saving" @click="saveClient">{{ editingClient ? '保存权限' : '创建并显示 Secret' }}</el-button></template>
      </el-dialog>
    </div>
  </div>
  </el-config-provider>
</template>

<script>
import axios from 'axios'
import { ElMessage, ElMessageBox } from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import MetricTrendChart from './components/MetricTrendChart.vue'

axios.defaults.withCredentials = true

const storedTrendKeys = () => {
  try {
    const value = JSON.parse(localStorage.getItem('gateway.metricTrendKeys') || '[]')
    return Array.isArray(value) && value.every(item => typeof item === 'string') ? value.slice(0, 4) : []
  } catch (_) { return [] }
}

export default {
  components: { MetricTrendChart },
  data: () => ({
    loading: true, saving: false, needsSetup: false, user: null, activeTab: 'overview', openTabs: ['overview'], sidebarCollapsed: localStorage.getItem('gateway.sidebarCollapsed') === 'true', uiTheme: localStorage.getItem('gateway.uiTheme') === 'dark' ? 'dark' : 'light', zhCn, generatedClient: null,
    eventSource: null, eventConnected: false, eventRefreshTimer: null,
    setup: { userName: 'admin', email: '', displayName: '管理员', password: '', aiClientName: 'Local AI Client' },
    setupRules: {
      userName: [{ required: true, message: '请输入用户名', trigger: 'blur' }, { pattern: /^[a-zA-Z0-9._@+-]+$/, message: '用户名只能包含英文字母、数字及 . - _ @ +', trigger: 'blur' }],
      email: [{ required: true, message: '请输入邮箱地址', trigger: 'blur' }, { type: 'email', message: '请输入有效的邮箱地址', trigger: ['blur', 'change'] }],
      displayName: [{ required: true, message: '请输入显示名称', trigger: 'blur' }],
      password: [{ required: true, message: '请输入管理员密码', trigger: 'blur' }, { min: 6, message: '密码长度至少为 6 位', trigger: 'blur' }]
    },
    loginForm: { userName: 'admin', password: '', rememberMe: true },
    adminResetDialog: false, adminResetForm: { userName: 'admin', resetPassword: '', newPassword: '', confirmPassword: '' },
    dataSources: [], projects: [], logSources: [], monitorTargets: [], metricSamples: [], metricTrendSamples: [], metricCatalog: [], metricCatalogDefaultKeys: [], metricCatalogRequiredKeys: [], approvals: [], auditLogs: [], applicationLogs: [], users: [], clients: [], roles: [], customModules: [],
    pageSizeOptions: [10, 20, 50, 100], projectPage: 1, projectPageSize: 20, dataSourcePage: 1, dataSourcePageSize: 20, logSourcePage: 1, logSourcePageSize: 20, userPage: 1, userPageSize: 20, clientPage: 1, clientPageSize: 20,
    monitorTargetPage: 1, monitorTargetPageSize: 8, customModulePage: 1, customModulePageSize: 20, customModuleInstalling: false,
    approvalFilter: 'all', approvalKeyword: '', approvalDataSourceFilter: null, approvalPage: 1, approvalPageSize: 20, approvalTotal: 0, approvalAllTotal: 0, pendingApprovalTotal: 0,
    logKeyword: '', logOutcome: '', auditLogPage: 1, auditLogPageSize: 20, auditLogTotal: 0, auditLogAllTotal: 0,
    dataSourceDialog: false, editingDataSource: null, dataSourceForm: {},
    projectDialog: false, editingProject: null, projectForm: {},
    logSourceDialog: false, editingLogSource: null, logSourceForm: {},
    monitorTargetDialog: false, editingMonitorTarget: null, monitorTargetForm: {}, monitorCredentialDialog: false, monitorCredential: null,
    selectedMonitorTargetId: '', monitorSection: 'overview', monitorLoading: false, metricSamplePage: 1, metricSamplePageSize: 20, metricSampleTotal: 0, metricTrendMode: 'recent', metricTrendKey: 'cpu.percent', metricTrendKeys: storedTrendKeys(), metricTrendSourceCount: 0, metricHistoryRange: [new Date(Date.now() - 24 * 60 * 60 * 1000), new Date()], metricHoverPoint: null,
    applicationLogSourceId: '', applicationLogQueryMode: 'simple', applicationLogQuery: '', applicationLogSearchText: '', applicationLogPropertyName: '', applicationLogPropertyValue: '', applicationLogTopic: '', applicationLogLevel: '', applicationLogRange: [new Date(Date.now() - 24 * 60 * 60 * 1000), new Date()], applicationLogPage: 1, applicationLogPageSize: 50, applicationLogTotal: 0, applicationLogPartial: false, applicationLogWarning: null, applicationLogsLoading: false,
    realtimeLogs: [], realtimeLogPage: 1, realtimeLogPageSize: 50, realtimeLogSourceId: '', realtimeLogSearchText: '', realtimeLogPropertyName: '', realtimeLogPropertyValue: '', realtimeLogTopic: '', realtimeLogLevel: '', realtimeLogEventSource: null, realtimeLogConnected: false, realtimeLogConnecting: false, realtimeLogAttempt: 0, realtimeLogError: null,
    applicationLogDialog: false, selectedApplicationLog: null, selectedApplicationLogSourceId: '', applicationLogDetailTab: 'overview', applicationLogPropertyPage: 1, logDetailMaximized: false, logSqlProjects: [], logSqlProjectId: '', logSqlDataSourceId: '', logSqlResult: null, logSqlResultPage: 1, logSqlLoading: false, approvalMaximized: false, auditLogMaximized: false, propertyViewerDialog: false, propertyViewer: { key: '', kind: null, text: '', pretty: '' }, toolboxHooks: [], toolboxDeliveries: [], toolboxDeliveryHook: null, toolboxDeliveryDialog: false, toolboxDeliveryMaximized: false, toolboxHookDialog: false, editingToolboxHook: null, toolboxHookForm: { name: '', description: '' }, xmlTool: { input: '', output: '', error: '' }, jsonTool: { input: '', output: '', error: '' }, caseTool: { input: '', output: '' }, base64Tool: { input: '', output: '', error: '' }, propertyViewer: { key: '', kind: null, text: '', pretty: '' },
    approvalDialog: false, selectedApproval: null, reviewComment: '', logDialog: false, selectedLog: null, selectedLogRowPage: 1, detailPageSize: 20,
    userDialog: false, editingUser: null, newUser: { userName: '', email: '', displayName: '', password: '', roles: ['Developer'], enabled: true },
    clientDialog: false, editingClient: null, clientForm: { displayName: '', scopes: [] },
    maintenanceSettings: { cleanupEnabled: true, retentionDays: 3, cleanupTimeLocal: '03:00', approvalExpirationMinutes: 15, lastCleanupAtUtc: null, lastCleanupSummary: null },
    adminRecovery: { usesDefaultPassword: true, newResetPassword: '' },
    desktopSettings: { available: false, memoryOverlayEnabled: false, storagePath: '', storageMigrationAvailable: false, storagePathManagedByEnvironment: false }, desktopMessageHandler: null, storageMigrationTarget: '', storageMigrationBusy: false,
    providers: [{ value: 1, label: 'SQL Server', port: 1433 }, { value: 2, label: 'MySQL', port: 3306 }, { value: 3, label: 'PostgreSQL', port: 5432 }, { value: 4, label: 'SQLite', port: 1 }, { value: 5, label: 'Oracle', port: 1521 }, { value: 6, label: 'MariaDB', port: 3306 }, { value: 7, label: '达梦 DM8', port: 5236 }, { value: 8, label: 'Firebird', port: 3050 }],
    accessModes: [{ value: 0, label: '禁用' }, { value: 1, label: '只读' }, { value: 2, label: '写入需审批' }, { value: 3, label: '开发模式' }],
    logLevels: ['Trace', 'Debug', 'Information', 'Info', 'Warning', 'Warn', 'Error', 'Fatal'],
    logDateShortcuts: [{ text: '最近 15 分钟', value: () => [new Date(Date.now() - 15 * 60 * 1000), new Date()] }, { text: '最近 1 小时', value: () => [new Date(Date.now() - 60 * 60 * 1000), new Date()] }, { text: '最近 24 小时', value: () => [new Date(Date.now() - 24 * 60 * 60 * 1000), new Date()] }, { text: '最近 7 天', value: () => [new Date(Date.now() - 7 * 24 * 60 * 60 * 1000), new Date()] }],
    oauthScopeOptions: [{ value: 'gateway.datasource.read', label: '读取数据源', description: '查看可用项目和数据源标识' }, { value: 'gateway.query.execute', label: '执行只读查询', description: '运行受网关约束的 SELECT 查询' }, { value: 'gateway.change.submit', label: '提交变更工单', description: '提交写入 SQL 等待人工审批' }, { value: 'gateway.logs.read', label: '读取应用日志', description: '查询项目关联的本地、Seq 或远程日志' }, { value: 'gateway.metrics.read', label: '读取服务器指标', description: '查询项目关联的服务器监控数据' }]
  }),
  computed: {
    isAdmin () { return this.user?.roles?.includes('Administrator') },
    canOperate () { return this.isAdmin || this.user?.roles?.includes('Operator') },
    canApprove () { return this.isAdmin || this.user?.roles?.includes('Approver') },
    canViewLogs () { return this.isAdmin || this.user?.roles?.includes('Approver') || this.user?.roles?.includes('Operator') || this.user?.roles?.includes('Auditor') },
    canViewMetrics () { return this.canViewLogs || this.user?.roles?.includes('Viewer') },
    approvalPageCount () { return Math.max(1, Math.ceil(this.approvalTotal / this.approvalPageSize)) },
    auditLogPageCount () { return Math.max(1, Math.ceil(this.auditLogTotal / this.auditLogPageSize)) },
    pagedProjects () { return this.paginate(this.projects, this.projectPage, this.projectPageSize) },
    pagedDataSources () { return this.paginate(this.dataSources, this.dataSourcePage, this.dataSourcePageSize) },
    pagedLogSources () { return this.paginate(this.logSources, this.logSourcePage, this.logSourcePageSize) },
    pagedUsers () { return this.paginate(this.users, this.userPage, this.userPageSize) },
    pagedClients () { return this.paginate(this.clients, this.clientPage, this.clientPageSize) },
    pagedCustomModules () { return this.paginate(this.customModules, this.customModulePage, this.customModulePageSize) },
    customModulePages () { return this.customModules.filter(item => item.enabled && item.loaded && item.pageUrl) },
    pagedRealtimeLogs () { return this.paginate(this.realtimeLogs, this.realtimeLogPage, this.realtimeLogPageSize) },
    pagedMonitorTargets () { return this.paginate(this.monitorTargets, this.monitorTargetPage, this.monitorTargetPageSize) },
    selectedApplicationLogSource () { return this.logSources.find(item => item.id === this.applicationLogSourceId) },
    selectedRealtimeLogSource () { return this.logSources.find(item => item.id === this.realtimeLogSourceId) },
    selectedMonitorTarget () { return this.monitorTargets.find(item => item.id === this.selectedMonitorTargetId) },
    onlineMonitorCount () { return this.monitorTargets.filter(item => item.online).length },
    gatewayBaseUrl () { return window.location.origin },
    metricCatalogGroups () {
      return [...new Set(this.metricCatalog.map(item => item.category))].map(name => ({ name, items: this.metricCatalog.filter(item => item.category === name) }))
    },
    selectedMetricCards () {
      const hidden = new Set(['cpu.percent', 'memory.percent', 'disk.percent', 'system.uptime_seconds'])
      const selected = new Set(this.selectedMonitorTarget?.metricKeys || [])
      return this.metricCatalog.filter(item => selected.has(item.key) && !hidden.has(item.key) && this.metricValue(this.selectedMonitorTarget?.latest, item.key) !== null)
    },
    selectableTrendMetrics () {
      const selected = new Set(this.selectedMonitorTarget?.metricKeys || [])
      return this.metricCatalog.filter(item => selected.has(item.key))
    },
    selectedTrendMetrics () { const selected = new Set(this.metricTrendKeys); return this.selectableTrendMetrics.filter(item => selected.has(item.key)).slice(0, 4) },
    selectedTrendMetric () { return this.metricCatalog.find(item => item.key === this.metricTrendKey) },
    metricTrendValues () { return this.metricTrendSamples.map(item => this.metricValue(item, this.metricTrendKey)).filter(value => value !== null) },
    metricTrendScale () {
      const values = this.metricTrendValues
      if (!values.length) return { min: 0, max: this.selectedTrendMetric?.unit === 'percent' ? 100 : 1 }
      const observedMin = Math.min(...values)
      const observedMax = Math.max(...values)
      const minimumSpan = this.selectedTrendMetric?.unit === 'percent' ? 10 : Math.max(Math.abs(observedMax) * 0.1, 1)
      const paddedSpan = Math.max(observedMax - observedMin, minimumSpan)
      let minimum = Math.max(0, observedMin - paddedSpan * 0.18)
      let maximum = observedMax + paddedSpan * 0.18
      const roughStep = Math.max((maximum - minimum) / 4, Number.EPSILON)
      const magnitude = 10 ** Math.floor(Math.log10(roughStep))
      const normalized = roughStep / magnitude
      const step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude
      minimum = Math.floor(minimum / step) * step
      maximum = Math.ceil(maximum / step) * step
      if (this.selectedTrendMetric?.unit === 'percent') {
        maximum = Math.min(100, maximum)
        if (maximum - minimum < 10) {
          if (maximum >= 100) minimum = 90
          else maximum = Math.min(100, minimum + 10)
        }
      }
      if (maximum <= minimum) maximum = minimum + step
      return { min: minimum, max: maximum }
    },
    metricTrendMin () { return this.metricTrendScale.min },
    metricTrendMax () { return this.metricTrendScale.max },
    metricTrendChartPoints () {
      const samples = [...this.metricTrendSamples]
        .sort((left, right) => new Date(left.collectedAtUtc) - new Date(right.collectedAtUtc))
        .map((sample, sourceIndex) => ({ sample, sourceIndex, value: this.metricValue(sample, this.metricTrendKey) }))
        .filter(item => item.value !== null)
      const range = Math.max(1e-9, this.metricTrendMax - this.metricTrendMin)
      return samples.map((item, index) => ({
        key: `${item.sample.collectedAtUtc}-${item.sourceIndex}`,
        timestamp: item.sample.collectedAtUtc,
        value: item.value,
        x: samples.length === 1 ? 431 : 72 + index * 718 / (samples.length - 1),
        y: 232 - (item.value - this.metricTrendMin) * 212 / range
      }))
    },
    metricTrendPoints () { return this.metricTrendChartPoints.map(point => `${point.x},${point.y}`).join(' ') },
    metricTrendAreaPoints () {
      const points = this.metricTrendChartPoints
      return points.length > 1 ? `${points[0].x},232 ${this.metricTrendPoints} ${points.at(-1).x},232` : ''
    },
    metricTrendReferenceLines () {
      const points = this.metricTrendChartPoints
      if (this.metricTrendMode !== 'history' || !points.length) return []
      const minimum = Math.min(...points.map(point => point.value))
      const maximum = Math.max(...points.map(point => point.value))
      const yFor = value => 232 - (value - this.metricTrendMin) * 212 / Math.max(1e-9, this.metricTrendMax - this.metricTrendMin)
      if (Math.abs(maximum - minimum) < 1e-9) {
        return [{ key: 'same', y: yFor(maximum), label: `最高/最低 ${this.formatMetricValue(maximum, this.selectedTrendMetric?.unit)}` }]
      }
      return [
        { key: 'maximum', y: yFor(maximum), label: `最高 ${this.formatMetricValue(maximum, this.selectedTrendMetric?.unit)}` },
        { key: 'minimum', y: yFor(minimum), label: `最低 ${this.formatMetricValue(minimum, this.selectedTrendMetric?.unit)}` }
      ]
    },
    metricYAxisTicks () {
      const range = this.metricTrendMax - this.metricTrendMin
      return Array.from({ length: 5 }, (_, index) => {
        const value = this.metricTrendMax - range * index / 4
        return { y: 20 + index * 53, value, label: this.formatMetricValue(value, this.selectedTrendMetric?.unit) }
      })
    },
    metricXAxisTicks () {
      const points = this.metricTrendChartPoints
      if (!points.length) return []
      const count = Math.min(5, points.length)
      const indices = [...new Set(Array.from({ length: count }, (_, index) => Math.round(index * (points.length - 1) / Math.max(1, count - 1))))]
      return indices.map((pointIndex, index) => ({
        index: pointIndex,
        x: points[pointIndex].x,
        label: this.formatChartAxisTime(points[pointIndex].timestamp),
        anchor: index === 0 ? 'start' : index === indices.length - 1 ? 'end' : 'middle'
      }))
    },
    metricTooltipStyle () {
      if (!this.metricHoverPoint) return {}
      return { left: `${this.metricHoverPoint.x / 8}%`, top: `${this.metricHoverPoint.y / 2.8}%` }
    },
    trendRangeLabel () { return `${this.formatMetricValue(this.metricTrendMin, this.selectedTrendMetric?.unit)} – ${this.formatMetricValue(this.metricTrendMax, this.selectedTrendMetric?.unit)}` },
    trendSummary () {
      if (this.metricTrendMode === 'recent') return `最近 ${this.metricTrendSamples.length} 个采样点，按采样顺序展开`
      return `时间范围内 ${this.metricTrendSourceCount} 条原始记录，显示 ${this.metricTrendSamples.length} 个趋势点，虚线标注最高值和最低值`
    },
    applicationLogHasNext () { return this.applicationLogPartial || this.applicationLogs.length >= this.applicationLogPageSize },
    applicationLogPaginationTotal () { return this.applicationLogPartial ? Math.max(this.applicationLogTotal, this.applicationLogPage * this.applicationLogPageSize + 1) : this.applicationLogTotal },
    applicationLogProperties () { return Object.entries(this.selectedApplicationLog?.properties || {}).map(([key, value]) => ({ key, value })) },
    pagedApplicationLogProperties () { return this.paginate(this.applicationLogProperties, this.applicationLogPropertyPage, this.detailPageSize) },
    selectedApplicationLogSql () { return this.extractLogSql(this.selectedApplicationLog) },
    selectedApplicationLogSqlHtml () { return this.highlightSql(this.formatSql(this.selectedApplicationLogSql)) },
    selectedApplicationLogSqlIsReadOnly () { return /^\s*(select|with)\b/i.test(this.selectedApplicationLogSql) },
    selectedLogSqlProject () { return this.logSqlProjects.find(item => item.id === this.logSqlProjectId) },
    logSqlDataSources () { return this.selectedLogSqlProject?.dataSources || [] },
    canExecuteSelectedLogSql () { return this.canOperate && this.selectedApplicationLogSqlIsReadOnly && !!this.logSqlProjectId && !!this.logSqlDataSourceId },
    pagedLogSqlRows () { return this.paginate(this.logSqlResult?.rows || [], this.logSqlResultPage, this.detailPageSize) },
    propertyViewerHtml () {
      const text = this.propertyViewer.text || ''
      if (this.propertyViewer.kind === 'json') {
        try {
          const pretty = JSON.stringify(JSON.parse(text), null, 2)
          return this.escapeHtml(pretty).replace(/("(?:[^"\\]|\.)*")(\s*:)?|\b(true|false|null)\b|-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b/g, match => {
            let cls = 'token-number'
            if (match.startsWith('"')) cls = match.endsWith(':') ? 'token-key' : 'token-string'
            else if (match === 'true' || match === 'false') cls = 'token-boolean'
            else if (match === 'null') cls = 'token-null'
            return `<span class="${cls}">${match}</span>`
          })
        } catch (error) { return this.escapeHtml(text) }
      }
      if (this.propertyViewer.kind === 'sql') {
        return this.highlightSql(this.formatSql(text))
      }
      return this.escapeHtml(text)
    },
    userInitials () { return (this.user?.displayName || this.user?.userName || 'U').trim().slice(0, 2).toUpperCase() },
    selectedLogDetail () { return this.parseLogDetail(this.selectedLog?.detail) },
    selectedLogColumns () {
      if (Array.isArray(this.selectedLogDetail.columns) && this.selectedLogDetail.columns.length) return this.selectedLogDetail.columns
      return Object.keys(this.selectedLogDetail.rows?.[0] || {})
    },
    pagedSelectedLogRows () { return this.paginate(this.selectedLogDetail.rows || [], this.selectedLogRowPage, this.detailPageSize) }
  },
  async created () { this.applyUiTheme(); this.initializeDesktopBridge(); await this.bootstrap() },
  beforeUnmount () { this.disconnectEvents(); this.stopRealtimeLogStream(); this.disposeDesktopBridge() },
  methods: {
    initializeDesktopBridge () {
      if (!window.chrome?.webview) return
      this.desktopSettings.available = true
      this.desktopMessageHandler = event => this.handleDesktopMessage(event)
      window.chrome.webview.addEventListener('message', this.desktopMessageHandler)
      window.chrome.webview.postMessage({ type: 'desktop.getState' })
    },
    disposeDesktopBridge () {
      if (this.desktopMessageHandler && window.chrome?.webview) window.chrome.webview.removeEventListener('message', this.desktopMessageHandler)
      this.desktopMessageHandler = null
    },
    handleDesktopMessage (event) {
      if (event.data?.type === 'desktop.state') {
        this.desktopSettings = {
          available: event.data.available === true,
          memoryOverlayEnabled: event.data.memoryOverlayEnabled === true,
          storagePath: event.data.storagePath || '',
          storageMigrationAvailable: event.data.storageMigrationAvailable === true,
          storagePathManagedByEnvironment: event.data.storagePathManagedByEnvironment === true
        }
      } else if (event.data?.type === 'desktop.storage.selection') {
        this.storageMigrationTarget = event.data.path || ''
      } else if (event.data?.type === 'desktop.storage.migrationResult') {
        this.storageMigrationBusy = false
        if (event.data.success !== true) ElMessage.error(event.data.message || '数据库迁移失败')
      }
    },
    setMemoryOverlayEnabled (enabled) {
      if (!this.desktopSettings.available || !window.chrome?.webview) return
      window.chrome.webview.postMessage({ type: 'desktop.memoryOverlay.set', enabled: enabled === true })
    },
    chooseStorageDirectory () {
      if (!this.desktopSettings.storageMigrationAvailable || !window.chrome?.webview) return
      window.chrome.webview.postMessage({ type: 'desktop.storage.choose' })
    },
    async migrateStorage () {
      if (!this.desktopSettings.storageMigrationAvailable || !this.storageMigrationTarget || !window.chrome?.webview) return
      try {
        await ElMessageBox.confirm('迁移时本地服务会短暂停止，完成后软件自动重启。原目录将保留为备份，是否继续？', '迁移软件数据库', { type: 'warning', confirmButtonText: '迁移并重启' })
      } catch (e) { if (!this.isCanceled(e)) this.error(e); return }
      this.storageMigrationBusy = true
      window.chrome.webview.postMessage({ type: 'desktop.storage.migrate', targetPath: this.storageMigrationTarget })
    },
    paginate (items, page, pageSize) {
      const start = (Math.max(1, page) - 1) * pageSize
      return (items || []).slice(start, start + pageSize)
    },
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
    openAdminReset () {
      this.adminResetForm = { userName: this.loginForm.userName || 'admin', resetPassword: '', newPassword: '', confirmPassword: '' }
      this.adminResetDialog = true
    },
    async resetAdminPassword () {
      const form = this.adminResetForm
      if (!form.userName.trim() || !form.resetPassword) { ElMessage.warning('请输入管理员用户名和重置口令'); return }
      if (form.newPassword.length < 6) { ElMessage.warning('新密码至少需要 6 位'); return }
      if (form.newPassword !== form.confirmPassword) { ElMessage.warning('两次输入的新密码不一致'); return }
      this.saving = true
      try {
        await axios.post('/api/auth/reset-admin-password', { userName: form.userName.trim(), resetPassword: form.resetPassword, newPassword: form.newPassword })
        this.loginForm.userName = form.userName.trim()
        this.loginForm.password = ''
        this.adminResetDialog = false
        ElMessage.success('管理员登录密码已重置，请使用新密码登录')
      } catch (e) {
        if (e.response?.status === 401) ElMessage.error('管理员用户名或重置口令不正确')
        else if (e.response?.status === 429) ElMessage.error('尝试次数过多，请一分钟后再试')
        else this.error(e)
      } finally { this.saving = false }
    },
    async logout () { this.disconnectEvents(); await axios.post('/api/auth/logout'); this.user = null; this.activeTab = 'overview'; this.openTabs = ['overview'] },
    async handleUserCommand (command) { if (command === 'logout') return this.logout(); await this.goTo(command) },
    applyUiTheme () { document.documentElement.classList.toggle('dark', this.uiTheme === 'dark') },
    toggleUiTheme () {
      this.uiTheme = this.uiTheme === 'dark' ? 'light' : 'dark'
      localStorage.setItem('gateway.uiTheme', this.uiTheme)
      this.applyUiTheme()
    },
    toggleSidebar () {
      this.sidebarCollapsed = !this.sidebarCollapsed
      localStorage.setItem('gateway.sidebarCollapsed', String(this.sidebarCollapsed))
    },
    isPageOpen (name) { return this.openTabs.includes(name) },
    async goTo (name) {
      if (!this.openTabs.includes(name)) this.openTabs.push(name)
      this.activeTab = name
      await this.loadActiveTab()
    },
    async closePage (name) {
      if (name === 'overview') return
      const index = this.openTabs.indexOf(name)
      if (index < 0) return
      this.openTabs.splice(index, 1)
      if (this.activeTab === name) {
        this.activeTab = this.openTabs[Math.max(0, index - 1)] || 'overview'
        await this.loadActiveTab()
      }
    },
    async loadOverview () {
      const jobs = [this.loadCustomModules()]
      if (this.canOperate) jobs.push(this.loadResourceCatalog())
      if (this.canApprove) jobs.push(this.loadApprovalMetrics())
      if (this.canViewLogs) jobs.push(this.loadAuditLogMetrics())
      if (this.canViewMetrics) jobs.push(this.loadMonitorTargets())
      await Promise.all(jobs)
    },
    async loadActiveTab () {
      if (this.activeTab === 'overview') await this.loadOverview()
      if (this.activeTab === 'projects') await this.loadResourceCatalog()
      if (this.activeTab === 'datasources') await this.loadDataSources()
      if (this.activeTab === 'logsources') await this.loadResourceCatalog()
      if (this.activeTab === 'applicationlogs') await this.loadLogSources()
      if (this.activeTab === 'realtimelogs') await this.loadLogSources()
      if (this.activeTab === 'monitoring') { await this.loadMonitorTargets(); await this.loadMetricSamples() }
      if (this.activeTab === 'approvals') await this.loadApprovals()
      if (this.activeTab === 'logs') await this.loadAuditLogs()
      if (this.activeTab === 'settings') await this.loadMaintenanceSettings()
      if (this.activeTab === 'users') await this.loadUsers()
      if (this.activeTab === 'clients') await this.loadClients()
      if (this.activeTab === 'toolboxwebhooks') await this.loadToolboxHooks()
      if (this.activeTab === 'custommodules') await this.loadCustomModules()
    },
    async loadDataSources () { this.dataSources = (await axios.get('/api/admin/datasources')).data; this.dataSourcePage = this.clampPage(this.dataSourcePage, this.dataSources.length, this.dataSourcePageSize) },
    async loadProjects () { this.projects = (await axios.get('/api/admin/projects')).data; this.projectPage = this.clampPage(this.projectPage, this.projects.length, this.projectPageSize) },
    async loadLogSources () {
      this.logSources = (await axios.get(this.canOperate ? '/api/admin/log-sources' : '/api/log-sources')).data
      this.logSourcePage = this.clampPage(this.logSourcePage, this.logSources.length, this.logSourcePageSize)
      const enabled = this.logSources.filter(item => item.enabled)
      if (!enabled.some(item => item.id === this.applicationLogSourceId)) this.applicationLogSourceId = enabled[0]?.id || ''
      if (!enabled.some(item => item.id === this.realtimeLogSourceId)) this.realtimeLogSourceId = enabled.find(item => item.type === 2)?.id || enabled[0]?.id || ''
    },
    async loadMonitorTargets () {
      if (!this.metricCatalog.length) await this.loadMetricCatalog()
      this.monitorTargets = (await axios.get('/api/monitoring/targets')).data
      this.monitorTargetPage = this.clampPage(this.monitorTargetPage, this.monitorTargets.length, this.monitorTargetPageSize)
      if (!this.monitorTargets.some(item => item.id === this.selectedMonitorTargetId)) this.selectedMonitorTargetId = this.monitorTargets[0]?.id || ''
      this.ensureTrendMetric()
    },
    async loadMetricCatalog () {
      const result = (await axios.get('/api/monitoring/metric-catalog')).data
      this.metricCatalog = result.items || []
      this.metricCatalogDefaultKeys = result.defaultKeys || []
      this.metricCatalogRequiredKeys = result.requiredKeys || []
    },
    async loadResourceCatalog () {
      await Promise.all([this.loadDataSources(), this.loadProjects(), this.loadLogSources(), this.loadMonitorTargets()])
      if (!this.applicationLogSourceId && this.logSources.some(item => item.enabled)) this.applicationLogSourceId = this.logSources.find(item => item.enabled).id
    },
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
      const response = await axios.get('/api/approvals', { params: { status: this.approvalFilter === 'all' ? undefined : this.approvalFilter, keyword: this.approvalKeyword.trim() || undefined, dataSourceId: this.approvalDataSourceFilter?.id || undefined, page: this.approvalPage, pageSize: this.approvalPageSize } })
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
    async resetApprovalSearch () { this.approvalFilter = 'all'; this.approvalKeyword = ''; this.approvalDataSourceFilter = null; this.approvalPage = 1; await this.loadApprovals() },
    async clearApprovalDataSourceFilter () { this.approvalDataSourceFilter = null; this.approvalPage = 1; await this.loadApprovals() },
    async approvalSizeChanged () { this.approvalPage = 1; await this.loadApprovals() },
    async changeApprovalPage (page) { if (page < 1 || page > this.approvalPageCount || page === this.approvalPage) return; this.approvalPage = page; await this.loadApprovals() },
    async searchAuditLogs () { this.auditLogPage = 1; await this.loadAuditLogs() },
    async resetLogSearch () { this.logKeyword = ''; this.logOutcome = ''; this.auditLogPage = 1; await this.loadAuditLogs() },
    async auditLogSizeChanged () { this.auditLogPage = 1; await this.loadAuditLogs() },
    async changeAuditLogPage (page) { if (page < 1 || page > this.auditLogPageCount || page === this.auditLogPage) return; this.auditLogPage = page; await this.loadAuditLogs() },
    async openDataSourcePage () { if (this.canOperate) await this.goTo('datasources') },
    async openProjectPage () { if (this.canOperate) await this.goTo('projects') },
    async openLogSourcePage () { if (this.canOperate) await this.goTo('logsources') },
    async openApplicationLogPage () { if (!this.canViewLogs) return; await this.goTo('applicationlogs') },
    async openMonitoringPage () { if (!this.canViewMetrics) return; await this.goTo('monitoring') },
    async openApprovalPage (status = 'all') { if (!this.canApprove) return; this.approvalFilter = status; this.approvalKeyword = ''; this.approvalPage = 1; await this.goTo('approvals') },
    async openLogSourceLogs (row) {
      if (!row.enabled) { ElMessage.warning('该日志源已禁用，请先启用后再查看应用日志'); return }
      this.applicationLogSourceId = row.id
      await this.goTo('applicationlogs')
      await this.searchApplicationLogs()
    },
    async openRealtimeLogs () {
      if (!this.canViewLogs) return
      this.realtimeLogSourceId = this.selectedApplicationLogSource?.enabled ? this.applicationLogSourceId : ''
      this.clearRealtimeLogs()
      await this.goTo('realtimelogs')
    },
    async openDataSourceApprovals (row) {
      if (!this.canApprove) { ElMessage.warning('当前账号没有审批权限（Administrator/Approver），无法查看审批记录'); return }
      this.approvalDataSourceFilter = { id: row.id, name: row.name }
      this.approvalFilter = 'all'
      this.approvalKeyword = ''
      this.approvalPage = 1
      await this.goTo('approvals')
    },
    async openLogPage () { if (!this.canViewLogs) return; this.logKeyword = ''; this.logOutcome = ''; this.auditLogPage = 1; await this.goTo('logs') },
    async loadUsers () { const [users, roles] = await Promise.all([axios.get('/api/admin/users'), axios.get('/api/admin/roles')]); this.users = users.data; this.roles = roles.data; this.userPage = this.clampPage(this.userPage, this.users.length, this.userPageSize) },
    async loadClients () { this.clients = (await axios.get('/api/admin/oauth-clients')).data; this.clientPage = this.clampPage(this.clientPage, this.clients.length, this.clientPageSize) },
    async loadMaintenanceSettings () {
      const [maintenance, recovery] = await Promise.all([axios.get('/api/settings/maintenance'), axios.get('/api/settings/admin-recovery')])
      this.maintenanceSettings = maintenance.data
      this.adminRecovery = { ...recovery.data, newResetPassword: '' }
    },
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
      if (this.canOperate && (action.startsWith('project.') || action.startsWith('logsource.'))) jobs.push(this.loadResourceCatalog())
      if (this.canViewMetrics && (action.startsWith('monitor-target.') || action.startsWith('metrics.'))) {
        jobs.push(this.loadMonitorTargets())
        if (this.activeTab === 'monitoring') jobs.push(this.loadMetricSamples())
      }
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
    providerChanged (provider) {
      const selected = this.providers.find(item => item.value === provider)
      if (selected) this.dataSourceForm.port = selected.port
    },
    async saveDataSource () {
      this.saving = true
      try { const payload = { ...this.dataSourceForm, blockedTables: this.dataSourceForm.blockedTablesText.split(/\r?\n/).map(item => item.trim()).filter(Boolean) }; delete payload.blockedTablesText; if (this.editingDataSource) await axios.put(`/api/admin/datasources/${this.editingDataSource.id}`, payload); else await axios.post('/api/admin/datasources', payload); this.dataSourceDialog = false; await this.loadDataSources(); ElMessage.success('已保存') } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async testDataSource (row) { try { const response = await axios.post(`/api/admin/datasources/${row.id}/test`); ElMessage({ type: response.data.success ? 'success' : 'error', message: response.data.message }) } catch (e) { this.error(e) } },
    async deleteDataSource (row) { try { await ElMessageBox.confirm(`确定删除 ${row.name}？`); await axios.delete(`/api/admin/datasources/${row.id}`); await this.loadDataSources() } catch (e) { if (!this.isCanceled(e)) this.error(e) } },
    openProject (row) {
      this.editingProject = row || null
      this.projectForm = row ? { code: row.code, name: row.name, description: row.description, enabled: row.enabled, dataSourceIds: row.dataSources.map(item => item.id), logSourceIds: row.logSources.map(item => item.id), monitorTargetIds: (row.monitorTargets || []).map(item => item.id) } : { code: '', name: '', description: '', enabled: true, dataSourceIds: [], logSourceIds: [], monitorTargetIds: [] }
      this.projectDialog = true
    },
    async saveProject () {
      this.saving = true
      try { if (this.editingProject) await axios.put(`/api/admin/projects/${this.editingProject.id}`, this.projectForm); else await axios.post('/api/admin/projects', this.projectForm); this.projectDialog = false; await this.loadResourceCatalog(); ElMessage.success('项目已保存') } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async deleteProject (row) { try { await ElMessageBox.confirm(`确定删除项目 ${row.name}？关联的数据源和日志源本身不会被删除。`, '删除项目', { type: 'warning' }); await axios.delete(`/api/admin/projects/${row.id}`); await this.loadResourceCatalog(); ElMessage.success('项目已删除') } catch (e) { if (!this.isCanceled(e)) this.error(e) } },
    openLogSource (row) {
      this.editingLogSource = row || null
      this.logSourceForm = row ? { key: row.key, name: row.name, type: row.type, endpoint: row.endpoint, nLogConfiguration: row.nLogConfiguration || '', nLogTargetName: row.nLogTargetName || '', nLogLayout: row.nLogLayout || '', apiKey: '', enabled: row.enabled, projectIds: row.projects.map(item => item.id) } : { key: '', name: '', type: 1, endpoint: '', nLogConfiguration: '', nLogTargetName: '', nLogLayout: '', apiKey: '', enabled: true, projectIds: [] }
      this.logSourceDialog = true
    },
    selectLogSourceType (type) { if (this.logSourceForm.type === type) return; this.logSourceForm.type = type; this.logSourceTypeChanged(type) },
    logSourceTypeChanged (type) { this.logSourceForm.endpoint = type === 2 ? 'http://127.0.0.1:5341' : type === 3 ? 'http://127.0.0.1:5188' : ''; this.logSourceForm.apiKey = '' },
    async saveLogSource () {
      this.saving = true
      try { const response = this.editingLogSource ? await axios.put(`/api/admin/log-sources/${this.editingLogSource.id}`, this.logSourceForm) : await axios.post('/api/admin/log-sources', this.logSourceForm); this.applicationLogSourceId = response.data.id; this.realtimeLogSourceId = response.data.id; this.logSourceDialog = false; await this.loadResourceCatalog(); ElMessage.success('日志源已保存，可直接到应用日志点击查询，无需先测试') } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async testLogSource (row) { try { const response = await axios.post(`/api/admin/log-sources/${row.id}/test`); ElMessage({ type: response.data.success ? 'success' : 'error', message: response.data.message, duration: 6000 }) } catch (e) { this.error(e) } },
    async deleteLogSource (row) { try { await ElMessageBox.confirm(`确定删除日志源 ${row.name}？不会删除本地日志文件或 Seq 数据。`, '删除日志源', { type: 'warning' }); await axios.delete(`/api/admin/log-sources/${row.id}`); await this.loadResourceCatalog(); ElMessage.success('日志源已删除') } catch (e) { if (!this.isCanceled(e)) this.error(e) } },
    async selectMonitorTarget (target) { this.selectedMonitorTargetId = target.id; this.metricSamplePage = 1; this.metricTrendMode = 'recent'; this.metricHoverPoint = null; this.ensureTrendMetric(); await this.loadMetricSamples() },
    async loadMetricSamples () {
      if (!this.selectedMonitorTargetId) { this.metricSamples = []; this.metricSampleTotal = 0; return }
      this.metricHoverPoint = null
      this.monitorLoading = true
      try {
        const requests = [axios.get(`/api/monitoring/targets/${this.selectedMonitorTargetId}/samples`, { params: { page: this.metricSamplePage, pageSize: this.metricSamplePageSize } })]
        if (this.metricTrendMode === 'recent') requests.push(axios.get(`/api/monitoring/targets/${this.selectedMonitorTargetId}/samples`, { params: { page: 1, pageSize: 120 } }))
        const responses = await Promise.all(requests)
        this.metricSamples = responses[0].data.items
        this.metricSampleTotal = responses[0].data.total
        if (this.metricTrendMode === 'recent') {
          this.metricTrendSamples = responses[1].data.items
          this.metricTrendSourceCount = responses[1].data.total
        }
      } catch (e) { this.error(e) } finally { this.monitorLoading = false }
    },
    async metricSampleSizeChanged () { this.metricSamplePage = 1; await this.loadMetricSamples() },
    async setMetricTrendMode (mode) {
      this.metricTrendMode = mode
      this.metricHoverPoint = null
      if (mode === 'recent') await this.loadMetricSamples(); else await this.loadHistoricalTrend()
    },
    async loadHistoricalTrend () {
      if (!this.selectedMonitorTargetId || !Array.isArray(this.metricHistoryRange) || this.metricHistoryRange.length !== 2) return
      this.metricHoverPoint = null
      this.monitorLoading = true
      try {
        const [from, to] = this.metricHistoryRange
        const result = (await axios.get(`/api/monitoring/targets/${this.selectedMonitorTargetId}/trend`, { params: { fromUtc: new Date(from).toISOString(), toUtc: new Date(to).toISOString(), maxPoints: 500 } })).data
        this.metricTrendSamples = result.items || []
        this.metricTrendSourceCount = result.sourceCount || 0
      } catch (e) { this.error(e) } finally { this.monitorLoading = false }
    },
    ensureTrendMetric () {
      const keys = this.selectedMonitorTarget?.metricKeys || []
      if (!keys.includes(this.metricTrendKey)) this.metricTrendKey = keys[0] || 'cpu.percent'
      const valid = this.metricTrendKeys.filter(key => keys.includes(key)).slice(0, 4)
      this.metricTrendKeys = valid.length ? valid : keys.slice(0, 2)
      localStorage.setItem('gateway.metricTrendKeys', JSON.stringify(this.metricTrendKeys))
    },
    trendMetricSelectionChanged (keys) {
      if (keys.length > 4) {
        this.metricTrendKeys = keys.slice(0, 4)
        ElMessage.warning('一个页面最多同时显示 4 个指标')
      }
      localStorage.setItem('gateway.metricTrendKeys', JSON.stringify(this.metricTrendKeys))
    },
    openMonitorTarget (row) {
      this.editingMonitorTarget = row || null
      this.monitorTargetForm = row ? { key: row.key, name: row.name, enabled: row.enabled, projectIds: row.projects.map(item => item.id), metricKeys: [...(row.metricKeys || this.metricCatalogDefaultKeys)] } : { key: '', name: '', enabled: true, projectIds: [], metricKeys: [...this.metricCatalogDefaultKeys] }
      this.monitorTargetDialog = true
    },
    selectDefaultMetrics () { this.monitorTargetForm.metricKeys = [...this.metricCatalogDefaultKeys] },
    async saveMonitorTarget () {
      this.saving = true
      try {
        if (this.editingMonitorTarget) {
          await axios.put(`/api/admin/monitoring/targets/${this.editingMonitorTarget.id}`, this.monitorTargetForm)
          ElMessage.success('监控节点已保存')
        } else {
          const created = (await axios.post('/api/admin/monitoring/targets', this.monitorTargetForm)).data
          this.monitorCredential = { targetKey: created.target.key, ingestSecret: created.ingestSecret }
          this.monitorCredentialDialog = true
        }
        this.monitorTargetDialog = false
        await this.loadMonitorTargets()
      } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async rotateMonitorSecret (target) {
      try {
        await ElMessageBox.confirm(`重置 ${target.name} 的上报密钥后，旧 Agent 会立即无法上报。`, '重置上报密钥', { type: 'warning' })
        const result = (await axios.post(`/api/admin/monitoring/targets/${target.id}/rotate-secret`)).data
        this.monitorCredential = { targetKey: result.targetKey, ingestSecret: result.ingestSecret }
        this.monitorCredentialDialog = true
      } catch (e) { if (!this.isCanceled(e)) this.error(e) }
    },
    async deleteMonitorTarget (target) {
      try { await ElMessageBox.confirm(`确定删除远端监控节点 ${target.name}？其历史指标也会删除。`, '删除监控节点', { type: 'warning' }); await axios.delete(`/api/admin/monitoring/targets/${target.id}`); await this.loadMonitorTargets(); await this.loadMetricSamples(); ElMessage.success('监控节点已删除') } catch (e) { if (!this.isCanceled(e)) this.error(e) }
    },
    monitorTargetTypeName (value) { return value === 1 ? '本机' : '远端' },
    showMetricPoint (point) { this.metricHoverPoint = point },
    formatChartAxisTime (value) {
      const date = new Date(value)
      if (Number.isNaN(date.getTime())) return '—'
      const points = this.metricTrendChartPoints
      const duration = points.length > 1 ? new Date(points.at(-1).timestamp) - new Date(points[0].timestamp) : 0
      const monthDay = `${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
      const time = `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`
      if (duration > 7 * 86400000) return monthDay
      if (duration > 86400000) return `${monthDay} ${time}`
      return `${time}:${String(date.getSeconds()).padStart(2, '0')}`
    },
    metricValue (sample, key) {
      if (!sample) return null
      const value = sample.metrics?.[key]
      return value === null || value === undefined || !Number.isFinite(Number(value)) ? null : Number(value)
    },
    formatMetricValue (value, unit) {
      if (value === null || value === undefined || !Number.isFinite(Number(value))) return '—'
      if (unit === 'percent') return this.formatPercent(value)
      if (unit === 'bytes') return this.formatBytes(value)
      if (unit === 'bytes_per_second') return `${this.formatBytes(value)}/s`
      if (unit === 'duration_seconds') return this.formatDuration(value)
      return Number(value).toLocaleString('zh-CN', { maximumFractionDigits: 1 })
    },
    formatPercent (value) { return value === null || value === undefined ? '—' : `${Number(value).toFixed(1)}%` },
    formatBytes (value) {
      if (value === null || value === undefined) return '—'
      const units = ['B', 'KB', 'MB', 'GB', 'TB']; let size = Number(value); let unit = 0
      while (size >= 1024 && unit < units.length - 1) { size /= 1024; unit++ }
      return `${size.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`
    },
    formatDuration (seconds) {
      if (seconds === null || seconds === undefined) return '—'
      const days = Math.floor(seconds / 86400); const hours = Math.floor((seconds % 86400) / 3600); const minutes = Math.floor((seconds % 3600) / 60)
      return days ? `${days}天 ${hours}小时` : hours ? `${hours}小时 ${minutes}分` : `${minutes}分钟`
    },
    async searchApplicationLogs () {
      if (!this.logSources.length || !this.logSources.some(item => item.id === this.applicationLogSourceId && item.enabled)) await this.loadLogSources()
      if (!this.applicationLogSourceId) { ElMessage.warning('没有可用日志源，请先创建并启用日志源'); return }
      this.applicationLogPage = 1; await this.loadApplicationLogs()
    },
    async loadApplicationLogs () {
      if (!this.applicationLogSourceId) { this.applicationLogs = []; return }
      this.applicationLogsLoading = true
      try {
        const response = await axios.post('/api/logs/query', this.applicationLogRequest())
        this.applicationLogs = response.data.items; this.applicationLogTotal = response.data.total; this.applicationLogPartial = response.data.isPartial; this.applicationLogWarning = response.data.warning
      } catch (e) { this.error(e) } finally { this.applicationLogsLoading = false }
    },
    applicationLogRequest () {
      const seqAdvanced = this.selectedApplicationLogSource?.type === 2 && this.applicationLogQueryMode === 'advanced'
      return { logSourceId: this.applicationLogSourceId, query: seqAdvanced ? this.applicationLogQuery.trim() || null : null, searchText: seqAdvanced ? null : this.applicationLogSearchText.trim() || null, propertyName: seqAdvanced ? null : (this.applicationLogTopic.trim() ? 'Topic' : (this.applicationLogPropertyName.trim() || null)), propertyValue: seqAdvanced ? null : (this.applicationLogTopic.trim() || this.applicationLogPropertyValue.trim() || null), level: this.applicationLogLevel || null, fromUtc: this.applicationLogRange?.[0]?.toISOString(), toUtc: this.applicationLogRange?.[1]?.toISOString(), page: this.applicationLogPage, pageSize: this.applicationLogPageSize }
    },
    async resetApplicationLogs () { this.applicationLogQuery = ''; this.applicationLogSearchText = ''; this.applicationLogTopic = ''; this.applicationLogPropertyName = ''; this.applicationLogPropertyValue = ''; this.applicationLogLevel = ''; this.applicationLogRange = [new Date(Date.now() - 24 * 60 * 60 * 1000), new Date()]; this.applicationLogPage = 1; this.applicationLogs = []; this.applicationLogTotal = 0; this.applicationLogWarning = null },
    async startRealtimeLogStream () {
      if (this.realtimeLogEventSource || this.realtimeLogConnecting) return
      const attempt = ++this.realtimeLogAttempt
      this.realtimeLogConnecting = true; this.realtimeLogConnected = false; this.realtimeLogError = null
      try { if (!this.logSources.length) await this.loadLogSources() } catch (e) { if (attempt === this.realtimeLogAttempt) { this.realtimeLogConnecting = false; this.error(e) }; return }
      if (attempt !== this.realtimeLogAttempt) return
      if (!this.realtimeLogSourceId) { this.realtimeLogConnecting = false; ElMessage.warning('请先选择日志源'); return }
      const params = new URLSearchParams({ logSourceId: this.realtimeLogSourceId, fromUtc: new Date().toISOString() })
      if (this.realtimeLogSearchText.trim()) params.set('searchText', this.realtimeLogSearchText.trim())
      if (this.realtimeLogLevel) params.set('level', this.realtimeLogLevel)
      if (this.realtimeLogTopic.trim()) {
        params.set('propertyName', 'Topic')
        params.set('propertyValue', this.realtimeLogTopic.trim())
      } else if (this.realtimeLogPropertyName.trim()) params.set('propertyName', this.realtimeLogPropertyName.trim())
      if (!this.realtimeLogTopic.trim() && this.realtimeLogPropertyValue.trim()) params.set('propertyValue', this.realtimeLogPropertyValue.trim())
      const stream = new EventSource(`/api/logs/stream?${params.toString()}`, { withCredentials: true })
      this.realtimeLogEventSource = stream
      stream.onopen = () => { if (this.realtimeLogEventSource === stream) { this.realtimeLogConnecting = false; this.realtimeLogConnected = true } }
      stream.onmessage = event => {
        const item = JSON.parse(event.data)
        if (!item.id || this.realtimeLogs.some(value => value.id === item.id)) return
        this.realtimeLogs.unshift(item)
        if (this.realtimeLogs.length > 500) this.realtimeLogs.length = 500
        this.realtimeLogPage = 1
      }
      stream.onerror = () => { if (this.realtimeLogEventSource === stream) { this.stopRealtimeLogStream(); this.realtimeLogError = '实时日志连接已断开，请检查日志源连接和权限后重新开始。' } }
    },
    stopRealtimeLogStream () { this.realtimeLogAttempt++; this.realtimeLogEventSource?.close(); this.realtimeLogEventSource = null; this.realtimeLogConnected = false; this.realtimeLogConnecting = false },
    clearRealtimeLogs () { this.realtimeLogs = []; this.realtimeLogPage = 1; this.realtimeLogError = null },
    async applicationLogSizeChanged () { this.applicationLogPage = 1; if (this.applicationLogs.length) await this.loadApplicationLogs() },
    async changeApplicationLogPage (page) { if (page < 1 || page === this.applicationLogPage) return; this.applicationLogPage = page; await this.loadApplicationLogs() },
    extractJsonPretty (value) {
      if (typeof value !== 'string') return null
      const text = value.trim()
      if (text.length < 3) return null
      const candidates = []
      if ((text[0] === '{' && text.endsWith('}')) || (text[0] === '[' && text.endsWith(']'))) candidates.push(text)
      const braceStart = text.indexOf('{')
      const braceEnd = text.lastIndexOf('}')
      if (braceStart >= 0 && braceEnd > braceStart) candidates.push(text.slice(braceStart, braceEnd + 1))
      const bracketStart = text.indexOf('[')
      const bracketEnd = text.lastIndexOf(']')
      if (bracketStart >= 0 && bracketEnd > bracketStart) candidates.push(text.slice(bracketStart, bracketEnd + 1))
      for (const candidate of candidates) {
        try { return JSON.stringify(JSON.parse(candidate), null, 2) } catch (error) { /* try next span */ }
      }
      return null
    },
    async toggleDetailMax (name) {
      this[name] = !this[name]
      await this.$nextTick()
      // a dragged dialog keeps its translate() offset, which would shift the
      // fullscreen window away from the viewport origin
      document.querySelectorAll('.el-dialog.is-fullscreen').forEach((el) => { el.style.transform = '' })
    },
    customModuleTab (module) { return 'custommodule:' + module.id },
    async loadCustomModules () {
      try {
        this.customModules = (await axios.get('/api/custom-modules')).data
        this.customModulePage = this.clampPage(this.customModulePage, this.customModules.length, this.customModulePageSize)
        const pageTabs = new Set(this.customModulePages.map(item => this.customModuleTab(item)))
        const removedActive = this.activeTab.startsWith('custommodule:') && !pageTabs.has(this.activeTab)
        this.openTabs = this.openTabs.filter(name => !name.startsWith('custommodule:') || pageTabs.has(name))
        if (removedActive) this.activeTab = 'custommodules'
      } catch (e) { this.error(e) }
    },
    async openCustomModule (module) {
      if (!module?.pageUrl || !module.loaded) return
      await this.goTo(this.customModuleTab(module))
    },
    async installCustomModule (event) {
      const input = event.target
      const file = input.files?.[0]
      if (!file) return
      try {
        await ElMessageBox.confirm(`将安装“${file.name}”。扩展 DLL 会在网关进程内运行，请确认其来源可信并已完成代码审核。`, '安装受信任扩展', { type: 'warning', confirmButtonText: '确认安装' })
      } catch (_) { input.value = ''; return }
      this.customModuleInstalling = true
      try {
        const form = new FormData()
        form.append('package', file)
        const result = (await axios.post('/api/admin/custom-modules/install', form)).data
        await this.loadCustomModules()
        if (result.loaded) ElMessage.success(`${result.name} 已安装并加载，MCP 客户端下次 tools/list 即可发现新工具`)
        else ElMessage.error(`${result.name} 已保存，但加载失败：${result.loadError || '未知错误'}`)
      } catch (e) { this.error(e) } finally { this.customModuleInstalling = false; input.value = '' }
    },
    async setCustomModuleEnabled (module, enabled) {
      try {
        const result = (await axios.put(`/api/admin/custom-modules/${encodeURIComponent(module.id)}/enabled`, { enabled })).data
        await this.loadCustomModules()
        if (enabled && !result.loaded) ElMessage.error(`启用失败：${result.loadError || '扩展无法加载'}`)
        else ElMessage.success(enabled ? '模块已启用' : '模块已停用')
      } catch (e) { await this.loadCustomModules(); this.error(e) }
    },
    async deleteCustomModule (module) {
      try { await ElMessageBox.confirm(`确定删除定制化模块“${module.name}”及其已安装制品？`, '删除模块', { type: 'warning' }) } catch (_) { return }
      try {
        await axios.delete(`/api/admin/custom-modules/${encodeURIComponent(module.id)}`)
        await this.loadCustomModules()
        ElMessage.success('模块已删除')
      } catch (e) { this.error(e) }
    },
    toolboxHookUrl (hook) { return window.location.origin + '/toolbox/hook/' + hook.token },
    async loadToolboxHooks () {
      try { this.toolboxHooks = (await axios.get('/api/toolbox/webhooks')).data } catch (e) { this.error(e) }
    },
    openToolboxHook (row) {
      this.editingToolboxHook = row || null
      this.toolboxHookForm = row ? { name: row.name, description: row.description } : { name: '', description: '' }
      this.toolboxHookDialog = true
    },
    async saveToolboxHook () {
      this.saving = true
      try {
        if (this.editingToolboxHook) await axios.put('/api/toolbox/webhooks/' + this.editingToolboxHook.id, { name: this.toolboxHookForm.name, description: this.toolboxHookForm.description, enabled: this.editingToolboxHook.enabled })
        else await axios.post('/api/toolbox/webhooks', { name: this.toolboxHookForm.name, description: this.toolboxHookForm.description })
        this.toolboxHookDialog = false
        await this.loadToolboxHooks()
        ElMessage.success('已保存')
      } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async toggleToolboxHook (row, enabled) {
      try { await axios.put('/api/toolbox/webhooks/' + row.id, { name: row.name, description: row.description, enabled }); await this.loadToolboxHooks() } catch (e) { this.error(e) }
    },
    async deleteToolboxHook (row) {
      try { await ElMessageBox.confirm('确定删除 ' + row.name + '？其全部历史报文将一并删除。', '删除 WebHook', { type: 'warning' }) } catch (e) { return }
      try { await axios.delete('/api/toolbox/webhooks/' + row.id); await this.loadToolboxHooks(); ElMessage.success('已删除') } catch (e) { this.error(e) }
    },
    viewToolboxDeliveries (row) {
      this.toolboxDeliveryHook = row
      this.toolboxDeliveries = []
      this.toolboxDeliveryDialog = true
      axios.get('/api/toolbox/webhooks/' + row.id + '/deliveries').then(res => { this.toolboxDeliveries = res.data }).catch(e => this.error(e))
    },
    async clearToolboxDeliveries (row) {
      try { await ElMessageBox.confirm('确定清空该 WebHook 的全部历史报文？', '清空报文', { type: 'warning' }) } catch (e) { return }
      try {
        await axios.post('/api/toolbox/webhooks/' + row.id + '/deliveries/clear')
        if (this.toolboxDeliveryHook && this.toolboxDeliveryHook.id === row.id) this.toolboxDeliveries = []
        await this.loadToolboxHooks()
        ElMessage.success('已清空')
      } catch (e) { this.error(e) }
    },
    copyToolboxUrl (hook) {
      navigator.clipboard.writeText(this.toolboxHookUrl(hook)).then(() => ElMessage.success('已复制')).catch(() => ElMessage.error('复制失败'))
    },
    formatXmlTool () {
      try {
        const doc = new DOMParser().parseFromString(this.xmlTool.input, 'application/xml')
        const parserError = doc.querySelector('parsererror')
        if (parserError) throw new Error('XML 格式不正确：' + (parserError.textContent || '').split('\n')[0])
        this.xmlTool.output = this.prettyXmlNode(doc.documentElement, 0)
        this.xmlTool.error = ''
      } catch (e) { this.xmlTool.output = ''; this.xmlTool.error = e.message }
    },
    prettyXmlNode (node, depth) {
      const pad = '  '.repeat(depth)
      const attrs = Array.from(node.attributes).map(item => ' ' + item.name + '="' + this.escapeHtml(item.value) + '"').join('')
      const tag = node.tagName
      const elements = Array.from(node.children)
      const textParts = Array.from(node.childNodes).filter(item => item.nodeType === 3 && item.textContent.trim())
      if (!elements.length && !textParts.length) return pad + '<' + tag + attrs + ' />'
      if (!elements.length) return pad + '<' + tag + attrs + '>' + this.escapeHtml(textParts.map(item => item.textContent.trim()).join(' ')) + '</' + tag + '>'
      const inner = elements.map(child => this.prettyXmlNode(child, depth + 1)).join('\n')
      return pad + '<' + tag + attrs + '>\n' + inner + '\n' + pad + '</' + tag + '>'
    },
    formatJsonTool () {
      try { this.jsonTool.output = JSON.stringify(JSON.parse(this.jsonTool.input), null, 2); this.jsonTool.error = '' }
      catch (e) { this.jsonTool.output = ''; this.jsonTool.error = 'JSON 解析失败：' + e.message }
    },
    convertCase (direction) {
      const text = this.caseTool.input
      if (direction === 'upper') this.caseTool.output = text.toUpperCase()
      else if (direction === 'lower') this.caseTool.output = text.toLowerCase()
      else if (direction === 'camel') this.caseTool.output = text.replace(/[^a-zA-Z0-9]+(.)?/g, (m, c) => c ? c.toUpperCase() : '').replace(/^[A-Z]/, c => c.toLowerCase())
      else if (direction === 'snake') this.caseTool.output = text.replace(/([a-z0-9])([A-Z])/g, (m, a, b) => a + '_' + b.toLowerCase()).replace(/[-\s]+/g, '_').toLowerCase()
    },
    encodeBase64 () {
      try {
        const bytes = new TextEncoder().encode(this.base64Tool.input)
        let binary = ''
        bytes.forEach(b => { binary += String.fromCharCode(b) })
        this.base64Tool.output = btoa(binary)
        this.base64Tool.error = ''
      } catch (e) { this.base64Tool.output = ''; this.base64Tool.error = '编码失败：' + e.message }
    },
    decodeBase64 () {
      try {
        const bytes = Uint8Array.from(atob(this.base64Tool.input.trim()), c => c.charCodeAt(0))
        this.base64Tool.output = new TextDecoder().decode(bytes)
        this.base64Tool.error = ''
      } catch (e) { this.base64Tool.output = ''; this.base64Tool.error = '无效的 Base64 字符串' }
    },
    copyToolOutput (path) {
      const value = path.split('.').reduce((object, key) => (object ? object[key] : ''), this)
      navigator.clipboard.writeText(value || '').then(() => ElMessage.success('已复制')).catch(() => ElMessage.error('复制失败'))
    },
    clearTool (kind) {
      if (kind === 'xml') this.xmlTool = { input: '', output: '', error: '' }
      if (kind === 'json') this.jsonTool = { input: '', output: '', error: '' }
      if (kind === 'case') this.caseTool = { input: '', output: '' }
      if (kind === 'base64') this.base64Tool = { input: '', output: '', error: '' }
    },
    detectStructuredValue (value) {
      if (typeof value !== 'string' || value.trim().length < 3) return null
      if (this.extractJsonPretty(value) !== null) return 'json'
      const words = value.toLowerCase().match(/[a-z_]+/g)
      if (!words || words.length === 0) return null
      const statements = ['select', 'insert', 'update', 'delete', 'create', 'alter', 'drop', 'truncate', 'merge', 'with']
      const support = ['from', 'into', 'set', 'table', 'values', 'database', 'index', 'view', 'procedure']
      if (statements.includes(words[0]) && words.some(word => support.includes(word))) return 'sql'
      return null
    },
    extractLogSql (row) {
      if (!row) return ''
      const properties = row.properties || {}
      const priorityKeys = ['sql', 'query', 'commandtext', 'command_text', 'statement', 'databasecommand', 'database_command']
      for (const [key, value] of Object.entries(properties)) {
        if (priorityKeys.includes(String(key).toLowerCase()) && this.detectStructuredValue(String(value)) === 'sql') return String(value).trim()
      }
      for (const value of [...Object.values(properties), row.message, row.rawText]) {
        if (typeof value !== 'string') continue
        const match = value.match(/\b(select|with|insert|update|delete|merge|create|alter|drop|truncate)\b[\s\S]*/i)
        if (match && this.detectStructuredValue(match[0]) === 'sql') return match[0].trim()
      }
      return ''
    },
    formatSql (value) {
      if (!value) return ''
      return String(value).trim().replace(/\s+/g, ' ').replace(/\s*;\s*/g, ';\n').replace(/\b(FROM|WHERE|GROUP\s+BY|ORDER\s+BY|HAVING|LIMIT|OFFSET|VALUES|SET|UNION(?:\s+ALL)?|INNER\s+JOIN|LEFT\s+JOIN|RIGHT\s+JOIN|FULL\s+JOIN|JOIN|ON|AND|OR)\b/gi, '\n$1').replace(/\n\s*\n/g, '\n').trim()
    },
    highlightSql (value) {
      return this.escapeHtml(value).replace(/\b(select|from|where|insert|into|values|update|set|delete|create|table|primary|key|foreign|references|alter|drop|truncate|merge|index|view|inner|left|right|full|outer|join|on|group|by|order|having|limit|offset|as|distinct|union|all|with|and|or|not|null|asc|desc|exec|use)\b/gi, m => `<span class="token-keyword">${m}</span>`)
    },
    isLongCell (value) { return this.formatCell(value).length > 36 },
    openTextViewer (key, value, kind = null) {
      const text = this.formatCell(value)
      const detected = kind || this.detectStructuredValue(text)
      this.propertyViewer = { key, kind: detected, text, pretty: detected === 'json' ? this.extractJsonPretty(text) || text : text }
      this.propertyViewerDialog = true
    },
    openPropertyValueViewer (row) {
      this.openTextViewer(row.key, row.value)
    },
    escapeHtml (text) {
      return String(text).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    },
    copyPropertyValue () {
      const text = this.propertyViewer.kind === 'json' && this.propertyViewer.pretty ? this.propertyViewer.pretty : this.propertyViewer.text
      navigator.clipboard.writeText(text).then(() => ElMessage.success('已复制')).catch(() => ElMessage.error('复制失败'))
    },
    topicOf (row) { return row.properties?.Topic ?? row.properties?.topic ?? null },
    async openApplicationLog (row, tab = 'overview') {
      this.selectedApplicationLog = row
      this.selectedApplicationLogSourceId = this.activeTab === 'realtimelogs' ? this.realtimeLogSourceId : this.applicationLogSourceId
      this.applicationLogPropertyPage = 1
      this.applicationLogDetailTab = tab === 'sql' && this.extractLogSql(row) ? 'sql' : tab
      this.logSqlProjects = []; this.logSqlProjectId = ''; this.logSqlDataSourceId = ''; this.logSqlResult = null; this.logSqlResultPage = 1
      this.applicationLogDialog = true
      if (this.extractLogSql(row)) await this.loadLogSqlProjects()
    },
    async loadLogSqlProjects () {
      if (!this.selectedApplicationLogSourceId) return
      try {
        this.logSqlProjects = (await axios.get(`/api/logs/${this.selectedApplicationLogSourceId}/sql/projects`)).data
        if (this.logSqlProjects.length === 1) this.logSqlProjectId = this.logSqlProjects[0].id
        this.logSqlProjectChanged()
      } catch (e) { this.error(e) }
    },
    logSqlProjectChanged () { this.logSqlDataSourceId = this.logSqlDataSources.length === 1 ? this.logSqlDataSources[0].id : ''; this.logSqlResult = null; this.logSqlResultPage = 1 },
    async executeSelectedLogSql () {
      if (!this.canExecuteSelectedLogSql) return
      this.logSqlLoading = true; this.logSqlResult = null; this.logSqlResultPage = 1
      try {
        this.logSqlResult = (await axios.post(`/api/logs/${this.selectedApplicationLogSourceId}/sql/query`, { projectId: this.logSqlProjectId, dataSourceId: this.logSqlDataSourceId, sql: this.selectedApplicationLogSql })).data
        ElMessage.success(`查询完成，返回 ${this.logSqlResult.rows.length} 行${this.logSqlResult.truncated ? '（已按数据源上限截断）' : ''}`)
      } catch (e) { this.error(e) } finally { this.logSqlLoading = false }
    },
    logSourceTypeName (type) { return ({ 1: '本地 NLog', 2: 'Seq', 3: '远程 Agent' })[type] || type },
    logLevelType (level) { const value = String(level || '').toLowerCase(); if (value === 'fatal' || value === 'error') return 'danger'; if (value === 'warn' || value === 'warning') return 'warning'; if (value === 'debug' || value === 'trace') return 'info'; return 'success' },
    async openApproval (row) { try { this.selectedApproval = (await axios.get(`/api/approvals/${row.id}`)).data; this.reviewComment = ''; this.approvalDialog = true } catch (e) { this.error(e) } },
    async reviewSelected (approved) {
      if (!this.selectedApproval) return
      this.saving = true
      try { await axios.post(`/api/approvals/${this.selectedApproval.id}/review`, { approved, comment: this.reviewComment }); this.approvalDialog = false; await this.loadApprovals(); ElMessage.success(approved ? '已批准并执行' : '已拒绝') } catch (e) { this.error(e) } finally { this.saving = false }
    },
    openLog (row) { this.selectedLog = row; this.selectedLogRowPage = 1; this.logDialog = true },
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
    openClientDialog (row) {
      this.editingClient = row || null
      this.clientForm = row ? { displayName: row.displayName || '', scopes: [...(row.scopes || [])] } : { displayName: 'Local AI Client', scopes: this.oauthScopeOptions.map(item => item.value) }
      this.clientDialog = true
    },
    async saveClient () {
      if (!this.clientForm.displayName.trim()) { ElMessage.warning('请输入客户端名称'); return }
      this.saving = true
      try {
        if (this.editingClient) {
          await axios.put(`/api/admin/oauth-clients/${encodeURIComponent(this.editingClient.clientId)}`, { displayName: this.clientForm.displayName.trim(), scopes: this.clientForm.scopes })
          ElMessage.success('客户端名称和权限已更新，请让 AI 重新获取 Token')
        } else {
          this.generatedClient = (await axios.post('/api/admin/oauth-clients', { displayName: this.clientForm.displayName.trim(), scopes: this.clientForm.scopes })).data
        }
        this.clientDialog = false; await this.loadClients()
      } catch (e) { this.error(e) } finally { this.saving = false }
    },
    oauthScopeName (scope) { return this.oauthScopeOptions.find(item => item.value === scope)?.label || scope },
    async deleteClient (row) { try { await ElMessageBox.confirm(`吊销并删除 ${row.displayName || row.clientId}？该客户端将无法获取新 Token，已签发 Token 也会立即失效。`, '吊销 OAuth2 客户端', { type: 'warning', confirmButtonText: '吊销并删除' }); await axios.delete(`/api/admin/oauth-clients/${encodeURIComponent(row.clientId)}`); await this.loadClients(); ElMessage.success('OAuth2 客户端已吊销并删除') } catch (e) { if (!this.isCanceled(e)) this.error(e) } },
    async saveMaintenanceSettings () {
      this.saving = true
      try { this.maintenanceSettings = (await axios.put('/api/settings/maintenance', { cleanupEnabled: this.maintenanceSettings.cleanupEnabled, retentionDays: this.maintenanceSettings.retentionDays, cleanupTimeLocal: this.maintenanceSettings.cleanupTimeLocal, approvalExpirationMinutes: this.maintenanceSettings.approvalExpirationMinutes })).data; ElMessage.success('系统设置已保存') } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async saveAdminRecoveryPassword () {
      const value = this.adminRecovery.newResetPassword.trim()
      if (value.length < 4) { ElMessage.warning('重置口令至少需要 4 位'); return }
      try { await ElMessageBox.confirm('修改后，登录页只能使用新的重置口令恢复管理员密码。请确认已经妥善保存。', '修改管理员重置口令', { type: 'warning' }) } catch (_) { return }
      this.saving = true
      try {
        const status = (await axios.put('/api/settings/admin-recovery', { newResetPassword: value })).data
        this.adminRecovery = { ...status, newResetPassword: '' }
        ElMessage.success('管理员重置口令已修改')
      } catch (e) { this.error(e) } finally { this.saving = false }
    },
    async cleanupNow () {
      try { await ElMessageBox.confirm(`将删除 ${this.maintenanceSettings.retentionDays} 天以前的运行日志、审批记录、服务器指标和日志文件，是否继续？`, '立即清理', { type: 'warning' }) } catch (e) { if (!this.isCanceled(e)) this.error(e); return }
      this.saving = true
      try { const result = (await axios.post('/api/settings/maintenance/cleanup-now')).data; await this.loadMaintenanceSettings(); ElMessage.success(`清理完成：日志 ${result.auditLogsDeleted} 条，审批 ${result.approvalRecordsDeleted} 条，指标 ${result.metricSamplesDeleted} 条，文件 ${result.logFilesDeleted} 个`) } catch (e) { this.error(e) } finally { this.saving = false }
    },
    formatDate (value) { return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—' },
    shortId (value) { return value ? String(value).slice(0, 8) : '—' },
    clampPage (page, total, pageSize) { return Math.min(Math.max(1, page), Math.max(1, Math.ceil(total / pageSize))) },
    approvalStatusName (status) { return ({ Pending: '待审批', Approved: '已批准', Rejected: '已拒绝', Executing: '执行中', Succeeded: '执行成功', Failed: '执行失败', Expired: '已过期' })[status] || status },
    approvalStatusType (status) { return ({ Pending: 'warning', Succeeded: 'success', Approved: 'primary', Rejected: 'danger', Failed: 'danger', Expired: 'info' })[status] || 'info' },
    riskType (risk) { return ({ Low: 'success', Medium: 'warning', High: 'danger', Critical: 'danger' })[risk] || 'info' },
    outcomeName (outcome) { return ({ success: '成功', failure: '失败', pending: '待处理', rejected: '已拒绝' })[outcome] || outcome },
    outcomeType (outcome) { return ({ success: 'success', failure: 'danger', pending: 'warning', rejected: 'danger' })[outcome] || 'info' },
    actionName (action) { return ({ 'system.setup': '系统初始化', 'auth.login': '用户登录', 'auth.logout': '用户退出', 'auth.admin-password-reset.verify': '校验管理员重置口令', 'auth.admin-password-reset': '重置管理员登录密码', 'settings.admin-recovery.update': '修改管理员重置口令', 'query.execute': 'AI 只读查询', 'query.blocked': '黑名单拦截查询', 'change.submit': '提交 SQL 工单', 'change.review': '审核 SQL 工单', 'change.execute': '执行 SQL 变更', 'datasource.create': '创建数据源', 'datasource.update': '更新数据源', 'datasource.delete': '删除数据源', 'datasource.test': '测试数据源', 'project.create': '创建项目', 'project.update': '更新项目', 'project.delete': '删除项目', 'logsource.create': '创建日志源', 'logsource.update': '更新日志源', 'logsource.delete': '删除日志源', 'logsource.test': '测试日志源', 'log.query': '读取应用日志', 'settings.maintenance.update': '更新系统设置', 'maintenance.cleanup': '清理日志与记录', 'user.create': '创建用户', 'user.update': '更新用户', 'user.delete': '删除用户', 'oauth-client.create': '创建 OAuth2 客户端', 'oauth-client.delete': '吊销 OAuth2 客户端' })[action] || action },
    isCanceled (e) { return e === 'cancel' || e === 'close' || e?.message === 'cancel' || e?.message === 'close' }
  }
}
</script>
