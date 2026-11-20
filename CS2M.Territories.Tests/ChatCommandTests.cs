using CS2M.Territories.Core;
using NUnit.Framework;

namespace CS2M.Territories.Tests
{
    public class ChatCommandTests
    {
        [TestCase("hello", ChatCommandKind.None)]
        [TestCase("", ChatCommandKind.None)]
        [TestCase("/help", ChatCommandKind.Help)]
        [TestCase("/LAND", ChatCommandKind.Land)]
        [TestCase("  /money ", ChatCommandKind.Money)]
        [TestCase("/claim 3", ChatCommandKind.Invalid)]
        [TestCase("/claim a b", ChatCommandKind.Invalid)]
        [TestCase("/give 1 -5", ChatCommandKind.Invalid)]
        [TestCase("/teleport", ChatCommandKind.Invalid)]
        public void Kinds(string text, ChatCommandKind expected)
        {
            Assert.That(ChatCommandParser.Parse(text).Kind, Is.EqualTo(expected));
        }

        [Test]
        public void Claim_And_Give_CarryArguments()
        {
            ChatCommand c = ChatCommandParser.Parse("/claim 4 17");
            Assert.That((c.Kind, c.X, c.Y), Is.EqualTo((ChatCommandKind.Claim, 4, 17)));

            ChatCommand g = ChatCommandParser.Parse("/give -1 250000");
            Assert.That((g.Kind, g.TargetPlayer, g.Amount), Is.EqualTo((ChatCommandKind.Give, -1, 250000L)));
        }
    }
}
