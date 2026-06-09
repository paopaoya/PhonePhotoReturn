using System;
using System.IO;
using System.Reflection;

namespace PhonePhotoReturn
{
    internal static class AssemblyLoader
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbeddedAssembly;
            _registered = true;
        }

        private static Assembly ResolveEmbeddedAssembly(object sender, ResolveEventArgs args)
        {
            var requestedName = new AssemblyName(args.Name).Name;
            if (!string.Equals(requestedName, "zxing", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("zxing.dll"))
            {
                if (stream == null)
                {
                    return null;
                }

                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return Assembly.Load(buffer.ToArray());
                }
            }
        }
    }
}
