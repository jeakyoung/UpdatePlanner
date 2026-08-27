using System.IO;
using System.Windows;

namespace UpdatePlanner
{
    public partial class AddMappingDialog : Window
    {
        private readonly MappingType _type;
        public DeployMapping Result { get; private set; }

        public AddMappingDialog(MappingType type)
        {
            _type = type;
            InitializeComponent();

            if (type == MappingType.Folder)
            {
                Title              = "배포 폴더 추가";
                LblSource.Content  = "업로드 폴더:";
                TxtSource.ToolTip  = "하위 파일 전체를 배포할 소스 폴더";
            }
            else
            {
                Title              = "배포 파일 추가";
                LblSource.Content  = "업로드 파일:";
                TxtSource.ToolTip  = "배포할 파일 경로";
            }
        }

        // ─── 소스 찾아보기 ────────────────────────────────────────────────────

        private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
        {
            if (_type == MappingType.Folder)
            {
                var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "업로드할 폴더를 선택하세요.",
                    ShowNewFolderButton = false
                };
                if (Directory.Exists(TxtSource.Text))
                    dlg.SelectedPath = TxtSource.Text;

                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    TxtSource.Text = dlg.SelectedPath;
            }
            else
            {
                var dlg = new System.Windows.Forms.OpenFileDialog
                {
                    Title  = "배포할 파일을 선택하세요.",
                    Filter = "모든 파일 (*.*)|*.*"
                };
                if (File.Exists(TxtSource.Text))
                    dlg.InitialDirectory = Path.GetDirectoryName(TxtSource.Text);

                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    TxtSource.Text = dlg.FileName;
            }
        }

        // ─── 배포 위치 찾아보기 ──────────────────────────────────────────────

        private void BtnBrowseDest_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description     = "배포할 대상 폴더를 선택하세요.",
                ShowNewFolderButton = true
            };
            if (Directory.Exists(TxtDest.Text))
                dlg.SelectedPath = TxtDest.Text;

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtDest.Text = dlg.SelectedPath;
        }

        // ─── 확인 ────────────────────────────────────────────────────────────

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string src  = TxtSource.Text.Trim();
            string dest = TxtDest.Text.Trim();

            if (string.IsNullOrWhiteSpace(src))
            {
                string label = _type == MappingType.Folder ? "업로드 폴더" : "업로드 파일";
                MessageBox.Show($"{label} 경로를 입력하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_type == MappingType.Folder && !Directory.Exists(src))
            {
                MessageBox.Show("업로드 폴더가 존재하지 않습니다.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_type == MappingType.File && !File.Exists(src))
            {
                MessageBox.Show("업로드 파일이 존재하지 않습니다.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(dest))
            {
                MessageBox.Show("배포 위치를 입력하세요.", "입력 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new DeployMapping { Type = _type, Source = src, Dest = dest };
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}
