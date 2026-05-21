using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UpdatePlanner
{
    public static class FileCopyService
    {
        /// <summary>
        /// sourcePath의 모든 파일/폴더를 destPath로 복사합니다. (덮어쓰기)
        /// </summary>
        /// <summary>
        /// sourcePath 폴더의 하위 파일/폴더를 전부 destPath로 복사합니다. (덮어쓰기)
        /// 소스 폴더 자체는 생성되지 않고 내용물만 대상에 복사됩니다.
        /// </summary>
        public static async Task CopyAsync(string sourcePath, string destPath, Action<string> log, CancellationToken ct)
        {
            if (!Directory.Exists(sourcePath))
                throw new DirectoryNotFoundException($"소스 폴더를 찾을 수 없습니다: {sourcePath}");

            await CopyDirectoryAsync(sourcePath, destPath, log, ct);
        }

        private static async Task CopyDirectoryAsync(string sourceDir, string destDir, Action<string> log, CancellationToken ct)
        {
            Directory.CreateDirectory(destDir);

            // 파일 복사
            foreach (string srcFile in Directory.GetFiles(sourceDir))
            {
                ct.ThrowIfCancellationRequested();

                string fileName = Path.GetFileName(srcFile);
                string destFile = Path.Combine(destDir, fileName);

                log($"복사: {MakeRelative(srcFile, sourceDir)}");
                await CopyFileAsync(srcFile, destFile, ct);
                log($"완료: {fileName}");
            }

            // 하위 폴더 복사
            foreach (string srcSubDir in Directory.GetDirectories(sourceDir))
            {
                ct.ThrowIfCancellationRequested();

                string dirName  = Path.GetFileName(srcSubDir);
                string destSub  = Path.Combine(destDir, dirName);

                log($"[폴더] {dirName}");
                await CopyDirectoryAsync(srcSubDir, destSub, log, ct);
            }
        }

        private static async Task CopyFileAsync(string source, string dest, CancellationToken ct)
        {
            using (var srcStream  = new FileStream(source, FileMode.Open,   FileAccess.Read,  FileShare.Read,  81920, true))
            using (var destStream = new FileStream(dest,   FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await srcStream.CopyToAsync(destStream, 81920, ct);
            }
        }

        private static string MakeRelative(string fullPath, string basePath)
        {
            return fullPath.StartsWith(basePath)
                ? fullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar)
                : Path.GetFileName(fullPath);
        }
    }
}
