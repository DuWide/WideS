namespace DevCockpit;

internal static class WinFormsDialogHelpers
{
    internal static Button DarkButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Height = 34,
        BackColor = Color.FromArgb(55, 55, 58),
        ForeColor = Color.WhiteSmoke,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(4)
    };

    internal static void StyleTextBox(TextBox box)
    {
        box.Dock = DockStyle.Fill;
        box.BackColor = Color.FromArgb(42, 42, 45);
        box.ForeColor = Color.WhiteSmoke;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Margin = new Padding(4, 6, 4, 6);
    }
}
