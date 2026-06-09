namespace PhonePhotoReturn
{
    internal static class MobilePage
    {
        public static string Render(string token)
        {
            return Template.Replace("{{TOKEN}}", token);
        }

        private const string Template = @"<!doctype html>
<html lang=""zh-CN"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>&#25293;&#29031;&#19978;&#20256;</title>
  <style>
    :root {
      color-scheme: light;
      font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", ""Microsoft YaHei"", sans-serif;
    }
    body {
      min-height: 100vh;
      margin: 0;
      display: grid;
      place-items: center;
      background: #f4f6f8;
      color: #172033;
    }
    main {
      width: min(92vw, 420px);
      padding: 24px;
      background: #fff;
      border: 1px solid #d9dee7;
      border-radius: 8px;
      box-shadow: 0 8px 26px rgba(23, 32, 51, .08);
      text-align: center;
    }
    h1 {
      margin: 0 0 18px;
      font-size: 24px;
      letter-spacing: 0;
    }
    label {
      display: block;
      padding: 16px 18px;
      border-radius: 8px;
      background: #1967d2;
      color: #fff;
      font-size: 18px;
      font-weight: 600;
    }
    input {
      display: none;
    }
    #status {
      min-height: 24px;
      margin: 18px 0 0;
      color: #5b6575;
    }
    #preview {
      display: none;
      width: 100%;
      margin-top: 18px;
      border-radius: 8px;
      border: 1px solid #d9dee7;
    }
  </style>
</head>
<body>
  <main>
    <h1>&#25293;&#29031;&#22238;&#20256;&#30005;&#33041;</h1>
    <label for=""photo"">&#28857;&#20987;&#25293;&#29031;</label>
    <input id=""photo"" name=""photo"" type=""file"" accept=""image/*"" capture=""environment"">
    <p id=""status"">&#20934;&#22791;&#23601;&#32490;</p>
    <img id=""preview"" alt=""&#29031;&#29255;&#39044;&#35272;"">
  </main>

  <script>
    const token = ""{{TOKEN}}"";
    const input = document.getElementById(""photo"");
    const status = document.getElementById(""status"");
    const preview = document.getElementById(""preview"");

    input.addEventListener(""change"", async () => {
      const file = input.files[0];
      if (!file) return;

      preview.src = URL.createObjectURL(file);
      preview.style.display = ""block"";
      status.textContent = ""\u6b63\u5728\u4e0a\u4f20..."";

      const form = new FormData();
      form.append(""token"", token);
      form.append(""photo"", file);

      try {
        const response = await fetch(`/upload?token=${encodeURIComponent(token)}`, {
          method: ""POST"",
          body: form
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        status.textContent = ""\u4e0a\u4f20\u6210\u529f"";
        input.value = """";
      } catch (error) {
        status.textContent = ""\u4e0a\u4f20\u5931\u8d25\uff0c\u8bf7\u68c0\u67e5\u7f51\u7edc"";
      }
    });
  </script>
</body>
</html>";
    }
}
