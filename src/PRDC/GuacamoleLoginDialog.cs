namespace PRDC;

internal sealed class GuacamoleLoginDialog : Form
{
    public const string PlaceholderGuacamoleUrl = "http://guacamole.example.local/guacamole/";

    private readonly TextBox _nameTextBox = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "ex: Guac-Prod, Guac-Hml"
    };
    private readonly TextBox _urlTextBox = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = PlaceholderGuacamoleUrl
    };
    private readonly TextBox _userTextBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _passwordTextBox = new()
    {
        Dock = DockStyle.Fill,
        UseSystemPasswordChar = true
    };

    public GuacamoleLoginDialog(string? defaultUrl = null, string? defaultDisplayName = null)
    {
        Text = "Login no Guacamole";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        _nameTextBox.Text = defaultDisplayName ?? string.Empty;
        _urlTextBox.Text = defaultUrl ?? string.Empty;

        TableLayoutPanel layout = new()
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 5,
            Dock = DockStyle.Fill
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));

        layout.Controls.Add(new Label
        {
            Text = "Nome do portal:",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        layout.Controls.Add(_nameTextBox, 1, 0);

        layout.Controls.Add(new Label
        {
            Text = "URL do Guacamole:",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 1);
        layout.Controls.Add(_urlTextBox, 1, 1);

        layout.Controls.Add(new Label
        {
            Text = "Usuário:",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 2);
        layout.Controls.Add(_userTextBox, 1, 2);

        layout.Controls.Add(new Label
        {
            Text = "Senha:",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 3);
        layout.Controls.Add(_passwordTextBox, 1, 3);

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

        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        ThemeManager.ApplyTheme(this);
    }

    public string PortalDisplayName => string.IsNullOrWhiteSpace(_nameTextBox.Text)
        ? "Guacamole"
        : _nameTextBox.Text.Trim();

    public string ServerUrl => _urlTextBox.Text.Trim();

    public string UserName => _userTextBox.Text.Trim();

    public string Password => _passwordTextBox.Text;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (string.IsNullOrWhiteSpace(_urlTextBox.Text))
        {
            _urlTextBox.Focus();
        }
        else
        {
            _userTextBox.Focus();
        }
    }

    private void Accept()
    {
        try
        {
            _ = GuacamoleInventoryProvider.NormalizeBaseUri(ServerUrl);
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(this, exception.Message, "PRDC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _urlTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(UserName))
        {
            MessageBox.Show(this, "Informe o usuário.", "PRDC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _userTextBox.Focus();
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            MessageBox.Show(this, "Informe a senha.", "PRDC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _passwordTextBox.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

