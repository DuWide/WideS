using System.IO;

using System.Diagnostics;

using System.Text.RegularExpressions;

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

    private void ShowCommandRecipes()

    {

        EnterView("commands");

        _viewScope = "commands";

        SetTitle("Команды", "Рецепты команд с переменными: host, port и другие параметры");

        var root = SectionWithActions(actions =>

        {

            actions.Children.Add(ActionButton("Новая команда", AddCommandRecipe));

            actions.Children.Add(ToolbarGap());

            AddViewModeButtons(actions, ShowCommandRecipes);

        }, out var panel);



        void Render()

        {

            panel.Children.Clear();

            var recipes = _commandRecipes.Recipes.OrderByDescending(r => r.CreatedAt).ToList();

            if (recipes.Count == 0)

            {

                panel.Children.Add(UiHelpers.EmptyState("Команд нет", "Добавьте первый рецепт.", "Новая команда", AddCommandRecipe));

            }

            else

            {

                if (IsTableView(_viewScope) && panel is StackPanel)

                {

                    panel.Children.Add(CommandRecipeTableHeader());

                }



                foreach (var recipe in recipes)

                {

                    panel.Children.Add(CommandRecipeCard(recipe));

                }

            }



            var history = _activity.Entries.Where(e => e.Status == "CMD").OrderByDescending(e => e.At).Take(10).ToList();

            if (history.Count > 0)

            {

                panel.Children.Add(CardText("Последние запуски", string.Join("\n", history.Select(h => $"[{h.At:HH:mm}] {h.Message}"))));

            }

        }



        Render();

        ContentHost.Content = root;

    }

    private void RunCommandRecipe(CommandRecipeItem recipe)

    {

        var command = BuildRecipeCommand(recipe);

        if (string.IsNullOrWhiteSpace(command)) return;



        try

        {

            Process.Start(new ProcessStartInfo

            {

                FileName = "powershell.exe",

                Arguments = "-NoExit -Command " + QuotePowerShell(command),

                UseShellExecute = true

            });

            AddLog("OK", $"Команда запущена: {command}");

            _activity.Entries.Add(new ActivityEntry { At = DateTime.Now, Status = "CMD", Message = command });

            _activityStore.Save(_activity);

        }

        catch (Exception ex)

        {

            AddLog("ERR", $"Команда: {ex.Message}");

        }

    }

    private void CopyCommandRecipe(CommandRecipeItem recipe)

    {

        var command = BuildRecipeCommand(recipe);

        if (string.IsNullOrWhiteSpace(command)) return;

        WpfClipboard.SetText(command);

        AddLog("OK", $"Команда скопирована: {command}");

    }

    private string BuildRecipeCommand(CommandRecipeItem recipe)

    {

        var variables = ExtractVariables(recipe.Command).ToList();

        if (variables.Count == 0) return recipe.Command;



        var prompt = new VariablePromptWindow(recipe) { Owner = this };

        if (prompt.ShowDialog() != true) return "";



        var command = recipe.Command;

        foreach (var pair in prompt.Values)

        {

            command = Regex.Replace(command, "\\{" + Regex.Escape(pair.Key) + "\\}", pair.Value.Replace("\"", "\\\""), RegexOptions.IgnoreCase);

        }



        return command;

    }

    private void EnsureCommandRecipeDefaults()

    {

        if (_commandRecipes.Recipes.Count > 0) return;



        _commandRecipes.Recipes.AddRange([

            new CommandRecipeItem { Name = "RDP подключение", Command = "mstsc /v:{host}" },

            new CommandRecipeItem { Name = "Ping host", Command = "ping {host}" },

            new CommandRecipeItem { Name = "Проверить порт", Command = "Test-NetConnection {host} -Port {port}" }

        ]);

        _commandRecipesStore.Save(_commandRecipes);

    }

    private void AddCommandRecipe()

    {

        var win = new CommandRecipeEditorWindow { Owner = this };

        win.Closed += (_, _) =>

        {

            if (!win.Saved) return;

            _commandRecipes.Recipes.Add(win.Recipe);

            _commandRecipesStore.Save(_commandRecipes);

            AddLog("OK", $"Команда добавлена: {win.Recipe.Name}");

            ShowCommandRecipes();

        };

        WindowPlacementService.PlaceOnPrimary(win);

        win.Show();

    }

    private void EditCommandRecipe(CommandRecipeItem recipe)

    {

        var win = new CommandRecipeEditorWindow(recipe) { Owner = this };

        win.Closed += (_, _) =>

        {

            if (!win.Saved) return;

            var index = _commandRecipes.Recipes.FindIndex(r => r.Id == recipe.Id);

            if (index >= 0) _commandRecipes.Recipes[index] = win.Recipe;

            _commandRecipesStore.Save(_commandRecipes);

            AddLog("OK", $"Команда изменена: {win.Recipe.Name}");

            ShowCommandRecipes();

        };

        win.Show();

    }

    private FrameworkElement CommandRecipeCard(CommandRecipeItem recipe)

    {

        if (IsTableView(_viewScope))

        {

            return CommandRecipeCompactRow(recipe);

        }



        if (IsListView(_viewScope))

        {

            return ListRow(recipe.Name, () => RunCommandRecipe(recipe), null,

                EditIconButton(() => EditCommandRecipe(recipe)));

        }



        var card = Card(recipe.Name);

        ApplyCardView(card, 430);

        card.Cursor = System.Windows.Input.Cursors.Hand;

        card.MouseLeftButtonUp += (_, e) =>

        {

            if (!IsInsideButton(e.OriginalSource as DependencyObject))

            {

                RunCommandRecipe(recipe);

            }

        };

        var layout = new Grid();

        var stack = BaseCardStack(recipe.Name);

        layout.Children.Add(stack);

        layout.Children.Add(EditIconButton(() => EditCommandRecipe(recipe)));

        stack.Children.Add(Text(recipe.Command, 14, WpfBrushes.WhiteSmoke, new Thickness(0, 6, 0, 14)));

        var variables = ExtractVariables(recipe.Command).ToList();

        stack.Children.Add(Muted(variables.Count == 0 ? "Без переменных" : "Переменные: " + string.Join(", ", variables)));

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };

        buttons.Children.Add(ActionButton("Выполнить", () => RunCommandRecipe(recipe)));

        buttons.Children.Add(ActionButton("Копировать", () => CopyCommandRecipe(recipe), false));

        stack.Children.Add(buttons);

        card.Child = layout;

        return card;

    }



    private Border CommandRecipeTableHeader() => BuildTableHeader(

        ("Название", new GridLength(1.4, GridUnitType.Star)),

        ("Команда", new GridLength(2, GridUnitType.Star)),

        ("Переменные", new GridLength(120)),

        ("Действия", GridLength.Auto));



    private Border CommandRecipeCompactRow(CommandRecipeItem recipe)

    {

        var variables = ExtractVariables(recipe.Command).ToList();

        var grid = CreateTableGrid(

            new GridLength(1.4, GridUnitType.Star),

            new GridLength(2, GridUnitType.Star),

            new GridLength(120),

            GridLength.Auto);



        AddCell(grid, 0, new TextBlock

        {

            Text = recipe.Name,

            FontSize = 13,

            FontWeight = FontWeights.SemiBold,

            Foreground = (WpfBrush)FindResource("TextBrush"),

            TextTrimming = TextTrimming.CharacterEllipsis,

            ToolTip = recipe.Name

        });

        AddCell(grid, 1, Muted(Preview(recipe.Command, 80)));

        AddCell(grid, 2, Muted(variables.Count == 0 ? "—" : string.Join(", ", variables)));



        var actions = CompactRowActions(

            CompactActionButton("Запуск", () => RunCommandRecipe(recipe)),

            CompactActionButton("Копир.", () => CopyCommandRecipe(recipe)),

            CompactIconButton(EditIconButton(() => EditCommandRecipe(recipe))));

        AddCell(grid, 3, actions);



        return WrapTableRow(grid, () => RunCommandRecipe(recipe));

    }

}


