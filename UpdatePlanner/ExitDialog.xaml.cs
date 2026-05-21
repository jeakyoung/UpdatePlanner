using System.Windows;

namespace UpdatePlanner
{
    public enum ExitResult { Cancel, Background, Exit }

    public partial class ExitDialog : Window
    {
        public ExitResult Result { get; private set; } = ExitResult.Cancel;

        private readonly bool _hasSchedule;

        public ExitDialog(bool hasSchedule)
        {
            InitializeComponent();
            _hasSchedule = hasSchedule;
        }

        private void BtnBackground_Click(object sender, RoutedEventArgs e)
        {
            Result = ExitResult.Background;
            Close();
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            if (_hasSchedule)
            {
                TxtMessage.Text = "등록된 예약이 존재하여 완전종료가 불가능합니다.\n백그라운드 시행을 선택하거나 예약을 취소한 후 종료하세요.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Crimson;
                BtnExit.IsEnabled = false;
                return;
            }

            Result = ExitResult.Exit;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = ExitResult.Cancel;
            Close();
        }
    }
}
