using System.Text;
using ChatTwoFive.Util;

namespace ChatTwoFive;

public sealed class RPMarkup {
    private struct State : IEquatable<State> {
        internal bool Phrase;
        internal bool Emote;
        internal bool OOC;

        internal bool IsInBlock() {
            return this.Emote || this.OOC;
        }

        public bool Equals(State other) {
            return this.Phrase == other.Phrase && this.Emote == other.Emote && this.OOC == other.OOC;
        }

        public override bool Equals(object? obj) {
            return obj is State other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(this.Phrase, this.Emote, this.OOC);
        }

        public static bool operator ==(State left, State right) {
            return left.Equals(right);
        }

        public static bool operator !=(State left, State right) {
            return !left.Equals(right);
        }
    }

    private Plugin _plugin;

    public RPMarkup(Plugin plugin) {
        this._plugin = plugin;
    }

    internal bool EnabledForMessage(Message msg) {
        return this._plugin.Config.RPFormattingEnabledTypes.Contains(msg.Code.Type);
    }

    public List<Chunk> ApplyFormatting(List<Chunk> chunks) {
        var result = new List<Chunk>(chunks.Count * 4);

        foreach (var chunk in chunks) {
            switch (chunk) {
                case TextChunk textChunk:
                    this.ParseTextChunk(textChunk, ref result);
                    break;

                default:
                    result.Add(chunk);
                    break;
            }
        }

        return result;
    }

    private void ParseTextChunk(TextChunk parsedChunk, ref List<Chunk> result) {
        var text = parsedChunk.Content;
        var state = new State();
        state.Phrase = true;
        var buffer = new StringBuilder();

        var resultDeref = result;

        void CommitBuffer(State bufferState) {
            if (buffer.Length == 0) {
                return;
            }

            var newChunk = new TextChunk(ChunkSource.Content, parsedChunk.Link, buffer.ToString());
            ApplyStateToChunk(newChunk, parsedChunk, bufferState);
            resultDeref.Add(newChunk);

            buffer.Clear();
        }

        char? pch = null;
        var previousState = state;
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            char? nch = i < text.Length - 1 ? text[i + 1] : null;

            previousState = state;
            bool ignore = false;

            if (ch == '*') {
                state.Emote ^= true;
                ignore = true;
            }

            if (ch == '(' && nch == '(') {
                state.OOC = true;
                ignore = true;
            }

            if (ch == '(' && pch == '(') {
                continue;
            }

            if (ch == ')' && nch == ')') {
                state.OOC = false;
                ignore = true;
            }

            if (ch == ')' && pch == ')') {
                continue;
            }

            if (state != previousState) {
                CommitBuffer(previousState);
                state.Phrase = !state.IsInBlock();
            }

            if (!ignore) {
                buffer.Append(ch);
            }

            pch = ch;
        }

        if (previousState.IsInBlock()) {
            previousState = new State();
            previousState.Phrase = true;
        }
        
        CommitBuffer(previousState);
    }

    private void ApplyStateToChunk(TextChunk chunk, TextChunk parentChunk, State state) {
        var content = chunk.Content;
        CalculateWhitespace(content, out var preWhitespace, out var postWhitespace);

        if (postWhitespace + preWhitespace == content.Length) {
            return;
        }

        if (this._plugin == null) {
            return;
        }

        if (state.Emote) {
            var settings = this._plugin.Config.RPEmoteSettings;
            if (settings.Wrap) {
                chunk.Content = WrapTextContent(content, preWhitespace, postWhitespace, "*", "*");
            }

            ApplySettingsToChunk(chunk, parentChunk, settings);
        }

        if (state.OOC) {
            var settings = this._plugin.Config.RPOOCSettings;
            if (settings.Wrap) {
                chunk.Content = WrapTextContent(content, preWhitespace, postWhitespace, "((", "))");
            }

            ApplySettingsToChunk(chunk, parentChunk, settings);
        }

        if (state.Phrase) {
            var settings = this._plugin.Config.RPPhraseSettings;
            if (settings.Wrap) {
                chunk.Content = WrapTextContent(content, preWhitespace, postWhitespace, "“", "”");
            }

            ApplySettingsToChunk(chunk, parentChunk, settings);
        }
    }

    private void ApplySettingsToChunk(TextChunk chunk, TextChunk parentChunk, RPBlockSettings settings) {
        if (settings.PreserveStyle) {
            chunk.Italic = parentChunk.Italic;
            chunk.Foreground = parentChunk.Foreground;
            chunk.Glow = parentChunk.Glow;
        } else {
            chunk.Italic = settings.Italic;
            chunk.Foreground = ColourUtil.Vector3ToRgba(settings.Color);
            chunk.Glow = ColourUtil.Vector3ToRgba(settings.Glow);
        }

        chunk.FallbackColour = parentChunk.FallbackColour;
        chunk.Message = parentChunk.Message;
    }

    private void CalculateWhitespace(string content, out int preWhitespace, out int postWhitespace) {
        preWhitespace = 0;
        var stillInPrefix = true;
        var whitespace = 0;
        for (var i = 0; i < content.Length; i++) {
            if (content[i] == ' ') {
                whitespace++;
            } else if (stillInPrefix) {
                preWhitespace = whitespace;
                stillInPrefix = false;
                whitespace = 0;
            } else {
                whitespace = 0;
            }
        }

        postWhitespace = whitespace;
    }

    private string WrapTextContent(string content, int preWhitespace, int postWhitespace, string prefix, string suffix) {
        var sb = new StringBuilder();
        for (var i = 0; i < preWhitespace; i++) {
            sb.Append(' ');
        }

        sb.Append($"{prefix}{content.Substring(preWhitespace, content.Length - postWhitespace - preWhitespace)}{suffix}");

        for (var i = 0; i < postWhitespace; i++) {
            sb.Append(' ');
        }

        return sb.ToString();
    }
}