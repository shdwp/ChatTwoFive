using ChatTwoFive.Code;
using Dalamud.Game.Text.SeStringHandling;
using LiteDB;

namespace ChatTwoFive;

public abstract class Chunk {
    [BsonIgnore]
    internal Message? Message { get; set; }

    internal ChunkSource Source { get; set; }
    internal Payload? Link { get; set; }

    protected Chunk(ChunkSource source, Payload? link) {
        this.Source = source;
        this.Link = link;
    }

    internal SeString? GetSeString() => this.Source switch {
        ChunkSource.None => null,
        ChunkSource.Sender => this.Message?.SenderSource,
        ChunkSource.Content => this.Message?.ContentSource,
        _ => null,
    };

    /// <summary>
    /// Get some basic text for use in generating hashes.
    /// </summary>
    internal string StringValue() {
        switch (this) {
            case TextChunk text:
                return text.Content;
            case IconChunk icon:
                return icon.Icon.ToString();
            default:
                return "";
        }
    }
}

public enum ChunkSource {
    None,
    Sender,
    Content,
}

public class TextChunk : Chunk {
    public ChatType? FallbackColour { get; set; }
    public uint? Foreground { get; set; }
    public uint? Glow { get; set; }
    public bool Italic { get; set; }
    public string Content { get; set; }

    public TextChunk(ChunkSource source, Payload? link, string content) : base(source, link) {
        this.Content = content;
    }

    #pragma warning disable CS8618
    public TextChunk() : base(ChunkSource.None, null) {
    }
    #pragma warning restore CS8618
}

internal class IconChunk : Chunk {
    internal BitmapFontIcon Icon { get; set; }

    public IconChunk(ChunkSource source, Payload? link, BitmapFontIcon icon) : base(source, link) {
        this.Icon = icon;
    }

    public IconChunk() : base(ChunkSource.None, null) {
    }
}