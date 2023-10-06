using ChatTwo.Resources;
using ChatTwo.Util;
using ImGuiNET;

namespace ChatTwo.Ui.SettingsTabs;

internal sealed class Display : ISettingsTab {
    private Configuration Mutable { get; }

    public string Name => Language.Options_Display_Tab + "###tabs-display";

    internal Display(Configuration mutable) {
        this.Mutable = mutable;
    }

    public void Draw(bool changed) {
        ImGui.PushTextWrapPos();

        ImGuiUtil.OptionCheckbox(ref this.Mutable.HideChat, Language.Options_HideChat_Name, Language.Options_HideChat_Description);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(
            ref this.Mutable.HideDuringCutscenes,
            Language.Options_HideDuringCutscenes_Name,
            string.Format(Language.Options_HideDuringCutscenes_Description, Plugin.PluginName)
        );
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(
            ref this.Mutable.HideWhenNotLoggedIn,
            Language.Options_HideWhenNotLoggedIn_Name,
            string.Format(Language.Options_HideWhenNotLoggedIn_Description, Plugin.PluginName)
        );
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(
            ref this.Mutable.HideWhenUiHidden,
            Language.Options_HideWhenUiHidden_Name,
            string.Format(Language.Options_HideWhenUiHidden_Description, Plugin.PluginName)
        );
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(
            ref this.Mutable.NativeItemTooltips,
            Language.Options_NativeItemTooltips_Name,
            string.Format(Language.Options_NativeItemTooltips_Description, Plugin.PluginName)
        );
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(
            ref this.Mutable.SidebarTabView,
            Language.Options_SidebarTabView_Name,
            string.Format(Language.Options_SidebarTabView_Description, Plugin.PluginName)
        );
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref this.Mutable.PrettierTimestamps, Language.Options_PrettierTimestamps_Name, Language.Options_PrettierTimestamps_Description);

        if (this.Mutable.PrettierTimestamps) {
            ImGui.TreePush();
            ImGuiUtil.OptionCheckbox(ref this.Mutable.MoreCompactPretty, Language.Options_MoreCompactPretty_Name, Language.Options_MoreCompactPretty_Description);
            ImGuiUtil.OptionCheckbox(ref this.Mutable.HideSameTimestamps, Language.Options_HideSameTimestamps_Name, Language.Options_HideSameTimestamps_Description);
            ImGui.TreePop();
        }
        
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref this.Mutable.Force24H, "Force 24H");
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref this.Mutable.HideCogButton, "Hide Cog button");
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref this.Mutable.HideChannelsButton, "Hide channels button");
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref this.Mutable.TellTabs, "Open conversation tabs on /tell");
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref this.Mutable.CollapseDuplicateMessages, Language.Options_CollapseDuplicateMessages_Name, Language.Options_CollapseDuplicateMessages_Description);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref this.Mutable.ShowNoviceNetwork, Language.Options_ShowNoviceNetwork_Name, Language.Options_ShowNoviceNetwork_Description);
        ImGui.Spacing();

        ImGuiUtil.DragFloatVertical(Language.Options_WindowOpacity_Name, ref this.Mutable.WindowAlpha, .25f, 0f, 100f, $"{this.Mutable.WindowAlpha:N2}%%", ImGuiSliderFlags.AlwaysClamp);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref this.Mutable.CanMove, Language.Options_CanMove_Name);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref this.Mutable.CanResize, Language.Options_CanResize_Name);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref this.Mutable.ShowTitleBar, Language.Options_ShowTitleBar_Name);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref this.Mutable.ShowPopOutTitleBar, Language.Options_ShowPopOutTitleBar_Name);
        ImGui.Spacing();

        ImGui.PopTextWrapPos();
    }
}