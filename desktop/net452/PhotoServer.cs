using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PhonePhotoReturn
{
    internal sealed class PhotoServer : IDisposable
    {
        public const int Port = 36666;
        private const long MaxRequestBodyBytes = 512L * 1024L * 1024L;
        private static readonly TimeSpan OutboxItemLifetime = TimeSpan.FromHours(1);
        private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(
            new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic" },
            StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ReservedDeviceNames = new HashSet<string>(
            new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" },
            StringComparer.OrdinalIgnoreCase);

        private readonly SynchronizationContext _context;
        private readonly object _syncRoot = new object();
        private readonly object _outboxLock = new object();
        private readonly List<OutboxItem> _outbox = new List<OutboxItem>();
        private TcpListener _listener;
        private Thread _listenerThread;
        private bool _disposed;
        private bool _running;

        public event EventHandler<ServerMessageEventArgs> Message;
        public event EventHandler<ServerErrorEventArgs> Error;
        public event EventHandler<OutboxChangedEventArgs> OutboxChanged;
        public event EventHandler Ready;

        public PhotoServer()
        {
            _context = SynchronizationContext.Current;
            var fixedToken = SettingsStore.LoadFixedTokenEnabled() ? SettingsStore.LoadFixedToken() : null;
            Token = string.IsNullOrEmpty(fixedToken) ? CreateToken() : fixedToken;
            PhotoUploadDirectory = SettingsStore.LoadUploadDirectory();
            FileUploadDirectory = SettingsStore.LoadFileUploadDirectory();
        }

        public string Token { get; private set; }
        public string UploadDirectory
        {
            get { return PhotoUploadDirectory; }
            set { PhotoUploadDirectory = value; }
        }

        public string PhotoUploadDirectory { get; set; }
        public string FileUploadDirectory { get; set; }

        public string Host
        {
            get { return GetLocalIpAddress(); }
        }

        public string BaseUrl
        {
            get { return "http://" + Host + ":" + Port; }
        }

        public string WebUrl
        {
            get { return BaseUrl + "/?token=" + Uri.EscapeDataString(Token); }
        }

        public string PairPayload
        {
            get
            {
                var host = Host;
                return "{"
                    + "\"type\":\"phone_photo_return_pair\","
                    + "\"version\":1,"
                    + "\"name\":\"" + JsonEscape(MainForm.AppName) + "\","
                    + "\"host\":\"" + JsonEscape(host) + "\","
                    + "\"port\":" + Port + ","
                    + "\"base_url\":\"" + JsonEscape("http://" + host + ":" + Port) + "\","
                    + "\"token\":\"" + JsonEscape(Token) + "\","
                    + "\"upload_path\":\"/upload\","
                    + "\"outbox_path\":\"/outbox.json\","
                    + "\"health_path\":\"/health\""
                    + "}";
            }
        }

        public void Start()
        {
            lock (_syncRoot)
            {
                if (_running)
                {
                    return;
                }

                Directory.CreateDirectory(PhotoUploadDirectory);
                Directory.CreateDirectory(FileUploadDirectory);
                _listener = new TcpListener(IPAddress.Any, Port);
                try
                {
                    _listener.Start();
                }
                catch (Exception ex)
                {
                    RaiseError("\u670d\u52a1\u542f\u52a8\u5931\u8d25\uff0c\u7aef\u53e3 " + Port + " \u4e0d\u53ef\u7528: " + ex.Message);
                    return;
                }

                _running = true;
                _listenerThread = new Thread(ListenLoop);
                _listenerThread.IsBackground = true;
                _listenerThread.Start();
            }

            RaiseReady();
        }

        public void Dispose()
        {
            _disposed = true;
            lock (_syncRoot)
            {
                _running = false;
                if (_listener != null)
                {
                    try
                    {
                        _listener.Stop();
                    }
                    catch
                    {
                    }
                    _listener = null;
                }
            }
        }

        private void ListenLoop()
        {
            while (!_disposed)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                    client = null;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (!_disposed)
                    {
                        RaiseError("\u670d\u52a1\u76d1\u542c\u5f02\u5e38\uff0c\u8bf7\u91cd\u65b0\u542f\u52a8\u7a0b\u5e8f\u3002");
                    }
                    break;
                }
                finally
                {
                    if (client != null)
                    {
                        client.Close();
                    }
                }
            }
        }

        private void HandleClient(object state)
        {
            using (var client = (TcpClient)state)
            {
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 15000;

                try
                {
                    using (var stream = client.GetStream())
                    {
                        var request = ReadRequest(stream);
                        if (request == null)
                        {
                            return;
                        }

                        HandleRequest(stream, request);
                    }
                }
                catch (Exception ex)
                {
                    RaiseError("\u8bf7\u6c42\u5904\u7406\u5931\u8d25: " + ex.Message);
                }
            }
        }

        private void HandleRequest(Stream stream, HttpRequestData request)
        {
            if (request.BodyTooLarge)
            {
                WriteResponse(stream, 413, "application/json; charset=utf-8", "{\"error\":\"Request entity too large\"}");
                return;
            }

            if (request.Method == "POST" && request.Path == "/upload")
            {
                HandleUpload(stream, request);
                return;
            }

            if (request.Method == "GET" && request.Path.StartsWith("/download/", StringComparison.OrdinalIgnoreCase))
            {
                HandleDownload(stream, request);
                return;
            }

            if (!ValidateToken(request))
            {
                WriteResponse(stream, 403, "application/json; charset=utf-8", "{\"error\":\"Forbidden\"}");
                return;
            }

            if (request.Method == "GET" && request.Path == "/")
            {
                WriteResponse(stream, 200, "text/html; charset=utf-8", MobilePage.Render(Token));
                return;
            }

            if (request.Method == "GET" && request.Path == "/health")
            {
                var json = "{"
                    + "\"ok\":true,"
                    + "\"name\":\"" + JsonEscape(MainForm.AppName) + "\","
                    + "\"version\":1,"
                    + "\"upload_path\":\"/upload\","
                    + "\"outbox_path\":\"/outbox.json\","
                    + "\"server_time\":\"" + JsonEscape(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")) + "\""
                    + "}";
                WriteResponse(stream, 200, "application/json; charset=utf-8", json);
                return;
            }

            if (request.Method == "GET" && request.Path == "/pair.json")
            {
                WriteResponse(stream, 200, "application/json; charset=utf-8", PairPayload);
                return;
            }

            if (request.Method == "GET" && request.Path == "/outbox.json")
            {
                WriteResponse(stream, 200, "application/json; charset=utf-8", BuildOutboxJson());
                return;
            }

            if (request.Method == "POST"
                && request.Path.StartsWith("/outbox/", StringComparison.OrdinalIgnoreCase)
                && request.Path.EndsWith("/ack", StringComparison.OrdinalIgnoreCase))
            {
                HandleOutboxAck(stream, request);
                return;
            }

            WriteResponse(stream, 404, "application/json; charset=utf-8", "{\"error\":\"Not found\"}");
        }

        private void HandleUpload(Stream stream, HttpRequestData request)
        {
            MultipartFormData form;
            try
            {
                form = MultipartFormData.Parse(request.Body, request.ContentType);
            }
            catch (Exception ex)
            {
                WriteResponse(stream, 400, "application/json; charset=utf-8", "{\"error\":\"" + JsonEscape(ex.Message) + "\"}");
                return;
            }

            var supplied = ReadBearerToken(request);
            if (string.IsNullOrEmpty(supplied))
            {
                supplied = request.Query.Get("token");
            }
            if (string.IsNullOrEmpty(supplied))
            {
                supplied = form.Token;
            }

            if (supplied != Token)
            {
                WriteResponse(stream, 403, "application/json; charset=utf-8", "{\"error\":\"Forbidden\"}");
                return;
            }

            if (form.FileBytes == null || string.IsNullOrEmpty(form.FieldName))
            {
                WriteResponse(stream, 400, "application/json; charset=utf-8", "{\"error\":\"No file part\"}");
                return;
            }

            if (form.IsPhoto && form.FileBytes.Length == 0)
            {
                WriteResponse(stream, 400, "application/json; charset=utf-8", "{\"error\":\"Empty photo\"}");
                return;
            }

            string savedPath;
            string kind;
            try
            {
                savedPath = SaveUpload(form);
                kind = form.IsFile ? "file" : "photo";
            }
            catch (Exception ex)
            {
                WriteResponse(stream, 500, "application/json; charset=utf-8", "{\"error\":\"" + JsonEscape(ex.Message) + "\"}");
                return;
            }

            var label = form.IsFile ? "\u6587\u4ef6" : "\u7167\u7247";
            RaiseMessage("\u5df2\u4fdd\u5b58" + label + ": " + savedPath);
            WriteResponse(stream, 200, "application/json; charset=utf-8", "{\"message\":\"Success\",\"filename\":\"" + JsonEscape(Path.GetFileName(savedPath)) + "\",\"kind\":\"" + kind + "\"}");
        }

        private string SaveUpload(MultipartFormData form)
        {
            if (form.IsFile)
            {
                return SaveFile(FileUploadDirectory, form.FileName, form.FileBytes, false);
            }

            return SaveFile(PhotoUploadDirectory, form.FileName, form.FileBytes, true);
        }

        private string SaveFile(string directory, string originalName, byte[] bytes, bool forcePhotoExtension)
        {
            Directory.CreateDirectory(directory);
            var fallbackName = forcePhotoExtension ? "photo.jpg" : "file.bin";
            var safeName = BuildSafeFileName(originalName, fallbackName);
            var suffix = Path.GetExtension(safeName);
            if (forcePhotoExtension && (string.IsNullOrEmpty(suffix) || !AllowedExtensions.Contains(suffix)))
            {
                suffix = ".jpg";
            }
            var stem = Path.GetFileNameWithoutExtension(safeName);
            if (string.IsNullOrWhiteSpace(stem))
            {
                stem = forcePhotoExtension ? "photo" : "file";
            }

            for (var i = 0; i < 100; i++)
            {
                var fileName = i == 0 ? stem + suffix : stem + "(" + i + ")" + suffix;
                var path = Path.Combine(directory, fileName);
                try
                {
                    using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    {
                        file.Write(bytes, 0, bytes.Length);
                    }
                    return path;
                }
                catch (IOException)
                {
                }
            }

            throw new IOException("\u65e0\u6cd5\u521b\u5efa\u552f\u4e00\u6587\u4ef6\u540d\u3002");
        }

        public OutboxItem AddOutboxFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("\u6587\u4ef6\u4e0d\u5b58\u5728\u3002", filePath);
            }

            var info = new FileInfo(filePath);
            var item = new OutboxItem();
            item.Id = CreateToken();
            item.FilePath = info.FullName;
            item.FileName = BuildSafeFileName(info.Name, "file.bin");
            item.Size = info.Length;
            item.CreatedAt = DateTime.Now;
            item.ExpiresAt = item.CreatedAt.Add(OutboxItemLifetime);
            item.Status = OutboxStatus.Pending;

            lock (_outboxLock)
            {
                CleanupExpiredOutboxItems(null);
                _outbox.Add(item);
            }

            RaiseOutboxChanged(item);
            RaiseMessage("\u5df2\u52a0\u5165\u5f85\u53d1\u9001\u6587\u4ef6: " + item.FileName);
            return item;
        }

        public OutboxItem[] GetOutboxSnapshot()
        {
            lock (_outboxLock)
            {
                CleanupExpiredOutboxItems(null);
                return _outbox.Select(CloneOutboxItem).ToArray();
            }
        }

        private string BuildOutboxJson()
        {
            OutboxItem[] snapshot;
            var expired = new List<OutboxItem>();
            lock (_outboxLock)
            {
                CleanupExpiredOutboxItems(expired);
                snapshot = _outbox
                    .Where(item => item.Status == OutboxStatus.Pending || item.Status == OutboxStatus.Downloading)
                    .Select(CloneOutboxItem)
                    .ToArray();
            }
            RaiseOutboxChanged(expired);

            var builder = new StringBuilder();
            builder.Append("{\"items\":[");
            for (var i = 0; i < snapshot.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(",");
                }

                var item = snapshot[i];
                builder.Append("{");
                builder.Append("\"id\":\"").Append(JsonEscape(item.Id)).Append("\",");
                builder.Append("\"name\":\"").Append(JsonEscape(item.FileName)).Append("\",");
                builder.Append("\"size\":").Append(item.Size).Append(",");
                builder.Append("\"created_at\":\"").Append(JsonEscape(FormatJsonDate(item.CreatedAt))).Append("\",");
                builder.Append("\"expires_at\":\"").Append(JsonEscape(FormatJsonDate(item.ExpiresAt))).Append("\",");
                builder.Append("\"download_path\":\"/download/").Append(JsonEscape(Uri.EscapeDataString(item.Id))).Append("\",");
                builder.Append("\"status\":\"").Append(JsonEscape(StatusToJson(item.Status))).Append("\"");
                builder.Append("}");
            }
            builder.Append("]}");
            return builder.ToString();
        }

        private void HandleDownload(Stream stream, HttpRequestData request)
        {
            if (!ValidateToken(request))
            {
                WriteResponse(stream, 403, "application/json; charset=utf-8", "{\"error\":\"Forbidden\"}");
                return;
            }

            var id = Uri.UnescapeDataString(request.Path.Substring("/download/".Length));
            OutboxItem item;
            var expired = new List<OutboxItem>();
            lock (_outboxLock)
            {
                CleanupExpiredOutboxItems(expired);
                item = _outbox.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
                if (item != null
                    && (item.Status == OutboxStatus.Pending || item.Status == OutboxStatus.Downloading)
                    && File.Exists(item.FilePath))
                {
                    item.Status = OutboxStatus.Downloading;
                    item = CloneOutboxItem(item);
                }
                else
                {
                    item = null;
                }
            }
            RaiseOutboxChanged(expired);

            if (item == null)
            {
                WriteResponse(stream, 404, "application/json; charset=utf-8", "{\"error\":\"Not found\"}");
                return;
            }

            RaiseOutboxChanged(item);
            WriteFileResponse(stream, item);
        }

        private void HandleOutboxAck(Stream stream, HttpRequestData request)
        {
            var prefix = "/outbox/";
            var suffix = "/ack";
            var id = request.Path.Substring(prefix.Length, request.Path.Length - prefix.Length - suffix.Length);
            id = Uri.UnescapeDataString(id);

            OutboxItem changed = null;
            lock (_outboxLock)
            {
                var item = _outbox.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
                if (item != null)
                {
                    item.Status = OutboxStatus.Completed;
                    changed = CloneOutboxItem(item);
                }
            }

            if (changed == null)
            {
                WriteResponse(stream, 404, "application/json; charset=utf-8", "{\"error\":\"Not found\"}");
                return;
            }

            RaiseOutboxChanged(changed);
            RaiseMessage("\u624b\u673a\u5df2\u4e0b\u8f7d: " + changed.FileName);
            WriteResponse(stream, 200, "application/json; charset=utf-8", "{\"ok\":true}");
        }

        private void WriteFileResponse(Stream stream, OutboxItem item)
        {
            try
            {
                using (var file = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var disposition = "attachment; filename=\"" + HeaderQuotedFileName(item.FileName) + "\"; filename*=UTF-8''" + Uri.EscapeDataString(item.FileName);
                    var header = "HTTP/1.1 200 OK\r\n"
                        + "Content-Type: application/octet-stream\r\n"
                        + "Content-Length: " + file.Length + "\r\n"
                        + "Content-Disposition: " + disposition + "\r\n"
                        + "Connection: close\r\n"
                        + "\r\n";
                    var headerBytes = Encoding.ASCII.GetBytes(header);
                    stream.Write(headerBytes, 0, headerBytes.Length);

                    var buffer = new byte[81920];
                    int read;
                    while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        stream.Write(buffer, 0, read);
                    }
                }
            }
            catch (IOException)
            {
                WriteResponse(stream, 404, "application/json; charset=utf-8", "{\"error\":\"Not found\"}");
            }
        }

        private void CleanupExpiredOutboxItems(List<OutboxItem> expiredItems)
        {
            var now = DateTime.Now;
            foreach (var item in _outbox)
            {
                if ((item.Status == OutboxStatus.Pending || item.Status == OutboxStatus.Downloading) && item.ExpiresAt < now)
                {
                    item.Status = OutboxStatus.Expired;
                    if (expiredItems != null)
                    {
                        expiredItems.Add(CloneOutboxItem(item));
                    }
                }
            }
        }

        private void RaiseOutboxChanged(IEnumerable<OutboxItem> items)
        {
            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                RaiseOutboxChanged(item);
            }
        }

        private static OutboxItem CloneOutboxItem(OutboxItem item)
        {
            return new OutboxItem
            {
                Id = item.Id,
                FilePath = item.FilePath,
                FileName = item.FileName,
                Size = item.Size,
                CreatedAt = item.CreatedAt,
                ExpiresAt = item.ExpiresAt,
                Status = item.Status
            };
        }

        private static string FormatJsonDate(DateTime value)
        {
            return value.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        private static string StatusToJson(OutboxStatus status)
        {
            switch (status)
            {
                case OutboxStatus.Downloading:
                    return "downloading";
                case OutboxStatus.Completed:
                    return "completed";
                case OutboxStatus.Failed:
                    return "failed";
                case OutboxStatus.Expired:
                    return "expired";
                default:
                    return "pending";
            }
        }

        private static string HeaderQuotedFileName(string value)
        {
            var safe = BuildAsciiFileName(value);
            return safe.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private bool ValidateToken(HttpRequestData request)
        {
            var supplied = ReadBearerToken(request);
            if (string.IsNullOrEmpty(supplied))
            {
                supplied = request.Query.Get("token");
            }

            return supplied == Token;
        }

        private static string ReadBearerToken(HttpRequestData request)
        {
            var authorization = request.Headers.Get("Authorization");
            if (authorization == null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return authorization.Substring("Bearer ".Length).Trim();
        }

        private static HttpRequestData ReadRequest(Stream stream)
        {
            var headerBytes = new List<byte>();
            var buffer = new byte[1];
            while (headerBytes.Count < 1024 * 1024)
            {
                var read = stream.Read(buffer, 0, 1);
                if (read == 0)
                {
                    return null;
                }

                headerBytes.Add(buffer[0]);
                var count = headerBytes.Count;
                if (count >= 4
                    && headerBytes[count - 4] == '\r'
                    && headerBytes[count - 3] == '\n'
                    && headerBytes[count - 2] == '\r'
                    && headerBytes[count - 1] == '\n')
                {
                    break;
                }
            }

            var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
            {
                return null;
            }

            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
            {
                return null;
            }

            var request = new HttpRequestData();
            request.Method = requestLine[0].ToUpperInvariant();
            request.Headers = new WebHeaderCollection();

            var rawUrl = requestLine[1];
            var queryIndex = rawUrl.IndexOf('?');
            request.Path = queryIndex >= 0 ? rawUrl.Substring(0, queryIndex) : rawUrl;
            request.Query = queryIndex >= 0
                ? ParseQuery(rawUrl.Substring(queryIndex + 1))
                : new System.Collections.Specialized.NameValueCollection();

            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                var separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                request.Headers[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }

            request.ContentType = request.Headers.Get("Content-Type");
            long contentLength = 0;
            long.TryParse(request.Headers.Get("Content-Length"), out contentLength);
            if (contentLength > MaxRequestBodyBytes)
            {
                request.BodyTooLarge = true;
                request.Body = new byte[0];
                return request;
            }

            request.Body = ReadBody(stream, (int)contentLength);
            return request;
        }

        private static byte[] ReadBody(Stream stream, int contentLength)
        {
            if (contentLength <= 0)
            {
                return new byte[0];
            }

            var body = new byte[contentLength];
            var offset = 0;
            while (offset < contentLength)
            {
                var read = stream.Read(body, offset, contentLength - offset);
                if (read <= 0)
                {
                    break;
                }
                offset += read;
            }

            if (offset == contentLength)
            {
                return body;
            }

            var trimmed = new byte[offset];
            Buffer.BlockCopy(body, 0, trimmed, 0, offset);
            return trimmed;
        }

        private static System.Collections.Specialized.NameValueCollection ParseQuery(string query)
        {
            var values = new System.Collections.Specialized.NameValueCollection();
            if (string.IsNullOrEmpty(query))
            {
                return values;
            }

            var parts = query.Split('&');
            foreach (var part in parts)
            {
                if (part.Length == 0)
                {
                    continue;
                }

                var equals = part.IndexOf('=');
                var name = equals >= 0 ? part.Substring(0, equals) : part;
                var value = equals >= 0 ? part.Substring(equals + 1) : "";
                values[Uri.UnescapeDataString(name.Replace("+", " "))] = Uri.UnescapeDataString(value.Replace("+", " "));
            }
            return values;
        }

        private static void WriteResponse(Stream stream, int statusCode, string contentType, string body)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body ?? "");
            var header = "HTTP/1.1 " + statusCode + " " + StatusText(statusCode) + "\r\n"
                + "Content-Type: " + contentType + "\r\n"
                + "Content-Length: " + bodyBytes.Length + "\r\n"
                + "Connection: close\r\n"
                + "\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
        }

        private static string StatusText(int statusCode)
        {
            switch (statusCode)
            {
                case 200:
                    return "OK";
                case 400:
                    return "Bad Request";
                case 403:
                    return "Forbidden";
                case 404:
                    return "Not Found";
                case 413:
                    return "Request Entity Too Large";
                case 500:
                    return "Internal Server Error";
                default:
                    return "OK";
            }
        }

        private static string GetLocalIpAddress()
        {
            var addresses = new List<IPAddress>();
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up
                        || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork
                            && !IPAddress.IsLoopback(unicast.Address))
                        {
                            addresses.Add(unicast.Address);
                        }
                    }
                }
            }
            catch
            {
            }

            if (addresses.Count == 0)
            {
                try
                {
                    addresses.AddRange(Dns.GetHostAddresses(Dns.GetHostName())
                        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)));
                }
                catch
                {
                }
            }

            var privateAddress = addresses.FirstOrDefault(IsPrivateAddress);
            if (privateAddress != null)
            {
                return privateAddress.ToString();
            }

            return addresses.Count > 0 ? addresses[0].ToString() : "127.0.0.1";
        }

        private static bool IsPrivateAddress(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        private static string CreateToken()
        {
            var bytes = new byte[18];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string BuildSafeFileName(string fileName)
        {
            return BuildSafeFileName(fileName, "photo.jpg");
        }

        private static string BuildSafeFileName(string fileName, string fallbackName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return fallbackName;
            }

            fileName = fileName.Trim().Replace('/', '_').Replace('\\', '_');
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }

            fileName = fileName.Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return fallbackName;
            }

            var stem = Path.GetFileNameWithoutExtension(fileName);
            if (ReservedDeviceNames.Contains(stem))
            {
                fileName = "_" + fileName;
            }

            return fileName;
        }

        private static string BuildAsciiFileName(string fileName)
        {
            fileName = BuildSafeFileName(fileName, "file.bin");
            var builder = new StringBuilder();
            foreach (var ch in fileName)
            {
                if ((ch >= 'a' && ch <= 'z')
                    || (ch >= 'A' && ch <= 'Z')
                    || (ch >= '0' && ch <= '9')
                    || ch == '.'
                    || ch == '_'
                    || ch == '-'
                    || ch == ' ')
                {
                    builder.Append(ch);
                }
                else
                {
                    builder.Append('_');
                }
            }

            var result = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? "file.bin" : result;
        }

        private static string JsonEscape(string value)
        {
            if (value == null)
            {
                return "";
            }

            var builder = new StringBuilder();
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(ch))
                        {
                            builder.Append("\\u");
                            builder.Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(ch);
                        }
                        break;
                }
            }
            return builder.ToString();
        }

        private void RaiseReady()
        {
            Post(delegate
            {
                var handler = Ready;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            });
        }

        private void RaiseMessage(string message)
        {
            Post(delegate
            {
                var handler = Message;
                if (handler != null)
                {
                    handler(this, new ServerMessageEventArgs(message));
                }
            });
        }

        private void RaiseError(string message)
        {
            Post(delegate
            {
                var handler = Error;
                if (handler != null)
                {
                    handler(this, new ServerErrorEventArgs(message));
                }
            });
        }

        private void RaiseOutboxChanged(OutboxItem item)
        {
            Post(delegate
            {
                var handler = OutboxChanged;
                if (handler != null)
                {
                    handler(this, new OutboxChangedEventArgs(CloneOutboxItem(item)));
                }
            });
        }

        private void Post(SendOrPostCallback callback)
        {
            if (_context != null)
            {
                _context.Post(callback, null);
            }
            else
            {
                callback(null);
            }
        }

        private sealed class HttpRequestData
        {
            public string Method;
            public string Path;
            public System.Collections.Specialized.NameValueCollection Query;
            public WebHeaderCollection Headers;
            public string ContentType;
            public byte[] Body;
            public bool BodyTooLarge;
        }
    }

    internal enum OutboxStatus
    {
        Pending,
        Downloading,
        Completed,
        Failed,
        Expired
    }

    internal sealed class OutboxItem
    {
        public string Id;
        public string FilePath;
        public string FileName;
        public long Size;
        public DateTime CreatedAt;
        public DateTime ExpiresAt;
        public OutboxStatus Status;
    }

    internal sealed class OutboxChangedEventArgs : EventArgs
    {
        public OutboxChangedEventArgs(OutboxItem item)
        {
            Item = item;
        }

        public OutboxItem Item { get; private set; }
    }

    internal sealed class ServerMessageEventArgs : EventArgs
    {
        public ServerMessageEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; private set; }
    }

    internal sealed class ServerErrorEventArgs : EventArgs
    {
        public ServerErrorEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; private set; }
    }
}
