using System.Diagnostics;
using System.Globalization;
using ChatTwoFive.Code;
using ChatTwoFive.Ipc;
using ChatTwoFive.Resources;
using ChatTwoFive.RTyping;
using ChatTwoFive.Util;
using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using Dalamud.Plugin;
using XivCommon;

namespace ChatTwoFive;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class Plugin : IDalamudPlugin {
    internal const string PluginName = "Chat 2";

    public string Name => PluginName;

    [PluginService]
    internal DalamudPluginInterface Interface { get; init; }

    [PluginService]
    internal ChatGui ChatGui { get; init; }

    [PluginService]
    internal ClientState ClientState { get; init; }

    [PluginService]
    internal CommandManager CommandManager { get; init; }

    [PluginService]
    internal Condition Condition { get; init; }

    [PluginService]
    internal DataManager DataManager { get; init; }

    [PluginService]
    internal Framework Framework { get; init; }

    [PluginService]
    internal GameGui GameGui { get; init; }

    [PluginService]
    internal KeyState KeyState { get; init; }

    [PluginService]
    internal ObjectTable ObjectTable { get; init; }

    [PluginService]
    internal PartyList PartyList { get; init; }

    [PluginService]
    internal TargetManager TargetManager { get; init; }

    internal Configuration Config { get; }
    internal Commands Commands { get; }
    internal XivCommonBase Common { get; }
    internal TextureCache TextureCache { get; }
    internal GameFunctions.GameFunctions Functions { get; }
    internal Store Store { get; }
    internal IpcManager Ipc { get; }
    internal RPMarkup RpMarkup { get; }
    internal ExtraChat ExtraChat { get; }
    internal PluginUi Ui { get; }
    internal RTypingAdapter RTyping { get; }

    internal int DeferredSaveFrames = -1;

    internal DateTime GameStarted { get; }

    #pragma warning disable CS8618
    public Plugin() {
        this.GameStarted = Process.GetCurrentProcess().StartTime.ToUniversalTime();

        this.Config = this.Interface!.GetPluginConfig() as Configuration ?? new Configuration();
        this.Config.Migrate();

        if (this.Config.Tabs.Count == 0) {
            this.Config.Tabs.Add(TabsUtil.VanillaGeneral);
        }

        if (this.Config.RPFormattingEnabledTypes.Count == 0) {
            this.Config.RPFormattingEnabledTypes.Add(ChatType.Say);
            this.Config.RPFormattingEnabledTypes.Add(ChatType.Party);
            this.Config.RPFormattingEnabledTypes.Add(ChatType.TellIncoming);
            this.Config.RPFormattingEnabledTypes.Add(ChatType.TellOutgoing);
        }

        if (this.Config.RPPhraseSettings == null) {
            var settings = new RPBlockSettings();
            settings.PreserveStyle = true;
            this.Config.RPPhraseSettings = settings;
        }

        if (this.Config.RPEmoteSettings == null) {
            var settings = new RPBlockSettings();
            settings.Wrap = true;
            settings.Italic = true;
            settings.Color = ColourUtil.RgbaToVector3(0xBAFFF0FF);
            this.Config.RPEmoteSettings = settings;
        }

        if (this.Config.RPOOCSettings == null) {
            var settings = new RPBlockSettings();
            settings.Wrap = true;
            settings.Color = ColourUtil.RgbaToVector3(0xD94D1DFF);
            this.Config.RPOOCSettings = settings;
        }

        this.LanguageChanged(this.Interface.UiLanguage);

        this.Commands = new Commands(this);
        this.Common = new XivCommonBase();
        this.TextureCache = new TextureCache(this.DataManager!);
        this.Functions = new GameFunctions.GameFunctions(this);
        this.Store = new Store(this);
        this.Ipc = new IpcManager(this.Interface);
        this.RpMarkup = new RPMarkup(this);
        this.ExtraChat = new ExtraChat(this);
        this.Ui = new PluginUi(this);
        this.RTyping = new RTypingAdapter(this);

        // let all the other components register, then initialise commands
        this.Commands.Initialise();

        if (this.Interface.Reason is not PluginLoadReason.Boot) {
            this.Store.FilterAllTabs(false);
        }

        this.Framework!.Update += this.FrameworkUpdate;
        this.Interface.LanguageChanged += this.LanguageChanged;
    }
    #pragma warning restore CS8618

    public void Dispose() {
        this.Interface.LanguageChanged -= this.LanguageChanged;
        this.Framework.Update -= this.FrameworkUpdate;
        GameFunctions.GameFunctions.SetChatInteractable(true);

        this.Ui.Dispose();
        this.ExtraChat.Dispose();
        this.Ipc.Dispose();
        this.Store.Dispose();
        this.Functions.Dispose();
        this.TextureCache.Dispose();
        this.Common.Dispose();
        this.Commands.Dispose();
    }

    internal void SaveConfig() {
        this.Interface.SavePluginConfig(this.Config);
    }

    internal void LanguageChanged(string langCode) {
        var info = this.Config.LanguageOverride is LanguageOverride.None
            ? new CultureInfo(langCode)
            : new CultureInfo(this.Config.LanguageOverride.Code());

        Language.Culture = info;
    }

    private static readonly string[] ChatAddonNames = {
        "ChatLog",
        "ChatLogPanel_0",
        "ChatLogPanel_1",
        "ChatLogPanel_2",
        "ChatLogPanel_3",
    };

    private void FrameworkUpdate(Framework framework) {
        if (this.DeferredSaveFrames >= 0 && this.DeferredSaveFrames-- == 0) {
            this.SaveConfig();
        }

        if (!this.Config.HideChat) {
            return;
        }

        foreach (var name in ChatAddonNames) {
            if (GameFunctions.GameFunctions.IsAddonInteractable(name)) {
                GameFunctions.GameFunctions.SetAddonInteractable(name, false);
            }
        }
    }
}