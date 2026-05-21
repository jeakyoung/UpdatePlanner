using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Xml;

namespace UpdatePlanner
{
    public partial class MainWindow : Window
    {
        // ─── 필드 ─────────────────────────────────────────────────────────────

        private readonly ObservableCollection<DeployMapping> _mappings
            = new ObservableCollection<DeployMapping>();

        private ScheduleConfig _scheduleConfig;   // 상세 설정에서 구성한 예약 설정
        private DateTime?      _nextFireTime;      // 현재 활성 예약의 다음 실행 시각

        private DispatcherTimer       _countdownTimer;
        private CancellationTokenSource _cts;
        private bool _isRunning;
        private bool _forceExit;

        private System.Windows.Forms.NotifyIcon _trayIcon;
        private LogService _logService;

        private const string ConfigFile = "settings.xml";

        // ─── 생성자 ──────────────────────────────────────────────────────────

        public MainWindow()
        {
            InitializeComponent();

            DgMappings.ItemsSource = _mappings;

            InitializeTrayIcon();
            LoadSettings();

            _logService = new LogService();
            CleanupOldLogs();

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += CountdownTimer_Tick;
        }

        private void CleanupOldLogs()
        {
            if (int.TryParse(TxtRetentionDays.Text.Trim(), out int days))
                _logService.Cleanup(days);
        }

        // ─── 트레이 아이콘 ───────────────────────────────────────────────────

        private void InitializeTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();
            try
            {
                _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
            }
            catch
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            _trayIcon.Text    = "Update Planner";
            _trayIcon.Visible = false;
            _trayIcon.DoubleClick += (s, e) => RestoreWindow();

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("열기",     null, (s, e) => RestoreWindow());
            menu.Items.Add("-");
            menu.Items.Add("예약 취소", null, (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_nextFireTime.HasValue)
                        BtnCancelSchedule_Click(s, null);
                });
            });
            menu.Items.Add("-");
            menu.Items.Add("종료", null, (s, e) => Dispatcher.Invoke(TryExitFromTray));

            _trayIcon.ContextMenuStrip = menu;
        }

        private void MinimizeToTray()
        {
            ShowInTaskbar = false;
            Hide();
            _trayIcon.Visible = true;

            string tip = _nextFireTime.HasValue
                ? $"백그라운드 실행 중\n예약: {_nextFireTime:yyyy-MM-dd HH:mm:ss}"
                : "백그라운드에서 실행 중입니다.";

            _trayIcon.ShowBalloonTip(3000, "Update Planner", tip,
                System.Windows.Forms.ToolTipIcon.Info);
        }

        private void RestoreWindow()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _trayIcon.Visible = false;
        }

        private void TryExitFromTray()
        {
            if (_nextFireTime.HasValue)
            {
                _trayIcon.ShowBalloonTip(3000, "완전종료 불가",
                    "등록된 예약이 존재하여 완전종료가 불가능합니다.\n예약을 취소한 후 종료하세요.",
                    System.Windows.Forms.ToolTipIcon.Warning);
                return;
            }
            DoExit();
        }

        private void DoExit()
        {
            _forceExit = true;
            _cts?.Cancel();
            _countdownTimer.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Current.Shutdown();
        }

        // ─── 창 닫기 (X 버튼) ────────────────────────────────────────────────

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_forceExit)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;

            var dialog = new ExitDialog(_nextFireTime.HasValue) { Owner = this };
            dialog.ShowDialog();

            switch (dialog.Result)
            {
                case ExitResult.Background: MinimizeToTray(); break;
                case ExitResult.Exit:       DoExit();         break;
            }
        }

        // ─── 카운트다운 타이머 ───────────────────────────────────────────────

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            if (!_nextFireTime.HasValue) return;

            TimeSpan remaining = _nextFireTime.Value - DateTime.Now;
            if (remaining <= TimeSpan.Zero)
            {
                _countdownTimer.Stop();
                TxtCountdown.Text = "";
                _ = RunCopyAsync();
            }
            else
            {
                TxtCountdown.Text =
                    $"남은 시간: {(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}" +
                    $"  (실행 예정: {_nextFireTime.Value:yyyy-MM-dd HH:mm:ss})";

                if (!IsVisible)
                    _trayIcon.Text = $"예약: {_nextFireTime:HH:mm} | 남은 {(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }
        }

        // ─── 경로 설정 버튼 ──────────────────────────────────────────────────

        private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
            => OpenAddMappingDialog(MappingType.Folder);

        private void BtnAddFile_Click(object sender, RoutedEventArgs e)
            => OpenAddMappingDialog(MappingType.File);

        private void OpenAddMappingDialog(MappingType type)
        {
            var dlg = new AddMappingDialog(type) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                _mappings.Add(dlg.Result);
                DgMappings.SelectedItem = null;   // 추가 후 행 선택 해제
            }
        }

        /// <summary>
        /// PreviewMouseLeftButtonDown으로 처리 → DataGrid가 행 선택을 하기 전에 가로채
        /// 삭제 버튼 클릭 시 행 하이라이트 플래시가 생기지 않도록 합니다.
        /// </summary>
        private void BtnDeleteMapping_PreviewMouseDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is System.Windows.Controls.Button btn &&
                btn.Tag is DeployMapping mapping)
                _mappings.Remove(mapping);
        }

        // ─── 예약 버튼 ───────────────────────────────────────────────────────

        private void BtnScheduleDetail_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ScheduleDetailDialog(_scheduleConfig) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                _scheduleConfig = dlg.Result;
                TxtScheduleSummary.Text = _scheduleConfig.GetSummary();
                TxtScheduleSummary.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_mappings.Count == 0)
            {
                MessageBox.Show("배포 경로를 하나 이상 추가하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_scheduleConfig == null)
            {
                MessageBox.Show("[상세 설정]을 눌러 예약 일시를 먼저 설정하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DateTime? next = _scheduleConfig.GetNextFireTime(DateTime.Now);
            if (!next.HasValue)
            {
                MessageBox.Show("예약 가능한 시각이 없습니다.\n설정한 일시 또는 반복 조건을 확인하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _nextFireTime = next;
            SaveSettings();
            SetScheduledState(true);
            _countdownTimer.Start();

            Log($"예약 완료 → {_nextFireTime:yyyy-MM-dd HH:mm:ss}  [{_scheduleConfig.GetSummary()}]");
            foreach (var m in _mappings)
                Log($"  {m.Source}  →  {m.Dest}");
        }

        private void BtnCancelSchedule_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _cts?.Cancel();
            _nextFireTime = null;
            SetScheduledState(false);
            TxtStatus.Text    = "예약 취소됨";
            TxtCountdown.Text = "";
            _trayIcon.Text    = "Update Planner";
            Log("예약이 취소되었습니다.");
        }

        private async void BtnRunNow_Click(object sender, RoutedEventArgs e)
        {
            if (_mappings.Count == 0)
            {
                MessageBox.Show("배포 경로를 하나 이상 추가하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show("지금 즉시 업데이트를 실행하겠습니까?", "즉시 실행",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            SaveSettings();
            await RunCopyAsync();
        }

        // ─── 복사 실행 ───────────────────────────────────────────────────────

        private async System.Threading.Tasks.Task RunCopyAsync()
        {
            SetRunningState(true);
            TxtStatus.Text = "업데이트 실행 중...";
            Log("========== 업데이트 시작 ==========");

            _cts = new CancellationTokenSource();

            if (!IsVisible)
                _trayIcon.ShowBalloonTip(2000, "Update Planner", "업데이트를 시작합니다.",
                    System.Windows.Forms.ToolTipIcon.Info);

            try
            {
                var snapshot = _mappings.ToList();
                foreach (var mapping in snapshot)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    Log($"--- [{mapping.TypeLabel}] {mapping.Source}  →  {mapping.Dest}");

                    if (mapping.Type == MappingType.Folder)
                        await FileCopyService.CopyAsync(mapping.Source, mapping.Dest, Log, _cts.Token);
                    else
                        await FileCopyService.CopyFileToFolderAsync(mapping.Source, mapping.Dest, Log, _cts.Token);
                }

                TxtStatus.Text = $"업데이트 완료  ({DateTime.Now:HH:mm:ss})";
                Log("========== 업데이트 완료 ==========");

                if (!IsVisible)
                    _trayIcon.ShowBalloonTip(4000, "Update Planner", "업데이트가 완료되었습니다.",
                        System.Windows.Forms.ToolTipIcon.Info);
                else
                    MessageBox.Show("업데이트가 완료되었습니다.", "완료",
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "업데이트 취소됨";
                Log("업데이트가 취소되었습니다.");
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "업데이트 실패";
                Log($"오류: {ex.Message}");

                if (!IsVisible)
                    _trayIcon.ShowBalloonTip(4000, "Update Planner - 오류", ex.Message,
                        System.Windows.Forms.ToolTipIcon.Error);
                else
                    MessageBox.Show($"업데이트 중 오류가 발생했습니다.\n\n{ex.Message}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 반복 예약이면 다음 실행 시각 계산
                if (_scheduleConfig != null && _scheduleConfig.Repeat != RepeatMode.None)
                {
                    _nextFireTime = _scheduleConfig.GetNextFireTime(DateTime.Now);
                    if (_nextFireTime.HasValue)
                    {
                        SetScheduledState(true);
                        _countdownTimer.Start();
                        Log($"다음 예약: {_nextFireTime:yyyy-MM-dd HH:mm:ss}");
                    }
                    else
                    {
                        // 더 이상 반복 없음
                        _scheduleConfig = null;
                        _nextFireTime   = null;
                        _trayIcon.Text  = "Update Planner";
                        SetRunningState(false);
                        Log("모든 예약 실행이 완료되었습니다.");
                    }
                }
                else
                {
                    _nextFireTime  = null;
                    _trayIcon.Text = "Update Planner";
                    SetRunningState(false);
                }
            }
        }

        // ─── UI 상태 관리 ────────────────────────────────────────────────────

        private void SetScheduledState(bool scheduled)
        {
            BtnStart.IsEnabled         = !scheduled;
            BtnCancelSchedule.IsEnabled = scheduled;
            BtnRunNow.IsEnabled        = !scheduled;

            if (scheduled)
                TxtStatus.Text = $"예약됨 → {_nextFireTime:yyyy-MM-dd HH:mm:ss}";
        }

        private void SetRunningState(bool running)
        {
            _isRunning                  = running;
            BtnStart.IsEnabled          = !running;
            BtnCancelSchedule.IsEnabled  = running;
            BtnRunNow.IsEnabled         = !running;
        }

        // ─── 설정 저장/불러오기 ──────────────────────────────────────────────

        private void SaveSettings()
        {
            try
            {
                var doc  = new XmlDocument();
                var root = doc.CreateElement("Settings");
                doc.AppendChild(root);

                // 매핑 목록
                var mappingsEl = doc.CreateElement("Mappings");
                root.AppendChild(mappingsEl);
                foreach (var m in _mappings)
                {
                    var mEl = doc.CreateElement("Mapping");
                    AppendNode(doc, mEl, "Type",   m.Type.ToString());
                    AppendNode(doc, mEl, "Source", m.Source);
                    AppendNode(doc, mEl, "Dest",   m.Dest);
                    mappingsEl.AppendChild(mEl);
                }

                // 예약 설정
                var schedEl = doc.CreateElement("Schedule");
                root.AppendChild(schedEl);
                if (_scheduleConfig != null)
                {
                    AppendNode(doc, schedEl, "StartDate",  _scheduleConfig.StartDateTime.ToString("yyyy-MM-dd"));
                    AppendNode(doc, schedEl, "Hour",       _scheduleConfig.StartDateTime.Hour.ToString("D2"));
                    AppendNode(doc, schedEl, "Minute",     _scheduleConfig.StartDateTime.Minute.ToString("D2"));
                    AppendNode(doc, schedEl, "EndDate",    _scheduleConfig.EndDateTime?.ToString("yyyy-MM-dd") ?? "");
                    AppendNode(doc, schedEl, "Repeat",     _scheduleConfig.Repeat.ToString());
                    AppendNode(doc, schedEl, "WeekDays",   string.Join(",", _scheduleConfig.WeekDays.ConvertAll(d => ((int)d).ToString())));
                    AppendNode(doc, schedEl, "MonthDays",  string.Join(",", _scheduleConfig.MonthDays));
                }

                // 기타
                AppendNode(doc, root, "RetentionDays", TxtRetentionDays.Text);

                doc.Save(ConfigFile);
            }
            catch (Exception ex)
            {
                Log($"설정 저장 실패: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            if (!File.Exists(ConfigFile)) return;
            try
            {
                var doc = new XmlDocument();
                doc.Load(ConfigFile);

                // 매핑 목록
                var mappingNodes = doc.SelectNodes("//Mappings/Mapping");
                if (mappingNodes != null)
                {
                    foreach (XmlNode n in mappingNodes)
                    {
                        string typeStr = n.SelectSingleNode("Type")?.InnerText   ?? "Folder";
                        string src     = n.SelectSingleNode("Source")?.InnerText ?? "";
                        string dest    = n.SelectSingleNode("Dest")?.InnerText   ?? "";
                        if (!string.IsNullOrWhiteSpace(src))
                        {
                            Enum.TryParse(typeStr, out MappingType t);
                            _mappings.Add(new DeployMapping { Type = t, Source = src, Dest = dest });
                        }
                    }
                }

                // 예약 설정
                var schedNode = doc.SelectSingleNode("//Schedule");
                if (schedNode != null)
                {
                    string startDateStr = schedNode.SelectSingleNode("StartDate")?.InnerText ?? "";
                    string hourStr      = schedNode.SelectSingleNode("Hour")?.InnerText      ?? "00";
                    string minuteStr    = schedNode.SelectSingleNode("Minute")?.InnerText    ?? "00";
                    string endDateStr   = schedNode.SelectSingleNode("EndDate")?.InnerText   ?? "";
                    string repeatStr    = schedNode.SelectSingleNode("Repeat")?.InnerText    ?? "None";
                    string weekDaysStr  = schedNode.SelectSingleNode("WeekDays")?.InnerText  ?? "";
                    string monthDaysStr = schedNode.SelectSingleNode("MonthDays")?.InnerText ?? "";

                    if (DateTime.TryParse(startDateStr, out DateTime startDate) &&
                        int.TryParse(hourStr, out int h) &&
                        int.TryParse(minuteStr, out int m))
                    {
                        var cfg = new ScheduleConfig
                        {
                            StartDateTime = startDate.Date.AddHours(h).AddMinutes(m)
                        };

                        if (DateTime.TryParse(endDateStr, out DateTime endDate))
                            cfg.EndDateTime = endDate.Date.AddHours(23).AddMinutes(59);

                        if (Enum.TryParse(repeatStr, out RepeatMode repeat))
                            cfg.Repeat = repeat;

                        foreach (var part in weekDaysStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            if (int.TryParse(part, out int dow))
                                cfg.WeekDays.Add((DayOfWeek)dow);

                        foreach (var part in monthDaysStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            if (int.TryParse(part, out int day))
                                cfg.MonthDays.Add(day);

                        _scheduleConfig = cfg;
                        TxtScheduleSummary.Text = cfg.GetSummary();
                        TxtScheduleSummary.Foreground = System.Windows.Media.Brushes.Black;
                    }
                }

                // 기타
                TxtRetentionDays.Text = doc.SelectSingleNode("//RetentionDays")?.InnerText ?? "3";
            }
            catch { }
        }

        private static void AppendNode(XmlDocument doc, XmlElement parent, string name, string value)
        {
            var el = doc.CreateElement(name);
            el.InnerText = value ?? "";
            parent.AppendChild(el);
        }

        // ─── 임시저장 / 초기화 ───────────────────────────────────────────────

        private void BtnSaveTemp_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            TxtSaveFeedback.Text = $"✓ 저장됨  ({DateTime.Now:HH:mm:ss})";
            TxtSaveFeedback.Foreground = System.Windows.Media.Brushes.SeaGreen;
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            string message =
                "아래 설정이 모두 초기화됩니다.\n\n" +
                "  ● 경로 설정  —  추가된 배포 경로 매핑 전체 (폴더·파일 모두)\n" +
                "  ● 예약 설정  —  상세 설정한 일시·반복 주기\n" +
                "  ● 저장 파일  —  settings.xml 삭제\n\n" +
                "※ 현재 진행 중인 예약이 있으면 자동으로 취소됩니다.\n" +
                "※ 지금까지 생성된 로그 파일은 삭제되지 않습니다.\n\n" +
                "초기화하시겠습니까?";

            var confirm = MessageBox.Show(message, "초기화 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes) return;

            // 진행 중 예약 취소
            if (_nextFireTime.HasValue)
                BtnCancelSchedule_Click(null, null);

            // 매핑 목록 초기화
            _mappings.Clear();

            // 예약 설정 초기화
            _scheduleConfig = null;
            TxtScheduleSummary.Text       = "예약 설정 없음  —  [상세 설정]을 눌러 일시/주기를 구성하세요.";
            TxtScheduleSummary.Foreground = System.Windows.Media.Brushes.DarkGray;

            // 저장 파일 삭제
            try
            {
                if (File.Exists(ConfigFile))
                    File.Delete(ConfigFile);
            }
            catch (Exception ex)
            {
                Log($"설정 파일 삭제 실패: {ex.Message}");
            }

            TxtStatus.Text        = "초기화 완료";
            TxtSaveFeedback.Text      = "";
            Log("설정이 초기화되었습니다.");
        }

        // ─── 로그 ────────────────────────────────────────────────────────────

        private void Log(string message)
        {
            _logService?.Write(GetLogTag(), message);
        }

        private string GetLogTag()
        {
            if (_mappings.Count == 0) return "general";
            if (_mappings.Count == 1)
            {
                string src = _mappings[0].Source?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? "";
                return string.IsNullOrEmpty(src) ? "general" : Path.GetFileName(src);
            }
            return "multi";
        }
    }
}
