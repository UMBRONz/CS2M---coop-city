using System;
using System.Globalization;

namespace CS2M.Territories.Core
{
    public enum ChatCommandKind
    {
        None,       // not a command: send as a normal chat message
        Help,
        Land,       // list my tiles and the tiles I can buy next
        Money,      // balances of all mayors
        Claim,      // /claim <x> <y>
        Give,       // /give <playerId> <amount>
        Invalid     // looked like a command but could not be parsed
    }

    public sealed class ChatCommand
    {
        public ChatCommandKind Kind { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int TargetPlayer { get; set; }
        public long Amount { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    ///     Text commands typed into the CS2M chat. A stop-gap until the territory map UI exists.
    /// </summary>
    public static class ChatCommandParser
    {
        public const string HelpText =
            "/land - your tiles and what you can buy | /claim X Y - buy a tile | " +
            "/money - treasuries | /give PLAYER AMOUNT - send money";

        public static ChatCommand Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith("/"))
            {
                return new ChatCommand { Kind = ChatCommandKind.None };
            }

            string[] parts = text.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string verb = parts[0].Substring(1).ToLowerInvariant();
            switch (verb)
            {
                case "help":
                case "?":
                    return new ChatCommand { Kind = ChatCommandKind.Help };
                case "land":
                    return new ChatCommand { Kind = ChatCommandKind.Land };
                case "money":
                    return new ChatCommand { Kind = ChatCommandKind.Money };
                case "claim":
                    if (parts.Length == 3 && TryInt(parts[1], out int x) && TryInt(parts[2], out int y))
                    {
                        return new ChatCommand { Kind = ChatCommandKind.Claim, X = x, Y = y };
                    }

                    return Invalid("usage: /claim X Y");
                case "give":
                    if (parts.Length == 3 && TryInt(parts[1], out int target)
                                          && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long amount)
                                          && amount > 0)
                    {
                        return new ChatCommand { Kind = ChatCommandKind.Give, TargetPlayer = target, Amount = amount };
                    }

                    return Invalid("usage: /give PLAYER AMOUNT");
                default:
                    return Invalid($"unknown command /{verb}. {HelpText}");
            }
        }

        private static bool TryInt(string s, out int value)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static ChatCommand Invalid(string error)
        {
            return new ChatCommand { Kind = ChatCommandKind.Invalid, Error = error };
        }
    }
}
