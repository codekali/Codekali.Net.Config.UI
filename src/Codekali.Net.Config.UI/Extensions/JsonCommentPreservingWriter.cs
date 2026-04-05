using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Codekali.Net.Config.UI.Extensions;

/// <summary>
/// Handles all write-path JSON operations while preserving <c>//</c> and
/// <c>/* */</c> developer comments in appsettings files.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not DOM serialisation?</b><br/>
/// Neither <c>JsonConvert.SerializeObject</c> nor <c>JToken.WriteTo</c> emit
/// <c>JTokenType.Comment</c> nodes — they are silently dropped during
/// serialisation even when loaded with <c>CommentHandling.Load</c>. This is a
/// known Newtonsoft limitation with no built-in workaround.
/// </para>
/// <para>
/// <b>Approach — positional string surgery</b><br/>
/// We parse the raw JSON text with Newtonsoft (retaining comment tokens and
/// full source-position metadata via <c>IJsonLineInfo</c>), locate the target
/// value token by its line/column offset in the original string, then splice
/// only the new value into that exact byte range — leaving every other
/// character, including comments, whitespace, and formatting, completely
/// untouched.
/// </para>
/// <para>
/// For <b>Add</b> operations (new key) we insert a new property line before
/// the closing brace of the parent object, preserving all surrounding content.
/// </para>
/// <para>
/// For <b>Delete</b> operations we remove the entire property line (key +
/// value + trailing comma if present) without touching adjacent lines.
/// </para>
/// </remarks>
internal static class JsonCommentPreservingWriter
{
    private static readonly JsonLoadSettings _loadSettings = new()
    {
        CommentHandling = CommentHandling.Load,
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
        LineInfoHandling = LineInfoHandling.Load,
    };

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the value at <paramref name="keyPath"/> in <paramref name="originalJson"/>,
    /// preserving all comments and formatting in the surrounding text.
    /// Creates intermediate objects and the key itself if they do not exist (Add).
    /// Replaces the existing value in-place if the key already exists (Update).
    /// </summary>
    public static string SetValue(string originalJson, string keyPath, string jsonValue)
    {
        var segments = SplitPath(keyPath);
        var root = Parse(originalJson);

        // Locate the target token if it already exists.
        var existing = GetToken(root, segments);

        if (existing != null)
        {
            var newValueToken = ParseValueToken(jsonValue);
            var newValueText = newValueToken.ToString(Formatting.Indented);
            var lineInfo = (IJsonLineInfo)existing;

            // When the last segment is a numeric index the token lives inside
            // an array — its source line has no ":" separator.
            // Route to the array-item splice path instead of the key-value path.
            bool lastSegmentIsIndex = int.TryParse(segments[^1], out _);
            if (lastSegmentIsIndex)
                return SpliceArrayItemValue(originalJson, lineInfo, existing, newValueText);

            return SpliceValue(originalJson, lineInfo, existing, newValueText);
        }
        else
        {
            // ADD path — only reached for object property keys, not array indices.
            // (Appending to an array goes through AppendToArray, not SetValue.)
            return InsertProperty(originalJson, root, segments, jsonValue);
        }
    }

    /// <summary>
    /// Removes the property at <paramref name="keyPath"/> from
    /// <paramref name="originalJson"/>, preserving all other content.
    /// </summary>
    public static string RemoveKey(string originalJson, string keyPath)
    {
        var segments = SplitPath(keyPath);
        var root = Parse(originalJson);
        var target = GetToken(root, segments);

        if (target is null) return originalJson;  // already gone — no-op

        return DeleteProperty(originalJson, target);
    }

    /// <summary>
    /// Validates that <paramref name="rawJson"/> is well-formed JSON
    /// (comments and trailing commas are accepted).
    /// Returns <c>null</c> if valid; otherwise the parse error message.
    /// </summary>
    public static string? Validate(string rawJson)
    {
        try
        {
            JToken.Parse(rawJson, _loadSettings);
            return null;
        }
        catch (JsonException ex)
        {
            return ex.Message;
        }
    }

    // ── Parsing ───────────────────────────────────────────────────────────

    internal static JObject Parse(string json)
    {
        try
        {
            return JObject.Parse(json, _loadSettings);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Cannot parse JSON for write operation: {ex.Message}", ex);
        }
    }

    // ── UPDATE: splice new value at exact source position ─────────────────

    /// <summary>
    /// Replaces the text span occupied by <paramref name="existing"/> in
    /// <paramref name="source"/> with <paramref name="newValueText"/>.
    /// </summary>
    private static string SpliceValue(
        string source, IJsonLineInfo lineInfo, JToken existing, string newValueText)
    {
        // Newtonsoft IJsonLineInfo.LinePosition points to the character AFTER
        // the last character of the token — not the start. We navigate to the
        // correct line then scan forward past the ':' separator to locate the
        // value start reliably, regardless of the column offset.
        int lineBegin = LineStart(source, lineInfo.LineNumber);

        int colon = source.IndexOf(':', lineBegin);
        if (colon < 0) return source;

        int valueStart = colon + 1;
        while (valueStart < source.Length && source[valueStart] is ' ' or '\t')
            valueStart++;

        int valueEnd = valueStart + MeasureTokenLength(source, valueStart, existing);

        if (existing.Type is JTokenType.Object or JTokenType.Array)
            valueEnd = FindMatchingClose(source, valueStart);

        return source[..valueStart] + newValueText + source[valueEnd..];
    }

    /// <summary>
    /// Replaces the value of a bare array item (no ":" on its line).
    /// Locates the value by its Newtonsoft line number then measures and
    /// replaces its exact character span.
    /// </summary>
    private static string SpliceArrayItemValue(
        string source, IJsonLineInfo lineInfo, JToken existing, string newValueText)
    {
        int lineBegin = LineStart(source, lineInfo.LineNumber);

        // Skip leading whitespace to reach the start of the value.
        int valueStart = lineBegin;
        while (valueStart < source.Length && source[valueStart] is ' ' or '\t')
            valueStart++;

        int valueEnd;
        if (existing.Type is JTokenType.Object or JTokenType.Array)
        {
            valueEnd = existing.Type == JTokenType.Object
                ? FindMatchingClose(source, valueStart)
                : FindMatchingCloseBracket(source, valueStart);
        }
        else
        {
            valueEnd = valueStart + MeasureTokenLength(source, valueStart, existing);
        }

        return source[..valueStart] + newValueText + source[valueEnd..];
    }


    /// <summary>
    /// Returns the 0-based char offset of the first character on
    /// <paramref name="line"/> (1-based) in <paramref name="source"/>.
    /// </summary>
    private static int LineStart(string source, int line)
    {
        int current = 1;
        int i = 0;
        while (i < source.Length && current < line)
        {
            if (source[i] == '\n') current++;
            i++;
        }
        return i;
    }

    // ── ADD: insert a new property line ───────────────────────────────────

    private static string InsertProperty(
        string source, JObject root, string[] segments, string jsonValue)
    {
        // Walk as deep as we can go with existing tokens, then insert.
        JObject current = root;
        string currentSrc = source;
        int depth = 0;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is JObject child)
            {
                current = child;
                depth = i + 1;
            }
            else
            {
                // Need to create the rest of the path as a nested object value.
                // Build the full nested JSON for the remaining segments.
                var remaining = segments[(i + 1)..];
                var nestedJson = BuildNestedJson(remaining, jsonValue);
                var propertyLine = BuildPropertyLine(segments[i], nestedJson, GetIndent(depth + 1, source));

                return InsertIntoObject(currentSrc, current, propertyLine);
            }
        }

        // All intermediate segments existed — just insert the leaf property.
        var leafLine = BuildPropertyLine(segments[^1], jsonValue, GetIndent(depth + 1, source));
        return InsertIntoObject(currentSrc, current, leafLine);
    }

    /// <summary>
    /// Inserts <paramref name="propertyLine"/> as the last property inside the
    /// closing brace of <paramref name="parentObj"/>.
    /// </summary>
    private static string InsertIntoObject(string source, JObject parentObj, string propertyLine)
    {
        // Locate the opening '{' of this object by scanning forward from the
        // start of the line Newtonsoft reports, skipping whitespace and comments.
        // We use line number only (reliable) and ignore the column (unreliable
        // when leading comments or BOM are present).
        var lineInfo = (IJsonLineInfo)parentObj;
        int lineBegin = LineStart(source, lineInfo.LineNumber);
        int openOffset = FindOpenBrace(source, lineBegin);

        if (openOffset < 0) return source; // malformed — bail safely

        int closeOffset = FindMatchingClose(source, openOffset);

        // closeOffset points one past the '}' character, so the '}' itself
        // is at closeOffset - 1.  Walk backward past whitespace/newlines to
        // find the last real content character, then insert after it.
        bool hasExistingProperties = parentObj.Count > 0;

        int insertAt = closeOffset - 2; // -1 for '}', -1 to start before it
        while (insertAt > openOffset && char.IsWhiteSpace(source[insertAt]))
            insertAt--;
        insertAt++; // advance past the last real character

        string separator = hasExistingProperties ? "," : string.Empty;
        string insertion = $"{separator}\n{propertyLine}\n";

        return source[..insertAt] + insertion + source[(closeOffset - 1)..];
    }

    /// <summary>
    /// Scans forward from <paramref name="from"/> to find the opening <c>{</c>
    /// of a JSON object, skipping whitespace, comments, and the property key +
    /// colon that precedes an object value on the same line (e.g.
    /// <c>"PaymentGateway": {</c>).
    /// Returns the offset of the <c>{</c>, or -1 if none is found before a
    /// line that cannot possibly contain the target brace.
    /// </summary>
    private static int FindOpenBrace(string source, int from)
    {
        int i = from;
        while (i < source.Length)
        {
            char c = source[i];

            if (c == '{') return i;

            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '/' && i + 1 < source.Length)
            {
                if (source[i + 1] == '/') { i = SkipLineComment(source, i); continue; }
                if (source[i + 1] == '*') { i = SkipBlockComment(source, i); continue; }
            }

            // A quoted string is either the property key or a string value.
            // Skip it entirely so we can continue scanning for '{'.
            if (c == '"') { i = SkipString(source, i); continue; }

            // A colon separates the key from the value — skip it.
            if (c == ':') { i++; continue; }

            // Any other character (comma, '[', digits, 't', 'f', 'n' for
            // true/false/null, closing braces) means this line does not
            // contain an object-value brace — stop searching.
            break;
        }
        return -1;
    }

    // ── DELETE: remove entire property line ───────────────────────────────

    private static string DeleteProperty(string source, JToken target)
    {
        // The property wraps the value — go up to the JProperty node.
        var prop = target.Parent as JProperty ?? target as JProperty;
        if (prop is null) return source;

        var lineInfo = (IJsonLineInfo)prop;
        int propOffset = LineColToOffset(source, lineInfo.LineNumber, lineInfo.LinePosition);

        // Find the start of the line (include leading whitespace/indent).
        int lineStart = propOffset;
        while (lineStart > 0 && source[lineStart - 1] != '\n')
            lineStart--;

        // Find the end of the line including the newline character.
        int lineEnd = propOffset;
        if (target.Type is JTokenType.Object or JTokenType.Array)
            lineEnd = FindMatchingClose(source, propOffset + prop.Name.Length + 3);
        else
            lineEnd = propOffset;

        // Advance to end of the value on this line.
        while (lineEnd < source.Length && source[lineEnd] != '\n')
            lineEnd++;
        if (lineEnd < source.Length) lineEnd++; // consume the '\n'

        var result = source[..lineStart] + source[lineEnd..];

        // Clean up a trailing comma left on the previous property line.
        result = RemoveTrailingComma(result, lineStart);

        return result;
    }

    // ── String-level helpers ──────────────────────────────────────────────

    /// <summary>
    /// Converts a 1-based Newtonsoft line/column to a 0-based char offset.
    /// </summary>
    private static int LineColToOffset(string source, int line, int col)
    {
        int currentLine = 1;
        int i = 0;
        while (i < source.Length && currentLine < line)
        {
            if (source[i] == '\n') currentLine++;
            i++;
        }
        // col is 1-based; subtract 1 for the 0-based offset.
        return Math.Min(i + col - 1, source.Length);
    }

    /// <summary>
    /// Measures how many characters a scalar token (string, number, bool, null)
    /// occupies in the source at <paramref name="startOffset"/>.
    /// </summary>
    private static int MeasureTokenLength(string source, int startOffset, JToken token)
    {
        return token.Type switch
        {
            JTokenType.String => MeasureStringLength(source, startOffset),
            JTokenType.Boolean => token.Value<bool>() ? 4 : 5,  // "true" / "false"
            JTokenType.Null => 4,                              // "null"
            JTokenType.Integer or JTokenType.Float => MeasureNumberLength(source, startOffset),
            _ => token.ToString().Length
        };
    }

    private static int MeasureStringLength(string source, int start)
    {
        // Walk from the opening '"' to the closing '"', respecting escapes.
        if (start >= source.Length || source[start] != '"') return 0;
        int i = start + 1;
        while (i < source.Length)
        {
            if (source[i] == '\\') { i += 2; continue; }
            if (source[i] == '"') { i++; break; }
            i++;
        }
        return i - start;
    }

    private static int MeasureNumberLength(string source, int start)
    {
        int i = start;
        while (i < source.Length && !char.IsWhiteSpace(source[i])
               && source[i] != ',' && source[i] != '}' && source[i] != ']')
            i++;
        return i - start;
    }

    /// <summary>
    /// Finds the offset of the closing <c>}</c> or <c>]</c> that matches the
    /// opening bracket at <paramref name="openOffset"/>.
    /// Correctly handles nested structures, strings, and comments.
    /// </summary>
    private static int FindMatchingClose(string source, int openOffset)
    {
        char open = source[openOffset];
        char close = open == '{' ? '}' : ']';
        int depth = 0;
        int i = openOffset;

        while (i < source.Length)
        {
            char c = source[i];

            if (c == '"') { i = SkipString(source, i); continue; }
            if (c == '/' && i + 1 < source.Length)
            {
                if (source[i + 1] == '/') { i = SkipLineComment(source, i); continue; }
                if (source[i + 1] == '*') { i = SkipBlockComment(source, i); continue; }
            }

            if (c == open) depth++;
            if (c == close) { depth--; if (depth == 0) return i + 1; }

            i++;
        }

        return source.Length;
    }

    private static int SkipString(string s, int i)
    {
        i++; // skip opening '"'
        while (i < s.Length)
        {
            if (s[i] == '\\') { i += 2; continue; }
            if (s[i] == '"') { return i + 1; }
            i++;
        }
        return i;
    }

    private static int SkipLineComment(string s, int i)
    {
        while (i < s.Length && s[i] != '\n') i++;
        return i;
    }

    private static int SkipBlockComment(string s, int i)
    {
        i += 2; // skip '/*'
        while (i < s.Length - 1)
        {
            if (s[i] == '*' && s[i + 1] == '/') return i + 2;
            i++;
        }
        return s.Length;
    }

    /// <summary>
    /// Removes a trailing comma from the line immediately before
    /// <paramref name="offset"/> if one is present.
    /// </summary>
    private static string RemoveTrailingComma(string source, int offset)
    {
        // Scan backward from offset to find the previous non-whitespace char.
        int i = offset - 1;
        while (i >= 0 && (source[i] == '\r' || source[i] == '\n' || source[i] == ' ' || source[i] == '\t'))
            i--;

        if (i >= 0 && source[i] == ',')
            return source[..i] + source[(i + 1)..];

        return source;
    }

    // ── Indentation and formatting ─────────────────────────────────────────

    /// <summary>
    /// Detects the indentation unit used in <paramref name="source"/> by
    /// examining lines that contain JSON property keys (i.e. contain <c>":"</c>)
    /// and measuring their leading whitespace. Handles both spaces and tabs.
    /// Falls back to 2 spaces if the file has no indented properties.
    /// </summary>
    private static string DetectIndentUnit(string source)
    {
        foreach (var line in source.Split('\n'))
        {
            // Only consider lines that look like JSON properties.
            if (!line.Contains("\":")) continue;

            int len = line.Length;
            if (len == 0) continue;

            if (line[0] == '\t')
            {
                // Tab-indented file — count leading tabs.
                int tabs = 0;
                while (tabs < len && line[tabs] == '\t') tabs++;
                if (tabs > 0) return "\t";
            }
            else if (line[0] == ' ')
            {
                // Space-indented — the leading spaces on the first indented
                // property line IS the indent unit (one level deep).
                int spaces = 0;
                while (spaces < len && line[spaces] == ' ') spaces++;
                if (spaces > 0) return new string(' ', spaces);
            }
        }
        return "  "; // default: 2 spaces
    }

    private static string GetIndent(int depth, string source)
        => string.Concat(Enumerable.Repeat(DetectIndentUnit(source), depth));

    private static string BuildPropertyLine(string key, string jsonValue, string indent)
    {
        // Parse the value to get canonical formatting.  ParseValueToken falls
        // back to treating unparseable input as a plain string — but for nested
        // object literals built by BuildNestedJson the input IS valid JSON, so
        // JToken.Parse will return a JObject and ToString gives us proper indentation.
        JToken parsed;
        try { parsed = JToken.Parse(jsonValue); }
        catch { parsed = new Newtonsoft.Json.Linq.JValue(jsonValue); }

        var value = parsed.ToString(Formatting.Indented);
        var indentedVal = IndentValue(value, indent);
        return $"{indent}\"{EscapeKey(key)}\": {indentedVal}";
    }

    private static string IndentValue(string value, string indent)
    {
        if (!value.Contains('\n')) return value;
        var lines = value.Split('\n');
        return string.Join('\n', lines.Select((l, i) => i == 0 ? l : indent + l));
    }

    private static string BuildNestedJson(string[] remainingSegments, string leafValue)
    {
        if (remainingSegments.Length == 0)
        {
            // Ensure the leaf is a valid JSON fragment.
            // If the value is not already parseable JSON, treat it as a plain
            // string and quote it so the resulting object literal is valid.
            try { Newtonsoft.Json.Linq.JToken.Parse(leafValue); return leafValue; }
            catch { return Newtonsoft.Json.JsonConvert.SerializeObject(leafValue); }
        }

        var inner = BuildNestedJson(remainingSegments[1..], leafValue);
        return $"{{\n  \"{EscapeKey(remainingSegments[0])}\": {inner}\n}}";
    }

    private static string EscapeKey(string key)
        => key.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ── Token helpers ─────────────────────────────────────────────────────

    private static JToken? GetToken(JObject root, string[] segments)
    {
        JToken? current = root;
        foreach (var seg in segments)
        {
            if (current is JObject obj)
            {
                current = obj[seg];
            }
            else if (current is JArray arr && int.TryParse(seg, out var idx))
            {
                if (idx < 0 || idx >= arr.Count) return null;
                current = arr[idx];
            }
            else return null;

            if (current is null) return null;
        }
        return current;
    }

    private static JToken ParseValueToken(string jsonValue)
    {
        if (string.IsNullOrEmpty(jsonValue)) return JValue.CreateNull();
        try { return JToken.Parse(jsonValue); }
        catch { return new JValue(jsonValue); }
    }

    private static string[] SplitPath(string path)
        => path.Split(':', StringSplitOptions.RemoveEmptyEntries);
    // ── Array operations ──────────────────────────────────────────────────

    /// <summary>
    /// Appends <paramref name="jsonValue"/> as a new item at the end of the
    /// JSON array at <paramref name="keyPath"/> in <paramref name="originalJson"/>.
    /// If the key does not exist, a new single-item array is created.
    /// If the key exists but is not an array, returns a failure message.
    /// </summary>
    public static (string result, string? error) AppendToArray(
        string originalJson, string keyPath, string jsonValue)
    {
        var root = Parse(originalJson);
        var segments = SplitPath(keyPath);
        var existing = GetNestedToken(root, segments);

        JToken newItem;
        try { newItem = JToken.Parse(jsonValue); }
        catch { newItem = new JValue(jsonValue); }

        if (existing is null)
        {
            // Key does not exist — create a new array with one item.
            var arr = new JArray(newItem);
            SetNestedToken(root, segments, arr);
            return (SerializePreserving(originalJson, root, keyPath, arr.ToString(Formatting.Indented)), null);
        }

        if (existing is not JArray jArray)
            return (originalJson, $"'{keyPath}' exists but is not an array.");

        // Append in place on the JToken tree — but we can't serialize back
        // with comments, so we do string surgery: find the closing ']' and
        // insert before it.
        jArray.Add(newItem);

        // Use string surgery to insert the new item before the closing ']'.
        var itemJson = newItem.ToString(Formatting.None);
        var indentUnit = DetectIndentUnit(originalJson);
        var depth = segments.Length + 1;          // array items are one deeper
        var itemIndent = string.Concat(Enumerable.Repeat(indentUnit, depth));
        var formatted = $"{itemIndent}{itemJson}";

        return (InsertArrayItem(originalJson, keyPath, formatted), null);
    }

    /// <summary>
    /// Removes the array item at zero-based <paramref name="index"/> from the
    /// array at <paramref name="keyPath"/>. Remaining items are not re-keyed in
    /// the source (JSON arrays are positional, not keyed).
    /// </summary>
    public static (string result, string? error) RemoveFromArray(
        string originalJson, string keyPath, int index)
    {
        var root = Parse(originalJson);
        var segments = SplitPath(keyPath);
        var existing = GetNestedToken(root, segments);

        if (existing is null)
            return (originalJson, $"Key '{keyPath}' not found.");

        if (existing is not JArray jArray)
            return (originalJson, $"'{keyPath}' is not an array.");

        if (index < 0 || index >= jArray.Count)
            return (originalJson, $"Index {index} is out of range (array has {jArray.Count} items).");

        // String surgery: find the array in the source and remove the nth item line.
        return (RemoveArrayItemAtIndex(originalJson, keyPath, index, jArray.Count), null);
    }

    // ── Array string-surgery helpers ──────────────────────────────────────

    /// <summary>
    /// Inserts a formatted item line before the closing <c>]</c> of the array
    /// identified by <paramref name="keyPath"/> in <paramref name="source"/>.
    /// </summary>
    private static string InsertArrayItem(string source, string keyPath, string formattedItem)
    {
        // Locate the array's '[' using the same key-scanning approach as FindOpenBrace.
        var segments = SplitPath(keyPath);
        var root = Parse(source);
        var arrToken = GetNestedToken(root, segments) as JArray;
        if (arrToken is null) return source;

        var lineInfo = (IJsonLineInfo)arrToken;
        int lineBegin = LineStart(source, lineInfo.LineNumber);
        int openBracket = FindOpenBracket(source, lineBegin);
        if (openBracket < 0) return source;

        int closeBracket = FindMatchingCloseBracket(source, openBracket);

        // Walk backward from closing ']' to find the last non-whitespace char.
        int insertAt = closeBracket - 2; // closeBracket points past ']'
        while (insertAt > openBracket && char.IsWhiteSpace(source[insertAt]))
            insertAt--;
        insertAt++;

        bool hasItems = arrToken.Count > 0;  // count was already incremented by Add
        // The original array (before Add) had Count-1 items.
        bool hadItemsBefore = arrToken.Count > 1;
        string separator = hadItemsBefore ? "," : string.Empty;
        string insertion = $"{separator}\n{formattedItem}\n";

        return source[..insertAt] + insertion + source[(closeBracket - 1)..];
    }

    /// <summary>
    /// Removes the item at <paramref name="index"/> from the array at
    /// <paramref name="keyPath"/> by locating it in the source text by
    /// counting item-start positions inside the array's brackets.
    /// </summary>
    private static string RemoveArrayItemAtIndex(
        string source, string keyPath, int index, int totalCount)
    {
        var segments = SplitPath(keyPath);
        var root = Parse(source);
        var arrToken = GetNestedToken(root, segments) as JArray;
        if (arrToken is null) return source;

        var lineInfo = (IJsonLineInfo)arrToken;
        int lineBegin = LineStart(source, lineInfo.LineNumber);
        int openBracket = FindOpenBracket(source, lineBegin);
        if (openBracket < 0) return source;

        int closeBracket = FindMatchingCloseBracket(source, openBracket);

        // Walk through the array content and find the start/end of the nth item.
        var (itemStart, itemEnd) = FindArrayItemSpan(source, openBracket, closeBracket, index, totalCount);
        if (itemStart < 0) return source;

        var result = source[..itemStart] + source[itemEnd..];

        // Clean up orphaned trailing comma if we removed a non-last item.
        if (index < totalCount - 1)
        {
            // Nothing to do — we already removed the item and its trailing comma
            // is included in itemEnd.
        }
        else
        {
            // Removed the last item — clean up trailing comma on the new last item.
            result = RemoveTrailingComma(result, itemStart);
        }

        return result;
    }

    /// <summary>
    /// Returns the (start, end) character span of the nth item inside an array,
    /// including any leading/trailing whitespace on its line(s) and a following
    /// comma if present. The span is suitable for direct removal.
    /// </summary>
    private static (int start, int end) FindArrayItemSpan(
        string source, int openBracket, int closeBracket, int targetIndex, int totalCount)
    {
        int i = openBracket + 1; // skip '['
        int itemNo = 0;

        while (i < closeBracket - 1)
        {
            // Skip whitespace between items.
            while (i < source.Length && char.IsWhiteSpace(source[i])) i++;

            if (i >= closeBracket - 1) break;

            // Skip comments.
            if (source[i] == '/' && i + 1 < source.Length)
            {
                if (source[i + 1] == '/') { i = SkipLineComment(source, i); continue; }
                if (source[i + 1] == '*') { i = SkipBlockComment(source, i); continue; }
            }

            // Mark start of item — include the line's leading whitespace.
            int lineStart = i;
            while (lineStart > 0 && source[lineStart - 1] != '\n') lineStart--;

            // Measure the item extent.
            int itemEnd;
            if (source[i] == '{' || source[i] == '[')
            {
                itemEnd = source[i] == '{' ? FindMatchingClose(source, i) : FindMatchingCloseBracket(source, i);
            }
            else if (source[i] == '"')
            {
                itemEnd = SkipString(source, i);
            }
            else
            {
                itemEnd = i;
                while (itemEnd < source.Length
                    && source[itemEnd] != ','
                    && source[itemEnd] != ']'
                    && source[itemEnd] != '\n')
                    itemEnd++;
            }

            // Include trailing comma if present.
            int afterItem = itemEnd;
            while (afterItem < source.Length && source[afterItem] is ' ' or '	') afterItem++;
            if (afterItem < source.Length && source[afterItem] == ',') afterItem++;

            // Include the trailing newline.
            if (afterItem < source.Length && source[afterItem] == '\n') afterItem++;

            if (itemNo == targetIndex)
                return (lineStart, afterItem);

            i = afterItem;
            itemNo++;
        }

        return (-1, -1);
    }

    // ── Bracket scanning helpers ──────────────────────────────────────────

    private static int FindOpenBracket(string source, int from)
    {
        int i = from;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '[') return i;
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < source.Length)
            {
                if (source[i + 1] == '/') { i = SkipLineComment(source, i); continue; }
                if (source[i + 1] == '*') { i = SkipBlockComment(source, i); continue; }
            }
            // Skip key name and colon before the '['.
            if (c == '"') { i = SkipString(source, i); continue; }
            if (c == ':') { i++; continue; }
            break;
        }
        return -1;
    }

    private static int FindMatchingCloseBracket(string source, int openOffset)
    {
        int depth = 0;
        int i = openOffset;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '"') { i = SkipString(source, i); continue; }
            if (c == '/' && i + 1 < source.Length)
            {
                if (source[i + 1] == '/') { i = SkipLineComment(source, i); continue; }
                if (source[i + 1] == '*') { i = SkipBlockComment(source, i); continue; }
            }
            if (c == '[') depth++;
            if (c == ']') { depth--; if (depth == 0) return i + 1; }
            i++;
        }
        return source.Length;
    }

    // ── JObject/JArray tree navigation (for array write path) ────────────

    private static JToken? GetNestedToken(JObject root, string[] segments)
    {
        JToken? current = root;
        foreach (var seg in segments)
        {
            if (current is JObject obj) { current = obj[seg]; }
            else if (current is JArray arr && int.TryParse(seg, out var idx)) { current = arr[idx]; }
            else return null;
            if (current is null) return null;
        }
        return current;
    }

    private static void SetNestedToken(JObject root, string[] segments, JToken value)
    {
        JObject current = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JObject child)
            {
                child = new JObject();
                current[segments[i]] = child;
            }
            current = child;
        }
        current[segments[^1]] = value;
    }

    private static string SerializePreserving(
        string originalJson, JObject mutatedRoot, string keyPath, string valueJson)
    {
        // For the case where the key didn't exist, we delegate to SetValue
        // which handles insertion with comment preservation.
        return SetValue(originalJson, keyPath, valueJson);
    }

}