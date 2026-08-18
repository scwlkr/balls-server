using System.Windows;
using BallsServer.Core.Sharing;
using BallsServer.Windows;

namespace BallsServer.Helper;

public partial class HostSetupApprovalWindow : Window
{
    private readonly HostSetupRequest _request;
    private readonly HostSetupMutationPreview _preview;

    public HostSetupApprovalWindow(HostSetupRequest request, HostSetupMutationPreview preview)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preview);
        _request = request;
        _preview = preview;
        InitializeComponent();
        DataContext = this;
    }

    public string ManagedFolder => _request.ManagedFolder ?? "The folder recorded in protected ownership state";

    public string PlanReference => $"Authoritative plan revision {_preview.Revision}, reference {_preview.PlanDigest[..12]}";

    public string WindowTitle => _request.Operation == HostSetupOperation.StopSharing
        ? "Balls Server — Approve Stop Sharing"
        : "Balls Server — Approve Host Setup";

    public string ActionTitle => _request.Operation == HostSetupOperation.StopSharing
        ? "Stop Sharing"
        : "Apply Host Files setup";

    public string ActionButtonText => _request.Operation == HostSetupOperation.StopSharing
        ? "Stop sharing"
        : "Apply changes";

    public string AccessPathLabel => _request.Operation == HostSetupOperation.StopSharing
        ? "The private path recorded in protected ownership state"
        : _request.AccessPath == AccessPathKind.Tailscale
        ? "Tailscale"
        : "Private LAN";

    public IReadOnlyList<string> Changes => _request.Operation == HostSetupOperation.StopSharing
        ?
        [
            "Disable and remove only the limited account recorded as product-owned.",
            "Remove only the recorded Balls share, private firewall rule, and exact folder permission entry.",
            "Remove the recorded product access group after its dependencies are gone.",
            "Preserve the selected folder and every file inside it.",
        ]
        :
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
