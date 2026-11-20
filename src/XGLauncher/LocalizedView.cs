// SPDX-License-Identifier: MIT
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;

namespace XGLauncher;

/// <summary>本地化只发生在显示边界，不改写持久化状态值或领域标识。</summary>
internal static class LocalizedView
{
    private sealed class Entry(string raw, Action<AvaloniaObject, string> write) { internal string Raw = raw; internal Action<AvaloniaObject, string> Write = write; internal bool Updating; }
    private static readonly ConditionalWeakTable<AvaloniaObject, Dictionary<AvaloniaProperty, Entry>> data = new();
    private static readonly ConditionalWeakTable<AvaloniaObject, object> preserved = new();
    private static readonly List<(WeakReference<AvaloniaObject> Target, AvaloniaProperty Property)> tracked = [];
    private static readonly List<IDisposable> subscriptions = [];
    private static bool initialized;
    internal static void Preserve(AvaloniaObject target) => preserved.GetValue(target, _ => new());
    internal static void Initialize() {
        if (initialized) return; initialized = true;
        subscriptions.Add(TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((c, e) => Track(c, e.Property, e.NewValue as string, static (o, t) => ((TextBlock)o).SetCurrentValue(TextBlock.TextProperty, t))));
        subscriptions.Add(ContentControl.ContentProperty.Changed.AddClassHandler<ContentControl>((c, e) => { if (e.NewValue is string s) Track(c, e.Property, s, static (o, t) => ((ContentControl)o).SetCurrentValue(ContentControl.ContentProperty, t)); else Forget(c, e.Property); }));
        subscriptions.Add(MenuItem.HeaderProperty.Changed.AddClassHandler<MenuItem>((c, e) => { if (e.NewValue is string s) Track(c, e.Property, s, static (o, t) => ((MenuItem)o).SetCurrentValue(MenuItem.HeaderProperty, t)); else Forget(c, e.Property); }));
        subscriptions.Add(TextBox.WatermarkProperty.Changed.AddClassHandler<TextBox>((c, e) => Track(c, e.Property, e.NewValue as string, static (o, t) => ((TextBox)o).SetCurrentValue(TextBox.WatermarkProperty, t))));
        subscriptions.Add(Window.TitleProperty.Changed.AddClassHandler<Window>((c, e) => Track(c, e.Property, e.NewValue as string, static (o, t) => ((Window)o).SetCurrentValue(Window.TitleProperty, t))));
        subscriptions.Add(ToolTip.TipProperty.Changed.AddClassHandler<Control>((c, e) => { if (e.NewValue is string s) Track(c, e.Property, s, static (o, t) => ToolTip.SetTip((Control)o, t)); else Forget(c, e.Property); }));
        subscriptions.Add(AutomationProperties.NameProperty.Changed.AddClassHandler<Control>((c, e) => Track(c, e.Property, e.NewValue as string, static (o, t) => AutomationProperties.SetName((Control)o, t))));
        subscriptions.Add(NativeMenuItem.HeaderProperty.Changed.AddClassHandler<NativeMenuItem>((c, e) => Track(c, e.Property, e.NewValue as string, static (o, t) => ((NativeMenuItem)o).SetCurrentValue(NativeMenuItem.HeaderProperty, t))));
        subscriptions.Add(TrayIcon.ToolTipTextProperty.Changed.AddClassHandler<TrayIcon>((c, e) => Track(c, e.Property, e.NewValue as string, static (o, t) => ((TrayIcon)o).SetCurrentValue(TrayIcon.ToolTipTextProperty, t))));
        Localizer.Changed += () => {
            for (var n = tracked.Count - 1; n >= 0; n--) {
                if (!tracked[n].Target.TryGetTarget(out var target)) { tracked.RemoveAt(n); continue; }
                if (data.TryGetValue(target, out var entries) && entries.TryGetValue(tracked[n].Property, out var entry)) Apply(target, entry);
            }
        };
    }
    private static void Forget(AvaloniaObject target, AvaloniaProperty property) {
        if (data.TryGetValue(target, out var entries)) entries.Remove(property);
    }
    private static void Track(AvaloniaObject target, AvaloniaProperty property, string? text, Action<AvaloniaObject, string> write) {
        if (preserved.TryGetValue(target, out _)) return;
        var entries = data.GetOrCreateValue(target);
        if (!entries.TryGetValue(property, out var entry)) {
            entry = new(text ?? "", write); entries.Add(property, entry); tracked.Add((new(target), property));
            if (tracked.Count > 10000) tracked.RemoveAll(x => !x.Target.TryGetTarget(out _));
        }
        if (entry.Updating) return;
        entry.Raw = text ?? ""; Apply(target, entry);
    }
    private static void Apply(AvaloniaObject target, Entry entry) {
        if (preserved.TryGetValue(target, out _)) return;
        entry.Updating = true;
        try { entry.Write(target, Localizer.Text(entry.Raw)); }
        finally { entry.Updating = false; }
    }
}
