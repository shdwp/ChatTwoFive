using ChatTwo;

namespace TestProject1;

public class Tests {
    [SetUp]
    public void Setup() {
    }

    [Test]
    public void Test1() {
        var parser = new MarkupParser();
        
        Assert.Pass();
    }
}