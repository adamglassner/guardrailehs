using System.Windows;
using System.Windows.Controls;

namespace Guardrail.Views;

/// <summary>A themed replacement for Windows' MessageBox: a message with OK, or a question with two choices.</summary>
public sealed class MessageDialog : DialogFrame
{
    private MessageDialog(string title, string message, string okText, string? cancelText, bool danger)
        : base(title, "", danger ? "" : "", 480)
    {
        if (danger) UseDangerTone();

        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(28, 0, 28, 20) };
        SetBody(text);

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
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        Footer.Children.Add(buttons);
    }

    /// <summary>Shows a message with an OK button.</summary>
    public static void Inform(string title, string message, bool danger = false) =>
        new MessageDialog(title, message, "OK", null, danger).ShowDialog();

    /// <summary>Asks a yes / no question. Returns true when the person picks <paramref name="okText"/>.</summary>
    public static bool Confirm(string title, string message, string okText, string cancelText = "Cancel", bool danger = false) =>
        new MessageDialog(title, message, okText, cancelText, danger).ShowDialog() == true;
}
