using System.ComponentModel;

namespace Guardrail.Models;

/// <summary>Describes one module (or Fleet Hub section) of the app. Drives the toolbar and the "coming soon" pages.</summary>
public sealed class ModuleInfo : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string ToolbarLabel { get; init; } = "";
    /// <summary>Segoe Fluent / MDL2 icon glyph.</summary>
    public required string Glyph { get; init; }
    public string Tagline { get; init; } = "";
    public string Description { get; init; } = "";
    public string PrimaryAction { get; init; } = "New record";
    public string[] Features { get; init; } = [];
    public string[] Kpis { get; init; } = [];
    public string[] Columns { get; init; } = [];
    public bool IsNew { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
