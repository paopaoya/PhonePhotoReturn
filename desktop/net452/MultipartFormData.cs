using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PhonePhotoReturn
{
    internal sealed class MultipartFormData
    {
        public string Token;
        public string FileName;
        public byte[] FileBytes;

        public static MultipartFormData Parse(byte[] body, string contentType)
        {
            var boundary = ReadBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
            {
                throw new InvalidDataException("Missing multipart boundary.");
            }

            var result = new MultipartFormData();
            var boundaryBytes = Encoding.ASCII.GetBytes("--" + boundary);
            var indexes = FindBoundaryIndexes(body, boundaryBytes);
            for (var i = 0; i < indexes.Count; i++)
            {
                var start = indexes[i] + boundaryBytes.Length;
                if (start + 1 < body.Length && body[start] == '-' && body[start + 1] == '-')
                {
                    break;
                }

                if (start + 1 < body.Length && body[start] == '\r' && body[start + 1] == '\n')
                {
                    start += 2;
                }

                var next = i + 1 < indexes.Count ? indexes[i + 1] : body.Length;
                var end = next;
                if (end >= 2 && body[end - 2] == '\r' && body[end - 1] == '\n')
                {
                    end -= 2;
                }

                if (end <= start)
                {
                    continue;
                }

                var headerEnd = Find(body, CrLfCrLf, start, end);
                if (headerEnd < 0)
                {
                    continue;
                }

                var headerText = Encoding.UTF8.GetString(body, start, headerEnd - start);
                var headers = ParsePartHeaders(headerText);
                var dataStart = headerEnd + CrLfCrLf.Length;
                var dataLength = end - dataStart;
                if (dataLength < 0)
                {
                    continue;
                }

                var disposition = GetHeader(headers, "Content-Disposition");
                var name = ReadDispositionValue(disposition, "name");
                if (string.Equals(name, "token", StringComparison.OrdinalIgnoreCase))
                {
                    result.Token = Encoding.UTF8.GetString(body, dataStart, dataLength).Trim();
                }
                else if (string.Equals(name, "photo", StringComparison.OrdinalIgnoreCase))
                {
                    result.FileName = ReadDispositionValue(disposition, "filename*") ?? ReadDispositionValue(disposition, "filename");
                    result.FileBytes = new byte[dataLength];
                    Buffer.BlockCopy(body, dataStart, result.FileBytes, 0, dataLength);
                }
            }

            return result;
        }

        private static string ReadBoundary(string contentType)
        {
            if (contentType == null)
            {
                return null;
            }

            var parts = contentType.Split(';');
            foreach (var rawPart in parts)
            {
                var part = rawPart.Trim();
                if (part.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = part.Substring("boundary=".Length).Trim();
                    if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                    return value;
                }
            }

            return null;
        }

        private static Dictionary<string, string> ParsePartHeaders(string headerText)
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }
                headers[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }
            return headers;
        }

        private static string GetHeader(Dictionary<string, string> headers, string name)
        {
            string value;
            return headers.TryGetValue(name, out value) ? value : null;
        }

        private static string ReadDispositionValue(string disposition, string name)
        {
            if (string.IsNullOrEmpty(disposition))
            {
                return null;
            }

            var parts = disposition.Split(';');
            var prefix = name + "=";
            foreach (var rawPart in parts)
            {
                var part = rawPart.Trim();
                if (!part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = part.Substring(prefix.Length).Trim();
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                {
                    value = value.Substring(1, value.Length - 2);
                }
                if (name.EndsWith("*", StringComparison.Ordinal) && value.StartsWith("UTF-8''", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(value.Substring("UTF-8''".Length));
                }
                return value.Replace("\\\"", "\"");
            }

            return null;
        }

        private static List<int> FindBoundaryIndexes(byte[] haystack, byte[] needle)
        {
            var result = new List<int>();
            var index = 0;
            while (index < haystack.Length)
            {
                var found = Find(haystack, needle, index, haystack.Length);
                if (found < 0)
                {
                    break;
                }
                if (IsBoundaryDelimiter(haystack, found, needle.Length))
                {
                    result.Add(found);
                }
                index = found + needle.Length;
            }
            return result;
        }

        private static bool IsBoundaryDelimiter(byte[] haystack, int index, int length)
        {
            if (!(index == 0 || (index >= 2 && haystack[index - 2] == '\r' && haystack[index - 1] == '\n')))
            {
                return false;
            }

            var after = index + length;
            return after >= haystack.Length
                || (after + 1 < haystack.Length && haystack[after] == '-' && haystack[after + 1] == '-')
                || (after + 1 < haystack.Length && haystack[after] == '\r' && haystack[after + 1] == '\n');
        }

        private static int Find(byte[] haystack, byte[] needle, int start, int end)
        {
            if (needle.Length == 0 || end - start < needle.Length)
            {
                return -1;
            }

            var last = end - needle.Length;
            for (var i = start; i <= last; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        private static readonly byte[] CrLfCrLf = Encoding.ASCII.GetBytes("\r\n\r\n");
    }
}
