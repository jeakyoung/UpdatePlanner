using System;
using System.IO;
using System.Text;

namespace UpdatePlanner
{
    public class LogService
    {
        private readonly string _logDir;

        public LogService()
        {
            _logDir = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "logs");
            Directory.CreateDirectory(_logDir);
        }

        /// <summary>
        /// 로그를 파일에 기록합니다.
        /// 파일명: log_YYYY-MM-DD_[sourceFolderName].txt
        /// </summary>
        public void Write(string sourceFolderName, string message)
        {
            try
            {
                string safeFolder = string.IsNullOrWhiteSpace(sourceFolderName) ? "general" : sourceFolderName;
                string fileName   = $"log_{DateTime.Now:yyyy-MM-dd}_{safeFolder}.txt";
                string filePath   = Path.Combine(_logDir, fileName);
                string line       = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";

                File.AppendAllText(filePath, line, Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// retentionDays 일보다 오래된 로그 파일을 삭제합니다.
        /// </summary>
        public void Cleanup(int retentionDays)
        {
            if (retentionDays <= 0) return;

            DateTime cutoff = DateTime.Today.AddDays(-retentionDays);

            foreach (string filePath in Directory.GetFiles(_logDir, "log_*.txt"))
            {
                try
                {
                    // 파일명에서 날짜 파싱: log_2026-05-21_folderName.txt
                    string name  = Path.GetFileNameWithoutExtension(filePath); // log_2026-05-21_folderName
                    int firstUs  = name.IndexOf('_');                          // log 뒤 첫 번째 _
                    int secondUs = name.IndexOf('_', firstUs + 1);             // 날짜 뒤 두 번째 _

                    string datePart = secondUs > firstUs
                        ? name.Substring(firstUs + 1, secondUs - firstUs - 1)
                        : name.Substring(firstUs + 1);

                    if (DateTime.TryParse(datePart, out DateTime fileDate) && fileDate < cutoff)
                        File.Delete(filePath);
                }
                catch { }
            }
        }

        public string LogDirectory => _logDir;
    }
}
