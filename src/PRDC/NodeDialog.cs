namespace PRDC;

internal sealed class NodeDialog : Form
{
    private readonly TextBox _nameTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _hostTextBox = new() { Dock = DockStyle.Fill };
    private readonly RdgNodeKind _kind;

    public NodeDialog(RdgNodeKind kind, string name = "", string hostName = "")
    {
        _kind = kind;
        Text = kind switch
        {
            RdgNodeKind.Server => "Servidor",
            RdgNodeKind.File => "Arquivo RDG",
            _ => "Grupo"
        };
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        _nameTextBox.Text = name;
        _hostTextBox.Text = hostName;

        TableLayoutPanel layout = new()
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = kind == RdgNodeKind.Server ? 3 : 2,
            Dock = DockStyle.Fill
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));

        layout.Controls.Add(new Label
        {
            Text = kind == RdgNodeKind.Server ? "Nome de exibição:" : "Nome:",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        layout.Controls.Add(_nameTextBox, 1, 0);

        if (kind == RdgNodeKind.Server)
        {
            layout.Controls.Add(new Label
            {
                Text = "Servidor / hostname:",
                AutoSize = true,
                Anchor = AnchorStyles.Left
            }, 0, 1);
            layout.Controls.Add(_hostTextBox, 1, 1);
        }

        FlowLayoutPanel buttons = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 12, 0, 0)
        };
        Button cancelButton = new() { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
        Button okButton = new() { Text = "OK", AutoSize = true };
        okButton.Click += (_, _) => Accept();
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        int buttonRow = kind == RdgNodeKind.Server ? 2 : 1;
        layout.Controls.Add(buttons, 0, buttonRow);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    public string NodeName => _nameTextBox.Text.Trim();

    public string HostName => _hostTextBox.Text.Trim();

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _nameTextBox.SelectAll();
        _nameTextBox.Focus();
    }

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(NodeName))
        {
            MessageBox.Show(this, "Informe o nome.", "PRDC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nameTextBox.Focus();
            return;
        }

        if (_kind == RdgNodeKind.Server && string.IsNullOrWhiteSpace(HostName))
        {
            MessageBox.Show(this, "Informe o servidor ou hostname.", "PRDC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _hostTextBox.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
