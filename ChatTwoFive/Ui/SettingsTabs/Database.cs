using ChatTwoFive.Resources;
using ChatTwoFive.Util;
using ImGuiNET;

namespace ChatTwoFive.Ui.SettingsTabs;

internal sealed class Database : ISettingsTab {
    private Configuration Mutable { get; }
    private Store Store { get; }

    public string Name => Language.Options_Database_Tab + "###tabs-database";

    internal Database(Configuration mutable, Store store) {
        this.Store = store;
        this.Mutable = mutable;
    }

    private bool _showAdvanced;

    public void Draw(bool changed) {
        if (changed) {
            this._showAdvanced = ImGui.GetIO().KeyShift;
        }

        ImGuiUtil.OptionCheckbox(ref this.Mutable.DatabaseBattleMessages, Language.Options_DatabaseBattleMessages_Name, Language.Options_DatabaseBattleMessages_Description);
        ImGui.Spacing();

        if (ImGuiUtil.OptionCheckbox(ref this.Mutable.LoadPreviousSession, Language.Options_LoadPreviousSession_Name, Language.Options_LoadPreviousSession_Description)) {
            if (this.Mutable.LoadPreviousSession) {
                this.Mutable.FilterIncludePreviousSessions = true;
            }
        }

        ImGui.Spacing();

        if (ImGuiUtil.OptionCheckbox(ref this.Mutable.FilterIncludePreviousSessions, Language.Options_FilterIncludePreviousSessions_Name, Language.Options_FilterIncludePreviousSessions_Description)) {
            if (!this.Mutable.FilterIncludePreviousSessions) {
                this.Mutable.LoadPreviousSession = false;
            }
        }

        ImGuiUtil.OptionCheckbox(
            ref this.Mutable.SharedMode,
            Language.Options_SharedMode_Name,
            string.Format(Language.Options_SharedMode_Description, Plugin.PluginName)
        );
        ImGuiUtil.WarningText(string.Format(Language.Options_SharedMode_Warning, Plugin.PluginName));

        ImGui.Spacing();

        if (this._showAdvanced && ImGui.TreeNodeEx(Language.Options_Database_Advanced)) {
            ImGui.PushTextWrapPos();
            ImGuiUtil.WarningText(Language.Options_Database_Advanced_Warning);

            if (ImGui.Button("Checkpoint")) {
                this.Store.Database.Checkpoint();
            }

            if (ImGui.Button("Rebuild")) {
                this.Store.Database.Rebuild();
            }

            ImGui.PopTextWrapPos();
            ImGui.TreePop();
        }
    }
}
