using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidBar.SDK;
using Widgets.Core;
using Widgets.Core.Appearance;
using Widgets.Core.Settings;

namespace Widgets.WidBar;

/// <summary>
/// The device-battery widget: a row of tiles on the taskbar (one per device), a flyout with the detail on click, and a
/// settings page. The batteries come from <see cref="BatteryHub"/>, the same readers the overlay app used.
/// </summary>
public sealed class MainPlugin : WidgetPluginBase, IConfigurableWidgetPlugin, IWidgetFlyoutLifecycle
{
    private const double SidePadding = PreviewBuilder.SidePadding;
    private const double DefaultSlotHeight = 48; // the taskbar's height, or about 26 with small taskbar buttons

    private BatteryWidgetSettings _settings = new();
    private Grid? _root;
    private StackPanel? _row;
    private FlyoutView? _flyout;
    private DispatcherQueue? _ui;
    private double _slotHeight = DefaultSlotHeight;
    private int _previewWidth = 160;

    public override string Id => "com.codemonk.devicebattery";
    public override string Name => "Device batteries";
    public override string Version => "0.4.0";

    public override int PreviewLogicalWidth => _previewWidth;
    public override int FlyoutWidth => 560;
    public override int FlyoutHeight => 520;

    public override async Task InitializeAsync(IWidgetContext context)
    {
        _settings = BatteryWidgetSettings.FromJson(context.SettingsJson);
        await base.InitializeAsync(context);
        BatteryHub.Changed += OnBatteriesChanged;
        BatteryHub.SetRefresh(context.InstanceId, _settings.RefreshMinutes);
        BatteryHub.Start(context.SharedDataDirectory);
    }

    public override UIElement? CreatePreviewContent()
    {
        _row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _root = new Grid { Padding = new Thickness(SidePadding, 0, SidePadding, 0), Background = null, Children = { _row } };
        _ui = _root.DispatcherQueue;

        _root.ActualThemeChanged += (_, _) => RenderPreview();
        _root.SizeChanged += (_, e) =>
        {
            // The slot is as tall as the taskbar, which the user can switch between normal and small buttons.
            if (e.NewSize.Height > 0 && Math.Abs(e.NewSize.Height - _slotHeight) > 1)
            {
                _slotHeight = e.NewSize.Height;
                RenderPreview();
            }
        };

        RenderPreview();
        return _root;
    }

    public override UIElement? CreateFlyoutContent()
    {
        _flyout = new FlyoutView();
        _flyout.Refresh(Shown(), _settings);
        return _flyout.Root;
    }

    public UIElement? CreateSettingsContent(IWidgetSettingsContext context) => new SettingsView(context, BatteryHub.Snapshot(), _slotHeight).Root;

    // WidBar calls this while the settings page is edited (so the taskbar follows along), after Save, and with the original JSON after Cancel.
    public override void OnSettingsDraftChanged(string settingsJson)
    {
        _settings = BatteryWidgetSettings.FromJson(settingsJson);
        if (Context is { } context) BatteryHub.SetRefresh(context.InstanceId, _settings.RefreshMinutes);
        RenderPreview();
        _flyout?.Refresh(Shown(), _settings);
    }

    public void OnFlyoutShown()
    {
        BatteryHub.Refresh(); // a fresh reading when someone is looking
        _flyout?.Refresh(Shown(), _settings);
    }

    public void OnFlyoutHidden() { }

    public override ValueTask DisposeAsync()
    {
        BatteryHub.Changed -= OnBatteriesChanged;
        if (Context is { } context) BatteryHub.ClearRefresh(context.InstanceId);
        return ValueTask.CompletedTask;
    }

    private IReadOnlyList<Metric> Shown()
    {
        var now = DateTimeOffset.UtcNow;
        var present = BatteryHub.Snapshot().Where(m => DevicePresence.IsShown(m, now, _settings.HideAfter)).ToList();
        return TileLayout.Apply(present, _settings.AsLayout());
    }

    private void OnBatteriesChanged() => _ui?.TryEnqueue(() =>
    {
        RenderPreview();
        _flyout?.Refresh(Shown(), _settings);
    });

    /// <summary>Redraws the taskbar tiles, and asks WidBar for a new width when the tiles need more or less room. UI thread only.</summary>
    private void RenderPreview()
    {
        if (_row is null || _root is null) return;

        var width = PreviewBuilder.Fill(_row, _settings, Shown(), _slotHeight, _root.ActualTheme == ElementTheme.Dark);
        if (width != _previewWidth)
        {
            _previewWidth = width; // already includes the width adjustment and the minimum
            Context?.RequestPreviewRefresh(); // WidBar reads PreviewLogicalWidth again and makes room
        }
    }
}
