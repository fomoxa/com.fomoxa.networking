using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Fomoxa.Unity.Editor
{
    internal readonly struct JsonNumber
    {
        public JsonNumber(string text)
        {
            Text = text;
        }

        public string Text { get; }
    }

    internal static class FomoxaJson
    {
        public static object Parse(string text)
        {
            int index = 0;
            object value = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);
            if (index != text.Length)
            {
                throw new FormatException($"unexpected content at position {index}");
            }

            return value;
        }

        public static string Write(object value)
        {
            var text = new StringBuilder();
            WriteValue(value, text, 0);
            return text.Append('\n').ToString();
        }

        public static Dictionary<string, object> AsObject(object value) =>
            value as Dictionary<string, object> ?? throw new FormatException("expected a JSON object");

        public static List<object> AsArray(object value) =>
            value as List<object> ?? throw new FormatException("expected a JSON array");

        public static string AsString(object value) =>
            value as string ?? throw new FormatException("expected a JSON string");

        private static object ParseValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                throw new FormatException("unexpected end of JSON");
            }

            switch (text[index])
            {
                case '{':
                    return ParseObject(text, ref index);
                case '[':
                    return ParseArray(text, ref index);
                case '"':
                    return ParseString(text, ref index);
                case 't':
                    Expect(text, ref index, "true");
                    return true;
                case 'f':
                    Expect(text, ref index, "false");
                    return false;
                case 'n':
                    Expect(text, ref index, "null");
                    return null;
                default:
                    return ParseNumber(text, ref index);
            }
        }

        private static Dictionary<string, object> ParseObject(string text, ref int index)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            index++;
            SkipWhitespace(text, ref index);
            if (Peek(text, index) == '}')
            {
                index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                string key = ParseString(text, ref index);
                SkipWhitespace(text, ref index);
                ExpectChar(text, ref index, ':');
                result[key] = ParseValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (Peek(text, index) == ',')
                {
                    index++;
                    continue;
                }

                ExpectChar(text, ref index, '}');
                return result;
            }
        }

        private static List<object> ParseArray(string text, ref int index)
        {
            var result = new List<object>();
            index++;
            SkipWhitespace(text, ref index);
            if (Peek(text, index) == ']')
            {
                index++;
                return result;
            }

            while (true)
            {
                result.Add(ParseValue(text, ref index));
                SkipWhitespace(text, ref index);
                if (Peek(text, index) == ',')
                {
                    index++;
                    continue;
                }

                ExpectChar(text, ref index, ']');
                return result;
            }
        }

        private static string ParseString(string text, ref int index)
        {
            ExpectChar(text, ref index, '"');
            var result = new StringBuilder();
            while (true)
            {
                if (index >= text.Length)
                {
                    throw new FormatException("unterminated JSON string");
                }

                char next = text[index++];
                if (next == '"')
                {
                    return result.ToString();
                }

                if (next != '\\')
                {
                    result.Append(next);
                    continue;
                }

                if (index >= text.Length)
                {
                    throw new FormatException("unterminated JSON escape");
                }

                char escape = text[index++];
                switch (escape)
                {
                    case '"':
                    case '\\':
                    case '/':
                        result.Append(escape);
                        break;
                    case 'b':
                        result.Append('\b');
                        break;
                    case 'f':
                        result.Append('\f');
                        break;
                    case 'n':
                        result.Append('\n');
                        break;
                    case 'r':
                        result.Append('\r');
                        break;
                    case 't':
                        result.Append('\t');
                        break;
                    case 'u' when index + 4 <= text.Length:
                        result.Append((char)ushort.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        index += 4;
                        break;
                    default:
                        throw new FormatException($"unknown JSON escape '\\{escape}'");
                }
            }
        }

        private static JsonNumber ParseNumber(string text, ref int index)
        {
            int start = index;
            while (index < text.Length && (char.IsDigit(text[index]) || "+-.eE".IndexOf(text[index]) >= 0))
            {
                index++;
            }

            if (index == start)
            {
                throw new FormatException($"unexpected character '{text[index]}' at position {index}");
            }

            return new JsonNumber(text.Substring(start, index - start));
        }

        private static void WriteValue(object value, StringBuilder text, int depth)
        {
            switch (value)
            {
                case null:
                    text.Append("null");
                    break;
                case string literal:
                    WriteString(literal, text);
                    break;
                case bool flag:
                    text.Append(flag ? "true" : "false");
                    break;
                case JsonNumber number:
                    text.Append(number.Text);
                    break;
                case Dictionary<string, object> members:
                    WriteObject(members, text, depth);
                    break;
                case List<object> items:
                    WriteArray(items, text, depth);
                    break;
                default:
                    throw new FormatException($"cannot write a JSON value of type {value.GetType()}");
            }
        }

        private static void WriteObject(Dictionary<string, object> members, StringBuilder text, int depth)
        {
            if (members.Count == 0)
            {
                text.Append("{}");
                return;
            }

            text.Append("{\n");
            bool first = true;
            foreach (KeyValuePair<string, object> member in members)
            {
                if (!first)
                {
                    text.Append(",\n");
                }

                first = false;
                text.Append(' ', (depth + 1) * 2);
                WriteString(member.Key, text);
                text.Append(": ");
                WriteValue(member.Value, text, depth + 1);
            }

            text.Append('\n').Append(' ', depth * 2).Append('}');
        }

        private static void WriteArray(List<object> items, StringBuilder text, int depth)
        {
            if (items.Count == 0)
            {
                text.Append("[]");
                return;
            }

            text.Append("[\n");
            for (int index = 0; index < items.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(",\n");
                }

                text.Append(' ', (depth + 1) * 2);
                WriteValue(items[index], text, depth + 1);
            }

            text.Append('\n').Append(' ', depth * 2).Append(']');
        }

        private static void WriteString(string value, StringBuilder text)
        {
            text.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"':
                        text.Append("\\\"");
                        break;
                    case '\\':
                        text.Append("\\\\");
                        break;
                    case '\n':
                        text.Append("\\n");
                        break;
                    case '\r':
                        text.Append("\\r");
                        break;
                    case '\t':
                        text.Append("\\t");
                        break;
                    default:
                        if (character < ' ')
                        {
                            text.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            text.Append(character);
                        }

                        break;
                }
            }

            text.Append('"');
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        private static char Peek(string text, int index) => index < text.Length ? text[index] : '\0';

        private static void ExpectChar(string text, ref int index, char expected)
        {
            if (Peek(text, index) != expected)
            {
                throw new FormatException($"expected '{expected}' at position {index}");
            }

            index++;
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (index + literal.Length > text.Length || string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw new FormatException($"expected '{literal}' at position {index}");
            }

            index += literal.Length;
        }
    }
}
