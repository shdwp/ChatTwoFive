using System.Security.Cryptography;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Group;

namespace ChatTwoFive.RTyping;

public sealed class RTypingAdapter {

    public List<string> TypingList { get; } = new();

    public IClientState ClientState => this._plugin.ClientState;
    public IChatGui ChatGui => this._plugin.ChatGui;

    private readonly Plugin _plugin;
    private readonly Client _client;

    public RTypingAdapter(Plugin plugin) {
        this._plugin = plugin;
        this._client = new Client(this);
    }

    public string HashContentID(ulong id) {
        var crypt = SHA256.Create();
        var hash = new StringBuilder();
        var crypto = crypt.ComputeHash(Encoding.UTF8.GetBytes($"{id}"));
        crypt.Clear();
        foreach (var cByte in crypto) {
            hash.Append(cByte.ToString("x2"));
        }
        return hash.ToString();
    }

    public void SendStartedTyping() {
        this._client.SendTyping(this.ObtainPartyMembers());
    }

    public void SendStoppedTyping() {
        this._client.SendStoppedTyping(this.ObtainPartyMembers());
    }

    private unsafe string ObtainPartyMembers() {
        var trustAnyone = true;

        var MemberIDs = new List<string>();

        var manager = (GroupManager*) this._plugin.PartyList.GroupManagerAddress;

        for (var i = 0; i < manager->MemberCount; i++) {
            var member = manager->GetPartyMemberByIndex(i);
            var cid = (ulong) member->ContentID;
            if ( /*trustedList.Contains($"{MemoryHelper.ReadSeStringNullTerminated((nint)member->Name)}@{member->HomeWorld}") ||*/ trustAnyone)
                MemberIDs.Add(HashContentID(cid));
        }

        return string.Join(",", MemberIDs.ToArray());
    }
}