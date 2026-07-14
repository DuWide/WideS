using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace DevCockpit;

public static class UiHelpers
{
    public static WpfTextBox CreateSearchBox(string hint, Action render, int debounceMs = 250, double minWidth = 320)
    {
        var box = new WpfTextBox
        {
            MinWidth = minWidth,
            Margin = new Thickness(8),
            ToolTip = hint,
            Tag = hint
        };

        if (System.Windows.Application.Current.TryFindResource("SearchTextBox") is Style style)
        {
            box.Style = style;
        }

        ApplyWatermark(box, hint);

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(debounceMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            render();
        };
        box.TextChanged += (_, _) =>
        {
            timer.Stop();
            timer.Start();
        };
        return box;
    }

    public static string EffectiveText(WpfTextBox box)
    {
        if (box.Tag is string hint && string.Equals(box.Text, hint, StringComparison.Ordinal))
        {
            return "";
        }

        return box.Text.Trim();
    }

    private static void ApplyWatermark(WpfTextBox box, string hint)
    {
        var muted = GetBrush("MutedBrush");
        var normal = GetBrush("TextBrush");
        box.Text = hint;
        box.Foreground = muted;
        box.GotFocus += (_, _) =>
        {
            if (string.Equals(box.Text, hint, StringComparison.Ordinal))
            {
                box.Text = "";
                box.Foreground = normal;
            }
        };
        box.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = hint;
                box.Foreground = muted;
            }
        };
    }

    public static Border EmptyState(string title, string hint, string actionText, Action action)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = GetBrush("TextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 13,
            Foreground = GetBrush("MutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });
        var button = new WpfButton
        {
            Content = actionText,
            Style = System.Windows.Application.Current.TryFindResource("PrimaryButton") as Style,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, _) => action();
        stack.Children.Add(button);

        return new Border
        {
            Style = System.Windows.Application.Current.TryFindResource("Card") as Style,
            Width = 760,
            MinHeight = 160,
            Child = stack,
            Margin = new Thickness(8)
        };
    }

    public static Border TypeBadge(string text)
    {
        return new Border
        {
            Background = GetBrush("AccentSoftBgBrush"),
            BorderBrush = GetBrush("AccentBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = GetBrush("AccentBrush")
            }
        };
    }

    public static string ProjectStatusDisplay(string? status) => status switch
    {
        "Paused" => "На паузе",
        "Archive" => "Архив",
        _ => "Активен"
    };

    public static WrapPanel ToolbarRow() =>
        new() { Margin = new Thickness(8, 4, 8, 4) };

    public static System.Windows.Controls.ComboBox CreateToolbarComboBox(double minWidth = 180)
    {
        var box = new System.Windows.Controls.ComboBox
        {
            MinWidth = minWidth,
            Height = 36,
            MinHeight = 36,
            MaxHeight = 36,
            Margin = new Thickness(3),
            VerticalAlignment = VerticalAlignment.Top,
            IsEditable = false
        };
        if (System.Windows.Application.Current.TryFindResource("ToolbarComboBox") is Style style)
        {
            box.Style = style;
        }
        else if (System.Windows.Application.Current.TryFindResource("DarkComboBox") is Style darkStyle)
        {
            box.Style = darkStyle;
        }
        return box;
    }

    public static async Task<bool?> CheckReachabilityAsync(string address, string type)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var host = address.Split(':')[0].Trim();
        var port = type.Equals("RDP", StringComparison.OrdinalIgnoreCase) ? 3389 : 22;
        if (address.Contains(':'))
        {
            var parts = address.Split(':');
            if (parts.Length > 1 && int.TryParse(parts[^1], out var customPort))
            {
                port = customPort;
            }
        }

        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(2000));
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    public static string RelativeDays(DateTime? date)
    {
        if (date is null) return "никогда";
        var days = (DateTime.Now.Date - date.Value.Date).Days;
        return days switch
        {
            0 => "сегодня",
            1 => "вчера",
            _ => $"{days} дн. назад"
        };
    }

    public static void SetAlbumArt(System.Windows.Controls.Image? image, byte[]? bytes)
    {
        if (image is null) return;
        if (bytes is null || bytes.Length == 0)
        {
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
            image.Visibility = Visibility.Visible;
        }
        catch
        {
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
        }
    }

    public static void BuildNotePreview(System.Windows.Controls.Panel panel, string text)
    {
        panel.Children.Clear();
        if (string.IsNullOrEmpty(text))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "(пусто)",
                Foreground = GetBrush("MutedBrush"),
                FontSize = 13
            });
            return;
        }

        var parts = text.Split("```", StringSplitOptions.None);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (string.IsNullOrWhiteSpace(part)) continue;
            var isCode = i % 2 == 1;
            if (isCode)
            {
                panel.Children.Add(new Border
                {
                    Background = GetBrush("PanelBrush"),
                    BorderBrush = GetBrush("AccentBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 4),
                    Child = new TextBlock
                    {
                        Text = part.Trim('\r', '\n'),
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        FontSize = 13,
                        LineHeight = 17,
                        Foreground = GetBrush("TextBrush"),
                        TextWrapping = TextWrapping.Wrap
                    }
                });
            }
            else
            {
                panel.Children.Add(new TextBlock
                {
                    Text = part.Trim(),
                    FontSize = 14,
                    LineHeight = 18,
                    Foreground = GetBrush("TextBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 2)
                });
            }
        }
    }

    private static WpfBrush GetBrush(string key)
        => System.Windows.Application.Current.TryFindResource(key) as WpfBrush ?? WpfBrushes.White;
}
