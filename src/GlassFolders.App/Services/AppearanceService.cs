using System.Windows;
using System.Windows.Threading;
using GlassFolders.Core.Models;
using Microsoft.Win32;

namespace GlassFolders.App.Services;

/// <summary>
/// Holds the Appearance settings and applies them. It swaps the dark or light colour set that Settings, menus
/// and dialogs use, and follows the Windows light/dark setting while the app runs when Theme is System.
/// Windows that draw glass listen to <see cref="Changed"/> and update themselves at once.
/// </summary>
public sealed class AppearanceService
{
    private static readonly Lazy<AppearanceService> _instance = new(() => new AppearanceService());
    public static AppearanceService Instance => _instance.Value;

    private static readonly Uri DarkUri = new("pack://application:,,,/GlassFolders;component/Themes/ThemeDark.xaml", UriKind.Absolute);
    private static readonly Uri LightUri = new("pack://application:,,,/GlassFolders;component/Themes/ThemeLight.xaml", UriKind.Absolute);

    private AppTheme _mode = AppTheme.System;
    private bool _listening;
    private bool _themeApplied;
    private bool _glassAllowed = true;

    public AppTheme Mode => _mode;

    /// <summary>True while Settings, menus and dialogs show their light colours.</summary>
    public bool IsLight { get; private set; }

    public GlassAppearance Glass { get; private set; } = GlassAppearance.Default;

    /// <summary>Raised after the theme or any glass setting changed. Runs on the UI thread.</summary>
    public event Action? Changed;

    public void Apply(AppSettings settings)
    {
        _mode = settings.Theme;
        Glass = GlassAppearance.From(settings);
        _glassAllowed = BackdropSnapshot.IsGlassAllowed();
        Refresh();

        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
    }

    /// <summary>Stops listening for Windows setting changes. Called when the app exits.</summary>
    public void Stop()
    {
        if (!_listening) return;
        _listening = false;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void Refresh()
    {
        bool light = ResolveLight(_mode);
        if (light != IsLight || !_themeApplied) SwapColours(light);
        IsLight = light;
        Changed?.Invoke();
    }

    /// <summary>Windows' "Choose your mode" for apps. Dark when it can't be read, the app's original look.</summary>
    public static bool ResolveLight(AppTheme mode) => mode switch
    {
        AppTheme.Light => true,
        AppTheme.Dark => false,
        _ => WindowsAppsUseLightTheme(),
    };

    private static bool WindowsAppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex)
        {
            DiagnosticService.Instance.LogError("Could not read the Windows colour mode", ex);
            return false;
        }
    }

    private void SwapColours(bool light)
    {
        var app = Application.Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = light ? LightUri : DarkUri };
        int index = -1;
        for (int i = 0; i < merged.Count; i++)
        {
            string? source = merged[i].Source?.OriginalString;
            if (source is not null && (source.EndsWith("ThemeDark.xaml", StringComparison.OrdinalIgnoreCase)
                                       || source.EndsWith("ThemeLight.xaml", StringComparison.OrdinalIgnoreCase)))
            {
                index = i;
                break;
            }
        }

        if (index >= 0) merged[index] = replacement;
        else merged.Insert(0, replacement);
        _themeApplied = true;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color
            or UserPreferenceCategory.Accessibility or UserPreferenceCategory.VisualStyle)) return;
        var dispatcher = Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_mode == AppTheme.System && ResolveLight(_mode) != IsLight)
            {
                DiagnosticService.Instance.LogInfo("Windows colour mode changed; updating Settings, menus and dialogs");
                Refresh();
                _glassAllowed = BackdropSnapshot.IsGlassAllowed();
                return;
            }

            // Transparency effects or high contrast changed: open windows drop to the solid panel.
            bool allowed = BackdropSnapshot.IsGlassAllowed();
            if (allowed != _glassAllowed)
            {
                _glassAllowed = allowed;
                DiagnosticService.Instance.LogInfo(allowed
                    ? "Glass is allowed again; windows opened from now on use it"
                    : "Transparency effects are off or high contrast is on; windows use the solid panel");
                Changed?.Invoke();
            }
        });
    }
}
