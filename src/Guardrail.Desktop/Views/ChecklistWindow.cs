using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;

namespace Guardrail.Views;

/// <summary>
/// Fills in an inspection: every checklist item gets Pass / Fail / N/A and an optional note.
/// Changes are only written back to the inspection when you save or complete.
/// </summary>
public sealed class ChecklistWindow : DialogFrame
{
    private readonly Inspection _inspection;
    private readonly List<(ChecklistItem Item, Func<string> Result, Action<string> SetResult, TextBox Note)> _rows = [];
    private readonly TextBlock _progress = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _progressTrack = new() { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 8, 0, 0) };
    private readonly Border _progressFill = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Border _errorBar;
    private readonly TextBlock _error;

    public bool DeleteRequested { get; private set; }

    public ChecklistWindow(Inspection inspection)
        : base(inspection.TemplateName,
               string.Join(" · ", new[] { inspection.Number, inspection.DateText, inspection.Location, inspection.Department, inspection.Equipment, inspection.Inspector }.Where(x => x.Length > 0)),
               "", 820)
    {
        _inspection = inspection;

        // ----- Footer -----
        var delete = MakeButton("Delete", "GhostButton", "");
        delete.SetResourceReference(ForegroundProperty, "Brush.Danger");
        delete.Click += (_, _) =>
        {
            var label = ((StackPanel)delete.Content).Children.OfType<TextBlock>().Last();
            if (label.Text == "Delete") { label.Text = "Click again to delete"; return; }
            DeleteRequested = true;
            DialogResult = true;
        };
        DockPanel.SetDock(delete, Dock.Left);
        Footer.Children.Add(delete);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var allPass = MakeButton("Mark the rest Pass", "GhostButton", "");
        allPass.Margin = new Thickness(0, 0, 10, 0);
        allPass.ToolTip = "Sets every unanswered item to Pass";
        allPass.Click += (_, _) =>
        {
            foreach (var row in _rows.Where(r => r.Result().Length == 0)) row.SetResult(ChecklistItem.Pass);
            UpdateProgress();
        };
        var later = MakeButton("Save for later", "GhostButton");
        later.Margin = new Thickness(0, 0, 10, 0);
        later.Click += (_, _) => Save(complete: false);
        var complete = MakeButton("Complete inspection", "PrimaryButton");
        complete.Click += (_, _) => Save(complete: true);
        buttons.Children.Add(allPass);
        buttons.Children.Add(later);
        buttons.Children.Add(complete);
        Footer.Children.Add(buttons);

        // ----- Body: progress, then one card per item -----
        var list = new StackPanel { Margin = new Thickness(28, 0, 28, 14) };
        _errorBar = MakeErrorBar(out _error);
        list.Children.Add(_errorBar);

        _progressTrack.SetResourceReference(Border.BackgroundProperty, "Brush.Track");
        _progressFill.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
        var trackGrid = new Grid();
        trackGrid.Children.Add(_progressTrack);
        trackGrid.Children.Add(_progressFill);
        trackGrid.SizeChanged += (_, _) => UpdateProgress();
        var progressBox = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        progressBox.Children.Add(_progress);
        progressBox.Children.Add(trackGrid);
        list.Children.Add(progressBox);

        int n = 0;
        foreach (var item in inspection.Items)
        {
            n++;
            var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 10) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var numberBadge = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            numberBadge.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
            var number = new TextBlock { Text = n.ToString(), FontSize = 11.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            number.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            numberBadge.Child = number;
            grid.Children.Add(numberBadge);

            var text = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            string group = "chk" + Guid.NewGuid().ToString("N");
            var radios = new List<RadioButton>();
            foreach (var option in ChecklistItem.Results)
            {
                var rb = new RadioButton
                {
                    Content = option,
                    GroupName = group,
                    IsChecked = item.Result == option,
                    Style = (Style)FindResource("ChipRadio"),
                    Margin = new Thickness(6, 0, 0, 0),
                    MinWidth = 52,
                };
                rb.Checked += (_, _) => UpdateProgress();
                radios.Add(rb);
                chips.Children.Add(rb);
            }
            Grid.SetColumn(chips, 2);
            grid.Children.Add(chips);

            var note = new TextBox { Text = item.Note, Margin = new Thickness(0, 10, 0, 0), ToolTip = "Note (what's wrong, where, photo reference…)" };
            Grid.SetRow(note, 1);
            Grid.SetColumn(note, 1);
            Grid.SetColumnSpan(note, 2);
            grid.Children.Add(note);

            card.Child = grid;
            list.Children.Add(card);
            _rows.Add((item,
                () => radios.FirstOrDefault(r => r.IsChecked == true)?.Content as string ?? "",
                value => { foreach (var r in radios) r.IsChecked = (string)r.Content == value; },
                note));
        }
        SetBody(list);
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        int answered = _rows.Count(r => r.Result().Length > 0);
        int pass = _rows.Count(r => r.Result() == ChecklistItem.Pass);
        int fail = _rows.Count(r => r.Result() == ChecklistItem.Fail);
        string score = pass + fail == 0 ? "—" : $"{100.0 * pass / (pass + fail):0}%";
        _progress.Text = $"{answered} of {_rows.Count} answered  ·  {pass} pass  ·  {fail} fail  ·  score {score}";
        double width = _progressTrack.ActualWidth;
        _progressFill.Width = _rows.Count == 0 || width <= 0 ? 0 : width * answered / _rows.Count;
        _errorBar.Visibility = Visibility.Collapsed;
    }

    private void Save(bool complete)
    {
        int left = _rows.Count(r => r.Result().Length == 0);
        if (complete && left > 0)
        {
            ShowError(_errorBar, _error, $"{left} item{(left == 1 ? "" : "s")} not answered yet. Answer them, or use \"Save for later\".");
            return;
        }
        foreach (var row in _rows)
        {
            row.Item.Result = row.Result();
            row.Item.Note = row.Note.Text.Trim();
        }
        _inspection.IsComplete = complete;
        DialogResult = true;
    }
}
