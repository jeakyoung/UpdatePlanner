using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace UpdatePlanner
{
    public class FtpService
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;

        public FtpService(string host, int port, string username, string password)
        {
            _host = host;
            _port = port;
            _username = username;
            _password = password;
        }

        private string BuildUri(string remotePath)
        {
            string path = remotePath.Replace('\\', '/').TrimStart('/');
            return $"ftp://{_host}:{_port}/{path}";
        }

        private FtpWebRequest CreateRequest(string uri, string method)
        {
            var req = (FtpWebRequest)WebRequest.Create(uri);
            req.Method = method;
            req.Credentials = new NetworkCredential(_username, _password);
            req.UsePassive = true;
            req.UseBinary = true;
            req.KeepAlive = false;
            req.Timeout = 30000;
            return req;
        }

        public async Task TestConnectionAsync(CancellationToken ct)
        {
            var uri = BuildUri("/");
            var req = CreateRequest(uri, WebRequestMethods.Ftp.ListDirectory);
            using (ct.Register(() => req.Abort()))
            using (var response = (FtpWebResponse)await Task.Factory.FromAsync(req.BeginGetResponse, req.EndGetResponse, null))
            {
                // Connection successful if we get here
            }
        }

        public async Task DownloadAsync(string remotePath, string localPath, Action<string> log, CancellationToken ct)
        {
            bool isDir = await IsDirectoryAsync(remotePath, ct);

            if (isDir)
            {
                log($"[폴더] {remotePath}");
                await DownloadDirectoryAsync(remotePath, localPath, log, ct);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? localPath);
                string fileName = Path.GetFileName(remotePath);
                string destFile = Path.Combine(localPath, fileName);
                if (File.Exists(destFile))
                    destFile = localPath; // localPath was meant as the full file path

                log($"[파일] {fileName}");
                await DownloadFileAsync(remotePath, localPath, ct);
                log($"[완료] {fileName}");
            }
        }

        private async Task DownloadDirectoryAsync(string remotePath, string localPath, Action<string> log, CancellationToken ct)
        {
            Directory.CreateDirectory(localPath);

            List<string> entries = await ListDirectoryAsync(remotePath, ct);

            foreach (string entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                string remoteEntry = $"{remotePath.TrimEnd('/')}/{entry}";
                string localEntry = Path.Combine(localPath, entry);

                bool isDir = await IsDirectoryAsync(remoteEntry, ct);

                if (isDir)
                {
                    log($"[폴더] {entry}");
                    await DownloadDirectoryAsync(remoteEntry, localEntry, log, ct);
                }
                else
                {
                    log($"[다운로드] {entry}");
                    await DownloadFileAsync(remoteEntry, localEntry, ct);
                    log($"[완료] {entry}");
                }
            }
        }

        private async Task<List<string>> ListDirectoryAsync(string remotePath, CancellationToken ct)
        {
            var uri = BuildUri(remotePath);
            var req = CreateRequest(uri, WebRequestMethods.Ftp.ListDirectory);

            var list = new List<string>();

            using (ct.Register(() => req.Abort()))
            using (var response = (FtpWebResponse)await Task.Factory.FromAsync(req.BeginGetResponse, req.EndGetResponse, null))
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    line = line.Trim();
                    if (!string.IsNullOrEmpty(line))
                    {
                        // NLST returns just the name or full path; extract filename
                        string name = Path.GetFileName(line.TrimEnd('/'));
                        if (!string.IsNullOrEmpty(name))
                            list.Add(name);
                    }
                }
            }

            return list;
        }

        private async Task<bool> IsDirectoryAsync(string remotePath, CancellationToken ct)
        {
            try
            {
                var uri = BuildUri(remotePath);
                var req = CreateRequest(uri, WebRequestMethods.Ftp.ListDirectory);
                using (ct.Register(() => req.Abort()))
                using (await Task.Factory.FromAsync(req.BeginGetResponse, req.EndGetResponse, null))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task DownloadFileAsync(string remotePath, string localPath, CancellationToken ct)
        {
            var uri = BuildUri(remotePath);
            var req = CreateRequest(uri, WebRequestMethods.Ftp.DownloadFile);

            using (ct.Register(() => req.Abort()))
            using (var response = (FtpWebResponse)await Task.Factory.FromAsync(req.BeginGetResponse, req.EndGetResponse, null))
            using (var ftpStream = response.GetResponseStream())
            using (var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write))
            {
                await ftpStream.CopyToAsync(fileStream, 81920, ct);
            }
        }
    }
}
