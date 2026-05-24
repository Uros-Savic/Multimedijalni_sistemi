using System.Drawing;
using MSI.Client.Services;

namespace MSI.Client;

public sealed class BatchFilterDialog : Form
{
    public List<FilterStep> SelectedSteps { get; private set; } = new();
    private ListView lstChain = null!;
    private ComboBox cmbFilter = null!;
    private TextBox txtParams = null!;
    private Button btnAdd = null!;
    private Button btnRemove = null!;
    private Button btnUp = null!;
    private Button btnDown = null!;
    private Button btnOk = null!;
    private Button btnCancel = null!;

    private static readonly string[] FilterNames = {
        "invert","contrast","mean_removal","edge_enhance",
        "sphere","pixelate","sierra","cross_domain_colorize"
    };

    private static readonly Dictionary<string, string> DefaultParams = new()
    {
        ["invert"] = "",
        ["contrast"] = "factor=1.5",
        ["mean_removal"] = "strength=1.0",
        ["edge_enhance"] = "strength=1.0",
        ["sphere"] = "radius=1.0",
        ["pixelate"] = "block_size=10",
        ["sierra"] = "levels=2",
        ["cross_domain_colorize"] = "hue_shift=0.0,saturation=0.9",
    };

    public BatchFilterDialog()
    {
        Text = "Batch – lanac filtera";
        Size = new Size(560, 480);
        MinimumSize = new Size(480, 400);
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = Color.FromArgb(36, 36, 44);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Tahoma", 9f);
        StartPosition = FormStartPosition.CenterParent;

        BuildUi();
    }

    private void BuildUi()
    {
        var lblTitle = new Label
        {
            Text = "Dodajte filtere i poredjajte ih redom primene:",
            Dock = DockStyle.Top,
            Height = 26,
            ForeColor = Color.LightGray,
            Font = new Font("Tahoma", 9f, FontStyle.Italic)
        };

        lstChain = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.FromArgb(25, 25, 32),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        lstChain.Columns.Add("#", 40);
        lstChain.Columns.Add("Filter", 150);
        lstChain.Columns.Add("Parametri", 280);

        var pnlAdd = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 70,
            BackColor = Color.FromArgb(40, 40, 50),
            Padding = new Padding(6)
        };

        var lblFilter = new Label { Text = "Filter:", ForeColor = Color.LightGray, Width = 50, Location = new Point(6, 8) };
        cmbFilter = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(55, 55, 68),
            ForeColor = Color.White,
            Location = new Point(58, 5),
            Width = 180
        };
        cmbFilter.Items.AddRange(FilterNames);
        cmbFilter.SelectedIndex = 0;
        cmbFilter.SelectedIndexChanged += (_, _) =>
        {
            string name = cmbFilter.SelectedItem?.ToString() ?? "";
            txtParams.Text = DefaultParams.GetValueOrDefault(name, "");
        };

        var lblP = new Label { Text = "Params:", ForeColor = Color.LightGray, Width = 55, Location = new Point(6, 40) };
        txtParams = new TextBox
        {
            Location = new Point(62, 37),
            Width = 310,
            BackColor = Color.FromArgb(55, 55, 68),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Text = DefaultParams[FilterNames[0]]
        };

        btnAdd = new Button
        {
            Text = "+ Dodaj",
            Location = new Point(380, 5),
            Size = new Size(80, 26),
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnAdd.Click += BtnAdd_Click;



        pnlAdd.Controls.AddRange(new Control[]
            { lblFilter, cmbFilter, lblP, txtParams, btnAdd });
        var pnlRight = new Panel { Dock = DockStyle.Right, Width = 90, BackColor = Color.FromArgb(36, 36, 44) };

        btnUp = MakeBtn("Gore", Color.FromArgb(70, 70, 90));
        btnDown = MakeBtn("Dole", Color.FromArgb(70, 70, 90));
        btnRemove = MakeBtn("Brisi", Color.FromArgb(140, 40, 40));

        btnUp.Click += (_, _) => MoveItem(-1);
        btnDown.Click += (_, _) => MoveItem(+1);
        btnRemove.Click += (_, _) => RemoveSelected();

        var flowRight = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(6),
            AutoSize = false
        };
        flowRight.Controls.AddRange(new Control[] { btnUp, btnDown, btnRemove });
        pnlRight.Controls.Add(flowRight);

        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Color.FromArgb(28, 28, 35) };

        btnOk = new Button
        {
            Text = "▶ Primeni lanac",
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 130, 80),
            ForeColor = Color.White,
            Size = new Size(140, 30),
            Location = new Point(6, 5),
            Cursor = Cursors.Hand
        };
        btnOk.Click += BtnOk_Click;

        btnCancel = new Button
        {
            Text = "Odustani",
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(70, 70, 80),
            ForeColor = Color.White,
            Size = new Size(90, 30),
            Location = new Point(154, 5),
            Cursor = Cursors.Hand
        };

        pnlBottom.Controls.AddRange(new Control[] { btnOk, btnCancel });
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        var pnlCenter = new Panel { Dock = DockStyle.Fill };
        pnlCenter.Controls.Add(lstChain);
        pnlCenter.Controls.Add(pnlRight);

        Controls.Add(pnlCenter);
        Controls.Add(pnlAdd);
        Controls.Add(pnlBottom);
        Controls.Add(lblTitle);
    }

    private void BtnAdd_Click(object? s, EventArgs e)
    {
        string name = cmbFilter.SelectedItem?.ToString() ?? "";
        string pStr = txtParams.Text.Trim();
        var prms = ParseParams(pStr);

        int idx = lstChain.Items.Count + 1;
        var item = new ListViewItem(idx.ToString());
        item.SubItems.Add(name);
        item.SubItems.Add(pStr);
        item.Tag = new FilterStep(name, prms);
        lstChain.Items.Add(item);
        RenumberItems();
    }

    private void BtnOk_Click(object? s, EventArgs e)
    {
        if (lstChain.Items.Count == 0)
        {
            MessageBox.Show("Dodajte barem jedan filter.", "Upozorenje",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        SelectedSteps = lstChain.Items
            .Cast<ListViewItem>()
            .Select(i => (FilterStep)i.Tag!)
            .ToList();
    }

    private void RemoveSelected()
    {
        foreach (ListViewItem item in lstChain.SelectedItems)
            lstChain.Items.Remove(item);
        RenumberItems();
    }

    private void MoveItem(int dir)
    {
        if (lstChain.SelectedItems.Count == 0) return;
        var item = lstChain.SelectedItems[0];
        int idx = item.Index;
        int newIdx = idx + dir;
        if (newIdx < 0 || newIdx >= lstChain.Items.Count) return;
        lstChain.Items.RemoveAt(idx);
        lstChain.Items.Insert(newIdx, item);
        lstChain.Items[newIdx].Selected = true;
        RenumberItems();
    }

    private void RenumberItems()
    {
        for (int i = 0; i < lstChain.Items.Count; i++)
            lstChain.Items[i].Text = (i + 1).ToString();
    }

    private static Dictionary<string, string> ParseParams(string raw)
    {
        var dict = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(raw)) return dict;
        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
                dict[parts[0].Trim()] = parts[1].Trim();
        }
        return dict;
    }

    private static Button MakeBtn(string text, Color bg) => new()
    {
        Text = text,
        BackColor = bg,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Width = 78,
        Height = 28,
        Margin = new Padding(0, 4, 0, 0),
        Cursor = Cursors.Hand
    };
}
