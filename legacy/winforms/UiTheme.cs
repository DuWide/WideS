namespace DevCockpit;

public enum ButtonKind
{
    Primary,
    Secondary,
    Danger,
    Outline
}

public static class UiTheme
{
    public static readonly Color App = Color.FromArgb(18, 20, 24);
    public static readonly Color Sidebar = Color.FromArgb(23, 26, 31);
    public static readonly Color Panel = Color.FromArgb(30, 34, 40);
    public static readonly Color Card = Color.FromArgb(36, 41, 48);
    public static readonly Color CardAlt = Color.FromArgb(42, 48, 56);
    public static readonly Color Border = Color.FromArgb(62, 70, 82);
    public static readonly Color Accent = Color.FromArgb(69, 153, 184);
    public static readonly Color AccentDark = Color.FromArgb(43, 105, 132);
    public static readonly Color Success = Color.FromArgb(95, 168, 130);
    public static readonly Color Warning = Color.FromArgb(211, 163, 79);
    public static readonly Color Danger = Color.FromArgb(190, 91, 91);
    public static readonly Color Text = Color.FromArgb(235, 239, 244);
    public static readonly Color Muted = Color.FromArgb(155, 164, 176);

    public static readonly Font TitleFont = new("Segoe UI Semibold", 18);
    public static readonly Font SectionFont = new("Segoe UI Semibold", 11);
    public static readonly Font BodyFont = new("Segoe UI", 10);
    public static readonly Font SmallFont = new("Segoe UI", 8.8f);

    public static Button Button(string text, ButtonKind kind = ButtonKind.Secondary)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 36,
            MinimumSize = new Size(96, 36),
            Padding = new Padding(12, 0, 12, 0),
            FlatStyle = FlatStyle.Flat,
            Font = BodyFont,
            ForeColor = Text,
            Margin = new Padding(4),
            Cursor = Cursors.Hand
        };

        var (back, border) = kind switch
        {
            ButtonKind.Primary => (AccentDark, Accent),
            ButtonKind.Danger => (Color.FromArgb(88, 43, 48), Danger),
            ButtonKind.Outline => (Panel, Border),
            _ => (Color.FromArgb(49, 56, 66), Border)
        };

        button.BackColor = back;
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.MouseOverBackColor = Blend(back, Color.White, 0.08);
        button.FlatAppearance.MouseDownBackColor = Blend(back, Color.Black, 0.12);
        return button;
    }

    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text,
        AutoSize = false,
        Dock = DockStyle.Top,
        Height = font == TitleFont ? 32 : 24,
        Font = font ?? BodyFont,
        ForeColor = color ?? Text
    };

    public static void StyleTextBox(TextBox box)
    {
        box.BackColor = Color.FromArgb(27, 31, 37);
        box.ForeColor = Text;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = BodyFont;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Card;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(43, 49, 58);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
        grid.ColumnHeadersDefaultCellStyle.Font = SectionFont;
        grid.DefaultCellStyle.BackColor = Card;
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(49, 82, 100);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.DefaultCellStyle.Font = BodyFont;
        grid.GridColor = Border;
        grid.EnableHeadersVisualStyles = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.RowTemplate.Height = 34;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    }

    public static Color Blend(Color a, Color b, double amount)
    {
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));
    }
}

public sealed class CardPanel : Panel
{
    public string Title { get; set; } = "";
    public string IconText { get; set; } = "";

    public CardPanel()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.Card;
        Padding = new Padding(14, 42, 14, 14);
        Margin = new Padding(8);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(UiTheme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using var title = new SolidBrush(UiTheme.Text);
        using var icon = new SolidBrush(UiTheme.Accent);
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        e.Graphics.DrawString(IconText, UiTheme.SectionFont, icon, 14, 13);
        e.Graphics.DrawString(Title, UiTheme.SectionFont, title, string.IsNullOrWhiteSpace(IconText) ? 14 : 38, 13);
    }
}

public sealed class ProjectListBox : ListBox
{
    public ProjectListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 58;
        BorderStyle = BorderStyle.None;
        BackColor = UiTheme.Sidebar;
        ForeColor = UiTheme.Text;
        IntegralHeight = false;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var project = Items[e.Index] as ProjectProfile;
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using var back = new SolidBrush(selected ? Color.FromArgb(43, 65, 78) : UiTheme.Sidebar);
        e.Graphics.FillRectangle(back, e.Bounds);

        var rect = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top + 6, e.Bounds.Width - 16, e.Bounds.Height - 12);
        using var card = new SolidBrush(selected ? Color.FromArgb(52, 76, 91) : Color.FromArgb(30, 34, 40));
        using var border = new Pen(selected ? UiTheme.Accent : Color.FromArgb(42, 48, 56));
        e.Graphics.FillRectangle(card, rect);
        e.Graphics.DrawRectangle(border, rect);

        using var title = new SolidBrush(UiTheme.Text);
        using var muted = new SolidBrush(UiTheme.Muted);
        e.Graphics.DrawString(project?.Name ?? "(без названия)", UiTheme.SectionFont, title, rect.Left + 12, rect.Top + 8);
        var path = project?.ProjectFolder ?? "";
        if (path.Length > 42) path = "..." + path[^39..];
        e.Graphics.DrawString(path, UiTheme.SmallFont, muted, rect.Left + 12, rect.Top + 31);
    }
}
