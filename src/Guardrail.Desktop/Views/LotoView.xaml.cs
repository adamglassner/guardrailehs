using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class LotoView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public LotoView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Loto.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Loto.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Loto.Items;
        string q = SearchBox.Text.Trim();
        KpiProcedures.Text = all.Count.ToString();
        KpiSoon.Text = all.Count(l => l.DisplayStatus == Status.DueSoon).ToString();
        KpiOverdue.Text = all.Count(l => l.DisplayStatus is Status.Overdue or Status.NotInspected).ToString();
        KpiFiles.Text = all.Count(l => l.HasFile).ToString();

        var shown = all
            .Where(l => _filter switch
            {
                "Due" => l.DisplayStatus is Status.DueSoon or Status.Overdue or Status.NotInspected,
                "Current" => l.DisplayStatus == Status.Current,
                _ => true,
            })
            .Where(l => q.Length == 0 || Matches(l, q))
            .OrderBy(l => l.NextInspection ?? DateTime.MinValue)
            .ThenBy(l => l.Equipment)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No LOTO procedures yet";
            EmptyText.Text = "Click \"Add procedure\" for each machine that needs energy control.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} procedures · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(LotoProcedure r, string q) =>
        new[] { r.Number, r.Equipment, r.AssetTag, r.Location, r.ProcedureNumber, r.EnergySources, r.IsolationPoints, r.Inspector, r.DisplayStatus }
            .Any(f => (f ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { Tag: string tag }) return;
        _filter = tag;
        Refresh();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (_ready) Refresh();
    }

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditLoto(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LotoProcedure r }) Editors.EditLoto(r);
    }

    private void Inspect_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: var ctx } && ctx is not null) Editors.RecordLotoInspection((LotoProcedure)ctx);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: var ctx } && ctx is not null) Editors.OpenStoredFile(((LotoProcedure)ctx).StoredFile, "this procedure");
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-loto",
            ["LOTO #", "Equipment", "Asset tag", "Location", "Procedure #", "Energy sources", "Isolation points", "Last inspection", "Next due", "Inspected by", "Status", "Document"],
            (Rows.ItemsSource as IEnumerable<LotoProcedure> ?? Enumerable.Empty<LotoProcedure>()).Select(r => new[]
            {
                r.Number, r.Equipment, r.AssetTag, r.Location, r.ProcedureNumber, r.EnergySources, r.IsolationPoints, r.LastInspection?.ToString("yyyy-MM-dd") ?? "", r.NextInspection?.ToString("yyyy-MM-dd") ?? "", r.Inspector, r.DisplayStatus, r.FileName,
            }));
}
