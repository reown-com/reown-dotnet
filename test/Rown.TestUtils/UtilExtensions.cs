namespace Reown.TestUtils;

public static class UtilExtensions
{
    public static IEnumerable<string> NextStrings(
        this Random rnd,
        string allowedChars,
        (int Min, int Max) length,
        int count)
    {
        ISet<string> usedRandomStrings = new HashSet<string>();
        var (min, max) = length;
        var chars = new char[max];
        var setLength = allowedChars.Length;

        while (count-- > 0)
        {
            var stringLength = rnd.Next(min, max + 1);

            for (var i = 0; i < stringLength; ++i)
            {
                chars[i] = allowedChars[rnd.Next(setLength)];
            }

            var randomString = new string(chars, 0, stringLength);

            if (usedRandomStrings.Add(randomString))
            {
                yield return randomString;
            }
            else
            {
                count++;
            }
        }
    }
}