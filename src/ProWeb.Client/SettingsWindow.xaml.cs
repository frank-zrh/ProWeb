using System.Windows;
using ProWeb.Client.Core;

namespace ProWeb.Client;

/// <summary>
/// Modal settings/connection dialog (UT-X-R1-004). Edits a copy of <see cref="ClientSettings"/> and
/// only reports success when the server URL is a valid absolute http(s) address; the validity check
/// is delegated to the Core model so it is unit-tested independently of WPF.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ClientSettings _working;

    public SettingsWindow(ClientSettings settings)
    {
        InitializeComponent();
        _working = settings ?? new ClientSettings();

        ServerUrlBox.Text = _working.ServerBaseUrl;
        DeviceIdBox.Text = _working.DeviceId;
        HomePageBox.Text = _working.HomePage;
        Validate();
    }

    /// <summary>The edited settings; only meaningful when <see cref="Window.DialogResult"/> is true.</summary>
    public ClientSettings Result { get; private set; } = new();

    private void ServerUrlBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Validate();

    private bool Validate()
    {
        var candidate = new ClientSettings { ServerBaseUrl = ServerUrlBox.Text };
        var valid = candidate.IsServerUrlValid;
        ServerUrlError.Text = valid ? string.Empty : "请输入有效的 http(s):// 服务地址。";
        ServerUrlError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (SaveButton is not null)
            SaveButton.IsEnabled = valid;
        return valid;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate())
            return;

        Result = new ClientSettings
        {
            ServerBaseUrl = ServerUrlBox.Text.Trim(),
            DeviceId = DeviceIdBox.Text.Trim(),
            HomePage = HomePageBox.Text.Trim(),
        };
        DialogResult = true;
    }
}
