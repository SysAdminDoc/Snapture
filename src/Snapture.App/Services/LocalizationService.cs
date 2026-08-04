using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Snapture.App.Services;

/// <summary>
/// Resolves user-facing copy from the embedded resource catalog. The base catalog is
/// en-US; satellite <c>Strings.&lt;culture&gt;.resx</c> files can be added without
/// changing view code. WPF windows are localized at load time so dynamically-created
/// tray and dialog surfaces follow the same contract as XAML.
/// </summary>
public static class LocalizationService
{
    public const string SystemCulture = "system";

    private static readonly ResourceManager ResourceManager = new(
        "Snapture.App.Resources.Strings",
        typeof(LocalizationService).Assembly);

    private static readonly CultureInfo ProcessCulture = CultureInfo.CurrentUICulture;
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, string>> OriginalValues = new();
    private static bool _windowHookRegistered;

    public static IReadOnlyList<string> PhaseOneCultures { get; } = new[]
    {
        "en-US", "de", "fr", "es", "it", "pt-BR", "nl", "pl", "cs", "ru", "tr",
        "ja", "zh-Hans", "zh-Hant", "ko", "ar"
    };

    public static CultureInfo CurrentCulture { get; private set; } = ProcessCulture;

    public static void Initialize(string? language = null)
    {
        CultureInfo culture = ResolveCulture(language);
        CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        RegisterWindowHook();
    }

    public static string Get(string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        return ResourceManager.GetString(ResourceKey(source), CurrentCulture) ?? source;
    }

    public static string ResourceKey(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"s_{Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant()}";
    }

    public static bool HasResource(string source) =>
        !string.IsNullOrEmpty(source) && ResourceManager.GetString(ResourceKey(source), CultureInfo.InvariantCulture) is not null;

    public static void RegisterWindowHook()
    {
        if (_windowHookRegistered)
            return;

        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));
        _windowHookRegistered = true;
    }

    public static void Localize(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        TranslateProperty(window, Window.TitleProperty);

        var visited = new HashSet<DependencyObject>();
        Visit(window, visited);
    }

    private static CultureInfo ResolveCulture(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Equals(SystemCulture, StringComparison.OrdinalIgnoreCase))
            return ProcessCulture;

        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return ProcessCulture;
        }
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
            Localize(window);
    }

    private static void Visit(DependencyObject node, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node))
            return;

        if (node is TextBlock)
            TranslateProperty(node, TextBlock.TextProperty);
        if (node is Run)
            TranslateProperty(node, Run.TextProperty);
        if (node is ContentControl)
            TranslateProperty(node, ContentControl.ContentProperty);
        if (node is HeaderedContentControl)
            TranslateProperty(node, HeaderedContentControl.HeaderProperty);
        if (node is HeaderedItemsControl)
            TranslateProperty(node, HeaderedItemsControl.HeaderProperty);

        if (node is UIElement element)
        {
            TranslateProperty(node, ToolTipService.ToolTipProperty);
            TranslateProperty(node, AutomationProperties.NameProperty);

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int index = 0; index < childCount; index++)
                Visit(VisualTreeHelper.GetChild(element, index), visited);
        }

        foreach (object child in LogicalTreeHelper.GetChildren(node))
        {
            if (child is DependencyObject dependencyObject)
                Visit(dependencyObject, visited);
        }
    }

    private static void TranslateProperty(DependencyObject target, DependencyProperty property)
    {
        object localValue = target.ReadLocalValue(property);
        if (localValue is not string current || string.IsNullOrWhiteSpace(current))
            return;

        if (!OriginalValues.TryGetValue(target, out var values))
        {
            values = new Dictionary<DependencyProperty, string>();
            OriginalValues.Add(target, values);
        }

        if (!values.TryGetValue(property, out string? source))
        {
            source = current;
            values[property] = source;
        }

        string translated = Get(source);
        if (!string.Equals(current, translated, StringComparison.Ordinal))
            target.SetValue(property, translated);
    }
}
