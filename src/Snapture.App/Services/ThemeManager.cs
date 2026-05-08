using System.Windows;
using Microsoft.Win32;

namespace Snapture.App.Services;

public static class ThemeManager
{
    public const string SystemMode = "system";
    public const string LightMode = "light";
    public const string DarkMode = "dark";

    private static readonly Uri DarkThemeUri = new("pack://application:,,,/Resources/Themes/CatppuccinMocha.xaml", UriKind.Absolute);
    private static readonly Uri LightThemeUri = new("pack://application:,,,/Resources/Themes/CatppuccinLatte.xaml", UriKind.Absolute);
    private static bool _initialized;

    public static string RequestedMode { get; private set; } = SystemMode;
    public static string EffectiveMode { get; private set; } = DarkMode;

    public static event EventHandler? ThemeChanged;

    public static void Initialize(string? mode)
    {
        if (!_initialized)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            Application.Current.Exit += (_, _) => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _initialized = true;
        }

        Apply(mode);
    }

    public static void Apply(string? mode)
    {
        RequestedMode = NormalizeMode(mode);
        EffectiveMode = RequestedMode == SystemMode
            ? (IsSystemLightTheme() ? LightMode : DarkMode)
            : RequestedMode;

        SwapThemeDictionary(EffectiveMode == LightMode ? LightThemeUri : DarkThemeUri);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string NormalizeMode(string? mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            LightMode => LightMode,
            DarkMode => DarkMode,
            _ => SystemMode
        };
    }

    public static string DisplayName(string? mode)
    {
        return NormalizeMode(mode) switch
        {
            LightMode => "Light",
            DarkMode => "Dark",
            _ => "System"
        };
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (RequestedMode != SystemMode) return;
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
            Application.Current.Dispatcher.Invoke(() => Apply(SystemMode));
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value ? value != 0 : true;
        }
        catch
        {
            return false;
        }
    }

    private static void SwapThemeDictionary(Uri themeUri)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString ?? "";
            if (source.Contains("/Resources/Themes/Catppuccin", StringComparison.OrdinalIgnoreCase) ||
                source.Contains("\\Resources\\Themes\\Catppuccin", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.Equals(dictionaries[i].Source, themeUri)) return;
                dictionaries[i] = new ResourceDictionary { Source = themeUri };
                return;
            }
        }

        dictionaries.Insert(0, new ResourceDictionary { Source = themeUri });
    }
}
