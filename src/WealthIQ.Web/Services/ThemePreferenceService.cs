using Microsoft.JSInterop;

namespace WealthIQ.Web.Services;

/// <summary>
/// Persists the user's dark/light choice in browser localStorage via JS interop.
/// JS interop is only available after the first interactive render, so callers must
/// invoke <see cref="LoadIsDarkAsync"/> from OnAfterRenderAsync(firstRender), not during init.
/// Default is dark (the primary Midnight Ledger mode).
/// </summary>
public sealed class ThemePreferenceService
{
    private const string Dark = "dark";
    private const string Light = "light";
    private readonly IJSRuntime _js;

    public ThemePreferenceService(IJSRuntime js) => _js = js;

    /// <summary>Reads the stored preference; returns true (dark) when absent or unrecognized.</summary>
    public async Task<bool> LoadIsDarkAsync()
    {
        var stored = await _js.InvokeAsync<string?>("wealthiq.getTheme");
        return !string.Equals(stored, Light, StringComparison.OrdinalIgnoreCase);
    }

    public async Task SaveAsync(bool isDark)
        => await _js.InvokeVoidAsync("wealthiq.setTheme", isDark ? Dark : Light);
}
