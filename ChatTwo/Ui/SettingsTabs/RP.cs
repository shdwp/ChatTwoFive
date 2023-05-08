using System.Numerics;
using ChatTwo.Code;
using ChatTwo.Util;
using ImGuiNET;

namespace ChatTwo.Ui.SettingsTabs;

internal sealed class RP : ISettingsTab {
    private Configuration Mutable { get; }

    public string Name => "Role-Playing Features" + "###tabs-rp";

    public RP(Configuration mutable) {
        this.Mutable = mutable;
    }

    public void Draw(bool changed) {
        ImGuiUtil.OptionCheckbox(ref this.Mutable.RPFormattingEnabled, $"Enable Role-Playing formatting");

        if (!this.Mutable.RPFormattingEnabled) {
            return;
        }
        
        ImGui.TreePush();
        ImGuiUtil.HelpText($"Which chat channels the formatting would be enabled on.");
        DrawChatTypeEnabledCheckbox(ChatType.Say);
        DrawChatTypeEnabledCheckbox(ChatType.Party);
        DrawChatTypeEnabledCheckbox(ChatType.TellIncoming);
        DrawChatTypeEnabledCheckbox(ChatType.TellOutgoing);
        ImGui.TreePop();
        
        ImGui.Dummy(new Vector2(0, 25));

        DrawRPBlockSettings(
            "Phrase",
            "Content of in-character phrase, for example \"How very glib!\".",
            ref this.Mutable.RPPhraseSettings
        );

        DrawRPBlockSettings(
            "Emote",
            "Content of in-character emote, for example \"*such devastation was not his intention*\".",
            ref this.Mutable.RPEmoteSettings
        );

        DrawRPBlockSettings("OOC",
            "Content of out-of-character phrase, for example \"((BRB 2 min))\".",
            ref this.Mutable.RPOOCSettings
        );
    }

    private void DrawChatTypeEnabledCheckbox(ChatType type) {
        var enabledRef = this.Mutable.RPFormattingEnabledTypes.Contains(type);
        if (ImGuiUtil.OptionCheckbox(ref enabledRef, $"In {type}")) {
            this.Mutable.RPFormattingEnabledTypes.Remove(type);
            
            if (enabledRef) {
                this.Mutable.RPFormattingEnabledTypes.Add(type);
            }
        }
    }

    private void DrawRPBlockSettings(string title, string hint, ref RPBlockSettings settings) {
        ImGui.Spacing();
        ImGui.TextDisabled(title);
        ImGui.TreePush();
        ImGuiUtil.HelpText($"{hint}");

        ImGuiUtil.OptionCheckbox(ref settings.Wrap, $"Wrap##wrap_{title}");
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref settings.PreserveStyle, $"Preserve channel style##preserve_{title}");
        ImGui.Spacing();

        if (!settings.PreserveStyle) {
            ImGuiUtil.OptionCheckbox(ref settings.Italic, $"Italic##italic_{title}");
            ImGui.Spacing();

            ImGui.ColorEdit3($"Color##color_{title}", ref settings.Color, ImGuiColorEditFlags.NoInputs);
            ImGui.Spacing();
        }

        ImGui.TreePop();
    }
}