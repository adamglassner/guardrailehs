using System.Windows;
using System.Windows.Controls;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

public partial class LibraryView : UserControl
{
    private string _filter = "All";
    private bool _ready;

    public LibraryView()
    {
        InitializeComponent();
        ((RadioButton)FilterChips.Children[0]).IsChecked = true;
        _ready = true;

        DataStore.Library.Changed += OnStoreChanged;
        Unloaded += (_, _) => DataStore.Library.Changed -= OnStoreChanged;
        Refresh();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var all = DataStore.Library.Items;

        KpiDocs.Text = all.Count.ToString();
        KpiPolicies.Text = all.Count(d => d.Category is "Policy" or "Procedure").ToString();
        KpiSoon.Text = all.Count(d => d.DisplayStatus == Status.ReviewDueSoon).ToString();
        KpiOverdue.Text = all.Count(d => d.DisplayStatus == Status.ReviewOverdue).ToString();

        string q = SearchBox.Text.Trim();
        var shown = all
            .Where(d => _filter switch
            {
                "All" => true,
                "Review" => d.DisplayStatus is Status.ReviewOverdue or Status.ReviewDueSoon,
                _ => d.Category == _filter,
            })
            .Where(d => q.Length == 0 || Matches(d, q))
            .OrderBy(d => d.DisplayStatus switch { Status.ReviewOverdue => 0, Status.ReviewDueSoon => 1, _ => 2 })
            .ThenBy(d => d.Category)
            .ThenBy(d => d.Title)
            .ToList();

        Rows.ItemsSource = shown;
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (all.Count == 0)
        {
            EmptyTitle.Text = "No documents yet";
            EmptyText.Text = "Click \"Add document\" to store a policy, procedure, program or form.";
        }
        else
        {
            EmptyTitle.Text = "Nothing matches";
            EmptyText.Text = "Try a different filter or search.";
        }
        CountText.Text = all.Count == 0 ? "" : $"Showing {shown.Count} of {all.Count} documents · click a row to edit, or Open to view the file";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(LibraryDocument d, string q) =>
        new[] { d.Number, d.Title, d.Category, d.DocVersion, d.Owner, d.Notes, d.FileName, d.DisplayStatus }
            .Any(f => f.Contains(q, StringComparison.OrdinalIgnoreCase));

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

    private void New_Click(object sender, RoutedEventArgs e) => Editors.EditDocument(null);

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryDocument d }) Editors.EditDocument(d);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // don't also open the editor
        if (sender is FrameworkElement { DataContext: LibraryDocument d }) Editors.OpenDocument(d);
    }

    private void Export_Click(object sender, RoutedEventArgs e) =>
        CsvExporter.Export("guardrail-library",
            ["Doc #", "Title", "Category", "Version", "Owner", "Next review", "Status", "File", "Last updated", "Notes"],
            (Rows.ItemsSource as IEnumerable<LibraryDocument> ?? Enumerable.Empty<LibraryDocument>()).Select(d => new[]
            {
                d.Number, d.Title, d.Category, d.DocVersion, d.Owner, d.ReviewDue?.ToString("yyyy-MM-dd") ?? "",
                d.DisplayStatus, d.FileName, d.UpdatedAt.ToString("yyyy-MM-dd"), d.Notes,
            }));
}
