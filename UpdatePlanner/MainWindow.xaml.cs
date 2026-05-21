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

        private const string ConfigFile = "settings.xml";

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            DateSchedule.SelectedDate = DateTime.Today;

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += CountdownTimer_Tick;
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
                _ = RunUpdateAsync();
            }
            else
            {
                TxtCountdown.Text = $"남은 시간: {(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}  " +
                                    $"(실행 예정: {_scheduledTime.Value:yyyy-MM-dd HH:mm:ss})";
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

            Log($"업데이트 예약 완료 → {_scheduledTime:yyyy-MM-dd HH:mm:ss}");
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _countdownTimer.Stop();
            _cts?.Cancel();
            _scheduledTime = null;
            SetScheduledState(false);
            TxtStatus.Text = "예약 취소됨";
            TxtCountdown.Text = "";
            Log("예약이 취소되었습니다.");
        }

        private async void BtnRunNow_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;

            var result = MessageBox.Show("지금 즉시 FTP 업데이트를 실행하겠습니까?", "즉시 실행",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            SaveSettings();
            await RunUpdateAsync();
        }

        private async void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtHost.Text))
            {
                MessageBox.Show("FTP 호스트를 입력하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TxtPort.Text.Trim(), out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("올바른 포트 번호를 입력하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnTest.IsEnabled = false;
            BtnTest.Content = "테스트 중...";
            Log("FTP 연결 테스트 중...");

            try
            {
                var svc = new FtpService(TxtHost.Text.Trim(), port,
                    TxtUsername.Text.Trim(), TxtPassword.Password);

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                await svc.TestConnectionAsync(cts.Token);

                Log("연결 테스트 성공!");
                MessageBox.Show("FTP 서버 연결에 성공했습니다.", "연결 성공",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"연결 테스트 실패: {ex.Message}");
                MessageBox.Show($"FTP 서버 연결에 실패했습니다.\n\n{ex.Message}", "연결 실패",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnTest.IsEnabled = true;
                BtnTest.Content = "연결 테스트";
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "업데이트 파일을 저장할 폴더를 선택하세요.",
                ShowNewFolderButton = true
            };

            if (!string.IsNullOrEmpty(TxtLocalPath.Text) && Directory.Exists(TxtLocalPath.Text))
                dialog.SelectedPath = TxtLocalPath.Text;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtLocalPath.Text = dialog.SelectedPath;
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isRunning)
            {
                var result = MessageBox.Show("업데이트가 실행 중입니다. 종료하시겠습니까?", "종료 확인",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
                _cts?.Cancel();
            }

            if (_scheduledTime.HasValue)
            {
                var result = MessageBox.Show(
                    $"예약된 업데이트({_scheduledTime:yyyy-MM-dd HH:mm:ss})가 있습니다.\n종료하면 예약이 취소됩니다. 계속하시겠습니까?",
                    "종료 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                    e.Cancel = true;
            }
        }

        // ─── 업데이트 실행 ───────────────────────────────────────────────────

        private async System.Threading.Tasks.Task RunUpdateAsync()
        {
            SetRunningState(true);
            TxtStatus.Text = "업데이트 실행 중...";
            Log("========== 업데이트 시작 ==========");
            Log($"FTP: {TxtHost.Text}:{TxtPort.Text} → {TxtFtpPath.Text}");
            Log($"로컬: {TxtLocalPath.Text}");

            _cts = new CancellationTokenSource();

            try
            {
                if (!int.TryParse(TxtPort.Text.Trim(), out int port))
                    port = 21;

                var svc = new FtpService(
                    TxtHost.Text.Trim(),
                    port,
                    TxtUsername.Text.Trim(),
                    TxtPassword.Password);

                await svc.DownloadAsync(
                    TxtFtpPath.Text.Trim(),
                    TxtLocalPath.Text.Trim(),
                    Log,
                    _cts.Token);

                TxtStatus.Text = $"업데이트 완료  ({DateTime.Now:HH:mm:ss})";
                Log("========== 업데이트 완료 ==========");
                MessageBox.Show("FTP 업데이트가 완료되었습니다.", "완료",
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
                MessageBox.Show($"업데이트 중 오류가 발생했습니다.\n\n{ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetRunningState(false);
                _scheduledTime = null;
            }
        }

        // ─── UI 상태 관리 ─────────────────────────────────────────────────────

        private void SetScheduledState(bool scheduled)
        {
            BtnStart.IsEnabled = !scheduled;
            BtnCancel.IsEnabled = scheduled;
            BtnRunNow.IsEnabled = !scheduled;
            BtnTest.IsEnabled = !scheduled;

            if (scheduled)
                TxtStatus.Text = $"예약됨 → {_scheduledTime:yyyy-MM-dd HH:mm:ss}";
        }

        private void SetRunningState(bool running)
        {
            _isRunning = running;
            BtnStart.IsEnabled = !running;
            BtnCancel.IsEnabled = running;
            BtnRunNow.IsEnabled = !running;
            BtnTest.IsEnabled = !running;
        }

        // ─── 입력 유효성 검사 ────────────────────────────────────────────────

        private bool ValidateInputs()
        {
            if (string.IsNullOrWhiteSpace(TxtHost.Text))
            {
                MessageBox.Show("FTP 호스트를 입력하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!int.TryParse(TxtPort.Text.Trim(), out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("올바른 포트 번호를 입력하세요. (1~65535)", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtFtpPath.Text))
            {
                MessageBox.Show("FTP 경로를 입력하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(TxtLocalPath.Text))
            {
                MessageBox.Show("대상 로컬 폴더를 선택하세요.", "입력 오류",
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

            string timeText = TxtTime.Text.Trim();
            if (!TimeSpan.TryParseExact(timeText, new[] { @"hh\:mm", @"h\:mm" }, null, out TimeSpan time))
            {
                MessageBox.Show("시간을 HH:mm 형식으로 입력하세요. (예: 03:00, 23:30)", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            result = DateSchedule.SelectedDate.Value.Date + time;
            return true;
        }

        // ─── 설정 저장/불러오기 ──────────────────────────────────────────────

        private void SaveSettings()
        {
            try
            {
                var doc = new XmlDocument();
                XmlElement root = doc.CreateElement("Settings");
                doc.AppendChild(root);

                AppendNode(doc, root, "Host", TxtHost.Text);
                AppendNode(doc, root, "Port", TxtPort.Text);
                AppendNode(doc, root, "Username", TxtUsername.Text);
                AppendNode(doc, root, "FtpPath", TxtFtpPath.Text);
                AppendNode(doc, root, "LocalPath", TxtLocalPath.Text);

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

                TxtHost.Text = doc.SelectSingleNode("//Host")?.InnerText ?? "";
                TxtPort.Text = doc.SelectSingleNode("//Port")?.InnerText ?? "21";
                TxtUsername.Text = doc.SelectSingleNode("//Username")?.InnerText ?? "";
                TxtFtpPath.Text = doc.SelectSingleNode("//FtpPath")?.InnerText ?? "";
                TxtLocalPath.Text = doc.SelectSingleNode("//LocalPath")?.InnerText ?? "";
            }
            catch (Exception ex)
            {
                Log($"설정 불러오기 실패: {ex.Message}");
            }
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
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Log(message));
                return;
            }

            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            TxtLog.ScrollToEnd();
        }
    }
}
