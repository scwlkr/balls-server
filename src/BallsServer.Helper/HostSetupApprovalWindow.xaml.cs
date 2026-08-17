using System.Windows;
using BallsServer.Core.Sharing;

namespace BallsServer.Helper;

public partial class HostSetupApprovalWindow : Window
{
    private readonly HostSetupRequest _request;

    public HostSetupApprovalWindow(HostSetupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
        InitializeComponent();
        DataContext = this;
    }

    public string ManagedFolder => _request.ManagedFolder;

    public string AccessPathLabel => _request.AccessPath == AccessPathKind.Tailscale
        ? "Tailscale"
        : "Private LAN";

    public IReadOnlyList<string> Changes { get; } =
    [
        "Create one non-administrator limited client account and one access group.",
        "Add one Modify permission entry to the selected folder.",
        "Create the authenticated Balls SMB share.",
        "Create one inbound TCP 445 firewall rule scoped to the selected private adapter.",
        "Write a protected ownership record for safe removal.",
    ];

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
