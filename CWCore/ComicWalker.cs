using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace CWCore;

public class ComicWalker
{
    /// <summary>
    /// Wait this many milliseconds between pages to reduce server load.
    /// </summary>
    public int PageWaitMs { get; set; } = 0;
    /// <summary>
    /// If true, ignore existing downloaded files and overwrite as needed.
    /// </summary>
    public bool Overwrite { get; set; } = false;
    /// <summary>
    /// If true, convert each page to a PNG and remove the original downloaded file afterward.
    /// </summary>
    public bool ConvertToPng { get; set; } = true;
    /// <summary>
    /// If true, prefix the chapter names with their index in the chapter list.
    /// Comic Walker removes middle chapters though, so the usefulness of this is questionable.
    /// </summary>
    public bool IndexPrefixChapters { get; set; } = false;
    /// <summary>
    /// An action that will be called as a simple logging mechanism.
    /// Uses Console.WriteLine by default.
    /// </summary>
    public Action<string> LogAction { get; set; } = Console.WriteLine;
    /// <summary>
    /// Path to save downloaded series to.
    /// </summary>
    public string DownloadPath { get; set; } = "manga";

    readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

    public async Task GetAsync(string cid, string episode)
    {
        (string seriesTitle, List<ChapterInfo> chapters) = await GetTitleChapterInfo(cid, episode);

        string seriesPath = Path.Join(DownloadPath, SanitizePath(seriesTitle));
        var di = new DirectoryInfo(seriesPath);
        if (!di.Exists) di.Create();

        LogAction($"Fetching series {seriesTitle}.");
        await DownloadChaptersAsync(seriesPath, chapters);
    }

    /// <summary>
    /// Removes invalid characters from folder paths.
    /// </summary>
    /// <param name="path">Folder path to strip.</param>
    /// <returns>The sanitized path.</returns>
    static string SanitizePath(string path)
    {
        string invalid = new(Path.GetInvalidPathChars());
        Regex rg = new Regex(string.Format("[{0}]", Regex.Escape(invalid)));
        return rg.Replace(path, "");
    }

    async Task<(string Title, List<ChapterInfo> Chapters)> GetTitleChapterInfo(string cid, string episode)
    {
        JsonNode meta;
        string detailUrl = $"https://comic-walker.com/_next/data/2SvEXbIS_EMCYklkC4JQy/detail/{cid}/episodes/{episode}.json?workCode={cid}&episodeCode={episode}&episodeType=first";

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, detailUrl)
        {
            Headers =
            {
                { "Accept", "*/*"},
                {"Accept-Encoding", "gzip, deflate, br, zstd"},
                {"Accept-Language", "en-US,en;q=0.9"},
                {"Referer", detailUrl },
                {"Sec-Ch-Ua", "\"Microsoft Edge\";v=\"123\", \"Not:A-Brand\";v=\"8\", \"Chromium\";v=\"123\""},
                {"Sec-Ch-Ua-Mobile", "?0"},
                {"Sec-Ch-Ua-Platform", "\"Windows\""},
                {"Sec-Fetch-Dest", "empty"},
                {"Sec-Fetch-Mode", "cors"},
                {"Sec-Fetch-Site", "same-origin"},
                {"User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36 Edg/123.0.0.0"},
                { "X-Nextjs-Data", "1"}
            }
        };
        string json;
        try
        {
            using HttpResponseMessage res = await _httpClient.SendAsync(detailRequest);
            res.EnsureSuccessStatusCode();
            json = await res.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            throw new Exception("Couldn't get chapter list.", ex);
        }

        try
        {
            meta = JsonNode.Parse(json)!;
        }
        catch (Exception ex)
        {
            throw new Exception("Couldn't read chapter list response.", ex);
        }

        // Get the title of the series
        string title = meta["pageProps"]["dehydratedState"]["queries"][0]["state"]["data"]["work"]["title"].GetValue<string>();
        // Get the list of chapters and their IDs
        List<ChapterInfo> chapters = [];
        var firstEpisodesNode = (JsonArray)meta["pageProps"]["dehydratedState"]["queries"][0]["state"]["data"]["firstEpisodes"]["result"];
        foreach (JsonNode? item in firstEpisodesNode)
        {
            if (item == null || item["isActive"] == null || !item["isActive"]!.GetValue<bool>())
                continue;
            chapters.Add(new(item["id"].GetValue<string>(), item["title"].GetValue<string>()));
        }
        return (title, chapters);
    }

    async Task DownloadChaptersAsync(string seriesPath, IList<ChapterInfo> chapters)
    {
        for (int c = 0; c < chapters.Count; c++)
        {
            ChapterInfo chapter = chapters[c];
            LogAction($"Fetching chapter {chapter.Title} ({c+1}/{chapters.Count}).");

            // Create the folder for the chapter
            string prefix = IndexPrefixChapters ? (c + 1).ToString().PadLeft(3, '0') + " - " : "";
            string chapterPath = Path.Join(seriesPath, prefix + SanitizePath(chapter.Title));
            var di = new DirectoryInfo(chapterPath);
            if (!di.Exists) di.Create();

            JsonNode meta;
            string url = $"https://comic-walker.com/api/contents/viewer?episodeId={chapter.Id}&imageSizeType=width%3A1284";
            using var pagesRequest = new HttpRequestMessage(HttpMethod.Get, url)
            {
                Headers =
                {
                    { "Accept", "*/*" },
                    { "Accept-Encoding", "gzip, deflate, br" },
                    { "Accept-Language", "en-US,en;q=0.9" },
                    { "Cache-Control", "no-cache" },
                    { "Pragma", "no-cache" },
                    { "Sec-Ch-Ua", "\"Chromium\";v=\"122\", \"Not(A:Brand\";v=\"24\", \"Microsoft Edge\";v=\"122\"" },
                    { "Sec-Ch-Ua-Mobile", "?0" },
                    { "Sec-Ch-Ua-Platform", "\"Windows\"" },
                    { "Sec-Fetch-Dest", "document" },
                    { "Sec-Fetch-Mode", "navigate" },
                    { "Sec-Fetch-Site", "none" },
                    { "Sec-Fetch-User", "?1" },
                    { "Upgrade-Insecure-Requests", "1" },
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36 Edg/122.0.0.0" }
                }
            };

            string json;
            try
            {
                using HttpResponseMessage res = await _httpClient.SendAsync(pagesRequest);
                res.EnsureSuccessStatusCode();
                json = await res.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                throw new Exception("Couldn't get page list.", ex);
            }

            try
            {
                meta = JsonNode.Parse(json)!;
            }
            catch (Exception ex)
            {
                throw new Exception("Couldn't read page list response.", ex);
            }

            // Get the list of pages
            var manuscripts = ((JsonArray)meta["manuscripts"])
                .Select(n => new PageInfo(
                    n["drmMode"].ToString(),
                    n["drmHash"].ToString(),
                    n["drmImageUrl"].ToString(),
                    n["page"].GetValue<int>()))
                .ToArray();

            // Download the pages
            await DownloadPagesAsync(chapterPath, manuscripts);
        }
    }

    private async Task DownloadPagesAsync(string chapterPath, IList<PageInfo> pages)
    {
        for (int p = 0; p < pages.Count; p++)
        {
            PageInfo page = pages[p];
            LogAction($"Fetching page {page.Page} ({p+1}/{pages.Count}).");

            // Get the image URL portion so we can tell what type of image it is
            var regex = @"^(.+)(\.png|\.webp|\.jpg)";
            var match = Regex.Match(page.DrmImageUrl, regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            if (!match.Success)
                throw new InvalidOperationException("Invalid page image url.");

            // Remote URL for the image
            string pageUrl = match.Groups[0].Value;
            // Local path for the saved file
            string pagePath = Path.Join(chapterPath, page.Page.ToString().PadLeft(3, '0') + match.Groups[2]);
            // If desired, we can convert the image to PNG. This will be the file path for it.
            string pagePathConverted = Path.Join(chapterPath, page.Page.ToString().PadLeft(3, '0') + ".png");

            // If we're not set to overwrite, check the final destination path to see if it exists already
            if (!Overwrite && File.Exists(ConvertToPng ? pagePathConverted : pagePath))
            {
                LogAction($"File exists; skipping.");
            }
            else
            {
                // Get the original image
                byte[] bytes = await _httpClient.GetByteArrayAsync(page.DrmImageUrl);

                // Images encoded with their simple XOR DRM need to be decoded
                if (page.DrmMode == "xor" && page.DrmHash != null)
                    Decode(bytes, page.DrmHash);

                // Write the downloaded/decoded image to file
                await File.WriteAllBytesAsync(pagePath, bytes);

                // If desired, we can convert the image to a PNG
                if (ConvertToPng && pagePath != pagePathConverted)
                {
                    using MemoryStream inputStr = new(bytes);
                    using SKManagedStream skInputStr = new(inputStr);
                    using SKBitmap inputBitmap = SKBitmap.Decode(skInputStr);
                    using SKImage inputImage = SKImage.FromBitmap(inputBitmap);
                    using FileStream outStream = File.OpenWrite(pagePathConverted);
                    inputImage.Encode().SaveTo(outStream);
                    try
                    {
                        // Remove the original image
                        File.Delete(pagePath);
                    }
                    catch (Exception ex)
                    {
                        LogAction($"Couldn't delete unconverted page. {ex.Message}");
                    }
                }
            }

            // If desired, wait a bit so we don't make too many requests too quickly
            if (PageWaitMs > 0)
                await Task.Delay(PageWaitMs);
        }
    }

    /// <summary>
    /// Performs a bitwise xor against a byte array using a provided hexadecimal hash string.
    /// </summary>
    /// <param name="bytes">Byte array to apply the operation to in-place.</param>
    /// <param name="hash">Hexadecimal hash key.</param>
    private static void Decode(byte[] bytes, string hash)
    {
        // Convert the hexadecimal string to a byte array
        byte[] hashBytes = Enumerable.Range(0, hash.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hash.Substring(x, 2), 16))
                             .ToArray();
        for (int b = 0; b < bytes.Length; b++)
        {
            // Just a simple bitwise XOR, with the key repeating for the length of the data
            bytes[b] = (byte)(bytes[b] ^ hashBytes[b % hashBytes.Length]);
        }
    }
}

internal record ChapterInfo(string Id, string Title);

internal record PageInfo(string DrmMode, string DrmHash, string DrmImageUrl, int Page);