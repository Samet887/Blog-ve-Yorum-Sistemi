using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace BireyselHesaplar.Utilities
{
    public static class TextFormat
    {
        private static readonly HtmlEncoder SafeHtmlEncoder = HtmlEncoder.Create(UnicodeRanges.All);

        private const string StrongOpen = "__ALLOW_STRONG_OPEN__";
        private const string StrongClose = "__ALLOW_STRONG_CLOSE__";
        private const string EmOpen = "__ALLOW_EM_OPEN__";
        private const string EmClose = "__ALLOW_EM_CLOSE__";
        private const string BrTag = "__ALLOW_BR__";
        private const string UnderlineOpen = "__ALLOW_U_OPEN__";
        private const string UnderlineClose = "__ALLOW_U_CLOSE__";
        private const string UlOpen = "__ALLOW_UL_OPEN__";
        private const string UlClose = "__ALLOW_UL_CLOSE__";
        private const string OlOpen = "__ALLOW_OL_OPEN__";
        private const string OlClose = "__ALLOW_OL_CLOSE__";
        private const string LiOpen = "__ALLOW_LI_OPEN__";
        private const string LiClose = "__ALLOW_LI_CLOSE__";

        public static string ToHtml(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var encoded = SafeHtmlEncoder.Encode(DecodeHtmlFully(input));

            encoded = Regex.Replace(encoded, @"\*\*(.+?)\*\*", "<strong>$1</strong>", RegexOptions.Singleline);
            encoded = Regex.Replace(encoded, @"__(.+?)__", "<strong>$1</strong>", RegexOptions.Singleline);
            encoded = Regex.Replace(encoded, @"\*(.+?)\*", "<em>$1</em>", RegexOptions.Singleline);
            encoded = Regex.Replace(encoded, @"_(.+?)_", "<em>$1</em>", RegexOptions.Singleline);

            encoded = encoded.Replace("\r\n", "\n").Replace("\n", "<br />");
            return encoded;
        }

        public static string SanitizeHtml(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var normalized = DecodeHtmlFully(input)
                .Replace("<strong>", StrongOpen, StringComparison.OrdinalIgnoreCase)
                .Replace("</strong>", StrongClose, StringComparison.OrdinalIgnoreCase)
                .Replace("<b>", StrongOpen, StringComparison.OrdinalIgnoreCase)
                .Replace("</b>", StrongClose, StringComparison.OrdinalIgnoreCase)
                .Replace("<em>", EmOpen, StringComparison.OrdinalIgnoreCase)
                .Replace("</em>", EmClose, StringComparison.OrdinalIgnoreCase)
                .Replace("<i>", EmOpen, StringComparison.OrdinalIgnoreCase)
                .Replace("</i>", EmClose, StringComparison.OrdinalIgnoreCase)
                .Replace("<u>", UnderlineOpen, StringComparison.OrdinalIgnoreCase)
                .Replace("</u>", UnderlineClose, StringComparison.OrdinalIgnoreCase)
                .Replace("<br>", BrTag, StringComparison.OrdinalIgnoreCase)
                .Replace("<br/>", BrTag, StringComparison.OrdinalIgnoreCase)
                .Replace("<br />", BrTag, StringComparison.OrdinalIgnoreCase)
                .Replace("<ul>", UlOpen, StringComparison.OrdinalIgnoreCase)
                .Replace("</ul>", UlClose, StringComparison.OrdinalIgnoreCase)
                .Replace("<ol>", OlOpen, StringComparison.OrdinalIgnoreCase)
                .Replace("</ol>", OlClose, StringComparison.OrdinalIgnoreCase)
                .Replace("<li>", LiOpen, StringComparison.OrdinalIgnoreCase)
                .Replace("</li>", LiClose, StringComparison.OrdinalIgnoreCase);

            normalized = normalized
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", BrTag);

            normalized = Regex.Replace(normalized, "<[^>]+>", string.Empty, RegexOptions.Singleline);

            var encoded = SafeHtmlEncoder.Encode(normalized);

            encoded = encoded
                .Replace(StrongOpen, "<strong>")
                .Replace(StrongClose, "</strong>")
                .Replace(EmOpen, "<em>")
                .Replace(EmClose, "</em>")
                .Replace(UnderlineOpen, "<u>")
                .Replace(UnderlineClose, "</u>")
                .Replace(UlOpen, "<ul>")
                .Replace(UlClose, "</ul>")
                .Replace(OlOpen, "<ol>")
                .Replace(OlClose, "</ol>")
                .Replace(LiOpen, "<li>")
                .Replace(LiClose, "</li>")
                .Replace(BrTag, "<br />");

            return encoded;
        }

        public static string ToExcerptHtml(string? input, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            if (maxCharacters <= 0)
                return string.Empty;

            var sanitized = SanitizeHtml(input);
            var plainText = StripHtml(sanitized);
            if (plainText.Length <= maxCharacters)
                return sanitized;

            var tagRegex = new Regex(@"<(?<close>/)?(?<name>strong|em|u|ul|ol|li|br)\s*/?>", RegexOptions.IgnoreCase);
            var output = new System.Text.StringBuilder();
            var openTags = new Stack<string>();
            var position = 0;
            var consumed = 0;
            var truncated = false;

            void AppendText(string encodedSegment)
            {
                if (truncated || string.IsNullOrEmpty(encodedSegment))
                    return;

                var decoded = WebUtility.HtmlDecode(encodedSegment);
                var remaining = maxCharacters - consumed;
                if (decoded.Length <= remaining)
                {
                    output.Append(encodedSegment);
                    consumed += decoded.Length;
                    return;
                }

                if (remaining > 0)
                {
                    output.Append(SafeHtmlEncoder.Encode(decoded.Substring(0, remaining).TrimEnd()));
                    consumed += remaining;
                }

                truncated = true;
            }

            foreach (Match match in tagRegex.Matches(sanitized))
            {
                AppendText(sanitized.Substring(position, match.Index - position));
                if (truncated)
                    break;

                var name = match.Groups["name"].Value.ToLowerInvariant();
                var isClosing = match.Groups["close"].Success;

                if (name == "br")
                {
                    output.Append("<br />");
                }
                else if (isClosing)
                {
                    output.Append(match.Value);
                    var openTagsList = openTags.ToList();
                    var index = openTagsList.IndexOf(name);
                    if (index >= 0)
                    {
                        openTags.Clear();
                        foreach (var tag in openTagsList.Where((_, i) => i != index).Reverse<string>())
                            openTags.Push(tag);
                    }
                }
                else
                {
                    output.Append($"<{name}>");
                    openTags.Push(name);
                }

                position = match.Index + match.Length;
            }

            if (!truncated)
                AppendText(sanitized.Substring(position));

            if (truncated)
                output.Append("...");

            foreach (var tag in openTags)
                output.Append($"</{tag}>");

            return output.ToString();
        }

        public static string StripHtml(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var normalized = DecodeHtmlFully(input)
                .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
                .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase);

            normalized = Regex.Replace(normalized, "<[^>]+>", string.Empty, RegexOptions.Singleline);
            return normalized;
        }

        private static string DecodeHtmlFully(string input)
        {
            var current = input;
            for (var i = 0; i < 4; i++)
            {
                var decoded = WebUtility.HtmlDecode(current);
                if (decoded == current)
                    break;

                current = decoded;
            }

            return current;
        }
    }
}
