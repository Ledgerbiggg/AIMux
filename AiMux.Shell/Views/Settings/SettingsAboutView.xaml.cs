using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace AiMux.Shell.Views.Settings;

public partial class SettingsAboutView
{
    public SettingsAboutView()
    {
        InitializeComponent();
    }

    private void OnGitHubLinkClick(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
