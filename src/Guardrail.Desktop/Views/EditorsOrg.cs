using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>Editors for the organization: locations.</summary>
public static partial class Editors
{
    public static Location? EditLocation(Location? existing, string? name = null)
    {
        var l = existing ?? new Location { Name = name ?? "" };
        bool isNew = existing is null;
        string oldName = l.Name;
        int used = isNew ? 0 : l.UsageCount;

        string[] Suggest(Func<Location, string> pick) => DataStore.Locations.Items.Select(pick).Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToArray();

        var dialog = new FormDialog(
            isNew ? "Add location" : $"Edit {l.Name}",
            isNew ? "A plant, office, terminal or job site. Its name is offered in every Location field across Guardrail."
                  : $"{l.Number}" + (used > 0 ? $" · used on {used} record{(used == 1 ? "" : "s")}" : ""),
            [
                new FormField { Key = "name", Label = "Location name", Required = true, Value = l.Name, Hint = isNew ? "e.g. Dallas Plant, North Terminal" : used > 0 ? "Renaming updates every record that uses it." : null },
                new FormField { Key = "code", Label = "Code", Value = l.Code, Hint = "Optional short code for reports, e.g. DAL-01." },
                new FormField { Key = "type", Label = "Type", Kind = FieldKind.Choice, Options = Location.Types, Value = l.Type },
                new FormField { Key = "region", Label = "Region", Value = l.Region, Suggestions = Suggest(x => x.Region), Hint = "Corporate → region → district → location. Leave blank if you don't use regions." },
                new FormField { Key = "district", Label = "District", Value = l.District, Suggestions = Suggest(x => x.District) },
                new FormField { Key = "address", Label = "Street address", Value = l.Address },
                new FormField { Key = "city", Label = "City and state", Value = l.CityState },
                new FormField { Key = "manager", Label = "Site manager", Value = l.Manager },
                new FormField { Key = "phone", Label = "Phone", Value = l.Phone },
                new FormField { Key = "employees", Label = "Employees", Kind = FieldKind.Number, Value = l.Employees },
                new FormField { Key = "status", Label = "Status", Kind = FieldKind.Choice, Options = Location.Statuses, Value = l.Status, Hint = "Inactive locations stay on old records but aren't suggested for new ones." },
                new FormField { Key = "notes", Label = "Notes", Kind = FieldKind.MultiLine, Value = l.Notes },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Add location" : "Save changes");

        if (dialog.ShowDialog() != true) return null;

        if (dialog.DeleteRequested)
        {
            DataStore.Locations.Delete(l);
            Toaster.Show(used > 0 ? $"{l.Name} removed from the list. The {used} record{(used == 1 ? "" : "s")} using it keep the name." : $"{l.Name} removed.");
            return null;
        }

        string newName = dialog.Text("name");
        if (DataStore.Locations.Items.Any(x => x != l && string.Equals(x.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageDialog.Inform("Already set up", $"There's already a location called \"{newName}\".");
            return null;
        }

        l.Name = newName;
        l.Code = dialog.Text("code");
        l.Type = dialog.Text("type");
        l.Region = dialog.Text("region");
        l.District = dialog.Text("district");
        l.Address = dialog.Text("address");
        l.CityState = dialog.Text("city");
        l.Manager = dialog.Text("manager");
        l.Phone = dialog.Text("phone");
        l.Employees = dialog.Number("employees");
        l.Status = dialog.Text("status");
        l.Notes = dialog.Text("notes");

        if (isNew) DataStore.Locations.Add(l); else DataStore.Locations.Update(l);
        int renamed = isNew ? 0 : LocationNames.Rename(oldName, newName);
        Toaster.Show(isNew ? $"{l.Name} added." : renamed > 0 ? $"{l.Name} saved and updated on {renamed} record{(renamed == 1 ? "" : "s")}." : $"{l.Name} saved.");
        return l;
    }
}
