using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ChatTwoFive.Code;
using ChatTwoFive.GameFunctions.Types;
using ChatTwoFive.Resources;
using ChatTwoFive.Util;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Style;
using Dalamud.Logging;
using Dalamud.Memory;
using ImGuiNET;
using ImGuiScene;
using Lumina.Excel.GeneratedSheets;

namespace ChatTwoFive.Ui;

internal sealed class ChatLog : IUiComponent {
    private const string ChatChannelPicker = "chat-channel-picker";
    private const string AutoCompleteId = "##chat2-autocomplete";

    internal PluginUi Ui { get; }

    internal bool Activate;
    private int _activatePos = -1;
    internal string Chat = string.Empty;
    private readonly TextureWrap? _fontIcon;
    private readonly List<string> _inputBacklog = new();
    private int _inputBacklogIdx = -1;
    internal int LastTab { get; private set; }
    internal int? SwitchToTab { get; set; }
    private InputChannel? _tempChannel;
    private TellTarget? _tellTarget;
    private readonly Stopwatch _lastResize = new();
    private string? _currentCommandText;
    private CommandHelp? _commandHelp;
    private AutoCompleteInfo? _autoCompleteInfo;
    private bool _autoCompleteOpen;
    private List<AutoTranslateEntry>? _autoCompleteList;
    private bool _fixCursor;
    private int _autoCompleteSelection;
    private bool _autoCompleteShouldScroll;

    internal Vector2 LastWindowSize { get; private set; } = Vector2.Zero;
    internal Vector2 LastWindowPos { get; private set; } = Vector2.Zero;

    private PayloadHandler PayloadHandler { get; }
    private Lender<PayloadHandler> HandlerLender { get; }
    private Dictionary<string, ChatType> TextCommandChannels { get; } = new();
    private HashSet<string> AllCommands { get; } = new();

    internal ChatLog(PluginUi ui) {
        this.Ui = ui;
        this.PayloadHandler = new PayloadHandler(this.Ui, this);
        this.HandlerLender = new Lender<PayloadHandler>(() => new PayloadHandler(this.Ui, this));

        this.SetUpTextCommandChannels();
        this.SetUpAllCommands();

        this.Ui.Plugin.Commands.Register("/clearlog2", "Clear the Chat 2 chat log").Execute += this.ClearLog;
        this.Ui.Plugin.Commands.Register("/chat2").Execute += this.ToggleChat;

        this._fontIcon = this.Ui.Plugin.DataManager.GetImGuiTexture("common/font/fonticon_ps5.tex");

        this.Ui.Plugin.Functions.Chat.Activated += this.Activated;
        this.Ui.Plugin.ClientState.Login += this.Login;
        this.Ui.Plugin.ClientState.Logout += this.Logout;
    }

    public void Dispose() {
        this.Ui.Plugin.ClientState.Logout -= this.Logout;
        this.Ui.Plugin.ClientState.Login -= this.Login;
        this.Ui.Plugin.Functions.Chat.Activated -= this.Activated;
        this._fontIcon?.Dispose();
        this.Ui.Plugin.Commands.Register("/chat2").Execute -= this.ToggleChat;
        this.Ui.Plugin.Commands.Register("/clearlog2").Execute -= this.ClearLog;
    }

    private void Logout(object? sender, EventArgs e) {
        foreach (var tab in this.Ui.Plugin.Config.Tabs) {
            tab.Clear();
        }
    }

    private void Login(object? sender, EventArgs e) {
        this.Ui.Plugin.Store.FilterAllTabs(false);
    }

    private void Activated(ChatActivatedArgs args) {
        this.Activate = true;
        if (args.AddIfNotPresent != null && !this.Chat.Contains(args.AddIfNotPresent)) {
            this.Chat += args.AddIfNotPresent;
        }

        if (args.Input != null) {
            this.Chat += args.Input;
        }

        var (info, reason, target) = (args.ChannelSwitchInfo, args.TellReason, args.TellTarget);

        if (info.Channel != null) {
            var prevTemp = this._tempChannel;

            if (info.Permanent) {
                this.Ui.Plugin.Functions.Chat.SetChannel(info.Channel.Value);
            } else {
                this._tempChannel = info.Channel.Value;
            }

            if (info.Channel is InputChannel.Tell) {
                if (info.Rotate != RotateMode.None) {
                    var idx = prevTemp != InputChannel.Tell
                        ? 0
                        : info.Rotate == RotateMode.Reverse
                            ? -1
                            : 1;

                    var tellInfo = this.Ui.Plugin.Functions.Chat.GetTellHistoryInfo(idx);
                    if (tellInfo != null && reason != null) {
                        this._tellTarget = new TellTarget(tellInfo.Name, (ushort) tellInfo.World, tellInfo.ContentId, reason.Value);
                    }
                } else {
                    this._tellTarget = null;

                    if (target != null) {
                        this._tellTarget = target;
                    }
                }
            } else {
                this._tellTarget = null;
            }

            var mode = prevTemp == null
                ? RotateMode.None
                : info.Rotate;

            if (info.Channel is InputChannel.Linkshell1 && info.Rotate != RotateMode.None) {
                var idx = this.Ui.Plugin.Functions.Chat.RotateLinkshellHistory(mode);
                this._tempChannel = info.Channel.Value + (uint) idx;
            } else if (info.Channel is InputChannel.CrossLinkshell1 && info.Rotate != RotateMode.None) {
                var idx = this.Ui.Plugin.Functions.Chat.RotateCrossLinkshellHistory(mode);
                this._tempChannel = info.Channel.Value + (uint) idx;
            }
        }

        if (info.Text != null && this.Chat.Length == 0) {
            this.Chat = info.Text;
        }
    }

    private bool IsValidCommand(string command) {
        return this.Ui.Plugin.CommandManager.Commands.ContainsKey(command)
               || this.AllCommands.Contains(command);
    }

    private void ClearLog(string command, string arguments) {
        switch (arguments) {
            case "all":
                foreach (var tab in this.Ui.Plugin.Config.Tabs) {
                    tab.Clear();
                }

                break;
            case "help":
                this.Ui.Plugin.ChatGui.Print("- /clearlog2: clears the active tab's log");
                this.Ui.Plugin.ChatGui.Print("- /clearlog2 all: clears all tabs' logs and the global history");
                this.Ui.Plugin.ChatGui.Print("- /clearlog2 help: shows this help");

                break;
            default:
                if (this.LastTab > -1 && this.LastTab < this.Ui.Plugin.Config.Tabs.Count) {
                    this.Ui.Plugin.Config.Tabs[this.LastTab].Clear();
                }

                break;
        }
    }

    private void ToggleChat(string command, string arguments) {
        var parts = arguments.Split(' ');
        if (parts.Length < 2 || parts[0] != "chat") {
            return;
        }

        switch (parts[1]) {
            case "hide":
                this._hideState = HideState.User;
                break;
            case "show":
                this._hideState = HideState.None;
                break;
            case "toggle":
                this._hideState = this._hideState switch {
                    HideState.User or HideState.CutsceneOverride => HideState.None,
                    HideState.Cutscene => HideState.CutsceneOverride,
                    HideState.None => HideState.User,
                    _ => this._hideState,
                };

                break;
        }
    }

    private void SetUpTextCommandChannels() {
        this.TextCommandChannels.Clear();

        foreach (var input in Enum.GetValues<InputChannel>()) {
            var commands = input.TextCommands(this.Ui.Plugin.DataManager);
            if (commands == null) {
                continue;
            }

            var type = input.ToChatType();
            foreach (var command in commands) {
                this.AddTextCommandChannel(command, type);
            }
        }

        var echo = this.Ui.Plugin.DataManager.GetExcelSheet<TextCommand>()?.GetRow(116);
        if (echo != null) {
            this.AddTextCommandChannel(echo, ChatType.Echo);
        }
    }

    private void AddTextCommandChannel(TextCommand command, ChatType type) {
        this.TextCommandChannels[command.Command] = type;
        this.TextCommandChannels[command.ShortCommand] = type;
        this.TextCommandChannels[command.Alias] = type;
        this.TextCommandChannels[command.ShortAlias] = type;
    }

    private void SetUpAllCommands() {
        if (this.Ui.Plugin.DataManager.GetExcelSheet<TextCommand>() is not { } commands) {
            return;
        }

        var commandNames = commands.SelectMany(cmd => new[] {
            cmd.Command.RawString,
            cmd.ShortCommand.RawString,
            cmd.Alias.RawString,
            cmd.ShortAlias.RawString,
        });

        foreach (var command in commandNames) {
            this.AllCommands.Add(command);
        }
    }

    private void AddBacklog(string message) {
        for (var i = 0; i < this._inputBacklog.Count; i++) {
            if (this._inputBacklog[i] != message) {
                continue;
            }

            this._inputBacklog.RemoveAt(i);
            break;
        }

        this._inputBacklog.Add(message);
    }

    private static float GetRemainingHeightForMessageLog() {
        var lineHeight = ImGui.CalcTextSize("A").Y;
        var value = ImGui.GetContentRegionAvail().Y
                    // - lineHeight * 2
                    - lineHeight
                    - ImGui.GetStyle().ItemSpacing.Y
                    - ImGui.GetStyle().FramePadding.Y * 2;

        return value;
    }

    private unsafe ImGuiViewport* _lastViewport;
    private bool _wasDocked;

    private void HandleKeybinds(bool modifiersOnly = false) {
        var modifierState = (ModifierFlag) 0;
        if (ImGui.GetIO().KeyAlt) {
            modifierState |= ModifierFlag.Alt;
        }

        if (ImGui.GetIO().KeyCtrl) {
            modifierState |= ModifierFlag.Ctrl;
        }

        if (ImGui.GetIO().KeyShift) {
            modifierState |= ModifierFlag.Shift;
        }

        var turnedOff = new Dictionary<VirtualKey, (uint, string)>();
        foreach (var (toIntercept, keybind) in this.Ui.Plugin.Functions.Chat.Keybinds) {
            if (toIntercept is "CMD_CHAT" or "CMD_COMMAND") {
                continue;
            }

            void Intercept(VirtualKey vk, ModifierFlag modifier) {
                if (!vk.TryToImGui(out var key)) {
                    return;
                }

                var modifierPressed = this.Ui.Plugin.Config.KeybindMode switch {
                    KeybindMode.Strict => modifier == modifierState,
                    KeybindMode.Flexible => modifierState.HasFlag(modifier),
                    _ => false,
                };
                if (!ImGui.IsKeyPressed(key) || !modifierPressed || modifier == 0 && modifiersOnly) {
                    return;
                }

                var bits = BitOperations.PopCount((uint) modifier);
                if (!turnedOff.TryGetValue(vk, out var previousBits) || previousBits.Item1 < bits) {
                    turnedOff[vk] = ((uint) bits, toIntercept);
                }
            }

            Intercept(keybind.Key1, keybind.Modifier1);
            Intercept(keybind.Key2, keybind.Modifier2);
        }

        foreach (var (_, (_, keybind)) in turnedOff) {
            if (!GameFunctions.Chat.KeybindsToIntercept.TryGetValue(keybind, out var info)) {
                continue;
            }

            try {
                TellReason? reason = info.Channel == InputChannel.Tell ? TellReason.Reply : null;
                this.Activated(new ChatActivatedArgs(info) {
                    TellReason = reason,
                });
            } catch (Exception ex) {
                PluginLog.LogError(ex, "Error in chat Activated event");
            }
        }
    }

    private bool CutsceneActive {
        get {
            var condition = this.Ui.Plugin.Condition;
            return condition[ConditionFlag.OccupiedInCutSceneEvent]
                   || condition[ConditionFlag.WatchingCutscene78];
        }
    }

    private bool GposeActive {
        get {
            var condition = this.Ui.Plugin.Condition;
            return condition[ConditionFlag.WatchingCutscene];
        }
    }

    private enum HideState {
        None,
        Cutscene,
        CutsceneOverride,
        User,
    }

    private HideState _hideState = HideState.None;

    public void Draw() {
        if (!this.DrawChatLog()) {
            return;
        }

        this._commandHelp?.Draw();
        this.DrawPopOuts();
        this.DrawAutoComplete();
    }

    /// <returns>true if window was rendered</returns>
    private unsafe bool DrawChatLog() {
        // if the chat has no hide state and in a cutscene, set the hide state to cutscene
        if (this.Ui.Plugin.Config.HideDuringCutscenes && this._hideState == HideState.None && (this.CutsceneActive || this.GposeActive)) {
            this._hideState = HideState.Cutscene;
        }

        // if the chat is hidden because of a cutscene and no longer in a cutscene, set the hide state to none
        if (this._hideState is HideState.Cutscene or HideState.CutsceneOverride && !this.CutsceneActive && !this.GposeActive) {
            this._hideState = HideState.None;
        }

        // if the chat is hidden because of a cutscene and the chat has been activated, show chat
        if (this._hideState == HideState.Cutscene && this.Activate) {
            this._hideState = HideState.CutsceneOverride;
        }

        // if the user hid the chat and is now activating chat, reset the hide state
        if (this._hideState == HideState.User && this.Activate) {
            this._hideState = HideState.None;
        }

        if (this._hideState is HideState.Cutscene or HideState.User) {
            return false;
        }

        if (this.Ui.Plugin.Config.HideWhenNotLoggedIn && !this.Ui.Plugin.ClientState.IsLoggedIn) {
            return false;
        }

        var flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        if (!this.Ui.Plugin.Config.CanMove) {
            flags |= ImGuiWindowFlags.NoMove;
        }

        if (!this.Ui.Plugin.Config.CanResize) {
            flags |= ImGuiWindowFlags.NoResize;
        }

        if (!this.Ui.Plugin.Config.ShowTitleBar) {
            flags |= ImGuiWindowFlags.NoTitleBar;
        }

        if (this._lastViewport == ImGuiHelpers.MainViewport.NativePtr && !this._wasDocked) {
            ImGui.SetNextWindowBgAlpha(this.Ui.Plugin.Config.WindowAlpha / 100f);
        }

        ImGui.SetNextWindowSize(new Vector2(500, 250) * ImGuiHelpers.GlobalScale, ImGuiCond.FirstUseEver);

        if (!ImGui.Begin($"{this.Ui.Plugin.Name}###chat2", flags)) {
            this._lastViewport = ImGui.GetWindowViewport().NativePtr;
            this._wasDocked = ImGui.IsWindowDocked();
            ImGui.End();
            return false;
        }

        var resized = this.LastWindowSize != ImGui.GetWindowSize();
        this.LastWindowSize = ImGui.GetWindowSize();
        this.LastWindowPos = ImGui.GetWindowPos();

        if (resized) {
            this._lastResize.Restart();
        }

        this._lastViewport = ImGui.GetWindowViewport().NativePtr;
        this._wasDocked = ImGui.IsWindowDocked();

        var currentTab = this.Ui.Plugin.Config.SidebarTabView
            ? this.DrawTabSidebar()
            : this.DrawTabBar();

        Tab? activeTab = null;
        if (currentTab > -1 && currentTab < this.Ui.Plugin.Config.Tabs.Count) {
            activeTab = this.Ui.Plugin.Config.Tabs[currentTab];
        }

        var channelHint = "";
        var channelSelector = false;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        try {
            if (this._tellTarget != null) {
                var world = this.Ui.Plugin.DataManager.GetExcelSheet<World>()
                    ?.GetRow(this._tellTarget.World)
                    ?.Name
                    ?.RawString ?? "???";

                channelHint = $"Tell " + this._tellTarget.Name;

                if (channelSelector) {
                    this.DrawChunks(new Chunk[] {
                        new TextChunk(ChunkSource.None, null, "Tell "),
                        new TextChunk(ChunkSource.None, null, this._tellTarget.Name),
                        new IconChunk(ChunkSource.None, null, BitmapFontIcon.CrossWorld),
                        new TextChunk(ChunkSource.None, null, world),
                    });
                }
            } else if (this._tempChannel != null) {
                if (this._tempChannel.Value.IsLinkshell()) {
                    var idx = (uint) this._tempChannel.Value - (uint) InputChannel.Linkshell1;
                    var lsName = this.Ui.Plugin.Functions.Chat.GetLinkshellName(idx);
                    channelHint = $"LS #{{idx + 1}}: {lsName}";

                    if (channelSelector) {
                        ImGui.TextUnformatted(channelHint);
                    }
                } else if (this._tempChannel.Value.IsCrossLinkshell()) {
                    var idx = (uint) this._tempChannel.Value - (uint) InputChannel.CrossLinkshell1;
                    var cwlsName = this.Ui.Plugin.Functions.Chat.GetCrossLinkshellName(idx);
                    channelHint = $"CWLS [{idx + 1}]: {cwlsName}";

                    if (channelSelector) {
                        ImGui.TextUnformatted(channelHint);
                    }
                } else {
                    channelHint = this._tempChannel.Value.ToChatType().Name();
                    if (channelSelector) {
                        ImGui.TextUnformatted(channelHint);
                    }
                }
            } else if (activeTab is { Channel: { } channel }) {
                channelHint = channel.ToChatType().Name();
                if (channelSelector) {
                    ImGui.TextUnformatted(channelHint);
                }
            } else if (this.Ui.Plugin.ExtraChat.ChannelOverride is var (overrideName, _)) {
                channelHint = overrideName;
                if (channelSelector) {
                    ImGui.TextUnformatted(channelHint);
                }
            } else {
                channelHint = this.Ui.Plugin.Functions.Chat.Channel.name.Aggregate("", (a, c) => $"{a}{c.StringValue()} ");

                if (channelSelector) {
                    this.DrawChunks(this.Ui.Plugin.Functions.Chat.Channel.name);
                }
            }
        } finally {
            ImGui.PopStyleVar();
        }

        var beforeIcon = ImGui.GetCursorPos();

        if (!this.Ui.Plugin.Config.SimplifiedInputField) {
            if (ImGuiUtil.IconButton(FontAwesomeIcon.Comment) && activeTab is not { Channel: { } }) {
                ImGui.OpenPopup(ChatChannelPicker);
            }
        } else {
            ImGui.NewLine();
        }

        if (activeTab is { Channel: { } } && ImGui.IsItemHovered()) {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(Language.ChatLog_SwitcherDisabled);
            ImGui.EndTooltip();
        }

        if (ImGui.BeginPopup(ChatChannelPicker)) {
            this.DrawChannelPicker();
            ImGui.EndPopup();
        }

        ImGui.SameLine();
        var afterIcon = ImGui.GetCursorPos();

        var buttonWidth = this.Ui.Plugin.Config.SimplifiedInputField ? 0 : (afterIcon.X - beforeIcon.X);
        var showNovice = this.Ui.Plugin.Config.ShowNoviceNetwork && this.Ui.Plugin.Functions.IsMentor();
        var inputWidth = ImGui.GetContentRegionAvail().X - buttonWidth * (showNovice ? 2 : 1);

        var inputType = this._tempChannel?.ToChatType() ?? activeTab?.Channel?.ToChatType() ?? this.Ui.Plugin.Functions.Chat.Channel.channel.ToChatType();
        var isCommand = this.Chat.Trim().StartsWith('/');
        if (isCommand) {
            var command = this.Chat.Split(' ')[0];
            if (this.TextCommandChannels.TryGetValue(command, out var channel)) {
                inputType = channel;
            }

            if (!this.IsValidCommand(command)) {
                inputType = ChatType.Error;
            }
        }

        var normalColour = *ImGui.GetStyleColorVec4(ImGuiCol.Text);

        var inputColour = this.Ui.Plugin.Config.ChatColours.TryGetValue(inputType, out var inputCol)
            ? inputCol
            : inputType.DefaultColour();

        if (!isCommand && this.Ui.Plugin.ExtraChat.ChannelOverride is var (_, overrideColour)) {
            inputColour = overrideColour;
        }

        if (isCommand && this.Ui.Plugin.ExtraChat.ChannelCommandColours.TryGetValue(this.Chat.Split(' ')[0], out var ecColour)) {
            inputColour = ecColour;
        }

        if (inputColour != null) {
            ImGui.PushStyleColor(ImGuiCol.Text, ColourUtil.RgbaToAbgr(inputColour.Value));
        }

        if (this.Activate) {
            ImGui.SetKeyboardFocusHere();
        }

        var chatCopy = this.Chat;
        ImGui.SetNextItemWidth(inputWidth);
        const ImGuiInputTextFlags inputFlags = ImGuiInputTextFlags.CallbackAlways
                                               | ImGuiInputTextFlags.CallbackCharFilter
                                               | ImGuiInputTextFlags.CallbackCompletion
                                               | ImGuiInputTextFlags.CallbackHistory;
        // ImGui.InputText("##chat2-input", ref this.Chat, 500, inputFlags, this.Callback);
        ImGui.InputTextWithHint("##chat2-input", channelHint ?? "", ref this.Chat, 500, inputFlags, this.Callback);

        if (ImGui.IsItemDeactivated()) {
            if (ImGui.IsKeyDown(ImGuiKey.Escape)) {
                this.Chat = chatCopy;
            }

            var enter = ImGui.IsKeyDown(ImGuiKey.Enter)
                        || ImGui.IsKeyDown(ImGuiKey.KeypadEnter);
            if (enter) {
                this.SendChatBox(activeTab);
            }
        }

        if (ImGui.IsItemActive()) {
            if (ImGui.IsKeyDown(ImGuiKey.ModAlt)) {
                var delta = ImGui.IsKeyPressed(ImGuiKey.LeftArrow) ? -1 : (ImGui.IsKeyPressed(ImGuiKey.RightArrow) ? +1 : 0);
                if (delta != 0) {
                    var newTab = currentTab + delta;

                    var count = this.Ui.Plugin.Config.Tabs.Count;
                    if (newTab < 0) {
                        newTab = count - 1;
                    } else if (newTab > count - 1) {
                        newTab = 0;
                    }

                    SwitchToTab = newTab;
                    PluginLog.Debug($"Current {currentTab}, Switching {newTab}");
                }
            }

            this.HandleKeybinds(true);
        }

        if (!this.Activate && !ImGui.IsItemActive()) {
            if (this._tempChannel is InputChannel.Tell) {
                this._tellTarget = null;
            }

            this._tempChannel = null;
        }

        if (ImGui.BeginPopupContextItem()) {
            ImGui.PushStyleColor(ImGuiCol.Text, normalColour);

            try {

                if (this.Ui.Plugin.Config.SimplifiedInputField) {
                    this.DrawChannelPicker();
                    ImGui.Separator();
                }

                if (ImGui.Selectable(Language.ChatLog_HideChat)) {
                    this.UserHide();
                }

                if (ImGui.Selectable("Settings")) {
                    this.Ui.SettingsVisible ^= true;
                }
            } finally {
                ImGui.PopStyleColor();
            }

            ImGui.EndPopup();
        }

        if (inputColour != null) {
            ImGui.PopStyleColor();
        }

        ImGui.SameLine();

        if (!this.Ui.Plugin.Config.SimplifiedInputField && ImGuiUtil.IconButton(FontAwesomeIcon.Cog)) {
            this.Ui.SettingsVisible ^= true;
        }

        if (showNovice) {
            ImGui.SameLine();

            if (ImGuiUtil.IconButton(FontAwesomeIcon.Leaf)) {
                this.Ui.Plugin.Functions.ClickNoviceNetworkButton();
            }
        }

        ImGui.End();

        return true;
    }

    private void DrawChannelPicker() {
        foreach (var channel in Enum.GetValues<InputChannel>()) {
            var name = this.Ui.Plugin.DataManager.GetExcelSheet<LogFilter>()!
                .FirstOrDefault(row => row.LogKind == (byte) channel.ToChatType())
                ?.Name
                ?.RawString ?? channel.ToString();

            if (channel.IsLinkshell()) {
                var lsName = this.Ui.Plugin.Functions.Chat.GetLinkshellName(channel.LinkshellIndex());
                if (string.IsNullOrWhiteSpace(lsName)) {
                    continue;
                }

                name += $": {lsName}";
            }

            if (channel.IsCrossLinkshell()) {
                var lsName = this.Ui.Plugin.Functions.Chat.GetCrossLinkshellName(channel.LinkshellIndex());
                if (string.IsNullOrWhiteSpace(lsName)) {
                    continue;
                }

                name += $": {lsName}";
            }

            if (ImGui.Selectable(name)) {
                this.Ui.Plugin.Functions.Chat.SetChannel(channel);
                this._tellTarget = null;
            }
        }
    }

    private void SendChatBox(Tab? activeTab) {
        if (!string.IsNullOrWhiteSpace(this.Chat)) {
            var trimmed = this.Chat.Trim();
            this.AddBacklog(trimmed);
            this._inputBacklogIdx = -1;

            if (!trimmed.StartsWith('/')) {
                if (this._tellTarget != null) {
                    var target = this._tellTarget;
                    var reason = target.Reason;
                    var world = this.Ui.Plugin.DataManager.GetExcelSheet<World>()?.GetRow(target.World);
                    if (world is { IsPublic: true }) {
                        if (reason == TellReason.Reply && this.Ui.Plugin.Common.Functions.FriendList.List.Any(friend => friend.ContentId == target.ContentId)) {
                            reason = TellReason.Friend;
                        }

                        this.Ui.Plugin.Functions.Chat.SendTell(reason, target.ContentId, target.Name, (ushort) world.RowId, trimmed);
                    }

                    if (this._tempChannel is InputChannel.Tell) {
                        this._tellTarget = null;
                    }

                    goto Skip;
                }

                var channel = activeTab.Channel;
                if (channel != null && activeTab.PartnerPayload != null) {
                    trimmed = $"{channel?.Prefix()} {activeTab.PartnerPayload.PlayerName}@{activeTab.PartnerPayload.World.Name} {trimmed}";
                } else if (this._tempChannel != null) {
                    trimmed = $"{this._tempChannel.Value.Prefix()} {trimmed}";
                } else if (channel != null) {
                    trimmed = $"{channel?.Prefix()} {trimmed}";
                }
            }

            var bytes = Encoding.UTF8.GetBytes(trimmed);
            AutoTranslate.ReplaceWithPayload(this.Ui.Plugin.DataManager, ref bytes);

            this.Ui.Plugin.Common.Functions.Chat.SendMessageUnsafe(bytes);
        }

        Skip:
        this.Chat = string.Empty;
    }

    internal void UserHide() {
        this._hideState = HideState.User;
    }

    private void DrawMessageLog(Tab tab, PayloadHandler handler, float childHeight, bool switchedTab) {
        if (ImGui.BeginChild("##chat2-messages", new Vector2(-1, childHeight))) {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            var table = tab.DisplayTimestamp && this.Ui.Plugin.Config.PrettierTimestamps;

            var oldCellPaddingY = ImGui.GetStyle().CellPadding.Y;
            if (this.Ui.Plugin.Config.PrettierTimestamps && this.Ui.Plugin.Config.MoreCompactPretty) {
                var padding = ImGui.GetStyle().CellPadding;
                padding.Y = 0;

                ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, padding);
            }

            if (table) {
                if (!ImGui.BeginTable("timestamp-table", 2, ImGuiTableFlags.PreciseWidths)) {
                    goto EndChild;
                }

                ImGui.TableSetupColumn("timestamps", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("messages", ImGuiTableColumnFlags.WidthStretch);
            }

            try {
                tab.MessagesMutex.Wait();

                var reset = false;
                if (this._lastResize.IsRunning && this._lastResize.Elapsed.TotalSeconds > 0.25) {
                    this._lastResize.Stop();
                    this._lastResize.Reset();
                    reset = true;
                }

                var renderCount = 0;
                var lastPos = ImGui.GetCursorPosY();
                var lastTimestamp = new DateTime();
                int? lastMessageHash = null;
                var sameCount = 0;
                for (var i = 0; i < tab.Messages.Count; i++) {
                    var message = tab.Messages[i];

                    if (reset) {
                        message.Height = null;
                        message.IsVisible = false;
                    }

                    if (this.Ui.Plugin.Config.CollapseDuplicateMessages) {
                        var messageHash = message.Hash;
                        var same = lastMessageHash == messageHash;
                        if (same) {
                            sameCount += 1;

                            if (i != tab.Messages.Count - 1) {
                                continue;
                            }
                        }

                        if (sameCount > 0) {
                            ImGui.SameLine();
                            this.DrawChunks(
                                new[] {
                                    new TextChunk(ChunkSource.None, null, $" ({sameCount + 1}x)") {
                                        FallbackColour = ChatType.System,
                                        Italic = true,
                                    },
                                },
                                true,
                                handler,
                                ImGui.GetContentRegionAvail().X
                            );
                            sameCount = 0;
                        }

                        lastMessageHash = messageHash;

                        if (same && i == tab.Messages.Count - 1) {
                            continue;
                        }
                    }

                    // go to next row
                    if (table) {
                        ImGui.TableNextColumn();
                    }

                    // message has rendered once
                    if (message.Height.HasValue) {
                        // message isn't visible, so render dummy
                        if (!message.IsVisible) {
                            var beforeDummy = ImGui.GetCursorPos();

                            if (table) {
                                // skip to the message column for vis test
                                ImGui.TableNextColumn();
                            }

                            ImGui.Dummy(new Vector2(10f, message.Height.Value));
                            message.IsVisible = ImGui.IsItemVisible();

                            if (message.IsVisible) {
                                if (table) {
                                    ImGui.TableSetColumnIndex(0);
                                }

                                ImGui.SetCursorPos(beforeDummy);
                            } else {
                                goto UpdateMessage;
                            }
                        }
                    }

                    if (tab.DisplayTimestamp) {
                        var format = this.Ui.Plugin.Config.TimestampsForce24Format ? "HH:mm" : "t";
                        var timestamp = message.Date.ToLocalTime().ToString(format);

                        var showTimestamp = true;
                        if (this.Ui.Plugin.Config.HideTimestampsSmart) {
                            var timeSpan = message.Date - lastTimestamp;
                            showTimestamp = timeSpan.TotalSeconds > 120.0;
                        } else if (this.Ui.Plugin.Config.HideSameTimestamps) {
                            showTimestamp = lastTimestamp.ToLocalTime().ToString(format) != timestamp;
                        }

                        if (showTimestamp) {
                            if (table) {
                                ImGui.TextUnformatted(timestamp);
                            } else {
                                this.DrawChunk(new TextChunk(ChunkSource.None, null, $"{timestamp} ") {
                                    Foreground = 0x999999FF,
                                });
                                ImGui.SameLine();
                            }

                            lastTimestamp = message.Date;
                        }
                    }

                    if (table) {
                        ImGui.TableNextColumn();
                    }

                    var lineWidth = ImGui.GetContentRegionAvail().X;

                    var beforeDraw = ImGui.GetCursorScreenPos();
                    if (message.Sender.Count > 0) {
                        this.DrawChunks(message.Sender, true, handler, lineWidth);
                        ImGui.SameLine();
                    }

                    if (message.Content.Count == 0) {
                        this.DrawChunks(new[] { new TextChunk(ChunkSource.Content, null, " ") }, true, handler, lineWidth);
                    } else {
                        var rpFormattingEnabled = this.Ui.Plugin.Config.RPFormattingEnabled;
                        var rpFormattingEnabledForTab = tab.RPFormatEnabled; 
                        
                        var content = rpFormattingEnabled && rpFormattingEnabledForTab && this.Ui.Plugin.RpMarkup.EnabledForMessage(message)
                            ? this.Ui.Plugin.RpMarkup.ApplyFormatting(message.Content)
                            : message.Content;

                        this.DrawChunks(content, true, handler, lineWidth);
                        renderCount++;
                    }

                    var afterDraw = ImGui.GetCursorScreenPos();

                    message.Height = ImGui.GetCursorPosY() - lastPos;
                    if (this.Ui.Plugin.Config.PrettierTimestamps && !this.Ui.Plugin.Config.MoreCompactPretty) {
                        message.Height -= oldCellPaddingY * 2;
                        beforeDraw.Y += oldCellPaddingY;
                        afterDraw.Y -= oldCellPaddingY;
                    }

                    message.IsVisible = ImGui.IsRectVisible(beforeDraw, afterDraw);

                    UpdateMessage:
                    lastPos = ImGui.GetCursorPosY();
                }
            } finally {
                tab.MessagesMutex.Release();
                ImGui.PopStyleVar(this.Ui.Plugin.Config.PrettierTimestamps && this.Ui.Plugin.Config.MoreCompactPretty ? 2 : 1);
            }

            if (switchedTab || ImGui.GetScrollY() >= ImGui.GetScrollMaxY()) {
                ImGui.SetScrollHereY(1f);
            }

            handler.Draw();

            if (table) {
                ImGui.EndTable();
            }
        }

        EndChild:
        ImGui.EndChild();
    }

    private int DrawTabBar() {
        var currentTabIndex = -1;

        var currentTab = this.Ui.CurrentTab;

        if (currentTab != null) {
            ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(currentTab.ButtonColor, 1f));
        }

        if (!ImGui.BeginTabBar("##chat2-tabs")) {
            return currentTabIndex;
        }

        for (var tabI = 0; tabI < this.Ui.Plugin.Config.Tabs.Count; tabI++) {
            var tab = this.Ui.Plugin.Config.Tabs[tabI];
            if (tab.PopOut) {
                continue;
            }

            var unread = tabI == this.LastTab || tab.UnreadMode == UnreadMode.None || tab.Unread == 0 ? "" : $" ({tab.Unread})";

            var opened = true;
            var flags = ImGuiTabItemFlags.NoReorder;
            if (this.SwitchToTab.HasValue && this.SwitchToTab == tabI) {
                this.SwitchToTab = null;
                PluginLog.Debug($"SwitchToTab fire: {tabI}");
                flags |= ImGuiTabItemFlags.SetSelected;
            }

            var color = new Vector4(tab.ButtonColor, 1f);
            ImGui.PushStyleColor(ImGuiCol.TabActive, color);
            ImGui.PushStyleColor(ImGuiCol.TabHovered, color);
            bool hasBeenDrawn;
            if (tab.IsProtected) {
                hasBeenDrawn = ImGuiUtil.BeginTabItem($"{tab.Name}{unread}###log-tab-{tabI}", flags);
            } else {
                hasBeenDrawn = ImGui.BeginTabItem($"{tab.Name}{unread}###log-tab-{tabI}", ref opened, flags);
            }

            ImGui.PopStyleColor(2);

            this.DrawTabContextMenu(tab, tabI);

            if (opened == false && !tab.IsProtected) {
                this.Ui.Plugin.Config.Tabs.RemoveAt(tabI);
                tabI--;
                break;
            }

            if (!hasBeenDrawn) {
                continue;
            }

            currentTabIndex = tabI;
            var switchedTab = this.LastTab != tabI;
            this.LastTab = tabI;
            tab.Unread = 0;

            this.DrawMessageLog(tab, this.PayloadHandler, GetRemainingHeightForMessageLog(), switchedTab);

            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();

        if (currentTab != null) {
            ImGui.PopStyleColor();
        }

        return currentTabIndex;
    }

    private int DrawTabSidebar() {
        var currentTab = -1;

        if (!ImGui.BeginTable("tabs-table", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable)) {
            return -1;
        }

        ImGui.TableSetupColumn("tabs", ImGuiTableColumnFlags.None, 1);
        ImGui.TableSetupColumn("chat", ImGuiTableColumnFlags.None, 4);

        ImGui.TableNextColumn();

        var switchedTab = false;
        var childHeight = GetRemainingHeightForMessageLog();
        if (ImGui.BeginChild("##chat2-tab-sidebar", new Vector2(-1, childHeight))) {
            for (var tabI = 0; tabI < this.Ui.Plugin.Config.Tabs.Count; tabI++) {
                var tab = this.Ui.Plugin.Config.Tabs[tabI];
                if (tab.PopOut) {
                    continue;
                }

                var unread = tabI == this.LastTab || tab.UnreadMode == UnreadMode.None || tab.Unread == 0 ? "" : $" ({tab.Unread})";
                var clicked = ImGui.Selectable($"{tab.Name}{unread}###log-tab-{tabI}", this.LastTab == tabI);
                this.DrawTabContextMenu(tab, tabI);

                if (!clicked) {
                    continue;
                }

                currentTab = tabI;
                switchedTab = this.LastTab != tabI;
                this.LastTab = tabI;
            }
        }

        ImGui.EndChild();

        ImGui.TableNextColumn();

        if (currentTab == -1 && this.LastTab < this.Ui.Plugin.Config.Tabs.Count) {
            currentTab = this.LastTab;
            this.Ui.Plugin.Config.Tabs[currentTab].Unread = 0;
        }

        if (currentTab > -1) {
            this.DrawMessageLog(this.Ui.Plugin.Config.Tabs[currentTab], this.PayloadHandler, childHeight, switchedTab);
        }

        ImGui.EndTable();

        return currentTab;
    }

    private void DrawTabContextMenu(Tab tab, int i) {
        if (!ImGui.BeginPopupContextItem()) {
            return;
        }

        var tabs = this.Ui.Plugin.Config.Tabs;
        var anyChanged = false;

        ImGui.PushID($"tab-context-menu-{i}");

        ImGui.SetNextItemWidth(250f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("##tab-name", ref tab.Name, 128)) {
            anyChanged = true;
        }

        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, tooltip: Language.ChatLog_Tabs_Delete)) {
            tabs.RemoveAt(i);
            anyChanged = true;
        }

        ImGui.SameLine();

        var (leftIcon, leftTooltip) = this.Ui.Plugin.Config.SidebarTabView
            ? (FontAwesomeIcon.ArrowUp, Language.ChatLog_Tabs_MoveUp)
            : (FontAwesomeIcon.ArrowLeft, Language.ChatLog_Tabs_MoveLeft);
        if (ImGuiUtil.IconButton(leftIcon, tooltip: leftTooltip) && i > 0) {
            (tabs[i - 1], tabs[i]) = (tabs[i], tabs[i - 1]);
            ImGui.CloseCurrentPopup();
            anyChanged = true;
        }

        ImGui.SameLine();

        var (rightIcon, rightTooltip) = this.Ui.Plugin.Config.SidebarTabView
            ? (FontAwesomeIcon.ArrowDown, Language.ChatLog_Tabs_MoveDown)
            : (FontAwesomeIcon.ArrowRight, Language.ChatLog_Tabs_MoveRight);
        if (ImGuiUtil.IconButton(rightIcon, tooltip: rightTooltip) && i < tabs.Count - 1) {
            (tabs[i + 1], tabs[i]) = (tabs[i], tabs[i + 1]);
            ImGui.CloseCurrentPopup();
            anyChanged = true;
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.WindowRestore, tooltip: Language.ChatLog_Tabs_PopOut)) {
            tab.PopOut = true;
            anyChanged = true;
        }

        if (anyChanged) {
            this.Ui.Plugin.SaveConfig();
        }

        ImGui.PopID();
        ImGui.EndPopup();
    }

    private readonly List<bool> _popOutDocked = new();

    private void DrawPopOuts() {
        this.HandlerLender.ResetCounter();

        if (this._popOutDocked.Count != this.Ui.Plugin.Config.Tabs.Count) {
            this._popOutDocked.Clear();
            this._popOutDocked.AddRange(Enumerable.Repeat(false, this.Ui.Plugin.Config.Tabs.Count));
        }

        for (var i = 0; i < this.Ui.Plugin.Config.Tabs.Count; i++) {
            var tab = this.Ui.Plugin.Config.Tabs[i];
            if (!tab.PopOut) {
                continue;
            }

            this.DrawPopOut(tab, i);
        }
    }

    private void DrawPopOut(Tab tab, int idx) {
        var flags = ImGuiWindowFlags.None;
        if (!this.Ui.Plugin.Config.ShowPopOutTitleBar) {
            flags |= ImGuiWindowFlags.NoTitleBar;
        }

        if (!this._popOutDocked[idx]) {
            var alpha = tab.IndependentOpacity ? tab.Opacity : this.Ui.Plugin.Config.WindowAlpha;
            ImGui.SetNextWindowBgAlpha(alpha / 100f);
        }

        ImGui.SetNextWindowSize(new Vector2(350, 350) * ImGuiHelpers.GlobalScale, ImGuiCond.FirstUseEver);
        if (!ImGui.Begin($"{tab.Name}##popout", ref tab.PopOut, flags)) {
            goto End;
        }

        ImGui.PushID($"popout-{tab.Name}");

        if (!this.Ui.Plugin.Config.ShowPopOutTitleBar) {
            ImGui.TextUnformatted(tab.Name);
            ImGui.Separator();
        }

        var handler = this.HandlerLender.Borrow();
        this.DrawMessageLog(tab, handler, ImGui.GetContentRegionAvail().Y, false);

        ImGui.PopID();

        End:
        this._popOutDocked[idx] = ImGui.IsWindowDocked();
        ImGui.End();

        if (!tab.PopOut) {
            this.Ui.Plugin.SaveConfig();
        }
    }

    private unsafe void DrawAutoComplete() {
        if (this._autoCompleteInfo == null) {
            return;
        }

        this._autoCompleteList ??= AutoTranslate.Matching(this.Ui.Plugin.DataManager, this._autoCompleteInfo.ToComplete, this.Ui.Plugin.Config.SortAutoTranslate);

        if (this._autoCompleteOpen) {
            ImGui.OpenPopup(AutoCompleteId);
            this._autoCompleteOpen = false;
        }

        ImGui.SetNextWindowSize(new Vector2(400, 300) * ImGuiHelpers.GlobalScale);
        if (!ImGui.BeginPopup(AutoCompleteId)) {
            if (this._activatePos == -1) {
                this._activatePos = this._autoCompleteInfo.EndPos;
            }

            this._autoCompleteInfo = null;
            this._autoCompleteList = null;
            this.Activate = true;
            return;
        }

        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##auto-complete-filter", Language.AutoTranslate_Search_Hint, ref this._autoCompleteInfo.ToComplete, 256, ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.CallbackHistory, this.AutoCompleteCallback)) {
            this._autoCompleteList = AutoTranslate.Matching(this.Ui.Plugin.DataManager, this._autoCompleteInfo.ToComplete, this.Ui.Plugin.Config.SortAutoTranslate);
            this._autoCompleteSelection = 0;
            this._autoCompleteShouldScroll = true;
        }

        var selected = -1;
        if (ImGui.IsItemActive() && ImGui.GetIO().KeyCtrl) {
            for (var i = 0; i < 10 && i < this._autoCompleteList.Count; i++) {
                var num = (i + 1) % 10;
                var key = ImGuiKey._0 + num;
                var key2 = ImGuiKey.Keypad0 + num;
                if (ImGui.IsKeyDown(key) || ImGui.IsKeyDown(key2)) {
                    selected = i;
                }
            }
        }

        if (ImGui.IsItemDeactivated()) {
            if (ImGui.IsKeyDown(ImGuiKey.Escape)) {
                ImGui.CloseCurrentPopup();
                goto End;
            }

            var enter = ImGui.IsKeyDown(ImGuiKey.Enter)
                        || ImGui.IsKeyDown(ImGuiKey.KeypadEnter);
            if (this._autoCompleteList.Count > 0 && enter) {
                selected = this._autoCompleteSelection;
            }
        }

        if (ImGui.IsWindowAppearing()) {
            this._fixCursor = true;
            ImGui.SetKeyboardFocusHere(-1);
        }

        if (ImGui.BeginChild("##auto-complete-list", Vector2.Zero, false, ImGuiWindowFlags.HorizontalScrollbar)) {
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());

            clipper.Begin(this._autoCompleteList.Count);
            while (clipper.Step()) {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++) {
                    var entry = this._autoCompleteList[i];

                    var highlight = this._autoCompleteSelection == i;
                    var clicked = ImGui.Selectable($"{entry.String}##{entry.Group}/{entry.Row}", highlight) || selected == i;

                    if (i < 10) {
                        var button = (i + 1) % 10;
                        var text = string.Format(Language.AutoTranslate_Completion_Key, button);
                        var size = ImGui.CalcTextSize(text);
                        ImGui.SameLine(ImGui.GetContentRegionAvail().X - size.X);
                        ImGui.PushStyleColor(ImGuiCol.Text, *ImGui.GetStyleColorVec4(ImGuiCol.TextDisabled));
                        ImGui.TextUnformatted(text);
                        ImGui.PopStyleColor();
                    }

                    if (!clicked) {
                        continue;
                    }

                    var before = this.Chat[..this._autoCompleteInfo.StartPos];
                    var after = this.Chat[this._autoCompleteInfo.EndPos..];
                    var replacement = $"<at:{entry.Group},{entry.Row}>";
                    this.Chat = $"{before}{replacement}{after}";
                    ImGui.CloseCurrentPopup();
                    this.Activate = true;
                    this._activatePos = this._autoCompleteInfo.StartPos + replacement.Length;
                }
            }

            if (this._autoCompleteShouldScroll) {
                this._autoCompleteShouldScroll = false;
                var selectedPos = clipper.StartPosY + clipper.ItemsHeight * (this._autoCompleteSelection * 1f);
                ImGui.SetScrollFromPosY(selectedPos - ImGui.GetWindowPos().Y);
            }

            ImGui.EndChild();
        }

        End:
        ImGui.EndPopup();
    }

    private unsafe int AutoCompleteCallback(ImGuiInputTextCallbackData* data) {
        if (this._fixCursor && this._autoCompleteInfo != null) {
            this._fixCursor = false;
            data->CursorPos = this._autoCompleteInfo.ToComplete.Length;
            data->SelectionStart = data->SelectionEnd = data->CursorPos;
        }

        if (this._autoCompleteList == null) {
            return 0;
        }

        switch (data->EventKey) {
            case ImGuiKey.UpArrow: {
                if (this._autoCompleteSelection == 0) {
                    this._autoCompleteSelection = this._autoCompleteList.Count - 1;
                } else {
                    this._autoCompleteSelection--;
                }

                this._autoCompleteShouldScroll = true;

                return 1;
            }
            case ImGuiKey.DownArrow: {
                if (this._autoCompleteSelection == this._autoCompleteList.Count - 1) {
                    this._autoCompleteSelection = 0;
                } else {
                    this._autoCompleteSelection++;
                }

                this._autoCompleteShouldScroll = true;

                return 1;
            }
        }

        return 0;
    }

    private unsafe int Callback(ImGuiInputTextCallbackData* data) {
        var ptr = new ImGuiInputTextCallbackDataPtr(data);

        if (data->EventFlag == ImGuiInputTextFlags.CallbackCompletion) {
            if (ptr.CursorPos == 0) {
                this._autoCompleteInfo = new AutoCompleteInfo(
                    string.Empty,
                    ptr.CursorPos,
                    ptr.CursorPos
                );
                this._autoCompleteOpen = true;
                this._autoCompleteSelection = 0;

                return 0;
            }

            int white;
            for (white = ptr.CursorPos - 1; white >= 0; white--) {
                if (data->Buf[white] == ' ') {
                    break;
                }
            }

            this._autoCompleteInfo = new AutoCompleteInfo(
                Marshal.PtrToStringUTF8(ptr.Buf + white + 1, ptr.CursorPos - white - 1),
                white + 1,
                ptr.CursorPos
            );
            this._autoCompleteOpen = true;
            this._autoCompleteSelection = 0;
            return 0;
        }

        if (data->EventFlag == ImGuiInputTextFlags.CallbackCharFilter) {
            var valid = this.Ui.Plugin.Functions.Chat.IsCharValid((char) ptr.EventChar);
            if (!valid) {
                return 1;
            }

            if (this._currentCommandText != null) {
                ushort? subChar = null;
                var layoutSubMap = this.Ui.Plugin.Config.KeyLayoutSubMap;
                var charPos = layoutSubMap.IndexOf((char) ptr.EventChar);
                if (charPos >= 0 && charPos < layoutSubMap.Length / 2) {
                    subChar = layoutSubMap[charPos + layoutSubMap.Length / 2];
                }

                if (!this.Ui.Plugin.Config.KeyLayoutSubDisabledCommands.Contains(this._currentCommandText)) {
                    data->EventChar = subChar ?? ptr.EventChar;
                }
            }
        }

        if (this.Activate) {
            this.Activate = false;
            var utf8Bytes = Encoding.UTF8.GetByteCount(this.Chat);
            data->CursorPos = this._activatePos > -1 ? this._activatePos : utf8Bytes;
            data->SelectionStart = data->SelectionEnd = data->CursorPos;
            this._activatePos = -1;
        }

        var text = MemoryHelper.ReadString((IntPtr) data->Buf, data->BufTextLen);
        if (text.StartsWith('/')) {
            var command = text.Split(' ')[0];
            this._currentCommandText = command;
            var cmd = this.Ui.Plugin.DataManager.GetExcelSheet<TextCommand>()?.FirstOrDefault(cmd => cmd.Command.RawString == command
                                                                                                     || cmd.Alias.RawString == command
                                                                                                     || cmd.ShortCommand.RawString == command
                                                                                                     || cmd.ShortAlias.RawString == command);

            if (cmd != null) {
                this._commandHelp = new CommandHelp(this, cmd);

                goto PostCommandHelp;
            }
        } else {
            this._currentCommandText = null;
        }

        this._commandHelp = null;

        PostCommandHelp:
        if (data->EventFlag != ImGuiInputTextFlags.CallbackHistory) {
            return 0;
        }

        var prevPos = this._inputBacklogIdx;

        switch (data->EventKey) {
            case ImGuiKey.UpArrow:
                switch (this._inputBacklogIdx) {
                    case -1:
                        var offset = 0;

                        if (!string.IsNullOrWhiteSpace(this.Chat)) {
                            this.AddBacklog(this.Chat);
                            offset = 1;
                        }

                        this._inputBacklogIdx = this._inputBacklog.Count - 1 - offset;
                        break;
                    case > 0:
                        this._inputBacklogIdx--;
                        break;
                }

                break;
            case ImGuiKey.DownArrow: {
                if (this._inputBacklogIdx != -1) {
                    if (++this._inputBacklogIdx >= this._inputBacklog.Count) {
                        this._inputBacklogIdx = -1;
                    }
                }

                break;
            }
        }

        if (prevPos == this._inputBacklogIdx) {
            return 0;
        }

        var historyStr = this._inputBacklogIdx >= 0 ? this._inputBacklog[this._inputBacklogIdx] : string.Empty;

        ptr.DeleteChars(0, ptr.BufTextLen);
        ptr.InsertChars(0, historyStr);

        return 0;
    }

    internal void DrawChunks(IReadOnlyList<Chunk> chunks, bool wrap = true, PayloadHandler? handler = null, float lineWidth = 0f) {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        try {
            for (var i = 0; i < chunks.Count; i++) {
                if (chunks[i] is TextChunk text && string.IsNullOrEmpty(text.Content)) {
                    continue;
                }

                this.DrawChunk(chunks[i], wrap, handler, lineWidth);

                if (i < chunks.Count - 1) {
                    ImGui.SameLine();
                }
            }
        } finally {
            ImGui.PopStyleVar();
        }
    }

    private void DrawChunk(Chunk chunk, bool wrap = true, PayloadHandler? handler = null, float lineWidth = 0f) {
        if (chunk is IconChunk icon && this._fontIcon != null) {
            var bounds = IconUtil.GetBounds((byte) icon.Icon);
            if (bounds != null) {
                var texSize = new Vector2(this._fontIcon.Width, this._fontIcon.Height);

                var sizeRatio = this.Ui.Plugin.Config.FontSize / bounds.Value.W;
                var size = new Vector2(bounds.Value.Z, bounds.Value.W) * sizeRatio * ImGuiHelpers.GlobalScale;

                var uv0 = new Vector2(bounds.Value.X, bounds.Value.Y - 2) / texSize;
                var uv1 = new Vector2(bounds.Value.X + bounds.Value.Z, bounds.Value.Y - 2 + bounds.Value.W) / texSize;
                ImGui.Image(this._fontIcon.ImGuiHandle, size, uv0, uv1);
                ImGuiUtil.PostPayload(chunk, handler);
            }

            return;
        }

        if (chunk is not TextChunk text) {
            return;
        }

        var colour = text.Foreground;
        if (colour == null && text.FallbackColour != null) {
            var type = text.FallbackColour.Value;
            colour = this.Ui.Plugin.Config.ChatColours.TryGetValue(type, out var col)
                ? col
                : type.DefaultColour();
        }

        if (colour != null) {
            colour = ColourUtil.RgbaToAbgr(colour.Value);
            ImGui.PushStyleColor(ImGuiCol.Text, colour.Value);
        }

        var pushed = false;
        if (text.Italic) {
            if (this.Ui.ItalicFont.HasValue && this.Ui.Plugin.Config.FontsEnabled) {
                ImGui.PushFont(this.Ui.ItalicFont.Value);
                pushed = true;
            }

            if (!this.Ui.Plugin.Config.FontsEnabled && (this.Ui.AxisItalic?.Available ?? false)) {
                ImGui.PushFont(this.Ui.AxisItalic.ImFont);
                pushed = true;
            }
        }

        var content = text.Content;
        if (this.Ui.ScreenshotMode) {
            if (chunk.Link is PlayerPayload playerPayload) {
                var hashCode = $"{this.Ui.Salt}{playerPayload.PlayerName}{playerPayload.World.RowId}".GetHashCode();
                content = $"Player {hashCode:X8}";
            } else if (this.Ui.Plugin.ClientState.LocalPlayer is { } player && content.Contains(player.Name.TextValue)) {
                var hashCode = $"{this.Ui.Salt}{player.Name.TextValue}{player.HomeWorld.Id}".GetHashCode();
                content = content.Replace(player.Name.TextValue, $"Player {hashCode:X8}");
            }
        }

        if (wrap) {
            ImGuiUtil.WrapText(content, chunk, handler, this.Ui.DefaultText, lineWidth);
        } else {
            ImGui.TextUnformatted(content);
            ImGuiUtil.PostPayload(chunk, handler);
        }

        if (pushed) {
            ImGui.PopFont();
        }

        if (colour != null) {
            ImGui.PopStyleColor();
        }
    }
}