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
    private void ShowAiAgents()
    {
        EnterView("ai");
        _viewScope = "ai";
        SetTitle("Браузер", "Рабочие ссылки, AI, почта и основные сервисы");
        var root = SectionWithActions(actions =>
        {
            var categories = BrowserCategories().ToList();
            if (!categories.Any(c => c.Equals(_browserCategoryFilter, StringComparison.OrdinalIgnoreCase)))
            {
                _browserCategoryFilter = categories[0];
            }
            actions.Children.Add(ActionButton("Новая ссылка", AddAiAgent));
            actions.Children.Add(ActionButton("Добавить категорию", AddBrowserCategory, false));
            actions.Children.Add(ActionButton("← кат.", () => MoveBrowserCategory(-1), false));
            actions.Children.Add(ActionButton("кат. →", () => MoveBrowserCategory(1), false));
            actions.Children.Add(ToolbarGap());
            foreach (var category in categories)
            {
                actions.Children.Add(FilterButton(category, category.Equals(_browserCategoryFilter, StringComparison.OrdinalIgnoreCase), () =>
                {
                    _browserCategoryFilter = category;
                    ShowAiAgents();
                }));
            }
            actions.Children.Add(ToolbarGap());
            AddViewModeButtons(actions, ShowAiAgents);
        }, out var panel);

        void Render()
        {
            panel.Children.Clear();
            var categories = BrowserCategories().ToList();
            if (!categories.Any(c => c.Equals(_browserCategoryFilter, StringComparison.OrdinalIgnoreCase)))
            {
                _browserCategoryFilter = categories[0];
            }

            var agents = _aiAgents.Agents
                .Where(a => string.Equals(NormalizeBrowserCategory(a.Category), _browserCategoryFilter, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.CreatedAt)
                .ToList();

            if (agents.Count == 0)
            {
                panel.Children.Add(CardText("Пусто", "В этой категории пока нет ссылок."));
                return;
            }

            if (IsTableView(_viewScope) && panel is StackPanel)
            {
                panel.Children.Add(AiAgentTableHeader());
            }

            foreach (var agent in agents)
            {
                panel.Children.Add(AiAgentCard(agent));
            }
        }

        Render();
        ContentHost.Content = root;
    }
    private void EnsureAiAgentDefaults()
    {
        if (_aiAgents.Agents.Count > 0) return;

        _aiAgents.Agents.AddRange([
            new AiAgentItem { Name = "Google AI Studio", Url = "https://aistudio.google.com/prompts/new_chat", Category = "AI Agents" },
            new AiAgentItem { Name = "Claude", Url = "https://claude.ai/new", Category = "AI Agents" },
            new AiAgentItem { Name = "Gemini", Url = "https://gemini.google.com/app", Category = "AI Agents" },
            new AiAgentItem { Name = "ChatGPT", Url = "https://chatgpt.com/", Category = "AI Agents" }
        ]);
        _aiAgentsStore.Save(_aiAgents);
    }
    private List<string> BrowserCategoriesOrdered()
    {
        var defaults = new[] { "AI Agents", "Основное", "Mail" };
        var all = defaults
            .Concat(_settings.BrowserCategories)
            .Concat(_aiAgents.Agents.Select(a => NormalizeBrowserCategory(a.Category)))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SyncBrowserCategoryOrder(all);
        return _settings.BrowserCategoryOrder
            .Where(c => all.Any(a => a.Equals(c, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
    private void SyncBrowserCategoryOrder(List<string> all)
    {
        if (_settings.BrowserCategoryOrder.Count == 0)
        {
            _settings.BrowserCategoryOrder = all.ToList();
            return;
        }

        var changed = false;
        foreach (var category in all)
        {
            if (!_settings.BrowserCategoryOrder.Any(c => c.Equals(category, StringComparison.OrdinalIgnoreCase)))
            {
                _settings.BrowserCategoryOrder.Add(category);
                changed = true;
            }
        }

        _settings.BrowserCategoryOrder.RemoveAll(c => !all.Any(a => a.Equals(c, StringComparison.OrdinalIgnoreCase)));
        if (changed) _settingsStore.Save(_settings);
    }
    private void MoveBrowserCategory(int delta)
    {
        var list = BrowserCategoriesOrdered();
        var idx = list.FindIndex(c => c.Equals(_browserCategoryFilter, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) idx = 0;
        var newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= list.Count) return;
        (list[idx], list[newIdx]) = (list[newIdx], list[idx]);
        _settings.BrowserCategoryOrder = list;
        _settingsStore.Save(_settings);
        ShowAiAgents();
    }
    private IEnumerable<string> BrowserCategories() => BrowserCategoriesOrdered();

    private static string NormalizeBrowserCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ? "AI Agents" : category.Trim();
    }
    private void AddBrowserCategory()
    {
        var name = PromptText("Новая категория", "Название категории");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (!_settings.BrowserCategories.Any(c => c.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _settings.BrowserCategories.Add(name);
            _settingsStore.Save(_settings);
        }
        ShowAiAgents();
    }
    private void AddAiAgent()
    {
        var win = new AiAgentEditorWindow(BrowserCategories()) { Owner = this };
        win.Closed += (_, _) =>
        {
            if (!win.Saved) return;
            _aiAgents.Agents.Add(win.Agent);
            _aiAgentsStore.Save(_aiAgents);
            AddLog("OK", $"AI агент добавлен: {win.Agent.Name}");
            ShowAiAgents();
        };
        win.Show();
    }
    private void EditAiAgent(AiAgentItem agent)
    {
        var win = new AiAgentEditorWindow(BrowserCategories(), agent) { Owner = this };
        win.Closed += (_, _) =>
        {
            if (!win.Saved) return;
            var index = _aiAgents.Agents.FindIndex(a => a.Id == agent.Id);
            if (index >= 0) _aiAgents.Agents[index] = win.Agent;
            _aiAgentsStore.Save(_aiAgents);
            AddLog("OK", $"AI агент изменен: {win.Agent.Name}");
            ShowAiAgents();
        };
        win.Show();
    }
    private void DeleteAiAgent(AiAgentItem agent)
    {
        if (WpfMessageBox.Show(this, $"Удалить AI агента \"{agent.Name}\"?", "WideS", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _aiAgents.Agents.Remove(agent);
        _aiAgentsStore.Save(_aiAgents);
        AddLog("WARN", $"AI агент удален: {agent.Name}");
        ShowAiAgents();
    }
    private FrameworkElement AiAgentCard(AiAgentItem agent)
    {
        if (IsTableView(_viewScope))
        {
            return AiAgentCompactRow(agent);
        }

        if (IsListView(_viewScope))
        {
            return ListRow(agent.Name, () => OpenUrl(agent.Url, agent.Name), null,
                FavoriteIconButton(agent.IsPinned, () => ToggleAiAgentPinned(agent)),
                EditIconButton(() => EditAiAgent(agent)));
        }

        var card = Card(agent.Name);
        ApplyCardView(card, 360);
        card.Cursor = System.Windows.Input.Cursors.Hand;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                OpenUrl(agent.Url, agent.Name);
            }
        };
        var layout = new Grid();
        var stack = BaseCardStack(agent.Name);
        layout.Children.Add(stack);
        layout.Children.Add(EditIconButton(() => EditAiAgent(agent)));
        layout.Children.Add(FavoriteIconButton(agent.IsPinned, () => ToggleAiAgentPinned(agent), 34));
        stack.Children.Add(Muted(NormalizeBrowserCategory(agent.Category)));
        stack.Children.Add(Muted(agent.Url));
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(LinkAction("Открыть", () => OpenUrl(agent.Url, agent.Name)));
        actions.Children.Add(LinkAction("Копировать URL", () => Copy(agent.Url, "URL скопирован.")));
        actions.Children.Add(LinkAction("Удалить", () => DeleteAiAgent(agent)));
        stack.Children.Add(actions);
        card.Child = layout;
        return card;
    }

    private Border AiAgentTableHeader() => BuildTableHeader(
        ("Название", new GridLength(1.6, GridUnitType.Star)),
        ("Категория", new GridLength(100)),
        ("URL", new GridLength(2, GridUnitType.Star)),
        ("Действия", GridLength.Auto));

    private Border AiAgentCompactRow(AiAgentItem agent)
    {
        var grid = CreateTableGrid(
            new GridLength(1.6, GridUnitType.Star),
            new GridLength(100),
            new GridLength(2, GridUnitType.Star),
            GridLength.Auto);

        AddCell(grid, 0, new TextBlock
        {
            Text = agent.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)FindResource("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = agent.Name
        });
        AddCell(grid, 1, Muted(NormalizeBrowserCategory(agent.Category)));
        AddCell(grid, 2, Muted(agent.Url));

        var actions = CompactRowActions(
            CompactActionButton("Открыть", () => OpenUrl(agent.Url, agent.Name)),
            CompactActionButton("Копир.", () => Copy(agent.Url, "URL скопирован.")),
            CompactIconButton(EditIconButton(() => EditAiAgent(agent))),
            CompactIconButton(FavoriteIconButton(agent.IsPinned, () => ToggleAiAgentPinned(agent), 28)));
        AddCell(grid, 3, actions);

        return WrapTableRow(grid, () => OpenUrl(agent.Url, agent.Name));
    }
}
