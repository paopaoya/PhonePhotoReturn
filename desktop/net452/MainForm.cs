using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ZXing;
using ZXing.Common;

namespace PhonePhotoReturn
{
    internal sealed class MainForm : Form
    {
        public const string AppName = "\u624b\u673a\u62cd\u7167\u81ea\u52a8\u56de\u4f20\u7535\u8111";

        private readonly PhotoServer _server;
        private PictureBox _qrPictureBox;
        private TextBox _scanUrlTextBox;
        private TextBox _pairPayloadTextBox;
        private TextBox _uploadDirectoryTextBox;
        private TextBox _logTextBox;
        private Label _statusLabel;
        private Button _copyUrlButton;
        private Button _copyPairButton;

        public MainForm()
        {
            Text = AppName;
            Width = 660;
            Height = 590;
            MinimumSize = new Size(590, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);

            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "icon.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "assets", "icon.ico"));
            }
            if (File.Exists(iconPath))
            {
                Icon = new Icon(iconPath);
            }
            else
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }

            _server = new PhotoServer();
            _server.Ready += ServerReady;
            _server.Message += ServerMessage;
            _server.Error += ServerError;

            BuildUi();
            UpdateUploadDirectoryText();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SetStatus("\u6b63\u5728\u542f\u52a8\u670d\u52a1...");
            _server.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _server.Dispose();
            base.OnFormClosed(e);
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.Padding = new Padding(10);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var title = new Label();
            title.AutoSize = true;
            title.Text = AppName;
            title.Font = new Font(Font.FontFamily, 16F, FontStyle.Bold);
            title.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(title, 0, 0);

            var qrGroup = new GroupBox();
            qrGroup.Text = "\u5fae\u4fe1 / App \u626b\u7801";
            qrGroup.Dock = DockStyle.Fill;
            qrGroup.Height = 255;
            qrGroup.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(qrGroup, 0, 1);

            _qrPictureBox = new PictureBox();
            _qrPictureBox.Dock = DockStyle.Fill;
            _qrPictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
            qrGroup.Controls.Add(_qrPictureBox);

            var fields = new TableLayoutPanel();
            fields.Dock = DockStyle.Fill;
            fields.ColumnCount = 3;
            fields.RowCount = 3;
            fields.Margin = new Padding(0, 0, 0, 8);
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(fields, 0, 2);

            fields.Controls.Add(MakeFieldLabel("\u626b\u7801\u5730\u5740:"), 0, 0);
            _scanUrlTextBox = MakeReadOnlyTextBox();
            fields.Controls.Add(_scanUrlTextBox, 1, 0);
            _copyUrlButton = new Button();
            _copyUrlButton.Text = "\u590d\u5236";
            _copyUrlButton.Enabled = false;
            _copyUrlButton.Click += delegate { CopyText(_scanUrlTextBox.Text, "\u626b\u7801\u5730\u5740\u5df2\u590d\u5236"); };
            fields.Controls.Add(_copyUrlButton, 2, 0);

            fields.Controls.Add(MakeFieldLabel("App \u914d\u5bf9:"), 0, 1);
            _pairPayloadTextBox = MakeReadOnlyTextBox();
            fields.Controls.Add(_pairPayloadTextBox, 1, 1);
            _copyPairButton = new Button();
            _copyPairButton.Text = "\u590d\u5236";
            _copyPairButton.Enabled = false;
            _copyPairButton.Click += delegate { CopyText(_pairPayloadTextBox.Text, "App \u914d\u5bf9\u5185\u5bb9\u5df2\u590d\u5236"); };
            fields.Controls.Add(_copyPairButton, 2, 1);

            fields.Controls.Add(MakeFieldLabel("\u4fdd\u5b58\u76ee\u5f55:"), 0, 2);
            _uploadDirectoryTextBox = MakeReadOnlyTextBox();
            fields.Controls.Add(_uploadDirectoryTextBox, 1, 2);
            var chooseButton = new Button();
            chooseButton.Text = "\u66f4\u6539";
            chooseButton.Click += ChooseUploadDirectory;
            fields.Controls.Add(chooseButton, 2, 2);

            foreach (Control control in fields.Controls)
            {
                control.Margin = new Padding(0, 3, 8, 3);
                if (control is TextBox)
                {
                    control.Dock = DockStyle.Fill;
                }
            }

            var logGroup = new GroupBox();
            logGroup.Text = "\u65e5\u5fd7";
            logGroup.Dock = DockStyle.Fill;
            logGroup.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(logGroup, 0, 3);

            _logTextBox = new TextBox();
            _logTextBox.Dock = DockStyle.Fill;
            _logTextBox.Multiline = true;
            _logTextBox.ReadOnly = true;
            _logTextBox.ScrollBars = ScrollBars.Vertical;
            logGroup.Controls.Add(_logTextBox);

            var bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Fill;
            bottom.FlowDirection = FlowDirection.LeftToRight;
            bottom.WrapContents = false;
            bottom.AutoSize = true;
            root.Controls.Add(bottom, 0, 4);

            _statusLabel = new Label();
            _statusLabel.AutoSize = true;
            _statusLabel.Text = "\u7b49\u5f85\u542f\u52a8";
            _statusLabel.Margin = new Padding(0, 8, 20, 0);
            bottom.Controls.Add(_statusLabel);

            var openButton = new Button();
            openButton.Text = "\u6253\u5f00\u4fdd\u5b58\u76ee\u5f55";
            openButton.AutoSize = true;
            openButton.Click += OpenUploadDirectory;
            bottom.Controls.Add(openButton);
        }

        private static Label MakeFieldLabel(string text)
        {
            var label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Padding = new Padding(0, 6, 0, 0);
            return label;
        }

        private static TextBox MakeReadOnlyTextBox()
        {
            var textBox = new TextBox();
            textBox.ReadOnly = true;
            return textBox;
        }

        private void ServerReady(object sender, EventArgs e)
        {
            _scanUrlTextBox.Text = _server.WebUrl;
            _pairPayloadTextBox.Text = _server.PairPayload;
            _copyUrlButton.Enabled = true;
            _copyPairButton.Enabled = true;
            RenderQrCode(_server.WebUrl);
            SetStatus("\u670d\u52a1\u5df2\u542f\u52a8\uff0c\u7b49\u5f85\u624b\u673a\u4e0a\u4f20");
            AddLog("\u670d\u52a1\u5df2\u542f\u52a8: " + _server.BaseUrl);
        }

        private void ServerMessage(object sender, ServerMessageEventArgs e)
        {
            SetStatus("\u6536\u5230\u65b0\u7167\u7247");
            AddLog(e.Message);
        }

        private void ServerError(object sender, ServerErrorEventArgs e)
        {
            SetStatus(e.Message);
            AddLog(e.Message);
        }

        private void RenderQrCode(string text)
        {
            var writer = new BarcodeWriter();
            writer.Format = BarcodeFormat.QR_CODE;
            writer.Options = new EncodingOptions
            {
                Height = 220,
                Width = 220,
                Margin = 1,
                PureBarcode = true
            };

            var bitmap = writer.Write(text);
            var oldImage = _qrPictureBox.Image;
            _qrPictureBox.Image = bitmap;
            if (oldImage != null)
            {
                oldImage.Dispose();
            }
        }

        private void ChooseUploadDirectory(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "\u9009\u62e9\u7167\u7247\u4fdd\u5b58\u76ee\u5f55";
                dialog.SelectedPath = _server.UploadDirectory;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _server.UploadDirectory = dialog.SelectedPath;
                Directory.CreateDirectory(_server.UploadDirectory);
                SettingsStore.SaveUploadDirectory(_server.UploadDirectory);
                UpdateUploadDirectoryText();
                AddLog("\u4fdd\u5b58\u76ee\u5f55\u5df2\u5207\u6362: " + _server.UploadDirectory);
            }
        }

        private void OpenUploadDirectory(object sender, EventArgs e)
        {
            Directory.CreateDirectory(_server.UploadDirectory);
            Process.Start(_server.UploadDirectory);
        }

        private void UpdateUploadDirectoryText()
        {
            _uploadDirectoryTextBox.Text = _server.UploadDirectory;
        }

        private void CopyText(string text, string status)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Clipboard.SetText(text);
            SetStatus(status);
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }

        private void AddLog(string message)
        {
            _logTextBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        }
    }
}
