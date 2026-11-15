using System.Windows;
using System.Windows.Controls;

namespace Guardrail.Views;

/// <summary>A themed replacement for Windows' MessageBox: a message with OK, or a question with two choices.</summary>
public sealed class MessageDialog : DialogFrame
{
    /// <summary>Which button was picked in <see cref="Choose"/> (null when the dialog was closed).</summary>
    private string? _picked;

    private MessageDialog(string title, string message, string okText, string? cancelText, bool danger, string? extraText = null)
        : base(title, "", danger ? "" : "", 480)
    {
        if (danger) UseDangerTone();

        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(28, 0, 28, 20) };
        SetBody(text);

        if (extraText != null)
        {
            var extra = MakeButton(extraText, "GhostButton");
            extra.SetResourceReference(ForegroundProperty, "Brush.Danger");
            extra.Click += (_, _) => { _picked = extraText; DialogResult = false; };
            DockPanel.SetDock(extra, Dock.Left);
            Footer.Children.Add(extra);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (cancelText != null)
        {
            var cancel = MakeButton(cancelText, "GhostButton");
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(0, 0, 10, 0);
            buttons.Children.Add(cancel);
        }
        var ok = MakeButton(okText, "PrimaryButton");
        ok.IsDefault = true;
        if (danger) ok.SetResourceReference(BackgroundProperty, "Brush.Danger");
        ok.Click += (_, _) => { _picked = okText; DialogResult = true; };
        buttons.Children.Add(ok);
        Footer.Children.Add(buttons);
    }

    /// <summary>Shows a message with an OK button.</summary>
    public static void Inform(string title, string message, bool danger = false) =>
        new MessageDialog(title, message, "OK", null, danger).ShowDialog();

    /// <summary>Asks a yes / no question. Returns true when the person picks <paramref name="okText"/>.</summary>
    public static bool Confirm(string title, string message, string okText, string cancelText = "Cancel", bool danger = false) =>
        new MessageDialog(title, message, okText, cancelText, danger).ShowDialog() == true;

    /// <summary>
    /// Three choices: <paramref name="okText"/> (primary), <paramref name="cancelText"/> and, on the left in red,
    /// <paramref name="sideText"/>. Returns the text of the button picked, or null when cancelled or closed.
    /// </summary>
    public static string? Choose(string title, string message, string okText, string cancelText, string sideText, bool danger = false)
    {
        var d = new MessageDialog(title, message, okText, cancelText, danger, sideText);
        d.ShowDialog();
        return d._picked;
    }
}
