using System;
using System.IO;
using System.Text;

namespace PhonePhotoReturn
{
    internal static class MobilePage
    {
        public static string Render(string token)
        {
            var html = ReadTemplate();
            return html.Replace("{{ token }}", token);
        }

        private static string ReadTemplate()
        {
            using (var stream = typeof(MobilePage).Assembly.GetManifestResourceStream("mobile.html"))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Missing embedded mobile.html template.");
                }

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
