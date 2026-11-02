using FluxPlayer.Models;
using FluxPlayer.Services;
using FluxPlayer.ViewModels;
using System.Windows;

namespace FluxPlayer.Windows;

public sealed class GpuPolicyWindow : FeatureWorkbench
{
    public GpuPolicyWindow(Window owner, MainViewModel vm, Action<PlayerEnhancements> commit) : base(owner, vm, "GPU decoding policy", "Choose a decoding backend per primary display adapter vendor. Restart FluxPlayer to apply. LibVLC chooses its actual decoding device; this policy does not move decoding to a chosen physical GPU. The WPF memory-video presentation surface stays active.")
    {
        var current = vm.CurrentSettings.Enhancements.GpuPolicy; var enabled = Toggle("Apply vendor-specific decoder rules at startup", current.Enabled);
        var fields = new[] { current.Nvidia, current.Amd, current.Intel, current.Other }.Select((v, i) => Choice(new[] { "NVIDIA", "AMD", "Intel", "Other adapters" }[i], GpuRenderPolicy.Backends, Array.IndexOf(GpuRenderPolicy.Backends, v))).ToArray();
        var table = Table(("Adapter", "Name", 3), ("Vendor", "Vendor", 1), ("Primary", "Primary", 1)); table.ItemsSource = GpuRenderPolicy.Detect();
        Action("Refresh adapters", () => { table.ItemsSource = GpuRenderPolicy.Detect(); return Task.CompletedTask; });
        Action("Save policy", () =>
        {
            string V(int i) => fields[i].SelectedItem?.ToString() ?? "any";
            var policy = new GpuRenderPolicy(enabled.IsChecked == true, V(0), V(1), V(2), V(3)); policy.Validate();
            commit(vm.CurrentSettings.Enhancements with { GpuPolicy = policy }); Status.Text = "Saved. Restart FluxPlayer to apply. Microsoft software/basic display adapters use software decoding; disabling hardware decoding overrides vendor rules."; return Task.CompletedTask;
        });
    }
}
