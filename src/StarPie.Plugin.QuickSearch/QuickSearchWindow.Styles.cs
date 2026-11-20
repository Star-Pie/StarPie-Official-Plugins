using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace StarPie.Plugin.QuickSearch;

internal sealed partial class QuickSearchWindow
{
    // Templates only reference WPF types. No pack URI needs to resolve the plugin from the host load context.
    private static readonly ConditionalWeakTable<Dispatcher, ResourceDictionary> Templates = new();
    private readonly ResourceDictionary _colors = new();

    private void InstallStyles()
    {
        Resources.MergedDictionaries.Add(Templates.GetValue(Dispatcher, _ => (ResourceDictionary)XamlReader.Parse(StyleMarkup)));
        Resources.MergedDictionaries.Add(_colors);
        RefreshColors();
        Loaded += (_, _) => SystemParameters.StaticPropertyChanged += OnSystemSettingsChanged;
        Unloaded += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemSettingsChanged;
    }

    private void OnSystemSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.HighContrast) or "" or null)
        {
            if (Dispatcher.CheckAccess()) RefreshColors();
            else Dispatcher.BeginInvoke(() => { if (!_closed) RefreshColors(); });
        }
    }

    private static SolidColorBrush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void RefreshColors()
    {
        // Let the host's dynamic resources take precedence. The same dictionary also serves
        // the detached menu, and supplies local colours in an SDK-only host or forced colours.
        _colors.Clear();
        bool contrast = SystemParameters.HighContrast;
        (string Key, string Fallback, Brush System)[] definitions =
        [
            ("CardBackgroundBrush", "#F4F9F8", SystemColors.WindowBrush),
            ("CardBorderBrush", "#CFDEDC", SystemColors.WindowTextBrush),
            ("TextPrimaryBrush", "#193C3B", SystemColors.WindowTextBrush),
            ("TextSecondaryBrush", "#4E6867", SystemColors.WindowTextBrush),
            ("InputBackgroundBrush", "#FFFFFF", SystemColors.WindowBrush),
            ("InputBorderBrush", "#C3D5D2", SystemColors.WindowTextBrush),
            ("ButtonDefaultBgBrush", "#F9FCFB", SystemColors.ControlBrush),
            ("ItemHoverBrush", "#E8F3F0", SystemColors.ControlBrush),
            ("AccentPrimaryBrush", "#216B62", SystemColors.HighlightBrush),
            ("AccentTextBrush", "#FFFFFF", SystemColors.HighlightTextBrush),
            ("NavTabActiveBgBrush", "#DDEFEA", SystemColors.HighlightBrush),
            ("NavTabActiveFgBrush", "#174C42", SystemColors.HighlightTextBrush)
        ];
        foreach (var (key, fallback, system) in definitions)
            if (contrast || TryFindResource(key) is not Brush)
                _colors[key] = contrast ? system : Solid(fallback);

        // Tinted tiles have their own foreground/background pair on both light and dark surfaces.
        (string Key, string Fill, string Ink)[] tiles =
        [
            ("apps", "#DDEDE9", "#205C50"), ("cad", "#DEEAF6", "#335C82"),
            ("docs", "#E4E9F9", "#454F87"), ("video", "#F0E4F4", "#784B80"),
            ("folders", "#F8ECD3", "#7A5A15"), ("system", "#DCEFED", "#256C65"),
            ("file", "#E5ECEC", "#46615F"), ("taskmgr", "#DCEAF8", "#2A5E8F"),
            ("control", "#D9EFE7", "#26644F"), ("calc", "#ECE5F7", "#694896")
        ];
        foreach (var (key, fill, ink) in tiles)
        {
            _colors["QuickSearch.Tile." + key] = contrast ? SystemColors.ControlBrush : Solid(fill);
            _colors["QuickSearch.Ink." + key] = contrast ? SystemColors.ControlTextBrush : Solid(ink);
        }
        _colors["QuickSearch.SelectedEdge"] = contrast ? SystemColors.HighlightTextBrush :
            TryFindResource("AccentPrimaryBrush") as Brush ?? Solid("#95C5B9");
        _colors["QuickSearch.Disabled"] = contrast ? SystemColors.GrayTextBrush :
            TryFindResource("TextSecondaryBrush") as Brush ?? SystemColors.GrayTextBrush;
    }

    private void StyleMenu(ContextMenu menu)
    {
        foreach (var dictionary in Resources.MergedDictionaries) menu.Resources.MergedDictionaries.Add(dictionary);
        menu.Style = (Style)FindResource("QuickSearch.Menu");
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.Icon = DrawGlyph((string)item.Tag, 16);
            item.Style = (Style)FindResource("QuickSearch.MenuItem");
        }
        foreach (var separator in menu.Items.OfType<Separator>()) separator.Style = (Style)FindResource("QuickSearch.Divider");
    }

    private const string StyleMarkup = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style x:Key="QuickSearch.Input" TargetType="TextBox">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
            <Setter Property="CaretBrush" Value="{DynamicResource TextPrimaryBrush}"/>
            <Setter Property="Padding" Value="8,0"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Template">
              <Setter.Value><ControlTemplate TargetType="TextBox">
                <Border Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}">
                  <ScrollViewer x:Name="PART_ContentHost" VerticalAlignment="Center" Focusable="False"/>
                </Border>
              </ControlTemplate></Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="QuickSearch.Button" TargetType="Button">
            <Setter Property="Background" Value="{DynamicResource ButtonDefaultBgBrush}"/>
            <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource CardBorderBrush}"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="8,3"/>
            <Setter Property="MinHeight" Value="27"/>
            <Setter Property="FontSize" Value="12"/>
            <Setter Property="Template">
              <Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="Shell" CornerRadius="10" Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"
                        Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
                  <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" RecognizesAccessKey="True"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Shell" Property="Background" Value="{DynamicResource ItemHoverBrush}"/>
                    <Setter TargetName="Shell" Property="BorderBrush" Value="{DynamicResource AccentPrimaryBrush}"/>
                  </Trigger>
                  <Trigger Property="Selector.IsSelected" Value="True">
                    <Setter TargetName="Shell" Property="Background" Value="{DynamicResource NavTabActiveBgBrush}"/>
                    <Setter TargetName="Shell" Property="BorderBrush" Value="{DynamicResource QuickSearch.SelectedEdge}"/>
                    <Setter Property="Foreground" Value="{DynamicResource NavTabActiveFgBrush}"/>
                    <Setter Property="FontWeight" Value="SemiBold"/>
                  </Trigger>
                  <Trigger Property="IsPressed" Value="True">
                    <Setter TargetName="Shell" Property="Background" Value="{DynamicResource AccentPrimaryBrush}"/>
                    <Setter Property="Foreground" Value="{DynamicResource AccentTextBrush}"/>
                  </Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="Shell" Property="BorderBrush" Value="{DynamicResource AccentPrimaryBrush}"/>
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Foreground" Value="{DynamicResource QuickSearch.Disabled}"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="QuickSearch.Tool" TargetType="Button" BasedOn="{StaticResource QuickSearch.Button}">
            <Setter Property="Width" Value="30"/><Setter Property="Height" Value="30"/>
            <Setter Property="Padding" Value="4"/><Setter Property="Margin" Value="1,0"/>
            <Setter Property="Background" Value="Transparent"/><Setter Property="BorderBrush" Value="Transparent"/>
          </Style>
          <Style x:Key="QuickSearch.RowOpen" TargetType="Button" BasedOn="{StaticResource QuickSearch.Tool}">
            <Style.Triggers>
              <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType={x:Type ListBoxItem}}}" Value="True">
                <Setter Property="Foreground" Value="{DynamicResource NavTabActiveFgBrush}"/>
              </DataTrigger>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="{DynamicResource ItemHoverBrush}"/>
                <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
              </Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True">
                <Setter Property="Background" Value="{DynamicResource ItemHoverBrush}"/>
                <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter Property="Foreground" Value="{DynamicResource AccentTextBrush}"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Foreground" Value="{DynamicResource QuickSearch.Disabled}"/>
              </Trigger>
            </Style.Triggers>
          </Style>
          <Style x:Key="QuickSearch.Row" TargetType="ListBoxItem">
            <Setter Property="Padding" Value="9,5"/>
            <Setter Property="Margin" Value="1,1"/>
            <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
            <Setter Property="Template">
              <Setter.Value><ControlTemplate TargetType="ListBoxItem">
                <Border x:Name="Shell" CornerRadius="10" Background="Transparent" BorderBrush="Transparent"
                        BorderThickness="1" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
                  <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Center"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="Shell" Property="Background" Value="{DynamicResource ItemHoverBrush}"/>
                  </Trigger>
                  <Trigger Property="IsSelected" Value="True">
                    <Setter TargetName="Shell" Property="Background" Value="{DynamicResource NavTabActiveBgBrush}"/>
                    <Setter TargetName="Shell" Property="BorderBrush" Value="{DynamicResource QuickSearch.SelectedEdge}"/>
                    <Setter Property="Foreground" Value="{DynamicResource NavTabActiveFgBrush}"/>
                  </Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True">
                    <Setter TargetName="Shell" Property="BorderBrush" Value="{DynamicResource AccentPrimaryBrush}"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="QuickSearch.PathText" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource TextSecondaryBrush}"/>
            <Style.Triggers>
              <DataTrigger Binding="{Binding IsSelected, RelativeSource={RelativeSource AncestorType={x:Type ListBoxItem}}}" Value="True">
                <Setter Property="Foreground" Value="{DynamicResource NavTabActiveFgBrush}"/>
              </DataTrigger>
            </Style.Triggers>
          </Style>
          <Style x:Key="QuickSearch.Menu" TargetType="ContextMenu">
            <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
            <Setter Property="Background" Value="{DynamicResource CardBackgroundBrush}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource CardBorderBrush}"/>
            <Setter Property="Template">
              <Setter.Value><ControlTemplate TargetType="ContextMenu">
                <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="1" CornerRadius="10" Padding="4">
                  <ScrollViewer VerticalScrollBarVisibility="Auto" CanContentScroll="True">
                    <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/>
                  </ScrollViewer>
                </Border>
              </ControlTemplate></Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="QuickSearch.MenuItem" TargetType="MenuItem">
            <Setter Property="Foreground" Value="{DynamicResource TextPrimaryBrush}"/>
            <Setter Property="Template">
              <Setter.Value><ControlTemplate TargetType="MenuItem">
                <Border x:Name="Shell" Background="Transparent" Padding="8,6" CornerRadius="6">
                  <DockPanel>
                    <ContentPresenter DockPanel.Dock="Left" ContentSource="Icon" Margin="0,0,9,0" VerticalAlignment="Center"/>
                    <TextBlock DockPanel.Dock="Right" FontSize="11" Margin="16,0,0,0" VerticalAlignment="Center" Text="{TemplateBinding InputGestureText}"/>
                    <ContentPresenter ContentSource="Header" VerticalAlignment="Center" RecognizesAccessKey="True"/>
                  </DockPanel>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsHighlighted" Value="True">
                    <Setter TargetName="Shell" Property="Background" Value="{DynamicResource NavTabActiveBgBrush}"/>
                    <Setter Property="Foreground" Value="{DynamicResource NavTabActiveFgBrush}"/>
                  </Trigger>
                  <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Foreground" Value="{DynamicResource QuickSearch.Disabled}"/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="QuickSearch.Divider" TargetType="Separator">
            <Setter Property="Template">
              <Setter.Value><ControlTemplate TargetType="Separator">
                <Border Height="1" Margin="7,3" Background="{DynamicResource CardBorderBrush}"/>
              </ControlTemplate></Setter.Value>
            </Setter>
          </Style>
        </ResourceDictionary>
        """;
}
