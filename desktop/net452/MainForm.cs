using System;
using System.Collections.Generic;
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
        private TextBox _fileUploadDirectoryTextBox;
        private TextBox _sendFileTextBox;
        private TextBox _logTextBox;
        private Label _statusLabel;
        private Label _sendStatusLabel;
        private Button _copyUrlButton;
        private Button _copyPairButton;
        private Button _confirmSendButton;
        private CheckBox _fixedTokenCheckBox;
        private readonly List<string> _selectedSendFilePaths = new List<string>();

        public MainForm()
        {
            Text = AppName;
            Width = 660;
            Height = 560;
            MinimumSize = new Size(620, 480);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
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
            _server.OutboxChanged += ServerOutboxChanged;

            BuildUi();
            UpdateUploadDirectoryText();
            UpdateFileUploadDirectoryText();
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
            var scrollPanel = new Panel();
            scrollPanel.Dock = DockStyle.Fill;
            scrollPanel.AutoScroll = true;
            Controls.Add(scrollPanel);

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Top;
            root.AutoSize = true;
            root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.Padding = new Padding(10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            scrollPanel.Controls.Add(root);

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
            fields.AutoSize = true;
            fields.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            fields.ColumnCount = 3;
            fields.RowCount = 5;
            fields.Margin = new Padding(0, 0, 0, 8);
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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

            _fixedTokenCheckBox = new CheckBox();
            _fixedTokenCheckBox.Text = "\u56fa\u5b9a token";
            _fixedTokenCheckBox.AutoSize = true;
            _fixedTokenCheckBox.Checked = SettingsStore.LoadFixedTokenEnabled();
            _fixedTokenCheckBox.CheckedChanged += FixedTokenCheckBoxChanged;
            fields.SetColumnSpan(_fixedTokenCheckBox, 2);
            fields.Controls.Add(_fixedTokenCheckBox, 1, 2);

            fields.Controls.Add(MakeFieldLabel("\u7167\u7247\u4fdd\u5b58\u76ee\u5f55:"), 0, 3);
            _uploadDirectoryTextBox = MakeReadOnlyTextBox();
            fields.Controls.Add(_uploadDirectoryTextBox, 1, 3);
            var chooseButton = new Button();
            chooseButton.Text = "\u66f4\u6539";
            chooseButton.Click += ChooseUploadDirectory;
            fields.Controls.Add(chooseButton, 2, 3);

            fields.Controls.Add(MakeFieldLabel("\u6587\u4ef6\u4fdd\u5b58\u76ee\u5f55:"), 0, 4);
            _fileUploadDirectoryTextBox = MakeReadOnlyTextBox();
            fields.Controls.Add(_fileUploadDirectoryTextBox, 1, 4);
            var chooseFileDirectoryButton = new Button();
            chooseFileDirectoryButton.Text = "\u66f4\u6539";
            chooseFileDirectoryButton.Click += ChooseFileUploadDirectory;
            fields.Controls.Add(chooseFileDirectoryButton, 2, 4);

            foreach (Control control in fields.Controls)
            {
                control.Margin = new Padding(0, 3, 8, 3);
                if (control is TextBox)
                {
                    control.Dock = DockStyle.Fill;
                }
            }

            var sendGroup = new GroupBox();
            sendGroup.Text = "\u53d1\u9001\u5230\u624b\u673a";
            sendGroup.Dock = DockStyle.Fill;
            sendGroup.AutoSize = true;
            sendGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            sendGroup.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(sendGroup, 0, 3);

            var sendLayout = new TableLayoutPanel();
            sendLayout.Dock = DockStyle.Top;
            sendLayout.AutoSize = true;
            sendLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            sendLayout.ColumnCount = 3;
            sendLayout.RowCount = 2;
            sendLayout.Padding = new Padding(8);
            sendLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sendLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sendLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sendLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sendLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sendGroup.Controls.Add(sendLayout);

            var chooseSendFileButton = new Button();
            chooseSendFileButton.Text = "\u9009\u62e9\u6587\u4ef6";
            chooseSendFileButton.AutoSize = true;
            chooseSendFileButton.Click += ChooseSendFile;
            sendLayout.Controls.Add(chooseSendFileButton, 0, 0);

            _sendFileTextBox = MakeReadOnlyTextBox();
            sendLayout.Controls.Add(_sendFileTextBox, 1, 0);

            _confirmSendButton = new Button();
            _confirmSendButton.Text = "\u786e\u8ba4\u53d1\u9001";
            _confirmSendButton.Enabled = false;
            _confirmSendButton.AutoSize = true;
            _confirmSendButton.Click += ConfirmSendFile;
            sendLayout.Controls.Add(_confirmSendButton, 2, 0);

            _sendStatusLabel = new Label();
            _sendStatusLabel.Text = "\u672a\u9009\u62e9\u6587\u4ef6";
            _sendStatusLabel.AutoSize = true;
            _sendStatusLabel.Margin = new Padding(0, 8, 0, 0);
            sendLayout.SetColumnSpan(_sendStatusLabel, 3);
            sendLayout.Controls.Add(_sendStatusLabel, 0, 1);

            foreach (Control control in sendLayout.Controls)
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
            logGroup.Height = 130;
            logGroup.MinimumSize = new Size(0, 130);
            logGroup.Margin = new Padding(0, 0, 0, 8);
            root.Controls.Add(logGroup, 0, 4);

            _logTextBox = new TextBox();
            _logTextBox.Dock = DockStyle.Fill;
            _logTextBox.Multiline = true;
            _logTextBox.ReadOnly = true;
            _logTextBox.ScrollBars = ScrollBars.Vertical;
            logGroup.Controls.Add(_logTextBox);

            var bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Fill;
            bottom.FlowDirection = FlowDirection.LeftToRight;
            bottom.WrapContents = true;
            bottom.AutoSize = true;
            root.Controls.Add(bottom, 0, 5);

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

            var openFileButton = new Button();
            openFileButton.Text = "\u6253\u5f00\u6587\u4ef6\u76ee\u5f55";
            openFileButton.AutoSize = true;
            openFileButton.Click += OpenFileUploadDirectory;
            bottom.Controls.Add(openFileButton);
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
            SetStatus("\u6536\u5230\u65b0\u6587\u4ef6");
            AddLog(e.Message);
        }

        private void ServerError(object sender, ServerErrorEventArgs e)
        {
            SetStatus(e.Message);
            AddLog(e.Message);
        }

        private void ServerOutboxChanged(object sender, OutboxChangedEventArgs e)
        {
            var item = e.Item;
            var status = OutboxStatusText(item.Status);
            _sendStatusLabel.Text = item.FileName + " - " + status;
            if (item.Status == OutboxStatus.Completed)
            {
                if (_selectedSendFilePaths.Count == 0)
                {
                    _sendFileTextBox.Text = "";
                    _confirmSendButton.Enabled = false;
                }
            }
        }

        private void FixedTokenCheckBoxChanged(object sender, EventArgs e)
        {
            var enabled = _fixedTokenCheckBox.Checked;
            SettingsStore.SaveFixedTokenSettings(enabled, _server.Token);
            SetStatus(enabled ? "\u5df2\u5f00\u542f\u56fa\u5b9a token" : "\u5df2\u5173\u95ed\u56fa\u5b9a token");
            AddLog(enabled ? "\u5df2\u5f00\u542f\u56fa\u5b9a token\uff0c\u4e0b\u6b21\u542f\u52a8\u4fdd\u6301\u4e0d\u53d8" : "\u5df2\u5173\u95ed\u56fa\u5b9a token\uff0c\u4e0b\u6b21\u542f\u52a8\u91cd\u65b0\u751f\u6210");
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
                dialog.SelectedPath = _server.PhotoUploadDirectory;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _server.PhotoUploadDirectory = dialog.SelectedPath;
                Directory.CreateDirectory(_server.PhotoUploadDirectory);
                SettingsStore.SaveUploadDirectory(_server.PhotoUploadDirectory);
                UpdateUploadDirectoryText();
                AddLog("\u7167\u7247\u4fdd\u5b58\u76ee\u5f55\u5df2\u5207\u6362: " + _server.PhotoUploadDirectory);
            }
        }

        private void ChooseFileUploadDirectory(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "\u9009\u62e9\u6587\u4ef6\u4fdd\u5b58\u76ee\u5f55";
                dialog.SelectedPath = _server.FileUploadDirectory;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _server.FileUploadDirectory = dialog.SelectedPath;
                Directory.CreateDirectory(_server.FileUploadDirectory);
                SettingsStore.SaveFileUploadDirectory(_server.FileUploadDirectory);
                UpdateFileUploadDirectoryText();
                AddLog("\u6587\u4ef6\u4fdd\u5b58\u76ee\u5f55\u5df2\u5207\u6362: " + _server.FileUploadDirectory);
            }
        }

        private void ChooseSendFile(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "\u9009\u62e9\u8981\u53d1\u9001\u5230\u624b\u673a\u7684\u6587\u4ef6";
                dialog.Multiselect = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _selectedSendFilePaths.Clear();
                _selectedSendFilePaths.AddRange(dialog.FileNames);
                UpdateSelectedSendFilesText();
                _confirmSendButton.Enabled = true;
            }
        }

        private void ConfirmSendFile(object sender, EventArgs e)
        {
            if (_selectedSendFilePaths.Count == 0)
            {
                _sendStatusLabel.Text = "\u672a\u9009\u62e9\u6587\u4ef6";
                _confirmSendButton.Enabled = false;
                return;
            }

            try
            {
                var added = 0;
                foreach (var filePath in _selectedSendFilePaths)
                {
                    if (!File.Exists(filePath))
                    {
                        throw new FileNotFoundException("\u6587\u4ef6\u4e0d\u5b58\u5728\u3002", filePath);
                    }

                    _server.AddOutboxFile(filePath);
                    added++;
                }

                _selectedSendFilePaths.Clear();
                _sendFileTextBox.Text = "";
                _sendStatusLabel.Text = "\u5df2\u52a0\u5165\u5f85\u53d1\u9001: " + added + " \u4e2a\u6587\u4ef6";
                _confirmSendButton.Enabled = false;
            }
            catch (Exception ex)
            {
                _sendStatusLabel.Text = "\u53d1\u9001\u5931\u8d25: " + ex.Message;
                AddLog(_sendStatusLabel.Text);
            }
        }

        private void UpdateSelectedSendFilesText()
        {
            if (_selectedSendFilePaths.Count == 0)
            {
                _sendFileTextBox.Text = "";
                _sendStatusLabel.Text = "\u672a\u9009\u62e9\u6587\u4ef6";
                return;
            }

            var first = new FileInfo(_selectedSendFilePaths[0]);
            _sendFileTextBox.Text = _selectedSendFilePaths.Count == 1
                ? _selectedSendFilePaths[0]
                : first.Name + " \u7b49 " + _selectedSendFilePaths.Count + " \u4e2a\u6587\u4ef6";
            _sendStatusLabel.Text = _selectedSendFilePaths.Count == 1
                ? "\u5df2\u9009\u62e9: " + first.Name + " (" + FormatBytes(first.Length) + ")"
                : "\u5df2\u9009\u62e9: " + _selectedSendFilePaths.Count + " \u4e2a\u6587\u4ef6";
        }

        private void OpenUploadDirectory(object sender, EventArgs e)
        {
            Directory.CreateDirectory(_server.PhotoUploadDirectory);
            Process.Start(_server.PhotoUploadDirectory);
        }

        private void OpenFileUploadDirectory(object sender, EventArgs e)
        {
            Directory.CreateDirectory(_server.FileUploadDirectory);
            Process.Start(_server.FileUploadDirectory);
        }

        private void UpdateUploadDirectoryText()
        {
            _uploadDirectoryTextBox.Text = _server.PhotoUploadDirectory;
        }

        private void UpdateFileUploadDirectoryText()
        {
            _fileUploadDirectoryTextBox.Text = _server.FileUploadDirectory;
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

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            var size = (double)bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }
            return size.ToString(unit == 0 ? "0" : "0.##") + " " + units[unit];
        }

        private static string OutboxStatusText(OutboxStatus status)
        {
            switch (status)
            {
                case OutboxStatus.Downloading:
                    return "\u6b63\u5728\u4e0b\u8f7d";
                case OutboxStatus.Completed:
                    return "\u5df2\u4e0b\u8f7d\u5230\u624b\u673a";
                case OutboxStatus.Failed:
                    return "\u53d1\u9001\u5931\u8d25";
                case OutboxStatus.Expired:
                    return "\u5df2\u8fc7\u671f";
                default:
                    return "\u7b49\u5f85\u624b\u673a\u63a5\u6536";
            }
        }
    }
}
