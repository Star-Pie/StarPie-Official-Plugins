using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace StarPie.Plugin.EmojiPicker;

internal static class EmojiDialogs
{
    internal static string? PromptGroupName(
        IPluginContext context,
        EmojiUiTheme theme,
        Window? owner,
        string title,
        string initialValue)
    {
        var box = new TextBox { Text = initialValue, Height = 30 };
        var body = new StackPanel();
        body.Children.Add(Label(EmojiTexts.T(context, "dlgGroupName"), theme));
        body.Children.Add(box);

        if (!ShowDialogWindow(context, theme, owner, title, 360, body, box))
            return null;

        string val = box.Text.Trim();
        return string.IsNullOrEmpty(val) ? null : val;
    }

    internal static (string Name, string Keywords)? EditItemProperties(
        IPluginContext context,
        EmojiUiTheme theme,
        Window? owner,
        string name,
        string keywords)
    {
        var nameBox = new TextBox { Text = name, Height = 30, Margin = new Thickness(0, 0, 0, 10) };
        var kwBox = new TextBox { Text = keywords, Height = 30 };
        var body = new StackPanel();
        body.Children.Add(Label(EmojiTexts.T(context, "colName"), theme));
        body.Children.Add(nameBox);
        body.Children.Add(Label(EmojiTexts.T(context, "colKeywords"), theme));
        body.Children.Add(kwBox);

        if (!ShowDialogWindow(context, theme, owner, EmojiTexts.T(context, "dlgEditTitle"), 380, body, nameBox))
            return null;

        string nextName = nameBox.Text.Trim();
        return (string.IsNullOrEmpty(nextName) ? name : nextName, kwBox.Text.Trim());
    }

    internal static (string Text, string Name, string Keywords, string Category)? ShowAddKaomojiDialog(
        IPluginContext context,
        EmojiUiTheme theme,
        Window? owner,
        string? defaultCategory = null)
    {
        var textBox = new TextBox { Height = 32, Margin = new Thickness(0, 0, 0, 8) };
        var nameBox = new TextBox { Height = 30, Margin = new Thickness(0, 0, 0, 8) };
        var kwBox = new TextBox { Height = 30, Margin = new Thickness(0, 0, 0, 8) };
        var catBox = new ComboBox { Height = 30 };

        foreach (string cat in EmojiCatalog.KaomojiCategories)
            catBox.Items.Add(new ComboBoxItem { Tag = cat, Content = EmojiTexts.T(context, cat) });
        catBox.SelectedIndex = Math.Max(0, Array.IndexOf(EmojiCatalog.KaomojiCategories, defaultCategory ?? "kaoHappy"));

        var body = new StackPanel();
        body.Children.Add(Label(EmojiTexts.T(context, "colText"), theme));
        body.Children.Add(textBox);
        body.Children.Add(Label(EmojiTexts.T(context, "colName"), theme));
        body.Children.Add(nameBox);
        body.Children.Add(Label(EmojiTexts.T(context, "colKeywords"), theme));
        body.Children.Add(kwBox);
        body.Children.Add(Label(EmojiTexts.T(context, "colCategory"), theme));
        body.Children.Add(catBox);

        if (!ShowDialogWindow(context, theme, owner, EmojiTexts.T(context, "dlgKaomojiTitle"), 400, body, textBox))
            return null;

        string text = textBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return null;

        string name = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = text.Length > 14 ? text[..14] : text;
        string catTag = (catBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "kaoHappy";
        return (text, name, kwBox.Text.Trim(), catTag);
    }

    internal static (string Path, string DisplayName)? ShowMountFolderDialog(
        IPluginContext context,
        EmojiUiTheme theme,
        Window? owner)
    {
        var hint = new Border
        {
            Background = theme.Surface,
            BorderBrush = theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock
            {
                Text = EmojiTexts.T(context, "dlgMountHint"),
                Foreground = theme.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5
            }
        };

        var pathBox = new TextBox { Height = 30 };
        var browseBtn = new Button
        {
            Content = EmojiTexts.T(context, "dlgMountBrowse"),
            Margin = new Thickness(6, 0, 0, 0),
            Height = 30
        };
        var nameBox = new TextBox { Height = 30 };

        browseBtn.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = EmojiTexts.T(context, "dlgMountTitle")
            };
            if (dlg.ShowDialog() == true)
            {
                pathBox.Text = dlg.FolderName;
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                    nameBox.Text = new DirectoryInfo(dlg.FolderName).Name;
            }
        };

        var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(pathBox, 0);
        Grid.SetColumn(browseBtn, 1);
        pathRow.Children.Add(pathBox);
        pathRow.Children.Add(browseBtn);

        var body = new StackPanel();
        body.Children.Add(hint);
        body.Children.Add(Label(EmojiTexts.T(context, "dlgMountPath"), theme));
        body.Children.Add(pathRow);
        body.Children.Add(Label(EmojiTexts.T(context, "dlgMountDisplayName"), theme));
        body.Children.Add(nameBox);

        if (!ShowDialogWindow(context, theme, owner, EmojiTexts.T(context, "dlgMountTitle"), 420, body, pathBox))
            return null;

        string path = pathBox.Text.Trim();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return null;
        string display = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(display)) display = new DirectoryInfo(path).Name;
        return (path, display);
    }

    internal static (string Name, string Keywords, string GroupId)? ShowClipboardFavoriteDialog(
        IPluginContext context,
        EmojiUiTheme theme,
        Window? owner,
        ClipboardImageReadResult read,
        IReadOnlyList<StickerGroup> managedGroups,
        string defaultGroupId)
    {
        if (read.TooLarge)
        {
            ShowInfoDialog(context, theme, owner, EmojiTexts.T(context, "dlgClipboardTitle"), EmojiTexts.T(context, "dlgClipboardTooLarge"));
            return null;
        }
        if (read.Bytes == null || read.Bytes.Length == 0)
        {
            ShowInfoDialog(context, theme, owner, EmojiTexts.T(context, "dlgClipboardTitle"), EmojiTexts.T(context, "dlgClipboardEmpty"));
            return null;
        }

        BitmapSource? previewImg = DecodePreview(read.Bytes);
        var previewCard = new Border
        {
            Background = theme.Surface,
            BorderBrush = theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var pGrid = new Grid();
        pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
        pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var thumbBorder = new Border
        {
            Width = 60,
            Height = 60,
            CornerRadius = new CornerRadius(7),
            Background = theme.WindowBg,
            BorderBrush = theme.StickerOutline,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4)
        };
        if (previewImg != null)
            thumbBorder.Child = new Image { Source = previewImg, Stretch = System.Windows.Media.Stretch.Uniform };
        pGrid.Children.Add(thumbBorder);

        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        infoStack.Children.Add(new TextBlock
        {
            Text = $"{read.Format} · {(read.Bytes.Length / 1024.0):F1} KB",
            Foreground = theme.TextPrimary,
            FontWeight = FontWeights.SemiBold
        });
        Grid.SetColumn(infoStack, 1);
        pGrid.Children.Add(infoStack);
        previewCard.Child = pGrid;

        var nameBox = new TextBox { Text = read.SuggestedName, Height = 30, Margin = new Thickness(0, 0, 0, 8) };
        var kwBox = new TextBox { Text = read.SuggestedName, Height = 30, Margin = new Thickness(0, 0, 0, 8) };
        var grpCombo = new ComboBox { Height = 30 };

        int sel = 0;
        for (int i = 0; i < managedGroups.Count; i++)
        {
            grpCombo.Items.Add(new ComboBoxItem { Tag = managedGroups[i].Id, Content = managedGroups[i].Name });
            if (string.Equals(managedGroups[i].Id, defaultGroupId, StringComparison.OrdinalIgnoreCase)) sel = i;
        }
        if (grpCombo.Items.Count > 0) grpCombo.SelectedIndex = sel;

        var body = new StackPanel();
        body.Children.Add(previewCard);
        body.Children.Add(Label(EmojiTexts.T(context, "colName"), theme));
        body.Children.Add(nameBox);
        body.Children.Add(Label(EmojiTexts.T(context, "colKeywords"), theme));
        body.Children.Add(kwBox);
        body.Children.Add(Label(EmojiTexts.T(context, "colGroup"), theme));
        body.Children.Add(grpCombo);

        if (!ShowDialogWindow(context, theme, owner, EmojiTexts.T(context, "dlgClipboardTitle"), 400, body, nameBox))
            return null;

        string grpId = (grpCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? managedGroups.FirstOrDefault()?.Id ?? "";
        return (string.IsNullOrWhiteSpace(nameBox.Text) ? read.SuggestedName : nameBox.Text.Trim(), kwBox.Text.Trim(), grpId);
    }

    internal static void ShowInfoDialog(IPluginContext context, EmojiUiTheme theme, Window? owner, string title, string message)
    {
        var body = new TextBlock
        {
            Text = message,
            Foreground = theme.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        };

        var dlg = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner is { IsVisible: true } ? owner : null,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true
        };
        theme.ApplyTo(dlg);

        var ok = new Button
        {
            Content = EmojiTexts.T(context, "ok"),
            IsDefault = true,
            Background = theme.Accent,
            Foreground = theme.AccentFg,
            BorderBrush = theme.Accent,
            MinWidth = 76,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        ok.Click += (_, _) => dlg.Close();

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(body);
        root.Children.Add(ok);
        dlg.Content = root;
        dlg.ShowDialog();
    }

    private static TextBlock Label(string text, EmojiUiTheme theme) => new()
    {
        Text = text,
        Foreground = theme.TextMuted,
        FontSize = 11,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static bool ShowDialogWindow(
        IPluginContext context,
        EmojiUiTheme theme,
        Window? owner,
        string title,
        double width,
        UIElement body,
        Control? focusControl)
    {
        var dlg = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner is { IsVisible: true } ? owner : null,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true
        };
        theme.ApplyTo(dlg);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(body);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var cancel = new Button
        {
            Content = EmojiTexts.T(context, "cancel"),
            IsCancel = true,
            MinWidth = 72,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var ok = new Button
        {
            Content = EmojiTexts.T(context, "ok"),
            IsDefault = true,
            MinWidth = 76,
            Background = theme.Accent,
            Foreground = theme.AccentFg,
            BorderBrush = theme.Accent
        };
        ok.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };

        btnRow.Children.Add(cancel);
        btnRow.Children.Add(ok);
        root.Children.Add(btnRow);

        dlg.Content = root;
        dlg.Loaded += (_, _) => focusControl?.Focus();
        return dlg.ShowDialog() == true;
    }

    private static BitmapSource? DecodePreview(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 128;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
