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
        public static async Task CopyAsync(string sourcePath, string destPath, Action<string> log, CancellationToken ct)
        {
            bool isFile = File.Exists(sourcePath);
            bool isDir  = Directory.Exists(sourcePath);

            if (!isFile && !isDir)
                throw new FileNotFoundException($"소스 경로를 찾을 수 없습니다: {sourcePath}");

            if (isFile)
            {
                Directory.CreateDirectory(destPath);
                string dest = Path.Combine(destPath, Path.GetFileName(sourcePath));
                log($"복사: {Path.GetFileName(sourcePath)}");
                await CopyFileAsync(sourcePath, dest, ct);
                log($"완료: {Path.GetFileName(sourcePath)}");
            }
            else
            {
                await CopyDirectoryAsync(sourcePath, destPath, log, ct);
            }
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

            // 하위 폴더 재귀 복사
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
