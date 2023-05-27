using System.Diagnostics;
using ChatTwoFive;

namespace ChatTwoFiveTests;

public class MarkupParserTests {

    [SetUp]
    public void Setup() {
    }

    [Test]
    public void FormattingTest() {
        var parser = new RPMarkup(null);

        var chunks = new List<Chunk> {
            new TextChunk(ChunkSource.Content, null, "quote, quote *emote emote* ((out of character))")
        };

        var contents = parser.ApplyFormatting(chunks);
        Console.WriteLine($"Chunks: {contents.Count}");

        foreach (var c in contents) {
            switch (c) {
                case TextChunk tc: {
                    Console.WriteLine($"[it{tc.Italic}]{tc.Content}EOL");
                    break;
                }

                default: {
                    break;
                }
            }
        }

        Assert.Pass();
    }

    [Test]
    public void PerformanceTest() {
        var parser = new RPMarkup(null);

        var chunks = new List<Chunk> {
            new TextChunk(ChunkSource.Content, null, "name quote *emote emote* ((out of character)) *(emote brackets)* another quote"
                                                     + "((ooc2)) another quote *super long emote text incoming*"
                                                     + "another quote and more quote 123456789"
                                                     + "(brackets and stuff blah blah blah blah)"
                                                     + "maybe i should include lorem ipsum here"
                                                     + "since the typing kinda gets ridiculous")
        };

        var sw = new Stopwatch();
        sw.Start();
        var iterations = 50;
        for (var i = 0; i < iterations; i++) {
            var contents = parser.ApplyFormatting(chunks);
        }

        double ns = 1000000000.0 * sw.ElapsedTicks / Stopwatch.Frequency;
        double ms = ns / 1000000.0;
        Console.WriteLine($"{ms:F5}ms ({sw.ElapsedTicks}) per {iterations} ({(ms / iterations):F5}ms)");
    }
}