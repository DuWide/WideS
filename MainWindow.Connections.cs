using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using Forms = System.Windows.Forms;

namespace DevCockpit;

public partial class MainWindow
{
    private void ShowConnections()
    {
        EnterView("connections");
        _viewScope = "connections";
        SetTitle("Подключения", "Общие подключения, не привязанные к проектам");
        var root = new DockPanel();
        var top = new WrapPanel { Margin = new Thickness(8) };
        var list = ItemsPanel();
        WpfTextBox search = null!;
        void Render()
        {
            list.Children.Clear();
            var query = UiHelpers.EffectiveText(search);
            var items = _connections.Connections
                         .Where(c => c.WorkspaceId is null)
                         .Where(c => _connectionTypeFilter == "Все" || c.Type.Equals(_connectionTypeFilter, StringComparison.OrdinalIgnoreCase))
                         .Where(c => string.IsNullOrWhiteSpace(query) ||
                                     c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                     c.Address.Contains(query, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                         .ToList();
            if (IsTableView(_viewScope) && items.Count > 0)
            {
                list.Children.Add(ConnectionTableHeader());
            }

            foreach (var item in items)
            {
                list.Children.Add(ConnectionCard(item));
            }

            if (list.Children.Count == 0) list.Children.Add(UiHelpers.EmptyState("Подключений нет", "Добавьте первое подключение.", "Новое подключение", () => AddConnection()));
        }
        search = SearchBox("Поиск подключения", Render);
        top.Children.Add(search);
        void AddTypeFilter(string value)
        {
            top.Children.Add(FilterButton(value, _connectionTypeFilter == value, () =>
            {
                _connectionTypeFilter = value;
                ShowConnections();
            }));
        }
        AddTypeFilter("Все");
        AddTypeFilter("AnyDesk");
        AddTypeFilter("RDP");
        top.Children.Add(ActionButton("Новое подключение", AddConnection));
        top.Children.Add(ToolbarGap());
        AddViewModeButtons(top, ShowConnections);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        Render();
        root.Children.Add(list);
        ContentHost.Content = root;
    }
    private void AddConnection()
    {
        var source = _viewScope == "project-detail" && _selectedProject is not null
            ? new ConnectionItem { WorkspaceId = _selectedProject.Id }
            : null;
        var win = new ConnectionEditorWindow(_projects.Projects, source) { Owner = this };
        win.Closed += (_, _) =>
        {
            if (!win.Saved) return;
            _connections.Connections.Add(win.Connection);
            _connectionsStore.Save(_connections);
            AddLog("OK", $"Подключение добавлено: {win.Connection.Name}");
            RefreshAfterConnectionChange(win.Connection);
        };
        WindowPlacementService.PlaceOnPrimary(win);
        win.Show();
    }
    private void EditConnection(ConnectionItem item)
    {
        var win = new ConnectionEditorWindow(_projects.Projects, item) { Owner = this };
        win.Closed += (_, _) =>
        {
            if (!win.Saved) return;
            var index = _connections.Connections.FindIndex(c => c.Id == item.Id);
            if (index >= 0) _connections.Connections[index] = win.Connection;
            _connectionsStore.Save(_connections);
            AddLog("OK", $"Подключение изменено: {win.Connection.Name}");
            RefreshAfterConnectionChange(win.Connection);
        };
        WindowPlacementService.PlaceOnPrimary(win);
        win.Show();
    }
    private void RefreshAfterConnectionChange(ConnectionItem connection)
    {
        RefreshDockConnections();
        var project = connection.WorkspaceId is { } id ? _projects.Projects.FirstOrDefault(p => p.Id == id) : null;
        if (project is not null)
        {
            ShowProjectDetail(project);
            return;
        }

        ShowConnections();
    }
    private void Connect(ConnectionItem item)
    {
        try
        {
            AddLog("OK", ConnectionService.Connect(item, _settings));
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"Подключение: {ex.Message}");
        }
    }
    private void DeleteSavedRdpCredentials(ConnectionItem item)
    {
        try
        {
            AddLog("OK", ConnectionService.DeleteRdpCredentials(item));
        }
        catch (Exception ex)
        {
            AddLog("ERR", $"RDP credentials: {ex.Message}");
        }
    }
    private FrameworkElement ConnectionCard(ConnectionItem item)
    {
        if (IsTableView(_viewScope))
        {
            return ConnectionCompactRow(item);
        }

        if (IsListView(_viewScope))
        {
            return ListRow(item.Name, () => Connect(item), null,
                FavoriteIconButton(item.IsPinned, () => ToggleConnectionPinned(item)),
                EditIconButton(() => EditConnection(item)));
        }

        var card = Card(item.Name);
        ApplyCardView(card);
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                Connect(item);
            }
        };
        var layout = new Grid();
        var stack = BaseCardStack(item.Name);
        layout.Children.Add(stack);
        layout.Children.Add(EditIconButton(() => EditConnection(item)));
        layout.Children.Add(FavoriteIconButton(item.IsPinned, () => ToggleConnectionPinned(item), 34));
        stack.Children.Add(Muted($"{item.Type} · {item.Address}"));
        var reach = new TextBlock { FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        stack.Children.Add(reach);
        StartReachabilityIndicator(item, reach);
        if (!string.IsNullOrWhiteSpace(item.Comment))
        {
            stack.Children.Add(Text(item.Comment, 13, (WpfBrush)FindResource("MutedBrush"), new Thickness(0, 12, 0, 8)));
        }
        var primary = new WrapPanel { Margin = new Thickness(0, 12, 0, 4) };
        primary.Children.Add(ActionButton("Подключиться", () => Connect(item)));
        stack.Children.Add(primary);

        var secondary = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        secondary.Children.Add(LinkAction("ID / адрес", () => Copy(item.Address, "Адрес/ID скопирован.")));
        secondary.Children.Add(LinkAction("Логин", () => Copy(item.Login, "Логин скопирован.")));
        secondary.Children.Add(LinkAction("Пароль", () => Copy(SecretService.Unprotect(item.EncryptedPassword), "Пароль скопирован.")));
        if (item.Type.Equals("RDP", StringComparison.OrdinalIgnoreCase))
        {
            secondary.Children.Add(LinkAction("Удалить сохраненные RDP-учетные данные", () => DeleteSavedRdpCredentials(item)));
        }
        stack.Children.Add(secondary);
        card.Child = layout;
        return card;
    }

    private Border ConnectionTableHeader() => BuildTableHeader(
        ("Название", new GridLength(1.8, GridUnitType.Star)),
        ("Тип", new GridLength(72)),
        ("ID / адрес", new GridLength(140)),
        ("Действия", GridLength.Auto));

    private Border ConnectionCompactRow(ConnectionItem item)
    {
        var row = new Border
        {
            Background = (WpfBrush)FindResource("CardBrush"),
            BorderBrush = ThemeBorderMain(),
            BorderThickness = new Thickness(1, 0, 1, 1),
            Padding = new Thickness(8, 4, 6, 4),
            MinHeight = 34,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = item.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)FindResource("TextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = item.Name
        };
        Grid.SetColumn(name, 0);

        var type = UiHelpers.TypeBadge(item.Type);
        type.VerticalAlignment = VerticalAlignment.Center;
        type.Margin = new Thickness(0);
        Grid.SetColumn(type, 1);

        var address = new TextBlock
        {
            Text = item.Address,
            FontSize = 12,
            Foreground = (WpfBrush)FindResource("MutedBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = item.Address
        };
        Grid.SetColumn(address, 2);

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var connect = ActionButton("Подк.", () => Connect(item));
        connect.Height = 28;
        connect.MinWidth = 54;
        connect.Padding = new Thickness(8, 2, 8, 2);
        connect.Margin = new Thickness(0, 0, 2, 0);
        actions.Children.Add(connect);

        var edit = EditIconButton(() => EditConnection(item));
        edit.Width = 28;
        edit.Height = 28;
        edit.MinWidth = 28;
        edit.Margin = new Thickness(0, 0, 2, 0);
        actions.Children.Add(edit);

        var favorite = FavoriteIconButton(item.IsPinned, () => ToggleConnectionPinned(item), 28);
        favorite.Width = 28;
        favorite.Height = 28;
        favorite.MinWidth = 28;
        actions.Children.Add(favorite);
        Grid.SetColumn(actions, 3);

        grid.Children.Add(name);
        grid.Children.Add(type);
        grid.Children.Add(address);
        grid.Children.Add(actions);
        row.Child = grid;
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                Connect(item);
            }
        };
        return row;
    }
}
