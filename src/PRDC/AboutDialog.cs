using System.Diagnostics;

namespace PRDC;

internal sealed class AboutDialog : Form
{
    private const string AuthorUrl = "https://github.com/pcmbezerra";
    private const string RepositoryUrl = "https://github.com/pcmbezerra/PRDC";

    public AboutDialog(Icon? applicationIcon)
    {
        Text = "Sobre o PRDC";
        if (applicationIcon is not null)
        {
            Icon = (Icon)applicationIcon.Clone();
        }
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 270);
        Padding = new Padding(20);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label
        {
            Text = "PRDC",
            AutoSize = true,
            Font = new Font(
                SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif,
                20,
                FontStyle.Bold)
        });
        layout.Controls.Add(new Label
        {
            Text = $"Versão {Application.ProductVersion}",
            AutoSize = true,
            Margin = new Padding(3, 4, 3, 12)
        });
        layout.Controls.Add(new Label
        {
            Text = "Gerenciador open source de conexões RDP para Windows.",
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 12)
        });
        layout.Controls.Add(new Label
        {
            Text = "Autor: Paulo Bezerra (@pcmbezerra)",
            AutoSize = true
        });

        LinkLabel authorLink = new()
        {
            Text = AuthorUrl,
            AutoSize = true,
            Margin = new Padding(3, 4, 3, 0)
        };
        authorLink.LinkClicked += (_, _) => OpenUrl(AuthorUrl);
        layout.Controls.Add(authorLink);

        LinkLabel repositoryLink = new()
        {
            Text = RepositoryUrl,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            Margin = new Padding(3, 12, 3, 4)
        };
        repositoryLink.LinkClicked += (_, _) => OpenUrl(RepositoryUrl);
        layout.Controls.Add(repositoryLink);

        Button closeButton = new()
        {
            Text = "Fechar",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Anchor = AnchorStyles.Right
        };
        layout.Controls.Add(closeButton);

        Controls.Add(layout);
        AcceptButton = closeButton;
        CancelButton = closeButton;
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
