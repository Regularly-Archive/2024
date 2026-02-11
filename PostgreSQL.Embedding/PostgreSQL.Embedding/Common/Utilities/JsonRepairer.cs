namespace PostgreSQL.Embedding.Common.Utilities;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

public static class JsonRepairer
{
    public static string Repair(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Empty input");

        var json = ExtractJsonCandidate(input);

        json = NormalizeQuotes(json);
        json = QuoteUnquotedKeys(json);
        json = NormalizeLiterals(json);
        json = RemoveTrailingCommas(json);

        json = BalanceBrackets(json);

        Validate(json);

        return json;
    }

    private static string ExtractJsonCandidate(string text)
    {
        int objStart = text.IndexOf('{');
        int arrStart = text.IndexOf('[');

        int start = objStart == -1
            ? arrStart
            : arrStart == -1
                ? objStart
                : Math.Min(objStart, arrStart);

        if (start == -1)
            throw new FormatException("No JSON object or array found.");

        return text.Substring(start);
    }

    private static string NormalizeQuotes(string s)
    {
        return Regex.Replace(s, @"'([^']*)'", "\"$1\"");
    }

    private static string QuoteUnquotedKeys(string s)
    {
        return Regex.Replace(
            s,
            @"(?<=[\{,])\s*([A-Za-z_][A-Za-z0-9_]*)\s*:",
            m => $"\"{m.Groups[1].Value}\":"
        );
    }

    private static string NormalizeLiterals(string s)
    {
        return Regex.Replace(s, @"\b(True|False|None)\b", m =>
        {
            return m.Value switch
            {
                "True" => "true",
                "False" => "false",
                "None" => "null",
                _ => m.Value
            };
        });
    }

    private static string RemoveTrailingCommas(string s)
    {
        return Regex.Replace(s, @",\s*([\]}])", "$1");
    }

    private static string BalanceBrackets(string s)
    {
        var stack = new Stack<char>();
        var sb = new StringBuilder();

        foreach (var c in s)
        {
            if (c == '{' || c == '[')
                stack.Push(c);

            if (c == '}' || c == ']')
            {
                if (stack.Count == 0)
                    continue;

                var open = stack.Peek();
                if ((open == '{' && c == '}') ||
                    (open == '[' && c == ']'))
                {
                    stack.Pop();
                }
                else
                {
                    continue;
                }
            }

            sb.Append(c);
        }

        while (stack.Count > 0)
        {
            sb.Append(stack.Pop() == '{' ? '}' : ']');
        }

        return sb.ToString();
    }

    private static void Validate(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            throw new FormatException("Repaired JSON is still invalid", ex);
        }
    }
}

