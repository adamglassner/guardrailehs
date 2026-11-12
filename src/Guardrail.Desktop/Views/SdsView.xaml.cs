using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class SdsView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public SdsView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Sds.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Sds.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Sds.Items;
        string q = SearchBox.Text.Trim();
        KpiProducts.Text = all.Count.ToString();
        KpiOnFile.Text = all.Count(s => s.HasFile).ToString();
        KpiMissing.Text = all.Count(s => !s.HasFile).ToString();
        KpiDanger.Text = all.Count(s => s.SignalWord == "Danger").ToString();

        var shown = all
            .Where(s => _filter switch
            {
                "Missing" => !s.HasFile,
                "Danger" or "Warning" => s.SignalWord == _filter,
                _ => true,
            })
            .Where(s => q.Length == 0 || Matches(s, q))
            .OrderBy(s => s.HasFile)
            .ThenBy(s => s.Product)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No products yet";
            EmptyText.Text = "Click \"Add product\" for each hazardous product on site and attach its Safety Data Sheet.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} products · click a row to edit";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(SdsEntry r, string q) =>
        new[] { r.Number, r.Product, r.Manufacturer, r.ProductCode, r.Location, r.SignalWord, r.Hazards, r.Notes, r.FileName }
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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditSds(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SdsEntry r }) Editors.EditSds(r);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: var ctx } && ctx is not null) Editors.OpenStoredFile(((SdsEntry)ctx).StoredFile, "this product");
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-sds",
            ["SDS #", "Product", "Manufacturer", "Product code", "Signal word", "Hazards", "Location", "Quantity", "Revision date", "SDS file", "Notes"],
            (Rows.ItemsSource as IEnumerable<SdsEntry> ?? Enumerable.Empty<SdsEntry>()).Select(r => new[]
            {
                r.Number, r.Product, r.Manufacturer, r.ProductCode, r.SignalWord, r.Hazards, r.Location, r.Quantity, r.RevisionDate?.ToString("yyyy-MM-dd") ?? "", r.FileName, r.Notes,
            }));
}
