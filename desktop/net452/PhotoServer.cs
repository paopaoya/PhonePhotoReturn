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
        public const int Port = 5000;
        private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(
            new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic" },
            StringComparer.OrdinalIgnoreCase);

        private readonly SynchronizationContext _context;
        private readonly object _syncRoot = new object();
        private TcpListener _listener;
        private Thread _listenerThread;
        private bool _disposed;
        private bool _running;

        public event EventHandler<ServerMessageEventArgs> Message;
        public event EventHandler<ServerErrorEventArgs> Error;
        public event EventHandler Ready;

        public PhotoServer()
        {
            _context = SynchronizationContext.Current;
            Token = CreateToken();
            UploadDirectory = SettingsStore.LoadUploadDirectory();
        }

        public string Token { get; private set; }
        public string UploadDirectory { get; set; }

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

                Directory.CreateDirectory(UploadDirectory);
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
            if (request.Method == "POST" && request.Path == "/upload")
            {
                HandleUpload(stream, request);
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

            if (form.FileBytes == null || form.FileBytes.Length == 0)
            {
                WriteResponse(stream, 400, "application/json; charset=utf-8", "{\"error\":\"No file part\"}");
                return;
            }

            var savedPath = SavePhoto(form.FileName, form.FileBytes);
            RaiseMessage("\u5df2\u4fdd\u5b58\u7167\u7247: " + savedPath);
            WriteResponse(stream, 200, "application/json; charset=utf-8", "{\"message\":\"Success\",\"filename\":\"" + JsonEscape(Path.GetFileName(savedPath)) + "\"}");
        }

        private string SavePhoto(string originalName, byte[] bytes)
        {
            Directory.CreateDirectory(UploadDirectory);
            var suffix = Path.GetExtension(SanitizeFileName(originalName));
            if (string.IsNullOrEmpty(suffix) || !AllowedExtensions.Contains(suffix))
            {
                suffix = ".jpg";
            }

            for (var i = 0; i < 100; i++)
            {
                var fileName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fffffff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + suffix.ToLowerInvariant();
                var path = Path.Combine(UploadDirectory, fileName);
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

            throw new IOException("\u65e0\u6cd5\u521b\u5efa\u552f\u4e00\u7167\u7247\u6587\u4ef6\u540d\u3002");
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
            var contentLength = 0;
            int.TryParse(request.Headers.Get("Content-Length"), out contentLength);
            request.Body = ReadBody(stream, contentLength);
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

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return "";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalid, '_');
            }
            return fileName;
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
        }
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
