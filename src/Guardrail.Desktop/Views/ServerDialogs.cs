using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;
using Guardrail.Sync;

namespace Guardrail.Views;

/// <summary>Connect to Guardrail Server: sign in, or join with an invite code.</summary>
public sealed class ConnectServerDialog : FormFrame
{
    private readonly TextBox _url = new() { Text = "http://" };
    private readonly WrapPanel _mode;
    private readonly TextBox _username = new();
    private readonly PasswordBox _password = new();
    private readonly TextBox _name = new();
    private readonly TextBox _invite = new() { CharacterCasing = CharacterCasing.Upper };
    private readonly FrameworkElement _nameRow, _inviteRow;

    public ConnectServerDialog()
        : base("Connect to GuardHawk Server", "Share records with your team. Everything stays on this PC too, so GuardHawk keeps working when the server can't be reached.",
               "", "Connect", 540)
    {
        if (ServerSync.Connection?.Url is { Length: > 0 } last) _url.Text = last;
        AddField("Server address", _url, "Ask whoever runs your GuardHawk Server, e.g. http://192.168.1.20:5080 or https://ehs.yourcompany.com");
        _mode = AddField("I want to", Chips([("signin", "Sign in"), ("join", "Create my account")], "signin"));
        AddField("Username", _username, "Often your email address.");
        AddField("Password", _password);
        _nameRow = Row(AddField("Your name", _name, "How you appear on records you create."));
        _inviteRow = Row(AddField("Invite code", _invite, "From your admin, like 7KQ4-M2XD-9PRT. Leave empty if the server lets anyone sign up."));
        foreach (var chip in _mode.Children.OfType<RadioButton>()) chip.Checked += (_, _) => UpdateMode();
        UpdateMode();
        Loaded += (_, _) => { _url.Focus(); _url.CaretIndex = _url.Text.Length; };
    }

    private static FrameworkElement Row(FrameworkElement input) => (FrameworkElement)input.Parent;

    private void UpdateMode()
    {
        bool join = Selected(_mode) == "join";
        _nameRow.Visibility = _inviteRow.Visibility = join ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Content = join ? "Create account and connect" : "Connect";
    }

    protected override async Task<string?> TrySaveAsync()
    {
        string url;
        try { url = SyncApiClient.NormalizeUrl(_url.Text); }
        catch (SyncException e) { return e.Message; }
        if (_username.Text.Trim().Length == 0 || _password.Password.Length == 0) return "Enter your username and password.";

        try
        {
            using var api = ServerSync.Client(url);
            var info = await api.InfoAsync();
            if (info.Product is not ("Guardrail Server" or "GuardHawk Server")) return "That address answers, but it isn't a GuardHawk Server.";
            if (info.Protocol > Protocol.Version) return "That server needs a newer version of GuardHawk. Update GuardHawk and try again.";

            string device = Environment.MachineName, version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
            var session = Selected(_mode) == "join"
                ? await api.RegisterAsync(new RegisterRequest(_username.Text, _password.Password, _name.Text, null, _invite.Text.Trim().Length > 0 ? _invite.Text : null, device, "Windows", version))
                : await api.LoginAsync(new LoginRequest(_username.Text, _password.Password, device, "Windows", version));
            api.Token = session.Token;

            // Both sides have records: combine them, or take the server's?
            bool keepLocal = true;
            int local = SyncHooks.Stores.Sum(s => s.Ids.Count());
            long remote = (await api.ChangesAsync(long.MaxValue - 1)).Latest;
            if (local > 0 && remote > 0 && session.User.Role != Protocol.Roles.Viewer)
            {
                var pick = MessageDialog.Choose($"{session.Org.Name} already has records",
                    $"This PC has {local:N0} record{(local == 1 ? "" : "s")} of its own.\n\nCombine both: this PC's records are added to the server's and everyone sees them.\n\nOr replace this PC's records with the server's (back them up first if you might need them).",
                    "Combine both", "Cancel", "Use the server's records", danger: false);
                if (pick == null || pick == "Cancel") { await api.LogoutAsync(); return "Not connected."; }
                keepLocal = pick == "Combine both";
            }

            await ServerSync.ConnectAsync(url, info, session, keepLocal);
            return null;
        }
        catch (SyncException e) { return e.Message; }
    }
}
