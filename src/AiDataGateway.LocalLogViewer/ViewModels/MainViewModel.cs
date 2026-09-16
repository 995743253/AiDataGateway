using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Data;
using AiDataGateway.LocalLogViewer.Models;
using AiDataGateway.LocalLogViewer.Services;

namespace AiDataGateway.LocalLogViewer.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        private readonly ConfigurationStore _store;
        private readonly CredentialProtector _protector;
        private readonly DatabaseConnectionService _databaseConnections;
        private readonly LocalLogReader _reader = new LocalLogReader();
        private readonly StructuredValueService _structuredValues = new StructuredValueService();
        private readonly LogSqlService _sqlService = new LogSqlService();
        private readonly List<LogEntry> _loadedEntries = new List<LogEntry>();
        private readonly List<LogEntry> _allEntries = new List<LogEntry>();
        private CancellationTokenSource _queryCancellation;
        private LogProfile _selectedLogProfile;
        private DatabaseProfile _selectedDatabaseProfile;
        private LogProfile _logEditor;
        private DatabaseProfile _databaseEditor;
        private LogEntry _selectedLogEntry;
        private DateTime? _fromDate = DateTime.Today;
        private DateTime? _toDate = DateTime.Today;
        private RecentRangeOption _selectedRecentRange;
        private string _level = "全部";
        private string _searchText = string.Empty;
        private string _selectedFunction = "全部函数";
        private string _databasePasswordInput = string.Empty;
        private string _statusText = "请选择日志配置并查询。";
        private bool _isBusy;
        private bool _isDialogOpen;
        private bool _isDialogConfirmation;
        private string _dialogTitle = string.Empty;
        private string _dialogMessage = string.Empty;
        private string _dialogConfirmText = "确定";
        private Action _dialogConfirmedAction;
        private string _currentPage = "logs";
        private WorkspaceTabItem _selectedWorkspaceTab;
        private LogProfile _activeLogProfile;
        private DatabaseProfile _selectedSqlDatabaseProfile;
        private DataView _sqlResultView;
        private string _sqlParametersJson = string.Empty;
        private string _sqlParameterMode = "逐项输入";
        private string _sqlResultSummary = "尚未执行查询";
        private bool _isSqlBusy;
        private int _page = 1;
        private bool _hasQueried;
        private const int PageSize = 100;

        public MainViewModel()
        {
            _store = new ConfigurationStore();
            _protector = new CredentialProtector();
            _databaseConnections = new DatabaseConnectionService(_protector);
            LogProfiles = new ObservableCollection<LogProfile>(_store.LoadLogProfiles());
            DatabaseProfiles = new ObservableCollection<DatabaseProfile>(_store.LoadDatabaseProfiles());
            LogEntries = new ObservableCollection<LogEntry>();
            StructuredNodes = new ObservableCollection<StructuredNode>();
            SqlParameterInputs = new ObservableCollection<SqlParameterInput>();
            SqlParameterModes = new ObservableCollection<string>(new[] { "逐项输入", "JSON" });
            WorkspaceTabs = new ObservableCollection<WorkspaceTabItem> { new WorkspaceTabItem("logs", "logs", "应用日志", true, null) };
            _selectedWorkspaceTab = WorkspaceTabs[0];
            Levels = new ObservableCollection<string>(new[] { "全部", "Trace", "Debug", "Info", "Information", "Warn", "Warning", "Error", "Fatal" });
            FunctionNames = new ObservableCollection<string>(new[] { "全部函数" });
            RecentRanges = new ObservableCollection<RecentRangeOption>(new[]
            {
                new RecentRangeOption("自定义日期", null),
                new RecentRangeOption("最近 15 分钟", TimeSpan.FromMinutes(15)),
                new RecentRangeOption("最近 30 分钟", TimeSpan.FromMinutes(30)),
                new RecentRangeOption("最近 1 小时", TimeSpan.FromHours(1)),
                new RecentRangeOption("最近 3 小时", TimeSpan.FromHours(3)),
                new RecentRangeOption("最近 6 小时", TimeSpan.FromHours(6)),
                new RecentRangeOption("最近 12 小时", TimeSpan.FromHours(12)),
                new RecentRangeOption("最近 24 小时", TimeSpan.FromHours(24)),
                new RecentRangeOption("最近 3 天", TimeSpan.FromDays(3)),
                new RecentRangeOption("最近 7 天", TimeSpan.FromDays(7))
            });
            _selectedRecentRange = RecentRanges[0];
            Providers = new ObservableCollection<ProviderOption>(new[]
            {
                new ProviderOption("SQL Server（系统自带）", "System.Data.SqlClient", 1433),
                new ProviderOption("MySQL", "MySql.Data.MySqlClient", 3306),
                new ProviderOption("PostgreSQL", "Npgsql", 5432),
                new ProviderOption("Oracle", "Oracle.ManagedDataAccess.Client", 1521),
                new ProviderOption("SQLite", "System.Data.SQLite", 0)
            });
            NewLogCommand = new RelayCommand(NewLog);
            SaveLogCommand = new RelayCommand(SaveLog);
            DeleteLogCommand = new RelayCommand(DeleteLog, () => SelectedLogProfile != null);
            NewDatabaseCommand = new RelayCommand(NewDatabase);
            SaveDatabaseCommand = new RelayCommand(SaveDatabase);
            DeleteDatabaseCommand = new RelayCommand(DeleteDatabase, () => SelectedDatabaseProfile != null);
            QueryCommand = new RelayCommand(async () => await QueryAsync(), () => !IsBusy && ActiveLogProfile != null);
            CancelCommand = new RelayCommand(Cancel, () => IsBusy);
            PreviousPageCommand = new RelayCommand(() => { Page--; RefreshPage(); }, () => Page > 1);
            NextPageCommand = new RelayCommand(() => { Page++; RefreshPage(); }, () => Page < TotalPages);
            ConfirmDialogCommand = new RelayCommand(ConfirmDialog);
            CancelDialogCommand = new RelayCommand(CloseDialog);
            NavigateLogsCommand = new RelayCommand(() => SelectWorkspaceTab("logs"));
            NavigateLogConfigurationCommand = new RelayCommand(() => CurrentPage = "log-configuration");
            NavigateDatabaseConfigurationCommand = new RelayCommand(() => CurrentPage = "database-configuration");
            OpenLogDetailCommand = new RelayCommand(parameter => OpenLogDetail(parameter as LogEntry));
            OpenLogSqlCommand = new RelayCommand(parameter => OpenLogSql(parameter as LogEntry));
            BackToLogsCommand = new RelayCommand(() => SelectWorkspaceTab("logs"));
            CloseWorkspaceTabCommand = new RelayCommand(parameter => CloseWorkspaceTab(parameter as WorkspaceTabItem));
            ActivateLogProfileCommand = new RelayCommand(parameter => ActivateLogProfile(parameter as LogProfile));
            ExecuteLogSqlCommand = new RelayCommand(async () => await ExecuteLogSqlAsync(), () => CanExecuteLogSql);

            if (LogProfiles.Count > 0)
            {
                ActiveLogProfile = LogProfiles.FirstOrDefault(item => item.IsActive) ?? LogProfiles[0];
                if (!ActiveLogProfile.IsActive) { ActiveLogProfile.IsActive = true; _store.SaveLogProfiles(LogProfiles); }
                SelectedLogProfile = ActiveLogProfile;
            }
            else NewLog();
            if (DatabaseProfiles.Count > 0) SelectedDatabaseProfile = DatabaseProfiles[0]; else NewDatabase();
            StatusText = "请选择或新建日志配置，然后设置日期范围进行查询。";
        }

        public ObservableCollection<LogProfile> LogProfiles { get; }
        public ObservableCollection<DatabaseProfile> DatabaseProfiles { get; }
        public ObservableCollection<LogEntry> LogEntries { get; }
        public ObservableCollection<StructuredNode> StructuredNodes { get; }
        public ObservableCollection<SqlParameterInput> SqlParameterInputs { get; }
        public ObservableCollection<string> SqlParameterModes { get; }
        public ObservableCollection<WorkspaceTabItem> WorkspaceTabs { get; }
        public ObservableCollection<string> Levels { get; }
        public ObservableCollection<string> FunctionNames { get; }
        public ObservableCollection<RecentRangeOption> RecentRanges { get; }
        public ObservableCollection<ProviderOption> Providers { get; }
        public int PageSizeValue => PageSize;

        public LogProfile SelectedLogProfile
        {
            get => _selectedLogProfile;
            set
            {
                if (!Set(ref _selectedLogProfile, value)) return;
                LogEditor = value == null ? null : value.Copy();
                RaiseCommands();
            }
        }
        public LogProfile ActiveLogProfile
        {
            get => _activeLogProfile;
            private set
            {
                if (!Set(ref _activeLogProfile, value)) return;
                SelectedSqlDatabaseProfile = value == null ? null : DatabaseProfiles.FirstOrDefault(item => item.Id == value.DatabaseProfileId);
                OnPropertyChanged(nameof(ActiveLogSourceSummary));
                RaiseCommands();
            }
        }
        public string ActiveLogSourceSummary => ActiveLogProfile == null ? "未启用日志源" : "当前日志源 · " + ActiveLogProfile.Name;
        public DatabaseProfile SelectedDatabaseProfile
        {
            get => _selectedDatabaseProfile;
            set
            {
                if (!Set(ref _selectedDatabaseProfile, value)) return;
                DatabaseEditor = value == null ? null : value.Copy();
                DatabasePasswordInput = string.Empty;
                RaiseCommands();
            }
        }
        public LogProfile LogEditor { get => _logEditor; set => Set(ref _logEditor, value); }
        public DatabaseProfile DatabaseEditor { get => _databaseEditor; set => Set(ref _databaseEditor, value); }
        public LogEntry SelectedLogEntry
        {
            get => _selectedLogEntry;
            set
            {
                if (!Set(ref _selectedLogEntry, value)) return;
                StructuredNodes.Clear();
                if (value != null) foreach (var node in _structuredValues.BuildTree(value.Properties)) StructuredNodes.Add(node);
                BuildSqlParameterInputs(value == null ? null : value.SqlText);
                OnPropertyChanged(nameof(SqlPlaceholderCount));
                OnPropertyChanged(nameof(HasSqlParameters));
                RaiseCommands();
            }
        }
        public DateTime? FromDate { get => _fromDate; set => Set(ref _fromDate, value); }
        public DateTime? ToDate { get => _toDate; set => Set(ref _toDate, value); }
        public RecentRangeOption SelectedRecentRange
        {
            get => _selectedRecentRange;
            set
            {
                if (!Set(ref _selectedRecentRange, value)) return;
                OnPropertyChanged(nameof(IsCustomDateRange));
                StatusText = IsCustomDateRange ? "使用自定义日期范围查询。" : "查询时将以日志中的最新时间为终点，读取“" + value.Name + "”。";
            }
        }
        public bool IsCustomDateRange => SelectedRecentRange == null || !SelectedRecentRange.Duration.HasValue;
        public string Level { get => _level; set => Set(ref _level, value); }
        public string SelectedFunction
        {
            get => _selectedFunction;
            set
            {
                if (!Set(ref _selectedFunction, string.IsNullOrWhiteSpace(value) ? "全部函数" : value)) return;
                ApplyFunctionFilter();
            }
        }
        public string SearchText { get => _searchText; set => Set(ref _searchText, value); }
        public string DatabasePasswordInput { get => _databasePasswordInput; set => Set(ref _databasePasswordInput, value); }
        public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
        public bool IsDialogOpen { get => _isDialogOpen; set => Set(ref _isDialogOpen, value); }
        public bool IsDialogConfirmation { get => _isDialogConfirmation; set => Set(ref _isDialogConfirmation, value); }
        public string DialogTitle { get => _dialogTitle; set => Set(ref _dialogTitle, value); }
        public string DialogMessage { get => _dialogMessage; set => Set(ref _dialogMessage, value); }
        public string DialogConfirmText { get => _dialogConfirmText; set => Set(ref _dialogConfirmText, value); }
        public string CurrentPage
        {
            get => _currentPage;
            set
            {
                if (!Set(ref _currentPage, value)) return;
                OnPropertyChanged(nameof(IsLogsPage));
                OnPropertyChanged(nameof(IsLogsNavigationActive));
                OnPropertyChanged(nameof(IsLogDetailPage));
                OnPropertyChanged(nameof(IsLogSqlPage));
                OnPropertyChanged(nameof(IsDocumentPage));
                OnPropertyChanged(nameof(IsLogConfigurationPage));
                OnPropertyChanged(nameof(IsDatabaseConfigurationPage));
                var workspaceTab = WorkspaceTabs.FirstOrDefault(item => item.Key == value);
                if (workspaceTab != null && !ReferenceEquals(_selectedWorkspaceTab, workspaceTab))
                {
                    _selectedWorkspaceTab = workspaceTab;
                    OnPropertyChanged(nameof(SelectedWorkspaceTab));
                }
            }
        }
        public WorkspaceTabItem SelectedWorkspaceTab
        {
            get => _selectedWorkspaceTab;
            set
            {
                if (!Set(ref _selectedWorkspaceTab, value) || value == null) return;
                CurrentPage = value.PageKey;
                if (value.Entry != null) SelectedLogEntry = value.Entry;
            }
        }
        public bool IsLogsPage => CurrentPage == "logs";
        public bool IsLogsNavigationActive => CurrentPage == "logs" || CurrentPage == "log-detail" || CurrentPage == "log-sql";
        public bool IsLogDetailPage => CurrentPage == "log-detail";
        public bool IsLogSqlPage => CurrentPage == "log-sql";
        public bool IsDocumentPage => IsLogDetailPage || IsLogSqlPage;
        public bool IsLogConfigurationPage => CurrentPage == "log-configuration";
        public bool IsDatabaseConfigurationPage => CurrentPage == "database-configuration";
        public DatabaseProfile SelectedSqlDatabaseProfile
        {
            get => _selectedSqlDatabaseProfile;
            private set { if (Set(ref _selectedSqlDatabaseProfile, value)) { OnPropertyChanged(nameof(SqlDatabaseName)); RaiseCommands(); } }
        }
        public string SqlDatabaseName => SelectedSqlDatabaseProfile == null ? "未关联数据库" : SelectedSqlDatabaseProfile.Name;
        public DataView SqlResultView { get => _sqlResultView; private set => Set(ref _sqlResultView, value); }
        public string SqlParametersJson { get => _sqlParametersJson; set { if (Set(ref _sqlParametersJson, value)) RaiseCommands(); } }
        public string SqlParameterMode { get => _sqlParameterMode; set { if (Set(ref _sqlParameterMode, value)) RaiseCommands(); } }
        public string SqlResultSummary { get => _sqlResultSummary; set => Set(ref _sqlResultSummary, value); }
        public bool IsSqlBusy { get => _isSqlBusy; set { if (Set(ref _isSqlBusy, value)) RaiseCommands(); } }
        public int SqlPlaceholderCount => SqlParameterInputs.Count;
        public bool HasSqlParameters => SqlPlaceholderCount > 0;
        public bool CanExecuteLogSql => !IsSqlBusy && SelectedLogEntry != null && SelectedLogEntry.SqlIsReadOnly && SelectedSqlDatabaseProfile != null;
        public bool IsBusy
        {
            get => _isBusy;
            set { if (Set(ref _isBusy, value)) RaiseCommands(); }
        }
        public int Page
        {
            get => _page;
            set { if (Set(ref _page, Math.Max(1, value))) { OnPropertyChanged(nameof(PageSummary)); RaiseCommands(); } }
        }
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(_allEntries.Count / (double)PageSize));
        public string PageSummary => "第 " + Page + " / " + TotalPages + " 页 · 共 " + _allEntries.Count + " 条";

        public RelayCommand NewLogCommand { get; }
        public RelayCommand SaveLogCommand { get; }
        public RelayCommand DeleteLogCommand { get; }
        public RelayCommand NewDatabaseCommand { get; }
        public RelayCommand SaveDatabaseCommand { get; }
        public RelayCommand DeleteDatabaseCommand { get; }
        public RelayCommand QueryCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand PreviousPageCommand { get; }
        public RelayCommand NextPageCommand { get; }
        public RelayCommand ConfirmDialogCommand { get; }
        public RelayCommand CancelDialogCommand { get; }
        public RelayCommand NavigateLogsCommand { get; }
        public RelayCommand NavigateLogConfigurationCommand { get; }
        public RelayCommand NavigateDatabaseConfigurationCommand { get; }
        public RelayCommand OpenLogDetailCommand { get; }
        public RelayCommand OpenLogSqlCommand { get; }
        public RelayCommand BackToLogsCommand { get; }
        public RelayCommand CloseWorkspaceTabCommand { get; }
        public RelayCommand ActivateLogProfileCommand { get; }
        public RelayCommand ExecuteLogSqlCommand { get; }

        public async Task<string> TestDatabaseAsync()
        {
            if (DatabaseEditor == null) throw new InvalidOperationException("请先选择数据库配置。");
            StatusText = "正在测试数据库连接……";
            var result = await _databaseConnections.TestAsync(DatabaseEditor, DatabasePasswordInput);
            StatusText = result;
            return result;
        }

        public void ShowAlert(string title, string message)
        {
            DialogTitle = title;
            DialogMessage = message;
            DialogConfirmText = "知道了";
            IsDialogConfirmation = false;
            _dialogConfirmedAction = null;
            IsDialogOpen = true;
        }

        private void OpenLogDetail(LogEntry entry)
        {
            if (entry == null) return;
            SelectedLogEntry = entry;
            var tab = new WorkspaceTabItem(Guid.NewGuid().ToString("N"), "log-detail", BuildDocumentTitle("详情", entry), false, entry);
            foreach (var node in _structuredValues.BuildTree(entry.Properties)) tab.StructuredNodes.Add(node);
            WorkspaceTabs.Add(tab);
            SelectedWorkspaceTab = tab;
        }

        private void OpenLogSql(LogEntry entry)
        {
            if (entry == null || !entry.HasSql) return;
            SelectedLogEntry = entry;
            var database = ActiveLogProfile == null ? null : DatabaseProfiles.FirstOrDefault(item => item.Id == ActiveLogProfile.DatabaseProfileId);
            var tab = new WorkspaceTabItem(Guid.NewGuid().ToString("N"), "log-sql", BuildDocumentTitle("SQL", entry), false, entry)
            {
                SelectedSqlDatabaseProfile = database,
                SqlParameterMode = "逐项输入",
                SqlParametersJson = BuildJsonExample(_sqlService.GetPlaceholders(entry.SqlText)),
                SqlResultSummary = "尚未执行查询"
            };
            foreach (var placeholder in _sqlService.GetPlaceholders(entry.SqlText))
                tab.SqlParameterInputs.Add(new SqlParameterInput { Key = placeholder.Key, Token = placeholder.Token, Label = placeholder.Label });
            tab.ExecuteSqlCommand = new RelayCommand(async () => await ExecuteLogSqlAsync(tab), () => tab.CanExecuteSql);
            WorkspaceTabs.Add(tab);
            SelectedWorkspaceTab = tab;
        }

        private static string BuildDocumentTitle(string prefix, LogEntry entry)
        {
            var time = entry.Timestamp.HasValue ? entry.Timestamp.Value.ToString("HH:mm:ss") : "未标记时间";
            var functionName = entry.FunctionName;
            if (!string.IsNullOrWhiteSpace(functionName))
            {
                var segments = functionName.Split('.');
                functionName = segments[segments.Length - 1];
                if (functionName.Length > 22) functionName = functionName.Substring(0, 22) + "…";
                return prefix + " · " + time + " · " + functionName;
            }
            return prefix + " · " + time;
        }

        private void SelectWorkspaceTab(string key)
        {
            var tab = WorkspaceTabs.FirstOrDefault(item => item.Key == key);
            if (tab != null) SelectedWorkspaceTab = tab;
            else CurrentPage = key;
        }

        private void CloseWorkspaceTab(WorkspaceTabItem tab)
        {
            if (tab == null || tab.IsPermanent) return;
            WorkspaceTabs.Remove(tab);
            SelectWorkspaceTab("logs");
        }

        public bool DetachWorkspaceTab(WorkspaceTabItem tab)
        {
            if (tab == null || tab.IsPermanent || !WorkspaceTabs.Contains(tab)) return false;
            WorkspaceTabs.Remove(tab);
            SelectWorkspaceTab("logs");
            return true;
        }

        public void AttachWorkspaceTab(WorkspaceTabItem tab)
        {
            if (tab == null || tab.IsPermanent) return;
            if (!WorkspaceTabs.Contains(tab)) WorkspaceTabs.Add(tab);
            SelectedWorkspaceTab = tab;
        }

        private void NewLog()
        {
            SelectedLogProfile = null;
            LogEditor = new LogProfile();
            StatusText = "正在新建日志源，保存后可将其设为当前使用。";
        }

        private void SaveLog()
        {
            if (LogEditor == null || string.IsNullOrWhiteSpace(LogEditor.Name) || string.IsNullOrWhiteSpace(LogEditor.RootDirectory))
            { ShowAlert("无法保存", "日志配置名称和日志目录不能为空，请补充后再保存。"); return; }
            LogEditor.MaximumReadMegabytesPerFile = Math.Max(1, Math.Min(10, LogEditor.MaximumReadMegabytesPerFile));
            var existing = LogProfiles.FirstOrDefault(item => item.Id == LogEditor.Id);
            if (existing == null)
            {
                existing = LogEditor.Copy();
                if (ActiveLogProfile == null) existing.IsActive = true;
                LogProfiles.Add(existing);
            }
            else CopyLog(LogEditor, existing);
            _store.SaveLogProfiles(LogProfiles);
            SelectedLogProfile = existing;
            if (existing.IsActive) ActiveLogProfile = existing;
            StatusText = "日志源配置已安全保存。";
        }

        private void DeleteLog()
        {
            if (SelectedLogProfile == null) return;
            var profile = SelectedLogProfile;
            ShowConfirmation("删除日志配置", "确定删除“" + profile.Name + "”吗？此操作不会删除磁盘上的日志文件。", "删除", () =>
            {
                var wasActive = profile.IsActive;
                LogProfiles.Remove(profile);
                if (wasActive && LogProfiles.Count > 0) { LogProfiles[0].IsActive = true; ActiveLogProfile = LogProfiles[0]; }
                else if (LogProfiles.Count == 0) ActiveLogProfile = null;
                _store.SaveLogProfiles(LogProfiles); SelectedLogProfile = LogProfiles.FirstOrDefault(); if (SelectedLogProfile == null) NewLog();
            });
        }

        private void NewDatabase()
        {
            SelectedDatabaseProfile = null;
            DatabaseEditor = new DatabaseProfile();
            DatabasePasswordInput = string.Empty;
            StatusText = "正在新建数据库配置；密码保存时会使用 Windows DPAPI 加密。";
        }

        private void SaveDatabase()
        {
            if (DatabaseEditor == null || string.IsNullOrWhiteSpace(DatabaseEditor.Name)) { ShowAlert("无法保存", "数据库配置名称不能为空，请填写一个便于识别的名称。"); return; }
            var existing = DatabaseProfiles.FirstOrDefault(item => item.Id == DatabaseEditor.Id);
            if (!string.IsNullOrEmpty(DatabasePasswordInput)) DatabaseEditor.ProtectedPassword = _protector.Protect(DatabasePasswordInput);
            else if (existing != null) DatabaseEditor.ProtectedPassword = existing.ProtectedPassword;
            if (existing == null) { existing = DatabaseEditor.Copy(); DatabaseProfiles.Add(existing); }
            else CopyDatabase(DatabaseEditor, existing);
            _store.SaveDatabaseProfiles(DatabaseProfiles);
            DatabasePasswordInput = string.Empty;
            SelectedDatabaseProfile = existing;
            StatusText = "数据库配置及加密凭据已安全保存。";
        }

        private void DeleteDatabase()
        {
            if (SelectedDatabaseProfile == null) return;
            var profile = SelectedDatabaseProfile;
            ShowConfirmation("删除数据库配置", "确定删除“" + profile.Name + "”吗？保存的加密凭据也会一并移除。", "删除", () =>
            {
                DatabaseProfiles.Remove(profile); _store.SaveDatabaseProfiles(DatabaseProfiles); SelectedDatabaseProfile = DatabaseProfiles.FirstOrDefault(); if (SelectedDatabaseProfile == null) NewDatabase();
            });
        }

        private async Task QueryAsync()
        {
            if (ActiveLogProfile == null) { ShowAlert("没有启用的日志源", "请先到“日志源配置”页面新增日志源，并将其中一个设为当前使用。"); return; }
            _queryCancellation?.Cancel();
            _queryCancellation = new CancellationTokenSource();
            IsBusy = true;
            StatusText = "正在读取日志……";
            try
            {
                var result = IsCustomDateRange
                    ? await _reader.ReadAsync(ActiveLogProfile, FromDate, ToDate, Level, SearchText, _queryCancellation.Token)
                    : await _reader.ReadRecentAsync(ActiveLogProfile, SelectedRecentRange.Duration.Value, Level, SearchText, _queryCancellation.Token);
                _loadedEntries.Clear(); _loadedEntries.AddRange(result.Items);
                _hasQueried = true;
                RefreshFunctionNames();
                ApplyFunctionFilter();
                var rangeText = !IsCustomDateRange && result.RangeStart.HasValue && result.RangeEnd.HasValue
                    ? "，时间范围 " + result.RangeStart.Value.ToString("yyyy-MM-dd HH:mm:ss") + " 至 " + result.RangeEnd.Value.ToString("yyyy-MM-dd HH:mm:ss")
                    : string.Empty;
                StatusText = "已读取 " + result.FilesScanned + " 个文件，匹配 " + result.Items.Count + " 条日志" + rangeText + (string.IsNullOrWhiteSpace(result.Warning) ? "。" : "；" + result.Warning + "。");
            }
            catch (OperationCanceledException) { StatusText = "查询已取消。"; }
            catch (Exception exception) { StatusText = "读取失败：" + exception.Message; ShowAlert("读取日志失败", exception.Message); }
            finally { IsBusy = false; }
        }

        private async Task ExecuteLogSqlAsync()
        {
            if (!CanExecuteLogSql) return;
            IsSqlBusy = true;
            SqlResultSummary = "正在执行只读查询……";
            try
            {
                var values = string.Equals(SqlParameterMode, "JSON", StringComparison.OrdinalIgnoreCase)
                    ? ParseJsonParameterValues()
                    : SqlParameterInputs.Select(item => ParseInputValue(item.Value)).ToList();
                var prepared = _sqlService.Prepare(SelectedLogEntry.SqlText, values, SelectedSqlDatabaseProfile.ProviderInvariantName);
                var result = await _databaseConnections.ExecuteReadOnlyAsync(SelectedSqlDatabaseProfile, prepared.Sql, 200, prepared.Parameters);
                SqlResultView = result.Table.DefaultView;
                SqlResultSummary = "返回 " + result.Table.Rows.Count + " 行" + (result.Truncated ? "，已按 200 行截断" : string.Empty);
            }
            catch (Exception exception)
            {
                SqlResultSummary = "查询失败";
                ShowAlert("SQL 查询失败", exception.Message);
            }
            finally { IsSqlBusy = false; }
        }

        private async Task ExecuteLogSqlAsync(WorkspaceTabItem tab)
        {
            if (tab == null || !tab.CanExecuteSql) return;
            tab.IsSqlBusy = true;
            tab.SqlResultSummary = "正在执行只读查询……";
            tab.ExecuteSqlCommand?.RaiseCanExecuteChanged();
            try
            {
                var values = string.Equals(tab.SqlParameterMode, "JSON", StringComparison.OrdinalIgnoreCase)
                    ? ParseJsonParameterValues(tab.Entry.SqlText, tab.SqlParametersJson)
                    : tab.SqlParameterInputs.Select(item => ParseInputValue(item.Value)).ToList();
                var prepared = _sqlService.Prepare(tab.Entry.SqlText, values, tab.SelectedSqlDatabaseProfile.ProviderInvariantName);
                var result = await _databaseConnections.ExecuteReadOnlyAsync(tab.SelectedSqlDatabaseProfile, prepared.Sql, 200, prepared.Parameters);
                tab.SqlResultView = result.Table.DefaultView;
                tab.SqlResultSummary = "返回 " + result.Table.Rows.Count + " 行" + (result.Truncated ? "，已按 200 行截断" : string.Empty);
            }
            catch (Exception exception)
            {
                tab.SqlResultSummary = "查询失败";
                ShowAlert("SQL 查询失败", exception.Message);
            }
            finally
            {
                tab.IsSqlBusy = false;
                tab.ExecuteSqlCommand?.RaiseCanExecuteChanged();
            }
        }

        private void Cancel() { _queryCancellation?.Cancel(); }

        private void BuildSqlParameterInputs(string sql)
        {
            SqlParameterInputs.Clear();
            foreach (var placeholder in _sqlService.GetPlaceholders(sql))
                SqlParameterInputs.Add(new SqlParameterInput { Key = placeholder.Key, Token = placeholder.Token, Label = placeholder.Label });
            OnPropertyChanged(nameof(SqlPlaceholderCount));
            OnPropertyChanged(nameof(HasSqlParameters));
        }

        private IList<object> ParseJsonParameterValues()
        {
            return ParseJsonParameterValues(SelectedLogEntry == null ? null : SelectedLogEntry.SqlText, SqlParametersJson);
        }

        private IList<object> ParseJsonParameterValues(string sql, string json)
        {
            var placeholders = _sqlService.GetPlaceholders(sql);
            if (placeholders.Count == 0) return new List<object>();
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("请粘贴参数 JSON。");
            object root;
            try { root = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 }.DeserializeObject(json); }
            catch (Exception exception) { throw new InvalidOperationException("参数 JSON 格式不正确：" + exception.Message, exception); }

            var array = root as object[];
            if (array != null)
            {
                if (array.Length != placeholders.Count) throw new InvalidOperationException("JSON 数组需要提供 " + placeholders.Count + " 个值，当前为 " + array.Length + " 个。");
                return array.ToList();
            }
            var dictionary = root as IDictionary<string, object>;
            if (dictionary != null)
            {
                var values = new List<object>();
                for (var index = 0; index < placeholders.Count; index++)
                {
                    var placeholder = placeholders[index];
                    object value;
                    if (!TryGetJsonValue(dictionary, placeholder, index, out value))
                        throw new InvalidOperationException("JSON 中缺少参数“" + placeholder.Label + "”。可使用键名 " + placeholder.Key + "。");
                    values.Add(value);
                }
                return values;
            }
            if (placeholders.Count == 1) return new List<object> { root };
            throw new InvalidOperationException("多个参数请使用 JSON 对象或数组。");
        }

        private static bool TryGetJsonValue(IDictionary<string, object> dictionary, SqlPlaceholder placeholder, int index, out object value)
        {
            var candidates = new[] { placeholder.Key, placeholder.Token, "p" + (index + 1), (index + 1).ToString(CultureInfo.InvariantCulture), "?" + (index + 1) };
            foreach (var candidate in candidates)
                foreach (var pair in dictionary)
                    if (string.Equals(pair.Key, candidate, StringComparison.OrdinalIgnoreCase)) { value = pair.Value; return true; }
            value = null;
            return false;
        }

        private static object ParseInputValue(string value)
        {
            if (value == null || string.Equals(value.Trim(), "null", StringComparison.OrdinalIgnoreCase)) return DBNull.Value;
            var trimmed = value.Trim();
            bool boolean;
            if (bool.TryParse(trimmed, out boolean)) return boolean;
            long integer;
            if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) return integer;
            decimal number;
            if (decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
            return value;
        }

        private static string BuildJsonExample(IList<SqlPlaceholder> placeholders)
        {
            if (placeholders == null || placeholders.Count == 0) return string.Empty;
            var pairs = placeholders.Select(item => "  \"" + item.Key.Replace("\"", "\\\"") + "\": \"\"");
            return "{\r\n" + string.Join(",\r\n", pairs) + "\r\n}";
        }
        private void RefreshFunctionNames()
        {
            var previous = SelectedFunction;
            var names = _loadedEntries.Select(item => item.FunctionName)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
                .Take(1000)
                .ToList();
            FunctionNames.Clear();
            FunctionNames.Add("全部函数");
            foreach (var name in names) FunctionNames.Add(name);
            SelectedFunction = FunctionNames.FirstOrDefault(item => string.Equals(item, previous, StringComparison.OrdinalIgnoreCase)) ?? "全部函数";
            OnPropertyChanged(nameof(FunctionFilterSummary));
        }

        private void ApplyFunctionFilter()
        {
            _allEntries.Clear();
            var source = string.Equals(SelectedFunction, "全部函数", StringComparison.OrdinalIgnoreCase)
                ? _loadedEntries
                : _loadedEntries.Where(item => string.Equals(item.FunctionName, SelectedFunction, StringComparison.OrdinalIgnoreCase));
            _allEntries.AddRange(source);
            Page = 1;
            RefreshPage();
            OnPropertyChanged(nameof(FunctionFilterSummary));
        }

        public string FunctionFilterSummary => !_hasQueried ? "查询后自动识别" : FunctionNames.Count <= 1 ? "日志未包含调用位置" : "已识别 " + (FunctionNames.Count - 1) + " 个打印位置";

        private void RefreshPage()
        {
            if (Page > TotalPages) Page = TotalPages;
            LogEntries.Clear(); foreach (var item in _allEntries.Skip((Page - 1) * PageSize).Take(PageSize)) LogEntries.Add(item);
            OnPropertyChanged(nameof(TotalPages)); OnPropertyChanged(nameof(PageSummary)); RaiseCommands();
        }
        private void RaiseCommands()
        {
            QueryCommand?.RaiseCanExecuteChanged(); CancelCommand?.RaiseCanExecuteChanged(); DeleteLogCommand?.RaiseCanExecuteChanged(); DeleteDatabaseCommand?.RaiseCanExecuteChanged(); PreviousPageCommand?.RaiseCanExecuteChanged(); NextPageCommand?.RaiseCanExecuteChanged(); ExecuteLogSqlCommand?.RaiseCanExecuteChanged();
        }
        private void ActivateLogProfile(LogProfile profile)
        {
            if (profile == null) return;
            foreach (var item in LogProfiles) item.IsActive = item.Id == profile.Id;
            ActiveLogProfile = profile;
            _store.SaveLogProfiles(LogProfiles);
            CollectionViewSource.GetDefaultView(LogProfiles).Refresh();
            StatusText = "当前日志源已切换为：“" + profile.Name + "”。";
        }
        private void ShowConfirmation(string title, string message, string confirmText, Action confirmedAction)
        {
            DialogTitle = title;
            DialogMessage = message;
            DialogConfirmText = confirmText;
            IsDialogConfirmation = true;
            _dialogConfirmedAction = confirmedAction;
            IsDialogOpen = true;
        }
        private void ConfirmDialog()
        {
            var action = _dialogConfirmedAction;
            CloseDialog();
            action?.Invoke();
        }
        private void CloseDialog()
        {
            IsDialogOpen = false;
            _dialogConfirmedAction = null;
        }
        private static void CopyLog(LogProfile source, LogProfile target) { target.Name = source.Name; target.RootDirectory = source.RootDirectory; target.NLogConfigurationPath = source.NLogConfigurationPath; target.NLogTargetName = source.NLogTargetName; target.LayoutOverride = source.LayoutOverride; target.EncodingName = source.EncodingName; target.FilePattern = source.FilePattern; target.MaximumReadMegabytesPerFile = source.MaximumReadMegabytesPerFile; target.DatabaseProfileId = source.DatabaseProfileId; }
        private static void CopyDatabase(DatabaseProfile source, DatabaseProfile target) { target.Name = source.Name; target.ProviderInvariantName = source.ProviderInvariantName; target.Host = source.Host; target.Port = source.Port; target.Database = source.Database; target.UserName = source.UserName; target.ProtectedPassword = source.ProtectedPassword; target.IntegratedSecurity = source.IntegratedSecurity; target.ConnectionTimeoutSeconds = source.ConnectionTimeoutSeconds; }
    }

    public sealed class SqlParameterInput : ObservableObject
    {
        private string _value = string.Empty;
        public string Key { get; set; }
        public string Token { get; set; }
        public string Label { get; set; }
        public string Value { get => _value; set => Set(ref _value, value); }
    }

    public sealed class WorkspaceTabItem : ObservableObject
    {
        private string _sqlParameterMode = "逐项输入";
        private string _sqlParametersJson = string.Empty;
        private DataView _sqlResultView;
        private string _sqlResultSummary = "尚未执行查询";
        private bool _isSqlBusy;

        public WorkspaceTabItem(string key, string pageKey, string title, bool isPermanent, LogEntry entry)
        {
            Key = key;
            PageKey = pageKey;
            Title = title;
            IsPermanent = isPermanent;
            Entry = entry;
            StructuredNodes = new ObservableCollection<StructuredNode>();
            SqlParameterInputs = new ObservableCollection<SqlParameterInput>();
            SqlParameterModes = new ObservableCollection<string>(new[] { "逐项输入", "JSON" });
        }
        public string Key { get; }
        public string PageKey { get; }
        public string Title { get; }
        public bool IsPermanent { get; }
        public bool CanClose => !IsPermanent;
        public LogEntry Entry { get; }
        public bool IsLogDetail => PageKey == "log-detail";
        public bool IsSql => PageKey == "log-sql";
        public ObservableCollection<StructuredNode> StructuredNodes { get; }
        public ObservableCollection<SqlParameterInput> SqlParameterInputs { get; }
        public ObservableCollection<string> SqlParameterModes { get; }
        public DatabaseProfile SelectedSqlDatabaseProfile { get; set; }
        public string SqlDatabaseName => SelectedSqlDatabaseProfile == null ? "未关联数据库" : SelectedSqlDatabaseProfile.Name;
        public string SqlParameterMode { get => _sqlParameterMode; set => Set(ref _sqlParameterMode, value); }
        public string SqlParametersJson { get => _sqlParametersJson; set => Set(ref _sqlParametersJson, value); }
        public DataView SqlResultView { get => _sqlResultView; set => Set(ref _sqlResultView, value); }
        public string SqlResultSummary { get => _sqlResultSummary; set => Set(ref _sqlResultSummary, value); }
        public bool IsSqlBusy { get => _isSqlBusy; set { if (Set(ref _isSqlBusy, value)) OnPropertyChanged(nameof(CanExecuteSql)); } }
        public int SqlPlaceholderCount => SqlParameterInputs.Count;
        public bool HasSqlParameters => SqlPlaceholderCount > 0;
        public bool CanExecuteSql => !IsSqlBusy && Entry != null && Entry.SqlIsReadOnly && SelectedSqlDatabaseProfile != null;
        public RelayCommand ExecuteSqlCommand { get; set; }
        public override string ToString() => Title;
    }
}
