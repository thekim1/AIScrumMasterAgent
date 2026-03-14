using AIScrumMasterAgent.Models;
using System.Text.RegularExpressions;

namespace AIScrumMasterAgent.Services;

public partial class SprintPlanParser : ISprintPlanParser
{
    [GeneratedRegex(@"^#(\d+)\s")]
    private static partial Regex TicketNumberRegex();

    [GeneratedRegex(@"</(div|p|li|br|tr)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockClosingTagRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleNewlinesRegex();

    public List<SprintPlanItem> Parse(string ticketDescription)
    {
        string text = StripHtml(ticketDescription);
        string[] lines = text.Split('\n');

        List<SprintPlanItem> result = [];
        Stack<(string Text, int Indent)> bulletStack = new();
        bool inSection = false;

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd();

            if (!inSection)
            {
                if (line.Contains("Features/Stories and Todos:"))
                    inSection = true;
                continue;
            }

            // Stop at the next top-level section heading (ends with ':' at indent 0, not a bullet)
            if (line.Length > 0
                && !line.StartsWith(' ')
                && !line.StartsWith('\t')
                && !line.TrimStart().StartsWith('*')
                && line.TrimEnd().EndsWith(':'))
            {
                break;
            }

            string trimmed = line.TrimStart();
            if (!trimmed.StartsWith("* "))
                continue;

            int indent = line.Length - trimmed.Length;
            string itemText = trimmed[2..].Trim();

            // Pop stack items that are siblings or at a higher/equal indent
            while (bulletStack.Count > 0 && bulletStack.Peek().Indent >= indent)
                bulletStack.Pop();

            string? parentFeature = bulletStack.Count > 0 ? bulletStack.Peek().Text : null;

            // Check for existing ticket number at the start of the item text
            Match ticketMatch = TicketNumberRegex().Match(itemText);
            if (ticketMatch.Success)
            {
                // Push to stack so it can serve as a parent, but skip from output
                bulletStack.Push((itemText, indent));
                continue;
            }

            bulletStack.Push((itemText, indent));

            ItemKind kind = DetectKind(itemText);
            result.Add(new SprintPlanItem(itemText, parentFeature, null, kind));
        }

        return result;
    }

    internal static string StripHtml(string html)
    {
        // Convert block-level closing tags to newlines to preserve line structure
        string text = BlockClosingTagRegex().Replace(html, "\n");
        text = LineBreakTagRegex().Replace(text, "\n");

        // Remove all remaining HTML tags
        text = HtmlTagRegex().Replace(text, "");

        // Decode common HTML entities
        text = text
            .Replace("&nbsp;", " ")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&amp;", "&")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&auml;", "ä")
            .Replace("&ouml;", "ö")
            .Replace("&aring;", "å")
            .Replace("&Auml;", "Ä")
            .Replace("&Ouml;", "Ö")
            .Replace("&Aring;", "Å");

        // Normalize multiple consecutive blank lines
        text = MultipleNewlinesRegex().Replace(text, "\n\n");

        return text.Trim();
    }

    private static ItemKind DetectKind(string text)
    {
        string lower = text.ToLowerInvariant();

        if (ContainsAny(lower, "boka", "möte", "meeting", "träff"))
            return ItemKind.Meeting;

        if (ContainsAny(lower, "undersök", "utred", "kolla", "verifiera"))
            return ItemKind.Investigation;

        return ItemKind.Implementation;
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(text.Contains);
}
