using System.Text.RegularExpressions;
using CWCore;

ComicWalker walker = new();

string? cid = null, episode = null;

// If the user provided a URL
if (args.Length == 1 && args[0].StartsWith("http"))
{
    string url = args[0];
    var regex = @"https:\/\/comic-walker\.com\/detail\/(?<cid>.+?)(?:\/episodes\/(?<episode>.+?))?\b";
    Match match = Regex.Match(url, regex);
    if (match.Success)
    {
        cid = match.Groups["cid"].Value;
        var episodeMatch = match.Groups["episode"];
        episode = episodeMatch.Success ? episodeMatch.Value : (cid[..^2] + "0000100011_E");
    }
}

// If the user provided a cid and episode separately
else if (args.Length >= 2 && Int32.TryParse(args[1], out int episodeNum))
{
    cid = args[0];
    episode = cid[..^2] + episodeNum.ToString().PadLeft(5, '0') + "00011_E";
}

if (cid == null || episode == null)
{
    WriteUsage();
    return;
}

await walker.GetAsync(cid, episode);

static void WriteUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("CWGet [url]");
    Console.WriteLine("CWGet [cid] [chapter]");
}
