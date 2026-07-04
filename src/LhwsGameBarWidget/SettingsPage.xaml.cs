using System.Collections.ObjectModel;
using System.ComponentModel;
using LhwsGameBarWidget.Config;
using LhwsGameBarWidget.Lhws;
using Microsoft.Gaming.XboxGameBar;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace LhwsGameBarWidget;

/// <summary>One selectable sensor in the slot pickers.</summary>
public sealed class SensorChoice
{
    public string Identifier { get; init; } = "";
    public string Label { get; init; } = "";
    public string SearchText { get; init; } = "";
    public bool IsPercent { get; init; }

    public override string ToString() => Label;
}

/// <summary>Editable view model of one config row.</summary>
public sealed class RowEditor : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string? BarId { get; set; }
    public string? CenterId { get; set; }
    public string? RightId { get; set; }

    public string BarText { get; set; } = "";
    public string CenterText { get; set; } = "";
    public string RightText { get; set; } = "";

    // The AutoSuggestBoxes keep their own visible text; notifications are only
    // needed when a slot label is set programmatically (initial load).
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Notify(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class SettingsPage : Page
{
    private readonly ObservableCollection<RowEditor> editors = [];
    private List<SensorChoice> choices = [];
    private XboxGameBarWidget? widget;
    private bool loading;

    public SettingsPage()
    {
        InitializeComponent();
        RowList.ItemsSource = editors;
        editors.CollectionChanged += (s, e) =>
            EmptyHint.Visibility = editors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        widget = e.Parameter as XboxGameBarWidget;
        if (widget != null)
        {
            widget.RequestedThemeChanged += Widget_RequestedThemeChanged;
            RequestedTheme = widget.RequestedTheme;
        }

        LoadChoices();
        LoadConfig();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (widget != null)
        {
            widget.RequestedThemeChanged -= Widget_RequestedThemeChanged;
        }
    }

    private async void Widget_RequestedThemeChanged(XboxGameBarWidget sender, object args)
    {
        await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
            () => RequestedTheme = sender.RequestedTheme);
    }

    private void LoadChoices()
    {
        using var reader = new LhwsSensorReader();
        LhwsSnapshot? snapshot = reader.TryConnect() ? reader.Read() : null;
        if (snapshot == null)
        {
            ServiceStatusText.Text =
                "LibreHardwareService is not reachable — sensor search is unavailable, but rows can still be renamed, reordered and removed.";
            return;
        }

        choices = snapshot.Sensors
            .OrderBy(s => s.HardwareName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.SensorType, StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new SensorChoice
            {
                Identifier = s.Identifier,
                Label = $"{s.HardwareName} · {s.Name} — {s.SensorType}",
                SearchText = $"{s.HardwareName} {s.Name} {s.SensorType}".ToLowerInvariant(),
                IsPercent = s.SensorType is "Load" or "Control" or "Level",
            })
            .ToList();
        ServiceStatusText.Text = $"{choices.Count} sensors available. Type to search, e.g. \"cpu temp\" or \"fan #2\".";
    }

    private void LoadConfig()
    {
        loading = true;
        var config = ConfigStore.Load();
        foreach (var r in config.Rows)
        {
            editors.Add(new RowEditor
            {
                Name = r.Name,
                BarId = r.Bar,
                CenterId = r.Center,
                RightId = r.Right,
                BarText = LabelFor(r.Bar),
                CenterText = LabelFor(r.Center),
                RightText = LabelFor(r.Right),
            });
        }
        EmptyHint.Visibility = editors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        loading = false;
    }

    private string LabelFor(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return "";
        }
        return choices.FirstOrDefault(c => c.Identifier == id)?.Label ?? $"⚠ {id}";
    }

    private void Save()
    {
        if (loading)
        {
            return;
        }
        var config = new WidgetConfig
        {
            Rows = editors.Select(ed => new RowConfig
            {
                Name = ed.Name,
                Bar = ed.BarId,
                Center = ed.CenterId,
                Right = ed.RightId,
            }).ToList(),
        };
        ConfigStore.Save(config);
    }

    private void AddBarRow_Click(object sender, RoutedEventArgs e)
    {
        editors.Add(new RowEditor { Name = "New row" });
        Save();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is RowEditor ed)
        {
            editors.Remove(ed);
            Save();
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(sender, -1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(sender, +1);

    private void Move(object sender, int delta)
    {
        if (((FrameworkElement)sender).DataContext is not RowEditor ed)
        {
            return;
        }
        int index = editors.IndexOf(ed);
        int target = index + delta;
        if (index >= 0 && target >= 0 && target < editors.Count)
        {
            editors.Move(index, target);
            Save();
        }
    }

    private void Name_TextChanged(object sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender;
        if (box.DataContext is RowEditor ed && ed.Name != box.Text)
        {
            ed.Name = box.Text;
            Save();
        }
    }

    private void Slot_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }
        if (sender.DataContext is not RowEditor ed)
        {
            return;
        }

        var query = sender.Text.Trim();
        if (query.Length == 0)
        {
            // Cleared by the user → unassign the slot
            SetSlot(ed, SlotOf(sender), null, "");
            sender.ItemsSource = null;
            Save();
            return;
        }

        bool percentOnly = SlotOf(sender) == "bar";
        var tokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        sender.ItemsSource = choices
            .Where(c => (!percentOnly || c.IsPercent) && tokens.All(t => c.SearchText.Contains(t)))
            .Take(50)
            .ToList();
    }

    private void Slot_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        sender.Text = ((SensorChoice)args.SelectedItem).Label;
    }

    private void Slot_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (sender.DataContext is not RowEditor ed)
        {
            return;
        }
        var choice = args.ChosenSuggestion as SensorChoice
            ?? (sender.ItemsSource as List<SensorChoice>)?.FirstOrDefault();
        if (choice == null)
        {
            return;
        }
        sender.Text = choice.Label;
        SetSlot(ed, SlotOf(sender), choice.Identifier, choice.Label);
        Save();
    }

    private static string SlotOf(AutoSuggestBox box) => (string)box.Tag;

    private static void SetSlot(RowEditor ed, string slot, string? id, string label)
    {
        switch (slot)
        {
            case "bar":
                ed.BarId = id;
                ed.BarText = label;
                break;
            case "center":
                ed.CenterId = id;
                ed.CenterText = label;
                break;
            case "right":
                ed.RightId = id;
                ed.RightText = label;
                break;
        }
    }
}
