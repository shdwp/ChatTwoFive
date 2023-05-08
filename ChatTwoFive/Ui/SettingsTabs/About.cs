using System.Diagnostics;
using System.Numerics;
using ChatTwoFive.Resources;
using ChatTwoFive.Util;
using Dalamud.Interface;
using ImGuiNET;

namespace ChatTwoFive.Ui.SettingsTabs;

internal sealed class About : ISettingsTab {
    public string Name => string.Format(Language.Options_About_Tab, Plugin.PluginName) + "###tabs-about";

    private readonly List<string> _translators = new() {
        "q673135110",
        "Akizem",
        "d0tiKs",
        "Moonlight_Everlit",
        "Dark32",
        "andreycout",
        "Button_",
        "Cali666",
        "cassandra308",
        "lokinmodar",
        "jtabox",
        "AkiraYorumoto",
        "MKhayle",
        "elena.space",
        "imlisa",
        "andrei5125",
        "ShivaMaheshvara",
        "aislinn87",
        "nishinatsu051",
        "lichuyuan",
        "Risu64",
        "yummypillow",
        "witchymary",
        "Yuzumi",
        "zomsakura",
        "Sirayuki",
    };

    internal About() {
        this._translators.Sort((a, b) => string.Compare(a.ToLowerInvariant(), b.ToLowerInvariant(), StringComparison.Ordinal));
    }

    public void Draw(bool changed) {
        ImGui.PushTextWrapPos();

        ImGui.TextUnformatted(string.Format(Language.Options_About_Opening, Plugin.PluginName));

        ImGui.Spacing();

        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExternalLinkAlt, "clickup")) {
            Process.Start(new ProcessStartInfo("https://sharing.clickup.com/b/h/6-122378074-2/1047d21a39a4140") {
                UseShellExecute = true,
            });
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(Language.Options_About_ClickUp);

        if (ImGuiUtil.IconButton(FontAwesomeIcon.ExternalLinkAlt, "crowdin")) {
            Process.Start(new ProcessStartInfo("https://crowdin.com/project/chat-2") {
                UseShellExecute = true,
            });
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(string.Format(Language.Options_About_CrowdIn, Plugin.PluginName));

        ImGui.Spacing();

        var height = ImGui.GetContentRegionAvail().Y
                     - ImGui.CalcTextSize("A").Y
                     - ImGui.GetStyle().ItemSpacing.Y * 2;
        if (ImGui.BeginChild("about", new Vector2(-1, height))) {
            if (ImGui.TreeNodeEx(Language.Options_About_Translators)) {
                if (ImGui.BeginChild("translators")) {
                    foreach (var translator in this._translators) {
                        ImGui.TextUnformatted(translator);
                    }

                    ImGui.EndChild();
                }

                ImGui.TreePop();
            }

            ImGui.EndChild();
        }

        ImGuiUtil.HelpText($"{Plugin.PluginName} v{this.GetType().Assembly.GetName().Version?.ToString(3)}");
        ImGui.PopTextWrapPos();
    }
}
