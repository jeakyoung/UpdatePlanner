using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Xml;

namespace UpdatePlanner
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _countdownTimer;
        private DateTime? _scheduledTime;
        private CancellationTokenSource _cts;
        private bool _isRunning;
        private bool _forceExit;

        private System.Windows.Forms.NotifyIcon _trayIcon;
        private LogService _logService;

        private const string ConfigFile = "settings.xml";

        public MainWindow()
        {
            InitializeComponent();
            InitializeTrayIcon();
            LoadSettings();
            DateSchedule.SelectedDate = DateTime.Today;

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

            _trayIcon.Text = "Update Planner";
            _trayIcon.Visible = false;
            _trayIcon.DoubleClick += (s, e) => RestoreWindow();

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("열기", null, (s, e) => RestoreWindow());
            menu.Items.Add("-");
            menu.Items.Add("예약 취소", null, (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_scheduledTime.HasValue)
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

            string tip = _scheduledTime.HasValue
                ? $"백그라운드 실행 중\n예약: {_scheduledTime:yyyy-MM-dd HH:mm:ss}"
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
            if (_scheduledTime.HasValue)
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

        // ─── 창 닫기 (X 버튼) ───────────────────────────────────────────────

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_forceExit)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;

            var dialog = new ExitDialog(_scheduledTime.HasValue) { Owner = this };
            dialog.ShowDialog();

            switch (dialog.Result)
            {
                case ExitResult.Background:
                    MinimizeToTray();
                    break;

                case ExitResult.Exit:
                    DoExit();
                    break;

                // ExitResult.Cancel → 아무것도 안 함
            }
        }

        // ─── 카운트다운 타이머 ───────────────────────────────────────────────

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            if (!_scheduledTime.HasValue) return;

            TimeSpan remaining = _scheduledTime.Value - DateTime.Now;

            if (remaining <= TimeSpan.Zero)
            {
                _countdownTimer.Stop();
                TxtCountdown.Text = "";
                _ = RunCopyAsync();
            }
            else
            {
                TxtCountdown.Text = $"남은 시간: {(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}" +
                                    $"  (실행 예정: {_scheduledTime.Value:yyyy-MM-dd HH:mm:ss})";

                // 트레이 툴팁 동기화
                if (!IsVisible)
                    _trayIcon.Text = $"예약: {_scheduledTime:HH:mm:ss} | 남은 {(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
            }
        }

        // ─── 버튼 이벤트 ─────────────────────────────────────────────────────

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;
            if (!TryParseScheduledTime(out DateTime scheduledTime)) return;

            if (scheduledTime <= DateTime.Now)
            {
                MessageBox.Show("예약 시간이 현재 시간보다 이후여야 합니다.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _scheduledTime = scheduledTime;
            SaveSettings();
            SetScheduledState(true);
            _countdownTimer.Start();
            Log($"예약 완료 → {_scheduledTime:yyyy-MM-dd HH:mm:ss}");
            Log($"  소스: {TxtSource.Text}");
            Log($"  대상: {TxtDest.Text}");
        }

        private void BtnCancelSchedule_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _cts?.Cancel();
            _scheduledTime = null;
            SetScheduledState(false);
            TxtStatus.Text = "예약 취소됨";
            TxtCountdown.Text = "";
            _trayIcon.Text = "Update Planner";
            Log("예약이 취소되었습니다.");
        }

        private async void BtnRunNow_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;

            var result = MessageBox.Show("지금 즉시 업데이트를 실행하겠습니까?", "즉시 실행",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            SaveSettings();
            await RunCopyAsync();
        }

        private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "하위 파일 전체를 복사할 소스 폴더를 선택하세요.",
                ShowNewFolderButton = false
            };
            if (!string.IsNullOrEmpty(TxtSource.Text) && Directory.Exists(TxtSource.Text))
                dialog.SelectedPath = TxtSource.Text;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtSource.Text = dialog.SelectedPath;
        }

        private void BtnBrowseDest_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "배포할 대상 폴더를 선택하세요.",
                ShowNewFolderButton = true
            };
            if (!string.IsNullOrEmpty(TxtDest.Text))
                dialog.SelectedPath = TxtDest.Text;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtDest.Text = dialog.SelectedPath;
        }

        // ─── 복사 실행 ───────────────────────────────────────────────────────

        private async System.Threading.Tasks.Task RunCopyAsync()
        {
            SetRunningState(true);
            TxtStatus.Text = "업데이트 실행 중...";
            Log("========== 업데이트 시작 ==========");
            Log($"소스: {TxtSource.Text}");
            Log($"대상: {TxtDest.Text}");

            _cts = new CancellationTokenSource();

            // 트레이에서 실행 중이면 알림
            if (!IsVisible)
                _trayIcon.ShowBalloonTip(2000, "Update Planner", "업데이트를 시작합니다.",
                    System.Windows.Forms.ToolTipIcon.Info);

            try
            {
                await FileCopyService.CopyAsync(
                    TxtSource.Text.Trim(),
                    TxtDest.Text.Trim(),
                    Log,
                    _cts.Token);

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
                _scheduledTime = null;
                _trayIcon.Text = "Update Planner";
                SetRunningState(false);
            }
        }

        // ─── UI 상태 관리 ─────────────────────────────────────────────────────

        private void SetScheduledState(bool scheduled)
        {
            BtnStart.IsEnabled = !scheduled;
            BtnCancelSchedule.IsEnabled = scheduled;
            BtnRunNow.IsEnabled = !scheduled;

            if (scheduled)
                TxtStatus.Text = $"예약됨 → {_scheduledTime:yyyy-MM-dd HH:mm:ss}";
        }

        private void SetRunningState(bool running)
        {
            _isRunning = running;
            BtnStart.IsEnabled = !running;
            BtnCancelSchedule.IsEnabled = running;
            BtnRunNow.IsEnabled = !running;
        }

        // ─── 입력 유효성 검사 ────────────────────────────────────────────────

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(TxtSource.Text))
            {
                MessageBox.Show("업데이트 폴더 경로를 입력하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!Directory.Exists(TxtSource.Text.Trim()))
            {
                MessageBox.Show("업데이트 폴더 경로가 존재하지 않습니다.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtDest.Text))
            {
                MessageBox.Show("배포 위치를 입력하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private bool TryParseScheduledTime(out DateTime result)
        {
            result = DateTime.MinValue;

            if (!DateSchedule.SelectedDate.HasValue)
            {
                MessageBox.Show("예약 날짜를 선택하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            result = DateSchedule.SelectedDate.Value.Date
                     .AddHours(Hour)
                     .AddMinutes(Minute);
            return true;
        }

        // ─── 시간 입력 ───────────────────────────────────────────────────────

        private int Hour   => int.TryParse(TxtHour.Text,   out int h) ? Math.Max(0, Math.Min(23, h)) : 0;
        private int Minute => int.TryParse(TxtMinute.Text, out int m) ? Math.Max(0, Math.Min(59, m)) : 0;

        private void TimeBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !System.Text.RegularExpressions.Regex.IsMatch(e.Text, @"^\d$");
        }

        private void TxtHour_LostFocus(object sender, RoutedEventArgs e)
            => TxtHour.Text = Hour.ToString("D2");

        private void TxtMinute_LostFocus(object sender, RoutedEventArgs e)
            => TxtMinute.Text = Minute.ToString("D2");

        // ─── 설정 저장/불러오기 ──────────────────────────────────────────────

        private void SaveSettings()
        {
            try
            {
                var doc = new XmlDocument();
                XmlElement root = doc.CreateElement("Settings");
                doc.AppendChild(root);
                AppendNode(doc, root, "Source",        TxtSource.Text);
                AppendNode(doc, root, "Dest",          TxtDest.Text);
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
                TxtSource.Text        = doc.SelectSingleNode("//Source")?.InnerText        ?? "";
                TxtDest.Text          = doc.SelectSingleNode("//Dest")?.InnerText          ?? "";
                TxtRetentionDays.Text = doc.SelectSingleNode("//RetentionDays")?.InnerText ?? "30";
            }
            catch { }
        }

        private static void AppendNode(XmlDocument doc, XmlElement parent, string name, string value)
        {
            XmlElement el = doc.CreateElement(name);
            el.InnerText = value ?? "";
            parent.AppendChild(el);
        }

        // ─── 로그 ────────────────────────────────────────────────────────────

        private void Log(string message)
        {
            _logService?.Write(GetSourceFolderName(), message);
        }

        private string GetSourceFolderName()
        {
            string src = TxtSource?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(src)) return "general";
            return Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
    }
}
