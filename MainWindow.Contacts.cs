using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Media;

using WpfBrush = System.Windows.Media.Brush;

using WpfButton = System.Windows.Controls.Button;

using WpfTextBox = System.Windows.Controls.TextBox;

using WpfClipboard = System.Windows.Clipboard;

using WpfMessageBox = System.Windows.MessageBox;



namespace DevCockpit;



public partial class MainWindow

{

    private void ShowContacts()

    {

        EnterView("contacts");

        _viewScope = "contacts";

        SetTitle("Клиенты", "Контакты из задач: телефоны и связанные задачи");



        var root = new DockPanel();

        var toolbar = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        var row = UiHelpers.ToolbarRow();

        var list = ItemsPanel();

        WpfTextBox search = null!;



        void Render()

        {

            list.Children.Clear();

            var contacts = ContactAggregator.Build(_tasks.Tasks, UiHelpers.EffectiveText(search))

                .OrderBy(c => string.IsNullOrWhiteSpace(c.Name) ? c.Phone : c.Name, StringComparer.CurrentCultureIgnoreCase)

                .ToList();

            if (contacts.Count == 0)

            {

                list.Children.Add(UiHelpers.EmptyState(

                    "Контактов нет",

                    "Добавьте имя или телефон в задачах — они появятся здесь.",

                    "К задачам",

                    ShowTasks));

                return;

            }



            if (IsTableView(_viewScope))

            {

                if (list is StackPanel stack)

                {

                    stack.Children.Add(ContactTableHeader());

                }

            }



            foreach (var contact in contacts)

            {

                list.Children.Add(ContactCard(contact));

            }

        }



        search = SearchBox("Поиск по имени или телефону", Render);

        row.Children.Add(search);

        AddViewModeButtons(row, ShowContacts);

        toolbar.Children.Add(row);

        DockPanel.SetDock(toolbar, Dock.Top);

        root.Children.Add(toolbar);

        Render();

        root.Children.Add(list);

        ContentHost.Content = root;

    }



    private FrameworkElement ContactCard(ContactSummary contact, bool listView = false)

    {

        if (IsTableView(_viewScope))

        {

            return ContactCompactRow(contact);

        }



        var title = string.IsNullOrWhiteSpace(contact.Name) ? "(без имени)" : contact.Name;

        var subtitle = string.IsNullOrWhiteSpace(contact.Phone)

            ? $"{contact.OpenTaskCount} открытых · {contact.TaskCount} всего"

            : $"{contact.Phone} · {contact.OpenTaskCount} открытых";



        if (IsListView(_viewScope) || listView)

        {

            var actions = new List<UIElement>();

            if (!string.IsNullOrWhiteSpace(contact.Phone))

            {

                actions.Add(ActionButton("Копировать", () => Copy(contact.Phone, "Телефон скопирован"), false));

            }

            actions.Add(ActionButton("Задачи", () => ShowTasksForContact(contact), false));

            return ListRow(title, () => ShowTasksForContact(contact), null, actions.ToArray());

        }



        var card = Card(title);

        ApplyCardView(card, 320);

        card.MinHeight = 140;

        var stack = BaseCardStack(title);

        stack.Children.Add(Muted(subtitle));

        if (contact.LastActivityAt is { } lastAt)

        {

            stack.Children.Add(Muted($"Последняя задача: {lastAt:dd.MM.yyyy}"));

        }



        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

        if (!string.IsNullOrWhiteSpace(contact.Phone))

        {

            buttons.Children.Add(ActionButton("Копировать телефон", () => Copy(contact.Phone, "Телефон скопирован"), false));

        }

        buttons.Children.Add(ActionButton("Задачи клиента", () => ShowTasksForContact(contact), false));

        stack.Children.Add(buttons);

        card.Child = stack;

        return card;

    }



    private Border ContactTableHeader() => BuildTableHeader(

        ("Клиент", new GridLength(1.6, GridUnitType.Star)),

        ("Телефон", new GridLength(130)),

        ("Задачи", new GridLength(110)),

        ("Действия", GridLength.Auto));



    private Border ContactCompactRow(ContactSummary contact)

    {

        var grid = CreateTableGrid(

            new GridLength(1.6, GridUnitType.Star),

            new GridLength(130),

            new GridLength(110),

            GridLength.Auto);



        var title = string.IsNullOrWhiteSpace(contact.Name) ? "(без имени)" : contact.Name;

        AddCell(grid, 0, new TextBlock

        {

            Text = title,

            FontSize = 13,

            FontWeight = FontWeights.SemiBold,

            Foreground = (WpfBrush)FindResource("TextBrush"),

            TextTrimming = TextTrimming.CharacterEllipsis,

            ToolTip = title

        });

        AddCell(grid, 1, Muted(string.IsNullOrWhiteSpace(contact.Phone) ? "—" : contact.Phone));

        AddCell(grid, 2, Muted($"{contact.OpenTaskCount} откр. / {contact.TaskCount}"));



        var actions = CompactRowActions();

        if (!string.IsNullOrWhiteSpace(contact.Phone))

        {

            actions.Children.Add(CompactActionButton("Копир.", () => Copy(contact.Phone, "Телефон скопирован")));

        }

        actions.Children.Add(CompactActionButton("Задачи", () => ShowTasksForContact(contact)));

        AddCell(grid, 3, actions);



        return WrapTableRow(grid, () => ShowTasksForContact(contact));

    }



    private void ShowTasksForContact(ContactSummary contact)

    {

        _contactFilterKey = contact.Key;

        _contactFilterLabel = string.IsNullOrWhiteSpace(contact.Name) ? contact.Phone : contact.Name;

        ShowTasks();

    }

}


