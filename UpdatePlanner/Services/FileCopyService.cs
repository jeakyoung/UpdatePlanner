using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UpdatePlanner
{
    public static class FileCopyService
    {
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

        /// <summary>
        /// 파일 한 개를 destFolder 안으로 복사합니다. (덮어쓰기)
        /// </summary>
        public static async Task CopyFileToFolderAsync(string sourceFile, string destFolder, Action<string> log, CancellationToken ct)
        {
            if (!File.Exists(sourceFile))
                throw new FileNotFoundException($"파일을 찾을 수 없습니다: {sourceFile}");

            Directory.CreateDirectory(destFolder);

            string fileName = Path.GetFileName(sourceFile);
            string destFile = Path.Combine(destFolder, fileName);

            ct.ThrowIfCancellationRequested();
            log($"복사: {fileName}");
            await CopyFileAsync(sourceFile, destFile, ct);
            log($"완료: {fileName}");
        }

        private static string MakeRelative(string fullPath, string basePath)
        {
            return fullPath.StartsWith(basePath)
                ? fullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar)
                : Path.GetFileName(fullPath);
        }

        // ─── 롤백용 백업 / 복원 ───────────────────────────────────────────────

        /// <summary>
        /// 복사 실행 전 dest 현재 상태를 backupPath로 백업합니다.
        /// 백업할 내용이 있으면 true, 없으면 false를 반환합니다.
        /// </summary>
        public static async Task<bool> BackupDestAsync(
            DeployMapping mapping, string backupPath, CancellationToken ct)
        {
            if (mapping.Type == MappingType.Folder)
            {
                if (!Directory.Exists(mapping.Dest)) return false;
                await CopyDirectoryAsync(mapping.Dest, backupPath, _ => { }, ct);
                return true;
            }
            else
            {
                string destFile = Path.Combine(mapping.Dest, Path.GetFileName(mapping.Source));
                if (!File.Exists(destFile)) return false;
                Directory.CreateDirectory(backupPath);
                await CopyFileAsync(destFile,
                    Path.Combine(backupPath, Path.GetFileName(mapping.Source)), ct);
                return true;
            }
        }

        /// <summary>
        /// backupPath에 저장된 내용을 원래 dest로 복원합니다.
        /// 복원 중 취소는 허용하지 않습니다(CancellationToken.None 사용).
        /// </summary>
        public static async Task RestoreDestAsync(
            DeployMapping mapping, string backupPath, bool hadContent, Action<string> log)
        {
            try
            {
                if (mapping.Type == MappingType.Folder)
                {
                    if (Directory.Exists(mapping.Dest))
                        Directory.Delete(mapping.Dest, true);

                    if (hadContent && Directory.Exists(backupPath))
                        await CopyDirectoryAsync(backupPath, mapping.Dest, _ => { }, CancellationToken.None);
                }
                else
                {
                    string destFile = Path.Combine(mapping.Dest, Path.GetFileName(mapping.Source));
                    if (File.Exists(destFile))
                        File.Delete(destFile);

                    if (hadContent)
                    {
                        string backupFile = Path.Combine(backupPath, Path.GetFileName(mapping.Source));
                        if (File.Exists(backupFile))
                        {
                            Directory.CreateDirectory(mapping.Dest);
                            await CopyFileAsync(backupFile, destFile, CancellationToken.None);
                        }
                    }
                }
                log($"  롤백 완료: {mapping.Dest}");
            }
            catch (Exception ex)
            {
                log($"  롤백 실패 [{mapping.Dest}]: {ex.Message}");
            }
        }
    }
}
