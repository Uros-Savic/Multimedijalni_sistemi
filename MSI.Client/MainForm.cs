using System.Drawing;
using System.Drawing.Imaging;
using MSI.Client.Services;
using MSI.Core.MsiFormat;

namespace MSI.Client;

public sealed partial class MainForm : Form
{
    private readonly MsiApiClient _api;
    private readonly UndoRedoManager _history = new();
    private readonly ClientLogger _log;
    private Bitmap? _currentImage;
    private Bitmap? _originalImage;
    private string _sessionId = string.Empty;
    private string _currentLabel = "original";
    private bool _compareMode = false;
    private float _zoom = 1.0f;
    private Point _panOffset = Point.Empty;
    private Point _panStart = Point.Empty;
    private bool _isPanning = false;
    private const float ZoomMin = 0.05f, ZoomMax = 10f, ZoomStep = 0.15f;
    private const int PanStep = 30;
    private const string DefaultServer = "http://localhost:5000";
    private Panel pnlCanvas = null!;
    private SplitContainer splitMain = null!;
    private Button btnOpen = null!;
    private Button btnUndo = null!;
    private Button btnRedo = null!;
    private Button btnCompare = null!;
    private Button btnExport = null!;
    private Button btnBatch = null!;
    private Button btnResetZoom = null!;
    private Label lblZoom = null!;
    private Label lblInfo = null!;
    private Label lblResult = null!;
    private ComboBox cmbFilter = null!;
    private GroupBox grpParams = null!;
    private TableLayoutPanel tblParams = null!;
    private TextBox txtLog = null!;
    private StatusStrip statusBar = null!;
    private ToolStripStatusLabel statusLabel = null!;
    private Button btnApplyFilter = null!;
    private ComboBox cmbColorspace = null!;
    private ComboBox cmbCompression = null!;

    public MainForm()
    {
        _log = new ClientLogger();
        _api = new MsiApiClient(DefaultServer);

        Text = "MSI Image Client";
        Size = new Size(1280, 800);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(30, 30, 35);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Tahoma", 9f);
        DoubleBuffered = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;

        BuildStatusBar();
        BuildToolbar();
        BuildMainSplit();

        KeyDown += MainForm_KeyDown;
        Load += MainForm_Load;

        _log.OnLog += line => SafeInvoke(() =>
        {
            if (txtLog.Lines.Length > 300)
                txtLog.Text = string.Join(Environment.NewLine, txtLog.Lines.Skip(100));
            txtLog.AppendText(line + Environment.NewLine);
        });

        UpdateButtonStates();
        _log.Info("MSI klijent pokrenut.");
    }

    private void MainForm_Load(object? s, EventArgs e)
    {
        try
        {
            int target = ClientSize.Width - 280;
            if (target > splitMain.Panel1MinSize && target < splitMain.Width - splitMain.Panel2MinSize)
                splitMain.SplitterDistance = target;
        }
        catch { }
    }

    private void BuildStatusBar()
    {
        statusBar = new StatusStrip { BackColor = Color.FromArgb(20, 20, 25), ForeColor = Color.LightGray };
        statusLabel = new ToolStripStatusLabel("Spreman.");
        statusBar.Items.Add(statusLabel);
        Controls.Add(statusBar);
    }

    private void BuildToolbar()
    {
        var pnlToolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 46,
            BackColor = Color.FromArgb(40, 40, 48),
            Padding = new Padding(4, 4, 4, 4)
        };

        btnOpen = MakeBtn("Otvori", Color.FromArgb(0, 120, 215), 90);
        btnUndo = MakeBtn("Undo", Color.FromArgb(70, 70, 88), 80);
        btnRedo = MakeBtn("Redo", Color.FromArgb(70, 70, 88), 80);
        btnCompare = MakeBtn("Poredi", Color.FromArgb(50, 100, 50), 85);
        btnBatch = MakeBtn("Batch", Color.FromArgb(90, 50, 130), 76);
        btnExport = MakeBtn("Export", Color.FromArgb(0, 140, 90), 80);
        btnResetZoom = MakeBtn("100%", Color.FromArgb(60, 60, 75), 38);

        lblZoom = new Label
        {
            Text = "100%",
            ForeColor = Color.LightGray,
            Width = 44,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Margin = new Padding(2, 0, 2, 0)
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Padding = new Padding(2, 3, 0, 0)
        };
        flow.Controls.Add(btnOpen); flow.Controls.Add(Sep());
        flow.Controls.Add(btnUndo); flow.Controls.Add(btnRedo); flow.Controls.Add(Sep());
        flow.Controls.Add(btnCompare); flow.Controls.Add(btnBatch); flow.Controls.Add(btnExport); flow.Controls.Add(Sep());
        flow.Controls.Add(btnResetZoom); flow.Controls.Add(lblZoom);

        pnlToolbar.Controls.Add(flow);
        Controls.Add(pnlToolbar);

        btnOpen.Click += BtnOpen_Click;
        btnUndo.Click += BtnUndo_Click;
        btnRedo.Click += BtnRedo_Click;
        btnCompare.Click += BtnCompare_Click;
        btnBatch.Click += BtnBatch_Click;
        btnExport.Click += BtnExport_Click;
        btnResetZoom.Click += (_, _) =>
        {
            _zoom = 1f; _panOffset = Point.Empty; lblZoom.Text = "100%"; pnlCanvas.Invalidate();
        };
    }

    private void BuildMainSplit()
    {
        splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Color.FromArgb(30, 30, 35),
            FixedPanel = FixedPanel.Panel2
        };

        BuildCanvas();
        BuildSidebar();
        Controls.Add(splitMain);
        splitMain.BringToFront();
    }

    private void BuildCanvas()
    {
        pnlCanvas = new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 18, 22)
        };
        pnlCanvas.Paint += PnlCanvas_Paint;
        pnlCanvas.MouseDown += PnlCanvas_MouseDown;
        pnlCanvas.MouseMove += PnlCanvas_MouseMove;
        pnlCanvas.MouseUp += PnlCanvas_MouseUp;
        pnlCanvas.MouseWheel += PnlCanvas_MouseWheel;

        lblResult = new Label
        {
            Text = "",
            ForeColor = Color.LightGreen,
            Font = new Font("Tahoma", 7.5f),
            Dock = DockStyle.Bottom,
            Height = 16,
            BackColor = Color.FromArgb(18, 18, 22),
            TextAlign = ContentAlignment.MiddleRight,
            Visible = false
        };

        splitMain.Panel1.Controls.Add(pnlCanvas);
        splitMain.Panel1.Controls.Add(lblResult);
    }

    private void BuildSidebar()
    {
        var sidebar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(38, 38, 46),
            Padding = new Padding(8, 6, 8, 6)
        };

        var lblFilter = new Label
        { Text = "Filter:", ForeColor = Color.Silver, Dock = DockStyle.Top, Height = 18 };

        cmbFilter = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(52, 52, 65),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        cmbFilter.Items.AddRange(new object[]
        {
            "invert","contrast","mean_removal","edge_enhance",
            "sphere","pixelate","sierra","cross_domain_colorize"
        });
        cmbFilter.SelectedIndex = 0;
        cmbFilter.SelectedIndexChanged += CmbFilter_SelectedIndexChanged;

        grpParams = new GroupBox
        {
            Text = "Parametri",
            ForeColor = Color.Silver,
            Dock = DockStyle.Top,
            Height = 155,
            BackColor = Color.FromArgb(38, 38, 46)
        };
        tblParams = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            BackColor = Color.FromArgb(38, 38, 46)
        };
        tblParams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        tblParams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        grpParams.Controls.Add(tblParams);

        btnApplyFilter = MakeBtn("Primeni Filter", Color.FromArgb(0, 115, 200), 180);
        btnApplyFilter.Dock = DockStyle.Top;
        btnApplyFilter.Height = 34;
        btnApplyFilter.Click += BtnApplyFilter_Click;

        var grpMsi = new GroupBox
        {
            Text = "MSI export opcije",
            ForeColor = Color.Silver,
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = Color.FromArgb(38, 38, 46)
        };
        var tblMsi = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 2,
            BackColor = Color.FromArgb(38, 38, 46),
            Padding = new Padding(4)
        };
        tblMsi.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        tblMsi.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tblMsi.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        tblMsi.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        tblMsi.Controls.Add(new Label
        {
            Text = "Colorspace:",
            ForeColor = Color.LightGray,
            Font = new Font("Tahoma", 8f),
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill
        }, 0, 0);

        cmbColorspace = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(52, 52, 65),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Tahoma", 8f)
        };
        cmbColorspace.Items.Add("RGB (1)");
        cmbColorspace.Items.Add("HSV (3)");
        cmbColorspace.Items.Add("Linear/grayscale (0)");
        cmbColorspace.SelectedIndex = 0;
        tblMsi.Controls.Add(cmbColorspace, 1, 0);

        tblMsi.Controls.Add(new Label
        {
            Text = "Kompresija:",
            ForeColor = Color.LightGray,
            Font = new Font("Tahoma", 8f),
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill
        }, 0, 1);

        cmbCompression = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(52, 52, 65),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Tahoma", 8f)
        };
        cmbCompression.Items.Add("None (0)");
        cmbCompression.Items.Add("Huffman (2)");
        cmbCompression.Items.Add("MPEG-2 (4)");
        cmbCompression.SelectedIndex = 0;
        tblMsi.Controls.Add(cmbCompression, 1, 1);
        grpMsi.Controls.Add(tblMsi);

        lblInfo = new Label
        {
            Dock = DockStyle.Top,
            Height = 64,
            ForeColor = Color.LightGray,
            Font = new Font("Tahoma", 8f),
            Text = "Ucitajte sliku  (Ctrl+O)",
            BackColor = Color.FromArgb(26, 26, 34),
            Padding = new Padding(4)
        };

        var lblLog = new Label
        {
            Text = "Log",
            Dock = DockStyle.Top,
            Height = 16,
            ForeColor = Color.DimGray,
            Font = new Font("Tahoma", 7f),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(22, 22, 28)
        };
        txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 18, 24),
            ForeColor = Color.FromArgb(120, 200, 120),
            Font = new Font("Courier New", 9f),
            BorderStyle = BorderStyle.None
        };
        var logPanel = new Panel { Dock = DockStyle.Fill };
        logPanel.Controls.Add(txtLog);
        logPanel.Controls.Add(lblLog);

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 7,
            ColumnCount = 1
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tbl.Controls.Add(lblFilter, 0, 0);
        tbl.Controls.Add(cmbFilter, 0, 1);
        tbl.Controls.Add(grpParams, 0, 2);
        tbl.Controls.Add(btnApplyFilter, 0, 3);
        tbl.Controls.Add(grpMsi, 0, 4);
        tbl.Controls.Add(lblInfo, 0, 5);
        tbl.Controls.Add(logPanel, 0, 6);

        sidebar.Controls.Add(tbl);
        splitMain.Panel2.Controls.Add(sidebar);

        CmbFilter_SelectedIndexChanged(null, EventArgs.Empty);
    }

    private static readonly Dictionary<string, (string Label, string Key, string Def)[]>
        ParamDefs = new()
        {
            ["invert"] = Array.Empty<(string, string, string)>(),
            ["contrast"] = new[] { ("Faktor (0.1-10)", "factor", "1.5") },
            ["mean_removal"] = new[] { ("Jacina (0-2)", "strength", "1.0") },
            ["edge_enhance"] = new[] { ("Jacina (0-3)", "strength", "1.0") },
            ["sphere"] = new[] { ("Radijus (0-2)", "radius", "1.0") },
            ["pixelate"] = new[] { ("Vel. bloka", "block_size", "10") },
            ["sierra"] = new[] { ("Nivoi (2-16)", "levels", "2") },
            ["cross_domain_colorize"] = new[]
            {
                ("Hue shift (0-1)", "hue_shift",  "0.0"),
                ("Zasicenje (0-1)", "saturation", "0.9")
            },
        };

    private void CmbFilter_SelectedIndexChanged(object? s, EventArgs e)
    {
        tblParams.Controls.Clear(); tblParams.RowStyles.Clear(); tblParams.RowCount = 0;
        string name = cmbFilter.SelectedItem?.ToString() ?? "";
        if (!ParamDefs.TryGetValue(name, out var defs) || defs.Length == 0)
        {
            tblParams.Controls.Add(new Label
            { Text = "(nema parametara)", ForeColor = Color.Gray, AutoSize = true });
            return;
        }
        foreach (var (label, key, def) in defs)
        {
            tblParams.RowCount++;
            tblParams.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            tblParams.Controls.Add(new Label
            {
                Text = label,
                ForeColor = Color.LightGray,
                Dock = DockStyle.Fill,
                Font = new Font("Tahoma", 8f),
                TextAlign = ContentAlignment.MiddleLeft
            });
            tblParams.Controls.Add(new TextBox
            {
                Name = $"p_{key}",
                Text = def,
                Tag = key,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(50, 50, 64),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            });
        }
    }

    private Dictionary<string, string> CollectParameters()
    {
        var d = new Dictionary<string, string>();
        foreach (Control c in tblParams.Controls)
            if (c is TextBox tb && tb.Tag is string k)
                d[k] = tb.Text.Trim();
        return d;
    }

    private void PnlCanvas_Paint(object? s, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.FromArgb(18, 18, 22));

        Bitmap? img = _compareMode ? _originalImage : _currentImage;
        if (img == null)
        {
            using var f = new Font("Tahoma", 13f);
            string hint = "Ucitajte sliku  -  Ctrl+O";
            var sz = g.MeasureString(hint, f);
            g.DrawString(hint, f, Brushes.DimGray,
                (pnlCanvas.Width - sz.Width) / 2f,
                (pnlCanvas.Height - sz.Height) / 2f);
            return;
        }

        float dw = img.Width * _zoom;
        float dh = img.Height * _zoom;
        float ox = (pnlCanvas.Width - dw) / 2f + _panOffset.X;
        float oy = (pnlCanvas.Height - dh) / 2f + _panOffset.Y;

        g.InterpolationMode = _zoom >= 1
            ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor
            : System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(img, ox, oy, dw, dh);

        if (_compareMode && _currentImage != null && _originalImage != null)
        {
            int mid = pnlCanvas.Width / 2;
            g.SetClip(new Rectangle(mid, 0, pnlCanvas.Width - mid, pnlCanvas.Height));
            g.DrawImage(_currentImage, ox, oy, dw, dh);
            g.ResetClip();
            using var p = new Pen(Color.Yellow, 2);
            g.DrawLine(p, mid, 0, mid, pnlCanvas.Height);
        }

        using var bp = new Pen(Color.FromArgb(65, 65, 80));
        g.DrawRectangle(bp, ox - 1, oy - 1, dw + 2, dh + 2);
    }

    private void PnlCanvas_MouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        { _isPanning = true; _panStart = e.Location; pnlCanvas.Cursor = Cursors.SizeAll; }
    }
    private void PnlCanvas_MouseMove(object? s, MouseEventArgs e)
    {
        if (!_isPanning) return;
        _panOffset.X += e.X - _panStart.X;
        _panOffset.Y += e.Y - _panStart.Y;
        _panStart = e.Location;
        pnlCanvas.Invalidate();
    }
    private void PnlCanvas_MouseUp(object? s, MouseEventArgs e)
    { _isPanning = false; pnlCanvas.Cursor = Cursors.Default; }

    private void PnlCanvas_MouseWheel(object? s, MouseEventArgs e)
    {
        if (ModifierKeys.HasFlag(Keys.Control))
            _panOffset.Y += e.Delta > 0 ? PanStep : -PanStep;
        else
        {
            _zoom = Math.Clamp(_zoom + (e.Delta > 0 ? ZoomStep : -ZoomStep), ZoomMin, ZoomMax);
            lblZoom.Text = $"{_zoom * 100:F0}%";
        }
        pnlCanvas.Invalidate();
    }

    private void MainForm_KeyDown(object? s, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Z) { BtnUndo_Click(null, EventArgs.Empty); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.Y) { BtnRedo_Click(null, EventArgs.Empty); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.O) { BtnOpen_Click(null, EventArgs.Empty); e.Handled = true; return; }
        if (e.Control && e.KeyCode == Keys.S) { BtnExport_Click(null, EventArgs.Empty); e.Handled = true; return; }

        if (e.Control)
        {
            switch (e.KeyCode)
            {
                case Keys.Left: _panOffset.X += PanStep; pnlCanvas.Invalidate(); e.Handled = true; return;
                case Keys.Right: _panOffset.X -= PanStep; pnlCanvas.Invalidate(); e.Handled = true; return;
                case Keys.Up: _panOffset.Y += PanStep; pnlCanvas.Invalidate(); e.Handled = true; return;
                case Keys.Down: _panOffset.Y -= PanStep; pnlCanvas.Invalidate(); e.Handled = true; return;
            }
        }

        bool zin = !e.Control && (e.KeyCode is Keys.Add or Keys.Oemplus);
        bool zout = !e.Control && (e.KeyCode is Keys.Subtract or Keys.OemMinus);
        if (zin || zout)
        {
            _zoom = Math.Clamp(_zoom + (zin ? ZoomStep : -ZoomStep), ZoomMin, ZoomMax);
            lblZoom.Text = $"{_zoom * 100:F0}%";
            pnlCanvas.Invalidate(); e.Handled = true;
        }
    }

    private async void BtnOpen_Click(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Otvori sliku",
            Filter = "Slike|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.msi|Svi fajlovi|*.*"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        await UploadFileAsync(dlg.FileName);
    }

    private async Task UploadFileAsync(string path)
    {
        SetStatus("Uploadujem...");
        SetBusy(true);
        try
        {
            _api.BaseUrl = DefaultServer;

            var fi = new FileInfo(path);
            if (fi.Length > 20L * 1024 * 1024)
            {
                ShowErr($"Fajl je prevelik: {fi.Length / 1024.0 / 1024.0:F1} MB\nMaksimum je 20 MB.");
                SetStatus("Upload odbijen - prevelik fajl.");
                return;
            }

            if (!string.IsNullOrEmpty(_sessionId))
            {
                await _api.DeleteSessionAsync(_sessionId);
                _log.Info($"Stara sesija obrisana.");
            }

            var resp = await _api.UploadAsync(path);
            _sessionId = resp.SessionId;

            _currentImage?.Dispose();
            _originalImage?.Dispose();

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".msi")
            {
                try
                {
                    byte[] msiBytes = await File.ReadAllBytesAsync(path);
                    var (bmp, _) = new MsiDecoder().Decode(msiBytes);
                    _currentImage = bmp;
                    _originalImage = new Bitmap(bmp);
                }
                catch (Exception decodeEx)
                {
                    _log.Warn($"MSI dekodiranje neuspjesno: {decodeEx.Message}");
                    _currentImage = new Bitmap(Math.Max(resp.Width, 1), Math.Max(resp.Height, 1),
                                                PixelFormat.Format24bppRgb);
                    _originalImage = new Bitmap(_currentImage);
                }
            }
            else
            {
                _currentImage = new Bitmap(path);
                _originalImage = new Bitmap(path);
            }

            _history.Clear();
            _compareMode = false;
            _currentLabel = "original";
            lblResult.Visible = false;
            FitToWindow();
            lblInfo.Text =
                $"Dim: {resp.Width}x{resp.Height}  {resp.Format}\n" +
                $"{resp.FileSizeBytes / 1024.0:F1} KB\n" +
                $"Sesija: {resp.SessionId[..8]}...";

            SetStatus($"OK - {resp.Width}x{resp.Height}");
            _log.Info($"Upload: {Path.GetFileName(path)} {resp.Width}x{resp.Height}");
        }
        catch (Exception ex)
        {
            _log.Error("Upload", ex);
            ShowErr($"Greska pri uploadu:\n{ex.Message}");
            SetStatus("Upload neuspesan.");
        }
        finally { SetBusy(false); UpdateButtonStates(); pnlCanvas.Invalidate(); }
    }

    private async void BtnApplyFilter_Click(object? s, EventArgs e)
    {
        if (_currentImage == null || string.IsNullOrEmpty(_sessionId))
        { ShowErr("Ucitajte sliku."); return; }

        string name = cmbFilter.SelectedItem?.ToString() ?? "";
        SetStatus($"Primjenjujem '{name}'...");
        SetBusy(true);
        try
        {
            _history.Push(_currentImage, _currentLabel, _sessionId);

            var req = new FilterRequest(
                _sessionId,
                new List<FilterStep> { new(name, CollectParameters()) },
                "png",
                GetSelectedCompression(),
                GetSelectedColorspace());
            var resp = await _api.ApplyFiltersAsync(req);

            var parts = resp.DownloadUrl.Split('/');
            byte[] bytes = await _api.DownloadAsync(_sessionId, parts[^2], "png");

            _currentImage?.Dispose();
            using var ms = new MemoryStream(bytes);
            _currentImage = new Bitmap(ms);
            _currentLabel = name;

            lblResult.Text = $"  <- {name.ToUpper()}  {resp.ProcessingMs}ms";
            lblResult.Visible = true;
            pnlCanvas.Invalidate();
            UpdateButtonStates();
            SetStatus($"'{name}' za {resp.ProcessingMs}ms");
            _log.Info($"Filter '{name}' OK | {resp.ProcessingMs}ms");
        }
        catch (Exception ex)
        {
            _log.Error($"Filter '{name}'", ex);
            ShowErr($"Greska:\n{ex.Message}");
            SetStatus("Filter neuspesan.");
        }
        finally { SetBusy(false); }
    }

    private async void BtnUndo_Click(object? s, EventArgs e)
    {
        if (_currentImage == null) return;
        var entry = _history.Undo(_currentImage, _currentLabel, _sessionId);
        if (entry == null) return;
        _currentImage?.Dispose();
        _currentImage = entry.Snapshot;
        _currentLabel = entry.Label;
        _sessionId = entry.SessionId;
        pnlCanvas.Invalidate();
        UpdateButtonStates();
        SetStatus($"Undo -> '{entry.Label}'");
        await SyncCurrentToServerAsync();
    }

    private async void BtnRedo_Click(object? s, EventArgs e)
    {
        if (_currentImage == null) return;
        var entry = _history.Redo(_currentImage, _currentLabel, _sessionId);
        if (entry == null) return;
        _currentImage?.Dispose();
        _currentImage = entry.Snapshot;
        _currentLabel = entry.Label;
        _sessionId = entry.SessionId;
        pnlCanvas.Invalidate();
        UpdateButtonStates();
        SetStatus($"Redo -> '{entry.Label}'");
        await SyncCurrentToServerAsync();
    }

    private async Task SyncCurrentToServerAsync()
    {
        if (_currentImage == null || string.IsNullOrEmpty(_sessionId)) return;
        try
        {
            using var ms = new MemoryStream();
            _currentImage.Save(ms, ImageFormat.Png);
            await _api.RestoreCurrentAsync(_sessionId, ms.ToArray());
            _log.Info($"Server sync OK -> '{_currentLabel}'");
        }
        catch (Exception ex)
        {
            _log.Warn($"Server sync neuspesan: {ex.Message}");
        }
    }

    private void BtnCompare_Click(object? s, EventArgs e)
    {
        _compareMode = !_compareMode;
        btnCompare.BackColor = _compareMode ? Color.FromArgb(80, 140, 40) : Color.FromArgb(50, 100, 50);
        btnCompare.Text = _compareMode ? "Zatvori" : "Poredi";
        pnlCanvas.Invalidate();
    }

    private async void BtnExport_Click(object? s, EventArgs e)
    {
        if (_currentImage == null) { ShowErr("Nema slike."); return; }
        using var dlg = new SaveFileDialog
        {
            Title = "Sacuvaj sliku",
            Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp|GIF|*.gif|MSI format|*.msi",
            DefaultExt = "png",
            FileName = $"output_{DateTime.Now:yyyyMMdd_HHmmss}"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant().TrimStart('.');
            if (ext == "msi")
            {
                byte cs = GetSelectedColorspace();
                byte comp = GetSelectedCompression();
                _log.Info($"MSI export pocinje: colorspace={cs} compression={comp}");
                byte[] msiBytes = new MsiEncoder().EncodeToBytes(_currentImage, cs, comp);
                await File.WriteAllBytesAsync(dlg.FileName, msiBytes);
                _log.Info($"MSI export zavrseno: {msiBytes.Length / 1024}KB -> {Path.GetFileName(dlg.FileName)}");
            }
            else
            {
                var fmt = ext switch
                {
                    "jpg" => ImageFormat.Jpeg,
                    "bmp" => ImageFormat.Bmp,
                    "gif" => ImageFormat.Gif,
                    _ => ImageFormat.Png
                };
                _currentImage.Save(dlg.FileName, fmt);
                _log.Info($"Export: {dlg.FileName}");
            }
            SetStatus($"Sacuvano: {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex) { _log.Error("Export", ex); ShowErr($"Export greska:\n{ex.Message}"); }
    }

    private byte GetSelectedColorspace() => cmbColorspace.SelectedIndex switch
    {
        1 => MsiConstants.CS_HSV,
        2 => MsiConstants.CS_LINEAR,
        _ => MsiConstants.CS_RGB
    };

    private byte GetSelectedCompression() => cmbCompression.SelectedIndex switch
    {
        1 => MsiConstants.COMP_HUFFMAN,
        2 => MsiConstants.COMP_MPEG2,
        _ => MsiConstants.COMP_NONE
    };

    private void FitToWindow()
    {
        if (_currentImage == null || pnlCanvas.Width <= 0 || pnlCanvas.Height <= 0) return;
        float zx = (float)pnlCanvas.Width / _currentImage.Width;
        float zy = (float)pnlCanvas.Height / _currentImage.Height;
        _zoom = Math.Min(zx, zy) * 0.92f;
        _panOffset = Point.Empty;
        lblZoom.Text = $"{_zoom * 100:F0}%";
    }

    private void UpdateButtonStates()
    {
        bool has = _currentImage != null;
        bool hasSid = !string.IsNullOrEmpty(_sessionId);
        btnApplyFilter.Enabled = has && hasSid;
        btnExport.Enabled = has;
        btnCompare.Enabled = has;
        btnBatch.Enabled = has && hasSid;
        btnUndo.Enabled = _history.CanUndo;
        btnRedo.Enabled = _history.CanRedo;
        btnUndo.Text = _history.CanUndo ? $"Undo ({_history.CurrentUndoLabel})" : "Undo";
        btnRedo.Text = _history.CanRedo ? $"Redo ({_history.CurrentRedoLabel})" : "Redo";
    }

    private void SetStatus(string msg) =>
        SafeInvoke(() => statusLabel.Text = $"  {msg}");

    private void SetBusy(bool busy) =>
        SafeInvoke(() =>
        {
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            btnApplyFilter.Enabled = !busy && _currentImage != null;
            btnBatch.Enabled = !busy && _currentImage != null;
        });

    private static void ShowErr(string msg) =>
        MessageBox.Show(msg, "Greska", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private void SafeInvoke(Action a)
    {
        if (IsDisposed) return;
        try { if (InvokeRequired) BeginInvoke(a); else a(); }
        catch { }
    }

    private static Button MakeBtn(string text, Color bg, int width = 0) => new()
    {
        Text = text,
        BackColor = bg,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Height = 28,
        Width = width > 0 ? width : text.Length * 7 + 16,
        AutoSize = false,
        FlatAppearance = { BorderColor = Color.FromArgb(55, 55, 68), BorderSize = 1 },
        Cursor = Cursors.Hand,
        Margin = new Padding(2, 0, 2, 0)
    };

    private static Label Sep() => new()
    {
        Text = "|",
        ForeColor = Color.FromArgb(55, 55, 68),
        Width = 8,
        TextAlign = ContentAlignment.MiddleCenter,
        AutoSize = false,
        Margin = new Padding(1, 0, 1, 0)
    };

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_sessionId))
        {
            try
            {
                _api.DeleteSessionAsync(_sessionId)
                    .Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
            _log.Info($"Sesija obrisana pri zatvaranju.");
        }

        _currentImage?.Dispose();
        _originalImage?.Dispose();
        _history.Dispose();
        _api.Dispose();
        base.OnFormClosed(e);
    }
}

internal sealed class DoubleBufferedPanel : Panel
{
    public DoubleBufferedPanel() => DoubleBuffered = true;
}